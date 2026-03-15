// World state — runtime map grids, character index management, and area visibility.
//
// Grids are dynamically sized (not hardcoded to 100x100). Each grid stores
// its own dimensions and zone configuration. The zone system provides O(1)
// area user lookups for broadcast operations.

use std::collections::HashMap;
use crate::net::ConnectionId;

// Default map dimensions (VB6 standard — used when loading .map files)
pub const MAP_WIDTH: usize = 100;
pub const MAP_HEIGHT: usize = 100;

// Client viewport (tiles visible on screen)
pub const X_WINDOW: i32 = 17;
pub const Y_WINDOW: i32 = 13;

// Border offsets (half viewport) — used for area queries and range checks
pub const MIN_X_BORDER: i32 = X_WINDOW / 2; // 8
pub const MIN_Y_BORDER: i32 = Y_WINDOW / 2; // 6

// Default zone size for area tracking
const DEFAULT_ZONE_SIZE: i32 = 9;

// Heading directions
pub const HEADING_NORTH: i32 = 1;
pub const HEADING_EAST: i32 = 2;
pub const HEADING_SOUTH: i32 = 3;
pub const HEADING_WEST: i32 = 4;

/// Character index — a unique ID visible to clients for rendering.
/// Each logged-in character (user or NPC) gets one.
#[derive(Debug, Clone, Copy, PartialEq, Eq, Hash)]
pub struct CharIndex(pub i32);

/// Ground item on a tile.
#[derive(Debug, Clone, Copy, Default)]
pub struct GroundItem {
    pub obj_index: i32, // 0 = no item
    pub amount: i32,
}

/// Per-tile runtime data (who's standing here).
#[derive(Debug, Clone, Default)]
pub struct TileRuntime {
    pub user_conn: Option<ConnectionId>, // Connection ID of user on this tile
    pub npc_index: i32,                  // NPC runtime index (0 = none)
    pub ground_item: GroundItem,         // Item on the ground (0 = none)
}

/// Runtime map grid — dynamically sized, tracks positions and zone-based user lists.
pub struct MapGrid {
    /// Flat tile array indexed as tiles[y * width + x] (0-based internally).
    tiles: Vec<TileRuntime>,
    /// Grid dimensions.
    pub width: i32,
    pub height: i32,
    pub num_users: i32,

    // Zone tracking for O(1) area user lookups
    zone_size: i32,
    zones_x: i32,
    zones_y: i32,
    /// Per-zone user lists: (conn_id, tile_x, tile_y) for distance filtering.
    zone_users: Vec<Vec<(ConnectionId, i32, i32)>>,
}

impl MapGrid {
    /// Create a new grid with the given dimensions and zone size.
    pub fn with_size(width: i32, height: i32, zone_size: i32) -> Self {
        let zones_x = (width + zone_size - 1) / zone_size;
        let zones_y = (height + zone_size - 1) / zone_size;
        let total_zones = (zones_x * zones_y) as usize;
        Self {
            tiles: (0..(width * height) as usize).map(|_| TileRuntime::default()).collect(),
            width,
            height,
            num_users: 0,
            zone_size,
            zones_x,
            zones_y,
            zone_users: (0..total_zones).map(|_| Vec::new()).collect(),
        }
    }

    /// Create a grid with default 100x100 dimensions (VB6 compat).
    pub fn new() -> Self {
        Self::with_size(MAP_WIDTH as i32, MAP_HEIGHT as i32, DEFAULT_ZONE_SIZE)
    }

    /// Convert 1-based tile coords to flat index. Returns None if out of bounds.
    #[inline]
    fn tile_index(&self, x: i32, y: i32) -> Option<usize> {
        if x >= 1 && x <= self.width && y >= 1 && y <= self.height {
            Some(((y - 1) * self.width + (x - 1)) as usize)
        } else {
            None
        }
    }

    /// Convert 1-based tile coords to zone index.
    #[inline]
    fn zone_idx(&self, x: i32, y: i32) -> usize {
        let zx = ((x - 1) / self.zone_size).min(self.zones_x - 1);
        let zy = ((y - 1) / self.zone_size).min(self.zones_y - 1);
        (zy * self.zones_x + zx) as usize
    }

    /// Get tile at (x, y). Coordinates are 1-based.
    pub fn tile(&self, x: i32, y: i32) -> Option<&TileRuntime> {
        self.tile_index(x, y).map(|i| &self.tiles[i])
    }

    /// Get mutable tile at (x, y). Coordinates are 1-based.
    pub fn tile_mut(&mut self, x: i32, y: i32) -> Option<&mut TileRuntime> {
        self.tile_index(x, y).map(|i| &mut self.tiles[i])
    }

