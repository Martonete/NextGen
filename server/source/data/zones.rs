//! Zone system — rectangular areas within maps with properties and NPC spawns.
//!
//! File format: `.aozone` (INI) per map, stored alongside `.aomap`/`.dat` files.
//! Loaded at server startup after maps. Pre-caches zone_id per tile for O(1) lookup.

use std::fs;
use std::path::Path;

/// Zone type classification.
#[derive(Debug, Clone, Copy, PartialEq, Eq, Default)]
pub enum ZoneType {
    #[default]
    Neutral = 0,
    Safe = 1,
    PvP = 2,
    Dungeon = 3,
    Arena = 4,
}

impl ZoneType {
    pub fn from_i32(v: i32) -> Self {
        match v {
            1 => Self::Safe,
            2 => Self::PvP,
            3 => Self::Dungeon,
            4 => Self::Arena,
            _ => Self::Neutral,
        }
    }
}

/// A rectangular zone within a map.
#[derive(Debug, Clone)]
pub struct ZoneData {
    pub id: u16,
    pub name: String,
    pub zone_type: ZoneType,

    // Bounds (1-based tile coordinates, inclusive)
    pub x1: i32,
    pub y1: i32,
    pub x2: i32,
    pub y2: i32,

    // Safety / restrictions
    pub segura: bool,
    pub newbie: bool,
    pub sin_magia: bool,
    pub sin_invi: bool,
    pub sin_mascotas: bool,
    pub sin_resucitar: bool,
    pub sin_ocultar: bool,
    pub sin_invocar: bool,
    pub solo_clanes: bool,
    pub solo_faccion: bool,
    pub faccion: i32,      // 0=any, 1=real, 2=caos
    pub combat_zone: bool, // ring: items don't drop on death

    // Level restriction (0 = no limit)
    pub min_level: i32,
    pub max_level: i32,

    // Ambient overrides (0 = use map default)
    pub musica: i32,
    pub lluvia: bool,
    pub nieve: bool,
    pub niebla: bool,
    pub ambient_r: i32,
    pub ambient_g: i32,
    pub ambient_b: i32,
    pub terreno: String,

    // Exit point (death/expulsion). Map=0 means use default.
    pub salida_map: i32,
    pub salida_x: i32,
    pub salida_y: i32,
}

impl Default for ZoneData {
    fn default() -> Self {
        Self {
            id: 0,
            name: String::new(),
            zone_type: ZoneType::Neutral,
            x1: 0,
            y1: 0,
            x2: 0,
            y2: 0,
            segura: false,
            newbie: false,
            sin_magia: false,
            sin_invi: false,
            sin_mascotas: false,
            sin_resucitar: false,
            sin_ocultar: false,
            sin_invocar: false,
            solo_clanes: false,
            solo_faccion: false,
            faccion: 0,
            combat_zone: false,
            min_level: 0,
            max_level: 0,
            musica: 0,
            lluvia: false,
            nieve: false,
            niebla: false,
            ambient_r: 0,
            ambient_g: 0,
            ambient_b: 0,
            terreno: String::new(),
            salida_map: 0,
            salida_x: 0,
            salida_y: 0,
        }
    }
}

impl ZoneData {
    /// Check if a tile coordinate is inside this zone's bounds (inclusive).
    pub fn contains(&self, x: i32, y: i32) -> bool {
        x >= self.x1 && x <= self.x2 && y >= self.y1 && y <= self.y2
    }
}

/// NPC spawn mode.
#[derive(Debug, Clone, Copy, PartialEq, Eq, Default)]
pub enum SpawnMode {
    #[default]
    Fixed = 0,
    Random = 1,
}

/// An NPC spawn definition within a zone.
#[derive(Debug, Clone, Default)]
pub struct NpcSpawnData {
    pub zone_id: u16,
    pub npc_index: i32,
    pub cantidad: i32,
    pub spawn_mode: SpawnMode,
    pub spawn_x: i32, // only for Fixed mode
    pub spawn_y: i32,
    pub respawn_time: i32, // seconds
}

/// All zone data for a single map.
#[derive(Debug, Clone, Default)]
pub struct MapZones {
    pub zones: Vec<ZoneData>,
    pub spawns: Vec<NpcSpawnData>,
}

