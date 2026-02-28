//! Combat handlers: melee/ranged attack, PvP damage, user_die, combat formulas.
//! Extracted from mod.rs to reduce file size.

use tracing::info;
use crate::net::ConnectionId;
use crate::game::types::{GameState, SendTarget, InventorySlot, MAX_INVENTORY_SLOTS};
use crate::game::world;
use crate::protocol::{server_opcodes, font_types};
use crate::data::objects::ObjType;
use super::common::*;
use super::{
    user_attack_npc, check_user_level, warp_user, naked_body,
    send_inventory_slot, send_full_inventory,
    resolve_duel_death, resolve_desafio_death,
    torneo_auto_death, evento_player_death,
    nobleza_etapa_uno, pretoriano_check_death,
    cvc_player_death, quest_check_player_kill,
    DEAD_BODY_NEUTRAL, DEAD_HEAD_NEUTRAL,
};
use super::npcs::fire_elemental_react;
use super::skills::{
    check_permanecer_oculto, calc_apunalar_damage, try_desarmar, try_level_skill,
};

// =====================================================================
// Combat handlers
// =====================================================================

/// AT — Melee/ranged attack.
pub(super) async fn handle_attack(state: &mut GameState, conn_id: ConnectionId) {
    let user_data = match state.users.get(&conn_id) {
        Some(u) if u.logged => (
            u.pos_map, u.pos_x, u.pos_y, u.heading, u.char_index,
            u.dead, u.paralyzed, u.safe_toggle, u.level,
            u.attributes[0], // Strength
            u.attributes[1], // Agility
            u.min_hit, u.max_hit,
            u.skills[1], // SK2 = Armas (combat skill)
            u.char_name.clone(),
            u.class.clone(),
        ),
        _ => return,
    };
    let (map, x, y, heading, char_index, dead, paralyzed, safe_on, level,
         strength, agility, min_hit, max_hit, skill_armas, attacker_name, class) = user_data;

    if dead || paralyzed {
        return;
    }

    // VB6: Attacking reveals hidden users (DoPermanecerOculto)
    let was_hidden = state.users.get(&conn_id).map(|u| u.hidden).unwrap_or(false);
    if was_hidden {
        if let Some(user) = state.users.get_mut(&conn_id) {
            let stayed = check_permanecer_oculto(user);
            if !stayed {
                // Revealed — broadcast NOVER
                let nover = format!("NOVER{},0", char_index.0);
                state.send_data(SendTarget::ToMap(map), &nover).await;
                state.send_to(conn_id, "||195").await; // "Has sido descubierto."
            }
        }
    }

    // Anti-cheat: check melee cooldown
    if !puede_pegar(state, conn_id) {
        return;
    }

    // Get target tile based on heading
    let (dx, dy) = world::heading_to_offset(heading);
    let target_x = x + dx;
    let target_y = y + dy;

    // Check if there's a user on the target tile
    let target_conn = state.world.grid(map)
        .and_then(|g| g.tile(target_x, target_y))
        .and_then(|t| t.user_conn);

    // Play attack sound/animation to area
    let swing_pkt = format!("TW{}", 2); // Generic attack sound
    state.send_data(
        SendTarget::ToArea { map, x, y },
        &swing_pkt,
    ).await;

    if let Some(victim_id) = target_conn {
        // PvP attack
        if safe_on {
            state.send_to(conn_id, "||207").await; // TEXTO207: Escribe /SEG para quitar el seguro
            return;
        }

        // VB6: Safe zone check (trigger=4) — no combat allowed
        let attacker_trigger = get_map_tile_trigger(state, map, x, y);
        if attacker_trigger == crate::data::maps::Trigger::SafeZone {
            state.send_to(conn_id, "||163").await; // TEXTO163: zona segura
            return;
        }
        let victim_pos = state.users.get(&victim_id).map(|v| (v.pos_x, v.pos_y)).unwrap_or((0, 0));
        let victim_trigger = get_map_tile_trigger(state, map, victim_pos.0, victim_pos.1);
        if victim_trigger == crate::data::maps::Trigger::SafeZone {
            state.send_to(conn_id, "||163").await; // TEXTO163: zona segura
            return;
        }

        let victim_data = match state.users.get(&victim_id) {
            Some(v) if v.logged => (
                v.dead, v.privileges, v.char_name.clone(),
                v.level, v.attributes[1], // Victim agility
                v.skills[3], // SK4 = Tacticas (evasion skill)
                v.max_hp, v.min_hp,
                v.class.clone(),
                v.heading,
            ),
            _ => return,
        };
        let (v_dead, v_privs, victim_name, v_level, v_agility, v_tacticas,
             v_max_hp, v_min_hp, v_class, v_heading) = victim_data;

        if v_dead {
            return;
        }
        if v_privs > 0 {
            return; // Can't attack GMs
        }

        // Hit/miss calculation
        let attack_power = calc_attack_power(skill_armas, agility, level);
        let defense_power = calc_defense_power(v_tacticas, v_agility, v_level);
        let hit_prob = ((50.0 + (attack_power - defense_power) * 0.4) as i32).clamp(10, 90);

        let mut hit = rand_range(1, 100) <= hit_prob;

        // VB6: Assassin backstab — if same heading and miss, convert to hit
        if !hit && class.eq_ignore_ascii_case("Asesino") && heading == v_heading {
            hit = true;
        }

        if !hit {
            // Miss
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

        // Get weapon penetration (Refuerzo)
        let weapon_refuerzo = state.users.get(&conn_id)
            .and_then(|u| {
                if u.equip.weapon > 0 && u.equip.weapon <= MAX_INVENTORY_SLOTS {
                    let obj_idx = u.inventory[u.equip.weapon - 1].obj_index;
                    state.get_object(obj_idx).map(|o| o.refuerzo)
                } else {
                    None
                }
            })
            .unwrap_or(0);

        // Armor absorption with weapon penetration (VB6: absorbido = armor_def + shield_def - Resist)
        let absorption = calc_armor_absorption_with_penetration(state, victim_id, body_part, weapon_refuerzo);
        damage -= absorption;

        // Head hit bonus (VB6: SistemaCombate.bas:1220-1224)
        if body_part == 1 {
            if class.eq_ignore_ascii_case("Asesino") {
                damage += rand_range(7, 11);
            } else {
                damage += rand_range(13, 20);
            }
        }

        // Critical hit (20% chance)
        if rand_range(1, 5) == 1 {
            damage = (damage as f64 * 1.8) as i32;
        }

        damage = damage.max(1);

        // Apuñalar (backstab) — VB6: DoApuñalar, 1.5x vs player if same heading
        let apunalar_skill = state.users.get(&conn_id).and_then(|u| u.skills.get(8).copied()).unwrap_or(0);
        if apunalar_skill > 0 && heading == v_heading {
            if let Some(stab_dmg) = calc_apunalar_damage(apunalar_skill, &class, heading, v_heading, damage, false) {
                damage = stab_dmg;
                let msg = format!("||821@{}@{}", victim_name, damage);
                state.send_to(conn_id, &msg).await;
                let msg2 = format!("||823@{}@{}", attacker_name, damage);
                state.send_to(victim_id, &msg2).await;
                // Skill gain for apuñalar
                if let Some(u) = state.users.get_mut(&conn_id) {
                    try_level_skill(u, 8);
                }
            }
        }

        // Apply damage
        if let Some(victim) = state.users.get_mut(&victim_id) {
            victim.min_hp -= damage;
        }
        let n4_pkt = format!("N4{},{},{}", body_part, damage, attacker_name);
        state.send_to(victim_id, &n4_pkt).await;

        let n5_pkt = format!("N5{},{},{}", body_part, damage, victim_name);
        state.send_to(conn_id, &n5_pkt).await;

        // Desarmar (disarm) — VB6: Desarmar, chance to unequip victim weapon
        let wresterling_skill = state.users.get(&conn_id).and_then(|u| u.skills.get(20).copied()).unwrap_or(0);
        if wresterling_skill > 0 && try_desarmar(wresterling_skill) {
            // Unequip victim's weapon
            if let Some(victim) = state.users.get_mut(&victim_id) {
                if victim.equip.weapon > 0 {
                    victim.equip.weapon = 0;
                }
            }
            let msg = format!("{}Te han desarmado!{}", server_opcodes::CONSOLE_MSG, font_types::COMBAT);
            state.send_to(victim_id, &msg).await;
            let msg2 = format!("{}Has desarmado a {}!{}", server_opcodes::CONSOLE_MSG, victim_name, font_types::COMBAT);
            state.send_to(conn_id, &msg2).await;
            // Skill gain
            if let Some(u) = state.users.get_mut(&conn_id) {
                try_level_skill(u, 20);
            }
        }

        // Weapon poison application (VB6: 60% chance if weapon has Envenena=1)
        let weapon_envenena = state.users.get(&conn_id)
            .and_then(|u| {
                if u.equip.weapon > 0 && u.equip.weapon <= MAX_INVENTORY_SLOTS {
                    let obj_idx = u.inventory[u.equip.weapon - 1].obj_index;
                    state.get_object(obj_idx).map(|o| o.envenena)
                } else {
                    None
                }
            })
            .unwrap_or(false);

        if weapon_envenena && rand_range(1, 100) <= 60 {
            let already_poisoned = state.users.get(&victim_id).map(|u| u.poisoned).unwrap_or(true);
            if !already_poisoned {
                if let Some(victim) = state.users.get_mut(&victim_id) {
                    victim.poisoned = true;
                    victim.counter_poison = 0;
                }
                let msg = format!("{}{}171@{}", server_opcodes::CONSOLE_MSG, "||", attacker_name);
                state.send_to(victim_id, &msg).await;
                let msg2 = format!("{}{}172@{}", server_opcodes::CONSOLE_MSG, "||", victim_name);
                state.send_to(conn_id, &msg2).await;
            }
        }

        // VB6: Fire Elemental reacts to PvP — enters defense mode against attacker
        fire_elemental_react(state, victim_id, &attacker_name);

        // Update victim HP
        send_stats_hp(state, victim_id).await;

        // Check death
        let v_hp = state.users.get(&victim_id).map(|u| u.min_hp).unwrap_or(0);
        if v_hp <= 0 {
            user_die(state, victim_id, Some(conn_id)).await;
        }
    } else {
        // Check for NPC on target tile
        let target_npc = state.world.grid(map)
            .and_then(|g| g.tile(target_x, target_y))
            .map(|t| t.npc_index)
            .unwrap_or(0);

        if target_npc > 0 {
            user_attack_npc(state, conn_id, target_npc as usize, map, x, y,
                            strength, agility, level, min_hit, max_hit,
                            skill_armas, &attacker_name, &class).await;
        }
    }
}

/// Calculate attack power based on weapon skill, agility, and level.
/// Calculate weapon attack power (VB6: PoderAtaqueArma).
/// Uses balance data mod_poder_ataque_armas per class.
pub(super) fn calc_attack_power_with_balance(skill: i32, agility: i32, level: i32, class_mod: f32) -> f64 {
    let base = if skill < 31 {
        skill as f64 * class_mod as f64
    } else if skill < 61 {
        (skill + agility) as f64 * class_mod as f64
    } else if skill < 91 {
        (skill + 2 * agility) as f64 * class_mod as f64
    } else {
        (skill + 3 * agility) as f64 * class_mod as f64
    };
    base + 2.5 * (level - 12).max(0) as f64
}

/// Simplified version for when we don't have balance data handy.
pub(super) fn calc_attack_power(skill: i32, agility: i32, level: i32) -> f64 {
    calc_attack_power_with_balance(skill, agility, level, 1.0)
}

/// Calculate defense/evasion power (VB6: PoderEvasion).
/// Uses balance data mod_evasion per class.
pub(super) fn calc_defense_power_with_balance(
    tacticas: i32, agility: i32, level: i32, class_mod: f32,
    has_shield: bool, shield_max_def: i32, shield_class_mod: f32,
    is_mago: bool,
) -> f64 {
    // VB6 PoderEvasion formula: (Tacticas + (Tacticas/33 * Agility)) * ModificadorEvasion(clase)
    let base = (tacticas as f64 + (tacticas as f64 / 33.0 * agility as f64)) * class_mod as f64;
    let mut power = base + 2.5 * (level - 12).max(0) as f64;

    // Shield evasion bonus (VB6: not for mages)
    if has_shield && !is_mago {
        let shield_evasion = ((agility as f64 + shield_max_def as f64 + 30.0) * shield_class_mod as f64) / 2.0;
        power += shield_evasion;
    }

    power
}

/// Simplified version.
pub(super) fn calc_defense_power(tacticas: i32, agility: i32, level: i32) -> f64 {
    calc_defense_power_with_balance(tacticas, agility, level, 1.0, false, 0, 1.0, false)
}

/// Get armor absorption for a body hit (VB6: body armor MinDef-MaxDef).
pub(super) fn calc_armor_absorption(state: &GameState, conn_id: ConnectionId, body_part: i32) -> i32 {
    let user = match state.users.get(&conn_id) {
        Some(u) => u,
        None => return 0,
    };

    let armor_slot = if body_part == 1 {
        // Head hit — use helmet
        user.equip.helmet
    } else {
        // Body hit — use armor
        user.equip.armor
    };

    if armor_slot == 0 || armor_slot > MAX_INVENTORY_SLOTS {
        return 0;
    }

    let obj_index = user.inventory[armor_slot - 1].obj_index;
    if obj_index <= 0 {
        return 0;
    }

    match state.get_object(obj_index) {
        Some(obj) if obj.min_def > 0 || obj.max_def > 0 => {
            rand_range(obj.min_def.max(0), obj.max_def.max(1))
        }
        _ => 0,
    }
}

/// Get armor absorption with weapon penetration (VB6: absorbido = armor_def + shield_def - Resist).
pub(super) fn calc_armor_absorption_with_penetration(state: &GameState, conn_id: ConnectionId, body_part: i32, refuerzo: i32) -> i32 {
    let user = match state.users.get(&conn_id) {
        Some(u) => u,
        None => return 0,
    };

    let armor_slot = if body_part == 1 {
        user.equip.helmet
    } else {
        user.equip.armor
    };

    if armor_slot == 0 || armor_slot > MAX_INVENTORY_SLOTS {
        return 0;
    }

    let obj_index = user.inventory[armor_slot - 1].obj_index;
    if obj_index <= 0 {
        return 0;
    }

    let armor_def = match state.get_object(obj_index) {
        Some(obj) if obj.min_def > 0 || obj.max_def > 0 => {
            rand_range(obj.min_def.max(0), obj.max_def.max(1))
        }
        _ => 0,
    };

    // Shield defense bonus (VB6: defbarco)
    let shield_def = if user.equip.shield > 0 && user.equip.shield <= MAX_INVENTORY_SLOTS {
        let shield_idx = user.inventory[user.equip.shield - 1].obj_index;
        match state.get_object(shield_idx) {
            Some(obj) if obj.min_def > 0 || obj.max_def > 0 => {
                rand_range(obj.min_def.max(0), obj.max_def.max(1))
            }
            _ => 0,
        }
    } else {
        0
    };

    // VB6: absorbido = armor_def + shield_def - Resist
    (armor_def + shield_def - refuerzo).max(0)
}

/// Class-based damage modifier (uses BalanceData when available).
pub(super) fn class_damage_modifier_from_balance(state: &GameState, class: &str) -> f64 {
    let bal = &state.game_data.balance;
    let mod_val = bal.mod_dano_clase_armas[
        crate::data::balance::class_name_to_index(class).unwrap_or(0)
    ];
    if mod_val > 0.0 { mod_val as f64 } else { class_damage_modifier(class) }
}

/// Fallback class-based damage modifier.
pub(super) fn class_damage_modifier(class: &str) -> f64 {
    match class.to_lowercase().as_str() {
        "guerrero" => 1.1,
        "cazador" => 0.9,
        "paladin" => 1.0,
        "asesino" => 1.0,
        "ladron" => 0.8,
        "bardo" => 0.8,
        "clerigo" => 0.8,
        "mago" => 0.5,
        "druida" => 0.7,
        "pirata" => 1.0,
        "trabajador" => 0.8,
        "bandido" => 0.9,
        _ => 0.8,
    }
}

/// Handle player death.
pub(super) async fn user_die(state: &mut GameState, conn_id: ConnectionId, killer_id: Option<ConnectionId>) {
    let user_data = match state.users.get(&conn_id) {
        Some(u) => (u.pos_map, u.pos_x, u.pos_y, u.char_index, u.char_name.clone(), u.level),
        None => return,
    };
    let (map, x, y, char_index, victim_name, victim_level) = user_data;

    // Mark as dead, change body to dead model
    if let Some(user) = state.users.get_mut(&conn_id) {
        user.dead = true;
        user.min_hp = 0;
        user.min_sta = 0;
        // Clear status effects
        user.paralyzed = false;
        user.invisible = false;
        user.meditating = false;
        // Dead body model (neutral)
        user.body = DEAD_BODY_NEUTRAL;
        user.head = DEAD_HEAD_NEUTRAL;
        user.weapon_anim = 0;
        user.shield_anim = 0;
        user.casco_anim = 0;
        // Resurrection cooldown (20 ticks before player can be rezzed)
        user.time_revivir = 20;
    }

    // CvC death: don't drop items, just score the death and return
    let en_cvc = state.users.get(&conn_id).map(|u| u.en_cvc).unwrap_or(false);
    if en_cvc && state.cvc_funciona {
        cvc_player_death(state, conn_id).await;
        return;
    }

    // Deequip all items and drop inventory (VB6 UserDie lines 1750-1800)
    if let Some(user) = state.users.get_mut(&conn_id) {
        // Reset equipment slots
        user.equip.weapon = 0;
        user.equip.armor = 0;
        user.equip.shield = 0;
        user.equip.helmet = 0;
        user.equip.municion = 0;
        // Mark inventory items as not equipped
        for slot in user.inventory.iter_mut() {
            slot.equipped = false;
        }
    }

    // Drop non-newbie items on the ground (VB6 TirarTodo)
    // Skip if user is newbie (level < 13) unless criminal
    let is_newbie = victim_level < 13;
    let is_criminal = state.users.get(&conn_id).map(|u| u.criminal).unwrap_or(false);
    if !is_newbie || is_criminal {
        // Collect items to drop
        let mut items_to_drop: Vec<(i32, i32)> = Vec::new(); // (obj_index, amount)
        if let Some(user) = state.users.get(&conn_id) {
            for slot in user.inventory.iter() {
                if slot.obj_index > 0 && slot.amount > 0 {
                    // Check if item is newbie (don't drop newbie items)
                    let is_newbie_item = state.game_data.objects.get((slot.obj_index - 1) as usize)
                        .map(|o| o.newbie)
                        .unwrap_or(false);
                    if !is_newbie_item {
                        items_to_drop.push((slot.obj_index, slot.amount));
                    }
                }
            }
        }

        // Clear dropped items from inventory
        if let Some(user) = state.users.get_mut(&conn_id) {
            for slot in user.inventory.iter_mut() {
                if slot.obj_index > 0 {
                    let is_newbie_item = state.game_data.objects.get((slot.obj_index - 1) as usize)
                        .map(|o| o.newbie)
                        .unwrap_or(false);
                    if !is_newbie_item {
                        slot.obj_index = 0;
                        slot.amount = 0;
                    }
                }
            }
        }

        // Place items on ground near death position (spiral out from death tile)
        let offsets = [(0,0),(1,0),(0,1),(-1,0),(0,-1),(1,1),(-1,1),(1,-1),(-1,-1)];
        let mut off_idx = 0;
        for (obj_idx, amount) in items_to_drop {
            if let Some(obj) = state.game_data.objects.get((obj_idx - 1) as usize) {
                let grh = obj.grh_index;
                // Find next tile that has no ground item
                let mut placed = false;
                for tries in 0..offsets.len() {
                    let idx = (off_idx + tries) % offsets.len();
                    let (ox, oy) = offsets[idx];
                    let tx = x + ox as i32;
                    let ty = y + oy as i32;
                    if tx < 1 || tx > 100 || ty < 1 || ty > 100 { continue; }
                    let tile_free = state.world.grid(map)
                        .and_then(|g| g.tile(tx, ty))
                        .map(|t| t.ground_item.obj_index == 0)
                        .unwrap_or(false);
                    if tile_free {
                        {
                            let grid = state.world.grid_mut(map);
                            if let Some(tile) = grid.tile_mut(tx, ty) {
                                tile.ground_item.obj_index = obj_idx;
                                tile.ground_item.amount = amount;
                            }
                        }
                        let ho_pkt = format!("HO{},{},{}", grh, tx, ty);
                        state.send_data(SendTarget::ToArea { map, x, y }, &ho_pkt).await;
                        off_idx = (idx + 1) % offsets.len();
                        placed = true;
                        break;
                    }
                }
                // If all tiles occupied, items are lost (VB6 behavior)
                if !placed { continue; }
            }
        }
    }

    // Send death notification
    state.send_to(conn_id, server_opcodes::YOU_DIED).await;
    send_stats_hp(state, conn_id).await;

    // VB6: On PK maps, send "MUERT" packet to show death dialog (frmMuertito)
    let map_is_pk = state.game_data.maps.get(map as usize)
        .and_then(|m| m.as_ref())
        .map(|m| m.info.pk)
        .unwrap_or(false);
    if map_is_pk {
        state.send_to(conn_id, "MUERT").await;
    }

    // Broadcast dead body model change (CP packet) to area
    let cp_pkt = format!(
        "CP{},{},{},{},{},{},0,0,{}",
        char_index.0, DEAD_BODY_NEUTRAL, DEAD_HEAD_NEUTRAL,
        state.users.get(&conn_id).map(|u| u.heading).unwrap_or(2),
        0, 0, 0  // weapon, shield, casco = 0
    );
    state.send_data(SendTarget::ToArea { map, x, y }, &cp_pkt).await;

    // Notify area
    let msg = format!("T|12632256\u{00B0}{} ha muerto\u{00B0}{}", victim_name, char_index.0);
    state.send_data(
        SendTarget::ToAreaButIndex { conn_id, map, x, y },
        &msg,
    ).await;

    // Check if the dead user was in a duel or desafio
    let en_duelo = state.users.get(&conn_id).map(|u| u.en_duelo).unwrap_or(false);
    let en_desafio = state.users.get(&conn_id).map(|u| u.en_desafio).unwrap_or(false);

    if en_duelo {
        resolve_duel_death(state, conn_id).await;
    }
    if en_desafio {
        resolve_desafio_death(state, conn_id).await;
    }

    // Check event deaths
    let en_evento = state.users.get(&conn_id).map(|u| u.en_evento).unwrap_or(false);
    let torneo_auto = state.users.get(&conn_id).map(|u| u.torneo_auto).unwrap_or(false);
    if en_evento {
        let kid = killer_id.unwrap_or(0);
        evento_player_death(state, conn_id, kid).await;
    }
    if torneo_auto {
        torneo_auto_death(state, conn_id).await;
    }

    // Award experience to killer
    if let Some(killer) = killer_id {
        let exp_gain = victim_level as i64;
        let killer_name = state.users.get(&killer).map(|u| u.char_name.clone()).unwrap_or_default();

        if let Some(k) = state.users.get_mut(&killer) {
            k.exp += exp_gain;
        }

        // Criminal/citizen reputation tracking on PvP kill (VB6: ContarMuerte)
        // Kill deduplication: only count if different from last kill of same faction
        let victim_criminal = state.users.get(&conn_id).map(|u| u.criminal).unwrap_or(false);
        let victim_name_upper = victim_name.to_uppercase();
        if victim_criminal {
            // Killed a criminal — increment criminales_matados (with dedup)
            let last = state.users.get(&killer).map(|k| k.last_crim_matado.clone()).unwrap_or_default();
            if last != victim_name_upper {
                if let Some(k) = state.users.get_mut(&killer) {
                    k.last_crim_matado = victim_name_upper;
                    if k.criminales_matados < 65000 {
                        k.criminales_matados += 1;
                    }
                }
            }
        } else {
            // Killed a citizen — increment ciudadanos_matados (with dedup), become criminal
            let last = state.users.get(&killer).map(|k| k.last_ciud_matado.clone()).unwrap_or_default();
            if last != victim_name_upper {
                if let Some(k) = state.users.get_mut(&killer) {
                    k.last_ciud_matado = victim_name_upper;
                    if k.ciudadanos_matados < 65000 {
                        k.ciudadanos_matados += 1;
                    }
                    k.criminal = true;
                }
            } else {
                // Still become criminal even if dedup prevents counter increment
                if let Some(k) = state.users.get_mut(&killer) {
                    k.criminal = true;
                }
            }
            // Broadcast appearance change (criminal status)
            let (km, kx, ky) = state.users.get(&killer)
                .map(|u| (u.pos_map, u.pos_x, u.pos_y))
                .unwrap_or((0, 0, 0));
            if km > 0 {
                if let Some(k) = state.users.get(&killer) {
                    let cp = format!(
                        "CP{},{},{},{},{},{},0,0,{}",
                        k.char_index.0, k.body, k.head, k.heading,
                        k.weapon_anim, k.shield_anim, k.casco_anim
                    );
                    state.send_data(SendTarget::ToArea { map: km, x: kx, y: ky }, &cp).await;
                }
            }
        }

        // Check quest kill tracking (pass victim conn_id for trigger zone check)
        quest_check_player_kill(state, killer, conn_id).await;

        // Notify killer (VB6: ||60@name@class + ||170@exp)
        state.send_to(killer, &format!("||60@{}@{}", victim_name, exp_gain)).await;
        state.send_to(killer, &format!("||170@{}", exp_gain)).await;
        send_stats_exp(state, killer).await;
        check_user_level(state, killer).await;

        info!("[COMBAT] '{}' killed '{}' (+{} exp)", killer_name, victim_name, exp_gain);
    }
}
