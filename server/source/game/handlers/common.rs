//! Shared helper functions used across all handler sub-modules.
//!
//! These are utility functions for: stats packets, tile access, random numbers,
//! inventory management, cooldown checks, and misc helpers.

use crate::net::ConnectionId;
use crate::data::maps::Trigger;
use crate::protocol::{fields::read_field, binary_packets};
use crate::game::types::{GameState, UserState, CleanWorldEntry, MAX_INVENTORY_SLOTS};
use crate::game::world;

// =====================================================================
// VB6 "empty" animation constants (Declares.bas: NingunArma/NingunEscudo/NingunCasco = 2)
// =====================================================================
pub(super) const NINGUN_ARMA: i32 = 2;
pub(super) const NINGUN_ESCUDO: i32 = 2;
pub(super) const NINGUN_CASCO: i32 = 2;

// =====================================================================
// Clan helpers
// =====================================================================

/// VB6 SameClan: returns true if both users are logged, share the same guild (>0).
pub(super) fn same_clan(state: &GameState, a: ConnectionId, b: ConnectionId) -> bool {
    let ga = state.users.get(&a).map(|u| u.guild_index).unwrap_or(0);
    if ga == 0 { return false; }
    let gb = state.users.get(&b).map(|u| u.guild_index).unwrap_or(0);
    ga == gb
}

// =====================================================================
// Map tile helpers
// =====================================================================

pub(super) fn get_map_tile_trigger(state: &GameState, map: i32, x: i32, y: i32) -> Trigger {
    let map_idx = map as usize;
    if let Some(Some(game_map)) = state.game_data.maps.get(map_idx) {
        if x >= 1 && x <= 100 && y >= 1 && y <= 100 {
            return game_map.tiles[(y - 1) as usize][(x - 1) as usize].trigger;
        }
    }
    Trigger::None
}

pub(super) fn get_map_tile_obj(state: &GameState, map: i32, x: i32, y: i32) -> i32 {
    let map_idx = map as usize;
    if let Some(Some(game_map)) = state.game_data.maps.get(map_idx) {
        if x >= 1 && x <= 100 && y >= 1 && y <= 100 {
            return game_map.tiles[(y - 1) as usize][(x - 1) as usize].obj.obj_index as i32;
        }
    }
    0
}

pub(super) fn get_map_tile_particle(state: &GameState, map: i32, x: i32, y: i32) -> i16 {
    let map_idx = map as usize;
    if let Some(Some(game_map)) = state.game_data.maps.get(map_idx) {
        if x >= 1 && x <= 100 && y >= 1 && y <= 100 {
            return game_map.tiles[(y - 1) as usize][(x - 1) as usize].particle_group_index;
        }
    }
    0
}

pub(super) fn set_map_tile_obj(state: &mut GameState, map: i32, x: i32, y: i32, new_obj: i16) {
    let map_idx = map as usize;
    if let Some(Some(game_map)) = state.game_data.maps.get_mut(map_idx) {
        if x >= 1 && x <= 100 && y >= 1 && y <= 100 {
            game_map.tiles[(y - 1) as usize][(x - 1) as usize].obj.obj_index = new_obj;
        }
    }
}

pub(super) fn set_map_tile_blocked(state: &mut GameState, map: i32, x: i32, y: i32, blocked: bool) {
    let map_idx = map as usize;
    if let Some(Some(game_map)) = state.game_data.maps.get_mut(map_idx) {
        if x >= 1 && x <= 100 && y >= 1 && y <= 100 {
            game_map.tiles[(y - 1) as usize][(x - 1) as usize].blocked = blocked;
        }
    }
}

// =====================================================================
// Health description
// =====================================================================

pub(super) fn health_description(current: i32, max: i32, dead: bool) -> &'static str {
    if dead {
        return "Muerto";
    }
    if max <= 0 {
        return "Intacto";
    }
    let pct = (current as f64 / max as f64 * 100.0) as i32;
    match pct {
        p if p >= 100 => "Intacto",
        p if p >= 75 => "Algo lastimado",
        p if p >= 45 => "Medio herido",
        p if p >= 20 => "Gravemente herido",
        _ => "Agonizando",
    }
}

