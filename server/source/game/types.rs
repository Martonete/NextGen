// Core game types — per-connection state and global server state.

use std::collections::{HashMap, HashSet};
use std::path::PathBuf;

use crate::config::ServerConfig;
use crate::db::bans::BanList;
use crate::data::GameData;
use sqlx::PgPool;
use crate::net::ConnectionId;
use crate::net::connection::ConnectionWriter;
use super::class_race::{PlayerClass, PlayerRace};
use super::world::{self, CharIndex, WorldState};
use super::npc::{NpcState, NpcIndex};

/// Maximum possible inventory slots (VB6 13.3: MAX_INVENTORY_SLOTS = 30 with backpack).
pub const MAX_INVENTORY_SLOTS: usize = 30;

/// Normal inventory slots without backpack (VB6 13.3: MAX_NORMAL_INVENTORY_SLOTS = 20).
pub const MAX_NORMAL_INVENTORY_SLOTS: usize = 20;

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
    pub class: PlayerClass,
    pub race: PlayerRace,
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
    pub exp_skills: [i32; 22],  // VB6: ExpSkills — current XP per skill
    pub elu_skills: [i32; 22],  // VB6: EluSkills — XP needed to level each skill
    pub skill_pts_libres: i32,  // Free skill points to distribute
    pub reputation: i32,

    // Inventory (30 max slots, 1-indexed in VB6 but 0-indexed here)
    pub inventory: Vec<InventorySlot>,
    pub equip: EquipSlots,
    /// Current inventory slot limit (VB6: CurrentInventorySlots). Default 20, expanded by backpack.
    pub current_inventory_slots: usize,
    /// Equipped backpack slot (VB6: MochilaEqpSlot, 1-indexed; 0 = none).
    pub backpack_slot: usize,

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
    pub cursed: bool,       // VB6: flags.Maldicion
    pub blessed: bool,      // VB6: flags.Bendicion
    pub stunned: bool,      // VB6: flags.Estupidez
    pub counter_stun: i32,  // Ticks remaining for stun
    pub blind: bool,        // VB6: flags.Ceguera
    pub counter_blind: i32, // Ticks remaining for blindness
    pub safe_toggle: bool,  // PvP safety (SEG)
    pub criminal: bool,
    pub navigating: bool,   // On a boat
    pub gender: i32,        // 1=Male, 2=Female (from charfile Genero)
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
    pub guild_name: String,     // Cached clan name (empty if no guild)
    pub guild_creating_alignment: i32, // Temp: alignment during guild creation flow
    pub seguro_clan: bool,      // Clan safe toggle — prevents attacking clanmates
    pub guild_bank_open: bool,  // Currently interacting with guild bank
    pub can_withdraw_items: bool, // Permission: withdraw items from guild bank
    pub can_withdraw_gold: bool,  // Permission: withdraw gold from guild bank

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
    pub target_obj: i32,                // VB6: flags.TargetObj (ObjIndex of last right-clicked obj)
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
    pub counter_hp_regen: i32, // HP regen counter (VB6: Sanar)
    pub counter_remo: i32,     // Remo potion cooldown (VB6: usoPotaRemo, 3 rounds)
    pub counter_frio: i32,     // VB6 Counters.Frio — cold damage counter (naked on snow/non-snow)
    pub counter_lava: i32,     // VB6 Counters.Lava — lava damage counter
    pub counter_mimetismo: i32, // VB6 Counters.Mimetismo — mimicry duration (counts up to IntervaloInvisible)
    pub resting: bool,         // VB6 flags.Descansar — resting (DOK) for faster HP/STA regen
    pub mimetizado: bool,      // VB6 flags.Mimetizado — disguised via Druid mimicry spell
    pub ignorado: bool,        // VB6 flags.Ignorado — ignored by NPCs (during mimicry)
    pub en_consulta: bool,     // VB6 flags.EnConsulta — being attended by GM, NPCs skip
    pub no_puede_ser_atacado: bool, // VB6 flags.NoPuedeSerAtacado — invulnerable to NPC aggro
    // Backup of original char appearance before mimicry
    pub char_mimetizado_body: i32,
    pub char_mimetizado_head: i32,
    pub char_mimetizado_weapon: i32,
    pub char_mimetizado_shield: i32,
    pub char_mimetizado_helmet: i32,

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
    pub gm_show_name: bool,       // GM name visible to players (VB6: /SHOWNAME toggle)
    pub old_body: i32,            // Saved body before going invisible
    pub old_head: i32,            // Saved head before going invisible
    pub emoticons: bool,          // Emoticons enabled (toggle via /EMOTICONS)
    pub silenced: bool,           // Muted by GM
    pub silence_timer: i32,       // Mute countdown in seconds (0 = permanent until toggled)
    pub jail_timer: i32,          // Jail countdown in seconds (0 = not jailed)
    pub warnings: i32,            // Warning count (advertencias)
    pub hogar: String,            // Home city (Thir, Inthak, Ruvendel, etc.)
    pub traveling: bool,           // VB6: Traveling — dead user teleporting home via /HOGAR
    pub counter_go_home: i32,      // VB6: GoHome counter — counts up to 250 ticks (10s at 40ms) then teleport

    // Navigation — barco_slot is the inventory slot (1-based) holding the equipped boat (VB6 BarcoSlot)
    pub barco_slot: usize,

    // SOS/Consultation system
    pub consulta_enviada: bool,    // Has pending consultation
    pub numero_consulta: i32,      // SOS message index

    // Macro detection
    pub tiene_macro: i32,          // Macro detection counter

    // Private messages toggle
    pub msj_privados: bool,        // Receive private messages

    // Montado (mounted)
    pub montado: bool,             // Is currently mounted
    pub montado_body: i32,         // Original body before mounting
    pub levitando: bool,           // Flying mount levitation

    // Description
    pub desc: String,                // User description (/DESC)

    // Marriage (VB6: Pareja)
    pub pareja: String,              // Name of spouse (empty = not married)

    // Duel system (VB6: AtacablePor)
    pub atacable_por: ConnectionId,  // 0 = no duel, >0 = can be attacked by this player only
    pub duel_pending: ConnectionId,  // Pending duel challenge from this player
    pub counter_atacable: i32,       // VB6: 60-second timeout for atacable_por (counts up to 1500 at 40ms/tick)
    pub warp_immunity_ticks: i32,    // Ticks after warp where NPCs won't target this user (prevents phantom sounds)

    // Timbero (gambling) stats
    pub timbero_target_npc: usize,   // Currently interacting with gambler NPC

    // Council membership (VB6: PlayerType.RoyalCouncil / ChaosCouncil)
    pub royal_council: bool,         // Member of Royal Army council
    pub chaos_council: bool,         // Member of Chaos Legion council

    // ShareNpc (VB6: flags.ShareNpcWith)
    pub share_npc_with: ConnectionId, // 0 = not sharing, >0 = sharing pets with this user

    // Centinela anti-bot system
    pub centinela_number: i32,       // Number the player must type (0 = no active check)
    pub centinela_timer: i32,        // Ticks remaining to answer (0 = inactive)
    pub centinela_fails: i32,        // Number of failed attempts

    // SOS help request (/GM)
    pub gm_request_pending: bool,    // Has pending /GM request
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
            class: PlayerClass::default(),
            race: PlayerRace::default(),
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
            exp_skills: [0; 22],
            elu_skills: [0; 22],
            skill_pts_libres: 0,
            reputation: 0,
            inventory: (0..MAX_INVENTORY_SLOTS).map(|_| InventorySlot::default()).collect(),
            equip: EquipSlots::default(),
            current_inventory_slots: MAX_NORMAL_INVENTORY_SLOTS,
            backpack_slot: 0,
            spells: [0; MAX_SPELL_SLOTS],
            dead: false,
            hidden: false,
            paralyzed: false,
            immobilized: false,
            meditating: false,
            poisoned: false,
            invisible: false,
            cursed: false,
            blessed: false,
            stunned: false,
            counter_stun: 0,
            blind: false,
            counter_blind: 0,
            safe_toggle: true, // Safety ON by default
            criminal: false,
            navigating: false,
            gender: 1,
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
            guild_name: String::new(),
            guild_creating_alignment: 0,
            seguro_clan: true, // Default ON — safe from clanmate attacks
            guild_bank_open: false,
            can_withdraw_items: false,
            can_withdraw_gold: false,
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
            target_obj: 0,
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
            counter_hp_regen: 0,
            counter_remo: 0,
            counter_frio: 0,
            counter_lava: 0,
            counter_mimetismo: 0,
            resting: false,
            mimetizado: false,
            ignorado: false,
            en_consulta: false,
            no_puede_ser_atacado: false,
            char_mimetizado_body: 0,
            char_mimetizado_head: 0,
            char_mimetizado_weapon: 0,
            char_mimetizado_shield: 0,
            char_mimetizado_helmet: 0,
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
            gm_show_name: false,
            old_body: 0,
            old_head: 0,
            emoticons: false,
            silenced: false,
            silence_timer: 0,
            jail_timer: 0,
            warnings: 0,
            hogar: String::new(),
            traveling: false,
            counter_go_home: 0,
            barco_slot: 0,
            consulta_enviada: false,
            numero_consulta: 0,
            tiene_macro: 0,
            msj_privados: true,
            montado: false,
            montado_body: 0,
            levitando: false,
            desc: String::new(),
            pareja: String::new(),
            atacable_por: 0,
            duel_pending: 0,
            counter_atacable: 0,
            warp_immunity_ticks: 0,
            timbero_target_npc: 0,
            royal_council: false,
            chaos_council: false,
            share_npc_with: 0,
            centinela_number: 0,
            centinela_timer: 0,
            centinela_fails: 0,
            gm_request_pending: false,
        }
    }

    /// Build binary CC (CharacterCreate) packet for this user.
    /// VB6: Name includes clan tag as "Nick <ClanName>" when guild_index > 0.
    pub fn build_cc_binary(&self) -> Vec<u8> {
        let nick_color = if self.criminal { 2u8 } else { 1u8 };
        let display_name = if self.guild_index > 0 && !self.guild_name.is_empty() {
            format!("{} <{}>", self.char_name, self.guild_name)
        } else {
            self.char_name.clone()
        };
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
            &display_name,
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
/// VB6 reference (ms): Melee=1500, Arrows=1400, Spells=1400, Potions=1200, Work=700.
#[derive(Debug, Clone)]
pub struct IntervalSettings {
    pub golpe: i32,           // Melee attack interval (VB6: 1500ms → 38 ticks)
    pub flechas: i32,         // Arrow shot interval (VB6: 1400ms → 35 ticks)
    pub lanzar_hechizo: i32,  // Spell cast interval (VB6: 1400ms → 35 ticks)
    pub poteo_u: i32,         // Potion use interval (VB6: 1200ms → 30 ticks)
    pub poteo_click: i32,     // Click action interval (default 6)
    pub work: i32,            // Work/skill interval (VB6: 700ms → 18 ticks)
}

impl Default for IntervalSettings {
    fn default() -> Self {
        Self {
            golpe: 38,           // VB6: IntervaloUserPuedeAtacar = 1500ms / 40ms
            flechas: 35,         // VB6: IntervaloUserPuedeFlechas = 1400ms / 40ms
            lanzar_hechizo: 35,  // VB6: IntervaloUserPuedeLanzarHechizo = 1400ms / 40ms
            poteo_u: 30,         // VB6: IntervaloUserPuedePotear = 1200ms / 40ms
            poteo_click: 6,
            work: 18,            // VB6: IntervaloUserPuedeTrabajar = 700ms / 40ms
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
    /// Indices of active (alive) NPCs — avoids scanning all 10,000 slots every tick.
    pub active_npc_indices: HashSet<usize>,

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

    // Auto-save counter (decrements in tick_player_passive, triggers save at 0)
    pub auto_save_counter: i32,

    // Anti-cheat intervals (loaded from Intervalos.ini)
    pub intervals: IntervalSettings,

    // World cleanup (tracks dropped items for auto-removal)
    pub clean_world: Vec<CleanWorldEntry>,

    // IP security (SecurityIp.bas) — rate limiting + max connections per IP
    pub ip_last_connect: HashMap<String, std::time::Instant>, // Last connect time per IP
    pub ip_connection_count: HashMap<String, u32>,            // Active connections per IP
    pub ip_max_connections: u32,                               // Max connections per IP (default 10)
    pub ip_min_interval_ms: u64,                               // Min ms between connections (default 500)
    pub flood_strike_limit: u32,                               // Strikes before disconnect (default 3)

    pub chat_global: bool,              // Global chat enabled (toggled by /NOGLOBAL)

    // Timbero (gambling) stats — VB6: tAPuestas, persisted in apuestas.dat
    pub timbero_ganancias: i64,         // Total player losses (house winnings)
    pub timbero_perdidas: i64,          // Total player winnings (house losses)
    pub timbero_jugadas: i64,           // Total bets placed

    // Guild diplomacy — runtime cache of guild relations
    // Key: (guild_a, guild_b) where guild_a < guild_b → value: -1=war, 0=peace, 1=alliance
    pub guild_relations: HashMap<(i32, i32), i32>,
    // Pending guild proposals: (proposer_guild, target_guild) → proposal type (0=peace, 1=alliance)
    pub guild_proposals: HashMap<(i32, i32), i32>,

    // Praetorian system (praetorians.bas)
    pub pretoriano_clan: Vec<usize>,      // NPC runtime indices in praetorian clan (up to 8)
    pub pretoriano_activo: bool,
    pub pretoriano_faccion: i32,          // 1=real, 2=caos
    pub pretoriano_alcoba: i32,           // Current alcoba state (0-4)

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

    // Forum system (VB6: modForum.bas)
    pub forums: HashMap<String, ForumData>,

    // Global NPC attack timer (VB6: CanAttackNpc counter — every 3 AI ticks)
    pub npc_can_attack_counter: i32,

    // Map user counts cache (map_number → user count, for skipping empty maps)
    pub map_user_counts: HashMap<i32, u32>,

    // Auction system (VB6: modSubastas)
    // Countdown system (VB6: /CONT)
    pub countdown_seconds: i32,           // 0 = inactive

    // Role overrides from server.ini (VB6: EsAdministrador, EsDios, etc.)
    // Maps lowercase character name → privilege level. Loaded at startup, reloaded with /RELOADSINI.
    pub role_overrides: crate::config::RoleMap,

    /// Per-connection receive buffers for accumulating partial binary packets.
    /// When a TCP read delivers a partial packet, leftover bytes are stored here
    /// and prepended to the next read.
    pub recv_buffers: HashMap<ConnectionId, Vec<u8>>,

    // ── Security: per-connection packet rate limiting ──
    /// Packet count per connection in the current 1-second window.
    /// Reset every second by tick_security(). If a connection exceeds
    /// max_packets_per_second, further packets are silently dropped.
    pub packet_counts: HashMap<ConnectionId, u32>,
    /// Maximum packets per second per connection (default 60).
    /// Legitimate gameplay produces ~5-15 pkt/s. Flood attacks produce thousands.
    pub max_packets_per_second: u32,
    /// Connections flagged for disconnection by security checks.
    /// Drained in the main loop after event processing.
    pub security_kick_queue: Vec<ConnectionId>,
    /// Flood strike counter per connection. Incremented each second the
    /// connection exceeds the packet rate limit. At 3 strikes, disconnected.
    pub flood_strikes: HashMap<ConnectionId, u32>,

    // Rain system (VB6: Lloviendo)
    pub raining: bool,
    pub rain_counter: i32,  // Tick counter for rain STA drain (incremented each 40ms tick)

    // GM forced night mode (VB6: /NOCHE toggle)
    pub forced_night: bool,

    // Server shutdown/restart countdown (VB6: /APAGAR, /REINICIAR)
    pub shutdown_countdown: i32,     // Seconds remaining until shutdown (0 = inactive)
    pub shutdown_restart: bool,      // true = restart, false = shutdown
}

/// SOS message (help request from player)
#[derive(Debug, Clone)]
pub struct SosMessage {
    pub tipo: String,
    pub autor: String,
    pub contenido: String,
}

/// VB6: tPost — a single forum post (title + author + body).
#[derive(Debug, Clone)]
pub struct ForumPost {
    pub title: String,
    pub author: String,
    pub body: String,
}

/// VB6: tForo — a forum with up to 30 posts + 5 stickies.
pub const MAX_FORUM_POSTS: usize = 30;
pub const MAX_FORUM_STICKIES: usize = 5;

#[derive(Debug, Clone, Default)]
pub struct ForumData {
    pub posts: Vec<ForumPost>,     // Newest first, max 30
    pub stickies: Vec<ForumPost>,  // Newest first, max 5
}

/// Party runtime state (matches VB6 tParty)
pub const MAX_PARTIES: usize = 300; // VB6: MAX_PARTIES
pub const MAX_PARTY_MEMBERS: usize = 5; // VB6: PARTY_MAXMEMBERS
pub const PARTY_MAX_DISTANCE: i32 = 18; // VB6: PARTY_MAXDISTANCIA (exp share range)
pub const MAX_DISTANCE_INGRESO_PARTY: i32 = 2; // VB6: MAXDISTANCIAINGRESOPARTY

#[derive(Debug, Clone)]
pub struct PartyState {
    pub leader: ConnectionId,
    pub members: Vec<ConnectionId>, // Up to 5 members (including leader)
}

impl GameState {
    pub fn new(config: ServerConfig, base_path: PathBuf, game_data: GameData, pool: PgPool, bans: BanList) -> Self {
        let notice = config.notice.clone();
        let exp_mult = config.exp_multiplier as i32;
        let security_code = format!("{}", rand_simple());
        // Extract security config before `config` is moved into struct
        let ip_max_conn = config.ip_max_connections.unwrap_or(10);
        let ip_min_ms = config.ip_min_interval_ms.unwrap_or(500);
        let flood_strikes_limit = config.flood_strike_limit.unwrap_or(3);
        let max_pps = config.max_packets_per_second.unwrap_or(60);

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
            active_npc_indices: HashSet::new(),
            parties: vec![None; MAX_PARTIES + 1], // 1-indexed
            next_party_index: 1,
            num_users: 0,
            record_users: 0,
            security_code,
            server_solo_gms: false,
            auto_save_counter: 60, // Save every 60 ticks (~60 seconds)
            intervals,
            clean_world: vec![CleanWorldEntry::default(); MAX_OBJS_CLEAR],
            ip_last_connect: HashMap::new(),
            ip_connection_count: HashMap::new(),
            ip_max_connections: ip_max_conn,
            ip_min_interval_ms: ip_min_ms,
            flood_strike_limit: flood_strikes_limit,
            chat_global: true,
            timbero_ganancias: 0,
            timbero_perdidas: 0,
            timbero_jugadas: 0,
            guild_relations: HashMap::new(),
            guild_proposals: HashMap::new(),
            pretoriano_clan: Vec::new(),
            pretoriano_activo: false,
            pretoriano_faccion: 0,
            pretoriano_alcoba: 0,
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
            forums: HashMap::new(),
            npc_can_attack_counter: 0,
            map_user_counts: HashMap::new(),
            countdown_seconds: 0,
            role_overrides,
            recv_buffers: HashMap::new(),
            packet_counts: HashMap::new(),
            max_packets_per_second: max_pps,
            security_kick_queue: Vec::new(),
            flood_strikes: HashMap::new(),
            raining: false,
            rain_counter: 0,
            forced_night: false,
            shutdown_countdown: 0,
            shutdown_restart: false,
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
        // Cancel active trade before removing user (VB6: FinComerciarUsu on disconnect)
        let trade_partner = self.users.get(&conn_id).and_then(|u| {
            if u.trading { u.trade_partner } else { None }
        });
        if let Some(partner) = trade_partner {
            // Clear partner's trade state
            if let Some(p) = self.users.get_mut(&partner) {
                p.trading = false;
                p.trade_partner = None;
                p.trade_offered = false;
                p.trade_accepted = false;
                p.trade_gold = 0;
                p.trade_items.clear();
            }
        }

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
                // VB6: SalirDeParty on disconnect — remove from party
                if user.party_index > 0 {
                    let pi = user.party_index as usize;
                    let mut dissolve = false;
                    if let Some(Some(party)) = self.parties.get_mut(pi) {
                        if party.leader == conn_id {
                            // Leader disconnects → party dissolves (VB6 behavior)
                            dissolve = true;
                        } else {
                            party.members.retain(|&c| c != conn_id);
                        }
                    }
                    if dissolve {
                        // Clear all members' party_index
                        let members: Vec<ConnectionId> = self.parties.get(pi)
                            .and_then(|p| p.as_ref())
                            .map(|p| p.members.clone())
                            .unwrap_or_default();
                        for &m in &members {
                            if let Some(mu) = self.users.get_mut(&m) {
                                mu.party_index = 0;
                            }
                        }
                        self.parties[pi] = None;
                    }
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

    /// Buffer raw binary bytes for a specific connection (13.3 protocol, no encryption).
    /// Data is not sent immediately — call flush_all_writers() at end of tick.
    pub fn send_bytes(&mut self, conn_id: ConnectionId, data: &[u8]) {
        if let Some(writer) = self.writers.get_mut(&conn_id) {
            writer.send_packet(data);
        }
    }

    /// Buffer binary data using routing target (matches VB6 SendData).
    /// Data is not sent immediately — call flush_all_writers() at end of tick.
    pub fn send_data_bytes(&mut self, target: SendTarget, data: &[u8]) {
        match target {
            SendTarget::ToIndex(conn_id) => {
                self.send_bytes(conn_id, data);
            }
            SendTarget::ToAll => {
                let ids: Vec<ConnectionId> = self.users.values()
                    .filter(|u| u.logged)
                    .map(|u| u.conn_id)
                    .collect();
                for id in ids {
                    self.send_bytes(id, data);
                }
            }
            SendTarget::ToMap(map) => {
                let ids: Vec<ConnectionId> = self.users.values()
                    .filter(|u| u.logged && u.pos_map == map)
                    .map(|u| u.conn_id)
                    .collect();
                for id in ids {
                    self.send_bytes(id, data);
                }
            }
            SendTarget::ToArea { map, x, y } => {
                let ids = if let Some(grid) = self.world.grid(map) {
                    world::get_users_in_area(grid, x, y)
                } else {
                    Vec::new()
                };
                for id in ids {
                    self.send_bytes(id, data);
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
                        self.send_bytes(id, data);
                    }
                }
            }
            SendTarget::ToMapButIndex { conn_id, map } => {
                let ids: Vec<ConnectionId> = self.users.values()
                    .filter(|u| u.logged && u.pos_map == map && u.conn_id != conn_id)
                    .map(|u| u.conn_id)
                    .collect();
                for id in ids {
                    self.send_bytes(id, data);
                }
            }
            SendTarget::ToGuildMembers(guild_index) => {
                if guild_index > 0 {
                    let ids: Vec<ConnectionId> = self.users.values()
                        .filter(|u| u.logged && u.guild_index == guild_index)
                        .map(|u| u.conn_id)
                        .collect();
                    for id in ids {
                        self.send_bytes(id, data);
                    }
                }
            }
            SendTarget::ToAdmins => {
                let ids: Vec<ConnectionId> = self.users.values()
                    .filter(|u| u.logged && u.privileges > privilege_level::USER)
                    .map(|u| u.conn_id)
                    .collect();
                for id in ids {
                    self.send_bytes(id, data);
                }
            }
        }
    }

    /// Get all user connection IDs in the area around (x,y) on a map, excluding `exclude_id`.
    pub fn get_area_users(&self, map: i32, x: i32, y: i32, exclude_id: ConnectionId) -> Vec<ConnectionId> {
        if let Some(grid) = self.world.grid(map) {
            world::get_users_in_area(grid, x, y)
                .into_iter()
                .filter(|id| *id != exclude_id)
                .collect()
        } else {
            Vec::new()
        }
    }

    /// Buffer binary bytes, flush immediately, and then close the connection.
    pub async fn send_bytes_and_close(&mut self, conn_id: ConnectionId, data: &[u8]) {
        self.send_bytes(conn_id, data);
        if let Some(mut writer) = self.writers.remove(&conn_id) {
            writer.shutdown().await; // shutdown() flushes buffer first
        }
    }

    /// Flush all connection write buffers to TCP sockets.
    /// Called once at the end of each game loop iteration to batch writes.
    pub async fn flush_all_writers(&mut self) {
        let mut failed: Vec<ConnectionId> = Vec::new();
        for (id, writer) in self.writers.iter_mut() {
            if let Err(e) = writer.flush().await {
                tracing::warn!("Flush failed for #{}: {}", id, e);
                failed.push(*id);
            }
        }
        for id in failed {
            if let Some(mut writer) = self.writers.remove(&id) {
                writer.shutdown().await;
            }
        }
    }

    // ── Binary packet send helpers ─────────────────────────────

    /// Send a console message by text ID (Textos.ao lookup).
    pub fn send_msg_id(&mut self, conn_id: ConnectionId, msg_id: i16, args: &str) {
        let pkt = crate::protocol::binary_packets::write_console_msg_id(msg_id, args);
        self.send_bytes(conn_id, &pkt);
    }

    /// Send an inline console message with font index.
    pub fn send_console(&mut self, conn_id: ConnectionId, msg: &str, font: u8) {
        let pkt = crate::protocol::binary_packets::write_console_msg(msg, font);
        self.send_bytes(conn_id, &pkt);
    }

    /// Send a console message by text ID using routing target.
    pub fn send_msg_id_to(&mut self, target: SendTarget, msg_id: i16, args: &str) {
        let pkt = crate::protocol::binary_packets::write_console_msg_id(msg_id, args);
        self.send_data_bytes(target, &pkt);
    }

    /// Send an inline console message with font index using routing target.
    pub fn send_console_to(&mut self, target: SendTarget, msg: &str, font: u8) {
        let pkt = crate::protocol::binary_packets::write_console_msg(msg, font);
        self.send_data_bytes(target, &pkt);
    }

    /// Send chat-over-head to a target group.
    pub fn send_chat_over_head_to(&mut self, target: SendTarget, msg: &str, char_index: i16, color: i32) {
        let pkt = crate::protocol::binary_packets::write_chat_over_head(msg, char_index, color);
        self.send_data_bytes(target, &pkt);
    }

    /// Send area talk chat.
    pub fn send_chat_talk_to(&mut self, target: SendTarget, char_index: i16, msg: &str, color: i32) {
        let pkt = crate::protocol::binary_packets::write_chat_talk(char_index, msg, color);
        self.send_data_bytes(target, &pkt);
    }

    /// Send GM broadcast to target.
    pub fn send_gm_broadcast_to(&mut self, target: SendTarget, msg: &str) {
        let pkt = crate::protocol::binary_packets::write_gm_broadcast(msg);
        self.send_data_bytes(target, &pkt);
    }

    /// Send guild chat to target.
    pub fn send_guild_chat_to(&mut self, target: SendTarget, msg: &str) {
        let pkt = crate::protocol::binary_packets::write_guild_chat(msg);
        self.send_data_bytes(target, &pkt);
    }

    /// Send whisper to a specific connection.
    pub fn send_whisper(&mut self, conn_id: ConnectionId, msg: &str, font: u8) {
        let pkt = crate::protocol::binary_packets::write_chat_whisper(msg, font);
        self.send_bytes(conn_id, &pkt);
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

    /// Check if a tile has water (VB6 HayAgua: 3 graphic ranges AND Graphic(2) = 0).
    pub fn hay_agua(&self, map: i32, x: i32, y: i32) -> bool {
        let map_idx = map as usize;
        if let Some(Some(game_map)) = self.game_data.maps.get(map_idx) {
            if x >= 1 && x <= 100 && y >= 1 && y <= 100 {
                let tile = &game_map.tiles[(y - 1) as usize][(x - 1) as usize];
                let g = tile.graphic[0];
                let is_water = (g >= 1505 && g <= 1520)
                    || (g >= 5665 && g <= 5680)
                    || (g >= 13547 && g <= 13562);
                is_water && tile.graphic[1] == 0
            } else {
                false
            }
        } else {
            false
        }
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
        self.active_npc_indices.insert(npc_idx);
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
    /// (Siege system removed — always returns None.)
    pub fn get_castle_owner_guild(&self, _map: i32) -> Option<i32> {
        None
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
        self.active_npc_indices.remove(&npc_idx);
    }

    /// Respawn an NPC at its original position.
    pub fn respawn_npc(&mut self, npc_idx: NpcIndex) -> bool {
        let (orig_map, orig_x, orig_y, max_hp, _npc_number) = match self.get_npc(npc_idx) {
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

        self.active_npc_indices.insert(npc_idx);
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
                golpe: get("Golpe", 38),           // VB6: 1500ms / 40ms
                flechas: get("Flechas", 35),       // VB6: 1400ms / 40ms
                lanzar_hechizo: get("LanzarHechizo", 35), // VB6: 1400ms / 40ms
                poteo_u: get("PoteoU", 30),        // VB6: 1200ms / 40ms
                poteo_click: get("PoteoClick", 6),
                work: get("Work", 18),             // VB6: 700ms / 40ms
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
