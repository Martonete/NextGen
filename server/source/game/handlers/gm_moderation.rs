//! GM moderation commands: /BAN, /KICK, /CARCEL, /SILENCIAR, etc.

use tracing::{info, error};
use crate::net::ConnectionId;
use crate::game::types::{GameState, SendTarget, privilege_level};
use crate::protocol::{font_index, binary_packets};
use super::common::*;
use super::{warp_user, user_die};

/// /KICK name — Disconnect a player (requires SEMIDIOS+).
pub(super) async fn handle_slash_kick(state: &mut GameState, conn_id: ConnectionId, target: &str) {
    match state.users.get(&conn_id) {
        Some(u) if u.logged && u.privileges >= privilege_level::SEMIDIOS => {}
        _ => {
            state.send_console(conn_id, "No tenes permisos para usar este comando.", font_index::INFO).await;
            return;
        }
    }

    let target_id = match state.find_user_by_name(target) {
        Some(id) => id,
        None => {
            state.send_console(conn_id, &format!("Jugador '{}' no encontrado.", target), font_index::INFO).await;
            return;
        }
    };

    // Don't let someone kick themselves
    if target_id == conn_id {
        state.send_console(conn_id, "No podes kickearte a vos mismo.", font_index::INFO).await;
        return;
    }

    // Check target privilege — can't kick equal or higher
    let target_priv = state.users.get(&target_id).map(|u| u.privileges).unwrap_or(0);
    let my_priv = state.users.get(&conn_id).map(|u| u.privileges).unwrap_or(0);
    if target_priv >= my_priv {
        state.send_console(conn_id, "No podes kickear a alguien de igual o mayor rango.", font_index::INFO).await;
        return;
    }

    state.send_console(target_id, "Has sido desconectado por un GM.", font_index::INFO).await;
    close_connection(state, target_id).await;
    state.send_console(conn_id, &format!("Jugador '{}' desconectado.", target), font_index::INFO).await;
}

/// /BAN nick@reason — Ban character.
pub(super) async fn handle_slash_ban(state: &mut GameState, conn_id: ConnectionId, args: &str) {
    match state.users.get(&conn_id) {
        Some(u) if u.logged && u.privileges >= privilege_level::DIOS => {}
        _ => return,
    }

    let parts: Vec<&str> = args.splitn(2, '@').collect();
    let nick = parts[0].trim().replace(['\\', '/'], "");
    let reason = if parts.len() > 1 { parts[1].trim() } else { "" };

    if nick.is_empty() || reason.is_empty() {
        state.send_msg_id(conn_id, 758, "").await;
        return;
    }

    let admin_name = state.users.get(&conn_id).map(|u| u.char_name.clone()).unwrap_or_default();

    // Check if target is online
    let target_upper = nick.to_uppercase();
    let target_conn = state.users.values()
        .find(|u| u.logged && u.char_name.to_uppercase() == target_upper)
        .map(|u| u.conn_id);

    // Set ban flag in DB
    if !crate::db::charfile::character_exists(&state.pool, &nick).await {
        state.send_msg_id(conn_id, 440, "").await;
        return;
    }
    let _ = crate::db::charfile::set_char_banned(&state.pool, &nick, true).await;

    // Broadcast ban notification
    state.send_msg_id_to(SendTarget::ToAdmins, 760, &format!("{}@{}", admin_name, nick)).await;

    // If online, disconnect
    if let Some(tc) = target_conn {
        state.send_console(tc, &format!("Has sido baneado. Razon: {}", reason), font_index::FIGHT).await;
        if let Some(w) = state.writers.get_mut(&tc) {
            w.shutdown().await;
        }
    }

    state.send_console(conn_id, &format!("{} ha sido baneado. Razon: {}", nick, reason), font_index::INFO).await;
    info!("[GM] {} banned {} for: {}", admin_name, nick, reason);
}

/// /UNBAN nick — Unban character.
pub(super) async fn handle_slash_unban(state: &mut GameState, conn_id: ConnectionId, target: &str) {
    match state.users.get(&conn_id) {
        Some(u) if u.logged && u.privileges >= privilege_level::SUB_ADMINISTRADOR => {}
        _ => return,
    }

    let nick = target.replace(['\\', '/'], "");
    if !crate::db::charfile::character_exists(&state.pool, &nick).await {
        state.send_msg_id(conn_id, 189, &nick.to_string()).await;
        return;
    }

    let _ = crate::db::charfile::set_char_banned(&state.pool, &nick, false).await;

    let admin_name = state.users.get(&conn_id).map(|u| u.char_name.clone()).unwrap_or_default();
    state.send_msg_id_to(SendTarget::ToAdmins, 762, &format!("{}@{}", admin_name, nick)).await;
    state.send_console(conn_id, &format!("{} ha sido desbaneado.", nick), font_index::INFO).await;
    info!("[GM] {} unbanned {}", admin_name, nick);
}

