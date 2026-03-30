//! NPC movement packets, area visibility updates, NPC-vs-NPC combat.

use crate::net::ConnectionId;
use crate::game::types::{GameState, SendTarget};
use crate::game::npc;
use crate::protocol::binary_packets;
use crate::game::handlers::common::*;
use crate::game::handlers::world;
use crate::game::handlers::{npc_die};

pub(crate) async fn send_npc_move(state: &mut GameState, npc_idx: usize) {
    let data = match state.get_npc(npc_idx) {
        Some(n) => (n.char_index.0, n.x, n.y, n.map, n.heading, n.area_min_x, n.area_min_y),
        None => return,
    };
    let (char_idx, x, y, map, heading, _area_min_x, _area_min_y) = data;

    // VB6 movement packet: *charindex,x,y → binary CharacterMove
    let pkt = binary_packets::write_character_move(char_idx as i16, x as i16, y as i16);

    // Send to all players in viewport range (17x13 tiles) — replaces the old
    // 27x27 tile scan (729 tiles) with the standard area broadcast (221 tiles).
    state.send_data_bytes(SendTarget::ToArea { map, x, y }, &pkt);

    // Check if NPC crossed a 9x9 area boundary — if so, send CC to newly visible players
    check_update_needed_npc(state, npc_idx, heading).await;
}

/// Send packets for a ghost push (position update to ghost + movement broadcast to area).
pub(crate) async fn send_ghost_push(state: &mut GameState, push: crate::game::handlers::npcs::GhostPush) {
    let pu_pkt = binary_packets::write_pos_update(push.new_x as i16, push.new_y as i16);
    state.send_bytes(push.ghost_conn, &pu_pkt);
    let move_pkt = binary_packets::write_character_move(push.ghost_char_index as i16, push.new_x as i16, push.new_y as i16);
    state.send_data_bytes(
        SendTarget::ToAreaButIndex { conn_id: push.ghost_conn, map: push.map, x: push.new_x, y: push.new_y },
        &move_pkt,
    );
}

/// CheckUpdateNeededNpc — VB6 ModAreas.bas line 320-406.
/// When an NPC crosses a 9x9 zone boundary, send CC packets to players in the new strip.
pub(crate) async fn check_update_needed_npc(state: &mut GameState, npc_idx: usize, heading: i32) {
    let (x, y, map, old_area_id, old_min_x, old_min_y) = match state.get_npc(npc_idx) {
        Some(n) => (n.x, n.y, n.map, n.area_id, n.area_min_x, n.area_min_y),
        None => return,
    };

    let new_area_id = area_id(x, y);

    // If still in the same 9x9 zone, nothing to do
    if new_area_id == old_area_id && old_area_id != 0 {
        return;
    }

    // Calculate the new visibility strip based on heading (same logic as user)
    let (min_x, max_x, min_y, max_y, new_min_x, new_min_y) = if old_area_id == 0 {
        // First time (spawn): full 27x27 area
        let nmin_y = (y / 9 - 1) * 9;
        let nmin_x = (x / 9 - 1) * 9;
        (nmin_x, nmin_x + 26, nmin_y, nmin_y + 26, nmin_x, nmin_y)
    } else if heading == world::HEADING_NORTH {
        let nmin_x = old_min_x;
        let nmin_y = old_min_y - 9;
        (nmin_x, nmin_x + 26, nmin_y, old_min_y - 1, nmin_x, nmin_y)
    } else if heading == world::HEADING_SOUTH {
        let nmin_x = old_min_x;
        let nmin_y = old_min_y + 27;
        let new_area_min_y = old_min_y + 9;
        (nmin_x, nmin_x + 26, nmin_y, nmin_y + 8, nmin_x, new_area_min_y)
    } else if heading == world::HEADING_WEST {
        let nmin_x = old_min_x - 9;
        let nmin_y = old_min_y;
        (nmin_x, old_min_x - 1, nmin_y, nmin_y + 26, nmin_x, nmin_y)
    } else if heading == world::HEADING_EAST {
        let nmin_x = old_min_x + 27;
        let nmin_y = old_min_y;
        let new_area_min_x = old_min_x + 9;
        (nmin_x, nmin_x + 8, nmin_y, nmin_y + 26, new_area_min_x, nmin_y)
    } else {
        // Unknown heading — do full area
        let nmin_y = (y / 9 - 1) * 9;
        let nmin_x = (x / 9 - 1) * 9;
        (nmin_x, nmin_x + 26, nmin_y, nmin_y + 26, nmin_x, nmin_y)
    };

    // Clamp to map bounds
    let (grid_w, grid_h) = state.grid_dimensions(map);
    let min_x = min_x.max(1);
    let min_y = min_y.max(1);
    let max_x = max_x.min(grid_w);
    let max_y = max_y.min(grid_h);

    // Update NPC's area tracking
    if let Some(npc) = state.get_npc_mut(npc_idx) {
        npc.area_id = new_area_id;
        npc.area_min_x = new_min_x;
        npc.area_min_y = new_min_y;
    }

    // Build NPC CC packet
    let npc_cc = match state.get_npc(npc_idx) {
        Some(npc) if npc.active => npc.build_cc_binary(),
        _ => return,
    };

    // Find users in the new strip and send CC to them
    let mut users_in_strip: Vec<ConnectionId> = Vec::new();
    if let Some(grid) = state.world.grid(map) {
        for sx in min_x..=max_x {
            for sy in min_y..=max_y {
                if let Some(tile) = grid.tile(sx, sy) {
                    if let Some(conn) = tile.user_conn {
                        users_in_strip.push(conn);
                    }
                }
            }
        }
    }

    for conn_id in users_in_strip {
        if state.users.get(&conn_id).map(|u| u.logged).unwrap_or(false) {
            state.send_bytes(conn_id, &npc_cc);
        }
    }
}

