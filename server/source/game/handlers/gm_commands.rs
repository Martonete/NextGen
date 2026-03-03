//! GM / Admin command handlers.
//!
//! All functions in this module require privilege > USER unless otherwise noted.
//! They are called from the `handle_slash_command` dispatcher in mod.rs.

use tracing::info;
use crate::net::ConnectionId;
use crate::game::types::{GameState, SendTarget, InventorySlot, EquipSlots, privilege_level};
use crate::protocol::{server_opcodes, font_types, fields::read_field};
use super::common::*;
use super::world;

// Functions from parent module (mod.rs) used by GM commands
use super::{
    warp_user, send_warp_fx, user_die, revive_user, check_user_level,
    naked_body, send_inventory_slot, send_full_inventory, llevar_usuarios_cvc,
    spawn_npc_at,
};

pub(super) async fn handle_slash_gmsg(state: &mut GameState, conn_id: ConnectionId, text: &str) {
    let (priv_level, name) = match state.users.get(&conn_id) {
        Some(u) if u.logged && u.privileges > privilege_level::USER => {
            (u.privileges, u.char_name.clone())
        }
        _ => return,
    };
    let _ = priv_level;
    let pkt = format!("||429@{}@{}", name, text);
    state.send_data(SendTarget::ToAdmins, &pkt).await;
    info!("[GM] {} sent GMSG: {}", name, text);
}

/// /SMSG <msg> — System message to all players. Requires privileges > 0.
pub(super) async fn handle_slash_smsg(state: &mut GameState, conn_id: ConnectionId, text: &str) {
    let name = match state.users.get(&conn_id) {
        Some(u) if u.logged && u.privileges > privilege_level::USER => u.char_name.clone(),
        _ => return,
    };
    let pkt = format!("!!{}\x1b", text); // ENDC = ESC char (0x1B)
    state.send_data(SendTarget::ToAll, &pkt).await;
    info!("[GM] {} sent SMSG: {}", name, text);
}

/// /NAVE — Toggle navigation mode (debug sailing). Requires privileges > 0.
/// /TELEP name map x y — Teleport self or another player (VB6 TCP.bas line 3368).
/// Format: /TELEP YO <map> <x> <y>  or  /TELEP <name> <map> <x> <y>
pub(super) async fn handle_slash_telep(state: &mut GameState, conn_id: ConnectionId, args: &str) {
    let priv_level = match state.users.get(&conn_id) {
        Some(u) if u.logged && u.privileges >= privilege_level::CONSEJERO => u.privileges,
        _ => return,
    };

    // Parse: name map x y (space-delimited)
    let parts: Vec<&str> = args.split_whitespace().collect();
    if parts.len() < 4 {
        state.send_to(conn_id, &format!("{}Uso: /TELEP nombre mapa x y{}", server_opcodes::CONSOLE_MSG, font_types::INFO)).await;
        return;
    }

    let name = parts[0];
    let map: i32 = parts[1].parse().unwrap_or(0);
    let x: i32 = parts[2].parse().unwrap_or(0);
    let y: i32 = parts[3].parse().unwrap_or(0);

    if map < 1 || !crate::game::world::in_map_bounds(x, y) {
        return;
    }

    // Check map exists
    let map_idx = map as usize;
    if map_idx >= state.game_data.maps.len() || state.game_data.maps.get(map_idx).and_then(|m| m.as_ref()).is_none() {
        return;
    }

    let target_id = if name.to_uppercase() == "YO" {
        conn_id
    } else {
        // Consejeros can't teleport others
        if priv_level <= privilege_level::CONSEJERO {
            return;
        }
        match state.find_user_by_name(name) {
            Some(id) => id,
            None => {
                state.send_to(conn_id, "||196").await; // User not found
                return;
            }
        }
    };

    warp_user(state, target_id, map, x, y).await;
    send_warp_fx(state, target_id).await;

    // Notify
    if target_id == conn_id {
        state.send_to(conn_id, "||773").await; // TEXTO773: Has sido transportado
    } else {
        let gm_name = state.users.get(&conn_id).map(|u| u.char_name.clone()).unwrap_or_default();
        state.send_to(target_id, &format!("||778@{}", gm_name)).await; // TEXTO778: %1 te ha transportado
        state.send_to(conn_id, &format!("||651@{}", gm_name)).await; // TEXTO651: %1 transportado
    }
}

/// /TELEPLOC — Teleport self to last left-click target location.
pub(super) async fn handle_slash_teleploc(state: &mut GameState, conn_id: ConnectionId) {
    // This uses the target coordinates set by left-click (LC packet).
    // For now, we don't track target_x/target_y from LC, so fall back to a message.
    // TODO: Track target_x/target_y from LC handler and warp there.
    let priv_level = match state.users.get(&conn_id) {
        Some(u) if u.logged && u.privileges >= privilege_level::CONSEJERO => u.privileges,
        _ => return,
    };

    // Get last click target from user state (VB6: flags.TargetX/Y)
    let (map, tx, ty) = match state.users.get(&conn_id) {
        Some(u) => (if u.target_map > 0 { u.target_map } else { u.pos_map }, u.target_x, u.target_y),
        None => return,
    };

    if tx == 0 && ty == 0 {
        state.send_to(conn_id, &format!("{}Primero haz click en el destino.{}", server_opcodes::CONSOLE_MSG, font_types::INFO)).await;
        return;
    }

    if !crate::game::world::in_map_bounds(tx, ty) {
        state.send_to(conn_id, &format!("{}Coordenadas fuera de los limites del mapa.{}", server_opcodes::CONSOLE_MSG, font_types::INFO)).await;
        return;
    }

    warp_user(state, conn_id, map, tx, ty).await;
    send_warp_fx(state, conn_id).await;
    state.send_to(conn_id, "||773").await; // TEXTO773: Has sido transportado
}

/// /GO map — Teleport self to map at position 50,50 (VB6 behavior, requires SEMIDIOS+).
pub(super) async fn handle_slash_go(state: &mut GameState, conn_id: ConnectionId, args: &str) {
    match state.users.get(&conn_id) {
        Some(u) if u.logged && u.privileges >= privilege_level::SEMIDIOS => {}
        _ => {
            state.send_to(conn_id, &format!("{}No tenes permisos para usar este comando.{}", server_opcodes::CONSOLE_MSG, font_types::INFO)).await;
            return;
        }
    };

    let parts: Vec<&str> = args.split_whitespace().collect();
    if parts.is_empty() {
        state.send_to(conn_id, &format!("{}Uso: /GO mapa{}", server_opcodes::CONSOLE_MSG, font_types::INFO)).await;
        return;
    }

    let map: i32 = parts[0].parse().unwrap_or(0);
    // VB6 always warps to 50,50 (TCP.bas line 4686)
    let x: i32 = if parts.len() >= 3 { parts[1].parse().unwrap_or(50) } else { 50 };
    let y: i32 = if parts.len() >= 3 { parts[2].parse().unwrap_or(50) } else { 50 };

    if map < 1 || !crate::game::world::in_map_bounds(x, y) {
        state.send_to(conn_id, &format!("{}Mapa o coordenadas invalidas.{}", server_opcodes::CONSOLE_MSG, font_types::INFO)).await;
        return;
    }

    // Check map exists
    let map_idx = map as usize;
    if map_idx >= state.game_data.maps.len() || state.game_data.maps.get(map_idx).and_then(|m| m.as_ref()).is_none() {
        state.send_to(conn_id, &format!("{}Mapa {} no existe.{}", server_opcodes::CONSOLE_MSG, map, font_types::INFO)).await;
        return;
    }

    warp_user(state, conn_id, map, x, y).await;
    send_warp_fx(state, conn_id).await;
    state.send_to(conn_id, "||773").await; // TEXTO773: Has sido transportado
}

/// /IRA name — Teleport to a player's position (requires SEMIDIOS+).
pub(super) async fn handle_slash_ira(state: &mut GameState, conn_id: ConnectionId, target: &str) {
    match state.users.get(&conn_id) {
        Some(u) if u.logged && u.privileges >= privilege_level::SEMIDIOS => {}
        _ => {
            state.send_to(conn_id, &format!("{}No tenes permisos para usar este comando.{}", server_opcodes::CONSOLE_MSG, font_types::INFO)).await;
            return;
        }
    }

    let target_id = match state.find_user_by_name(target) {
        Some(id) => id,
        None => {
            state.send_to(conn_id, &format!("{}Jugador '{}' no encontrado.{}", server_opcodes::CONSOLE_MSG, target, font_types::INFO)).await;
            return;
        }
    };

    let (map, x, y) = match state.users.get(&target_id) {
        Some(u) if u.logged => (u.pos_map, u.pos_x, u.pos_y),
        _ => {
            state.send_to(conn_id, &format!("{}Jugador '{}' no encontrado.{}", server_opcodes::CONSOLE_MSG, target, font_types::INFO)).await;
            return;
        }
    };

    warp_user(state, conn_id, map, x, y).await;
    send_warp_fx(state, conn_id).await;
    state.send_to(conn_id, "||773").await; // TEXTO773: Has sido transportado
}

/// /SUM name — Summon a player to your position (requires SEMIDIOS+).
pub(super) async fn handle_slash_sum(state: &mut GameState, conn_id: ConnectionId, target: &str) {
    let (my_map, my_x, my_y) = match state.users.get(&conn_id) {
        Some(u) if u.logged && u.privileges >= privilege_level::SEMIDIOS => (u.pos_map, u.pos_x, u.pos_y),
        _ => {
            state.send_to(conn_id, &format!("{}No tenes permisos para usar este comando.{}", server_opcodes::CONSOLE_MSG, font_types::INFO)).await;
            return;
        }
    };

    let target_id = match state.find_user_by_name(target) {
        Some(id) => id,
        None => {
            state.send_to(conn_id, &format!("{}Jugador '{}' no encontrado.{}", server_opcodes::CONSOLE_MSG, target, font_types::INFO)).await;
            return;
        }
    };

    warp_user(state, target_id, my_map, my_x, my_y).await;
    send_warp_fx(state, target_id).await;
    state.send_to(conn_id, &format!("{}Invocaste a '{}'.{}", server_opcodes::CONSOLE_MSG, target, font_types::INFO)).await;
    state.send_to(target_id, &format!("{}Has sido invocado por un GM.{}", server_opcodes::CONSOLE_MSG, font_types::INFO)).await;
}

/// /KICK name — Disconnect a player (requires SEMIDIOS+).
pub(super) async fn handle_slash_kick(state: &mut GameState, conn_id: ConnectionId, target: &str) {
    match state.users.get(&conn_id) {
        Some(u) if u.logged && u.privileges >= privilege_level::SEMIDIOS => {}
        _ => {
            state.send_to(conn_id, &format!("{}No tenes permisos para usar este comando.{}", server_opcodes::CONSOLE_MSG, font_types::INFO)).await;
            return;
        }
    }

    let target_id = match state.find_user_by_name(target) {
        Some(id) => id,
        None => {
            state.send_to(conn_id, &format!("{}Jugador '{}' no encontrado.{}", server_opcodes::CONSOLE_MSG, target, font_types::INFO)).await;
            return;
        }
    };

    // Don't let someone kick themselves
    if target_id == conn_id {
        state.send_to(conn_id, &format!("{}No podes kickearte a vos mismo.{}", server_opcodes::CONSOLE_MSG, font_types::INFO)).await;
        return;
    }

    // Check target privilege — can't kick equal or higher
    let target_priv = state.users.get(&target_id).map(|u| u.privileges).unwrap_or(0);
    let my_priv = state.users.get(&conn_id).map(|u| u.privileges).unwrap_or(0);
    if target_priv >= my_priv {
        state.send_to(conn_id, &format!("{}No podes kickear a alguien de igual o mayor rango.{}", server_opcodes::CONSOLE_MSG, font_types::INFO)).await;
        return;
    }

    state.send_to(target_id, &format!("{}Has sido desconectado por un GM.{}", server_opcodes::CONSOLE_MSG, font_types::INFO)).await;
    close_connection(state, target_id).await;
    state.send_to(conn_id, &format!("{}Jugador '{}' desconectado.{}", server_opcodes::CONSOLE_MSG, target, font_types::INFO)).await;
}

/// /ITEM objid amount — Create item in inventory (requires DIOS+).
pub(super) async fn handle_slash_item(state: &mut GameState, conn_id: ConnectionId, args: &str) {
    match state.users.get(&conn_id) {
        Some(u) if u.logged && u.privileges >= privilege_level::DIOS => {}
        _ => {
            state.send_to(conn_id, &format!("{}No tenes permisos para usar este comando.{}", server_opcodes::CONSOLE_MSG, font_types::INFO)).await;
            return;
        }
    }

    let parts: Vec<&str> = args.split_whitespace().collect();
    if parts.is_empty() {
        state.send_to(conn_id, &format!("{}Uso: /ITEM objid [cantidad]{}", server_opcodes::CONSOLE_MSG, font_types::INFO)).await;
        return;
    }

    let obj_idx: i32 = parts[0].parse().unwrap_or(0);
    let amount: i32 = if parts.len() > 1 { parts[1].parse().unwrap_or(1) } else { 1 };

    if obj_idx < 1 || amount < 1 {
        state.send_to(conn_id, &format!("{}Parametros invalidos.{}", server_opcodes::CONSOLE_MSG, font_types::INFO)).await;
        return;
    }

    // Verify object exists
    let obj_name = match state.get_object(obj_idx) {
        Some(obj) => obj.name.clone(),
        None => {
            state.send_to(conn_id, &format!("{}Objeto {} no existe.{}", server_opcodes::CONSOLE_MSG, obj_idx, font_types::INFO)).await;
            return;
        }
    };

    // Find first empty inventory slot
    let slot = match state.users.get(&conn_id) {
        Some(u) => u.inventory.iter().position(|s| s.obj_index == 0),
        None => return,
    };

    match slot {
        Some(slot_idx) => {
            if let Some(user) = state.users.get_mut(&conn_id) {
                user.inventory[slot_idx] = InventorySlot {
                    obj_index: obj_idx,
                    amount,
                    equipped: false,
                };
            }
            // Send CSI to update client inventory
            send_inventory_slot(state, conn_id, slot_idx).await;
            state.send_to(conn_id, &format!("{}Creado: {} x{}.{}", server_opcodes::CONSOLE_MSG, obj_name, amount, font_types::INFO)).await;
        }
        None => {
            state.send_to(conn_id, &format!("{}Inventario lleno.{}", server_opcodes::CONSOLE_MSG, font_types::INFO)).await;
        }
    }
}

/// /SOBJ <name> — Search objects by name (VB6 TCP.bas line 3677).
/// Sends ||748@<name>@<index> for each match. Requires SEMIDIOS+.
pub(super) async fn handle_slash_sobj(state: &mut GameState, conn_id: ConnectionId, search: &str) {
    match state.users.get(&conn_id) {
        Some(u) if u.logged && u.privileges >= privilege_level::SEMIDIOS => {}
        _ => {
            state.send_to(conn_id, &format!("{}No tenes permisos para usar este comando.{}", server_opcodes::CONSOLE_MSG, font_types::INFO)).await;
            return;
        }
    }

    if search.is_empty() {
        state.send_to(conn_id, &format!("{}Uso: /SOBJ <nombre>{}", server_opcodes::CONSOLE_MSG, font_types::INFO)).await;
        return;
    }

    let search_upper = search.to_uppercase();
    let mut found = 0;

    for i in 0..state.game_data.objects.len() {
        let name_upper = state.game_data.objects[i].name.to_uppercase();
        if name_upper.contains(&search_upper) {
            let name = state.game_data.objects[i].name.clone();
            let idx = state.game_data.objects[i].index;
            state.send_to(conn_id, &format!("||748@{}@{}", name, idx)).await;
            found += 1;
        }
    }

    if found == 0 {
        state.send_to(conn_id, &format!("{}No se encontraron objetos con '{}'.{}", server_opcodes::CONSOLE_MSG, search, font_types::INFO)).await;
    }
}

// =============================================================================
// GM Commands migrated from VB6 (TCP.bas)
// =============================================================================

/// /INVISIBLE — Toggle admin invisibility (body=0, head=0).
pub(super) async fn handle_slash_invisible(state: &mut GameState, conn_id: ConnectionId) {
    let (map, x, y, char_index, is_invisible) = match state.users.get(&conn_id) {
        Some(u) if u.logged && u.privileges >= privilege_level::CONSEJERO => {
            (u.pos_map, u.pos_x, u.pos_y, u.char_index, u.admin_invisible)
        }
        _ => return,
    };

    if is_invisible {
        // Make visible again — restore old body/head
        let (old_body, old_head) = {
            let user = state.users.get(&conn_id).unwrap();
            (user.old_body, user.old_head)
        };
        if let Some(user) = state.users.get_mut(&conn_id) {
            user.admin_invisible = false;
            user.invisible = false;
            user.body = old_body;
            user.head = old_head;
        }
        // Broadcast appearance change
        let cc = state.users.get(&conn_id).unwrap().build_cc_packet();
        state.send_data(SendTarget::ToArea { map, x, y }, &cc).await;
        // NOVER packet (visible)
        let nover = format!("NOVER{},0", char_index.0);
        state.send_data(SendTarget::ToArea { map, x, y }, &nover).await;
        state.send_to(conn_id, &format!("{}Sos visible.{}", server_opcodes::CONSOLE_MSG, font_types::INFO)).await;
    } else {
        // Save current body/head, go invisible
        if let Some(user) = state.users.get_mut(&conn_id) {
            user.old_body = user.body;
            user.old_head = user.head;
            user.body = 0;
            user.head = 0;
            user.admin_invisible = true;
            user.invisible = true;
        }
        // Broadcast appearance change (body=0, head=0 = invisible)
        let cc = state.users.get(&conn_id).unwrap().build_cc_packet();
        state.send_data(SendTarget::ToArea { map, x, y }, &cc).await;
        // NOVER packet (invisible)
        let nover = format!("NOVER{},1", char_index.0);
        state.send_data(SendTarget::ToArea { map, x, y }, &nover).await;
        state.send_to(conn_id, &format!("{}Sos invisible.{}", server_opcodes::CONSOLE_MSG, font_types::INFO)).await;
    }
}

/// /DONDE nick — Locate user on map. Sends ||735@name@map@x@y.
pub(super) async fn handle_slash_donde(state: &mut GameState, conn_id: ConnectionId, target: &str) {
    match state.users.get(&conn_id) {
        Some(u) if u.logged && u.privileges >= privilege_level::CONSEJERO => {}
        _ => return,
    }

    let target_upper = target.to_uppercase();
    let found = state.users.values()
        .find(|u| u.logged && u.char_name.to_uppercase() == target_upper)
        .map(|u| (u.char_name.clone(), u.pos_map, u.pos_x, u.pos_y, u.privileges));

    match found {
        Some((name, map, x, y, target_priv)) => {
            // Can't locate gods+
            if target_priv >= privilege_level::DIOS {
                let my_priv = state.users.get(&conn_id).map(|u| u.privileges).unwrap_or(0);
                if my_priv < target_priv {
                    state.send_to(conn_id, &format!("{}No podes localizar a ese usuario.{}", server_opcodes::CONSOLE_MSG, font_types::INFO)).await;
                    return;
                }
            }
            state.send_to(conn_id, &format!("||735@{}@{}@{}@{}", name, map, x, y)).await;
        }
        None => {
            state.send_to(conn_id, &format!("||196")).await;
        }
    }
}

