//! Crafting skills: smithing, carpentry, smelting, upgrades, construction.

use crate::net::ConnectionId;
use crate::game::class_race::PlayerClass;
use crate::game::types::{GameState, UserState, SendTarget, InventorySlot, MAX_INVENTORY_SLOTS};
use crate::protocol::{font_index, binary_packets};
use crate::data::objects::{ObjData, ObjType};
use crate::game::handlers::common::*;
use crate::game::handlers::{send_inventory_slot, send_full_inventory, check_user_level};
use crate::game::constants::*;
use super::{skill_id, luck_denominator, max_items_extraibles, grant_crafting_rep,
    has_items, remove_items, try_level_skill, try_level_skill_with_hit, equipped_weapon_obj, has_tool_equipped,
    mod_herreria, mod_carpinteria, mod_fundicion,
};

pub(crate) async fn do_herreria(state: &mut GameState, conn_id: ConnectionId, tx: i32, ty: i32) {
    let (map, ux, uy) = match state.users.get(&conn_id) {
        Some(u) => (u.pos_map, u.pos_x, u.pos_y),
        None => return,
    };

    // Distance check
    if (tx - ux).abs() > 2 || (ty - uy).abs() > 2 {
        return;
    }

    // Check target is an anvil (ObjType::Anvil = 27)
    let is_anvil = state.world.grid(map)
        .and_then(|g| g.tile(tx, ty))
        .and_then(|t| {
            if t.ground_item.obj_index > 0 {
                state.get_object(t.ground_item.obj_index).map(|o| o.obj_type == ObjType::Anvil)
            } else {
                None
            }
        })
        .unwrap_or(false);

    // Also check map static obj
    let is_anvil = is_anvil || state.game_data.maps.get(map as usize)
        .and_then(|m| m.as_ref())
        .map(|m| {
            if let Some(tile) = m.tiles.get((tx - 1) as usize, (ty - 1) as usize) {
                if tile.obj.obj_index > 0 {
                    state.get_object(tile.obj.obj_index as i32)
                        .map(|o| o.obj_type == ObjType::Anvil)
                        .unwrap_or(false)
                } else {
                    false
                }
            } else {
                false
            }
        })
        .unwrap_or(false);

    if !is_anvil {
        state.send_msg_id(conn_id, 263, "");
        return;
    }

    // Send buildable items lists (VB6 13.3 binary format)
    let (skill_herreria, class) = state.users.get(&conn_id)
        .map(|u| (u.skills[15], u.class))
        .unwrap_or((0, PlayerClass::Guerrero)); // Herreria = 15 (1-based 16)
    let effective_skill = (skill_herreria as f32 / mod_herreria(class)) as i32;

    let mut weapons = Vec::new();
    let mut armors = Vec::new();
    // VB6: Use parsed ArmasHerrero.dat / ArmadurasHerrero.dat lists instead of scanning all objects
    for &idx in &state.game_data.crafting.smith_weapons {
        if let Some(obj) = state.get_object(idx) {
            if obj.sk_herreria > 0 && obj.sk_herreria <= effective_skill {
                weapons.push(binary_packets::CraftItem {
                    name: obj.name.clone(),
                    grh_index: obj.grh_index as i16,
                    mat1: obj.ling_h as i16,
                    mat2: obj.ling_p as i16,
                    mat3: obj.ling_o as i16,
                    obj_index: obj.index as i16,
                    upgrade: 0,
                });
            }
        }
    }
    for &idx in &state.game_data.crafting.smith_armors {
        if let Some(obj) = state.get_object(idx) {
            if obj.sk_herreria > 0 && obj.sk_herreria <= effective_skill {
                armors.push(binary_packets::CraftItem {
                    name: obj.name.clone(),
                    grh_index: obj.grh_index as i16,
                    mat1: obj.ling_h as i16,
                    mat2: obj.ling_p as i16,
                    mat3: obj.ling_o as i16,
                    obj_index: obj.index as i16,
                    upgrade: 0,
                });
            }
        }
    }

    let pkt = binary_packets::write_smith_weapons(&weapons);
    state.send_bytes(conn_id, &pkt);
    let pkt = binary_packets::write_smith_armors(&armors);
    state.send_bytes(conn_id, &pkt);
    let pkt = binary_packets::write_show_blacksmith_form();
    state.send_bytes(conn_id, &pkt);
}

