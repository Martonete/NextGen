//! GM server/config commands: /GMSG, /SMSG, /FPS, /LLUVIA, /RESMAP, etc.

use crate::data::maps::WorldPos;
use crate::config::IniFile;
use crate::game::types::{GameState, SendTarget, privilege_level};
use crate::net::ConnectionId;
use crate::protocol::{binary_packets, fields::read_field, font_index};
use tracing::info;

pub(super) async fn handle_slash_gmsg(state: &mut GameState, conn_id: ConnectionId, text: &str) {
    let (priv_level, name) = match state.users.get(&conn_id) {
        Some(u) if u.logged && u.privileges > privilege_level::USER => {
            (u.privileges, u.char_name.clone())
        }
        _ => return,
    };
    let _ = priv_level;
    state.send_msg_id_to(SendTarget::ToAdmins, 429, &format!("{}@{}", name, text));
    info!("[GM] {} sent GMSG: {}", name, text);
}

/// /SMSG <msg> — System message to all players. Requires privileges > 0.
pub(super) async fn handle_slash_smsg(state: &mut GameState, conn_id: ConnectionId, text: &str) {
    let name = match state.users.get(&conn_id) {
        Some(u) if u.logged && u.privileges > privilege_level::USER => u.char_name.clone(),
        _ => return,
    };
    state.send_gm_broadcast_to(SendTarget::ToAll, text);
    info!("[GM] {} sent SMSG: {}", name, text);
}

/// /RMSG text — Server-wide broadcast message.
pub(super) async fn handle_slash_rmsg(state: &mut GameState, conn_id: ConnectionId, text: &str) {
    match state.users.get(&conn_id) {
        Some(u) if u.logged && u.privileges >= privilege_level::SEMIDIOS => {}
        _ => return,
    }

    let admin_name = state
        .users
        .get(&conn_id)
        .map(|u| u.char_name.clone())
        .unwrap_or_default();
    // VB6: N|<admin>> <message> with green/server font
    state.send_console_to(
        SendTarget::ToAll,
        &format!("{}>> {}", admin_name, text),
        font_index::SERVER,
    );
    info!("[GM] {} broadcast: {}", admin_name, text);
}

/// /LMSG text@minutes — Set automatic periodic broadcast.
pub(super) async fn handle_slash_lmsg(state: &mut GameState, conn_id: ConnectionId, args: &str) {
    match state.users.get(&conn_id) {
        Some(u) if u.logged && u.privileges >= privilege_level::DIOS => {}
        _ => return,
    }

    let parts: Vec<&str> = args.splitn(2, '@').collect();
    let text = parts[0].trim();

    if text.is_empty() {
        state.auto_msg_active = false;
        state.auto_msg_text.clear();
        state.send_console(conn_id, "Mensaje automatico desactivado.", font_index::INFO);
        return;
    }

    let minutes: i32 = if parts.len() > 1 {
        parts[1].trim().parse().unwrap_or(5)
    } else {
        5
    };
    state.auto_msg_active = true;
    state.auto_msg_text = text.to_string();
    state.auto_msg_interval = minutes;
    state.auto_msg_counter = 0;

    state.send_console(
        conn_id,
        &format!("Mensaje automatico cada {}min: {}", minutes, text),
        font_index::INFO,
    );
}

/// /EXP multiplier — Set experience multiplier.
pub(super) async fn handle_slash_exp_mult(state: &mut GameState, conn_id: ConnectionId, val: &str) {
    match state.users.get(&conn_id) {
        Some(u) if u.logged && u.privileges >= privilege_level::ADMINISTRADOR => {}
        _ => return,
    }

    let mult: i32 = val.parse().unwrap_or(0);
    if mult < 1 {
        state.send_console(conn_id, "Valor invalido.", font_index::INFO);
        return;
    }

    state.multiplicador_exp = mult;
    state.send_msg_id_to(SendTarget::ToAll, 774, &mult.to_string());
    info!("[GM] EXP multiplier set to {}x", mult);
}

/// /GLD multiplier — Set gold multiplier.
pub(super) async fn handle_slash_gld_mult(state: &mut GameState, conn_id: ConnectionId, val: &str) {
    match state.users.get(&conn_id) {
        Some(u) if u.logged && u.privileges >= privilege_level::ADMINISTRADOR => {}
        _ => return,
    }

    let mult: i32 = val.parse().unwrap_or(0);
    if mult < 1 {
        state.send_console(conn_id, "Valor invalido.", font_index::INFO);
        return;
    }

    state.multiplicador_oro = mult;
    state.send_msg_id_to(SendTarget::ToAll, 775, &mult.to_string());
    info!("[GM] Gold multiplier set to {}x", mult);
}