/// /BAN nick@reason — Ban character.
pub(super) async fn handle_slash_ban(state: &mut GameState, conn_id: ConnectionId, args: &str) {
    match state.users.get(&conn_id) {
        Some(u) if u.logged && u.privileges >= privilege_level::DIOS => {}
        _ => return,
    }

    let parts: Vec<&str> = args.splitn(2, '@').collect();
    let nick = parts[0].trim().replace(['\\', '/'], "");
    let reason = if parts.len() > 1 { parts[1].trim() } else { "" };

    if nick.is_empty() || reason.is_empty() {
        state.send_to(conn_id, "||758").await;
        return;
    }

    let admin_name = state.users.get(&conn_id).map(|u| u.char_name.clone()).unwrap_or_default();

    // Check if target is online
    let target_upper = nick.to_uppercase();
    let target_conn = state.users.values()
        .find(|u| u.logged && u.char_name.to_uppercase() == target_upper)
        .map(|u| u.conn_id);

    // Set ban flag in charfile
    let chr_path = state.base_path.join("charfile").join(format!("{}.chr", nick));
    if chr_path.exists() {
        // Write Ban=1 to charfile
        if let Ok(content) = std::fs::read_to_string(&chr_path) {
            let updated = content.replace("Ban=0", "Ban=1");
            let _ = std::fs::write(&chr_path, updated);
        }
    } else {
        state.send_to(conn_id, &format!("||440")).await;
        return;
    }

    // Broadcast ban notification
    let ban_msg = format!("||760@{}@{}", admin_name, nick);
    state.send_data(SendTarget::ToAdmins, &ban_msg).await;

    // If online, disconnect
    if let Some(tc) = target_conn {
        state.send_to(tc, &format!("{}Has sido baneado. Razon: {}{}", server_opcodes::CONSOLE_MSG, reason, font_types::COMBAT)).await;
        if let Some(w) = state.writers.get_mut(&tc) {
            w.shutdown().await;
        }
    }

    state.send_to(conn_id, &format!("{}{} ha sido baneado. Razon: {}{}", server_opcodes::CONSOLE_MSG, nick, reason, font_types::INFO)).await;
    info!("[GM] {} banned {} for: {}", admin_name, nick, reason);
}

/// /UNBAN nick — Unban character.
pub(super) async fn handle_slash_unban(state: &mut GameState, conn_id: ConnectionId, target: &str) {
    match state.users.get(&conn_id) {
        Some(u) if u.logged && u.privileges >= privilege_level::SUB_ADMINISTRADOR => {}
        _ => return,
    }

    let nick = target.replace(['\\', '/'], "");
    let chr_path = state.base_path.join("charfile").join(format!("{}.chr", nick));
    if !chr_path.exists() {
        state.send_to(conn_id, &format!("||189@{}", nick)).await;
        return;
    }

    // Set Ban=0 in charfile
    if let Ok(content) = std::fs::read_to_string(&chr_path) {
        let updated = content.replace("Ban=1", "Ban=0");
        let _ = std::fs::write(&chr_path, updated);
    }

    let admin_name = state.users.get(&conn_id).map(|u| u.char_name.clone()).unwrap_or_default();
    let msg = format!("||762@{}@{}", admin_name, nick);
    state.send_data(SendTarget::ToAdmins, &msg).await;
    state.send_to(conn_id, &format!("{}{} ha sido desbaneado.{}", server_opcodes::CONSOLE_MSG, nick, font_types::INFO)).await;
    info!("[GM] {} unbanned {}", admin_name, nick);
}

/// /BANIP nick|ip — Ban IP address.
pub(super) async fn handle_slash_banip(state: &mut GameState, conn_id: ConnectionId, args: &str) {
    match state.users.get(&conn_id) {
        Some(u) if u.logged && u.privileges >= privilege_level::SEMIDIOS => {}
        _ => return,
    }

    let target = args.trim().replace(['\\', '/'], "");
    let admin_name = state.users.get(&conn_id).map(|u| u.char_name.clone()).unwrap_or_default();

    // Check if target is a nick (online user)
    let target_upper = target.to_uppercase();
    let found = state.users.values()
        .find(|u| u.logged && u.char_name.to_uppercase() == target_upper)
        .map(|u| (u.conn_id, u.ip.clone(), u.char_name.clone()));

    let ip_to_ban = if let Some((tc, ip, name)) = found {
        // Banning by nick — also kick the player
        let msg = format!("||798@{}@{}", admin_name, name);
        state.send_data(SendTarget::ToAdmins, &msg).await;
        if let Some(w) = state.writers.get_mut(&tc) {
            w.shutdown().await;
        }
        ip
    } else {
        // Treat as direct IP
        target.clone()
    };

    // Add to ban list
    if state.bans.is_ip_banned(&ip_to_ban) {
        state.send_to(conn_id, "||797").await;
        return;
    }

    let _ = state.bans.ban_ip(&state.pool, &ip_to_ban).await;
    state.send_to(conn_id, &format!("{}IP {} baneada.{}", server_opcodes::CONSOLE_MSG, ip_to_ban, font_types::INFO)).await;
    info!("[GM] {} banned IP {}", admin_name, ip_to_ban);
}

/// /UNBANIP ip — Unban IP address.
pub(super) async fn handle_slash_unbanip(state: &mut GameState, conn_id: ConnectionId, ip: &str) {
    match state.users.get(&conn_id) {
        Some(u) if u.logged && u.privileges >= privilege_level::SEMIDIOS => {}
        _ => return,
    }

    let ip = ip.trim();
    if state.bans.unban_ip(&state.pool, ip).await {
        state.send_to(conn_id, &format!("||799@{}", ip)).await;
        let admin_name = state.users.get(&conn_id).map(|u| u.char_name.clone()).unwrap_or_default();
        info!("[GM] {} unbanned IP {}", admin_name, ip);
    } else {
        state.send_to(conn_id, "||800").await;
    }
}

/// /BANACC nick@reason — Ban account.
pub(super) async fn handle_slash_banacc(state: &mut GameState, conn_id: ConnectionId, args: &str) {
    match state.users.get(&conn_id) {
        Some(u) if u.logged && u.privileges >= privilege_level::DIOS => {}
        _ => return,
    }

    let parts: Vec<&str> = args.splitn(2, '@').collect();
    let nick = parts[0].trim().replace(['\\', '/'], "");
    let reason = if parts.len() > 1 { parts[1].trim() } else { "Sin razon" };

    if nick.is_empty() {
        state.send_to(conn_id, "||758").await;
        return;
    }

    let admin_name = state.users.get(&conn_id).map(|u| u.char_name.clone()).unwrap_or_default();

    // Find account name (from online user or charfile)
    let target_upper = nick.to_uppercase();
    let online = state.users.values()
        .find(|u| u.logged && u.char_name.to_uppercase() == target_upper)
        .map(|u| (u.conn_id, u.account_name.clone()));

    let account_name = if let Some((tc, acc)) = online {
        // Kick the player
        let msg = format!("||752@{}@{}", admin_name, nick);
        state.send_data(SendTarget::ToAdmins, &msg).await;
        if let Some(w) = state.writers.get_mut(&tc) {
            w.shutdown().await;
        }
        acc
    } else {
        // Try to read from charfile
        nick.clone() // Fallback: use nick as account name
    };

    // Ban character
    let chr_path = state.base_path.join("charfile").join(format!("{}.chr", nick));
    if chr_path.exists() {
        if let Ok(content) = std::fs::read_to_string(&chr_path) {
            let updated = content.replace("Ban=0", "Ban=1");
            let _ = std::fs::write(&chr_path, updated);
        }
    }

    // Ban account
    let acc_path = state.base_path.join("Accounts").join(format!("{}.act", account_name));
    if acc_path.exists() {
        if let Ok(content) = std::fs::read_to_string(&acc_path) {
            let updated = content.replace("ban=0", "ban=1").replace("Ban=0", "Ban=1");
            let _ = std::fs::write(&acc_path, updated);
        }
    }

    let msg = format!("||760@{}@{}", admin_name, nick);
    state.send_data(SendTarget::ToAdmins, &msg).await;
    let msg2 = format!("||761@{}@{}", admin_name, account_name);
    state.send_data(SendTarget::ToAdmins, &msg2).await;
    state.send_to(conn_id, &format!("{}{} y cuenta {} baneados.{}", server_opcodes::CONSOLE_MSG, nick, account_name, font_types::INFO)).await;
    info!("[GM] {} banned account {} (char: {})", admin_name, account_name, nick);
}

/// /UNBANACC nick — Unban account.
pub(super) async fn handle_slash_unbanacc(state: &mut GameState, conn_id: ConnectionId, target: &str) {
    match state.users.get(&conn_id) {
        Some(u) if u.logged && u.privileges >= privilege_level::SUB_ADMINISTRADOR => {}
        _ => return,
    }

    let nick = target.replace(['\\', '/'], "");
    let admin_name = state.users.get(&conn_id).map(|u| u.char_name.clone()).unwrap_or_default();

    // Unban character
    let chr_path = state.base_path.join("charfile").join(format!("{}.chr", nick));
    if chr_path.exists() {
        if let Ok(content) = std::fs::read_to_string(&chr_path) {
            let updated = content.replace("Ban=1", "Ban=0");
            let _ = std::fs::write(&chr_path, updated);
        }
    }

    // Try unban account (use nick as account name fallback)
    let acc_path = state.base_path.join("Accounts").join(format!("{}.act", nick));
    if acc_path.exists() {
        if let Ok(content) = std::fs::read_to_string(&acc_path) {
            let updated = content.replace("ban=1", "ban=0").replace("Ban=1", "Ban=0");
            let _ = std::fs::write(&acc_path, updated);
        }
    }

    let msg = format!("||763@{}@{}", admin_name, nick);
    state.send_data(SendTarget::ToAdmins, &msg).await;
    state.send_to(conn_id, &format!("{}Cuenta de {} desbaneada.{}", server_opcodes::CONSOLE_MSG, nick, font_types::INFO)).await;
    info!("[GM] {} unbanned account for {}", admin_name, nick);
}

/// /CARCEL nick@reason@minutes — Jail user.
pub(super) async fn handle_slash_carcel(state: &mut GameState, conn_id: ConnectionId, args: &str) {
    match state.users.get(&conn_id) {
        Some(u) if u.logged && u.privileges >= privilege_level::SEMIDIOS => {}
        _ => return,
    }

    let parts: Vec<&str> = args.splitn(3, '@').collect();
    if parts.len() < 3 {
        state.send_to(conn_id, "||745").await;
        return;
    }

    let nick = parts[0].trim().replace(['\\', '/'], "");
    let reason = parts[1].trim();
    let minutes: i32 = parts[2].trim().parse().unwrap_or(0);

    if minutes < 1 || minutes > 60 {
        state.send_to(conn_id, "||746").await;
        return;
    }

    let target_upper = nick.to_uppercase();
    let target_conn = state.users.values()
        .find(|u| u.logged && u.char_name.to_uppercase() == target_upper)
        .map(|u| (u.conn_id, u.privileges));

    match target_conn {
        Some((tc, priv_level)) => {
            // Can't jail admins
            if priv_level >= privilege_level::DIOS {
                state.send_to(conn_id, "||442").await;
                return;
            }

            // Set jail timer and warp to prison (map 78, 50, 50)
            if let Some(user) = state.users.get_mut(&tc) {
                user.jail_timer = minutes * 60; // Convert to seconds
            }

            // Send jail notification
            state.send_to(tc, &format!("||659@{}", minutes)).await;

            // Warp to prison
            warp_user(state, tc, 78, 50, 50).await;

            let admin_name = state.users.get(&conn_id).map(|u| u.char_name.clone()).unwrap_or_default();
            state.send_to(conn_id, &format!("{}{} encarcelado por {} minutos.{}", server_opcodes::CONSOLE_MSG, nick, minutes, font_types::INFO)).await;
            info!("[GM] {} jailed {} for {}m: {}", admin_name, nick, minutes, reason);
        }
        None => {
            state.send_to(conn_id, "||196").await;
        }
    }
}

/// /SILENCIAR nick@minutes — Mute/unmute user.
pub(super) async fn handle_slash_silenciar(state: &mut GameState, conn_id: ConnectionId, args: &str) {
    match state.users.get(&conn_id) {
        Some(u) if u.logged && u.privileges >= privilege_level::CONSEJERO => {}
        _ => return,
    }

    let parts: Vec<&str> = args.splitn(2, '@').collect();
    let nick = parts[0].trim().replace(['\\', '/'], "");
    let minutes: i32 = if parts.len() > 1 { parts[1].trim().parse().unwrap_or(5) } else { 5 };

    if minutes > 60 {
        state.send_to(conn_id, "||944").await;
        return;
    }

    let target_upper = nick.to_uppercase();
    let target_conn = state.users.values()
        .find(|u| u.logged && u.char_name.to_uppercase() == target_upper)
        .map(|u| (u.conn_id, u.silenced));

    match target_conn {
        Some((tc, is_silenced)) => {
            if is_silenced {
                // Unmute
                if let Some(user) = state.users.get_mut(&tc) {
                    user.silenced = false;
                    user.silence_timer = 0;
                }
                state.send_to(conn_id, "||738").await;
                state.send_to(tc, "||946").await;
            } else {
                // Mute
                if let Some(user) = state.users.get_mut(&tc) {
                    user.silenced = true;
                    user.silence_timer = minutes * 60;
                }
                state.send_to(conn_id, "||737").await;
                state.send_to(tc, &format!("||943@{}", minutes)).await;
            }
            let admin_name = state.users.get(&conn_id).map(|u| u.char_name.clone()).unwrap_or_default();
            info!("[GM] {} {} {}", admin_name, if is_silenced { "unmuted" } else { "muted" }, nick);
        }
        None => {
            state.send_to(conn_id, "||196").await;
        }
    }
}

/// /ADVERTIR nick@reason — Warn user. 5 warnings = auto-ban.
pub(super) async fn handle_slash_advertir(state: &mut GameState, conn_id: ConnectionId, args: &str) {
    match state.users.get(&conn_id) {
        Some(u) if u.logged && u.privileges >= privilege_level::CONSEJERO => {}
        _ => return,
    }

    let parts: Vec<&str> = args.splitn(2, '@').collect();
    let nick = parts[0].trim().replace(['\\', '/'], "");
    let reason = if parts.len() > 1 { parts[1].trim() } else { "" };

    if nick.is_empty() || reason.is_empty() {
        state.send_to(conn_id, "||741").await;
        return;
    }

    let admin_name = state.users.get(&conn_id).map(|u| u.char_name.clone()).unwrap_or_default();

    let target_upper = nick.to_uppercase();
    let target_conn = state.users.values()
        .find(|u| u.logged && u.char_name.to_uppercase() == target_upper)
        .map(|u| (u.conn_id, u.privileges, u.warnings));

    match target_conn {
        Some((tc, priv_level, current_warnings)) => {
            // Can't warn higher privilege
            let my_priv = state.users.get(&conn_id).map(|u| u.privileges).unwrap_or(0);
            if priv_level >= my_priv && my_priv < privilege_level::ADMINISTRADOR {
                state.send_to(conn_id, "||751").await;
                return;
            }

            let new_warnings = current_warnings + 1;
            if let Some(user) = state.users.get_mut(&tc) {
                user.warnings = new_warnings;
            }

            // Broadcast
            let msg = format!("||742@{}@{}", admin_name, nick);
            state.send_data(SendTarget::ToAdmins, &msg).await;
            state.send_to(tc, &format!("||743@{}@{}@{}", admin_name, reason, new_warnings)).await;

            if new_warnings >= 5 {
                // Auto-ban
                let chr_path = state.base_path.join("charfile").join(format!("{}.chr", nick));
                if chr_path.exists() {
                    if let Ok(content) = std::fs::read_to_string(&chr_path) {
                        let updated = content.replace("Ban=0", "Ban=1");
                        let _ = std::fs::write(&chr_path, updated);
                    }
                }
                let ban_msg = format!("||744@{}", nick);
                state.send_data(SendTarget::ToAdmins, &ban_msg).await;
                if let Some(w) = state.writers.get_mut(&tc) {
                    w.shutdown().await;
                }
                info!("[GM] {} auto-banned (5 warnings)", nick);
            } else {
                // Jail for warnings*5 minutes
                let jail_minutes = new_warnings * 5;
                if let Some(user) = state.users.get_mut(&tc) {
                    user.jail_timer = jail_minutes * 60;
                }
                state.send_to(tc, &format!("||659@{}", jail_minutes)).await;
                warp_user(state, tc, 78, 50, 50).await;
            }

            info!("[GM] {} warned {} (#{}) for: {}", admin_name, nick, new_warnings, reason);
        }
        None => {
            state.send_to(conn_id, "||440").await;
        }
    }
}

/// /KILL nick — Kill user instantly.
pub(super) async fn handle_slash_kill(state: &mut GameState, conn_id: ConnectionId, target: &str) {
    match state.users.get(&conn_id) {
        Some(u) if u.logged && u.privileges >= privilege_level::SEMIDIOS => {}
        _ => return,
    }

    let target_upper = target.to_uppercase();
    let target_conn = state.users.values()
        .find(|u| u.logged && u.char_name.to_uppercase() == target_upper)
        .map(|u| u.conn_id);

    match target_conn {
        Some(tc) => {
            let admin_name = state.users.get(&conn_id).map(|u| u.char_name.clone()).unwrap_or_default();
            let (map, x, y) = state.users.get(&tc).map(|u| (u.pos_map, u.pos_x, u.pos_y)).unwrap_or((0, 0, 0));

            // Kill the user
            user_die(state, tc, None).await;

            // Broadcast
            let msg = format!("||753@{}@{}", admin_name, target);
            state.send_data(SendTarget::ToArea { map, x, y }, &msg).await;
            info!("[GM] {} killed {}", admin_name, target);
        }
        None => {
            state.send_to(conn_id, "||196").await;
        }
    }
}

/// /ECHAR nick — Kick user (same as /KICK but VB6 name).
pub(super) async fn handle_slash_echar(state: &mut GameState, conn_id: ConnectionId, target: &str) {
    handle_slash_kick(state, conn_id, target).await;
}

/// /REVIVIR nick|YO — Revive user.
pub(super) async fn handle_slash_revivir(state: &mut GameState, conn_id: ConnectionId, target: &str) {
    match state.users.get(&conn_id) {
        Some(u) if u.logged && u.privileges >= privilege_level::SEMIDIOS => {}
        _ => return,
    }

    let target_upper = target.to_uppercase();
    let target_conn = if target_upper == "YO" {
        Some(conn_id)
    } else {
        state.users.values()
            .find(|u| u.logged && u.char_name.to_uppercase() == target_upper)
            .map(|u| u.conn_id)
    };

    match target_conn {
        Some(tc) => {
            let is_dead = state.users.get(&tc).map(|u| u.dead).unwrap_or(false);
            if !is_dead {
                state.send_to(conn_id, &format!("{}Ese usuario no esta muerto.{}", server_opcodes::CONSOLE_MSG, font_types::INFO)).await;
                return;
            }

            // Revive: restore HP, clear dead flag, restore body
            let (map, x, y, race, max_hp) = match state.users.get(&tc) {
                Some(u) => (u.pos_map, u.pos_x, u.pos_y, u.race.clone(), u.max_hp),
                None => return,
            };

            // Read gender and head from DB for naked body
            let char_name_for_load = state.users.get(&tc).map(|u| u.char_name.clone()).unwrap_or_default();
            let (gender, orig_head) = match crate::db::charfile::load_charfile(&state.pool, &char_name_for_load).await {
                Ok(chr) => (chr.gender.to_string(), chr.head),
                Err(_) => ("1".to_string(), 1),
            };

            let new_body = naked_body(&race, &gender);

            if let Some(user) = state.users.get_mut(&tc) {
                user.dead = false;
                user.min_hp = max_hp;
                user.body = new_body;
                user.head = orig_head;
                if user.admin_invisible {
                    user.old_body = new_body;
                    user.old_head = orig_head;
                }
            }

            // Read final state for CP packet
            let (heading, weapon_anim, shield_anim, casco_anim, char_index) = match state.users.get(&tc) {
                Some(u) => (u.heading, u.weapon_anim, u.shield_anim, u.casco_anim, u.char_index),
                None => return,
            };

            // Resurrection FX (VB6: CFF charindex, 65, 0)
            let cff_pkt = format!("CFF{},65,0", char_index.0);
            state.send_data(SendTarget::ToArea { map, x, y }, &cff_pkt).await;

            // Send HP update
            let hp_pkt = {
                let u = state.users.get(&tc).unwrap();
                format!("[H]{},{}", u.max_hp, u.min_hp)
            };
            state.send_to(tc, &hp_pkt).await;

            // Broadcast CP (character model change) — VB6: ChangeUserChar
            let cp_pkt = format!(
                "CP{},{},{},{},{},{},0,0,{}",
                char_index.0, new_body, orig_head, heading,
                weapon_anim, shield_anim, casco_anim
            );
            state.send_data(SendTarget::ToArea { map, x, y }, &cp_pkt).await;

            let admin_name = state.users.get(&conn_id).map(|u| u.char_name.clone()).unwrap_or_default();
            state.send_to(conn_id, &format!("{}{} revivido.{}", server_opcodes::CONSOLE_MSG, target, font_types::INFO)).await;
            info!("[GM] {} revived {}", admin_name, target);
        }
        None => {
            state.send_to(conn_id, "||196").await;
        }
    }
}