/// Carpenter (open UI — sends buildable items list + ShowCarpenterForm).
/// VB6: triggered by double-clicking equipped serrucho.
pub(crate) async fn do_carpinteria(state: &mut GameState, conn_id: ConnectionId) {
    let (skill_carpinteria, class) = state.users.get(&conn_id)
        .map(|u| (u.skills[14], u.class))
        .unwrap_or((0, PlayerClass::Guerrero)); // Carpinteria=14 (1-based 15)
    let effective_skill = (skill_carpinteria as f32 / mod_carpinteria(class)) as i32;

    let mut items = Vec::new();
    // VB6: Use parsed ObjCarpintero.dat list instead of scanning all objects
    for &idx in &state.game_data.crafting.carpenter_items {
        if let Some(obj) = state.get_object(idx) {
            if obj.sk_carpinteria > 0 && obj.sk_carpinteria <= effective_skill {
                items.push(binary_packets::CraftItem {
                    name: obj.name.clone(),
                    grh_index: obj.grh_index as i16,
                    mat1: obj.madera as i16,
                    mat2: 0, // MaderaElfica — not loaded yet
                    mat3: 0, // unused for carpenter
                    obj_index: obj.index as i16,
                    upgrade: 0,
                });
            }
        }
    }

    let pkt = binary_packets::write_carp_items(&items);
    state.send_bytes(conn_id, &pkt);
    let pkt = binary_packets::write_show_carpenter_form();
    state.send_bytes(conn_id, &pkt);
}

/// Smelting (FundirMineral).
pub(crate) async fn do_fundir(state: &mut GameState, conn_id: ConnectionId) {
    // Find mineral in inventory
    let mineral_slot = match state.users.get(&conn_id) {
        Some(u) => {
            u.inventory.iter().enumerate().find_map(|(i, slot)| {
                if slot.obj_index > 0 {
                    state.get_object(slot.obj_index).and_then(|o| {
                        if o.obj_type == ObjType::Mineral {
                            Some((i, slot.obj_index, slot.amount, o.lingote_index))
                        } else {
                            None
                        }
                    })
                } else {
                    None
                }
            })
        }
        None => return,
    };

    let (slot_idx, mineral_obj, amount, lingote_idx) = match mineral_slot {
        Some(data) => data,
        None => {
            state.send_msg_id(conn_id, 259, "");
            return;
        }
    };

    // VB6 13.3: Check mining skill with class modifier (ModFundicion)
    let (skill_mineria, class) = state.users.get(&conn_id)
        .map(|u| (u.skills[13], u.class))
        .unwrap_or((0, PlayerClass::Guerrero)); // Mineria=13 (1-based 14)
    let effective_skill = (skill_mineria as f32 / mod_fundicion(class)) as i32;

    // VB6: ObjData(mineral).MinSkill <= Skills(Mineria) / ModFundicion(clase)
    let min_skill = state.get_object(mineral_obj).map(|o| o.min_skill).unwrap_or(0);
    if effective_skill < min_skill {
        state.send_console(conn_id, "No tienes suficiente habilidad de minería para fundir esto.", font_index::INFO);
        return;
    }

    // VB6 13.3: Minerals per ingot (HierroCrudo=14, PlataCruda=20, OroCrudo=35)
    let minerals_needed = match mineral_obj {
        HIERRO_CRUDO => 14,
        PLATA_CRUDA => 20,
        ORO_CRUDO => 35,
        _ => 14,
    };

    if amount < minerals_needed {
        state.send_console(conn_id, &format!("No tienes suficientes minerales (necesitas {})", minerals_needed), font_index::INFO);
        return;
    }

    let ingot = if lingote_idx > 0 { lingote_idx } else {
        match mineral_obj {
            HIERRO_CRUDO => LINGOTE_HIERRO,
            PLATA_CRUDA => LINGOTE_PLATA,
            ORO_CRUDO => LINGOTE_ORO,
            _ => LINGOTE_HIERRO,
        }
    };

    // Remove minerals
    if let Some(u) = state.users.get_mut(&conn_id) {
        u.inventory[slot_idx].amount -= minerals_needed;
        if u.inventory[slot_idx].amount <= 0 {
            u.inventory[slot_idx].obj_index = 0;
            u.inventory[slot_idx].amount = 0;
        }
    }
    send_inventory_slot(state, conn_id, slot_idx).await;

    // Add ingot
    let slot = find_or_add_inv_slot(state, conn_id, ingot, 1);
    if let Some(idx) = slot {
        send_inventory_slot(state, conn_id, idx).await;
    }

    state.send_console(conn_id, "Has fundido un lingote", font_index::INFO);

    if let Some(u) = state.users.get_mut(&conn_id) {
        try_level_skill(u, 14); // Mining skill
        grant_crafting_rep(u);
    }
}

