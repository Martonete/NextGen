// Character file I/O — reads and writes charfile/*.chr (INI format).
//
// Character files store all persistent character data:
// [INIT]      → visual (Head, Body, Arma, Escudo, Casco), Clase, Raza, Password, Logged
// [STATS]     → ELV, EXP, HP, Mana, Stamina, GLD, etc.
// [ATRIBUTOS] → AT1..AT5 (Str, Agi, Int, Cha, Con)
// [SKILLS]    → SK1..SK22
// [FLAGS]     → Ban, Muerto, Envenenado, Paralizado, etc.
// [HECHIZOS]  → H1..H20 (spell slots)
// [INVENTARIO]→ inventory items
// [GUILD]     → GUILDINDEX

use std::path::{Path, PathBuf};
use crate::config::IniFile;

/// Minimal character data needed for the account selection screen (ADDPJ packet).
#[derive(Debug, Clone)]
pub struct CharPreview {
    pub name: String,
    pub head: i32,
    pub body: i32,
    pub weapon: i32,
    pub shield: i32,
    pub helmet: i32,
    pub level: i32,
    pub class: String,
    pub dead: bool,
    pub race: String,
}

impl CharPreview {
    /// Format as comma-separated string for ADDPJ packet.
    /// Order: Head,Body,Weapon,Shield,Helmet,Level,Class,Dead,Race
    pub fn to_addpj_data(&self) -> String {
        format!(
            "{},{},{},{},{},{},{},{},{}",
            self.head, self.body, self.weapon, self.shield, self.helmet,
            self.level, self.class,
            if self.dead { 1 } else { 0 },
            self.race,
        )
    }
}

/// Full character data loaded from a .chr file.
#[derive(Debug, Clone)]
pub struct CharData {
    // INIT section
    pub name: String,
    pub head: i32,
    pub body: i32,
    pub heading: i32,
    pub weapon: i32,    // Arma
    pub shield: i32,    // Escudo
    pub helmet: i32,    // Casco
    pub class: String,  // Clase
    pub race: String,   // Raza
    pub gender: i32,    // Genero (1=Male, 2=Female)
    pub password: String, // CodeX from account
    pub hogar: i32,     // Home city

    // STATS
    pub level: i32,
    pub exp: i64,
    pub max_hp: i32,
    pub min_hp: i32,
    pub max_sta: i32,
    pub min_sta: i32,
    pub max_mana: i32,
    pub min_mana: i32,
    pub max_hit: i32,
    pub min_hit: i32,
    pub max_agua: i32,
    pub min_agua: i32,
    pub max_ham: i32,
    pub min_ham: i32,
    pub gold: i64,
    pub bank_gold: i64,
    pub skill_pts_libres: i32,

    // ATRIBUTOS (1-5: Str, Agi, Int, Cha, Con)
    pub attributes: [i32; 5],

    // SKILLS (1-22)
    pub skills: [i32; 22],

    // FLAGS
    pub banned: bool,
    pub dead: bool,
    pub poisoned: bool,
    pub paralyzed: bool,
    pub hidden: bool,
    pub navigating: bool,
    pub barco_slot: i32,   // VB6 BarcoSlot — inventory slot (1-based) with equipped boat
    pub montado: bool,
    pub levitando: bool,
    pub montado_body: i32,

    // POSITION
    pub map: i32,
    pub x: i32,
    pub y: i32,

    // GUILD
    pub guild_index: i32,

    // Privileges (from FLAGS)
    pub privileges: i32,

    // Inventory (25 slots: obj_index, amount, equipped)
    pub inventory: Vec<(i32, i32, bool)>,
    pub weapon_eqp_slot: usize,
    pub armour_eqp_slot: usize,
    pub shield_eqp_slot: usize,
    pub helmet_eqp_slot: usize,
    pub municion_eqp_slot: usize,

    // Bank (up to 40 slots: obj_index, amount)
    pub bank: Vec<(i32, i32)>,

    // Spells (20 slots)
    pub spells: [i32; 20],

    // Reputation
    pub reputation: i32,

    // Criminal flag
    pub criminal: bool,

    // Factions
    pub armada_real: bool,
    pub fuerzas_caos: bool,
    pub criminales_matados: i32,
    pub ciudadanos_matados: i32,
    pub recompensas_real: i32,   // Tier 0-5
    pub recompensas_caos: i32,   // Tier 0-5
    pub reenlistadas: bool,      // Can only enlist once
}

/// Resolve path to a character file.
pub fn charfile_path(base: &Path, char_name: &str) -> PathBuf {
    base.join("charfile").join(format!("{}.chr", char_name.to_uppercase()))
}

