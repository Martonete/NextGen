//! Social system handlers: factions, mail, friends, utility slash commands.
//! Extracted from mod.rs to reduce file size.

use tracing::info;
use crate::net::ConnectionId;
use crate::game::types::{GameState, SendTarget};
use crate::protocol::{server_opcodes, font_types, fields::read_field};
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
        state.send_to(conn_id, "||9").await;
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
        state.send_to(conn_id, "||158").await;
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
        let msg = format!("{}Ya perteneces a una faccion.{}", server_opcodes::CONSOLE_MSG, font_types::INFO);
        state.send_to(conn_id, &msg).await;
        return;
    }

    if reenlistadas {
        let msg = format!("{}Ya no puedes enlistarte nuevamente.{}", server_opcodes::CONSOLE_MSG, font_types::INFO);
        state.send_to(conn_id, &msg).await;
        return;
    }

    let base = state.base_path.clone();

    if !criminal {
        // Try to join Royal Army
        if crim_killed < 50 {
            let msg = format!("{}Necesitas haber matado al menos 50 criminales.{}", server_opcodes::CONSOLE_MSG, font_types::INFO);
            state.send_to(conn_id, &msg).await;
            return;
        }

        if let Some(user) = state.users.get_mut(&conn_id) {
            user.armada_real = true;
        }

        // Save to charfile
        let chr_path = base.join("charfile").join(format!("{}.chr", char_name));
        let chr = chr_path.to_str().unwrap_or("");
        crate::config::write_var(chr, "FACCIONES", "EjercitoReal", "1").ok();
        crate::config::write_var(chr, "FACCIONES", "rExReal", "1").ok();

        // Award initial XP (50000)
        if let Some(user) = state.users.get_mut(&conn_id) {
            user.exp += 50000;
        }
        send_stats_exp(state, conn_id).await;

        let msg = format!("{}Te has enlistado en la Armada Real!{}", server_opcodes::CONSOLE_MSG, font_types::GUILD_MSG);
        state.send_to(conn_id, &msg).await;

        // Broadcast
        let broadcast = format!("{}851@{}", server_opcodes::CONSOLE_MSG_ID, char_name);
        state.send_data(SendTarget::ToAll, &broadcast).await;
    } else {
        // Try to join Chaos Forces
        if ciud_killed < 50 {
            let msg = format!("{}Necesitas haber matado al menos 50 ciudadanos.{}", server_opcodes::CONSOLE_MSG, font_types::INFO);
            state.send_to(conn_id, &msg).await;
            return;
        }

        // Cannot join chaos if ever received royal XP
        let chr_path = base.join("charfile").join(format!("{}.chr", char_name));
        let chr = chr_path.to_str().unwrap_or("");
        let had_royal_xp = crate::config::get_var(chr, "FACCIONES", "rExReal") == "1";
        if had_royal_xp {
            let msg = format!("{}No puedes unirte al Caos habiendo sido parte de la Armada.{}", server_opcodes::CONSOLE_MSG, font_types::INFO);
            state.send_to(conn_id, &msg).await;
            return;
        }

        if let Some(user) = state.users.get_mut(&conn_id) {
            user.fuerzas_caos = true;
        }

        crate::config::write_var(chr, "FACCIONES", "EjercitoCaos", "1").ok();
        crate::config::write_var(chr, "FACCIONES", "rExCaos", "1").ok();

        // Award initial XP (50000)
        if let Some(user) = state.users.get_mut(&conn_id) {
            user.exp += 50000;
        }
        send_stats_exp(state, conn_id).await;

        let msg = format!("{}Te has enlistado en las Fuerzas del Caos!{}", server_opcodes::CONSOLE_MSG, font_types::GUILD_MSG);
        state.send_to(conn_id, &msg).await;

        // Broadcast
        let broadcast = format!("{}852@{}", server_opcodes::CONSOLE_MSG_ID, char_name);
        state.send_data(SendTarget::ToAll, &broadcast).await;
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
        let msg = format!("{}--- Armada Real ---{}", server_opcodes::CONSOLE_MSG, font_types::GUILD_MSG);
        state.send_to(conn_id, &msg).await;
        let msg = format!("{}Criminales matados: {}{}", server_opcodes::CONSOLE_MSG, crim_killed, font_types::INFO);
        state.send_to(conn_id, &msg).await;
        let msg = format!("{}Rango: {}{}", server_opcodes::CONSOLE_MSG, faction_rank_name(rec_real, true), font_types::INFO);
        state.send_to(conn_id, &msg).await;
    } else if caos {
        let msg = format!("{}--- Fuerzas del Caos ---{}", server_opcodes::CONSOLE_MSG, font_types::GUILD_MSG);
        state.send_to(conn_id, &msg).await;
        let msg = format!("{}Ciudadanos matados: {}{}", server_opcodes::CONSOLE_MSG, ciud_killed, font_types::INFO);
        state.send_to(conn_id, &msg).await;
        let msg = format!("{}Rango: {}{}", server_opcodes::CONSOLE_MSG, faction_rank_name(rec_caos, false), font_types::INFO);
        state.send_to(conn_id, &msg).await;
    } else {
        let msg = format!("{}No perteneces a ninguna faccion.{}", server_opcodes::CONSOLE_MSG, font_types::INFO);
        state.send_to(conn_id, &msg).await;
        let msg = format!("{}Criminales matados: {}{}", server_opcodes::CONSOLE_MSG, crim_killed, font_types::INFO);
        state.send_to(conn_id, &msg).await;
        let msg = format!("{}Ciudadanos matados: {}{}", server_opcodes::CONSOLE_MSG, ciud_killed, font_types::INFO);
        state.send_to(conn_id, &msg).await;
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
        state.send_to(conn_id, "||9").await;
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
        state.send_to(conn_id, "||12").await;
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
        let msg = format!("{}No perteneces a ninguna faccion.{}", server_opcodes::CONSOLE_MSG, font_types::INFO);
        state.send_to(conn_id, &msg).await;
        return;
    }

    let base = state.base_path.clone();
    let chr_path = base.join("charfile").join(format!("{}.chr", char_name));
    let chr = chr_path.to_str().unwrap_or("");

    if armada {
        let current_tier = rec_real;
        if current_tier >= 4 {
            let msg = format!("{}Ya has alcanzado el rango maximo.{}", server_opcodes::CONSOLE_MSG, font_types::INFO);
            state.send_to(conn_id, &msg).await;
            return;
        }

        let needed = FACTION_TIER_THRESHOLDS[current_tier as usize];
        if crim_killed < needed {
            let msg = format!("{}Necesitas {} criminales matados para el siguiente rango (tienes {}).{}", server_opcodes::CONSOLE_MSG, needed, crim_killed, font_types::INFO);
            state.send_to(conn_id, &msg).await;
            return;
        }

        // Advance tier
        let new_tier = current_tier + 1;
        if let Some(user) = state.users.get_mut(&conn_id) {
            user.recompensas_real = new_tier;
        }
        crate::config::write_var(chr, "FACCIONES", "recReal", &new_tier.to_string()).ok();

        let msg = format!("{}Has ascendido al rango: {}!{}", server_opcodes::CONSOLE_MSG, faction_rank_name(new_tier, true), font_types::GUILD_MSG);
        state.send_to(conn_id, &msg).await;
    } else {
        let current_tier = rec_caos;
        if current_tier >= 4 {
            let msg = format!("{}Ya has alcanzado el rango maximo.{}", server_opcodes::CONSOLE_MSG, font_types::INFO);
            state.send_to(conn_id, &msg).await;
            return;
        }

        let needed = FACTION_TIER_THRESHOLDS[current_tier as usize];
        if ciud_killed < needed {
            let msg = format!("{}Necesitas {} ciudadanos matados para el siguiente rango (tienes {}).{}", server_opcodes::CONSOLE_MSG, needed, ciud_killed, font_types::INFO);
            state.send_to(conn_id, &msg).await;
            return;
        }

        let new_tier = current_tier + 1;
        if let Some(user) = state.users.get_mut(&conn_id) {
            user.recompensas_caos = new_tier;
        }
        crate::config::write_var(chr, "FACCIONES", "recCaos", &new_tier.to_string()).ok();

        let msg = format!("{}Has ascendido al rango: {}!{}", server_opcodes::CONSOLE_MSG, faction_rank_name(new_tier, false), font_types::GUILD_MSG);
        state.send_to(conn_id, &msg).await;
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
        let msg = format!("{}No perteneces a ninguna faccion.{}", server_opcodes::CONSOLE_MSG, font_types::INFO);
        state.send_to(conn_id, &msg).await;
        return;
    }

    // Cannot leave faction while in a guild
    if guild_index > 0 {
        let msg = format!("{}302", server_opcodes::CONSOLE_MSG_ID);
        state.send_to(conn_id, &msg).await;
        return;
    }

    let base = state.base_path.clone();
    let chr_path = base.join("charfile").join(format!("{}.chr", char_name));
    let chr = chr_path.to_str().unwrap_or("");

    if let Some(user) = state.users.get_mut(&conn_id) {
        user.armada_real = false;
        user.fuerzas_caos = false;
        user.reenlistadas = true; // Can never re-enlist
    }

    crate::config::write_var(chr, "FACCIONES", "EjercitoReal", "0").ok();
    crate::config::write_var(chr, "FACCIONES", "EjercitoCaos", "0").ok();
    crate::config::write_var(chr, "FACCIONES", "Reenlistadas", "1").ok();

    let msg = format!("{}Has renunciado a tu faccion.{}", server_opcodes::CONSOLE_MSG, font_types::INFO);
    state.send_to(conn_id, &msg).await;
}

