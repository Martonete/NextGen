//! Duel-bot AI (`AI_BOT_DUEL`) — GM `/BOT <clase>` practice opponents.
//!
//! Ticked separately from `tick_npc_ai` (see `tick_bot_ai`, called from
//! `main.rs` every 8th 40ms game tick, ~320ms) so bots react at a player-like
//! pace instead of the slow shared monster AI cadence. Movement/attack helpers
//! are reused from `npcs.rs`/`npc_move.rs` wherever the existing NPC-vs-player
//! systems already fit (melee hit/miss RNG, spell damage/heal, area broadcast);
//! only genuinely new behavior (self-cleanse, ranged shot, zigzag movement,
//! per-bot cooldowns) gets new code.

use crate::game::class_race::PlayerClass;
use crate::game::handlers::common::rand_range;
use crate::game::handlers::gm_bot;
use crate::game::handlers::world;
use crate::game::types::{GameState, SendTarget};
use crate::net::ConnectionId;
use crate::protocol::binary_packets;

// Per-bot attack cooldowns, in 40ms game ticks. NOT sourced from
// `state.intervals` — `Intervalos.ini`'s values (Golpe=37, Flechas=28,
// LanzarHechizo=13, ...) are misinterpreted as *milliseconds* by
// `load_intervals`/`ms_to_ticks` (game/types.rs), which rounds LanzarHechizo
// down to 0 ticks — a real (pre-existing, not bot-related) cooldown bug
// affecting real players too, left untouched here since fixing it changes
// live combat pacing server-wide. These constants instead directly encode
// the intended pacing (~1 attack/sec, matching a real player's actual
// cooldown feel) so bots don't spam every AI decision (~320ms).
const GOLPE_CD_TICKS: u64 = 37; // ~1.48s
const FLECHA_CD_TICKS: u64 = 28; // ~1.12s
const HECHIZO_CD_TICKS: u64 = 35; // ~1.40s
const CONTROL_CD_TICKS: u64 = 125; // ~5s before trying another paralysis/immobilize

/// Called every ~320ms from `main.rs` (game_tick_count % 8 == 0) — NOT every
/// 40ms tick, so bots don't spam decisions faster than a real player could act.
pub async fn tick_bot_ai(state: &mut GameState) {
    let indices: Vec<usize> = state.active_bot_indices.iter().copied().collect();
    for idx in indices {
        let alive = state.get_npc(idx).map(|n| n.is_alive()).unwrap_or(false);
        if !alive {
            // Bot died (or was otherwise removed) since the last tick — self-prune.
            // kill_npc() only touches active_npc_indices, not our sidecar maps.
            state.active_bot_indices.remove(&idx);
            state.bot_cooldowns.remove(&idx);
            state.bot_classes.remove(&idx);
            continue;
        }
        bot_ai_step(state, idx).await;
    }
}

async fn bot_ai_step(state: &mut GameState, npc_idx: usize) {
    let class = match state.bot_classes.get(&npc_idx).copied() {
        Some(c) => c,
        None => return,
    };

    let (map, x, y, is_paralyzed, min_hp, max_hp) = match state.get_npc(npc_idx) {
        Some(n) => (n.map, n.x, n.y, n.paralyzed, n.min_hp, n.max_hp),
        None => return,
    };

    // Priority 0: self-cleanse paralysis. npc_cast_spell can only target a
    // player, never the caster itself, so this mirrors AI_SACERDOTE_PRETORIANO's
    // pattern (ticks/npc_ai.rs) of flipping the fields directly.
    if is_paralyzed {
        self_cleanse_paralysis(state, npc_idx, map, x, y).await;
        set_hechizo_cd(state, npc_idx);
        return;
    }

    let target_conn = match acquire_target(state, npc_idx, map, x, y) {
        Some(t) => t,
        None => return, // nothing nearby — idle, don't wander like a normal monster
    };
    let (tx, ty) = match state.users.get(&target_conn) {
        Some(u) if u.logged && !u.dead => (u.pos_x, u.pos_y),
        _ => {
            if let Some(n) = state.get_npc_mut(npc_idx) {
                n.target = None;
            }
            return;
        }
    };
    let dist = (x - tx).abs() + (y - ty).abs();

    match class {
        PlayerClass::Paladin => {
            paladin_step(state, npc_idx, target_conn, map, x, y, tx, ty, dist, min_hp, max_hp).await
        }
        PlayerClass::Mago => mago_step(state, npc_idx, target_conn, map, x, y, tx, ty, dist).await,
        PlayerClass::Cazador => {
            cazador_step(state, npc_idx, target_conn, x, y, tx, ty, dist).await
        }
        _ => {}
    }
}

