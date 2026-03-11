//! VB6 13.3 parity features: pet commands, ShareNpc, council messages,
//! MoveBank, centinela, faction alerts, GM commands, player commands.

use tracing::info;
use crate::net::ConnectionId;
use crate::game::types::{GameState, SendTarget, privilege_level, MAX_BANK_SLOTS};
use crate::game::npc::{AI_STATIC, AI_FOLLOW_OWNER};
use crate::protocol::{font_index, binary_packets};
use super::common::*;
use super::{
    remove_pet_from_owner, warp_user, send_full_inventory,
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
        state.send_console(conn_id, "Estas muerto.", font_index::INFO).await;
        return;
    }

    if target_npc == 0 {
        state.send_console(conn_id, "Primero selecciona una mascota.", font_index::INFO).await;
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
        state.send_console(conn_id, "Estas demasiado lejos.", font_index::INFO).await;
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

    state.send_console(conn_id, "La mascota se queda quieta.", font_index::INFO).await;
}

/// /ACOMPANAR — Pet follows owner (set AI to FOLLOW_OWNER).
/// VB6: HandlePetFollow — same validation as PetStand.
pub(super) async fn handle_slash_acompanar(state: &mut GameState, conn_id: ConnectionId) {
    let (dead, target_npc) = match state.users.get(&conn_id) {
        Some(u) if u.logged => (u.dead, u.target_npc),
        _ => return,
    };

    if dead {
        state.send_console(conn_id, "Estas muerto.", font_index::INFO).await;
        return;
    }

    if target_npc == 0 {
        state.send_console(conn_id, "Primero selecciona una mascota.", font_index::INFO).await;
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
        state.send_console(conn_id, "Estas demasiado lejos.", font_index::INFO).await;
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

    state.send_console(conn_id, "La mascota te sigue.", font_index::INFO).await;
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
        state.send_console(conn_id, "Estas muerto.", font_index::INFO).await;
        return;
    }

    if target_npc == 0 {
        state.send_console(conn_id, "Primero selecciona una mascota.", font_index::INFO).await;
        return;
    }

    let is_owner = state.get_npc(target_npc)
        .map(|n| n.maestro_user == Some(conn_id))
        .unwrap_or(false);
    if !is_owner {
        state.send_console(conn_id, "Esa no es tu mascota.", font_index::INFO).await;
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
        state.send_console(conn_id, "Estas demasiado lejos.", font_index::INFO).await;
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

    state.send_console(conn_id, "Has liberado a tu mascota.", font_index::INFO).await;
}

// =====================================================================
// 2. ShareNpc (VB6: /COMPARTIR, /NOCOMPARTIR)
// =====================================================================

/// /COMPARTIR — Share owned NPCs with target player.
/// VB6: HandleShareNpc — faction-restricted sharing.
pub(super) async fn handle_slash_compartir(state: &mut GameState, conn_id: ConnectionId) {
    let (target_user, my_criminal, my_armada, my_caos, my_name) = match state.users.get(&conn_id) {
        Some(u) if u.logged => (u.target_user, u.criminal, u.armada_real, u.fuerzas_caos, u.char_name.clone()),
        _ => return,
    };

    if target_user == 0 {
        state.send_console(conn_id, "Primero selecciona un jugador.", font_index::INFO).await;
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
        state.send_console(conn_id, "No puedes compartir NPCs con administradores.", font_index::INFO).await;
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
                state.send_console(conn_id, "Solo puedes compartir NPCs con miembros de tu misma faccion.", font_index::INFO).await;
                return;
            }
        } else {
            // PKs don't share
            return;
        }
    } else {
        if target_criminal {
            state.send_console(conn_id, "No puedes compartir NPCs con criminales.", font_index::INFO).await;
            return;
        }
    }

    // Check if already sharing with same target
    let current_share = state.users.get(&conn_id).map(|u| u.share_npc_with).unwrap_or(0);
    if current_share == target_user { return; }

    // Notify previous share partner
    if current_share != 0 {
        let prev_name = state.users.get(&current_share).map(|u| u.char_name.clone()).unwrap_or_default();
        state.send_console(current_share, &format!("{} ha dejado de compartir sus NPCs contigo.", my_name), font_index::INFO).await;
        state.send_console(conn_id, &format!("Has dejado de compartir tus NPCs con {}.", prev_name), font_index::INFO).await;
    }

    // Set new share target
    if let Some(u) = state.users.get_mut(&conn_id) {
        u.share_npc_with = target_user;
    }

    let target_name = state.users.get(&target_user).map(|u| u.char_name.clone()).unwrap_or_default();
    state.send_console(target_user, &format!("{} ahora comparte sus NPCs contigo.", my_name), font_index::INFO).await;
    state.send_console(conn_id, &format!("Ahora compartes tus NPCs con {}.", target_name), font_index::INFO).await;
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
        state.send_console(share_with, &format!("{} ha dejado de compartir sus NPCs contigo.", my_name), font_index::INFO).await;
        state.send_console(conn_id, &format!("Has dejado de compartir tus NPCs con {}.", partner_name), font_index::INFO).await;

        if let Some(u) = state.users.get_mut(&conn_id) {
            u.share_npc_with = 0;
        }
    }
}

