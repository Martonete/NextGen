//! Combat-related skills: taming, ranged attacks.

use super::{skill_id, try_level_skill, try_level_skill_with_hit};
use crate::game::handlers::common::*;
use crate::game::handlers::{
    calc_armor_absorption, calc_attack_power_with_balance, check_user_level, poder_evasion,
    poder_evasion_escudo, send_inventory_slot, user_die,
};
use crate::game::types::{GameState, MAX_INVENTORY_SLOTS, SendTarget};
use crate::net::ConnectionId;
use crate::protocol::packets::MultiMessageID;
use crate::protocol::{binary_packets, font_index};

pub(crate) async fn do_domar(state: &mut GameState, conn_id: ConnectionId, tx: i32, ty: i32) {
    let (map, ux, uy) = match state.users.get(&conn_id) {
        Some(u) => (u.pos_map, u.pos_x, u.pos_y),
        None => return,
    };

    // Distance check
    if (tx - ux).abs() > 2 || (ty - uy).abs() > 2 {
        return;
    }

    // Find NPC on tile
    let npc_idx = state
        .world
        .grid(map)
        .and_then(|g| g.tile(tx, ty))
        .and_then(|t| {
            if t.npc_index > 0 {
                Some(t.npc_index as usize)
            } else {
                None
            }
        });

    let npc_idx = match npc_idx {
        Some(idx) => idx,
        None => {
            state.send_msg_id(conn_id, 258, "");
            return;
        }
    };

    // Check NPC is tameable
    let npc_number = match state.get_npc(npc_idx) {
        Some(npc) if npc.is_alive() => npc.npc_number,
        _ => return,
    };
    let domable = state
        .game_data
        .npcs
        .get(npc_number)
        .map(|d| d.domable)
        .unwrap_or(0);

    if domable <= 0 {
        state.send_msg_id(conn_id, 257, "");
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
    let ring_obj = state
        .users
        .get(&conn_id)
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
        let num_pets = state
            .users
            .get(&conn_id)
            .map(|u| u.nro_mascotas)
            .unwrap_or(0);
        if num_pets >= 3 {
            state.send_console(conn_id, "No puedes tener más mascotas!", font_index::INFO);
        } else {
            // VB6: PuedeDomarMascota — max 2 of same NPC type
            let pet_indices = state
                .users
                .get(&conn_id)
                .map(|u| u.mascotas_index)
                .unwrap_or([0; 3]);
            let same_type_count = pet_indices
                .iter()
                .filter(|&&idx| {
                    idx > 0
                        && state
                            .get_npc(idx)
                            .map(|n| n.npc_number == npc_number)
                            .unwrap_or(false)
                })
                .count();
            if same_type_count >= 2 {
                state.send_console(
                    conn_id,
                    "Ya tienes demasiadas mascotas de ese tipo.",
                    font_index::INFO,
                );
            } else {
                // Assign pet to user
                if let Some(npc) = state.get_npc_mut(npc_idx) {
                    npc.maestro_user = Some(conn_id);
                    npc.hostile = false;
                    npc.target = None;
                }
                if let Some(u) = state.users.get_mut(&conn_id) {
                    u.nro_mascotas = u.nro_mascotas + 1;
                    // Store in pet slots
                    for i in 0..3 {
                        if u.mascotas_index[i] == 0 {
                            u.mascotas_index[i] = npc_idx;
                            u.mascotas_type[i] = npc_number as i32;
                            break;
                        }
                    }
                }
                state.send_console(conn_id, "Has domado a la criatura!", font_index::INFO);

                // VB6: SubirSkill on success
                if let Some(u) = state.users.get_mut(&conn_id) {
                    try_level_skill_with_hit(u, 17, true); // Domar = index 17
                }
            }
        }
    } else {
        state.send_console(
            conn_id,
            "No has podido domar a la criatura.",
            font_index::INFO,
        );

        if let Some(u) = state.users.get_mut(&conn_id) {
            try_level_skill_with_hit(u, 17, false);
        }
    }
}

const MAXDISTANCIAARCO: i32 = 18;

