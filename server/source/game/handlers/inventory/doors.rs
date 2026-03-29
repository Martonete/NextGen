//! Door interaction, forum system, and safe toggle handlers.
//! Split from inventory.rs for file size management.

use crate::net::ConnectionId;
use crate::game::types::{GameState, SendTarget, MAX_INVENTORY_SLOTS, privilege_level};
use crate::game::world;
use crate::protocol::{font_index, fields::read_field};
use crate::protocol::binary_packets;
use crate::data::objects::ObjType;
use crate::game::handlers::common::*;

pub(crate) async fn accion_para_puerta(state: &mut GameState, conn_id: ConnectionId, map: i32, x: i32, y: i32, obj_index: i32) {
    let (user_x, user_y) = match state.users.get(&conn_id) {
        Some(u) => (u.pos_x, u.pos_y),
        None => return,
    };

    // Distance check: must be within 3 tiles (VB6: Distance > 3)
    if (x - user_x).abs() > 3 || (y - user_y).abs() > 3 {
        state.send_msg_id(conn_id, 10, "");
        return;
    }

    let obj = match state.get_object(obj_index) {
        Some(o) => o.clone(),
        None => return,
    };

    // Check if door needs a key (VB6: Llave = 0 means no key needed)
    if obj.llave == 1 {
        // Check if user has the matching key in inventory
        let has_key = state.users.get(&conn_id).map(|u| {
            u.inventory.iter().any(|s| {
                if s.obj_index <= 0 { return false; }
                state.get_object(s.obj_index)
                    .map(|ko| ko.obj_type == crate::data::objects::ObjType::Key && ko.clave == obj.clave)
                    .unwrap_or(false)
            })
        }).unwrap_or(false);

        if !has_key {
            state.send_msg_id(conn_id, 652, "");
            return;
        }
    }

    if obj.cerrada == 1 {
        // Door is CLOSED → open it

        // RejaForta (fortress gate) — guild permission check
        if obj.reja_forta == 1 {
            if obj_index == 1472 { return; } // Hardcoded locked gate
            let guild_idx = state.users.get(&conn_id).map(|u| u.guild_index).unwrap_or(0);
            if guild_idx <= 0 { return; }
        }

        let new_obj_idx = obj.index_abierta;
        if new_obj_idx <= 0 { return; }

        // VB6: Change ObjIndex FIRST, then read the NEW object's properties
        set_map_tile_obj(state, map, x, y, new_obj_idx as i16);

        // Send HO packet with the NEW object's graphic (VB6: after changing ObjIndex)
        let new_grh = state.get_object(new_obj_idx).map(|o| o.grh_index).unwrap_or(0);
        let pkt_ho = binary_packets::write_object_create(x as i16, y as i16, new_grh as i16);
        state.send_data_bytes(SendTarget::ToArea { map, x, y }, &pkt_ho);

        // Read door type from the NEW object (VB6 reads after ObjIndex change)
        let new_obj = state.get_object(new_obj_idx).cloned();
        let is_puerta_doble = new_obj.as_ref().map(|o| o.puerta_doble == 1).unwrap_or(false);
        let is_porton = new_obj.as_ref().map(|o| o.porton == 1).unwrap_or(false);

        // Determine tiles to unblock based on door type
        let tiles: Vec<i32> = if obj.reja_forta == 1 || is_porton {
            vec![x, x - 1, x - 2, x + 1, x + 2]
        } else if is_puerta_doble {
            vec![x, x - 1, x + 1, x + 2]
        } else {
            vec![x, x - 1]
        };

        // Unblock tiles and send BQ packets to entire map (VB6: Bloquear SendTarget.toMap)
        for tx in &tiles {
            set_map_tile_blocked(state, map, *tx, y, false);
            let pkt_bq = binary_packets::write_block_position(*tx as i16, y as i16, false);
            state.send_data_bytes(SendTarget::ToMap(map), &pkt_bq);
        }

        // Play door sound (VB6: SND_PUERTA = 5)
        let pkt_wave = binary_packets::write_play_wave(5, x as i16, y as i16);
        state.send_data_bytes(SendTarget::ToArea { map, x, y }, &pkt_wave);
    } else {
        // Door is OPEN → close it

        // RejaForta (fortress gate) — guild permission check
        if obj.reja_forta == 1 {
            if obj_index == 1472 { return; }
            let guild_idx = state.users.get(&conn_id).map(|u| u.guild_index).unwrap_or(0);
            if guild_idx <= 0 { return; }
        }

        let new_obj_idx = obj.index_cerrada;
        if new_obj_idx <= 0 { return; }

        // VB6: Change ObjIndex FIRST, then read the NEW (closed) object's properties
        set_map_tile_obj(state, map, x, y, new_obj_idx as i16);

        // Send HO packet with the NEW object's graphic
        let closed_obj = state.get_object(new_obj_idx).cloned();
        let new_grh = closed_obj.as_ref().map(|o| o.grh_index).unwrap_or(0);
        let pkt_ho = binary_packets::write_object_create(x as i16, y as i16, new_grh as i16);
        state.send_data_bytes(SendTarget::ToArea { map, x, y }, &pkt_ho);

        // Read door type from the NEW (closed) object
        let is_puerta_doble = closed_obj.as_ref().map(|o| o.puerta_doble == 1).unwrap_or(false);
        let is_porton = closed_obj.as_ref().map(|o| o.porton == 1).unwrap_or(false);

        // Determine tiles to block based on door type
        let tiles: Vec<i32> = if obj.reja_forta == 1 || is_porton {
            vec![x, x - 1, x - 2, x + 1, x + 2]
        } else if is_puerta_doble {
            vec![x, x - 1, x + 1, x + 2]
        } else {
            vec![x, x - 1]
        };

        // Block tiles and send BQ packets to entire map
        for tx in &tiles {
            set_map_tile_blocked(state, map, *tx, y, true);
            let pkt_bq = binary_packets::write_block_position(*tx as i16, y as i16, true);
            state.send_data_bytes(SendTarget::ToMap(map), &pkt_bq);
        }

        // Play door sound (VB6: SND_PUERTA = 5)
        let pkt_wave = binary_packets::write_play_wave(5, x as i16, y as i16);
        state.send_data_bytes(SendTarget::ToArea { map, x, y }, &pkt_wave);
    }

    // VB6: Set TargetObj position (after toggle)
    if let Some(user) = state.users.get_mut(&conn_id) {
        user.target_obj_map = map;
        user.target_obj_x = x;
        user.target_obj_y = y;
    }
}

