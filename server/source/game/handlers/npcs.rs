//! NPC system handlers: area visibility, NPC combat, NPC death, item drops.
//! Extracted from mod.rs to reduce file size.

use tracing::info;
use crate::net::ConnectionId;
use crate::game::class_race::PlayerClass;
use crate::game::types::{GameState, SendTarget, privilege_level};
use crate::game::world;
use crate::game::npc;
use crate::protocol::binary_packets;
use crate::data::experience::MAX_LEVEL;
use crate::data::npcs::NpcType;
use super::common::*;
use crate::game::types::MAX_INVENTORY_SLOTS;
use crate::protocol::font_index;
use super::{
    user_die, check_user_level, send_inventory_slot, send_full_inventory,
    calc_armor_absorption_with_penetration,
    poder_evasion, poder_evasion_escudo,
    poder_ataque_arma, poder_ataque_proyectil, poder_ataque_wrestling,
    calcular_dano, get_weapon_info, get_ring_info,
    do_apunalar, do_golpe_critico, puede_apunalar,
    do_acuchillar, puede_acuchillar,
    pretoriano_check_death, es_pretoriano,
    remove_pet_from_owner,
};
use super::skills::try_level_skill_with_hit;
use crate::game::constants::*;

// =====================================================================
// NPC system — spawning, AI, combat
// =====================================================================

/// Send CC packets for all NPCs in the area around (x, y) on map to a specific user.
pub(super) async fn send_area_npc_ccs(state: &mut GameState, conn_id: ConnectionId, map: i32, x: i32, y: i32) {
    let (grid_w, grid_h) = state.grid_dimensions(map);
    let min_y = (y - world::MIN_Y_BORDER + 1).max(1);
    let max_y = (y + world::MIN_Y_BORDER - 1).min(grid_h);
    let min_x = (x - world::MIN_X_BORDER + 1).max(1);
    let max_x = (x + world::MIN_X_BORDER - 1).min(grid_w);

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
        state.send_bytes(conn_id, cc);
    }
}

