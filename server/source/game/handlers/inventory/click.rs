//! Inventory click handlers: left click (look at), right click (interact), doors, forum, safe toggle.
//! Split from inventory.rs for file size management.

use crate::net::ConnectionId;
use crate::game::types::{GameState, SendTarget};
use crate::game::world;
use crate::protocol::font_index;
use crate::protocol::binary_packets;
use crate::game::handlers::common::*;
use crate::game::handlers::{
    warp_user, iniciar_comercio_npc, iniciar_banco, iniciar_banco_clan,
};
use super::doors::{accion_para_puerta, accion_para_foro};

pub(crate) async fn handle_left_click(state: &mut GameState, conn_id: ConnectionId, x: i32, y: i32) {
    do_lookat_tile(state, conn_id, x, y).await;
}

/// Core LookatTile logic (VB6: GameLogic.bas:505-1115).
/// Called from LC handler and WLC Magia handler (VB6 calls LookatTile before LanzarHechizo).
pub(crate) async fn do_lookat_tile(state: &mut GameState, conn_id: ConnectionId, x: i32, y: i32) {
    let (map, user_x, user_y, my_privileges, my_survival_skill) = match state.users.get(&conn_id) {
        Some(u) if u.logged => (u.pos_map, u.pos_x, u.pos_y, u.privileges,
            u.skills.get(8).copied().unwrap_or(0)), // eSkill.Supervivencia = 9 (1-based) → skills[8] (0-based)
        _ => return,
    };

    // VB6: flags.TargetMap/X/Y
    if let Some(user) = state.users.get_mut(&conn_id) {
        user.target_x = x;
        user.target_y = y;
        user.target_map = map;
    }

    // Range check
    if (x - user_x).abs() > world::MIN_X_BORDER || (y - user_y).abs() > world::MIN_Y_BORDER {
        return;
    }

    let is_gm = my_privileges >= crate::game::types::privilege_level::SEMIDIOS;
    let is_user = my_privileges == 0;
    let mut found_something = false;
    let mut found_char: u8 = 0; // 0=none, 1=user, 2=npc
    let mut temp_char_index_user: ConnectionId = 0;
    let mut temp_char_index_npc: usize = 0;

    // ========== OBJECT / DOOR DETECTION (VB6 lines 520-759) ==========
    // Check exact tile first, then nearby tiles for doors
    let obj_tile = state.world.grid(map).and_then(|g| g.tile(x, y))
        .map(|t| t.ground_item.obj_index).unwrap_or(0);
    if obj_tile > 0 {
        // Set target obj
        if let Some(user) = state.users.get_mut(&conn_id) {
            user.target_obj_map = map;
            user.target_obj_x = x;
            user.target_obj_y = y;
        }
        found_something = true;
        // Display object info
        let obj_info = state.get_object(obj_tile).map(|o| (o.name.clone(), o.index));
        if let Some((name, idx)) = obj_info {
            let amount = state.world.grid(map).and_then(|g| g.tile(x, y))
                .map(|t| t.ground_item.amount).unwrap_or(0);
            let msg = if !is_user {
                if amount > 1 {
                    format!("{} - {} - {}", name, amount, idx)
                } else {
                    format!("{} - {}", name, idx)
                }
            } else {
                if amount > 1 {
                    format!("{} - {}", name, amount)
                } else {
                    name.to_string()
                }
            };
            state.send_console(conn_id, &msg, font_index::INFO);
        }
    }

    // ========== CHARACTER DETECTION (VB6 lines 779-800) ==========
    // VB6 checks Y+1 FIRST, then Y for chars
    let grid_height = state.world.grid(map).map(|g| g.height).unwrap_or(100);
    if found_char == 0 {
        for &check_y in &[y + 1, y] {
            if check_y < 1 || check_y > grid_height { continue; }
            // Check for user
            if found_char == 0 {
                let tile_user = state.world.grid(map)
                    .and_then(|g| g.tile(x, check_y))
                    .and_then(|t| t.user_conn);
                if let Some(tc) = tile_user {
                    if state.users.get(&tc).map(|u| u.logged).unwrap_or(false) {
                        temp_char_index_user = tc;
                        found_char = 1;
                    }
                }
            }
            // Check for NPC on same tile
            if found_char == 0 {
                let tile_npc = state.world.grid(map)
                    .and_then(|g| g.tile(x, check_y))
                    .map(|t| t.npc_index)
                    .unwrap_or(0);
                if tile_npc > 0 {
                    temp_char_index_npc = tile_npc as usize;
                    found_char = 2;
                }
            }
            if found_char != 0 { break; }
        }
    }

    // ========== USER DISPLAY (VB6 lines 807-981) — EXACT REPLICA ==========
    if found_char == 1 {
        let target = temp_char_index_user;
        let info = state.users.get(&target).map(|t| {
            (t.char_name.clone(), t.level, t.guild_index, t.min_hp, t.max_hp,
             t.dead, t.privileges, t.criminal, t.armada_real, t.fuerzas_caos,
             t.desc.clone(), t.char_index.0,
             t.recompensas_real, t.recompensas_caos)
        });

        if let Some((name, level, guild_idx, min_hp, max_hp, dead, priv_target,
                      criminal, armada, caos, desc, char_idx,
                      recomp_real, recomp_caos)) = info {

            let mut stat = String::new();
            let limite_newbie = 9;

            // VB6: EsNewbie tag
            if level <= limite_newbie {
                stat.push_str(" <NEWBIE>");
            }

            // VB6: Guild tag
            if guild_idx > 0 {
                let gn = state.users.get(&target).map(|u| u.guild_name.clone()).unwrap_or_default();
                if !gn.is_empty() {
                    stat.push_str(&format!(" <{}>", gn));
                }
            }

            // "Ves a <name><tags>"
            stat = format!("Ves a {}{}", name, stat);

            // VB6: GM info
            if my_privileges > 0 {
                stat.push_str(&format!(" <UI:{}>", char_idx));
                stat.push_str(&format!(" ({}/{})", min_hp, max_hp));
            }

            // VB6: Faction tags with titles
            if armada {
                let titulo = titulo_real(recomp_real);
                stat.push_str(&format!(" <Alianza Imperial> <{}>", titulo));
            } else if caos {
                let titulo = titulo_caos(recomp_caos);
                stat.push_str(&format!(" <Horda Infernal> <{}>", titulo));
            }

            // VB6: Description
            if desc.len() > 1 {
                stat.push_str(&format!(" - {}", desc));
            }

            // VB6: Health status in brackets (lines 863-876)
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

            // Label + font_index by privilege hierarchy (VB6 lines 895-956)
            let fi = if priv_target > 11 {
                stat.push_str(" <Administrador>");
                font_index::BLANCO
            } else if priv_target > 10 {
                stat.push_str(" <Sub Administrador>");
                font_index::AMARILLO
            } else if priv_target > 9 {
                stat.push_str(" <Developer>");
                font_index::VERDE
            } else if priv_target > 8 {
                stat.push_str(" <Director de Game Master>");
                font_index::SERVER
            } else if priv_target > 7 {
                stat.push_str(" <Game Master> <Gran Dios>");
                font_index::SERVER
            } else if priv_target > 3 {
                stat.push_str(" <Game Master> <Dios>");
                font_index::CELESTE
            } else if priv_target > 2 {
                stat.push_str(" <Event Master>");
                font_index::GRIS
            } else if priv_target > 1 {
                stat.push_str(" <Game Master> <Semi Dios>");
                font_index::INFO
            } else if priv_target > 0 {
                stat.push_str(" <Game Master> <Consejero>");
                font_index::SERVER
            } else if level <= limite_newbie {
                font_index::NEWBIE
            } else if armada {
                font_index::CIUDADANO
            } else if caos {
                font_index::ROJO
            } else if criminal {
                stat.push_str(" <CRIMINAL>");
                font_index::ROJO
            } else {
                stat.push_str(" <CIUDADANO>");
                font_index::CIUDADANO
            };

            state.send_console(conn_id, &stat, fi);

            found_something = true;
            if let Some(user) = state.users.get_mut(&conn_id) {
                user.target_user = target;
                // Don't reset target_npc while in commerce (VB6: form is modal)
                if !user.comerciando {
                    user.target_npc = 0;
                }
            }
        }
    }

    // ========== NPC DISPLAY (VB6 lines 983-1083) — EXACT REPLICA ==========
    if found_char == 2 {
        let npc_idx = temp_char_index_npc;
        let npc_data = state.get_npc(npc_idx).map(|npc| {
            (npc.is_alive(), npc.name.clone(), npc.desc.clone(),
             npc.char_index, npc.min_hp, npc.max_hp, npc.npc_number,
             npc.npc_type)
        });

        if let Some((alive, npc_name, npc_desc, npc_char_index, npc_min_hp, npc_max_hp, npc_num, _npc_type)) = npc_data {
            if alive {
                // VB6: GM gets detailed NPC info (line 987)
                if my_privileges > 0 {
                    state.send_console(conn_id,
                        &format!("Nombre : {} /  Vida : {}/{} Numero de NPC : {}", npc_name, npc_min_hp, npc_max_hp, npc_num),
                        font_index::NPCSX);
                }

                // VB6: Health status based on Survival skill (lines 993-1036)
                let estatus = if is_gm {
                    format!("{}/{}", npc_min_hp, npc_max_hp)
                } else {
                    npc_health_by_survival(npc_min_hp, npc_max_hp, my_survival_skill)
                };

                // VB6: NPC display (lines 1038-1076)
                if npc_desc.len() > 1 {
                    // GM gets extra info line before desc
                    if is_gm {
                        state.send_console(conn_id,
                            &format!("Nombre: {} Vida: {}/{} Numero de NPC: {} Indice: {}", npc_name, npc_min_hp, npc_max_hp, npc_num, npc_idx),
                            font_index::NPCSX);
                    }
                    // Speech bubble with desc (overhead white text)
                    state.send_chat_over_head_to(SendTarget::ToIndex(conn_id), &npc_desc, npc_char_index.0 as i16, 16777215);
                } else {
                    // No desc → show name + health status
                    state.send_msg_id(conn_id, 674, &format!("{}@{}", npc_name, estatus));
                }

                found_something = true;
                if let Some(user) = state.users.get_mut(&conn_id) {
                    user.target_npc_idx = npc_idx;
                    user.target_npc = npc_idx;
                    user.target_user = 0;
                }
            }
        }
    }

    // ========== CLEANUP (VB6 lines 1085-1115) ==========
    // Don't reset target_npc while in commerce mode (VB6: commerce form is modal,
    // so LC packets can't arrive during commerce. Godot client is non-modal.)
    let comerciando = state.users.get(&conn_id).map(|u| u.comerciando).unwrap_or(false);
    if found_char == 0 {
        if let Some(user) = state.users.get_mut(&conn_id) {
            if !comerciando { user.target_npc = 0; }
            user.target_npc_idx = 0;
            user.target_user = 0;
        }
    }
    if !found_something {
        if let Some(user) = state.users.get_mut(&conn_id) {
            if !comerciando { user.target_npc = 0; }
            user.target_npc_idx = 0;
            user.target_user = 0;
        }
    }
}

