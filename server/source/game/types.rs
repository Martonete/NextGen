// Core game types — per-connection state and global server state.

use std::collections::HashMap;
use std::path::PathBuf;

use crate::config::ServerConfig;
use crate::db::bans::BanList;
use crate::data::GameData;
use crate::data::objects::ObjType;
use crate::data::ranking::RankingData;
use sqlx::PgPool;
use crate::net::ConnectionId;
use crate::net::connection::ConnectionWriter;
use super::world::{self, CharIndex, WorldState};
use super::npc::{NpcState, NpcIndex};

/// Maximum inventory slots (VB6: MAX_INVENTORY_SLOTS = 25)
pub const MAX_INVENTORY_SLOTS: usize = 25;

/// Maximum spell slots (VB6: MAXUSERHECHIZOS)
pub const MAX_SPELL_SLOTS: usize = 20;

/// Maximum bank slots (VB6: MAX_BANCOINVENTORY_SLOTS = 40)
pub const MAX_BANK_SLOTS: usize = 40;

/// An inventory slot — object index, amount, and equipped flag.
#[derive(Debug, Clone, Default)]
pub struct InventorySlot {
    pub obj_index: i32,  // 0 = empty
    pub amount: i32,
    pub equipped: bool,
}

/// Equipment slot indices (which inventory slot holds each equipment type).
#[derive(Debug, Clone, Default)]
pub struct EquipSlots {
    pub weapon: usize,   // WeaponEqpSlot
    pub armor: usize,    // ArmourEqpSlot
    pub shield: usize,   // EscudoEqpSlot
    pub helmet: usize,   // CascoEqpSlot
    pub municion: usize, // MunicionEqpSlot (arrows)
    pub ring: usize,     // AnilloEqpSlot (ring/tool)
}

/// Per-connection user state (tracks the login flow and in-game state).
#[derive(Debug)]
pub struct UserState {
    pub conn_id: ConnectionId,
    pub ip: String,

    // Pre-login state (set by KERD22)
    pub hd_serial: String,
    pub paso_hd: bool,

    // Account state (set by ALOGIN)
    pub account_name: String,
    pub account_password: String,
    pub account_id: i32,

    // Character state (set by THCJXD/OOLOGI)
    pub char_name: String,
    pub logged: bool,

    // Dice roll state (TIRDAD) — used during character creation only
    pub dice_attributes: [i32; 5],

    // In-game position and character data (set after LOGGED)
    pub pos_map: i32,
    pub pos_x: i32,
    pub pos_y: i32,
    pub heading: i32,
    pub char_index: CharIndex,


    // Character appearance (for CC packet)
    pub body: i32,
    pub head: i32,
    /// VB6: OrigChar.Head — original head from charfile, never modified at runtime.
    /// Used to restore head on revive (since `head` gets set to dead head 500 on death).
    pub orig_head: i32,
    pub weapon_anim: i32,
    pub shield_anim: i32,
    pub casco_anim: i32,
    pub privileges: i32,
    /// Original privileges from DB — runtime /PJ changes are temporary, this is what gets saved
    pub saved_privileges: i32,

    // Auras (VB6: Char.AuraA/W/E/R/C — set from equipped item CreaAura by type)
    pub aura_a: i32,     // Armor aura
    pub aura_w: i32,     // Weapon aura
    pub aura_e: i32,     // Shield aura
    pub aura_r: i32,     // Ring/Tool aura
    pub aura_c: i32,     // Helmet aura

    // Stats
    pub class: String,
    pub race: String,
    pub level: i32,
    pub exp: i64,
    pub max_hp: i32,
    pub min_hp: i32,
    pub max_mana: i32,
    pub min_mana: i32,
    pub max_sta: i32,
    pub min_sta: i32,
    pub max_hit: i32,
    pub min_hit: i32,
    pub gold: i64,
    pub max_agua: i32,
    pub min_agua: i32,
    pub max_ham: i32,
    pub min_ham: i32,
    pub attributes: [i32; 5], // Str, Agi, Int, Cha, Con
    pub skills: [i32; 22],
    pub skill_pts_libres: i32,  // Free skill points to distribute
    pub reputation: i32,

    // Inventory (25 slots, 1-indexed in VB6 but 0-indexed here)
    pub inventory: Vec<InventorySlot>,
    pub equip: EquipSlots,

    // Spells (20 slots)
    pub spells: [i32; MAX_SPELL_SLOTS],

    // Flags
    pub dead: bool,
    pub hidden: bool,
    pub paralyzed: bool,
    pub immobilized: bool,    // VB6 flags.Inmovilizado — separate from paralyzed
    pub meditating: bool,
    pub poisoned: bool,
    pub invisible: bool,
    pub safe_toggle: bool,  // PvP safety (SEG)
    pub criminal: bool,
    pub navigating: bool,   // On a boat
    pub transformed: bool,  // Demon/Angel transformation active
    pub gender: i32,        // 1=Male, 2=Female (from charfile Genero)
    pub es_noble: bool,     // Has noble rank (VB6 flags.EsNoble)
    pub en_guerra: bool,    // Enrolled in current war event
    pub comerciando: bool,  // In NPC commerce window
    pub target_npc: usize,  // NPC runtime index for commerce/interaction

    // Bank (40 slots, stored in account file)
    pub bank: Vec<InventorySlot>,
    pub bank_gold: i64,

    // Player trading
    pub trading: bool,
    pub trade_partner: Option<ConnectionId>,
    pub trade_offered: bool,    // Has offered items
    pub trade_accepted: bool,   // Has accepted
    pub trade_gold: i64,        // Gold being offered
    pub trade_items: Vec<InventorySlot>, // Items being offered (max 20)

    // Guild
    pub guild_index: i32,       // 0 = no guild
    pub guild_creating_alignment: i32, // Temp: alignment during guild creation flow
    pub puede_retirar_obj: bool, // Guild bank: can withdraw items
    pub puede_retirar_oro: bool, // Guild bank: can withdraw gold
    pub cuenta_bancaria: String, // Guild name when clan bank is open (lock)
    pub clan_bank: Vec<InventorySlot>, // Clan bank inventory (loaded from .bov)

    // Factions
    pub armada_real: bool,       // In Royal Army
    pub fuerzas_caos: bool,      // In Chaos Forces
    pub criminales_matados: i32, // Criminal kill count
    pub ciudadanos_matados: i32, // Citizen kill count
    pub recompensas_real: i32,   // Royal reward tier (0-5)
    pub recompensas_caos: i32,   // Chaos reward tier (0-5)
    pub reenlistadas: bool,      // Can only enlist once per character
    pub last_crim_matado: String, // Kill dedup: last criminal killed by name
    pub last_ciud_matado: String, // Kill dedup: last citizen killed by name

    // Party
    pub party_index: i32,        // 0 = no party
    pub party_pending: i32,      // Party index of pending invite (0 = none)

    // Quests
    pub questeando: bool,        // Currently on a quest
    pub quest_num: i32,          // Quest ID (1-31, 0=none)
    pub quest_kills: i32,        // Kill counter for current quest
    pub quests_completed: i32,   // Lifetime quests completed

