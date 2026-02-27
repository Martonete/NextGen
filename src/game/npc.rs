// NPC runtime state and AI — manages live NPC instances in the game world.
//
// Each NPC spawned from map data gets a unique runtime index (NpcIndex)
// and a shared CharIndex (same pool as players) for client rendering.
//
// AI types (from VB6 TipoAI):
//   1 = Static (no movement)
//   2 = Random walk (1/12 chance per tick)
//   3 = Hostile chase (attack nearby players)
//   4 = Defense (follow attacker)
//   5 = Guard (attack criminals)
//   8 = Follow owner (pet)
//  10 = Pathfinding chase

use crate::data::npcs::{NpcData, NpcType};
use super::world::CharIndex;

/// Maximum NPC inventory slots (matches VB6 MAX_INVENTORY_SLOTS).
pub const MAX_NPC_INV_SLOTS: usize = 25;

/// AI movement type constants (match VB6 TipoAI enum).
pub const AI_STATIC: i32 = 1;
pub const AI_RANDOM: i32 = 2;
pub const AI_HOSTILE_CHASE: i32 = 3;
pub const AI_DEFENSE: i32 = 4;
pub const AI_GUARD: i32 = 5;
pub const AI_FOLLOW_OWNER: i32 = 8;
pub const AI_PATHFINDING: i32 = 10;

/// NPC vision range for aggro detection.
pub const NPC_VISION_X: i32 = 11;
pub const NPC_VISION_Y: i32 = 9;

/// Unique runtime index for a live NPC instance.
pub type NpcIndex = usize;

/// Runtime state for a single live NPC.
#[derive(Debug, Clone)]
pub struct NpcState {
    /// Runtime index in the global NPC list.
    pub index: NpcIndex,

    /// NPC number from the database (e.g., 500 = Murcielago).
    pub npc_number: usize,

    /// Character index shared with players (for CC/BP/MP packets).
    pub char_index: CharIndex,

    // Appearance
    pub body: i32,
    pub head: i32,
    pub heading: i32,
    pub weapon_anim: i32,
    pub shield_anim: i32,
    pub casco_anim: i32,

    // Position
    pub map: i32,
    pub x: i32,
    pub y: i32,

    // Original spawn position (for respawn)
    pub orig_map: i32,
    pub orig_x: i32,
    pub orig_y: i32,

    // AI
    pub movement: i32,
    pub hostile: bool,
    pub npc_type: NpcType,
    pub can_attack: bool,
    pub target: Option<crate::net::ConnectionId>, // Player target

    // Stats
    pub max_hp: i32,
    pub min_hp: i32,
    pub max_hit: i32,
    pub min_hit: i32,
    pub def: i32,
    pub poder_ataque: i32,
    pub poder_evasion: i32,
    pub alineacion: i32,

    // Economy
    pub give_exp: i32,
    pub give_gld: i32,
    pub give_gld_min: i32,
    pub give_gld_max: i32,

    // Commerce
    pub comercia: bool,
    pub inflacion: i32,
    pub tipo_items: i32,
    pub inv_respawn: bool,
    pub inventory: Vec<NpcInvSlot>,
    pub nro_items: i32,

    // Flags
    pub respawn: bool,
    pub active: bool,
    pub attackable: bool,
    pub name: String,
    pub desc: String,

    // Status effects
    pub veneno: bool,   // Poisons on hit
    pub paralyzed: bool,
    pub counter_paralisis: i32,  // Ticks remaining (decremented in game tick)

    // Spells
    pub lanza_spells: i32,
    pub spells: Vec<i32>,

    // Pet/summon owner
    pub maestro_user: Option<crate::net::ConnectionId>,

    // Area tracking (9x9 zone visibility — VB6 ModAreas.bas)
    pub area_id: i32,
    pub area_min_x: i32,
    pub area_min_y: i32,

    // Pathfinding (PathFinding.bas) — BFS path storage
    pub pf_path: Vec<(i32, i32)>,   // Computed path [(x,y), ...]
    pub pf_step: usize,             // Current step in path

    // Defense AI — saved state before switching to AI_DEFENSE (VB6: NpcAtacado)
    pub old_movement: i32,          // Original AI movement type to restore
    pub old_hostile: bool,          // Original hostile flag to restore
    pub attacked_by: String,        // Name of player who triggered defense mode

    // Crystal drops (copied from NpcData on spawn)
    pub cristales: bool,
    pub crystal_min1: i32,
    pub crystal_max1: i32,
    pub crystal_min2: i32,
    pub crystal_max2: i32,
    pub crystal_min3: i32,
    pub crystal_max3: i32,
    pub crystal_min4: i32,
    pub crystal_max4: i32,

