//! GM item/NPC commands: /ITEM, /ACC, /MATA, /MASSKILL, /DEST, etc.

use tracing::{info};
use crate::net::ConnectionId;
use crate::game::types::{GameState, SendTarget, InventorySlot, privilege_level};
use crate::protocol::{font_index, binary_packets};
use super::world;
use super::{send_inventory_slot, send_full_inventory, find_closest_legal_pos};

/// /ITEM objid amount — Create item in inventory (requires DIOS+).
pub(super) async fn handle_slash_item(state: &mut GameState, conn_id: ConnectionId, args: &str) {
    match state.users.get(&conn_id) {
        Some(u) if u.logged && u.privileges >= privilege_level::DIOS => {}
        _ => {
            state.send_console(conn_id, "No tenes permisos para usar este comando.", font_index::INFO);
            return;
        }
    }

    let parts: Vec<&str> = args.split_whitespace().collect();
    if parts.is_empty() {
        state.send_console(conn_id, "Uso: /ITEM objid [cantidad]", font_index::INFO);
        return;
    }

    let obj_idx: i32 = parts[0].parse().unwrap_or(0);
    let amount: i32 = if parts.len() > 1 { parts[1].parse().unwrap_or(1) } else { 1 };

    if obj_idx < 1 || amount < 1 {
        state.send_console(conn_id, "Parametros invalidos.", font_index::INFO);
        return;
    }

    // Verify object exists
    let obj_name = match state.get_object(obj_idx) {
        Some(obj) => obj.name.clone(),
        None => {
            state.send_console(conn_id, &format!("Objeto {} no existe.", obj_idx), font_index::INFO);
            return;
        }
    };

    // Find first empty inventory slot
    let slot = match state.users.get(&conn_id) {
        Some(u) => u.inventory.iter().position(|s| s.obj_index == 0),
        None => return,
    };

    match slot {
        Some(slot_idx) => {
            if let Some(user) = state.users.get_mut(&conn_id) {
                user.inventory[slot_idx].obj_index = obj_idx;
        user.inventory[slot_idx].amount = amount;
        user.inventory[slot_idx].equipped = false;
            }
            // Send CSI to update client inventory
            send_inventory_slot(state, conn_id, slot_idx).await;
            state.send_console(conn_id, &format!("Creado: {} x{}.", obj_name, amount), font_index::INFO);
        }
        None => {
            state.send_console(conn_id, "Inventario lleno.", font_index::INFO);
        }
    }
}

/// Strip accents/diacritics for accent-insensitive search.
/// "túnica" → "tunica", "ción" → "cion", etc.
fn strip_accents(s: &str) -> String {
    s.chars().map(|c| match c {
        'á' | 'à' | 'ä' | 'â' => 'a',
        'é' | 'è' | 'ë' | 'ê' => 'e',
        'í' | 'ì' | 'ï' | 'î' => 'i',
        'ó' | 'ò' | 'ö' | 'ô' => 'o',
        'ú' | 'ù' | 'ü' | 'û' => 'u',
        'Á' | 'À' | 'Ä' | 'Â' => 'A',
        'É' | 'È' | 'Ë' | 'Ê' => 'E',
        'Í' | 'Ì' | 'Ï' | 'Î' => 'I',
        'Ó' | 'Ò' | 'Ö' | 'Ô' => 'O',
        'Ú' | 'Ù' | 'Ü' | 'Û' => 'U',
        'ñ' => 'n', 'Ñ' => 'N',
        _ => c,
    }).collect()
}

/// /SOBJ <name> — Search objects by name (VB6 TCP.bas line 3677).
/// Sends ||748@<name>@<index> for each match. Requires SEMIDIOS+.
/// Accent-insensitive: "tunica" matches "túnica".
pub(super) async fn handle_slash_sobj(state: &mut GameState, conn_id: ConnectionId, search: &str) {
    match state.users.get(&conn_id) {
        Some(u) if u.logged && u.privileges >= privilege_level::SEMIDIOS => {}
        _ => {
            state.send_console(conn_id, "No tenes permisos para usar este comando.", font_index::INFO);
            return;
        }
    }

    if search.is_empty() {
        state.send_console(conn_id, "Uso: /SOBJ <nombre>", font_index::INFO);
        return;
    }

    let search_norm = strip_accents(&search.to_uppercase());
    let mut found = 0;

    for i in 0..state.game_data.objects.len() {
        let name_norm = strip_accents(&state.game_data.objects[i].name.to_uppercase());
        if name_norm.contains(&search_norm) {
            let name = state.game_data.objects[i].name.clone();
            let idx = state.game_data.objects[i].index;
            state.send_msg_id(conn_id, 748, &format!("{}@{}", name, idx));
            found += 1;
        }
    }

    if found == 0 {
        state.send_console(conn_id, &format!("No se encontraron objetos con '{}'.", search), font_index::INFO);
    }
}

