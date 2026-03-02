//! Miscellaneous packet handlers: swap, skills, stat updates, UI packets,
//! pet commands, travel, citizenship, training, voting, pareja, CvC accept.
//! Extracted from mod.rs to reduce file size.

use tracing::info;
use crate::net::ConnectionId;
use crate::game::types::{GameState, UserState, SendTarget, InventorySlot, MAX_INVENTORY_SLOTS, MAX_SPELL_SLOTS, privilege_level};
use crate::game::world;
use crate::protocol::{server_opcodes, font_types, fields::read_field};
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
            state.send_to(conn_id, "||153").await;
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
            state.send_to(conn_id, &format!("{}Valor invalido.{}", server_opcodes::CONSOLE_MSG, font_types::INFO)).await;
            return;
        }
        increments[i] = val;
        total += val;
    }

    if let Some(user) = state.users.get(&conn_id) {
        if total > user.skill_pts_libres || total <= 0 {
            state.send_to(conn_id, &format!("{}Puntos de skill invalidos.{}", server_opcodes::CONSOLE_MSG, font_types::INFO)).await;
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

    state.send_to(conn_id, &format!("{}Has distribuido {} puntos de skill.{}", server_opcodes::CONSOLE_MSG, total, font_types::INFO)).await;
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
        state.send_to(conn_id, "||288").await; // VB6: error message
        return;
    }

    let spell = &state.game_data.spells[spell_idx as usize - 1];
    let nombre = spell.nombre.clone();
    let desc = spell.desc.clone();
    let min_skill = spell.min_skill;
    let mana_req = spell.mana_requerido;
    let sta_req = spell.sta_requerido;

    // VB6 sends 7 packets: ||281 (header), ||282@name, ||283@desc, ||284@skill, ||285@mana, ||286@sta, ||287 (footer)
    state.send_to(conn_id, "||281").await;
    state.send_to(conn_id, &format!("||282@{}", nombre)).await;
    state.send_to(conn_id, &format!("||283@{}", desc)).await;
    state.send_to(conn_id, &format!("||284@{}", min_skill)).await;
    state.send_to(conn_id, &format!("||285@{}", mana_req)).await;
    state.send_to(conn_id, &format!("||286@{}", sta_req)).await;
    state.send_to(conn_id, "||287").await;
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
            state.send_to(conn_id, "||37").await; // VB6: can't move first slot up
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
            state.send_to(conn_id, "||37").await; // VB6: can't move last slot down
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
        state.send_to(conn_id, &format!("{}Usuario no encontrado.{}", server_opcodes::CONSOLE_MSG, font_types::INFO)).await;
        return;
    }
    let target_conn = target_conn.unwrap();

    let info = if let Some(target) = state.users.get(&target_conn) {
        format!("GINF{},{},{},{},{},{},{},{},{},{},{},{},{},{},{},{},{}",
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

    state.send_to(conn_id, &info).await;
}

/// FEST — Send mini statistics.
pub(super) async fn handle_fest(state: &mut GameState, conn_id: ConnectionId) {
    let info = if let Some(user) = state.users.get(&conn_id) {
        format!("FEST{},{},{},{},{},{},{},{},{},{},{}",
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
    state.send_to(conn_id, &info).await;
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
            state.send_to(conn_id, &format!("{}No tienes suficiente oro.{}", server_opcodes::CONSOLE_MSG, font_types::INFO)).await;
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
    let cc = state.users.get(&conn_id).unwrap().build_cc_packet();
    let cp = format!("CP{},{},{},{},{},{},0,0,{}", old_ci,
        state.users.get(&conn_id).unwrap().body,
        new_head,
        state.users.get(&conn_id).unwrap().heading,
        state.users.get(&conn_id).unwrap().weapon_anim,
        state.users.get(&conn_id).unwrap().shield_anim,
        state.users.get(&conn_id).unwrap().casco_anim,
    );
    state.send_data(SendTarget::ToArea { map, x, y }, &cp).await;
    state.send_to(conn_id, &format!("{}Cabeza cambiada.{}", server_opcodes::CONSOLE_MSG, font_types::INFO)).await;
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
    state.send_to(conn_id, &format!("{}Has ganado {} puntos de vida extra!{}", server_opcodes::CONSOLE_MSG, hp_bonus, font_types::INFO)).await;
}

/// UK — Use Skill. VB6: TCP_HandleData1.bas Case "UK".
/// Robar/Magia/Domar → sends T01<skillID> to client (opens skill tree UI).
/// Ocultarse → checks navigating/already hidden, then calls do_ocultarse.
pub(super) async fn handle_uk(state: &mut GameState, conn_id: ConnectionId, data: &str) {
    // Check dead
    let (dead, navigating, hidden) = match state.users.get(&conn_id) {
        Some(u) if u.logged => (u.dead, u.navigating, u.hidden),
        _ => return,
    };

    if dead {
        state.send_to(conn_id, "||3").await;
        return;
    }

    let payload = strip_opcode(data, 2); // "UK" is 2 chars
    let skill_num: i32 = payload.trim().parse().unwrap_or(0);

    match skill_num {
        3 => { // Robar
            state.send_to(conn_id, &format!("T01{}", skill_num)).await;
        }
        2 => { // Magia
            state.send_to(conn_id, &format!("T01{}", skill_num)).await;
        }
        18 => { // Domar
            state.send_to(conn_id, &format!("T01{}", skill_num)).await;
        }
        8 => { // Ocultarse
            if navigating {
                state.send_to(conn_id, "||233").await;
                return;
            }
            if hidden {
                state.send_to(conn_id, "||234").await;
                return;
            }
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
        state.send_to(conn_id, &format!("{}No estas interactuando con un NPC.{}", server_opcodes::CONSOLE_MSG, font_types::INFO)).await;
        return;
    }

    if nro_mascotas >= 3 {
        state.send_to(conn_id, &format!("{}Ya tienes el maximo de mascotas.{}", server_opcodes::CONSOLE_MSG, font_types::INFO)).await;
        return;
    }

    // Simple: spawn a pet NPC near the player
    // In VB6 this reads from the trainer's creature list — for now, just acknowledge
    state.send_to(conn_id, &format!("{}Criatura entrenada.{}", server_opcodes::CONSOLE_MSG, font_types::INFO)).await;
}

/// ACTPT — Send tournament/donation points.
pub(super) async fn handle_actpt(state: &mut GameState, conn_id: ConnectionId) {
    let info = if let Some(user) = state.users.get(&conn_id) {
        format!("APT{},{},{}", user.puntos_torneo, user.puntos_donacion, user.ts_points)
    } else {
        return;
    };
    state.send_to(conn_id, &info).await;
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
    let mut result = String::from("MTOP");
    for i in 0..10 {
        let entry = &top.entries[i];
        if !entry.name.is_empty() {
            result.push_str(&format!("{}-{},", entry.name, entry.value));
        } else {
            result.push_str("N/A-0,");
        }
    }

    state.send_to(conn_id, &result).await;
}

/// ACTUALIZAR — Position re-sync.
pub(super) async fn handle_actualizar(state: &mut GameState, conn_id: ConnectionId) {
    let msg = if let Some(user) = state.users.get(&conn_id) {
        format!("PU{},{}", user.pos_x, user.pos_y)
    } else {
        return;
    };
    state.send_to(conn_id, &msg).await;
}

/// IDUELOS — Duel arena info.
pub(super) async fn handle_iduelos(state: &mut GameState, conn_id: ConnectionId) {
    let mut msg = String::from("MAR");
    for i in 1..=8 {
        if i > 1 { msg.push(','); }
        msg.push_str(&state.nombre_dueleando[i]);
    }
    state.send_to(conn_id, &msg).await;
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
        let msg = format!("N|Seguridad>> se detecto el uso de macros en el usuario: {}, hay que revisarlo. ~255~255~0", name);
        state.send_data(SendTarget::ToAdmins,&msg).await;

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
    state.send_to(conn_id, "FINCBNOK").await;
}

/// VLKG — Query guild bank permissions for a player.
/// VB6: TCP_HandleData1.bas Case "VLKG". Returns KHEKD<canObj>,<canGold>.
pub(super) async fn handle_vlkg(state: &mut GameState, conn_id: ConnectionId, data: &str) {
    let nick = strip_opcode(data, 4).trim().to_string();
    if nick.is_empty() { return; }

    // Try online first
    if let Some(target_conn) = state.find_user_by_name(&nick) {
        let (can_obj, can_gold) = match state.users.get(&target_conn) {
            Some(u) => (u.puede_retirar_obj as i32, u.puede_retirar_oro as i32),
            None => (0, 0),
        };
        state.send_to(conn_id, &format!("KHEKD{},{}", can_obj, can_gold)).await;
    } else {
        // Offline — not tracked in DB (runtime-only flags), return defaults
        state.send_to(conn_id, &format!("KHEKD{},{}", 0, 0)).await;
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
        state.send_to(conn_id, "||265").await;
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
            state.send_to(conn_id, &format!("||945@{}", user.silence_timer)).await;
            return;
        }
        if user.consulta_enviada {
            state.send_to(conn_id, "||192").await;
            return;
        }
    } else {
        return;
    }

    let name = state.users.get(&conn_id).unwrap().char_name.clone();

    // Notify admins
    state.send_data(SendTarget::ToAdmins,"||193").await;

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
        state.send_to(target, "||190").await;
        let admin_name = state.users.get(&conn_id).map(|u| u.char_name.clone()).unwrap_or_default();
        state.send_to(target, &format!("RESPUES{}*{}", texto, admin_name)).await;
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
    state.send_to(conn_id, &format!("ZSOS{}", data_sos)).await;
}

/// ENVFPZ — FPZ anti-hack report.
pub(super) async fn handle_envfpz(state: &mut GameState, conn_id: ConnectionId, data: &str) {
    let payload = strip_opcode(data, 6);
    let name = state.users.get(&conn_id).map(|u| u.char_name.clone()).unwrap_or_default();
    let msg = format!("||218@{}@{}", name, payload);
    state.send_data(SendTarget::ToAdmins,&msg).await;
}

/// DCANJE — Donation exchange menu.
pub(super) async fn handle_dcanje(state: &mut GameState, conn_id: ConnectionId) {
    // Donation system — send basic info
    let pts = state.users.get(&conn_id).map(|u| u.puntos_donacion).unwrap_or(0);
    // Send empty donation list (no donations configured in this server)
    let msg = format!("DRM{},0,", pts);
    state.send_to(conn_id, &msg).await;
}

/// DPX — Donation item preview.
pub(super) async fn handle_dpx(state: &mut GameState, conn_id: ConnectionId, data: &str) {
    let payload = strip_opcode(data, 3);
    let _item_id: i32 = read_field(1, payload, ',').parse().unwrap_or(0);
    // Donation system not fully implemented — send empty preview
    state.send_to(conn_id, "DNF0,0,0,0,0,0,0,0,Sin descripcion,0,").await;
}

/// DRX — Redeem donation.
pub(super) async fn handle_drx(state: &mut GameState, conn_id: ConnectionId, data: &str) {
    let payload = strip_opcode(data, 3);
    let _item_id: i32 = read_field(1, payload, ',').parse().unwrap_or(0);
    state.send_to(conn_id, "||632").await; // "No tienes suficientes puntos"
}

/// CCANJE — Tournament prize menu.
pub(super) async fn handle_ccanje(state: &mut GameState, conn_id: ConnectionId) {
    let info = if let Some(user) = state.users.get(&conn_id) {
        format!("PRM0,{},{}", user.puntos_torneo, user.ts_points)
    } else {
        return;
    };
    state.send_to(conn_id, &info).await;
}

/// IPX — Prize item info.
pub(super) async fn handle_ipx(state: &mut GameState, conn_id: ConnectionId, _data: &str) {
    state.send_to(conn_id, "INF0,0,0,0,0,0,0,0,0,Sin premios disponibles").await;
}

/// SPX — Buy tournament prize.
pub(super) async fn handle_spx(state: &mut GameState, conn_id: ConnectionId, _data: &str) {
    state.send_to(conn_id, &format!("{}No hay premios disponibles.{}", server_opcodes::CONSOLE_MSG, font_types::INFO)).await;
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
        state.send_to(conn_id, "||226").await;
        return;
    }

    state.send_to(conn_id, &format!("ENCHAT{}", friend_name)).await;
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
        let recv_msg = format!("IRCHAT{},{}", sender_name, text);
        state.send_to(target_conn, &recv_msg).await;
    } else {
        state.send_to(conn_id, &format!("{}El usuario no esta online.{}", server_opcodes::CONSOLE_MSG, font_types::INFO)).await;
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
        state.send_to(conn_id, &format!("{}No perteneces a un clan.{}", server_opcodes::CONSOLE_MSG, font_types::INFO)).await;
        return;
    }

    if let Some(user) = state.users.get(&conn_id) {
        if user.puntos_torneo < amount {
            state.send_to(conn_id, &format!("{}No tienes suficientes puntos.{}", server_opcodes::CONSOLE_MSG, font_types::INFO)).await;
            return;
        }
    }

    if let Some(user) = state.users.get_mut(&conn_id) {
        user.puntos_torneo -= amount;
    }

    state.send_to(conn_id, &format!("{}Has donado {} puntos al clan.{}", server_opcodes::CONSOLE_MSG, amount, font_types::INFO)).await;
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
        state.send_to(conn_id, &format!("{}No hay nadie ahi.{}", server_opcodes::CONSOLE_MSG, font_types::INFO)).await;
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
            state.send_to(conn_id, &format!("{}No puedes transferir un item equipado.{}", server_opcodes::CONSOLE_MSG, font_types::INFO)).await;
            return;
        }
    }

    // Transfer: remove from source, add to target
    let actual_amount = state.users.get(&conn_id).map(|u| u.inventory[si].amount.min(amount)).unwrap_or(0);
    if actual_amount <= 0 { return; }

    // Try to add to target inventory
    let added = add_item_to_user_inventory(state, target_user, obj_idx, actual_amount);
    if !added {
        state.send_to(conn_id, &format!("{}El otro jugador no tiene espacio.{}", server_opcodes::CONSOLE_MSG, font_types::INFO)).await;
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
    state.send_to(conn_id, &format!("{}Has transferido {} {}.{}", server_opcodes::CONSOLE_MSG, actual_amount, obj_name, font_types::INFO)).await;
    state.send_to(target_user, &format!("{}{} te ha dado {} {}.{}", server_opcodes::CONSOLE_MSG, sender_name, actual_amount, obj_name, font_types::INFO)).await;

    send_full_inventory(state, conn_id).await;
    send_full_inventory(state, target_user).await;
}

