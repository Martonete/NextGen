// World state — runtime map grids, character index management, and area visibility.
//
// Grids use chunk-based tile storage (100x100 chunks) for memory efficiency.
// Only chunks that are actually written to get allocated. The zone system
// provides O(1) area user lookups for broadcast operations.

use crate::net::ConnectionId;
use std::collections::HashMap;

// Default map dimensions (VB6 standard — used when loading .map files)
pub const MAP_WIDTH: usize = 100;
pub const MAP_HEIGHT: usize = 100;

// Client viewport — extended ~35% past the 1920x1080 worst case (45x23) for a
// wider visibility range (61x31 tiles visible).
pub const X_WINDOW: i32 = 61;
pub const Y_WINDOW: i32 = 31;

// Border offsets (half viewport) — used for area queries and range checks
pub const MIN_X_BORDER: i32 = X_WINDOW / 2; // 30
pub const MIN_Y_BORDER: i32 = Y_WINDOW / 2; // 15

// Extended visibility for objects (doors, ground items, particles, lights).
// Kept at a small margin above MIN_X/Y_BORDER, same ratio as before.
// Characters/NPCs still use MIN_X/Y_BORDER — they fade via client FOV system.
pub const OBJ_X_BORDER: i32 = 31;
pub const OBJ_Y_BORDER: i32 = 16;

// Default zone size for area tracking
const DEFAULT_ZONE_SIZE: i32 = 9;

// Chunk dimensions (tiles per chunk side)
const CHUNK_SIZE: i32 = 100;

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

/// A 100x100 chunk of tiles, allocated on demand.
struct RuntimeChunk {
    tiles: Vec<TileRuntime>, // CHUNK_SIZE * CHUNK_SIZE = 10,000 entries
}

impl RuntimeChunk {
    fn new() -> Self {
        let count = (CHUNK_SIZE * CHUNK_SIZE) as usize;
        Self {
            tiles: (0..count).map(|_| TileRuntime::default()).collect(),
        }
    }
}

/// Convert 1-based tile coords to chunk coordinates.
#[inline]
fn chunk_coords(x: i32, y: i32) -> (i32, i32) {
    ((x - 1) / CHUNK_SIZE, (y - 1) / CHUNK_SIZE)
}

/// Convert 1-based tile coords to a local index within a chunk.
#[inline]
fn local_index(x: i32, y: i32) -> usize {
    let lx = ((x - 1) % CHUNK_SIZE) as usize;
    let ly = ((y - 1) % CHUNK_SIZE) as usize;
    ly * CHUNK_SIZE as usize + lx
}

