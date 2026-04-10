//! Event system handlers — Pretoriano, meditation, /REGRESAR, pathfinding.

use super::common::*;
use super::world;
use crate::game::types::{GameState, SendTarget, privilege_level};
use crate::net::ConnectionId;
use crate::protocol::{binary_packets, font_index};
use tracing::info;

// Functions from parent module (mod.rs)
use super::{move_npc, warp_user};

/// /REGRESAR — Return to home city (die and respawn at home).
pub(super) async fn handle_slash_regresar(state: &mut GameState, conn_id: ConnectionId) {
    let user_data = match state.users.get(&conn_id) {
        Some(u) if u.logged => (u.dead, u.privileges, u.level, u.pos_map),
        _ => return,
    };
    let (dead, privileges, level, _cur_map) = user_data;

    if privileges >= privilege_level::CONSEJERO {
        return;
    }
    if level < 10 {
        state.send_console(
            conn_id,
            "Debes ser nivel 10 o superior para usar /REGRESAR.",
            font_index::INFO,
        );
        return;
    }

    if !dead {
        if let Some(user) = state.users.get_mut(&conn_id) {
            user.min_hp = 0;
            user.dead = true;
            user.poisoned = false;
            user.paralyzed = false;
            user.immobilized = false;
            user.meditating = false;
            user.min_sta = 0;
        }
        let dead_pkt = binary_packets::write_dead();
        state.send_bytes(conn_id, &dead_pkt);
        send_stats_hp(state, conn_id).await;
        if let Some(user) = state.users.get(&conn_id) {
            let cc = user.build_cc_binary();
            let (m, ux, uy) = (user.pos_map, user.pos_x, user.pos_y);
            state.send_data_bytes(
                SendTarget::ToArea {
                    map: m,
                    x: ux,
                    y: uy,
                },
                &cc,
            );
        }
    }

    // Default: Ullathorpe
    let (dest_map, dest_x, dest_y) = (1, 58, 45);

    warp_user(state, conn_id, dest_map, dest_x, dest_y).await;
    state.send_console(conn_id, "Has regresado a tu hogar.", font_index::INFO);
}

/// /SALIR — Disconnect/logout.
pub(super) async fn handle_slash_salir(state: &mut GameState, conn_id: ConnectionId) {
    close_connection(state, conn_id).await;
}

/// /DESCANSAR — Toggle resting near a campfire.
/// VB6: HandleRest (Protocol.bas). Requires FOGATA (obj 63) in view area.
/// Resting doubles HP/STA regen rate in tick_player_passive.
pub(super) async fn handle_slash_descansar(state: &mut GameState, conn_id: ConnectionId) {
    let (dead, resting, pos_map, pos_x, pos_y) = match state.users.get(&conn_id) {
        Some(u) if u.logged => (u.dead, u.resting, u.pos_map, u.pos_x, u.pos_y),
        _ => return,
    };

    if dead {
        state.send_console(conn_id, "¡¡Estás muerto!!", font_index::INFO);
        return;
    }

    const FOGATA: i16 = 63;
    let has_fogata = hay_obj_area(state, pos_map, pos_x, pos_y, FOGATA);

    if has_fogata {
        state.send_bytes(conn_id, &binary_packets::write_rest_ok());

        if !resting {
            state.send_console(
                conn_id,
                "Te acomodás junto a la fogata y comienzas a descansar.",
                font_index::INFO,
            );
        } else {
            state.send_console(conn_id, "Te levantás.", font_index::INFO);
        }

        if let Some(user) = state.users.get_mut(&conn_id) {
            user.resting = !resting;
        }
    } else {
        if resting {
            // Was resting but moved away — stop resting
            state.send_bytes(conn_id, &binary_packets::write_rest_ok());
            state.send_console(conn_id, "Te levantás.", font_index::INFO);
            if let Some(user) = state.users.get_mut(&conn_id) {
                user.resting = false;
            }
        } else {
            state.send_console(
                conn_id,
                "No hay ninguna fogata junto a la cual descansar.",
                font_index::INFO,
            );
        }
    }
}

/// VB6: HayOBJarea — check if an object with given index exists within the view area.
/// Scans roughly ±8 tiles X and ±6 tiles Y around position (VB6 MinXBorder/MinYBorder).
fn hay_obj_area(state: &GameState, map: i32, cx: i32, cy: i32, obj_index: i16) -> bool {
    const RANGE_X: i32 = 8;
    const RANGE_Y: i32 = 6;

    let game_map = match state
        .game_data
        .maps
        .get(map as usize)
        .and_then(|m| m.as_ref())
    {
        Some(m) => m,
        None => return false,
    };

    for y in (cy - RANGE_Y)..=(cy + RANGE_Y) {
        for x in (cx - RANGE_X)..=(cx + RANGE_X) {
            if let Some(tile) = game_map.tiles.get((x - 1) as usize, (y - 1) as usize) {
                if tile.obj.obj_index == obj_index {
                    return true;
                }
            }
        }
    }
    false
}

