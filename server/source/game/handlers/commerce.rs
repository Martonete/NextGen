//! Commerce handlers: NPC buy/sell, user-to-user trading, personal bank.
//! Extracted from mod.rs to reduce file size.

use tracing::info;
use crate::net::ConnectionId;
use crate::game::types::{GameState, InventorySlot, MAX_BANK_SLOTS};
use crate::protocol::{fields::read_field, binary_packets};
use crate::data::objects::ObjType;
use super::common::*;
use super::{send_inventory_slot, send_full_inventory};

/// Commerce skill index in skills array.
const SK_COMERCIAR: usize = 11; // VB6 eSkill.Comerciar = 11

/// Start NPC commerce (VB6: IniciarComercioNPC).
/// Sends NPC inventory, gold, sets flag, sends INITCOM.
pub(super) async fn iniciar_comercio_npc(state: &mut GameState, conn_id: ConnectionId, npc_idx: usize) {
    // Check user not dead
    if let Some(u) = state.users.get(&conn_id) {
        if u.dead {
            let pkt = binary_packets::write_console_msg_id(3, ""); // TEXTO3: Estás muerto
            state.send_bytes(conn_id, &pkt).await;
            return;
        }
        if u.comerciando {
            return; // Already in commerce
        }
    }

    // Send NPC inventory
    enviar_npc_inv(state, conn_id, npc_idx, 0).await;

    // Send gold
    send_stats_gold(state, conn_id).await;

    // Set commerce flag
    if let Some(user) = state.users.get_mut(&conn_id) {
        user.comerciando = true;
        user.target_npc = npc_idx;
    }

    // Send INITCOM
    let pkt = binary_packets::write_commerce_init();
    state.send_bytes(conn_id, &pkt).await;
}

/// Send NPC inventory to client (VB6: EnviarNpcInv).
/// If slot == 0: send full inventory (NPCR + NPCI*N).
/// If slot != 0: send single slot update (NPC|).
pub(super) async fn enviar_npc_inv(state: &mut GameState, conn_id: ConnectionId, npc_idx: usize, slot: usize) {
    // Calculate discount from commerce skill
    let comerciar_skill = state.users.get(&conn_id)
        .map(|u| u.skills[SK_COMERCIAR])
        .unwrap_or(0);
    let descuento = 1.0 + comerciar_skill as f64 / 100.0;
    let descuento = if descuento <= 0.0 { 1.0 } else { descuento };

    // Gather NPC inventory data
    let npc_data: Vec<(i32, i32, usize)> = match state.get_npc(npc_idx) {
        Some(npc) => {
            npc.inventory.iter().enumerate().map(|(i, s)| (s.obj_index, s.amount, i)).collect()
        }
        None => return,
    };
    let inflacion = state.get_npc(npc_idx).map(|n| n.inflacion).unwrap_or(0);

    if slot == 0 {
        // Full inventory send — reset then send each non-empty slot
        let pkt = binary_packets::write_npc_inv_reset();
        state.send_bytes(conn_id, &pkt).await;

        for (obj_index, amount, idx) in &npc_data {
            if *obj_index <= 0 { continue; }
            let obj = match state.get_object(*obj_index) {
                Some(o) => o.clone(),
                None => continue,
            };

            let infla = (inflacion as i64 * obj.valor as i64) / 100;
            let price = ((obj.valor as i64 + infla) as f64 / descuento) as i64;

            let pkt = binary_packets::write_change_npc_inv_slot(
                (*idx + 1) as u8, // 1-based slot for client
                &obj.name,
                *amount as i16,
                price as f32,
                obj.grh_index as i16,
                *obj_index as i16,
                obj.obj_type as u8,
                obj.max_hit as i16,
                obj.min_hit as i16,
                obj.max_def as i16,
            );
            state.send_bytes(conn_id, &pkt).await;
        }
    } else {
        // Single slot update
        let idx = slot - 1; // Convert to 0-based
        if idx >= npc_data.len() { return; }
        let (obj_index, amount, _) = npc_data[idx];

        if obj_index <= 0 {
            // Empty slot — send zeroed data
            let pkt = binary_packets::write_change_npc_inv_slot(
                slot as u8, "(Nada)", 0, 0.0, 0, 0, 0, 0, 0, 0,
            );
            state.send_bytes(conn_id, &pkt).await;
        } else {
            let obj = match state.get_object(obj_index) {
                Some(o) => o.clone(),
                None => return,
            };
            let infla = (inflacion as i64 * obj.valor as i64) / 100;
            let price = ((obj.valor as i64 + infla) as f64 / descuento) as i64;

            let pkt = binary_packets::write_change_npc_inv_slot(
                slot as u8,
                &obj.name,
                amount as i16,
                price as f32,
                obj.grh_index as i16,
                obj_index as i16,
                obj.obj_type as u8,
                obj.max_hit as i16,
                obj.min_hit as i16,
                obj.max_def as i16,
            );
            state.send_bytes(conn_id, &pkt).await;
        }
    }
}

