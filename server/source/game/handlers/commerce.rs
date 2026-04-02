//! Commerce handlers: NPC buy/sell, user-to-user trading, personal bank.
//! Extracted from mod.rs to reduce file size.

use tracing::{info, warn};
use crate::net::ConnectionId;
use crate::game::types::{GameState, InventorySlot, MAX_BANK_SLOTS};
use crate::protocol::{binary_packets, font_index};
use crate::data::objects::ObjType;
use super::common::*;
use super::{send_inventory_slot, send_full_inventory};
use super::skills::try_level_skill_with_hit;

/// Commerce skill index in skills array.
const SK_COMERCIAR: usize = 9; // VB6 eSkill.Comerciar = 10 (1-based), 0-based = 9

/// Start NPC commerce (VB6: IniciarComercioNPC).
/// Sends NPC inventory, gold, sets flag, sends INITCOM.
pub(super) async fn iniciar_comercio_npc(state: &mut GameState, conn_id: ConnectionId, npc_idx: usize) {
    // Check user not dead
    if let Some(u) = state.users.get(&conn_id) {
        if u.dead {
            let pkt = binary_packets::write_console_msg_id(3, ""); // TEXTO3: Estás muerto
            state.send_bytes(conn_id, &pkt);
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
    state.send_bytes(conn_id, &pkt);
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
        state.send_bytes(conn_id, &pkt);

        for (obj_index, amount, idx) in &npc_data {
            if *obj_index <= 0 { continue; }
            let obj = match state.get_object(*obj_index) {
                Some(o) => o.clone(),
                None => continue,
            };

            let infla = (inflacion as i64 * obj.valor as i64) / 100;
            let price = ((obj.valor as i64 + infla) as f64 / descuento + 0.5) as i64;

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
                obj.min_def as i16,
            );
            state.send_bytes(conn_id, &pkt);
        }
    } else {
        // Single slot update
        let idx = slot - 1; // Convert to 0-based
        if idx >= npc_data.len() { return; }
        let (obj_index, amount, _) = npc_data[idx];

        if obj_index <= 0 {
            // Empty slot — send zeroed data
            let pkt = binary_packets::write_change_npc_inv_slot(
                slot as u8, "(Nada)", 0, 0.0, 0, 0, 0, 0, 0, 0, 0,
            );
            state.send_bytes(conn_id, &pkt);
        } else {
            let obj = match state.get_object(obj_index) {
                Some(o) => o.clone(),
                None => return,
            };
            let infla = (inflacion as i64 * obj.valor as i64) / 100;
            let price = ((obj.valor as i64 + infla) as f64 / descuento + 0.5) as i64;

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
                obj.min_def as i16,
            );
            state.send_bytes(conn_id, &pkt);
        }
    }
}

