//! Remaining player slash commands: /desc, /comerciar, /boveda, /daroro,
//! /depositar, /retirar, /fmsg, /hora, /curar, /pmsg, etc.
//! Extracted from mod.rs to reduce file size.

use super::common::*;
use super::{enviar_banco_inv, iniciar_comercio_usuario};
use crate::db::charfile;
use crate::game::types::{GameState, SendTarget, privilege_level};
use crate::net::ConnectionId;
use crate::protocol::{binary_packets, fields::read_field, font_index};
use tracing::info;

// GM / Admin command handlers (TCP_HandleData3.bas — GM section)
// =====================================================================
// =====================================================================
// Remaining player slash commands
// =====================================================================

/// /DESC <text> — Set character description. Saved to charfile.
pub(super) async fn handle_slash_desc(state: &mut GameState, conn_id: ConnectionId, desc: &str) {
    let _name = match state.users.get(&conn_id) {
        Some(u) if u.logged => u.char_name.clone(),
        _ => return,
    };

    if desc.len() > 128 {
        state.send_console(
            conn_id,
            "Descripcion muy larga (max 128 caracteres).",
            font_index::INFO,
        );
        return;
    }

    if let Some(user) = state.users.get_mut(&conn_id) {
        user.desc = desc.to_string();
    }

    state.send_console(conn_id, "Descripcion actualizada.", font_index::INFO);
}

/// /VERASPEC — View target user's character description.
/// VB6: uses TargetUser to look up description.
pub(super) async fn handle_slash_veraspec(state: &mut GameState, conn_id: ConnectionId) {
    let target_conn = match state.users.get(&conn_id) {
        Some(u) if u.logged => u.target_user,
        _ => return,
    };

    if target_conn == 0 {
        state.send_console(conn_id, "Primero selecciona un jugador.", font_index::INFO);
        return;
    }

    let (target_name, target_desc) = match state.users.get(&target_conn) {
        Some(u) if u.logged => (u.char_name.clone(), u.desc.clone()),
        _ => {
            state.send_console(conn_id, "Jugador no encontrado.", font_index::INFO);
            return;
        }
    };

    if target_desc.is_empty() {
        state.send_console(
            conn_id,
            &format!("{} no tiene descripcion.", target_name),
            font_index::INFO,
        );
    } else {
        state.send_console(
            conn_id,
            &format!("[{}] {}", target_name, target_desc),
            font_index::INFO,
        );
    }
}

/// /COMERCIAR — Trade with target player (VB6: comManda). Requires mutual confirmation.
pub(super) async fn handle_slash_comerciar(state: &mut GameState, conn_id: ConnectionId) {
    let (dead, trading, target_user, char_name) = match state.users.get(&conn_id) {
        Some(u) if u.logged => (u.dead, u.trading, u.target_user, u.char_name.clone()),
        _ => return,
    };

    if dead {
        state.send_console(conn_id, "Estas muerto.", font_index::INFO);
        return;
    }
    if trading {
        state.send_console(conn_id, "Ya estas comerciando.", font_index::INFO);
        return;
    }
    if target_user == 0 {
        state.send_console(conn_id, "Primero selecciona un jugador.", font_index::INFO);
        return;
    }
    if target_user == conn_id {
        state.send_console(
            conn_id,
            "No puedes comerciar contigo mismo.",
            font_index::INFO,
        );
        return;
    }

    // Check target is valid
    let target_ok = state
        .users
        .get(&target_user)
        .map(|u| u.logged && !u.dead && !u.trading)
        .unwrap_or(false);
    if !target_ok {
        state.send_console(conn_id, "El jugador no esta disponible.", font_index::INFO);
        return;
    }

    // VB6 13.3 parity: both players must be on the same map and within 3 tiles (Chebyshev)
    let (user_map, user_x, user_y) = match state.users.get(&conn_id) {
        Some(u) => (u.pos_map, u.pos_x, u.pos_y),
        None => return,
    };
    let (target_map, target_x, target_y) = match state.users.get(&target_user) {
        Some(u) => (u.pos_map, u.pos_x, u.pos_y),
        None => return,
    };
    if user_map != target_map {
        state.send_console(
            conn_id,
            "El jugador no esta en el mismo mapa.",
            font_index::INFO,
        );
        return;
    }
    let dist = (user_x - target_x).abs().max((user_y - target_y).abs());
    if dist > 3 {
        state.send_console(
            conn_id,
            "Estás demasiado lejos para comerciar.",
            font_index::INFO,
        );
        return;
    }

    // VB6 mutual confirmation: check if target already requested trade with us
    let target_wants_us = state
        .users
        .get(&target_user)
        .map(|u| u.trade_partner == Some(conn_id) && !u.trading)
        .unwrap_or(false);

    if target_wants_us {
        // Both players want to trade with each other — initiate trade
        iniciar_comercio_usuario(state, conn_id, target_user).await;
    } else {
        // Mark our trade intention and notify target
        if let Some(u) = state.users.get_mut(&conn_id) {
            u.trade_partner = Some(target_user);
        }
        let target_name = state
            .users
            .get(&target_user)
            .map(|u| u.char_name.clone())
            .unwrap_or_default();
        state.send_console(
            target_user,
            &format!(
                "{} quiere comerciar contigo. Usa /COMERCIAR para aceptar.",
                char_name
            ),
            font_index::INFO,
        );
        state.send_console(
            conn_id,
            &format!("Le has propuesto comerciar a {}.", target_name),
            font_index::INFO,
        );
    }
}

