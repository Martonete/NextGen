// Guild data — file-based guild storage (guilds/ directory)
//
// Matches VB6 clsClan.cls and modGuilds.bas.
// All guild data is stored in INI files under the guilds/ directory.
// Data is read from disk on access to avoid sync issues (VB6 pattern).

use std::path::Path;
use crate::config::IniFile;

pub const MAX_GUILDS: i32 = 1000;
pub const MAX_CODEX_LINES: usize = 8;
pub const MAX_ASPIRANTES: usize = 10;
pub const MAX_ANTIFACCION: i32 = 5;
pub const MAX_GUILD_BANK_SLOTS: usize = 40;
pub const MAX_GUILD_BANK_GOLD: i64 = 999_999_999;

// Guild alignment enum
pub const ALIGN_LEGION: i32 = 1;
pub const ALIGN_CRIMINAL: i32 = 2;
pub const ALIGN_NEUTRO: i32 = 3;
pub const ALIGN_CIUDA: i32 = 4;
pub const ALIGN_ARMADA: i32 = 5;
pub const ALIGN_MASTER: i32 = 6;

// Guild relations
pub const REL_GUERRA: i32 = -1;
pub const REL_PAZ: i32 = 0;
pub const REL_ALIADOS: i32 = 1;

// Member caps per clan level
pub fn max_members_for_level(level: i32) -> i32 {
    level * 4
}

pub fn alignment_name(align: i32) -> &'static str {
    match align {
        ALIGN_LEGION => "Legion oscura",
        ALIGN_CRIMINAL => "Criminal",
        ALIGN_NEUTRO => "Neutro",
        ALIGN_CIUDA => "Legal",
        ALIGN_ARMADA => "Armada Real",
        ALIGN_MASTER => "Game Masters",
        _ => "Neutro",
    }
}

pub fn alignment_from_name(name: &str) -> i32 {
    match name.to_lowercase().as_str() {
        "legion oscura" => ALIGN_LEGION,
        "criminal" => ALIGN_CRIMINAL,
        "neutro" => ALIGN_NEUTRO,
        "legal" => ALIGN_CIUDA,
        "armada real" => ALIGN_ARMADA,
        "game masters" => ALIGN_MASTER,
        _ => ALIGN_NEUTRO,
    }
}

/// Guild info loaded from guildsinfo.inf
#[derive(Debug, Clone)]
pub struct GuildInfo {
    pub guild_number: i32,
    pub name: String,
    pub founder: String,
    pub date: String,
    pub alignment: i32,
    pub antifaccion: i32,
    pub leader: String,
    pub sub_lider1: String,
    pub sub_lider2: String,
    pub url: String,
    pub desc: String,
    pub news: String,
    pub codex: Vec<String>,
    pub nivel_clan: i32,
    pub puntos_clan: i32,
    pub cvc_wins: i32,
    pub cvc_losses: i32,
    pub castle_sieges: i32,
    pub reputation: i32,
    pub elecciones_abiertas: bool,
}

/// Guild applicant
#[derive(Debug, Clone)]
pub struct GuildApplicant {
    pub name: String,
    pub detail: String,
}

/// Guild bank slot
#[derive(Debug, Clone, Default)]
pub struct GuildBankSlot {
    pub obj_index: i32,
    pub amount: i32,
}

/// Get the guild directory path
fn guild_dir(base: &Path) -> std::path::PathBuf {
    base.join("guilds")
}

/// Get the guildsinfo.inf path
fn guilds_info_path(base: &Path) -> std::path::PathBuf {
    guild_dir(base).join("guildsinfo.inf")
}

/// Get total number of guilds
pub fn get_num_guilds(base: &Path) -> i32 {
    let path = guilds_info_path(base);
    if let Ok(ini) = IniFile::load(&path) {
        ini.get("INIT", "nroGuilds")
            .and_then(|s| s.parse().ok())
            .unwrap_or(0)
    } else {
        0
    }
}

