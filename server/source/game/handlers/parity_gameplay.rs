//! VB6 13.3 parity features: pet commands, ShareNpc, council messages,
//! MoveBank, centinela, faction alerts, GM commands, player commands.

use tracing::info;
use crate::net::ConnectionId;
use crate::game::types::{GameState, SendTarget, privilege_level, MAX_BANK_SLOTS};
use crate::game::npc::{AI_STATIC, AI_FOLLOW_OWNER};
use crate::protocol::font_index;
use super::{
    remove_pet_from_owner,
    enviar_banco_inv,
};

// =====================================================================
// 1. Pet Commands (VB6: /QUIETO, /ACOMPANAR, /LIBERAR)
// =====================================================================

/// /QUIETO — Pet stays still (set AI to STATIC).
/// VB6: HandlePetStand — validates dead, targetNPC, distance, ownership.
pub(super) async fn handle_slash_quieto(state: &mut GameState, conn_id: ConnectionId) {
    let (dead, target_npc) = match state.users.get(&conn_id) {
        Some(u) if u.logged => (u.dead, u.target_npc),
        _ => return,
    };

    if dead {
        state.send_console(conn_id, "Estas muerto.", font_index::INFO);
        return;
    }

    if target_npc == 0 {
        state.send_console(conn_id, "Primero selecciona una mascota.", font_index::INFO);
        return;
    }

    // Check distance <= 10
    let (npc_map, npc_x, npc_y) = match state.get_npc(target_npc) {
        Some(npc) => (npc.map, npc.x, npc.y),
        None => return,
    };
    let (u_map, u_x, u_y) = match state.users.get(&conn_id) {
        Some(u) => (u.pos_map, u.pos_x, u.pos_y),
        None => return,
    };
    if u_map != npc_map || (u_x - npc_x).abs() > 10 || (u_y - npc_y).abs() > 10 {
        state.send_console(conn_id, "Estas demasiado lejos.", font_index::INFO);
        return;
    }

    // Must be owner
    let is_owner = state.get_npc(target_npc)
        .map(|n| n.maestro_user == Some(conn_id))
        .unwrap_or(false);
    if !is_owner { return; }

    // Set AI to static
    if let Some(Some(npc)) = state.npcs.get_mut(target_npc) {
        npc.movement = AI_STATIC;
    }

    state.send_console(conn_id, "La mascota se queda quieta.", font_index::INFO);
}

/// /ACOMPANAR — Pet follows owner (set AI to FOLLOW_OWNER).
/// VB6: HandlePetFollow — same validation as PetStand.
pub(super) async fn handle_slash_acompanar(state: &mut GameState, conn_id: ConnectionId) {
    let (dead, target_npc) = match state.users.get(&conn_id) {
        Some(u) if u.logged => (u.dead, u.target_npc),
        _ => return,
    };

    if dead {
        state.send_console(conn_id, "Estas muerto.", font_index::INFO);
        return;
    }

    if target_npc == 0 {
        state.send_console(conn_id, "Primero selecciona una mascota.", font_index::INFO);
        return;
    }

    let (npc_map, npc_x, npc_y) = match state.get_npc(target_npc) {
        Some(npc) => (npc.map, npc.x, npc.y),
        None => return,
    };
    let (u_map, u_x, u_y) = match state.users.get(&conn_id) {
        Some(u) => (u.pos_map, u.pos_x, u.pos_y),
        None => return,
    };
    if u_map != npc_map || (u_x - npc_x).abs() > 10 || (u_y - npc_y).abs() > 10 {
        state.send_console(conn_id, "Estas demasiado lejos.", font_index::INFO);
        return;
    }

    let is_owner = state.get_npc(target_npc)
        .map(|n| n.maestro_user == Some(conn_id))
        .unwrap_or(false);
    if !is_owner { return; }

    // Set AI to follow owner
    if let Some(Some(npc)) = state.npcs.get_mut(target_npc) {
        npc.movement = AI_FOLLOW_OWNER;
    }

    state.send_console(conn_id, "La mascota te sigue.", font_index::INFO);
}