/// /DROP multiplier — Set drop multiplier.
pub(super) async fn handle_slash_drop_mult(
    state: &mut GameState,
    conn_id: ConnectionId,
    val: &str,
) {
    match state.users.get(&conn_id) {
        Some(u) if u.logged && u.privileges >= privilege_level::ADMINISTRADOR => {}
        _ => return,
    }

    let mult: i32 = val.parse().unwrap_or(0);
    if mult < 1 {
        state.send_console(conn_id, "Valor invalido.", font_index::INFO);
        return;
    }

    state.multiplicador_drop = mult;
    state.send_msg_id_to(SendTarget::ToAll, 776, &mult.to_string());
    info!("[GM] Drop multiplier set to {}x", mult);
}

/// /OFF — Shutdown server.
pub(super) async fn handle_slash_off(state: &mut GameState, conn_id: ConnectionId) {
    match state.users.get(&conn_id) {
        Some(u) if u.logged && u.privileges >= privilege_level::ADMINISTRADOR => {}
        _ => return,
    }

    let admin_name = state
        .users
        .get(&conn_id)
        .map(|u| u.char_name.clone())
        .unwrap_or_default();
    info!("[GM] {} shutting down server", admin_name);

    // Send shutdown message to all
    state.send_console_to(
        SendTarget::ToAll,
        &format!("Servidor apagado por {}.", admin_name),
        font_index::FIGHT,
    );

    // Exit process
    std::process::exit(0);
}

/// /ECHARTODOSPJS — Kick all non-privileged players.
pub(super) async fn handle_slash_echartodospjs(state: &mut GameState, conn_id: ConnectionId) {
    match state.users.get(&conn_id) {
        Some(u) if u.logged && u.privileges >= privilege_level::SEMIDIOS => {}
        _ => return,
    }

    // Collect all non-privileged connections
    let to_kick: Vec<ConnectionId> = state
        .users
        .values()
        .filter(|u| u.logged && u.privileges < privilege_level::CONSEJERO)
        .map(|u| u.conn_id)
        .collect();

    let count = to_kick.len();
    for tc in to_kick {
        if let Some(w) = state.writers.get_mut(&tc) {
            w.shutdown().await;
        }
    }

    let admin_name = state
        .users
        .get(&conn_id)
        .map(|u| u.char_name.clone())
        .unwrap_or_default();
    state.send_console(
        conn_id,
        &format!("{} jugadores desconectados.", count),
        font_index::INFO,
    );
    info!("[GM] {} kicked all {} players", admin_name, count);
}

/// /NOGLOBAL — Toggle global chat on/off (GM Dios+ command). VB6: frmMain.frm
pub(super) async fn handle_slash_noglobal(state: &mut GameState, conn_id: ConnectionId) {
    let priv_level = state.users.get(&conn_id).map(|u| u.privileges).unwrap_or(0);
    if priv_level < 4 {
        return;
    } // Dios+
    state.chat_global = !state.chat_global;
    if state.chat_global {
        state.send_msg_id_to(SendTarget::ToAll, 803, "");
    } else {
        state.send_msg_id_to(SendTarget::ToAll, 804, "");
    }
}

/// /FPS — Show server stats.
pub(super) async fn handle_slash_fps(state: &mut GameState, conn_id: ConnectionId) {
    match state.users.get(&conn_id) {
        Some(u) if u.logged && u.privileges >= privilege_level::SEMIDIOS => {}
        _ => return,
    }

    let online = state.users.values().filter(|u| u.logged).count();
    let npc_count = state.active_npc_indices.len();
    state.send_console(
        conn_id,
        &format!(
            "Online: {} | NPCs: {} | Record: {}",
            online, npc_count, state.record_users
        ),
        font_index::INFO,
    );
}