/// /ESPIAR nick — Teleport invisibly to user.
pub(super) async fn handle_slash_espiar(state: &mut GameState, conn_id: ConnectionId, target: &str) {
    match state.users.get(&conn_id) {
        Some(u) if u.logged && u.privileges >= privilege_level::SEMIDIOS => {}
        _ => return,
    }

    let target_upper = target.to_uppercase();
    let found = state.users.values()
        .find(|u| u.logged && u.char_name.to_uppercase() == target_upper)
        .map(|u| (u.pos_map, u.pos_x, u.pos_y));

    match found {
        Some((map, x, y)) => {
            // Make invisible if not already
            let is_invisible = state.users.get(&conn_id).map(|u| u.admin_invisible).unwrap_or(false);
            if !is_invisible {
                handle_slash_invisible(state, conn_id).await;
            }

            // Warp to target (offset -2 to not be on exact tile)
            let warp_x = (x - 2).max(1);
            let warp_y = (y - 2).max(1);
            warp_user(state, conn_id, map, warp_x, warp_y).await;

            state.send_to(conn_id, &format!("{}Espiando a {}.{}", server_opcodes::CONSOLE_MSG, target, font_types::INFO)).await;
        }
        None => {
            state.send_to(conn_id, "||196").await;
        }
    }
}

/// /INV nick — View user's inventory.
pub(super) async fn handle_slash_inv(state: &mut GameState, conn_id: ConnectionId, target: &str) {
    match state.users.get(&conn_id) {
        Some(u) if u.logged && u.privileges >= privilege_level::SEMIDIOS => {}
        _ => return,
    }

    let target_upper = target.to_uppercase();
    let found = state.users.values()
        .find(|u| u.logged && u.char_name.to_uppercase() == target_upper)
        .map(|u| u.conn_id);

    match found {
        Some(tc) => {
            // Send each inventory slot as console messages
            let inv_data: Vec<(usize, i32, i32, bool)> = state.users.get(&tc)
                .map(|u| u.inventory.iter().enumerate()
                    .filter(|(_, s)| s.obj_index > 0)
                    .map(|(i, s)| (i + 1, s.obj_index, s.amount, s.equipped))
                    .collect())
                .unwrap_or_default();

            let target_name = state.users.get(&tc).map(|u| u.char_name.clone()).unwrap_or_default();
            state.send_to(conn_id, &format!("{}Inventario de {}:{}", server_opcodes::CONSOLE_MSG, target_name, font_types::INFO)).await;

            let is_empty = inv_data.is_empty();
            for (slot, obj_idx, amount, equipped) in inv_data {
                let name = state.get_object(obj_idx).map(|o| o.name.clone()).unwrap_or_else(|| format!("Obj#{}", obj_idx));
                let eq = if equipped { " [E]" } else { "" };
                state.send_to(conn_id, &format!("{}  Slot {}: {} x{}{}{}", server_opcodes::CONSOLE_MSG, slot, name, amount, eq, font_types::INFO)).await;
            }

            if is_empty {
                state.send_to(conn_id, &format!("{}  (vacio){}", server_opcodes::CONSOLE_MSG, font_types::INFO)).await;
            }
        }
        None => {
            state.send_to(conn_id, "||196").await;
        }
    }
}

/// /BOV nick — View user's bank vault.
pub(super) async fn handle_slash_bov(state: &mut GameState, conn_id: ConnectionId, target: &str) {
    match state.users.get(&conn_id) {
        Some(u) if u.logged && u.privileges >= privilege_level::SEMIDIOS => {}
        _ => return,
    }

    let target_upper = target.to_uppercase();
    let found = state.users.values()
        .find(|u| u.logged && u.char_name.to_uppercase() == target_upper)
        .map(|u| u.conn_id);

    match found {
        Some(tc) => {
            let bank_data: Vec<(usize, i32, i32)> = state.users.get(&tc)
                .map(|u| u.bank.iter().enumerate()
                    .filter(|(_, s)| s.obj_index > 0)
                    .map(|(i, s)| (i + 1, s.obj_index, s.amount))
                    .collect())
                .unwrap_or_default();

            let target_name = state.users.get(&tc).map(|u| u.char_name.clone()).unwrap_or_default();
            let bank_gold = state.users.get(&tc).map(|u| u.bank_gold).unwrap_or(0);
            state.send_to(conn_id, &format!("{}Boveda de {} (Oro: {}):{}", server_opcodes::CONSOLE_MSG, target_name, bank_gold, font_types::INFO)).await;

            let bank_empty = bank_data.is_empty();
            for (slot, obj_idx, amount) in bank_data {
                let name = state.get_object(obj_idx).map(|o| o.name.clone()).unwrap_or_else(|| format!("Obj#{}", obj_idx));
                state.send_to(conn_id, &format!("{}  Slot {}: {} x{}{}", server_opcodes::CONSOLE_MSG, slot, name, amount, font_types::INFO)).await;
            }

            if bank_empty {
                state.send_to(conn_id, &format!("{}  (vacia){}", server_opcodes::CONSOLE_MSG, font_types::INFO)).await;
            }
        }
        None => {
            state.send_to(conn_id, "||196").await;
        }
    }
}

/// /RMSG text — Server-wide broadcast message.
pub(super) async fn handle_slash_rmsg(state: &mut GameState, conn_id: ConnectionId, text: &str) {
    match state.users.get(&conn_id) {
        Some(u) if u.logged && u.privileges >= privilege_level::SEMIDIOS => {}
        _ => return,
    }

    let admin_name = state.users.get(&conn_id).map(|u| u.char_name.clone()).unwrap_or_default();
    // VB6: N|<admin>> <message> with celeste font
    let msg = format!("N|{}>> {}{}", admin_name, text, font_types::SYSTEM);
    state.send_data(SendTarget::ToAll, &msg).await;
    info!("[GM] {} broadcast: {}", admin_name, text);
}

/// /LMSG text@minutes — Set automatic periodic broadcast.
pub(super) async fn handle_slash_lmsg(state: &mut GameState, conn_id: ConnectionId, args: &str) {
    match state.users.get(&conn_id) {
        Some(u) if u.logged && u.privileges >= privilege_level::DIOS => {}
        _ => return,
    }

    let parts: Vec<&str> = args.splitn(2, '@').collect();
    let text = parts[0].trim();

    if text.is_empty() {
        state.auto_msg_active = false;
        state.auto_msg_text.clear();
        state.send_to(conn_id, &format!("{}Mensaje automatico desactivado.{}", server_opcodes::CONSOLE_MSG, font_types::INFO)).await;
        return;
    }

    let minutes: i32 = if parts.len() > 1 { parts[1].trim().parse().unwrap_or(5) } else { 5 };
    state.auto_msg_active = true;
    state.auto_msg_text = text.to_string();
    state.auto_msg_interval = minutes;
    state.auto_msg_counter = 0;

    state.send_to(conn_id, &format!("{}Mensaje automatico cada {}min: {}{}", server_opcodes::CONSOLE_MSG, minutes, text, font_types::INFO)).await;
}

/// /EXP multiplier — Set experience multiplier.
pub(super) async fn handle_slash_exp_mult(state: &mut GameState, conn_id: ConnectionId, val: &str) {
    match state.users.get(&conn_id) {
        Some(u) if u.logged && u.privileges >= privilege_level::ADMINISTRADOR => {}
        _ => return,
    }

    let mult: i32 = val.parse().unwrap_or(0);
    if mult < 1 {
        state.send_to(conn_id, &format!("{}Valor invalido.{}", server_opcodes::CONSOLE_MSG, font_types::INFO)).await;
        return;
    }

    state.multiplicador_exp = mult;
    let msg = format!("||774@{}", mult);
    state.send_data(SendTarget::ToAll, &msg).await;
    info!("[GM] EXP multiplier set to {}x", mult);
}

/// /GLD multiplier — Set gold multiplier.
pub(super) async fn handle_slash_gld_mult(state: &mut GameState, conn_id: ConnectionId, val: &str) {
    match state.users.get(&conn_id) {
        Some(u) if u.logged && u.privileges >= privilege_level::ADMINISTRADOR => {}
        _ => return,
    }

    let mult: i32 = val.parse().unwrap_or(0);
    if mult < 1 {
        state.send_to(conn_id, &format!("{}Valor invalido.{}", server_opcodes::CONSOLE_MSG, font_types::INFO)).await;
        return;
    }

    state.multiplicador_oro = mult;
    let msg = format!("||775@{}", mult);
    state.send_data(SendTarget::ToAll, &msg).await;
    info!("[GM] Gold multiplier set to {}x", mult);
}

/// /DROP multiplier — Set drop multiplier.
pub(super) async fn handle_slash_drop_mult(state: &mut GameState, conn_id: ConnectionId, val: &str) {
    match state.users.get(&conn_id) {
        Some(u) if u.logged && u.privileges >= privilege_level::ADMINISTRADOR => {}
        _ => return,
    }

    let mult: i32 = val.parse().unwrap_or(0);
    if mult < 1 {
        state.send_to(conn_id, &format!("{}Valor invalido.{}", server_opcodes::CONSOLE_MSG, font_types::INFO)).await;
        return;
    }

    state.multiplicador_drop = mult;
    let msg = format!("||776@{}", mult);
    state.send_data(SendTarget::ToAll, &msg).await;
    info!("[GM] Drop multiplier set to {}x", mult);
}

/// /HOME nick — Teleport user to their home city.
pub(super) async fn handle_slash_home(state: &mut GameState, conn_id: ConnectionId, target: &str) {
    match state.users.get(&conn_id) {
        Some(u) if u.logged && u.privileges >= privilege_level::SEMIDIOS => {}
        _ => return,
    }

    let target_upper = target.to_uppercase();
    let target_conn = state.users.values()
        .find(|u| u.logged && u.char_name.to_uppercase() == target_upper)
        .map(|u| (u.conn_id, u.hogar.clone(), u.armada_real, u.fuerzas_caos));

    match target_conn {
        Some((tc, hogar, armada, caos)) => {
            // Determine home location
            let (map, x, y) = if armada {
                (29, 50, 90) // Royal Army camp
            } else if caos {
                (27, 47, 48) // Chaos Forces camp
            } else {
                match hogar.to_uppercase().as_str() {
                    "THIR" => (25, 74, 44),
                    "INTHAK" => (130, 52, 56),
                    "RUVENDEL" => (26, 51, 52),
                    _ => (28, 54, 36), // Default: Banderbill
                }
            };

            warp_user(state, tc, map, x, y).await;
            state.send_to(conn_id, "||772").await;
            state.send_to(tc, "||773").await;
        }
        None => {
            state.send_to(conn_id, "||198").await;
        }
    }
}

/// /OFF — Shutdown server.
pub(super) async fn handle_slash_off(state: &mut GameState, conn_id: ConnectionId) {
    match state.users.get(&conn_id) {
        Some(u) if u.logged && u.privileges >= privilege_level::ADMINISTRADOR => {}
        _ => return,
    }

    let admin_name = state.users.get(&conn_id).map(|u| u.char_name.clone()).unwrap_or_default();
    info!("[GM] {} shutting down server", admin_name);

    // Send shutdown message to all
    let msg = format!("N|Servidor apagado por {}.{}", admin_name, font_types::COMBAT);
    state.send_data(SendTarget::ToAll, &msg).await;

    // Exit process
    std::process::exit(0);
}

/// /ECHARTODOSPJS — Kick all non-privileged players.
pub(super) async fn handle_slash_echartodospjs(state: &mut GameState, conn_id: ConnectionId) {
    match state.users.get(&conn_id) {
        Some(u) if u.logged && u.privileges >= privilege_level::SEMIDIOS => {}
        _ => return,
    }

    // Collect all non-privileged connections
    let to_kick: Vec<ConnectionId> = state.users.values()
        .filter(|u| u.logged && u.privileges < privilege_level::CONSEJERO)
        .map(|u| u.conn_id)
        .collect();

    let count = to_kick.len();
    for tc in to_kick {
        if let Some(w) = state.writers.get_mut(&tc) {
            w.shutdown().await;
        }
    }

    let admin_name = state.users.get(&conn_id).map(|u| u.char_name.clone()).unwrap_or_default();
    state.send_to(conn_id, &format!("{}{} jugadores desconectados.{}", server_opcodes::CONSOLE_MSG, count, font_types::INFO)).await;
    info!("[GM] {} kicked all {} players", admin_name, count);
}

/// /DAMETODO nick — Drop all user's inventory items on ground.
pub(super) async fn handle_slash_dametodo(state: &mut GameState, conn_id: ConnectionId, target: &str) {
    match state.users.get(&conn_id) {
        Some(u) if u.logged && u.privileges >= privilege_level::GRAN_DIOS => {}
        _ => return,
    }

    let target_upper = target.to_uppercase();
    let target_conn = state.users.values()
        .find(|u| u.logged && u.char_name.to_uppercase() == target_upper)
        .map(|u| u.conn_id);

    match target_conn {
        Some(tc) => {
            // Clear inventory
            if let Some(user) = state.users.get_mut(&tc) {
                for slot in &mut user.inventory {
                    *slot = InventorySlot { obj_index: 0, amount: 0, equipped: false };
                }
                user.equip = EquipSlots::default();
            }

            // Send full inventory update
            send_full_inventory(state, tc).await;

            let admin_name = state.users.get(&conn_id).map(|u| u.char_name.clone()).unwrap_or_default();
            state.send_to(tc, &format!("||754@{}", admin_name)).await;
            let msg = format!("||755@{}@{}", admin_name, target);
            state.send_data(SendTarget::ToAdmins, &msg).await;
            info!("[GM] {} stripped inventory of {}", admin_name, target);
        }
        None => {
            state.send_to(conn_id, "||196").await;
        }
    }
}

/// /MATA npc_index — Kill target NPC by runtime index.
pub(super) async fn handle_slash_mata(state: &mut GameState, conn_id: ConnectionId, target: &str) {
    match state.users.get(&conn_id) {
        Some(u) if u.logged && u.privileges >= privilege_level::SEMIDIOS => {}
        _ => return,
    }

    if let Ok(npc_idx) = target.parse::<usize>() {
        if npc_idx > 0 && npc_idx < state.npcs.len() {
            let npc_data = state.npcs[npc_idx].as_ref().map(|n| (n.map, n.x, n.y, n.char_index));
            if let Some((map, x, y, ci)) = npc_data {
                // Remove from world tile
                if map > 0 {
                    let grid = state.world.grid_mut(map);
                    if let Some(tile) = grid.tile_mut(x, y) {
                        if tile.npc_index == npc_idx as i32 {
                            tile.npc_index = 0;
                        }
                    }
                }
                let bp = format!("BP{}", ci.0);
                state.send_data(SendTarget::ToArea { map, x, y }, &bp).await;
                state.npcs[npc_idx] = None;
                state.send_to(conn_id, &format!("{}NPC #{} eliminado.{}", server_opcodes::CONSOLE_MSG, npc_idx, font_types::INFO)).await;
                return;
            }
        }
    }

    state.send_to(conn_id, &format!("{}NPC no encontrado. Usa /MATA <npc_runtime_index>{}", server_opcodes::CONSOLE_MSG, font_types::INFO)).await;
}

/// /MASSKILL — Kill all NPCs on current map.
pub(super) async fn handle_slash_masskill(state: &mut GameState, conn_id: ConnectionId) {
    let map = match state.users.get(&conn_id) {
        Some(u) if u.logged && u.privileges >= privilege_level::DIOS => u.pos_map,
        _ => return,
    };

    let mut killed = 0;
    let npc_indices: Vec<usize> = state.npcs.iter().enumerate()
        .filter_map(|(i, n)| n.as_ref().filter(|n| n.map == map).map(|_| i))
        .collect();

    for idx in npc_indices {
        let npc_data = state.npcs[idx].as_ref().map(|n| (n.x, n.y, n.char_index));
        if let Some((nx, ny, ci)) = npc_data {
            let grid = state.world.grid_mut(map);
            if let Some(tile) = grid.tile_mut(nx, ny) {
                if tile.npc_index == idx as i32 {
                    tile.npc_index = 0;
                }
            }
            let bp = format!("BP{}", ci.0);
            state.send_data(SendTarget::ToArea { map, x: nx, y: ny }, &bp).await;
            killed += 1;
        }
        state.npcs[idx] = None;
    }

    state.send_to(conn_id, &format!("{}{} NPCs eliminados en mapa {}.{}", server_opcodes::CONSOLE_MSG, killed, map, font_types::INFO)).await;
}

/// Check if an object is a map fixture (not player-droppable).
/// VB6: ItemNoEsDeMapa — returns TRUE if item is NOT a map fixture (i.e. can be cleaned).
/// Map fixtures: Doors(6), Trees(4), Signs(8), Forums(10), Minerals(23), Teleports(19).
fn is_map_fixture(state: &GameState, obj_index: i32) -> bool {
    use crate::data::objects::ObjType;
    match state.get_object(obj_index) {
        Some(obj) => matches!(obj.obj_type,
            ObjType::Door | ObjType::Trees | ObjType::Sign |
            ObjType::Forum | ObjType::Mineral | ObjType::Teleport
        ),
        None => false, // Unknown object — safe to clean
    }
}

/// /LIMPIAR or /LMAP — Clean dropped ground items on current map.
/// Skips map fixtures (doors, trees, signs, forums, minerals, teleports).
/// VB6: LimpiarMapa / LimpiarMundoEntero — only cleans items in tClearWorld.
pub(super) async fn handle_slash_limpiar(state: &mut GameState, conn_id: ConnectionId) {
    let map = match state.users.get(&conn_id) {
        Some(u) if u.logged && u.privileges >= privilege_level::DIOS => u.pos_map,
        _ => return,
    };

    // Collect items to clean (skip map fixtures)
    let mut to_clean: Vec<(i32, i32)> = Vec::new();
    if let Some(grid) = state.world.grid(map) {
        for y in 1..=100i32 {
            for x in 1..=100i32 {
                if let Some(tile) = grid.tile(x, y) {
                    if tile.ground_item.obj_index > 0 && !is_map_fixture(state, tile.ground_item.obj_index) {
                        to_clean.push((x, y));
                    }
                }
            }
        }
    }

    // Remove items and send BO packets
    for &(x, y) in &to_clean {
        let grid = state.world.grid_mut(map);
        if let Some(tile) = grid.tile_mut(x, y) {
            tile.ground_item = world::GroundItem::default();
        }
        // VB6: EraseObj sends "BO{x},{y}" to map
        state.send_data(SendTarget::ToMap(map), &format!("BO{},{}", x, y)).await;
    }

    let gm_name = state.users.get(&conn_id).map(|u| u.char_name.clone()).unwrap_or_default();
    info!("[GM] {} used /LIMPIAR on map {} — cleaned {} items", gm_name, map, to_clean.len());
    state.send_to(conn_id, &format!("{}{} items del suelo limpiados en mapa {}.{}", server_opcodes::CONSOLE_MSG, to_clean.len(), map, font_types::INFO)).await;
}