// set_map_tile_obj, set_map_tile_blocked, health_description — moved to common.rs

/// SEG — Toggle PvP safety.
pub(crate) async fn handle_safe_toggle(state: &mut GameState, conn_id: ConnectionId) {
    let safe = match state.users.get(&conn_id) {
        Some(u) if u.logged => u.safe_toggle,
        _ => return,
    };

    // VB6: Seguro AND SeguroClan toggled together
    if let Some(user) = state.users.get_mut(&conn_id) {
        user.safe_toggle = !safe;
    }

    if safe {
        let pkt = binary_packets::write_safe_off();
        state.send_bytes(conn_id, &pkt);
    } else {
        let pkt = binary_packets::write_safe_on();
        state.send_bytes(conn_id, &pkt);
    }
}

// ── Forum system (VB6: AccionParaForo / modForum.bas) ──────────────

/// VB6 eForumMsgType — matches client protocol.
pub(crate) mod forum_msg_type {
    pub const GENERAL: u8 = 0;
    pub const GENERAL_STICKY: u8 = 1;
    pub const CAOS: u8 = 2;
    pub const CAOS_STICKY: u8 = 3;
    pub const REAL: u8 = 4;
    pub const REAL_STICKY: u8 = 5;
}

/// VB6 eForumVisibility — bitflags for ShowForumForm.
pub(crate) mod forum_visibility {
    pub const GENERAL_MEMBER: u8 = 1;
    pub const CAOS_MEMBER: u8 = 2;
    pub const REAL_MEMBER: u8 = 4;
}

const FORO_REAL_ID: &str = "REAL";
const FORO_CAOS_ID: &str = "CAOS";

