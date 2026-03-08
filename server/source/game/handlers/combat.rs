//! Combat handlers: melee/ranged attack, PvP damage, user_die, combat formulas.
//! Extracted from mod.rs to reduce file size.
//! VB6 13.3 parity: UsuarioImpacto, UsuarioAtacaUsuario, UserDañoUser, CalcularDaño,
//! PoderAtaqueArma, PoderEvasion, PoderEvasionEscudo, DoApuñalar, DoGolpeCritico.

use tracing::info;
use crate::net::ConnectionId;
use crate::game::types::{GameState, SendTarget, InventorySlot, MAX_INVENTORY_SLOTS};
use crate::game::world;
use crate::protocol::{font_index, binary_packets};
use crate::data::objects::ObjType;
use super::common::*;
use super::{
    user_attack_npc, check_user_level, warp_user, naked_body,
    send_inventory_slot, send_full_inventory,
    pretoriano_check_death,
    DEAD_BODY_NEUTRAL, DEAD_HEAD_NEUTRAL,
};
use super::npcs::fire_elemental_react;
use super::skills::{
    try_desarmar, try_level_skill, try_level_skill_with_hit,
};

// =====================================================================
// VB6 Combat Constants
// =====================================================================

/// VB6: PartesCuerpo.bCabeza = 1
const BODY_PART_HEAD: i32 = 1;
/// VB6: PartesCuerpo.bTorso = 6
const BODY_PART_TORSO: i32 = 6;

/// VB6: ESPADA_VIKINGA / EspadaMataDragonesIndex (Dragon Slayer + DoGolpeCritico)
const ESPADA_VIKINGA: i32 = 402;

/// VB6: GUANTE_HURTO = 873 (pickpocket gloves)
const GUANTE_HURTO: i32 = 873;

/// VB6: PROB_ACUCHILLAR = 20 (20% chance for Pirate throat cut)
const PROB_ACUCHILLAR: i32 = 20;
/// VB6: DAÑO_ACUCHILLAR = 0.2 (20% of base damage)
const DANO_ACUCHILLAR: f64 = 0.2;

// =====================================================================
// VB6 Attack Power formulas (exact replicas)
// =====================================================================

