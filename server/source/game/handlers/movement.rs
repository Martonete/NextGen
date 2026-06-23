//! Movement handlers: walk, change heading, request position.
//! Extracted from mod.rs to reduce file size.

use super::common::*;
use super::{
    auto_cura_user, check_update_needed_user, mover_casper, send_warp_fx, warp_user, zona_cura,
};
use crate::game::class_race::PlayerClass;
use crate::game::types::{GameState, SendTarget};
use crate::game::world;
use crate::net::ConnectionId;
use crate::protocol::{binary_packets, font_index};
use tracing::warn;

// =====================================================================
// Helpers
// =====================================================================

/// Check map/zone entry restrictions for a teleport destination.
///
/// Returns `true` if the player is allowed to enter, `false` if blocked.
/// Sends an appropriate console message when blocked.
///
/// Checks (in order):
/// 1. Newbie zone: blocked if player level exceeds `max_level` (when > 0).
/// 2. Level range: blocked if player level is below `min_level` (when > 0).
/// 3. Faction zone: blocked if `solo_faccion` is set and player isn't in the required faction.
fn check_teleport_restrictions(
    state: &mut GameState,
    conn_id: ConnectionId,
    dest_map: i32,
    dest_x: i32,
    dest_y: i32,
) -> bool {
    // Collect the fields we need from the user.
    let (level, armada_real, fuerzas_caos) = match state.users.get(&conn_id) {
        Some(u) => (u.level, u.armada_real, u.fuerzas_caos),
        None => return true, // No user — let warp_user handle it.
    };

    // Look up zone data at destination (zones use 0-based coordinates).
    let map_idx = dest_map as usize;
    let zone_opt: Option<(bool, i32, i32, bool, i32)> = state
        .game_data
        .maps
        .get(map_idx)
        .and_then(|m| m.as_ref())
        .and_then(|gm| gm.get_zone_at(dest_x - 1, dest_y - 1))
        .map(|z| {
            (
                z.newbie,
                z.min_level,
                z.max_level,
                z.solo_faccion,
                z.faccion,
            )
        });

    if let Some((newbie, min_level, max_level, solo_faccion, faccion)) = zone_opt {
        // Newbie zone: block high-level players (max_level > 0).
        if newbie && max_level > 0 && level > max_level {
            state.send_console(
                conn_id,
                "Solo jugadores nuevos pueden ingresar a esa zona.",
                font_index::INFO,
            );
            return false;
        }

        // Min-level restriction (e.g. entering a high-level area).
        if min_level > 0 && level < min_level {
            state.send_console(
                conn_id,
                "No tienes el nivel requerido para ingresar a esa zona.",
                font_index::INFO,
            );
            return false;
        }

        // Faction-restricted zone.
        if solo_faccion {
            let allowed = match faccion {
                1 => armada_real,  // Royal Army only
                2 => fuerzas_caos, // Chaos Forces only
                _ => true,         // 0 or unknown = no restriction
            };
            if !allowed {
                state.send_console(
                    conn_id,
                    "No perteneces a la faccion que controla esa zona.",
                    font_index::INFO,
                );
                return false;
            }
        }
    }

    true
}

/// VB6 13.3 parity (M23): RhombLegalPos — outward diamond/rhombus spiral search.
///
/// Starting at (exit_x, exit_y), walks outward in diamond layers from distance 0 to `radio`.
/// At each distance d, walks the 4 perimeter segments (NE, SE, SW, NW diagonals).
/// Returns the first unblocked tile, or (exit_x, exit_y) as fallback.
///
/// VB6 RhombLegalPos algorithm: 4 loop iterations each stepping diagonally:
///   i=1: dx+1, dy-1 (NE quadrant)
///   i=2: dx+1, dy+1 (SE quadrant)
///   i=3: dx-1, dy+1 (SW quadrant)
///   i=4: dx-1, dy-1 (NW quadrant)
fn randomize_exit(
    state: &GameState,
    exit_map: i32,
    exit_x: i32,
    exit_y: i32,
    radio: i32,
) -> (i32, i32) {
    if radio <= 0 {
        return (exit_x, exit_y);
    }
    // Check center first (distance 0)
    if !state.is_tile_blocked(exit_map, exit_x, exit_y) {
        return (exit_x, exit_y);
    }
    // Walk outward diamond layers
    for d in 1..=radio {
        // Start position of this diamond layer: top vertex
        let mut cx = exit_x;
        let mut cy = exit_y - d;
        // 4 quadrant walks, each of length d steps
        let steps = [(1i32, 1i32), (1i32, -1i32), (-1i32, -1i32), (-1i32, 1i32)];
        for (sdx, sdy) in steps {
            for _ in 0..d {
                cx += sdx;
                cy += sdy;
                if !state.is_tile_blocked(exit_map, cx, cy) {
                    return (cx, cy);
                }
            }
        }
    }
    (exit_x, exit_y)
}

