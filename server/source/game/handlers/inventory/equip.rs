//! Inventory equip/unequip handlers.
//! Split from inventory.rs for file size management.

use crate::data::objects::ObjType;
use crate::game::constants::*;
use crate::game::handlers::{build_anm_packet, naked_body, send_inventory_slot};
use crate::game::types::{GameState, MAX_INVENTORY_SLOTS, SendTarget, privilege_level};
use crate::net::ConnectionId;
use crate::protocol::binary_packets;
use crate::protocol::font_index;

/// EQUI<slot> — Equip/unequip item from inventory slot.
pub(crate) async fn handle_equip(state: &mut GameState, conn_id: ConnectionId, slot: usize) {
    if slot < 1 || slot > MAX_INVENTORY_SLOTS {
        return;
    }
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

    let mut aura_changed = false;

    if currently_equipped {
        // VB6: Tools that are equipped and double-clicked trigger work actions
        // instead of unequipping (InvUsuario.bas Tool case).
        if obj_data.obj_type == ObjType::Tool {
            // Tool constants imported from crate::game::constants

            match obj_index {
                CANA_PESCA | CANA_PESCA_NEWBIE => {
                    // Send WorkRequestTarget(Pesca) — client picks a target tile
                    let pkt = binary_packets::write_work_request_target(13); // Pesca=13
                    state.send_bytes(conn_id, &pkt);
                }
                HACHA_LENADOR | HACHA_LENA_ELFICA | HACHA_LENADOR_NEWBIE => {
                    let pkt = binary_packets::write_work_request_target(10); // Talar=10
                    state.send_bytes(conn_id, &pkt);
                }
                PIQUETE_MINERO | PIQUETE_MINERO_NEWBIE => {
                    let pkt = binary_packets::write_work_request_target(14); // Mineria=14
                    state.send_bytes(conn_id, &pkt);
                }
                MARTILLO_HERRERO | MARTILLO_HERRERO_NEWBIE => {
                    let pkt = binary_packets::write_work_request_target(16); // Herreria=16
                    state.send_bytes(conn_id, &pkt);
                }
                SERRUCHO_CARPINTERO | SERRUCHO_CARPINTERO_NEWBIE => {
                    // Carpenter opens directly (no target needed)
                    crate::game::handlers::skills::do_carpinteria(state, conn_id).await;
                }
                _ => {
                    // Other tools: if SkHerreria > 0, trigger FundirMetal
                    if obj_data.sk_herreria > 0 {
                        let pkt = binary_packets::write_work_request_target(88); // FundirMetal=88
                        state.send_bytes(conn_id, &pkt);
                    }
                }
            }
            return;
        }

        // Unequip
        // Check if unequipping clears an aura
        let had_aura = obj_data.crea_aura > 0;
        unequip_slot(state, conn_id, idx, &obj_data.obj_type);
        if had_aura {
            aura_changed = true;
        }
    } else {
        // Item restriction checks (VB6: InvUsuario.bas)
        let (user_level, user_class, user_privileges, user_criminal, user_armada, user_caos) =
            match state.users.get(&conn_id) {
                Some(u) => (
                    u.level,
                    u.class,
                    u.privileges,
                    u.criminal,
                    u.armada_real,
                    u.fuerzas_caos,
                ),
                None => return,
            };

        // VB6: GMs (>= Semidios) bypass ALL equipment restrictions
        let is_gm = user_privileges >= privilege_level::SEMIDIOS;

        // VB6 parity: newbie items can only be used by players level <= 12
        if obj_data.newbie && user_level > 12 && !is_gm {
            state.send_console(
                conn_id,
                "Solo los newbies pueden usar este objeto.",
                font_index::INFO,
            );
            return;
        }

        // Class restriction (VB6: ClasePuedeUsarItem Or Privilegios >= Semidios)
        if !is_gm && !obj_data.class_prohibida.is_empty() {
            let uc = user_class.to_string().to_uppercase();
            if obj_data
                .class_prohibida
                .iter()
                .any(|c| c.to_uppercase() == uc)
            {
                state.send_msg_id(conn_id, 113, ""); // TEXTO113: Tu clase, genero o raza no puede usar este objeto
                return;
            }
        }

        // Faction restriction (VB6: FaccionPuedeUsarItem Or Privilegios >= Semidios)
        if !is_gm {
            if obj_data.real {
                if user_criminal || !user_armada {
                    state.send_console(
                        conn_id,
                        "Solo miembros de la Armada Real pueden usar este item",
                        font_index::INFO,
                    );
                    return;
                }
            }
            if obj_data.caos {
                if !user_criminal || !user_caos {
                    state.send_console(
                        conn_id,
                        "Solo miembros de las Fuerzas del Caos pueden usar este item",
                        font_index::INFO,
                    );
                    return;
                }
            }
        }

        // Only allow equipping valid equipment types (VB6: InvUsuario.bas)
        // Potions, food, keys, etc. cannot be equipped
        match obj_data.obj_type {
            ObjType::Weapon
            | ObjType::Armor
            | ObjType::Shield
            | ObjType::Helmet
            | ObjType::Arrow
            | ObjType::Instrument
            | ObjType::Tool
            | ObjType::Backpack => {}
            _ => {
                // Not an equippable item type — reject silently
                return;
            }
        }

        // VB6: Can't equip armor/shield/helmet while navigating (InvUsuario.bas)
        let is_navigating = state
            .users
            .get(&conn_id)
            .map(|u| u.navigating)
            .unwrap_or(false);
        if is_navigating {
            match obj_data.obj_type {
                ObjType::Armor | ObjType::Shield | ObjType::Helmet => return,
                _ => {}
            }
        }

        // Two-handed weapon check: unequip shield if equipping 2h weapon
        if obj_data.obj_type == ObjType::Weapon && obj_data.dos_manos {
            let shield_slot = state
                .users
                .get(&conn_id)
                .map(|u| u.equip.shield)
                .unwrap_or(0);
            if shield_slot > 0 && shield_slot <= MAX_INVENTORY_SLOTS {
                unequip_slot(state, conn_id, shield_slot - 1, &ObjType::Shield);
                send_inventory_slot(state, conn_id, shield_slot - 1).await;
            }
        }

        // Arrow equip handling
        if obj_data.obj_type == ObjType::Arrow {
            // Class restriction (VB6: ClasePuedeUsarItem)
            if !is_gm && !obj_data.class_prohibida.is_empty() {
                let uc = user_class.to_string().to_uppercase();
                if obj_data
                    .class_prohibida
                    .iter()
                    .any(|c| c.to_uppercase() == uc)
                {
                    state.send_msg_id(conn_id, 113, ""); // TEXTO113: Tu clase, genero o raza no puede usar este objeto
                    return;
                }
            }

            // Faction restriction (VB6: FaccionPuedeUsarItem)
            if !is_gm {
                if obj_data.real {
                    if user_criminal || !user_armada {
                        state.send_console(
                            conn_id,
                            "Solo miembros de la Armada Real pueden usar este item",
                            font_index::INFO,
                        );
                        return;
                    }
                }
                if obj_data.caos {
                    if !user_criminal || !user_caos {
                        state.send_console(
                            conn_id,
                            "Solo miembros de las Fuerzas del Caos pueden usar este item",
                            font_index::INFO,
                        );
                        return;
                    }
                }
            }

            // Equip as ammo
            let old_ammo = state
                .users
                .get(&conn_id)
                .map(|u| u.equip.municion)
                .unwrap_or(0);
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

        // Backpack equip: VB6 — toggle equip, expand/shrink inventory slots
        if obj_data.obj_type == ObjType::Backpack {
            let bp_slot = state
                .users
                .get(&conn_id)
                .map(|u| u.backpack_slot)
                .unwrap_or(0);
            if bp_slot == slot {
                // Already wearing this backpack — unequip it
                // VB6: TirarTodosLosItemsEnMochila + reset CurrentInventorySlots
                if let Some(user) = state.users.get_mut(&conn_id) {
                    user.inventory[idx].equipped = false;
                    user.backpack_slot = 0;
                    user.current_inventory_slots = crate::game::types::MAX_NORMAL_INVENTORY_SLOTS;
                }
                send_inventory_slot(state, conn_id, idx).await;
                return;
            }
            // Unequip old backpack first
            if bp_slot > 0 && bp_slot <= MAX_INVENTORY_SLOTS {
                if let Some(user) = state.users.get_mut(&conn_id) {
                    user.inventory[bp_slot - 1].equipped = false;
                    user.backpack_slot = 0;
                    user.current_inventory_slots = crate::game::types::MAX_NORMAL_INVENTORY_SLOTS;
                }
                send_inventory_slot(state, conn_id, bp_slot - 1).await;
            }
            // Equip new backpack: CurrentInventorySlots = 20 + MochilaType * 5
            let mochila_type = obj_data.mochila_type;
            let new_slots =
                crate::game::types::MAX_NORMAL_INVENTORY_SLOTS + (mochila_type as usize) * 5;
            let new_slots = new_slots.min(MAX_INVENTORY_SLOTS);
            if let Some(user) = state.users.get_mut(&conn_id) {
                user.inventory[idx].equipped = true;
                user.backpack_slot = slot;
                user.current_inventory_slots = new_slots;
            }
            send_inventory_slot(state, conn_id, idx).await;
            state.send_console(
                conn_id,
                &format!("Mochila equipada. Espacios de inventario: {}.", new_slots),
                font_index::INFO,
            );
            return;
        }

        // Equip — first unequip any item in the same category
        let old_slot = match obj_data.obj_type {
            ObjType::Weapon => state
                .users
                .get(&conn_id)
                .map(|u| u.equip.weapon)
                .unwrap_or(0),
            ObjType::Armor => state
                .users
                .get(&conn_id)
                .map(|u| u.equip.armor)
                .unwrap_or(0),
            ObjType::Shield => state
                .users
                .get(&conn_id)
                .map(|u| u.equip.shield)
                .unwrap_or(0),
            ObjType::Helmet => state
                .users
                .get(&conn_id)
                .map(|u| u.equip.helmet)
                .unwrap_or(0),
            ObjType::Tool => state.users.get(&conn_id).map(|u| u.equip.ring).unwrap_or(0),
            _ => 0,
        };

        if old_slot > 0 && old_slot <= MAX_INVENTORY_SLOTS {
            let old_idx = old_slot - 1;
            // Check if old item had an aura — if so, we need to broadcast the change
            // even if the new item has no aura (VB6: unequip always sends AU|)
            let old_had_aura = {
                let old_obj_idx = state
                    .users
                    .get(&conn_id)
                    .map(|u| u.inventory[old_idx].obj_index as usize)
                    .unwrap_or(0);
                if old_obj_idx >= 1 {
                    state
                        .game_data
                        .objects
                        .get(old_obj_idx - 1)
                        .map(|o| o.crea_aura > 0)
                        .unwrap_or(false)
                } else {
                    false
                }
            };
            unequip_slot(state, conn_id, old_idx, &obj_data.obj_type);
            if old_had_aura {
                aura_changed = true;
            }
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
                    if obj_data.crea_aura > 0 {
                        user.aura_w = obj_data.crea_aura;
                        aura_changed = true;
                    }
                }
                ObjType::Armor => {
                    user.equip.armor = slot;
                    // VB6: equiparRopaje — set body to armor's Ropaje graphic
                    if obj_data.num_ropaje > 0 {
                        user.body = obj_data.num_ropaje;
                    }
                    if obj_data.crea_aura > 0 {
                        user.aura_a = obj_data.crea_aura;
                        aura_changed = true;
                    }
                }
                ObjType::Shield => {
                    user.equip.shield = slot;
                    tracing::info!(
                        "[EQUIP] Shield slot={} obj={} '{}' shield_anim={}",
                        slot,
                        obj_index,
                        obj_data.name,
                        obj_data.shield_anim
                    );
                    user.shield_anim = obj_data.shield_anim;
                    if obj_data.crea_aura > 0 {
                        user.aura_e = obj_data.crea_aura;
                        aura_changed = true;
                    }
                }
                ObjType::Helmet => {
                    user.equip.helmet = slot;
                    user.casco_anim = obj_data.casco_anim;
                    if obj_data.crea_aura > 0 {
                        user.aura_c = obj_data.crea_aura;
                        aura_changed = true;
                    }
                }
                ObjType::Tool => {
                    user.equip.ring = slot;
                    if obj_data.crea_aura > 0 {
                        user.aura_r = obj_data.crea_aura;
                        aura_changed = true;
                    }
                }
                _ => {}
            }
        }
    }

    // Send updated CSI for this slot
    send_inventory_slot(state, conn_id, idx).await;

    // Broadcast appearance change to area
    let user_data = match state.users.get(&conn_id) {
        Some(u) => (
            u.pos_map,
            u.pos_x,
            u.pos_y,
            u.char_index,
            u.weapon_anim,
            u.shield_anim,
            u.casco_anim,
        ),
        None => return,
    };
    let (map, x, y, char_index, weapon, shield, helmet) = user_data;

    // Send updated equipment stats (ANM) to the user
    {
        let anm_text = build_anm_packet(state, conn_id);
        let pkt_anm = binary_packets::write_anim_data(&anm_text);
        state.send_bytes(conn_id, &pkt_anm);
    }

    // Send equipment change packets to area
    // Get full user data for CP (character change) packet
    let (body_now, head_now, heading_now) = state
        .users
        .get(&conn_id)
        .map(|u| (u.body, u.head, u.heading))
        .unwrap_or((0, 0, 0));
    match obj_data.obj_type {
        ObjType::Weapon => {
            // VB6: SND_SACARARMA (25) — draw weapon sound
            let pkt_wave = binary_packets::write_play_wave(25, x as i16, y as i16);
            state.send_data_bytes(SendTarget::ToArea { map, x, y }, &pkt_wave);
            // Send CP (character change) to update weapon appearance
            let pkt_cp = binary_packets::write_character_change(
                char_index.0 as i16,
                body_now as i16,
                head_now as i16,
                heading_now as u8,
                weapon as i16,
                shield as i16,
                helmet as i16,
                0,
                0,
            );
            state.send_data_bytes(SendTarget::ToArea { map, x, y }, &pkt_cp);
        }
        ObjType::Armor => {
            // VB6: ChangeUserBody sends CP packet with new body
            let body = state.users.get(&conn_id).map(|u| u.body).unwrap_or(0);
            let pkt_cp = binary_packets::write_character_change(
                char_index.0 as i16,
                body as i16,
                head_now as i16,
                heading_now as u8,
                weapon as i16,
                shield as i16,
                helmet as i16,
                0,
                0,
            );
            state.send_data_bytes(SendTarget::ToArea { map, x, y }, &pkt_cp);
        }
        ObjType::Shield => {
            let pkt_cp = binary_packets::write_character_change(
                char_index.0 as i16,
                body_now as i16,
                head_now as i16,
                heading_now as u8,
                weapon as i16,
                shield as i16,
                helmet as i16,
                0,
                0,
            );
            state.send_data_bytes(SendTarget::ToArea { map, x, y }, &pkt_cp);
        }
        ObjType::Helmet => {
            let pkt_cp = binary_packets::write_character_change(
                char_index.0 as i16,
                body_now as i16,
                head_now as i16,
                heading_now as u8,
                weapon as i16,
                shield as i16,
                helmet as i16,
                0,
                0,
            );
            state.send_data_bytes(SendTarget::ToArea { map, x, y }, &pkt_cp);
        }
        _ => {}
    }

    // VB6: SendUserAura — broadcast aura change when equipment with aura is changed
    if aura_changed {
        if let Some(user) = state.users.get(&conn_id) {
            let pkt_au = crate::game::handlers::common::build_aura_binary(user);
            state.send_data_bytes(SendTarget::ToArea { map, x, y }, &pkt_au);
        }
    }
}