// =============================================================================
// GM Commands migrated from VB6 (TCP.bas)

/// /DAMETODO nick — Drop all user's inventory items on ground.
pub(super) async fn handle_slash_dametodo(state: &mut GameState, conn_id: ConnectionId, target: &str) {
    match state.users.get(&conn_id) {
        Some(u) if u.logged && u.privileges >= privilege_level::GRAN_DIOS => {}
        _ => return,
    }

    let target_upper = target.to_uppercase();
    let target_conn = state.users.values()
        .find(|u| u.logged && u.char_name.to_uppercase() == target_upper)
        .map(|u| u.conn_id);

    match target_conn {
        Some(tc) => {
            // Clear inventory
            if let Some(user) = state.users.get_mut(&tc) {
                for i in 0..user.inventory.len() {
                    user.inventory[i].obj_index = 0;
        user.inventory[i].amount = 0;
        user.inventory[i].equipped = false;
                }
                user.equip.weapon = 0;
                user.equip.armor = 0;
                user.equip.shield = 0;
                user.equip.helmet = 0;
                user.equip.municion = 0;
            }

            // Send full inventory update
            send_full_inventory(state, tc).await;

            let admin_name = state.users.get(&conn_id).map(|u| u.char_name.clone()).unwrap_or_default();
            state.send_msg_id(tc, 754, &admin_name.to_string());
            state.send_msg_id_to(SendTarget::ToAdmins, 755, &format!("{}@{}", admin_name, target));
            info!("[GM] {} stripped inventory of {}", admin_name, target);
        }
        None => {
            state.send_msg_id(conn_id, 196, "");
        }
    }
}

/// /MATA npc_index — Kill target NPC by runtime index.
pub(super) async fn handle_slash_mata(state: &mut GameState, conn_id: ConnectionId, target: &str) {
    match state.users.get(&conn_id) {
        Some(u) if u.logged && u.privileges >= privilege_level::SEMIDIOS => {}
        _ => return,
    }

    if let Ok(npc_idx) = target.parse::<usize>() {
        if npc_idx > 0 && npc_idx < state.npcs.len() {
            let npc_data = state.npcs[npc_idx].as_ref().map(|n| (n.map, n.x, n.y, n.char_index));
            if let Some((map, x, y, ci)) = npc_data {
                // Remove from world tile
                if map > 0 {
                    let grid = state.world.grid_mut(map);
                    if let Some(tile) = grid.tile_mut(x, y) {
                        if tile.npc_index == npc_idx as i32 {
                            tile.npc_index = 0;
                        }
                    }
                }
                let bp = binary_packets::write_character_remove(ci.0 as i16);
                state.send_data_bytes(SendTarget::ToArea { map, x, y }, &bp);
                state.npcs[npc_idx] = None;
                state.active_npc_indices.remove(&npc_idx);
                state.send_console(conn_id, &format!("NPC #{} eliminado.", npc_idx), font_index::INFO);
                return;
            }
        }
    }

    state.send_console(conn_id, "NPC no encontrado. Usa /MATA <npc_runtime_index>", font_index::INFO);
}

/// /MASSKILL — Kill all NPCs on current map.
pub(super) async fn handle_slash_masskill(state: &mut GameState, conn_id: ConnectionId) {
    let map = match state.users.get(&conn_id) {
        Some(u) if u.logged && u.privileges >= privilege_level::DIOS => u.pos_map,
        _ => return,
    };

    let mut killed = 0;
    let npc_indices: Vec<usize> = state.active_npc_indices.iter()
        .copied()
        .filter(|&i| state.npcs.get(i).and_then(|s| s.as_ref()).map(|n| n.map == map).unwrap_or(false))
        .collect();

    for idx in npc_indices {
        let npc_data = state.npcs[idx].as_ref().map(|n| (n.x, n.y, n.char_index));
        if let Some((nx, ny, ci)) = npc_data {
            let grid = state.world.grid_mut(map);
            if let Some(tile) = grid.tile_mut(nx, ny) {
                if tile.npc_index == idx as i32 {
                    tile.npc_index = 0;
                }
            }
            let bp = binary_packets::write_character_remove(ci.0 as i16);
            state.send_data_bytes(SendTarget::ToArea { map, x: nx, y: ny }, &bp);
            killed += 1;
        }
        state.npcs[idx] = None;
        state.active_npc_indices.remove(&idx);
    }

    state.send_console(conn_id, &format!("{} NPCs eliminados en mapa {}.", killed, map), font_index::INFO);
}

/// Check if an object is a map fixture (not player-droppable).
/// VB6: ItemNoEsDeMapa — returns TRUE if item is NOT a map fixture (i.e. can be cleaned).
/// Map fixtures: Doors(6), Trees(4), Signs(8), Forums(10), Minerals(23), Teleports(19).
/// Check if an object is a map fixture that should NOT be cleaned by /MASSDEST.
fn is_map_fixture(state: &GameState, obj_index: i32) -> bool {
    use crate::data::objects::ObjType;
    match state.get_object(obj_index) {
        Some(obj) => matches!(obj.obj_type,
            ObjType::Door | ObjType::Trees | ObjType::Sign |
            ObjType::Forum | ObjType::Mineral | ObjType::Teleport
        ),
        None => false,
    }
}

