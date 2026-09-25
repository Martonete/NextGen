//! Argentum 20 `SistemaCombate.bas` — the pure formulas, ported line by line so they can be
//! unit-tested without a `GameState`. Every reference below is to
//! `C:\AO20\Argentum Server\Codigo\SistemaCombate.bas`.
//!
//! Things AO20 layers on top through `Modifiers` / EffectsOverTime (GetHitBonus,
//! GetEvasionBonus, PhysicalDamageModifier, PhysicDamageReduction, GetLinearDamageBonus)
//! do not exist in this server: they are the identity (0 / 1) and are not stubbed.

use super::common::rand_range;
use crate::data::objects::{ObjData, WeaponType};
use crate::game::class_race::PlayerClass;
use crate::game::handlers::skills::skill_id;

/// VB6 assignment of a Single/Double to a Long rounds half to even (banker's rounding).
fn vb_round(v: f64) -> i64 {
    v.round_ties_even() as i64
}

/// AO20 `MIN_APUÑALAR` (Declares.bas).
pub const MIN_APUNALAR: i32 = 10;
/// AO20 `MAXDISTANCIAARCO`.
pub const MAX_DISTANCIA_ARCO: i32 = 18;

/// `e_PartesCuerpo`
pub const B_CABEZA: i32 = 1;
pub const B_PIERNA_IZQUIERDA: i32 = 2;
pub const B_PIERNA_DERECHA: i32 = 3;
pub const B_BRAZO_DERECHO: i32 = 4;
pub const B_BRAZO_IZQUIERDO: i32 = 5;
pub const B_TORSO: i32 = 6;

#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum AttackType {
    Melee,
    Ranged,
}

// =====================================================================
// Attack / evasion power
// =====================================================================

/// `AttackPower` (:177): `(Skill + (3*Skill/100)*Agilidad) * mod + 2.5*max(ELV-12, 0)`.
/// VB6 evaluates in Single/Double and assigns to Long (banker's rounding); we keep f64 and
/// round the same way at the end.
pub fn attack_power(skill: i32, agility: i32, level: i32, skill_modifier: f32) -> i64 {
    let s = skill as f64;
    let temp = (s + (3.0 * s / 100.0) * agility as f64) * skill_modifier as f64;
    let ap = vb_round(temp); // TempAttackPower As Long
    vb_round(ap as f64 + 2.5 * (level - 12).max(0) as f64)
}

/// `PoderEvasion` (:165): `(Tacticas + (3*Tacticas/100)*Agilidad) * MODEVASION + 2.5*max(ELV-12,0)`.
pub fn poder_evasion(tacticas: i32, agility: i32, level: i32, mod_evasion: f32) -> i64 {
    let t = tacticas as f64;
    let base = (t + (3.0 * t / 100.0) * agility as f64) * mod_evasion as f64;
    vb_round(base + 2.5 * (level - 12).max(0) as f64)
}

/// `PoderEvasionEscudo` (:149): `((Defensa * MODESCUDO) / 2) * (Porcentaje / 100)`; 0 without shield.
pub fn poder_evasion_escudo(skill_defensa: i32, mod_escudo: f32, porcentaje: i32) -> i64 {
    let item_modifier = porcentaje as f64 / 100.0;
    vb_round(((skill_defensa as f64 * mod_escudo as f64) / 2.0) * item_modifier)
}

/// `GetSkillRequiredForWeapon` (:2013): which skill drives the attack power.
pub fn skill_required_for_weapon(weapon: Option<&ObjData>) -> i32 {
    match weapon {
        None => skill_id::WRESTERLING,
        Some(o) => match o.weapon_type {
            WeaponType::Knuckle => skill_id::WRESTERLING,
            WeaponType::Bow | WeaponType::GunPowder => skill_id::PROYECTILES,
            WeaponType::Dagger => skill_id::APUNALAR,
            WeaponType::Sword => skill_id::ARMAS,
        },
    }
}

/// `UserImpactoNpc` (:222) / `UsuarioImpacto` (:942): pick the attack power for the equipped
/// weapon. `ModificadorPoderAtaqueArmas` for everything but projectiles (daggers and
/// wrestling use the *armas* class modifier too, with their own skill).
pub fn poder_ataque_for_weapon(
    weapon: Option<&ObjData>,
    skill_of: impl Fn(i32) -> i32,
    agility: i32,
    level: i32,
    mod_ataque_armas: f32,
    mod_ataque_proyectiles: f32,
) -> (i64, i32) {
    let skill = skill_required_for_weapon(weapon);
    let modifier = if skill == skill_id::PROYECTILES {
        mod_ataque_proyectiles
    } else {
        mod_ataque_armas
    };
    (attack_power(skill_of(skill), agility, level, modifier), skill)
}