/// /BOVEDA — Open bank vault. VB6: requires Banquero NPC target, distance <= 5, not dead.
pub(super) async fn handle_slash_boveda(state: &mut GameState, conn_id: ConnectionId) {
    let (dead, target_npc) = match state.users.get(&conn_id) {
        Some(u) if u.logged => (u.dead, u.target_npc),
        _ => return,
    };

    // VB6: If Muerto = 1 Then ||3
    if dead {
        state.send_msg_id(conn_id, 3, "");
        return;
    }

    // VB6: If TargetNPC = 0 Then ||9
    if target_npc == 0 {
        state.send_msg_id(conn_id, 9, "");
        return;
    }

    // VB6: distance > 5 → ||13
    let (npc_map, npc_x, npc_y, npc_type) = match state.get_npc(target_npc) {
        Some(npc) => (npc.map, npc.x, npc.y, npc.npc_type),
        None => {
            state.send_msg_id(conn_id, 9, "");
            return;
        }
    };
    let (u_map, u_x, u_y) = match state.users.get(&conn_id) {
        Some(u) => (u.pos_map, u.pos_x, u.pos_y),
        None => return,
    };
    if u_map != npc_map || (u_x - npc_x).abs() > 5 || (u_y - npc_y).abs() > 5 {
        state.send_msg_id(conn_id, 13, "");
        return;
    }

    // VB6: NPCtype must be Banquero (4)
    if npc_type != crate::data::npcs::NpcType::Banker {
        return;
    }

    // VB6: IniciarDeposito — send bank inventory then INITBANCO
    enviar_banco_inv(state, conn_id).await;
    send_stats_gold(state, conn_id).await;

    if let Some(user) = state.users.get_mut(&conn_id) {
        user.comerciando = true;
    }

    let bank_gold = state.users.get(&conn_id).map(|u| u.bank_gold).unwrap_or(0);
    let pkt = binary_packets::write_bank_init(bank_gold as i32);
    state.send_bytes(conn_id, &pkt);
}