/// VB6 13.3: FundirArmas (Trabajo.bas:298-321, 921-985) — melt weapons back to ingots.
/// Returns 10-25% of the original crafting materials (LingH/LingP/LingO).
/// Requires: Blacksmith Hammer equipped, weapon in target slot, sufficient Herrería skill.
pub(crate) async fn do_fundir_arma(state: &mut GameState, conn_id: ConnectionId, inv_slot: usize) {
    if inv_slot == 0 || inv_slot > MAX_INVENTORY_SLOTS { return; }
    let slot_idx = inv_slot - 1;

    // Check weapon equipped is Blacksmith Hammer
    let has_hammer = match state.users.get(&conn_id) {
        Some(u) => {
            if u.equip.weapon == 0 || u.equip.weapon > MAX_INVENTORY_SLOTS { false }
            else {
                let w_idx = u.inventory[u.equip.weapon - 1].obj_index;
                w_idx == MARTILLO_HERRERO
            }
        }
        None => return,
    };
    if !has_hammer {
        state.send_console(conn_id, "Necesitas equipar un martillo de herrero.", font_index::INFO);
        return;
    }

    // Get target item data
    let item_data = match state.users.get(&conn_id) {
        Some(u) => {
            let item = &u.inventory[slot_idx];
            if item.obj_index <= 0 || item.amount <= 0 { None }
            else {
                state.get_object(item.obj_index).map(|o| {
                    (o.obj_type, o.ling_h, o.ling_p, o.ling_o, o.sk_herreria, item.equipped, o.name.clone())
                })
            }
        }
        None => return,
    };

    let (obj_type, ling_h, ling_p, ling_o, sk_needed, is_equipped, item_name) = match item_data {
        Some(d) => d,
        None => {
            state.send_console(conn_id, "No hay ningún objeto en ese slot.", font_index::INFO);
            return;
        }
    };

    // Must be a weapon
    if obj_type != ObjType::Weapon {
        state.send_console(conn_id, "Solo se pueden fundir armas.", font_index::INFO);
        return;
    }

    // Must have some crafting materials defined
    if ling_h == 0 && ling_p == 0 && ling_o == 0 {
        state.send_console(conn_id, "Este arma no se puede fundir.", font_index::INFO);
        return;
    }

    // Check Herrería skill (VB6: skill / ModHerreria)
    let (user_skill, class) = state.users.get(&conn_id)
        .map(|u| (u.skills[15], u.class))
        .unwrap_or((0, PlayerClass::Guerrero)); // SK16 = Herreria
    let effective_skill = (user_skill as f32 / mod_herreria(class)) as i32;
    if effective_skill < sk_needed {
        state.send_console(conn_id, &format!("Necesitas {} de herrería para fundir esto.", sk_needed), font_index::INFO);
        return;
    }

    // Random yield: 10-25%
    let pct = rand_range(10, 25);

    // Calculate returned lingots
    let ret_h = ((ling_h as f64 * pct as f64) * 0.01) as i32;
    let ret_p = ((ling_p as f64 * pct as f64) * 0.01) as i32;
    let ret_o = ((ling_o as f64 * pct as f64) * 0.01) as i32;

    // Unequip if equipped
    if is_equipped {
        if let Some(u) = state.users.get_mut(&conn_id) {
            if u.equip.weapon as usize == inv_slot { u.equip.weapon = 0; }
        }
    }

    // Remove 1 weapon from slot
    if let Some(u) = state.users.get_mut(&conn_id) {
        u.inventory[slot_idx].amount -= 1;
        if u.inventory[slot_idx].amount <= 0 {
            u.inventory[slot_idx].obj_index = 0;
            u.inventory[slot_idx].amount = 0;
        }
    }
    send_inventory_slot(state, conn_id, slot_idx).await;

    // Add returned lingots
    if ret_h > 0 {
        if let Some(idx) = find_or_add_inv_slot(state, conn_id, LINGOTE_HIERRO, ret_h) {
            send_inventory_slot(state, conn_id, idx).await;
        }
    }
    if ret_p > 0 {
        if let Some(idx) = find_or_add_inv_slot(state, conn_id, LINGOTE_PLATA, ret_p) {
            send_inventory_slot(state, conn_id, idx).await;
        }
    }
    if ret_o > 0 {
        if let Some(idx) = find_or_add_inv_slot(state, conn_id, LINGOTE_ORO, ret_o) {
            send_inventory_slot(state, conn_id, idx).await;
        }
    }

    // Play sound + message
    let snd = binary_packets::write_play_wave(SND_HERRERO as u8, 0, 0);
    state.send_bytes(conn_id, &snd);
    state.send_console(conn_id, &format!("Has fundido {} y obtenido el {}% de los lingotes.", item_name, pct), font_index::INFO);

    // Skill gain + reputation
    if let Some(u) = state.users.get_mut(&conn_id) {
        try_level_skill(u, 15); // Herreria
        grant_crafting_rep(u);
    }
}

