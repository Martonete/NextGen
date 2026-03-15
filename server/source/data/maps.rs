// Map loader — maps/MapaN.map (binary), MapaN.inf (binary), MapaN.dat (INI)
//
// Binary .map format (legacy):
//   Header: 273 bytes (MapVersion(2) + Desc(255) + CRC(4) + MagicWord(4) + Reserved(8))
//   Tiles: 100x100 grid, variable-length per tile using ByFlags bitfield.
//
// Binary .aomap format (extended):
//   Header: 16 bytes (Magic(6) + Version(2) + Width(2) + Height(2) + Flags(4))
//   Tiles: Width × Height grid, same ByFlags encoding as .map
//
// Binary .inf format:
//   Header: 10 bytes (5 reserved Integers)
//   Tiles: 100x100 grid, variable-length. Contains exits, NPCs, objects.
//
// INI .dat format:
//   [MAPAN] section with Name, MusicNum, Pk, Terreno, Zona, etc.
//
// All integers are little-endian (VB6 standard).

use std::io::{self, Read, Cursor};
use std::path::Path;
use crate::config::IniFile;

pub const MAP_WIDTH: usize = 100;
pub const MAP_HEIGHT: usize = 100;
pub const MAX_MAPS: usize = 200; // Buffer above the 180-193 actual maps

/// Trigger type for map tiles.
#[derive(Debug, Clone, Copy, PartialEq, Eq, Default)]
#[repr(u8)]
pub enum Trigger {
    #[default]
    None = 0,
    Indoor = 1,         // BAJOTECHO — under roof
    Reserved = 2,
    InvalidPos = 3,     // POSINVALIDA — NPCs can't walk
    SafeZone = 4,       // ZONASEGURA — no theft/combat
    AntiBlock = 5,      // ANTIPIQUETE — anti-picketing
    CombatZone = 6,     // ZONAPELEA — items don't drop on death
    NoElevation = 7,    // SINELE
}

impl Trigger {
    fn from_i16(v: i16) -> Self {
        match v {
            1 => Self::Indoor,
            2 => Self::Reserved,
            3 => Self::InvalidPos,
            4 => Self::SafeZone,
            5 => Self::AntiBlock,
            6 => Self::CombatZone,
            7 => Self::NoElevation,
            _ => Self::None,
        }
    }
}

/// World position (map + x,y coordinates).
#[derive(Debug, Clone, Copy, Default)]
pub struct WorldPos {
    pub map: i16,
    pub x: i16,
    pub y: i16,
}

/// Object on a map tile.
#[derive(Debug, Clone, Copy, Default)]
pub struct TileObj {
    pub obj_index: i16,
    pub amount: i16,
}

/// A single map tile.
#[derive(Debug, Clone, Default)]
pub struct MapTile {
    // From .map file
    pub blocked: bool,
    pub graphic: [i16; 4],          // 4 graphic layers
    pub trigger: Trigger,
    pub particle_group_index: i16,
    pub range_light: i16,
    pub rgb_light: [i16; 3],        // R, G, B

    // From .inf file
    pub tile_exit: Option<WorldPos>,
    pub npc_index: i16,
    pub obj: TileObj,

    // Runtime state (not loaded from file)
    pub user_index: i16,
    /// Original blocked state from .map file — used to detect door changes on map entry.
    pub original_blocked: bool,
    /// Original object index from .inf file — used to detect door object changes on map entry.
    pub original_obj_index: i16,
}

/// Dynamic-size tile grid. Supports variable map dimensions (legacy 100x100 and extended .aomap).
pub struct MapTiles {
    data: Vec<MapTile>,
    pub width: usize,
    pub height: usize,
}

impl MapTiles {
    pub fn new(width: usize, height: usize) -> Self {
        Self {
            data: vec![MapTile::default(); width * height],
            width,
            height,
        }
    }

    pub fn get(&self, x: usize, y: usize) -> Option<&MapTile> {
        if x < self.width && y < self.height {
            Some(&self.data[y * self.width + x])
        } else {
            None
        }
    }

    pub fn get_mut(&mut self, x: usize, y: usize) -> Option<&mut MapTile> {
        if x < self.width && y < self.height {
            Some(&mut self.data[y * self.width + x])
        } else {
            None
        }
    }
}

