//! Spell casting and effect handlers.
//! Extracted from mod.rs to reduce file size.
//!
//! Sub-modules:
//!   spell_offensive — damage/status helpers, InfoHechizo, NPC/user property/status effects
//!   spell_support   — buffs, invocation, summon, teleport, mimicry, lookat helpers

mod spell_offensive;
mod spell_support;

pub(super) use spell_offensive::*;
pub(super) use spell_support::*;

use crate::net::ConnectionId;
use crate::game::class_race::PlayerClass;
use crate::game::types::{GameState, SendTarget, MAX_SPELL_SLOTS};
use crate::protocol::{font_index, binary_packets};
use super::common::*;
use super::{user_die, npc_die, check_user_level, revive_user};

// =====================================================================
// Spell handler
// =====================================================================

/// LH<slot>,<target_x>,<target_y> — Cast spell.
/// LH<slot> — Select spell to cast (VB6: flags.Hechizo = slot).
/// Does NOT cast the spell — the cast happens on the next RC (right-click) that targets a tile.
pub(super) async fn handle_cast_spell(state: &mut GameState, conn_id: ConnectionId, spell_slot: usize) {
    if spell_slot < 1 || spell_slot > MAX_SPELL_SLOTS { return; }

    if let Some(user) = state.users.get_mut(&conn_id) {
        if user.logged {
            user.pending_spell = spell_slot;
        }
    }
}