/// VB6 13.3: DoUpgrade (Trabajo.bas:987-1116) — upgrade an item to its improved version.
/// Uses the Upgrade field in ObjData to find the target item.
/// Requires materials (difference between upgraded and original item, scaled).
/// Two paths: Herrería (hammer) for weapons/shields/helmets/armor, Carpintería (saw) for arrows/bows/boats.
pub(crate) async fn do_upgrade(state: &mut GameState, conn_id: ConnectionId, inv_slot: usize) {
    if inv_slot == 0 || inv_slot > MAX_INVENTORY_SLOTS { return; }
    let slot_idx = inv_slot - 1;

    // Get item and its upgrade target
    let item_data = match state.users.get(&conn_id) {
        Some(u) => {
            let item = &u.inventory[slot_idx];
            if item.obj_index <= 0 || item.amount <= 0 { None }
            else {
                state.get_object(item.obj_index).map(|o| {
                    (item.obj_index, o.upgrade, o.obj_type, o.ling_h, o.ling_p, o.ling_o,
                     o.madera, o.piedras, o.name.clone(), item.equipped)
                })
            }
        }
        None => return,
    };

    let (_item_idx, upgrade_idx, obj_type, cur_ling_h, cur_ling_p, cur_ling_o,
         cur_madera, _cur_piedras, item_name, is_equipped) = match item_data {
        Some(d) => d,
        None => {
            state.send_console(conn_id, "No hay ningún objeto en ese slot.", font_index::INFO);
            return;
        }
    };

    if upgrade_idx <= 0 {
        state.send_console(conn_id, "Este objeto no se puede mejorar.", font_index::INFO);
        return;
    }

    // Get upgrade target data
    let upgrade_data = match state.get_object(upgrade_idx) {
        Some(o) => (o.ling_h, o.ling_p, o.ling_o, o.madera, o.piedras,
                    o.sk_herreria, o.sk_carpinteria, o.name.clone()),
        None => {
            state.send_console(conn_id, "Error: objeto mejorado no existe.", font_index::INFO);
            return;
        }
    };
    let (up_ling_h, up_ling_p, up_ling_o, up_madera, up_piedras,
         up_sk_herreria, up_sk_carpinteria, upgrade_name) = upgrade_data;

    // Determine path: Herrería or Carpintería
    let weapon_idx = state.users.get(&conn_id)
        .and_then(|u| {
            if u.equip.weapon > 0 && u.equip.weapon <= MAX_INVENTORY_SLOTS {
                Some(u.inventory[u.equip.weapon - 1].obj_index)
            } else { None }
        })
        .unwrap_or(0);

    let is_smith = weapon_idx == MARTILLO_HERRERO;
    let is_carp = weapon_idx == SERRUCHO_CARPINTERO;

    if !is_smith && !is_carp {
        state.send_console(conn_id, "Necesitas equipar un martillo de herrero o un serrucho.", font_index::INFO);
        return;
    }

    // VB6: PORCENTAJE_MATERIALES_UPGRADE — materials needed = upgrade mats - (current mats * 0.85)
    let pct = 0.85f64;

    if is_smith {
        // Herrería path: weapons, shields, helmets, armor (VB6: skill / ModHerreria)
        let (user_skill, class) = state.users.get(&conn_id)
            .map(|u| (u.skills[15], u.class))
            .unwrap_or((0, PlayerClass::Guerrero));
        let effective_skill = (user_skill as f32 / mod_herreria(class)) as i32;
        if effective_skill < up_sk_herreria {
            state.send_console(conn_id, &format!("Necesitas {} de herrería.", up_sk_herreria), font_index::INFO);
            return;
        }

        let need_h = (up_ling_h as f64 - cur_ling_h as f64 * pct).max(0.0) as i32;
        let need_p = (up_ling_p as f64 - cur_ling_p as f64 * pct).max(0.0) as i32;
        let need_o = (up_ling_o as f64 - cur_ling_o as f64 * pct).max(0.0) as i32;

        // Check materials
        if !has_items(state, conn_id, LINGOTE_HIERRO, need_h)
            || !has_items(state, conn_id, LINGOTE_PLATA, need_p)
            || !has_items(state, conn_id, LINGOTE_ORO, need_o) {
            state.send_console(conn_id, "No tienes suficientes lingotes para la mejora.", font_index::INFO);
            return;
        }

        // Remove materials
        remove_items(state, conn_id, LINGOTE_HIERRO, need_h).await;
        remove_items(state, conn_id, LINGOTE_PLATA, need_p).await;
        remove_items(state, conn_id, LINGOTE_ORO, need_o).await;

        let snd = binary_packets::write_play_wave(SND_HERRERO as u8, 0, 0);
        state.send_bytes(conn_id, &snd);

        if let Some(u) = state.users.get_mut(&conn_id) {
            try_level_skill(u, 15); // Herreria
        }
    } else {
        // Carpintería path: arrows, weapons (wood), boats (VB6: skill / ModCarpinteria)
        let (user_skill, class) = state.users.get(&conn_id)
            .map(|u| (u.skills[14], u.class))
            .unwrap_or((0, PlayerClass::Guerrero));
        let effective_skill = (user_skill as f32 / mod_carpinteria(class)) as i32;
        if effective_skill < up_sk_carpinteria {
            state.send_console(conn_id, &format!("Necesitas {} de carpintería.", up_sk_carpinteria), font_index::INFO);
            return;
        }

        let need_wood = (up_madera as f64 - cur_madera as f64 * pct).max(0.0) as i32;
        let need_stones = (up_piedras as f64).max(0.0) as i32;

        if !has_items(state, conn_id, LENA_OBJ, need_wood) {
            state.send_console(conn_id, "No tienes suficiente madera para la mejora.", font_index::INFO);
            return;
        }
        if need_stones > 0 && !has_items(state, conn_id, PIEDRA_OBJ, need_stones) {
            state.send_console(conn_id, "No tienes suficientes piedras para la mejora.", font_index::INFO);
            return;
        }

        remove_items(state, conn_id, LENA_OBJ, need_wood).await;
        if need_stones > 0 {
            remove_items(state, conn_id, PIEDRA_OBJ, need_stones).await;
        }

        let snd = binary_packets::write_play_wave(SND_CARPINTERO as u8, 0, 0);
        state.send_bytes(conn_id, &snd);

        if let Some(u) = state.users.get_mut(&conn_id) {
            try_level_skill(u, 14); // Carpinteria
        }
    }

    // Unequip if equipped
    if is_equipped {
        if let Some(u) = state.users.get_mut(&conn_id) {
            match obj_type {
                ObjType::Weapon => { if u.equip.weapon as usize == inv_slot { u.equip.weapon = 0; } }
                ObjType::Shield => { if u.equip.shield as usize == inv_slot { u.equip.shield = 0; } }
                ObjType::Helmet => { if u.equip.helmet as usize == inv_slot { u.equip.helmet = 0; } }
                ObjType::Armor => { if u.equip.armor as usize == inv_slot { u.equip.armor = 0; } }
                _ => {}
            }
        }
    }

    // Replace item with upgraded version
    if let Some(u) = state.users.get_mut(&conn_id) {
        u.inventory[slot_idx].obj_index = upgrade_idx;
        // Keep amount = 1, unequipped
        u.inventory[slot_idx].amount = 1;
        u.inventory[slot_idx].equipped = false;
    }
    send_inventory_slot(state, conn_id, slot_idx).await;

    // Stamina cost
    let is_worker = state.users.get(&conn_id)
        .map(|u| u.class.is_recolector())
        .unwrap_or(false);
    let sta_cost = if is_worker { 2 } else { 6 };
    if let Some(u) = state.users.get_mut(&conn_id) {
        u.min_sta = (u.min_sta - sta_cost).max(0);
    }

    state.send_console(conn_id, &format!("Has mejorado {} a {}.", item_name, upgrade_name), font_index::INFO);
    send_stats_sta(state, conn_id).await;

    // Crafting reputation
    if let Some(u) = state.users.get_mut(&conn_id) {
        grant_crafting_rep(u);
    }
}