// =====================================================================
// Utility slash command handlers
// =====================================================================

/// /ONLINE — Show online player count.
pub(super) async fn handle_slash_online(state: &mut GameState, conn_id: ConnectionId) {
    let count = state.num_users;
    let record = state.record_users;
    let msg = format!("{}Jugadores online: {}. Record: {}.{}", server_opcodes::CONSOLE_MSG, count, record, font_types::INFO);
    state.send_to(conn_id, &msg).await;
}

/// /BALANCE — Show gold and bank gold.
pub(super) async fn handle_slash_balance(state: &mut GameState, conn_id: ConnectionId) {
    let (gold, bank_gold) = match state.users.get(&conn_id) {
        Some(u) if u.logged => (u.gold, u.bank_gold),
        _ => return,
    };
    let msg = format!("{}Oro: {}. En banco: {}. Total: {}.{}", server_opcodes::CONSOLE_MSG, gold, bank_gold, gold + bank_gold, font_types::INFO);
    state.send_to(conn_id, &msg).await;
}

/// /GLOBAL <text> — Send global chat message.
pub(super) async fn handle_slash_global(state: &mut GameState, conn_id: ConnectionId, text: &str) {
    let (char_name, priv_level) = match state.users.get(&conn_id) {
        Some(u) if u.logged => (u.char_name.clone(), u.privileges),
        _ => return,
    };

    // VB6: If ChatGlobal == False and user is not staff → blocked
    if !state.chat_global && priv_level == 0 {
        state.send_to(conn_id, "||549").await;
        return;
    }

    if text.contains('~') { return; }

    let pkt = format!("{}{}> {}{}", server_opcodes::GUILD_CHAT, char_name, text, font_types::GUILD);
    state.send_data(SendTarget::ToAll, &pkt).await;
}

