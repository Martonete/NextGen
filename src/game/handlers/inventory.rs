//! Inventory handlers: equip, use item, pickup, drop, left/right click, safe toggle.
//! Extracted from mod.rs to reduce file size.

use tracing::info;
use crate::net::ConnectionId;
use crate::game::types::{GameState, SendTarget, InventorySlot, MAX_INVENTORY_SLOTS, privilege_level};
use crate::game::world;
use crate::protocol::{server_opcodes, font_types, fields::read_field};
use crate::data::objects::{ObjData, ObjType};
use super::common::*;
use super::{
    send_inventory_slot, send_full_inventory,
    warp_user, revive_user, naked_body,
    iniciar_comercio_npc, iniciar_banco, iniciar_clan_banco,
    DEAD_BODY_NEUTRAL, DEAD_HEAD_NEUTRAL,
};

// =====================================================================
// Inventory handlers
// =====================================================================

/// EQUI<slot> — Equip/unequip item from inventory slot.
pub(super) async fn handle_equip(state: &mut GameState, conn_id: ConnectionId, data: &str) {
    let slot_str = strip_opcode(data, 4);
    let slot: usize = match slot_str.parse::<usize>() {
        Ok(s) if s >= 1 && s <= MAX_INVENTORY_SLOTS => s,
        _ => return,
    };
    let idx = slot - 1; // 0-based

    let user = match state.users.get(&conn_id) {
        Some(u) if u.logged => u,
        _ => return,
    };

    let inv_slot = &user.inventory[idx];
    if inv_slot.obj_index == 0 {
        return;
    }

    let obj_index = inv_slot.obj_index;
    let currently_equipped = inv_slot.equipped;

    // Look up the object to determine equipment type
    let obj_data = match state.get_object(obj_index) {
        Some(o) => o.clone(),
        None => return,
    };

    if currently_equipped {
        // Unequip
        unequip_slot(state, conn_id, idx, &obj_data.obj_type);
    } else {
        // Item restriction checks (VB6: InvUsuario.bas)
        let (user_level, user_class, user_privileges, user_criminal,
             user_armada, user_caos) = match state.users.get(&conn_id) {
            Some(u) => (u.level, u.class.clone(), u.privileges, u.criminal,
                       u.armada_real, u.fuerzas_caos),
            None => return,
        };

        // VB6: GMs (>= Semidios) bypass ALL equipment restrictions
        let is_gm = user_privileges >= privilege_level::SEMIDIOS;

        // Level requirement
        if obj_data.lvl > 0 && user_level < obj_data.lvl && !is_gm {
            let msg = format!("{}112@{}", server_opcodes::CONSOLE_MSG, obj_data.lvl);
            state.send_to(conn_id, &msg).await;
            return;
        }

        // Class restriction (VB6: ClasePuedeUsarItem Or Privilegios >= Semidios)
        if !is_gm && !obj_data.class_prohibida.is_empty() {
            let uc = user_class.to_uppercase();
            if obj_data.class_prohibida.iter().any(|c| c.to_uppercase() == uc) {
                let msg = "||113".to_string(); // TEXTO113: Tu clase, genero o raza no puede usar este objeto
                state.send_to(conn_id, &msg).await;
                return;
            }
        }

        // Faction restriction (VB6: FaccionPuedeUsarItem Or Privilegios >= Semidios)
        if !is_gm {
            if obj_data.real {
                if user_criminal || !user_armada {
                    let msg = format!("{}Solo miembros de la Armada Real pueden usar este item{}", server_opcodes::CONSOLE_MSG, font_types::INFO);
                    state.send_to(conn_id, &msg).await;
                    return;
                }
            }
            if obj_data.caos {
                if !user_criminal || !user_caos {
                    let msg = format!("{}Solo miembros de las Fuerzas del Caos pueden usar este item{}", server_opcodes::CONSOLE_MSG, font_types::INFO);
                    state.send_to(conn_id, &msg).await;
                    return;
                }
            }
        }

        // Only allow equipping valid equipment types (VB6: InvUsuario.bas)
        // Potions, food, keys, etc. cannot be equipped
        match obj_data.obj_type {
            ObjType::Weapon | ObjType::Armor | ObjType::Shield |
            ObjType::Helmet | ObjType::Arrow | ObjType::Instrument |
            ObjType::Tool => {},
            _ => {
                // Not an equippable item type — reject silently
                return;
            }
        }

        // VB6: Can't equip armor/shield/helmet while navigating (InvUsuario.bas)
        let is_navigating = state.users.get(&conn_id).map(|u| u.navigating).unwrap_or(false);
        if is_navigating {
            match obj_data.obj_type {
                ObjType::Armor | ObjType::Shield | ObjType::Helmet => return,
                _ => {}
            }
        }

        // Two-handed weapon check: unequip shield if equipping 2h weapon
        if obj_data.obj_type == ObjType::Weapon && obj_data.dos_manos {
            let shield_slot = state.users.get(&conn_id).map(|u| u.equip.shield).unwrap_or(0);
            if shield_slot > 0 && shield_slot <= MAX_INVENTORY_SLOTS {
                unequip_slot(state, conn_id, shield_slot - 1, &ObjType::Shield);
                send_inventory_slot(state, conn_id, shield_slot - 1).await;
            }
        }

        // Arrow equip handling
        if obj_data.obj_type == ObjType::Arrow {
            // Equip as ammo
            let old_ammo = state.users.get(&conn_id).map(|u| u.equip.municion).unwrap_or(0);
            if old_ammo > 0 && old_ammo <= MAX_INVENTORY_SLOTS {
                if let Some(user) = state.users.get_mut(&conn_id) {
                    user.inventory[old_ammo - 1].equipped = false;
                }
                send_inventory_slot(state, conn_id, old_ammo - 1).await;
            }
            if let Some(user) = state.users.get_mut(&conn_id) {
                user.equip.municion = slot;
                user.inventory[idx].equipped = true;
            }
            send_inventory_slot(state, conn_id, idx).await;
            return;
        }

        // Equip — first unequip any item in the same category
        let old_slot = match obj_data.obj_type {
            ObjType::Weapon => state.users.get(&conn_id).map(|u| u.equip.weapon).unwrap_or(0),
            ObjType::Armor => state.users.get(&conn_id).map(|u| u.equip.armor).unwrap_or(0),
            ObjType::Shield => state.users.get(&conn_id).map(|u| u.equip.shield).unwrap_or(0),
            ObjType::Helmet => state.users.get(&conn_id).map(|u| u.equip.helmet).unwrap_or(0),
            _ => 0,
        };

        if old_slot > 0 && old_slot <= MAX_INVENTORY_SLOTS {
            let old_idx = old_slot - 1;
            unequip_slot(state, conn_id, old_idx, &obj_data.obj_type);
            // Send updated CSI for the OLD slot so client sees it as unequipped
            send_inventory_slot(state, conn_id, old_idx).await;
        }

        // Equip the new item
        if let Some(user) = state.users.get_mut(&conn_id) {
            user.inventory[idx].equipped = true;
            match obj_data.obj_type {
                ObjType::Weapon => {
                    user.equip.weapon = slot;
                    user.weapon_anim = obj_data.weapon_anim;
                }
                ObjType::Armor => {
                    user.equip.armor = slot;
                    // VB6: equiparRopaje — set body to armor's Ropaje graphic
                    if obj_data.num_ropaje > 0 {
                        user.body = obj_data.num_ropaje;
                    }
                }
                ObjType::Shield => {
                    user.equip.shield = slot;
                    user.shield_anim = obj_data.shield_anim;
                }
                ObjType::Helmet => {
                    user.equip.helmet = slot;
                    user.casco_anim = obj_data.casco_anim;
                }
                _ => {}
            }
        }
    }

    // Send updated CSI for this slot
    send_inventory_slot(state, conn_id, idx).await;

    // Broadcast appearance change to area
    let user_data = match state.users.get(&conn_id) {
        Some(u) => (u.pos_map, u.pos_x, u.pos_y, u.char_index,
                    u.weapon_anim, u.shield_anim, u.casco_anim),
        None => return,
    };
    let (map, x, y, char_index, weapon, shield, helmet) = user_data;

    // Send equipment change packets to area
    match obj_data.obj_type {
        ObjType::Weapon => {
            let pkt = format!("|W{},{}", char_index.0, weapon);
            state.send_data(SendTarget::ToArea { map, x, y }, &pkt).await;
        }
        ObjType::Armor => {
            // VB6: ChangeUserBody sends |B packet with new body
            let body = state.users.get(&conn_id).map(|u| u.body).unwrap_or(0);
            let pkt = format!("|B{},{}", char_index.0, body);
            state.send_data(SendTarget::ToArea { map, x, y }, &pkt).await;
        }
        ObjType::Shield => {
            let pkt = format!("|E{},{}", char_index.0, shield);
            state.send_data(SendTarget::ToArea { map, x, y }, &pkt).await;
        }
        ObjType::Helmet => {
            let pkt = format!("|C{},{}", char_index.0, helmet);
            state.send_data(SendTarget::ToArea { map, x, y }, &pkt).await;
        }
        _ => {}
    }
}

/// Unequip an item from a specific inventory slot.
pub(super) fn unequip_slot(state: &mut GameState, conn_id: ConnectionId, idx: usize, obj_type: &ObjType) {
    if let Some(user) = state.users.get_mut(&conn_id) {
        user.inventory[idx].equipped = false;
        match obj_type {
            ObjType::Weapon => {
                user.equip.weapon = 0;
                user.weapon_anim = super::common::NINGUN_ARMA;
            }
            ObjType::Armor => {
                user.equip.armor = 0;
                // VB6: DarCuerpoDesnudo — revert to naked body for race/gender
                let race = user.race.clone();
                let gender = user.gender.to_string();
                user.body = naked_body(&race, &gender);
            }
            ObjType::Shield => {
                user.equip.shield = 0;
                user.shield_anim = super::common::NINGUN_ESCUDO;
            }
            ObjType::Helmet => {
                user.equip.helmet = 0;
                user.casco_anim = super::common::NINGUN_CASCO;
            }
            _ => {}
        }
    }
}

