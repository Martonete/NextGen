//! Social system handlers: factions, mail, friends, utility slash commands.
//! Extracted from mod.rs to reduce file size.

use tracing::info;
use crate::net::ConnectionId;
use crate::game::types::{GameState, SendTarget};
use crate::protocol::{font_index, fields::read_field, binary_packets};
use crate::db::charfile;
use super::common::*;


// =====================================================================
// Faction system handlers (ModFacciones.bas)
// =====================================================================

/// Kill thresholds for faction tiers
const FACTION_TIER_THRESHOLDS: [i32; 4] = [50, 100, 200, 350];

/// /ENLISTAR — Join a faction (Royal Army or Chaos Forces).
/// VB6: requires NPC target (type 5), distance <= 4, not dead.
pub(super) async fn handle_slash_enlistar(state: &mut GameState, conn_id: ConnectionId) {
    let (target_npc, dead) = match state.users.get(&conn_id) {
        Some(u) if u.logged => (u.target_npc, u.dead),
        _ => return,
    };

    // VB6: If TargetNPC = 0 Then ||9
    if target_npc == 0 {
        state.send_msg_id(conn_id, 9, "").await;
        return;
    }

    // VB6: NPCtype must be 5 (faction officer) AND not dead
    let npc_type_num = state.get_npc(target_npc).map(|n| n.npc_type as i32).unwrap_or(0);
    if npc_type_num != 5 || dead { return; }

    // VB6: distance > 4 → ||158
    let (npc_map, npc_x, npc_y) = match state.get_npc(target_npc) {
        Some(npc) => (npc.map, npc.x, npc.y),
        None => return,
    };
    let (u_map, u_x, u_y) = match state.users.get(&conn_id) {
        Some(u) => (u.pos_map, u.pos_x, u.pos_y),
        None => return,
    };
    if u_map != npc_map || (u_x - npc_x).abs() > 4 || (u_y - npc_y).abs() > 4 {
        state.send_msg_id(conn_id, 158, "").await;
        return;
    }

    let user_data = match state.users.get(&conn_id) {
        Some(u) if u.logged => (
            u.armada_real, u.fuerzas_caos, u.criminal,
            u.criminales_matados, u.ciudadanos_matados,
            u.reenlistadas, u.char_name.clone(),
            u.guild_index,
        ),
        _ => return,
    };
    let (armada, caos, criminal, crim_killed, ciud_killed, reenlistadas, char_name, guild_index) = user_data;

    if armada || caos {
        state.send_console(conn_id, "Ya perteneces a una faccion.", font_index::INFO).await;
        return;
    }

    if reenlistadas {
        state.send_console(conn_id, "Ya no puedes enlistarte nuevamente.", font_index::INFO).await;
        return;
    }

    if !criminal {
        // Try to join Royal Army
        if crim_killed < 50 {
            state.send_console(conn_id, "Necesitas haber matado al menos 50 criminales.", font_index::INFO).await;
            return;
        }

        if let Some(user) = state.users.get_mut(&conn_id) {
            user.armada_real = true;
        }

        // Award initial XP (50000)
        if let Some(user) = state.users.get_mut(&conn_id) {
            user.exp += 50000;
        }
        send_stats_exp(state, conn_id).await;

        state.send_console(conn_id, "Te has enlistado en la Armada Real!", font_index::GUILD_MSG).await;

        // Broadcast
        state.send_msg_id_to(SendTarget::ToAll, 851, &char_name).await;
    } else {
        // Try to join Chaos Forces
        if ciud_killed < 50 {
            state.send_console(conn_id, "Necesitas haber matado al menos 50 ciudadanos.", font_index::INFO).await;
            return;
        }

        if let Some(user) = state.users.get_mut(&conn_id) {
            user.fuerzas_caos = true;
        }

        // Award initial XP (50000)
        if let Some(user) = state.users.get_mut(&conn_id) {
            user.exp += 50000;
        }
        send_stats_exp(state, conn_id).await;

        state.send_console(conn_id, "Te has enlistado en las Fuerzas del Caos!", font_index::GUILD_MSG).await;

        // Broadcast
        state.send_msg_id_to(SendTarget::ToAll, 852, &char_name).await;
    }
}

