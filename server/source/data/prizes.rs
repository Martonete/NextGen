// Prize database loader — Dat/Premios.dat
//
// INI format with [INIT] NumPremios=27 and [PREMIO1]..[PREMIOnn] sections.
// Tournament prize shop items exchanged for puntos_torneo.

use std::path::Path;
use crate::config::IniFile;

/// Prize definition loaded from Premios.dat
#[derive(Debug, Clone)]
pub struct PrizeData {
    pub index: usize,
    pub name: String,
    pub obj_index: i32,
    pub require: i32,          // Tournament points cost
    pub atk_min: i32,
    pub atk_max: i32,
    pub def_min: i32,
    pub def_max: i32,
    pub atk_mag_min: i32,
    pub atk_mag_max: i32,
    pub def_mag_min: i32,
    pub def_mag_max: i32,
    pub description: String,
}

/// Load prizes database
pub fn load_prizes(base: &Path) -> Result<Vec<PrizeData>, String> {
    let path = base.join("Dat").join("Premios.dat");
    let ini = IniFile::load(&path)
        .map_err(|e| format!("Failed to load Premios.dat: {}", e))?;

    let num: usize = ini.get("INIT", "NumPremios")
        .and_then(|s| s.parse().ok())
        .unwrap_or(0);

    let mut prizes = Vec::with_capacity(num);

    for i in 1..=num {
        let section = format!("PREMIO{}", i);

        prizes.push(PrizeData {
            index: i,
            name: ini.get(&section, "Nombre").unwrap_or_default(),
            obj_index: ini.get(&section, "NumObj").and_then(|s| s.parse().ok()).unwrap_or(0),
            require: ini.get(&section, "Requiere").and_then(|s| s.parse().ok()).unwrap_or(0),
            atk_min: ini.get(&section, "AtaqueMinimo").and_then(|s| s.parse().ok()).unwrap_or(0),
            atk_max: ini.get(&section, "AtaqueMaximo").and_then(|s| s.parse().ok()).unwrap_or(0),
            def_min: ini.get(&section, "DefensaMinima").and_then(|s| s.parse().ok()).unwrap_or(0),
            def_max: ini.get(&section, "DefensaMaxima").and_then(|s| s.parse().ok()).unwrap_or(0),
            atk_mag_min: ini.get(&section, "AtaqueMagicoMinimo").and_then(|s| s.parse().ok()).unwrap_or(0),
            atk_mag_max: ini.get(&section, "AtaqueMagicoMaximo").and_then(|s| s.parse().ok()).unwrap_or(0),
            def_mag_min: ini.get(&section, "DefensaMagicaMinima").and_then(|s| s.parse().ok()).unwrap_or(0),
            def_mag_max: ini.get(&section, "DefensaMagicaMaxima").and_then(|s| s.parse().ok()).unwrap_or(0),
            description: ini.get(&section, "Descripcion").unwrap_or_else(|| "Sin descripcion.".to_string()),
        });
    }

    Ok(prizes)
}
