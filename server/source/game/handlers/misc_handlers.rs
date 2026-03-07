//! Miscellaneous packet handlers: swap, skills, stat updates, UI packets,
//! pet commands, travel, citizenship, training, voting, pareja, CvC accept.
//! Extracted from mod.rs to reduce file size.

use tracing::info;
use crate::net::ConnectionId;
use crate::game::types::{GameState, UserState, SendTarget, InventorySlot, MAX_INVENTORY_SLOTS, MAX_SPELL_SLOTS, privilege_level};
use crate::game::world;
use crate::protocol::{font_index, fields::read_field, binary_packets};
use crate::data::objects::{ObjData, ObjType};
use crate::db::guilds;
use super::common::*;
use super::{
    warp_user, revive_user, send_inventory_slot, send_full_inventory,
    check_user_level, send_full_spells, build_anm_packet,
    make_user_visible,
    remove_pet_from_owner, handle_drop_item, do_cast_spell, do_ocultarse,
};

// =====================================================================
// Missing VB6 handlers — Phase 10 parity
// =====================================================================

/// SWAP — Swap two inventory slots.
pub(super) async fn handle_swap(state: &mut GameState, conn_id: ConnectionId, data: &str) {
    let payload = strip_opcode(data, 4);
    let slot1: usize = read_field(1, payload, ',').parse().unwrap_or(0);
    let slot2: usize = read_field(2, payload, ',').parse().unwrap_or(0);

    if let Some(user) = state.users.get(&conn_id) {
        if user.comerciando || user.trading {
            state.send_msg_id(conn_id, 153, "").await;
            return;
        }
    }

    if let Some(user) = state.users.get_mut(&conn_id) {
        if slot1 == 0 || slot2 == 0 || slot1 > MAX_INVENTORY_SLOTS || slot2 > MAX_INVENTORY_SLOTS || slot1 == slot2 {
            return;
        }
        let s1 = slot1 - 1;
        let s2 = slot2 - 1;
        user.inventory.swap(s1, s2);

        // Update equipped slot references
        if user.equip.weapon == slot1 { user.equip.weapon = slot2; }
        else if user.equip.weapon == slot2 { user.equip.weapon = slot1; }
        if user.equip.armor == slot1 { user.equip.armor = slot2; }
        else if user.equip.armor == slot2 { user.equip.armor = slot1; }
        if user.equip.shield == slot1 { user.equip.shield = slot2; }
        else if user.equip.shield == slot2 { user.equip.shield = slot1; }
        if user.equip.helmet == slot1 { user.equip.helmet = slot2; }
        else if user.equip.helmet == slot2 { user.equip.helmet = slot1; }
        if user.equip.municion == slot1 { user.equip.municion = slot2; }
        else if user.equip.municion == slot2 { user.equip.municion = slot1; }
        if user.equip.ring == slot1 { user.equip.ring = slot2; }
        else if user.equip.ring == slot2 { user.equip.ring = slot1; }
    }
    send_full_inventory(state, conn_id).await;
}

/// SKSE — Distribute skill points.
pub(super) async fn handle_skse(state: &mut GameState, conn_id: ConnectionId, data: &str) {
    let payload = strip_opcode(data, 4);

    // Parse 22 comma-delimited skill increments
    let mut increments = [0i32; 22];
    let mut total = 0i32;
    for i in 0..22 {
        let val: i32 = read_field(i + 1, payload, ',').parse().unwrap_or(0);
        if val < 0 {
            state.send_console(conn_id, "Valor invalido.", font_index::INFO).await;
            return;
        }
        increments[i] = val;
        total += val;
    }

    if let Some(user) = state.users.get(&conn_id) {
        if total > user.skill_pts_libres || total <= 0 {
            state.send_console(conn_id, "Puntos de skill invalidos.", font_index::INFO).await;
            return;
        }
    } else {
        return;
    }

    if let Some(user) = state.users.get_mut(&conn_id) {
        for i in 0..22 {
            user.skills[i] = (user.skills[i] + increments[i]).min(100);
        }
        user.skill_pts_libres -= total;
    }

    state.send_console(conn_id, &format!("Has distribuido {} puntos de skill.", total), font_index::INFO).await;
}

/// INFS — Spell info. VB6: TCP_HandleData1.bas:2747-2764
/// Sends ||281 through ||287 packets (message-based, client reads from Textos.tsao)
pub(super) async fn handle_infs(state: &mut GameState, conn_id: ConnectionId, data: &str) {
    let payload = strip_opcode(data, 4);
    let slot: usize = payload.trim().parse().unwrap_or(0);

    let spell_idx = state.users.get(&conn_id)
        .and_then(|u| if slot >= 1 && slot <= MAX_SPELL_SLOTS { Some(u.spells[slot - 1]) } else { None })
        .unwrap_or(0);

    if spell_idx <= 0 || spell_idx as usize > state.game_data.spells.len() {
        state.send_msg_id(conn_id, 288, "").await; // VB6: error message
        return;
    }

    let spell = &state.game_data.spells[spell_idx as usize - 1];
    let nombre = spell.nombre.clone();
    let desc = spell.desc.clone();
    let min_skill = spell.min_skill;
    let mana_req = spell.mana_requerido;
    let sta_req = spell.sta_requerido;

    // VB6 sends 7 packets: ||281 (header), ||282@name, ||283@desc, ||284@skill, ||285@mana, ||286@sta, ||287 (footer)
    state.send_msg_id(conn_id, 281, "").await;
    state.send_msg_id(conn_id, 282, &format!("{}", nombre)).await;
    state.send_msg_id(conn_id, 283, &format!("{}", desc)).await;
    state.send_msg_id(conn_id, 284, &format!("{}", min_skill)).await;
    state.send_msg_id(conn_id, 285, &format!("{}", mana_req)).await;
    state.send_msg_id(conn_id, 286, &format!("{}", sta_req)).await;
    state.send_msg_id(conn_id, 287, "").await;
}

/// DESPHE — Move/swap spell positions. VB6: DesplazarHechizo(userindex, Dire, CualHechizo)
/// Format: DESPHE<direction>,<slot> where direction=1(up) or 2(down), slot=1-based
pub(super) async fn handle_desphe(state: &mut GameState, conn_id: ConnectionId, data: &str) {
    let payload = strip_opcode(data, 6);
    let direction: i32 = read_field(1, payload, ',').parse().unwrap_or(0);
    let slot: usize = read_field(2, payload, ',').parse().unwrap_or(0);

    if !(direction >= 1 && direction <= 2) { return; }
    if slot < 1 || slot > MAX_SPELL_SLOTS { return; }

    if direction == 1 {
        // Move UP: swap slot with slot-1
        if slot == 1 {
            state.send_msg_id(conn_id, 37, "").await; // VB6: can't move first slot up
            return;
        }
        if let Some(user) = state.users.get_mut(&conn_id) {
            let s = slot - 1; // 0-based
            let temp = user.spells[s];
            user.spells[s] = user.spells[s - 1];
            user.spells[s - 1] = temp;
        }
    } else {
        // Move DOWN: swap slot with slot+1
        if slot == MAX_SPELL_SLOTS {
            state.send_msg_id(conn_id, 37, "").await; // VB6: can't move last slot down
            return;
        }
        if let Some(user) = state.users.get_mut(&conn_id) {
            let s = slot - 1; // 0-based
            let temp = user.spells[s];
            user.spells[s] = user.spells[s + 1];
            user.spells[s + 1] = temp;
        }
    }

    // VB6 sends UpdateUserHechizos for both affected slots; we send all for simplicity
    send_full_spells(state, conn_id).await;
}

