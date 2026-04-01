//! NPC combat API: attack/defense/armor helpers used by npcs.rs.
//! Extracted from combat.rs.

use crate::net::ConnectionId;
use crate::game::class_race::PlayerClass;
use crate::game::types::{GameState, MAX_INVENTORY_SLOTS};
use super::super::common::rand_range;
use super::poder_ataque_arma;

// =====================================================================
// NPC combat helpers (used by npcs.rs)
// =====================================================================

/// Calculate attack power with balance class modifier.
pub(crate) fn calc_attack_power_with_balance(skill: i32, agility: i32, level: i32, class_mod: f32) -> f64 {
    poder_ataque_arma(skill, agility, level, class_mod) as f64
}

/// Get armor absorption for NPC combat (unchanged from before).
pub(crate) fn calc_armor_absorption(state: &GameState, conn_id: ConnectionId, body_part: i32) -> i32 {
    let user = match state.users.get(&conn_id) {
        Some(u) => u,
        None => return 0,
    };

    let armor_slot = if body_part == 1 {
        user.equip.helmet
    } else {
        user.equip.armor
    };

    if armor_slot == 0 || armor_slot > MAX_INVENTORY_SLOTS {
        return 0;
    }

    let obj_index = user.inventory[armor_slot - 1].obj_index;
    if obj_index <= 0 {
        return 0;
    }

    match state.get_object(obj_index) {
        Some(obj) if obj.min_def > 0 || obj.max_def > 0 => {
            rand_range(obj.min_def.max(0), obj.max_def.max(1))
        }
        _ => 0,
    }
}

/// Get armor absorption with weapon penetration (for NPC combat).
pub(crate) fn calc_armor_absorption_with_penetration(state: &GameState, conn_id: ConnectionId, body_part: i32, refuerzo: i32) -> i32 {
    let user = match state.users.get(&conn_id) {
        Some(u) => u,
        None => return 0,
    };

    let armor_slot = if body_part == 1 {
        user.equip.helmet
    } else {
        user.equip.armor
    };

    if armor_slot == 0 || armor_slot > MAX_INVENTORY_SLOTS {
        return 0;
    }

    let obj_index = user.inventory[armor_slot - 1].obj_index;
    if obj_index <= 0 {
        return 0;
    }

    let armor_def = match state.get_object(obj_index) {
        Some(obj) if obj.min_def > 0 || obj.max_def > 0 => {
            rand_range(obj.min_def.max(0), obj.max_def.max(1))
        }
        _ => 0,
    };

    let shield_def = if user.equip.shield > 0 && user.equip.shield <= MAX_INVENTORY_SLOTS {
        let shield_idx = user.inventory[user.equip.shield - 1].obj_index;
        match state.get_object(shield_idx) {
            Some(obj) if obj.min_def > 0 || obj.max_def > 0 => {
                rand_range(obj.min_def.max(0), obj.max_def.max(1))
            }
            _ => 0,
        }
    } else {
        0
    };

    (armor_def + shield_def - refuerzo).max(0)
}

/// Class-based damage modifier from balance data.
pub(super) fn class_damage_modifier_from_balance(state: &GameState, class: PlayerClass) -> f64 {
    state.game_data.balance.class_mod_dano_armas_e(class) as f64
}