/// Keep chasing the same target across ticks (avoids flip-flopping between
/// two nearby players); re-acquire via the shared vision-range scan if the
/// current target logged off, died, or wandered too far away.
fn acquire_target(
    state: &mut GameState,
    npc_idx: usize,
    map: i32,
    x: i32,
    y: i32,
) -> Option<ConnectionId> {
    if let Some(existing) = state.get_npc(npc_idx).and_then(|n| n.target) {
        let still_valid = state
            .users
            .get(&existing)
            .map(|u| {
                u.logged
                    && !u.dead
                    && u.privileges == 0
                    && !u.admin_invisible
                    && (x - u.pos_x).abs() + (y - u.pos_y).abs() <= 20
            })
            .unwrap_or(false);
        if still_valid {
            return Some(existing);
        }
    }

    let found = super::find_nearest_player(state, map, x, y);
    if let Some(n) = state.get_npc_mut(npc_idx) {
        n.target = found;
    }
    found
}

async fn self_cleanse_paralysis(state: &mut GameState, npc_idx: usize, map: i32, x: i32, y: i32) {
    let (npc_char, npc_name) = match state.get_npc(npc_idx) {
        Some(n) => (n.char_index, n.name.clone()),
        None => return,
    };
    let msg = format!("{} dice: Remover Parálisis", npc_name);
    state.send_chat_over_head_to(
        SendTarget::ToArea { map, x, y },
        &msg,
        npc_char.0 as i16,
        16711680, // vbRed, matches npc_cast_spell's magic-words color
    );
    if let Some(n) = state.get_npc_mut(npc_idx) {
        n.paralyzed = false;
        n.counter_paralisis = 0;
    }
}

/// Self-heal — same problem as self-cleanse: `npc_cast_spell`'s heal branch
/// always heals `target_conn` (a player), never the NPC caster, so this
/// applies the heal directly to the bot's own `min_hp` instead.
async fn self_heal(state: &mut GameState, npc_idx: usize, map: i32, x: i32, y: i32, spell_name: &str) {
    let spell_id = match gm_bot::find_bot_spell(state, spell_name) {
        Some(id) => id,
        None => return,
    };
    let spell = match state.get_spell(spell_id) {
        Some(s) => s.clone(),
        None => return,
    };
    let (npc_char, npc_name, max_hp) = match state.get_npc(npc_idx) {
        Some(n) => (n.char_index, n.name.clone(), n.max_hp),
        None => return,
    };

    if !spell.palabras_magicas.is_empty() {
        let msg = format!("{} dice: {}", npc_name, spell.palabras_magicas);
        state.send_chat_over_head_to(
            SendTarget::ToArea { map, x, y },
            &msg,
            npc_char.0 as i16,
            16711680,
        );
    }
    if spell.fx_grh > 0 {
        let fx =
            binary_packets::write_create_fx(npc_char.0 as i16, spell.fx_grh as i16, spell.loops as i16);
        state.send_data_bytes(SendTarget::ToArea { map, x, y }, &fx);
    }

    let heal = rand_range(spell.min_hp.max(1), spell.max_hp.max(1));
    if let Some(n) = state.get_npc_mut(npc_idx) {
        n.min_hp = (n.min_hp + heal).min(max_hp);
    }
}

