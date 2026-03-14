// Allow dead_code globally: VB6-parity infrastructure (protocol, data) declares
// functions/constants that mirror VB6 but aren't all wired up yet.
#![allow(dead_code)]

mod net;
mod protocol;
mod config;
mod data;
mod db;
mod game;

use tracing::{debug, info, error};

use config::ServerConfig;
use game::types::GameState;
use net::listener::ServerEvent;

#[tokio::main]
async fn main() {
    // Initialize logging
    tracing_subscriber::fmt()
        .with_env_filter(
            tracing_subscriber::EnvFilter::try_from_default_env()
                .unwrap_or_else(|_| "ao_server=info".into()),
        )
        .init();

    info!("==============================================");
    info!("  Argentum Nextgen - Server (Rust)");
    info!("  Version 0.1.0");
    info!("==============================================");

    // Determine base path (where server.ini and data folders are)
    // Accept optional CLI arg, default to ./server relative to CWD
    let base_path = std::env::args().nth(1)
        .map(std::path::PathBuf::from)
        .unwrap_or_else(|| std::path::PathBuf::from("./server"));
    let base_path = if base_path.is_relative() {
        std::env::current_dir()
            .expect("Failed to get current directory")
            .join(&base_path)
    } else {
        base_path
    };
    info!("Base path: {}", base_path.display());

    // Load configuration
    let config = match ServerConfig::load(&base_path) {
        Ok(cfg) => {
            info!("server.ini loaded successfully");
            info!("  Port: {}", cfg.port);
            info!("  Max users: {}", cfg.max_users);
            info!("  Version: {}", cfg.version);
            info!("  Start position: Map {} ({}, {})", cfg.start_map, cfg.start_x, cfg.start_y);
            info!("  EXP multiplier: {}x", cfg.exp_multiplier);
            info!("  Can create characters: {}", cfg.can_create_characters);
            info!("  Multi-login: {}", if cfg.allow_multi_logins { "allowed" } else { "blocked" });
            info!("  Security: max_pkt/s={}, ip_max_conn={}, ip_min_ms={}, flood_strikes={}",
                cfg.max_packets_per_second.unwrap_or(60),
                cfg.ip_max_connections.unwrap_or(10),
                cfg.ip_min_interval_ms.unwrap_or(500),
                cfg.flood_strike_limit.unwrap_or(3));
            cfg
        }
        Err(e) => {
            error!("Failed to load server.ini: {}", e);
            error!("Make sure you're running from the server directory");
            std::process::exit(1);
        }
    };

    if !config.notice.is_empty() {
        info!("Notice: {}", config.notice);
    }

    // Load all game data (objects, spells, NPCs, maps, experience)
    let game_data = match data::GameData::load(&base_path) {
        Ok(gd) => {
            info!("All game data loaded successfully");
            gd
        }
        Err(e) => {
            error!("Failed to load game data: {}", e);
            std::process::exit(1);
        }
    };

    // Load .env for DATABASE_URL
    let _ = dotenvy::dotenv();
    let database_url = std::env::var("DATABASE_URL")
        .unwrap_or_else(|_| "postgres://ao:ao_secret@localhost:5432/ao_server".to_string());

    // Initialize database pool and run migrations
    let pool = match db::init_pool(&database_url).await {
        Ok(p) => p,
        Err(e) => {
            error!("Failed to initialize database: {}", e);
            std::process::exit(1);
        }
    };

    // Load bans and rankings from DB
    let bans = db::bans::BanList::load(&pool).await;

    // Initialize game state
    let mut state = GameState::new(config.clone(), base_path, game_data, pool, bans);
    info!("Game state initialized");

    // Spawn NPCs from map data
    let npc_count = state.spawn_map_npcs();
    info!("Spawned {} NPCs from map data", npc_count);

    // Load static map objects (doors, items from .inf files) into world grid
    let obj_count = state.load_map_objects();
    info!("Loaded {} static map objects (doors, items)", obj_count);

    // Start TCP server
    let mut events = match net::TcpServer::start(
        "0.0.0.0",
        config.port,
        config.max_users,
    ).await {
        Ok(rx) => rx,
        Err(e) => {
            error!("Failed to start TCP server: {}", e);
            std::process::exit(1);
        }
    };

    info!("Server is running. Waiting for connections...");

    // Graceful shutdown signal (Ctrl+C / SIGTERM)
    let mut shutdown_signal = Box::pin(async {
        let ctrl_c = tokio::signal::ctrl_c();
        #[cfg(unix)]
        {
            let mut sigterm = tokio::signal::unix::signal(tokio::signal::unix::SignalKind::terminate())
                .expect("Failed to register SIGTERM handler");
            tokio::select! {
                _ = ctrl_c => {},
                _ = sigterm.recv() => {},
            }
        }
        #[cfg(not(unix))]
        {
            ctrl_c.await.ok();
        }
    });

    // Game tick (40ms — anti-cheat interval decrements, matches VB6 TimerRestoTiempo)
    let mut game_tick = tokio::time::interval(std::time::Duration::from_millis(40));
    // AI tick timer — VB6: TIMER_AI.Interval = IntervaloNpcAI (default 1300ms from server.ini)
    let ai_interval_ms = config.npc_ai_interval_ms.max(100); // floor at 100ms
    let mut ai_tick = tokio::time::interval(std::time::Duration::from_millis(ai_interval_ms));
    info!("NPC AI interval: {}ms", ai_interval_ms);
    // Respawn timer (every 30 seconds check for dead NPCs to respawn)
    let mut respawn_tick = tokio::time::interval(std::time::Duration::from_secs(30));
    // Player passive timer (1s — hunger/thirst drain, stamina regen, poison)
    let mut player_tick = tokio::time::interval(std::time::Duration::from_secs(1));
    // World cleanup timer (60s — VB6: LimpiezaTimer.Interval = 60000)
    // Items dropped on ground get 10 ticks × 60s = 10 minutes before removal
    let mut cleanup_tick = tokio::time::interval(std::time::Duration::from_secs(60));

    // Security tick (1s — packet rate reset, flood strike check, kick offenders)
    let mut security_tick = tokio::time::interval(std::time::Duration::from_secs(1));

    // Main event loop
    loop {
        tokio::select! {
            // Graceful shutdown — save all users and exit
            _ = &mut shutdown_signal => {
                info!("Server shutting down... saving all users");
                game::handlers::auto_save_all_users(&state).await;
                info!("All users saved. Goodbye!");
                break;
            }

            Some(event) = events.recv() => {
        match event {
            ServerEvent::NewConnection(writer) => {
                let id = writer.id;
                let ip = writer.ip();
                debug!("[CONN] #{} connected from {}", id, ip);

                // Check IP ban
                if state.bans.is_ip_banned(&ip) {
                    info!("[CONN] #{} IP banned, dropping", id);
                    let mut w = writer;
                    w.shutdown().await;
                    continue;
                }

                // IP security: rate-limit + max connections per IP (SecurityIp.bas)
                if !game::handlers::ip_security_accept(&mut state, &ip) {
                    info!("[CONN] #{} IP rate-limited or max connections exceeded, dropping", id);
                    let mut w = writer;
                    w.shutdown().await;
                    continue;
                }

                state.add_connection(writer);
            }

            ServerEvent::PacketReceived(conn_id, data) => {
                // 13.3 binary protocol: raw bytes, no encryption.
                // Handles multi-packet TCP reads and partial packet buffering.
                if !data.is_empty() {
                    game::handlers::handle_packet_stream(&mut state, conn_id, &data).await;
                }
            }

            ServerEvent::Disconnected(conn_id) => {
                // Get user info and save charfile before removal
                let user_info = state.users.get(&conn_id)
                    .filter(|u| u.logged)
                    .map(|u| (u.char_name.clone(), u.pos_map, u.pos_x, u.pos_y, u.char_index, u.area_min_x, u.area_min_y));

                if let Some((name, map, x, y, char_index, area_min_x, area_min_y)) = user_info {
                    info!("[DISC] #{} '{}' disconnected — saving charfile", conn_id, name);

                    // VB6: Clear any active FX before removing the character
                    let fx_clear_pkt = protocol::binary_packets::write_create_fx(char_index.0 as i16, 0, 0);
                    state.send_data_bytes(
                        game::types::SendTarget::ToArea { map, x, y },
                        &fx_clear_pkt,
                    );

                    // VB6 CloseUser: QDL sent to entire map, BP sent to 27×27 area
                    // QDL removes dialog labels — must reach all players on the map (VB6: ToMapButIndex)
                    let qdl_pkt = protocol::binary_packets::write_remove_char_dialog(char_index.0 as i16);
                    state.send_data_bytes(
                        game::types::SendTarget::ToMapButIndex { conn_id, map },
                        &qdl_pkt,
                    );

                    // BP (remove char) — VB6 EraseUserChar uses SendToUserArea (27×27 zone)
                    let bp_pkt = protocol::binary_packets::write_character_remove(char_index.0 as i16);
                    if area_min_x > 0 || area_min_y > 0 {
                        // Use the full 27×27 area zone — matches movement broadcast range
                        let amx = area_min_x.max(1);
                        let amy = area_min_y.max(1);
                        let axx = (area_min_x + 26).min(100);
                        let axy = (area_min_y + 26).min(100);
                        if let Some(grid) = state.world.grid(map) {
                            let mut targets: Vec<net::ConnectionId> = Vec::new();
                            for sy in amy..=axy {
                                for sx in amx..=axx {
                                    if let Some(tile) = grid.tile(sx, sy) {
                                        if let Some(c) = tile.user_conn {
                                            if c != conn_id { targets.push(c); }
                                        }
                                    }
                                }
                            }
                            for c in &targets {
                                state.send_bytes(*c, &bp_pkt);
                            }
                        }
                    } else {
                        // Fallback: send to entire map (safe catch-all)
                        state.send_data_bytes(
                            game::types::SendTarget::ToMapButIndex { conn_id, map },
                            &bp_pkt,
                        );
                    }

                    // Save full character state to DB (always save on disconnect, regardless of dirty flag)
                    if let Some(user) = state.users.get(&conn_id) {
                        let save_data = game::handlers::build_char_save_data(user);
                        let pool = state.pool.clone();
                        match db::charfile::save_charfile(&pool, &name, &save_data).await {
                            Ok(()) => info!("[DISC] Character saved for '{}'", name),
                            Err(e) => {
                                error!("[DISC] Failed to save character '{}': {}", name, e);
                                // Fallback: at least clear the logged flag so the user can reconnect
                                let _ = db::charfile::set_logged_flag(&pool, &name, false).await;
                            }
                        }
                    }
                } else {
                    debug!("[DISC] #{} disconnected (no login)", conn_id);
                }

                // Decrement IP connection count
                if let Some(user) = state.users.get(&conn_id) {
                    let ip = user.ip.clone();
                    if let Some(count) = state.ip_connection_count.get_mut(&ip) {
                        if *count > 0 {
                            *count -= 1;
                        }
                    }
                }

                state.remove_connection(conn_id);

                // VB6 MostrarNumUsers: broadcast updated online count to all remaining players
                let on_pkt = protocol::binary_packets::write_online_count(state.num_users as i16);
                state.send_data_bytes(game::types::SendTarget::ToAll, &on_pkt);
            }
        }
            } // end tokio::select Some(event)

            // Game tick — anti-cheat interval decrements (every 40ms)
            _ = game_tick.tick() => {
                game::handlers::tick_intervals(&mut state).await;
            }

            // AI tick — NPC movement and combat (every 100ms)
            _ = ai_tick.tick() => {
                game::handlers::tick_npc_ai(&mut state).await;
            }

            // Respawn tick — revive dead NPCs (every 30s)
            _ = respawn_tick.tick() => {
                game::handlers::tick_npc_respawn(&mut state).await;
            }

            // Player passive tick — regen/drain (every 1s)
            _ = player_tick.tick() => {
                game::handlers::tick_player_passive(&mut state).await;
            }

            // World cleanup tick — auto-remove ground items (every 60s, VB6: LimpiezaTimer)
            _ = cleanup_tick.tick() => {
                game::handlers::tick_clean_world(&mut state).await;
            }

            // Security tick — rate limit reset, flood detection, kick offenders (every 1s)
            _ = security_tick.tick() => {
                game::handlers::tick_security(&mut state);

                // Drain security kick queue — disconnect flagged connections
                let kicks: Vec<_> = std::mem::take(&mut state.security_kick_queue);
                for conn_id in kicks {
                    if let Some(mut writer) = state.writers.remove(&conn_id) {
                        writer.shutdown().await;
                    }
                    // Clean up recv buffers and counters
                    state.recv_buffers.remove(&conn_id);
                    state.packet_counts.remove(&conn_id);
                    state.flood_strikes.remove(&conn_id);

                    // Decrement IP connection count
                    if let Some(user) = state.users.get(&conn_id) {
                        let ip = user.ip.clone();
                        if let Some(count) = state.ip_connection_count.get_mut(&ip) {
                            *count = count.saturating_sub(1);
                        }
                    }

                    state.remove_connection(conn_id);
                    info!("[SEC] Connection #{} kicked for flood", conn_id);
                }
            }
        } // end tokio::select!

        // Flush all buffered writes to TCP sockets (write batching).
        // This is the single point where bytes actually hit the wire,
        // reducing syscalls from hundreds-per-tick to one-per-connection.
        state.flush_all_writers().await;
    } // end loop
}
