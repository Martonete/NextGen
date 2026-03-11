//! Party system handlers (mdParty.bas + clsParty.cls).

use crate::net::ConnectionId;
use crate::game::types::{GameState, PartyState, MAX_PARTIES, MAX_PARTY_MEMBERS, PARTY_MAX_DISTANCE, MAX_DISTANCE_INGRESO_PARTY};
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
        state.send_console(conn_id, "Ya perteneces a un grupo.", font_index::INFO);
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
        state.send_console(conn_id, "No se pueden crear mas grupos.", font_index::INFO);
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

    state.send_console(conn_id, "Has creado un grupo. Usa /PARTY <nombre> para invitar jugadores.", font_index::INFO);
}

/// /PARTY <target> — Invite a player to party (leader only, max 2 tiles distance).
/// VB6: SolicitarIngresoAParty + AprobarIngresoAParty combined.
pub(super) async fn handle_slash_party_invite(state: &mut GameState, conn_id: ConnectionId, target_name: &str) {
    let (party_index, map, x, y) = match state.users.get(&conn_id) {
        Some(u) if u.logged && u.party_index > 0 => (u.party_index, u.pos_map, u.pos_x, u.pos_y),
        Some(u) if u.logged => {
            state.send_console(conn_id, "No perteneces a un grupo. Usa /NUEVAPARTY.", font_index::INFO);
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
        state.send_console(conn_id, "Solo el lider puede invitar jugadores.", font_index::INFO);
        return;
    }

    // Check party not full
    let member_count = state.parties.get(party_index as usize)
        .and_then(|p| p.as_ref())
        .map(|p| p.members.len())
        .unwrap_or(0);
    if member_count >= MAX_PARTY_MEMBERS {
        state.send_console(conn_id, "El grupo esta lleno.", font_index::INFO);
        return;
    }

    // Find target
    let target_conn = match state.online_names.get(&target_name.to_uppercase()) {
        Some(&c) => c,
        None => {
            state.send_console(conn_id, "El jugador no esta conectado.", font_index::INFO);
            return;
        }
    };

    let target_data = match state.users.get(&target_conn) {
        Some(u) if u.logged => (u.party_index, u.pos_map, u.pos_x, u.pos_y, u.dead),
        _ => return,
    };
    let (t_party, t_map, t_x, t_y, t_dead) = target_data;

    if t_dead {
        state.send_console(conn_id, "No puedes invitar a un muerto.", font_index::INFO);
        return;
    }

    if t_party > 0 {
        state.send_console(conn_id, "El jugador ya esta en un grupo.", font_index::INFO);
        return;
    }

    // Check distance (VB6: MAXDISTANCIAINGRESOPARTY = 2)
    if t_map != map || (t_x - x).abs() > MAX_DISTANCE_INGRESO_PARTY || (t_y - y).abs() > MAX_DISTANCE_INGRESO_PARTY {
        state.send_console(conn_id, "El jugador esta muy lejos.", font_index::INFO);
        return;
    }

    // Set pending invite
    if let Some(user) = state.users.get_mut(&target_conn) {
        user.party_pending = party_index;
    }

    let inviter_name = state.users.get(&conn_id).map(|u| u.char_name.clone()).unwrap_or_default();
    state.send_console(target_conn, &format!("{} te ha invitado a un grupo. Usa /ACEPTAR o /CANCELAR.", inviter_name), font_index::INFO);

    state.send_console(conn_id, &format!("Has invitado a {} al grupo.", target_name), font_index::INFO);
}

/// /ACEPTAR — Accept party invite.
pub(super) async fn handle_slash_party_accept(state: &mut GameState, conn_id: ConnectionId) {
    let (party_pending, char_name) = match state.users.get(&conn_id) {
        Some(u) if u.logged => (u.party_pending, u.char_name.clone()),
        _ => return,
    };

    if party_pending <= 0 {
        state.send_console(conn_id, "No tienes ninguna invitacion pendiente.", font_index::INFO);
        return;
    }

    // Check party still exists and not full
    let party_ok = state.parties.get(party_pending as usize)
        .and_then(|p| p.as_ref())
        .map(|p| p.members.len() < MAX_PARTY_MEMBERS)
        .unwrap_or(false);

    if !party_ok {
        state.send_console(conn_id, "El grupo ya no existe o esta lleno.", font_index::INFO);
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
    send_console_to_party(state, party_pending, &notify, font_index::INFO);
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
        state.send_console(conn_id, "Has rechazado la invitacion.", font_index::INFO);
        return;
    }

    if party_index <= 0 {
        state.send_console(conn_id, "No perteneces a un grupo.", font_index::INFO);
        return;
    }

    // Check if leader — leader uses /FINPARTY
    let is_leader = state.parties.get(party_index as usize)
        .and_then(|p| p.as_ref())
        .map(|p| p.leader == conn_id)
        .unwrap_or(false);

    if is_leader {
        state.send_console(conn_id, "Eres el lider. Usa /FINPARTY para disolver el grupo.", font_index::INFO);
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
    send_console_to_party(state, party_index, &notify, font_index::INFO);

    state.send_console(conn_id, "Has abandonado el grupo.", font_index::INFO);
}

/// /FINPARTY — Disband party (leader only).
pub(super) async fn handle_slash_finparty(state: &mut GameState, conn_id: ConnectionId) {
    let party_index = match state.users.get(&conn_id) {
        Some(u) if u.logged && u.party_index > 0 => u.party_index,
        _ => {
            state.send_console(conn_id, "No perteneces a un grupo.", font_index::INFO);
            return;
        }
    };

    let is_leader = state.parties.get(party_index as usize)
        .and_then(|p| p.as_ref())
        .map(|p| p.leader == conn_id)
        .unwrap_or(false);

    if !is_leader {
        state.send_console(conn_id, "Solo el lider puede disolver el grupo.", font_index::INFO);
        return;
    }

    // Notify all members
    send_console_to_party(state, party_index, "El grupo ha sido disuelto.", font_index::INFO);

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
            state.send_console(conn_id, "No perteneces a un grupo.", font_index::INFO);
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

    state.send_console(conn_id, "--- Miembros del grupo ---", font_index::GUILD_MSG);

    for &member_conn in &members {
        if let Some(user) = state.users.get(&member_conn) {
            let role = if member_conn == leader_conn { " [Lider]" } else { "" };
            state.send_console(conn_id, &format!("  {}{}", user.char_name, role), font_index::INFO);
        }
    }
}

/// /SACAR <name> — Kick a member from party (leader only).
/// VB6: ExpulsarDeParty in mdParty.bas
pub(super) async fn handle_slash_sacar(state: &mut GameState, conn_id: ConnectionId, target_name: &str) {
    let party_index = match state.users.get(&conn_id) {
        Some(u) if u.logged && u.party_index > 0 => u.party_index,
        _ => {
            state.send_console(conn_id, "No perteneces a un grupo.", font_index::INFO);
            return;
        }
    };

    // Must be leader
    let is_leader = state.parties.get(party_index as usize)
        .and_then(|p| p.as_ref())
        .map(|p| p.leader == conn_id)
        .unwrap_or(false);
    if !is_leader {
        state.send_console(conn_id, "Solo el lider puede expulsar jugadores.", font_index::INFO);
        return;
    }

    // Find target
    let target_conn = match state.online_names.get(&target_name.to_uppercase()) {
        Some(&c) => c,
        None => {
            state.send_console(conn_id, "El jugador no esta conectado.", font_index::INFO);
            return;
        }
    };

    // Can't kick yourself (leader)
    if target_conn == conn_id {
        state.send_console(conn_id, "No puedes expulsarte a ti mismo. Usa /FINPARTY.", font_index::INFO);
        return;
    }

    // Check target is in same party (VB6: PI = UserList(OldMember).PartyIndex, if PI = leader's PI)
    let target_party = state.users.get(&target_conn).map(|u| u.party_index).unwrap_or(0);
    if target_party != party_index {
        let target_display = state.users.get(&target_conn).map(|u| u.char_name.clone()).unwrap_or_default();
        state.send_console(conn_id, &format!("{} no pertenece a tu grupo.", target_display), font_index::INFO);
        return;
    }

    let target_name_display = state.users.get(&target_conn).map(|u| u.char_name.clone()).unwrap_or_default();

    // Remove from party
    if let Some(Some(party)) = state.parties.get_mut(party_index as usize) {
        party.members.retain(|&c| c != target_conn);
    }

    if let Some(user) = state.users.get_mut(&target_conn) {
        user.party_index = 0;
    }

    // Notify
    let notify = format!("{} ha sido expulsado del grupo.", target_name_display);
    send_console_to_party(state, party_index, &notify, font_index::INFO);
    state.send_console(target_conn, "Has sido expulsado del grupo.", font_index::INFO);
}

/// /DARPARTIDO <name> — Transfer party leadership (leader only).
/// VB6: TransformarEnLider in mdParty.bas
pub(super) async fn handle_slash_darpartido(state: &mut GameState, conn_id: ConnectionId, target_name: &str) {
    let party_index = match state.users.get(&conn_id) {
        Some(u) if u.logged && u.party_index > 0 => u.party_index,
        _ => {
            state.send_console(conn_id, "No perteneces a un grupo.", font_index::INFO);
            return;
        }
    };

    // Must be leader
    let is_leader = state.parties.get(party_index as usize)
        .and_then(|p| p.as_ref())
        .map(|p| p.leader == conn_id)
        .unwrap_or(false);
    if !is_leader {
        state.send_console(conn_id, "Solo el lider puede transferir el liderazgo.", font_index::INFO);
        return;
    }

    // Find target
    let target_conn = match state.online_names.get(&target_name.to_uppercase()) {
        Some(&c) => c,
        None => {
            state.send_console(conn_id, "El jugador no esta conectado.", font_index::INFO);
            return;
        }
    };

    if target_conn == conn_id {
        state.send_console(conn_id, "Ya eres el lider.", font_index::INFO);
        return;
    }

    // Check target is in same party
    let target_party = state.users.get(&target_conn).map(|u| u.party_index).unwrap_or(0);
    if target_party != party_index {
        let target_display = state.users.get(&target_conn).map(|u| u.char_name.clone()).unwrap_or_default();
        state.send_console(conn_id, &format!("{} no pertenece a tu grupo.", target_display), font_index::INFO);
        return;
    }

    // Check target not dead (VB6 check)
    let target_dead = state.users.get(&target_conn).map(|u| u.dead).unwrap_or(true);
    if target_dead {
        state.send_console(conn_id, "Esta muerto!", font_index::INFO);
        return;
    }

    let target_name_display = state.users.get(&target_conn).map(|u| u.char_name.clone()).unwrap_or_default();
    let old_leader_name = state.users.get(&conn_id).map(|u| u.char_name.clone()).unwrap_or_default();

    // Transfer leadership (VB6: HacerLeader swaps positions in array)
    if let Some(Some(party)) = state.parties.get_mut(party_index as usize) {
        party.leader = target_conn;
    }

    let notify = format!("El nuevo lider del grupo es {}.", target_name_display);
    send_console_to_party(state, party_index, &notify, font_index::INFO);
}

/// Send binary packet to all party members.
pub(super) fn send_to_party(state: &mut GameState, party_index: i32, data: &[u8]) {
    let members: Vec<ConnectionId> = state.parties.get(party_index as usize)
        .and_then(|p| p.as_ref())
        .map(|p| p.members.clone())
        .unwrap_or_default();

    for &member_conn in &members {
        state.send_bytes(member_conn, data);
    }
}

/// Send a binary console message to all party members.
pub(super) fn send_console_to_party(state: &mut GameState, party_index: i32, msg: &str, font: u8) {
    let members: Vec<ConnectionId> = state.parties.get(party_index as usize)
        .and_then(|p| p.as_ref())
        .map(|p| p.members.clone())
        .unwrap_or_default();

    for &member_conn in &members {
        state.send_console(member_conn, msg, font);
    }
}

/// Share experience among party members within 18 tiles.
/// VB6: clsParty.ObtenerExito — Formula: Exp * (Level ^ ExponenteNivelParty) / SumaNivelesElevados
/// ExponenteNivelParty default = 1.75 (from Balance.dat)
pub async fn party_share_exp(state: &mut GameState, killer_conn: ConnectionId, total_exp: i64) {
    let (party_index, map, x, y) = match state.users.get(&killer_conn) {
        Some(u) if u.logged && u.party_index > 0 => (u.party_index, u.pos_map, u.pos_x, u.pos_y),
        _ => return,
    };

    let members: Vec<ConnectionId> = state.parties.get(party_index as usize)
        .and_then(|p| p.as_ref())
        .map(|p| p.members.clone())
        .unwrap_or_default();

    let exponent = state.game_data.balance.exponente_nivel_party as f64;

    // Collect levels and filter nearby+alive members (VB6: same map, within PARTY_MAXDISTANCIA, not dead)
    let mut nearby: Vec<(ConnectionId, f64)> = Vec::new();
    let mut sum_levels_elevated: f64 = 0.0;

    for &c in &members {
        if let Some(u) = state.users.get(&c) {
            let level_elevated = (u.level as f64).powf(exponent);
            // VB6: all members get their share calculated, but only nearby+alive receive it
            if u.logged && u.pos_map == map && !u.dead
                && (u.pos_x - x).abs() <= PARTY_MAX_DISTANCE
                && (u.pos_y - y).abs() <= PARTY_MAX_DISTANCE
            {
                nearby.push((c, level_elevated));
            }
            // Sum ALL members' levels (VB6: p_SumaNivelesElevados includes everyone)
            sum_levels_elevated += level_elevated;
        }
    }

    if nearby.is_empty() || sum_levels_elevated <= 0.0 { return; }

    // VB6: expThisUser = ExpGanada * (Level ^ ExponenteNivelParty) / p_SumaNivelesElevados
    for (member_conn, level_elevated) in &nearby {
        let exp_this_user = (total_exp as f64 * level_elevated / sum_levels_elevated).floor() as i64;
        if exp_this_user <= 0 { continue; }

        if let Some(user) = state.users.get_mut(member_conn) {
            user.exp += exp_this_user;
        }
        send_stats_exp(state, *member_conn).await;
    }
}
