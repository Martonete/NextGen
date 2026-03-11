//! Skills and crafting handlers: fishing, logging, mining, taming, smithing,
//! smelting, stealing, hiding, backstab, ranged combat, construction.
//! Extracted from mod.rs to reduce file size.

use tracing::info;
use crate::net::ConnectionId;
use crate::game::class_race::PlayerClass;
use crate::game::types::{GameState, UserState, SendTarget, InventorySlot, MAX_INVENTORY_SLOTS};
use crate::game::world;
use crate::protocol::{font_index, fields::read_field, binary_packets};
use crate::protocol::packets::MultiMessageID;
use crate::data::objects::{ObjData, ObjType};
use super::common::*;
use super::{
    send_inventory_slot, send_full_inventory, user_die, do_cast_spell,
    calc_attack_power, calc_attack_power_with_balance, calc_defense_power, calc_armor_absorption,
    class_damage_modifier, class_damage_modifier_from_balance,
    check_user_level,
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

use crate::game::constants::*;

/// Stamina costs.
const ESFUERZO_TALAR_RECOLECTOR: i32 = 2;
const ESFUERZO_TALAR_GENERAL: i32 = 4;
const ESFUERZO_PESCAR_RECOLECTOR: i32 = 1;
const ESFUERZO_PESCAR_GENERAL: i32 = 3;
const ESFUERZO_EXCAVAR_RECOLECTOR: i32 = 2;
const ESFUERZO_EXCAVAR_GENERAL: i32 = 5;

/// VB6: vlProleta = 2 — reputation gain per crafting action (non-criminals only)
const VL_PROLETA: i32 = 2;
/// VB6: MAXREP = 500000 — max reputation cap
const MAX_REP: i32 = 500000;

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
/// Used to determine how many resources a Worker class gets per extraction.
fn max_items_extraibles(level: i32) -> i32 {
    ((level - 2) as f64 * 0.2).floor().max(1.0) as i32 + 1
}

/// Simple random number in range [min, max] inclusive.
// random_number — moved to common.rs

// VB6 13.3 skill constants (Declares.bas)
const MAXSKILLPOINTS: i32 = 100;
const EXP_ACIERTO_SKILL: i32 = 50;  // XP on successful skill use
const EXP_FALLO_SKILL: i32 = 20;    // XP on failed skill use
const ELU_SKILL_INICIAL: i32 = 200;  // Base ELU for skill progression

/// VB6 13.3 level cap table for skills (General.bas:347-397).
/// LevelSkill[n].LevelValue — skill points capped at this value per character level.
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

/// VB6 13.3: CheckEluSkill — calculate ELU for a skill (Modulo_UsUaRiOs.bas:2468-2490).
/// ELU = 200 * 1.05^current_skill_level
pub(super) fn calc_elu_skill(skill_level: i32) -> i32 {
    if skill_level >= MAXSKILLPOINTS { return 0; }
    (ELU_SKILL_INICIAL as f64 * 1.05f64.powi(skill_level)) as i32
}

/// VB6 13.3: SubirSkill — gain skill XP from use (Modulo_UsUaRiOs.bas:1327-1376).
/// `hit` = true if the skill action succeeded, false if it failed.
/// Returns true if skill leveled up.
pub(super) fn try_level_skill(user: &mut UserState, skill_idx: usize) -> bool {
    try_level_skill_with_hit(user, skill_idx, true)
}

pub(super) fn try_level_skill_with_hit(user: &mut UserState, skill_idx: usize, hit: bool) -> bool {
    if skill_idx == 0 || skill_idx > 21 { return false; }
    let idx = skill_idx - 1; // 0-based

    // Must not be hungry or thirsty
    if user.min_ham <= 0 || user.min_agua <= 0 { return false; }

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
        // Recalculate ELU for new skill level
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

/// Check if class is Worker/Trabajador (VB6: eClass.Worker = 11).
pub(super) fn is_recolector(class: PlayerClass) -> bool {
    class.is_recolector()
}

/// WLC — Work Left Click (main skill dispatch).
pub(super) async fn handle_work_left_click(state: &mut GameState, conn_id: ConnectionId, data: &str) {
    let payload = strip_opcode(data, 3); // "WLC" = 3 chars
    let target_x: i32 = match read_field(1, payload, ',').parse() {
        Ok(v) => v,
        _ => return,
    };
    let target_y: i32 = match read_field(2, payload, ',').parse() {
        Ok(v) => v,
        _ => return,
    };
    let skill_type: i32 = match read_field(3, payload, ',').parse() {
        Ok(v) => v,
        _ => return,
    };

    // Validate user
    let (dead, meditating, map, ux, uy) = match state.users.get(&conn_id) {
        Some(u) if u.logged => (u.dead, u.meditating, u.pos_map, u.pos_x, u.pos_y),
        _ => return,
    };

    if dead {
        state.send_msg_id(conn_id, 3, "").await;
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
            do_pescar(state, conn_id, target_x, target_y).await;
        }
        skill_id::TALAR => {
            do_talar(state, conn_id, target_x, target_y).await;
        }
        skill_id::MINERIA => {
            do_mineria(state, conn_id, target_x, target_y).await;
        }
        skill_id::DOMAR => {
            do_domar(state, conn_id, target_x, target_y).await;
        }
        skill_id::HERRERIA => {
            do_herreria(state, conn_id, target_x, target_y).await;
        }
        skill_id::FUNDIR_METAL => {
            do_fundir(state, conn_id).await;
        }
        skill_id::ROBAR => {
            do_robar(state, conn_id, target_x, target_y).await;
        }
        skill_id::PROYECTILES => {
            // VB6 line 1846: Call LookatTile before ranged attack
            super::inventory::do_lookat_tile(state, conn_id, target_x, target_y).await;
            do_ranged_attack(state, conn_id, target_x, target_y).await;
        }
        skill_id::MAGIA => {
            // VB6: WLC Magia flow (TCP_HandleData1.bas line 1910-1931)
            // 1. LookatTile (sets targets + shows info in console)
            // 2. Anti-cheat cooldown
            // 3. Cast pending spell

            // VB6 line 1915: Call LookatTile(userindex, Map, X, Y)
            // This shows "Ves a <name>" for users AND NPC info (name + HP status)
            // in console BEFORE the spell is cast.
            super::inventory::do_lookat_tile(state, conn_id, target_x, target_y).await;

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
                state.send_msg_id(conn_id, 288, "").await;
            }
        }
        skill_id::OCULTARSE => {
            do_ocultarse(state, conn_id).await;
        }
        _ => {
            // Unsupported skill
            info!("[WLC] #{} unsupported skill type {}", conn_id, skill_type);
        }
    }
}

/// Fishing (DoPescar).
pub(super) async fn do_pescar(state: &mut GameState, conn_id: ConnectionId, tx: i32, ty: i32) {
    let (map, ux, uy, class, skill) = match state.users.get(&conn_id) {
        Some(u) => (u.pos_map, u.pos_x, u.pos_y, u.class, u.skills[12]), // Pesca = index 12 (1-based 13)
        None => return,
    };

    // Check equipped fishing tool
    let weapon = state.users.get(&conn_id).map(|u| equipped_weapon_obj(u)).unwrap_or(0);
    if weapon == 0 {
        state.send_console(conn_id, "Necesitas una caña de pescar", font_index::INFO).await;
        return;
    }

    // Check target has water (VB6 HayAgua)
    let has_water = state.hay_agua(map, tx, ty);

    if !has_water {
        state.send_msg_id(conn_id, 250, "").await;
        return;
    }

    // Stamina cost
    let sta_cost = if is_recolector(class) { ESFUERZO_PESCAR_RECOLECTOR } else { ESFUERZO_PESCAR_GENERAL };
    if let Some(u) = state.users.get(&conn_id) {
        if u.min_sta < sta_cost {
            state.send_msg_id(conn_id, 17, "").await;
            return;
        }
    }

    // Deduct stamina
    if let Some(u) = state.users.get_mut(&conn_id) {
        u.min_sta -= sta_cost;
    }

    // Play sound to area
    let snd = binary_packets::write_play_wave(SND_PESCAR as u8, ux as u8, uy as u8);
    state.send_data_bytes(SendTarget::ToArea { map, x: ux, y: uy }, &snd).await;

    // Luck roll
    let suerte = luck_denominator(skill);
    let roll = random_number(1, suerte);

    if roll <= 6 {
        // VB6: Worker class gets MaxItemsExtraibles, others get 1
        let level = state.users.get(&conn_id).map(|u| u.level).unwrap_or(1);
        let amount = if is_recolector(class) {
            random_number(1, max_items_extraibles(level))
        } else {
            1
        };

        let slot = find_or_add_inv_slot(state, conn_id, PESCADO_OBJ, amount);
        if let Some(idx) = slot {
            send_inventory_slot(state, conn_id, idx).await;
            state.send_msg_id(conn_id, 813, "").await;
        }

        // VB6: SubirSkill on success
        if let Some(u) = state.users.get_mut(&conn_id) {
            try_level_skill_with_hit(u, 12, true); // Pesca = index 12
        }
    } else {
        state.send_msg_id(conn_id, 814, "").await;

        // VB6: SubirSkill on failure
        if let Some(u) = state.users.get_mut(&conn_id) {
            try_level_skill_with_hit(u, 12, false);
        }
    }
    send_stats_sta(state, conn_id).await;
}

