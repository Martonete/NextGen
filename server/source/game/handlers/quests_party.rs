//! Party system handlers (mdParty.bas).

use crate::net::ConnectionId;
use crate::game::types::{GameState, PartyState, MAX_PARTIES, MAX_PARTY_MEMBERS};
use crate::protocol::font_index;
use super::common::*;

// =====================================================================
// Party system handlers (mdParty.bas)
// =====================================================================

/// /NUEVAPARTY — Create a new party.
pub(super) async fn handle_slash_nuevaparty(state: &mut GameState, conn_id: ConnectionId) {
    let party_index = match state.users.get(&conn_id) {
        Some(u) if u.logged => u.party_index,
        _ => return,
    };

    if party_index > 0 {
        state.send_console(conn_id, "Ya perteneces a un grupo.", font_index::INFO).await;
        return;
    }

    // Find free party slot
    let mut new_index = 0;
    for i in 1..=MAX_PARTIES {
        if state.parties[i].is_none() {
            new_index = i as i32;
            break;
        }
    }

    if new_index == 0 {
        state.send_console(conn_id, "No se pueden crear mas grupos.", font_index::INFO).await;
        return;
    }

    // Create party
    state.parties[new_index as usize] = Some(PartyState {
        leader: conn_id,
        members: vec![conn_id],
    });

    if let Some(user) = state.users.get_mut(&conn_id) {
        user.party_index = new_index;
    }

    state.send_console(conn_id, "Has creado un grupo. Usa /PARTY <nombre> para invitar jugadores.", font_index::INFO).await;
}

/// /PARTY <target> — Invite a player to party (leader only, max 3 tiles distance).
pub(super) async fn handle_slash_party_invite(state: &mut GameState, conn_id: ConnectionId, target_name: &str) {
    let (party_index, map, x, y) = match state.users.get(&conn_id) {
        Some(u) if u.logged && u.party_index > 0 => (u.party_index, u.pos_map, u.pos_x, u.pos_y),
        Some(u) if u.logged => {
            state.send_console(conn_id, "No perteneces a un grupo. Usa /NUEVAPARTY.", font_index::INFO).await;
            return;
        }
        _ => return,
    };

    // Must be leader
    let is_leader = state.parties.get(party_index as usize)
        .and_then(|p| p.as_ref())
        .map(|p| p.leader == conn_id)
        .unwrap_or(false);
    if !is_leader {
        state.send_console(conn_id, "Solo el lider puede invitar jugadores.", font_index::INFO).await;
        return;
    }

    // Check party not full
    let member_count = state.parties.get(party_index as usize)
        .and_then(|p| p.as_ref())
        .map(|p| p.members.len())
        .unwrap_or(0);
    if member_count >= MAX_PARTY_MEMBERS {
        state.send_console(conn_id, "El grupo esta lleno.", font_index::INFO).await;
        return;
    }

    // Find target
    let target_conn = match state.online_names.get(&target_name.to_uppercase()) {
        Some(&c) => c,
        None => {
            state.send_console(conn_id, "El jugador no esta conectado.", font_index::INFO).await;
            return;
        }
    };

    let target_data = match state.users.get(&target_conn) {
        Some(u) if u.logged => (u.party_index, u.pos_map, u.pos_x, u.pos_y, u.dead),
        _ => return,
    };
    let (t_party, t_map, t_x, t_y, t_dead) = target_data;

    if t_dead {
        state.send_console(conn_id, "No puedes invitar a un muerto.", font_index::INFO).await;
        return;
    }

    if t_party > 0 {
        state.send_console(conn_id, "El jugador ya esta en un grupo.", font_index::INFO).await;
        return;
    }

    // Check distance (max 3 tiles)
    if t_map != map || (t_x - x).abs() > 3 || (t_y - y).abs() > 3 {
        state.send_console(conn_id, "El jugador esta muy lejos.", font_index::INFO).await;
        return;
    }

    // Set pending invite
    if let Some(user) = state.users.get_mut(&target_conn) {
        user.party_pending = party_index;
    }

    let inviter_name = state.users.get(&conn_id).map(|u| u.char_name.clone()).unwrap_or_default();
    state.send_console(target_conn, &format!("{} te ha invitado a un grupo. Usa /ACEPTAR o /CANCELAR.", inviter_name), font_index::INFO).await;

    state.send_console(conn_id, &format!("Has invitado a {} al grupo.", target_name), font_index::INFO).await;
}

