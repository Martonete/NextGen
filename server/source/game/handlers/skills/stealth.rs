//! Stealth skills: stealing, hiding.

use crate::net::ConnectionId;
use crate::game::class_race::PlayerClass;
use crate::game::types::{GameState, UserState, SendTarget, InventorySlot, MAX_INVENTORY_SLOTS};
use crate::game::world;
use crate::protocol::{font_index, binary_packets};
use crate::data::objects::ObjType;
use crate::game::handlers::common::*;
use crate::game::handlers::{send_inventory_slot, check_user_level};
use crate::game::constants::*;
use super::{skill_id, luck_denominator_lookup, try_level_skill_with_hit};

pub(crate) async fn do_robar(state: &mut GameState, conn_id: ConnectionId, tx: i32, ty: i32) {
    let (map, ux, uy, skill, class, level) = match state.users.get(&conn_id) {
        Some(u) => (u.pos_map, u.pos_x, u.pos_y, u.skills[2], u.class, u.level),
        None => return,
    };

    // Distance check
    if (tx - ux).abs() > 2 || (ty - uy).abs() > 2 {
        return;
    }

    // VB6: 15 stamina cost
    if let Some(u) = state.users.get(&conn_id) {
        if u.min_sta < 15 {
            state.send_msg_id(conn_id, 17, "");
            return;
        }
    }
    if let Some(u) = state.users.get_mut(&conn_id) {
        u.min_sta = u.min_sta - 15;
    }
    send_stats_sta(state, conn_id).await;

    // Find target user on tile
    let target_conn = state.world.grid(map)
        .and_then(|g| g.tile(tx, ty))
        .and_then(|t| t.user_conn);

    let target_conn = match target_conn {
        Some(id) if id != conn_id => id,
        _ => {
            state.send_msg_id(conn_id, 252, "");
            return;
        }
    };

    // VB6: Luck roll using lookup table (not polynomial)
    let suerte = luck_denominator_lookup(skill);
    let roll = random_number(1, suerte);

    if roll < 3 {
        // VB6: 50% chance item theft (Thief class only), 50% gold theft
        let is_ladron = class == PlayerClass::Ladron;
        let steal_item = is_ladron && random_number(1, 2) == 1;

        if steal_item {
            // VB6: RobarObjeto — steal 5-10% of a random item stack
            let mut stolen_something = false;
            if let Some(victim) = state.users.get(&target_conn) {
                // Find a stealable item in victim's inventory
                let mut stealable_slots: Vec<usize> = Vec::new();
                for (i, slot) in victim.inventory.iter().enumerate() {
                    if slot.obj_index > 0 && slot.amount > 0 && !slot.equipped {
                        // Check item isn't newbie/key/special
                        let ok = state.get_object(slot.obj_index)
                            .map(|o| !o.newbie && o.obj_type != ObjType::Key)
                            .unwrap_or(false);
                        if ok {
                            stealable_slots.push(i);
                        }
                    }
                }
                if !stealable_slots.is_empty() {
                    let idx = stealable_slots[random_number(0, stealable_slots.len() as i32 - 1) as usize];
                    let (obj_idx, amount) = (victim.inventory[idx].obj_index, victim.inventory[idx].amount);
                    // VB6: 5-10% of stack, min 1
                    let pct = random_number(5, 10) as f64 / 100.0;
                    let steal_amount = ((amount as f64 * pct).floor() as i32).max(1);

                    // Take from victim
                    if let Some(v) = state.users.get_mut(&target_conn) {
                        let new_amt = v.inventory[idx].amount - steal_amount;
                        if new_amt <= 0 {
                            v.inventory[idx].obj_index = 0;
        v.inventory[idx].amount = 0;
        v.inventory[idx].equipped = false;
                        } else {
                            v.inventory[idx].amount = new_amt;
                        }
                    }
                    send_inventory_slot(state, target_conn, idx).await;

                    // Give to thief
                    let slot = find_or_add_inv_slot(state, conn_id, obj_idx, steal_amount);
                    if let Some(s) = slot {
                        send_inventory_slot(state, conn_id, s).await;
                    }

                    let obj_name = state.get_object(obj_idx).map(|o| o.name.clone()).unwrap_or_default();
                    let victim_name = state.users.get(&target_conn).map(|u| u.char_name.clone()).unwrap_or_default();
                    state.send_console(conn_id, &format!("Has robado {} {} a {}!", steal_amount, obj_name, victim_name), font_index::FIGHT);
                    stolen_something = true;
                }
            }
            if !stolen_something {
                state.send_msg_id(conn_id, 818, "");
            }
        } else {
            // VB6: Gold theft — amount based on level and class
            let has_gloves = state.users.get(&conn_id)
                .map(|u| {
                    if u.equip.ring > 0 && u.equip.ring <= MAX_INVENTORY_SLOTS {
                        u.inventory[u.equip.ring - 1].obj_index == 873 // GUANTE_HURTO
                    } else {
                        false
                    }
                })
                .unwrap_or(false);

            let gold_amount = if is_ladron && has_gloves {
                random_number(level * 50, level * 100) as i64
            } else if is_ladron {
                random_number(level * 25, level * 50) as i64
            } else {
                random_number(1, 100) as i64
            };

            let victim_gold = state.users.get(&target_conn).map(|u| u.gold).unwrap_or(0);
            let stolen = gold_amount.min(victim_gold);

            if stolen > 0 {
                if let Some(victim) = state.users.get_mut(&target_conn) {
                    victim.gold -= stolen;
                }
                if let Some(thief) = state.users.get_mut(&conn_id) {
                    thief.gold = (thief.gold + stolen).min(MAX_GOLD);
                }

                let victim_name = state.users.get(&target_conn).map(|u| u.char_name.clone()).unwrap_or_default();
                state.send_msg_id(conn_id, 816, &format!("{}@{}", stolen, victim_name));

                send_stats_gold(state, conn_id).await;
                send_stats_gold(state, target_conn).await;
            } else {
                state.send_msg_id(conn_id, 818, "");
            }
        }

        // VB6: Notify victim on success
        let thief_name = state.users.get(&conn_id).map(|u| u.char_name.clone()).unwrap_or_default();
        state.send_msg_id(target_conn, 819, &thief_name);

        // VB6: SubirSkill on success
        if let Some(u) = state.users.get_mut(&conn_id) {
            try_level_skill_with_hit(u, 2, true); // Robar = index 2
        }
    } else {
        // Fail — notify victim
        let thief_name = state.users.get(&conn_id).map(|u| u.char_name.clone()).unwrap_or_default();
        state.send_msg_id(conn_id, 818, "");
        state.send_console(target_conn, &format!("{} ha intentado robarte!", thief_name), font_index::FIGHT);

        if let Some(u) = state.users.get_mut(&conn_id) {
            try_level_skill_with_hit(u, 2, false);
        }
    }
}

