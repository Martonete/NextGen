//! World-level tick handlers: anti-cheat intervals, world cleanup, security, utilities.

use crate::net::ConnectionId;
use crate::game::class_race::PlayerClass;
use crate::game::types::{GameState, SendTarget, CleanWorldEntry};
use crate::protocol::{binary_packets, font_index};
use crate::game::handlers::common::*;
use crate::game::handlers::{warp_user};
use super::remove_pet_from_owner;

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
        if user.warp_immunity_ticks > 0 { user.warp_immunity_ticks -= 1; }

        // VB6: Meditation concentration delay — 2-second warmup before regen starts
        if user.meditation_start_tick > 0 { user.meditation_start_tick -= 1; }

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
        state.send_bytes(conn_id, &pkt);
    }

    // VB6: EfectoInvisibilidad — count up invisibility timer each tick.
    // Only for spell invisibility (not admin_invisible which is permanent).
    let intervalo_invis = state.intervals.invisible;
    let mut uninvis: Vec<(ConnectionId, i16, i32, i32, i32, bool, bool)> = Vec::new();
    for (&conn_id, user) in state.users.iter_mut() {
        if user.invisible && !user.admin_invisible {
            if user.counter_invisible < intervalo_invis {
                user.counter_invisible += 1;
            } else {
                // Timer expired — remove invisibility
                // VB6: .Counters.Invisibilidad = RandomNumber(-100, 100) — variable next duration
                user.invisible = false;
                user.counter_invisible = rand_range(-250, 250); // VB6: RandomNumber(-100,100) at 10Hz → ±10s; Rust: ±250 at 25Hz (40ms) → same ±10s
                // VB6: only send SetInvisible(false) if Oculto=0 (still hidden → no visibility change)
                uninvis.push((conn_id, user.char_index.0 as i16, user.pos_map, user.pos_x, user.pos_y, user.navigating, user.hidden));
            }
        }
    }
    for (conn_id, ci, map, x, y, navigating, still_hidden) in uninvis {
        if !still_hidden {
            state.send_console(conn_id, "Has vuelto a ser visible.", font_index::INFO);
            if !navigating {
                // Re-broadcast CC so others see us again
                if let Some(user) = state.users.get(&conn_id) {
                    let cc = user.build_cc_binary();
                    let cd = crate::game::handlers::common::build_cd_binary(user);
                    state.send_data_bytes(SendTarget::ToArea { map, x, y }, &cc);
                    state.send_data_bytes(SendTarget::ToArea { map, x, y }, &cd);
                }
                // Tell self we're visible again
                let nover = binary_packets::write_set_invisible(ci, false, 0);
                state.send_bytes(conn_id, &nover);
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
            if user.class == PlayerClass::Cazador && skill > 90 && (armor_obj == 648 || armor_obj == 360) {
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
            state.send_console(conn_id, "Has vuelto a ser visible.", font_index::INFO);
            if !navigating {
                // Send CC+CD only to non-clanmates (they had CharacterRemove and need
                // the full character recreated). Clanmates already have the character
                // so they just need SetInvisible(false) — avoids animation reset.
                let (cc, cd) = match state.users.get(&conn_id) {
                    Some(user) => (user.build_cc_binary(), build_cd_binary(user)),
                    None => continue,
                };
                let nover = binary_packets::write_set_invisible(ci, false, 0);
                let area_users = state.get_area_users(map, x, y, conn_id);
                for other_id in area_users {
                    if same_clan(state, conn_id, other_id) {
                        state.send_bytes(other_id, &nover);
                    } else {
                        state.send_bytes(other_id, &cc);
                        state.send_bytes(other_id, &cd);
                    }
                }
                // Tell self we're visible again (no CC needed — preserves animation)
                state.send_bytes(conn_id, &nover);
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
        state.send_console(conn_id, "Recuperas tu apariencia normal.", font_index::INFO);
        if let Some(u) = state.users.get(&conn_id) {
            let cp = binary_packets::write_character_change(
                ci, u.body as i16, u.head as i16, u.heading as u8,
                u.equip.weapon as i16, u.equip.shield as i16, u.equip.helmet as i16, 0, 0,
            );
            state.send_data_bytes(SendTarget::ToArea { map, x, y }, &cp);
        }
    }

    // NPC paralysis countdown (same 40ms tick as user paralysis)
    // Only iterate active NPCs via index set
    let npc_para_indices: Vec<usize> = state.active_npc_indices.iter().copied().collect();
    for idx in npc_para_indices {
        if let Some(npc) = state.npcs.get_mut(idx).and_then(|n| n.as_mut()) {
            if npc.paralyzed {
                if npc.counter_paralisis > 0 {
                    npc.counter_paralisis -= 1;
                } else {
                    npc.paralyzed = false;
                }
            }
        }
    }

    // --- EfectoEstadoAtacable timeout (VB6: 60-second duel attackable timer) ---
    // When atacable_por != 0, count up. At 1500 ticks (60s at 40ms/tick), clear attackable state.
    let mut clear_atacable: Vec<ConnectionId> = Vec::new();
    for (&conn_id, user) in state.users.iter_mut() {
        if user.logged && user.atacable_por != 0 {
            user.counter_atacable += 1;
            if user.counter_atacable >= 1500 {
                user.atacable_por = 0;
                user.counter_atacable = 0;
                clear_atacable.push(conn_id);
            }
        }
    }
    for conn_id in clear_atacable {
        state.send_console(conn_id, "El estado de duelo ha expirado.", font_index::INFO);
    }

    // --- GoHome traveling system (VB6: dead user /HOGAR teleport after 10s delay) ---
    let mut teleport_home: Vec<ConnectionId> = Vec::new();
    for (&conn_id, user) in state.users.iter_mut() {
        if user.logged && user.traveling {
            user.counter_go_home += 1;
            if user.counter_go_home >= 250 { // 250 ticks * 40ms = 10 seconds
                teleport_home.push(conn_id);
            }
        }
    }
    for conn_id in teleport_home {
        // Get home city and resolve coordinates
        let hogar = state.users.get(&conn_id).map(|u| u.hogar.clone()).unwrap_or_default();
        // Clear traveling state
        if let Some(u) = state.users.get_mut(&conn_id) {
            u.traveling = false;
            u.counter_go_home = 0;
        }
        // VB6 home city coordinates (common spawn points)
        let (home_map, home_x, home_y) = resolve_home_city(&hogar);
        if home_map > 0 {
            warp_user(state, conn_id, home_map, home_x, home_y).await;
            state.send_console(conn_id, "Has llegado a tu hogar.", font_index::INFO);
        }
    }

    // --- NPC pet ownership expiry (VB6: TiemPerdique — 18s inactivity timer) ---
    // Pets with an owner: if owner is too far or offline, increment counter. At 450 ticks (18s), despawn.
    let pet_indices: Vec<usize> = state.active_npc_indices.iter()
        .copied()
        .filter(|&i| {
            state.npcs.get(i)
                .and_then(|slot| slot.as_ref())
                .map(|n| n.maestro_user.is_some())
                .unwrap_or(false)
        })
        .collect();
    for npc_idx in pet_indices {
        let (owner_conn, npc_map, npc_x, npc_y) = match state.get_npc(npc_idx) {
            Some(n) => match n.maestro_user {
                Some(owner) => (owner, n.map, n.x, n.y),
                None => continue, // filtered to is_some above — shouldn't happen
            },
            None => continue,
        };
        let owner_nearby = state.users.get(&owner_conn)
            .map(|u| u.logged && u.pos_map == npc_map && (u.pos_x - npc_x).abs() <= 15 && (u.pos_y - npc_y).abs() <= 15)
            .unwrap_or(false);

        if owner_nearby {
            // Owner is nearby — reset counter
            if let Some(n) = state.get_npc_mut(npc_idx) {
                n.counter_perdio_npc = 0;
            }
        } else {
            // Owner is far or offline — increment counter
            let counter = state.get_npc(npc_idx).map(|n| n.counter_perdio_npc).unwrap_or(0);
            if counter >= 450 { // 450 ticks * 40ms = 18 seconds
                // Despawn pet
                let (map, x, y, char_index) = match state.get_npc(npc_idx) {
                    Some(n) => (n.map, n.x, n.y, n.char_index),
                    None => continue,
                };
                let bp_pkt = binary_packets::write_character_remove(char_index.0 as i16);
                state.send_data_bytes(SendTarget::ToArea { map, x, y }, &bp_pkt);
                remove_pet_from_owner(state, owner_conn, npc_idx);
                state.kill_npc(npc_idx);
            } else {
                if let Some(n) = state.get_npc_mut(npc_idx) {
                    n.counter_perdio_npc += 1;
                }
            }
        }
    }

    // --- Rain STA drain (VB6: EfectoLluvia — 3% MaxSTA drain on exterior maps) ---
    if state.raining {
        state.rain_counter += 1;
        if state.rain_counter >= 100 { // 100 ticks * 40ms = 4 seconds
            state.rain_counter = 0;
            let user_ids: Vec<ConnectionId> = state.users.keys().copied().collect();
            for conn_id in user_ids {
                let should_drain = state.users.get(&conn_id).map(|u| {
                    u.logged && !u.dead && u.privileges == 0 && is_exterior_map(state, u.pos_map)
                }).unwrap_or(false);
                if should_drain {
                    let sta_drain = state.users.get(&conn_id)
                        .map(|u| ((u.max_sta as f64 * 3.0) / 100.0) as i32)
                        .unwrap_or(0);
                    if sta_drain > 0 {
                        if let Some(u) = state.users.get_mut(&conn_id) {
                            u.min_sta = (u.min_sta - sta_drain).max(0);
                        }
                        send_stats_sta(state, conn_id).await;
                    }
                }
            }
        }
    }
}

/// Check if a map is an exterior map (affected by rain).
/// VB6: Maps are "intemperie" if they are outdoor maps (not dungeons/interiors).
/// We check: pk=true (PvP/exterior maps), or if the terrain is not explicitly a dungeon.
fn is_exterior_map(state: &GameState, map_num: i32) -> bool {
    state.game_data.maps.get(map_num as usize)
        .and_then(|m| m.as_ref())
        .map(|m| {
            // Maps with pk=true are exterior (PvP-enabled outdoor maps).
            // Also check terreno — dungeons/caves are explicitly marked.
            m.info.pk || m.info.terreno.eq_ignore_ascii_case("CAMPO")
                || m.info.terreno.eq_ignore_ascii_case("BOSQUE")
                || m.info.terreno.eq_ignore_ascii_case("NIEVE")
                || m.info.terreno.eq_ignore_ascii_case("DESIERTO")
                || m.info.terreno.is_empty()
        })
        .unwrap_or(false)
}

/// Resolve a home city name to (map, x, y) coordinates.
/// VB6: Standard Argentum cities with their spawn points.
fn resolve_home_city(hogar: &str) -> (i32, i32, i32) {
    match hogar.to_uppercase().as_str() {
        "ULLATHORPE" => (1, 50, 50),
        "NIX" => (3, 50, 50),
        "BANDERBILL" => (14, 50, 50),
        "LINDOS" => (28, 50, 50),
        "ARGHAL" => (35, 50, 50),
        _ => {
            // Default: Ullathorpe if home not recognized
            if hogar.is_empty() { (0, 0, 0) } else { (1, 50, 50) }
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
            let tracked_obj = state.clean_world[i].obj_index;

            // Timer expired — remove item from world only if the same item is still there.
            // If the item was picked up or replaced by a different item, skip deletion.
            let should_remove = {
                if let Some(grid) = state.world.grid(map) {
                    if let Some(tile) = grid.tile(x, y) {
                        tile.ground_item.obj_index > 0 && tile.ground_item.obj_index == tracked_obj
                    } else {
                        false
                    }
                } else {
                    false
                }
            };

            if should_remove {
                // Erase the object from the tile
                let grid = state.world.grid_mut(map);
                if let Some(tile) = grid.tile_mut(x, y) {
                    tile.ground_item.obj_index = 0;
                    tile.ground_item.amount = 0;
                }
                // Broadcast BO (remove object from ground) to area
                let pkt = binary_packets::write_object_delete(x as i16, y as i16);
                state.send_data_bytes(SendTarget::ToArea { map, x, y }, &pkt);
            }

            // Clear the cleanup slot
            state.clean_world[i] = CleanWorldEntry::default();
        } else {
            // Decrement timer
            state.clean_world[i].tiempo -= 1;
        }
    }
}

/// Security tick — called every 1 second from the main loop.
///
/// 1. Checks which connections exceeded the packet rate limit this window.
///    - If exceeded: increment flood_strikes for that connection.
///    - If strikes >= flood_strike_limit: queue for disconnect.
///    - If NOT exceeded: reset strikes to 0 (clean window resets history).
/// 2. Resets all packet counters for the next 1-second window.
/// 3. Cleans up flood_strikes for disconnected connections.
pub fn tick_security(state: &mut GameState) {
    let limit = state.max_packets_per_second;
    let strike_limit = state.flood_strike_limit;

    // Collect connection IDs that had packet activity this window
    let conn_ids: Vec<crate::net::ConnectionId> = state.packet_counts.keys().copied().collect();

    for conn_id in &conn_ids {
        let count = state.packet_counts.get(conn_id).copied().unwrap_or(0);
        if count > limit {
            // Exceeded rate — add a strike
            let strikes = state.flood_strikes.entry(*conn_id).or_insert(0);
            *strikes += 1;
            if *strikes >= strike_limit {
                tracing::warn!(
                    "[SEC] Connection #{} flood: {} strikes ({}+ pkt/s), disconnecting",
                    conn_id, strikes, count
                );
                state.security_kick_queue.push(*conn_id);
            } else {
                tracing::debug!(
                    "[SEC] Connection #{} flood strike {}/{} ({} pkt/s)",
                    conn_id, strikes, strike_limit, count
                );
            }
        } else {
            // Clean window — reset strikes
            state.flood_strikes.remove(conn_id);
        }
    }

    // Reset packet counters for the next window
    state.packet_counts.clear();

    // Clean up flood_strikes for connections that no longer exist
    state.flood_strikes.retain(|conn_id, _| state.users.contains_key(conn_id));
}