/// Load a .aozone file for a given map number.
/// Returns None if file doesn't exist (map has no zones — wilderness only).
pub fn load_zone_file(base_dir: &str, map_num: i32) -> Option<MapZones> {
    let path = Path::new(base_dir).join(format!("Mapa{}.aozone", map_num));
    if !path.exists() {
        return None;
    }

    let content = match fs::read_to_string(&path) {
        Ok(c) => c,
        Err(e) => {
            tracing::warn!("[ZONES] Failed to read {}: {}", path.display(), e);
            return None;
        }
    };

    let mut result = MapZones::default();
    let mut current_section = String::new();
    let mut current_zone = ZoneData::default();
    let mut current_spawn = NpcSpawnData::default();
    let mut zone_count = 0u16;

    for line in content.lines() {
        let line = line.trim();
        if line.is_empty() || line.starts_with(';') || line.starts_with('#') {
            continue;
        }

        // Section header
        if line.starts_with('[') && line.ends_with(']') {
            // Save previous section
            if current_section.starts_with("ZONE") && current_zone.x1 > 0 {
                result.zones.push(current_zone.clone());
            } else if current_section.starts_with("SPAWN") && current_spawn.npc_index > 0 {
                result.spawns.push(current_spawn.clone());
            }

            current_section = line[1..line.len() - 1].to_uppercase();

            if current_section.starts_with("ZONE") {
                zone_count += 1;
                current_zone = ZoneData {
                    id: zone_count,
                    ..Default::default()
                };
            } else if current_section.starts_with("SPAWN") {
                current_spawn = NpcSpawnData::default();
            }
            continue;
        }

        // Key=Value
        let eq = match line.find('=') {
            Some(i) => i,
            None => continue,
        };
        let key = line[..eq].trim().to_uppercase();
        let val = line[eq + 1..].trim();

        if current_section.starts_with("ZONE") {
            parse_zone_field(&mut current_zone, &key, val);
        } else if current_section.starts_with("SPAWN") {
            parse_spawn_field(&mut current_spawn, &key, val);
        }
    }

    // Save last section
    if current_section.starts_with("ZONE") && current_zone.x1 > 0 {
        result.zones.push(current_zone);
    } else if current_section.starts_with("SPAWN") && current_spawn.npc_index > 0 {
        result.spawns.push(current_spawn);
    }

    tracing::info!(
        "[ZONES] Map {} loaded: {} zones, {} spawns",
        map_num,
        result.zones.len(),
        result.spawns.len()
    );
    Some(result)
}

fn parse_zone_field(zone: &mut ZoneData, key: &str, val: &str) {
    match key {
        "NAME" => zone.name = val.to_string(),
        "TYPE" => zone.zone_type = ZoneType::from_i32(val.parse().unwrap_or(0)),
        "X1" => zone.x1 = val.parse().unwrap_or(0),
        "Y1" => zone.y1 = val.parse().unwrap_or(0),
        "X2" => zone.x2 = val.parse().unwrap_or(0),
        "Y2" => zone.y2 = val.parse().unwrap_or(0),
        "SEGURA" => zone.segura = val == "1",
        "NEWBIE" => zone.newbie = val == "1",
        "SINMAGIA" => zone.sin_magia = val == "1",
        "SININVI" => zone.sin_invi = val == "1",
        "SINMASCOTAS" => zone.sin_mascotas = val == "1",
        "SINRESUCITAR" => zone.sin_resucitar = val == "1",
        "SINOCULTAR" => zone.sin_ocultar = val == "1",
        "SININVOCAR" => zone.sin_invocar = val == "1",
        "SOLOCLANES" => zone.solo_clanes = val == "1",
        "SOLOFACCION" => zone.solo_faccion = val == "1",
        "FACCION" => zone.faccion = val.parse().unwrap_or(0),
        "COMBATZONE" => zone.combat_zone = val == "1",
        "MINLEVEL" => zone.min_level = val.parse().unwrap_or(0),
        "MAXLEVEL" => zone.max_level = val.parse().unwrap_or(0),
        "MUSICA" => zone.musica = val.parse().unwrap_or(0),
        "LLUVIA" => zone.lluvia = val == "1",
        "NIEVE" => zone.nieve = val == "1",
        "NIEBLA" => zone.niebla = val == "1",
        "AMBIENTR" => zone.ambient_r = val.parse().unwrap_or(0),
        "AMBIENTG" => zone.ambient_g = val.parse().unwrap_or(0),
        "AMBIENTB" => zone.ambient_b = val.parse().unwrap_or(0),
        "TERRENO" => zone.terreno = val.to_string(),
        "SALIDAMAP" => zone.salida_map = val.parse().unwrap_or(0),
        "SALIDAX" => zone.salida_x = val.parse().unwrap_or(0),
        "SALIDAY" => zone.salida_y = val.parse().unwrap_or(0),
        _ => {}
    }
}