pub(crate) async fn do_ocultarse(state: &mut GameState, conn_id: ConnectionId) {
    let (map, x, y, skill, class, char_index, already_hidden, is_invisible, navigating) =
        match state.users.get(&conn_id) {
            Some(u) if u.logged && !u.dead => {
                (u.pos_map, u.pos_x, u.pos_y, u.skills.get(7).copied().unwrap_or(0),
                 u.class, u.char_index, u.hidden, u.invisible && !u.admin_invisible,
                 u.navigating)
            }
            _ => return,
        };

    // Blocked on special maps (VB6: OcultarSinEfecto)
    let ocultar_blocked = state.game_data.maps.get(map as usize)
        .and_then(|m| m.as_ref())
        .map(|m| m.info.ocultar_sin_efecto)
        .unwrap_or(false);
    if ocultar_blocked {
        state.send_msg_id(conn_id, 838, "");
        return;
    }

    // VB6: Can't hide while navigating (except pirate)
    if navigating && class != PlayerClass::Pirata {
        state.send_console(conn_id, "No puedes ocultarte navegando.", font_index::INFO);
        return;
    }

    // Using ocultarse while invisible (spell) breaks the spell invisibility.
    // Then the normal hide check runs — success = hidden via skill, failure = fully visible.
    if is_invisible {
        if let Some(user) = state.users.get_mut(&conn_id) {
            user.invisible = false;
            user.hidden = false;
            user.counter_invisible = 0;
            user.counter_oculto = 0;
        }
        // Broadcast visibility restoration to area
        let cc = state.users.get(&conn_id).unwrap().build_cc_binary();
        state.send_data_bytes(SendTarget::ToArea { map, x, y }, &cc);
        let cd = crate::game::handlers::common::build_cd_binary(state.users.get(&conn_id).unwrap());
        state.send_data_bytes(SendTarget::ToArea { map, x, y }, &cd);
        let nover = binary_packets::write_set_invisible(char_index.0 as i16, false, 0);
        state.send_data_bytes(SendTarget::ToMap(map), &nover);
        state.send_bytes(conn_id, &nover);
        // Now fall through to the normal hide check below
    } else if already_hidden {
        // Already hidden (not from spell) — "Ya estás oculto."
        state.send_console(conn_id, "Ya estás oculto.", font_index::INFO);
        return;
    }

    // VB6 DoOcultarse: Suerte formula (polynomial based on skill level)
    let skill_f = skill as f64;
    let suerte = (((0.000002 * skill_f - 0.0002) * skill_f + 0.0064) * skill_f + 0.1124) * 100.0;
    let res = rand_range(1, 100);

    if (res as f64) <= suerte {
        // Success — calculate duration based on skill (VB6 polynomial)
        let remaining = (100 - skill) as f64;
        let mut duration = (-0.000001 * remaining.powi(3))
            + (0.00009229 * remaining.powi(2))
            + (-0.0088 * remaining)
            + 0.9571;
        duration *= state.intervals.oculto as f64;

        // Bandits hide for half time
        let is_bandit = class == PlayerClass::Bandido;
        let counter = if is_bandit { (duration / 2.0) as i32 } else { duration as i32 };

        if let Some(user) = state.users.get_mut(&conn_id) {
            user.hidden = true;
            user.counter_oculto = counter;
        }
        // Send NOVER to make invisible on all clients (clanmates still see us)
        if !navigating {
            let ci = char_index.0 as i16;
            let nover = binary_packets::write_set_invisible(ci, true, 0);
            let bp_remove = binary_packets::write_character_remove(ci);
            let (px, py) = state.users.get(&conn_id).map(|u| (u.pos_x, u.pos_y)).unwrap_or((1, 1));
            let area_users = state.get_area_users(map, px, py, conn_id);
            for other_id in area_users {
                if same_clan(state, conn_id, other_id) {
                    // Clanmate: SetInvisible → they see character semi-transparent
                    state.send_bytes(other_id, &nover);
                } else {
                    // Non-clanmate: remove character entirely
                    state.send_bytes(other_id, &bp_remove);
                }
            }
            // Tell self we're invisible
            state.send_bytes(conn_id, &nover);
        }
        state.send_msg_id(conn_id, 808, ""); // "Te has ocultado."
        if let Some(u) = state.users.get_mut(&conn_id) {
            try_level_skill_with_hit(u, 7, true); // Ocultarse = index 7
        }
    } else {
        // Failure
        state.send_msg_id(conn_id, 809, ""); // "No has logrado ocultarte."
        if let Some(u) = state.users.get_mut(&conn_id) {
            try_level_skill_with_hit(u, 7, false);
        }
    }
}