/// `UsuarioImpacto` (:1000-1008): `clamp(5, 95, 50 + (PA - PE)*0.4 + WeaponHitModifier)`, then the
/// meditating victim loses 25% of its evasion: `ProbEvadir = (100-Prob)*0.75; Prob = min(90, 100-ProbEvadir)`.
pub fn prob_impacto_user(poder_ataque: i64, poder_evasion: i64, victim_meditando: bool, weapon_hit_modifier: i32) -> i32 {
    let raw = 50.0 + (poder_ataque - poder_evasion) as f64 * 0.4 + weapon_hit_modifier as f64;
    let mut prob = vb_round(raw).clamp(5, 95) as i32;
    if victim_meditando {
        let prob_evadir = vb_round((100 - prob) as f64 * 0.75) as i32;
        prob = (100 - prob_evadir).min(90);
    }
    prob
}

/// `UserImpactoNpc` (:245): `clamp(5, 95, 50 + (PA - NpcPoderEvasion)*0.4)`.
pub fn prob_impacto_npc(poder_ataque: i64, npc_poder_evasion: i64) -> i32 {
    vb_round(50.0 + (poder_ataque - npc_poder_evasion) as f64 * 0.4).clamp(5, 95) as i32
}

/// `NpcImpacto` (:272): `clamp(10, 90, 50 + (NpcPA - UserEvasion)*0.4)` where the user
/// evasion already includes the shield power.
pub fn prob_npc_impacto(npc_poder_ataque: i64, user_evasion: i64) -> i32 {
    vb_round(50.0 + (npc_poder_ataque - user_evasion) as f64 * 0.4).clamp(10, 90) as i32
}

/// `UsuarioImpacto` (:975-985): shield rejection chance vs users.
/// `clamp(10, 90, Porcentaje * Defensa / max(Defensa+Tacticas, 1))`; 10 when Defensa = 0;
/// 0 without shield or with `Porcentaje = 0`.
pub fn prob_rechazo_escudo_user(has_shield: bool, porcentaje: i32, defensa: i32, tacticas: i32) -> i32 {
    if !has_shield || porcentaje <= 0 {
        return 0;
    }
    if defensa <= 0 {
        return 10;
    }
    vb_round(porcentaje as f64 * (defensa as f64 / (defensa + tacticas).max(1) as f64))
        .clamp(10, 90) as i32
}

/// `NpcImpacto` (:279-283): `clamp(10, 90, 100 * Defensa / (Defensa + Tacticas))`; only
/// evaluated when the shield has `Porcentaje > 0` and `Defensa + Tacticas > 0`.
pub fn prob_rechazo_escudo_npc(defensa: i32, tacticas: i32) -> Option<i32> {
    if defensa + tacticas <= 0 {
        return None;
    }
    Some(vb_round(100.0 * defensa as f64 / (defensa + tacticas) as f64).clamp(10, 90) as i32)
}

// =====================================================================
// Damage
// =====================================================================

/// `GetHitRangeValues` (:2398): vs NPC the `*ToNPC` range wins when any of the two is set.
pub fn hit_range(obj: &ObjData, vs_npc: bool) -> (i32, i32) {
    if vs_npc && (obj.min_hit_to_npc > 0 || obj.max_hit_to_npc > 0) {
        (
            if obj.min_hit_to_npc > 0 { obj.min_hit_to_npc } else { obj.min_hit },
            if obj.max_hit_to_npc > 0 { obj.max_hit_to_npc } else { obj.max_hit },
        )
    } else {
        (obj.min_hit, obj.max_hit)
    }
}

/// `GetClassAttackModifier` (:308).
pub fn class_attack_modifier(weapon: &ObjData, dano_proyectiles: f32, dano_wrestling: f32, dano_armas: f32) -> f32 {
    if weapon.proyectil {
        dano_proyectiles
    } else if weapon.weapon_type == WeaponType::Knuckle {
        dano_wrestling
    } else {
        dano_armas
    }
}