/// Cazador's bow shot — the arrow visual is new (players get it from
/// `do_ranged_attack`, which is 100% player-inventory-driven and unusable for
/// an NPC caller), but the hit/miss/damage resolution reuses `npc_attack_user`
/// as-is: it has no adjacency check built in (that's enforced by whoever calls
/// it), so it works unchanged for a ranged "melee-shaped" attack too.
async fn npc_ranged_attack_user(state: &mut GameState, npc_idx: usize, target_conn: ConnectionId) {
    let npc_data = match state.get_npc(npc_idx) {
        Some(n) if n.is_alive() => (n.char_index, n.map),
        _ => return,
    };
    let (npc_char, map) = npc_data;
    let target_char = state.users.get(&target_conn).map(|u| u.char_index);

    if let Some(target_char) = target_char {
        let arrow_grh = gm_bot::compute_bot_gear(state, PlayerClass::Cazador).arrow_grh;
        if arrow_grh > 0 {
            let flechi =
                binary_packets::write_arrow(npc_char.0 as i16, target_char.0 as i16, arrow_grh as i16);
            state.send_data_bytes(SendTarget::ToMap(map), &flechi);
        }
    }

    crate::game::handlers::npc_attack_user(state, npc_idx, target_conn).await;
}

// ── Per-class decision trees ────────────────────────────────────────────

async fn paladin_step(
    state: &mut GameState,
    npc_idx: usize,
    target: ConnectionId,
    map: i32,
    x: i32,
    y: i32,
    tx: i32,
    ty: i32,
    dist: i32,
    min_hp: i32,
    max_hp: i32,
) {
    if max_hp > 0 && min_hp * 100 / max_hp < 40 && hechizo_ready(state, npc_idx) {
        self_heal(state, npc_idx, map, x, y, "Curar Heridas Graves").await;
        set_hechizo_cd(state, npc_idx);
        return;
    }

    let target_controlled = state
        .users
        .get(&target)
        .map(|u| u.paralyzed || u.immobilized)
        .unwrap_or(false);

    if dist <= 1 {
        if !target_controlled
            && hechizo_ready(state, npc_idx)
            && control_ready(state, npc_idx)
            && rand_range(1, 100) <= 20
        {
            if let Some(spell_id) = gm_bot::find_bot_spell(state, "Inmovilizar") {
                crate::game::handlers::npc_cast_spell(state, npc_idx, target, spell_id).await;
                set_hechizo_cd(state, npc_idx);
                set_control_cd(state, npc_idx);
                return;
            }
        }
        if golpe_ready(state, npc_idx) {
            crate::game::handlers::npc_attack_user(state, npc_idx, target).await;
            set_golpe_cd(state, npc_idx);
        }
        return;
    }

    if dist <= 8 && hechizo_ready(state, npc_idx) {
        let spell_name = if !target_controlled && control_ready(state, npc_idx) && rand_range(1, 100) <= 35 {
            "Inmovilizar"
        } else {
            "Fuego Divino"
        };
        if let Some(spell_id) = gm_bot::find_bot_spell(state, spell_name) {
            crate::game::handlers::npc_cast_spell(state, npc_idx, target, spell_id).await;
            set_hechizo_cd(state, npc_idx);
            if spell_name == "Inmovilizar" {
                set_control_cd(state, npc_idx);
            }
            return;
        }
    }

    if dist <= 2 {
        sidestep(state, npc_idx, x, y, tx, ty).await;
    } else {
        move_toward(state, npc_idx, x, y, tx, ty, true).await;
    }
}

