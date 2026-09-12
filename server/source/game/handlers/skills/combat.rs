//! Combat-related skills: taming, ranged attacks.

use super::{skill_id, try_level_skill_with_hit};
use crate::game::handlers::common::*;
use crate::game::handlers::send_inventory_slot;
use crate::game::types::{GameState, MAX_INVENTORY_SLOTS, SendTarget};
use crate::net::ConnectionId;
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
                crate::game::handlers::send_pets_update(state, conn_id);

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
    // AO20 HandleWorkLeftClick/Proyectiles: spell→melee (peek), melee→spell (peek), bow interval.
    if !intervalo_permite_magia_golpe(state, conn_id, false) {
        return;
    }
    if !intervalo_permite_golpe_magia(state, conn_id, false) {
        return;
    }
    if !intervalo_permite_usar_arcos(state, conn_id, true) {
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
/// Ranged attack against an NPC — AO20 `UsuarioAtacaNpc(.., Ranged)`: the very same
/// resolution as a melee hit (`user_attack_npc`), only the attack power skill differs
/// because the equipped weapon is a bow.
async fn resolve_ranged_attack_npc(
    state: &mut GameState,
    conn_id: ConnectionId,
    npc_idx: usize,
    _arrow_min_hit: i32,
    _arrow_max_hit: i32,
) {
    let (map, x, y, level, strength, agility, min_hit, max_hit, skill_armas, name, class) =
        match state.users.get(&conn_id) {
            Some(u) if u.logged && !u.dead => (
                u.pos_map,
                u.pos_x,
                u.pos_y,
                u.level,
                u.attributes[0],
                u.attributes[1],
                u.min_hit,
                u.max_hit,
                u.skills[(skill_id::ARMAS - 1) as usize],
                u.char_name.clone(),
                u.class,
            ),
            _ => return,
        };
    crate::game::handlers::user_attack_npc(
        state, conn_id, npc_idx, map, x, y, strength, agility, level, min_hit, max_hit, skill_armas, &name, class,
    )
    .await;
}

/// Ranged attack against a user — AO20 `UsuarioAtacaUsuario(.., Ranged)`: `PuedeAtacar`
/// and then the shared resolution (`UsuarioImpacto` → `UserDamageToUser`).
async fn resolve_ranged_attack_user(
    state: &mut GameState,
    conn_id: ConnectionId,
    victim_id: ConnectionId,
    _arrow_min_hit: i32,
    _arrow_max_hit: i32,
    _arrow_envenena: bool,
) {
    let Some(both_in_arena) = crate::game::handlers::puede_atacar(state, conn_id, victim_id).await else { return };
    crate::game::handlers::usuario_ataca_usuario(
        state,
        conn_id,
        victim_id,
        crate::game::handlers::combat_ao20::AttackType::Ranged,
        both_in_arena,
    )
    .await;
}