pub(crate) async fn handle_construct_smith(state: &mut GameState, conn_id: ConnectionId, item_index: i32) {
    // VB6: must have MARTILLO_HERRERO or MARTILLO_HERRERO_NEWBIE equipped
    let weapon_idx = state.users.get(&conn_id).map(|u| equipped_weapon_obj(u)).unwrap_or(0);
    if weapon_idx != MARTILLO_HERRERO && weapon_idx != MARTILLO_HERRERO_NEWBIE {
        state.send_console(conn_id, "Necesitas un martillo de herrero equipado.", font_index::INFO);
        return;
    }

    let obj_index: i32 = item_index;
    if obj_index < 1 { return; }

    let obj = match state.get_object(obj_index) {
        Some(o) => o.clone(),
        None => return,
    };

    if obj.sk_herreria <= 0 { return; }

    // VB6: Validate item is in ArmasHerrero or ArmadurasHerrero list
    if !state.game_data.crafting.is_smith_item(obj_index) { return; }

    // Check skill (VB6: skill / ModHerreria)
    let (skill, class) = state.users.get(&conn_id)
        .map(|u| (u.skills[15], u.class))
        .unwrap_or((0, PlayerClass::Guerrero)); // Herreria=15 (1-based 16)
    let effective_skill = (skill as f32 / mod_herreria(class)) as i32;
    if effective_skill < obj.sk_herreria {
        state.send_console(conn_id, "No tienes suficiente habilidad", font_index::INFO);
        return;
    }

    // Check materials (iron, silver, gold ingots)
    let has_materials = check_has_items(state, conn_id, &[
        (LINGOTE_HIERRO, obj.ling_h),
        (LINGOTE_PLATA, obj.ling_p),
        (LINGOTE_ORO, obj.ling_o),
    ]);

    if !has_materials {
        state.send_console(conn_id, "No tienes los materiales necesarios", font_index::INFO);
        return;
    }

    // Remove materials
    remove_items_from_inv(state, conn_id, LINGOTE_HIERRO, obj.ling_h).await;
    remove_items_from_inv(state, conn_id, LINGOTE_PLATA, obj.ling_p).await;
    remove_items_from_inv(state, conn_id, LINGOTE_ORO, obj.ling_o).await;

    // Give crafted item
    let slot = find_or_add_inv_slot(state, conn_id, obj_index, 1);
    if let Some(idx) = slot {
        send_inventory_slot(state, conn_id, idx).await;
    }

    // Play sound
    let (map, x, y) = state.users.get(&conn_id).map(|u| (u.pos_map, u.pos_x, u.pos_y)).unwrap_or((0,0,0));
    let snd = binary_packets::write_play_wave(SND_HERRERO as u8, x as i16, y as i16);
    state.send_data_bytes(SendTarget::ToArea { map, x, y }, &snd);

    state.send_console(conn_id, &format!("Has construido {}", obj.name), font_index::INFO);

    if let Some(u) = state.users.get_mut(&conn_id) {
        try_level_skill(u, 16);
        grant_crafting_rep(u);
    }
}

