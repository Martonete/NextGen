//! NPC AI tick handlers: movement, chase, attack, healing, respawn.

use crate::net::ConnectionId;
use crate::game::types::{GameState, SendTarget};
use crate::data::npcs::NpcType;
use crate::game::npc;
use crate::protocol::{binary_packets, font_index};
use crate::game::handlers::common::*;
use crate::game::handlers::world;
use crate::game::handlers::{
    npc_attack_user, npc_die, npc_cast_spell, move_npc,
    npc_pathfind_step, pathfind_bfs,
};
// Cross-module access to npc_move functions (send_npc_move, send_ghost_push, etc.)
use super::{
    send_npc_move, send_ghost_push, find_adjacent_player, find_nearest_player,
    find_player_by_name, chase_heading, restore_old_movement, npc_attack_npc,
};

/// AI tick — called every 100ms from the main loop.
/// VB6: AI_MoveNpc / modIA.bas — full NPC behavior including hostile chase,
/// defense mode, guard chase, self-heal, and global attack timer.
pub async fn tick_npc_ai(state: &mut GameState) {
    // Global CanAttack timer: every 3 ticks (300ms), all NPCs can attack again
    state.npc_can_attack_counter += 1;
    if state.npc_can_attack_counter >= 3 {
        state.npc_can_attack_counter = 0;
        // Reset active NPCs' can_attack flag (uses index set instead of scanning all 10k)
        let indices: Vec<usize> = state.active_npc_indices.iter().copied().collect();
        for idx in &indices {
            if let Some(npc) = state.npcs.get_mut(*idx).and_then(|n| n.as_mut()) {
                npc.can_attack = true;
            }
        }
    }

    // Update map user counts for skipping empty maps
    update_map_user_counts(state);

    // Use active NPC index set instead of scanning all 10,000 slots
    let active_npcs: Vec<usize> = state.active_npc_indices.iter()
        .copied()
        .filter(|&i| state.npcs.get(i).and_then(|s| s.as_ref()).map(|n| n.is_alive()).unwrap_or(false))
        .collect();

    for npc_idx in active_npcs {
        // Skip paralyzed NPCs — they can't move or attack
        let is_paralyzed = state.get_npc(npc_idx).map(|n| n.paralyzed).unwrap_or(false);
        if is_paralyzed { continue; }

        let npc_data = match state.get_npc(npc_idx) {
            Some(n) => (n.movement, n.hostile, n.can_attack, n.map, n.x, n.y, n.target,
                        n.lanza_spells, n.spells.clone(), n.npc_type, n.attacked_by.clone(),
                        n.min_hp, n.max_hp, n.alineacion, n.ataca_doble),
            None => continue,
        };
        let (movement, hostile, can_attack, map, x, y, target,
             lanza_spells, spells, npc_type, attacked_by,
             cur_hp, max_hp, alineacion, ataca_doble) = npc_data;

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
                        // VB6 AtacaDoble: 50% chance to cast spell instead of melee
                        if ataca_doble && lanza_spells > 0 && !spells.is_empty() && rand_range(0, 1) == 0 {
                            let spell_idx = rand_range(0, spells.len() as i32 - 1) as usize;
                            let spell_id = spells[spell_idx];
                            npc_cast_spell(state, npc_idx, target_conn, spell_id).await;
                        } else {
                            npc_attack_user(state, npc_idx, target_conn).await;
                        }
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
                            // VB6 AtacaDoble: 50% chance to cast spell instead of melee
                            if ataca_doble && lanza_spells > 0 && !spells.is_empty() && rand_range(0, 1) == 0 {
                                let spell_idx = rand_range(0, spells.len() as i32 - 1) as usize;
                                let spell_id = spells[spell_idx];
                                npc_cast_spell(state, npc_idx, target_conn, spell_id).await;
                            } else {
                                npc_attack_user(state, npc_idx, target_conn).await;
                            }
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
                // VB6 AI_AI_Guardia — Guards do NOT chase players.
                // They only attack adjacent hostile players (±1 tile).
                // Royal guards attack criminals, Chaos guards attack citizens.
                let is_royal = npc_type == NpcType::RoyalGuard || alineacion == 1;

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
                            // VB6 AtacaDoble: 50% chance to cast spell instead of melee
                            if ataca_doble && lanza_spells > 0 && !spells.is_empty() && rand_range(0, 1) == 0 {
                                let spell_idx = rand_range(0, spells.len() as i32 - 1) as usize;
                                let spell_id = spells[spell_idx];
                                npc_cast_spell(state, npc_idx, target_conn, spell_id).await;
                            } else {
                                npc_attack_user(state, npc_idx, target_conn).await;
                            }
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

            npc::AI_NPC_OBJETO => {
                // VB6 NpcObjeto — stationary "turret" NPC. No movement.
                // Attack phase (VB6 NPCAI lines 160-189): can melee adjacent + spells.
                // NpcObjeto has 1/3 chance to skip attacking a given user (RandomNumber(1,3)==3).
                // Movement phase (AiNpcObjeto): scans vision for spell targets.
                if hostile && can_attack {
                    // Adjacent melee/spell attack (VB6: same attack phase as other hostiles)
                    if let Some((target_conn, _adj_heading)) = find_adjacent_player(state, map, x, y) {
                        // VB6: NpcObjeto → RandomNumber(1,3) < 3 → 2/3 chance to attack
                        if rand_range(1, 3) < 3 {
                            if ataca_doble && lanza_spells > 0 && !spells.is_empty() && rand_range(0, 1) == 0 {
                                let spell_idx = rand_range(0, spells.len() as i32 - 1) as usize;
                                let spell_id = spells[spell_idx];
                                npc_cast_spell(state, npc_idx, target_conn, spell_id).await;
                            } else if lanza_spells > 0 && !spells.is_empty() {
                                let spell_idx = rand_range(0, spells.len() as i32 - 1) as usize;
                                let spell_id = spells[spell_idx];
                                npc_cast_spell(state, npc_idx, target_conn, spell_id).await;
                            } else {
                                npc_attack_user(state, npc_idx, target_conn).await;
                            }
                            if let Some(n) = state.get_npc_mut(npc_idx) {
                                n.can_attack = false;
                            }
                        }
                    } else if lanza_spells > 0 && !spells.is_empty() {
                        // VB6 AiNpcObjeto: scan vision for spell targets at range
                        if let Some(target_conn) = find_nearest_player(state, map, x, y) {
                            if rand_range(1, 3) < 3 {
                                let spell_idx = rand_range(0, spells.len() as i32 - 1) as usize;
                                let spell_id = spells[spell_idx];
                                npc_cast_spell(state, npc_idx, target_conn, spell_id).await;
                                if let Some(n) = state.get_npc_mut(npc_idx) {
                                    n.can_attack = false;
                                }
                            }
                        }
                    }
                }
                // No movement — NpcObjeto is stationary
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
pub(crate) async fn npc_try_self_heal(state: &mut GameState, npc_idx: usize, spells: &[i32]) {
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