/// Map metadata from .dat INI file.
#[derive(Debug, Clone)]
pub struct MapInfo {
    pub num: usize,
    pub name: String,
    pub music: i32,
    pub pk: bool,
    pub magia_sin_efecto: bool,
    pub invi_sin_efecto: bool,
    pub resu_sin_efecto: bool,
    pub ocultar_sin_efecto: bool,
    pub invocar_sin_efecto: bool,
    pub no_encriptar_mp: bool,
    pub terreno: String,
    pub zona: String,
    pub restringir: String,
    pub backup: bool,
    pub r: i32,
    pub g: i32,
    pub b: i32,

    // Runtime state
    pub num_users: i32,
}

impl Default for MapInfo {
    fn default() -> Self {
        Self {
            num: 0,
            name: String::new(),
            music: 0,
            pk: false,
            magia_sin_efecto: false,
            invi_sin_efecto: false,
            resu_sin_efecto: false,
            ocultar_sin_efecto: false,
            invocar_sin_efecto: false,
            no_encriptar_mp: false,
            terreno: String::new(),
            zona: String::new(),
            restringir: "NO".into(),
            backup: false,
            r: 200, g: 200, b: 200,
            num_users: 0,
        }
    }
}

/// A complete loaded map.
pub struct GameMap {
    pub info: MapInfo,
    pub tiles: MapTiles,
}

/// Helper to read a little-endian i16 from a cursor.
fn read_i16(cursor: &mut Cursor<&[u8]>) -> io::Result<i16> {
    let mut buf = [0u8; 2];
    cursor.read_exact(&mut buf)?;
    Ok(i16::from_le_bytes(buf))
}

/// Helper to read a little-endian u16 from a cursor.
fn read_u16(cursor: &mut Cursor<&[u8]>) -> io::Result<u16> {
    let mut buf = [0u8; 2];
    cursor.read_exact(&mut buf)?;
    Ok(u16::from_le_bytes(buf))
}

/// Helper to read a little-endian i32 from a cursor.
fn read_i32(cursor: &mut Cursor<&[u8]>) -> io::Result<i32> {
    let mut buf = [0u8; 4];
    cursor.read_exact(&mut buf)?;
    Ok(i32::from_le_bytes(buf))
}

/// Helper to read a single byte.
fn read_u8(cursor: &mut Cursor<&[u8]>) -> io::Result<u8> {
    let mut buf = [0u8; 1];
    cursor.read_exact(&mut buf)?;
    Ok(buf[0])
}

/// Read tiles from cursor using the ByFlags encoding shared by .map and .aomap formats.
fn read_map_tiles(cursor: &mut Cursor<&[u8]>, tiles: &mut MapTiles) -> Result<(), String> {
    for y in 0..tiles.height {
        for x in 0..tiles.width {
            let by_flags = read_u8(cursor)
                .map_err(|e| format!("Failed to read tile ({},{}) flags: {}", x + 1, y + 1, e))?;

            let tile = tiles.get_mut(x, y)
                .ok_or_else(|| format!("Tile ({},{}) out of bounds", x + 1, y + 1))?;

            // Bit 0: Blocked
            tile.blocked = (by_flags & 0x01) != 0;

            // Graphic[1] always present
            tile.graphic[0] = read_i16(cursor)
                .map_err(|e| format!("Tile ({},{}) graphic[1]: {}", x + 1, y + 1, e))?;

            // Bit 1: Graphic[2]
            if (by_flags & 0x02) != 0 {
                tile.graphic[1] = read_i16(cursor)
                    .map_err(|e| format!("Tile ({},{}) graphic[2]: {}", x + 1, y + 1, e))?;
            }

            // Bit 2: Graphic[3]
            if (by_flags & 0x04) != 0 {
                tile.graphic[2] = read_i16(cursor)
                    .map_err(|e| format!("Tile ({},{}) graphic[3]: {}", x + 1, y + 1, e))?;
            }

            // Bit 3: Graphic[4]
            if (by_flags & 0x08) != 0 {
                tile.graphic[3] = read_i16(cursor)
                    .map_err(|e| format!("Tile ({},{}) graphic[4]: {}", x + 1, y + 1, e))?;
            }

            // Bit 4: Trigger
            if (by_flags & 0x10) != 0 {
                let trigger_val = read_i16(cursor)
                    .map_err(|e| format!("Tile ({},{}) trigger: {}", x + 1, y + 1, e))?;
                tile.trigger = Trigger::from_i16(trigger_val);
            }

            // Bit 5: Particle group index
            if (by_flags & 0x20) != 0 {
                tile.particle_group_index = read_i16(cursor)
                    .map_err(|e| format!("Tile ({},{}) particle: {}", x + 1, y + 1, e))?;
            }

            // Bit 6: Range light + RGB (8 bytes total)
            if (by_flags & 0x40) != 0 {
                tile.range_light = read_i16(cursor)
                    .map_err(|e| format!("Tile ({},{}) range_light: {}", x + 1, y + 1, e))?;
                tile.rgb_light[0] = read_i16(cursor)
                    .map_err(|e| format!("Tile ({},{}) rgb_r: {}", x + 1, y + 1, e))?;
                tile.rgb_light[1] = read_i16(cursor)
                    .map_err(|e| format!("Tile ({},{}) rgb_g: {}", x + 1, y + 1, e))?;
                tile.rgb_light[2] = read_i16(cursor)
                    .map_err(|e| format!("Tile ({},{}) rgb_b: {}", x + 1, y + 1, e))?;
            }
        }
    }

    Ok(())
}

