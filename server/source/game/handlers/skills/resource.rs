//! Resource gathering skills: fishing, woodcutting, mining.

use super::{
    ESFUERZO_EXCAVAR_GENERAL, ESFUERZO_EXCAVAR_RECOLECTOR, ESFUERZO_PESCAR_GENERAL,
    ESFUERZO_PESCAR_RECOLECTOR, ESFUERZO_TALAR_GENERAL, ESFUERZO_TALAR_RECOLECTOR,
    equipped_weapon_obj, is_recolector, luck_denominator, max_items_extraibles, mod_fundicion,
    try_level_skill_with_hit,
};
use crate::data::objects::ObjType;
use crate::game::constants::*;
use crate::game::handlers::common::*;
use crate::game::handlers::send_inventory_slot;
use crate::game::types::{GameState, SendTarget};
use crate::net::ConnectionId;
use crate::protocol::{binary_packets, font_index};

/// VB6: PECES_POSIBLES — fish types obtainable from net fishing (ListaPeces).
/// Regular fishing (rod) always yields PESCADO_OBJ (139).
/// Net fishing yields a random fish from this list.
const LISTA_PECES: [i32; 4] = [139, 544, 545, 546];

pub(crate) async fn do_pescar(state: &mut GameState, conn_id: ConnectionId, tx: i32, ty: i32) {
    let (map, ux, uy, class, skill) = match state.users.get(&conn_id) {
        Some(u) => (u.pos_map, u.pos_x, u.pos_y, u.class, u.skills[12]), // Pesca = index 12 (1-based 13)
        None => return,
    };

    // Check equipped fishing tool
    let weapon = state
        .users
        .get(&conn_id)
        .map(|u| equipped_weapon_obj(u))
        .unwrap_or(0);
    if weapon == 0 {
        state.send_console(conn_id, "Necesitas una caña de pescar", font_index::INFO);
        return;
    }

    // Check target has water (VB6 HayAgua)
    let has_water = state.hay_agua(map, tx, ty);

    if !has_water {
        state.send_msg_id(conn_id, 250, "");
        return;
    }

    // Stamina cost
    let sta_cost = if is_recolector(class) {
        ESFUERZO_PESCAR_RECOLECTOR
    } else {
        ESFUERZO_PESCAR_GENERAL
    };
    if let Some(u) = state.users.get(&conn_id) {
        if u.min_sta < sta_cost {
            state.send_msg_id(conn_id, 17, "");
            return;
        }
    }

    // Deduct stamina
    if let Some(u) = state.users.get_mut(&conn_id) {
        u.min_sta -= sta_cost;
    }

    // Play sound to area
    let snd = binary_packets::write_play_wave(SND_PESCAR as u8, ux as i16, uy as i16);
    state.send_data_bytes(SendTarget::ToArea { map, x: ux, y: uy }, &snd);

    // Luck roll
    let suerte = luck_denominator(skill);
    let roll = random_number(1, suerte);

    if roll <= 6 {
        // VB6: Worker class gets MaxItemsExtraibles, others get 1
        let level = state.users.get(&conn_id).map(|u| u.level).unwrap_or(1);
        let amount = if is_recolector(class) {
            random_number(1, max_items_extraibles(level))
        } else {
            1
        };

        let slot = find_or_add_inv_slot(state, conn_id, PESCADO_OBJ, amount);
        if let Some(idx) = slot {
            send_inventory_slot(state, conn_id, idx).await;
            state.send_msg_id(conn_id, 813, "");
        }

        // VB6: SubirSkill on success
        if let Some(u) = state.users.get_mut(&conn_id) {
            try_level_skill_with_hit(u, 12, true); // Pesca = index 12
        }
    } else {
        state.send_msg_id(conn_id, 814, "");

        // VB6: SubirSkill on failure
        if let Some(u) = state.users.get_mut(&conn_id) {
            try_level_skill_with_hit(u, 12, false);
        }
    }
    send_stats_sta(state, conn_id).await;
}

