// Ranking system — Dat/Ranking.dat
//
// Top-10 rankings across 9 categories: Frags, Torneos, Duelos, Parejas,
// Reputacion, Rondas, CVCs, Castillos, RepuClanes.
//
// Matches VB6 Mod_Ranking.bas.

use std::path::Path;
use crate::config::IniFile;

pub const MAX_TOP: usize = 10;
pub const MAX_RANKINGS: usize = 9;

/// Ranking category enum matching VB6 eRanking.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
#[repr(u8)]
pub enum RankingType {
    Frags = 1,
    Torneos = 2,
    Duelos = 3,
    Parejas = 4,
    Reputacion = 5,
    Rondas = 6,
    CVCs = 7,
    Castillos = 8,
    RepuClanes = 9,
}

impl RankingType {
    /// INI section name for this ranking.
    pub fn section_name(&self) -> &'static str {
        match self {
            Self::Frags => "FRAGS",
            Self::Torneos => "TORNEOS",
            Self::Duelos => "DUELOS",
            Self::Parejas => "PAREJAS",
            Self::Reputacion => "REPUTACION",
            Self::Rondas => "RONDAS",
            Self::CVCs => "CVCS",
            Self::Castillos => "CASTILLOS",
            Self::RepuClanes => "REPUCLAN",
        }
    }

    /// All ranking types for iteration.
    pub fn all() -> &'static [RankingType] {
        &[
            Self::Frags, Self::Torneos, Self::Duelos, Self::Parejas,
            Self::Reputacion, Self::Rondas, Self::CVCs, Self::Castillos,
            Self::RepuClanes,
        ]
    }

    /// From 1-indexed integer (matches VB6 eRanking).
    pub fn from_i32(v: i32) -> Option<Self> {
        match v {
            1 => Some(Self::Frags),
            2 => Some(Self::Torneos),
            3 => Some(Self::Duelos),
            4 => Some(Self::Parejas),
            5 => Some(Self::Reputacion),
            6 => Some(Self::Rondas),
            7 => Some(Self::CVCs),
            8 => Some(Self::Castillos),
            9 => Some(Self::RepuClanes),
            _ => None,
        }
    }
}

/// A single ranking entry (name + value).
#[derive(Debug, Clone, Default)]
pub struct RankingEntry {
    pub name: String,
    pub value: i64,
}

/// A single ranking category (top 10).
#[derive(Debug, Clone)]
pub struct RankingTop {
    pub entries: [RankingEntry; MAX_TOP],
}

impl Default for RankingTop {
    fn default() -> Self {
        Self {
            entries: Default::default(),
        }
    }
}

/// All rankings (9 categories).
#[derive(Debug, Clone)]
pub struct RankingData {
    /// 1-indexed (0 unused) to match VB6 eRanking.
    pub rankings: [RankingTop; MAX_RANKINGS + 1],
}

impl Default for RankingData {
    fn default() -> Self {
        Self {
            rankings: Default::default(),
        }
    }
}

impl RankingData {
    /// Get ranking entries for a category.
    pub fn get(&self, rank: RankingType) -> &RankingTop {
        &self.rankings[rank as usize]
    }

    /// Get mutable ranking entries for a category.
    pub fn get_mut(&mut self, rank: RankingType) -> &mut RankingTop {
        &mut self.rankings[rank as usize]
    }

    /// Check if a name appears in any top-3 ranking. Returns best position (1-3) or 99.
    pub fn tiene_ranking(&self, name: &str) -> u8 {
        let upper = name.to_uppercase();
        let mut best = 99u8;
        for rt in RankingType::all() {
            let top = self.get(*rt);
            for i in 0..3 {
                if top.entries[i].name.to_uppercase() == upper && (i as u8 + 1) < best {
                    best = i as u8 + 1;
                }
            }
        }
        best
    }

    /// Update a user's ranking position. Inserts/shifts as needed (bubble sort).
    /// Returns true if a change was made.
    pub fn check_ranking_user(&mut self, name: &str, value: i64, rank: RankingType) -> bool {
        let top = self.get_mut(rank);
        let upper = name.to_uppercase();

        // Check if already in ranking
        for i in 0..MAX_TOP {
            if top.entries[i].name.to_uppercase() == upper {
                if value > top.entries[i].value {
                    // Update value and bubble up
                    top.entries[i].value = value;
                    top.entries[i].name = name.to_string();
                    // Bubble up
                    let mut pos = i;
                    while pos > 0 && top.entries[pos].value > top.entries[pos - 1].value {
                        top.entries.swap(pos, pos - 1);
                        pos -= 1;
                    }
                    return true;
                }
                return false;
            }
        }

        // Not in ranking — check if qualifies
        if value > top.entries[MAX_TOP - 1].value || top.entries[MAX_TOP - 1].name.is_empty() {
            // Insert at bottom and bubble up
            top.entries[MAX_TOP - 1] = RankingEntry {
                name: name.to_string(),
                value,
            };
            let mut pos = MAX_TOP - 1;
            while pos > 0 && top.entries[pos].value > top.entries[pos - 1].value {
                top.entries.swap(pos, pos - 1);
                pos -= 1;
            }
            return true;
        }

        false
    }