/// Check if a character exists on disk.
pub fn character_exists(base: &Path, char_name: &str) -> bool {
    charfile_path(base, char_name).exists()
}

/// Load minimal character data for the account selection screen.
pub fn load_char_preview(base: &Path, char_name: &str) -> Result<CharPreview, String> {
    let path = charfile_path(base, char_name);
    let ini = IniFile::load(&path)
        .map_err(|e| format!("Failed to load charfile '{}': {}", char_name, e))?;

    Ok(CharPreview {
        name: char_name.to_string(),
        head: ini.get("INIT", "Head").and_then(|s| s.parse().ok()).unwrap_or(0),
        body: ini.get("INIT", "Body").and_then(|s| s.parse().ok()).unwrap_or(0),
        weapon: ini.get("INIT", "Arma").and_then(|s| s.parse().ok()).unwrap_or(0),
        shield: ini.get("INIT", "Escudo").and_then(|s| s.parse().ok()).unwrap_or(0),
        helmet: ini.get("INIT", "Casco").and_then(|s| s.parse().ok()).unwrap_or(0),
        level: ini.get("STATS", "ELV").and_then(|s| s.parse().ok()).unwrap_or(1),
        class: ini.get("INIT", "Clase").unwrap_or_default(),
        dead: ini.get("FLAGS", "Muerto").map(|s| s == "1").unwrap_or(false),
        race: ini.get("INIT", "Raza").unwrap_or_default(),
    })
}