/// DAMINF — Player stats form (send detailed info about a target player).
pub(super) async fn handle_daminf(state: &mut GameState, conn_id: ConnectionId, data: &str) {
    let payload = strip_opcode(data, 6);
    let target_name = payload.trim();

    // Find target user by name
    let target_conn = state.online_names.get(&target_name.to_uppercase()).copied();
    if target_conn.is_none() {
        state.send_console(conn_id, "Usuario no encontrado.", font_index::INFO).await;
        return;
    }
    let target_conn = target_conn.unwrap();

    let info = if let Some(target) = state.users.get(&target_conn) {
        format!("{},{},{},{},{},{},{},{},{},{},{},{},{},{},{}",
            target.char_name,
            target.race,
            target.class,
            target.level,
            target.gold,
            target.reputation,
            target.criminales_matados,
            target.ciudadanos_matados,
            if target.criminal { "Criminal" } else { "Ciudadano" },
            if target.armada_real { "Armada Real" } else if target.fuerzas_caos { "Fuerzas del Caos" } else { "Ninguna" },
            target.guild_index,
            0,
            target.max_hp,
            target.max_mana,
            target.max_sta,
        )
    } else {
        return;
    };

    let pkt = binary_packets::write_full_char_info(&info);
    state.send_bytes(conn_id, &pkt).await;
}

/// FEST — Send mini statistics.
pub(super) async fn handle_fest(state: &mut GameState, conn_id: ConnectionId) {
    let info = if let Some(user) = state.users.get(&conn_id) {
        format!("{},{},{},{},{},{},{},{}",
            user.criminales_matados,
            user.ciudadanos_matados,
            user.level,
            user.class,
            if user.criminal { "Criminal" } else { "Ciudadano" },
            0,
            user.guild_index,
            user.reputation,
        )
    } else {
        return;
    };
    let pkt = binary_packets::write_fest_data(&info);
    state.send_bytes(conn_id, &pkt).await;
}

/// CABEZI — Change head/hairstyle (barber). Costs 500 gold.
pub(super) async fn handle_cabezi(state: &mut GameState, conn_id: ConnectionId, data: &str) {
    let payload = strip_opcode(data, 6);
    let new_head: i32 = payload.trim().parse().unwrap_or(0);

    if new_head <= 0 {
        return;
    }

    let cost: i64 = 500;

    if let Some(user) = state.users.get(&conn_id) {
        if user.gold < cost {
            state.send_console(conn_id, "No tienes suficiente oro.", font_index::INFO).await;
            return;
        }
    } else {
        return;
    }

    let (map, x, y, old_ci) = {
        let user = state.users.get_mut(&conn_id).unwrap();
        user.gold -= cost;
        user.head = new_head;
        (user.pos_map, user.pos_x, user.pos_y, user.char_index.0)
    };

    // Update appearance for all nearby
    send_stats_gold(state, conn_id).await;
    let u = state.users.get(&conn_id).unwrap();
    let cp = binary_packets::write_character_change(
        old_ci as i16, u.body as i16, new_head as i16, u.heading as u8,
        u.weapon_anim as i16, u.shield_anim as i16, u.casco_anim as i16, 0, 0,
    );
    state.send_data_bytes(SendTarget::ToArea { map, x, y }, &cp).await;
    state.send_console(conn_id, "Cabeza cambiada.", font_index::INFO).await;
}

/// TR — Drop item via mouse click (at current position).
pub(super) async fn handle_mouse_drop(state: &mut GameState, conn_id: ConnectionId, data: &str) {
    let payload = strip_opcode(data, 2);
    let slot: usize = read_field(1, payload, ',').parse().unwrap_or(0);
    let amount: i32 = read_field(2, payload, ',').parse().unwrap_or(1);

    if slot < 1 || slot > MAX_INVENTORY_SLOTS || amount <= 0 {
        return;
    }

    // Delegate to the same drop logic as TI
    let drop_data = format!("TI{},{}", slot, amount);
    handle_drop_item(state, conn_id, &drop_data).await;
}

/// BOF — Level bonus selection.
pub(super) async fn handle_bof(state: &mut GameState, conn_id: ConnectionId, data: &str) {
    let payload = strip_opcode(data, 3);
    let selection: i32 = payload.trim().parse().unwrap_or(0);

    if selection < 1 || selection > 3 {
        return;
    }

    // Level bonuses at 53, 56, 60 — give HP bonus
    // VB6: different amounts per selection (10, 15, 20 HP bonus)
    let hp_bonus = match selection {
        1 => 10,
        2 => 15,
        3 => 20,
        _ => 0,
    };

    if let Some(user) = state.users.get_mut(&conn_id) {
        user.max_hp += hp_bonus;
        user.min_hp = user.min_hp.min(user.max_hp);
    }

    send_stats_hp(state, conn_id).await;
    state.send_console(conn_id, &format!("Has ganado {} puntos de vida extra!", hp_bonus), font_index::INFO).await;
}

/// UK — Use Skill. VB6: TCP_HandleData1.bas Case "UK".
/// Robar/Magia/Domar → sends T01<skillID> to client (opens skill tree UI).
/// Skill use handler (UK packet). do_ocultarse handles all its own checks.
pub(super) async fn handle_uk(state: &mut GameState, conn_id: ConnectionId, data: &str) {
    let dead = match state.users.get(&conn_id) {
        Some(u) if u.logged => u.dead,
        _ => return,
    };

    if dead {
        state.send_msg_id(conn_id, 3, "").await;
        return;
    }

    let payload = strip_opcode(data, 2); // "UK" is 2 chars
    let skill_num: i32 = payload.trim().parse().unwrap_or(0);

    match skill_num {
        3 => { // Robar
            let pkt = binary_packets::write_work_mode(skill_num as u8);
            state.send_bytes(conn_id, &pkt).await;
        }
        2 => { // Magia
            let pkt = binary_packets::write_work_mode(skill_num as u8);
            state.send_bytes(conn_id, &pkt).await;
        }
        18 => { // Domar
            let pkt = binary_packets::write_work_mode(skill_num as u8);
            state.send_bytes(conn_id, &pkt).await;
        }
        8 => { // Ocultarse — all checks handled inside do_ocultarse (TSAO mechanic)
            do_ocultarse(state, conn_id).await;
        }
        _ => {} // Unknown skill, ignore
    }
}