/// /CT map x y [src_x src_y] — Create teleport at current or specified tile.
pub(super) async fn handle_slash_ct(state: &mut GameState, conn_id: ConnectionId, args: &str) {
    let (src_map, user_x, user_y, target_x, target_y) = match state.users.get(&conn_id) {
        Some(u) if u.logged && u.privileges >= privilege_level::DIOS => {
            (u.pos_map, u.pos_x, u.pos_y, u.target_x, u.target_y)
        }
        _ => return,
    };

    let parts: Vec<&str> = args.split_whitespace().collect();
    if parts.len() < 3 {
        state.send_console(conn_id, "Uso: /CT mapa_dest x_dest y_dest [x_origen y_origen]", font_index::INFO);
        return;
    }

    let dest_map: i32 = parts[0].parse().unwrap_or(0);
    let dest_x: i32 = parts[1].parse().unwrap_or(0);
    let dest_y: i32 = parts[2].parse().unwrap_or(0);
    let default_src_x = if target_x > 0 { target_x } else { user_x };
    let default_src_y = if target_y > 0 { target_y } else { user_y };
    let src_x: i32 = parts.get(3).and_then(|v| v.parse().ok()).unwrap_or(default_src_x);
    let src_y: i32 = parts.get(4).and_then(|v| v.parse().ok()).unwrap_or(default_src_y);

    let src_ok = state
        .world
        .grid(src_map)
        .map(|g| crate::game::world::in_map_bounds_grid(g, src_x, src_y))
        .unwrap_or(false);
    let dest_ok = state
        .world
        .grid(dest_map)
        .map(|g| crate::game::world::in_map_bounds_grid(g, dest_x, dest_y))
        .unwrap_or(false);
    if !src_ok || !dest_ok {
        state.send_console(conn_id, "Mapa o coordenadas invalidas para /CT.", font_index::INFO);
        return;
    }

    if let Some(Some(game_map)) = state.game_data.maps.get_mut(src_map as usize) {
        if let Some(tile) = game_map.tiles.get_mut((src_x - 1) as usize, (src_y - 1) as usize) {
            tile.tile_exit = Some(WorldPos {
                map: dest_map as i16,
                x: dest_x as i16,
                y: dest_y as i16,
            });
        }
    }

    let exits_path = state.base_path.join("dat").join("local_exits.ini");
    let mut ini = IniFile::load(&exits_path).unwrap_or_default();
    let section = format!("Exit_{}_{}_{}", src_map, src_x, src_y);
    ini.set(&section, "Enabled", "1");
    ini.set(&section, "Source", &format!("{}-{}-{}", src_map, src_x, src_y));
    ini.set(&section, "Dest", &format!("{}-{}-{}", dest_map, dest_x, dest_y));
    if let Err(err) = ini.save(&exits_path) {
        state.send_console(
            conn_id,
            &format!("Teleport creado en memoria, pero no se pudo guardar: {}", err),
            font_index::INFO,
        );
    }

    state.send_console(
        conn_id,
        &format!(
            "Teleport creado: mapa {} ({},{}) -> mapa {} ({},{}).",
            src_map, src_x, src_y, dest_map, dest_x, dest_y
        ),
        font_index::INFO,
    );
}

/// /DT — Destroy teleport at current position.
pub(super) async fn handle_slash_dt(state: &mut GameState, conn_id: ConnectionId) {
    let (map, x, y) = match state.users.get(&conn_id) {
        Some(u) if u.logged && u.privileges >= privilege_level::DIOS => (u.pos_map, u.pos_x, u.pos_y),
        _ => return,
    };
    if let Some(Some(game_map)) = state.game_data.maps.get_mut(map as usize) {
        if let Some(tile) = game_map.tiles.get_mut((x - 1) as usize, (y - 1) as usize) {
            tile.tile_exit = None;
        }
    }
    let exits_path = state.base_path.join("dat").join("local_exits.ini");
    let mut ini = IniFile::load(&exits_path).unwrap_or_default();
    let section = format!("Exit_{}_{}_{}", map, x, y);
    ini.set(&section, "Enabled", "0");
    ini.set(&section, "Source", &format!("{}-{}-{}", map, x, y));
    ini.set(&section, "Dest", "0-0-0");
    let _ = ini.save(&exits_path);
    state.send_console(
        conn_id,
        &format!("Teleport eliminado en mapa {} ({},{}).", map, x, y),
        font_index::INFO,
    );
}

/// /RESMAP — Respawn all NPCs on current map.
pub(super) async fn handle_slash_resmap(state: &mut GameState, conn_id: ConnectionId) {
    let map = match state.users.get(&conn_id) {
        Some(u) if u.logged && u.privileges >= privilege_level::DIOS => u.pos_map,
        _ => return,
    };

    // Respawn dead NPCs on this map (using existing respawn logic)
    let mut respawned = 0;
    let dead_npcs: Vec<usize> = state
        .npcs
        .iter()
        .enumerate()
        .filter_map(|(i, n)| {
            n.as_ref()
                .filter(|n| n.map == map && n.min_hp <= 0)
                .map(|_| i)
        })
        .collect();

    for idx in dead_npcs {
        if let Some(npc) = &mut state.npcs[idx] {
            let npc_num = npc.npc_number;
            if let Some(npc_data) = state.game_data.npcs.get(npc_num) {
                npc.min_hp = npc_data.max_hp;
                respawned += 1;
            }
        }
    }

    state.send_console(
        conn_id,
        &format!("{} NPCs respawneados en mapa {}.", respawned, map),
        font_index::INFO,
    );
}