// =====================================================================
// In-game handlers
// =====================================================================

/// M<heading> — Character movement.
pub(super) async fn handle_walk(state: &mut GameState, conn_id: ConnectionId, heading: i32) {
    // Movement packets are very frequent, don't log them
    // Check logged in
    let user_data = match state.users.get(&conn_id) {
        Some(u) if u.logged => (
            u.pos_map,
            u.pos_x,
            u.pos_y,
            u.char_index,
            u.paralyzed,
            u.dead,
            u.meditating,
            u.navigating,
            u.resting,
            u.traveling,
        ),
        _ => return,
    };
    let (
        map,
        old_x,
        old_y,
        char_index,
        paralyzed,
        dead,
        meditating,
        navigating,
        resting,
        traveling,
    ) = user_data;

    // VB6: Dead users CAN move (they walk as ghosts). Only paralyzed blocks.
    if paralyzed {
        // Force client back to server position (prevents ghost movement on client)
        state.send_bytes(
            conn_id,
            &binary_packets::write_pos_update(old_x as i16, old_y as i16),
        );
        return;
    }

    // NOTE: VB6 defines PuedoPU() but NEVER calls it in the movement handler.
    // Movement speed is controlled entirely client-side by animation timing.
    // No server-side anti-flood for movement.

    // Validate heading (1=north, 2=east, 3=south, 4=west)
    if heading < 1 || heading > 4 {
        warn!("[WALK] #{} bad heading {}", conn_id, heading);
        return;
    }

    // Cancel meditation on movement (VB6: TCP_HandleData1.bas lines 360-365)
    if meditating {
        if let Some(user) = state.users.get_mut(&conn_id) {
            user.meditating = false;
        }
        state.send_bytes(conn_id, &binary_packets::write_meditate_toggle());
        state.send_msg_id(conn_id, 205, ""); // TEXTO205: Dejas de meditar
        // Remove meditation FX from area
        state.send_data_bytes(
            SendTarget::ToArea {
                map,
                x: old_x,
                y: old_y,
            },
            &binary_packets::write_create_fx(char_index.0 as i16, 0, 0),
        );
    }

    // Cancel resting on movement (VB6: HandleWalk line 2037-2040)
    if resting {
        if let Some(user) = state.users.get_mut(&conn_id) {
            user.resting = false;
        }
        state.send_bytes(conn_id, &binary_packets::write_rest_ok());
        state.send_console(conn_id, "Te levantás.", font_index::INFO);
    }

    // VB6 13.3: movement is blocked while traveling home
    if traveling {
        return;
    }

    // VB6 13.3 parity (I3): speed-hack detection — max 30 steps per 5800ms (GMs exempt).
    // 2-strike system: first detection stores timestamp; second detection within 30s kicks.
    {
        let privileges = state.users.get(&conn_id).map(|u| u.privileges).unwrap_or(0);
        if privileges == 0 {
            let now = std::time::Instant::now();
            let now_ms = std::time::SystemTime::now()
                .duration_since(std::time::UNIX_EPOCH)
                .unwrap_or_default()
                .as_millis() as u64;
            let (elapsed_ms, steps) = match state.users.get(&conn_id) {
                Some(u) => (
                    now.duration_since(u.speed_window_start).as_millis() as i64,
                    u.speed_steps,
                ),
                None => return,
            };

            if elapsed_ms > 5800 {
                // Reset window
                if let Some(user) = state.users.get_mut(&conn_id) {
                    user.speed_steps = 1;
                    user.speed_window_start = now;
                }
            } else {
                let new_steps = steps + 1;
                if let Some(user) = state.users.get_mut(&conn_id) {
                    user.speed_steps = new_steps;
                }
                if new_steps > 30 {
                    let name = state
                        .users
                        .get(&conn_id)
                        .map(|u| u.char_name.clone())
                        .unwrap_or_default();
                    let first_strike_ms = state.users.get(&conn_id).and_then(|u| u.count_sh);
                    match first_strike_ms {
                        None => {
                            // First detection — record timestamp and warn
                            tracing::warn!(
                                "[SECURITY] Speed-hack first strike: '{}' ({} steps in {}ms)",
                                name,
                                new_steps,
                                elapsed_ms
                            );
                            if let Some(user) = state.users.get_mut(&conn_id) {
                                user.count_sh = Some(now_ms);
                                user.speed_steps = 0;
                                user.speed_window_start = now;
                            }
                        }
                        Some(first_ms) => {
                            // Second detection — check if within 30 seconds
                            if now_ms.saturating_sub(first_ms) <= 30_000 {
                                tracing::warn!(
                                    "[SECURITY] Speed-hack second strike: '{}' kicked ({} steps in {}ms)",
                                    name,
                                    new_steps,
                                    elapsed_ms
                                );
                                state.security_kick_queue.push(conn_id);
                                return;
                            } else {
                                // More than 30s since first strike — reset to first strike
                                tracing::warn!(
                                    "[SECURITY] Speed-hack first strike (reset): '{}' ({} steps in {}ms)",
                                    name,
                                    new_steps,
                                    elapsed_ms
                                );
                                if let Some(user) = state.users.get_mut(&conn_id) {
                                    user.count_sh = Some(now_ms);
                                    user.speed_steps = 0;
                                    user.speed_window_start = now;
                                }
                            }
                        }
                    }
                }
            }
        }
    }

    let (dx, dy) = world::heading_to_offset(heading);
    let new_x = old_x + dx;
    let new_y = old_y + dy;

    // Check map bounds and blocked
    // VB6 LegalPos: When navigating (PuedeAgua=True), only water tiles are legal.
    // When walking normally, water tiles are impassable.
    let tile_blocked = state.is_tile_blocked(map, new_x, new_y);
    let has_water = state.hay_agua(map, new_x, new_y);
    let blocked = if navigating {
        // On boat: can only move on water tiles, blocked tiles still block
        tile_blocked || !has_water
    } else {
        // On foot: blocked tiles and water tiles are impassable
        tile_blocked || has_water
    };

    // VB6 "Mover Casper": if a living user walks onto a dead user's tile, push the ghost aside.
    // Must happen BEFORE is_legal_pos since dead users occupy the tile (user_conn is set).
    if !dead && !blocked {
        mover_casper(state, map, new_x, new_y, heading).await;
    }

    let legal = state.world.is_legal_pos(map, new_x, new_y, blocked);

    // Walk movement — don't log (too frequent)

    if !legal {
        // Check if there's a map exit at the target tile
        if let Some((exit_map, exit_x, exit_y)) = state.get_tile_exit(map, new_x, new_y) {
            // VB6 M22: dead players cannot enter maps that have OnDeathGoTo defined
            let is_dead = state.users.get(&conn_id).map(|u| u.dead).unwrap_or(false);
            let exit_map_restricts_dead = state
                .game_data
                .maps
                .get(exit_map as usize)
                .and_then(|m| m.as_ref())
                .map(|m| m.info.on_death_go_to.0 != 0)
                .unwrap_or(false);
            if is_dead && exit_map_restricts_dead {
                state.send_console(
                    conn_id,
                    "Solo se permite entrar al mapa a los personajes vivos.",
                    crate::protocol::font_index::INFO,
                );
                state.send_bytes(
                    conn_id,
                    &binary_packets::write_pos_update(old_x as i16, old_y as i16),
                );
                return;
            }

            // FX if tile has otTeleport object OR particle group (particle teleports)
            let (has_teleport_fx, teleport_radio) = {
                let obj_idx = get_map_tile_obj(state, map, new_x, new_y);
                let has_obj = obj_idx > 0
                    && state
                        .get_object(obj_idx)
                        .map(|o| o.obj_type == crate::data::objects::ObjType::Teleport)
                        .unwrap_or(false);
                let has_particle = get_map_tile_particle(state, map, new_x, new_y) > 0;
                let radio = if obj_idx > 0 {
                    state.get_object(obj_idx).map(|o| o.radio).unwrap_or(0)
                } else {
                    0
                };
                (has_obj || has_particle, radio)
            };
            let (dest_x, dest_y) = randomize_exit(state, exit_map, exit_x, exit_y, teleport_radio);
            if check_teleport_restrictions(state, conn_id, exit_map, dest_x, dest_y) {
                warp_user(state, conn_id, exit_map, dest_x, dest_y).await;
                if has_teleport_fx {
                    send_warp_fx(state, conn_id).await;
                }
            } else {
                // Blocked — send position correction so the client doesn't drift.
                state.send_bytes(
                    conn_id,
                    &binary_packets::write_pos_update(old_x as i16, old_y as i16),
                );
            }
            return;
        }

        // Reject movement — send position correction
        // Walk rejected — don't log (too frequent)
        state.send_bytes(
            conn_id,
            &binary_packets::write_pos_update(old_x as i16, old_y as i16),
        );
        return;
    }

    // Update heading
    if let Some(user) = state.users.get_mut(&conn_id) {
        user.heading = heading;
    }

    // VB6 HandleWalk: Moving while hidden (Oculto) reveals non-Thief/non-Bandit classes.
    // Spell invisibility is NOT broken by movement.
    let (was_hidden, class_for_hide, is_spell_invis, navigating_for_hide) =
        match state.users.get(&conn_id) {
            Some(u) => (
                u.hidden && !u.admin_invisible,
                u.class,
                u.invisible && !u.admin_invisible,
                u.navigating,
            ),
            None => (false, PlayerClass::default(), false, false),
        };
    if was_hidden {
        if !class_for_hide.is_thief_or_bandit() {
            // Reveal hidden state
            if let Some(user) = state.users.get_mut(&conn_id) {
                user.hidden = false;
                user.counter_oculto = 0;
            }
            // Only send unhide if spell invisibility is NOT active
            if !is_spell_invis && !navigating_for_hide {
                state.send_console(conn_id, "Has vuelto a ser visible.", font_index::INFO);
                // CC+CD only to non-clanmates (they had CharacterRemove).
                // Clanmates get SetInvisible(false) — avoids animation reset.
                let ci = char_index.0 as i16;
                let nover = binary_packets::write_set_invisible(ci, false, 0);
                if let Some(u) = state.users.get(&conn_id) {
                    let cc = u.build_cc_binary();
                    let cd = build_cd_binary(u);
                    let (px, py) = (u.pos_x, u.pos_y);
                    let area_users = state.get_area_users(map, px, py, conn_id);
                    for other_id in area_users {
                        if same_clan(state, conn_id, other_id) {
                            state.send_bytes(other_id, &nover);
                        } else {
                            state.send_bytes(other_id, &cc);
                            state.send_bytes(other_id, &cd);
                        }
                    }
                }
                state.send_bytes(conn_id, &nover);
            }
        }
    }

    // Move on grid
    state.world.remove_user(map, old_x, old_y);
    state.world.place_user(map, new_x, new_y, conn_id);

    // Update user position
    if let Some(user) = state.users.get_mut(&conn_id) {
        user.pos_x = new_x;
        user.pos_y = new_y;
    }

    // Broadcast movement to area (CharacterMove packet) — only to OTHER players
    // VB6 SendToUserAreaButindex: broadcasts to all users in the sender's 27x27 area
    // When invisible, still send movement to same-clan members.
    let is_invisible = state
        .users
        .get(&conn_id)
        .map(|u| u.invisible || u.hidden)
        .unwrap_or(false);
    {
        let move_pkt =
            binary_packets::write_character_move(char_index.0 as i16, new_x as i16, new_y as i16);
        let (area_min_x, area_min_y) = match state.users.get(&conn_id) {
            Some(u) => (u.area_min_x, u.area_min_y),
            None => (0, 0),
        };
        if area_min_x > 0 || area_min_y > 0 {
            let amx = area_min_x.max(1);
            let amy = area_min_y.max(1);
            let (grid_w, grid_h) = state.grid_dimensions(map);
            let axx = (area_min_x + 26).min(grid_w);
            let axy = (area_min_y + 26).min(grid_h);
            let mut targets: Vec<ConnectionId> = Vec::new();
            if let Some(grid) = state.world.grid(map) {
                for sy in amy..=axy {
                    for sx in amx..=axx {
                        if let Some(tile) = grid.tile(sx, sy) {
                            if let Some(c) = tile.user_conn {
                                if c != conn_id {
                                    // If we're invisible, only send to clanmates
                                    if !is_invisible || same_clan(state, conn_id, c) {
                                        targets.push(c);
                                    }
                                }
                            }
                        }
                    }
                }
            }
            for c in targets {
                state.send_bytes(c, &move_pkt);
            }
        } else if !is_invisible {
            // Fallback: use standard area broadcast (no clan filter in fallback)
            state.send_data_bytes(
                SendTarget::ToAreaButIndex {
                    conn_id,
                    map,
                    x: new_x,
                    y: new_y,
                },
                &move_pkt,
            );
        }
    }

    // VB6: ZonaCura check — auto-heal/revive if near a Revividor NPC (Sacerdotes automáticos)
    if zona_cura(state, map, new_x, new_y) {
        auto_cura_user(state, conn_id).await;
    }

    // Zone change detection — send ZoneChange packet if player crossed into a different zone
    check_zone_change(state, conn_id).await;

    // Area boundary visibility (VB6: ModAreas.CheckUpdateNeededUser)
    // Only fires when crossing a 9x9 zone boundary — sends CA + new strip CCs
    check_update_needed_user(state, conn_id, heading).await;

    // VB6 DoTileEvents: check tile exit AFTER successful movement (map transitions)
    if let Some((exit_map, exit_x, exit_y)) = state.get_tile_exit(map, new_x, new_y) {
        // VB6 M22: dead players cannot enter maps that have OnDeathGoTo defined
        let is_dead = state.users.get(&conn_id).map(|u| u.dead).unwrap_or(false);
        let exit_map_restricts_dead = state
            .game_data
            .maps
            .get(exit_map as usize)
            .and_then(|m| m.as_ref())
            .map(|m| m.info.on_death_go_to.0 != 0)
            .unwrap_or(false);
        if is_dead && exit_map_restricts_dead {
            state.send_console(
                conn_id,
                "Solo se permite entrar al mapa a los personajes vivos.",
                crate::protocol::font_index::INFO,
            );
            state.send_bytes(
                conn_id,
                &binary_packets::write_pos_update(new_x as i16, new_y as i16),
            );
            return;
        }

        // FX if tile has otTeleport object OR particle group (particle teleports)
        let (has_teleport_fx, teleport_radio) = {
            let obj_idx = get_map_tile_obj(state, map, new_x, new_y);
            let has_obj = obj_idx > 0
                && state
                    .get_object(obj_idx)
                    .map(|o| o.obj_type == crate::data::objects::ObjType::Teleport)
                    .unwrap_or(false);
            let has_particle = get_map_tile_particle(state, map, new_x, new_y) > 0;
            let radio = if obj_idx > 0 {
                state.get_object(obj_idx).map(|o| o.radio).unwrap_or(0)
            } else {
                0
            };
            (has_obj || has_particle, radio)
        };
        let (dest_x, dest_y) = randomize_exit(state, exit_map, exit_x, exit_y, teleport_radio);
        if check_teleport_restrictions(state, conn_id, exit_map, dest_x, dest_y) {
            warp_user(state, conn_id, exit_map, dest_x, dest_y).await;
            if has_teleport_fx {
                send_warp_fx(state, conn_id).await;
            }
        }
    }
}

/// CHEA<heading> — Change heading without moving.
pub(super) async fn handle_change_heading(
    state: &mut GameState,
    conn_id: ConnectionId,
    heading: i32,
) {
    let user_data = match state.users.get(&conn_id) {
        Some(u) if u.logged => (u.pos_map, u.pos_x, u.pos_y, u.char_index),
        _ => return,
    };
    let (map, x, y, char_index) = user_data;

    if heading < 1 || heading > 4 {
        return;
    }

    if let Some(user) = state.users.get_mut(&conn_id) {
        user.heading = heading;
    }

    // Broadcast heading change to area (VB6: |H<charIndex>,<heading>)
    state.send_data_bytes(
        SendTarget::ToAreaButIndex { conn_id, map, x, y },
        &binary_packets::write_heading_change(char_index.0 as i16, heading as u8),
    );
}

/// RPU — Request position update.
pub(super) async fn handle_request_pos(state: &mut GameState, conn_id: ConnectionId) {
    if let Some(user) = state.users.get(&conn_id) {
        if user.logged {
            state.send_bytes(
                conn_id,
                &binary_packets::write_pos_update(user.pos_x as i16, user.pos_y as i16),
            );
        }
    }
}
