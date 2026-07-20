/// Configuration module — loads server.ini and related config files.
///
/// Mirrors VB6's GetVar/WriteVar functions that read Windows INI files.
/// Uses a simple INI parser compatible with the VB6 WritePrivateProfileString format.
mod ini;

pub use ini::{IniFile, get_var, write_var};

use std::collections::HashMap;
use std::path::Path;

/// Role overrides from server.ini — maps lowercase character names to privilege levels.
pub type RoleMap = HashMap<String, i32>;

/// Load role assignments from server.ini [INIT] section.
///
/// Format: comma-separated character names per role key.
/// ```ini
/// [INIT]
/// Admins=Shay,Shay2
/// SubAdmins=
/// Devs=
/// Directors=
/// GranDioses=
/// Dioses=
/// Events=
/// SemiDioses=
/// Consejeros=
/// ```
///
/// Returns a map of lowercase_name → privilege_level.
/// Processed highest-first so if a name appears in multiple roles, the highest wins.
pub fn load_roles(base_path: &Path) -> RoleMap {
    let ini_path = base_path.join("server.ini");
    let ini = match IniFile::load(&ini_path) {
        Ok(ini) => ini,
        Err(_) => return HashMap::new(),
    };

    // INI key → privilege level (priority order: highest first)
    let role_keys: &[(&str, i32)] = &[
        ("Admins", super::game::types::privilege_level::ADMINISTRADOR),
        (
            "SubAdmins",
            super::game::types::privilege_level::SUB_ADMINISTRADOR,
        ),
        ("Devs", super::game::types::privilege_level::DEVELOPER),
        ("Directors", super::game::types::privilege_level::DIRECTOR),
        ("GranDioses", super::game::types::privilege_level::GRAN_DIOS),
        ("Dioses", super::game::types::privilege_level::DIOS),
        ("Events", super::game::types::privilege_level::EVENT_MASTER),
        ("SemiDioses", super::game::types::privilege_level::SEMIDIOS),
        ("Consejeros", super::game::types::privilege_level::CONSEJERO),
    ];

    let mut roles = HashMap::new();

    for &(key, priv_level) in role_keys {
        let value = match ini.get("INIT", key) {
            Some(v) => v,
            None => continue,
        };

        // Split by comma and insert each non-empty name
        for name in value.split(',') {
            let clean = name.trim().to_lowercase();
            if !clean.is_empty() {
                // First match wins (highest priority key is processed first)
                roles.entry(clean).or_insert(priv_level);
            }
        }
    }

    roles
}

/// Server configuration loaded from server.ini
#[derive(Debug, Clone)]
pub struct ServerConfig {
    pub server_ip: String,
    pub port: u16,
    pub max_users: u32,
    pub version: String,
    pub client_version: String,
    pub allow_multi_logins: bool,
    pub can_create_characters: bool,
    pub server_only_gms: bool,
    pub exp_multiplier: u32,
    pub gold_multiplier: u32,
    pub drop_multiplier: u32,
    pub start_map: i32,
    pub start_x: i32,
    pub start_y: i32,
    pub notice: String,
    pub pretoriano_map: i32,
    // Security settings (loaded from [Security] section)
    pub max_packets_per_second: Option<u32>, // Per-connection packet rate limit (default 60)
    pub ip_max_connections: Option<u32>,     // Max simultaneous connections per IP (default 10)
    pub ip_min_interval_ms: Option<u64>, // Min ms between connections from same IP (default 500)
    pub flood_strike_limit: Option<u32>, // Strikes before disconnect (default 3)
    pub centinela_enabled: Option<bool>,  // Enable anti-bot Centinela checks (default true)
    pub centinela_min_seconds: Option<i32>, // Min seconds between automatic checks (default 300)
    pub centinela_max_seconds: Option<i32>, // Max seconds between automatic checks (default 900)
    pub centinela_macro_seconds: Option<i32>, // Max delay after macro suspicion (default 90)
    pub centinela_answer_seconds: Option<i32>, // Seconds to answer an active challenge (default 120)
    pub centinela_max_fails: Option<i32>, // Failed/expired checks before disconnect (default 3)
}