/// Ranged attack using projectile weapons (bow + arrows).
pub(crate) async fn do_ranged_attack(
    state: &mut GameState,
    conn_id: ConnectionId,
    tx: i32,
    ty: i32,
) {
    // Anti-cheat: check arrow cooldown
    if !puede_flechear(state, conn_id) {
        return;
    }

    // Get attacker data
    let user_data = match state.users.get(&conn_id) {
        Some(u) if u.logged && !u.dead => (
            u.pos_map,
            u.pos_x,
            u.pos_y,
            u.char_index,
            u.equip.weapon,
            u.equip.municion,
            u.min_sta,
            u.safe_toggle,
            u.target_user,
            u.target_npc_idx,
        ),
        _ => return,
    };
    let (
        map,
        ux,
        uy,
        char_index,
        weapon_slot,
        municion_slot,
        sta,
        _safe_toggle,
        _target_user,
        _target_npc_idx,
    ) = user_data;

    // VB6: MAXDISTANCIAARCO range check
    let dist = ((ux - tx).abs()).max((uy - ty).abs());
    if dist > MAXDISTANCIAARCO {
        state.send_console(
            conn_id,
            "Estás demasiado lejos para disparar.",
            font_index::INFO,
        );
        return;
    }

    // Check weapon is a bow (proyectil=1)
    let weapon_obj_idx = if weapon_slot > 0 && weapon_slot <= MAX_INVENTORY_SLOTS {
        state
            .users
            .get(&conn_id)
            .map(|u| u.inventory[weapon_slot - 1].obj_index)
            .unwrap_or(0)
    } else {
        0
    };

    let is_bow = weapon_obj_idx > 0
        && state
            .get_object(weapon_obj_idx)
            .map(|o| o.proyectil)
            .unwrap_or(false);

    if !is_bow {
        // Not a ranged weapon
        return;
    }

    // VB6: Check if weapon requires ammo (Municion=1) or is a throwing weapon (Municion=0)
    let weapon_needs_ammo = state
        .get_object(weapon_obj_idx)
        .map(|o| o.municion > 0)
        .unwrap_or(false);

    // Determine the projectile source: ammo slot (bow+arrow) or weapon slot (throwing weapon)
    let (consume_slot, projectile_obj_idx, _projectile_amount);

    if weapon_needs_ammo {
        // Bow + arrows: consume from ammo slot
        let municion_obj_idx = if municion_slot > 0 && municion_slot <= MAX_INVENTORY_SLOTS {
            state
                .users
                .get(&conn_id)
                .map(|u| u.inventory[municion_slot - 1].obj_index)
                .unwrap_or(0)
        } else {
            0
        };
        let municion_amount = if municion_slot > 0 && municion_slot <= MAX_INVENTORY_SLOTS {
            state
                .users
                .get(&conn_id)
                .map(|u| u.inventory[municion_slot - 1].amount)
                .unwrap_or(0)
        } else {
            0
        };
        let is_arrow = municion_obj_idx > 0
            && state
                .get_object(municion_obj_idx)
                .map(|o| o.obj_type == crate::data::objects::ObjType::Arrow)
                .unwrap_or(false);
        if !is_arrow || municion_amount < 1 {
            state.send_console(conn_id, "No tienes municiones equipadas.", font_index::INFO);
            if let Some(user) = state.users.get_mut(&conn_id) {
                user.equip.municion = 0;
            }
            return;
        }
        consume_slot = municion_slot;
        projectile_obj_idx = municion_obj_idx;
        _projectile_amount = municion_amount;
    } else {
        // Throwing weapon: consume the weapon itself
        let wp_amount = if weapon_slot > 0 && weapon_slot <= MAX_INVENTORY_SLOTS {
            state
                .users
                .get(&conn_id)
                .map(|u| u.inventory[weapon_slot - 1].amount)
                .unwrap_or(0)
        } else {
            0
        };
        if wp_amount < 1 {
            state.send_console(conn_id, "No tienes municiones.", font_index::INFO);
            return;
        }
        consume_slot = weapon_slot;
        projectile_obj_idx = weapon_obj_idx;
        _projectile_amount = wp_amount;
    }

    // Get projectile properties (damage + poison flag)
    let (arrow_min_hit, arrow_max_hit, arrow_envenena) = state
        .get_object(projectile_obj_idx)
        .map(|o| (o.min_hit, o.max_hit, o.envenena))
        .unwrap_or((0, 0, false));

    // Stamina cost (VB6: min 10 required, 1-10 consumed)
    if sta < 10 {
        state.send_msg_id(conn_id, 17, "");
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
            let idx = consume_slot - 1;
            let new_amt = user.inventory[idx].amount - 1;
            if new_amt <= 0 {
                user.inventory[idx].obj_index = 0;
                user.inventory[idx].amount = 0;
                user.inventory[idx].equipped = false;
                if weapon_needs_ammo {
                    user.equip.municion = 0;
                } else {
                    user.equip.weapon = 0;
                    user.weapon_anim = crate::game::handlers::common::NINGUN_ARMA;
                }
            } else {
                user.inventory[idx].amount = new_amt;
            }
        }
    }
    // Update inventory slot
    send_inventory_slot(state, conn_id, consume_slot).await;

    // Get projectile GRH for visual
    let arrow_grh = state
        .get_object(projectile_obj_idx)
        .map(|o| o.grh_index)
        .unwrap_or(0);

    // Find target (NPC or user on clicked tile)
    let target_npc = state
        .world
        .grid(map)
        .and_then(|g| g.tile(tx, ty))
        .and_then(|t| {
            if t.npc_index > 0 {
                Some(t.npc_index as usize)
            } else {
                None
            }
        });

    let target_user_tile = state
        .world
        .grid(map)
        .and_then(|g| g.tile(tx, ty))
        .and_then(|t| t.user_conn);

    if let Some(npc_idx) = target_npc {
        // Ranged attack vs NPC
        let npc_data = state
            .get_npc(npc_idx)
            .map(|n| (n.char_index.0, n.attackable));

        if let Some((npc_char, true)) = npc_data {
            // Send arrow visual
            let flechi =
                binary_packets::write_arrow(char_index.0 as i16, npc_char as i16, arrow_grh as i16);
            state.send_data_bytes(SendTarget::ToMap(map), &flechi);

            // Store target for combat resolution
            if let Some(user) = state.users.get_mut(&conn_id) {
                user.target_npc_idx = npc_idx;
            }
            resolve_ranged_attack_npc(state, conn_id, npc_idx, arrow_min_hit, arrow_max_hit).await;
        }
    } else if let Some(target) = target_user_tile {
        if target != conn_id {
            // Ranged attack vs user
            let target_char = state
                .users
                .get(&target)
                .map(|u| u.char_index.0)
                .unwrap_or(0);

            // Send arrow visual
            let flechi = binary_packets::write_arrow(
                char_index.0 as i16,
                target_char as i16,
                arrow_grh as i16,
            );
            state.send_data_bytes(SendTarget::ToMap(map), &flechi);

            // Store target and resolve
            if let Some(user) = state.users.get_mut(&conn_id) {
                user.target_user = target;
            }
            resolve_ranged_attack_user(
                state,
                conn_id,
                target,
                arrow_min_hit,
                arrow_max_hit,
                arrow_envenena,
            )
            .await;
        }
    }
}