/// Load legacy .map binary file (tile graphics and properties).
/// Header: 273 bytes, fixed 100x100 grid.
fn load_map_file(path: &Path, tiles: &mut MapTiles) -> Result<(), String> {
    let data = std::fs::read(path)
        .map_err(|e| format!("Failed to read .map: {}", e))?;

    let mut cursor = Cursor::new(data.as_slice());

    // Skip header: 273 bytes
    // MapVersion(2) + Desc(255) + CRC(4) + MagicWord(4) + Reserved(8)
    let mut header = vec![0u8; 273];
    cursor.read_exact(&mut header)
        .map_err(|e| format!("Failed to read .map header: {}", e))?;

    read_map_tiles(&mut cursor, tiles)
}

/// Load .aomap binary file (extended format with dimensions in header).
/// Header (16 bytes):
///   Magic: "AOMAP\0" (6 bytes)
///   Version: u16 LE (1)
///   Width: u16 LE
///   Height: u16 LE
///   Flags: u32 LE (reserved)
///   Padding: 2 bytes (reserved)
/// Tiles: Width x Height, same ByFlags encoding as legacy .map
fn load_aomap_file(path: &Path) -> Result<MapTiles, String> {
    let data = std::fs::read(path)
        .map_err(|e| format!("Failed to read .aomap: {}", e))?;

    if data.len() < 16 {
        return Err("Invalid .aomap file: too short for header".into());
    }

    let mut cursor = Cursor::new(data.as_slice());

    // Read and validate magic: "AOMAP\0" (6 bytes)
    let mut magic = [0u8; 6];
    cursor.read_exact(&mut magic)
        .map_err(|e| format!("Failed to read .aomap magic: {}", e))?;
    if &magic != b"AOMAP\0" {
        return Err(format!("Invalid .aomap magic: expected AOMAP\\0, got {:?}", magic));
    }

    // Read version (must be 1)
    let version = read_u16(&mut cursor)
        .map_err(|e| format!("Failed to read .aomap version: {}", e))?;
    if version != 1 {
        return Err(format!("Unsupported .aomap version: {} (expected 1)", version));
    }

    // Read dimensions
    let width = read_u16(&mut cursor)
        .map_err(|e| format!("Failed to read .aomap width: {}", e))? as usize;
    let height = read_u16(&mut cursor)
        .map_err(|e| format!("Failed to read .aomap height: {}", e))? as usize;

    if width == 0 || height == 0 {
        return Err(format!("Invalid .aomap dimensions: {}x{}", width, height));
    }
    if width > 10000 || height > 10000 {
        return Err(format!("Unreasonable .aomap dimensions: {}x{}", width, height));
    }

    // Read flags (reserved, ignored for now)
    let _flags = read_i32(&mut cursor)
        .map_err(|e| format!("Failed to read .aomap flags: {}", e))?;

    // Read padding (2 bytes reserved)
    let mut _padding = [0u8; 2];
    cursor.read_exact(&mut _padding)
        .map_err(|e| format!("Failed to read .aomap padding: {}", e))?;

    // Create tiles and read using shared ByFlags encoding
    let mut tiles = MapTiles::new(width, height);
    read_map_tiles(&mut cursor, &mut tiles)?;

    Ok(tiles)
}