/// /LIMPIAR or /LMAP — Clean items tracked in the TrashCollector (clean_world).
/// VB6: LimpiarMundo (General.bas) — iterates TrashCollector, calls EraseObj
/// on each entry, then clears the list. Does NOT scan the map for all items.
/// Only items explicitly added via clean_world_add_item (NPC drops, player drops,
/// campfires, etc.) are cleaned. Map fixtures (trees, flowers, signs, doors,
/// minerals, etc.) placed by the map editor are never touched.
pub(super) async fn handle_slash_limpiar(state: &mut GameState, conn_id: ConnectionId) {
    match state.users.get(&conn_id) {
        Some(u) if u.logged && u.privileges >= privilege_level::DIOS => {}
        _ => return,
    }

    // Collect active TrashCollector entries (VB6: iterate TrashCollector in reverse)
    let mut cleaned = 0i32;
    for i in 0..state.clean_world.len() {
        let entry = &state.clean_world[i];
        if entry.map == 0 && entry.x == 0 && entry.y == 0 {
            continue; // empty slot
        }

        let map = entry.map;
        let x = entry.x;
        let y = entry.y;

        // VB6: EraseObj(1, d.Map, d.X, d.Y) — remove 1 unit from ground
        let grid = state.world.grid_mut(map);
        if let Some(tile) = grid.tile_mut(x, y) {
            if tile.ground_item.obj_index > 0 {
                tile.ground_item.amount -= 1;
                if tile.ground_item.amount <= 0 {
                    tile.ground_item = world::GroundItem::default();
                }
                // Send BO (object delete) to everyone on that map
                let bo_pkt = binary_packets::write_object_delete(x as i16, y as i16);
                state.send_data_bytes(SendTarget::ToMap(map), &bo_pkt);
                cleaned += 1;
            }
        }

        // Clear the entry
        state.clean_world[i] = crate::game::types::CleanWorldEntry::default();
    }

    // VB6 also calls SecurityIp.IpSecurityMantenimientoLista — no equivalent needed

    let gm_name = state.users.get(&conn_id).map(|u| u.char_name.clone()).unwrap_or_default();
    info!("[GM] {} used /LIMPIAR — cleaned {} tracked items", gm_name, cleaned);
    state.send_console(conn_id, &format!("{} items limpiados del TrashCollector.", cleaned), font_index::INFO);
}

/// /ACC <npc_id> or /RACC <npc_id> — Spawn NPC at GM's position. Requires GranDios+.
pub(super) async fn handle_slash_acc(state: &mut GameState, conn_id: ConnectionId, npc_id_str: &str, with_respawn: bool) {
    let (map, x, y, name) = match state.users.get(&conn_id) {
        Some(u) if u.logged && u.privileges >= privilege_level::GRAN_DIOS => {
            (u.pos_map, u.pos_x, u.pos_y, u.char_name.clone())
        }
        _ => return,
    };

    let npc_num: usize = match npc_id_str.parse() {
        Ok(n) if n > 0 => n,
        _ => return,
    };

    // Look up NPC name for logging
    let npc_name = state.game_data.npcs.get(npc_num)
        .map(|n| n.name.clone())
        .unwrap_or_else(|| format!("NPC#{}", npc_num));

    // VB6: SpawnNpc uses ClosestLegalPos — find nearest free tile
    // The GM's tile is occupied by the GM, so search nearby tiles
    let (spawn_x, spawn_y) = find_closest_legal_pos(state, map, x, y);
    if spawn_x == 0 && spawn_y == 0 {
        state.send_console(conn_id, "No hay posicion valida para spawnear.", font_index::INFO);
        return;
    }

    // Spawn NPC at closest legal position
    if let Some(npc_idx) = state.spawn_npc(npc_num, map, spawn_x, spawn_y) {
        // Broadcast CC for the NPC to nearby players
        let cc_pkt = state.npcs.get(npc_idx)
            .and_then(|n| n.as_ref())
            .map(|n| n.build_cc_binary());
        if let Some(cc) = cc_pkt {
            state.send_data_bytes(SendTarget::ToArea { map, x: spawn_x, y: spawn_y }, &cc);
        }
        let prefix = if with_respawn { "con respawn" } else { "sin respawn" };
        info!("[GM] {} spawned {} ({}) at map {} ({},{}) {}", name, npc_name, npc_num, map, spawn_x, spawn_y, prefix);
    }
}

