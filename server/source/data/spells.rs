// Spells database loader — dat/Hechizos.dat
//
// INI format with [INIT] NumeroHechizos and [HECHIZO1]..[HECHIZOn] sections.
// 65 spells in the current database.

use std::path::Path;
use crate::config::IniFile;

/// Spell type matching VB6 TipoHechizo.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
#[repr(u8)]
pub enum SpellType {
    Properties = 1,     // HP/Mana/Sta modifications
    Status = 2,         // Status effects (poison, paralyze, etc.)
    Materialize = 3,    // Create items
    Invocation = 4,     // Summon creatures
    Teleport = 5,       // Teleportation
    Bubble = 6,         // Bubble shield
    SummonPet = 7,      // Summon pet
    Unknown = 0,
}

impl SpellType {
    fn from_i32(v: i32) -> Self {
        match v {
            1 => Self::Properties,
            2 => Self::Status,
            3 => Self::Materialize,
            4 => Self::Invocation,
            5 => Self::Teleport,
            6 => Self::Bubble,
            7 => Self::SummonPet,
            _ => Self::Unknown,
        }
    }
}

/// Spell target type.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
#[repr(u8)]
pub enum TargetType {
    UserOnly = 1,
    NpcOnly = 2,
    UserAndNpc = 3,
    Terrain = 4,
    Self_ = 5,
    Unknown = 0,
}

impl TargetType {
    fn from_i32(v: i32) -> Self {
        match v {
            1 => Self::UserOnly,
            2 => Self::NpcOnly,
            3 => Self::UserAndNpc,
            4 => Self::Terrain,
            5 => Self::Self_,
            _ => Self::Unknown,
        }
    }
}

/// Spell data matching VB6 tHechizo type.
#[derive(Debug, Clone)]
pub struct SpellData {
    pub index: usize,
    pub nombre: String,
    pub desc: String,
    pub palabras_magicas: String,
    pub hechizero_msg: String,
    pub target_msg: String,
    pub propio_msg: String,
    pub tipo: SpellType,
    pub target: TargetType,

    // Requirements
    pub min_skill: i32,
    pub mana_requerido: i32,
    pub sta_requerido: i32,

    // Effects — HP/Mana/Sta modifications
    pub sube_hp: i32,       // 1=heal, 2=damage
    pub min_hp: i32,
    pub max_hp: i32,
    pub sube_mana: i32,
    pub min_mana: i32,
    pub max_mana: i32,
    pub sube_sta: i32,
    pub min_sta: i32,
    pub max_sta: i32,
    pub sube_ham: i32,
    pub sube_sed: i32,
    pub sube_agilidad: i32,
    pub min_agilidad: i32,
    pub max_agilidad: i32,
    pub sube_fuerza: i32,
    pub min_fuerza: i32,
    pub max_fuerza: i32,
    pub sube_carisma: i32,

    // Status effects
    pub invisibilidad: bool,
    pub paraliza: bool,
    pub inmoviliza: bool,
    pub envenena: bool,
    pub maldicion: bool,
    pub bendicion: bool,
    pub cura_veneno: bool,
    pub remover_paralisis: bool,
    pub revivir: bool,
    pub resis: bool,        // Resistible

    // Invocation/summon
    pub num_npc: i32,       // NPC index to summon
    pub cant: i32,          // Number to summon

    // Teleport
    pub portal_map: i32,
    pub portal_x: i32,
    pub portal_y: i32,

    // Visual/audio
    pub wav: i32,           // Sound effect
    pub fx_grh: i32,        // Visual effect graphic
    pub loops: i32,         // Animation loops
}

impl Default for SpellData {
    fn default() -> Self {
        Self {
            index: 0,
            nombre: String::new(),
            desc: String::new(),
            palabras_magicas: String::new(),
            hechizero_msg: String::new(),
            target_msg: String::new(),
            propio_msg: String::new(),
            tipo: SpellType::Unknown,
            target: TargetType::Unknown,
            min_skill: 0,
            mana_requerido: 0,
            sta_requerido: 0,
            sube_hp: 0, min_hp: 0, max_hp: 0,
            sube_mana: 0, min_mana: 0, max_mana: 0,
            sube_sta: 0, min_sta: 0, max_sta: 0,
            sube_ham: 0, sube_sed: 0,
            sube_agilidad: 0, min_agilidad: 0, max_agilidad: 0,
            sube_fuerza: 0, min_fuerza: 0, max_fuerza: 0,
            sube_carisma: 0,
            invisibilidad: false, paraliza: false, inmoviliza: false,
            envenena: false, maldicion: false, bendicion: false,
            cura_veneno: false, remover_paralisis: false, revivir: false, resis: false,
            num_npc: 0, cant: 0,
            portal_map: 0, portal_x: 0, portal_y: 0,
            wav: 0, fx_grh: 0, loops: 0,
        }
    }
}