/// COMP — User buys from NPC (VB6: NPCVentaItem / UserCompraObj).
pub(super) async fn handle_commerce_buy(state: &mut GameState, conn_id: ConnectionId, data: &str) {
    let payload = strip_opcode(data, 5); // "COMP," = 5 chars (VB6 strips 5)
    info!("[COMP] #{} raw='{}' payload='{}'", conn_id, data, payload);
    let slot: usize = match read_field(1, payload, ',').parse() {
        Ok(v) if v >= 1 => v,
        _ => { info!("[COMP] #{} bad slot field: '{}'", conn_id, read_field(1, payload, ',')); return; },
    };
    let cantidad: i32 = match read_field(2, payload, ',').parse() {
        Ok(v) if v >= 1 => v,
        _ => { info!("[COMP] #{} bad cantidad field: '{}'", conn_id, read_field(2, payload, ',')); return; },
    };
    info!("[COMP] #{} slot={} qty={}", conn_id, slot, cantidad);

    // Anti-cheat: max stack check
    if cantidad > MAX_INVENTORY_OBJS {
        info!("[COMMERCE] #{} attempted to buy {} items (exceeds max), potential cheat", conn_id, cantidad);
        return;
    }

    // Validate user state
    let (dead, comerciando, target_npc, user_gold, comerciar_skill) = match state.users.get(&conn_id) {
        Some(u) if u.logged => (u.dead, u.comerciando, u.target_npc, u.gold, u.skills[SK_COMERCIAR]),
        _ => { info!("[COMP] #{} user not logged or missing", conn_id); return; },
    };
    info!("[COMP] #{} state: dead={} comerciando={} target_npc={} gold={}", conn_id, dead, comerciando, target_npc, user_gold);

    if dead {
        let pkt = binary_packets::write_console_msg_id(3, ""); // TEXTO3: Estás muerto
        state.send_bytes(conn_id, &pkt).await;
        return;
    }
    if !comerciando || target_npc == 0 {
        info!("[COMP] #{} REJECTED: comerciando={} target_npc={}", conn_id, comerciando, target_npc);
        return;
    }

    // Get NPC data
    let (npc_comercia, npc_inflacion) = match state.get_npc(target_npc) {
        Some(npc) => (npc.comercia, npc.inflacion),
        None => { info!("[COMP] #{} NPC {} not found", conn_id, target_npc); return; },
    };
    if !npc_comercia { info!("[COMP] #{} NPC {} not merchant", conn_id, target_npc); return; }

    // Get item from NPC inventory (slot is 1-based)
    let slot_idx = slot - 1;
    let (obj_index, npc_amount) = match state.get_npc(target_npc) {
        Some(npc) if slot_idx < npc.inventory.len() => {
            (npc.inventory[slot_idx].obj_index, npc.inventory[slot_idx].amount)
        }
        _ => { info!("[COMP] #{} NPC inv slot {} out of range", conn_id, slot_idx); return; },
    };
    if obj_index <= 0 || npc_amount <= 0 { info!("[COMP] #{} NPC slot {} empty: obj={} amt={}", conn_id, slot_idx, obj_index, npc_amount); return; }
    info!("[COMP] #{} NPC item: obj={} amt={} slot_idx={}", conn_id, obj_index, npc_amount, slot_idx);

    // Clamp quantity to NPC stock
    let cantidad = cantidad.min(npc_amount);

    // Get object data for price calc
    let obj = match state.get_object(obj_index) {
        Some(o) => o.clone(),
        None => return,
    };

    // Calculate buy price
    let descuento = 1.0 + comerciar_skill as f64 / 100.0;
    let descuento = if descuento <= 0.0 { 1.0 } else { descuento };
    let infla = (npc_inflacion as i64 * obj.valor as i64) / 100;
    let unit_price = ((obj.valor as i64 + infla) as f64 / descuento) as i64;
    let total_price = unit_price * cantidad as i64;

    // Check gold
    if user_gold < total_price {
        let pkt = binary_packets::write_console_msg_id(663, ""); // TEXTO663: No tenes suficiente dinero
        state.send_bytes(conn_id, &pkt).await;
        return;
    }

    // Find user inventory slot (try stacking first, then empty)
    let user_inv_slot = {
        let user = match state.users.get(&conn_id) {
            Some(u) => u,
            None => return,
        };

        // Try to stack
        let mut stack_slot = None;
        let mut empty_slot = None;
        for (i, inv) in user.inventory.iter().enumerate() {
            if inv.obj_index == obj_index && inv.amount + cantidad <= MAX_INVENTORY_OBJS {
                stack_slot = Some(i);
                break;
            }
            if inv.obj_index == 0 && empty_slot.is_none() {
                empty_slot = Some(i);
            }
        }
        stack_slot.or(empty_slot)
    };

    let user_slot = match user_inv_slot {
        Some(s) => s,
        None => {
            let pkt = binary_packets::write_console_msg_id(108, ""); // TEXTO108: No podes cargar mas objetos
            state.send_bytes(conn_id, &pkt).await;
            return;
        }
    };

    // Execute transaction: add to user, deduct gold
    if let Some(user) = state.users.get_mut(&conn_id) {
        if user.inventory[user_slot].obj_index == 0 {
            user.inventory[user_slot].obj_index = obj_index;
            user.inventory[user_slot].amount = cantidad;
        } else {
            user.inventory[user_slot].amount += cantidad;
        }
        user.gold -= total_price;
        if user.gold < 0 { user.gold = 0; }
    }

    // Remove from NPC inventory
    let mut restocked = false;
    if let Some(npc) = state.get_npc_mut(target_npc) {
        npc.inventory[slot_idx].amount -= cantidad;
        if npc.inventory[slot_idx].amount <= 0 {
            npc.inventory[slot_idx].obj_index = 0;
            npc.inventory[slot_idx].amount = 0;
            npc.nro_items -= 1;

            // VB6 parity: auto-replenish when ALL slots emptied and InvReSpawn <> 1
            if npc.nro_items <= 0 && !npc.inv_respawn {
                restocked = true;
            }
        }
    }
    if restocked {
        reload_npc_inventory(state, target_npc);
    }

    // Send updates to client
    send_inventory_slot(state, conn_id, user_slot).await;
    send_stats_gold(state, conn_id).await;
    // After restock: send full NPC inventory (slot=0). Otherwise just the changed slot.
    enviar_npc_inv(state, conn_id, target_npc, if restocked { 0 } else { slot }).await;

    let pkt = binary_packets::write_trans_ok(slot as u8, 0); // 0 = buy
    state.send_bytes(conn_id, &pkt).await;
}