/// CNC — Construct carpentry item.
pub(crate) async fn handle_construct_carp(state: &mut GameState, conn_id: ConnectionId, item_index: i32) {
    let obj_index: i32 = item_index;
    if obj_index < 1 { return; }

    let obj = match state.get_object(obj_index) {
        Some(o) => o.clone(),
        None => return,
    };

    if obj.sk_carpinteria <= 0 { return; }

    // VB6: Validate item is in ObjCarpintero list
    if !state.game_data.crafting.is_carpenter_item(obj_index) { return; }

    // VB6: SERRUCHO_CARPINTERO or SERRUCHO_CARPINTERO_NEWBIE must be equipped
    let weapon = state.users.get(&conn_id).map(|u| equipped_weapon_obj(u)).unwrap_or(0);
    if weapon != SERRUCHO_CARPINTERO && weapon != SERRUCHO_CARPINTERO_NEWBIE {
        state.send_console(conn_id, "Necesitas un serrucho de carpintero", font_index::INFO);
        return;
    }

    // Check skill (VB6: skill / ModCarpinteria)
    let (skill, class) = state.users.get(&conn_id)
        .map(|u| (u.skills[14], u.class))
        .unwrap_or((0, PlayerClass::Guerrero)); // Carpinteria=14 (1-based 15)
    let effective_skill = (skill as f32 / mod_carpinteria(class)) as i32;
    if effective_skill < obj.sk_carpinteria {
        state.send_console(conn_id, "No tienes suficiente habilidad", font_index::INFO);
        return;
    }

    // Check materials (wood + stones)
    let has_materials = check_has_items(state, conn_id, &[
        (LENA_OBJ, obj.madera),
        (PIEDRA_OBJ, obj.piedras),
    ]);

    if !has_materials {
        state.send_console(conn_id, "No tienes los materiales necesarios", font_index::INFO);
        return;
    }

    // Remove materials
    remove_items_from_inv(state, conn_id, LENA_OBJ, obj.madera).await;
    remove_items_from_inv(state, conn_id, PIEDRA_OBJ, obj.piedras).await;

    // Give crafted item
    let slot = find_or_add_inv_slot(state, conn_id, obj_index, 1);
    if let Some(idx) = slot {
        send_inventory_slot(state, conn_id, idx).await;
    }

    // Play sound
    let (map, x, y) = state.users.get(&conn_id).map(|u| (u.pos_map, u.pos_x, u.pos_y)).unwrap_or((0,0,0));
    let snd = binary_packets::write_play_wave(SND_CARPINTERO as u8, x as i16, y as i16);
    state.send_data_bytes(SendTarget::ToArea { map, x, y }, &snd);

    state.send_console(conn_id, &format!("Has construido {}", obj.name), font_index::INFO);

    if let Some(u) = state.users.get_mut(&conn_id) {
        try_level_skill(u, 15);
        grant_crafting_rep(u);
    }
}

