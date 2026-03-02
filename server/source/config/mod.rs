/// Configuration module — loads server.ini and related config files.
///
/// Mirrors VB6's GetVar/WriteVar functions that read Windows INI files.
/// Uses a simple INI parser compatible with the VB6 WritePrivateProfileString format.

mod ini;

pub use ini::{IniFile, get_var, write_var};

use std::collections::HashMap;
use std::path::Path;

/// Role overrides from server.ini — maps lowercase character names to privilege levels.
/// VB6: EsAdministrador, EsDios, EsSemiDios, EsConsejero, etc. in FileIO.bas
pub type RoleMap = HashMap<String, i32>;

/// Load role assignments from server.ini sections.
/// Each section ([Administradores], [Dioses], etc.) lists character names.
/// Returns a map of lowercase_name → privilege_level.
pub fn load_roles(base_path: &Path) -> RoleMap {
    let ini_path = base_path.join("server.ini");
    let ini = match IniFile::load(&ini_path) {
        Ok(ini) => ini,
        Err(_) => return HashMap::new(),
    };

    // VB6 section → key prefix → privilege level (priority order: highest first)
    let role_sections: &[(&str, &str, i32)] = &[
        ("Administradores",    "administrador",    super::game::types::privilege_level::ADMINISTRADOR),
        ("SubAdministradores", "subadministrador",  super::game::types::privilege_level::SUB_ADMINISTRADOR),
        ("Desarrolladores",    "desarrollador",     super::game::types::privilege_level::DEVELOPER),
        ("Directores",         "director",          super::game::types::privilege_level::DIRECTOR),
        ("GranDioses",         "grandios",          super::game::types::privilege_level::GRAN_DIOS),
        ("Dioses",             "dios",              super::game::types::privilege_level::DIOS),
        ("Events",             "event",             super::game::types::privilege_level::EVENT_MASTER),
        ("SemiDioses",         "semidios",          super::game::types::privilege_level::SEMIDIOS),
        ("Consejeros",         "consejero",         super::game::types::privilege_level::CONSEJERO),
    ];

    let mut roles = HashMap::new();

    for &(section, _prefix, priv_level) in role_sections {
        // Read the count from [INIT] section
        let count: usize = ini.get("INIT", section)
            .and_then(|s| s.trim().parse().ok())
            .unwrap_or(0);

        if count == 0 {
            continue;
        }

        // Read all values from the section — iterate keys and extract non-empty names
        let keys = ini.keys(section);
        for key in &keys {
            if let Some(name) = ini.get(section, key) {
                let name = name.trim().to_string();
                if !name.is_empty() {
                    // VB6 strips leading * and + from names
                    let clean = name.trim_start_matches('*').trim_start_matches('+').to_lowercase();
                    if !clean.is_empty() {
                        // First match wins (highest priority section is processed first)
                        roles.entry(clean).or_insert(priv_level);
                    }
                }
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
    pub idle_limit: u32,
    pub allow_multi_logins: bool,
    pub can_create_characters: bool,
    pub server_only_gms: bool,
    pub encrypt: bool,
    pub exp_multiplier: u32,
    pub gold_multiplier: u32,
    pub drop_multiplier: u32,
    pub start_map: i32,
    pub start_x: i32,
    pub start_y: i32,
    pub char_dir: String,
    pub log_dir: String,
    pub notice: String,
    pub pretoriano_map: i32,
    pub intervalo_paralizado: i32,  // VB6: IntervaloParalizado (ticks at 40ms — default 500 = 20s)
    pub npc_ai_interval_ms: u64,    // VB6: IntervaloNpcAI (ms — default 1300)
}

impl ServerConfig {
    /// Load configuration from server.ini at the given base path.
    pub fn load(base_path: &Path) -> Result<Self, String> {
        let ini_path = base_path.join("server.ini");
        let ini = IniFile::load(&ini_path)
            .map_err(|e| format!("Failed to load server.ini: {}", e))?;

        let start_pos = ini.get("INIT", "StartPos").unwrap_or_default();
        let parts: Vec<&str> = start_pos.split('-').collect();

        Ok(Self {
            server_ip: ini.get("INIT", "ServerIp").unwrap_or("0.0.0.0".into()),
            port: ini.get("INIT", "StartPort")
                .and_then(|s| s.parse().ok())
                .unwrap_or(5028),
            max_users: ini.get("INIT", "MaxUsers")
                .and_then(|s| s.parse().ok())
                .unwrap_or(400),
            version: ini.get("INIT", "Version").unwrap_or("0.11.5".into()),
            client_version: ini.get("INIT", "ClientVersion").unwrap_or("1.0.1".into()),
            idle_limit: ini.get("INIT", "IdleLimit")
                .and_then(|s| s.parse().ok())
                .unwrap_or(10),
            allow_multi_logins: ini.get("INIT", "AllowMultiLogins")
                .map(|s| s == "1")
                .unwrap_or(true),
            can_create_characters: ini.get("INIT", "PuedeCrearPersonajes")
                .map(|s| s == "1")
                .unwrap_or(true),
            server_only_gms: ini.get("INIT", "ServerSoloGMs")
                .map(|s| s != "0")
                .unwrap_or(false),
            encrypt: ini.get("INIT", "Encriptar")
                .map(|s| s == "1")
                .unwrap_or(true),
            exp_multiplier: ini.get("INIT", "MultiplicadordeExp")
                .and_then(|s| s.parse().ok())
                .unwrap_or(1),
            gold_multiplier: ini.get("INIT", "MultiplicadordeOro")
                .and_then(|s| s.parse().ok())
                .unwrap_or(1),
            drop_multiplier: ini.get("INIT", "MultiplicadordeDrop")
                .and_then(|s| s.parse().ok())
                .unwrap_or(1),
            start_map: parts.first()
                .and_then(|s| s.parse().ok())
                .unwrap_or(1),
            start_x: parts.get(1)
                .and_then(|s| s.parse().ok())
                .unwrap_or(58),
            start_y: parts.get(2)
                .and_then(|s| s.parse().ok())
                .unwrap_or(45),
            char_dir: ini.get("AOSSLib", "chardir").unwrap_or("charfile".into()),
            log_dir: ini.get("AOSSLib", "logdir").unwrap_or("logs".into()),
            notice: ini.get("INIT", "Notice").unwrap_or_default(),
            pretoriano_map: ini.get("INIT", "MapaPretoriano")
                .and_then(|s| s.parse().ok())
                .unwrap_or(163),
            intervalo_paralizado: ini.get("INTERVALOS", "IntervaloParalizado")
                .and_then(|s| s.trim().parse().ok())
                .unwrap_or(500),
            npc_ai_interval_ms: ini.get("INTERVALOS", "IntervaloNpcAI")
                .and_then(|s| s.trim().parse().ok())
                .unwrap_or(1300),
        })
    }
}