// =====================================================================
// 3. Item Upgrade (VB6: DoUpgrade / HandleItemUpgrade)
// =====================================================================

/// /UPGRADE <slot> — Upgrade item in inventory slot.
/// VB6: DoUpgrade — checks ObjData.Upgrade field, materials, skill requirements.
/// Simplified: checks upgrade target exists, removes old item, gives new one.
pub(super) async fn handle_slash_upgrade(state: &mut GameState, conn_id: ConnectionId, slot_str: &str) {
    let slot: usize = slot_str.parse().unwrap_or(0);
    if slot == 0 || slot > 30 {
        state.send_console(conn_id, "Uso: /UPGRADE <slot>", font_index::INFO).await;
        return;
    }

    let slot_idx = slot - 1;

    let (dead, obj_index) = match state.users.get(&conn_id) {
        Some(u) if u.logged => {
            let oi = if slot_idx < u.inventory.len() { u.inventory[slot_idx].obj_index } else { 0 };
            (u.dead, oi)
        }
        _ => return,
    };

    if dead {
        state.send_console(conn_id, "Estas muerto.", font_index::INFO).await;
        return;
    }

    if obj_index <= 0 {
        state.send_console(conn_id, "No hay item en ese slot.", font_index::INFO).await;
        return;
    }

    // Get upgrade target
    let upgrade_target = state.get_object(obj_index).map(|o| o.upgrade).unwrap_or(0);
    if upgrade_target <= 0 {
        state.send_console(conn_id, "Este item no se puede mejorar.", font_index::INFO).await;
        return;
    }

    // Check upgrade target exists
    let upgrade_name = state.get_object(upgrade_target).map(|o| o.name.clone()).unwrap_or_default();
    if upgrade_name.is_empty() {
        state.send_console(conn_id, "Item de mejora no encontrado.", font_index::INFO).await;
        return;
    }

    // Check stamina (VB6: 6 for Worker, 8 for others)
    let (min_sta, class) = match state.users.get(&conn_id) {
        Some(u) => (u.min_sta, u.class),
        None => return,
    };
    let sta_cost = if class == crate::game::class_race::PlayerClass::Trabajador { 6 } else { 8 };
    if min_sta < sta_cost {
        state.send_console(conn_id, "No tienes suficiente energia.", font_index::INFO).await;
        return;
    }

    // VB6: Check materials (LingH, LingP, LingO, Madera, MaderaElfica)
    // Simplified: check upgrade target's material requirements
    let upgrade_obj = state.get_object(upgrade_target).cloned();
    let source_obj = state.get_object(obj_index).cloned();
    if upgrade_obj.is_none() || source_obj.is_none() { return; }

    let upgrade_obj = upgrade_obj.unwrap();
    let source_obj = source_obj.unwrap();

    // Material constants (VB6 item indices)
    const LINGOTE_HIERRO: i32 = 386;
    const LINGOTE_PLATA: i32 = 387;
    const LINGOTE_ORO: i32 = 388;
    const LENA: i32 = 58;
    const LENA_ELFICA: i32 = 1007;
    const PORCENTAJE_MATERIALES: f64 = 0.5; // 50% of materials refunded from source

    // Check materials needed = upgrade - source * 50%
    let needed_iron = (upgrade_obj.ling_h as f64 - source_obj.ling_h as f64 * PORCENTAJE_MATERIALES).max(0.0) as i32;
    let needed_silver = (upgrade_obj.ling_p as f64 - source_obj.ling_p as f64 * PORCENTAJE_MATERIALES).max(0.0) as i32;
    let needed_gold_mat = (upgrade_obj.ling_o as f64 - source_obj.ling_o as f64 * PORCENTAJE_MATERIALES).max(0.0) as i32;
    let needed_wood = (upgrade_obj.madera as f64 - source_obj.madera as f64 * PORCENTAJE_MATERIALES).max(0.0) as i32;

    let mut missing = Vec::new();
    if needed_iron > 0 && !user_has_items(state, conn_id, LINGOTE_HIERRO, needed_iron) {
        missing.push("lingotes de hierro");
    }
    if needed_silver > 0 && !user_has_items(state, conn_id, LINGOTE_PLATA, needed_silver) {
        missing.push("lingotes de plata");
    }
    if needed_gold_mat > 0 && !user_has_items(state, conn_id, LINGOTE_ORO, needed_gold_mat) {
        missing.push("lingotes de oro");
    }
    if needed_wood > 0 && !user_has_items(state, conn_id, LENA, needed_wood) {
        missing.push("madera");
    }
    if !missing.is_empty() {
        state.send_console(conn_id, &format!("No tienes suficientes {}.", missing.join(", ")), font_index::INFO).await;
        return;
    }

    // Deduct stamina
    if let Some(u) = state.users.get_mut(&conn_id) {
        u.min_sta -= sta_cost;
    }
    send_stats_sta(state, conn_id).await;

    // Remove materials
    if needed_iron > 0 { remove_items_from_inv(state, conn_id, LINGOTE_HIERRO, needed_iron).await; }
    if needed_silver > 0 { remove_items_from_inv(state, conn_id, LINGOTE_PLATA, needed_silver).await; }
    if needed_gold_mat > 0 { remove_items_from_inv(state, conn_id, LINGOTE_ORO, needed_gold_mat).await; }
    if needed_wood > 0 { remove_items_from_inv(state, conn_id, LENA, needed_wood).await; }

    // Remove source item
    if let Some(u) = state.users.get_mut(&conn_id) {
        if slot_idx < u.inventory.len() {
            u.inventory[slot_idx].amount -= 1;
            if u.inventory[slot_idx].amount <= 0 {
                u.inventory[slot_idx].obj_index = 0;
                u.inventory[slot_idx].amount = 0;
                u.inventory[slot_idx].equipped = false;
            }
        }
    }

    // Add upgraded item
    let added = add_item_to_user_inventory(state, conn_id, upgrade_target, 1);
    if !added {
        // Drop on floor if inventory full
        state.send_console(conn_id, "Inventario lleno, el item se cayo al piso.", font_index::INFO).await;
    }

    let item_type_name = match source_obj.obj_type {
        crate::data::objects::ObjType::Weapon => "arma",
        crate::data::objects::ObjType::Shield => "escudo",
        crate::data::objects::ObjType::Helmet => "casco",
        crate::data::objects::ObjType::Armor => "armadura",
        _ => "item",
    };
    state.send_console(conn_id, &format!("Has mejorado el {}!", item_type_name), font_index::INFO).await;
    send_full_inventory(state, conn_id).await;

    let name = state.users.get(&conn_id).map(|u| u.char_name.clone()).unwrap_or_default();
    info!("[UPGRADE] {} upgraded {} to {}", name, source_obj.name, upgrade_name);
}

