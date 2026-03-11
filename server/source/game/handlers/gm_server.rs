//! GM server/config commands: /GMSG, /SMSG, /FPS, /LLUVIA, /RESMAP, etc.

use tracing::{info};
use crate::net::ConnectionId;
use crate::game::types::{GameState, SendTarget, privilege_level};
use crate::protocol::{font_index, fields::read_field, binary_packets};

pub(super) async fn handle_slash_gmsg(state: &mut GameState, conn_id: ConnectionId, text: &str) {
    let (priv_level, name) = match state.users.get(&conn_id) {
        Some(u) if u.logged && u.privileges > privilege_level::USER => {
            (u.privileges, u.char_name.clone())
        }
        _ => return,
    };
    let _ = priv_level;
    state.send_msg_id_to(SendTarget::ToAdmins, 429, &format!("{}@{}", name, text)).await;
    info!("[GM] {} sent GMSG: {}", name, text);
}

/// /SMSG <msg> — System message to all players. Requires privileges > 0.
pub(super) async fn handle_slash_smsg(state: &mut GameState, conn_id: ConnectionId, text: &str) {
    let name = match state.users.get(&conn_id) {
        Some(u) if u.logged && u.privileges > privilege_level::USER => u.char_name.clone(),
        _ => return,
    };
    state.send_gm_broadcast_to(SendTarget::ToAll, text).await;
    info!("[GM] {} sent SMSG: {}", name, text);
}

/// /RMSG text — Server-wide broadcast message.
pub(super) async fn handle_slash_rmsg(state: &mut GameState, conn_id: ConnectionId, text: &str) {
    match state.users.get(&conn_id) {
        Some(u) if u.logged && u.privileges >= privilege_level::SEMIDIOS => {}
        _ => return,
    }

    let admin_name = state.users.get(&conn_id).map(|u| u.char_name.clone()).unwrap_or_default();
    // VB6: N|<admin>> <message> with green/server font
    state.send_console_to(SendTarget::ToAll, &format!("{}>> {}", admin_name, text), font_index::SERVER).await;
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
        state.send_console(conn_id, "Mensaje automatico desactivado.", font_index::INFO).await;
        return;
    }

    let minutes: i32 = if parts.len() > 1 { parts[1].trim().parse().unwrap_or(5) } else { 5 };
    state.auto_msg_active = true;
    state.auto_msg_text = text.to_string();
    state.auto_msg_interval = minutes;
    state.auto_msg_counter = 0;

    state.send_console(conn_id, &format!("Mensaje automatico cada {}min: {}", minutes, text), font_index::INFO).await;
}

/// /EXP multiplier — Set experience multiplier.
pub(super) async fn handle_slash_exp_mult(state: &mut GameState, conn_id: ConnectionId, val: &str) {
    match state.users.get(&conn_id) {
        Some(u) if u.logged && u.privileges >= privilege_level::ADMINISTRADOR => {}
        _ => return,
    }

    let mult: i32 = val.parse().unwrap_or(0);
    if mult < 1 {
        state.send_console(conn_id, "Valor invalido.", font_index::INFO).await;
        return;
    }

    state.multiplicador_exp = mult;
    state.send_msg_id_to(SendTarget::ToAll, 774, &mult.to_string()).await;
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
        state.send_console(conn_id, "Valor invalido.", font_index::INFO).await;
        return;
    }

    state.multiplicador_oro = mult;
    state.send_msg_id_to(SendTarget::ToAll, 775, &mult.to_string()).await;
    info!("[GM] Gold multiplier set to {}x", mult);
}

/// /DROP multiplier — Set drop multiplier.
pub(super) async fn handle_slash_drop_mult(state: &mut GameState, conn_id: ConnectionId, val: &str) {
    match state.users.get(&conn_id) {
        Some(u) if u.logged && u.privileges >= privilege_level::ADMINISTRADOR => {}
        _ => return,
    }

    let mult: i32 = val.parse().unwrap_or(0);
    if mult < 1 {
        state.send_console(conn_id, "Valor invalido.", font_index::INFO).await;
        return;
    }

    state.multiplicador_drop = mult;
    state.send_msg_id_to(SendTarget::ToAll, 776, &mult.to_string()).await;
    info!("[GM] Drop multiplier set to {}x", mult);
}