/// COMP — User buys from NPC (VB6: NPCVentaItem / UserCompraObj).
pub(super) async fn handle_commerce_buy(state: &mut GameState, conn_id: ConnectionId, slot: usize, cantidad: i32) {
    info!("[COMP] #{} slot={} qty={}", conn_id, slot, cantidad);
    if slot < 1 || cantidad < 1 { return; }

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
        state.send_bytes(conn_id, &pkt);
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

    // VB6: Faction armor restriction — Real/Chaos items can only be bought by faction members
    if obj.real || obj.caos {
        let (is_real, is_caos) = state.users.get(&conn_id)
            .map(|u| (u.armada_real, u.fuerzas_caos))
            .unwrap_or((false, false));
        if obj.real && !is_real {
            state.send_console(conn_id, "Solo miembros de la Armada Real pueden comprar este objeto.", font_index::INFO);
            return;
        }
        if obj.caos && !is_caos {
            state.send_console(conn_id, "Solo miembros de la Legion del Caos pueden comprar este objeto.", font_index::INFO);
            return;
        }
    }

    // Calculate buy price
    let descuento = 1.0 + comerciar_skill as f64 / 100.0;
    let descuento = if descuento <= 0.0 { 1.0 } else { descuento };
    let infla = (npc_inflacion as i64 * obj.valor as i64) / 100;
    let unit_price = ((obj.valor as i64 + infla) as f64 / descuento + 0.5) as i64;
    let total_price = unit_price * cantidad as i64;

    // Check gold
    if user_gold < total_price {
        let pkt = binary_packets::write_console_msg_id(663, ""); // TEXTO663: No tenes suficiente dinero
        state.send_bytes(conn_id, &pkt);
        return;
    }

    // Find user inventory slot (try stacking first, then empty within current_inventory_slots)
    let user_inv_slot = {
        let user = match state.users.get(&conn_id) {
            Some(u) => u,
            None => return,
        };
        let max_slots = user.current_inventory_slots;

        // Try to stack
        let mut stack_slot = None;
        let mut empty_slot = None;
        for (i, inv) in user.inventory.iter().enumerate() {
            if inv.obj_index == obj_index && inv.amount + cantidad <= MAX_INVENTORY_OBJS {
                stack_slot = Some(i);
                break;
            }
            if i < max_slots && inv.obj_index == 0 && empty_slot.is_none() {
                empty_slot = Some(i);
            }
        }
        stack_slot.or(empty_slot)
    };

    let user_slot = match user_inv_slot {
        Some(s) => s,
        None => {
            let pkt = binary_packets::write_console_msg_id(108, ""); // TEXTO108: No podes cargar mas objetos
            state.send_bytes(conn_id, &pkt);
            return;
        }
    };

    // Execute transaction: add to user, deduct gold
    if let Some(user) = state.users.get_mut(&conn_id) {
        if user.inventory[user_slot].obj_index == 0 {
            user.inventory[user_slot].obj_index = obj_index;
        user.inventory[user_slot].amount = cantidad;
        user.inventory[user_slot].equipped = false;
        } else {
            user.inventory[user_slot].amount += cantidad;
        }
        user.gold -= total_price;
        if user.gold < 0 { user.gold = 0; }
    }

    // Get NPC static data (original inventory + inv_respawn flag) before mutating
    let (npc_data_items, npc_inv_respawn) = {
        let npc_number = state.get_npc(target_npc).map(|n| n.npc_number).unwrap_or(0);
        match state.game_data.npcs.get(npc_number) {
            Some(d) => (d.items.clone(), d.inv_respawn),
            None => (Vec::new(), false),
        }
    };

    // Remove from NPC inventory. VB6 parity: items deplete when bought.
    // Restock only happens when the ENTIRE inventory is empty AND inv_respawn != true.
    if let Some(npc) = state.get_npc_mut(target_npc) {
        npc.inventory[slot_idx].amount -= cantidad;
        if npc.inventory[slot_idx].amount <= 0 {
            // Slot depleted — clear it
            npc.inventory[slot_idx].obj_index = 0;
            npc.inventory[slot_idx].amount = 0;
        }

        // Check if all slots are now empty (only non-empty original slots count)
        if !npc_inv_respawn {
            let all_empty = npc.inventory.iter().enumerate().all(|(i, slot)| {
                let orig_obj = npc_data_items.get(i).map(|s| s.obj_index).unwrap_or(0);
                orig_obj == 0 || slot.obj_index == 0
            });
            if all_empty {
                // Restock entire inventory from original data
                for (i, orig) in npc_data_items.iter().enumerate() {
                    if i < npc.inventory.len() && orig.obj_index > 0 {
                        npc.inventory[i].obj_index = orig.obj_index;
                        npc.inventory[i].amount = orig.amount;
                    }
                }
            }
        }
    }

    // Send updates to client
    send_inventory_slot(state, conn_id, user_slot).await;
    send_stats_gold(state, conn_id).await;
    enviar_npc_inv(state, conn_id, target_npc, slot).await;

    let pkt = binary_packets::write_trans_ok(slot as u8, 0); // 0 = buy
    state.send_bytes(conn_id, &pkt);

    // Security audit: log high-quantity transactions (>1000 items)
    if cantidad > 1000 {
        let char_name = state.users.get(&conn_id).map(|u| u.char_name.clone()).unwrap_or_default();
        let obj_name = obj.name.clone();
        warn!("[COMMERCE-AUDIT] BUY: user='{}' conn={} obj='{}' (#{}) qty={} total_gold={}",
            char_name, conn_id, obj_name, obj_index, cantidad, total_price);
    }

    // VB6: SubirSkill(UserIndex, Comerciar, True) — commerce XP on buy
    if let Some(user) = state.users.get_mut(&conn_id) {
        try_level_skill_with_hit(user, SK_COMERCIAR, true);
    }
}