/// Send ground item visuals (HO) in the area to a user.
pub(super) async fn send_area_ground_items(state: &mut GameState, conn_id: ConnectionId, map: i32, x: i32, y: i32) {
    let (grid_w, grid_h) = state.grid_dimensions(map);
    let min_y = (y - world::MIN_Y_BORDER + 1).max(1);
    let max_y = (y + world::MIN_Y_BORDER - 1).min(grid_h);
    let min_x = (x - world::MIN_X_BORDER + 1).max(1);
    let max_x = (x + world::MIN_X_BORDER - 1).min(grid_w);

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
                            ho_packets.push(binary_packets::write_object_create(gx as i16, gy as i16, grh as i16));
                        }
                    }
                }
            }
        }
    }

    for ho in ho_packets {
        state.send_bytes(conn_id, &ho);
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
        state.send_msg_id(conn_id, 140, ""); // "No puedes atacar a este NPC"
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
        state.send_msg_id(conn_id, 3, ""); // "Estas muerto"
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
        state.send_msg_id(conn_id, 167, ""); // "No puedes atacar a un NPC de tu facción"
        return false;
    }

    // VB6: Alliance faction can't attack NPCs 618 and 947
    if (npc_number == 618 || npc_number == 947) && is_alianza {
        state.send_msg_id(conn_id, 167, "");
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
            state.send_msg_id(conn_id, 120, ""); // "Necesitas pertenecer a un clan"
            return false;
        }

        // Can't attack your own guild's castle king
        // Check if this NPC's map belongs to a castle owned by the attacker's guild
        if let Some(castle_owner_guild) = state.get_castle_owner_guild(npc_map) {
            if castle_owner_guild == user_guild_index {
                state.send_msg_id(conn_id, 169, ""); // "No puedes atacar al rey de tu propio castillo"
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
    class: PlayerClass,
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
            let mod_atk = state.game_data.balance.class_mod_ataque_proyectiles_e(class);
            (poder_ataque_proyectil(
                state.users.get(&conn_id).map(|u| u.skills[5]).unwrap_or(0),
                agility, level, mod_atk,
            ), 5usize)
        } else {
            let mod_atk = state.game_data.balance.class_mod_ataque_armas_e(class);
            (poder_ataque_arma(skill_armas, agility, level, mod_atk), 1usize)
        }
    } else {
        let mod_atk = state.game_data.balance.class_mod_ataque_wrestling_e(class);
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
        let snd = binary_packets::write_play_wave(2, x as i16, y as i16);
        state.send_data_bytes(SendTarget::ToArea { map, x, y }, &snd);
        let pkt = binary_packets::write_multi_msg_simple(crate::protocol::packets::MultiMessageID::UserSwing);
        state.send_bytes(conn_id, &pkt);
        state.send_chat_over_head_to(SendTarget::ToArea { map, x, y }, "\u{00A1}Fallo!", npc_char_index.0 as i16, 255);
        return;
    }

    // VB6: CalcularDaño(UserIndex, NpcIndex) — with proper weapon type class modifier
    let class_mod_damage = if weapon_info.obj_index > 0 {
        if weapon_info.is_proyectil {
            state.game_data.balance.class_mod_dano_proyectiles_e(class) as f64
        } else {
            state.game_data.balance.class_mod_dano_armas_e(class) as f64
        }
    } else {
        state.game_data.balance.class_mod_dano_wrestling_e(class) as f64
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

    // VB6: EspadaMataDragonesIndex (402) — instakill dragons, 1 damage to non-dragons
    let npc_is_dragon = state.get_npc(npc_idx).map(|n| n.npc_type == NpcType::Dragon).unwrap_or(false);
    let npc_def = state.get_npc(npc_idx).map(|n| n.def).unwrap_or(0) as i64;

    let damage = if weapon_info.obj_index == ESPADA_MATA_DRAGONES {
        if npc_is_dragon {
            // VB6: CalcularDaño = NpcList(NpcIndex).Stats.MinHp + NpcList(NpcIndex).Stats.def
            let npc_min_hp = state.get_npc(npc_idx).map(|n| n.min_hp).unwrap_or(0);
            npc_min_hp + npc_def as i32 // Guaranteed kill — damage equals remaining HP + def
        } else {
            1 // Dragon Slayer always deals exactly 1 to non-dragons (ignores defense)
        }
    } else {
        (damage_before_def - npc_def).max(0) as i32
    };

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
                    } else {
                        // VB6 AttackedBy queue: hostile NPC prioritizes most recent
                        // attacker as target (not just first adjacent player found).
                        // Always update target to the latest attacker.
                        npc.target = Some(conn_id);
                        if npc.movement == npc::AI_DEFENSE {
                            npc.attacked_by = attacker_name.to_string();
                        }
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
    state.send_bytes(conn_id, &u2_pkt);

    // VB6: floating yellow damage number above NPC (vbYellow=65535)
    state.send_chat_over_head_to(SendTarget::ToArea { map, x, y }, &format!("-{}", damage), npc_char_index.0 as i16, 65535);

    // VB6: NPC Snd1 (attack sound) + SND_IMPACTO + Snd2 (victim hurt sound, fallback SND_IMPACTO2=12)
    let (npc_snd1, npc_snd2) = state.get_npc(npc_idx)
        .map(|n| (n.snd1, n.snd2))
        .unwrap_or((0, 0));
    if npc_snd1 > 0 {
        let snd = binary_packets::write_play_wave(npc_snd1 as u8, x as i16, y as i16);
        state.send_data_bytes(SendTarget::ToArea { map, x, y }, &snd);
    }
    let snd = binary_packets::write_play_wave(10, x as i16, y as i16);
    state.send_data_bytes(SendTarget::ToArea { map, x, y }, &snd);
    if npc_snd2 > 0 {
        let snd = binary_packets::write_play_wave(npc_snd2 as u8, x as i16, y as i16);
        state.send_data_bytes(SendTarget::ToArea { map, x, y }, &snd);
    } else {
        let snd = binary_packets::write_play_wave(12, x as i16, y as i16);
        state.send_data_bytes(SendTarget::ToArea { map, x, y }, &snd);
    }

    // Blood FX on NPC
    let fx_pkt = binary_packets::write_create_fx(npc_char_index.0 as i16, 14, 0); // VB6: FXSANGRE = 14
    state.send_data_bytes(SendTarget::ToArea { map, x, y }, &fx_pkt);

    // VB6: If NPC still alive after initial hit, try backstab and critical
    let npc_still_alive = state.get_npc(npc_idx).map(|n| n.min_hp > 0).unwrap_or(false);
    if npc_still_alive {
        let apunalar_sk = state.users.get(&conn_id).map(|u| u.skills[8]).unwrap_or(0);

        // VB6: DoApuñalar — backstab (NPC target gets 2x damage)
        if puede_apunalar(class, weapon_info.apunala, apunalar_sk) {
            // VB6: Assassin ignores NPC defense for backstab base damage
            let stab_base = if class == PlayerClass::Asesino {
                damage_before_def as i64 // Ignore defense for Assassin
            } else {
                damage as i64
            };

            if let Some(stab_dmg) = do_apunalar(apunalar_sk, class, stab_base, true) {
                if let Some(npc) = state.get_npc_mut(npc_idx) {
                    npc.min_hp -= stab_dmg as i32;
                    npc.damage_received.push((conn_id, stab_dmg as i32));
                }
                state.send_console(conn_id, &format!("Has apuñalado la criatura por {}", stab_dmg), font_index::FIGHT);
                if let Some(u) = state.users.get_mut(&conn_id) {
                    try_level_skill_with_hit(u, 8, true);
                }
            } else {
                state.send_console(conn_id, "\u{00A1}No has logrado apuñalar a tu enemigo!", font_index::FIGHT);
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
            state.send_console(conn_id, &format!("Has golpeado críticamente a la criatura por {}.", crit_dmg), font_index::FIGHT);
        }

        // VB6: DoAcuchillar (Pirate throat cut — melee NPC attacks, SistemaCombate.bas:423)
        if puede_acuchillar(class, weapon_info.acuchilla) {
            if let Some(cut_dmg) = do_acuchillar(damage as i64) {
                if let Some(npc) = state.get_npc_mut(npc_idx) {
                    npc.min_hp -= cut_dmg as i32;
                    npc.damage_received.push((conn_id, cut_dmg as i32));
                }
                state.send_console(conn_id, &format!("Has acuchillado a la criatura por {}", cut_dmg), font_index::FIGHT);
            }
        }
    }

    // Re-check dead status after backstab/crit
    let npc_dead = state.get_npc(npc_idx).map(|n| n.min_hp <= 0).unwrap_or(false);

    // Per-hit EXP (VB6: CalcularDarExp — gives proportional exp on EVERY hit, not just on death)
    // VB6: ExpaDar = CLng(ElDaño * (GiveEXP / MaxHp)) — NO multiplier per hit
    if npc_give_exp > 0 && npc_max_hp > 0 {
        let exp_award = ((npc_give_exp as f64 / npc_max_hp as f64) * damage as f64) as i64;

        // Level cap check
        let can_level = state.users.get(&conn_id)
            .map(|u| u.logged && u.level < MAX_LEVEL as i32)
            .unwrap_or(false);

        if can_level && exp_award > 0 {
            if let Some(user) = state.users.get_mut(&conn_id) {
                user.exp += exp_award;
            }
            // Send msg 170 notification (VB6: "Has ganado %1 puntos de experiencia")
            state.send_msg_id(conn_id, 170, &exp_award.to_string());
            send_stats_exp(state, conn_id).await;
            check_user_level(state, conn_id).await;
        }
    }

    if npc_dead {
        // VB6: Dragon Slayer sword is consumed when killing a dragon
        if npc_is_dragon && weapon_info.obj_index == ESPADA_MATA_DRAGONES {
            if let Some(user) = state.users.get_mut(&conn_id) {
                if user.equip.weapon > 0 && user.equip.weapon <= MAX_INVENTORY_SLOTS {
                    let slot = user.equip.weapon - 1;
                    if user.inventory[slot].obj_index == ESPADA_MATA_DRAGONES {
                        user.inventory[slot].amount -= 1;
                        if user.inventory[slot].amount <= 0 {
                            user.inventory[slot].obj_index = 0;
        user.inventory[slot].amount = 0;
        user.inventory[slot].equipped = false;
                            user.equip.weapon = 0;
                        }
                    }
                }
            }
            state.send_console(conn_id, "La espada mata dragones se ha destruido.", font_index::INFO);
            send_full_inventory(state, conn_id).await;
        }
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
                    n.snd3, n.maestro_user),
        None => return,
    };
    let (map, x, y, char_index, npc_name, npc_number,
         snd3, is_pet_owner) = npc_info;

    // 1) Death sound (VB6: TW{snd3})
    if snd3 > 0 {
        let snd_pkt = binary_packets::write_play_wave(snd3 as u8, x as i16, y as i16);
        state.send_data_bytes(SendTarget::ToArea { map, x, y }, &snd_pkt);
    }

    // 2) Send msg 50 to killer (NPC death notification — client plays sound/animation)
    state.send_msg_id(killer_id, 50, "");

    // 3) Drop items (NPC_TIRAR_ITEMS) — only if not a pet
    let is_pet = is_pet_owner.is_some();
    if !is_pet {
        npc_drop_items(state, npc_idx, killer_id, map, x, y).await;
    }

    // 4) Remove NPC character from area (BP packet)
    let bp_pkt = binary_packets::write_character_remove(char_index.0 as i16);
    state.send_data_bytes(SendTarget::ToArea { map, x, y }, &bp_pkt);

    // Kill NPC (remove from grid, mark inactive)
    state.kill_npc(npc_idx);

    // Check praetorian death
    if es_pretoriano(npc_number) {
        pretoriano_check_death(state, npc_idx);
    }

    // EXP is now given per-hit via CalcularDarExp in user_attack_npc().
    // No death-time exp distribution needed (VB6 parity: exp per hit, not per kill).

    // 6) Gold drops to floor at NPC position (VB6: TirarOro in MuereNpc)
    let gold_mult = state.multiplicador_oro;
    let gld_min = (give_gld_min as i64) * (gold_mult as i64);
    let gld_max = (give_gld_max as i64) * (gold_mult as i64);
    let gold_award = if gld_max > gld_min {
        rand_range(gld_min as i32, gld_max as i32) as i64
    } else {
        gld_min
    };
    if gold_award > 0 {
        drop_gold_on_floor(state, map, x, y, gold_award as i32).await;
    }

    // 7) Pet cleanup — if NPC was someone's pet, remove from owner
    if let Some(owner_conn) = is_pet_owner {
        remove_pet_from_owner(state, owner_conn, npc_idx);
    }

    // 9) Notify killer (VB6: ||50 + ||56@gold)
    // Note: ||50 already sent at step 2. ||170 exp is now per-hit (CalcularDarExp).
    if gold_award > 0 {
        state.send_msg_id(killer_id, 56, &gold_award.to_string()); // TEXTO56: La criatura ha dejado %1 monedas
    }

    info!("[NPC] '{}' killed NPC '{}' (idx={}, +{} exp, +{} gold)",
          state.users.get(&killer_id).map(|u| u.char_name.as_str()).unwrap_or("?"),
          npc_name, npc_idx, give_exp, gold_award);
}


