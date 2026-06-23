// Zone properties system — configurable zone behaviors.
//
// Each "map" is a zone with an ID and configurable properties.
// Zones define: PvP rules, safe areas, spawn points, magic restrictions,
// ambient settings, and more. This replaces the old per-tile trigger system
// with a zone-level configuration that can be loaded from zones.ini.
//
// Future: sub-zones within a large map (e.g., a 1000x1000 world map split
// into named regions with different rules).

use crate::config::IniFile;
use std::collections::HashMap;
use std::path::Path;

/// Zone type — determines core PvP/safety rules.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum ZoneType {
    /// Normal zone — PvP allowed with safety toggle.
    Normal,
    /// Safe zone — no PvP, no stealing, no aggro.
    Safe,
    /// PvP forced — all players can attack each other.
    PvP,
    /// Arena — PvP with special rules (no item drop on death).
    Arena,
    /// Dungeon — PvE focused, possibly instanced.
    Dungeon,
    /// Town — safe with NPCs, shops, banks.
    Town,
}

impl ZoneType {
    fn from_str(s: &str) -> Self {
        match s.to_lowercase().as_str() {
            "safe" | "segura" => Self::Safe,
            "pvp" | "pk" => Self::PvP,
            "arena" => Self::Arena,
            "dungeon" | "mazmorra" => Self::Dungeon,
            "town" | "ciudad" => Self::Town,
            _ => Self::Normal,
        }
    }

    /// Can players attack each other in this zone?
    pub fn allows_pvp(&self) -> bool {
        matches!(self, Self::Normal | Self::PvP | Self::Arena)
    }

    /// Is this a protected zone (no PvP, no stealing)?
    pub fn is_safe(&self) -> bool {
        matches!(self, Self::Safe | Self::Town)
    }

    /// Do players keep items on death?
    pub fn keep_items_on_death(&self) -> bool {
        matches!(self, Self::Arena | Self::Safe | Self::Town)
    }
}

/// Properties for a single zone (map).
#[derive(Debug, Clone)]
pub struct ZoneProperties {
    /// Zone ID (same as map number).
    pub id: i32,
    /// Display name.
    pub name: String,
    /// Zone type (determines PvP/safety rules).
    pub zone_type: ZoneType,
    /// Music track ID.
    pub music: i32,
    /// Ambient color (R, G, B).
    pub ambient: (i32, i32, i32),
    /// Terrain type (for footstep sounds, etc.).
    pub terrain: String,

    // Magic restrictions
    /// Block all magic in this zone.
    pub no_magic: bool,
    /// Block invisibility spells.
    pub no_invis: bool,
    /// Block resurrection spells.
    pub no_resurrect: bool,
    /// Block hide skill.
    pub no_hide: bool,
    /// Block invocation/summon spells.
    pub no_summon: bool,

    // Spawn configuration
    /// Respawn point (zone_id, x, y) — where players respawn on death.
    /// If None, uses the global start position.
    pub respawn_point: Option<(i32, i32, i32)>,
    /// NPC respawn enabled in this zone.
    pub npc_respawn: bool,

    // Grid dimensions (allows zones larger than 100x100).
    /// Grid width in tiles (default 100 for legacy maps).
    pub grid_width: i32,
    /// Grid height in tiles (default 100 for legacy maps).
    pub grid_height: i32,

    /// Whether to persist ground items (backup).
    pub persist_ground_items: bool,
    /// Level range restriction (min, max). None = no restriction.
    pub level_range: Option<(i32, i32)>,

    /// Max distance NPCs can wander from their spawn point (Chebyshev).
    /// 0 = unlimited (default for legacy maps). Used to keep NPCs in their zone.
    pub npc_wander_radius: i32,
}

impl Default for ZoneProperties {
    fn default() -> Self {
        Self {
            id: 0,
            name: String::new(),
            zone_type: ZoneType::Normal,
            music: 0,
            ambient: (200, 200, 200),
            terrain: String::new(),
            no_magic: false,
            no_invis: false,
            no_resurrect: false,
            no_hide: false,
            no_summon: false,
            respawn_point: None,
            npc_respawn: true,
            grid_width: 100,
            grid_height: 100,
            persist_ground_items: false,
            level_range: None,
            npc_wander_radius: 0, // 0 = unlimited
        }
    }
}