/// Load guild info by guild number
pub fn load_guild(base: &Path, guild_num: i32) -> Option<GuildInfo> {
    let path = guilds_info_path(base);
    let ini = IniFile::load(&path).ok()?;
    let section = format!("GUILD{}", guild_num);

    let name = ini.get(&section, "GuildName").unwrap_or_default();
    if name.is_empty() || name.starts_with("cerrado") {
        return None; // Dissolved guild
    }

    let alignment_str = ini.get(&section, "Alineacion").unwrap_or_default();
    let alignment = alignment_from_name(&alignment_str);

    let mut codex = Vec::new();
    for i in 1..=MAX_CODEX_LINES {
        let line = ini.get(&section, &format!("Codex{}", i)).unwrap_or_default();
        codex.push(line);
    }

    Some(GuildInfo {
        guild_number: guild_num,
        name,
        founder: ini.get(&section, "Founder").unwrap_or_default(),
        date: ini.get(&section, "Date").unwrap_or_default(),
        alignment,
        antifaccion: ini.get(&section, "Antifaccion").and_then(|s| s.parse().ok()).unwrap_or(0),
        leader: ini.get(&section, "Leader").unwrap_or_default(),
        sub_lider1: ini.get(&section, "SubLider1").unwrap_or_else(|| "Fermin".to_string()),
        sub_lider2: ini.get(&section, "SubLider2").unwrap_or_else(|| "Fermin".to_string()),
        url: ini.get(&section, "URL").unwrap_or_default(),
        desc: ini.get(&section, "Desc").unwrap_or_default(),
        news: ini.get(&section, "GuildNews").unwrap_or_default(),
        codex,
        nivel_clan: ini.get(&section, "NivelClan").and_then(|s| s.parse().ok()).unwrap_or(1),
        puntos_clan: ini.get(&section, "PuntosClan").and_then(|s| s.parse().ok()).unwrap_or(0),
        cvc_wins: ini.get(&section, "CVCG").and_then(|s| s.parse().ok()).unwrap_or(0),
        cvc_losses: ini.get(&section, "CVCP").and_then(|s| s.parse().ok()).unwrap_or(0),
        castle_sieges: ini.get(&section, "CASTIS").and_then(|s| s.parse().ok()).unwrap_or(0),
        reputation: ini.get(&section, "Repu").and_then(|s| s.parse().ok()).unwrap_or(0),
        elecciones_abiertas: ini.get(&section, "EleccionesAbiertas").map(|s| s == "1").unwrap_or(false),
    })
}

/// Find guild number by name (case-insensitive)
pub fn find_guild_by_name(base: &Path, name: &str) -> Option<i32> {
    let num = get_num_guilds(base);
    let path = guilds_info_path(base);
    let ini = match IniFile::load(&path) {
        Ok(i) => i,
        Err(_) => return None,
    };

    for i in 1..=num {
        let section = format!("GUILD{}", i);
        if let Some(gname) = ini.get(&section, "GuildName") {
            if gname.to_uppercase() == name.to_uppercase() && !gname.starts_with("cerrado") {
                return Some(i);
            }
        }
    }
    None
}

/// Load guild members list
pub fn load_members(base: &Path, guild_name: &str) -> Vec<String> {
    let path = guild_dir(base).join(format!("{}-members.mem", guild_name));
    let ini = match IniFile::load(&path) {
        Ok(i) => i,
        Err(_) => return Vec::new(),
    };

    let count: usize = ini.get("INIT", "NroMembers")
        .and_then(|s| s.parse().ok())
        .unwrap_or(0);

    let mut members = Vec::new();
    for i in 1..=count {
        if let Some(name) = ini.get("Members", &format!("Member{}", i)) {
            if !name.is_empty() {
                members.push(name);
            }
        }
    }
    members
}

