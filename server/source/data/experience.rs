// Experience table loader — Dat/Experiencia.dat
//
// Format: [EXPERIENCIA] section with Nivel1..NivelN keys.
// Each key value is the EXP required to reach that level.

use std::path::Path;
use crate::config::IniFile;

pub const MAX_LEVEL: usize = 50;

/// Load the experience table from Experiencia.dat.
/// Returns a Vec where index 0 = level 1 exp, index 1 = level 2 exp, etc.
pub fn load_experience_table(base: &Path) -> Result<Vec<i64>, String> {
    let path = base.join("Dat").join("Experiencia.dat");
    let ini = IniFile::load(&path)
        .map_err(|e| format!("Failed to load Experiencia.dat: {}", e))?;

    let mut table = Vec::with_capacity(MAX_LEVEL);

    for level in 1..=MAX_LEVEL {
        let key = format!("Nivel{}", level);
        let exp = ini.get("EXPERIENCIA", &key)
            .and_then(|s| s.parse::<i64>().ok())
            .unwrap_or(0);
        table.push(exp);
    }

    tracing::info!("Experience table loaded: {} levels (max exp: {})",
        table.len(),
        table.last().copied().unwrap_or(0)
    );

    Ok(table)
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn load_real_experience_table() {
        let base = Path::new("/workspace/Tierras-Sagradas-AO/server-rust/server");
        if !base.join("Dat").join("Experiencia.dat").exists() {
            return; // Skip if data files not present
        }
        let table = load_experience_table(base).unwrap();
        assert_eq!(table.len(), MAX_LEVEL);
        assert!(table[0] > 0, "Level 1 exp should be > 0");
        // Each level should require more or equal exp than the previous
        for i in 1..table.len() {
            assert!(table[i] >= table[i - 1],
                "Level {} exp ({}) should be >= level {} exp ({})",
                i + 1, table[i], i, table[i - 1]
            );
        }
    }
}