/// Load the complete spells database.
pub fn load_spells(base: &Path) -> Result<Vec<SpellData>, String> {
    let path = base.join("dat").join("Hechizos.dat");
    let ini = IniFile::load(&path)
        .map_err(|e| format!("Failed to load Hechizos.dat: {}", e))?;

    let num_spells: usize = ini.get("INIT", "NumeroHechizos")
        .and_then(|s| s.parse().ok())
        .unwrap_or(0);

    if num_spells == 0 {
        return Err("Hechizos.dat: NumeroHechizos is 0 or missing".into());
    }

    let mut spells = Vec::with_capacity(num_spells);

    for i in 1..=num_spells {
        let section = format!("Hechizo{}", i);

        let get_str = |key: &str| -> String {
            ini.get(&section, key).unwrap_or_default()
        };
        let get_int = |key: &str| -> i32 {
            ini.get(&section, key).and_then(|s| s.parse().ok()).unwrap_or(0)
        };
        let get_bool = |key: &str| -> bool {
            ini.get(&section, key).map(|s| s == "1").unwrap_or(false)
        };

        let spell = SpellData {
            index: i,
            nombre: get_str("Nombre"),
            desc: get_str("Desc"),
            palabras_magicas: get_str("PalabrasMagicas"),
            hechizero_msg: get_str("HechizeroMsg"),
            target_msg: get_str("TargetMsg"),
            propio_msg: get_str("PropioMsg"),
            tipo: SpellType::from_i32(get_int("Tipo")),
            target: TargetType::from_i32(get_int("Target")),
            min_skill: get_int("MinSkill"),
            mana_requerido: get_int("ManaRequerido"),
            sta_requerido: get_int("StaRequerido"),
            sube_hp: get_int("SubeHP"),
            min_hp: get_int("MinHP"),
            max_hp: get_int("MaxHP"),
            sube_mana: get_int("SubeMana"),
            min_mana: get_int("MinMana"),
            max_mana: get_int("MaxMana"),
            sube_sta: get_int("SubeSta"),
            min_sta: get_int("MinSta"),
            max_sta: get_int("MaxSta"),
            sube_ham: get_int("SubeHam"),
            sube_sed: get_int("SubeSed"),
            sube_agilidad: get_int("SubeAgilidad"),
            min_agilidad: get_int("MinAG"),
            max_agilidad: get_int("MaxAG"),
            sube_fuerza: get_int("SubeFuerza"),
            min_fuerza: get_int("MinFU"),
            max_fuerza: get_int("MaxFU"),
            sube_carisma: get_int("SubeCarisma"),
            invisibilidad: get_bool("Invisibilidad"),
            paraliza: get_bool("Paraliza"),
            inmoviliza: get_bool("Inmoviliza"),
            envenena: get_bool("Envenena"),
            maldicion: get_bool("Maldicion"),
            bendicion: get_bool("Bendicion"),
            cura_veneno: get_bool("CuraVeneno"),
            remover_paralisis: get_bool("RemoverParalisis"),
            revivir: get_bool("Revivir"),
            resis: get_bool("Resis"),
            num_npc: get_int("numNPC"),
            cant: get_int("Cant"),
            portal_map: get_int("PortalMap"),
            portal_x: get_int("PortalX"),
            portal_y: get_int("PortalY"),
            wav: get_int("WAV"),
            fx_grh: get_int("FXgrh"),
            loops: get_int("Loops"),
        };

        spells.push(spell);
    }

    tracing::info!("Spells loaded: {} spells", spells.len());
    Ok(spells)
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn load_real_spells() {
        let base = Path::new(env!("CARGO_MANIFEST_DIR")).join("server");
        let base = base.as_path();
        if !base.join("dat").join("Hechizos.dat").exists() {
            return;
        }
        let spells = load_spells(base).unwrap();
        assert_eq!(spells.len(), 65);
        assert_eq!(spells[0].nombre, "Curar Veneno");
        assert_eq!(spells[0].tipo, SpellType::Status);
        assert!(spells[0].cura_veneno);
    }
}