/// /HECHIZO <name> <spell_id> — Teach a spell to a player. Requires Administrador.
pub(super) async fn handle_slash_hechizo(state: &mut GameState, conn_id: ConnectionId, args: &str) {
    match state.users.get(&conn_id) {
        Some(u) if u.logged && u.privileges >= privilege_level::ADMINISTRADOR => {}
        _ => return,
    }

    let args_upper = args.to_uppercase();
    let parts: Vec<&str> = args_upper.splitn(2, ' ').collect();
    if parts.len() < 2 { return; }

    let target_name = parts[0].replace('+', " ");
    let spell_id: i32 = match parts[1].parse() {
        Ok(s) if s > 0 => s,
        _ => return,
    };

    let target_upper = target_name.to_uppercase();
    let target_conn = match state.online_names.get(&target_upper).copied() {
        Some(c) => c,
        None => return,
    };

    // Check if they already have this spell
    let already_has = state.users.get(&target_conn)
        .map(|u| u.spells.iter().any(|&s| s == spell_id))
        .unwrap_or(false);
    if already_has { return; }

    // Find empty spell slot
    let empty_slot = state.users.get(&target_conn)
        .and_then(|u| u.spells.iter().position(|&s| s == 0));

    if let Some(slot) = empty_slot {
        if let Some(user) = state.users.get_mut(&target_conn) {
            user.spells[slot] = spell_id;
        }
        // Send spell slot update
        let spell_name = state.get_spell(spell_id)
            .map(|s| s.nombre.clone())
            .unwrap_or_else(|| format!("Hechizo {}", spell_id));
        let pkt = binary_packets::write_change_spell_slot((slot + 1) as u8, spell_id as i16, &spell_name);
        state.send_bytes(target_conn, &pkt);
    }
}

// =====================================================================

/// /BLOQ — Toggle tile blocked state at GM position. VB6 TCP.bas:5134
/// Requires Semidios+. Broadcasts BQ packet to map.
pub(super) async fn handle_slash_bloq(state: &mut GameState, conn_id: ConnectionId) {
    let (map, x, y) = match state.users.get(&conn_id) {
        Some(u) if u.logged && u.privileges >= privilege_level::SEMIDIOS => (u.pos_map, u.pos_x, u.pos_y),
        _ => return,
    };

    let map_idx = map as usize;
    // Toggle blocked state and capture result
    let toggle_result = if let Some(Some(game_map)) = state.game_data.maps.get_mut(map_idx) {
        if let Some(tile) = game_map.tiles.get_mut((x - 1) as usize, (y - 1) as usize) {
            tile.blocked = !tile.blocked;
            Some(tile.blocked)
        } else {
            None
        }
    } else {
        None
    };

    if let Some(is_blocked) = toggle_result {
        let blocked_val = if is_blocked { 1 } else { 0 };

        // Broadcast BQ packet to everyone on the map
        let bq_pkt = binary_packets::write_block_position(x as i16, y as i16, is_blocked);
        state.send_data_bytes(SendTarget::ToMap(map), &bq_pkt);

        let status = if is_blocked { "bloqueado" } else { "desbloqueado" };
        state.send_console(conn_id, &format!("Tile ({},{}) {}.", x, y, status), font_index::INFO);
    }
}

/// /DAMEBANCO <name> — Transfer all items from player's bank to GM's inventory. VB6 TCP.bas:3880
/// Requires GranDios+.
pub(super) async fn handle_slash_damebanco(state: &mut GameState, conn_id: ConnectionId, target: &str) {
    match state.users.get(&conn_id) {
        Some(u) if u.logged && u.privileges >= privilege_level::GRAN_DIOS => {}
        _ => return,
    }

    let target_id = match state.find_user_by_name(target) {
        Some(id) => id,
        None => {
            state.send_console(conn_id, &format!("Jugador '{}' no encontrado.", target), font_index::INFO);
            return;
        }
    };

    // Collect bank items from target
    let bank_items: Vec<InventorySlot> = match state.users.get(&target_id) {
        Some(u) => u.bank.iter().filter(|s| s.obj_index > 0).cloned().collect(),
        None => return,
    };

    if bank_items.is_empty() {
        state.send_console(conn_id, "El banco del jugador esta vacio.", font_index::INFO);
        return;
    }

    // Clear target's bank
    if let Some(u) = state.users.get_mut(&target_id) {
        for i in 0..u.bank.len() {
            u.bank[i].obj_index = 0;
        u.bank[i].amount = 0;
        }
    }

    // Add to GM's inventory
    let mut added = 0;
    for item in &bank_items {
        let empty_slot = state.users.get(&conn_id)
            .and_then(|u| u.inventory.iter().position(|s| s.obj_index == 0));
        if let Some(slot_idx) = empty_slot {
            if let Some(user) = state.users.get_mut(&conn_id) {
                user.inventory[slot_idx].obj_index = item.obj_index;
                user.inventory[slot_idx].amount = item.amount;
                user.inventory[slot_idx].equipped = item.equipped;
                added += 1;
            }
        }
    }

    send_full_inventory(state, conn_id).await;
    state.send_console(conn_id, &format!("Transferidos {} items del banco de '{}'.", added, target), font_index::INFO);
}