/// VEND — User sells to NPC (VB6: NPCCompraItem / NpcCompraObj).
pub(super) async fn handle_commerce_sell(state: &mut GameState, conn_id: ConnectionId, slot: usize, cantidad: i32) {
    if slot < 1 || cantidad < 1 { return; }

    // Validate user state
    let (dead, comerciando, target_npc) = match state.users.get(&conn_id) {
        Some(u) if u.logged => (u.dead, u.comerciando, u.target_npc),
        _ => return,
    };

    if dead {
        let pkt = binary_packets::write_console_msg_id(3, ""); // TEXTO3: Estás muerto
        state.send_bytes(conn_id, &pkt);
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
        state.send_bytes(conn_id, &pkt);
        return;
    }

    // Get object data
    let obj = match state.get_object(obj_index) {
        Some(o) => o.clone(),
        None => return,
    };

    // VB6 13.3: Faction items can only be sold to matching faction NPCs (SR/SC).
    if obj.real {
        let npc_name = state.get_npc(target_npc)
            .map(|n| n.name.to_uppercase())
            .unwrap_or_default();
        if npc_name != "SR" {
            state.send_console(conn_id, "Solo puedes vender objetos reales al Soldado Real.", font_index::INFO);
            return;
        }
    }
    if obj.caos {
        let npc_name = state.get_npc(target_npc)
            .map(|n| n.name.to_uppercase())
            .unwrap_or_default();
        if npc_name != "SC" {
            state.send_console(conn_id, "Solo puedes vender objetos del caos al Soldado del Caos.", font_index::INFO);
            return;
        }
    }

    // Reject conditions (VB6: NpcCompraObj)
    if obj.newbie {
        let pkt = binary_packets::write_console_msg_id(660, ""); // TEXTO660: No comercio objetos para newbies
        state.send_bytes(conn_id, &pkt);
        return;
    }
    if obj_index == GOLD_OBJ_INDEX {
        let pkt = binary_packets::write_console_msg_id(661, ""); // TEXTO661: El npc no esta interesado en comprar ese objeto
        state.send_bytes(conn_id, &pkt);
        return;
    }
    if obj.obj_type == ObjType::Key {
        let pkt = binary_packets::write_console_msg_id(661, ""); // TEXTO661: El npc no esta interesado en comprar ese objeto
        state.send_bytes(conn_id, &pkt);
        return;
    }
    // TipoItems filter: 0 = buys anything, otherwise must match
    if npc_tipo_items != 0 && npc_tipo_items != obj.obj_type as i32 {
        let pkt = binary_packets::write_console_msg_id(661, ""); // TEXTO661: El npc no esta interesado en comprar ese objeto
        state.send_bytes(conn_id, &pkt);
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
        let remaining = user.inventory[user_slot_idx].amount;
        if remaining <= 0 {
            user.inventory[user_slot_idx].obj_index = 0;
            user.inventory[user_slot_idx].amount = 0;
            user.inventory[user_slot_idx].equipped = false;
        }
        // Add gold (capped)
        let new_gold = (user.gold + sell_price).min(MAX_GOLD);
        user.gold = new_gold;
    }

    // Send updates
    send_inventory_slot(state, conn_id, user_slot_idx).await;
    send_stats_gold(state, conn_id).await;

    // Send NPC inventory update for the slot that changed
    if let Some(npc_slot_idx) = npc_slot {
        enviar_npc_inv(state, conn_id, target_npc, npc_slot_idx + 1).await;
    }

    let pkt = binary_packets::write_trans_ok(slot as u8, 1); // 1 = sell
    state.send_bytes(conn_id, &pkt);

    // Security audit: log high-quantity transactions (>1000 items)
    if cantidad > 1000 {
        let char_name = state.users.get(&conn_id).map(|u| u.char_name.clone()).unwrap_or_default();
        let obj_name = obj.name.clone();
        warn!("[COMMERCE-AUDIT] SELL: user='{}' conn={} obj='{}' (#{}) qty={} gold_gained={}",
            char_name, conn_id, obj_name, obj_index, cantidad, sell_price);
    }

    // VB6: SubirSkill(UserIndex, Comerciar, True) — commerce XP on sell
    if let Some(user) = state.users.get_mut(&conn_id) {
        try_level_skill_with_hit(user, SK_COMERCIAR, true);
    }
}

/// FINCOM — Close commerce window.
pub(super) async fn handle_commerce_close(state: &mut GameState, conn_id: ConnectionId) {
    if let Some(user) = state.users.get_mut(&conn_id) {
        user.comerciando = false;
    }
    let pkt = binary_packets::write_commerce_end();
    state.send_bytes(conn_id, &pkt);
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
    // H6 fix: also block trade initiation while the user (or target) is in the bank
    // (comerciando=true). Allowing trade while banking could cause inventory corruption
    // since the same items could be simultaneously in a trade offer and in a bank
    // transaction, leading to duplication or loss on completion/cancellation.
    let user_ok = state.users.get(&conn_id)
        .map(|u| !u.dead && u.logged && !u.trading && !u.comerciando)
        .unwrap_or(false);
    let target_ok = state.users.get(&target_conn)
        .map(|u| !u.dead && u.logged && !u.trading && !u.comerciando)
        .unwrap_or(false);

    if !user_ok {
        let pkt = binary_packets::write_error_show(
            "No puedes iniciar un comercio mientras tienes otra ventana de comercio abierta."
        );
        state.send_bytes(conn_id, &pkt);
        return;
    }
    if !target_ok {
        let pkt = binary_packets::write_error_show(
            "El otro jugador no puede comerciar ahora mismo."
        );
        state.send_bytes(conn_id, &pkt);
        return;
    }

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
    state.send_bytes(conn_id, &pkt1);
    let pkt2 = binary_packets::write_user_commerce_init(&user_name);
    state.send_bytes(target_conn, &pkt2);
}

