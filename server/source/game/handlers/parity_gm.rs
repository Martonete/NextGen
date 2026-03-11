//! VB6 13.3 parity: Missing GM commands, player commands, and stubbed features.

use tracing::info;
use crate::net::ConnectionId;
use crate::game::types::{GameState, SendTarget, privilege_level};
use crate::protocol::{font_index, binary_packets};
use super::common::*;
use super::{
    warp_user, send_full_inventory,
};

// =====================================================================
// 7. Missing GM Commands
// =====================================================================

/// /TRABAJANDO — List players currently working. Requires SEMIDIOS+.
pub(super) async fn handle_slash_trabajando(state: &mut GameState, conn_id: ConnectionId) {
    match state.users.get(&conn_id) {
        Some(u) if u.logged && u.privileges >= privilege_level::SEMIDIOS => {}
        _ => return,
    }

    // VB6: checks Counters.Trabajando > 0 — we check interval_trabajar > 0 as proxy
    let mut names = Vec::new();
    for u in state.users.values() {
        if u.logged && u.interval_trabajar > 0 {
            names.push(u.char_name.clone());
        }
    }

    if names.is_empty() {
        state.send_console(conn_id, "Nadie esta trabajando.", font_index::INFO);
    } else {
        state.send_console(conn_id, &format!("Trabajando ({}): {}", names.len(), names.join(", ")), font_index::INFO);
    }
}

/// /OCULTANDO — List hidden players. Requires SEMIDIOS+.
pub(super) async fn handle_slash_ocultando(state: &mut GameState, conn_id: ConnectionId) {
    match state.users.get(&conn_id) {
        Some(u) if u.logged && u.privileges >= privilege_level::SEMIDIOS => {}
        _ => return,
    }

    let mut names = Vec::new();
    for u in state.users.values() {
        if u.logged && u.hidden {
            names.push(u.char_name.clone());
        }
    }

    if names.is_empty() {
        state.send_console(conn_id, "Nadie esta ocultandose.", font_index::INFO);
    } else {
        state.send_console(conn_id, &format!("Ocultos ({}): {}", names.len(), names.join(", ")), font_index::INFO);
    }
}

/// /SEGUIR <name> — GM follows a player (teleport to player). Requires SEMIDIOS+.
pub(super) async fn handle_slash_seguir(state: &mut GameState, conn_id: ConnectionId, target: &str) {
    match state.users.get(&conn_id) {
        Some(u) if u.logged && u.privileges >= privilege_level::SEMIDIOS => {}
        _ => return,
    }

    let target_conn = match state.find_user_by_name(target) {
        Some(c) => c,
        None => {
            state.send_console(conn_id, "Jugador no encontrado.", font_index::INFO);
            return;
        }
    };

    let (map, x, y) = match state.users.get(&target_conn) {
        Some(u) if u.logged => (u.pos_map, u.pos_x, u.pos_y),
        _ => return,
    };

    warp_user(state, conn_id, map, x, y).await;
    let target_name = state.users.get(&target_conn).map(|u| u.char_name.clone()).unwrap_or_default();
    state.send_console(conn_id, &format!("Siguiendo a {}.", target_name), font_index::INFO);
}

/// /REALMSG <text> — Send message to all Royal Army members. Requires SEMIDIOS+.
pub(super) async fn handle_slash_realmsg(state: &mut GameState, conn_id: ConnectionId, text: &str) {
    let (_priv_level, name) = match state.users.get(&conn_id) {
        Some(u) if u.logged && u.privileges >= privilege_level::SEMIDIOS => (u.privileges, u.char_name.clone()),
        _ => return,
    };

    let msg = format!("[Armada Real] {}> {}", name, text);
    let targets: Vec<ConnectionId> = state.users.values()
        .filter(|u| u.logged && u.armada_real)
        .map(|u| u.conn_id)
        .collect();
    for t in targets {
        state.send_console(t, &msg, font_index::SERVER);
    }
}

