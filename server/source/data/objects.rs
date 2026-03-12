// Objects database loader — dat/Obj.dat
//
// INI format with [INIT] NumOBJs and [OBJ1]..[OBJn] sections.
// Each object has variable fields depending on ObjType.
// 1664 objects in the current database.

use std::path::Path;
use crate::config::IniFile;

/// Object type enum matching VB6 eOBJType.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
#[repr(u8)]
pub enum ObjType {
    UseOnce = 1,
    Weapon = 2,
    Armor = 3,
    Trees = 4,
    Money = 5,
    Door = 6,
    Container = 7,
    Sign = 8,
    Key = 9,
    Forum = 10,
    Potion = 11,
    Drink = 13,
    Firewood = 14,
    Campfire = 15,
    Shield = 16,
    Helmet = 17,
    Tool = 18,
    Teleport = 19,
    Deposit = 22,
    Mineral = 23,
    Scroll = 24,
    Instrument = 26,
    Anvil = 27,
    Forge = 28,
    Boat = 31,
    Arrow = 32,
    EmptyBottle = 33,
    FullBottle = 34,
    Stain = 35,
    Ram = 36,
    Backpack = 37,       // otMochilas — expandable inventory backpack
    FishingSpot = 38,    // otYacimientoPez — fishing spot (VB6 13.3)
    GemEarth = 39,
    GemWind = 40,
    StorageBox = 42,
    GodBox = 43,
    Fragment = 45,
    ResurrectPotion = 46,
    RandomChest = 47,
    Mount = 48,
    JDHChest = 49,
    ScrollItem = 50,
    Sack = 51,
    RenounceHorde = 52,   // otRenunciaH — renounce Chaos faction
    ClanUpgrade6 = 53,    // otSubeClan6 — guild level 5→6
    ClanUpgrade7 = 54,    // otSubeClan7 — guild level 6→7
    RenounceRoyal = 55,   // otRenunciaA — renounce Royal faction
    Unknown = 255,
}

impl ObjType {
    fn from_i32(v: i32) -> Self {
        match v {
            1 => Self::UseOnce,
            2 => Self::Weapon,
            3 => Self::Armor,
            4 => Self::Trees,
            5 => Self::Money,
            6 => Self::Door,
            7 => Self::Container,
            8 => Self::Sign,
            9 => Self::Key,
            10 => Self::Forum,
            11 => Self::Potion,
            13 => Self::Drink,
            14 => Self::Firewood,
            15 => Self::Campfire,
            16 => Self::Shield,
            17 => Self::Helmet,
            18 => Self::Tool,
            19 => Self::Teleport,
            22 => Self::Deposit,
            23 => Self::Mineral,
            24 => Self::Scroll,
            26 => Self::Instrument,
            27 => Self::Anvil,
            28 => Self::Forge,
            31 => Self::Boat,
            32 => Self::Arrow,
            33 => Self::EmptyBottle,
            34 => Self::FullBottle,
            35 => Self::Stain,
            36 => Self::Ram,
            37 => Self::Backpack,
            38 => Self::FishingSpot,
            39 => Self::GemEarth,
            40 => Self::GemWind,
            42 => Self::StorageBox,
            43 => Self::GodBox,
            45 => Self::Fragment,
            46 => Self::ResurrectPotion,
            47 => Self::RandomChest,
            48 => Self::Mount,
            49 => Self::JDHChest,
            50 => Self::ScrollItem,
            51 => Self::Sack,
            52 => Self::RenounceHorde,
            53 => Self::ClanUpgrade6,
            54 => Self::ClanUpgrade7,
            55 => Self::RenounceRoyal,
            _ => Self::Unknown,
        }
    }
}

/// Object data matching VB6 ObjData type.
/// Only commonly-used fields are explicitly typed; others stored in `extra`.
#[derive(Debug, Clone)]
pub struct ObjData {
    pub index: usize,
    pub name: String,
    pub obj_type: ObjType,
    pub grh_index: i32,
    pub valor: i32,          // Item value/price
    pub agarrable: bool,     // VB6: Agarrable=1 means FIXED (cannot pick up)

    // Weapon fields
    pub min_hit: i32,
    pub max_hit: i32,
    pub weapon_anim: i32,
    pub dos_manos: bool,     // Two-handed
    pub proyectil: bool,     // Ranged
    pub municion: i32,       // Ammo type (obj index)