/// /PREMIAR <name> <item_id> — Give prize item to player. VB6 TCP.bas:4237
/// Requires GranDios+.
pub(super) async fn handle_slash_premiar(state: &mut GameState, conn_id: ConnectionId, args: &str) {
    match state.users.get(&conn_id) {
        Some(u) if u.logged && u.privileges >= privilege_level::GRAN_DIOS => {}
        _ => return,
    }

    let parts: Vec<&str> = args.split_whitespace().collect();
    if parts.len() < 2 {
        state.send_console(conn_id, "Uso: /PREMIAR nombre item_id", font_index::INFO);
        return;
    }

    let target_name = parts[0];
    let item_id: i32 = parts[1].parse().unwrap_or(0);
    if item_id < 1 {
        state.send_console(conn_id, "Item invalido.", font_index::INFO);
        return;
    }

    give_item_to_player(state, conn_id, target_name, item_id, 1).await;
}

/// /PREMIARTS <name> <item_id> — Give TS-specific prize. VB6 TCP.bas:4262
/// Requires Developer+.
pub(super) async fn handle_slash_premiarts(state: &mut GameState, conn_id: ConnectionId, args: &str) {
    match state.users.get(&conn_id) {
        Some(u) if u.logged && u.privileges >= privilege_level::DEVELOPER => {}
        _ => return,
    }

    let parts: Vec<&str> = args.split_whitespace().collect();
    if parts.len() < 2 {
        state.send_console(conn_id, "Uso: /PREMIARTS nombre item_id", font_index::INFO);
        return;
    }

    let target_name = parts[0];
    let item_id: i32 = parts[1].parse().unwrap_or(0);
    if item_id < 1 { return; }

    give_item_to_player(state, conn_id, target_name, item_id, 1).await;
}

/// Helper: Give an item to a player by name, notify GM.
async fn give_item_to_player(state: &mut GameState, gm_id: ConnectionId, target_name: &str, item_id: i32, amount: i32) {
    let target_id = match state.find_user_by_name(target_name) {
        Some(id) => id,
        None => {
            state.send_console(gm_id, &format!("Jugador '{}' no encontrado.", target_name), font_index::INFO);
            return;
        }
    };

    // Verify object exists
    if state.get_object(item_id).is_none() {
        state.send_console(gm_id, &format!("Objeto {} no existe.", item_id), font_index::INFO);
        return;
    }

    let empty_slot = state.users.get(&target_id)
        .and_then(|u| u.inventory.iter().position(|s| s.obj_index == 0));

    if let Some(slot_idx) = empty_slot {
        if let Some(user) = state.users.get_mut(&target_id) {
            user.inventory[slot_idx].obj_index = item_id;
        user.inventory[slot_idx].amount = amount;
        user.inventory[slot_idx].equipped = false;
        }
        send_inventory_slot(state, target_id, slot_idx + 1).await;
        state.send_console(gm_id, &format!("Item {} dado a '{}'.", item_id, target_name), font_index::INFO);
        state.send_console(target_id, "Has recibido un premio!", font_index::INFO);
    } else {
        state.send_console(gm_id, "El jugador no tiene espacio en el inventario.", font_index::INFO);
    }
}

/// /NPCAURA <npc_runtime_index> <aura_id> — Set aura on a live NPC instance.
/// Requires DIOS+ privileges. Useful for testing NPC aura visuals.
pub(super) async fn handle_slash_npcaura(state: &mut GameState, conn_id: ConnectionId, args: &str) {
    let is_gm = state.users.get(&conn_id).map(|u| u.logged && u.privileges >= privilege_level::DIOS).unwrap_or(false);
    if !is_gm { return; }
    let parts: Vec<&str> = args.split_whitespace().collect();
    if parts.len() < 2 {
        state.send_console(conn_id, "Uso: /NPCAURA <npc_index> <aura_id>", font_index::INFO);
        return;
    }

    let npc_idx: usize = match parts[0].parse() {
        Ok(v) => v,
        Err(_) => {
            state.send_console(conn_id, "NPC index inválido.", font_index::INFO);
            return;
        }
    };
    let aura_id: i32 = match parts[1].parse() {
        Ok(v) => v,
        Err(_) => {
            state.send_console(conn_id, "Aura ID inválido.", font_index::INFO);
            return;
        }
    };

    let (npc_name, map) = match state.get_npc(npc_idx) {
        Some(npc) if npc.active => (npc.name.clone(), npc.map),
        _ => {
            state.send_console(conn_id, &format!("NPC {} no encontrado o inactivo.", npc_idx), font_index::INFO);
            return;
        }
    };

    if let Some(npc) = state.get_npc_mut(npc_idx) {
        npc.aura = aura_id;
        let cc = npc.build_cc_binary();
        state.send_data_bytes(SendTarget::ToMap(map), &cc);
    }

    let gm_name = state.users.get(&conn_id).map(|u| u.char_name.clone()).unwrap_or_default();
    info!("[GM] {} set NPC {} ({}) aura to {}", gm_name, npc_idx, npc_name, aura_id);
    state.send_console(conn_id, &format!("NPC {} ({}) aura = {}", npc_idx, npc_name, aura_id), font_index::INFO);
}