    // Duel/Arena
    pub en_duelo: bool,          // In an arena duel
    pub dueliando_contra: String, // Name of duel opponent
    pub le_mandaron_duelo: bool,  // Has a pending duel challenge
    pub ultimo_en_mandar_duelo: String, // Who sent the challenge
    pub en_que_arena: i32,       // Which arena (1-4, 0=none)
    pub apuesta_oro: i64,        // Gold bet for duel
    pub mapa_anterior: i32,      // Saved map before duel/event
    pub x_anterior: i32,         // Saved X before duel/event
    pub y_anterior: i32,         // Saved Y before duel/event

    // Desafio (1v1 challenge)
    pub en_desafio: bool,
    pub rondas: i32,             // Rounds won as defender

    // CvC
    pub en_cvc: bool,
    pub seguro_cvc: bool,        // CvC safety toggle

    // Tournament
    pub en_torneo: bool,
    pub num_torneo: i32,         // Tournament slot number

    // Pets/Summons
    pub nro_mascotas: i32,              // Active pet count (max 3)
    pub mascotas_index: [usize; 3],     // NPC runtime indices of pets
    pub mascotas_type: [i32; 3],        // NPC type numbers of pets

    // Druid Elementals (VB6: flags.EleDeFuego/EleDeAgua/EleDeTierra)
    pub ele_de_fuego: bool,             // Has active Fire Elemental (NPC 93)
    pub ele_de_agua: bool,              // Has active Water Elemental (NPC 92)
    pub ele_de_tierra: bool,            // Has active Earth Elemental (NPC 94)

    // Buff/Potion system
    pub duracion_efecto: i32,           // Buff ticks remaining (0 = no buff)
    pub tomo_pocion: bool,              // Currently under a buff effect
    pub attributes_backup: [i32; 5],    // Stat backup for buff expiry restoration

    // Resurrection
    pub seguro_resu: bool,              // Resurrection safety (opt-out of being rezzed)
    pub time_revivir: i32,              // Cooldown ticks after death before rezzing allowed
    pub segundos_para_revivir: i32,     // Delayed resurrection countdown (non-cleric)

    // Target tracking (for ranged attacks, spells, and GM teleport)
    pub target_user: ConnectionId,      // Last targeted user
    pub target_npc_idx: usize,          // Last targeted NPC (runtime index)
    pub target_x: i32,                  // Last left-click X (for /TELEPLOC)
    pub target_y: i32,                  // Last left-click Y (for /TELEPLOC)
    pub target_map: i32,                // Last left-click map (for /TELEPLOC)
    pub target_obj_map: i32,            // VB6: flags.TargetObjMap
    pub target_obj_x: i32,              // VB6: flags.TargetObjX
    pub target_obj_y: i32,              // VB6: flags.TargetObjY
    pub pending_spell: usize,           // VB6 flags.Hechizo — spell slot selected via LH, cast on next RC click
    pub counter_paralisis: i32,         // VB6 Counters.Paralisis — countdown to auto-remove paralysis
    pub counter_invisible: i32,         // VB6 Counters.Invisibilidad — counts up to IntervaloInvisible
    pub counter_oculto: i32,            // VB6 Counters.TiempoOculto — counts down to 0 (hide duration)

    // Timer counters (incremented each game tick, reset when action fires)
    pub counter_hunger: i32,   // Hunger drain counter
    pub counter_thirst: i32,   // Thirst drain counter
    pub counter_stamina: i32,  // Stamina regen counter
    pub counter_poison: i32,   // Poison damage counter
    pub counter_remo: i32,     // Remo potion cooldown (VB6: usoPotaRemo, 3 rounds)

    // Area system (VB6 ModAreas — 9x9 tile zones for visibility updates)
    pub area_id: i32,        // (x/9+1)*(y/9+1), 0 = uninitialized
    pub area_min_x: i32,     // Left edge of current 27-wide area
    pub area_min_y: i32,     // Top edge of current 27-tall area

    // Anti-cheat cooldown counters (decremented each tick, 0 = action allowed)
    pub interval_golpe: i32,     // Melee attack cooldown
    pub interval_flechas: i32,   // Arrow shot cooldown
    pub interval_casteo: i32,    // Spell cast cooldown
    pub interval_poteo: i32,     // Potion use cooldown
    pub interval_click: i32,     // Click action cooldown
    pub interval_trabajar: i32,  // Work/skill cooldown
    pub interval_pu: i32,        // Position update cooldown

    // Admin / moderation flags
    pub admin_invisible: bool,    // GM invisible mode (body=0, head=0)
    pub old_body: i32,            // Saved body before going invisible
    pub old_head: i32,            // Saved head before going invisible
    pub emoticons: bool,          // Emoticons enabled (toggle via /EMOTICONS)
    pub silenced: bool,           // Muted by GM
    pub silence_timer: i32,       // Mute countdown in seconds (0 = permanent until toggled)
    pub jail_timer: i32,          // Jail countdown in seconds (0 = not jailed)
    pub warnings: i32,            // Warning count (advertencias)
    pub hogar: String,            // Home city (Thir, Inthak, Ruvendel, etc.)

    // Navigation — barco_slot is the inventory slot (1-based) holding the equipped boat (VB6 BarcoSlot)
    pub barco_slot: usize,

    // Event system flags
    pub en_evento: bool,          // In any event
    pub evento_tipo: i32,         // 0=none, 1=CTF, 2=JDH, 3=LUZ, 4=ARAM, 5=BatMistica, 6=Faccionario, 7=TorneoAuto, 8=Guerra
    pub evento_equipo: i32,       // Team number (1=Azul/Alianza, 2=Rojo/Horda, 3=Amarillo, 4=Verde)
    pub evento_muertes: i32,      // Deaths in current event (for respawn delay calc)
    pub evento_seconds: i32,      // Respawn countdown seconds
    pub not_move: bool,           // Paralyzed during event countdown
    pub torneo_auto: bool,        // In automatic tournament
    pub torneo_auto_slot: i32,    // Slot in bracket
    pub torneo_auto_muerto: bool, // Dead in auto tournament

    // SOS/Consultation system
    pub consulta_enviada: bool,    // Has pending consultation
    pub numero_consulta: i32,      // SOS message index

    // Macro detection
    pub tiene_macro: i32,          // Macro detection counter

    // Points
    pub puntos_donacion: i64,      // Donation points
    pub puntos_torneo: i64,        // Tournament points
    pub ts_points: i64,            // TS points

    // Scroll buffs (VB6: activoScroll, Scrolls)
    // Index 0..3 for typeScroll 1..4 (exp, gold, drop, crystal drop)
    pub scroll_active: [bool; 4],
    pub scroll_time: [i32; 4],
    pub scroll_mult: [i32; 4],

    // Private messages toggle
    pub msj_privados: bool,        // Receive private messages

    // Friends chat
    pub nombre_amigo: [String; 10], // Friends list names

    // Montado (mounted)
    pub montado: bool,             // Is currently mounted
    pub montado_body: i32,         // Original body before mounting
    pub levitando: bool,           // Flying mount levitation

    // Divine system (Dioses)
    pub sirviente_de_dios: String,   // God name: Mifrit, Poseidon, Tarraske, Erebros
    pub almas_contenidas: i64,       // Contained souls
    pub almas_ofrecidas: i64,        // Offered souls
    pub jerarquia_dios: i32,         // God rank (1-5)
    pub cofre_dios: [i32; 4],        // God chest item indices (4 slots)
    pub cofre_dios_cant: i32,        // God chest item count

    // Arena spectator
    pub espectador_arena1: bool,
    pub espectador_arena2: bool,
    pub espectador_arena3: bool,
    pub espectador_arena4: bool,