/// /ACEPTAR — Accept party invite.
pub(super) async fn handle_slash_party_accept(state: &mut GameState, conn_id: ConnectionId) {
    let (party_pending, char_name) = match state.users.get(&conn_id) {
        Some(u) if u.logged => (u.party_pending, u.char_name.clone()),
        _ => return,
    };

    if party_pending <= 0 {
        state.send_console(conn_id, "No tienes ninguna invitacion pendiente.", font_index::INFO).await;
        return;
    }

    // Check party still exists and not full
    let party_ok = state.parties.get(party_pending as usize)
        .and_then(|p| p.as_ref())
        .map(|p| p.members.len() < MAX_PARTY_MEMBERS)
        .unwrap_or(false);

    if !party_ok {
        state.send_console(conn_id, "El grupo ya no existe o esta lleno.", font_index::INFO).await;
        if let Some(user) = state.users.get_mut(&conn_id) {
            user.party_pending = 0;
        }
        return;
    }

    // Add to party
    if let Some(Some(party)) = state.parties.get_mut(party_pending as usize) {
        party.members.push(conn_id);
    }

    if let Some(user) = state.users.get_mut(&conn_id) {
        user.party_index = party_pending;
        user.party_pending = 0;
    }

    // Notify all party members
    let notify = format!("{} se ha unido al grupo.", char_name);
    send_console_to_party(state, party_pending, &notify, font_index::INFO).await;
}

/// /CANCELAR — Leave party or reject invite.
pub(super) async fn handle_slash_party_cancel(state: &mut GameState, conn_id: ConnectionId) {
    let (party_index, party_pending, char_name) = match state.users.get(&conn_id) {
        Some(u) if u.logged => (u.party_index, u.party_pending, u.char_name.clone()),
        _ => return,
    };

    // If has pending invite, reject it
    if party_pending > 0 {
        if let Some(user) = state.users.get_mut(&conn_id) {
            user.party_pending = 0;
        }
        state.send_console(conn_id, "Has rechazado la invitacion.", font_index::INFO).await;
        return;
    }

    if party_index <= 0 {
        state.send_console(conn_id, "No perteneces a un grupo.", font_index::INFO).await;
        return;
    }

    // Check if leader — leader uses /FINPARTY
    let is_leader = state.parties.get(party_index as usize)
        .and_then(|p| p.as_ref())
        .map(|p| p.leader == conn_id)
        .unwrap_or(false);

    if is_leader {
        state.send_console(conn_id, "Eres el lider. Usa /FINPARTY para disolver el grupo.", font_index::INFO).await;
        return;
    }

    // Remove from party
    if let Some(Some(party)) = state.parties.get_mut(party_index as usize) {
        party.members.retain(|&c| c != conn_id);
    }

    if let Some(user) = state.users.get_mut(&conn_id) {
        user.party_index = 0;
    }

    // Notify
    let notify = format!("{} ha abandonado el grupo.", char_name);
    send_console_to_party(state, party_index, &notify, font_index::INFO).await;

    state.send_console(conn_id, "Has abandonado el grupo.", font_index::INFO).await;
}

