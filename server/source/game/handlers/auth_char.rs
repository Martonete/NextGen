//! Character management handlers: creation, deletion, dice roll, and account management.

use tracing::{info, warn};

use super::super::common::*;
use super::connect_user;
use crate::db::{accounts, charfile, password};
use crate::game::types::GameState;
use crate::net::ConnectionId;
use crate::protocol::binary_packets;

// =====================================================================
// Character creation/deletion handlers
// =====================================================================

/// CreateCharacter — Create new character.
pub(crate) async fn handle_create_character(
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
    let paso_hd = state
        .users
        .get(&conn_id)
        .map(|u| u.paso_hd)
        .unwrap_or(false);
    if !paso_hd {
        state.send_bytes(
            conn_id,
            &binary_packets::write_error_msg("Tu PC se encuentra bajo Tolerancia 0."),
        );
        close_connection(state, conn_id).await;
        return;
    }

    if !state.config.can_create_characters {
        state.send_bytes(
            conn_id,
            &binary_packets::write_error_msg(
                "La creacion de personajes en este servidor se ha deshabilitado.",
            ),
        );
        close_connection(state, conn_id).await;
        return;
    }

    if state.config.server_only_gms {
        state.send_bytes(conn_id, &binary_packets::write_error_msg("Servidor restringido a administradores. La creacion de personajes se encuentra deshabilitada."));
        close_connection(state, conn_id).await;
        return;
    }

    info!(
        "[AUTH] New character request: '{}' race='{}' class='{}' from #{}",
        char_name, race, class, conn_id
    );

    if !is_valid_name(&char_name) {
        state.send_bytes(
            conn_id,
            &binary_packets::write_error_msg("Nombre invalido."),
        );
        close_connection(state, conn_id).await;
        return;
    }

    if char_name.len() < 3 {
        state.send_bytes(
            conn_id,
            &binary_packets::write_error_msg(
                "El nombre del personaje debe tener al menos 3 caracteres.",
            ),
        );
        close_connection(state, conn_id).await;
        return;
    }

    let attributes = state
        .users
        .get(&conn_id)
        .map(|u| u.dice_attributes)
        .unwrap_or([18; 5]);

    let account_id = state.users.get(&conn_id).map(|u| u.account_id).unwrap_or(0);

    if account_id == 0 {
        state.send_bytes(
            conn_id,
            &binary_packets::write_error_msg("Debes iniciar sesion primero."),
        );
        close_connection(state, conn_id).await;
        return;
    }

    // VB6 13.3: max 5 characters per account
    let account_name_for_check = state
        .users
        .get(&conn_id)
        .map(|u| u.account_name.clone())
        .unwrap_or_default();
    if !account_name_for_check.is_empty() {
        let num_pjs = accounts::load_account(&state.pool, &account_name_for_check)
            .await
            .map(|a| a.num_pjs)
            .unwrap_or(5);
        if num_pjs >= 5 {
            state.send_bytes(
                conn_id,
                &binary_packets::write_error_msg("Ya tienes el maximo de 5 personajes."),
            );
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
    )
    .await
    {
        Ok(_char_id) => {
            info!(
                "[AUTH] Character '{}' created successfully — auto-logging in",
                char_name
            );
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
pub(crate) async fn handle_roll_dice(state: &mut GameState, conn_id: ConnectionId) {
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

    state.send_bytes(
        conn_id,
        &binary_packets::write_dice_roll(
            attrs[0] as u8,
            attrs[1] as u8,
            attrs[2] as u8,
            attrs[3] as u8,
            attrs[4] as u8,
        ),
    );
}

/// DeleteCharacter — Delete character.
pub(crate) async fn handle_delete_character(
    state: &mut GameState,
    conn_id: ConnectionId,
    char_name: &str,
    _account_name: &str,
    password: &str,
) {
    info!(
        "[AUTH] Delete character request: '{}' from #{}",
        char_name, conn_id
    );

    // Rate limit: 5-second cooldown between delete attempts
    let now = std::time::Instant::now();
    if let Some(user) = state.users.get(&conn_id) {
        if let Some(last) = user.last_delete_attempt {
            if now.duration_since(last).as_secs() < 5 {
                state.send_bytes(
                    conn_id,
                    &binary_packets::write_error_msg(
                        "Debes esperar antes de intentar borrar otro personaje.",
                    ),
                );
                return;
            }
        }
    }
    if let Some(user) = state.users.get_mut(&conn_id) {
        user.last_delete_attempt = Some(now);
    }

    // Cannot delete a character that is currently online
    if state.is_name_online(&char_name) {
        state.send_bytes(
            conn_id,
            &binary_packets::write_error_msg("No puedes borrar un personaje que esta conectado."),
        );
        return;
    }

    if !charfile::character_exists(&state.pool, &char_name).await {
        state.send_bytes(
            conn_id,
            &binary_packets::write_error_msg("El personaje no existe."),
        );
        close_connection(state, conn_id).await;
        return;
    }

    let char_data = match charfile::load_charfile(&state.pool, &char_name).await {
        Ok(data) => data,
        Err(_) => {
            state.send_bytes(
                conn_id,
                &binary_packets::write_error_msg("Error al leer el personaje."),
            );
            close_connection(state, conn_id).await;
            return;
        }
    };

    // Verify password (security_code from account)
    if char_data.password.to_uppercase() != password.to_uppercase() {
        state.send_bytes(
            conn_id,
            &binary_packets::write_error_msg("Password incorrecto."),
        );
        state.send_bytes(conn_id, &binary_packets::write_finish_ok());
        close_connection(state, conn_id).await;
        return;
    }

    // Block deletion of GMs/privileged characters (VB6 checks privilege level)
    if char_data.privileges > 0 {
        state.send_bytes(
            conn_id,
            &binary_packets::write_error_show("No podes borrar gms."),
        );
        return;
    }

    if char_data.level >= 50 {
        state.send_bytes(
            conn_id,
            &binary_packets::write_error_msg("No podes borrar usuarios nivel 50 o superior."),
        );
        return;
    }

    if char_data.guild_index > 0 {
        state.send_bytes(
            conn_id,
            &binary_packets::write_error_msg(
                "No podes borrar usuarios que esten dentro de un clan. Primero abandona el clan.",
            ),
        );
        return;
    }

    if let Err(e) = charfile::delete_charfile(&state.pool, &char_name).await {
        warn!("[AUTH] Failed to delete character: {}", e);
    }

    info!("[AUTH] Character '{}' deleted", char_name);
    state.send_bytes(conn_id, &binary_packets::write_finish_ok());
    state.send_bytes(
        conn_id,
        &binary_packets::write_error_show("Personaje Borrado con exito."),
    );
    close_connection(state, conn_id).await;
}

/// ChangePassword — Change account password.
pub(crate) async fn handle_change_password(
    state: &mut GameState,
    conn_id: ConnectionId,
    account_name: &str,
    old_password: &str,
    new_password: &str,
    confirm_password: &str,
) {
    info!(
        "[AUTH] Password change request for '{}' from #{}",
        account_name, conn_id
    );

    if new_password == old_password {
        state.send_bytes(
            conn_id,
            &binary_packets::write_error_show("No puedes volver a utilizar la misma contrasena."),
        );
        return;
    }

    if new_password.len() < 3 {
        state.send_bytes(
            conn_id,
            &binary_packets::write_error_show(
                "La contrasena debe tener un minimo de 3 caracteres.",
            ),
        );
        return;
    }

    if new_password != confirm_password {
        state.send_bytes(
            conn_id,
            &binary_packets::write_error_show("Las contrasenas no coinciden."),
        );
        return;
    }

    let account = match accounts::load_account(&state.pool, &account_name).await {
        Ok(acc) => acc,
        Err(_) => {
            state.send_bytes(
                conn_id,
                &binary_packets::write_error_show("La cuenta no existe."),
            );
            return;
        }
    };

    if !password::verify_password(&old_password, &account.password_hash) {
        state.send_bytes(
            conn_id,
            &binary_packets::write_error_show(
                "La Password actual que nos proporciono, no coincide con la del registro.",
            ),
        );
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
            state.send_bytes(
                conn_id,
                &binary_packets::write_error_show("Error al cambiar la password."),
            );
        }
    }
}

/// AccountRecovery — Account recovery via PIN.
pub(crate) async fn handle_account_recovery(
    state: &mut GameState,
    conn_id: ConnectionId,
    account_name: &str,
    pin: &str,
) {
    info!(
        "[AUTH] Account recovery request for '{}' from #{}",
        account_name, conn_id
    );

    let account = match accounts::load_account(&state.pool, &account_name).await {
        Ok(acc) => acc,
        Err(_) => {
            state.send_bytes(
                conn_id,
                &binary_packets::write_error_msg("La cuenta no existe."),
            );
            close_connection(state, conn_id).await;
            return;
        }
    };

    if pin.to_uppercase() != account.pin.to_uppercase() {
        state.send_bytes(
            conn_id,
            &binary_packets::write_error_msg("El pin ingresado no es correcto."),
        );
        close_connection(state, conn_id).await;
        return;
    }

    // Generate random 8-character alphanumeric password (excludes ambiguous chars: 0/O/1/l/I)
    let chars: Vec<char> = "ABCDEFGHJKLMNPQRSTUVWXYZabcdefghjkmnpqrstuvwxyz23456789"
        .chars()
        .collect();
    let new_pass: String = (0..8)
        .map(|_| chars[rand_range(0, chars.len() as i32 - 1) as usize])
        .collect();

    if let Ok(hash) = password::hash_password(&new_pass) {
        let _ = accounts::update_password(&state.pool, &account_name, &hash).await;
    }

    state.send_bytes(
        conn_id,
        &binary_packets::write_error_show(&format!(
            "Has recuperado la cuenta, utiliza la contrasena {} para poder logearte.",
            new_pass
        )),
    );

    info!(
        "[AUTH] Account '{}' recovered, new password generated",
        account_name
    );
}
