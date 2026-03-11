// Game balance data loader — dat/Balance.dat
//
// Loads all VB6 13.3 balance sections:
//   MODRAZA, MODEVASION, MODATAQUEARMAS, MODATAQUEPROYECTILES, MODATAQUEWRESTLING,
//   MODDAÑOARMAS, MODDAÑOPROYECTILES, MODDAÑOWRESTLING, MODESCUDO, MODVIDA,
//   DISTRIBUCION, EXTRA, PARTY, RECOMPENSAFACCION.

use std::path::Path;
use crate::config::IniFile;
use crate::game::class_race::{PlayerClass, PlayerRace};

/// Number of classes in the game (VB6 13.3: Mage..Pirat = 12).
pub const NUM_CLASSES: usize = 12;

/// VB6 eClass enum order (1-based in VB6, 0-based here).
pub mod class_id {
    pub const MAGO: usize = 0;       // eClass.Mage = 1
    pub const CLERIGO: usize = 1;    // eClass.Cleric = 2
    pub const GUERRERO: usize = 2;   // eClass.Warrior = 3
    pub const ASESINO: usize = 3;    // eClass.Assasin = 4
    pub const LADRON: usize = 4;     // eClass.Thief = 5
    pub const BARDO: usize = 5;      // eClass.Bard = 6
    pub const DRUIDA: usize = 6;     // eClass.Druid = 7
    pub const BANDIDO: usize = 7;    // eClass.Bandit = 8
    pub const PALADIN: usize = 8;    // eClass.Paladin = 9
    pub const CAZADOR: usize = 9;    // eClass.Hunter = 10
    pub const TRABAJADOR: usize = 10; // eClass.Worker = 11
    pub const PIRATA: usize = 11;    // eClass.Pirat = 12
}

/// Race indices.
pub mod race_id {
    pub const HUMANO: usize = 0;       // eRaza.Humano = 1
    pub const ELFO: usize = 1;         // eRaza.Elfo = 2
    pub const ELFO_OSCURO: usize = 2;  // eRaza.Drow = 3
    pub const ENANO: usize = 3;        // eRaza.Enano = 4
    pub const GNOMO: usize = 4;        // eRaza.Gnomo = 5
}

pub const NUM_RACES: usize = 5;

/// Class names as they appear in Balance.dat keys.
const CLASS_NAMES: [&str; NUM_CLASSES] = [
    "Mago", "Clerigo", "Guerrero", "Asesino", "Ladron", "Bardo",
    "Druida", "Bandido", "Paladin", "Cazador", "Trabajador", "Pirata",
];

/// Class section names (uppercase, for AF/AM matrices).
const CLASS_SECTIONS: [&str; NUM_CLASSES] = [
    "MAGO", "CLERIGO", "GUERRERO", "ASESINO", "LADRON", "BARDO",
    "DRUIDA", "BANDIDO", "PALADIN", "CAZADOR", "TRABAJADOR", "PIRATA",
];

/// Race names for MODRAZA loading.
const RACE_PREFIXES: [&str; NUM_RACES] = [
    "Humano", "Elfo", "Drow", "Enano", "Gnomo",
];

/// Per-race attribute modifiers (VB6: ModRaza).
#[derive(Debug, Clone, Copy, Default)]
pub struct RaceModifiers {
    pub fuerza: i32,
    pub agilidad: i32,
    pub inteligencia: i32,
    pub carisma: i32,
    pub constitucion: i32,
}

/// HP distribution probabilities for level-up (VB6: [DISTRIBUCION]).
#[derive(Debug, Clone)]
pub struct HpDistribution {
    pub entera: [i32; 5],       // E1-E5 (integer average: 5 brackets)
    pub semientera: [i32; 4],   // S1-S4 (half-integer average: 4 brackets)
}

impl Default for HpDistribution {
    fn default() -> Self {
        Self {
            entera: [10, 20, 40, 20, 10],
            semientera: [10, 40, 40, 10],
        }
    }
}

/// Per-class+race faction armor assignments (VB6: ArmadurasFaccionarias.dat).
/// Indexed by [class_index][race_index], each entry has 3 armor tiers per faction.
#[derive(Debug, Clone, Copy, Default)]
pub struct FactionArmors {
    /// ObjIndex for Armada Real armor at each tier (Baja=0, Media=1, Alta=2)
    pub armada: [i32; 3],
    /// ObjIndex for Fuerzas del Caos armor at each tier
    pub caos: [i32; 3],
}

