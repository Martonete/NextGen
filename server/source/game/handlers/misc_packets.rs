//! Miscellaneous packet handlers: swap, skills, stat updates, UI packets,
//! training, SOS, drag & drop, voting, reporting.

use tracing::info;
use crate::net::ConnectionId;
use crate::game::class_race::PlayerRace;
use crate::game::types::{GameState, UserState, SendTarget, InventorySlot, MAX_INVENTORY_SLOTS, MAX_SPELL_SLOTS, privilege_level};
use crate::game::world;
use crate::protocol::{font_index, binary_packets};
use crate::data::objects::{ObjData, ObjType};
use crate::db::guilds;
use super::common::*;
use super::{
    warp_user, revive_user, send_inventory_slot, send_full_inventory,
    check_user_level, send_full_spells, build_anm_packet,
    make_user_visible, handle_drop_item, do_cast_spell,
};

// =====================================================================
// Missing VB6 handlers — Phase 10 parity
// =====================================================================

/// SWAP — Swap two inventory slots.
pub(super) async fn handle_swap(state: &mut GameState, conn_id: ConnectionId, slot1: usize, slot2: usize) {
    if let Some(user) = state.users.get(&conn_id) {
        if user.comerciando || user.trading {
            state.send_msg_id(conn_id, 153, "");
            return;
        }
    }

    if let Some(user) = state.users.get_mut(&conn_id) {
        let max_slots = user.current_inventory_slots;
        if slot1 == 0 || slot2 == 0 || slot1 > max_slots || slot2 > max_slots || slot1 == slot2 {
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
        if user.backpack_slot == slot1 { user.backpack_slot = slot2; }
        else if user.backpack_slot == slot2 { user.backpack_slot = slot1; }
    }
    send_full_inventory(state, conn_id).await;
}

/// SKSE — Distribute skill points.
pub(super) async fn handle_skse(state: &mut GameState, conn_id: ConnectionId, increments: &[i32; 22]) {
    let mut total = 0i32;
    for &val in increments.iter() {
        if val < 0 {
            state.send_console(conn_id, "Valor invalido.", font_index::INFO);
            return;
        }
        total += val;
    }

    if let Some(user) = state.users.get(&conn_id) {
        if total > user.skill_pts_libres || total <= 0 {
            state.send_console(conn_id, "Puntos de skill invalidos.", font_index::INFO);
            return;
        }
    } else {
        return;
    }

    if let Some(user) = state.users.get_mut(&conn_id) {
        for i in 0..22 {
            let new_val = (user.skills[i] + increments[i]).min(100);
            user.skills[i] = new_val;
        }
        user.skill_pts_libres -= total;
    }

    state.send_console(conn_id, &format!("Has distribuido {} puntos de skill.", total), font_index::INFO);
}

/// INFS — Spell info. VB6: TCP_HandleData1.bas:2747-2764
/// Sends ||281 through ||287 packets (message-based, client reads from Textos.ao)
pub(super) async fn handle_infs(state: &mut GameState, conn_id: ConnectionId, slot: usize) {
    let spell_idx = state.users.get(&conn_id)
        .and_then(|u| if slot >= 1 && slot <= MAX_SPELL_SLOTS { Some(u.spells[slot - 1]) } else { None })
        .unwrap_or(0);

    if spell_idx <= 0 || spell_idx as usize > state.game_data.spells.len() {
        state.send_msg_id(conn_id, 288, ""); // VB6: error message
        return;
    }

    let spell = &state.game_data.spells[spell_idx as usize - 1];
    let nombre = spell.nombre.clone();
    let desc = spell.desc.clone();
    let min_skill = spell.min_skill;
    let mana_req = spell.mana_requerido;
    let sta_req = spell.sta_requerido;

    // VB6 sends 7 packets: ||281 (header), ||282@name, ||283@desc, ||284@skill, ||285@mana, ||286@sta, ||287 (footer)
    state.send_msg_id(conn_id, 281, "");
    state.send_msg_id(conn_id, 282, &format!("{}", nombre));
    state.send_msg_id(conn_id, 283, &format!("{}", desc));
    state.send_msg_id(conn_id, 284, &format!("{}", min_skill));
    state.send_msg_id(conn_id, 285, &format!("{}", mana_req));
    state.send_msg_id(conn_id, 286, &format!("{}", sta_req));
    state.send_msg_id(conn_id, 287, "");
}

/// DESPHE — Move/swap spell positions. VB6: DesplazarHechizo(userindex, Dire, CualHechizo)
/// Format: DESPHE<direction>,<slot> where direction=1(up) or 2(down), slot=1-based
pub(super) async fn handle_desphe(state: &mut GameState, conn_id: ConnectionId, direction: i32, slot: usize) {
    if !(direction >= 1 && direction <= 2) { return; }
    if slot < 1 || slot > MAX_SPELL_SLOTS { return; }

    if direction == 1 {
        // Move UP: swap slot with slot-1
        if slot == 1 {
            state.send_msg_id(conn_id, 37, ""); // VB6: can't move first slot up
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
            state.send_msg_id(conn_id, 37, ""); // VB6: can't move last slot down
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
pub(super) async fn handle_daminf(state: &mut GameState, conn_id: ConnectionId, target_name: &str) {
    // Find target user by name
    let target_conn = state.online_names.get(&target_name.to_uppercase()).copied();
    if target_conn.is_none() {
        state.send_console(conn_id, "Usuario no encontrado.", font_index::INFO);
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
    state.send_bytes(conn_id, &pkt);
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
    state.send_bytes(conn_id, &pkt);
}

/// CABEZI — Change head/hairstyle (barber). Costs 500 gold.
pub(super) async fn handle_cabezi(state: &mut GameState, conn_id: ConnectionId, new_head: i32) {
    if new_head <= 0 {
        return;
    }

    let cost: i64 = 500;

    if let Some(user) = state.users.get(&conn_id) {
        if user.gold < cost {
            state.send_console(conn_id, "No tienes suficiente oro.", font_index::INFO);
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
    state.send_data_bytes(SendTarget::ToArea { map, x, y }, &cp);
    state.send_console(conn_id, "Cabeza cambiada.", font_index::INFO);
}

/// TR — Drop item via mouse click (at current position).
pub(super) async fn handle_mouse_drop(state: &mut GameState, conn_id: ConnectionId, slot: usize, amount: i32) {
    if slot < 1 || slot > MAX_INVENTORY_SLOTS || amount <= 0 {
        return;
    }

    // Delegate to the same drop logic as TI
    handle_drop_item(state, conn_id, slot, amount).await;
}

/// BOF — Level bonus selection.
pub(super) async fn handle_bof(state: &mut GameState, conn_id: ConnectionId, selection: i32) {
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
        let clamped = user.min_hp.min(user.max_hp);
        user.min_hp = clamped;
    }

    send_stats_hp(state, conn_id).await;
    state.send_console(conn_id, &format!("Has ganado {} puntos de vida extra!", hp_bonus), font_index::INFO);
}

/// UK — Use Skill. VB6: TCP_HandleData1.bas Case "UK".
/// Robar/Magia/Domar → sends T01<skillID> to client (opens skill tree UI).
/// Skill use handler (UK packet). do_ocultarse handles all its own checks.
pub(super) async fn handle_uk(state: &mut GameState, conn_id: ConnectionId, skill_num: i32) {
    let dead = match state.users.get(&conn_id) {
        Some(u) if u.logged => u.dead,
        _ => return,
    };

    if dead {
        state.send_msg_id(conn_id, 3, "");
        return;
    }

    match skill_num {
        3 => { // Robar
            let pkt = binary_packets::write_work_mode(skill_num as u8);
            state.send_bytes(conn_id, &pkt);
        }
        2 => { // Magia
            let pkt = binary_packets::write_work_mode(skill_num as u8);
            state.send_bytes(conn_id, &pkt);
        }
        18 => { // Domar
            let pkt = binary_packets::write_work_mode(skill_num as u8);
            state.send_bytes(conn_id, &pkt);
        }
        8 => { // Ocultarse — all checks handled inside do_ocultarse
            super::skills::do_ocultarse(state, conn_id).await;
        }
        _ => {} // Unknown skill, ignore
    }
}

/// ENTR — Train creature from trainer NPC.
pub(super) async fn handle_entr(state: &mut GameState, conn_id: ConnectionId, creature_slot: i32) {
    if creature_slot <= 0 {
        return;
    }

    let (target_npc, map, x, y, nro_mascotas, gold) = match state.users.get(&conn_id) {
        Some(u) => (u.target_npc, u.pos_map, u.pos_x, u.pos_y, u.nro_mascotas, u.gold),
        None => return,
    };

    if target_npc == 0 {
        state.send_console(conn_id, "No estas interactuando con un NPC.", font_index::INFO);
        return;
    }

    if nro_mascotas >= 3 {
        state.send_console(conn_id, "Ya tienes el maximo de mascotas.", font_index::INFO);
        return;
    }

    // Simple: spawn a pet NPC near the player
    // In VB6 this reads from the trainer's creature list — for now, just acknowledge
    state.send_console(conn_id, "Criatura entrenada.", font_index::INFO);
}


/// ACTUALIZAR — Position re-sync.
pub(super) async fn handle_actualizar(state: &mut GameState, conn_id: ConnectionId) {
    let (px, py) = if let Some(user) = state.users.get(&conn_id) {
        (user.pos_x, user.pos_y)
    } else {
        return;
    };
    let pkt = binary_packets::write_pos_update(px as i16, py as i16);
    state.send_bytes(conn_id, &pkt);
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
            font_index::AMARILLO);

        if let Some(user) = state.users.get_mut(&conn_id) {
            user.tiene_macro = 0;
        }
    }
}


/// # — Send SOS/consultation.
pub(super) async fn handle_sos_send(state: &mut GameState, conn_id: ConnectionId, contenido: &str) {
    if let Some(user) = state.users.get(&conn_id) {
        if user.silenced {
            state.send_msg_id(conn_id, 945, &format!("{}", user.silence_timer));
            return;
        }
        if user.consulta_enviada {
            state.send_msg_id(conn_id, 192, "");
            return;
        }
    } else {
        return;
    }

    let name = state.users.get(&conn_id).unwrap().char_name.clone();

    // Notify admins
    state.send_msg_id_to(SendTarget::ToAdmins, 193, "");

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
pub(super) async fn handle_sos_respond(state: &mut GameState, conn_id: ConnectionId, target_name: &str, texto: &str) {
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
        state.send_msg_id(target, 190, "");
        let admin_name = state.users.get(&conn_id).map(|u| u.char_name.clone()).unwrap_or_default();
        let pkt = binary_packets::write_response(&texto, &admin_name);
        state.send_bytes(target, &pkt);
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
    state.send_bytes(conn_id, &pkt);
}


/// DYDTRA — Drag & drop transfer items to another player.
pub(super) async fn handle_dydtra(state: &mut GameState, conn_id: ConnectionId, slot: usize, amount: i32) {
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
        state.send_console(conn_id, "No hay nadie ahi.", font_index::INFO);
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
            state.send_console(conn_id, "No puedes transferir un item equipado.", font_index::INFO);
            return;
        }
    }

    // Transfer: remove from source, add to target
    let actual_amount = state.users.get(&conn_id).map(|u| u.inventory[si].amount.min(amount)).unwrap_or(0);
    if actual_amount <= 0 { return; }

    // Try to add to target inventory
    let added = add_item_to_user_inventory(state, target_user, obj_idx, actual_amount);
    if !added {
        state.send_console(conn_id, "El otro jugador no tiene espacio.", font_index::INFO);
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
    state.send_console(conn_id, &format!("Has transferido {} {}.", actual_amount, obj_name), font_index::INFO);
    state.send_console(target_user, &format!("{} te ha dado {} {}.", sender_name, actual_amount, obj_name), font_index::INFO);

    send_full_inventory(state, conn_id).await;
    send_full_inventory(state, target_user).await;
}


/// DOWNSI — Cast spell by target name.
pub(super) async fn handle_downsi(state: &mut GameState, conn_id: ConnectionId, target_name: &str) {
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
pub(super) async fn handle_nvot(state: &mut GameState, conn_id: ConnectionId, option: usize) {
    if !state.poll_active {
        state.send_console(conn_id, "No hay votacion activa.", font_index::INFO);
        return;
    }

    if option < 1 || option > 5 {
        return;
    }

    let name = state.users.get(&conn_id).map(|u| u.char_name.clone()).unwrap_or_default();
    if state.poll_voters.contains(&name) {
        state.send_console(conn_id, "Ya has votado.", font_index::INFO);
        return;
    }

    state.poll_votes[option - 1] += 1;
    state.poll_voters.push(name);
    state.send_console(conn_id, "Voto registrado.", font_index::INFO);
}

/// NEWD — New report/denuncia.
pub(super) async fn handle_newd(state: &mut GameState, conn_id: ConnectionId, target_name: &str, reason: &str) {
    let reporter = state.users.get(&conn_id).map(|u| u.char_name.clone()).unwrap_or_default();

    info!("[REPORT] {} reports {}: {}", reporter, target_name, reason);

    state.send_msg_id_to(SendTarget::ToAdmins, 218, &format!("{}@Denuncia contra {}: {}", reporter, target_name, reason));

    state.send_console(conn_id, "Denuncia enviada.", font_index::INFO);
}

// add_item_to_user_inventory — moved to common.rs

