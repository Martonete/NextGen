//! NPC system handlers: area visibility, NPC combat, NPC death, item drops.
//! Extracted from mod.rs to reduce file size.

use tracing::info;
use crate::net::ConnectionId;
use crate::game::types::{GameState, SendTarget, privilege_level};
use crate::game::world;
use crate::game::npc;
use crate::protocol::binary_packets;
use crate::data::experience::MAX_LEVEL;
use super::common::*;
use crate::game::types::MAX_INVENTORY_SLOTS;
use crate::protocol::font_index;
use super::{
    user_die, check_user_level, send_inventory_slot,
    calc_attack_power, calc_defense_power, calc_armor_absorption,
    class_damage_modifier,
    poder_ataque_arma, poder_ataque_proyectil, poder_ataque_wrestling,
    calcular_dano, get_weapon_info, get_ring_info,
    do_apunalar, do_golpe_critico, puede_apunalar,
    pretoriano_check_death, es_pretoriano,
    remove_pet_from_owner,
};
use super::skills::try_level_skill_with_hit;

// =====================================================================
// NPC system — spawning, AI, combat
// =====================================================================

/// Send CC packets for all NPCs in the area around (x, y) on map to a specific user.
pub(super) async fn send_area_npc_ccs(state: &mut GameState, conn_id: ConnectionId, map: i32, x: i32, y: i32) {
    let min_y = (y - world::MIN_Y_BORDER + 1).max(1);
    let max_y = (y + world::MIN_Y_BORDER - 1).min(world::MAP_HEIGHT as i32);
    let min_x = (x - world::MIN_X_BORDER + 1).max(1);
    let max_x = (x + world::MIN_X_BORDER - 1).min(world::MAP_WIDTH as i32);

    // Collect NPC CCs first to avoid borrow issues
    let mut npc_ccs: Vec<Vec<u8>> = Vec::new();
    if let Some(grid) = state.world.grid(map) {
        for ny in min_y..=max_y {
            for nx in min_x..=max_x {
                if let Some(tile) = grid.tile(nx, ny) {
                    if tile.npc_index > 0 {
                        if let Some(npc) = state.get_npc(tile.npc_index as usize) {
                            if npc.is_alive() {
                                npc_ccs.push(npc.build_cc_binary());
                            }
                        }
                    }
                }
            }
        }
    }

    for cc in &npc_ccs {
        state.send_bytes(conn_id, cc).await;
    }
}

/// Send ground item visuals (HO) in the area to a user.
pub(super) async fn send_area_ground_items(state: &mut GameState, conn_id: ConnectionId, map: i32, x: i32, y: i32) {
    let min_y = (y - world::MIN_Y_BORDER + 1).max(1);
    let max_y = (y + world::MIN_Y_BORDER - 1).min(world::MAP_HEIGHT as i32);
    let min_x = (x - world::MIN_X_BORDER + 1).max(1);
    let max_x = (x + world::MIN_X_BORDER - 1).min(world::MAP_WIDTH as i32);

    let mut ho_packets: Vec<Vec<u8>> = Vec::new();
    if let Some(grid) = state.world.grid(map) {
        for gy in min_y..=max_y {
            for gx in min_x..=max_x {
                if let Some(tile) = grid.tile(gx, gy) {
                    let obj_idx = tile.ground_item.obj_index;
                    if obj_idx > 0 {
                        let grh = state.get_object(obj_idx)
                            .map(|o| o.grh_index)
                            .unwrap_or(0);
                        if grh > 0 {
                            ho_packets.push(binary_packets::write_object_create(gx as u8, gy as u8, grh as i16));
                        }
                    }
                }
            }
        }
    }

    for ho in ho_packets {
        state.send_bytes(conn_id, &ho).await;
    }
}