/// Full faction armor table: [class][race] → FactionArmors.
pub type FactionArmorTable = [[FactionArmors; NUM_RACES]; NUM_CLASSES];

/// VB6: GetArmourAmount — quantity of armors given per tier based on rank.
pub fn faction_armor_amount(rango: i32, tier: usize) -> i32 {
    match tier {
        0 => 20 / (rango + 1),                               // Baja: many at low rank
        1 => rango * 2 / (rango - 4).max(1),                 // Media: scales then plateaus
        2 => (rango as f32 * 1.35) as i32,                   // Alta: linear growth
        _ => 0,
    }
}

/// All combat balance data — VB6 13.3 exact.
#[derive(Debug, Clone)]
pub struct BalanceData {
    // Per-race attribute modifiers (VB6: MODRAZA)
    pub mod_raza: [RaceModifiers; NUM_RACES],

    // Per-class combat modifiers (from Balance.dat sections)
    pub mod_evasion: [f32; NUM_CLASSES],
    pub mod_ataque_armas: [f32; NUM_CLASSES],
    pub mod_ataque_proyectiles: [f32; NUM_CLASSES],
    pub mod_ataque_wrestling: [f32; NUM_CLASSES],
    pub mod_dano_armas: [f32; NUM_CLASSES],
    pub mod_dano_proyectiles: [f32; NUM_CLASSES],
    pub mod_dano_wrestling: [f32; NUM_CLASSES],
    pub mod_escudo: [f32; NUM_CLASSES],

    // HP base per class (VB6: MODVIDA)
    pub mod_vida: [f32; NUM_CLASSES],

    // HP distribution probabilities (VB6: DISTRIBUCION)
    pub hp_distribution: HpDistribution,

    // Mana recovery percentage (VB6: EXTRA.PorcentajeRecuperoMana)
    pub porcentaje_recupero_mana: i32,

    // Party XP exponent (VB6: PARTY.ExponenteNivelParty)
    pub exponente_nivel_party: f32,

    // Faction reward thresholds (VB6: RECOMPENSAFACCION)
    pub recompensa_faccion: Vec<i64>,

    // Faction armor assignments (VB6: ArmadurasFaccionarias.dat)
    pub faction_armor: FactionArmorTable,
}

impl Default for BalanceData {
    fn default() -> Self {
        Self {
            mod_raza: [RaceModifiers::default(); NUM_RACES],
            mod_evasion: [1.0; NUM_CLASSES],
            mod_ataque_armas: [1.0; NUM_CLASSES],
            mod_ataque_proyectiles: [1.0; NUM_CLASSES],
            mod_ataque_wrestling: [1.0; NUM_CLASSES],
            mod_dano_armas: [1.0; NUM_CLASSES],
            mod_dano_proyectiles: [1.0; NUM_CLASSES],
            mod_dano_wrestling: [1.0; NUM_CLASSES],
            mod_escudo: [1.0; NUM_CLASSES],
            mod_vida: [
                7.5,  // Mago
                8.5,  // Clerigo
                10.0, // Guerrero
                8.5,  // Asesino
                10.0, // Ladron
                8.5,  // Bardo
                8.5,  // Druida
                9.5,  // Bandido
                9.5,  // Paladin
                9.5,  // Cazador
                9.5,  // Trabajador
                9.5,  // Pirata
            ],
            hp_distribution: HpDistribution::default(),
            porcentaje_recupero_mana: 6,
            exponente_nivel_party: 1.75,
            recompensa_faccion: Vec::new(),
            faction_armor: [[FactionArmors::default(); NUM_RACES]; NUM_CLASSES],
        }
    }
}

/// Convert class name string to class index.
pub fn class_name_to_index(name: &str) -> Option<usize> {
    let upper = name.to_uppercase();
    match upper.as_str() {
        "MAGO" => Some(class_id::MAGO),
        "CLERIGO" | "CLÉRIGO" => Some(class_id::CLERIGO),
        "GUERRERO" => Some(class_id::GUERRERO),
        "ASESINO" => Some(class_id::ASESINO),
        "LADRON" | "LADRÓN" => Some(class_id::LADRON),
        "BARDO" => Some(class_id::BARDO),
        "DRUIDA" => Some(class_id::DRUIDA),
        "BANDIDO" => Some(class_id::BANDIDO),
        "PALADIN" | "PALADÍN" => Some(class_id::PALADIN),
        "CAZADOR" => Some(class_id::CAZADOR),
        "TRABAJADOR" => Some(class_id::TRABAJADOR),
        "PIRATA" => Some(class_id::PIRATA),
        _ => None,
    }
}