// =====================================================================
// Stats packets
// =====================================================================

pub(super) async fn send_stats_hp(state: &mut GameState, conn_id: ConnectionId) {
    if let Some(u) = state.users.get(&conn_id) {
        let pkt = binary_packets::write_update_hp(u.max_hp as i16, u.min_hp as i16);
        state.send_bytes(conn_id, &pkt).await;
    }
}

pub(super) async fn send_stats_mana(state: &mut GameState, conn_id: ConnectionId) {
    if let Some(u) = state.users.get(&conn_id) {
        let pkt = binary_packets::write_update_mana(u.max_mana as i16, u.min_mana as i16);
        state.send_bytes(conn_id, &pkt).await;
    }
}

pub(super) async fn send_stats_sta(state: &mut GameState, conn_id: ConnectionId) {
    if let Some(u) = state.users.get(&conn_id) {
        let pkt = binary_packets::write_update_sta(u.max_sta as i16, u.min_sta as i16);
        state.send_bytes(conn_id, &pkt).await;
    }
}

pub(super) async fn send_stats_gold(state: &mut GameState, conn_id: ConnectionId) {
    if let Some(u) = state.users.get(&conn_id) {
        let pkt = binary_packets::write_update_gold(u.gold as i32);
        state.send_bytes(conn_id, &pkt).await;
    }
}

pub(super) async fn send_stats_exp(state: &mut GameState, conn_id: ConnectionId) {
    if let Some(u) = state.users.get(&conn_id) {
        let pkt = binary_packets::write_update_exp(u.exp as i32);
        state.send_bytes(conn_id, &pkt).await;
    }
}

pub(super) async fn send_hunger_thirst(state: &mut GameState, conn_id: ConnectionId) {
    let (max_agua, min_agua, max_ham, min_ham) = match state.users.get(&conn_id) {
        Some(u) => (u.max_agua as u8, u.min_agua as u8, u.max_ham as u8, u.min_ham as u8),
        None => return,
    };
    let pkt = binary_packets::write_update_hunger_thirst(max_agua, min_agua, max_ham, min_ham);
    state.send_bytes(conn_id, &pkt).await;
}

// =====================================================================
// Area ID & character data packet
// =====================================================================

pub(super) fn area_id(x: i32, y: i32) -> i32 {
    (x / 9 + 1) * (y / 9 + 1)
}

/// Build binary CharData packet for a user.
pub(super) fn build_cd_binary(user: &UserState) -> Vec<u8> {
    crate::protocol::binary_packets::write_char_data(
        user.char_index.0 as i16,
        0, // color
        user.aura_a as i16,
        user.aura_w as i16,
        user.aura_e as i16,
        user.aura_r as i16,
        user.aura_c as i16,
        user.levitando,
        0, // ranking
    )
}

/// Build binary AuraUpdate packet for a user.
pub(super) fn build_aura_binary(user: &UserState) -> Vec<u8> {
    crate::protocol::binary_packets::write_aura_update(
        user.char_index.0 as i16,
        user.aura_a as i16,
        user.aura_w as i16,
        user.aura_e as i16,
        user.aura_r as i16,
        user.aura_c as i16,
    )
}

// =====================================================================
// Position helpers
// =====================================================================

pub(super) fn find_free_pos(state: &GameState, map: i32, x: i32, y: i32) -> (i32, i32) {
    let occupied = state.world.grid(map)
        .and_then(|g| g.tile(x, y))
        .map(|t| t.user_conn.is_some() || t.npc_index > 0)
        .unwrap_or(false);
    let blocked = state.is_tile_blocked(map, x, y);

    if !occupied && !blocked {
        return (x, y);
    }

    for radius in 1i32..=10 {
        for dy in -radius..=radius {
            for dx in -radius..=radius {
                if dx.abs() != radius && dy.abs() != radius { continue; }
                let nx = x + dx;
                let ny = y + dy;
                if nx < 1 || nx > 100 || ny < 1 || ny > 100 { continue; }
                let tile_blocked = state.is_tile_blocked(map, nx, ny);
                if tile_blocked { continue; }
                let tile_free = state.world.grid(map)
                    .and_then(|g| g.tile(nx, ny))
                    .map(|t| t.user_conn.is_none() && t.npc_index == 0)
                    .unwrap_or(false);
                if tile_free {
                    return (nx, ny);
                }
            }
        }
    }

    (x, y)
}