/// /INFORMACION — Display faction status.
pub(super) async fn handle_slash_faction_info(state: &mut GameState, conn_id: ConnectionId) {
    let user_data = match state.users.get(&conn_id) {
        Some(u) if u.logged => (
            u.armada_real, u.fuerzas_caos,
            u.criminales_matados, u.ciudadanos_matados,
            u.recompensas_real, u.recompensas_caos,
        ),
        _ => return,
    };
    let (armada, caos, crim_killed, ciud_killed, rec_real, rec_caos) = user_data;

    if armada {
        state.send_console(conn_id, "--- Armada Real ---", font_index::GUILD_MSG).await;
        state.send_console(conn_id, &format!("Criminales matados: {}", crim_killed), font_index::INFO).await;
        state.send_console(conn_id, &format!("Rango: {}", faction_rank_name(rec_real, true)), font_index::INFO).await;
    } else if caos {
        state.send_console(conn_id, "--- Fuerzas del Caos ---", font_index::GUILD_MSG).await;
        state.send_console(conn_id, &format!("Ciudadanos matados: {}", ciud_killed), font_index::INFO).await;
        state.send_console(conn_id, &format!("Rango: {}", faction_rank_name(rec_caos, false)), font_index::INFO).await;
    } else {
        state.send_console(conn_id, "No perteneces a ninguna faccion.", font_index::INFO).await;
        state.send_console(conn_id, &format!("Criminales matados: {}", crim_killed), font_index::INFO).await;
        state.send_console(conn_id, &format!("Ciudadanos matados: {}", ciud_killed), font_index::INFO).await;
    }
}

/// Get faction rank name
pub(super) fn faction_rank_name(tier: i32, is_royal: bool) -> &'static str {
    if is_royal {
        match tier {
            0 => "Recluta",
            1 => "Soldado Imperial",
            2 => "Capitan Imperial",
            3 => "Comandante Imperial",
            4 => "General Imperial",
            _ => "Caballero de la Luz",
        }
    } else {
        match tier {
            0 => "Recluta Oscuro",
            1 => "Soldado del Caos",
            2 => "Capitan del Caos",
            3 => "Comandante del Caos",
            4 => "General del Caos",
            _ => "Caballero de las Sombras",
        }
    }
}