/// /LIBERAR (pet version) — Release/dismiss a specific pet.
/// VB6: HandleReleasePet — removes the targeted pet from owner's pet list.
/// NOTE: The existing /LIBERAR dispatch in mod.rs calls handle_slash_liberar (GM command).
/// This is the player-side pet release, dispatched via /LIBERARMASCOTA.
pub(super) async fn handle_slash_liberarmascota(state: &mut GameState, conn_id: ConnectionId) {
    let (dead, target_npc) = match state.users.get(&conn_id) {
        Some(u) if u.logged => (u.dead, u.target_npc),
        _ => return,
    };

    if dead {
        state.send_console(conn_id, "Estas muerto.", font_index::INFO);
        return;
    }

    if target_npc == 0 {
        state.send_console(conn_id, "Primero selecciona una mascota.", font_index::INFO);
        return;
    }

    let is_owner = state.get_npc(target_npc)
        .map(|n| n.maestro_user == Some(conn_id))
        .unwrap_or(false);
    if !is_owner {
        state.send_console(conn_id, "Esa no es tu mascota.", font_index::INFO);
        return;
    }

    let (npc_map, npc_x, npc_y) = match state.get_npc(target_npc) {
        Some(npc) => (npc.map, npc.x, npc.y),
        None => return,
    };
    let (u_map, u_x, u_y) = match state.users.get(&conn_id) {
        Some(u) => (u.pos_map, u.pos_x, u.pos_y),
        None => return,
    };
    if u_map != npc_map || (u_x - npc_x).abs() > 10 || (u_y - npc_y).abs() > 10 {
        state.send_console(conn_id, "Estas demasiado lejos.", font_index::INFO);
        return;
    }

    // Remove pet
    remove_pet_from_owner(state, conn_id, target_npc);

    // Kill the NPC (despawn)
    if let Some(Some(npc)) = state.npcs.get_mut(target_npc) {
        npc.min_hp = 0;
        npc.active = false;
    }
    state.active_npc_indices.remove(&target_npc);

    state.send_console(conn_id, "Has liberado a tu mascota.", font_index::INFO);
}

// =====================================================================
// 2. ShareNpc (VB6: /COMPARTIR, /NOCOMPARTIR)
// =====================================================================

/// /COMPARTIR — Share owned NPCs with target player.
/// VB6: HandleShareNpc — faction-restricted sharing.
pub(super) async fn handle_slash_compartir(state: &mut GameState, conn_id: ConnectionId) {
    let (target_user, my_criminal, _my_armada, my_caos, my_name) = match state.users.get(&conn_id) {
        Some(u) if u.logged => (u.target_user, u.criminal, u.armada_real, u.fuerzas_caos, u.char_name.clone()),
        _ => return,
    };

    if target_user == 0 {
        state.send_console(conn_id, "Primero selecciona un jugador.", font_index::INFO);
        return;
    }

    if target_user == conn_id {
        return;
    }

    // Can't share with GMs
    let target_is_gm = state.users.get(&target_user)
        .map(|u| u.privileges > privilege_level::USER)
        .unwrap_or(false);
    if target_is_gm {
        state.send_console(conn_id, "No puedes compartir NPCs con administradores.", font_index::INFO);
        return;
    }

    let (target_criminal, target_caos) = match state.users.get(&target_user) {
        Some(u) if u.logged => (u.criminal, u.fuerzas_caos),
        _ => return,
    };

    // VB6 faction restrictions
    if my_criminal {
        if my_caos {
            if !target_caos {
                state.send_console(conn_id, "Solo puedes compartir NPCs con miembros de tu misma faccion.", font_index::INFO);
                return;
            }
        } else {
            // PKs don't share
            return;
        }
    } else {
        if target_criminal {
            state.send_console(conn_id, "No puedes compartir NPCs con criminales.", font_index::INFO);
            return;
        }
    }

    // Check if already sharing with same target
    let current_share = state.users.get(&conn_id).map(|u| u.share_npc_with).unwrap_or(0);
    if current_share == target_user { return; }

    // Notify previous share partner
    if current_share != 0 {
        let prev_name = state.users.get(&current_share).map(|u| u.char_name.clone()).unwrap_or_default();
        state.send_console(current_share, &format!("{} ha dejado de compartir sus NPCs contigo.", my_name), font_index::INFO);
        state.send_console(conn_id, &format!("Has dejado de compartir tus NPCs con {}.", prev_name), font_index::INFO);
    }

    // Set new share target
    if let Some(u) = state.users.get_mut(&conn_id) {
        u.share_npc_with = target_user;
    }

    let target_name = state.users.get(&target_user).map(|u| u.char_name.clone()).unwrap_or_default();
    state.send_console(target_user, &format!("{} ahora comparte sus NPCs contigo.", my_name), font_index::INFO);
    state.send_console(conn_id, &format!("Ahora compartes tus NPCs con {}.", target_name), font_index::INFO);
}

/// /NOCOMPARTIR — Stop sharing NPCs.
/// VB6: HandleStopSharingNpc.
pub(super) async fn handle_slash_nocompartir(state: &mut GameState, conn_id: ConnectionId) {
    let (share_with, my_name) = match state.users.get(&conn_id) {
        Some(u) if u.logged => (u.share_npc_with, u.char_name.clone()),
        _ => return,
    };

    if share_with != 0 {
        let partner_name = state.users.get(&share_with).map(|u| u.char_name.clone()).unwrap_or_default();
        state.send_console(share_with, &format!("{} ha dejado de compartir sus NPCs contigo.", my_name), font_index::INFO);
        state.send_console(conn_id, &format!("Has dejado de compartir tus NPCs con {}.", partner_name), font_index::INFO);

        if let Some(u) = state.users.get_mut(&conn_id) {
            u.share_npc_with = 0;
        }
    }
}


// =====================================================================
// 4. ConsultasPopulares (Public Polls — VB6: /ENCUESTA, /VOTO)
// =====================================================================