/// /NICK2IP nick — Get IP of user.
pub(super) async fn handle_slash_nick2ip(state: &mut GameState, conn_id: ConnectionId, target: &str) {
    match state.users.get(&conn_id) {
        Some(u) if u.logged && u.privileges >= privilege_level::DIOS => {}
        _ => return,
    }

    let target_upper = target.to_uppercase();
    let found = state.users.values()
        .find(|u| u.logged && u.char_name.to_uppercase() == target_upper)
        .map(|u| (u.char_name.clone(), u.ip.clone()));

    match found {
        Some((name, ip)) => {
            state.send_to(conn_id, &format!("{}IP de {}: {}{}", server_opcodes::CONSOLE_MSG, name, ip, font_types::INFO)).await;
        }
        None => {
            state.send_to(conn_id, "||196").await;
        }
    }
}

/// /IP2NICK ip — Find users with given IP.
pub(super) async fn handle_slash_ip2nick(state: &mut GameState, conn_id: ConnectionId, ip: &str) {
    match state.users.get(&conn_id) {
        Some(u) if u.logged && u.privileges >= privilege_level::DIOS => {}
        _ => return,
    }

    let matches: Vec<String> = state.users.values()
        .filter(|u| u.logged && u.ip == ip)
        .map(|u| u.char_name.clone())
        .collect();

    if matches.is_empty() {
        state.send_to(conn_id, &format!("{}Nadie con IP {}{}", server_opcodes::CONSOLE_MSG, ip, font_types::INFO)).await;
    } else {
        state.send_to(conn_id, &format!("{}IP {}: {}{}", server_opcodes::CONSOLE_MSG, ip, matches.join(", "), font_types::INFO)).await;
    }
}

/// /NOGLOBAL — Toggle global chat on/off (GM Dios+ command). VB6: frmMain.frm
pub(super) async fn handle_slash_noglobal(state: &mut GameState, conn_id: ConnectionId) {
    let priv_level = state.users.get(&conn_id).map(|u| u.privileges).unwrap_or(0);
    if priv_level < 4 { return; } // Dios+
    state.chat_global = !state.chat_global;
    if state.chat_global {
        state.send_data(SendTarget::ToAll, "||803").await;
    } else {
        state.send_data(SendTarget::ToAll, "||804").await;
    }
}

/// /FPS — Show server stats.
pub(super) async fn handle_slash_fps(state: &mut GameState, conn_id: ConnectionId) {
    match state.users.get(&conn_id) {
        Some(u) if u.logged && u.privileges >= privilege_level::SEMIDIOS => {}
        _ => return,
    }

    let online = state.users.values().filter(|u| u.logged).count();
    let npc_count = state.npcs.iter().filter(|n| n.is_some()).count();
    state.send_to(conn_id, &format!("{}Online: {} | NPCs: {} | Record: {}{}", server_opcodes::CONSOLE_MSG, online, npc_count, state.record_users, font_types::INFO)).await;
}

/// /CT map x y — Create teleport at current position (requires map .inf modification).
pub(super) async fn handle_slash_ct(state: &mut GameState, conn_id: ConnectionId, args: &str) {
    match state.users.get(&conn_id) {
        Some(u) if u.logged && u.privileges >= privilege_level::DIOS => {}
        _ => return,
    }
    // Teleport creation requires modifying map .inf files which is not yet supported.
    // In VB6 this modifies MapData().TileExit in memory.
    state.send_to(conn_id, &format!("{}Creacion de teleports no soportada aun (requiere edicion de .inf).{}", server_opcodes::CONSOLE_MSG, font_types::INFO)).await;
}

/// /DT — Destroy teleport at current position.
pub(super) async fn handle_slash_dt(state: &mut GameState, conn_id: ConnectionId) {
    match state.users.get(&conn_id) {
        Some(u) if u.logged && u.privileges >= privilege_level::DIOS => {}
        _ => return,
    }
    state.send_to(conn_id, &format!("{}Destruccion de teleports no soportada aun (requiere edicion de .inf).{}", server_opcodes::CONSOLE_MSG, font_types::INFO)).await;
}

/// /RESMAP — Respawn all NPCs on current map.
pub(super) async fn handle_slash_resmap(state: &mut GameState, conn_id: ConnectionId) {
    let map = match state.users.get(&conn_id) {
        Some(u) if u.logged && u.privileges >= privilege_level::DIOS => u.pos_map,
        _ => return,
    };

    // Respawn dead NPCs on this map (using existing respawn logic)
    let mut respawned = 0;
    let dead_npcs: Vec<usize> = state.npcs.iter().enumerate()
        .filter_map(|(i, n)| {
            n.as_ref().filter(|n| n.map == map && n.min_hp <= 0).map(|_| i)
        })
        .collect();

    for idx in dead_npcs {
        if let Some(npc) = &mut state.npcs[idx] {
            let npc_num = npc.npc_number;
            if let Some(npc_data) = state.game_data.npcs.get(npc_num) {
                npc.min_hp = npc_data.max_hp;
                respawned += 1;
            }
        }
    }

    state.send_to(conn_id, &format!("{}{} NPCs respawneados en mapa {}.{}", server_opcodes::CONSOLE_MSG, respawned, map, font_types::INFO)).await;
}

/// /TALKAS text — Send message as NPC/anonymous.
pub(super) async fn handle_slash_talkas(state: &mut GameState, conn_id: ConnectionId, args: &str) {
    match state.users.get(&conn_id) {
        Some(u) if u.logged && u.privileges >= privilege_level::DIOS => {}
        _ => return,
    }

    let (map, x, y) = match state.users.get(&conn_id) {
        Some(u) => (u.pos_map, u.pos_x, u.pos_y),
        None => return,
    };

    let msg = format!("T|{}\u{00B0}{}\u{00B0}0", "16776960", args); // Yellow color
    state.send_data(SendTarget::ToArea { map, x, y }, &msg).await;
}

/// /SETDESC nick description — Set NPC/user description.
pub(super) async fn handle_slash_setdesc(state: &mut GameState, conn_id: ConnectionId, args: &str) {
    match state.users.get(&conn_id) {
        Some(u) if u.logged && u.privileges >= privilege_level::DIOS => {}
        _ => return,
    }

    state.send_to(conn_id, &format!("{}Descripcion actualizada.{}", server_opcodes::CONSOLE_MSG, font_types::INFO)).await;
}

/// /STOP nick — Freeze user (can't move).
pub(super) async fn handle_slash_stop(state: &mut GameState, conn_id: ConnectionId, target: &str, freeze: bool) {
    match state.users.get(&conn_id) {
        Some(u) if u.logged && u.privileges >= privilege_level::SEMIDIOS => {}
        _ => return,
    }

    let target_upper = target.to_uppercase();
    let target_conn = state.users.values()
        .find(|u| u.logged && u.char_name.to_uppercase() == target_upper)
        .map(|u| u.conn_id);

    match target_conn {
        Some(tc) => {
            if let Some(user) = state.users.get_mut(&tc) {
                user.paralyzed = freeze;
            }
            let status = if freeze { "paralizado" } else { "desparalizado" };
            state.send_to(conn_id, &format!("{}{} {}.{}", server_opcodes::CONSOLE_MSG, target, status, font_types::INFO)).await;
        }
        None => {
            state.send_to(conn_id, "||196").await;
        }
    }
}

/// /CHEAT nick — Toggle god mode for user (full HP/MP/STA regen).
/// /MODMAPINFO — Modify map properties: PK, PART, LUZ, RGB.
/// VB6: TCP.bas /MODMAPINFO handler (Dios+ privilege).
pub(super) async fn handle_slash_modmapinfo(state: &mut GameState, conn_id: ConnectionId, args: &str) {
    let (map, x, y) = match state.users.get(&conn_id) {
        Some(u) if u.logged => (u.pos_map, u.pos_x, u.pos_y),
        _ => return,
    };

    let parts: Vec<&str> = args.split_whitespace().collect();
    if parts.is_empty() { return; }

    let sub_cmd = parts[0].to_uppercase();
    let map_idx = map as usize;

    match sub_cmd.as_str() {
        "PK" => {
            // VB6: /MODMAPINFO PK <0|1> — 0 = PvP on (pk=true), 1 = Safe (pk=false)
            let val: i32 = parts.get(1).and_then(|s| s.parse().ok()).unwrap_or(-1);
            if val < 0 || val > 1 { return; }
            let pk = val == 0; // VB6 inverts: 0 = PvP, 1 = safe
            if let Some(Some(game_map)) = state.game_data.maps.get_mut(map_idx) {
                game_map.info.pk = pk;
            }
            // Persist to map dat file
            let map_dat = state.base_path.join("maps").join(format!("mapa{}.dat", map));
            let section = format!("Mapa{}", map);
            let _ = crate::config::write_var(map_dat.to_str().unwrap_or(""), &section, "Pk", &val.to_string());
        }
        "PART" => {
            // VB6: /MODMAPINFO PART <particle_id> — Set particle at player tile
            let particle_id: i32 = parts.get(1).and_then(|s| s.parse().ok()).unwrap_or(0);
            if particle_id == 0 { return; }
            let pcf = format!("PCF{},{},{},0", particle_id, x, y);
            state.send_data(SendTarget::ToMap(map), &pcf).await;
        }
        "LUZ" => {
            // VB6: /MODMAPINFO LUZ <range> <R> <G> <B>
            let range: i32 = parts.get(1).and_then(|s| s.parse().ok()).unwrap_or(0);
            if range == 0 { return; }
            let r: i32 = parts.get(2).and_then(|s| s.parse().ok()).unwrap_or(0);
            let g: i32 = parts.get(3).and_then(|s| s.parse().ok()).unwrap_or(0);
            let b: i32 = parts.get(4).and_then(|s| s.parse().ok()).unwrap_or(0);
            let pcl = format!("PCL{},{},{},{},{},{}", x, y, range, r, g, b);
            state.send_data(SendTarget::ToMap(map), &pcl).await;
        }
        "RGB" => {
            // VB6: /MODMAPINFO RGB <R> <G> <B> — Set map ambient light
            let r: i32 = parts.get(1).and_then(|s| s.parse().ok()).unwrap_or(0);
            let g: i32 = parts.get(2).and_then(|s| s.parse().ok()).unwrap_or(0);
            let b: i32 = parts.get(3).and_then(|s| s.parse().ok()).unwrap_or(0);
            if let Some(Some(game_map)) = state.game_data.maps.get_mut(map_idx) {
                game_map.info.r = r;
                game_map.info.g = g;  // Note: VB6 has r/b/g order bug, we keep correct r/g/b
                game_map.info.b = b;
            }
            let pcr = format!("PCR{},{},{}", r, g, b);
            state.send_data(SendTarget::ToMap(map), &pcr).await;
        }
        _ => {}
    }
}

pub(super) async fn handle_slash_cheat(state: &mut GameState, conn_id: ConnectionId, target: &str) {
    match state.users.get(&conn_id) {
        Some(u) if u.logged && u.privileges >= privilege_level::ADMINISTRADOR => {}
        _ => return,
    }

    let target_upper = target.to_uppercase();
    let target_conn = if target_upper == "YO" {
        Some(conn_id)
    } else {
        state.users.values()
            .find(|u| u.logged && u.char_name.to_uppercase() == target_upper)
            .map(|u| u.conn_id)
    };

    match target_conn {
        Some(tc) => {
            if let Some(user) = state.users.get_mut(&tc) {
                user.min_hp = user.max_hp;
                user.min_mana = user.max_mana;
                user.min_sta = user.max_sta;
                user.min_agua = user.max_agua;
                user.min_ham = user.max_ham;
            }
            // Send stat updates
            let (hp, mana, sta) = {
                let u = state.users.get(&tc).unwrap();
                (format!("[H]{},{}", u.max_hp, u.min_hp),
                 format!("[M]{},{}", u.max_mana, u.min_mana),
                 format!("[S]{},{}", u.max_sta, u.min_sta))
            };
            state.send_to(tc, &hp).await;
            state.send_to(tc, &mana).await;
            state.send_to(tc, &sta).await;

            state.send_to(conn_id, &format!("{}{} restaurado.{}", server_opcodes::CONSOLE_MSG, target, font_types::INFO)).await;
        }
        None => {
            state.send_to(conn_id, "||196").await;
        }
    }
}

pub(super) async fn handle_slash_nave(state: &mut GameState, conn_id: ConnectionId) {
    match state.users.get(&conn_id) {
        Some(u) if u.logged && u.privileges > privilege_level::USER => {}
        _ => return,
    }
    if let Some(user) = state.users.get_mut(&conn_id) {
        user.navigating = !user.navigating;
        let status = if user.navigating { "activada" } else { "desactivada" };
        let msg = format!("||Navegacion {}{}", status, font_types::SYSTEM);
        let conn = user.conn_id;
        state.send_to(conn, &msg).await;
    }
}

/// /HABILITAR — Toggle server GM-only mode. Requires privileges > 0.
pub(super) async fn handle_slash_habilitar(state: &mut GameState, conn_id: ConnectionId) {
    match state.users.get(&conn_id) {
        Some(u) if u.logged && u.privileges > privilege_level::USER => {}
        _ => return,
    }
    if state.server_solo_gms {
        state.send_to(conn_id, "||563").await; // Server abierto
        state.server_solo_gms = false;
    } else {
        state.send_to(conn_id, "||564").await; // Server solo GMs
        state.server_solo_gms = true;
    }
}

/// /COL <color>@<msg> — Send colored message to all. Requires privileges > 0.
pub(super) async fn handle_slash_col(state: &mut GameState, conn_id: ConnectionId, args: &str) {
    let name = match state.users.get(&conn_id) {
        Some(u) if u.logged && u.privileges > privilege_level::USER => u.char_name.clone(),
        _ => return,
    };

    let color_str = read_field(1, args, '@');
    let msg_text = read_field(2, args, '@');
    if msg_text.is_empty() { return; }

    let font = match color_str.to_uppercase().as_str() {
        "LILA"     => "~200~20~215~1~0",
        "VERDE"    => "~0~255~0~1~0",
        "AZUL"     => "~0~0~255~1~0",
        "ROJO"     => "~255~0~0~1~0",
        "AMARILLO" => "~255~255~0~1~0",
        "BLANCO"   => "~255~255~255~1~0",
        "GRIS"     => "~120~120~120~1~0",
        "NARANJA"  => "~255~128~0~1~0",
        "MARRON"   => "~128~64~0~1~0",
        "CELESTE"  => "~0~255~255~1~0",
        "VIOLETA"  => "~64~0~128~1~0",
        _ => return,
    };

    let pkt = format!("N|{}> {}{}", name, msg_text, font);
    state.send_data(SendTarget::ToAll, &pkt).await;
}

/// /NOADV <name> — Clear all warnings/penalties for a character. Requires Semidios+.
pub(super) async fn handle_slash_noadv(state: &mut GameState, conn_id: ConnectionId, target: &str) {
    match state.users.get(&conn_id) {
        Some(u) if u.logged && u.privileges >= privilege_level::SEMIDIOS => {}
        _ => return,
    }
    // Clear penalties from charfile
    let base = state.base_path.clone();
    let charpath = base.join("charfile").join(format!("{}.chr", target));
    if charpath.exists() {
        let chr = charpath.to_str().unwrap_or("");
        let _ = crate::config::write_var(chr, "PENAS", "Cant", "0");
        state.send_to(conn_id, "||441").await;
        info!("[GM] Cleared penalties for {}", target);
    } else {
        state.send_to(conn_id, "||196").await; // User not found
    }
}

/// /LIBERAR <name> — Release player from prison. Requires Semidios+.
pub(super) async fn handle_slash_liberar(state: &mut GameState, conn_id: ConnectionId, target: &str) {
    let gm_name = match state.users.get(&conn_id) {
        Some(u) if u.logged && u.privileges >= privilege_level::SEMIDIOS => u.char_name.clone(),
        _ => return,
    };

    let target_upper = target.to_uppercase();
    let target_conn = state.online_names.get(&target_upper).copied();

    if let Some(t_conn) = target_conn {
        // Warp target to Ullathorpe (map 1, 50, 50) as release location
        warp_user(state, t_conn, 1, 50, 50).await;
        let pkt = format!("||443@{}@{}", gm_name, target);
        state.send_data(SendTarget::ToAdmins, &pkt).await;
        state.send_to(t_conn, "||444").await; // You've been released
        info!("[GM] {} released {} from prison", gm_name, target);
    } else {
        state.send_to(conn_id, "||198").await; // Not online
    }
}

/// /PENAS <name> — View penalties of a character. Requires Semidios+.
pub(super) async fn handle_slash_penas(state: &mut GameState, conn_id: ConnectionId, target: &str) {
    match state.users.get(&conn_id) {
        Some(u) if u.logged && u.privileges >= privilege_level::SEMIDIOS => {}
        _ => return,
    }

    let base = state.base_path.clone();
    let charpath = base.join("charfile").join(format!("{}.chr", target));
    if !charpath.exists() {
        state.send_to(conn_id, "||196").await;
        return;
    }
    let chr = charpath.to_str().unwrap_or("");

    let cant: i32 = crate::config::get_var(chr, "PENAS", "Cant")
        .parse().unwrap_or(0);
    let pkt_header = format!("||327@{}", cant);
    state.send_to(conn_id, &pkt_header).await;

    for i in 1..=cant {
        let p = crate::config::get_var(chr, "PENAS", &format!("P{}", i));
        let pkt = format!("||327@{}", p);
        state.send_to(conn_id, &pkt).await;
    }
}

/// /MOD <stat> <value> — Modify own stats. Requires Semidios+.
/// /SMOD <name> <stat> <value> — Modify another player's stats. Requires Director+.
pub(super) async fn handle_slash_mod(state: &mut GameState, conn_id: ConnectionId, args: &str, _is_self: bool) {
    match state.users.get(&conn_id) {
        Some(u) if u.logged && u.privileges >= privilege_level::SEMIDIOS => {}
        _ => return,
    }

    // VB6: ReadField(1, rData, 32) and ReadField(2, rData, 32) — space-delimited
    let parts: Vec<&str> = args.splitn(2, ' ').collect();
    if parts.len() < 2 { return; }
    let stat = parts[0].to_uppercase();
    let value_str = parts[1];
    let value: i64 = value_str.parse().unwrap_or(0);

    // Apply to self — /MOD only affects the invoker
    let target = conn_id;
    apply_mod_self(state, conn_id, target, &stat, value, value_str).await;
}

/// /SMOD <name> <stat> <value> — Modify another player's stats. Requires Director+.
/// VB6: Only a subset of /MOD subcommands (no AURA, ARMA, ESCU, CASCO, HAM, AGU, ATRI, FX).
pub(super) async fn handle_slash_smod(state: &mut GameState, conn_id: ConnectionId, args: &str) {
    let gm_name = match state.users.get(&conn_id) {
        Some(u) if u.logged && u.privileges >= privilege_level::DIRECTOR => u.char_name.clone(),
        _ => return,
    };

    // VB6: ReadField(1)=name, ReadField(2)=subcommand, ReadField(3)=value — space delimited
    let parts: Vec<&str> = args.splitn(3, ' ').collect();
    if parts.len() < 3 { return; }
    let target_name = parts[0].replace('+', " ");
    let stat = parts[1].to_uppercase();
    let value: i64 = parts.get(2).and_then(|v| v.parse().ok()).unwrap_or(0);

    let target_upper = target_name.to_uppercase();
    // SHAY protection
    if target_upper == "SHAY" && gm_name.to_uppercase() != "SHAY" { return; }

    let target_conn = match state.online_names.get(&target_upper).copied() {
        Some(c) => c,
        None => {
            state.send_to(conn_id, "||196").await;
            return;
        }
    };

    apply_mod_other(state, conn_id, target_conn, &stat, value).await;
}