/// /BANIP nick|ip — Ban IP address.
pub(super) async fn handle_slash_banip(state: &mut GameState, conn_id: ConnectionId, args: &str) {
    match state.users.get(&conn_id) {
        Some(u) if u.logged && u.privileges >= privilege_level::SEMIDIOS => {}
        _ => return,
    }

    let target = args.trim().replace(['\\', '/'], "");
    let admin_name = state.users.get(&conn_id).map(|u| u.char_name.clone()).unwrap_or_default();

    // Check if target is a nick (online user)
    let target_upper = target.to_uppercase();
    let found = state.users.values()
        .find(|u| u.logged && u.char_name.to_uppercase() == target_upper)
        .map(|u| (u.conn_id, u.ip.clone(), u.char_name.clone()));

    let ip_to_ban = if let Some((tc, ip, name)) = found {
        // Banning by nick — also kick the player
        state.send_msg_id_to(SendTarget::ToAdmins, 798, &format!("{}@{}", admin_name, name)).await;
        if let Some(w) = state.writers.get_mut(&tc) {
            w.shutdown().await;
        }
        ip
    } else {
        // Treat as direct IP
        target.clone()
    };

    // Add to ban list
    if state.bans.is_ip_banned(&ip_to_ban) {
        state.send_msg_id(conn_id, 797, "").await;
        return;
    }

    let _ = state.bans.ban_ip(&state.pool, &ip_to_ban).await;
    state.send_console(conn_id, &format!("IP {} baneada.", ip_to_ban), font_index::INFO).await;
    info!("[GM] {} banned IP {}", admin_name, ip_to_ban);
}

/// /UNBANIP ip — Unban IP address.
pub(super) async fn handle_slash_unbanip(state: &mut GameState, conn_id: ConnectionId, ip: &str) {
    match state.users.get(&conn_id) {
        Some(u) if u.logged && u.privileges >= privilege_level::SEMIDIOS => {}
        _ => return,
    }

    let ip = ip.trim();
    if state.bans.unban_ip(&state.pool, ip).await {
        state.send_msg_id(conn_id, 799, &ip.to_string()).await;
        let admin_name = state.users.get(&conn_id).map(|u| u.char_name.clone()).unwrap_or_default();
        info!("[GM] {} unbanned IP {}", admin_name, ip);
    } else {
        state.send_msg_id(conn_id, 800, "").await;
    }
}

/// /BANACC nick@reason — Ban account.
pub(super) async fn handle_slash_banacc(state: &mut GameState, conn_id: ConnectionId, args: &str) {
    match state.users.get(&conn_id) {
        Some(u) if u.logged && u.privileges >= privilege_level::DIOS => {}
        _ => return,
    }

    let parts: Vec<&str> = args.splitn(2, '@').collect();
    let nick = parts[0].trim().replace(['\\', '/'], "");
    let reason = if parts.len() > 1 { parts[1].trim() } else { "Sin razon" };

    if nick.is_empty() {
        state.send_msg_id(conn_id, 758, "").await;
        return;
    }

    let admin_name = state.users.get(&conn_id).map(|u| u.char_name.clone()).unwrap_or_default();

    // Find account name (from online user or charfile)
    let target_upper = nick.to_uppercase();
    let online = state.users.values()
        .find(|u| u.logged && u.char_name.to_uppercase() == target_upper)
        .map(|u| (u.conn_id, u.account_name.clone()));

    let account_name = if let Some((tc, acc)) = online {
        // Kick the player
        state.send_msg_id_to(SendTarget::ToAdmins, 752, &format!("{}@{}", admin_name, nick)).await;
        if let Some(w) = state.writers.get_mut(&tc) {
            w.shutdown().await;
        }
        acc
    } else {
        // Try to read from charfile
        nick.clone() // Fallback: use nick as account name
    };

    // Ban character in DB
    let _ = crate::db::charfile::set_char_banned(&state.pool, &nick, true).await;

    // Ban account in DB
    let _ = crate::db::accounts::set_account_banned(&state.pool, &account_name, true, "BANACC").await;

    state.send_msg_id_to(SendTarget::ToAdmins, 760, &format!("{}@{}", admin_name, nick)).await;
    state.send_msg_id_to(SendTarget::ToAdmins, 761, &format!("{}@{}", admin_name, account_name)).await;
    state.send_console(conn_id, &format!("{} y cuenta {} baneados.", nick, account_name), font_index::INFO).await;
    info!("[GM] {} banned account {} (char: {})", admin_name, account_name, nick);
}

