//! Event system handlers — Duels, Tournaments, CvC (Clan vs Clan),
//! Eventos (GM-managed events), Siege, Nobleza, Guerra, Pretoriano.

use tracing::info;
use crate::net::ConnectionId;
use crate::game::types::{GameState, UserState, SendTarget, privilege_level};
use crate::game::npc;
use crate::protocol::{server_opcodes, font_types, fields::read_field};
use crate::db::guilds;
use crate::data::objects::ObjData;
use super::common::*;
use super::world;

// Functions from parent module (mod.rs)
use super::{warp_user, revive_user, move_npc};

// =====================================================================
// Duel, Tournament, and Event handlers
// =====================================================================

/// Arena duel map and positions (VB6: map 71, 4 arenas)
const DUEL_MAP: i32 = 71;
const DUEL_MIN_BET: i64 = 200_000;
const DUEL_POSITIONS: [(i32, i32, i32, i32); 4] = [
    (23, 28, 44, 42), // Arena 1: (p1x,p1y, p2x,p2y)
    (23, 61, 44, 76), // Arena 2
    (59, 28, 80, 42), // Arena 3
    (59, 61, 80, 76), // Arena 4
];

/// Desafio map (1v1 king-of-the-hill)
const DESAFIO_MAP: i32 = 109;
const DESAFIO_COST_DEFENDER: i64 = 200_000;
const DESAFIO_COST_CHALLENGER: i64 = 30_000;
const DESAFIO_MIN_LEVEL: i32 = 50;
const DESAFIO_REWARD: i64 = 100_000;

/// CvC map
pub(super) const CVC_MAP: i32 = 108;
pub(super) const CVC_COST: i64 = 200_000;

/// Exit map (common return point)
const EXIT_MAP: i32 = 28;
const EXIT_X: i32 = 54;
const EXIT_Y: i32 = 36;

/// /DUELO <name>@<gold> — Challenge another player to an arena duel.
pub(super) async fn handle_slash_duelo(state: &mut GameState, conn_id: ConnectionId, args: &str) {
    let target_name = read_field(1, args, '@');
    let bet_str = read_field(2, args, '@');
    let bet: i64 = bet_str.parse().unwrap_or(0);

    let (name, level, class, gold, dead, map) = match state.users.get(&conn_id) {
        Some(u) if u.logged => (u.char_name.clone(), u.level, u.class.clone(), u.gold, u.dead, u.pos_map),
        _ => return,
    };

    if dead {
        state.send_to(conn_id, &format!("{}No puedes hacer eso estando muerto.{}", server_opcodes::CONSOLE_MSG, font_types::INFO)).await;
        return;
    }
    if bet < DUEL_MIN_BET {
        state.send_to(conn_id, &format!("{}La apuesta minima es {}.{}", server_opcodes::CONSOLE_MSG, DUEL_MIN_BET, font_types::INFO)).await;
        return;
    }
    if gold < bet {
        state.send_to(conn_id, &format!("{}No tenes suficiente oro.{}", server_opcodes::CONSOLE_MSG, font_types::INFO)).await;
        return;
    }

    // Check if any arena is free
    let free_arena = (1..=4).find(|&i| !state.arena_ocupada[i]);
    if free_arena.is_none() {
        state.send_to(conn_id, &format!("{}Todas las arenas estan ocupadas.{}", server_opcodes::CONSOLE_MSG, font_types::INFO)).await;
        return;
    }

    // Find target
    let target_upper = target_name.to_uppercase();
    let target_conn = match state.online_names.get(&target_upper).copied() {
        Some(c) => c,
        None => {
            state.send_to(conn_id, &format!("{}Jugador no encontrado.{}", server_opcodes::CONSOLE_MSG, font_types::INFO)).await;
            return;
        }
    };

    // Check target has enough gold
    let target_gold = state.users.get(&target_conn).map(|u| u.gold).unwrap_or(0);
    if target_gold < bet {
        state.send_to(conn_id, &format!("{}El otro jugador no tiene suficiente oro.{}", server_opcodes::CONSOLE_MSG, font_types::INFO)).await;
        return;
    }

    // Set challenge on target
    if let Some(target_user) = state.users.get_mut(&target_conn) {
        target_user.le_mandaron_duelo = true;
        target_user.ultimo_en_mandar_duelo = name.clone();
    }
    if let Some(user) = state.users.get_mut(&conn_id) {
        user.apuesta_oro = bet;
    }

    // Notify target
    let pkt = format!("||546@{}@{}@{}@{}", name, class, level, bet);
    state.send_to(target_conn, &pkt).await;

    let msg = format!("{}Has desafiado a {} por {} de oro.{}", server_opcodes::CONSOLE_MSG, target_name, bet, font_types::INFO);
    state.send_to(conn_id, &msg).await;
}

/// /SIDUELO — Accept a pending duel challenge.
pub(super) async fn handle_slash_siduelo(state: &mut GameState, conn_id: ConnectionId) {
    let (has_challenge, challenger_name) = match state.users.get(&conn_id) {
        Some(u) if u.logged && u.le_mandaron_duelo => {
            (true, u.ultimo_en_mandar_duelo.clone())
        }
        _ => (false, String::new()),
    };

    if !has_challenge {
        state.send_to(conn_id, &format!("{}No tenes ninguna oferta de duelo.{}", server_opcodes::CONSOLE_MSG, font_types::INFO)).await;
        return;
    }

    let challenger_upper = challenger_name.to_uppercase();
    let challenger_conn = match state.online_names.get(&challenger_upper).copied() {
        Some(c) => c,
        None => {
            state.send_to(conn_id, &format!("{}El retador ya no esta online.{}", server_opcodes::CONSOLE_MSG, font_types::INFO)).await;
            return;
        }
    };

    // Get bet amount from challenger
    let bet = state.users.get(&challenger_conn).map(|u| u.apuesta_oro).unwrap_or(0);
    if bet <= 0 { return; }

    // Check both have gold
    let my_gold = state.users.get(&conn_id).map(|u| u.gold).unwrap_or(0);
    let their_gold = state.users.get(&challenger_conn).map(|u| u.gold).unwrap_or(0);
    if my_gold < bet || their_gold < bet {
        state.send_to(conn_id, &format!("{}No hay suficiente oro.{}", server_opcodes::CONSOLE_MSG, font_types::INFO)).await;
        return;
    }

    // Find free arena
    let arena = match (1..=4).find(|&i| !state.arena_ocupada[i]) {
        Some(a) => a,
        None => {
            state.send_to(conn_id, &format!("{}No hay arenas disponibles.{}", server_opcodes::CONSOLE_MSG, font_types::INFO)).await;
            return;
        }
    };

    // Deduct gold
    if let Some(user) = state.users.get_mut(&conn_id) {
        user.gold -= bet;
        user.le_mandaron_duelo = false;
        user.ultimo_en_mandar_duelo.clear();
    }
    if let Some(user) = state.users.get_mut(&challenger_conn) {
        user.gold -= bet;
        user.apuesta_oro = 0;
    }
    send_stats_gold(state, conn_id).await;
    send_stats_gold(state, challenger_conn).await;

    // Save positions and set duel flags
    let my_name = state.users.get(&conn_id).map(|u| u.char_name.clone()).unwrap_or_default();
    let their_name = state.users.get(&challenger_conn).map(|u| u.char_name.clone()).unwrap_or_default();

    for &c in &[conn_id, challenger_conn] {
        if let Some(user) = state.users.get_mut(&c) {
            user.mapa_anterior = user.pos_map;
            user.x_anterior = user.pos_x;
            user.y_anterior = user.pos_y;
            user.en_duelo = true;
            user.en_que_arena = arena as i32;
            user.apuesta_oro = bet;
        }
    }
    if let Some(user) = state.users.get_mut(&conn_id) {
        user.dueliando_contra = their_name.clone();
    }
    if let Some(user) = state.users.get_mut(&challenger_conn) {
        user.dueliando_contra = my_name.clone();
    }

    // Mark arena occupied
    state.arena_ocupada[arena] = true;
    let idx = (arena - 1) * 2;
    state.nombre_dueleando[idx + 1] = my_name.clone();
    state.nombre_dueleando[idx + 2] = their_name.clone();

    // Announce
    let pkt = format!("||548@{}@{}@{}@{}", arena, my_name, their_name, bet);
    state.send_data(SendTarget::ToAll, &pkt).await;

    // Warp to arena positions
    let (p1x, p1y, p2x, p2y) = DUEL_POSITIONS[arena - 1];
    warp_user(state, challenger_conn, DUEL_MAP, p1x, p1y).await;
    warp_user(state, conn_id, DUEL_MAP, p2x, p2y).await;

    info!("[DUEL] {} vs {} in arena {} for {} gold", my_name, their_name, arena, bet);
}

/// /DESAFIO — Create a 1v1 challenge (become the defender). Costs 200K gold, requires lvl 50+.
pub(super) async fn handle_slash_desafio(state: &mut GameState, conn_id: ConnectionId) {
    let (name, class, level, gold, dead) = match state.users.get(&conn_id) {
        Some(u) if u.logged => (u.char_name.clone(), u.class.clone(), u.level, u.gold, u.dead),
        _ => return,
    };

    if dead { return; }
    if level < DESAFIO_MIN_LEVEL {
        state.send_to(conn_id, &format!("{}Necesitas nivel {} para crear un desafio.{}", server_opcodes::CONSOLE_MSG, DESAFIO_MIN_LEVEL, font_types::INFO)).await;
        return;
    }
    if gold < DESAFIO_COST_DEFENDER {
        state.send_to(conn_id, &format!("{}Necesitas {} de oro.{}", server_opcodes::CONSOLE_MSG, DESAFIO_COST_DEFENDER, font_types::INFO)).await;
        return;
    }
    if state.desafio_primero != 0 {
        state.send_to(conn_id, &format!("{}Ya hay un desafio activo.{}", server_opcodes::CONSOLE_MSG, font_types::INFO)).await;
        return;
    }

    // Deduct gold
    if let Some(user) = state.users.get_mut(&conn_id) {
        user.gold -= DESAFIO_COST_DEFENDER;
        user.en_desafio = true;
        user.rondas = 0;
        user.mapa_anterior = user.pos_map;
        user.x_anterior = user.pos_x;
        user.y_anterior = user.pos_y;
    }
    send_stats_gold(state, conn_id).await;

    state.desafio_primero = conn_id;

    // Announce
    let pkt = format!("||407@{}@{}@{}", name, class, level);
    state.send_data(SendTarget::ToAll, &pkt).await;

    // Warp to desafio map
    warp_user(state, conn_id, DESAFIO_MAP, 52, 32).await;

    info!("[DESAFIO] {} created challenge", name);
}

/// /DESAFIAR — Accept an existing 1v1 challenge. Costs 30K gold.
pub(super) async fn handle_slash_desafiar(state: &mut GameState, conn_id: ConnectionId) {
    let (name, gold, dead) = match state.users.get(&conn_id) {
        Some(u) if u.logged => (u.char_name.clone(), u.gold, u.dead),
        _ => return,
    };

    if dead { return; }
    if state.desafio_primero == 0 {
        state.send_to(conn_id, &format!("{}No hay ningun desafio activo.{}", server_opcodes::CONSOLE_MSG, font_types::INFO)).await;
        return;
    }
    if state.desafio_segundo != 0 {
        state.send_to(conn_id, &format!("{}Ya hay un retador peleando.{}", server_opcodes::CONSOLE_MSG, font_types::INFO)).await;
        return;
    }
    if gold < DESAFIO_COST_CHALLENGER {
        state.send_to(conn_id, &format!("{}Necesitas {} de oro.{}", server_opcodes::CONSOLE_MSG, DESAFIO_COST_CHALLENGER, font_types::INFO)).await;
        return;
    }

    // Deduct gold
    if let Some(user) = state.users.get_mut(&conn_id) {
        user.gold -= DESAFIO_COST_CHALLENGER;
        user.en_desafio = true;
        user.mapa_anterior = user.pos_map;
        user.x_anterior = user.pos_x;
        user.y_anterior = user.pos_y;
    }
    send_stats_gold(state, conn_id).await;

    state.desafio_segundo = conn_id;

    // Announce
    let pkt = format!("||410@{}", name);
    state.send_data(SendTarget::ToAll, &pkt).await;

    // Notify defender
    let class = state.users.get(&conn_id).map(|u| u.class.clone()).unwrap_or_default();
    let level = state.users.get(&conn_id).map(|u| u.level).unwrap_or(1);
    let info_pkt = format!("||411@{}@{}@{}", name, class, level);
    state.send_to(state.desafio_primero, &info_pkt).await;

    // Warp to desafio map
    warp_user(state, conn_id, DESAFIO_MAP, 52, 48).await;

    info!("[DESAFIO] {} accepted challenge", name);
}

