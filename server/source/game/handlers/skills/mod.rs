//! Skills and crafting handlers — thin dispatcher module.
//!
//! Split into focused sub-modules:
//! - `resource` — fishing, woodcutting, mining
//! - `craft` — smithing, carpentry, smelting, upgrades, construction
//! - `combat` — taming, ranged attacks
//! - `stealth` — stealing, hiding

mod resource;
mod craft;
mod combat;
mod stealth;

// Re-export all sub-module items to parent (handlers) scope
pub(crate) use craft::*;
pub(crate) use stealth::*;


use crate::net::ConnectionId;
use crate::game::class_race::PlayerClass;
use crate::game::types::{GameState, UserState};
use crate::game::world;
use crate::game::handlers::common::*;
use crate::game::handlers::{
    send_inventory_slot, do_cast_spell,
};

pub(super) mod skill_id {
    pub const SUERTE: i32 = 1;
    pub const MAGIA: i32 = 2;
    pub const ROBAR: i32 = 3;
    pub const TACTICAS: i32 = 4;
    pub const ARMAS: i32 = 5;
    pub const MEDITAR: i32 = 6;
    pub const APUNALAR: i32 = 7;
    pub const OCULTARSE: i32 = 8;
    pub const SUPERVIVENCIA: i32 = 9;
    pub const TALAR: i32 = 10;
    pub const COMERCIAR: i32 = 11;
    pub const DEFENSA: i32 = 12;
    pub const PESCA: i32 = 13;
    pub const MINERIA: i32 = 14;
    pub const CARPINTERIA: i32 = 15;
    pub const HERRERIA: i32 = 16;
    pub const LIDERAZGO: i32 = 17;
    pub const DOMAR: i32 = 18;
    pub const PROYECTILES: i32 = 19;
    pub const WRESTERLING: i32 = 20;
    pub const NAVEGACION: i32 = 21;
    pub const DEFENSA_MAGICA: i32 = 22;
    pub const FUNDIR_METAL: i32 = 88;
}


/// Stamina costs.
pub(crate) const ESFUERZO_TALAR_RECOLECTOR: i32 = 2;
pub(crate) const ESFUERZO_TALAR_GENERAL: i32 = 6;
pub(crate) const ESFUERZO_PESCAR_RECOLECTOR: i32 = 2;
pub(crate) const ESFUERZO_PESCAR_GENERAL: i32 = 6;
pub(crate) const ESFUERZO_EXCAVAR_RECOLECTOR: i32 = 2;
pub(crate) const ESFUERZO_EXCAVAR_GENERAL: i32 = 6;

/// VB6: vlProleta = 2 — reputation gain per crafting action (non-criminals only)
const VL_PROLETA: i32 = 2;
/// VB6: MAXREP = 6000000 — max reputation cap
const MAX_REP: i32 = 6_000_000;

/// VB6: Grant crafting reputation (+2 Proleta) if user is not criminal.
fn grant_crafting_rep(user: &mut UserState) {
    if !user.criminal {
        user.reputation = (user.reputation + VL_PROLETA).min(MAX_REP);
    }
}

/// VB6 13.3: Suerte = Int(-0.00125 * Skill^2 - 0.3 * Skill + 49)
/// Used for resource extraction (fishing, mining, woodcutting).
pub(super) fn luck_denominator(skill: i32) -> i32 {
    let s = skill as f64;
    let suerte = (-0.00125 * s * s - 0.3 * s + 49.0) as i32;
    suerte.max(1) // Ensure at least 1 to avoid div by zero
}

/// VB6 13.3: Lookup-based luck for steal, meditate, and other skills.
pub(super) fn luck_denominator_lookup(skill: i32) -> i32 {
    match skill {
        0..=10 => 35,
        11..=20 => 30,
        21..=30 => 28,
        31..=40 => 24,
        41..=50 => 22,
        51..=60 => 20,
        61..=70 => 18,
        71..=80 => 15,
        81..=90 => 10,
        91..=99 => 7,
        100.. => 5,
        _ => 35,
    }
}

/// VB6 13.3: MaxItemsExtraibles(Level) = Max(1, Int((Level - 2) * 0.2)) + 1
fn max_items_extraibles(level: i32) -> i32 {
    ((level - 2) as f64 * 0.2).floor().max(1.0) as i32 + 1
}

// VB6 13.3 skill constants (Declares.bas)
const MAXSKILLPOINTS: i32 = 100;
const EXP_ACIERTO_SKILL: i32 = 50;  // XP on successful skill use
const EXP_FALLO_SKILL: i32 = 20;    // XP on failed skill use
const ELU_SKILL_INICIAL: i32 = 200;  // Base ELU for skill progression