/// /CAOSMSG <text> — Send message to all Chaos Legion members. Requires SEMIDIOS+.
pub(super) async fn handle_slash_caosmsg(state: &mut GameState, conn_id: ConnectionId, text: &str) {
    let (_priv_level, name) = match state.users.get(&conn_id) {
        Some(u) if u.logged && u.privileges >= privilege_level::SEMIDIOS => (u.privileges, u.char_name.clone()),
        _ => return,
    };

    let msg = format!("[Legion del Caos] {}> {}", name, text);
    let targets: Vec<ConnectionId> = state.users.values()
        .filter(|u| u.logged && u.fuerzas_caos)
        .map(|u| u.conn_id)
        .collect();
    for t in targets {
        state.send_console(t, &msg, font_index::SERVER);
    }
}

/// /CIUMSG <text> — Send message to all citizens. Requires SEMIDIOS+.
pub(super) async fn handle_slash_ciumsg(state: &mut GameState, conn_id: ConnectionId, text: &str) {
    let (_priv_level, name) = match state.users.get(&conn_id) {
        Some(u) if u.logged && u.privileges >= privilege_level::SEMIDIOS => (u.privileges, u.char_name.clone()),
        _ => return,
    };

    let msg = format!("[Ciudadanos] {}> {}", name, text);
    let targets: Vec<ConnectionId> = state.users.values()
        .filter(|u| u.logged && !u.criminal)
        .map(|u| u.conn_id)
        .collect();
    for t in targets {
        state.send_console(t, &msg, font_index::SERVER);
    }
}

/// /CRIMSG <text> — Send message to all criminals. Requires SEMIDIOS+.
pub(super) async fn handle_slash_crimsg(state: &mut GameState, conn_id: ConnectionId, text: &str) {
    let (_priv_level, name) = match state.users.get(&conn_id) {
        Some(u) if u.logged && u.privileges >= privilege_level::SEMIDIOS => (u.privileges, u.char_name.clone()),
        _ => return,
    };

    let msg = format!("[Criminales] {}> {}", name, text);
    let targets: Vec<ConnectionId> = state.users.values()
        .filter(|u| u.logged && u.criminal)
        .map(|u| u.conn_id)
        .collect();
    for t in targets {
        state.send_console(t, &msg, font_index::SERVER);
    }
}

/// /ACEPTCONSE <name> — Accept player into Royal council. Requires DIOS+.
pub(super) async fn handle_slash_aceptconse(state: &mut GameState, conn_id: ConnectionId, target: &str) {
    match state.users.get(&conn_id) {
        Some(u) if u.logged && u.privileges >= privilege_level::DIOS => {}
        _ => return,
    }

    let target_conn = match state.find_user_by_name(target) {
        Some(c) => c,
        None => {
            state.send_console(conn_id, "Jugador no encontrado.", font_index::INFO);
            return;
        }
    };

    // Must be in Royal Army
    let is_armada = state.users.get(&target_conn).map(|u| u.armada_real).unwrap_or(false);
    if !is_armada {
        state.send_console(conn_id, "El jugador no pertenece a la Armada Real.", font_index::INFO);
        return;
    }

    if let Some(u) = state.users.get_mut(&target_conn) {
        u.royal_council = true;
    }

    let target_name = state.users.get(&target_conn).map(|u| u.char_name.clone()).unwrap_or_default();
    state.send_console(conn_id, &format!("{} ahora es consejero real.", target_name), font_index::INFO);
    state.send_console(target_conn, "Has sido aceptado en el Consejo Real.", font_index::INFO);
}

/// /ACEPTCONSECAOS <name> — Accept player into Chaos council. Requires DIOS+.
pub(super) async fn handle_slash_aceptconsecaos(state: &mut GameState, conn_id: ConnectionId, target: &str) {
    match state.users.get(&conn_id) {
        Some(u) if u.logged && u.privileges >= privilege_level::DIOS => {}
        _ => return,
    }

    let target_conn = match state.find_user_by_name(target) {
        Some(c) => c,
        None => {
            state.send_console(conn_id, "Jugador no encontrado.", font_index::INFO);
            return;
        }
    };

    let is_caos = state.users.get(&target_conn).map(|u| u.fuerzas_caos).unwrap_or(false);
    if !is_caos {
        state.send_console(conn_id, "El jugador no pertenece a las Fuerzas del Caos.", font_index::INFO);
        return;
    }

    if let Some(u) = state.users.get_mut(&target_conn) {
        u.chaos_council = true;
    }

    let target_name = state.users.get(&target_conn).map(|u| u.char_name.clone()).unwrap_or_default();
    state.send_console(conn_id, &format!("{} ahora es consejero del caos.", target_name), font_index::INFO);
    state.send_console(target_conn, "Has sido aceptado en el Consejo del Caos.", font_index::INFO);
}