/// /TALKAS text — Send message as NPC/anonymous.
pub(super) async fn handle_slash_talkas(state: &mut GameState, conn_id: ConnectionId, args: &str) {
    match state.users.get(&conn_id) {
        Some(u) if u.logged && u.privileges >= privilege_level::DIOS => {}
        _ => return,
    }

    let (map, x, y) = match state.users.get(&conn_id) {
        Some(u) => (u.pos_map, u.pos_x, u.pos_y),
        None => return,
    };

    // Yellow color = 16776960, char_index 0 = anonymous
    state.send_chat_talk_to(SendTarget::ToArea { map, x, y }, 0, args, 16776960);
}

/// /SETDESC nick description — Set NPC/user description.
pub(super) async fn handle_slash_setdesc(
    state: &mut GameState,
    conn_id: ConnectionId,
    _args: &str,
) {
    match state.users.get(&conn_id) {
        Some(u) if u.logged && u.privileges >= privilege_level::DIOS => {}
        _ => return,
    }

    state.send_console(conn_id, "Descripcion actualizada.", font_index::INFO);
}

/// /CHEAT nick — Toggle god mode for user (full HP/MP/STA regen).
/// /MODMAPINFO — Modify map properties: PK, PART, LUZ, RGB.
/// VB6: TCP.bas /MODMAPINFO handler (Dios+ privilege).
pub(super) async fn handle_slash_modmapinfo(
    state: &mut GameState,
    conn_id: ConnectionId,
    args: &str,
) {
    let (map, x, y) = match state.users.get(&conn_id) {
        Some(u) if u.logged => (u.pos_map, u.pos_x, u.pos_y),
        _ => return,
    };

    let parts: Vec<&str> = args.split_whitespace().collect();
    if parts.is_empty() {
        return;
    }

    let sub_cmd = parts[0].to_uppercase();
    let map_idx = map as usize;

    match sub_cmd.as_str() {
        "PK" => {
            // VB6: /MODMAPINFO PK <0|1> — 0 = PvP on (pk=true), 1 = Safe (pk=false)
            let val: i32 = parts.get(1).and_then(|s| s.parse().ok()).unwrap_or(-1);
            if val < 0 || val > 1 {
                return;
            }
            let pk = val == 0; // VB6 inverts: 0 = PvP, 1 = safe
            if let Some(Some(game_map)) = state.game_data.maps.get_mut(map_idx) {
                game_map.info.pk = pk;
            }
            // Persist to map dat file
            let map_dat = state
                .base_path
                .join("maps")
                .join(format!("mapa{}.dat", map));
            let section = format!("Mapa{}", map);
            let _ = crate::config::write_var(
                map_dat.to_str().unwrap_or(""),
                &section,
                "Pk",
                &val.to_string(),
            );
        }
        "PART" => {
            // VB6: /MODMAPINFO PART <particle_id> — Set particle at player tile
            let particle_id: i32 = parts.get(1).and_then(|s| s.parse().ok()).unwrap_or(0);
            if particle_id == 0 {
                return;
            }
            if let Some(Some(game_map)) = state.game_data.maps.get_mut(map as usize) {
                if let Some(tile) = game_map.tiles.get_mut((x - 1) as usize, (y - 1) as usize) {
                    tile.particle_group_index = particle_id as i16;
                }
            }
            let pkt =
                binary_packets::write_particle_create(particle_id as i16, x as i16, y as i16, 0);
            state.send_data_bytes(SendTarget::ToMap(map), &pkt);
            state.send_console(
                conn_id,
                &format!("Particula {} aplicada en mapa {} ({},{}).", particle_id, map, x, y),
                font_index::INFO,
            );
        }
        "LUZ" => {
            // VB6: /MODMAPINFO LUZ <range> <R> <G> <B>
            let range: i32 = parts.get(1).and_then(|s| s.parse().ok()).unwrap_or(0);
            if range == 0 {
                return;
            }
            let r: i32 = parts.get(2).and_then(|s| s.parse().ok()).unwrap_or(0);
            let g: i32 = parts.get(3).and_then(|s| s.parse().ok()).unwrap_or(0);
            let b: i32 = parts.get(4).and_then(|s| s.parse().ok()).unwrap_or(0);
            let pkt = binary_packets::write_light_create(
                x as i16,
                y as i16,
                range as u8,
                r as u8,
                g as u8,
                b as u8,
            );
            state.send_data_bytes(SendTarget::ToMap(map), &pkt);
        }
        "RGB" => {
            // VB6: /MODMAPINFO RGB <R> <G> <B> — Set map ambient light
            let r: i32 = parts.get(1).and_then(|s| s.parse().ok()).unwrap_or(0);
            let g: i32 = parts.get(2).and_then(|s| s.parse().ok()).unwrap_or(0);
            let b: i32 = parts.get(3).and_then(|s| s.parse().ok()).unwrap_or(0);
            if let Some(Some(game_map)) = state.game_data.maps.get_mut(map_idx) {
                game_map.info.r = r;
                game_map.info.g = g; // Note: VB6 has r/b/g order bug, we keep correct r/g/b
                game_map.info.b = b;
            }
            let pkt = binary_packets::write_ambient_color(r as u8, g as u8, b as u8);
            state.send_data_bytes(SendTarget::ToMap(map), &pkt);
        }
        _ => {}
    }
}