    // Armor/defense fields
    pub min_def: i32,
    pub max_def: i32,
    pub shield_anim: i32,
    pub casco_anim: i32,

    // Door fields
    pub llave: i32,
    pub cerrada: i32,          // 1=closed, 0=open (VB6: abierta field)
    pub puerta_doble: i32,     // 1=double door (4-tile width)
    pub porton: i32,           // 1=grand gate (5-tile width)
    pub reja_forta: i32,       // 1=fortress gate (guild-owned)
    pub clave: i32,            // Key code for locked doors
    pub index_abierta: i32,
    pub index_cerrada: i32,
    // Potion/food fields
    pub tipo_pocion: i32,    // Potion subtype: 1=agility, 2=strength, 3=HP, 4=mana, 5=cure poison, 6=remo
    pub min_modificador: i32,
    pub max_modificador: i32,
    pub duracion_efecto: i32,
    pub min_ham: i32,        // Food restoration
    pub min_agua: i32,       // Drink restoration

    // Spell item
    pub hechizo_index: i32,

    // Equipment requirements
    pub min_skill: i32,
    pub num_ropaje: i32,     // Body graphic when equipped (mount)

    // Crafting
    pub sk_herreria: i32,
    pub sk_carpinteria: i32,
    pub ling_h: i32,         // Iron ingots
    pub ling_o: i32,         // Gold ingots
    pub ling_p: i32,         // Silver ingots

    // Crafting (extended)
    pub madera: i32,          // Wood needed (carpentry)
    pub piedras: i32,         // Magic stones needed (carpentry)
    pub mineral_index: i32,   // What mineral a yacimiento yields
    pub lingote_index: i32,   // What ingot a raw mineral produces
    pub real: bool,           // Faction: Royal Army
    pub caos: bool,           // Faction: Chaos
    pub item_dios: bool,      // GM-only item
    pub intransferible: bool, // Cannot be traded

    // Flags
    pub cura_veneno: bool,
    pub envenena: bool,      // Weapon poisons on hit (60% chance)
    pub refuerzo: i32,       // Weapon penetration (reduces armor absorption)
    pub es_voladora: bool,   // Flying mount
    pub newbie: bool,

    // Class restrictions (empty = no restriction)
    pub class_prohibida: Vec<String>,

    // Level requirement
    pub lvl: i32,             // Minimum level to equip (0 = no req)
    pub raza_enana: bool,     // Dwarf race only
    pub raza_doble: bool,     // All races can use
    pub mujer: bool,          // Female only
    pub hombre: bool,         // Male only

    // Sound
    pub snd1: i32,            // VB6 Snd1 — sound ID (used by instruments)

    // Paralysis on hit
    pub paraliza: bool,       // Weapon paralyzes on hit

    // Aura (VB6: CreaAura — aura index from Auras.dat, 0 = no aura)
    pub crea_aura: i32,

    // Magic defense (VB6: DefensaMagicaMin/Max — helmets and rings)
    pub defensa_magica_min: i32,
    pub defensa_magica_max: i32,

    // Staff fields (VB6: StaffPower, StaffDamageBonus — mage weapons)
    pub staff_power: i32,
    pub staff_damage_bonus: i32,

    // Pirate throat-cut (VB6: Acuchilla — weapon flag for DoAcuchillar)
    pub acuchilla: bool,

    // Upgrade target (VB6: Upgrade — obj index of upgraded version)
    pub upgrade: i32,

    // Forum identifier (VB6: ForoID — string ID linking object to a forum board)
    pub foro_id: String,

    // Backpack (VB6: MochilaType — 1=small +5 slots, 2=large +10 slots)
    pub mochila_type: i32,
}

