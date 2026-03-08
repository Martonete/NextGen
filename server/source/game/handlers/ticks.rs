//! Server tick handlers — NPC AI, respawn, player passive effects,
//! anti-cheat intervals, and world cleanup.
//!
//! These are called from the main event loop at regular intervals.

use tracing::{info, error};
use crate::net::ConnectionId;
use crate::game::types::{GameState, SendTarget, CleanWorldEntry};
use crate::data::npcs::NpcType;
use crate::db::charfile;
use crate::game::npc;
use crate::protocol::{binary_packets, font_index};
use super::common::*;
use super::world;

// Functions from parent module (mod.rs)
use super::{
    npc_attack_user, npc_die, npc_cast_spell, move_npc,
    user_die, revive_user, warp_user, npc_pathfind_step, pathfind_bfs,
};


/// AI tick — called every 100ms from the main loop.
/// VB6: AI_MoveNpc / modIA.bas — full NPC behavior including hostile chase,
/// defense mode, guard chase, self-heal, and global attack timer.
pub async fn tick_npc_ai(state: &mut GameState) {
    // Global CanAttack timer: every 3 ticks (300ms), all NPCs can attack again
    state.npc_can_attack_counter += 1;
    if state.npc_can_attack_counter >= 3 {
        state.npc_can_attack_counter = 0;
        // Reset all NPCs' can_attack flag
        for slot in state.npcs.iter_mut() {
            if let Some(npc) = slot.as_mut() {
                if npc.active {
                    npc.can_attack = true;
                }
            }
        }
    }

    // Update map user counts for skipping empty maps
    update_map_user_counts(state);

    // Collect active NPC indices to process
    let active_npcs: Vec<usize> = state.npcs.iter().enumerate()
        .filter_map(|(i, slot)| {
            slot.as_ref().filter(|n| n.is_alive()).map(|_| i)
        })
        .collect();

    for npc_idx in active_npcs {
        // Skip paralyzed NPCs — they can't move or attack
        let is_paralyzed = state.get_npc(npc_idx).map(|n| n.paralyzed).unwrap_or(false);
        if is_paralyzed { continue; }

        let npc_data = match state.get_npc(npc_idx) {
            Some(n) => (n.movement, n.hostile, n.can_attack, n.map, n.x, n.y, n.target,
                        n.lanza_spells, n.spells.clone(), n.npc_type, n.attacked_by.clone(),
                        n.min_hp, n.max_hp, n.alineacion),
            None => continue,
        };
        let (movement, hostile, can_attack, map, x, y, target,
             lanza_spells, spells, npc_type, attacked_by,
             cur_hp, max_hp, alineacion) = npc_data;

        // Skip NPCs on maps with no users (VB6 optimization)
        let map_users = state.map_user_counts.get(&map).copied().unwrap_or(0);
        if map_users == 0 && movement != npc::AI_FOLLOW_OWNER {
            continue;
        }

        // NPC self-heal: if HP < 50% and has a heal spell, try to cast it on self
        if max_hp > 0 && cur_hp > 0 && cur_hp < max_hp / 2 && lanza_spells > 0 {
            npc_try_self_heal(state, npc_idx, &spells).await;
        }

        match movement {
            npc::AI_STATIC => {
                // No movement, but attack adjacent if hostile
                if hostile && can_attack {
                    if let Some((target_conn, _adj_heading)) = find_adjacent_player(state, map, x, y) {
                        npc_attack_user(state, npc_idx, target_conn).await;
                        if let Some(n) = state.get_npc_mut(npc_idx) {
                            n.can_attack = false;
                        }
                    }
                }
            }

            npc::AI_RANDOM => {
                // Random movement (1/12 chance per tick)
                // Guards with AI_RANDOM also chase their targets
                if hostile && can_attack {
                    if let Some((target_conn, _adj_heading)) = find_adjacent_player(state, map, x, y) {
                        npc_attack_user(state, npc_idx, target_conn).await;
                        if let Some(n) = state.get_npc_mut(npc_idx) {
                            n.can_attack = false;
                        }
                    }
                }
                if rand_range(1, 12) == 3 {
                    let heading = rand_range(1, 4);
                    {
                        let (moved, ghost) = move_npc(state, npc_idx, heading);
                        if let Some(gp) = ghost { send_ghost_push(state, gp).await; }
                        if moved { send_npc_move(state, npc_idx).await; }
                    }
                }
            }

            npc::AI_HOSTILE_CHASE => {
                // VB6 AI_AI_Hostile (modIA.bas)
                // 1) Check 4 adjacent tiles for users/pets → attack
                // 2) Scan vision for nearest player → spell + chase
                // 3) No target → restore_old_movement + random 1/12

                let mut attacked = false;

                // First: check adjacent tiles for attack
                if can_attack {
                    if let Some((target_conn, _adj_heading)) = find_adjacent_player(state, map, x, y) {
                        npc_attack_user(state, npc_idx, target_conn).await;
                        if let Some(n) = state.get_npc_mut(npc_idx) {
                            n.can_attack = false;
                            n.target = Some(target_conn);
                        }
                        attacked = true;
                    } else {
                        // Check for pet NPC at adjacent tiles
                        for heading in 1..=4 {
                            let (dx, dy) = world::heading_to_offset(heading);
                            let tx = x + dx;
                            let ty = y + dy;
                            if let Some(adj_npc_idx) = state.npc_at_tile(map, tx, ty) {
                                let is_pet = state.get_npc(adj_npc_idx)
                                    .map(|n| n.maestro_user.is_some() && n.is_alive())
                                    .unwrap_or(false);
                                if is_pet {
                                    npc_attack_npc(state, npc_idx, adj_npc_idx).await;
                                    if let Some(n) = state.get_npc_mut(npc_idx) {
                                        n.can_attack = false;
                                    }
                                    attacked = true;
                                    break;
                                }
                            }
                        }
                    }
                }

                if !attacked {
                    // Scan vision range for nearest player
                    // Filter out GM targets — if existing target is a GM, discard it
                    let valid_target = target.filter(|t| {
                        state.users.get(t).map(|u| u.privileges == 0).unwrap_or(false)
                    });
                    let chase_target = valid_target.or_else(|| find_nearest_player(state, map, x, y));

                    if let Some(target_conn) = chase_target {
                        if let Some(n) = state.get_npc_mut(npc_idx) {
                            n.target = Some(target_conn);
                        }

                        let target_pos = state.users.get(&target_conn)
                            .filter(|u| u.logged && !u.dead && u.pos_map == map)
                            .map(|u| (u.pos_x, u.pos_y));

                        if let Some((tx, ty)) = target_pos {
                            let dist = (x - tx).abs() + (y - ty).abs();

                            // Cast spell if in range
                            if dist > 1 && dist <= 8 && lanza_spells > 0 && can_attack && !spells.is_empty() {
                                let spell_idx = rand_range(0, spells.len() as i32 - 1) as usize;
                                let spell_id = spells[spell_idx];
                                npc_cast_spell(state, npc_idx, target_conn, spell_id).await;
                                if let Some(n) = state.get_npc_mut(npc_idx) {
                                    n.can_attack = false;
                                }
                            }

                            // Chase target
                            if dist > 1 {
                                let heading = chase_heading(x, y, tx, ty);
                                {
                                    let (moved, ghost) = move_npc(state, npc_idx, heading);
                                    if let Some(gp) = ghost { send_ghost_push(state, gp).await; }
                                    if moved { send_npc_move(state, npc_idx).await; }
                                }
                            }
                        } else {
                            // Target gone (left map or dead)
                            if let Some(n) = state.get_npc_mut(npc_idx) {
                                n.target = None;
                            }
                        }
                    } else {
                        // No target at all — if this was originally a non-hostile NPC
                        // that was switched to hostile chase, restore old movement
                        let was_defense_switch = state.get_npc(npc_idx)
                            .map(|n| n.old_movement != npc::AI_HOSTILE_CHASE && !n.attacked_by.is_empty())
                            .unwrap_or(false);
                        if was_defense_switch {
                            restore_old_movement(state, npc_idx);
                        }
                        // Random walk
                        if rand_range(1, 12) == 3 {
                            let heading = rand_range(1, 4);
                            {
                                let (moved, ghost) = move_npc(state, npc_idx, heading);
                                if let Some(gp) = ghost { send_ghost_push(state, gp).await; }
                                if moved { send_npc_move(state, npc_idx).await; }
                            }
                        }
                    }
                }
            }

            npc::AI_DEFENSE => {
                // VB6 AI_AI_Defense — follow attacker (attacked_by) within 15 tiles
                // Adjacent → melee. Far → spell + chase. Gone → restore_old_movement.
                // Filter out GM attackers — NPCs should not chase GMs
                let attacker_conn = find_player_by_name(state, map, x, y, &attacked_by)
                    .filter(|c| state.users.get(c).map(|u| u.privileges == 0).unwrap_or(false));

                if let Some(target_conn) = attacker_conn {
                    if let Some(n) = state.get_npc_mut(npc_idx) {
                        n.target = Some(target_conn);
                    }

                    let target_pos = state.users.get(&target_conn)
                        .filter(|u| u.logged && !u.dead && u.pos_map == map)
                        .map(|u| (u.pos_x, u.pos_y));

                    if let Some((tx, ty)) = target_pos {
                        let dist = (x - tx).abs() + (y - ty).abs();

                        if dist <= 1 && can_attack {
                            npc_attack_user(state, npc_idx, target_conn).await;
                            if let Some(n) = state.get_npc_mut(npc_idx) {
                                n.can_attack = false;
                            }
                        } else {
                            // Cast spell if possible
                            if dist > 1 && dist <= 8 && lanza_spells > 0 && can_attack && !spells.is_empty() {
                                let spell_idx = rand_range(0, spells.len() as i32 - 1) as usize;
                                let spell_id = spells[spell_idx];
                                npc_cast_spell(state, npc_idx, target_conn, spell_id).await;
                                if let Some(n) = state.get_npc_mut(npc_idx) {
                                    n.can_attack = false;
                                }
                            }

                            // Chase attacker
                            if dist > 1 {
                                let heading = chase_heading(x, y, tx, ty);
                                {
                                    let (moved, ghost) = move_npc(state, npc_idx, heading);
                                    if let Some(gp) = ghost { send_ghost_push(state, gp).await; }
                                    if moved { send_npc_move(state, npc_idx).await; }
                                }
                            }
                        }
                    } else {
                        // Attacker left map — restore
                        restore_old_movement(state, npc_idx);
                    }
                } else {
                    // Attacker not found within 15 tiles — restore old movement
                    restore_old_movement(state, npc_idx);
                    if rand_range(1, 12) == 3 {
                        let heading = rand_range(1, 4);
                        {
                            let (moved, ghost) = move_npc(state, npc_idx, heading);
                            if let Some(gp) = ghost { send_ghost_push(state, gp).await; }
                            if moved { send_npc_move(state, npc_idx).await; }
                        }
                    }
                }
            }

            npc::AI_GUARD => {
                // VB6 AI_AI_Guardia — Royal guards chase criminals, Chaos guards chase citizens
                // Castle guards (map 620/621) don't move
                let is_royal = npc_type == NpcType::RoyalGuard || alineacion == 1;
                let is_castle = map == 620 || map == 621;

                // First check adjacent for attack
                if can_attack {
                    if let Some((target_conn, _adj_heading)) = find_adjacent_player(state, map, x, y) {
                        let should_attack = if is_royal {
                            state.users.get(&target_conn).map(|u| u.criminal).unwrap_or(false)
                        } else {
                            // Chaos guard — attack citizens
                            state.users.get(&target_conn).map(|u| !u.criminal).unwrap_or(false)
                        };
                        if should_attack {
                            npc_attack_user(state, npc_idx, target_conn).await;
                            if let Some(n) = state.get_npc_mut(npc_idx) {
                                n.can_attack = false;
                            }
                        }
                    }
                }

                // Chase behavior (skip for castle guards)
                if !is_castle {
                    let chase_target = if is_royal {
                        find_nearest_criminal(state, map, x, y)
                    } else {
                        find_nearest_citizen(state, map, x, y)
                    };

                    if let Some(target_conn) = chase_target {
                        let target_pos = state.users.get(&target_conn)
                            .filter(|u| u.logged && !u.dead && u.pos_map == map)
                            .map(|u| (u.pos_x, u.pos_y));

                        if let Some((tx, ty)) = target_pos {
                            let dist = (x - tx).abs() + (y - ty).abs();
                            if dist > 1 {
                                let heading = chase_heading(x, y, tx, ty);
                                {
                                    let (moved, ghost) = move_npc(state, npc_idx, heading);
                                    if let Some(gp) = ghost { send_ghost_push(state, gp).await; }
                                    if moved { send_npc_move(state, npc_idx).await; }
                                }
                            }
                        }
                    } else if rand_range(1, 12) == 3 {
                        // No target — random wander
                        let heading = rand_range(1, 4);
                        {
                            let (moved, ghost) = move_npc(state, npc_idx, heading);
                            if let Some(gp) = ghost { send_ghost_push(state, gp).await; }
                            if moved { send_npc_move(state, npc_idx).await; }
                        }
                    }
                }
            }

            npc::AI_PATHFINDING => {
                // Chase nearest player using BFS pathfinding
                let chase_target = target.or_else(|| find_nearest_player(state, map, x, y));

                if let Some(target_conn) = chase_target {
                    if let Some(n) = state.get_npc_mut(npc_idx) {
                        n.target = Some(target_conn);
                    }

                    let target_pos = state.users.get(&target_conn)
                        .filter(|u| u.logged && !u.dead && u.pos_map == map)
                        .map(|u| (u.pos_x, u.pos_y));

                    if let Some((tx, ty)) = target_pos {
                        let dist = (x - tx).abs() + (y - ty).abs();
                        if dist <= 1 && can_attack {
                            npc_attack_user(state, npc_idx, target_conn).await;
                            if let Some(n) = state.get_npc_mut(npc_idx) {
                                n.can_attack = false;
                            }
                        } else if dist > 1 && dist <= 8 && lanza_spells > 0 && can_attack && !spells.is_empty() {
                            let spell_idx = rand_range(0, spells.len() as i32 - 1) as usize;
                            let spell_id = spells[spell_idx];
                            npc_cast_spell(state, npc_idx, target_conn, spell_id).await;
                            if let Some(n) = state.get_npc_mut(npc_idx) {
                                n.can_attack = false;
                            }
                        } else if dist > 1 {
                            // Use BFS pathfinding
                            let has_path = state.get_npc(npc_idx)
                                .map(|n| !n.pf_path.is_empty() && n.pf_step < n.pf_path.len())
                                .unwrap_or(false);

                            if has_path {
                                {
                                    let (pf_moved, pf_ghost) = npc_pathfind_step(state, npc_idx);
                                    if let Some(gp) = pf_ghost { send_ghost_push(state, gp).await; }
                                    if pf_moved { send_npc_move(state, npc_idx).await; }
                                }
                            } else {
                                let path = pathfind_bfs(state, map, x, y, tx, ty);
                                if !path.is_empty() {
                                    if let Some(n) = state.get_npc_mut(npc_idx) {
                                        n.pf_path = path;
                                        n.pf_step = 0;
                                    }
                                    {
                                        let (pf_moved, pf_ghost) = npc_pathfind_step(state, npc_idx);
                                        if let Some(gp) = pf_ghost { send_ghost_push(state, gp).await; }
                                        if pf_moved { send_npc_move(state, npc_idx).await; }
                                    }
                                } else {
                                    let heading = chase_heading(x, y, tx, ty);
                                    {
                                        let (moved, ghost) = move_npc(state, npc_idx, heading);
                                        if let Some(gp) = ghost { send_ghost_push(state, gp).await; }
                                        if moved { send_npc_move(state, npc_idx).await; }
                                    }
                                }
                            }
                        }
                    } else {
                        if let Some(n) = state.get_npc_mut(npc_idx) {
                            n.target = None;
                            n.pf_path.clear();
                            n.pf_step = 0;
                        }
                    }
                } else {
                    if rand_range(1, 12) == 3 {
                        let heading = rand_range(1, 4);
                        {
                            let (moved, ghost) = move_npc(state, npc_idx, heading);
                            if let Some(gp) = ghost { send_ghost_push(state, gp).await; }
                            if moved { send_npc_move(state, npc_idx).await; }
                        }
                    }
                }
            }

            npc::AI_FOLLOW_OWNER => {
                // Pet AI — follow master, attack assigned target NPC or master's target
                let (master_id, pet_target_npc) = match state.get_npc(npc_idx) {
                    Some(n) => (n.maestro_user, n.target_npc),
                    None => (None, 0),
                };
                if let Some(master_conn) = master_id {
                    let master_pos = state.users.get(&master_conn)
                        .filter(|u| u.logged && !u.dead && u.pos_map == map)
                        .map(|u| (u.pos_x, u.pos_y, u.target_npc_idx));

                    if let Some((mx, my, master_target_npc)) = master_pos {
                        let dist = (x - mx).abs() + (y - my).abs();

                        // Priority: pet's own target (from check_pets) > master's target
                        let effective_target = if pet_target_npc > 0 {
                            // Validate pet target is still alive
                            if state.get_npc(pet_target_npc).map(|n| n.is_alive()).unwrap_or(false) {
                                pet_target_npc
                            } else {
                                // Target dead — clear it
                                if let Some(n) = state.get_npc_mut(npc_idx) { n.target_npc = 0; }
                                master_target_npc
                            }
                        } else {
                            master_target_npc
                        };

                        if effective_target > 0 && can_attack {
                            let target_npc_alive = state.get_npc(effective_target)
                                .map(|n| n.is_alive())
                                .unwrap_or(false);
                            if target_npc_alive {
                                let target_npc_pos = state.get_npc(effective_target)
                                    .map(|n| (n.x, n.y));
                                if let Some((tnx, tny)) = target_npc_pos {
                                    let tdist = (x - tnx).abs() + (y - tny).abs();
                                    if tdist <= 1 {
                                        npc_attack_npc(state, npc_idx, effective_target).await;
                                        if let Some(n) = state.get_npc_mut(npc_idx) {
                                            n.can_attack = false;
                                        }
                                    } else {
                                        let heading = chase_heading(x, y, tnx, tny);
                                        {
                                            let (moved, ghost) = move_npc(state, npc_idx, heading);
                                            if let Some(gp) = ghost { send_ghost_push(state, gp).await; }
                                            if moved { send_npc_move(state, npc_idx).await; }
                                        }
                                    }
                                }
                            } else {
                                // Target dead — clear
                                if let Some(n) = state.get_npc_mut(npc_idx) { n.target_npc = 0; }
                            }
                        } else if dist > 3 {
                            // Too far from master — follow
                            let heading = chase_heading(x, y, mx, my);
                            {
                                let (moved, ghost) = move_npc(state, npc_idx, heading);
                                if let Some(gp) = ghost { send_ghost_push(state, gp).await; }
                                if moved { send_npc_move(state, npc_idx).await; }
                            }
                        } else if rand_range(1, 12) == 3 {
                            // Near master — random wander
                            let heading = rand_range(1, 4);
                            {
                                let (moved, ghost) = move_npc(state, npc_idx, heading);
                                if let Some(gp) = ghost { send_ghost_push(state, gp).await; }
                                if moved { send_npc_move(state, npc_idx).await; }
                            }
                        }
                    }
                }
            }

            _ => {
                // Unknown AI type — do nothing
            }
        }
    }
}

