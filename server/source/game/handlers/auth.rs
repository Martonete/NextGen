//! Authentication handlers: login, account creation, character creation/deletion,
//! password change, account recovery, and connect_user flow.
//! Extracted from mod.rs to reduce file size.

use tracing::{info, warn};

use crate::net::ConnectionId;
use crate::protocol::{fields::read_field, binary_packets};
use crate::db::{accounts, charfile, password};
use crate::data::objects::ObjType;
use crate::game::class_race::{PlayerClass, PlayerRace};
use crate::game::types::{GameState, SendTarget, MAX_INVENTORY_SLOTS, MAX_NORMAL_INVENTORY_SLOTS};
use crate::game::constants::{DEAD_BODY_NEUTRAL, DEAD_HEAD_NEUTRAL};
use crate::game::world;
use super::common::*;
use super::{
    send_full_inventory, send_full_spells, send_warp_fx, make_user_visible, build_anm_packet,
    naked_body, default_head_for_race,
};

// =====================================================================
// Pre-login handlers
// =====================================================================

/// HardwareCheck — Hardware serial check (first packet from client).
pub(super) async fn handle_hardware_check(state: &mut GameState, conn_id: ConnectionId, hd_serial: &str) {
    info!("[AUTH] HD serial check from #{}: HD={}", conn_id, hd_serial);

    let is_banned = state.bans.is_hd_banned(&hd_serial);

    if let Some(user) = state.users.get_mut(&conn_id) {
        user.hd_serial = hd_serial.to_string();
        user.paso_hd = !is_banned;
    }

    if is_banned {
        warn!("[AUTH] HD banned, connection #{} will be rejected", conn_id);
    }
}

const MAX_AUTH_FAILURES: u32 = 5;
const AUTH_LOCKOUT_SECS: u64 = 300;

/// Result of a rate-limit check on an IP address.
enum RateLimitResult {
    /// Request is allowed (no record or count below threshold).
    Allow,
    /// The lockout window has expired; the stale entry was cleared by the caller.
    Expired,
    /// IP is locked out — too many failures within the window.
    Locked,
}

/// Check the per-IP auth failure counter against the global thresholds.
///
/// Does NOT mutate `failures` — the caller must remove the entry on `Expired`
/// to keep the borrow checker happy (avoids holding a `&mut` across checks).
fn check_rate_limit(
    failures: &std::collections::HashMap<String, (u32, std::time::Instant)>,
    ip: &str,
) -> RateLimitResult {
    match failures.get(ip) {
        Some((count, first_time)) => {
            if first_time.elapsed().as_secs() >= AUTH_LOCKOUT_SECS {
                RateLimitResult::Expired
            } else if *count >= MAX_AUTH_FAILURES {
                RateLimitResult::Locked
            } else {
                RateLimitResult::Allow
            }
        }
        None => RateLimitResult::Allow,
    }
}

/// AccountLogin — Account login.
pub(super) async fn handle_account_login(state: &mut GameState, conn_id: ConnectionId, account_name: &str, password: &str) {
    info!("[AUTH] Login attempt: account='{}' pass_len={} pass_bytes={:?} from #{}", account_name, password.len(), password.as_bytes(), conn_id);

    let paso_hd = state.users.get(&conn_id).map(|u| u.paso_hd).unwrap_or(false);
    if !paso_hd {
        state.send_bytes(conn_id, &binary_packets::write_error_msg("Tu PC se encuentra bajo Tolerancia 0."));
        close_connection(state, conn_id).await;
        return;
    }

    // Rate limiting: check per-IP failed-attempt counter before processing credentials.
    let ip = state.users.get(&conn_id).map(|u| u.ip.clone()).unwrap_or_default();
    if !ip.is_empty() {
        match check_rate_limit(&state.auth_failures, &ip) {
            RateLimitResult::Expired => { state.auth_failures.remove(&ip); }
            RateLimitResult::Locked => {
                warn!("[AUTH] Rate limit: IP {} is locked out (too many failed attempts)", ip);
                state.send_bytes(conn_id, &binary_packets::write_error_msg(
                    "Demasiados intentos fallidos. Intente nuevamente en 5 minutos."
                ));
                return;
            }
            RateLimitResult::Allow => {}
        }
    }

    if !is_valid_name(&account_name) {
        state.send_bytes(conn_id, &binary_packets::write_error_msg("Nombre de cuenta invalido."));
        return; // Don't disconnect — let client retry
    }

    if !accounts::account_exists(&state.pool, &account_name).await {
        state.send_bytes(conn_id, &binary_packets::write_error_msg("La cuenta no existe. Verificá el nombre e intentá nuevamente."));
        return;
    }

    let account = match accounts::load_account(&state.pool, &account_name).await {
        Ok(acc) => acc,
        Err(e) => {
            warn!("[AUTH] Failed to load account '{}': {}", account_name, e);
            state.send_bytes(conn_id, &binary_packets::write_error_msg("Error interno al leer la cuenta. Intentá nuevamente."));
            return;
        }
    };

    if !password::verify_password(&password, &account.password_hash) {
        // Record failure for rate limiting.
        if !ip.is_empty() {
            let entry = state.auth_failures.entry(ip.clone()).or_insert((0, std::time::Instant::now()));
            entry.0 += 1;
            warn!("[AUTH] Wrong password for account '{}' from {} (failure #{}/{})", account_name, ip, entry.0, MAX_AUTH_FAILURES);
        }
        state.send_bytes(conn_id, &binary_packets::write_error_msg("Contraseña incorrecta. Verificá e intentá nuevamente."));
        return;
    }

    if account.banned {
        let motivo = &account.ban_reason;
        let ban_by = read_field(2, motivo, ',');
        let ban_reason = read_field(1, motivo, ',');
        state.send_bytes(conn_id, &binary_packets::write_error_msg(
            &format!("Tu cuenta se encuentra actualmente baneada por: {} con motivo: {}.", ban_by, ban_reason)
        ));
        close_connection(state, conn_id).await;
        return;
    }

    // Successful login — clear any accumulated failure counter for this IP.
    if !ip.is_empty() {
        state.auth_failures.remove(&ip);
    }

    if let Some(user) = state.users.get_mut(&conn_id) {
        user.account_name = account_name.to_string();
        user.account_id = account.id;
    }

    state.send_bytes(conn_id, &binary_packets::write_init_account(account.num_pjs as u8, &state.notice));

    for i in 0..account.num_pjs {
        let pj_name = &account.characters[i];
        if pj_name.is_empty() {
            continue;
        }
        match charfile::load_char_preview(&state.pool, pj_name).await {
            Ok(preview) => {
                // Resolve equip slots → obj indices → animation indices
                let (weapon_obj, shield_obj, helmet_obj) =
                    charfile::load_equipped_obj_indices(&state.pool, pj_name).await;

                let mut weapon_anim: i16 = 0;
                let mut shield_anim: i16 = 0;
                let mut helmet_anim: i16 = 0;

                if weapon_obj >= 1 {
                    if let Some(obj) = state.game_data.objects.get((weapon_obj - 1) as usize) {
                        weapon_anim = obj.weapon_anim as i16;
                    }
                }
                if shield_obj >= 1 {
                    if let Some(obj) = state.game_data.objects.get((shield_obj - 1) as usize) {
                        shield_anim = obj.shield_anim as i16;
                    }
                }
                if helmet_obj >= 1 {
                    if let Some(obj) = state.game_data.objects.get((helmet_obj - 1) as usize) {
                        helmet_anim = obj.casco_anim as i16;
                    }
                }

                state.send_bytes(conn_id, &binary_packets::write_add_pj(
                    pj_name, (i + 1) as u8,
                    preview.head as i16, preview.body as i16,
                    weapon_anim, shield_anim, helmet_anim,
                    preview.level as u8, &preview.class, preview.dead, &preview.race,
                ));
            }
            Err(e) => {
                warn!("[AUTH] Failed to load preview for '{}': {}", pj_name, e);
            }
        }
    }

    state.send_bytes(conn_id, &binary_packets::write_security_code(&account.security_code));

    info!("[AUTH] Account '{}' authenticated, {} characters", account_name, account.num_pjs);
}