/// /ABANDONAR — Leave duel/desafio/event.
pub(super) async fn handle_slash_abandonar(state: &mut GameState, conn_id: ConnectionId) {
    let (en_duelo, en_desafio, en_que_arena, mapa_anterior, x_anterior, y_anterior) = match state.users.get(&conn_id) {
        Some(u) if u.logged => (u.en_duelo, u.en_desafio, u.en_que_arena, u.mapa_anterior, u.x_anterior, u.y_anterior),
        _ => return,
    };

    if en_duelo {
        // Forfeit arena duel
        let arena = en_que_arena as usize;
        let opponent_name = state.users.get(&conn_id).map(|u| u.dueliando_contra.clone()).unwrap_or_default();

        // Clear duel state
        if let Some(user) = state.users.get_mut(&conn_id) {
            user.en_duelo = false;
            user.dueliando_contra.clear();
            user.en_que_arena = 0;
        }

        // Warp back
        if mapa_anterior > 0 {
            warp_user(state, conn_id, mapa_anterior, x_anterior, y_anterior).await;
        } else {
            warp_user(state, conn_id, EXIT_MAP, EXIT_X, EXIT_Y).await;
        }

        // Find and reward opponent
        let opponent_upper = opponent_name.to_uppercase();
        if let Some(&opp_conn) = state.online_names.get(&opponent_upper) {
            let bet = state.users.get(&opp_conn).map(|u| u.apuesta_oro).unwrap_or(0);
            if let Some(opp_user) = state.users.get_mut(&opp_conn) {
                opp_user.gold += bet * 3 / 2; // Winner gets 1.5x
                opp_user.en_duelo = false;
                opp_user.dueliando_contra.clear();
                opp_user.en_que_arena = 0;
            }
            send_stats_gold(state, opp_conn).await;
            let (om, ox, oy) = state.users.get(&opp_conn)
                .map(|u| (u.mapa_anterior, u.x_anterior, u.y_anterior))
                .unwrap_or((EXIT_MAP, EXIT_X, EXIT_Y));
            if om > 0 {
                warp_user(state, opp_conn, om, ox, oy).await;
            } else {
                warp_user(state, opp_conn, EXIT_MAP, EXIT_X, EXIT_Y).await;
            }
        }

        // Free arena
        if arena >= 1 && arena <= 4 {
            state.arena_ocupada[arena] = false;
            let idx = (arena - 1) * 2;
            state.nombre_dueleando[idx + 1].clear();
            state.nombre_dueleando[idx + 2].clear();
        }
    } else if en_desafio {
        // Leave desafio
        if state.desafio_primero == conn_id {
            state.desafio_primero = 0;
        }
        if state.desafio_segundo == conn_id {
            state.desafio_segundo = 0;
        }
        if let Some(user) = state.users.get_mut(&conn_id) {
            user.en_desafio = false;
            user.rondas = 0;
        }
        if mapa_anterior > 0 {
            warp_user(state, conn_id, mapa_anterior, x_anterior, y_anterior).await;
        } else {
            warp_user(state, conn_id, EXIT_MAP, EXIT_X, EXIT_Y).await;
        }
    } else {
        // Generic leave — warp to exit
        warp_user(state, conn_id, EXIT_MAP, EXIT_X, EXIT_Y).await;
    }
}

/// /TORNEO — Join an active tournament.
pub(super) async fn handle_slash_torneo(state: &mut GameState, conn_id: ConnectionId) {
    let (name, dead) = match state.users.get(&conn_id) {
        Some(u) if u.logged => (u.char_name.clone(), u.dead),
        _ => return,
    };

    if dead { return; }

    if !state.hay_torneo {
        state.send_to(conn_id, &format!("{}No hay ningun torneo activo.{}", server_opcodes::CONSOLE_MSG, font_types::INFO)).await;
        return;
    }

    let already = state.users.get(&conn_id).map(|u| u.en_torneo).unwrap_or(false);
    if already {
        state.send_to(conn_id, &format!("{}Ya estas inscripto en el torneo.{}", server_opcodes::CONSOLE_MSG, font_types::INFO)).await;
        return;
    }

    if state.usuarios_en_torneo >= 64 {
        state.send_to(conn_id, &format!("{}El torneo esta lleno.{}", server_opcodes::CONSOLE_MSG, font_types::INFO)).await;
        return;
    }

    state.usuarios_en_torneo += 1;
    state.cronologia_participantes.push(name.clone());

    if let Some(user) = state.users.get_mut(&conn_id) {
        user.en_torneo = true;
        user.num_torneo = state.usuarios_en_torneo;
    }

    let msg = format!("{}Te has inscripto al torneo! (#{}){}",
        server_opcodes::CONSOLE_MSG, state.usuarios_en_torneo, font_types::INFO);
    state.send_to(conn_id, &msg).await;

    // Announce
    let pkt = format!("{}{} se ha inscripto al torneo. ({}/64){}",
        server_opcodes::CONSOLE_MSG, name, state.usuarios_en_torneo, font_types::SYSTEM);
    state.send_data(SendTarget::ToAll, &pkt).await;
}

/// /PARTICIPANTES — List tournament participants.
pub(super) async fn handle_slash_participantes(state: &mut GameState, conn_id: ConnectionId) {
    if state.cronologia_participantes.is_empty() {
        state.send_to(conn_id, &format!("{}No hay participantes.{}", server_opcodes::CONSOLE_MSG, font_types::INFO)).await;
        return;
    }

    let msg = format!("{}Participantes del torneo ({}):{}", server_opcodes::CONSOLE_MSG,
        state.cronologia_participantes.len(), font_types::INFO);
    state.send_to(conn_id, &msg).await;

    let participants = state.cronologia_participantes.clone();
    for (i, name) in participants.iter().enumerate() {
        let pkt = format!("{}{}: {}{}", server_opcodes::CONSOLE_MSG, i + 1, name, font_types::INFO);
        state.send_to(conn_id, &pkt).await;
    }
}

/// /HORDA — Join the Horde faction. Warps to map 27.
pub(super) async fn handle_slash_horda(state: &mut GameState, conn_id: ConnectionId) {
    let (dead, level, guild, armada, caos) = match state.users.get(&conn_id) {
        Some(u) if u.logged => (u.dead, u.level, u.guild_index, u.armada_real, u.fuerzas_caos),
        _ => return,
    };

    if dead || level < 10 || guild > 0 || armada || caos {
        state.send_to(conn_id, &format!("{}No puedes unirte a la Horda.{}", server_opcodes::CONSOLE_MSG, font_types::INFO)).await;
        return;
    }

    if let Some(user) = state.users.get_mut(&conn_id) {
        user.criminal = true;
    }

    // Warp to Horde spawn
    warp_user(state, conn_id, 27, 47, 48).await;

    state.send_to(conn_id, &format!("{}Te has unido a la Horda!{}", server_opcodes::CONSOLE_MSG, font_types::SYSTEM)).await;
}

/// /ALIANZA — Join the Alliance faction. Warps to map 29.
pub(super) async fn handle_slash_alianza(state: &mut GameState, conn_id: ConnectionId) {
    let (dead, level, guild, armada, caos) = match state.users.get(&conn_id) {
        Some(u) if u.logged => (u.dead, u.level, u.guild_index, u.armada_real, u.fuerzas_caos),
        _ => return,
    };

    if dead || level < 10 || guild > 0 || armada || caos {
        state.send_to(conn_id, &format!("{}No puedes unirte a la Alianza.{}", server_opcodes::CONSOLE_MSG, font_types::INFO)).await;
        return;
    }

    if let Some(user) = state.users.get_mut(&conn_id) {
        user.criminal = false;
    }

    // Warp to Alliance spawn
    warp_user(state, conn_id, 29, 50, 90).await;

    state.send_to(conn_id, &format!("{}Te has unido a la Alianza!{}", server_opcodes::CONSOLE_MSG, font_types::SYSTEM)).await;
}

/// /PARTICIPAR — Join the currently active event.
pub(super) async fn handle_slash_participar(state: &mut GameState, conn_id: ConnectionId) {
    if state.evento_inscripciones {
        handle_slash_participar_evento(state, conn_id).await;
    } else if state.torneo_auto_activo {
        torneo_auto_join(state, conn_id).await;
    } else {
        state.send_to(conn_id, &format!("{}No hay ningun evento activo en este momento.{}", server_opcodes::CONSOLE_MSG, font_types::INFO)).await;
    }
}

/// /EVENTOS — Show active events info.
pub(super) async fn handle_slash_eventos(state: &mut GameState, conn_id: ConnectionId) {
    let mut msgs = Vec::new();

    if state.evento_activo {
        let tipo_name = event_type_name(state.evento_tipo);
        msgs.push(format!("{}{} en curso ({} participantes).{}", server_opcodes::CONSOLE_MSG, tipo_name, state.evento_participantes.len(), font_types::INFO));
    }
    if state.evento_inscripciones {
        let tipo_name = event_type_name(state.evento_tipo);
        msgs.push(format!("{}Inscripciones abiertas para {} ({}/{}).{}", server_opcodes::CONSOLE_MSG, tipo_name, state.evento_participantes.len(), state.evento_max_players, font_types::INFO));
    }
    if state.torneo_auto_activo {
        let max = 1 << state.torneo_auto_rondas;
        msgs.push(format!("{}Torneo automatico activo ({}/{} slots).{}", server_opcodes::CONSOLE_MSG, state.torneo_auto_bracket.len(), max, font_types::INFO));
    }
    if state.hay_torneo {
        msgs.push(format!("{}Torneo activo con {} participantes.{}", server_opcodes::CONSOLE_MSG, state.usuarios_en_torneo, font_types::INFO));
    }
    if state.cvc_funciona {
        msgs.push(format!("{}CvC en curso.{}", server_opcodes::CONSOLE_MSG, font_types::INFO));
    }
    if state.desafio_primero != 0 {
        let defender = state.users.get(&state.desafio_primero)
            .map(|u| u.char_name.clone())
            .unwrap_or_default();
        msgs.push(format!("{}Desafio activo. Defensor: {}{}", server_opcodes::CONSOLE_MSG, defender, font_types::INFO));
    }
    if state.siege_active {
        msgs.push(format!("{}Asedio al castillo en curso.{}", server_opcodes::CONSOLE_MSG, font_types::INFO));
    }

    if msgs.is_empty() {
        state.send_to(conn_id, &format!("{}No hay eventos activos.{}", server_opcodes::CONSOLE_MSG, font_types::INFO)).await;
    } else {
        for msg in msgs {
            state.send_to(conn_id, &msg).await;
        }
    }
}

/// Restricted maps where CvC participants cannot join from
const CVC_RESTRICTED_MAPS: [i32; 9] = [71, 78, 100, 104, 106, 108, 109, 110, 141];

/// Check if a user is eligible for CvC (alive, seguro_cvc on, not on restricted map, no god items)
pub(super) fn is_cvc_eligible(user: &UserState, objects: &[crate::data::objects::ObjData]) -> bool {
    if !user.logged || user.dead || !user.seguro_cvc { return false; }
    if CVC_RESTRICTED_MAPS.contains(&user.pos_map) { return false; }
    // Check for god items equipped
    let equip_slots = [user.equip.weapon, user.equip.armor, user.equip.shield, user.equip.helmet];
    for slot_idx in equip_slots {
        if slot_idx > 0 && (slot_idx as usize) <= user.inventory.len() {
            let obj_idx = user.inventory[(slot_idx - 1) as usize].obj_index;
            if obj_idx > 0 {
                if let Some(obj) = objects.get((obj_idx - 1) as usize) {
                    if obj.item_dios { return false; }
                }
            }
        }
    }
    true
}

/// Return all CvC participants to their saved positions and clear their CvC flags.
pub(super) async fn llevar_usuarios_cvc(state: &mut GameState) {
    // Collect all en_cvc users
    let cvc_users: Vec<(ConnectionId, i32, i32, i32)> = state.users.iter()
        .filter(|(_, u)| u.en_cvc)
        .map(|(&cid, u)| (cid, u.vieja_pos_map, u.vieja_pos_x, u.vieja_pos_y))
        .collect();

    for (cid, old_map, old_x, old_y) in cvc_users {
        // Clear CvC flags
        if let Some(u) = state.users.get_mut(&cid) {
            u.en_cvc = false;
            u.cvc_blue = false;
            u.vieja_pos_map = 0;
            u.vieja_pos_x = 0;
            u.vieja_pos_y = 0;
        }
        // Warp back (if they have a valid saved position)
        if old_map > 0 {
            warp_user(state, cid, old_map, old_x, old_y).await;
        }
    }
}

/// End the CvC battle. `winner_guild` is the guild index of the winning clan.
pub(super) async fn cvc_end_battle(state: &mut GameState, winner_guild: i32) {
    let loser_guild = if winner_guild == state.cvc_guild1 {
        state.cvc_guild2
    } else {
        state.cvc_guild1
    };

    let winner_name = if winner_guild == state.cvc_guild1 {
        state.cvc_nombre1.clone()
    } else {
        state.cvc_nombre2.clone()
    };
    let loser_name = if loser_guild == state.cvc_guild1 {
        state.cvc_nombre1.clone()
    } else {
        state.cvc_nombre2.clone()
    };

    // Broadcast result: ||85@winner@loser
    let pkt = format!("||85@{}@{}", winner_name, loser_name);
    state.send_data(SendTarget::ToAll, &pkt).await;

    // Update guild files: winner gets +1 CVCG, +75 reputation; loser gets +1 CVCP
    if let Some(mut guild) = guilds::load_guild(&state.pool, winner_guild).await {
        guild.cvc_wins += 1;
        guild.reputation += 75;
        guilds::save_guild(&state.pool, &guild).await;
    }
    if let Some(mut guild) = guilds::load_guild(&state.pool, loser_guild).await {
        guild.cvc_losses += 1;
        guilds::save_guild(&state.pool, &guild).await;
    }

    // Revive dead participants before warping back
    let dead_cvc_users: Vec<ConnectionId> = state.users.iter()
        .filter(|(_, u)| u.en_cvc && u.dead)
        .map(|(&cid, _)| cid)
        .collect();
    for cid in dead_cvc_users {
        revive_user(state, cid).await;
    }

    // Warp everyone back
    llevar_usuarios_cvc(state).await;

    // Clear all CvC state
    state.cvc_funciona = false;
    state.cvc_clan1_count = 0;
    state.cvc_clan2_count = 0;
    state.cvc_guild1 = 0;
    state.cvc_guild2 = 0;
    state.cvc_nombre1.clear();
    state.cvc_nombre2.clear();
}

