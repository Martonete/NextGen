//! Social system handlers: factions, utility slash commands.
//! Extracted from mod.rs to reduce file size.

use crate::net::ConnectionId;
use crate::game::types::{GameState, SendTarget};
use crate::protocol::font_index;
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