    // CvC extended
    pub puede_entrar_cvc: bool,
    pub cvc_blue: bool,              // Team flag in CvC
    pub vieja_pos_map: i32,          // Save position for CvC return
    pub vieja_pos_x: i32,
    pub vieja_pos_y: i32,

    // 2vs2 pareja
    pub espera_pareja: bool,
    pub su_pareja: ConnectionId,
    pub en_pareja: bool,

    // Command cooldown
    pub time_comandos: i32,          // Cooldown for /PAREJA etc.

    // Description
    pub desc: String,                // User description (/DESC)

    // Marriage (VB6: Pareja)
    pub pareja: String,              // Name of spouse (empty = not married)
}

impl UserState {
    pub fn new(conn_id: ConnectionId, ip: String) -> Self {
        Self {
            conn_id,
            ip,
            hd_serial: String::new(),
            paso_hd: false,
            account_name: String::new(),
            account_password: String::new(),
            account_id: 0,
            char_name: String::new(),
            logged: false,
            dice_attributes: [18; 5],

            pos_map: 0,
            pos_x: 0,
            pos_y: 0,
            heading: world::HEADING_SOUTH,
            char_index: CharIndex(0),
            body: 0,
            head: 0,
            orig_head: 0,
            weapon_anim: 0,
            shield_anim: 0,
            casco_anim: 0,
            privileges: 0,
            saved_privileges: 0,
            aura_a: 0,
            aura_w: 0,
            aura_e: 0,
            aura_r: 0,
            aura_c: 0,
            class: String::new(),
            race: String::new(),
            level: 1,
            exp: 0,
            max_hp: 0,
            min_hp: 0,
            max_mana: 0,
            min_mana: 0,
            max_sta: 0,
            min_sta: 0,
            max_hit: 0,
            min_hit: 0,
            gold: 0,
            max_agua: 100,
            min_agua: 100,
            max_ham: 100,
            min_ham: 100,
            attributes: [18; 5],
            skills: [0; 22],
            skill_pts_libres: 0,
            reputation: 0,
            inventory: (0..MAX_INVENTORY_SLOTS).map(|_| InventorySlot::default()).collect(),
            equip: EquipSlots::default(),
            spells: [0; MAX_SPELL_SLOTS],
            dead: false,
            hidden: false,
            paralyzed: false,
            immobilized: false,
            meditating: false,
            poisoned: false,
            invisible: false,
            safe_toggle: true, // Safety ON by default
            criminal: false,
            navigating: false,
            transformed: false,
            gender: 1,
            es_noble: false,
            en_guerra: false,
            comerciando: false,
            target_npc: 0,
            bank: (0..MAX_BANK_SLOTS).map(|_| InventorySlot::default()).collect(),
            bank_gold: 0,
            trading: false,
            trade_partner: None,
            trade_offered: false,
            trade_accepted: false,
            trade_gold: 0,
            trade_items: Vec::new(),
            guild_index: 0,
            guild_creating_alignment: 0,
            puede_retirar_obj: false,
            puede_retirar_oro: false,
            cuenta_bancaria: String::new(),
            clan_bank: Vec::new(),
            armada_real: false,
            fuerzas_caos: false,
            criminales_matados: 0,
            ciudadanos_matados: 0,
            recompensas_real: 0,
            recompensas_caos: 0,
            reenlistadas: false,
            last_crim_matado: String::new(),
            last_ciud_matado: String::new(),
            party_index: 0,
            party_pending: 0,
            questeando: false,
            quest_num: 0,
            quest_kills: 0,
            quests_completed: 0,
            en_duelo: false,
            dueliando_contra: String::new(),
            le_mandaron_duelo: false,
            ultimo_en_mandar_duelo: String::new(),
            en_que_arena: 0,
            apuesta_oro: 0,
            mapa_anterior: 0,
            x_anterior: 0,
            y_anterior: 0,
            en_desafio: false,
            rondas: 0,
            en_cvc: false,
            seguro_cvc: true,
            en_torneo: false,
            num_torneo: 0,
            nro_mascotas: 0,
            mascotas_index: [0; 3],
            mascotas_type: [0; 3],
            ele_de_fuego: false,
            ele_de_agua: false,
            ele_de_tierra: false,
            duracion_efecto: 0,
            tomo_pocion: false,
            attributes_backup: [0; 5],
            seguro_resu: false,
            time_revivir: 0,
            segundos_para_revivir: 0,
            target_user: 0,
            target_npc_idx: 0,
            target_x: 0,
            target_y: 0,
            target_map: 0,
            target_obj_map: 0,
            target_obj_x: 0,
            target_obj_y: 0,
            pending_spell: 0,
            counter_paralisis: 0,
            counter_invisible: 0,
            counter_oculto: 0,
            counter_hunger: 0,
            counter_thirst: 0,
            counter_stamina: 0,
            counter_poison: 0,
            counter_remo: 0,
            area_id: 0,
            area_min_x: 0,
            area_min_y: 0,
            interval_golpe: 0,
            interval_flechas: 0,
            interval_casteo: 0,
            interval_poteo: 0,
            interval_click: 0,
            interval_trabajar: 0,
            interval_pu: 0,
            admin_invisible: false,
            old_body: 0,
            old_head: 0,
            emoticons: false,
            silenced: false,
            silence_timer: 0,
            jail_timer: 0,
            warnings: 0,
            hogar: String::new(),
            barco_slot: 0,
            en_evento: false,
            evento_tipo: 0,
            evento_equipo: 0,
            evento_muertes: 0,
            evento_seconds: 0,
            not_move: false,
            torneo_auto: false,
            torneo_auto_slot: 0,
            torneo_auto_muerto: false,
            consulta_enviada: false,
            numero_consulta: 0,
            tiene_macro: 0,
            puntos_donacion: 0,
            puntos_torneo: 0,
            ts_points: 0,
            scroll_active: [false; 4],
            scroll_time: [0; 4],
            scroll_mult: [0; 4],
            msj_privados: true,
            nombre_amigo: Default::default(),
            montado: false,
            montado_body: 0,
            levitando: false,
            sirviente_de_dios: String::new(),
            almas_contenidas: 0,
            almas_ofrecidas: 0,
            jerarquia_dios: 0,
            cofre_dios: [0; 4],
            cofre_dios_cant: 0,
            espectador_arena1: false,
            espectador_arena2: false,
            espectador_arena3: false,
            espectador_arena4: false,
            puede_entrar_cvc: false,
            cvc_blue: false,
            vieja_pos_map: 0,
            vieja_pos_x: 0,
            vieja_pos_y: 0,
            espera_pareja: false,
            su_pareja: 0,
            en_pareja: false,
            time_comandos: 0,
            desc: String::new(),
            pareja: String::new(),
        }
    }

    /// Build binary CC (CharacterCreate) packet for this user.
    pub fn build_cc_binary(&self) -> Vec<u8> {
        let nick_color = if self.criminal { 2u8 } else { 1u8 };
        crate::protocol::binary_packets::write_character_create(
            self.char_index.0 as i16,
            self.body as i16,
            self.head as i16,
            self.heading as u8,
            self.pos_x as u8,
            self.pos_y as u8,
            self.weapon_anim as i16,
            self.shield_anim as i16,
            self.casco_anim as i16,
            0, 0, // fx_index, fx_loops
            &self.char_name,
            nick_color,
            self.privileges as u8,
        )
    }
}