/// /DEST — Destroy floor object at GM's current tile.
/// VB6: TCP.bas line 5125. Calls EraseObj(SendTarget.toMap, ..., 10000, ...)
pub(super) async fn handle_slash_dest(state: &mut GameState, conn_id: ConnectionId) {
    let (map, x, y) = match state.users.get(&conn_id) {
        Some(u) if u.logged && u.privileges >= privilege_level::DIOS => (u.pos_map, u.pos_x, u.pos_y),
        _ => return,
    };

    let obj_idx = state.world.grid(map)
        .and_then(|g| g.tile(x, y))
        .map(|t| t.ground_item.obj_index)
        .unwrap_or(0);

    if obj_idx <= 0 {
        state.send_console(conn_id, "No hay objeto en esta posición.", font_index::INFO);
        return;
    }

    let obj_name = state.get_object(obj_idx).map(|o| o.name.clone()).unwrap_or_default();

    let grid = state.world.grid_mut(map);
    if let Some(tile) = grid.tile_mut(x, y) {
        tile.ground_item = world::GroundItem::default();
    }

    // Send BO packet to notify clients
    let bo_pkt = binary_packets::write_object_delete(x as i16, y as i16);
    state.send_data_bytes(SendTarget::ToMap(map), &bo_pkt);

    let gm_name = state.users.get(&conn_id).map(|u| u.char_name.clone()).unwrap_or_default();
    info!("[GM] {} used /DEST at ({},{},{}) — destroyed {}", gm_name, map, x, y, obj_name);
    state.send_console(conn_id, &format!("Destruido: {} en ({},{}).", obj_name, x, y), font_index::INFO);
}

/// /MASSDEST — Destroy all non-map floor objects in GM's visible area.
/// VB6: TCP.bas line 4914. Iterates MinXBorder/MinYBorder area, skips map fixtures.
pub(super) async fn handle_slash_massdest(state: &mut GameState, conn_id: ConnectionId) {
    let (map, cx, cy) = match state.users.get(&conn_id) {
        Some(u) if u.logged && u.privileges >= privilege_level::DIOS => (u.pos_map, u.pos_x, u.pos_y),
        _ => return,
    };

    let (grid_w, grid_h) = state.world.grid(map).map(|g| (g.width, g.height)).unwrap_or((100, 100));
    let min_y = (cy - world::MIN_Y_BORDER + 1).max(1);
    let max_y = (cy + world::MIN_Y_BORDER - 1).min(grid_h);
    let min_x = (cx - world::MIN_X_BORDER + 1).max(1);
    let max_x = (cx + world::MIN_X_BORDER - 1).min(grid_w);

    // Collect positions to clean (skip map fixtures)
    let mut to_clean: Vec<(i32, i32)> = Vec::new();
    if let Some(grid) = state.world.grid(map) {
        for y in min_y..=max_y {
            for x in min_x..=max_x {
                if let Some(tile) = grid.tile(x, y) {
                    if tile.ground_item.obj_index > 0 && !is_map_fixture(state, tile.ground_item.obj_index) {
                        to_clean.push((x, y));
                    }
                }
            }
        }
    }

    for &(x, y) in &to_clean {
        let grid = state.world.grid_mut(map);
        if let Some(tile) = grid.tile_mut(x, y) {
            tile.ground_item = world::GroundItem::default();
        }
        let bo_pkt = binary_packets::write_object_delete(x as i16, y as i16);
        state.send_data_bytes(SendTarget::ToMap(map), &bo_pkt);
    }

    let gm_name = state.users.get(&conn_id).map(|u| u.char_name.clone()).unwrap_or_default();
    info!("[GM] {} used /MASSDEST at ({},{},{}) — cleaned {} items", gm_name, map, cx, cy, to_clean.len());
    state.send_console(conn_id, &format!("{} objetos destruidos en el área.", to_clean.len()), font_index::INFO);
}