/// ENTR — Train creature from trainer NPC.
pub(super) async fn handle_entr(state: &mut GameState, conn_id: ConnectionId, data: &str) {
    let payload = strip_opcode(data, 4);
    let creature_slot: i32 = payload.trim().parse().unwrap_or(0);

    if creature_slot <= 0 {
        return;
    }

    let (target_npc, map, x, y, nro_mascotas, gold) = match state.users.get(&conn_id) {
        Some(u) => (u.target_npc, u.pos_map, u.pos_x, u.pos_y, u.nro_mascotas, u.gold),
        None => return,
    };

    if target_npc == 0 {
        state.send_console(conn_id, "No estas interactuando con un NPC.", font_index::INFO).await;
        return;
    }

    if nro_mascotas >= 3 {
        state.send_console(conn_id, "Ya tienes el maximo de mascotas.", font_index::INFO).await;
        return;
    }

    // Simple: spawn a pet NPC near the player
    // In VB6 this reads from the trainer's creature list — for now, just acknowledge
    state.send_console(conn_id, "Criatura entrenada.", font_index::INFO).await;
}


/// ACTUALIZAR — Position re-sync.
pub(super) async fn handle_actualizar(state: &mut GameState, conn_id: ConnectionId) {
    let (px, py) = if let Some(user) = state.users.get(&conn_id) {
        (user.pos_x, user.pos_y)
    } else {
        return;
    };
    let pkt = binary_packets::write_pos_update(px as u8, py as u8);
    state.send_bytes(conn_id, &pkt).await;
}

/// TENGOMACROS — Macro detection.
pub(super) async fn handle_tengomacros(state: &mut GameState, conn_id: ConnectionId) {
    let (count, name) = if let Some(user) = state.users.get_mut(&conn_id) {
        user.tiene_macro += 1;
        (user.tiene_macro, user.char_name.clone())
    } else {
        return;
    };

    if count >= 2 {
        // Notify admins
        state.send_console_to(SendTarget::ToAdmins,
            &format!("Seguridad>> se detecto el uso de macros en el usuario: {}, hay que revisarlo.", name),
            font_index::AMARILLO).await;

        if let Some(user) = state.users.get_mut(&conn_id) {
            user.tiene_macro = 0;
        }
    }
}


/// # — Send SOS/consultation.
pub(super) async fn handle_sos_send(state: &mut GameState, conn_id: ConnectionId, data: &str) {
    let payload = strip_opcode(data, 1);
    let _tipo: i32 = read_field(1, payload, '|').parse().unwrap_or(0);
    let contenido = read_field(2, payload, '|');

    if let Some(user) = state.users.get(&conn_id) {
        if user.silenced {
            state.send_msg_id(conn_id, 945, &format!("{}", user.silence_timer)).await;
            return;
        }
        if user.consulta_enviada {
            state.send_msg_id(conn_id, 192, "").await;
            return;
        }
    } else {
        return;
    }

    let name = state.users.get(&conn_id).unwrap().char_name.clone();

    // Notify admins
    state.send_msg_id_to(SendTarget::ToAdmins, 193, "").await;

    // Store SOS message
    state.sos_messages.push(crate::game::types::SosMessage {
        tipo: "Consulta".to_string(),
        autor: name,
        contenido: contenido.to_string(),
    });

    let msg_num = state.sos_messages.len() as i32;
    if let Some(user) = state.users.get_mut(&conn_id) {
        user.consulta_enviada = true;
        user.numero_consulta = msg_num;
    }
}

/// X — Admin responds to SOS.
pub(super) async fn handle_sos_respond(state: &mut GameState, conn_id: ConnectionId, data: &str) {
    let payload = strip_opcode(data, 1);
    let target_name = read_field(1, payload, '*');
    let texto = read_field(2, payload, '*');

    let priv_level = state.users.get(&conn_id).map(|u| u.privileges).unwrap_or(0);
    if priv_level < privilege_level::CONSEJERO {
        return;
    }

    let target_conn = state.online_names.get(&target_name.to_uppercase()).copied();
    if let Some(target) = target_conn {
        if let Some(user) = state.users.get_mut(&target) {
            user.consulta_enviada = false;
            user.numero_consulta = 0;
        }
        state.send_msg_id(target, 190, "").await;
        let admin_name = state.users.get(&conn_id).map(|u| u.char_name.clone()).unwrap_or_default();
        let pkt = binary_packets::write_response(&texto, &admin_name);
        state.send_bytes(target, &pkt).await;
    }
}

/// CONSUL — Admin view SOS messages.
pub(super) async fn handle_consul(state: &mut GameState, conn_id: ConnectionId) {
    let priv_level = state.users.get(&conn_id).map(|u| u.privileges).unwrap_or(0);
    if priv_level < privilege_level::CONSEJERO {
        return;
    }

    let mut data_sos = format!("{}|", state.sos_messages.len());
    for msg in &state.sos_messages {
        data_sos.push_str(&format!("{}-{}-{}|", msg.tipo, msg.autor, msg.contenido));
    }
    let pkt = binary_packets::write_sos_data(&data_sos);
    state.send_bytes(conn_id, &pkt).await;
}

/// ENVFPZ — FPZ anti-hack report.
pub(super) async fn handle_envfpz(state: &mut GameState, conn_id: ConnectionId, data: &str) {
    let payload = strip_opcode(data, 6);
    let name = state.users.get(&conn_id).map(|u| u.char_name.clone()).unwrap_or_default();
    state.send_msg_id_to(SendTarget::ToAdmins, 218, &format!("{}@{}", name, payload)).await;
}