/// VEND — User sells to NPC (VB6: NPCCompraItem / NpcCompraObj).
pub(super) async fn handle_commerce_sell(state: &mut GameState, conn_id: ConnectionId, data: &str) {
    let payload = strip_opcode(data, 5); // "VEND," = 5 chars (VB6 strips 5)
    let slot: usize = match read_field(1, payload, ',').parse() {
        Ok(v) if v >= 1 => v,
        _ => return,
    };
    let cantidad: i32 = match read_field(2, payload, ',').parse() {
        Ok(v) if v >= 1 => v,
        _ => return,
    };

    // Validate user state
    let (dead, comerciando, target_npc) = match state.users.get(&conn_id) {
        Some(u) if u.logged => (u.dead, u.comerciando, u.target_npc),
        _ => return,
    };

    if dead {
        let pkt = binary_packets::write_console_msg_id(3, ""); // TEXTO3: Estás muerto
        state.send_bytes(conn_id, &pkt).await;
        return;
    }
    if !comerciando || target_npc == 0 { return; }

    // Validate NPC
    let (npc_comercia, npc_tipo_items) = match state.get_npc(target_npc) {
        Some(npc) => (npc.comercia, npc.tipo_items),
        None => return,
    };
    if !npc_comercia { return; }

    // Get user item data (slot is 1-based)
    let user_slot_idx = slot - 1;
    let (obj_index, user_amount, equipped) = match state.users.get(&conn_id) {
        Some(u) if user_slot_idx < u.inventory.len() => {
            let inv = &u.inventory[user_slot_idx];
            (inv.obj_index, inv.amount, inv.equipped)
        }
        _ => return,
    };

    if obj_index <= 0 || user_amount <= 0 { return; }
    if equipped {
        let pkt = binary_packets::write_console_msg_id(185, ""); // TEXTO185: No podes depositar este objeto
        state.send_bytes(conn_id, &pkt).await;
        return;
    }

    // Get object data
    let obj = match state.get_object(obj_index) {
        Some(o) => o.clone(),
        None => return,
    };

    // Reject conditions (VB6: NpcCompraObj)
    if obj.newbie {
        let pkt = binary_packets::write_console_msg_id(660, ""); // TEXTO660: No comercio objetos para newbies
        state.send_bytes(conn_id, &pkt).await;
        return;
    }
    if obj_index == GOLD_OBJ_INDEX {
        let pkt = binary_packets::write_console_msg_id(661, ""); // TEXTO661: El npc no esta interesado en comprar ese objeto
        state.send_bytes(conn_id, &pkt).await;
        return;
    }
    if obj.obj_type == ObjType::Key {
        let pkt = binary_packets::write_console_msg_id(661, ""); // TEXTO661: El npc no esta interesado en comprar ese objeto
        state.send_bytes(conn_id, &pkt).await;
        return;
    }
    // TipoItems filter: 0 = buys anything, otherwise must match
    if npc_tipo_items != 0 && npc_tipo_items != obj.obj_type as i32 {
        let pkt = binary_packets::write_console_msg_id(661, ""); // TEXTO661: El npc no esta interesado en comprar ese objeto
        state.send_bytes(conn_id, &pkt).await;
        return;
    }

    // Clamp quantity
    let cantidad = cantidad.min(user_amount);

    // Calculate sell price (always 1/3 of base value)
    let sell_price = (obj.valor as i64 / 3) * cantidad as i64;

    // Find NPC slot (try stacking, then empty)
    let npc_slot = {
        let npc = match state.get_npc(target_npc) {
            Some(n) => n,
            None => return,
        };
        let mut stack_slot = None;
        let mut empty_slot = None;
        for (i, inv) in npc.inventory.iter().enumerate() {
            if inv.obj_index == obj_index && inv.amount + cantidad <= MAX_INVENTORY_OBJS {
                stack_slot = Some(i);
                break;
            }
            if inv.obj_index == 0 && empty_slot.is_none() {
                empty_slot = Some(i);
            }
        }
        stack_slot.or(empty_slot)
    };

    // Add to NPC inventory (if slot available)
    if let Some(npc_slot_idx) = npc_slot {
        if let Some(npc) = state.get_npc_mut(target_npc) {
            if npc.inventory[npc_slot_idx].obj_index == 0 {
                npc.inventory[npc_slot_idx].obj_index = obj_index;
                npc.inventory[npc_slot_idx].amount = cantidad;
                npc.nro_items += 1;
            } else {
                npc.inventory[npc_slot_idx].amount += cantidad;
            }
        }
    }
    // If no NPC slot, item is still sold (gold given, item removed) — VB6 behavior

    // Remove from user inventory
    if let Some(user) = state.users.get_mut(&conn_id) {
        user.inventory[user_slot_idx].amount -= cantidad;
        if user.inventory[user_slot_idx].amount <= 0 {
            user.inventory[user_slot_idx].obj_index = 0;
            user.inventory[user_slot_idx].amount = 0;
            user.inventory[user_slot_idx].equipped = false;
        }
        // Add gold (capped)
        user.gold = (user.gold + sell_price).min(MAX_GOLD);
    }

    // Send updates
    send_inventory_slot(state, conn_id, user_slot_idx).await;
    send_stats_gold(state, conn_id).await;

    // Send NPC inventory update for the slot that changed
    if let Some(npc_slot_idx) = npc_slot {
        enviar_npc_inv(state, conn_id, target_npc, npc_slot_idx + 1).await;
    }

    let pkt = binary_packets::write_trans_ok(slot as u8, 1); // 1 = sell
    state.send_bytes(conn_id, &pkt).await;
}

