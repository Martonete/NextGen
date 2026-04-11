//! Shared helper functions used across all handler sub-modules.
//!
//! These are utility functions for: stats packets, tile access, random numbers,
//! inventory management, cooldown checks, and misc helpers.

use crate::data::maps::Trigger;
use crate::data::npcs::NpcType;
use crate::game::class_race::PlayerRace;
use crate::game::types::{CleanWorldEntry, GameState, MAX_INVENTORY_SLOTS, SendTarget, UserState};
use crate::game::world;
use crate::net::ConnectionId;
use crate::protocol::binary_packets;

// VB6 "empty" animation constants — re-exported from centralized constants module.
pub(super) use crate::game::constants::{NINGUN_ARMA, NINGUN_ESCUDO};
pub(super) const NINGUN_CASCO: i32 = 2;

// =====================================================================
// Clan helpers
// =====================================================================

/// VB6 SameClan: returns true if both users are logged, share the same guild (>0).
pub(super) fn same_clan(state: &GameState, a: ConnectionId, b: ConnectionId) -> bool {
    let ga = state.users.get(&a).map(|u| u.guild_index).unwrap_or(0);
    if ga == 0 {
        return false;
    }
    let gb = state.users.get(&b).map(|u| u.guild_index).unwrap_or(0);
    ga == gb
}

// =====================================================================
// Zone change detection
// =====================================================================

/// Check if player changed zone after moving, and send ZoneChange packet if so.
pub(super) async fn check_zone_change(state: &mut GameState, conn_id: ConnectionId) {
    let (map, x, y) = match state.users.get(&conn_id) {
        Some(u) if u.logged => (u.pos_map, u.pos_x, u.pos_y),
        _ => return,
    };

    let new_zone_id = get_zone_id_at(state, map, x, y);
    let old_zone_id = state
        .users
        .get(&conn_id)
        .map(|u| u.current_zone_id)
        .unwrap_or(0);

    if new_zone_id == old_zone_id {
        return;
    }

    // Update zone ID
    if let Some(user) = state.users.get_mut(&conn_id) {
        user.current_zone_id = new_zone_id;
    }

    // Build and send ZoneChange packet
    let map_idx = map as usize;
    let pkt = if let Some(Some(game_map)) = state.game_data.maps.get(map_idx) {
        if let Some(zone) = game_map
            .zones
            .as_ref()
            .and_then(|zs| zs.zones.iter().find(|z| z.id == new_zone_id))
        {
            binary_packets::write_zone_change(
                &zone.name,
                zone.zone_type as u8,
                zone.segura,
                zone.musica as i16,
                zone.lluvia,
                zone.nieve,
                zone.niebla,
                zone.x1 as i16,
                zone.y1 as i16,
                zone.x2 as i16,
                zone.y2 as i16,
                zone.ambient_r.clamp(0, 255) as u8,
                zone.ambient_g.clamp(0, 255) as u8,
                zone.ambient_b.clamp(0, 255) as u8,
                zone.niebla_density,
                zone.niebla_r,
                zone.niebla_g,
                zone.niebla_b,
                zone.niebla_speed_x,
                zone.niebla_speed_y,
            )
        } else {
            // Wilderness — no zone
            let is_safe = !game_map.info.pk;
            let music = game_map.info.music as i16;
            binary_packets::write_zone_change_wilderness(&game_map.info.name, is_safe, music)
        }
    } else {
        binary_packets::write_zone_change_wilderness(&format!("Mapa {}", map), false, 0)
    };

    state.send_bytes(conn_id, &pkt);
}

// =====================================================================
// Map tile helpers
// =====================================================================

pub(super) fn get_map_tile_trigger(state: &GameState, map: i32, x: i32, y: i32) -> Trigger {
    let map_idx = map as usize;
    if let Some(Some(game_map)) = state.game_data.maps.get(map_idx) {
        if let Some(tile) = game_map.tiles.get((x - 1) as usize, (y - 1) as usize) {
            return tile.trigger;
        }
    }
    Trigger::None
}