/// /RECOMPENSA — Claim faction tier reward. VB6: requires NPC type 5, distance <= 4.
pub(super) async fn handle_slash_recompensa(state: &mut GameState, conn_id: ConnectionId) {
    let (target_npc, dead) = match state.users.get(&conn_id) {
        Some(u) if u.logged => (u.target_npc, u.dead),
        _ => return,
    };

    // VB6: If TargetNPC = 0 Then ||9
    if target_npc == 0 {
        state.send_msg_id(conn_id, 9, "").await;
        return;
    }

    // VB6: NPCtype must be 5, not dead
    let npc_type_num = state.get_npc(target_npc).map(|n| n.npc_type as i32).unwrap_or(0);
    if npc_type_num != 5 || dead { return; }

    // VB6: distance > 4 → ||12
    let (npc_map, npc_x, npc_y) = match state.get_npc(target_npc) {
        Some(npc) => (npc.map, npc.x, npc.y),
        None => return,
    };
    let (u_map, u_x, u_y) = match state.users.get(&conn_id) {
        Some(u) => (u.pos_map, u.pos_x, u.pos_y),
        None => return,
    };
    if u_map != npc_map || (u_x - npc_x).abs() > 4 || (u_y - npc_y).abs() > 4 {
        state.send_msg_id(conn_id, 12, "").await;
        return;
    }

    let user_data = match state.users.get(&conn_id) {
        Some(u) if u.logged => (
            u.armada_real, u.fuerzas_caos,
            u.criminales_matados, u.ciudadanos_matados,
            u.recompensas_real, u.recompensas_caos,
            u.char_name.clone(),
        ),
        _ => return,
    };
    let (armada, caos, crim_killed, ciud_killed, rec_real, rec_caos, char_name) = user_data;

    if !armada && !caos {
        state.send_console(conn_id, "No perteneces a ninguna faccion.", font_index::INFO).await;
        return;
    }

    if armada {
        let current_tier = rec_real;
        if current_tier >= 4 {
            state.send_console(conn_id, "Ya has alcanzado el rango maximo.", font_index::INFO).await;
            return;
        }

        let needed = FACTION_TIER_THRESHOLDS[current_tier as usize];
        if crim_killed < needed {
            state.send_console(conn_id, &format!("Necesitas {} criminales matados para el siguiente rango (tienes {}).", needed, crim_killed), font_index::INFO).await;
            return;
        }

        // Advance tier
        let new_tier = current_tier + 1;
        if let Some(user) = state.users.get_mut(&conn_id) {
            user.recompensas_real = new_tier;
        }

        state.send_console(conn_id, &format!("Has ascendido al rango: {}!", faction_rank_name(new_tier, true)), font_index::GUILD_MSG).await;
    } else {
        let current_tier = rec_caos;
        if current_tier >= 4 {
            state.send_console(conn_id, "Ya has alcanzado el rango maximo.", font_index::INFO).await;
            return;
        }

        let needed = FACTION_TIER_THRESHOLDS[current_tier as usize];
        if ciud_killed < needed {
            state.send_console(conn_id, &format!("Necesitas {} ciudadanos matados para el siguiente rango (tienes {}).", needed, ciud_killed), font_index::INFO).await;
            return;
        }

        let new_tier = current_tier + 1;
        if let Some(user) = state.users.get_mut(&conn_id) {
            user.recompensas_caos = new_tier;
        }

        state.send_console(conn_id, &format!("Has ascendido al rango: {}!", faction_rank_name(new_tier, false)), font_index::GUILD_MSG).await;
    }
}

/// /RENUNCIA — Leave faction.
pub(super) async fn handle_slash_renunciar(state: &mut GameState, conn_id: ConnectionId) {
    let user_data = match state.users.get(&conn_id) {
        Some(u) if u.logged => (
            u.armada_real, u.fuerzas_caos,
            u.char_name.clone(), u.guild_index,
        ),
        _ => return,
    };
    let (armada, caos, char_name, guild_index) = user_data;

    if !armada && !caos {
        state.send_console(conn_id, "No perteneces a ninguna faccion.", font_index::INFO).await;
        return;
    }

    // Cannot leave faction while in a guild
    if guild_index > 0 {
        state.send_msg_id(conn_id, 302, "").await;
        return;
    }

    if let Some(user) = state.users.get_mut(&conn_id) {
        user.armada_real = false;
        user.fuerzas_caos = false;
        user.reenlistadas = true; // Can never re-enlist
    }

    state.send_console(conn_id, "Has renunciado a tu faccion.", font_index::INFO).await;
}

// =====================================================================
// Utility slash command handlers
// =====================================================================

/// /ONLINE — Show online player count.
pub(super) async fn handle_slash_online(state: &mut GameState, conn_id: ConnectionId) {
    let count = state.num_users;
    let record = state.record_users;
    state.send_console(conn_id, &format!("Jugadores online: {}. Record: {}.", count, record), font_index::INFO).await;
}

/// /BALANCE — Show gold and bank gold.
pub(super) async fn handle_slash_balance(state: &mut GameState, conn_id: ConnectionId) {
    let (gold, bank_gold) = match state.users.get(&conn_id) {
        Some(u) if u.logged => (u.gold, u.bank_gold),
        _ => return,
    };
    state.send_console(conn_id, &format!("Oro: {}. En banco: {}. Total: {}.", gold, bank_gold, gold + bank_gold), font_index::INFO).await;
}

/// /GLOBAL <text> — Send global chat message.
pub(super) async fn handle_slash_global(state: &mut GameState, conn_id: ConnectionId, text: &str) {
    let (char_name, priv_level) = match state.users.get(&conn_id) {
        Some(u) if u.logged => (u.char_name.clone(), u.privileges),
        _ => return,
    };

    // VB6: If ChatGlobal == False and user is not staff → blocked
    if !state.chat_global && priv_level == 0 {
        state.send_msg_id(conn_id, 549, "").await;
        return;
    }

    if text.contains('~') { return; }

    state.send_guild_chat_to(SendTarget::ToAll, &format!("{}> {}", char_name, text)).await;
}