/// Find an adjacent player (1 tile away in any cardinal direction).
/// Returns (ConnectionId, heading_toward_target) if a valid player is adjacent.
pub(super) fn find_adjacent_player(state: &GameState, map: i32, x: i32, y: i32) -> Option<(ConnectionId, i32)> {
    for heading in 1..=4 {
        let (dx, dy) = world::heading_to_offset(heading);
        let tx = x + dx;
        let ty = y + dy;
        if let Some(grid) = state.world.grid(map) {
            if let Some(tile) = grid.tile(tx, ty) {
                if let Some(conn) = tile.user_conn {
                    // Check if player is alive, logged, and NOT a GM (VB6: Privilegios = User)
                    // VB6: skip NoPuedeSerAtacado, Ignorado, EnConsulta
                    if let Some(user) = state.users.get(&conn) {
                        if user.logged && !user.dead && user.privileges == 0 && !user.admin_invisible
                            && !user.no_puede_ser_atacado && !user.ignorado && !user.en_consulta
                            && user.warp_immunity_ticks <= 0 {
                            return Some((conn, heading));
                        }
                    }
                }
            }
        }
    }
    None
}

/// Find the nearest player within NPC vision range.
pub(super) fn find_nearest_player(state: &GameState, map: i32, x: i32, y: i32) -> Option<ConnectionId> {
    let half_x = npc::NPC_VISION_X / 2;
    let half_y = npc::NPC_VISION_Y / 2;
    let (gw, gh) = state.grid_dimensions(map);
    let min_x = (x - half_x).max(1);
    let max_x = (x + half_x).min(gw);
    let min_y = (y - half_y).max(1);
    let max_y = (y + half_y).min(gh);

    let mut best: Option<(ConnectionId, i32)> = None;

    if let Some(grid) = state.world.grid(map) {
        for cy in min_y..=max_y {
            for cx in min_x..=max_x {
                if let Some(tile) = grid.tile(cx, cy) {
                    if let Some(conn) = tile.user_conn {
                        if let Some(user) = state.users.get(&conn) {
                            if user.logged && !user.dead && user.privileges == 0
                                && !user.no_puede_ser_atacado && !user.ignorado && !user.en_consulta
                                && user.warp_immunity_ticks <= 0 {
                                let dist = (x - cx).abs() + (y - cy).abs();
                                if best.is_none() || dist < best.unwrap().1 {
                                    best = Some((conn, dist));
                                }
                            }
                        }
                    }
                }
            }
        }
    }

    best.map(|(conn, _)| conn)
}