/// DYDTRA — Drag & drop transfer items to another player.
pub(super) async fn handle_dydtra(state: &mut GameState, conn_id: ConnectionId, data: &str) {
    let payload = strip_opcode(data, 6);
    let slot: usize = read_field(1, payload, ',').parse().unwrap_or(0);
    let amount: i32 = read_field(2, payload, ',').parse().unwrap_or(0);

    if slot < 1 || slot > MAX_INVENTORY_SLOTS || amount <= 0 {
        return;
    }

    let (map, x, y, _heading) = match state.users.get(&conn_id) {
        Some(u) => (u.pos_map, u.pos_x, u.pos_y, u.heading),
        None => return,
    };

    // Find a player in the target position (1 tile ahead based on heading)
    let target_user = state.users.get(&conn_id).map(|u| u.target_user).unwrap_or(0);
    if target_user == 0 {
        state.send_console(conn_id, "No hay nadie ahi.", font_index::INFO).await;
        return;
    }

    // Check item is transferable
    let si = slot - 1;
    let obj_idx = state.users.get(&conn_id).map(|u| u.inventory[si].obj_index).unwrap_or(0);
    if obj_idx <= 0 {
        return;
    }

    // Check not equipped
    if let Some(user) = state.users.get(&conn_id) {
        if user.inventory[si].equipped {
            state.send_console(conn_id, "No puedes transferir un item equipado.", font_index::INFO).await;
            return;
        }
    }

    // Transfer: remove from source, add to target
    let actual_amount = state.users.get(&conn_id).map(|u| u.inventory[si].amount.min(amount)).unwrap_or(0);
    if actual_amount <= 0 { return; }

    // Try to add to target inventory
    let added = add_item_to_user_inventory(state, target_user, obj_idx, actual_amount);
    if !added {
        state.send_console(conn_id, "El otro jugador no tiene espacio.", font_index::INFO).await;
        return;
    }

    // Remove from source
    if let Some(user) = state.users.get_mut(&conn_id) {
        user.inventory[si].amount -= actual_amount;
        if user.inventory[si].amount <= 0 {
            user.inventory[si] = InventorySlot::default();
        }
    }

    let obj_name = if (obj_idx as usize) < state.game_data.objects.len() {
        state.game_data.objects[obj_idx as usize].name.clone()
    } else {
        "item".to_string()
    };

    let sender_name = state.users.get(&conn_id).map(|u| u.char_name.clone()).unwrap_or_default();
    state.send_console(conn_id, &format!("Has transferido {} {}.", actual_amount, obj_name), font_index::INFO).await;
    state.send_console(target_user, &format!("{} te ha dado {} {}.", sender_name, actual_amount, obj_name), font_index::INFO).await;

    send_full_inventory(state, conn_id).await;
    send_full_inventory(state, target_user).await;
}


/// DOWNSI — Cast spell by target name.
pub(super) async fn handle_downsi(state: &mut GameState, conn_id: ConnectionId, data: &str) {
    let payload = strip_opcode(data, 6);
    let target_name = payload.trim();

    let target_conn = state.online_names.get(&target_name.to_uppercase()).copied();
    if target_conn.is_none() {
        return;
    }
    let target_conn = target_conn.unwrap();

    let pending_spell = state.users.get(&conn_id).map(|u| u.pending_spell).unwrap_or(0);
    if pending_spell == 0 {
        return;
    }

    // Set target and cast
    if let Some(user) = state.users.get_mut(&conn_id) {
        user.target_user = target_conn;
    }

    // Delegate to spell casting logic
    do_cast_spell(state, conn_id).await;
}

/// NVOT — Vote in poll.
pub(super) async fn handle_nvot(state: &mut GameState, conn_id: ConnectionId, data: &str) {
    let payload = strip_opcode(data, 4);
    let option: usize = payload.trim().parse().unwrap_or(0);

    if !state.poll_active {
        state.send_console(conn_id, "No hay votacion activa.", font_index::INFO).await;
        return;
    }

    if option < 1 || option > 5 {
        return;
    }

    let name = state.users.get(&conn_id).map(|u| u.char_name.clone()).unwrap_or_default();
    if state.poll_voters.contains(&name) {
        state.send_console(conn_id, "Ya has votado.", font_index::INFO).await;
        return;
    }

    state.poll_votes[option - 1] += 1;
    state.poll_voters.push(name);
    state.send_console(conn_id, "Voto registrado.", font_index::INFO).await;
}

/// NEWD — New report/denuncia.
pub(super) async fn handle_newd(state: &mut GameState, conn_id: ConnectionId, data: &str) {
    let payload = strip_opcode(data, 4);
    let target_name = read_field(1, payload, ',');
    let reason = read_field(2, payload, ',');

    let reporter = state.users.get(&conn_id).map(|u| u.char_name.clone()).unwrap_or_default();

    info!("[REPORT] {} reports {}: {}", reporter, target_name, reason);

    state.send_msg_id_to(SendTarget::ToAdmins, 218, &format!("{}@Denuncia contra {}: {}", reporter, target_name, reason)).await;

    state.send_console(conn_id, "Denuncia enviada.", font_index::INFO).await;
}

/// Helper: add item to user inventory, returns true if successful.
// add_item_to_user_inventory — moved to common.rs

// =====================================================================
// Missing slash commands
// =====================================================================

/// /MONTAR — Mount pet.
pub(super) async fn handle_slash_montar(state: &mut GameState, conn_id: ConnectionId) {
    let has_mount = state.users.get(&conn_id).map(|u| u.nro_mascotas > 0).unwrap_or(false);
    if !has_mount {
        state.send_console(conn_id, "No tienes una montura.", font_index::INFO).await;
        return;
    }

    let already_mounted = state.users.get(&conn_id).map(|u| u.montado).unwrap_or(false);
    if already_mounted {
        state.send_console(conn_id, "Ya estas montado.", font_index::INFO).await;
        return;
    }

    // Simple mount: save body and change to mount body
    let (map, x, y) = match state.users.get(&conn_id) {
        Some(u) => (u.pos_map, u.pos_x, u.pos_y),
        None => return,
    };

    // VB6: Check first pet's NPC number and assign mount body
    let pet_idx = state.users.get(&conn_id).map(|u| u.mascotas_index[0]).unwrap_or(0);
    let npc_num = state.get_npc(pet_idx).map(|n| n.npc_number).unwrap_or(0);
    let mount_body = match npc_num {
        156 => 331, // Horse 1
        157 => 330, // Horse 2
        158 => 352, // Horse 3
        181 => 358, // Dragon/Special 1
        182 => 359, // Dragon/Special 2
        _ => 296,   // Generic mount fallback
    };

    if let Some(user) = state.users.get_mut(&conn_id) {
        user.montado = true;
        user.montado_body = user.body;
        user.body = mount_body;
        user.weapon_anim = super::common::NINGUN_ARMA;
        user.shield_anim = super::common::NINGUN_ESCUDO;
        user.casco_anim = super::common::NINGUN_CASCO;
    }

    let cp = {
        let user = state.users.get(&conn_id).unwrap();
        binary_packets::write_character_change(
            user.char_index.0 as i16, user.body as i16, user.head as i16, user.heading as u8,
            super::common::NINGUN_ARMA as i16, super::common::NINGUN_ESCUDO as i16,
            super::common::NINGUN_CASCO as i16, 0, 0,
        )
    };
    state.send_data_bytes(SendTarget::ToArea { map, x, y }, &cp).await;

    // Send mount state packet
    let char_index = state.users.get(&conn_id).map(|u| u.char_index.0).unwrap_or(0);
    let usm_pkt = binary_packets::write_user_mount(char_index as i16, true);
    state.send_data_bytes(SendTarget::ToArea { map, x, y }, &usm_pkt).await;

    state.send_console(conn_id, "Te has montado.", font_index::INFO).await;
}

