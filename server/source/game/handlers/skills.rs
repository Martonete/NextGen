//! Skills and crafting handlers: fishing, logging, mining, taming, smithing,
//! smelting, stealing, hiding, backstab, ranged combat, construction.
//! Extracted from mod.rs to reduce file size.

use tracing::info;
use crate::net::ConnectionId;
use crate::game::types::{GameState, UserState, SendTarget, InventorySlot, MAX_INVENTORY_SLOTS};
use crate::game::world;
use crate::protocol::{server_opcodes, font_types, fields::read_field};
use crate::data::objects::{ObjData, ObjType};
use super::common::*;
use super::{
    send_inventory_slot, user_die, do_cast_spell,
    calc_attack_power, calc_defense_power, calc_armor_absorption,
    class_damage_modifier, class_damage_modifier_from_balance,
    check_user_level, quest_check_npc_kill,
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

/// Tool object indices (VB6 constants).
const HACHA_LENADOR: i32 = 127;
const PIQUETE_MINERO: i32 = 187;
const MARTILLO_HERRERO: i32 = 389;
const SERRUCHO_CARPINTERO: i32 = 198;
const CANA_PESCA: i32 = 543; // Fishing rod
const RED_PESCA: i32 = 543;  // Fishing net (same category)

/// Resource items.
const LENA_OBJ: i32 = 58;
const PESCADO_OBJ: i32 = 139;
const HIERRO_CRUDO: i32 = 192;
const PLATA_CRUDA: i32 = 193;
const ORO_CRUDO: i32 = 194;
const LINGOTE_HIERRO: i32 = 386;
const LINGOTE_PLATA: i32 = 387;
const LINGOTE_ORO: i32 = 388;
const PIEDRA_OBJ: i32 = 1225;    // Magic stones (carpentry material)

/// Sound IDs.
const SND_TALAR: i32 = 13;
const SND_PESCAR: i32 = 14;
const SND_MINERO: i32 = 15;
const SND_HERRERO: i32 = 41;
const SND_CARPINTERO: i32 = 42;

/// Stamina costs.
const ESFUERZO_TALAR_RECOLECTOR: i32 = 2;
const ESFUERZO_TALAR_GENERAL: i32 = 4;
const ESFUERZO_PESCAR_RECOLECTOR: i32 = 1;
const ESFUERZO_PESCAR_GENERAL: i32 = 3;
const ESFUERZO_EXCAVAR_RECOLECTOR: i32 = 2;
const ESFUERZO_EXCAVAR_GENERAL: i32 = 5;

/// Common luck table for skill success checks (VB6 pattern).
pub(super) fn luck_denominator(skill: i32) -> i32 {
    match skill {
        0..=10 => 35,
        11..=20 => 30,
        21..=30 => 28,
        31..=40 => 24,
        41..=50 => 22,
        51..=60 => 20,
        61..=70 => 18,
        71..=80 => 15,
        81..=90 => 13,
        91..=100 => 7,
        _ => 35,
    }
}

/// Simple random number in range [min, max] inclusive.
// random_number — moved to common.rs

/// Try to level up a skill (VB6: SubirSkill).
/// Returns true if skill increased.
pub(super) fn try_level_skill(user: &mut UserState, skill_idx: usize) -> bool {
    if skill_idx == 0 || skill_idx > 21 { return false; }
    let idx = skill_idx - 1; // Convert to 0-based

    // Must not be hungry or thirsty
    if user.min_ham <= 0 || user.min_agua <= 0 { return false; }

    // Max skill points
    if user.skills[idx] >= 100 { return false; }

    // Level cap: min(level * 3, 100)
    let cap = (user.level * 3).min(100);
    if user.skills[idx] >= cap { return false; }

    // Probability based on level
    let prob = match user.level {
        1..=3 => 25,
        4..=5 => 35,
        6..=9 => 40,
        10..=19 => 45,
        _ => 50,
    };

    let roll = random_number(1, prob);
    if roll == 7 {
        user.skills[idx] += 1;
        user.exp += 50;
        return true;
    }
    false
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

/// Check if class is RECOLECTOR (resource gatherer class).
pub(super) fn is_recolector(class: &str) -> bool {
    class.eq_ignore_ascii_case("Recolector")
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
        let msg = "||3".to_string(); // TEXTO3: Estás muerto
        state.send_to(conn_id, &msg).await;
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
                state.send_to(conn_id, "||288").await;
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
        Some(u) => (u.pos_map, u.pos_x, u.pos_y, u.class.clone(), u.skills[12]), // Pesca = index 12 (1-based 13)
        None => return,
    };

    // Check equipped fishing tool
    let weapon = state.users.get(&conn_id).map(|u| equipped_weapon_obj(u)).unwrap_or(0);
    if weapon == 0 {
        let msg = format!("{}Necesitas una caña de pescar{}", server_opcodes::CONSOLE_MSG, font_types::INFO);
        state.send_to(conn_id, &msg).await;
        return;
    }

    // Check target has water (VB6 HayAgua)
    let has_water = state.hay_agua(map, tx, ty);

    if !has_water {
        let msg = "||250".to_string(); // TEXTO250: No hay agua donde pescar
        state.send_to(conn_id, &msg).await;
        return;
    }

    // Stamina cost
    let sta_cost = if is_recolector(&class) { ESFUERZO_PESCAR_RECOLECTOR } else { ESFUERZO_PESCAR_GENERAL };
    if let Some(u) = state.users.get(&conn_id) {
        if u.min_sta < sta_cost {
            let msg = "||17".to_string(); // TEXTO17: Estas muy cansado para realizar esa acción
            state.send_to(conn_id, &msg).await;
            return;
        }
    }

    // Deduct stamina
    if let Some(u) = state.users.get_mut(&conn_id) {
        u.min_sta -= sta_cost;
    }

    // Play sound to area
    let snd = format!("TW{}", SND_PESCAR);
    state.send_data(SendTarget::ToArea { map, x: ux, y: uy }, &snd).await;

    // Luck roll
    let suerte = luck_denominator(skill);
    let roll = random_number(1, suerte);

    if roll < 6 {
        // Success: give fish (VB6: always 1 fish regardless of class)
        let amount = 1;

        // Find inventory slot
        let slot = find_or_add_inv_slot(state, conn_id, PESCADO_OBJ, amount);
        if let Some(idx) = slot {
            send_inventory_slot(state, conn_id, idx).await;
            let msg = "||813".to_string(); // TEXTO813: Has pescado un lindo pez!
            state.send_to(conn_id, &msg).await;
        }
    } else {
        let msg = "||814".to_string(); // TEXTO814: No has pescado nada!
        state.send_to(conn_id, &msg).await;
    }

    // Try level skill + send stat updates
    if let Some(u) = state.users.get_mut(&conn_id) {
        try_level_skill(u, 13);
    }
    send_stats_sta(state, conn_id).await;
}

/// Woodcutting (DoTalar).
pub(super) async fn do_talar(state: &mut GameState, conn_id: ConnectionId, tx: i32, ty: i32) {
    let (map, ux, uy, class, skill) = match state.users.get(&conn_id) {
        Some(u) => (u.pos_map, u.pos_x, u.pos_y, u.class.clone(), u.skills[9]), // Talar = index 9 (1-based 10)
        None => return,
    };

    // Check equipped axe
    let weapon = state.users.get(&conn_id).map(|u| equipped_weapon_obj(u)).unwrap_or(0);
    if weapon != HACHA_LENADOR {
        let msg = format!("{}Necesitas un hacha de leñador{}", server_opcodes::CONSOLE_MSG, font_types::INFO);
        state.send_to(conn_id, &msg).await;
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
        let msg = "||255".to_string(); // TEXTO255: No hay ningun arbol ahi
        state.send_to(conn_id, &msg).await;
        return;
    }

    // Stamina cost
    let sta_cost = if is_recolector(&class) { ESFUERZO_TALAR_RECOLECTOR } else { ESFUERZO_TALAR_GENERAL };
    if let Some(u) = state.users.get(&conn_id) {
        if u.min_sta < sta_cost {
            let msg = "||17".to_string(); // TEXTO17: Estas muy cansado para realizar esa acción
            state.send_to(conn_id, &msg).await;
            return;
        }
    }
    if let Some(u) = state.users.get_mut(&conn_id) {
        u.min_sta -= sta_cost;
    }

    // Play sound
    let snd = format!("TW{}", SND_TALAR);
    state.send_data(SendTarget::ToArea { map, x: ux, y: uy }, &snd).await;

    // Luck roll
    let suerte = luck_denominator(skill);
    let roll = random_number(1, suerte);

    if roll < 6 {
        let amount = if is_recolector(&class) { random_number(1, 5) } else { 1 };
        let slot = find_or_add_inv_slot(state, conn_id, LENA_OBJ, amount);
        if let Some(idx) = slot {
            send_inventory_slot(state, conn_id, idx).await;
            let msg = "||825".to_string(); // TEXTO825: Has conseguido algo de leña!
            state.send_to(conn_id, &msg).await;
        }
    } else {
        let msg = "||826".to_string(); // TEXTO826: No has obtenido leña!
        state.send_to(conn_id, &msg).await;
    }

    if let Some(u) = state.users.get_mut(&conn_id) {
        try_level_skill(u, 10);
    }
    send_stats_sta(state, conn_id).await;
}

/// Mining (DoMineria).
pub(super) async fn do_mineria(state: &mut GameState, conn_id: ConnectionId, tx: i32, ty: i32) {
    let (map, ux, uy, class, skill) = match state.users.get(&conn_id) {
        Some(u) => (u.pos_map, u.pos_x, u.pos_y, u.class.clone(), u.skills[13]), // Mineria = index 13 (1-based 14)
        None => return,
    };

    // Check equipped pick
    let weapon = state.users.get(&conn_id).map(|u| equipped_weapon_obj(u)).unwrap_or(0);
    if weapon != PIQUETE_MINERO {
        let msg = format!("{}Necesitas un pico de minero{}", server_opcodes::CONSOLE_MSG, font_types::INFO);
        state.send_to(conn_id, &msg).await;
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
            let msg = "||256".to_string(); // TEXTO256: Ahi no hay ningun yacimiento
            state.send_to(conn_id, &msg).await;
            return;
        }
    };

    // Stamina cost
    let sta_cost = if is_recolector(&class) { ESFUERZO_EXCAVAR_RECOLECTOR } else { ESFUERZO_EXCAVAR_GENERAL };
    if let Some(u) = state.users.get(&conn_id) {
        if u.min_sta < sta_cost {
            let msg = "||17".to_string(); // TEXTO17: Estas muy cansado para realizar esa acción
            state.send_to(conn_id, &msg).await;
            return;
        }
    }
    if let Some(u) = state.users.get_mut(&conn_id) {
        u.min_sta -= sta_cost;
    }

    // Play sound
    let snd = format!("TW{}", SND_MINERO);
    state.send_data(SendTarget::ToArea { map, x: ux, y: uy }, &snd).await;

    // Luck roll
    let suerte = luck_denominator(skill);
    let roll = random_number(1, suerte);

    if roll <= 5 {
        // Determine mineral type from yacimiento
        let mineral_item = if mineral_data.mineral_index > 0 {
            mineral_data.mineral_index
        } else {
            HIERRO_CRUDO // Default to iron
        };

        let amount = if is_recolector(&class) { random_number(1, 6) } else { 1 };
        let slot = find_or_add_inv_slot(state, conn_id, mineral_item, amount);
        if let Some(idx) = slot {
            send_inventory_slot(state, conn_id, idx).await;
            let msg = "||827".to_string(); // TEXTO827: Has extraido algunos minerales!
            state.send_to(conn_id, &msg).await;
        }
    } else {
        let msg = "||828".to_string(); // TEXTO828: No has conseguido nada!
        state.send_to(conn_id, &msg).await;
    }

    if let Some(u) = state.users.get_mut(&conn_id) {
        try_level_skill(u, 14);
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
            let msg = "||258".to_string(); // TEXTO258: No hay ninguna criatura alli!
            state.send_to(conn_id, &msg).await;
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
        let msg = "||257".to_string(); // TEXTO257: No podes domar a esa criatura
        state.send_to(conn_id, &msg).await;
        return;
    }

    // Calculate taming power
    let (carisma, skill_domar, class) = match state.users.get(&conn_id) {
        Some(u) => (u.attributes[3], u.skills[17], u.class.clone()), // Cha=3, Domar=17 (1-based 18)
        None => return,
    };

    let mod_domar = match class.to_lowercase().as_str() {
        "druida" | "cazador" => 6,
        "clerigo" => 7,
        _ => 10,
    };

    let poder = carisma * (skill_domar / mod_domar)
        + random_number(1, carisma.max(1) / 3 + 1)
        + random_number(1, carisma.max(1) / 3 + 1)
        + random_number(1, carisma.max(1) / 3 + 1);

    if poder >= domable {
        let msg = format!("{}Has domado a la criatura{}", server_opcodes::CONSOLE_MSG, font_types::INFO);
        state.send_to(conn_id, &msg).await;
        // Note: Full pet system (NPC follows owner) not yet implemented
    } else {
        let msg = format!("{}No has podido domar a la criatura{}", server_opcodes::CONSOLE_MSG, font_types::INFO);
        state.send_to(conn_id, &msg).await;
    }

    if let Some(u) = state.users.get_mut(&conn_id) {
        try_level_skill(u, 18);
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
        let msg = "||263".to_string(); // TEXTO263: Ahi no hay ningun yunque
        state.send_to(conn_id, &msg).await;
        return;
    }

    // Send buildable items lists
    let skill_herreria = state.users.get(&conn_id).map(|u| u.skills[15]).unwrap_or(0); // Herreria = 15 (1-based 16)

    // Build weapons list
    let mut weapons_list = String::new();
    let mut armors_list = String::new();
    for obj in state.game_data.objects.iter() {
        if obj.sk_herreria > 0 && obj.sk_herreria <= skill_herreria {
            let entry = format!("{} ({}-{}-{}),{},",
                obj.name, obj.ling_h, obj.ling_p, obj.ling_o, obj.index);
            if obj.obj_type == ObjType::Weapon {
                weapons_list.push_str(&entry);
            } else if obj.obj_type == ObjType::Armor || obj.obj_type == ObjType::Shield || obj.obj_type == ObjType::Helmet {
                armors_list.push_str(&entry);
            }
        }
    }

    let pkt = format!("{}{}", server_opcodes::SMITH_WEAPONS, weapons_list);
    state.send_to(conn_id, &pkt).await;
    let pkt = format!("{}{}", server_opcodes::SMITH_ARMORS, armors_list);
    state.send_to(conn_id, &pkt).await;
    state.send_to(conn_id, server_opcodes::OPEN_SMITH).await;
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
            let msg = "||259".to_string(); // TEXTO259: No tienes mas minerales
            state.send_to(conn_id, &msg).await;
            return;
        }
    };

    // Minerals per ingot
    let minerals_needed = match mineral_obj {
        HIERRO_CRUDO => 13,
        PLATA_CRUDA => 25,
        ORO_CRUDO => 50,
        _ => 13,
    };

    if amount < minerals_needed {
        let msg = format!("{}No tienes suficientes minerales (necesitas {}){}", server_opcodes::CONSOLE_MSG, minerals_needed, font_types::INFO);
        state.send_to(conn_id, &msg).await;
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

    let msg = format!("{}Has fundido un lingote{}", server_opcodes::CONSOLE_MSG, font_types::INFO);
    state.send_to(conn_id, &msg).await;

    if let Some(u) = state.users.get_mut(&conn_id) {
        try_level_skill(u, 14); // Mining skill
    }
}

