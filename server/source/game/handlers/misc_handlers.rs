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
use crate::data::ranking;
use super::common::*;
use super::{
    warp_user, revive_user, send_inventory_slot, send_full_inventory,
    check_user_level, send_full_spells, build_anm_packet,
    make_user_visible, llevar_usuarios_cvc,
    remove_pet_from_owner, handle_drop_item, do_cast_spell, do_ocultarse,
    is_cvc_eligible, CVC_COST, CVC_MAP,
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
        format!("{},{},{},{},{},{},{},{},{},{},{},{},{},{},{},{},{}",
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
            target.puntos_torneo,
            target.puntos_donacion,
            target.quests_completed,
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
        format!("{},{},{},{},{},{},{},{},{},{},{}",
            user.criminales_matados,
            user.ciudadanos_matados,
            user.level,
            user.class,
            if user.criminal { "Criminal" } else { "Ciudadano" },
            user.puntos_torneo,
            user.puntos_donacion,
            user.ts_points,
            user.quests_completed,
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

/// ACTPT — Send tournament/donation points.
pub(super) async fn handle_actpt(state: &mut GameState, conn_id: ConnectionId) {
    let info = if let Some(user) = state.users.get(&conn_id) {
        format!("{},{},{}", user.puntos_torneo, user.puntos_donacion, user.ts_points)
    } else {
        return;
    };
    let pkt = binary_packets::write_auction_list(&info);
    state.send_bytes(conn_id, &pkt).await;
}

/// RANKIN — View rankings.
pub(super) async fn handle_rankin(state: &mut GameState, conn_id: ConnectionId, data: &str) {
    let payload = strip_opcode(data, 6);
    let category = read_field(1, payload, ',').to_uppercase();

    let rank_type = match category.as_str() {
        "DUELOS" => 3,
        "PAREJAS" => 4,
        "RONDAS" => 6,
        "REPUTACION" => 5,
        "TORNEOS" => 2,
        "CVCS" => 7,
        "CASTILLOS" => 8,
        "REPUCLANES" => 9,
        "FRAGS" => 1,
        _ => return,
    };

    use crate::data::ranking::RankingType;
    let ranking_type = match RankingType::from_i32(rank_type) {
        Some(r) => r,
        None => return,
    };

    let top = state.ranking.get(ranking_type);
    let mut result = String::new();
    for i in 0..10 {
        let entry = &top.entries[i];
        if !entry.name.is_empty() {
            result.push_str(&format!("{}-{},", entry.name, entry.value));
        } else {
            result.push_str("N/A-0,");
        }
    }

    let pkt = binary_packets::write_mini_top_data(&result);
    state.send_bytes(conn_id, &pkt).await;
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

/// IDUELOS — Duel arena info.
pub(super) async fn handle_iduelos(state: &mut GameState, conn_id: ConnectionId) {
    let mut msg = String::new();
    for i in 1..=8 {
        if i > 1 { msg.push(','); }
        msg.push_str(&state.nombre_dueleando[i]);
    }
    let pkt = binary_packets::write_arena_data(&msg);
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

/// FINCBN — Close guild bank.
pub(super) async fn handle_fincbn(state: &mut GameState, conn_id: ConnectionId) {
    if let Some(user) = state.users.get_mut(&conn_id) {
        user.comerciando = false;
        user.cuenta_bancaria.clear();
    }
    let pkt = binary_packets::write_bank_close_ok();
    state.send_bytes(conn_id, &pkt).await;
}

/// VLKG — Query guild bank permissions for a player.
/// VB6: TCP_HandleData1.bas Case "VLKG". Returns KHEKD<canObj>,<canGold>.
pub(super) async fn handle_vlkg(state: &mut GameState, conn_id: ConnectionId, data: &str) {
    let nick = strip_opcode(data, 4).trim().to_string();
    if nick.is_empty() { return; }

    // Try online first
    if let Some(target_conn) = state.find_user_by_name(&nick) {
        let (can_obj, can_gold) = match state.users.get(&target_conn) {
            Some(u) => (u.puede_retirar_obj, u.puede_retirar_oro),
            None => (false, false),
        };
        let pkt = binary_packets::write_guild_bank_perms(can_obj, can_gold);
        state.send_bytes(conn_id, &pkt).await;
    } else {
        // Offline — not tracked in DB (runtime-only flags), return defaults
        let pkt = binary_packets::write_guild_bank_perms(false, false);
        state.send_bytes(conn_id, &pkt).await;
    }
}

/// BOVC — Set guild bank permissions for a player.
/// VB6: TCP_HandleData1.bas Case "BOVC". Format: BOVC<nick>,<permLevel>
/// permLevel: 0=none, 1=gold only, 2=items only, 3=both
pub(super) async fn handle_bovc(state: &mut GameState, conn_id: ConnectionId, data: &str) {
    let payload = strip_opcode(data, 4);
    let parts: Vec<&str> = payload.splitn(2, ',').collect();
    if parts.len() < 2 { return; }
    let nick = parts[0].trim();
    let perm: i32 = parts[1].trim().parse().unwrap_or(-1);
    if perm < 0 || perm > 3 { return; }

    // Must be guild leader
    let (char_name, guild_index) = match state.users.get(&conn_id) {
        Some(u) if u.logged && u.guild_index > 0 => (u.char_name.clone(), u.guild_index),
        _ => return,
    };
    let guild = match crate::db::guilds::load_guild(&state.pool, guild_index).await {
        Some(g) => g,
        None => return,
    };
    if guild.leader.to_uppercase() != char_name.to_uppercase() {
        state.send_msg_id(conn_id, 265, "").await;
        return;
    }

    let can_obj = perm == 2 || perm == 3;
    let can_gold = perm == 1 || perm == 3;

    // Try online first
    if let Some(target_conn) = state.find_user_by_name(nick) {
        if let Some(u) = state.users.get_mut(&target_conn) {
            u.puede_retirar_obj = can_obj;
            u.puede_retirar_oro = can_gold;
        }
    } else {
        // Offline — these flags are runtime-only, not persisted
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

/// DCANJE — Donation exchange menu.
pub(super) async fn handle_dcanje(state: &mut GameState, conn_id: ConnectionId) {
    // Donation system — send basic info
    let pts = state.users.get(&conn_id).map(|u| u.puntos_donacion).unwrap_or(0);
    // Send empty donation list (no donations configured in this server)
    let msg = format!("{},0,", pts);
    let pkt = binary_packets::write_quest_list_data(&msg);
    state.send_bytes(conn_id, &pkt).await;
}

/// DPX — Donation item preview.
pub(super) async fn handle_dpx(state: &mut GameState, conn_id: ConnectionId, data: &str) {
    let payload = strip_opcode(data, 3);
    let _item_id: i32 = read_field(1, payload, ',').parse().unwrap_or(0);
    // Donation system not fully implemented — send empty preview
    let pkt = binary_packets::write_quest_selected_data("0,0,0,0,0,0,0,0,Sin descripcion,0,");
    state.send_bytes(conn_id, &pkt).await;
}

/// DRX — Redeem donation.
pub(super) async fn handle_drx(state: &mut GameState, conn_id: ConnectionId, data: &str) {
    let payload = strip_opcode(data, 3);
    let _item_id: i32 = read_field(1, payload, ',').parse().unwrap_or(0);
    state.send_msg_id(conn_id, 632, "").await; // "No tienes suficientes puntos"
}

/// CCANJE — Tournament prize menu.
/// Sends PRM<count>,<name1>,...,<nameN>,<torneo_pts>,<ts_pts>
pub(super) async fn handle_ccanje(state: &mut GameState, conn_id: ConnectionId) {
    let (torneo_pts, ts_pts) = match state.users.get(&conn_id) {
        Some(user) => (user.puntos_torneo, user.ts_points),
        None => return,
    };

    let prizes = &state.game_data.prizes;
    let count = prizes.len();
    // Build: <count>,<name1>,<name2>,...,<nameN>,<torneo_pts>,<ts_pts>
    let mut data = format!("{}", count);
    for prize in prizes {
        data.push(',');
        data.push_str(&prize.name);
    }
    data.push_str(&format!(",{},{}", torneo_pts, ts_pts));
    let pkt = binary_packets::write_quest_current_data(&data);
    state.send_bytes(conn_id, &pkt).await;
}

/// IPX — Prize item info request.
/// Client sends IPX<n> (1-indexed). Server responds with:
/// INF<require>,<atkMax>,<atkMin>,<defMax>,<defMin>,<atkMagMax>,<atkMagMin>,<defMagMax>,<defMagMin>,<desc>,<player_pts>,<grhindex>
pub(super) async fn handle_ipx(state: &mut GameState, conn_id: ConnectionId, data: &str) {
    let payload = strip_opcode(data, 3); // Strip "IPX"
    let idx: usize = payload.parse().unwrap_or(0);

    let player_pts = match state.users.get(&conn_id) {
        Some(user) => user.puntos_torneo,
        None => return,
    };

    let prizes = &state.game_data.prizes;
    if idx < 1 || idx > prizes.len() {
        let pkt = binary_packets::write_quest_current_data("0,0,0,0,0,0,0,0,0,Premio invalido.,0,0");
        state.send_bytes(conn_id, &pkt).await;
        return;
    }

    let prize = &prizes[idx - 1];

    // Look up GrhIndex from ObjData
    let grh_index = if prize.obj_index >= 1 {
        state.game_data.objects.get((prize.obj_index - 1) as usize)
            .map(|o| o.grh_index).unwrap_or(0)
    } else {
        0
    };

    let data = format!(
        "{},{},{},{},{},{},{},{},{},{},{},{}",
        prize.require,
        prize.atk_max, prize.atk_min,
        prize.def_max, prize.def_min,
        prize.atk_mag_max, prize.atk_mag_min,
        prize.def_mag_max, prize.def_mag_min,
        prize.description,
        player_pts,
        grh_index,
    );
    let pkt = binary_packets::write_quest_current_data(&data);
    state.send_bytes(conn_id, &pkt).await;
}

/// SPX — Buy tournament prize.
/// Client sends SPX<n>,<qty> where n is 1-indexed prize and qty is amount.
pub(super) async fn handle_spx(state: &mut GameState, conn_id: ConnectionId, data: &str) {
    let payload = strip_opcode(data, 3); // Strip "SPX"
    let idx: usize = read_field(1, payload, ',').parse().unwrap_or(0);
    let qty: i32 = read_field(2, payload, ',').parse().unwrap_or(1);

    if qty < 1 { return; }

    let prizes = &state.game_data.prizes;
    if idx < 1 || idx > prizes.len() {
        state.send_console(conn_id, "Premio invalido.", font_index::INFO).await;
        return;
    }

    let prize_name = prizes[idx - 1].name.clone();
    let prize_obj_index = prizes[idx - 1].obj_index;
    let prize_require = prizes[idx - 1].require as i64;
    let total_cost = prize_require * qty as i64;

    // Check player has enough points
    let user_pts = match state.users.get(&conn_id) {
        Some(user) => user.puntos_torneo,
        None => return,
    };

    if user_pts < total_cost {
        // ||245@<qty>@<item_name> — insufficient points
        state.send_msg_id(conn_id, 245, &format!("{}@{}", qty, prize_name)).await;
        return;
    }

    // Try to add item to inventory
    if !add_item_to_user_inventory(state, conn_id, prize_obj_index, qty) {
        state.send_msg_id(conn_id, 108, "").await; // No inventory space
        return;
    }

    // Deduct points
    if let Some(user) = state.users.get_mut(&conn_id) {
        user.puntos_torneo -= total_cost;
    }

    // Send updated inventory
    send_full_inventory(state, conn_id).await;

    // ||232@<qty>@<item_name> — purchase success
    state.send_msg_id(conn_id, 232, &format!("{}@{}", qty, prize_name)).await;

    // Send updated points (APT packet)
    if let Some(user) = state.users.get(&conn_id) {
        let data = format!("{},{},{}", user.puntos_torneo, user.puntos_donacion, user.ts_points);
        let pkt = binary_packets::write_auction_list(&data);
        state.send_bytes(conn_id, &pkt).await;
    }
}

/// INCHAT — Init chat with friend.
pub(super) async fn handle_inchat(state: &mut GameState, conn_id: ConnectionId, data: &str) {
    let payload = strip_opcode(data, 6);
    let friend_slot: usize = payload.trim().parse().unwrap_or(0);

    let friend_name = if let Some(user) = state.users.get(&conn_id) {
        if friend_slot >= 1 && friend_slot <= 10 {
            user.nombre_amigo[friend_slot - 1].clone()
        } else {
            String::new()
        }
    } else {
        return;
    };

    if friend_name.is_empty() || friend_name.to_uppercase() == "(NADIE)" {
        state.send_msg_id(conn_id, 226, "").await;
        return;
    }

    let pkt = binary_packets::write_enchat(&friend_name);
    state.send_bytes(conn_id, &pkt).await;
}

/// KKCHAT — Send chat message to friend.
pub(super) async fn handle_kkchat(state: &mut GameState, conn_id: ConnectionId, data: &str) {
    let payload = strip_opcode(data, 6);
    let target_name = read_field(1, payload, ',');
    let text = read_field(2, payload, ',');

    if target_name.is_empty() || text.is_empty() {
        return;
    }

    let sender_name = state.users.get(&conn_id).map(|u| u.char_name.clone()).unwrap_or_default();

    // Find target online
    if let Some(&target_conn) = state.online_names.get(&target_name.to_uppercase()) {
        let pkt = binary_packets::write_irchat(&sender_name, &text);
        state.send_bytes(target_conn, &pkt).await;
    } else {
        state.send_console(conn_id, "El usuario no esta online.", font_index::INFO).await;
    }
}

/// ADDPTS — Donate tournament points to guild.
pub(super) async fn handle_addpts(state: &mut GameState, conn_id: ConnectionId, data: &str) {
    let payload = strip_opcode(data, 6);
    let amount: i64 = payload.trim().parse().unwrap_or(0);

    if amount <= 0 {
        return;
    }

    let guild_idx = state.users.get(&conn_id).map(|u| u.guild_index).unwrap_or(0);
    if guild_idx <= 0 {
        state.send_console(conn_id, "No perteneces a un clan.", font_index::INFO).await;
        return;
    }

    if let Some(user) = state.users.get(&conn_id) {
        if user.puntos_torneo < amount {
            state.send_console(conn_id, "No tienes suficientes puntos.", font_index::INFO).await;
            return;
        }
    }

    if let Some(user) = state.users.get_mut(&conn_id) {
        user.puntos_torneo -= amount;
    }

    state.send_console(conn_id, &format!("Has donado {} puntos al clan.", amount), font_index::INFO).await;
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

/// TOINFO — Tournament info.
pub(super) async fn handle_toinfo(state: &mut GameState, conn_id: ConnectionId) {
    let mut list = String::new();
    for name in &state.cronologia_participantes {
        list.push_str(name);
        list.push(',');
    }
    let pkt = binary_packets::write_friend_list(&list);
    state.send_bytes(conn_id, &pkt).await;
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

/// /GUERRA — Join war event. VB6: TCP_HandleData2.bas:430-454.
/// Warps player to the war zone based on faction (Alianza/Horda).
pub(super) async fn handle_slash_guerra(state: &mut GameState, conn_id: ConnectionId) {
    if !state.hay_guerra {
        state.send_msg_id(conn_id, 322, "").await;
        return;
    }

    let (armada, caos, jerarquia, cur_map_pk, en_guerra) = match state.users.get(&conn_id) {
        Some(u) if u.logged => {
            let map_pk = state.game_data.maps.get(u.pos_map as usize)
                .and_then(|m| m.as_ref()).map(|m| m.info.pk).unwrap_or(false);
            (u.armada_real, u.fuerzas_caos, u.jerarquia_dios, map_pk, u.en_guerra)
        },
        _ => return,
    };

    // Must be in a faction with hierarchy
    if jerarquia < 1 && !armada && !caos {
        state.send_msg_id(conn_id, 324, "").await;
        return;
    }

    if cur_map_pk {
        state.send_msg_id(conn_id, 323, "").await;
        return;
    }

    if en_guerra { return; }

    if let Some(user) = state.users.get_mut(&conn_id) {
        user.en_guerra = true;
    }

    // Determine faction: armada_real = Alianza, fuerzas_caos = Horda
    let is_alianza = armada;

    if state.hay_guerra_khalim {
        if is_alianza {
            warp_user(state, conn_id, 1, 21, 30).await;
        } else {
            warp_user(state, conn_id, 27, 50, 78).await;
        }
    } else if state.hay_guerra_anvil {
        if is_alianza {
            warp_user(state, conn_id, 29, 46, 68).await;
        } else {
            warp_user(state, conn_id, 41, 50, 13).await;
        }
    }

    state.send_msg_id(conn_id, 325, "").await;
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

/// /NOBLE — Become noble. VB6: TCP_HandleData2.bas:998-1052.
/// Requires items 1073-1077 (qty 1 each). Grants spell 46. Sets EsNoble flag.
pub(super) async fn handle_slash_noble(state: &mut GameState, conn_id: ConnectionId) {
    // Check all 5 required items
    for obj_id in 1073..=1077 {
        if !user_has_items(state, conn_id, obj_id, 1) {
            state.send_msg_id(conn_id, 356, "").await;
            return;
        }
    }

    let (dead, es_noble, class) = match state.users.get(&conn_id) {
        Some(u) if u.logged => (u.dead, u.es_noble, u.class.clone()),
        _ => return,
    };

    if dead {
        state.send_msg_id(conn_id, 3, "").await;
        return;
    }

    if es_noble { return; }

    // Consume all 5 items
    for obj_id in 1073..=1077 {
        remove_items_from_inv(state, conn_id, obj_id, 1).await;
    }

    // Grant spell 46
    let spell_idx = 46i32;
    let already_has = state.users.get(&conn_id).map(|u| {
        u.spells.iter().any(|&s| s == spell_idx)
    }).unwrap_or(false);

    if !already_has {
        let empty_slot = state.users.get(&conn_id).and_then(|u| {
            u.spells.iter().position(|&s| s == 0)
        });
        if let Some(slot) = empty_slot {
            if let Some(user) = state.users.get_mut(&conn_id) {
                user.spells[slot] = spell_idx;
                user.es_noble = true;
            }
            // Send spell update for the slot
            let spell_name = state.game_data.spells.get(spell_idx as usize)
                .map(|s| s.nombre.as_str()).unwrap_or("Desconocido");
            let pkt = binary_packets::write_change_spell_slot((slot + 1) as u8, spell_idx as i16, spell_name);
            state.send_bytes(conn_id, &pkt).await;
        } else {
            state.send_msg_id(conn_id, 181, "").await; // No spell slots
            if let Some(user) = state.users.get_mut(&conn_id) {
                user.es_noble = true;
            }
        }
    } else {
        state.send_msg_id(conn_id, 182, "").await; // Already has spell
        if let Some(user) = state.users.get_mut(&conn_id) {
            user.es_noble = true;
        }
    }

    let name = state.users.get(&conn_id).map(|u| u.char_name.clone()).unwrap_or_default();
    state.send_msg_id(conn_id, 357, &format!("{}", class)).await;
    state.send_msg_id_to(SendTarget::ToAll, 358, &format!("{}", name)).await;
}

/// /DESENTERRAR — Dig up treasure. VB6: TCP_HandleData2.bas:1054-1072 + modTesoros.bas.
/// Must be at exact treasure coords AND have LlaveTesoro (obj 1062).
pub(super) async fn handle_slash_desenterrar(state: &mut GameState, conn_id: ConnectionId) {
    let (map, x, y) = match state.users.get(&conn_id) {
        Some(u) if u.logged => (u.pos_map, u.pos_x, u.pos_y),
        _ => return,
    };

    // Check if player is at treasure location
    if map != state.tesoro_map || x != state.tesoro_x || y != state.tesoro_y || state.tesoro_map == 0 {
        state.send_msg_id(conn_id, 359, "").await;
        return;
    }

    // Check for LlaveTesoro (obj 1062)
    const LLAVE_TESORO: i32 = 1062;
    if !user_has_items(state, conn_id, LLAVE_TESORO, 1) {
        state.send_msg_id(conn_id, 360, "").await;
        return;
    }

    // Consume key
    remove_items_from_inv(state, conn_id, LLAVE_TESORO, 1).await;

    // Start treasure countdown
    state.tesoro_contando = true;
    state.tesoro_tiempo = 30;

    // Spawn Cofre Cerrado (obj 11) on the map tile
    const COFRE_CERRADO: i32 = 11;
    let t_map = state.tesoro_map;
    let t_x = state.tesoro_x;
    let t_y = state.tesoro_y;
    let grh = state.get_object(COFRE_CERRADO).map(|o| o.grh_index).unwrap_or(0);
    {
        let grid = state.world.grid_mut(t_map);
        if let Some(tile) = grid.tile_mut(t_x, t_y) {
            tile.ground_item.obj_index = COFRE_CERRADO;
            tile.ground_item.amount = 1;
        }
    }
    // Notify area about the new object
    let obj_pkt = binary_packets::write_object_create(t_x as u8, t_y as u8, grh as i16);
    state.send_data_bytes(SendTarget::ToArea { map: t_map, x: t_x, y: t_y }, &obj_pkt).await;

    state.send_msg_id(conn_id, 361, "").await;
}

/// /BOTIX — Spawn AI bot.
pub(super) async fn handle_slash_botix(state: &mut GameState, conn_id: ConnectionId) {
    state.send_console(conn_id, "Sistema de bots no disponible.", font_index::INFO).await;
}

/// /INFOSUB — Show current auction info. VB6 TCP_HandleData2.bas
pub(super) async fn handle_slash_infosub(state: &mut GameState, conn_id: ConnectionId) {
    match &state.auction {
        Some(auction) => {
            let obj_name = state.get_object(auction.obj_index)
                .map(|o| o.name.clone())
                .unwrap_or_else(|| format!("obj#{}", auction.obj_index));
            let msg = format!(
                "Subasta: {} x{} | Puja minima: {} | Puja actual: {} ({}) | Tiempo: {}s",
                obj_name, auction.amount,
                auction.min_gold, auction.current_bid,
                if auction.bidder_name.is_empty() { "nadie" } else { &auction.bidder_name },
                auction.timer
            );
            state.send_console(conn_id, &msg, font_index::INFO).await;
        }
        None => {
            state.send_console(conn_id, "No hay subasta activa.", font_index::INFO).await;
        }
    }
}

/// /SUBASTAR — Check if auction active, send INITSUB to open auction UI. VB6 TCP_HandleData2.bas:279
pub(super) async fn handle_slash_subastar(state: &mut GameState, conn_id: ConnectionId) {
    if state.auction.is_some() {
        // VB6: Send ||314 (there's already an auction)
        state.send_msg_id(conn_id, 314, "").await;
    } else {
        // VB6: Send INITSUB packet to open the auction creation UI
        let pkt = binary_packets::write_auction_init();
        state.send_bytes(conn_id, &pkt).await;
    }
}

/// /INISUB <item>@<qty>@<gold> — Start auction. VB6 TCP_HandleData3.bas:425
/// Removes item from inventory, broadcasts auction to all. 4-min timer.
pub(super) async fn handle_slash_inisub(state: &mut GameState, conn_id: ConnectionId, args: &str) {
    if state.auction.is_some() {
        state.send_msg_id(conn_id, 314, "").await; // Already an auction active
        return;
    }

    let parts: Vec<&str> = args.split('@').collect();
    if parts.len() < 3 {
        state.send_console(conn_id, "Uso: /INISUB slot@cantidad@oro_minimo", font_index::INFO).await;
        return;
    }

    let slot: usize = parts[0].trim().parse().unwrap_or(0);
    let qty: i32 = parts[1].trim().parse().unwrap_or(0);
    let min_gold: i64 = parts[2].trim().parse().unwrap_or(0);

    if slot < 1 || slot > MAX_INVENTORY_SLOTS || qty < 1 || min_gold < 1000 {
        state.send_console(conn_id, "Parametros invalidos. Oro minimo: 1000.", font_index::INFO).await;
        return;
    }

    let (dead, logged) = match state.users.get(&conn_id) {
        Some(u) => (u.dead, u.logged),
        None => return,
    };
    if dead || !logged { return; }

    // Validate item in slot
    let (obj_idx, obj_amount) = match state.users.get(&conn_id) {
        Some(u) => {
            let s = &u.inventory[slot - 1];
            (s.obj_index, s.amount)
        }
        None => return,
    };

    if obj_idx <= 0 || obj_amount < qty {
        state.send_console(conn_id, "No tenes suficientes items en ese slot.", font_index::INFO).await;
        return;
    }

    // Remove items from inventory
    if let Some(u) = state.users.get_mut(&conn_id) {
        u.inventory[slot - 1].amount -= qty;
        if u.inventory[slot - 1].amount <= 0 {
            u.inventory[slot - 1].obj_index = 0;
            u.inventory[slot - 1].amount = 0;
        }
    }
    send_inventory_slot(state, conn_id, slot).await;

    let char_name = state.users.get(&conn_id).map(|u| u.char_name.clone()).unwrap_or_default();

    // Create auction state
    state.auction = Some(crate::game::types::AuctionState {
        auctioneer: conn_id,
        obj_index: obj_idx,
        amount: qty,
        min_gold,
        current_bid: 0,
        bidder: 0,
        bidder_name: String::new(),
        timer: 240, // 4 minutes
    });

    // Broadcast auction start: ||528@name@obj@qty@gold
    let obj_name = state.get_object(obj_idx)
        .map(|o| o.name.clone())
        .unwrap_or_else(|| format!("obj#{}", obj_idx));
    state.send_msg_id_to(SendTarget::ToAll, 528, &format!("{}@{}@{}@{}", char_name, obj_name, qty, min_gold)).await;
}

/// /OFRECER <amount> — Bid on active auction. VB6 TCP_HandleData3.bas:489
pub(super) async fn handle_slash_ofrecer(state: &mut GameState, conn_id: ConnectionId, args: &str) {
    let bid: i64 = args.trim().parse().unwrap_or(0);
    if bid < 1 {
        state.send_console(conn_id, "Uso: /OFRECER cantidad", font_index::INFO).await;
        return;
    }

    let auction = match &state.auction {
        Some(a) => a.clone(),
        None => {
            state.send_console(conn_id, "No hay subasta activa.", font_index::INFO).await;
            return;
        }
    };

    // VB6: Can't bid on own auction
    if auction.auctioneer == conn_id {
        state.send_console(conn_id, "No podes pujar en tu propia subasta.", font_index::INFO).await;
        return;
    }

    // VB6: Can't bid if you're already the highest bidder
    if auction.bidder == conn_id {
        state.send_console(conn_id, "Ya sos el mayor postor.", font_index::INFO).await;
        return;
    }

    // Must exceed current bid (or minimum if no bids)
    let min_required = if auction.current_bid > 0 { auction.current_bid + 1 } else { auction.min_gold };
    if bid < min_required {
        state.send_console(conn_id, &format!("La puja minima es {}.", min_required), font_index::INFO).await;
        return;
    }

    // Check gold
    let my_gold = state.users.get(&conn_id).map(|u| u.gold).unwrap_or(0);
    if my_gold < bid {
        state.send_console(conn_id, "No tenes suficiente oro.", font_index::INFO).await;
        return;
    }

    // Refund previous bidder
    if auction.bidder > 0 && auction.current_bid > 0 {
        if let Some(u) = state.users.get_mut(&auction.bidder) {
            u.gold += auction.current_bid;
        }
        send_stats_gold(state, auction.bidder).await;
        state.send_console(auction.bidder, &format!("Tu puja fue superada. Se te devolvieron {} monedas.", auction.current_bid), font_index::INFO).await;
    }

    // Deduct gold from new bidder
    if let Some(u) = state.users.get_mut(&conn_id) {
        u.gold -= bid;
    }
    send_stats_gold(state, conn_id).await;

    let bidder_name = state.users.get(&conn_id).map(|u| u.char_name.clone()).unwrap_or_default();

    // Update auction
    if let Some(a) = state.auction.as_mut() {
        a.current_bid = bid;
        a.bidder = conn_id;
        a.bidder_name = bidder_name.clone();
    }

    // Broadcast new bid: ||534@name@amount
    state.send_msg_id_to(SendTarget::ToAll, 534, &format!("{}@{}", bidder_name, bid)).await;
}

// =====================================================================
// Missing VB6 parity handlers — Phase 2
// =====================================================================

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

/// GEMS — Gem exchange (requires all 7 gems: items 406-413).
pub(super) async fn handle_gems(state: &mut GameState, conn_id: ConnectionId, data: &str) {
    let payload = strip_opcode(data, 4);
    let option: i32 = payload.trim().parse().unwrap_or(-1);

    // Check all 7 gems (407 included implicitly in VB6 removal)
    let gem_ids = [406, 407, 408, 409, 410, 411, 412, 413];
    for &gid in &gem_ids {
        if !user_has_items(state, conn_id, gid, 1) {
            state.send_msg_id(conn_id, 271, "").await;
            return;
        }
    }

    match option {
        // 0 = Renounce god
        0 => {
            let god = state.users.get(&conn_id).map(|u| u.sirviente_de_dios.to_uppercase()).unwrap_or_default();
            if god == "MIFRIT" || god == "POSEIDON" || god == "EREBROS" || god == "TARRASKE" {
                remove_items_from_inv(state, conn_id, 1274, 1).await;
                if let Some(user) = state.users.get_mut(&conn_id) {
                    user.sirviente_de_dios.clear();
                    user.almas_contenidas = 0;
                    user.almas_ofrecidas = 0;
                    user.cofre_dios = [0; 4];
                    user.cofre_dios_cant = 0;
                    // Remove god items from inventory
                    let items_to_remove: Vec<(i32, i32)> = user.inventory.iter()
                        .filter(|s| s.obj_index > 0 && s.amount > 0)
                        .filter(|s| {
                            state.game_data.objects.get(s.obj_index as usize)
                                .map(|o| o.item_dios).unwrap_or(false)
                        })
                        .map(|s| (s.obj_index, s.amount))
                        .collect();
                    drop(user); // Release borrow for remove
                    for (idx, amt) in items_to_remove {
                        remove_items_from_inv(state, conn_id, idx, amt).await;
                    }
                }
                state.send_msg_id(conn_id, 275, "").await;
            } else {
                state.send_msg_id(conn_id, 276, "").await;
                return; // Don't remove gems
            }
        }
        // 1 = Octarina gem
        1 => {
            if !add_item_to_user_inventory(state, conn_id, 1448, 1) {
                state.send_msg_id(conn_id, 108, "").await;
                return;
            }
            state.send_msg_id(conn_id, 232, "1@Gema Octarina").await;
        }
        // 2 = 1500 tournament points
        2 => {
            if let Some(user) = state.users.get_mut(&conn_id) {
                user.puntos_torneo += 1500;
            }
            state.send_msg_id(conn_id, 57, "1.500").await;
        }
        // 3 = 30000 souls
        3 => {
            if !user_has_items(state, conn_id, 1274, 1) {
                state.send_msg_id(conn_id, 127, "").await;
                return;
            }
            if let Some(user) = state.users.get_mut(&conn_id) {
                user.almas_contenidas += 30000;
            }
            state.send_msg_id(conn_id, 274, "30.000").await;
        }
        // 4 = Fragment
        4 => {
            if !add_item_to_user_inventory(state, conn_id, 1272, 1) {
                state.send_msg_id(conn_id, 108, "").await;
            }
            state.send_msg_id(conn_id, 277, "").await;
        }
        _ => return, // Invalid option, don't remove gems
    }

    // Remove all gems
    for &gid in &gem_ids {
        remove_items_from_inv(state, conn_id, gid, 1).await;
    }
    send_full_inventory(state, conn_id).await;
}

/// GEPS — Medal exchange (item 1025 = medal).
pub(super) async fn handle_geps(state: &mut GameState, conn_id: ConnectionId, data: &str) {
    let payload = strip_opcode(data, 4);
    let option: i32 = payload.trim().parse().unwrap_or(-1);

    match option {
        // 0 = Random gem (8 medals)
        0 => {
            if !user_has_items(state, conn_id, 1025, 8) {
                state.send_msg_id(conn_id, 278, "").await;
                return;
            }
            let gem_idx = 406 + (rand_simple_u32() % 6) as i32; // 406-411
            if !add_item_to_user_inventory(state, conn_id, gem_idx, 1) {
                state.send_msg_id(conn_id, 108, "").await;
                return;
            }
            let name = state.game_data.objects.get(gem_idx as usize).map(|o| o.name.clone()).unwrap_or_default();
            state.send_msg_id(conn_id, 232, &format!("1@{}", name)).await;
            remove_items_from_inv(state, conn_id, 1025, 8).await;
        }
        // 1 = Sacris (1 medal)
        1 => {
            if !user_has_items(state, conn_id, 1025, 1) {
                state.send_msg_id(conn_id, 278, "").await;
                return;
            }
            if !add_item_to_user_inventory(state, conn_id, 936, 1) {
                state.send_msg_id(conn_id, 108, "").await;
                return;
            }
            let name = state.game_data.objects.get(936usize).map(|o| o.name.clone()).unwrap_or_default();
            state.send_msg_id(conn_id, 232, &format!("1@{}", name)).await;
            remove_items_from_inv(state, conn_id, 1025, 1).await;
        }
        // 2 = 150 tournament points (1 medal)
        2 => {
            if !user_has_items(state, conn_id, 1025, 1) {
                state.send_msg_id(conn_id, 278, "").await;
                return;
            }
            if let Some(user) = state.users.get_mut(&conn_id) {
                user.puntos_torneo += 150;
            }
            state.send_msg_id(conn_id, 57, "150").await;
            remove_items_from_inv(state, conn_id, 1025, 1).await;
        }
        // 3 = 5000 souls (6 medals)
        3 => {
            if !user_has_items(state, conn_id, 1274, 1) {
                state.send_msg_id(conn_id, 127, "").await;
                return;
            }
            if !user_has_items(state, conn_id, 1025, 6) {
                state.send_msg_id(conn_id, 278, "").await;
                return;
            }
            if let Some(user) = state.users.get_mut(&conn_id) {
                user.almas_contenidas += 5000;
            }
            state.send_msg_id(conn_id, 274, "5.000").await;
            remove_items_from_inv(state, conn_id, 1025, 6).await;
        }
        // 4 = Item 1512 (2 medals)
        4 => {
            if !user_has_items(state, conn_id, 1025, 2) {
                state.send_msg_id(conn_id, 278, "").await;
                return;
            }
            if !add_item_to_user_inventory(state, conn_id, 1512, 1) {
                state.send_msg_id(conn_id, 108, "").await;
                return;
            }
            let name = state.game_data.objects.get(1512usize).map(|o| o.name.clone()).unwrap_or_default();
            state.send_msg_id(conn_id, 232, &format!("1@{}", name)).await;
            remove_items_from_inv(state, conn_id, 1025, 2).await;
        }
        // 5 = Item 1513 (3 medals)
        5 => {
            if !user_has_items(state, conn_id, 1025, 3) {
                state.send_msg_id(conn_id, 278, "").await;
                return;
            }
            if !add_item_to_user_inventory(state, conn_id, 1513, 1) {
                state.send_msg_id(conn_id, 108, "").await;
                return;
            }
            let name = state.game_data.objects.get(1513usize).map(|o| o.name.clone()).unwrap_or_default();
            state.send_msg_id(conn_id, 232, &format!("1@{}", name)).await;
            remove_items_from_inv(state, conn_id, 1025, 3).await;
        }
        _ => return,
    }
    send_full_inventory(state, conn_id).await;
}

/// OFDIOZ — Divine offering (sacrifice souls to a god).
pub(super) async fn handle_ofdioz(state: &mut GameState, conn_id: ConnectionId, data: &str) {
    let payload = strip_opcode(data, 6);
    let cant_almas: i64 = read_field(1, payload, ',').parse().unwrap_or(0);

    if cant_almas <= 0 { return; }

    let current_almas = state.users.get(&conn_id).map(|u| u.almas_contenidas).unwrap_or(0);
    if current_almas < cant_almas {
        let pkt = binary_packets::write_error_msg("No tienes esa cantidad de almas.");
        state.send_bytes(conn_id, &pkt).await;
        return;
    }

    let god_name = state.users.get(&conn_id).map(|u| u.sirviente_de_dios.clone()).unwrap_or_default();
    let map = state.users.get(&conn_id).map(|u| u.pos_map).unwrap_or(0);

    if let Some(user) = state.users.get_mut(&conn_id) {
        user.almas_contenidas -= cant_almas;
        user.almas_ofrecidas += cant_almas;
    }

    state.send_msg_id(conn_id, 230, &format!("{}@{}", cant_almas, god_name)).await;

    // Send PCF (particle effect) based on god
    let pcf_opt = match god_name.as_str() {
        "Mifrit"   => Some(binary_packets::write_particle_create(77, 84, 51, 30)),
        "Poseidon" => Some(binary_packets::write_particle_create(77, 49, 14, 30)),
        "Tarraske" => Some(binary_packets::write_particle_create(77, 16, 51, 30)),
        "Erebros"  => Some(binary_packets::write_particle_create(77, 50, 87, 30)),
        _ => None,
    };
    if let Some(pcf_pkt) = pcf_opt {
        state.send_data_bytes(SendTarget::ToMap(map), &pcf_pkt).await;
    }

    // Check for hierarchical rewards (AlmasNecesarias = 5000 in VB6)
    let almas_necesarias: i64 = 5000;
    let jerarquia = state.users.get(&conn_id).map(|u| u.jerarquia_dios).unwrap_or(0);
    let almas_ofrecidas = state.users.get(&conn_id).map(|u| u.almas_ofrecidas).unwrap_or(0);
    let race = state.users.get(&conn_id).map(|u| u.race.to_uppercase()).unwrap_or_default();
    let class = state.users.get(&conn_id).map(|u| u.class.to_uppercase()).unwrap_or_default();

    let rank_names = ["", "Soldado", "Guerrero", "Caballero", "Campe\u{00F3}n"];
    let new_jerarquia_target = jerarquia + 1;
    let required_almas = almas_necesarias * (jerarquia as i64);

    if jerarquia >= 1 && jerarquia <= 4 && almas_ofrecidas >= required_almas {
        let is_short = race == "ENANO" || race == "GNOMO";
        let file_suffix = if is_short { "Bajos.dat" } else { "Altos.dat" };
        let obj_key = format!("Obj{}", jerarquia);
        let god_path = state.base_path.join("dioses").join(&god_name).join(file_suffix);
        let obj_idx_str = ini_get(&god_path, &class, &obj_key);
        if !obj_idx_str.is_empty() {
            let obj_idx: i32 = obj_idx_str.parse().unwrap_or(0);
            if obj_idx > 0 && !user_has_items(state, conn_id, obj_idx, 1) {
                if add_item_to_user_inventory(state, conn_id, obj_idx, 1) {
                    let rank_name = rank_names.get(jerarquia as usize).unwrap_or(&"");
                    state.send_msg_id(conn_id, 231, &format!("{}@{}", rank_name, god_name)).await;
                    let obj_name = state.game_data.objects.get(obj_idx as usize).map(|o| o.name.clone()).unwrap_or_default();
                    state.send_msg_id(conn_id, 232, &format!("1@{}", obj_name)).await;
                    if let Some(user) = state.users.get_mut(&conn_id) {
                        user.jerarquia_dios = new_jerarquia_target;
                    }
                    send_full_inventory(state, conn_id).await;
                } else {
                    state.send_msg_id(conn_id, 108, "").await;
                }
            }
        }
    }

    // Check for 120000 almas + specific item → remove item 1274
    if almas_ofrecidas >= 120000 {
        let check_item = match god_name.as_str() {
            "Tarraske" => Some(1479),
            "Poseidon" => Some(1477),
            "Mifrit" => Some(1475),
            "Erebros" => Some(1473),
            _ => None,
        };
        if let Some(item_id) = check_item {
            if user_has_items(state, conn_id, item_id, 1) {
                remove_items_from_inv(state, conn_id, 1274, 1).await;
                send_full_inventory(state, conn_id).await;
            }
        }
    }
}

/// FTSPTS — TS points shop.
pub(super) async fn handle_ftspts(state: &mut GameState, conn_id: ConnectionId, data: &str) {
    let payload = strip_opcode(data, 6);
    let ts_index: i32 = read_field(1, payload, ',').parse().unwrap_or(-1);

    let (obj_index, amount, ts_price) = match ts_index {
        0 => (1055, 1, 10),
        1 => (1033, 1, 15),
        2 => (915, 1, 25),
        3 => (1227, 1, 35),
        4 => (1215, 1, 30),
        5 | 6 => (1050, 1, 40),
        7 => (1539, 2, 5),
        8 => (1035, 1, 30),
        9 => (1059, 1, 65),
        10 => (1060, 1, 70),
        11 => (1535, 1, 20),
        _ => return,
    };

    let current_pts = state.users.get(&conn_id).map(|u| u.ts_points).unwrap_or(0);
    if current_pts < ts_price as i64 {
        state.send_msg_id(conn_id, 212, &format!("{}", ts_price)).await;
        return;
    }

    if !add_item_to_user_inventory(state, conn_id, obj_index, amount) {
        state.send_msg_id(conn_id, 108, "").await;
        return;
    }

    let name = state.game_data.objects.get(obj_index as usize).map(|o| o.name.clone()).unwrap_or_default();
    state.send_msg_id(conn_id, 232, &format!("{}@{}", amount, name)).await;
    if let Some(user) = state.users.get_mut(&conn_id) {
        user.ts_points -= ts_price as i64;
    }
    send_full_inventory(state, conn_id).await;
}

/// SPH — Query upgrade item info (Mejorados.dat).
pub(super) async fn handle_sph(state: &mut GameState, conn_id: ConnectionId, data: &str) {
    let payload = strip_opcode(data, 3);
    let mejorados_path = state.base_path.join("dat").join("Mejorados.dat");

    let numero_mejorado = ini_get(&mejorados_path, "ITEMS", payload.trim());

    let num: i32 = numero_mejorado.parse().unwrap_or(0);
    if num <= 0 { return; }

    let section = format!("ITEM{}", num);
    let nombre = ini_get(&mejorados_path, &section, "Nombre");
    let at_min = ini_get(&mejorados_path, &section, "AtaqueMinimo");
    let at_max = ini_get(&mejorados_path, &section, "AtaqueMaximo");
    let def_min = ini_get(&mejorados_path, &section, "DefensaMinima");
    let def_max = ini_get(&mejorados_path, &section, "DefensaMaxima");
    let atm_min = ini_get(&mejorados_path, &section, "AtaqueMagicoMinimo");
    let atm_max = ini_get(&mejorados_path, &section, "AtaqueMagicoMaximo");
    let defm_min = ini_get(&mejorados_path, &section, "DefensaMagicaMinima");
    let defm_max = ini_get(&mejorados_path, &section, "DefensaMagicaMaxima");
    let desc = ini_get(&mejorados_path, &section, "Descripcion");
    let obj_idx: i32 = ini_get(&mejorados_path, &section, "NumObj").parse().unwrap_or(0);
    let grh = state.game_data.objects.get(obj_idx as usize).map(|o| o.grh_index).unwrap_or(0);

    let data = format!("{},{}/{},{}/{},{}/{},{}/{},{},{}",
        nombre, at_min, at_max, def_min, def_max, atm_min, atm_max, defm_min, defm_max, desc, grh);
    let pkt = binary_packets::write_cosmetic_image(&data);
    state.send_bytes(conn_id, &pkt).await;
}

/// SPÉ — Upgrade item (requires octarina gem 1448 + required item).
pub(super) async fn handle_spe(state: &mut GameState, conn_id: ConnectionId, data: &str) {
    let payload = strip_opcode(data, 3);
    let mejorados_path = state.base_path.join("dat").join("Mejorados.dat");

    let numero_mejorado = ini_get(&mejorados_path, "ITEMS", payload.trim());

    let num: i32 = numero_mejorado.parse().unwrap_or(0);
    if num <= 0 { return; }

    let section = format!("ITEM{}", num);
    let requiere: i32 = ini_get(&mejorados_path, &section, "Requiere").parse().unwrap_or(0);
    let obj_idx: i32 = ini_get(&mejorados_path, &section, "NumObj").parse().unwrap_or(0);

    // Need octarina gem (1448)
    if !user_has_items(state, conn_id, 1448, 1) {
        state.send_msg_id(conn_id, 235, "").await;
        return;
    }

    // Need the required item
    if !user_has_items(state, conn_id, requiere, 1) {
        state.send_msg_id(conn_id, 236, "").await;
        return;
    }

    if !add_item_to_user_inventory(state, conn_id, obj_idx, 1) {
        state.send_msg_id(conn_id, 108, "").await;
        return;
    }

    let name = state.game_data.objects.get(requiere as usize).map(|o| o.name.clone()).unwrap_or_default();
    state.send_msg_id(conn_id, 237, &format!("{}", name)).await;
    remove_items_from_inv(state, conn_id, requiere, 1).await;
    remove_items_from_inv(state, conn_id, 1448, 1).await;
    send_full_inventory(state, conn_id).await;
}

/// ARE — Arena spectator (enter arena to watch duel).
pub(super) async fn handle_are(state: &mut GameState, conn_id: ConnectionId, data: &str) {
    let payload = strip_opcode(data, 3);
    let arena_num: i32 = payload.trim().parse().unwrap_or(0);

    // Check if on PK map
    let map = state.users.get(&conn_id).map(|u| u.pos_map).unwrap_or(0);
    let is_pk = if let Some(Some(gm)) = state.game_data.maps.get(map as usize) { gm.info.pk } else { false };
    if is_pk {
        state.send_msg_id(conn_id, 291, "").await;
        return;
    }

    // Need 100k gold
    let gold = state.users.get(&conn_id).map(|u| u.gold).unwrap_or(0);
    if gold < 100000 {
        state.send_msg_id(conn_id, 215, "100.000").await;
        return;
    }

    // Can't be already spectating
    let already = state.users.get(&conn_id).map(|u|
        u.espectador_arena1 || u.espectador_arena2 || u.espectador_arena3 || u.espectador_arena4
    ).unwrap_or(false);
    if already { return; }

    // Can't be in special map, CvC, or dead
    let special = state.users.get(&conn_id).map(|u| u.en_cvc || u.dead).unwrap_or(false);
    if special {
        state.send_msg_id(conn_id, 239, "").await;
        return;
    }

    // Arena spectator positions on map 71
    let (espectadores, max_esp, positions, flag_setter): (i32, i32, Vec<(i32, i32)>, i32) = match arena_num {
        1 => (state.espectadores_arena1, 4, vec![(33,34),(34,34),(33,35),(34,35)], 1),
        2 => (state.espectadores_arena2, 4, vec![(33,68),(34,68),(33,69),(34,69)], 2),
        3 => (state.espectadores_arena3, 4, vec![(69,34),(70,34),(69,35),(70,35)], 3),
        4 => (state.espectadores_arena4, 4, vec![(69,68),(70,68),(69,69),(70,69)], 4),
        _ => return,
    };

    if !state.arena_ocupada[arena_num as usize] || espectadores >= max_esp {
        state.send_msg_id(conn_id, 241, "").await;
        return;
    }

    // Save position
    if let Some(user) = state.users.get_mut(&conn_id) {
        user.mapa_anterior = user.pos_map;
        user.x_anterior = user.pos_x;
        user.y_anterior = user.pos_y;
        match flag_setter {
            1 => user.espectador_arena1 = true,
            2 => user.espectador_arena2 = true,
            3 => user.espectador_arena3 = true,
            4 => user.espectador_arena4 = true,
            _ => {}
        }
    }

    // Find free spectator position
    let pos = positions.first().copied().unwrap_or((33, 34));
    warp_user(state, conn_id, 71, pos.0, pos.1).await;

    match arena_num {
        1 => state.espectadores_arena1 += 1,
        2 => state.espectadores_arena2 += 1,
        3 => state.espectadores_arena3 += 1,
        4 => state.espectadores_arena4 += 1,
        _ => {}
    }

    if let Some(user) = state.users.get_mut(&conn_id) {
        user.gold -= 100000;
    }
    send_stats_gold(state, conn_id).await;
    state.send_msg_id(conn_id, 240, "").await;
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

/// /PAREJA — 2vs2 system.
pub(super) async fn handle_slash_pareja(state: &mut GameState, conn_id: ConnectionId, target_name: &str) {
    let target = state.online_names.get(&target_name.to_uppercase()).copied();

    // Command cooldown
    let cooldown = state.users.get(&conn_id).map(|u| u.time_comandos).unwrap_or(0);
    if cooldown > 0 {
        state.send_msg_id(conn_id, 290, "").await;
        return;
    }
    if let Some(user) = state.users.get_mut(&conn_id) {
        user.time_comandos = 5;
    }

    let target_id = match target {
        Some(id) => id,
        None => {
            state.send_msg_id(conn_id, 196, "").await;
            return;
        }
    };

    if target_id == conn_id { return; }

    // Check gold (300k each)
    let my_gold = state.users.get(&conn_id).map(|u| u.gold).unwrap_or(0);
    if my_gold < 300000 {
        state.send_msg_id(conn_id, 215, "300.000").await;
        return;
    }
    let t_gold = state.users.get(&target_id).map(|u| u.gold).unwrap_or(0);
    if t_gold < 300000 {
        state.send_msg_id(conn_id, 446, "").await;
        return;
    }

    // Check dead, in commerce, in cvc etc
    let my_dead = state.users.get(&conn_id).map(|u| u.dead || u.en_cvc || u.comerciando).unwrap_or(true);
    if my_dead {
        state.send_msg_id(conn_id, 239, "").await;
        return;
    }

    // Check same class
    let my_class = state.users.get(&conn_id).map(|u| u.class.clone()).unwrap_or_default();
    let t_class = state.users.get(&target_id).map(|u| u.class.clone()).unwrap_or_default();
    if my_class == t_class {
        state.send_msg_id(conn_id, 448, "").await;
        return;
    }

    // Check if all 4 slots are full
    if state.pareja[3] > 0 && state.pareja[4] > 0 {
        state.send_msg_id(conn_id, 406, "").await;
        return;
    }

    // Set up pairing
    if let Some(user) = state.users.get_mut(&target_id) {
        user.espera_pareja = true;
    }
    if let Some(user) = state.users.get_mut(&conn_id) {
        user.su_pareja = target_id;
    }

    let my_name = state.users.get(&conn_id).map(|u| u.char_name.clone()).unwrap_or_default();
    let t_name = state.users.get(&target_id).map(|u| u.char_name.clone()).unwrap_or_default();

    // Check if target also wants to pair with us
    let target_wants_me = state.users.get(&target_id).map(|u| u.su_pareja == conn_id).unwrap_or(false);

    if state.pareja[1] == 0 && state.pareja[2] == 0 {
        if !target_wants_me {
            state.send_msg_id(target_id, 449, &format!("{}", my_name)).await;
            return;
        }
        // Form first pair
        state.pareja[1] = conn_id;
        state.pareja[2] = target_id;
        for &pid in &[conn_id, target_id] {
            if let Some(u) = state.users.get_mut(&pid) {
                u.en_pareja = true;
                u.mapa_anterior = u.pos_map;
                u.x_anterior = u.pos_x;
                u.y_anterior = u.pos_y;
            }
        }
        warp_user(state, conn_id, 106, 41, 55).await;
        warp_user(state, target_id, 106, 43, 57).await;
        for &pid in &[conn_id, target_id] {
            if let Some(u) = state.users.get_mut(&pid) {
                u.gold -= 300000;
            }
            send_stats_gold(state, pid).await;
        }
        state.send_msg_id_to(SendTarget::ToAll, 450, &format!("{}@{}", my_name, t_name)).await;
    } else if state.pareja[1] > 0 && state.pareja[2] > 0 {
        if !target_wants_me {
            state.send_msg_id(target_id, 449, &format!("{}", my_name)).await;
            return;
        }
        // Form second pair
        state.pareja[3] = conn_id;
        state.pareja[4] = target_id;
        for &pid in &[conn_id, target_id] {
            if let Some(u) = state.users.get_mut(&pid) {
                u.en_pareja = true;
                u.mapa_anterior = u.pos_map;
                u.x_anterior = u.pos_x;
                u.y_anterior = u.pos_y;
            }
        }
        warp_user(state, state.pareja[1], 106, 41, 55).await;
        warp_user(state, state.pareja[2], 106, 43, 57).await;
        warp_user(state, conn_id, 106, 60, 40).await;
        warp_user(state, target_id, 106, 62, 42).await;
        for &pid in &[conn_id, target_id] {
            if let Some(u) = state.users.get_mut(&pid) {
                u.gold -= 300000;
            }
            send_stats_gold(state, pid).await;
        }
        state.send_msg_id_to(SendTarget::ToAll, 451, &format!("{}@{}", my_name, t_name)).await;
    }
}

/// /SICV — Accept CvC challenge and start the battle.
pub(super) async fn handle_slash_sicv(state: &mut GameState, conn_id: ConnectionId) {
    let (char_name, guild_idx, dead) = match state.users.get(&conn_id) {
        Some(u) if u.logged => (u.char_name.clone(), u.guild_index, u.dead),
        _ => return,
    };

    if dead {
        state.send_console(conn_id, "Estas muerto!", font_index::INFO).await;
        return;
    }
    if guild_idx < 1 {
        state.send_msg_id(conn_id, 120, "").await;
        return;
    }
    if state.cvc_funciona {
        state.send_msg_id(conn_id, 364, "").await;
        return;
    }

    // Check there is a pending challenge for this guild
    if state.cvc_pending_target_guild != guild_idx {
        state.send_console(conn_id, "No hay un desafio CvC pendiente para tu clan.", font_index::INFO).await;
        return;
    }

    // Validate caller is leader of target guild
    let my_guild = guilds::load_guild(&state.pool, guild_idx).await;
    let is_leader = match &my_guild {
        Some(g) => g.leader.to_uppercase() == char_name.to_uppercase(),
        None => false,
    };
    if !is_leader {
        state.send_console(conn_id, "Solo el lider puede aceptar el desafio CvC.", font_index::INFO).await;
        return;
    }

    let challenger_guild_idx = state.cvc_pending_challenger_guild;
    let challenger_name = state.cvc_pending_challenger_name.clone();
    let acceptor_name = my_guild.as_ref().map(|g| g.name.clone()).unwrap_or_default();

    // Count eligible members from each clan
    let objects = &state.game_data.objects;
    let mut clan1_members: Vec<ConnectionId> = Vec::new(); // Acceptor (blue)
    let mut clan2_members: Vec<ConnectionId> = Vec::new(); // Challenger (red)

    for (&cid, user) in state.users.iter() {
        if user.guild_index == guild_idx && is_cvc_eligible(user, objects) {
            clan1_members.push(cid);
        } else if user.guild_index == challenger_guild_idx && is_cvc_eligible(user, objects) {
            clan2_members.push(cid);
        }
    }

    if clan1_members.is_empty() || clan2_members.is_empty() {
        state.send_console(conn_id, "Ambos clanes necesitan al menos 1 miembro elegible.", font_index::INFO).await;
        // Clear pending
        state.cvc_pending_target_guild = 0;
        state.cvc_pending_challenger_guild = 0;
        state.cvc_pending_challenger_name.clear();
        return;
    }

    // Balance: limit each clan to the smaller count
    let balanced_count = clan1_members.len().min(clan2_members.len());
    clan1_members.truncate(balanced_count);
    clan2_members.truncate(balanced_count);

    // Check gold from both leaders
    let challenger_leader_name = guilds::load_guild(&state.pool, challenger_guild_idx).await
        .map(|g| g.leader.to_uppercase())
        .unwrap_or_default();
    let challenger_leader_conn = state.users.iter()
        .find(|(_, u)| u.logged && u.guild_index == challenger_guild_idx && u.char_name.to_uppercase() == challenger_leader_name)
        .map(|(&cid, _)| cid);

    let acceptor_gold = state.users.get(&conn_id).map(|u| u.gold).unwrap_or(0);
    let challenger_gold = challenger_leader_conn
        .and_then(|cid| state.users.get(&cid))
        .map(|u| u.gold)
        .unwrap_or(0);

    if acceptor_gold < CVC_COST {
        state.send_console(conn_id, &format!("No tienes suficiente oro ({} requeridos).", CVC_COST), font_index::INFO).await;
        return;
    }
    if challenger_gold < CVC_COST {
        state.send_console(conn_id, "El lider del clan desafiante no tiene suficiente oro.", font_index::INFO).await;
        return;
    }

    // Charge both leaders
    if let Some(u) = state.users.get_mut(&conn_id) {
        u.gold -= CVC_COST;
    }
    send_stats_gold(state, conn_id).await;

    if let Some(cl_conn) = challenger_leader_conn {
        if let Some(u) = state.users.get_mut(&cl_conn) {
            u.gold -= CVC_COST;
        }
        send_stats_gold(state, cl_conn).await;
    }

    // Set CvC state
    state.cvc_funciona = true;
    state.cvc_guild1 = guild_idx;        // Blue team (acceptor)
    state.cvc_guild2 = challenger_guild_idx; // Red team (challenger)
    state.cvc_nombre1 = acceptor_name.clone();
    state.cvc_nombre2 = challenger_name.clone();
    state.cvc_clan1_count = balanced_count as i32;
    state.cvc_clan2_count = balanced_count as i32;

    // Clear pending
    state.cvc_pending_target_guild = 0;
    state.cvc_pending_challenger_guild = 0;
    state.cvc_pending_challenger_name.clear();

    // Prepare and warp all participants
    // Blue team: X=37-48, Y=70-77
    let mut blue_x = 37;
    let mut blue_y = 70;
    for &cid in &clan1_members {
        // Revive if dead
        let is_dead = state.users.get(&cid).map(|u| u.dead).unwrap_or(false);
        if is_dead {
            revive_user(state, cid).await;
        }
        // Save old position
        if let Some(u) = state.users.get_mut(&cid) {
            u.vieja_pos_map = u.pos_map;
            u.vieja_pos_x = u.pos_x;
            u.vieja_pos_y = u.pos_y;
            u.en_cvc = true;
            u.cvc_blue = true;
        }
        warp_user(state, cid, CVC_MAP, blue_x, blue_y).await;
        blue_x += 1;
        if blue_x > 48 { blue_x = 37; blue_y += 1; }
        if blue_y > 77 { blue_y = 70; }
    }

    // Red team: X=75-86, Y=35-45
    let mut red_x = 75;
    let mut red_y = 35;
    for &cid in &clan2_members {
        let is_dead = state.users.get(&cid).map(|u| u.dead).unwrap_or(false);
        if is_dead {
            revive_user(state, cid).await;
        }
        if let Some(u) = state.users.get_mut(&cid) {
            u.vieja_pos_map = u.pos_map;
            u.vieja_pos_x = u.pos_x;
            u.vieja_pos_y = u.pos_y;
            u.en_cvc = true;
            u.cvc_blue = false;
        }
        warp_user(state, cid, CVC_MAP, red_x, red_y).await;
        red_x += 1;
        if red_x > 86 { red_x = 75; red_y += 1; }
        if red_y > 45 { red_y = 35; }
    }

    // Announce battle start
    state.send_msg_id_to(SendTarget::ToAll, 85, &format!("{}@{}", acceptor_name, challenger_name)).await;
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

/// /PODER — Check/assign Gran Poder. VB6 TCP_HandleData2.bas:1074
/// If nobody has it, assigns to a random eligible player in PK zone.
/// If someone has it, shows who and where.
pub(super) async fn handle_slash_poder(state: &mut GameState, conn_id: ConnectionId) {
    match state.users.get(&conn_id) {
        Some(u) if u.logged => {}
        _ => return,
    }

    if state.gran_poder_holder > 0 {
        // Someone has it — show who
        let (holder_name, holder_map) = match state.users.get(&state.gran_poder_holder) {
            Some(u) if u.logged => (u.char_name.clone(), u.pos_map),
            _ => {
                // Holder disconnected, clear it
                state.gran_poder_holder = 0;
                state.send_console(conn_id, "Nadie tiene el Gran Poder.", font_index::INFO).await;
                return;
            }
        };

        // VB6: ||362@name or ||363@name@map
        state.send_msg_id(conn_id, 362, &format!("{}@{}", holder_name, holder_map)).await;
    } else {
        // Nobody has it — try to assign to a random eligible player
        // VB6: OtorgarGranPoder — picks random player in PK zone (map with PK enabled)
        let eligible: Vec<ConnectionId> = state.users.values()
            .filter(|u| u.logged && !u.dead && u.privileges == privilege_level::USER)
            .map(|u| u.conn_id)
            .collect();

        if eligible.is_empty() {
            state.send_console(conn_id, "Nadie tiene el Gran Poder y no hay jugadores elegibles.", font_index::INFO).await;
            return;
        }

        // Simple random selection
        let idx = (std::time::SystemTime::now()
            .duration_since(std::time::UNIX_EPOCH)
            .unwrap_or_default()
            .as_millis() as usize) % eligible.len();
        let chosen = eligible[idx];
        state.gran_poder_holder = chosen;

        let chosen_name = state.users.get(&chosen).map(|u| u.char_name.clone()).unwrap_or_default();

        // Broadcast: ||363@name
        state.send_msg_id_to(SendTarget::ToAll, 363, &format!("{}", chosen_name)).await;
    }
}

// =====================================================================
// Integration tests — full client login flow
// =====================================================================

