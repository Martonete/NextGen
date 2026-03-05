//! Quest and party system handlers.
//! Extracted from mod.rs to reduce file size.

use tracing::info;
use crate::net::ConnectionId;
use crate::game::types::{GameState, SendTarget, PartyState, MAX_PARTIES, MAX_PARTY_MEMBERS};
use crate::protocol::{font_index, fields::read_field, binary_packets};
use crate::data::quests;
use super::common::*;
use super::{send_inventory_slot, check_user_level};

// =====================================================================
// Quest system handlers (modQuests.bas)
// =====================================================================

/// Format a number with period separators (VB6 PonerPuntos).
/// e.g. 200000 -> "200.000", 1500 -> "1.500"
// poner_puntos — moved to common.rs

/// IQUEST — Request quest list + current quest progress.
/// VB6: QTL<count>,<name1>,<name2>,...  then MQC<cantNPC>,<muereQuest>,<name>,<oro>,<ptsTorneo>,<creditos>,<ptsTS>
pub(super) async fn handle_quest_list(state: &mut GameState, conn_id: ConnectionId) {
    let (questeando, quest_num, quest_kills) = match state.users.get(&conn_id) {
        Some(u) if u.logged => (u.questeando, u.quest_num, u.quest_kills),
        _ => return,
    };

    // Send quest list: QTL<count>,<name1>,<name2>,...
    let num_quests = state.game_data.quests.len();
    let mut data = num_quests.to_string();
    for quest in &state.game_data.quests {
        data.push(',');
        data.push_str(&quest.name);
    }
    let pkt = binary_packets::write_quest_list_data(&data);
    state.send_bytes(conn_id, &pkt).await;

    // If currently on a quest, send progress
    // VB6: MQC<cantNPC>,<muereQuest>,<name>,<oro>,<ptsTorneo>,<creditos>,<ptsTS>
    if questeando && quest_num > 0 {
        if let Some(quest) = state.game_data.quests.get(quest_num as usize - 1) {
            let target = quests::quest_target(quest);
            let data = format!("{},{},{},{},{},{},{}",
                target, quest_kills, quest.name, quest.oro, quest.pts_torneo, quest.creditos, quest.pts_ts
            );
            let pkt = binary_packets::write_quest_current_data(&data);
            state.send_bytes(conn_id, &pkt).await;
        }
    }
}

/// INFD — Get details for quest selection (before accepting).
/// VB6: MQS<Name>,<Oro>,<ptsTorneo>,<Creditos>,<ptsTS>
pub(super) async fn handle_quest_info(state: &mut GameState, conn_id: ConnectionId, data: &str) {
    let payload = strip_opcode(data, 4);
    // VB6: ReadField(1, rData, 44) — field separator is comma (ASCII 44)
    let quest_id: i32 = payload.split(',').next().unwrap_or("").trim().parse().unwrap_or(0);

    if !state.users.get(&conn_id).map(|u| u.logged).unwrap_or(false) { return; }

    if quest_id <= 0 || quest_id as usize > state.game_data.quests.len() { return; }

    let quest = &state.game_data.quests[quest_id as usize - 1];

    // VB6 format: MQS<Name>,<Oro>,<ptsTorneo>,<Creditos>,<ptsTS>
    let data = format!("{},{},{},{},{}",
        quest.name, quest.oro, quest.pts_torneo, quest.creditos, quest.pts_ts
    );
    let pkt = binary_packets::write_quest_selected_data(&data);
    state.send_bytes(conn_id, &pkt).await;
}