/// Check if a position is safe (no PvP) using Trigger > Zone > Map priority.
/// Returns true if the tile is in a safe area.
pub(super) fn is_safe_at(state: &GameState, map: i32, x: i32, y: i32) -> bool {
    let trigger = get_map_tile_trigger(state, map, x, y);
    // Trigger wins: SafeZone tile = always safe
    if trigger == Trigger::SafeZone {
        return true;
    }
    // Trigger wins: CombatZone tile = never safe (ring)
    if trigger == Trigger::CombatZone {
        return false;
    }

    // Zone check
    let map_idx = map as usize;
    if let Some(Some(game_map)) = state.game_data.maps.get(map_idx) {
        if let Some(zone) = game_map.get_zone_at(x - 1, y - 1) {
            return zone.segura;
        }
    }

    // Map fallback: pk=false means map is safe, pk=true means unsafe
    state
        .game_data
        .maps
        .get(map as usize)
        .and_then(|m| m.as_ref())
        .map(|m| !m.info.pk)
        .unwrap_or(false)
}

/// Shared implementation for all zone-based action-blocking checks.
///
/// Each check reads a different boolean from `ZoneInfo` (via `zone_field`) and a
/// different boolean from `MapInfo` (via `map_field`). Callers use the five typed
/// wrappers below so call sites remain self-documenting.
fn is_action_blocked_at<F, G>(
    state: &GameState,
    map: i32,
    x: i32,
    y: i32,
    zone_field: F,
    map_field: G,
) -> bool
where
    F: Fn(&crate::data::zones::ZoneData) -> bool,
    G: Fn(&crate::data::maps::MapInfo) -> bool,
{
    let map_idx = map as usize;
    if let Some(Some(game_map)) = state.game_data.maps.get(map_idx) {
        if let Some(zone) = game_map.get_zone_at(x - 1, y - 1) {
            return zone_field(zone);
        }
    }
    state
        .game_data
        .maps
        .get(map_idx)
        .and_then(|m| m.as_ref())
        .map(|m| map_field(&m.info))
        .unwrap_or(false)
}

/// Check if magic is blocked at a position (Zone > Map).
pub(super) fn is_magic_blocked_at(state: &GameState, map: i32, x: i32, y: i32) -> bool {
    is_action_blocked_at(state, map, x, y, |z| z.sin_magia, |m| m.magia_sin_efecto)
}

/// Check if invisibility is blocked at a position (Zone > Map).
pub(super) fn is_invi_blocked_at(state: &GameState, map: i32, x: i32, y: i32) -> bool {
    is_action_blocked_at(state, map, x, y, |z| z.sin_invi, |m| m.invi_sin_efecto)
}

/// Check if invocation is blocked at a position (Zone > Map).
pub(super) fn is_invocar_blocked_at(state: &GameState, map: i32, x: i32, y: i32) -> bool {
    is_action_blocked_at(
        state,
        map,
        x,
        y,
        |z| z.sin_invocar,
        |m| m.invocar_sin_efecto,
    )
}

/// Check if hiding (ocultar) is blocked at a position (Zone > Map).
pub(super) fn is_ocultar_blocked_at(state: &GameState, map: i32, x: i32, y: i32) -> bool {
    is_action_blocked_at(
        state,
        map,
        x,
        y,
        |z| z.sin_ocultar,
        |m| m.ocultar_sin_efecto,
    )
}

/// Check if resurrection is blocked at a position (Zone > Map).
pub(super) fn is_resu_blocked_at(state: &GameState, map: i32, x: i32, y: i32) -> bool {
    is_action_blocked_at(state, map, x, y, |z| z.sin_resucitar, |m| m.resu_sin_efecto)
}

/// Get the zone ID at a tile position (0 = no zone).
pub(super) fn get_zone_id_at(state: &GameState, map: i32, x: i32, y: i32) -> u16 {
    let map_idx = map as usize;
    if let Some(Some(game_map)) = state.game_data.maps.get(map_idx) {
        if let Some(tile) = game_map.tiles.get((x - 1) as usize, (y - 1) as usize) {
            return tile.zone_id;
        }
    }
    0
}

/// Get the zone name at a position, or the map name if no zone.
pub(super) fn get_zone_name_at(state: &GameState, map: i32, x: i32, y: i32) -> String {
    let map_idx = map as usize;
    if let Some(Some(game_map)) = state.game_data.maps.get(map_idx) {
        if let Some(zone) = game_map.get_zone_at(x - 1, y - 1) {
            return zone.name.clone();
        }
        return game_map.info.name.clone();
    }
    format!("Mapa {}", map)
}

