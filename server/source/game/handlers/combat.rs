//! Combat handlers: melee/ranged attack, PvP damage, user_die, combat formulas.
//! Extracted from mod.rs to reduce file size.
//! VB6 13.3 parity: UsuarioImpacto, UsuarioAtacaUsuario, UserDañoUser, CalcularDaño,
//! PoderAtaqueArma, PoderEvasion, PoderEvasionEscudo, DoApuñalar, DoGolpeCritico.

#[path = "combat_pvp.rs"]
mod combat_pvp;
#[path = "combat_npc.rs"]
mod combat_npc;

use combat_pvp::*;
pub(crate) use combat_npc::*;

use tracing::info;
use crate::net::ConnectionId;
use crate::game::class_race::PlayerClass;
use crate::game::types::{GameState, SendTarget, MAX_INVENTORY_SLOTS};
use crate::game::world;
use crate::protocol::{font_index, binary_packets};
use super::common::*;
use crate::game::constants::*;
use super::{
    user_attack_npc, check_user_level, warp_user, send_full_inventory,
};
use super::npcs::fire_elemental_react;
use super::skills::{
    try_desarmar, try_level_skill, try_level_skill_with_hit,
};

// =====================================================================
// VB6 Combat Constants
// =====================================================================

// Combat constants imported from crate::game::constants

// =====================================================================
// VB6 Attack Power formulas (exact replicas)
// =====================================================================

/// VB6: PoderAtaqueArma — melee weapon attack power.
/// `skill` = UserSkills(eSkill.Armas), `agility` = UserAtributos(Agilidad),
/// `level` = ELV, `class_mod` = ModClase(clase).AtaqueArmas
pub(super) fn poder_ataque_arma(skill: i32, agility: i32, level: i32, class_mod: f32) -> i64 {
    // VB6: PoderAtaqueArma — use f64 arithmetic to match VB6 Single multiplication precision.
    let temp = if skill < 31 {
        (skill as f64 * class_mod as f64) as i64
    } else if skill < 61 {
        ((skill + agility) as f64 * class_mod as f64) as i64
    } else if skill < 91 {
        ((skill + 2 * agility) as f64 * class_mod as f64) as i64
    } else {
        ((skill + 3 * agility) as f64 * class_mod as f64) as i64
    };
    temp + (2.5 * (level - 12).max(0) as f64) as i64
}

/// VB6: PoderAtaqueProyectil — ranged weapon attack power.
/// Same formula as PoderAtaqueArma but uses Proyectiles skill and ModClase.AtaqueProyectiles.
pub(super) fn poder_ataque_proyectil(skill: i32, agility: i32, level: i32, class_mod: f32) -> i64 {
    // Identical structure to PoderAtaqueArma
    poder_ataque_arma(skill, agility, level, class_mod)
}

/// VB6: PoderAtaqueWrestling — unarmed attack power.
/// Same formula but uses Wrestling skill and ModClase.AtaqueWrestling.
pub(super) fn poder_ataque_wrestling(skill: i32, agility: i32, level: i32, class_mod: f32) -> i64 {
    poder_ataque_arma(skill, agility, level, class_mod)
}

/// VB6: PoderEvasion — evasion power.
/// Formula: (Tacticas + (Tacticas\33) * Agility) * ModClase.Evasion + 2.5 * max(0, Level-12)
/// Note: VB6 uses integer division (backslash operator) for Tacticas\33.
pub(super) fn poder_evasion(tacticas: i32, agility: i32, level: i32, class_mod: f32) -> i64 {
    let temp = (tacticas as f64 + ((tacticas / 33) as f64 * agility as f64)) * class_mod as f64;
    temp as i64 + (2.5 * (level - 12).max(0) as f64) as i64
}

/// VB6: PoderEvasionEscudo — shield evasion bonus.
/// Formula: (SkillDefensa * ModClase.Escudo) / 2
pub(super) fn poder_evasion_escudo(skill_defensa: i32, class_mod_escudo: f32) -> i64 {
    ((skill_defensa as f64 * class_mod_escudo as f64) / 2.0) as i64
}

// =====================================================================
// VB6 CalcularDaño (exact replica)
// =====================================================================

/// VB6: CalcularDaño — damage calculation for user attacks.
/// Returns damage based on weapon type (melee/ranged/wrestling).
///
/// For weapons: `(3 * WeaponDmg + (WeaponMaxHIT/5 * max(0, STR-15)) + UserHIT) * ModClase`
/// For wrestling (no weapon): base 4-9 damage, uses ModClase.DañoWrestling
///
/// Parameters:
/// - `weapon_obj_index`: equipped weapon object index (0 = unarmed)
/// - `weapon_is_proyectil`: whether equipped weapon is ranged
/// - `weapon_min_hit`, `weapon_max_hit`: weapon's MinHIT/MaxHIT from ObjData
/// - `ammo_min_hit`, `ammo_max_hit`: munition's MinHIT/MaxHIT (if weapon uses ammo)
/// - `has_ammo`: whether weapon uses munition and munition is equipped
/// - `user_min_hit`, `user_max_hit`: user's MinHIT/MaxHIT (Stats.MinHIT/MaxHIT)
/// - `strength`: user's Fuerza attribute
/// - `class_mod`: appropriate ModClase damage modifier for the weapon type
pub(super) fn calcular_dano(
    weapon_obj_index: i32,
    weapon_is_proyectil: bool,
    weapon_min_hit: i32,
    weapon_max_hit: i32,
    has_ammo: bool,
    ammo_min_hit: i32,
    ammo_max_hit: i32,
    user_min_hit: i32,
    user_max_hit: i32,
    strength: i32,
    class_mod: f64,
    _ring_obj_index: i32,
    _ring_is_guante: bool,
    _ring_min_hit: i32,
    _ring_max_hit: i32,
) -> i64 {
    let (dano_arma, dano_max_arma);

    if weapon_obj_index > 0 {
        // Has weapon equipped
        let mut dmg = rand_range(weapon_min_hit.max(1), weapon_max_hit.max(1)) as i64;
        let max_dmg = weapon_max_hit as i64;

        if weapon_is_proyectil && has_ammo {
            dmg += rand_range(ammo_min_hit.max(0), ammo_max_hit.max(0)) as i64;
            // VB6: does NOT add ammo max to DañoMaxArma (commented out in VB6 source)
        }

        dano_arma = dmg;
        dano_max_arma = max_dmg;
    } else {
        // Wrestling (unarmed) — base damage 4-9
        // VB6: Plus de guantes (en slot de anillo)
        let mut min_dmg = 4i64;
        let mut max_dmg = 9i64;

        if _ring_is_guante {
            min_dmg += _ring_min_hit as i64;
            max_dmg += _ring_max_hit as i64;
        }

        dano_arma = rand_range(min_dmg as i32, max_dmg as i32) as i64;
        dano_max_arma = max_dmg;
    }

    let dano_usuario = rand_range(user_min_hit.max(0), user_max_hit.max(1)) as i64;

    // VB6: (3 * DañoArma + (DañoMaxArma/5 * max(0, Fuerza-15)) + DañoUsuario) * ModifClase
    let raw = 3 * dano_arma + (dano_max_arma / 5 * (strength - 15).max(0) as i64) + dano_usuario;
    (raw as f64 * class_mod) as i64
}

