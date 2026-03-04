//! Spell casting and effect handlers.
//! Extracted from mod.rs to reduce file size.

use tracing::info;
use crate::net::ConnectionId;
use crate::game::types::{GameState, SendTarget, MAX_SPELL_SLOTS};
use crate::game::world;
use crate::protocol::{font_index, fields::read_field, binary_packets};
use crate::data::objects::ObjType;
use crate::data::experience::MAX_LEVEL;
use super::common::*;
use super::{user_die, npc_die, check_user_level, revive_user, warp_user};

// =====================================================================
// Spell handler
// =====================================================================

/// LH<slot>,<target_x>,<target_y> — Cast spell.
/// LH<slot> — Select spell to cast (VB6: flags.Hechizo = slot).
/// Does NOT cast the spell — the cast happens on the next RC (right-click) that targets a tile.
pub(super) async fn handle_cast_spell(state: &mut GameState, conn_id: ConnectionId, data: &str) {
    let payload = strip_opcode(data, 2);
    let spell_slot: usize = match payload.parse::<usize>() {
        Ok(s) if s >= 1 && s <= MAX_SPELL_SLOTS => s,
        _ => return,
    };

    if let Some(user) = state.users.get_mut(&conn_id) {
        if user.logged {
            user.pending_spell = spell_slot;
        }
    }
}

/// Send "Ves a <name>" info for a user target (VB6: LookatTile user display during spell cast).
/// Replicates the exact same format as the LC handler.
pub(super) async fn send_lookat_user_info(state: &mut GameState, conn_id: ConnectionId, target_conn: ConnectionId) {
    let info = state.users.get(&target_conn).map(|t| {
        (t.char_name.clone(), t.level, t.dead, t.criminal, t.min_hp, t.max_hp,
         t.privileges, t.armada_real, t.fuerzas_caos, t.guild_index, t.desc.clone())
    });
    let my_priv = state.users.get(&conn_id).map(|u| u.privileges).unwrap_or(0);
    if let Some((name, level, dead, criminal, min_hp, max_hp, priv_target, armada, caos, guild_idx, desc)) = info {
        let mut stat = String::new();
        let limite_newbie = 9;
        if level <= limite_newbie { stat.push_str(" <NEWBIE>"); }
        if guild_idx > 0 {
            if let Some(gn) = state.get_guild_name(guild_idx) {
                stat.push_str(&format!(" <{}>", gn));
            }
        }
        stat = format!("Ves a {}{}", name, stat);
        if my_priv > 0 {
            let ci = state.users.get(&target_conn).map(|u| u.char_index.0).unwrap_or(0);
            stat.push_str(&format!(" <UI:{}> ({}/{})", ci, min_hp, max_hp));
        }
        if armada { stat.push_str(" <Alianza Imperial>"); }
        else if caos { stat.push_str(" <Horda Infernal>"); }
        if desc.len() > 1 { stat.push_str(&format!(" - {}", desc)); }
        // Health status (separate from faction)
        if priv_target >= crate::game::types::privilege_level::ADMINISTRADOR {
            stat.push_str(" [Creator]");
        } else if priv_target > 0 {
            stat.push_str(" [Inmortal]");
        } else if dead {
            stat.push_str(" [Muerto]");
        } else if min_hp < ((max_hp as f64 * 0.2) as i32) {
            stat.push_str(" [Agonizando]");
        } else if min_hp < ((max_hp as f64 * 0.45) as i32) {
            stat.push_str(" [Gravemente herido]");
        } else if min_hp < ((max_hp as f64 * 0.75) as i32) {
            stat.push_str(" [Medio herido]");
        } else if min_hp < max_hp {
            stat.push_str(" [Algo lastimado]");
        } else {
            stat.push_str(" [Intacto]");
        }
        // Label + font_index by privilege/faction (VB6 LookatTile lines 895-956)
        let fi = if priv_target > 11 {
            stat.push_str(" <Administrador>");
            font_index::BLANCO           // white
        } else if priv_target > 3 {
            stat.push_str(" <Game Master>");
            font_index::CELESTE          // cyan-ish
        } else if priv_target > 0 {
            stat.push_str(" <Game Master>");
            font_index::SERVER           // green
        } else if level <= limite_newbie {
            font_index::NEWBIE           // light yellow-green
        } else if armada {
            font_index::CIUDADANO        // blue
        } else if caos {
            font_index::ROJO             // red
        } else if criminal {
            stat.push_str(" <CRIMINAL>");
            font_index::ROJO             // red
        } else {
            stat.push_str(" <CIUDADANO>");
            font_index::CIUDADANO        // blue
        };

        state.send_console(conn_id, &stat, fi).await;
    }
}

