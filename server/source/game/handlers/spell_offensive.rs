//! Offensive and damage spell helpers: info packets, property effects, status effects.

use crate::net::ConnectionId;
use crate::game::class_race::PlayerClass;
use crate::game::types::{GameState, SendTarget};
use crate::protocol::{font_index, binary_packets};
use crate::data::experience::MAX_LEVEL;
use super::common::*;
use super::super::{user_die, npc_die, check_user_level, revive_user};

// =====================================================================
// Item-index constants (shared with spell_support.rs via pub(super))
// =====================================================================

pub(super) const SUPERANILLO: i32 = 700;
pub(super) const LAUDELFICO: i32 = 1049;
pub(super) const FLAUTAELFICA: i32 = 1050;
pub(super) const LAUDMAGICO: i32 = 696;

// =====================================================================
// Small helper utilities
// =====================================================================

/// Get the obj_index of the item equipped in a given slot (0 if none).
pub(super) fn get_equipped_obj_index(state: &GameState, conn_id: ConnectionId, slot: usize) -> i32 {
    state.users.get(&conn_id).map(|u| {
        if slot > 0 && slot <= u.inventory.len() {
            u.inventory[slot - 1].obj_index
        } else { 0 }
    }).unwrap_or(0)
}

/// VB6 Porcentaje: (total * porc) / 100
pub(super) fn porcentaje(total: i32, porc: i32) -> i32 {
    (total as i64 * porc as i64 / 100) as i32
}

/// Calculate spell damage with VB6 13.3 formula (modHechizos.bas lines 1890-1918).
/// Used for user→user and user→NPC.
pub(super) fn calc_spell_damage(state: &GameState, caster_id: ConnectionId, spell: &crate::data::spells::SpellData) -> i32 {
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
pub(super) fn calc_spell_heal(state: &GameState, caster_id: ConnectionId, spell: &crate::data::spells::SpellData) -> i32 {
    let mut heal = rand_range(spell.min_hp, spell.max_hp);
    let level = state.users.get(&caster_id).map(|u| u.level).unwrap_or(1);
    heal += porcentaje(heal, 3 * level);
    heal
}

/// Subtract magic defense from equipped helmet + ring (VB6: DefensaMagicaMin/Max).
pub(super) fn subtract_magic_defense(state: &GameState, target_id: ConnectionId, damage: i32) -> i32 {
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

// =====================================================================
// InfoHechizo — FX + messages
// =====================================================================

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

// =====================================================================
// NPC spell effects
// =====================================================================

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
        npc.min_hp = npc.min_hp.saturating_sub(damage);
        npc.damage_received.push((caster_id, damage));
    }

    // Detect death synchronously (before any await — prevents double-death race)
    let npc_is_dead = state.get_npc(npc_idx).map(|n| n.min_hp < 1).unwrap_or(false);
    if npc_is_dead {
        // Mark as dead immediately to prevent race with concurrent casters
        if let Some(npc) = state.get_npc_mut(npc_idx) {
            npc.min_hp = 0;
        }
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

    // Execute death after exp (NPC already marked dead above — safe from race)
    if npc_is_dead {
        let death_data = state.get_npc(npc_idx).map(|n| (n.give_exp, n.give_gld_min, n.give_gld_max));
        if let Some((give_exp, give_gld_min, give_gld_max)) = death_data {
            npc_die(state, npc_idx, caster_id, give_exp, give_gld_min, give_gld_max).await;
        }
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

// =====================================================================
// User spell effects
// =====================================================================

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
            target.min_hp = target.min_hp.saturating_sub(final_damage);
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
    // Zone-aware invisibility check (Zone > Map)
    if spell.invisibilidad {
        let (tx, ty) = state.users.get(&target_id).map(|u| (u.pos_x, u.pos_y)).unwrap_or((0, 0));
        if is_invi_blocked_at(state, target_map, tx, ty) {
            state.send_console(caster_id, "La invisibilidad no funciona aqui.", font_index::INFO);
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

            // Check if the HP cost would kill the caster before applying resurrection.
            // Cost formula: caster loses HP * (1 - target_level * 0.015) of current HP,
            // meaning new_hp = current_hp * (1 - target_level * 0.015).
            // If new_hp <= 0 the caster would die — reject early to avoid inconsistent state.
            let caster_hp = state.users.get(&caster_id).map(|u| u.min_hp).unwrap_or(0);
            let new_caster_hp = ((caster_hp as f64) * (1.0 - target_level as f64 * 0.015)) as i32;
            if new_caster_hp <= 0 {
                state.send_console(caster_id, "No tienes suficiente vida para lanzar este hechizo.", font_index::INFO);
                return;
            }

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
                    const MAX_REP: i32 = 6_000_000;
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