// =====================================================================
// VB6 DoApuñalar (exact polynomial formula)
// =====================================================================

/// VB6: DoApuñalar — backstab attack.
/// Returns additional damage dealt and whether it succeeded.
/// Uses polynomial luck formula per class, then applies damage multiplier.
pub(super) fn do_apunalar(
    skill: i32,
    class: PlayerClass,
    base_damage: i64,
    is_npc_target: bool,
) -> Option<i64> {
    let s = skill as f64;

    // VB6 polynomial luck formula per class
    let suerte = match class {
        PlayerClass::Asesino => ((0.00003 * s - 0.002) * s + 0.098) * s + 4.25,
        PlayerClass::Clerigo | PlayerClass::Paladin | PlayerClass::Pirata => {
            ((0.000003 * s + 0.0006) * s + 0.0107) * s + 4.93
        }
        PlayerClass::Bardo => ((0.000002 * s + 0.0002) * s + 0.032) * s + 4.81,
        _ => 0.0361 * s + 4.39,
    };

    let suerte = suerte as i32;

    if rand_range(0, 100) < suerte {
        let dmg = if is_npc_target {
            base_damage * 2
        } else {
            if class == PlayerClass::Asesino {
                (base_damage as f64 * 1.4).round() as i64
            } else {
                (base_damage as f64 * 1.5).round() as i64
            }
        };
        Some(dmg)
    } else {
        None
    }
}

/// VB6: PuedeApuñalar — check if user can backstab.
/// Requirements: weapon.Apuñala == 1 AND (skill >= MIN_APUÑALAR OR class == Asesino).
pub(super) fn puede_apunalar(class: PlayerClass, weapon_apunala: bool, skill_apunalar: i32) -> bool {
    weapon_apunala && (skill_apunalar > 0 || class == PlayerClass::Asesino)
}

// =====================================================================
// VB6 DoGolpeCritico (Bandido + Espada Vikinga only)
// =====================================================================

/// VB6: DoGolpeCritico — critical hit, ONLY for Bandido class with Espada Vikinga.
/// Returns additional damage dealt if critical succeeds.
pub(super) fn do_golpe_critico(
    class: PlayerClass,
    weapon_obj_index: i32,
    wrestling_skill: i32,
    base_damage: i64,
) -> Option<i64> {
    // VB6: Only Bandido with Espada Vikinga can do critical hits
    if class != PlayerClass::Bandido {
        return None;
    }
    if weapon_obj_index != ESPADA_VIKINGA {
        return None;
    }

    let s = wrestling_skill as f64;
    let suerte = (((0.00000003 * s + 0.000006) * s + 0.000107) * s + 0.0893) * 100.0;
    let suerte = suerte as i32;

    if rand_range(1, 100) <= suerte {
        let dmg = (base_damage as f64 * 0.75) as i64;
        Some(dmg)
    } else {
        None
    }
}

// =====================================================================
// VB6 DoDesequipar — unequip victim's item (shield → weapon → helmet)
// =====================================================================

/// VB6: DoDesequipar — try to unequip victim's shield, weapon, or helmet.
/// Uses 3 sequential rolls: first shield, if miss try weapon, if miss try helmet.
/// VB6 formula: prob = wrestling * 0.2 + level * 0.66
/// Returns true if something was unequipped.
async fn do_desequipar(state: &mut GameState, victim_id: ConnectionId, prob: i32) -> bool {
    if let Some(victim) = state.users.get_mut(&victim_id) {
        // Roll 1: Try shield
        if victim.equip.shield > 0 && rand_range(1, 100) <= prob {
            victim.equip.shield = 0;
            return true;
        }
        // Roll 2: Try weapon
        if victim.equip.weapon > 0 && rand_range(1, 100) <= prob {
            victim.equip.weapon = 0;
            return true;
        }
        // Roll 3: Try helmet
        if victim.equip.helmet > 0 && rand_range(1, 100) <= prob {
            victim.equip.helmet = 0;
            return true;
        }
    }
    false
}

// =====================================================================
// VB6 DoAcuchillar (Pirate throat cut — Trabajo.bas:1991)
// =====================================================================

/// VB6: PuedeAcuchillar — checks Pirate class + weapon Acuchilla flag.
pub(super) fn puede_acuchillar(class: PlayerClass, weapon_acuchilla: bool) -> bool {
    class == PlayerClass::Pirata && weapon_acuchilla
}

/// VB6: DoAcuchillar — Pirate throat cut attack.
/// 20% chance, deals 20% of base damage as additional damage.
/// Works on both user and NPC targets.
pub(super) fn do_acuchillar(base_damage: i64) -> Option<i64> {
    if rand_range(1, 100) <= PROB_ACUCHILLAR {
        let dmg = (base_damage as f64 * DANO_ACUCHILLAR) as i64;
        Some(dmg)
    } else {
        None
    }
}

// =====================================================================
// Helper: get weapon info from user state
// =====================================================================

pub(super) struct WeaponInfo {
    pub obj_index: i32,
    pub is_proyectil: bool,
    pub min_hit: i32,
    pub max_hit: i32,
    pub refuerzo: i32,
    pub envenena: bool,
    pub has_ammo: bool,
    pub ammo_min_hit: i32,
    pub ammo_max_hit: i32,
    pub acuchilla: bool,
    pub apunala: bool,
}

pub(super) fn get_weapon_info(state: &GameState, conn_id: ConnectionId) -> WeaponInfo {
    let user = match state.users.get(&conn_id) {
        Some(u) => u,
        None => return WeaponInfo {
            obj_index: 0, is_proyectil: false, min_hit: 0, max_hit: 0,
            refuerzo: 0, envenena: false, has_ammo: false, ammo_min_hit: 0, ammo_max_hit: 0,
            acuchilla: false, apunala: false,
        },
    };

    if user.equip.weapon == 0 || user.equip.weapon > MAX_INVENTORY_SLOTS {
        return WeaponInfo {
            obj_index: 0, is_proyectil: false, min_hit: 0, max_hit: 0,
            refuerzo: 0, envenena: false, has_ammo: false, ammo_min_hit: 0, ammo_max_hit: 0,
            acuchilla: false, apunala: false,
        };
    }

    let obj_idx = user.inventory[user.equip.weapon - 1].obj_index;
    if obj_idx <= 0 {
        return WeaponInfo {
            obj_index: 0, is_proyectil: false, min_hit: 0, max_hit: 0,
            refuerzo: 0, envenena: false, has_ammo: false, ammo_min_hit: 0, ammo_max_hit: 0,
            acuchilla: false, apunala: false,
        };
    }

    let (is_proy, w_min, w_max, refuerzo, envenena, uses_ammo, acuchilla, apunala) = match state.get_object(obj_idx) {
        Some(o) => (o.proyectil, o.min_hit, o.max_hit, o.refuerzo, o.envenena, o.municion > 0, o.acuchilla, o.apunala),
        None => (false, 0, 0, 0, false, false, false, false),
    };

    let (has_ammo, ammo_min, ammo_max) = if is_proy && uses_ammo {
        if user.equip.municion > 0 && user.equip.municion <= MAX_INVENTORY_SLOTS {
            let ammo_idx = user.inventory[user.equip.municion - 1].obj_index;
            match state.get_object(ammo_idx) {
                Some(a) => (true, a.min_hit, a.max_hit),
                None => (false, 0, 0),
            }
        } else {
            (false, 0, 0)
        }
    } else {
        (false, 0, 0)
    };

    WeaponInfo {
        obj_index: obj_idx,
        is_proyectil: is_proy,
        min_hit: w_min,
        max_hit: w_max,
        refuerzo,
        envenena,
        has_ammo,
        ammo_min_hit: ammo_min,
        ammo_max_hit: ammo_max,
        acuchilla,
        apunala,
    }
}

