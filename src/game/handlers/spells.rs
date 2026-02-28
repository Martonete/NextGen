//! Spell casting and effect handlers.
//! Extracted from mod.rs to reduce file size.

use tracing::info;
use crate::net::ConnectionId;
use crate::game::types::{GameState, SendTarget, MAX_SPELL_SLOTS};
use crate::game::world;
use crate::protocol::{server_opcodes, font_types, fields::read_field};
use crate::data::objects::ObjType;
use super::common::*;
use super::{user_die, npc_die, revive_user, warp_user};

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
        // Color by privilege/faction (VB6 LookatTile lines 895-956)
        if priv_target > 11 { stat.push_str(" <Administrador> ~255~255~255~1~0"); }
        else if priv_target > 3 { stat.push_str(" <Game Master> ~120~250~250~1~0"); }
        else if priv_target > 0 { stat.push_str(" <Game Master> ~0~185~0~1~0"); }
        else if level <= limite_newbie { stat.push_str(" ~255~255~202~1~0"); }
        else if armada { stat.push_str(" ~0~128~255~1~0"); }
        else if caos { stat.push_str(" ~255~0~0~1~0"); }
        else if criminal { stat.push_str(" <CRIMINAL> ~255~0~0~1~0"); }
        else { stat.push_str(" <CIUDADANO> ~0~128~255~1~0"); }

        let msg = format!("N|{}", stat);
        state.send_to(conn_id, &msg).await;
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
        let msg = format!("{}{}{}", server_opcodes::CONSOLE_MSG, msg_text, font_types::INFO);
        state.send_to(conn_id, &msg).await;
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
                u.dead, u.paralyzed, u.min_mana,
                spell_id, u.level, u.target_x, u.target_y, u.target_map,
                u.target_user, u.target_npc as usize, u.privileges,
            )
        }
        _ => return,
    };
    let (map, x, y, char_index, dead, paralyzed, min_mana,
         spell_id, _level, target_x, target_y, _target_map,
         target_user_conn, target_npc_idx, privileges) = user_data;

    if dead || paralyzed || spell_id == 0 {
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

    // ===== STEP 1: PuedeLanzar — mana, skill checks (VB6 lines 269-309) =====
    if min_mana < spell.mana_requerido {
        state.send_to(conn_id, "||18").await; // Not enough mana
        return;
    }
    if spell.min_skill > 0 {
        let magic_skill = state.users.get(&conn_id).map(|u| u.skills[5]).unwrap_or(0);
        if magic_skill < spell.min_skill {
            state.send_to(conn_id, "||834").await; // Magic skill too low
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
                state.send_to(conn_id, "||25").await; // No valid user target
                return;
            }
        }
        TargetType::NpcOnly => {
            // Needs an NPC target
            if !has_npc_target {
                state.send_to(conn_id, "||29").await; // No valid NPC target
                return;
            }
        }
        TargetType::UserAndNpc => {
            // Needs either user or NPC
            if !has_user_target && !has_npc_target {
                state.send_to(conn_id, "||25").await; // No valid target
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

    // ===== STEP 4: Specific spell validations (inside HandleHechizo) =====

    // Route to NPC or User handling
    if has_npc_target && !has_user_target {
        // ===== NPC TARGET =====
        let npc_idx = target_npc.unwrap();

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
            state.send_to(conn_id, "||31").await; // Can't attack yourself
            return; // NO mana consumed, NO FX
        }

        // VB6: Healing full HP check (||145)
        if spell.sube_hp == 1 {
            let full_hp = state.users.get(&target_id)
                .map(|u| u.min_hp >= u.max_hp).unwrap_or(false);
            if full_hp {
                state.send_to(conn_id, "||145").await;
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

    // Magic words
    if !spell.palabras_magicas.is_empty() {
        let pkt = format!("N|16776960\u{00B0}{}\u{00B0}{}", spell.palabras_magicas, caster_ci.0);
        state.send_data(SendTarget::ToArea { map, x, y }, &pkt).await;
    }

    // Target char_index for FX
    let target_ci = state.users.get(&target_id).map(|u| u.char_index.0).unwrap_or(caster_ci.0);
    let (fx_map, fx_x, fx_y) = state.users.get(&target_id)
        .map(|u| (u.pos_map, u.pos_x, u.pos_y)).unwrap_or((map, x, y));

    // FX + Sound
    if spell.fx_grh > 0 {
        let fx_pkt = format!("CFX{},{},{}", target_ci, spell.fx_grh, spell.loops);
        state.send_data(SendTarget::ToArea { map: fx_map, x: fx_x, y: fx_y }, &fx_pkt).await;
    }
    if spell.wav > 0 {
        let snd_pkt = format!("TW{}", spell.wav);
        state.send_data(SendTarget::ToArea { map: fx_map, x: fx_x, y: fx_y }, &snd_pkt).await;
    }

    // Console messages
    if target_id != caster_id {
        let target_name = state.users.get(&target_id).map(|u| u.char_name.clone()).unwrap_or_default();
        if !spell.hechizero_msg.is_empty() {
            let msg = format!("N|{} {}~255~0~0~1", spell.hechizero_msg, target_name);
            state.send_to(caster_id, &msg).await;
        }
        if !spell.target_msg.is_empty() {
            let msg = format!("N|{} {}~255~0~0~1", caster_name, spell.target_msg);
            state.send_to(target_id, &msg).await;
        }
    } else {
        if !spell.propio_msg.is_empty() {
            let msg = format!("N|{}~255~0~0~1", spell.propio_msg);
            state.send_to(caster_id, &msg).await;
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

    // Magic words
    if !spell.palabras_magicas.is_empty() {
        let pkt = format!("N|16776960\u{00B0}{}\u{00B0}{}", spell.palabras_magicas, caster_ci.0);
        state.send_data(SendTarget::ToArea { map, x, y }, &pkt).await;
    }

    // NPC char_index for FX
    let npc_ci = state.get_npc(npc_idx).map(|n| n.char_index.0).unwrap_or(0);
    let (fx_map, fx_x, fx_y) = state.get_npc(npc_idx)
        .map(|n| (n.map, n.x, n.y)).unwrap_or((map, x, y));

    if spell.fx_grh > 0 {
        let fx_pkt = format!("CFX{},{},{}", npc_ci, spell.fx_grh, spell.loops);
        state.send_data(SendTarget::ToArea { map: fx_map, x: fx_x, y: fx_y }, &fx_pkt).await;
    }
    if spell.wav > 0 {
        let snd_pkt = format!("TW{}", spell.wav);
        state.send_data(SendTarget::ToArea { map: fx_map, x: fx_x, y: fx_y }, &snd_pkt).await;
    }

    // Console message
    if !spell.hechizero_msg.is_empty() {
        let msg = format!("N|{} la criatura.~255~0~0~1", spell.hechizero_msg);
        state.send_to(caster_id, &msg).await;
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

    // Get NPC data for damage number display
    let npc_data = state.get_npc(npc_idx).map(|n| (n.char_index.0, n.map, n.x, n.y));
    let (npc_ci, npc_map, npc_x, npc_y) = match npc_data {
        Some(d) => d,
        None => return,
    };

    // Send damage number over NPC head (VB6: N| vbYellow°-<damage>°<npc_charindex>)
    let caster_map = state.users.get(&caster_id).map(|u| (u.pos_map, u.pos_x, u.pos_y)).unwrap_or((npc_map, npc_x, npc_y));
    let dmg_pkt = format!("N|65535\u{00B0}-{}\u{00B0}{}", damage, npc_ci);
    state.send_data(SendTarget::ToArea { map: caster_map.0, x: caster_map.1, y: caster_map.2 }, &dmg_pkt).await;

    // Send damage console message to caster: ||850@<damage>
    state.send_to(caster_id, &format!("||850@{}", damage)).await;

    // Apply damage to NPC
    if let Some(npc) = state.get_npc_mut(npc_idx) {
        npc.min_hp -= damage;
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
    _caster_id: ConnectionId,
    npc_idx: usize,
    spell: &crate::data::spells::SpellData,
) {
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
            npc.counter_paralisis = paralisis_interval;
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

    // Send updated stats
    send_stats_hp(state, target_id).await;
    send_stats_mana(state, target_id).await;
    send_stats_sta(state, target_id).await;

    // Check death from damage spell
    let hp = state.users.get(&target_id).map(|u| u.min_hp).unwrap_or(0);
    if hp <= 0 {
        user_die(state, target_id, None).await;
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
    if spell.paraliza && caster_id == target_id {
        state.send_to(caster_id, "||31").await;
        return;
    }

    // Track what we need to send after dropping the mutable borrow
    let mut send_paradok_on = false;   // paralysis applied → send PARADOK + PU
    let mut send_paradok_off = false;  // paralysis removed → send PARADOK

    if let Some(target) = state.users.get_mut(&target_id) {
        if spell.cura_veneno {
            target.poisoned = false;
        }
        if spell.paraliza {
            if !target.paralyzed {
                target.paralyzed = true;
                target.counter_paralisis = state.config.intervalo_paralizado;
                send_paradok_on = true;
            }
        }
        if spell.remover_paralisis {
            if target.paralyzed {
                target.paralyzed = false;
                send_paradok_off = true;
            }
        }
        if spell.envenena {
            target.poisoned = true;
        }
        if spell.invisibilidad {
            target.invisible = true;
            target.hidden = true;
        }
    }

    // Send PARADOK + PU outside borrow scope (VB6: lines 759-760)
    if send_paradok_on {
        state.send_to(target_id, "PARADOK").await;
        // PU forces client position to server-known position (prevents ghost movement)
        let pu = state.users.get(&target_id)
            .map(|u| format!("PU{},{}", u.pos_x, u.pos_y));
        if let Some(pkt) = pu {
            state.send_to(target_id, &pkt).await;
        }
    }
    if send_paradok_off {
        state.send_to(target_id, "PARADOK").await;
    }

    // Resurrection spell
    if spell.revivir {
        let target_dead = state.users.get(&target_id).map(|u| u.dead).unwrap_or(false);
        let target_seguro_resu = state.users.get(&target_id).map(|u| u.seguro_resu).unwrap_or(false);
        let target_time_revivir = state.users.get(&target_id).map(|u| u.time_revivir).unwrap_or(0);

        if target_dead {
            // Check resurrection safety (target opted out)
            if target_seguro_resu {
                let msg = format!("{}841", server_opcodes::CONSOLE_MSG);
                state.send_to(caster_id, &msg).await;
                return;
            }
            // Check resurrection cooldown
            if target_time_revivir > 0 {
                let msg = format!("{}843@{}", server_opcodes::CONSOLE_MSG, target_time_revivir);
                state.send_to(caster_id, &msg).await;
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
                let msg = format!("{}749@{}", server_opcodes::CONSOLE_MSG, caster_name);
                state.send_to(target_id, &msg).await;
            } else {
                // Non-cleric: 10 second delayed resurrection
                if let Some(target) = state.users.get_mut(&target_id) {
                    target.segundos_para_revivir = 10;
                }
                let msg = format!("{}845", server_opcodes::CONSOLE_MSG);
                state.send_to(target_id, &msg).await;
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
        let msg = format!("{}No puedes invocar mas criaturas.", server_opcodes::CONSOLE_MSG);
        state.send_to(caster_id, &msg).await;
        return;
    }

    let npc_num = spell.num_npc;
    let cant = spell.cant;
    if npc_num <= 0 || cant <= 0 {
        return;
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
            }

            // Set NPC owner
            if let Some(npc) = state.get_npc_mut(npc_idx) {
                npc.maestro_user = Some(caster_id);
            }

            // Broadcast NPC creation using its CC packet
            let cc_pkt = state.get_npc(npc_idx).map(|n| n.build_cc_packet());
            if let Some(pkt) = cc_pkt {
                state.send_data(SendTarget::ToArea { map, x, y }, &pkt).await;
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

    // Send BP to area (remove character)
    let bp_pkt = format!("BP{}", char_index.0);
    state.send_data(SendTarget::ToArea { map: cur_map, x: cur_x, y: cur_y }, &bp_pkt).await;

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
    state.send_to(caster_id, &format!("CM{},{},{},{}", dest_map, r, g, b)).await;
    state.send_to(caster_id, &format!("PU{},{}", dest_x, dest_y)).await;
    state.send_to(caster_id, &format!("XM{}", music)).await;
    state.send_to(caster_id, &format!("N~{}", map_name)).await;

    // Send CC to new area
    let cc_pkt = state.users.get(&caster_id).map(|u| u.build_cc_packet());
    if let Some(pkt) = cc_pkt {
        state.send_data(SendTarget::ToAreaButIndex { conn_id: caster_id, map: dest_map, x: dest_x, y: dest_y }, &pkt).await;
    }

    // Drain all mana (VB6 sets mana to 0 on teleport)
    if let Some(user) = state.users.get_mut(&caster_id) {
        user.min_mana = 0;
    }
    send_stats_mana(state, caster_id).await;
}