/// VB6: PoderAtaqueArma — melee weapon attack power.
/// `skill` = UserSkills(eSkill.Armas), `agility` = UserAtributos(Agilidad),
/// `level` = ELV, `class_mod` = ModClase(clase).AtaqueArmas
pub(super) fn poder_ataque_arma(skill: i32, agility: i32, level: i32, class_mod: f32) -> i64 {
    let temp = if skill < 31 {
        skill as i64 * class_mod as i64
    } else if skill < 61 {
        (skill + agility) as i64 * class_mod as i64
    } else if skill < 91 {
        (skill + 2 * agility) as i64 * class_mod as i64
    } else {
        (skill + 3 * agility) as i64 * class_mod as i64
    };
    // VB6: integer arithmetic — cast class_mod to i64 truncates like VB6's Long multiplication
    // Actually VB6 multiplies by Single (float), so let's be more precise:
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
/// Formula: (Tacticas + Tacticas/33 * Agility) * ModClase.Evasion + 2.5 * max(0, Level-12)
pub(super) fn poder_evasion(tacticas: i32, agility: i32, level: i32, class_mod: f32) -> i64 {
    let temp = (tacticas as f64 + (tacticas as f64 / 33.0 * agility as f64)) * class_mod as f64;
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
    class: &str,
    base_damage: i64,
    is_npc_target: bool,
) -> Option<i64> {
    let s = skill as f64;

    // VB6 polynomial luck formula per class
    let suerte = if class.eq_ignore_ascii_case("Asesino") {
        ((0.00003 * s - 0.002) * s + 0.098) * s + 4.25
    } else if class.eq_ignore_ascii_case("Clerigo")
        || class.eq_ignore_ascii_case("Paladin")
        || class.eq_ignore_ascii_case("Pirata")
    {
        ((0.000003 * s + 0.0006) * s + 0.0107) * s + 4.93
    } else if class.eq_ignore_ascii_case("Bardo") {
        ((0.000002 * s + 0.0002) * s + 0.032) * s + 4.81
    } else {
        0.0361 * s + 4.39
    };

    let suerte = suerte as i32;

    if rand_range(0, 100) < suerte {
        let dmg = if is_npc_target {
            // VB6: NPC target = damage * 2
            base_damage * 2
        } else {
            // VB6: User target — Assassin 1.4x, others 1.5x
            if class.eq_ignore_ascii_case("Asesino") {
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
/// Requirements: same heading as victim, Apuñalar skill > 0, behind the target.
pub(super) fn puede_apunalar(class: &str, attacker_heading: i32, victim_heading: i32) -> bool {
    // VB6: Must be facing the same direction (behind the target)
    attacker_heading == victim_heading
}

// =====================================================================
// VB6 DoGolpeCritico (Bandido + Espada Vikinga only)
// =====================================================================

/// VB6: DoGolpeCritico — critical hit, ONLY for Bandido class with Espada Vikinga.
/// Returns additional damage dealt if critical succeeds.
pub(super) fn do_golpe_critico(
    class: &str,
    weapon_obj_index: i32,
    wrestling_skill: i32,
    base_damage: i64,
) -> Option<i64> {
    // VB6: Only Bandido with Espada Vikinga can do critical hits
    if !class.eq_ignore_ascii_case("Bandido") {
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

/// VB6: DoDesequipar — try to unequip victim's shield, weapon, or helmet (in that order).
/// Returns true if something was unequipped.
async fn do_desequipar(state: &mut GameState, victim_id: ConnectionId) -> bool {
    if let Some(victim) = state.users.get_mut(&victim_id) {
        // Try shield first
        if victim.equip.shield > 0 {
            victim.equip.shield = 0;
            return true;
        }
        // Then weapon
        if victim.equip.weapon > 0 {
            victim.equip.weapon = 0;
            return true;
        }
        // Then helmet
        if victim.equip.helmet > 0 {
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
pub(super) fn puede_acuchillar(class: &str, weapon_acuchilla: bool) -> bool {
    class.eq_ignore_ascii_case("Pirata") && weapon_acuchilla
}

/// VB6: DoAcuchillar — Pirate throat cut attack.
/// 20% chance, deals 20% of base damage as additional damage.
/// Works on both user and NPC targets.
pub(super) fn do_acuchillar(base_damage: i64) -> Option<i64> {
    if rand_range(1, 100) <= PROB_ACUCHILLAR {
        let dmg = (base_damage as f64 * DANO_ACUCHILLAR) as i64;
        Some(dmg.max(1))
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
}

pub(super) fn get_weapon_info(state: &GameState, conn_id: ConnectionId) -> WeaponInfo {
    let user = match state.users.get(&conn_id) {
        Some(u) => u,
        None => return WeaponInfo {
            obj_index: 0, is_proyectil: false, min_hit: 0, max_hit: 0,
            refuerzo: 0, envenena: false, has_ammo: false, ammo_min_hit: 0, ammo_max_hit: 0,
            acuchilla: false,
        },
    };

    if user.equip.weapon == 0 || user.equip.weapon > MAX_INVENTORY_SLOTS {
        return WeaponInfo {
            obj_index: 0, is_proyectil: false, min_hit: 0, max_hit: 0,
            refuerzo: 0, envenena: false, has_ammo: false, ammo_min_hit: 0, ammo_max_hit: 0,
            acuchilla: false,
        };
    }

    let obj_idx = user.inventory[user.equip.weapon - 1].obj_index;
    if obj_idx <= 0 {
        return WeaponInfo {
            obj_index: 0, is_proyectil: false, min_hit: 0, max_hit: 0,
            refuerzo: 0, envenena: false, has_ammo: false, ammo_min_hit: 0, ammo_max_hit: 0,
            acuchilla: false,
        };
    }

    let (is_proy, w_min, w_max, refuerzo, envenena, uses_ammo, acuchilla) = match state.get_object(obj_idx) {
        Some(o) => (o.proyectil, o.min_hit, o.max_hit, o.refuerzo, o.envenena, o.municion > 0, o.acuchilla),
        None => (false, 0, 0, 0, false, false, false),
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
    }
}

/// Get ring/glove info for wrestling bonus
pub(super) fn get_ring_info(state: &GameState, conn_id: ConnectionId) -> (i32, bool, i32, i32) {
    let user = match state.users.get(&conn_id) {
        Some(u) => u,
        None => return (0, false, 0, 0),
    };
    if user.equip.ring == 0 || user.equip.ring > MAX_INVENTORY_SLOTS {
        return (0, false, 0, 0);
    }
    let obj_idx = user.inventory[user.equip.ring - 1].obj_index;
    // VB6: ObjData(ObjIndex).Guante = 1 — we check if the ring is a "guante" type
    // Since we don't have a guante field, skip for now
    (obj_idx, false, 0, 0)
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
            u.class.clone(),
        ),
        _ => return,
    };
    let (map, x, y, heading, char_index, dead, safe_on, level,
         strength, agility, min_hit, max_hit,
         skill_armas, skill_proyectiles, skill_tacticas, skill_defensa, skill_wrestling, skill_apunalar,
         attacker_name, class) = user_data;

    if dead {
        return;
    }

    // VB6: Attacking ALWAYS reveals hidden users (no chance check).
    let (was_hidden, was_invisible) = state.users.get(&conn_id)
        .map(|u| (u.hidden && !u.admin_invisible, u.invisible && !u.admin_invisible))
        .unwrap_or((false, false));
    if was_hidden || was_invisible {
        if let Some(user) = state.users.get_mut(&conn_id) {
            user.hidden = false;
            user.counter_oculto = 0;
            user.invisible = false;
            user.counter_invisible = 0;
        }
        if let Some(u) = state.users.get(&conn_id) {
            let cc = u.build_cc_binary();
            let cd = build_cd_binary(u);
            let (px, py) = (u.pos_x, u.pos_y);
            state.send_data_bytes(SendTarget::ToArea { map, x: px, y: py }, &cc).await;
            state.send_data_bytes(SendTarget::ToArea { map, x: px, y: py }, &cd).await;
        }
        let nover = binary_packets::write_set_invisible(char_index.0 as i16, false, 0);
        state.send_bytes(conn_id, &nover).await;
        state.send_console(conn_id, "Has vuelto a ser visible.", font_index::INFO).await;
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
    let swing_pkt = binary_packets::write_play_wave(2, x as u8, y as u8);
    state.send_data_bytes(
        SendTarget::ToArea { map, x, y },
        &swing_pkt,
    ).await;

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
            state.send_msg_id(conn_id, 207, "").await;
            return;
        }

        // Clan safe check
        if attacker_guild > 0 && attacker_guild == victim_guild && attacker_seguro {
            state.send_console(conn_id, "No puedes atacar a un miembro de tu clan. Usa /SEGUROCLAN para desactivar el seguro.", font_index::INFO).await;
            return;
        }

        // VB6: Safe zone check — dueling players bypass safe zone restriction
        let in_duel = state.users.get(&conn_id).map(|u| u.atacable_por == victim_id).unwrap_or(false)
            && state.users.get(&victim_id).map(|u| u.atacable_por == conn_id).unwrap_or(false);

        if !in_duel {
            let attacker_trigger = get_map_tile_trigger(state, map, x, y);
            if attacker_trigger == crate::data::maps::Trigger::SafeZone {
                state.send_msg_id(conn_id, 163, "").await;
                return;
            }
            let victim_pos = state.users.get(&victim_id).map(|v| (v.pos_x, v.pos_y)).unwrap_or((0, 0));
            let victim_trigger = get_map_tile_trigger(state, map, victim_pos.0, victim_pos.1);
            if victim_trigger == crate::data::maps::Trigger::SafeZone {
                state.send_msg_id(conn_id, 163, "").await;
                return;
            }
        }

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
             v_max_hp, v_min_hp, v_class, v_heading, v_char_index, v_meditating) = victim_data;

        if v_dead {
            state.send_msg_id(conn_id, 154, "").await;
            return;
        }
        if v_privs > 0 {
            state.send_msg_id(conn_id, 155, "").await;
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
                let mod_atk = state.game_data.balance.class_mod_ataque_proyectiles(&class);
                (poder_ataque_proyectil(skill_proyectiles, agility, level, mod_atk), 5usize) // eSkill.Proyectiles
            } else {
                let mod_atk = state.game_data.balance.class_mod_ataque_armas(&class);
                (poder_ataque_arma(skill_armas, agility, level, mod_atk), 1usize) // eSkill.Armas
            }
        } else {
            let mod_atk = state.game_data.balance.class_mod_ataque_wrestling(&class);
            (poder_ataque_wrestling(skill_wrestling, agility, level, mod_atk), 20usize) // eSkill.Wrestling
        };

        // VB6: PoderEvasion for victim
        let v_evasion_mod = state.game_data.balance.class_mod_evasion(&v_class);
        let mut victim_evasion = poder_evasion(v_tacticas, v_agility, v_level, v_evasion_mod);

        // VB6: Add shield evasion if victim has shield
        let victim_shield_evasion = if v_has_shield {
            let shield_mod = state.game_data.balance.class_mod_escudo(&v_class);
            poder_evasion_escudo(v_defensa, shield_mod)
        } else {
            0
        };
        victim_evasion += victim_shield_evasion;

        // VB6: ProbExito = clamp(50 + (PoderAtaque - UserPoderEvasion) * 0.4, 10, 90)
        let mut prob_exito = (50.0 + (attack_power - victim_evasion) as f64 * 0.4) as i32;
        prob_exito = prob_exito.clamp(10, 90);

        // VB6: Meditation reduces evasion by 25%
        if v_meditating {
            let prob_evadir = ((100 - prob_exito) as f64 * 0.75) as i32;
            prob_exito = (100 - prob_evadir).min(90);
        }

        let hit = rand_range(1, 100) <= prob_exito;

        if !hit {
            // VB6: Shield block check — separate from evasion
            if v_has_shield {
                let suma_skills = (v_defensa + v_tacticas).max(1);
                let prob_rechazo = ((100 * v_defensa / suma_skills) as i32).clamp(10, 90);
                let rechazo = rand_range(1, 100) <= prob_rechazo;

                if rechazo {
                    // Shield block — VB6: SND_ESCUDO + messages
                    let (vx, vy) = state.users.get(&victim_id).map(|v| (v.pos_x, v.pos_y)).unwrap_or((0, 0));
                    let snd = binary_packets::write_play_wave(37, vx as u8, vy as u8); // SND_ESCUDO
                    state.send_data_bytes(SendTarget::ToArea { map, x: vx, y: vy }, &snd).await;

                    let pkt_atk = binary_packets::write_multi_msg_simple(crate::protocol::packets::MultiMessageID::BlockedWithShieldOther);
                    state.send_bytes(conn_id, &pkt_atk).await;
                    let pkt_vic = binary_packets::write_multi_msg_simple(crate::protocol::packets::MultiMessageID::BlockedWithShieldUser);
                    state.send_bytes(victim_id, &pkt_vic).await;

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
            let snd = binary_packets::write_play_wave(2, x as u8, y as u8);
            state.send_data_bytes(SendTarget::ToArea { map, x, y }, &snd).await;
            let pkt = binary_packets::write_multi_user_attacked_swing(char_index.0 as i16);
            state.send_bytes(victim_id, &pkt).await;
            let pkt = binary_packets::write_multi_msg_simple(crate::protocol::packets::MultiMessageID::UserSwing);
            state.send_bytes(conn_id, &pkt).await;
            state.send_chat_over_head_to(SendTarget::ToArea { map, x, y }, "\u{00A1}Fallo!", v_char_index.0 as i16, 255).await;

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
        let snd = binary_packets::write_play_wave(10, x as u8, y as u8);
        state.send_data_bytes(SendTarget::ToArea { map, x, y }, &snd).await;

        // VB6: Blood FX on victim (if not navigating)
        // (client handles this via MultiMessage)

        // VB6: DoDesequipar — requires pickpocket gloves (ring=873) + unarmed
        {
            let (has_gloves, is_unarmed, wrestling_sk, attacker_level) = state.users.get(&conn_id)
                .map(|u| {
                    let ring_idx = if u.equip.ring > 0 && u.equip.ring <= MAX_INVENTORY_SLOTS {
                        u.inventory[u.equip.ring - 1].obj_index
                    } else { 0 };
                    (ring_idx == GUANTE_HURTO, u.equip.weapon == 0, u.skills[20], u.level)
                })
                .unwrap_or((false, false, 0, 0));

            if has_gloves && is_unarmed {
                // VB6: Probabilidad = Wrestling*0.2 + Level*0.66
                let prob = (wrestling_sk as f64 * 0.2 + attacker_level as f64 * 0.66) as i32;
                let roll = rand_range(1, 100);

                if roll <= prob {
                    // Try unequip: shield → weapon → helmet
                    let unequipped = do_desequipar(state, victim_id).await;
                    if unequipped {
                        // Send CP to area to update victim appearance
                        if let Some(v) = state.users.get(&victim_id) {
                            let cp = binary_packets::write_character_change(
                                v.char_index.0 as i16, v.body as i16, v.head as i16, v.heading as u8,
                                v.weapon_anim as i16, v.shield_anim as i16, v.casco_anim as i16, 0, 0,
                            );
                            state.send_data_bytes(SendTarget::ToArea { map, x, y }, &cp).await;
                        }
                        state.send_console(conn_id, &format!("Has desarmado a {}!", victim_name), font_index::FIGHT).await;
                        state.send_console(victim_id, "Te han quitado un equipo!", font_index::FIGHT).await;
                    }
                }
            }

            // VB6: DoHandInmo — Thief only + gloves, paralyze for half duration
            if has_gloves && class.eq_ignore_ascii_case("Ladron") {
                let v_paralyzed = state.users.get(&victim_id).map(|u| u.paralyzed).unwrap_or(true);
                if !v_paralyzed {
                    // VB6: prob = Wrestling / 4
                    let prob = wrestling_sk / 4;
                    if rand_range(0, 100) < prob {
                        let half_para = state.config.intervalo_paralizado / 2;
                        if let Some(victim) = state.users.get_mut(&victim_id) {
                            victim.paralyzed = true;
                            victim.counter_paralisis = half_para;
                        }
                        let para_secs = (half_para as f32 * 0.04) as i16;
                        let pkt = binary_packets::write_paralize_ok(para_secs);
                        state.send_bytes(victim_id, &pkt).await;
                        if let Some(u) = state.users.get(&victim_id) {
                            let pu = binary_packets::write_pos_update(u.pos_x as u8, u.pos_y as u8);
                            state.send_bytes(victim_id, &pu).await;
                        }
                        state.send_console(conn_id, &format!("Has paralizado a {}!", victim_name), font_index::FIGHT).await;
                        state.send_console(victim_id, "Has sido paralizado!", font_index::FIGHT).await;
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
                state.game_data.balance.class_mod_dano_proyectiles(&class) as f64
            } else {
                state.game_data.balance.class_mod_dano_armas(&class) as f64
            }
        } else {
            state.game_data.balance.class_mod_dano_wrestling(&class) as f64
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
        if weapon.obj_index == ESPADA_VIKINGA {
            damage = 1;
        } else {
            // VB6: Weapon Refuerzo (penetration) adds to damage
            damage += weapon.refuerzo as i64;
        }

        // VB6: Body part hit (1=head, 2-6=body)
        let lugar = rand_range(BODY_PART_HEAD, BODY_PART_TORSO);

        // VB6: Armor absorption (skip for Dragon Slayer — always 1)
        if weapon.obj_index != ESPADA_VIKINGA {
            let (head_defense, body_defense) = calc_pvp_armor_absorption(state, victim_id, lugar);
            damage = damage - head_defense as i64 - body_defense as i64;
        }

        // VB6: if damage < 0 then damage = 1
        if damage < 0 { damage = 1; }

        // Send hit messages
        let n4_pkt = binary_packets::write_multi_user_hitted_by_user(char_index.0 as i16, lugar as u8, damage as i16);
        state.send_bytes(victim_id, &n4_pkt).await;
        let n5_pkt = binary_packets::write_multi_user_hitted_user(v_char_index.0 as i16, lugar as u8, damage as i16);
        state.send_bytes(conn_id, &n5_pkt).await;

        // VB6: floating yellow damage number
        state.send_chat_over_head_to(SendTarget::ToArea { map, x, y }, &format!("-{}", damage), v_char_index.0 as i16, 65535).await;

        // Apply damage to victim
        if let Some(victim) = state.users.get_mut(&victim_id) {
            victim.min_hp -= damage as i32;
        }

        // VB6: Weapon poison application (60% chance if weapon has Envenena=1)
        if weapon.envenena && rand_range(1, 100) <= 60 {
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
            if puede_apunalar(&class, heading, v_heading) && skill_apunalar > 0 {
                if let Some(stab_dmg) = do_apunalar(skill_apunalar, &class, damage, false) {
                    if let Some(victim) = state.users.get_mut(&victim_id) {
                        victim.min_hp -= stab_dmg as i32;
                    }
                    state.send_console(conn_id, &format!("Has apuñalado a {} por {}", victim_name, stab_dmg), font_index::FIGHT).await;
                    state.send_console(victim_id, &format!("Te ha apuñalado {} por {}", attacker_name, stab_dmg), font_index::FIGHT).await;
                    if let Some(u) = state.users.get_mut(&conn_id) {
                        try_level_skill_with_hit(u, 8, true); // Apuñalar
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
            if let Some(crit_dmg) = do_golpe_critico(&class, weapon.obj_index, wrestling_sk, damage) {
                if let Some(victim) = state.users.get_mut(&victim_id) {
                    victim.min_hp -= crit_dmg as i32;
                }
                state.send_console(conn_id, &format!("Has golpeado críticamente a {} por {}.", victim_name, crit_dmg), font_index::FIGHT).await;
                state.send_console(victim_id, &format!("{} te ha golpeado críticamente por {}.", attacker_name, crit_dmg), font_index::FIGHT).await;
            }

            // VB6: DoAcuchillar (Pirate throat cut — projectile PvP + melee NPC)
            // In PvP: only on projectile attacks (VB6 SistemaCombate.bas:1272)
            if weapon.is_proyectil && puede_acuchillar(&class, weapon.acuchilla) {
                if let Some(cut_dmg) = do_acuchillar(damage) {
                    if let Some(victim) = state.users.get_mut(&victim_id) {
                        victim.min_hp -= cut_dmg as i32;
                    }
                    state.send_console(conn_id, &format!("Has acuchillado a {} por {}", victim_name, cut_dmg), font_index::FIGHT).await;
                    state.send_console(victim_id, &format!("{} te ha acuchillado por {}", attacker_name, cut_dmg), font_index::FIGHT).await;
                }
            }
        }

        // VB6: Desarmar (Ladrón class — disarm)
        if class.eq_ignore_ascii_case("Ladron") {
            let wresterling_skill = state.users.get(&conn_id).and_then(|u| u.skills.get(20).copied()).unwrap_or(0);
            if wresterling_skill > 0 && try_desarmar(wresterling_skill) {
                if let Some(victim) = state.users.get_mut(&victim_id) {
                    if victim.equip.weapon > 0 {
                        victim.equip.weapon = 0;
                    }
                }
                state.send_console(victim_id, "Te han desarmado!", font_index::FIGHT).await;
                state.send_console(conn_id, &format!("Has desarmado a {}!", victim_name), font_index::FIGHT).await;
                if let Some(u) = state.users.get_mut(&conn_id) {
                    try_level_skill(u, 20);
                }
            }
        }

        // VB6: Fire Elemental reacts to PvP
        fire_elemental_react(state, victim_id, &attacker_name);

        // Update victim HP
        send_stats_hp(state, victim_id).await;

        // Check death
        let v_hp = state.users.get(&victim_id).map(|u| u.min_hp).unwrap_or(0);
        if v_hp <= 0 {
            user_die(state, victim_id, Some(conn_id)).await;
        }
    } else {
        // Safe zone check for NPC attacks too
        let attacker_trigger = get_map_tile_trigger(state, map, x, y);
        if attacker_trigger == crate::data::maps::Trigger::SafeZone {
            state.send_msg_id(conn_id, 164, "").await;
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
                            skill_armas, &attacker_name, &class).await;
        }
    }
}

// =====================================================================
// PvP armor absorption (VB6: UserDañoUser)
// =====================================================================

/// VB6: PvP armor absorption — separate from NPC combat.
/// Head hits use helmet only, body hits use armor + shield.
/// Returns (head_defense, body_defense).
fn calc_pvp_armor_absorption(state: &GameState, victim_id: ConnectionId, lugar: i32) -> (i32, i32) {
    let user = match state.users.get(&victim_id) {
        Some(u) => u,
        None => return (0, 0),
    };

    match lugar {
        BODY_PART_HEAD => {
            // Helmet absorbs head hits
            let helmet_def = if user.equip.helmet > 0 && user.equip.helmet <= MAX_INVENTORY_SLOTS {
                let obj_idx = user.inventory[user.equip.helmet - 1].obj_index;
                match state.get_object(obj_idx) {
                    Some(obj) if obj.min_def > 0 || obj.max_def > 0 => {
                        rand_range(obj.min_def.max(0), obj.max_def.max(1))
                    }
                    _ => 0,
                }
            } else {
                0
            };
            (helmet_def, 0)
        }
        _ => {
            // Body hits — armor + shield defense combined
            let mut min_def = 0i32;
            let mut max_def = 0i32;

            // Armor
            if user.equip.armor > 0 && user.equip.armor <= MAX_INVENTORY_SLOTS {
                let obj_idx = user.inventory[user.equip.armor - 1].obj_index;
                if let Some(obj) = state.get_object(obj_idx) {
                    min_def += obj.min_def;
                    max_def += obj.max_def;
                }
            }

            // Shield (also absorbs body hits in VB6)
            if user.equip.shield > 0 && user.equip.shield <= MAX_INVENTORY_SLOTS {
                let obj_idx = user.inventory[user.equip.shield - 1].obj_index;
                if let Some(obj) = state.get_object(obj_idx) {
                    min_def += obj.min_def;
                    max_def += obj.max_def;
                }
            }

            let body_def = if max_def > 0 {
                rand_range(min_def.max(0), max_def.max(1))
            } else {
                0
            };
            (0, body_def)
        }
    }
}

// =====================================================================
// Legacy API compatibility (used by npcs.rs)
// =====================================================================

/// Calculate attack power for NPC combat — uses balance class modifiers.
pub(super) fn calc_attack_power(skill: i32, agility: i32, level: i32) -> f64 {
    // Legacy — called from npcs.rs where we don't know the weapon type yet
    // Returns approximate value without class modifier (mod=1.0)
    poder_ataque_arma(skill, agility, level, 1.0) as f64
}

/// Calculate attack power with balance modifier.
pub(super) fn calc_attack_power_with_balance(skill: i32, agility: i32, level: i32, class_mod: f32) -> f64 {
    poder_ataque_arma(skill, agility, level, class_mod) as f64
}

/// Calculate defense/evasion power (legacy API for npcs.rs).
pub(super) fn calc_defense_power(tacticas: i32, agility: i32, level: i32) -> f64 {
    poder_evasion(tacticas, agility, level, 1.0) as f64
}

/// Calculate defense/evasion power with balance modifier (legacy API).
pub(super) fn calc_defense_power_with_balance(
    tacticas: i32, agility: i32, level: i32, class_mod: f32,
    has_shield: bool, _shield_max_def: i32, shield_class_mod: f32,
    _is_mago: bool,
) -> f64 {
    let base = poder_evasion(tacticas, agility, level, class_mod) as f64;
    // For NPC combat, shield evasion uses the old formula
    // (npcs don't have Defensa skill)
    base
}

/// Get armor absorption for NPC combat (unchanged from before).
pub(super) fn calc_armor_absorption(state: &GameState, conn_id: ConnectionId, body_part: i32) -> i32 {
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

    match state.get_object(obj_index) {
        Some(obj) if obj.min_def > 0 || obj.max_def > 0 => {
            rand_range(obj.min_def.max(0), obj.max_def.max(1))
        }
        _ => 0,
    }
}

/// Get armor absorption with weapon penetration (for NPC combat).
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

    (armor_def + shield_def - refuerzo).max(0)
}

/// Class-based damage modifier from balance data.
pub(super) fn class_damage_modifier_from_balance(state: &GameState, class: &str) -> f64 {
    state.game_data.balance.class_mod_dano_armas(class) as f64
}

/// Fallback class-based damage modifier (no longer used in PvP but kept for reference).
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
    state.send_chat_over_head_to(SendTarget::ToArea { map, x, y }, "\u{00A1}Aaaahhhh!", char_index.0 as i16, 255).await;

    // Mark as dead, change body to dead model
    if let Some(user) = state.users.get_mut(&conn_id) {
        user.dead = true;
        user.min_hp = 0;
        user.min_sta = 0;
        // Clear status effects
        user.paralyzed = false;
        user.immobilized = false;
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

    // Drop non-newbie items on the ground (VB6 TirarTodo)
    let is_newbie = victim_level < 13;
    let is_criminal = state.users.get(&conn_id).map(|u| u.criminal).unwrap_or(false);
    if !is_newbie || is_criminal {
        let mut items_to_drop: Vec<(i32, i32)> = Vec::new();
        if let Some(user) = state.users.get(&conn_id) {
            for slot in user.inventory.iter() {
                if slot.obj_index > 0 && slot.amount > 0 {
                    let is_newbie_item = state.game_data.objects.get((slot.obj_index - 1) as usize)
                        .map(|o| o.newbie)
                        .unwrap_or(false);
                    if !is_newbie_item {
                        items_to_drop.push((slot.obj_index, slot.amount));
                    }
                }
            }
        }

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

        let offsets = [(0,0),(1,0),(0,1),(-1,0),(0,-1),(1,1),(-1,1),(1,-1),(-1,-1)];
        let mut off_idx = 0;
        for (obj_idx, amount) in items_to_drop {
            if let Some(obj) = state.game_data.objects.get((obj_idx - 1) as usize) {
                let grh = obj.grh_index;
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
                        let ho_pkt = binary_packets::write_object_create(tx as u8, ty as u8, grh as i16);
                        state.send_data_bytes(SendTarget::ToArea { map, x, y }, &ho_pkt).await;
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
    state.send_bytes(conn_id, &pkt).await;
    send_stats_hp(state, conn_id).await;

    let map_is_pk = state.game_data.maps.get(map as usize)
        .and_then(|m| m.as_ref())
        .map(|m| m.info.pk)
        .unwrap_or(false);
    if map_is_pk {
        let pkt = binary_packets::write_dead();
        state.send_bytes(conn_id, &pkt).await;
    }

    let fx_clear_pkt = binary_packets::write_create_fx(char_index.0 as i16, 0, 0);
    state.send_data_bytes(SendTarget::ToArea { map, x, y }, &fx_clear_pkt).await;

    let heading = state.users.get(&conn_id).map(|u| u.heading).unwrap_or(2);
    let cp_pkt = binary_packets::write_character_change(
        char_index.0 as i16, DEAD_BODY_NEUTRAL as i16, DEAD_HEAD_NEUTRAL as i16,
        heading as u8, 0, 0, 0, 0, 0,
    );
    state.send_data_bytes(SendTarget::ToArea { map, x, y }, &cp_pkt).await;

    if let Some(user) = state.users.get(&conn_id) {
        let au_pkt = binary_packets::write_aura_update(
            user.char_index.0 as i16,
            user.aura_a as i16, user.aura_w as i16,
            user.aura_e as i16, user.aura_r as i16, user.aura_c as i16,
        );
        state.send_data_bytes(SendTarget::ToArea { map, x, y }, &au_pkt).await;
    }

    // Award experience to killer
    if let Some(killer) = killer_id {
        let exp_gain = victim_level as i64;
        let killer_name = state.users.get(&killer).map(|u| u.char_name.clone()).unwrap_or_default();

        if let Some(k) = state.users.get_mut(&killer) {
            k.exp += exp_gain;
        }

        // Criminal/citizen reputation tracking on PvP kill
        let victim_criminal = state.users.get(&conn_id).map(|u| u.criminal).unwrap_or(false);
        let victim_name_upper = victim_name.to_uppercase();
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
        } else {
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
                    let cp = binary_packets::write_character_change(
                        k.char_index.0 as i16, k.body as i16, k.head as i16,
                        k.heading as u8, k.weapon_anim as i16, k.shield_anim as i16,
                        k.casco_anim as i16, 0, 0,
                    );
                    state.send_data_bytes(SendTarget::ToArea { map: km, x: kx, y: ky }, &cp).await;
                }
            }
        }

        state.send_msg_id(killer, 60, &format!("{}@{}", victim_name, exp_gain)).await;
        state.send_msg_id(killer, 170, &format!("{}", exp_gain)).await;
        send_stats_exp(state, killer).await;
        check_user_level(state, killer).await;

        info!("[COMBAT] '{}' killed '{}' (+{} exp)", killer_name, victim_name, exp_gain);
    }
}