/// FINCOM — Close commerce window.
pub(super) async fn handle_commerce_close(state: &mut GameState, conn_id: ConnectionId) {
    if let Some(user) = state.users.get_mut(&conn_id) {
        user.comerciando = false;
    }
    let pkt = binary_packets::write_commerce_end();
    state.send_bytes(conn_id, &pkt).await;
}

/// Reload NPC inventory from game data (VB6: CargarInvent).
/// Called when NPC runs out of items and InvReSpawn is false.
pub(super) fn reload_npc_inventory(state: &mut GameState, npc_idx: usize) {
    let npc_number = match state.get_npc(npc_idx) {
        Some(npc) => npc.npc_number,
        None => return,
    };

    // Get original inventory from NPC database
    let items = match state.game_data.npcs.get(npc_number) {
        Some(data) => data.items.clone(),
        None => return,
    };
    let nro_items = match state.game_data.npcs.get(npc_number) {
        Some(data) => data.nro_items,
        None => return,
    };

    if let Some(npc) = state.get_npc_mut(npc_idx) {
        // Reset inventory
        for slot in npc.inventory.iter_mut() {
            slot.obj_index = 0;
            slot.amount = 0;
        }
        // Reload from data
        for (i, item) in items.iter().enumerate() {
            if i < npc.inventory.len() {
                npc.inventory[i].obj_index = item.obj_index;
                npc.inventory[i].amount = item.amount;
            }
        }
        npc.nro_items = nro_items;
    }
}

// =====================================================================
// Player Trading handlers (AA_ComercioUsuarios.bas)
// =====================================================================