/// /DARORO <name>@<amount> — Give gold to another player. Min 10000.
pub(super) async fn handle_slash_daroro(state: &mut GameState, conn_id: ConnectionId, args: &str) {
    let target_name = read_field(1, args, '@');
    let amount_str = read_field(2, args, '@');
    let amount: i64 = amount_str.parse().unwrap_or(0);

    if amount < 10000 {
        state.send_console(conn_id, "Minimo 10.000 de oro.", font_index::INFO);
        return;
    }

    let (my_gold, my_name) = match state.users.get(&conn_id) {
        Some(u) if u.logged => (u.gold, u.char_name.clone()),
        _ => return,
    };

    if my_gold < amount {
        state.send_console(conn_id, "No tenes suficiente oro.", font_index::INFO);
        return;
    }

    let target_upper = target_name.to_uppercase();
    let target_conn = match state.online_names.get(&target_upper).copied() {
        Some(c) => c,
        None => {
            state.send_console(conn_id, "Jugador no encontrado.", font_index::INFO);
            return;
        }
    };

    // Transfer
    if let Some(user) = state.users.get_mut(&conn_id) {
        user.gold -= amount;
    }
    if let Some(target) = state.users.get_mut(&target_conn) {
        target.gold = (target.gold + amount).min(MAX_GOLD);
    }

    send_stats_gold(state, conn_id).await;
    send_stats_gold(state, target_conn).await;

    let target_real = state
        .users
        .get(&target_conn)
        .map(|u| u.char_name.clone())
        .unwrap_or_default();
    state.send_console(
        conn_id,
        &format!("Le diste {} de oro a {}.", amount, target_real),
        font_index::INFO,
    );
    state.send_console(
        target_conn,
        &format!("{} te dio {} de oro.", my_name, amount),
        font_index::INFO,
    );

    info!("[GOLD] {} gave {} gold to {}", my_name, amount, target_real);
}

/// /DEPOSITAR <amount> — Deposit gold at bank (slash command shortcut).
pub(super) async fn handle_slash_depositar(
    state: &mut GameState,
    conn_id: ConnectionId,
    amount: i64,
) {
    if amount <= 0 {
        return;
    }

    let gold = state.users.get(&conn_id).map(|u| u.gold).unwrap_or(0);
    if gold < amount {
        state.send_console(conn_id, "No tenes suficiente oro.", font_index::INFO);
        return;
    }

    if let Some(user) = state.users.get_mut(&conn_id) {
        user.gold -= amount;
        user.bank_gold += amount;
    }
    send_stats_gold(state, conn_id).await;

    let bank_gold = state.users.get(&conn_id).map(|u| u.bank_gold).unwrap_or(0);
    let bg_pkt = binary_packets::write_update_bank_gold(bank_gold as i32);
    state.send_bytes(conn_id, &bg_pkt);
    state.send_console(
        conn_id,
        &format!("Depositaste {} de oro.", amount),
        font_index::INFO,
    );
}

/// /RETIRAR <amount> — Withdraw gold from bank (slash command shortcut).
pub(super) async fn handle_slash_retirar_oro(
    state: &mut GameState,
    conn_id: ConnectionId,
    amount: i64,
) {
    if amount <= 0 {
        return;
    }

    let bank_gold = state.users.get(&conn_id).map(|u| u.bank_gold).unwrap_or(0);
    if bank_gold < amount {
        state.send_console(
            conn_id,
            "No tenes suficiente oro en la boveda.",
            font_index::INFO,
        );
        return;
    }

    if let Some(user) = state.users.get_mut(&conn_id) {
        user.gold += amount;
        user.bank_gold -= amount;
    }
    send_stats_gold(state, conn_id).await;

    let bank_gold = state.users.get(&conn_id).map(|u| u.bank_gold).unwrap_or(0);
    let bg_pkt = binary_packets::write_update_bank_gold(bank_gold as i32);
    state.send_bytes(conn_id, &bg_pkt);
    state.send_console(
        conn_id,
        &format!("Retiraste {} de oro.", amount),
        font_index::INFO,
    );
}