/// /UNBANACC nick — Unban account.
pub(super) async fn handle_slash_unbanacc(state: &mut GameState, conn_id: ConnectionId, target: &str) {
    match state.users.get(&conn_id) {
        Some(u) if u.logged && u.privileges >= privilege_level::SUB_ADMINISTRADOR => {}
        _ => return,
    }

    let nick = target.replace(['\\', '/'], "");
    let admin_name = state.users.get(&conn_id).map(|u| u.char_name.clone()).unwrap_or_default();

    // Unban character in DB
    let _ = crate::db::charfile::set_char_banned(&state.pool, &nick, false).await;

    // Try unban account (use nick as account name fallback)
    let _ = crate::db::accounts::set_account_banned(&state.pool, &nick, false, "").await;

    state.send_msg_id_to(SendTarget::ToAdmins, 763, &format!("{}@{}", admin_name, nick)).await;
    state.send_console(conn_id, &format!("Cuenta de {} desbaneada.", nick), font_index::INFO).await;
    info!("[GM] {} unbanned account for {}", admin_name, nick);
}

/// /CARCEL nick@reason@minutes — Jail user.
pub(super) async fn handle_slash_carcel(state: &mut GameState, conn_id: ConnectionId, args: &str) {
    match state.users.get(&conn_id) {
        Some(u) if u.logged && u.privileges >= privilege_level::SEMIDIOS => {}
        _ => return,
    }

    let parts: Vec<&str> = args.splitn(3, '@').collect();
    if parts.len() < 3 {
        state.send_msg_id(conn_id, 745, "").await;
        return;
    }

    let nick = parts[0].trim().replace(['\\', '/'], "");
    let reason = parts[1].trim();
    let minutes: i32 = parts[2].trim().parse().unwrap_or(0);

    if minutes < 1 || minutes > 60 {
        state.send_msg_id(conn_id, 746, "").await;
        return;
    }

    let target_upper = nick.to_uppercase();
    let target_conn = state.users.values()
        .find(|u| u.logged && u.char_name.to_uppercase() == target_upper)
        .map(|u| (u.conn_id, u.privileges));

    match target_conn {
        Some((tc, priv_level)) => {
            // Can't jail admins
            if priv_level >= privilege_level::DIOS {
                state.send_msg_id(conn_id, 442, "").await;
                return;
            }

            // Set jail timer and warp to prison (map 78, 50, 50)
            if let Some(user) = state.users.get_mut(&tc) {
                user.jail_timer = minutes * 60; // Convert to seconds
            }

            // Send jail notification
            state.send_msg_id(tc, 659, &minutes.to_string()).await;

            // Warp to prison
            warp_user(state, tc, 78, 50, 50).await;

            let admin_name = state.users.get(&conn_id).map(|u| u.char_name.clone()).unwrap_or_default();
            state.send_console(conn_id, &format!("{} encarcelado por {} minutos.", nick, minutes), font_index::INFO).await;
            info!("[GM] {} jailed {} for {}m: {}", admin_name, nick, minutes, reason);
        }
        None => {
            state.send_msg_id(conn_id, 196, "").await;
        }
    }
}

/// /SILENCIAR nick@minutes — Mute/unmute user.
pub(super) async fn handle_slash_silenciar(state: &mut GameState, conn_id: ConnectionId, args: &str) {
    match state.users.get(&conn_id) {
        Some(u) if u.logged && u.privileges >= privilege_level::CONSEJERO => {}
        _ => return,
    }

    let parts: Vec<&str> = args.splitn(2, '@').collect();
    let nick = parts[0].trim().replace(['\\', '/'], "");
    let minutes: i32 = if parts.len() > 1 { parts[1].trim().parse().unwrap_or(5) } else { 5 };

    if minutes > 60 {
        state.send_msg_id(conn_id, 944, "").await;
        return;
    }

    let target_upper = nick.to_uppercase();
    let target_conn = state.users.values()
        .find(|u| u.logged && u.char_name.to_uppercase() == target_upper)
        .map(|u| (u.conn_id, u.silenced));

    match target_conn {
        Some((tc, is_silenced)) => {
            if is_silenced {
                // Unmute
                if let Some(user) = state.users.get_mut(&tc) {
                    user.silenced = false;
                    user.silence_timer = 0;
                }
                state.send_msg_id(conn_id, 738, "").await;
                state.send_msg_id(tc, 946, "").await;
            } else {
                // Mute
                if let Some(user) = state.users.get_mut(&tc) {
                    user.silenced = true;
                    user.silence_timer = minutes * 60;
                }
                state.send_msg_id(conn_id, 737, "").await;
                state.send_msg_id(tc, 943, &minutes.to_string()).await;
            }
            let admin_name = state.users.get(&conn_id).map(|u| u.char_name.clone()).unwrap_or_default();
            info!("[GM] {} {} {}", admin_name, if is_silenced { "unmuted" } else { "muted" }, nick);
        }
        None => {
            state.send_msg_id(conn_id, 196, "").await;
        }
    }
}

