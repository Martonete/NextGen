// City data loader — dat/Ciudades.Dat
//
// VB6: Ciudades.dat defines city teleport destinations.
// Format: INI with section = city name, keys: X, Y, MAPA.

use std::path::Path;
use crate::config::IniFile;

/// A city definition with name and teleport coordinates.
#[derive(Debug, Clone)]
pub struct CityData {
    pub name: String,
    pub map: i32,
    pub x: i32,
    pub y: i32,
}

/// Load all cities from dat/Ciudades.Dat.
pub fn load_cities(base: &Path) -> Result<Vec<CityData>, String> {
    let path = base.join("dat").join("Ciudades.Dat");
    if !path.exists() {
        tracing::warn!("Ciudades.Dat not found at {}, using empty city list", path.display());
        return Ok(Vec::new());
    }

    let ini = IniFile::load(&path)
        .map_err(|e| format!("Failed to load Ciudades.Dat: {}", e))?;

    let mut cities = Vec::new();
    for section in ini.section_names() {
        // Each section is a city name (e.g. [NIX], [Ullathorpe])
        let x = ini.get(&section, "X")
            .and_then(|v| v.parse::<i32>().ok())
            .unwrap_or(0);
        let y = ini.get(&section, "Y")
            .and_then(|v| v.parse::<i32>().ok())
            .unwrap_or(0);
        let map = ini.get(&section, "MAPA")
            .and_then(|v| v.parse::<i32>().ok())
            .unwrap_or(0);

        if map > 0 {
            cities.push(CityData {
                name: section.clone(),
                map,
                x,
                y,
            });
        }
    }

    tracing::info!("Loaded {} cities from Ciudades.Dat", cities.len());
    Ok(cities)
}

/// Find a city by name (case-insensitive).
pub fn find_city<'a>(cities: &'a [CityData], name: &str) -> Option<&'a CityData> {
    let lower = name.to_lowercase();
    cities.iter().find(|c| c.name.to_lowercase() == lower)
}