/// VB6 13.3 level cap table for skills (General.bas:347-397).
fn skill_level_cap(char_level: i32) -> i32 {
    match char_level {
        1..=2 => 3,
        3 => 5,
        4 => 7,
        5 => 10,
        6 => 13,
        7 => 15,
        8 => 17,
        9 => 20,
        10 => 23,
        11 => 25,
        12 => 27,
        13 => 30,
        14 => 33,
        15 => 35,
        16 => 37,
        17 => 40,
        18 => 43,
        19 => 45,
        20 => 47,
        _ => 100, // Level 21+ = no cap (100 max)
    }
}

/// VB6 13.3: CheckEluSkill — calculate ELU for a skill.
pub(super) fn calc_elu_skill(skill_level: i32) -> i32 {
    if skill_level >= MAXSKILLPOINTS { return 0; }
    (ELU_SKILL_INICIAL as f64 * 1.05f64.powi(skill_level)) as i32
}

/// VB6 13.3: SubirSkill — gain skill XP from use.
pub(super) fn try_level_skill(user: &mut UserState, skill_idx: usize) -> bool {
    try_level_skill_with_hit(user, skill_idx, true)
}

pub(super) fn try_level_skill_with_hit(user: &mut UserState, skill_idx: usize, hit: bool) -> bool {
    if skill_idx == 0 || skill_idx > 21 { return false; }
    let idx = skill_idx - 1; // 0-based

    // VB6 13.3 parity: can't level skills while starving or dehydrated
    if user.min_ham <= 1 || user.min_agua <= 1 { return false; }

    // Max skill check
    if user.skills[idx] >= MAXSKILLPOINTS { return false; }

    // Level cap check
    let cap = skill_level_cap(user.level);
    if user.skills[idx] >= cap { return false; }

    // Add skill XP based on success/failure
    let xp_gain = if hit { EXP_ACIERTO_SKILL } else { EXP_FALLO_SKILL };
    user.exp_skills[idx] += xp_gain;

    // Check if enough XP to level up
    if user.elu_skills[idx] <= 0 {
        user.elu_skills[idx] = calc_elu_skill(user.skills[idx]);
    }

    if user.exp_skills[idx] >= user.elu_skills[idx] {
        user.exp_skills[idx] -= user.elu_skills[idx];
        user.skills[idx] += 1;
        user.exp += 50; // VB6: +50 character XP on skill up
        user.elu_skills[idx] = calc_elu_skill(user.skills[idx]);
        return true;
    }
    false
}

/// Initialize ELU values for all skills (call on login).
pub(super) fn init_elu_skills(user: &mut UserState) {
    for i in 0..22 {
        user.elu_skills[i] = calc_elu_skill(user.skills[i]);
    }
}

/// Check if user has the right tool equipped for a skill.
pub(super) fn has_tool_equipped(user: &UserState, tool_obj_index: i32) -> bool {
    if user.equip.weapon > 0 && user.equip.weapon <= user.inventory.len() {
        let slot = &user.inventory[user.equip.weapon - 1];
        return slot.obj_index == tool_obj_index && slot.equipped;
    }
    false
}

/// Get the equipped weapon obj_index.
pub(super) fn equipped_weapon_obj(user: &UserState) -> i32 {
    if user.equip.weapon > 0 && user.equip.weapon <= user.inventory.len() {
        let slot = &user.inventory[user.equip.weapon - 1];
        if slot.equipped { return slot.obj_index; }
    }
    0
}

/// Check if class is Worker/Trabajador.
pub(super) fn is_recolector(class: PlayerClass) -> bool {
    class.is_recolector()
}

/// VB6: ModFundicion — mining/smelting class modifier.
/// Worker=1x, Others=3x (non-workers need 3x more skill).
pub(super) fn mod_fundicion(class: PlayerClass) -> f32 {
    if class.is_recolector() { 1.0 } else { 3.0 }
}

/// VB6: ModHerreria — smithing class modifier.
/// Worker=1x, Others=4x (non-workers need 4x more skill).
pub(super) fn mod_herreria(class: PlayerClass) -> f32 {
    if class.is_recolector() { 1.0 } else { 4.0 }
}

/// VB6: ModCarpinteria — carpentry class modifier.
/// Worker=1x, Others=3x (non-workers need 3x more skill).
pub(super) fn mod_carpinteria(class: PlayerClass) -> f32 {
    if class.is_recolector() { 1.0 } else { 3.0 }
}

/// VB6: ModNavegacion — navigation skill modifier for boat boarding.
/// Pirata=1.0 (no penalty), Worker with Pesca=100: 1.71, Worker otherwise: 2.0,
/// all others: 2.0.
/// `fishing_skill` is the player's Pesca skill value (0–100).
pub(crate) fn mod_navegacion(class: PlayerClass, fishing_skill: i32) -> f32 {
    match class {
        PlayerClass::Pirata => 1.0,
        PlayerClass::Trabajador => {
            if fishing_skill >= 100 { 1.71 } else { 2.0 }
        }
        _ => 2.0,
    }
}

