//! `/BOT <clase>` — GM duel-practice bot: spawn, gear/spell curation, cleanup.
//!
//! Spawns a max-stat, best-geared `NpcState` (not a fake `UserState`) with
//! player-like appearance, fighting via `handlers::ticks::bot_ai::tick_bot_ai`
//! instead of the normal (slower) monster AI. See the approved implementation
//! plan for the full design rationale.

use crate::data::objects::{ObjData, ObjType};
use crate::game::class_race::PlayerClass;
use crate::game::npc::{self, BotCooldowns};
use crate::game::types::{GameState, SendTarget, privilege_level};
use crate::net::ConnectionId;
use crate::protocol::{binary_packets, font_index};

/// Any humanoid NPC number used purely as `spawn_npc` scaffolding — body/head/
/// stats/appearance are all overwritten immediately after, so the specific
/// template doesn't matter beyond "exists and is humanoid". Reuses the
/// Praetoriano Guerrero slot, which is always present in NPCs.dat.
const BOT_TEMPLATE_NPC: usize = 902;

/// `/BOT Paladin|Mago|Cazador` — spawn a duel bot at the GM's position.
pub(super) async fn handle_slash_bot(state: &mut GameState, conn_id: ConnectionId, args: &str) {
    let (map, x, y) = match state.users.get(&conn_id) {
        Some(u) if u.logged && u.privileges >= privilege_level::DIOS => {
            (u.pos_map, u.pos_x, u.pos_y)
        }
        _ => return,
    };

    let class = match PlayerClass::from_str_opt(args.trim()) {
        Some(c @ (PlayerClass::Paladin | PlayerClass::Mago | PlayerClass::Cazador)) => c,
        _ => {
            state.send_console(conn_id, "Uso: /BOT Paladin|Mago|Cazador", font_index::INFO);
            return;
        }
    };

    match spawn_bot_npc(state, class, map, x, y) {
        Some(npc_idx) => {
            state.active_bot_indices.insert(npc_idx);
            state
                .bot_cooldowns
                .insert(npc_idx, BotCooldowns::default());
            state.bot_classes.insert(npc_idx, class);
            if let Some(npc) = state.get_npc(npc_idx) {
                let cc_pkt = npc.build_cc_binary();
                state.send_data_bytes(SendTarget::ToArea { map, x, y }, &cc_pkt);
            }
            state.send_console(
                conn_id,
                &format!(
                    "Bot {} creado. Recorda que en zona segura nadie puede pelear.",
                    class
                ),
                font_index::INFO,
            );
        }
        None => {
            state.send_console(conn_id, "No se pudo crear el bot.", font_index::INFO);
        }
    }
}

/// `/BOT CLEAR` — remove all active duel bots without having to kill them.
pub(super) async fn handle_slash_bot_clear(state: &mut GameState, conn_id: ConnectionId) {
    let is_gm = state
        .users
        .get(&conn_id)
        .map(|u| u.logged && u.privileges >= privilege_level::DIOS)
        .unwrap_or(false);
    if !is_gm {
        return;
    }

    let indices: Vec<usize> = state.active_bot_indices.drain().collect();
    let count = indices.len();
    for idx in indices {
        if let Some(npc) = state.get_npc(idx) {
            let (map, x, y, char_index) = (npc.map, npc.x, npc.y, npc.char_index);
            let bp = binary_packets::write_character_remove(char_index.0 as i16);
            state.send_data_bytes(SendTarget::ToArea { map, x, y }, &bp);
        }
        // respawn=false (set at spawn) → frees the char_index permanently, no respawn.
        state.kill_npc(idx);
        state.bot_cooldowns.remove(&idx);
        state.bot_classes.remove(&idx);
    }
    state.send_console(
        conn_id,
        &format!("{} bot(s) eliminados.", count),
        font_index::INFO,
    );
}