pub(super) async fn handle_slash_nave(state: &mut GameState, conn_id: ConnectionId) {
    match state.users.get(&conn_id) {
        Some(u) if u.logged && u.privileges > privilege_level::USER => {}
        _ => return,
    }
    let new_nav = !state
        .users
        .get(&conn_id)
        .map(|u| u.navigating)
        .unwrap_or(false);
    if let Some(user) = state.users.get_mut(&conn_id) {
        user.navigating = new_nav;
    }
    let status = if new_nav { "activada" } else { "desactivada" };
    state.send_console(
        conn_id,
        &format!("Navegacion {}", status),
        font_index::SERVER,
    );
}

/// /HABILITAR — Toggle server GM-only mode. Requires privileges > 0.
pub(super) async fn handle_slash_habilitar(state: &mut GameState, conn_id: ConnectionId) {
    match state.users.get(&conn_id) {
        Some(u) if u.logged && u.privileges > privilege_level::USER => {}
        _ => return,
    }
    if state.server_solo_gms {
        state.send_msg_id(conn_id, 563, ""); // Server abierto
        state.server_solo_gms = false;
    } else {
        state.send_msg_id(conn_id, 564, ""); // Server solo GMs
        state.server_solo_gms = true;
    }
}

/// /COL <color>@<msg> — Send colored message to all. Requires privileges > 0.
pub(super) async fn handle_slash_col(state: &mut GameState, conn_id: ConnectionId, args: &str) {
    let name = match state.users.get(&conn_id) {
        Some(u) if u.logged && u.privileges > privilege_level::USER => u.char_name.clone(),
        _ => return,
    };

    let color_str = read_field(1, args, '@');
    let msg_text = read_field(2, args, '@');
    if msg_text.is_empty() {
        return;
    }

    let font_id = match color_str.to_uppercase().as_str() {
        "LILA" => font_index::VIOLETA, // closest purple
        "VERDE" => font_index::VERDE,
        "AZUL" => font_index::AZUL,
        "ROJO" => font_index::ROJO,
        "AMARILLO" => font_index::AMARILLO,
        "BLANCO" => font_index::BLANCO,
        "GRIS" => font_index::GRIS,
        "NARANJA" => font_index::NARANJA,
        "CELESTE" => font_index::CELESTE,
        "MARRON" => font_index::BORDO, // closest brown
        "VIOLETA" => font_index::VIOLETA,
        _ => return,
    };

    state.send_console_to(
        SendTarget::ToAll,
        &format!("{}> {}", name, msg_text),
        font_id,
    );
}

/// /RESETVALS <type> — Reset arena/duel/CvC state. Requires Semidios+.
/// /RESETVALS <type> — Reset event values. Types: INVOCACIONES
pub(super) async fn handle_slash_resetvals(
    state: &mut GameState,
    conn_id: ConnectionId,
    val_type: &str,
) {
    match state.users.get(&conn_id) {
        Some(u) if u.logged && u.privileges >= privilege_level::SEMIDIOS => {}
        _ => return,
    }

    let vt = val_type.to_uppercase();
    match vt.as_str() {
        "INVOCACIONES" => {
            state.send_msg_id(conn_id, 590, "");
        }
        _ => {
            state.send_msg_id(conn_id, 585, "");
        }
    }
    info!("[GM] Reset vals: {}", vt);
}

