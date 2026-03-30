//! NPC AI tick handlers: movement, chase, attack, healing, respawn.

use crate::game::types::{GameState, SendTarget};
use crate::data::npcs::NpcType;
use crate::game::npc;
use crate::protocol::binary_packets;
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
    find_target_npc_in_vision, find_nearest_hostile_npc, find_wounded_pretoriano_ally, find_paralyzed_pretoriano_ally,
    has_pretoriano_allies,
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

        // VB6: Elemental lifetime countdown (TiempoExistencia)
        // AI tick runs every 100ms. Decrement and kill when expired.
        {
            let lifetime = state.get_npc(npc_idx).map(|n| n.tiempo_existencia_ms).unwrap_or(0);
            if lifetime > 0 {
                if let Some(n) = state.get_npc_mut(npc_idx) {
                    n.tiempo_existencia_ms -= 100; // 100ms per tick
                }
                let expired = state.get_npc(npc_idx).map(|n| n.tiempo_existencia_ms <= 0).unwrap_or(false);
                if expired {
                    npc_die(state, npc_idx, 0, 0, 0, 0).await;
                    continue;
                }
            }
        }

        let npc_data = match state.get_npc(npc_idx) {
            Some(n) => (n.movement, n.hostile, n.can_attack, n.map, n.x, n.y, n.target,
                        n.lanza_spells, n.spells.len(), n.npc_type, !n.attacked_by.is_empty(),
                        n.min_hp, n.max_hp, n.alineacion, n.ataca_doble, n.npc_number),
            None => continue,
        };
        let (movement, hostile, can_attack, map, x, y, target,
             lanza_spells, spells_len, npc_type, has_attacked_by,
             cur_hp, max_hp, alineacion, ataca_doble, npc_number) = npc_data;

        // Skip NPCs on maps with no users (VB6 optimization)
        let map_users = state.map_user_counts.get(&map).copied().unwrap_or(0);
        if map_users == 0 && movement != npc::AI_FOLLOW_OWNER {
            continue;
        }

        // NPC self-heal: if HP < 50% and has a heal spell, try to cast it on self
        if max_hp > 0 && cur_hp > 0 && cur_hp < max_hp / 2 && lanza_spells > 0 {
            npc_try_self_heal(state, npc_idx).await;
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
                        // VB6 AtacaDoble: spell FIRST, then ALWAYS melee.
                        // AtacaDoble = 50% chance to SKIP the spell (melee only).
                        // Non-AtacaDoble with LanzaSpells = always spell+melee.
                        if lanza_spells > 0 && spells_len > 0 {
                            let skip_spell = ataca_doble && rand_range(0, 1) == 0;
                            if !skip_spell {
                                if let Some(spell_id) = pick_npc_spell(state, npc_idx, spells_len) {
                                    npc_cast_spell(state, npc_idx, target_conn, spell_id).await;
                                }
                            }
                        }
                        // Melee ALWAYS happens after spell
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
                            if dist > 1 && dist <= 8 && lanza_spells > 0 && can_attack && spells_len > 0 {
                                if let Some(spell_id) = pick_npc_spell(state, npc_idx, spells_len) {
                                    npc_cast_spell(state, npc_idx, target_conn, spell_id).await;
                                }
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
                let attacker_name_opt: Option<String> = if has_attacked_by {
                    state.get_npc(npc_idx).map(|n| n.attacked_by.clone())
                } else {
                    None
                };
                let attacker_conn = attacker_name_opt.as_deref()
                    .and_then(|name| find_player_by_name(state, map, x, y, name))
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
                            // VB6 AtacaDoble: spell FIRST, then ALWAYS melee.
                            // AtacaDoble = 50% chance to SKIP the spell (melee only).
                            if lanza_spells > 0 && spells_len > 0 {
                                let skip_spell = ataca_doble && rand_range(0, 1) == 0;
                                if !skip_spell {
                                    if let Some(spell_id) = pick_npc_spell(state, npc_idx, spells_len) {
                                        npc_cast_spell(state, npc_idx, target_conn, spell_id).await;
                                    }
                                }
                            }
                            // VB6: Water elemental (#92) does NOT melee users (AI_NPC.bas:451)
                            if npc_number != npc::ELEMENTAL_AGUA as usize {
                                npc_attack_user(state, npc_idx, target_conn).await;
                            }
                            if let Some(n) = state.get_npc_mut(npc_idx) {
                                n.can_attack = false;
                            }
                        } else {
                            // Cast spell if possible
                            if dist > 1 && dist <= 8 && lanza_spells > 0 && can_attack && spells_len > 0 {
                                if let Some(spell_id) = pick_npc_spell(state, npc_idx, spells_len) {
                                    npc_cast_spell(state, npc_idx, target_conn, spell_id).await;
                                }
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
                            // VB6 AtacaDoble: spell FIRST, then ALWAYS melee.
                            // AtacaDoble = 50% chance to SKIP the spell (melee only).
                            if lanza_spells > 0 && spells_len > 0 {
                                let skip_spell = ataca_doble && rand_range(0, 1) == 0;
                                if !skip_spell {
                                    if let Some(spell_id) = pick_npc_spell(state, npc_idx, spells_len) {
                                        npc_cast_spell(state, npc_idx, target_conn, spell_id).await;
                                    }
                                }
                            }
                            // Melee ALWAYS happens after spell
                            npc_attack_user(state, npc_idx, target_conn).await;
                            if let Some(n) = state.get_npc_mut(npc_idx) {
                                n.can_attack = false;
                            }
                        } else if dist > 1 && dist <= 8 && lanza_spells > 0 && can_attack && spells_len > 0 {
                            if let Some(spell_id) = pick_npc_spell(state, npc_idx, spells_len) {
                                npc_cast_spell(state, npc_idx, target_conn, spell_id).await;
                            }
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
                        // VB6: NpcObjeto -> RandomNumber(1,3) < 3 -> 2/3 chance to attack
                        if rand_range(1, 3) < 3 {
                            // VB6 AtacaDoble: spell FIRST, then ALWAYS melee.
                            // AtacaDoble = 50% chance to SKIP the spell (melee only).
                            if lanza_spells > 0 && spells_len > 0 {
                                let skip_spell = ataca_doble && rand_range(0, 1) == 0;
                                if !skip_spell {
                                    if let Some(spell_id) = pick_npc_spell(state, npc_idx, spells_len) {
                                        npc_cast_spell(state, npc_idx, target_conn, spell_id).await;
                                    }
                                }
                            }
                            // Melee ALWAYS happens after spell
                            npc_attack_user(state, npc_idx, target_conn).await;
                            if let Some(n) = state.get_npc_mut(npc_idx) {
                                n.can_attack = false;
                            }
                        }
                    } else if lanza_spells > 0 && spells_len > 0 {
                        // VB6 AiNpcObjeto: scan vision for spell targets at range
                        if let Some(target_conn) = find_nearest_player(state, map, x, y) {
                            if rand_range(1, 3) < 3 {
                                if let Some(spell_id) = pick_npc_spell(state, npc_idx, spells_len) {
                                    npc_cast_spell(state, npc_idx, target_conn, spell_id).await;
                                }
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

                        // VB6: Water elemental (#92) does NOT attack users (line 451/489 AI_NPC.bas)
                        // and has LanzaSpells=0. It's a tank that only attacks assigned NPC targets.
                        // No healing behavior exists in VB6 13.3.

                        // VB6: Elementals do NOT proactively cast spells while following.
                        // Spell casting only happens in AI_DEFENSE mode (triggered when
                        // the elemental or its owner is attacked). That mode is handled
                        // separately in the AI_DEFENSE/AI_HOSTILE_CHASE branches.

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

            npc::AI_NPC_ATACA_NPC => {
                // VB6 AiNpcAtacaNpc — NPC that targets and attacks other NPCs.
                // Used by elementals and pets assigned to fight specific NPCs.
                // Behavior:
                //   1) If has target_npc, scan vision for it → attack if adjacent, else chase
                //   2) If target not found, follow owner (if pet) or restore old movement
                let target_npc_idx = state.get_npc(npc_idx).map(|n| n.target_npc).unwrap_or(0);
                let is_fire_elemental = state.get_npc(npc_idx)
                    .map(|n| n.npc_number == npc::ELEMENTAL_FUEGO as usize)
                    .unwrap_or(false);

                if target_npc_idx > 0 {
                    // Look for target NPC in vision range
                    let found = find_target_npc_in_vision(state, map, x, y, target_npc_idx);

                    if let Some((t_idx, tx, ty)) = found {
                        let dist = (x - tx).abs() + (y - ty).abs();

                        if is_fire_elemental {
                            // Fire elemental attacks target NPC at range (VB6: NpcLanzaUnSpellSobreNpc)
                            // We use npc_attack_npc as a simplified equivalent since spell-on-NPC
                            // isn't separately implemented.
                            if can_attack {
                                npc_attack_npc(state, npc_idx, t_idx).await;
                                if let Some(n) = state.get_npc_mut(npc_idx) {
                                    n.can_attack = false;
                                }
                            }
                        } else if dist <= 1 {
                            // Adjacent — melee attack
                            npc_attack_npc(state, npc_idx, t_idx).await;
                            if let Some(n) = state.get_npc_mut(npc_idx) {
                                n.can_attack = false;
                            }
                        }

                        // Chase target if not adjacent (and not immobilized)
                        if dist > 1 {
                            let heading = chase_heading(x, y, tx, ty);
                            let (moved, ghost) = move_npc(state, npc_idx, heading);
                            if let Some(gp) = ghost { send_ghost_push(state, gp).await; }
                            if moved { send_npc_move(state, npc_idx).await; }
                        }
                    } else {
                        // Target not found in vision — follow owner if pet, else restore
                        let has_master = state.get_npc(npc_idx)
                            .map(|n| n.maestro_user.is_some())
                            .unwrap_or(false);

                        if has_master {
                            // Follow owner (same logic as AI_FOLLOW_OWNER but simplified)
                            let master_id = state.get_npc(npc_idx).and_then(|n| n.maestro_user);
                            if let Some(master_conn) = master_id {
                                let master_pos = state.users.get(&master_conn)
                                    .filter(|u| u.logged && !u.dead && u.pos_map == map)
                                    .map(|u| (u.pos_x, u.pos_y));
                                if let Some((mx, my)) = master_pos {
                                    let dist = (x - mx).abs() + (y - my).abs();
                                    if dist > 3 {
                                        let heading = chase_heading(x, y, mx, my);
                                        let (moved, ghost) = move_npc(state, npc_idx, heading);
                                        if let Some(gp) = ghost { send_ghost_push(state, gp).await; }
                                        if moved { send_npc_move(state, npc_idx).await; }
                                    }
                                }
                            }
                        } else {
                            // No master, no target — restore original AI
                            restore_old_movement(state, npc_idx);
                        }
                    }
                } else {
                    // No target assigned — follow owner or restore
                    let has_master = state.get_npc(npc_idx)
                        .map(|n| n.maestro_user.is_some())
                        .unwrap_or(false);
                    if has_master {
                        let master_id = state.get_npc(npc_idx).and_then(|n| n.maestro_user);
                        if let Some(master_conn) = master_id {
                            let master_pos = state.users.get(&master_conn)
                                .filter(|u| u.logged && !u.dead && u.pos_map == map)
                                .map(|u| (u.pos_x, u.pos_y));
                            if let Some((mx, my)) = master_pos {
                                let dist = (x - mx).abs() + (y - my).abs();
                                if dist > 3 {
                                    let heading = chase_heading(x, y, mx, my);
                                    let (moved, ghost) = move_npc(state, npc_idx, heading);
                                    if let Some(gp) = ghost { send_ghost_push(state, gp).await; }
                                    if moved { send_npc_move(state, npc_idx).await; }
                                }
                            }
                        }
                    } else {
                        restore_old_movement(state, npc_idx);
                    }
                }
            }

            npc::AI_GUERRERO_PRETORIANO => {
                // VB6 PRGUER_AI — Pretoriano warrior: chase nearest player, melee attack.
                // Scans vision for closest player, chases and attacks.
                // Returns to spawn area if too far from origin.

                // Attack adjacent players first
                if can_attack {
                    if let Some((target_conn, _adj_heading)) = find_adjacent_player(state, map, x, y) {
                        npc_attack_user(state, npc_idx, target_conn).await;
                        if let Some(n) = state.get_npc_mut(npc_idx) {
                            n.can_attack = false;
                        }
                    }
                }

                // Chase nearest player
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
                        if dist > 1 {
                            let heading = chase_heading(x, y, tx, ty);
                            let (moved, ghost) = move_npc(state, npc_idx, heading);
                            if let Some(gp) = ghost { send_ghost_push(state, gp).await; }
                            if moved { send_npc_move(state, npc_idx).await; }
                        }
                    } else {
                        // Target gone
                        if let Some(n) = state.get_npc_mut(npc_idx) { n.target = None; }
                    }
                } else {
                    // No target — return toward spawn
                    let (ox, oy) = state.get_npc(npc_idx)
                        .map(|n| (n.orig_x, n.orig_y))
                        .unwrap_or((x, y));
                    let dist_to_origin = (x - ox).abs() + (y - oy).abs();
                    if dist_to_origin > 3 {
                        let heading = chase_heading(x, y, ox, oy);
                        let (moved, ghost) = move_npc(state, npc_idx, heading);
                        if let Some(gp) = ghost { send_ghost_push(state, gp).await; }
                        if moved { send_npc_move(state, npc_idx).await; }
                    }
                }
            }

            npc::AI_CAZADOR_PRETORIANO => {
                // VB6 Cazador — ranged pretoriano: prefers spells at distance, melee if adjacent.
                // Similar to guerrero but uses spells at range.

                let mut attacked = false;

                if can_attack {
                    if let Some((target_conn, _adj_heading)) = find_adjacent_player(state, map, x, y) {
                        npc_attack_user(state, npc_idx, target_conn).await;
                        if let Some(n) = state.get_npc_mut(npc_idx) {
                            n.can_attack = false;
                        }
                        attacked = true;
                    }
                }

                if !attacked {
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

                            // Cast spell at range
                            if dist > 1 && dist <= 8 && lanza_spells > 0 && can_attack && spells_len > 0 {
                                if let Some(spell_id) = pick_npc_spell(state, npc_idx, spells_len) {
                                    npc_cast_spell(state, npc_idx, target_conn, spell_id).await;
                                }
                                if let Some(n) = state.get_npc_mut(npc_idx) {
                                    n.can_attack = false;
                                }
                            }

                            // Chase if not adjacent
                            if dist > 1 {
                                let heading = chase_heading(x, y, tx, ty);
                                let (moved, ghost) = move_npc(state, npc_idx, heading);
                                if let Some(gp) = ghost { send_ghost_push(state, gp).await; }
                                if moved { send_npc_move(state, npc_idx).await; }
                            }
                        } else {
                            if let Some(n) = state.get_npc_mut(npc_idx) { n.target = None; }
                        }
                    } else {
                        // No target — return toward spawn
                        let (ox, oy) = state.get_npc(npc_idx)
                            .map(|n| (n.orig_x, n.orig_y))
                            .unwrap_or((x, y));
                        let dist_to_origin = (x - ox).abs() + (y - oy).abs();
                        if dist_to_origin > 3 {
                            let heading = chase_heading(x, y, ox, oy);
                            let (moved, ghost) = move_npc(state, npc_idx, heading);
                            if let Some(gp) = ghost { send_ghost_push(state, gp).await; }
                            if moved { send_npc_move(state, npc_idx).await; }
                        }
                    }
                }
            }

            npc::AI_MAGO_PRETORIANO => {
                // VB6 Mago Pretoriano — spell caster: prioritizes spells, paralyzes pets,
                // attacks players with offensive spells at range.

                let mut acted = false;

                // First: try to paralyze adjacent pet NPCs
                if can_attack && lanza_spells > 0 && spells_len > 0 {
                    if let Some((pet_idx, _, _)) = find_nearest_hostile_npc(state, map, x, y, npc_idx) {
                        let pet_pos = state.get_npc(pet_idx).map(|n| (n.x, n.y));
                        if let Some((px, py)) = pet_pos {
                            let dist = (x - px).abs() + (y - py).abs();
                            if dist <= 8 {
                                // Attack pet NPC
                                npc_attack_npc(state, npc_idx, pet_idx).await;
                                if let Some(n) = state.get_npc_mut(npc_idx) {
                                    n.can_attack = false;
                                }
                                acted = true;
                            }
                        }
                    }
                }

                // Then: cast offensive spells on players
                if !acted && can_attack && lanza_spells > 0 && spells_len > 0 {
                    let spell_target = target.or_else(|| find_nearest_player(state, map, x, y));
                    if let Some(target_conn) = spell_target {
                        if let Some(n) = state.get_npc_mut(npc_idx) {
                            n.target = Some(target_conn);
                        }
                        let target_pos = state.users.get(&target_conn)
                            .filter(|u| u.logged && !u.dead && u.pos_map == map)
                            .map(|u| (u.pos_x, u.pos_y));
                        if let Some((tx, ty)) = target_pos {
                            let dist = (x - tx).abs() + (y - ty).abs();
                            if dist <= 8 {
                                if let Some(spell_id) = pick_npc_spell(state, npc_idx, spells_len) {
                                    npc_cast_spell(state, npc_idx, target_conn, spell_id).await;
                                }
                                if let Some(n) = state.get_npc_mut(npc_idx) {
                                    n.can_attack = false;
                                }
                            }
                        } else {
                            if let Some(n) = state.get_npc_mut(npc_idx) { n.target = None; }
                        }
                    }
                }

                // Movement: stay near spawn, avoid melee range of players
                let (ox, oy) = state.get_npc(npc_idx)
                    .map(|n| (n.orig_x, n.orig_y))
                    .unwrap_or((x, y));
                let dist_to_origin = (x - ox).abs() + (y - oy).abs();
                if dist_to_origin > 5 {
                    let heading = chase_heading(x, y, ox, oy);
                    let (moved, ghost) = move_npc(state, npc_idx, heading);
                    if let Some(gp) = ghost { send_ghost_push(state, gp).await; }
                    if moved { send_npc_move(state, npc_idx).await; }
                }
            }

            npc::AI_SACERDOTE_PRETORIANO => {
                // VB6 PRCLER_AI — Sacerdote Pretoriano: healer/support.
                // Priority: 1) unparalyze allies, 2) paralyze enemy pets, 3) attack players with spells,
                //           4) heal wounded allies.

                let mut acted = false;

                if can_attack && lanza_spells > 0 && spells_len > 0 {
                    // Priority 1: unparalyze allied pretoriano
                    if let Some((ally_idx, _ax, _ay)) = find_paralyzed_pretoriano_ally(state, map, x, y, npc_idx) {
                        // Remove paralysis from ally (20% effectiveness like VB6 king)
                        if rand_range(1, 100) <= 20 {
                            if let Some(ally) = state.get_npc_mut(ally_idx) {
                                ally.paralyzed = false;
                                ally.counter_paralisis = 0;
                            }
                        }
                        if let Some(n) = state.get_npc_mut(npc_idx) {
                            n.can_attack = false;
                        }
                        acted = true;
                    }

                    // Priority 2: paralyze enemy pets
                    if !acted {
                        if let Some((pet_idx, px, py)) = find_nearest_hostile_npc(state, map, x, y, npc_idx) {
                            let dist = (x - px).abs() + (y - py).abs();
                            if dist <= 8 {
                                // Paralyze the pet
                                if let Some(pet) = state.get_npc_mut(pet_idx) {
                                    if !pet.paralyzed {
                                        pet.paralyzed = true;
                                        pet.counter_paralisis = 40; // ~4 seconds
                                    }
                                }
                                if let Some(n) = state.get_npc_mut(npc_idx) {
                                    n.can_attack = false;
                                }
                                acted = true;
                            }
                        }
                    }

                    // Priority 3: attack players with spells
                    if !acted {
                        let spell_target = target.or_else(|| find_nearest_player(state, map, x, y));
                        if let Some(target_conn) = spell_target {
                            if let Some(n) = state.get_npc_mut(npc_idx) {
                                n.target = Some(target_conn);
                            }
                            let target_pos = state.users.get(&target_conn)
                                .filter(|u| u.logged && !u.dead && u.pos_map == map)
                                .map(|u| (u.pos_x, u.pos_y));
                            if let Some((tx, ty)) = target_pos {
                                let dist = (x - tx).abs() + (y - ty).abs();
                                if dist <= 8 {
                                    if let Some(spell_id) = pick_npc_spell(state, npc_idx, spells_len) {
                                        npc_cast_spell(state, npc_idx, target_conn, spell_id).await;
                                    }
                                    if let Some(n) = state.get_npc_mut(npc_idx) {
                                        n.can_attack = false;
                                    }
                                    acted = true;
                                }
                            } else {
                                if let Some(n) = state.get_npc_mut(npc_idx) { n.target = None; }
                            }
                        }
                    }

                    // Priority 4: heal wounded allies
                    if !acted {
                        if let Some((ally_idx, _ax, _ay)) = find_wounded_pretoriano_ally(state, map, x, y, npc_idx) {
                            // Heal: use existing npc_try_self_heal-style logic but on ally
                            let heal_amount = 30; // VB6: NPCcuraNPC heals 30 HP
                            if let Some(ally) = state.get_npc_mut(ally_idx) {
                                ally.min_hp = (ally.min_hp + heal_amount).min(ally.max_hp);
                            }
                            if let Some(n) = state.get_npc_mut(npc_idx) {
                                n.can_attack = false;
                            }
                        }
                    }
                }

                // Movement: stay near spawn (close to king)
                let (ox, oy) = state.get_npc(npc_idx)
                    .map(|n| (n.orig_x, n.orig_y))
                    .unwrap_or((x, y));
                let dist_to_origin = (x - ox).abs() + (y - oy).abs();
                if dist_to_origin > 4 {
                    let heading = chase_heading(x, y, ox, oy);
                    let (moved, ghost) = move_npc(state, npc_idx, heading);
                    if let Some(gp) = ghost { send_ghost_push(state, gp).await; }
                    if moved { send_npc_move(state, npc_idx).await; }
                }
            }

            npc::AI_REY_PRETORIANO => {
                // VB6 PRREY_AI — Rey Pretoriano (King):
                // While allies exist: heal self, unparalyze allies, cure poison, heal allies.
                // When alone (no allies): chase + melee attack players directly (speed hack — can_attack reset).

                let allies_exist = has_pretoriano_allies(state, map, x, y, npc_idx);

                if allies_exist {
                    // King heals self to full while allies are alive
                    if let Some(n) = state.get_npc_mut(npc_idx) {
                        n.min_hp = n.max_hp;
                    }

                    if can_attack && lanza_spells > 0 && spells_len > 0 {
                        // Priority 1: unparalyze allied pretoriano (20% chance)
                        if let Some((ally_idx, _ax, _ay)) = find_paralyzed_pretoriano_ally(state, map, x, y, npc_idx) {
                            if rand_range(1, 100) <= 20 {
                                if let Some(ally) = state.get_npc_mut(ally_idx) {
                                    ally.paralyzed = false;
                                    ally.counter_paralisis = 0;
                                }
                            }
                            if let Some(n) = state.get_npc_mut(npc_idx) {
                                n.can_attack = false;
                            }
                        } else if let Some((ally_idx, _ax, _ay)) = find_wounded_pretoriano_ally(state, map, x, y, npc_idx) {
                            // Priority 2: heal wounded allies
                            let heal_amount = 5; // VB6: king heals +5 (CuraLeves)
                            if let Some(ally) = state.get_npc_mut(ally_idx) {
                                ally.min_hp = (ally.min_hp + heal_amount).min(ally.max_hp);
                            }
                            if let Some(n) = state.get_npc_mut(npc_idx) {
                                n.can_attack = false;
                            }
                        }
                    }

                    // King doesn't move aggressively while allies exist — stays back
                    let (ox, oy) = state.get_npc(npc_idx)
                        .map(|n| (n.orig_x, n.orig_y))
                        .unwrap_or((x, y));
                    let dist_to_origin = (x - ox).abs() + (y - oy).abs();
                    if dist_to_origin > 2 {
                        let heading = chase_heading(x, y, ox, oy);
                        let (moved, ghost) = move_npc(state, npc_idx, heading);
                        if let Some(gp) = ghost { send_ghost_push(state, gp).await; }
                        if moved { send_npc_move(state, npc_idx).await; }
                    }
                } else {
                    // King is alone — all allies dead. Enter berserker mode.
                    // VB6: special speed ability — CanAttack reset to 1 after each attack.

                    // Attack adjacent players (check all 4 directions)
                    let mut did_attack = false;
                    if can_attack {
                        if let Some((target_conn, _adj_heading)) = find_adjacent_player(state, map, x, y) {
                            npc_attack_user(state, npc_idx, target_conn).await;
                            // VB6: special speed ability — can attack again immediately
                            if let Some(n) = state.get_npc_mut(npc_idx) {
                                n.can_attack = true; // NOT a bug — VB6 comment says so!
                            }
                            did_attack = true;
                        }
                    }

                    // Chase nearest player or cast spells if not adjacent
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

                            // Cast debuff spells at range if can't reach
                            if dist > 1 && dist <= 8 && lanza_spells > 0 && spells_len > 0 && !did_attack {
                                if let Some(spell_id) = pick_npc_spell(state, npc_idx, spells_len) {
                                    npc_cast_spell(state, npc_idx, target_conn, spell_id).await;
                                }
                            }

                            // Chase aggressively
                            if dist > 1 {
                                let heading = chase_heading(x, y, tx, ty);
                                let (moved, ghost) = move_npc(state, npc_idx, heading);
                                if let Some(gp) = ghost { send_ghost_push(state, gp).await; }
                                if moved { send_npc_move(state, npc_idx).await; }
                            }
                        } else {
                            if let Some(n) = state.get_npc_mut(npc_idx) { n.target = None; }
                        }
                    } else {
                        // No targets — heal self and return to spawn
                        if cur_hp < max_hp {
                            npc_try_self_heal(state, npc_idx).await;
                        }
                        let (ox, oy) = state.get_npc(npc_idx)
                            .map(|n| (n.orig_x, n.orig_y))
                            .unwrap_or((x, y));
                        let dist_to_origin = (x - ox).abs() + (y - oy).abs();
                        if dist_to_origin > 2 {
                            let heading = chase_heading(x, y, ox, oy);
                            let (moved, ghost) = move_npc(state, npc_idx, heading);
                            if let Some(gp) = ghost { send_ghost_push(state, gp).await; }
                            if moved { send_npc_move(state, npc_idx).await; }
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

/// Pick a random spell ID from an NPC's spell list without cloning the whole Vec.
/// Returns `None` if the NPC no longer exists or has no spells.
fn pick_npc_spell(state: &GameState, npc_idx: usize, spells_len: usize) -> Option<i32> {
    if spells_len == 0 {
        return None;
    }
    let spell_idx = (rand_range(0, spells_len as i32 - 1) as usize).min(spells_len.saturating_sub(1));
    state.get_npc(npc_idx).and_then(|n| n.spells.get(spell_idx).copied())
}

/// NPC self-heal: check if any spell has SubeHP=1 and cast it on self.
pub(crate) async fn npc_try_self_heal(state: &mut GameState, npc_idx: usize) {
    let spell_ids: Vec<i32> = match state.get_npc(npc_idx) {
        Some(n) => n.spells.clone(),
        None => return,
    };
    for &spell_id in &spell_ids {
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
                state.send_data_bytes(SendTarget::ToArea { map, x: nx, y: ny }, &pkt);
            }
            if spell.wav > 0 {
                let pkt = binary_packets::write_play_wave(spell.wav as u8, nx as i16, ny as i16);
                state.send_data_bytes(SendTarget::ToArea { map, x: nx, y: ny }, &pkt);
            }

            // Magic words
            if !spell.palabras_magicas.is_empty() {
                state.send_chat_over_head_to(SendTarget::ToArea { map, x: nx, y: ny }, &format!("{} dice: {}", npc_name, spell.palabras_magicas), npc_char.0 as i16, 255);
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
            state.send_data_bytes(SendTarget::ToArea { map, x, y }, &cc_pkt);
        }
    }
}
