// Data module — filesystem-based game data loaders.
//
// VB6-parity stubs: many struct fields mirror VB6 data but aren't all consumed yet.
#![allow(dead_code)]
//
// Handles loading of all static game data at startup:
// - Experience table (dat/Experiencia.dat)
// - Objects database (dat/Obj.dat)
// - Spells database (dat/Hechizos.dat)
// - NPC database (dat/NPCs.dat, dat/NPCs-HOSTILES.dat)
// - Maps (maps/MapaN.map, .inf, .dat)
// - Balance configuration (dat/Balance.dat)
// - Crafting recipes (dat/ObjCarpintero.dat, etc.)
//
// Accounts, characters, guilds, and bans are handled by the db module (PostgreSQL).

pub mod balance;
pub mod crafting;
pub mod experience;
pub mod maps;
pub mod npcs;
pub mod objects;
pub mod spells;
pub mod zones;

use std::path::Path;

/// All game data loaded at startup.
pub struct GameData {
    pub experience: Vec<i64>,
    pub objects: Vec<objects::ObjData>,
    pub spells: Vec<spells::SpellData>,
    pub npcs: npcs::NpcDatabase,
    pub maps: Vec<Option<maps::GameMap>>,
    pub balance: balance::BalanceData,
    pub crafting: crafting::CraftingData,
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
        let balance = balance::load_balance(base).unwrap_or_default();
        let crafting = crafting::load_crafting(base).unwrap_or_default();

        // VB6: Doors with cerrada=0 (open) should have their tiles unblocked on startup.
        // The .map file may have blocked=true for tiles where open doors exist.
        // Fix: iterate all tiles, check .inf ObjIndex against objects DB, unblock open doors.
        let mut doors_fixed = 0;
        for game_map in maps.iter_mut().flatten() {
            let w = game_map.tiles.width;
            let h = game_map.tiles.height;
            for y in 0..h {
                for x in 0..w {
                    let oi = game_map
                        .tiles
                        .get(x, y)
                        .map(|t| t.obj.obj_index as usize)
                        .unwrap_or(0);
                    if oi >= 1 {
                        if let Some(obj) = objects.get(oi - 1) {
                            // Door type (ObjType 6) with cerrada=0 means open → unblock
                            let is_blocked =
                                game_map.tiles.get(x, y).map(|t| t.blocked).unwrap_or(false);
                            if obj.obj_type == objects::ObjType::Door
                                && obj.cerrada == 0
                                && is_blocked
                            {
                                if let Some(tile) = game_map.tiles.get_mut(x, y) {
                                    tile.blocked = false;
                                    tile.original_blocked = false;
                                }
                                doors_fixed += 1;
                                // Also unblock adjacent tiles for double/grand doors
                                if obj.puerta_doble == 1 {
                                    // Double door: x-1, x+1, x+2
                                    for dx in &[-1i32, 1, 2] {
                                        let nx = x as i32 + dx;
                                        if nx >= 0 && (nx as usize) < w {
                                            if let Some(adj) =
                                                game_map.tiles.get_mut(nx as usize, y)
                                            {
                                                adj.blocked = false;
                                                adj.original_blocked = false;
                                            }
                                        }
                                    }
                                } else if obj.porton == 1 || obj.reja_forta == 1 {
                                    // Grand/fortress gate: x-1, x-2, x+1, x+2
                                    for dx in &[-2i32, -1, 1, 2] {
                                        let nx = x as i32 + dx;
                                        if nx >= 0 && (nx as usize) < w {
                                            if let Some(adj) =
                                                game_map.tiles.get_mut(nx as usize, y)
                                            {
                                                adj.blocked = false;
                                                adj.original_blocked = false;
                                            }
                                        }
                                    }
                                } else {
                                    // Single door: x-1
                                    if x > 0 {
                                        if let Some(adj) = game_map.tiles.get_mut(x - 1, y) {
                                            adj.blocked = false;
                                            adj.original_blocked = false;
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
            }
        }
        if doors_fixed > 0 {
            tracing::info!(
                "Fixed {} open door tiles that were incorrectly blocked",
                doors_fixed
            );
        }

        let map_count = maps.iter().filter(|m| m.is_some()).count();
        tracing::info!(
            "Game data loaded: {} levels, {} objects, {} spells, {} NPCs, {} maps",
            experience.len(),
            objects.len(),
            spells.len(),
            npcs.count(),
            map_count
        );

        Ok(Self {
            experience,
            objects,
            spells,
            npcs,
            maps,
            balance,
            crafting,
        })
    }
}