fn spawn_bot_npc(
    state: &mut GameState,
    class: PlayerClass,
    map: i32,
    x: i32,
    y: i32,
) -> Option<usize> {
    let npc_idx = state.spawn_npc(BOT_TEMPLATE_NPC, map, x, y)?;

    let gear = compute_bot_gear(state, class);
    let spells = bot_spell_list(state, class);

    // Real level-50 HP/Hit — replays the exact same level-up curve a live
    // character would go through (leveling::simulate_class_level50_stats),
    // instead of an arbitrary flat number. CON=20 is a solid-but-not-maxed
    // roll (attributes cap at 25); INT doesn't affect the result (mana isn't
    // tracked on NpcState — bots cast for free, see npc_cast_spell).
    let (max_hp, base_max_hit, base_min_hit) = super::leveling::simulate_class_level50_stats(
        class,
        crate::game::class_race::PlayerRace::Humano,
        20,
        20,
        &state.game_data.balance,
    );

    // def/poder_ataque/poder_evasion have no leveling-curve equivalent — real
    // players get these dynamically from skills+equipped gear at combat time
    // (calc_attack_power_with_balance/poder_evasion), not a stored number, so
    // NpcState's static fields are hand-tuned per class instead.
    let (def, def_m, poder_ataque, poder_evasion) = match class {
        PlayerClass::Paladin => (30, 10, 100, 100),
        PlayerClass::Mago => (10, 30, 100, 90),
        PlayerClass::Cazador => (20, 20, 100, 95),
        _ => (15, 15, 90, 90),
    };

    if let Some(npc) = state.get_npc_mut(npc_idx) {
        npc.movement = npc::AI_BOT_DUEL;
        npc.hostile = true;
        npc.attackable = true;
        npc.respawn = false;
        npc.name = format!("Bot {}", class);
        npc.desc = "Bot de entrenamiento GM (/BOT)".to_string();

        // Player-like appearance (Humano, hombre) instead of the Praetoriano
        // template's soldier look. NpcState has no separate "armor" appearance
        // slot (only weapon/shield/casco anim) — armor's def bonus still
        // applies numerically even though the body sprite stays plain.
        npc.body = 1;
        npc.head = 1;
        npc.weapon_anim = gear.weapon_anim;
        npc.shield_anim = gear.shield_anim;
        npc.casco_anim = gear.casco_anim;

        npc.max_hp = max_hp;
        npc.min_hp = max_hp;
        npc.min_hit = (base_min_hit + gear.hit_bonus_min).max(1);
        npc.max_hit = (base_max_hit + gear.hit_bonus_max).max(1);
        npc.def = def + gear.def_bonus;
        npc.def_m = def_m + gear.def_m_bonus;
        npc.poder_ataque = poder_ataque;
        npc.poder_evasion = poder_evasion;

        npc.lanza_spells = if spells.is_empty() { 0 } else { 100 };
        npc.spells = spells;

        npc.maestro_user = None; // not a pet — AI_FOLLOW_OWNER/inactivity timers don't apply
    }

    Some(npc_idx)
}

/// Equipment computed at spawn time from `obj.dat`, since no "best gear per
/// class" data exists anywhere. Cazador gets a bow+arrow (ranged); Paladin/
/// Mago get a melee weapon/staff + shield, all classes get armor+helmet.
pub(super) struct BotGear {
    pub weapon_anim: i32,
    pub shield_anim: i32,
    pub casco_anim: i32,
    pub hit_bonus_min: i32,
    pub hit_bonus_max: i32,
    pub def_bonus: i32,
    pub def_m_bonus: i32,
    /// GRH of the best arrow found (Cazador only) — used for the arrow visual
    /// in `npc_ranged_attack_user`. 0 for non-Cazador classes.
    pub arrow_grh: i32,
}