/// Load .inf binary file (exits, NPCs, objects on tiles).
fn load_inf_file(path: &Path, tiles: &mut MapTiles) -> Result<(), String> {
    let data = std::fs::read(path)
        .map_err(|e| format!("Failed to read .inf: {}", e))?;

    let mut cursor = Cursor::new(data.as_slice());

    // Skip header: 10 bytes (5 reserved Integers)
    let mut header = vec![0u8; 10];
    cursor.read_exact(&mut header)
        .map_err(|e| format!("Failed to read .inf header: {}", e))?;

    // Read tiles in same order as .map
    for y in 0..tiles.height {
        for x in 0..tiles.width {
            let by_flags = read_u8(&mut cursor)
                .map_err(|e| format!("Failed to read inf tile ({},{}) flags: {}", x + 1, y + 1, e))?;

            let tile = tiles.get_mut(x, y)
                .ok_or_else(|| format!("Inf tile ({},{}) out of bounds", x + 1, y + 1))?;

            // Bit 0: TileExit (6 bytes: Map, X, Y)
            if (by_flags & 0x01) != 0 {
                let map = read_i16(&mut cursor)
                    .map_err(|e| format!("Inf tile ({},{}) exit map: {}", x + 1, y + 1, e))?;
                let ex = read_i16(&mut cursor)
                    .map_err(|e| format!("Inf tile ({},{}) exit x: {}", x + 1, y + 1, e))?;
                let ey = read_i16(&mut cursor)
                    .map_err(|e| format!("Inf tile ({},{}) exit y: {}", x + 1, y + 1, e))?;
                tile.tile_exit = Some(WorldPos { map, x: ex, y: ey });
            }

            // Bit 1: NPC index (2 bytes)
            if (by_flags & 0x02) != 0 {
                tile.npc_index = read_i16(&mut cursor)
                    .map_err(|e| format!("Inf tile ({},{}) npc: {}", x + 1, y + 1, e))?;
            }

            // Bit 2: Object (4 bytes: ObjIndex, Amount)
            if (by_flags & 0x04) != 0 {
                tile.obj.obj_index = read_i16(&mut cursor)
                    .map_err(|e| format!("Inf tile ({},{}) obj index: {}", x + 1, y + 1, e))?;
                tile.obj.amount = read_i16(&mut cursor)
                    .map_err(|e| format!("Inf tile ({},{}) obj amount: {}", x + 1, y + 1, e))?;
            }
        }
    }

    Ok(())
}

/// Load .aoinf binary file (extended format with dimensions in header).
/// Same header format as .aomap: Magic("AOINF\0") + version + width + height + flags.
fn load_aoinf_file(path: &Path, tiles: &mut MapTiles) -> Result<(), String> {
    let data = std::fs::read(path)
        .map_err(|e| format!("Failed to read .aoinf: {}", e))?;

    if data.len() < 16 {
        return Err("Invalid .aoinf file: too short for header".into());
    }

    let mut cursor = Cursor::new(data.as_slice());

    // Read and validate header
    let mut magic = [0u8; 6];
    cursor.read_exact(&mut magic).map_err(|e| format!(".aoinf magic: {}", e))?;
    if &magic != b"AOINF\0" {
        return Err("Invalid .aoinf file: bad magic".into());
    }
    let _version = read_u16(&mut cursor).map_err(|e| format!(".aoinf version: {}", e))?;
    let width = read_u16(&mut cursor).map_err(|e| format!(".aoinf width: {}", e))? as usize;
    let height = read_u16(&mut cursor).map_err(|e| format!(".aoinf height: {}", e))? as usize;
    let _flags = read_i32(&mut cursor).map_err(|e| format!(".aoinf flags: {}", e))?;

    if width != tiles.width || height != tiles.height {
        return Err(format!(".aoinf dimensions {}x{} don't match map {}x{}", width, height, tiles.width, tiles.height));
    }

    // Read tiles (same format as legacy .inf but using the extended dimensions)
    for y in 0..height {
        for x in 0..width {
            let by_flags = read_u8(&mut cursor)
                .map_err(|e| format!("aoinf tile ({},{}) flags: {}", x + 1, y + 1, e))?;

            let tile = tiles.get_mut(x, y)
                .ok_or_else(|| format!("aoinf tile ({},{}) out of bounds", x + 1, y + 1))?;

            if (by_flags & 0x01) != 0 {
                let map = read_i16(&mut cursor).map_err(|e| format!("aoinf exit: {}", e))?;
                let ex = read_i16(&mut cursor).map_err(|e| format!("aoinf exit x: {}", e))?;
                let ey = read_i16(&mut cursor).map_err(|e| format!("aoinf exit y: {}", e))?;
                tile.tile_exit = Some(WorldPos { map, x: ex, y: ey });
            }
            if (by_flags & 0x02) != 0 {
                tile.npc_index = read_i16(&mut cursor).map_err(|e| format!("aoinf npc: {}", e))?;
            }
            if (by_flags & 0x04) != 0 {
                tile.obj.obj_index = read_i16(&mut cursor).map_err(|e| format!("aoinf obj: {}", e))?;
                tile.obj.amount = read_i16(&mut cursor).map_err(|e| format!("aoinf amt: {}", e))?;
            }
        }
    }

    Ok(())
}