/// CreateAccount — Create new account.
pub(super) async fn handle_create_account(state: &mut GameState, conn_id: ConnectionId, account_name: &str, password: &str, pin: &str) {
    info!("[AUTH] New account request: '{}' from #{}", account_name, conn_id);

    // Rate limiting: block IPs that have exceeded the failed-attempt threshold.
    let ip = state.users.get(&conn_id).map(|u| u.ip.clone()).unwrap_or_default();
    if !ip.is_empty() {
        match check_rate_limit(&state.auth_failures, &ip) {
            RateLimitResult::Expired => { state.auth_failures.remove(&ip); }
            RateLimitResult::Locked => {
                warn!("[AUTH] Rate limit: IP {} locked out from account creation", ip);
                state.send_bytes(conn_id, &binary_packets::write_error_msg(
                    "Demasiados intentos fallidos. Intente nuevamente en 5 minutos."
                ));
                close_connection(state, conn_id).await;
                return;
            }
            RateLimitResult::Allow => {}
        }
    }

    if !is_valid_name(&account_name) {
        state.send_bytes(conn_id, &binary_packets::write_error_msg("Nombre de cuenta invalido."));
        close_connection(state, conn_id).await;
        return;
    }

    if account_name.len() < 3 {
        state.send_bytes(conn_id, &binary_packets::write_error_msg("El nombre de la cuenta debe tener al menos 3 caracteres."));
        close_connection(state, conn_id).await;
        return;
    }

    if password.len() < 3 {
        state.send_bytes(conn_id, &binary_packets::write_error_msg("La password debe tener al menos 3 caracteres."));
        close_connection(state, conn_id).await;
        return;
    }

    // PIN validation: 4-10 numeric digits only
    if pin.len() < 4 || pin.len() > 10 || !pin.chars().all(|c| c.is_ascii_digit()) {
        state.send_bytes(conn_id, &binary_packets::write_error_msg("El PIN debe ser de 4 a 10 digitos numericos."));
        close_connection(state, conn_id).await;
        return;
    }

    let password_hash = match password::hash_password(&password) {
        Ok(h) => h,
        Err(e) => {
            warn!("[AUTH] Failed to hash password: {}", e);
            state.send_bytes(conn_id, &binary_packets::write_error_msg("Error interno del servidor."));
            close_connection(state, conn_id).await;
            return;
        }
    };

    match accounts::create_account(
        &state.pool,
        &account_name,
        &password_hash,
        &pin,
        &state.security_code,
    ).await {
        Ok(_account_id) => {
            info!("[AUTH] Account '{}' created successfully", account_name);
            state.send_bytes(conn_id, &binary_packets::write_error_msg("Cuenta creada con exito!"));
            close_connection(state, conn_id).await;
        }
        Err(e) => {
            // Count creation failures (e.g. duplicate account name) toward the lockout.
            if !ip.is_empty() {
                let entry = state.auth_failures.entry(ip.clone()).or_insert((0, std::time::Instant::now()));
                entry.0 += 1;
                warn!("[AUTH] Account creation failed for '{}' from {} (failure #{}/{}): {}", account_name, ip, entry.0, MAX_AUTH_FAILURES, e);
            }
            state.send_bytes(conn_id, &binary_packets::write_error_msg(&e.to_string()));
            close_connection(state, conn_id).await;
        }
    }
}

/// CharacterSelect — Character login (primary, with full validation).
pub(super) async fn handle_character_select(state: &mut GameState, conn_id: ConnectionId, char_name: &str, account: &str, codex: &str) {
    info!("[AUTH] Character login (CharacterSelect): '{}' account='{}' from #{}", char_name, account, conn_id);

    let paso_hd = state.users.get(&conn_id).map(|u| u.paso_hd).unwrap_or(false);
    if !paso_hd {
        state.send_bytes(conn_id, &binary_packets::write_error_msg("Tu PC se encuentra bajo Tolerancia 0."));
        close_connection(state, conn_id).await;
        return;
    }

    if !is_valid_name(&char_name) {
        state.send_bytes(conn_id, &binary_packets::write_error_msg("Nombre invalido."));
        close_connection(state, conn_id).await;
        return;
    }

    if !charfile::character_exists(&state.pool, &char_name).await {
        state.send_bytes(conn_id, &binary_packets::write_error_msg("El personaje no existe."));
        close_connection(state, conn_id).await;
        return;
    }

    if is_char_banned(&state.pool, &char_name).await {
        state.send_bytes(conn_id, &binary_packets::write_error_msg(
            "Se te ha prohibido la entrada a Argentum Nextgen debido a tu mal comportamiento. Consulta a un administrador para saber el motivo de la prohibicion."
        ));
        close_connection(state, conn_id).await;
        return;
    }

    let expected_account = state.users.get(&conn_id)
        .map(|u| u.account_name.clone())
        .unwrap_or_default();
    if !expected_account.is_empty() && account.to_uppercase() != expected_account.to_uppercase() {
        state.send_bytes(conn_id, &binary_packets::write_error_msg("Error al conectar, intente de nuevo."));
        close_connection(state, conn_id).await;
        return;
    }

    connect_user(state, conn_id, char_name, account, codex).await;
}