/// /FMSG <msg> — Faction message (to all same-faction members).
pub(super) async fn handle_slash_fmsg(state: &mut GameState, conn_id: ConnectionId, text: &str) {
    let (name, armada, caos) = match state.users.get(&conn_id) {
        Some(u) if u.logged => (u.char_name.clone(), u.armada_real, u.fuerzas_caos),
        _ => return,
    };

    if !armada && !caos {
        state.send_console(
            conn_id,
            "No perteneces a ninguna faccion.",
            font_index::INFO,
        );
        return;
    }

    let msg = format!("[Faccion] {}> {}", name, text);

    // Send to all users in the same faction
    let targets: Vec<ConnectionId> = state
        .users
        .values()
        .filter(|u| u.logged && ((armada && u.armada_real) || (caos && u.fuerzas_caos)))
        .map(|u| u.conn_id)
        .collect();
    for t in targets {
        state.send_console(t, &msg, font_index::GUILD);
    }
}

/// /HORA — Show server time. GMs broadcast to all.
pub(super) async fn handle_slash_hora(state: &mut GameState, conn_id: ConnectionId) {
    let privileges = state.users.get(&conn_id).map(|u| u.privileges).unwrap_or(0);

    let now = std::time::SystemTime::now()
        .duration_since(std::time::UNIX_EPOCH)
        .unwrap_or_default()
        .as_secs();
    let hours = (now / 3600) % 24;
    let minutes = (now / 60) % 60;
    let seconds = now % 60;
    let time_str = format!("{:02}:{:02}:{:02}", hours, minutes, seconds);

    if privileges > privilege_level::USER {
        // GMs broadcast to all
        state.send_console_to(
            SendTarget::ToAll,
            &format!("Hora del servidor: {}", time_str),
            font_index::INFO,
        );
    } else {
        state.send_console(
            conn_id,
            &format!("Hora del servidor: {}", time_str),
            font_index::INFO,
        );
    }
}

/// /NICK <name> — Check if a character exists online/offline (player command, NOT GM /CHANGENICK).
pub(super) async fn handle_slash_nick_check(
    state: &mut GameState,
    conn_id: ConnectionId,
    name: &str,
) {
    let target_upper = name.to_uppercase();

    if let Some(&_t_conn) = state.online_names.get(&target_upper) {
        state.send_console(conn_id, &format!("{} esta ONLINE.", name), font_index::INFO);
    } else if charfile::character_exists(&state.pool, name).await {
        state.send_console(
            conn_id,
            &format!("{} existe pero esta OFFLINE.", name),
            font_index::INFO,
        );
    } else {
        state.send_console(conn_id, &format!("{} no existe.", name), font_index::INFO);
    }
}

/// /ADVERTENCIAS — View own warnings/penalties.
pub(super) async fn handle_slash_advertencias(state: &mut GameState, conn_id: ConnectionId) {
    let name = match state.users.get(&conn_id) {
        Some(u) if u.logged => u.char_name.clone(),
        _ => return,
    };

    let pool = state.pool.clone();
    let penalties = charfile::load_penalties(&pool, &name).await;
    if penalties.is_empty() {
        state.send_console(conn_id, "No tenes advertencias.", font_index::INFO);
        return;
    }

    state.send_console(
        conn_id,
        &format!("Tenes {} advertencias:", penalties.len()),
        font_index::INFO,
    );
    for (i, p) in penalties.iter().enumerate() {
        state.send_console(conn_id, &format!("{}: {}", i + 1, p), font_index::INFO);
    }
}

/// /CURAR — Heal at Revividor NPC. VB6: requires Revividor NPC, distance <= 10, alive.
/// Removes poison, heals to full HP.
pub(super) async fn handle_slash_curar(state: &mut GameState, conn_id: ConnectionId) {
    let (dead, target_npc) = match state.users.get(&conn_id) {
        Some(u) if u.logged => (u.dead, u.target_npc),
        _ => return,
    };

    // VB6: If TargetNPC = 0 Then ||9
    if target_npc == 0 {
        state.send_msg_id(conn_id, 9, "");
        return;
    }

    // VB6: NPCtype must be Revividor (1) AND user must be alive
    let npc_type = state.get_npc(target_npc).map(|n| n.npc_type);
    if npc_type != Some(crate::data::npcs::NpcType::Reviver) || dead {
        return;
    }

    // VB6: distance > 10 → ||12
    let (npc_map, npc_x, npc_y) = match state.get_npc(target_npc) {
        Some(npc) => (npc.map, npc.x, npc.y),
        None => return,
    };
    let (u_map, u_x, u_y) = match state.users.get(&conn_id) {
        Some(u) => (u.pos_map, u.pos_x, u.pos_y),
        None => return,
    };
    if u_map != npc_map || (u_x - npc_x).abs() > 10 || (u_y - npc_y).abs() > 10 {
        state.send_msg_id(conn_id, 12, "");
        return;
    }

    // VB6: Remove poison, heal to full HP, send ||398
    if let Some(user) = state.users.get_mut(&conn_id) {
        user.poisoned = false;
        user.poisoned_by = None;
        user.poisoned_skill_id = 0;
        let max = user.max_hp;
        user.min_hp = max;
    }
    send_stats_hp(state, conn_id).await;
    state.send_msg_id(conn_id, 398, "");
}

