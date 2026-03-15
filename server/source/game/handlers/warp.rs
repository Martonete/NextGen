//! Warp/visibility handlers: warp_user, make_user_visible, check_update_needed_user,
//! mover_casper, warp_mascotas, send_warp_fx.
//! Extracted from mod.rs to reduce file size.

use crate::net::ConnectionId;
use crate::protocol::binary_packets;
use crate::game::types::{GameState, SendTarget};
use crate::game::world;
use super::common::*;
use super::build_cd_binary;

// =====================================================================
// World/visibility helpers
// =====================================================================

/// Make a newly-logged-in user visible: send existing chars to them, and their CC to others.
pub(crate) async fn make_user_visible(state: &mut GameState, conn_id: ConnectionId) {
    // Reset area tracking so CheckUpdateNeededUser fires with USER_NUEVO (255)
    if let Some(user) = state.users.get_mut(&conn_id) {
        user.area_id = 0;
        user.area_min_x = 0;
        user.area_min_y = 0;
    }

    // Trigger full area initialization (heading=255 = USER_NUEVO)
    // This sends CA packet + all CCs/NPCs/items in the 27x27 area
    check_update_needed_user(state, conn_id, 255).await;
}

/// CheckUpdateNeededUser — VB6 ModAreas.bas area-based visibility system.
/// Only fires when the player crosses a 9x9 area boundary.
/// Sends CA packet to client (cleanup out-of-range entities),
/// then sends CC/NPC/items for the newly visible strip.
pub(crate) async fn check_update_needed_user(
    state: &mut GameState,
    conn_id: ConnectionId,
    heading: i32,
) {
    let (pos_x, pos_y, map, old_area_id, old_min_x, old_min_y) = match state.users.get(&conn_id) {
        Some(u) => (u.pos_x, u.pos_y, u.pos_map, u.area_id, u.area_min_x, u.area_min_y),
        None => return,
    };

    let new_area_id = area_id(pos_x, pos_y);

    // If still in the same 9x9 zone, nothing to do
    if new_area_id == old_area_id && old_area_id != 0 {
        return;
    }

    // Calculate the new visibility strip based on heading (VB6 ModAreas lines 158-198)
    let (min_x, max_x, min_y, max_y, new_min_x, new_min_y) = if heading == 255 {
        // USER_NUEVO (login/warp): full 27x27 area
        let nmin_y = (pos_y / 9 - 1) * 9;
        let nmin_x = (pos_x / 9 - 1) * 9;
        (nmin_x, nmin_x + 26, nmin_y, nmin_y + 26, nmin_x, nmin_y)
    } else if heading == world::HEADING_NORTH {
        let nmin_x = old_min_x;
        let nmin_y = old_min_y - 9;
        (nmin_x, nmin_x + 26, nmin_y, old_min_y - 1, nmin_x, nmin_y)
    } else if heading == world::HEADING_SOUTH {
        let nmin_x = old_min_x;
        let nmin_y = old_min_y + 27;
        let new_area_min_y = old_min_y + 9; // VB6: MinY - 18 but MinY was old+27, so net = old+9
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
        return;
    };

    // Clamp to map bounds
    let min_x = min_x.max(1);
    let min_y = min_y.max(1);
    let max_x = max_x.min(100);
    let max_y = max_y.min(100);

    // Update user's area tracking
    if let Some(user) = state.users.get_mut(&conn_id) {
        user.area_id = new_area_id;
        user.area_min_x = new_min_x;
        user.area_min_y = new_min_y;
    }

    // Send AreaChanged packet — tells client to erase out-of-range entities
    state.send_bytes(conn_id, &binary_packets::write_area_changed(pos_x as i16, pos_y as i16));

    // Build our CC (binary) for sending to newly visible users
    let my_cc = match state.users.get(&conn_id) {
        Some(u) => u.build_cc_binary(),
        None => return,
    };

    // Collect users, NPCs, ground items, particles, and lights in the new strip
    // (collect first to avoid borrow conflicts)
    let mut new_users: Vec<ConnectionId> = Vec::new();
    let mut new_npcs: Vec<usize> = Vec::new();
    let mut new_items: Vec<(i32, i32, i32)> = Vec::new(); // (grh, x, y)
    let mut new_door_bqs: Vec<(i32, i32, bool)> = Vec::new(); // (x, y, blocked) for door tiles
    let mut new_particles: Vec<(i16, i32, i32)> = Vec::new(); // (particle_group_index, x, y)
    let mut new_lights: Vec<(i32, i32, i16, i16, i16, i16)> = Vec::new(); // (x, y, range, r, g, b)

    if let Some(grid) = state.world.grid(map) {
        for sx in min_x..=max_x {
            for sy in min_y..=max_y {
                if let Some(tile) = grid.tile(sx, sy) {
                    if let Some(other_conn) = tile.user_conn {
                        if other_conn != conn_id {
                            new_users.push(other_conn);
                        }
                    }
                    if tile.npc_index > 0 {
                        new_npcs.push(tile.npc_index as usize);
                    }
                    if tile.ground_item.obj_index > 0 {
                        // Look up GRH for the object
                        if let Some(obj) = state.get_object(tile.ground_item.obj_index) {
                            new_items.push((obj.grh_index, sx, sy));
                        }
                    }
                }
            }
        }
    }

    // Collect particles, lights, and static .inf objects from static map data
    let map_idx = map as usize;
    if let Some(Some(game_map)) = state.game_data.maps.get(map_idx) {
        for sx in min_x..=max_x {
            for sy in min_y..=max_y {
                if let Some(tile) = game_map.tiles.get((sx - 1) as usize, (sy - 1) as usize) {
                    if tile.particle_group_index > 0 {
                        new_particles.push((tile.particle_group_index, sx, sy));
                    }
                    if tile.range_light > 0 {
                        new_lights.push((sx, sy, tile.range_light, tile.rgb_light[0], tile.rgb_light[1], tile.rgb_light[2]));
                    }
                    // VB6: Send HO for static .inf objects (doors, furniture, etc.)
                    // The client can't resolve ObjIndex→GRH, so server must send HO.
                    let oi = tile.obj.obj_index as usize;
                    if oi >= 1 {
                        if let Some(obj) = state.game_data.objects.get(oi - 1) {
                            if obj.grh_index > 0 {
                                new_items.push((obj.grh_index, sx, sy));
                            }

                            // VB6 ModAreas.bas:273-300 — send BQ for door tiles + adjacent tiles
                            // This ensures correct blocked state regardless of what .map file says
                            if obj.obj_type == crate::data::objects::ObjType::Door {
                                let blocked_at = |ty: i32, tx: i32| -> bool {
                                    game_map.tiles.get((tx - 1) as usize, (ty - 1) as usize)
                                        .map(|t| t.blocked)
                                        .unwrap_or(false)
                                };

                                // Always send BQ for door tile + x-1 (single door minimum)
                                new_door_bqs.push((sx, sy, blocked_at(sy, sx)));
                                new_door_bqs.push((sx - 1, sy, blocked_at(sy, sx - 1)));

                                if obj.puerta_doble == 1 {
                                    new_door_bqs.push((sx + 1, sy, blocked_at(sy, sx + 1)));
                                    new_door_bqs.push((sx + 2, sy, blocked_at(sy, sx + 2)));
                                } else if obj.porton == 1 || obj.reja_forta == 1 {
                                    for dx in [-2i32, -1, 0, 1, 2] {
                                        new_door_bqs.push((sx + dx, sy, blocked_at(sy, sx + dx)));
                                    }
                                }

                            }
                        }
                    }
                }
            }
        }
    }

    // Get self char_index and invisible/hidden flags for [CD and NOVER
    let (my_char_idx, my_invisible, my_privileges) = match state.users.get(&conn_id) {
        Some(u) => (u.char_index.0, u.admin_invisible || u.invisible || u.hidden, u.privileges),
        None => return,
    };

    // Send mutual CC + [CD + NOVER to newly visible users
    // Clanmates can see each other even when invisible/hidden.
    for other_id in new_users {
        if let Some(other) = state.users.get(&other_id) {
            if other.logged {
                let other_cc = other.build_cc_binary();
                let other_char_idx = other.char_index.0;
                let other_invisible = other.admin_invisible || other.invisible || other.hidden;
                let other_cd = build_cd_binary(other);
                let are_clanmates = same_clan(state, conn_id, other_id);
                state.send_bytes(conn_id, &other_cc);
                state.send_bytes(conn_id, &other_cd);
                // If the other player is invisible, tell us not to render them (unless clanmates)
                if other_invisible && !are_clanmates {
                    state.send_bytes(conn_id, &binary_packets::write_set_invisible(other_char_idx as i16, true, 0));
                }
                // Send our CC + [CD to them
                state.send_bytes(other_id, &my_cc);
                let my_cd = match state.users.get(&conn_id) {
                    Some(u) => build_cd_binary(u),
                    None => continue,
                };
                state.send_bytes(other_id, &my_cd);
                // If we are invisible, tell them not to render us (unless clanmates)
                if my_invisible && !are_clanmates {
                    state.send_bytes(other_id, &binary_packets::write_set_invisible(my_char_idx as i16, true, 0));
                }
            }
        } else {
            state.send_bytes(other_id, &my_cc);
        }
    }

    // Send NPC CCs
    for npc_idx in new_npcs {
        let npc_cc = match state.get_npc(npc_idx) {
            Some(npc) if npc.active => npc.build_cc_binary(),
            _ => continue,
        };
        state.send_bytes(conn_id, &npc_cc);
    }

    // Send ground items (HO = ObjectCreate packet) — VB6 ModAreas.bas line 264
    for (grh, ix, iy) in new_items {
        state.send_bytes(conn_id, &binary_packets::write_object_create(ix as i16, iy as i16, grh as i16));
    }

    // Send door BQ packets (BlockPosition) — VB6 ModAreas.bas lines 273-300
    for (bx, by, blocked) in new_door_bqs {
        state.send_bytes(conn_id, &binary_packets::write_block_position(bx as i16, by as i16, blocked));
    }

    // Send particle effects (PCF) — VB6 ModAreas.bas line 255
    for (pg, px, py) in new_particles {
        state.send_bytes(conn_id, &binary_packets::write_particle_create(pg, px as i16, py as i16, 0));
    }

    // Send lighting effects (PCL) — VB6 ModAreas.bas line 259
    for (lx, ly, range, r, g, b) in new_lights {
        state.send_bytes(conn_id, &binary_packets::write_light_create(lx as i16, ly as i16, range as u8, r as u8, g as u8, b as u8));
    }
}

