//! Movement handlers: walk, change heading, request position.
//! Extracted from mod.rs to reduce file size.

use tracing::warn;
use crate::net::ConnectionId;
use crate::protocol::{font_index, binary_packets};
use crate::game::class_race::PlayerClass;
use crate::game::types::{GameState, SendTarget};
use crate::game::world;
use super::common::*;
use super::{
    warp_user, send_warp_fx, mover_casper, check_update_needed_user, zona_cura, auto_cura_user,
};

// =====================================================================
// In-game handlers
// =====================================================================

/// M<heading> — Character movement.
pub(super) async fn handle_walk(state: &mut GameState, conn_id: ConnectionId, data: &str) {
    // Movement packets are very frequent, don't log them
    // Check logged in
    let user_data = match state.users.get(&conn_id) {
        Some(u) if u.logged => (u.pos_map, u.pos_x, u.pos_y, u.char_index, u.paralyzed, u.dead, u.meditating, u.navigating, u.resting, u.traveling),
        _ => return,
    };
    let (map, old_x, old_y, char_index, paralyzed, dead, meditating, navigating, resting, traveling) = user_data;

    // VB6: Dead users CAN move (they walk as ghosts). Only paralyzed blocks.
    if paralyzed {
        // Force client back to server position (prevents ghost movement on client)
        state.send_bytes(conn_id, &binary_packets::write_pos_update(old_x as i16, old_y as i16));
        return;
    }

    // NOTE: VB6 defines PuedoPU() but NEVER calls it in the movement handler.
    // Movement speed is controlled entirely client-side by animation timing.
    // No server-side anti-flood for movement.

    // Parse heading from payload (single digit after "M")
    let heading_str = strip_opcode(data, 1);
    let heading: i32 = heading_str.parse().unwrap_or(0);
    if heading < 1 || heading > 4 {
        warn!("[WALK] #{} bad heading '{}' parsed={}", conn_id, heading_str, heading);
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
            SendTarget::ToArea { map, x: old_x, y: old_y },
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

    // Cancel GoHome traveling on movement (VB6: movement interrupts /HOGAR travel)
    if traveling {
        if let Some(user) = state.users.get_mut(&conn_id) {
            user.traveling = false;
            user.counter_go_home = 0;
        }
        state.send_console(conn_id, "Has cancelado el viaje a tu hogar.", font_index::INFO);
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
            // FX if tile has otTeleport object OR particle group (particle teleports)
            let has_teleport_fx = {
                let obj_idx = get_map_tile_obj(state, map, new_x, new_y);
                let has_obj = obj_idx > 0 && state.get_object(obj_idx).map(|o| o.obj_type == crate::data::objects::ObjType::Teleport).unwrap_or(false);
                let has_particle = get_map_tile_particle(state, map, new_x, new_y) > 0;
                has_obj || has_particle
            };
            warp_user(state, conn_id, exit_map, exit_x, exit_y).await;
            if has_teleport_fx {
                send_warp_fx(state, conn_id).await;
            }
            return;
        }

        // Reject movement — send position correction
        // Walk rejected — don't log (too frequent)
        state.send_bytes(conn_id, &binary_packets::write_pos_update(old_x as i16, old_y as i16));
        return;
    }

    // Update heading
    if let Some(user) = state.users.get_mut(&conn_id) {
        user.heading = heading;
    }

    // VB6 HandleWalk: Moving while hidden (Oculto) reveals non-Thief/non-Bandit classes.
    // Spell invisibility is NOT broken by movement.
    let (was_hidden, class_for_hide, is_spell_invis, navigating_for_hide) = match state.users.get(&conn_id) {
        Some(u) => (u.hidden && !u.admin_invisible, u.class, u.invisible && !u.admin_invisible, u.navigating),
        None => (false, PlayerClass::default(), false, false),
    };
    if was_hidden {
        if !class_for_hide.is_thief_or_bandit() {
            // Reveal hidden state
            if let Some(user) = state.users.get_mut(&conn_id) {
                user.hidden = false;
                user.counter_oculto = 0;
            }
            // Only send SetInvisible(false) if spell invisibility is NOT active
            if !is_spell_invis && !navigating_for_hide {
                state.send_console(conn_id, "Has vuelto a ser visible.", font_index::INFO);
                // Re-broadcast CC+CD so non-clanmates (who had CharacterRemove) see us again
                if let Some(u) = state.users.get(&conn_id) {
                    let cc = u.build_cc_binary();
                    let cd = build_cd_binary(u);
                    let (px, py) = (u.pos_x, u.pos_y);
                    state.send_data_bytes(SendTarget::ToArea { map, x: px, y: py }, &cc);
                    state.send_data_bytes(SendTarget::ToArea { map, x: px, y: py }, &cd);
                }
                let nover = binary_packets::write_set_invisible(char_index.0 as i16, false, 0);
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
    let is_invisible = state.users.get(&conn_id).map(|u| u.invisible || u.hidden).unwrap_or(false);
    {
        let move_pkt = binary_packets::write_character_move(char_index.0 as i16, new_x as i16, new_y as i16);
        let (area_min_x, area_min_y) = match state.users.get(&conn_id) {
            Some(u) => (u.area_min_x, u.area_min_y),
            None => (0, 0),
        };
        if area_min_x > 0 || area_min_y > 0 {
            let amx = area_min_x.max(1);
            let amy = area_min_y.max(1);
            let axx = (area_min_x + 26).min(100);
            let axy = (area_min_y + 26).min(100);
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
                SendTarget::ToAreaButIndex { conn_id, map, x: new_x, y: new_y },
                &move_pkt,
            );
        }
    }

    // VB6: ZonaCura check — auto-heal/revive if near a Revividor NPC (Sacerdotes automáticos)
    if zona_cura(state, map, new_x, new_y) {
        auto_cura_user(state, conn_id).await;
    }

    // Area boundary visibility (VB6: ModAreas.CheckUpdateNeededUser)
    // Only fires when crossing a 9x9 zone boundary — sends CA + new strip CCs
    check_update_needed_user(state, conn_id, heading).await;

    // VB6 DoTileEvents: check tile exit AFTER successful movement (map transitions)
    if let Some((exit_map, exit_x, exit_y)) = state.get_tile_exit(map, new_x, new_y) {
        // FX if tile has otTeleport object OR particle group (particle teleports)
        let has_teleport_fx = {
            let obj_idx = get_map_tile_obj(state, map, new_x, new_y);
            let has_obj = obj_idx > 0 && state.get_object(obj_idx).map(|o| o.obj_type == crate::data::objects::ObjType::Teleport).unwrap_or(false);
            let has_particle = get_map_tile_particle(state, map, new_x, new_y) > 0;
            has_obj || has_particle
        };
        warp_user(state, conn_id, exit_map, exit_x, exit_y).await;
        if has_teleport_fx {
            send_warp_fx(state, conn_id).await;
        }
    }
}

/// CHEA<heading> — Change heading without moving.
pub(super) async fn handle_change_heading(state: &mut GameState, conn_id: ConnectionId, data: &str) {
    let user_data = match state.users.get(&conn_id) {
        Some(u) if u.logged => (u.pos_map, u.pos_x, u.pos_y, u.char_index),
        _ => return,
    };
    let (map, x, y, char_index) = user_data;

    let heading: i32 = strip_opcode(data, 4).parse().unwrap_or(0);
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
            state.send_bytes(conn_id, &binary_packets::write_pos_update(user.pos_x as i16, user.pos_y as i16));
        }
    }
}
