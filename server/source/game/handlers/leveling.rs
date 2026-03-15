//! Level-up system: check_user_level, level_up_gains, hp_gain_from_distribution.
//! Extracted from mod.rs to reduce file size.

use tracing::info;

use crate::net::ConnectionId;
use crate::protocol::binary_packets;
use crate::game::class_race::{PlayerClass, PlayerRace};
use crate::game::types::{GameState, SendTarget};
use super::common::*;

// =====================================================================
// Level up system
// =====================================================================

const MAX_LEVEL: i32 = 50;

/// Check if user has enough exp to level up, and apply it.
/// VB6 parity: two separate paths for levels 1-49 vs 50+.
pub(crate) async fn check_user_level(state: &mut GameState, conn_id: ConnectionId) {
    loop {
        let (level, exp, class, race, intelligence, constitution) = match state.users.get(&conn_id) {
            Some(u) if u.logged => (
                u.level, u.exp, u.class, u.race,
                u.attributes[2], // Intelligence
                u.attributes[4], // Constitution
            ),
            _ => return,
        };

        if level >= MAX_LEVEL {
            if let Some(user) = state.users.get_mut(&conn_id) {
                user.exp = 0;
            }
            return;
        }

        let exp_needed = state.exp_for_level(level);
        if exp_needed <= 0 || exp < exp_needed {
            return;
        }

        // ══════════════════════════════════════════════════════════════
        // VB6: Level 1-49 path — full promedio HP + INT-based mana
        // ══════════════════════════════════════════════════════════════
        let (hp_gain, mana_gain, sta_gain, hit_gain) =
            level_up_gains(class, race, level, constitution, intelligence, &state.game_data.balance);

        if let Some(user) = state.users.get_mut(&conn_id) {
            let new_level = level + 1;
            user.level = new_level;
            // VB6 parity: Exp = Exp - ELU (carry over excess exp)
            user.exp = user.exp - exp_needed;

            // HP: add gain, cap at STAT_MAXHP (VB6: 999)
            user.max_hp += hp_gain;
            if user.max_hp > STAT_MAXHP { user.max_hp = STAT_MAXHP; }

            // Mana: add with cap (VB6: <36 → STAT_MAXMAN, >=36 → 9999)
            user.max_mana += mana_gain;
            let mana_cap = if user.level < 36 { STAT_MAXMAN } else { 9999 };
            if user.max_mana > mana_cap { user.max_mana = mana_cap; }

            // STA: add with cap
            user.max_sta += sta_gain;
            if user.max_sta > STAT_MAXSTA { user.max_sta = STAT_MAXSTA; }

            // Hit: add with level-dependent caps
            let hit_cap = if user.level < 36 { STAT_MAXHIT_UNDER36 } else { STAT_MAXHIT_OVER36 };
            user.max_hit += hit_gain;
            if user.max_hit > hit_cap { user.max_hit = hit_cap; }
            user.min_hit += hit_gain;
            if user.min_hit > hit_cap { user.min_hit = hit_cap; }

            // VB6 parity: full heal on level up (MinHP = MaxHP)
            let final_max_hp = user.max_hp;
            let final_max_mana = user.max_mana;
            let final_max_sta = user.max_sta;
            user.min_hp = final_max_hp;
            user.min_mana = final_max_mana;
            user.min_sta = final_max_sta;
        }

        let new_level = level + 1;
        let char_index = state.users.get(&conn_id).map(|u| u.char_index.0).unwrap_or(0);
        let map = state.users.get(&conn_id).map(|u| u.pos_map).unwrap_or(0);
        let x = state.users.get(&conn_id).map(|u| u.pos_x).unwrap_or(0);
        let y = state.users.get(&conn_id).map(|u| u.pos_y).unwrap_or(0);

        // Level up sound + FX
        state.send_data_bytes(SendTarget::ToArea { map, x, y }, &binary_packets::write_play_wave(6, x as i16, y as i16));
        state.send_data_bytes(
            SendTarget::ToArea { map, x, y },
            &binary_packets::write_char_particle_create(char_index as i16, 58),
        );

        // VB6: ||67 = "Has subido de nivel!"
        state.send_msg_id(conn_id, 67, "");

        // VB6: Stat gain notifications (||71=HP, ||72=STA, ||73=MANA, ||74/75=HIT)
        if hp_gain > 0 {
            state.send_msg_id(conn_id, 71, &hp_gain.to_string());
        }
        if sta_gain > 0 {
            state.send_msg_id(conn_id, 72, &sta_gain.to_string());
        }
        if mana_gain > 0 {
            state.send_msg_id(conn_id, 73, &mana_gain.to_string());
        }
        if hit_gain > 0 {
            state.send_msg_id(conn_id, 74, &hit_gain.to_string());
            state.send_msg_id(conn_id, 75, &hit_gain.to_string());
        }

        // VB6: If .Stats.ELV = 1 Then Pts = 10 Else Pts = Pts + 5
        if let Some(user) = state.users.get_mut(&conn_id) {
            if level == 1 {
                user.skill_pts_libres += 10;
            } else {
                user.skill_pts_libres += 5;
            }
        }

        // Send updated stats
        state.send_bytes(conn_id, &binary_packets::write_level_update(new_level as u8));
        send_stats_hp(state, conn_id).await;
        send_stats_mana(state, conn_id).await;
        send_stats_sta(state, conn_id).await;
        send_stats_exp(state, conn_id).await;

        let name = state.users.get(&conn_id).map(|u| u.char_name.clone()).unwrap_or_default();
        info!("[LEVEL] '{}' reached level {} (HP+{}, MANA+{}, STA+{}, HIT+{})",
            name, new_level, hp_gain, mana_gain, sta_gain, hit_gain);

        // 13.3: Level 50 — announcement + skill points (one-time)
        if new_level == 50 {
            state.send_chat_talk_to(SendTarget::ToAll, 0i16, &format!("{} ha alcanzado el nivel 50!", name), 65535);
            state.send_msg_id(conn_id, 57, "50");
            // TODO: AgregarPuntos(50) — add 50 free skill points
        }

        // Continue looping in case of multi-level jumps
    }
}

