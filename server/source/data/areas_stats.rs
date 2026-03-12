// AreasStats.dat parser — Area bandwidth optimization statistics.
//
// VB6 ModAreas.bas: AreasStats.dat stores per-map connection group sizes
// indexed by day-type and hour-slot, used to pre-allocate user arrays
// for the area-based visibility system.
//
// Format: INI-style
//   [MapaN]         — Section per map number (1..NumMaps)
//   dayType-hourSlot=value
//
// dayType: 1 = weekend (Sat-Sun), 2 = weekday (Mon-Fri)
// hourSlot: Hour \ 3 → 0..7 (3-hour blocks: 0-2, 3-5, 6-8, 9-11, 12-14, 15-17, 18-20, 21-23)
// value: optimal ConnGroup array size (0 defaults to 1)

use std::collections::HashMap;
use std::path::Path;

use crate::config::IniFile;

/// Per-map area statistics: expected connection group sizes by time slot.
#[derive(Debug, Clone)]
pub struct MapAreaStats {
    /// Key: (day_type, hour_slot) → optimal user array size.
    /// day_type: 1=weekend, 2=weekday. hour_slot: 0..7.
    pub slots: HashMap<(u8, u8), i32>,
}

/// All area statistics indexed by map number.
#[derive(Debug, Clone)]
pub struct AreasStats {
    /// Map number → area stats. Map numbers are 1-based.
    pub maps: HashMap<i32, MapAreaStats>,
}

impl Default for AreasStats {
    fn default() -> Self {
        Self { maps: HashMap::new() }
    }
}

/// Load AreasStats.dat from the dat/ directory.
pub fn load_areas_stats(base: &Path) -> Result<AreasStats, String> {
    let path = base.join("server").join("dat").join("AreasStats.dat");
    if !path.exists() {
        tracing::warn!("AreasStats.dat not found at {:?}, using defaults", path);
        return Ok(AreasStats::default());
    }

    let ini = IniFile::load(&path)
        .map_err(|e| format!("Failed to load AreasStats.dat: {}", e))?;

    let mut stats = AreasStats { maps: HashMap::new() };

    // Iterate sections: [Mapa1], [Mapa2], ... [MapaN]
    // IniFile stores section names lowercased
    for map_num in 1..=500 {
        let section = format!("mapa{}", map_num);
        // Try to read all 16 possible keys (2 day types * 8 hour slots)
        let mut slots = HashMap::new();
        let mut found_any = false;

        for day_type in 1u8..=2 {
            for hour_slot in 0u8..=7 {
                let key = format!("{}-{}", day_type, hour_slot);
                if let Some(val_str) = ini.get(&section, &key) {
                    let val: i32 = val_str.trim().parse::<i32>().unwrap_or(0);
                    slots.insert((day_type, hour_slot), val);
                    found_any = true;
                }
            }
        }

        if found_any {
            stats.maps.insert(map_num, MapAreaStats { slots });
        }
    }

    tracing::info!("Loaded AreasStats.dat: {} maps with area statistics", stats.maps.len());
    Ok(stats)
}