/// /STATS or /EST — Show character stats summary.
pub(super) async fn handle_slash_stats(state: &mut GameState, conn_id: ConnectionId) {
    let u = match state.users.get(&conn_id) {
        Some(u) if u.logged => u,
        _ => return,
    };

    let char_name = u.char_name.clone();
    let class = u.class.clone();
    let race = u.race.clone();
    let level = u.level;
    let (min_hp, max_hp) = (u.min_hp, u.max_hp);
    let (min_mana, max_mana) = (u.min_mana, u.max_mana);
    let (min_sta, max_sta) = (u.min_sta, u.max_sta);
    let attrs = u.attributes.clone();
    let gold = u.gold;
    let exp = u.exp;

    state.send_console(conn_id, &format!("--- Estadisticas de {} ---", char_name), font_index::GUILD_MSG).await;
    state.send_console(conn_id, &format!("Clase: {} | Raza: {} | Nivel: {}", class, race, level), font_index::INFO).await;
    state.send_console(conn_id, &format!("HP: {}/{} | Mana: {}/{} | STA: {}/{}", min_hp, max_hp, min_mana, max_mana, min_sta, max_sta), font_index::INFO).await;
    state.send_console(conn_id, &format!("Fuerza: {} | Agilidad: {} | Inteligencia: {}", attrs[0], attrs[1], attrs[2]), font_index::INFO).await;
    state.send_console(conn_id, &format!("Carisma: {} | Constitucion: {}", attrs[3], attrs[4]), font_index::INFO).await;
    state.send_console(conn_id, &format!("Oro: {} | EXP: {}", gold, exp), font_index::INFO).await;
}

// =====================================================================
// Mail system handlers (AA_Correos.bas)
// =====================================================================

const MAX_MAILS: usize = 30;

/// CZM — Send mail. Format: CZM<destinatario>$<asunto>$<mensaje>$,<items>
pub(super) async fn handle_mail_send(state: &mut GameState, conn_id: ConnectionId, data: &str) {
    let payload = strip_opcode(data, 3);
    let parts: Vec<&str> = payload.splitn(4, '$').collect();

    if parts.len() < 3 {
        return;
    }

    let recipient_name = parts[0];
    let subject = parts[1];
    let message = parts[2];

    let sender_name = match state.users.get(&conn_id) {
        Some(u) if u.logged => u.char_name.clone(),
        _ => return,
    };

    // Validate recipient exists
    if !charfile::character_exists(&state.pool, recipient_name).await {
        state.send_console(conn_id, "El personaje no existe.", font_index::INFO).await;
        return;
    }

    let pool = state.pool.clone();
    let now = chrono_like_date();

    match crate::db::mail::send_mail(&pool, recipient_name, &sender_name, subject, message, &now).await {
        Ok(()) => {
            // Notify recipient if online
            if let Some(&target_conn) = state.online_names.get(&recipient_name.to_uppercase()) {
                state.send_msg_id(target_conn, 631, "").await;
            }
            state.send_console(conn_id, &format!("Correo enviado a {}.", recipient_name), font_index::INFO).await;
        }
        Err(e) if e.contains("full") => {
            state.send_msg_id(conn_id, 629, "").await;
        }
        Err(_) => {
            state.send_console(conn_id, "Error al enviar correo.", font_index::INFO).await;
        }
    }
}

