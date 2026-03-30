//! GM teleport commands: /TELEP, /GO, /IRA, /SUM, /IRCERCA, /HOME.

use crate::net::ConnectionId;
use crate::game::types::{GameState, privilege_level};
use crate::protocol::{font_index};
use super::{warp_user, warp_user_exact, send_warp_fx};

/// After a GM warp, check if the destination tile has an exit and follow it.
async fn follow_tile_exit_after_warp(state: &mut GameState, conn_id: ConnectionId) {
    if let Some(u) = state.users.get(&conn_id) {
        let (m, fx, fy) = (u.pos_map, u.pos_x, u.pos_y);
        if let Some((exit_map, exit_x, exit_y)) = state.get_tile_exit(m, fx, fy) {
            warp_user(state, conn_id, exit_map as i32, exit_x as i32, exit_y as i32).await;
            send_warp_fx(state, conn_id).await;
        }
    }
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
        state.send_console(conn_id, "Uso: /TELEP nombre mapa x y", font_index::INFO);
        return;
    }

    let name = parts[0];
    let map: i32 = parts[1].parse().unwrap_or(0);
    let x: i32 = parts[2].parse().unwrap_or(0);
    let y: i32 = parts[3].parse().unwrap_or(0);

    if map < 1 || state.world.grid(map).map(|g| !crate::game::world::in_map_bounds_grid(g, x, y)).unwrap_or(true) {
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
                state.send_msg_id(conn_id, 196, ""); // User not found
                return;
            }
        }
    };

    warp_user_exact(state, target_id, map, x, y).await;
    send_warp_fx(state, target_id).await;
    follow_tile_exit_after_warp(state, target_id).await;

    // Notify
    if target_id == conn_id {
        state.send_msg_id(conn_id, 773, ""); // TEXTO773: Has sido transportado
    } else {
        let gm_name = state.users.get(&conn_id).map(|u| u.char_name.clone()).unwrap_or_default();
        state.send_msg_id(target_id, 778, &gm_name.to_string()); // TEXTO778: %1 te ha transportado
        state.send_msg_id(conn_id, 651, &gm_name.to_string()); // TEXTO651: %1 transportado
    }
}

/// /TELEPLOC — Teleport self to last left-click target location.
pub(super) async fn handle_slash_teleploc(state: &mut GameState, conn_id: ConnectionId) {
    // This uses the target coordinates set by left-click (LC packet).
    // VB6-PARITY: VB6 stores the last left-click tile in Flags.TargetX/TargetY on the LC handler
    // (server reads the MapNum, X, Y from the packet). Clicking a tile sets the target and the GM
    // then uses /TELEPLOC to warp to that exact tile. Currently target_x/target_y default to 0
    // until a player has clicked a tile, falling back to a console message below.
    let _priv_level = match state.users.get(&conn_id) {
        Some(u) if u.logged && u.privileges >= privilege_level::CONSEJERO => u.privileges,
        _ => return,
    };

    // Get last click target from user state (VB6: flags.TargetX/Y)
    let (map, tx, ty) = match state.users.get(&conn_id) {
        Some(u) => (if u.target_map > 0 { u.target_map } else { u.pos_map }, u.target_x, u.target_y),
        None => return,
    };

    if tx == 0 && ty == 0 {
        state.send_console(conn_id, "Primero haz click en el destino.", font_index::INFO);
        return;
    }

    let bounds_ok = state.world.grid(map)
        .map(|g| crate::game::world::in_map_bounds_grid(g, tx, ty))
        .unwrap_or(false);
    if !bounds_ok {
        state.send_console(conn_id, "Coordenadas fuera de los limites del mapa.", font_index::INFO);
        return;
    }

    warp_user_exact(state, conn_id, map, tx, ty).await;
    send_warp_fx(state, conn_id).await;
    follow_tile_exit_after_warp(state, conn_id).await;
    state.send_msg_id(conn_id, 773, ""); // TEXTO773: Has sido transportado
}