/// Stealing (DoRobar).
pub(super) async fn do_robar(state: &mut GameState, conn_id: ConnectionId, tx: i32, ty: i32) {
    let (map, ux, uy, skill, class) = match state.users.get(&conn_id) {
        Some(u) => (u.pos_map, u.pos_x, u.pos_y, u.skills[2], u.class.clone()), // Robar = 2 (1-based 3)
        None => return,
    };

    // Distance check
    if (tx - ux).abs() > 2 || (ty - uy).abs() > 2 {
        return;
    }

    // Find target user on tile
    let target_conn = state.world.grid(map)
        .and_then(|g| g.tile(tx, ty))
        .and_then(|t| t.user_conn);

    let target_conn = match target_conn {
        Some(id) if id != conn_id => id,
        _ => {
            let msg = "||252".to_string(); // TEXTO252: No hay a quien robarle!
            state.send_to(conn_id, &msg).await;
            return;
        }
    };

    // Luck roll
    let suerte = luck_denominator(skill);
    let roll = random_number(1, suerte);

    if roll < 3 {
        // Steal gold
        let is_ladron = class.eq_ignore_ascii_case("Ladron") || class.eq_ignore_ascii_case("Asesino");
        let gold_amount = if is_ladron { random_number(100, 1000) } else { random_number(1, 100) };

        let victim_gold = state.users.get(&target_conn).map(|u| u.gold).unwrap_or(0);
        let stolen = (gold_amount as i64).min(victim_gold);

        if stolen > 0 {
            if let Some(victim) = state.users.get_mut(&target_conn) {
                victim.gold -= stolen;
            }
            if let Some(thief) = state.users.get_mut(&conn_id) {
                thief.gold = (thief.gold + stolen).min(MAX_GOLD);
            }

            // TEXTO816: Le has robado %1 monedas de oro a %2
            let victim_name_for_rob = state.users.get(&target_conn).map(|u| u.char_name.clone()).unwrap_or_default();
            state.send_to(conn_id, &format!("||816@{}@{}", stolen, victim_name_for_rob)).await;
            // TEXTO819: %1 ha intentado robarte!
            let thief_name = state.users.get(&conn_id).map(|u| u.char_name.clone()).unwrap_or_default();
            state.send_to(target_conn, &format!("||819@{}", thief_name)).await;

            send_stats_gold(state, conn_id).await;
            send_stats_gold(state, target_conn).await;
        } else {
            let msg = "||818".to_string(); // TEXTO818: No has logrado robar nada!
            state.send_to(conn_id, &msg).await;
        }
    } else {
        // Fail — notify victim
        let thief_name = state.users.get(&conn_id).map(|u| u.char_name.clone()).unwrap_or_default();
        let msg = "||818".to_string(); // TEXTO818: No has logrado robar nada!
        state.send_to(conn_id, &msg).await;

        let victim_msg = format!("{}{} ha intentado robarte!{}", server_opcodes::CONSOLE_MSG, thief_name, font_types::COMBAT);
        state.send_to(target_conn, &victim_msg).await;
    }

    if let Some(u) = state.users.get_mut(&conn_id) {
        try_level_skill(u, 3);
    }
}