/// Calculate heading to move from (x,y) towards (tx,ty).
pub(super) fn chase_heading(x: i32, y: i32, tx: i32, ty: i32) -> i32 {
    let dx = tx - x;
    let dy = ty - y;

    // Prioritize larger difference
    if dx.abs() >= dy.abs() {
        if dx > 0 { world::HEADING_EAST } else { world::HEADING_WEST }
    } else {
        if dy > 0 { world::HEADING_SOUTH } else { world::HEADING_NORTH }
    }
}

// =====================================================================
// NPC AI helper functions
// =====================================================================

/// Find nearest criminal player within NPC vision range (for royal guards).
pub(super) fn find_nearest_criminal(state: &GameState, map: i32, x: i32, y: i32) -> Option<ConnectionId> {
    let half_x = npc::NPC_VISION_X / 2;
    let half_y = npc::NPC_VISION_Y / 2;
    let (gw, gh) = state.grid_dimensions(map);
    let min_x = (x - half_x).max(1);
    let max_x = (x + half_x).min(gw);
    let min_y = (y - half_y).max(1);
    let max_y = (y + half_y).min(gh);

    let mut best: Option<(ConnectionId, i32)> = None;

    if let Some(grid) = state.world.grid(map) {
        for cy in min_y..=max_y {
            for cx in min_x..=max_x {
                if let Some(tile) = grid.tile(cx, cy) {
                    if let Some(conn) = tile.user_conn {
                        if let Some(user) = state.users.get(&conn) {
                            if user.logged && !user.dead && user.criminal && user.privileges == 0
                                && !user.no_puede_ser_atacado && !user.ignorado && !user.en_consulta {
                                let dist = (x - cx).abs() + (y - cy).abs();
                                if best.is_none() || dist < best.unwrap().1 {
                                    best = Some((conn, dist));
                                }
                            }
                        }
                    }
                }
            }
        }
    }

    best.map(|(conn, _)| conn)
}

/// Find nearest citizen (non-criminal) player within NPC vision range (for chaos guards).
pub(super) fn find_nearest_citizen(state: &GameState, map: i32, x: i32, y: i32) -> Option<ConnectionId> {
    let half_x = npc::NPC_VISION_X / 2;
    let half_y = npc::NPC_VISION_Y / 2;
    let (gw, gh) = state.grid_dimensions(map);
    let min_x = (x - half_x).max(1);
    let max_x = (x + half_x).min(gw);
    let min_y = (y - half_y).max(1);
    let max_y = (y + half_y).min(gh);

    let mut best: Option<(ConnectionId, i32)> = None;

    if let Some(grid) = state.world.grid(map) {
        for cy in min_y..=max_y {
            for cx in min_x..=max_x {
                if let Some(tile) = grid.tile(cx, cy) {
                    if let Some(conn) = tile.user_conn {
                        if let Some(user) = state.users.get(&conn) {
                            if user.logged && !user.dead && !user.criminal && user.privileges == 0
                                && !user.no_puede_ser_atacado && !user.ignorado && !user.en_consulta {
                                let dist = (x - cx).abs() + (y - cy).abs();
                                if best.is_none() || dist < best.unwrap().1 {
                                    best = Some((conn, dist));
                                }
                            }
                        }
                    }
                }
            }
        }
    }

    best.map(|(conn, _)| conn)
}

/// Find a player by name within 15 tiles (for defense AI — NPC looking for its attacker).
pub(super) fn find_player_by_name(state: &GameState, map: i32, x: i32, y: i32, name: &str) -> Option<ConnectionId> {
    if name.is_empty() { return None; }
    let range = 15;
    let (gw, gh) = state.grid_dimensions(map);
    let min_x = (x - range).max(1);
    let max_x = (x + range).min(gw);
    let min_y = (y - range).max(1);
    let max_y = (y + range).min(gh);

    if let Some(grid) = state.world.grid(map) {
        for cy in min_y..=max_y {
            for cx in min_x..=max_x {
                if let Some(tile) = grid.tile(cx, cy) {
                    if let Some(conn) = tile.user_conn {
                        if let Some(user) = state.users.get(&conn) {
                            if user.logged && !user.dead && user.char_name == name {
                                return Some(conn);
                            }
                        }
                    }
                }
            }
        }
    }
    None
}