async fn mago_step(
    state: &mut GameState,
    npc_idx: usize,
    target: ConnectionId,
    map: i32,
    x: i32,
    y: i32,
    tx: i32,
    ty: i32,
    dist: i32,
) {
    const IDEAL: i32 = 6;
    const DAMAGE_POOL: &[&str] = &[
        "Apocalípsis",
        "Descarga Eléctrica",
        "Fuego Mágico",
        "Relampago",
        "Destello Mágico",
        "Tormenta de Fuego",
        "Inferno",
        "Flecha Mágica",
    ];

    // Attempt to cast whenever in range — including dist<=1 if cornered, since
    // the Mago never melees. Only falls through to movement if this doesn't fire.
    if dist <= 8 && hechizo_ready(state, npc_idx) {
        let target_controlled = state
            .users
            .get(&target)
            .map(|u| u.paralyzed || u.immobilized)
            .unwrap_or(false);
        let spell_name = if !target_controlled
            && control_ready(state, npc_idx)
            && rand_range(1, 100) <= 35
        {
            "Paralizar"
        } else {
            DAMAGE_POOL[rand_range(0, DAMAGE_POOL.len() as i32 - 1) as usize]
        };
        if let Some(spell_id) = gm_bot::find_bot_spell(state, spell_name) {
            crate::game::handlers::npc_cast_spell(state, npc_idx, target, spell_id).await;
            set_hechizo_cd(state, npc_idx);
            if spell_name == "Paralizar" {
                set_control_cd(state, npc_idx);
            }
            return;
        }
    }

    if dist < IDEAL - 1 {
        move_toward(state, npc_idx, x, y, tx, ty, false).await;
    } else if dist > IDEAL + 1 {
        move_toward(state, npc_idx, x, y, tx, ty, true).await;
    } else {
        sidestep(state, npc_idx, x, y, tx, ty).await;
    }
    let _ = map;
}

async fn cazador_step(
    state: &mut GameState,
    npc_idx: usize,
    target: ConnectionId,
    x: i32,
    y: i32,
    tx: i32,
    ty: i32,
    dist: i32,
) {
    const IDEAL: i32 = 8;

    if dist <= 10 && flecha_ready(state, npc_idx) {
        npc_ranged_attack_user(state, npc_idx, target).await;
        set_flecha_cd(state, npc_idx);
        return;
    }

    if dist < IDEAL - 1 {
        move_toward(state, npc_idx, x, y, tx, ty, false).await;
    } else if dist > IDEAL + 1 {
        move_toward(state, npc_idx, x, y, tx, ty, true).await;
    } else {
        sidestep(state, npc_idx, x, y, tx, ty).await;
    }
}

// ── Movement: zigzag chase/flee ─────────────────────────────────────────

/// 35% of the time, sidesteps perpendicular to the direct heading instead of
/// taking it — reads as a player dodging/repositioning instead of a monster
/// beelining straight at (or away from) its target.
async fn sidestep(
    state: &mut GameState,
    npc_idx: usize,
    x: i32,
    y: i32,
    tx: i32,
    ty: i32,
) {
    let base = super::chase_heading(x, y, tx, ty);
    let clockwise = rand_range(0, 1) == 0;
    let first = rotate_90(base, clockwise);
    let second = rotate_90(base, !clockwise);

    for heading in [first, second] {
        let (moved, ghost) = crate::game::handlers::move_npc(state, npc_idx, heading);
        if let Some(gp) = ghost {
            super::send_ghost_push(state, gp).await;
        }
        if moved {
            super::send_npc_move(state, npc_idx).await;
            return;
        }
    }
}

