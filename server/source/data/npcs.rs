// NPC database loader — dat/NPCs.dat + dat/NPCs-HOSTILES.dat
//
// INI format. NPCs.dat has [NPC1]..[NPC206] (normal NPCs).
// NPCs-HOSTILES.dat has [NPC500]+ (hostile monsters).
// Both files share the same field structure.

use std::path::Path;
use crate::config::IniFile;

/// NPC type matching VB6 eNPCType.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
#[repr(u8)]
pub enum NpcType {
    Common = 0,
    Reviver = 1,
    RoyalGuard = 2,
    Trainer = 3,
    Banker = 4,
    Noble = 5,
    Dragon = 6,
    Gambler = 7,
    ChaosGuard = 8,
    Resign = 9,
    CastleKing = 10,
    Quest = 11,
    Traveler = 12,
    Citizenship = 13,
    Inscribe = 14,
    HouseSeller = 15,
    Arena = 16,
    QuestNoble = 17,
    GodNpc = 18,
    Surgeon = 19,
    Bargomaud = 20,
    QuintaJera = 21,
    BoveClan = 22,
    Mail = 23,
    BoxDelivery = 24,
}

impl NpcType {
    fn from_i32(v: i32) -> Self {
        match v {
            0 => Self::Common,
            1 => Self::Reviver,
            2 => Self::RoyalGuard,
            3 => Self::Trainer,
            4 => Self::Banker,
            5 => Self::Noble,
            6 => Self::Dragon,
            7 => Self::Gambler,
            8 => Self::ChaosGuard,
            9 => Self::Resign,
            10 => Self::CastleKing,
            11 => Self::Quest,
            12 => Self::Traveler,
            13 => Self::Citizenship,
            14 => Self::Inscribe,
            15 => Self::HouseSeller,
            16 => Self::Arena,
            17 => Self::QuestNoble,
            18 => Self::GodNpc,
            19 => Self::Surgeon,
            20 => Self::Bargomaud,
            21 => Self::QuintaJera,
            22 => Self::BoveClan,
            23 => Self::Mail,
            24 => Self::BoxDelivery,
            _ => Self::Common,
        }
    }
}

/// NPC data loaded from NPCs.dat or NPCs-HOSTILES.dat.
#[derive(Debug, Clone)]
pub struct NpcData {
    pub index: usize,
    pub name: String,
    pub desc: String,
    pub npc_type: NpcType,

    // Appearance
    pub head: i32,
    pub body: i32,
    pub heading: i32,
    pub weapon_anim: i32,   // VB6: ArmaAnim
    pub shield_anim: i32,   // VB6: EscudoAnim
    pub casco_anim: i32,    // VB6: CascoAnim

    // Behavior
    pub movement: i32,      // AI movement type (0=static, 1=random, 2=wander, 3=chase)
    pub attackable: bool,
    pub hostile: bool,
    pub respawn: bool,
    pub domable: i32,       // Taming skill required (0 = can't tame)
    pub comercia: bool,     // Can trade

    // Combat
    pub min_hp: i32,
    pub max_hp: i32,
    pub min_hit: i32,
    pub max_hit: i32,
    pub def: i32,
    pub def_m: i32,         // VB6: DEFm — magic defense vs spells
    pub poder_ataque: i32,
    pub poder_evasion: i32,

    // Economy
    pub give_exp: i32,
    pub give_gld: i32,
    pub give_gld_min: i32,
    pub give_gld_max: i32,

    // Commerce
    pub inflacion: i32,     // Price markup percentage (e.g. 50 = +50%)
    pub tipo_items: i32,    // ObjType filter (0 = buys anything)
    pub inv_respawn: bool,  // If true, inventory does NOT auto-replenish

    // Inventory
    pub nro_items: i32,
    pub items: Vec<NpcInvItem>, // Items loaded from Obj1..ObjN
    pub alineacion: i32,    // Alignment (0=neutral, 1=good, 2=evil)

    // Movement constraints (VB6: LegalPosNPC)
    pub agua_valida: bool,      // Can walk on water tiles
    pub tierra_invalida: bool,  // Can ONLY walk on water tiles

    // Status effects
    pub veneno: bool,           // Poisons on hit (VB6: Npclist.Veneno)

    // Spells
    pub lanza_spells: i32,      // Number of spells (0 = can't cast)
    pub spells: Vec<i32>,       // Spell indices (Sp1..SpN)

    // Crystal drops (VB6: Cristales section in NPC dat)
    pub cristales: bool,                   // Whether NPC drops crystals on death
    pub crystal_min1: i32,
    pub crystal_max1: i32,
    pub crystal_min2: i32,
    pub crystal_max2: i32,
    pub crystal_min3: i32,
    pub crystal_max3: i32,
    pub crystal_min4: i32,
    pub crystal_max4: i32,

    // Points awarded on kill (VB6: GivePTS — faction points)
    pub give_pts: i32,