/// Routing target for SendData (matches VB6 SendTarget enum).
#[derive(Debug, Clone, Copy)]
pub enum SendTarget {
    ToIndex(ConnectionId),
    ToAll,
    ToMap(i32),
    ToArea { map: i32, x: i32, y: i32 },
    ToAreaButIndex { conn_id: ConnectionId, map: i32, x: i32, y: i32 },
    ToMapButIndex { conn_id: ConnectionId, map: i32 },
    ToGuildMembers(i32), // Send to all online members of guild_index
    ToAdmins,            // Send to all GMs (privileges > 0)
}

/// VB6 PlayerType privilege levels (must match Declares.bas)
pub mod privilege_level {
    pub const USER: i32 = 0;
    pub const CONSEJERO: i32 = 1;
    pub const SEMIDIOS: i32 = 2;
    pub const EVENT_MASTER: i32 = 3;
    pub const DIOS: i32 = 4;
    pub const GRAN_DIOS: i32 = 8;
    pub const DIRECTOR: i32 = 9;
    pub const DEVELOPER: i32 = 10;
    pub const SUB_ADMINISTRADOR: i32 = 11;
    pub const ADMINISTRADOR: i32 = 12;
}

/// Anti-cheat interval settings loaded from Intervalos.ini.
/// Values are in game ticks (1 tick = 40ms).
#[derive(Debug, Clone)]
pub struct IntervalSettings {
    pub golpe: i32,           // Melee attack interval (default 37)
    pub flechas: i32,         // Arrow shot interval (default 28)
    pub lanzar_hechizo: i32,  // Spell cast interval (default 13)
    pub poteo_u: i32,         // Potion use interval (default 8)
    pub poteo_click: i32,     // Click action interval (default 6)
    pub work: i32,            // Work/skill interval (default 10)
}

impl Default for IntervalSettings {
    fn default() -> Self {
        Self {
            golpe: 37,
            flechas: 28,
            lanzar_hechizo: 13,
            poteo_u: 8,
            poteo_click: 6,
            work: 10,
        }
    }
}

/// World cleanup — tracks dropped items for auto-removal.
pub const MAX_OBJS_CLEAR: usize = 4000;

#[derive(Debug, Clone, Default)]
pub struct CleanWorldEntry {
    pub map: i32,
    pub x: i32,
    pub y: i32,
    pub tiempo: i32,      // Countdown ticks until removal
    pub obj_index: i32,
}

/// Global server state — holds all shared data.
pub struct GameState {
    pub config: ServerConfig,
    pub base_path: PathBuf,
    pub pool: PgPool,
    pub bans: BanList,
    pub notice: String,

    // Game data (objects, spells, NPCs, maps, experience)
    pub game_data: GameData,

    // Runtime world state (map grids, char indices)
    pub world: WorldState,

    // Online tracking
    pub users: HashMap<ConnectionId, UserState>,
    pub writers: HashMap<ConnectionId, ConnectionWriter>,

    // Name → ConnectionId for online characters (multi-login prevention)
    pub online_names: HashMap<String, ConnectionId>,

    // NPC runtime instances (index 0 is unused — NPC indices start at 1)
    pub npcs: Vec<Option<NpcState>>,
    pub next_npc_index: NpcIndex,

    // Party system (runtime only, not persisted)
    pub parties: Vec<Option<PartyState>>,
    pub next_party_index: i32,

    // Counters
    pub num_users: u32,
    pub record_users: u32,

    // Security code for new accounts (global CodeX)
    pub security_code: String,

    // GM-only mode (when enabled, only GMs can log in)
    pub server_solo_gms: bool,

    // Arena duels (4 arenas on map 71)
    pub arena_ocupada: [bool; 5],         // 1-indexed (0 unused)
    pub tiempo_duelo: [i32; 5],           // Timer per arena (minutes)
    pub nombre_dueleando: [String; 9],    // 1-indexed, 2 per arena (1-2, 3-4, 5-6, 7-8)

    // Desafio (1v1 king-of-the-hill on map 109)
    pub desafio_primero: ConnectionId,    // The defender
    pub desafio_segundo: ConnectionId,    // The challenger

    // CvC state
    pub cvc_funciona: bool,
    pub cvc_clan1_count: i32,
    pub cvc_clan2_count: i32,
    pub cvc_nombre1: String,           // Acceptor clan name (blue team)
    pub cvc_nombre2: String,           // Challenger clan name (red team)
    pub cvc_guild1: i32,               // Acceptor guild index (blue)
    pub cvc_guild2: i32,               // Challenger guild index (red)
    pub cvc_pending_target_guild: i32,  // Guild being challenged (pending acceptance)
    pub cvc_pending_challenger_guild: i32, // Guild that sent the challenge
    pub cvc_pending_challenger_name: String, // Challenger clan name (pending)

    // Arena spectators
    pub espectadores_arena1: i32,
    pub espectadores_arena2: i32,
    pub espectadores_arena3: i32,
    pub espectadores_arena4: i32,

    // 2vs2 Pareja system
    pub pareja: [ConnectionId; 5], // 1-indexed (0 unused), slots 1-4

    // Tournament
    pub hay_torneo: bool,
    pub usuarios_en_torneo: i32,
    pub cronologia_participantes: Vec<String>, // Up to 64 participants

    // Auto-save counter (decrements in tick_player_passive, triggers save at 0)
    pub auto_save_counter: i32,

    // Anti-cheat intervals (loaded from Intervalos.ini)
    pub intervals: IntervalSettings,

    // Ranking system (loaded from Ranking.dat)
    pub ranking: RankingData,

    // World cleanup (tracks dropped items for auto-removal)
    pub clean_world: Vec<CleanWorldEntry>,

    // Nobility quest state (modNobleza.bas)
    pub nobility_user: ConnectionId,   // User doing the quest (0 = none)
    pub nobility_stage: i32,           // Current stage (1-3, 0 = inactive)
    pub nobility_timer: i32,           // Countdown ticks
    pub nobility_kills: i32,           // Kills in current stage

    // IP security (SecurityIp.bas) — rate limiting + max connections per IP
    pub ip_last_connect: HashMap<String, std::time::Instant>, // Last connect time per IP
    pub ip_connection_count: HashMap<String, u32>,            // Active connections per IP
    pub ip_max_connections: u32,                               // Max connections per IP (default 10)
    pub ip_min_interval_ms: u64,                               // Min ms between connections (default 500)

    // War system (frmMain.frm / TCP_HandleData2.bas)
    pub hay_guerra: bool,
    pub hay_guerra_anvil: bool,         // War at Anvilmar (map 29)
    pub hay_guerra_khalim: bool,        // War at Khalimdar (map 27)
    pub rey_guerra_index: usize,        // NPC runtime index of war king
    pub guerra_minutes: i32,            // Minute counter for war timer (VB6 Minus)
    pub guerra_seconds: i32,            // Second counter (0-59, increments to minutes)
    pub chat_global: bool,              // Global chat enabled (toggled by /NOGLOBAL)

    // Treasure system (modTesoros.bas)
    pub tesoro_map: i32,
    pub tesoro_x: i32,
    pub tesoro_y: i32,
    pub tesoro_contando: bool,
    pub tesoro_tiempo: i32,             // Countdown ticks
    pub se_puede_desenterrar: bool,