/// /PMSG <msg> — Party message to all party members.
pub(super) async fn handle_slash_pmsg(state: &mut GameState, conn_id: ConnectionId, text: &str) {
    let (party_idx, name) = match state.users.get(&conn_id) {
        Some(u) if u.logged && u.party_index > 0 => (u.party_index, u.char_name.clone()),
        _ => {
            return;
        }
    };

    let msg = format!("[Party] {}> {}", name, text);
    let members: Vec<ConnectionId> = state
        .parties
        .get(party_idx as usize)
        .and_then(|p| p.as_ref())
        .map(|p| p.members.clone())
        .unwrap_or_default();
    for &member_conn in &members {
        state.send_guild_chat_to(SendTarget::ToIndex(member_conn), &msg);
    }
}

// =====================================================================
// Missing VB6 player commands — Parity audit
// =====================================================================

/// /ONLINEGM — List online GMs. VB6 TCP.bas:3794
/// Hides Dios+ from non-Dios users. Sends N| packet with green color.
pub(super) async fn handle_slash_onlinegm(state: &mut GameState, conn_id: ConnectionId) {
    let my_priv = match state.users.get(&conn_id) {
        Some(u) if u.logged => u.privileges,
        _ => return,
    };

    let mut names = Vec::new();
    for u in state.users.values() {
        if !u.logged || u.privileges <= privilege_level::USER {
            continue;
        }
        // VB6: Hide Dios+ from non-Dios users
        if u.privileges >= privilege_level::DIOS && my_priv < privilege_level::DIOS {
            continue;
        }
        names.push(u.char_name.clone());
    }

    if names.is_empty() {
        state.send_console(conn_id, "No hay GMs online.", font_index::INFO);
    } else {
        // VB6 sends via N| packet (green text, one name per line)
        state.send_console(
            conn_id,
            &format!("GMs online: {}", names.join(", ")),
            font_index::SERVER,
        );
    }
}

/// /ONLINEMAP — List online players on same map. VB6 TCP.bas:3811
/// Hides Dios+ from non-Dios users. Sends ||750@names.
pub(super) async fn handle_slash_onlinemap(state: &mut GameState, conn_id: ConnectionId) {
    let (my_priv, my_map) = match state.users.get(&conn_id) {
        Some(u) if u.logged => (u.privileges, u.pos_map),
        _ => return,
    };

    let mut names = Vec::new();
    for u in state.users.values() {
        if !u.logged || u.pos_map != my_map {
            continue;
        }
        // VB6: Hide Dios+ from non-Dios users
        if u.privileges >= privilege_level::DIOS && my_priv < privilege_level::DIOS {
            continue;
        }
        names.push(u.char_name.clone());
    }

    let list = names.join(", ");
    state.send_msg_id(conn_id, 750, &list);
}

// =====================================================================
// Duel system (VB6: AtacablePor / /DESAFIO)
// =====================================================================