impl Default for ObjData {
    fn default() -> Self {
        Self {
            index: 0,
            name: String::new(),
            obj_type: ObjType::Unknown,
            grh_index: 0,
            valor: 0,
            agarrable: false,
            min_hit: 0,
            max_hit: 0,
            weapon_anim: 0,
            dos_manos: false,
            proyectil: false,
            municion: 0,
            min_def: 0,
            max_def: 0,
            shield_anim: 0,
            casco_anim: 0,
            llave: 0,
            cerrada: 0,
            puerta_doble: 0,
            porton: 0,
            reja_forta: 0,
            clave: 0,
            index_abierta: 0,
            index_cerrada: 0,
            tipo_pocion: 0,
            min_modificador: 0,
            max_modificador: 0,
            duracion_efecto: 0,
            min_ham: 0,
            min_agua: 0,
            hechizo_index: 0,
            min_skill: 0,
            num_ropaje: 0,
            sk_herreria: 0,
            sk_carpinteria: 0,
            ling_h: 0,
            ling_o: 0,
            ling_p: 0,
            madera: 0,
            piedras: 0,
            mineral_index: 0,
            lingote_index: 0,
            real: false,
            caos: false,
            item_dios: false,
            intransferible: false,
            cura_veneno: false,
            envenena: false,
            refuerzo: 0,
            es_voladora: false,
            newbie: false,
            class_prohibida: Vec::new(),
            lvl: 0,
            raza_enana: false,
            raza_doble: false,
            mujer: false,
            hombre: false,
            snd1: 0,
            paraliza: false,
            crea_aura: 0,
            defensa_magica_min: 0,
            defensa_magica_max: 0,
            staff_power: 0,
            staff_damage_bonus: 0,
            acuchilla: false,
            upgrade: 0,
            foro_id: String::new(),
            mochila_type: 0,
        }
    }
}