/// /OFF — Shutdown server.
pub(super) async fn handle_slash_off(state: &mut GameState, conn_id: ConnectionId) {
    match state.users.get(&conn_id) {
        Some(u) if u.logged && u.privileges >= privilege_level::ADMINISTRADOR => {}
        _ => return,
    }

    let admin_name = state.users.get(&conn_id).map(|u| u.char_name.clone()).unwrap_or_default();
    info!("[GM] {} shutting down server", admin_name);

    // Send shutdown message to all
    state.send_console_to(SendTarget::ToAll, &format!("Servidor apagado por {}.", admin_name), font_index::FIGHT).await;

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
    let to_kick: Vec<ConnectionId> = state.users.values()
        .filter(|u| u.logged && u.privileges < privilege_level::CONSEJERO)
        .map(|u| u.conn_id)
        .collect();

    let count = to_kick.len();
    for tc in to_kick {
        if let Some(w) = state.writers.get_mut(&tc) {
            w.shutdown().await;
        }
    }

    let admin_name = state.users.get(&conn_id).map(|u| u.char_name.clone()).unwrap_or_default();
    state.send_console(conn_id, &format!("{} jugadores desconectados.", count), font_index::INFO).await;
    info!("[GM] {} kicked all {} players", admin_name, count);
}