// =====================================================================
// IP Security (SecurityIp.bas) — rate limiting + max connections per IP
// =====================================================================

/// Check if a new connection from this IP should be accepted.
/// Returns false if rate-limited or max connections exceeded.
pub fn ip_security_accept(state: &mut GameState, ip: &str) -> bool {
    let now = std::time::Instant::now();

    if let Some(last) = state.ip_last_connect.get(ip) {
        let elapsed = now.duration_since(*last).as_millis() as u64;
        if elapsed < state.ip_min_interval_ms {
            return false;
        }
    }
    state.ip_last_connect.insert(ip.to_string(), now);

    let count = state.ip_connection_count.entry(ip.to_string()).or_insert(0);
    if *count >= state.ip_max_connections {
        return false;
    }
    *count += 1;

    true
}

// =====================================================================
// NPC Pathfinding (PathFinding.bas) — BFS on map grid
// =====================================================================

/// Maximum pathfinding steps (matches VB6 MAXPASOS = 30).
const PF_MAX_STEPS: usize = 30;

/// BFS pathfinding — find path from (sx,sy) to (tx,ty) on the given map.
/// Returns a Vec of (x,y) positions from start to target (excluding start).
/// Uses 4-directional adjacency on a 100x100 grid.
pub(super) fn pathfind_bfs(
    state: &GameState,
    map: i32,
    sx: i32,
    sy: i32,
    tx: i32,
    ty: i32,
) -> Vec<(i32, i32)> {
    if sx == tx && sy == ty {
        return Vec::new();
    }
    let (grid_w, grid_h) = state.grid_dimensions(map);
    if sx < 1 || sx > grid_w || sy < 1 || sy > grid_h {
        return Vec::new();
    }
    if tx < 1 || tx > grid_w || ty < 1 || ty > grid_h {
        return Vec::new();
    }

    let mut visited = vec![vec![0u8; (grid_w + 2) as usize]; (grid_h + 2) as usize];
    let mut queue: std::collections::VecDeque<(i32, i32, usize)> =
        std::collections::VecDeque::new();

    visited[sy as usize][sx as usize] = 5;
    queue.push_back((sx, sy, 0));

    let dirs: [(i32, i32, u8); 4] = [(0, -1, 1), (1, 0, 2), (0, 1, 3), (-1, 0, 4)];

    let mut found = false;

    while let Some((cx, cy, depth)) = queue.pop_front() {
        if depth >= PF_MAX_STEPS {
            continue;
        }
        if cx == tx && cy == ty {
            found = true;
            break;
        }

        for &(dx, dy, dir_code) in &dirs {
            let nx = cx + dx;
            let ny = cy + dy;
            if nx < 1 || nx > grid_w || ny < 1 || ny > grid_h {
                continue;
            }
            if visited[ny as usize][nx as usize] != 0 {
                continue;
            }

            if state.is_tile_blocked(map, nx, ny) {
                continue;
            }
            let has_npc = state
                .world
                .grid(map)
                .and_then(|g| g.tile(nx, ny))
                .map(|t| t.npc_index > 0)
                .unwrap_or(false);
            if has_npc && !(nx == tx && ny == ty) {
                continue;
            }

            visited[ny as usize][nx as usize] = dir_code;
            queue.push_back((nx, ny, depth + 1));
        }
    }

    if !found {
        return Vec::new();
    }

    let mut path = Vec::new();
    let mut cx = tx;
    let mut cy = ty;

    while !(cx == sx && cy == sy) {
        path.push((cx, cy));
        let dir = visited[cy as usize][cx as usize];
        match dir {
            1 => cy += 1,
            2 => cx -= 1,
            3 => cy -= 1,
            4 => cx += 1,
            _ => break,
        }
        if path.len() > PF_MAX_STEPS {
            break;
        }
    }

    path.reverse();
    path
}

/// Compute heading from step direction for NPC movement.
pub(super) fn heading_from_step(cx: i32, cy: i32, nx: i32, ny: i32) -> i32 {
    let dx = nx - cx;
    let dy = ny - cy;
    if dy < 0 {
        world::HEADING_NORTH
    } else if dy > 0 {
        world::HEADING_SOUTH
    } else if dx > 0 {
        world::HEADING_EAST
    } else if dx < 0 {
        world::HEADING_WEST
    } else {
        world::HEADING_SOUTH
    }
}

