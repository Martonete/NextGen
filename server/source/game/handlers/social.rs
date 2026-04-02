//! Social system handlers: chat (talk/yell/whisper), factions, utility slash commands.
//! Extracted from mod.rs to reduce file size.

use crate::net::ConnectionId;
use crate::game::types::{GameState, SendTarget, InventorySlot, MAX_INVENTORY_SLOTS};
use crate::protocol::font_index;
use crate::data::balance;
use super::common::*;
use super::{send_inventory_slot, handle_slash_command};
use crate::protocol::binary_packets;

/// VB6: GiveFactionArmours — give 3 tiers of faction armor based on class+race+faction+rank.
async fn give_faction_armours(state: &mut GameState, conn_id: ConnectionId, is_caos: bool) {
    let (class, race, rango) = match state.users.get(&conn_id) {
        Some(u) => {
            let r = if is_caos { u.recompensas_caos } else { u.recompensas_real };
            (u.class, u.race, r + 1) // VB6: Rango = RecompensasX + 1
        }
        None => return,
    };
    let armors = state.game_data.balance.get_faction_armor_e(class, race);

    for tier in 0..3 {
        let obj_index = if is_caos { armors.caos[tier] } else { armors.armada[tier] };
        if obj_index <= 0 { continue; }
        let amount = balance::faction_armor_amount(rango, tier);
        if amount <= 0 { continue; }

        // Try to add to inventory (stack or empty slot within current_inventory_slots)
        let added = if let Some(user) = state.users.get_mut(&conn_id) {
            let max_slots = user.current_inventory_slots;
            // First try stacking
            if let Some(slot_idx) = user.inventory.iter().position(|s| s.obj_index == obj_index && !s.equipped) {
                user.inventory[slot_idx].amount += amount;
                Some(slot_idx)
            } else if let Some(slot_idx) = user.inventory.iter().take(max_slots).position(|s| s.obj_index == 0) {
                user.inventory[slot_idx] = InventorySlot { obj_index, amount, equipped: false };
                Some(slot_idx)
            } else {
                None
            }
        } else {
            None
        };

        if let Some(slot_idx) = added {
            super::send_inventory_slot(state, conn_id, slot_idx).await;
        } else {
            // No inventory space — VB6: drop on floor at player's position
            let (map, x, y) = match state.users.get(&conn_id) {
                Some(u) => (u.pos_map, u.pos_x, u.pos_y),
                None => continue,
            };
            let grh_index = state.get_object(obj_index).map(|o| o.grh_index).unwrap_or(0);
            let grid = state.world.grid_mut(map);
            let placed = if let Some(tile) = grid.tile_mut(x, y) {
                if tile.ground_item.obj_index == 0 || tile.ground_item.obj_index == obj_index {
                    if tile.ground_item.obj_index == obj_index {
                        tile.ground_item.amount += amount;
                    } else {
                        tile.ground_item.obj_index = obj_index;
                        tile.ground_item.amount = amount;
                    }
                    true
                } else {
                    false
                }
            } else {
                false
            };
            if placed && grh_index > 0 {
                let pkt = binary_packets::write_object_create(x as i16, y as i16, grh_index as i16);
                state.send_data_bytes(SendTarget::ToArea { map, x, y }, &pkt);
                clean_world_add_item(state, map, x, y, 10, obj_index);
            }
        }
    }
}


// =====================================================================
// Faction system handlers (ModFacciones.bas)
// =====================================================================

/// Kill thresholds for faction tiers
const FACTION_TIER_THRESHOLDS: [i32; 4] = [50, 100, 200, 350];