/// Send brief NPC desc info during spell cast (VB6: LookatTile NPC display).
pub(super) async fn send_lookat_npc_info(state: &mut GameState, conn_id: ConnectionId, npc_idx: usize) {
    let npc_data = state.get_npc(npc_idx).map(|n| {
        (n.name.clone(), n.desc.clone(), n.char_index.0, n.min_hp, n.max_hp)
    });
    if let Some((npc_name, npc_desc, _npc_ci, min_hp, max_hp)) = npc_data {
        let is_gm = state.users.get(&conn_id).map(|u| u.privileges >= crate::game::types::privilege_level::SEMIDIOS).unwrap_or(false);
        let mut msg_text = if !npc_desc.is_empty() {
            format!("Ves {} - {}", npc_name, npc_desc)
        } else {
            format!("Ves {}", npc_name)
        };
        if is_gm {
            msg_text.push_str(&format!(" ({}/{})", min_hp, max_hp));
        }
        state.send_console(conn_id, &msg_text, font_index::INFO).await;
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
        state.send_msg_id(conn_id, 26, "").await; // "Necesitas un arma mágica para lanzar hechizos"
        return;
    }

    if min_mana < spell.mana_requerido {
        state.send_msg_id(conn_id, 18, "").await; // Not enough mana
        return;
    }
    if spell.min_skill > 0 {
        let magic_skill = state.users.get(&conn_id).map(|u| u.skills[5]).unwrap_or(0);
        if magic_skill < spell.min_skill {
            state.send_msg_id(conn_id, 834, "").await; // Magic skill too low
            return;
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
                state.send_msg_id(conn_id, 25, "").await; // No valid user target
                return;
            }
        }
        TargetType::NpcOnly => {
            // Needs an NPC target
            if !has_npc_target {
                state.send_msg_id(conn_id, 29, "").await; // No valid NPC target
                return;
            }
        }
        TargetType::UserAndNpc => {
            // Needs either user or NPC
            if !has_user_target && !has_npc_target {
                state.send_msg_id(conn_id, 25, "").await; // No valid target
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

    // Determine if spell is offensive
    let is_offensive = spell.sube_hp == 2 || spell.paraliza || spell.inmoviliza
        || spell.envenena || spell.maldicion;

    // ===== STEP 4: Safe zone check for offensive spells (VB6: PuedeAtacar) =====
    if is_offensive {
        let attacker_trigger = get_map_tile_trigger(state, map, x, y);
        if attacker_trigger == crate::data::maps::Trigger::SafeZone {
            state.send_msg_id(conn_id, 164, "").await; // Can't attack in safe zone
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

        // Consume mana (VB6: only if b=True, after HandleHechizoNPC)
        consume_spell_mana(state, conn_id, &spell, privileges).await;
    } else if has_user_target {
        // ===== USER TARGET =====
        let target_id = target_conn.unwrap();

        // VB6: Self-attack check (HechizoEstadoUsuario line 725, HechizoPropUsuario line 1425)
        if is_offensive && target_id == conn_id {
            state.send_msg_id(conn_id, 31, "").await; // Can't attack yourself
            return; // NO mana consumed, NO FX
        }

        // VB6: Safe zone check — victim in safe zone blocks offensive spells
        if is_offensive && target_id != conn_id {
            let victim_pos = state.users.get(&target_id).map(|u| (u.pos_x, u.pos_y)).unwrap_or((0, 0));
            let victim_trigger = get_map_tile_trigger(state, map, victim_pos.0, victim_pos.1);
            if victim_trigger == crate::data::maps::Trigger::SafeZone {
                state.send_msg_id(conn_id, 164, "").await;
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
                state.send_msg_id(conn_id, 155, "").await;
                return;
            }
        }

        // VB6: Healing full HP check (||145)
        if spell.sube_hp == 1 {
            let full_hp = state.users.get(&target_id)
                .map(|u| u.min_hp >= u.max_hp).unwrap_or(false);
            if full_hp {
                state.send_msg_id(conn_id, 145, "").await;
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
pub(super) async fn consume_spell_mana(state: &mut GameState, conn_id: ConnectionId,
                             spell: &crate::data::spells::SpellData, privileges: i32) {
    if privileges == 0 {
        // Normal user — consume mana and stamina
        if let Some(user) = state.users.get_mut(&conn_id) {
            user.min_mana = (user.min_mana - spell.mana_requerido).max(0);
            user.min_sta = (user.min_sta - spell.sta_requerido).max(0);
        }
    }
    send_stats_mana(state, conn_id).await;
    send_stats_sta(state, conn_id).await;
}

/// VB6: InfoHechizo — Send FX, sound, and chat messages for a spell cast on a USER.
pub(super) async fn send_spell_info_user(state: &mut GameState, caster_id: ConnectionId,
                               target_id: ConnectionId,
                               spell: &crate::data::spells::SpellData,
                               caster_ci: crate::game::world::CharIndex) {
    let caster_name = state.users.get(&caster_id).map(|u| u.char_name.clone()).unwrap_or_default();
    let (map, x, y) = state.users.get(&caster_id)
        .map(|u| (u.pos_map, u.pos_x, u.pos_y)).unwrap_or((0,0,0));

    // Magic words (overhead yellow text)
    if !spell.palabras_magicas.is_empty() {
        state.send_chat_over_head_to(SendTarget::ToArea { map, x, y }, &spell.palabras_magicas, caster_ci.0 as i16, 16776960).await;
    }

    // Target char_index for FX
    let target_ci = state.users.get(&target_id).map(|u| u.char_index.0).unwrap_or(caster_ci.0);
    let (fx_map, fx_x, fx_y) = state.users.get(&target_id)
        .map(|u| (u.pos_map, u.pos_x, u.pos_y)).unwrap_or((map, x, y));

    // FX + Sound
    if spell.fx_grh > 0 {
        let fx_pkt = binary_packets::write_create_fx(target_ci as i16, spell.fx_grh as i16, spell.loops as i16);
        state.send_data_bytes(SendTarget::ToArea { map: fx_map, x: fx_x, y: fx_y }, &fx_pkt).await;
    }
    if spell.wav > 0 {
        let snd_pkt = binary_packets::write_play_wave(spell.wav as u8, fx_x as u8, fx_y as u8);
        state.send_data_bytes(SendTarget::ToArea { map: fx_map, x: fx_x, y: fx_y }, &snd_pkt).await;
    }

    // Console messages (red font)
    if target_id != caster_id {
        let target_name = state.users.get(&target_id).map(|u| u.char_name.clone()).unwrap_or_default();
        if !spell.hechizero_msg.is_empty() {
            state.send_console(caster_id, &format!("{} {}", spell.hechizero_msg, target_name), font_index::FIGHT).await;
        }
        if !spell.target_msg.is_empty() {
            state.send_console(target_id, &format!("{} {}", caster_name, spell.target_msg), font_index::FIGHT).await;
        }
    } else {
        if !spell.propio_msg.is_empty() {
            state.send_console(caster_id, &spell.propio_msg, font_index::FIGHT).await;
        }
    }
}

/// VB6: InfoHechizo — Send FX, sound, and chat messages for a spell cast on an NPC.
pub(super) async fn send_spell_info_npc(state: &mut GameState, caster_id: ConnectionId,
                              npc_idx: usize,
                              spell: &crate::data::spells::SpellData,
                              caster_ci: crate::game::world::CharIndex) {
    let (map, x, y) = state.users.get(&caster_id)
        .map(|u| (u.pos_map, u.pos_x, u.pos_y)).unwrap_or((0,0,0));

    // Magic words (overhead yellow text)
    if !spell.palabras_magicas.is_empty() {
        state.send_chat_over_head_to(SendTarget::ToArea { map, x, y }, &spell.palabras_magicas, caster_ci.0 as i16, 16776960).await;
    }

    // NPC char_index for FX
    let npc_ci = state.get_npc(npc_idx).map(|n| n.char_index.0).unwrap_or(0);
    let (fx_map, fx_x, fx_y) = state.get_npc(npc_idx)
        .map(|n| (n.map, n.x, n.y)).unwrap_or((map, x, y));

    if spell.fx_grh > 0 {
        let fx_pkt = binary_packets::write_create_fx(npc_ci as i16, spell.fx_grh as i16, spell.loops as i16);
        state.send_data_bytes(SendTarget::ToArea { map: fx_map, x: fx_x, y: fx_y }, &fx_pkt).await;
    }
    if spell.wav > 0 {
        let snd_pkt = binary_packets::write_play_wave(spell.wav as u8, fx_x as u8, fx_y as u8);
        state.send_data_bytes(SendTarget::ToArea { map: fx_map, x: fx_x, y: fx_y }, &snd_pkt).await;
    }

    // Console message (red font)
    if !spell.hechizero_msg.is_empty() {
        state.send_console(caster_id, &format!("{} la criatura.", spell.hechizero_msg), font_index::FIGHT).await;
    }
}

/// Apply property-type spell effects to an NPC (damage/heal).
/// VB6: HechizoPropNPC in modHechizos.bas
pub(super) async fn apply_spell_properties_npc(
    state: &mut GameState,
    caster_id: ConnectionId,
    npc_idx: usize,
    spell: &crate::data::spells::SpellData,
) {
    if spell.sube_hp != 2 {
        // Only damage spells (SubeHP=2) apply to NPCs
        // Heal spells on hostile NPCs don't make sense in VB6 either
        return;
    }

    let mut damage = rand_range(spell.min_hp, spell.max_hp);

    // VB6: spell damage * 1.4 on NPCs
    damage = (damage as f64 * 1.4) as i32;

    // Get NPC data for damage number display and exp calculation
    let npc_data = state.get_npc(npc_idx).map(|n| (n.char_index.0, n.map, n.x, n.y, n.give_exp, n.max_hp));
    let (npc_ci, npc_map, npc_x, npc_y, npc_give_exp, npc_max_hp) = match npc_data {
        Some(d) => d,
        None => return,
    };

    // Send damage number over NPC head (VB6: N| vbYellow°-<damage>°<npc_charindex>)
    let caster_map = state.users.get(&caster_id).map(|u| (u.pos_map, u.pos_x, u.pos_y)).unwrap_or((npc_map, npc_x, npc_y));
    state.send_chat_over_head_to(SendTarget::ToArea { map: caster_map.0, x: caster_map.1, y: caster_map.2 }, &format!("-{}", damage), npc_ci as i16, 65535).await;

    // Send damage console message to caster: ||850@<damage>
    state.send_msg_id(caster_id, 850, &format!("{}", damage)).await;

    // Apply damage to NPC
    if let Some(npc) = state.get_npc_mut(npc_idx) {
        npc.min_hp -= damage;
        npc.damage_received.push((caster_id, damage));
    }

    // Per-hit EXP (VB6: CalcularDarExp — proportional exp on every hit)
    if npc_give_exp > 0 && npc_max_hp > 0 {
        let exp_mult = state.multiplicador_exp;
        let mut exp_award = ((npc_give_exp as f64 / npc_max_hp as f64) * damage as f64 * exp_mult as f64) as i64;

        let scroll_mult = state.users.get(&caster_id)
            .map(|u| if u.scroll_active[0] { u.scroll_mult[0] as i64 } else { 1 })
            .unwrap_or(1);
        exp_award *= scroll_mult;

        let can_level = state.users.get(&caster_id)
            .map(|u| u.logged && u.level < MAX_LEVEL as i32)
            .unwrap_or(false);

        if can_level && exp_award > 0 {
            if let Some(user) = state.users.get_mut(&caster_id) {
                user.exp += exp_award;
            }
            state.send_msg_id(caster_id, 170, &format!("{}", exp_award)).await;
            send_stats_exp(state, caster_id).await;
            check_user_level(state, caster_id).await;
        }
    }

    // Check NPC death
    let npc_death_data = state.get_npc(npc_idx).and_then(|n| {
        if n.min_hp < 1 { Some((n.give_exp, n.give_gld_min, n.give_gld_max)) } else { None }
    });
    if let Some((give_exp, give_gld_min, give_gld_max)) = npc_death_data {
        if let Some(npc) = state.get_npc_mut(npc_idx) {
            npc.min_hp = 0;
        }
        npc_die(state, npc_idx, caster_id, give_exp, give_gld_min, give_gld_max).await;
    }
}

/// Apply status-type spell effects to an NPC.
/// VB6: HechizoEstadoNPC in modHechizos.bas
pub(super) async fn apply_spell_status_npc(
    state: &mut GameState,
    caster_id: ConnectionId,
    npc_idx: usize,
    spell: &crate::data::spells::SpellData,
) {
    use crate::game::npc::{ELEMENTAL_AGUA, ELEMENTAL_FUEGO, ELEMENTAL_TIERRA};

    // VB6: Elementals are immune to paralysis/immobilize (modHechizos.bas line 993)
    if spell.paraliza {
        let npc_num = state.get_npc(npc_idx).map(|n| n.npc_number as i32).unwrap_or(0);
        if npc_num == ELEMENTAL_AGUA || npc_num == ELEMENTAL_FUEGO || npc_num == ELEMENTAL_TIERRA {
            state.send_msg_id(caster_id, 846, "").await; // Immune
            return;
        }
    }

    let paralisis_interval = state.config.intervalo_paralizado;
    if let Some(npc) = state.get_npc_mut(npc_idx) {
        if spell.envenena {
            npc.veneno = true;
        }
        if spell.cura_veneno {
            npc.veneno = false;
        }
        if spell.paraliza {
            npc.paralyzed = true;
            // NPCs use 5x the normal paralysis duration (they recover much slower)
            npc.counter_paralisis = paralisis_interval * 5;
        }
        // VB6: RemoverParalisis does NOT work on NPCs (only users)
    }
}

/// Apply property-type spell effects (HP, Mana, Stamina modifications).
pub(super) async fn apply_spell_properties(
    state: &mut GameState,
    _caster_id: ConnectionId,
    target_id: ConnectionId,
    spell: &crate::data::spells::SpellData,
) {
    // Capture target info for floating damage before mutable borrow
    let target_info = state.users.get(&target_id)
        .map(|u| (u.char_index.0, u.pos_map, u.pos_x, u.pos_y));

    let mut damage_dealt = 0i32;

    if let Some(target) = state.users.get_mut(&target_id) {
        // HP effect
        if spell.sube_hp == 1 {
            // Heal
            let amount = rand_range(spell.min_hp, spell.max_hp);
            target.min_hp = (target.min_hp + amount).min(target.max_hp);
        } else if spell.sube_hp == 2 {
            // Damage
            let amount = rand_range(spell.min_hp, spell.max_hp);
            target.min_hp -= amount;
            damage_dealt = amount;
        }

        // Mana effect
        if spell.sube_mana == 1 {
            let amount = rand_range(spell.min_mana, spell.max_mana);
            target.min_mana = (target.min_mana + amount).min(target.max_mana);
        }

        // Stamina effect
        if spell.sube_sta == 1 {
            let amount = rand_range(spell.min_sta, spell.max_sta);
            target.min_sta = (target.min_sta + amount).min(target.max_sta);
        }
    }

    // VB6: floating yellow damage number above target for damage spells
    if damage_dealt > 0 {
        if let Some((ci, map, tx, ty)) = target_info {
            state.send_chat_over_head_to(SendTarget::ToArea { map, x: tx, y: ty }, &format!("-{}", damage_dealt), ci as i16, 65535).await;
        }
    }

    // Send updated stats
    send_stats_hp(state, target_id).await;
    send_stats_mana(state, target_id).await;
    send_stats_sta(state, target_id).await;

    // Check death from damage spell
    let hp = state.users.get(&target_id).map(|u| u.min_hp).unwrap_or(0);
    if hp <= 0 {
        user_die(state, target_id, Some(_caster_id)).await;
    }
}

/// Apply status-type spell effects (poison, paralysis, cure, remove paralysis, resurrection, etc.).
pub(super) async fn apply_spell_status(
    state: &mut GameState,
    caster_id: ConnectionId,
    target_id: ConnectionId,
    spell: &crate::data::spells::SpellData,
) {
    // VB6: Can't paralyze/immobilize yourself (||31)
    if (spell.paraliza || spell.inmoviliza) && caster_id == target_id {
        state.send_msg_id(caster_id, 31, "").await;
        return;
    }
    // VB6: Can't poison yourself
    if spell.envenena && caster_id == target_id {
        return;
    }

    // Pre-read target state for validation checks
    let target_dead = state.users.get(&target_id).map(|u| u.dead).unwrap_or(false);
    let target_navigating = state.users.get(&target_id).map(|u| u.navigating).unwrap_or(false);
    let target_map = state.users.get(&target_id).map(|u| u.pos_map).unwrap_or(0);

    // VB6: Cure poison — can't cure dead users
    if spell.cura_veneno && target_dead {
        state.send_console(caster_id, "¡El usuario está muerto!", font_index::INFO).await;
        return;
    }

    // VB6: Invisibility — can't invis dead users
    if spell.invisibilidad && target_dead {
        return;
    }
    // VB6: Invisibility — check map InviSinEfecto
    if spell.invisibilidad {
        let invi_blocked = state.game_data.maps.get(target_map as usize)
            .and_then(|m| m.as_ref())
            .map(|m| m.info.invi_sin_efecto)
            .unwrap_or(false);
        if invi_blocked {
            state.send_console(caster_id, "La invisibilidad no funciona en este mapa.", font_index::INFO).await;
            return;
        }
    }

    // Track what we need to send after dropping the mutable borrow
    let mut send_paradok_on = false;   // paralysis applied → send PARADOK + PU
    let mut send_paradok_off = false;  // paralysis removed → send PARADOK
    let mut send_invis = false;        // invisibility applied → send BP + SetInvisible

    if let Some(target) = state.users.get_mut(&target_id) {
        if spell.cura_veneno {
            target.poisoned = false;
        }
        if spell.paraliza || spell.inmoviliza {
            if !target.paralyzed {
                target.paralyzed = true;
                if spell.inmoviliza {
                    target.immobilized = true;
                }
                target.counter_paralisis = state.config.intervalo_paralizado;
                send_paradok_on = true;
            }
        }
        if spell.remover_paralisis {
            if target.paralyzed {
                target.paralyzed = false;
                target.immobilized = false;
                target.counter_paralisis = 0;
                send_paradok_off = true;
            }
        }
        if spell.envenena {
            target.poisoned = true;
        }
        if spell.invisibilidad {
            target.invisible = true;
            target.hidden = true;
            send_invis = true;
        }
    }

    // Send PARADOK + PU outside borrow scope (VB6: lines 759-760)
    if send_paradok_on {
        let pkt = binary_packets::write_paralize_ok();
        state.send_bytes(target_id, &pkt).await;
        // PU forces client position to server-known position (prevents ghost movement)
        if let Some(u) = state.users.get(&target_id) {
            let pu = binary_packets::write_pos_update(u.pos_x as u8, u.pos_y as u8);
            state.send_bytes(target_id, &pu).await;
        }
    }
    if send_paradok_off {
        let pkt = binary_packets::write_paralize_ok();
        state.send_bytes(target_id, &pkt).await;
    }

    // Invisibility spell — remove from others' screens, tell self
    // VB6: skip SetInvisible packet if navigating (boat already hides char)
    if send_invis && !target_navigating {
        if let Some(u) = state.users.get(&target_id) {
            let ci = u.char_index.0 as i16;
            let (map, x, y) = (u.pos_map, u.pos_x, u.pos_y);
            let bp = binary_packets::write_character_remove(ci);
            state.send_data_bytes(SendTarget::ToAreaButIndex { conn_id: target_id, map, x, y }, &bp).await;
            let nover = binary_packets::write_set_invisible(ci, true);
            state.send_bytes(target_id, &nover).await;
        }
    }

    // Resurrection spell
    if spell.revivir {
        let target_dead = state.users.get(&target_id).map(|u| u.dead).unwrap_or(false);
        let target_seguro_resu = state.users.get(&target_id).map(|u| u.seguro_resu).unwrap_or(false);
        let target_time_revivir = state.users.get(&target_id).map(|u| u.time_revivir).unwrap_or(0);

        if target_dead {
            // Check resurrection safety (target opted out)
            if target_seguro_resu {
                state.send_msg_id(caster_id, 841, "").await;
                return;
            }
            // Check resurrection cooldown
            if target_time_revivir > 0 {
                state.send_msg_id(caster_id, 843, &format!("{}", target_time_revivir)).await;
                return;
            }

            // Check if caster is cleric (instant full HP rez)
            let caster_class = state.users.get(&caster_id)
                .map(|u| u.class.to_uppercase())
                .unwrap_or_default();
            let caster_name = state.users.get(&caster_id)
                .map(|u| u.char_name.clone())
                .unwrap_or_default();

            if caster_class == "CLERIGO" {
                // Cleric: instant resurrection at full HP
                revive_user(state, target_id).await;
                if let Some(target) = state.users.get_mut(&target_id) {
                    target.min_hp = target.max_hp;
                }
                send_stats_hp(state, target_id).await;
                state.send_msg_id(target_id, 749, &caster_name).await;
            } else {
                // Non-cleric: 10 second delayed resurrection
                if let Some(target) = state.users.get_mut(&target_id) {
                    target.segundos_para_revivir = 10;
                }
                state.send_msg_id(target_id, 845, "").await;
            }

            // Caster pays HP cost (reduced to 10)
            if let Some(caster) = state.users.get_mut(&caster_id) {
                caster.min_hp = 10;
            }
            send_stats_hp(state, caster_id).await;
        }
    }
}

/// Apply attribute buff spells (SubeAgilidad, SubeFuerza).
/// VB6: modHechizos.bas — SubeAgilidad=1 buffs, =2 debuffs. SubeFuerza same pattern.
/// Buffs are temporary: DuracionEfecto ticks, then attributes restored from backup.
pub(super) async fn apply_spell_buffs(
    state: &mut GameState,
    _caster_id: ConnectionId,
    target_id: ConnectionId,
    spell: &crate::data::spells::SpellData,
) {
    const MAX_ATTR: i32 = 35;
    const MIN_ATTR: i32 = 1;

    // SubeAgilidad: 1=buff, 2=debuff
    if spell.sube_agilidad > 0 {
        let amount = rand_range(spell.min_agilidad, spell.max_agilidad);
        if let Some(target) = state.users.get_mut(&target_id) {
            if !target.tomo_pocion {
                // Save backup before first buff
                target.attributes_backup = target.attributes;
            }
            if spell.sube_agilidad == 1 {
                // Buff: increase agility
                target.attributes[1] = (target.attributes[1] + amount).min(MAX_ATTR); // [1] = Agi
                target.duracion_efecto = 7000;
            } else {
                // Debuff: decrease agility
                target.attributes[1] = (target.attributes[1] - amount).max(MIN_ATTR);
                target.duracion_efecto = 700;
            }
            target.tomo_pocion = true;
        }
    }

    // SubeFuerza: 1=buff, 2=debuff
    if spell.sube_fuerza > 0 {
        let amount = rand_range(spell.min_fuerza, spell.max_fuerza);
        if let Some(target) = state.users.get_mut(&target_id) {
            if !target.tomo_pocion {
                target.attributes_backup = target.attributes;
            }
            if spell.sube_fuerza == 1 {
                target.attributes[0] = (target.attributes[0] + amount).min(MAX_ATTR); // [0] = Str
                target.duracion_efecto = 1200;
            } else {
                target.attributes[0] = (target.attributes[0] - amount).max(MIN_ATTR);
                target.duracion_efecto = 700;
            }
            target.tomo_pocion = true;
        }
    }
}

/// Apply invocation spell — spawn NPCs as pets.
/// VB6: HechizoInvocacion (modHechizos.bas). Max 3 pets, singleton elementals.
const MAX_MASCOTAS: i32 = 3;

pub(super) async fn apply_spell_invocation(
    state: &mut GameState,
    caster_id: ConnectionId,
    spell: &crate::data::spells::SpellData,
) {
    let (map, x, y, nro_mascotas) = match state.users.get(&caster_id) {
        Some(u) if u.logged => (u.pos_map, u.pos_x, u.pos_y, u.nro_mascotas),
        _ => return,
    };

    if nro_mascotas >= MAX_MASCOTAS {
        state.send_console(caster_id, "No puedes invocar mas criaturas.", font_index::INFO).await;
        return;
    }

    let npc_num = spell.num_npc;
    let cant = spell.cant;
    if npc_num <= 0 || cant <= 0 {
        return;
    }

    // VB6: Elemental singleton checks — can only have one of each type
    {
        use crate::game::npc::{ELEMENTAL_AGUA, ELEMENTAL_FUEGO, ELEMENTAL_TIERRA};
        if let Some(user) = state.users.get(&caster_id) {
            match npc_num {
                ELEMENTAL_AGUA if user.ele_de_agua => {
                    state.send_msg_id(caster_id, 23, "").await; // Ya tienes un Elemental de Agua
                    return;
                }
                ELEMENTAL_FUEGO if user.ele_de_fuego => {
                    state.send_msg_id(caster_id, 24, "").await; // Ya tienes un Elemental de Fuego
                    return;
                }
                ELEMENTAL_TIERRA if user.ele_de_tierra => {
                    state.send_msg_id(caster_id, 22, "").await; // Ya tienes un Elemental de Tierra
                    return;
                }
                _ => {}
            }
        }
    }

    // Spawn up to `cant` NPCs (limited by MAXMASCOTAS)
    for _ in 0..cant {
        let current_pets = state.users.get(&caster_id).map(|u| u.nro_mascotas).unwrap_or(MAX_MASCOTAS);
        if current_pets >= MAX_MASCOTAS {
            break;
        }

        // Spawn the NPC at caster's position
        if let Some(npc_idx) = state.spawn_npc(npc_num as usize, map, x, y) {
            // Update pet tracking
            if let Some(user) = state.users.get_mut(&caster_id) {
                for slot in 0..3 {
                    if user.mascotas_index[slot] == 0 {
                        user.mascotas_index[slot] = npc_idx;
                        user.mascotas_type[slot] = npc_num;
                        user.nro_mascotas += 1;
                        break;
                    }
                }
                // Set elemental flags
                use crate::game::npc::{ELEMENTAL_AGUA, ELEMENTAL_FUEGO, ELEMENTAL_TIERRA};
                match npc_num {
                    ELEMENTAL_AGUA => user.ele_de_agua = true,
                    ELEMENTAL_FUEGO => user.ele_de_fuego = true,
                    ELEMENTAL_TIERRA => user.ele_de_tierra = true,
                    _ => {}
                }
            }

            // Set NPC owner
            if let Some(npc) = state.get_npc_mut(npc_idx) {
                npc.maestro_user = Some(caster_id);
            }

            // Broadcast NPC creation using its CC packet
            let cc_pkt = state.get_npc(npc_idx).map(|n| n.build_cc_binary());
            if let Some(pkt) = cc_pkt {
                state.send_data_bytes(SendTarget::ToArea { map, x, y }, &pkt).await;
            }
        }
    }
}

/// Apply summon pet spell — toggle single mount/pet.
/// VB6: InvocarMascota (modHechizos.bas). If already summoned, dismiss.
pub(super) async fn apply_spell_summon_pet(
    state: &mut GameState,
    caster_id: ConnectionId,
    spell: &crate::data::spells::SpellData,
) {
    let npc_num = spell.num_npc;
    if npc_num <= 0 {
        return;
    }

    // Check if already has this pet type — if so, dismiss it
    let dismiss_slot = state.users.get(&caster_id).and_then(|u| {
        (0..3).find(|&slot| u.mascotas_type[slot] == npc_num && u.mascotas_index[slot] > 0)
    });

    if let Some(slot) = dismiss_slot {
        // Dismiss the existing pet
        let npc_idx = state.users.get(&caster_id).map(|u| u.mascotas_index[slot]).unwrap_or(0);
        if npc_idx > 0 {
            state.kill_npc(npc_idx);
        }
        if let Some(user) = state.users.get_mut(&caster_id) {
            user.mascotas_index[slot] = 0;
            user.mascotas_type[slot] = 0;
            user.nro_mascotas = (user.nro_mascotas - 1).max(0);
        }
        return;
    }

    // Otherwise summon like invocation
    apply_spell_invocation(state, caster_id, spell).await;
}

// =====================================================================
/// Apply teleport spell — warp caster to fixed destination.
/// VB6: modHechizos.bas TipoHechizo=5. Uses PortalMap/PortalX/PortalY.
pub(super) async fn apply_spell_teleport(
    state: &mut GameState,
    caster_id: ConnectionId,
    spell: &crate::data::spells::SpellData,
) {
    let dest_map = spell.portal_map;
    let dest_x = spell.portal_x;
    let dest_y = spell.portal_y;

    if dest_map <= 0 || dest_x <= 0 || dest_y <= 0 {
        return;
    }

    let (cur_map, cur_x, cur_y, char_index) = match state.users.get(&caster_id) {
        Some(u) if u.logged && !u.dead => (u.pos_map, u.pos_x, u.pos_y, u.char_index),
        _ => return,
    };

    // Remove from current position
    state.world.remove_user(cur_map, cur_x, cur_y);

    // VB6: QDL + BP sent to full area to prevent ghost characters
    let qdl_pkt = binary_packets::write_character_remove(char_index.0 as i16);
    state.send_data_bytes(SendTarget::ToMapButIndex { conn_id: caster_id, map: cur_map }, &qdl_pkt).await;
    let bp_pkt = binary_packets::write_character_remove(char_index.0 as i16);
    state.send_data_bytes(SendTarget::ToMapButIndex { conn_id: caster_id, map: cur_map }, &bp_pkt).await;

    // Update position
    if let Some(user) = state.users.get_mut(&caster_id) {
        user.pos_map = dest_map;
        user.pos_x = dest_x;
        user.pos_y = dest_y;
    }

    // Place on new map
    state.world.place_user(dest_map, dest_x, dest_y, caster_id);

    // Get map info
    let map_idx = dest_map as usize;
    let (r, g, b, music, map_name) = if let Some(Some(game_map)) = state.game_data.maps.get(map_idx) {
        (game_map.info.r, game_map.info.g, game_map.info.b, game_map.info.music, game_map.info.name.clone())
    } else {
        (200, 200, 200, 0, format!("Mapa {}", dest_map))
    };

    // Send map change packets
    let cm_pkt = binary_packets::write_change_map(dest_map as i16, 0, r as u8, g as u8, b as u8);
    state.send_bytes(caster_id, &cm_pkt).await;
    let pu_pkt = binary_packets::write_pos_update(dest_x as u8, dest_y as u8);
    state.send_bytes(caster_id, &pu_pkt).await;
    let midi_pkt = binary_packets::write_play_midi(music as u8);
    state.send_bytes(caster_id, &midi_pkt).await;
    // Map name
    let mn_pkt = binary_packets::write_map_name(&map_name);
    state.send_bytes(caster_id, &mn_pkt).await;

    // Send CC to new area
    let cc_pkt = state.users.get(&caster_id).map(|u| u.build_cc_binary());
    if let Some(pkt) = cc_pkt {
        state.send_data_bytes(SendTarget::ToAreaButIndex { conn_id: caster_id, map: dest_map, x: dest_x, y: dest_y }, &pkt).await;
    }

    // Drain all mana (VB6 sets mana to 0 on teleport)
    if let Some(user) = state.users.get_mut(&caster_id) {
        user.min_mana = 0;
    }
    send_stats_mana(state, caster_id).await;
}