/// Initiate trade from right-click menu (/COMERCIAR).
pub(super) async fn iniciar_comercio_usuario(state: &mut GameState, conn_id: ConnectionId, target_conn: ConnectionId) {
    // Check both users alive
    let user_ok = state.users.get(&conn_id).map(|u| !u.dead && u.logged && !u.trading).unwrap_or(false);
    let target_ok = state.users.get(&target_conn).map(|u| !u.dead && u.logged && !u.trading).unwrap_or(false);

    if !user_ok || !target_ok { return; }

    // Set both in trading mode
    if let Some(u) = state.users.get_mut(&conn_id) {
        u.trading = true;
        u.trade_partner = Some(target_conn);
        u.trade_offered = false;
        u.trade_accepted = false;
        u.trade_gold = 0;
        u.trade_items.clear();
    }
    if let Some(u) = state.users.get_mut(&target_conn) {
        u.trading = true;
        u.trade_partner = Some(conn_id);
        u.trade_offered = false;
        u.trade_accepted = false;
        u.trade_gold = 0;
        u.trade_items.clear();
    }

    // Send trade init to both — use user_commerce_init with partner name
    let target_name = state.users.get(&target_conn).map(|u| u.char_name.clone()).unwrap_or_default();
    let user_name = state.users.get(&conn_id).map(|u| u.char_name.clone()).unwrap_or_default();
    let pkt1 = binary_packets::write_user_commerce_init(&target_name);
    state.send_bytes(conn_id, &pkt1).await;
    let pkt2 = binary_packets::write_user_commerce_init(&user_name);
    state.send_bytes(target_conn, &pkt2).await;
}

/// UOR — Offer gold in trade.
pub(super) async fn handle_trade_offer_gold(state: &mut GameState, conn_id: ConnectionId, data: &str) {
    let payload = strip_opcode(data, 3);
    let gold: i64 = match payload.trim().parse() {
        Ok(v) if v >= 0 => v,
        _ => return,
    };

    let (trading, partner, user_gold) = match state.users.get(&conn_id) {
        Some(u) if u.trading => (true, u.trade_partner, u.gold),
        _ => return,
    };
    if !trading { return; }
    let partner = match partner {
        Some(p) => p,
        None => return,
    };

    // Can't offer more than you have
    let gold = gold.min(user_gold);

    if let Some(u) = state.users.get_mut(&conn_id) {
        u.trade_gold = gold;
        u.trade_offered = true;
        u.trade_accepted = false; // Reset acceptance on new offer
    }
    // Reset partner acceptance too
    if let Some(u) = state.users.get_mut(&partner) {
        u.trade_accepted = false;
    }

    // Notify partner
    let pkt = binary_packets::write_trade_offer_recv(gold as i32);
    state.send_bytes(partner, &pkt).await;
}

/// UOC — Offer items in trade.
pub(super) async fn handle_trade_offer_item(state: &mut GameState, conn_id: ConnectionId, data: &str) {
    let payload = strip_opcode(data, 3);
    let slot: usize = match read_field(1, payload, ',').parse() {
        Ok(v) if v >= 1 => v,
        _ => return,
    };
    let cantidad: i32 = match read_field(2, payload, ',').parse() {
        Ok(v) if v >= 1 => v,
        _ => return,
    };

    let (trading, partner) = match state.users.get(&conn_id) {
        Some(u) if u.trading => (true, u.trade_partner),
        _ => return,
    };
    if !trading { return; }
    let partner = match partner {
        Some(p) => p,
        None => return,
    };

    let slot_idx = slot - 1;
    let (obj_index, amount, equipped) = match state.users.get(&conn_id) {
        Some(u) if slot_idx < u.inventory.len() => {
            let s = &u.inventory[slot_idx];
            (s.obj_index, s.amount, s.equipped)
        }
        _ => return,
    };

    if obj_index <= 0 || amount <= 0 || equipped { return; }

    // VB6 validation: can't trade keys, intransferable, or god items
    let blocked = state.get_object(obj_index).map(|o| {
        o.obj_type == ObjType::Key || o.intransferible || o.item_dios
    }).unwrap_or(false);
    if blocked {
        let pkt = binary_packets::write_console_msg_id(223, ""); // TEXTO223: No puedes transferir este objeto
        state.send_bytes(conn_id, &pkt).await;
        return;
    }

    let cantidad = cantidad.min(amount);

    // Add to trade items
    if let Some(u) = state.users.get_mut(&conn_id) {
        u.trade_items.push(InventorySlot {
            obj_index,
            amount: cantidad,
            equipped: false,
        });
        u.trade_offered = true;
        u.trade_accepted = false;
    }
    if let Some(u) = state.users.get_mut(&partner) {
        u.trade_accepted = false;
    }

    // Send item info to partner
    let obj_name = state.get_object(obj_index).map(|o| o.name.clone()).unwrap_or_default();
    let pkt = binary_packets::write_trade_items(obj_index as i16, cantidad as i16, &obj_name);
    state.send_bytes(partner, &pkt).await;
}