/// VB6 PuedeAtacarNPC — validate whether a player can attack this NPC.
/// Returns true if attack is allowed, false if blocked (sends feedback to player).
pub(super) async fn puede_atacar_npc(
    state: &mut GameState,
    conn_id: ConnectionId,
    npc_idx: usize,
) -> bool {
    let npc = match state.get_npc(npc_idx) {
        Some(n) => n,
        None => return false,
    };

    // NPC must be alive
    if !npc.is_alive() {
        return false;
    }

    // NPC must be attackable (VB6: Attackable flag in NPCs.dat)
    if !npc.attackable {
        state.send_msg_id(conn_id, 140, "").await; // "No puedes atacar a este NPC"
        return false;
    }

    let npc_number = npc.npc_number as i32;
    let npc_map = npc.map;
    let npc_type = npc.npc_type;

    // Get attacker data
    let user = match state.users.get(&conn_id) {
        Some(u) => u,
        None => return false,
    };

    // VB6: Dead users can't attack
    if user.dead {
        state.send_msg_id(conn_id, 3, "").await; // "Estas muerto"
        return false;
    }

    // VB6: Privilegios > User AND Privilegios <= GranDios can't attack NPCs
    // Levels 1-8 (Consejero..GranDios) are blocked; 9+ (Developer, SubAdmin, Admin) CAN attack
    if user.privileges > privilege_level::USER && user.privileges <= privilege_level::GRAN_DIOS {
        return false;
    }

    let is_horda = user.fuerzas_caos;
    let is_alianza = user.armada_real;
    let user_guild_index = user.guild_index;

    // VB6: Horde faction can't attack NPCs 617 and 948 (faction-aligned NPCs)
    if (npc_number == 617 || npc_number == 948) && is_horda {
        state.send_msg_id(conn_id, 167, "").await; // "No puedes atacar a un NPC de tu facción"
        return false;
    }

    // VB6: Alliance faction can't attack NPCs 618 and 947
    if (npc_number == 618 || npc_number == 947) && is_alianza {
        state.send_msg_id(conn_id, 167, "").await;
        return false;
    }

    // VB6: ARAM team restrictions (NPC 963 = blue tower, 964 = red tower)
    // Can't attack your own team's tower
    // TODO: when ARAM event team field is added to UserState, check:
    //   if npc_number == 963 && user.aram_team == ARAM_RED { block }
    //   if npc_number == 964 && user.aram_team == ARAM_BLUE { block }

    // VB6: Mithrandir status restrictions (NPCs 966/967)
    // TODO: when Mithrandir system is implemented, check:
    //   if npc_number == 966 && (user.status_mith == 1 || is_alianza) { block }
    //   if npc_number == 967 && (user.status_mith == 2 || is_horda) { block }

    // VB6: Castle King / NPC 615 — guild ownership checks
    if npc_type == crate::data::npcs::NpcType::CastleKing || npc_number == 615 {
        // Must be in a guild to attack castle kings
        if user_guild_index <= 0 {
            state.send_msg_id(conn_id, 120, "").await; // "Necesitas pertenecer a un clan"
            return false;
        }

        // Can't attack your own guild's castle king
        // Check if this NPC's map belongs to a castle owned by the attacker's guild
        if let Some(castle_owner_guild) = state.get_castle_owner_guild(npc_map) {
            if castle_owner_guild == user_guild_index {
                state.send_msg_id(conn_id, 169, "").await; // "No puedes atacar al rey de tu propio castillo"
                return false;
            }
        }
    }

    true
}