/// USA<slot> — Use item from inventory.
/// QSA<slot>,<visible> — Use item via double-click on inventory picture.
/// VB6: picInv_DblClick sends QSA<slot>,<True|False>.
/// If InvenVisible = "FALSO", it's a hack attempt (using items with inv hidden).
pub(super) async fn handle_use_item_click(state: &mut GameState, conn_id: ConnectionId, data: &str) {
    let payload = strip_opcode(data, 3);
    let slot_str = read_field(1, payload, ',');
    let visible_str = read_field(2, payload, ',');

    // Anti-cheat: if inventory window is hidden, it's a hack
    if visible_str.eq_ignore_ascii_case("falso") || visible_str.eq_ignore_ascii_case("false") {
        info!("[CHEAT] QSA with hidden inventory from #{}", conn_id);
        return;
    }

    let slot: usize = match slot_str.parse::<usize>() {
        Ok(s) if s >= 1 && s <= MAX_INVENTORY_SLOTS => s,
        _ => return,
    };
    let idx = slot - 1;

    let (obj_index, _amount) = match state.users.get(&conn_id) {
        Some(u) if u.logged => {
            let inv_slot = &u.inventory[idx];
            if inv_slot.obj_index == 0 { return; }
            (inv_slot.obj_index, inv_slot.amount)
        }
        _ => return,
    };

    // Projectile items do nothing on use
    let is_projectile = state.get_object(obj_index).map(|o| o.proyectil).unwrap_or(false);
    if is_projectile { return; }

    // Anti-cheat: PuedoClickear — checks interval_click AND sets both
    // interval_click=6 and interval_poteo=8 (cross-locking, matches VB6)
    if !puede_clickear(state, conn_id) { return; }

    // Delegate to inner use-item with from_click=true so it skips
    // puede_potear() (already set by puede_clickear above)
    let usa_data = format!("USA{}", slot);
    handle_use_item_inner(state, conn_id, &usa_data, true).await;
}

pub(super) async fn handle_use_item(state: &mut GameState, conn_id: ConnectionId, data: &str) {
    handle_use_item_inner(state, conn_id, data, false).await;
}