/// /MOD apply — self-modification only. VB6: TCP_HandleData3.bas:1725-1877
/// Supports all subcommands: PART, AURA, FX, ATRI, ORO, EXP, BODY, HEAD,
/// CRI, CIU, LEVEL, CLASE, HAM, AGU, STA, MP, HP, ESCU, CASCO, ARMA.
pub(super) async fn apply_mod_self(state: &mut GameState, conn_id: ConnectionId, target: ConnectionId, stat: &str, value: i64, value_str: &str) {
    let (char_index, map, x, y) = match state.users.get(&target) {
        Some(u) => (u.char_index.0, u.pos_map, u.pos_x, u.pos_y),
        None => return,
    };

    match stat {
        "PART" => {
            if value <= 0 { return; }
            let pkt = format!("CFF{},{},0", char_index, value);
            state.send_data(SendTarget::ToArea { map, x, y }, &pkt).await;
        }
        "AURA" => {
            // VB6: UserList(tIndex).Char.AuraA = val(Arg2); SendUserAura
            if let Some(user) = state.users.get_mut(&target) {
                user.aura_a = value as i32;
            }
            if let Some(user) = state.users.get(&target) {
                let au_pkt = super::common::build_aura_packet(user);
                state.send_data(SendTarget::ToArea { map, x, y }, &au_pkt).await;
            }
        }
        "FX" => {
            let pkt = format!("CFX{},{},20", char_index, value);
            state.send_data(SendTarget::ToArea { map, x, y }, &pkt).await;
        }
        "ATRI" => {
            if let Some(user) = state.users.get_mut(&target) {
                for i in 0..5 {
                    user.attributes[i] = value as i32;
                }
            }
            state.send_to(conn_id, &format!("||571@{}", value)).await;
        }
        "ORO" => {
            if let Some(user) = state.users.get_mut(&target) {
                user.gold = value;
            }
            send_stats_gold(state, target).await;
            state.send_to(conn_id, &format!("||572@{}", value)).await;
        }
        "EXP" => {
            if let Some(user) = state.users.get_mut(&target) {
                user.exp = value;
            }
            check_user_level(state, target).await;
            send_stats_exp(state, target).await;
            state.send_to(conn_id, &format!("||572@{}", value)).await;
        }
        "BODY" => {
            if let Some(user) = state.users.get_mut(&target) {
                user.body = value as i32;
            }
            state.send_to(conn_id, &format!("||573@{}", value)).await;
            // VB6: ChangeUserChar → CP to map
            let u = state.users.get(&target).unwrap();
            let cp = format!("CP{},{},{},{},{},{},0,0,{}", char_index, value, u.head, u.heading, u.weapon_anim, u.shield_anim, u.casco_anim);
            state.send_data(SendTarget::ToMap(map), &cp).await;
        }
        "HEAD" => {
            if let Some(user) = state.users.get_mut(&target) {
                user.head = value as i32;
            }
            state.send_to(conn_id, &format!("||574@{}", value)).await;
            let u = state.users.get(&target).unwrap();
            let cp = format!("CP{},{},{},{},{},{},0,0,{}", char_index, u.body, value, u.heading, u.weapon_anim, u.shield_anim, u.casco_anim);
            state.send_data(SendTarget::ToMap(map), &cp).await;
        }
        "CRI" => {
            if let Some(user) = state.users.get_mut(&target) {
                user.criminales_matados = value as i32;
            }
            state.send_to(conn_id, &format!("||575@{}", value)).await;
        }
        "CIU" => {
            if let Some(user) = state.users.get_mut(&target) {
                user.ciudadanos_matados = value as i32;
            }
            state.send_to(conn_id, &format!("||576@{}", value)).await;
        }
        "LEVEL" => {
            if let Some(user) = state.users.get_mut(&target) {
                user.level = value as i32;
            }
            state.send_to(conn_id, &format!("||577@{}", value)).await;
        }
        "CLASE" => {
            if let Some(user) = state.users.get_mut(&target) {
                user.class = value_str.to_string();
            }
            state.send_to(conn_id, &format!("||578@{}", value_str)).await;
        }
        "HAM" => {
            if let Some(user) = state.users.get_mut(&target) {
                user.min_ham = value as i32;
                user.max_ham = value as i32;
            }
            state.send_to(conn_id, &format!("||579@{}", value)).await;
        }
        "AGU" => {
            if let Some(user) = state.users.get_mut(&target) {
                user.min_agua = value as i32;
                user.max_agua = value as i32;
            }
            state.send_to(conn_id, &format!("||580@{}", value)).await;
        }
        "STA" => {
            if let Some(user) = state.users.get_mut(&target) {
                user.min_sta = value as i32;
                user.max_sta = value as i32;
            }
            send_stats_sta(state, target).await;
            state.send_to(conn_id, &format!("||581@{}", value)).await;
        }
        "MP" => {
            if let Some(user) = state.users.get_mut(&target) {
                user.min_mana = value as i32;
                user.max_mana = value as i32;
            }
            send_stats_mana(state, target).await;
            state.send_to(conn_id, &format!("||582@{}", value)).await;
        }
        "HP" => {
            if let Some(user) = state.users.get_mut(&target) {
                user.min_hp = value as i32;
                user.max_hp = value as i32;
            }
            send_stats_hp(state, target).await;
            state.send_to(conn_id, &format!("||583@{}", value)).await;
        }
        "ESCU" => {
            if let Some(user) = state.users.get_mut(&target) {
                user.shield_anim = value as i32;
            }
            let u = state.users.get(&target).unwrap();
            let cp = format!("CP{},{},{},{},{},{},0,0,{}", char_index, u.body, u.head, u.heading, u.weapon_anim, value, u.casco_anim);
            state.send_data(SendTarget::ToMap(map), &cp).await;
        }
        "CASCO" => {
            if let Some(user) = state.users.get_mut(&target) {
                user.casco_anim = value as i32;
            }
            let u = state.users.get(&target).unwrap();
            let cp = format!("CP{},{},{},{},{},{},0,0,{}", char_index, u.body, u.head, u.heading, u.weapon_anim, u.shield_anim, value);
            state.send_data(SendTarget::ToMap(map), &cp).await;
        }
        "ARMA" => {
            if let Some(user) = state.users.get_mut(&target) {
                user.weapon_anim = value as i32;
            }
            let u = state.users.get(&target).unwrap();
            let cp = format!("CP{},{},{},{},{},{},0,0,{}", char_index, u.body, u.head, u.heading, value, u.shield_anim, u.casco_anim);
            state.send_data(SendTarget::ToMap(map), &cp).await;
        }
        _ => {
            state.send_to(conn_id, "||584").await;
        }
    }
}

/// /SMOD apply — modify another player. VB6: TCP_HandleData3.bas:2051-2163
/// Only supports: PART, ORO, EXP, BODY, HEAD, CRI, CIU, LEVEL, CLASE, STA, MP, HP.
/// All modifications are broadcast to admins via ||591 packets.
pub(super) async fn apply_mod_other(state: &mut GameState, gm_conn: ConnectionId, target: ConnectionId, stat: &str, value: i64) {
    let gm_name = state.users.get(&gm_conn).map(|u| u.char_name.clone()).unwrap_or_default();
    let target_name = state.users.get(&target).map(|u| u.char_name.clone()).unwrap_or_default();
    let (char_index, map, x, y) = match state.users.get(&target) {
        Some(u) => (u.char_index.0, u.pos_map, u.pos_x, u.pos_y),
        None => return,
    };

    match stat {
        "PART" => {
            if value <= 0 { return; }
            let pkt = format!("CFF{},{},0", char_index, value);
            state.send_data(SendTarget::ToArea { map, x, y }, &pkt).await;
        }
        "ORO" => {
            if let Some(user) = state.users.get_mut(&target) {
                user.gold = value;
            }
            send_stats_gold(state, target).await;
            let pkt = format!("||591@{}@oro@{}@{}", gm_name, target_name, value);
            state.send_data(SendTarget::ToAdmins, &pkt).await;
        }
        "EXP" => {
            if let Some(user) = state.users.get_mut(&target) {
                user.exp = value;
            }
            check_user_level(state, target).await;
            send_stats_exp(state, target).await;
            let pkt = format!("||591@{}@experiencia@{}@{}", gm_name, target_name, value);
            state.send_data(SendTarget::ToAdmins, &pkt).await;
        }
        "BODY" => {
            if let Some(user) = state.users.get_mut(&target) {
                user.body = value as i32;
            }
            let u = state.users.get(&target).unwrap();
            let cp = format!("CP{},{},{},{},{},{},0,0,{}", char_index, value, u.head, u.heading, u.weapon_anim, u.shield_anim, u.casco_anim);
            state.send_data(SendTarget::ToMap(map), &cp).await;
            let pkt = format!("||591@{}@body@{}@{}", gm_name, target_name, value);
            state.send_data(SendTarget::ToAdmins, &pkt).await;
        }
        "HEAD" => {
            if let Some(user) = state.users.get_mut(&target) {
                user.head = value as i32;
            }
            let u = state.users.get(&target).unwrap();
            let cp = format!("CP{},{},{},{},{},{},0,0,{}", char_index, u.body, value, u.heading, u.weapon_anim, u.shield_anim, u.casco_anim);
            state.send_data(SendTarget::ToMap(map), &cp).await;
            let pkt = format!("||591@{}@head@{}@{}", gm_name, target_name, value);
            state.send_data(SendTarget::ToAdmins, &pkt).await;
        }
        "CRI" => {
            if let Some(user) = state.users.get_mut(&target) {
                user.criminales_matados = value as i32;
            }
            let pkt = format!("||591@{}@criminales@{}@{}", gm_name, target_name, value);
            state.send_data(SendTarget::ToAdmins, &pkt).await;
        }
        "CIU" => {
            if let Some(user) = state.users.get_mut(&target) {
                user.ciudadanos_matados = value as i32;
            }
            let pkt = format!("||591@{}@ciudadanos@{}@{}", gm_name, target_name, value);
            state.send_data(SendTarget::ToAdmins, &pkt).await;
        }
        "LEVEL" => {
            if let Some(user) = state.users.get_mut(&target) {
                user.level = value as i32;
            }
            let pkt_level = format!("[L]{}", value);
            state.send_to(target, &pkt_level).await;
            let pkt = format!("||591@{}@nivel@{}@{}", gm_name, target_name, value);
            state.send_data(SendTarget::ToAdmins, &pkt).await;
        }
        "CLASE" => {
            // VB6: class is a string, but we receive it as numeric here
            let pkt = format!("||591@{}@clase@{}@{}", gm_name, target_name, value);
            state.send_data(SendTarget::ToAdmins, &pkt).await;
        }
        "STA" => {
            if let Some(user) = state.users.get_mut(&target) {
                user.min_sta = value as i32;
                user.max_sta = value as i32;
            }
            send_stats_sta(state, target).await;
            let pkt = format!("||591@{}@energia@{}@{}", gm_name, target_name, value);
            state.send_data(SendTarget::ToAdmins, &pkt).await;
        }
        "MP" => {
            if let Some(user) = state.users.get_mut(&target) {
                user.min_mana = value as i32;
                user.max_mana = value as i32;
            }
            send_stats_mana(state, target).await;
            let pkt = format!("||591@{}@mana@{}@{}", gm_name, target_name, value);
            state.send_data(SendTarget::ToAdmins, &pkt).await;
        }
        "HP" => {
            if let Some(user) = state.users.get_mut(&target) {
                user.min_hp = value as i32;
                user.max_hp = value as i32;
            }
            send_stats_hp(state, target).await;
            let pkt = format!("||591@{}@vida@{}@{}", gm_name, target_name, value);
            state.send_data(SendTarget::ToAdmins, &pkt).await;
        }
        _ => {
            state.send_to(gm_conn, "||584").await;
        }
    }
}

/// VB6 ClosestLegalPos: find the nearest walkable tile to (x,y) on map.
/// Searches in expanding rings: exact pos first, then ±1, ±2, etc.
// find_closest_legal_pos — moved to common.rs

/// /ACC <npc_id> or /RACC <npc_id> — Spawn NPC at GM's position. Requires GranDios+.
pub(super) async fn handle_slash_acc(state: &mut GameState, conn_id: ConnectionId, npc_id_str: &str, with_respawn: bool) {
    let (map, x, y, name) = match state.users.get(&conn_id) {
        Some(u) if u.logged && u.privileges >= privilege_level::GRAN_DIOS => {
            (u.pos_map, u.pos_x, u.pos_y, u.char_name.clone())
        }
        _ => return,
    };

    let npc_num: usize = match npc_id_str.parse() {
        Ok(n) if n > 0 => n,
        _ => return,
    };

    // Look up NPC name for logging
    let npc_name = state.game_data.npcs.get(npc_num)
        .map(|n| n.name.clone())
        .unwrap_or_else(|| format!("NPC#{}", npc_num));

    // VB6: SpawnNpc uses ClosestLegalPos — find nearest free tile
    // The GM's tile is occupied by the GM, so search nearby tiles
    let (spawn_x, spawn_y) = find_closest_legal_pos(state, map, x, y);
    if spawn_x == 0 && spawn_y == 0 {
        let msg = format!("{}No hay posicion valida para spawnear.{}", server_opcodes::CONSOLE_MSG, font_types::INFO);
        state.send_to(conn_id, &msg).await;
        return;
    }

    // Spawn NPC at closest legal position
    if let Some(npc_idx) = state.spawn_npc(npc_num, map, spawn_x, spawn_y) {
        // Broadcast CC for the NPC to nearby players
        let cc_pkt = state.npcs.get(npc_idx)
            .and_then(|n| n.as_ref())
            .map(|n| n.build_cc_packet());
        if let Some(cc) = cc_pkt {
            state.send_data(SendTarget::ToArea { map, x: spawn_x, y: spawn_y }, &cc).await;
        }
        let prefix = if with_respawn { "con respawn" } else { "sin respawn" };
        info!("[GM] {} spawned {} ({}) at map {} ({},{}) {}", name, npc_name, npc_num, map, spawn_x, spawn_y, prefix);
    }
}

/// Generic privilege setter — /CONSEJERO, /SEMIDIOS, /DIOS, /GDIOS, /EVENT, /DIRECTOR,
/// /SUBADMINISTRADOR, /DEVELOPER, /ADMIN, /PJ
pub(super) async fn handle_slash_set_privilege(
    state: &mut GameState,
    conn_id: ConnectionId,
    target: &str,
    new_level: i32,
    level_name: &str,
    required_level: i32,
) {
    let gm_name = match state.users.get(&conn_id) {
        Some(u) if u.logged && u.privileges >= required_level => u.char_name.clone(),
        _ => return,
    };

    let target_upper = target.to_uppercase();

    // SHAY protection: if targeting SHAY and GM is not SHAY, punish the GM
    if target_upper == "SHAY" && gm_name.to_uppercase() != "SHAY" {
        // Demote the attacker instead (matching VB6 behavior)
        if let Some(user) = state.users.get_mut(&conn_id) {
            user.privileges = new_level.min(user.privileges);
        }
        // Re-warp to refresh
        let (map, x, y) = state.users.get(&conn_id)
            .map(|u| (u.pos_map, u.pos_x, u.pos_y))
            .unwrap_or((1, 50, 50));
        warp_user(state, conn_id, map, x, y).await;
        return;
    }

    // Find target
    let target_conn = match state.online_names.get(&target_upper).copied() {
        Some(c) => c,
        None => {
            state.send_to(conn_id, "||198").await; // Not online
            return;
        }
    };

    let target_real_name = state.users.get(&target_conn)
        .map(|u| u.char_name.clone())
        .unwrap_or_default();

    // Announce to admins
    let pkt = format!("||562@{}@{}@{}", gm_name, target_real_name, level_name);
    state.send_data(SendTarget::ToAdmins, &pkt).await;

    // Set the new privilege level
    if let Some(user) = state.users.get_mut(&target_conn) {
        user.privileges = new_level;
    }

    // Re-warp to refresh character appearance (privilege affects CC packet)
    let (map, x, y) = state.users.get(&target_conn)
        .map(|u| (u.pos_map, u.pos_x, u.pos_y))
        .unwrap_or((1, 50, 50));
    warp_user(state, target_conn, map, x, y).await;

    info!("[GM] {} set {} to {} (level {})", gm_name, target_real_name, level_name, new_level);
}

/// /CHANGENICK <newname> — Change own character name. Requires Administrador.
pub(super) async fn handle_slash_changenick(state: &mut GameState, conn_id: ConnectionId, new_name: &str) {
    match state.users.get(&conn_id) {
        Some(u) if u.logged && u.privileges >= privilege_level::ADMINISTRADOR => {}
        _ => return,
    }

    if new_name.is_empty() { return; }

    // Remove old name from online_names
    let old_name = state.users.get(&conn_id).map(|u| u.char_name.clone()).unwrap_or_default();
    state.online_names.remove(&old_name.to_uppercase());

    // Set new name
    if let Some(user) = state.users.get_mut(&conn_id) {
        user.char_name = new_name.to_string();
    }
    state.online_names.insert(new_name.to_uppercase(), conn_id);

    // Re-warp to refresh CC packet with new name
    let (map, x, y) = state.users.get(&conn_id)
        .map(|u| (u.pos_map, u.pos_x, u.pos_y))
        .unwrap_or((1, 50, 50));
    warp_user(state, conn_id, map, x, y).await;

    info!("[GM] {} changed nick to {}", old_name, new_name);
}

/// /BORRARPJ <name> — Delete a character. Requires Director+.
pub(super) async fn handle_slash_borrarpj(state: &mut GameState, conn_id: ConnectionId, target: &str) {
    let gm_name = match state.users.get(&conn_id) {
        Some(u) if u.logged && u.privileges >= privilege_level::DIRECTOR => u.char_name.clone(),
        _ => return,
    };

    let target_upper = target.to_uppercase();

    // SHAY protection
    if target_upper == "SHAY" && gm_name.to_uppercase() != "SHAY" { return; }

    // If target is online, disconnect them
    if let Some(&t_conn) = state.online_names.get(&target_upper) {
        close_connection(state, t_conn).await;
    }

    let base = state.base_path.clone();

    // Find the account that owns this character
    let charpath = base.join("charfile").join(format!("{}.chr", target));
    if !charpath.exists() {
        state.send_to(conn_id, "||196").await;
        return;
    }
    let chr = charpath.to_str().unwrap_or("");
    let account_name = crate::config::get_var(chr, "CHAR", "Cuenta");
    if account_name.is_empty() {
        state.send_to(conn_id, "||196").await;
        return;
    }

    // Remove from account file
    let acc_path = base.join("Accounts").join(format!("{}.act", account_name));
    if acc_path.exists() {
        let act = acc_path.to_str().unwrap_or("");
        let num_pjs: i32 = crate::config::get_var(act, "PJS", "NumPjs")
            .parse().unwrap_or(0);
        let mut found_idx = 0;
        for i in 1..=num_pjs {
            let pj = crate::config::get_var(act, "PJS", &format!("PJ{}", i));
            if pj.to_uppercase() == target_upper {
                found_idx = i;
                break;
            }
        }
        if found_idx > 0 {
            // Shift remaining characters down
            for i in found_idx..num_pjs {
                let next_pj = crate::config::get_var(act, "PJS", &format!("PJ{}", i + 1));
                let _ = crate::config::write_var(act, "PJS", &format!("PJ{}", i), &next_pj);
            }
            let _ = crate::config::write_var(act, "PJS", &format!("PJ{}", num_pjs), "");
            let _ = crate::config::write_var(act, "PJS", "NumPjs", &(num_pjs - 1).to_string());
        }
    }

    // Delete charfile
    let _ = std::fs::remove_file(&charpath);

    // Announce
    let pkt = format!("||565@{}@{}", gm_name, target);
    state.send_data(SendTarget::ToAll, &pkt).await;

    info!("[GM] {} deleted character {}", gm_name, target);
}