/// Player attacks an NPC (PvE melee combat).
#[allow(clippy::too_many_arguments)]
pub(super) async fn user_attack_npc(
    state: &mut GameState,
    conn_id: ConnectionId,
    npc_idx: usize,
    map: i32,
    x: i32,
    y: i32,
    strength: i32,
    agility: i32,
    level: i32,
    min_hit: i32,
    max_hit: i32,
    skill_armas: i32,
    attacker_name: &str,
    class: &str,
) {
    // VB6 PuedeAtacarNPC — full validation
    if !puede_atacar_npc(state, conn_id, npc_idx).await {
        return;
    }

    // Get NPC data (safe after puede_atacar_npc validated alive + attackable)
    let npc_data = match state.get_npc(npc_idx) {
        Some(n) => (n.poder_evasion, n.char_index, n.name.clone(), n.give_exp, n.max_hp),
        None => return,
    };
    let (npc_evasion, npc_char_index, npc_name, npc_give_exp, npc_max_hp) = npc_data;

    // VB6: UserImpactoNpc — determine weapon type and calculate attack power
    let weapon_info = get_weapon_info(state, conn_id);
    let (attack_power, attack_skill_idx) = if weapon_info.obj_index > 0 {
        if weapon_info.is_proyectil {
            let mod_atk = state.game_data.balance.class_mod_ataque_proyectiles(class);
            (poder_ataque_proyectil(
                state.users.get(&conn_id).map(|u| u.skills[5]).unwrap_or(0),
                agility, level, mod_atk,
            ), 5usize)
        } else {
            let mod_atk = state.game_data.balance.class_mod_ataque_armas(class);
            (poder_ataque_arma(skill_armas, agility, level, mod_atk), 1usize)
        }
    } else {
        let mod_atk = state.game_data.balance.class_mod_ataque_wrestling(class);
        let wrestling_sk = state.users.get(&conn_id).map(|u| u.skills[20]).unwrap_or(0);
        (poder_ataque_wrestling(wrestling_sk, agility, level, mod_atk), 20usize)
    };

    let defense_power = npc_evasion as i64;
    let hit_prob = ((50.0 + (attack_power - defense_power) as f64 * 0.4) as i32).clamp(10, 90);
    let hit = rand_range(1, 100) <= hit_prob;

    // VB6: SubirSkill on hit/miss
    if let Some(u) = state.users.get_mut(&conn_id) {
        try_level_skill_with_hit(u, attack_skill_idx, hit);
    }

    if !hit {
        let snd = binary_packets::write_play_wave(2, x as u8, y as u8);
        state.send_data_bytes(SendTarget::ToArea { map, x, y }, &snd).await;
        let pkt = binary_packets::write_multi_msg_simple(crate::protocol::packets::MultiMessageID::UserSwing);
        state.send_bytes(conn_id, &pkt).await;
        state.send_chat_over_head_to(SendTarget::ToArea { map, x, y }, "\u{00A1}Fallo!", npc_char_index.0 as i16, 255).await;
        return;
    }

    // VB6: CalcularDaño(UserIndex, NpcIndex) — with proper weapon type class modifier
    let class_mod_damage = if weapon_info.obj_index > 0 {
        if weapon_info.is_proyectil {
            state.game_data.balance.class_mod_dano_proyectiles(class) as f64
        } else {
            state.game_data.balance.class_mod_dano_armas(class) as f64
        }
    } else {
        state.game_data.balance.class_mod_dano_wrestling(class) as f64
    };

    let (ring_idx, ring_guante, ring_min, ring_max) = get_ring_info(state, conn_id);
    let base_damage = calcular_dano(
        weapon_info.obj_index, weapon_info.is_proyectil,
        weapon_info.min_hit, weapon_info.max_hit,
        weapon_info.has_ammo, weapon_info.ammo_min_hit, weapon_info.ammo_max_hit,
        min_hit, max_hit,
        strength, class_mod_damage,
        ring_idx, ring_guante, ring_min, ring_max,
    );

    // VB6: UserDañoNpc — boat damage bonus
    let boat_bonus = if state.users.get(&conn_id).map(|u| u.navigating).unwrap_or(false) {
        let boat_slot = state.users.get(&conn_id).map(|u| u.barco_slot).unwrap_or(0);
        if boat_slot > 0 && boat_slot <= MAX_INVENTORY_SLOTS {
            let boat_idx = state.users.get(&conn_id).map(|u| u.inventory[boat_slot - 1].obj_index).unwrap_or(0);
            match state.get_object(boat_idx) {
                Some(obj) => rand_range(obj.min_hit.max(0), obj.max_hit.max(0)) as i64,
                None => 0,
            }
        } else { 0 }
    } else { 0 };

    let damage_before_def = base_damage + boat_bonus;

    // VB6: damage = DañoBase - NPC.Stats.def
    let npc_def = state.get_npc(npc_idx).map(|n| n.def).unwrap_or(0) as i64;
    let damage = (damage_before_def - npc_def).max(0) as i32;

    // Check attacker GM status BEFORE taking mutable NPC borrow
    let attacker_is_gm = state.users.get(&conn_id)
        .map(|u| u.privileges > 0)
        .unwrap_or(false);

    // Apply damage to NPC
    let (npc_dead, npc_give_exp, npc_give_gld_min, npc_give_gld_max) = {
        match state.get_npc_mut(npc_idx) {
            Some(npc) => {
                npc.min_hp -= damage;

                // Track damage for proportional EXP
                npc.damage_received.push((conn_id, damage));

                // NpcAtacado trigger (VB6): if NPC is not hostile, switch to defense mode
                // GMs (privileges > 0) should NOT become NPC targets
                if !attacker_is_gm {
                    if !npc.hostile && npc.movement != npc::AI_DEFENSE {
                        npc.old_movement = npc.movement;
                        npc.old_hostile = npc.hostile;
                        npc.movement = npc::AI_DEFENSE;
                        npc.hostile = true;
                        npc.attacked_by = attacker_name.to_string();
                        npc.target = Some(conn_id);
                    } else if npc.target.is_none() {
                        // Already hostile — just set target
                        npc.target = Some(conn_id);
                    }
                }

                let dead = npc.min_hp <= 0;
                (dead, npc.give_exp, npc.give_gld_min, npc.give_gld_max)
            }
            None => return,
        }
    };

    // Send hit feedback: U2 to attacker (you hit NPC)
    let u2_pkt = binary_packets::write_multi_user_hit_npc(damage);
    state.send_bytes(conn_id, &u2_pkt).await;

    // VB6: floating yellow damage number above NPC (vbYellow=65535)
    state.send_chat_over_head_to(SendTarget::ToArea { map, x, y }, &format!("-{}", damage), npc_char_index.0 as i16, 65535).await;

    // VB6: NPC Snd1 (attack sound) + SND_IMPACTO + Snd2 (victim hurt sound, fallback SND_IMPACTO2=12)
    let (npc_snd1, npc_snd2) = state.get_npc(npc_idx)
        .map(|n| (n.snd1, n.snd2))
        .unwrap_or((0, 0));
    if npc_snd1 > 0 {
        let snd = binary_packets::write_play_wave(npc_snd1 as u8, x as u8, y as u8);
        state.send_data_bytes(SendTarget::ToArea { map, x, y }, &snd).await;
    }
    let snd = binary_packets::write_play_wave(10, x as u8, y as u8);
    state.send_data_bytes(SendTarget::ToArea { map, x, y }, &snd).await;
    if npc_snd2 > 0 {
        let snd = binary_packets::write_play_wave(npc_snd2 as u8, x as u8, y as u8);
        state.send_data_bytes(SendTarget::ToArea { map, x, y }, &snd).await;
    } else {
        let snd = binary_packets::write_play_wave(12, x as u8, y as u8);
        state.send_data_bytes(SendTarget::ToArea { map, x, y }, &snd).await;
    }

    // Blood FX on NPC
    let fx_pkt = binary_packets::write_create_fx(npc_char_index.0 as i16, 14, 0); // VB6: FXSANGRE = 14
    state.send_data_bytes(SendTarget::ToArea { map, x, y }, &fx_pkt).await;

    // VB6: If NPC still alive after initial hit, try backstab and critical
    let npc_still_alive = state.get_npc(npc_idx).map(|n| n.min_hp > 0).unwrap_or(false);
    if npc_still_alive {
        let user_heading = state.users.get(&conn_id).map(|u| u.heading).unwrap_or(0);
        let npc_heading = state.get_npc(npc_idx).map(|n| n.heading).unwrap_or(0);
        let apunalar_sk = state.users.get(&conn_id).map(|u| u.skills[8]).unwrap_or(0);

        // VB6: DoApuñalar — backstab (NPC target gets 2x damage)
        if puede_apunalar(class, user_heading, npc_heading) && apunalar_sk > 0 {
            // VB6: Assassin ignores NPC defense for backstab base damage
            let stab_base = if class.eq_ignore_ascii_case("Asesino") {
                damage_before_def as i64 // Ignore defense for Assassin
            } else {
                damage as i64
            };

            if let Some(stab_dmg) = do_apunalar(apunalar_sk, class, stab_base, true) {
                if let Some(npc) = state.get_npc_mut(npc_idx) {
                    npc.min_hp -= stab_dmg as i32;
                    npc.damage_received.push((conn_id, stab_dmg as i32));
                }
                state.send_console(conn_id, &format!("Has apuñalado la criatura por {}", stab_dmg), font_index::FIGHT).await;
                if let Some(u) = state.users.get_mut(&conn_id) {
                    try_level_skill_with_hit(u, 8, true);
                }
            } else {
                state.send_console(conn_id, "\u{00A1}No has logrado apuñalar a tu enemigo!", font_index::FIGHT).await;
                if let Some(u) = state.users.get_mut(&conn_id) {
                    try_level_skill_with_hit(u, 8, false);
                }
            }
        }

        // VB6: DoGolpeCritico (Bandido + Espada Vikinga only)
        let wrestling_sk = state.users.get(&conn_id).map(|u| u.skills[20]).unwrap_or(0);
        if let Some(crit_dmg) = do_golpe_critico(class, weapon_info.obj_index, wrestling_sk, damage as i64) {
            if let Some(npc) = state.get_npc_mut(npc_idx) {
                npc.min_hp -= crit_dmg as i32;
                npc.damage_received.push((conn_id, crit_dmg as i32));
            }
            state.send_console(conn_id, &format!("Has golpeado críticamente a la criatura por {}.", crit_dmg), font_index::FIGHT).await;
        }
    }

    // Re-check dead status after backstab/crit
    let npc_dead = state.get_npc(npc_idx).map(|n| n.min_hp <= 0).unwrap_or(false);

    // Per-hit EXP (VB6: CalcularDarExp — gives proportional exp on EVERY hit, not just on death)
    if npc_give_exp > 0 && npc_max_hp > 0 {
        let exp_mult = state.multiplicador_exp;
        let exp_award = ((npc_give_exp as f64 / npc_max_hp as f64) * damage as f64 * exp_mult as f64) as i64;

        // Level cap check
        let can_level = state.users.get(&conn_id)
            .map(|u| u.logged && u.level < MAX_LEVEL as i32)
            .unwrap_or(false);

        if can_level && exp_award > 0 {
            if let Some(user) = state.users.get_mut(&conn_id) {
                user.exp += exp_award;
            }
            // Send msg 170 notification (VB6: "Has ganado %1 puntos de experiencia")
            state.send_msg_id(conn_id, 170, &exp_award.to_string()).await;
            send_stats_exp(state, conn_id).await;
            check_user_level(state, conn_id).await;
        }
    }

    if npc_dead {
        npc_die(state, npc_idx, conn_id, npc_give_exp, npc_give_gld_min, npc_give_gld_max).await;
    }
}