/// CharacterLogin — Character login (simplified variant).
pub(super) async fn handle_character_login(state: &mut GameState, conn_id: ConnectionId, char_name: &str, account: &str, codex: &str) {
    info!("[AUTH] Character login (CharacterLogin): '{}' from #{}", char_name, conn_id);

    // HD ban check (VB6 13.3 parity)
    let paso_hd = state.users.get(&conn_id).map(|u| u.paso_hd).unwrap_or(false);
    if !paso_hd {
        state.send_bytes(conn_id, &binary_packets::write_error_msg("Tu PC se encuentra bajo Tolerancia 0."));
        close_connection(state, conn_id).await;
        return;
    }

    // Verify user is logged into an account
    let (account_name, account_id) = state.users.get(&conn_id)
        .map(|u| (u.account_name.clone(), u.account_id))
        .unwrap_or_default();
    if account_name.is_empty() || account_id == 0 {
        state.send_bytes(conn_id, &binary_packets::write_error_msg("Debes iniciar sesion primero."));
        close_connection(state, conn_id).await;
        return;
    }

    if !charfile::character_exists(&state.pool, &char_name).await {
        state.send_bytes(conn_id, &binary_packets::write_error_msg("El personaje no existe."));
        close_connection(state, conn_id).await;
        return;
    }

    // Verify character belongs to the logged-in account (prevent cross-account login)
    let char_account_id = charfile::load_charfile(&state.pool, &char_name).await
        .map(|c| c.account_id).unwrap_or(0);
    if char_account_id != account_id {
        state.send_bytes(conn_id, &binary_packets::write_error_msg("Este personaje no pertenece a tu cuenta."));
        close_connection(state, conn_id).await;
        return;
    }

    if is_char_banned(&state.pool, &char_name).await {
        state.send_bytes(conn_id, &binary_packets::write_error_msg(
            "Se te ha prohibido la entrada a Argentum Nextgen debido a tu mal comportamiento."
        ));
        close_connection(state, conn_id).await;
        return;
    }

    connect_user(state, conn_id, char_name, account, codex).await;
}