/// Inner use-item logic. `from_click` = true when called from QSA (double-click),
/// which means puede_clickear() already set both interval_click and interval_poteo,
/// so we skip the puede_potear() check to avoid double-blocking.
pub(super) async fn handle_use_item_inner(state: &mut GameState, conn_id: ConnectionId, data: &str, from_click: bool) {
    let slot_str = strip_opcode(data, 3);
    let slot: usize = match slot_str.parse::<usize>() {
        Ok(s) if s >= 1 && s <= MAX_INVENTORY_SLOTS => s,
        _ => return,
    };
    let idx = slot - 1;

    let (obj_index, amount) = match state.users.get(&conn_id) {
        Some(u) if u.logged => {
            let inv = &u.inventory[idx];
            (inv.obj_index, inv.amount)
        }
        _ => return,
    };

    if obj_index == 0 || amount <= 0 {
        return;
    }

    // Death check — only ResurrectPotion can be used while dead
    let is_dead = state.users.get(&conn_id).map(|u| u.dead).unwrap_or(false);

    let obj_data = match state.get_object(obj_index) {
        Some(o) => o.clone(),
        None => return,
    };

    if is_dead && obj_data.obj_type != ObjType::ResurrectPotion {
        state.send_to(conn_id, "||5").await; // VB6 TEXTO5: muerto, no puede usar items
        return;
    }

    match obj_data.obj_type {
        ObjType::UseOnce | ObjType::Potion => {
            // Anti-cheat: check potion cooldown
            // When from_click=true, puede_clickear() already set both cooldowns
            if !from_click && !puede_potear(state, conn_id) {
                return;
            }

            // Remo potion (TipoPocion=6) — special handling: remove paralysis, costs 60 HP
            if obj_data.tipo_pocion == 6 {
                let (paralyzed, min_hp, class) = match state.users.get(&conn_id) {
                    Some(u) => (u.paralyzed, u.min_hp, u.class.clone()),
                    None => return,
                };
                if !paralyzed {
                    let msg = format!("P|No estas paralizado!{}", font_types::INFO);
                    state.send_to(conn_id, &msg).await;
                    return;
                }
                if min_hp <= 60 {
                    let msg = format!("P|No tienes suficiente vida para usar la pocion!{}", font_types::INFO);
                    state.send_to(conn_id, &msg).await;
                    return;
                }
                // Non-warrior/hunter have 3-round cooldown
                let is_warrior_or_hunter = class.eq_ignore_ascii_case("Guerrero") || class.eq_ignore_ascii_case("Cazador");
                if !is_warrior_or_hunter {
                    let counter_remo = state.users.get(&conn_id).map(|u| u.counter_remo).unwrap_or(0);
                    if counter_remo > 0 {
                        let msg = format!("P|Debes esperar para usar otra pocion Remo{}", font_types::INFO);
                        state.send_to(conn_id, &msg).await;
                        return;
                    }
                }
                // Apply: remove paralysis, cost 60 HP, set cooldown
                if let Some(user) = state.users.get_mut(&conn_id) {
                    user.paralyzed = false;
                    user.min_hp -= 60;
                    if !is_warrior_or_hunter {
                        user.counter_remo = 3;
                    }
                    user.inventory[idx].amount -= 1;
                    if user.inventory[idx].amount <= 0 {
                        user.inventory[idx] = InventorySlot::default();
                    }
                }
                // Send PARADOK to toggle paralysis off on client
                state.send_to(conn_id, "PARADOK").await;
                send_inventory_slot(state, conn_id, idx).await;
                send_stats_hp(state, conn_id).await;
                return;
            }

            // Apply potion/food effect
            apply_consumable(state, conn_id, &obj_data);

            // Consume one
            if let Some(user) = state.users.get_mut(&conn_id) {
                user.inventory[idx].amount -= 1;
                if user.inventory[idx].amount <= 0 {
                    user.inventory[idx] = InventorySlot::default();
                }
            }

            // Send updated slot and stats
            send_inventory_slot(state, conn_id, idx).await;
            send_stats_hp(state, conn_id).await;
            send_stats_mana(state, conn_id).await;
            send_stats_sta(state, conn_id).await;
            send_hunger_thirst(state, conn_id).await;
        }
        ObjType::Drink => {
            // Drinks restore thirst (min_agua), not stamina
            if let Some(user) = state.users.get_mut(&conn_id) {
                user.min_agua = (user.min_agua + obj_data.min_agua).min(user.max_agua);
                user.inventory[idx].amount -= 1;
                if user.inventory[idx].amount <= 0 {
                    user.inventory[idx] = InventorySlot::default();
                }
            }
            send_inventory_slot(state, conn_id, idx).await;
            send_hunger_thirst(state, conn_id).await;
        }
        ObjType::Key => {
            // Keys are used on doors — they're not "used" from inventory directly
            // The door interaction happens via LC/RC on a door tile
            let msg = format!("P|Esta llave sirve para abrir una puerta{}", font_types::INFO);
            state.send_to(conn_id, &msg).await;
        }
        ObjType::Boat => {
            // VB6: InvUsuario.bas Case eOBJType.otBarcos + Trabajo.bas DoNavega
            let (is_navigating, user_map, user_x, user_y) = match state.users.get(&conn_id) {
                Some(u) if u.logged => (u.navigating, u.pos_map, u.pos_x, u.pos_y),
                _ => return,
            };

            // VB6 mount check: LegalPos(adjacent, PuedeAgua=True) checks water + not blocked + no user/NPC
            // VB6 dismount: NO land check — always allowed (player may get stuck on water)
            if !is_navigating {
                // Mount — must have water tile adjacent (VB6: 4 cardinal LegalPos checks)
                let has_water_nearby = (1..=4).any(|h| {
                    let (dx, dy) = world::heading_to_offset(h);
                    let nx = user_x + dx;
                    let ny = user_y + dy;
                    // VB6 LegalPos(map, x, y, PuedeAgua=True): not blocked AND has water AND no user/NPC
                    !state.is_tile_blocked(user_map, nx, ny)
                        && state.hay_agua(user_map, nx, ny)
                        && state.world.grids.get(&user_map)
                            .map(|g| g.is_tile_free(nx, ny)).unwrap_or(false)
                });
                if !has_water_nearby {
                    state.send_to(conn_id, "||106").await; // TEXTO106
                    return;
                }
            }

            // VB6 DoNavega: If hidden, reveal first (NOVER)
            let (was_hidden, char_index_val, map_for_nover) = match state.users.get(&conn_id) {
                Some(u) => (u.hidden, u.char_index.0, u.pos_map),
                None => return,
            };
            if was_hidden {
                if let Some(u) = state.users.get_mut(&conn_id) {
                    u.hidden = false;
                }
                let nover = format!("NOVER{},0", char_index_val);
                state.send_data(SendTarget::ToMap(map_for_nover), &nover).await;
            }

            if is_navigating {
                // === DISMOUNT (VB6 DoNavega else branch) ===
                // No land check — VB6 allows dismounting anywhere
                let equip_info = state.users.get(&conn_id).map(|u| {
                    let get_inv_obj = |slot: usize| -> i32 {
                        if slot >= 1 && slot <= u.inventory.len() { u.inventory[slot - 1].obj_index } else { 0 }
                    };
                    (
                        get_inv_obj(u.equip.armor),
                        get_inv_obj(u.equip.weapon),
                        get_inv_obj(u.equip.shield),
                        get_inv_obj(u.equip.helmet),
                        u.old_head, u.dead, u.race.clone(), u.gender,
                    )
                });
                if let Some((armor_obj, weapon_obj, shield_obj, helmet_obj, saved_head, dead, race, gender)) = equip_info {
                    let armor_body = state.get_object(armor_obj).map(|o| o.num_ropaje).unwrap_or(0);
                    let weapon_anim = state.get_object(weapon_obj).map(|o| o.weapon_anim).unwrap_or(0);
                    let shield_anim = state.get_object(shield_obj).map(|o| o.shield_anim).unwrap_or(0);
                    let casco_anim = state.get_object(helmet_obj).map(|o| o.casco_anim).unwrap_or(0);
                    if let Some(u) = state.users.get_mut(&conn_id) {
                        u.navigating = false;
                        if !dead {
                            u.head = saved_head;
                            u.body = if armor_body > 0 { armor_body } else { naked_body(&race, &gender.to_string()) };
                            u.weapon_anim = weapon_anim;
                            u.shield_anim = shield_anim;
                            u.casco_anim = casco_anim;
                        } else {
                            // VB6: dead dismount → ghost body/head, no equipment
                            u.body = DEAD_BODY_NEUTRAL;
                            u.head = DEAD_HEAD_NEUTRAL;
                            u.weapon_anim = super::common::NINGUN_ARMA;
                            u.shield_anim = super::common::NINGUN_ESCUDO;
                            u.casco_anim = super::common::NINGUN_CASCO;
                        }
                    }
                }
            } else {
                // === MOUNT (VB6 DoNavega if branch) ===
                let ropaje = obj_data.num_ropaje;
                if let Some(u) = state.users.get_mut(&conn_id) {
                    u.old_head = u.head;
                    u.head = 0;
                    u.weapon_anim = super::common::NINGUN_ARMA;
                    u.shield_anim = super::common::NINGUN_ESCUDO;
                    u.casco_anim = super::common::NINGUN_CASCO;
                    u.navigating = true;
                    if ropaje > 0 {
                        u.body = ropaje;
                    }
                }
            }

            // VB6 DoNavega packets (order matters):
            // 1. ChangeUserChar → CP packet to area (including self)
            // 2. NAVEG to self
            // 3. NVG<charindex>,<flag> to ALL players
            let (cp_pkt, nvg_pkt, map, bx, by) = match state.users.get(&conn_id) {
                Some(u) => {
                    let nav_flag = if u.navigating { 1 } else { 0 };
                    // VB6 CP format: CP<charindex>,<body>,<head>,<heading>,<weapon>,<shield>,<fx>,<loops>,<helmet>
                    let cp = format!("CP{},{},{},{},{},{},{},{},{}",
                        u.char_index.0, u.body, u.head, u.heading,
                        u.weapon_anim, u.shield_anim,
                        0, 0, // FX, loops (no active FX during boat toggle)
                        u.casco_anim,
                    );
                    let nvg = format!("NVG{},{}", u.char_index.0, nav_flag);
                    (cp, nvg, u.pos_map, u.pos_x, u.pos_y)
                }
                None => return,
            };
            // CP to area (VB6 SendToUserArea = includes self)
            state.send_data(SendTarget::ToArea { map, x: bx, y: by }, &cp_pkt).await;
            // NAVEG to self (toggle client navigation state)
            state.send_to(conn_id, "NAVEG").await;
            // NVG to all (VB6 SendTarget.ToAll)
            state.send_data(SendTarget::ToAll, &nvg_pkt).await;
        }
        ObjType::Instrument => {
            // VB6: Play music instrument — broadcast TW<Snd1> to area
            let wav = obj_data.snd1; // VB6 uses Snd1 field for instrument sound ID
            let (map, x, y) = match state.users.get(&conn_id) {
                Some(u) if u.logged => (u.pos_map, u.pos_x, u.pos_y),
                _ => return,
            };
            if wav > 0 {
                let snd = format!("TW{}", wav);
                state.send_data(SendTarget::ToArea { map, x, y }, &snd).await;
            }
            let msg = format!("P|Tocas una melodia{}", font_types::INFO);
            state.send_to(conn_id, &msg).await;
        }
        ObjType::Scroll => {
            // Learn spell from scroll
            let spell_id = obj_data.hechizo_index;
            if spell_id <= 0 { return; }

            // VB6: Check if user already knows this spell
            let already_known = state.users.get(&conn_id)
                .map(|u| u.spells.iter().any(|&s| s == spell_id))
                .unwrap_or(false);
            if already_known {
                state.send_to(conn_id, "||182").await; // TEXTO182: Ya tenes ese hechizo
                return;
            }

            // Find empty spell slot
            let slot = match state.users.get(&conn_id) {
                Some(u) => u.spells.iter().position(|&s| s == 0),
                None => return,
            };

            if let Some(slot_idx) = slot {
                if let Some(u) = state.users.get_mut(&conn_id) {
                    u.spells[slot_idx] = spell_id;
                    u.inventory[idx].amount -= 1;
                    if u.inventory[idx].amount <= 0 {
                        u.inventory[idx] = InventorySlot::default();
                    }
                }
                send_inventory_slot(state, conn_id, idx).await;
                let spell_name = state.get_spell(spell_id)
                    .map(|s| s.nombre.clone())
                    .unwrap_or_default();
                // Send SHS to update the spell slot on client
                let shs_slot = slot_idx + 1; // 1-based
                let shs_pkt = format!("SHS{},{},{}", shs_slot, spell_id, spell_name);
                state.send_to(conn_id, &shs_pkt).await;
                state.send_to(conn_id, &format!("||832@{}", spell_name)).await; // TEXTO832
            } else {
                state.send_to(conn_id, "||181").await; // TEXTO181
            }
        }
        ObjType::EmptyBottle => {
            // Fill at water source — simplified, just inform
            state.send_to(conn_id, "||103").await; // TEXTO103: No hay agua allí
        }
        ObjType::FullBottle => {
            // Drink from bottle → restore thirst, swap to empty bottle variant
            if let Some(user) = state.users.get_mut(&conn_id) {
                user.min_agua = (user.min_agua + obj_data.min_agua).min(user.max_agua);
                // Swap to empty variant (IndexAbierta stores the empty bottle obj index)
                let empty_index = obj_data.index_abierta;
                if empty_index > 0 {
                    user.inventory[idx].obj_index = empty_index;
                    // Amount stays the same (1 full bottle → 1 empty bottle)
                } else {
                    // No empty variant, just consume
                    user.inventory[idx].amount -= 1;
                    if user.inventory[idx].amount <= 0 {
                        user.inventory[idx] = InventorySlot::default();
                    }
                }
            }
            send_inventory_slot(state, conn_id, idx).await;
            send_hunger_thirst(state, conn_id).await;
        }
        ObjType::Money => {
            // Gold pile: add to gold, remove from inventory
            if let Some(user) = state.users.get_mut(&conn_id) {
                user.gold += amount as i64;
                user.inventory[idx] = InventorySlot::default();
            }
            send_inventory_slot(state, conn_id, idx).await;
            send_stats_gold(state, conn_id).await;
        }
        ObjType::ResurrectPotion => {
            // Resurrection potion — can only use while dead
            if !is_dead {
                state.send_to(conn_id, "||117").await; // TEXTO117: Debes estar muerto para utilizar esta poción
                return;
            }
            // Consume the item first
            if let Some(user) = state.users.get_mut(&conn_id) {
                user.inventory[idx].amount -= 1;
                if user.inventory[idx].amount <= 0 {
                    user.inventory[idx] = InventorySlot::default();
                }
            }
            send_inventory_slot(state, conn_id, idx).await;
            // Use shared revive logic (restores body, head, sends CFF + CP)
            revive_user(state, conn_id).await;
            state.send_to(conn_id, "||119").await; // TEXTO119: Te has resucitado
        }
        ObjType::Mount => {
            // Mount/dismount — similar to boat but for land mounts
            let ropaje = obj_data.num_ropaje;
            let is_flying = obj_data.es_voladora;
            let navigating = state.users.get(&conn_id).map(|u| u.navigating).unwrap_or(false);
            if navigating {
                // Dismount
                if let Some(u) = state.users.get_mut(&conn_id) {
                    u.navigating = false;
                    u.body = 1; // Reset to default body
                }
            } else {
                // Mount up
                if let Some(u) = state.users.get_mut(&conn_id) {
                    u.navigating = true;
                    if ropaje > 0 {
                        u.body = ropaje;
                    }
                }
            }
            let cc = state.users.get(&conn_id).map(|u| u.build_cc_packet()).unwrap_or_default();
            let (map, x, y) = match state.users.get(&conn_id) {
                Some(u) => (u.pos_map, u.pos_x, u.pos_y),
                None => return,
            };
            if !cc.is_empty() {
                state.send_data(SendTarget::ToArea { map, x, y }, &cc).await;
            }
        }
        ObjType::ScrollItem => {
            // VB6: Buff scroll — typeScroll: 1=exp, 2=gold, 3=drop, 4=crystal drop
            let ts = obj_data.type_scroll;
            if ts < 1 || ts > 4 { return; }

            // Check if scroll type already active
            let already_active = state.users.get(&conn_id)
                .map(|u| u.scroll_active[ts as usize - 1])
                .unwrap_or(false);

            if already_active {
                state.send_to(conn_id, "||928").await; // VB6: scroll already active
                return;
            }

            let time_s = obj_data.time_scroll;
            let mult = obj_data.mult_scroll;

            if let Some(user) = state.users.get_mut(&conn_id) {
                user.scroll_active[ts as usize - 1] = true;
                user.scroll_time[ts as usize - 1] = time_s;
                user.scroll_mult[ts as usize - 1] = mult;
                user.inventory[idx].amount -= 1;
                if user.inventory[idx].amount <= 0 {
                    user.inventory[idx] = InventorySlot::default();
                }
            }

            // VB6: Send TIS packet + message
            let tis_pkt = format!("TIS{},{},{}", ts, time_s, time_s);
            state.send_to(conn_id, &tis_pkt).await;

            let scroll_name = match ts {
                1 => "Experiencia",
                2 => "Oro",
                3 => "Drop",
                4 => "Drop de Cristales",
                _ => "Desconocido",
            };
            let msg = format!("||929@{}@{}@{}", scroll_name, time_s, mult);
            state.send_to(conn_id, &msg).await;
            send_inventory_slot(state, conn_id, idx).await;
        }
        ObjType::Sack => {
            // VB6: Donation sack — add credits
            let credits = obj_data.cant_credits;
            if credits <= 0 { return; }

            if let Some(user) = state.users.get_mut(&conn_id) {
                user.puntos_donacion += credits as i64;
                user.inventory[idx].amount -= 1;
                if user.inventory[idx].amount <= 0 {
                    user.inventory[idx] = InventorySlot::default();
                }
            }
            let msg = format!("||930@{}", credits);
            state.send_to(conn_id, &msg).await;
            send_inventory_slot(state, conn_id, idx).await;
        }
        ObjType::RenounceHorde => {
            // VB6: Renounce Chaos faction
            let guild_idx = state.users.get(&conn_id).map(|u| u.guild_index).unwrap_or(0);
            if guild_idx > 0 {
                state.send_to(conn_id, "||302").await; // Must leave guild first
                return;
            }
            let is_caos = state.users.get(&conn_id)
                .map(|u| u.criminal || u.fuerzas_caos)
                .unwrap_or(false);
            if !is_caos {
                state.send_to(conn_id, "||239").await; // Not in chaos faction
                return;
            }
            if let Some(user) = state.users.get_mut(&conn_id) {
                user.criminal = false;
                user.fuerzas_caos = false;
                user.inventory[idx].amount -= 1;
                if user.inventory[idx].amount <= 0 {
                    user.inventory[idx] = InventorySlot::default();
                }
            }
            state.send_to(conn_id, "||355").await; // Faction changed
            send_inventory_slot(state, conn_id, idx).await;
            // Send updated status
            let cc = state.users.get(&conn_id).map(|u| u.build_cc_packet()).unwrap_or_default();
            let (map, x, y) = state.users.get(&conn_id).map(|u| (u.pos_map, u.pos_x, u.pos_y)).unwrap_or((0,0,0));
            if !cc.is_empty() {
                state.send_data(SendTarget::ToArea { map, x, y }, &cc).await;
            }
        }
        ObjType::RenounceRoyal => {
            // VB6: Renounce Royal faction
            let guild_idx = state.users.get(&conn_id).map(|u| u.guild_index).unwrap_or(0);
            if guild_idx > 0 {
                state.send_to(conn_id, "||302").await;
                return;
            }
            let is_armada = state.users.get(&conn_id)
                .map(|u| !u.criminal || u.armada_real)
                .unwrap_or(false);
            if !is_armada {
                state.send_to(conn_id, "||239").await;
                return;
            }
            if let Some(user) = state.users.get_mut(&conn_id) {
                user.criminal = true;
                user.armada_real = false;
                user.inventory[idx].amount -= 1;
                if user.inventory[idx].amount <= 0 {
                    user.inventory[idx] = InventorySlot::default();
                }
            }
            state.send_to(conn_id, "||355").await;
            send_inventory_slot(state, conn_id, idx).await;
            let cc = state.users.get(&conn_id).map(|u| u.build_cc_packet()).unwrap_or_default();
            let (map, x, y) = state.users.get(&conn_id).map(|u| (u.pos_map, u.pos_x, u.pos_y)).unwrap_or((0,0,0));
            if !cc.is_empty() {
                state.send_data(SendTarget::ToArea { map, x, y }, &cc).await;
            }
        }
        _ => {
            // Unhandled item types — inform user
        }
    }
}