pub(super) fn find_free_tile(state: &GameState, map: i32, x: i32, y: i32) -> (i32, i32) {
    let grid = match state.world.grid(map) {
        Some(g) => g,
        None => return (0, 0),
    };

    if let Some(tile) = grid.tile(x, y) {
        if tile.ground_item.obj_index == 0 {
            return (0, 0);
        }
    }

    let offsets = [(0, 1), (1, 0), (0, -1), (-1, 0), (1, 1), (-1, -1), (1, -1), (-1, 1)];
    for (dx, dy) in &offsets {
        let tx = x + dx;
        let ty = y + dy;
        if let Some(tile) = grid.tile(tx, ty) {
            if tile.ground_item.obj_index == 0 {
                return (*dx, *dy);
            }
        }
    }

    (0, 0)
}

pub(super) fn find_closest_legal_pos(state: &GameState, map: i32, x: i32, y: i32) -> (i32, i32) {
    for ring in 0..=12 {
        for ty in (y - ring)..=(y + ring) {
            for tx in (x - ring)..=(x + ring) {
                if tx < 1 || tx > 100 || ty < 1 || ty > 100 { continue; }
                if !state.is_tile_blocked(map, tx, ty) {
                    return (tx, ty);
                }
            }
        }
    }
    (0, 0)
}

pub(super) fn zona_cura(state: &GameState, map: i32, px: i32, py: i32) -> bool {
    let min_x = (px - world::MIN_X_BORDER + 1).max(1);
    let max_x = (px + world::MIN_X_BORDER - 1).min(100);
    let min_y = (py - world::MIN_Y_BORDER + 1).max(1);
    let max_y = (py + world::MIN_Y_BORDER - 1).min(100);

    if let Some(grid) = state.world.grid(map) {
        for cy in min_y..=max_y {
            for cx in min_x..=max_x {
                if let Some(tile) = grid.tile(cx, cy) {
                    if tile.npc_index > 0 {
                        if let Some(npc) = state.get_npc(tile.npc_index as usize) {
                            if npc.npc_type == crate::data::npcs::NpcType::Reviver && npc.is_alive() {
                                let dist = (px - npc.x).abs() + (py - npc.y).abs();
                                if dist < 20 {
                                    return true;
                                }
                            }
                        }
                    }
                }
            }
        }
    }
    false
}

// =====================================================================
// String / opcode helpers
// =====================================================================

pub(super) fn strip_opcode(data: &str, opcode_len: usize) -> &str {
    if data.len() > opcode_len {
        &data[opcode_len..]
    } else {
        ""
    }
}

pub(super) fn is_valid_name(name: &str) -> bool {
    if name.is_empty() || name.len() > 30 {
        return false;
    }
    name.chars().all(|c| c.is_ascii_alphanumeric() || c == '_' || c == '-' || c == ' ')
}

pub(super) async fn is_char_banned(pool: &sqlx::PgPool, char_name: &str) -> bool {
    let result = sqlx::query_scalar::<_, bool>(
        "SELECT banned FROM characters WHERE UPPER(name) = UPPER($1)"
    )
    .bind(char_name)
    .fetch_optional(pool)
    .await;
    matches!(result, Ok(Some(true)))
}

pub(super) fn is_same_ip_online(state: &GameState, exclude_id: ConnectionId, ip: &str) -> bool {
    state.users.values().any(|u| {
        u.conn_id != exclude_id && u.logged && u.ip == ip
    })
}