/// Warp a user to a new map/position (map transition).
/// Matches VB6 WarpUserChar: BKW, QDL, EraseUserChar, CM/XM/N~, MakeUserChar, PU, BKW.
/// VB6: WarpMascotas — teleport user's pets to the new map.
/// Persistent pets are removed from old map and respawned at master's new position.
pub(crate) async fn warp_mascotas(state: &mut GameState, owner_conn: ConnectionId, new_map: i32, new_x: i32, new_y: i32) {
    use crate::game::npc::{ELEMENTAL_AGUA, ELEMENTAL_FUEGO, ELEMENTAL_TIERRA, AI_FOLLOW_OWNER};

    let pets = match state.users.get(&owner_conn) {
        Some(u) => (u.mascotas_index, u.mascotas_type, u.nro_mascotas),
        None => return,
    };
    let (pet_indices, pet_types, _nro) = pets;

    // Collect pet info before mutation
    let mut pets_to_move: Vec<(usize, i32)> = Vec::new(); // (slot_index, npc_type)
    for i in 0..3 {
        let idx = pet_indices[i];
        if idx == 0 { continue; }
        let npc_type = pet_types[i];
        if npc_type <= 0 { continue; }

        // Check if pet is alive and on the OLD map (not already on the new map)
        let pet_alive = state.get_npc(idx).map(|n| n.is_alive() && n.map != new_map).unwrap_or(false);
        if pet_alive {
            pets_to_move.push((i, npc_type));

            // Remove old NPC from world (send BP to area)
            let old_data = state.get_npc(idx).map(|n| (n.char_index, n.map, n.x, n.y));
            if let Some((ci, omap, ox, oy)) = old_data {
                state.send_data_bytes(
                    SendTarget::ToArea { map: omap, x: ox, y: oy },
                    &binary_packets::write_character_remove(ci.0 as i16),
                );
            }
            state.kill_npc(idx);

            // Clear old slot
            if let Some(user) = state.users.get_mut(&owner_conn) {
                user.mascotas_index[i] = 0;

                user.mascotas_type[i] = 0;
                user.nro_mascotas = (user.nro_mascotas - 1).max(0);
            }
        }
    }

    // Respawn pets at new position
    for (slot, npc_type) in pets_to_move {
        if let Some(new_idx) = state.spawn_npc(npc_type as usize, new_map, new_x, new_y) {
            // Link to owner
            if let Some(npc) = state.get_npc_mut(new_idx) {
                npc.maestro_user = Some(owner_conn);
                npc.movement = AI_FOLLOW_OWNER;
            }

            // Update user tracking
            if let Some(user) = state.users.get_mut(&owner_conn) {
                user.mascotas_index[slot] = new_idx;

                user.mascotas_type[slot] = npc_type;
                user.nro_mascotas = user.nro_mascotas + 1;

                // Restore elemental flags
                match npc_type {
                    ELEMENTAL_AGUA => user.ele_de_agua = true,
                    ELEMENTAL_FUEGO => user.ele_de_fuego = true,
                    ELEMENTAL_TIERRA => user.ele_de_tierra = true,
                    _ => {}
                }
            }

            // Broadcast new NPC to area
            let cc_pkt = state.get_npc(new_idx).map(|n| n.build_cc_binary());
            if let Some(pkt) = cc_pkt {
                state.send_data_bytes(SendTarget::ToArea { map: new_map, x: new_x, y: new_y }, &pkt);
            }
        }
    }
}