/// Actually cast the pending spell at the target coordinates.
/// Called from handle_right_click when pending_spell > 0.
///
/// VB6 flow (modHechizos.bas LanzarHechizo):
///   1. Basic checks (dead, paralyzed, weapon, mana, skill)
///   2. Target validation by TargetType — if invalid → message, EXIT, NO mana consumed
///   3. HandleHechizo → specific validations (self-attack, PuedeAtacar)
///   4. InfoHechizo (FX + messages) — only if all checks pass
///   5. Consume mana — only if spell succeeded (b = True)
pub(super) async fn do_cast_spell(state: &mut GameState, conn_id: ConnectionId) {
    let user_data = match state.users.get(&conn_id) {
        Some(u) if u.logged => {
            let slot = u.pending_spell;
            if slot < 1 || slot > MAX_SPELL_SLOTS { return; }
            let spell_id = u.spells[slot - 1];
            (
                u.pos_map, u.pos_x, u.pos_y, u.char_index,
                u.dead, u.min_mana,
                spell_id, u.level, u.target_x, u.target_y, u.target_map,
                u.target_user, u.target_npc as usize, u.privileges,
            )
        }
        _ => return,
    };
    let (map, x, y, char_index, dead, min_mana,
         spell_id, _level, target_x, target_y, _target_map,
         target_user_conn, target_npc_idx, privileges) = user_data;

    // VB6: PuedeLanzar does NOT check paralysis — paralyzed users CAN cast spells
    if dead || spell_id == 0 {
        return;
    }

    // VB6: LanzarHechizo range check — target must be within visible area (8x6 tiles)
    // Self-target spells skip this check (TargetType::Self_ uses caster's own position)
    const RANGO_VISION_X: i32 = 8;
    const RANGO_VISION_Y: i32 = 6;
    if (target_x - x).abs() > RANGO_VISION_X || (target_y - y).abs() > RANGO_VISION_Y {
        state.send_console(conn_id, "Estás muy lejos para lanzar ese hechizo.", font_index::FIGHT);
        return;
    }

    // Clear pending spell immediately
    if let Some(user) = state.users.get_mut(&conn_id) {
        user.pending_spell = 0;
    }

    // Look up spell data
    let spell = match state.get_spell(spell_id) {
        Some(s) => s.clone(),
        None => return,
    };

    // ===== STEP 1: PuedeLanzar — weapon, mana, skill checks (VB6 lines 269-309) =====

    // VB6 modHechizos.bas:614-617: must have weapon/staff equipped to cast spells
    let weapon_equipped = state.users.get(&conn_id)
        .map(|u| u.equip.weapon > 0)
        .unwrap_or(false);
    if !weapon_equipped {
        state.send_msg_id(conn_id, 26, ""); // "Necesitas un arma mágica para lanzar hechizos"
        return;
    }

    // VB6: PuedeLanzar — GMs (privileges > 0) skip mana/stamina checks entirely
    // modHechizos.bas: "If UserList(UserIndex).flags.Privilegios = 0 Then"
    if privileges == 0 {
        if min_mana < spell.mana_requerido {
            state.send_msg_id(conn_id, 18, ""); // Not enough mana
            return;
        }
        // VB6: Stamina check (modHechizos.bas lines 468-475)
        if spell.sta_requerido > 0 {
            let sta = state.users.get(&conn_id).map(|u| u.min_sta).unwrap_or(0);
            if sta < spell.sta_requerido {
                state.send_msg_id(conn_id, 18, ""); // Not enough stamina
                return;
            }
        }
    } // end privileges == 0 mana/stamina gate
    if spell.min_skill > 0 {
        let magic_skill = state.users.get(&conn_id).map(|u| u.skills[5]).unwrap_or(0);
        if magic_skill < spell.min_skill {
            state.send_msg_id(conn_id, 834, ""); // Magic skill too low
            return;
        }
    }
    // VB6: Staff power check for Mages (modHechizos.bas lines 449-460)
    if spell.need_staff > 0 {
        let (class, weapon_slot, obj_idx) = match state.users.get(&conn_id) {
            Some(u) => {
                let slot = u.equip.weapon;
                let idx = if slot > 0 && slot <= u.inventory.len() {
                    u.inventory[slot - 1].obj_index
                } else {
                    0
                };
                (u.class, slot, idx)
            }
            None => (PlayerClass::default(), 0, 0),
        };
        if class == PlayerClass::Mago {
            let staff_power = if weapon_slot > 0 {
                state.game_data.objects.get(obj_idx as usize).map(|o| o.staff_power).unwrap_or(0)
            } else { 0 };
            if staff_power < spell.need_staff {
                state.send_msg_id(conn_id, 835, ""); // Staff too weak
                return;
            }
        }
    }

    // ===== STEP 2: Resolve targets from world grid =====
    let target_conn: Option<ConnectionId> = if target_user_conn != 0 {
        Some(target_user_conn)
    } else {
        // Check tile and tile+1 for user (VB6 LookatTile checks Y and Y+1)
        state.world.grid(map)
            .and_then(|g| {
                g.tile(target_x, target_y).and_then(|t| t.user_conn)
                    .or_else(|| g.tile(target_x, target_y + 1).and_then(|t| t.user_conn))
            })
    };
    let target_npc = if target_npc_idx > 0 {
        Some(target_npc_idx)
    } else {
        // Check tile for NPC
        state.npc_at_tile(map, target_x, target_y)
            .or_else(|| state.npc_at_tile(map, target_x, target_y + 1))
    };

    let has_user_target = target_conn.is_some();
    let has_npc_target = target_npc.is_some();

    // ===== STEP 3: Target validation by TargetType (VB6 lines 632-695) =====
    // VB6: Select Case Hechizos(uh).Target
    use crate::data::spells::TargetType;
    match spell.target {
        TargetType::UserOnly => {
            // Needs a user target
            if !has_user_target {
                state.send_msg_id(conn_id, 25, ""); // No valid user target
                return;
            }
        }
        TargetType::NpcOnly => {
            // Needs an NPC target
            if !has_npc_target {
                state.send_msg_id(conn_id, 29, ""); // No valid NPC target
                return;
            }
        }
        TargetType::UserAndNpc => {
            // Needs either user or NPC
            if !has_user_target && !has_npc_target {
                state.send_msg_id(conn_id, 25, ""); // No valid target
                return;
            }
        }
        TargetType::Self_ => {
            // Self-only — VB6: TargetUser must equal userindex
            // Force target to self (ignore what was clicked)
        }
        TargetType::Terrain | TargetType::Unknown => {
            // Terrain spells (invocations, teleport) — no target needed
        }
    }

    // Determine if spell is offensive (VB6: PuedeAtacar check)
    let is_offensive = spell.sube_hp == 2 || spell.sube_ham == 2 || spell.sube_sed == 2
        || spell.paraliza || spell.inmoviliza
        || spell.envenena || spell.maldicion;

    // ===== STEP 4: Safe zone check for offensive spells (Trigger > Zone > Map) =====
    if is_offensive {
        if is_safe_at(state, map, x, y) {
            state.send_msg_id(conn_id, 164, "");
            return;
        }
    }

    // Route to NPC or User handling
    if has_npc_target && !has_user_target {
        // ===== NPC TARGET =====
        let npc_idx = target_npc.unwrap();

        // VB6: PuedeAtacarNPC — validate DAMAGE spells on NPC targets.
        // Status spells (paralizar, inmovilizar, envenenar) bypass this check
        // because VB6 HandleHechizoEstadoNPC does NOT call PuedeAtacarNPC.
        let is_damage_spell = spell.sube_hp == 2;
        if is_damage_spell {
            if !super::npcs::puede_atacar_npc(state, conn_id, npc_idx).await {
                return;
            }
        }

        // VB6: InfoHechizo — FX + messages (sent BEFORE mana consumption)
        send_spell_info_npc(state, conn_id, npc_idx, &spell, char_index).await;

        // Apply effect
        match spell.tipo {
            crate::data::spells::SpellType::Properties => {
                apply_spell_properties_npc(state, conn_id, npc_idx, &spell).await;
            }
            crate::data::spells::SpellType::Status => {
                apply_spell_status_npc(state, conn_id, npc_idx, &spell).await;
            }
            _ => {}
        }

        // VB6: Mimetiza on NPC — Druid only, copies NPC appearance onto caster
        if spell.mimetiza {
            let is_druid = state.users.get(&conn_id)
                .map(|u| u.class == PlayerClass::Druida)
                .unwrap_or(false);
            if is_druid {
                apply_mimetiza_npc(state, conn_id, npc_idx).await;
            }
        }

        // Consume mana (VB6: only if b=True, after HandleHechizoNPC)
        consume_spell_mana(state, conn_id, &spell, privileges).await;
    } else if has_user_target {
        // ===== USER TARGET =====
        let target_id = target_conn.unwrap();

        // VB6: Self-attack check (HechizoEstadoUsuario line 725, HechizoPropUsuario line 1425)
        if is_offensive && target_id == conn_id {
            state.send_msg_id(conn_id, 31, ""); // Can't attack yourself
            return; // NO mana consumed, NO FX
        }

        // Zone-aware safe check — victim in safe zone blocks offensive spells
        if is_offensive && target_id != conn_id {
            let victim_pos = state.users.get(&target_id).map(|u| (u.pos_x, u.pos_y)).unwrap_or((0, 0));
            if is_safe_at(state, map, victim_pos.0, victim_pos.1) {
                state.send_msg_id(conn_id, 164, "");
                return;
            }
        }

        // VB6: Can't cast offensive spells on dead users or administrators
        if is_offensive && target_id != conn_id {
            let (t_dead, t_privs) = state.users.get(&target_id)
                .map(|u| (u.dead, u.privileges))
                .unwrap_or((false, 0));
            if t_dead {
                return;
            }
            if t_privs > 0 {
                state.send_msg_id(conn_id, 155, "");
                return;
            }
            // Clan safe check — can't cast offensive spells on clanmates if EITHER party has seguro_clan on
            let caster_seguro = state.users.get(&conn_id).map(|u| u.seguro_clan).unwrap_or(false);
            let target_seguro = state.users.get(&target_id).map(|u| u.seguro_clan).unwrap_or(false);
            if (caster_seguro || target_seguro) && same_clan(state, conn_id, target_id) {
                state.send_console(conn_id, "No puedes atacar a un miembro de tu clan. Usa /SEGUROCLAN para desactivar el seguro.", font_index::INFO);
                return;
            }
        }

        // VB6: Healing full HP check (||145)
        if spell.sube_hp == 1 {
            let full_hp = state.users.get(&target_id)
                .map(|u| u.min_hp >= u.max_hp).unwrap_or(false);
            if full_hp {
                state.send_msg_id(conn_id, 145, "");
                return;
            }
        }

        // VB6: RemoverParalisis only works if target IS paralyzed (modHechizos.bas:766-802)
        // If target is not paralyzed, b stays False → no mana consumed, no FX
        if spell.remover_paralisis {
            let is_paralyzed = state.users.get(&target_id)
                .map(|u| u.paralyzed).unwrap_or(false);
            if !is_paralyzed {
                return;
            }
        }

        // VB6: InfoHechizo — FX + messages (sent BEFORE mana consumption)
        send_spell_info_user(state, conn_id, target_id, &spell, char_index).await;

        // Apply effects
        match spell.tipo {
            crate::data::spells::SpellType::Properties => {
                apply_spell_properties(state, conn_id, target_id, &spell).await;
                apply_spell_buffs(state, conn_id, target_id, &spell).await;
            }
            crate::data::spells::SpellType::Status => {
                apply_spell_status(state, conn_id, target_id, &spell).await;
                apply_spell_buffs(state, conn_id, target_id, &spell).await;
            }
            _ => {}
        }

        // VB6: Mimetiza — copy target user's appearance onto caster
        if spell.mimetiza && target_id != conn_id {
            apply_mimetiza_user(state, conn_id, target_id).await;
        }

        // Consume mana
        consume_spell_mana(state, conn_id, &spell, privileges).await;
    } else {
        // ===== SELF / TERRAIN (no external target) =====
        match spell.target {
            TargetType::Self_ => {
                // Self-only spell — beneficial only
                // VB6: RemoverParalisis on self also requires being paralyzed
                if spell.remover_paralisis {
                    let is_paralyzed = state.users.get(&conn_id)
                        .map(|u| u.paralyzed).unwrap_or(false);
                    if !is_paralyzed {
                        return;
                    }
                }
                send_spell_info_user(state, conn_id, conn_id, &spell, char_index).await;
                match spell.tipo {
                    crate::data::spells::SpellType::Properties => {
                        apply_spell_properties(state, conn_id, conn_id, &spell).await;
                        apply_spell_buffs(state, conn_id, conn_id, &spell).await;
                    }
                    crate::data::spells::SpellType::Status => {
                        apply_spell_status(state, conn_id, conn_id, &spell).await;
                        apply_spell_buffs(state, conn_id, conn_id, &spell).await;
                    }
                    _ => {}
                }
                consume_spell_mana(state, conn_id, &spell, privileges).await;
            }
            TargetType::Terrain | TargetType::Unknown => {
                // Terrain spells (invocation, summon, teleport)
                match spell.tipo {
                    crate::data::spells::SpellType::Invocation => {
                        apply_spell_invocation(state, conn_id, &spell).await;
                    }
                    crate::data::spells::SpellType::SummonPet => {
                        apply_spell_summon_pet(state, conn_id, &spell).await;
                    }
                    crate::data::spells::SpellType::Teleport => {
                        apply_spell_teleport(state, conn_id, &spell).await;
                    }
                    _ => {}
                }
                consume_spell_mana(state, conn_id, &spell, privileges).await;
            }
            _ => {
                // Should have been caught by Step 3 target validation
                return;
            }
        }
    }
}