/// Load full character data from a .chr file.
pub fn load_charfile(base: &Path, char_name: &str) -> Result<CharData, String> {
    let path = charfile_path(base, char_name);
    let ini = IniFile::load(&path)
        .map_err(|e| format!("Failed to load charfile '{}': {}", char_name, e))?;

    let get_int = |section: &str, key: &str| -> i32 {
        ini.get(section, key).and_then(|s| s.parse().ok()).unwrap_or(0)
    };
    let get_long = |section: &str, key: &str| -> i64 {
        ini.get(section, key).and_then(|s| s.parse().ok()).unwrap_or(0)
    };

    let mut attributes = [0i32; 5];
    for i in 0..5 {
        attributes[i] = get_int("ATRIBUTOS", &format!("AT{}", i + 1));
    }

    let mut skills = [0i32; 22];
    for i in 0..22 {
        skills[i] = get_int("SKILLS", &format!("SK{}", i + 1));
    }

    Ok(CharData {
        name: char_name.to_string(),
        head: get_int("INIT", "Head"),
        body: get_int("INIT", "Body"),
        heading: get_int("INIT", "Heading"),
        weapon: get_int("INIT", "Arma"),
        shield: get_int("INIT", "Escudo"),
        helmet: get_int("INIT", "Casco"),
        class: ini.get("INIT", "Clase").unwrap_or_default(),
        race: ini.get("INIT", "Raza").unwrap_or_default(),
        gender: get_int("INIT", "Genero"),
        password: ini.get("INIT", "Password").unwrap_or_default(),
        hogar: get_int("INIT", "Hogar"),

        level: get_int("STATS", "ELV"),
        exp: get_long("STATS", "EXP"),
        max_hp: get_int("STATS", "MaxHP"),
        min_hp: get_int("STATS", "MinHP"),
        max_sta: get_int("STATS", "MaxSTA"),
        min_sta: get_int("STATS", "MinSTA"),
        max_mana: get_int("STATS", "MaxMAN"),
        min_mana: get_int("STATS", "MinMAN"),
        max_hit: get_int("STATS", "MaxHIT"),
        min_hit: get_int("STATS", "MinHIT"),
        max_agua: get_int("STATS", "MaxAGU"),
        min_agua: get_int("STATS", "MinAGU"),
        max_ham: get_int("STATS", "MaxHAM"),
        min_ham: get_int("STATS", "MinHAM"),
        gold: get_long("STATS", "GLD"),
        bank_gold: get_long("STATS", "BancoGLD"),
        skill_pts_libres: get_int("STATS", "SkillPtsLibres"),

        attributes,
        skills,

        banned: ini.get("FLAGS", "Ban").map(|s| s == "1").unwrap_or(false),
        dead: ini.get("FLAGS", "Muerto").map(|s| s == "1").unwrap_or(false),
        poisoned: ini.get("FLAGS", "Envenenado").map(|s| s == "1").unwrap_or(false),
        paralyzed: ini.get("FLAGS", "Paralizado").map(|s| s == "1").unwrap_or(false),
        hidden: ini.get("FLAGS", "Oculto").map(|s| s == "1").unwrap_or(false),
        navigating: ini.get("FLAGS", "Navegando").map(|s| s == "1").unwrap_or(false),
        barco_slot: get_int("Inventory", "BarcoSlot"),
        montado: ini.get("FLAGS", "Montado").map(|s| s == "1").unwrap_or(false),
        levitando: ini.get("FLAGS", "Levitando").map(|s| s == "1").unwrap_or(false),
        montado_body: get_int("FLAGS", "MontadoBody"),

        map: get_int("INIT", "Map"),
        x: get_int("INIT", "X"),
        y: get_int("INIT", "Y"),

        guild_index: get_int("GUILD", "GUILDINDEX"),
        privileges: get_int("FLAGS", "Privilegios"),

        // Load inventory (Obj1=idx-amt-equipped, delimiter is hyphen ASCII 45)
        inventory: {
            let mut inv = Vec::with_capacity(25);
            for i in 1..=25 {
                let raw = ini.get("INVENTARIO", &format!("Obj{}", i)).unwrap_or_default();
                if raw.is_empty() || raw == "0-0" || raw == "0-0-0" {
                    inv.push((0, 0, false));
                } else {
                    let obj_idx: i32 = crate::protocol::fields::read_field(1, &raw, '-').parse().unwrap_or(0);
                    let amount: i32 = crate::protocol::fields::read_field(2, &raw, '-').parse().unwrap_or(0);
                    let equipped: bool = crate::protocol::fields::read_field(3, &raw, '-') == "1";
                    inv.push((obj_idx, amount, equipped));
                }
            }
            inv
        },
        weapon_eqp_slot: get_int("INVENTARIO", "WeaponEqpSlot") as usize,
        armour_eqp_slot: get_int("INVENTARIO", "ArmourEqpSlot") as usize,
        shield_eqp_slot: get_int("INVENTARIO", "EscudoEqpSlot") as usize,
        helmet_eqp_slot: get_int("INVENTARIO", "CascoEqpSlot") as usize,
        municion_eqp_slot: get_int("INVENTARIO", "MunicionEqpSlot") as usize,

        // Load bank inventory
        bank: {
            let mut bank = Vec::with_capacity(40);
            for i in 1..=40 {
                let raw = ini.get("BANCO", &format!("Obj{}", i)).unwrap_or_default();
                if raw.is_empty() || raw == "0-0" {
                    bank.push((0, 0));
                } else {
                    let obj_idx: i32 = crate::protocol::fields::read_field(1, &raw, '-').parse().unwrap_or(0);
                    let amount: i32 = crate::protocol::fields::read_field(2, &raw, '-').parse().unwrap_or(0);
                    bank.push((obj_idx, amount));
                }
            }
            bank
        },

        // Load spells (H1..H20)
        spells: {
            let mut sp = [0i32; 20];
            for i in 0..20 {
                sp[i] = get_int("HECHIZOS", &format!("H{}", i + 1));
            }
            sp
        },

        // Reputation
        reputation: get_int("REP", "Promedio"),

        // Criminal flag
        criminal: ini.get("FLAGS", "Criminal").map(|s| s == "1").unwrap_or(false),

        // Factions
        armada_real: ini.get("FACCIONES", "EjercitoReal").map(|s| s == "1").unwrap_or(false),
        fuerzas_caos: ini.get("FACCIONES", "EjercitoCaos").map(|s| s == "1").unwrap_or(false),
        criminales_matados: get_int("FACCIONES", "CrimMatados"),
        ciudadanos_matados: get_int("FACCIONES", "CiudMatados"),
        recompensas_real: get_int("FACCIONES", "recReal"),
        recompensas_caos: get_int("FACCIONES", "recCaos"),
        reenlistadas: ini.get("FACCIONES", "Reenlistadas").map(|s| s == "1").unwrap_or(false),
    })
}

