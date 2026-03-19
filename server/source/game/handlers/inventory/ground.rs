//! Inventory ground interaction: pickup, drop items, drop gold.
//! Split from inventory.rs for file size management.

use tracing::info;
use crate::net::ConnectionId;
use crate::game::types::{GameState, SendTarget, MAX_INVENTORY_SLOTS, privilege_level};
use crate::game::world;
use crate::protocol::font_index;
use crate::protocol::binary_packets;
use crate::data::objects::ObjType;
use crate::game::handlers::common::*;
use crate::game::constants::*;
use crate::game::handlers::{
    send_inventory_slot, send_full_inventory,
};
use super::equip::unequip_slot;

pub(crate) async fn handle_pick_up(state: &mut GameState, conn_id: ConnectionId) {
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

    // VB6: Agarrable=1 means the object CANNOT be picked up (fixed object like sign, fire, etc.)
    // If Agarrable <> 1 Then → allow pickup
    let is_fixed = state.get_object(ground_item.obj_index)
        .map(|o| o.agarrable)
        .unwrap_or(false);

    if is_fixed {
        state.send_console(conn_id, "No puedes agarrar ese objeto", font_index::INFO);
        return;
    }

    let obj_idx = ground_item.obj_index;
    let amount = ground_item.amount;

    // VB6 13.3: Gold (otGuita / ObjType::Money) goes directly to wallet, not inventory
    let is_gold = state.get_object(obj_idx)
        .map(|o| o.obj_type == ObjType::Money)
        .unwrap_or(false);

    if is_gold {
        // Remove item from ground
        {
            let grid = state.world.grid_mut(map);
            if let Some(tile) = grid.tile_mut(x, y) {
                tile.ground_item = world::GroundItem::default();
            }
        }
        // Broadcast BO (erase object) to area
        let pkt_bo = binary_packets::write_object_delete(x as i16, y as i16);
        state.send_data_bytes(SendTarget::ToArea { map, x, y }, &pkt_bo);

        // Add directly to gold counter (VB6: Stats.GLD += Amount)
        if let Some(user) = state.users.get_mut(&conn_id) {
            user.gold += amount as i64;
        }
        send_stats_gold(state, conn_id).await;
        state.send_console(conn_id, &format!("Has recogido {} monedas de oro.", amount), font_index::INFO);
        return;
    }

    // Find free inventory slot
    let free_slot = {
        let user = match state.users.get(&conn_id) {
            Some(u) => u,
            None => return,
        };
        let max_slots = user.current_inventory_slots;
        // First check if we can stack with an existing slot
        let mut stack_slot = None;
        let mut empty_slot = None;
        for i in 0..MAX_INVENTORY_SLOTS {
            if user.inventory[i].obj_index == ground_item.obj_index && user.inventory[i].amount > 0 {
                stack_slot = Some(i);
                break;
            }
            if i < max_slots && user.inventory[i].obj_index == 0 && empty_slot.is_none() {
                empty_slot = Some(i);
            }
        }
        stack_slot.or(empty_slot)
    };

    let slot = match free_slot {
        Some(s) => s,
        None => {
            state.send_msg_id(conn_id, 108, ""); // TEXTO108: No podes cargar mas objetos
            return;
        }
    };

    // Remove item from ground
    {
        let grid = state.world.grid_mut(map);
        if let Some(tile) = grid.tile_mut(x, y) {
            tile.ground_item = world::GroundItem::default();
        }
    }

    // Broadcast BO (erase object) to area
    let pkt_bo = binary_packets::write_object_delete(x as i16, y as i16);
    state.send_data_bytes(SendTarget::ToArea { map, x, y }, &pkt_bo);

    // Add to inventory
    if let Some(user) = state.users.get_mut(&conn_id) {
        if user.inventory[slot].obj_index == obj_idx {
            // Stack
            let new_amt = user.inventory[slot].amount + amount;
            user.inventory[slot].amount = new_amt;
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

    state.send_msg_id(conn_id, 115, &format!("{}@{}", amount, item_name)); // TEXTO115: Recibiste %1 - %2
}

/// TI<slot>,<amount> — Drop item from inventory to ground.
pub(crate) async fn handle_drop_item(state: &mut GameState, conn_id: ConnectionId, slot: usize, amount: i32) {
    if amount <= 0 {
        return;
    }

    // slot must be 1..=max_slots (FLAGORO=-1 gold drops are handled by caller)
    let max_slots = state.users.get(&conn_id).map(|u| u.current_inventory_slots).unwrap_or(MAX_INVENTORY_SLOTS);
    if slot < 1 || slot > max_slots {
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
        state.send_console(conn_id, "Los GMs no pueden tirar items", font_index::INFO);
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
        state.send_msg_id(conn_id, 107, ""); // TEXTO107: No hay espacio en el piso
        return;
    }

    // If item is equipped, unequip it first (VB6: DropObj line 295 calls Desequipar)
    let is_equipped = state.users.get(&conn_id)
        .map(|u| u.inventory[idx].equipped)
        .unwrap_or(false);

    if is_equipped {
        let obj_data = state.get_object(obj_idx).cloned();
        if let Some(obj) = &obj_data {
            let had_aura = obj.crea_aura > 0;
            let obj_type = obj.obj_type;
            unequip_slot(state, conn_id, idx, &obj_type);

            // Extract values needed for packets before borrowing state mutably
            let (ci, weapon_anim, body, head, heading, shield_anim, casco_anim, au_pkt_bin) =
                match state.users.get(&conn_id) {
                    Some(user) => (
                        user.char_index.0,
                        user.weapon_anim,
                        user.body,
                        user.head,
                        user.heading,
                        user.shield_anim,
                        user.casco_anim,
                        if had_aura { Some(crate::game::handlers::common::build_aura_binary(user)) } else { None },
                    ),
                    None => return,
                };

            // Broadcast appearance change to area via CP (character change)
            let send_cp = matches!(obj_type, ObjType::Weapon | ObjType::Armor | ObjType::Shield | ObjType::Helmet);
            if send_cp {
                let pkt_cp = binary_packets::write_character_change(
                    ci as i16, body as i16, head as i16, heading as u8,
                    weapon_anim as i16, shield_anim as i16, casco_anim as i16, 0, 0,
                );
                state.send_data_bytes(SendTarget::ToArea { map, x, y }, &pkt_cp);
            }

            // Send aura update if item had an aura
            if let Some(pkt_au) = au_pkt_bin {
                state.send_data_bytes(SendTarget::ToArea { map, x, y }, &pkt_au);
            }

            // Send updated ANM (equipment stats) to the user
            {
                let anm_text = build_anm_packet(state, conn_id);
                let pkt_anm = binary_packets::write_anim_data(&anm_text);
                state.send_bytes(conn_id, &pkt_anm);
            }
        }
    }

    // VB6: If dropping a mount while mounted, dismount (only if this is the last one)
    // Same for boats while navigating.
    let drop_obj_type = state.get_object(obj_idx).map(|o| o.obj_type);
    let (is_mounted, is_navigating) = state.users.get(&conn_id)
        .map(|u| (u.montado, u.navigating))
        .unwrap_or((false, false));

    if drop_obj_type == Some(ObjType::Mount) && is_mounted {
        let remaining = state.users.get(&conn_id)
            .map(|u| {
                let mut count = 0i32;
                for s in &u.inventory {
                    if s.obj_index == obj_idx { count += s.amount; }
                }
                count - drop_amount
            })
            .unwrap_or(0);

        if remaining <= 0 {
            if let Some(u) = state.users.get_mut(&conn_id) {
                let saved_body = u.montado_body;
                let saved_head = u.orig_head;
                u.montado = false;
                u.levitando = false;
                u.body = saved_body;
                u.head = saved_head;
            }
            let (wa, sa, ca) = get_equipped_anims(state, conn_id);
            if let Some(u) = state.users.get_mut(&conn_id) {
                u.weapon_anim = wa;
                u.shield_anim = sa;
                u.casco_anim = ca;
            }
            let (cp_bytes, pkt_usm, pkt_mvol, pkt_cd) = {
                let u = match state.users.get(&conn_id) { Some(u) => u, None => return };
                (
                    binary_packets::write_character_change(
                        u.char_index.0 as i16, u.body as i16, u.head as i16, u.heading as u8,
                        u.weapon_anim as i16, u.shield_anim as i16, u.casco_anim as i16, 0, 0,
                    ),
                    binary_packets::write_user_mount(u.char_index.0 as i16, false),
                    binary_packets::write_levitate(u.char_index.0 as i16, false),
                    crate::game::handlers::common::build_cd_binary(u),
                )
            };
            state.send_data_bytes(SendTarget::ToArea { map, x, y }, &cp_bytes);
            state.send_data_bytes(SendTarget::ToArea { map, x, y }, &pkt_usm);
            state.send_data_bytes(SendTarget::ToArea { map, x, y }, &pkt_mvol);
            state.send_data_bytes(SendTarget::ToArea { map, x, y }, &pkt_cd);
        }
    }

    if drop_obj_type == Some(ObjType::Boat) && is_navigating {
        let remaining = state.users.get(&conn_id)
            .map(|u| {
                let mut count = 0i32;
                for s in &u.inventory {
                    if s.obj_index == obj_idx { count += s.amount; }
                }
                count - drop_amount
            })
            .unwrap_or(0);

        if remaining <= 0 {
            if let Some(u) = state.users.get_mut(&conn_id) {
                let saved_body = u.montado_body;
                let saved_head = u.old_head;
                u.navigating = false;
                u.body = saved_body;
                if saved_head > 0 { u.head = saved_head; }
                u.barco_slot = 0;
            }
            let (wa, sa, ca) = get_equipped_anims(state, conn_id);
            if let Some(u) = state.users.get_mut(&conn_id) {
                u.weapon_anim = wa;
                u.shield_anim = sa;
                u.casco_anim = ca;
            }
            let (cp_bytes, naveg_ci) = {
                let u = match state.users.get(&conn_id) { Some(u) => u, None => return };
                (
                    binary_packets::write_character_change(
                        u.char_index.0 as i16, u.body as i16, u.head as i16, u.heading as u8,
                        u.weapon_anim as i16, u.shield_anim as i16, u.casco_anim as i16, 0, 0,
                    ),
                    u.char_index.0,
                )
            };
            state.send_data_bytes(SendTarget::ToArea { map, x, y }, &cp_bytes);
            let pkt_naveg = binary_packets::write_navigate_broadcast(naveg_ci as i16, false);
            state.send_data_bytes(SendTarget::ToArea { map, x, y }, &pkt_naveg);
            // Navigate toggle to self (client-side navigation state off)
            let pkt_nav_self = binary_packets::write_navigate_toggle();
            state.send_bytes(conn_id, &pkt_nav_self);
        }
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
        let cur_amt = user.inventory[idx].amount;
        if drop_amount >= cur_amt {
            user.inventory[idx].obj_index = 0;
        user.inventory[idx].amount = 0;
        user.inventory[idx].equipped = false;
        } else {
            user.inventory[idx].amount = cur_amt - drop_amount;
        }
    }

    send_inventory_slot(state, conn_id, idx).await;

    // Broadcast HO (show object) to area if new item on tile
    if is_new && grh_index > 0 {
        let pkt_ho = binary_packets::write_object_create(x as i16, y as i16, grh_index as i16);
        state.send_data_bytes(SendTarget::ToArea { map, x, y }, &pkt_ho);
    }

    // Track for world cleanup (auto-remove after 10 ticks)
    clean_world_add_item(state, map, x, y, 10, obj_idx);
}

/// Drop gold from inventory (TI with slot=-1).
/// VB6: TirarOro — splits into piles of max 10,000 on ground.
pub(crate) async fn handle_drop_gold(state: &mut GameState, conn_id: ConnectionId, amount: i32) {
    let (gold, map, x, y) = match state.users.get(&conn_id) {
        Some(u) if u.logged && !u.dead => (u.gold, u.pos_map, u.pos_x, u.pos_y),
        _ => return,
    };

    if gold < amount as i64 || amount <= 0 {
        return;
    }

    // VB6: Cap at 500,000 per drop
    let drop_total = (amount as i64).min(500_000).min(gold) as i32;

    // Deduct gold
    if let Some(user) = state.users.get_mut(&conn_id) {
        user.gold -= drop_total as i64;
    }
    send_stats_gold(state, conn_id).await;

    // VB6: TirarOro — place gold object (iORO=12) on ground tile
    // Split into MAX_INVENTORY_OBJS (10000) chunks like VB6
    let grh_index = state.get_object(GOLD_OBJ_INDEX)
        .map(|o| o.grh_index)
        .unwrap_or(0);

    let mut remaining = drop_total;
    while remaining > 0 {
        let chunk = remaining.min(10_000);
        remaining -= chunk;

        // Check tile availability
        let can_place = state.world.grid(map)
            .and_then(|g| g.tile(x, y))
            .map(|t| t.ground_item.obj_index == 0 || t.ground_item.obj_index == GOLD_OBJ_INDEX)
            .unwrap_or(false);

        if !can_place {
            // Refund what we couldn't place
            if let Some(user) = state.users.get_mut(&conn_id) {
                user.gold += (chunk + remaining) as i64;
            }
            send_stats_gold(state, conn_id).await;
            break;
        }

        let is_new = {
            let grid = state.world.grid_mut(map);
            if let Some(tile) = grid.tile_mut(x, y) {
                if tile.ground_item.obj_index == GOLD_OBJ_INDEX {
                    tile.ground_item.amount += chunk;
                    false
                } else {
                    tile.ground_item.obj_index = GOLD_OBJ_INDEX;
                    tile.ground_item.amount = chunk;
                    true
                }
            } else {
                break;
            }
        };

        if is_new && grh_index > 0 {
            let pkt_ho = binary_packets::write_object_create(x as i16, y as i16, grh_index as i16);
            state.send_data_bytes(SendTarget::ToArea { map, x, y }, &pkt_ho);
        }

        clean_world_add_item(state, map, x, y, 10, GOLD_OBJ_INDEX);
        // Only one pile per tile — break after placing
        break;
    }

    state.send_console(conn_id, &format!("Tiraste {} monedas de oro.", drop_total), font_index::INFO);
}