/// Helper: check if user has at least `amount` of an item.
fn has_items(state: &GameState, conn_id: ConnectionId, obj_index: i32, amount: i32) -> bool {
    if amount <= 0 { return true; }
    let total: i32 = state.users.get(&conn_id)
        .map(|u| u.inventory.iter()
            .filter(|s| s.obj_index == obj_index)
            .map(|s| s.amount)
            .sum())
        .unwrap_or(0);
    total >= amount
}

/// Helper: remove `amount` of an item from inventory, updating slots.
async fn remove_items(state: &mut GameState, conn_id: ConnectionId, obj_index: i32, mut amount: i32) {
    if amount <= 0 { return; }
    let slots_to_update: Vec<usize> = {
        let mut slots = Vec::new();
        if let Some(u) = state.users.get_mut(&conn_id) {
            for i in 0..u.inventory.len() {
                if u.inventory[i].obj_index == obj_index && amount > 0 {
                    let take = amount.min(u.inventory[i].amount);
                    amount -= take;
                    let new_amt = u.inventory[i].amount - take;
                    if new_amt <= 0 {
                        u.inventory[i].obj_index = 0;
        u.inventory[i].amount = 0;
        u.inventory[i].equipped = false;
                    } else {
                        u.inventory[i].amount = new_amt;
                    }
                    slots.push(i);
                }
            }
        }
        slots
    };
    for idx in slots_to_update {
        send_inventory_slot(state, conn_id, idx).await;
    }
}

/// WLC — Work Left Click (main skill dispatch).
pub(super) async fn handle_work_left_click(state: &mut GameState, conn_id: ConnectionId, target_x: i32, target_y: i32, skill_type: i32) {

    // Validate user
    let (dead, meditating, _map, ux, uy) = match state.users.get(&conn_id) {
        Some(u) if u.logged => (u.dead, u.meditating, u.pos_map, u.pos_x, u.pos_y),
        _ => return,
    };

    if dead {
        state.send_msg_id(conn_id, 3, "");
        return;
    }
    if meditating { return; }

    // Range check
    if (target_x - ux).abs() > world::MIN_X_BORDER || (target_y - uy).abs() > world::MIN_Y_BORDER {
        return;
    }

    // Anti-cheat: check work cooldown (except for ranged attack which has its own)
    if skill_type != skill_id::PROYECTILES && !puede_trabajar(state, conn_id) {
        return;
    }

    match skill_type {
        skill_id::PESCA => {
            resource::do_pescar(state, conn_id, target_x, target_y).await;
        }
        skill_id::TALAR => {
            resource::do_talar(state, conn_id, target_x, target_y).await;
        }
        skill_id::MINERIA => {
            resource::do_mineria(state, conn_id, target_x, target_y).await;
        }
        skill_id::DOMAR => {
            combat::do_domar(state, conn_id, target_x, target_y).await;
        }
        skill_id::HERRERIA => {
            craft::do_herreria(state, conn_id, target_x, target_y).await;
        }
        skill_id::FUNDIR_METAL => {
            craft::do_fundir(state, conn_id).await;
        }
        skill_id::ROBAR => {
            stealth::do_robar(state, conn_id, target_x, target_y).await;
        }
        skill_id::PROYECTILES => {
            // VB6 line 1846: Call LookatTile before ranged attack
            crate::game::handlers::do_lookat_tile(state, conn_id, target_x, target_y).await;
            combat::do_ranged_attack(state, conn_id, target_x, target_y).await;
        }
        skill_id::MAGIA => {
            crate::game::handlers::do_lookat_tile(state, conn_id, target_x, target_y).await;

            // Anti-cheat cooldown
            if !puede_castear(state, conn_id) {
                return;
            }

            // Cast the pending spell
            let pending = state.users.get(&conn_id).map(|u| u.pending_spell).unwrap_or(0);
            if pending > 0 {
                do_cast_spell(state, conn_id).await;
                send_stats_mana(state, conn_id).await;
                send_stats_hp(state, conn_id).await;
            } else {
                state.send_msg_id(conn_id, 288, "");
            }
        }
        skill_id::OCULTARSE => {
            stealth::do_ocultarse(state, conn_id).await;
        }
        skill_id::SUERTE | skill_id::TACTICAS | skill_id::ARMAS |
        skill_id::MEDITAR | skill_id::APUNALAR | skill_id::SUPERVIVENCIA |
        skill_id::COMERCIAR | skill_id::DEFENSA | skill_id::LIDERAZGO |
        skill_id::WRESTERLING | skill_id::NAVEGACION | skill_id::DEFENSA_MAGICA => {
            // Passive skills have no click action — VB6 silently ignores
        }
        _ => {
            tracing::info!("[WLC] #{} unknown skill type {}", conn_id, skill_type);
        }
    }
}