/// /GO map — Teleport self to map at position 50,50 (VB6 behavior, requires SEMIDIOS+).
pub(super) async fn handle_slash_go(state: &mut GameState, conn_id: ConnectionId, args: &str) {
    match state.users.get(&conn_id) {
        Some(u) if u.logged && u.privileges >= privilege_level::SEMIDIOS => {}
        _ => {
            state.send_console(conn_id, "No tenes permisos para usar este comando.", font_index::INFO);
            return;
        }
    };

    let parts: Vec<&str> = args.split_whitespace().collect();
    if parts.is_empty() {
        state.send_console(conn_id, "Uso: /GO mapa", font_index::INFO);
        return;
    }

    let map: i32 = parts[0].parse().unwrap_or(0);
    // VB6 always warps to 50,50 (TCP.bas line 4686)
    let x: i32 = if parts.len() >= 3 { parts[1].parse().unwrap_or(50) } else { 50 };
    let y: i32 = if parts.len() >= 3 { parts[2].parse().unwrap_or(50) } else { 50 };

    if map < 1 || state.world.grid(map).map(|g| !crate::game::world::in_map_bounds_grid(g, x, y)).unwrap_or(true) {
        state.send_console(conn_id, "Mapa o coordenadas invalidas.", font_index::INFO);
        return;
    }

    // Check map exists
    let map_idx = map as usize;
    if map_idx >= state.game_data.maps.len() || state.game_data.maps.get(map_idx).and_then(|m| m.as_ref()).is_none() {
        state.send_console(conn_id, &format!("Mapa {} no existe.", map), font_index::INFO);
        return;
    }

    warp_user_exact(state, conn_id, map, x, y).await;
    send_warp_fx(state, conn_id).await;
    follow_tile_exit_after_warp(state, conn_id).await;
    state.send_msg_id(conn_id, 773, ""); // TEXTO773: Has sido transportado
}

/// /IRA name — Teleport to a player's position (requires SEMIDIOS+).
pub(super) async fn handle_slash_ira(state: &mut GameState, conn_id: ConnectionId, target: &str) {
    match state.users.get(&conn_id) {
        Some(u) if u.logged && u.privileges >= privilege_level::SEMIDIOS => {}
        _ => {
            state.send_console(conn_id, "No tenes permisos para usar este comando.", font_index::INFO);
            return;
        }
    }

    let target_id = match state.find_user_by_name(target) {
        Some(id) => id,
        None => {
            state.send_console(conn_id, &format!("Jugador '{}' no encontrado.", target), font_index::INFO);
            return;
        }
    };

    let (map, x, y) = match state.users.get(&target_id) {
        Some(u) if u.logged => (u.pos_map, u.pos_x, u.pos_y),
        _ => {
            state.send_console(conn_id, &format!("Jugador '{}' no encontrado.", target), font_index::INFO);
            return;
        }
    };

    warp_user_exact(state, conn_id, map, x, y).await;
    send_warp_fx(state, conn_id).await;
    follow_tile_exit_after_warp(state, conn_id).await;
    state.send_msg_id(conn_id, 773, ""); // TEXTO773: Has sido transportado
}

/// /SUM name — Summon a player to your position (requires SEMIDIOS+).
pub(super) async fn handle_slash_sum(state: &mut GameState, conn_id: ConnectionId, target: &str) {
    let (my_map, my_x, my_y) = match state.users.get(&conn_id) {
        Some(u) if u.logged && u.privileges >= privilege_level::SEMIDIOS => (u.pos_map, u.pos_x, u.pos_y),
        _ => {
            state.send_console(conn_id, "No tenes permisos para usar este comando.", font_index::INFO);
            return;
        }
    };

    let target_id = match state.find_user_by_name(target) {
        Some(id) => id,
        None => {
            state.send_console(conn_id, &format!("Jugador '{}' no encontrado.", target), font_index::INFO);
            return;
        }
    };

    warp_user(state, target_id, my_map, my_x, my_y).await;
    send_warp_fx(state, target_id).await;
    state.send_console(conn_id, &format!("Invocaste a '{}'.", target), font_index::INFO);
    state.send_console(target_id, "Has sido invocado por un GM.", font_index::INFO);
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
            let (map, x, y) = (1, 58, 45); // Ullathorpe
            let _ = (armada, caos, hogar);

            warp_user(state, tc, map, x, y).await;
            state.send_msg_id(conn_id, 772, "");
            state.send_msg_id(tc, 773, "");
        }
        None => {
            state.send_msg_id(conn_id, 198, "");
        }
    }
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
            state.send_msg_id(conn_id, 196, ""); // User not found
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
    let (ircerca_w, ircerca_h) = state.grid_dimensions(tmap);
    for dist in 2..=5i32 {
        for ix in (tx - dist)..=(tx + dist) {
            for iy in (ty - dist)..=(ty + dist) {
                // Only check perimeter of the square
                if ix > tx - dist && ix < tx + dist && iy > ty - dist && iy < ty + dist {
                    continue;
                }
                if ix < 1 || ix > ircerca_w || iy < 1 || iy > ircerca_h {
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

    state.send_console(conn_id, "No se encontró posición libre cerca del jugador.", font_index::INFO);
}

