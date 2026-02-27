// Game balance data loader — Dat/Balance.dat + Dat/Vidas.dat
//
// Balance.dat: per-class combat modifiers (attack %, defense %, evasion multiplier, etc.)
//              plus class-vs-class AF/AM matrices.
// Vidas.dat:   per-class per-race HP min/max ranges for level-up.
//
// Matches VB6 modBalance.bas LoadBalance().

use std::path::Path;
use crate::config::IniFile;

/// Number of classes in the game (Guerrero..Druida = 9).
pub const NUM_CLASSES: usize = 9;

/// Class indices matching VB6 order.
pub mod class_id {
    pub const GUERRERO: usize = 0;
    pub const CAZADOR: usize = 1;
    pub const PALADIN: usize = 2;
    pub const ASESINO: usize = 3;
    pub const LADRON: usize = 4;
    pub const BARDO: usize = 5;
    pub const CLERIGO: usize = 6;
    pub const MAGO: usize = 7;
    pub const DRUIDA: usize = 8;
}

/// Race indices for HP table.
pub mod race_id {
    pub const HUMANO: usize = 0;
    pub const ELFO: usize = 1;
    pub const ELFO_OSCURO: usize = 2;
    pub const GNOMO: usize = 3;
    pub const ENANO: usize = 4;
}

pub const NUM_RACES: usize = 5;

/// Class names as they appear in INI files (section keys).
const CLASS_NAMES: [&str; NUM_CLASSES] = [
    "Guerrero", "Cazador", "Paladin", "Asesino", "Ladron", "Bardo", "Clerigo", "Mago", "Druida",
];

/// Class section names (uppercase, for class-vs-class AF/AM matrices).
const CLASS_SECTIONS: [&str; NUM_CLASSES] = [
    "GUERRERO", "CAZADOR", "PALADIN", "ASESINO", "LADRON", "BARDO", "CLERIGO", "MAGO", "DRUIDA",
];

/// Race names as they appear in Vidas.dat.
const RACE_NAMES: [&str; NUM_RACES] = [
    "Humano", "Elfo", "ElfoOscuro", "Gnomo", "Enano",
];

/// Per-class per-race HP range for level-up (min, max).
#[derive(Debug, Clone, Copy, Default)]
pub struct HpRange {
    pub min: i32,
    pub max: i32,
}

/// All combat balance data.
#[derive(Debug, Clone)]
pub struct BalanceData {
    /// Physical attack % modifier per class (e.g., -15 for Guerrero).
    pub ataque_fisico: [f32; NUM_CLASSES],
    /// Magic attack % modifier per class.
    pub ataque_magico: [f32; NUM_CLASSES],
    /// Physical defense % modifier per class.
    pub defensa_fisica: [f32; NUM_CLASSES],
    /// Magic defense % modifier per class.
    pub defensa_magica: [f32; NUM_CLASSES],
    /// Projectile attack % modifier per class.
    pub ataque_proyectil: [f32; NUM_CLASSES],
    /// Evasion multiplier per class (0.0-1.0).
    pub mod_evasion: [f32; NUM_CLASSES],
    /// Weapon attack power multiplier per class.
    pub mod_poder_ataque_armas: [f32; NUM_CLASSES],
    /// Projectile attack power multiplier per class.
    pub mod_poder_ataque_proyectiles: [f32; NUM_CLASSES],
    /// Weapon damage class multiplier.
    pub mod_dano_clase_armas: [f32; NUM_CLASSES],
    /// Projectile damage class multiplier.
    pub mod_dano_clase_proyectiles: [f32; NUM_CLASSES],
    /// Shield evasion bonus per class.
    pub mod_evasion_escudo: [f32; NUM_CLASSES],

    /// Class-vs-class physical attack modifier (AF). [attacker][victim] = %.
    pub af_clase_vs_clase: [[f32; NUM_CLASSES]; NUM_CLASSES],
    /// Class-vs-class magic attack modifier (AM). [attacker][victim] = %.
    pub am_clase_vs_clase: [[f32; NUM_CLASSES]; NUM_CLASSES],

    /// HP ranges per class per race. [class][race] = HpRange.
    pub vidas: [[HpRange; NUM_RACES]; NUM_CLASSES],
}

impl Default for BalanceData {
    fn default() -> Self {
        Self {
            ataque_fisico: [0.0; NUM_CLASSES],
            ataque_magico: [0.0; NUM_CLASSES],
            defensa_fisica: [0.0; NUM_CLASSES],
            defensa_magica: [0.0; NUM_CLASSES],
            ataque_proyectil: [0.0; NUM_CLASSES],
            mod_evasion: [1.0; NUM_CLASSES],
            mod_poder_ataque_armas: [1.0; NUM_CLASSES],
            mod_poder_ataque_proyectiles: [1.0; NUM_CLASSES],
            mod_dano_clase_armas: [1.0; NUM_CLASSES],
            mod_dano_clase_proyectiles: [1.0; NUM_CLASSES],
            mod_evasion_escudo: [1.0; NUM_CLASSES],
            af_clase_vs_clase: [[0.0; NUM_CLASSES]; NUM_CLASSES],
            am_clase_vs_clase: [[0.0; NUM_CLASSES]; NUM_CLASSES],
            vidas: [[HpRange::default(); NUM_RACES]; NUM_CLASSES],
        }
    }
}