/// TDR — Trade response (0=accept, 1=reject).
pub(super) async fn handle_trade_response(state: &mut GameState, conn_id: ConnectionId, data: &str) {
    let payload = strip_opcode(data, 3);
    let response: i32 = match payload.trim().parse() {
        Ok(v) => v,
        _ => return,
    };

    let partner = match state.users.get(&conn_id) {
        Some(u) if u.trading => u.trade_partner,
        _ => return,
    };
    let partner = match partner {
        Some(p) => p,
        None => return,
    };

    if response == 1 {
        // Reject — cancel trade
        cancel_trade(state, conn_id, partner).await;
        return;
    }

    // Accept
    if let Some(u) = state.users.get_mut(&conn_id) {
        u.trade_accepted = true;
    }

    // Check if both accepted
    let both_accepted = state.users.get(&conn_id).map(|u| u.trade_accepted).unwrap_or(false)
        && state.users.get(&partner).map(|u| u.trade_accepted).unwrap_or(false);

    if both_accepted {
        execute_trade(state, conn_id, partner).await;
    }
}

/// Execute the trade (both parties accepted).
pub(super) async fn execute_trade(state: &mut GameState, user1: ConnectionId, user2: ConnectionId) {
    // Get trade data
    let (gold1, items1) = match state.users.get(&user1) {
        Some(u) => (u.trade_gold, u.trade_items.clone()),
        None => return,
    };
    let (gold2, items2) = match state.users.get(&user2) {
        Some(u) => (u.trade_gold, u.trade_items.clone()),
        None => return,
    };

    // Transfer gold
    if let Some(u) = state.users.get_mut(&user1) {
        u.gold -= gold1;
        u.gold += gold2;
        u.gold = u.gold.max(0).min(MAX_GOLD);
    }
    if let Some(u) = state.users.get_mut(&user2) {
        u.gold -= gold2;
        u.gold += gold1;
        u.gold = u.gold.max(0).min(MAX_GOLD);
    }

    // Remove items from user1, give to user2
    for item in &items1 {
        remove_items_from_inv(state, user1, item.obj_index, item.amount).await;
        find_or_add_inv_slot(state, user2, item.obj_index, item.amount);
    }
    // Remove items from user2, give to user1
    for item in &items2 {
        remove_items_from_inv(state, user2, item.obj_index, item.amount).await;
        find_or_add_inv_slot(state, user1, item.obj_index, item.amount);
    }

    // Clear trade state
    for uid in &[user1, user2] {
        if let Some(u) = state.users.get_mut(uid) {
            u.trading = false;
            u.trade_partner = None;
            u.trade_offered = false;
            u.trade_accepted = false;
            u.trade_gold = 0;
            u.trade_items.clear();
        }
    }

    // Send updates
    let pkt = binary_packets::write_trade_ok();
    state.send_bytes(user1, &pkt).await;
    state.send_bytes(user2, &pkt).await;
    send_stats_gold(state, user1).await;
    send_stats_gold(state, user2).await;
    send_full_inventory(state, user1).await;
    send_full_inventory(state, user2).await;
}

/// Cancel trade between two users.
pub(super) async fn cancel_trade(state: &mut GameState, user1: ConnectionId, user2: ConnectionId) {
    let pkt = binary_packets::write_user_commerce_end();
    for uid in &[user1, user2] {
        if let Some(u) = state.users.get_mut(uid) {
            u.trading = false;
            u.trade_partner = None;
            u.trade_offered = false;
            u.trade_accepted = false;
            u.trade_gold = 0;
            u.trade_items.clear();
        }
        state.send_bytes(*uid, &pkt).await;
    }
}

/// TCM — Cancel trade.
pub(super) async fn handle_trade_cancel(state: &mut GameState, conn_id: ConnectionId) {
    let partner = match state.users.get(&conn_id) {
        Some(u) if u.trading => u.trade_partner,
        _ => return,
    };
    if let Some(p) = partner {
        cancel_trade(state, conn_id, p).await;
    }
}

/// VHC — Trade chat message.
pub(super) async fn handle_trade_chat(state: &mut GameState, conn_id: ConnectionId, data: &str) {
    let payload = strip_opcode(data, 3);
    let partner = match state.users.get(&conn_id) {
        Some(u) if u.trading => u.trade_partner,
        _ => return,
    };
    if let Some(p) = partner {
        let name = state.users.get(&conn_id).map(|u| u.char_name.clone()).unwrap_or_default();
        let msg = format!("{}: {}", name, payload);
        let pkt = binary_packets::write_commerce_chat(&msg);
        state.send_bytes(p, &pkt).await;
    }
}

// =====================================================================
// Banking handlers (modBancoNuevo.bas)
// =====================================================================

/// Maximum bank item stack.
const MAX_BANK_STACK: i32 = 999;