/// Find the target NPC within vision range for NpcAtacaNpc AI (type 9).
/// Scans vision area for the NPC's assigned target_npc. Returns its index and position if found.
pub(super) fn find_target_npc_in_vision(state: &GameState, map: i32, x: i32, y: i32, target_npc_idx: usize) -> Option<(usize, i32, i32)> {
    let half_x = npc::NPC_VISION_X / 2;
    let half_y = npc::NPC_VISION_Y / 2;
    let (gw, gh) = state.grid_dimensions(map);
    let min_x = (x - half_x).max(1);
    let max_x = (x + half_x).min(gw);
    let min_y = (y - half_y).max(1);
    let max_y = (y + half_y).min(gh);

    if let Some(grid) = state.world.grid(map) {
        for cy in min_y..=max_y {
            for cx in min_x..=max_x {
                if let Some(tile) = grid.tile(cx, cy) {
                    if tile.npc_index > 0 && tile.npc_index as usize == target_npc_idx {
                        if state.get_npc(target_npc_idx).map(|n| n.is_alive()).unwrap_or(false) {
                            return Some((target_npc_idx, cx, cy));
                        }
                    }
                }
            }
        }
    }
    None
}

/// Find any hostile NPC (non-pet) within vision range, for pretoriano warriors.
/// Returns the closest NPC index and its position.
pub(super) fn find_nearest_hostile_npc(state: &GameState, map: i32, x: i32, y: i32, self_idx: usize) -> Option<(usize, i32, i32)> {
    let half_x = npc::NPC_VISION_X / 2;
    let half_y = npc::NPC_VISION_Y / 2;
    let (gw, gh) = state.grid_dimensions(map);
    let min_x = (x - half_x).max(1);
    let max_x = (x + half_x).min(gw);
    let min_y = (y - half_y).max(1);
    let max_y = (y + half_y).min(gh);

    let mut best: Option<(usize, i32, i32, i32)> = None; // (idx, x, y, dist)

    if let Some(grid) = state.world.grid(map) {
        for cy in min_y..=max_y {
            for cx in min_x..=max_x {
                if let Some(tile) = grid.tile(cx, cy) {
                    if tile.npc_index > 0 {
                        let npc_idx = tile.npc_index as usize;
                        if npc_idx == self_idx { continue; }
                        if let Some(npc) = state.get_npc(npc_idx) {
                            // Target: pet NPCs (owned by a player) that are alive and not paralyzed
                            if npc.is_alive() && npc.maestro_user.is_some() && !npc.paralyzed {
                                let dist = (x - cx).abs() + (y - cy).abs();
                                if best.is_none() || dist < best.unwrap().3 {
                                    best = Some((npc_idx, cx, cy, dist));
                                }
                            }
                        }
                    }
                }
            }
        }
    }

    best.map(|(idx, bx, by, _)| (idx, bx, by))
}

/// Check if an NPC is a pretoriano (AI type 20-24).
pub(super) fn is_pretoriano(movement: i32) -> bool {
    movement >= npc::AI_SACERDOTE_PRETORIANO && movement <= npc::AI_REY_PRETORIANO
}

/// Find the nearest pretoriano ally within vision range (for healer/king support).
/// Returns (npc_idx, x, y) of the ally with lowest HP ratio, or None.
pub(super) fn find_wounded_pretoriano_ally(state: &GameState, map: i32, x: i32, y: i32, self_idx: usize) -> Option<(usize, i32, i32)> {
    let half_x = npc::NPC_VISION_X / 2;
    let half_y = npc::NPC_VISION_Y / 2;
    let (gw, gh) = state.grid_dimensions(map);
    let min_x = (x - half_x).max(1);
    let max_x = (x + half_x).min(gw);
    let min_y = (y - half_y).max(1);
    let max_y = (y + half_y).min(gh);

    let mut best: Option<(usize, i32, i32, f32)> = None; // (idx, x, y, hp_ratio)

    if let Some(grid) = state.world.grid(map) {
        for cy in min_y..=max_y {
            for cx in min_x..=max_x {
                if let Some(tile) = grid.tile(cx, cy) {
                    if tile.npc_index > 0 {
                        let npc_idx = tile.npc_index as usize;
                        if npc_idx == self_idx { continue; }
                        if let Some(npc) = state.get_npc(npc_idx) {
                            if npc.is_alive() && is_pretoriano(npc.movement) && npc.max_hp > 0
                                && npc.min_hp < npc.max_hp
                            {
                                let ratio = npc.min_hp as f32 / npc.max_hp as f32;
                                if best.is_none() || ratio < best.unwrap().3 {
                                    best = Some((npc_idx, cx, cy, ratio));
                                }
                            }
                        }
                    }
                }
            }
        }
    }

    best.map(|(idx, bx, by, _)| (idx, bx, by))
}