pub(super) fn poner_puntos(num: i64) -> String {
    let s = num.to_string();
    let len = s.len();
    if len <= 3 { return s; }
    let mut result = String::new();
    for (i, ch) in s.chars().enumerate() {
        if i > 0 && (len - i) % 3 == 0 {
            result.push('.');
        }
        result.push(ch);
    }
    result
}

pub(super) fn chrono_like_date() -> String {
    use std::time::{SystemTime, UNIX_EPOCH};
    let secs = SystemTime::now()
        .duration_since(UNIX_EPOCH)
        .unwrap_or_default()
        .as_secs();
    let days = secs / 86400;
    let time_of_day = secs % 86400;
    let hours = time_of_day / 3600;
    let minutes = (time_of_day % 3600) / 60;
    let mut y = 1970i64;
    let mut remaining = days as i64;
    loop {
        let days_in_year = if y % 4 == 0 && (y % 100 != 0 || y % 400 == 0) { 366 } else { 365 };
        if remaining < days_in_year { break; }
        remaining -= days_in_year;
        y += 1;
    }
    let leap = y % 4 == 0 && (y % 100 != 0 || y % 400 == 0);
    let month_days = [31, if leap { 29 } else { 28 }, 31, 30, 31, 30, 31, 31, 30, 31, 30, 31];
    let mut m = 0usize;
    for i in 0..12 {
        if remaining < month_days[i] { m = i; break; }
        remaining -= month_days[i];
    }
    format!("{:02}/{:02}/{} {:02}:{:02}", remaining + 1, m + 1, y, hours, minutes)
}

// =====================================================================
// Random number generation
// =====================================================================

pub(super) fn rand_simple_u32() -> u32 {
    use std::time::{SystemTime, UNIX_EPOCH};
    let nanos = SystemTime::now()
        .duration_since(UNIX_EPOCH)
        .unwrap_or_default()
        .as_nanos() as u32;
    nanos.wrapping_mul(1103515245).wrapping_add(12345) % 1000
}

pub(super) fn rand_range(min: i32, max: i32) -> i32 {
    if min >= max {
        return min;
    }
    let range = (max - min + 1) as u32;
    min + (rand_simple_u32() % range) as i32
}

pub(super) fn random_number(min: i32, max: i32) -> i32 {
    use std::time::{SystemTime, UNIX_EPOCH};
    if min >= max { return min; }
    let seed = SystemTime::now()
        .duration_since(UNIX_EPOCH)
        .unwrap_or_default()
        .as_nanos() as u64;
    let val = seed.wrapping_mul(6364136223846793005).wrapping_add(1442695040888963407);
    let range = (max - min + 1) as u64;
    min + (val % range) as i32
}

// =====================================================================
// INI helpers
// =====================================================================

pub(super) fn ini_get(path: &std::path::Path, section: &str, key: &str) -> String {
    crate::config::get_var(path.to_str().unwrap_or(""), section, key)
}

pub(super) fn ini_write(path: &std::path::Path, section: &str, key: &str, value: &str) {
    let _ = crate::config::write_var(path.to_str().unwrap_or(""), section, key, value);
}

// =====================================================================
// Connection helpers
// =====================================================================

pub(super) async fn close_connection(state: &mut GameState, conn_id: ConnectionId) {
    if let Some(mut writer) = state.writers.remove(&conn_id) {
        tokio::time::sleep(tokio::time::Duration::from_millis(100)).await;
        writer.shutdown().await;
    }
}

pub(super) fn update_map_user_counts(state: &mut GameState) {
    state.map_user_counts.clear();
    for user in state.users.values() {
        if user.logged && !user.dead {
            *state.map_user_counts.entry(user.pos_map).or_insert(0) += 1;
        }
    }
}

// =====================================================================
// Cooldown checks (anti-cheat interval validation)
// =====================================================================

pub fn puede_pegar(state: &mut GameState, conn_id: ConnectionId) -> bool {
    let user = match state.users.get(&conn_id) {
        Some(u) => u,
        None => return false,
    };
    if user.interval_golpe > 0 {
        return false;
    }
    let golpe = state.intervals.golpe;
    let flechas = state.intervals.flechas;
    if let Some(u) = state.users.get_mut(&conn_id) {
        u.interval_golpe = golpe;
        u.interval_flechas = flechas;
    }
    true
}