/// NPC self-heal: check if any spell has SubeHP=1 and cast it on self.
pub(super) async fn npc_try_self_heal(state: &mut GameState, npc_idx: usize, spells: &[i32]) {
    for &spell_id in spells {
        let spell = match state.get_spell(spell_id) {
            Some(s) => s.clone(),
            None => continue,
        };
        if spell.sube_hp == 1 {
            // Heal spell — heal the NPC
            let heal = rand_range(spell.min_hp.max(1), spell.max_hp.max(1));
            let (map, nx, ny, npc_name) = match state.get_npc(npc_idx) {
                Some(n) => (n.map, n.x, n.y, n.name.clone()),
                None => return,
            };
            let npc_char = match state.get_npc(npc_idx) {
                Some(n) => n.char_index,
                None => return,
            };

            if let Some(npc) = state.get_npc_mut(npc_idx) {
                npc.min_hp = (npc.min_hp + heal).min(npc.max_hp);
            }

            // FX on NPC
            if spell.fx_grh > 0 {
                let pkt = binary_packets::write_create_fx(npc_char.0 as i16, spell.fx_grh as i16, spell.loops as i16);
                state.send_data_bytes(SendTarget::ToArea { map, x: nx, y: ny }, &pkt).await;
            }
            if spell.wav > 0 {
                let pkt = binary_packets::write_play_wave(spell.wav as u8, nx as u8, ny as u8);
                state.send_data_bytes(SendTarget::ToArea { map, x: nx, y: ny }, &pkt).await;
            }

            // Magic words
            if !spell.palabras_magicas.is_empty() {
                state.send_chat_over_head_to(SendTarget::ToArea { map, x: nx, y: ny }, &format!("{} dice: {}", npc_name, spell.palabras_magicas), npc_char.0 as i16, 255).await;
            }
            break; // Only cast one heal per tick
        }
    }
}