fn parse_spawn_field(spawn: &mut NpcSpawnData, key: &str, val: &str) {
    match key {
        "ZONE" => spawn.zone_id = val.parse().unwrap_or(0),
        "NPCINDEX" => spawn.npc_index = val.parse().unwrap_or(0),
        "CANTIDAD" => spawn.cantidad = val.parse().unwrap_or(1),
        "SPAWNMODE" => {
            spawn.spawn_mode = if val == "1" {
                SpawnMode::Random
            } else {
                SpawnMode::Fixed
            }
        }
        "SPAWNX" => spawn.spawn_x = val.parse().unwrap_or(0),
        "SPAWNY" => spawn.spawn_y = val.parse().unwrap_or(0),
        "RESPAWNTIME" => spawn.respawn_time = val.parse().unwrap_or(30),
        _ => {}
    }
}

/// Save a MapZones to a .aozone file.
pub fn save_zone_file(base_dir: &str, map_num: i32, zones: &MapZones) -> Result<(), String> {
    let path = Path::new(base_dir).join(format!("Mapa{}.aozone", map_num));

    let mut content = String::new();
    content.push_str("; Zone file for map ");
    content.push_str(&map_num.to_string());
    content.push_str("\n; Generated by AO World Editor\n\n");

    for (i, zone) in zones.zones.iter().enumerate() {
        content.push_str(&format!("[ZONE{}]\n", i + 1));
        content.push_str(&format!("Name={}\n", zone.name));
        content.push_str(&format!("Type={}\n", zone.zone_type as i32));
        content.push_str(&format!(
            "X1={}\nY1={}\nX2={}\nY2={}\n",
            zone.x1, zone.y1, zone.x2, zone.y2
        ));
        content.push_str(&format!("Segura={}\n", zone.segura as i32));
        content.push_str(&format!("Newbie={}\n", zone.newbie as i32));
        content.push_str(&format!("SinMagia={}\n", zone.sin_magia as i32));
        content.push_str(&format!("SinInvi={}\n", zone.sin_invi as i32));
        content.push_str(&format!("SinMascotas={}\n", zone.sin_mascotas as i32));
        content.push_str(&format!("SinResucitar={}\n", zone.sin_resucitar as i32));
        content.push_str(&format!("SinOcultar={}\n", zone.sin_ocultar as i32));
        content.push_str(&format!("SinInvocar={}\n", zone.sin_invocar as i32));
        content.push_str(&format!("SoloClanes={}\n", zone.solo_clanes as i32));
        content.push_str(&format!("SoloFaccion={}\n", zone.solo_faccion as i32));
        content.push_str(&format!("Faccion={}\n", zone.faccion));
        content.push_str(&format!("CombatZone={}\n", zone.combat_zone as i32));
        content.push_str(&format!("MinLevel={}\n", zone.min_level));
        content.push_str(&format!("MaxLevel={}\n", zone.max_level));
        content.push_str(&format!("Musica={}\n", zone.musica));
        content.push_str(&format!("Lluvia={}\n", zone.lluvia as i32));
        content.push_str(&format!("Nieve={}\n", zone.nieve as i32));
        content.push_str(&format!("Niebla={}\n", zone.niebla as i32));
        content.push_str(&format!(
            "AmbientR={}\nAmbientG={}\nAmbientB={}\n",
            zone.ambient_r, zone.ambient_g, zone.ambient_b
        ));
        content.push_str(&format!("Terreno={}\n", zone.terreno));
        content.push_str(&format!(
            "SalidaMap={}\nSalidaX={}\nSalidaY={}\n",
            zone.salida_map, zone.salida_x, zone.salida_y
        ));
        content.push('\n');
    }

    for (i, spawn) in zones.spawns.iter().enumerate() {
        content.push_str(&format!("[SPAWN{}]\n", i + 1));
        content.push_str(&format!("Zone={}\n", spawn.zone_id));
        content.push_str(&format!("NpcIndex={}\n", spawn.npc_index));
        content.push_str(&format!("Cantidad={}\n", spawn.cantidad));
        content.push_str(&format!("SpawnMode={}\n", spawn.spawn_mode as i32));
        if spawn.spawn_mode == SpawnMode::Fixed {
            content.push_str(&format!(
                "SpawnX={}\nSpawnY={}\n",
                spawn.spawn_x, spawn.spawn_y
            ));
        }
        content.push_str(&format!("RespawnTime={}\n", spawn.respawn_time));
        content.push('\n');
    }

    fs::write(&path, content).map_err(|e| format!("Failed to write {}: {}", path.display(), e))
}