/// /ENCUESTA <question>@<opt1>@<opt2>@... — Create poll (GM only).
pub(super) async fn handle_slash_encuesta_crear(state: &mut GameState, conn_id: ConnectionId, args: &str) {
    let priv_level = state.users.get(&conn_id).map(|u| u.privileges).unwrap_or(0);
    if priv_level < privilege_level::DIOS {
        state.send_console(conn_id, "No tenes permisos.", font_index::INFO);
        return;
    }

    let parts: Vec<&str> = args.split('@').collect();
    if parts.len() < 3 {
        state.send_console(conn_id, "Uso: /ENCUESTA pregunta@opcion1@opcion2@...", font_index::INFO);
        return;
    }

    state.poll_active = true;
    state.poll_voters.clear();
    state.poll_votes = [0; 5];

    for i in 0..5 {
        if i + 1 < parts.len() {
            state.poll_options[i] = parts[i + 1].trim().to_string();
        } else {
            state.poll_options[i].clear();
        }
    }

    let admin_name = state.users.get(&conn_id).map(|u| u.char_name.clone()).unwrap_or_default();
    let msg = format!("ENCUESTA PUBLICA: {} (Usa /VOTAR para ver opciones, /VOTO <numero> para votar)", parts[0]);
    state.send_console_to(SendTarget::ToAll, &msg, font_index::GUILD);
    info!("[GM] {} created poll: {}", admin_name, parts[0]);
}

/// /CERRARENCUESTA — Close active poll (GM only).
pub(super) async fn handle_slash_cerrar_encuesta(state: &mut GameState, conn_id: ConnectionId) {
    let priv_level = state.users.get(&conn_id).map(|u| u.privileges).unwrap_or(0);
    if priv_level < privilege_level::DIOS { return; }

    state.poll_active = false;
    state.send_console_to(SendTarget::ToAll, "La encuesta ha sido cerrada.", font_index::GUILD);
}

// =====================================================================
// 5. Council Messages (VB6: /CONSEJO, /CONSEJOCAOS)
// =====================================================================

/// /CONSEJO <msg> — Send message to Royal Army council members only.
/// VB6: HandleCouncilMessage — checks PlayerType.RoyalCouncil flag.
pub(super) async fn handle_slash_consejo(state: &mut GameState, conn_id: ConnectionId, text: &str) {
    let (_is_royal, name) = match state.users.get(&conn_id) {
        Some(u) if u.logged && u.royal_council => (true, u.char_name.clone()),
        _ => {
            state.send_console(conn_id, "No eres miembro del consejo real.", font_index::INFO);
            return;
        }
    };

    if text.is_empty() { return; }

    let msg = format!("(Consejero) {}> {}", name, text);

    // Send to all royal council members
    let targets: Vec<ConnectionId> = state.users.values()
        .filter(|u| u.logged && u.royal_council)
        .map(|u| u.conn_id)
        .collect();
    for t in targets {
        state.send_console(t, &msg, font_index::CONSEJO);
    }
}

/// /CONSEJOCAOS <msg> — Send message to Chaos council members only.
pub(super) async fn handle_slash_consejocaos(state: &mut GameState, conn_id: ConnectionId, text: &str) {
    let (_is_chaos, name) = match state.users.get(&conn_id) {
        Some(u) if u.logged && u.chaos_council => (true, u.char_name.clone()),
        _ => {
            state.send_console(conn_id, "No eres miembro del consejo del caos.", font_index::INFO);
            return;
        }
    };

    if text.is_empty() { return; }

    let msg = format!("(Consejero) {}> {}", name, text);

    let targets: Vec<ConnectionId> = state.users.values()
        .filter(|u| u.logged && u.chaos_council)
        .map(|u| u.conn_id)
        .collect();
    for t in targets {
        state.send_console(t, &msg, font_index::CONSEJO_CAOS);
    }
}

// =====================================================================
// 6. MoveBank (VB6: HandleMoveBank — swap bank slot positions)
// =====================================================================

/// /MOVEBANK <dir> <slot> — Move bank item up or down.
/// VB6: dir=1 moves up (swap with slot-1), dir=-1 moves down (swap with slot+1).
pub(super) async fn handle_slash_movebank(state: &mut GameState, conn_id: ConnectionId, args: &str) {
    let parts: Vec<&str> = args.split_whitespace().collect();
    if parts.len() < 2 {
        return;
    }

    let dir: i32 = parts[0].parse().unwrap_or(0); // 1=up, -1=down
    let slot: usize = parts[1].parse().unwrap_or(0);

    if slot == 0 || slot > MAX_BANK_SLOTS { return; }

    let slot_idx = slot - 1;

    if let Some(user) = state.users.get_mut(&conn_id) {
        if dir == 1 && slot_idx > 0 {
            // Swap with previous slot
            user.bank.swap(slot_idx, slot_idx - 1);
        } else if dir == -1 && slot_idx + 1 < user.bank.len() {
            // Swap with next slot
            user.bank.swap(slot_idx, slot_idx + 1);
        }
    }

    // Re-send bank inventory
    enviar_banco_inv(state, conn_id).await;
}