/// /KICKCONSE <name> — Kick player from council. Requires DIOS+.
pub(super) async fn handle_slash_kickconse(state: &mut GameState, conn_id: ConnectionId, target: &str) {
    match state.users.get(&conn_id) {
        Some(u) if u.logged && u.privileges >= privilege_level::DIOS => {}
        _ => return,
    }

    let target_conn = match state.find_user_by_name(target) {
        Some(c) => c,
        None => {
            state.send_console(conn_id, "Jugador no encontrado.", font_index::INFO);
            return;
        }
    };

    let (was_royal, was_chaos) = match state.users.get(&target_conn) {
        Some(u) => (u.royal_council, u.chaos_council),
        None => return,
    };

    if !was_royal && !was_chaos {
        state.send_console(conn_id, "El jugador no es consejero.", font_index::INFO);
        return;
    }

    if let Some(u) = state.users.get_mut(&target_conn) {
        u.royal_council = false;
        u.chaos_council = false;
    }

    let target_name = state.users.get(&target_conn).map(|u| u.char_name.clone()).unwrap_or_default();
    state.send_console(conn_id, &format!("{} ha sido removido del consejo.", target_name), font_index::INFO);
    state.send_console(target_conn, "Has sido removido del consejo.", font_index::INFO);
}

/// /ESTUPIDO <name> — Mute player (prevent speech). Requires SEMIDIOS+.
pub(super) async fn handle_slash_estupido(state: &mut GameState, conn_id: ConnectionId, target: &str) {
    match state.users.get(&conn_id) {
        Some(u) if u.logged && u.privileges >= privilege_level::SEMIDIOS => {}
        _ => return,
    }

    let target_conn = match state.find_user_by_name(target) {
        Some(c) => c,
        None => {
            state.send_console(conn_id, "Jugador no encontrado.", font_index::INFO);
            return;
        }
    };

    if let Some(u) = state.users.get_mut(&target_conn) {
        u.silenced = true;
    }

    let target_name = state.users.get(&target_conn).map(|u| u.char_name.clone()).unwrap_or_default();
    let admin_name = state.users.get(&conn_id).map(|u| u.char_name.clone()).unwrap_or_default();
    state.send_console(conn_id, &format!("{} ha sido silenciado.", target_name), font_index::INFO);
    state.send_console(target_conn, "Has sido silenciado por un GM.", font_index::INFO);
    info!("[GM] {} silenced {}", admin_name, target_name);
}

/// /NOESTUPIDO <name> — Unmute player. Requires SEMIDIOS+.
pub(super) async fn handle_slash_noestupido(state: &mut GameState, conn_id: ConnectionId, target: &str) {
    match state.users.get(&conn_id) {
        Some(u) if u.logged && u.privileges >= privilege_level::SEMIDIOS => {}
        _ => return,
    }

    let target_conn = match state.find_user_by_name(target) {
        Some(c) => c,
        None => {
            state.send_console(conn_id, "Jugador no encontrado.", font_index::INFO);
            return;
        }
    };

    if let Some(u) = state.users.get_mut(&target_conn) {
        u.silenced = false;
    }

    let target_name = state.users.get(&target_conn).map(|u| u.char_name.clone()).unwrap_or_default();
    state.send_console(conn_id, &format!("{} ya no esta silenciado.", target_name), font_index::INFO);
    state.send_console(target_conn, "Tu silencio ha sido removido.", font_index::INFO);
}