/// Load guild applicants
pub fn load_applicants(base: &Path, guild_name: &str) -> Vec<GuildApplicant> {
    let path = guild_dir(base).join(format!("{}-solicitudes.sol", guild_name));
    let ini = match IniFile::load(&path) {
        Ok(i) => i,
        Err(_) => return Vec::new(),
    };

    let count: usize = ini.get("INIT", "CantSolicitudes")
        .and_then(|s| s.parse().ok())
        .unwrap_or(0);

    let mut applicants = Vec::new();
    for i in 1..=count {
        let section = format!("SOLICITUD{}", i);
        let name = ini.get(&section, "Nombre").unwrap_or_default();
        let detail = ini.get(&section, "Detalle").unwrap_or_default();
        if !name.is_empty() {
            applicants.push(GuildApplicant { name, detail });
        }
    }
    applicants
}

/// Save guild info to guildsinfo.inf
pub fn save_guild(base: &Path, guild: &GuildInfo) {
    let path = guilds_info_path(base);
    let section = format!("GUILD{}", guild.guild_number);

    // Ensure guilds directory exists
    let _ = std::fs::create_dir_all(guild_dir(base));

    let mut ini = IniFile::load(&path).unwrap_or_default();
    ini.set(&section, "GuildName", &guild.name);
    ini.set(&section, "Founder", &guild.founder);
    ini.set(&section, "Date", &guild.date);
    ini.set(&section, "Alineacion", alignment_name(guild.alignment));
    ini.set(&section, "Antifaccion", &guild.antifaccion.to_string());
    ini.set(&section, "Leader", &guild.leader);
    ini.set(&section, "SubLider1", &guild.sub_lider1);
    ini.set(&section, "SubLider2", &guild.sub_lider2);
    ini.set(&section, "URL", &guild.url);
    ini.set(&section, "Desc", &guild.desc);
    ini.set(&section, "GuildNews", &guild.news);
    for i in 0..MAX_CODEX_LINES {
        ini.set(&section, &format!("Codex{}", i + 1), guild.codex.get(i).map(|s| s.as_str()).unwrap_or(""));
    }
    ini.set(&section, "NivelClan", &guild.nivel_clan.to_string());
    ini.set(&section, "PuntosClan", &guild.puntos_clan.to_string());
    ini.set(&section, "CVCG", &guild.cvc_wins.to_string());
    ini.set(&section, "CVCP", &guild.cvc_losses.to_string());
    ini.set(&section, "CASTIS", &guild.castle_sieges.to_string());
    ini.set(&section, "Repu", &guild.reputation.to_string());
    ini.set(&section, "EleccionesAbiertas", if guild.elecciones_abiertas { "1" } else { "0" });

    if let Err(e) = ini.save(&path) {
        tracing::error!("Failed to save guild {}: {}", guild.guild_number, e);
    }
}

/// Set total number of guilds
pub fn set_num_guilds(base: &Path, count: i32) {
    let path = guilds_info_path(base);
    let _ = std::fs::create_dir_all(guild_dir(base));
    let mut ini = IniFile::load(&path).unwrap_or_default();
    ini.set("INIT", "nroGuilds", &count.to_string());
    let _ = ini.save(&path);
}

/// Save members list
pub fn save_members(base: &Path, guild_name: &str, members: &[String]) {
    let path = guild_dir(base).join(format!("{}-members.mem", guild_name));
    let _ = std::fs::create_dir_all(guild_dir(base));
    let mut ini = IniFile::default();
    ini.set("INIT", "NroMembers", &members.len().to_string());
    for (i, name) in members.iter().enumerate() {
        ini.set("Members", &format!("Member{}", i + 1), name);
    }
    let _ = ini.save(&path);
}

/// Add member to guild
pub fn add_member(base: &Path, guild_name: &str, char_name: &str) {
    let mut members = load_members(base, guild_name);
    if !members.iter().any(|m| m.to_uppercase() == char_name.to_uppercase()) {
        members.push(char_name.to_string());
    }
    save_members(base, guild_name, &members);
}

