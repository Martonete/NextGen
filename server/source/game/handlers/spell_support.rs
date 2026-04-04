//! Support spell helpers: buff/debuff, invocations, summon pet, teleport, mimicry, lookat.

use crate::net::ConnectionId;
use crate::game::types::{GameState, SendTarget};
use crate::protocol::{font_index, binary_packets};
use super::super::common::*;

// =====================================================================
// Constants
// =====================================================================

pub(super) const APOCALIPSIS_SPELL_INDEX: i32 = 25;
/// VB6: Max simultaneous pets.
pub(super) const MAX_MASCOTAS: i32 = 3;
/// VB6: IntervaloInvocacion = 1001 ticks at 50ms = ~50 seconds
pub(super) const ELEMENTAL_LIFETIME_MS: i64 = 50_000;

// =====================================================================
// Lookat helpers (used during spell targeting)
// =====================================================================

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

// =====================================================================
// Buff / debuff spells
// =====================================================================

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

// =====================================================================
// Invocation / summon / teleport
// =====================================================================

/// Apply invocation spell — spawn NPCs as pets.
/// VB6: HechizoInvocacion (modHechizos.bas). Max 3 pets, singleton elementals.
pub(super) async fn apply_spell_invocation(
    state: &mut GameState,
    caster_id: ConnectionId,
    spell: &crate::data::spells::SpellData,
) {
    let (map, x, y, nro_mascotas) = match state.users.get(&caster_id) {
        Some(u) if u.logged => (u.pos_map, u.pos_x, u.pos_y, u.nro_mascotas),
        _ => return,
    };

    // Zone-aware invocation check (Zone > Map)
    if is_invocar_blocked_at(state, map, x, y) {
        state.send_console(caster_id, "No puedes invocar criaturas aqui.", font_index::INFO);
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
// Mimetiza (Druid mimicry — body swap)
// =====================================================================

/// VB6: Mimetiza on user target — caster copies target's body, head, weapon, shield, helmet.
pub(super) async fn apply_mimetiza_user(state: &mut GameState, caster_id: ConnectionId, target_id: ConnectionId) {
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
pub(super) async fn apply_mimetiza_npc(state: &mut GameState, caster_id: ConnectionId, npc_idx: usize) {
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

/// M8: VB6: HechizoTerrenoEstado — RemueveInvisibilidadParcial (Detect Hidden) terrain spell.
/// Scans an ±8-tile radius around the target tile. For each spell-invisible (non-admin-invisible)
/// player found, sends the spell FX to the caster's area showing their position.
/// VB6: Only sends FX — does NOT remove invisibility, does NOT notify the invisible player.
pub(super) async fn apply_terrain_detect_hidden(
    state: &mut GameState,
    caster_id: ConnectionId,
    target_x: i32,
    target_y: i32,
    map: i32,
    spell: &crate::data::spells::SpellData,
) {
    if spell.fx_grh <= 0 {
        return;
    }

    // Collect spell-invisible users within ±8 tile radius on the same map
    // VB6: checks flags.invisible = 1 AND flags.AdminInvisible = 0
    let targets: Vec<i32> = state.users.values()
        .filter(|u| {
            u.logged && !u.dead && u.pos_map == map
                && u.invisible && !u.admin_invisible
                && (u.pos_x - target_x).abs() <= 8
                && (u.pos_y - target_y).abs() <= 8
        })
        .map(|u| u.char_index.0)
        .collect();

    // VB6: SendData(ToPCArea, caster, CreateFX(invis_user.CharIndex, FXgrh, loops))
    // Sends FX to caster's area, using the invisible user's CharIndex
    let caster_pos = state.users.get(&caster_id).map(|u| (u.pos_map, u.pos_x, u.pos_y));
    if let Some((cmap, cx, cy)) = caster_pos {
        for char_idx in &targets {
            let fx = binary_packets::write_create_fx(*char_idx as i16, spell.fx_grh as i16, spell.loops as i16);
            state.send_data_bytes(SendTarget::ToArea { map: cmap, x: cx, y: cy }, &fx);
        }
    }
}