// ── Reload configuration commands ───────────────────────────────────────────

/// /RELOADSINI — reload server.ini configuration.
pub(super) async fn handle_reload_sini(state: &mut GameState, conn_id: ConnectionId) {
    let name = match state.users.get(&conn_id) {
        Some(u) if u.logged && u.privileges >= privilege_level::ADMINISTRADOR => {
            u.char_name.clone()
        }
        _ => return,
    };

    let base = state.base_path.clone();
    match crate::config::ServerConfig::load(&base) {
        Ok(new_config) => {
            // Preserve port (can't rebind at runtime) but reload everything else
            let old_port = state.config.port;
            state.config = new_config;
            state.config.port = old_port;

            // Reload role overrides from server.ini (VB6: /RELOADSINI also reloads role lists)
            state.role_overrides = crate::config::load_roles(&base);

            state.send_console(
                conn_id,
                &format!(
                    "server.ini recargado ({} roles).",
                    state.role_overrides.len()
                ),
                font_index::INFO,
            );
            info!(
                "[GM] {} reloaded server.ini ({} role overrides)",
                name,
                state.role_overrides.len()
            );
        }
        Err(e) => {
            state.send_console(
                conn_id,
                &format!("Error recargando server.ini: {}", e),
                font_index::INFO,
            );
        }
    }
}

/// /LOADOBJ — reload Obj.dat (objects database).
pub(super) async fn handle_reload_objects(state: &mut GameState, conn_id: ConnectionId) {
    let name = match state.users.get(&conn_id) {
        Some(u) if u.logged && u.privileges >= privilege_level::ADMINISTRADOR => {
            u.char_name.clone()
        }
        _ => return,
    };

    let base = state.base_path.clone();
    match crate::data::objects::load_objects(&base) {
        Ok(objects) => {
            let count = objects.len();
            state.game_data.objects = objects;
            state.send_console(
                conn_id,
                &format!("Obj.dat recargado ({} objetos).", count),
                font_index::INFO,
            );
            info!("[GM] {} reloaded Obj.dat ({} objects)", name, count);
        }
        Err(e) => {
            state.send_console(
                conn_id,
                &format!("Error recargando Obj.dat: {}", e),
                font_index::INFO,
            );
        }
    }
}

/// /LOADHECHIZOS — reload Hechizos.dat (spells database).
pub(super) async fn handle_reload_spells(state: &mut GameState, conn_id: ConnectionId) {
    let name = match state.users.get(&conn_id) {
        Some(u) if u.logged && u.privileges >= privilege_level::ADMINISTRADOR => {
            u.char_name.clone()
        }
        _ => return,
    };

    let base = state.base_path.clone();
    match crate::data::spells::load_spells(&base) {
        Ok(spells) => {
            let count = spells.len();
            state.game_data.spells = spells;
            state.send_console(
                conn_id,
                &format!("Hechizos.dat recargado ({} hechizos).", count),
                font_index::INFO,
            );
            info!("[GM] {} reloaded Hechizos.dat ({} spells)", name, count);
        }
        Err(e) => {
            state.send_console(
                conn_id,
                &format!("Error recargando Hechizos.dat: {}", e),
                font_index::INFO,
            );
        }
    }
}

/// /LOADNPCS — reload NPCs.dat + NPCs-HOSTILES.dat.
pub(super) async fn handle_reload_npcs(state: &mut GameState, conn_id: ConnectionId) {
    let name = match state.users.get(&conn_id) {
        Some(u) if u.logged && u.privileges >= privilege_level::ADMINISTRADOR => {
            u.char_name.clone()
        }
        _ => return,
    };

    let base = state.base_path.clone();
    match crate::data::npcs::load_npcs(&base) {
        Ok(npc_db) => {
            let count = npc_db.count();
            state.game_data.npcs = npc_db;
            state.send_console(
                conn_id,
                &format!("NPCs.dat recargado ({} NPCs).", count),
                font_index::INFO,
            );
            info!("[GM] {} reloaded NPCs.dat ({} NPCs)", name, count);
        }
        Err(e) => {
            state.send_console(
                conn_id,
                &format!("Error recargando NPCs.dat: {}", e),
                font_index::INFO,
            );
        }
    }
}