/// Woodcutting (DoTalar).
pub(super) async fn do_talar(state: &mut GameState, conn_id: ConnectionId, tx: i32, ty: i32) {
    let (map, ux, uy, class, skill) = match state.users.get(&conn_id) {
        Some(u) => (u.pos_map, u.pos_x, u.pos_y, u.class, u.skills[9]), // Talar = index 9 (1-based 10)
        None => return,
    };

    // Check equipped axe
    let weapon = state.users.get(&conn_id).map(|u| equipped_weapon_obj(u)).unwrap_or(0);
    if weapon != HACHA_LENADOR {
        state.send_console(conn_id, "Necesitas un hacha de leñador", font_index::INFO).await;
        return;
    }

    // Distance check
    let dist = (tx - ux).abs().max((ty - uy).abs());
    if dist > 2 || dist == 0 {
        return;
    }

    // Check tile has a tree (otArboles = 4)
    let tile_obj = state.game_data.maps.get(map as usize)
        .and_then(|m| m.as_ref())
        .and_then(|m| {
            if tx >= 1 && tx <= 100 && ty >= 1 && ty <= 100 {
                let tile = &m.tiles[(ty - 1) as usize][(tx - 1) as usize];
                if tile.obj.obj_index > 0 {
                    state.get_object(tile.obj.obj_index as i32).map(|o| o.obj_type)
                } else {
                    None
                }
            } else {
                None
            }
        });

    // Also check ground items on world grid
    let ground_obj_type = state.world.grid(map)
        .and_then(|g| g.tile(tx, ty))
        .and_then(|t| {
            if t.ground_item.obj_index > 0 {
                state.get_object(t.ground_item.obj_index).map(|o| o.obj_type)
            } else {
                None
            }
        });

    let is_tree = tile_obj == Some(ObjType::Trees) || ground_obj_type == Some(ObjType::Trees);
    if !is_tree {
        state.send_msg_id(conn_id, 255, "").await;
        return;
    }

    // Stamina cost
    let sta_cost = if is_recolector(class) { ESFUERZO_TALAR_RECOLECTOR } else { ESFUERZO_TALAR_GENERAL };
    if let Some(u) = state.users.get(&conn_id) {
        if u.min_sta < sta_cost {
            state.send_msg_id(conn_id, 17, "").await;
            return;
        }
    }
    if let Some(u) = state.users.get_mut(&conn_id) {
        u.min_sta -= sta_cost;
    }

    // Play sound
    let snd = binary_packets::write_play_wave(SND_TALAR as u8, ux as u8, uy as u8);
    state.send_data_bytes(SendTarget::ToArea { map, x: ux, y: uy }, &snd).await;

    // Luck roll
    let suerte = luck_denominator(skill);
    let roll = random_number(1, suerte);

    if roll <= 6 {
        // VB6: Worker class gets MaxItemsExtraibles, others get 1
        let level = state.users.get(&conn_id).map(|u| u.level).unwrap_or(1);
        let amount = if is_recolector(class) {
            random_number(1, max_items_extraibles(level))
        } else {
            1
        };
        let slot = find_or_add_inv_slot(state, conn_id, LENA_OBJ, amount);
        if let Some(idx) = slot {
            send_inventory_slot(state, conn_id, idx).await;
            state.send_msg_id(conn_id, 825, "").await;
        }

        if let Some(u) = state.users.get_mut(&conn_id) {
            try_level_skill_with_hit(u, 9, true); // Talar = index 9
        }
    } else {
        state.send_msg_id(conn_id, 826, "").await;

        if let Some(u) = state.users.get_mut(&conn_id) {
            try_level_skill_with_hit(u, 9, false);
        }
    }
    send_stats_sta(state, conn_id).await;
}

/// Mining (DoMineria).
pub(super) async fn do_mineria(state: &mut GameState, conn_id: ConnectionId, tx: i32, ty: i32) {
    let (map, ux, uy, class, skill) = match state.users.get(&conn_id) {
        Some(u) => (u.pos_map, u.pos_x, u.pos_y, u.class, u.skills[13]), // Mineria = index 13 (1-based 14)
        None => return,
    };

    // Check equipped pick
    let weapon = state.users.get(&conn_id).map(|u| equipped_weapon_obj(u)).unwrap_or(0);
    if weapon != PIQUETE_MINERO {
        state.send_console(conn_id, "Necesitas un pico de minero", font_index::INFO).await;
        return;
    }

    // Distance check
    let dist = (tx - ux).abs().max((ty - uy).abs());
    if dist > 2 {
        return;
    }

    // Check tile has a mineral deposit (otYacimiento = Deposit = 22)
    let mineral_obj = state.world.grid(map)
        .and_then(|g| g.tile(tx, ty))
        .and_then(|t| {
            if t.ground_item.obj_index > 0 {
                state.get_object(t.ground_item.obj_index).cloned()
            } else {
                None
            }
        })
        .or_else(|| {
            state.game_data.maps.get(map as usize)
                .and_then(|m| m.as_ref())
                .and_then(|m| {
                    if tx >= 1 && tx <= 100 && ty >= 1 && ty <= 100 {
                        let tile = &m.tiles[(ty - 1) as usize][(tx - 1) as usize];
                        if tile.obj.obj_index > 0 {
                            state.get_object(tile.obj.obj_index as i32).cloned()
                        } else {
                            None
                        }
                    } else {
                        None
                    }
                })
        });

    let mineral_data = match mineral_obj {
        Some(ref o) if o.obj_type == ObjType::Deposit => o.clone(),
        _ => {
            state.send_msg_id(conn_id, 256, "").await;
            return;
        }
    };

    // Stamina cost
    let sta_cost = if is_recolector(class) { ESFUERZO_EXCAVAR_RECOLECTOR } else { ESFUERZO_EXCAVAR_GENERAL };
    if let Some(u) = state.users.get(&conn_id) {
        if u.min_sta < sta_cost {
            state.send_msg_id(conn_id, 17, "").await;
            return;
        }
    }
    if let Some(u) = state.users.get_mut(&conn_id) {
        u.min_sta -= sta_cost;
    }

    // Play sound
    let snd = binary_packets::write_play_wave(SND_MINERO as u8, ux as u8, uy as u8);
    state.send_data_bytes(SendTarget::ToArea { map, x: ux, y: uy }, &snd).await;

    // Luck roll
    let suerte = luck_denominator(skill);
    let roll = random_number(1, suerte);

    if roll <= 5 {
        // VB6: Mining threshold is 5 (slightly harder than fishing's 6)
        let mineral_item = if mineral_data.mineral_index > 0 {
            mineral_data.mineral_index
        } else {
            HIERRO_CRUDO
        };

        // VB6: Worker class gets MaxItemsExtraibles, others get 1
        let level = state.users.get(&conn_id).map(|u| u.level).unwrap_or(1);
        let amount = if is_recolector(class) {
            random_number(1, max_items_extraibles(level))
        } else {
            1
        };
        let slot = find_or_add_inv_slot(state, conn_id, mineral_item, amount);
        if let Some(idx) = slot {
            send_inventory_slot(state, conn_id, idx).await;
            state.send_msg_id(conn_id, 827, "").await;
        }

        if let Some(u) = state.users.get_mut(&conn_id) {
            try_level_skill_with_hit(u, 13, true); // Mineria = index 13
        }
    } else {
        state.send_msg_id(conn_id, 828, "").await;

        if let Some(u) = state.users.get_mut(&conn_id) {
            try_level_skill_with_hit(u, 13, false);
        }
    }
    send_stats_sta(state, conn_id).await;
}

/// Taming (DoDomar).
pub(super) async fn do_domar(state: &mut GameState, conn_id: ConnectionId, tx: i32, ty: i32) {
    let (map, ux, uy) = match state.users.get(&conn_id) {
        Some(u) => (u.pos_map, u.pos_x, u.pos_y),
        None => return,
    };

    // Distance check
    if (tx - ux).abs() > 2 || (ty - uy).abs() > 2 {
        return;
    }

    // Find NPC on tile
    let npc_idx = state.world.grid(map)
        .and_then(|g| g.tile(tx, ty))
        .and_then(|t| if t.npc_index > 0 { Some(t.npc_index as usize) } else { None });

    let npc_idx = match npc_idx {
        Some(idx) => idx,
        None => {
            state.send_msg_id(conn_id, 258, "").await;
            return;
        }
    };

    // Check NPC is tameable
    let npc_number = match state.get_npc(npc_idx) {
        Some(npc) if npc.is_alive() => npc.npc_number,
        _ => return,
    };
    let domable = state.game_data.npcs.get(npc_number)
        .map(|d| d.domable)
        .unwrap_or(0);

    if domable <= 0 {
        state.send_msg_id(conn_id, 257, "").await;
        return;
    }

    // VB6 13.3: DoDomar formula
    let (carisma, skill_domar) = match state.users.get(&conn_id) {
        Some(u) => (u.attributes[3], u.skills[17]), // Cha=3, Domar=17 (1-based 18)
        None => return,
    };

    // VB6: puntosDomar = Int(Carisma) * Int(UserSkills(Domar))
    let puntos_domar = carisma as i64 * skill_domar as i64;

    // VB6: Flute modifiers (ring slot check)
    const FLAUTA_MAGICA: i32 = 208;
    const FLAUTA_ELFICA: i32 = 1050;
    let ring_obj = state.users.get(&conn_id)
        .and_then(|u| {
            if u.equip.ring > 0 && u.equip.ring <= MAX_INVENTORY_SLOTS {
                Some(u.inventory[u.equip.ring - 1].obj_index)
            } else {
                None
            }
        })
        .unwrap_or(0);

    let modifier = if ring_obj == FLAUTA_ELFICA {
        0.8 // 20% bonus
    } else if ring_obj == FLAUTA_MAGICA {
        0.89 // 11% bonus
    } else {
        1.0 // No flute
    };

    // VB6: puntosRequeridos = Domable * modifier
    let puntos_requeridos = (domable as f64 * modifier) as i64;

    // VB6: Success = (puntosRequeridos <= puntosDomar) AND (RandomNumber(1, 5) = 1)
    let success = puntos_requeridos <= puntos_domar && random_number(1, 5) == 1;

    if success {
        // VB6: Check max pets (MAXMASCOTAS = 3)
        let num_pets = state.users.get(&conn_id).map(|u| u.nro_mascotas).unwrap_or(0);
        if num_pets >= 3 {
            state.send_console(conn_id, "No puedes tener más mascotas!", font_index::INFO).await;
        } else {
            // Assign pet to user
            if let Some(npc) = state.get_npc_mut(npc_idx) {
                npc.maestro_user = Some(conn_id);
                npc.hostile = false;
                npc.target = None;
            }
            if let Some(u) = state.users.get_mut(&conn_id) {
                u.nro_mascotas += 1;
                // Store in pet slots
                for i in 0..3 {
                    if u.mascotas_index[i] == 0 {
                        u.mascotas_index[i] = npc_idx;
                        u.mascotas_type[i] = npc_number as i32;
                        break;
                    }
                }
            }
            state.send_console(conn_id, "Has domado a la criatura!", font_index::INFO).await;

            // VB6: SubirSkill on success
            if let Some(u) = state.users.get_mut(&conn_id) {
                try_level_skill_with_hit(u, 17, true); // Domar = index 17
            }
        }
    } else {
        state.send_console(conn_id, "No has podido domar a la criatura.", font_index::INFO).await;

        if let Some(u) = state.users.get_mut(&conn_id) {
            try_level_skill_with_hit(u, 17, false);
        }
    }
}