/// /ADVERTIR nick@reason — Warn user. 5 warnings = auto-ban.
pub(super) async fn handle_slash_advertir(state: &mut GameState, conn_id: ConnectionId, args: &str) {
    match state.users.get(&conn_id) {
        Some(u) if u.logged && u.privileges >= privilege_level::CONSEJERO => {}
        _ => return,
    }

    let parts: Vec<&str> = args.splitn(2, '@').collect();
    let nick = parts[0].trim().replace(['\\', '/'], "");
    let reason = if parts.len() > 1 { parts[1].trim() } else { "" };

    if nick.is_empty() || reason.is_empty() {
        state.send_msg_id(conn_id, 741, "").await;
        return;
    }

    let admin_name = state.users.get(&conn_id).map(|u| u.char_name.clone()).unwrap_or_default();

    let target_upper = nick.to_uppercase();
    let target_conn = state.users.values()
        .find(|u| u.logged && u.char_name.to_uppercase() == target_upper)
        .map(|u| (u.conn_id, u.privileges, u.warnings));

    match target_conn {
        Some((tc, priv_level, current_warnings)) => {
            // Can't warn higher privilege
            let my_priv = state.users.get(&conn_id).map(|u| u.privileges).unwrap_or(0);
            if priv_level >= my_priv && my_priv < privilege_level::ADMINISTRADOR {
                state.send_msg_id(conn_id, 751, "").await;
                return;
            }

            let new_warnings = current_warnings + 1;
            if let Some(user) = state.users.get_mut(&tc) {
                user.warnings = new_warnings;
            }

            // Broadcast
            state.send_msg_id_to(SendTarget::ToAdmins, 742, &format!("{}@{}", admin_name, nick)).await;
            state.send_msg_id(tc, 743, &format!("{}@{}@{}", admin_name, reason, new_warnings)).await;

            if new_warnings >= 5 {
                // Auto-ban in DB
                let _ = crate::db::charfile::set_char_banned(&state.pool, &nick, true).await;
                state.send_msg_id_to(SendTarget::ToAdmins, 744, &nick.to_string()).await;
                if let Some(w) = state.writers.get_mut(&tc) {
                    w.shutdown().await;
                }
                info!("[GM] {} auto-banned (5 warnings)", nick);
            } else {
                // Jail for warnings*5 minutes
                let jail_minutes = new_warnings * 5;
                if let Some(user) = state.users.get_mut(&tc) {
                    user.jail_timer = jail_minutes * 60;
                }
                state.send_msg_id(tc, 659, &jail_minutes.to_string()).await;
                warp_user(state, tc, 78, 50, 50).await;
            }

            info!("[GM] {} warned {} (#{}) for: {}", admin_name, nick, new_warnings, reason);
        }
        None => {
            state.send_msg_id(conn_id, 440, "").await;
        }
    }
}

/// /KILL nick — Kill user instantly.
pub(super) async fn handle_slash_kill(state: &mut GameState, conn_id: ConnectionId, target: &str) {
    match state.users.get(&conn_id) {
        Some(u) if u.logged && u.privileges >= privilege_level::SEMIDIOS => {}
        _ => return,
    }

    let target_upper = target.to_uppercase();
    let target_conn = state.users.values()
        .find(|u| u.logged && u.char_name.to_uppercase() == target_upper)
        .map(|u| u.conn_id);

    match target_conn {
        Some(tc) => {
            let admin_name = state.users.get(&conn_id).map(|u| u.char_name.clone()).unwrap_or_default();
            let (map, x, y) = state.users.get(&tc).map(|u| (u.pos_map, u.pos_x, u.pos_y)).unwrap_or((0, 0, 0));

            // Kill the user
            user_die(state, tc, None).await;

            // Broadcast
            state.send_msg_id_to(SendTarget::ToArea { map, x, y }, 753, &format!("{}@{}", admin_name, target)).await;
            info!("[GM] {} killed {}", admin_name, target);
        }
        None => {
            state.send_msg_id(conn_id, 196, "").await;
        }
    }
}