/// ConnectUser — Full character login (called after CharacterSelect or CharacterLogin validation).
pub(crate) async fn connect_user(
    state: &mut GameState,
    conn_id: ConnectionId,
    char_name: &str,
    account: &str,
    codex: &str,
) {
    // Check max users
    if state.num_users >= state.config.max_users {
        state.send_bytes(conn_id, &binary_packets::write_error_msg(
            "El servidor ha alcanzado el maximo de usuarios soportado. Intente mas tarde."
        ));
        close_connection(state, conn_id).await;
        return;
    }

    // Multi-login check
    if !state.config.allow_multi_logins {
        let user_ip = state.users.get(&conn_id).map(|u| u.ip.clone()).unwrap_or_default();
        if is_same_ip_online(state, conn_id, &user_ip) {
            state.send_bytes(conn_id, &binary_packets::write_finish_ok());
            state.send_bytes(conn_id, &binary_packets::write_error_show(
                "No es posible usar mas de un personaje al mismo tiempo."
            ));
            close_connection(state, conn_id).await;
            return;
        }
    }

    // Load character data
    let char_data = match charfile::load_charfile(&state.pool, char_name).await {
        Ok(data) => data,
        Err(e) => {
            warn!("[AUTH] Failed to load character '{}': {}", char_name, e);
            state.send_bytes(conn_id, &binary_packets::write_error_msg("Error en el personaje."));
            close_connection(state, conn_id).await;
            return;
        }
    };

    // Verify password (CodeX): if character has a password, client must provide it.
    // Guard on char_data.password (not codex) to prevent bypass via empty string.
    if !char_data.password.is_empty() && char_data.password.to_uppercase() != codex.to_uppercase() {
        state.send_bytes(conn_id, &binary_packets::write_finish_ok());
        state.send_bytes(conn_id, &binary_packets::write_error_show("Password incorrecto."));
        close_connection(state, conn_id).await;
        return;
    }

    // Check if already logged in — force-disconnect stale session if same account.
    // This handles the race condition where the server hasn't processed the old
    // Disconnected event yet when the client reconnects.
    if state.is_name_online(char_name) {
        // Find the old connection with the same char name
        let old_conn = state.users.iter()
            .find(|(id, u)| **id != conn_id && u.logged && u.char_name.eq_ignore_ascii_case(char_name))
            .map(|(id, _)| *id);

        if let Some(old_id) = old_conn {
            info!("[AUTH] Force-disconnecting stale session #{} for '{}' (re-login from #{})", old_id, char_name, conn_id);
            // VB6: QDL to entire map, BP to 27×27 area to remove old character
            if let Some(old_user) = state.users.get(&old_id) {
                let ci = old_user.char_index.0;
                let map = old_user.pos_map;
                let qdl_pkt = binary_packets::write_remove_char_dialog(ci as i16);
                state.send_data_bytes(
                    SendTarget::ToMapButIndex { conn_id: old_id, map },
                    &qdl_pkt,
                );
                let bp_pkt = binary_packets::write_character_remove(ci as i16);
                state.send_data_bytes(
                    SendTarget::ToMapButIndex { conn_id: old_id, map },
                    &bp_pkt,
                );
            }
            // remove_connection cleans up users, writers, online_names, world grid
            state.remove_connection(old_id);
        } else {
            // Name is "online" but no matching connection — stale entry, clean it up
            info!("[AUTH] Cleaning stale online_names entry for '{}'", char_name);
            state.online_names.remove(&char_name.to_uppercase());
        }
    }

    // Check same-account multi-character
    let account_chars = accounts::load_account(&state.pool, account).await
        .map(|a| a.characters)
        .unwrap_or_default();
    if let Some(other) = state.is_account_char_online(&account_chars, char_name) {
        state.send_bytes(conn_id, &binary_packets::write_error_show(
            &format!("Perdon, un usuario de la misma cuenta esta conectado ({}), intente de nuevo en 5 minutos.", other)
        ));
        close_connection(state, conn_id).await;
        return;
    }

    // Determine position — fallback to start pos if map doesn't exist
    let (map, x, y) = {
        let saved_map = if char_data.map > 0 { char_data.map } else { state.config.start_map };
        let saved_x = if char_data.x > 0 { char_data.x } else { state.config.start_x };
        let saved_y = if char_data.y > 0 { char_data.y } else { state.config.start_y };
        if state.world.grid(saved_map).is_some() {
            (saved_map, saved_x, saved_y)
        } else {
            tracing::warn!("Map {} does not exist, sending {} to start position", saved_map, char_name);
            (state.config.start_map, state.config.start_x, state.config.start_y)
        }
    };

    // Allocate a char index for the client rendering
    let char_index = state.world.alloc_char_index();

    // Character is valid — mark as logged in and set in-game state
    if let Some(user) = state.users.get_mut(&conn_id) {
        user.char_name = char_name.to_string();
        user.logged = true;
        user.pos_map = map;
        user.pos_x = x;
        user.pos_y = y;
        user.heading = if char_data.heading > 0 { char_data.heading } else { world::HEADING_SOUTH };
        user.char_index = char_index;
        user.dead = char_data.dead;
        user.body = char_data.body;
        user.head = char_data.head;
        user.old_head = char_data.head; // VB6 OrigChar.Head — for boat/invis restore
        user.navigating = char_data.navigating;
        user.barco_slot = char_data.barco_slot as usize;
        user.montado = char_data.montado;
        user.levitando = char_data.levitando;
        user.montado_body = char_data.montado_body;

        // Safety: if charfile has invalid body (0=invisible, 8=ghost) but dead=false,
        // restore naked body. body=0 can happen if a GM disconnects while invisible.
        // SKIP if navigating (boat has head=0) or montado (flying mount has head=0).
        let parsed_race = PlayerRace::from_str_or_default(&char_data.race);
        if !user.dead && !char_data.navigating && !char_data.montado && (user.body <= 0 || user.body == DEAD_BODY_NEUTRAL || user.head <= 0 || user.head == DEAD_HEAD_NEUTRAL) {
            user.body = naked_body(parsed_race, char_data.gender);
            // Head 500 is ghost head, head 0 is invisible — use a default for race/gender
            if user.head <= 0 || user.head == DEAD_HEAD_NEUTRAL {
                user.head = default_head_for_race(parsed_race, char_data.gender);
            }
        }

        // VB6: OrigChar.Head — immutable original head for revive.
        // If DB has dead head (500/501/511/512), use a default for the race/gender instead.
        let saved_head = char_data.head;
        user.orig_head = if saved_head == 500 || saved_head == 501 || saved_head == 511 || saved_head == 512 || saved_head <= 0 {
            default_head_for_race(parsed_race, char_data.gender)
        } else {
            saved_head
        };

        // Animation IDs: resolve from equipped items (VB6 TCP.bas lines 1511-1513)
        // Default 2 = "empty" animation (NingunArma/NingunEscudo/NingunCasco)
        user.weapon_anim = 2;
        user.shield_anim = 2;
        user.casco_anim = 2;
        // VB6: TCP.bas lines 1565-1598 — check server.ini role sections in priority order.
        // If the character name is listed in any role section, override DB privileges.
        let role_priv = state.role_overrides.get(&char_data.name.to_lowercase()).copied();
        let effective_priv = role_priv.unwrap_or(char_data.privileges);
        user.privileges = effective_priv;
        user.saved_privileges = effective_priv;
        user.gender = char_data.gender;
        user.poisoned = char_data.poisoned;
        user.paralyzed = char_data.paralyzed;
        // VB6: FileIO.bas sets Counters.Paralisis = IntervaloParalizado on load
        if user.paralyzed {
            user.counter_paralisis = state.intervals.paralizado;
        }
        user.criminal = char_data.criminal;
        user.hidden = char_data.hidden;
        user.navigating = char_data.navigating;
        user.guild_index = char_data.guild_index;
        // Cache guild name for CC packet clan tag
        if char_data.guild_index > 0 {
            if let Some(g) = crate::db::guilds::load_guild(&state.pool, char_data.guild_index).await {
                user.guild_name = g.name;
            }
        }
        user.armada_real = char_data.armada_real;
        user.fuerzas_caos = char_data.fuerzas_caos;
        user.criminales_matados = char_data.criminales_matados;
        user.ciudadanos_matados = char_data.ciudadanos_matados;
        user.recompensas_real = char_data.recompensas_real;
        user.recompensas_caos = char_data.recompensas_caos;
        user.reenlistadas = char_data.reenlistadas;
        user.recibio_armadura_real = char_data.recibio_armadura_real;
        user.recibio_armadura_caos = char_data.recibio_armadura_caos;
        user.recibio_exp_real = char_data.recibio_exp_real;
        user.recibio_exp_caos = char_data.recibio_exp_caos;
        user.nivel_ingreso = char_data.nivel_ingreso;
        user.fecha_ingreso = char_data.fecha_ingreso.clone();
        user.matados_ingreso = char_data.matados_ingreso;
        user.next_recompensa = char_data.next_recompensa;
        user.hogar = "Ullathorpe".to_string();

        // Stats
        user.class = PlayerClass::from_str_or_default(&char_data.class);
        user.race = PlayerRace::from_str_or_default(&char_data.race);
        user.level = char_data.level;
        user.exp = char_data.exp;
        user.max_hp = char_data.max_hp;
        user.min_hp = char_data.min_hp;
        user.max_mana = char_data.max_mana;
        user.min_mana = char_data.min_mana;
        user.max_sta = char_data.max_sta;
        user.min_sta = char_data.min_sta;
        user.max_hit = char_data.max_hit;
        user.min_hit = char_data.min_hit;
        user.gold = char_data.gold;
        user.bank_gold = char_data.bank_gold;
        user.skill_pts_libres = char_data.skill_pts_libres;
        // hogar loaded above (default Ullathorpe)
        user.max_agua = char_data.max_agua;
        user.min_agua = char_data.min_agua;
        user.max_ham = char_data.max_ham;
        user.min_ham = char_data.min_ham;
        user.attributes = char_data.attributes;
        user.skills = char_data.skills;
        user.exp_skills = char_data.exp_skills;
        // Initialize skill ELU values for progression
        super::skills::init_elu_skills(user);
        user.skill_pts_libres = char_data.skill_pts_libres;
        user.skills_asignados = char_data.skills_asignados;
        user.reputation = char_data.reputation;
        user.rep_asesino = char_data.rep_asesino;
        user.rep_bandido = char_data.rep_bandido;
        user.rep_burgues = char_data.rep_burgues;
        user.rep_ladrones = char_data.rep_ladrones;
        user.rep_noble = char_data.rep_noble;
        user.rep_plebe = char_data.rep_plebe;

        // Inventory
        for (i, &(obj_idx, amount, equipped)) in char_data.inventory.iter().enumerate() {
            if i < MAX_INVENTORY_SLOTS {
                user.inventory[i].obj_index = obj_idx;
        user.inventory[i].amount = amount;
        user.inventory[i].equipped = equipped;
            }
        }
        // Reconstruct ring equip slot from inventory (equipped Tool/Ring item)
        let mut ring_slot = 0usize;
        for (i, inv) in user.inventory.iter().enumerate() {
            if inv.equipped && inv.obj_index > 0 {
                if let Some(obj) = state.game_data.objects.get((inv.obj_index - 1) as usize) {
                    if obj.obj_type == crate::data::objects::ObjType::Tool {
                        ring_slot = i + 1;
                        break;
                    }
                }
            }
        }
        user.equip.weapon = char_data.weapon_eqp_slot;
        user.equip.armor = char_data.armour_eqp_slot;
        user.equip.shield = char_data.shield_eqp_slot;
        user.equip.helmet = char_data.helmet_eqp_slot;
        user.equip.municion = char_data.municion_eqp_slot;
        user.equip.ring = ring_slot;

        // Restore backpack (VB6: MochilaEqpSlot → CurrentInventorySlots on login)
        user.backpack_slot = 0;
        user.current_inventory_slots = MAX_NORMAL_INVENTORY_SLOTS;
        for i in 0..MAX_INVENTORY_SLOTS {
            if user.inventory[i].equipped && user.inventory[i].obj_index > 0 {
                let oi = user.inventory[i].obj_index as usize;
                if let Some(obj) = state.game_data.objects.get(oi.wrapping_sub(1)) {
                    if obj.obj_type == ObjType::Backpack {
                        user.backpack_slot = i + 1; // 1-indexed
                        let new_slots = MAX_NORMAL_INVENTORY_SLOTS + (obj.mochila_type as usize) * 5;
                        user.current_inventory_slots = new_slots.min(MAX_INVENTORY_SLOTS);
                        break;
                    }
                }
            }
        }

        // Bank
        for (i, &(obj_idx, amount)) in char_data.bank.iter().enumerate() {
            if i < user.bank.len() {
                user.bank[i].obj_index = obj_idx;
        user.bank[i].amount = amount;
            }
        }

        // Spells
        user.spells = char_data.spells;

        // Kill counters and misc
        user.usuarios_matados = char_data.usuarios_matados;
        user.npcs_muertos = char_data.npcs_muertos;
        user.counter_pena = char_data.counter_pena;
        user.last_map = char_data.last_map;
        user.uptime = char_data.uptime;
        user.pareja = char_data.pareja.clone();
        user.desc = char_data.description.clone();

        // Resolve equipment animation IDs from equipped items (VB6: TCP.bas lines 1511-1530)
        // Pre-extract obj indices to avoid borrow conflict with state.get_object()
        let weapon_obj_idx = if user.equip.weapon > 0 {
            let s = (user.equip.weapon - 1) as usize;
            if s < MAX_INVENTORY_SLOTS { user.inventory[s].obj_index } else { 0 }
        } else { 0 };
        let shield_obj_idx = if user.equip.shield > 0 {
            let s = (user.equip.shield - 1) as usize;
            if s < MAX_INVENTORY_SLOTS { user.inventory[s].obj_index } else { 0 }
        } else { 0 };
        let helmet_obj_idx = if user.equip.helmet > 0 {
            let s = (user.equip.helmet - 1) as usize;
            if s < MAX_INVENTORY_SLOTS { user.inventory[s].obj_index } else { 0 }
        } else { 0 };
        // Look up animations directly from game_data.objects (avoids borrow conflict with state.users)
        if weapon_obj_idx >= 1 {
            if let Some(obj) = state.game_data.objects.get((weapon_obj_idx - 1) as usize) {
                user.weapon_anim = obj.weapon_anim;
            }
        }
        if shield_obj_idx >= 1 {
            if let Some(obj) = state.game_data.objects.get((shield_obj_idx - 1) as usize) {
                tracing::info!("[LOGIN-EQUIP] Shield obj={} '{}' type={:?} shield_anim={}", shield_obj_idx, obj.name, obj.obj_type, obj.shield_anim);
                user.shield_anim = obj.shield_anim;
            }
        } else {
            tracing::info!("[LOGIN-EQUIP] No shield equipped (equip.shield={}, shield_obj_idx={})", user.equip.shield, shield_obj_idx);
        }
        if helmet_obj_idx >= 1 {
            if let Some(obj) = state.game_data.objects.get((helmet_obj_idx - 1) as usize) {
                tracing::info!("[LOGIN-EQUIP] Helmet obj={} '{}' type={:?} casco_anim={}", helmet_obj_idx, obj.name, obj.obj_type, obj.casco_anim);
                user.casco_anim = obj.casco_anim;
            }
        }

        // VB6: Initialize auras from all equipped items on login (TCP.bas lines 1703-1718)
        // Skip if mounted — weapons/shield/helmet are hidden, no auras should show.
        if !user.montado {
            for slot_idx in 0..MAX_INVENTORY_SLOTS {
                if !user.inventory[slot_idx].equipped { continue; }
                let oi = user.inventory[slot_idx].obj_index;
                if oi < 1 { continue; }
                if let Some(obj) = state.game_data.objects.get((oi - 1) as usize) {
                    if obj.crea_aura > 0 {
                        match obj.obj_type {
                            ObjType::Armor => user.aura_a = obj.crea_aura,
                            ObjType::Weapon => user.aura_w = obj.crea_aura,
                            ObjType::Shield => user.aura_e = obj.crea_aura,
                            ObjType::Helmet => user.aura_c = obj.crea_aura,
                            ObjType::Tool => user.aura_r = obj.crea_aura,
                            _ => {}
                        }
                    }
                }
            }
        }
    }
    state.online_names.insert(char_name.to_uppercase(), conn_id);
    state.num_users += 1;
    if state.num_users > state.record_users {
        state.record_users = state.num_users;
    }

    // Place on world grid
    state.world.place_user(map, x, y, conn_id);

    // Grant warp immunity on login — suppresses area sounds (potions, food, NPC hits)
    // that other players/NPCs are generating nearby. Without this, freshly logged users
    // hear phantom sounds from ongoing activity in the area.
    if let Some(user) = state.users.get_mut(&conn_id) {
        user.warp_immunity_ticks = 25; // ~1 second at 40ms/tick
    }

    // Respawn saved pets near the player
    if char_data.pet_count > 0 && !char_data.pet_types.is_empty() {
        for (slot, &npc_type) in char_data.pet_types.iter().enumerate() {
            if npc_type <= 0 || slot >= 3 { break; }
            if let Some(npc_idx) = state.spawn_npc(npc_type as usize, map, x, y) {
                // Link pet to owner
                if let Some(npc) = state.get_npc_mut(npc_idx) {
                    npc.maestro_user = Some(conn_id);
                    npc.hostile = false;
                    npc.target = None;
                }
                if let Some(user) = state.users.get_mut(&conn_id) {
                    user.mascotas_index[slot] = npc_idx;
                    user.mascotas_type[slot] = npc_type;
                    let new_nro = user.nro_mascotas + 1;
                    user.nro_mascotas = new_nro;
                    // Restore elemental flags
                    match npc_type {
                        93 => user.ele_de_fuego = true,
                        92 => user.ele_de_agua = true,
                        91 => user.ele_de_tierra = true,
                        _ => {}
                    }
                }
                tracing::info!("[LOGIN] Spawned saved pet NPC {} (slot {}) for '{}'", npc_type, slot, char_name);
            }
        }
    }



    // Set Logged=1 in charfile
    let _ = charfile::set_logged_flag(&state.pool, char_name, true).await;

    // Get map info from loaded data
    let map_idx = map as usize;
    let (r, g, b, music, map_name) = if let Some(Some(game_map)) = state.game_data.maps.get(map_idx) {
        (
            game_map.info.r,
            game_map.info.g,
            game_map.info.b,
            game_map.info.music,
            game_map.info.name.clone(),
        )
    } else {
        (200, 200, 200, 0, format!("Mapa {}", map))
    };

    // =========================================================
    // COMPLETE LOGIN PACKET SEQUENCE
    // Must match VB6 ConnectUser() in TCP.bas lines 1402-1868
    // =========================================================

    // --- PHASE 1: Map setup (VB6 lines 1552-1555) ---
    state.send_bytes(conn_id, &binary_packets::write_change_map(map as i16, 0, r as u8, g as u8, b as u8));
    state.send_bytes(conn_id, &binary_packets::write_pos_update(x as i16, y as i16));
    state.send_bytes(conn_id, &binary_packets::write_play_midi(music as u8));
    state.send_bytes(conn_id, &binary_packets::write_map_name(&map_name));

    // --- PHASE 2: Privilege level (VB6 lines 1558-1596) ---
    // Use effective privileges (role_overrides may have elevated above char_data.privileges)
    let eff_priv = state.users.get(&conn_id).map(|u| u.privileges).unwrap_or(char_data.privileges);
    state.send_bytes(conn_id, &binary_packets::write_privilege_level(eff_priv as u8));

    // --- PHASE 3: Hunger/Thirst (VB6 line 1608) ---
    state.send_bytes(conn_id, &binary_packets::write_update_hunger_thirst(
        char_data.max_agua as u8, char_data.min_agua as u8,
        char_data.max_ham as u8, char_data.min_ham as u8,
    ));

    // --- PHASE 4: Broadcast status to area (VB6 line 1609) ---
    let status_mith = if char_data.criminal { 2u8 } else { 1u8 };
    let px_pkt = binary_packets::write_update_tag_and_status(char_index.0 as i16, status_mith, char_name);
    state.send_data_bytes(
        SendTarget::ToAreaButIndex { conn_id, map, x, y },
        &px_pkt,
    );

    // --- PHASE 5: Reputation (VB6 line 1666) ---
    state.send_bytes(conn_id, &binary_packets::write_reputation(char_data.reputation as i32));

    // --- PHASE 7: LOGGED — client switches to game mode (VB6 line 1692) ---
    // CRITICAL: Must come BEFORE stats, inventory, spells, area visibility
    // Generate anti-cheat coord cipher seed with full 32-bit entropy.
    // Uses /dev/urandom (not rand_simple_u32 which only has ~10 bits).
    let coord_seed = {
        use std::io::Read;
        let mut buf = [0u8; 4];
        if let Ok(mut f) = std::fs::File::open("/dev/urandom") {
            let _ = f.read_exact(&mut buf);
        }
        let s = u32::from_le_bytes(buf);
        if s == 0 { 1u32 } else { s } // XorShift requires non-zero seed
    };
    if let Some(user) = state.users.get_mut(&conn_id) {
        user.coord_cipher = Some(crate::protocol::coord_cipher::CoordCipher::new(coord_seed));
    }
    state.send_bytes(conn_id, &binary_packets::write_logged(0, coord_seed));

    // --- PHASE 8: Equipment hitbox stats (VB6 line 1701) ---
    let anm = build_anm_packet(state, conn_id);
    // build_anm_packet returns "ANM<csv>" — strip the 3-char "ANM" prefix for the binary builder
    state.send_bytes(conn_id, &binary_packets::write_anim_data(&anm[3..]));

    // --- PHASE 9: Bulk stats [ES (VB6 line 1720) ---
    let exp_next = state.exp_for_level(char_data.level);
    state.send_bytes(conn_id, &binary_packets::write_update_user_stats(
        char_data.max_hp as i16, char_data.min_hp as i16,
        char_data.max_mana as i16, char_data.min_mana as i16,
        char_data.max_sta as i16, char_data.min_sta as i16,
        char_data.gold as i32,
        char_data.level as u8,
        exp_next as i32,
        char_data.exp as i32,
    ));

    // --- PHASE 10: Stop state (VB6 line 1730) ---
    state.send_bytes(conn_id, &binary_packets::write_stop_dancing(false));

    // --- PHASE 10b: PARADOK if paralyzed (VB6 line 1732) ---
    // PARADOK is a toggle — client starts with UserParalizado=False,
    // so we send one PARADOK to set it to True if the char is paralyzed.
    if char_data.paralyzed {
        let para_secs = (state.users.get(&conn_id).map(|u| u.counter_paralisis).unwrap_or(0) as f32 * 0.04) as i16;
        state.send_bytes(conn_id, &binary_packets::write_paralize_ok(para_secs));
    }

    // --- PHASE 10c: NAVEG if navigating (VB6 TCP.bas lines 1515-1521, 1654) ---
    // VB6: On login, if Navegando=1, restore boat appearance from BarcoSlot object.
    // NAVEG is a toggle — client starts with UserNavegando=False,
    // so we send one NAVEG to set it to True.
    {
        let nav_info = state.users.get(&conn_id).map(|u| (u.navigating, u.barco_slot));
        if let Some((true, barco_slot)) = nav_info {
            // VB6: BarcoObjIndex = Invent.Object(BarcoSlot).ObjIndex
            // Then Body = ObjData(BarcoObjIndex).Ropaje
            let boat_ropaje = if barco_slot >= 1 {
                state.users.get(&conn_id)
                    .and_then(|u| u.inventory.get(barco_slot - 1))
                    .map(|s| s.obj_index)
                    .and_then(|oi| if oi > 0 { state.get_object(oi) } else { None })
                    .map(|o| o.num_ropaje)
                    .unwrap_or(0)
            } else { 0 };

            if let Some(user) = state.users.get_mut(&conn_id) {
                user.head = 0;
                user.weapon_anim = NINGUN_ARMA;
                user.shield_anim = NINGUN_ESCUDO;
                user.casco_anim = NINGUN_CASCO;
                if boat_ropaje > 0 {
                    user.body = boat_ropaje;
                }
            }
            state.send_bytes(conn_id, &binary_packets::write_navigate_toggle());
        }
    }

    // --- PHASE 10d: USM/MVOL if mounted ---
    // Send mount state to the user's client so it knows the character is mounted.
    // Also broadcast to area so other players see the mount.
    {
        let mount_info = state.users.get(&conn_id).map(|u| (u.montado, u.levitando, u.char_index.0));
        if let Some((true, levitando, char_idx)) = mount_info {
            let (m, x, y) = state.users.get(&conn_id)
                .map(|u| (u.pos_map, u.pos_x, u.pos_y))
                .unwrap_or((0, 0, 0));
            state.send_data_bytes(SendTarget::ToArea { map: m, x, y }, &binary_packets::write_user_mount(char_idx as i16, true));
            if levitando {
                state.send_data_bytes(SendTarget::ToArea { map: m, x, y }, &binary_packets::write_levitate(char_idx as i16, true));
            }
        }
    }

    // --- PHASE 11: WarpUserChar — position + area visibility (VB6 line 1736) ---
    // VB6 calls WarpUserChar which sends: BKW, CM, XM, N~, PU, CC, area visibility, BKW
    {
        let map_idx = map as usize;
        let (r, g, b, music, map_name) = if let Some(Some(game_map)) = state.game_data.maps.get(map_idx) {
            (
                game_map.info.r,
                game_map.info.g,
                game_map.info.b,
                game_map.info.music,
                game_map.info.name.clone(),
            )
        } else {
            (200, 200, 200, 0, format!("Mapa {}", map))
        };

        // BKW (fade to black)
        state.send_bytes(conn_id, &binary_packets::write_pause_toggle());
        // CM (change map — client loads the map file)
        state.send_bytes(conn_id, &binary_packets::write_change_map(map as i16, 0, r as u8, g as u8, b as u8));
        // XM (music)
        state.send_bytes(conn_id, &binary_packets::write_play_midi(music as u8));
        // N~ (map name display)
        state.send_bytes(conn_id, &binary_packets::write_map_name(&map_name));
        // IP (self char index — tells client which CC is "me")
        state.send_bytes(conn_id, &binary_packets::write_user_char_index(char_index.0 as i16));
        // Own CC packet (client renders self) + [CD (char data)
        if let Some(user) = state.users.get(&conn_id) {
            let own_cc = user.build_cc_binary();
            let own_cd = build_cd_binary(user);
            state.send_bytes(conn_id, &own_cc);
            state.send_bytes(conn_id, &own_cd);
        }
        // PU (position update — tells client where to center camera)
        state.send_bytes(conn_id, &binary_packets::write_pos_update(x as i16, y as i16));
        // Send area visibility (other players, NPCs, ground items)
        make_user_visible(state, conn_id).await;

        // Send initial zone info (so client knows zone name, safety, bounds from the start)
        check_zone_change(state, conn_id).await;

        // Door BQ/HO sync is handled by make_user_visible() → check_update_needed_user()

        // BKW again to toggle pausa back to False (VB6 WarpUserChar line 2404)
        // BKW toggles pausa — first one pauses, second one un-pauses.
        // Without this, client stays paused and CheckKeys never runs (no movement!)
        state.send_bytes(conn_id, &binary_packets::write_pause_toggle());

        // VB6: WarpUserChar(..., True) on login — send warp FX to area
        send_warp_fx(state, conn_id).await;
    }

    // --- PHASE 12: Console messages (VB6 lines 1738-1747) ---
    state.send_msg_id(conn_id, 705, "");
    state.send_msg_id(conn_id, 706, "0"); // 0 penalties
    state.send_msg_id(conn_id, 707, "");
    state.send_msg_id(conn_id, 709, char_name);
    state.send_msg_id(conn_id, 710, ""); // Messages activated

    // --- PHASE 12b: Online count (ON opcode → frmMain.ONLINES.Caption) ---
    // VB6 MostrarNumUsers: broadcast online count to ALL players (General.bas:628)
    state.send_data_bytes(SendTarget::ToAll, &binary_packets::write_online_count(state.num_users as i16));

    // --- PHASE 13: Scroll timers (VB6 lines 1781-1783) ---
    for i in 1u8..=4 {
        state.send_bytes(conn_id, &binary_packets::write_timer_info(i, 0, 0));
    }

    // --- PHASE 14: Inventory (VB6 lines 1785-1786) ---
    state.send_bytes(conn_id, &binary_packets::write_inv_init());
    send_full_inventory(state, conn_id).await;

    // --- PHASE 15: Spells (VB6 line 1787) ---
    send_full_spells(state, conn_id).await;

    // --- PHASE 16: Rain state (VB6: send rain toggle if currently raining) ---
    if state.raining {
        state.send_bytes(conn_id, &binary_packets::write_rain_toggle());
    }

    info!(
        "[AUTH] '{}' logged in (CharIdx={}, Map {}, {},{}) — {} users online",
        char_name, char_index.0, map, x, y, state.num_users
    );
}