/// Apply a consumable item's effects (potions, food).
/// VB6 TipoPocion: 1=agility, 2=strength, 3=HP, 4=mana, 5=cure poison, 6=remo (paralysis removal)
pub(super) fn apply_consumable(state: &mut GameState, conn_id: ConnectionId, obj: &crate::data::objects::ObjData) {
    if let Some(user) = state.users.get_mut(&conn_id) {
        let amount = if obj.max_modificador > obj.min_modificador {
            rand_range(obj.min_modificador, obj.max_modificador)
        } else {
            obj.min_modificador
        };

        match obj.tipo_pocion {
            1 => {
                // Agility potion — boost Agilidad (capped at 35), store backup for expiry
                if !user.tomo_pocion {
                    user.attributes_backup = user.attributes;
                }
                user.attributes[1] = (user.attributes[1] + amount).min(35);
                user.tomo_pocion = true;
                user.duracion_efecto = obj.duracion_efecto / 40; // ms → ticks (40ms each)
            }
            2 => {
                // Strength potion — boost Fuerza (capped at 35)
                if !user.tomo_pocion {
                    user.attributes_backup = user.attributes;
                }
                user.attributes[0] = (user.attributes[0] + amount).min(35);
                user.tomo_pocion = true;
                user.duracion_efecto = obj.duracion_efecto / 40;
            }
            3 => {
                // Red potion — HP restoration
                if amount > 0 {
                    user.min_hp = (user.min_hp + amount).min(user.max_hp);
                }
            }
            4 => {
                // Blue potion — Mana restoration (5% of max mana)
                let mana_restore = (user.max_mana as f64 * 0.05) as i32;
                let mana_restore = mana_restore.max(1);
                user.min_mana = (user.min_mana + mana_restore).min(user.max_mana);
            }
            5 => {
                // Purple potion — Cure poison
                user.poisoned = false;
            }
            6 => {
                // Remo potion — Remove paralysis (costs 60 HP, 3-round cooldown for non-warrior/hunter)
                // Handled separately in handle_use_item since it needs async and class checks
            }
            _ => {
                // Generic consumable (ObjType::UseOnce food items, etc.)
                // HP restoration
                if amount > 0 {
                    user.min_hp = (user.min_hp + amount).min(user.max_hp);
                }
            }
        }

        // Food/hunger restoration (applies to all subtypes)
        if obj.min_ham > 0 {
            user.min_ham = (user.min_ham + obj.min_ham).min(user.max_ham);
        }

        // Thirst restoration (applies to all subtypes)
        if obj.min_agua > 0 {
            user.min_agua = (user.min_agua + obj.min_agua).min(user.max_agua);
        }

        // Cure poison flag (for UseOnce items that have CuraVeneno=1 but no TipoPocion)
        if obj.cura_veneno && obj.tipo_pocion != 5 {
            user.poisoned = false;
        }
    }
}

/// AG — Pick up item from ground (stub — needs map item system).
pub(super) async fn handle_pick_up(state: &mut GameState, conn_id: ConnectionId) {
    let user_data = match state.users.get(&conn_id) {
        Some(u) if u.logged && !u.dead => (u.pos_map, u.pos_x, u.pos_y),
        _ => return,
    };
    let (map, x, y) = user_data;

    // Check if there's a ground item on the user's tile
    let ground_item = state.world.grid(map)
        .and_then(|g| g.tile(x, y))
        .map(|t| t.ground_item)
        .unwrap_or_default();

    if ground_item.obj_index <= 0 || ground_item.amount <= 0 {
        return;
    }

    // Check if the object is pickable (agarrable)
    let is_agarrable = state.get_object(ground_item.obj_index)
        .map(|o| o.agarrable)
        .unwrap_or(false);

    if !is_agarrable {
        let msg = format!("P|No puedes agarrar ese objeto{}", font_types::INFO);
        state.send_to(conn_id, &msg).await;
        return;
    }

    // Find free inventory slot
    let free_slot = {
        let user = match state.users.get(&conn_id) {
            Some(u) => u,
            None => return,
        };
        // First check if we can stack with an existing slot
        let mut stack_slot = None;
        let mut empty_slot = None;
        for i in 0..MAX_INVENTORY_SLOTS {
            if user.inventory[i].obj_index == ground_item.obj_index && user.inventory[i].amount > 0 {
                stack_slot = Some(i);
                break;
            }
            if user.inventory[i].obj_index == 0 && empty_slot.is_none() {
                empty_slot = Some(i);
            }
        }
        stack_slot.or(empty_slot)
    };

    let slot = match free_slot {
        Some(s) => s,
        None => {
            state.send_to(conn_id, "||108").await; // TEXTO108: No podes cargar mas objetos
            return;
        }
    };

    let obj_idx = ground_item.obj_index;
    let amount = ground_item.amount;

    // Remove item from ground
    {
        let grid = state.world.grid_mut(map);
        if let Some(tile) = grid.tile_mut(x, y) {
            tile.ground_item = world::GroundItem::default();
        }
    }

    // Broadcast BO (erase object) to area
    let bo_pkt = format!("BO{},{}", x, y);
    state.send_data(SendTarget::ToArea { map, x, y }, &bo_pkt).await;

    // Add to inventory
    if let Some(user) = state.users.get_mut(&conn_id) {
        if user.inventory[slot].obj_index == obj_idx {
            // Stack
            user.inventory[slot].amount += amount;
        } else {
            // New slot
            user.inventory[slot].obj_index = obj_idx;
            user.inventory[slot].amount = amount;
            user.inventory[slot].equipped = false;
        }
    }

    // Send updated inventory slot
    send_inventory_slot(state, conn_id, slot).await;

    // Get item name for notification
    let item_name = state.get_object(obj_idx)
        .map(|o| o.name.clone())
        .unwrap_or_else(|| format!("Item #{}", obj_idx));

    state.send_to(conn_id, &format!("||115@{}@{}", amount, item_name)).await; // TEXTO115: Recibiste %1 - %2
}