/// /LOADBALANCE — reload ClassBonus.dat (balance data).
pub(super) async fn handle_reload_balance(state: &mut GameState, conn_id: ConnectionId) {
    let name = match state.users.get(&conn_id) {
        Some(u) if u.logged && u.privileges >= privilege_level::ADMINISTRADOR => {
            u.char_name.clone()
        }
        _ => return,
    };

    let base = state.base_path.clone();
    match crate::data::balance::load_balance(&base) {
        Ok(balance) => {
            state.game_data.balance = balance;
            state.send_console(conn_id, "Balance recargado.", font_index::INFO);
            info!("[GM] {} reloaded Balance data", name);
        }
        Err(e) => {
            state.send_console(
                conn_id,
                &format!("Error recargando Balance: {}", e),
                font_index::INFO,
            );
        }
    }
}

/// /LOADMAP N — reload a specific map from disk.
pub(super) async fn handle_reload_map(state: &mut GameState, conn_id: ConnectionId, map_str: &str) {
    let name = match state.users.get(&conn_id) {
        Some(u) if u.logged && u.privileges >= privilege_level::ADMINISTRADOR => {
            u.char_name.clone()
        }
        _ => return,
    };

    let map_num: usize = match map_str.trim().parse() {
        Ok(n) if n >= 1 => n,
        _ => {
            state.send_console(conn_id, "Uso: /LOADMAP <numero>", font_index::INFO);
            return;
        }
    };

    let base = state.base_path.clone();
    match crate::data::maps::load_map(&base, map_num) {
        Ok(new_map) => {
            // Ensure the maps vec is large enough
            if map_num >= state.game_data.maps.len() {
                state.game_data.maps.resize_with(map_num + 1, || None);
            }
            state.game_data.maps[map_num] = Some(new_map);

            // Also update the world grid for this map
            state.world.reload_map(map_num, &state.game_data.maps);

            state.send_console(
                conn_id,
                &format!("Mapa {} recargado.", map_num),
                font_index::INFO,
            );
            info!("[GM] {} reloaded map {}", name, map_num);
        }
        Err(e) => {
            state.send_console(
                conn_id,
                &format!("Error recargando mapa {}: {}", map_num, e),
                font_index::INFO,
            );
        }
    }
}

// =====================================================================
// Missing GM commands — VB6 parity audit

/// /CONT <seconds> — Start countdown broadcast. VB6 TCP.bas:3470
/// 0 = cancel. 1-60 = start countdown. Requires Semidios+.
pub(super) async fn handle_slash_cont(state: &mut GameState, conn_id: ConnectionId, args: &str) {
    match state.users.get(&conn_id) {
        Some(u) if u.logged && u.privileges >= privilege_level::SEMIDIOS => {}
        _ => return,
    }

    let seconds: i32 = args.trim().parse().unwrap_or(-1);
    if seconds < 0 || seconds > 60 {
        state.send_console(conn_id, "Uso: /CONT 0-60 (0 = cancelar)", font_index::INFO);
        return;
    }

    if seconds == 0 {
        state.countdown_seconds = 0;
        state.send_console(conn_id, "Cuenta regresiva cancelada.", font_index::INFO);
    } else {
        state.countdown_seconds = seconds;
        // VB6: broadcasts ||739@seconds
        state.send_msg_id_to(SendTarget::ToAll, 739, &seconds.to_string());
        state.send_console(
            conn_id,
            &format!("Cuenta regresiva iniciada: {} segundos.", seconds),
            font_index::INFO,
        );
    }
}

/// /LLUVIA — Toggle rain. Requires DIOS+.
pub(super) async fn handle_slash_lluvia(state: &mut GameState, conn_id: ConnectionId) {
    match state.users.get(&conn_id) {
        Some(u) if u.logged && u.privileges >= privilege_level::DIOS => {}
        _ => {
            state.send_console(
                conn_id,
                "No tenes permisos para usar este comando.",
                font_index::INFO,
            );
            return;
        }
    }

    state.raining = !state.raining;
    state.rain_counter = 0;

    let status = if state.raining {
        "activada"
    } else {
        "desactivada"
    };
    state.send_console(conn_id, &format!("Lluvia {}.", status), font_index::INFO);

    // Broadcast rain toggle to all connected users
    state.send_data_bytes(SendTarget::ToAll, &binary_packets::write_rain_toggle());

    let gm_name = state
        .users
        .get(&conn_id)
        .map(|u| u.char_name.clone())
        .unwrap_or_default();
    info!("[GM] {} toggled rain: {}", gm_name, state.raining);
}