/// Inputs of `GetUserDamageWithItem` (:318) already resolved from the equipment.
pub struct DamageInputs<'a> {
    pub user_min_hit: i32,
    pub user_max_hit: i32,
    pub fuerza: i32,
    pub weapon: Option<&'a ObjData>,
    pub ammo: Option<&'a ObjData>,
    pub vs_npc: bool,
    pub dano_armas: f32,
    pub dano_proyectiles: f32,
    pub dano_wrestling: f32,
    /// Ship (navigating) `MinHIT/MaxHit` bonus, else mount, else none.
    pub ship_or_saddle: Option<&'a ObjData>,
    pub navigating: bool,
    pub mounted: bool,
}

/// `GetUserDamageWithItem`:
/// `(3*WeaponDmg + MaxWeaponDmg*0.2*max(0, Fuerza-15) + UserDmg) * ClassMod` (+ ship/mount roll).
/// Without a weapon `WeaponDmg = MaxWeaponDmg = 0` and the wrestling modifier applies.
/// Projectiles add the ammo roll to `WeaponDmg` **and** its max to `MaxWeaponDmg`.
pub fn get_user_damage_with_item(i: &DamageInputs) -> i64 {
    let user_damage = rand_range(i.user_min_hit, i.user_max_hit.max(i.user_min_hit)) as i64;
    let mut weapon_damage = 0i64;
    let mut max_weapon_damage = 0i64;
    let class_modifier = match i.weapon {
        Some(w) => {
            let (min, max) = hit_range(w, i.vs_npc);
            weapon_damage = rand_range(min, max.max(min)) as i64;
            max_weapon_damage = max as i64;
            if w.proyectil && w.municion > 0 {
                if let Some(a) = i.ammo {
                    let (amin, amax) = hit_range(a, i.vs_npc);
                    weapon_damage += rand_range(amin, amax.max(amin)) as i64;
                    max_weapon_damage += amax as i64;
                }
            }
            class_attack_modifier(w, i.dano_proyectiles, i.dano_wrestling, i.dano_armas)
        }
        None => i.dano_wrestling,
    };
    let base = (3.0 * weapon_damage as f64
        + max_weapon_damage as f64 * 0.2 * (i.fuerza - 15).max(0) as f64
        + user_damage as f64)
        * class_modifier as f64;
    let mut damage = vb_round(base); // GetUserDamageWithItem As Long
    if let Some(v) = i.ship_or_saddle {
        if i.navigating || i.mounted {
            damage += rand_range(v.min_hit, v.max_hit.max(v.min_hit)) as i64;
        }
    }
    damage
}

/// `UserDamageToUser` (:1110-1126): random body part 1..8 — 1 = head; anything above the
/// torso (7, 8) is re-rolled between the legs and the torso.
pub fn lugar_golpe_user() -> i32 {
    let lugar = rand_range(1, 8);
    if lugar > B_TORSO {
        rand_range(B_PIERNA_IZQUIERDA, B_TORSO)
    } else {
        lugar
    }
}

/// `NpcDamage` (:517): random body part 1..6 — 1 = head (helmet), else armor + shield.
pub fn lugar_golpe_npc() -> i32 {
    rand_range(1, 6)
}

// =====================================================================
// Specials: stab, critical, unequip
// =====================================================================

/// `PuedeApuñalar` (:1988): `(Asesino Or Apuñalar >= MIN_APUÑALAR) And arma.Apuñala`.
pub fn puede_apunalar(class: PlayerClass, skill_apunalar: i32, weapon: Option<&ObjData>) -> bool {
    match weapon {
        Some(w) => (class == PlayerClass::Asesino || skill_apunalar >= MIN_APUNALAR) && w.apunala,
        None => false,
    }
}

/// `PuedeGolpeCritico` (:2000): Bandido with a knuckle weapon.
pub fn puede_golpe_critico(class: PlayerClass, weapon: Option<&ObjData>) -> bool {
    matches!(weapon, Some(w) if class == PlayerClass::Bandido && w.weapon_type == WeaponType::Knuckle)
}

/// `PuedeDesequiparDeUnGolpe` (:1964): Bandido/Ladrón, unarmed or with knuckles.
pub fn puede_desequipar_de_un_golpe(class: PlayerClass, weapon: Option<&ObjData>) -> bool {
    if let Some(w) = weapon {
        if w.weapon_type != WeaponType::Knuckle {
            return false;
        }
    }
    matches!(class, PlayerClass::Bandido | PlayerClass::Ladron)
}