/// Ranged attack (bow + arrows).
/// VB6: TCP_HandleData1.bas case Proyectiles. Requires bow equipped (proyectil=1) and arrows.
/// Consumes 1 arrow per shot, sends FLECHI packet for visual, then resolves hit via normal combat.
const OBJ_TYPE_FLECHAS: i32 = 32;

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

    // Check arrows equipped
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
        let msg = format!("{}246", server_opcodes::CONSOLE_MSG_ID); // "No arrows"
        state.send_to(conn_id, &msg).await;
        // Unequip ammo slot
        if let Some(user) = state.users.get_mut(&conn_id) {
            user.equip.municion = 0;
        }
        return;
    }

    // Stamina cost (1-10)
    if sta < 10 {
        let msg = format!("{}17", server_opcodes::CONSOLE_MSG_ID); // "Not enough stamina"
        state.send_to(conn_id, &msg).await;
        return;
    }
    let sta_cost = random_number(1, 10);
    if let Some(user) = state.users.get_mut(&conn_id) {
        user.min_sta = (user.min_sta - sta_cost).max(0);
    }
    send_stats_sta(state, conn_id).await;

    // Consume 1 arrow
    if let Some(user) = state.users.get_mut(&conn_id) {
        if municion_slot > 0 && municion_slot <= MAX_INVENTORY_SLOTS {
            user.inventory[municion_slot - 1].amount -= 1;
            if user.inventory[municion_slot - 1].amount <= 0 {
                user.inventory[municion_slot - 1] = InventorySlot::default();
                user.equip.municion = 0;
            }
        }
    }
    // Update inventory slot
    send_inventory_slot(state, conn_id, municion_slot).await;

    // Get arrow GRH for visual
    let arrow_grh = state.get_object(municion_obj_idx).map(|o| o.grh_index).unwrap_or(0);

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
            let flechi = format!("FLECHI{},{},{}", char_index.0, npc_char, arrow_grh);
            state.send_data(SendTarget::ToMap(map), &flechi).await;

            // Store target for combat resolution, then call standard attack
            if let Some(user) = state.users.get_mut(&conn_id) {
                user.target_npc_idx = npc_idx;
            }
            // Reuse melee hit resolution (damage calc is the same for ranged in VB6)
            resolve_attack_npc(state, conn_id, npc_idx).await;
        }
    } else if let Some(target) = target_user_tile {
        if target != conn_id {
            // Ranged attack vs user
            let target_char = state.users.get(&target).map(|u| u.char_index.0).unwrap_or(0);

            // Send arrow visual
            let flechi = format!("FLECHI{},{},{}", char_index.0, target_char, arrow_grh);
            state.send_data(SendTarget::ToMap(map), &flechi).await;

            // Store target and resolve
            if let Some(user) = state.users.get_mut(&conn_id) {
                user.target_user = target;
            }
            resolve_attack_user(state, conn_id, target).await;
        }
    }
}