pub fn puede_flechear(state: &mut GameState, conn_id: ConnectionId) -> bool {
    let user = match state.users.get(&conn_id) {
        Some(u) => u,
        None => return false,
    };
    if user.interval_flechas > 0 {
        return false;
    }
    let flechas = state.intervals.flechas;
    let golpe = 25;
    if let Some(u) = state.users.get_mut(&conn_id) {
        u.interval_flechas = flechas;
        u.interval_golpe = golpe;
    }
    true
}

pub fn puede_castear(state: &mut GameState, conn_id: ConnectionId) -> bool {
    let user = match state.users.get(&conn_id) {
        Some(u) => u,
        None => return false,
    };
    if user.interval_casteo > 0 {
        return false;
    }
    let hechizo = state.intervals.lanzar_hechizo;
    if let Some(u) = state.users.get_mut(&conn_id) {
        u.interval_casteo = hechizo;
    }
    true
}

pub fn puede_potear(state: &mut GameState, conn_id: ConnectionId) -> bool {
    let user = match state.users.get(&conn_id) {
        Some(u) => u,
        None => return false,
    };
    if user.interval_poteo > 0 {
        return false;
    }
    let poteo = state.intervals.poteo_u;
    if let Some(u) = state.users.get_mut(&conn_id) {
        u.interval_poteo = poteo;
    }
    true
}

pub fn puede_trabajar(state: &mut GameState, conn_id: ConnectionId) -> bool {
    let user = match state.users.get(&conn_id) {
        Some(u) => u,
        None => return false,
    };
    if user.interval_trabajar > 0 {
        return false;
    }
    let work = state.intervals.work;
    if let Some(u) = state.users.get_mut(&conn_id) {
        u.interval_trabajar = work;
    }
    true
}

pub fn puede_clickear(state: &mut GameState, conn_id: ConnectionId) -> bool {
    let user = match state.users.get(&conn_id) {
        Some(u) => u,
        None => return false,
    };
    if user.interval_click > 0 {
        return false;
    }
    let click = state.intervals.poteo_click;
    let poteo = state.intervals.poteo_u;
    if let Some(u) = state.users.get_mut(&conn_id) {
        u.interval_click = click;
        u.interval_poteo = poteo;
    }
    true
}

// =====================================================================
// World cleanup
// =====================================================================

pub fn clean_world_add_item(state: &mut GameState, map: i32, x: i32, y: i32, tiempo: i32, obj_index: i32) {
    for entry in state.clean_world.iter_mut() {
        if entry.map == 0 && entry.x == 0 && entry.y == 0 && entry.tiempo == 0 {
            *entry = CleanWorldEntry { map, x, y, tiempo, obj_index };
            return;
        }
    }
}

// =====================================================================
// Inventory helpers
// =====================================================================

/// Maximum items per stack.
pub(super) const MAX_INVENTORY_OBJS: i32 = 10000;
/// Maximum gold (VB6: MAXORO = 90,000,000).
pub(super) const MAX_GOLD: i64 = 90_000_000;
/// Gold item object index.
pub(super) const GOLD_OBJ_INDEX: i32 = 12;

pub(super) fn find_or_add_inv_slot(state: &mut GameState, conn_id: ConnectionId, obj_index: i32, amount: i32) -> Option<usize> {
    let user = state.users.get(&conn_id)?;
    let max_slots = user.current_inventory_slots;

    let mut stack_slot = None;
    let mut empty_slot = None;
    for (i, slot) in user.inventory.iter().enumerate() {
        // Stacking is allowed in any slot that already has the item
        if slot.obj_index == obj_index && slot.amount + amount <= MAX_INVENTORY_OBJS {
            stack_slot = Some(i);
            break;
        }
        // Empty slots only within current inventory limit
        if i < max_slots && slot.obj_index == 0 && empty_slot.is_none() {
            empty_slot = Some(i);
        }
    }

    let target = stack_slot.or(empty_slot)?;

    let user = state.users.get_mut(&conn_id)?;
    if user.inventory[target].obj_index == 0 {
        user.inventory[target].obj_index = obj_index;
        user.inventory[target].amount = amount;
    } else {
        user.inventory[target].amount += amount;
    }
    Some(target)
}

