// Data module — filesystem-based database.
//
// Handles loading and saving of all game data:
// - Account files (Accounts/*.act)
// - Character files (charfile/*.chr)
// - Ban lists (Dat/BanHds.dat, Dat/BanIps.dat)
// - Experience table (Dat/Experiencia.dat)
// - Objects database (Dat/Obj.dat)
// - Spells database (Dat/Hechizos.dat)
// - NPC database (Dat/NPCs.dat, Dat/NPCs-HOSTILES.dat)
// - Maps (Maps/MapaN.map, .inf, .dat)

pub mod accounts;
pub mod charfile;
pub mod bans;
pub mod experience;
pub mod objects;
pub mod spells;
pub mod npcs;
pub mod maps;
pub mod guilds;
pub mod quests;
pub mod balance;
pub mod ranking;

use std::path::Path;

/// All game data loaded at startup.
pub struct GameData {
    pub experience: Vec<i64>,
    pub objects: Vec<objects::ObjData>,
    pub spells: Vec<spells::SpellData>,
    pub npcs: npcs::NpcDatabase,
    pub maps: Vec<Option<maps::GameMap>>,
    pub quests: Vec<quests::QuestData>,
    pub balance: balance::BalanceData,
}

impl GameData {
    /// Load all game data from the server's base directory.
    pub fn load(base: &Path) -> Result<Self, String> {
        tracing::info!("Loading game data...");

        let experience = experience::load_experience_table(base)?;
        let objects = objects::load_objects(base)?;
        let spells = spells::load_spells(base)?;
        let npcs = npcs::load_npcs(base)?;
        let maps = maps::load_all_maps(base)?;
        let quests = quests::load_quests(base).unwrap_or_default();
        let balance = balance::load_balance(base).unwrap_or_default();

        let map_count = maps.iter().filter(|m| m.is_some()).count();
        tracing::info!(
            "Game data loaded: {} levels, {} objects, {} spells, {} NPCs, {} maps, {} quests",
            experience.len(), objects.len(), spells.len(), npcs.count(), map_count, quests.len()
        );

        Ok(Self { experience, objects, spells, npcs, maps, quests, balance })
    }
}