/// NPC dies — remove from world, reward players proportionally, schedule respawn.
/// VB6: MuereNpc (modNPC.bas) — death sound, gold to inventory, crystal drops, faction points.
pub(super) async fn npc_die(
    state: &mut GameState,
    npc_idx: usize,
    killer_id: ConnectionId,
    give_exp: i32,
    give_gld_min: i32,
    give_gld_max: i32,
) {
    let npc_info = match state.get_npc(npc_idx) {
        Some(n) => (n.map, n.x, n.y, n.char_index, n.name.clone(), n.npc_number,
                    n.snd3, n.give_pts, n.cristales,
                    n.crystal_min1, n.crystal_max1, n.crystal_min2, n.crystal_max2,
                    n.crystal_min3, n.crystal_max3, n.crystal_min4, n.crystal_max4,
                    n.maestro_user),
        None => return,
    };
    let (map, x, y, char_index, npc_name, npc_number,
         snd3, give_pts, has_crystals,
         cr_min1, cr_max1, cr_min2, cr_max2,
         cr_min3, cr_max3, cr_min4, cr_max4,
         is_pet_owner) = npc_info;

    // 1) Death sound (VB6: TW{snd3})
    if snd3 > 0 {
        let snd_pkt = binary_packets::write_play_wave(snd3 as u8, x as u8, y as u8);
        state.send_data_bytes(SendTarget::ToArea { map, x, y }, &snd_pkt).await;
    }

    // 2) Send msg 50 to killer (NPC death notification — client plays sound/animation)
    state.send_msg_id(killer_id, 50, "").await;

    // 3) Drop items (NPC_TIRAR_ITEMS) — only if not a pet
    let is_pet = is_pet_owner.is_some();
    if !is_pet {
        npc_drop_items(state, npc_idx, killer_id, map, x, y).await;
    }

    // 4) Remove NPC character from area (BP packet)
    let bp_pkt = binary_packets::write_character_remove(char_index.0 as i16);
    state.send_data_bytes(SendTarget::ToArea { map, x, y }, &bp_pkt).await;

    // Kill NPC (remove from grid, mark inactive)
    state.kill_npc(npc_idx);

    // Check praetorian death
    if es_pretoriano(npc_number) {
        pretoriano_check_death(state, npc_idx);
    }

    // EXP is now given per-hit via CalcularDarExp in user_attack_npc().
    // No death-time exp distribution needed (VB6 parity: exp per hit, not per kill).

    // 6) Gold to killer's inventory (VB6: gold goes to player, NOT floor)
    let gold_mult = state.multiplicador_oro;
    let gld_min = (give_gld_min as i64) * (gold_mult as i64);
    let gld_max = (give_gld_max as i64) * (gold_mult as i64);
    let gold_award = if gld_max > gld_min {
        rand_range(gld_min as i32, gld_max as i32) as i64
    } else {
        gld_min
    };
    if gold_award > 0 {
        if let Some(user) = state.users.get_mut(&killer_id) {
            user.gold += gold_award;
        }
        send_stats_gold(state, killer_id).await;
    }

    // 7) Faction points (VB6: GivePTS)
    if give_pts > 0 {
        if let Some(user) = state.users.get_mut(&killer_id) {
            if user.armada_real {
                user.recompensas_real = (user.recompensas_real + give_pts).min(999);
            } else if user.fuerzas_caos {
                user.recompensas_caos = (user.recompensas_caos + give_pts).min(999);
            }
        }
    }

    // 8) Pet cleanup — if NPC was someone's pet, remove from owner
    if let Some(owner_conn) = is_pet_owner {
        remove_pet_from_owner(state, owner_conn, npc_idx);
    }

    // 9) Notify killer (VB6: ||50 + ||56@gold)
    // Note: ||50 already sent at step 2. ||170 exp is now per-hit (CalcularDarExp).
    if gold_award > 0 {
        state.send_msg_id(killer_id, 56, &gold_award.to_string()).await; // TEXTO56: La criatura ha dejado %1 monedas
    }

    info!("[NPC] '{}' killed NPC '{}' (idx={}, +{} exp, +{} gold)",
          state.users.get(&killer_id).map(|u| u.char_name.as_str()).unwrap_or("?"),
          npc_name, npc_idx, give_exp, gold_award);
}


