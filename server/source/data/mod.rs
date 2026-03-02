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
        let mut maps = maps::load_all_maps(base)?;
        let quests = quests::load_quests(base).unwrap_or_default();
        let balance = balance::load_balance(base).unwrap_or_default();

        // VB6: Doors with cerrada=0 (open) should have their tiles unblocked on startup.
        // The .map file may have blocked=true for tiles where open doors exist.
        // Fix: iterate all tiles, check .inf ObjIndex against objects DB, unblock open doors.
        let mut doors_fixed = 0;
        for game_map in maps.iter_mut().flatten() {
            for y in 0..maps::MAP_HEIGHT {
                for x in 0..maps::MAP_WIDTH {
                    let oi = game_map.tiles[y][x].obj.obj_index as usize;
                    if oi >= 1 {
                        if let Some(obj) = objects.get(oi - 1) {
                            // Door type (ObjType 6) with cerrada=0 means open → unblock
                            if obj.obj_type == objects::ObjType::Door && obj.cerrada == 0 && game_map.tiles[y][x].blocked {
                                game_map.tiles[y][x].blocked = false;
                                game_map.tiles[y][x].original_blocked = false;
                                doors_fixed += 1;
                                // Also unblock adjacent tiles for double/grand doors
                                if obj.puerta_doble == 1 {
                                    // Double door: x-1, x+1, x+2
                                    for dx in &[-1i32, 1, 2] {
                                        let nx = x as i32 + dx;
                                        if nx >= 0 && (nx as usize) < maps::MAP_WIDTH {
                                            game_map.tiles[y][nx as usize].blocked = false;
                                            game_map.tiles[y][nx as usize].original_blocked = false;
                                        }
                                    }
                                } else if obj.porton == 1 || obj.reja_forta == 1 {
                                    // Grand/fortress gate: x-1, x-2, x+1, x+2
                                    for dx in &[-2i32, -1, 1, 2] {
                                        let nx = x as i32 + dx;
                                        if nx >= 0 && (nx as usize) < maps::MAP_WIDTH {
                                            game_map.tiles[y][nx as usize].blocked = false;
                                            game_map.tiles[y][nx as usize].original_blocked = false;
                                        }
                                    }
                                } else {
                                    // Single door: x-1
                                    if x > 0 {
                                        game_map.tiles[y][x - 1].blocked = false;
                                        game_map.tiles[y][x - 1].original_blocked = false;
                                    }
                                }
                            }
                        }
                    }
                }
            }
        }
        if doors_fixed > 0 {
            tracing::info!("Fixed {} open door tiles that were incorrectly blocked", doors_fixed);
        }

        let map_count = maps.iter().filter(|m| m.is_some()).count();
        tracing::info!(
            "Game data loaded: {} levels, {} objects, {} spells, {} NPCs, {} maps, {} quests",
            experience.len(), objects.len(), spells.len(), npcs.count(), map_count, quests.len()
        );

        Ok(Self { experience, objects, spells, npcs, maps, quests, balance })
    }
}