async fn move_toward(
    state: &mut GameState,
    npc_idx: usize,
    x: i32,
    y: i32,
    tx: i32,
    ty: i32,
    approach: bool,
) {
    let main_heading = if approach {
        super::chase_heading(x, y, tx, ty)
    } else {
        flee_heading(x, y, tx, ty)
    };
    let heading = if rand_range(1, 100) <= 35 {
        rotate_90(main_heading, rand_range(0, 1) == 0)
    } else {
        main_heading
    };

    let (moved, ghost) = crate::game::handlers::move_npc(state, npc_idx, heading);
    if let Some(gp) = ghost {
        super::send_ghost_push(state, gp).await;
    }
    if moved {
        super::send_npc_move(state, npc_idx).await;
        return;
    }
    if heading != main_heading {
        // Sidestep blocked (wall/edge) — fall back to the direct heading so the
        // bot doesn't just stall against an obstacle.
        let (moved2, ghost2) = crate::game::handlers::move_npc(state, npc_idx, main_heading);
        if let Some(gp) = ghost2 {
            super::send_ghost_push(state, gp).await;
        }
        if moved2 {
            super::send_npc_move(state, npc_idx).await;
        }
    }
}

fn flee_heading(x: i32, y: i32, tx: i32, ty: i32) -> i32 {
    let dx = x - tx;
    let dy = y - ty;
    if dx.abs() >= dy.abs() {
        if dx >= 0 { world::HEADING_EAST } else { world::HEADING_WEST }
    } else if dy >= 0 {
        world::HEADING_SOUTH
    } else {
        world::HEADING_NORTH
    }
}

fn rotate_90(heading: i32, clockwise: bool) -> i32 {
    match (heading, clockwise) {
        (h, true) if h == world::HEADING_NORTH => world::HEADING_EAST,
        (h, true) if h == world::HEADING_EAST => world::HEADING_SOUTH,
        (h, true) if h == world::HEADING_SOUTH => world::HEADING_WEST,
        (h, true) if h == world::HEADING_WEST => world::HEADING_NORTH,
        (h, false) if h == world::HEADING_NORTH => world::HEADING_WEST,
        (h, false) if h == world::HEADING_WEST => world::HEADING_SOUTH,
        (h, false) if h == world::HEADING_SOUTH => world::HEADING_EAST,
        (h, false) if h == world::HEADING_EAST => world::HEADING_NORTH,
        (h, _) => h,
    }
}

// ── Per-bot attack cooldowns (player-like pacing, see BotCooldowns) ─────

fn golpe_ready(state: &GameState, idx: usize) -> bool {
    state
        .bot_cooldowns
        .get(&idx)
        .map(|c| c.next_golpe <= state.game_tick_count)
        .unwrap_or(true)
}
fn set_golpe_cd(state: &mut GameState, idx: usize) {
    let next = state.game_tick_count + GOLPE_CD_TICKS;
    state.bot_cooldowns.entry(idx).or_default().next_golpe = next;
}

fn flecha_ready(state: &GameState, idx: usize) -> bool {
    state
        .bot_cooldowns
        .get(&idx)
        .map(|c| c.next_flecha <= state.game_tick_count)
        .unwrap_or(true)
}
fn set_flecha_cd(state: &mut GameState, idx: usize) {
    let next = state.game_tick_count + FLECHA_CD_TICKS;
    state.bot_cooldowns.entry(idx).or_default().next_flecha = next;
}

fn hechizo_ready(state: &GameState, idx: usize) -> bool {
    state
        .bot_cooldowns
        .get(&idx)
        .map(|c| c.next_hechizo <= state.game_tick_count)
        .unwrap_or(true)
}
fn set_hechizo_cd(state: &mut GameState, idx: usize) {
    let next = state.game_tick_count + HECHIZO_CD_TICKS;
    state.bot_cooldowns.entry(idx).or_default().next_hechizo = next;
}

fn control_ready(state: &GameState, idx: usize) -> bool {
    state
        .bot_cooldowns
        .get(&idx)
        .map(|c| c.next_control <= state.game_tick_count)
        .unwrap_or(true)
}
fn set_control_cd(state: &mut GameState, idx: usize) {
    let next = state.game_tick_count + CONTROL_CD_TICKS;
    state.bot_cooldowns.entry(idx).or_default().next_control = next;
}