/// Handle CvC participant death — decrement counter, check if battle ends.
pub(super) async fn cvc_player_death(state: &mut GameState, conn_id: ConnectionId) {
    let is_blue = state.users.get(&conn_id).map(|u| u.cvc_blue).unwrap_or(false);

    if is_blue {
        state.cvc_clan1_count -= 1;
        if state.cvc_clan1_count <= 0 {
            // Blue team eliminated — Red (guild2) wins
            cvc_end_battle(state, state.cvc_guild2).await;
            return;
        }
    } else {
        state.cvc_clan2_count -= 1;
        if state.cvc_clan2_count <= 0 {
            // Red team eliminated — Blue (guild1) wins
            cvc_end_battle(state, state.cvc_guild1).await;
            return;
        }
    }

    // Announce remaining counts
    let msg = format!("{}CvC: {} ({}) vs {} ({}){}",
        server_opcodes::CONSOLE_MSG,
        state.cvc_nombre1, state.cvc_clan1_count,
        state.cvc_nombre2, state.cvc_clan2_count,
        font_types::INFO);
    // Send to all CvC participants
    let cvc_conns: Vec<ConnectionId> = state.users.iter()
        .filter(|(_, u)| u.en_cvc)
        .map(|(&cid, _)| cid)
        .collect();
    for cid in cvc_conns {
        state.send_to(cid, &msg).await;
    }
}

/// Handle CvC participant disconnect — same as death for scoring purposes.
pub async fn cvc_player_disconnect(state: &mut GameState, conn_id: ConnectionId) {
    if !state.cvc_funciona { return; }
    let is_in_cvc = state.users.get(&conn_id).map(|u| u.en_cvc).unwrap_or(false);
    if !is_in_cvc { return; }

    cvc_player_death(state, conn_id).await;

    // Clear CvC flags on the disconnecting user
    if let Some(u) = state.users.get_mut(&conn_id) {
        u.en_cvc = false;
        u.cvc_blue = false;
    }
}

/// /CVC <clan_name> — Challenge another clan to CvC. Requires guild leader/sub-leader.
pub(super) async fn handle_slash_cvc(state: &mut GameState, conn_id: ConnectionId, target_clan: &str) {
    let (char_name, guild_index, dead, map) = match state.users.get(&conn_id) {
        Some(u) if u.logged => (u.char_name.clone(), u.guild_index, u.dead, u.pos_map),
        _ => return,
    };

    if dead {
        state.send_to(conn_id, &format!("{}Estas muerto!{}", server_opcodes::CONSOLE_MSG, font_types::INFO)).await;
        return;
    }
    if guild_index <= 0 {
        state.send_to(conn_id, "||120").await; // No guild
        return;
    }
    if state.cvc_funciona {
        state.send_to(conn_id, "||364").await; // CvC already active
        return;
    }
    if state.cvc_pending_target_guild > 0 {
        state.send_to(conn_id, &format!("{}Ya hay un desafio CvC pendiente.{}", server_opcodes::CONSOLE_MSG, font_types::INFO)).await;
        return;
    }
    if map == 141 { return; } // Jail

    // Validate caller is leader or sub-leader of their guild
    let my_guild = guilds::load_guild(&state.pool, guild_index).await;
    let is_leader = match &my_guild {
        Some(g) => {
            let name_upper = char_name.to_uppercase();
            g.leader.to_uppercase() == name_upper
                || g.sub_lider1.to_uppercase() == name_upper
                || g.sub_lider2.to_uppercase() == name_upper
        }
        None => false,
    };
    if !is_leader {
        state.send_to(conn_id, &format!("{}Solo el lider o sub-lider puede desafiar a CvC.{}", server_opcodes::CONSOLE_MSG, font_types::INFO)).await;
        return;
    }

    // Find target guild
    let target_upper = target_clan.to_uppercase();
    let target_guild_idx = match guilds::find_guild_by_name(&state.pool, &target_upper).await {
        Some(idx) if idx != guild_index => idx,
        _ => {
            state.send_to(conn_id, &format!("{}Clan no encontrado o es tu propio clan.{}", server_opcodes::CONSOLE_MSG, font_types::INFO)).await;
            return;
        }
    };

    // Count eligible members from challenger's clan
    let objects = &state.game_data.objects;
    let my_eligible: i32 = state.users.values()
        .filter(|u| u.guild_index == guild_index && is_cvc_eligible(u, objects))
        .count() as i32;

    if my_eligible < 1 {
        state.send_to(conn_id, &format!("{}Tu clan no tiene miembros elegibles para CvC.{}", server_opcodes::CONSOLE_MSG, font_types::INFO)).await;
        return;
    }

    // Find the target guild's leader online to send the challenge
    let target_guild = guilds::load_guild(&state.pool, target_guild_idx).await;
    let target_leader_name = target_guild.as_ref().map(|g| g.leader.to_uppercase()).unwrap_or_default();
    let target_leader_conn = state.users.iter()
        .find(|(_, u)| u.logged && u.guild_index == target_guild_idx && u.char_name.to_uppercase() == target_leader_name)
        .map(|(&cid, _)| cid);

    if target_leader_conn.is_none() {
        state.send_to(conn_id, &format!("{}El lider del clan {} no esta online.{}", server_opcodes::CONSOLE_MSG, target_clan, font_types::INFO)).await;
        return;
    }
    let target_leader_id = target_leader_conn.unwrap();

    // Store pending challenge
    let my_guild_name = my_guild.as_ref().map(|g| g.name.clone()).unwrap_or_default();
    state.cvc_pending_challenger_guild = guild_index;
    state.cvc_pending_target_guild = target_guild_idx;
    state.cvc_pending_challenger_name = my_guild_name.clone();

    // Send challenge to target leader: ||413@<challenger_clan>@<eligible_count>
    let pkt = format!("||413@{}@{}", my_guild_name, my_eligible);
    state.send_to(target_leader_id, &pkt).await;

    state.send_to(conn_id, &format!("{}Desafio CvC enviado a {}. Esperando respuesta de su lider.{}", server_opcodes::CONSOLE_MSG, target_clan, font_types::INFO)).await;
}

/// /NCVC — Disable CvC safety (opt out of auto-warp).
pub(super) async fn handle_slash_ncvc(state: &mut GameState, conn_id: ConnectionId) {
    if let Some(user) = state.users.get_mut(&conn_id) {
        user.seguro_cvc = false;
    }
    state.send_to(conn_id, "||370").await;
}

/// /SCVC — Enable CvC safety (opt in for auto-warp).
pub(super) async fn handle_slash_scvc(state: &mut GameState, conn_id: ConnectionId) {
    if let Some(user) = state.users.get_mut(&conn_id) {
        user.seguro_cvc = true;
    }
    state.send_to(conn_id, "||371").await;
}

/// /REGRESAR — Return to home city (die and respawn at home).
/// VB6: TCP_HandleData2.bas lines 741-827
pub(super) async fn handle_slash_regresar(state: &mut GameState, conn_id: ConnectionId) {
    let user_data = match state.users.get(&conn_id) {
        Some(u) if u.logged => (
            u.dead, u.privileges, u.level, u.pos_map,
            u.hogar.clone(), u.armada_real, u.fuerzas_caos,
        ),
        _ => return,
    };
    let (dead, privileges, level, cur_map, hogar, armada_real, fuerzas_caos) = user_data;

    // GMs (Consejero+) cannot use /regresar
    if privileges >= privilege_level::CONSEJERO {
        return;
    }

    // Level check — must be level 10+
    if level < 10 {
        let msg = format!("{}Debes ser nivel 10 o superior para usar /REGRESAR.{}", server_opcodes::CONSOLE_MSG, font_types::INFO);
        state.send_to(conn_id, &msg).await;
        return;
    }

    // Block in special maps (31-34 arenas)
    if cur_map >= 31 && cur_map <= 34 {
        let msg = format!("{}No puedes usar /REGRESAR en esta zona.{}", server_opcodes::CONSOLE_MSG, font_types::INFO);
        state.send_to(conn_id, &msg).await;
        return;
    }

    // Kill the player if not already dead
    if !dead {
        if let Some(user) = state.users.get_mut(&conn_id) {
            user.min_hp = 0;
            user.dead = true;
            user.poisoned = false;
            user.paralyzed = false;
            user.meditating = false;
            user.min_sta = 0;
        }
        state.send_to(conn_id, "MUERT").await;
        send_stats_hp(state, conn_id).await;
        // Update appearance to dead body
        if let Some(user) = state.users.get(&conn_id) {
            let cc = user.build_cc_packet();
            let (m, ux, uy) = (user.pos_map, user.pos_x, user.pos_y);
            state.send_data(SendTarget::ToArea { map: m, x: ux, y: uy }, &cc).await;
        }
    }

    // Determine destination based on faction/home
    let (dest_map, dest_x, dest_y) = if armada_real {
        // Ejército Real → Lindos (map 29)
        (29, 50, 90)
    } else if fuerzas_caos {
        // Legión Oscura → Barak (map 27)
        (27, 47, 48)
    } else {
        // Regular player → based on Hogar
        match hogar.to_uppercase().as_str() {
            "THIR" => (25, 74, 44),
            "INTHAK" => (130, 52, 56),
            "RUVENDEL" => (26, 51, 52),
            _ => (28, 54, 36), // Default: Ullathorpe
        }
    };

    warp_user(state, conn_id, dest_map, dest_x, dest_y).await;

    let msg = format!("{}Has regresado a tu hogar.{}", server_opcodes::CONSOLE_MSG, font_types::INFO);
    state.send_to(conn_id, &msg).await;
}

/// /SALIR — Disconnect/logout.
pub(super) async fn handle_slash_salir(state: &mut GameState, conn_id: ConnectionId) {
    close_connection(state, conn_id).await;
}

/// /MEDITAR — Toggle meditation. VB6: level-based FX, GMs get instant full mana.
/// FX IDs: chico=4 (<15), mediano=5 (15-29), grande=6 (30-49), xgrande=43 (50-59),
/// neutral=103/alianza=104/horda=105 (60+), transfo=16 (transformed).
pub(super) async fn handle_slash_meditar(state: &mut GameState, conn_id: ConnectionId) {
    let (dead, privileges, meditating, max_mana) = match state.users.get(&conn_id) {
        Some(u) if u.logged => (u.dead, u.privileges, u.meditating, u.max_mana),
        _ => return,
    };

    // VB6: If Muerto = 1 Then ||3
    if dead {
        state.send_to(conn_id, "||3").await;
        return;
    }

    // VB6: If MaxMAN = 0 Then ||4
    if max_mana == 0 {
        state.send_to(conn_id, "||4").await;
        return;
    }

    // GMs get instant full mana (VB6: Privilegios > User)
    if privileges > privilege_level::USER {
        if let Some(user) = state.users.get_mut(&conn_id) {
            user.min_mana = user.max_mana;
        }
        state.send_to(conn_id, "||393").await;
        send_stats_mana(state, conn_id).await;
        state.send_to(conn_id, "MEDOK").await;
        return;
    }

    // Toggle meditation
    let was_meditating = meditating;
    if let Some(user) = state.users.get_mut(&conn_id) {
        user.meditating = !was_meditating;
    }

    if !was_meditating {
        // Starting meditation — VB6: ||394 + MEDOK
        state.send_to(conn_id, "||394").await;
        state.send_to(conn_id, "MEDOK").await;

        // VB6: If MinMAN = MaxMAN Then exit (already full)
        let min_mana = state.users.get(&conn_id).map(|u| u.min_mana).unwrap_or(0);
        if min_mana >= max_mana { return; }

        // VB6: Send level-based meditation FX
        let (ci, map, x, y, level, transformed) = match state.users.get(&conn_id) {
            Some(u) => (u.char_index.0, u.pos_map, u.pos_x, u.pos_y, u.level, u.transformed),
            None => return,
        };

        let fx_id = if transformed {
            16 // FXMEDITARTRANSFO
        } else if level < 15 {
            4  // FXMEDITARCHICO
        } else if level < 30 {
            5  // FXMEDITARMEDIANO
        } else if level < 50 {
            6  // FXMEDITARGRANDE
        } else if level <= 59 {
            43 // FXMEDITARXGRANDE
        } else {
            // Level 60+ — faction-based
            103 // FXNUEVATPNEUTRAL (default)
        };

        let loops = 999; // LoopAdEternum
        let pkt = format!("CFX{},{},{}", ci, fx_id, loops);
        state.send_data(SendTarget::ToArea { map, x, y }, &pkt).await;
    } else {
        // Stopping meditation — VB6: ||205 + MEDOK + clear FX
        state.send_to(conn_id, "||205").await;
        state.send_to(conn_id, "MEDOK").await;

        // VB6: Clear FX for area
        let (ci, map, x, y) = match state.users.get(&conn_id) {
            Some(u) => (u.char_index.0, u.pos_map, u.pos_x, u.pos_y),
            None => return,
        };
        let pkt = format!("CFX{},0,0", ci);
        state.send_data(SendTarget::ToArea { map, x, y }, &pkt).await;
    }
}