/// Resolve ranged attack against NPC.
/// VB6: SistemaCombate.bas UsuarioAtacaNpc — uses Proyectiles skill + projectile class modifiers.
/// arrow_min/max add to bow weapon damage (VB6: DañoArma += RandomNumber(Ammo.MinHIT, Ammo.MaxHIT)).
async fn resolve_ranged_attack_npc(
    state: &mut GameState,
    conn_id: ConnectionId,
    npc_idx: usize,
    arrow_min_hit: i32,
    arrow_max_hit: i32,
) {
    let user_data = match state.users.get(&conn_id) {
        Some(u) if u.logged && !u.dead => (
            u.pos_map,
            u.pos_x,
            u.pos_y,
            u.char_index,
            u.level,
            u.attributes[0],
            u.attributes[1],
            u.min_hit,
            u.max_hit,
            u.skills[19],
            u.char_name.clone(),
            u.class,
        ),
        _ => return,
    };
    let (
        map,
        x,
        y,
        _char_index,
        level,
        strength,
        agility,
        min_hit,
        max_hit,
        skill_proyectiles,
        _attacker_name,
        class,
    ) = user_data;

    let npc_data = match state.get_npc(npc_idx) {
        Some(n) if n.active && n.attackable => (
            n.char_index,
            n.def,
            n.poder_evasion,
            n.min_hp,
            n.max_hp,
            n.give_exp,
            n.npc_number,
            n.name.clone(),
        ),
        _ => return,
    };
    let (npc_char, npc_def, npc_evasion, _npc_hp, _npc_max_hp, npc_exp, _npc_number, _npc_name) =
        npc_data;

    // Hit check — VB6: PoderAtaqueProyectil uses Proyectiles skill + ModClase.AtaqueProyectiles
    let atk_mod = state
        .game_data
        .balance
        .class_mod_ataque_proyectiles_e(class);
    let attack_power = calc_attack_power_with_balance(skill_proyectiles, agility, level, atk_mod);
    let hit_prob = ((50.0 + (attack_power - npc_evasion as f64) * 0.4) as i32).clamp(10, 90);

    if rand_range(1, 100) > hit_prob {
        let pkt = binary_packets::write_multi_msg_simple(MultiMessageID::UserSwing);
        state.send_bytes(conn_id, &pkt);
        state.send_chat_over_head_to(
            SendTarget::ToArea { map, x, y },
            "\u{00A1}Fallo!",
            npc_char.0 as i16,
            255,
        );
        // VB6: Level Proyectiles skill even on miss
        if let Some(u) = state.users.get_mut(&conn_id) {
            try_level_skill(u, 19);
        }
        return;
    }

    // VB6 CalcularDaño: DañoArma = Rand(Bow.MinHIT, Bow.MaxHIT) + Rand(Arrow.MinHIT, Arrow.MaxHIT)
    let bow_dmg = rand_range(min_hit.max(1), max_hit.max(1));
    let ammo_dmg = if arrow_max_hit > 0 {
        rand_range(arrow_min_hit.max(0), arrow_max_hit.max(1))
    } else {
        0
    };
    let weapon_dmg = bow_dmg + ammo_dmg;

    // VB6: StrBonus uses only bow's MaxHIT (not arrow), matching VB6's commented-out line
    let str_bonus = ((max_hit as f64 / 5.0) * (strength - 15).max(0) as f64) as i32;
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
    state.send_bytes(conn_id, &u2_pkt);
    state.send_chat_over_head_to(
        SendTarget::ToArea { map, x, y },
        &format!("-{}", damage),
        npc_char.0 as i16,
        65535,
    );

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
        state.send_data_bytes(SendTarget::ToArea { map, x, y }, &bp_pkt);

        state.kill_npc(npc_idx);
    }
}