/// Open bank window (VB6: IniciarDeposito).
pub(super) async fn iniciar_banco(state: &mut GameState, conn_id: ConnectionId) {
    if let Some(u) = state.users.get(&conn_id) {
        if u.dead {
            let pkt = binary_packets::write_console_msg_id(3, ""); // TEXTO3: Estás muerto
            state.send_bytes(conn_id, &pkt).await;
            return;
        }
    }

    // VB6 IniciarDeposito: UpdateBanUserInv(True) → bank slots, SendUserGLD, INITBANCO, Comerciando=True
    enviar_banco_inv(state, conn_id).await;

    // Send user gold
    send_stats_gold(state, conn_id).await;

    // Send bank gold (VB6: SendBankGold in IniciarDeposito)
    let bank_gold = state.users.get(&conn_id).map(|u| u.bank_gold).unwrap_or(0);
    let pkt = binary_packets::write_update_bank_gold(bank_gold as i32);
    state.send_bytes(conn_id, &pkt).await;

    // Set comerciando flag (VB6 sets this)
    if let Some(user) = state.users.get_mut(&conn_id) {
        user.comerciando = true;
    }

    // Send init bank packet (includes bank gold for client init)
    let bank_gold = state.users.get(&conn_id).map(|u| u.bank_gold).unwrap_or(0);
    let pkt = binary_packets::write_bank_init(bank_gold as i32);
    state.send_bytes(conn_id, &pkt).await;
}

/// Send all bank slots to client (VB6: UpdateBanUserInv with UpdateAll=True).
/// VB6 sends SBR first to reset, then SBO for each non-empty slot.
/// SBO format: SBO<slot>,<obj_index>,<name>,<amount>,<grh>,<type>,<max_hit>,<min_hit>,<max_def>
pub(super) async fn enviar_banco_inv(state: &mut GameState, conn_id: ConnectionId) {
    // Send all bank slots via binary ChangeBankSlot packets.
    // Empty slots get zeroed data; non-empty slots get full object info.
    for idx in 0..MAX_BANK_SLOTS {
        let slot_num = (idx + 1) as u8;

        let slot_data = match state.users.get(&conn_id) {
            Some(u) if idx < u.bank.len() => {
                let s = &u.bank[idx];
                if s.obj_index > 0 {
                    state.get_object(s.obj_index).map(|o| {
                        (s.obj_index, o.name.clone(), s.amount, o.grh_index, o.obj_type as u8,
                         o.max_hit, o.min_hit, o.max_def, o.valor)
                    })
                } else {
                    None
                }
            }
            _ => None,
        };

        if let Some((obj_idx, name, amount, grh, obj_type, max_hit, min_hit, max_def, valor)) = slot_data {
            let pkt = binary_packets::write_change_bank_slot(
                slot_num, obj_idx as i16, &name, amount as i16,
                false, grh as i16, obj_type,
                max_hit as i16, min_hit as i16, max_def as i16, (valor / 3) as f32,
            );
            state.send_bytes(conn_id, &pkt).await;
        } else {
            // Empty slot
            let pkt = binary_packets::write_change_bank_slot(
                slot_num, 0, "", 0, false, 0, 0, 0, 0, 0, 0.0,
            );
            state.send_bytes(conn_id, &pkt).await;
        }
    }
}

/// DEPO,<slot>,<cantidad> — Deposit item to bank.
/// VB6: Right(rData, Len(rData) - 5) then ReadField with chr(44)
pub(super) async fn handle_bank_deposit(state: &mut GameState, conn_id: ConnectionId, data: &str) {
    let payload = strip_opcode(data, 5); // "DEPO," = 5 chars (VB6 strips 5)
    info!("[DEPO] #{} raw='{}' payload='{}'", conn_id, data, payload);
    let slot: usize = match read_field(1, payload, ',').parse() {
        Ok(v) if v >= 1 => v,
        _ => { info!("[DEPO] #{} bad slot: '{}'", conn_id, read_field(1, payload, ',')); return; },
    };
    let cantidad: i32 = match read_field(2, payload, ',').parse() {
        Ok(v) if v >= 1 => v,
        _ => { info!("[DEPO] #{} bad qty: '{}'", conn_id, read_field(2, payload, ',')); return; },
    };
    info!("[DEPO] #{} slot={} qty={}", conn_id, slot, cantidad);

    let slot_idx = slot - 1;

    // Get item from user inventory
    let (obj_index, user_amount, equipped) = match state.users.get(&conn_id) {
        Some(u) if u.logged && slot_idx < u.inventory.len() => {
            let s = &u.inventory[slot_idx];
            (s.obj_index, s.amount, s.equipped)
        }
        _ => return,
    };

    if obj_index <= 0 || user_amount <= 0 { return; }
    if equipped {
        let pkt = binary_packets::write_console_msg_id(185, ""); // TEXTO185: No podes depositar este objeto
        state.send_bytes(conn_id, &pkt).await;
        return;
    }

    // Check not intransferible
    let intransferible = state.get_object(obj_index).map(|o| o.intransferible).unwrap_or(false);
    if intransferible {
        let pkt = binary_packets::write_console_msg_id(223, ""); // TEXTO223: No puedes transferir este objeto
        state.send_bytes(conn_id, &pkt).await;
        return;
    }

    let cantidad = cantidad.min(user_amount);

    // Find bank slot (stack or empty)
    let bank_slot = {
        let user = match state.users.get(&conn_id) {
            Some(u) => u,
            None => return,
        };
        let mut stack = None;
        let mut empty = None;
        for (i, s) in user.bank.iter().enumerate() {
            if s.obj_index == obj_index && s.amount + cantidad <= MAX_BANK_STACK {
                stack = Some(i);
                break;
            }
            if s.obj_index == 0 && empty.is_none() {
                empty = Some(i);
            }
        }
        stack.or(empty)
    };

    let bank_idx = match bank_slot {
        Some(i) => i,
        None => {
            let pkt = binary_packets::write_console_msg_id(186, ""); // TEXTO186: No tienes mas espacio en el banco
            state.send_bytes(conn_id, &pkt).await;
            return;
        }
    };

    // Transfer
    if let Some(user) = state.users.get_mut(&conn_id) {
        // Remove from inventory
        user.inventory[slot_idx].amount -= cantidad;
        if user.inventory[slot_idx].amount <= 0 {
            user.inventory[slot_idx].obj_index = 0;
            user.inventory[slot_idx].amount = 0;
            user.inventory[slot_idx].equipped = false;
        }

        // Add to bank
        if user.bank[bank_idx].obj_index == 0 {
            user.bank[bank_idx].obj_index = obj_index;
            user.bank[bank_idx].amount = cantidad;
        } else {
            user.bank[bank_idx].amount += cantidad;
        }
    }

    // Send updates (VB6: UpdateBanUserInv(True) + UpdateVentanaBanco)
    send_inventory_slot(state, conn_id, slot_idx).await;
    enviar_banco_inv(state, conn_id).await;
    // Bank operation confirmation
    let pkt = binary_packets::write_bank_ok();
    state.send_bytes(conn_id, &pkt).await;
}