/// Convert race name string to race index.
pub fn race_name_to_index(name: &str) -> Option<usize> {
    let upper = name.to_uppercase();
    match upper.as_str() {
        "HUMANO" => Some(race_id::HUMANO),
        "ELFO" => Some(race_id::ELFO),
        "ELFO OSCURO" | "ELFOOSCURO" | "DROW" => Some(race_id::ELFO_OSCURO),
        "ENANO" => Some(race_id::ENANO),
        "GNOMO" => Some(race_id::GNOMO),
        _ => None,
    }
}

impl BalanceData {
    pub fn class_mod_evasion(&self, class: &str) -> f32 {
        class_name_to_index(class).map(|i| self.mod_evasion[i]).unwrap_or(1.0)
    }
    pub fn class_mod_ataque_armas(&self, class: &str) -> f32 {
        class_name_to_index(class).map(|i| self.mod_ataque_armas[i]).unwrap_or(1.0)
    }
    pub fn class_mod_ataque_proyectiles(&self, class: &str) -> f32 {
        class_name_to_index(class).map(|i| self.mod_ataque_proyectiles[i]).unwrap_or(1.0)
    }
    pub fn class_mod_ataque_wrestling(&self, class: &str) -> f32 {
        class_name_to_index(class).map(|i| self.mod_ataque_wrestling[i]).unwrap_or(1.0)
    }
    pub fn class_mod_dano_armas(&self, class: &str) -> f32 {
        class_name_to_index(class).map(|i| self.mod_dano_armas[i]).unwrap_or(1.0)
    }
    pub fn class_mod_dano_proyectiles(&self, class: &str) -> f32 {
        class_name_to_index(class).map(|i| self.mod_dano_proyectiles[i]).unwrap_or(1.0)
    }
    pub fn class_mod_dano_wrestling(&self, class: &str) -> f32 {
        class_name_to_index(class).map(|i| self.mod_dano_wrestling[i]).unwrap_or(1.0)
    }
    pub fn class_mod_escudo(&self, class: &str) -> f32 {
        class_name_to_index(class).map(|i| self.mod_escudo[i]).unwrap_or(1.0)
    }
    pub fn class_mod_vida(&self, class: &str) -> f32 {
        class_name_to_index(class).map(|i| self.mod_vida[i]).unwrap_or(8.0)
    }
    pub fn race_modifiers(&self, race: &str) -> RaceModifiers {
        race_name_to_index(race).map(|i| self.mod_raza[i]).unwrap_or_default()
    }
    /// Get faction armor assignments for a class+race combo.
    pub fn get_faction_armor(&self, class: &str, race: &str) -> FactionArmors {
        let ci = class_name_to_index(class).unwrap_or(0);
        let ri = race_name_to_index(race).unwrap_or(0);
        self.faction_armor[ci][ri]
    }

    // ── Enum-based overloads (type-safe, no string parsing) ─────────

    pub fn class_mod_evasion_e(&self, class: PlayerClass) -> f32 {
        self.mod_evasion[class.index()]
    }
    pub fn class_mod_ataque_armas_e(&self, class: PlayerClass) -> f32 {
        self.mod_ataque_armas[class.index()]
    }
    pub fn class_mod_ataque_proyectiles_e(&self, class: PlayerClass) -> f32 {
        self.mod_ataque_proyectiles[class.index()]
    }
    pub fn class_mod_ataque_wrestling_e(&self, class: PlayerClass) -> f32 {
        self.mod_ataque_wrestling[class.index()]
    }
    pub fn class_mod_dano_armas_e(&self, class: PlayerClass) -> f32 {
        self.mod_dano_armas[class.index()]
    }
    pub fn class_mod_dano_proyectiles_e(&self, class: PlayerClass) -> f32 {
        self.mod_dano_proyectiles[class.index()]
    }
    pub fn class_mod_dano_wrestling_e(&self, class: PlayerClass) -> f32 {
        self.mod_dano_wrestling[class.index()]
    }
    pub fn class_mod_escudo_e(&self, class: PlayerClass) -> f32 {
        self.mod_escudo[class.index()]
    }
    pub fn class_mod_vida_e(&self, class: PlayerClass) -> f32 {
        self.mod_vida[class.index()]
    }
    pub fn race_modifiers_e(&self, race: PlayerRace) -> RaceModifiers {
        self.mod_raza[race.index()]
    }
    pub fn get_faction_armor_e(&self, class: PlayerClass, race: PlayerRace) -> FactionArmors {
        self.faction_armor[class.index()][race.index()]
    }
}