/// Respawn tick — check dead NPCs and revive them.
pub async fn tick_npc_respawn(state: &mut GameState) {
    let dead_npcs: Vec<usize> = state.npcs.iter().enumerate()
        .filter_map(|(i, slot)| {
            slot.as_ref().filter(|n| !n.active && n.respawn).map(|_| i)
        })
        .collect();

    for npc_idx in dead_npcs {
        if state.respawn_npc(npc_idx) {
            // Send CC to area users
            let cc = match state.get_npc(npc_idx) {
                Some(n) => (n.build_cc_binary(), n.map, n.x, n.y),
                None => continue,
            };
            let (cc_pkt, map, x, y) = cc;
            state.send_data_bytes(SendTarget::ToArea { map, x, y }, &cc_pkt).await;
        }
    }
}

/// Send NPC movement packet (* opcode) to area users.
pub(super) async fn send_npc_move(state: &mut GameState, npc_idx: usize) {
    let data = match state.get_npc(npc_idx) {
        Some(n) => (n.char_index.0, n.x, n.y, n.map, n.heading, n.area_min_x, n.area_min_y),
        None => return,
    };
    let (char_idx, x, y, map, heading, area_min_x, area_min_y) = data;

    // VB6 movement packet: *charindex,x,y → binary CharacterMove
    let pkt = binary_packets::write_character_move(char_idx as i16, x as u8, y as u8);

    // VB6 SendToNpcArea: sends to all users in the NPC's 27x27 area zone
    // (NOT the smaller 17x13 client window used by SendTarget::ToArea)
    let area_max_x = (area_min_x + 26).min(100);
    let area_max_y = (area_min_y + 26).min(100);
    let area_min_x_clamped = area_min_x.max(1);
    let area_min_y_clamped = area_min_y.max(1);

    let mut targets: Vec<ConnectionId> = Vec::new();
    if let Some(grid) = state.world.grid(map) {
        for sy in area_min_y_clamped..=area_max_y {
            for sx in area_min_x_clamped..=area_max_x {
                if let Some(tile) = grid.tile(sx, sy) {
                    if let Some(conn) = tile.user_conn {
                        targets.push(conn);
                    }
                }
            }
        }
    }
    for conn in targets {
        state.send_bytes(conn, &pkt).await;
    }

    // Check if NPC crossed a 9x9 area boundary — if so, send CC to newly visible players
    check_update_needed_npc(state, npc_idx, heading).await;
}