    // Castle siege state (modSiege.bas)
    pub siege_active: bool,
    pub siege_guild_owner: i32,           // Guild index that owns the castle
    pub siege_guild_attacker: i32,        // Guild index attacking
    pub siege_conquest: [i32; 4],         // 3 conquest points (1-indexed), guild_index or 0
    pub siege_timer: i32,                 // Countdown ticks
    pub siege_map: i32,                   // Map 151

    // Praetorian system (praetorians.bas)
    pub pretoriano_clan: Vec<usize>,      // NPC runtime indices in praetorian clan (up to 8)
    pub pretoriano_activo: bool,
    pub pretoriano_faccion: i32,          // 1=real, 2=caos
    pub pretoriano_alcoba: i32,           // Current alcoba state (0-4)

    // Event system — generic event state
    pub evento_activo: bool,
    pub evento_tipo: i32,                 // 0=none, 1=CTF, 2=JDH, 3=LUZ, 4=ARAM, 5=BatMistica, 6=Faccionario, 7=TorneoAuto, 8=Guerra
    pub evento_participantes: Vec<ConnectionId>,
    pub evento_timer: i32,                // Event-specific timer (seconds)
    pub evento_inscripciones: bool,       // Signup window open
    pub evento_max_players: i32,
    pub evento_costo: i64,               // Entry cost in gold
    pub evento_map: i32,                  // Event map
    pub evento_countdown: i32,            // Pre-start countdown (seconds)

    // CTF-specific
    pub ctf_puntos_azul: i32,
    pub ctf_puntos_rojo: i32,

    // ARAM-specific
    pub aram_torre_azul: usize,           // NPC runtime index of blue tower
    pub aram_torre_roja: usize,           // NPC runtime index of red tower

    // Batalla Mistica — 4-team kills
    pub bat_kills: [i32; 5],              // 1-indexed, team kills

    // Multipliers (set by GM commands)
    pub multiplicador_exp: i32,    // /EXP multiplier (default from config)
    pub multiplicador_oro: i32,    // /GLD multiplier
    pub multiplicador_drop: i32,   // /DROP multiplier

    // Automatic broadcast message (/LMSG)
    pub auto_msg_active: bool,
    pub auto_msg_text: String,
    pub auto_msg_interval: i32,    // Minutes between broadcasts
    pub auto_msg_counter: i32,     // Seconds counter

    // SOS/Consultation system
    pub sos_messages: Vec<SosMessage>,

    // Poll/Voting system
    pub poll_active: bool,
    pub poll_options: [String; 5],
    pub poll_votes: [i32; 5],
    pub poll_voters: Vec<String>,  // Names who already voted

    // Automatic tournament
    pub torneo_auto_activo: bool,
    pub torneo_auto_rondas: i32,          // Total rounds
    pub torneo_auto_ronda_actual: i32,    // Current round
    pub torneo_auto_bracket: Vec<ConnectionId>, // Players in bracket (2^N)
    pub torneo_auto_timer: i32,           // Countdown timer

    // Ancalagon boss system (VB6: modDragon.bas)
    pub ancalagon_alive: bool,            // Is the dragon (NPC 936) currently alive?
    pub ancalagon_guardians: i32,         // Number of guardian NPCs (938) still alive
    pub ancalagon_pre_dragon: bool,       // Is the pre-dragon (937) spawned?
    pub ancalagon_pre_dragon_idx: NpcIndex, // Runtime index of pre-dragon NPC (for aura removal)
    pub ancalagon_minutes: i32,           // Minutes since dragon death (counts to 60)
    pub ancalagon_seconds: i32,           // Seconds counter (0-59)

    // Global NPC attack timer (VB6: CanAttackNpc counter — every 3 AI ticks)
    pub npc_can_attack_counter: i32,

    // Map user counts cache (map_number → user count, for skipping empty maps)
    pub map_user_counts: HashMap<i32, u32>,

    // Auction system (VB6: modSubastas)
    pub auction: Option<AuctionState>,

    // Gran Poder system (VB6: modGranPoder)
    pub gran_poder_holder: ConnectionId,  // 0 = nobody has it

    // Countdown system (VB6: /CONT)
    pub countdown_seconds: i32,           // 0 = inactive

    // Role overrides from server.ini (VB6: EsAdministrador, EsDios, etc.)
    // Maps lowercase character name → privilege level. Loaded at startup, reloaded with /RELOADSINI.
    pub role_overrides: crate::config::RoleMap,

    /// Per-connection receive buffers for accumulating partial binary packets.
    /// When a TCP read delivers a partial packet, leftover bytes are stored here
    /// and prepended to the next read.
    pub recv_buffers: HashMap<ConnectionId, Vec<u8>>,
}

/// SOS message (help request from player)
#[derive(Debug, Clone)]
pub struct SosMessage {
    pub tipo: String,
    pub autor: String,
    pub contenido: String,
}

/// Auction state (VB6: modSubastas — one global auction at a time)
#[derive(Debug, Clone)]
pub struct AuctionState {
    pub auctioneer: ConnectionId,  // Who started the auction
    pub obj_index: i32,            // Object being auctioned
    pub amount: i32,               // Quantity
    pub min_gold: i64,             // Minimum bid
    pub current_bid: i64,          // Current highest bid (0 = no bids yet)
    pub bidder: ConnectionId,      // Current highest bidder (0 = none)
    pub bidder_name: String,       // Name of highest bidder
    pub timer: i32,                // Seconds remaining (240 = 4 min)
}

/// Party runtime state (matches VB6 tParty)
pub const MAX_PARTIES: usize = 1000;
pub const MAX_PARTY_MEMBERS: usize = 10;

#[derive(Debug, Clone)]
pub struct PartyState {
    pub leader: ConnectionId,
    pub members: Vec<ConnectionId>, // Up to 10 members (including leader)
}