pub(super) fn get_map_tile_obj(state: &GameState, map: i32, x: i32, y: i32) -> i32 {
    let map_idx = map as usize;
    if let Some(Some(game_map)) = state.game_data.maps.get(map_idx) {
        if let Some(tile) = game_map.tiles.get((x - 1) as usize, (y - 1) as usize) {
            return tile.obj.obj_index as i32;
        }
    }
    0
}

pub(super) fn get_map_tile_particle(state: &GameState, map: i32, x: i32, y: i32) -> i16 {
    let map_idx = map as usize;
    if let Some(Some(game_map)) = state.game_data.maps.get(map_idx) {
        if let Some(tile) = game_map.tiles.get((x - 1) as usize, (y - 1) as usize) {
            return tile.particle_group_index;
        }
    }
    0
}

pub(super) fn set_map_tile_obj(state: &mut GameState, map: i32, x: i32, y: i32, new_obj: i16) {
    let map_idx = map as usize;
    if let Some(Some(game_map)) = state.game_data.maps.get_mut(map_idx) {
        if let Some(tile) = game_map.tiles.get_mut((x - 1) as usize, (y - 1) as usize) {
            tile.obj.obj_index = new_obj;
        }
    }
}

pub(super) fn set_map_tile_blocked(state: &mut GameState, map: i32, x: i32, y: i32, blocked: bool) {
    let map_idx = map as usize;
    if let Some(Some(game_map)) = state.game_data.maps.get_mut(map_idx) {
        if let Some(tile) = game_map.tiles.get_mut((x - 1) as usize, (y - 1) as usize) {
            tile.blocked = blocked;
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
        state.send_bytes(conn_id, &pkt);
    }
}

pub(super) async fn send_stats_mana(state: &mut GameState, conn_id: ConnectionId) {
    if let Some(u) = state.users.get(&conn_id) {
        let pkt = binary_packets::write_update_mana(u.max_mana as i16, u.min_mana as i16);
        state.send_bytes(conn_id, &pkt);
    }
}

pub(super) async fn send_stats_sta(state: &mut GameState, conn_id: ConnectionId) {
    if let Some(u) = state.users.get(&conn_id) {
        let pkt = binary_packets::write_update_sta(u.max_sta as i16, u.min_sta as i16);
        state.send_bytes(conn_id, &pkt);
    }
}

pub(super) async fn send_stats_gold(state: &mut GameState, conn_id: ConnectionId) {
    if let Some(u) = state.users.get(&conn_id) {
        let pkt = binary_packets::write_update_gold(u.gold as i32);
        state.send_bytes(conn_id, &pkt);
    }
}

pub(super) async fn send_stats_exp(state: &mut GameState, conn_id: ConnectionId) {
    if let Some(u) = state.users.get(&conn_id) {
        let pkt = binary_packets::write_update_exp(u.exp as i32);
        state.send_bytes(conn_id, &pkt);
    }
}

pub(super) async fn send_hunger_thirst(state: &mut GameState, conn_id: ConnectionId) {
    let (max_agua, min_agua, max_ham, min_ham) = match state.users.get(&conn_id) {
        Some(u) => (
            u.max_agua as u8,
            u.min_agua as u8,
            u.max_ham as u8,
            u.min_ham as u8,
        ),
        None => return,
    };
    let pkt = binary_packets::write_update_hunger_thirst(max_agua, min_agua, max_ham, min_ham);
    state.send_bytes(conn_id, &pkt);
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
    let occupied = state
        .world
        .grid(map)
        .and_then(|g| g.tile(x, y))
        .map(|t| t.user_conn.is_some() || t.npc_index > 0)
        .unwrap_or(false);
    let blocked = state.is_tile_blocked(map, x, y);

    if !occupied && !blocked {
        return (x, y);
    }

    let (grid_w, grid_h) = state.grid_dimensions(map);
    for radius in 1i32..=10 {
        for dy in -radius..=radius {
            for dx in -radius..=radius {
                if dx.abs() != radius && dy.abs() != radius {
                    continue;
                }
                let nx = x + dx;
                let ny = y + dy;
                if nx < 1 || nx > grid_w || ny < 1 || ny > grid_h {
                    continue;
                }
                let tile_blocked = state.is_tile_blocked(map, nx, ny);
                if tile_blocked {
                    continue;
                }
                let tile_free = state
                    .world
                    .grid(map)
                    .and_then(|g| g.tile(nx, ny))
                    .map(|t| t.user_conn.is_none() && t.npc_index == 0)
                    .unwrap_or(false);
                if tile_free {
                    return (nx, ny);
                }
            }
        }
    }

    // Fallback: return the original position only if it's within bounds; clamp otherwise.
    let (grid_w, grid_h) = state.grid_dimensions(map);
    let safe_x = x.clamp(1, grid_w.max(1));
    let safe_y = y.clamp(1, grid_h.max(1));
    (safe_x, safe_y)
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

    let offsets = [
        (0, 1),
        (1, 0),
        (0, -1),
        (-1, 0),
        (1, 1),
        (-1, -1),
        (1, -1),
        (-1, 1),
    ];
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
    let (grid_w, grid_h) = state.grid_dimensions(map);
    for ring in 0..=12 {
        for ty in (y - ring)..=(y + ring) {
            for tx in (x - ring)..=(x + ring) {
                if tx < 1 || tx > grid_w || ty < 1 || ty > grid_h {
                    continue;
                }
                if !state.is_tile_blocked(map, tx, ty) {
                    return (tx, ty);
                }
            }
        }
    }
    (0, 0)
}

pub(super) fn zona_cura(state: &GameState, map: i32, px: i32, py: i32) -> bool {
    let (grid_w, grid_h) = state.grid_dimensions(map);
    let min_x = (px - world::MIN_X_BORDER + 1).max(1);
    let max_x = (px + world::MIN_X_BORDER - 1).min(grid_w);
    let min_y = (py - world::MIN_Y_BORDER + 1).max(1);
    let max_y = (py + world::MIN_Y_BORDER - 1).min(grid_h);

    if let Some(grid) = state.world.grid(map) {
        for cy in min_y..=max_y {
            for cx in min_x..=max_x {
                if let Some(tile) = grid.tile(cx, cy) {
                    if tile.npc_index > 0 {
                        if let Some(npc) = state.get_npc(tile.npc_index as usize) {
                            if npc.npc_type == crate::data::npcs::NpcType::Reviver && npc.is_alive()
                            {
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
    name.chars()
        .all(|c| c.is_ascii_alphanumeric() || c == '_' || c == '-' || c == ' ')
}

pub(super) async fn is_char_banned(pool: &sqlx::PgPool, char_name: &str) -> bool {
    let result = sqlx::query_scalar::<_, bool>(
        "SELECT banned FROM characters WHERE UPPER(name) = UPPER($1)",
    )
    .bind(char_name)
    .fetch_optional(pool)
    .await;
    matches!(result, Ok(Some(true)))
}

pub(super) fn is_same_ip_online(state: &GameState, exclude_id: ConnectionId, ip: &str) -> bool {
    state
        .users
        .values()
        .any(|u| u.conn_id != exclude_id && u.logged && u.ip == ip)
}

pub(super) fn poner_puntos(num: i64) -> String {
    let s = num.to_string();
    let len = s.len();
    if len <= 3 {
        return s;
    }
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
        let days_in_year = if y % 4 == 0 && (y % 100 != 0 || y % 400 == 0) {
            366
        } else {
            365
        };
        if remaining < days_in_year {
            break;
        }
        remaining -= days_in_year;
        y += 1;
    }
    let leap = y % 4 == 0 && (y % 100 != 0 || y % 400 == 0);
    let month_days = [
        31,
        if leap { 29 } else { 28 },
        31,
        30,
        31,
        30,
        31,
        31,
        30,
        31,
        30,
        31,
    ];
    let mut m = 0usize;
    for i in 0..12 {
        if remaining < month_days[i] {
            m = i;
            break;
        }
        remaining -= month_days[i];
    }
    format!(
        "{:02}/{:02}/{} {:02}:{:02}",
        remaining + 1,
        m + 1,
        y,
        hours,
        minutes
    )
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

pub(crate) fn rand_range(min: i32, max: i32) -> i32 {
    if min >= max {
        return min;
    }
    let range = (max - min + 1) as u32;
    min + (rand_simple_u32() % range) as i32
}

pub(super) fn random_number(min: i32, max: i32) -> i32 {
    use std::time::{SystemTime, UNIX_EPOCH};
    if min >= max {
        return min;
    }
    let seed = SystemTime::now()
        .duration_since(UNIX_EPOCH)
        .unwrap_or_default()
        .as_nanos() as u64;
    let val = seed
        .wrapping_mul(6364136223846793005)
        .wrapping_add(1442695040888963407);
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
    let poteo = state.intervals.poteo_u;
    let golpe_magia = state.intervals.golpe_magia;
    if let Some(u) = state.users.get_mut(&conn_id) {
        u.interval_golpe = golpe;
        u.interval_flechas = flechas;
        // VB6: IntervaloPermiteAtacar also resets potion timer (TimerGolpeUsar = TActual)
        u.interval_poteo = poteo;
        // VB6: IntervaloGolpeMagia — melee sets spell cross-cooldown
        u.interval_casteo = u.interval_casteo.max(golpe_magia);
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
    let golpe = state.intervals.golpe;
    let poteo = state.intervals.poteo_u;
    let golpe_magia = state.intervals.golpe_magia;
    if let Some(u) = state.users.get_mut(&conn_id) {
        u.interval_flechas = flechas;
        u.interval_golpe = golpe;
        // VB6: Also resets potion timer and spell cross-cooldown
        u.interval_poteo = poteo;
        u.interval_casteo = u.interval_casteo.max(golpe_magia);
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
    let magia_golpe = state.intervals.magia_golpe;
    if let Some(u) = state.users.get_mut(&conn_id) {
        u.interval_casteo = hechizo;
        // VB6: IntervaloMagiaGolpe — spell sets melee cross-cooldown
        u.interval_golpe = u.interval_golpe.max(magia_golpe);
        u.interval_flechas = u.interval_flechas.max(magia_golpe);
        // VB6: Also resets potion timer
        u.interval_poteo = u.interval_poteo.max(0);
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

pub fn clean_world_add_item(
    state: &mut GameState,
    map: i32,
    x: i32,
    y: i32,
    tiempo: i32,
    obj_index: i32,
) {
    // First check if there's already an entry for the same tile — update it instead
    // of creating a duplicate. This handles gold stacking and item replacement.
    for entry in state.clean_world.iter_mut() {
        if entry.map == map && entry.x == x && entry.y == y && entry.tiempo > 0 {
            entry.tiempo = tiempo;
            entry.obj_index = obj_index;
            return;
        }
    }
    // No existing entry — find an empty slot
    for entry in state.clean_world.iter_mut() {
        if entry.map == 0 && entry.x == 0 && entry.y == 0 && entry.tiempo == 0 {
            *entry = CleanWorldEntry {
                map,
                x,
                y,
                tiempo,
                obj_index,
            };
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

pub(super) fn find_or_add_inv_slot(
    state: &mut GameState,
    conn_id: ConnectionId,
    obj_index: i32,
    amount: i32,
) -> Option<usize> {
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

pub(super) fn check_has_items(
    state: &GameState,
    conn_id: ConnectionId,
    items: &[(i32, i32)],
) -> bool {
    let user = match state.users.get(&conn_id) {
        Some(u) => u,
        None => return false,
    };

    for &(obj_index, needed) in items {
        if needed <= 0 {
            continue;
        }
        let total: i32 = user
            .inventory
            .iter()
            .filter(|s| s.obj_index == obj_index)
            .map(|s| s.amount)
            .sum();
        if total < needed {
            return false;
        }
    }
    true
}

pub(super) async fn remove_items_from_inv(
    state: &mut GameState,
    conn_id: ConnectionId,
    obj_index: i32,
    mut amount: i32,
) {
    if amount <= 0 {
        return;
    }

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

pub(super) fn add_item_to_user_inventory(
    state: &mut GameState,
    conn_id: ConnectionId,
    obj_index: i32,
    amount: i32,
) -> bool {
    if let Some(user) = state.users.get_mut(&conn_id) {
        let stack_idx = user
            .inventory
            .iter()
            .position(|s| s.obj_index == obj_index && s.amount > 0);
        if let Some(i) = stack_idx {
            user.inventory[i].amount += amount;

            return true;
        }
        let empty_idx = user
            .inventory
            .iter()
            .position(|s| s.obj_index <= 0 || s.amount <= 0);
        if let Some(i) = empty_idx {
            user.inventory[i].obj_index = obj_index;
            user.inventory[i].amount = amount;
            user.inventory[i].equipped = false;

            return true;
        }
    }
    false
}

pub(super) fn user_has_items(
    state: &GameState,
    conn_id: ConnectionId,
    obj_index: i32,
    amount: i32,
) -> bool {
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
            state
                .game_data
                .objects
                .get((obj_idx - 1) as usize)
                .map(|o| o.weapon_anim)
                .unwrap_or(0)
        } else {
            0
        }
    } else {
        0
    };

    let shield = if user.equip.shield > 0 && user.equip.shield <= MAX_INVENTORY_SLOTS {
        let obj_idx = user.inventory[user.equip.shield - 1].obj_index;
        if obj_idx >= 1 {
            state
                .game_data
                .objects
                .get((obj_idx - 1) as usize)
                .map(|o| o.shield_anim)
                .unwrap_or(0)
        } else {
            0
        }
    } else {
        0
    };

    let helmet = if user.equip.helmet > 0 && user.equip.helmet <= MAX_INVENTORY_SLOTS {
        let obj_idx = user.inventory[user.equip.helmet - 1].obj_index;
        if obj_idx >= 1 {
            state
                .game_data
                .objects
                .get((obj_idx - 1) as usize)
                .map(|o| o.casco_anim)
                .unwrap_or(0)
        } else {
            0
        }
    } else {
        0
    };

    (weapon, shield, helmet)
}

// =====================================================================
// Character body/head helpers
// =====================================================================

/// Naked body IDs by race + gender (VB6 DarCuerpoDesnudo)
pub(super) fn naked_body(race: PlayerRace, gender: i32) -> i32 {
    let is_female = gender == 2;
    match (race, is_female) {
        (PlayerRace::Humano, false) | (PlayerRace::Elfo, false) => 21,
        (PlayerRace::Humano, true) | (PlayerRace::Elfo, true) => 39,
        (PlayerRace::ElfoOscuro, false) => 32,
        (PlayerRace::ElfoOscuro, true) => 40,
        (PlayerRace::Enano, false) | (PlayerRace::Gnomo, false) => 53,
        (PlayerRace::Enano, true) | (PlayerRace::Gnomo, true) => 60,
    }
}

/// Default head GRH by race + gender (for recovery when head is corrupted to 500)
pub(super) fn default_head_for_race(race: PlayerRace, gender: i32) -> i32 {
    let is_female = gender == 2;
    match (race, is_female) {
        (PlayerRace::Humano, true) | (PlayerRace::Elfo, true) => 70,
        (PlayerRace::Humano, false) => 1,
        (PlayerRace::Elfo, false) => 101,
        (PlayerRace::ElfoOscuro, true) => 480,
        (PlayerRace::ElfoOscuro, false) => 401,
        (PlayerRace::Enano, true) | (PlayerRace::Gnomo, true) => 270,
        (PlayerRace::Enano, false) | (PlayerRace::Gnomo, false) => 201,
    }
}

// =====================================================================
// Resurrection helpers
// =====================================================================

/// /RESUCITAR — Resurrect. VB6: requires Revividor NPC target, distance <= 10, must be dead.
pub(super) async fn handle_resucitar(state: &mut GameState, conn_id: ConnectionId) {
    let (dead, target_npc) = match state.users.get(&conn_id) {
        Some(u) if u.logged => (u.dead, u.target_npc),
        _ => return,
    };

    if target_npc == 0 {
        state.send_msg_id(conn_id, 9, "");
        return;
    }

    let npc_type = state.get_npc(target_npc).map(|n| n.npc_type);
    if npc_type != Some(NpcType::Reviver) || !dead {
        return;
    }

    let (npc_map, npc_x, npc_y) = match state.get_npc(target_npc) {
        Some(npc) => (npc.map, npc.x, npc.y),
        None => return,
    };
    let (u_map, u_x, u_y) = match state.users.get(&conn_id) {
        Some(u) => (u.pos_map, u.pos_x, u.pos_y),
        None => return,
    };
    if u_map != npc_map || (u_x - npc_x).abs() > 10 || (u_y - npc_y).abs() > 10 {
        state.send_msg_id(conn_id, 11, "");
        return;
    }

    revive_user(state, conn_id).await;
    state.send_msg_id(conn_id, 396, "");
}

/// Core revive logic — shared between /RESUCITAR, resurrection spell, and delayed resurrection timer.
/// VB6: RevivirUsuario() — sets dead=false, HP=35, DarCuerpoDesnudo, ChangeUserChar(OrigChar.Head).
pub(super) async fn revive_user(state: &mut GameState, conn_id: ConnectionId) {
    let (race, gender, max_hp, orig_head, constitution) = match state.users.get(&conn_id) {
        Some(u) if u.logged && u.dead => (u.race, u.gender, u.max_hp, u.orig_head, u.attributes[4]),
        _ => return,
    };

    // VB6 13.3 parity: MinHP = UserAtributos(Constitucion)
    let revive_hp = constitution.min(max_hp);
    let new_body = naked_body(race, gender);

    if let Some(user) = state.users.get_mut(&conn_id) {
        user.dead = false;
        user.min_hp = revive_hp;
        user.body = new_body;
        user.head = orig_head;
        user.traveling = false;
        user.counter_go_home = 0;
    }

    let (map, x, y, char_index, heading, weapon_anim, shield_anim, casco_anim) =
        match state.users.get(&conn_id) {
            Some(u) => (
                u.pos_map,
                u.pos_x,
                u.pos_y,
                u.char_index,
                u.heading,
                u.weapon_anim,
                u.shield_anim,
                u.casco_anim,
            ),
            None => return,
        };

    state.send_data_bytes(
        SendTarget::ToArea { map, x, y },
        &binary_packets::write_char_particle_create(char_index.0 as i16, 65),
    );

    state.send_data_bytes(
        SendTarget::ToArea { map, x, y },
        &binary_packets::write_character_change(
            char_index.0 as i16,
            new_body as i16,
            orig_head as i16,
            heading as u8,
            weapon_anim as i16,
            shield_anim as i16,
            casco_anim as i16,
            0,
            0,
        ),
    );

    send_stats_hp(state, conn_id).await;
    send_stats_mana(state, conn_id).await;
}

// =====================================================================
// Inventory/spell packet helpers
// =====================================================================

/// Build ANM packet (equipment hitbox stats — 20 comma-separated fields).
pub(crate) fn build_anm_packet(state: &GameState, conn_id: ConnectionId) -> String {
    let user = match state.users.get(&conn_id) {
        Some(u) => u,
        None => return "ANM0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0".into(),
    };

    let (min_arma, max_arma) = if user.equip.weapon > 0 && user.equip.weapon <= MAX_INVENTORY_SLOTS
    {
        let obj_idx = user.inventory[user.equip.weapon - 1].obj_index;
        if obj_idx > 0 {
            state
                .game_data
                .objects
                .get(obj_idx as usize)
                .map(|o| (o.min_hit, o.max_hit))
                .unwrap_or((0, 0))
        } else {
            (0, 0)
        }
    } else {
        (0, 0)
    };

    let (min_armor, max_armor) = if user.equip.armor > 0 && user.equip.armor <= MAX_INVENTORY_SLOTS
    {
        let obj_idx = user.inventory[user.equip.armor - 1].obj_index;
        if obj_idx > 0 {
            state
                .game_data
                .objects
                .get(obj_idx as usize)
                .map(|o| (o.min_def, o.max_def))
                .unwrap_or((0, 0))
        } else {
            (0, 0)
        }
    } else {
        (0, 0)
    };

    let (min_shield, max_shield) =
        if user.equip.shield > 0 && user.equip.shield <= MAX_INVENTORY_SLOTS {
            let obj_idx = user.inventory[user.equip.shield - 1].obj_index;
            if obj_idx > 0 {
                state
                    .game_data
                    .objects
                    .get(obj_idx as usize)
                    .map(|o| (o.min_def, o.max_def))
                    .unwrap_or((0, 0))
            } else {
                (0, 0)
            }
        } else {
            (0, 0)
        };

    let (min_helmet, max_helmet) =
        if user.equip.helmet > 0 && user.equip.helmet <= MAX_INVENTORY_SLOTS {
            let obj_idx = user.inventory[user.equip.helmet - 1].obj_index;
            if obj_idx > 0 {
                state
                    .game_data
                    .objects
                    .get(obj_idx as usize)
                    .map(|o| (o.min_def, o.max_def))
                    .unwrap_or((0, 0))
            } else {
                (0, 0)
            }
        } else {
            (0, 0)
        };

    format!(
        "ANM{},{},{},{},{},{},{},{},0,0,0,0,0,0,0,0,0,0,0,0",
        min_arma, max_arma, min_armor, max_armor, min_shield, max_shield, min_helmet, max_helmet,
    )
}

/// Send a single inventory slot CSI packet.
pub(super) async fn send_inventory_slot(state: &mut GameState, conn_id: ConnectionId, idx: usize) {
    let slot = idx + 1;
    let inv = match state.users.get(&conn_id) {
        Some(u) => u.inventory[idx].clone(),
        None => return,
    };

    if inv.obj_index == 0 {
        state.send_bytes(
            conn_id,
            &binary_packets::write_change_inventory_slot(
                slot as u8, 0, "(None)", 0, false, 0, 0, 0, 0, 0, 0, 0.0,
            ),
        );
    } else {
        let obj = state.get_object(inv.obj_index).cloned();
        let (name, grh, obj_type, max_hit, min_hit, max_def, min_def, valor) = match obj {
            Some(o) => (
                o.name.clone(),
                o.grh_index,
                o.obj_type as i32,
                o.max_hit,
                o.min_hit,
                o.max_def,
                o.min_def,
                o.valor / 3,
            ),
            None => ("???".into(), 0, 0, 0, 0, 0, 0, 0),
        };
        state.send_bytes(
            conn_id,
            &binary_packets::write_change_inventory_slot(
                slot as u8,
                inv.obj_index as i16,
                &name,
                inv.amount as i16,
                inv.equipped,
                grh as i16,
                obj_type as u8,
                max_hit as i16,
                min_hit as i16,
                max_def as i16,
                min_def as i16,
                valor as f32,
            ),
        );
    }
}

/// Send all inventory slots.
pub(super) async fn send_full_inventory(state: &mut GameState, conn_id: ConnectionId) {
    for idx in 0..MAX_INVENTORY_SLOTS {
        send_inventory_slot(state, conn_id, idx).await;
    }
}

/// Send all spell slots.
pub(super) async fn send_full_spells(state: &mut GameState, conn_id: ConnectionId) {
    let spells = match state.users.get(&conn_id) {
        Some(u) => u.spells,
        None => return,
    };

    for (i, &spell_id) in spells.iter().enumerate() {
        let slot = i + 1;
        if spell_id > 0 {
            let name = state
                .get_spell(spell_id)
                .map(|s| s.nombre.clone())
                .unwrap_or_else(|| "(Desconocido)".into());
            state.send_bytes(
                conn_id,
                &binary_packets::write_change_spell_slot(slot as u8, spell_id as i16, &name),
            );
        } else {
            state.send_bytes(
                conn_id,
                &binary_packets::write_change_spell_slot(slot as u8, 0, "(Nada)"),
            );
        }
    }
}

// =====================================================================
// Auto-heal helper
// =====================================================================

/// VB6 AutoCuraUser — Automatic priest heal/revive/cure.
/// Called when player moves into ZonaCura range of a Revividor NPC.
pub(super) async fn auto_cura_user(state: &mut GameState, conn_id: ConnectionId) {
    let (dead, hp_low, poisoned) = match state.users.get(&conn_id) {
        Some(u) if u.logged => (u.dead, u.min_hp < u.max_hp, u.poisoned),
        _ => return,
    };

    if dead {
        revive_user(state, conn_id).await;
        if let Some(user) = state.users.get_mut(&conn_id) {
            let max_hp = user.max_hp;
            let max_sta = user.max_sta;
            user.min_hp = max_hp;
            user.min_sta = max_sta;
        }
        state.send_msg_id(conn_id, 693, "");
        let (map, x, y) = match state.users.get(&conn_id) {
            Some(u) => (u.pos_map, u.pos_x, u.pos_y),
            None => return,
        };
        state.send_data_bytes(
            SendTarget::ToArea { map, x, y },
            &binary_packets::write_play_wave(20, x as i16, y as i16),
        );
        send_stats_hp(state, conn_id).await;
        send_stats_sta(state, conn_id).await;
    } else if hp_low {
        if let Some(user) = state.users.get_mut(&conn_id) {
            let max_hp = user.max_hp;
            user.min_hp = max_hp;
        }
        state.send_msg_id(conn_id, 694, "");
        let (map, x, y) = match state.users.get(&conn_id) {
            Some(u) => (u.pos_map, u.pos_x, u.pos_y),
            None => return,
        };
        state.send_data_bytes(
            SendTarget::ToArea { map, x, y },
            &binary_packets::write_play_wave(20, x as i16, y as i16),
        );
        send_stats_hp(state, conn_id).await;
    }

    if poisoned {
        if let Some(user) = state.users.get_mut(&conn_id) {
            user.poisoned = false;
        }
    }
}