// =====================================================================
// Character creation/deletion handlers
// =====================================================================

/// CreateCharacter — Create new character.
pub(super) async fn handle_create_character(
    state: &mut GameState,
    conn_id: ConnectionId,
    char_name: &str,
    race: &str,
    gender: i32,
    class: &str,
    hogar: i32,
    account: &str,
    head: i32,
) {
    let paso_hd = state.users.get(&conn_id).map(|u| u.paso_hd).unwrap_or(false);
    if !paso_hd {
        state.send_bytes(conn_id, &binary_packets::write_error_msg("Tu PC se encuentra bajo Tolerancia 0."));
        close_connection(state, conn_id).await;
        return;
    }

    if !state.config.can_create_characters {
        state.send_bytes(conn_id, &binary_packets::write_error_msg("La creacion de personajes en este servidor se ha deshabilitado."));
        close_connection(state, conn_id).await;
        return;
    }

    if state.config.server_only_gms {
        state.send_bytes(conn_id, &binary_packets::write_error_msg("Servidor restringido a administradores. La creacion de personajes se encuentra deshabilitada."));
        close_connection(state, conn_id).await;
        return;
    }

    info!("[AUTH] New character request: '{}' race='{}' class='{}' from #{}", char_name, race, class, conn_id);

    if !is_valid_name(&char_name) {
        state.send_bytes(conn_id, &binary_packets::write_error_msg("Nombre invalido."));
        close_connection(state, conn_id).await;
        return;
    }

    if char_name.len() < 3 {
        state.send_bytes(conn_id, &binary_packets::write_error_msg("El nombre del personaje debe tener al menos 3 caracteres."));
        close_connection(state, conn_id).await;
        return;
    }

    let attributes = state.users.get(&conn_id)
        .map(|u| u.dice_attributes)
        .unwrap_or([18; 5]);

    let account_id = state.users.get(&conn_id)
        .map(|u| u.account_id)
        .unwrap_or(0);

    if account_id == 0 {
        state.send_bytes(conn_id, &binary_packets::write_error_msg("Debes iniciar sesion primero."));
        close_connection(state, conn_id).await;
        return;
    }

    // VB6 13.3: max 5 characters per account
    let account_name_for_check = state.users.get(&conn_id)
        .map(|u| u.account_name.clone())
        .unwrap_or_default();
    if !account_name_for_check.is_empty() {
        let num_pjs = accounts::load_account(&state.pool, &account_name_for_check).await
            .map(|a| a.num_pjs)
            .unwrap_or(5);
        if num_pjs >= 5 {
            state.send_bytes(conn_id, &binary_packets::write_error_msg("Ya tienes el maximo de 5 personajes."));
            return;
        }
    }

    match charfile::create_charfile(
        &state.pool,
        account_id,
        &char_name,
        &race,
        gender,
        &class,
        hogar,
        head,
        attributes,
        state.config.start_map,
        state.config.start_x,
        state.config.start_y,
        &state.game_data.balance,
    ).await {
        Ok(_char_id) => {
            info!("[AUTH] Character '{}' created successfully — auto-logging in", char_name);
            // Auto-login after creation (VB6 behavior: enter game directly)
            connect_user(state, conn_id, char_name, account, "").await;
        }
        Err(e) => {
            state.send_bytes(conn_id, &binary_packets::write_error_msg(&e.to_string()));
            close_connection(state, conn_id).await;
        }
    }
}