/// /HACERITEM <objID>@<amount> — Create item on floor at GM position.
/// VB6: TCP.bas line 5072. Requires GRAN_DIOS+. Fails if tile already has object.
pub(super) async fn handle_slash_haceritem(state: &mut GameState, conn_id: ConnectionId, args: &str) {
    let (map, x, y) = match state.users.get(&conn_id) {
        Some(u) if u.logged && u.privileges >= privilege_level::GRAN_DIOS => (u.pos_map, u.pos_x, u.pos_y),
        _ => return,
    };

    // Parse objID@amount (VB6 uses '@' delimiter)
    let parts: Vec<&str> = args.split('@').collect();
    let obj_id: i32 = parts.first().and_then(|s| s.trim().parse().ok()).unwrap_or(0);
    let amount: i32 = if parts.len() >= 2 {
        parts[1].trim().parse().unwrap_or(1).max(1).min(10000)
    } else {
        1
    };

    if obj_id < 1 {
        state.send_console(conn_id, "Uso: /HACERITEM <objID>@<cantidad>", font_index::INFO);
        return;
    }

    // Verify object exists
    let (obj_name, grh) = match state.get_object(obj_id) {
        Some(obj) => (obj.name.clone(), obj.grh_index),
        None => {
            state.send_console(conn_id, &format!("Objeto {} no existe.", obj_id), font_index::INFO);
            return;
        }
    };

    // Check tile is empty
    let tile_occupied = state.world.grid(map)
        .and_then(|g| g.tile(x, y))
        .map(|t| t.ground_item.obj_index > 0)
        .unwrap_or(true);

    if tile_occupied {
        state.send_console(conn_id, "Ya hay un objeto en esta posición.", font_index::INFO);
        return;
    }

    // Place item
    let grid = state.world.grid_mut(map);
    if let Some(tile) = grid.tile_mut(x, y) {
        tile.ground_item.obj_index = obj_id;
        tile.ground_item.amount = amount;
    }

    // Send HO packet to show item visually
    if grh > 0 {
        let ho_pkt = binary_packets::write_object_create(x as i16, y as i16, grh as i16);
        state.send_data_bytes(SendTarget::ToArea { map, x, y }, &ho_pkt);
    }

    let gm_name = state.users.get(&conn_id).map(|u| u.char_name.clone()).unwrap_or_default();
    info!("[GM] {} used /HACERITEM {} x{} ({}) at ({},{},{})", gm_name, obj_id, amount, obj_name, map, x, y);
    // Notify GM
    state.send_console(conn_id, &format!("Creado: {} x{} ({}) en ({},{}).", obj_name, amount, obj_id, x, y), font_index::INFO);
    // Notify admins (VB6: ||802)
    state.send_msg_id_to(SendTarget::ToAdmins, 802, &format!("{}@{}@{}@{}", gm_name, obj_id, obj_name, amount));
}

/// /MASSDEST — already handled above.

/// /NENE <map> — List hostile NPCs alive on a given map.
/// VB6: TCP.bas line 3287. Lists hostile NPCs with Alineacion=2.
pub(super) async fn handle_slash_nene(state: &mut GameState, conn_id: ConnectionId, args: &str) {
    let is_gm = state.users.get(&conn_id).map(|u| u.logged && u.privileges >= privilege_level::CONSEJERO).unwrap_or(false);
    if !is_gm { return; }

    let target_map: i32 = args.trim().parse().unwrap_or(0);
    if target_map < 1 {
        state.send_console(conn_id, "Uso: /NENE <mapa>", font_index::INFO);
        return;
    }

    let mut names: Vec<String> = Vec::new();
    for &idx in &state.active_npc_indices {
        if let Some(npc_opt) = state.npcs.get(idx).and_then(|s| s.as_ref()) {
            if npc_opt.is_alive() && npc_opt.map == target_map
                && npc_opt.hostile && npc_opt.alineacion == 2
            {
                names.push(npc_opt.name.clone());
            }
        }
    }

    let list = if names.is_empty() {
        "No hay NPCs hostiles".to_string()
    } else {
        names.join(", ")
    };

    state.send_console(conn_id, &format!("NPCs hostiles en mapa {}: {}", target_map, list), font_index::INFO);
}

/// /RESETINV — Reset targeted NPC's inventory to its NpcData defaults.
/// VB6: TCP.bas line 4610. Requires targeting an NPC first.
pub(super) async fn handle_slash_resetinv(state: &mut GameState, conn_id: ConnectionId) {
    let (is_gm, target_npc_idx) = match state.users.get(&conn_id) {
        Some(u) if u.logged && u.privileges >= privilege_level::DIOS => (true, u.target_npc),
        _ => (false, 0),
    };
    if !is_gm { return; }

    if target_npc_idx == 0 {
        state.send_console(conn_id, "No tenés un NPC seleccionado.", font_index::INFO);
        return;
    }

    // Get NPC number to look up original data
    let npc_number = match state.get_npc(target_npc_idx) {
        Some(npc) if npc.active => npc.npc_number,
        _ => {
            state.send_console(conn_id, "NPC no encontrado o inactivo.", font_index::INFO);
            return;
        }
    };

    // Get original inventory from NpcData
    let original_items = match state.game_data.npcs.get(npc_number) {
        Some(data) => data.items.clone(),
        None => {
            state.send_console(conn_id, &format!("NPC data {} no encontrado.", npc_number), font_index::INFO);
            return;
        }
    };

    // Reset inventory
    if let Some(npc) = state.get_npc_mut(target_npc_idx) {
        for slot in npc.inventory.iter_mut() {
            *slot = super::super::npc::NpcInvSlot::default();
        }
        for (i, item) in original_items.iter().enumerate() {
            if i < npc.inventory.len() {
                npc.inventory[i].obj_index = item.obj_index;
                npc.inventory[i].amount = item.amount;
                npc.inventory[i].prob_tirar = item.prob_tirar;
            }
        }
        npc.nro_items = original_items.len() as i32;
    }

    let npc_name = state.get_npc(target_npc_idx).map(|n| n.name.clone()).unwrap_or_default();
    let gm_name = state.users.get(&conn_id).map(|u| u.char_name.clone()).unwrap_or_default();
    info!("[GM] {} used /RESETINV on NPC {} ({})", gm_name, target_npc_idx, npc_name);
    state.send_console(conn_id, &format!("Inventario de {} reseteado.", npc_name), font_index::INFO);
}

