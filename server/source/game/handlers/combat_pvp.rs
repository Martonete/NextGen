//! PvP-specific combat helpers: armor absorption for player vs player.
//! Extracted from combat.rs.

use super::super::common::rand_range;
use crate::game::constants::BODY_PART_HEAD;
use crate::game::types::{GameState, MAX_INVENTORY_SLOTS};
use crate::net::ConnectionId;

// =====================================================================
// VB6 13.3: Criminal status recalculation
// =====================================================================

/// VB6 13.3: Recalculate criminal status from 6 reputation fields.
/// L = (-asesino - bandido + burgues - ladrones + noble + plebe) / 6
/// criminal = L < 0
pub(super) fn recalc_criminal(state: &mut GameState, conn_id: ConnectionId) {
    if let Some(user) = state.users.get_mut(&conn_id) {
        let l = (-user.rep_asesino - user.rep_bandido + user.rep_burgues - user.rep_ladrones
            + user.rep_noble
            + user.rep_plebe)
            / 6;
        user.criminal = l < 0;
    }
}

// =====================================================================
// PvP armor absorption (VB6: UserDañoUser)
// =====================================================================

/// VB6: PvP armor absorption — separate from NPC combat.
/// Head hits use helmet only, body hits use armor + shield.
/// Returns (head_defense, body_defense).
pub(super) fn calc_pvp_armor_absorption(
    state: &GameState,
    victim_id: ConnectionId,
    lugar: i32,
) -> (i32, i32) {
    let user = match state.users.get(&victim_id) {
        Some(u) => u,
        None => return (0, 0),
    };

    match lugar {
        BODY_PART_HEAD => {
            // Helmet absorbs head hits
            let helmet_def = if user.equip.helmet > 0 && user.equip.helmet <= MAX_INVENTORY_SLOTS {
                let obj_idx = user.inventory[user.equip.helmet - 1].obj_index;
                match state.get_object(obj_idx) {
                    Some(obj) if obj.min_def > 0 || obj.max_def > 0 => {
                        rand_range(obj.min_def.max(0), obj.max_def.max(1))
                    }
                    _ => 0,
                }
            } else {
                0
            };
            (helmet_def, 0)
        }
        _ => {
            // Body hits — armor + shield defense combined
            let mut min_def = 0i32;
            let mut max_def = 0i32;

            // Armor
            if user.equip.armor > 0 && user.equip.armor <= MAX_INVENTORY_SLOTS {
                let obj_idx = user.inventory[user.equip.armor - 1].obj_index;
                if let Some(obj) = state.get_object(obj_idx) {
                    min_def += obj.min_def;
                    max_def += obj.max_def;
                }
            }

            // Shield (also absorbs body hits in VB6)
            if user.equip.shield > 0 && user.equip.shield <= MAX_INVENTORY_SLOTS {
                let obj_idx = user.inventory[user.equip.shield - 1].obj_index;
                if let Some(obj) = state.get_object(obj_idx) {
                    min_def += obj.min_def;
                    max_def += obj.max_def;
                }
            }

            let body_def = if max_def > 0 {
                rand_range(min_def.max(0), max_def.max(1))
            } else {
                0
            };
            (0, body_def)
        }
    }
}