/// Execute one pathfinding step for an NPC — moves to next step in path.
/// Returns (moved, optional ghost push data).
pub(super) fn npc_pathfind_step(
    state: &mut GameState,
    npc_idx: usize,
) -> (bool, Option<super::npcs::GhostPush>) {
    let (step, path_len, _map, x, y) = match state.get_npc(npc_idx) {
        Some(n) => (n.pf_step, n.pf_path.len(), n.map, n.x, n.y),
        None => return (false, None),
    };

    if step >= path_len {
        return (false, None);
    }

    let (nx, ny) = match state.get_npc(npc_idx) {
        Some(n) => n.pf_path[step],
        None => return (false, None),
    };

    let heading = heading_from_step(x, y, nx, ny);

    let (moved, ghost) = move_npc(state, npc_idx, heading);
    if moved {
        if let Some(n) = state.get_npc_mut(npc_idx) {
            n.pf_step += 1;
        }
        (true, ghost)
    } else {
        if let Some(n) = state.get_npc_mut(npc_idx) {
            n.pf_path.clear();
            n.pf_step = 0;
        }
        (false, ghost)
    }
}

// =====================================================================
// Praetorian System (praetorians.bas) — faction fortress NPCs
// =====================================================================

/// Praetorian NPC numbers (900-904 in VB6)
const PRETORIANO_MAGO: usize = 900;
const PRETORIANO_CLERIGO: usize = 901;
const PRETORIANO_GUERRERO: usize = 902;
const PRETORIANO_CAZADOR: usize = 903;
const PRETORIANO_REY: usize = 904;

const MAX_PRETORIANOS_CLAN: usize = 8;

/// Create a praetorian clan at a given location.
pub(super) async fn crear_clan_pretoriano(
    state: &mut GameState,
    map: i32,
    x: i32,
    y: i32,
    faccion: i32,
) {
    limpiar_clan_pretoriano(state).await;

    state.pretoriano_faccion = faccion;
    state.pretoriano_activo = true;
    state.pretoriano_alcoba = 0;

    let positions = [
        (x - 2, y - 2),
        (x + 2, y - 2),
        (x - 2, y + 2),
        (x + 2, y + 2),
        (x - 1, y),
        (x + 1, y),
        (x, y - 1),
        (x, y + 1),
    ];

    let npc_types = [
        PRETORIANO_GUERRERO,
        PRETORIANO_GUERRERO,
        PRETORIANO_CAZADOR,
        PRETORIANO_CAZADOR,
        PRETORIANO_MAGO,
        PRETORIANO_MAGO,
        PRETORIANO_CLERIGO,
        PRETORIANO_REY,
    ];

    for i in 0..MAX_PRETORIANOS_CLAN {
        let (px, py) = positions[i];
        let (pret_w, pret_h) = state.grid_dimensions(map);
        if px >= 1 && px <= pret_w && py >= 1 && py <= pret_h {
            if let Some(npc_idx) = state.spawn_npc(npc_types[i], map, px, py) {
                state.pretoriano_clan.push(npc_idx);

                if let Some(npc) = state.get_npc(npc_idx) {
                    let cc_pkt = npc.build_cc_binary();
                    state.send_data_bytes(SendTarget::ToArea { map, x: px, y: py }, &cc_pkt);
                }
            }
        }
    }

    info!(
        "[PRET] Created praetorian clan on map {} at ({},{}) faction {}",
        map, x, y, faccion
    );
}

/// Remove all praetorian NPCs.
pub(super) async fn limpiar_clan_pretoriano(state: &mut GameState) {
    let clan_indices: Vec<usize> = state.pretoriano_clan.drain(..).collect();
    for npc_idx in clan_indices {
        if let Some(npc) = state.get_npc(npc_idx) {
            let bp_pkt = binary_packets::write_character_remove(npc.char_index.0 as i16);
            let (map, x, y) = (npc.map, npc.x, npc.y);
            state.send_data_bytes(SendTarget::ToArea { map, x, y }, &bp_pkt);
        }
        state.kill_npc(npc_idx);
    }
    state.pretoriano_activo = false;
    state.pretoriano_alcoba = 0;
}

/// Check if an NPC number is a praetorian type.
pub(super) fn es_pretoriano(npc_number: usize) -> bool {
    npc_number >= 900 && npc_number <= 904
}

/// Handle praetorian death — check if clan is wiped.
pub fn pretoriano_check_death(state: &mut GameState, npc_idx: usize) {
    if !state.pretoriano_activo {
        return;
    }

    state.pretoriano_clan.retain(|&idx| idx != npc_idx);

    let rey_alive = state.pretoriano_clan.iter().any(|&idx| {
        state
            .get_npc(idx)
            .filter(|n| n.is_alive() && n.npc_number == PRETORIANO_REY)
            .is_some()
    });

    if !rey_alive {
        state.pretoriano_alcoba += 1;
        if state.pretoriano_alcoba >= 4 {
            state.pretoriano_activo = false;
            info!(
                "[PRET] Fortress conquered! Faction {}",
                state.pretoriano_faccion
            );
        } else {
            info!(
                "[PRET] Alcoba {} cleared, advancing",
                state.pretoriano_alcoba
            );
        }
    }
}

// =====================================================================