/// TI<slot>,<amount> — Drop item from inventory to ground.
pub(super) async fn handle_drop_item(state: &mut GameState, conn_id: ConnectionId, data: &str) {
    let payload = strip_opcode(data, 2);
    let slot_raw: i32 = read_field(1, payload, ',').parse().unwrap_or(0);
    let amount: i32 = read_field(2, payload, ',').parse().unwrap_or(1);

    if amount <= 0 {
        return;
    }

    // FLAGORO = -1 means drop gold
    if slot_raw == -1 {
        handle_drop_gold(state, conn_id, amount).await;
        return;
    }

    let slot = slot_raw as usize;
    if slot < 1 || slot > MAX_INVENTORY_SLOTS {
        return;
    }
    let idx = slot - 1;

    let user = match state.users.get(&conn_id) {
        Some(u) if u.logged && !u.dead => u,
        _ => return,
    };

    // GM anti-abuse: GMs (Consejero through Gran_Dios) cannot drop items.
    // Only regular users (0) and Director+ (>=9) are allowed.
    if user.privileges > 0 && user.privileges < 9 {
        let msg = format!("P|Los GMs no pueden tirar items{}", font_types::INFO);
        state.send_to(conn_id, &msg).await;
        return;
    }

    let obj_idx = user.inventory[idx].obj_index;
    let inv_amount = user.inventory[idx].amount;
    if obj_idx == 0 || inv_amount <= 0 {
        return;
    }

    let drop_amount = amount.min(inv_amount);
    let map = user.pos_map;
    let x = user.pos_x;
    let y = user.pos_y;

    // Check if target tile can hold the item (same item or empty)
    let can_place = if let Some(grid) = state.world.grid(map) {
        if let Some(tile) = grid.tile(x, y) {
            tile.ground_item.obj_index == 0 || tile.ground_item.obj_index == obj_idx
        } else {
            false
        }
    } else {
        false
    };

    if !can_place {
        state.send_to(conn_id, "||107").await; // TEXTO107: No hay espacio en el piso
        return;
    }

    // Get GrhIndex for the item
    let grh_index = state.get_object(obj_idx)
        .map(|o| o.grh_index)
        .unwrap_or(0);

    // Place on ground
    let is_new = {
        let grid = state.world.grid_mut(map);
        if let Some(tile) = grid.tile_mut(x, y) {
            if tile.ground_item.obj_index == obj_idx {
                // Stack on existing
                tile.ground_item.amount += drop_amount;
                false
            } else {
                // New item on tile
                tile.ground_item.obj_index = obj_idx;
                tile.ground_item.amount = drop_amount;
                true
            }
        } else {
            return;
        }
    };

    // Remove from inventory
    if let Some(user) = state.users.get_mut(&conn_id) {
        let inv = &mut user.inventory[idx];
        if drop_amount >= inv.amount {
            *inv = InventorySlot::default();
        } else {
            inv.amount -= drop_amount;
        }
    }

    send_inventory_slot(state, conn_id, idx).await;

    // Broadcast HO (show object) to area if new item on tile
    if is_new && grh_index > 0 {
        let ho_pkt = format!("HO{},{},{}", grh_index, x, y);
        state.send_data(SendTarget::ToArea { map, x, y }, &ho_pkt).await;
    }

    // Track for world cleanup (auto-remove after 10 ticks)
    clean_world_add_item(state, map, x, y, 10, obj_idx);
}

/// Drop gold from inventory (TI with slot=-1).
pub(super) async fn handle_drop_gold(state: &mut GameState, conn_id: ConnectionId, amount: i32) {
    let user = match state.users.get(&conn_id) {
        Some(u) if u.logged && !u.dead => u,
        _ => return,
    };

    if user.gold < amount as i64 || amount <= 0 {
        return;
    }

    // Deduct gold
    if let Some(user) = state.users.get_mut(&conn_id) {
        user.gold -= amount as i64;
    }
    send_stats_gold(state, conn_id).await;

    let msg = format!("P|Tiraste {} monedas de oro{}", amount, font_types::INFO);
    state.send_to(conn_id, &msg).await;
}

/// LC<x>,<y> — Left click on tile (look / inspect).
/// VB6: LookatTile (GameLogic.bas:505-1115) — packet handler wrapper.
pub(super) async fn handle_left_click(state: &mut GameState, conn_id: ConnectionId, data: &str) {
    let payload = strip_opcode(data, 2);
    let x: i32 = match read_field(1, payload, ',').parse() {
        Ok(v) => v,
        _ => return,
    };
    let y: i32 = match read_field(2, payload, ',').parse() {
        Ok(v) => v,
        _ => return,
    };
    do_lookat_tile(state, conn_id, x, y).await;
}