/// VB6 TituloReal (ModFacciones.bas:369)
pub(crate) fn titulo_real(recompensas: i32) -> &'static str {
    match recompensas {
        0 | 1 => "Servidor del Rey",
        2 => "Soldado Imperial",
        3 => "Protector del Imperio",
        4 => "Maestro de la Luz",
        5 => "Caballero de la Luz",
        _ => "Servidor del Rey",
    }
}

/// VB6 TituloCaos (ModFacciones.bas:701)
pub(crate) fn titulo_caos(recompensas: i32) -> &'static str {
    match recompensas {
        0 | 1 => "Servidor del Demonio",
        2 => "Mercenario de la Oscuridad",
        3 => "General de los Infiernos",
        4 => "Maestro de la Oscuridad",
        5 => "Caballero de la Oscuridad",
        _ => "Servidor del Demonio",
    }
}

/// VB6 NPC health status based on Survival skill (GameLogic.bas:993-1036).
///
/// Replicates VB6 Acciones.bas Supervivencia logic exactly:
/// - skill <= 10: "Dudoso"
/// - skill <= 20: >50% → "Sano", else "Herido"
/// - skill <= 30: >75% → "Sano", >50% → "Herido", else "Malherido"
/// - skill <= 40: >75% → "Sano", >50% → "Herido", >25% → "Malherido", else "Agonizando"
/// - skill 41-59: fine-grained 5%/10%/25%/50%/75% thresholds
/// - skill >= 60: exact "Tiene X puntos de vida"
pub(crate) fn npc_health_by_survival(min_hp: i32, max_hp: i32, survival_skill: i32) -> String {
    if max_hp <= 0 { return "Intacto".to_string(); }

    // VB6: NpcHP% = Int(Npc(NpcIndex).MIN_HP * 100 / Npc(NpcIndex).MAX_HP)
    let npc_hp_pct = (min_hp as f64 / max_hp as f64 * 100.0) as i32;

    if survival_skill >= 60 {
        // VB6: "Tiene " & Npc(NpcIndex).MIN_HP & " puntos de vida"
        return format!("Tiene {} puntos de vida", min_hp);
    } else if survival_skill >= 41 {
        // VB6 skill < 60 branch — 5-tier detail with 5%/10%/25%/50%/75% thresholds
        if npc_hp_pct <= 5 { "Agonizando".to_string() }
        else if npc_hp_pct <= 10 { "Casi muerto".to_string() }
        else if npc_hp_pct <= 25 { "Muy malherido".to_string() }
        else if npc_hp_pct <= 50 { "Malherido".to_string() }
        else if npc_hp_pct <= 75 { "Herido".to_string() }
        else { "Sano".to_string() }
    } else if survival_skill <= 40 && survival_skill >= 31 {
        // VB6 skill <= 40 branch
        if npc_hp_pct <= 25 { "Agonizando".to_string() }
        else if npc_hp_pct <= 50 { "Malherido".to_string() }
        else if npc_hp_pct <= 75 { "Herido".to_string() }
        else { "Sano".to_string() }
    } else if survival_skill <= 30 && survival_skill >= 21 {
        // VB6 skill <= 30 branch
        if npc_hp_pct <= 50 { "Malherido".to_string() }
        else if npc_hp_pct <= 75 { "Herido".to_string() }
        else { "Sano".to_string() }
    } else if survival_skill <= 20 && survival_skill >= 11 {
        // VB6 skill <= 20 branch
        if npc_hp_pct <= 50 { "Herido".to_string() }
        else { "Sano".to_string() }
    } else {
        // VB6 skill <= 10 branch
        "Dudoso".to_string()
    }
}