/// Woodcutting (DoTalar).
pub(crate) async fn do_talar(state: &mut GameState, conn_id: ConnectionId, tx: i32, ty: i32) {
    let (map, ux, uy, class, skill) = match state.users.get(&conn_id) {
        Some(u) => (u.pos_map, u.pos_x, u.pos_y, u.class, u.skills[9]), // Talar = index 9 (1-based 10)
        None => return,
    };

    // Check equipped axe
    let weapon = state
        .users
        .get(&conn_id)
        .map(|u| equipped_weapon_obj(u))
        .unwrap_or(0);
    if weapon != HACHA_LENADOR {
        state.send_console(conn_id, "Necesitas un hacha de leñador", font_index::INFO);
        return;
    }

    // Distance check
    let dist = (tx - ux).abs().max((ty - uy).abs());
    if dist > 2 || dist == 0 {
        return;
    }

    // Check tile has a tree (otArboles = 4)
    let tile_obj = state
        .game_data
        .maps
        .get(map as usize)
        .and_then(|m| m.as_ref())
        .and_then(|m| {
            if let Some(tile) = m.tiles.get((tx - 1) as usize, (ty - 1) as usize) {
                if tile.obj.obj_index > 0 {
                    state
                        .get_object(tile.obj.obj_index as i32)
                        .map(|o| o.obj_type)
                } else {
                    None
                }
            } else {
                None
            }
        });

    // Also check ground items on world grid
    let ground_obj_type = state
        .world
        .grid(map)
        .and_then(|g| g.tile(tx, ty))
        .and_then(|t| {
            if t.ground_item.obj_index > 0 {
                state
                    .get_object(t.ground_item.obj_index)
                    .map(|o| o.obj_type)
            } else {
                None
            }
        });

    let is_tree = tile_obj == Some(ObjType::Trees) || ground_obj_type == Some(ObjType::Trees);
    if !is_tree {
        state.send_msg_id(conn_id, 255, "");
        return;
    }

    // Stamina cost
    let sta_cost = if is_recolector(class) {
        ESFUERZO_TALAR_RECOLECTOR
    } else {
        ESFUERZO_TALAR_GENERAL
    };
    if let Some(u) = state.users.get(&conn_id) {
        if u.min_sta < sta_cost {
            state.send_msg_id(conn_id, 17, "");
            return;
        }
    }
    if let Some(u) = state.users.get_mut(&conn_id) {
        u.min_sta -= sta_cost;
    }

    // Play sound
    let snd = binary_packets::write_play_wave(SND_TALAR as u8, ux as i16, uy as i16);
    state.send_data_bytes(SendTarget::ToArea { map, x: ux, y: uy }, &snd);

    // Luck roll
    let suerte = luck_denominator(skill);
    let roll = random_number(1, suerte);

    if roll <= 6 {
        // VB6: Worker class gets MaxItemsExtraibles, others get 1
        let level = state.users.get(&conn_id).map(|u| u.level).unwrap_or(1);
        let amount = if is_recolector(class) {
            random_number(1, max_items_extraibles(level))
        } else {
            1
        };
        let slot = find_or_add_inv_slot(state, conn_id, LENA_OBJ, amount);
        if let Some(idx) = slot {
            send_inventory_slot(state, conn_id, idx).await;
            state.send_msg_id(conn_id, 825, "");
        }

        if let Some(u) = state.users.get_mut(&conn_id) {
            try_level_skill_with_hit(u, 9, true); // Talar = index 9
        }
    } else {
        state.send_msg_id(conn_id, 826, "");

        if let Some(u) = state.users.get_mut(&conn_id) {
            try_level_skill_with_hit(u, 9, false);
        }
    }
    send_stats_sta(state, conn_id).await;
}