/// Find a paralyzed pretoriano ally within vision range (for king/sacerdote to unparalyze).
pub(super) fn find_paralyzed_pretoriano_ally(state: &GameState, map: i32, x: i32, y: i32, self_idx: usize) -> Option<(usize, i32, i32)> {
    let half_x = npc::NPC_VISION_X / 2;
    let half_y = npc::NPC_VISION_Y / 2;
    let (gw, gh) = state.grid_dimensions(map);
    let min_x = (x - half_x).max(1);
    let max_x = (x + half_x).min(gw);
    let min_y = (y - half_y).max(1);
    let max_y = (y + half_y).min(gh);

    if let Some(grid) = state.world.grid(map) {
        for cy in min_y..=max_y {
            for cx in min_x..=max_x {
                if let Some(tile) = grid.tile(cx, cy) {
                    if tile.npc_index > 0 {
                        let npc_idx = tile.npc_index as usize;
                        if npc_idx == self_idx { continue; }
                        if let Some(npc) = state.get_npc(npc_idx) {
                            if npc.is_alive() && is_pretoriano(npc.movement) && npc.paralyzed {
                                return Some((npc_idx, cx, cy));
                            }
                        }
                    }
                }
            }
        }
    }
    None
}

/// Check if any pretoriano allies exist nearby (for king AI — determines if king should fight directly).
pub(super) fn has_pretoriano_allies(state: &GameState, map: i32, x: i32, y: i32, self_idx: usize) -> bool {
    let half_x = npc::NPC_VISION_X / 2;
    let half_y = npc::NPC_VISION_Y / 2;
    let (gw, gh) = state.grid_dimensions(map);
    let min_x = (x - half_x).max(1);
    let max_x = (x + half_x).min(gw);
    let min_y = (y - half_y).max(1);
    let max_y = (y + half_y).min(gh);

    if let Some(grid) = state.world.grid(map) {
        for cy in min_y..=max_y {
            for cx in min_x..=max_x {
                if let Some(tile) = grid.tile(cx, cy) {
                    if tile.npc_index > 0 {
                        let npc_idx = tile.npc_index as usize;
                        if npc_idx == self_idx { continue; }
                        if let Some(npc) = state.get_npc(npc_idx) {
                            if npc.is_alive() && is_pretoriano(npc.movement) {
                                return true;
                            }
                        }
                    }
                }
            }
        }
    }
    false
}

/// Restore NPC to its original movement AI (VB6: RestoreOldMovement).
/// Called when defense-mode NPC loses its target.
pub(super) fn restore_old_movement(state: &mut GameState, npc_idx: usize) {
    if let Some(npc) = state.get_npc_mut(npc_idx) {
        npc.movement = npc.old_movement;
        npc.hostile = npc.old_hostile;
        npc.attacked_by.clear();
        npc.target = None;
    }
}