/// /FINPARTY — Disband party (leader only).
pub(super) async fn handle_slash_finparty(state: &mut GameState, conn_id: ConnectionId) {
    let party_index = match state.users.get(&conn_id) {
        Some(u) if u.logged && u.party_index > 0 => u.party_index,
        _ => {
            state.send_console(conn_id, "No perteneces a un grupo.", font_index::INFO).await;
            return;
        }
    };

    let is_leader = state.parties.get(party_index as usize)
        .and_then(|p| p.as_ref())
        .map(|p| p.leader == conn_id)
        .unwrap_or(false);

    if !is_leader {
        state.send_console(conn_id, "Solo el lider puede disolver el grupo.", font_index::INFO).await;
        return;
    }

    // Notify all members
    send_console_to_party(state, party_index, "El grupo ha sido disuelto.", font_index::INFO).await;

    // Get member list before clearing
    let members: Vec<ConnectionId> = state.parties.get(party_index as usize)
        .and_then(|p| p.as_ref())
        .map(|p| p.members.clone())
        .unwrap_or_default();

    // Clear party state for all members
    for &member_conn in &members {
        if let Some(user) = state.users.get_mut(&member_conn) {
            user.party_index = 0;
        }
    }

    // Remove party
    state.parties[party_index as usize] = None;
}

/// /PINFO — View party members.
pub(super) async fn handle_slash_pinfo(state: &mut GameState, conn_id: ConnectionId) {
    let party_index = match state.users.get(&conn_id) {
        Some(u) if u.logged && u.party_index > 0 => u.party_index,
        _ => {
            state.send_console(conn_id, "No perteneces a un grupo.", font_index::INFO).await;
            return;
        }
    };

    let members: Vec<ConnectionId> = state.parties.get(party_index as usize)
        .and_then(|p| p.as_ref())
        .map(|p| p.members.clone())
        .unwrap_or_default();

    let leader_conn = state.parties.get(party_index as usize)
        .and_then(|p| p.as_ref())
        .map(|p| p.leader)
        .unwrap_or(0);

    state.send_console(conn_id, "--- Miembros del grupo ---", font_index::GUILD_MSG).await;

    for &member_conn in &members {
        if let Some(user) = state.users.get(&member_conn) {
            let role = if member_conn == leader_conn { " [Lider]" } else { "" };
            state.send_console(conn_id, &format!("  {}{}", user.char_name, role), font_index::INFO).await;
        }
    }
}

/// Send binary packet to all party members.
pub(super) async fn send_to_party(state: &mut GameState, party_index: i32, data: &[u8]) {
    let members: Vec<ConnectionId> = state.parties.get(party_index as usize)
        .and_then(|p| p.as_ref())
        .map(|p| p.members.clone())
        .unwrap_or_default();

    for &member_conn in &members {
        state.send_bytes(member_conn, data).await;
    }
}

/// Send a binary console message to all party members.
pub(super) async fn send_console_to_party(state: &mut GameState, party_index: i32, msg: &str, font: u8) {
    let members: Vec<ConnectionId> = state.parties.get(party_index as usize)
        .and_then(|p| p.as_ref())
        .map(|p| p.members.clone())
        .unwrap_or_default();

    for &member_conn in &members {
        state.send_console(member_conn, msg, font).await;
    }
}

/// Share experience among party members within 15 tiles.
pub async fn party_share_exp(state: &mut GameState, killer_conn: ConnectionId, total_exp: i64) {
    let (party_index, map, x, y) = match state.users.get(&killer_conn) {
        Some(u) if u.logged && u.party_index > 0 => (u.party_index, u.pos_map, u.pos_x, u.pos_y),
        _ => return,
    };

    let members: Vec<ConnectionId> = state.parties.get(party_index as usize)
        .and_then(|p| p.as_ref())
        .map(|p| p.members.clone())
        .unwrap_or_default();

    // Filter members within 15 tiles on same map
    let nearby: Vec<ConnectionId> = members.iter()
        .copied()
        .filter(|&c| {
            state.users.get(&c)
                .map(|u| u.logged && u.pos_map == map && (u.pos_x - x).abs() <= 15 && (u.pos_y - y).abs() <= 15)
                .unwrap_or(false)
        })
        .collect();

    if nearby.is_empty() { return; }

    let share = total_exp / nearby.len() as i64;
    if share <= 0 { return; }

    for &member_conn in &nearby {
        if let Some(user) = state.users.get_mut(&member_conn) {
            user.exp += share;
        }
        send_stats_exp(state, member_conn).await;
    }
}