/// Resolve ranged/melee attack against NPC — shared damage calculation.
/// Extracted from handle_attack for reuse in ranged attacks.
pub(super) async fn resolve_attack_npc(state: &mut GameState, conn_id: ConnectionId, npc_idx: usize) {
    let user_data = match state.users.get(&conn_id) {
        Some(u) if u.logged && !u.dead => {
            (u.pos_map, u.pos_x, u.pos_y, u.char_index,
             u.level, u.attributes[0], u.attributes[1],
             u.min_hit, u.max_hit, u.skills[1], u.char_name.clone(), u.class.clone())
        }
        _ => return,
    };
    let (map, x, y, char_index, level, strength, agility,
         min_hit, max_hit, skill_armas, attacker_name, class) = user_data;

    let npc_data = match state.get_npc(npc_idx) {
        Some(n) if n.active && n.attackable => {
            (n.char_index, n.def, n.poder_evasion, n.min_hp,
             n.max_hp, n.give_exp, n.npc_number, n.name.clone())
        }
        _ => return,
    };
    let (npc_char, npc_def, npc_evasion, npc_hp, npc_max_hp,
         npc_exp, npc_number, npc_name) = npc_data;

    // Hit check
    let attack_power = calc_attack_power(skill_armas, agility, level);
    let hit_prob = ((50.0 + (attack_power - npc_evasion as f64) * 0.4) as i32).clamp(10, 90);

    if rand_range(1, 100) > hit_prob {
        state.send_to(conn_id, "U1").await; // Miss
        return;
    }

    // Damage
    let weapon_dmg = rand_range(min_hit.max(1), max_hit.max(1));
    let str_bonus = ((max_hit as f64 / 5.0) * (strength - 15).max(0) as f64) as i32;
    let base_dmg = 3 * weapon_dmg + str_bonus + rand_range(min_hit, max_hit);
    let class_mod = class_damage_modifier(&class);
    let mut damage = ((base_dmg as f64) * class_mod) as i32;
    damage = (damage - npc_def).max(1);

    // Apply damage
    let new_hp = if let Some(npc) = state.get_npc_mut(npc_idx) {
        npc.min_hp -= damage;
        npc.min_hp
    } else {
        return;
    };

    // Send hit packet
    let u2_pkt = format!("U2,{},{}", damage, npc_name);
    state.send_to(conn_id, &u2_pkt).await;

    if new_hp <= 0 {
        // NPC killed
        if let Some(u) = state.users.get_mut(&conn_id) {
            let exp_gained = npc_exp as i64;
            u.exp += exp_gained;
        }
        send_stats_exp(state, conn_id).await;
        check_user_level(state, conn_id).await;

        // Kill NPC
        let npc_ci = state.get_npc(npc_idx).map(|n| n.char_index.0).unwrap_or(0);
        let bp_pkt = format!("BP{}", npc_ci);
        state.send_data(SendTarget::ToArea { map, x, y }, &bp_pkt).await;

        state.kill_npc(npc_idx);
        quest_check_npc_kill(state, conn_id, npc_number as i32).await;
    }
}