/// VB6: AccionParaForo — triggered on right-click on a Forum object (ObjType 10).
/// Sends all posts for the forum board + faction boards the user can see, then opens the UI.
pub(crate) async fn accion_para_foro(state: &mut GameState, conn_id: ConnectionId, obj_index: i32) {
    let foro_id = match state.get_object(obj_index) {
        Some(obj) => obj.foro_id.clone(),
        None => return,
    };
    if foro_id.is_empty() { return; }

    // Save target object for ForumPost handler
    if let Some(user) = state.users.get_mut(&conn_id) {
        user.target_obj = obj_index;
    }

    let (is_gm, is_caos, is_armada) = match state.users.get(&conn_id) {
        Some(u) => (u.privileges >= 1, u.fuerzas_caos, u.armada_real),
        None => return,
    };

    // 1. Send general forum posts
    send_forum_posts(state, conn_id, &foro_id, forum_msg_type::GENERAL, forum_msg_type::GENERAL_STICKY).await;

    // 2. Faction-specific boards
    if is_caos || is_gm {
        send_forum_posts(state, conn_id, FORO_CAOS_ID, forum_msg_type::CAOS, forum_msg_type::CAOS_STICKY).await;
    }
    if is_armada || is_gm {
        send_forum_posts(state, conn_id, FORO_REAL_ID, forum_msg_type::REAL, forum_msg_type::REAL_STICKY).await;
    }

    // 3. Compute visibility + sticky permissions
    let mut visibility = forum_visibility::GENERAL_MEMBER;
    if is_caos || is_gm { visibility |= forum_visibility::CAOS_MEMBER; }
    if is_armada || is_gm { visibility |= forum_visibility::REAL_MEMBER; }

    let can_make_sticky = if is_gm { 2u8 } else { 0u8 };

    // 4. Show the forum form
    let pkt = binary_packets::write_show_forum_form(visibility, can_make_sticky);
    state.send_bytes(conn_id, &pkt);
}

/// Send all posts (regular + sticky) from a specific forum board to a user.
async fn send_forum_posts(
    state: &mut GameState,
    conn_id: ConnectionId,
    forum_id: &str,
    regular_type: u8,
    sticky_type: u8,
) {
    let forum = match state.forums.get(forum_id) {
        Some(f) => f.clone(),
        None => return,
    };

    for post in &forum.posts {
        let pkt = binary_packets::write_add_forum_msg(regular_type, &post.title, &post.author, &post.body);
        state.send_bytes(conn_id, &pkt);
    }
    for post in &forum.stickies {
        let pkt = binary_packets::write_add_forum_msg(sticky_type, &post.title, &post.author, &post.body);
        state.send_bytes(conn_id, &pkt);
    }
}

/// Handle client ForumPost packet — add a new post to a forum board.
/// VB6: HandleForumPost — reads ForumMsgType(byte) + Title(string) + Post(string).
pub(crate) async fn handle_forum_post(state: &mut GameState, conn_id: ConnectionId, msg_type: u8, title: String, body: String) {
    use crate::game::types::{ForumPost, MAX_FORUM_POSTS, MAX_FORUM_STICKIES};

    let (author, target_obj, is_gm) = match state.users.get(&conn_id) {
        Some(u) if u.logged && !u.dead => (u.char_name.clone(), u.target_obj, u.privileges >= 1),
        _ => return,
    };

    if target_obj <= 0 || title.is_empty() || body.is_empty() { return; }

    // Determine forum ID based on message type
    let forum_id = match msg_type {
        forum_msg_type::CAOS | forum_msg_type::CAOS_STICKY => FORO_CAOS_ID.to_string(),
        forum_msg_type::REAL | forum_msg_type::REAL_STICKY => FORO_REAL_ID.to_string(),
        _ => {
            // General forum — get ID from the target object
            match state.get_object(target_obj) {
                Some(obj) if !obj.foro_id.is_empty() => obj.foro_id.clone(),
                _ => return,
            }
        }
    };

    let is_sticky = matches!(msg_type,
        forum_msg_type::GENERAL_STICKY | forum_msg_type::CAOS_STICKY | forum_msg_type::REAL_STICKY);

    // Only GMs can make stickies
    if is_sticky && !is_gm { return; }

    let post = ForumPost { title, author, body };

    let forum = state.forums.entry(forum_id).or_default();
    if is_sticky {
        forum.stickies.insert(0, post);
        forum.stickies.truncate(MAX_FORUM_STICKIES);
    } else {
        forum.posts.insert(0, post);
        forum.posts.truncate(MAX_FORUM_POSTS);
    }
}