/// Core LookatTile logic (VB6: GameLogic.bas:505-1115).
/// Called from LC handler and WLC Magia handler (VB6 calls LookatTile before LanzarHechizo).
pub(super) async fn do_lookat_tile(state: &mut GameState, conn_id: ConnectionId, x: i32, y: i32) {
    let (map, user_x, user_y, my_privileges, my_survival_skill) = match state.users.get(&conn_id) {
        Some(u) if u.logged => (u.pos_map, u.pos_x, u.pos_y, u.privileges,
            u.skills.get(9).copied().unwrap_or(0)), // eSkill.Supervivencia = 9
        _ => return,
    };

    // VB6: flags.TargetMap/X/Y
    if let Some(user) = state.users.get_mut(&conn_id) {
        user.target_x = x;
        user.target_y = y;
        user.target_map = map;
    }

    // Range check
    if (x - user_x).abs() > world::MIN_X_BORDER || (y - user_y).abs() > world::MIN_Y_BORDER {
        return;
    }

    let is_gm = my_privileges >= crate::game::types::privilege_level::SEMIDIOS;
    let is_user = my_privileges == 0;
    let mut found_something = false;
    let mut found_char: u8 = 0; // 0=none, 1=user, 2=npc
    let mut temp_char_index_user: ConnectionId = 0;
    let mut temp_char_index_npc: usize = 0;

    // ========== OBJECT / DOOR DETECTION (VB6 lines 520-759) ==========
    // Check exact tile first, then nearby tiles for doors
    let obj_tile = state.world.grid(map).and_then(|g| g.tile(x, y))
        .map(|t| t.ground_item.obj_index).unwrap_or(0);
    if obj_tile > 0 {
        // Set target obj
        if let Some(user) = state.users.get_mut(&conn_id) {
            user.target_obj_map = map;
            user.target_obj_x = x;
            user.target_obj_y = y;
        }
        found_something = true;
        // Display object info
        let obj_info = state.get_object(obj_tile).map(|o| (o.name.clone(), o.index));
        if let Some((name, idx)) = obj_info {
            let amount = state.world.grid(map).and_then(|g| g.tile(x, y))
                .map(|t| t.ground_item.amount).unwrap_or(0);
            let msg = if !is_user {
                if amount > 1 {
                    format!("N|{} - {} - {}~69~190~156", name, amount, idx)
                } else {
                    format!("N|{} - {}~69~190~156", name, idx)
                }
            } else {
                if amount > 1 {
                    format!("N|{} - {}~69~190~156", name, amount)
                } else {
                    format!("N|{}~69~190~156", name)
                }
            };
            state.send_to(conn_id, &msg).await;
        }
    }

    // ========== CHARACTER DETECTION (VB6 lines 779-800) ==========
    // VB6 checks Y+1 FIRST, then Y for chars
    if found_char == 0 {
        for &check_y in &[y + 1, y] {
            if check_y < 1 || check_y > 100 { continue; }
            // Check for user
            if found_char == 0 {
                let tile_user = state.world.grid(map)
                    .and_then(|g| g.tile(x, check_y))
                    .and_then(|t| t.user_conn);
                if let Some(tc) = tile_user {
                    if state.users.get(&tc).map(|u| u.logged).unwrap_or(false) {
                        temp_char_index_user = tc;
                        found_char = 1;
                    }
                }
            }
            // Check for NPC on same tile
            if found_char == 0 {
                let tile_npc = state.world.grid(map)
                    .and_then(|g| g.tile(x, check_y))
                    .map(|t| t.npc_index)
                    .unwrap_or(0);
                if tile_npc > 0 {
                    temp_char_index_npc = tile_npc as usize;
                    found_char = 2;
                }
            }
            if found_char != 0 { break; }
        }
    }

    // ========== USER DISPLAY (VB6 lines 807-981) — EXACT REPLICA ==========
    if found_char == 1 {
        let target = temp_char_index_user;
        let info = state.users.get(&target).map(|t| {
            (t.char_name.clone(), t.level, t.guild_index, t.min_hp, t.max_hp,
             t.dead, t.privileges, t.criminal, t.armada_real, t.fuerzas_caos,
             t.desc.clone(), t.char_index.0,
             t.recompensas_real, t.recompensas_caos)
        });

        if let Some((name, level, guild_idx, min_hp, max_hp, dead, priv_target,
                      criminal, armada, caos, desc, char_idx,
                      recomp_real, recomp_caos)) = info {

            let mut stat = String::new();
            let limite_newbie = 9;

            // VB6: EsNewbie tag
            if level <= limite_newbie {
                stat.push_str(" <NEWBIE>");
            }

            // VB6: Guild tag
            if guild_idx > 0 {
                if let Some(guild_name) = state.get_guild_name(guild_idx) {
                    stat.push_str(&format!(" <{}>", guild_name));
                }
            }

            // "Ves a <name><tags>"
            stat = format!("Ves a {}{}", name, stat);

            // VB6: GM info
            if my_privileges > 0 {
                stat.push_str(&format!(" <UI:{}>", char_idx));
                stat.push_str(&format!(" ({}/{})", min_hp, max_hp));
            }

            // VB6: Faction tags with titles
            if armada {
                let titulo = titulo_real(recomp_real);
                stat.push_str(&format!(" <Alianza Imperial> <{}>", titulo));
            } else if caos {
                let titulo = titulo_caos(recomp_caos);
                stat.push_str(&format!(" <Horda Infernal> <{}>", titulo));
            }

            // VB6: Description
            if desc.len() > 1 {
                stat.push_str(&format!(" - {}", desc));
            }

            // VB6: Health status in brackets (lines 863-876)
            if priv_target >= crate::game::types::privilege_level::ADMINISTRADOR {
                stat.push_str(" [Creator]");
            } else if priv_target > 0 {
                stat.push_str(" [Inmortal]");
            } else if dead {
                stat.push_str(" [Muerto]");
            } else if min_hp < ((max_hp as f64 * 0.2) as i32) {
                stat.push_str(" [Agonizando]");
            } else if min_hp < ((max_hp as f64 * 0.45) as i32) {
                stat.push_str(" [Gravemente herido]");
            } else if min_hp < ((max_hp as f64 * 0.75) as i32) {
                stat.push_str(" [Medio herido]");
            } else if min_hp < max_hp {
                stat.push_str(" [Algo lastimado]");
            } else {
                stat.push_str(" [Intacto]");
            }

            // VB6: Color coding by privilege hierarchy (lines 895-956)
            if priv_target > 11 {
                stat.push_str(" <Administrador> ~255~255~255~1~0");
            } else if priv_target > 10 {
                stat.push_str(" <Sub Administrador> ~255~198~0~1~0");
            } else if priv_target > 9 {
                stat.push_str(" <Developer> ~128~255~128~1~0");
            } else if priv_target > 8 {
                stat.push_str(" <Director de Game Master> ~123~155~0~1~0");
            } else if priv_target > 7 {
                stat.push_str(" <Game Master> <Gran Dios> ~0~225~128~1~0");
            } else if priv_target > 3 {
                stat.push_str(" <Game Master> <Dios> ~120~250~250~1~0");
            } else if priv_target > 2 {
                stat.push_str(" <Event Master> ~128~128~64~1~0");
            } else if priv_target > 1 {
                stat.push_str(" <Game Master> <Semi Dios> ~0~170~190~1~0");
            } else if priv_target > 0 {
                stat.push_str(" <Game Master> <Consejero> ~0~185~0~1~0");
            } else if level <= limite_newbie {
                stat.push_str(" ~255~255~202~1~0");
            } else if armada {
                stat.push_str(" ~0~128~255~1~0");
            } else if caos {
                stat.push_str(" ~255~0~0~1~0");
            } else if criminal {
                stat.push_str(" <CRIMINAL> ~255~0~0~1~0");
            } else {
                // Ciudadano (default)
                stat.push_str(" <CIUDADANO> ~0~128~255~1~0");
            }

            let msg = format!("N|{}", stat);
            state.send_to(conn_id, &msg).await;

            found_something = true;
            if let Some(user) = state.users.get_mut(&conn_id) {
                user.target_user = target;
                user.target_npc = 0;
            }
        }
    }

    // ========== NPC DISPLAY (VB6 lines 983-1083) — EXACT REPLICA ==========
    if found_char == 2 {
        let npc_idx = temp_char_index_npc;
        let npc_data = state.get_npc(npc_idx).map(|npc| {
            (npc.is_alive(), npc.name.clone(), npc.desc.clone(),
             npc.char_index, npc.min_hp, npc.max_hp, npc.npc_number,
             npc.npc_type)
        });

        if let Some((alive, npc_name, npc_desc, npc_char_index, npc_min_hp, npc_max_hp, npc_num, _npc_type)) = npc_data {
            if alive {
                // VB6: GM gets detailed NPC info (line 987)
                if my_privileges > 0 {
                    let gm_msg = format!("N|Nombre : {} /  Vida : {}/{} Numero de NPC : {}~255~113~255~0~0",
                        npc_name, npc_min_hp, npc_max_hp, npc_num);
                    state.send_to(conn_id, &gm_msg).await;
                }

                // VB6: Health status based on Survival skill (lines 993-1036)
                let estatus = if is_gm {
                    format!("{}/{}", npc_min_hp, npc_max_hp)
                } else {
                    npc_health_by_survival(npc_min_hp, npc_max_hp, my_survival_skill)
                };

                // VB6: NPC display (lines 1038-1076)
                if npc_desc.len() > 1 {
                    // GM gets extra info line before desc
                    if is_gm {
                        let gm_extra = format!("N|Nombre: {} Vida: {}/{} Numero de NPC: {} Indice: {}~255~83~255~0~0",
                            npc_name, npc_min_hp, npc_max_hp, npc_num, npc_idx);
                        state.send_to(conn_id, &gm_extra).await;
                    }
                    // Speech bubble with desc
                    let sep = "\u{00B0}"; // chr(176) = °
                    let msg = format!("N|16777215{}{}{}{}{}", sep, npc_desc, sep, npc_char_index.0, font_types::INFO);
                    state.send_to(conn_id, &msg).await;
                } else {
                    // No desc → show name + health status
                    let msg = format!("||674@{}@{}", npc_name, estatus);
                    state.send_to(conn_id, &msg).await;
                }

                found_something = true;
                if let Some(user) = state.users.get_mut(&conn_id) {
                    user.target_npc_idx = npc_idx;
                    user.target_npc = npc_idx;
                    user.target_user = 0;
                }
            }
        }
    }

    // ========== CLEANUP (VB6 lines 1085-1115) ==========
    if found_char == 0 {
        if let Some(user) = state.users.get_mut(&conn_id) {
            user.target_npc = 0;
            user.target_npc_idx = 0;
            user.target_user = 0;
        }
    }
    if !found_something {
        if let Some(user) = state.users.get_mut(&conn_id) {
            user.target_npc = 0;
            user.target_npc_idx = 0;
            user.target_user = 0;
        }
    }
}

/// VB6 TituloReal (ModFacciones.bas:369)
pub(super) fn titulo_real(recompensas: i32) -> &'static str {
    match recompensas {
        0 | 1 => "Servidor del Rey",
        2 => "Soldado Imperial",
        3 => "Protector del Imperio",
        4 => "Maestro de la Luz",
        5 => "Caballero de la Luz",
        _ => "Servidor del Rey",
    }
}

/// VB6 TituloCaos (ModFacciones.bas:701)
pub(super) fn titulo_caos(recompensas: i32) -> &'static str {
    match recompensas {
        0 | 1 => "Servidor del Demonio",
        2 => "Mercenario de la Oscuridad",
        3 => "General de los Infiernos",
        4 => "Maestro de la Oscuridad",
        5 => "Caballero de la Oscuridad",
        _ => "Servidor del Demonio",
    }
}

/// VB6 NPC health status based on Survival skill (GameLogic.bas:993-1036).
pub(super) fn npc_health_by_survival(min_hp: i32, max_hp: i32, survival_skill: i32) -> String {
    if max_hp <= 0 { return "Intacto".to_string(); }
    if survival_skill >= 60 {
        return format!("{}/{}", min_hp, max_hp);
    }
    let ratio = min_hp as f64 / max_hp as f64;
    if survival_skill >= 40 {
        if ratio < 0.05 { "Agonizando".to_string() }
        else if ratio < 0.10 { "Casi muerto".to_string() }
        else if ratio < 0.25 { "Muy Malherido".to_string() }
        else if ratio < 0.50 { "Herido".to_string() }
        else if ratio < 0.75 { "Levemente herido".to_string() }
        else if ratio < 1.0 { "Sano".to_string() }
        else { "Intacto".to_string() }
    } else if survival_skill > 30 {
        if ratio < 0.25 { "Muy malherido".to_string() }
        else if ratio < 0.50 { "Herido".to_string() }
        else if ratio < 0.75 { "Levemente herido".to_string() }
        else { "Sano".to_string() }
    } else if survival_skill > 20 {
        if ratio < 0.50 { "Malherido".to_string() }
        else if ratio < 0.75 { "Herido".to_string() }
        else { "Sano".to_string() }
    } else if survival_skill > 10 {
        if ratio < 0.50 { "Herido".to_string() }
        else { "Sano".to_string() }
    } else {
        "Dudoso".to_string()
    }
}