/// Load Balance.dat (all VB6 13.3 sections).
pub fn load_balance(base: &Path) -> Result<BalanceData, String> {
    let mut data = BalanceData::default();

    let balance_path = base.join("dat").join("Balance.dat");
    if let Ok(ini) = IniFile::load(&balance_path) {
        let get_f32 = |section: &str, key: &str| -> f32 {
            ini.get(section, key)
                .and_then(|s| s.replace(',', ".").parse::<f32>().ok())
                .unwrap_or(0.0)
        };
        let get_i32 = |section: &str, key: &str| -> i32 {
            ini.get(section, key)
                .and_then(|s| s.replace('+', "").trim().parse::<i32>().ok())
                .unwrap_or(0)
        };

        // MODRAZA — race attribute modifiers
        let attr_names = ["Fuerza", "Agilidad", "Inteligencia", "Carisma", "Constitucion"];
        for (ri, race_prefix) in RACE_PREFIXES.iter().enumerate() {
            let f = get_i32("MODRAZA", &format!("{}Fuerza", race_prefix));
            let a = get_i32("MODRAZA", &format!("{}Agilidad", race_prefix));
            let i = get_i32("MODRAZA", &format!("{}Inteligencia", race_prefix));
            let c = get_i32("MODRAZA", &format!("{}Carisma", race_prefix));
            let co = get_i32("MODRAZA", &format!("{}Constitucion", race_prefix));
            data.mod_raza[ri] = RaceModifiers {
                fuerza: f, agilidad: a, inteligencia: i, carisma: c, constitucion: co,
            };
        }

        // Per-class modifiers
        for ci in 0..NUM_CLASSES {
            let name = CLASS_NAMES[ci];
            data.mod_evasion[ci] = get_f32("MODEVASION", name);
            data.mod_ataque_armas[ci] = get_f32("MODATAQUEARMAS", name);
            data.mod_ataque_proyectiles[ci] = get_f32("MODATAQUEPROYECTILES", name);
            data.mod_ataque_wrestling[ci] = get_f32("MODATAQUEWRESTLING", name);

            // MODDAÑOARMAS — section name has ñ (Ñ = \u{00d1})
            data.mod_dano_armas[ci] = get_f32("MODDA\u{00d1}OARMAS", name);
            if data.mod_dano_armas[ci] == 0.0 {
                data.mod_dano_armas[ci] = get_f32("MODDANOARMAS", name);
            }
            data.mod_dano_proyectiles[ci] = get_f32("MODDA\u{00d1}OPROYECTILES", name);
            if data.mod_dano_proyectiles[ci] == 0.0 {
                data.mod_dano_proyectiles[ci] = get_f32("MODDANOPROYECTILES", name);
            }
            data.mod_dano_wrestling[ci] = get_f32("MODDA\u{00d1}OWRESTLING", name);
            if data.mod_dano_wrestling[ci] == 0.0 {
                data.mod_dano_wrestling[ci] = get_f32("MODDANOWRESTLING", name);
            }

            data.mod_escudo[ci] = get_f32("MODESCUDO", name);

            // MODVIDA
            data.mod_vida[ci] = get_f32("MODVIDA", name);

            // Default multipliers to 1.0 if still 0.0
            if data.mod_evasion[ci] == 0.0 { data.mod_evasion[ci] = 1.0; }
            if data.mod_ataque_armas[ci] == 0.0 { data.mod_ataque_armas[ci] = 1.0; }
            if data.mod_ataque_proyectiles[ci] == 0.0 { data.mod_ataque_proyectiles[ci] = 1.0; }
            if data.mod_ataque_wrestling[ci] == 0.0 { data.mod_ataque_wrestling[ci] = 1.0; }
            if data.mod_dano_armas[ci] == 0.0 { data.mod_dano_armas[ci] = 1.0; }
            if data.mod_dano_proyectiles[ci] == 0.0 { data.mod_dano_proyectiles[ci] = 1.0; }
            if data.mod_dano_wrestling[ci] == 0.0 { data.mod_dano_wrestling[ci] = 1.0; }
            if data.mod_escudo[ci] == 0.0 { data.mod_escudo[ci] = 1.0; }
            // mod_vida 0 is valid for non-loaded, use default
            if data.mod_vida[ci] == 0.0 { data.mod_vida[ci] = 8.0; }
        }

        // DISTRIBUCION — HP level-up probability brackets
        for i in 0..5 {
            let val = get_i32("DISTRIBUCION", &format!("E{}", i + 1));
            if val > 0 { data.hp_distribution.entera[i] = val; }
        }
        for i in 0..4 {
            let val = get_i32("DISTRIBUCION", &format!("S{}", i + 1));
            if val > 0 { data.hp_distribution.semientera[i] = val; }
        }

        // EXTRA
        let prm = get_i32("EXTRA", "PorcentajeRecuperoMana");
        if prm > 0 { data.porcentaje_recupero_mana = prm; }

        // PARTY
        let enp = get_f32("PARTY", "ExponenteNivelParty");
        if enp > 0.0 { data.exponente_nivel_party = enp; }

        // RECOMPENSAFACCION
        for i in 1..=15 {
            let val = ini.get("RECOMPENSAFACCION", &format!("Rango{}", i))
                .and_then(|s| s.parse::<i64>().ok())
                .unwrap_or(0);
            if val > 0 { data.recompensa_faccion.push(val); }
        }
    } else {
        tracing::warn!("Balance.dat not found, using defaults");
    }

    // Load ArmadurasFaccionarias.dat
    let af_path = base.join("dat").join("ArmadurasFaccionarias.dat");
    if let Ok(af_ini) = IniFile::load(&af_path) {
        let af_get = |section: &str, key: &str| -> i32 {
            af_ini.get(section, key)
                .and_then(|s| s.trim().parse::<i32>().ok())
                .unwrap_or(0)
        };
        for ci in 0..NUM_CLASSES {
            let section = format!("CLASE{}", ci + 1); // VB6: CLASE1..CLASE12
            // Alto races (Humano, Elfo, Drow) get "Alto" entries
            let alto_races = [race_id::HUMANO, race_id::ELFO, race_id::ELFO_OSCURO];
            // Bajo races (Enano, Gnomo) get "Bajo" entries
            let bajo_races = [race_id::ENANO, race_id::GNOMO];

            for &ri in &alto_races {
                data.faction_armor[ci][ri].armada = [
                    af_get(&section, "DefMinArmyAlto"),
                    af_get(&section, "DefMedArmyAlto"),
                    af_get(&section, "DefAltaArmyAlto"),
                ];
                data.faction_armor[ci][ri].caos = [
                    af_get(&section, "DefMinCaosAlto"),
                    af_get(&section, "DefMedCaosAlto"),
                    af_get(&section, "DefAltaCaosAlto"),
                ];
            }
            for &ri in &bajo_races {
                data.faction_armor[ci][ri].armada = [
                    af_get(&section, "DefMinArmyBajo"),
                    af_get(&section, "DefMedArmyBajo"),
                    af_get(&section, "DefAltaArmyBajo"),
                ];
                data.faction_armor[ci][ri].caos = [
                    af_get(&section, "DefMinCaosBajo"),
                    af_get(&section, "DefMedCaosBajo"),
                    af_get(&section, "DefAltaCaosBajo"),
                ];
            }
        }
        tracing::info!("ArmadurasFaccionarias.dat loaded — {} classes x {} races", NUM_CLASSES, NUM_RACES);
    } else {
        tracing::warn!("ArmadurasFaccionarias.dat not found");
    }

    tracing::info!("Balance data loaded — {} classes, {} races",
        NUM_CLASSES, NUM_RACES);
    Ok(data)
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn load_real_balance() {
        let base = Path::new(env!("CARGO_MANIFEST_DIR")).join("server");
        let base = base.as_path();
        if !base.join("dat").join("Balance.dat").exists() {
            return;
        }
        let bal = load_balance(base).unwrap();

        // 13.3 Balance.dat: Guerrero evasion = 1.0
        assert!((bal.mod_evasion[class_id::GUERRERO] - 1.0).abs() < 0.01);
        // Cazador evasion = 0.9
        assert!((bal.mod_evasion[class_id::CAZADOR] - 0.9).abs() < 0.01);
        // Pirata evasion = 1.25
        assert!((bal.mod_evasion[class_id::PIRATA] - 1.25).abs() < 0.01);
        // MODRAZA: Humano STR = +1
        assert_eq!(bal.mod_raza[race_id::HUMANO].fuerza, 1);
        // MODRAZA: Gnomo INT = +4
        assert_eq!(bal.mod_raza[race_id::GNOMO].inteligencia, 4);
    }
}
