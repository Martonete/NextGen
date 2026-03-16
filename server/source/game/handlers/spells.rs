//! Spell casting and effect handlers.
//! Extracted from mod.rs to reduce file size.

use tracing::info;
use crate::net::ConnectionId;
use crate::game::class_race::PlayerClass;
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
            let gn = state.users.get(&target_conn).map(|u| u.guild_name.clone()).unwrap_or_default();
            if !gn.is_empty() {
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

        state.send_console(conn_id, &stat, fi);
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
        state.send_console(conn_id, &msg_text, font_index::INFO);
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
        state.send_msg_id(conn_id, 26, ""); // "Necesitas un arma mágica para lanzar hechizos"
        return;
    }

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
    if spell.min_skill > 0 {
        let magic_skill = state.users.get(&conn_id).map(|u| u.skills[5]).unwrap_or(0);
        if magic_skill < spell.min_skill {
            state.send_msg_id(conn_id, 834, ""); // Magic skill too low
            return;
        }
    }
    // VB6: Staff power check for Mages (modHechizos.bas lines 449-460)
    if spell.need_staff > 0 {
        let (class, weapon_slot, inv) = state.users.get(&conn_id)
            .map(|u| (u.class, u.equip.weapon, u.inventory.clone()))
            .unwrap_or_default();
        if class == PlayerClass::Mago {
            let staff_power = if weapon_slot > 0 && weapon_slot <= inv.len() {
                let obj_idx = inv[weapon_slot - 1].obj_index;
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

    // ===== STEP 4: Safe zone check for offensive spells (VB6: PuedeAtacar) =====
    if is_offensive {
        let attacker_trigger = get_map_tile_trigger(state, map, x, y);
        if attacker_trigger == crate::data::maps::Trigger::SafeZone {
            state.send_msg_id(conn_id, 164, ""); // Can't attack in safe zone
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

        // VB6: Safe zone check — victim in safe zone blocks offensive spells
        if is_offensive && target_id != conn_id {
            let victim_pos = state.users.get(&target_id).map(|u| (u.pos_x, u.pos_y)).unwrap_or((0, 0));
            let victim_trigger = get_map_tile_trigger(state, map, victim_pos.0, victim_pos.1);
            if victim_trigger == crate::data::maps::Trigger::SafeZone {
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
            // Clan safe check — can't cast offensive spells on clanmates with seguro_clan on
            let caster_seguro = state.users.get(&conn_id).map(|u| u.seguro_clan).unwrap_or(false);
            if caster_seguro && same_clan(state, conn_id, target_id) {
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
const APOCALIPSIS_SPELL_INDEX: i32 = 25;

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
                if ring_obj == FLAUTAELFICA {
                    if spell.mimetiza {
                        // VB6: Mimicry: 50% less mana
                        mana_cost = (mana_cost as f64 * 0.5) as i32;
                    } else if spell.tipo == crate::data::spells::SpellType::Invocation {
                        // VB6: Invocation spells: 30% less mana (mana * 0.7)
                        mana_cost = (mana_cost as f64 * 0.7) as i32;
                    } else if spell.index as i32 != APOCALIPSIS_SPELL_INDEX {
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
        state.send_chat_over_head_to(SendTarget::ToArea { map, x, y }, &spell.palabras_magicas, caster_ci.0 as i16, 16776960);
    }

    // Target char_index for FX
    let target_ci = state.users.get(&target_id).map(|u| u.char_index.0).unwrap_or(caster_ci.0);
    let (fx_map, fx_x, fx_y) = state.users.get(&target_id)
        .map(|u| (u.pos_map, u.pos_x, u.pos_y)).unwrap_or((map, x, y));

    // FX + Sound
    if spell.fx_grh > 0 {
        let fx_pkt = binary_packets::write_create_fx(target_ci as i16, spell.fx_grh as i16, spell.loops as i16);
        state.send_data_bytes(SendTarget::ToArea { map: fx_map, x: fx_x, y: fx_y }, &fx_pkt);
    }
    if spell.wav > 0 {
        let snd_pkt = binary_packets::write_play_wave(spell.wav as u8, fx_x as i16, fx_y as i16);
        state.send_data_bytes(SendTarget::ToArea { map: fx_map, x: fx_x, y: fx_y }, &snd_pkt);
    }

    // Console messages (red font)
    if target_id != caster_id {
        let target_name = state.users.get(&target_id).map(|u| u.char_name.clone()).unwrap_or_default();
        if !spell.hechizero_msg.is_empty() {
            state.send_console(caster_id, &format!("{} {}", spell.hechizero_msg, target_name), font_index::FIGHT);
        }
        if !spell.target_msg.is_empty() {
            state.send_console(target_id, &format!("{} {}", caster_name, spell.target_msg), font_index::FIGHT);
        }
    } else {
        if !spell.propio_msg.is_empty() {
            state.send_console(caster_id, &spell.propio_msg, font_index::FIGHT);
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
        state.send_chat_over_head_to(SendTarget::ToArea { map, x, y }, &spell.palabras_magicas, caster_ci.0 as i16, 16776960);
    }

    // NPC char_index for FX
    let npc_ci = state.get_npc(npc_idx).map(|n| n.char_index.0).unwrap_or(0);
    let (fx_map, fx_x, fx_y) = state.get_npc(npc_idx)
        .map(|n| (n.map, n.x, n.y)).unwrap_or((map, x, y));

    if spell.fx_grh > 0 {
        let fx_pkt = binary_packets::write_create_fx(npc_ci as i16, spell.fx_grh as i16, spell.loops as i16);
        state.send_data_bytes(SendTarget::ToArea { map: fx_map, x: fx_x, y: fx_y }, &fx_pkt);
    }
    if spell.wav > 0 {
        let snd_pkt = binary_packets::write_play_wave(spell.wav as u8, fx_x as i16, fx_y as i16);
        state.send_data_bytes(SendTarget::ToArea { map: fx_map, x: fx_x, y: fx_y }, &snd_pkt);
    }

    // Console message (red font)
    if !spell.hechizero_msg.is_empty() {
        state.send_console(caster_id, &format!("{} la criatura.", spell.hechizero_msg), font_index::FIGHT);
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
    // VB6: SubeHP=1 heals NPC (pet healing), SubeHP=2 damages
    if spell.sube_hp == 1 {
        // Heal NPC (VB6: HechizoPropNPC heal path)
        let heal = calc_spell_heal(state, caster_id, spell);
        if let Some(npc) = state.get_npc_mut(npc_idx) {
            npc.min_hp = (npc.min_hp + heal).min(npc.max_hp);
        }
        return;
    }
    if spell.sube_hp != 2 {
        return;
    }

    // VB6: Damage = base + level scaling + staff + lute - NPC.defM
    let mut damage = calc_spell_damage(state, caster_id, spell);

    // Subtract NPC magic defense (VB6: daño = daño - .Stats.defM)
    let npc_def_m = state.get_npc(npc_idx).map(|n| n.def_m).unwrap_or(0);
    damage = (damage - npc_def_m).max(0);

    // Get NPC data for damage number display and exp calculation
    let npc_data = state.get_npc(npc_idx).map(|n| (n.char_index.0, n.map, n.x, n.y, n.give_exp, n.max_hp));
    let (npc_ci, npc_map, npc_x, npc_y, npc_give_exp, npc_max_hp) = match npc_data {
        Some(d) => d,
        None => return,
    };

    // Send damage number over NPC head (VB6: N| vbYellow°-<damage>°<npc_charindex>)
    let caster_map = state.users.get(&caster_id).map(|u| (u.pos_map, u.pos_x, u.pos_y)).unwrap_or((npc_map, npc_x, npc_y));
    state.send_chat_over_head_to(SendTarget::ToArea { map: caster_map.0, x: caster_map.1, y: caster_map.2 }, &format!("-{}", damage), npc_ci as i16, 65535);

    // Send damage console message to caster: ||850@<damage>
    state.send_msg_id(caster_id, 850, &format!("{}", damage));

    // Apply damage to NPC
    if let Some(npc) = state.get_npc_mut(npc_idx) {
        npc.min_hp -= damage;
        npc.damage_received.push((caster_id, damage));
    }

    // Per-hit EXP (VB6: CalcularDarExp — proportional exp on every hit)
    if npc_give_exp > 0 && npc_max_hp > 0 {
        let exp_mult = state.multiplicador_exp;
        let exp_award = ((npc_give_exp as f64 / npc_max_hp as f64) * damage as f64 * exp_mult as f64) as i64;

        let can_level = state.users.get(&caster_id)
            .map(|u| u.logged && u.level < MAX_LEVEL as i32)
            .unwrap_or(false);

        if can_level && exp_award > 0 {
            if let Some(user) = state.users.get_mut(&caster_id) {
                user.exp += exp_award;
            }
            state.send_msg_id(caster_id, 170, &format!("{}", exp_award));
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
            state.send_msg_id(caster_id, 846, ""); // Immune
            return;
        }
    }

    let paralisis_interval = state.intervals.paralizado;
    if let Some(npc) = state.get_npc_mut(npc_idx) {
        if spell.envenena {
            npc.veneno = true;
        }
        if spell.cura_veneno {
            npc.veneno = false;
        }
        if spell.paraliza {
            npc.paralyzed = true;
            // VB6: NPCs use the same paralysis duration as users (IntervaloParalizado)
            npc.counter_paralisis = paralisis_interval;
        }
        // VB6: RemoverParalisis does NOT work on NPCs (only users)
    }
}

/// VB6 spell damage/heal constants.
const SUPERANILLO: i32 = 700;
const LAUDELFICO: i32 = 1049;
const FLAUTAELFICA: i32 = 1050;
const LAUDMAGICO: i32 = 696;

/// Get the obj_index of the item equipped in a given slot (0 if none).
fn get_equipped_obj_index(state: &GameState, conn_id: ConnectionId, slot: usize) -> i32 {
    state.users.get(&conn_id).map(|u| {
        if slot > 0 && slot <= u.inventory.len() {
            u.inventory[slot - 1].obj_index
        } else { 0 }
    }).unwrap_or(0)
}

/// VB6 Porcentaje: (total * porc) / 100
fn porcentaje(total: i32, porc: i32) -> i32 {
    (total as i64 * porc as i64 / 100) as i32
}

/// Calculate spell damage with VB6 13.3 formula (modHechizos.bas lines 1890-1918).
/// Used for user→user and user→NPC.
fn calc_spell_damage(state: &GameState, caster_id: ConnectionId, spell: &crate::data::spells::SpellData) -> i32 {
    let mut damage = rand_range(spell.min_hp, spell.max_hp);

    // Level scaling: damage + damage * (3 * caster_level / 100)
    let level = state.users.get(&caster_id).map(|u| u.level).unwrap_or(1);
    damage += porcentaje(damage, 3 * level);

    // Staff damage bonus for Mages (VB6: StaffAffected check)
    if spell.staff_affected {
        let class = state.users.get(&caster_id).map(|u| u.class).unwrap_or_default();
        if class == PlayerClass::Mago {
            let weapon_slot = state.users.get(&caster_id).map(|u| u.equip.weapon).unwrap_or(0);
            let weapon_obj = get_equipped_obj_index(state, caster_id, weapon_slot);
            if weapon_obj > 0 {
                let staff_bonus = state.game_data.objects.get(weapon_obj as usize)
                    .map(|o| o.staff_damage_bonus).unwrap_or(0);
                damage = (damage as i64 * (staff_bonus as i64 + 70) / 100) as i32;
            } else {
                // No staff = 70% damage
                damage = (damage as f64 * 0.7) as i32;
            }
        }
    }

    // Bard/Druid lute bonus: +4% with Laud Élfico or Flauta Élfica
    let ring_slot = state.users.get(&caster_id).map(|u| u.equip.ring).unwrap_or(0);
    let ring_obj = get_equipped_obj_index(state, caster_id, ring_slot);
    if ring_obj == LAUDELFICO || ring_obj == FLAUTAELFICA {
        damage = (damage as f64 * 1.04) as i32;
    }

    damage
}

/// Calculate spell heal with VB6 13.3 formula (modHechizos.bas lines 1864-1866).
fn calc_spell_heal(state: &GameState, caster_id: ConnectionId, spell: &crate::data::spells::SpellData) -> i32 {
    let mut heal = rand_range(spell.min_hp, spell.max_hp);
    let level = state.users.get(&caster_id).map(|u| u.level).unwrap_or(1);
    heal += porcentaje(heal, 3 * level);
    heal
}

/// Subtract magic defense from equipped helmet + ring (VB6: DefensaMagicaMin/Max).
fn subtract_magic_defense(state: &GameState, target_id: ConnectionId, damage: i32) -> i32 {
    let (helmet_slot, ring_slot) = state.users.get(&target_id)
        .map(|u| (u.equip.helmet, u.equip.ring))
        .unwrap_or((0, 0));
    let mut d = damage;
    // Helmet magic defense
    let helmet_obj = get_equipped_obj_index(state, target_id, helmet_slot);
    if helmet_obj > 0 {
        if let Some(obj) = state.game_data.objects.get(helmet_obj as usize) {
            if obj.defensa_magica_max > 0 {
                d -= rand_range(obj.defensa_magica_min, obj.defensa_magica_max);
            }
        }
    }
    // Ring magic defense
    let ring_obj = get_equipped_obj_index(state, target_id, ring_slot);
    if ring_obj > 0 {
        if let Some(obj) = state.game_data.objects.get(ring_obj as usize) {
            if obj.defensa_magica_max > 0 {
                d -= rand_range(obj.defensa_magica_min, obj.defensa_magica_max);
            }
        }
    }
    d.max(0)
}

/// Apply property-type spell effects (HP, Mana, Stamina modifications).
/// VB6: HechizoPropUsuario (modHechizos.bas lines 1860-1920).
pub(super) async fn apply_spell_properties(
    state: &mut GameState,
    caster_id: ConnectionId,
    target_id: ConnectionId,
    spell: &crate::data::spells::SpellData,
) {
    // Capture target info for floating damage before mutable borrow
    let target_info = state.users.get(&target_id)
        .map(|u| (u.char_index.0, u.pos_map, u.pos_x, u.pos_y));

    let mut damage_dealt = 0i32;

    // HP effect
    if spell.sube_hp == 1 {
        // Heal — VB6 level-scaled
        let amount = calc_spell_heal(state, caster_id, spell);
        if let Some(target) = state.users.get_mut(&target_id) {
            target.min_hp = (target.min_hp + amount).min(target.max_hp);
        }
    } else if spell.sube_hp == 2 {
        // Damage — VB6 level-scaled + staff + lute, then subtract magic defense
        let base_damage = calc_spell_damage(state, caster_id, spell);
        let final_damage = subtract_magic_defense(state, target_id, base_damage);
        if let Some(target) = state.users.get_mut(&target_id) {
            target.min_hp -= final_damage;
        }
        damage_dealt = final_damage;
    }

    // Mana effect
    if spell.sube_mana == 1 {
        let amount = rand_range(spell.min_mana, spell.max_mana);
        if let Some(target) = state.users.get_mut(&target_id) {
            target.min_mana = (target.min_mana + amount).min(target.max_mana);
        }
    }

    // Stamina effect
    if spell.sube_sta == 1 {
        let amount = rand_range(spell.min_sta, spell.max_sta);
        if let Some(target) = state.users.get_mut(&target_id) {
            target.min_sta = (target.min_sta + amount).min(target.max_sta);
        }
    }

    // Hunger effect (VB6: SubeHam — 1=restore, 2=damage)
    if spell.sube_ham == 1 {
        let amount = rand_range(spell.min_ham, spell.max_ham);
        if let Some(target) = state.users.get_mut(&target_id) {
            target.min_ham = (target.min_ham + amount).min(target.max_ham);
        }
    } else if spell.sube_ham == 2 {
        let amount = rand_range(spell.min_ham, spell.max_ham);
        if let Some(target) = state.users.get_mut(&target_id) {
            target.min_ham = (target.min_ham - amount).max(0);
        }
    }

    // Thirst effect (VB6: SubeSed — 1=restore, 2=damage)
    if spell.sube_sed == 1 {
        let amount = rand_range(spell.min_sed, spell.max_sed);
        if let Some(target) = state.users.get_mut(&target_id) {
            target.min_agua = (target.min_agua + amount).min(target.max_agua);
        }
    } else if spell.sube_sed == 2 {
        let amount = rand_range(spell.min_sed, spell.max_sed);
        if let Some(target) = state.users.get_mut(&target_id) {
            target.min_agua = (target.min_agua - amount).max(0);
        }
    }

    // VB6: floating yellow damage number above target for damage spells
    if damage_dealt > 0 {
        if let Some((ci, map, tx, ty)) = target_info {
            state.send_chat_over_head_to(SendTarget::ToArea { map, x: tx, y: ty }, &format!("-{}", damage_dealt), ci as i16, 65535);
        }
    }

    // Send updated stats
    send_stats_hp(state, target_id).await;
    send_stats_mana(state, target_id).await;
    send_stats_sta(state, target_id).await;

    // Send hunger/thirst if affected
    if spell.sube_ham != 0 || spell.sube_sed != 0 {
        send_hunger_thirst(state, target_id).await;
    }

    // Check death from damage spell
    let hp = state.users.get(&target_id).map(|u| u.min_hp).unwrap_or(0);
    if hp <= 0 {
        user_die(state, target_id, Some(caster_id)).await;
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
        state.send_msg_id(caster_id, 31, "");
        return;
    }
    // VB6: Can't poison yourself
    if spell.envenena && caster_id == target_id {
        return;
    }

    // VB6: Super Anillo (700) blocks paralysis, immobilize, stun, blindness only
    // Does NOT block poison (envenena) or curse (maldicion)
    if spell.paraliza || spell.inmoviliza || spell.estupidez || spell.ceguera {
        let ring_slot = state.users.get(&target_id).map(|u| u.equip.ring).unwrap_or(0);
        let ring_obj = get_equipped_obj_index(state, target_id, ring_slot);
        if ring_obj == SUPERANILLO {
            state.send_console(caster_id, "El Super Anillo rechaza el hechizo.", font_index::INFO);
            return;
        }
    }

    // Pre-read target state for validation checks
    let target_dead = state.users.get(&target_id).map(|u| u.dead).unwrap_or(false);
    let target_navigating = state.users.get(&target_id).map(|u| u.navigating).unwrap_or(false);
    let target_map = state.users.get(&target_id).map(|u| u.pos_map).unwrap_or(0);

    // VB6: Cure poison — can't cure dead users
    if spell.cura_veneno && target_dead {
        state.send_console(caster_id, "¡El usuario está muerto!", font_index::INFO);
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
            state.send_console(caster_id, "La invisibilidad no funciona en este mapa.", font_index::INFO);
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
                target.counter_paralisis = state.intervals.paralizado;
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
        if spell.maldicion {
            target.cursed = true;
        }
        if spell.remover_maldicion {
            target.cursed = false;
        }
        if spell.bendicion {
            target.blessed = true;
        }
        if spell.estupidez {
            target.stunned = true;
            target.counter_stun = state.intervals.paralizado; // VB6: same duration
        }
        if spell.remover_estupidez {
            target.stunned = false;
            target.counter_stun = 0;
        }
        if spell.ceguera {
            target.blind = true;
            target.counter_blind = state.intervals.paralizado / 3; // VB6: IntervaloParalizado / 3
        }
        if spell.invisibilidad {
            target.invisible = true;
            target.hidden = true;
            target.counter_invisible = 0; // VB6: starts at 0, counts up to IntervaloInvisible
            send_invis = true;
        }
    }

    // Send PARADOK + PU outside borrow scope (VB6: lines 759-760)
    if send_paradok_on {
        let para_secs = (state.intervals.paralizado as f32 * 0.04) as i16;
        let pkt = binary_packets::write_paralize_ok(para_secs);
        state.send_bytes(target_id, &pkt);
        // PU forces client position to server-known position (prevents ghost movement)
        if let Some(u) = state.users.get(&target_id) {
            let pu = binary_packets::write_pos_update(u.pos_x as i16, u.pos_y as i16);
            state.send_bytes(target_id, &pu);
        }
    }
    if send_paradok_off {
        let pkt = binary_packets::write_paralize_ok(0);
        state.send_bytes(target_id, &pkt);
    }

    // Invisibility spell — remove from others' screens, tell self
    // VB6: skip SetInvisible packet if navigating (boat already hides char)
    // Clanmates see the character as semi-transparent instead of removing it.
    if send_invis && !target_navigating {
        if let Some(u) = state.users.get(&target_id) {
            let ci = u.char_index.0 as i16;
            let (map, x, y) = (u.pos_map, u.pos_x, u.pos_y);
            let invis_secs = (state.intervals.invisible as f32 * 0.04) as i16;
            let bp_remove = binary_packets::write_character_remove(ci);
            let nover_pkt = binary_packets::write_set_invisible(ci, true, invis_secs);
            // Collect area users and decide per-user
            let area_users = state.get_area_users(map, x, y, target_id);
            for other_id in area_users {
                if same_clan(state, target_id, other_id) {
                    // Clanmate: send SetInvisible so they see transparency, but don't remove
                    state.send_bytes(other_id, &nover_pkt);
                } else {
                    // Non-clanmate: remove character from their screen
                    state.send_bytes(other_id, &bp_remove);
                }
            }
            // Tell self about invisibility status
            state.send_bytes(target_id, &nover_pkt);
        }
    }

    // Resurrection spell — VB6 13.3: all classes resurrect immediately
    if spell.revivir {
        let target_dead = state.users.get(&target_id).map(|u| u.dead).unwrap_or(false);
        let target_seguro_resu = state.users.get(&target_id).map(|u| u.seguro_resu).unwrap_or(false);
        let target_time_revivir = state.users.get(&target_id).map(|u| u.time_revivir).unwrap_or(0);

        if target_dead {
            // Check resurrection safety (target opted out)
            if target_seguro_resu {
                state.send_msg_id(caster_id, 841, "");
                return;
            }
            // Check resurrection cooldown
            if target_time_revivir > 0 {
                state.send_msg_id(caster_id, 843, &format!("{}", target_time_revivir));
                return;
            }

            let caster_class = state.users.get(&caster_id)
                .map(|u| u.class)
                .unwrap_or_default();
            let caster_name = state.users.get(&caster_id)
                .map(|u| u.char_name.clone())
                .unwrap_or_default();

            // VB6 13.3: Caster must have full stamina to resurrect
            let (caster_min_sta, caster_max_sta) = state.users.get(&caster_id)
                .map(|u| (u.min_sta, u.max_sta))
                .unwrap_or((0, 1));
            if caster_min_sta != caster_max_sta {
                state.send_console(caster_id, "Necesitas tener toda tu energia para resucitar.", font_index::INFO);
                return;
            }

            // VB6 13.3: Instrument check — Bardo needs LAUDELFICO or LAUDMAGICO,
            // Druida needs FLAUTAELFICA or FLAUTAMAGICA equipped as ring
            let ring_slot = state.users.get(&caster_id).map(|u| u.equip.ring).unwrap_or(0);
            let ring_obj = get_equipped_obj_index(state, caster_id, ring_slot);
            const FLAUTAMAGICA: i32 = 208;

            if caster_class == PlayerClass::Bardo {
                if ring_obj != LAUDELFICO && ring_obj != LAUDMAGICO {
                    state.send_console(caster_id, "Necesitas un laúd para resucitar.", font_index::INFO);
                    return;
                }
            } else if caster_class == PlayerClass::Druida {
                if ring_obj != FLAUTAELFICA && ring_obj != FLAUTAMAGICA {
                    state.send_console(caster_id, "Necesitas una flauta para resucitar.", font_index::INFO);
                    return;
                }
            }

            let target_level = state.users.get(&target_id).map(|u| u.level).unwrap_or(1);

            // VB6 13.3: ALL classes resurrect immediately (no Cleric vs others branching)
            revive_user(state, target_id).await;
            if let Some(target) = state.users.get_mut(&target_id) {
                target.min_hp = target.max_hp;
                // VB6 13.3: reset stats on resurrection
                target.min_ham = 0;
                target.min_agua = 0;
                target.min_mana = 0;
                target.min_sta = 0;
            }
            send_stats_hp(state, target_id).await;
            state.send_msg_id(target_id, 749, &caster_name);

            // VB6: +500 Noble rep if target is not criminal and not self-res
            if caster_id != target_id {
                let target_criminal = state.users.get(&target_id).map(|u| u.criminal).unwrap_or(false);
                if !target_criminal {
                    const MAX_REP: i32 = 500_000;
                    if let Some(caster) = state.users.get_mut(&caster_id) {
                        caster.rep_noble = (caster.rep_noble + 500).min(MAX_REP);
                    }
                    state.send_console(caster_id, "Los Dioses te sonrien, has ganado 500 puntos de nobleza!", font_index::INFO);
                }
            }

            // Caster pays HP cost — VB6 13.3: hp * (1 - target_level * 0.015)
            // Allow HP to reach 0 (caster can die from resurrection cost)
            if let Some(caster) = state.users.get_mut(&caster_id) {
                let new_hp = ((caster.min_hp as f64) * (1.0 - target_level as f64 * 0.015)) as i32;
                caster.min_hp = new_hp;
            }
            send_stats_hp(state, caster_id).await;

            // If caster HP <= 0, caster dies from the resurrection cost
            let caster_hp = state.users.get(&caster_id).map(|u| u.min_hp).unwrap_or(0);
            if caster_hp <= 0 {
                user_die(state, caster_id, None).await;
            }
        }
    }
}

/// Apply attribute buff spells (SubeAgilidad, SubeFuerza, SubeCA).
/// VB6: modHechizos.bas — SubeAgilidad=1 buffs, =2 debuffs. SubeFuerza/SubeCA same pattern.
/// Buffs are temporary: DuracionEfecto ticks, then attributes restored from backup.
pub(super) async fn apply_spell_buffs(
    state: &mut GameState,
    _caster_id: ConnectionId,
    target_id: ConnectionId,
    spell: &crate::data::spells::SpellData,
) {
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
                // Buff: increase agility, VB6: cap at base*2 (MAXATRIBUTOS in VB6)
                let max_cap = (target.attributes_backup[1] * 2).min(50);
                target.attributes[1] = (target.attributes[1] + amount).min(max_cap); // [1] = Agi
                target.duracion_efecto = 1200; // VB6: DuracionEfecto = 1200
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
                let max_cap = (target.attributes_backup[0] * 2).min(50);
                target.attributes[0] = (target.attributes[0] + amount).min(max_cap); // [0] = Str
                target.duracion_efecto = 1200;
            } else {
                target.attributes[0] = (target.attributes[0] - amount).max(MIN_ATTR);
                target.duracion_efecto = 700;
            }
            target.tomo_pocion = true;
        }
    }

    // SubeCA (Charisma): 1=buff, 2=debuff
    if spell.sube_carisma > 0 {
        let amount = rand_range(spell.min_carisma, spell.max_carisma);
        if let Some(target) = state.users.get_mut(&target_id) {
            if !target.tomo_pocion {
                target.attributes_backup = target.attributes;
            }
            if spell.sube_carisma == 1 {
                let max_cap = (target.attributes_backup[3] * 2).min(50);
                target.attributes[3] = (target.attributes[3] + amount).min(max_cap); // [3] = Cha
                target.duracion_efecto = 1200;
            } else {
                target.attributes[3] = (target.attributes[3] - amount).max(MIN_ATTR);
                target.duracion_efecto = 700;
            }
            target.tomo_pocion = true;
        }
    }
}

/// Apply invocation spell — spawn NPCs as pets.
/// VB6: HechizoInvocacion (modHechizos.bas). Max 3 pets, singleton elementals.
const MAX_MASCOTAS: i32 = 3;
/// VB6: IntervaloInvocacion = 1001 ticks at 50ms = ~50 seconds
const ELEMENTAL_LIFETIME_MS: i64 = 50_000;

pub(super) async fn apply_spell_invocation(
    state: &mut GameState,
    caster_id: ConnectionId,
    spell: &crate::data::spells::SpellData,
) {
    let (map, x, y, nro_mascotas) = match state.users.get(&caster_id) {
        Some(u) if u.logged => (u.pos_map, u.pos_x, u.pos_y, u.nro_mascotas),
        _ => return,
    };

    // VB6: InvocarSinEfecto — block summoning on certain maps
    let invocar_blocked = state.game_data.maps.get(map as usize)
        .and_then(|m| m.as_ref())
        .map(|m| m.info.invocar_sin_efecto)
        .unwrap_or(false);
    if invocar_blocked {
        state.send_console(caster_id, "No puedes invocar criaturas en este mapa.", font_index::INFO);
        return;
    }

    if nro_mascotas >= MAX_MASCOTAS {
        state.send_console(caster_id, "No puedes invocar mas criaturas.", font_index::INFO);
        return;
    }

    let npc_num = spell.num_npc;
    let cant = spell.cant;
    if npc_num <= 0 || cant <= 0 {
        return;
    }

    // VB6: Elemental singleton — if already summoned, warp it to caster + reset lifetime
    {
        use crate::game::npc::{ELEMENTAL_AGUA, ELEMENTAL_FUEGO, ELEMENTAL_TIERRA};
        let already_has = match state.users.get(&caster_id) {
            Some(user) => match npc_num {
                ELEMENTAL_AGUA => user.ele_de_agua,
                ELEMENTAL_FUEGO => user.ele_de_fuego,
                ELEMENTAL_TIERRA => user.ele_de_tierra,
                _ => false,
            },
            None => false,
        };
        if already_has {
            // Find the existing elemental and warp it to caster
            let existing_idx = state.users.get(&caster_id)
                .and_then(|u| {
                    for slot in 0..3 {
                        if u.mascotas_type[slot] == npc_num && u.mascotas_index[slot] > 0 {
                            return Some(u.mascotas_index[slot]);
                        }
                    }
                    None
                });
            if let Some(npc_idx) = existing_idx {
                // Remove from old tile
                let old_data = state.get_npc(npc_idx).map(|n| (n.char_index, n.map, n.x, n.y));
                if let Some((ci, old_map, old_x, old_y)) = old_data {
                    let grid = state.world.grid_mut(old_map);
                    if let Some(tile) = grid.tile_mut(old_x, old_y) {
                        if tile.npc_index == npc_idx as i32 { tile.npc_index = 0; }
                    }
                    let remove_pkt = binary_packets::write_character_remove(ci.0 as i16);
                    state.send_data_bytes(SendTarget::ToArea { map: old_map, x: old_x, y: old_y }, &remove_pkt);
                }
                // Set new position + reset lifetime
                if let Some(npc) = state.get_npc_mut(npc_idx) {
                    npc.map = map;
                    npc.x = x;
                    npc.y = y;
                    npc.tiempo_existencia_ms = ELEMENTAL_LIFETIME_MS;
                    npc.target = None;
                    npc.target_npc = 0;
                }
                // Place on new tile
                let grid = state.world.grid_mut(map);
                if let Some(tile) = grid.tile_mut(x, y) {
                    tile.npc_index = npc_idx as i32;
                }
                // Broadcast creation at new position
                let cc_pkt = state.get_npc(npc_idx).map(|n| n.build_cc_binary());
                if let Some(pkt) = cc_pkt {
                    state.send_data_bytes(SendTarget::ToArea { map, x, y }, &pkt);
                }
            }
            return;
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
                        user.nro_mascotas = user.nro_mascotas + 1;
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

            // Set NPC owner + lifetime for elementals
            if let Some(npc) = state.get_npc_mut(npc_idx) {
                npc.maestro_user = Some(caster_id);
                use crate::game::npc::{ELEMENTAL_AGUA, ELEMENTAL_FUEGO, ELEMENTAL_TIERRA};
                if npc_num == ELEMENTAL_AGUA || npc_num == ELEMENTAL_FUEGO || npc_num == ELEMENTAL_TIERRA {
                    npc.tiempo_existencia_ms = ELEMENTAL_LIFETIME_MS;
                }
            }

            // Broadcast NPC creation using its CC packet
            let cc_pkt = state.get_npc(npc_idx).map(|n| n.build_cc_binary());
            if let Some(pkt) = cc_pkt {
                state.send_data_bytes(SendTarget::ToArea { map, x, y }, &pkt);
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
    state.send_data_bytes(SendTarget::ToMapButIndex { conn_id: caster_id, map: cur_map }, &qdl_pkt);
    let bp_pkt = binary_packets::write_character_remove(char_index.0 as i16);
    state.send_data_bytes(SendTarget::ToMapButIndex { conn_id: caster_id, map: cur_map }, &bp_pkt);

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
    state.send_bytes(caster_id, &cm_pkt);
    let pu_pkt = binary_packets::write_pos_update(dest_x as i16, dest_y as i16);
    state.send_bytes(caster_id, &pu_pkt);
    let midi_pkt = binary_packets::write_play_midi(music as u8);
    state.send_bytes(caster_id, &midi_pkt);
    // Map name
    let mn_pkt = binary_packets::write_map_name(&map_name);
    state.send_bytes(caster_id, &mn_pkt);

    // Send CC to new area
    let cc_pkt = state.users.get(&caster_id).map(|u| u.build_cc_binary());
    if let Some(pkt) = cc_pkt {
        state.send_data_bytes(SendTarget::ToAreaButIndex { conn_id: caster_id, map: dest_map, x: dest_x, y: dest_y }, &pkt);
    }

    // Drain all mana (VB6 sets mana to 0 on teleport)
    if let Some(user) = state.users.get_mut(&caster_id) {
        user.min_mana = 0;
    }
    send_stats_mana(state, caster_id).await;
}

// =====================================================================
// VB6 Mimetiza (Druid mimicry — body swap)
// =====================================================================

/// VB6: Mimetiza on user target — caster copies target's body, head, weapon, shield, helmet.
async fn apply_mimetiza_user(state: &mut GameState, caster_id: ConnectionId, target_id: ConnectionId) {
    // Read target appearance
    let target_look = match state.users.get(&target_id) {
        Some(t) => (t.body, t.head, t.weapon_anim, t.shield_anim, t.casco_anim),
        None => return,
    };
    let (t_body, t_head, t_weapon, t_shield, t_helmet) = target_look;

    // Save original and apply new appearance
    if let Some(caster) = state.users.get_mut(&caster_id) {
        if !caster.mimetizado {
            caster.char_mimetizado_body = caster.body;
            caster.char_mimetizado_head = caster.head;
            caster.char_mimetizado_weapon = caster.weapon_anim;
            caster.char_mimetizado_shield = caster.shield_anim;
            caster.char_mimetizado_helmet = caster.casco_anim;
        }
        caster.body = t_body;
        caster.head = t_head;
        caster.weapon_anim = t_weapon;
        caster.shield_anim = t_shield;
        caster.casco_anim = t_helmet;
        caster.mimetizado = true;
    }

    // Send CP to area so everyone sees the change
    if let Some(u) = state.users.get(&caster_id) {
        let cp = binary_packets::write_character_change(
            u.char_index.0 as i16, u.body as i16, u.head as i16, u.heading as u8,
            u.weapon_anim as i16, u.shield_anim as i16, u.casco_anim as i16, 0, 0,
        );
        let (map, x, y) = (u.pos_map, u.pos_x, u.pos_y);
        state.send_data_bytes(SendTarget::ToArea { map, x, y }, &cp);
    }
}

/// VB6: Mimetiza on NPC target — Druid only, copies NPC body+head, clears weapon/shield/helmet.
async fn apply_mimetiza_npc(state: &mut GameState, caster_id: ConnectionId, npc_idx: usize) {
    // Read NPC appearance
    let npc_look = match state.get_npc(npc_idx) {
        Some(n) => (n.body, n.head),
        None => return,
    };
    let (n_body, n_head) = npc_look;

    // Save original and apply NPC appearance
    if let Some(caster) = state.users.get_mut(&caster_id) {
        if !caster.mimetizado {
            caster.char_mimetizado_body = caster.body;
            caster.char_mimetizado_head = caster.head;
            caster.char_mimetizado_weapon = caster.weapon_anim;
            caster.char_mimetizado_shield = caster.shield_anim;
            caster.char_mimetizado_helmet = caster.casco_anim;
        }
        caster.body = n_body;
        caster.head = n_head;
        // VB6: Clear weapon/shield/helmet when mimicking NPC
        caster.weapon_anim = 0;
        caster.shield_anim = 0;
        caster.casco_anim = 0;
        caster.mimetizado = true;
    }

    // Send CP to area
    if let Some(u) = state.users.get(&caster_id) {
        let cp = binary_packets::write_character_change(
            u.char_index.0 as i16, u.body as i16, u.head as i16, u.heading as u8,
            0, 0, 0, 0, 0,
        );
        let (map, x, y) = (u.pos_map, u.pos_x, u.pos_y);
        state.send_data_bytes(SendTarget::ToArea { map, x, y }, &cp);
    }
}