// VB6 13.3 constants (Declares.bas)
const STAT_MAXHP: i32 = 999;             // VB6: STAT_MAXHP = 999
const AUMENTO_ST_DEF: i32 = 15;
const AUMENTO_ST_LADRON: i32 = 18;    // AumentoSTDef + 3
const AUMENTO_ST_BANDIDO: i32 = 18;   // AumentoSTDef + 3
const AUMENTO_ST_MAGO: i32 = 14;      // AumentoSTDef - 1
const AUMENTO_ST_TRABAJADOR: i32 = 40; // AumentoSTDef + 25
const STAT_MAXSTA: i32 = 999;         // VB6: STAT_MAXSTA = 999
const STAT_MAXMAN: i32 = 9999;        // VB6: STAT_MAXMAN = 9999
const STAT_MAXHIT_UNDER36: i32 = 99;  // VB6: STAT_MAXHIT_UNDER36
const STAT_MAXHIT_OVER36: i32 = 999;  // VB6: STAT_MAXHIT_OVER36

/// VB6 13.3 level-up stat gains (Modulo_UsUaRiOs.bas:499-707).
/// HP: MODVIDA + CON-based probabilistic distribution.
/// Mana: INT-based per class (exact VB6 multipliers).
/// STA: per-class constants.
/// HIT: per-class with level>35 cutoffs.
/// Returns (hp_gain, mana_gain, sta_gain, hit_gain).
fn level_up_gains(class: PlayerClass, _race: PlayerRace, level: i32, constitution: i32, intelligence: i32,
                  balance: &crate::data::balance::BalanceData) -> (i32, i32, i32, i32) {
    use PlayerClass::*;

    // ── HP gain (VB6: MODVIDA + CON distribution) ──
    let mod_vida = balance.class_mod_vida_e(class);
    // VB6: Promedio = ModVida(clase) - (21 - Constitucion) * 0.5
    let promedio = mod_vida as f64 - (21.0 - constitution as f64) * 0.5;
    let hp_gain = hp_gain_from_distribution(promedio, &balance.hp_distribution);

    // ── Mana gain (VB6 exact multipliers) ──
    let mana_gain = match class {
        Mago    => (2.8 * intelligence as f64) as i32,
        Clerigo => 2 * intelligence,
        Druida  => 2 * intelligence,
        Bardo   => 2 * intelligence,
        Asesino => intelligence,
        Paladin => intelligence,
        Bandido => intelligence / 3 * 2,
        Guerrero | Cazador | Ladron | Trabajador | Pirata => 0,
    };

    // ── STA gain (VB6 exact constants) ──
    let sta_gain = match class {
        Mago       => AUMENTO_ST_MAGO,
        Ladron     => AUMENTO_ST_LADRON,
        Bandido    => AUMENTO_ST_BANDIDO,
        Trabajador => AUMENTO_ST_TRABAJADOR,
        _ => AUMENTO_ST_DEF,
    };

    // ── HIT gain (VB6 exact with level>35 cutoffs) ──
    let hit_gain = match class {
        Guerrero => if level > 35 { 2 } else { 3 },
        Cazador  => if level > 35 { 2 } else { 3 },
        Pirata   => 3,
        Paladin  => if level > 35 { 1 } else { 3 },
        Asesino  => if level > 35 { 1 } else { 3 },
        Bandido  => if level > 35 { 1 } else { 3 },
        Ladron   => 2,
        Mago     => 1,
        Clerigo  => 2,
        Druida   => 2,
        Bardo    => 2,
        Trabajador => 2,
    };

    (hp_gain, mana_gain, sta_gain, hit_gain)
}

/// VB6 HP distribution using probabilistic brackets (Modulo_UsUaRiOs.bas:545-610).
fn hp_gain_from_distribution(promedio: f64, dist: &crate::data::balance::HpDistribution) -> i32 {
    let roll = rand_range(1, 100);
    let is_half = (promedio * 2.0).fract().abs() > 0.01; // Half-integer check

    if is_half {
        // Semi-integer distribution (4 brackets: +1.5, +0.5, -0.5, -1.5)
        let mut cumulative = 0;
        cumulative += dist.semientera[0]; // S1
        if roll <= cumulative { return (promedio + 1.5) as i32; }
        cumulative += dist.semientera[1]; // S2
        if roll <= cumulative { return (promedio + 0.5) as i32; }
        cumulative += dist.semientera[2]; // S3
        if roll <= cumulative { return (promedio - 0.5) as i32; }
        return (promedio - 1.5).max(1.0) as i32;
    } else {
        // Integer distribution (5 brackets: +2, +1, 0, -1, -2)
        let mut cumulative = 0;
        cumulative += dist.entera[0]; // E1
        if roll <= cumulative { return (promedio as i32) + 2; }
        cumulative += dist.entera[1]; // E2
        if roll <= cumulative { return (promedio as i32) + 1; }
        cumulative += dist.entera[2]; // E3
        if roll <= cumulative { return promedio as i32; }
        cumulative += dist.entera[3]; // E4
        if roll <= cumulative { return (promedio as i32) - 1; }
        return ((promedio as i32) - 2).max(1);
    }
}