/// Runtime map grid — chunk-based storage, tracks positions and zone-based user lists.
pub struct MapGrid {
    /// Chunk map: (chunk_x, chunk_y) -> RuntimeChunk. Allocated on demand.
    chunks: HashMap<(i32, i32), RuntimeChunk>,
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
    /// Chunks are NOT pre-allocated — they are created on demand when tiles are written.
    pub fn with_size(width: i32, height: i32, zone_size: i32) -> Self {
        let zones_x = (width + zone_size - 1) / zone_size;
        let zones_y = (height + zone_size - 1) / zone_size;
        let total_zones = (zones_x * zones_y) as usize;
        Self {
            chunks: HashMap::new(),
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
    /// Pre-allocates the single (0,0) chunk for legacy compatibility.
    pub fn new() -> Self {
        let mut grid = Self::with_size(MAP_WIDTH as i32, MAP_HEIGHT as i32, DEFAULT_ZONE_SIZE);
        // Pre-create the single chunk so all 100x100 tiles are immediately readable
        grid.chunks.insert((0, 0), RuntimeChunk::new());
        grid
    }

    /// Ensure the chunk containing (x, y) exists. Creates it if missing.
    pub fn ensure_chunk(&mut self, x: i32, y: i32) {
        let (cx, cy) = chunk_coords(x, y);
        self.chunks
            .entry((cx, cy))
            .or_insert_with(RuntimeChunk::new);
    }

    /// Return the number of currently loaded chunks.
    pub fn loaded_chunk_count(&self) -> usize {
        self.chunks.len()
    }

    /// Convert 1-based tile coords to zone index.
    #[inline]
    fn zone_idx(&self, x: i32, y: i32) -> usize {
        let zx = ((x - 1) / self.zone_size).min(self.zones_x - 1);
        let zy = ((y - 1) / self.zone_size).min(self.zones_y - 1);
        (zy * self.zones_x + zx) as usize
    }

    /// Get tile at (x, y). Coordinates are 1-based.
    /// Returns None if out of bounds or if the chunk hasn't been allocated yet.
    pub fn tile(&self, x: i32, y: i32) -> Option<&TileRuntime> {
        if x < 1 || x > self.width || y < 1 || y > self.height {
            return None;
        }
        let (cx, cy) = chunk_coords(x, y);
        self.chunks
            .get(&(cx, cy))
            .map(|chunk| &chunk.tiles[local_index(x, y)])
    }

    /// Get mutable tile at (x, y). Coordinates are 1-based.
    /// Auto-creates the chunk if it doesn't exist (callers expect to always be able to write).
    /// Returns None only if out of bounds.
    pub fn tile_mut(&mut self, x: i32, y: i32) -> Option<&mut TileRuntime> {
        if x < 1 || x > self.width || y < 1 || y > self.height {
            return None;
        }
        let (cx, cy) = chunk_coords(x, y);
        let chunk = self
            .chunks
            .entry((cx, cy))
            .or_insert_with(RuntimeChunk::new);
        let idx = local_index(x, y);
        Some(&mut chunk.tiles[idx])
    }

    /// Check if a tile is free for walking.
    /// If the chunk isn't loaded yet, the tile is considered free (no user/NPC on it).
    /// Out-of-bounds coordinates return false.
    pub fn is_tile_free(&self, x: i32, y: i32) -> bool {
        if x < 1 || x > self.width || y < 1 || y > self.height {
            return false;
        }
        let (cx, cy) = chunk_coords(x, y);
        match self.chunks.get(&(cx, cy)) {
            Some(chunk) => {
                let t = &chunk.tiles[local_index(x, y)];
                t.user_conn.is_none() && t.npc_index == 0
            }
            None => true, // Chunk not loaded = no user/NPC = free
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
        self.zone_users
            .iter()
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

    /// Character indices released by disconnected users or non-respawning NPCs.
    free_char_indices: Vec<i32>,
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
            free_char_indices: Vec::new(),
        }
    }

    /// Allocate a new unique character index.
    pub fn alloc_char_index(&mut self) -> CharIndex {
        if let Some(idx) = self.free_char_indices.pop() {
            return CharIndex(idx);
        }

        let idx = CharIndex(self.next_char_index);
        self.next_char_index += 1;
        idx
    }

    /// Return a character index to the reusable pool.
    pub fn free_char_index(&mut self, char_index: CharIndex) {
        if char_index.0 > 0 && char_index.0 < self.next_char_index {
            self.free_char_indices.push(char_index.0);
        }
    }

    /// Ensure a grid exists for a reloaded map.
    pub fn reload_map(&mut self, map_num: usize, _maps: &[Option<crate::data::maps::GameMap>]) {
        self.grids
            .entry(map_num as i32)
            .or_insert_with(MapGrid::new);
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
    x >= 1 && x <= width && y >= 1 && y <= height
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
        // with_size does NOT pre-allocate chunks, so tile() returns None
        // for unallocated chunks. Use tile_mut to auto-allocate.
        assert!(grid.tile(1, 1).is_none()); // chunk not allocated yet
        assert!(grid.tile(201, 1).is_none()); // out of bounds

        // Verify zone dimensions
        assert_eq!(grid.zones_x, 13); // ceil(200/16)
        assert_eq!(grid.zones_y, 10); // ceil(150/16)

        // tile_mut auto-allocates chunks
        let mut grid = grid;
        assert!(grid.tile_mut(1, 1).is_some());
        assert!(grid.tile(1, 1).is_some()); // now readable
        assert!(grid.tile_mut(200, 150).is_some());
        assert!(grid.tile(200, 150).is_some());
        assert!(grid.tile_mut(201, 1).is_none()); // still out of bounds
    }

    #[test]
    fn chunk_lazy_allocation() {
        let mut grid = MapGrid::with_size(300, 300, 9);
        assert_eq!(grid.loaded_chunk_count(), 0);

        // Writing to (1,1) creates chunk (0,0)
        grid.tile_mut(1, 1);
        assert_eq!(grid.loaded_chunk_count(), 1);

        // Writing to (101,1) creates chunk (1,0)
        grid.tile_mut(101, 1);
        assert_eq!(grid.loaded_chunk_count(), 2);

        // Writing to (50,50) reuses chunk (0,0) — no new chunk
        grid.tile_mut(50, 50);
        assert_eq!(grid.loaded_chunk_count(), 2);

        // Reading from unallocated chunk returns None
        assert!(grid.tile(201, 201).is_none());
        assert_eq!(grid.loaded_chunk_count(), 2);
    }

    #[test]
    fn ensure_chunk_creates_on_demand() {
        let mut grid = MapGrid::with_size(500, 500, 9);
        assert_eq!(grid.loaded_chunk_count(), 0);

        grid.ensure_chunk(250, 250);
        assert_eq!(grid.loaded_chunk_count(), 1);

        // Now readable
        assert!(grid.tile(250, 250).is_some());
    }

    #[test]
    fn new_grid_has_one_chunk() {
        let grid = MapGrid::new();
        assert_eq!(grid.loaded_chunk_count(), 1);
        // All 100x100 tiles accessible
        assert!(grid.tile(1, 1).is_some());
        assert!(grid.tile(100, 100).is_some());
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
        world.place_user(1, 1, 1, conn3); // Far away

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
        // min_x = 1 + X_WINDOW/2 = 1 + 22 = 23
        // min_y = 1 + Y_WINDOW/2 = 1 + 11 = 12
        assert!(in_map_bounds_for(23, 12, 100, 100));
        assert!(!in_map_bounds_for(22, 12, 100, 100));
        // Larger map
        assert!(in_map_bounds_for(23, 12, 200, 200));
        assert!(in_map_bounds_for(178, 189, 200, 200));
        assert!(!in_map_bounds_for(179, 12, 200, 200));
        // 1000x1000 map — full range minus border
        assert!(in_map_bounds_for(23, 12, 1000, 1000));
        assert!(in_map_bounds_for(978, 989, 1000, 1000));
        assert!(!in_map_bounds_for(979, 12, 1000, 1000));
    }
}
