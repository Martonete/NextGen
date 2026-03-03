// Suppress warnings for VB6-parity code that is declared but not yet wired up.
// These functions, constants, and imports will be used as more features are connected.
#![allow(dead_code, unused_variables, unused_imports, unused_assignments, dropping_references)]

mod crypto;
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
    info!("  Tierras Sagradas AO - Server (Rust)");
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
            info!("  Encryption: {}", if cfg.encrypt { "enabled" } else { "disabled" });
            info!("  EXP multiplier: {}x", cfg.exp_multiplier);
            info!("  Can create characters: {}", cfg.can_create_characters);
            info!("  Multi-login: {}", if cfg.allow_multi_logins { "allowed" } else { "blocked" });
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
    let ranking = db::ranking::load_ranking(&pool).await;

    // Initialize game state
    let mut state = GameState::new(config.clone(), base_path, game_data, pool, bans, ranking);
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

    // Main event loop
    loop {
        tokio::select! {
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
                // Convert to string for opcode parsing (VB6 uses text-based protocol)
                // VB6 client sends Latin-1 (Windows-1252), not UTF-8.
                // Each byte maps directly to its Unicode code point.
                let data_str: String = data.iter().map(|&b| b as char).collect();
                // trim() removes whitespace; also strip trailing null bytes and control chars
                // that may survive decryption (VB6 string handling artifacts)
                let data_str = data_str.trim().trim_end_matches(|c: char| c.is_control());

                if !data_str.is_empty() {
                    // Debug: log packets starting with COMP or VEND
                    if data_str.starts_with("COMP") || data_str.starts_with("VEND") {
                        tracing::info!("[RAW-PKT] #{} '{}'", conn_id, data_str);
                    }
                    game::handlers::handle_packet(&mut state, conn_id, data_str).await;
                }
            }

            ServerEvent::Disconnected(conn_id) => {
                // Handle CvC disconnect (counts as death for scoring)
                let in_cvc = state.users.get(&conn_id).map(|u| u.en_cvc).unwrap_or(false);
                if in_cvc && state.cvc_funciona {
                    game::handlers::cvc_player_disconnect(&mut state, conn_id).await;
                }

                // Get user info and save charfile before removal
                let user_info = state.users.get(&conn_id)
                    .filter(|u| u.logged)
                    .map(|u| (u.char_name.clone(), u.pos_map, u.pos_x, u.pos_y, u.char_index, u.area_min_x, u.area_min_y));

                if let Some((name, map, x, y, char_index, area_min_x, area_min_y)) = user_info {
                    info!("[DISC] #{} '{}' disconnected — saving charfile", conn_id, name);

                    // VB6 CloseUser: QDL sent to entire map, BP sent to 27×27 area
                    // QDL removes dialog labels — must reach all players on the map (VB6: ToMapButIndex)
                    let qdl_pkt = format!("QDL{}", char_index.0);
                    state.send_data(
                        game::types::SendTarget::ToMapButIndex { conn_id, map },
                        &qdl_pkt,
                    ).await;

                    // BP (remove char) — VB6 EraseUserChar uses SendToUserArea (27×27 zone)
                    let bp_pkt = format!("BP{}", char_index.0);
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
                                state.send_to(*c, &bp_pkt).await;
                            }
                        }
                    } else {
                        // Fallback: send to entire map (safe catch-all)
                        state.send_data(
                            game::types::SendTarget::ToMapButIndex { conn_id, map },
                            &bp_pkt,
                        ).await;
                    }

                    // Save full character state to DB
                    if let Some(user) = state.users.get(&conn_id) {
                        let save_data = db::charfile::CharSaveData {
                            head: if user.navigating { user.old_head } else { user.head },
                            body: user.body,
                            heading: user.heading,
                            weapon: user.weapon_anim,
                            shield: user.shield_anim,
                            helmet: user.casco_anim,
                            map: user.pos_map,
                            x: user.pos_x,
                            y: user.pos_y,
                            level: user.level,
                            exp: user.exp,
                            max_hp: user.max_hp,
                            min_hp: user.min_hp,
                            max_sta: user.max_sta,
                            min_sta: user.min_sta,
                            max_mana: user.max_mana,
                            min_mana: user.min_mana,
                            max_hit: user.max_hit,
                            min_hit: user.min_hit,
                            max_agua: user.max_agua,
                            min_agua: user.min_agua,
                            max_ham: user.max_ham,
                            min_ham: user.min_ham,
                            gold: user.gold,
                            bank_gold: user.bank_gold,
                            attributes: user.attributes,
                            skills: user.skills,
                            dead: user.dead,
                            poisoned: user.poisoned,
                            paralyzed: user.paralyzed,
                            criminal: user.criminal,
                            hidden: user.hidden,
                            navigating: user.navigating,
                            barco_slot: user.barco_slot,
                            montado: user.montado,
                            levitando: user.levitando,
                            montado_body: user.montado_body,
                            privileges: user.saved_privileges,
                            spells: user.spells,
                            inventory: user.inventory.iter().map(|s| (s.obj_index, s.amount, s.equipped)).collect(),
                            bank: user.bank.iter().map(|s| (s.obj_index, s.amount)).collect(),
                            weapon_eqp_slot: user.equip.weapon,
                            armour_eqp_slot: user.equip.armor,
                            shield_eqp_slot: user.equip.shield,
                            helmet_eqp_slot: user.equip.helmet,
                            municion_eqp_slot: user.equip.municion,
                            reputation: user.reputation,
                            guild_index: user.guild_index,
                            criminales_matados: user.criminales_matados,
                            ciudadanos_matados: user.ciudadanos_matados,
                            ejercito_real: user.armada_real,
                            ejercito_caos: user.fuerzas_caos,
                            skill_pts_libres: user.skill_pts_libres,
                            puntos_donacion: user.puntos_donacion,
                            puntos_torneo: user.puntos_torneo,
                            ts_points: user.ts_points,
                        };
                        let pool = state.pool.clone();
                        match db::charfile::save_charfile(&pool, &name, &save_data).await {
                            Ok(()) => info!("[DISC] Character saved for '{}'", name),
                            Err(e) => error!("[DISC] Failed to save character '{}': {}", name, e),
                        }
                    }
                } else {
                    debug!("[DISC] #{} disconnected (no login)", conn_id);
                }

                // Notify friends that this user went offline
                game::handlers::broadcast_friend_disconnect(&mut state, conn_id).await;

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
                let on_pkt = format!("ON{}", state.num_users);
                state.send_data(game::types::SendTarget::ToAll, &on_pkt).await;
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
                game::handlers::tick_nobleza(&mut state).await;
                game::handlers::tick_siege(&mut state).await;
                game::handlers::tick_guerra(&mut state).await;
                game::handlers::tick_eventos(&mut state).await;
                game::handlers::tick_ancalagon(&mut state).await;
            }

            // World cleanup tick — auto-remove ground items (every 60s, VB6: LimpiezaTimer)
            _ = cleanup_tick.tick() => {
                game::handlers::tick_clean_world(&mut state).await;
            }
        } // end tokio::select!
    } // end loop
}