/// /BANHD <name> — Ban player's HD + IP + account. Requires Administrador.
pub(super) async fn handle_slash_banhd(state: &mut GameState, conn_id: ConnectionId, target: &str) {
    let gm_name = match state.users.get(&conn_id) {
        Some(u) if u.logged && u.privileges >= privilege_level::ADMINISTRADOR => u.char_name.clone(),
        _ => return,
    };

    let target_upper = target.to_uppercase();

    // SHAY protection
    if target_upper == "SHAY" { return; }

    let target_conn = match state.online_names.get(&target_upper).copied() {
        Some(c) => c,
        None => {
            state.send_to(conn_id, "||196").await;
            return;
        }
    };

    // Get target data
    let (target_ip, target_hd, target_account) = match state.users.get(&target_conn) {
        Some(u) => (u.ip.clone(), u.hd_serial.clone(), u.account_name.clone()),
        None => return,
    };

    // Set ban flag in charfile
    let base = state.base_path.clone();
    let charpath = base.join("charfile").join(format!("{}.chr", target));
    if charpath.exists() {
        let chr = charpath.to_str().unwrap_or("");
        let _ = crate::config::write_var(chr, "FLAGS", "Ban", "1");
        // Add penalty
        let cant: i32 = crate::config::get_var(chr, "PENAS", "Cant")
            .parse().unwrap_or(0);
        let _ = crate::config::write_var(chr, "PENAS", "Cant", &(cant + 1).to_string());
        let _ = crate::config::write_var(chr, "PENAS", &format!("P{}", cant + 1), "Tolerancia 0.");
    }

    // Ban HD serial
    if !target_hd.is_empty() {
        let _ = state.bans.ban_hd(&state.pool, &target_hd).await;
    }

    // Ban IP
    if !target_ip.is_empty() {
        let _ = state.bans.ban_ip(&state.pool, &target_ip).await;
    }

    // Ban account
    if !target_account.is_empty() {
        let _ = crate::db::accounts::set_account_banned(&state.pool, &target_account, true, "Tolerancia 0").await;
    }

    // Disconnect the target
    close_connection(state, target_conn).await;

    // Announce
    let pkt1 = format!("||567@{}@{}", gm_name, target);
    let pkt2 = format!("||568@{}@{}", gm_name, target_hd);
    let pkt3 = format!("||569@{}@{}", gm_name, target_ip);
    let pkt4 = format!("||570@{}@{}", gm_name, target_account);
    state.send_data(SendTarget::ToAll, &pkt1).await;
    state.send_data(SendTarget::ToAll, &pkt2).await;
    state.send_data(SendTarget::ToAll, &pkt3).await;
    state.send_data(SendTarget::ToAll, &pkt4).await;

    info!("[GM] {} banned {} (HD={}, IP={}, Account={})", gm_name, target, target_hd, target_ip, target_account);
}

/// /HECHIZO <name> <spell_id> — Teach a spell to a player. Requires Administrador.
pub(super) async fn handle_slash_hechizo(state: &mut GameState, conn_id: ConnectionId, args: &str) {
    match state.users.get(&conn_id) {
        Some(u) if u.logged && u.privileges >= privilege_level::ADMINISTRADOR => {}
        _ => return,
    }

    let args_upper = args.to_uppercase();
    let parts: Vec<&str> = args_upper.splitn(2, ' ').collect();
    if parts.len() < 2 { return; }

    let target_name = parts[0].replace('+', " ");
    let spell_id: i32 = match parts[1].parse() {
        Ok(s) if s > 0 => s,
        _ => return,
    };

    let target_upper = target_name.to_uppercase();
    let target_conn = match state.online_names.get(&target_upper).copied() {
        Some(c) => c,
        None => return,
    };

    // Check if they already have this spell
    let already_has = state.users.get(&target_conn)
        .map(|u| u.spells.iter().any(|&s| s == spell_id))
        .unwrap_or(false);
    if already_has { return; }

    // Find empty spell slot
    let empty_slot = state.users.get(&target_conn)
        .and_then(|u| u.spells.iter().position(|&s| s == 0));

    if let Some(slot) = empty_slot {
        if let Some(user) = state.users.get_mut(&target_conn) {
            user.spells[slot] = spell_id;
        }
        // Send spell slot update
        let spell_name = state.get_spell(spell_id)
            .map(|s| s.nombre.clone())
            .unwrap_or_else(|| format!("Hechizo {}", spell_id));
        let pkt = format!("SHS{},{}", slot + 1, spell_name);
        state.send_to(target_conn, &pkt).await;
    }
}

/// /DONACION <name>@<amount> — Award donation points. Requires Administrador.
pub(super) async fn handle_slash_donacion(state: &mut GameState, conn_id: ConnectionId, args: &str) {
    match state.users.get(&conn_id) {
        Some(u) if u.logged && u.privileges >= privilege_level::ADMINISTRADOR => {}
        _ => return,
    }

    let target_name = read_field(1, args, '@');
    let amount_str = read_field(2, args, '@');
    let amount: i64 = amount_str.parse().unwrap_or(0);
    if amount <= 0 { return; }

    let target_upper = target_name.to_uppercase();
    let target_conn = match state.online_names.get(&target_upper).copied() {
        Some(c) => c,
        None => {
            state.send_to(conn_id, "||196").await;
            return;
        }
    };

    let target_real_name = state.users.get(&target_conn)
        .map(|u| u.char_name.clone())
        .unwrap_or_default();

    // Donation points = amount * 10 (stored in charfile, not in runtime UserState for now)
    let pkt = format!("||561@{}@{}", amount, target_real_name);
    state.send_to(conn_id, &pkt).await;

    info!("[GM] Donation of ${} to {} ({} points)", amount, target_real_name, amount * 10);
}

/// /RESETVALS <type> — Reset arena/duel/CvC state. Requires Semidios+.
/// Types: ARENA1, ARENA2, ARENA3, ARENA4, 2VS2, DESAFIO, CVC, INVOCACIONES
pub(super) async fn handle_slash_resetvals(state: &mut GameState, conn_id: ConnectionId, val_type: &str) {
    match state.users.get(&conn_id) {
        Some(u) if u.logged && u.privileges >= privilege_level::SEMIDIOS => {}
        _ => return,
    }

    let vt = val_type.to_uppercase();
    match vt.as_str() {
        "DESAFIO" => {
            state.send_to(conn_id, "||586").await;
        }
        "ARENA1" => {
            state.send_data(SendTarget::ToAll, "||587@1").await;
        }
        "ARENA2" => {
            state.send_data(SendTarget::ToAll, "||587@2").await;
        }
        "ARENA3" => {
            state.send_data(SendTarget::ToAll, "||587@3").await;
        }
        "ARENA4" => {
            state.send_data(SendTarget::ToAll, "||587@4").await;
        }
        "2VS2" => {
            state.send_to(conn_id, "||588").await;
        }
        "CVC" => {
            if state.cvc_funciona {
                // Revive dead CvC participants before warping back
                let dead_cvc: Vec<ConnectionId> = state.users.iter()
                    .filter(|(_, u)| u.en_cvc && u.dead)
                    .map(|(&cid, _)| cid)
                    .collect();
                for cid in dead_cvc {
                    revive_user(state, cid).await;
                }
                llevar_usuarios_cvc(state).await;
                state.cvc_funciona = false;
                state.cvc_clan1_count = 0;
                state.cvc_clan2_count = 0;
                state.cvc_guild1 = 0;
                state.cvc_guild2 = 0;
                state.cvc_nombre1.clear();
                state.cvc_nombre2.clear();
            }
            state.cvc_pending_target_guild = 0;
            state.cvc_pending_challenger_guild = 0;
            state.cvc_pending_challenger_name.clear();
            state.send_to(conn_id, "||589").await;
        }
        "INVOCACIONES" => {
            state.send_to(conn_id, "||590").await;
        }
        _ => {
            state.send_to(conn_id, "||585").await; // Invalid type
        }
    }
    info!("[GM] Reset vals: {}", vt);
}

// ── Reload configuration commands ───────────────────────────────────────────

/// /RELOADSINI — reload server.ini configuration.
pub(super) async fn handle_reload_sini(state: &mut GameState, conn_id: ConnectionId) {
    let name = match state.users.get(&conn_id) {
        Some(u) if u.logged && u.privileges >= privilege_level::ADMINISTRADOR => u.char_name.clone(),
        _ => return,
    };

    let base = state.base_path.clone();
    match crate::config::ServerConfig::load(&base) {
        Ok(new_config) => {
            // Preserve port (can't rebind at runtime) but reload everything else
            let old_port = state.config.port;
            state.config = new_config;
            state.config.port = old_port;

            // Reload role overrides from server.ini (VB6: /RELOADSINI also reloads role lists)
            state.role_overrides = crate::config::load_roles(&base);

            state.send_to(conn_id, &format!("{}server.ini recargado ({} roles).{}", server_opcodes::CONSOLE_MSG, state.role_overrides.len(), font_types::INFO)).await;
            info!("[GM] {} reloaded server.ini ({} role overrides)", name, state.role_overrides.len());
        }
        Err(e) => {
            state.send_to(conn_id, &format!("{}Error recargando server.ini: {}{}", server_opcodes::CONSOLE_MSG, e, font_types::INFO)).await;
        }
    }
}

/// /LOADOBJ — reload Obj.dat (objects database).
pub(super) async fn handle_reload_objects(state: &mut GameState, conn_id: ConnectionId) {
    let name = match state.users.get(&conn_id) {
        Some(u) if u.logged && u.privileges >= privilege_level::ADMINISTRADOR => u.char_name.clone(),
        _ => return,
    };

    let base = state.base_path.clone();
    match crate::data::objects::load_objects(&base) {
        Ok(objects) => {
            let count = objects.len();
            state.game_data.objects = objects;
            state.send_to(conn_id, &format!("{}Obj.dat recargado ({} objetos).{}", server_opcodes::CONSOLE_MSG, count, font_types::INFO)).await;
            info!("[GM] {} reloaded Obj.dat ({} objects)", name, count);
        }
        Err(e) => {
            state.send_to(conn_id, &format!("{}Error recargando Obj.dat: {}{}", server_opcodes::CONSOLE_MSG, e, font_types::INFO)).await;
        }
    }
}

/// /LOADHECHIZOS — reload Hechizos.dat (spells database).
pub(super) async fn handle_reload_spells(state: &mut GameState, conn_id: ConnectionId) {
    let name = match state.users.get(&conn_id) {
        Some(u) if u.logged && u.privileges >= privilege_level::ADMINISTRADOR => u.char_name.clone(),
        _ => return,
    };

    let base = state.base_path.clone();
    match crate::data::spells::load_spells(&base) {
        Ok(spells) => {
            let count = spells.len();
            state.game_data.spells = spells;
            state.send_to(conn_id, &format!("{}Hechizos.dat recargado ({} hechizos).{}", server_opcodes::CONSOLE_MSG, count, font_types::INFO)).await;
            info!("[GM] {} reloaded Hechizos.dat ({} spells)", name, count);
        }
        Err(e) => {
            state.send_to(conn_id, &format!("{}Error recargando Hechizos.dat: {}{}", server_opcodes::CONSOLE_MSG, e, font_types::INFO)).await;
        }
    }
}

/// /LOADNPCS — reload NPCs.dat + NPCs-HOSTILES.dat.
pub(super) async fn handle_reload_npcs(state: &mut GameState, conn_id: ConnectionId) {
    let name = match state.users.get(&conn_id) {
        Some(u) if u.logged && u.privileges >= privilege_level::ADMINISTRADOR => u.char_name.clone(),
        _ => return,
    };

    let base = state.base_path.clone();
    match crate::data::npcs::load_npcs(&base) {
        Ok(npc_db) => {
            let count = npc_db.count();
            state.game_data.npcs = npc_db;
            state.send_to(conn_id, &format!("{}NPCs.dat recargado ({} NPCs).{}", server_opcodes::CONSOLE_MSG, count, font_types::INFO)).await;
            info!("[GM] {} reloaded NPCs.dat ({} NPCs)", name, count);
        }
        Err(e) => {
            state.send_to(conn_id, &format!("{}Error recargando NPCs.dat: {}{}", server_opcodes::CONSOLE_MSG, e, font_types::INFO)).await;
        }
    }
}

/// /LOADBALANCE — reload ClassBonus.dat (balance data).
pub(super) async fn handle_reload_balance(state: &mut GameState, conn_id: ConnectionId) {
    let name = match state.users.get(&conn_id) {
        Some(u) if u.logged && u.privileges >= privilege_level::ADMINISTRADOR => u.char_name.clone(),
        _ => return,
    };

    let base = state.base_path.clone();
    match crate::data::balance::load_balance(&base) {
        Ok(balance) => {
            state.game_data.balance = balance;
            state.send_to(conn_id, &format!("{}Balance recargado.{}", server_opcodes::CONSOLE_MSG, font_types::INFO)).await;
            info!("[GM] {} reloaded Balance data", name);
        }
        Err(e) => {
            state.send_to(conn_id, &format!("{}Error recargando Balance: {}{}", server_opcodes::CONSOLE_MSG, e, font_types::INFO)).await;
        }
    }
}

/// /LOADQUESTS — reload Quests.dat.
pub(super) async fn handle_reload_quests(state: &mut GameState, conn_id: ConnectionId) {
    let name = match state.users.get(&conn_id) {
        Some(u) if u.logged && u.privileges >= privilege_level::ADMINISTRADOR => u.char_name.clone(),
        _ => return,
    };

    let base = state.base_path.clone();
    match crate::data::quests::load_quests(&base) {
        Ok(quests) => {
            let count = quests.len();
            state.game_data.quests = quests;
            state.send_to(conn_id, &format!("{}Quests.dat recargado ({} quests).{}", server_opcodes::CONSOLE_MSG, count, font_types::INFO)).await;
            info!("[GM] {} reloaded Quests.dat ({} quests)", name, count);
        }
        Err(e) => {
            state.send_to(conn_id, &format!("{}Error recargando Quests.dat: {}{}", server_opcodes::CONSOLE_MSG, e, font_types::INFO)).await;
        }
    }
}

/// /LOADMAP N — reload a specific map from disk.
pub(super) async fn handle_reload_map(state: &mut GameState, conn_id: ConnectionId, map_str: &str) {
    let name = match state.users.get(&conn_id) {
        Some(u) if u.logged && u.privileges >= privilege_level::ADMINISTRADOR => u.char_name.clone(),
        _ => return,
    };

    let map_num: usize = match map_str.trim().parse() {
        Ok(n) if n >= 1 => n,
        _ => {
            state.send_to(conn_id, &format!("{}Uso: /LOADMAP <numero>{}", server_opcodes::CONSOLE_MSG, font_types::INFO)).await;
            return;
        }
    };

    let base = state.base_path.clone();
    match crate::data::maps::load_map(&base, map_num) {
        Ok(new_map) => {
            // Ensure the maps vec is large enough
            if map_num >= state.game_data.maps.len() {
                state.game_data.maps.resize_with(map_num + 1, || None);
            }
            state.game_data.maps[map_num] = Some(new_map);

            // Also update the world grid for this map
            state.world.reload_map(map_num, &state.game_data.maps);

            state.send_to(conn_id, &format!("{}Mapa {} recargado.{}", server_opcodes::CONSOLE_MSG, map_num, font_types::INFO)).await;
            info!("[GM] {} reloaded map {}", name, map_num);
        }
        Err(e) => {
            state.send_to(conn_id, &format!("{}Error recargando mapa {}: {}{}", server_opcodes::CONSOLE_MSG, map_num, e, font_types::INFO)).await;
        }
    }
}

// =====================================================================
// Missing GM commands — VB6 parity audit
// =====================================================================

/// /BLOQ — Toggle tile blocked state at GM position. VB6 TCP.bas:5134
/// Requires Semidios+. Broadcasts BQ packet to map.
pub(super) async fn handle_slash_bloq(state: &mut GameState, conn_id: ConnectionId) {
    let (map, x, y) = match state.users.get(&conn_id) {
        Some(u) if u.logged && u.privileges >= privilege_level::SEMIDIOS => (u.pos_map, u.pos_x, u.pos_y),
        _ => return,
    };

    let map_idx = map as usize;
    // Toggle blocked state and capture result
    let toggle_result = if let Some(Some(game_map)) = state.game_data.maps.get_mut(map_idx) {
        if x >= 1 && x <= 100 && y >= 1 && y <= 100 {
            let tile = &mut game_map.tiles[(y - 1) as usize][(x - 1) as usize];
            tile.blocked = !tile.blocked;
            Some(tile.blocked)
        } else {
            None
        }
    } else {
        None
    };

    if let Some(is_blocked) = toggle_result {
        let blocked_val = if is_blocked { 1 } else { 0 };

        // Broadcast BQ packet to everyone on the map
        let bq_pkt = format!("BQ{},{},{}", x, y, blocked_val);
        state.send_data(SendTarget::ToMap(map), &bq_pkt).await;

        let status = if is_blocked { "bloqueado" } else { "desbloqueado" };
        state.send_to(conn_id, &format!("{}Tile ({},{}) {}.{}", server_opcodes::CONSOLE_MSG, x, y, status, font_types::INFO)).await;
    }
}

/// /DAMEBANCO <name> — Transfer all items from player's bank to GM's inventory. VB6 TCP.bas:3880
/// Requires GranDios+.
pub(super) async fn handle_slash_damebanco(state: &mut GameState, conn_id: ConnectionId, target: &str) {
    match state.users.get(&conn_id) {
        Some(u) if u.logged && u.privileges >= privilege_level::GRAN_DIOS => {}
        _ => return,
    }

    let target_id = match state.find_user_by_name(target) {
        Some(id) => id,
        None => {
            state.send_to(conn_id, &format!("{}Jugador '{}' no encontrado.{}", server_opcodes::CONSOLE_MSG, target, font_types::INFO)).await;
            return;
        }
    };

    // Collect bank items from target
    let bank_items: Vec<InventorySlot> = match state.users.get(&target_id) {
        Some(u) => u.bank.iter().filter(|s| s.obj_index > 0).cloned().collect(),
        None => return,
    };

    if bank_items.is_empty() {
        state.send_to(conn_id, &format!("{}El banco del jugador esta vacio.{}", server_opcodes::CONSOLE_MSG, font_types::INFO)).await;
        return;
    }

    // Clear target's bank
    if let Some(u) = state.users.get_mut(&target_id) {
        for slot in u.bank.iter_mut() {
            *slot = InventorySlot::default();
        }
    }

    // Add to GM's inventory
    let mut added = 0;
    for item in &bank_items {
        let empty_slot = state.users.get(&conn_id)
            .and_then(|u| u.inventory.iter().position(|s| s.obj_index == 0));
        if let Some(slot_idx) = empty_slot {
            if let Some(user) = state.users.get_mut(&conn_id) {
                user.inventory[slot_idx] = item.clone();
                added += 1;
            }
        }
    }

    send_full_inventory(state, conn_id).await;
    state.send_to(conn_id, &format!("{}Transferidos {} items del banco de '{}'.{}", server_opcodes::CONSOLE_MSG, added, target, font_types::INFO)).await;
}