/// Get ring/glove info for wrestling bonus.
/// VB6: If ring slot has GUANTE_HURTO (873), its MinHIT/MaxHIT add to wrestling damage.
pub(super) fn get_ring_info(state: &GameState, conn_id: ConnectionId) -> (i32, bool, i32, i32) {
    let user = match state.users.get(&conn_id) {
        Some(u) => u,
        None => return (0, false, 0, 0),
    };
    if user.equip.ring == 0 || user.equip.ring > MAX_INVENTORY_SLOTS {
        return (0, false, 0, 0);
    }
    let obj_idx = user.inventory[user.equip.ring - 1].obj_index;
    // VB6: GUANTE_HURTO (873) adds min_hit/max_hit to wrestling base damage (4-9)
    if obj_idx == GUANTE_HURTO {
        let (g_min, g_max) = state.get_object(obj_idx)
            .map(|o| (o.min_hit, o.max_hit))
            .unwrap_or((0, 0));
        (obj_idx, true, g_min, g_max)
    } else {
        (obj_idx, false, 0, 0)
    }
}

// =====================================================================
// AT — Melee/ranged attack handler
// =====================================================================

/// AT — Melee/ranged attack.
pub(super) async fn handle_attack(state: &mut GameState, conn_id: ConnectionId) {
    let user_data = match state.users.get(&conn_id) {
        Some(u) if u.logged => (
            u.pos_map, u.pos_x, u.pos_y, u.heading, u.char_index,
            u.dead, u.safe_toggle, u.level,
            u.attributes[0], // Strength
            u.attributes[1], // Agility
            u.min_hit, u.max_hit,
            u.skills[1], // SK2 = Armas (combat skill)
            u.skills[5], // SK6 = Proyectiles
            u.skills[3], // SK4 = Tacticas
            u.skills[4], // SK5 = Defensa
            u.skills[20], // SK21 = Wrestling
            u.skills[8], // SK9 = Apuñalar
            u.char_name.clone(),
            u.class,
        ),
        _ => return,
    };
    let (map, x, y, heading, char_index, dead, safe_on, level,
         strength, agility, min_hit, max_hit,
         skill_armas, skill_proyectiles, _skill_tacticas, _skill_defensa, skill_wrestling, skill_apunalar,
         attacker_name, class) = user_data;

    if dead {
        return;
    }

    // VB6: HandleAttack exits early if Meditando
    let is_meditating = state.users.get(&conn_id).map(|u| u.meditating).unwrap_or(false);
    if is_meditating {
        return;
    }

    // VB6 13.3 parity: Attacking reveals hidden (stealth) but NOT invisible (spell).
    // Spell invisibility runs on its own timer and is NOT broken by combat.
    let was_hidden = state.users.get(&conn_id)
        .map(|u| u.hidden && !u.admin_invisible)
        .unwrap_or(false);
    if was_hidden {
        if let Some(user) = state.users.get_mut(&conn_id) {
            user.hidden = false;
            user.counter_oculto = 0;
        }
        // Send CC+CD only to non-clanmates (they had CharacterRemove).
        // Clanmates get SetInvisible(false) — avoids animation reset/tosqueo.
        let ci = char_index.0 as i16;
        let nover = binary_packets::write_set_invisible(ci, false, 0);
        if let Some(u) = state.users.get(&conn_id) {
            let cc = u.build_cc_binary();
            let cd = build_cd_binary(u);
            let (px, py) = (u.pos_x, u.pos_y);
            let area_users = state.get_area_users(map, px, py, conn_id);
            for other_id in area_users {
                if same_clan(state, conn_id, other_id) {
                    state.send_bytes(other_id, &nover);
                } else {
                    state.send_bytes(other_id, &cc);
                    state.send_bytes(other_id, &cd);
                }
            }
        }
        state.send_bytes(conn_id, &nover);
        state.send_console(conn_id, "Has vuelto a ser visible.", font_index::INFO);
    }

    // VB6: If equipped weapon is ranged, block melee attack — "No puedes usar así este arma."
    {
        let weapon_slot = state.users.get(&conn_id).map(|u| u.equip.weapon).unwrap_or(0);
        if weapon_slot > 0 && weapon_slot <= MAX_INVENTORY_SLOTS {
            let weapon_obj = state.users.get(&conn_id)
                .map(|u| u.inventory[weapon_slot - 1].obj_index).unwrap_or(0);
            if weapon_obj > 0 {
                if state.get_object(weapon_obj).map(|o| o.proyectil).unwrap_or(false) {
                    state.send_console(conn_id, "No puedes usar así este arma.", font_index::INFO);
                    return;
                }
            }
        }
    }

    // Anti-cheat: check melee cooldown
    if !puede_pegar(state, conn_id) {
        return;
    }

    // VB6 13.3 parity: melee attacks require min 10 stamina, deduct random 1-10.
    {
        let min_sta = state.users.get(&conn_id).map(|u| u.min_sta).unwrap_or(0);
        if min_sta < 10 {
            state.send_console(conn_id, "No tienes suficiente energía para atacar.", font_index::INFO);
            return;
        }
        let sta_cost = rand_range(1, 10);
        if let Some(user) = state.users.get_mut(&conn_id) {
            user.min_sta = (user.min_sta - sta_cost).max(0);
        }
        send_stats_sta(state, conn_id).await;
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
    let swing_pkt = binary_packets::write_play_wave(2, x as i16, y as i16);
    state.send_data_bytes(
        SendTarget::ToArea { map, x, y },
        &swing_pkt,
    );

    if let Some(victim_id) = target_conn {
        // PvP attack — check guild war bypass before safety toggle
        let (attacker_guild, attacker_seguro) = state.users.get(&conn_id)
            .map(|u| (u.guild_index, u.seguro_clan)).unwrap_or((0, false));
        let victim_guild = state.users.get(&victim_id).map(|u| u.guild_index).unwrap_or(0);

        // Guild war bypasses safety toggle
        let guilds_at_war = attacker_guild > 0 && victim_guild > 0
            && attacker_guild != victim_guild
            && super::guilds_handler::get_guild_relation(state, attacker_guild, victim_guild) == super::guilds_handler::GUILD_REL_WAR;

        if safe_on && !guilds_at_war {
            // VB6 13.3 parity: safety toggle only blocks attacking citizens (non-criminals).
            // Attacking criminals is always allowed.
            let victim_is_criminal = state.users.get(&victim_id).map(|u| u.criminal).unwrap_or(false);
            if !victim_is_criminal {
                state.send_msg_id(conn_id, 207, "");
                return;
            }
        }

        // Clan safe check
        if attacker_guild > 0 && attacker_guild == victim_guild && attacker_seguro {
            state.send_console(conn_id, "No puedes atacar a un miembro de tu clan. Usa /SEGUROCLAN para desactivar el seguro.", font_index::INFO);
            return;
        }

        // VB6: Safe zone check — dueling players bypass safe zone restriction
        let in_duel = state.users.get(&conn_id).map(|u| u.atacable_por == victim_id).unwrap_or(false)
            && state.users.get(&victim_id).map(|u| u.atacable_por == conn_id).unwrap_or(false);

        if !in_duel {
            // Zone-aware safe check (Trigger > Zone > Map hierarchy)
            if is_safe_at(state, map, x, y) {
                state.send_msg_id(conn_id, 163, "");
                return;
            }
            let victim_pos = state.users.get(&victim_id).map(|v| (v.pos_x, v.pos_y)).unwrap_or((0, 0));
            if is_safe_at(state, map, victim_pos.0, victim_pos.1) {
                state.send_msg_id(conn_id, 163, "");
                return;
            }
        }

        // VB6 13.3: ZONAPELEA — if BOTH players are in CombatZone, allow PvP without criminal penalty.
        // If only one is in CombatZone, block PvP entirely.
        let attacker_in_arena = get_map_tile_trigger(state, map, x, y) == crate::data::maps::Trigger::CombatZone;
        let victim_arena_pos = state.users.get(&victim_id).map(|v| (v.pos_map, v.pos_x, v.pos_y)).unwrap_or((0, 0, 0));
        let victim_in_arena = get_map_tile_trigger(state, victim_arena_pos.0, victim_arena_pos.1, victim_arena_pos.2) == crate::data::maps::Trigger::CombatZone;

        if attacker_in_arena != victim_in_arena {
            // One player is in the arena, the other is not — block combat entirely.
            state.send_console(conn_id, "Ambos jugadores deben estar en la zona de pelea.", font_index::INFO);
            return;
        }
        let both_in_arena = attacker_in_arena && victim_in_arena;

        let victim_data = match state.users.get(&victim_id) {
            Some(v) if v.logged => (
                v.dead, v.privileges, v.char_name.clone(),
                v.level, v.attributes[1], // Victim agility
                v.skills[3], // SK4 = Tacticas
                v.skills[4], // SK5 = Defensa
                v.max_hp, v.min_hp,
                v.class.clone(),
                v.heading,
                v.char_index,
                v.meditating,
            ),
            _ => return,
        };
        let (v_dead, v_privs, victim_name, v_level, v_agility, v_tacticas, v_defensa,
             _v_max_hp, _v_min_hp, v_class, _v_heading, v_char_index, v_meditating) = victim_data;

        if v_dead {
            state.send_msg_id(conn_id, 154, "");
            return;
        }
        if v_privs > 0 {
            state.send_msg_id(conn_id, 155, "");
            return;
        }

        // Check if victim has shield equipped
        let v_has_shield = state.users.get(&victim_id)
            .map(|v| v.equip.shield > 0 && v.equip.shield <= MAX_INVENTORY_SLOTS)
            .unwrap_or(false);

        // Get weapon info to determine attack type
        let weapon = get_weapon_info(state, conn_id);

        // VB6: UsuarioImpacto — calculate attack power based on weapon type
        let (attack_power, attack_skill_idx) = if weapon.obj_index > 0 {
            if weapon.is_proyectil {
                let mod_atk = state.game_data.balance.class_mod_ataque_proyectiles_e(class);
                (poder_ataque_proyectil(skill_proyectiles, agility, level, mod_atk), 5usize) // eSkill.Proyectiles
            } else {
                let mod_atk = state.game_data.balance.class_mod_ataque_armas_e(class);
                (poder_ataque_arma(skill_armas, agility, level, mod_atk), 1usize) // eSkill.Armas
            }
        } else {
            let mod_atk = state.game_data.balance.class_mod_ataque_wrestling_e(class);
            (poder_ataque_wrestling(skill_wrestling, agility, level, mod_atk), 20usize) // eSkill.Wrestling
        };

        // VB6: PoderEvasion for victim
        let v_evasion_mod = state.game_data.balance.class_mod_evasion_e(v_class);
        let mut victim_evasion = poder_evasion(v_tacticas, v_agility, v_level, v_evasion_mod);

        // VB6: Add shield evasion if victim has shield
        let victim_shield_evasion = if v_has_shield {
            let shield_mod = state.game_data.balance.class_mod_escudo_e(v_class);
            poder_evasion_escudo(v_defensa, shield_mod)
        } else {
            0
        };
        victim_evasion += victim_shield_evasion;

        // VB6 13.3 parity: any PvP targeting cancels victim meditation (before hit/miss)
        let victim_meditating = state.users.get(&victim_id).map(|u| u.meditating).unwrap_or(false);
        if victim_meditating {
            if let Some(victim) = state.users.get_mut(&victim_id) {
                victim.meditating = false;
            }
        }

        // VB6: ProbExito = clamp(50 + (PoderAtaque - UserPoderEvasion) * 0.4, 10, 90)
        let mut prob_exito = (50.0 + (attack_power - victim_evasion) as f64 * 0.4) as i32;
        prob_exito = prob_exito.clamp(10, 90);

        // VB6: Meditation reduces evasion by 25%
        if v_meditating {
            let prob_evadir = ((100 - prob_exito) as f64 * 0.75) as i32;
            prob_exito = (100 - prob_evadir).min(90);
        }

        let hit = rand_range(1, 100) <= prob_exito;

        // VB6: Desarmar (Ladrón class — disarm) runs regardless of hit or miss
        if class == PlayerClass::Ladron {
            let wresterling_skill = state.users.get(&conn_id).and_then(|u| u.skills.get(20).copied()).unwrap_or(0);
            if wresterling_skill > 0 && try_desarmar(wresterling_skill) {
                if let Some(victim) = state.users.get_mut(&victim_id) {
                    if victim.equip.weapon > 0 {
                        victim.equip.weapon = 0;
                    }
                }
                state.send_console(victim_id, "Te han desarmado!", font_index::FIGHT);
                state.send_console(conn_id, &format!("Has desarmado a {}!", victim_name), font_index::FIGHT);
                if let Some(u) = state.users.get_mut(&conn_id) {
                    try_level_skill(u, 20);
                }
            }
        }

        if !hit {
            // VB6: Shield block check — separate from evasion
            if v_has_shield {
                let suma_skills = (v_defensa + v_tacticas).max(1);
                let prob_rechazo = ((100 * v_defensa / suma_skills) as i32).clamp(10, 90);
                let rechazo = rand_range(1, 100) <= prob_rechazo;

                if rechazo {
                    // Shield block — VB6: SND_ESCUDO + messages
                    let (vx, vy) = state.users.get(&victim_id).map(|v| (v.pos_x, v.pos_y)).unwrap_or((0, 0));
                    let snd = binary_packets::write_play_wave(37, vx as i16, vy as i16); // SND_ESCUDO
                    state.send_data_bytes(SendTarget::ToArea { map, x: vx, y: vy }, &snd);

                    let pkt_atk = binary_packets::write_multi_msg_simple(crate::protocol::packets::MultiMessageID::BlockedWithShieldOther);
                    state.send_bytes(conn_id, &pkt_atk);
                    let pkt_vic = binary_packets::write_multi_msg_simple(crate::protocol::packets::MultiMessageID::BlockedWithShieldUser);
                    state.send_bytes(victim_id, &pkt_vic);

                    // VB6: SubirSkill Defensa on block success
                    if let Some(victim) = state.users.get_mut(&victim_id) {
                        try_level_skill_with_hit(victim, 4, true); // Defensa skill
                    }
                } else {
                    // Failed block — still skill gain (failure)
                    if let Some(victim) = state.users.get_mut(&victim_id) {
                        try_level_skill_with_hit(victim, 4, false);
                    }
                }
            }

            // Miss
            let snd = binary_packets::write_play_wave(2, x as i16, y as i16);
            state.send_data_bytes(SendTarget::ToArea { map, x, y }, &snd);
            let pkt = binary_packets::write_multi_user_attacked_swing(char_index.0 as i16);
            state.send_bytes(victim_id, &pkt);
            let pkt = binary_packets::write_multi_msg_simple(crate::protocol::packets::MultiMessageID::UserSwing);
            state.send_bytes(conn_id, &pkt);
            state.send_chat_over_head_to(SendTarget::ToArea { map, x, y }, "\u{00A1}Fallo!", v_char_index.0 as i16, 255);

            // VB6: SubirSkill (attacker skill on miss)
            if let Some(u) = state.users.get_mut(&conn_id) {
                try_level_skill_with_hit(u, attack_skill_idx, false);
            }
            // VB6: SubirSkill Tacticas (victim gains on dodge)
            if let Some(victim) = state.users.get_mut(&victim_id) {
                try_level_skill_with_hit(victim, 3, true); // Tacticas
            }
            return;
        }

        // HIT — VB6: UsuarioAtacaUsuario flow after UsuarioImpacto = True

        // VB6: SND_IMPACTO to area
        let snd = binary_packets::write_play_wave(10, x as i16, y as i16);
        state.send_data_bytes(SendTarget::ToArea { map, x, y }, &snd);

        // VB6: Blood FX on victim (if not navigating)
        // (client handles this via MultiMessage)

        // VB6: DoDesequipar — requires Bandido class + pickpocket gloves (ring=873) + unarmed
        {
            let (has_gloves, is_unarmed, wrestling_sk, attacker_level, is_bandido) = state.users.get(&conn_id)
                .map(|u| {
                    let ring_idx = if u.equip.ring > 0 && u.equip.ring <= MAX_INVENTORY_SLOTS {
                        u.inventory[u.equip.ring - 1].obj_index
                    } else { 0 };
                    (ring_idx == GUANTE_HURTO, u.equip.weapon == 0, u.skills[20], u.level, u.class == PlayerClass::Bandido)
                })
                .unwrap_or((false, false, 0, 0, false));

            if is_bandido && has_gloves && is_unarmed {
                // VB6: Probabilidad = Wrestling*0.2 + Level*0.66
                let prob = (wrestling_sk as f64 * 0.2 + attacker_level as f64 * 0.66) as i32;

                // VB6: 3 sequential rolls (shield, weapon, helmet) inside do_desequipar
                let unequipped = do_desequipar(state, victim_id, prob).await;
                if unequipped {
                    // Send CP to area to update victim appearance
                    if let Some(v) = state.users.get(&victim_id) {
                        let cp = binary_packets::write_character_change(
                            v.char_index.0 as i16, v.body as i16, v.head as i16, v.heading as u8,
                            v.weapon_anim as i16, v.shield_anim as i16, v.casco_anim as i16, 0, 0,
                        );
                        state.send_data_bytes(SendTarget::ToArea { map, x, y }, &cp);
                    }
                    state.send_console(conn_id, &format!("Has desarmado a {}!", victim_name), font_index::FIGHT);
                    state.send_console(victim_id, "Te han quitado un equipo!", font_index::FIGHT);
                }
            }

            // VB6: DoHandInmo — Thief only + gloves, paralyze for half duration
            if has_gloves && class == PlayerClass::Ladron {
                let v_paralyzed = state.users.get(&victim_id).map(|u| u.paralyzed).unwrap_or(true);
                if !v_paralyzed {
                    // VB6: prob = Wrestling / 4
                    let prob = wrestling_sk / 4;
                    if rand_range(0, 100) < prob {
                        let half_para = state.intervals.paralizado / 2;
                        if let Some(victim) = state.users.get_mut(&victim_id) {
                            victim.paralyzed = true;
                            victim.counter_paralisis = half_para;
                            victim.paralyzed_by = Some(conn_id);
                        }
                        let para_secs = (half_para as f32 * 0.04) as i16;
                        let pkt = binary_packets::write_paralize_ok(para_secs);
                        state.send_bytes(victim_id, &pkt);
                        if let Some(u) = state.users.get(&victim_id) {
                            let pu = binary_packets::write_pos_update(u.pos_x as i16, u.pos_y as i16);
                            state.send_bytes(victim_id, &pu);
                        }
                        state.send_console(conn_id, &format!("Has paralizado a {}!", victim_name), font_index::FIGHT);
                        state.send_console(victim_id, "Has sido paralizado!", font_index::FIGHT);
                    }
                }
            }
        }

        // VB6: SubirSkill Tacticas (victim on hit — failure)
        if let Some(victim) = state.users.get_mut(&victim_id) {
            try_level_skill_with_hit(victim, 3, false); // Tacticas failed
        }

        // === VB6: UserDañoUser ===

        // Calculate damage using VB6 CalcularDaño
        let class_mod_damage = if weapon.obj_index > 0 {
            if weapon.is_proyectil {
                state.game_data.balance.class_mod_dano_proyectiles_e(class) as f64
            } else {
                state.game_data.balance.class_mod_dano_armas_e(class) as f64
            }
        } else {
            state.game_data.balance.class_mod_dano_wrestling_e(class) as f64
        };

        let (ring_idx, ring_guante, ring_min, ring_max) = get_ring_info(state, conn_id);
        let mut damage = calcular_dano(
            weapon.obj_index, weapon.is_proyectil,
            weapon.min_hit, weapon.max_hit,
            weapon.has_ammo, weapon.ammo_min_hit, weapon.ammo_max_hit,
            min_hit, max_hit,
            strength, class_mod_damage,
            ring_idx, ring_guante, ring_min, ring_max,
        );

        // VB6: EspadaMataDragonesIndex (402) — always deals 1 damage to players
        if weapon.obj_index == ESPADA_MATA_DRAGONES {
            damage = 1;
        } else {
            // VB6: Weapon Refuerzo (penetration) adds to damage
            damage += weapon.refuerzo as i64;
        }

        // VB6: Body part hit (1=head, 2-6=body)
        let lugar = rand_range(BODY_PART_HEAD, BODY_PART_TORSO);

        // VB6: Armor absorption (skip for Dragon Slayer — always 1)
        if weapon.obj_index != ESPADA_MATA_DRAGONES {
            let (head_defense, body_defense) = calc_pvp_armor_absorption(state, victim_id, lugar);
            damage = damage - head_defense as i64 - body_defense as i64;
        }

        // VB6: if damage < 0 then damage = 1
        if damage < 0 { damage = 1; }

        // Send hit messages
        let n4_pkt = binary_packets::write_multi_user_hitted_by_user(char_index.0 as i16, lugar as u8, damage as i16);
        state.send_bytes(victim_id, &n4_pkt);
        let n5_pkt = binary_packets::write_multi_user_hitted_user(v_char_index.0 as i16, lugar as u8, damage as i16);
        state.send_bytes(conn_id, &n5_pkt);

        // VB6: floating yellow damage number
        state.send_chat_over_head_to(SendTarget::ToArea { map, x, y }, &format!("-{}", damage), v_char_index.0 as i16, 65535);

        // Apply damage to victim
        if let Some(victim) = state.users.get_mut(&victim_id) {
            victim.min_hp = victim.min_hp.saturating_sub(damage as i32);
        }

        // VB6 13.3 parity: send meditation cancel packets if victim was meditating when targeted
        // (victim.meditating was already set to false earlier, before the hit/miss roll)
        if v_meditating {
            state.send_bytes(victim_id, &binary_packets::write_meditate_toggle());
            if let Some(v) = state.users.get(&victim_id) {
                let fx_clear = binary_packets::write_create_fx(v.char_index.0 as i16, 0, 0);
                state.send_data_bytes(
                    SendTarget::ToArea { map: v.pos_map, x: v.pos_x, y: v.pos_y },
                    &fx_clear,
                );
            }
        }

        // VB6: Weapon poison application (60% chance if weapon has Envenena=1)
        if weapon.envenena && rand_range(1, 100) < 60 {
            let already_poisoned = state.users.get(&victim_id).map(|u| u.poisoned).unwrap_or(true);
            if !already_poisoned {
                if let Some(victim) = state.users.get_mut(&victim_id) {
                    victim.poisoned = true;
                    victim.counter_poison = 0;
                }
                state.send_msg_id(victim_id, 171, &attacker_name);
                state.send_msg_id(conn_id, 172, &victim_name);
            }
        }

        // VB6: SubirSkill (attacker weapon skill on hit)
        // VB6: if .flags.Hambre = 0 And .flags.Sed = 0 — we don't track hunger/thirst flags yet
        {
            if weapon.obj_index > 0 {
                if weapon.is_proyectil {
                    if let Some(u) = state.users.get_mut(&conn_id) {
                        try_level_skill_with_hit(u, 5, true); // Proyectiles
                    }
                } else {
                    if let Some(u) = state.users.get_mut(&conn_id) {
                        try_level_skill_with_hit(u, 1, true); // Armas
                    }
                }
            } else {
                if let Some(u) = state.users.get_mut(&conn_id) {
                    try_level_skill_with_hit(u, 20, true); // Wrestling
                }
            }

            // VB6: DoApuñalar (backstab attempt)
            if puede_apunalar(class, weapon.apunala, skill_apunalar) {
                if let Some(stab_dmg) = do_apunalar(skill_apunalar, class, damage, false) {
                    if let Some(victim) = state.users.get_mut(&victim_id) {
                        victim.min_hp = victim.min_hp.saturating_sub(stab_dmg as i32);
                    }
                    state.send_console(conn_id, &format!("Has apuñalado a {} por {}", victim_name, stab_dmg), font_index::FIGHT);
                    state.send_console(victim_id, &format!("Te ha apuñalado {} por {}", attacker_name, stab_dmg), font_index::FIGHT);
                    if let Some(u) = state.users.get_mut(&conn_id) {
                        try_level_skill_with_hit(u, 8, true); // Apuñalar
                    }
                } else {
                    state.send_console(conn_id, "\u{00A1}No has logrado apuñalar a tu enemigo!", font_index::FIGHT);
                    if let Some(u) = state.users.get_mut(&conn_id) {
                        try_level_skill_with_hit(u, 8, false);
                    }
                }
            }

            // Early death check — stop further damage if victim already dead after backstab
            if state.users.get(&victim_id).map(|u| u.min_hp <= 0).unwrap_or(false) {
                // Skip remaining special attacks (crit + cut) — victim is already dead
            } else {

            // VB6: DoGolpeCritico (Bandido + Espada Vikinga only)
            let wrestling_sk = state.users.get(&conn_id).map(|u| u.skills[20]).unwrap_or(0);
            if let Some(crit_dmg) = do_golpe_critico(class, weapon.obj_index, wrestling_sk, damage) {
                if let Some(victim) = state.users.get_mut(&victim_id) {
                    victim.min_hp = victim.min_hp.saturating_sub(crit_dmg as i32);
                }
                state.send_console(conn_id, &format!("Has golpeado críticamente a {} por {}.", victim_name, crit_dmg), font_index::FIGHT);
                state.send_console(victim_id, &format!("{} te ha golpeado críticamente por {}.", attacker_name, crit_dmg), font_index::FIGHT);
            }

            // Early death check — stop further damage if victim already dead after crit
            if !state.users.get(&victim_id).map(|u| u.min_hp <= 0).unwrap_or(false) {

            // VB6: DoAcuchillar (Pirate throat cut — projectile PvP + melee NPC)
            // In PvP: only on projectile attacks (VB6 SistemaCombate.bas:1272)
            if weapon.is_proyectil && puede_acuchillar(class, weapon.acuchilla) {
                if let Some(cut_dmg) = do_acuchillar(damage) {
                    if let Some(victim) = state.users.get_mut(&victim_id) {
                        victim.min_hp = victim.min_hp.saturating_sub(cut_dmg as i32);
                    }
                    state.send_console(conn_id, &format!("Has acuchillado a {} por {}", victim_name, cut_dmg), font_index::FIGHT);
                    state.send_console(victim_id, &format!("{} te ha acuchillado por {}", attacker_name, cut_dmg), font_index::FIGHT);
                }
            }

            } // end early death check after crit
            } // end early death check after backstab
        }

        // VB6: Fire Elemental reacts to PvP
        fire_elemental_react(state, victim_id, &attacker_name);

        // Update victim HP
        send_stats_hp(state, victim_id).await;

        // VB6 13.3: PvP hit reputation update
        // Attack citizen: rep_bandido += 100, rep_noble halved
        // Attack criminal: rep_noble += 5
        // ZONAPELEA: skip all reputation changes when both players are in CombatZone
        if !both_in_arena {
            let victim_is_criminal = state.users.get(&victim_id).map(|u| u.criminal).unwrap_or(false);
            if victim_is_criminal {
                if let Some(attacker) = state.users.get_mut(&conn_id) {
                    attacker.rep_noble += 5;
                }
            } else {
                if let Some(attacker) = state.users.get_mut(&conn_id) {
                    attacker.rep_bandido += 100;
                    attacker.rep_noble = (attacker.rep_noble as f32 * 0.5) as i32;
                }
            }
            recalc_criminal(state, conn_id);
        }

        // Check death
        let v_hp = state.users.get(&victim_id).map(|u| u.min_hp).unwrap_or(0);
        if v_hp <= 0 {
            user_die(state, victim_id, Some(conn_id)).await;
        }
    } else {
        // Zone-aware safe check for NPC attacks
        if is_safe_at(state, map, x, y) {
            state.send_msg_id(conn_id, 164, "");
            return;
        }

        // Check for NPC on target tile
        let target_npc = state.world.grid(map)
            .and_then(|g| g.tile(target_x, target_y))
            .map(|t| t.npc_index)
            .unwrap_or(0);

        if target_npc > 0 {
            user_attack_npc(state, conn_id, target_npc as usize, map, x, y,
                            strength, agility, level, min_hit, max_hit,
                            skill_armas, &attacker_name, class).await;
        }
    }
}


// =====================================================================
// Player death
// =====================================================================

/// Handle player death.
pub(super) async fn user_die(state: &mut GameState, conn_id: ConnectionId, killer_id: Option<ConnectionId>) {
    let user_data = match state.users.get(&conn_id) {
        Some(u) => (u.pos_map, u.pos_x, u.pos_y, u.char_index, u.char_name.clone(), u.level),
        None => return,
    };
    let (map, x, y, char_index, victim_name, victim_level) = user_data;

    // Cancel active trade on death (VB6: FinComerciarUsu)
    let trade_partner = state.users.get(&conn_id).and_then(|u| {
        if u.trading { u.trade_partner } else { None }
    });
    if let Some(partner) = trade_partner {
        super::commerce::cancel_trade(state, conn_id, partner).await;
    }

    // Cancel NPC commerce on death
    if let Some(user) = state.users.get_mut(&conn_id) {
        user.comerciando = false;
    }

    // VB6: "¡Aaaahhhh!" floating text in red (vbRed=255) above dying character
    state.send_chat_over_head_to(SendTarget::ToArea { map, x, y }, "\u{00A1}Aaaahhhh!", char_index.0 as i16, 255);

    // Mark as dead, change body to dead model
    if let Some(user) = state.users.get_mut(&conn_id) {
        user.dead = true;
        user.min_hp = 0;
        user.min_sta = 0;
        // Clear status effects
        user.paralyzed = false;
        user.immobilized = false;
        user.paralyzed_by = None;
        user.invisible = false;
        user.meditating = false;
        user.resting = false;
        user.poisoned = false;
        user.montado = false;
        user.levitando = false;
        user.mimetizado = false;
        user.hidden = false;
        // VB6 UserDie: if navigating, use ghost boat (iFragataFantasmal=87), else normal dead body
        if user.navigating {
            user.body = 87; // iFragataFantasmal
        } else {
            user.body = DEAD_BODY_NEUTRAL;
        }
        user.head = DEAD_HEAD_NEUTRAL;
        user.weapon_anim = super::common::NINGUN_ARMA;
        user.shield_anim = super::common::NINGUN_ESCUDO;
        user.casco_anim = super::common::NINGUN_CASCO;
        // Clear auras
        user.aura_a = 0;
        user.aura_w = 0;
        user.aura_e = 0;
        user.aura_r = 0;
        user.aura_c = 0;
        // Resurrection cooldown
        user.time_revivir = 20;
    }

    // Clear duel state on death
    let duel_partner = state.users.get(&conn_id).map(|u| u.atacable_por).unwrap_or(0);
    if let Some(user) = state.users.get_mut(&conn_id) {
        user.atacable_por = 0;
        user.duel_pending = 0;
    }
    if duel_partner > 0 {
        if let Some(partner) = state.users.get_mut(&duel_partner) {
            partner.atacable_por = 0;
            partner.duel_pending = 0;
        }
    }

    // VB6: Restore attributes if TomoPocion (Modulo_UsUaRiOs.bas:1558-1563)
    if let Some(u) = state.users.get_mut(&conn_id) {
        if u.tomo_pocion {
            u.attributes = u.attributes_backup;
            u.tomo_pocion = false;
            u.duracion_efecto = 0;
        }
    }

    // VB6: Kill all pets on death (Modulo_UsUaRiOs.bas:1576-1585)
    let pets = state.users.get(&conn_id).map(|u| u.mascotas_index).unwrap_or([0; 3]);
    for i in 0..3 {
        let pet_idx = pets[i];
        if pet_idx > 0 {
            // Kill pet NPC — send removal to area first
            if let Some(n) = state.get_npc(pet_idx) {
                let bp = binary_packets::write_character_remove(n.char_index.0 as i16);
                state.send_data_bytes(SendTarget::ToArea { map: n.map, x: n.x, y: n.y }, &bp);
            }
            state.kill_npc(pet_idx);
        }
    }
    if let Some(u) = state.users.get_mut(&conn_id) {
        u.mascotas_index = [0; 3];
        u.mascotas_type = [0; 3];
        u.nro_mascotas = 0;
    }

    // Deequip all items and drop inventory
    if let Some(user) = state.users.get_mut(&conn_id) {
        user.equip.weapon = 0;
        user.equip.armor = 0;
        user.equip.shield = 0;
        user.equip.helmet = 0;
        user.equip.municion = 0;
        user.equip.ring = 0;
        for slot in user.inventory.iter_mut() {
            slot.equipped = false;
        }
    }

    // VB6 parity: item drops on death
    // - ZONAPELEA (trigger 6): NO item drops at all
    // - Non-newbie (level > 12): TirarTodo — drop ALL items
    // - Newbie (level <= 12): TirarTodosLosItemsNoNewbies — drop only non-newbie items
    // Note: VB6 does NOT check criminal status for drops
    let tile_trigger = get_map_tile_trigger(state, map, x, y);
    let in_arena = tile_trigger == crate::data::maps::Trigger::CombatZone;
    let is_newbie = victim_level <= 12;

    if !in_arena {
        let mut items_to_drop: Vec<(i32, i32)> = Vec::new();
        if let Some(user) = state.users.get(&conn_id) {
            for slot in user.inventory.iter() {
                if slot.obj_index > 0 && slot.amount > 0 {
                    if is_newbie {
                        // Newbie: only drop non-newbie items
                        let is_newbie_item = state.game_data.objects.get((slot.obj_index - 1) as usize)
                            .map(|o| o.newbie)
                            .unwrap_or(false);
                        if !is_newbie_item {
                            items_to_drop.push((slot.obj_index, slot.amount));
                        }
                    } else {
                        // Non-newbie: drop ALL items (TirarTodo)
                        items_to_drop.push((slot.obj_index, slot.amount));
                    }
                }
            }
        }

        if let Some(user) = state.users.get_mut(&conn_id) {
            for slot in user.inventory.iter_mut() {
                if slot.obj_index > 0 {
                    if is_newbie {
                        let is_newbie_item = state.game_data.objects.get((slot.obj_index - 1) as usize)
                            .map(|o| o.newbie)
                            .unwrap_or(false);
                        if !is_newbie_item {
                            slot.obj_index = 0;
                            slot.amount = 0;
                        }
                    } else {
                        // Non-newbie: clear ALL items
                        slot.obj_index = 0;
                        slot.amount = 0;
                    }
                }
            }
        }

        let offsets = [(0,0),(1,0),(0,1),(-1,0),(0,-1),(1,1),(-1,1),(1,-1),(-1,-1)];
        let mut off_idx = 0;
        let (drop_grid_w, drop_grid_h) = state.grid_dimensions(map);
        for (obj_idx, amount) in items_to_drop {
            if let Some(obj) = state.game_data.objects.get((obj_idx - 1) as usize) {
                let grh = obj.grh_index;
                let mut placed = false;
                for tries in 0..offsets.len() {
                    let idx = (off_idx + tries) % offsets.len();
                    let (ox, oy) = offsets[idx];
                    let tx = x + ox as i32;
                    let ty = y + oy as i32;
                    if tx < 1 || tx > drop_grid_w || ty < 1 || ty > drop_grid_h { continue; }
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
                        let ho_pkt = binary_packets::write_object_create(tx as i16, ty as i16, grh as i16);
                        state.send_data_bytes(SendTarget::ToArea { map, x, y }, &ho_pkt);
                        off_idx = (idx + 1) % offsets.len();
                        placed = true;
                        break;
                    }
                }
                if !placed { continue; }
            }
        }
    }

    send_full_inventory(state, conn_id).await;

    let pkt = binary_packets::write_multi_msg_simple(crate::protocol::packets::MultiMessageID::NPCKillUser);
    state.send_bytes(conn_id, &pkt);
    send_stats_hp(state, conn_id).await;

    let map_is_pk = state.game_data.maps.get(map as usize)
        .and_then(|m| m.as_ref())
        .map(|m| m.info.pk)
        .unwrap_or(false);
    if map_is_pk {
        let pkt = binary_packets::write_dead();
        state.send_bytes(conn_id, &pkt);
    }

    let fx_clear_pkt = binary_packets::write_create_fx(char_index.0 as i16, 0, 0);
    state.send_data_bytes(SendTarget::ToArea { map, x, y }, &fx_clear_pkt);

    let heading = state.users.get(&conn_id).map(|u| u.heading).unwrap_or(2);
    let cp_pkt = binary_packets::write_character_change(
        char_index.0 as i16, DEAD_BODY_NEUTRAL as i16, DEAD_HEAD_NEUTRAL as i16,
        heading as u8, 0, 0, 0, 0, 0,
    );
    state.send_data_bytes(SendTarget::ToArea { map, x, y }, &cp_pkt);

    if let Some(user) = state.users.get(&conn_id) {
        let au_pkt = binary_packets::write_aura_update(
            user.char_index.0 as i16,
            user.aura_a as i16, user.aura_w as i16,
            user.aura_e as i16, user.aura_r as i16, user.aura_c as i16,
        );
        state.send_data_bytes(SendTarget::ToArea { map, x, y }, &au_pkt);
    }

    // Award experience to killer
    if let Some(killer) = killer_id {
        let exp_gain = (victim_level as i64) * 2;
        let killer_name = state.users.get(&killer).map(|u| u.char_name.clone()).unwrap_or_default();

        if let Some(k) = state.users.get_mut(&killer) {
            k.exp += exp_gain;
        }

        // VB6 13.3: ZONAPELEA — both players in CombatZone suppresses criminal penalty on kill.
        let victim_in_arena_kill = get_map_tile_trigger(state, map, x, y) == crate::data::maps::Trigger::CombatZone;
        let killer_arena_pos = state.users.get(&killer).map(|k| (k.pos_map, k.pos_x, k.pos_y)).unwrap_or((0, 0, 0));
        let killer_in_arena_kill = get_map_tile_trigger(state, killer_arena_pos.0, killer_arena_pos.1, killer_arena_pos.2) == crate::data::maps::Trigger::CombatZone;
        let both_in_arena_kill = victim_in_arena_kill && killer_in_arena_kill;

        // VB6 13.3: PvP kill reputation update
        // Kill citizen: rep_asesino += 2000
        // Kill criminal: rep_noble += 500
        // Also update kill counters (criminales_matados / ciudadanos_matados)
        // ZONAPELEA: skip all reputation and kill-counter changes when both players are in CombatZone
        let victim_criminal = state.users.get(&conn_id).map(|u| u.criminal).unwrap_or(false);
        let victim_name_upper = victim_name.to_uppercase();
        if !both_in_arena_kill {
            if victim_criminal {
                let last = state.users.get(&killer).map(|k| k.last_crim_matado.clone()).unwrap_or_default();
                if last != victim_name_upper {
                    if let Some(k) = state.users.get_mut(&killer) {
                        k.last_crim_matado = victim_name_upper;
                        if k.criminales_matados < 65000 {
                            k.criminales_matados += 1;
                        }
                    }
                }
                if let Some(k) = state.users.get_mut(&killer) {
                    k.rep_noble += 500;
                }
            } else {
                let last = state.users.get(&killer).map(|k| k.last_ciud_matado.clone()).unwrap_or_default();
                if last != victim_name_upper {
                    if let Some(k) = state.users.get_mut(&killer) {
                        k.last_ciud_matado = victim_name_upper;
                        if k.ciudadanos_matados < 65000 {
                            k.ciudadanos_matados += 1;
                        }
                    }
                }
                if let Some(k) = state.users.get_mut(&killer) {
                    k.rep_asesino += 2000;
                }
            }
        }
        recalc_criminal(state, killer);
        // Broadcast appearance change (criminal status may have changed)
        let (km, kx, ky) = state.users.get(&killer)
            .map(|u| (u.pos_map, u.pos_x, u.pos_y))
            .unwrap_or((0, 0, 0));
        if km > 0 {
            if let Some(k) = state.users.get(&killer) {
                let cp = binary_packets::write_character_change(
                    k.char_index.0 as i16, k.body as i16, k.head as i16,
                    k.heading as u8, k.weapon_anim as i16, k.shield_anim as i16,
                    k.casco_anim as i16, 0, 0,
                );
                state.send_data_bytes(SendTarget::ToArea { map: km, x: kx, y: ky }, &cp);
            }
        }

        state.send_msg_id(killer, 60, &format!("{}@{}", victim_name, exp_gain));
        state.send_msg_id(killer, 170, &format!("{}", exp_gain));
        send_stats_exp(state, killer).await;
        check_user_level(state, killer).await;

        info!("[COMBAT] '{}' killed '{}' (+{} exp)", killer_name, victim_name, exp_gain);
    }

    // Zone exit point: if the player died inside a zone with a defined exit point,
    // warp their ghost to that exit instead of leaving them at the death tile.
    // VB6 parity: zones with salida_map > 0 expel dead players to the exit coords.
    let zone_exit = state.game_data.maps
        .get(map as usize)
        .and_then(|m| m.as_ref())
        .and_then(|game_map| game_map.get_zone_at(x - 1, y - 1))
        .filter(|z| z.salida_map > 0)
        .map(|z| (z.salida_map, z.salida_x, z.salida_y));

    if let Some((exit_map, exit_x, exit_y)) = zone_exit {
        info!("[COMBAT] '{}' died in zone with exit — warping to map {} ({},{})", victim_name, exit_map, exit_x, exit_y);
        warp_user(state, conn_id, exit_map, exit_x, exit_y).await;
    }
}