/// /STATS or /EST — Show character stats summary.
pub(super) async fn handle_slash_stats(state: &mut GameState, conn_id: ConnectionId) {
    let u = match state.users.get(&conn_id) {
        Some(u) if u.logged => u,
        _ => return,
    };

    let lines = vec![
        format!("{}--- Estadisticas de {} ---{}", server_opcodes::CONSOLE_MSG, u.char_name, font_types::GUILD_MSG),
        format!("{}Clase: {} | Raza: {} | Nivel: {}{}", server_opcodes::CONSOLE_MSG, u.class, u.race, u.level, font_types::INFO),
        format!("{}HP: {}/{} | Mana: {}/{} | STA: {}/{}{}", server_opcodes::CONSOLE_MSG, u.min_hp, u.max_hp, u.min_mana, u.max_mana, u.min_sta, u.max_sta, font_types::INFO),
        format!("{}Fuerza: {} | Agilidad: {} | Inteligencia: {}{}", server_opcodes::CONSOLE_MSG, u.attributes[0], u.attributes[1], u.attributes[2], font_types::INFO),
        format!("{}Carisma: {} | Constitucion: {}{}", server_opcodes::CONSOLE_MSG, u.attributes[3], u.attributes[4], font_types::INFO),
        format!("{}Oro: {} | EXP: {}{}", server_opcodes::CONSOLE_MSG, u.gold, u.exp, font_types::INFO),
    ];

    // Need to clone to avoid borrow issue
    let lines_clone = lines;
    for line in &lines_clone {
        state.send_to(conn_id, line).await;
    }
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
        let msg = format!("{}El personaje no existe.{}", server_opcodes::CONSOLE_MSG, font_types::INFO);
        state.send_to(conn_id, &msg).await;
        return;
    }

    // Load recipient's mail count
    let base = state.base_path.clone();
    let chr_path = base.join("charfile").join(format!("{}.chr", recipient_name.to_uppercase()));
    let chr = chr_path.to_str().unwrap_or("");
    let num_mails: usize = crate::config::get_var(chr, "CORREO", "NUMCORREOS").parse().unwrap_or(0);

    if num_mails >= MAX_MAILS {
        let msg = format!("{}629", server_opcodes::CONSOLE_MSG_ID);
        state.send_to(conn_id, &msg).await;
        return;
    }

    // Add mail to recipient's charfile
    let new_slot = num_mails + 1;
    // Current date (VB6: Format(Now, "dd/mm/yyyy hh:nn"))
    let now = chrono_like_date();
    let mail_content = format!("{}${}${}${}$", sender_name, subject, message, now);

    crate::config::write_var(chr, "CORREO", "NUMCORREOS", &new_slot.to_string()).ok();
    crate::config::write_var(chr, "CORREO", &format!("CORREONUM{}", new_slot), &mail_content).ok();
    crate::config::write_var(chr, "CORREO", &format!("NUECORREOS{}", new_slot), "1").ok(); // Mark as new

    // Notify recipient if online
    if let Some(&target_conn) = state.online_names.get(&recipient_name.to_uppercase()) {
        let msg = format!("{}631", server_opcodes::CONSOLE_MSG_ID);
        state.send_to(target_conn, &msg).await;
    }

    let msg = format!("{}Correo enviado a {}.{}", server_opcodes::CONSOLE_MSG, recipient_name, font_types::INFO);
    state.send_to(conn_id, &msg).await;
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
        // Request mail list
        send_mail_list(state, conn_id, &char_name).await;
        return;
    }

    // Read specific mail
    let base = state.base_path.clone();
    let chr_path = base.join("charfile").join(format!("{}.chr", char_name));
    let chr = chr_path.to_str().unwrap_or("");

    let content = crate::config::get_var(chr, "CORREO", &format!("CORREONUM{}", slot));
    if content.is_empty() { return; }

    // Mark as read
    crate::config::write_var(chr, "CORREO", &format!("NUECORREOS{}", slot), "0").ok();

    // Send content: ILO<remitente>$<asunto>$<mensaje>$<fecha>$
    let pkt = format!("{}{}", server_opcodes::MAIL_CONTENT, content);
    state.send_to(conn_id, &pkt).await;
}