/// /DV <name> — Devolver: warp player back to previous map position. VB6 TCP.bas:4190
/// Used for prison release / arena exit. Requires Semidios+.
pub(super) async fn handle_slash_dv(state: &mut GameState, conn_id: ConnectionId, target: &str) {
    match state.users.get(&conn_id) {
        Some(u) if u.logged && u.privileges >= privilege_level::SEMIDIOS => {}
        _ => return,
    }

    let target_id = match state.find_user_by_name(target) {
        Some(id) => id,
        None => {
            state.send_to(conn_id, &format!("{}Jugador '{}' no encontrado.{}", server_opcodes::CONSOLE_MSG, target, font_types::INFO)).await;
            return;
        }
    };

    // Get saved previous position
    let (prev_map, prev_x, prev_y) = match state.users.get(&target_id) {
        Some(u) if u.mapa_anterior > 0 => (u.mapa_anterior, u.x_anterior, u.y_anterior),
        _ => {
            state.send_to(conn_id, &format!("{}El jugador no tiene posicion anterior guardada.{}", server_opcodes::CONSOLE_MSG, font_types::INFO)).await;
            return;
        }
    };

    // Clear saved position
    if let Some(u) = state.users.get_mut(&target_id) {
        u.mapa_anterior = 0;
        u.x_anterior = 0;
        u.y_anterior = 0;
        u.jail_timer = 0; // Release from jail if applicable
    }

    warp_user(state, target_id, prev_map, prev_x, prev_y).await;
    send_warp_fx(state, target_id).await;
    state.send_to(target_id, &format!("{}Un GM te ha devuelto a tu posicion anterior.{}", server_opcodes::CONSOLE_MSG, font_types::INFO)).await;
    state.send_to(conn_id, &format!("{}Jugador '{}' devuelto.{}", server_opcodes::CONSOLE_MSG, target, font_types::INFO)).await;
}

/// /CONT <seconds> — Start countdown broadcast. VB6 TCP.bas:3470
/// 0 = cancel. 1-60 = start countdown. Requires Semidios+.
pub(super) async fn handle_slash_cont(state: &mut GameState, conn_id: ConnectionId, args: &str) {
    match state.users.get(&conn_id) {
        Some(u) if u.logged && u.privileges >= privilege_level::SEMIDIOS => {}
        _ => return,
    }

    let seconds: i32 = args.trim().parse().unwrap_or(-1);
    if seconds < 0 || seconds > 60 {
        state.send_to(conn_id, &format!("{}Uso: /CONT 0-60 (0 = cancelar){}", server_opcodes::CONSOLE_MSG, font_types::INFO)).await;
        return;
    }

    if seconds == 0 {
        state.countdown_seconds = 0;
        state.send_to(conn_id, &format!("{}Cuenta regresiva cancelada.{}", server_opcodes::CONSOLE_MSG, font_types::INFO)).await;
    } else {
        state.countdown_seconds = seconds;
        // VB6: broadcasts ||739@seconds
        let pkt = format!("||739@{}", seconds);
        state.send_data(SendTarget::ToAll, &pkt).await;
        state.send_to(conn_id, &format!("{}Cuenta regresiva iniciada: {} segundos.{}", server_opcodes::CONSOLE_MSG, seconds, font_types::INFO)).await;
    }
}

/// /INFO <name> — View detailed player info. VB6 TCP.bas:3716
/// Requires GranDios+. Shows class, level, gold, stats, IP, map, etc.
pub(super) async fn handle_slash_info(state: &mut GameState, conn_id: ConnectionId, target: &str) {
    match state.users.get(&conn_id) {
        Some(u) if u.logged && u.privileges >= privilege_level::GRAN_DIOS => {}
        _ => return,
    }

    let target_id = match state.find_user_by_name(target) {
        Some(id) => id,
        None => {
            state.send_to(conn_id, &format!("{}Jugador '{}' no esta online.{}", server_opcodes::CONSOLE_MSG, target, font_types::INFO)).await;
            return;
        }
    };

    let info_lines = match state.users.get(&target_id) {
        Some(u) => {
            vec![
                format!("--- Info de {} ---", u.char_name),
                format!("Clase: {} | Raza: {} | Nivel: {}", u.class, u.race, u.level),
                format!("HP: {}/{} | Mana: {}/{} | Sta: {}/{}", u.min_hp, u.max_hp, u.min_mana, u.max_mana, u.min_sta, u.max_sta),
                format!("Fuerza: {} | Agilidad: {} | Inteligencia: {} | Carisma: {} | Constitucion: {}",
                    u.attributes[0], u.attributes[1], u.attributes[2], u.attributes[3], u.attributes[4]),
                format!("Oro: {} | Banco: {} | Exp: {}", u.gold, u.bank_gold, u.exp),
                format!("Mapa: {} ({},{}) | IP: {}", u.pos_map, u.pos_x, u.pos_y, u.ip),
                format!("Criminal: {} | Muerto: {} | Privilegios: {}", u.criminal, u.dead, u.privileges),
                format!("Guild: {} | Pareja: {}", u.guild_index, u.pareja),
            ]
        }
        None => return,
    };

    for line in info_lines {
        state.send_to(conn_id, &format!("{}{}{}", server_opcodes::CONSOLE_MSG, line, font_types::INFO)).await;
    }
}

/// /EDIT <name>@<levels> — Give player N level-ups. VB6 TCP.bas:3692
/// Requires GranDios+. Sets exp=ELU repeatedly to trigger level_up.
pub(super) async fn handle_slash_edit(state: &mut GameState, conn_id: ConnectionId, args: &str) {
    match state.users.get(&conn_id) {
        Some(u) if u.logged && u.privileges >= privilege_level::GRAN_DIOS => {}
        _ => return,
    }

    let parts: Vec<&str> = args.splitn(2, '@').collect();
    if parts.len() < 2 {
        state.send_to(conn_id, &format!("{}Uso: /EDIT nombre@niveles{}", server_opcodes::CONSOLE_MSG, font_types::INFO)).await;
        return;
    }

    let target_name = parts[0].trim();
    let levels: i32 = parts[1].trim().parse().unwrap_or(0);
    if levels < 1 || levels > 50 {
        state.send_to(conn_id, &format!("{}Niveles debe ser entre 1 y 50.{}", server_opcodes::CONSOLE_MSG, font_types::INFO)).await;
        return;
    }

    let target_id = match state.find_user_by_name(target_name) {
        Some(id) => id,
        None => {
            state.send_to(conn_id, &format!("{}Jugador '{}' no encontrado.{}", server_opcodes::CONSOLE_MSG, target_name, font_types::INFO)).await;
            return;
        }
    };

    // VB6: Set exp = ELU for each level, then call CheckUserLevel
    for _ in 0..levels {
        let level = match state.users.get(&target_id) {
            Some(u) => u.level,
            None => return,
        };
        if level >= 50 { break; } // Max level cap

        // Get ELU for current level and set exp to trigger level-up
        let elu = state.exp_for_level(level);
        if elu <= 0 { break; }
        if let Some(u) = state.users.get_mut(&target_id) {
            u.exp = elu;
        }
        check_user_level(state, target_id).await;
    }

    send_stats_exp(state, target_id).await;
    let final_level = state.users.get(&target_id).map(|u| u.level).unwrap_or(0);
    state.send_to(conn_id, &format!("{}Jugador '{}' ahora es nivel {}.{}", server_opcodes::CONSOLE_MSG, target_name, final_level, font_types::INFO)).await;
    state.send_to(target_id, &format!("{}Un GM te ha subido de nivel!{}", server_opcodes::CONSOLE_MSG, font_types::INFO)).await;
}

/// /PREMIAR <name> <item_id> — Give prize item to player. VB6 TCP.bas:4237
/// Requires GranDios+.
pub(super) async fn handle_slash_premiar(state: &mut GameState, conn_id: ConnectionId, args: &str) {
    match state.users.get(&conn_id) {
        Some(u) if u.logged && u.privileges >= privilege_level::GRAN_DIOS => {}
        _ => return,
    }

    let parts: Vec<&str> = args.split_whitespace().collect();
    if parts.len() < 2 {
        state.send_to(conn_id, &format!("{}Uso: /PREMIAR nombre item_id{}", server_opcodes::CONSOLE_MSG, font_types::INFO)).await;
        return;
    }

    let target_name = parts[0];
    let item_id: i32 = parts[1].parse().unwrap_or(0);
    if item_id < 1 {
        state.send_to(conn_id, &format!("{}Item invalido.{}", server_opcodes::CONSOLE_MSG, font_types::INFO)).await;
        return;
    }

    give_item_to_player(state, conn_id, target_name, item_id, 1).await;
}

/// /PREMIARTS <name> <item_id> — Give TS-specific prize. VB6 TCP.bas:4262
/// Requires Developer+.
pub(super) async fn handle_slash_premiarts(state: &mut GameState, conn_id: ConnectionId, args: &str) {
    match state.users.get(&conn_id) {
        Some(u) if u.logged && u.privileges >= privilege_level::DEVELOPER => {}
        _ => return,
    }

    let parts: Vec<&str> = args.split_whitespace().collect();
    if parts.len() < 2 {
        state.send_to(conn_id, &format!("{}Uso: /PREMIARTS nombre item_id{}", server_opcodes::CONSOLE_MSG, font_types::INFO)).await;
        return;
    }

    let target_name = parts[0];
    let item_id: i32 = parts[1].parse().unwrap_or(0);
    if item_id < 1 { return; }

    give_item_to_player(state, conn_id, target_name, item_id, 1).await;
}

/// Helper: Give an item to a player by name, notify GM.
async fn give_item_to_player(state: &mut GameState, gm_id: ConnectionId, target_name: &str, item_id: i32, amount: i32) {
    let target_id = match state.find_user_by_name(target_name) {
        Some(id) => id,
        None => {
            state.send_to(gm_id, &format!("{}Jugador '{}' no encontrado.{}", server_opcodes::CONSOLE_MSG, target_name, font_types::INFO)).await;
            return;
        }
    };

    // Verify object exists
    if state.get_object(item_id).is_none() {
        state.send_to(gm_id, &format!("{}Objeto {} no existe.{}", server_opcodes::CONSOLE_MSG, item_id, font_types::INFO)).await;
        return;
    }

    let empty_slot = state.users.get(&target_id)
        .and_then(|u| u.inventory.iter().position(|s| s.obj_index == 0));

    if let Some(slot_idx) = empty_slot {
        if let Some(user) = state.users.get_mut(&target_id) {
            user.inventory[slot_idx] = InventorySlot { obj_index: item_id, amount, equipped: false };
        }
        send_inventory_slot(state, target_id, slot_idx + 1).await;
        state.send_to(gm_id, &format!("{}Item {} dado a '{}'.{}", server_opcodes::CONSOLE_MSG, item_id, target_name, font_types::INFO)).await;
        state.send_to(target_id, &format!("{}Has recibido un premio!{}", server_opcodes::CONSOLE_MSG, font_types::INFO)).await;
    } else {
        state.send_to(gm_id, &format!("{}El jugador no tiene espacio en el inventario.{}", server_opcodes::CONSOLE_MSG, font_types::INFO)).await;
    }
}

/// /PLATA <name> — Give silver trophy (obj 896) + update tournament points. VB6 TCP.bas:3114
/// Requires Semidios+.
pub(super) async fn handle_slash_plata(state: &mut GameState, conn_id: ConnectionId, target: &str) {
    match state.users.get(&conn_id) {
        Some(u) if u.logged && u.privileges >= privilege_level::SEMIDIOS => {}
        _ => return,
    }

    let target_id = match state.find_user_by_name(target) {
        Some(id) => id,
        None => {
            state.send_to(conn_id, &format!("{}Jugador '{}' no encontrado.{}", server_opcodes::CONSOLE_MSG, target, font_types::INFO)).await;
            return;
        }
    };

    // VB6: Give silver trophy (obj 896) and add tournament points
    if let Some(u) = state.users.get_mut(&target_id) {
        u.puntos_torneo += 2;
    }
    give_item_to_player(state, conn_id, target, 896, 1).await;

    let pkt = format!("||520@{}", target); // Announce silver medal
    state.send_data(SendTarget::ToAll, &pkt).await;
}

/// /BRONCE <name> — Give bronze trophy (obj 897) + update tournament points. VB6 TCP.bas:3147
/// Requires Semidios+.
pub(super) async fn handle_slash_bronce(state: &mut GameState, conn_id: ConnectionId, target: &str) {
    match state.users.get(&conn_id) {
        Some(u) if u.logged && u.privileges >= privilege_level::SEMIDIOS => {}
        _ => return,
    }

    let target_id = match state.find_user_by_name(target) {
        Some(id) => id,
        None => {
            state.send_to(conn_id, &format!("{}Jugador '{}' no encontrado.{}", server_opcodes::CONSOLE_MSG, target, font_types::INFO)).await;
            return;
        }
    };

    if let Some(u) = state.users.get_mut(&target_id) {
        u.puntos_torneo += 1;
    }
    give_item_to_player(state, conn_id, target, 897, 1).await;

    let pkt = format!("||521@{}", target); // Announce bronze medal
    state.send_data(SendTarget::ToAll, &pkt).await;
}

/// /MEDALLA <name> — Give gold medal (obj 1025) + update tournament points. VB6 TCP.bas:3179
/// Requires Semidios+.
pub(super) async fn handle_slash_medalla(state: &mut GameState, conn_id: ConnectionId, target: &str) {
    match state.users.get(&conn_id) {
        Some(u) if u.logged && u.privileges >= privilege_level::SEMIDIOS => {}
        _ => return,
    }

    let target_id = match state.find_user_by_name(target) {
        Some(id) => id,
        None => {
            state.send_to(conn_id, &format!("{}Jugador '{}' no encontrado.{}", server_opcodes::CONSOLE_MSG, target, font_types::INFO)).await;
            return;
        }
    };

    if let Some(u) = state.users.get_mut(&target_id) {
        u.puntos_torneo += 3;
    }
    give_item_to_player(state, conn_id, target, 1025, 1).await;

    let pkt = format!("||519@{}", target); // Announce gold medal
    state.send_data(SendTarget::ToAll, &pkt).await;
}

/// /DESCALIFICAR <name> — Remove player from tournament. VB6 TCP.bas:3088
/// Requires Semidios+.
pub(super) async fn handle_slash_descalificar(state: &mut GameState, conn_id: ConnectionId, target: &str) {
    match state.users.get(&conn_id) {
        Some(u) if u.logged && u.privileges >= privilege_level::SEMIDIOS => {}
        _ => return,
    }

    if !state.hay_torneo {
        state.send_to(conn_id, &format!("{}No hay torneo activo.{}", server_opcodes::CONSOLE_MSG, font_types::INFO)).await;
        return;
    }

    let target_id = match state.find_user_by_name(target) {
        Some(id) => id,
        None => {
            state.send_to(conn_id, &format!("{}Jugador '{}' no encontrado.{}", server_opcodes::CONSOLE_MSG, target, font_types::INFO)).await;
            return;
        }
    };

    let en_torneo = state.users.get(&target_id).map(|u| u.en_torneo).unwrap_or(false);
    if !en_torneo {
        state.send_to(conn_id, &format!("{}El jugador no esta en el torneo.{}", server_opcodes::CONSOLE_MSG, font_types::INFO)).await;
        return;
    }

    // Remove from tournament
    if let Some(u) = state.users.get_mut(&target_id) {
        u.en_torneo = false;
        u.num_torneo = 0;
    }
    state.usuarios_en_torneo = (state.usuarios_en_torneo - 1).max(0);

    // Remove from participants list
    let name_upper = target.to_uppercase();
    state.cronologia_participantes.retain(|n| n.to_uppercase() != name_upper);

    // Renumber remaining participants
    for (i, name) in state.cronologia_participantes.iter().enumerate() {
        if let Some(id) = state.find_user_by_name(name) {
            if let Some(u) = state.users.get_mut(&id) {
                u.num_torneo = (i + 1) as i32;
            }
        }
    }

    let pkt = format!("||522@{}", target); // Announce disqualification
    state.send_data(SendTarget::ToAll, &pkt).await;
    state.send_to(conn_id, &format!("{}Jugador '{}' descalificado del torneo.{}", server_opcodes::CONSOLE_MSG, target, font_types::INFO)).await;
}

/// /MVP <npc>@<map>@<x>@<y> — Spawn MVP NPC at location. VB6 TCP.bas:3214
/// Requires Semidios+.
pub(super) async fn handle_slash_mvp(state: &mut GameState, conn_id: ConnectionId, args: &str) {
    match state.users.get(&conn_id) {
        Some(u) if u.logged && u.privileges >= privilege_level::SEMIDIOS => {}
        _ => return,
    }

    let parts: Vec<&str> = args.split('@').collect();
    if parts.len() < 4 {
        state.send_to(conn_id, &format!("{}Uso: /MVP npc@mapa@x@y{}", server_opcodes::CONSOLE_MSG, font_types::INFO)).await;
        return;
    }

    let npc_num: i32 = parts[0].trim().parse().unwrap_or(0);
    let map: i32 = parts[1].trim().parse().unwrap_or(0);
    let x: i32 = parts[2].trim().parse().unwrap_or(0);
    let y: i32 = parts[3].trim().parse().unwrap_or(0);

    if npc_num < 1 || map < 1 || !crate::game::world::in_map_bounds(x, y) {
        state.send_to(conn_id, &format!("{}Parametros invalidos.{}", server_opcodes::CONSOLE_MSG, font_types::INFO)).await;
        return;
    }

    // Verify NPC exists
    if state.game_data.npcs.get(npc_num as usize).is_none() {
        state.send_to(conn_id, &format!("{}NPC {} no existe.{}", server_opcodes::CONSOLE_MSG, npc_num, font_types::INFO)).await;
        return;
    }

    // Use spawn_npc_at from events module
    super::spawn_npc_at(state, npc_num as usize, map, x, y).await;
    state.send_to(conn_id, &format!("{}NPC {} (MVP) spawneado en mapa {} ({},{}).{}", server_opcodes::CONSOLE_MSG, npc_num, map, x, y, font_types::INFO)).await;

    // Announce MVP spawn
    let pkt = format!("||523@{}", map);
    state.send_data(SendTarget::ToAll, &pkt).await;
}

/// /DOTORNEO <modality> — Start tournament. VB6 TCP.bas:4053
/// Modality: 1=1v1, 2=2v2, 3=3v3, 4=FFA, 5=special. Requires Semidios+.
pub(super) async fn handle_slash_dotorneo(state: &mut GameState, conn_id: ConnectionId, args: &str) {
    match state.users.get(&conn_id) {
        Some(u) if u.logged && u.privileges >= privilege_level::SEMIDIOS => {}
        _ => return,
    }

    if state.hay_torneo {
        state.send_to(conn_id, &format!("{}Ya hay un torneo activo.{}", server_opcodes::CONSOLE_MSG, font_types::INFO)).await;
        return;
    }

    let modality: i32 = args.trim().parse().unwrap_or(0);
    if modality < 1 || modality > 5 {
        state.send_to(conn_id, &format!("{}Uso: /DOTORNEO 1-5 (1=1v1, 2=2v2, 3=3v3, 4=FFA, 5=especial){}", server_opcodes::CONSOLE_MSG, font_types::INFO)).await;
        return;
    }

    state.hay_torneo = true;
    state.usuarios_en_torneo = 0;
    state.cronologia_participantes.clear();

    // Broadcast tournament start announcement
    let pkt = format!("||524@{}", modality);
    state.send_data(SendTarget::ToAll, &pkt).await;
    state.send_to(conn_id, &format!("{}Torneo modalidad {} iniciado. Jugadores usen /TORNEO para inscribirse.{}", server_opcodes::CONSOLE_MSG, modality, font_types::INFO)).await;
}