/// DoPermanecerOculto — Timer-based check: returns true if still hidden.
/// VB6: Trabajo.bas DoPermanecerOculto — decrements TiempoOculto each tick.
/// Hunter with skill>90 + special armor stays hidden indefinitely.
pub(crate) fn check_permanecer_oculto(user: &mut UserState) -> bool {
    if !user.hidden { return false; }

    // Hunter with skill>90 and specific armor stays hidden indefinitely
    let skill = user.skills.get(7).copied().unwrap_or(0);
    if user.class == PlayerClass::Cazador && skill > 90 {
        let armor_obj = if user.equip.armor >= 1 && user.equip.armor <= user.inventory.len() {
            user.inventory[user.equip.armor - 1].obj_index
        } else { 0 };
        if armor_obj == 648 || armor_obj == 360 {
            return true;
        }
    }

    // Decrement timer
    user.counter_oculto -= 1;
    if user.counter_oculto <= 0 {
        user.counter_oculto = 0;
        user.hidden = false;
        return false; // Revealed — timer expired
    }
    true // Still hidden
}

/// Desarmar — Disarm skill (VB6: Trabajo.bas Desarmar).
/// Chance to unequip victim's weapon based on Wresterling skill.
pub(crate) fn try_desarmar(skill: i32) -> bool {
    let suerte = match skill {
        0..=10 => 35,
        11..=20 => 30,
        21..=30 => 28,
        31..=40 => 24,
        41..=50 => 22,
        51..=60 => 20,
        61..=70 => 18,
        71..=80 => 15,
        81..=90 => 10,
        91..=100 => 5,
        _ => 5,
    };
    let res = rand_range(1, suerte);
    res <= 2
}