    /// Check if a tile is free for walking.
    pub fn is_tile_free(&self, x: i32, y: i32) -> bool {
        if let Some(t) = self.tile(x, y) {
            t.user_conn.is_none() && t.npc_index == 0
        } else {
            false
        }
    }

    /// Add a user to a zone's tracking list.
    fn zone_add(&mut self, x: i32, y: i32, conn: ConnectionId) {
        let idx = self.zone_idx(x, y);
        if !self.zone_users[idx].iter().any(|&(c, _, _)| c == conn) {
            self.zone_users[idx].push((conn, x, y));
        }
    }

    /// Remove a user from a zone's tracking list.
    fn zone_remove(&mut self, x: i32, y: i32, conn: ConnectionId) {
        let idx = self.zone_idx(x, y);
        self.zone_users[idx].retain(|&(c, _, _)| c != conn);
    }

    /// Get all users within viewport range around a point.
    /// Checks 3x3 zone neighborhood, filters by exact viewport distance.
    pub fn get_nearby_users(&self, center_x: i32, center_y: i32) -> Vec<ConnectionId> {
        let czx = (center_x - 1) / self.zone_size;
        let czy = (center_y - 1) / self.zone_size;
        let mut users = Vec::new();
        for dy in -1..=1i32 {
            for dx in -1..=1i32 {
                let zx = czx + dx;
                let zy = czy + dy;
                if zx >= 0 && zx < self.zones_x && zy >= 0 && zy < self.zones_y {
                    let idx = (zy * self.zones_x + zx) as usize;
                    for &(conn, ux, uy) in &self.zone_users[idx] {
                        if (ux - center_x).abs() <= MIN_X_BORDER
                            && (uy - center_y).abs() <= MIN_Y_BORDER
                        {
                            users.push(conn);
                        }
                    }
                }
            }
        }
        users
    }

    /// Get all user connections on this grid.
    pub fn get_all_users(&self) -> Vec<ConnectionId> {
        self.zone_users.iter()
            .flat_map(|zone| zone.iter().map(|&(conn, _, _)| conn))
            .collect()
    }
}

/// World-level state managing all map grids and character indices.
pub struct WorldState {
    /// Runtime grids per map (sparse — only loaded maps).
    pub grids: HashMap<i32, MapGrid>,

    /// Next character index to assign.
    next_char_index: i32,
}

impl WorldState {
    pub fn new(map_count: usize) -> Self {
        let mut grids = HashMap::new();
        // Pre-create grids for all loaded maps (default 100x100)
        for i in 1..=map_count {
            grids.insert(i as i32, MapGrid::new());
        }
        Self {
            grids,
            next_char_index: 1,
        }
    }

    /// Allocate a new unique character index.
    pub fn alloc_char_index(&mut self) -> CharIndex {
        let idx = CharIndex(self.next_char_index);
        self.next_char_index += 1;
        idx
    }

    /// Ensure a grid exists for a reloaded map.
    pub fn reload_map(&mut self, map_num: usize, _maps: &[Option<crate::data::maps::GameMap>]) {
        self.grids.entry(map_num as i32).or_insert_with(MapGrid::new);
    }

    /// Get grid for a map.
    pub fn grid(&self, map: i32) -> Option<&MapGrid> {
        self.grids.get(&map)
    }

    pub fn grid_mut(&mut self, map: i32) -> &mut MapGrid {
        self.grids.entry(map).or_insert_with(MapGrid::new)
    }

    /// Place a user on a tile and register in zone tracking.
    pub fn place_user(&mut self, map: i32, x: i32, y: i32, conn_id: ConnectionId) {
        let grid = self.grid_mut(map);
        if let Some(tile) = grid.tile_mut(x, y) {
            tile.user_conn = Some(conn_id);
        }
        grid.zone_add(x, y, conn_id);
        grid.num_users += 1;
    }

    /// Remove a user from a tile and unregister from zone tracking.
    pub fn remove_user(&mut self, map: i32, x: i32, y: i32) {
        if let Some(grid) = self.grids.get_mut(&map) {
            let conn = grid.tile(x, y).and_then(|t| t.user_conn);
            if let Some(tile) = grid.tile_mut(x, y) {
                tile.user_conn = None;
            }
            if let Some(conn_id) = conn {
                grid.zone_remove(x, y, conn_id);
            }
            grid.num_users = (grid.num_users - 1).max(0);
        }
    }

    /// Check if position is legal for walking.
    pub fn is_legal_pos(&self, map: i32, x: i32, y: i32, blocked: bool) -> bool {
        if blocked {
            return false;
        }
        if let Some(grid) = self.grids.get(&map) {
            if !in_map_bounds_for(x, y, grid.width, grid.height) {
                return false;
            }
            grid.is_tile_free(x, y)
        } else {
            false
        }
    }
}