/// Resolve ranged attack against user — VB6: SistemaCombate.bas UsuarioAtacaUsuario (ranged path).
/// Uses Proyectiles skill + projectile class modifiers + arrow poison application.
async fn resolve_ranged_attack_user(
    state: &mut GameState,
    conn_id: ConnectionId,
    victim_id: ConnectionId,
    arrow_min_hit: i32,
    arrow_max_hit: i32,
    arrow_envenena: bool,
) {
    let att_data = match state.users.get(&conn_id) {
        Some(u) if u.logged && !u.dead => (
            u.pos_map,
            u.pos_x,
            u.pos_y,
            u.char_index,
            u.level,
            u.attributes[0],
            u.attributes[1],
            u.min_hit,
            u.max_hit,
            u.skills[19],
            u.char_name.clone(),
            u.class,
            u.safe_toggle,
        ),
        _ => return,
    };
    let (
        map,
        x,
        y,
        _char_index,
        level,
        strength,
        agility,
        min_hit,
        max_hit,
        skill_proyectiles,
        attacker_name,
        class,
        safe_on,
    ) = att_data;

    if safe_on {
        state.send_msg_id(conn_id, 207, "");
        return;
    }

    let victim_data = match state.users.get(&victim_id) {
        Some(v) if v.logged && !v.dead => (
            v.level,
            v.attributes[1],
            v.skills[3],
            v.skills[4],
            v.char_name.clone(),
            v.privileges,
            v.char_index,
            v.class,
            v.equip.shield > 0 && v.equip.shield <= MAX_INVENTORY_SLOTS,
            v.meditating,
        ),
        _ => return,
    };
    let (
        v_level,
        v_agility,
        v_tacticas,
        v_defensa,
        victim_name,
        v_privs,
        v_char_index,
        v_class,
        v_has_shield,
        v_meditating,
    ) = victim_data;

    if v_privs > 0 {
        return;
    }

    // Hit check — VB6: PoderAtaqueProyectil + ModClase.AtaqueProyectiles
    let atk_mod = state
        .game_data
        .balance
        .class_mod_ataque_proyectiles_e(class);
    let attack_power = calc_attack_power_with_balance(skill_proyectiles, agility, level, atk_mod);
    // VB6: victim evasion includes shield bonus (same as melee PvP)
    let v_evasion_mod = state.game_data.balance.class_mod_evasion_e(v_class);
    let mut victim_evasion = poder_evasion(v_tacticas, v_agility, v_level, v_evasion_mod) as f64;
    if v_has_shield {
        let shield_mod = state.game_data.balance.class_mod_escudo_e(v_class);
        victim_evasion += poder_evasion_escudo(v_defensa, shield_mod) as f64;
    }
    let mut prob = (50.0 + (attack_power - victim_evasion) * 0.4) as i32;
    prob = prob.clamp(10, 90);

    // VB6: Meditation reduces evasion by 25%
    if v_meditating {
        let prob_evadir = ((100 - prob) as f64 * 0.75) as i32;
        prob = (100 - prob_evadir).min(90);
    }

    if rand_range(1, 100) > prob {
        let pkt = binary_packets::write_multi_user_attacked_swing(_char_index.0 as i16);
        state.send_bytes(victim_id, &pkt);
        let pkt = binary_packets::write_multi_msg_simple(MultiMessageID::UserSwing);
        state.send_bytes(conn_id, &pkt);
        state.send_chat_over_head_to(
            SendTarget::ToArea { map, x, y },
            "\u{00A1}Fallo!",
            v_char_index.0 as i16,
            255,
        );
        // VB6: Level Proyectiles skill even on miss
        if let Some(u) = state.users.get_mut(&conn_id) {
            try_level_skill(u, 19);
        }
        return;
    }

    // VB6 CalcularDaño: DañoArma = Rand(Bow.MinHIT, Bow.MaxHIT) + Rand(Arrow.MinHIT, Arrow.MaxHIT)
    let bow_dmg = rand_range(min_hit.max(1), max_hit.max(1));
    let ammo_dmg = if arrow_max_hit > 0 {
        rand_range(arrow_min_hit.max(0), arrow_max_hit.max(1))
    } else {
        0
    };
    let weapon_dmg = bow_dmg + ammo_dmg;

    // VB6: StrBonus uses only bow's MaxHIT (not arrow), matching VB6's commented-out line
    let str_bonus = ((max_hit as f64 / 5.0) * (strength - 15).max(0) as f64) as i32;
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
    let n4_pkt = binary_packets::write_multi_user_hitted_by_user(
        _char_index.0 as i16,
        body_part as u8,
        damage as i16,
    );
    state.send_bytes(victim_id, &n4_pkt);
    let n5_pkt = binary_packets::write_multi_user_hitted_user(
        v_char_index.0 as i16,
        body_part as u8,
        damage as i16,
    );
    state.send_bytes(conn_id, &n5_pkt);

    state.send_chat_over_head_to(
        SendTarget::ToArea { map, x, y },
        &format!("-{}", damage),
        v_char_index.0 as i16,
        65535,
    );

    // VB6: Arrow poison (60% chance if ammo has Envenena=1) — SistemaCombate.bas UserEnvenena
    // VB6 uses `< 60` (59% effective), matching the melee weapon poison path.
    if arrow_envenena && rand_range(1, 100) < 60 {
        let already_poisoned = state
            .users
            .get(&victim_id)
            .map(|u| u.poisoned)
            .unwrap_or(true);
        if !already_poisoned {
            if let Some(victim) = state.users.get_mut(&victim_id) {
                victim.poisoned = true;
                victim.counter_poison = 0;
                victim.poisoned_by = Some(conn_id);
                victim.poisoned_skill_id = skill_id::PROYECTILES;
            }
            state.send_msg_id(victim_id, 171, &attacker_name);
            state.send_msg_id(conn_id, 172, &victim_name);
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