/// Remove member from guild
pub fn remove_member(base: &Path, guild_name: &str, char_name: &str) {
    let mut members = load_members(base, guild_name);
    members.retain(|m| m.to_uppercase() != char_name.to_uppercase());
    save_members(base, guild_name, &members);
}

/// Save applicants
pub fn save_applicants(base: &Path, guild_name: &str, applicants: &[GuildApplicant]) {
    let path = guild_dir(base).join(format!("{}-solicitudes.sol", guild_name));
    let _ = std::fs::create_dir_all(guild_dir(base));
    let mut ini = IniFile::default();
    ini.set("INIT", "CantSolicitudes", &applicants.len().to_string());
    for (i, app) in applicants.iter().enumerate() {
        let section = format!("SOLICITUD{}", i + 1);
        ini.set(&section, "Nombre", &app.name);
        ini.set(&section, "Detalle", &app.detail);
    }
    let _ = ini.save(&path);
}

/// Add applicant to guild
pub fn add_applicant(base: &Path, guild_name: &str, char_name: &str, detail: &str) -> bool {
    let mut applicants = load_applicants(base, guild_name);
    if applicants.len() >= MAX_ASPIRANTES {
        return false;
    }
    applicants.push(GuildApplicant {
        name: char_name.to_string(),
        detail: detail.to_string(),
    });
    save_applicants(base, guild_name, &applicants);
    true
}

/// Remove applicant from guild
pub fn remove_applicant(base: &Path, guild_name: &str, char_name: &str) {
    let mut applicants = load_applicants(base, guild_name);
    applicants.retain(|a| a.name.to_uppercase() != char_name.to_uppercase());
    save_applicants(base, guild_name, &applicants);
}

/// Load guild bank gold
pub fn load_bank_gold(base: &Path, guild_name: &str) -> i64 {
    let path = guild_dir(base).join("Bancos").join(format!("{}.bov", guild_name));
    let ini = match IniFile::load(&path) {
        Ok(i) => i,
        Err(_) => return 0,
    };
    ini.get(guild_name, "BANCO")
        .and_then(|s| s.parse().ok())
        .unwrap_or(0)
}

/// Save guild bank gold
pub fn save_bank_gold(base: &Path, guild_name: &str, gold: i64) {
    let bank_dir = guild_dir(base).join("Bancos");
    let _ = std::fs::create_dir_all(&bank_dir);
    let path = bank_dir.join(format!("{}.bov", guild_name));
    let mut ini = IniFile::load(&path).unwrap_or_default();
    ini.set(guild_name, "BANCO", &gold.to_string());
    let _ = ini.save(&path);
}

/// Load guild bank items
pub fn load_bank_items(base: &Path, guild_name: &str) -> Vec<GuildBankSlot> {
    let path = guild_dir(base).join("Bancos").join(format!("{}.bov", guild_name));
    let ini = match IniFile::load(&path) {
        Ok(i) => i,
        Err(_) => return (0..MAX_GUILD_BANK_SLOTS).map(|_| GuildBankSlot::default()).collect(),
    };

    let count: usize = ini.get("BancoInventory", "CantidadItems")
        .and_then(|s| s.parse().ok())
        .unwrap_or(0);

    let mut items = Vec::new();
    for i in 1..=MAX_GUILD_BANK_SLOTS {
        if i <= count {
            let val = ini.get("BancoInventory", &format!("Obj{}", i)).unwrap_or_default();
            let parts: Vec<&str> = val.split('-').collect();
            let obj_index = parts.first().and_then(|s| s.parse().ok()).unwrap_or(0);
            let amount = parts.get(1).and_then(|s| s.parse().ok()).unwrap_or(0);
            items.push(GuildBankSlot { obj_index, amount });
        } else {
            items.push(GuildBankSlot::default());
        }
    }
    items
}