/// Drop NPC inventory items on death.
/// VB6: NPC_TIRAR_ITEMS (Modulo_InventANDobj.bas).
/// Probability: ProbTirar * 2, capped at 200. Roll 1-200, if <= prob → drop.
/// Charisma bonus: >=20 adds luck.
pub(super) async fn npc_drop_items(
    state: &mut GameState,
    npc_idx: usize,
    killer_id: ConnectionId,
    map: i32,
    npc_x: i32,
    npc_y: i32,
) {
    // Get NPC inventory before killing
    let npc_inv: Vec<(i32, i32, i32)> = match state.get_npc(npc_idx) {
        Some(n) => n.inventory.iter()
            .filter(|slot| slot.obj_index > 0 && slot.amount > 0)
            .map(|slot| (slot.obj_index, slot.amount, slot.prob_tirar))
            .collect(),
        None => return,
    };

    if npc_inv.is_empty() {
        return;
    }

    // Get killer's charisma for luck bonus
    let charisma = state.users.get(&killer_id)
        .map(|u| u.attributes[3]) // [3] = Charisma
        .unwrap_or(18);

    // Charisma luck modifier
    let luck_mod = match charisma {
        0..=18 => -1,
        19 => 0,
        20 => 1,
        21 => 2,
        c => (c - 21).max(0),
    };

    // Drop multiplier
    let drop_mult = state.multiplicador_drop;

    for (obj_index, amount, prob_tirar) in npc_inv {
        if prob_tirar <= 0 {
            continue;
        }

        // Calculate drop probability (VB6: ProbTirar * 2 * mult_drop, cap 200)
        let prob = ((prob_tirar * 2 + luck_mod) * drop_mult).max(1).min(200);

        // Roll
        let roll = random_number(1, 200);
        if roll <= prob {
            // Drop item — use actual amount from NPC inventory (VB6 drops full amount)
            let drop_amount = amount.max(1);

            // Find a free tile near the NPC
            let (dx, dy) = find_free_tile(state, map, npc_x, npc_y);
            let drop_x = npc_x + dx;
            let drop_y = npc_y + dy;

            // Place item on ground
            let grid = state.world.grid_mut(map);
            if let Some(tile) = grid.tile_mut(drop_x, drop_y) {
                if tile.ground_item.obj_index == 0 {
                    tile.ground_item.obj_index = obj_index;
                    tile.ground_item.amount = drop_amount;
                }
            }

            // Get GRH for the object
            let grh = state.get_object(obj_index).map(|o| o.grh_index).unwrap_or(0);
            if grh > 0 {
                let ho_pkt = binary_packets::write_object_create(drop_x as u8, drop_y as u8, grh as i16);
                state.send_data_bytes(SendTarget::ToArea { map, x: npc_x, y: npc_y }, &ho_pkt).await;
            }

            // Track for world cleanup
            clean_world_add_item(state, map, drop_x, drop_y, 10, obj_index);
        }
    }
}

/// Find a free adjacent tile for item drop. Returns (dx, dy) offset.
// find_free_tile — moved to common.rs