/// Create a new character file with starter stats.
pub fn create_charfile(
    base: &Path,
    name: &str,
    race: &str,
    gender: i32,
    class: &str,
    hogar: i32,
    head: i32,
    password: &str,  // CodeX from account
    attributes: [i32; 5],
    start_map: i32,
    start_x: i32,
    start_y: i32,
) -> Result<(), String> {
    let path = charfile_path(base, name);

    if path.exists() {
        return Err("El nombre del personaje ya esta siendo utilizado.".into());
    }

    // Ensure charfile directory exists
    if let Some(parent) = path.parent() {
        std::fs::create_dir_all(parent)
            .map_err(|e| format!("Failed to create charfile directory: {}", e))?;
    }

    // Determine starter body based on race + gender
    let body = starter_body(race, gender);
    let head_val = if head > 0 { head } else { starter_head(race, gender) };

    // Apply race attribute bonuses FIRST (VB6 13.3 Balance.dat ModRaza values)
    let mut attrs = attributes;
    let race_upper = race.to_uppercase();
    match race_upper.as_str() {
        "HUMANO" => {
            attrs[0] += 1; // Fuerza +1
            attrs[1] += 1; // Agilidad +1
            attrs[4] += 2; // Constitucion +2
        }
        "ELFO" => {
            attrs[0] -= 1; // Fuerza -1
            attrs[1] += 3; // Agilidad +3
            attrs[2] += 2; // Inteligencia +2
            attrs[3] += 2; // Carisma +2
            attrs[4] += 1; // Constitucion +1
        }
        "ELFO OSCURO" => {
            attrs[0] += 2; // Fuerza +2
            attrs[1] += 3; // Agilidad +3
            attrs[2] += 2; // Inteligencia +2
            attrs[3] -= 3; // Carisma -3
        }
        "ENANO" => {
            attrs[0] += 3; // Fuerza +3
            attrs[4] += 3; // Constitucion +3
            attrs[2] -= 2; // Inteligencia -2
            attrs[3] -= 2; // Carisma -2
        }
        "GNOMO" => {
            attrs[0] -= 2; // Fuerza -2
            attrs[1] += 3; // Agilidad +3
            attrs[2] += 4; // Inteligencia +4
            attrs[3] += 1; // Carisma +1
        }
        _ => {}
    }
    // Clamp attributes to valid range (1-25)
    for a in attrs.iter_mut() {
        *a = (*a).clamp(1, 25);
    }

    // Base stats by class (VB6 13.3: uses post-race-bonus attributes)
    let (base_hp, base_mana, base_sta) = starter_stats(class, attrs[4], attrs[1], attrs[2]);

    // Determine race-specific armor (VB6 ConnectNewUser lines 660-680)
    let race_armor = match race_upper.as_str() {
        "HUMANO" => 463,
        "ELFO" => 464,
        "ELFO OSCURO" => 465,
        "ENANO" | "GNOMO" => 466,
        _ => 463,
    };

    // Determine starting spells (VB6 13.3: casters get spell 2, druids also get spell 46)
    let class_upper = class.to_uppercase();
    let starting_spell_1 = match class_upper.as_str() {
        "MAGO" | "CLERIGO" | "DRUIDA" | "BARDO" | "ASESINO" => 2,
        _ => 0,
    };
    let starting_spell_2 = if class_upper == "DRUIDA" { 46 } else { 0 };

    let mut lines = Vec::new();

    // [INIT]
    lines.push("[INIT]".into());
    lines.push(format!("Head={}", head_val));
    lines.push(format!("Body={}", body));
    lines.push("Arma=0".into());
    lines.push("Escudo=0".into());
    lines.push("Casco=0".into());
    lines.push(format!("Clase={}", class));
    lines.push(format!("Raza={}", race));
    lines.push(format!("Genero={}", gender));
    lines.push(format!("Hogar={}", hogar));
    lines.push(format!("Password={}", password));
    lines.push("Logged=0".into());
    lines.push(format!("Map={}", start_map));
    lines.push(format!("X={}", start_x));
    lines.push(format!("Y={}", start_y));

    // [STATS]
    lines.push("[STATS]".into());
    lines.push("ELV=1".into());
    lines.push("EXP=0".into());
    lines.push(format!("MaxHP={}", base_hp));
    lines.push(format!("MinHP={}", base_hp));
    lines.push(format!("MaxSTA={}", base_sta));
    lines.push(format!("MinSTA={}", base_sta));
    lines.push(format!("MaxMAN={}", base_mana));
    lines.push(format!("MinMAN={}", base_mana));
    lines.push("MaxHIT=2".into());
    lines.push("MinHIT=1".into());
    lines.push("MaxAGU=100".into());
    lines.push("MinAGU=100".into());
    lines.push("MaxHAM=100".into());
    lines.push("MinHAM=100".into());
    lines.push("GLD=0".into());
    lines.push("SkillPtsLibres=10".into());

    // [ATRIBUTOS] — with race bonuses applied
    lines.push("[ATRIBUTOS]".into());
    for i in 0..5 {
        lines.push(format!("AT{}={}", i + 1, attrs[i]));
    }

    // [SKILLS] — VB6 13.3: all skills start at 0, player distributes 10 skill points
    lines.push("[SKILLS]".into());
    for i in 1..=22 {
        lines.push(format!("SK{}=0", i));
    }

    // [FLAGS]
    lines.push("[FLAGS]".into());
    lines.push("Ban=0".into());
    lines.push("Muerto=0".into());
    lines.push("Envenenado=0".into());
    lines.push("Paralizado=0".into());
    lines.push("Privilegios=0".into());
    lines.push("Criminal=0".into());
    lines.push("Navegando=0".into());

    // [HECHIZOS] — VB6 13.3: casters get spell 2 in slot 1, druids also get spell 46 in slot 2
    lines.push("[HECHIZOS]".into());
    lines.push(format!("H1={}", starting_spell_1));
    lines.push(format!("H2={}", starting_spell_2));
    for i in 3..=20 {
        lines.push(format!("H{}=0", i));
    }

    // [INVENTARIO] — VB6 13.3 ConnectNewUser starter items
    // Build inventory dynamically based on class
    let mut inv: Vec<(i32, i32, bool)> = Vec::new(); // (obj_index, amount, equipped)

    // Slot 1: Red Potion (857) x200
    inv.push((857, 200, false));

    // Slot 2: Blue Potion (856) x200 if mana class, else Yellow (855) x100 + Green (858) x50
    let has_mana = base_mana > 0;
    if has_mana {
        inv.push((856, 200, false));
    } else {
        inv.push((855, 100, false));
        inv.push((858, 50, false));
    }

    // Armor by race, equipped
    inv.push((race_armor, 1, true));

    // Weapon by class, equipped
    let is_cazador = class_upper == "CAZADOR";
    let is_trabajador = class_upper == "TRABAJADOR";
    if is_cazador {
        inv.push((859, 1, true)); // Bow
    } else if is_trabajador {
        // Random tool 561-565
        let tool = 560 + simple_rand_range(5);
        inv.push((tool, 1, true));
    } else {
        inv.push((460, 1, true)); // Dagger
    }

    // Arrows for hunters
    if is_cazador {
        inv.push((860, 150, true)); // Arrows, equipped as ammo
    }

    // Food and drink
    inv.push((467, 100, false)); // Apples
    inv.push((468, 100, false)); // Juice

    let item_count = inv.len();
    lines.push("[INVENTARIO]".into());
    lines.push(format!("CantidadItems={}", item_count));
    for (i, &(obj, amt, eq)) in inv.iter().enumerate() {
        if eq {
            lines.push(format!("Obj{}={}-{}-1", i + 1, obj, amt));
        } else {
            lines.push(format!("Obj{}={}-{}", i + 1, obj, amt));
        }
    }
    for i in (inv.len() + 1)..=25 {
        lines.push(format!("Obj{}=0-0", i));
    }

    // [GUILD]
    lines.push("[GUILD]".into());
    lines.push("GUILDINDEX=0".into());

    // [FACCIONES]
    lines.push("[FACCIONES]".into());
    lines.push("EjercitoReal=0".into());
    lines.push("EjercitoCaos=0".into());
    lines.push("CrimMatados=0".into());
    lines.push("CiudMatados=0".into());
    lines.push("NeutrMatados=0".into());
    lines.push("recReal=0".into());
    lines.push("recCaos=0".into());
    lines.push("Reenlistadas=0".into());
    lines.push("rArReal=0".into());
    lines.push("rArCaos=0".into());
    lines.push("rExReal=0".into());
    lines.push("rExCaos=0".into());

    // [REP] — reputation (VB6 13.3: NobleRep=1000, PlebeRep=30, rest=0)
    lines.push("[REP]".into());
    lines.push("BurguesRep=0".into());
    lines.push("NobleRep=1000".into());
    lines.push("PlebeRep=30".into());
    lines.push("CriminalRep=0".into());
    lines.push("Promedio=0".into());

    let content = lines.join("\r\n") + "\r\n";
    std::fs::write(&path, content)
        .map_err(|e| format!("Failed to write charfile: {}", e))?;

    Ok(())
}