    /// Update a clan's ranking (same logic, but uses clan name).
    pub fn check_ranking_clan(&mut self, clan_name: &str, value: i64, rank: RankingType) -> bool {
        self.check_ranking_user(clan_name, value, rank)
    }
}

/// Load all rankings from Dat/Ranking.dat.
pub fn load_ranking(base: &Path) -> RankingData {
    let mut data = RankingData::default();

    let path = base.join("Dat").join("Ranking.dat");
    let ini = match IniFile::load(&path) {
        Ok(ini) => ini,
        Err(_) => {
            tracing::warn!("Ranking.dat not found, starting with empty rankings");
            return data;
        }
    };

    for rt in RankingType::all() {
        let section = rt.section_name();
        let top = data.get_mut(*rt);
        for i in 0..MAX_TOP {
            let key = format!("Top{}", i + 1);
            if let Some(val) = ini.get(section, &key) {
                // Format: "NAME-VALUE" (e.g., "OWEN THIZ-963")
                // Split on last '-' since names can contain hyphens
                if let Some(last_dash) = val.rfind('-') {
                    let name = &val[..last_dash];
                    let value: i64 = val[last_dash + 1..].parse().unwrap_or(0);
                    top.entries[i] = RankingEntry {
                        name: name.to_string(),
                        value,
                    };
                }
            }
        }
    }

    let total: usize = data.rankings.iter()
        .flat_map(|t| t.entries.iter())
        .filter(|e| !e.name.is_empty())
        .count();
    tracing::info!("Rankings loaded: {} entries across {} categories", total, MAX_RANKINGS);

    data
}

/// Save a single ranking to Dat/Ranking.dat.
pub fn save_ranking(base: &Path, data: &RankingData, rank: RankingType) {
    let path = base.join("Dat").join("Ranking.dat");
    let path_str = path.to_string_lossy();
    let section = rank.section_name();
    let top = data.get(rank);

    for i in 0..MAX_TOP {
        let key = format!("Top{}", i + 1);
        let entry = &top.entries[i];
        let value = if entry.name.is_empty() {
            "N/A-0".to_string()
        } else {
            format!("{}-{}", entry.name, entry.value)
        };
        if let Err(e) = crate::config::write_var(&path_str, section, &key, &value) {
            tracing::error!("Failed to save ranking {}/{}: {}", section, key, e);
        }
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn load_real_ranking() {
        let base = Path::new("/workspace/Tierras-Sagradas-AO/server-rust/server");
        if !base.join("Dat").join("Ranking.dat").exists() {
            return;
        }
        let data = load_ranking(base);

        // FRAGS top1 = OWEN THIZ with 963
        let frags = data.get(RankingType::Frags);
        assert_eq!(frags.entries[0].name, "OWEN THIZ");
        assert_eq!(frags.entries[0].value, 963);

        // DUELOS top1 = THE UNKNOWN with 978
        let duelos = data.get(RankingType::Duelos);
        assert_eq!(duelos.entries[0].name, "THE UNKNOWN");
        assert_eq!(duelos.entries[0].value, 978);
    }

    #[test]
    fn ranking_insert_and_bubble() {
        let mut data = RankingData::default();

        // Insert first entry
        assert!(data.check_ranking_user("Alice", 100, RankingType::Frags));
        assert_eq!(data.get(RankingType::Frags).entries[0].name, "Alice");

        // Insert higher entry — should bubble to top
        assert!(data.check_ranking_user("Bob", 200, RankingType::Frags));
        assert_eq!(data.get(RankingType::Frags).entries[0].name, "Bob");
        assert_eq!(data.get(RankingType::Frags).entries[1].name, "Alice");

        // Update existing entry
        assert!(data.check_ranking_user("Alice", 300, RankingType::Frags));
        assert_eq!(data.get(RankingType::Frags).entries[0].name, "Alice");
        assert_eq!(data.get(RankingType::Frags).entries[0].value, 300);
    }

    #[test]
    fn tiene_ranking_check() {
        let mut data = RankingData::default();
        data.check_ranking_user("TopPlayer", 999, RankingType::Frags);
        assert_eq!(data.tiene_ranking("TopPlayer"), 1);
        assert_eq!(data.tiene_ranking("Nobody"), 99);
    }
}