/// NPC attacks a player.
pub(super) async fn npc_attack_user(state: &mut GameState, npc_idx: usize, target_conn: ConnectionId) {
    let npc_data = match state.get_npc(npc_idx) {
        Some(n) if n.is_alive() => {
            (n.poder_ataque, n.min_hit, n.max_hit, n.char_index, n.map, n.x, n.y, n.name.clone(),
             n.body, n.head, n.heading)
        }
        _ => return,
    };
    let (npc_ataque, npc_min_hit, npc_max_hit, npc_char_index, map, nx, ny, npc_name,
         npc_body, npc_head, _old_heading) = npc_data;

    // VB6: ChangeNPCChar — update heading to face target before attacking
    let target_pos = state.users.get(&target_conn).map(|u| (u.pos_x, u.pos_y));
    if let Some((tx, ty)) = target_pos {
        let new_heading = if ty < ny { world::HEADING_NORTH }
            else if ty > ny { world::HEADING_SOUTH }
            else if tx > nx { world::HEADING_EAST }
            else { world::HEADING_WEST };
        if let Some(npc) = state.get_npc_mut(npc_idx) {
            npc.heading = new_heading;
        }
        // Send CP packet to area (VB6: ChangeNPCChar sends CP<charindex>,<body>,<head>,<heading>)
        let cp_pkt = binary_packets::write_character_change(
            npc_char_index.0 as i16, npc_body as i16, npc_head as i16,
            new_heading as u8, 0, 0, 0, 0, 0,
        );
        state.send_data_bytes(SendTarget::ToArea { map, x: nx, y: ny }, &cp_pkt).await;
    }

    let user_data = match state.users.get(&target_conn) {
        // VB6 NpcAtacaUser: AdminInvisible=1 or Privilegios<>User → exit (no attack)
        Some(u) if u.logged && !u.dead && u.privileges == 0 && !u.admin_invisible => {
            (u.attributes[1], // Agility
             u.skills[3],     // SK4 = Tacticas
             u.level, u.char_index)
        }
        _ => return,
    };
    let (u_agility, u_tacticas, u_level, u_char_index) = user_data;

    // Hit/miss calculation
    let npc_power = npc_ataque as f64;
    let user_evasion = calc_defense_power(u_tacticas, u_agility, u_level);
    let hit_prob = ((50.0 + (npc_power - user_evasion) * 0.4) as i32).clamp(10, 90);

    if rand_range(1, 100) > hit_prob {
        // Miss — VB6: SND_SWING to area + N1
        let snd = binary_packets::write_play_wave(2, nx as u8, ny as u8);
        state.send_data_bytes(SendTarget::ToArea { map, x: nx, y: ny }, &snd).await;
        let pkt = binary_packets::write_multi_msg_simple(crate::protocol::packets::MultiMessageID::NPCSwing);
        state.send_bytes(target_conn, &pkt).await;
        // VB6: floating red "¡Fallo!" above user (N| vbRed°¡Fallo!°charIndex)
        state.send_chat_over_head_to(SendTarget::ToArea { map, x: nx, y: ny }, "\u{00A1}Fallo!", u_char_index.0 as i16, 255).await;
        return;
    }

    // Damage + armor absorption (VB6: NpcDaño)
    let body_part = rand_range(1, 6);
    let raw_damage = rand_range(npc_min_hit.max(1), npc_max_hit.max(1));
    let absorption = calc_armor_absorption(state, target_conn, body_part);
    let damage = (raw_damage - absorption).max(1);

    // Apply damage
    if let Some(user) = state.users.get_mut(&target_conn) {
        user.min_hp -= damage;
    }
    let n2_pkt = binary_packets::write_multi_npc_hit_user(body_part as u8, damage as i16);
    state.send_bytes(target_conn, &n2_pkt).await;

    // VB6: floating yellow damage number above user (N| vbYellow°-<damage>°charIndex)
    state.send_chat_over_head_to(SendTarget::ToArea { map, x: nx, y: ny }, &format!("-{}", damage), u_char_index.0 as i16, 65535).await;

    // Blood FX on player (VB6: FXSANGRE = 14)
    let fx_pkt = binary_packets::write_create_fx(u_char_index.0 as i16, 14, 0);
    state.send_data_bytes(SendTarget::ToArea { map, x: nx, y: ny }, &fx_pkt).await;

    // VB6: NPC attack sound (Snd1) + victim hit sound (Snd2 or SND_IMPACTO2=12)
    let (npc_snd1, npc_snd2) = state.get_npc(npc_idx)
        .map(|n| (n.snd1, n.snd2))
        .unwrap_or((0, 0));
    if npc_snd1 > 0 {
        let snd = binary_packets::write_play_wave(npc_snd1 as u8, nx as u8, ny as u8);
        state.send_data_bytes(SendTarget::ToArea { map, x: nx, y: ny }, &snd).await;
    }
    // SND_IMPACTO to area on hit
    let snd = binary_packets::write_play_wave(10, nx as u8, ny as u8);
    state.send_data_bytes(SendTarget::ToArea { map, x: nx, y: ny }, &snd).await;
    // Victim sound: Snd2 if defined, else SND_IMPACTO2 (12)
    if npc_snd2 > 0 {
        let snd = binary_packets::write_play_wave(npc_snd2 as u8, nx as u8, ny as u8);
        state.send_data_bytes(SendTarget::ToArea { map, x: nx, y: ny }, &snd).await;
    } else {
        let snd = binary_packets::write_play_wave(12, nx as u8, ny as u8);
        state.send_data_bytes(SendTarget::ToArea { map, x: nx, y: ny }, &snd).await;
    }

    // NPC poison on hit (VB6: If Npclist(NpcIndex).Veneno = 1 Then NpcEnvenenarUser)
    let npc_veneno = state.get_npc(npc_idx).map(|n| n.veneno).unwrap_or(false);
    if npc_veneno {
        let already_poisoned = state.users.get(&target_conn).map(|u| u.poisoned).unwrap_or(true);
        if !already_poisoned {
            if let Some(user) = state.users.get_mut(&target_conn) {
                user.poisoned = true;
                user.counter_poison = 0;
            }
            state.send_msg_id(target_conn, 171, &npc_name).await;
        }
    }

    // VB6: CheckPets — user's pets react to NPC attacking their master
    check_pets(state, npc_idx, target_conn, true);

    // Update HP
    send_stats_hp(state, target_conn).await;

    // Check death
    let hp = state.users.get(&target_conn).map(|u| u.min_hp).unwrap_or(0);
    if hp <= 0 {
        user_die(state, target_conn, None).await;
    }
}