/// TOINFO — Tournament info.
pub(super) async fn handle_toinfo(state: &mut GameState, conn_id: ConnectionId) {
    let mut list = String::from("LTR");
    for name in &state.cronologia_participantes {
        list.push_str(name);
        list.push(',');
    }
    state.send_to(conn_id, &list).await;
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
        state.send_to(conn_id, &format!("{}No hay votacion activa.{}", server_opcodes::CONSOLE_MSG, font_types::INFO)).await;
        return;
    }

    if option < 1 || option > 5 {
        return;
    }

    let name = state.users.get(&conn_id).map(|u| u.char_name.clone()).unwrap_or_default();
    if state.poll_voters.contains(&name) {
        state.send_to(conn_id, &format!("{}Ya has votado.{}", server_opcodes::CONSOLE_MSG, font_types::INFO)).await;
        return;
    }

    state.poll_votes[option - 1] += 1;
    state.poll_voters.push(name);
    state.send_to(conn_id, &format!("{}Voto registrado.{}", server_opcodes::CONSOLE_MSG, font_types::INFO)).await;
}

/// NEWD — New report/denuncia.
pub(super) async fn handle_newd(state: &mut GameState, conn_id: ConnectionId, data: &str) {
    let payload = strip_opcode(data, 4);
    let target_name = read_field(1, payload, ',');
    let reason = read_field(2, payload, ',');

    let reporter = state.users.get(&conn_id).map(|u| u.char_name.clone()).unwrap_or_default();

    info!("[REPORT] {} reports {}: {}", reporter, target_name, reason);

    let msg = format!("||218@{}@Denuncia contra {}: {}", reporter, target_name, reason);
    state.send_data(SendTarget::ToAdmins,&msg).await;

    state.send_to(conn_id, &format!("{}Denuncia enviada.{}", server_opcodes::CONSOLE_MSG, font_types::INFO)).await;
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
        state.send_to(conn_id, &format!("{}No tienes una montura.{}", server_opcodes::CONSOLE_MSG, font_types::INFO)).await;
        return;
    }

    let already_mounted = state.users.get(&conn_id).map(|u| u.montado).unwrap_or(false);
    if already_mounted {
        state.send_to(conn_id, &format!("{}Ya estas montado.{}", server_opcodes::CONSOLE_MSG, font_types::INFO)).await;
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
        format!("CP{},{},{},{},{},{},0,0,{}", user.char_index.0, user.body, user.head, user.heading,
            super::common::NINGUN_ARMA, super::common::NINGUN_ESCUDO, super::common::NINGUN_CASCO)
    };
    state.send_data(SendTarget::ToArea { map, x, y }, &cp).await;
    state.send_to(conn_id, &format!("{}Te has montado.{}", server_opcodes::CONSOLE_MSG, font_types::INFO)).await;
}