/// CZB — Delete mail.
pub(super) async fn handle_mail_delete(state: &mut GameState, conn_id: ConnectionId, data: &str) {
    let payload = strip_opcode(data, 3);
    let slot: usize = payload.trim().parse().unwrap_or(0);

    let char_name = match state.users.get(&conn_id) {
        Some(u) if u.logged => u.char_name.clone(),
        _ => return,
    };

    let base = state.base_path.clone();
    let chr_path = base.join("charfile").join(format!("{}.chr", char_name));
    let chr = chr_path.to_str().unwrap_or("");

    let num_mails: usize = crate::config::get_var(chr, "CORREO", "NUMCORREOS").parse().unwrap_or(0);
    if slot == 0 || slot > num_mails { return; }

    // Shift mails down to compact
    for i in slot..num_mails {
        let next = crate::config::get_var(chr, "CORREO", &format!("CORREONUM{}", i + 1));
        let next_new = crate::config::get_var(chr, "CORREO", &format!("NUECORREOS{}", i + 1));
        crate::config::write_var(chr, "CORREO", &format!("CORREONUM{}", i), &next).ok();
        crate::config::write_var(chr, "CORREO", &format!("NUECORREOS{}", i), &next_new).ok();
    }

    // Clear last slot and decrement count
    crate::config::write_var(chr, "CORREO", &format!("CORREONUM{}", num_mails), "").ok();
    crate::config::write_var(chr, "CORREO", &format!("NUECORREOS{}", num_mails), "0").ok();
    crate::config::write_var(chr, "CORREO", "NUMCORREOS", &(num_mails - 1).to_string()).ok();

    // Refresh mail list
    send_mail_list(state, conn_id, &char_name).await;
}

/// CZR — Extract items from mail (simplified — no item attachment in basic impl).
pub(super) async fn handle_mail_extract(state: &mut GameState, conn_id: ConnectionId, _data: &str) {
    let msg = format!("{}Este correo no tiene objetos adjuntos.{}", server_opcodes::CONSOLE_MSG, font_types::INFO);
    state.send_to(conn_id, &msg).await;
}