/// RollDice — Roll dice for character attributes.
/// VB6 parity (Modulo_UsUaRiOs.bas TirarDados):
///   STR = max(15, 13 + rand(0,3) + rand(0,2))   → 15-18
///   AGI = max(15, 12 + rand(0,3) + rand(0,3))   → 15-18
///   INT = max(16, 13 + rand(0,3) + rand(0,2))   → 16-18
///   CHA = max(15, 12 + rand(0,3) + rand(0,3))   → 15-18
///   CON = 16 + rand(0,1) + rand(0,1)            → 16-18
pub(super) async fn handle_roll_dice(state: &mut GameState, conn_id: ConnectionId) {
    info!("[AUTH] Dice roll request from #{}", conn_id);

    let str_val = (13 + rand_range(0, 3) + rand_range(0, 2)).max(15);
    let agi_val = (12 + rand_range(0, 3) + rand_range(0, 3)).max(15);
    let int_val = (13 + rand_range(0, 3) + rand_range(0, 2)).max(16);
    let cha_val = (12 + rand_range(0, 3) + rand_range(0, 3)).max(15);
    let con_val = 16 + rand_range(0, 1) + rand_range(0, 1);

    let attrs = [str_val, agi_val, int_val, cha_val, con_val];

    if let Some(user) = state.users.get_mut(&conn_id) {
        user.dice_attributes = attrs;
    }

    state.send_bytes(conn_id, &binary_packets::write_dice_roll(
        attrs[0] as u8, attrs[1] as u8, attrs[2] as u8, attrs[3] as u8, attrs[4] as u8,
    ));
}