    // Sound effects (VB6: SND1 = attack, SND2 = hit/hurt, SND3 = death)
    pub snd1: i32,
    pub snd2: i32,
    pub snd3: i32,

    // Trainer creature data (VB6: NroCriaturas, CI1..CIN, CN1..CNN)
    pub nro_criaturas: i32,
    pub criaturas: Vec<TrainerCreature>,

    // Default aura (VB6: NPC can have CreaAura set in dat file, applied on spawn)
    pub aura: i32,
}

/// Trainer creature entry (VB6: tCriaturasEntrenador).
#[derive(Debug, Clone, Default)]
pub struct TrainerCreature {
    pub npc_index: i32,
    pub npc_name: String,
}

/// NPC inventory item as loaded from dat file.
#[derive(Debug, Clone, Default)]
pub struct NpcInvItem {
    pub obj_index: i32,
    pub amount: i32,
    pub prob_tirar: i32, // Drop probability (0-100, used in death drop calc)
}

impl Default for NpcData {
    fn default() -> Self {
        Self {
            index: 0,
            name: String::new(),
            desc: String::new(),
            npc_type: NpcType::Common,
            head: 0, body: 0, heading: 3, weapon_anim: 0, shield_anim: 0, casco_anim: 0,
            movement: 0, attackable: false, hostile: false,
            respawn: false, domable: 0, comercia: false,
            min_hp: 0, max_hp: 0, min_hit: 0, max_hit: 0,
            def: 0, def_m: 0, poder_ataque: 0, poder_evasion: 0,
            give_exp: 0, give_gld: 0, give_gld_min: 0, give_gld_max: 0,
            inflacion: 0, tipo_items: 0, inv_respawn: false,
            nro_items: 0, items: Vec::new(), alineacion: 0,
            agua_valida: false, tierra_invalida: false,
            veneno: false,
            lanza_spells: 0, spells: Vec::new(),
            cristales: false,
            crystal_min1: 0, crystal_max1: 0,
            crystal_min2: 0, crystal_max2: 0,
            crystal_min3: 0, crystal_max3: 0,
            crystal_min4: 0, crystal_max4: 0,
            give_pts: 0,
            snd1: 0, snd2: 0, snd3: 0,
            nro_criaturas: 0, criaturas: Vec::new(),
            aura: 0,
        }
    }
}

/// Load NPC from a single INI section.
fn load_npc_from_ini(ini: &IniFile, section: &str, index: usize) -> NpcData {
    let get_str = |key: &str| -> String {
        ini.get(section, key).unwrap_or_default()
    };
    let get_int = |key: &str| -> i32 {
        ini.get(section, key).and_then(|s| s.parse().ok()).unwrap_or(0)
    };
    let get_bool = |key: &str| -> bool {
        ini.get(section, key).map(|s| s == "1").unwrap_or(false)
    };

    NpcData {
        index,
        name: get_str("Name"),
        desc: get_str("Desc"),
        npc_type: NpcType::from_i32(get_int("NpcType")),
        head: get_int("Head"),
        body: get_int("Body"),
        heading: get_int("Heading"),
        weapon_anim: get_int("ArmaAnim"),
        shield_anim: get_int("EscudoAnim"),
        casco_anim: get_int("CascoAnim"),
        movement: get_int("Movement"),
        attackable: get_bool("Attackable"),
        hostile: get_bool("Hostile"),
        respawn: get_bool("ReSpawn"),
        domable: get_int("Domable"),
        comercia: get_bool("Comercia"),
        inflacion: get_int("Inflacion"),
        tipo_items: get_int("TipoItems"),
        inv_respawn: get_bool("InvReSpawn"),
        min_hp: get_int("MinHP"),
        max_hp: get_int("MaxHP"),
        min_hit: get_int("MinHIT"),
        max_hit: get_int("MaxHIT"),
        def: get_int("DEF"),
        def_m: get_int("DEFm"),
        poder_ataque: get_int("PoderAtaque"),
        poder_evasion: get_int("PoderEvasion"),
        give_exp: get_int("GiveEXP"),
        give_gld: get_int("GiveGLD"),
        give_gld_min: get_int("GiveGLDMin"),
        give_gld_max: get_int("GiveGLDMax"),
        nro_items: get_int("NROITEMS"),
        items: {
            let nro = get_int("NROITEMS") as usize;
            let mut items = Vec::with_capacity(nro);
            for i in 1..=nro {
                let line = get_str(&format!("Obj{}", i));
                if !line.is_empty() {
                    // Format: ObjIndex-Amount-ProbTirar (delimiter '-', ASCII 45)
                    let parts: Vec<&str> = line.split('-').collect();
                    let obj_index = parts.first().and_then(|s| s.parse::<i32>().ok()).unwrap_or(0);
                    let amount = parts.get(1).and_then(|s| s.parse::<i32>().ok()).unwrap_or(0);
                    let prob_tirar = parts.get(2).and_then(|s| s.parse::<i32>().ok()).unwrap_or(0);
                    if obj_index > 0 {
                        items.push(NpcInvItem { obj_index, amount, prob_tirar });
                    }
                }
            }
            items
        },
        alineacion: get_int("Alineacion"),
        agua_valida: get_bool("AguaValida"),
        tierra_invalida: get_bool("TierraInvalida"),
        veneno: get_bool("Veneno"),
        lanza_spells: get_int("LanzaSpells"),
        spells: {
            let nro = get_int("LanzaSpells") as usize;
            let mut spells = Vec::with_capacity(nro);
            for i in 1..=nro {
                let sp = get_int(&format!("Sp{}", i));
                if sp > 0 {
                    spells.push(sp);
                }
            }
            spells
        },
        cristales: get_bool("Cristales"),
        crystal_min1: get_int("CristalMin1"),
        crystal_max1: get_int("CristalMax1"),
        crystal_min2: get_int("CristalMin2"),
        crystal_max2: get_int("CristalMax2"),
        crystal_min3: get_int("CristalMin3"),
        crystal_max3: get_int("CristalMax3"),
        crystal_min4: get_int("CristalMin4"),
        crystal_max4: get_int("CristalMax4"),
        give_pts: get_int("GivePTS"),
        snd1: get_int("SND1"),
        snd2: get_int("Snd2"),
        snd3: get_int("SND3"),
        aura: get_int("CreaAura"),
        nro_criaturas: get_int("NroCriaturas"),
        criaturas: {
            let nro = get_int("NroCriaturas") as usize;
            let mut criaturas = Vec::with_capacity(nro);
            for i in 1..=nro {
                let ci = get_int(&format!("CI{}", i));
                let cn = get_str(&format!("CN{}", i));
                criaturas.push(TrainerCreature { npc_index: ci, npc_name: cn });
            }
            criaturas
        },
    }
}