/// VB6: TirarOro — drop gold on the floor at the given position.
/// Splits into 10,000-chunk piles (INTMAXGOLD). Stacks with existing gold on tile.
#[allow(unused_assignments)]
async fn drop_gold_on_floor(state: &mut GameState, map: i32, x: i32, y: i32, total: i32) {
    let grh_index = state.get_object(GOLD_OBJ_INDEX)
        .map(|o| o.grh_index)
        .unwrap_or(0);

    let mut remaining = total;
    while remaining > 0 {
        let chunk = remaining.min(10_000);
        remaining -= chunk;

        // Check if tile can hold gold (empty or already gold)
        let can_place = state.world.grid(map)
            .and_then(|g| g.tile(x, y))
            .map(|t| t.ground_item.obj_index == 0 || t.ground_item.obj_index == GOLD_OBJ_INDEX)
            .unwrap_or(false);

        if !can_place {
            break;
        }

        let is_new = {
            let grid = state.world.grid_mut(map);
            if let Some(tile) = grid.tile_mut(x, y) {
                if tile.ground_item.obj_index == GOLD_OBJ_INDEX {
                    tile.ground_item.amount += chunk;
                    false
                } else {
                    tile.ground_item.obj_index = GOLD_OBJ_INDEX;
                    tile.ground_item.amount = chunk;
                    true
                }
            } else {
                break;
            }
        };

        if is_new && grh_index > 0 {
            let pkt = binary_packets::write_object_create(x as i16, y as i16, grh_index as i16);
            state.send_data_bytes(SendTarget::ToArea { map, x, y }, &pkt);
        }

        clean_world_add_item(state, map, x, y, 10, GOLD_OBJ_INDEX);
        // Gold stacks on a single tile — no need for multiple piles
        break;
    }
}