/// `ProbabilidadDesequipar` (:2032): Bandido `0.15 * skill(weapon)`, Ladrón 33, else 0.
/// (`bandit_unequip_bonus` feature flag off → 0.15.)
pub fn prob_desequipar(class: PlayerClass, skill_for_weapon: i32) -> i32 {
    match class {
        PlayerClass::Bandido => (0.15 * skill_for_weapon as f64) as i32,
        PlayerClass::Ladron => 33,
        _ => 0,
    }
}

fn clamp_chance(v: f32) -> f32 {
    v.clamp(0.0, 100.0)
}

/// `GetStabbingChanceBase` (:2339): `skill * <clase>StabbingChance` + weapon extra, clamped.
pub fn stabbing_chance_base(class: PlayerClass, skill_apunalar: i32, bs: &crate::data::balance::BackstabConfig, weapon_extra: f32) -> f32 {
    let per_point = match class {
        PlayerClass::Asesino => bs.assasin_stabbing_chance,
        PlayerClass::Bardo => bs.bard_stabbing_chance,
        PlayerClass::Cazador => bs.hunter_stabbing_chance,
        _ => bs.generic_stabbing_chance,
    };
    clamp_chance(skill_apunalar as f32 * per_point + weapon_extra)
}

/// `GetCriticalHitChanceBase` (:2371): `Wrestling * BanditCriticalHitChance` + weapon extra.
pub fn critical_chance_base(skill_wrestling: i32, bs: &crate::data::balance::BackstabConfig, weapon_extra: f32) -> f32 {
    clamp_chance(skill_wrestling as f32 * bs.bandit_critical_hit_chance + weapon_extra)
}

/// `GetBackHitBonusChanceAgainstUsers` (:2360): same heading and adjacent → `ExtraBackstabChance`.
pub fn back_hit_bonus(attacker_heading: i32, victim_heading: i32, distance: i32, bs: &crate::data::balance::BackstabConfig) -> f32 {
    if attacker_heading == victim_heading && distance <= 1 {
        bs.extra_backstab_chance
    } else {
        0.0
    }
}

/// `Distancia` (Chebyshev, as `Distancia(pos, pos)` in AO20 for same-map positions).
pub fn distancia(x1: i32, y1: i32, x2: i32, y2: i32) -> i32 {
    (x1 - x2).abs().max((y1 - y2).abs())
}

/// `DesequiparObjetoDeUnGolpe` (:1268): which slot goes, by body part. Returns
/// (helmet, weapon, shield) — exactly one true, or none.
pub fn desequipar_target(lugar: i32, has_helmet: bool, has_weapon: bool, has_shield: bool) -> (bool, bool, bool) {
    let (mut casco, mut arma, mut escudo) = (false, false, false);
    match lugar {
        B_CABEZA => {
            casco = has_helmet;
            arma = !casco && has_weapon;
            escudo = !casco && !arma && has_shield;
        }
        B_BRAZO_DERECHO | B_BRAZO_IZQUIERDO | B_TORSO => {
            arma = has_weapon;
            escudo = !arma && has_shield;
            casco = !escudo && !arma && has_helmet;
        }
        B_PIERNA_DERECHA | B_PIERNA_IZQUIERDA => {
            escudo = has_shield;
            arma = !escudo && has_weapon;
            casco = !escudo && !arma && has_helmet;
        }
        _ => {}
    }
    (casco, arma, escudo)
}

/// `UserDamageToUser` bonus rule (:1236-1244): with the victim at full HP, a total ≥ MaxHp
/// is capped to MinHp — "simulates the death" without overkill.
pub fn apply_bonus_damage(damage: i64, bonus: i64, victim_min_hp: i32, victim_max_hp: i32) -> i64 {
    if bonus <= 0 {
        return damage;
    }
    let total = damage + bonus;
    if victim_min_hp == victim_max_hp && total >= victim_max_hp as i64 {
        return victim_min_hp as i64;
    }
    total
}

#[cfg(test)]
mod tests {
    use super::*;
    use crate::data::balance::BackstabConfig;