/// Resolve ranged/melee attack against user — shared PvP damage calculation.
pub(super) async fn resolve_attack_user(state: &mut GameState, conn_id: ConnectionId, victim_id: ConnectionId) {
    let att_data = match state.users.get(&conn_id) {
        Some(u) if u.logged && !u.dead => {
            (u.pos_map, u.pos_x, u.pos_y, u.char_index,
             u.level, u.attributes[0], u.attributes[1],
             u.min_hit, u.max_hit, u.skills[1], u.char_name.clone(), u.class.clone(),
             u.safe_toggle)
        }
        _ => return,
    };
    let (map, x, y, _char_index, level, strength, agility,
         min_hit, max_hit, skill_armas, attacker_name, class, safe_on) = att_data;

    if safe_on {
        state.send_to(conn_id, "||207").await; // TEXTO207: Escribe /SEG para quitar el seguro
        return;
    }

    let victim_data = match state.users.get(&victim_id) {
        Some(v) if v.logged && !v.dead => {
            (v.level, v.attributes[1], v.skills[3], v.char_name.clone(), v.privileges)
        }
        _ => return,
    };
    let (v_level, v_agility, v_tacticas, victim_name, v_privs) = victim_data;

    if v_privs > 0 { return; }

    // Hit check
    let attack_power = calc_attack_power(skill_armas, agility, level);
    let defense_power = calc_defense_power(v_tacticas, v_agility, v_level);
    let hit_prob = ((50.0 + (attack_power - defense_power) * 0.4) as i32).clamp(10, 90);

    if rand_range(1, 100) > hit_prob {
        let pkt = format!("U3{}", attacker_name);
        state.send_to(victim_id, &pkt).await;
        state.send_to(conn_id, "U1").await;
        return;
    }

    // Damage calculation (VB6: CalcularDaño + UserDañoUser)
    let weapon_dmg = rand_range(min_hit.max(1), max_hit.max(1));
    let str_bonus = ((max_hit as f64 / 5.0) * (strength - 15).max(0) as f64) as i32;
    let base_dmg = 3 * weapon_dmg + str_bonus + rand_range(min_hit, max_hit);
    let class_mod = class_damage_modifier_from_balance(state, &class);
    let mut damage = ((base_dmg as f64) * class_mod) as i32;

    // Body part hit (1=head, 2-6=body)
    let body_part = rand_range(1, 6);

    // Armor absorption (VB6: victim's armor reduces damage)
    let absorption = calc_armor_absorption(state, victim_id, body_part);
    damage -= absorption;

    // Critical hit (20% chance — VB6: RandomNumber(1,5) == 1)
    if rand_range(1, 5) == 1 {
        damage *= 2;
    }

    let damage = damage.max(1);

    // Apply damage
    if let Some(victim) = state.users.get_mut(&victim_id) {
        victim.min_hp -= damage;
    }
    let n4_pkt = format!("N4{},{},{}", body_part, damage, attacker_name);
    state.send_to(victim_id, &n4_pkt).await;
    let n5_pkt = format!("N5{},{},{}", body_part, damage, victim_name);
    state.send_to(conn_id, &n5_pkt).await;

    send_stats_hp(state, victim_id).await;

    // Check death
    let hp = state.users.get(&victim_id).map(|u| u.min_hp).unwrap_or(0);
    if hp <= 0 {
        user_die(state, victim_id, Some(conn_id)).await;
    }
}