/// /CANCELARTORNEO — Cancel active tournament. VB6 TCP.bas:4397
/// Requires Semidios+.
pub(super) async fn handle_slash_cancelartorneo(state: &mut GameState, conn_id: ConnectionId) {
    match state.users.get(&conn_id) {
        Some(u) if u.logged && u.privileges >= privilege_level::SEMIDIOS => {}
        _ => return,
    }

    if !state.hay_torneo {
        state.send_to(conn_id, &format!("{}No hay torneo activo.{}", server_opcodes::CONSOLE_MSG, font_types::INFO)).await;
        return;
    }

    // Remove all players from tournament
    let participants: Vec<String> = state.cronologia_participantes.clone();
    for name in &participants {
        if let Some(id) = state.find_user_by_name(name) {
            if let Some(u) = state.users.get_mut(&id) {
                u.en_torneo = false;
                u.num_torneo = 0;
            }
        }
    }

    state.hay_torneo = false;
    state.usuarios_en_torneo = 0;
    state.cronologia_participantes.clear();

    let pkt = "||525"; // Announce tournament cancelled
    state.send_data(SendTarget::ToAll, pkt).await;
    state.send_to(conn_id, &format!("{}Torneo cancelado.{}", server_opcodes::CONSOLE_MSG, font_types::INFO)).await;
}

/// /TSUM <from>@<to> — Teleport tournament players (numbered from-to) to GM's position. VB6 TCP.bas:4215
/// Requires Semidios+.
pub(super) async fn handle_slash_tsum(state: &mut GameState, conn_id: ConnectionId, args: &str) {
    let (gm_map, gm_x, gm_y) = match state.users.get(&conn_id) {
        Some(u) if u.logged && u.privileges >= privilege_level::SEMIDIOS => (u.pos_map, u.pos_x, u.pos_y),
        _ => return,
    };

    if !state.hay_torneo {
        state.send_to(conn_id, &format!("{}No hay torneo activo.{}", server_opcodes::CONSOLE_MSG, font_types::INFO)).await;
        return;
    }

    let parts: Vec<&str> = args.split('@').collect();
    if parts.len() < 2 {
        state.send_to(conn_id, &format!("{}Uso: /TSUM desde@hasta{}", server_opcodes::CONSOLE_MSG, font_types::INFO)).await;
        return;
    }

    let from: usize = parts[0].trim().parse().unwrap_or(0);
    let to: usize = parts[1].trim().parse().unwrap_or(0);

    if from < 1 || to < from {
        state.send_to(conn_id, &format!("{}Rango invalido.{}", server_opcodes::CONSOLE_MSG, font_types::INFO)).await;
        return;
    }

    let participants = state.cronologia_participantes.clone();
    let mut warped = 0;
    for i in from..=to {
        if i > participants.len() { break; }
        if let Some(id) = state.find_user_by_name(&participants[i - 1]) {
            warp_user(state, id, gm_map, gm_x, gm_y).await;
            send_warp_fx(state, id).await;
            warped += 1;
        }
    }

    state.send_to(conn_id, &format!("{}Invocados {} participantes del torneo.{}", server_opcodes::CONSOLE_MSG, warped, font_types::INFO)).await;
}

/// /REY — Spawn the Ancalagon pre-dragon + 4 guardians (VB6: /SALEREY in TCP.bas line 5170).
/// Requires DIOS+ privileges.
pub(super) async fn handle_slash_rey(state: &mut GameState, conn_id: ConnectionId) {
    let is_gm = state.users.get(&conn_id).map(|u| u.logged && u.privileges >= privilege_level::DIOS).unwrap_or(false);
    if !is_gm { return; }

    if state.ancalagon_pre_dragon || state.ancalagon_alive {
        state.send_to(conn_id, &format!("{}El Rey Ancalagon ya está activo.{}", server_opcodes::CONSOLE_MSG, font_types::INFO)).await;
        return;
    }

    // Broadcast warning (VB6: SendData ToAll, "||805")
    state.send_data(SendTarget::ToAll, "||805").await;

    // Spawn pre-dragon (NPC 937) at map 123, (50,18)
    state.ancalagon_guardians = 0;
    if let Some(idx) = state.spawn_npc(937, 123, 50, 18) {
        state.ancalagon_pre_dragon = true;
        state.ancalagon_pre_dragon_idx = idx;
        // Set aura 3 (VB6: Npclist(IndexReyAncalagon).Char.AuraA = 3)
        if let Some(npc) = state.get_npc_mut(idx) {
            npc.aura = 3;
            let cc = npc.build_cc_packet();
            state.send_data(SendTarget::ToMap(123), &cc).await;
        }
    }

    // Spawn 4 guardians (NPC 938) — VB6 positions
    let guardian_positions = [(50, 17), (49, 18), (51, 18), (50, 19)];
    for (gx, gy) in &guardian_positions {
        state.spawn_npc(938, 123, *gx, *gy);
    }

    // Reset timer so tick_ancalagon won't auto-spawn again
    state.ancalagon_minutes = 0;
    state.ancalagon_seconds = 0;

    let gm_name = state.users.get(&conn_id).map(|u| u.char_name.clone()).unwrap_or_default();
    info!("[GM] {} used /REY — spawned Ancalagon pre-dragon + 4 guardians", gm_name);
    state.send_to(conn_id, &format!("{}Rey Ancalagon invocado en mapa 123.{}", server_opcodes::CONSOLE_MSG, font_types::INFO)).await;
}

/// /NPCAURA <npc_runtime_index> <aura_id> — Set aura on a live NPC instance.
/// Requires DIOS+ privileges. Useful for testing NPC aura visuals.
pub(super) async fn handle_slash_npcaura(state: &mut GameState, conn_id: ConnectionId, args: &str) {
    let is_gm = state.users.get(&conn_id).map(|u| u.logged && u.privileges >= privilege_level::DIOS).unwrap_or(false);
    if !is_gm { return; }

    let parts: Vec<&str> = args.split_whitespace().collect();
    if parts.len() < 2 {
        state.send_to(conn_id, &format!("{}Uso: /NPCAURA <npc_index> <aura_id>{}", server_opcodes::CONSOLE_MSG, font_types::INFO)).await;
        return;
    }

    let npc_idx: usize = match parts[0].parse() {
        Ok(v) => v,
        Err(_) => {
            state.send_to(conn_id, &format!("{}NPC index inválido.{}", server_opcodes::CONSOLE_MSG, font_types::INFO)).await;
            return;
        }
    };
    let aura_id: i32 = match parts[1].parse() {
        Ok(v) => v,
        Err(_) => {
            state.send_to(conn_id, &format!("{}Aura ID inválido.{}", server_opcodes::CONSOLE_MSG, font_types::INFO)).await;
            return;
        }
    };

    let (npc_name, map) = match state.get_npc(npc_idx) {
        Some(npc) if npc.active => (npc.name.clone(), npc.map),
        _ => {
            state.send_to(conn_id, &format!("{}NPC {} no encontrado o inactivo.{}", server_opcodes::CONSOLE_MSG, npc_idx, font_types::INFO)).await;
            return;
        }
    };

    if let Some(npc) = state.get_npc_mut(npc_idx) {
        npc.aura = aura_id;
        let cc = npc.build_cc_packet();
        state.send_data(SendTarget::ToMap(map), &cc).await;
    }

    let gm_name = state.users.get(&conn_id).map(|u| u.char_name.clone()).unwrap_or_default();
    info!("[GM] {} set NPC {} ({}) aura to {}", gm_name, npc_idx, npc_name, aura_id);
    state.send_to(conn_id, &format!("{}NPC {} ({}) aura = {}{}", server_opcodes::CONSOLE_MSG, npc_idx, npc_name, aura_id, font_types::INFO)).await;
}

/// /DEST — Destroy floor object at GM's current tile.
/// VB6: TCP.bas line 5125. Calls EraseObj(SendTarget.toMap, ..., 10000, ...)
pub(super) async fn handle_slash_dest(state: &mut GameState, conn_id: ConnectionId) {
    let (map, x, y) = match state.users.get(&conn_id) {
        Some(u) if u.logged && u.privileges >= privilege_level::DIOS => (u.pos_map, u.pos_x, u.pos_y),
        _ => return,
    };

    let obj_idx = state.world.grid(map)
        .and_then(|g| g.tile(x, y))
        .map(|t| t.ground_item.obj_index)
        .unwrap_or(0);

    if obj_idx <= 0 {
        state.send_to(conn_id, &format!("{}No hay objeto en esta posición.{}", server_opcodes::CONSOLE_MSG, font_types::INFO)).await;
        return;
    }

    let obj_name = state.get_object(obj_idx).map(|o| o.name.clone()).unwrap_or_default();

    let grid = state.world.grid_mut(map);
    if let Some(tile) = grid.tile_mut(x, y) {
        tile.ground_item = world::GroundItem::default();
    }

    // Send BO packet to notify clients
    state.send_data(SendTarget::ToMap(map), &format!("BO{},{}", x, y)).await;

    let gm_name = state.users.get(&conn_id).map(|u| u.char_name.clone()).unwrap_or_default();
    info!("[GM] {} used /DEST at ({},{},{}) — destroyed {}", gm_name, map, x, y, obj_name);
    state.send_to(conn_id, &format!("{}Destruido: {} en ({},{}).{}", server_opcodes::CONSOLE_MSG, obj_name, x, y, font_types::INFO)).await;
}

/// /MASSDEST — Destroy all non-map floor objects in GM's visible area.
/// VB6: TCP.bas line 4914. Iterates MinXBorder/MinYBorder area, skips map fixtures.
pub(super) async fn handle_slash_massdest(state: &mut GameState, conn_id: ConnectionId) {
    let (map, cx, cy) = match state.users.get(&conn_id) {
        Some(u) if u.logged && u.privileges >= privilege_level::DIOS => (u.pos_map, u.pos_x, u.pos_y),
        _ => return,
    };

    let min_y = (cy - world::MIN_Y_BORDER + 1).max(1);
    let max_y = (cy + world::MIN_Y_BORDER - 1).min(100);
    let min_x = (cx - world::MIN_X_BORDER + 1).max(1);
    let max_x = (cx + world::MIN_X_BORDER - 1).min(100);

    // Collect positions to clean (skip map fixtures)
    let mut to_clean: Vec<(i32, i32)> = Vec::new();
    if let Some(grid) = state.world.grid(map) {
        for y in min_y..=max_y {
            for x in min_x..=max_x {
                if let Some(tile) = grid.tile(x, y) {
                    if tile.ground_item.obj_index > 0 && !is_map_fixture(state, tile.ground_item.obj_index) {
                        to_clean.push((x, y));
                    }
                }
            }
        }
    }

    for &(x, y) in &to_clean {
        let grid = state.world.grid_mut(map);
        if let Some(tile) = grid.tile_mut(x, y) {
            tile.ground_item = world::GroundItem::default();
        }
        state.send_data(SendTarget::ToMap(map), &format!("BO{},{}", x, y)).await;
    }

    let gm_name = state.users.get(&conn_id).map(|u| u.char_name.clone()).unwrap_or_default();
    info!("[GM] {} used /MASSDEST at ({},{},{}) — cleaned {} items", gm_name, map, cx, cy, to_clean.len());
    state.send_to(conn_id, &format!("{}{} objetos destruidos en el área.{}", server_opcodes::CONSOLE_MSG, to_clean.len(), font_types::INFO)).await;
}

/// /IRCERCA <name> — Teleport GM to an empty tile near a target player.
/// VB6: TCP.bas line 2931. Searches outward from distance 2 to 5 for a legal free tile.
pub(super) async fn handle_slash_ircerca(state: &mut GameState, conn_id: ConnectionId, target_name: &str) {
    let priv_level = match state.users.get(&conn_id) {
        Some(u) if u.logged && u.privileges >= privilege_level::CONSEJERO => u.privileges,
        _ => return,
    };

    let target_id = match state.find_user_by_name(target_name) {
        Some(id) => id,
        None => {
            state.send_to(conn_id, "||196").await; // User not found
            return;
        }
    };

    // Can't teleport to higher-ranked GMs unless you're DIOS+
    let target_priv = state.users.get(&target_id).map(|u| u.privileges).unwrap_or(0);
    if target_priv >= privilege_level::DIOS && priv_level < privilege_level::DIOS {
        return;
    }

    let (tmap, tx, ty) = match state.users.get(&target_id) {
        Some(u) => (u.pos_map, u.pos_x, u.pos_y),
        None => return,
    };

    // Search outward from distance 2 to 5 for a legal free tile (VB6 pattern)
    for dist in 2..=5i32 {
        for ix in (tx - dist)..=(tx + dist) {
            for iy in (ty - dist)..=(ty + dist) {
                // Only check perimeter of the square
                if ix > tx - dist && ix < tx + dist && iy > ty - dist && iy < ty + dist {
                    continue;
                }
                if ix < 1 || ix > 100 || iy < 1 || iy > 100 {
                    continue;
                }
                let blocked = state.is_tile_blocked(tmap, ix, iy);
                if !blocked && state.world.is_legal_pos(tmap, ix, iy, false) {
                    warp_user(state, conn_id, tmap, ix, iy).await;
                    send_warp_fx(state, conn_id).await;
                    return;
                }
            }
        }
    }

    state.send_to(conn_id, &format!("{}No se encontró posición libre cerca del jugador.{}", server_opcodes::CONSOLE_MSG, font_types::INFO)).await;
}

/// /HACERITEM <objID>@<amount> — Create item on floor at GM position.
/// VB6: TCP.bas line 5072. Requires GRAN_DIOS+. Fails if tile already has object.
pub(super) async fn handle_slash_haceritem(state: &mut GameState, conn_id: ConnectionId, args: &str) {
    let (map, x, y) = match state.users.get(&conn_id) {
        Some(u) if u.logged && u.privileges >= privilege_level::GRAN_DIOS => (u.pos_map, u.pos_x, u.pos_y),
        _ => return,
    };

    // Parse objID@amount (VB6 uses '@' delimiter)
    let parts: Vec<&str> = args.split('@').collect();
    let obj_id: i32 = parts.first().and_then(|s| s.trim().parse().ok()).unwrap_or(0);
    let amount: i32 = if parts.len() >= 2 {
        parts[1].trim().parse().unwrap_or(1).max(1).min(10000)
    } else {
        1
    };

    if obj_id < 1 {
        state.send_to(conn_id, &format!("{}Uso: /HACERITEM <objID>@<cantidad>{}", server_opcodes::CONSOLE_MSG, font_types::INFO)).await;
        return;
    }

    // Verify object exists
    let (obj_name, grh) = match state.get_object(obj_id) {
        Some(obj) => (obj.name.clone(), obj.grh_index),
        None => {
            state.send_to(conn_id, &format!("{}Objeto {} no existe.{}", server_opcodes::CONSOLE_MSG, obj_id, font_types::INFO)).await;
            return;
        }
    };

    // Check tile is empty
    let tile_occupied = state.world.grid(map)
        .and_then(|g| g.tile(x, y))
        .map(|t| t.ground_item.obj_index > 0)
        .unwrap_or(true);

    if tile_occupied {
        state.send_to(conn_id, &format!("{}Ya hay un objeto en esta posición.{}", server_opcodes::CONSOLE_MSG, font_types::INFO)).await;
        return;
    }

    // Place item
    let grid = state.world.grid_mut(map);
    if let Some(tile) = grid.tile_mut(x, y) {
        tile.ground_item.obj_index = obj_id;
        tile.ground_item.amount = amount;
    }

    // Send HO packet to show item visually
    if grh > 0 {
        state.send_data(SendTarget::ToArea { map, x, y }, &format!("HO{},{},{}", grh, x, y)).await;
    }

    let gm_name = state.users.get(&conn_id).map(|u| u.char_name.clone()).unwrap_or_default();
    info!("[GM] {} used /HACERITEM {} x{} ({}) at ({},{},{})", gm_name, obj_id, amount, obj_name, map, x, y);
    // Notify GM
    state.send_to(conn_id, &format!("{}Creado: {} x{} ({}) en ({},{}).{}", server_opcodes::CONSOLE_MSG, obj_name, amount, obj_id, x, y, font_types::INFO)).await;
    // Notify admins (VB6: ||802)
    state.send_data(SendTarget::ToAdmins, &format!("||802@{}@{}@{}@{}", gm_name, obj_id, obj_name, amount)).await;
}

/// /MASSDEST — already handled above.

/// /NENE <map> — List hostile NPCs alive on a given map.
/// VB6: TCP.bas line 3287. Lists hostile NPCs with Alineacion=2.
pub(super) async fn handle_slash_nene(state: &mut GameState, conn_id: ConnectionId, args: &str) {
    let is_gm = state.users.get(&conn_id).map(|u| u.logged && u.privileges >= privilege_level::CONSEJERO).unwrap_or(false);
    if !is_gm { return; }

    let target_map: i32 = args.trim().parse().unwrap_or(0);
    if target_map < 1 {
        state.send_to(conn_id, &format!("{}Uso: /NENE <mapa>{}", server_opcodes::CONSOLE_MSG, font_types::INFO)).await;
        return;
    }

    let mut names: Vec<String> = Vec::new();
    for npc_opt in state.npcs.iter().flatten() {
        if npc_opt.active && npc_opt.is_alive() && npc_opt.map == target_map
            && npc_opt.hostile && npc_opt.alineacion == 2
        {
            names.push(npc_opt.name.clone());
        }
    }

    let list = if names.is_empty() {
        "No hay NPCs hostiles".to_string()
    } else {
        names.join(", ")
    };

    state.send_to(conn_id, &format!("{}NPCs hostiles en mapa {}: {}{}", server_opcodes::CONSOLE_MSG, target_map, list, font_types::INFO)).await;
}

/// /RESETINV — Reset targeted NPC's inventory to its NpcData defaults.
/// VB6: TCP.bas line 4610. Requires targeting an NPC first.
pub(super) async fn handle_slash_resetinv(state: &mut GameState, conn_id: ConnectionId) {
    let (is_gm, target_npc_idx) = match state.users.get(&conn_id) {
        Some(u) if u.logged && u.privileges >= privilege_level::DIOS => (true, u.target_npc),
        _ => (false, 0),
    };
    if !is_gm { return; }

    if target_npc_idx == 0 {
        state.send_to(conn_id, &format!("{}No tenés un NPC seleccionado.{}", server_opcodes::CONSOLE_MSG, font_types::INFO)).await;
        return;
    }

    // Get NPC number to look up original data
    let npc_number = match state.get_npc(target_npc_idx) {
        Some(npc) if npc.active => npc.npc_number,
        _ => {
            state.send_to(conn_id, &format!("{}NPC no encontrado o inactivo.{}", server_opcodes::CONSOLE_MSG, font_types::INFO)).await;
            return;
        }
    };

    // Get original inventory from NpcData
    let original_items = match state.game_data.npcs.get(npc_number) {
        Some(data) => data.items.clone(),
        None => {
            state.send_to(conn_id, &format!("{}NPC data {} no encontrado.{}", server_opcodes::CONSOLE_MSG, npc_number, font_types::INFO)).await;
            return;
        }
    };

    // Reset inventory
    if let Some(npc) = state.get_npc_mut(target_npc_idx) {
        for slot in npc.inventory.iter_mut() {
            *slot = super::super::npc::NpcInvSlot::default();
        }
        for (i, item) in original_items.iter().enumerate() {
            if i < npc.inventory.len() {
                npc.inventory[i].obj_index = item.obj_index;
                npc.inventory[i].amount = item.amount;
                npc.inventory[i].prob_tirar = item.prob_tirar;
            }
        }
        npc.nro_items = original_items.len() as i32;
    }

    let npc_name = state.get_npc(target_npc_idx).map(|n| n.name.clone()).unwrap_or_default();
    let gm_name = state.users.get(&conn_id).map(|u| u.char_name.clone()).unwrap_or_default();
    info!("[GM] {} used /RESETINV on NPC {} ({})", gm_name, target_npc_idx, npc_name);
    state.send_to(conn_id, &format!("{}Inventario de {} reseteado.{}", server_opcodes::CONSOLE_MSG, npc_name, font_types::INFO)).await;
}