/// CZC — Open/read mail slot.
pub(super) async fn handle_mail_open(state: &mut GameState, conn_id: ConnectionId, data: &str) {
    let payload = strip_opcode(data, 3);
    let slot: usize = payload.trim().parse().unwrap_or(0);

    let char_name = match state.users.get(&conn_id) {
        Some(u) if u.logged => u.char_name.clone(),
        _ => return,
    };

    if slot == 0 {
        send_mail_list(state, conn_id, &char_name).await;
        return;
    }

    let pool = state.pool.clone();
    let char_id = match charfile::get_char_id(&pool, &char_name).await {
        Ok(id) => id,
        Err(_) => return,
    };

    let mails = crate::db::mail::load_mails(&pool, char_id).await;
    let idx = slot - 1;
    if idx >= mails.len() { return; }

    let mail = &mails[idx];
    crate::db::mail::mark_read(&pool, mail.id).await;

    // Send content in VB6 format: sender$subject$message$date$
    let content = format!("{}${}${}${}$", mail.sender, mail.subject, mail.message, mail.sent_at);
    let pkt = binary_packets::write_mail_content(&content);
    state.send_bytes(conn_id, &pkt).await;
}

/// CZB — Delete mail.
pub(super) async fn handle_mail_delete(state: &mut GameState, conn_id: ConnectionId, data: &str) {
    let payload = strip_opcode(data, 3);
    let slot: usize = payload.trim().parse().unwrap_or(0);

    let char_name = match state.users.get(&conn_id) {
        Some(u) if u.logged => u.char_name.clone(),
        _ => return,
    };

    let pool = state.pool.clone();
    let char_id = match charfile::get_char_id(&pool, &char_name).await {
        Ok(id) => id,
        Err(_) => return,
    };

    let mails = crate::db::mail::load_mails(&pool, char_id).await;
    let idx = slot - 1;
    if slot == 0 || idx >= mails.len() { return; }

    crate::db::mail::delete_mail(&pool, mails[idx].id).await;

    send_mail_list(state, conn_id, &char_name).await;
}

/// CZR — Extract items from mail (simplified — no item attachment in basic impl).
pub(super) async fn handle_mail_extract(state: &mut GameState, conn_id: ConnectionId, _data: &str) {
    state.send_console(conn_id, "Este correo no tiene objetos adjuntos.", font_index::INFO).await;
}

/// Send mail list to player.
pub(super) async fn send_mail_list(state: &mut GameState, conn_id: ConnectionId, char_name: &str) {
    let pool = state.pool.clone();
    let char_id = match charfile::get_char_id(&pool, char_name).await {
        Ok(id) => id,
        Err(_) => return,
    };

    let mails = crate::db::mail::load_mails(&pool, char_id).await;

    let mut entries = Vec::new();
    for i in 0..MAX_MAILS {
        if i < mails.len() {
            let mail = &mails[i];
            let new_tag = if mail.is_new { " (NUEVO)" } else { "" };
            entries.push(format!("{}{}", mail.sender, new_tag));
        } else {
            entries.push(String::new());
        }
    }

    let pkt = binary_packets::write_mail_list(&entries.join(","));
    state.send_bytes(conn_id, &pkt).await;
}

// =====================================================================
// Friend list handlers
// =====================================================================

/// Broadcast KFM + updated LDM to all online users who have `char_name` in their friend list.
/// Called after a user finishes logging in.
pub(super) async fn broadcast_friend_connect(state: &mut GameState, conn_id: ConnectionId) {
    let char_name = match state.users.get(&conn_id) {
        Some(u) if u.logged => u.char_name.clone(),
        _ => return,
    };

    let pool = state.pool.clone();

    // Collect all other logged users' conn_ids and account names
    let others: Vec<(ConnectionId, String)> = state.users.iter()
        .filter(|&(&cid, ref u)| cid != conn_id && u.logged)
        .map(|(&cid, u)| (cid, u.account_name.clone()))
        .collect();

    for (other_conn, account) in others {
        if crate::db::friends::is_friend_of_account_name(&pool, &account, &char_name).await {
            let kfm = binary_packets::write_friend_online(&char_name);
            state.send_bytes(other_conn, &kfm).await;
            send_friend_list(state, other_conn).await;
        }
    }
}