/// Drop NPC inventory items on death using VB6 tiered drop table.
/// VB6: NPC_TIRAR_ITEMS (Modulo_InventANDobj.bas).
/// Tiered system: Roll 1-100. If > 90: no drops (10% nothing).
/// If <= 90: drop tier 1 (slot 0). If <= 10: also drop tier 2 (slot 1).
/// Then 10% chance each for tiers 3, 4, 5 (slots 2, 3, 4).
/// Pretoriano NPCs: drop ALL inventory + gold (special case).
pub(super) async fn npc_drop_items(
    state: &mut GameState,
    npc_idx: usize,
    _killer_id: ConnectionId,
    map: i32,
    npc_x: i32,
    npc_y: i32,
) {
    // Get NPC number and inventory (slots 0-4 = tiers 1-5)
    let (npc_number, npc_inv): (usize, Vec<(i32, i32)>) = match state.get_npc(npc_idx) {
        Some(n) => {
            let inv: Vec<(i32, i32)> = n.inventory.iter()
                .map(|slot| (slot.obj_index, slot.amount))
                .collect();
            (n.npc_number, inv)
        }
        None => return,
    };

    // Special case: Pretoriano NPCs drop ALL inventory items + gold
    if es_pretoriano(npc_number) {
        for (obj_index, amount) in &npc_inv {
            if *obj_index <= 0 || *amount <= 0 { continue; }
            let (dx, dy) = find_free_tile(state, map, npc_x, npc_y);
            let drop_x = npc_x + dx;
            let drop_y = npc_y + dy;
            let grid = state.world.grid_mut(map);
            if let Some(tile) = grid.tile_mut(drop_x, drop_y) {
                if tile.ground_item.obj_index == 0 {
                    tile.ground_item.obj_index = *obj_index;
                    tile.ground_item.amount = *amount;
                }
            }
            let grh = state.get_object(*obj_index).map(|o| o.grh_index).unwrap_or(0);
            if grh > 0 {
                let ho_pkt = binary_packets::write_object_create(drop_x as i16, drop_y as i16, grh as i16);
                state.send_data_bytes(SendTarget::ToArea { map, x: npc_x, y: npc_y }, &ho_pkt);
            }
            clean_world_add_item(state, map, drop_x, drop_y, 10, *obj_index);
        }
        return;
    }

    // VB6 tiered drop table
    let roll = random_number(1, 100);

    // If roll > 90: no drops at all (10% chance of nothing)
    if roll > 90 {
        return;
    }

    // Collect which tiers to drop based on the roll
    let mut tiers_to_drop: Vec<usize> = Vec::new();

    // Roll <= 90: always drop tier 1 (slot 0)
    tiers_to_drop.push(0);

    // Roll <= 10: also drop tier 2 (slot 1), plus 10% chance each for tiers 3-5
    if roll <= 10 {
        tiers_to_drop.push(1);
        if random_number(1, 10) == 1 { tiers_to_drop.push(2); }
        if random_number(1, 10) == 1 { tiers_to_drop.push(3); }
        if random_number(1, 10) == 1 { tiers_to_drop.push(4); }
    }

    // Drop items for each tier
    for tier_idx in tiers_to_drop {
        if tier_idx >= npc_inv.len() { continue; }
        let (obj_index, amount) = npc_inv[tier_idx];
        if obj_index <= 0 || amount <= 0 { continue; }

        let (dx, dy) = find_free_tile(state, map, npc_x, npc_y);
        let drop_x = npc_x + dx;
        let drop_y = npc_y + dy;

        let grid = state.world.grid_mut(map);
        if let Some(tile) = grid.tile_mut(drop_x, drop_y) {
            if tile.ground_item.obj_index == 0 {
                tile.ground_item.obj_index = obj_index;
                tile.ground_item.amount = amount;
            }
        }

        let grh = state.get_object(obj_index).map(|o| o.grh_index).unwrap_or(0);
        if grh > 0 {
            let ho_pkt = binary_packets::write_object_create(drop_x as i16, drop_y as i16, grh as i16);
            state.send_data_bytes(SendTarget::ToArea { map, x: npc_x, y: npc_y }, &ho_pkt);
        }

        clean_world_add_item(state, map, drop_x, drop_y, 10, obj_index);
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
        state.send_data_bytes(SendTarget::ToArea { map, x: nx, y: ny }, &cp_pkt);
    }

    let user_data = match state.users.get(&target_conn) {
        // VB6 NpcAtacaUser: AdminInvisible=1 or Privilegios<>User → exit (no attack)
        Some(u) if u.logged && !u.dead && u.privileges == 0 && !u.admin_invisible => {
            (u.attributes[1], // Agility
             u.skills[3],     // SK4 = Tacticas
             u.skills[4],     // SK5 = Defensa
             u.level, u.char_index, u.class,
             u.equip.shield > 0 && u.equip.shield <= crate::game::types::MAX_INVENTORY_SLOTS)
        }
        _ => return,
    };
    let (u_agility, u_tacticas, u_defensa, u_level, u_char_index, u_class, u_has_shield) = user_data;

    // VB6: PoderEvasion with class modifier
    let evasion_mod = state.game_data.balance.class_mod_evasion_e(u_class);
    let mut user_evasion = poder_evasion(u_tacticas, u_agility, u_level, evasion_mod) as f64;

    // VB6: Add shield evasion if victim has shield
    if u_has_shield {
        let shield_mod = state.game_data.balance.class_mod_escudo_e(u_class);
        user_evasion += poder_evasion_escudo(u_defensa, shield_mod) as f64;
    }

    // Hit/miss calculation
    let npc_power = npc_ataque as f64;
    let hit_prob = ((50.0 + (npc_power - user_evasion) * 0.4) as i32).clamp(10, 90);

    if rand_range(1, 100) > hit_prob {
        // VB6: Shield block check on miss (NpcImpacto lines 235-255)
        if u_has_shield {
            let suma_skills = (u_defensa + u_tacticas).max(1);
            let prob_rechazo = ((100 * u_defensa / suma_skills) as i32).clamp(10, 90);
            let rechazo = rand_range(1, 100) <= prob_rechazo;

            if rechazo {
                // Shield blocks — VB6: SND_ESCUDO + messages + skill up
                let snd = binary_packets::write_play_wave(37, nx as i16, ny as i16);
                state.send_data_bytes(SendTarget::ToArea { map, x: nx, y: ny }, &snd);
                let pkt = binary_packets::write_multi_msg_simple(
                    crate::protocol::packets::MultiMessageID::BlockedWithShieldUser);
                state.send_bytes(target_conn, &pkt);
                // VB6: SubirSkill Defensa on shield block success
                if let Some(victim) = state.users.get_mut(&target_conn) {
                    try_level_skill_with_hit(victim, 4, true);
                }
            } else {
                // Failed block — still skill gain attempt
                if let Some(victim) = state.users.get_mut(&target_conn) {
                    try_level_skill_with_hit(victim, 4, false);
                }
            }
        }

        // Miss — VB6: SND_SWING to area + N1
        let snd = binary_packets::write_play_wave(2, nx as i16, ny as i16);
        state.send_data_bytes(SendTarget::ToArea { map, x: nx, y: ny }, &snd);
        let pkt = binary_packets::write_multi_msg_simple(crate::protocol::packets::MultiMessageID::NPCSwing);
        state.send_bytes(target_conn, &pkt);
        // VB6: floating red "¡Fallo!" above user (N| vbRed°¡Fallo!°charIndex)
        state.send_chat_over_head_to(SendTarget::ToArea { map, x: nx, y: ny }, "\u{00A1}Fallo!", u_char_index.0 as i16, 255);

        // VB6: SubirSkill Tacticas (victim on miss)
        if let Some(victim) = state.users.get_mut(&target_conn) {
            try_level_skill_with_hit(victim, 3, true);
        }
        return;
    }

    // VB6: SubirSkill Tacticas (victim on hit — failure)
    if let Some(victim) = state.users.get_mut(&target_conn) {
        try_level_skill_with_hit(victim, 3, false);
    }

    // Damage + armor absorption (VB6: NpcDaño — combined armor+shield single roll)
    // VB6: Lugar = RandomNumber(bCabeza, bTorso) → 1=head, 2=torso
    let body_part = rand_range(1, 2);
    let raw_damage = rand_range(npc_min_hit.max(1), npc_max_hit.max(1));
    // VB6: AtacarPersonaje applies refuerzo (weapon penetration) from NPC weapon.
    // NPC data does not carry a weapon-object refuerzo value, so penetration is 0.
    let absorption = calc_armor_absorption_with_penetration(state, target_conn, body_part, 0);

    // VB6: Boat defense — if user is sailing, boat absorbs damage too
    let boat_defense = {
        let mut def = 0;
        if let Some(u) = state.users.get(&target_conn) {
            if u.navigating && u.barco_slot > 0 && u.barco_slot <= crate::game::types::MAX_INVENTORY_SLOTS {
                let boat_idx = u.inventory[u.barco_slot - 1].obj_index;
                if let Some(obj) = state.game_data.objects.get(boat_idx as usize) {
                    if obj.max_def > 0 {
                        def = rand_range(obj.min_def.max(0), obj.max_def.max(1));
                    }
                }
            }
        }
        def
    };

    let damage = (raw_damage - absorption - boat_defense).max(1);

    // Apply damage
    if let Some(user) = state.users.get_mut(&target_conn) {
        user.min_hp -= damage;
    }
    let n2_pkt = binary_packets::write_multi_npc_hit_user(body_part as u8, damage as i16);
    state.send_bytes(target_conn, &n2_pkt);

    // Blood FX on player (VB6: FXSANGRE = 14)
    let fx_pkt = binary_packets::write_create_fx(u_char_index.0 as i16, 14, 0);
    state.send_data_bytes(SendTarget::ToArea { map, x: nx, y: ny }, &fx_pkt);

    // VB6: NPC attack sound (Snd1) + victim hit sound (Snd2 or SND_IMPACTO2=12)
    let (npc_snd1, npc_snd2) = state.get_npc(npc_idx)
        .map(|n| (n.snd1, n.snd2))
        .unwrap_or((0, 0));
    if npc_snd1 > 0 {
        let snd = binary_packets::write_play_wave(npc_snd1 as u8, nx as i16, ny as i16);
        state.send_data_bytes(SendTarget::ToArea { map, x: nx, y: ny }, &snd);
    }
    // SND_IMPACTO to area on hit
    let snd = binary_packets::write_play_wave(10, nx as i16, ny as i16);
    state.send_data_bytes(SendTarget::ToArea { map, x: nx, y: ny }, &snd);
    // Victim sound: Snd2 if defined, else SND_IMPACTO2 (12)
    if npc_snd2 > 0 {
        let snd = binary_packets::write_play_wave(npc_snd2 as u8, nx as i16, ny as i16);
        state.send_data_bytes(SendTarget::ToArea { map, x: nx, y: ny }, &snd);
    } else {
        let snd = binary_packets::write_play_wave(12, nx as i16, ny as i16);
        state.send_data_bytes(SendTarget::ToArea { map, x: nx, y: ny }, &snd);
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
            state.send_msg_id(target_conn, 171, &npc_name);
        }
    }

    // VB6: NpcDano meditation break formula — conditional based on damage vs threshold
    {
        let med_data = state.users.get(&target_conn).map(|u| {
            (u.meditating, u.min_hp, u.attributes[2], u.skills[6], u.char_index.0, u.pos_map, u.pos_x, u.pos_y)
        });
        if let Some((is_meditating, min_hp, intelligence, meditar_skill, ci, umap, ux, uy)) = med_data {
            if is_meditating {
                let threshold = (min_hp as f64 / 100.0 * intelligence as f64
                    * meditar_skill as f64 / 100.0 * 12.0
                    / (rand_range(0, 5) as f64 + 7.0)) as i32;
                if damage > threshold {
                    if let Some(user) = state.users.get_mut(&target_conn) {
                        user.meditating = false;
                    }
                    state.send_bytes(target_conn, &binary_packets::write_meditate_toggle());
                    let fx_clear = binary_packets::write_create_fx(ci as i16, 0, 0);
                    state.send_data_bytes(SendTarget::ToArea { map: umap, x: ux, y: uy }, &fx_clear);
                }
            }
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
        state.send_chat_over_head_to(SendTarget::ToArea { map, x: nx, y: ny }, &msg, npc_char.0 as i16, 16711680); // vbRed
    }

    // FX on target
    let target_ci = state.users.get(&target_conn).map(|u| u.char_index.0).unwrap_or(0);
    if spell.fx_grh > 0 {
        let fx = binary_packets::write_create_fx(target_ci as i16, spell.fx_grh as i16, spell.loops as i16);
        state.send_data_bytes(SendTarget::ToArea { map, x: nx, y: ny }, &fx);
    }
    if spell.wav > 0 {
        let snd = binary_packets::write_play_wave(spell.wav as u8, nx as i16, ny as i16);
        state.send_data_bytes(SendTarget::ToArea { map, x: nx, y: ny }, &snd);
    }

    // Spell effect
    match spell.sube_hp {
        1 => {
            // Heal spell (SubeHP=1) — VB6: heals the TARGET USER, not the NPC
            // (VB6: NpcLanzaSpellSobreUser → UserList(UserIndex).Stats.MinHp += damage)
            let heal = rand_range(spell.min_hp.max(1), spell.max_hp.max(1));
            if let Some(user) = state.users.get_mut(&target_conn) {
                user.min_hp = (user.min_hp + heal).min(user.max_hp);
            }
            send_stats_hp(state, target_conn).await;
        }
        2 => {
            // Damage spell (SubeHP=2)
            let mut damage = rand_range(spell.min_hp.max(1), spell.max_hp.max(1));

            // VB6: Magic defense from helmet + ring (DefensaMagicaMin/Max)
            let (helmet_slot, ring_slot) = state.users.get(&target_conn)
                .map(|u| (u.equip.helmet, u.equip.ring))
                .unwrap_or((0, 0));
            // Helmet magic defense
            if helmet_slot > 0 && helmet_slot <= crate::game::types::MAX_INVENTORY_SLOTS {
                let helmet_obj_idx = state.users.get(&target_conn)
                    .map(|u| u.inventory[helmet_slot - 1].obj_index).unwrap_or(0);
                if helmet_obj_idx > 0 {
                    if let Some(obj) = state.game_data.objects.get(helmet_obj_idx as usize) {
                        if obj.defensa_magica_max > 0 {
                            damage -= rand_range(obj.defensa_magica_min, obj.defensa_magica_max);
                        }
                    }
                }
            }
            // Ring magic defense
            if ring_slot > 0 && ring_slot <= crate::game::types::MAX_INVENTORY_SLOTS {
                let ring_obj_idx = state.users.get(&target_conn)
                    .map(|u| u.inventory[ring_slot - 1].obj_index).unwrap_or(0);
                if ring_obj_idx > 0 {
                    if let Some(obj) = state.game_data.objects.get(ring_obj_idx as usize) {
                        if obj.defensa_magica_max > 0 {
                            damage -= rand_range(obj.defensa_magica_min, obj.defensa_magica_max);
                        }
                    }
                }
            }
            damage = damage.max(1);

            // Apply damage
            if let Some(user) = state.users.get_mut(&target_conn) {
                user.min_hp -= damage;
            }

            // Send damage console message (VB6: msg 830 — NpcName@Damage)
            state.send_msg_id(target_conn, 830, &format!("{}@{}", npc_name, damage));
            send_stats_hp(state, target_conn).await;

            // Check death
            let hp = state.users.get(&target_conn).map(|u| u.min_hp).unwrap_or(0);
            if hp <= 0 {
                user_die(state, target_conn, None).await;
            }
        }
        _ => {}
    }

    // Paralysis effect — VB6: SUPERANILLO (700) blocks NPC paralysis
    if spell.paraliza {
        const SUPERANILLO: i32 = 700;
        let ring_slot = state.users.get(&target_conn).map(|u| u.equip.ring).unwrap_or(0);
        let ring_obj = if ring_slot > 0 && ring_slot <= crate::game::types::MAX_INVENTORY_SLOTS {
            state.users.get(&target_conn).map(|u| u.inventory[ring_slot - 1].obj_index).unwrap_or(0)
        } else { 0 };
        if ring_obj == SUPERANILLO {
            state.send_console(target_conn, "Tu anillo rechaza los efectos del hechizo.", font_index::INFO);
        } else {
            if let Some(user) = state.users.get_mut(&target_conn) {
                user.paralyzed = true;
                user.counter_paralisis = state.intervals.paralizado;
            }
            let duration_secs = (state.intervals.paralizado as f32 * 0.04) as i16;
            let pkt = binary_packets::write_paralize_ok(duration_secs);
            state.send_bytes(target_conn, &pkt);
        }
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

    // Check bounds using actual grid dimensions (not hardcoded 100)
    let bounds_ok = state.world.grid(map)
        .map(|g| world::in_map_bounds_grid(g, new_x, new_y))
        .unwrap_or(false);
    if !bounds_ok {
        return (false, None);
    }

    // Zone confinement: NPC with zone_id > 0 can't leave its zone bounds
    let npc_zone_id = state.get_npc(npc_idx).map(|n| n.zone_id).unwrap_or(0);
    if npc_zone_id > 0 {
        let map_idx = map as usize;
        let zone_allows = state.game_data.maps.get(map_idx)
            .and_then(|m| m.as_ref())
            .and_then(|gm| gm.zones.as_ref())
            .and_then(|zs| zs.zones.iter().find(|z| z.id == npc_zone_id))
            .map(|z| z.contains(new_x, new_y))
            .unwrap_or(true);
        if !zone_allows {
            return (false, None);
        }
    }

    // Zone boundary: NPC can't wander beyond configured radius from spawn
    let (orig_x, orig_y) = state.get_npc(npc_idx)
        .map(|n| (n.orig_x, n.orig_y)).unwrap_or((0, 0));
    if let Some(zone) = state.get_zone(map) {
        if zone.npc_wander_radius > 0 {
            let dist = (new_x - orig_x).abs().max((new_y - orig_y).abs());
            if dist > zone.npc_wander_radius {
                return (false, None);
            }
        }
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
            if !state.world.grid(map).map(|g| world::in_map_bounds_grid(g, nx, ny)).unwrap_or(false) { continue; }
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
/// VB6: CheckElementales=True (default) → ALL pets respond.
///       CheckElementales=False → exclude FUEGO (93) and TIERRA (94).
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

        // VB6: If CheckElementales=False, skip FUEGO (93) and TIERRA (94)
        // (CheckElementales OR (Numero <> FUEGO AND Numero <> TIERRA))
        if !check_elementals && (pet_number == npc::ELEMENTAL_FUEGO || pet_number == npc::ELEMENTAL_TIERRA) {
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