/// DoOcultarse — Hide skill (VB6: Trabajo.bas DoOcultarse).
/// Skill-based success chance. Thieves/Hunters get bonus. Blocked on special maps.
pub(super) async fn do_ocultarse(state: &mut GameState, conn_id: ConnectionId) {
    let (map, skill, class, char_index, already_hidden) = match state.users.get(&conn_id) {
        Some(u) if u.logged && !u.dead => {
            (u.pos_map, u.skills.get(7).copied().unwrap_or(0), u.class.clone(), u.char_index, u.hidden)
        }
        _ => return,
    };

    // Blocked on special maps (VB6: 142, 121-123, 31-34)
    let blocked_maps = [142, 121, 122, 123, 31, 32, 33, 34];
    if blocked_maps.contains(&map) {
        state.send_to(conn_id, "||838").await;
        return;
    }

    // Suerte (luck) table from VB6
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
        91..=100 => 7,
        _ => 7,
    };

    // Non-thief/hunter get +50 penalty
    let is_thief_or_hunter = class.eq_ignore_ascii_case("Ladron") || class.eq_ignore_ascii_case("Cazador");
    let suerte = if is_thief_or_hunter { suerte } else { suerte + 50 };

    let res = rand_range(1, suerte);

    if res <= 5 {
        // Success — hide
        if let Some(user) = state.users.get_mut(&conn_id) {
            user.hidden = true;
        }
        // Send NOVER to make invisible on all clients in map
        let nover = format!("NOVER{},1", char_index.0);
        state.send_data(SendTarget::ToMap(map), &nover).await;
        state.send_to(conn_id, "||808").await; // "Te has ocultado."
        // Skill gain
        if let Some(u) = state.users.get_mut(&conn_id) {
            try_level_skill(u, 8); // Ocultarse = eSkill 8 (1-based)
        }
    } else {
        // Failure
        state.send_to(conn_id, "||809").await; // "No has logrado ocultarte."
    }
}