/// Load .dat metadata INI file for a map.
fn load_map_dat(path: &Path, map_num: usize) -> MapInfo {
    let ini = match IniFile::load(path) {
        Ok(i) => i,
        Err(_) => return MapInfo { num: map_num, ..Default::default() },
    };

    let section = format!("Mapa{}", map_num);

    let get_str = |key: &str| -> String {
        ini.get(&section, key).unwrap_or_default()
    };
    let get_int = |key: &str| -> i32 {
        ini.get(&section, key).and_then(|s| s.parse().ok()).unwrap_or(0)
    };
    let get_bool = |key: &str| -> bool {
        ini.get(&section, key).map(|s| s == "1").unwrap_or(false)
    };

    MapInfo {
        num: map_num,
        name: get_str("Name"),
        music: get_int("MusicNum"),
        pk: !get_bool("Pk"), // VB6 inverts: file Pk=0 → MapInfo.Pk=True (PvP), Pk=1 → False (safe)
        magia_sin_efecto: get_bool("MagiaSinefecto"),
        invi_sin_efecto: get_bool("InviSinEfecto"),
        resu_sin_efecto: get_bool("ResuSinEfecto"),
        ocultar_sin_efecto: get_bool("OcultarSinEfecto"),
        invocar_sin_efecto: get_bool("InvocarSinEfecto"),
        no_encriptar_mp: get_bool("NoEncriptarMP"),
        terreno: get_str("Terreno"),
        zona: get_str("Zona"),
        restringir: {
            let r = get_str("Restringir");
            if r.is_empty() { "NO".into() } else { r }
        },
        backup: get_bool("BackUp"),
        r: { let v = get_int("R"); if v == 0 { 200 } else { v } },
        g: { let v = get_int("G"); if v == 0 { 200 } else { v } },
        b: { let v = get_int("B"); if v == 0 { 200 } else { v } },
        num_users: 0,
    }
}

/// Resolve a map file path with case-insensitive extension.
/// Tries .ext, .Ext, .EXT variants since Linux is case-sensitive.
fn resolve_map_path(maps_dir: &Path, name: &str, extensions: &[&str]) -> std::path::PathBuf {
    for ext in extensions {
        let p = maps_dir.join(format!("{}.{}", name, ext));
        if p.exists() { return p; }
    }
    // Fallback to first extension
    maps_dir.join(format!("{}.{}", name, extensions[0]))
}

/// Load a single map (all 3 files: .map/.aomap, .inf, .dat).
pub fn load_map(base: &Path, map_num: usize) -> Result<GameMap, String> {
    let maps_dir = base.join("maps");
    let name = format!("Mapa{}", map_num);

    // Check for .aomap first (extended format with variable dimensions)
    let aomap_file = maps_dir.join(format!("{}.aomap", name));
    let mut tiles = if aomap_file.exists() {
        load_aomap_file(&aomap_file)?
    } else {
        // Fallback to legacy .map format (fixed 100x100)
        let map_file = resolve_map_path(&maps_dir, &name, &["map", "Map", "MAP"]);
        let mut tiles = MapTiles::new(MAP_WIDTH, MAP_HEIGHT);
        load_map_file(&map_file, &mut tiles)?;
        tiles
    };

    // Load .aoinf first (extended format), fallback to legacy .inf
    let aoinf_file = maps_dir.join(format!("{}.aoinf", name));
    if aoinf_file.exists() {
        load_aoinf_file(&aoinf_file, &mut tiles)?;
    } else {
        let inf_file = resolve_map_path(&maps_dir, &name, &["inf", "Inf", "INF"]);
        load_inf_file(&inf_file, &mut tiles)?;
    }

    // Snapshot original state for door persistence detection
    for y in 0..tiles.height {
        for x in 0..tiles.width {
            if let Some(tile) = tiles.get_mut(x, y) {
                tile.original_blocked = tile.blocked;
                tile.original_obj_index = tile.obj.obj_index;
            }
        }
    }

    // Load metadata
    let dat_file = resolve_map_path(&maps_dir, &name, &["dat", "dat", "DAT"]);
    let info = load_map_dat(&dat_file, map_num);

    Ok(GameMap { info, tiles })
}