/// Send packets for a ghost push (position update to ghost + movement broadcast to area).
pub(super) async fn send_ghost_push(state: &mut GameState, push: super::npcs::GhostPush) {
    let pu_pkt = binary_packets::write_pos_update(push.new_x as u8, push.new_y as u8);
    state.send_bytes(push.ghost_conn, &pu_pkt).await;
    let move_pkt = binary_packets::write_character_move(push.ghost_char_index as i16, push.new_x as u8, push.new_y as u8);
    state.send_data_bytes(
        SendTarget::ToAreaButIndex { conn_id: push.ghost_conn, map: push.map, x: push.new_x, y: push.new_y },
        &move_pkt,
    ).await;
}

/// CheckUpdateNeededNpc — VB6 ModAreas.bas line 320-406.
/// When an NPC crosses a 9x9 zone boundary, send CC packets to players in the new strip.
pub(super) async fn check_update_needed_npc(state: &mut GameState, npc_idx: usize, heading: i32) {
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
    let min_x = min_x.max(1);
    let min_y = min_y.max(1);
    let max_x = max_x.min(100);
    let max_y = max_y.min(100);

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
            state.send_bytes(conn_id, &npc_cc).await;
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
                    if let Some(user) = state.users.get(&conn) {
                        if user.logged && !user.dead && user.privileges == 0 && !user.admin_invisible {
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
    let min_x = (x - half_x).max(1);
    let max_x = (x + half_x).min(100);
    let min_y = (y - half_y).max(1);
    let max_y = (y + half_y).min(100);

    let mut best: Option<(ConnectionId, i32)> = None;

    if let Some(grid) = state.world.grid(map) {
        for cy in min_y..=max_y {
            for cx in min_x..=max_x {
                if let Some(tile) = grid.tile(cx, cy) {
                    if let Some(conn) = tile.user_conn {
                        if let Some(user) = state.users.get(&conn) {
                            if user.logged && !user.dead && user.privileges == 0 {
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
    let min_x = (x - half_x).max(1);
    let max_x = (x + half_x).min(100);
    let min_y = (y - half_y).max(1);
    let max_y = (y + half_y).min(100);

    let mut best: Option<(ConnectionId, i32)> = None;

    if let Some(grid) = state.world.grid(map) {
        for cy in min_y..=max_y {
            for cx in min_x..=max_x {
                if let Some(tile) = grid.tile(cx, cy) {
                    if let Some(conn) = tile.user_conn {
                        if let Some(user) = state.users.get(&conn) {
                            if user.logged && !user.dead && user.criminal && user.privileges == 0 {
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
    let min_x = (x - half_x).max(1);
    let max_x = (x + half_x).min(100);
    let min_y = (y - half_y).max(1);
    let max_y = (y + half_y).min(100);

    let mut best: Option<(ConnectionId, i32)> = None;

    if let Some(grid) = state.world.grid(map) {
        for cy in min_y..=max_y {
            for cx in min_x..=max_x {
                if let Some(tile) = grid.tile(cx, cy) {
                    if let Some(conn) = tile.user_conn {
                        if let Some(user) = state.users.get(&conn) {
                            if user.logged && !user.dead && !user.criminal && user.privileges == 0 {
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
    let min_x = (x - range).max(1);
    let max_x = (x + range).min(100);
    let min_y = (y - range).max(1);
    let max_y = (y + range).min(100);

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
pub(super) async fn npc_attack_npc(state: &mut GameState, attacker_idx: usize, target_idx: usize) {
    let attacker_data = match state.get_npc(attacker_idx) {
        Some(n) if n.is_alive() => (n.min_hit, n.max_hit, n.char_index, n.map, n.x, n.y, n.maestro_user),
        _ => return,
    };
    let (a_min_hit, a_max_hit, _a_char, a_map, a_x, a_y, a_master) = attacker_data;

    let target_alive = state.get_npc(target_idx).map(|n| n.is_alive()).unwrap_or(false);
    if !target_alive { return; }

    // Simple damage — no armor for NPC vs NPC
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
    let snd_pkt = binary_packets::write_play_wave(10, a_x as u8, a_y as u8);
    state.send_data_bytes(SendTarget::ToArea { map: a_map, x: a_x, y: a_y }, &snd_pkt).await;

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
            state.send_data_bytes(SendTarget::ToArea { map, x, y }, &bp_pkt).await;
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
        state.send_data_bytes(SendTarget::ToArea { map: p_map, x: p_x, y: p_y }, &bp).await;
        // Remove pet from owner
        if let Some(owner_conn) = a_master {
            remove_pet_from_owner(state, owner_conn, attacker_idx);
        }
        state.kill_npc(attacker_idx);
    }
}

/// Remove a pet NPC from its owner's mascotas array.
pub(super) fn remove_pet_from_owner(state: &mut GameState, owner_conn: ConnectionId, npc_idx: usize) {
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

/// Update map_user_counts cache (how many logged users per map).
// update_map_user_counts — moved to common.rs

// =====================================================================
// Passive regeneration / drain system
// =====================================================================

// VB6 13.3 intervals (in seconds, since tick_player_passive runs every 1s).
// VB6 original values are in ticks (~100ms each): IntervaloHambre=6500, IntervaloSed=6000,
// IntervaloVeneno=500, SanaIntervaloSinDescansar=1600, SanaIntervaloDescansar=100,
// StaminaIntervaloSinDescansar=10, StaminaIntervaloDescansar=5.
// Converted: VB6_ticks / 10 = seconds.
const HUNGER_INTERVAL: i32 = 650;  // VB6: 6500 ticks = ~650 seconds (~10.8 min)
const THIRST_INTERVAL: i32 = 600;  // VB6: 6000 ticks = ~600 seconds (~10 min)
const STAMINA_INTERVAL: i32 = 1;   // VB6: 10 ticks = ~1 second (standing)
const STAMINA_INTERVAL_REST: i32 = 1; // VB6: 5 ticks = ~0.5s (resting, we use 1s min)
const HP_REGEN_INTERVAL: i32 = 160; // VB6: SanaIntervaloSinDescansar=1600 ticks (~160s)
const HP_REGEN_INTERVAL_REST: i32 = 10; // VB6: SanaIntervaloDescansar=100 ticks (~10s)
const POISON_INTERVAL: i32 = 50;   // VB6: IntervaloVeneno=500 ticks (500 * 100ms = 50s)
const HUNGER_DRAIN: i32 = 10;      // VB6: 10 per interval
const THIRST_DRAIN: i32 = 10;      // VB6: 10 per interval
const COLD_LAVA_INTERVAL: i32 = 2; // VB6: IntervaloFrio=15 ticks (15 * 100ms = 1.5s, ~2s at 1s tick)
const INTERVALO_INVISIBLE: i32 = 50; // VB6: IntervaloInvisible=500 ticks (500 * 100ms = 50s)
// VB6 meditation FX by level (Declares.bas lines 204-211)
const FXMEDITARCHICO: i16 = 4;       // level < 13
const FXMEDITARMEDIANO: i16 = 5;     // level 13-24
const FXMEDITARGRANDE: i16 = 6;      // level 25-34
const FXMEDITARXGRANDE: i16 = 16;    // level 35-41
const FXMEDITARXXGRANDE: i16 = 34;   // level >= 42

pub fn meditation_fx_for_level(level: i32) -> i16 {
    if level < 13 { FXMEDITARCHICO }
    else if level < 25 { FXMEDITARMEDIANO }
    else if level < 35 { FXMEDITARGRANDE }
    else if level < 42 { FXMEDITARXGRANDE }
    else { FXMEDITARXXGRANDE }
}

/// ME — Toggle meditation on/off.
pub(super) async fn handle_meditate(state: &mut GameState, conn_id: ConnectionId) {
    let user = match state.users.get(&conn_id) {
        Some(u) if u.logged && !u.dead => u,
        _ => return,
    };

    let meditating = user.meditating;
    let char_index = user.char_index;
    let map = user.pos_map;
    let x = user.pos_x;
    let y = user.pos_y;
    let level = user.level;

    if meditating {
        // Stop meditation — clear FX
        if let Some(user) = state.users.get_mut(&conn_id) {
            user.meditating = false;
        }
        state.send_msg_id(conn_id, 205, "").await; // Dejas de meditar
        state.send_bytes(conn_id, &binary_packets::write_meditate_toggle()).await;
        let fx_clear = binary_packets::write_create_fx(char_index.0 as i16, 0, 0);
        state.send_data_bytes(SendTarget::ToArea { map, x, y }, &fx_clear).await;
    } else {
        // Check if mana is already full
        if user.min_mana >= user.max_mana {
            state.send_msg_id(conn_id, 393, "").await; // Mana restaurado
            return;
        }

        if let Some(user) = state.users.get_mut(&conn_id) {
            user.meditating = true;
        }

        // VB6: meditation FX scales by level (5 tiers), 999 loops = forever
        let med_fx = meditation_fx_for_level(level);
        let fx_pkt = binary_packets::write_create_fx(char_index.0 as i16, med_fx, 999);
        state.send_data_bytes(SendTarget::ToArea { map, x, y }, &fx_pkt).await;

        state.send_msg_id(conn_id, 394, "").await; // Comenzas a meditar
    }
}

/// Called every 1 second — handles all passive regen/drain for logged users.
pub async fn tick_player_passive(state: &mut GameState) {
    // Collect logged user connections
    let user_ids: Vec<ConnectionId> = state.users.keys().copied().collect();

    for conn_id in user_ids {
        let user_data = match state.users.get(&conn_id) {
            Some(u) if u.logged && !u.dead => Some((
                u.poisoned,
                u.meditating,
                u.min_hp, u.max_hp,
                u.min_mana, u.max_mana,
                u.min_sta, u.max_sta,
                u.min_agua, u.max_agua,
                u.min_ham, u.max_ham,
                u.counter_hunger, u.counter_thirst,
                u.counter_stamina, u.counter_poison,
                u.skills[6], // SK7 = Meditar skill
                u.privileges,
                u.resting,
                u.mimetizado,
                u.invisible,
                u.pos_map, u.pos_x, u.pos_y,
                u.equip.armor,
                u.char_index.0,
            )),
            _ => None,
        };

        let (poisoned, meditating, min_hp, max_hp, min_mana, max_mana,
             min_sta, max_sta, min_agua, _max_agua, min_ham, _max_ham,
             cnt_hunger, cnt_thirst, cnt_sta, cnt_poison,
             meditate_skill, privileges, resting, _mimetizado, _invisible,
             pos_map, pos_x, pos_y, equip_armor, _char_idx) = match user_data {
            Some(d) => d,
            None => continue,
        };

        // Only non-GM users have hunger/thirst
        let is_player = privileges == 0;

        // --- Hunger drain ---
        if is_player && min_ham > 0 {
            if cnt_hunger >= HUNGER_INTERVAL {
                if let Some(u) = state.users.get_mut(&conn_id) {
                    u.counter_hunger = 0;
                    u.min_ham = (u.min_ham - HUNGER_DRAIN).max(0);
                }
                send_hunger_thirst(state, conn_id).await;
            } else {
                if let Some(u) = state.users.get_mut(&conn_id) {
                    u.counter_hunger += 1;
                }
            }
        }

        // --- Thirst drain ---
        if is_player && min_agua > 0 {
            if cnt_thirst >= THIRST_INTERVAL {
                if let Some(u) = state.users.get_mut(&conn_id) {
                    u.counter_thirst = 0;
                    u.min_agua = (u.min_agua - THIRST_DRAIN).max(0);
                }
                send_hunger_thirst(state, conn_id).await;
            } else {
                if let Some(u) = state.users.get_mut(&conn_id) {
                    u.counter_thirst += 1;
                }
            }
        }

        // --- Stamina regeneration (VB6: RecStamina) ---
        // VB6: regen = RandomNumber(1, Porcentaje(MaxSta, 5)) = 1 to 5% of max STA
        // Blocked when hungry or thirsty, blocked when naked (desnudo = no armor)
        let desnudo = equip_armor == 0;
        let sta_interval = if resting { STAMINA_INTERVAL_REST } else { STAMINA_INTERVAL };
        if min_sta < max_sta && min_ham > 0 && min_agua > 0 && !desnudo {
            if cnt_sta >= sta_interval {
                let five_pct = ((max_sta as f64 * 5.0) / 100.0).max(1.0) as i32;
                let regen = rand_range(1, five_pct);
                if let Some(u) = state.users.get_mut(&conn_id) {
                    u.counter_stamina = 0;
                    u.min_sta = (u.min_sta + regen).min(u.max_sta);
                }
                send_stats_sta(state, conn_id).await;
            } else {
                if let Some(u) = state.users.get_mut(&conn_id) {
                    u.counter_stamina += 1;
                }
            }
        }

        // --- Poison damage ---
        if poisoned {
            if cnt_poison >= POISON_INTERVAL {
                let dmg = rand_range(1, 5);
                let new_hp = min_hp - dmg;
                if let Some(u) = state.users.get_mut(&conn_id) {
                    u.counter_poison = 0;
                    u.min_hp = new_hp;
                }
                // VB6: "Estás envenenado, si no te curas morirás." (FONTTYPE_VENENO)
                state.send_console(conn_id, "Estás envenenado, si no te curas morirás.", font_index::VENENO).await;
                send_stats_hp(state, conn_id).await;

                // VB6: FXSANGRE (blood FX 14) on poison tick if not meditating/navigating
                if !meditating {
                    if let Some(u) = state.users.get(&conn_id) {
                        if !u.navigating {
                            let fx_pkt = binary_packets::write_create_fx(
                                u.char_index.0 as i16, 14, 0, // FXSANGRE = 14
                            );
                            state.send_data_bytes(
                                SendTarget::ToArea { map: u.pos_map, x: u.pos_x, y: u.pos_y },
                                &fx_pkt,
                            ).await;
                        }
                    }
                }

                if new_hp <= 0 {
                    user_die(state, conn_id, None).await;
                }
            } else {
                if let Some(u) = state.users.get_mut(&conn_id) {
                    u.counter_poison += 1;
                }
            }
        }

        // --- Cold damage (VB6: EfectoFrio) ---
        // VB6: Only when naked (no armor). On snow terrain: 5% MaxHP damage. Elsewhere: 5% MaxSTA drain.
        if is_player && desnudo {
            let cnt_frio = state.users.get(&conn_id).map(|u| u.counter_frio).unwrap_or(0);
            if cnt_frio >= COLD_LAVA_INTERVAL {
                if let Some(u) = state.users.get_mut(&conn_id) {
                    u.counter_frio = 0;
                }
                // Check terrain type from map info
                let is_snow = state.game_data.maps.get(pos_map as usize)
                    .and_then(|m| m.as_ref())
                    .map(|m| m.info.terreno.eq_ignore_ascii_case("NIEVE"))
                    .unwrap_or(false);
                if is_snow {
                    let dmg = ((max_hp as f64 * 5.0) / 100.0) as i32;
                    let new_hp = min_hp - dmg;
                    state.send_console(conn_id, "¡¡Estás muriendo de frío, abrigate o morirás!!", font_index::INFO).await;
                    if let Some(u) = state.users.get_mut(&conn_id) {
                        u.min_hp = new_hp;
                    }
                    send_stats_hp(state, conn_id).await;
                    if new_hp <= 0 {
                        state.send_console(conn_id, "¡¡Has muerto de frío!!", font_index::INFO).await;
                        user_die(state, conn_id, None).await;
                    }
                } else {
                    // Non-snow: stamina drain
                    let sta_dmg = ((max_sta as f64 * 5.0) / 100.0) as i32;
                    if let Some(u) = state.users.get_mut(&conn_id) {
                        u.min_sta = (u.min_sta - sta_dmg).max(0);
                    }
                    send_stats_sta(state, conn_id).await;
                }
            } else {
                if let Some(u) = state.users.get_mut(&conn_id) {
                    u.counter_frio += 1;
                }
            }
        }

        // --- Lava damage (VB6: EfectoLava) ---
        // VB6: If standing on lava tile (graphic[0] in 5837-5852), 5% MaxHP damage.
        if is_player {
            let on_lava = state.game_data.maps.get(pos_map as usize)
                .and_then(|m| m.as_ref())
                .and_then(|m| {
                    if pos_x > 0 && pos_x <= 100 && pos_y > 0 && pos_y <= 100 {
                        Some(m.tiles[(pos_y - 1) as usize][(pos_x - 1) as usize].graphic[0])
                    } else { None }
                })
                .map(|g| g >= 5837 && g <= 5852)
                .unwrap_or(false);
            if on_lava {
                let cnt_lava = state.users.get(&conn_id).map(|u| u.counter_lava).unwrap_or(0);
                if cnt_lava >= COLD_LAVA_INTERVAL {
                    let dmg = ((max_hp as f64 * 5.0) / 100.0) as i32;
                    let new_hp = min_hp - dmg;
                    state.send_console(conn_id, "¡¡Quitate de la lava, te estás quemando!!", font_index::INFO).await;
                    if let Some(u) = state.users.get_mut(&conn_id) {
                        u.counter_lava = 0;
                        u.min_hp = new_hp;
                    }
                    send_stats_hp(state, conn_id).await;
                    if new_hp <= 0 {
                        state.send_console(conn_id, "¡¡Has muerto quemado!!", font_index::INFO).await;
                        user_die(state, conn_id, None).await;
                    }
                } else {
                    if let Some(u) = state.users.get_mut(&conn_id) {
                        u.counter_lava += 1;
                    }
                }
            }
        }

        // Mimetismo is handled in tick_intervals (40ms tick) like invisibility/hide.

        // --- HP Regeneration (VB6: Sanar) ---
        // VB6: regen = RandomNumber(2, Porcentaje(MaxSta, 5)) — note: uses MaxSta not MaxHp (VB6 bug we replicate)
        // Interval: SanaIntervaloSinDescansar=1600 ticks (~160s), SanaIntervaloDescansar=100 ticks (~10s)
        // Blocked when hungry or thirsty, only for non-GMs
        let hp_interval = if resting { HP_REGEN_INTERVAL_REST } else { HP_REGEN_INTERVAL };
        if is_player && min_hp > 0 && min_hp < max_hp && min_ham > 0 && min_agua > 0 {
            let hp_counter = state.users.get(&conn_id).map(|u| u.counter_hp_regen).unwrap_or(0);
            if hp_counter >= hp_interval {
                let five_pct = ((max_sta as f64 * 5.0) / 100.0).max(2.0) as i32;
                let regen = rand_range(2, five_pct);
                if let Some(u) = state.users.get_mut(&conn_id) {
                    u.counter_hp_regen = 0;
                    u.min_hp = (u.min_hp + regen).min(u.max_hp);
                }
                state.send_console(conn_id, "Has sanado.", font_index::INFO).await;
                send_stats_hp(state, conn_id).await;
            } else {
                if let Some(u) = state.users.get_mut(&conn_id) {
                    u.counter_hp_regen += 1;
                }
            }
        }

        // --- Blindness countdown ---
        let blind_counter = state.users.get(&conn_id).map(|u| (u.blind, u.counter_blind)).unwrap_or((false, 0));
        if blind_counter.0 {
            if blind_counter.1 > 0 {
                if let Some(u) = state.users.get_mut(&conn_id) {
                    u.counter_blind -= 1;
                }
            } else {
                if let Some(u) = state.users.get_mut(&conn_id) {
                    u.blind = false;
                    u.counter_blind = 0;
                }
                state.send_console(conn_id, "Ya puedes ver.", font_index::INFO).await;
            }
        }

        // --- Stun countdown ---
        let stun_data = state.users.get(&conn_id).map(|u| (u.stunned, u.counter_stun)).unwrap_or((false, 0));
        if stun_data.0 {
            if stun_data.1 > 0 {
                if let Some(u) = state.users.get_mut(&conn_id) {
                    u.counter_stun -= 1;
                }
            } else {
                if let Some(u) = state.users.get_mut(&conn_id) {
                    u.stunned = false;
                    u.counter_stun = 0;
                }
                state.send_console(conn_id, "Has recuperado la lucidez.", font_index::INFO).await;
            }
        }

        // --- Buff duration (DuracionPociones) ---
        let duracion = state.users.get(&conn_id).map(|u| u.duracion_efecto).unwrap_or(0);
        if duracion > 0 {
            if let Some(u) = state.users.get_mut(&conn_id) {
                u.duracion_efecto -= 1;
                if u.duracion_efecto <= 0 {
                    // Buff expired — restore attributes from backup
                    u.tomo_pocion = false;
                    u.attributes = u.attributes_backup;
                }
            }
        }

        // --- Remo potion cooldown ---
        let remo = state.users.get(&conn_id).map(|u| u.counter_remo).unwrap_or(0);
        if remo > 0 {
            if let Some(u) = state.users.get_mut(&conn_id) {
                u.counter_remo -= 1;
            }
        }

        // --- Silence timer (mute countdown) ---
        let silence = state.users.get(&conn_id).map(|u| (u.silenced, u.silence_timer)).unwrap_or((false, 0));
        if silence.0 && silence.1 > 0 {
            if let Some(u) = state.users.get_mut(&conn_id) {
                u.silence_timer -= 1;
                if u.silence_timer <= 0 {
                    u.silenced = false;
                    u.silence_timer = 0;
                }
            }
            let unmuted = state.users.get(&conn_id).map(|u| !u.silenced).unwrap_or(false);
            if unmuted {
                state.send_msg_id(conn_id, 946, "").await;
            }
        }

        // --- Jail timer (prison countdown) ---
        let jail = state.users.get(&conn_id).map(|u| u.jail_timer).unwrap_or(0);
        if jail > 0 {
            if let Some(u) = state.users.get_mut(&conn_id) {
                u.jail_timer -= 1;
            }
            let released = state.users.get(&conn_id).map(|u| u.jail_timer <= 0).unwrap_or(false);
            if released {
                // Release from jail — warp to Libertad (map 28, 50, 50)
                state.send_msg_id(conn_id, 444, "").await;
                warp_user(state, conn_id, 28, 50, 50).await;
            }
        }

        // --- Delayed resurrection countdown ---
        let seg_revivir = state.users.get(&conn_id).map(|u| u.segundos_para_revivir).unwrap_or(0);
        if seg_revivir > 0 {
            if let Some(u) = state.users.get_mut(&conn_id) {
                u.segundos_para_revivir -= 1;
            }
            let ready = state.users.get(&conn_id).map(|u| u.segundos_para_revivir <= 0).unwrap_or(false);
            if ready {
                revive_user(state, conn_id).await;
            }
        }

        // --- Resurrection cooldown ---
        let time_rev = state.users.get(&conn_id).map(|u| u.time_revivir).unwrap_or(0);
        if time_rev > 0 {
            if let Some(u) = state.users.get_mut(&conn_id) {
                u.time_revivir -= 1;
            }
        }

        // Anti-cheat intervals now decremented in tick_intervals (40ms tick)

        // --- Meditation (mana regen) — VB6: Trabajo.bas DoMeditar ---
        if meditating && min_mana < max_mana {
            // VB6: Skill-based "1 in N" chance per tick (lower N = better)
            let suerte = match meditate_skill {
                0..=10 => 35,
                11..=20 => 30,
                21..=30 => 28,
                31..=40 => 24,
                41..=50 => 22,
                51..=60 => 20,
                61..=70 => 18,
                71..=80 => 15,
                81..=90 => 10,
                91..=99 => 7,
                _ => 5, // skill 100
            };

            if rand_range(1, suerte) == 1 {
                // VB6: cant = Porcentaje(MaxMAN, PorcentajeRecuperoMana)
                // PorcentajeRecuperoMana is typically 5 from Balance.dat
                let regen = ((max_mana as f64 * 5.0) / 100.0) as i32;
                let regen = regen.max(1);
                if let Some(u) = state.users.get_mut(&conn_id) {
                    u.min_mana = (u.min_mana + regen).min(u.max_mana);
                    if u.min_mana >= u.max_mana {
                        u.meditating = false;
                    }
                }
                send_stats_mana(state, conn_id).await;

                // Stop meditation when full — clear FX + notify
                let stopped = state.users.get(&conn_id).map(|u| !u.meditating).unwrap_or(false);
                if stopped {
                    state.send_msg_id(conn_id, 829, "").await; // Has terminado de meditar
                    state.send_bytes(conn_id, &binary_packets::write_meditate_toggle()).await;
                    // Clear FX for area
                    if let Some(u) = state.users.get(&conn_id) {
                        let fx_clear = binary_packets::write_create_fx(u.char_index.0 as i16, 0, 0);
                        state.send_data_bytes(
                            SendTarget::ToArea { map: u.pos_map, x: u.pos_x, y: u.pos_y },
                            &fx_clear,
                        ).await;
                    }
                }
            }
        }
    }

    // --- Auto-save all characters every 60 seconds ---
    state.auto_save_counter -= 1;
    if state.auto_save_counter <= 0 {
        state.auto_save_counter = 60;
        auto_save_all_users(state).await;
    }
}

/// Save all logged-in users to DB (periodic auto-save).
pub(super) async fn auto_save_all_users(state: &GameState) {
    let pool = state.pool.clone();
    let mut saved = 0;
    for (_conn_id, user) in state.users.iter() {
        if !user.logged || user.char_name.is_empty() { continue; }
        // Convert inventory to save format
        let inv: Vec<(i32, i32, bool)> = user.inventory.iter()
            .map(|s| (s.obj_index, s.amount, s.equipped))
            .collect();
        let bank: Vec<(i32, i32)> = user.bank.iter()
            .map(|s| (s.obj_index, s.amount))
            .collect();
        let data = charfile::CharSaveData {
            // VB6: When navigating, save the REAL head (old_head), not 0.
            // The boat body is transient — on login we reconstruct it from BarcoSlot.
            head: if user.navigating { user.old_head } else { user.head },
            body: user.body,
            heading: user.heading,
            weapon: user.equip.weapon as i32,
            shield: user.equip.shield as i32,
            helmet: user.equip.helmet as i32,
            gold: user.gold,
            bank_gold: user.bank_gold,
            exp: user.exp,
            level: user.level,
            map: user.pos_map,
            x: user.pos_x,
            y: user.pos_y,
            min_hp: user.min_hp,
            max_hp: user.max_hp,
            min_mana: user.min_mana,
            max_mana: user.max_mana,
            min_sta: user.min_sta,
            max_sta: user.max_sta,
            max_hit: user.max_hit,
            min_hit: user.min_hit,
            max_agua: user.max_agua,
            min_agua: user.min_agua,
            max_ham: user.max_ham,
            min_ham: user.min_ham,
            dead: user.dead,
            poisoned: user.poisoned,
            criminal: user.criminal,
            paralyzed: user.paralyzed,
            hidden: user.hidden,
            navigating: user.navigating,
            barco_slot: user.barco_slot,
            montado: user.montado,
            levitando: user.levitando,
            montado_body: user.montado_body,
            privileges: user.saved_privileges,
            attributes: user.attributes,
            skills: user.skills,
            spells: user.spells,
            inventory: inv,
            bank,
            weapon_eqp_slot: user.equip.weapon,
            armour_eqp_slot: user.equip.armor,
            shield_eqp_slot: user.equip.shield,
            helmet_eqp_slot: user.equip.helmet,
            municion_eqp_slot: user.equip.municion,
            reputation: user.reputation,
            guild_index: user.guild_index,
            criminales_matados: user.criminales_matados,
            ciudadanos_matados: user.ciudadanos_matados,
            ejercito_real: user.armada_real,
            ejercito_caos: user.fuerzas_caos,
            skill_pts_libres: user.skill_pts_libres,
            recompensas_real: user.recompensas_real,
            recompensas_caos: user.recompensas_caos,
            reenlistadas: user.reenlistadas,
            description: user.desc.clone(),
        };
        if charfile::save_charfile(&pool, &user.char_name, &data).await.is_ok() {
            saved += 1;
        }
    }
    if saved > 0 {
        tracing::debug!("[SAVE] Auto-saved {} characters", saved);
    }
}

// =====================================================================
// World cleanup system (ModuloLimpieza.bas)
// =====================================================================

/// Add an item to the world cleanup tracking queue.
/// Items will be auto-removed after `tiempo` ticks.
// clean_world_add_item — moved to common.rs

/// 40ms game tick — decrement anti-cheat interval counters (RestoTiempo).
/// This matches VB6's timer interval for movement/action speed limiting.
pub async fn tick_intervals(state: &mut GameState) {
    // Collect IDs of users whose paralysis expired this tick (need to send PARADOK)
    let mut unparalyze: Vec<ConnectionId> = Vec::new();

    for (&conn_id, user) in state.users.iter_mut() {
        if !user.logged { continue; }
        if user.interval_golpe > 0 { user.interval_golpe -= 1; }
        if user.interval_flechas > 0 { user.interval_flechas -= 1; }
        if user.interval_casteo > 0 { user.interval_casteo -= 1; }
        if user.interval_poteo > 0 { user.interval_poteo -= 1; }
        if user.interval_click > 0 { user.interval_click -= 1; }
        if user.interval_trabajar > 0 { user.interval_trabajar -= 1; }
        if user.interval_pu > 0 { user.interval_pu -= 1; }

        // VB6: EfectoParalisisUser — count down paralysis timer each tick
        if user.paralyzed {
            if user.counter_paralisis > 0 {
                user.counter_paralisis -= 1;
            } else {
                // Timer expired — remove paralysis
                user.paralyzed = false;
                user.immobilized = false;
                unparalyze.push(conn_id);
            }
        }
    }

    // Send PARADOK to users who just got unparalyzed
    for conn_id in unparalyze {
        let pkt = binary_packets::write_paralize_ok(0);
        state.send_bytes(conn_id, &pkt).await;
    }

    // VB6: EfectoInvisibilidad — count up invisibility timer each tick.
    // Only for spell invisibility (not admin_invisible which is permanent).
    let intervalo_invis = state.config.intervalo_invisible;
    let mut uninvis: Vec<(ConnectionId, i16, i32, i32, i32, bool, bool)> = Vec::new();
    for (&conn_id, user) in state.users.iter_mut() {
        if user.invisible && !user.admin_invisible {
            if user.counter_invisible < intervalo_invis {
                user.counter_invisible += 1;
            } else {
                // Timer expired — remove invisibility
                // VB6: .Counters.Invisibilidad = RandomNumber(-100, 100) — variable next duration
                user.invisible = false;
                user.counter_invisible = rand_range(-10, 10); // Scaled: VB6 -100..100 ticks → -10..10 seconds
                // VB6: only send SetInvisible(false) if Oculto=0 (still hidden → no visibility change)
                uninvis.push((conn_id, user.char_index.0 as i16, user.pos_map, user.pos_x, user.pos_y, user.navigating, user.hidden));
            }
        }
    }
    for (conn_id, ci, map, x, y, navigating, still_hidden) in uninvis {
        if !still_hidden {
            state.send_console(conn_id, "Has vuelto a ser visible.", font_index::INFO).await;
            if !navigating {
                // Re-broadcast CC so others see us again
                let cc = state.users.get(&conn_id).unwrap().build_cc_binary();
                state.send_data_bytes(SendTarget::ToArea { map, x, y }, &cc).await;
                let cd = super::common::build_cd_binary(state.users.get(&conn_id).unwrap());
                state.send_data_bytes(SendTarget::ToArea { map, x, y }, &cd).await;
                // Tell self we're visible again
                let nover = binary_packets::write_set_invisible(ci, false, 0);
                state.send_bytes(conn_id, &nover).await;
            }
        }
    }

    // VB6: DoPermanecerOculto — count down hide timer each tick.
    // When timer expires, hidden is cleared. Only send SetInvisible(false) if invisible=0.
    let mut unhide: Vec<(ConnectionId, i16, i32, i32, i32, bool, bool)> = Vec::new();
    for (&conn_id, user) in state.users.iter_mut() {
        if user.hidden && !user.admin_invisible {
            // Hunter with skill>90 + special armor stays hidden indefinitely
            let skill = user.skills.get(7).copied().unwrap_or(0);
            let armor_obj = if user.equip.armor >= 1 && user.equip.armor <= user.inventory.len() {
                user.inventory[user.equip.armor - 1].obj_index
            } else { 0 };
            if user.class.eq_ignore_ascii_case("Cazador") && skill > 90 && (armor_obj == 648 || armor_obj == 360) {
                continue;
            }

            user.counter_oculto -= 1;
            if user.counter_oculto <= 0 {
                user.counter_oculto = 0;
                user.hidden = false;
                unhide.push((conn_id, user.char_index.0 as i16, user.pos_map, user.pos_x, user.pos_y, user.navigating, user.invisible));
            }
        }
    }
    for (conn_id, ci, map, x, y, navigating, still_invisible) in unhide {
        if !still_invisible {
            state.send_console(conn_id, "Has vuelto a ser visible.", font_index::INFO).await;
            if !navigating {
                let cc = state.users.get(&conn_id).unwrap().build_cc_binary();
                state.send_data_bytes(SendTarget::ToArea { map, x, y }, &cc).await;
                let cd = super::common::build_cd_binary(state.users.get(&conn_id).unwrap());
                state.send_data_bytes(SendTarget::ToArea { map, x, y }, &cd).await;
                let nover = binary_packets::write_set_invisible(ci, false, 0);
                state.send_bytes(conn_id, &nover).await;
            }
        }
    }

    // VB6: EfectoMimetismo — count up mimicry timer each tick (same interval as invisibility).
    let mut unmime: Vec<(ConnectionId, i16, i32, i32, i32)> = Vec::new();
    for (&conn_id, user) in state.users.iter_mut() {
        if user.mimetizado {
            user.counter_mimetismo += 1;
            if user.counter_mimetismo >= intervalo_invis {
                // Restore original appearance
                user.body = user.char_mimetizado_body;
                user.head = user.char_mimetizado_head;
                user.mimetizado = false;
                user.ignorado = false;
                user.counter_mimetismo = 0;
                unmime.push((conn_id, user.char_index.0 as i16, user.pos_map, user.pos_x, user.pos_y));
            }
        }
    }
    for (conn_id, ci, map, x, y) in unmime {
        state.send_console(conn_id, "Recuperas tu apariencia normal.", font_index::INFO).await;
        if let Some(u) = state.users.get(&conn_id) {
            let cp = binary_packets::write_character_change(
                ci, u.body as i16, u.head as i16, u.heading as u8,
                u.equip.weapon as i16, u.equip.shield as i16, u.equip.helmet as i16, 0, 0,
            );
            state.send_data_bytes(SendTarget::ToArea { map, x, y }, &cp).await;
        }
    }

    // NPC paralysis countdown (same 40ms tick as user paralysis)
    for slot in state.npcs.iter_mut() {
        if let Some(npc) = slot.as_mut() {
            if npc.paralyzed {
                if npc.counter_paralisis > 0 {
                    npc.counter_paralisis -= 1;
                } else {
                    npc.paralyzed = false;
                }
            }
        }
    }
}

/// Tick the world cleanup — decrement timers and remove expired items.
pub async fn tick_clean_world(state: &mut GameState) {
    for i in 0..state.clean_world.len() {
        let entry = &state.clean_world[i];
        if entry.map == 0 && entry.tiempo == 0 {
            continue;
        }

        let map = entry.map;
        let x = entry.x;
        let y = entry.y;
        let tiempo = entry.tiempo;

        if tiempo <= 1 {
            // Timer expired — remove item from world
            let has_item = {
                if let Some(grid) = state.world.grid(map) {
                    if let Some(tile) = grid.tile(x, y) {
                        tile.ground_item.obj_index > 0
                    } else {
                        false
                    }
                } else {
                    false
                }
            };

            if has_item {
                // Erase the object from the tile
                let grid = state.world.grid_mut(map);
                if let Some(tile) = grid.tile_mut(x, y) {
                    tile.ground_item.obj_index = 0;
                    tile.ground_item.amount = 0;
                }
                // Broadcast BO (remove object from ground) to area
                let pkt = binary_packets::write_object_delete(x as u8, y as u8);
                state.send_data_bytes(SendTarget::ToArea { map, x, y }, &pkt).await;
            }

            // Clear the cleanup slot
            state.clean_world[i] = CleanWorldEntry::default();
        } else {
            // Decrement timer
            state.clean_world[i].tiempo -= 1;
        }
    }
}