/// /TRIGGER <value> — Set map trigger at current position. Requires DIOS+.
pub(super) async fn handle_slash_trigger(state: &mut GameState, conn_id: ConnectionId, args: &str) {
    let priv_level = state.users.get(&conn_id).map(|u| u.privileges).unwrap_or(0);
    if priv_level < privilege_level::DIOS { return; }

    let trigger_val: i32 = args.trim().parse().unwrap_or(-1);
    if trigger_val < 0 || trigger_val > 255 {
        state.send_console(conn_id, "Valor de trigger invalido (0-255).", font_index::INFO);
        return;
    }

    let (map, x, y) = match state.users.get(&conn_id) {
        Some(u) => (u.pos_map, u.pos_x, u.pos_y),
        None => return,
    };

    // Set trigger on map tile
    let map_idx = map as usize;
    if let Some(Some(game_map)) = state.game_data.maps.get_mut(map_idx) {
        if x >= 1 && x <= 100 && y >= 1 && y <= 100 {
            let trigger = match trigger_val {
                0 => crate::data::maps::Trigger::None,
                1 => crate::data::maps::Trigger::Indoor,
                2 => crate::data::maps::Trigger::Reserved,
                3 => crate::data::maps::Trigger::InvalidPos,
                4 => crate::data::maps::Trigger::SafeZone,
                5 => crate::data::maps::Trigger::AntiBlock,
                6 => crate::data::maps::Trigger::CombatZone,
                7 => crate::data::maps::Trigger::NoElevation,
                _ => crate::data::maps::Trigger::None,
            };
            game_map.tiles[(y - 1) as usize][(x - 1) as usize].trigger = trigger;
        }
    }

    state.send_console(conn_id, &format!("Trigger {} establecido en ({},{},{}).", trigger_val, map, x, y), font_index::INFO);
}

/// /SETDIALOG <npc_idx> <text> — Set NPC dialog. Requires DIOS+.
pub(super) async fn handle_slash_setdialog(state: &mut GameState, conn_id: ConnectionId, args: &str) {
    let priv_level = state.users.get(&conn_id).map(|u| u.privileges).unwrap_or(0);
    if priv_level < privilege_level::DIOS { return; }

    let parts: Vec<&str> = args.splitn(2, ' ').collect();
    if parts.len() < 2 {
        state.send_console(conn_id, "Uso: /SETDIALOG <npc_index> <texto>", font_index::INFO);
        return;
    }

    let npc_idx: usize = parts[0].parse().unwrap_or(0);
    let dialog = parts[1].trim();

    if let Some(Some(npc)) = state.npcs.get_mut(npc_idx) {
        npc.desc = dialog.to_string();
        state.send_console(conn_id, &format!("Dialogo del NPC {} actualizado.", npc_idx), font_index::INFO);
    } else {
        state.send_console(conn_id, "NPC no encontrado.", font_index::INFO);
    }
}

/// /APAGAR <seconds> — Shutdown server with countdown. Requires ADMINISTRADOR+.
pub(super) async fn handle_slash_apagar(state: &mut GameState, conn_id: ConnectionId, args: &str) {
    match state.users.get(&conn_id) {
        Some(u) if u.logged && u.privileges >= privilege_level::ADMINISTRADOR => {}
        _ => return,
    }

    let seconds: i32 = args.trim().parse().unwrap_or(60);
    if seconds <= 0 {
        state.send_console(conn_id, "Uso: /APAGAR <segundos>", font_index::INFO);
        return;
    }

    state.shutdown_countdown = seconds;
    state.shutdown_restart = false;

    let admin_name = state.users.get(&conn_id).map(|u| u.char_name.clone()).unwrap_or_default();
    let msg = format!("ATENCION: El servidor se apagara en {} segundos.", seconds);
    state.send_console_to(SendTarget::ToAll, &msg, font_index::WARNING);
    info!("[GM] {} initiated shutdown in {}s", admin_name, seconds);
}