/// Convert class name string to class index.
pub fn class_name_to_index(name: &str) -> Option<usize> {
    let upper = name.to_uppercase();
    match upper.as_str() {
        "GUERRERO" => Some(class_id::GUERRERO),
        "CAZADOR" => Some(class_id::CAZADOR),
        "PALADIN" | "PALADÍN" => Some(class_id::PALADIN),
        "ASESINO" => Some(class_id::ASESINO),
        "LADRON" | "LADRÓN" => Some(class_id::LADRON),
        "BARDO" => Some(class_id::BARDO),
        "CLERIGO" | "CLÉRIGO" => Some(class_id::CLERIGO),
        "MAGO" => Some(class_id::MAGO),
        "DRUIDA" => Some(class_id::DRUIDA),
        _ => None,
    }
}

/// Convert race name string to race index.
pub fn race_name_to_index(name: &str) -> Option<usize> {
    let upper = name.to_uppercase();
    match upper.as_str() {
        "HUMANO" => Some(race_id::HUMANO),
        "ELFO" => Some(race_id::ELFO),
        "ELFO OSCURO" | "ELFOOSCURO" => Some(race_id::ELFO_OSCURO),
        "GNOMO" => Some(race_id::GNOMO),
        "ENANO" => Some(race_id::ENANO),
        _ => None,
    }
}

impl BalanceData {
    /// Get physical attack % modifier for a class name.
    pub fn modificar_ataque_fisico(&self, class: &str) -> f32 {
        class_name_to_index(class)
            .map(|i| self.ataque_fisico[i])
            .unwrap_or(0.0)
    }

    /// Get magic attack % modifier for a class name.
    pub fn modificar_ataque_magico(&self, class: &str) -> f32 {
        class_name_to_index(class)
            .map(|i| self.ataque_magico[i])
            .unwrap_or(0.0)
    }

    /// Get physical defense % modifier for a class name.
    pub fn modificar_defensa_fisica(&self, class: &str) -> f32 {
        class_name_to_index(class)
            .map(|i| self.defensa_fisica[i])
            .unwrap_or(0.0)
    }

    /// Get magic defense % modifier for a class name.
    pub fn modificar_defensa_magica(&self, class: &str) -> f32 {
        class_name_to_index(class)
            .map(|i| self.defensa_magica[i])
            .unwrap_or(0.0)
    }

    /// Get projectile attack % modifier for a class name.
    pub fn modificar_ataque_proyectil(&self, class: &str) -> f32 {
        class_name_to_index(class)
            .map(|i| self.ataque_proyectil[i])
            .unwrap_or(0.0)
    }

    /// Get class-vs-class AF modifier (attacker class vs victim class).
    pub fn af_clase_vs(&self, attacker: &str, victim: &str) -> f32 {
        match (class_name_to_index(attacker), class_name_to_index(victim)) {
            (Some(a), Some(v)) => self.af_clase_vs_clase[a][v],
            _ => 0.0,
        }
    }

    /// Get class-vs-class AM modifier (attacker class vs victim class).
    pub fn am_clase_vs(&self, attacker: &str, victim: &str) -> f32 {
        match (class_name_to_index(attacker), class_name_to_index(victim)) {
            (Some(a), Some(v)) => self.am_clase_vs_clase[a][v],
            _ => 0.0,
        }
    }

    /// Get HP range for level-up given class and race names.
    pub fn hp_range(&self, class: &str, race: &str) -> HpRange {
        match (class_name_to_index(class), race_name_to_index(race)) {
            (Some(c), Some(r)) => self.vidas[c][r],
            _ => HpRange { min: 1, max: 1 },
        }
    }
}