/// /DESMONTAR — Dismount.
pub(super) async fn handle_slash_desmontar(state: &mut GameState, conn_id: ConnectionId) {
    let is_mounted = state.users.get(&conn_id).map(|u| u.montado).unwrap_or(false);
    if !is_mounted {
        state.send_console(conn_id, "No estas montado.", font_index::INFO).await;
        return;
    }

    let (map, x, y) = match state.users.get(&conn_id) {
        Some(u) => (u.pos_map, u.pos_x, u.pos_y),
        None => return,
    };

    if let Some(user) = state.users.get_mut(&conn_id) {
        user.montado = false;
        user.levitando = false;
        user.body = user.montado_body;
        user.head = user.orig_head; // Restore head (flying mounts hide it)
    }

    // Restore equipped weapon/shield/helmet appearance
    let (weapon_anim, shield_anim, casco_anim) = get_equipped_anims(state, conn_id);
    if let Some(user) = state.users.get_mut(&conn_id) {
        user.weapon_anim = weapon_anim;
        user.shield_anim = shield_anim;
        user.casco_anim = casco_anim;
    }

    let cp = {
        let user = state.users.get(&conn_id).unwrap();
        binary_packets::write_character_change(
            user.char_index.0 as i16, user.body as i16, user.head as i16, user.heading as u8,
            user.weapon_anim as i16, user.shield_anim as i16, user.casco_anim as i16, 0, 0,
        )
    };
    state.send_data_bytes(SendTarget::ToArea { map, x, y }, &cp).await;

    // Send mount state packet
    let char_index = state.users.get(&conn_id).map(|u| u.char_index.0).unwrap_or(0);
    let usm_pkt = binary_packets::write_user_mount(char_index as i16, false);
    state.send_data_bytes(SendTarget::ToArea { map, x, y }, &usm_pkt).await;

    state.send_console(conn_id, "Te has desmontado.", font_index::INFO).await;
}

/// Helper: get equipped item animation GRH IDs.
// get_equipped_anims — moved to common.rs

/// /QUITARMASCOTA — Remove pet.
pub(super) async fn handle_slash_quitarmascota(state: &mut GameState, conn_id: ConnectionId) {
    let nro = state.users.get(&conn_id).map(|u| u.nro_mascotas).unwrap_or(0);
    if nro == 0 {
        state.send_console(conn_id, "No tienes mascotas.", font_index::INFO).await;
        return;
    }

    // Remove all pets
    if let Some(user) = state.users.get_mut(&conn_id) {
        for i in 0..3 {
            if user.mascotas_index[i] > 0 {
                // Kill the NPC
                let idx = user.mascotas_index[i];
                if let Some(npc) = state.npcs.get_mut(idx).and_then(|n| n.as_mut()) {
                    npc.min_hp = 0;
                }
            }
            user.mascotas_index[i] = 0;
            user.mascotas_type[i] = 0;
        }
        user.nro_mascotas = 0;
    }

    state.send_console(conn_id, "Mascota removida.", font_index::INFO).await;
}

/// /MSJ — Toggle private messages.
pub(super) async fn handle_slash_msj(state: &mut GameState, conn_id: ConnectionId) {
    let new_state = if let Some(user) = state.users.get_mut(&conn_id) {
        user.msj_privados = !user.msj_privados;
        user.msj_privados
    } else {
        return;
    };

    if new_state {
        state.send_console(conn_id, "Mensajes privados activados.", font_index::INFO).await;
    } else {
        state.send_console(conn_id, "Mensajes privados desactivados.", font_index::INFO).await;
    }
}

/// /CIUDADANIA — Set citizenship. VB6: requires Ciudadania NPC (type 13), distance <= 3.
/// Maps: 130→Inthak, 25→Thir/Ruvendel.
pub(super) async fn handle_slash_ciudadania(state: &mut GameState, conn_id: ConnectionId) {
    let (target_npc, map) = match state.users.get(&conn_id) {
        Some(u) if u.logged => (u.target_npc, u.pos_map),
        _ => return,
    };

    // VB6: If TargetNPC = 0 Then ||9
    if target_npc == 0 {
        state.send_msg_id(conn_id, 9, "").await;
        return;
    }

    // VB6: distance > 3 → ||10
    let (npc_map, npc_x, npc_y, npc_type) = match state.get_npc(target_npc) {
        Some(npc) => (npc.map, npc.x, npc.y, npc.npc_type),
        None => return,
    };
    let (u_map, u_x, u_y) = match state.users.get(&conn_id) {
        Some(u) => (u.pos_map, u.pos_x, u.pos_y),
        None => return,
    };
    if u_map != npc_map || (u_x - npc_x).abs() > 3 || (u_y - npc_y).abs() > 3 {
        state.send_msg_id(conn_id, 10, "").await;
        return;
    }

    // VB6: NPCtype must be Ciudadania (13)
    if npc_type != crate::data::npcs::NpcType::Citizenship { return; }

    // VB6: Set home based on map (130=Inthak, 25=Thir)
    let city = match map {
        130 => "Inthak",
        25 => "Thir",
        _ => {
            state.send_console(conn_id, "No estas en una ciudad valida.", font_index::INFO).await;
            return;
        }
    };

    let current_home = state.users.get(&conn_id).map(|u| u.hogar.clone()).unwrap_or_default();
    if current_home == city { return; } // VB6: If already same home, exit

    if let Some(user) = state.users.get_mut(&conn_id) {
        user.hogar = city.to_string();
    }

    // VB6: ||318@<home>
    state.send_msg_id(conn_id, 318, &format!("{}", city)).await;
}