/// RETI,<slot>,<cantidad> — Withdraw item from bank.
/// VB6: Right(rData, Len(rData) - 5) then ReadField with chr(44)
pub(super) async fn handle_bank_withdraw(state: &mut GameState, conn_id: ConnectionId, data: &str) {
    let payload = strip_opcode(data, 5); // "RETI," = 5 chars (VB6 strips 5)
    let slot: usize = match read_field(1, payload, ',').parse() {
        Ok(v) if v >= 1 => v,
        _ => return,
    };
    let cantidad: i32 = match read_field(2, payload, ',').parse() {
        Ok(v) if v >= 1 => v,
        _ => return,
    };

    let slot_idx = slot - 1;

    // Get item from bank
    let (obj_index, bank_amount) = match state.users.get(&conn_id) {
        Some(u) if u.logged && slot_idx < u.bank.len() => {
            let s = &u.bank[slot_idx];
            (s.obj_index, s.amount)
        }
        _ => return,
    };

    if obj_index <= 0 || bank_amount <= 0 { return; }

    let cantidad = cantidad.min(bank_amount);

    // Find user inventory slot
    let inv_slot = find_or_add_inv_slot(state, conn_id, obj_index, cantidad);
    let inv_idx = match inv_slot {
        Some(i) => i,
        None => {
            let pkt = binary_packets::write_console_msg_id(108, ""); // TEXTO108: No podes cargar mas objetos
            state.send_bytes(conn_id, &pkt).await;
            return;
        }
    };

    // Remove from bank and compact (VB6 updateBInventory shifts items to fill gaps)
    if let Some(user) = state.users.get_mut(&conn_id) {
        user.bank[slot_idx].amount -= cantidad;
        if user.bank[slot_idx].amount <= 0 {
            // Shift remaining items down to fill gap (VB6 behavior)
            for i in slot_idx..user.bank.len() - 1 {
                user.bank[i] = user.bank[i + 1].clone();
            }
            let last = user.bank.len() - 1;
            user.bank[last].obj_index = 0;
            user.bank[last].amount = 0;
        }
    }

    // Send updates (VB6: UpdateBanUserInv(True) + UpdateVentanaBanco)
    send_inventory_slot(state, conn_id, inv_idx).await;
    send_stats_gold(state, conn_id).await;
    enviar_banco_inv(state, conn_id).await;
    // Bank operation confirmation
    let pkt = binary_packets::write_bank_ok();
    state.send_bytes(conn_id, &pkt).await;
}

/// FINBAN — Close bank/commerce window.
/// VB6: Both FINBAN and FINCOM reset Comerciando=False.
/// The NPC commerce window sends FINBAN to close (not FINCOM).
pub(super) async fn handle_bank_close(state: &mut GameState, conn_id: ConnectionId) {
    if let Some(user) = state.users.get_mut(&conn_id) {
        user.comerciando = false;
    }
    let pkt = binary_packets::write_bank_end();
    state.send_bytes(conn_id, &pkt).await;
}