impl ServerConfig {
    /// Load configuration from server.ini at the given base path.
    pub fn load(base_path: &Path) -> Result<Self, String> {
        let ini_path = base_path.join("server.ini");
        let ini =
            IniFile::load(&ini_path).map_err(|e| format!("Failed to load server.ini: {}", e))?;

        let start_pos = ini.get("INIT", "StartPos").unwrap_or_default();
        let parts: Vec<&str> = start_pos.split('-').collect();

        Ok(Self {
            server_ip: ini.get("INIT", "ServerIp").unwrap_or("0.0.0.0".into()),
            port: ini
                .get("INIT", "StartPort")
                .and_then(|s| s.parse().ok())
                .unwrap_or(5028),
            max_users: ini
                .get("INIT", "MaxUsers")
                .and_then(|s| s.parse().ok())
                .unwrap_or(400),
            version: ini.get("INIT", "Version").unwrap_or("0.11.5".into()),
            client_version: ini.get("INIT", "ClientVersion").unwrap_or("1.0.0".into()),
            allow_multi_logins: ini
                .get("INIT", "AllowMultiLogins")
                .map(|s| s == "1")
                .unwrap_or(true),
            can_create_characters: ini
                .get("INIT", "PuedeCrearPersonajes")
                .map(|s| s == "1")
                .unwrap_or(true),
            server_only_gms: ini
                .get("INIT", "ServerSoloGMs")
                .map(|s| s != "0")
                .unwrap_or(false),
            exp_multiplier: ini
                .get("INIT", "MultiplicadordeExp")
                .and_then(|s| s.parse().ok())
                .unwrap_or(1),
            gold_multiplier: ini
                .get("INIT", "MultiplicadordeOro")
                .and_then(|s| s.parse().ok())
                .unwrap_or(1),
            drop_multiplier: ini
                .get("INIT", "MultiplicadordeDrop")
                .and_then(|s| s.parse().ok())
                .unwrap_or(1),
            start_map: parts.first().and_then(|s| s.parse().ok()).unwrap_or(1),
            start_x: parts.get(1).and_then(|s| s.parse().ok()).unwrap_or(58),
            start_y: parts.get(2).and_then(|s| s.parse().ok()).unwrap_or(45),
            notice: ini.get("INIT", "Notice").unwrap_or_default(),
            pretoriano_map: ini
                .get("INIT", "MapaPretoriano")
                .and_then(|s| s.parse().ok())
                .unwrap_or(163),
            max_packets_per_second: ini
                .get("Security", "MaxPacketsPerSecond")
                .and_then(|s| s.trim().parse().ok()),
            ip_max_connections: ini
                .get("Security", "IpMaxConnections")
                .and_then(|s| s.trim().parse().ok()),
            ip_min_interval_ms: ini
                .get("Security", "IpMinIntervalMs")
                .and_then(|s| s.trim().parse().ok()),
            flood_strike_limit: ini
                .get("Security", "FloodStrikeLimit")
                .and_then(|s| s.trim().parse().ok()),
            centinela_enabled: ini
                .get("Security", "CentinelaEnabled")
                .and_then(|s| parse_bool_flag(&s)),
            centinela_min_seconds: ini
                .get("Security", "CentinelaMinSeconds")
                .and_then(|s| s.trim().parse().ok()),
            centinela_max_seconds: ini
                .get("Security", "CentinelaMaxSeconds")
                .and_then(|s| s.trim().parse().ok()),
            centinela_macro_seconds: ini
                .get("Security", "CentinelaMacroSeconds")
                .and_then(|s| s.trim().parse().ok()),
            centinela_answer_seconds: ini
                .get("Security", "CentinelaAnswerSeconds")
                .and_then(|s| s.trim().parse().ok()),
            centinela_max_fails: ini
                .get("Security", "CentinelaMaxFails")
                .and_then(|s| s.trim().parse().ok()),
        })
    }
}

fn parse_bool_flag(value: &str) -> Option<bool> {
    match value.trim().to_ascii_lowercase().as_str() {
        "1" | "true" | "yes" | "on" | "si" => Some(true),
        "0" | "false" | "no" | "off" => Some(false),
        _ => None,
    }
}