/// Load the complete objects database.
/// Returns a Vec where index 0 is OBJ1, index 1 is OBJ2, etc.
pub fn load_objects(base: &Path) -> Result<Vec<ObjData>, String> {
    let path = base.join("dat").join("Obj.dat");
    let ini = IniFile::load(&path)
        .map_err(|e| format!("Failed to load Obj.dat: {}", e))?;

    let num_objs: usize = ini.get("INIT", "NumOBJs")
        .or_else(|| ini.get("INIT", "Numobjs"))
        .and_then(|s| s.parse().ok())
        .unwrap_or(0);

    if num_objs == 0 {
        return Err("Obj.dat: NumOBJs is 0 or missing".into());
    }

    let mut objects = Vec::with_capacity(num_objs);

    for i in 1..=num_objs {
        let section = format!("OBJ{}", i);

        let get_str = |key: &str| -> String {
            ini.get(&section, key).unwrap_or_default()
        };
        let get_int = |key: &str| -> i32 {
            ini.get(&section, key).and_then(|s| s.parse().ok()).unwrap_or(0)
        };
        let get_bool = |key: &str| -> bool {
            ini.get(&section, key).map(|s| s == "1").unwrap_or(false)
        };

        let mut obj = ObjData {
            index: i,
            name: get_str("Name"),
            obj_type: ObjType::from_i32(get_int("ObjType")),
            grh_index: get_int("GrhIndex"),
            valor: get_int("Valor"),
            agarrable: get_bool("Agarrable"),
            min_hit: get_int("MinHIT"),
            max_hit: get_int("MaxHIT"),
            weapon_anim: 0,  // Set below based on ObjType (VB6: all use "Anim" field)
            dos_manos: get_bool("DosManos"),
            proyectil: get_bool("proyectil"),
            municion: get_int("Municion"),
            min_def: get_int("MINDEF"),
            max_def: get_int("MAXDEF"),
            shield_anim: 0,  // Set below based on ObjType
            casco_anim: 0,   // Set below based on ObjType
            llave: get_int("llave"),
            cerrada: get_int("abierta"),  // VB6 field "abierta" maps to Cerrada (1=closed)
            puerta_doble: get_int("PuertaDoble"),
            porton: get_int("Porton"),
            reja_forta: get_int("RejaForta"),
            clave: get_int("clave"),
            index_abierta: get_int("IndexAbierta"),
            index_cerrada: get_int("IndexCerrada"),
            tipo_pocion: get_int("TipoPocion"),
            min_modificador: get_int("MinModificador"),
            max_modificador: get_int("MaxModificador"),
            duracion_efecto: get_int("DuracionEfecto"),
            min_ham: get_int("MinHAM"),
            min_agua: get_int("MinAGU"),
            hechizo_index: get_int("HechizoIndex"),
            min_skill: get_int("MinSkill"),
            num_ropaje: get_int("NumRopaje"),
            sk_herreria: get_int("SkHerreria"),
            sk_carpinteria: get_int("SkCarpinteria"),
            ling_h: get_int("LingH"),
            ling_o: get_int("LingO"),
            ling_p: get_int("LingP"),
            madera: get_int("Madera"),
            piedras: get_int("Piedras"),
            mineral_index: get_int("MineralIndex"),
            lingote_index: get_int("LingoteIndex"),
            real: get_bool("Real"),
            caos: get_bool("Caos"),
            item_dios: get_bool("ItemDios"),
            intransferible: get_bool("Intransferible"),
            cura_veneno: get_bool("CuraVeneno"),
            envenena: get_bool("Envenena"),
            refuerzo: get_int("Refuerzo"),
            es_voladora: get_bool("esVoladora"),
            newbie: get_bool("Newbie"),
            lvl: get_int("lvl"),
            raza_enana: get_bool("RazaEnana"),
            raza_doble: get_bool("RazaDoble"),
            mujer: get_bool("Mujer"),
            hombre: get_bool("Hombre"),
            snd1: get_int("Snd1"),
            paraliza: get_bool("Paraliza"),
            crea_aura: get_int("CreaAura"),
            defensa_magica_min: get_int("DefensaMagicaMin"),
            defensa_magica_max: get_int("DefensaMagicaMax"),
            staff_power: get_int("StaffPower"),
            staff_damage_bonus: get_int("StaffDamageBonus"),
            acuchilla: get_bool("Acuchilla"),
            upgrade: get_int("Upgrade"),
            foro_id: get_str("ForoID"),
            mochila_type: get_int("MochilaType"),
            ..Default::default()
        };

        // VB6: All equipment types use the same "Anim" field in Obj.dat,
        // but it maps to different struct fields based on ObjType:
        //   Weapon (2) → weapon_anim, Shield (16) → shield_anim, Helmet (17) → casco_anim
        let anim_value = get_int("Anim");
        match obj.obj_type {
            ObjType::Weapon => obj.weapon_anim = anim_value,
            ObjType::Shield => obj.shield_anim = anim_value,
            ObjType::Helmet => obj.casco_anim = anim_value,
            _ => {
                // Some other types might use Anim too (e.g. Tool); store in weapon_anim as fallback
                obj.weapon_anim = anim_value;
            }
        }

        // Load class restrictions (CP1..CP8)
        for cp in 1..=8 {
            let class = get_str(&format!("CP{}", cp));
            if !class.is_empty() {
                obj.class_prohibida.push(class);
            }
        }

        objects.push(obj);
    }

    // Debug: verify shield/helmet anim values were loaded correctly
    let shields_with_anim: Vec<_> = objects.iter().enumerate()
        .filter(|(_, o)| o.obj_type == ObjType::Shield && o.shield_anim > 0)
        .map(|(i, o)| (i + 1, &o.name, o.shield_anim))
        .collect();
    let helmets_with_anim: Vec<_> = objects.iter().enumerate()
        .filter(|(_, o)| o.obj_type == ObjType::Helmet && o.casco_anim > 0)
        .map(|(i, o)| (i + 1, &o.name, o.casco_anim))
        .collect();
    tracing::info!("Objects loaded: {} items ({} shields with anim, {} helmets with anim)",
        objects.len(), shields_with_anim.len(), helmets_with_anim.len());
    if !shields_with_anim.is_empty() {
        tracing::debug!("Shield anims: {:?}", &shields_with_anim[..shields_with_anim.len().min(5)]);
    }
    if !helmets_with_anim.is_empty() {
        tracing::debug!("Helmet anims: {:?}", &helmets_with_anim[..helmets_with_anim.len().min(5)]);
    }
    Ok(objects)
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn load_real_objects() {
        let base = Path::new(env!("CARGO_MANIFEST_DIR")).join("server");
        let base = base.as_path();
        if !base.join("dat").join("Obj.dat").exists() {
            return;
        }
        let objs = load_objects(base).unwrap();
        assert!(objs.len() > 100, "Should have many objects, got {}", objs.len());

        // First object should be "Manzana Roja"
        assert_eq!(objs[0].name, "Manzana Roja");
        assert_eq!(objs[0].obj_type, ObjType::UseOnce);

        // Second object should be a weapon
        assert_eq!(objs[1].name, "Espada Larga");
        assert_eq!(objs[1].obj_type, ObjType::Weapon);
        assert!(objs[1].max_hit > 0);
    }
}