/// NPC casts a spell on a user (VB6: NpcLanzaSpellSobreUser).
pub(super) async fn npc_cast_spell(state: &mut GameState, npc_idx: usize, target_conn: ConnectionId, spell_id: i32) {
    let spell = match state.get_spell(spell_id) {
        Some(s) => s.clone(),
        None => return,
    };

    let npc_data = match state.get_npc(npc_idx) {
        Some(n) if n.is_alive() => (n.char_index, n.map, n.x, n.y, n.name.clone()),
        _ => return,
    };
    let (npc_char, map, nx, ny, npc_name) = npc_data;

    let target_alive = state.users.get(&target_conn)
        .map(|u| u.logged && !u.dead)
        .unwrap_or(false);
    if !target_alive { return; }

    // Magic words broadcast (spell name)
    if !spell.palabras_magicas.is_empty() {
        let msg = format!("{} dice: {}", npc_name, spell.palabras_magicas);
        state.send_chat_over_head_to(SendTarget::ToArea { map, x: nx, y: ny }, &msg, npc_char.0 as i16, 16711680).await; // vbRed
    }

    // FX on target
    let target_ci = state.users.get(&target_conn).map(|u| u.char_index.0).unwrap_or(0);
    if spell.fx_grh > 0 {
        let fx = binary_packets::write_create_fx(target_ci as i16, spell.fx_grh as i16, spell.loops as i16);
        state.send_data_bytes(SendTarget::ToArea { map, x: nx, y: ny }, &fx).await;
    }
    if spell.wav > 0 {
        let snd = binary_packets::write_play_wave(spell.wav as u8, nx as u8, ny as u8);
        state.send_data_bytes(SendTarget::ToArea { map, x: nx, y: ny }, &snd).await;
    }

    // Spell effect
    match spell.sube_hp {
        1 => {
            // Heal spell (SubeHP=1) — when cast on a user target, NPC heals ITSELF
            // (VB6: NpcLanzaSpellSobreUser with SubeHP=1 heals the NPC, not the user)
            let heal = rand_range(spell.min_hp.max(1), spell.max_hp.max(1));
            if let Some(npc) = state.get_npc_mut(npc_idx) {
                npc.min_hp = (npc.min_hp + heal).min(npc.max_hp);
            }
        }
        2 => {
            // Damage spell (SubeHP=2)
            let mut damage = rand_range(spell.min_hp.max(1), spell.max_hp.max(1));

            // Armor magic defense reduction (simplified — use armor absorption)
            let absorption = calc_armor_absorption(state, target_conn, rand_range(1, 6));
            damage = (damage - absorption / 2).max(1); // Magic has half physical absorption

            // Apply damage
            if let Some(user) = state.users.get_mut(&target_conn) {
                user.min_hp -= damage;
            }

            // Send damage console message (VB6: msg 830 — NpcName@Damage)
            state.send_msg_id(target_conn, 830, &format!("{}@{}", npc_name, damage)).await;
            send_stats_hp(state, target_conn).await;

            // Check death
            let hp = state.users.get(&target_conn).map(|u| u.min_hp).unwrap_or(0);
            if hp <= 0 {
                user_die(state, target_conn, None).await;
            }
        }
        _ => {}
    }

    // Paralysis effect
    if spell.paraliza {
        if let Some(user) = state.users.get_mut(&target_conn) {
            user.paralyzed = true;
            user.counter_paralisis = state.config.intervalo_paralizado;
        }
        let duration_secs = (state.config.intervalo_paralizado as f32 * 0.04) as i16;
        let pkt = binary_packets::write_paralize_ok(duration_secs);
        state.send_bytes(target_conn, &pkt).await;
    }
}

/// Ghost push data returned by move_npc when a dead user was pushed aside.
pub(super) struct GhostPush {
    pub ghost_conn: ConnectionId,
    pub ghost_char_index: i32,
    pub map: i32,
    pub new_x: i32,
    pub new_y: i32,
}