/// DeleteCharacter — Delete character.
pub(super) async fn handle_delete_character(state: &mut GameState, conn_id: ConnectionId, char_name: &str, _account_name: &str, password: &str) {
    info!("[AUTH] Delete character request: '{}' from #{}", char_name, conn_id);

    // Rate limit: 5-second cooldown between delete attempts
    let now = std::time::Instant::now();
    if let Some(user) = state.users.get(&conn_id) {
        if let Some(last) = user.last_delete_attempt {
            if now.duration_since(last).as_secs() < 5 {
                state.send_bytes(conn_id, &binary_packets::write_error_msg("Debes esperar antes de intentar borrar otro personaje."));
                return;
            }
        }
    }
    if let Some(user) = state.users.get_mut(&conn_id) {
        user.last_delete_attempt = Some(now);
    }

    // Cannot delete a character that is currently online
    if state.is_name_online(&char_name) {
        state.send_bytes(conn_id, &binary_packets::write_error_msg("No puedes borrar un personaje que esta conectado."));
        return;
    }

    if !charfile::character_exists(&state.pool, &char_name).await {
        state.send_bytes(conn_id, &binary_packets::write_error_msg("El personaje no existe."));
        close_connection(state, conn_id).await;
        return;
    }

    let char_data = match charfile::load_charfile(&state.pool, &char_name).await {
        Ok(data) => data,
        Err(_) => {
            state.send_bytes(conn_id, &binary_packets::write_error_msg("Error al leer el personaje."));
            close_connection(state, conn_id).await;
            return;
        }
    };

    // Verify password (security_code from account)
    if char_data.password.to_uppercase() != password.to_uppercase() {
        state.send_bytes(conn_id, &binary_packets::write_error_msg("Password incorrecto."));
        state.send_bytes(conn_id, &binary_packets::write_finish_ok());
        close_connection(state, conn_id).await;
        return;
    }

    // Block deletion of GMs/privileged characters (VB6 checks privilege level)
    if char_data.privileges > 0 {
        state.send_bytes(conn_id, &binary_packets::write_error_show("No podes borrar gms."));
        return;
    }

    if char_data.level >= 50 {
        state.send_bytes(conn_id, &binary_packets::write_error_msg("No podes borrar usuarios nivel 50 o superior."));
        return;
    }

    if char_data.guild_index > 0 {
        state.send_bytes(conn_id, &binary_packets::write_error_msg("No podes borrar usuarios que esten dentro de un clan. Primero abandona el clan."));
        return;
    }

    if let Err(e) = charfile::delete_charfile(&state.pool, &char_name).await {
        warn!("[AUTH] Failed to delete character: {}", e);
    }

    info!("[AUTH] Character '{}' deleted", char_name);
    state.send_bytes(conn_id, &binary_packets::write_finish_ok());
    state.send_bytes(conn_id, &binary_packets::write_error_show("Personaje Borrado con exito."));
    close_connection(state, conn_id).await;
}