/// /VIAJAR — Travel to city via Traveler NPC.
/// VB6: TCP_HandleData3.bas lines 760-846
pub(super) async fn handle_slash_viajar(state: &mut GameState, conn_id: ConnectionId, city: &str) {
    let city_upper = city.trim().to_uppercase();

    // Validate city name (VB6 line 763)
    let valid = ["TANARIS", "ANVILMAR", "KAHLIMDOR", "THIR", "INTHAK", "JHUMBEL", "RUVENDEL", "HELKA"];
    if !valid.contains(&city_upper.as_str()) {
        state.send_console(conn_id, "Ciudad desconocida. Ciudades: Tanaris, Anvilmar, Kahlimdor, Thir, Inthak, Jhumbel, Ruvendel, Helka", font_index::INFO).await;
        return;
    }

    // Must have traveler NPC targeted (VB6 NpcType=12)
    let user_data = match state.users.get(&conn_id) {
        Some(u) if u.logged => (u.dead, u.level, u.gold, u.target_npc_idx, u.pos_map, u.pos_x, u.pos_y),
        _ => return,
    };
    let (dead, level, gold, target_npc, _map, _ux, _uy) = user_data;

    if dead {
        state.send_msg_id(conn_id, 3, "").await;
        return;
    }

    if target_npc == 0 {
        state.send_msg_id(conn_id, 9, "").await;
        return;
    }

    // Check NPC is a Traveler (type 12)
    let npc_ok = state.get_npc(target_npc).map(|n| n.npc_type == crate::data::npcs::NpcType::Traveler).unwrap_or(false);
    if !npc_ok { return; }

    // Gold cost: <30 = 1000, >=30 = 5000 (VB6 lines 778-788)
    let cost = if level < 30 { 1000i64 } else { 5000 };
    if gold < cost {
        let cost_str = if level < 30 { "1.000" } else { "5.000" };
        state.send_msg_id(conn_id, 215, &format!("{}", cost_str)).await;
        return;
    }

    // Inthak requires level 30+ (VB6 line 812)
    if city_upper == "INTHAK" && level < 30 {
        state.send_msg_id(conn_id, 542, "").await;
        return;
    }

    // VB6 exact destinations (TCP_HandleData3.bas lines 790-838)
    let (dest_map, dest_x, dest_y) = match city_upper.as_str() {
        "TANARIS" => (28, 54, 35),
        "ANVILMAR" => (29, 46, 85),
        "KAHLIMDOR" => (27, 50, 48),
        "THIR" => (25, 74, 45),
        "INTHAK" => (130, 50, 57),
        "JHUMBEL" => {
            // Random spawn in map 69 (VB6 lines 820-832)
            let roll = rand_simple_u32() % 5;
            match roll {
                0 => (69, 35 + (rand_simple_u32() % 8) as i32, 16 + (rand_simple_u32() % 9) as i32),
                1 => (69, 42 + (rand_simple_u32() % 6) as i32, 40 + (rand_simple_u32() % 9) as i32),
                2 => (69, 54 + (rand_simple_u32() % 14) as i32, 71 + (rand_simple_u32() % 6) as i32),
                3 => (69, 30 + (rand_simple_u32() % 8) as i32, 79 + (rand_simple_u32() % 7) as i32),
                _ => (69, 19 + (rand_simple_u32() % 6) as i32, 31 + (rand_simple_u32() % 4) as i32),
            }
        }
        "RUVENDEL" => (26, 51, 52),
        "HELKA" => (136, 52, 55),
        _ => return,
    };

    // Deduct gold (VB6 lines 840-844)
    if let Some(user) = state.users.get_mut(&conn_id) {
        user.gold -= cost;
    }
    send_stats_gold(state, conn_id).await;
    warp_user(state, conn_id, dest_map, dest_x, dest_y).await;

    // VB6: WarpUserChar with FX=True sends warp sound + warp FX (FXWARP=1, SND_WARP=3)
    if let Some(user) = state.users.get(&conn_id) {
        if !user.admin_invisible {
            let char_idx = user.char_index.0;
            let map = user.pos_map;
            let x = user.pos_x;
            let y = user.pos_y;
            let snd_pkt = binary_packets::write_play_wave(3, x as u8, y as u8); // SND_WARP = 3
            let fx_pkt = binary_packets::write_create_fx(char_idx as i16, 1, 0); // FXWARP = 1
            // Send to area (others see it) AND directly to self (ensure self always gets it)
            state.send_data_bytes(SendTarget::ToArea { map, x, y }, &snd_pkt).await;
            state.send_data_bytes(SendTarget::ToArea { map, x, y }, &fx_pkt).await;
            // Also send directly to ensure self receives it (area detection may miss self right after warp)
            state.send_bytes(conn_id, &snd_pkt).await;
            state.send_bytes(conn_id, &fx_pkt).await;
        }
    }
}

/// /ENTRENAR — Open creature training list from trainer NPC.
/// VB6: requires Entrenador NPC (type 3), distance <= 10, not dead.
pub(super) async fn handle_slash_entrenar(state: &mut GameState, conn_id: ConnectionId) {
    let (dead, target_npc) = match state.users.get(&conn_id) {
        Some(u) if u.logged => (u.dead, u.target_npc),
        _ => return,
    };

    // VB6: If Muerto Then ||3
    if dead {
        state.send_msg_id(conn_id, 3, "").await;
        return;
    }

    // VB6: If TargetNPC = 0 Then ||9
    if target_npc == 0 {
        state.send_msg_id(conn_id, 9, "").await;
        return;
    }

    // VB6: distance > 10 → ||10
    let (npc_map, npc_x, npc_y, npc_type) = match state.get_npc(target_npc) {
        Some(npc) => (npc.map, npc.x, npc.y, npc.npc_type),
        None => return,
    };
    let (u_map, u_x, u_y) = match state.users.get(&conn_id) {
        Some(u) => (u.pos_map, u.pos_x, u.pos_y),
        None => return,
    };
    if u_map != npc_map || (u_x - npc_x).abs() > 10 || (u_y - npc_y).abs() > 10 {
        state.send_msg_id(conn_id, 10, "").await;
        return;
    }

    // VB6: NPCtype must be Entrenador (3)
    if npc_type != crate::data::npcs::NpcType::Trainer { return; }

    // VB6: EnviarListaCriaturas — sends LSTCRI<count>,<name1>,<name2>,...
    let npc_number = match state.get_npc(target_npc) {
        Some(npc) => npc.npc_number,
        None => return,
    };
    let npc_data = match state.game_data.npcs.get(npc_number) {
        Some(nd) => nd,
        None => return,
    };
    let mut creatures = format!("{},", npc_data.nro_criaturas);
    for c in &npc_data.criaturas {
        creatures.push_str(&c.npc_name);
        creatures.push(',');
    }
    let pkt = binary_packets::write_trainer_creature_list(&creatures);
    state.send_bytes(conn_id, &pkt).await;
}

/// /CENTINELA — Anti-AFK response.
pub(super) async fn handle_slash_centinela(state: &mut GameState, conn_id: ConnectionId, code: &str) {
    // Simple anti-AFK — accept any response
    state.send_console(conn_id, "Centinela verificado.", font_index::INFO).await;
}

/// /IR — Premium travel.
pub(super) async fn handle_slash_ir(state: &mut GameState, conn_id: ConnectionId, destination: &str) {
    // Check premium status (not fully implemented — just accept for now)
    let dest_upper = destination.trim().to_uppercase();

    let (dest_map, dest_x, dest_y) = match dest_upper.as_str() {
        "INTHAK" => (1, 50, 50),
        "THIR" => (6, 50, 50),
        "RUVENDEL" => (11, 50, 50),
        _ => {
            state.send_console(conn_id, "Destino desconocido.", font_index::INFO).await;
            return;
        }
    };

    warp_user(state, conn_id, dest_map, dest_x, dest_y).await;
}

/// /VOTAR — Vote in poll.
pub(super) async fn handle_slash_votar(state: &mut GameState, conn_id: ConnectionId) {
    if !state.poll_active {
        state.send_console(conn_id, "No hay votacion activa.", font_index::INFO).await;
        return;
    }
    // Send poll options
    let mut msg = String::new();
    for i in 0..5 {
        msg.push_str(&state.poll_options[i]);
        msg.push(',');
    }
    let pkt = binary_packets::write_select_data(&msg);
    state.send_bytes(conn_id, &pkt).await;
}