/// Exposed so `ticks::bot_ai` can re-derive the arrow GRH for the ranged-attack
/// visual without duplicating the obj.dat scan logic.
pub(super) fn compute_bot_gear(state: &GameState, class: PlayerClass) -> BotGear {
    let class_name = class.to_string();
    let allowed =
        |o: &ObjData| !o.class_prohibida.iter().any(|c| c.eq_ignore_ascii_case(&class_name));

    let mut gear = BotGear {
        weapon_anim: 0,
        shield_anim: 0,
        casco_anim: 0,
        hit_bonus_min: 0,
        hit_bonus_max: 0,
        def_bonus: 0,
        def_m_bonus: 0,
        arrow_grh: 0,
    };

    if class == PlayerClass::Cazador {
        let bow = state
            .game_data
            .objects
            .iter()
            .filter(|o| o.obj_type == ObjType::Weapon && o.proyectil && allowed(o))
            .max_by_key(|o| o.max_hit);
        if let Some(b) = bow {
            gear.weapon_anim = b.weapon_anim;
        }

        let arrow = state
            .game_data
            .objects
            .iter()
            .filter(|o| o.obj_type == ObjType::Arrow && allowed(o))
            .max_by_key(|o| o.max_hit);
        if let Some(a) = arrow {
            gear.hit_bonus_min += a.min_hit;
            gear.hit_bonus_max += a.max_hit;
            gear.arrow_grh = a.grh_index;
        }
    } else {
        let weapon = state
            .game_data
            .objects
            .iter()
            .filter(|o| o.obj_type == ObjType::Weapon && !o.proyectil && allowed(o))
            // staff_power/staff_damage_bonus favor a Mago's bordón over a plain sword.
            .max_by_key(|o| o.max_hit + o.staff_power + o.staff_damage_bonus);
        if let Some(w) = weapon {
            gear.weapon_anim = w.weapon_anim;
            gear.hit_bonus_min += w.min_hit;
            gear.hit_bonus_max += w.max_hit;
        }

        let shield = state
            .game_data
            .objects
            .iter()
            .filter(|o| o.obj_type == ObjType::Shield && allowed(o))
            .max_by_key(|o| o.max_def);
        if let Some(s) = shield {
            gear.shield_anim = s.shield_anim;
            gear.def_bonus += s.max_def;
        }
    }

    let armor = state
        .game_data
        .objects
        .iter()
        .filter(|o| o.obj_type == ObjType::Armor && allowed(o))
        .max_by_key(|o| o.max_def);
    if let Some(a) = armor {
        gear.def_bonus += a.max_def;
    }

    let helmet = state
        .game_data
        .objects
        .iter()
        .filter(|o| o.obj_type == ObjType::Helmet && allowed(o))
        .max_by_key(|o| o.max_def + o.defensa_magica_max);
    if let Some(h) = helmet {
        gear.casco_anim = h.casco_anim;
        gear.def_bonus += h.max_def;
        gear.def_m_bonus += h.defensa_magica_max;
    }

    gear
}

/// Resolve a curated per-class spell list by name (robust to `Hechizos.dat`
/// reordering). No class-restriction field exists on `SpellData` — this list
/// is a design choice, not derived from data. Cazador gets none (pure archer).
fn bot_spell_list(state: &GameState, class: PlayerClass) -> Vec<i32> {
    let names: &[&str] = match class {
        PlayerClass::Paladin => &[
            "Inmovilizar",
            "Remover Parálisis",
            "Curar Heridas Graves",
            "Fuego Divino",
        ],
        PlayerClass::Mago => &[
            "Paralizar",
            "Remover Parálisis",
            "Inmovilizar",
            "Apocalípsis",
            "Descarga Eléctrica",
            "Destello Mágico",
            "Flecha Mágica",
            "Fuego Mágico",
            "Relampago",
            "Tormenta de Fuego",
            "Inferno",
        ],
        _ => &[],
    };

    names
        .iter()
        .filter_map(|n| {
            state
                .game_data
                .spells
                .iter()
                .find(|s| s.nombre.eq_ignore_ascii_case(n))
        })
        .map(|s| s.index as i32)
        .collect()
}

/// Look up a spell's index by exact/curated name — used by `bot_ai.rs` to
/// pick a specific action (e.g. "Remover Parálisis", "Inmovilizar") out of a
/// bot's already-granted spell list without re-scanning `Hechizos.dat` names
/// every AI tick.
pub(super) fn find_bot_spell(state: &GameState, name: &str) -> Option<i32> {
    state
        .game_data
        .spells
        .iter()
        .find(|s| s.nombre.eq_ignore_ascii_case(name))
        .map(|s| s.index as i32)
}