/// /DESAFIO <name> — Challenge another player to a duel.
/// VB6: Sets AtacablePor on both users so they can attack each other
/// outside of normal PvP rules (e.g. in safe zones).
pub(super) async fn handle_slash_desafio(
    state: &mut GameState,
    conn_id: ConnectionId,
    target_name: &str,
) {
    let (my_name, dead, my_map, _mx, _my_) = match state.users.get(&conn_id) {
        Some(u) if u.logged => (u.char_name.clone(), u.dead, u.pos_map, u.pos_x, u.pos_y),
        _ => return,
    };

    if dead {
        state.send_console(conn_id, "Estas muerto.", font_index::INFO);
        return;
    }

    let target_conn = match state.online_names.get(&target_name.to_uppercase()).copied() {
        Some(c) => c,
        None => {
            state.send_console(conn_id, "El jugador no esta conectado.", font_index::INFO);
            return;
        }
    };

    if target_conn == conn_id {
        state.send_console(
            conn_id,
            "No puedes desafiarte a ti mismo.",
            font_index::INFO,
        );
        return;
    }

    let target_data = match state.users.get(&target_conn) {
        Some(u) if u.logged && !u.dead => {
            (u.pos_map, u.pos_x, u.pos_y, u.atacable_por, u.duel_pending)
        }
        _ => {
            state.send_console(conn_id, "El jugador no esta disponible.", font_index::INFO);
            return;
        }
    };
    let (t_map, _t_x, _t_y, t_atacable, t_pending) = target_data;

    // Must be on same map
    if t_map != my_map {
        state.send_console(
            conn_id,
            "El jugador no esta en el mismo mapa.",
            font_index::INFO,
        );
        return;
    }

    // Check if already dueling
    let my_atacable = state
        .users
        .get(&conn_id)
        .map(|u| u.atacable_por)
        .unwrap_or(0);
    if my_atacable > 0 {
        state.send_console(conn_id, "Ya estas en un duelo.", font_index::INFO);
        return;
    }
    if t_atacable > 0 {
        state.send_console(conn_id, "El jugador ya esta en un duelo.", font_index::INFO);
        return;
    }

    // Check if target already challenged us — if so, accept the duel
    if t_pending == conn_id {
        // Accept: enable mutual PvP
        if let Some(u) = state.users.get_mut(&conn_id) {
            u.atacable_por = target_conn;
            u.duel_pending = 0;
        }
        if let Some(u) = state.users.get_mut(&target_conn) {
            u.atacable_por = conn_id;
            u.duel_pending = 0;
        }

        let target_real = state
            .users
            .get(&target_conn)
            .map(|u| u.char_name.clone())
            .unwrap_or_default();
        state.send_console(
            conn_id,
            &format!("Has aceptado el duelo con {}!", target_real),
            font_index::FIGHT,
        );
        state.send_console(
            target_conn,
            &format!("{} ha aceptado tu desafio!", my_name),
            font_index::FIGHT,
        );
        return;
    }

    // Send challenge
    if let Some(u) = state.users.get_mut(&conn_id) {
        u.duel_pending = target_conn;
    }

    let target_real = state
        .users
        .get(&target_conn)
        .map(|u| u.char_name.clone())
        .unwrap_or_default();
    state.send_console(
        target_conn,
        &format!(
            "{} te ha desafiado a un duelo. Usa /DESAFIO {} para aceptar.",
            my_name, my_name
        ),
        font_index::FIGHT,
    );
    state.send_console(
        conn_id,
        &format!("Has desafiado a {} a un duelo.", target_real),
        font_index::INFO,
    );
}

/// /FINDESAFIO — End current duel.
pub(super) async fn handle_slash_findesafio(state: &mut GameState, conn_id: ConnectionId) {
    let (atacable_por, my_name) = match state.users.get(&conn_id) {
        Some(u) if u.logged => (u.atacable_por, u.char_name.clone()),
        _ => return,
    };

    if atacable_por == 0 {
        // Also clear pending challenge
        if let Some(u) = state.users.get_mut(&conn_id) {
            u.duel_pending = 0;
        }
        state.send_console(conn_id, "No estas en un duelo.", font_index::INFO);
        return;
    }

    let partner = atacable_por;

    // Clear both sides
    if let Some(u) = state.users.get_mut(&conn_id) {
        u.atacable_por = 0;
        u.duel_pending = 0;
    }
    if let Some(u) = state.users.get_mut(&partner) {
        u.atacable_por = 0;
        u.duel_pending = 0;
    }

    state.send_console(conn_id, "Has terminado el duelo.", font_index::INFO);
    state.send_console(
        partner,
        &format!("{} ha terminado el duelo.", my_name),
        font_index::INFO,
    );
}