/// Save guild bank items
pub fn save_bank_items(base: &Path, guild_name: &str, items: &[GuildBankSlot]) {
    let bank_dir = guild_dir(base).join("Bancos");
    let _ = std::fs::create_dir_all(&bank_dir);
    let path = bank_dir.join(format!("{}.bov", guild_name));
    let mut ini = IniFile::load(&path).unwrap_or_default();

    let count = items.iter().filter(|s| s.obj_index > 0).count();
    ini.set("BancoInventory", "CantidadItems", &count.to_string());
    for (i, slot) in items.iter().enumerate() {
        ini.set("BancoInventory", &format!("Obj{}", i + 1), &format!("{}-{}", slot.obj_index, slot.amount));
    }
    let _ = ini.save(&path);
}

/// Create a new guild — returns the new guild number
pub fn create_guild(base: &Path, name: &str, founder: &str, alignment: i32, desc: &str, url: &str, codex: Vec<String>) -> i32 {
    let _ = std::fs::create_dir_all(guild_dir(base));
    let _ = std::fs::create_dir_all(guild_dir(base).join("Bancos"));

    let num = get_num_guilds(base) + 1;
    set_num_guilds(base, num);

    let now = chrono_date();
    let guild = GuildInfo {
        guild_number: num,
        name: name.to_string(),
        founder: founder.to_string(),
        date: now,
        alignment,
        antifaccion: 0,
        leader: founder.to_string(),
        sub_lider1: "Fermin".to_string(),
        sub_lider2: "Fermin".to_string(),
        url: url.to_string(),
        desc: desc.to_string(),
        news: String::new(),
        codex,
        nivel_clan: 1,
        puntos_clan: 0,
        cvc_wins: 0,
        cvc_losses: 0,
        castle_sieges: 0,
        reputation: 0,
        elecciones_abiertas: false,
    };
    save_guild(base, &guild);
    save_members(base, name, &[founder.to_string()]);
    save_applicants(base, name, &[]);

    // Create empty bank
    save_bank_gold(base, name, 0);
    save_bank_items(base, name, &(0..MAX_GUILD_BANK_SLOTS).map(|_| GuildBankSlot::default()).collect::<Vec<_>>());

    num
}

/// Dissolve a guild
pub fn dissolve_guild(base: &Path, guild_num: i32) {
    let path = guilds_info_path(base);
    if let Ok(mut ini) = IniFile::load(&path) {
        let section = format!("GUILD{}", guild_num);
        ini.set(&section, "GuildName", &format!("cerrado{}", guild_num));
        ini.set(&section, "Leader", "");
        ini.set(&section, "Founder", "");
        let _ = ini.save(&path);
    }
}

/// Get a list of all active guilds (number, name, alignment, level)
pub fn list_guilds(base: &Path) -> Vec<(i32, String, i32, i32)> {
    let num = get_num_guilds(base);
    let path = guilds_info_path(base);
    let ini = match IniFile::load(&path) {
        Ok(i) => i,
        Err(_) => return Vec::new(),
    };

    let mut guilds = Vec::new();
    for i in 1..=num {
        let section = format!("GUILD{}", i);
        let name = ini.get(&section, "GuildName").unwrap_or_default();
        if !name.is_empty() && !name.starts_with("cerrado") {
            let align = alignment_from_name(&ini.get(&section, "Alineacion").unwrap_or_default());
            let level: i32 = ini.get(&section, "NivelClan").and_then(|s| s.parse().ok()).unwrap_or(1);
            guilds.push((i, name, align, level));
        }
    }
    guilds
}

/// Simple date string (MM/DD/YYYY)
fn chrono_date() -> String {
    // Simple date without chrono dependency
    "01/01/2026".to_string()
}

/// Validate guild name: only letters a-z, space allowed
pub fn is_valid_guild_name(name: &str) -> bool {
    if name.is_empty() || name.len() > 40 {
        return false;
    }
    name.chars().all(|c| c.is_ascii_alphabetic() || c == ' ')
}