/// ChangePassword — Change account password.
pub(super) async fn handle_change_password(state: &mut GameState, conn_id: ConnectionId, account_name: &str, old_password: &str, new_password: &str, confirm_password: &str) {
    info!("[AUTH] Password change request for '{}' from #{}", account_name, conn_id);

    if new_password == old_password {
        state.send_bytes(conn_id, &binary_packets::write_error_show("No puedes volver a utilizar la misma contrasena."));
        return;
    }

    if new_password.len() < 3 {
        state.send_bytes(conn_id, &binary_packets::write_error_show("La contrasena debe tener un minimo de 3 caracteres."));
        return;
    }

    if new_password != confirm_password {
        state.send_bytes(conn_id, &binary_packets::write_error_show("Las contrasenas no coinciden."));
        return;
    }

    let account = match accounts::load_account(&state.pool, &account_name).await {
        Ok(acc) => acc,
        Err(_) => {
            state.send_bytes(conn_id, &binary_packets::write_error_show("La cuenta no existe."));
            return;
        }
    };

    if !password::verify_password(&old_password, &account.password_hash) {
        state.send_bytes(conn_id, &binary_packets::write_error_show("La Password actual que nos proporciono, no coincide con la del registro."));
        return;
    }

    let new_hash = match password::hash_password(&new_password) {
        Ok(h) => h,
        Err(_) => {
            state.send_bytes(conn_id, &binary_packets::write_error_show("Error interno."));
            return;
        }
    };

    match accounts::update_password(&state.pool, &account_name, &new_hash).await {
        Ok(()) => {
            info!("[AUTH] Password changed for account '{}'", account_name);
            state.send_bytes(conn_id, &binary_packets::write_error_show("La password de su cuenta fue cambiada con exito. Ahora para logear debera de utilizar la nueva."));
        }
        Err(e) => {
            warn!("[AUTH] Failed to update password: {}", e);
            state.send_bytes(conn_id, &binary_packets::write_error_show("Error al cambiar la password."));
        }
    }
}

/// AccountRecovery — Account recovery via PIN.
pub(super) async fn handle_account_recovery(state: &mut GameState, conn_id: ConnectionId, account_name: &str, pin: &str) {
    info!("[AUTH] Account recovery request for '{}' from #{}", account_name, conn_id);

    let account = match accounts::load_account(&state.pool, &account_name).await {
        Ok(acc) => acc,
        Err(_) => {
            state.send_bytes(conn_id, &binary_packets::write_error_msg("La cuenta no existe."));
            close_connection(state, conn_id).await;
            return;
        }
    };

    if pin.to_uppercase() != account.pin.to_uppercase() {
        state.send_bytes(conn_id, &binary_packets::write_error_msg("El pin ingresado no es correcto."));
        close_connection(state, conn_id).await;
        return;
    }

    // Generate random 8-character alphanumeric password (excludes ambiguous chars: 0/O/1/l/I)
    let chars: Vec<char> = "ABCDEFGHJKLMNPQRSTUVWXYZabcdefghjkmnpqrstuvwxyz23456789".chars().collect();
    let new_pass: String = (0..8).map(|_| chars[rand_range(0, chars.len() as i32 - 1) as usize]).collect();

    if let Ok(hash) = password::hash_password(&new_pass) {
        let _ = accounts::update_password(&state.pool, &account_name, &hash).await;
    }

    state.send_bytes(conn_id, &binary_packets::write_error_show(
        &format!("Has recuperado la cuenta, utiliza la contrasena {} para poder logearte.", new_pass)
    ));

    info!("[AUTH] Account '{}' recovered, new password generated", account_name);
}