    // Points/sounds (copied from NpcData on spawn)
    pub give_pts: i32,              // Faction points on kill
    pub snd1: i32,                  // Attack sound
    pub snd3: i32,                  // Death sound

    // Damage tracking for proportional EXP distribution
    pub damage_received: Vec<(crate::net::ConnectionId, i32)>,
}

/// Runtime NPC inventory slot.
#[derive(Debug, Clone, Default)]
pub struct NpcInvSlot {
    pub obj_index: i32,
    pub amount: i32,
    pub prob_tirar: i32, // Drop probability
}

impl NpcState {
    /// Create a new NPC runtime instance from database data.
    pub fn from_data(
        index: NpcIndex,
        data: &NpcData,
        char_index: CharIndex,
        map: i32,
        x: i32,
        y: i32,
    ) -> Self {
        Self {
            index,
            npc_number: data.index,
            char_index,
            body: data.body,
            head: data.head,
            heading: if data.heading > 0 { data.heading } else { 3 }, // Default south
            weapon_anim: 0,
            shield_anim: 0,
            casco_anim: 0,
            map,
            x,
            y,
            orig_map: map,
            orig_x: x,
            orig_y: y,
            movement: data.movement,
            hostile: data.hostile,
            npc_type: data.npc_type,
            can_attack: true,
            target: None,
            max_hp: data.max_hp,
            min_hp: data.max_hp, // Start at full HP
            max_hit: data.max_hit,
            min_hit: data.min_hit,
            def: data.def,
            poder_ataque: data.poder_ataque,
            poder_evasion: data.poder_evasion,
            alineacion: data.alineacion,
            give_exp: data.give_exp,
            give_gld: data.give_gld,
            give_gld_min: data.give_gld_min,
            give_gld_max: data.give_gld_max,
            comercia: data.comercia,
            inflacion: data.inflacion,
            tipo_items: data.tipo_items,
            inv_respawn: data.inv_respawn,
            inventory: {
                let mut inv: Vec<NpcInvSlot> = Vec::with_capacity(MAX_NPC_INV_SLOTS);
                for item in &data.items {
                    inv.push(NpcInvSlot { obj_index: item.obj_index, amount: item.amount, prob_tirar: item.prob_tirar });
                }
                // Pad to MAX_NPC_INV_SLOTS
                inv.resize(MAX_NPC_INV_SLOTS, NpcInvSlot::default());
                inv
            },
            nro_items: data.nro_items,
            respawn: data.respawn,
            active: true,
            attackable: data.attackable,
            name: data.name.clone(),
            desc: data.desc.clone(),
            veneno: data.veneno,
            paralyzed: false,
            counter_paralisis: 0,
            lanza_spells: data.lanza_spells,
            spells: data.spells.clone(),
            maestro_user: None,
            area_id: 0,
            area_min_x: 0,
            area_min_y: 0,
            pf_path: Vec::new(),
            pf_step: 0,
            old_movement: data.movement,
            old_hostile: data.hostile,
            attacked_by: String::new(),
            cristales: data.cristales,
            crystal_min1: data.crystal_min1,
            crystal_max1: data.crystal_max1,
            crystal_min2: data.crystal_min2,
            crystal_max2: data.crystal_max2,
            crystal_min3: data.crystal_min3,
            crystal_max3: data.crystal_max3,
            crystal_min4: data.crystal_min4,
            crystal_max4: data.crystal_max4,
            give_pts: data.give_pts,
            snd1: data.snd1,
            snd3: data.snd3,
            damage_received: Vec::new(),
        }
    }

    /// Build CC packet for this NPC.
    /// VB6 format (MakeNPCChar): body,head,heading,charindex,x,y,weapon,shield,helmet,,,,aura,npcnumber
    /// NPC has 4 empty fields (10-13) instead of name/status/priv, then aura + npc_number
    pub fn build_cc_packet(&self) -> String {
        format!(
            "CC{},{},{},{},{},{},{},{},{},,,,{},{}",
            self.body,
            self.head,
            self.heading,
            self.char_index.0,
            self.x,
            self.y,
            self.weapon_anim,
            self.shield_anim,
            self.casco_anim,
            0, // AuraA
            self.npc_number,
        )
    }

    /// Is this NPC alive?
    pub fn is_alive(&self) -> bool {
        // Non-hostile NPCs (shops, bankers, travelers) have max_hp=0 — they're always "alive"
        self.active && (self.max_hp == 0 || self.min_hp > 0)
    }
}