/// Resolve a duel when a player dies in an arena — called from user_die.
pub async fn resolve_duel_death(state: &mut GameState, dead_conn: ConnectionId) {
    let arena = match state.users.get(&dead_conn) {
        Some(u) if u.en_duelo => u.en_que_arena as usize,
        _ => return,
    };
    if arena < 1 || arena > 4 { return; }

    let loser_name = state.users.get(&dead_conn).map(|u| u.char_name.clone()).unwrap_or_default();
    let opponent_name = state.users.get(&dead_conn).map(|u| u.dueliando_contra.clone()).unwrap_or_default();
    let bet = state.users.get(&dead_conn).map(|u| u.apuesta_oro).unwrap_or(0);

    // Find winner
    let opponent_upper = opponent_name.to_uppercase();
    let winner_conn = state.online_names.get(&opponent_upper).copied();

    // Award winner
    let winnings = bet * 3 / 2; // 1.5x bet
    if let Some(w_conn) = winner_conn {
        if let Some(winner) = state.users.get_mut(&w_conn) {
            winner.gold += winnings;
            winner.en_duelo = false;
            winner.dueliando_contra.clear();
            winner.en_que_arena = 0;
        }
        send_stats_gold(state, w_conn).await;

        // Warp winner back
        let (wm, wx, wy) = state.users.get(&w_conn)
            .map(|u| (u.mapa_anterior, u.x_anterior, u.y_anterior))
            .unwrap_or((EXIT_MAP, EXIT_X, EXIT_Y));
        if wm > 0 {
            warp_user(state, w_conn, wm, wx, wy).await;
        } else {
            warp_user(state, w_conn, EXIT_MAP, EXIT_X, EXIT_Y).await;
        }
    }

    // Clear loser state
    if let Some(loser) = state.users.get_mut(&dead_conn) {
        loser.en_duelo = false;
        loser.dueliando_contra.clear();
        loser.en_que_arena = 0;
        loser.apuesta_oro = 0;
    }

    // Warp loser back
    let (lm, lx, ly) = state.users.get(&dead_conn)
        .map(|u| (u.mapa_anterior, u.x_anterior, u.y_anterior))
        .unwrap_or((EXIT_MAP, EXIT_X, EXIT_Y));
    if lm > 0 {
        warp_user(state, dead_conn, lm, lx, ly).await;
    } else {
        warp_user(state, dead_conn, EXIT_MAP, EXIT_X, EXIT_Y).await;
    }

    // Free arena
    state.arena_ocupada[arena] = false;
    let idx = (arena - 1) * 2;
    state.nombre_dueleando[idx + 1].clear();
    state.nombre_dueleando[idx + 2].clear();

    // Announce result
    let pkt = format!("||691@{}@{}@{}@{}", arena, opponent_name, loser_name, winnings);
    state.send_data(SendTarget::ToAll, &pkt).await;

    info!("[DUEL] {} defeated {} in arena {}. Won {} gold.", opponent_name, loser_name, arena, winnings);
}

/// Resolve desafio when a player dies — called from user_die.
pub async fn resolve_desafio_death(state: &mut GameState, dead_conn: ConnectionId) {
    let is_defender = state.desafio_primero == dead_conn;
    let is_challenger = state.desafio_segundo == dead_conn;

    if !is_defender && !is_challenger { return; }

    if is_challenger {
        // Challenger died — defender wins the round
        state.desafio_segundo = 0;

        // Increment defender rounds
        if let Some(defender) = state.users.get_mut(&state.desafio_primero) {
            defender.rondas += 1;
            // Restore HP/MP
            defender.min_hp = defender.max_hp;
            defender.min_mana = defender.max_mana;
        }
        let rounds = state.users.get(&state.desafio_primero).map(|u| u.rondas).unwrap_or(0);
        send_stats_hp(state, state.desafio_primero).await;
        send_stats_mana(state, state.desafio_primero).await;

        // Warp challenger out
        let (lm, lx, ly) = state.users.get(&dead_conn)
            .map(|u| (u.mapa_anterior, u.x_anterior, u.y_anterior))
            .unwrap_or((EXIT_MAP, EXIT_X, EXIT_Y));
        if let Some(user) = state.users.get_mut(&dead_conn) {
            user.en_desafio = false;
        }
        if lm > 0 {
            warp_user(state, dead_conn, lm, lx, ly).await;
        } else {
            warp_user(state, dead_conn, EXIT_MAP, EXIT_X, EXIT_Y).await;
        }

        // Announce rounds milestone
        if rounds == 3 || rounds == 5 || rounds == 10 || rounds == 20 || rounds == 50 || rounds >= 100 {
            let defender_name = state.users.get(&state.desafio_primero).map(|u| u.char_name.clone()).unwrap_or_default();
            let pkt = format!("{}El defensor {} lleva {} rondas!{}", server_opcodes::CONSOLE_MSG, defender_name, rounds, font_types::SYSTEM);
            state.send_data(SendTarget::ToAll, &pkt).await;
        }
    } else {
        // Defender died — challenger wins
        let defender_conn = state.desafio_primero;
        let challenger_conn = state.desafio_segundo;

        // Award challenger
        if let Some(winner) = state.users.get_mut(&challenger_conn) {
            winner.gold += DESAFIO_REWARD;
            winner.en_desafio = false;
        }
        send_stats_gold(state, challenger_conn).await;

        // Clear defender
        if let Some(loser) = state.users.get_mut(&defender_conn) {
            loser.en_desafio = false;
            loser.rondas = 0;
        }

        // Reset desafio
        state.desafio_primero = 0;
        state.desafio_segundo = 0;

        // Warp both to exit
        warp_user(state, challenger_conn, EXIT_MAP, EXIT_X, EXIT_Y).await;
        warp_user(state, defender_conn, EXIT_MAP, EXIT_X, EXIT_Y).await;

        let winner_name = state.users.get(&challenger_conn).map(|u| u.char_name.clone()).unwrap_or_default();
        let pkt = format!("{}{} ha ganado el desafio y recibe {} de oro!{}", server_opcodes::CONSOLE_MSG, winner_name, DESAFIO_REWARD, font_types::SYSTEM);
        state.send_data(SendTarget::ToAll, &pkt).await;
    }
}

// =====================================================================
// Nobility System (modNobleza.bas)
// =====================================================================
// 3-stage quest on Map 141: kill dragons → hatchlings → Smaug.
// Time-limited per stage. Teleports player on completion or timeout.

const NOBILITY_MAP: i32 = 141;
const NOBILITY_DRAGON_NPC: i32 = 968;
const NOBILITY_CRIA_NPC: i32 = 969;
const NOBILITY_SMAUG_NPC: i32 = 970;

/// Start the nobility quest stage 1 — spawn 4 small dragons.
pub async fn nobleza_etapa_uno(state: &mut GameState, conn_id: ConnectionId) {
    // Check if someone else is already doing the quest
    if state.nobility_user != 0 {
        let msg = format!("{}Alguien ya esta realizando la prueba de nobleza{}", server_opcodes::CONSOLE_MSG, font_types::INFO);
        state.send_to(conn_id, &msg).await;
        return;
    }

    state.nobility_user = conn_id;
    state.nobility_stage = 1;
    state.nobility_timer = 125; // ~5 minutes at 1 tick/s
    state.nobility_kills = 0;

    // Spawn 4 small dragons on map 141
    let spawn_positions = [(50, 50), (55, 50), (50, 55), (55, 55)];
    for (sx, sy) in &spawn_positions {
        spawn_npc_at(state, NOBILITY_DRAGON_NPC as usize, NOBILITY_MAP, *sx, *sy).await;
    }

    let msg = format!("{}Etapa 1: Elimina a los 4 dragones{}", server_opcodes::CONSOLE_MSG, font_types::INFO);
    state.send_to(conn_id, &msg).await;
}

/// Start stage 2 — spawn 2 Smaug hatchlings.
pub(super) async fn nobleza_etapa_dos(state: &mut GameState) {
    state.nobility_stage = 2;
    state.nobility_timer = 125;
    state.nobility_kills = 0;

    let spawn_positions = [(52, 52), (53, 53)];
    for (sx, sy) in &spawn_positions {
        spawn_npc_at(state, NOBILITY_CRIA_NPC as usize, NOBILITY_MAP, *sx, *sy).await;
    }

    let conn_id = state.nobility_user;
    let msg = format!("{}Etapa 2: Elimina a las 2 crias de Smaug{}", server_opcodes::CONSOLE_MSG, font_types::INFO);
    state.send_to(conn_id, &msg).await;
}

/// Start stage 3 — spawn Smaug.
pub(super) async fn nobleza_etapa_tres(state: &mut GameState) {
    state.nobility_stage = 3;
    state.nobility_timer = 250; // ~10 minutes
    state.nobility_kills = 0;

    spawn_npc_at(state, NOBILITY_SMAUG_NPC as usize, NOBILITY_MAP, 52, 52).await;

    let conn_id = state.nobility_user;
    let msg = format!("{}Etapa 3: Elimina a Smaug{}", server_opcodes::CONSOLE_MSG, font_types::INFO);
    state.send_to(conn_id, &msg).await;
}

/// Called when an NPC is killed — check if it's part of the nobility quest.
pub fn nobleza_check_npc_kill(state: &mut GameState, npc_number: i32) {
    if state.nobility_user == 0 { return; }

    match state.nobility_stage {
        1 if npc_number == NOBILITY_DRAGON_NPC => {
            state.nobility_kills += 1;
        }
        2 if npc_number == NOBILITY_CRIA_NPC => {
            state.nobility_kills += 1;
        }
        3 if npc_number == NOBILITY_SMAUG_NPC => {
            state.nobility_kills += 1;
        }
        _ => {}
    }
}

/// Tick the nobility quest timer. Called every second.
pub async fn tick_nobleza(state: &mut GameState) {
    if state.nobility_user == 0 { return; }

    state.nobility_timer -= 1;

    // Check stage completion
    let advance = match state.nobility_stage {
        1 => state.nobility_kills >= 4,
        2 => state.nobility_kills >= 2,
        3 => state.nobility_kills >= 1,
        _ => false,
    };

    if advance {
        match state.nobility_stage {
            1 => nobleza_etapa_dos(state).await,
            2 => nobleza_etapa_tres(state).await,
            3 => {
                // Quest complete!
                let conn_id = state.nobility_user;
                let msg = format!("{}Has completado la prueba de nobleza!{}", server_opcodes::CONSOLE_MSG, font_types::INFO);
                state.send_to(conn_id, &msg).await;

                // Give reputation reward
                if let Some(u) = state.users.get_mut(&conn_id) {
                    u.reputation += 500;
                }

                state.nobility_user = 0;
                state.nobility_stage = 0;
                state.nobility_timer = 0;
                state.nobility_kills = 0;
            }
            _ => {}
        }
        return;
    }

    // Check timeout
    if state.nobility_timer <= 0 {
        let conn_id = state.nobility_user;
        let msg = format!("{}El tiempo se ha acabado. Has fallado la prueba{}", server_opcodes::CONSOLE_MSG, font_types::INFO);
        state.send_to(conn_id, &msg).await;

        // Teleport back to Tanaris (map 1, 50, 50)
        warp_user(state, conn_id, 1, 50, 50).await;

        state.nobility_user = 0;
        state.nobility_stage = 0;
        state.nobility_timer = 0;
        state.nobility_kills = 0;
    }
}

/// Helper: spawn a specific NPC at a given position.
pub(super) async fn spawn_npc_at(state: &mut GameState, npc_number: usize, map: i32, x: i32, y: i32) {
    let data = match state.game_data.npcs.get(npc_number) {
        Some(d) => d.clone(),
        None => return,
    };

    let idx = state.next_npc_index;
    state.next_npc_index += 1;

    let char_index = state.world.alloc_char_index();
    let mut npc = npc::NpcState::from_data(idx, &data, char_index, map, x, y);
    npc.respawn = false; // Don't respawn nobility quest NPCs

    // Place on world
    let grid = state.world.grid_mut(map);
    if let Some(tile) = grid.tile_mut(x, y) {
        tile.npc_index = idx as i32;
    }

    // Broadcast CC to area
    let cc_pkt = npc.build_cc_packet();
    state.send_data(SendTarget::ToArea { map, x, y }, &cc_pkt).await;

    // Store NPC
    if idx >= state.npcs.len() {
        state.npcs.resize(idx + 1, None);
    }
    state.npcs[idx] = Some(npc);
}

/// Helper: send CC packets for all nearby characters (users + NPCs) in area.
pub(super) async fn send_area_chars(state: &mut GameState, conn_id: ConnectionId, map: i32, x: i32, y: i32) {
    // Send CC for nearby users
    let nearby_users: Vec<(ConnectionId, String)> = state.users.values()
        .filter(|u| u.logged && u.conn_id != conn_id && u.pos_map == map
                && (u.pos_x - x).abs() <= world::MIN_X_BORDER
                && (u.pos_y - y).abs() <= world::MIN_Y_BORDER)
        .map(|u| (u.conn_id, u.build_cc_packet()))
        .collect();

    for (_uid, cc_pkt) in &nearby_users {
        state.send_to(conn_id, cc_pkt).await;
    }

    // Send CC for nearby NPCs
    let mut npc_ccs: Vec<String> = Vec::new();
    for npc_opt in state.npcs.iter() {
        if let Some(npc) = npc_opt {
            if npc.active && npc.map == map
                && (npc.x - x).abs() <= world::MIN_X_BORDER
                && (npc.y - y).abs() <= world::MIN_Y_BORDER
            {
                npc_ccs.push(npc.build_cc_packet());
            }
        }
    }
    for cc_pkt in &npc_ccs {
        state.send_to(conn_id, cc_pkt).await;
    }
}

// =====================================================================
// Event Systems — CTF, JDH, LUZ, ARAM, Batalla Mistica, Faccionario,
// Torneos Automaticos, Guerras
// =====================================================================

/// Event type constants
const EVENTO_NONE: i32 = 0;
const EVENTO_CTF: i32 = 1;
const EVENTO_JDH: i32 = 2;
const EVENTO_LUZ: i32 = 3;
const EVENTO_ARAM: i32 = 4;
const EVENTO_BAT_MISTICA: i32 = 5;
const EVENTO_FACCIONARIO: i32 = 6;
const EVENTO_TORNEO_AUTO: i32 = 7;
const EVENTO_GUERRA: i32 = 8;

/// CTF map and constants
const CTF_MAP: i32 = 166;
const CTF_MAX_PLAYERS: i32 = 10;
const CTF_POINTS_TO_WIN: i32 = 3;

