// World state — runtime map grids, character index management, and area visibility.
//
// Each map has a 100x100 tile grid tracking which user/NPC occupies each tile.
// Area management uses the VB6 9x9 grid-based visibility system.

use std::collections::HashMap;
use crate::net::ConnectionId;

// Map dimensions (VB6 standard)
pub const MAP_WIDTH: usize = 100;
pub const MAP_HEIGHT: usize = 100;

// Client viewport (tiles visible on screen)
pub const X_WINDOW: i32 = 17;
pub const Y_WINDOW: i32 = 13;

// Border offsets (half viewport)
pub const MIN_X_BORDER: i32 = X_WINDOW / 2; // 8
pub const MIN_Y_BORDER: i32 = Y_WINDOW / 2; // 6

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

/// Runtime map grid — tracks positions for collision and area queries.
pub struct MapGrid {
    pub tiles: Box<[[TileRuntime; MAP_WIDTH]; MAP_HEIGHT]>,
    pub num_users: i32,
}

impl MapGrid {
    pub fn new() -> Self {
        Self {
            tiles: Box::new(std::array::from_fn(|_| std::array::from_fn(|_| TileRuntime::default()))),
            num_users: 0,
        }
    }

    /// Get tile at (x, y). Coordinates are 1-based (VB6 style).
    pub fn tile(&self, x: i32, y: i32) -> Option<&TileRuntime> {
        if x >= 1 && x <= MAP_WIDTH as i32 && y >= 1 && y <= MAP_HEIGHT as i32 {
            Some(&self.tiles[(y - 1) as usize][(x - 1) as usize])
        } else {
            None
        }
    }

    /// Get mutable tile at (x, y). Coordinates are 1-based.
    pub fn tile_mut(&mut self, x: i32, y: i32) -> Option<&mut TileRuntime> {
        if x >= 1 && x <= MAP_WIDTH as i32 && y >= 1 && y <= MAP_HEIGHT as i32 {
            Some(&mut self.tiles[(y - 1) as usize][(x - 1) as usize])
        } else {
            None
        }
    }

    /// Check if a tile is free for walking.
    pub fn is_tile_free(&self, x: i32, y: i32) -> bool {
        if let Some(t) = self.tile(x, y) {
            t.user_conn.is_none() && t.npc_index == 0
        } else {
            false
        }
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
        // Pre-create grids for all loaded maps
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

    /// Get grid for a map (creates if needed).
    pub fn grid(&self, map: i32) -> Option<&MapGrid> {
        self.grids.get(&map)
    }

    pub fn grid_mut(&mut self, map: i32) -> &mut MapGrid {
        self.grids.entry(map).or_insert_with(MapGrid::new)
    }

    /// Place a user on a tile.
    pub fn place_user(&mut self, map: i32, x: i32, y: i32, conn_id: ConnectionId) {
        let grid = self.grid_mut(map);
        if let Some(tile) = grid.tile_mut(x, y) {
            tile.user_conn = Some(conn_id);
        }
        grid.num_users += 1;
    }

    /// Remove a user from a tile.
    pub fn remove_user(&mut self, map: i32, x: i32, y: i32) {
        if let Some(grid) = self.grids.get_mut(&map) {
            if let Some(tile) = grid.tile_mut(x, y) {
                tile.user_conn = None;
            }
            grid.num_users = (grid.num_users - 1).max(0);
        }
    }

    /// Check if position is legal for walking.
    /// blocked_tiles comes from the static map data.
    pub fn is_legal_pos(&self, map: i32, x: i32, y: i32, blocked: bool) -> bool {
        if !in_map_bounds(x, y) {
            return false;
        }
        if blocked {
            return false;
        }
        if let Some(grid) = self.grids.get(&map) {
            grid.is_tile_free(x, y)
        } else {
            false
        }
    }
}

/// Check if position is within VB6 map border limits.
/// VB6: MinXBorder = XMinMapSize + (XWindow \ 2) = 1 + 8 = 9
///      MaxXBorder = XMaxMapSize - (XWindow \ 2) = 100 - 8 = 92
///      MinYBorder = YMinMapSize + (YWindow \ 2) = 1 + 6 = 7
///      MaxYBorder = YMaxMapSize - (YWindow \ 2) = 100 - 6 = 94
pub fn in_map_bounds(x: i32, y: i32) -> bool {
    const MIN_X: i32 = 1 + (X_WINDOW / 2); // 9
    const MAX_X: i32 = MAP_WIDTH as i32 - (X_WINDOW / 2); // 92
    const MIN_Y: i32 = 1 + (Y_WINDOW / 2); // 7
    const MAX_Y: i32 = MAP_HEIGHT as i32 - (Y_WINDOW / 2); // 94
    x >= MIN_X && x <= MAX_X && y >= MIN_Y && y <= MAX_Y
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

/// Get all user connections in the area around (center_x, center_y) on a map.
/// Area is the client viewport: ±8 X, ±6 Y.
pub fn get_users_in_area(grid: &MapGrid, center_x: i32, center_y: i32) -> Vec<ConnectionId> {
    let mut users = Vec::new();
    let min_y = (center_y - MIN_Y_BORDER + 1).max(1);
    let max_y = (center_y + MIN_Y_BORDER - 1).min(MAP_HEIGHT as i32);
    let min_x = (center_x - MIN_X_BORDER + 1).max(1);
    let max_x = (center_x + MIN_X_BORDER - 1).min(MAP_WIDTH as i32);

    for y in min_y..=max_y {
        for x in min_x..=max_x {
            if let Some(tile) = grid.tile(x, y) {
                if let Some(conn) = tile.user_conn {
                    users.push(conn);
                }
            }
        }
    }
    users
}

/// Get all user connections on a specific map.
pub fn get_users_on_map(grid: &MapGrid) -> Vec<ConnectionId> {
    let mut users = Vec::new();
    for y in 0..MAP_HEIGHT {
        for x in 0..MAP_WIDTH {
            if let Some(conn) = grid.tiles[y][x].user_conn {
                users.push(conn);
            }
        }
    }
    users
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
        // (1,1) should be valid
        assert!(grid.tile(1, 1).is_some());
        // (100,100) should be valid
        assert!(grid.tile(100, 100).is_some());
        // (0,0) should be invalid (1-based)
        assert!(grid.tile(0, 0).is_none());
        // (101,1) should be invalid
        assert!(grid.tile(101, 1).is_none());
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
    fn area_query() {
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
}