/// VB6 "Mover Casper": push a dead user off a tile so a living user/NPC can occupy it.
/// Tries the mover's heading direction first, then S, N, E, W as fallback.
/// If no free adjacent tile is found, the ghost stays (movement will be rejected by is_legal_pos).
pub(crate) async fn mover_casper(state: &mut GameState, map: i32, x: i32, y: i32, mover_heading: i32) {
    // Check if there's a user on the target tile
    let ghost_conn = match state.world.grid(map)
        .and_then(|g| g.tile(x, y))
        .and_then(|t| t.user_conn) {
        Some(c) => c,
        None => return, // no user on tile
    };

    // Only push if that user is dead
    let is_ghost = state.users.get(&ghost_conn).map(|u| u.dead).unwrap_or(false);
    if !is_ghost {
        return;
    }

    let ghost_char_index = match state.users.get(&ghost_conn) {
        Some(u) => u.char_index,
        None => return,
    };

    // VB6 MoverCasper: try heading direction first, then S(3), N(1), E(2), W(4) fallback
    let directions = [mover_heading, 3, 1, 2, 4];
    let mut push_x = 0i32;
    let mut push_y = 0i32;
    let mut found = false;

    for &dir in &directions {
        let (dx, dy) = world::heading_to_offset(dir);
        let nx = x + dx;
        let ny = y + dy;
        if !world::in_map_bounds(nx, ny) { continue; }
        if state.is_tile_blocked(map, nx, ny) { continue; }
        let tile_free = state.world.grid(map)
            .map(|g| g.is_tile_free(nx, ny))
            .unwrap_or(false);
        if tile_free {
            push_x = nx;
            push_y = ny;
            found = true;
            break;
        }
    }

    if !found {
        return; // no free tile — ghost stays, movement will be blocked
    }

    // Move ghost: update grid
    state.world.remove_user(map, x, y);
    state.world.place_user(map, push_x, push_y, ghost_conn);

    // Update ghost user position
    if let Some(ghost) = state.users.get_mut(&ghost_conn) {
        ghost.pos_x = push_x;
        ghost.pos_y = push_y;
    }

    // Send position update to ghost (PU) and movement broadcast to area
    state.send_bytes(ghost_conn, &binary_packets::write_pos_update(push_x as i16, push_y as i16));
    state.send_data_bytes(
        SendTarget::ToAreaButIndex { conn_id: ghost_conn, map, x: push_x, y: push_y },
        &binary_packets::write_character_move(ghost_char_index.0 as i16, push_x as i16, push_y as i16),
    );
}