/// Move an NPC in a direction. Returns (moved, optional ghost push info).
pub(super) fn move_npc(state: &mut GameState, npc_idx: usize, heading: i32) -> (bool, Option<GhostPush>) {
    let npc = match state.get_npc(npc_idx) {
        Some(n) if n.is_alive() => n,
        _ => return (false, None),
    };
    let (map, x, y) = (npc.map, npc.x, npc.y);
    let (dx, dy) = world::heading_to_offset(heading);
    let new_x = x + dx;
    let new_y = y + dy;

    // Check bounds and blocked
    if !world::in_map_bounds(new_x, new_y) {
        return (false, None);
    }
    if state.is_tile_blocked(map, new_x, new_y) {
        return (false, None);
    }

    // VB6 LegalPosNPC: water + trigger checks
    let agua_valida = state.get_npc(npc_idx).map(|n| n.agua_valida).unwrap_or(false);
    let tierra_invalida = state.get_npc(npc_idx).map(|n| n.tierra_invalida).unwrap_or(false);
    let has_water = state.hay_agua(map, new_x, new_y);

    if !agua_valida && has_water {
        return (false, None);
    }
    if tierra_invalida && !has_water {
        return (false, None);
    }

    // VB6: trigger <> eTrigger.POSINVALIDA (InvalidPos=3)
    let trigger = get_map_tile_trigger(state, map, new_x, new_y);
    if trigger == crate::data::maps::Trigger::InvalidPos {
        return (false, None);
    }

    // Check runtime tile (user or NPC already there)
    let tile_info = state.world.grid(map)
        .and_then(|g| g.tile(new_x, new_y))
        .map(|t| (t.user_conn, t.npc_index));
    let (tile_user, tile_npc) = match tile_info {
        Some((u, n)) => (u, n),
        None => return (false, None),
    };

    // Another NPC on tile → can't move
    if tile_npc != 0 {
        return (false, None);
    }

    // VB6 "Mover Casper" for NPCs: if a dead user is on the tile, push them aside
    let mut ghost_push: Option<GhostPush> = None;
    if let Some(occupant_conn) = tile_user {
        let is_ghost = state.users.get(&occupant_conn).map(|u| u.dead).unwrap_or(false);
        if !is_ghost {
            // Living user on tile → NPC can't move
            return (false, None);
        }

        // Try to push ghost to adjacent free tile (S, N, E, W)
        let directions = [heading, 3, 1, 2, 4];
        let mut push_found = false;
        let mut px = 0i32;
        let mut py = 0i32;
        for &dir in &directions {
            let (ddx, ddy) = world::heading_to_offset(dir);
            let nx = new_x + ddx;
            let ny = new_y + ddy;
            if !world::in_map_bounds(nx, ny) { continue; }
            if state.is_tile_blocked(map, nx, ny) { continue; }
            let free = state.world.grid(map)
                .map(|g| g.is_tile_free(nx, ny))
                .unwrap_or(false);
            if free {
                px = nx;
                py = ny;
                push_found = true;
                break;
            }
        }

        if !push_found {
            return (false, None); // no room to push ghost
        }

        // Push ghost on grid
        state.world.remove_user(map, new_x, new_y);
        state.world.place_user(map, px, py, occupant_conn);

        let ghost_ci = state.users.get(&occupant_conn).map(|u| u.char_index.0).unwrap_or(0);
        if let Some(ghost) = state.users.get_mut(&occupant_conn) {
            ghost.pos_x = px;
            ghost.pos_y = py;
        }

        ghost_push = Some(GhostPush {
            ghost_conn: occupant_conn,
            ghost_char_index: ghost_ci,
            map,
            new_x: px,
            new_y: py,
        });
    }

    // Move: update grid
    {
        let grid = state.world.grid_mut(map);
        if let Some(tile) = grid.tile_mut(x, y) {
            tile.npc_index = 0;
        }
        if let Some(tile) = grid.tile_mut(new_x, new_y) {
            tile.npc_index = npc_idx as i32;
        }
    }

    // Update NPC state
    if let Some(npc) = state.get_npc_mut(npc_idx) {
        npc.x = new_x;
        npc.y = new_y;
        npc.heading = heading;
    }

    (true, ghost_push)
}

/// VB6: CheckPets — When an NPC attacks a user, the user's pets react by targeting that NPC.
/// Water Elemental (NPC 92) is excluded from auto-aggression when check_elementals=true.
pub(super) fn check_pets(state: &mut GameState, attacker_npc_idx: usize, victim_conn: ConnectionId, check_elementals: bool) {
    let pets = match state.users.get(&victim_conn) {
        Some(u) => u.mascotas_index,
        None => return,
    };

    for i in 0..3 {
        let pet_idx = pets[i];
        if pet_idx == 0 || pet_idx == attacker_npc_idx { continue; }

        let pet_data = match state.get_npc(pet_idx) {
            Some(n) if n.is_alive() => (n.npc_number as i32, n.target_npc),
            _ => continue,
        };
        let (pet_number, existing_target) = pet_data;

        // VB6: Water Elemental excluded from auto-aggression when check_elementals=true
        if check_elementals && pet_number == npc::ELEMENTAL_AGUA {
            continue;
        }

        // Only set target if pet doesn't already have one
        if existing_target == 0 {
            if let Some(pet) = state.get_npc_mut(pet_idx) {
                pet.target_npc = attacker_npc_idx;
            }
        }
    }
}

/// VB6: Fire Elemental reaction — when a user attacks the master, the Fire Elemental
/// enters DEFENSE mode and becomes hostile, chasing the attacker.
pub(super) fn fire_elemental_react(state: &mut GameState, master_conn: ConnectionId, attacker_name: &str) {
    let pets = match state.users.get(&master_conn) {
        Some(u) => u.mascotas_index,
        None => return,
    };

    for i in 0..3 {
        let pet_idx = pets[i];
        if pet_idx == 0 { continue; }

        let is_fire = state.get_npc(pet_idx)
            .map(|n| n.npc_number as i32 == npc::ELEMENTAL_FUEGO && n.is_alive())
            .unwrap_or(false);

        if is_fire {
            if let Some(pet) = state.get_npc_mut(pet_idx) {
                pet.attacked_by = attacker_name.to_string();
                pet.movement = npc::AI_DEFENSE;
                pet.hostile = true;
            }
        }
    }
}