/// RC<x>,<y> — Right click on tile (interact / context menu).
/// VB6 equivalent: Accion() in Acciones.bas — handles doors, NPCs, users, items.
pub(super) async fn handle_right_click(state: &mut GameState, conn_id: ConnectionId, data: &str) {
    let payload = strip_opcode(data, 2);
    let x: i32 = match read_field(1, payload, ',').parse() {
        Ok(v) => v,
        _ => return,
    };
    let y: i32 = match read_field(2, payload, ',').parse() {
        Ok(v) => v,
        _ => return,
    };

    let (map, user_x, user_y, privileges, dead) = match state.users.get(&conn_id) {
        Some(u) if u.logged => (u.pos_map, u.pos_x, u.pos_y, u.privileges, u.dead),
        _ => return,
    };

    // Range check
    if (x - user_x).abs() > world::MIN_X_BORDER || (y - user_y).abs() > world::MIN_Y_BORDER {
        // RC out of range — don't log
        return;
    }

    // Save target coordinates
    if let Some(user) = state.users.get_mut(&conn_id) {
        user.target_x = x;
        user.target_y = y;
        user.target_map = map;
    }

    // NOTE: Spells are NOT cast from right-click. They are cast from WLC (Work Left Click)
    // with skill_type = MAGIA (2). The right-click only does LookatTile (inspect).

    // Gather tile data without holding borrows
    let tile_data = state.world.grid(map).and_then(|g| g.tile(x, y)).map(|t| {
        (t.user_conn, t.npc_index, t.ground_item.obj_index, t.ground_item.amount)
    });
    let (tile_user, mut tile_npc_idx, tile_obj_idx, _tile_obj_amt) = match tile_data {
        Some(d) => d,
        None => { return; }
    };

    // Also check y-1 for NPC (character heads are above their tile position)
    if tile_npc_idx == 0 && y - 1 >= 1 {
        if let Some(npc_on_ym1) = state.world.grid(map).and_then(|g| g.tile(x, y - 1)).map(|t| t.npc_index) {
            if npc_on_ym1 > 0 {
                tile_npc_idx = npc_on_ym1;
            }
        }
    }

    // RC click — don't log (too frequent)

    // Also check y+1 for NPC (VB6: Acciones.bas checks MapData(Map, X, Y).NpcIndex AND
    // MapData(Map, X, Y+1).NpcIndex for NPC interactions via LookatTile)
    if tile_npc_idx == 0 && y + 1 <= 100 {
        if let Some(npc_on_y1) = state.world.grid(map).and_then(|g| g.tile(x, y + 1)).map(|t| t.npc_index) {
            if npc_on_y1 > 0 {
                tile_npc_idx = npc_on_y1;
            }
        }
    }

    // 1. Check for DOOR on tile (VB6: AccionParaPuerta — otPuertas=6)
    // Also check the clicked tile's ground object in static map data
    let ground_obj = get_map_tile_obj(state, map, x, y);
    if ground_obj > 0 {
        if let Some(obj) = state.get_object(ground_obj) {
            if obj.obj_type == crate::data::objects::ObjType::Door {
                accion_para_puerta(state, conn_id, map, x, y, ground_obj).await;
                return;
            }
        }
    }
    // Also check adjacent tiles for doors (VB6: Accion() checks x-1, x-2, x+1, x+2)
    // x-1: only for PuertaDoble or Porton doors
    for dx in [-1i32] {
        let ax = x + dx;
        if ax < 1 || ax > 100 { continue; }
        let adj_obj = get_map_tile_obj(state, map, ax, y);
        if adj_obj > 0 {
            if let Some(obj) = state.get_object(adj_obj) {
                if obj.obj_type == crate::data::objects::ObjType::Door
                    && (obj.puerta_doble == 1 || obj.porton == 1) {
                    accion_para_puerta(state, conn_id, map, ax, y, adj_obj).await;
                    return;
                }
            }
        }
    }
    // x-2: only for Porton doors
    for dx in [-2i32] {
        let ax = x + dx;
        if ax < 1 || ax > 100 { continue; }
        let adj_obj = get_map_tile_obj(state, map, ax, y);
        if adj_obj > 0 {
            if let Some(obj) = state.get_object(adj_obj) {
                if obj.obj_type == crate::data::objects::ObjType::Door
                    && obj.porton == 1 {
                    accion_para_puerta(state, conn_id, map, ax, y, adj_obj).await;
                    return;
                }
            }
        }
    }
    // x+1: any door type
    for dx in [1i32] {
        let ax = x + dx;
        if ax < 1 || ax > 100 { continue; }
        let adj_obj = get_map_tile_obj(state, map, ax, y);
        if adj_obj > 0 {
            if let Some(obj) = state.get_object(adj_obj) {
                if obj.obj_type == crate::data::objects::ObjType::Door {
                    accion_para_puerta(state, conn_id, map, ax, y, adj_obj).await;
                    return;
                }
            }
        }
    }
    // x+2: only for PuertaDoble or Porton doors (VB6 line 93-99)
    for dx in [2i32] {
        let ax = x + dx;
        if ax < 1 || ax > 100 { continue; }
        let adj_obj = get_map_tile_obj(state, map, ax, y);
        if adj_obj > 0 {
            if let Some(obj) = state.get_object(adj_obj) {
                if obj.obj_type == crate::data::objects::ObjType::Door
                    && (obj.puerta_doble == 1 || obj.porton == 1) {
                    accion_para_puerta(state, conn_id, map, ax, y, adj_obj).await;
                    return;
                }
            }
        }
    }

    // 2. Check for USER on tile → send MENU packet
    if let Some(target_conn) = tile_user {
        if target_conn != conn_id {
            let target_info = state.users.get(&target_conn).and_then(|t| {
                if t.logged && !t.admin_invisible {
                    Some(t.char_name.clone())
                } else {
                    None
                }
            });
            if let Some(target_name) = target_info {
                if let Some(user) = state.users.get_mut(&conn_id) {
                    user.target_user = target_conn;
                }
                let menu_pkt = format!("MENU{},{}", target_name, privileges);
                state.send_to(conn_id, &menu_pkt).await;
                return;
            }
        }
    }

    // 3. Check for NPC on tile — type-specific interaction
    if tile_npc_idx > 0 {
        let npc_idx = tile_npc_idx as usize;
        let npc_info = state.get_npc(npc_idx).map(|npc| {
            (npc.is_alive(), npc.comercia, npc.name.clone(), npc.npc_type, npc.desc.clone(), npc.npc_number)
        });

        if let Some((alive, comercia, npc_name, npc_type, npc_desc, npc_num)) = npc_info {
            if alive {
                // Set target NPC
                if let Some(user) = state.users.get_mut(&conn_id) {
                    user.target_npc = npc_idx;
                }

                use crate::data::npcs::NpcType;
                let dist = (x - user_x).abs().max((y - user_y).abs());

                // VB6 Accion() does NOT show NPC name/desc — that's only LookatTile (LC).
                // RC just triggers the action (commerce, bank, revive, etc.)

                // VB6 Accion(): First check Comercia, then check NPCtype
                // Commerce takes priority over type (VB6 line 135)
                if comercia {
                    if dead { state.send_to(conn_id, "||3").await; return; }
                    if dist > 6 {
                        state.send_to(conn_id, "||13").await; return;
                    }
                    iniciar_comercio_npc(state, conn_id, npc_idx).await;
                } else {
                    match npc_type {
                        NpcType::Banker => {
                            if dead { state.send_to(conn_id, "||3").await; return; }
                            if dist > 10 {
                                state.send_to(conn_id, "||13").await; return;
                            }
                            iniciar_banco(state, conn_id).await;
                        }
                        NpcType::BoveClan => {
                            if dead { state.send_to(conn_id, "||3").await; return; }
                            if dist > 5 {
                                state.send_to(conn_id, "||10").await; return;
                            }
                            iniciar_clan_banco(state, conn_id).await;
                        }
                        NpcType::Traveler => {
                            if dead { state.send_to(conn_id, "||3").await; return; }
                            if dist > 5 {
                                state.send_to(conn_id, "||13").await; return;
                            }
                            state.send_to(conn_id, "TRAVELS").await;
                        }
                        NpcType::Quest | NpcType::QuestNoble => {
                            if dead { state.send_to(conn_id, "||3").await; return; }
                            if dist > 10 {
                                state.send_to(conn_id, "||10").await; return;
                            }
                            state.send_to(conn_id, "DAMEQUEST").await;
                        }
                        NpcType::Reviver => {
                            // VB6 Acciones.bas:408-422 — Revividor NPC
                            // Distance check: <= 10 tiles
                            if dist > 10 {
                                state.send_to(conn_id, "||12").await; return;
                            }

                            // If dead: revive first
                            if dead {
                                revive_user(state, conn_id).await;
                            }

                            // Always full-heal + cure poison (VB6: MinHP=MaxHP, Envenenado=False)
                            if let Some(user) = state.users.get_mut(&conn_id) {
                                user.min_hp = user.max_hp;
                                user.poisoned = false;
                            }
                            send_stats_hp(state, conn_id).await;
                        }
                        NpcType::Trainer => {
                            if dead { state.send_to(conn_id, "||3").await; return; }
                            if dist > 10 {
                                state.send_to(conn_id, "||13").await; return;
                            }
                            let msg = format!("{}Habla con el entrenador usando el chat.{}", server_opcodes::CONSOLE_MSG, font_types::INFO);
                            state.send_to(conn_id, &msg).await;
                        }
                        NpcType::Surgeon => {
                            if dist > 10 {
                                state.send_to(conn_id, "||13").await; return;
                            }
                            if let Some(user) = state.users.get_mut(&conn_id) {
                                if user.poisoned {
                                    user.poisoned = false;
                                    let msg = format!("{}El cirujano te ha curado el veneno.{}", server_opcodes::CONSOLE_MSG, font_types::INFO);
                                    state.send_to(conn_id, &msg).await;
                                } else {
                                    let msg = format!("{}No necesitas curacion.{}", server_opcodes::CONSOLE_MSG, font_types::INFO);
                                    state.send_to(conn_id, &msg).await;
                                }
                            }
                        }
                        NpcType::Mail => {
                            // VB6: Correos (type 23) — opens mail form
                            if dead { state.send_to(conn_id, "||3").await; return; }
                            if dist > 5 {
                                state.send_to(conn_id, "||13").await; return;
                            }
                            state.send_to(conn_id, "CORREO").await;
                        }
                        NpcType::Citizenship => {
                            // VB6: Ciudadania (type 13)
                            if dead { state.send_to(conn_id, "||3").await; return; }
                            if dist > 5 {
                                state.send_to(conn_id, "||13").await; return;
                            }
                            let msg = format!("P|Habla conmigo para cambiar tu ciudadania. Escribe /CIUDADANO para convertirte en ciudadano o /CRIMINAL para renunciar.{}", font_types::INFO);
                            state.send_to(conn_id, &msg).await;
                        }
                        NpcType::HouseSeller => {
                            // VB6: ShowCasas (type 15) — MFC packet
                            if dead { state.send_to(conn_id, "||3").await; return; }
                            state.send_to(conn_id, "MFC").await;
                        }
                        NpcType::Arena => {
                            // VB6: Arenas (type 16) — MAR packet with duel names
                            if dead { state.send_to(conn_id, "||3").await; return; }
                            // Build tournament list
                            let mut names = Vec::new();
                            for i in 0..8 {
                                if let Some(name) = state.nombre_dueleando.get(i) {
                                    names.push(name.clone());
                                } else {
                                    names.push(String::new());
                                }
                            }
                            let mar_pkt = format!("MAR{}", names.join(","));
                            state.send_to(conn_id, &mar_pkt).await;
                        }
                        NpcType::GodNpc => {
                            // VB6: NpcDioses (type 18)
                            if dead { state.send_to(conn_id, "||3").await; return; }
                            if dist > 3 {
                                state.send_to(conn_id, "||14").await; return;
                            }
                            let msg = format!("P|Acercate mas para hablar con los dioses.{}", font_types::INFO);
                            state.send_to(conn_id, &msg).await;
                        }
                        NpcType::Bargomaud => {
                            // VB6: NpcBargomaud (type 20) — check level >= 55, warp to 161,50,53
                            if dead { state.send_to(conn_id, "||3").await; return; }
                            if dist > 5 {
                                state.send_to(conn_id, "||14").await; return;
                            }
                            let level = state.users.get(&conn_id).map(|u| u.level).unwrap_or(0);
                            if level < 55 {
                                state.send_to(conn_id, "||643").await; return;
                            }
                            warp_user(state, conn_id, 161, 50, 53).await;
                            let name = state.users.get(&conn_id).map(|u| u.char_name.clone()).unwrap_or_default();
                            state.send_to(conn_id, &format!("||651@{}", name)).await;
                        }
                        NpcType::QuintaJera => {
                            // VB6: QuintaJera (type 21) — faction rewards
                            if dead { state.send_to(conn_id, "||3").await; return; }
                            if dist > 5 {
                                state.send_to(conn_id, "||13").await; return;
                            }
                            let msg = format!("P|Usa los comandos /RECOMPENSA y /ENLISTAR para interactuar.{}", font_types::INFO);
                            state.send_to(conn_id, &msg).await;
                        }
                        NpcType::BoxDelivery => {
                            // VB6: EntregaCajas (type 24)
                            if dead { state.send_to(conn_id, "||3").await; return; }
                            if dist > 5 {
                                state.send_to(conn_id, "||13").await; return;
                            }
                            let msg = format!("P|Trae las cajas de quest para recibir tu recompensa.{}", font_types::INFO);
                            state.send_to(conn_id, &msg).await;
                        }
                        _ => {
                            // Non-interactive NPC — description already shown above
                        }
                    }
                }
                return;
            }
        }
    }

    // 4. Ground item interaction
    if tile_obj_idx > 0 {
        if let Some(obj) = state.get_object(tile_obj_idx) {
            let sele_pkt = format!("SELE{},{},OBJ", obj.obj_type as i32, obj.name);
            state.send_to(conn_id, &sele_pkt).await;
        }
    }
}