/// /NOGLOBAL — Toggle global chat on/off (GM Dios+ command). VB6: frmMain.frm
pub(super) async fn handle_slash_noglobal(state: &mut GameState, conn_id: ConnectionId) {
    let priv_level = state.users.get(&conn_id).map(|u| u.privileges).unwrap_or(0);
    if priv_level < 4 { return; } // Dios+
    state.chat_global = !state.chat_global;
    if state.chat_global {
        state.send_msg_id_to(SendTarget::ToAll, 803, "").await;
    } else {
        state.send_msg_id_to(SendTarget::ToAll, 804, "").await;
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
    state.send_console(conn_id, &format!("Online: {} | NPCs: {} | Record: {}", online, npc_count, state.record_users), font_index::INFO).await;
}

/// /CT map x y — Create teleport at current position (requires map .inf modification).
pub(super) async fn handle_slash_ct(state: &mut GameState, conn_id: ConnectionId, args: &str) {
    match state.users.get(&conn_id) {
        Some(u) if u.logged && u.privileges >= privilege_level::DIOS => {}
        _ => return,
    }
    // Teleport creation requires modifying map .inf files which is not yet supported.
    // In VB6 this modifies MapData().TileExit in memory.
    state.send_console(conn_id, "Creacion de teleports no soportada aun (requiere edicion de .inf).", font_index::INFO).await;
}

/// /DT — Destroy teleport at current position.
pub(super) async fn handle_slash_dt(state: &mut GameState, conn_id: ConnectionId) {
    match state.users.get(&conn_id) {
        Some(u) if u.logged && u.privileges >= privilege_level::DIOS => {}
        _ => return,
    }
    state.send_console(conn_id, "Destruccion de teleports no soportada aun (requiere edicion de .inf).", font_index::INFO).await;
}

/// /RESMAP — Respawn all NPCs on current map.
pub(super) async fn handle_slash_resmap(state: &mut GameState, conn_id: ConnectionId) {
    let map = match state.users.get(&conn_id) {
        Some(u) if u.logged && u.privileges >= privilege_level::DIOS => u.pos_map,
        _ => return,
    };

    // Respawn dead NPCs on this map (using existing respawn logic)
    let mut respawned = 0;
    let dead_npcs: Vec<usize> = state.npcs.iter().enumerate()
        .filter_map(|(i, n)| {
            n.as_ref().filter(|n| n.map == map && n.min_hp <= 0).map(|_| i)
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

    state.send_console(conn_id, &format!("{} NPCs respawneados en mapa {}.", respawned, map), font_index::INFO).await;
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
    state.send_chat_talk_to(SendTarget::ToArea { map, x, y }, 0, args, 16776960).await;
}

/// /SETDESC nick description — Set NPC/user description.
pub(super) async fn handle_slash_setdesc(state: &mut GameState, conn_id: ConnectionId, args: &str) {
    match state.users.get(&conn_id) {
        Some(u) if u.logged && u.privileges >= privilege_level::DIOS => {}
        _ => return,
    }

    state.send_console(conn_id, "Descripcion actualizada.", font_index::INFO).await;
}

/// /CHEAT nick — Toggle god mode for user (full HP/MP/STA regen).
/// /MODMAPINFO — Modify map properties: PK, PART, LUZ, RGB.
/// VB6: TCP.bas /MODMAPINFO handler (Dios+ privilege).
pub(super) async fn handle_slash_modmapinfo(state: &mut GameState, conn_id: ConnectionId, args: &str) {
    let (map, x, y) = match state.users.get(&conn_id) {
        Some(u) if u.logged => (u.pos_map, u.pos_x, u.pos_y),
        _ => return,
    };

    let parts: Vec<&str> = args.split_whitespace().collect();
    if parts.is_empty() { return; }

    let sub_cmd = parts[0].to_uppercase();
    let map_idx = map as usize;

    match sub_cmd.as_str() {
        "PK" => {
            // VB6: /MODMAPINFO PK <0|1> — 0 = PvP on (pk=true), 1 = Safe (pk=false)
            let val: i32 = parts.get(1).and_then(|s| s.parse().ok()).unwrap_or(-1);
            if val < 0 || val > 1 { return; }
            let pk = val == 0; // VB6 inverts: 0 = PvP, 1 = safe
            if let Some(Some(game_map)) = state.game_data.maps.get_mut(map_idx) {
                game_map.info.pk = pk;
            }
            // Persist to map dat file
            let map_dat = state.base_path.join("maps").join(format!("mapa{}.dat", map));
            let section = format!("Mapa{}", map);
            let _ = crate::config::write_var(map_dat.to_str().unwrap_or(""), &section, "Pk", &val.to_string());
        }
        "PART" => {
            // VB6: /MODMAPINFO PART <particle_id> — Set particle at player tile
            let particle_id: i32 = parts.get(1).and_then(|s| s.parse().ok()).unwrap_or(0);
            if particle_id == 0 { return; }
            let pkt = binary_packets::write_particle_create(particle_id as i16, x as u8, y as u8, 0);
            state.send_data_bytes(SendTarget::ToMap(map), &pkt).await;
        }
        "LUZ" => {
            // VB6: /MODMAPINFO LUZ <range> <R> <G> <B>
            let range: i32 = parts.get(1).and_then(|s| s.parse().ok()).unwrap_or(0);
            if range == 0 { return; }
            let r: i32 = parts.get(2).and_then(|s| s.parse().ok()).unwrap_or(0);
            let g: i32 = parts.get(3).and_then(|s| s.parse().ok()).unwrap_or(0);
            let b: i32 = parts.get(4).and_then(|s| s.parse().ok()).unwrap_or(0);
            let pkt = binary_packets::write_light_create(x as u8, y as u8, range as u8, r as u8, g as u8, b as u8);
            state.send_data_bytes(SendTarget::ToMap(map), &pkt).await;
        }
        "RGB" => {
            // VB6: /MODMAPINFO RGB <R> <G> <B> — Set map ambient light
            let r: i32 = parts.get(1).and_then(|s| s.parse().ok()).unwrap_or(0);
            let g: i32 = parts.get(2).and_then(|s| s.parse().ok()).unwrap_or(0);
            let b: i32 = parts.get(3).and_then(|s| s.parse().ok()).unwrap_or(0);
            if let Some(Some(game_map)) = state.game_data.maps.get_mut(map_idx) {
                game_map.info.r = r;
                game_map.info.g = g;  // Note: VB6 has r/b/g order bug, we keep correct r/g/b
                game_map.info.b = b;
            }
            let pkt = binary_packets::write_ambient_color(r as u8, g as u8, b as u8);
            state.send_data_bytes(SendTarget::ToMap(map), &pkt).await;
        }
        _ => {}
    }
}

pub(super) async fn handle_slash_nave(state: &mut GameState, conn_id: ConnectionId) {
    match state.users.get(&conn_id) {
        Some(u) if u.logged && u.privileges > privilege_level::USER => {}
        _ => return,
    }
    if let Some(user) = state.users.get_mut(&conn_id) {
        user.navigating = !user.navigating;
        let status = if user.navigating { "activada" } else { "desactivada" };
        let conn = user.conn_id;
        state.send_console(conn, &format!("Navegacion {}", status), font_index::SERVER).await;
    }
}

/// /HABILITAR — Toggle server GM-only mode. Requires privileges > 0.
pub(super) async fn handle_slash_habilitar(state: &mut GameState, conn_id: ConnectionId) {
    match state.users.get(&conn_id) {
        Some(u) if u.logged && u.privileges > privilege_level::USER => {}
        _ => return,
    }
    if state.server_solo_gms {
        state.send_msg_id(conn_id, 563, "").await; // Server abierto
        state.server_solo_gms = false;
    } else {
        state.send_msg_id(conn_id, 564, "").await; // Server solo GMs
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
    if msg_text.is_empty() { return; }

    let font_id = match color_str.to_uppercase().as_str() {
        "LILA"     => font_index::VIOLETA,   // closest purple
        "VERDE"    => font_index::VERDE,
        "AZUL"     => font_index::AZUL,
        "ROJO"     => font_index::ROJO,
        "AMARILLO" => font_index::AMARILLO,
        "BLANCO"   => font_index::BLANCO,
        "GRIS"     => font_index::GRIS,
        "NARANJA"  => font_index::NARANJA,
        "CELESTE"  => font_index::CELESTE,
        "MARRON"   => font_index::BORDO,     // closest brown
        "VIOLETA"  => font_index::VIOLETA,
        _ => return,
    };

    state.send_console_to(SendTarget::ToAll, &format!("{}> {}", name, msg_text), font_id).await;
}

/// /RESETVALS <type> — Reset arena/duel/CvC state. Requires Semidios+.
/// /RESETVALS <type> — Reset event values. Types: INVOCACIONES
pub(super) async fn handle_slash_resetvals(state: &mut GameState, conn_id: ConnectionId, val_type: &str) {
    match state.users.get(&conn_id) {
        Some(u) if u.logged && u.privileges >= privilege_level::SEMIDIOS => {}
        _ => return,
    }

    let vt = val_type.to_uppercase();
    match vt.as_str() {
        "INVOCACIONES" => {
            state.send_msg_id(conn_id, 590, "").await;
        }
        _ => {
            state.send_msg_id(conn_id, 585, "").await;
        }
    }
    info!("[GM] Reset vals: {}", vt);
}

// ── Reload configuration commands ───────────────────────────────────────────

/// /RELOADSINI — reload server.ini configuration.
pub(super) async fn handle_reload_sini(state: &mut GameState, conn_id: ConnectionId) {
    let name = match state.users.get(&conn_id) {
        Some(u) if u.logged && u.privileges >= privilege_level::ADMINISTRADOR => u.char_name.clone(),
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

            state.send_console(conn_id, &format!("server.ini recargado ({} roles).", state.role_overrides.len()), font_index::INFO).await;
            info!("[GM] {} reloaded server.ini ({} role overrides)", name, state.role_overrides.len());
        }
        Err(e) => {
            state.send_console(conn_id, &format!("Error recargando server.ini: {}", e), font_index::INFO).await;
        }
    }
}

/// /LOADOBJ — reload Obj.dat (objects database).
pub(super) async fn handle_reload_objects(state: &mut GameState, conn_id: ConnectionId) {
    let name = match state.users.get(&conn_id) {
        Some(u) if u.logged && u.privileges >= privilege_level::ADMINISTRADOR => u.char_name.clone(),
        _ => return,
    };

    let base = state.base_path.clone();
    match crate::data::objects::load_objects(&base) {
        Ok(objects) => {
            let count = objects.len();
            state.game_data.objects = objects;
            state.send_console(conn_id, &format!("Obj.dat recargado ({} objetos).", count), font_index::INFO).await;
            info!("[GM] {} reloaded Obj.dat ({} objects)", name, count);
        }
        Err(e) => {
            state.send_console(conn_id, &format!("Error recargando Obj.dat: {}", e), font_index::INFO).await;
        }
    }
}

/// /LOADHECHIZOS — reload Hechizos.dat (spells database).
pub(super) async fn handle_reload_spells(state: &mut GameState, conn_id: ConnectionId) {
    let name = match state.users.get(&conn_id) {
        Some(u) if u.logged && u.privileges >= privilege_level::ADMINISTRADOR => u.char_name.clone(),
        _ => return,
    };

    let base = state.base_path.clone();
    match crate::data::spells::load_spells(&base) {
        Ok(spells) => {
            let count = spells.len();
            state.game_data.spells = spells;
            state.send_console(conn_id, &format!("Hechizos.dat recargado ({} hechizos).", count), font_index::INFO).await;
            info!("[GM] {} reloaded Hechizos.dat ({} spells)", name, count);
        }
        Err(e) => {
            state.send_console(conn_id, &format!("Error recargando Hechizos.dat: {}", e), font_index::INFO).await;
        }
    }
}

/// /LOADNPCS — reload NPCs.dat + NPCs-HOSTILES.dat.
pub(super) async fn handle_reload_npcs(state: &mut GameState, conn_id: ConnectionId) {
    let name = match state.users.get(&conn_id) {
        Some(u) if u.logged && u.privileges >= privilege_level::ADMINISTRADOR => u.char_name.clone(),
        _ => return,
    };

    let base = state.base_path.clone();
    match crate::data::npcs::load_npcs(&base) {
        Ok(npc_db) => {
            let count = npc_db.count();
            state.game_data.npcs = npc_db;
            state.send_console(conn_id, &format!("NPCs.dat recargado ({} NPCs).", count), font_index::INFO).await;
            info!("[GM] {} reloaded NPCs.dat ({} NPCs)", name, count);
        }
        Err(e) => {
            state.send_console(conn_id, &format!("Error recargando NPCs.dat: {}", e), font_index::INFO).await;
        }
    }
}

/// /LOADBALANCE — reload ClassBonus.dat (balance data).
pub(super) async fn handle_reload_balance(state: &mut GameState, conn_id: ConnectionId) {
    let name = match state.users.get(&conn_id) {
        Some(u) if u.logged && u.privileges >= privilege_level::ADMINISTRADOR => u.char_name.clone(),
        _ => return,
    };

    let base = state.base_path.clone();
    match crate::data::balance::load_balance(&base) {
        Ok(balance) => {
            state.game_data.balance = balance;
            state.send_console(conn_id, "Balance recargado.", font_index::INFO).await;
            info!("[GM] {} reloaded Balance data", name);
        }
        Err(e) => {
            state.send_console(conn_id, &format!("Error recargando Balance: {}", e), font_index::INFO).await;
        }
    }
}


/// /LOADMAP N — reload a specific map from disk.
pub(super) async fn handle_reload_map(state: &mut GameState, conn_id: ConnectionId, map_str: &str) {
    let name = match state.users.get(&conn_id) {
        Some(u) if u.logged && u.privileges >= privilege_level::ADMINISTRADOR => u.char_name.clone(),
        _ => return,
    };

    let map_num: usize = match map_str.trim().parse() {
        Ok(n) if n >= 1 => n,
        _ => {
            state.send_console(conn_id, "Uso: /LOADMAP <numero>", font_index::INFO).await;
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

            state.send_console(conn_id, &format!("Mapa {} recargado.", map_num), font_index::INFO).await;
            info!("[GM] {} reloaded map {}", name, map_num);
        }
        Err(e) => {
            state.send_console(conn_id, &format!("Error recargando mapa {}: {}", map_num, e), font_index::INFO).await;
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
        state.send_console(conn_id, "Uso: /CONT 0-60 (0 = cancelar)", font_index::INFO).await;
        return;
    }

    if seconds == 0 {
        state.countdown_seconds = 0;
        state.send_console(conn_id, "Cuenta regresiva cancelada.", font_index::INFO).await;
    } else {
        state.countdown_seconds = seconds;
        // VB6: broadcasts ||739@seconds
        state.send_msg_id_to(SendTarget::ToAll, 739, &seconds.to_string()).await;
        state.send_console(conn_id, &format!("Cuenta regresiva iniciada: {} segundos.", seconds), font_index::INFO).await;
    }
}

/// /LLUVIA — Toggle rain. Requires DIOS+.
pub(super) async fn handle_slash_lluvia(state: &mut GameState, conn_id: ConnectionId) {
    match state.users.get(&conn_id) {
        Some(u) if u.logged && u.privileges >= privilege_level::DIOS => {}
        _ => {
            state.send_console(conn_id, "No tenes permisos para usar este comando.", font_index::INFO).await;
            return;
        }
    }

    state.raining = !state.raining;
    state.rain_counter = 0;

    let status = if state.raining { "activada" } else { "desactivada" };
    state.send_console(conn_id, &format!("Lluvia {}.", status), font_index::INFO).await;

    // Broadcast rain toggle to all connected users
    state.send_data_bytes(SendTarget::ToAll, &binary_packets::write_rain_toggle()).await;

    let gm_name = state.users.get(&conn_id).map(|u| u.char_name.clone()).unwrap_or_default();
    info!("[GM] {} toggled rain: {}", gm_name, state.raining);
}

/// /NOCHE — Toggle forced night mode. Requires DIOS+.
pub(super) async fn handle_slash_noche(state: &mut GameState, conn_id: ConnectionId) {
    match state.users.get(&conn_id) {
        Some(u) if u.logged && u.privileges >= privilege_level::DIOS => {}
        _ => {
            state.send_console(conn_id, "No tenes permisos para usar este comando.", font_index::INFO).await;
            return;
        }
    }

    state.forced_night = !state.forced_night;

    let status = if state.forced_night { "activada" } else { "desactivada" };
    state.send_console(conn_id, &format!("Noche forzada {}.", status), font_index::INFO).await;

    let gm_name = state.users.get(&conn_id).map(|u| u.char_name.clone()).unwrap_or_default();
    info!("[GM] {} toggled forced night: {}", gm_name, state.forced_night);
}

/// /SHOWNAME — Toggle GM visible name. Requires CONSEJERO+.
pub(super) async fn handle_slash_showname(state: &mut GameState, conn_id: ConnectionId) {
    let (map, x, y) = match state.users.get(&conn_id) {
        Some(u) if u.logged && u.privileges >= privilege_level::CONSEJERO => {
            (u.pos_map, u.pos_x, u.pos_y)
        }
        _ => {
            state.send_console(conn_id, "No tenes permisos para usar este comando.", font_index::INFO).await;
            return;
        }
    };

    let new_val = !state.users.get(&conn_id).map(|u| u.gm_show_name).unwrap_or(false);
    if let Some(user) = state.users.get_mut(&conn_id) {
        user.gm_show_name = new_val;
    }

    // Re-broadcast CC so clients see updated name visibility
    let cc = state.users.get(&conn_id).unwrap().build_cc_binary();
    state.send_data_bytes(SendTarget::ToArea { map, x, y }, &cc).await;

    let status = if new_val { "visible" } else { "oculto" };
    state.send_console(conn_id, &format!("Tu nombre ahora es {}.", status), font_index::INFO).await;
}

/// /MAPMSG <text> — Send message to all users on current map. Requires DIOS+.
pub(super) async fn handle_slash_mapmsg(state: &mut GameState, conn_id: ConnectionId, text: &str) {
    let map = match state.users.get(&conn_id) {
        Some(u) if u.logged && u.privileges >= privilege_level::DIOS => u.pos_map,
        _ => {
            state.send_console(conn_id, "No tenes permisos para usar este comando.", font_index::INFO).await;
            return;
        }
    };

    if text.is_empty() {
        state.send_console(conn_id, "Uso: /MAPMSG mensaje", font_index::INFO).await;
        return;
    }

    let gm_name = state.users.get(&conn_id).map(|u| u.char_name.clone()).unwrap_or_default();
    let msg = format!("[GM {}] {}", gm_name, text);

    // Collect all connection IDs on this map
    let map_users: Vec<ConnectionId> = state.users.values()
        .filter(|u| u.logged && u.pos_map == map)
        .map(|u| u.conn_id)
        .collect();

    for uid in &map_users {
        state.send_console(*uid, &msg, font_index::SERVER).await;
    }

    state.send_console(conn_id, &format!("Mensaje enviado a {} usuarios en mapa {}.", map_users.len(), map), font_index::INFO).await;
    info!("[GM] {} sent MAPMSG on map {}: {}", gm_name, map, text);
}