/// /REINICIAR <seconds> — Restart server with countdown. Requires ADMINISTRADOR+.
pub(super) async fn handle_slash_reiniciar(state: &mut GameState, conn_id: ConnectionId, args: &str) {
    match state.users.get(&conn_id) {
        Some(u) if u.logged && u.privileges >= privilege_level::ADMINISTRADOR => {}
        _ => return,
    }

    let seconds: i32 = args.trim().parse().unwrap_or(60);
    if seconds <= 0 {
        state.send_console(conn_id, "Uso: /REINICIAR <segundos>", font_index::INFO);
        return;
    }

    state.shutdown_countdown = seconds;
    state.shutdown_restart = true;

    let admin_name = state.users.get(&conn_id).map(|u| u.char_name.clone()).unwrap_or_default();
    let msg = format!("ATENCION: El servidor se reiniciara en {} segundos.", seconds);
    state.send_console_to(SendTarget::ToAll, &msg, font_index::WARNING);
    info!("[GM] {} initiated restart in {}s", admin_name, seconds);
}

/// /GRABAR — Force save all player data. Requires ADMINISTRADOR+.
pub(super) async fn handle_slash_grabar(state: &mut GameState, conn_id: ConnectionId) {
    match state.users.get(&conn_id) {
        Some(u) if u.logged && u.privileges >= privilege_level::ADMINISTRADOR => {}
        _ => return,
    }

    // Trigger auto-save by resetting the counter to 0
    state.auto_save_counter = 0;

    let admin_name = state.users.get(&conn_id).map(|u| u.char_name.clone()).unwrap_or_default();
    state.send_console(conn_id, "Guardado forzado iniciado.", font_index::INFO);
    info!("[GM] {} forced save all users", admin_name);
}

/// /APASS <name>@<newpass> — Admin change user password. Requires ADMINISTRADOR+.
pub(super) async fn handle_slash_apass(state: &mut GameState, conn_id: ConnectionId, args: &str) {
    match state.users.get(&conn_id) {
        Some(u) if u.logged && u.privileges >= privilege_level::ADMINISTRADOR => {}
        _ => return,
    }

    let parts: Vec<&str> = args.splitn(2, '@').collect();
    if parts.len() < 2 {
        state.send_console(conn_id, "Uso: /APASS <nombre>@<nuevapassword>", font_index::INFO);
        return;
    }

    let target_name = parts[0].trim();
    let new_pass = parts[1].trim();

    if new_pass.len() < 3 {
        state.send_console(conn_id, "La password debe tener al menos 3 caracteres.", font_index::INFO);
        return;
    }

    // Find target's account name (if online)
    let account_name = match state.find_user_by_name(target_name) {
        Some(c) => state.users.get(&c).map(|u| u.account_name.clone()).unwrap_or_default(),
        None => {
            state.send_console(conn_id, "Jugador no encontrado online. Solo funciona con jugadores conectados.", font_index::INFO);
            return;
        }
    };

    if account_name.is_empty() {
        state.send_console(conn_id, "Cuenta no encontrada.", font_index::INFO);
        return;
    }

    // Hash and update
    let new_hash = match crate::db::password::hash_password(new_pass) {
        Ok(h) => h,
        Err(_) => {
            state.send_console(conn_id, "Error al generar hash.", font_index::INFO);
            return;
        }
    };

    if crate::db::accounts::update_password(&state.pool, &account_name, &new_hash).await.is_ok() {
        let admin_name = state.users.get(&conn_id).map(|u| u.char_name.clone()).unwrap_or_default();
        state.send_console(conn_id, &format!("Password de {} cambiada.", target_name), font_index::INFO);
        info!("[GM] {} changed password for {}", admin_name, target_name);
    } else {
        state.send_console(conn_id, "Error al cambiar la password.", font_index::INFO);
    }
}