// =====================================================================
// 4. ConsultasPopulares (Public Polls — VB6: /ENCUESTA, /VOTO)
// =====================================================================

/// /ENCUESTA <question>@<opt1>@<opt2>@... — Create poll (GM only).
pub(super) async fn handle_slash_encuesta_crear(state: &mut GameState, conn_id: ConnectionId, args: &str) {
    let priv_level = state.users.get(&conn_id).map(|u| u.privileges).unwrap_or(0);
    if priv_level < privilege_level::DIOS {
        state.send_console(conn_id, "No tenes permisos.", font_index::INFO).await;
        return;
    }

    let parts: Vec<&str> = args.split('@').collect();
    if parts.len() < 3 {
        state.send_console(conn_id, "Uso: /ENCUESTA pregunta@opcion1@opcion2@...", font_index::INFO).await;
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
    state.send_console_to(SendTarget::ToAll, &msg, font_index::GUILD).await;
    info!("[GM] {} created poll: {}", admin_name, parts[0]);
}

/// /CERRARENCUESTA — Close active poll (GM only).
pub(super) async fn handle_slash_cerrar_encuesta(state: &mut GameState, conn_id: ConnectionId) {
    let priv_level = state.users.get(&conn_id).map(|u| u.privileges).unwrap_or(0);
    if priv_level < privilege_level::DIOS { return; }

    state.poll_active = false;
    state.send_console_to(SendTarget::ToAll, "La encuesta ha sido cerrada.", font_index::GUILD).await;
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
            state.send_console(conn_id, "No eres miembro del consejo real.", font_index::INFO).await;
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
        state.send_console(t, &msg, font_index::CONSEJO).await;
    }
}

/// /CONSEJOCAOS <msg> — Send message to Chaos council members only.
pub(super) async fn handle_slash_consejocaos(state: &mut GameState, conn_id: ConnectionId, text: &str) {
    let (_is_chaos, name) = match state.users.get(&conn_id) {
        Some(u) if u.logged && u.chaos_council => (true, u.char_name.clone()),
        _ => {
            state.send_console(conn_id, "No eres miembro del consejo del caos.", font_index::INFO).await;
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
        state.send_console(t, &msg, font_index::CONSEJO_CAOS).await;
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