/// Save full character state back to .chr file (called on disconnect).
/// Preserves fields we don't track at runtime by loading the existing file first.
pub fn save_charfile(base: &Path, char_name: &str, data: &CharSaveData) -> Result<(), String> {
    let path = charfile_path(base, char_name);

    // Load existing file to preserve fields we don't modify
    let ini = IniFile::load(&path)
        .map_err(|e| format!("Failed to load charfile for save: {}", e))?;

    let get_existing = |section: &str, key: &str| -> String {
        ini.get(section, key).unwrap_or_default()
    };

    let mut lines = Vec::new();

    // [INIT] — preserve class, race, gender, hogar, password
    lines.push("[INIT]".into());
    lines.push(format!("Head={}", data.head));
    lines.push(format!("Body={}", data.body));
    lines.push(format!("Heading={}", data.heading));
    lines.push(format!("Arma={}", data.weapon));
    lines.push(format!("Escudo={}", data.shield));
    lines.push(format!("Casco={}", data.helmet));
    lines.push(format!("Clase={}", get_existing("INIT", "Clase")));
    lines.push(format!("Raza={}", get_existing("INIT", "Raza")));
    lines.push(format!("Genero={}", get_existing("INIT", "Genero")));
    lines.push(format!("Hogar={}", get_existing("INIT", "Hogar")));
    lines.push(format!("Password={}", get_existing("INIT", "Password")));
    lines.push("Logged=0".into());
    lines.push(format!("Map={}", data.map));
    lines.push(format!("X={}", data.x));
    lines.push(format!("Y={}", data.y));

    // [STATS]
    lines.push("[STATS]".into());
    lines.push(format!("ELV={}", data.level));
    lines.push(format!("EXP={}", data.exp));
    lines.push(format!("MaxHP={}", data.max_hp));
    lines.push(format!("MinHP={}", data.min_hp));
    lines.push(format!("MaxSTA={}", data.max_sta));
    lines.push(format!("MinSTA={}", data.min_sta));
    lines.push(format!("MaxMAN={}", data.max_mana));
    lines.push(format!("MinMAN={}", data.min_mana));
    lines.push(format!("MaxHIT={}", data.max_hit));
    lines.push(format!("MinHIT={}", data.min_hit));
    lines.push(format!("MaxAGU={}", data.max_agua));
    lines.push(format!("MinAGU={}", data.min_agua));
    lines.push(format!("MaxHAM={}", data.max_ham));
    lines.push(format!("MinHAM={}", data.min_ham));
    lines.push(format!("GLD={}", data.gold));
    lines.push(format!("BancoGLD={}", data.bank_gold));
    lines.push(format!("SkillPtsLibres={}", data.skill_pts_libres));

    // [ATRIBUTOS]
    lines.push("[ATRIBUTOS]".into());
    for i in 0..5 {
        lines.push(format!("AT{}={}", i + 1, data.attributes[i]));
    }

    // [SKILLS]
    lines.push("[SKILLS]".into());
    for i in 0..22 {
        lines.push(format!("SK{}={}", i + 1, data.skills[i]));
    }

    // [FLAGS]
    lines.push("[FLAGS]".into());
    lines.push(format!("Ban={}", get_existing("FLAGS", "Ban")));
    lines.push(format!("Muerto={}", if data.dead { 1 } else { 0 }));
    lines.push(format!("Envenenado={}", if data.poisoned { 1 } else { 0 }));
    lines.push(format!("Paralizado={}", if data.paralyzed { 1 } else { 0 }));
    lines.push(format!("Privilegios={}", data.privileges));
    lines.push(format!("Criminal={}", if data.criminal { 1 } else { 0 }));
    lines.push(format!("Oculto={}", if data.hidden { 1 } else { 0 }));
    lines.push(format!("Navegando={}", if data.navigating { 1 } else { 0 }));
    lines.push(format!("Montado={}", if data.montado { 1 } else { 0 }));
    lines.push(format!("Levitando={}", if data.levitando { 1 } else { 0 }));
    lines.push(format!("MontadoBody={}", data.montado_body));

    // [HECHIZOS]
    lines.push("[HECHIZOS]".into());
    for i in 0..20 {
        lines.push(format!("H{}={}", i + 1, data.spells[i]));
    }

    // [INVENTARIO]
    lines.push("[INVENTARIO]".into());
    let item_count = data.inventory.iter().filter(|s| s.0 > 0).count();
    lines.push(format!("CantidadItems={}", item_count));
    for i in 0..25 {
        let (obj_idx, amount, equipped) = data.inventory[i];
        let eq = if equipped { 1 } else { 0 };
        lines.push(format!("Obj{}={}-{}-{}", i + 1, obj_idx, amount, eq));
    }
    lines.push(format!("WeaponEqpSlot={}", data.weapon_eqp_slot));
    lines.push(format!("ArmourEqpSlot={}", data.armour_eqp_slot));
    lines.push(format!("EscudoEqpSlot={}", data.shield_eqp_slot));
    lines.push(format!("CascoEqpSlot={}", data.helmet_eqp_slot));
    lines.push(format!("MunicionEqpSlot={}", data.municion_eqp_slot));
    lines.push(format!("BarcoSlot={}", data.barco_slot));

    // [BANCO] — bank inventory
    lines.push("[BANCO]".into());
    let bank_count = data.bank.iter().filter(|s| s.0 > 0).count();
    lines.push(format!("CantidadItems={}", bank_count));
    for i in 0..data.bank.len().min(25) {
        let (obj_idx, amount) = data.bank[i];
        lines.push(format!("Obj{}={}-{}", i + 1, obj_idx, amount));
    }

    // [GUILD]
    lines.push("[GUILD]".into());
    lines.push(format!("GUILDINDEX={}", data.guild_index));

    // [FACCIONES] — save live kill data
    lines.push("[FACCIONES]".into());
    lines.push(format!("EjercitoReal={}", if data.ejercito_real { 1 } else { 0 }));
    lines.push(format!("EjercitoCaos={}", if data.ejercito_caos { 1 } else { 0 }));
    lines.push(format!("CrimMatados={}", data.criminales_matados));
    lines.push(format!("CiudMatados={}", data.ciudadanos_matados));
    lines.push(format!("NeutrMatados={}", get_existing("FACCIONES", "NeutrMatados")));
    lines.push(format!("recReal={}", get_existing("FACCIONES", "recReal")));
    lines.push(format!("recCaos={}", get_existing("FACCIONES", "recCaos")));
    lines.push(format!("Reenlistadas={}", get_existing("FACCIONES", "Reenlistadas")));
    lines.push(format!("rArReal={}", get_existing("FACCIONES", "rArReal")));
    lines.push(format!("rArCaos={}", get_existing("FACCIONES", "rArCaos")));
    lines.push(format!("rExReal={}", get_existing("FACCIONES", "rExReal")));
    lines.push(format!("rExCaos={}", get_existing("FACCIONES", "rExCaos")));

    // [REP] — preserve existing
    lines.push("[REP]".into());
    lines.push(format!("BurguesRep={}", get_existing("REP", "BurguesRep")));
    lines.push(format!("NobleRep={}", get_existing("REP", "NobleRep")));
    lines.push(format!("PlebeRep={}", get_existing("REP", "PlebeRep")));
    lines.push(format!("CriminalRep={}", get_existing("REP", "CriminalRep")));
    lines.push(format!("Promedio={}", data.reputation));

    let content = lines.join("\r\n") + "\r\n";
    std::fs::write(&path, content)
        .map_err(|e| format!("Failed to write charfile: {}", e))?;

    Ok(())
}