impl GameState {
    pub fn new(config: ServerConfig, base_path: PathBuf, game_data: GameData, pool: PgPool, bans: BanList, ranking: RankingData) -> Self {
        let notice = config.notice.clone();
        let exp_mult = config.exp_multiplier as i32;
        let security_code = format!("{}", rand_simple());

        // Load anti-cheat intervals
        let intervals = load_intervals(&base_path);

        // Load role overrides from server.ini
        let role_overrides = crate::config::load_roles(&base_path);

        // Count loaded maps to pre-allocate world grids
        let map_count = game_data.maps.len();

        Self {
            config,
            base_path,
            pool,
            bans,
            notice,
            game_data,
            world: WorldState::new(map_count),
            users: HashMap::new(),
            writers: HashMap::new(),
            online_names: HashMap::new(),
            npcs: vec![None; 10000], // Pre-allocate (VB6: MAXNPCS)
            next_npc_index: 1,
            parties: vec![None; MAX_PARTIES + 1], // 1-indexed
            next_party_index: 1,
            num_users: 0,
            record_users: 0,
            security_code,
            server_solo_gms: false,
            arena_ocupada: [false; 5],
            tiempo_duelo: [0; 5],
            nombre_dueleando: Default::default(),
            desafio_primero: 0,
            desafio_segundo: 0,
            cvc_funciona: false,
            cvc_clan1_count: 0,
            cvc_clan2_count: 0,
            cvc_nombre1: String::new(),
            cvc_nombre2: String::new(),
            cvc_guild1: 0,
            cvc_guild2: 0,
            cvc_pending_target_guild: 0,
            cvc_pending_challenger_guild: 0,
            cvc_pending_challenger_name: String::new(),
            espectadores_arena1: 0,
            espectadores_arena2: 0,
            espectadores_arena3: 0,
            espectadores_arena4: 0,
            pareja: [0; 5],
            hay_torneo: false,
            usuarios_en_torneo: 0,
            cronologia_participantes: Vec::new(),
            auto_save_counter: 60, // Save every 60 ticks (~60 seconds)
            intervals,
            ranking,
            clean_world: vec![CleanWorldEntry::default(); MAX_OBJS_CLEAR],
            nobility_user: 0,
            nobility_stage: 0,
            nobility_timer: 0,
            nobility_kills: 0,
            ip_last_connect: HashMap::new(),
            ip_connection_count: HashMap::new(),
            ip_max_connections: 10,
            ip_min_interval_ms: 500,
            hay_guerra: false,
            hay_guerra_anvil: false,
            hay_guerra_khalim: false,
            rey_guerra_index: 0,
            guerra_minutes: 0,
            guerra_seconds: 0,
            chat_global: true,
            tesoro_map: 0,
            tesoro_x: 0,
            tesoro_y: 0,
            tesoro_contando: false,
            tesoro_tiempo: 0,
            se_puede_desenterrar: false,
            siege_active: false,
            siege_guild_owner: 0,
            siege_guild_attacker: 0,
            siege_conquest: [0; 4],
            siege_timer: 0,
            siege_map: 151,
            pretoriano_clan: Vec::new(),
            pretoriano_activo: false,
            pretoriano_faccion: 0,
            pretoriano_alcoba: 0,
            evento_activo: false,
            evento_tipo: 0,
            evento_participantes: Vec::new(),
            evento_timer: 0,
            evento_inscripciones: false,
            evento_max_players: 10,
            evento_costo: 0,
            evento_map: 0,
            evento_countdown: 0,
            ctf_puntos_azul: 0,
            ctf_puntos_rojo: 0,
            aram_torre_azul: 0,
            aram_torre_roja: 0,
            bat_kills: [0; 5],
            multiplicador_exp: exp_mult,
            multiplicador_oro: 1,
            multiplicador_drop: 1,
            auto_msg_active: false,
            auto_msg_text: String::new(),
            auto_msg_interval: 0,
            auto_msg_counter: 0,
            sos_messages: Vec::new(),
            poll_active: false,
            poll_options: Default::default(),
            poll_votes: [0; 5],
            poll_voters: Vec::new(),
            torneo_auto_activo: false,
            torneo_auto_rondas: 0,
            torneo_auto_ronda_actual: 0,
            torneo_auto_bracket: Vec::new(),
            torneo_auto_timer: 0,
            ancalagon_alive: false,
            ancalagon_guardians: 0,
            ancalagon_pre_dragon: false,
            ancalagon_pre_dragon_idx: 0,
            ancalagon_minutes: 0,
            ancalagon_seconds: 0,
            npc_can_attack_counter: 0,
            map_user_counts: HashMap::new(),
            auction: None,
            gran_poder_holder: 0,
            countdown_seconds: 0,
            role_overrides,
            recv_buffers: HashMap::new(),
        }
    }

    /// Register a new connection.
    pub fn add_connection(&mut self, writer: ConnectionWriter) {
        let id = writer.id;
        let ip = writer.ip();
        self.users.insert(id, UserState::new(id, ip));
        self.writers.insert(id, writer);
    }

    /// Remove a connection and clean up world state.
    pub fn remove_connection(&mut self, conn_id: ConnectionId) {
        if let Some(user) = self.users.remove(&conn_id) {
            // Decrement IP connection count (SecurityIp.bas)
            if let Some(count) = self.ip_connection_count.get_mut(&user.ip) {
                *count = count.saturating_sub(1);
            }

            if user.logged {
                self.num_users = self.num_users.saturating_sub(1);
                if !user.char_name.is_empty() {
                    self.online_names.remove(&user.char_name.to_uppercase());
                }
                // Remove from world grid
                if user.pos_map > 0 {
                    self.world.remove_user(user.pos_map, user.pos_x, user.pos_y);
                }
            }
        }
        self.writers.remove(&conn_id);
        self.recv_buffers.remove(&conn_id);
    }

    /// Check if a character name is currently online.
    pub fn is_name_online(&self, name: &str) -> bool {
        self.online_names.contains_key(&name.to_uppercase())
    }

    /// Check if any other character from the same account is online.
    /// Takes the list of character names from the already-loaded account.
    pub fn is_account_char_online(&self, characters: &[String], exclude_name: &str) -> Option<String> {
        for pj in characters {
            if !pj.is_empty()
                && pj.to_uppercase() != exclude_name.to_uppercase()
                && self.is_name_online(pj)
            {
                return Some(pj.clone());
            }
        }
        None
    }

    /// Send raw binary bytes to a specific connection (13.3 protocol, no encryption).
    pub async fn send_bytes(&mut self, conn_id: ConnectionId, data: &[u8]) {
        if let Some(writer) = self.writers.get_mut(&conn_id) {
            if let Err(e) = writer.send_packet(data).await {
                tracing::warn!("Failed to send to #{}: {}", conn_id, e);
            }
        }
    }

    /// Send binary data using routing target (matches VB6 SendData).
    pub async fn send_data_bytes(&mut self, target: SendTarget, data: &[u8]) {
        match target {
            SendTarget::ToIndex(conn_id) => {
                self.send_bytes(conn_id, data).await;
            }
            SendTarget::ToAll => {
                let ids: Vec<ConnectionId> = self.users.values()
                    .filter(|u| u.logged)
                    .map(|u| u.conn_id)
                    .collect();
                for id in ids {
                    self.send_bytes(id, data).await;
                }
            }
            SendTarget::ToMap(map) => {
                let ids: Vec<ConnectionId> = self.users.values()
                    .filter(|u| u.logged && u.pos_map == map)
                    .map(|u| u.conn_id)
                    .collect();
                for id in ids {
                    self.send_bytes(id, data).await;
                }
            }
            SendTarget::ToArea { map, x, y } => {
                let ids = if let Some(grid) = self.world.grid(map) {
                    world::get_users_in_area(grid, x, y)
                } else {
                    Vec::new()
                };
                for id in ids {
                    self.send_bytes(id, data).await;
                }
            }
            SendTarget::ToAreaButIndex { conn_id, map, x, y } => {
                let ids = if let Some(grid) = self.world.grid(map) {
                    world::get_users_in_area(grid, x, y)
                } else {
                    Vec::new()
                };
                for id in ids {
                    if id != conn_id {
                        self.send_bytes(id, data).await;
                    }
                }
            }
            SendTarget::ToMapButIndex { conn_id, map } => {
                let ids: Vec<ConnectionId> = self.users.values()
                    .filter(|u| u.logged && u.pos_map == map && u.conn_id != conn_id)
                    .map(|u| u.conn_id)
                    .collect();
                for id in ids {
                    self.send_bytes(id, data).await;
                }
            }
            SendTarget::ToGuildMembers(guild_index) => {
                if guild_index > 0 {
                    let ids: Vec<ConnectionId> = self.users.values()
                        .filter(|u| u.logged && u.guild_index == guild_index)
                        .map(|u| u.conn_id)
                        .collect();
                    for id in ids {
                        self.send_bytes(id, data).await;
                    }
                }
            }
            SendTarget::ToAdmins => {
                let ids: Vec<ConnectionId> = self.users.values()
                    .filter(|u| u.logged && u.privileges > privilege_level::USER)
                    .map(|u| u.conn_id)
                    .collect();
                for id in ids {
                    self.send_bytes(id, data).await;
                }
            }
        }
    }