/// UOR — Offer gold in trade.
pub(super) async fn handle_trade_offer_gold(state: &mut GameState, conn_id: ConnectionId, gold: i64) {
    if gold < 0 { return; }

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
    state.send_bytes(partner, &pkt);
}

/// UOC — Offer items in trade.
pub(super) async fn handle_trade_offer_item(state: &mut GameState, conn_id: ConnectionId, slot: usize, cantidad: i32) {
    if slot < 1 || cantidad < 1 { return; }

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
        state.send_bytes(conn_id, &pkt);
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
    state.send_bytes(partner, &pkt);
}

/// TDR — Trade response (0=accept, 1=reject).
pub(super) async fn handle_trade_response(state: &mut GameState, conn_id: ConnectionId, response: i32) {

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

    // Re-validate that none of the offered items are currently equipped BEFORE any transfer.
    // A user could have equipped an item between offering it and accepting the trade,
    // which would lead to transferring an item that is actively worn.
    let user1_has_equipped = items1.iter().any(|item| {
        state.users.get(&user1)
            .map(|u| u.inventory.iter().any(|slot| {
                slot.obj_index == item.obj_index && slot.equipped
            }))
            .unwrap_or(false)
    });
    let user2_has_equipped = items2.iter().any(|item| {
        state.users.get(&user2)
            .map(|u| u.inventory.iter().any(|slot| {
                slot.obj_index == item.obj_index && slot.equipped
            }))
            .unwrap_or(false)
    });

    if user1_has_equipped || user2_has_equipped {
        // Abort the trade and notify both parties — no gold or items have moved yet.
        cancel_trade(state, user1, user2).await;
        state.send_console(user1, "El trato fue cancelado: un objeto ofrecido está equipado.", font_index::INFO);
        state.send_console(user2, "El trato fue cancelado: un objeto ofrecido está equipado.", font_index::INFO);
        return;
    }

    // Transfer gold
    if let Some(u) = state.users.get_mut(&user1) {
        let new_gold = u.gold.saturating_sub(gold1).saturating_add(gold2).min(MAX_GOLD);
        u.gold = new_gold;
    }
    if let Some(u) = state.users.get_mut(&user2) {
        let new_gold = u.gold.saturating_sub(gold2).saturating_add(gold1).min(MAX_GOLD);
        u.gold = new_gold;
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
    state.send_bytes(user1, &pkt);
    state.send_bytes(user2, &pkt);
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
        state.send_bytes(*uid, &pkt);
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
pub(super) async fn handle_trade_chat(state: &mut GameState, conn_id: ConnectionId, msg: &str) {
    let partner = match state.users.get(&conn_id) {
        Some(u) if u.trading => u.trade_partner,
        _ => return,
    };
    if let Some(p) = partner {
        let name = state.users.get(&conn_id).map(|u| u.char_name.clone()).unwrap_or_default();
        let msg = format!("{}: {}", name, msg);
        let pkt = binary_packets::write_commerce_chat(&msg);
        state.send_bytes(p, &pkt);
    }
}

// =====================================================================
// Banking handlers (modBancoNuevo.bas)
// =====================================================================

/// Maximum bank item stack.
const MAX_BANK_STACK: i32 = 10_000; // VB6 13.3: MAX_INVENTORY_OBJS = 10000

/// Check if another character from the same account is currently using the bank.
/// Defense-in-depth against item duplication via concurrent bank access.
fn is_same_account_banking(state: &GameState, conn_id: ConnectionId) -> bool {
    let my_account = match state.users.get(&conn_id) {
        Some(u) if !u.account_name.is_empty() => u.account_name.clone(),
        _ => return false,
    };
    state.users.values().any(|u| {
        u.conn_id != conn_id
            && u.logged
            && u.comerciando
            && u.account_name.eq_ignore_ascii_case(&my_account)
    })
}

/// Open bank window (VB6: IniciarDeposito).
pub(super) async fn iniciar_banco(state: &mut GameState, conn_id: ConnectionId) {
    if let Some(u) = state.users.get(&conn_id) {
        if u.dead {
            let pkt = binary_packets::write_console_msg_id(3, ""); // TEXTO3: Estás muerto
            state.send_bytes(conn_id, &pkt);
            return;
        }
    }

    // H6 fix: block bank access while the user is in a player-to-player trade.
    // Opening the bank during an active trade could allow inventory corruption:
    // items could be simultaneously offered in trade and deposited/withdrawn from
    // the bank, resulting in item duplication or loss.
    if state.users.get(&conn_id).map(|u| u.trading).unwrap_or(false) {
        let pkt = binary_packets::write_error_show(
            "No puedes acceder a la bóveda mientras estás comerciando con otro jugador."
        );
        state.send_bytes(conn_id, &pkt);
        return;
    }

    // Safety: block bank access if another character from the same account is already banking.
    // Prevents item duplication via race condition if two chars from same account are online.
    if is_same_account_banking(state, conn_id) {
        let pkt = binary_packets::write_error_show(
            "No puedes acceder a la bóveda porque otro personaje de tu cuenta la está usando."
        );
        state.send_bytes(conn_id, &pkt);
        return;
    }

    // VB6 IniciarDeposito: UpdateBanUserInv(True) → bank slots, SendUserGLD, INITBANCO, Comerciando=True
    enviar_banco_inv(state, conn_id).await;

    // Send user gold
    send_stats_gold(state, conn_id).await;

    // Send bank gold (VB6: SendBankGold in IniciarDeposito)
    let bank_gold = state.users.get(&conn_id).map(|u| u.bank_gold).unwrap_or(0);
    let pkt = binary_packets::write_update_bank_gold(bank_gold as i32);
    state.send_bytes(conn_id, &pkt);

    // Set comerciando flag (VB6 sets this)
    if let Some(user) = state.users.get_mut(&conn_id) {
        user.comerciando = true;
    }

    // Send init bank packet (includes bank gold for client init)
    let bank_gold = state.users.get(&conn_id).map(|u| u.bank_gold).unwrap_or(0);
    let pkt = binary_packets::write_bank_init(bank_gold as i32);
    state.send_bytes(conn_id, &pkt);
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
                         o.max_hit, o.min_hit, o.max_def, o.min_def, o.valor)
                    })
                } else {
                    None
                }
            }
            _ => None,
        };

        if let Some((obj_idx, name, amount, grh, obj_type, max_hit, min_hit, max_def, min_def, valor)) = slot_data {
            let pkt = binary_packets::write_change_bank_slot(
                slot_num, obj_idx as i16, &name, amount as i16,
                false, grh as i16, obj_type,
                max_hit as i16, min_hit as i16, max_def as i16, min_def as i16, (valor / 3) as f32,
            );
            state.send_bytes(conn_id, &pkt);
        } else {
            // Empty slot
            let pkt = binary_packets::write_change_bank_slot(
                slot_num, 0, "", 0, false, 0, 0, 0, 0, 0, 0, 0.0,
            );
            state.send_bytes(conn_id, &pkt);
        }
    }
}