/// NPC vs NPC combat (VB6: NpcAtacaNpc — used by pets attacking target NPCs).
pub(crate) async fn npc_attack_npc(state: &mut GameState, attacker_idx: usize, target_idx: usize) {
    let attacker_data = match state.get_npc(attacker_idx) {
        Some(n) if n.is_alive() => (n.min_hit, n.max_hit, n.char_index, n.map, n.x, n.y, n.maestro_user, n.poder_ataque),
        _ => return,
    };
    let (a_min_hit, a_max_hit, _a_char, a_map, a_x, a_y, a_master, a_poder_ataque) = attacker_data;

    let target_data = match state.get_npc(target_idx) {
        Some(n) if n.is_alive() => (n.poder_evasion,),
        _ => return,
    };
    let (t_poder_evasion,) = target_data;

    // VB6: NpcImpactoNpc — hit/miss roll
    let prob_exito = ((50.0 + (a_poder_ataque - t_poder_evasion) as f64 * 0.4) as i32).clamp(10, 90);
    if rand_range(1, 100) > prob_exito {
        // Miss — play swing sound
        let snd_pkt = binary_packets::write_play_wave(2, a_x as i16, a_y as i16);
        state.send_data_bytes(SendTarget::ToArea { map: a_map, x: a_x, y: a_y }, &snd_pkt);
        return;
    }

    // VB6: NpcDañoNpc — damage (no armor for NPC vs NPC)
    let damage = rand_range(a_min_hit.max(1), a_max_hit.max(1));

    let target_dead = {
        match state.get_npc_mut(target_idx) {
            Some(target) => {
                target.min_hp -= damage;
                target.min_hp <= 0
            }
            None => return,
        }
    };

    // Impact sound
    let snd_pkt = binary_packets::write_play_wave(10, a_x as i16, a_y as i16);
    state.send_data_bytes(SendTarget::ToArea { map: a_map, x: a_x, y: a_y }, &snd_pkt);

    if target_dead {
        // Target NPC dies — use pet owner as killer if available
        if let Some(master_conn) = a_master {
            let (give_exp, give_gld_min, give_gld_max) = state.get_npc(target_idx)
                .map(|n| (n.give_exp, n.give_gld_min, n.give_gld_max))
                .unwrap_or((0, 0, 0));
            npc_die(state, target_idx, master_conn, give_exp, give_gld_min, give_gld_max).await;
        } else {
            // No master — just kill it
            let (map, x, y, char_index) = match state.get_npc(target_idx) {
                Some(n) => (n.map, n.x, n.y, n.char_index),
                None => return,
            };
            let bp_pkt = binary_packets::write_character_remove(char_index.0 as i16);
            state.send_data_bytes(SendTarget::ToArea { map, x, y }, &bp_pkt);
            state.kill_npc(target_idx);
        }
    }

    // Check if attacker (pet) died from target's counter (not implemented in VB6 NvN)
    // Pets can also die — check attacker_idx NPC
    let pet_dead = state.get_npc(attacker_idx).map(|n| n.min_hp <= 0).unwrap_or(false);
    if pet_dead {
        let (p_map, p_x, p_y, p_ci) = match state.get_npc(attacker_idx) {
            Some(n) => (n.map, n.x, n.y, n.char_index),
            None => return,
        };
        let bp = binary_packets::write_character_remove(p_ci.0 as i16);
        state.send_data_bytes(SendTarget::ToArea { map: p_map, x: p_x, y: p_y }, &bp);
        // Remove pet from owner
        if let Some(owner_conn) = a_master {
            remove_pet_from_owner(state, owner_conn, attacker_idx);
        }
        state.kill_npc(attacker_idx);
    }
}

/// Remove a pet NPC from its owner's mascotas array.
pub(crate) fn remove_pet_from_owner(state: &mut GameState, owner_conn: ConnectionId, npc_idx: usize) {
    // Get NPC number before removing (for elemental flag cleanup)
    let npc_number = state.get_npc(npc_idx).map(|n| n.npc_number as i32).unwrap_or(0);

    if let Some(user) = state.users.get_mut(&owner_conn) {
        for i in 0..3 {
            if user.mascotas_index[i] == npc_idx {
                user.mascotas_index[i] = 0;
                user.mascotas_type[i] = 0;
                user.nro_mascotas = (user.nro_mascotas - 1).max(0);
                break;
            }
        }
        // VB6: Clear elemental flags when elemental dies/is removed
        use crate::game::npc::{ELEMENTAL_AGUA, ELEMENTAL_FUEGO, ELEMENTAL_TIERRA};
        match npc_number {
            ELEMENTAL_AGUA => user.ele_de_agua = false,
            ELEMENTAL_FUEGO => user.ele_de_fuego = false,
            ELEMENTAL_TIERRA => user.ele_de_tierra = false,
            _ => {}
        }
    }
}