    /// Send binary bytes and then close the connection.
    pub async fn send_bytes_and_close(&mut self, conn_id: ConnectionId, data: &[u8]) {
        self.send_bytes(conn_id, data).await;
        if let Some(mut writer) = self.writers.remove(&conn_id) {
            writer.shutdown().await;
        }
    }

    // ── Binary packet send helpers ─────────────────────────────

    /// Send a console message by text ID (Textos.tsao lookup).
    pub async fn send_msg_id(&mut self, conn_id: ConnectionId, msg_id: i16, args: &str) {
        let pkt = crate::protocol::binary_packets::write_console_msg_id(msg_id, args);
        self.send_bytes(conn_id, &pkt).await;
    }

    /// Send an inline console message with font index.
    pub async fn send_console(&mut self, conn_id: ConnectionId, msg: &str, font: u8) {
        let pkt = crate::protocol::binary_packets::write_console_msg(msg, font);
        self.send_bytes(conn_id, &pkt).await;
    }

    /// Send a console message by text ID using routing target.
    pub async fn send_msg_id_to(&mut self, target: SendTarget, msg_id: i16, args: &str) {
        let pkt = crate::protocol::binary_packets::write_console_msg_id(msg_id, args);
        self.send_data_bytes(target, &pkt).await;
    }

    /// Send an inline console message with font index using routing target.
    pub async fn send_console_to(&mut self, target: SendTarget, msg: &str, font: u8) {
        let pkt = crate::protocol::binary_packets::write_console_msg(msg, font);
        self.send_data_bytes(target, &pkt).await;
    }

    /// Send chat-over-head to a target group.
    pub async fn send_chat_over_head_to(&mut self, target: SendTarget, msg: &str, char_index: i16, color: i32) {
        let pkt = crate::protocol::binary_packets::write_chat_over_head(msg, char_index, color);
        self.send_data_bytes(target, &pkt).await;
    }

    /// Send area talk chat.
    pub async fn send_chat_talk_to(&mut self, target: SendTarget, char_index: i16, msg: &str, color: i32) {
        let pkt = crate::protocol::binary_packets::write_chat_talk(char_index, msg, color);
        self.send_data_bytes(target, &pkt).await;
    }

    /// Send GM broadcast to target.
    pub async fn send_gm_broadcast_to(&mut self, target: SendTarget, msg: &str) {
        let pkt = crate::protocol::binary_packets::write_gm_broadcast(msg);
        self.send_data_bytes(target, &pkt).await;
    }

    /// Send guild chat to target.
    pub async fn send_guild_chat_to(&mut self, target: SendTarget, msg: &str) {
        let pkt = crate::protocol::binary_packets::write_guild_chat(msg);
        self.send_data_bytes(target, &pkt).await;
    }

    /// Send whisper to a specific connection.
    pub async fn send_whisper(&mut self, conn_id: ConnectionId, msg: &str, font: u8) {
        let pkt = crate::protocol::binary_packets::write_chat_whisper(msg, font);
        self.send_bytes(conn_id, &pkt).await;
    }

    /// Check if a tile is blocked (from static map data).
    pub fn is_tile_blocked(&self, map: i32, x: i32, y: i32) -> bool {
        let map_idx = map as usize;
        if let Some(Some(game_map)) = self.game_data.maps.get(map_idx) {
            if x >= 1 && x <= 100 && y >= 1 && y <= 100 {
                game_map.tiles[(y - 1) as usize][(x - 1) as usize].blocked
            } else {
                true
            }
        } else {
            true // Unknown map = blocked
        }
    }

    /// Check if a tile has water (VB6 HayAgua: Graphic(1) in 1505..1520 AND Graphic(2) = 0).
    pub fn hay_agua(&self, map: i32, x: i32, y: i32) -> bool {
        let map_idx = map as usize;
        if let Some(Some(game_map)) = self.game_data.maps.get(map_idx) {
            if x >= 1 && x <= 100 && y >= 1 && y <= 100 {
                let tile = &game_map.tiles[(y - 1) as usize][(x - 1) as usize];
                tile.graphic[0] >= 1505 && tile.graphic[0] <= 1520 && tile.graphic[1] == 0
            } else {
                false
            }
        } else {
            false
        }
    }

    /// Look up guild name by guild index (1-based). Blocking call for sync context.
    /// For async context, use db::guilds::load_guild() directly.
    pub fn get_guild_name(&self, guild_index: i32) -> Option<String> {
        if guild_index <= 0 {
            return None;
        }
        // Use a blocking call to avoid requiring async here.
        // This is only used in sync formatting contexts.
        let pool = self.pool.clone();
        let rt = tokio::runtime::Handle::current();
        rt.block_on(async {
            crate::db::guilds::load_guild(&pool, guild_index).await.map(|g| g.name)
        })
    }

    pub fn get_object(&self, obj_index: i32) -> Option<&crate::data::objects::ObjData> {
        if obj_index >= 1 {
            self.game_data.objects.get((obj_index - 1) as usize)
        } else {
            None
        }
    }

    /// Look up a spell by index (1-based).
    pub fn get_spell(&self, spell_index: i32) -> Option<&crate::data::spells::SpellData> {
        if spell_index >= 1 {
            self.game_data.spells.get((spell_index - 1) as usize)
        } else {
            None
        }
    }

    /// Get experience needed for next level.
    pub fn exp_for_level(&self, level: i32) -> i64 {
        if level >= 1 {
            self.game_data.experience.get((level - 1) as usize).copied().unwrap_or(0)
        } else {
            0
        }
    }

    /// Find a user by character name (case-insensitive).
    pub fn find_user_by_name(&self, name: &str) -> Option<ConnectionId> {
        self.online_names.get(&name.to_uppercase()).copied()
    }

    /// Check if a tile has a map exit (teleport).
    pub fn get_tile_exit(&self, map: i32, x: i32, y: i32) -> Option<(i32, i32, i32)> {
        let map_idx = map as usize;
        if let Some(Some(game_map)) = self.game_data.maps.get(map_idx) {
            if x >= 1 && x <= 100 && y >= 1 && y <= 100 {
                let tile = &game_map.tiles[(y - 1) as usize][(x - 1) as usize];
                if let Some(ref exit) = tile.tile_exit {
                    return Some((exit.map as i32, exit.x as i32, exit.y as i32));
                }
            }
        }
        None
    }

    // =================================================================
    // NPC methods
    // =================================================================

    /// Spawn a single NPC from database at a specific position.
    /// Returns the NPC runtime index, or None if the NPC number doesn't exist.
    pub fn spawn_npc(&mut self, npc_number: usize, map: i32, x: i32, y: i32) -> Option<NpcIndex> {
        let data = self.game_data.npcs.get(npc_number)?.clone();
        let npc_idx = self.next_npc_index;

        // Ensure capacity
        if npc_idx >= self.npcs.len() {
            self.npcs.resize(npc_idx + 100, None);
        }

        let char_index = self.world.alloc_char_index();
        let mut npc = NpcState::from_data(npc_idx, &data, char_index, map, x, y);

        // Initialize NPC area tracking (VB6: ArgegarNpc + CheckUpdateNeededNpc(USER_NUEVO))
        npc.area_id = (x / 9 + 1) * (y / 9 + 1);
        npc.area_min_x = (x / 9 - 1) * 9;
        npc.area_min_y = (y / 9 - 1) * 9;

        // Place on world grid
        let grid = self.world.grid_mut(map);
        if let Some(tile) = grid.tile_mut(x, y) {
            tile.npc_index = npc_idx as i32;
        }

        self.npcs[npc_idx] = Some(npc);
        self.next_npc_index += 1;

        Some(npc_idx)
    }