/// DoPermanecerOculto — Check if hidden user remains hidden (called each tick or on action).
/// VB6: Trabajo.bas DoPermanecerOculto. Returns false if user was revealed.
pub(super) fn check_permanecer_oculto(user: &mut UserState) -> bool {
    if !user.hidden { return false; }

    let skill = user.skills.get(7).copied().unwrap_or(0); // Ocultarse = eSkill 8, 0-based = 7
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
        91..=100 => 10,
        _ => 10,
    };

    let is_thief_or_hunter = user.class.eq_ignore_ascii_case("Ladron") || user.class.eq_ignore_ascii_case("Cazador");
    let suerte = if is_thief_or_hunter { suerte } else { suerte + 50 };

    // Hunter with skill>90 and specific armor stays hidden
    if user.class.eq_ignore_ascii_case("Cazador") && skill > 90 {
        if user.equip.armor == 648 || user.equip.armor == 360 {
            return true;
        }
    }

    let res = rand_range(1, suerte);
    if res > 9 {
        user.hidden = false;
        return false; // Revealed
    }
    true // Still hidden
}

/// DoApuñalar — Backstab skill (VB6: Trabajo.bas DoApuñalar).
/// Called during melee attack when attacker has Apuñalar skill and same heading as victim.
/// Applies 1.5x damage to players, 2x to NPCs.
pub(super) fn calc_apunalar_damage(skill: i32, class: &str, attacker_heading: i32, victim_heading: i32, base_damage: i32, is_npc_target: bool) -> Option<i32> {
    let suerte = match skill {
        0..=10 => 200,
        11..=20 => 190,
        21..=30 => 180,
        31..=40 => 170,
        41..=50 => 160,
        51..=60 => 150,
        61..=70 => 140,
        71..=80 => 130,
        81..=90 => 120,
        91..=99 => 110,
        100.. => 100,
        _ => 200,
    };

    let res = if class.eq_ignore_ascii_case("Asesino") {
        // Assassin gets better odds (0-95 range)
        rand_range(0, 95)
    } else {
        rand_range(0, suerte)
    };

    // Assassin backstab from same heading = auto-success
    let res = if class.eq_ignore_ascii_case("Asesino") && attacker_heading == victim_heading {
        1 // Guaranteed success
    } else {
        res
    };

    if res < 15 {
        let multiplied = if is_npc_target {
            (base_damage as f64 * 2.0) as i32
        } else {
            (base_damage as f64 * 1.5) as i32
        };
        Some(multiplied)
    } else {
        None // Failed
    }
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

    // Check skill
    let skill = state.users.get(&conn_id).map(|u| u.skills[15]).unwrap_or(0); // Herreria=15 (1-based 16)
    if skill < obj.sk_herreria {
        let msg = format!("{}No tienes suficiente habilidad{}", server_opcodes::CONSOLE_MSG, font_types::INFO);
        state.send_to(conn_id, &msg).await;
        return;
    }

    // Check materials (iron, silver, gold ingots)
    let has_materials = check_has_items(state, conn_id, &[
        (LINGOTE_HIERRO, obj.ling_h),
        (LINGOTE_PLATA, obj.ling_p),
        (LINGOTE_ORO, obj.ling_o),
    ]);

    if !has_materials {
        let msg = format!("{}No tienes los materiales necesarios{}", server_opcodes::CONSOLE_MSG, font_types::INFO);
        state.send_to(conn_id, &msg).await;
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
    let snd = format!("TW{}", SND_HERRERO);
    state.send_data(SendTarget::ToArea { map, x, y }, &snd).await;

    let msg = format!("{}Has construido {}{}", server_opcodes::CONSOLE_MSG, obj.name, font_types::INFO);
    state.send_to(conn_id, &msg).await;

    if let Some(u) = state.users.get_mut(&conn_id) {
        try_level_skill(u, 16);
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

    // Check equipped carpentry tool (VB6: SERRUCHO_CARPINTERO must be equipped)
    let weapon = state.users.get(&conn_id).map(|u| equipped_weapon_obj(u)).unwrap_or(0);
    if weapon != SERRUCHO_CARPINTERO {
        let msg = format!("{}Necesitas un serrucho de carpintero{}", server_opcodes::CONSOLE_MSG, font_types::INFO);
        state.send_to(conn_id, &msg).await;
        return;
    }

    // Check skill
    let skill = state.users.get(&conn_id).map(|u| u.skills[14]).unwrap_or(0); // Carpinteria=14 (1-based 15)
    if skill < obj.sk_carpinteria {
        let msg = format!("{}No tienes suficiente habilidad{}", server_opcodes::CONSOLE_MSG, font_types::INFO);
        state.send_to(conn_id, &msg).await;
        return;
    }

    // Check materials (wood + stones)
    let has_materials = check_has_items(state, conn_id, &[
        (LENA_OBJ, obj.madera),
        (PIEDRA_OBJ, obj.piedras),
    ]);

    if !has_materials {
        let msg = format!("{}No tienes los materiales necesarios{}", server_opcodes::CONSOLE_MSG, font_types::INFO);
        state.send_to(conn_id, &msg).await;
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
    let snd = format!("TW{}", SND_CARPINTERO);
    state.send_data(SendTarget::ToArea { map, x, y }, &snd).await;

    let msg = format!("{}Has construido {}{}", server_opcodes::CONSOLE_MSG, obj.name, font_types::INFO);
    state.send_to(conn_id, &msg).await;

    if let Some(u) = state.users.get_mut(&conn_id) {
        try_level_skill(u, 15);
    }
}

// find_or_add_inv_slot, check_has_items, remove_items_from_inv — moved to common.rs