/// ACQT — Accept a quest.
/// VB6: checks already questeando, PK map, then accepts.
pub(super) async fn handle_quest_accept(state: &mut GameState, conn_id: ConnectionId, data: &str) {
    let payload = strip_opcode(data, 4);
    // VB6: ReadField(1, rData, 44) — field separator is comma
    let quest_id: i32 = payload.split(',').next().unwrap_or("").trim().parse().unwrap_or(0);

    let (questeando, quest_num, char_name, user_map) = match state.users.get(&conn_id) {
        Some(u) if u.logged => (u.questeando, u.quest_num, u.char_name.clone(), u.pos_map),
        _ => return,
    };

    // VB6: If Questeando = 1 Or UserNumQuest > 0 Then send ||279
    if questeando || quest_num > 0 {
        state.send_msg_id(conn_id, 279, "").await;
        return;
    }

    // VB6: If MapInfo(Map).Pk = True Then send ||291
    let map_idx = user_map as usize;
    let is_pk = state.game_data.maps.get(map_idx)
        .and_then(|m| m.as_ref())
        .map(|m| m.info.pk)
        .unwrap_or(false);
    if is_pk {
        state.send_msg_id(conn_id, 291, "").await;
        return;
    }

    if quest_id <= 0 || quest_id as usize > state.game_data.quests.len() { return; }

    // VB6: send ||280, set flags
    state.send_msg_id(conn_id, 280, "").await;

    if let Some(user) = state.users.get_mut(&conn_id) {
        user.questeando = true;
        user.quest_num = quest_id;
        user.quest_kills = 0;
    }
}

/// /QUEST — Show quest info (same as IQUEST)
pub(super) async fn handle_slash_quest(state: &mut GameState, conn_id: ConnectionId) {
    handle_quest_list(state, conn_id).await;
}

/// /NOQUEST — Abandon current quest.
/// VB6: sends ||304 if no quest, ||305 if abandoned.
pub(super) async fn handle_slash_noquest(state: &mut GameState, conn_id: ConnectionId) {
    let (questeando, char_name) = match state.users.get(&conn_id) {
        Some(u) if u.logged => (u.questeando, u.char_name.clone()),
        _ => return,
    };

    if !questeando {
        state.send_msg_id(conn_id, 304, "").await;
        return;
    }

    if let Some(user) = state.users.get_mut(&conn_id) {
        user.questeando = false;
        user.quest_num = 0;
        user.quest_kills = 0;
    }

    state.send_msg_id(conn_id, 305, "").await;
}

/// Check if an NPC kill counts towards quest progress.
/// VB6: modQuests.RestarNPC — only counts if NPC type matches quest's numNPC.
pub async fn quest_check_npc_kill(state: &mut GameState, killer_conn: ConnectionId, npc_type: i32) {
    let (questeando, quest_num, quest_kills, char_name) = match state.users.get(&killer_conn) {
        Some(u) if u.logged && u.questeando => (true, u.quest_num, u.quest_kills, u.char_name.clone()),
        _ => return,
    };

    if !questeando || quest_num <= 0 { return; }

    let quest = match state.game_data.quests.get(quest_num as usize - 1) {
        Some(q) if q.quest_type == quests::QuestType::KillNpc && q.mata_npc == npc_type => q.clone(),
        _ => return,
    };

    // Increment kill counter
    let new_kills = quest_kills + 1;
    if let Some(user) = state.users.get_mut(&killer_conn) {
        user.quest_kills = new_kills;
    }

    let target = quests::quest_target(&quest);

    if new_kills >= target {
        quest_complete(state, killer_conn, &char_name, &quest).await;
    }
}

/// Check if a player kill counts towards quest progress.
/// VB6: modQuests.RestarUser — only counts if NOT in TRIGGER6 (combat zone).
pub async fn quest_check_player_kill(state: &mut GameState, killer_conn: ConnectionId, victim_conn: ConnectionId) {
    let (questeando, quest_num, quest_kills, char_name) = match state.users.get(&killer_conn) {
        Some(u) if u.logged && u.questeando => (true, u.quest_num, u.quest_kills, u.char_name.clone()),
        _ => return,
    };

    if !questeando || quest_num <= 0 { return; }

    // VB6: TriggerZonaPelea(userindex, VictimIndex) <> TRIGGER6_PERMITE
    // Kills in TRIGGER6 combat zones don't count for quests
    let (v_map, v_x, v_y) = match state.users.get(&victim_conn) {
        Some(v) => (v.pos_map, v.pos_x, v.pos_y),
        None => return,
    };
    let victim_trigger = get_map_tile_trigger(state, v_map, v_x, v_y);
    if victim_trigger == crate::data::maps::Trigger::CombatZone { return; }

    let quest = match state.game_data.quests.get(quest_num as usize - 1) {
        Some(q) if q.quest_type == quests::QuestType::KillPlayers => q.clone(),
        _ => return,
    };

    let new_kills = quest_kills + 1;
    if let Some(user) = state.users.get_mut(&killer_conn) {
        user.quest_kills = new_kills;
    }

    let target = quests::quest_target(&quest);

    if new_kills >= target {
        quest_complete(state, killer_conn, &char_name, &quest).await;
    }
}