/// Load all maps from the maps/ directory.
/// Returns a Vec where index 0 is unused (maps are 1-indexed).
pub fn load_all_maps(base: &Path) -> Result<Vec<Option<GameMap>>, String> {
    // Read Map.dat for total count
    let map_dat_path = base.join("dat").join("Map.dat");
    let num_maps = if let Ok(ini) = IniFile::load(&map_dat_path) {
        ini.get("INIT", "NumMaps")
            .and_then(|s| s.parse::<usize>().ok())
            .unwrap_or(193)
    } else {
        193
    };

    let mut maps: Vec<Option<GameMap>> = Vec::with_capacity(num_maps + 1);
    maps.push(None); // Index 0 unused

    let mut loaded = 0;
    let mut failed = 0;

    for i in 1..=num_maps {
        let maps_dir = base.join("maps");
        let name = format!("Mapa{}", i);

        // Check for either .aomap or legacy .map
        let aomap_file = maps_dir.join(format!("{}.aomap", name));
        let map_file = resolve_map_path(&maps_dir, &name, &["map", "Map", "MAP"]);
        if !aomap_file.exists() && !map_file.exists() {
            maps.push(None);
            continue;
        }

        match load_map(base, i) {
            Ok(map) => {
                maps.push(Some(map));
                loaded += 1;
            }
            Err(e) => {
                tracing::warn!("Failed to load map {}: {}", i, e);
                maps.push(None);
                failed += 1;
            }
        }
    }

    // Count tile exits across all loaded maps for diagnostics
    let mut total_exits = 0;
    for map_opt in &maps {
        if let Some(gm) = map_opt {
            for y in 0..gm.tiles.height {
                for x in 0..gm.tiles.width {
                    if let Some(tile) = gm.tiles.get(x, y) {
                        if tile.tile_exit.is_some() {
                            total_exits += 1;
                        }
                    }
                }
            }
        }
    }
    tracing::info!("Maps loaded: {} OK, {} failed, {} total slots, {} tile exits found", loaded, failed, num_maps, total_exits);
    Ok(maps)
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn load_single_map() {
        let base = Path::new(env!("CARGO_MANIFEST_DIR")).join("server");
        let base = base.as_path();
        let map_file = base.join("maps").join("Mapa1.map");
        if !map_file.exists() {
            return;
        }

        let map = load_map(base, 1).unwrap();
        assert_eq!(map.info.num, 1);
        assert!(!map.info.name.is_empty(), "Map 1 should have a name");

        // Check that some tiles have graphics
        let mut has_graphic = false;
        for y in 0..map.tiles.height {
            for x in 0..map.tiles.width {
                if let Some(tile) = map.tiles.get(x, y) {
                    if tile.graphic[0] != 0 {
                        has_graphic = true;
                        break;
                    }
                }
            }
            if has_graphic { break; }
        }
        assert!(has_graphic, "Map 1 should have at least one tile with graphics");
    }

    #[test]
    fn load_map_metadata() {
        let base = Path::new(env!("CARGO_MANIFEST_DIR")).join("server");
        let base = base.as_path();
        let dat_file = base.join("maps").join("Mapa1.dat");
        if !dat_file.exists() {
            return;
        }

        let info = load_map_dat(&dat_file, 1);
        assert_eq!(info.num, 1);
        assert!(!info.name.is_empty());
        assert!(info.r > 0, "RGB should have default values");
    }

    #[test]
    fn map_tiles_basic_operations() {
        let mut tiles = MapTiles::new(10, 5);
        assert_eq!(tiles.width, 10);
        assert_eq!(tiles.height, 5);

        // Valid access
        assert!(tiles.get(0, 0).is_some());
        assert!(tiles.get(9, 4).is_some());

        // Out of bounds
        assert!(tiles.get(10, 0).is_none());
        assert!(tiles.get(0, 5).is_none());

        // Mutable access
        if let Some(tile) = tiles.get_mut(3, 2) {
            tile.blocked = true;
        }
        assert!(tiles.get(3, 2).unwrap().blocked);
        assert!(!tiles.get(3, 1).unwrap().blocked);
    }
}