pub(super) fn check_has_items(state: &GameState, conn_id: ConnectionId, items: &[(i32, i32)]) -> bool {
    let user = match state.users.get(&conn_id) {
        Some(u) => u,
        None => return false,
    };

    for &(obj_index, needed) in items {
        if needed <= 0 { continue; }
        let total: i32 = user.inventory.iter()
            .filter(|s| s.obj_index == obj_index)
            .map(|s| s.amount)
            .sum();
        if total < needed {
            return false;
        }
    }
    true
}

pub(super) async fn remove_items_from_inv(state: &mut GameState, conn_id: ConnectionId, obj_index: i32, mut amount: i32) {
    if amount <= 0 { return; }

    let user = match state.users.get_mut(&conn_id) {
        Some(u) => u,
        None => return,
    };

    for slot in user.inventory.iter_mut() {
        if slot.obj_index == obj_index && amount > 0 {
            let remove = slot.amount.min(amount);
            slot.amount -= remove;
            amount -= remove;
            if slot.amount <= 0 {
                slot.obj_index = 0;
                slot.amount = 0;
                slot.equipped = false;
            }
        }
    }
}

pub(super) fn add_item_to_user_inventory(state: &mut GameState, conn_id: ConnectionId, obj_index: i32, amount: i32) -> bool {
    if let Some(user) = state.users.get_mut(&conn_id) {
        for slot in user.inventory.iter_mut() {
            if slot.obj_index == obj_index && slot.amount > 0 {
                slot.amount += amount;
                return true;
            }
        }
        for slot in user.inventory.iter_mut() {
            if slot.obj_index <= 0 || slot.amount <= 0 {
                slot.obj_index = obj_index;
                slot.amount = amount;
                slot.equipped = false;
                return true;
            }
        }
    }
    false
}

pub(super) fn user_has_items(state: &GameState, conn_id: ConnectionId, obj_index: i32, amount: i32) -> bool {
    if let Some(user) = state.users.get(&conn_id) {
        let mut total = 0i32;
        for slot in &user.inventory {
            if slot.obj_index == obj_index {
                total += slot.amount;
            }
        }
        total >= amount
    } else {
        false
    }
}

pub(super) fn get_equipped_anims(state: &GameState, conn_id: ConnectionId) -> (i32, i32, i32) {
    let user = match state.users.get(&conn_id) {
        Some(u) => u,
        None => return (0, 0, 0),
    };

    let weapon = if user.equip.weapon > 0 && user.equip.weapon <= MAX_INVENTORY_SLOTS {
        let obj_idx = user.inventory[user.equip.weapon - 1].obj_index;
        if obj_idx >= 1 {
            state.game_data.objects.get((obj_idx - 1) as usize)
                .map(|o| o.weapon_anim).unwrap_or(0)
        } else { 0 }
    } else { 0 };

    let shield = if user.equip.shield > 0 && user.equip.shield <= MAX_INVENTORY_SLOTS {
        let obj_idx = user.inventory[user.equip.shield - 1].obj_index;
        if obj_idx >= 1 {
            state.game_data.objects.get((obj_idx - 1) as usize)
                .map(|o| o.shield_anim).unwrap_or(0)
        } else { 0 }
    } else { 0 };

    let helmet = if user.equip.helmet > 0 && user.equip.helmet <= MAX_INVENTORY_SLOTS {
        let obj_idx = user.inventory[user.equip.helmet - 1].obj_index;
        if obj_idx >= 1 {
            state.game_data.objects.get((obj_idx - 1) as usize)
                .map(|o| o.casco_anim).unwrap_or(0)
        } else { 0 }
    } else { 0 };

    (weapon, shield, helmet)
}