/// /ENLISTAR — Join a faction (Royal Army or Chaos Forces).
/// VB6: requires NPC target (type 5), distance <= 4, not dead.
pub(super) async fn handle_slash_enlistar(state: &mut GameState, conn_id: ConnectionId) {
    let (target_npc, dead) = match state.users.get(&conn_id) {
        Some(u) if u.logged => (u.target_npc, u.dead),
        _ => return,
    };

    // VB6: If TargetNPC = 0 Then ||9
    if target_npc == 0 {
        state.send_msg_id(conn_id, 9, "");
        return;
    }

    // VB6: NPCtype must be 5 (faction officer) AND not dead
    let npc_type_num = state.get_npc(target_npc).map(|n| n.npc_type as i32).unwrap_or(0);
    if npc_type_num != 5 || dead { return; }

    // VB6: distance > 4 → ||158
    let (npc_map, npc_x, npc_y) = match state.get_npc(target_npc) {
        Some(npc) => (npc.map, npc.x, npc.y),
        None => return,
    };
    let (u_map, u_x, u_y) = match state.users.get(&conn_id) {
        Some(u) => (u.pos_map, u.pos_x, u.pos_y),
        None => return,
    };
    if u_map != npc_map || (u_x - npc_x).abs() > 4 || (u_y - npc_y).abs() > 4 {
        state.send_msg_id(conn_id, 158, "");
        return;
    }

    let user_data = match state.users.get(&conn_id) {
        Some(u) if u.logged => (
            u.armada_real, u.fuerzas_caos, u.criminal,
            u.criminales_matados, u.ciudadanos_matados,
            u.reenlistadas, u.char_name.clone(),
            u.guild_index,
        ),
        _ => return,
    };
    let (armada, caos, criminal, crim_killed, ciud_killed, reenlistadas, char_name, _guild_index) = user_data;

    if armada || caos {
        state.send_console(conn_id, "Ya perteneces a una faccion.", font_index::INFO);
        return;
    }

    if reenlistadas {
        state.send_console(conn_id, "Ya no puedes enlistarte nuevamente.", font_index::INFO);
        return;
    }

    if !criminal {
        // Try to join Royal Army
        if crim_killed < 50 {
            state.send_console(conn_id, "Necesitas haber matado al menos 50 criminales.", font_index::INFO);
            return;
        }

        if let Some(user) = state.users.get_mut(&conn_id) {
            user.armada_real = true;
        }

        // Award initial XP (50000)
        if let Some(user) = state.users.get_mut(&conn_id) {
            user.exp += 50000;
        }
        send_stats_exp(state, conn_id).await;

        // VB6: GiveFactionArmours on enlistment
        give_faction_armours(state, conn_id, false).await;

        state.send_console(conn_id, "Te has enlistado en la Armada Real!", font_index::GUILD_MSG);

        // Broadcast
        state.send_msg_id_to(SendTarget::ToAll, 851, &char_name);
    } else {
        // Try to join Chaos Forces
        if ciud_killed < 50 {
            state.send_console(conn_id, "Necesitas haber matado al menos 50 ciudadanos.", font_index::INFO);
            return;
        }

        if let Some(user) = state.users.get_mut(&conn_id) {
            user.fuerzas_caos = true;
        }

        // Award initial XP (50000)
        if let Some(user) = state.users.get_mut(&conn_id) {
            user.exp += 50000;
        }
        send_stats_exp(state, conn_id).await;

        // VB6: GiveFactionArmours on enlistment
        give_faction_armours(state, conn_id, true).await;

        state.send_console(conn_id, "Te has enlistado en las Fuerzas del Caos!", font_index::GUILD_MSG);

        // Broadcast
        state.send_msg_id_to(SendTarget::ToAll, 852, &char_name);
    }
}

/// /INFORMACION — Display faction status.
pub(super) async fn handle_slash_faction_info(state: &mut GameState, conn_id: ConnectionId) {
    let user_data = match state.users.get(&conn_id) {
        Some(u) if u.logged => (
            u.armada_real, u.fuerzas_caos,
            u.criminales_matados, u.ciudadanos_matados,
            u.recompensas_real, u.recompensas_caos,
        ),
        _ => return,
    };
    let (armada, caos, crim_killed, ciud_killed, rec_real, rec_caos) = user_data;

    if armada {
        state.send_console(conn_id, "--- Armada Real ---", font_index::GUILD_MSG);
        state.send_console(conn_id, &format!("Criminales matados: {}", crim_killed), font_index::INFO);
        state.send_console(conn_id, &format!("Rango: {}", faction_rank_name(rec_real, true)), font_index::INFO);
    } else if caos {
        state.send_console(conn_id, "--- Fuerzas del Caos ---", font_index::GUILD_MSG);
        state.send_console(conn_id, &format!("Ciudadanos matados: {}", ciud_killed), font_index::INFO);
        state.send_console(conn_id, &format!("Rango: {}", faction_rank_name(rec_caos, false)), font_index::INFO);
    } else {
        state.send_console(conn_id, "No perteneces a ninguna faccion.", font_index::INFO);
        state.send_console(conn_id, &format!("Criminales matados: {}", crim_killed), font_index::INFO);
        state.send_console(conn_id, &format!("Ciudadanos matados: {}", ciud_killed), font_index::INFO);
    }
}