/// /DESMONTAR — Dismount.
pub(super) async fn handle_slash_desmontar(state: &mut GameState, conn_id: ConnectionId) {
    let is_mounted = state.users.get(&conn_id).map(|u| u.montado).unwrap_or(false);
    if !is_mounted {
        state.send_to(conn_id, &format!("{}No estas montado.{}", server_opcodes::CONSOLE_MSG, font_types::INFO)).await;
        return;
    }

    let (map, x, y) = match state.users.get(&conn_id) {
        Some(u) => (u.pos_map, u.pos_x, u.pos_y),
        None => return,
    };

    if let Some(user) = state.users.get_mut(&conn_id) {
        user.montado = false;
        user.body = user.montado_body;
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
        format!("CP{},{},{},{},{},{},0,0,{}", user.char_index.0, user.body, user.head, user.heading, user.weapon_anim, user.shield_anim, user.casco_anim)
    };
    state.send_data(SendTarget::ToArea { map, x, y }, &cp).await;
    state.send_to(conn_id, &format!("{}Te has desmontado.{}", server_opcodes::CONSOLE_MSG, font_types::INFO)).await;
}

/// Helper: get equipped item animation GRH IDs.
// get_equipped_anims — moved to common.rs

/// /QUITARMASCOTA — Remove pet.
pub(super) async fn handle_slash_quitarmascota(state: &mut GameState, conn_id: ConnectionId) {
    let nro = state.users.get(&conn_id).map(|u| u.nro_mascotas).unwrap_or(0);
    if nro == 0 {
        state.send_to(conn_id, &format!("{}No tienes mascotas.{}", server_opcodes::CONSOLE_MSG, font_types::INFO)).await;
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

    state.send_to(conn_id, &format!("{}Mascota removida.{}", server_opcodes::CONSOLE_MSG, font_types::INFO)).await;
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
        state.send_to(conn_id, &format!("{}Mensajes privados activados.{}", server_opcodes::CONSOLE_MSG, font_types::INFO)).await;
    } else {
        state.send_to(conn_id, &format!("{}Mensajes privados desactivados.{}", server_opcodes::CONSOLE_MSG, font_types::INFO)).await;
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
        state.send_to(conn_id, "||9").await;
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
        state.send_to(conn_id, "||10").await;
        return;
    }

    // VB6: NPCtype must be Ciudadania (13)
    if npc_type != crate::data::npcs::NpcType::Citizenship { return; }

    // VB6: Set home based on map (130=Inthak, 25=Thir)
    let city = match map {
        130 => "Inthak",
        25 => "Thir",
        _ => {
            state.send_to(conn_id, &format!("{}No estas en una ciudad valida.{}", server_opcodes::CONSOLE_MSG, font_types::INFO)).await;
            return;
        }
    };

    let current_home = state.users.get(&conn_id).map(|u| u.hogar.clone()).unwrap_or_default();
    if current_home == city { return; } // VB6: If already same home, exit

    if let Some(user) = state.users.get_mut(&conn_id) {
        user.hogar = city.to_string();
    }

    // VB6: ||318@<home>
    state.send_to(conn_id, &format!("||318@{}", city)).await;
}