/// JDH (Juegos del Hambre) constants
const JDH_MAX_PLAYERS: i32 = 10;

/// LUZ event constants
const LUZ_MAX_PLAYERS: i32 = 14;

/// ARAM constants
const ARAM_MAP_A: i32 = 189;
const ARAM_MAP_B: i32 = 186;
const ARAM_TOWER_BLUE_NPC: usize = 964;
const ARAM_TOWER_RED_NPC: usize = 963;

/// Batalla Mistica constants
const BAT_MISTICA_MAP: i32 = 191;
const BAT_MISTICA_DURATION: i32 = 420; // 7 minutes in seconds

/// Faccionario event maps
const FACC_MAP_A: i32 = 185;
const FACC_MAP_B: i32 = 184;
const FACC_DURATION: i32 = 600; // 10 minutes in seconds

/// Torneo Automatico constants
const TORNEO_AUTO_MAP: i32 = 100;

/// Guerra map
const GUERRA_MAP: i32 = 164;

// ---- Generic event management ----

/// GM: Start event inscriptions.
/// /EVENTO <type> <max_players> <cost> <map>
pub(super) async fn handle_gm_evento(state: &mut GameState, conn_id: ConnectionId, args: &str) {
    let priv_level = state.users.get(&conn_id).map(|u| u.privileges).unwrap_or(0);
    if priv_level < privilege_level::EVENT_MASTER { return; }

    if state.evento_activo || state.evento_inscripciones {
        state.send_to(conn_id, &format!("{}Ya hay un evento activo.{}", server_opcodes::CONSOLE_MSG, font_types::INFO)).await;
        return;
    }

    let parts: Vec<&str> = args.split_whitespace().collect();
    let tipo: i32 = parts.first().and_then(|s| s.parse().ok()).unwrap_or(0);
    let max_p: i32 = parts.get(1).and_then(|s| s.parse().ok()).unwrap_or(10);
    let cost: i64 = parts.get(2).and_then(|s| s.parse().ok()).unwrap_or(0);
    let map: i32 = parts.get(3).and_then(|s| s.parse().ok()).unwrap_or(0);

    if tipo < 1 || tipo > 8 {
        state.send_to(conn_id, &format!("{}Tipos: 1=CTF 2=JDH 3=LUZ 4=ARAM 5=BatMistica 6=Faccionario 7=TorneoAuto 8=Guerra{}", server_opcodes::CONSOLE_MSG, font_types::INFO)).await;
        return;
    }

    let event_map = if map > 0 { map } else {
        match tipo {
            EVENTO_CTF => CTF_MAP,
            EVENTO_ARAM => ARAM_MAP_A,
            EVENTO_BAT_MISTICA => BAT_MISTICA_MAP,
            EVENTO_GUERRA => GUERRA_MAP,
            EVENTO_TORNEO_AUTO => TORNEO_AUTO_MAP,
            _ => 100, // Generic fallback
        }
    };

    state.evento_inscripciones = true;
    state.evento_tipo = tipo;
    state.evento_max_players = max_p;
    state.evento_costo = cost;
    state.evento_map = event_map;
    state.evento_participantes.clear();
    state.evento_timer = 180; // 3 min signup window
    state.evento_countdown = 0;

    // Reset event-specific state
    state.ctf_puntos_azul = 0;
    state.ctf_puntos_rojo = 0;
    state.bat_kills = [0; 5];

    let tipo_name = event_type_name(tipo);
    let msg = format!("{}Inscripciones abiertas para {}! Costo: {} oro. Usa /PARTICIPAR para inscribirte.{}",
        server_opcodes::CONSOLE_MSG, tipo_name, cost, font_types::INFO);
    state.send_data(SendTarget::ToAll, &msg).await;

    info!("[EVENT] {} inscriptions opened (max {}, cost {}, map {})", tipo_name, max_p, cost, event_map);
}

/// /PARTICIPAR — Join the active event inscription.
pub(super) async fn handle_slash_participar_evento(state: &mut GameState, conn_id: ConnectionId) {
    if !state.evento_inscripciones {
        state.send_to(conn_id, &format!("{}No hay inscripciones abiertas.{}", server_opcodes::CONSOLE_MSG, font_types::INFO)).await;
        return;
    }

    let (name, gold, dead, en_evento) = match state.users.get(&conn_id) {
        Some(u) if u.logged => (u.char_name.clone(), u.gold, u.dead, u.en_evento),
        _ => return,
    };

    if dead || en_evento {
        state.send_to(conn_id, &format!("{}No puedes inscribirte ahora.{}", server_opcodes::CONSOLE_MSG, font_types::INFO)).await;
        return;
    }

    if state.evento_participantes.len() as i32 >= state.evento_max_players {
        state.send_to(conn_id, &format!("{}El evento esta lleno.{}", server_opcodes::CONSOLE_MSG, font_types::INFO)).await;
        return;
    }

    if state.evento_participantes.contains(&conn_id) {
        state.send_to(conn_id, &format!("{}Ya estas inscripto.{}", server_opcodes::CONSOLE_MSG, font_types::INFO)).await;
        return;
    }

    if gold < state.evento_costo {
        state.send_to(conn_id, &format!("{}No tenes suficiente oro (necesitas {}).{}", server_opcodes::CONSOLE_MSG, state.evento_costo, font_types::INFO)).await;
        return;
    }

    // Deduct gold
    let cost = state.evento_costo;
    if let Some(user) = state.users.get_mut(&conn_id) {
        user.gold -= cost;
    }
    send_stats_gold(state, conn_id).await;

    state.evento_participantes.push(conn_id);
    let count = state.evento_participantes.len();

    let msg = format!("{}Te inscribiste al evento! ({}/{}){}",
        server_opcodes::CONSOLE_MSG, count, state.evento_max_players, font_types::INFO);
    state.send_to(conn_id, &msg).await;

    // Auto-start when full
    if count as i32 >= state.evento_max_players {
        evento_begin(state).await;
    }
}

/// Begin the event — assign teams, warp players, start countdown.
pub(super) async fn evento_begin(state: &mut GameState) {
    state.evento_inscripciones = false;
    state.evento_activo = true;
    state.evento_countdown = 6; // 6 second countdown before action

    let tipo = state.evento_tipo;
    let map = state.evento_map;
    let participants = state.evento_participantes.clone();

    // Assign teams based on event type
    match tipo {
        EVENTO_CTF => {
            let half = participants.len() / 2;
            for (i, &pid) in participants.iter().enumerate() {
                let team = if i < half { 1 } else { 2 }; // 1=Azul, 2=Rojo
                if let Some(user) = state.users.get_mut(&pid) {
                    user.mapa_anterior = user.pos_map;
                    user.x_anterior = user.pos_x;
                    user.y_anterior = user.pos_y;
                    user.en_evento = true;
                    user.evento_tipo = tipo;
                    user.evento_equipo = team;
                    user.evento_muertes = 0;
                    user.not_move = true;
                }
                // CTF spawn: team 1 left side, team 2 right side
                let (sx, sy) = if i < half {
                    (20 + (i as i32 % 5) * 2, 50)
                } else {
                    (80 - ((i - half) as i32 % 5) * 2, 50)
                };
                warp_user(state, pid, CTF_MAP, sx, sy).await;
            }
            state.evento_timer = 300; // 5 min match
        }
        EVENTO_JDH => {
            for (i, &pid) in participants.iter().enumerate() {
                if let Some(user) = state.users.get_mut(&pid) {
                    user.mapa_anterior = user.pos_map;
                    user.x_anterior = user.pos_x;
                    user.y_anterior = user.pos_y;
                    user.en_evento = true;
                    user.evento_tipo = tipo;
                    user.evento_equipo = 0; // FFA
                    user.evento_muertes = 0;
                    user.not_move = true;
                }
                // Spread around map
                let sx = 20 + (i as i32 % 10) * 7;
                let sy = 20 + (i as i32 / 10) * 7;
                warp_user(state, pid, map, sx, sy).await;
            }
            state.evento_timer = 600; // 10 min max
        }
        EVENTO_LUZ => {
            for (i, &pid) in participants.iter().enumerate() {
                if let Some(user) = state.users.get_mut(&pid) {
                    user.mapa_anterior = user.pos_map;
                    user.x_anterior = user.pos_x;
                    user.y_anterior = user.pos_y;
                    user.en_evento = true;
                    user.evento_tipo = tipo;
                    user.evento_equipo = 0;
                    user.not_move = true;
                }
                let sx = 40 + (i as i32 % 7) * 3;
                let sy = 40 + (i as i32 / 7) * 3;
                warp_user(state, pid, map, sx, sy).await;
            }
            state.evento_timer = 300;
        }
        EVENTO_ARAM => {
            let half = participants.len() / 2;
            for (i, &pid) in participants.iter().enumerate() {
                let team = if i < half { 1 } else { 2 };
                if let Some(user) = state.users.get_mut(&pid) {
                    user.mapa_anterior = user.pos_map;
                    user.x_anterior = user.pos_x;
                    user.y_anterior = user.pos_y;
                    user.en_evento = true;
                    user.evento_tipo = tipo;
                    user.evento_equipo = team;
                    user.evento_muertes = 0;
                    user.not_move = true;
                }
                let (sx, sy) = if team == 1 {
                    (44 + (i as i32 % 5) * 2, 20 + (i as i32 / 5) * 2)
                } else {
                    (44 + ((i - half) as i32 % 5) * 2, 71 + ((i - half) as i32 / 5) * 2)
                };
                warp_user(state, pid, map, sx, sy).await;
            }
            // Spawn towers
            if let Some(idx) = state.spawn_npc(ARAM_TOWER_BLUE_NPC, map, 50, 65) {
                state.aram_torre_azul = idx;
                if let Some(npc) = state.get_npc(idx) {
                    let cc = npc.build_cc_packet();
                    state.send_data(SendTarget::ToMap(map), &cc).await;
                }
            }
            if let Some(idx) = state.spawn_npc(ARAM_TOWER_RED_NPC, map, 50, 35) {
                state.aram_torre_roja = idx;
                if let Some(npc) = state.get_npc(idx) {
                    let cc = npc.build_cc_packet();
                    state.send_data(SendTarget::ToMap(map), &cc).await;
                }
            }
            state.evento_timer = 600;
        }
        EVENTO_BAT_MISTICA => {
            let team_size = participants.len() / 4;
            for (i, &pid) in participants.iter().enumerate() {
                let team = (i / team_size.max(1)) as i32 + 1;
                let team = team.min(4);
                if let Some(user) = state.users.get_mut(&pid) {
                    user.mapa_anterior = user.pos_map;
                    user.x_anterior = user.pos_x;
                    user.y_anterior = user.pos_y;
                    user.en_evento = true;
                    user.evento_tipo = tipo;
                    user.evento_equipo = team;
                    user.evento_muertes = 0;
                    user.not_move = true;
                }
                let (sx, sy) = match team {
                    1 => (39 + (i as i32 % 5), 26 + (i as i32 / 5)),
                    2 => (56 + (i as i32 % 5), 26 + (i as i32 / 5)),
                    3 => (39 + (i as i32 % 5), 69 + (i as i32 / 5)),
                    _ => (56 + (i as i32 % 5), 69 + (i as i32 / 5)),
                };
                warp_user(state, pid, BAT_MISTICA_MAP, sx, sy).await;
            }
            state.bat_kills = [0; 5];
            state.evento_timer = BAT_MISTICA_DURATION;
        }
        EVENTO_FACCIONARIO => {
            let half = participants.len() / 2;
            for (i, &pid) in participants.iter().enumerate() {
                let team = if i < half { 1 } else { 2 }; // 1=Alianza, 2=Horda
                if let Some(user) = state.users.get_mut(&pid) {
                    user.mapa_anterior = user.pos_map;
                    user.x_anterior = user.pos_x;
                    user.y_anterior = user.pos_y;
                    user.en_evento = true;
                    user.evento_tipo = tipo;
                    user.evento_equipo = team;
                    user.evento_muertes = 0;
                    user.not_move = true;
                }
                let (sx, sy) = if team == 1 {
                    (34 + (i as i32 % 4) * 2, 64 + (i as i32 / 4) * 2)
                } else {
                    (70 + ((i - half) as i32 % 4), 18 + ((i - half) as i32 / 4) * 2)
                };
                warp_user(state, pid, map, sx, sy).await;
            }
            state.evento_timer = FACC_DURATION;
        }
        EVENTO_GUERRA => {
            let half = participants.len() / 2;
            for (i, &pid) in participants.iter().enumerate() {
                let team = if i < half { 1 } else { 2 };
                if let Some(user) = state.users.get_mut(&pid) {
                    user.mapa_anterior = user.pos_map;
                    user.x_anterior = user.pos_x;
                    user.y_anterior = user.pos_y;
                    user.en_evento = true;
                    user.evento_tipo = tipo;
                    user.evento_equipo = team;
                    user.evento_muertes = 0;
                    user.not_move = true;
                }
                let (sx, sy) = if team == 1 {
                    (30 + (i as i32 % 5) * 2, 50)
                } else {
                    (65 + ((i - half) as i32 % 5) * 2, 50)
                };
                warp_user(state, pid, GUERRA_MAP, sx, sy).await;
            }
            state.evento_timer = 600;
        }
        _ => {
            // Generic: FFA warp
            for (i, &pid) in participants.iter().enumerate() {
                if let Some(user) = state.users.get_mut(&pid) {
                    user.mapa_anterior = user.pos_map;
                    user.x_anterior = user.pos_x;
                    user.y_anterior = user.pos_y;
                    user.en_evento = true;
                    user.evento_tipo = tipo;
                    user.not_move = true;
                }
                let sx = 30 + (i as i32 % 10) * 4;
                let sy = 30 + (i as i32 / 10) * 4;
                warp_user(state, pid, map, sx, sy).await;
            }
            state.evento_timer = 300;
        }
    }

    let tipo_name = event_type_name(tipo);
    let msg = format!("{}El evento {} ha comenzado!{}", server_opcodes::CONSOLE_MSG, tipo_name, font_types::INFO);
    state.send_data(SendTarget::ToAll, &msg).await;

    info!("[EVENT] {} started with {} players on map {}", tipo_name, participants.len(), map);
}

