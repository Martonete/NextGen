// Quest database loader — Dat/QUESTS.DAT
//
// INI format with [Init] Num=31 and [Quest1]..[Quest31] sections.
// Two types: 1=Kill NPC, 2=Kill Players.

use std::path::Path;
use crate::config::IniFile;

/// Quest type
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum QuestType {
    KillNpc = 1,
    KillPlayers = 2,
}

/// Quest definition loaded from QUESTS.DAT
#[derive(Debug, Clone)]
pub struct QuestData {
    pub index: usize,
    pub name: String,
    pub quest_type: QuestType,
    pub mata_npc: i32,      // NPC type to kill (Tipo=1)
    pub cant_npc: i32,      // How many NPCs to kill (Tipo=1)
    pub usuarios: i32,      // How many players to kill (Tipo=2)
    pub oro: i64,           // Gold reward
    pub pts_torneo: i32,    // Tournament points reward
    pub pts_ts: i32,        // TS seasonal points
    pub creditos: i32,      // Donation credits
}

/// Load quests database
pub fn load_quests(base: &Path) -> Result<Vec<QuestData>, String> {
    let path = base.join("Dat").join("QUESTS.DAT");
    let ini = IniFile::load(&path)
        .map_err(|e| format!("Failed to load QUESTS.DAT: {}", e))?;

    let num: usize = ini.get("Init", "Num")
        .or_else(|| ini.get("INIT", "Num"))
        .and_then(|s| s.parse().ok())
        .unwrap_or(0);

    let mut quests = Vec::with_capacity(num);

    for i in 1..=num {
        let section = format!("Quest{}", i);

        let tipo: i32 = ini.get(&section, "Tipo").and_then(|s| s.parse().ok()).unwrap_or(1);
        let quest_type = if tipo == 2 { QuestType::KillPlayers } else { QuestType::KillNpc };

        quests.push(QuestData {
            index: i,
            name: ini.get(&section, "Name").unwrap_or_default(),
            quest_type,
            mata_npc: ini.get(&section, "MataNPC").and_then(|s| s.parse().ok()).unwrap_or(0),
            cant_npc: ini.get(&section, "Cant").and_then(|s| s.parse().ok()).unwrap_or(0),
            usuarios: ini.get(&section, "Usuarios").and_then(|s| s.parse().ok()).unwrap_or(0),
            oro: ini.get(&section, "Oro").and_then(|s| s.parse().ok()).unwrap_or(0),
            pts_torneo: ini.get(&section, "ptsTorneo").and_then(|s| s.parse().ok()).unwrap_or(0),
            pts_ts: ini.get(&section, "ptsTS").and_then(|s| s.parse().ok()).unwrap_or(0),
            creditos: ini.get(&section, "Creditos").and_then(|s| s.parse().ok()).unwrap_or(0),
        });
    }

    tracing::info!("Quests loaded: {} quests", quests.len());
    Ok(quests)
}

/// Get the kill target for a quest
pub fn quest_target(quest: &QuestData) -> i32 {
    match quest.quest_type {
        QuestType::KillNpc => quest.cant_npc,
        QuestType::KillPlayers => quest.usuarios,
    }
}