// =====================================================================
// Timbero (Gambling NPC) — VB6: HandleBet
// =====================================================================

/// /APOSTAR <amount> — Bet gold at the Timbero NPC.
/// VB6: 47% win chance, min 1 max 5000 gold.
pub(super) async fn handle_slash_apostar(
    state: &mut GameState,
    conn_id: ConnectionId,
    amount: i64,
) {
    let (dead, target_npc, gold, map, x, y) = match state.users.get(&conn_id) {
        Some(u) if u.logged => (u.dead, u.target_npc, u.gold, u.pos_map, u.pos_x, u.pos_y),
        _ => return,
    };

    if dead {
        state.send_console(conn_id, "Estas muerto.", font_index::INFO);
        return;
    }

    if amount < 1 || amount > 5000 {
        state.send_console(
            conn_id,
            "La apuesta debe ser entre 1 y 5000 monedas de oro.",
            font_index::INFO,
        );
        return;
    }

    if gold < amount {
        state.send_console(conn_id, "No tenes suficiente oro.", font_index::INFO);
        return;
    }

    // Must target a Timbero NPC (type 7)
    if target_npc == 0 {
        state.send_console(conn_id, "Primero selecciona un Timbero.", font_index::INFO);
        return;
    }

    let npc_type = state.get_npc(target_npc).map(|n| n.npc_type);
    if npc_type != Some(crate::data::npcs::NpcType::Gambler) {
        state.send_console(
            conn_id,
            "Debes seleccionar un Timbero para apostar.",
            font_index::INFO,
        );
        return;
    }

    // Distance check (max 10 tiles)
    let (npc_map, npc_x, npc_y) = match state.get_npc(target_npc) {
        Some(npc) => (npc.map, npc.x, npc.y),
        None => return,
    };
    if map != npc_map || (x - npc_x).abs() > 10 || (y - npc_y).abs() > 10 {
        state.send_console(conn_id, "Estas muy lejos del Timbero.", font_index::INFO);
        return;
    }

    // 47% win chance (VB6 exact)
    let roll: i32 = rand_range(1, 100);
    let win = roll <= 47;

    state.timbero_jugadas += 1;

    if win {
        if let Some(u) = state.users.get_mut(&conn_id) {
            u.gold += amount;
        }
        state.timbero_perdidas += amount;
        state.send_console(
            conn_id,
            &format!("Ganaste {} monedas de oro!", amount),
            font_index::FIGHT,
        );
    } else {
        if let Some(u) = state.users.get_mut(&conn_id) {
            u.gold -= amount;
        }
        state.timbero_ganancias += amount;
        state.send_console(
            conn_id,
            &format!("Perdiste {} monedas de oro.", amount),
            font_index::INFO,
        );
    }

    send_stats_gold(state, conn_id).await;
}

// =====================================================================
// Governor NPC — Set home city (VB6: Gobernador type 11)
// =====================================================================