// =====================================================================
// Survival skill — Campfire creation (VB6: DoHacerFogata)
// =====================================================================

/// Campfire object indices
// FOGATA_OBJ and LENA_FOGATA imported from crate::game::constants

/// /FOGATA or survival skill — Create a campfire using firewood from inventory.
/// VB6: Requires 3+ Leña, success based on survival skill level.
pub(crate) async fn handle_crear_fogata(state: &mut GameState, conn_id: ConnectionId) {
    let user_data = match state.users.get(&conn_id) {
        Some(u) if u.logged => (u.dead, u.pos_map, u.pos_x, u.pos_y,
            u.skills.get((skill_id::SUPERVIVENCIA - 1) as usize).copied().unwrap_or(0),
            u.criminal),
        _ => return,
    };
    let (dead, map, x, y, skill_surv, _criminal) = user_data;

    if dead {
        state.send_console(conn_id, "Estas muerto.", font_index::INFO);
        return;
    }

    // Zone-aware safe check — no campfires in safe zones
    if is_safe_at(state, map, x, y) {
        state.send_console(conn_id, "No puedes crear fogatas en zona segura.", font_index::INFO);
        return;
    }

    // Check for firewood in inventory (need 3+)
    let lena_count: i32 = state.users.get(&conn_id)
        .map(|u| u.inventory.iter().filter(|s| s.obj_index == LENA_FOGATA).map(|s| s.amount).sum())
        .unwrap_or(0);

    if lena_count < 3 {
        state.send_console(conn_id, "Necesitas al menos 3 leñas para crear una fogata.", font_index::INFO);
        return;
    }

    // Success chance based on skill (VB6)
    let suerte = if skill_surv < 6 {
        3 // 33%
    } else if skill_surv <= 34 {
        2 // 50%
    } else {
        1 // 100%
    };

    let roll = rand_range(1, suerte);
    let success = roll == 1;

    // Consume 3 firewood
    let mut removed = 0;
    if let Some(user) = state.users.get_mut(&conn_id) {
        for slot in user.inventory.iter_mut() {
            if slot.obj_index == LENA_FOGATA && slot.amount > 0 && removed < 3 {
                let take = (3 - removed).min(slot.amount);
                slot.amount -= take;
                removed += take;
                if slot.amount <= 0 {
                    slot.obj_index = 0;
                    slot.amount = 0;
                }
            }
        }
    }
    send_full_inventory(state, conn_id).await;

    if success {
        // Place campfire on ground
        let tile_free = state.world.grid(map).and_then(|g| g.tile(x, y))
            .map(|t| t.ground_item.obj_index == 0)
            .unwrap_or(false);

        if tile_free {
            {
                let grid = state.world.grid_mut(map);
                if let Some(tile) = grid.tile_mut(x, y) {
                    tile.ground_item.obj_index = FOGATA_OBJ;
                    tile.ground_item.amount = 1;
                }
            }

            // Get campfire GRH for visual
            let grh = state.get_object(FOGATA_OBJ).map(|o| o.grh_index).unwrap_or(0);
            let ho_pkt = binary_packets::write_object_create(x as i16, y as i16, grh as i16);
            state.send_data_bytes(SendTarget::ToArea { map, x, y }, &ho_pkt);

            // Add to cleanup list (temporary — VB6 uses garbage collector)
            clean_world_add_item(state, map, x, y, 180, FOGATA_OBJ);
        }

        state.send_console(conn_id, "Has creado una fogata.", font_index::INFO);

        // XP gain on success
        if let Some(u) = state.users.get_mut(&conn_id) {
            try_level_skill(u, skill_id::SUPERVIVENCIA as usize);
        }
    } else {
        state.send_console(conn_id, "No lograste encender la fogata.", font_index::INFO);

        // XP on failure (half)
        if let Some(u) = state.users.get_mut(&conn_id) {
            try_level_skill_with_hit(u, skill_id::SUPERVIVENCIA as usize, false);
        }
    }
}