/// Check bounds using a specific grid's dimensions.
pub fn in_map_bounds_grid(grid: &MapGrid, x: i32, y: i32) -> bool {
    in_map_bounds_for(x, y, grid.width, grid.height)
}

/// Check if position is within walkable border limits for any grid size.
pub fn in_map_bounds_for(x: i32, y: i32, width: i32, height: i32) -> bool {
    let min_x = 1 + (X_WINDOW / 2);
    let max_x = width - (X_WINDOW / 2);
    let min_y = 1 + (Y_WINDOW / 2);
    let max_y = height - (Y_WINDOW / 2);
    x >= min_x && x <= max_x && y >= min_y && y <= max_y
}

/// Convert heading to position offset.
pub fn heading_to_offset(heading: i32) -> (i32, i32) {
    match heading {
        HEADING_NORTH => (0, -1),
        HEADING_EAST => (1, 0),
        HEADING_SOUTH => (0, 1),
        HEADING_WEST => (-1, 0),
        _ => (0, 0),
    }
}

/// Get all user connections within viewport range around a point on a grid.
/// Delegates to the grid's zone-based lookup with distance filtering.
pub fn get_users_in_area(grid: &MapGrid, center_x: i32, center_y: i32) -> Vec<ConnectionId> {
    grid.get_nearby_users(center_x, center_y)
}

/// Get all user connections on a specific map.
pub fn get_users_on_map(grid: &MapGrid) -> Vec<ConnectionId> {
    grid.get_all_users()
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn heading_offsets() {
        assert_eq!(heading_to_offset(HEADING_NORTH), (0, -1));
        assert_eq!(heading_to_offset(HEADING_EAST), (1, 0));
        assert_eq!(heading_to_offset(HEADING_SOUTH), (0, 1));
        assert_eq!(heading_to_offset(HEADING_WEST), (-1, 0));
    }

    #[test]
    fn tile_access_1based() {
        let grid = MapGrid::new();
        assert!(grid.tile(1, 1).is_some());
        assert!(grid.tile(100, 100).is_some());
        assert!(grid.tile(0, 0).is_none());
        assert!(grid.tile(101, 1).is_none());
    }

    #[test]
    fn custom_size_grid() {
        let grid = MapGrid::with_size(200, 150, 16);
        assert!(grid.tile(1, 1).is_some());
        assert!(grid.tile(200, 150).is_some());
        assert!(grid.tile(201, 1).is_none());
        assert_eq!(grid.zones_x, 13); // ceil(200/16)
        assert_eq!(grid.zones_y, 10); // ceil(150/16)
    }

    #[test]
    fn place_and_remove_user() {
        let mut world = WorldState::new(1);
        let conn: ConnectionId = 42;
        world.place_user(1, 50, 50, conn);

        assert!(!world.grid(1).unwrap().is_tile_free(50, 50));
        assert!(world.grid(1).unwrap().is_tile_free(50, 51));

        world.remove_user(1, 50, 50);
        assert!(world.grid(1).unwrap().is_tile_free(50, 50));
    }

    #[test]
    fn area_query_zone_based() {
        let mut world = WorldState::new(1);
        let conn1: ConnectionId = 1;
        let conn2: ConnectionId = 2;
        let conn3: ConnectionId = 3;

        world.place_user(1, 50, 50, conn1);
        world.place_user(1, 55, 50, conn2); // Within ±8 X
        world.place_user(1, 1, 1, conn3);   // Far away

        let nearby = get_users_in_area(world.grid(1).unwrap(), 50, 50);
        assert!(nearby.contains(&conn1));
        assert!(nearby.contains(&conn2));
        assert!(!nearby.contains(&conn3));
    }

    #[test]
    fn zone_tracking_consistency() {
        let mut world = WorldState::new(1);
        let conn: ConnectionId = 99;

        // Place and verify zone contains user
        world.place_user(1, 50, 50, conn);
        let all = world.grid(1).unwrap().get_all_users();
        assert!(all.contains(&conn));

        // Remove and verify zone is clean
        world.remove_user(1, 50, 50);
        let all = world.grid(1).unwrap().get_all_users();
        assert!(!all.contains(&conn));
    }

    #[test]
    fn bounds_check_dynamic() {
        assert!(in_map_bounds_for(9, 7, 100, 100));
        assert!(!in_map_bounds_for(8, 7, 100, 100));
        // Larger map
        assert!(in_map_bounds_for(9, 7, 200, 200));
        assert!(in_map_bounds_for(192, 194, 200, 200));
        assert!(!in_map_bounds_for(193, 7, 200, 200));
    }
}