/// /RESULTADOS — Poll results.
pub(super) async fn handle_slash_resultados(state: &mut GameState, conn_id: ConnectionId) {
    let total: i32 = state.poll_votes.iter().sum();
    let mut msg = String::from("Resultados de la votacion:");
    for i in 0..5 {
        if !state.poll_options[i].is_empty() {
            let pct = if total > 0 { (state.poll_votes[i] * 100) / total } else { 0 };
            msg.push_str(&format!(" {}: {} ({}%)", state.poll_options[i], state.poll_votes[i], pct));
        }
    }
    state.send_console(conn_id, &msg, font_index::INFO).await;
}


/// /CIRUJIA — Surgery (race change). VB6: requires cirujano NPC, distance <= 3.
pub(super) async fn handle_slash_cirujia(state: &mut GameState, conn_id: ConnectionId) {
    let (dead, target_npc) = match state.users.get(&conn_id) {
        Some(u) if u.logged => (u.dead, u.target_npc),
        _ => return,
    };

    if dead {
        state.send_msg_id(conn_id, 3, "").await;
        return;
    }

    if target_npc == 0 { return; }

    // Check distance <= 3
    let (npc_map, npc_x, npc_y, npc_type) = match state.get_npc(target_npc) {
        Some(npc) => (npc.map, npc.x, npc.y, npc.npc_type),
        None => return,
    };
    let (u_map, u_x, u_y) = match state.users.get(&conn_id) {
        Some(u) => (u.pos_map, u.pos_x, u.pos_y),
        None => return,
    };
    if u_map != npc_map || (u_x - npc_x).abs() > 3 || (u_y - npc_y).abs() > 3 {
        state.send_msg_id(conn_id, 158, "").await;
        return;
    }

    // VB6: NPCtype must be cirujano (19)
    if npc_type != crate::data::npcs::NpcType::Surgeon { return; }

    // VB6: sends CIRUJA<raza>,<genero>
    let (raza, genero) = match state.users.get(&conn_id) {
        Some(u) => (u.race.clone(), u.gender),
        None => return,
    };
    let raza_num = match raza.as_str() {
        "Humano" => 1, "Elfo" => 2, "ElfoOscuro" => 3, "Enano" => 4, "Gnomo" => 5,
        _ => 1,
    };
    let pkt = binary_packets::write_cosmetic_surgery(raza_num as u8, genero as u8);
    state.send_bytes(conn_id, &pkt).await;
}




/// Helper: get INI var using PathBuf.
// ini_get, ini_write, user_has_items — moved to common.rs

/// FWO — Query house owner and price from Casas.dat.
pub(super) async fn handle_fwo(state: &mut GameState, conn_id: ConnectionId, data: &str) {
    let payload = strip_opcode(data, 3);
    let num_casa = read_field(1, payload, ',');

    let casas_path = state.base_path.join("dat").join("Casas.dat");
    let section = format!("Casa{}", num_casa);

    let mut dueno = ini_get(&casas_path, &section, "Dueno");
    if dueno.is_empty() { dueno = ini_get(&casas_path, &section, "Due\u{00F1}o"); }
    if dueno.is_empty() { dueno = "N/A".to_string(); }
    let precio = ini_get(&casas_path, &section, "Precio");
    let fecha = ini_get(&casas_path, &section, "Fecha");

    let data = format!("{},{},{}", dueno, precio, fecha);
    let pkt = binary_packets::write_auction_bid(&data);
    state.send_bytes(conn_id, &pkt).await;
}

/// CUC — Buy a house.
pub(super) async fn handle_cuc(state: &mut GameState, conn_id: ConnectionId, data: &str) {
    let payload = strip_opcode(data, 3);
    let num_casa: i32 = read_field(1, payload, ',').parse().unwrap_or(0);

    let casas_path = state.base_path.join("dat").join("Casas.dat");
    let section = format!("Casa{}", num_casa);

    let mut dueno = ini_get(&casas_path, &section, "Dueno");
    if dueno.is_empty() { dueno = ini_get(&casas_path, &section, "Due\u{00F1}o"); }
    if dueno.is_empty() { dueno = "N/A".to_string(); }

    if dueno != "N/A" {
        state.send_msg_id(conn_id, 243, "").await;
        return;
    }

    let precio: i64 = ini_get(&casas_path, &section, "Precio").parse().unwrap_or(0);

    let gold = state.users.get(&conn_id).map(|u| u.gold).unwrap_or(0);
    if gold < precio {
        state.send_msg_id(conn_id, 215, &format!("{}", precio)).await;
        return;
    }

    if num_casa <= 0 {
        return;
    }

    // Key obj_index = 1093 + num_casa
    let key_index = 1093 + num_casa;
    if !add_item_to_user_inventory(state, conn_id, key_index, 1) {
        state.send_msg_id(conn_id, 108, "").await;
        return;
    }

    // Save owner to Casas.dat
    let char_name = state.users.get(&conn_id).map(|u| u.char_name.clone()).unwrap_or_default();
    ini_write(&casas_path, &section, "Dueno", &char_name);
    ini_write(&casas_path, &section, "Fecha", &chrono_like_date());

    // Broadcast to all
    state.send_msg_id_to(SendTarget::ToAll, 244, &format!("{}@{}", char_name, num_casa)).await;

    // Deduct gold
    if let Some(user) = state.users.get_mut(&conn_id) {
        user.gold -= precio;
    }
    send_stats_gold(state, conn_id).await;
    send_full_inventory(state, conn_id).await;
}

/// CNM — Rename pet/creature.
pub(super) async fn handle_cnm(state: &mut GameState, conn_id: ConnectionId, data: &str) {
    let payload = strip_opcode(data, 3);
    let nick = read_field(1, payload, ',');

    let pet_idx = state.users.get(&conn_id)
        .and_then(|u| if u.nro_mascotas > 0 { Some(u.mascotas_index[0]) } else { None })
        .unwrap_or(0);

    if pet_idx > 0 {
        if let Some(Some(npc)) = state.npcs.get_mut(pet_idx) {
            npc.name = nick.clone();
            state.send_console(conn_id, &format!("Mascota renombrada a: {}", nick), font_index::INFO).await;
        }
    } else {
        state.send_console(conn_id, "No tienes mascotas.", font_index::INFO).await;
    }
}


/// NANVAME — Clan name validated (notify admins).
pub(super) async fn handle_nanvame(state: &mut GameState, conn_id: ConnectionId, _data: &str) {
    let name = state.users.get(&conn_id).map(|u| u.char_name.clone()).unwrap_or_default();
    state.send_msg_id_to(SendTarget::ToAdmins, 498, &format!("{}", name)).await;
}

/// NANVAMX — Clan name invalid (notify admins).
pub(super) async fn handle_nanvamx(state: &mut GameState, conn_id: ConnectionId, _data: &str) {
    let name = state.users.get(&conn_id).map(|u| u.char_name.clone()).unwrap_or_default();
    state.send_msg_id_to(SendTarget::ToAdmins, 499, &format!("{}", name)).await;
}