/// RC<x>,<y> — Right click on tile (interact / context menu).
/// VB6 equivalent: Accion() in Acciones.bas — handles doors, NPCs, users, items.
pub(crate) async fn handle_right_click(state: &mut GameState, conn_id: ConnectionId, x: i32, y: i32) {
    let (map, user_x, user_y, privileges, dead) = match state.users.get(&conn_id) {
        Some(u) if u.logged => (u.pos_map, u.pos_x, u.pos_y, u.privileges, u.dead),
        _ => return,
    };

    // Range check
    if (x - user_x).abs() > world::MIN_X_BORDER || (y - user_y).abs() > world::MIN_Y_BORDER {
        // RC out of range — don't log
        return;
    }

    // Save target coordinates
    if let Some(user) = state.users.get_mut(&conn_id) {
        user.target_x = x;
        user.target_y = y;
        user.target_map = map;
    }

    // VB6: Right-click also shows tile info (LookatTile) before performing the action.
    // This displays NPC/player name, health, guild, faction, etc.
    do_lookat_tile(state, conn_id, x, y).await;

    // Gather tile data without holding borrows
    let tile_data = state.world.grid(map).and_then(|g| g.tile(x, y)).map(|t| {
        (t.user_conn, t.npc_index, t.ground_item.obj_index, t.ground_item.amount)
    });
    let (tile_user, mut tile_npc_idx, tile_obj_idx, _tile_obj_amt) = match tile_data {
        Some(d) => d,
        None => { return; }
    };

    // VB6 Acciones.bas: check Y+1 FIRST, then Y for characters.
    // Character heads extend upward — clicking on the head area (tile Y) finds the
    // character whose body is at tile Y+1. Only these two tiles are checked.
    let rc_grid_height = state.world.grid(map).map(|g| g.height).unwrap_or(100);
    let rc_grid_width = state.world.grid(map).map(|g| g.width).unwrap_or(100);
    if tile_npc_idx == 0 && y + 1 <= rc_grid_height {
        if let Some(npc_on_y1) = state.world.grid(map).and_then(|g| g.tile(x, y + 1)).map(|t| t.npc_index) {
            if npc_on_y1 > 0 {
                tile_npc_idx = npc_on_y1;
            }
        }
    }

    // 1. Check for DOOR on tile (VB6: AccionParaPuerta — otPuertas=6)
    // Also check the clicked tile's ground object in static map data
    let ground_obj = get_map_tile_obj(state, map, x, y);
    if ground_obj > 0 {
        if let Some(obj) = state.get_object(ground_obj) {
            if obj.obj_type == crate::data::objects::ObjType::Door {
                accion_para_puerta(state, conn_id, map, x, y, ground_obj).await;
                return;
            }
        }
    }
    // Also check adjacent tiles for doors (VB6: Accion() checks x-1, x-2, x+1, x+2)
    // x-1: only for PuertaDoble or Porton doors
    for dx in [-1i32] {
        let ax = x + dx;
        if ax < 1 || ax > rc_grid_width { continue; }
        let adj_obj = get_map_tile_obj(state, map, ax, y);
        if adj_obj > 0 {
            if let Some(obj) = state.get_object(adj_obj) {
                if obj.obj_type == crate::data::objects::ObjType::Door
                    && (obj.puerta_doble == 1 || obj.porton == 1) {
                    accion_para_puerta(state, conn_id, map, ax, y, adj_obj).await;
                    return;
                }
            }
        }
    }
    // x-2: only for Porton doors
    for dx in [-2i32] {
        let ax = x + dx;
        if ax < 1 || ax > rc_grid_width { continue; }
        let adj_obj = get_map_tile_obj(state, map, ax, y);
        if adj_obj > 0 {
            if let Some(obj) = state.get_object(adj_obj) {
                if obj.obj_type == crate::data::objects::ObjType::Door
                    && obj.porton == 1 {
                    accion_para_puerta(state, conn_id, map, ax, y, adj_obj).await;
                    return;
                }
            }
        }
    }
    // x+1: any door type
    for dx in [1i32] {
        let ax = x + dx;
        if ax < 1 || ax > rc_grid_width { continue; }
        let adj_obj = get_map_tile_obj(state, map, ax, y);
        if adj_obj > 0 {
            if let Some(obj) = state.get_object(adj_obj) {
                if obj.obj_type == crate::data::objects::ObjType::Door {
                    accion_para_puerta(state, conn_id, map, ax, y, adj_obj).await;
                    return;
                }
            }
        }
    }
    // x+2: only for PuertaDoble or Porton doors (VB6 line 93-99)
    for dx in [2i32] {
        let ax = x + dx;
        if ax < 1 || ax > rc_grid_width { continue; }
        let adj_obj = get_map_tile_obj(state, map, ax, y);
        if adj_obj > 0 {
            if let Some(obj) = state.get_object(adj_obj) {
                if obj.obj_type == crate::data::objects::ObjType::Door
                    && (obj.puerta_doble == 1 || obj.porton == 1) {
                    accion_para_puerta(state, conn_id, map, ax, y, adj_obj).await;
                    return;
                }
            }
        }
    }

    // 1b. Check for FORUM on tile (VB6: AccionParaForo — ObjType 10)
    if ground_obj > 0 {
        if let Some(obj) = state.get_object(ground_obj) {
            if obj.obj_type == crate::data::objects::ObjType::Forum {
                if !dead {
                    accion_para_foro(state, conn_id, ground_obj).await;
                }
                return;
            }
        }
    }

    // 2. Check for USER on tile → send MENU packet
    if let Some(target_conn) = tile_user {
        if target_conn != conn_id {
            let target_info = state.users.get(&target_conn).and_then(|t| {
                if t.logged && !t.admin_invisible {
                    Some(t.char_name.clone())
                } else {
                    None
                }
            });
            if let Some(target_name) = target_info {
                if let Some(user) = state.users.get_mut(&conn_id) {
                    user.target_user = target_conn;
                }
                let pkt_menu = binary_packets::write_menu_data(&target_name, privileges as u8);
                state.send_bytes(conn_id, &pkt_menu);
                return;
            }
        }
    }

    // 3. Check for NPC on tile — type-specific interaction
    if tile_npc_idx > 0 {
        let npc_idx = tile_npc_idx as usize;
        let npc_info = state.get_npc(npc_idx).map(|npc| {
            (npc.is_alive(), npc.comercia, npc.name.clone(), npc.npc_type, npc.desc.clone(), npc.npc_number)
        });

        if let Some((alive, comercia, _npc_name, npc_type, _npc_desc, _npc_num)) = npc_info {
            if alive {
                // Set target NPC
                if let Some(user) = state.users.get_mut(&conn_id) {
                    user.target_npc = npc_idx;
                }

                use crate::data::npcs::NpcType;
                let dist = (x - user_x).abs().max((y - user_y).abs());

                // VB6 Accion() does NOT show NPC name/desc — that's only LookatTile (LC).
                // RC just triggers the action (commerce, bank, revive, etc.)

                // VB6 Accion(): First check Comercia, then check NPCtype
                // Commerce takes priority over type (VB6 line 135)
                if comercia {
                    if dead { state.send_msg_id(conn_id, 3, ""); return; }
                    if dist > 6 {
                        state.send_msg_id(conn_id, 13, ""); return;
                    }
                    iniciar_comercio_npc(state, conn_id, npc_idx).await;
                } else {
                    match npc_type {
                        NpcType::Banker => {
                            if dead { state.send_msg_id(conn_id, 3, ""); return; }
                            if dist > 10 {
                                state.send_msg_id(conn_id, 13, ""); return;
                            }
                            iniciar_banco(state, conn_id).await;
                        }
                        NpcType::BoveClan => {
                            if dead { state.send_msg_id(conn_id, 3, ""); return; }
                            if dist > 10 {
                                state.send_msg_id(conn_id, 13, ""); return;
                            }
                            iniciar_banco_clan(state, conn_id).await;
                        }
                        NpcType::Traveler => {
                            if dead { state.send_msg_id(conn_id, 3, ""); return; }
                            if dist > 5 {
                                state.send_msg_id(conn_id, 13, ""); return;
                            }
                            let pkt_travels = binary_packets::write_travels();
                            state.send_bytes(conn_id, &pkt_travels);
                        }
                        NpcType::Quest | NpcType::QuestNoble => { }
                        NpcType::Reviver => {
                            // VB6 Acciones.bas:408-422 — Revividor NPC
                            // Distance check: <= 10 tiles
                            if dist > 10 {
                                state.send_msg_id(conn_id, 12, ""); return;
                            }

                            // If dead: revive first
                            if dead {
                                revive_user(state, conn_id).await;
                            }

                            // Always full-heal + cure poison (VB6: MinHP=MaxHP, Envenenado=False)
                            if let Some(user) = state.users.get_mut(&conn_id) {
                                let max = user.max_hp;
                                user.min_hp = max;
                                user.poisoned = false;
                            }
                            send_stats_hp(state, conn_id).await;
                        }
                        NpcType::Trainer => {
                            if dead { state.send_msg_id(conn_id, 3, ""); return; }
                            if dist > 10 {
                                state.send_msg_id(conn_id, 13, ""); return;
                            }
                            state.send_console(conn_id, "Habla con el entrenador usando el chat.", font_index::INFO);
                        }
                        NpcType::Surgeon => {
                            if dist > 10 {
                                state.send_msg_id(conn_id, 13, ""); return;
                            }
                            if let Some(user) = state.users.get_mut(&conn_id) {
                                if user.poisoned {
                                    user.poisoned = false;
                                    state.send_console(conn_id, "El cirujano te ha curado el veneno.", font_index::INFO);
                                } else {
                                    state.send_console(conn_id, "No necesitas curacion.", font_index::INFO);
                                }
                            }
                        }
                        NpcType::Mail => {
                            // Mail system removed — was never part of VB6 13.3
                        }
                        NpcType::Citizenship => {
                            // VB6: Ciudadania (type 13)
                            if dead { state.send_msg_id(conn_id, 3, ""); return; }
                            if dist > 5 {
                                state.send_msg_id(conn_id, 13, ""); return;
                            }
                            state.send_console(conn_id, "Habla conmigo para cambiar tu ciudadania. Escribe /CIUDADANO para convertirte en ciudadano o /CRIMINAL para renunciar.", font_index::INFO);
                        }
                        NpcType::HouseSeller => {
                            // VB6: ShowCasas (type 15) — house seller NPC
                            if dead { state.send_msg_id(conn_id, 3, ""); return; }
                            if dist > 5 {
                                state.send_msg_id(conn_id, 13, ""); return;
                            }
                            // VB6: sends house listing UI. Rust: use /CASAINFO <num> and /CASACOMPRAR <num>
                            state.send_console(conn_id, "Bienvenido a la inmobiliaria. Usa /CASAINFO <numero> para ver detalles o /CASACOMPRAR <numero> para comprar.", font_index::INFO);
                        }
                        NpcType::Arena => { }
                        NpcType::GodNpc => {
                            // VB6: NpcDioses (type 18)
                            if dead { state.send_msg_id(conn_id, 3, ""); return; }
                            if dist > 3 {
                                state.send_msg_id(conn_id, 14, ""); return;
                            }
                            state.send_console(conn_id, "Acercate mas para hablar con los dioses.", font_index::INFO);
                        }
                        NpcType::Bargomaud => {
                            // VB6: NpcBargomaud (type 20) — check level >= 55, warp to 161,50,53
                            if dead { state.send_msg_id(conn_id, 3, ""); return; }
                            if dist > 5 {
                                state.send_msg_id(conn_id, 14, ""); return;
                            }
                            let level = state.users.get(&conn_id).map(|u| u.level).unwrap_or(0);
                            if level < 55 {
                                state.send_msg_id(conn_id, 643, ""); return;
                            }
                            warp_user(state, conn_id, 161, 50, 53).await;
                            let name = state.users.get(&conn_id).map(|u| u.char_name.clone()).unwrap_or_default();
                            state.send_msg_id(conn_id, 651, &name);
                        }
                        NpcType::QuintaJera => {
                            // VB6: QuintaJera (type 21) — faction rewards
                            if dead { state.send_msg_id(conn_id, 3, ""); return; }
                            if dist > 5 {
                                state.send_msg_id(conn_id, 13, ""); return;
                            }
                            state.send_console(conn_id, "Usa los comandos /RECOMPENSA y /ENLISTAR para interactuar.", font_index::INFO);
                        }
                        NpcType::BoxDelivery => {
                            // VB6: EntregaCajas (type 24)
                            if dead { state.send_msg_id(conn_id, 3, ""); return; }
                            if dist > 5 {
                                state.send_msg_id(conn_id, 13, ""); return;
                            }
                            state.send_console(conn_id, "Trae las cajas de quest para recibir tu recompensa.", font_index::INFO);
                        }
                        _ => {
                            // Non-interactive NPC — description already shown above
                        }
                    }
                }
                return;
            }
        }
    }

    // 4. Ground item interaction
    if tile_obj_idx > 0 {
        if let Some(obj) = state.get_object(tile_obj_idx) {
            let sele_data = format!("{},{},OBJ", obj.obj_type as i32, obj.name);
            let pkt_sele = binary_packets::write_select_data(&sele_data);
            state.send_bytes(conn_id, &pkt_sele);
        }
    }
}