/// /BANCLAN <guild> — Ban guild. Requires ADMINISTRADOR+.
pub(super) async fn handle_slash_banclan(state: &mut GameState, conn_id: ConnectionId, guild_name: &str) {
    match state.users.get(&conn_id) {
        Some(u) if u.logged && u.privileges >= privilege_level::ADMINISTRADOR => {}
        _ => return,
    }

    // Find the guild
    let guild_idx = match crate::db::guilds::find_guild_by_name(&state.pool, guild_name).await {
        Some(idx) => idx,
        None => {
            state.send_console(conn_id, "Clan no encontrado.", font_index::INFO);
            return;
        }
    };

    // Kick all online members of this guild
    let member_conns: Vec<ConnectionId> = state.users.values()
        .filter(|u| u.logged && u.guild_index == guild_idx)
        .map(|u| u.conn_id)
        .collect();

    for mc in &member_conns {
        if let Some(u) = state.users.get_mut(mc) {
            u.guild_index = 0;
            u.guild_name.clear();
        }
        state.send_console(*mc, "Tu clan ha sido baneado por un administrador.", font_index::FIGHT);
    }

    let admin_name = state.users.get(&conn_id).map(|u| u.char_name.clone()).unwrap_or_default();
    state.send_console(conn_id, &format!("Clan '{}' baneado. {} miembros online afectados.", guild_name, member_conns.len()), font_index::INFO);
    info!("[GM] {} banned guild {} ({} members affected)", admin_name, guild_name, member_conns.len());
}

/// /MIEMBROSCLAN <guild> — List guild members. Requires SEMIDIOS+.
pub(super) async fn handle_slash_miembrosclan(state: &mut GameState, conn_id: ConnectionId, guild_name: &str) {
    match state.users.get(&conn_id) {
        Some(u) if u.logged && u.privileges >= privilege_level::SEMIDIOS => {}
        _ => return,
    }

    let guild_idx = match crate::db::guilds::find_guild_by_name(&state.pool, guild_name).await {
        Some(idx) => idx,
        None => {
            state.send_console(conn_id, "Clan no encontrado.", font_index::INFO);
            return;
        }
    };

    // Show online members
    let members: Vec<String> = state.users.values()
        .filter(|u| u.logged && u.guild_index == guild_idx)
        .map(|u| u.char_name.clone())
        .collect();

    if members.is_empty() {
        state.send_console(conn_id, &format!("No hay miembros de '{}' online.", guild_name), font_index::INFO);
    } else {
        state.send_console(conn_id, &format!("Miembros de '{}' online ({}): {}", guild_name, members.len(), members.join(", ")), font_index::INFO);
    }
}

/// /RAJARCLAN <name> — Admin kick player from their guild. Requires ADMINISTRADOR+.
pub(super) async fn handle_slash_rajarclan(state: &mut GameState, conn_id: ConnectionId, target: &str) {
    match state.users.get(&conn_id) {
        Some(u) if u.logged && u.privileges >= privilege_level::ADMINISTRADOR => {}
        _ => return,
    }

    let target_conn = match state.find_user_by_name(target) {
        Some(c) => c,
        None => {
            state.send_console(conn_id, "Jugador no encontrado.", font_index::INFO);
            return;
        }
    };

    let guild_name = state.users.get(&target_conn).map(|u| u.guild_name.clone()).unwrap_or_default();
    if guild_name.is_empty() {
        state.send_console(conn_id, "El jugador no pertenece a ningun clan.", font_index::INFO);
        return;
    }

    if let Some(u) = state.users.get_mut(&target_conn) {
        u.guild_index = 0;
        u.guild_name.clear();
    }

    let target_name = state.users.get(&target_conn).map(|u| u.char_name.clone()).unwrap_or_default();
    state.send_console(conn_id, &format!("{} ha sido removido del clan {}.", target_name, guild_name), font_index::INFO);
    state.send_console(target_conn, "Has sido removido de tu clan por un administrador.", font_index::FIGHT);
}

// =====================================================================
// 8. Missing Player Commands
// =====================================================================