/// Tick the event timers (called every 1s).
pub async fn tick_eventos(state: &mut GameState) {
    // Automatic broadcast message (/LMSG)
    if state.auto_msg_active {
        state.auto_msg_counter += 1;
        if state.auto_msg_counter >= state.auto_msg_interval * 60 {
            state.auto_msg_counter = 0;
            let msg = format!("N|{}{}", state.auto_msg_text, font_types::SYSTEM);
            state.send_data(SendTarget::ToAll, &msg).await;
        }
    }

    // Handle signup timeout
    if state.evento_inscripciones {
        state.evento_timer -= 1;
        if state.evento_timer <= 0 {
            if state.evento_participantes.len() >= 2 {
                evento_begin(state).await;
            } else {
                // Cancel — not enough players, refund
                let cost = state.evento_costo;
                let participants = state.evento_participantes.clone();
                for &pid in &participants {
                    if let Some(user) = state.users.get_mut(&pid) {
                        user.gold += cost;
                    }
                    send_stats_gold(state, pid).await;
                    state.send_to(pid, &format!("{}Evento cancelado por falta de participantes. Oro devuelto.{}", server_opcodes::CONSOLE_MSG, font_types::INFO)).await;
                }
                evento_reset(state);
                return;
            }
        }
        return;
    }

    if !state.evento_activo { return; }

    // Handle pre-start countdown
    if state.evento_countdown > 0 {
        state.evento_countdown -= 1;
        if state.evento_countdown == 0 {
            // Unlock movement
            let participants = state.evento_participantes.clone();
            for &pid in &participants {
                if let Some(user) = state.users.get_mut(&pid) {
                    user.not_move = false;
                }
            }
            let msg = format!("{}Comiencen!{}", server_opcodes::CONSOLE_MSG, font_types::INFO);
            for &pid in &participants {
                state.send_to(pid, &msg).await;
            }
        } else {
            let msg = format!("{}El evento comienza en {}...{}", server_opcodes::CONSOLE_MSG, state.evento_countdown, font_types::INFO);
            let participants = state.evento_participantes.clone();
            for &pid in &participants {
                state.send_to(pid, &msg).await;
            }
        }
        return;
    }

    // Decrement event timer
    state.evento_timer -= 1;

    // Handle respawn timers for dead players
    let participants = state.evento_participantes.clone();
    for &pid in &participants {
        let seconds = state.users.get(&pid).map(|u| u.evento_seconds).unwrap_or(0);
        if seconds > 0 {
            if let Some(user) = state.users.get_mut(&pid) {
                user.evento_seconds -= 1;
                if user.evento_seconds <= 0 {
                    // Respawn player at team base
                    user.dead = false;
                    user.min_hp = user.max_hp;
                }
            }
            if state.users.get(&pid).map(|u| u.evento_seconds).unwrap_or(0) <= 0 {
                evento_respawn_player(state, pid).await;
            }
        }
    }

    // Time expired — resolve
    if state.evento_timer <= 0 {
        evento_finalize(state).await;
    }
}

/// Handle a player death during an event.
pub async fn evento_player_death(state: &mut GameState, conn_id: ConnectionId, killer_id: ConnectionId) {
    let tipo = state.evento_tipo;

    // Increment death count and set respawn timer
    if let Some(user) = state.users.get_mut(&conn_id) {
        user.evento_muertes += 1;
        user.evento_seconds = user.evento_muertes * 2; // Escalating respawn delay
    }

    match tipo {
        EVENTO_JDH => {
            // Elimination — remove from event
            evento_remove_player(state, conn_id).await;

            // Check if only one remains
            let remaining: Vec<ConnectionId> = state.evento_participantes.iter()
                .filter(|&&pid| state.users.get(&pid).map(|u| u.en_evento && !u.dead).unwrap_or(false))
                .copied()
                .collect();

            if remaining.len() <= 1 {
                evento_finalize(state).await;
            }
        }
        EVENTO_LUZ => {
            // LUZ doesn't have PvP death — handled via guessing mechanic
        }
        EVENTO_CTF => {
            // Respawn at team base after delay
        }
        EVENTO_BAT_MISTICA => {
            // Count kill for killer's team
            let killer_team = state.users.get(&killer_id).map(|u| u.evento_equipo).unwrap_or(0);
            if killer_team >= 1 && killer_team <= 4 {
                state.bat_kills[killer_team as usize] += 1;
                // Send update to all event participants
                let pkt = format!("BTM1,{},{},{},{}", state.bat_kills[1], state.bat_kills[2], state.bat_kills[3], state.bat_kills[4]);
                let participants = state.evento_participantes.clone();
                for &pid in &participants {
                    state.send_to(pid, &pkt).await;
                }
            }
        }
        EVENTO_ARAM => {
            // ARAM death: escalating respawn
        }
        _ => {}
    }
}

/// Respawn a player at their team base during an event.
pub(super) async fn evento_respawn_player(state: &mut GameState, conn_id: ConnectionId) {
    let (tipo, team, map) = match state.users.get(&conn_id) {
        Some(u) => (u.evento_tipo, u.evento_equipo, state.evento_map),
        None => return,
    };

    let (sx, sy) = match tipo {
        EVENTO_CTF => if team == 1 { (20, 50) } else { (80, 50) },
        EVENTO_ARAM => if team == 1 { (50, 25) } else { (50, 75) },
        EVENTO_BAT_MISTICA => match team {
            1 => (41, 28),
            2 => (58, 28),
            3 => (41, 71),
            _ => (58, 71),
        },
        EVENTO_FACCIONARIO => if team == 1 { (37, 67) } else { (71, 21) },
        EVENTO_GUERRA => if team == 1 { (35, 50) } else { (67, 50) },
        _ => (50, 50),
    };

    // Revive and warp
    if let Some(user) = state.users.get_mut(&conn_id) {
        user.dead = false;
        user.min_hp = user.max_hp;
    }
    warp_user(state, conn_id, map, sx, sy).await;
}

/// Remove a player from the active event (death in elimination, disconnect).
pub(super) async fn evento_remove_player(state: &mut GameState, conn_id: ConnectionId) {
    state.evento_participantes.retain(|&pid| pid != conn_id);

    if let Some(user) = state.users.get_mut(&conn_id) {
        user.en_evento = false;
        user.evento_tipo = 0;
        user.not_move = false;
    }

    // Warp back to saved position
    let (m, x, y) = state.users.get(&conn_id)
        .map(|u| (u.mapa_anterior, u.x_anterior, u.y_anterior))
        .unwrap_or((EXIT_MAP, EXIT_X, EXIT_Y));
    if m > 0 {
        warp_user(state, conn_id, m, x, y).await;
    } else {
        warp_user(state, conn_id, EXIT_MAP, EXIT_X, EXIT_Y).await;
    }
}

/// Finalize the event — determine winner, award prizes, warp out.
pub(super) async fn evento_finalize(state: &mut GameState) {
    let tipo = state.evento_tipo;
    let participants = state.evento_participantes.clone();

    match tipo {
        EVENTO_CTF => {
            let winner_team = if state.ctf_puntos_azul >= CTF_POINTS_TO_WIN { 1 }
                else if state.ctf_puntos_rojo >= CTF_POINTS_TO_WIN { 2 }
                else if state.ctf_puntos_azul > state.ctf_puntos_rojo { 1 }
                else { 2 };
            let team_name = if winner_team == 1 { "Azul" } else { "Rojo" };
            let msg = format!("{}El equipo {} gano la Captura de Bandera!{}", server_opcodes::CONSOLE_MSG, team_name, font_types::INFO);
            state.send_data(SendTarget::ToAll, &msg).await;

            // Award winning team
            for &pid in &participants {
                let team = state.users.get(&pid).map(|u| u.evento_equipo).unwrap_or(0);
                if team == winner_team {
                    if let Some(user) = state.users.get_mut(&pid) {
                        user.reputation += 20;
                    }
                }
            }
        }
        EVENTO_JDH => {
            // Last player standing wins
            let winner = participants.first().copied();
            if let Some(wid) = winner {
                let name = state.users.get(&wid).map(|u| u.char_name.clone()).unwrap_or_default();
                let msg = format!("{}{} es el ultimo superviviente de los Juegos del Hambre!{}", server_opcodes::CONSOLE_MSG, name, font_types::INFO);
                state.send_data(SendTarget::ToAll, &msg).await;
                if let Some(user) = state.users.get_mut(&wid) {
                    user.reputation += 50;
                }
            }
        }
        EVENTO_BAT_MISTICA => {
            // Team with most kills wins
            let mut best_team = 1;
            let mut best_kills = state.bat_kills[1];
            for t in 2..=4 {
                if state.bat_kills[t] > best_kills {
                    best_team = t as i32;
                    best_kills = state.bat_kills[t];
                }
            }
            let team_names = ["", "Azul", "Amarillo", "Rojo", "Verde"];
            let msg = format!("{}El equipo {} gano la Batalla Mistica con {} kills!{}",
                server_opcodes::CONSOLE_MSG, team_names[best_team as usize], best_kills, font_types::INFO);
            state.send_data(SendTarget::ToAll, &msg).await;

            for &pid in &participants {
                let team = state.users.get(&pid).map(|u| u.evento_equipo).unwrap_or(0);
                if team == best_team {
                    if let Some(user) = state.users.get_mut(&pid) {
                        user.reputation += 20;
                    }
                }
            }
        }
        EVENTO_ARAM => {
            // Check which tower survived
            let blue_alive = state.get_npc(state.aram_torre_azul).map(|n| n.is_alive()).unwrap_or(false);
            let red_alive = state.get_npc(state.aram_torre_roja).map(|n| n.is_alive()).unwrap_or(false);
            let winner_team = if blue_alive && !red_alive { 1 }
                else if !blue_alive && red_alive { 2 }
                else { 0 }; // Draw

            if winner_team > 0 {
                let team_name = if winner_team == 1 { "Azul" } else { "Rojo" };
                let msg = format!("{}El equipo {} gano el ARAM!{}", server_opcodes::CONSOLE_MSG, team_name, font_types::INFO);
                state.send_data(SendTarget::ToAll, &msg).await;
                for &pid in &participants {
                    let team = state.users.get(&pid).map(|u| u.evento_equipo).unwrap_or(0);
                    if team == winner_team {
                        if let Some(user) = state.users.get_mut(&pid) {
                            user.reputation += 20;
                        }
                    }
                }
            } else {
                state.send_data(SendTarget::ToAll, &format!("{}El ARAM termino en empate!{}", server_opcodes::CONSOLE_MSG, font_types::INFO)).await;
            }

            // Kill tower NPCs
            state.kill_npc(state.aram_torre_azul);
            state.kill_npc(state.aram_torre_roja);
        }
        EVENTO_FACCIONARIO | EVENTO_GUERRA => {
            let msg = format!("{}El evento ha terminado!{}", server_opcodes::CONSOLE_MSG, font_types::INFO);
            state.send_data(SendTarget::ToAll, &msg).await;
        }
        _ => {
            state.send_data(SendTarget::ToAll, &format!("{}El evento ha terminado!{}", server_opcodes::CONSOLE_MSG, font_types::INFO)).await;
        }
    }

    // Warp all participants back
    for &pid in &participants {
        if let Some(user) = state.users.get_mut(&pid) {
            user.en_evento = false;
            user.evento_tipo = 0;
            user.not_move = false;
            user.dead = false;
            user.min_hp = user.max_hp;
        }
        let (m, x, y) = state.users.get(&pid)
            .map(|u| (u.mapa_anterior, u.x_anterior, u.y_anterior))
            .unwrap_or((EXIT_MAP, EXIT_X, EXIT_Y));
        if m > 0 {
            warp_user(state, pid, m, x, y).await;
        } else {
            warp_user(state, pid, EXIT_MAP, EXIT_X, EXIT_Y).await;
        }
    }

    evento_reset(state);
    info!("[EVENT] {} finalized", event_type_name(tipo));
}

/// Reset all event state.
pub(super) fn evento_reset(state: &mut GameState) {
    state.evento_activo = false;
    state.evento_inscripciones = false;
    state.evento_tipo = 0;
    state.evento_participantes.clear();
    state.evento_timer = 0;
    state.evento_countdown = 0;
    state.ctf_puntos_azul = 0;
    state.ctf_puntos_rojo = 0;
    state.bat_kills = [0; 5];
    state.aram_torre_azul = 0;
    state.aram_torre_roja = 0;
}

/// Get human-readable event type name.
pub(super) fn event_type_name(tipo: i32) -> &'static str {
    match tipo {
        EVENTO_CTF => "Captura de Bandera",
        EVENTO_JDH => "Juegos del Hambre",
        EVENTO_LUZ => "Evento Luz",
        EVENTO_ARAM => "ARAM",
        EVENTO_BAT_MISTICA => "Batalla Mistica",
        EVENTO_FACCIONARIO => "Evento Faccionario",
        EVENTO_TORNEO_AUTO => "Torneo Automatico",
        EVENTO_GUERRA => "Guerra",
        _ => "Evento",
    }
}

/// CTF: Score a point for a team.
pub fn ctf_score_point(state: &mut GameState, team: i32) {
    match team {
        1 => state.ctf_puntos_azul += 1,
        2 => state.ctf_puntos_rojo += 1,
        _ => {}
    }
}