/// Data needed to save a character (extracted from UserState).
pub struct CharSaveData {
    pub head: i32,
    pub body: i32,
    pub heading: i32,
    pub weapon: i32,
    pub shield: i32,
    pub helmet: i32,
    pub map: i32,
    pub x: i32,
    pub y: i32,
    pub level: i32,
    pub exp: i64,
    pub max_hp: i32,
    pub min_hp: i32,
    pub max_sta: i32,
    pub min_sta: i32,
    pub max_mana: i32,
    pub min_mana: i32,
    pub max_hit: i32,
    pub min_hit: i32,
    pub max_agua: i32,
    pub min_agua: i32,
    pub max_ham: i32,
    pub min_ham: i32,
    pub gold: i64,
    pub bank_gold: i64,
    pub attributes: [i32; 5],
    pub skills: [i32; 22],
    pub dead: bool,
    pub poisoned: bool,
    pub paralyzed: bool,
    pub criminal: bool,
    pub hidden: bool,
    pub navigating: bool,
    pub barco_slot: usize,
    pub montado: bool,
    pub levitando: bool,
    pub montado_body: i32,
    pub privileges: i32,
    pub spells: [i32; 20],
    pub inventory: Vec<(i32, i32, bool)>, // (obj_idx, amount, equipped)
    pub bank: Vec<(i32, i32)>,            // (obj_idx, amount)
    pub weapon_eqp_slot: usize,
    pub armour_eqp_slot: usize,
    pub shield_eqp_slot: usize,
    pub helmet_eqp_slot: usize,
    pub municion_eqp_slot: usize,
    pub reputation: i32,
    pub guild_index: i32,
    pub criminales_matados: i32,
    pub ciudadanos_matados: i32,
    pub ejercito_real: bool,
    pub ejercito_caos: bool,
    pub skill_pts_libres: i32,
}