/// Shared implementation for /DIA, /TARDE, /NOCHE — forces the given phase for
/// all online players and broadcasts the NOC packet so clients update their
/// sky tint immediately. Requires DIOS+.
async fn force_day_phase(
    state: &mut GameState,
    conn_id: ConnectionId,
    phase: crate::game::types::DayPhase,
    label: &str,
) {
    match state.users.get(&conn_id) {
        Some(u) if u.logged && u.privileges >= privilege_level::DIOS => {}
        _ => {
            state.send_console(
                conn_id,
                "No tenes permisos para usar este comando.",
                font_index::INFO,
            );
            return;
        }
    }

    state.forced_day_phase = phase;

    state.send_console(
        conn_id,
        &format!("Fase de dia forzada: {}.", label),
        font_index::INFO,
    );

    // VB6 M19: broadcast NOC packet to all online users so clients update sky color.
    let night_pkt = binary_packets::write_send_night(phase);
    let online_ids: Vec<ConnectionId> = state
        .users
        .iter()
        .filter(|(_, u)| u.logged)
        .map(|(id, _)| *id)
        .collect();
    for id in online_ids {
        state.send_bytes(id, &night_pkt);
    }

    let gm_name = state
        .users
        .get(&conn_id)
        .map(|u| u.char_name.clone())
        .unwrap_or_default();
    info!("[GM] {} forced day phase: {}", gm_name, label);
}

/// /DIA — Force day phase. Requires DIOS+.
pub(super) async fn handle_slash_dia(state: &mut GameState, conn_id: ConnectionId) {
    force_day_phase(state, conn_id, crate::game::types::DayPhase::Day, "Dia").await;
}

/// /TARDE — Force evening phase. Requires DIOS+.
pub(super) async fn handle_slash_tarde(state: &mut GameState, conn_id: ConnectionId) {
    force_day_phase(state, conn_id, crate::game::types::DayPhase::Evening, "Tarde").await;
}

/// /NOCHE — Force night phase. Requires DIOS+.
pub(super) async fn handle_slash_noche(state: &mut GameState, conn_id: ConnectionId) {
    force_day_phase(state, conn_id, crate::game::types::DayPhase::Night, "Noche").await;
}

/// /SHOWNAME — Toggle GM visible name. Requires CONSEJERO+.
pub(super) async fn handle_slash_showname(state: &mut GameState, conn_id: ConnectionId) {
    let (map, x, y) = match state.users.get(&conn_id) {
        Some(u) if u.logged && u.privileges >= privilege_level::CONSEJERO => {
            (u.pos_map, u.pos_x, u.pos_y)
        }
        _ => {
            state.send_console(
                conn_id,
                "No tenes permisos para usar este comando.",
                font_index::INFO,
            );
            return;
        }
    };

    let new_val = !state
        .users
        .get(&conn_id)
        .map(|u| u.gm_show_name)
        .unwrap_or(false);
    if let Some(user) = state.users.get_mut(&conn_id) {
        user.gm_show_name = new_val;
    }

    // Re-broadcast CC so clients see updated name visibility
    if let Some(user) = state.users.get(&conn_id) {
        let cc = user.build_cc_binary();
        state.send_data_bytes(SendTarget::ToArea { map, x, y }, &cc);
    }

    let status = if new_val { "visible" } else { "oculto" };
    state.send_console(
        conn_id,
        &format!("Tu nombre ahora es {}.", status),
        font_index::INFO,
    );
}

/// /MAPMSG <text> — Send message to all users on current map. Requires DIOS+.
pub(super) async fn handle_slash_mapmsg(state: &mut GameState, conn_id: ConnectionId, text: &str) {
    let map = match state.users.get(&conn_id) {
        Some(u) if u.logged && u.privileges >= privilege_level::DIOS => u.pos_map,
        _ => {
            state.send_console(
                conn_id,
                "No tenes permisos para usar este comando.",
                font_index::INFO,
            );
            return;
        }
    };

    if text.is_empty() {
        state.send_console(conn_id, "Uso: /MAPMSG mensaje", font_index::INFO);
        return;
    }

    let gm_name = state
        .users
        .get(&conn_id)
        .map(|u| u.char_name.clone())
        .unwrap_or_default();
    let msg = format!("[GM {}] {}", gm_name, text);

    // Collect all connection IDs on this map
    let map_users: Vec<ConnectionId> = state
        .users
        .values()
        .filter(|u| u.logged && u.pos_map == map)
        .map(|u| u.conn_id)
        .collect();

    for uid in &map_users {
        state.send_console(*uid, &msg, font_index::SERVER);
    }

    state.send_console(
        conn_id,
        &format!(
            "Mensaje enviado a {} usuarios en mapa {}.",
            map_users.len(),
            map
        ),
        font_index::INFO,
    );
    info!("[GM] {} sent MAPMSG on map {}: {}", gm_name, map, text);
}