/// ARAM: Check if a tower NPC death ends the event.
pub async fn aram_check_tower_death(state: &mut GameState, npc_idx: usize) {
    if state.evento_tipo != EVENTO_ARAM || !state.evento_activo { return; }

    if npc_idx == state.aram_torre_azul || npc_idx == state.aram_torre_roja {
        evento_finalize(state).await;
    }
}

// ---- Torneo Automatico (single-elimination bracket) ----

/// GM: Start automatic tournament with N rounds.
pub(super) async fn handle_gm_torneo_auto(state: &mut GameState, conn_id: ConnectionId, rounds: i32) {
    let priv_level = state.users.get(&conn_id).map(|u| u.privileges).unwrap_or(0);
    if priv_level < privilege_level::EVENT_MASTER { return; }

    if state.torneo_auto_activo {
        state.send_to(conn_id, &format!("{}Ya hay un torneo automatico activo.{}", server_opcodes::CONSOLE_MSG, font_types::INFO)).await;
        return;
    }

    let max_players = 1 << rounds; // 2^rounds
    state.torneo_auto_activo = true;
    state.torneo_auto_rondas = rounds;
    state.torneo_auto_ronda_actual = 1;
    state.torneo_auto_bracket = Vec::with_capacity(max_players as usize);
    state.torneo_auto_timer = 60; // 60s signup

    let msg = format!("{}Torneo automatico abierto! {} slots. Usa /TORNEO para inscribirte.{}",
        server_opcodes::CONSOLE_MSG, max_players, font_types::INFO);
    state.send_data(SendTarget::ToAll, &msg).await;

    info!("[TORNEO] Auto tournament started: {} rounds, {} slots", rounds, max_players);
}

/// /TORNEO — Join automatic tournament (redirected here if torneo_auto active).
pub async fn torneo_auto_join(state: &mut GameState, conn_id: ConnectionId) {
    if !state.torneo_auto_activo {
        state.send_to(conn_id, &format!("{}No hay torneo automatico activo.{}", server_opcodes::CONSOLE_MSG, font_types::INFO)).await;
        return;
    }

    let max = 1 << state.torneo_auto_rondas;
    if state.torneo_auto_bracket.len() as i32 >= max {
        state.send_to(conn_id, &format!("{}El torneo esta lleno.{}", server_opcodes::CONSOLE_MSG, font_types::INFO)).await;
        return;
    }

    if state.torneo_auto_bracket.contains(&conn_id) {
        state.send_to(conn_id, &format!("{}Ya estas inscripto.{}", server_opcodes::CONSOLE_MSG, font_types::INFO)).await;
        return;
    }

    state.torneo_auto_bracket.push(conn_id);
    let slot = state.torneo_auto_bracket.len() as i32;

    if let Some(user) = state.users.get_mut(&conn_id) {
        user.torneo_auto = true;
        user.torneo_auto_slot = slot;
        user.torneo_auto_muerto = false;
        user.mapa_anterior = user.pos_map;
        user.x_anterior = user.pos_x;
        user.y_anterior = user.pos_y;
    }

    let msg = format!("{}Inscripto al torneo! Slot #{}/{}{}",
        server_opcodes::CONSOLE_MSG, slot, max, font_types::INFO);
    state.send_to(conn_id, &msg).await;

    // Auto-start when full
    if slot >= max {
        torneo_auto_start_round(state).await;
    }
}

/// Start the next round of fights in the automatic tournament.
pub(super) async fn torneo_auto_start_round(state: &mut GameState) {
    let alive: Vec<ConnectionId> = state.torneo_auto_bracket.iter()
        .filter(|&&pid| state.users.get(&pid).map(|u| u.torneo_auto && !u.torneo_auto_muerto).unwrap_or(false))
        .copied()
        .collect();

    if alive.len() <= 1 {
        // We have a winner
        if let Some(&winner) = alive.first() {
            let name = state.users.get(&winner).map(|u| u.char_name.clone()).unwrap_or_default();
            let msg = format!("{}{} ha ganado el torneo automatico!{}", server_opcodes::CONSOLE_MSG, name, font_types::INFO);
            state.send_data(SendTarget::ToAll, &msg).await;

            if let Some(user) = state.users.get_mut(&winner) {
                user.reputation += 100;
            }
        }

        // End tournament — warp everyone back
        torneo_auto_end(state).await;
        return;
    }

    // Pair up fighters
    let round = state.torneo_auto_ronda_actual;
    let msg = format!("{}Ronda {} del torneo!{}", server_opcodes::CONSOLE_MSG, round, font_types::INFO);

    for &pid in &alive {
        state.send_to(pid, &msg).await;
    }

    // Warp first pair to arena corners
    if alive.len() >= 2 {
        warp_user(state, alive[0], TORNEO_AUTO_MAP, 41, 42).await;
        warp_user(state, alive[1], TORNEO_AUTO_MAP, 60, 57).await;
    }

    // Rest wait in waiting zone
    for i in 2..alive.len() {
        warp_user(state, alive[i], TORNEO_AUTO_MAP, 23 + (i as i32 % 8), 37 + (i as i32 / 8) * 3).await;
    }

    state.torneo_auto_ronda_actual += 1;
}

/// Handle death in automatic tournament.
pub async fn torneo_auto_death(state: &mut GameState, conn_id: ConnectionId) {
    if !state.torneo_auto_activo { return; }

    if let Some(user) = state.users.get_mut(&conn_id) {
        user.torneo_auto_muerto = true;
        user.dead = false;
        user.min_hp = user.max_hp;
    }

    // Warp loser to waiting zone
    warp_user(state, conn_id, TORNEO_AUTO_MAP, 25, 45).await;

    // Check if current match is decided, then start next pair or next round
    let alive_in_ring: Vec<ConnectionId> = state.torneo_auto_bracket.iter()
        .filter(|&&pid| {
            state.users.get(&pid).map(|u| {
                u.torneo_auto && !u.torneo_auto_muerto && u.pos_map == TORNEO_AUTO_MAP
                    && u.pos_x >= 35 && u.pos_x <= 65 && u.pos_y >= 35 && u.pos_y <= 65
            }).unwrap_or(false)
        })
        .copied()
        .collect();

    if alive_in_ring.len() <= 1 {
        // Current match decided — advance to next
        torneo_auto_start_round(state).await;
    }
}

/// End automatic tournament — warp all participants back.
pub(super) async fn torneo_auto_end(state: &mut GameState) {
    let bracket = state.torneo_auto_bracket.clone();
    for &pid in &bracket {
        if let Some(user) = state.users.get_mut(&pid) {
            user.torneo_auto = false;
            user.torneo_auto_muerto = false;
        }
        let (m, x, y) = state.users.get(&pid)
            .map(|u| (u.mapa_anterior, u.x_anterior, u.y_anterior))
            .unwrap_or((EXIT_MAP, EXIT_X, EXIT_Y));
        if m > 0 {
            warp_user(state, pid, m, x, y).await;
        } else {
            warp_user(state, pid, EXIT_MAP, EXIT_X, EXIT_Y).await;
        }
    }

    state.torneo_auto_activo = false;
    state.torneo_auto_bracket.clear();
    state.torneo_auto_rondas = 0;
    state.torneo_auto_ronda_actual = 0;
    state.torneo_auto_timer = 0;

    info!("[TORNEO] Auto tournament ended");
}

// =====================================================================
// IP Security (SecurityIp.bas) — rate limiting + max connections per IP
// =====================================================================

/// Check if a new connection from this IP should be accepted.
/// Returns false if rate-limited or max connections exceeded.
pub fn ip_security_accept(state: &mut GameState, ip: &str) -> bool {
    let now = std::time::Instant::now();

    // Check rate limit (min interval between connections from same IP)
    if let Some(last) = state.ip_last_connect.get(ip) {
        let elapsed = now.duration_since(*last).as_millis() as u64;
        if elapsed < state.ip_min_interval_ms {
            return false;
        }
    }
    state.ip_last_connect.insert(ip.to_string(), now);

    // Check max connections per IP
    let count = state.ip_connection_count.entry(ip.to_string()).or_insert(0);
    if *count >= state.ip_max_connections {
        return false;
    }
    *count += 1;

    true
}

// =====================================================================
// NPC Pathfinding (PathFinding.bas) — BFS on map grid
// =====================================================================

/// Maximum pathfinding steps (matches VB6 MAXPASOS = 30).
const PF_MAX_STEPS: usize = 30;

/// BFS pathfinding — find path from (sx,sy) to (tx,ty) on the given map.
/// Returns a Vec of (x,y) positions from start to target (excluding start).
/// Uses 4-directional adjacency on a 100x100 grid.
pub(super) fn pathfind_bfs(state: &GameState, map: i32, sx: i32, sy: i32, tx: i32, ty: i32) -> Vec<(i32, i32)> {
    if sx == tx && sy == ty { return Vec::new(); }
    if sx < 1 || sx > 100 || sy < 1 || sy > 100 { return Vec::new(); }
    if tx < 1 || tx > 100 || ty < 1 || ty > 100 { return Vec::new(); }

    // BFS grid — 0 = unvisited, 1-4 = came-from direction, 5 = start
    let mut visited = vec![vec![0u8; 102]; 102]; // 1-indexed
    let mut queue: std::collections::VecDeque<(i32, i32, usize)> = std::collections::VecDeque::new();

    visited[sy as usize][sx as usize] = 5; // Mark start
    queue.push_back((sx, sy, 0));

    // Directions: 1=north(y-1), 2=east(x+1), 3=south(y+1), 4=west(x-1)
    let dirs: [(i32, i32, u8); 4] = [
        (0, -1, 1), // north
        (1, 0, 2),  // east
        (0, 1, 3),  // south
        (-1, 0, 4), // west
    ];

    let mut found = false;

    while let Some((cx, cy, depth)) = queue.pop_front() {
        if depth >= PF_MAX_STEPS { continue; }
        if cx == tx && cy == ty { found = true; break; }

        for &(dx, dy, dir_code) in &dirs {
            let nx = cx + dx;
            let ny = cy + dy;
            if nx < 1 || nx > 100 || ny < 1 || ny > 100 { continue; }
            if visited[ny as usize][nx as usize] != 0 { continue; }

            // Check if tile is walkable (not blocked, no NPC)
            if state.is_tile_blocked(map, nx, ny) { continue; }
            let has_npc = state.world.grid(map)
                .and_then(|g| g.tile(nx, ny))
                .map(|t| t.npc_index > 0)
                .unwrap_or(false);
            if has_npc && !(nx == tx && ny == ty) { continue; }

            visited[ny as usize][nx as usize] = dir_code;
            queue.push_back((nx, ny, depth + 1));
        }
    }

    if !found { return Vec::new(); }

    // Reconstruct path by backtracking from target
    let mut path = Vec::new();
    let mut cx = tx;
    let mut cy = ty;

    while !(cx == sx && cy == sy) {
        path.push((cx, cy));
        let dir = visited[cy as usize][cx as usize];
        match dir {
            1 => cy += 1,  // came from south (reverse of north)
            2 => cx -= 1,  // came from west (reverse of east)
            3 => cy -= 1,  // came from north (reverse of south)
            4 => cx += 1,  // came from east (reverse of west)
            _ => break,    // start or error
        }
        if path.len() > PF_MAX_STEPS { break; }
    }

    path.reverse();
    path
}

/// Compute heading from step direction for NPC movement.
pub(super) fn heading_from_step(cx: i32, cy: i32, nx: i32, ny: i32) -> i32 {
    let dx = nx - cx;
    let dy = ny - cy;
    if dy < 0 { world::HEADING_NORTH }
    else if dy > 0 { world::HEADING_SOUTH }
    else if dx > 0 { world::HEADING_EAST }
    else if dx < 0 { world::HEADING_WEST }
    else { world::HEADING_SOUTH }
}

/// Execute one pathfinding step for an NPC — moves to next step in path.
/// Returns (moved, optional ghost push data).
pub(super) fn npc_pathfind_step(state: &mut GameState, npc_idx: usize) -> (bool, Option<super::npcs::GhostPush>) {
    let (step, path_len, _map, x, y) = match state.get_npc(npc_idx) {
        Some(n) => (n.pf_step, n.pf_path.len(), n.map, n.x, n.y),
        None => return (false, None),
    };

    if step >= path_len { return (false, None); }

    let (nx, ny) = match state.get_npc(npc_idx) {
        Some(n) => n.pf_path[step],
        None => return (false, None),
    };

    let heading = heading_from_step(x, y, nx, ny);

    // Try to move NPC in this direction
    let (moved, ghost) = move_npc(state, npc_idx, heading);
    if moved {
        if let Some(n) = state.get_npc_mut(npc_idx) {
            n.pf_step += 1;
        }
        (true, ghost)
    } else {
        // Path blocked — clear path to force recompute
        if let Some(n) = state.get_npc_mut(npc_idx) {
            n.pf_path.clear();
            n.pf_step = 0;
        }
        (false, ghost)
    }
}

// =====================================================================
// Castle Siege (modSiege.bas) — guild vs guild castle warfare
// =====================================================================

const SIEGE_MAP: i32 = 151;
const SIEGE_DURATION_TICKS: i32 = 1800; // 30 minutes at 1 tick/sec

/// /CASTILLOS — Show siege info.
pub(super) async fn handle_slash_castillos(state: &mut GameState, conn_id: ConnectionId) {
    if state.siege_active {
        let owner = state.siege_guild_owner;
        let attacker = state.siege_guild_attacker;
        let msg = format!("{}Asedio activo: Clan {} vs Clan {}. Puntos: {}/{}/{}{}",
            server_opcodes::CONSOLE_MSG,
            owner, attacker,
            state.siege_conquest[1], state.siege_conquest[2], state.siege_conquest[3],
            font_types::INFO);
        state.send_to(conn_id, &msg).await;
    } else {
        let owner = state.siege_guild_owner;
        if owner > 0 {
            let msg = format!("{}El castillo pertenece al clan {}.{}", server_opcodes::CONSOLE_MSG, owner, font_types::INFO);
            state.send_to(conn_id, &msg).await;
        } else {
            state.send_to(conn_id, &format!("{}El castillo no pertenece a ningun clan.{}", server_opcodes::CONSOLE_MSG, font_types::INFO)).await;
        }
    }
}