    fn weapon(weapon_type: WeaponType, min: i32, max: i32, apunala: bool) -> ObjData {
        let mut o = ObjData::default();
        o.weapon_type = weapon_type;
        o.min_hit = min;
        o.max_hit = max;
        o.apunala = apunala;
        o.proyectil = weapon_type == WeaponType::Bow;
        o
    }

    #[test]
    fn attack_power_matches_hand_calculation() {
        // Guerrero ELV 40, Armas 100, Agi 18, MODATAQUEARMAS 1.1: (100 + 3*18) * 1.1 = 169.4 → 169; + 70
        assert_eq!(attack_power(100, 18, 40, 1.1), 239);
        // Under level 12 there is no level bonus.
        assert_eq!(attack_power(50, 10, 5, 1.0), 65);
    }

    #[test]
    fn evasion_matches_hand_calculation() {
        // Bardo Tacticas 100, Agi 20, MODEVASION 1.2, ELV 40: (100 + 60) * 1.2 + 70 = 262
        assert_eq!(poder_evasion(100, 20, 40, 1.2), 262);
        // Shield: Defensa 100, MODESCUDO 1.0, Porcentaje 100 → 50; Porcentaje 5 → 2.5 → 2 (banker's)
        assert_eq!(poder_evasion_escudo(100, 1.0, 100), 50);
        assert_eq!(poder_evasion_escudo(100, 1.0, 5), 2);
    }

    #[test]
    fn hit_chance_and_meditation() {
        // 50 + (239 - 262) * 0.4 = 40.8 → 41
        assert_eq!(prob_impacto_user(239, 262, false, 0), 41);
        // meditating: evade = 59 * 0.75 = 44.25 → 44; prob = 56
        assert_eq!(prob_impacto_user(239, 262, true, 0), 56);
        assert_eq!(prob_impacto_user(1000, 0, false, 0), 95);
        assert_eq!(prob_impacto_user(0, 1000, false, 0), 5);
        assert_eq!(prob_impacto_npc(0, 1000), 5);
        assert_eq!(prob_npc_impacto(0, 1000), 10);
        assert_eq!(prob_npc_impacto(1000, 0), 90);
    }

    #[test]
    fn shield_rejection() {
        assert_eq!(prob_rechazo_escudo_user(false, 100, 100, 100), 0);
        assert_eq!(prob_rechazo_escudo_user(true, 0, 100, 100), 0);
        assert_eq!(prob_rechazo_escudo_user(true, 100, 0, 100), 10);
        assert_eq!(prob_rechazo_escudo_user(true, 100, 100, 100), 50);
        assert_eq!(prob_rechazo_escudo_user(true, 5, 100, 100), 10);
        assert_eq!(prob_rechazo_escudo_user(true, 100, 100, 0), 90);
        assert_eq!(prob_rechazo_escudo_npc(100, 100), Some(50));
        assert_eq!(prob_rechazo_escudo_npc(0, 0), None);
    }

    #[test]
    fn skill_for_weapon() {
        assert_eq!(skill_required_for_weapon(None), skill_id::WRESTERLING);
        assert_eq!(skill_required_for_weapon(Some(&weapon(WeaponType::Dagger, 1, 2, true))), skill_id::APUNALAR);
        assert_eq!(skill_required_for_weapon(Some(&weapon(WeaponType::Bow, 1, 2, false))), skill_id::PROYECTILES);
        assert_eq!(skill_required_for_weapon(Some(&weapon(WeaponType::Sword, 1, 2, false))), skill_id::ARMAS);
    }

    #[test]
    fn damage_formula_bounds() {
        // Sword 10-15, Fuerza 20, user 2-2, mod 1.0: 3*d + 15*0.2*5 + 2 → d=10: 47, d=15: 62
        let w = weapon(WeaponType::Sword, 10, 15, false);
        for _ in 0..200 {
            let d = get_user_damage_with_item(&DamageInputs {
                user_min_hit: 2, user_max_hit: 2, fuerza: 20, weapon: Some(&w), ammo: None, vs_npc: false,
                dano_armas: 1.0, dano_proyectiles: 1.0, dano_wrestling: 0.4,
                ship_or_saddle: None, navigating: false, mounted: false,
            });
            assert!((47..=62).contains(&d), "damage {d}");
        }
        // Unarmed: (0 + 0 + user) * wrestling mod → 5 * 0.4 = 2
        let d = get_user_damage_with_item(&DamageInputs {
            user_min_hit: 5, user_max_hit: 5, fuerza: 20, weapon: None, ammo: None, vs_npc: false,
            dano_armas: 1.0, dano_proyectiles: 1.0, dano_wrestling: 0.4,
            ship_or_saddle: None, navigating: false, mounted: false,
        });
        assert_eq!(d, 2);
    }