/// /HOGAR — Set home city via Governor NPC (alive), or start traveling home (dead).
/// VB6: Alive + Gobernador NPC → set home city. Dead + has home → start GoHome timer (10s).
pub(super) async fn handle_slash_hogar(state: &mut GameState, conn_id: ConnectionId) {
    let (dead, target_npc, hogar, traveling) = match state.users.get(&conn_id) {
        Some(u) if u.logged => (u.dead, u.target_npc, u.hogar.clone(), u.traveling),
        _ => return,
    };

    // Dead user: start traveling home (VB6 GoHome mechanic)
    if dead {
        if hogar.is_empty() {
            state.send_console(conn_id, "No tienes un hogar establecido.", font_index::INFO);
            return;
        }
        if traveling {
            state.send_console(conn_id, "Ya estas viajando a tu hogar.", font_index::INFO);
            return;
        }
        if let Some(u) = state.users.get_mut(&conn_id) {
            u.traveling = true;
            u.counter_go_home = 0;
        }
        state.send_console(conn_id, "Viajando a tu hogar, espera...", font_index::INFO);
        return;
    }

    if target_npc == 0 {
        state.send_console(
            conn_id,
            "Primero selecciona un Gobernador.",
            font_index::INFO,
        );
        return;
    }

    // VB6: NpcType = 11 (Gobernador) — our enum uses Quest=11 for this
    let npc_type_num = state
        .get_npc(target_npc)
        .map(|n| n.npc_type as i32)
        .unwrap_or(0);
    if npc_type_num != 11 {
        state.send_console(
            conn_id,
            "Debes seleccionar un Gobernador.",
            font_index::INFO,
        );
        return;
    }

    // Distance check
    let (npc_map, npc_x, npc_y) = match state.get_npc(target_npc) {
        Some(npc) => (npc.map, npc.x, npc.y),
        None => return,
    };
    let (u_map, u_x, u_y) = match state.users.get(&conn_id) {
        Some(u) => (u.pos_map, u.pos_x, u.pos_y),
        None => return,
    };
    if u_map != npc_map || (u_x - npc_x).abs() > 5 || (u_y - npc_y).abs() > 5 {
        state.send_console(conn_id, "Estas muy lejos del Gobernador.", font_index::INFO);
        return;
    }

    // Get city from NPC's spawn location (map name as city)
    let city_name = state
        .get_npc(target_npc)
        .and_then(|n| state.game_data.maps.get(n.map as usize))
        .and_then(|m| m.as_ref())
        .map(|m| m.info.name.clone())
        .unwrap_or_else(|| "Desconocida".to_string());

    if let Some(u) = state.users.get_mut(&conn_id) {
        u.hogar = city_name.clone();
    }

    state.send_console(
        conn_id,
        &format!("Tu hogar ha sido establecido en {}.", city_name),
        font_index::INFO,
    );
}

// =====================================================================
// Password change — /PASSWD
// =====================================================================

/// /PASSWD <old>@<new> — Change account password.
pub(super) async fn handle_slash_passwd(state: &mut GameState, conn_id: ConnectionId, args: &str) {
    let old_pass = read_field(1, args, '@');
    let new_pass = read_field(2, args, '@');

    if old_pass.is_empty() || new_pass.is_empty() {
        state.send_console(conn_id, "Uso: /PASSWD <vieja>@<nueva>", font_index::INFO);
        return;
    }

    if new_pass.len() < 3 {
        state.send_console(
            conn_id,
            "La nueva password debe tener al menos 3 caracteres.",
            font_index::INFO,
        );
        return;
    }

    let account_name = match state.users.get(&conn_id) {
        Some(u) if u.logged => u.account_name.clone(),
        _ => return,
    };

    // Verify old password
    let account = match crate::db::accounts::load_account(&state.pool, &account_name).await {
        Ok(acc) => acc,
        Err(_) => {
            state.send_console(conn_id, "Error al verificar la cuenta.", font_index::INFO);
            return;
        }
    };

    if !crate::db::password::verify_password(&old_pass, &account.password_hash) {
        state.send_console(conn_id, "Password actual incorrecta.", font_index::INFO);
        return;
    }

    // Hash new password
    let new_hash = match crate::db::password::hash_password(&new_pass) {
        Ok(h) => h,
        Err(_) => {
            state.send_console(conn_id, "Error al cambiar la password.", font_index::INFO);
            return;
        }
    };

    // Update in DB
    if crate::db::accounts::update_password(&state.pool, &account_name, &new_hash)
        .await
        .is_ok()
    {
        state.send_console(conn_id, "Password cambiada exitosamente.", font_index::INFO);
    } else {
        state.send_console(
            conn_id,
            "Error al guardar la nueva password.",
            font_index::INFO,
        );
    }
}