/// GM command: Start siege between guild_owner and guild_attacker.
pub(super) async fn handle_gm_start_siege(state: &mut GameState, conn_id: ConnectionId, args: &str) {
    let parts: Vec<&str> = args.split('@').collect();
    let guild_owner: i32 = parts.first().and_then(|s| s.parse().ok()).unwrap_or(0);
    let guild_attacker: i32 = parts.get(1).and_then(|s| s.parse().ok()).unwrap_or(0);

    if guild_owner <= 0 || guild_attacker <= 0 {
        state.send_to(conn_id, &format!("{}Uso: /SIEGE <owner>@<attacker>{}", server_opcodes::CONSOLE_MSG, font_types::INFO)).await;
        return;
    }

    state.siege_active = true;
    state.siege_guild_owner = guild_owner;
    state.siege_guild_attacker = guild_attacker;
    state.siege_conquest = [0; 4];
    state.siege_timer = SIEGE_DURATION_TICKS;

    let msg = format!("{}El asedio al castillo ha comenzado!{}", server_opcodes::CONSOLE_MSG, font_types::INFO);
    state.send_data(SendTarget::ToAll, &msg).await;

    info!("[SIEGE] Started: guild {} vs guild {}", guild_owner, guild_attacker);
}

/// GM command: End siege.
pub(super) async fn handle_gm_end_siege(state: &mut GameState, conn_id: ConnectionId) {
    if !state.siege_active {
        state.send_to(conn_id, &format!("{}No hay asedio activo.{}", server_opcodes::CONSOLE_MSG, font_types::INFO)).await;
        return;
    }

    resolve_siege(state).await;
}

/// Tick the siege timer (called every 1s from player_tick).
pub async fn tick_siege(state: &mut GameState) {
    if !state.siege_active { return; }

    state.siege_timer -= 1;
    if state.siege_timer <= 0 {
        resolve_siege(state).await;
    }
}

/// Resolve the siege — count conquest points and determine winner.
pub(super) async fn resolve_siege(state: &mut GameState) {
    let owner = state.siege_guild_owner;
    let attacker = state.siege_guild_attacker;

    // Count conquest points: how many are held by attacker
    let attacker_points = (1..=3).filter(|&i| state.siege_conquest[i] == attacker).count();
    let owner_points = (1..=3).filter(|&i| state.siege_conquest[i] == owner || state.siege_conquest[i] == 0).count();

    if attacker_points >= 2 {
        // Attacker wins — they become the new owner
        state.siege_guild_owner = attacker;
        let msg = format!("{}El clan atacante ha conquistado el castillo!{}", server_opcodes::CONSOLE_MSG, font_types::INFO);
        state.send_data(SendTarget::ToAll, &msg).await;
        info!("[SIEGE] Attacker guild {} conquered castle from guild {}", attacker, owner);
    } else {
        // Defender holds
        let msg = format!("{}El clan defensor ha mantenido el castillo!{}", server_opcodes::CONSOLE_MSG, font_types::INFO);
        state.send_data(SendTarget::ToAll, &msg).await;
        info!("[SIEGE] Defender guild {} held castle against guild {}", owner, attacker);
    }

    state.siege_active = false;
    state.siege_conquest = [0; 4];
    state.siege_timer = 0;

    // Warp siege participants back (all users on siege map)
    let siege_users: Vec<ConnectionId> = state.users.values()
        .filter(|u| u.logged && u.pos_map == state.siege_map)
        .map(|u| u.conn_id)
        .collect();
    for uid in siege_users {
        let (m, x, y) = state.users.get(&uid)
            .map(|u| (u.mapa_anterior, u.x_anterior, u.y_anterior))
            .unwrap_or((EXIT_MAP, EXIT_X, EXIT_Y));
        if m > 0 {
            warp_user(state, uid, m, x, y).await;
        } else {
            warp_user(state, uid, EXIT_MAP, EXIT_X, EXIT_Y).await;
        }
    }
}

/// War timer tick — runs every 1s. VB6: frmMain.frm lines 819-876.
/// War starts at minute 120, warnings from 122-131, ends at 132.
/// Ancalagon boss timer — called every 1 second from main loop.
/// VB6: modDragon.bas — When dragon is dead, count to 60 minutes.
/// Spawn pre-dragon (937) + 4 guardians (938) at map 123. Pre-dragon death → spawn dragon (936).
pub async fn tick_ancalagon(state: &mut GameState) {
    if state.ancalagon_alive {
        return; // Dragon is alive, nothing to do
    }

    state.ancalagon_seconds += 1;
    if state.ancalagon_seconds < 60 {
        return;
    }
    state.ancalagon_seconds = 0;
    state.ancalagon_minutes += 1;

    if state.ancalagon_minutes >= 60 && !state.ancalagon_pre_dragon {
        // Time to spawn pre-dragon + guardians
        state.ancalagon_minutes = 0;
        state.ancalagon_seconds = 0;

        // Broadcast warning
        state.send_data(SendTarget::ToAll, "||471").await;

        // Spawn pre-dragon (NPC 937) at map 123, (50,18) — VB6: frmMain.frm line 1157-1159
        state.ancalagon_guardians = 0;
        if let Some(idx) = state.spawn_npc(937, 123, 50, 18) {
            state.ancalagon_pre_dragon = true;
            state.ancalagon_pre_dragon_idx = idx;
            // VB6: Npclist(IndexReyAncalagon).Char.AuraA = 3
            if let Some(npc) = state.get_npc_mut(idx) {
                npc.aura = 3;
                let cc = npc.build_cc_packet();
                state.send_data(SendTarget::ToMap(123), &cc).await;
            }
        }

        // Spawn 4 guardians (NPC 938) — VB6 positions from frmMain.frm lines 1136-1150
        let guardian_positions = [(50, 17), (49, 18), (51, 18), (50, 19)];
        for (gx, gy) in &guardian_positions {
            state.spawn_npc(938, 123, *gx, *gy);
        }
    }

}

/// Check if pre-dragon was killed and spawn real dragon at pre-dragon's death position.
/// Called from npc_die when NPC 937 dies. VB6: SpawnNpc(936, MiNPC.Pos, ...)
pub(super) async fn try_spawn_ancalagon_dragon(state: &mut GameState, death_x: i32, death_y: i32) {
    if !state.ancalagon_alive && !state.ancalagon_pre_dragon {
        if let Some(_) = state.spawn_npc(936, 123, death_x, death_y) {
            state.ancalagon_alive = true;
            let msg = "T|¡El Ancalagon ha aparecido!~255~0~0~1~0";
            state.send_data(SendTarget::ToAll, msg).await;
        }
    }
}

pub async fn tick_guerra(state: &mut GameState) {
    state.guerra_seconds += 1;
    if state.guerra_seconds < 60 { return; }
    state.guerra_seconds = 0;
    state.guerra_minutes += 1;

    let mins = state.guerra_minutes;

    if mins == 120 {
        // Start war — pick random location
        // Simple alternation based on system time for pseudo-randomness
        let location = if std::time::SystemTime::now()
            .duration_since(std::time::UNIX_EPOCH)
            .unwrap_or_default().as_secs() % 2 == 0 { 1u32 } else { 2u32 };
        if location == 1 {
            // Anvilmar (map 29)
            state.send_data(SendTarget::ToAll, "||460").await;
            state.hay_guerra_anvil = true;
            if let Some(npc_idx) = state.spawn_npc(947, 29, 78, 45) {
                state.rey_guerra_index = npc_idx;
            }
            state.hay_guerra = true;
        } else {
            // Khalimdar (map 27)
            state.send_data(SendTarget::ToAll, "||461").await;
            state.hay_guerra_khalim = true;
            if let Some(npc_idx) = state.spawn_npc(948, 27, 50, 18) {
                state.rey_guerra_index = npc_idx;
            }
            state.hay_guerra = true;
        }
    }

    if mins >= 122 && mins < 132 && state.hay_guerra {
        let remaining = 132 - mins;
        state.send_data(SendTarget::ToAll, &format!("||462@{}", remaining)).await;
    }

    if mins == 132 && state.hay_guerra {
        // War ends — no winner
        if state.hay_guerra_khalim {
            state.send_data(SendTarget::ToAll, "||463").await;
        } else if state.hay_guerra_anvil {
            state.send_data(SendTarget::ToAll, "||464").await;
        }
        // Remove war king NPC
        if state.rey_guerra_index > 0 {
            state.kill_npc(state.rey_guerra_index);
        }
        state.hay_guerra = false;
        state.hay_guerra_anvil = false;
        state.hay_guerra_khalim = false;
        state.rey_guerra_index = 0;
        state.guerra_minutes = 0;

        // Reset all players' en_guerra flag
        let war_users: Vec<ConnectionId> = state.users.values()
            .filter(|u| u.en_guerra)
            .map(|u| u.conn_id)
            .collect();
        for uid in war_users {
            if let Some(user) = state.users.get_mut(&uid) {
                user.en_guerra = false;
            }
        }
    }
}

/// Capture a conquest point in the siege (called when a guild member stands on a point tile).
pub fn siege_capture_point(state: &mut GameState, point: usize, guild_index: i32) {
    if !state.siege_active || point < 1 || point > 3 { return; }
    state.siege_conquest[point] = guild_index;
}

// =====================================================================
// Praetorian System (praetorians.bas) — faction fortress NPCs
// =====================================================================

/// Praetorian NPC numbers (900-904 in VB6)
const PRETORIANO_MAGO: usize = 900;
const PRETORIANO_CLERIGO: usize = 901;
const PRETORIANO_GUERRERO: usize = 902;
const PRETORIANO_CAZADOR: usize = 903;
const PRETORIANO_REY: usize = 904;

const MAX_PRETORIANOS_CLAN: usize = 8;

/// Create a praetorian clan at a given location.
pub(super) async fn crear_clan_pretoriano(state: &mut GameState, map: i32, x: i32, y: i32, faccion: i32) {
    // Clear existing clan
    limpiar_clan_pretoriano(state).await;

    state.pretoriano_faccion = faccion;
    state.pretoriano_activo = true;
    state.pretoriano_alcoba = 0;

    // Spawn 8 praetorians in a formation around (x,y)
    let positions = [
        (x - 2, y - 2), (x + 2, y - 2),
        (x - 2, y + 2), (x + 2, y + 2),
        (x - 1, y), (x + 1, y),
        (x, y - 1), (x, y + 1),
    ];

    let npc_types = [
        PRETORIANO_GUERRERO, PRETORIANO_GUERRERO,
        PRETORIANO_CAZADOR, PRETORIANO_CAZADOR,
        PRETORIANO_MAGO, PRETORIANO_MAGO,
        PRETORIANO_CLERIGO, PRETORIANO_REY,
    ];

    for i in 0..MAX_PRETORIANOS_CLAN {
        let (px, py) = positions[i];
        if px >= 1 && px <= 100 && py >= 1 && py <= 100 {
            if let Some(npc_idx) = state.spawn_npc(npc_types[i], map, px, py) {
                state.pretoriano_clan.push(npc_idx);

                // Broadcast CC
                if let Some(npc) = state.get_npc(npc_idx) {
                    let cc_pkt = npc.build_cc_packet();
                    state.send_data(SendTarget::ToArea { map, x: px, y: py }, &cc_pkt).await;
                }
            }
        }
    }

    info!("[PRET] Created praetorian clan on map {} at ({},{}) faction {}", map, x, y, faccion);
}

/// Remove all praetorian NPCs.
pub(super) async fn limpiar_clan_pretoriano(state: &mut GameState) {
    let clan_indices: Vec<usize> = state.pretoriano_clan.drain(..).collect();
    for npc_idx in clan_indices {
        if let Some(npc) = state.get_npc(npc_idx) {
            let bp_pkt = format!("BP{}", npc.char_index.0);
            let (map, x, y) = (npc.map, npc.x, npc.y);
            state.send_data(SendTarget::ToArea { map, x, y }, &bp_pkt).await;
        }
        state.kill_npc(npc_idx);
    }
    state.pretoriano_activo = false;
    state.pretoriano_alcoba = 0;
}

/// Check if an NPC number is a praetorian type.
pub(super) fn es_pretoriano(npc_number: usize) -> bool {
    npc_number >= 900 && npc_number <= 904
}

/// Handle praetorian death — check if clan is wiped.
pub fn pretoriano_check_death(state: &mut GameState, npc_idx: usize) {
    if !state.pretoriano_activo { return; }

    // Remove from clan list
    state.pretoriano_clan.retain(|&idx| idx != npc_idx);

    // Check if king (REY) is dead
    let rey_alive = state.pretoriano_clan.iter().any(|&idx| {
        state.get_npc(idx)
            .filter(|n| n.is_alive() && n.npc_number == PRETORIANO_REY)
            .is_some()
    });

    if !rey_alive {
        // All praetorians defeated or king killed — advance alcoba
        state.pretoriano_alcoba += 1;
        if state.pretoriano_alcoba >= 4 {
            // Fortress conquered!
            state.pretoriano_activo = false;
            info!("[PRET] Fortress conquered! Faction {}", state.pretoriano_faccion);
        } else {
            info!("[PRET] Alcoba {} cleared, advancing", state.pretoriano_alcoba);
        }
    }
}

// =====================================================================