/// Warp variant that skips find_free_pos — places the user exactly at (x,y)
/// even if blocked/occupied. Used for GM teleport.
pub(crate) async fn warp_user_exact(state: &mut GameState, conn_id: ConnectionId, new_map: i32, new_x: i32, new_y: i32) {
    warp_user_inner(state, conn_id, new_map, new_x, new_y, true).await;
}

pub(crate) async fn warp_user(state: &mut GameState, conn_id: ConnectionId, new_map: i32, new_x: i32, new_y: i32) {
    warp_user_inner(state, conn_id, new_map, new_x, new_y, false).await;
}

pub(crate) async fn warp_user_inner(state: &mut GameState, conn_id: ConnectionId, new_map: i32, new_x: i32, new_y: i32, exact: bool) {
    let old_data = match state.users.get(&conn_id) {
        Some(u) => (u.pos_map, u.pos_x, u.pos_y, u.char_index, u.area_min_x, u.area_min_y),
        None => return,
    };
    let (old_map, old_x, old_y, char_index, area_min_x, area_min_y) = old_data;

    // 1. BKW — fade to black (VB6 line 2262)
    state.send_bytes(conn_id, &binary_packets::write_pause_toggle());

    // 2. QDL + BP — remove dialog and character from the full 27×27 area.
    // VB6 ToPCArea uses the 27×27 zone group, not just the viewport (±8x, ±6y).
    // Movement `+` packets are broadcast to the full 27×27 area, so QDL/BP must
    // reach the same players — otherwise ghosts remain visible at the teleport tile.
    let qdl_pkt = binary_packets::write_remove_char_dialog(char_index.0 as i16);
    let bp_pkt = binary_packets::write_character_remove(char_index.0 as i16);
    state.send_bytes(conn_id, &binary_packets::write_remove_dialogs());

    if area_min_x > 0 || area_min_y > 0 {
        let amx = area_min_x.max(1);
        let amy = area_min_y.max(1);
        let axx = (area_min_x + 26).min(100);
        let axy = (area_min_y + 26).min(100);
        if let Some(grid) = state.world.grid(old_map) {
            let mut targets: Vec<ConnectionId> = Vec::new();
            for sy in amy..=axy {
                for sx in amx..=axx {
                    if let Some(tile) = grid.tile(sx, sy) {
                        if let Some(c) = tile.user_conn {
                            if c != conn_id { targets.push(c); }
                        }
                    }
                }
            }
            for c in &targets {
                state.send_bytes(*c, &qdl_pkt);
            }
            for c in &targets {
                state.send_bytes(*c, &bp_pkt);
            }
        }
    } else {
        // Fallback: area not initialized yet — send to entire map (safe catch-all)
        state.send_data_bytes(
            SendTarget::ToMapButIndex { conn_id, map: old_map },
            &qdl_pkt,
        );
        state.send_data_bytes(
            SendTarget::ToMapButIndex { conn_id, map: old_map },
            &bp_pkt,
        );
    }
    state.world.remove_user(old_map, old_x, old_y);

    // Cancel resting and meditating on map change (VB6: WarpUserChar)
    let was_meditating = state.users.get(&conn_id).map(|u| u.meditating).unwrap_or(false);
    if let Some(user) = state.users.get_mut(&conn_id) {
        user.resting = false;
        if user.meditating {
            user.meditating = false;
        }
    }
    // VB6: Send meditate toggle to client so it clears the meditation UI state
    if was_meditating {
        state.send_bytes(conn_id, &binary_packets::write_meditate_toggle());
    }

    // VB6: Remove invisibility when entering InviSinEfecto map
    let invi_blocked = state.game_data.maps.get(new_map as usize)
        .and_then(|m| m.as_ref())
        .map(|m| m.info.invi_sin_efecto)
        .unwrap_or(false);
    if invi_blocked {
        let was_invis = state.users.get(&conn_id)
            .map(|u| u.invisible && !u.admin_invisible)
            .unwrap_or(false);
        if was_invis {
            if let Some(user) = state.users.get_mut(&conn_id) {
                user.invisible = false;
                user.hidden = false;
                user.counter_invisible = 0;
                user.counter_oculto = 0;
            }
        }
    }

    // VB6: Remove mimetizado on map change (revert appearance)
    let was_mimetizado = state.users.get(&conn_id).map(|u| u.mimetizado).unwrap_or(false);
    if was_mimetizado {
        if let Some(user) = state.users.get_mut(&conn_id) {
            let b = user.char_mimetizado_body;
            let h = user.char_mimetizado_head;
            user.body = b;
            user.head = h;
            user.weapon_anim = user.char_mimetizado_weapon;
            user.shield_anim = user.char_mimetizado_shield;
            user.casco_anim = user.char_mimetizado_helmet;
            user.mimetizado = false;
        }
    }

    // 4. Find a free tile if destination is occupied (VB6 DamePos)
    // GMs with exact=true skip this — they can stand on blocked tiles.
    let (final_x, final_y) = if exact { (new_x, new_y) } else { find_free_pos(state, new_map, new_x, new_y) };

    // 5. Update user position
    if let Some(user) = state.users.get_mut(&conn_id) {
        user.pos_map = new_map;
        user.pos_x = final_x;
        user.pos_y = final_y;
        // Brief NPC targeting immunity after warp — prevents phantom combat sounds
        // on arrival (NPCs in range would otherwise attack on the very next AI tick)
        user.warp_immunity_ticks = 25; // ~1 second at 40ms/tick — prevents phantom sounds on arrival
    }

    // 6. Place on new grid
    state.world.place_user(new_map, final_x, final_y, conn_id);

    // 7. Send map change packets (only if map changed, but we send always for safety)
    let map_idx = new_map as usize;
    let (r, g, b, music, map_name) = if let Some(Some(game_map)) = state.game_data.maps.get(map_idx) {
        (
            game_map.info.r,
            game_map.info.g,
            game_map.info.b,
            game_map.info.music,
            game_map.info.name.clone(),
        )
    } else {
        (200, 200, 200, 0, format!("Mapa {}", new_map))
    };

    state.send_bytes(conn_id, &binary_packets::write_change_map(new_map as i16, 0, r as u8, g as u8, b as u8));
    state.send_bytes(conn_id, &binary_packets::write_play_midi(music as u8));
    state.send_bytes(conn_id, &binary_packets::write_map_name(&map_name));

    // 8. Send IP (self char index) + own CC + [CD so client renders self at new position
    let (ci, own_cc, own_cd) = match state.users.get(&conn_id) {
        Some(u) => (u.char_index, u.build_cc_binary(), build_cd_binary(u)),
        None => return,
    };
    state.send_bytes(conn_id, &binary_packets::write_user_char_index(ci.0 as i16));
    state.send_bytes(conn_id, &own_cc);
    state.send_bytes(conn_id, &own_cd);

    // 8b. Re-send mount state — CC creates a fresh Character with Mounted=false,
    // so the client loses the mount flag. Send USM to restore it.
    if state.users.get(&conn_id).map(|u| u.montado).unwrap_or(false) {
        state.send_bytes(conn_id, &binary_packets::write_user_mount(ci.0 as i16, true));
    }

    // 8c. Re-send invisible state — CC creates a fresh Character with Invisible=false,
    // so the client loses the pulsing alpha. Send NOVER to restore it.
    // Covers both GM /invisible and spell invisibility.
    if let Some(u) = state.users.get(&conn_id) {
        if u.invisible {
            let remaining = if u.admin_invisible { 0 } else {
                ((state.intervals.invisible - u.counter_invisible) as f32 * 0.04) as i16
            };
            state.send_bytes(conn_id, &binary_packets::write_set_invisible(ci.0 as i16, true, remaining));
        }
    }

    // 9. PU (position update — tells client where to center camera)
    state.send_bytes(conn_id, &binary_packets::write_pos_update(final_x as i16, final_y as i16));

    // 10. Send area visibility (CA + strip CCs/NPCs/items)
    make_user_visible(state, conn_id).await;

    // 11. Send CC + [CD to other players in new area so they see us
    //     Skip if invisible (GM or spell) — others must NOT see us (except clanmates).
    let is_invis = state.users.get(&conn_id).map(|u| u.invisible).unwrap_or(false);
    if !is_invis {
        state.send_data_bytes(
            SendTarget::ToAreaButIndex { conn_id, map: new_map, x: final_x, y: final_y },
            &own_cc,
        );
        state.send_data_bytes(
            SendTarget::ToAreaButIndex { conn_id, map: new_map, x: final_x, y: final_y },
            &own_cd,
        );
    } else {
        // Invisible but clanmates should still see us
        let area_users = state.get_area_users(new_map, final_x, final_y, conn_id);
        for other_id in area_users {
            if same_clan(state, conn_id, other_id) {
                state.send_bytes(other_id, &own_cc);
                let cd = match state.users.get(&conn_id) {
                    Some(u) => build_cd_binary(u),
                    None => continue,
                };
                state.send_bytes(other_id, &cd);
                // Tell clanmate we're invisible (semi-transparent rendering)
                state.send_bytes(other_id, &binary_packets::write_set_invisible(ci.0 as i16, true, 0));
            }
        }
    }

    // 12. Warp FX is NOT sent by default — only when caller sets fx=true
    // (VB6: FX param is Optional, only DoTileEvents sets it when tile has otTeleport object)

    // Door BQ/HO sync is handled by make_user_visible() → check_update_needed_user()

    // 13. BKW — fade back in (VB6 end of WarpUserChar)
    state.send_bytes(conn_id, &binary_packets::write_pause_toggle());

    // 14. Warp pets to new map (VB6: WarpMascotas)
    warp_mascotas(state, conn_id, new_map, final_x, final_y).await;

}

/// Send warp FX (sound + visual) at user's current position.
/// VB6: Only called when tile has otTeleport object (FX=True param).
pub(crate) async fn send_warp_fx(state: &mut GameState, conn_id: ConnectionId) {
    let (invisible, ci, map, x, y) = match state.users.get(&conn_id) {
        Some(u) => (u.admin_invisible, u.char_index.0, u.pos_map, u.pos_x, u.pos_y),
        None => return,
    };
    if !invisible {
        state.send_data_bytes(SendTarget::ToArea { map, x, y }, &binary_packets::write_play_wave(3, x as i16, y as i16));
        state.send_data_bytes(SendTarget::ToArea { map, x, y }, &binary_packets::write_create_fx(ci as i16, 1, 0));
    }
}

// find_free_pos — moved to common.rs