/// Delete a character file.
pub fn delete_charfile(base: &Path, char_name: &str) -> Result<(), String> {
    let path = charfile_path(base, char_name);
    if path.exists() {
        std::fs::remove_file(&path)
            .map_err(|e| format!("Failed to delete charfile: {}", e))?;
    }
    Ok(())
}

/// Set the Logged flag in a character file.
pub fn set_logged_flag(base: &Path, char_name: &str, logged: bool) -> Result<(), String> {
    let path = charfile_path(base, char_name);
    let path_str = path.to_string_lossy().to_string();
    let value = if logged { "1" } else { "0" };
    crate::config::write_var(&path_str, "INIT", "Logged", value)
        .map_err(|e| format!("Failed to update Logged flag: {}", e))
}

// Starter body IDs by race and gender (VB6 Declares.bas / General.bas)
fn starter_body(race: &str, _gender: i32) -> i32 {
    // VB6 13.3 DarCuerpo(): body per race (no gender distinction in 13.3)
    let race_upper = race.to_uppercase();
    match race_upper.as_str() {
        "HUMANO" => 1,
        "ELFO" => 2,
        "ELFO OSCURO" => 3,
        "ENANO" | "GNOMO" => 300,
        _ => 1,
    }
}

fn starter_head(race: &str, gender: i32) -> i32 {
    let race_lower = race.to_lowercase();
    match (race_lower.as_str(), gender) {
        ("humano", 1) => 1,
        ("humano", 2) => 70,
        ("elfo", 1) => 101,
        ("elfo", 2) => 170,
        ("elfo oscuro", 1) => 201,
        ("elfo oscuro", 2) => 270,
        ("enano", 1) => 301,
        ("enano", 2) => 370,
        ("gnomo", 1) => 401,
        ("gnomo", 2) => 470,
        _ => 1,
    }
}