    #[test]
    fn npc_hit_range_overrides() {
        let mut w = weapon(WeaponType::Sword, 10, 15, false);
        w.min_hit_to_npc = 20;
        assert_eq!(hit_range(&w, true), (20, 15));
        assert_eq!(hit_range(&w, false), (10, 15));
    }

    #[test]
    fn specials_gates() {
        let dagger = weapon(WeaponType::Dagger, 1, 2, true);
        let sword = weapon(WeaponType::Sword, 1, 2, false);
        assert!(puede_apunalar(PlayerClass::Asesino, 0, Some(&dagger)));
        assert!(puede_apunalar(PlayerClass::Guerrero, 10, Some(&dagger)));
        assert!(!puede_apunalar(PlayerClass::Guerrero, 9, Some(&dagger)));
        assert!(!puede_apunalar(PlayerClass::Asesino, 100, Some(&sword)));
        assert!(!puede_apunalar(PlayerClass::Asesino, 100, None));
        assert!(!puede_golpe_critico(PlayerClass::Bandido, Some(&sword)));
        assert!(puede_desequipar_de_un_golpe(PlayerClass::Bandido, None));
        assert!(puede_desequipar_de_un_golpe(PlayerClass::Ladron, None));
        assert!(!puede_desequipar_de_un_golpe(PlayerClass::Ladron, Some(&sword)));
        assert!(!puede_desequipar_de_un_golpe(PlayerClass::Guerrero, None));
        assert_eq!(prob_desequipar(PlayerClass::Bandido, 100), 15);
        assert_eq!(prob_desequipar(PlayerClass::Ladron, 100), 33);
        assert_eq!(prob_desequipar(PlayerClass::Mago, 100), 0);
    }

    #[test]
    fn backstab_chances_follow_balance() {
        let bs = BackstabConfig { assasin_stabbing_chance: 0.5, generic_stabbing_chance: 0.1, extra_backstab_chance: 10.0, ..Default::default() };
        assert_eq!(stabbing_chance_base(PlayerClass::Asesino, 100, &bs, 0.0), 50.0);
        assert_eq!(stabbing_chance_base(PlayerClass::Guerrero, 100, &bs, 5.0), 15.0);
        assert_eq!(back_hit_bonus(1, 1, 1, &bs), 10.0);
        assert_eq!(back_hit_bonus(1, 2, 1, &bs), 0.0);
        assert_eq!(back_hit_bonus(1, 1, 2, &bs), 0.0);
        // AO20 default Balance.dat has no [BACKSTAB] → every chance is 0.
        assert_eq!(stabbing_chance_base(PlayerClass::Asesino, 100, &BackstabConfig::default(), 0.0), 0.0);
    }

    #[test]
    fn unequip_target_by_body_part() {
        assert_eq!(desequipar_target(B_CABEZA, true, true, true), (true, false, false));
        assert_eq!(desequipar_target(B_CABEZA, false, true, true), (false, true, false));
        assert_eq!(desequipar_target(B_TORSO, true, true, true), (false, true, false));
        assert_eq!(desequipar_target(B_TORSO, true, false, true), (false, false, true));
        assert_eq!(desequipar_target(B_PIERNA_DERECHA, true, true, true), (false, false, true));
        assert_eq!(desequipar_target(B_PIERNA_DERECHA, true, true, false), (false, true, false));
        assert_eq!(desequipar_target(B_TORSO, false, false, false), (false, false, false));
    }

    #[test]
    fn bonus_damage_full_hp_rule() {
        assert_eq!(apply_bonus_damage(30, 0, 100, 100), 30);
        assert_eq!(apply_bonus_damage(30, 20, 100, 100), 50);
        assert_eq!(apply_bonus_damage(80, 40, 100, 100), 100);
        assert_eq!(apply_bonus_damage(80, 40, 90, 100), 120);
    }

    #[test]
    fn body_parts_in_range() {
        for _ in 0..500 {
            let l = lugar_golpe_user();
            assert!((1..=6).contains(&l));
            let n = lugar_golpe_npc();
            assert!((1..=6).contains(&n));
        }
    }
}