// =============================================================================
// NEW GM Commands (HIGH priority batch)

/// /SLOT <name> <slot> — Show contents of a user's inventory slot. Requires DIOS+.
pub(super) async fn handle_slash_slot(state: &mut GameState, conn_id: ConnectionId, args: &str) {
    match state.users.get(&conn_id) {
        Some(u) if u.logged && u.privileges >= privilege_level::DIOS => {}
        _ => {
            state.send_console(conn_id, "No tenes permisos para usar este comando.", font_index::INFO);
            return;
        }
    }

    let parts: Vec<&str> = args.split_whitespace().collect();
    if parts.len() < 2 {
        state.send_console(conn_id, "Uso: /SLOT nombre slot_num", font_index::INFO);
        return;
    }

    let name = parts[0];
    let slot_num: usize = parts[1].parse().unwrap_or(0);

    if slot_num < 1 || slot_num > 36 {
        state.send_console(conn_id, "Slot debe ser entre 1 y 36.", font_index::INFO);
        return;
    }

    let target_id = match state.find_user_by_name(name) {
        Some(id) => id,
        None => {
            state.send_console(conn_id, &format!("Jugador '{}' no encontrado.", name), font_index::INFO);
            return;
        }
    };

    let slot_idx = slot_num - 1; // 0-based
    let slot_info = state.users.get(&target_id).map(|u| {
        if slot_idx < u.inventory.len() {
            let s = &u.inventory[slot_idx];
            (s.obj_index, s.amount, s.equipped)
        } else {
            (0, 0, false)
        }
    }).unwrap_or((0, 0, false));

    let (obj_idx, amount, equipped) = slot_info;
    if obj_idx == 0 {
        state.send_console(conn_id, &format!("{} - Slot {}: (vacio)", name, slot_num), font_index::INFO);
    } else {
        let obj_name = state.get_object(obj_idx).map(|o| o.name.clone()).unwrap_or_else(|| format!("OBJ#{}", obj_idx));
        let eq_str = if equipped { " [equipado]" } else { "" };
        state.send_console(conn_id, &format!("{} - Slot {}: {} x{}{}", name, slot_num, obj_name, amount, eq_str), font_index::INFO);
    }
}

/// /PISO — List all items on the floor near the GM (±8x, ±6y). Requires DIOS+.
pub(super) async fn handle_slash_piso(state: &mut GameState, conn_id: ConnectionId) {
    let (map, cx, cy) = match state.users.get(&conn_id) {
        Some(u) if u.logged && u.privileges >= privilege_level::DIOS => {
            (u.pos_map, u.pos_x, u.pos_y)
        }
        _ => {
            state.send_console(conn_id, "No tenes permisos para usar este comando.", font_index::INFO);
            return;
        }
    };

    let mut items_found: Vec<String> = Vec::new();
    let grid = match state.world.grid(map) {
        Some(g) => g,
        None => {
            state.send_console(conn_id, "Mapa no cargado.", font_index::INFO);
            return;
        }
    };

    for dy in -6i32..=6 {
        for dx in -8i32..=8 {
            let tx = cx + dx;
            let ty = cy + dy;
            if let Some(tile) = grid.tile(tx, ty) {
                if tile.ground_item.obj_index > 0 {
                    let obj_idx = tile.ground_item.obj_index;
                    let amount = tile.ground_item.amount;
                    let obj_name = state.get_object(obj_idx)
                        .map(|o| o.name.clone())
                        .unwrap_or_else(|| format!("OBJ#{}", obj_idx));
                    items_found.push(format!("({},{}) {} x{}", tx, ty, obj_name, amount));
                }
            }
        }
    }

    if items_found.is_empty() {
        state.send_console(conn_id, "No hay items en el piso cercano.", font_index::INFO);
    } else {
        state.send_console(conn_id, &format!("Items en el piso ({}):", items_found.len()), font_index::INFO);
        for item_str in &items_found {
            state.send_console(conn_id, item_str, font_index::INFO);
        }
    }
}