/// NPC database holding both normal and hostile NPCs.
#[derive(Debug)]
pub struct NpcDatabase {
    /// All NPCs indexed by their number (sparse — index 0 is empty).
    /// Normal NPCs: 1-499, Hostile NPCs: 500+.
    npcs: Vec<Option<NpcData>>,
}

impl NpcDatabase {
    /// Get an NPC by its index number.
    pub fn get(&self, index: usize) -> Option<&NpcData> {
        self.npcs.get(index).and_then(|n| n.as_ref())
    }

    /// Total number of NPCs loaded.
    pub fn count(&self) -> usize {
        self.npcs.iter().filter(|n| n.is_some()).count()
    }
}

/// Load both NPC databases and merge them.
pub fn load_npcs(base: &Path) -> Result<NpcDatabase, String> {
    let normal_path = base.join("dat").join("NPCs.dat");
    let hostile_path = base.join("dat").join("NPCs-HOSTILES.dat");

    // Start with enough capacity for index 1000+
    let mut npcs: Vec<Option<NpcData>> = vec![None; 1500];
    let mut count = 0;

    // Load normal NPCs (1-499)
    if let Ok(ini) = IniFile::load(&normal_path) {
        let num: usize = ini.get("INIT", "NumNPCs")
            .and_then(|s| s.parse().ok())
            .unwrap_or(0);

        for i in 1..=num {
            let section = format!("NPC{}", i);
            let name = ini.get(&section, "Name").unwrap_or_default();
            if !name.is_empty() {
                if i >= npcs.len() {
                    npcs.resize(i + 1, None);
                }
                npcs[i] = Some(load_npc_from_ini(&ini, &section, i));
                count += 1;
            }
        }
    }

    // Load hostile NPCs (500+)
    if let Ok(ini) = IniFile::load(&hostile_path) {
        // Scan for NPC sections starting at 500
        // The header says NumNPCs=1000 but actual entries start at [NPC500]
        for i in 500..1500 {
            let section = format!("NPC{}", i);
            let name = ini.get(&section, "Name").unwrap_or_default();
            if !name.is_empty() {
                if i >= npcs.len() {
                    npcs.resize(i + 1, None);
                }
                npcs[i] = Some(load_npc_from_ini(&ini, &section, i));
                count += 1;
            }
        }
    }

    tracing::info!("NPCs loaded: {} total (normal + hostile)", count);
    Ok(NpcDatabase { npcs })
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn load_real_npcs() {
        let base = Path::new(env!("CARGO_MANIFEST_DIR")).join("server");
        let base = base.as_path();
        if !base.join("dat").join("NPCs.dat").exists() {
            return;
        }
        let db = load_npcs(base).unwrap();
        assert!(db.count() > 50, "Should have many NPCs, got {}", db.count());

        // NPC1 = Aldeano
        let aldeano = db.get(1).expect("NPC1 should exist");
        assert_eq!(aldeano.name, "Aldeano");
        assert_eq!(aldeano.npc_type, NpcType::Common);
        assert!(!aldeano.hostile);

        // NPC500 = first hostile (Murcielago)
        let bat = db.get(500).expect("NPC500 should exist");
        assert!(bat.hostile, "NPC500 should be hostile");
    }
}