/// Mining (DoMineria).
pub(crate) async fn do_mineria(state: &mut GameState, conn_id: ConnectionId, tx: i32, ty: i32) {
    let (map, ux, uy, class, skill) = match state.users.get(&conn_id) {
        Some(u) => (u.pos_map, u.pos_x, u.pos_y, u.class, u.skills[13]), // Mineria = index 13 (1-based 14)
        None => return,
    };

    // Check equipped pick
    let weapon = state
        .users
        .get(&conn_id)
        .map(|u| equipped_weapon_obj(u))
        .unwrap_or(0);
    if weapon != PIQUETE_MINERO {
        state.send_console(conn_id, "Necesitas un pico de minero", font_index::INFO);
        return;
    }

    // Distance check
    let dist = (tx - ux).abs().max((ty - uy).abs());
    if dist > 2 {
        return;
    }

    // Check tile has a mineral deposit (otYacimiento = Deposit = 22)
    let mineral_obj = state
        .world
        .grid(map)
        .and_then(|g| g.tile(tx, ty))
        .and_then(|t| {
            if t.ground_item.obj_index > 0 {
                state.get_object(t.ground_item.obj_index).cloned()
            } else {
                None
            }
        })
        .or_else(|| {
            state
                .game_data
                .maps
                .get(map as usize)
                .and_then(|m| m.as_ref())
                .and_then(|m| {
                    if let Some(tile) = m.tiles.get((tx - 1) as usize, (ty - 1) as usize) {
                        if tile.obj.obj_index > 0 {
                            state.get_object(tile.obj.obj_index as i32).cloned()
                        } else {
                            None
                        }
                    } else {
                        None
                    }
                })
        });

    let mineral_data = match mineral_obj {
        Some(ref o) if o.obj_type == ObjType::Deposit => o.clone(),
        _ => {
            state.send_msg_id(conn_id, 256, "");
            return;
        }
    };

    // Stamina cost
    let sta_cost = if is_recolector(class) {
        ESFUERZO_EXCAVAR_RECOLECTOR
    } else {
        ESFUERZO_EXCAVAR_GENERAL
    };
    if let Some(u) = state.users.get(&conn_id) {
        if u.min_sta < sta_cost {
            state.send_msg_id(conn_id, 17, "");
            return;
        }
    }
    if let Some(u) = state.users.get_mut(&conn_id) {
        u.min_sta -= sta_cost;
    }

    // Play sound
    let snd = binary_packets::write_play_wave(SND_MINERO as u8, ux as i16, uy as i16);
    state.send_data_bytes(SendTarget::ToArea { map, x: ux, y: uy }, &snd);

    // VB6: Apply ModFundicion class modifier to mining skill for luck calculation
    let effective_skill = (skill as f32 / mod_fundicion(class)) as i32;

    // Luck roll (uses effective skill with class modifier)
    let suerte = luck_denominator(effective_skill);
    let roll = random_number(1, suerte);

    if roll <= 5 {
        // VB6: Mining threshold is 5 (slightly harder than fishing's 6)
        let mineral_item = if mineral_data.mineral_index > 0 {
            mineral_data.mineral_index
        } else {
            HIERRO_CRUDO
        };

        // VB6: Worker class gets MaxItemsExtraibles, others get 1
        let level = state.users.get(&conn_id).map(|u| u.level).unwrap_or(1);
        let amount = if is_recolector(class) {
            random_number(1, max_items_extraibles(level))
        } else {
            1
        };
        let slot = find_or_add_inv_slot(state, conn_id, mineral_item, amount);
        if let Some(idx) = slot {
            send_inventory_slot(state, conn_id, idx).await;
            state.send_msg_id(conn_id, 827, "");
        }

        if let Some(u) = state.users.get_mut(&conn_id) {
            try_level_skill_with_hit(u, 13, true); // Mineria = index 13
        }
    } else {
        state.send_msg_id(conn_id, 828, "");

        if let Some(u) = state.users.get_mut(&conn_id) {
            try_level_skill_with_hit(u, 13, false);
        }
    }
    send_stats_sta(state, conn_id).await;
}