/// Get faction rank name
pub(super) fn faction_rank_name(tier: i32, is_royal: bool) -> &'static str {
    if is_royal {
        match tier {
            0 => "Recluta",
            1 => "Soldado Imperial",
            2 => "Capitan Imperial",
            3 => "Comandante Imperial",
            4 => "General Imperial",
            _ => "Caballero de la Luz",
        }
    } else {
        match tier {
            0 => "Recluta Oscuro",
            1 => "Soldado del Caos",
            2 => "Capitan del Caos",
            3 => "Comandante del Caos",
            4 => "General del Caos",
            _ => "Caballero de las Sombras",
        }
    }
}

/// /RECOMPENSA — Claim faction tier reward. VB6: requires NPC type 5, distance <= 4.
pub(super) async fn handle_slash_recompensa(state: &mut GameState, conn_id: ConnectionId) {
    let (target_npc, dead) = match state.users.get(&conn_id) {
        Some(u) if u.logged => (u.target_npc, u.dead),
        _ => return,
    };

    // VB6: If TargetNPC = 0 Then ||9
    if target_npc == 0 {
        state.send_msg_id(conn_id, 9, "");
        return;
    }

    // VB6: NPCtype must be 5, not dead
    let npc_type_num = state.get_npc(target_npc).map(|n| n.npc_type as i32).unwrap_or(0);
    if npc_type_num != 5 || dead { return; }

    // VB6: distance > 4 → ||12
    let (npc_map, npc_x, npc_y) = match state.get_npc(target_npc) {
        Some(npc) => (npc.map, npc.x, npc.y),
        None => return,
    };
    let (u_map, u_x, u_y) = match state.users.get(&conn_id) {
        Some(u) => (u.pos_map, u.pos_x, u.pos_y),
        None => return,
    };
    if u_map != npc_map || (u_x - npc_x).abs() > 4 || (u_y - npc_y).abs() > 4 {
        state.send_msg_id(conn_id, 12, "");
        return;
    }

    let user_data = match state.users.get(&conn_id) {
        Some(u) if u.logged => (
            u.armada_real, u.fuerzas_caos,
            u.criminales_matados, u.ciudadanos_matados,
            u.recompensas_real, u.recompensas_caos,
            u.char_name.clone(),
        ),
        _ => return,
    };
    let (armada, caos, crim_killed, ciud_killed, rec_real, rec_caos, _char_name) = user_data;

    if !armada && !caos {
        state.send_console(conn_id, "No perteneces a ninguna faccion.", font_index::INFO);
        return;
    }

    if armada {
        let current_tier = rec_real;
        if current_tier >= 4 {
            state.send_console(conn_id, "Ya has alcanzado el rango maximo.", font_index::INFO);
            return;
        }

        let needed = FACTION_TIER_THRESHOLDS[current_tier as usize];
        if crim_killed < needed {
            state.send_console(conn_id, &format!("Necesitas {} criminales matados para el siguiente rango (tienes {}).", needed, crim_killed), font_index::INFO);
            return;
        }

        // Advance tier
        let new_tier = current_tier + 1;
        if let Some(user) = state.users.get_mut(&conn_id) {
            user.recompensas_real = new_tier;
        }

        // VB6: GiveFactionArmours + GiveExpReward on rank-up
        give_faction_armours(state, conn_id, false).await;

        state.send_console(conn_id, &format!("Has ascendido al rango: {}!", faction_rank_name(new_tier, true)), font_index::GUILD_MSG);
    } else {
        let current_tier = rec_caos;
        if current_tier >= 4 {
            state.send_console(conn_id, "Ya has alcanzado el rango maximo.", font_index::INFO);
            return;
        }

        let needed = FACTION_TIER_THRESHOLDS[current_tier as usize];
        if ciud_killed < needed {
            state.send_console(conn_id, &format!("Necesitas {} ciudadanos matados para el siguiente rango (tienes {}).", needed, ciud_killed), font_index::INFO);
            return;
        }

        let new_tier = current_tier + 1;
        if let Some(user) = state.users.get_mut(&conn_id) {
            user.recompensas_caos = new_tier;
        }

        // VB6: GiveFactionArmours + GiveExpReward on rank-up
        give_faction_armours(state, conn_id, true).await;

        state.send_console(conn_id, &format!("Has ascendido al rango: {}!", faction_rank_name(new_tier, false)), font_index::GUILD_MSG);
    }
}