/// /GM <text> — Send SOS help request to online GMs.
pub(super) async fn handle_slash_gm_request(state: &mut GameState, conn_id: ConnectionId, text: &str) {
    let name = match state.users.get(&conn_id) {
        Some(u) if u.logged => u.char_name.clone(),
        _ => return,
    };

    let request_pending = state.users.get(&conn_id).map(|u| u.gm_request_pending).unwrap_or(false);
    if request_pending {
        state.send_console(conn_id, "Ya tienes una solicitud pendiente.", font_index::INFO);
        return;
    }

    if let Some(u) = state.users.get_mut(&conn_id) {
        u.gm_request_pending = true;
    }

    // Add to SOS list
    use crate::game::types::SosMessage;
    state.sos_messages.push(SosMessage {
        tipo: "GM".to_string(),
        autor: name.clone(),
        contenido: text.to_string(),
    });

    // Notify all GMs
    let msg = format!("[SOS/GM] {} solicita ayuda: {}", name, text);
    let gm_conns: Vec<ConnectionId> = state.users.values()
        .filter(|u| u.logged && u.privileges > privilege_level::USER)
        .map(|u| u.conn_id)
        .collect();
    for gc in gm_conns {
        state.send_console(gc, &msg, font_index::SERVER);
    }

    state.send_console(conn_id, "Tu solicitud ha sido enviada a los GMs online.", font_index::INFO);
}

/// /ROL <text> — Send roleplay request to GMs.
pub(super) async fn handle_slash_rol(state: &mut GameState, conn_id: ConnectionId, text: &str) {
    let name = match state.users.get(&conn_id) {
        Some(u) if u.logged => u.char_name.clone(),
        _ => return,
    };

    use crate::game::types::SosMessage;
    state.sos_messages.push(SosMessage {
        tipo: "ROL".to_string(),
        autor: name.clone(),
        contenido: text.to_string(),
    });

    let msg = format!("[ROL] {} solicita evento de rol: {}", name, text);
    let gm_conns: Vec<ConnectionId> = state.users.values()
        .filter(|u| u.logged && u.privileges > privilege_level::USER)
        .map(|u| u.conn_id)
        .collect();
    for gc in gm_conns {
        state.send_console(gc, &msg, font_index::SERVER);
    }

    state.send_console(conn_id, "Tu solicitud de rol ha sido enviada.", font_index::INFO);
}

/// /ELECCIONES — Open guild elections. Requires guild leader.
pub(super) async fn handle_slash_elecciones(state: &mut GameState, conn_id: ConnectionId) {
    let (guild_idx, char_name) = match state.users.get(&conn_id) {
        Some(u) if u.logged && u.guild_index > 0 => (u.guild_index, u.char_name.clone()),
        _ => {
            state.send_console(conn_id, "No perteneces a un clan.", font_index::INFO);
            return;
        }
    };

    // Check if leader — load guild and compare founder name
    let guild_info = crate::db::guilds::load_guild(&state.pool, guild_idx).await;
    let is_leader = guild_info
        .as_ref()
        .map(|g| g.leader.to_uppercase() == char_name.to_uppercase())
        .unwrap_or(false);
    if !is_leader {
        state.send_console(conn_id, "Solo el lider puede abrir elecciones.", font_index::INFO);
        return;
    }

    // Notify all guild members
    let guild_name = state.users.get(&conn_id).map(|u| u.guild_name.clone()).unwrap_or_default();
    let msg = format!("Se han abierto elecciones en el clan {}. Usa /VOTO <nombre> para votar.", guild_name);
    let member_conns: Vec<ConnectionId> = state.users.values()
        .filter(|u| u.logged && u.guild_index == guild_idx)
        .map(|u| u.conn_id)
        .collect();
    for mc in member_conns {
        state.send_console(mc, &msg, font_index::GUILD);
    }
}

// =====================================================================
// 9. Stubbed Features — Improved Implementations
// =====================================================================