/// Blacksmith (open UI when clicking anvil).
pub(super) async fn do_herreria(state: &mut GameState, conn_id: ConnectionId, tx: i32, ty: i32) {
    let (map, ux, uy) = match state.users.get(&conn_id) {
        Some(u) => (u.pos_map, u.pos_x, u.pos_y),
        None => return,
    };

    // Distance check
    if (tx - ux).abs() > 2 || (ty - uy).abs() > 2 {
        return;
    }

    // Check target is an anvil (ObjType::Anvil = 27)
    let is_anvil = state.world.grid(map)
        .and_then(|g| g.tile(tx, ty))
        .and_then(|t| {
            if t.ground_item.obj_index > 0 {
                state.get_object(t.ground_item.obj_index).map(|o| o.obj_type == ObjType::Anvil)
            } else {
                None
            }
        })
        .unwrap_or(false);

    // Also check map static obj
    let is_anvil = is_anvil || state.game_data.maps.get(map as usize)
        .and_then(|m| m.as_ref())
        .map(|m| {
            if tx >= 1 && tx <= 100 && ty >= 1 && ty <= 100 {
                let tile = &m.tiles[(ty - 1) as usize][(tx - 1) as usize];
                if tile.obj.obj_index > 0 {
                    state.get_object(tile.obj.obj_index as i32)
                        .map(|o| o.obj_type == ObjType::Anvil)
                        .unwrap_or(false)
                } else {
                    false
                }
            } else {
                false
            }
        })
        .unwrap_or(false);

    if !is_anvil {
        state.send_msg_id(conn_id, 263, "").await;
        return;
    }

    // Send buildable items lists (VB6 13.3 binary format)
    let skill_herreria = state.users.get(&conn_id).map(|u| u.skills[15]).unwrap_or(0); // Herreria = 15 (1-based 16)

    let mut weapons = Vec::new();
    let mut armors = Vec::new();
    // VB6: Use parsed ArmasHerrero.dat / ArmadurasHerrero.dat lists instead of scanning all objects
    for &idx in &state.game_data.crafting.smith_weapons {
        if let Some(obj) = state.get_object(idx) {
            if obj.sk_herreria > 0 && obj.sk_herreria <= skill_herreria {
                weapons.push(binary_packets::CraftItem {
                    name: obj.name.clone(),
                    grh_index: obj.grh_index as i16,
                    mat1: obj.ling_h as i16,
                    mat2: obj.ling_p as i16,
                    mat3: obj.ling_o as i16,
                    obj_index: obj.index as i16,
                    upgrade: 0,
                });
            }
        }
    }
    for &idx in &state.game_data.crafting.smith_armors {
        if let Some(obj) = state.get_object(idx) {
            if obj.sk_herreria > 0 && obj.sk_herreria <= skill_herreria {
                armors.push(binary_packets::CraftItem {
                    name: obj.name.clone(),
                    grh_index: obj.grh_index as i16,
                    mat1: obj.ling_h as i16,
                    mat2: obj.ling_p as i16,
                    mat3: obj.ling_o as i16,
                    obj_index: obj.index as i16,
                    upgrade: 0,
                });
            }
        }
    }

    let pkt = binary_packets::write_smith_weapons(&weapons);
    state.send_bytes(conn_id, &pkt).await;
    let pkt = binary_packets::write_smith_armors(&armors);
    state.send_bytes(conn_id, &pkt).await;
    let pkt = binary_packets::write_show_blacksmith_form();
    state.send_bytes(conn_id, &pkt).await;
}

/// Carpenter (open UI — sends buildable items list + ShowCarpenterForm).
/// VB6: triggered by double-clicking equipped serrucho.
pub(super) async fn do_carpinteria(state: &mut GameState, conn_id: ConnectionId) {
    let skill_carpinteria = state.users.get(&conn_id).map(|u| u.skills[14]).unwrap_or(0); // Carpinteria=14 (1-based 15)

    let mut items = Vec::new();
    // VB6: Use parsed ObjCarpintero.dat list instead of scanning all objects
    for &idx in &state.game_data.crafting.carpenter_items {
        if let Some(obj) = state.get_object(idx) {
            if obj.sk_carpinteria > 0 && obj.sk_carpinteria <= skill_carpinteria {
                items.push(binary_packets::CraftItem {
                    name: obj.name.clone(),
                    grh_index: obj.grh_index as i16,
                    mat1: obj.madera as i16,
                    mat2: 0, // MaderaElfica — not loaded yet
                    mat3: 0, // unused for carpenter
                    obj_index: obj.index as i16,
                    upgrade: 0,
                });
            }
        }
    }

    let pkt = binary_packets::write_carp_items(&items);
    state.send_bytes(conn_id, &pkt).await;
    let pkt = binary_packets::write_show_carpenter_form();
    state.send_bytes(conn_id, &pkt).await;
}

/// Smelting (FundirMineral).
pub(super) async fn do_fundir(state: &mut GameState, conn_id: ConnectionId) {
    // Find mineral in inventory
    let mineral_slot = match state.users.get(&conn_id) {
        Some(u) => {
            u.inventory.iter().enumerate().find_map(|(i, slot)| {
                if slot.obj_index > 0 {
                    state.get_object(slot.obj_index).and_then(|o| {
                        if o.obj_type == ObjType::Mineral {
                            Some((i, slot.obj_index, slot.amount, o.lingote_index))
                        } else {
                            None
                        }
                    })
                } else {
                    None
                }
            })
        }
        None => return,
    };

    let (slot_idx, mineral_obj, amount, lingote_idx) = match mineral_slot {
        Some(data) => data,
        None => {
            state.send_msg_id(conn_id, 259, "").await;
            return;
        }
    };

    // VB6 13.3: Minerals per ingot (HierroCrudo=14, PlataCruda=20, OroCrudo=35)
    let minerals_needed = match mineral_obj {
        HIERRO_CRUDO => 14,
        PLATA_CRUDA => 20,
        ORO_CRUDO => 35,
        _ => 14,
    };

    if amount < minerals_needed {
        state.send_console(conn_id, &format!("No tienes suficientes minerales (necesitas {})", minerals_needed), font_index::INFO).await;
        return;
    }

    let ingot = if lingote_idx > 0 { lingote_idx } else {
        match mineral_obj {
            HIERRO_CRUDO => LINGOTE_HIERRO,
            PLATA_CRUDA => LINGOTE_PLATA,
            ORO_CRUDO => LINGOTE_ORO,
            _ => LINGOTE_HIERRO,
        }
    };

    // Remove minerals
    if let Some(u) = state.users.get_mut(&conn_id) {
        u.inventory[slot_idx].amount -= minerals_needed;
        if u.inventory[slot_idx].amount <= 0 {
            u.inventory[slot_idx].obj_index = 0;
            u.inventory[slot_idx].amount = 0;
        }
    }
    send_inventory_slot(state, conn_id, slot_idx).await;

    // Add ingot
    let slot = find_or_add_inv_slot(state, conn_id, ingot, 1);
    if let Some(idx) = slot {
        send_inventory_slot(state, conn_id, idx).await;
    }

    state.send_console(conn_id, "Has fundido un lingote", font_index::INFO).await;

    if let Some(u) = state.users.get_mut(&conn_id) {
        try_level_skill(u, 14); // Mining skill
        grant_crafting_rep(u);
    }
}

/// VB6 13.3: FundirArmas (Trabajo.bas:298-321, 921-985) — melt weapons back to ingots.
/// Returns 10-25% of the original crafting materials (LingH/LingP/LingO).
/// Requires: Blacksmith Hammer equipped, weapon in target slot, sufficient Herrería skill.
pub(super) async fn do_fundir_arma(state: &mut GameState, conn_id: ConnectionId, inv_slot: usize) {
    if inv_slot == 0 || inv_slot > MAX_INVENTORY_SLOTS { return; }
    let slot_idx = inv_slot - 1;

    // Check weapon equipped is Blacksmith Hammer
    let has_hammer = match state.users.get(&conn_id) {
        Some(u) => {
            if u.equip.weapon == 0 || u.equip.weapon > MAX_INVENTORY_SLOTS { false }
            else {
                let w_idx = u.inventory[u.equip.weapon - 1].obj_index;
                w_idx == MARTILLO_HERRERO
            }
        }
        None => return,
    };
    if !has_hammer {
        state.send_console(conn_id, "Necesitas equipar un martillo de herrero.", font_index::INFO).await;
        return;
    }

    // Get target item data
    let item_data = match state.users.get(&conn_id) {
        Some(u) => {
            let item = &u.inventory[slot_idx];
            if item.obj_index <= 0 || item.amount <= 0 { None }
            else {
                state.get_object(item.obj_index).map(|o| {
                    (o.obj_type, o.ling_h, o.ling_p, o.ling_o, o.sk_herreria, item.equipped, o.name.clone())
                })
            }
        }
        None => return,
    };

    let (obj_type, ling_h, ling_p, ling_o, sk_needed, is_equipped, item_name) = match item_data {
        Some(d) => d,
        None => {
            state.send_console(conn_id, "No hay ningún objeto en ese slot.", font_index::INFO).await;
            return;
        }
    };

    // Must be a weapon
    if obj_type != ObjType::Weapon {
        state.send_console(conn_id, "Solo se pueden fundir armas.", font_index::INFO).await;
        return;
    }

    // Must have some crafting materials defined
    if ling_h == 0 && ling_p == 0 && ling_o == 0 {
        state.send_console(conn_id, "Este arma no se puede fundir.", font_index::INFO).await;
        return;
    }

    // Check Herrería skill
    let user_skill = state.users.get(&conn_id).map(|u| u.skills[15]).unwrap_or(0); // SK16 = Herreria
    if user_skill < sk_needed {
        state.send_console(conn_id, &format!("Necesitas {} de herrería para fundir esto.", sk_needed), font_index::INFO).await;
        return;
    }

    // Random yield: 10-25%
    let pct = rand_range(10, 25);

    // Calculate returned lingots
    let ret_h = ((ling_h as f64 * pct as f64) * 0.01) as i32;
    let ret_p = ((ling_p as f64 * pct as f64) * 0.01) as i32;
    let ret_o = ((ling_o as f64 * pct as f64) * 0.01) as i32;

    // Unequip if equipped
    if is_equipped {
        if let Some(u) = state.users.get_mut(&conn_id) {
            if u.equip.weapon as usize == inv_slot { u.equip.weapon = 0; }
        }
    }

    // Remove 1 weapon from slot
    if let Some(u) = state.users.get_mut(&conn_id) {
        u.inventory[slot_idx].amount -= 1;
        if u.inventory[slot_idx].amount <= 0 {
            u.inventory[slot_idx].obj_index = 0;
            u.inventory[slot_idx].amount = 0;
        }
    }
    send_inventory_slot(state, conn_id, slot_idx).await;

    // Add returned lingots
    if ret_h > 0 {
        if let Some(idx) = find_or_add_inv_slot(state, conn_id, LINGOTE_HIERRO, ret_h) {
            send_inventory_slot(state, conn_id, idx).await;
        }
    }
    if ret_p > 0 {
        if let Some(idx) = find_or_add_inv_slot(state, conn_id, LINGOTE_PLATA, ret_p) {
            send_inventory_slot(state, conn_id, idx).await;
        }
    }
    if ret_o > 0 {
        if let Some(idx) = find_or_add_inv_slot(state, conn_id, LINGOTE_ORO, ret_o) {
            send_inventory_slot(state, conn_id, idx).await;
        }
    }

    // Play sound + message
    let snd = binary_packets::write_play_wave(SND_HERRERO as u8, 0, 0);
    state.send_bytes(conn_id, &snd).await;
    state.send_console(conn_id, &format!("Has fundido {} y obtenido el {}% de los lingotes.", item_name, pct), font_index::INFO).await;

    // Skill gain + reputation
    if let Some(u) = state.users.get_mut(&conn_id) {
        try_level_skill(u, 15); // Herreria
        grant_crafting_rep(u);
    }
}