/// /ECHAR nick — Kick user (same as /KICK but VB6 name).
pub(super) async fn handle_slash_echar(state: &mut GameState, conn_id: ConnectionId, target: &str) {
    handle_slash_kick(state, conn_id, target).await;
}

/// /STOP nick — Freeze user (can't move).
pub(super) async fn handle_slash_stop(state: &mut GameState, conn_id: ConnectionId, target: &str, freeze: bool) {
    match state.users.get(&conn_id) {
        Some(u) if u.logged && u.privileges >= privilege_level::SEMIDIOS => {}
        _ => return,
    }

    let target_upper = target.to_uppercase();
    let target_conn = state.users.values()
        .find(|u| u.logged && u.char_name.to_uppercase() == target_upper)
        .map(|u| u.conn_id);

    match target_conn {
        Some(tc) => {
            if let Some(user) = state.users.get_mut(&tc) {
                user.paralyzed = freeze;
            }
            let status = if freeze { "paralizado" } else { "desparalizado" };
            state.send_console(conn_id, &format!("{} {}.", target, status), font_index::INFO).await;
        }
        None => {
            state.send_msg_id(conn_id, 196, "").await;
        }
    }
}

pub(super) async fn handle_slash_cheat(state: &mut GameState, conn_id: ConnectionId, target: &str) {
    match state.users.get(&conn_id) {
        Some(u) if u.logged && u.privileges >= privilege_level::ADMINISTRADOR => {}
        _ => return,
    }

    let target_upper = target.to_uppercase();
    let target_conn = if target_upper == "YO" {
        Some(conn_id)
    } else {
        state.users.values()
            .find(|u| u.logged && u.char_name.to_uppercase() == target_upper)
            .map(|u| u.conn_id)
    };

    match target_conn {
        Some(tc) => {
            if let Some(user) = state.users.get_mut(&tc) {
                user.min_hp = user.max_hp;
                user.min_mana = user.max_mana;
                user.min_sta = user.max_sta;
                user.min_agua = user.max_agua;
                user.min_ham = user.max_ham;
            }
            // Send stat updates
            let u = state.users.get(&tc).unwrap();
            let hp_pkt = binary_packets::write_update_hp(u.max_hp as i16, u.min_hp as i16);
            let mana_pkt = binary_packets::write_update_mana(u.max_mana as i16, u.min_mana as i16);
            let sta_pkt = binary_packets::write_update_sta(u.max_sta as i16, u.min_sta as i16);
            state.send_bytes(tc, &hp_pkt).await;
            state.send_bytes(tc, &mana_pkt).await;
            state.send_bytes(tc, &sta_pkt).await;

            state.send_console(conn_id, &format!("{} restaurado.", target), font_index::INFO).await;
        }
        None => {
            state.send_msg_id(conn_id, 196, "").await;
        }
    }
}

/// /NOADV <name> — Clear all warnings/penalties for a character. Requires Semidios+.
pub(super) async fn handle_slash_noadv(state: &mut GameState, conn_id: ConnectionId, target: &str) {
    match state.users.get(&conn_id) {
        Some(u) if u.logged && u.privileges >= privilege_level::SEMIDIOS => {}
        _ => return,
    }
    let pool = state.pool.clone();
    if !crate::db::charfile::character_exists(&pool, target).await {
        state.send_msg_id(conn_id, 196, "").await;
        return;
    }
    let _ = crate::db::charfile::clear_penalties(&pool, target).await;
    state.send_msg_id(conn_id, 441, "").await;
    info!("[GM] Cleared penalties for {}", target);
}

/// /LIBERAR <name> — Release player from prison. Requires Semidios+.
pub(super) async fn handle_slash_liberar(state: &mut GameState, conn_id: ConnectionId, target: &str) {
    let gm_name = match state.users.get(&conn_id) {
        Some(u) if u.logged && u.privileges >= privilege_level::SEMIDIOS => u.char_name.clone(),
        _ => return,
    };

    let target_upper = target.to_uppercase();
    let target_conn = state.online_names.get(&target_upper).copied();

    if let Some(t_conn) = target_conn {
        // Warp target to Ullathorpe (map 1, 50, 50) as release location
        warp_user(state, t_conn, 1, 50, 50).await;
        state.send_msg_id_to(SendTarget::ToAdmins, 443, &format!("{}@{}", gm_name, target)).await;
        state.send_msg_id(t_conn, 444, "").await; // You've been released
        info!("[GM] {} released {} from prison", gm_name, target);
    } else {
        state.send_msg_id(conn_id, 198, "").await; // Not online
    }
}