impl ZoneProperties {
    /// Build zone properties from the legacy MapInfo (backward compatible).
    pub fn from_map_info(info: &crate::data::maps::MapInfo) -> Self {
        let zone_type = if !info.pk {
            // VB6 inversion: pk=false means PvP enabled
            ZoneType::Normal
        } else {
            ZoneType::Safe
        };

        Self {
            id: info.num as i32,
            name: info.name.clone(),
            zone_type,
            music: info.music,
            ambient: (info.r, info.g, info.b),
            terrain: info.terreno.clone(),
            no_magic: info.magia_sin_efecto,
            no_invis: info.invi_sin_efecto,
            no_resurrect: info.resu_sin_efecto,
            no_hide: info.ocultar_sin_efecto,
            no_summon: info.invocar_sin_efecto,
            respawn_point: None,
            npc_respawn: true,
            grid_width: 100,
            grid_height: 100,
            persist_ground_items: info.backup,
            level_range: None,
            npc_wander_radius: 0,
        }
    }
}

/// Zone registry — holds properties for all zones.
pub type ZoneRegistry = HashMap<i32, ZoneProperties>;

/// Load zone overrides from zones.ini.
/// Zones not defined in zones.ini use properties derived from their .dat files.
///
/// Format:
/// ```ini
/// [ZONE1]
/// Name=Ciudad de Ullathorpe
/// Type=Town
/// Music=3
/// Ambient=200,200,200
/// NoMagic=0
/// NoInvis=0
/// NoResurrect=0
/// NoHide=0
/// NoSummon=0
/// RespawnZone=1
/// RespawnX=50
/// RespawnY=50
/// NpcRespawn=1
/// GridWidth=100
/// GridHeight=100
/// Backup=1
/// LevelMin=0
/// LevelMax=0
/// ```
pub fn load_zone_overrides(base_path: &Path) -> ZoneRegistry {
    let path = base_path.join("dat").join("zones.ini");
    let ini = match IniFile::load(&path) {
        Ok(ini) => ini,
        Err(_) => {
            tracing::info!("zones.ini not found — using legacy .dat zone properties");
            return HashMap::new();
        }
    };

    let mut zones = HashMap::new();

    // Scan for sections named ZONE<N>
    for zone_id in 1..=2000 {
        let section = format!("ZONE{}", zone_id);
        let name = match ini.get(&section, "Name") {
            Some(n) if !n.is_empty() => n,
            _ => continue, // Section doesn't exist or has no name
        };

        let get_str = |key: &str| ini.get(&section, key).unwrap_or_default();
        let get_int = |key: &str, default: i32| -> i32 {
            ini.get(&section, key)
                .and_then(|s| s.trim().parse().ok())
                .unwrap_or(default)
        };
        let get_bool = |key: &str| -> bool {
            ini.get(&section, key)
                .map(|s| s.trim() == "1" || s.trim().eq_ignore_ascii_case("true"))
                .unwrap_or(false)
        };

        let ambient = {
            let raw = get_str("Ambient");
            let parts: Vec<i32> = raw
                .split(',')
                .filter_map(|s| s.trim().parse().ok())
                .collect();
            if parts.len() >= 3 {
                (parts[0], parts[1], parts[2])
            } else {
                (200, 200, 200)
            }
        };

        let respawn = {
            let rz = get_int("RespawnZone", 0);
            let rx = get_int("RespawnX", 0);
            let ry = get_int("RespawnY", 0);
            if rz > 0 && rx > 0 && ry > 0 {
                Some((rz, rx, ry))
            } else {
                None
            }
        };

        let level_range = {
            let min = get_int("LevelMin", 0);
            let max = get_int("LevelMax", 0);
            if min > 0 || max > 0 {
                Some((min, if max > 0 { max } else { 255 }))
            } else {
                None
            }
        };

        let props = ZoneProperties {
            id: zone_id,
            name,
            zone_type: ZoneType::from_str(&get_str("Type")),
            music: get_int("Music", 0),
            ambient,
            terrain: get_str("Terrain"),
            no_magic: get_bool("NoMagic"),
            no_invis: get_bool("NoInvis"),
            no_resurrect: get_bool("NoResurrect"),
            no_hide: get_bool("NoHide"),
            no_summon: get_bool("NoSummon"),
            respawn_point: respawn,
            npc_respawn: !get_bool("NoNpcRespawn"), // inverted: default is true
            grid_width: get_int("GridWidth", 100),
            grid_height: get_int("GridHeight", 100),
            persist_ground_items: get_bool("Backup"),
            level_range,
            npc_wander_radius: get_int("NpcWanderRadius", 0),
        };

        tracing::info!(
            "Zone {} ({}) loaded: type={:?}, {}x{}",
            zone_id,
            props.name,
            props.zone_type,
            props.grid_width,
            props.grid_height
        );
        zones.insert(zone_id, props);
    }

    if !zones.is_empty() {
        tracing::info!("{} zone overrides loaded from zones.ini", zones.len());
    }

    zones
}