/// VB6 13.3: DoUpgrade (Trabajo.bas:987-1116) — upgrade an item to its improved version.
/// Uses the Upgrade field in ObjData to find the target item.
/// Requires materials (difference between upgraded and original item, scaled).
/// Two paths: Herrería (hammer) for weapons/shields/helmets/armor, Carpintería (saw) for arrows/bows/boats.
pub(super) async fn do_upgrade(state: &mut GameState, conn_id: ConnectionId, inv_slot: usize) {
    if inv_slot == 0 || inv_slot > MAX_INVENTORY_SLOTS { return; }
    let slot_idx = inv_slot - 1;

    // Get item and its upgrade target
    let item_data = match state.users.get(&conn_id) {
        Some(u) => {
            let item = &u.inventory[slot_idx];
            if item.obj_index <= 0 || item.amount <= 0 { None }
            else {
                state.get_object(item.obj_index).map(|o| {
                    (item.obj_index, o.upgrade, o.obj_type, o.ling_h, o.ling_p, o.ling_o,
                     o.madera, o.piedras, o.name.clone(), item.equipped)
                })
            }
        }
        None => return,
    };

    let (item_idx, upgrade_idx, obj_type, cur_ling_h, cur_ling_p, cur_ling_o,
         cur_madera, _cur_piedras, item_name, is_equipped) = match item_data {
        Some(d) => d,
        None => {
            state.send_console(conn_id, "No hay ningún objeto en ese slot.", font_index::INFO).await;
            return;
        }
    };

    if upgrade_idx <= 0 {
        state.send_console(conn_id, "Este objeto no se puede mejorar.", font_index::INFO).await;
        return;
    }

    // Get upgrade target data
    let upgrade_data = match state.get_object(upgrade_idx) {
        Some(o) => (o.ling_h, o.ling_p, o.ling_o, o.madera, o.piedras,
                    o.sk_herreria, o.sk_carpinteria, o.name.clone()),
        None => {
            state.send_console(conn_id, "Error: objeto mejorado no existe.", font_index::INFO).await;
            return;
        }
    };
    let (up_ling_h, up_ling_p, up_ling_o, up_madera, up_piedras,
         up_sk_herreria, up_sk_carpinteria, upgrade_name) = upgrade_data;

    // Determine path: Herrería or Carpintería
    let weapon_idx = state.users.get(&conn_id)
        .and_then(|u| {
            if u.equip.weapon > 0 && u.equip.weapon <= MAX_INVENTORY_SLOTS {
                Some(u.inventory[u.equip.weapon - 1].obj_index)
            } else { None }
        })
        .unwrap_or(0);

    let is_smith = weapon_idx == MARTILLO_HERRERO;
    let is_carp = weapon_idx == SERRUCHO_CARPINTERO;

    if !is_smith && !is_carp {
        state.send_console(conn_id, "Necesitas equipar un martillo de herrero o un serrucho.", font_index::INFO).await;
        return;
    }

    // VB6: PORCENTAJE_MATERIALES_UPGRADE — materials needed = upgrade mats - (current mats * 0.5)
    let pct = 0.5f64;

    if is_smith {
        // Herrería path: weapons, shields, helmets, armor
        let user_skill = state.users.get(&conn_id).map(|u| u.skills[15]).unwrap_or(0);
        if user_skill < up_sk_herreria {
            state.send_console(conn_id, &format!("Necesitas {} de herrería.", up_sk_herreria), font_index::INFO).await;
            return;
        }

        let need_h = (up_ling_h as f64 - cur_ling_h as f64 * pct).max(0.0) as i32;
        let need_p = (up_ling_p as f64 - cur_ling_p as f64 * pct).max(0.0) as i32;
        let need_o = (up_ling_o as f64 - cur_ling_o as f64 * pct).max(0.0) as i32;

        // Check materials
        if !has_items(state, conn_id, LINGOTE_HIERRO, need_h)
            || !has_items(state, conn_id, LINGOTE_PLATA, need_p)
            || !has_items(state, conn_id, LINGOTE_ORO, need_o) {
            state.send_console(conn_id, "No tienes suficientes lingotes para la mejora.", font_index::INFO).await;
            return;
        }

        // Remove materials
        remove_items(state, conn_id, LINGOTE_HIERRO, need_h).await;
        remove_items(state, conn_id, LINGOTE_PLATA, need_p).await;
        remove_items(state, conn_id, LINGOTE_ORO, need_o).await;

        let snd = binary_packets::write_play_wave(SND_HERRERO as u8, 0, 0);
        state.send_bytes(conn_id, &snd).await;

        if let Some(u) = state.users.get_mut(&conn_id) {
            try_level_skill(u, 15); // Herreria
        }
    } else {
        // Carpintería path: arrows, weapons (wood), boats
        let user_skill = state.users.get(&conn_id).map(|u| u.skills[14]).unwrap_or(0);
        if user_skill < up_sk_carpinteria {
            state.send_console(conn_id, &format!("Necesitas {} de carpintería.", up_sk_carpinteria), font_index::INFO).await;
            return;
        }

        let need_wood = (up_madera as f64 - cur_madera as f64 * pct).max(0.0) as i32;
        let need_stones = (up_piedras as f64).max(0.0) as i32;

        if !has_items(state, conn_id, LENA_OBJ, need_wood) {
            state.send_console(conn_id, "No tienes suficiente madera para la mejora.", font_index::INFO).await;
            return;
        }
        if need_stones > 0 && !has_items(state, conn_id, PIEDRA_OBJ, need_stones) {
            state.send_console(conn_id, "No tienes suficientes piedras para la mejora.", font_index::INFO).await;
            return;
        }

        remove_items(state, conn_id, LENA_OBJ, need_wood).await;
        if need_stones > 0 {
            remove_items(state, conn_id, PIEDRA_OBJ, need_stones).await;
        }

        let snd = binary_packets::write_play_wave(SND_CARPINTERO as u8, 0, 0);
        state.send_bytes(conn_id, &snd).await;

        if let Some(u) = state.users.get_mut(&conn_id) {
            try_level_skill(u, 14); // Carpinteria
        }
    }

    // Unequip if equipped
    if is_equipped {
        if let Some(u) = state.users.get_mut(&conn_id) {
            match obj_type {
                ObjType::Weapon => { if u.equip.weapon as usize == inv_slot { u.equip.weapon = 0; } }
                ObjType::Shield => { if u.equip.shield as usize == inv_slot { u.equip.shield = 0; } }
                ObjType::Helmet => { if u.equip.helmet as usize == inv_slot { u.equip.helmet = 0; } }
                ObjType::Armor => { if u.equip.armor as usize == inv_slot { u.equip.armor = 0; } }
                _ => {}
            }
        }
    }

    // Replace item with upgraded version
    if let Some(u) = state.users.get_mut(&conn_id) {
        u.inventory[slot_idx].obj_index = upgrade_idx;
        // Keep amount = 1, unequipped
        u.inventory[slot_idx].amount = 1;
        u.inventory[slot_idx].equipped = false;
    }
    send_inventory_slot(state, conn_id, slot_idx).await;

    // Stamina cost
    let is_worker = state.users.get(&conn_id)
        .map(|u| u.class.is_recolector())
        .unwrap_or(false);
    let sta_cost = if is_worker { 2 } else { 6 };
    if let Some(u) = state.users.get_mut(&conn_id) {
        u.min_sta = (u.min_sta - sta_cost).max(0);
    }

    state.send_console(conn_id, &format!("Has mejorado {} a {}.", item_name, upgrade_name), font_index::INFO).await;
    send_stats_sta(state, conn_id).await;

    // Crafting reputation
    if let Some(u) = state.users.get_mut(&conn_id) {
        grant_crafting_rep(u);
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
                    u.inventory[i].amount -= take;
                    amount -= take;
                    if u.inventory[i].amount <= 0 {
                        u.inventory[i].obj_index = 0;
                        u.inventory[i].amount = 0;
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

/// VB6 13.3: DoRobar — Stealing from other players.
/// Requires 15 stamina. Level-based gold theft for Thief/Bandit with gloves.
/// 50% chance item theft (Thief only), 50% gold theft.
pub(super) async fn do_robar(state: &mut GameState, conn_id: ConnectionId, tx: i32, ty: i32) {
    let (map, ux, uy, skill, class, level) = match state.users.get(&conn_id) {
        Some(u) => (u.pos_map, u.pos_x, u.pos_y, u.skills[2], u.class, u.level),
        None => return,
    };

    // Distance check
    if (tx - ux).abs() > 2 || (ty - uy).abs() > 2 {
        return;
    }

    // VB6: 15 stamina cost
    if let Some(u) = state.users.get(&conn_id) {
        if u.min_sta < 15 {
            state.send_msg_id(conn_id, 17, "").await;
            return;
        }
    }
    if let Some(u) = state.users.get_mut(&conn_id) {
        u.min_sta -= 15;
    }
    send_stats_sta(state, conn_id).await;

    // Find target user on tile
    let target_conn = state.world.grid(map)
        .and_then(|g| g.tile(tx, ty))
        .and_then(|t| t.user_conn);

    let target_conn = match target_conn {
        Some(id) if id != conn_id => id,
        _ => {
            state.send_msg_id(conn_id, 252, "").await;
            return;
        }
    };

    // VB6: Luck roll using lookup table (not polynomial)
    let suerte = luck_denominator_lookup(skill);
    let roll = random_number(1, suerte);

    if roll < 3 {
        // VB6: 50% chance item theft (Thief class only), 50% gold theft
        let is_ladron = class == PlayerClass::Ladron;
        let steal_item = is_ladron && random_number(1, 2) == 1;

        if steal_item {
            // VB6: RobarObjeto — steal 5-10% of a random item stack
            let mut stolen_something = false;
            if let Some(victim) = state.users.get(&target_conn) {
                // Find a stealable item in victim's inventory
                let mut stealable_slots: Vec<usize> = Vec::new();
                for (i, slot) in victim.inventory.iter().enumerate() {
                    if slot.obj_index > 0 && slot.amount > 0 && !slot.equipped {
                        // Check item isn't newbie/key/special
                        let ok = state.get_object(slot.obj_index)
                            .map(|o| !o.newbie && o.obj_type != ObjType::Key)
                            .unwrap_or(false);
                        if ok {
                            stealable_slots.push(i);
                        }
                    }
                }
                if !stealable_slots.is_empty() {
                    let idx = stealable_slots[random_number(0, stealable_slots.len() as i32 - 1) as usize];
                    let (obj_idx, amount) = (victim.inventory[idx].obj_index, victim.inventory[idx].amount);
                    // VB6: 5-10% of stack, min 1
                    let pct = random_number(5, 10) as f64 / 100.0;
                    let steal_amount = ((amount as f64 * pct).floor() as i32).max(1);

                    // Take from victim
                    if let Some(v) = state.users.get_mut(&target_conn) {
                        v.inventory[idx].amount -= steal_amount;
                        if v.inventory[idx].amount <= 0 {
                            v.inventory[idx] = InventorySlot::default();
                        }
                    }
                    send_inventory_slot(state, target_conn, idx).await;

                    // Give to thief
                    let slot = find_or_add_inv_slot(state, conn_id, obj_idx, steal_amount);
                    if let Some(s) = slot {
                        send_inventory_slot(state, conn_id, s).await;
                    }

                    let obj_name = state.get_object(obj_idx).map(|o| o.name.clone()).unwrap_or_default();
                    let victim_name = state.users.get(&target_conn).map(|u| u.char_name.clone()).unwrap_or_default();
                    state.send_console(conn_id, &format!("Has robado {} {} a {}!", steal_amount, obj_name, victim_name), font_index::FIGHT).await;
                    stolen_something = true;
                }
            }
            if !stolen_something {
                state.send_msg_id(conn_id, 818, "").await;
            }
        } else {
            // VB6: Gold theft — amount based on level and class
            let has_gloves = state.users.get(&conn_id)
                .map(|u| {
                    if u.equip.ring > 0 && u.equip.ring <= MAX_INVENTORY_SLOTS {
                        u.inventory[u.equip.ring - 1].obj_index == 873 // GUANTE_HURTO
                    } else {
                        false
                    }
                })
                .unwrap_or(false);

            let gold_amount = if is_ladron && has_gloves {
                random_number(level * 50, level * 100) as i64
            } else if is_ladron {
                random_number(level * 25, level * 50) as i64
            } else {
                random_number(1, 100) as i64
            };

            let victim_gold = state.users.get(&target_conn).map(|u| u.gold).unwrap_or(0);
            let stolen = gold_amount.min(victim_gold);

            if stolen > 0 {
                if let Some(victim) = state.users.get_mut(&target_conn) {
                    victim.gold -= stolen;
                }
                if let Some(thief) = state.users.get_mut(&conn_id) {
                    thief.gold = (thief.gold + stolen).min(MAX_GOLD);
                }

                let victim_name = state.users.get(&target_conn).map(|u| u.char_name.clone()).unwrap_or_default();
                state.send_msg_id(conn_id, 816, &format!("{}@{}", stolen, victim_name)).await;

                send_stats_gold(state, conn_id).await;
                send_stats_gold(state, target_conn).await;
            } else {
                state.send_msg_id(conn_id, 818, "").await;
            }
        }

        // VB6: Notify victim on success
        let thief_name = state.users.get(&conn_id).map(|u| u.char_name.clone()).unwrap_or_default();
        state.send_msg_id(target_conn, 819, &thief_name).await;

        // VB6: SubirSkill on success
        if let Some(u) = state.users.get_mut(&conn_id) {
            try_level_skill_with_hit(u, 2, true); // Robar = index 2
        }
    } else {
        // Fail — notify victim
        let thief_name = state.users.get(&conn_id).map(|u| u.char_name.clone()).unwrap_or_default();
        state.send_msg_id(conn_id, 818, "").await;
        state.send_console(target_conn, &format!("{} ha intentado robarte!", thief_name), font_index::FIGHT).await;

        if let Some(u) = state.users.get_mut(&conn_id) {
            try_level_skill_with_hit(u, 2, false);
        }
    }
}

/// Ranged attack (bow + arrows).
/// VB6: TCP_HandleData1.bas case Proyectiles + SistemaCombate.bas UsuarioAtacaUsuario/Npc.
/// Requires bow equipped (proyectil=1) and arrows. Consumes 1 arrow per shot.
/// Uses Proyectiles skill (19) + mod_poder_ataque_proyectiles + mod_dano_clase_proyectiles.
const MAXDISTANCIAARCO: i32 = 18;

pub(super) async fn do_ranged_attack(state: &mut GameState, conn_id: ConnectionId, tx: i32, ty: i32) {
    // Anti-cheat: check arrow cooldown
    if !puede_flechear(state, conn_id) {
        return;
    }

    // Get attacker data
    let user_data = match state.users.get(&conn_id) {
        Some(u) if u.logged && !u.dead => {
            (u.pos_map, u.pos_x, u.pos_y, u.char_index,
             u.equip.weapon, u.equip.municion, u.min_sta,
             u.safe_toggle, u.target_user, u.target_npc_idx)
        }
        _ => return,
    };
    let (map, ux, uy, char_index, weapon_slot, municion_slot, sta,
         safe_toggle, target_user, target_npc_idx) = user_data;

    // VB6: MAXDISTANCIAARCO range check
    let dist = ((ux - tx).abs()).max((uy - ty).abs());
    if dist > MAXDISTANCIAARCO {
        state.send_console(conn_id, "Estás demasiado lejos para disparar.", font_index::INFO).await;
        return;
    }

    // Check weapon is a bow (proyectil=1)
    let weapon_obj_idx = if weapon_slot > 0 && weapon_slot <= MAX_INVENTORY_SLOTS {
        state.users.get(&conn_id).map(|u| u.inventory[weapon_slot - 1].obj_index).unwrap_or(0)
    } else {
        0
    };

    let is_bow = weapon_obj_idx > 0 && state.get_object(weapon_obj_idx)
        .map(|o| o.proyectil)
        .unwrap_or(false);

    if !is_bow {
        // Not a ranged weapon
        return;
    }

    // VB6: Check if weapon requires ammo (Municion=1) or is a throwing weapon (Municion=0)
    let weapon_needs_ammo = state.get_object(weapon_obj_idx)
        .map(|o| o.municion > 0)
        .unwrap_or(false);

    // Determine the projectile source: ammo slot (bow+arrow) or weapon slot (throwing weapon)
    let (consume_slot, projectile_obj_idx, projectile_amount);

    if weapon_needs_ammo {
        // Bow + arrows: consume from ammo slot
        let municion_obj_idx = if municion_slot > 0 && municion_slot <= MAX_INVENTORY_SLOTS {
            state.users.get(&conn_id).map(|u| u.inventory[municion_slot - 1].obj_index).unwrap_or(0)
        } else {
            0
        };
        let municion_amount = if municion_slot > 0 && municion_slot <= MAX_INVENTORY_SLOTS {
            state.users.get(&conn_id).map(|u| u.inventory[municion_slot - 1].amount).unwrap_or(0)
        } else {
            0
        };
        let is_arrow = municion_obj_idx > 0 && state.get_object(municion_obj_idx)
            .map(|o| o.obj_type == crate::data::objects::ObjType::Arrow)
            .unwrap_or(false);
        if !is_arrow || municion_amount < 1 {
            state.send_console(conn_id, "No tienes municiones equipadas.", font_index::INFO).await;
            if let Some(user) = state.users.get_mut(&conn_id) {
                user.equip.municion = 0;
            }
            return;
        }
        consume_slot = municion_slot;
        projectile_obj_idx = municion_obj_idx;
        projectile_amount = municion_amount;
    } else {
        // Throwing weapon: consume the weapon itself
        let wp_amount = if weapon_slot > 0 && weapon_slot <= MAX_INVENTORY_SLOTS {
            state.users.get(&conn_id).map(|u| u.inventory[weapon_slot - 1].amount).unwrap_or(0)
        } else {
            0
        };
        if wp_amount < 1 {
            state.send_console(conn_id, "No tienes municiones.", font_index::INFO).await;
            return;
        }
        consume_slot = weapon_slot;
        projectile_obj_idx = weapon_obj_idx;
        projectile_amount = wp_amount;
    }

    // Get projectile properties (damage + poison flag)
    let (arrow_min_hit, arrow_max_hit, arrow_envenena) = state.get_object(projectile_obj_idx)
        .map(|o| (o.min_hit, o.max_hit, o.envenena))
        .unwrap_or((0, 0, false));

    // Stamina cost (VB6: min 10 required, 1-10 consumed)
    if sta < 10 {
        state.send_msg_id(conn_id, 17, "").await;
        return;
    }
    let sta_cost = random_number(1, 10);
    if let Some(user) = state.users.get_mut(&conn_id) {
        user.min_sta = (user.min_sta - sta_cost).max(0);
    }
    send_stats_sta(state, conn_id).await;

    // Consume 1 projectile
    if let Some(user) = state.users.get_mut(&conn_id) {
        if consume_slot > 0 && consume_slot <= MAX_INVENTORY_SLOTS {
            user.inventory[consume_slot - 1].amount -= 1;
            if user.inventory[consume_slot - 1].amount <= 0 {
                user.inventory[consume_slot - 1] = InventorySlot::default();
                if weapon_needs_ammo {
                    user.equip.municion = 0;
                } else {
                    user.equip.weapon = 0;
                    user.weapon_anim = super::common::NINGUN_ARMA;
                }
            }
        }
    }
    // Update inventory slot
    send_inventory_slot(state, conn_id, consume_slot).await;

    // Get projectile GRH for visual
    let arrow_grh = state.get_object(projectile_obj_idx).map(|o| o.grh_index).unwrap_or(0);

    // Find target (NPC or user on clicked tile)
    let target_npc = state.world.grid(map)
        .and_then(|g| g.tile(tx, ty))
        .and_then(|t| if t.npc_index > 0 { Some(t.npc_index as usize) } else { None });

    let target_user_tile = state.world.grid(map)
        .and_then(|g| g.tile(tx, ty))
        .and_then(|t| t.user_conn);

    if let Some(npc_idx) = target_npc {
        // Ranged attack vs NPC
        let npc_data = state.get_npc(npc_idx)
            .map(|n| (n.char_index.0, n.attackable));

        if let Some((npc_char, true)) = npc_data {
            // Send arrow visual
            let flechi = binary_packets::write_arrow(char_index.0 as i16, npc_char as i16, arrow_grh as i16);
            state.send_data_bytes(SendTarget::ToMap(map), &flechi).await;

            // Store target for combat resolution
            if let Some(user) = state.users.get_mut(&conn_id) {
                user.target_npc_idx = npc_idx;
            }
            resolve_ranged_attack_npc(state, conn_id, npc_idx, arrow_min_hit, arrow_max_hit).await;
        }
    } else if let Some(target) = target_user_tile {
        if target != conn_id {
            // Ranged attack vs user
            let target_char = state.users.get(&target).map(|u| u.char_index.0).unwrap_or(0);

            // Send arrow visual
            let flechi = binary_packets::write_arrow(char_index.0 as i16, target_char as i16, arrow_grh as i16);
            state.send_data_bytes(SendTarget::ToMap(map), &flechi).await;

            // Store target and resolve
            if let Some(user) = state.users.get_mut(&conn_id) {
                user.target_user = target;
            }
            resolve_ranged_attack_user(state, conn_id, target, arrow_min_hit, arrow_max_hit, arrow_envenena).await;
        }
    }
}

/// Resolve ranged attack against NPC.
/// VB6: SistemaCombate.bas UsuarioAtacaNpc — uses Proyectiles skill + projectile class modifiers.
/// arrow_min/max add to bow weapon damage (VB6: DañoArma += RandomNumber(Ammo.MinHIT, Ammo.MaxHIT)).
async fn resolve_ranged_attack_npc(
    state: &mut GameState, conn_id: ConnectionId, npc_idx: usize,
    arrow_min_hit: i32, arrow_max_hit: i32,
) {
    let user_data = match state.users.get(&conn_id) {
        Some(u) if u.logged && !u.dead => {
            (u.pos_map, u.pos_x, u.pos_y, u.char_index,
             u.level, u.attributes[0], u.attributes[1],
             u.min_hit, u.max_hit, u.skills[19], u.char_name.clone(), u.class)
        }
        _ => return,
    };
    let (map, x, y, char_index, level, strength, agility,
         min_hit, max_hit, skill_proyectiles, attacker_name, class) = user_data;

    let npc_data = match state.get_npc(npc_idx) {
        Some(n) if n.active && n.attackable => {
            (n.char_index, n.def, n.poder_evasion, n.min_hp,
             n.max_hp, n.give_exp, n.npc_number, n.name.clone())
        }
        _ => return,
    };
    let (npc_char, npc_def, npc_evasion, _npc_hp, _npc_max_hp,
         npc_exp, npc_number, _npc_name) = npc_data;

    // Hit check — VB6: PoderAtaqueProyectil uses Proyectiles skill + ModClase.AtaqueProyectiles
    let atk_mod = state.game_data.balance.class_mod_ataque_proyectiles_e(class);
    let attack_power = calc_attack_power_with_balance(skill_proyectiles, agility, level, atk_mod);
    let hit_prob = ((50.0 + (attack_power - npc_evasion as f64) * 0.4) as i32).clamp(10, 90);

    if rand_range(1, 100) > hit_prob {
        let pkt = binary_packets::write_multi_msg_simple(MultiMessageID::UserSwing);
        state.send_bytes(conn_id, &pkt).await;
        state.send_chat_over_head_to(SendTarget::ToArea { map, x, y }, "\u{00A1}Fallo!", npc_char.0 as i16, 255).await;
        // VB6: Level Proyectiles skill even on miss
        if let Some(u) = state.users.get_mut(&conn_id) {
            try_level_skill(u, 19);
        }
        return;
    }

    // VB6 CalcularDaño: DañoArma = Rand(Bow.MinHIT, Bow.MaxHIT) + Rand(Arrow.MinHIT, Arrow.MaxHIT)
    let bow_dmg = rand_range(min_hit.max(1), max_hit.max(1));
    let ammo_dmg = if arrow_max_hit > 0 { rand_range(arrow_min_hit.max(0), arrow_max_hit.max(1)) } else { 0 };
    let weapon_dmg = bow_dmg + ammo_dmg;

    // VB6: StrBonus = (MaxHIT/5) * max(0, Strength-15), UserDmg = Rand(MinHIT, MaxHIT)
    let total_max_hit = max_hit + arrow_max_hit;
    let str_bonus = ((total_max_hit as f64 / 5.0) * (strength - 15).max(0) as f64) as i32;
    let user_dmg = rand_range(min_hit, max_hit);
    let base_dmg = 3 * weapon_dmg + str_bonus + user_dmg;

    // VB6: ModClase.DañoProyectiles (not DañoArmas)
    let dmg_mod = state.game_data.balance.class_mod_dano_proyectiles_e(class) as f64;
    let mut damage = (base_dmg as f64 * dmg_mod) as i32;
    damage = (damage - npc_def).max(1);

    // Apply damage
    let new_hp = if let Some(npc) = state.get_npc_mut(npc_idx) {
        npc.min_hp -= damage;
        npc.min_hp
    } else {
        return;
    };

    let u2_pkt = binary_packets::write_multi_user_hit_npc(damage as i32);
    state.send_bytes(conn_id, &u2_pkt).await;
    state.send_chat_over_head_to(SendTarget::ToArea { map, x, y }, &format!("-{}", damage), npc_char.0 as i16, 65535).await;

    // Level Proyectiles skill on hit
    if let Some(u) = state.users.get_mut(&conn_id) {
        try_level_skill(u, 19);
    }

    if new_hp <= 0 {
        if let Some(u) = state.users.get_mut(&conn_id) {
            u.exp += npc_exp as i64;
        }
        send_stats_exp(state, conn_id).await;
        check_user_level(state, conn_id).await;

        let npc_ci = state.get_npc(npc_idx).map(|n| n.char_index.0).unwrap_or(0);
        let bp_pkt = binary_packets::write_character_remove(npc_ci as i16);
        state.send_data_bytes(SendTarget::ToArea { map, x, y }, &bp_pkt).await;

        state.kill_npc(npc_idx);
    }
}

/// Resolve ranged attack against user — VB6: SistemaCombate.bas UsuarioAtacaUsuario (ranged path).
/// Uses Proyectiles skill + projectile class modifiers + arrow poison application.
async fn resolve_ranged_attack_user(
    state: &mut GameState, conn_id: ConnectionId, victim_id: ConnectionId,
    arrow_min_hit: i32, arrow_max_hit: i32, arrow_envenena: bool,
) {
    let att_data = match state.users.get(&conn_id) {
        Some(u) if u.logged && !u.dead => {
            (u.pos_map, u.pos_x, u.pos_y, u.char_index,
             u.level, u.attributes[0], u.attributes[1],
             u.min_hit, u.max_hit, u.skills[19], u.char_name.clone(), u.class,
             u.safe_toggle)
        }
        _ => return,
    };
    let (map, x, y, _char_index, level, strength, agility,
         min_hit, max_hit, skill_proyectiles, attacker_name, class, safe_on) = att_data;

    if safe_on {
        state.send_msg_id(conn_id, 207, "").await;
        return;
    }

    let victim_data = match state.users.get(&victim_id) {
        Some(v) if v.logged && !v.dead => {
            (v.level, v.attributes[1], v.skills[3], v.char_name.clone(), v.privileges, v.char_index)
        }
        _ => return,
    };
    let (v_level, v_agility, v_tacticas, victim_name, v_privs, v_char_index) = victim_data;

    if v_privs > 0 { return; }

    // Hit check — VB6: PoderAtaqueProyectil + ModClase.AtaqueProyectiles
    let atk_mod = state.game_data.balance.class_mod_ataque_proyectiles_e(class);
    let attack_power = calc_attack_power_with_balance(skill_proyectiles, agility, level, atk_mod);
    let defense_power = calc_defense_power(v_tacticas, v_agility, v_level);
    let hit_prob = ((50.0 + (attack_power - defense_power) * 0.4) as i32).clamp(10, 90);

    if rand_range(1, 100) > hit_prob {
        let pkt = binary_packets::write_multi_user_attacked_swing(_char_index.0 as i16);
        state.send_bytes(victim_id, &pkt).await;
        let pkt = binary_packets::write_multi_msg_simple(MultiMessageID::UserSwing);
        state.send_bytes(conn_id, &pkt).await;
        state.send_chat_over_head_to(SendTarget::ToArea { map, x, y }, "\u{00A1}Fallo!", v_char_index.0 as i16, 255).await;
        // VB6: Level Proyectiles skill even on miss
        if let Some(u) = state.users.get_mut(&conn_id) {
            try_level_skill(u, 19);
        }
        return;
    }

    // VB6 CalcularDaño: DañoArma = Rand(Bow.MinHIT, Bow.MaxHIT) + Rand(Arrow.MinHIT, Arrow.MaxHIT)
    let bow_dmg = rand_range(min_hit.max(1), max_hit.max(1));
    let ammo_dmg = if arrow_max_hit > 0 { rand_range(arrow_min_hit.max(0), arrow_max_hit.max(1)) } else { 0 };
    let weapon_dmg = bow_dmg + ammo_dmg;

    // VB6: StrBonus uses combined max, UserDmg from user stats
    let total_max_hit = max_hit + arrow_max_hit;
    let str_bonus = ((total_max_hit as f64 / 5.0) * (strength - 15).max(0) as f64) as i32;
    let user_dmg = rand_range(min_hit, max_hit);
    let base_dmg = 3 * weapon_dmg + str_bonus + user_dmg;

    // VB6: ModClase.DañoProyectiles
    let dmg_mod = state.game_data.balance.class_mod_dano_proyectiles_e(class) as f64;
    let mut damage = (base_dmg as f64 * dmg_mod) as i32;

    // Body part hit (1=head, 2-6=body)
    let body_part = rand_range(1, 6);

    // Armor absorption
    let absorption = calc_armor_absorption(state, victim_id, body_part);
    damage -= absorption;

    // VB6 13.3: No generic crit in ranged PvP — DoGolpeCritico is Bandido+EspadaVikinga only (melee)
    let damage = damage.max(1);

    // Apply damage
    if let Some(victim) = state.users.get_mut(&victim_id) {
        victim.min_hp -= damage;
    }
    let n4_pkt = binary_packets::write_multi_user_hitted_by_user(_char_index.0 as i16, body_part as u8, damage as i16);
    state.send_bytes(victim_id, &n4_pkt).await;
    let n5_pkt = binary_packets::write_multi_user_hitted_user(v_char_index.0 as i16, body_part as u8, damage as i16);
    state.send_bytes(conn_id, &n5_pkt).await;

    state.send_chat_over_head_to(SendTarget::ToArea { map, x, y }, &format!("-{}", damage), v_char_index.0 as i16, 65535).await;

    // VB6: Arrow poison (60% chance if ammo has Envenena=1) — SistemaCombate.bas UserEnvenena
    if arrow_envenena && rand_range(1, 100) <= 60 {
        let already_poisoned = state.users.get(&victim_id).map(|u| u.poisoned).unwrap_or(true);
        if !already_poisoned {
            if let Some(victim) = state.users.get_mut(&victim_id) {
                victim.poisoned = true;
                victim.counter_poison = 0;
            }
            state.send_msg_id(victim_id, 171, &attacker_name).await;
            state.send_msg_id(conn_id, 172, &victim_name).await;
        }
    }

    // Level Proyectiles skill on hit
    if let Some(u) = state.users.get_mut(&conn_id) {
        try_level_skill(u, 19);
    }

    send_stats_hp(state, victim_id).await;

    // Check death
    let hp = state.users.get(&victim_id).map(|u| u.min_hp).unwrap_or(0);
    if hp <= 0 {
        user_die(state, victim_id, Some(conn_id)).await;
    }
}

/// DoOcultarse — Hide skill (VB6: Trabajo.bas DoOcultarse).
/// Skill-based success chance. Thieves/Hunters get bonus. Blocked on special maps.
/// TSAO mechanic: using ocultarse while invisible (spell) breaks the spell invisibility,
/// then attempts the normal hide check. This is the "spam O to lose invi" mechanic.
pub(super) async fn do_ocultarse(state: &mut GameState, conn_id: ConnectionId) {
    let (map, x, y, skill, class, char_index, already_hidden, is_invisible, navigating) =
        match state.users.get(&conn_id) {
            Some(u) if u.logged && !u.dead => {
                (u.pos_map, u.pos_x, u.pos_y, u.skills.get(7).copied().unwrap_or(0),
                 u.class, u.char_index, u.hidden, u.invisible && !u.admin_invisible,
                 u.navigating)
            }
            _ => return,
        };

    // Blocked on special maps (VB6: OcultarSinEfecto)
    let ocultar_blocked = state.game_data.maps.get(map as usize)
        .and_then(|m| m.as_ref())
        .map(|m| m.info.ocultar_sin_efecto)
        .unwrap_or(false);
    if ocultar_blocked {
        state.send_msg_id(conn_id, 838, "").await;
        return;
    }

    // VB6: Can't hide while navigating (except pirate)
    if navigating && class != PlayerClass::Pirata {
        state.send_console(conn_id, "No puedes ocultarte navegando.", font_index::INFO).await;
        return;
    }

    // TSAO mechanic: Using ocultarse while invisible (spell) breaks the spell invisibility.
    // Then the normal hide check runs — success = hidden via skill, failure = fully visible.
    if is_invisible {
        if let Some(user) = state.users.get_mut(&conn_id) {
            user.invisible = false;
            user.hidden = false;
            user.counter_invisible = 0;
            user.counter_oculto = 0;
        }
        // Broadcast visibility restoration to area
        let cc = state.users.get(&conn_id).unwrap().build_cc_binary();
        state.send_data_bytes(SendTarget::ToArea { map, x, y }, &cc).await;
        let cd = super::common::build_cd_binary(state.users.get(&conn_id).unwrap());
        state.send_data_bytes(SendTarget::ToArea { map, x, y }, &cd).await;
        let nover = binary_packets::write_set_invisible(char_index.0 as i16, false, 0);
        state.send_data_bytes(SendTarget::ToMap(map), &nover).await;
        state.send_bytes(conn_id, &nover).await;
        // Now fall through to the normal hide check below
    } else if already_hidden {
        // Already hidden (not from spell) — "Ya estás oculto."
        state.send_console(conn_id, "Ya estás oculto.", font_index::INFO).await;
        return;
    }

    // VB6 DoOcultarse: Suerte formula (polynomial based on skill level)
    let skill_f = skill as f64;
    let suerte = (((0.000002 * skill_f - 0.0002) * skill_f + 0.0064) * skill_f + 0.1124) * 100.0;
    let res = rand_range(1, 100);

    if (res as f64) <= suerte {
        // Success — calculate duration based on skill (VB6 polynomial)
        let remaining = (100 - skill) as f64;
        let mut duration = (-0.000001 * remaining.powi(3))
            + (0.00009229 * remaining.powi(2))
            + (-0.0088 * remaining)
            + 0.9571;
        duration *= state.config.intervalo_oculto as f64;

        // Bandits hide for half time
        let is_bandit = class == PlayerClass::Bandido;
        let counter = if is_bandit { (duration / 2.0) as i32 } else { duration as i32 };

        if let Some(user) = state.users.get_mut(&conn_id) {
            user.hidden = true;
            user.counter_oculto = counter;
        }
        // Send NOVER to make invisible on all clients (TSAO: clanmates still see us)
        if !navigating {
            let ci = char_index.0 as i16;
            let nover = binary_packets::write_set_invisible(ci, true, 0);
            let bp_remove = binary_packets::write_character_remove(ci);
            let (px, py) = state.users.get(&conn_id).map(|u| (u.pos_x, u.pos_y)).unwrap_or((1, 1));
            let area_users = state.get_area_users(map, px, py, conn_id);
            for other_id in area_users {
                if same_clan(state, conn_id, other_id) {
                    // Clanmate: SetInvisible → they see character semi-transparent
                    state.send_bytes(other_id, &nover).await;
                } else {
                    // Non-clanmate: remove character entirely
                    state.send_bytes(other_id, &bp_remove).await;
                }
            }
            // Tell self we're invisible
            state.send_bytes(conn_id, &nover).await;
        }
        state.send_msg_id(conn_id, 808, "").await; // "Te has ocultado."
        if let Some(u) = state.users.get_mut(&conn_id) {
            try_level_skill_with_hit(u, 7, true); // Ocultarse = index 7
        }
    } else {
        // Failure
        state.send_msg_id(conn_id, 809, "").await; // "No has logrado ocultarte."
        if let Some(u) = state.users.get_mut(&conn_id) {
            try_level_skill_with_hit(u, 7, false);
        }
    }
}

/// DoPermanecerOculto — Timer-based check: returns true if still hidden.
/// VB6: Trabajo.bas DoPermanecerOculto — decrements TiempoOculto each tick.
/// Hunter with skill>90 + special armor stays hidden indefinitely.
pub(super) fn check_permanecer_oculto(user: &mut UserState) -> bool {
    if !user.hidden { return false; }

    // Hunter with skill>90 and specific armor stays hidden indefinitely
    let skill = user.skills.get(7).copied().unwrap_or(0);
    if user.class == PlayerClass::Cazador && skill > 90 {
        let armor_obj = if user.equip.armor >= 1 && user.equip.armor <= user.inventory.len() {
            user.inventory[user.equip.armor - 1].obj_index
        } else { 0 };
        if armor_obj == 648 || armor_obj == 360 {
            return true;
        }
    }

    // Decrement timer
    user.counter_oculto -= 1;
    if user.counter_oculto <= 0 {
        user.counter_oculto = 0;
        user.hidden = false;
        return false; // Revealed — timer expired
    }
    true // Still hidden
}

/// Desarmar — Disarm skill (VB6: Trabajo.bas Desarmar).
/// Chance to unequip victim's weapon based on Wresterling skill.
pub(super) fn try_desarmar(skill: i32) -> bool {
    let suerte = match skill {
        0..=10 => 35,
        11..=20 => 30,
        21..=30 => 28,
        31..=40 => 24,
        41..=50 => 22,
        51..=60 => 20,
        61..=70 => 18,
        71..=80 => 15,
        81..=90 => 10,
        91..=100 => 5,
        _ => 5,
    };
    let res = rand_range(1, suerte);
    res <= 2
}

/// CNS — Construct blacksmith item.
pub(super) async fn handle_construct_smith(state: &mut GameState, conn_id: ConnectionId, data: &str) {
    let payload = strip_opcode(data, 3);
    let obj_index: i32 = match payload.trim().parse() {
        Ok(v) if v >= 1 => v,
        _ => return,
    };

    let obj = match state.get_object(obj_index) {
        Some(o) => o.clone(),
        None => return,
    };

    if obj.sk_herreria <= 0 { return; }

    // VB6: Validate item is in ArmasHerrero or ArmadurasHerrero list
    if !state.game_data.crafting.is_smith_item(obj_index) { return; }

    // Check skill
    let skill = state.users.get(&conn_id).map(|u| u.skills[15]).unwrap_or(0); // Herreria=15 (1-based 16)
    if skill < obj.sk_herreria {
        state.send_console(conn_id, "No tienes suficiente habilidad", font_index::INFO).await;
        return;
    }

    // Check materials (iron, silver, gold ingots)
    let has_materials = check_has_items(state, conn_id, &[
        (LINGOTE_HIERRO, obj.ling_h),
        (LINGOTE_PLATA, obj.ling_p),
        (LINGOTE_ORO, obj.ling_o),
    ]);

    if !has_materials {
        state.send_console(conn_id, "No tienes los materiales necesarios", font_index::INFO).await;
        return;
    }

    // Remove materials
    remove_items_from_inv(state, conn_id, LINGOTE_HIERRO, obj.ling_h).await;
    remove_items_from_inv(state, conn_id, LINGOTE_PLATA, obj.ling_p).await;
    remove_items_from_inv(state, conn_id, LINGOTE_ORO, obj.ling_o).await;

    // Give crafted item
    let slot = find_or_add_inv_slot(state, conn_id, obj_index, 1);
    if let Some(idx) = slot {
        send_inventory_slot(state, conn_id, idx).await;
    }

    // Play sound
    let (map, x, y) = state.users.get(&conn_id).map(|u| (u.pos_map, u.pos_x, u.pos_y)).unwrap_or((0,0,0));
    let snd = binary_packets::write_play_wave(SND_HERRERO as u8, x as u8, y as u8);
    state.send_data_bytes(SendTarget::ToArea { map, x, y }, &snd).await;

    state.send_console(conn_id, &format!("Has construido {}", obj.name), font_index::INFO).await;

    if let Some(u) = state.users.get_mut(&conn_id) {
        try_level_skill(u, 16);
        grant_crafting_rep(u);
    }
}

/// CNC — Construct carpentry item.
pub(super) async fn handle_construct_carp(state: &mut GameState, conn_id: ConnectionId, data: &str) {
    let payload = strip_opcode(data, 3);
    let obj_index: i32 = match payload.trim().parse() {
        Ok(v) if v >= 1 => v,
        _ => return,
    };

    let obj = match state.get_object(obj_index) {
        Some(o) => o.clone(),
        None => return,
    };

    if obj.sk_carpinteria <= 0 { return; }

    // VB6: Validate item is in ObjCarpintero list
    if !state.game_data.crafting.is_carpenter_item(obj_index) { return; }

    // Check equipped carpentry tool (VB6: SERRUCHO_CARPINTERO must be equipped)
    let weapon = state.users.get(&conn_id).map(|u| equipped_weapon_obj(u)).unwrap_or(0);
    if weapon != SERRUCHO_CARPINTERO {
        state.send_console(conn_id, "Necesitas un serrucho de carpintero", font_index::INFO).await;
        return;
    }

    // Check skill
    let skill = state.users.get(&conn_id).map(|u| u.skills[14]).unwrap_or(0); // Carpinteria=14 (1-based 15)
    if skill < obj.sk_carpinteria {
        state.send_console(conn_id, "No tienes suficiente habilidad", font_index::INFO).await;
        return;
    }

    // Check materials (wood + stones)
    let has_materials = check_has_items(state, conn_id, &[
        (LENA_OBJ, obj.madera),
        (PIEDRA_OBJ, obj.piedras),
    ]);

    if !has_materials {
        state.send_console(conn_id, "No tienes los materiales necesarios", font_index::INFO).await;
        return;
    }

    // Remove materials
    remove_items_from_inv(state, conn_id, LENA_OBJ, obj.madera).await;
    remove_items_from_inv(state, conn_id, PIEDRA_OBJ, obj.piedras).await;

    // Give crafted item
    let slot = find_or_add_inv_slot(state, conn_id, obj_index, 1);
    if let Some(idx) = slot {
        send_inventory_slot(state, conn_id, idx).await;
    }

    // Play sound
    let (map, x, y) = state.users.get(&conn_id).map(|u| (u.pos_map, u.pos_x, u.pos_y)).unwrap_or((0,0,0));
    let snd = binary_packets::write_play_wave(SND_CARPINTERO as u8, x as u8, y as u8);
    state.send_data_bytes(SendTarget::ToArea { map, x, y }, &snd).await;

    state.send_console(conn_id, &format!("Has construido {}", obj.name), font_index::INFO).await;

    if let Some(u) = state.users.get_mut(&conn_id) {
        try_level_skill(u, 15);
        grant_crafting_rep(u);
    }
}

// =====================================================================
// Survival skill — Campfire creation (VB6: DoHacerFogata)
// =====================================================================

/// Campfire object indices
// FOGATA_OBJ and LENA_FOGATA imported from crate::game::constants

/// /FOGATA or survival skill — Create a campfire using firewood from inventory.
/// VB6: Requires 3+ Leña, success based on survival skill level.
pub(super) async fn handle_crear_fogata(state: &mut GameState, conn_id: ConnectionId) {
    let user_data = match state.users.get(&conn_id) {
        Some(u) if u.logged => (u.dead, u.pos_map, u.pos_x, u.pos_y,
            u.skills.get((skill_id::SUPERVIVENCIA - 1) as usize).copied().unwrap_or(0),
            u.criminal),
        _ => return,
    };
    let (dead, map, x, y, skill_surv, _criminal) = user_data;

    if dead {
        state.send_console(conn_id, "Estas muerto.", font_index::INFO).await;
        return;
    }

    // Check tile trigger — no campfires in safe zones (trigger 1)
    let tile_trigger = get_map_tile_trigger(state, map, x, y);
    if tile_trigger == crate::data::maps::Trigger::SafeZone {
        state.send_console(conn_id, "No puedes crear fogatas en zona segura.", font_index::INFO).await;
        return;
    }

    // Check for firewood in inventory (need 3+)
    let lena_count: i32 = state.users.get(&conn_id)
        .map(|u| u.inventory.iter().filter(|s| s.obj_index == LENA_FOGATA).map(|s| s.amount).sum())
        .unwrap_or(0);

    if lena_count < 3 {
        state.send_console(conn_id, "Necesitas al menos 3 leñas para crear una fogata.", font_index::INFO).await;
        return;
    }

    // Success chance based on skill (VB6)
    let suerte = if skill_surv < 6 {
        3 // 33%
    } else if skill_surv <= 34 {
        2 // 50%
    } else {
        1 // 100%
    };

    let roll = rand_range(1, suerte);
    let success = roll == 1;

    // Consume 3 firewood
    let mut removed = 0;
    if let Some(user) = state.users.get_mut(&conn_id) {
        for slot in user.inventory.iter_mut() {
            if slot.obj_index == LENA_FOGATA && slot.amount > 0 && removed < 3 {
                let take = (3 - removed).min(slot.amount);
                slot.amount -= take;
                removed += take;
                if slot.amount <= 0 {
                    slot.obj_index = 0;
                    slot.amount = 0;
                }
            }
        }
    }
    send_full_inventory(state, conn_id).await;

    if success {
        // Place campfire on ground
        let tile_free = state.world.grid(map).and_then(|g| g.tile(x, y))
            .map(|t| t.ground_item.obj_index == 0)
            .unwrap_or(false);

        if tile_free {
            {
                let grid = state.world.grid_mut(map);
                if let Some(tile) = grid.tile_mut(x, y) {
                    tile.ground_item.obj_index = FOGATA_OBJ;
                    tile.ground_item.amount = 1;
                }
            }

            // Get campfire GRH for visual
            let grh = state.get_object(FOGATA_OBJ).map(|o| o.grh_index).unwrap_or(0);
            let ho_pkt = binary_packets::write_object_create(x as u8, y as u8, grh as i16);
            state.send_data_bytes(SendTarget::ToArea { map, x, y }, &ho_pkt).await;

            // Add to cleanup list (temporary — VB6 uses garbage collector)
            clean_world_add_item(state, map, x, y, 180, FOGATA_OBJ);
        }

        state.send_console(conn_id, "Has creado una fogata.", font_index::INFO).await;

        // XP gain on success
        if let Some(u) = state.users.get_mut(&conn_id) {
            try_level_skill(u, skill_id::SUPERVIVENCIA as usize);
        }
    } else {
        state.send_console(conn_id, "No lograste encender la fogata.", font_index::INFO).await;

        // XP on failure (half)
        if let Some(u) = state.users.get_mut(&conn_id) {
            try_level_skill_with_hit(u, skill_id::SUPERVIVENCIA as usize, false);
        }
    }
}

/// VB6: Get health status text based on survival skill level.
/// Used when looking at NPCs/players — gives more detail with higher skill.
pub(super) fn health_status_text(current_hp: i32, max_hp: i32, survival_skill: i32) -> &'static str {
    if max_hp <= 0 { return "Dudoso"; }
    let pct = (current_hp as f64 / max_hp as f64 * 100.0) as i32;

    if survival_skill <= 10 {
        "Dudoso"
    } else if survival_skill <= 20 {
        if pct < 50 { "Herido" } else { "Sano" }
    } else if survival_skill <= 30 {
        if pct < 25 { "Malherido" } else if pct < 75 { "Herido" } else { "Sano" }
    } else if survival_skill <= 40 {
        if pct < 15 { "Muy malherido" } else if pct < 50 { "Herido" }
        else if pct < 85 { "Levemente herido" } else { "Sano" }
    } else {
        if pct < 5 { "Agonizando" }
        else if pct < 15 { "Casi muerto" }
        else if pct < 30 { "Muy malherido" }
        else if pct < 50 { "Herido" }
        else if pct < 75 { "Levemente herido" }
        else if pct < 95 { "Sano" }
        else { "Intacto" }
    }
}

// find_or_add_inv_slot, check_has_items, remove_items_from_inv — moved to common.rs