/// Send mail list to player.
pub(super) async fn send_mail_list(state: &mut GameState, conn_id: ConnectionId, char_name: &str) {
    let base = state.base_path.clone();
    let chr_path = base.join("charfile").join(format!("{}.chr", char_name));
    let chr = chr_path.to_str().unwrap_or("");

    let num_mails: usize = crate::config::get_var(chr, "CORREO", "NUMCORREOS").parse().unwrap_or(0);

    let mut entries = Vec::new();
    for i in 1..=MAX_MAILS {
        if i <= num_mails {
            let content = crate::config::get_var(chr, "CORREO", &format!("CORREONUM{}", i));
            let is_new = crate::config::get_var(chr, "CORREO", &format!("NUECORREOS{}", i)) == "1";
            // Extract sender name (first field before $)
            let sender = content.split('$').next().unwrap_or("???");
            let new_tag = if is_new { " (NUEVO)" } else { "" };
            entries.push(format!("{}{}", sender, new_tag));
        } else {
            entries.push(String::new());
        }
    }

    let pkt = format!("{}{}", server_opcodes::MAIL_LIST, entries.join(","));
    state.send_to(conn_id, &pkt).await;
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

    let base = state.base_path.clone();

    // Collect all other logged users' conn_ids and account names
    let others: Vec<(ConnectionId, String)> = state.users.iter()
        .filter(|&(&cid, ref u)| cid != conn_id && u.logged)
        .map(|(&cid, u)| (cid, u.account_name.clone()))
        .collect();

    for (other_conn, account) in others {
        if is_friend_of_account(&base, &account, &char_name) {
            // Send KFM notification
            let kfm = format!("KFM{}", char_name);
            state.send_to(other_conn, &kfm).await;
            // Send updated friend list so their ON/OFF status refreshes
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

    let base = state.base_path.clone();

    let others: Vec<(ConnectionId, String)> = state.users.iter()
        .filter(|&(&cid, ref u)| cid != conn_id && u.logged)
        .map(|(&cid, u)| (cid, u.account_name.clone()))
        .collect();

    for (other_conn, account) in others {
        if is_friend_of_account(&base, &account, &char_name) {
            let dfm = format!("DFM{}", char_name);
            state.send_to(other_conn, &dfm).await;
            send_friend_list(state, other_conn).await;
        }
    }
}

/// Check if `name` is in the friend list of the account file for `account_name`.
fn is_friend_of_account(base: &std::path::Path, account_name: &str, name: &str) -> bool {
    let act_path = base.join("Accounts").join(format!("{}.act", account_name));
    let act = act_path.to_str().unwrap_or("");
    let count: usize = crate::config::get_var(act, "AMIGOS", "CANT").parse().unwrap_or(0);
    for i in 1..=count {
        let friend = crate::config::get_var(act, "AMIGOS", &format!("A{}", i));
        if friend.to_uppercase() == name.to_uppercase() {
            return true;
        }
    }
    false
}

/// ADDCON<name> — Add friend.
pub(super) async fn handle_friend_add(state: &mut GameState, conn_id: ConnectionId, data: &str) {
    let friend_name = strip_opcode(data, 6).trim().to_string();

    let account_name = match state.users.get(&conn_id) {
        Some(u) if u.logged => u.account_name.clone(),
        _ => return,
    };

    // VB6: Can't add self
    let self_name = state.users.get(&conn_id).map(|u| u.char_name.clone()).unwrap_or_default();
    if friend_name.to_uppercase() == self_name.to_uppercase() {
        state.send_to(conn_id, &format!("{}No puedes agregarte a ti mismo.", server_opcodes::ERROR_SHOW)).await;
        return;
    }

    if !charfile::character_exists(&state.pool, &friend_name).await {
        state.send_to(conn_id, &format!("{}El personaje no existe.", server_opcodes::ERROR_SHOW)).await;
        return;
    }

    // VB6: Can't add GMs
    // Check if the target character has privileges by loading their charfile
    if let Ok(char_data) = charfile::load_charfile(&state.pool, &friend_name).await {
        if char_data.privileges > 0 {
            state.send_to(conn_id, &format!("{}No podes agregar GM's.", server_opcodes::ERROR_SHOW)).await;
            return;
        }
    }

    // Load friend list from account file
    let base = state.base_path.clone();
    let accounts_dir = base.join("Accounts");
    // Ensure Accounts directory exists
    let _ = std::fs::create_dir_all(&accounts_dir);
    let act_path = accounts_dir.join(format!("{}.act", account_name));
    let act = act_path.to_str().unwrap_or("");

    let count: usize = crate::config::get_var(act, "AMIGOS", "CANT").parse().unwrap_or(0);
    if count >= 20 {
        state.send_to(conn_id, &format!("{}Lista de amigos llena, solo puedes agregar 20.", server_opcodes::ERROR_SHOW)).await;
        return;
    }

    // Check not duplicate
    for i in 1..=count {
        let existing = crate::config::get_var(act, "AMIGOS", &format!("A{}", i));
        if existing.to_uppercase() == friend_name.to_uppercase() {
            state.send_to(conn_id, &format!("{}El usuario ya esta en tu lista de amigos.", server_opcodes::ERROR_SHOW)).await;
            return;
        }
    }

    // Add friend
    let new_count = count + 1;
    crate::config::write_var(act, "AMIGOS", "CANT", &new_count.to_string()).ok();
    crate::config::write_var(act, "AMIGOS", &format!("A{}", new_count), &friend_name).ok();

    // Send updated list
    send_friend_list(state, conn_id).await;

    let msg = format!("{}{} agregado a tu lista de amigos.{}", server_opcodes::CONSOLE_MSG, friend_name, font_types::INFO);
    state.send_to(conn_id, &msg).await;
}

/// BORRAC<index> — Remove friend by slot.
pub(super) async fn handle_friend_remove(state: &mut GameState, conn_id: ConnectionId, data: &str) {
    let payload = strip_opcode(data, 6);
    let slot: usize = payload.trim().parse().unwrap_or(0);

    let account_name = match state.users.get(&conn_id) {
        Some(u) if u.logged => u.account_name.clone(),
        _ => return,
    };

    let base = state.base_path.clone();
    let accounts_dir = base.join("Accounts");
    let _ = std::fs::create_dir_all(&accounts_dir);
    let act_path = accounts_dir.join(format!("{}.act", account_name));
    let act = act_path.to_str().unwrap_or("");

    let count: usize = crate::config::get_var(act, "AMIGOS", "CANT").parse().unwrap_or(0);
    if slot == 0 || slot > count { return; }

    // Shift friends down
    for i in slot..count {
        let next = crate::config::get_var(act, "AMIGOS", &format!("A{}", i + 1));
        crate::config::write_var(act, "AMIGOS", &format!("A{}", i), &next).ok();
    }
    crate::config::write_var(act, "AMIGOS", &format!("A{}", count), "").ok();
    crate::config::write_var(act, "AMIGOS", "CANT", &(count - 1).to_string()).ok();

    send_friend_list(state, conn_id).await;
}

/// Send friend list to player (VB6 SendFriendList in modGuilds.bas).
/// Format: LDM<count>,name1(ON),name2(OFF),...
pub(super) async fn send_friend_list(state: &mut GameState, conn_id: ConnectionId) {
    let account_name = match state.users.get(&conn_id) {
        Some(u) if u.logged => u.account_name.clone(),
        _ => return,
    };

    let base = state.base_path.clone();
    let accounts_dir = base.join("Accounts");
    let _ = std::fs::create_dir_all(&accounts_dir);
    let act_path = accounts_dir.join(format!("{}.act", account_name));
    let act = act_path.to_str().unwrap_or("");

    let count: usize = crate::config::get_var(act, "AMIGOS", "CANT").parse().unwrap_or(0);

    // Build VB6 format: "count,name1(ON),name2(OFF),"
    let mut result = format!("{},", count);
    for i in 1..=count {
        let friend_name = crate::config::get_var(act, "AMIGOS", &format!("A{}", i));
        if friend_name.is_empty() || friend_name.to_uppercase() == "(NADIE)" {
            result.push_str("(NADIE)(OFF),");
        } else {
            // Check if friend is online
            let is_online = state.users.values()
                .any(|u| u.logged && u.char_name.to_uppercase() == friend_name.to_uppercase());
            if is_online {
                result.push_str(&format!("{}(ON),", friend_name));
            } else {
                result.push_str(&format!("{}(OFF),", friend_name));
            }
        }
    }

    let pkt = format!("LDM{}", result);
    state.send_to(conn_id, &pkt).await;
}

// =====================================================================
