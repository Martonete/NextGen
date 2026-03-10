// Invocation spawn list loader — dat/Invokar.dat
//
// VB6: CargarSpawnList (FileIO.bas). Used by GM /CC command to spawn creatures.
// Format: [INIT] NumNPCs=N, [LIST] NI1..NIN = NPC index, NN1..NNN = NPC name.

use std::path::Path;
use crate::config::IniFile;

/// A creature entry in the spawn list (Invokar.dat).
#[derive(Debug, Clone)]
pub struct SpawnListEntry {
    pub npc_index: i32,
    pub npc_name: String,
}

/// Load the spawn list from dat/Invokar.dat.
pub fn load_spawn_list(base: &Path) -> Result<Vec<SpawnListEntry>, String> {
    let path = base.join("dat").join("Invokar.dat");
    if !path.exists() {
        tracing::warn!("Invokar.dat not found at {}, using empty spawn list", path.display());
        return Ok(Vec::new());
    }

    let ini = IniFile::load(&path)
        .map_err(|e| format!("Failed to load Invokar.dat: {}", e))?;

    let num_npcs = ini.get("INIT", "NumNPCs")
        .and_then(|v| v.parse::<i32>().ok())
        .unwrap_or(0);

    let mut entries = Vec::with_capacity(num_npcs as usize);
    for i in 1..=num_npcs {
        let npc_index = ini.get("LIST", &format!("NI{}", i))
            .and_then(|v| v.parse::<i32>().ok())
            .unwrap_or(0);
        let npc_name = ini.get("LIST", &format!("NN{}", i))
            .unwrap_or_default();

        if npc_index > 0 {
            entries.push(SpawnListEntry {
                npc_index,
                npc_name,
            });
        }
    }

    tracing::info!("Loaded {} spawn list entries from Invokar.dat", entries.len());
    Ok(entries)
}