/// VB6: Get health status text based on survival skill level.
/// Used when looking at NPCs/players — gives more detail with higher skill.
pub(crate) fn health_status_text(current_hp: i32, max_hp: i32, survival_skill: i32) -> &'static str {
    if max_hp <= 0 { return "Dudoso"; }
    let pct = (current_hp as f64 / max_hp as f64 * 100.0) as i32;

    if survival_skill <= 10 {
        "Dudoso"
    } else if survival_skill <= 20 {
        if pct < 50 { "Herido" } else { "Sano" }
    } else if survival_skill <= 30 {
        if pct < 25 { "Malherido" } else if pct < 75 { "Herido" } else { "Sano" }
    } else if survival_skill <= 40 {
        if pct < 15 { "Muy malherido" } else if pct < 50 { "Herido" }
        else if pct < 85 { "Levemente herido" } else { "Sano" }
    } else {
        if pct < 5 { "Agonizando" }
        else if pct < 15 { "Casi muerto" }
        else if pct < 30 { "Muy malherido" }
        else if pct < 50 { "Herido" }
        else if pct < 75 { "Levemente herido" }
        else if pct < 95 { "Sano" }
        else { "Intacto" }
    }
}

// find_or_add_inv_slot, check_has_items, remove_items_from_inv — moved to common.rs