/// Simple random in range [1, max] (not crypto-secure).
fn simple_rand_range(max: i32) -> i32 {
    use std::time::{SystemTime, UNIX_EPOCH};
    if max <= 1 { return 1; }
    let seed = SystemTime::now()
        .duration_since(UNIX_EPOCH)
        .unwrap_or_default()
        .as_nanos() as u32;
    let r = seed.wrapping_mul(1103515245).wrapping_add(12345);
    1 + (r % max as u32) as i32
}

fn starter_stats(class: &str, constitution: i32, agility: i32, intelligence: i32) -> (i32, i32, i32) {
    // VB6 13.3 ConnectNewUser: HP/Mana/Sta formulas using attributes

    // HP = 15 + Random(1, Constitution/3)
    let con_div = (constitution / 3).max(1);
    let hp = 15 + simple_rand_range(con_div);

    // Stamina = 20 * Random(1, Agility/6); if roll==1 then roll=2
    let agi_div = (agility / 6).max(1);
    let sta_roll = {
        let r = simple_rand_range(agi_div);
        if r == 1 { 2 } else { r }
    };
    let sta = 20 * sta_roll;

    // Mana by class
    let class_upper = class.to_uppercase();
    let mana = match class_upper.as_str() {
        "MAGO" => intelligence * 3,
        "CLERIGO" | "DRUIDA" | "BARDO" | "ASESINO" | "BANDIDO" => 50,
        _ => 0, // Guerrero, Cazador, Ladron, Paladin, Trabajador, Pirata
    };

    (hp, mana, sta)
}

#[cfg(test)]
mod tests {
    use super::*;
    use std::fs;

    fn setup_temp_dir(name: &str) -> PathBuf {
        let dir = std::env::temp_dir().join(format!("ao_chr_test_{}_{}", std::process::id(), name));
        let _ = fs::remove_dir_all(&dir);
        fs::create_dir_all(dir.join("charfile")).unwrap();
        dir
    }

    #[test]
    fn create_and_load_character() {
        let base = setup_temp_dir("create");
        let attrs = [18, 18, 18, 18, 18];

        create_charfile(&base, "Warrior", "Humano", 1, "Guerrero", 1, 0, "CODEX", attrs, 1, 50, 50).unwrap();

        assert!(character_exists(&base, "Warrior"));

        let preview = load_char_preview(&base, "Warrior").unwrap();
        assert_eq!(preview.level, 1);
        assert_eq!(preview.class, "Guerrero");
        assert_eq!(preview.race, "Humano");
        assert!(!preview.dead);

        let full = load_charfile(&base, "Warrior").unwrap();
        // 13.3: HP = 15 + rand(1, CON/3), Guerrero has 0 mana
        assert!(full.max_hp >= 15, "HP should be at least 15");
        assert_eq!(full.max_mana, 0);
        // Humano 13.3 race bonuses: Str+1, Agi+1, Con+2 → [19,19,18,18,20]
        assert_eq!(full.attributes, [19, 19, 18, 18, 20]);
        assert_eq!(full.map, 1);

        let _ = fs::remove_dir_all(&base);
    }

    #[test]
    fn duplicate_character_fails() {
        let base = setup_temp_dir("dupe");
        let attrs = [18; 5];
        create_charfile(&base, "Dupe", "Humano", 1, "Mago", 1, 0, "C", attrs, 1, 50, 50).unwrap();
        let result = create_charfile(&base, "Dupe", "Elfo", 2, "Clerigo", 1, 0, "C", attrs, 1, 50, 50);
        assert!(result.is_err());
        let _ = fs::remove_dir_all(&base);
    }
}