/// Complete a quest — award ALL rewards matching VB6 exactly.
/// VB6: modQuests.bas lines 56-98 — Gold, ptsTorneo, ptsTS, Creditos, Reputation.
/// Premium/Estado multiplier: 2x for Gold, ptsTorneo, Reputation if estado!=0 OR EsPremium!=0.
pub(super) async fn quest_complete(state: &mut GameState, conn_id: ConnectionId, char_name: &str, quest: &quests::QuestData) {
    // VB6: send ||66 (quest complete message)
    state.send_msg_id(conn_id, 66, "").await;

    // Note: VB6 has estado/EsPremium multiplier (2x if either is nonzero).
    // We don't track these flags yet, so we use 1x multiplier for now.
    // When es_premium/estado are added, update this.
    let multiplier: i64 = 1;

    // Gold reward
    if quest.oro > 0 {
        let gold_reward = quest.oro * multiplier;
        if let Some(user) = state.users.get_mut(&conn_id) {
            user.gold += gold_reward;
        }
        // VB6: ||63@<formatted_gold>
        let formatted = poner_puntos(gold_reward);
        state.send_msg_id(conn_id, 63, &formatted).await;
    }

    // Tournament points reward
    if quest.pts_torneo > 0 {
        let pts_reward = quest.pts_torneo as i64 * multiplier;
        if let Some(user) = state.users.get_mut(&conn_id) {
            user.puntos_torneo += pts_reward;
        }
        // VB6: ||57@<pts>
        state.send_msg_id(conn_id, 57, &pts_reward.to_string()).await;
        // VB6: AgregarPuntos sends PNT<total_pts>
        let total_pts = state.users.get(&conn_id).map(|u| u.puntos_torneo).unwrap_or(0);
        let pkt = binary_packets::write_tournament_points(total_pts as i32);
        state.send_bytes(conn_id, &pkt).await;
    }

    // TS points reward (no multiplier in VB6)
    if quest.pts_ts > 0 {
        let ts_reward = quest.pts_ts as i64;
        if let Some(user) = state.users.get_mut(&conn_id) {
            user.ts_points += ts_reward;
        }
        // VB6: ||900@<pts>
        state.send_msg_id(conn_id, 900, &ts_reward.to_string()).await;
    }

    // Credits reward (no multiplier in VB6)
    if quest.creditos > 0 {
        let credits_reward = quest.creditos as i64;
        if let Some(user) = state.users.get_mut(&conn_id) {
            user.puntos_donacion += credits_reward;
        }
        // VB6: ||930@<credits>
        state.send_msg_id(conn_id, 930, &credits_reward.to_string()).await;
    }

    // Reputation reward (same multiplier as ptsTorneo)
    if quest.pts_torneo > 0 {
        let rep_reward = quest.pts_torneo as i32 * multiplier as i32;
        if let Some(user) = state.users.get_mut(&conn_id) {
            user.reputation += rep_reward;
        }
    }

    // Reset quest state
    if let Some(user) = state.users.get_mut(&conn_id) {
        user.questeando = false;
        user.quest_num = 0;
        user.quest_kills = 0;
        user.quests_completed += 1;
    }

    // VB6: SendUserGLD
    send_stats_gold(state, conn_id).await;
}

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
/// Called when a party member kills something — splits exp equally.
pub async fn party_share_exp(state: &mut GameState, killer_conn: ConnectionId, total_exp: i64) {
    let (party_index, map, x, y) = match state.users.get(&killer_conn) {
        Some(u) if u.logged && u.party_index > 0 => (u.party_index, u.pos_map, u.pos_x, u.pos_y),
        _ => return, // Not in a party, no sharing
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