/// Net fishing (DoPescarRed) — VB6: Trabajo.bas:1559.
/// Requires RED_PESCA (543) equipped, target tile has a fish school (ObjType::FishingSpot),
/// Manhattan distance from player <= 2, and player is not on the same tile.
/// Yields a random fish from LISTA_PECES instead of always PESCADO_OBJ.
pub(crate) async fn do_pescar_red(state: &mut GameState, conn_id: ConnectionId, tx: i32, ty: i32) {
    let (map, ux, uy, class, skill) = match state.users.get(&conn_id) {
        Some(u) => (u.pos_map, u.pos_x, u.pos_y, u.class, u.skills[12]), // Pesca = index 12
        None => return,
    };

    // Distance check — VB6: Abs(.Pos.X - X) + Abs(.Pos.Y - Y) > 2 (Manhattan)
    let manhattan = (tx - ux).abs() + (ty - uy).abs();
    if manhattan > 2 {
        state.send_console(
            conn_id,
            "Estás demasiado lejos para pescar.",
            font_index::INFO,
        );
        return;
    }

    // Can't fish from your own tile
    if tx == ux && ty == uy {
        state.send_console(conn_id, "No puedes pescar desde allí.", font_index::INFO);
        return;
    }

    // Check the static map tile has a fish school object (ObjType::FishingSpot = otYacimientoPez)
    let tile_obj_index = state
        .game_data
        .maps
        .get(map as usize)
        .and_then(|m| m.as_ref())
        .and_then(|m| m.tiles.get((tx - 1) as usize, (ty - 1) as usize))
        .map(|t| t.obj.obj_index)
        .unwrap_or(0);

    if tile_obj_index == 0 {
        state.send_console(
            conn_id,
            "No hay un yacimiento de peces donde pescar.",
            font_index::INFO,
        );
        return;
    }

    let is_fish_spot = state
        .get_object(tile_obj_index as i32)
        .map(|o| o.obj_type == ObjType::FishingSpot)
        .unwrap_or(false);

    if !is_fish_spot {
        state.send_console(
            conn_id,
            "No hay un yacimiento de peces donde pescar.",
            font_index::INFO,
        );
        return;
    }

    // Stamina cost
    let sta_cost = if is_recolector(class) {
        ESFUERZO_PESCAR_RECOLECTOR
    } else {
        ESFUERZO_PESCAR_GENERAL
    };
    if let Some(u) = state.users.get(&conn_id) {
        if u.min_sta < sta_cost {
            state.send_msg_id(conn_id, 17, "");
            return;
        }
    }
    if let Some(u) = state.users.get_mut(&conn_id) {
        u.min_sta -= sta_cost;
    }

    // Play sound to area
    let snd = binary_packets::write_play_wave(SND_PESCAR as u8, ux as i16, uy as i16);
    state.send_data_bytes(SendTarget::ToArea { map, x: ux, y: uy }, &snd);

    // Luck roll — same formula as DoPescar
    let suerte = luck_denominator(skill);
    let roll = random_number(1, suerte);

    if roll <= 6 {
        let level = state.users.get(&conn_id).map(|u| u.level).unwrap_or(1);
        let amount = if is_recolector(class) {
            random_number(1, max_items_extraibles(level))
        } else {
            1
        };

        // VB6: ListaPeces(RandomNumber(1, NUM_PECES)) — random fish type from 4 possible
        let fish_idx = random_number(0, (LISTA_PECES.len() as i32) - 1) as usize;
        let fish_obj = LISTA_PECES[fish_idx];

        let slot = find_or_add_inv_slot(state, conn_id, fish_obj, amount);
        if let Some(idx) = slot {
            send_inventory_slot(state, conn_id, idx).await;
            state.send_console(conn_id, "¡Has pescado algunos peces!", font_index::INFO);
        }

        if let Some(u) = state.users.get_mut(&conn_id) {
            try_level_skill_with_hit(u, 12, true); // Pesca = index 12
        }
    } else {
        state.send_console(conn_id, "¡No has pescado nada!", font_index::INFO);

        if let Some(u) = state.users.get_mut(&conn_id) {
            try_level_skill_with_hit(u, 12, false);
        }
    }

    // VB6: Always grant proleta reputation (DoPescarRed doesn't check criminal unlike DoPescar)
    if let Some(u) = state.users.get_mut(&conn_id) {
        super::grant_crafting_rep(u);
    }

    send_stats_sta(state, conn_id).await;
}