/// /RENUNCIA — Leave faction.
pub(super) async fn handle_slash_renunciar(state: &mut GameState, conn_id: ConnectionId) {
    let user_data = match state.users.get(&conn_id) {
        Some(u) if u.logged => (
            u.armada_real, u.fuerzas_caos,
            u.char_name.clone(), u.guild_index,
        ),
        _ => return,
    };
    let (armada, caos, _char_name, guild_index) = user_data;

    if !armada && !caos {
        state.send_console(conn_id, "No perteneces a ninguna faccion.", font_index::INFO);
        return;
    }

    // Cannot leave faction while in a guild
    if guild_index > 0 {
        state.send_msg_id(conn_id, 302, "");
        return;
    }

    if let Some(user) = state.users.get_mut(&conn_id) {
        user.armada_real = false;
        user.fuerzas_caos = false;
        user.reenlistadas = true; // Can never re-enlist
    }

    state.send_console(conn_id, "Has renunciado a tu faccion.", font_index::INFO);
}

/// /DESERTAR — Desert from faction. VB6: ExpulsarFaccionReal/ExpulsarFaccionCaos.
/// Resets faction flag, unequips faction armor/shield, removes faction armors from inventory.
pub(super) async fn handle_slash_desertar(state: &mut GameState, conn_id: ConnectionId) {
    let (armada, caos) = match state.users.get(&conn_id) {
        Some(u) if u.logged => (u.armada_real, u.fuerzas_caos),
        _ => return,
    };

    if !armada && !caos {
        state.send_console(conn_id, "No perteneces a ninguna faccion.", font_index::INFO);
        return;
    }

    let is_real = armada;

    // Unequip faction armor if equipped (VB6: ObjData(ArmourEqpObjIndex).Real/Caos = 1)
    let armor_slot = state.users.get(&conn_id).map(|u| u.equip.armor).unwrap_or(0);
    if armor_slot > 0 && armor_slot <= MAX_INVENTORY_SLOTS {
        let obj_idx = state.users.get(&conn_id).map(|u| u.inventory[armor_slot - 1].obj_index).unwrap_or(0);
        if obj_idx > 0 {
            let is_faction = state.game_data.objects.get(obj_idx as usize)
                .map(|o| if is_real { o.real } else { o.caos })
                .unwrap_or(false);
            if is_faction {
                if let Some(user) = state.users.get_mut(&conn_id) {
                    user.inventory[armor_slot - 1].equipped = false;
                    user.equip.armor = 0;
                }
                send_inventory_slot(state, conn_id, armor_slot - 1).await;
            }
        }
    }

    // Unequip faction shield if equipped (VB6: ObjData(EscudoEqpObjIndex).Real/Caos = 1)
    let shield_slot = state.users.get(&conn_id).map(|u| u.equip.shield).unwrap_or(0);
    if shield_slot > 0 && shield_slot <= MAX_INVENTORY_SLOTS {
        let obj_idx = state.users.get(&conn_id).map(|u| u.inventory[shield_slot - 1].obj_index).unwrap_or(0);
        if obj_idx > 0 {
            let is_faction = state.game_data.objects.get(obj_idx as usize)
                .map(|o| if is_real { o.real } else { o.caos })
                .unwrap_or(false);
            if is_faction {
                if let Some(user) = state.users.get_mut(&conn_id) {
                    user.inventory[shield_slot - 1].equipped = false;
                    user.equip.shield = 0;
                    user.shield_anim = 0;
                }
                send_inventory_slot(state, conn_id, shield_slot - 1).await;
            }
        }
    }

    // Remove all faction armors from inventory
    let mut slots_to_clear = Vec::new();
    if let Some(user) = state.users.get(&conn_id) {
        for i in 0..MAX_INVENTORY_SLOTS {
            let obj_idx = user.inventory[i].obj_index;
            if obj_idx <= 0 { continue; }
            let is_faction = state.game_data.objects.get(obj_idx as usize)
                .map(|o| if is_real { o.real } else { o.caos })
                .unwrap_or(false);
            if is_faction {
                slots_to_clear.push(i);
            }
        }
    }
    for slot_idx in &slots_to_clear {
        if let Some(user) = state.users.get_mut(&conn_id) {
            user.inventory[*slot_idx] = InventorySlot::default();
        }
        send_inventory_slot(state, conn_id, *slot_idx).await;
    }

    // Reset faction state
    if let Some(user) = state.users.get_mut(&conn_id) {
        if is_real {
            user.armada_real = false;
            user.criminales_matados = 0;
            user.recompensas_real = 0;
        } else {
            user.fuerzas_caos = false;
            user.ciudadanos_matados = 0;
            user.recompensas_caos = 0;
        }
        user.reenlistadas = true; // Cannot re-enlist
    }

    if is_real {
        state.send_console(conn_id, "Has desertado de la Armada Real.", font_index::FIGHT);
    } else {
        state.send_console(conn_id, "Has desertado de las Fuerzas del Caos.", font_index::FIGHT);
    }
}