/// /VIAJAR — Travel to city via Traveler NPC.
/// VB6: TCP_HandleData3.bas lines 760-846
pub(super) async fn handle_slash_viajar(state: &mut GameState, conn_id: ConnectionId, city: &str) {
    let city_upper = city.trim().to_uppercase();

    // Validate city name (VB6 line 763)
    let valid = ["TANARIS", "ANVILMAR", "KAHLIMDOR", "THIR", "INTHAK", "JHUMBEL", "RUVENDEL", "HELKA"];
    if !valid.contains(&city_upper.as_str()) {
        state.send_to(conn_id, &format!("{}Ciudad desconocida. Ciudades: Tanaris, Anvilmar, Kahlimdor, Thir, Inthak, Jhumbel, Ruvendel, Helka{}", server_opcodes::CONSOLE_MSG, font_types::INFO)).await;
        return;
    }

    // Must have traveler NPC targeted (VB6 NpcType=12)
    let user_data = match state.users.get(&conn_id) {
        Some(u) if u.logged => (u.dead, u.level, u.gold, u.target_npc_idx, u.pos_map, u.pos_x, u.pos_y),
        _ => return,
    };
    let (dead, level, gold, target_npc, _map, _ux, _uy) = user_data;

    if dead {
        state.send_to(conn_id, "||3").await;
        return;
    }

    if target_npc == 0 {
        state.send_to(conn_id, "||9").await;
        return;
    }

    // Check NPC is a Traveler (type 12)
    let npc_ok = state.get_npc(target_npc).map(|n| n.npc_type == crate::data::npcs::NpcType::Traveler).unwrap_or(false);
    if !npc_ok { return; }

    // Gold cost: <30 = 1000, >=30 = 5000 (VB6 lines 778-788)
    let cost = if level < 30 { 1000i64 } else { 5000 };
    if gold < cost {
        let cost_str = if level < 30 { "1.000" } else { "5.000" };
        state.send_to(conn_id, &format!("||215@{}", cost_str)).await;
        return;
    }

    // Inthak requires level 30+ (VB6 line 812)
    if city_upper == "INTHAK" && level < 30 {
        state.send_to(conn_id, "||542").await;
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
            let snd_pkt = "TW3".to_string(); // SND_WARP = 3
            let fx_pkt = format!("CFX{},1,0", char_idx); // FXWARP = 1
            // Send to area (others see it) AND directly to self (ensure self always gets it)
            state.send_data(SendTarget::ToArea { map, x, y }, &snd_pkt).await;
            state.send_data(SendTarget::ToArea { map, x, y }, &fx_pkt).await;
            // Also send directly to ensure self receives it (area detection may miss self right after warp)
            state.send_to(conn_id, &snd_pkt).await;
            state.send_to(conn_id, &fx_pkt).await;
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
        state.send_to(conn_id, "||3").await;
        return;
    }

    // VB6: If TargetNPC = 0 Then ||9
    if target_npc == 0 {
        state.send_to(conn_id, "||9").await;
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
        state.send_to(conn_id, "||10").await;
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
    let mut pkt = format!("LSTCRI{},", npc_data.nro_criaturas);
    for c in &npc_data.criaturas {
        pkt.push_str(&c.npc_name);
        pkt.push(',');
    }
    state.send_to(conn_id, &pkt).await;
}

/// /CENTINELA — Anti-AFK response.
pub(super) async fn handle_slash_centinela(state: &mut GameState, conn_id: ConnectionId, code: &str) {
    // Simple anti-AFK — accept any response
    state.send_to(conn_id, &format!("{}Centinela verificado.{}", server_opcodes::CONSOLE_MSG, font_types::INFO)).await;
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
            state.send_to(conn_id, &format!("{}Destino desconocido.{}", server_opcodes::CONSOLE_MSG, font_types::INFO)).await;
            return;
        }
    };

    warp_user(state, conn_id, dest_map, dest_x, dest_y).await;
}

/// /VOTAR — Vote in poll.
pub(super) async fn handle_slash_votar(state: &mut GameState, conn_id: ConnectionId) {
    if !state.poll_active {
        state.send_to(conn_id, &format!("{}No hay votacion activa.{}", server_opcodes::CONSOLE_MSG, font_types::INFO)).await;
        return;
    }
    // Send poll options
    let mut msg = String::from("VOT");
    for i in 0..5 {
        msg.push_str(&state.poll_options[i]);
        msg.push(',');
    }
    state.send_to(conn_id, &msg).await;
}