/// Improved /CENTINELA handler — basic number verification.
/// VB6: Centinela module sends a random number, player must type it back.
pub(super) async fn handle_centinela_improved(state: &mut GameState, conn_id: ConnectionId, code: &str) {
    let expected = state.users.get(&conn_id).map(|u| u.centinela_number).unwrap_or(0);

    if expected == 0 {
        // No active centinela check — just acknowledge
        state.send_console(conn_id, "Centinela verificado.", font_index::INFO);
        return;
    }

    let typed: i32 = code.trim().parse().unwrap_or(-1);

    if typed == expected {
        // Correct answer — clear centinela
        if let Some(u) = state.users.get_mut(&conn_id) {
            u.centinela_number = 0;
            u.centinela_timer = 0;
            u.centinela_fails = 0;
        }
        state.send_console(conn_id, "Centinela verificado correctamente.", font_index::CENTINELA);
    } else {
        // Wrong answer
        if let Some(u) = state.users.get_mut(&conn_id) {
            u.centinela_fails += 1;
        }
        let fails = state.users.get(&conn_id).map(|u| u.centinela_fails).unwrap_or(0);
        if fails >= 3 {
            // Too many fails — kick
            let name = state.users.get(&conn_id).map(|u| u.char_name.clone()).unwrap_or_default();
            info!("[CENTINELA] {} kicked for 3 failed centinela attempts", name);
            state.send_console(conn_id, "Has fallado la verificacion centinela. Desconectado.", font_index::FIGHT);
            close_connection(state, conn_id).await;
        } else {
            state.send_console(conn_id, &format!("Respuesta incorrecta. Intentos restantes: {}.", 3 - fails), font_index::WARNING);
        }
    }
}

/// AlertarFaccionarios — Alert all online faction members.
/// VB6: Plays a sound and sends a message to all faction members.
pub(super) async fn alertar_faccionarios(state: &mut GameState, conn_id: ConnectionId) {
    let (armada, caos, name, map, x, y) = match state.users.get(&conn_id) {
        Some(u) if u.logged => (u.armada_real, u.fuerzas_caos, u.char_name.clone(), u.pos_map, u.pos_x, u.pos_y),
        _ => return,
    };

    if !armada && !caos {
        state.send_console(conn_id, "No perteneces a ninguna faccion.", font_index::INFO);
        return;
    }

    let faction_name = if armada { "Armada Real" } else { "Legion del Caos" };
    let msg = format!("[{}] {} ha hecho sonar el cuerno de alerta! (Mapa {})", faction_name, name, map);

    let targets: Vec<ConnectionId> = state.users.values()
        .filter(|u| u.logged && ((armada && u.armada_real) || (caos && u.fuerzas_caos)))
        .map(|u| u.conn_id)
        .collect();

    for t in &targets {
        state.send_console(*t, &msg, font_index::FIGHT);
        // Play horn sound (VB6 sound 45 = horn)
        state.send_bytes(*t, &binary_packets::write_play_wave(45, 50, 50));
    }
}

/// /PANELGM — Send GM panel data to client. Requires SEMIDIOS+.
/// TODO: Client-side GM panel packet not fully implemented — sends console summary.
pub(super) async fn handle_slash_panelgm(state: &mut GameState, conn_id: ConnectionId) {
    match state.users.get(&conn_id) {
        Some(u) if u.logged && u.privileges >= privilege_level::SEMIDIOS => {}
        _ => return,
    }

    let online = state.num_users;
    let record = state.record_users;
    let exp_mult = state.multiplicador_exp;
    let gold_mult = state.multiplicador_oro;
    let drop_mult = state.multiplicador_drop;
    let raining = state.raining;
    let night = state.forced_night;

    state.send_console(conn_id, &format!(
        "=== Panel GM === Online: {} | Record: {} | EXP: {}x | Gold: {}x | Drop: {}x | Lluvia: {} | Noche: {}",
        online, record, exp_mult, gold_mult, drop_mult,
        if raining { "Si" } else { "No" },
        if night { "Si" } else { "No" }
    ), font_index::SERVER);

    // Show pending SOS messages
    if !state.sos_messages.is_empty() {
        state.send_console(conn_id, &format!("Solicitudes pendientes: {}", state.sos_messages.len()), font_index::SERVER);
        // Clone to avoid borrow conflict
        let sos_list: Vec<_> = state.sos_messages.iter().cloned().collect();
        for (i, sos) in sos_list.iter().enumerate() {
            state.send_console(conn_id, &format!("  {}: [{}] {} - {}", i + 1, sos.tipo, sos.autor, sos.contenido), font_index::INFO);
        }
    }
}

/// /ALERTA — Sound faction alert horn.
pub(super) async fn handle_slash_alerta(state: &mut GameState, conn_id: ConnectionId) {
    alertar_faccionarios(state, conn_id).await;
}