/// Load Balance.dat and Vidas.dat.
pub fn load_balance(base: &Path) -> Result<BalanceData, String> {
    let mut data = BalanceData::default();

    // --- Load Balance.dat ---
    let balance_path = base.join("Dat").join("Balance.dat");
    if let Ok(ini) = IniFile::load(&balance_path) {
        let get_f32 = |section: &str, key: &str| -> f32 {
            ini.get(section, key)
                .and_then(|s| s.replace(',', ".").parse::<f32>().ok())
                .unwrap_or(0.0)
        };

        // Per-class modifiers
        for i in 0..NUM_CLASSES {
            let name = CLASS_NAMES[i];
            data.ataque_fisico[i] = get_f32("AtaqueFisico", name);
            data.ataque_magico[i] = get_f32("AtaqueMagico", name);
            data.defensa_fisica[i] = get_f32("DefensaFisica", name);
            data.defensa_magica[i] = get_f32("DefensaMagica", name);
            data.ataque_proyectil[i] = get_f32("AtaqueProyectil", name);
            data.mod_evasion[i] = get_f32("ModificadorEvasion", name);
            data.mod_poder_ataque_armas[i] = get_f32("ModificadorPoderAtaqueArmas", name);
            data.mod_poder_ataque_proyectiles[i] = get_f32("ModificadorPoderAtaqueProyectiles", name);
            // VB6 uses "ModicadorDa\xf1oClaseArmas" (with typo and ñ)
            data.mod_dano_clase_armas[i] = get_f32("ModicadorDa\u{00f1}oClaseArmas", name);
            if data.mod_dano_clase_armas[i] == 0.0 {
                // Try without the ñ (in case INI parser normalized it)
                data.mod_dano_clase_armas[i] = get_f32("ModicadorDanoClaseArmas", name);
            }
            data.mod_dano_clase_proyectiles[i] = get_f32("ModicadorDa\u{00f1}oClaseProyectiles", name);
            if data.mod_dano_clase_proyectiles[i] == 0.0 {
                data.mod_dano_clase_proyectiles[i] = get_f32("ModicadorDanoClaseProyectiles", name);
            }
            data.mod_evasion_escudo[i] = get_f32("ModEvasionDeEscudoClase", name);

            // Default multipliers to 1.0 if loaded as 0.0
            if data.mod_evasion[i] == 0.0 { data.mod_evasion[i] = 1.0; }
            if data.mod_poder_ataque_armas[i] == 0.0 { data.mod_poder_ataque_armas[i] = 1.0; }
            if data.mod_poder_ataque_proyectiles[i] == 0.0 { data.mod_poder_ataque_proyectiles[i] = 1.0; }
            if data.mod_dano_clase_armas[i] == 0.0 { data.mod_dano_clase_armas[i] = 1.0; }
            if data.mod_dano_clase_proyectiles[i] == 0.0 { data.mod_dano_clase_proyectiles[i] = 1.0; }
            if data.mod_evasion_escudo[i] == 0.0 { data.mod_evasion_escudo[i] = 1.0; }
        }

        // Class-vs-class AF/AM matrices
        // Sections: GUERRERO, CAZADOR, PALADIN, etc.
        // Keys: AFMago, AFGuerrero, ..., AMMago, AMGuerrero, ...
        for attacker in 0..NUM_CLASSES {
            let section = CLASS_SECTIONS[attacker];
            for victim in 0..NUM_CLASSES {
                let victim_name = CLASS_NAMES[victim];
                let af_key = format!("AF{}", victim_name);
                let am_key = format!("AM{}", victim_name);
                data.af_clase_vs_clase[attacker][victim] = get_f32(section, &af_key);
                data.am_clase_vs_clase[attacker][victim] = get_f32(section, &am_key);
            }
        }
    } else {
        tracing::warn!("Balance.dat not found, using defaults");
    }

    // --- Load Vidas.dat ---
    let vidas_path = base.join("Dat").join("Vidas.dat");
    if let Ok(ini) = IniFile::load(&vidas_path) {
        for class_i in 0..NUM_CLASSES {
            let section = CLASS_SECTIONS[class_i];
            for race_i in 0..NUM_RACES {
                let race_name = RACE_NAMES[race_i];
                if let Some(val) = ini.get(section, race_name) {
                    // Format: "min-max" (e.g., "11-12")
                    let parts: Vec<&str> = val.split('-').collect();
                    let min_hp = parts.first().and_then(|s| s.parse::<i32>().ok()).unwrap_or(1);
                    let max_hp = parts.get(1).and_then(|s| s.parse::<i32>().ok()).unwrap_or(min_hp);
                    data.vidas[class_i][race_i] = HpRange { min: min_hp, max: max_hp };
                }
            }
        }
    } else {
        tracing::warn!("Vidas.dat not found, using defaults");
    }

    tracing::info!("Balance data loaded (Balance.dat + Vidas.dat)");
    Ok(data)
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn load_real_balance() {
        let base = Path::new("/workspace/Tierras-Sagradas-AO/server-rust");
        if !base.join("Dat").join("Balance.dat").exists() {
            return;
        }
        let bal = load_balance(base).unwrap();

        // Guerrero physical attack = -15
        assert_eq!(bal.ataque_fisico[class_id::GUERRERO], -15.0);
        // Druida physical attack = 44
        assert_eq!(bal.ataque_fisico[class_id::DRUIDA], 44.0);
        // Mago evasion = 0.3
        assert!((bal.mod_evasion[class_id::MAGO] - 0.3).abs() < 0.01);

        // Class vs class: Guerrero AFGuerrero = 8
        assert_eq!(bal.af_clase_vs_clase[class_id::GUERRERO][class_id::GUERRERO], 8.0);
        // Paladin AFMago = 22
        assert_eq!(bal.af_clase_vs_clase[class_id::PALADIN][class_id::MAGO], 22.0);

        // Vidas: Guerrero Humano = 11-12
        let hp = bal.vidas[class_id::GUERRERO][race_id::HUMANO];
        assert_eq!(hp.min, 11);
        assert_eq!(hp.max, 12);

        // Mago Gnomo = 5-8
        let hp2 = bal.vidas[class_id::MAGO][race_id::GNOMO];
        assert_eq!(hp2.min, 5);
        assert_eq!(hp2.max, 8);
    }
}