/// /RESULTADOS — Poll results.
pub(super) async fn handle_slash_resultados(state: &mut GameState, conn_id: ConnectionId) {
    let total: i32 = state.poll_votes.iter().sum();
    let mut msg = format!("{}Resultados de la votacion:", server_opcodes::CONSOLE_MSG);
    for i in 0..5 {
        if !state.poll_options[i].is_empty() {
            let pct = if total > 0 { (state.poll_votes[i] * 100) / total } else { 0 };
            msg.push_str(&format!(" {}: {} ({}%)", state.poll_options[i], state.poll_votes[i], pct));
        }
    }
    msg.push_str(font_types::INFO);
    state.send_to(conn_id, &msg).await;
}

/// /GUERRA — Join war event. VB6: TCP_HandleData2.bas:430-454.
/// Warps player to the war zone based on faction (Alianza/Horda).
pub(super) async fn handle_slash_guerra(state: &mut GameState, conn_id: ConnectionId) {
    if !state.hay_guerra {
        state.send_to(conn_id, "||322").await;
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
        state.send_to(conn_id, "||324").await;
        return;
    }

    if cur_map_pk {
        state.send_to(conn_id, "||323").await;
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

    state.send_to(conn_id, "||325").await;
}

/// /CIRUJIA — Surgery (race change). VB6: requires cirujano NPC, distance <= 3.
pub(super) async fn handle_slash_cirujia(state: &mut GameState, conn_id: ConnectionId) {
    let (dead, target_npc) = match state.users.get(&conn_id) {
        Some(u) if u.logged => (u.dead, u.target_npc),
        _ => return,
    };

    if dead {
        state.send_to(conn_id, "||3").await;
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
        state.send_to(conn_id, "||158").await;
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
    state.send_to(conn_id, &format!("CIRUJA{},{}", raza_num, genero)).await;
}

/// /NOBLE — Become noble. VB6: TCP_HandleData2.bas:998-1052.
/// Requires items 1073-1077 (qty 1 each). Grants spell 46. Sets EsNoble flag.
pub(super) async fn handle_slash_noble(state: &mut GameState, conn_id: ConnectionId) {
    // Check all 5 required items
    for obj_id in 1073..=1077 {
        if !user_has_items(state, conn_id, obj_id, 1) {
            state.send_to(conn_id, "||356").await;
            return;
        }
    }

    let (dead, es_noble, class) = match state.users.get(&conn_id) {
        Some(u) if u.logged => (u.dead, u.es_noble, u.class.clone()),
        _ => return,
    };

    if dead {
        state.send_to(conn_id, "||3").await;
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
            state.send_to(conn_id, &format!("SHI{},{}", slot + 1, spell_name)).await;
        } else {
            state.send_to(conn_id, "||181").await; // No spell slots
            if let Some(user) = state.users.get_mut(&conn_id) {
                user.es_noble = true;
            }
        }
    } else {
        state.send_to(conn_id, "||182").await; // Already has spell
        if let Some(user) = state.users.get_mut(&conn_id) {
            user.es_noble = true;
        }
    }

    let name = state.users.get(&conn_id).map(|u| u.char_name.clone()).unwrap_or_default();
    state.send_to(conn_id, &format!("||357@{}", class)).await;
    state.send_data(SendTarget::ToAll, &format!("||358@{}", name)).await;
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
        state.send_to(conn_id, "||359").await;
        return;
    }

    // Check for LlaveTesoro (obj 1062)
    const LLAVE_TESORO: i32 = 1062;
    if !user_has_items(state, conn_id, LLAVE_TESORO, 1) {
        state.send_to(conn_id, "||360").await;
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
    let obj_pkt = format!("HO{},{},{},{}", grh, t_x, t_y, 1);
    state.send_data(SendTarget::ToArea { map: t_map, x: t_x, y: t_y }, &obj_pkt).await;

    state.send_to(conn_id, "||361").await;
}

/// /BOTIX — Spawn AI bot.
pub(super) async fn handle_slash_botix(state: &mut GameState, conn_id: ConnectionId) {
    state.send_to(conn_id, &format!("{}Sistema de bots no disponible.{}", server_opcodes::CONSOLE_MSG, font_types::INFO)).await;
}

/// /INFOSUB — Auction info.
pub(super) async fn handle_slash_infosub(state: &mut GameState, conn_id: ConnectionId) {
    state.send_to(conn_id, &format!("{}No hay subasta activa.{}", server_opcodes::CONSOLE_MSG, font_types::INFO)).await;
}

/// /SUBASTAR — Start auction.
pub(super) async fn handle_slash_subastar(state: &mut GameState, conn_id: ConnectionId) {
    state.send_to(conn_id, &format!("{}Sistema de subastas no disponible.{}", server_opcodes::CONSOLE_MSG, font_types::INFO)).await;
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

    let casas_path = state.base_path.join("Dat").join("Casas.dat");
    let section = format!("Casa{}", num_casa);

    let mut dueno = ini_get(&casas_path, &section, "Dueno");
    if dueno.is_empty() { dueno = ini_get(&casas_path, &section, "Due\u{00F1}o"); }
    if dueno.is_empty() { dueno = "N/A".to_string(); }
    let precio = ini_get(&casas_path, &section, "Precio");
    let fecha = ini_get(&casas_path, &section, "Fecha");

    state.send_to(conn_id, &format!("GVN{},{},{}", dueno, precio, fecha)).await;
}

/// CUC — Buy a house.
pub(super) async fn handle_cuc(state: &mut GameState, conn_id: ConnectionId, data: &str) {
    let payload = strip_opcode(data, 3);
    let num_casa: i32 = read_field(1, payload, ',').parse().unwrap_or(0);

    let casas_path = state.base_path.join("Dat").join("Casas.dat");
    let section = format!("Casa{}", num_casa);

    let mut dueno = ini_get(&casas_path, &section, "Dueno");
    if dueno.is_empty() { dueno = ini_get(&casas_path, &section, "Due\u{00F1}o"); }
    if dueno.is_empty() { dueno = "N/A".to_string(); }

    if dueno != "N/A" {
        state.send_to(conn_id, "||243").await;
        return;
    }

    let precio: i64 = ini_get(&casas_path, &section, "Precio").parse().unwrap_or(0);

    let gold = state.users.get(&conn_id).map(|u| u.gold).unwrap_or(0);
    if gold < precio {
        state.send_to(conn_id, &format!("||215@{}", precio)).await;
        return;
    }

    if num_casa <= 0 {
        return;
    }

    // Key obj_index = 1093 + num_casa
    let key_index = 1093 + num_casa;
    if !add_item_to_user_inventory(state, conn_id, key_index, 1) {
        state.send_to(conn_id, "||108").await;
        return;
    }

    // Save owner to Casas.dat
    let char_name = state.users.get(&conn_id).map(|u| u.char_name.clone()).unwrap_or_default();
    ini_write(&casas_path, &section, "Dueno", &char_name);
    ini_write(&casas_path, &section, "Fecha", &chrono_like_date());

    // Broadcast to all
    state.send_data(SendTarget::ToAll, &format!("||244@{}@{}", char_name, num_casa)).await;

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
            state.send_to(conn_id, &format!("{}Mascota renombrada a: {}{}", server_opcodes::CONSOLE_MSG, nick, font_types::INFO)).await;
        }
    } else {
        state.send_to(conn_id, &format!("{}No tienes mascotas.{}", server_opcodes::CONSOLE_MSG, font_types::INFO)).await;
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
            state.send_to(conn_id, "||271").await;
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
                state.send_to(conn_id, "||275").await;
            } else {
                state.send_to(conn_id, "||276").await;
                return; // Don't remove gems
            }
        }
        // 1 = Octarina gem
        1 => {
            if !add_item_to_user_inventory(state, conn_id, 1448, 1) {
                state.send_to(conn_id, "||108").await;
                return;
            }
            state.send_to(conn_id, "||232@1@Gema Octarina").await;
        }
        // 2 = 1500 tournament points
        2 => {
            if let Some(user) = state.users.get_mut(&conn_id) {
                user.puntos_torneo += 1500;
            }
            state.send_to(conn_id, "||57@1.500").await;
        }
        // 3 = 30000 souls
        3 => {
            if !user_has_items(state, conn_id, 1274, 1) {
                state.send_to(conn_id, "||127").await;
                return;
            }
            if let Some(user) = state.users.get_mut(&conn_id) {
                user.almas_contenidas += 30000;
            }
            state.send_to(conn_id, "||274@30.000").await;
        }
        // 4 = Fragment
        4 => {
            if !add_item_to_user_inventory(state, conn_id, 1272, 1) {
                state.send_to(conn_id, "||108").await;
            }
            state.send_to(conn_id, "||277").await;
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
                state.send_to(conn_id, "||278").await;
                return;
            }
            let gem_idx = 406 + (rand_simple_u32() % 6) as i32; // 406-411
            if !add_item_to_user_inventory(state, conn_id, gem_idx, 1) {
                state.send_to(conn_id, "||108").await;
                return;
            }
            let name = state.game_data.objects.get(gem_idx as usize).map(|o| o.name.clone()).unwrap_or_default();
            state.send_to(conn_id, &format!("||232@1@{}", name)).await;
            remove_items_from_inv(state, conn_id, 1025, 8).await;
        }
        // 1 = Sacris (1 medal)
        1 => {
            if !user_has_items(state, conn_id, 1025, 1) {
                state.send_to(conn_id, "||278").await;
                return;
            }
            if !add_item_to_user_inventory(state, conn_id, 936, 1) {
                state.send_to(conn_id, "||108").await;
                return;
            }
            let name = state.game_data.objects.get(936usize).map(|o| o.name.clone()).unwrap_or_default();
            state.send_to(conn_id, &format!("||232@1@{}", name)).await;
            remove_items_from_inv(state, conn_id, 1025, 1).await;
        }
        // 2 = 150 tournament points (1 medal)
        2 => {
            if !user_has_items(state, conn_id, 1025, 1) {
                state.send_to(conn_id, "||278").await;
                return;
            }
            if let Some(user) = state.users.get_mut(&conn_id) {
                user.puntos_torneo += 150;
            }
            state.send_to(conn_id, "||57@150").await;
            remove_items_from_inv(state, conn_id, 1025, 1).await;
        }
        // 3 = 5000 souls (6 medals)
        3 => {
            if !user_has_items(state, conn_id, 1274, 1) {
                state.send_to(conn_id, "||127").await;
                return;
            }
            if !user_has_items(state, conn_id, 1025, 6) {
                state.send_to(conn_id, "||278").await;
                return;
            }
            if let Some(user) = state.users.get_mut(&conn_id) {
                user.almas_contenidas += 5000;
            }
            state.send_to(conn_id, "||274@5.000").await;
            remove_items_from_inv(state, conn_id, 1025, 6).await;
        }
        // 4 = Item 1512 (2 medals)
        4 => {
            if !user_has_items(state, conn_id, 1025, 2) {
                state.send_to(conn_id, "||278").await;
                return;
            }
            if !add_item_to_user_inventory(state, conn_id, 1512, 1) {
                state.send_to(conn_id, "||108").await;
                return;
            }
            let name = state.game_data.objects.get(1512usize).map(|o| o.name.clone()).unwrap_or_default();
            state.send_to(conn_id, &format!("||232@1@{}", name)).await;
            remove_items_from_inv(state, conn_id, 1025, 2).await;
        }
        // 5 = Item 1513 (3 medals)
        5 => {
            if !user_has_items(state, conn_id, 1025, 3) {
                state.send_to(conn_id, "||278").await;
                return;
            }
            if !add_item_to_user_inventory(state, conn_id, 1513, 1) {
                state.send_to(conn_id, "||108").await;
                return;
            }
            let name = state.game_data.objects.get(1513usize).map(|o| o.name.clone()).unwrap_or_default();
            state.send_to(conn_id, &format!("||232@1@{}", name)).await;
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
        state.send_to(conn_id, "ERONo tienes esa cantidad de almas.").await;
        return;
    }

    let god_name = state.users.get(&conn_id).map(|u| u.sirviente_de_dios.clone()).unwrap_or_default();
    let map = state.users.get(&conn_id).map(|u| u.pos_map).unwrap_or(0);

    if let Some(user) = state.users.get_mut(&conn_id) {
        user.almas_contenidas -= cant_almas;
        user.almas_ofrecidas += cant_almas;
    }

    state.send_to(conn_id, &format!("||230@{}@{}", cant_almas, god_name)).await;

    // Send PCF (particle effect) based on god
    let pcf = match god_name.as_str() {
        "Mifrit" => format!("PCF{},{},{},{}", 77, 84, 51, 30),
        "Poseidon" => format!("PCF{},{},{},{}", 77, 49, 14, 30),
        "Tarraske" => format!("PCF{},{},{},{}", 77, 16, 51, 30),
        "Erebros" => format!("PCF{},{},{},{}", 77, 50, 87, 30),
        _ => String::new(),
    };
    if !pcf.is_empty() {
        state.send_data(SendTarget::ToMap(map), &pcf).await;
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
        let god_path = state.base_path.join("Dioses").join(&god_name).join(file_suffix);
        let obj_idx_str = ini_get(&god_path, &class, &obj_key);
        if !obj_idx_str.is_empty() {
            let obj_idx: i32 = obj_idx_str.parse().unwrap_or(0);
            if obj_idx > 0 && !user_has_items(state, conn_id, obj_idx, 1) {
                if add_item_to_user_inventory(state, conn_id, obj_idx, 1) {
                    let rank_name = rank_names.get(jerarquia as usize).unwrap_or(&"");
                    state.send_to(conn_id, &format!("||231@{}@{}", rank_name, god_name)).await;
                    let obj_name = state.game_data.objects.get(obj_idx as usize).map(|o| o.name.clone()).unwrap_or_default();
                    state.send_to(conn_id, &format!("||232@1@{}", obj_name)).await;
                    if let Some(user) = state.users.get_mut(&conn_id) {
                        user.jerarquia_dios = new_jerarquia_target;
                    }
                    send_full_inventory(state, conn_id).await;
                } else {
                    state.send_to(conn_id, "||108").await;
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
        state.send_to(conn_id, &format!("||212@{}", ts_price)).await;
        return;
    }

    if !add_item_to_user_inventory(state, conn_id, obj_index, amount) {
        state.send_to(conn_id, "||108").await;
        return;
    }

    let name = state.game_data.objects.get(obj_index as usize).map(|o| o.name.clone()).unwrap_or_default();
    state.send_to(conn_id, &format!("||232@{}@{}", amount, name)).await;
    if let Some(user) = state.users.get_mut(&conn_id) {
        user.ts_points -= ts_price as i64;
    }
    send_full_inventory(state, conn_id).await;
}

/// SPH — Query upgrade item info (Mejorados.dat).
pub(super) async fn handle_sph(state: &mut GameState, conn_id: ConnectionId, data: &str) {
    let payload = strip_opcode(data, 3);
    let mejorados_path = state.base_path.join("Dat").join("Mejorados.dat");

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

    state.send_to(conn_id, &format!("IMEJ{},{}/{},{}/{},{}/{},{}/{},{},{}",
        nombre, at_min, at_max, def_min, def_max, atm_min, atm_max, defm_min, defm_max, desc, grh)).await;
}

/// SPÉ — Upgrade item (requires octarina gem 1448 + required item).
pub(super) async fn handle_spe(state: &mut GameState, conn_id: ConnectionId, data: &str) {
    let payload = strip_opcode(data, 3);
    let mejorados_path = state.base_path.join("Dat").join("Mejorados.dat");

    let numero_mejorado = ini_get(&mejorados_path, "ITEMS", payload.trim());

    let num: i32 = numero_mejorado.parse().unwrap_or(0);
    if num <= 0 { return; }

    let section = format!("ITEM{}", num);
    let requiere: i32 = ini_get(&mejorados_path, &section, "Requiere").parse().unwrap_or(0);
    let obj_idx: i32 = ini_get(&mejorados_path, &section, "NumObj").parse().unwrap_or(0);

    // Need octarina gem (1448)
    if !user_has_items(state, conn_id, 1448, 1) {
        state.send_to(conn_id, "||235").await;
        return;
    }

    // Need the required item
    if !user_has_items(state, conn_id, requiere, 1) {
        state.send_to(conn_id, "||236").await;
        return;
    }

    if !add_item_to_user_inventory(state, conn_id, obj_idx, 1) {
        state.send_to(conn_id, "||108").await;
        return;
    }

    let name = state.game_data.objects.get(requiere as usize).map(|o| o.name.clone()).unwrap_or_default();
    state.send_to(conn_id, &format!("||237@{}", name)).await;
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
        state.send_to(conn_id, "||291").await;
        return;
    }

    // Need 100k gold
    let gold = state.users.get(&conn_id).map(|u| u.gold).unwrap_or(0);
    if gold < 100000 {
        state.send_to(conn_id, "||215@100.000").await;
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
        state.send_to(conn_id, "||239").await;
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
        state.send_to(conn_id, "||241").await;
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
    state.send_to(conn_id, "||240").await;
}

/// NANVAME — Clan name validated (notify admins).
pub(super) async fn handle_nanvame(state: &mut GameState, conn_id: ConnectionId, _data: &str) {
    let name = state.users.get(&conn_id).map(|u| u.char_name.clone()).unwrap_or_default();
    state.send_data(SendTarget::ToAdmins, &format!("||498@{}", name)).await;
}

/// NANVAMX — Clan name invalid (notify admins).
pub(super) async fn handle_nanvamx(state: &mut GameState, conn_id: ConnectionId, _data: &str) {
    let name = state.users.get(&conn_id).map(|u| u.char_name.clone()).unwrap_or_default();
    state.send_data(SendTarget::ToAdmins, &format!("||499@{}", name)).await;
}

/// PCGF — Forward party/clan GUI data to target user.
pub(super) async fn handle_pcgf(state: &mut GameState, conn_id: ConnectionId, data: &str) {
    let payload = strip_opcode(data, 4);
    let proceso = read_field(1, payload, ',');
    let peso = read_field(2, payload, ',');
    let target_idx: ConnectionId = read_field(3, payload, ',').parse().unwrap_or(0);

    let sender_name = state.users.get(&conn_id).map(|u| u.char_name.clone()).unwrap_or_default();
    if target_idx > 0 && state.users.contains_key(&target_idx) {
        state.send_to(target_idx, &format!("PCGN{},{},{}", proceso, peso, sender_name)).await;
    }
}

/// PCWC — Forward party/clan window command to target user.
pub(super) async fn handle_pcwc(state: &mut GameState, conn_id: ConnectionId, data: &str) {
    let payload = strip_opcode(data, 4);
    let proceso = read_field(1, payload, ',');
    let target_idx: ConnectionId = read_field(2, payload, ',').parse().unwrap_or(0);

    let sender_name = state.users.get(&conn_id).map(|u| u.char_name.clone()).unwrap_or_default();
    if target_idx > 0 && state.users.contains_key(&target_idx) {
        state.send_to(target_idx, &format!("PCSS{},{}", proceso, sender_name)).await;
    }
}

/// PCCC — Forward party/clan caption to target user.
pub(super) async fn handle_pccc(state: &mut GameState, conn_id: ConnectionId, data: &str) {
    let payload = strip_opcode(data, 4);
    let caption = read_field(1, payload, ',');
    let target_idx: ConnectionId = read_field(2, payload, ',').parse().unwrap_or(0);

    let sender_name = state.users.get(&conn_id).map(|u| u.char_name.clone()).unwrap_or_default();
    if target_idx > 0 && state.users.contains_key(&target_idx) {
        state.send_to(target_idx, &format!("PCCC{},{}", caption, sender_name)).await;
    }
}

/// /VOTO — Vote for guild leader candidate.
pub(super) async fn handle_slash_voto(state: &mut GameState, conn_id: ConnectionId, candidate: &str) {
    let guild_idx = state.users.get(&conn_id).map(|u| u.guild_index).unwrap_or(0);
    if guild_idx <= 0 {
        state.send_to(conn_id, &format!("{}No perteneces a ningun clan.{}", server_opcodes::CONSOLE_MSG, font_types::INFO)).await;
        return;
    }

    // Simplified: just acknowledge the vote (full guild elections not implemented yet)
    state.send_to(conn_id, "||439").await;
}

/// /PAREJA — 2vs2 system.
pub(super) async fn handle_slash_pareja(state: &mut GameState, conn_id: ConnectionId, target_name: &str) {
    let target = state.online_names.get(&target_name.to_uppercase()).copied();

    // Command cooldown
    let cooldown = state.users.get(&conn_id).map(|u| u.time_comandos).unwrap_or(0);
    if cooldown > 0 {
        state.send_to(conn_id, "||290").await;
        return;
    }
    if let Some(user) = state.users.get_mut(&conn_id) {
        user.time_comandos = 5;
    }

    let target_id = match target {
        Some(id) => id,
        None => {
            state.send_to(conn_id, "||196").await;
            return;
        }
    };

    if target_id == conn_id { return; }

    // Check gold (300k each)
    let my_gold = state.users.get(&conn_id).map(|u| u.gold).unwrap_or(0);
    if my_gold < 300000 {
        state.send_to(conn_id, "||215@300.000").await;
        return;
    }
    let t_gold = state.users.get(&target_id).map(|u| u.gold).unwrap_or(0);
    if t_gold < 300000 {
        state.send_to(conn_id, "||446").await;
        return;
    }

    // Check dead, in commerce, in cvc etc
    let my_dead = state.users.get(&conn_id).map(|u| u.dead || u.en_cvc || u.comerciando).unwrap_or(true);
    if my_dead {
        state.send_to(conn_id, "||239").await;
        return;
    }

    // Check same class
    let my_class = state.users.get(&conn_id).map(|u| u.class.clone()).unwrap_or_default();
    let t_class = state.users.get(&target_id).map(|u| u.class.clone()).unwrap_or_default();
    if my_class == t_class {
        state.send_to(conn_id, "||448").await;
        return;
    }

    // Check if all 4 slots are full
    if state.pareja[3] > 0 && state.pareja[4] > 0 {
        state.send_to(conn_id, "||406").await;
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
            state.send_to(target_id, &format!("||449@{}", my_name)).await;
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
        state.send_data(SendTarget::ToAll, &format!("||450@{}@{}", my_name, t_name)).await;
    } else if state.pareja[1] > 0 && state.pareja[2] > 0 {
        if !target_wants_me {
            state.send_to(target_id, &format!("||449@{}", my_name)).await;
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
        state.send_data(SendTarget::ToAll, &format!("||451@{}@{}", my_name, t_name)).await;
    }
}

/// /SICV — Accept CvC challenge and start the battle.
pub(super) async fn handle_slash_sicv(state: &mut GameState, conn_id: ConnectionId) {
    let (char_name, guild_idx, dead) = match state.users.get(&conn_id) {
        Some(u) if u.logged => (u.char_name.clone(), u.guild_index, u.dead),
        _ => return,
    };

    if dead {
        state.send_to(conn_id, &format!("{}Estas muerto!{}", server_opcodes::CONSOLE_MSG, font_types::INFO)).await;
        return;
    }
    if guild_idx < 1 {
        state.send_to(conn_id, "||120").await;
        return;
    }
    if state.cvc_funciona {
        state.send_to(conn_id, "||364").await;
        return;
    }

    // Check there is a pending challenge for this guild
    if state.cvc_pending_target_guild != guild_idx {
        state.send_to(conn_id, &format!("{}No hay un desafio CvC pendiente para tu clan.{}", server_opcodes::CONSOLE_MSG, font_types::INFO)).await;
        return;
    }

    // Validate caller is leader of target guild
    let my_guild = guilds::load_guild(&state.pool, guild_idx).await;
    let is_leader = match &my_guild {
        Some(g) => g.leader.to_uppercase() == char_name.to_uppercase(),
        None => false,
    };
    if !is_leader {
        state.send_to(conn_id, &format!("{}Solo el lider puede aceptar el desafio CvC.{}", server_opcodes::CONSOLE_MSG, font_types::INFO)).await;
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
        state.send_to(conn_id, &format!("{}Ambos clanes necesitan al menos 1 miembro elegible.{}", server_opcodes::CONSOLE_MSG, font_types::INFO)).await;
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
        state.send_to(conn_id, &format!("{}No tienes suficiente oro ({} requeridos).{}", server_opcodes::CONSOLE_MSG, CVC_COST, font_types::INFO)).await;
        return;
    }
    if challenger_gold < CVC_COST {
        state.send_to(conn_id, &format!("{}El lider del clan desafiante no tiene suficiente oro.{}", server_opcodes::CONSOLE_MSG, font_types::INFO)).await;
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
    let pkt = format!("||85@{}@{}", acceptor_name, challenger_name);
    state.send_data(SendTarget::ToAll, &pkt).await;
}

// =====================================================================
// Integration tests — full client login flow
// =====================================================================