/// Consume mana and stamina after a successful spell cast.
/// VB6: Only consumed if b=True (spell succeeded), and only for normal users (not GMs).
/// VB6: Druid with Flauta Élfica gets mana discounts (modHechizos.bas lines 733-752).
pub(super) async fn consume_spell_mana(state: &mut GameState, conn_id: ConnectionId,
                             spell: &crate::data::spells::SpellData, privileges: i32) {
    if privileges == 0 {
        let mut mana_cost = spell.mana_requerido;

        // VB6: Druid mana bonuses with Flauta Élfica
        if let Some(user) = state.users.get(&conn_id) {
            if user.class == PlayerClass::Druida {
                let ring_slot = user.equip.ring;
                let ring_obj = if ring_slot > 0 && ring_slot <= user.inventory.len() {
                    user.inventory[ring_slot - 1].obj_index
                } else { 0 };
                if ring_obj == spell_offensive::FLAUTAELFICA {
                    if spell.mimetiza {
                        // VB6: Mimicry: 50% less mana
                        mana_cost = (mana_cost as f64 * 0.5) as i32;
                    } else if spell.tipo == crate::data::spells::SpellType::Invocation {
                        // VB6: Invocation spells: 30% less mana (mana * 0.7)
                        mana_cost = (mana_cost as f64 * 0.7) as i32;
                    } else if spell.index as i32 != spell_support::APOCALIPSIS_SPELL_INDEX {
                        // VB6: Other spells (except Apocalypse): 10% less mana
                        mana_cost = (mana_cost as f64 * 0.9) as i32;
                    }
                }
            }
        }

        if let Some(user) = state.users.get_mut(&conn_id) {
            user.min_mana = (user.min_mana - mana_cost).max(0);
            user.min_sta = (user.min_sta - spell.sta_requerido).max(0);
        }
    }
    send_stats_mana(state, conn_id).await;
    send_stats_sta(state, conn_id).await;
}