/// DEPO,<slot>,<cantidad> — Deposit item to bank.
/// VB6: Right(rData, Len(rData) - 5) then ReadField with chr(44)
pub(super) async fn handle_bank_deposit(state: &mut GameState, conn_id: ConnectionId, slot: usize, cantidad: i32) {
    info!("[DEPO] #{} slot={} qty={}", conn_id, slot, cantidad);
    if slot < 1 || cantidad < 1 { return; }

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
        state.send_bytes(conn_id, &pkt);
        return;
    }

    // Check not intransferible
    let intransferible = state.get_object(obj_index).map(|o| o.intransferible).unwrap_or(false);
    if intransferible {
        let pkt = binary_packets::write_console_msg_id(223, ""); // TEXTO223: No puedes transferir este objeto
        state.send_bytes(conn_id, &pkt);
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
            state.send_bytes(conn_id, &pkt);
            return;
        }
    };

    // Transfer
    if let Some(user) = state.users.get_mut(&conn_id) {
        // Remove from inventory
        user.inventory[slot_idx].amount -= cantidad;
        let remaining = user.inventory[slot_idx].amount;
        if remaining <= 0 {
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
    state.send_bytes(conn_id, &pkt);
}

/// RETI,<slot>,<cantidad> — Withdraw item from bank.
/// VB6: Right(rData, Len(rData) - 5) then ReadField with chr(44)
pub(super) async fn handle_bank_withdraw(state: &mut GameState, conn_id: ConnectionId, slot: usize, cantidad: i32) {
    if slot < 1 || cantidad < 1 { return; }

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
            state.send_bytes(conn_id, &pkt);
            return;
        }
    };

    // Remove from bank and compact (VB6 updateBInventory shifts items to fill gaps)
    if let Some(user) = state.users.get_mut(&conn_id) {
        user.bank[slot_idx].amount -= cantidad;
        if user.bank[slot_idx].amount <= 0 {
            // Shift remaining items down to fill gap (VB6 behavior)
            let last = user.bank.len() - 1;
            for i in slot_idx..last {
                let next = user.bank[i + 1].clone();
                user.bank[i] = next;
            }
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
    state.send_bytes(conn_id, &pkt);
}

/// FINBAN — Close bank/commerce window.
/// VB6: Both FINBAN and FINCOM reset Comerciando=False.
/// The NPC commerce window sends FINBAN to close (not FINCOM).
pub(super) async fn handle_bank_close(state: &mut GameState, conn_id: ConnectionId) {
    if let Some(user) = state.users.get_mut(&conn_id) {
        user.comerciando = false;
    }
    let pkt = binary_packets::write_bank_end();
    state.send_bytes(conn_id, &pkt);
}

// ──────────────────────────────────────────────────────────────
// Guild bank helpers
// ──────────────────────────────────────────────────────────────

/// Send one guild bank slot to the client.
fn send_guild_bank_slot(state: &mut GameState, conn_id: ConnectionId, slot_num: u8, obj_index: i32, amount: i32) {
    if obj_index > 0 {
        if let Some(obj) = state.get_object(obj_index) {
            let pkt = binary_packets::write_guild_bank_slot(
                slot_num, obj_index as i16, &obj.name.clone(), amount as i16,
                obj.grh_index as i16, obj.obj_type as u8,
                obj.max_hit as i16, obj.min_hit as i16, obj.max_def as i16, obj.min_def as i16, obj.valor as f32,
            );
            state.send_bytes(conn_id, &pkt);
            return;
        }
    }
    let pkt = binary_packets::write_guild_bank_slot(slot_num, 0, "", 0, 0, 0, 0, 0, 0, 0, 0.0);
    state.send_bytes(conn_id, &pkt);
}

/// Deposit item from user inventory into guild bank.
pub(super) async fn handle_guild_bank_deposit(state: &mut GameState, conn_id: ConnectionId, slot: usize, cantidad: i32) {
    if slot < 1 || cantidad < 1 { return; }
    let slot_idx = slot - 1;

    if !state.users.get(&conn_id).map(|u| u.guild_bank_open).unwrap_or(false) { return; }

    let guild_name = match state.users.get(&conn_id) {
        Some(u) if !u.guild_name.is_empty() => u.guild_name.clone(),
        _ => return,
    };

    let (obj_index, user_amount, equipped) = match state.users.get(&conn_id) {
        Some(u) if u.logged && slot_idx < u.inventory.len() => {
            let s = &u.inventory[slot_idx];
            (s.obj_index, s.amount, s.equipped)
        }
        _ => return,
    };
    if obj_index <= 0 || user_amount <= 0 { return; }
    if equipped {
        state.send_msg_id(conn_id, 185, "");
        return;
    }
    let intransferible = state.get_object(obj_index).map(|o| o.intransferible).unwrap_or(false);
    if intransferible {
        state.send_msg_id(conn_id, 223, "");
        return;
    }

    let cantidad = cantidad.min(user_amount);

    let pool = state.pool.clone();
    let mut bank_items = crate::db::guilds::load_bank_items(&pool, &guild_name).await;

    let bank_slot = {
        let mut stack: Option<usize> = None;
        let mut empty: Option<usize> = None;
        for (i, s) in bank_items.iter().enumerate() {
            if s.obj_index == obj_index && s.amount + cantidad <= 10000 {
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
            state.send_console(conn_id, "No hay espacio en la boveda del clan.", font_index::INFO);
            return;
        }
    };

    // Remove from inventory
    if let Some(user) = state.users.get_mut(&conn_id) {
        user.inventory[slot_idx].amount -= cantidad;
        if user.inventory[slot_idx].amount <= 0 {
            user.inventory[slot_idx].obj_index = 0;
            user.inventory[slot_idx].amount = 0;
            user.inventory[slot_idx].equipped = false;
        }
    }

    // Add to guild bank
    if bank_items[bank_idx].obj_index == 0 {
        bank_items[bank_idx].obj_index = obj_index;
        bank_items[bank_idx].amount = cantidad;
    } else {
        bank_items[bank_idx].amount += cantidad;
    }

    crate::db::guilds::save_bank_items(&pool, &guild_name, &bank_items).await;

    send_inventory_slot(state, conn_id, slot_idx).await;

    let slot_num = (bank_idx + 1) as u8;
    let (new_obj, new_amt) = (bank_items[bank_idx].obj_index, bank_items[bank_idx].amount);
    send_guild_bank_slot(state, conn_id, slot_num, new_obj, new_amt);
}

/// Withdraw item from guild bank into user inventory.
pub(super) async fn handle_guild_bank_withdraw(state: &mut GameState, conn_id: ConnectionId, slot: usize, cantidad: i32) {
    if slot < 1 || cantidad < 1 { return; }
    let slot_idx = slot - 1;

    let can_withdraw = state.users.get(&conn_id)
        .map(|u| u.guild_bank_open && u.can_withdraw_items)
        .unwrap_or(false);
    if !can_withdraw {
        if state.users.get(&conn_id).map(|u| u.guild_bank_open).unwrap_or(false) {
            state.send_console(conn_id, "No tienes permiso para retirar objetos de la boveda del clan.", font_index::INFO);
        }
        return;
    }

    let guild_name = match state.users.get(&conn_id) {
        Some(u) if !u.guild_name.is_empty() => u.guild_name.clone(),
        _ => return,
    };

    let pool = state.pool.clone();
    let mut bank_items = crate::db::guilds::load_bank_items(&pool, &guild_name).await;

    if slot_idx >= bank_items.len() { return; }
    let (obj_index, bank_amount) = (bank_items[slot_idx].obj_index, bank_items[slot_idx].amount);
    if obj_index <= 0 || bank_amount <= 0 { return; }

    let cantidad = cantidad.min(bank_amount);

    let inv_slot = find_or_add_inv_slot(state, conn_id, obj_index, cantidad);
    let inv_idx = match inv_slot {
        Some(i) => i,
        None => {
            state.send_msg_id(conn_id, 108, "");
            return;
        }
    };

    // Remove from guild bank
    bank_items[slot_idx].amount -= cantidad;
    if bank_items[slot_idx].amount <= 0 {
        bank_items[slot_idx].obj_index = 0;
        bank_items[slot_idx].amount = 0;
    }

    crate::db::guilds::save_bank_items(&pool, &guild_name, &bank_items).await;

    send_inventory_slot(state, conn_id, inv_idx).await;

    let slot_num = (slot_idx + 1) as u8;
    let (new_obj, new_amt) = (bank_items[slot_idx].obj_index, bank_items[slot_idx].amount);
    send_guild_bank_slot(state, conn_id, slot_num, new_obj, new_amt);
}

/// Deposit gold from user into guild bank.
/// Called when BankDeposit packet arrives with slot == 0 and guild_bank_open.
pub(super) async fn handle_guild_bank_deposit_gold(state: &mut GameState, conn_id: ConnectionId, amount: i32) {
    if amount < 1 { return; }
    if !state.users.get(&conn_id).map(|u| u.guild_bank_open).unwrap_or(false) { return; }

    let guild_name = match state.users.get(&conn_id) {
        Some(u) if !u.guild_name.is_empty() => u.guild_name.clone(),
        _ => return,
    };

    let user_gold = match state.users.get(&conn_id) {
        Some(u) if u.logged => u.gold,
        _ => return,
    };

    if user_gold < amount as i64 {
        state.send_console(conn_id, "No tienes suficiente oro.", font_index::INFO);
        return;
    }

    let pool = state.pool.clone();
    let bank_gold = crate::db::guilds::load_bank_gold(&pool, &guild_name).await;
    let new_bank_gold = bank_gold + amount as i64;
    crate::db::guilds::save_bank_gold(&pool, &guild_name, new_bank_gold).await;

    if let Some(user) = state.users.get_mut(&conn_id) {
        user.gold -= amount as i64;
    }

    send_stats_gold(state, conn_id).await;
    let pkt = binary_packets::write_guild_bank_gold((new_bank_gold.min(i32::MAX as i64)) as i32);
    state.send_bytes(conn_id, &pkt);
}

/// Withdraw gold from guild bank into user inventory.
/// Called when BankWithdraw packet arrives with slot == 0 and guild_bank_open.
pub(super) async fn handle_guild_bank_withdraw_gold(state: &mut GameState, conn_id: ConnectionId, amount: i32) {
    if amount < 1 { return; }

    let can_withdraw = state.users.get(&conn_id)
        .map(|u| u.guild_bank_open && u.can_withdraw_gold)
        .unwrap_or(false);
    if !can_withdraw {
        if state.users.get(&conn_id).map(|u| u.guild_bank_open).unwrap_or(false) {
            state.send_console(conn_id, "No tienes permiso para retirar oro de la boveda del clan.", font_index::INFO);
        }
        return;
    }

    let guild_name = match state.users.get(&conn_id) {
        Some(u) if !u.guild_name.is_empty() => u.guild_name.clone(),
        _ => return,
    };

    let pool = state.pool.clone();
    let bank_gold = crate::db::guilds::load_bank_gold(&pool, &guild_name).await;

    if bank_gold < amount as i64 {
        state.send_console(conn_id, "No hay suficiente oro en la boveda del clan.", font_index::INFO);
        return;
    }

    let new_bank_gold = bank_gold - amount as i64;
    crate::db::guilds::save_bank_gold(&pool, &guild_name, new_bank_gold).await;

    if let Some(user) = state.users.get_mut(&conn_id) {
        user.gold += amount as i64;
    }

    send_stats_gold(state, conn_id).await;
    let pkt = binary_packets::write_guild_bank_gold((new_bank_gold.min(i32::MAX as i64)) as i32);
    state.send_bytes(conn_id, &pkt);
}

/// Close guild bank window.
pub(super) async fn handle_guild_bank_close(state: &mut GameState, conn_id: ConnectionId) {
    if let Some(user) = state.users.get_mut(&conn_id) {
        user.guild_bank_open = false;
        user.comerciando = false;
    }
    let pkt = binary_packets::write_bank_end();
    state.send_bytes(conn_id, &pkt);
}

/// Open guild bank window (BoveClan NPC).
pub(super) async fn iniciar_banco_clan(state: &mut GameState, conn_id: ConnectionId) {
    if state.users.get(&conn_id).map(|u| u.dead).unwrap_or(true) {
        state.send_msg_id(conn_id, 3, "");
        return;
    }

    if state.users.get(&conn_id).map(|u| u.trading).unwrap_or(false) {
        let pkt = binary_packets::write_error_show(
            "No puedes acceder a la bóveda de clan mientras comercias."
        );
        state.send_bytes(conn_id, &pkt);
        return;
    }

    let guild_name = match state.users.get(&conn_id) {
        Some(u) if u.guild_index > 0 && !u.guild_name.is_empty() => u.guild_name.clone(),
        _ => {
            state.send_console(conn_id, "No perteneces a un clan.", crate::protocol::font_index::INFO);
            return;
        }
    };

    let pool = state.pool.clone();
    let bank_items = crate::db::guilds::load_bank_items(&pool, &guild_name).await;
    let bank_gold = crate::db::guilds::load_bank_gold(&pool, &guild_name).await;

    for idx in 0..crate::db::guilds::MAX_GUILD_BANK_SLOTS {
        let slot_num = (idx + 1) as u8;
        let (obj_index, amount) = {
            let slot = &bank_items[idx];
            (slot.obj_index, slot.amount)
        };

        if obj_index > 0 {
            let obj_data = state.get_object(obj_index).map(|o| {
                (o.name.clone(), o.grh_index as i16, o.obj_type as u8,
                 o.max_hit as i16, o.min_hit as i16, o.max_def as i16, o.min_def as i16, o.valor as f32)
            });
            if let Some((name, grh, obj_type, max_hit, min_hit, max_def, min_def, valor)) = obj_data {
                let pkt = binary_packets::write_guild_bank_slot(
                    slot_num,
                    obj_index as i16,
                    &name,
                    amount as i16,
                    grh, obj_type, max_hit, min_hit, max_def, min_def, valor,
                );
                state.send_bytes(conn_id, &pkt);
            } else {
                let pkt = binary_packets::write_guild_bank_slot(slot_num, 0, "", 0, 0, 0, 0, 0, 0, 0, 0.0);
                state.send_bytes(conn_id, &pkt);
            }
        } else {
            let pkt = binary_packets::write_guild_bank_slot(slot_num, 0, "", 0, 0, 0, 0, 0, 0, 0, 0.0);
            state.send_bytes(conn_id, &pkt);
        }
    }

    // Note: bank_gold is i64 from DB but protocol wire format is i32 (4-byte signed).
    // Saturate to prevent silent wraparound on servers with very high gold.
    let gold_i32 = (bank_gold.min(i32::MAX as i64)) as i32;

    // VB6 order: slots (above) → gold → set comerciando → init (matches iniciar_banco)
    let pkt = binary_packets::write_guild_bank_gold(gold_i32);
    state.send_bytes(conn_id, &pkt);

    if let Some(user) = state.users.get_mut(&conn_id) {
        user.guild_bank_open = true;
        user.comerciando = true;
    }

    let pkt = binary_packets::write_guild_bank_init(gold_i32);
    state.send_bytes(conn_id, &pkt);
}