/// PCGF — Forward party/clan GUI data to target user.
pub(super) async fn handle_pcgf(state: &mut GameState, conn_id: ConnectionId, data: &str) {
    let payload = strip_opcode(data, 4);
    let proceso = read_field(1, payload, ',');
    let peso = read_field(2, payload, ',');
    let target_idx: ConnectionId = read_field(3, payload, ',').parse().unwrap_or(0);

    let sender_name = state.users.get(&conn_id).map(|u| u.char_name.clone()).unwrap_or_default();
    if target_idx > 0 && state.users.contains_key(&target_idx) {
        let data = format!("{},{},{}", proceso, peso, sender_name);
        let pkt = binary_packets::write_cosmetic_pcgn(&data);
        state.send_bytes(target_idx, &pkt).await;
    }
}

/// PCWC — Forward party/clan window command to target user.
pub(super) async fn handle_pcwc(state: &mut GameState, conn_id: ConnectionId, data: &str) {
    let payload = strip_opcode(data, 4);
    let proceso = read_field(1, payload, ',');
    let target_idx: ConnectionId = read_field(2, payload, ',').parse().unwrap_or(0);

    let sender_name = state.users.get(&conn_id).map(|u| u.char_name.clone()).unwrap_or_default();
    if target_idx > 0 && state.users.contains_key(&target_idx) {
        let data = format!("{},{}", proceso, sender_name);
        let pkt = binary_packets::write_cosmetic_pcss(&data);
        state.send_bytes(target_idx, &pkt).await;
    }
}

/// PCCC — Forward party/clan caption to target user.
pub(super) async fn handle_pccc(state: &mut GameState, conn_id: ConnectionId, data: &str) {
    let payload = strip_opcode(data, 4);
    let caption = read_field(1, payload, ',');
    let target_idx: ConnectionId = read_field(2, payload, ',').parse().unwrap_or(0);

    let sender_name = state.users.get(&conn_id).map(|u| u.char_name.clone()).unwrap_or_default();
    if target_idx > 0 && state.users.contains_key(&target_idx) {
        let data = format!("{},{}", caption, sender_name);
        let pkt = binary_packets::write_cosmetic_pccc(&data);
        state.send_bytes(target_idx, &pkt).await;
    }
}

/// /VOTO — Vote for guild leader candidate.
pub(super) async fn handle_slash_voto(state: &mut GameState, conn_id: ConnectionId, candidate: &str) {
    let guild_idx = state.users.get(&conn_id).map(|u| u.guild_index).unwrap_or(0);
    if guild_idx <= 0 {
        state.send_console(conn_id, "No perteneces a ningun clan.", font_index::INFO).await;
        return;
    }

    // Simplified: just acknowledge the vote (full guild elections not implemented yet)
    state.send_msg_id(conn_id, 439, "").await;
}


// =====================================================================
// Marriage system — VB6 TCP_HandleData3.bas
// =====================================================================

/// /CASAR <name> — Marry another player. VB6 TCP_HandleData3.bas:1195
/// Both must be online, neither married, distance <= 3 tiles.
pub(super) async fn handle_slash_casar(state: &mut GameState, conn_id: ConnectionId, target_name: &str) {
    let (my_name, my_map, my_x, my_y, my_pareja, my_dead) = match state.users.get(&conn_id) {
        Some(u) if u.logged => (u.char_name.clone(), u.pos_map, u.pos_x, u.pos_y, u.pareja.clone(), u.dead),
        _ => return,
    };

    if my_dead {
        state.send_msg_id(conn_id, 3, "").await; // Can't do this while dead
        return;
    }

    if !my_pareja.is_empty() {
        state.send_console(conn_id, &format!("Ya estas casado/a con {}.", my_pareja), font_index::INFO).await;
        return;
    }

    let target_id = match state.find_user_by_name(target_name) {
        Some(id) => id,
        None => {
            state.send_msg_id(conn_id, 196, "").await; // User not found
            return;
        }
    };

    if target_id == conn_id {
        state.send_console(conn_id, "No podes casarte con vos mismo.", font_index::INFO).await;
        return;
    }

    let (t_pareja, t_map, t_x, t_y, t_dead, t_name) = match state.users.get(&target_id) {
        Some(u) if u.logged => (u.pareja.clone(), u.pos_map, u.pos_x, u.pos_y, u.dead, u.char_name.clone()),
        _ => {
            state.send_msg_id(conn_id, 196, "").await;
            return;
        }
    };

    if t_dead {
        state.send_console(conn_id, "El jugador esta muerto.", font_index::INFO).await;
        return;
    }

    if !t_pareja.is_empty() {
        state.send_console(conn_id, &format!("{} ya esta casado/a.", t_name), font_index::INFO).await;
        return;
    }

    // VB6: Distance check <= 3
    if my_map != t_map || (my_x - t_x).abs() > 3 || (my_y - t_y).abs() > 3 {
        state.send_console(conn_id, "Debes estar cerca del jugador (3 tiles).", font_index::INFO).await;
        return;
    }

    // Marry both
    if let Some(u) = state.users.get_mut(&conn_id) {
        u.pareja = t_name.clone();
    }
    if let Some(u) = state.users.get_mut(&target_id) {
        u.pareja = my_name.clone();
    }

    // Broadcast marriage announcement
    state.send_msg_id_to(SendTarget::ToAll, 526, &format!("{}@{}", my_name, t_name)).await;

    state.send_console(conn_id, &format!("Te has casado con {}!", t_name), font_index::INFO).await;
    state.send_console(target_id, &format!("Te has casado con {}!", my_name), font_index::INFO).await;
}

/// /DIVORCIARSE — Divorce from spouse. VB6 TCP_HandleData3.bas:1262
pub(super) async fn handle_slash_divorciarse(state: &mut GameState, conn_id: ConnectionId) {
    let (my_name, my_pareja) = match state.users.get(&conn_id) {
        Some(u) if u.logged => (u.char_name.clone(), u.pareja.clone()),
        _ => return,
    };

    if my_pareja.is_empty() {
        state.send_console(conn_id, "No estas casado/a.", font_index::INFO).await;
        return;
    }

    // Clear our marriage
    if let Some(u) = state.users.get_mut(&conn_id) {
        u.pareja.clear();
    }

    // Clear spouse's marriage (if online)
    if let Some(spouse_id) = state.find_user_by_name(&my_pareja) {
        if let Some(u) = state.users.get_mut(&spouse_id) {
            u.pareja.clear();
        }
        state.send_console(spouse_id, &format!("{} se ha divorciado de ti.", my_name), font_index::INFO).await;
    }

    // Broadcast divorce
    state.send_msg_id_to(SendTarget::ToAll, 527, &format!("{}@{}", my_name, my_pareja)).await;

    state.send_console(conn_id, &format!("Te has divorciado de {}.", my_pareja), font_index::INFO).await;
}

// =====================================================================
// Gran Poder system — VB6 modGranPoder
// =====================================================================


// =====================================================================
// Integration tests — full client login flow
// =====================================================================