    /// Spawn all NPCs from map tile data (called on server startup).
    pub fn spawn_map_npcs(&mut self) -> usize {
        let mut count = 0;
        // Collect NPC spawn positions from map data
        let mut spawns: Vec<(usize, i32, i32, i32)> = Vec::new();
        for (map_idx, maybe_map) in self.game_data.maps.iter().enumerate() {
            if let Some(game_map) = maybe_map {
                for y in 0..100 {
                    for x in 0..100 {
                        let npc_idx = game_map.tiles[y][x].npc_index;
                        if npc_idx > 0 {
                            spawns.push((npc_idx as usize, map_idx as i32, (x + 1) as i32, (y + 1) as i32));
                        }
                    }
                }
            }
        }

        // Now spawn them
        for (npc_number, map, x, y) in spawns {
            if self.spawn_npc(npc_number, map, x, y).is_some() {
                count += 1;
            }
        }
        count
    }

    /// Load static map objects (doors, items, etc.) from .inf data into the WorldState grid.
    /// VB6 loads these at startup: each tile's OBJInfo becomes a ground item on the runtime grid.
    pub fn load_map_objects(&mut self) -> usize {
        let mut count = 0;
        for (map_idx, maybe_map) in self.game_data.maps.iter().enumerate() {
            if let Some(game_map) = maybe_map {
                let map_num = map_idx as i32;
                for y in 0..100usize {
                    for x in 0..100usize {
                        let obj = &game_map.tiles[y][x].obj;
                        if obj.obj_index > 0 {
                            let tile_x = (x + 1) as i32;
                            let tile_y = (y + 1) as i32;
                            let grid = self.world.grid_mut(map_num);
                            if let Some(tile) = grid.tile_mut(tile_x, tile_y) {
                                tile.ground_item.obj_index = obj.obj_index as i32;
                                tile.ground_item.amount = if obj.amount > 0 { obj.amount as i32 } else { 1 };
                            }
                            count += 1;
                        }
                    }
                }
            }
        }
        count
    }

    /// Get an NPC by runtime index.
    pub fn get_npc(&self, npc_idx: NpcIndex) -> Option<&NpcState> {
        self.npcs.get(npc_idx).and_then(|n| n.as_ref())
    }

    /// Get a mutable NPC by runtime index.
    pub fn get_npc_mut(&mut self, npc_idx: NpcIndex) -> Option<&mut NpcState> {
        self.npcs.get_mut(npc_idx).and_then(|n| n.as_mut())
    }

    /// Get the guild that owns a castle on the given map.
    /// VB6: CastilloNorte/Sur/Este/Oeste/Fortaleza correspond to different maps.
    /// Currently uses single siege_guild_owner for the siege map.
    pub fn get_castle_owner_guild(&self, map: i32) -> Option<i32> {
        // Only the siege map has an owner
        if map == self.siege_map && self.siege_guild_owner > 0 {
            Some(self.siege_guild_owner)
        } else {
            None
        }
    }

    /// Remove an NPC from the world (death). Does NOT deallocate — for respawn.
    pub fn kill_npc(&mut self, npc_idx: NpcIndex) {
        if let Some(npc) = self.npcs.get_mut(npc_idx).and_then(|n| n.as_mut()) {
            npc.active = false;
            npc.min_hp = 0;
            // Remove from grid
            let grid = self.world.grid_mut(npc.map);
            if let Some(tile) = grid.tile_mut(npc.x, npc.y) {
                if tile.npc_index == npc_idx as i32 {
                    tile.npc_index = 0;
                }
            }
        }
    }

    /// Respawn an NPC at its original position.
    pub fn respawn_npc(&mut self, npc_idx: NpcIndex) -> bool {
        let (orig_map, orig_x, orig_y, max_hp, npc_number) = match self.get_npc(npc_idx) {
            Some(npc) if !npc.active && npc.respawn => {
                (npc.orig_map, npc.orig_x, npc.orig_y, npc.max_hp, npc.npc_number)
            }
            _ => return false,
        };

        // Check if original tile is free
        let tile_free = self.world.grid(orig_map)
            .and_then(|g| g.tile(orig_x, orig_y))
            .map(|t| t.npc_index == 0 && t.user_conn.is_none())
            .unwrap_or(false);

        if !tile_free {
            return false;
        }

        if let Some(npc) = self.get_npc_mut(npc_idx) {
            npc.active = true;
            npc.min_hp = max_hp;
            npc.map = orig_map;
            npc.x = orig_x;
            npc.y = orig_y;
            npc.target = None;
            npc.can_attack = true;
            // Reset defense AI state
            npc.movement = npc.old_movement;
            npc.hostile = npc.old_hostile;
            npc.attacked_by.clear();
            npc.damage_received.clear();
            npc.pf_path.clear();
            npc.pf_step = 0;
        }

        // Place on grid
        let grid = self.world.grid_mut(orig_map);
        if let Some(tile) = grid.tile_mut(orig_x, orig_y) {
            tile.npc_index = npc_idx as i32;
        }

        true
    }

    /// Find the NPC on a specific tile (if any).
    pub fn npc_at_tile(&self, map: i32, x: i32, y: i32) -> Option<NpcIndex> {
        let grid = self.world.grid(map)?;
        let tile = grid.tile(x, y)?;
        if tile.npc_index > 0 {
            Some(tile.npc_index as NpcIndex)
        } else {
            None
        }
    }
}

/// Simple pseudo-random number (not crypto-secure, just for CodeX generation).
fn rand_simple() -> u32 {
    use std::time::{SystemTime, UNIX_EPOCH};
    let seed = SystemTime::now()
        .duration_since(UNIX_EPOCH)
        .unwrap_or_default()
        .as_nanos() as u32;
    seed.wrapping_mul(1103515245).wrapping_add(12345) % 1000
}

/// Load anti-cheat interval settings from dat/Intervalos.ini.
fn load_intervals(base: &std::path::Path) -> IntervalSettings {
    let path = base.join("dat").join("Intervalos.ini");
    match crate::config::IniFile::load(&path) {
        Ok(ini) => {
            let get = |key: &str, default: i32| -> i32 {
                ini.get("INTERVALOS", key)
                    .and_then(|s| s.parse().ok())
                    .unwrap_or(default)
            };
            let settings = IntervalSettings {
                golpe: get("Golpe", 37),
                flechas: get("Flechas", 28),
                lanzar_hechizo: get("LanzarHechizo", 13),
                poteo_u: get("PoteoU", 8),
                poteo_click: get("PoteoClick", 6),
                work: get("Work", 10),
            };
            tracing::info!(
                "Intervals loaded: golpe={}, flechas={}, hechizo={}, poteo={}, click={}, work={}",
                settings.golpe, settings.flechas, settings.lanzar_hechizo,
                settings.poteo_u, settings.poteo_click, settings.work
            );
            settings
        }
        Err(_) => {
            tracing::warn!("Intervalos.ini not found, using defaults");
            IntervalSettings::default()
        }
    }
}