/// /PENAS <name> — View penalties of a character. Requires Semidios+.
pub(super) async fn handle_slash_penas(state: &mut GameState, conn_id: ConnectionId, target: &str) {
    match state.users.get(&conn_id) {
        Some(u) if u.logged && u.privileges >= privilege_level::SEMIDIOS => {}
        _ => return,
    }

    if !crate::db::charfile::character_exists(&state.pool, target).await {
        state.send_msg_id(conn_id, 196, "").await;
        return;
    }

    let penalties = crate::db::charfile::load_penalties(&state.pool, target).await;
    state.send_msg_id(conn_id, 327, &penalties.len().to_string()).await;

    for p in &penalties {
        state.send_msg_id(conn_id, 327, p).await;
    }
}

/// Generic privilege setter — /CONSEJERO, /SEMIDIOS, /DIOS, /GDIOS, /EVENT, /DIRECTOR,
/// /SUBADMINISTRADOR, /DEVELOPER, /ADMIN, /PJ
pub(super) async fn handle_slash_set_privilege(
    state: &mut GameState,
    conn_id: ConnectionId,
    target: &str,
    new_level: i32,
    level_name: &str,
    required_level: i32,
) {
    let gm_name = match state.users.get(&conn_id) {
        Some(u) if u.logged && u.privileges >= required_level => u.char_name.clone(),
        _ => return,
    };

    let target_upper = target.to_uppercase();

    // SHAY protection: if targeting SHAY and GM is not SHAY, punish the GM
    if target_upper == "SHAY" && gm_name.to_uppercase() != "SHAY" {
        // Demote the attacker instead (matching VB6 behavior)
        if let Some(user) = state.users.get_mut(&conn_id) {
            user.privileges = new_level.min(user.privileges);
        }
        // Re-warp to refresh
        let (map, x, y) = state.users.get(&conn_id)
            .map(|u| (u.pos_map, u.pos_x, u.pos_y))
            .unwrap_or((1, 50, 50));
        warp_user(state, conn_id, map, x, y).await;
        return;
    }

    // Find target
    let target_conn = match state.online_names.get(&target_upper).copied() {
        Some(c) => c,
        None => {
            state.send_msg_id(conn_id, 198, "").await; // Not online
            return;
        }
    };

    let target_real_name = state.users.get(&target_conn)
        .map(|u| u.char_name.clone())
        .unwrap_or_default();

    // Announce to admins
    state.send_msg_id_to(SendTarget::ToAdmins, 562, &format!("{}@{}@{}", gm_name, target_real_name, level_name)).await;

    // Set the new privilege level
    if let Some(user) = state.users.get_mut(&target_conn) {
        user.privileges = new_level;
    }

    // Re-warp to refresh character appearance (privilege affects CC packet)
    let (map, x, y) = state.users.get(&target_conn)
        .map(|u| (u.pos_map, u.pos_x, u.pos_y))
        .unwrap_or((1, 50, 50));
    warp_user(state, target_conn, map, x, y).await;

    info!("[GM] {} set {} to {} (level {})", gm_name, target_real_name, level_name, new_level);
}

/// /CHANGENICK <newname> — Change own character name. Requires Administrador.
pub(super) async fn handle_slash_changenick(state: &mut GameState, conn_id: ConnectionId, new_name: &str) {
    match state.users.get(&conn_id) {
        Some(u) if u.logged && u.privileges >= privilege_level::ADMINISTRADOR => {}
        _ => return,
    }

    if new_name.is_empty() { return; }

    // Remove old name from online_names
    let old_name = state.users.get(&conn_id).map(|u| u.char_name.clone()).unwrap_or_default();
    state.online_names.remove(&old_name.to_uppercase());

    // Set new name
    if let Some(user) = state.users.get_mut(&conn_id) {
        user.char_name = new_name.to_string();
    }
    state.online_names.insert(new_name.to_uppercase(), conn_id);

    // Re-warp to refresh CC packet with new name
    let (map, x, y) = state.users.get(&conn_id)
        .map(|u| (u.pos_map, u.pos_x, u.pos_y))
        .unwrap_or((1, 50, 50));
    warp_user(state, conn_id, map, x, y).await;

    info!("[GM] {} changed nick to {}", old_name, new_name);
}