/// Unequip an item from a specific inventory slot.
pub(crate) fn unequip_slot(
    state: &mut GameState,
    conn_id: ConnectionId,
    idx: usize,
    obj_type: &ObjType,
) {
    // Get the item's aura before unequipping (to clear matching aura field)
    let item_aura = {
        let obj_idx = state
            .users
            .get(&conn_id)
            .map(|u| u.inventory[idx].obj_index as usize)
            .unwrap_or(0);
        if obj_idx >= 1 {
            state
                .game_data
                .objects
                .get(obj_idx - 1)
                .map(|o| o.crea_aura)
                .unwrap_or(0)
        } else {
            0
        }
    };

    if let Some(user) = state.users.get_mut(&conn_id) {
        user.inventory[idx].equipped = false;
        match obj_type {
            ObjType::Weapon => {
                user.equip.weapon = 0;
                user.weapon_anim = crate::game::handlers::common::NINGUN_ARMA;
                if item_aura > 0 && user.aura_w == item_aura {
                    user.aura_w = 0;
                }
            }
            ObjType::Armor => {
                user.equip.armor = 0;
                let nb = naked_body(user.race, user.gender);
                user.body = nb;
                if item_aura > 0 && user.aura_a == item_aura {
                    user.aura_a = 0;
                }
            }
            ObjType::Shield => {
                user.equip.shield = 0;
                user.shield_anim = crate::game::handlers::common::NINGUN_ESCUDO;
                if item_aura > 0 && user.aura_e == item_aura {
                    user.aura_e = 0;
                }
            }
            ObjType::Helmet => {
                user.equip.helmet = 0;
                user.casco_anim = crate::game::handlers::common::NINGUN_CASCO;
                if item_aura > 0 && user.aura_c == item_aura {
                    user.aura_c = 0;
                }
            }
            ObjType::Tool => {
                user.equip.ring = 0;
                if item_aura > 0 && user.aura_r == item_aura {
                    user.aura_r = 0;
                }
            }
            _ => {}
        }
    }
}