/// Broadcast DFM + updated LDM to all online users who have `name` in their friend list.
/// Called before a user is removed on disconnect.
pub async fn broadcast_friend_disconnect(state: &mut GameState, conn_id: ConnectionId) {
    let char_name = match state.users.get(&conn_id) {
        Some(u) if u.logged => u.char_name.clone(),
        _ => return,
    };

    let pool = state.pool.clone();

    let others: Vec<(ConnectionId, String)> = state.users.iter()
        .filter(|&(&cid, ref u)| cid != conn_id && u.logged)
        .map(|(&cid, u)| (cid, u.account_name.clone()))
        .collect();

    for (other_conn, account) in others {
        if crate::db::friends::is_friend_of_account_name(&pool, &account, &char_name).await {
            let dfm = binary_packets::write_friend_offline(&char_name);
            state.send_bytes(other_conn, &dfm).await;
            send_friend_list(state, other_conn).await;
        }
    }
}

/// ADDCON<name> — Add friend.
pub(super) async fn handle_friend_add(state: &mut GameState, conn_id: ConnectionId, data: &str) {
    let friend_name = strip_opcode(data, 6).trim().to_string();

    let account_id = match state.users.get(&conn_id) {
        Some(u) if u.logged => u.account_id,
        _ => return,
    };

    // VB6: Can't add self
    let self_name = state.users.get(&conn_id).map(|u| u.char_name.clone()).unwrap_or_default();
    if friend_name.to_uppercase() == self_name.to_uppercase() {
        let pkt = binary_packets::write_message_box("No puedes agregarte a ti mismo.");
        state.send_bytes(conn_id, &pkt).await;
        return;
    }

    if !charfile::character_exists(&state.pool, &friend_name).await {
        let pkt = binary_packets::write_message_box("El personaje no existe.");
        state.send_bytes(conn_id, &pkt).await;
        return;
    }

    // VB6: Can't add GMs
    if let Ok(char_data) = charfile::load_charfile(&state.pool, &friend_name).await {
        if char_data.privileges > 0 {
            let pkt = binary_packets::write_message_box("No podes agregar GM's.");
            state.send_bytes(conn_id, &pkt).await;
            return;
        }
    }

    let pool = state.pool.clone();
    match crate::db::friends::add_friend(&pool, account_id, &friend_name).await {
        Ok(()) => {
            send_friend_list(state, conn_id).await;
            state.send_console(conn_id, &format!("{} agregado a tu lista de amigos.", friend_name), font_index::INFO).await;
        }
        Err(msg) => {
            let pkt = binary_packets::write_message_box(&msg);
            state.send_bytes(conn_id, &pkt).await;
        }
    }
}

/// BORRAC<index> — Remove friend by slot.
pub(super) async fn handle_friend_remove(state: &mut GameState, conn_id: ConnectionId, data: &str) {
    let payload = strip_opcode(data, 6);
    let slot: usize = payload.trim().parse().unwrap_or(0);

    let account_id = match state.users.get(&conn_id) {
        Some(u) if u.logged => u.account_id,
        _ => return,
    };

    let pool = state.pool.clone();
    let _ = crate::db::friends::remove_friend(&pool, account_id, slot).await;

    send_friend_list(state, conn_id).await;
}

/// Send friend list to player (VB6 SendFriendList in modGuilds.bas).
/// Format: LDM<count>,name1(ON),name2(OFF),...
pub(super) async fn send_friend_list(state: &mut GameState, conn_id: ConnectionId) {
    let account_id = match state.users.get(&conn_id) {
        Some(u) if u.logged => u.account_id,
        _ => return,
    };

    let pool = state.pool.clone();
    let friends = crate::db::friends::load_friends(&pool, account_id).await;
    let count = friends.len();

    // Build VB6 format: "count,name1(ON),name2(OFF),"
    let mut result = format!("{},", count);
    for friend_name in &friends {
        if friend_name.is_empty() || friend_name.to_uppercase() == "(NADIE)" {
            result.push_str("(NADIE)(OFF),");
        } else {
            let is_online = state.users.values()
                .any(|u| u.logged && u.char_name.to_uppercase() == friend_name.to_uppercase());
            if is_online {
                result.push_str(&format!("{}(ON),", friend_name));
            } else {
                result.push_str(&format!("{}(OFF),", friend_name));
            }
        }
    }

    let pkt = binary_packets::write_friend_list(&result);
    state.send_bytes(conn_id, &pkt).await;
}

// =====================================================================