/// /BORRARPJ <name> — Delete a character. Requires Director+.
pub(super) async fn handle_slash_borrarpj(state: &mut GameState, conn_id: ConnectionId, target: &str) {
    let gm_name = match state.users.get(&conn_id) {
        Some(u) if u.logged && u.privileges >= privilege_level::DIRECTOR => u.char_name.clone(),
        _ => return,
    };

    let target_upper = target.to_uppercase();

    // SHAY protection
    if target_upper == "SHAY" && gm_name.to_uppercase() != "SHAY" { return; }

    // If target is online, disconnect them
    if let Some(&t_conn) = state.online_names.get(&target_upper) {
        close_connection(state, t_conn).await;
    }

    let pool = state.pool.clone();

    // Check character exists in DB
    if !crate::db::charfile::character_exists(&pool, target).await {
        state.send_msg_id(conn_id, 196, "").await;
        return;
    }

    // Delete character from DB (CASCADE handles inventory, bank, mail, penalties)
    if let Err(e) = crate::db::charfile::delete_charfile(&pool, target).await {
        error!("[GM] Failed to delete character {}: {}", target, e);
        return;
    }

    // Announce
    state.send_msg_id_to(SendTarget::ToAll, 565, &format!("{}@{}", gm_name, target)).await;

    info!("[GM] {} deleted character {}", gm_name, target);
}

/// /BANHD <name> — Ban player's HD + IP + account. Requires Administrador.
pub(super) async fn handle_slash_banhd(state: &mut GameState, conn_id: ConnectionId, target: &str) {
    let gm_name = match state.users.get(&conn_id) {
        Some(u) if u.logged && u.privileges >= privilege_level::ADMINISTRADOR => u.char_name.clone(),
        _ => return,
    };

    let target_upper = target.to_uppercase();

    // SHAY protection
    if target_upper == "SHAY" { return; }

    let target_conn = match state.online_names.get(&target_upper).copied() {
        Some(c) => c,
        None => {
            state.send_msg_id(conn_id, 196, "").await;
            return;
        }
    };

    // Get target data
    let (target_ip, target_hd, target_account) = match state.users.get(&target_conn) {
        Some(u) => (u.ip.clone(), u.hd_serial.clone(), u.account_name.clone()),
        None => return,
    };

    // Set ban flag in DB
    let pool = state.pool.clone();
    let _ = crate::db::charfile::set_char_banned(&pool, target, true).await;
    let _ = crate::db::charfile::add_penalty(&pool, target, "Tolerancia 0.").await;

    // Ban HD serial
    if !target_hd.is_empty() {
        let _ = state.bans.ban_hd(&state.pool, &target_hd).await;
    }

    // Ban IP
    if !target_ip.is_empty() {
        let _ = state.bans.ban_ip(&state.pool, &target_ip).await;
    }

    // Ban account
    if !target_account.is_empty() {
        let _ = crate::db::accounts::set_account_banned(&state.pool, &target_account, true, "Tolerancia 0").await;
    }

    // Disconnect the target
    close_connection(state, target_conn).await;

    // Announce
    state.send_msg_id_to(SendTarget::ToAll, 567, &format!("{}@{}", gm_name, target)).await;
    state.send_msg_id_to(SendTarget::ToAll, 568, &format!("{}@{}", gm_name, target_hd)).await;
    state.send_msg_id_to(SendTarget::ToAll, 569, &format!("{}@{}", gm_name, target_ip)).await;
    state.send_msg_id_to(SendTarget::ToAll, 570, &format!("{}@{}", gm_name, target_account)).await;

    info!("[GM] {} banned {} (HD={}, IP={}, Account={})", gm_name, target, target_hd, target_ip, target_account);
}

// =============================================================================

/// /PERDON <name> — Forgive user: set criminal=false, broadcast updated appearance. Requires ADMIN+.
pub(super) async fn handle_slash_perdon(state: &mut GameState, conn_id: ConnectionId, target: &str) {
    match state.users.get(&conn_id) {
        Some(u) if u.logged && u.privileges >= privilege_level::ADMINISTRADOR => {}
        _ => {
            state.send_console(conn_id, "No tenes permisos para usar este comando.", font_index::INFO).await;
            return;
        }
    }

    let target_id = match state.find_user_by_name(target) {
        Some(id) => id,
        None => {
            state.send_console(conn_id, &format!("Jugador '{}' no encontrado.", target), font_index::INFO).await;
            return;
        }
    };

    let (map, x, y) = match state.users.get(&target_id) {
        Some(u) => (u.pos_map, u.pos_x, u.pos_y),
        None => return,
    };

    if let Some(user) = state.users.get_mut(&target_id) {
        user.criminal = false;
    }

    // Re-broadcast appearance with updated nick color (citizen = blue)
    let cc = state.users.get(&target_id).unwrap().build_cc_binary();
    state.send_data_bytes(SendTarget::ToArea { map, x, y }, &cc).await;

    let gm_name = state.users.get(&conn_id).map(|u| u.char_name.clone()).unwrap_or_default();
    state.send_console(target_id, "Has sido perdonado.", font_index::INFO).await;
    state.send_console(conn_id, &format!("{} ha sido perdonado.", target), font_index::INFO).await;
    info!("[GM] {} forgave {}", gm_name, target);
}