/// Get the ground object index from static map data at a given position.
/// Get the trigger type for a map tile.
// get_map_tile_trigger — moved to common.rs
// get_map_tile_obj — moved to common.rs

/// Handle door interaction (VB6: AccionParaPuerta in Acciones.bas).
/// Opens/closes doors, handles locks, updates tile blocking and graphics.
/// Sends BQ packets to notify clients about blocking changes.
pub(super) async fn accion_para_puerta(state: &mut GameState, conn_id: ConnectionId, map: i32, x: i32, y: i32, obj_index: i32) {
    let (user_x, user_y) = match state.users.get(&conn_id) {
        Some(u) => (u.pos_x, u.pos_y),
        None => return,
    };

    // Distance check: must be within 3 tiles (VB6: Distance > 3)
    if (x - user_x).abs() > 3 || (y - user_y).abs() > 3 {
        state.send_to(conn_id, "||10").await;
        return;
    }

    let obj = match state.get_object(obj_index) {
        Some(o) => o.clone(),
        None => return,
    };

    // Check if door needs a key (VB6: Llave = 0 means no key needed)
    if obj.llave == 1 {
        // Check if user has the matching key in inventory
        let has_key = state.users.get(&conn_id).map(|u| {
            u.inventory.iter().any(|s| {
                if s.obj_index <= 0 { return false; }
                state.get_object(s.obj_index)
                    .map(|ko| ko.obj_type == crate::data::objects::ObjType::Key && ko.clave == obj.clave)
                    .unwrap_or(false)
            })
        }).unwrap_or(false);

        if !has_key {
            state.send_to(conn_id, "||652").await;
            return;
        }
    }

    if obj.cerrada == 1 {
        // Door is CLOSED → open it

        // RejaForta (fortress gate) — guild permission check
        if obj.reja_forta == 1 {
            if obj_index == 1472 { return; } // Hardcoded locked gate
            let guild_idx = state.users.get(&conn_id).map(|u| u.guild_index).unwrap_or(0);
            if guild_idx <= 0 { return; }
            // Only the guild owning the fortress can toggle (siege_guild_owner)
            if guild_idx != state.siege_guild_owner { return; }
        }

        let new_obj_idx = obj.index_abierta;
        if new_obj_idx <= 0 { return; }

        // VB6: Change ObjIndex FIRST, then read the NEW object's properties
        set_map_tile_obj(state, map, x, y, new_obj_idx as i16);

        // Send HO packet with the NEW object's graphic (VB6: after changing ObjIndex)
        let new_grh = state.get_object(new_obj_idx).map(|o| o.grh_index).unwrap_or(0);
        let ho_pkt = format!("HO{},{},{}", new_grh, x, y);
        state.send_data(SendTarget::ToArea { map, x, y }, &ho_pkt).await;

        // Read door type from the NEW object (VB6 reads after ObjIndex change)
        let new_obj = state.get_object(new_obj_idx).cloned();
        let is_puerta_doble = new_obj.as_ref().map(|o| o.puerta_doble == 1).unwrap_or(false);
        let is_porton = new_obj.as_ref().map(|o| o.porton == 1).unwrap_or(false);

        // Determine tiles to unblock based on door type
        let tiles: Vec<i32> = if obj.reja_forta == 1 || is_porton {
            vec![x, x - 1, x - 2, x + 1, x + 2]
        } else if is_puerta_doble {
            vec![x, x - 1, x + 1, x + 2]
        } else {
            vec![x, x - 1]
        };

        // Unblock tiles and send BQ packets to entire map (VB6: Bloquear SendTarget.toMap)
        for tx in &tiles {
            set_map_tile_blocked(state, map, *tx, y, false);
            let bq_pkt = format!("BQ{},{},{}", tx, y, 0);
            state.send_data(SendTarget::ToMap(map), &bq_pkt).await;
        }

        // Play door sound (VB6: SND_PUERTA)
        let snd_pkt = format!("TW{}", 45);
        state.send_data(SendTarget::ToArea { map, x, y }, &snd_pkt).await;
    } else {
        // Door is OPEN → close it

        // RejaForta (fortress gate) — guild permission check
        if obj.reja_forta == 1 {
            if obj_index == 1472 { return; }
            let guild_idx = state.users.get(&conn_id).map(|u| u.guild_index).unwrap_or(0);
            if guild_idx <= 0 { return; }
            if guild_idx != state.siege_guild_owner { return; }
        }

        let new_obj_idx = obj.index_cerrada;
        if new_obj_idx <= 0 { return; }

        // VB6: Change ObjIndex FIRST, then read the NEW (closed) object's properties
        set_map_tile_obj(state, map, x, y, new_obj_idx as i16);

        // Send HO packet with the NEW object's graphic
        let closed_obj = state.get_object(new_obj_idx).cloned();
        let new_grh = closed_obj.as_ref().map(|o| o.grh_index).unwrap_or(0);
        let ho_pkt = format!("HO{},{},{}", new_grh, x, y);
        state.send_data(SendTarget::ToArea { map, x, y }, &ho_pkt).await;

        // Read door type from the NEW (closed) object
        let is_puerta_doble = closed_obj.as_ref().map(|o| o.puerta_doble == 1).unwrap_or(false);
        let is_porton = closed_obj.as_ref().map(|o| o.porton == 1).unwrap_or(false);

        // Determine tiles to block based on door type
        let tiles: Vec<i32> = if obj.reja_forta == 1 || is_porton {
            vec![x, x - 1, x - 2, x + 1, x + 2]
        } else if is_puerta_doble {
            vec![x, x - 1, x + 1, x + 2]
        } else {
            vec![x, x - 1]
        };

        // Block tiles and send BQ packets to entire map
        for tx in &tiles {
            set_map_tile_blocked(state, map, *tx, y, true);
            let bq_pkt = format!("BQ{},{},{}", tx, y, 1);
            state.send_data(SendTarget::ToMap(map), &bq_pkt).await;
        }

        // Play door sound
        let snd_pkt = format!("TW{}", 45);
        state.send_data(SendTarget::ToArea { map, x, y }, &snd_pkt).await;
    }

    // VB6: Set TargetObj position (after toggle)
    if let Some(user) = state.users.get_mut(&conn_id) {
        user.target_obj_map = map;
        user.target_obj_x = x;
        user.target_obj_y = y;
    }
}

// set_map_tile_obj, set_map_tile_blocked, health_description — moved to common.rs

/// SEG — Toggle PvP safety.
pub(super) async fn handle_safe_toggle(state: &mut GameState, conn_id: ConnectionId) {
    let safe = match state.users.get(&conn_id) {
        Some(u) if u.logged => u.safe_toggle,
        _ => return,
    };

    if let Some(user) = state.users.get_mut(&conn_id) {
        user.safe_toggle = !safe;
    }

    if safe {
        state.send_to(conn_id, server_opcodes::SAFE_OFF).await;
    } else {
        state.send_to(conn_id, server_opcodes::SAFE_ON).await;
    }
}
