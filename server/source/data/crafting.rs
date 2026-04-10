// Crafting recipe data loader — dat/ArmasHerrero.dat, ArmadurasHerrero.dat, ObjCarpintero.dat
//
// VB6: CargarArmasHerrero, CargarArmadurasHerrero, LoadObjCarpintero (FileIO.bas).
// Each file contains a list of object indices that can be crafted in the respective skill.
// The actual recipe details (materials, skill requirements) come from Obj.dat.

use crate::config::IniFile;
use std::path::Path;

/// All crafting recipe lists loaded from DAT files.
#[derive(Debug, Clone, Default)]
pub struct CraftingData {
    /// Object indices that can be crafted as blacksmith weapons (ArmasHerrero.dat).
    pub smith_weapons: Vec<i32>,
    /// Object indices that can be crafted as blacksmith armors (ArmadurasHerrero.dat).
    pub smith_armors: Vec<i32>,
    /// Object indices that can be crafted by carpenter (ObjCarpintero.dat).
    pub carpenter_items: Vec<i32>,
}

impl CraftingData {
    /// Check if an object index is a valid blacksmith weapon recipe.
    pub fn is_smith_weapon(&self, obj_index: i32) -> bool {
        self.smith_weapons.contains(&obj_index)
    }

    /// Check if an object index is a valid blacksmith armor recipe.
    pub fn is_smith_armor(&self, obj_index: i32) -> bool {
        self.smith_armors.contains(&obj_index)
    }

    /// Check if an object index is a valid blacksmith recipe (weapon or armor).
    pub fn is_smith_item(&self, obj_index: i32) -> bool {
        self.is_smith_weapon(obj_index) || self.is_smith_armor(obj_index)
    }

    /// Check if an object index is a valid carpenter recipe.
    pub fn is_carpenter_item(&self, obj_index: i32) -> bool {
        self.carpenter_items.contains(&obj_index)
    }
}

/// Load all crafting recipe lists from the dat/ directory.
pub fn load_crafting(base: &Path) -> Result<CraftingData, String> {
    let smith_weapons = load_smith_weapons(base)?;
    let smith_armors = load_smith_armors(base)?;
    let carpenter_items = load_carpenter_items(base)?;

    tracing::info!(
        "Crafting recipes loaded: {} smith weapons, {} smith armors, {} carpenter items",
        smith_weapons.len(),
        smith_armors.len(),
        carpenter_items.len()
    );

    Ok(CraftingData {
        smith_weapons,
        smith_armors,
        carpenter_items,
    })
}

/// Load blacksmith weapon indices from ArmasHerrero.dat.
/// VB6: [INIT] NumArmas=N, [Arma1]..[ArmaN] Index=<obj_index>
fn load_smith_weapons(base: &Path) -> Result<Vec<i32>, String> {
    let path = base.join("dat").join("ArmasHerrero.dat");
    if !path.exists() {
        tracing::warn!("ArmasHerrero.dat not found, using empty weapon list");
        return Ok(Vec::new());
    }

    let ini =
        IniFile::load(&path).map_err(|e| format!("Failed to load ArmasHerrero.dat: {}", e))?;

    let num = ini
        .get("INIT", "NumArmas")
        .and_then(|v| v.parse::<i32>().ok())
        .unwrap_or(0);

    let mut items = Vec::with_capacity(num as usize);
    for i in 1..=num {
        let section = format!("Arma{}", i);
        if let Some(idx) = ini
            .get(&section, "Index")
            .and_then(|v| v.parse::<i32>().ok())
        {
            if idx > 0 {
                items.push(idx);
            }
        }
    }
    Ok(items)
}

/// Load blacksmith armor indices from ArmadurasHerrero.dat.
/// VB6: [INIT] NumArmaduras=N, [Armadura1]..[ArmaduraN] Index=<obj_index>
fn load_smith_armors(base: &Path) -> Result<Vec<i32>, String> {
    let path = base.join("dat").join("ArmadurasHerrero.dat");
    if !path.exists() {
        tracing::warn!("ArmadurasHerrero.dat not found, using empty armor list");
        return Ok(Vec::new());
    }

    let ini =
        IniFile::load(&path).map_err(|e| format!("Failed to load ArmadurasHerrero.dat: {}", e))?;

    let num = ini
        .get("INIT", "NumArmaduras")
        .and_then(|v| v.parse::<i32>().ok())
        .unwrap_or(0);

    let mut items = Vec::with_capacity(num as usize);
    for i in 1..=num {
        let section = format!("Armadura{}", i);
        if let Some(idx) = ini
            .get(&section, "Index")
            .and_then(|v| v.parse::<i32>().ok())
        {
            if idx > 0 {
                items.push(idx);
            }
        }
    }
    Ok(items)
}

/// Load carpenter item indices from ObjCarpintero.dat.
/// VB6: [INIT] NumObjs=N, [OBJ1]..[OBJN] Index=<obj_index>
fn load_carpenter_items(base: &Path) -> Result<Vec<i32>, String> {
    let path = base.join("dat").join("ObjCarpintero.dat");
    if !path.exists() {
        tracing::warn!("ObjCarpintero.dat not found, using empty carpenter list");
        return Ok(Vec::new());
    }

    let ini =
        IniFile::load(&path).map_err(|e| format!("Failed to load ObjCarpintero.dat: {}", e))?;

    let num = ini
        .get("INIT", "NumObjs")
        .and_then(|v| v.parse::<i32>().ok())
        .unwrap_or(0);

    let mut items = Vec::with_capacity(num as usize);
    for i in 1..=num {
        let section = format!("OBJ{}", i);
        if let Some(idx) = ini
            .get(&section, "Index")
            .and_then(|v| v.parse::<i32>().ok())
        {
            if idx > 0 {
                items.push(idx);
            }
        }
    }
    Ok(items)
}