// =====================================================================
// Utility slash command handlers
// =====================================================================

/// /ONLINE — Show online player count.
pub(super) async fn handle_slash_online(state: &mut GameState, conn_id: ConnectionId) {
    let count = state.num_users;
    let record = state.record_users;
    state.send_console(conn_id, &format!("Jugadores online: {}. Record: {}.", count, record), font_index::INFO);
}

/// /BALANCE — Show gold and bank gold.
pub(super) async fn handle_slash_balance(state: &mut GameState, conn_id: ConnectionId) {
    let (gold, bank_gold) = match state.users.get(&conn_id) {
        Some(u) if u.logged => (u.gold, u.bank_gold),
        _ => return,
    };
    state.send_console(conn_id, &format!("Oro: {}. En banco: {}. Total: {}.", gold, bank_gold, gold + bank_gold), font_index::INFO);
}

/// /GLOBAL <text> — Send global chat message.
pub(super) async fn handle_slash_global(state: &mut GameState, conn_id: ConnectionId, text: &str) {
    let (char_name, priv_level) = match state.users.get(&conn_id) {
        Some(u) if u.logged => (u.char_name.clone(), u.privileges),
        _ => return,
    };

    // VB6: If ChatGlobal == False and user is not staff → blocked
    if !state.chat_global && priv_level == 0 {
        state.send_msg_id(conn_id, 549, "");
        return;
    }

    if text.contains('~') { return; }

    state.send_guild_chat_to(SendTarget::ToAll, &format!("{}> {}", char_name, text));
}

/// /STATS or /EST — Show character stats summary.
pub(super) async fn handle_slash_stats(state: &mut GameState, conn_id: ConnectionId) {
    let u = match state.users.get(&conn_id) {
        Some(u) if u.logged => u,
        _ => return,
    };

    let char_name = u.char_name.clone();
    let class = u.class;
    let race = u.race;
    let level = u.level;
    let (min_hp, max_hp) = (u.min_hp, u.max_hp);
    let (min_mana, max_mana) = (u.min_mana, u.max_mana);
    let (min_sta, max_sta) = (u.min_sta, u.max_sta);
    let attrs = u.attributes.clone();
    let gold = u.gold;
    let exp = u.exp;

    state.send_console(conn_id, &format!("--- Estadisticas de {} ---", char_name), font_index::GUILD_MSG);
    state.send_console(conn_id, &format!("Clase: {} | Raza: {} | Nivel: {}", class, race, level), font_index::INFO);
    state.send_console(conn_id, &format!("HP: {}/{} | Mana: {}/{} | STA: {}/{}", min_hp, max_hp, min_mana, max_mana, min_sta, max_sta), font_index::INFO);
    state.send_console(conn_id, &format!("Fuerza: {} | Agilidad: {} | Inteligencia: {}", attrs[0], attrs[1], attrs[2]), font_index::INFO);
    state.send_console(conn_id, &format!("Carisma: {} | Constitucion: {}", attrs[3], attrs[4]), font_index::INFO);
    state.send_console(conn_id, &format!("Oro: {} | EXP: {}", gold, exp), font_index::INFO);
}