/// /EJECUTAR <name> — Execute user: kill with full penalty (lose exp, drop items). Requires DIOS+.
pub(super) async fn handle_slash_ejecutar(state: &mut GameState, conn_id: ConnectionId, target: &str) {
    match state.users.get(&conn_id) {
        Some(u) if u.logged && u.privileges >= privilege_level::DIOS => {}
        _ => {
            state.send_console(conn_id, "No tenes permisos para usar este comando.", font_index::INFO).await;
            return;
        }
    }

    let target_id = match state.find_user_by_name(target) {
        Some(id) => id,
        None => {
            state.send_console(conn_id, &format!("Jugador '{}' no encontrado.", target), font_index::INFO).await;
            return;
        }
    };

    let is_dead = state.users.get(&target_id).map(|u| u.dead).unwrap_or(true);
    if is_dead {
        state.send_console(conn_id, "Ese usuario ya esta muerto.", font_index::INFO).await;
        return;
    }

    // Kill with full penalty (user_die handles exp loss, item drop, etc.)
    user_die(state, target_id, None).await;

    let gm_name = state.users.get(&conn_id).map(|u| u.char_name.clone()).unwrap_or_default();
    state.send_console(target_id, "Has sido ejecutado por un GM.", font_index::INFO).await;
    state.send_console(conn_id, &format!("{} ha sido ejecutado.", target), font_index::INFO).await;
    info!("[GM] {} executed {}", gm_name, target);
}

/// /NOCAOS <name> — Kick from Chaos faction: set fuerzas_caos=false, reset kills. Requires ADMIN+.
pub(super) async fn handle_slash_nocaos(state: &mut GameState, conn_id: ConnectionId, target: &str) {
    match state.users.get(&conn_id) {
        Some(u) if u.logged && u.privileges >= privilege_level::ADMINISTRADOR => {}
        _ => {
            state.send_console(conn_id, "No tenes permisos para usar este comando.", font_index::INFO).await;
            return;
        }
    }

    let target_id = match state.find_user_by_name(target) {
        Some(id) => id,
        None => {
            state.send_console(conn_id, &format!("Jugador '{}' no encontrado.", target), font_index::INFO).await;
            return;
        }
    };

    if let Some(user) = state.users.get_mut(&target_id) {
        user.fuerzas_caos = false;
        user.ciudadanos_matados = 0;
        user.recompensas_caos = 0;
    }

    let gm_name = state.users.get(&conn_id).map(|u| u.char_name.clone()).unwrap_or_default();
    state.send_console(target_id, "Has sido expulsado de la Legion del Caos.", font_index::INFO).await;
    state.send_console(conn_id, &format!("{} expulsado de la Legion del Caos.", target), font_index::INFO).await;
    info!("[GM] {} kicked {} from Chaos faction", gm_name, target);
}

/// /NOREAL <name> — Kick from Royal Army: set armada_real=false, reset kills. Requires ADMIN+.
pub(super) async fn handle_slash_noreal(state: &mut GameState, conn_id: ConnectionId, target: &str) {
    match state.users.get(&conn_id) {
        Some(u) if u.logged && u.privileges >= privilege_level::ADMINISTRADOR => {}
        _ => {
            state.send_console(conn_id, "No tenes permisos para usar este comando.", font_index::INFO).await;
            return;
        }
    }

    let target_id = match state.find_user_by_name(target) {
        Some(id) => id,
        None => {
            state.send_console(conn_id, &format!("Jugador '{}' no encontrado.", target), font_index::INFO).await;
            return;
        }
    };

    if let Some(user) = state.users.get_mut(&target_id) {
        user.armada_real = false;
        user.criminales_matados = 0;
        user.recompensas_real = 0;
    }

    let gm_name = state.users.get(&conn_id).map(|u| u.char_name.clone()).unwrap_or_default();
    state.send_console(target_id, "Has sido expulsado de la Armada Real.", font_index::INFO).await;
    state.send_console(conn_id, &format!("{} expulsado de la Armada Real.", target), font_index::INFO).await;
    info!("[GM] {} kicked {} from Royal Army", gm_name, target);
}