// =====================================================================
// Chat handlers (talk, yell, whisper)
// =====================================================================

/// ; — Chat message (talk to area).
/// VB6 format: T|<color>~<message>~<charindex>
/// Client parses with ReadField(N, rData, 176) for dialog bubble,
/// or ReadField(N, rData, 126) for console text.
pub(super) async fn handle_talk(state: &mut GameState, conn_id: ConnectionId, message: &str) {
    let user_data = match state.users.get(&conn_id) {
        Some(u) if u.logged => (u.pos_map, u.pos_x, u.pos_y, u.char_index, u.dead, u.privileges, u.silenced),
        _ => return,
    };
    let (map, x, y, char_index, dead, privileges, silenced) = user_data;

    // Handle slash commands (e.g., /RESUCITAR) — silenced users can still use commands
    // VB6: empty messages are allowed (used for "cartelear" — clearing text bubbles)
    if !message.is_empty() && message.starts_with('/') {
        handle_slash_command(state, conn_id, message).await;
        return;
    }

    // Silenced users can't chat
    if silenced {
        state.send_msg_id(conn_id, 191, ""); // TEXTO191: Has sido silenciado
        return;
    }

    // Color based on status
    let color: i32 = if dead {
        12632256 // Gray for dead
    } else if privileges > 0 {
        65535 // Yellow for GM (vbYellow)
    } else {
        16777215 // White (vbWhite)
    };

    // VB6 13.3: dead players' chat (ToDeadArea) is only visible to other dead players nearby
    if dead {
        let pkt = crate::protocol::binary_packets::write_chat_over_head(message, char_index.0 as i16, color as i32);
        let area_users = state.get_area_users(map, x, y, conn_id);
        for other_id in area_users {
            if state.users.get(&other_id).map(|u| u.dead).unwrap_or(false) {
                state.send_bytes(other_id, &pkt);
            }
        }
        state.send_bytes(conn_id, &pkt);
    } else {
        state.send_chat_talk_to(SendTarget::ToArea { map, x, y }, char_index.0 as i16, message, color);
    }
}

/// - — Yell message (larger area, red text).
/// VB6 format: N|<color>~<message>~<charindex>
pub(super) async fn handle_yell(state: &mut GameState, conn_id: ConnectionId, message: &str) {
    let user_data = match state.users.get(&conn_id) {
        Some(u) if u.logged => (u.pos_map, u.pos_x, u.pos_y, u.char_index, u.dead),
        _ => return,
    };
    let (map, x, y, char_index, dead) = user_data;

    if dead {
        return; // Dead players can't yell
    }

    if message.is_empty() {
        return;
    }

    // VB6 13.3 parity: yell goes to ToPCArea (standard vision range), not entire map
    state.send_chat_over_head_to(SendTarget::ToArea { map, x, y }, message, char_index.0 as i16, 255);
}

/// \ — Whisper (private message).
/// Client sends: \<targetname>@<message>
/// Server sends P| packets to both sender and receiver.
pub(super) async fn handle_whisper(state: &mut GameState, conn_id: ConnectionId, target_name: &str, message: &str) {
    let user_data = match state.users.get(&conn_id) {
        Some(u) if u.logged => (u.char_name.clone(), u.privileges),
        _ => return,
    };
    let (sender_name, privileges) = user_data;

    if target_name.is_empty() || message.is_empty() {
        return;
    }

    // Find target user
    let target_id = state.find_user_by_name(target_name);

    if target_id.is_none() {
        // User not found — send console message
        state.send_msg_id(conn_id, 196, "");
        return;
    }
    let target_id = target_id.unwrap();

    // Get target's display name
    let target_display = state.users.get(&target_id)
        .map(|u| u.char_name.clone())
        .unwrap_or_else(|| target_name.to_string());

    // Send to sender: "Le dijiste a <target>: <message>" (binary whisper)
    state.send_whisper(conn_id, &format!("Le dijiste a {}: {}", target_display, message), font_index::WHISPER_SENT);

    // Send to receiver (binary whisper)
    let prefix = if privileges > 0 { "(GM) " } else { "" };
    state.send_whisper(target_id, &format!("{}{} te dijo: {}", prefix, sender_name, message), font_index::WHISPER_RECV);
}
