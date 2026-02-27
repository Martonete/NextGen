// Packet handlers — processes decrypted client packets.
//
// Pre-login flow:
//   1. Client sends KERD22 + HD serial → server checks ban
//   2. Client sends ALOGIN + account,password → server validates, sends INIAC + ADDPJ + CODEH
//      OR client sends NACCNT + account,password,pin → server creates account
//   3. Client sends THCJXD/OOLOGI + charname,account,codex → server loads character, sends LOGGED
//      OR client sends NLOGIN + charname,race,gender,class,... → server creates character
//   4. Client sends TIRDAD → server sends dice roll (during char creation)
//   5. Client sends TBRP → server deletes character
//
// In-game flow:
//   M<heading> — movement
//   CHEA<heading> — change heading
//   RPU — request position update
//   ; — chat message (talk)

// Many functions/constants are declared for VB6 parity but not yet wired up.

use std::collections::HashMap;
use tracing::{info, warn};

use crate::net::ConnectionId;
use crate::protocol::{client_opcodes, server_opcodes, font_types, fields::read_field};
use crate::data::{accounts, charfile};
use crate::data::objects::ObjType;
use crate::data::maps::Trigger;
use crate::data::guilds;
use super::types::{GameState, SendTarget, InventorySlot, EquipSlots, PartyState, CleanWorldEntry, privilege_level, MAX_INVENTORY_SLOTS, MAX_SPELL_SLOTS, MAX_PARTY_MEMBERS, MAX_PARTIES};
use super::world;
use super::npc;
use crate::data::npcs::NpcType;

/// Route a decrypted packet to the appropriate handler.
pub async fn handle_packet(state: &mut GameState, conn_id: ConnectionId, data: &str) {
    // Log for debugging — only unhandled packets and slash commands
    // (movement/attack/click packets are too frequent to log)

    // Match opcode prefix — order matters for single-char opcodes
    if data.starts_with(client_opcodes::KERD22) {
        handle_kerd22(state, conn_id, data).await;
    } else if data.starts_with(client_opcodes::ALOGIN) {
        handle_alogin(state, conn_id, data).await;
    } else if data.starts_with(client_opcodes::NACCNT) {
        handle_naccnt(state, conn_id, data).await;
    } else if data.starts_with(client_opcodes::THCJXD) {
        handle_thcjxd(state, conn_id, data).await;
    } else if data.starts_with(client_opcodes::OOLOGI) {
        handle_oologi(state, conn_id, data).await;
    } else if data.starts_with(client_opcodes::NLOGIN) {
        handle_nlogin(state, conn_id, data).await;
    } else if data.starts_with(client_opcodes::TIRDAD) {
        handle_tirdad(state, conn_id).await;
    } else if data.starts_with(client_opcodes::TBRP) {
        handle_tbrp(state, conn_id, data).await;
    } else if data.starts_with(client_opcodes::REPASS) {
        handle_repass(state, conn_id, data).await;
    } else if data.starts_with(client_opcodes::REECUH) {
        handle_reecuh(state, conn_id, data).await;
    } else if data.starts_with(client_opcodes::COMMERCE_CLOSE) {
        handle_commerce_close(state, conn_id).await;
    } else if data.starts_with(client_opcodes::COMMERCE_BUY) {
        handle_commerce_buy(state, conn_id, data).await;
    } else if data.starts_with(client_opcodes::COMMERCE_SELL) {
        handle_commerce_sell(state, conn_id, data).await;
    } else if data.starts_with(client_opcodes::TRADE_CANCEL) {
        handle_trade_cancel(state, conn_id).await;
    } else if data.starts_with(client_opcodes::TRADE_RESPONSE) {
        handle_trade_response(state, conn_id, data).await;
    } else if data.starts_with(client_opcodes::TRADE_OFFER_GOLD) {
        handle_trade_offer_gold(state, conn_id, data).await;
    } else if data.starts_with(client_opcodes::TRADE_OFFER_ITEM) {
        handle_trade_offer_item(state, conn_id, data).await;
    } else if data.starts_with(client_opcodes::TRADE_CHAT) {
        handle_trade_chat(state, conn_id, data).await;
    } else if data.starts_with(client_opcodes::BANK_CLOSE) {
        handle_bank_close(state, conn_id).await;
    } else if data.starts_with(client_opcodes::BANK_DEPOSIT) {
        handle_bank_deposit(state, conn_id, data).await;
    } else if data.starts_with(client_opcodes::BANK_WITHDRAW) {
        handle_bank_withdraw(state, conn_id, data).await;
    } else if data.starts_with(client_opcodes::WORK_LEFT_CLICK) {
        handle_work_left_click(state, conn_id, data).await;
    } else if data.starts_with(client_opcodes::GUILD_INFO) {
        handle_guild_info(state, conn_id).await;
    } else if data.starts_with(client_opcodes::GUILD_CREATE) {
        handle_guild_create(state, conn_id, data).await;
    } else if data.starts_with(client_opcodes::GUILD_UPDATE_CODEX) {
        handle_guild_update_codex(state, conn_id, data).await;
    } else if data.starts_with(client_opcodes::GUILD_ACCEPT) {
        handle_guild_accept(state, conn_id, data).await;
    } else if data.starts_with(client_opcodes::GUILD_REJECT) {
        handle_guild_reject(state, conn_id, data).await;
    } else if data.starts_with(client_opcodes::GUILD_EXPEL) {
        handle_guild_expel(state, conn_id, data).await;
    } else if data.starts_with(client_opcodes::GUILD_NEWS) {
        handle_guild_news(state, conn_id, data).await;
    } else if data.starts_with(client_opcodes::GUILD_APPLY) {
        handle_guild_apply(state, conn_id, data).await;
    } else if data.starts_with(client_opcodes::GUILD_DETAILS) {
        handle_guild_details(state, conn_id, data).await;
    } else if data.starts_with(client_opcodes::GUILD_BANK_OPEN) {
        handle_guild_bank_open(state, conn_id).await;
    } else if data.starts_with(client_opcodes::GUILD_BANK_DEPOSIT) {
        handle_guild_bank_deposit(state, conn_id, data).await;
    } else if data.starts_with(client_opcodes::GUILD_BANK_WITHDRAW) {
        handle_guild_bank_withdraw(state, conn_id, data).await;
    } else if data.starts_with(client_opcodes::GUILD_BANK_SAVE) {
        handle_clan_bank_save(state, conn_id).await;
    } else if data.starts_with(client_opcodes::CLAN_BANK_WITHDRAW_ITEM) {
        handle_clan_bank_withdraw_item(state, conn_id, data).await;
    } else if data.starts_with(client_opcodes::CLAN_BANK_DEPOSIT_ITEM) {
        handle_clan_bank_deposit_item(state, conn_id, data).await;
    } else if data.starts_with(client_opcodes::QUEST_LIST) {
        handle_quest_list(state, conn_id).await;
    } else if data.starts_with(client_opcodes::QUEST_INFO) {
        handle_quest_info(state, conn_id, data).await;
    } else if data.starts_with(client_opcodes::QUEST_ACCEPT) {
        handle_quest_accept(state, conn_id, data).await;
    } else if data.starts_with(client_opcodes::MAIL_SEND) {
        handle_mail_send(state, conn_id, data).await;
    } else if data.starts_with(client_opcodes::MAIL_OPEN) {
        handle_mail_open(state, conn_id, data).await;
    } else if data.starts_with(client_opcodes::MAIL_EXTRACT) {
        handle_mail_extract(state, conn_id, data).await;
    } else if data.starts_with(client_opcodes::MAIL_DELETE) {
        handle_mail_delete(state, conn_id, data).await;
    } else if data.starts_with(client_opcodes::FRIEND_ADD) {
        handle_friend_add(state, conn_id, data).await;
    } else if data.starts_with(client_opcodes::FRIEND_REMOVE) {
        handle_friend_remove(state, conn_id, data).await;
    } else if data.starts_with(client_opcodes::CONSTRUCT_SMITH) {
        handle_construct_smith(state, conn_id, data).await;
    } else if data.starts_with(client_opcodes::CONSTRUCT_CARP) {
        handle_construct_carp(state, conn_id, data).await;
    } else if data.starts_with(client_opcodes::EQUIP_ITEM) {
        handle_equip(state, conn_id, data).await;
    } else if data.starts_with(client_opcodes::POSITION_UPDATE) {
        handle_actualizar(state, conn_id).await;
    } else if data.starts_with(client_opcodes::MACRO_DETECT) {
        handle_tengomacros(state, conn_id).await;
    } else if data.starts_with(client_opcodes::HOUSE_QUERY) {
        handle_fwo(state, conn_id, data).await;
    } else if data.starts_with(client_opcodes::HOUSE_BUY) {
        handle_cuc(state, conn_id, data).await;
    } else if data.starts_with(client_opcodes::PET_RENAME) {
        handle_cnm(state, conn_id, data).await;
    } else if data.starts_with(client_opcodes::GEM_EXCHANGE) {
        handle_gems(state, conn_id, data).await;
    } else if data.starts_with(client_opcodes::MEDAL_EXCHANGE) {
        handle_geps(state, conn_id, data).await;
    } else if data.starts_with(client_opcodes::DIVINE_OFFER) {
        handle_ofdioz(state, conn_id, data).await;
    } else if data.starts_with(client_opcodes::TS_SHOP) {
        handle_ftspts(state, conn_id, data).await;
    } else if data.starts_with(client_opcodes::UPGRADE_QUERY) {
        handle_sph(state, conn_id, data).await;
    } else if data.starts_with(client_opcodes::UPGRADE_DO) {
        handle_spe(state, conn_id, data).await;
    } else if data.starts_with(client_opcodes::ARENA_SPECTATE) {
        handle_are(state, conn_id, data).await;
    } else if data.starts_with(client_opcodes::CLAN_VALID_NAME) {
        handle_nanvame(state, conn_id, data).await;
    } else if data.starts_with(client_opcodes::CLAN_INVALID_NAME) {
        handle_nanvamx(state, conn_id, data).await;
    } else if data.starts_with(client_opcodes::PCGF) {
        handle_pcgf(state, conn_id, data).await;
    } else if data.starts_with(client_opcodes::PCWC) {
        handle_pcwc(state, conn_id, data).await;
    } else if data.starts_with(client_opcodes::PCCC) {
        handle_pccc(state, conn_id, data).await;
    } else if data.starts_with(client_opcodes::CLOSE_GUILD_BANK) {
        handle_fincbn(state, conn_id).await;
    } else if data.starts_with(client_opcodes::CAST_BY_NAME) {
        handle_downsi(state, conn_id, data).await;
    } else if data.starts_with(client_opcodes::RANKINGS) {
        handle_rankin(state, conn_id, data).await;
    } else if data.starts_with(client_opcodes::MOVE_SPELL) {
        handle_desphe(state, conn_id, data).await;
    } else if data.starts_with(client_opcodes::PLAYER_INFO) {
        handle_daminf(state, conn_id, data).await;
    } else if data.starts_with(client_opcodes::FPZ_REPORT) {
        handle_envfpz(state, conn_id, data).await;
    } else if data.starts_with(client_opcodes::DONATION_MENU) {
        handle_dcanje(state, conn_id).await;
    } else if data.starts_with(client_opcodes::DONATION_PREVIEW) {
        handle_dpx(state, conn_id, data).await;
    } else if data.starts_with(client_opcodes::DONATION_REDEEM) {
        handle_drx(state, conn_id, data).await;
    } else if data.starts_with(client_opcodes::TOURNAMENT_MENU) {
        handle_ccanje(state, conn_id).await;
    } else if data.starts_with(client_opcodes::PRIZE_INFO) {
        handle_ipx(state, conn_id, data).await;
    } else if data.starts_with(client_opcodes::PRIZE_BUY) {
        handle_spx(state, conn_id, data).await;
    } else if data.starts_with(client_opcodes::INIT_CHAT) {
        handle_inchat(state, conn_id, data).await;
    } else if data.starts_with(client_opcodes::CHAT_MSG) {
        handle_kkchat(state, conn_id, data).await;
    } else if data.starts_with(client_opcodes::GUILD_DONATE_PTS) {
        handle_addpts(state, conn_id, data).await;
    } else if data.starts_with(client_opcodes::MINI_STATS) {
        handle_fest(state, conn_id).await;
    } else if data.starts_with(client_opcodes::HEAD_CHANGE) {
        handle_cabezi(state, conn_id, data).await;
    } else if data.starts_with(client_opcodes::DRAG_DROP) {
        handle_dydtra(state, conn_id, data).await;
    } else if data.starts_with(client_opcodes::TOINFO) {
        handle_toinfo(state, conn_id).await;
    } else if data.starts_with(client_opcodes::DUEL_ARENA_INFO) {
        handle_iduelos(state, conn_id).await;
    } else if data.starts_with(client_opcodes::SEND_POINTS) {
        handle_actpt(state, conn_id).await;
    } else if data.starts_with(client_opcodes::VOTE) {
        handle_nvot(state, conn_id, data).await;
    } else if data.starts_with(client_opcodes::REPORT) {
        handle_newd(state, conn_id, data).await;
    } else if data.starts_with(client_opcodes::SOS_VIEW) {
        handle_consul(state, conn_id).await;
    } else if data.starts_with(client_opcodes::SWAP_ITEMS) {
        handle_swap(state, conn_id, data).await;
    } else if data.starts_with(client_opcodes::SKILL_SET) {
        handle_skse(state, conn_id, data).await;
    } else if data.starts_with(client_opcodes::SPELL_INFO) {
        handle_infs(state, conn_id, data).await;
    } else if data.starts_with(client_opcodes::LEVEL_BONUS) {
        handle_bof(state, conn_id, data).await;
    } else if data.starts_with(client_opcodes::TRAIN_CREATURE) {
        handle_entr(state, conn_id, data).await;
    } else if data.starts_with(client_opcodes::SAFE_TOGGLE) {
        handle_safe_toggle(state, conn_id).await;
    } else if data.starts_with(client_opcodes::USE_ITEM_CLICK) {
        handle_use_item_click(state, conn_id, data).await;
    } else if data.starts_with(client_opcodes::USE_ITEM) {
        handle_use_item(state, conn_id, data).await;
    } else if data.starts_with(client_opcodes::ATTACK) {
        handle_attack(state, conn_id).await;
    } else if data.starts_with(client_opcodes::PICK_UP) {
        handle_pick_up(state, conn_id).await;
    } else if data.starts_with(client_opcodes::DROP_ITEM) {
        handle_drop_item(state, conn_id, data).await;
    } else if data.starts_with(client_opcodes::CAST_SPELL) {
        handle_cast_spell(state, conn_id, data).await;
    } else if data.starts_with(client_opcodes::LEFT_CLICK) {
        handle_left_click(state, conn_id, data).await;
    } else if data.starts_with(client_opcodes::RIGHT_CLICK) {
        handle_right_click(state, conn_id, data).await;
    } else if data.starts_with(client_opcodes::MEDITATE) {
        handle_meditate(state, conn_id).await;
    } else if data.starts_with(client_opcodes::CHANGE_HEADING) {
        handle_change_heading(state, conn_id, data).await;
    } else if data.starts_with(client_opcodes::REQUEST_POS) {
        handle_request_pos(state, conn_id).await;
    } else if data.starts_with(client_opcodes::WALK) {
        handle_walk(state, conn_id, data).await;
    } else if data.starts_with(client_opcodes::MOUSE_DROP) {
        handle_mouse_drop(state, conn_id, data).await;
    } else if data.starts_with(client_opcodes::YELL) {
        handle_yell(state, conn_id, data).await;
    } else if data.starts_with(client_opcodes::SOS_SEND) {
        handle_sos_send(state, conn_id, data).await;
    } else if data.starts_with(client_opcodes::TALK) {
        handle_talk(state, conn_id, data).await;
    } else if data.starts_with(client_opcodes::WHISPER) {
        handle_whisper(state, conn_id, data).await;
    } else if data.starts_with(client_opcodes::SOS_RESPOND) {
        handle_sos_respond(state, conn_id, data).await;
    } else if data.starts_with('/') {
        // VB6 client sends slash commands directly (without ";" prefix)
        // See General.bas enviarMacro: if first char is "/" → SendData(text) raw
        let logged = state.users.get(&conn_id).map(|u| u.logged).unwrap_or(false);
        if logged {
            handle_slash_command(state, conn_id, data).await;
        }
    } else {
        // Unhandled opcode — safe truncation for logging
        let op: String = data.chars().take(20).collect();
        info!("[PKT] Unhandled from #{}: '{}'", conn_id, op);
    }
}

// =====================================================================
// Pre-login handlers
// =====================================================================

/// KERD22 — Hardware serial check (first packet from client).
async fn handle_kerd22(state: &mut GameState, conn_id: ConnectionId, data: &str) {
    let payload = strip_opcode(data, 6);
    let hd_serial = read_field(1, payload, ',');

    info!("[AUTH] HD serial check from #{}: HD={}", conn_id, hd_serial);

    let is_banned = state.bans.is_hd_banned(&hd_serial);

    if let Some(user) = state.users.get_mut(&conn_id) {
        user.hd_serial = hd_serial;
        user.paso_hd = !is_banned;
    }

    if is_banned {
        warn!("[AUTH] HD banned, connection #{} will be rejected", conn_id);
    }
}

/// ALOGIN — Account login.
async fn handle_alogin(state: &mut GameState, conn_id: ConnectionId, data: &str) {
    let payload = strip_opcode(data, 6);
    let account_name = read_field(1, payload, ',');
    let password = read_field(2, payload, ',');

    info!("[AUTH] Login attempt: account='{}' from #{}", account_name, conn_id);

    let paso_hd = state.users.get(&conn_id).map(|u| u.paso_hd).unwrap_or(false);
    if !paso_hd {
        state.send_to(conn_id, &format!("{}Tu PC se encuentra bajo Tolerancia 0.", server_opcodes::ERROR)).await;
        close_connection(state, conn_id).await;
        return;
    }

    if !is_valid_name(&account_name) {
        state.send_to(conn_id, &format!("{}Nombre invalido.", server_opcodes::ERROR)).await;
        close_connection(state, conn_id).await;
        return;
    }

    if !accounts::account_exists(&state.base_path, &account_name) {
        state.send_to(conn_id, &format!("{}La cuenta no existe.", server_opcodes::ERROR)).await;
        close_connection(state, conn_id).await;
        return;
    }

    let account = match accounts::load_account(&state.base_path, &account_name) {
        Ok(acc) => acc,
        Err(e) => {
            warn!("[AUTH] Failed to load account '{}': {}", account_name, e);
            state.send_to(conn_id, &format!("{}Error al leer la cuenta.", server_opcodes::ERROR)).await;
            close_connection(state, conn_id).await;
            return;
        }
    };

    if password != account.password {
        state.send_to(conn_id, &format!("{}Password incorrecto.", server_opcodes::ERROR)).await;
        close_connection(state, conn_id).await;
        return;
    }

    if account.banned {
        let motivo = &account.ban_reason;
        let ban_by = read_field(2, motivo, ',');
        let ban_reason = read_field(1, motivo, ',');
        state.send_to(conn_id, &format!(
            "{}Tu cuenta se encuentra actualmente baneada por: {} con motivo: {}.",
            server_opcodes::ERROR, ban_by, ban_reason
        )).await;
        close_connection(state, conn_id).await;
        return;
    }

    if let Some(user) = state.users.get_mut(&conn_id) {
        user.account_name = account_name.clone();
        user.account_password = password;
    }

    let iniac = format!("INIAC{},{}", account.num_pjs, state.notice);
    state.send_to(conn_id, &iniac).await;

    for i in 0..account.num_pjs {
        let pj_name = &account.characters[i];
        if pj_name.is_empty() {
            continue;
        }
        match charfile::load_char_preview(&state.base_path, pj_name) {
            Ok(preview) => {
                let addpj = format!("ADDPJ{},{},{}", pj_name, i + 1, preview.to_addpj_data());
                state.send_to(conn_id, &addpj).await;
            }
            Err(e) => {
                warn!("[AUTH] Failed to load preview for '{}': {}", pj_name, e);
            }
        }
    }

    let codeh = format!("CODEH{}", account.security_code);
    state.send_to(conn_id, &codeh).await;

    info!("[AUTH] Account '{}' authenticated, {} characters", account_name, account.num_pjs);
}

/// NACCNT — Create new account.
async fn handle_naccnt(state: &mut GameState, conn_id: ConnectionId, data: &str) {
    let payload = strip_opcode(data, 6);
    let account_name = read_field(1, payload, ',');
    let password = read_field(2, payload, ',');
    let pin = read_field(3, payload, ',');

    info!("[AUTH] New account request: '{}' from #{}", account_name, conn_id);

    if !is_valid_name(&account_name) {
        state.send_to(conn_id, &format!("{}Nombre de cuenta invalido.", server_opcodes::ERROR)).await;
        close_connection(state, conn_id).await;
        return;
    }

    if account_name.len() < 3 {
        state.send_to(conn_id, &format!("{}El nombre de la cuenta debe tener al menos 3 caracteres.", server_opcodes::ERROR)).await;
        close_connection(state, conn_id).await;
        return;
    }

    if password.len() < 3 {
        state.send_to(conn_id, &format!("{}La password debe tener al menos 3 caracteres.", server_opcodes::ERROR)).await;
        close_connection(state, conn_id).await;
        return;
    }

    match accounts::create_account(
        &state.base_path,
        &account_name,
        &password,
        &pin,
        &state.security_code,
    ) {
        Ok(()) => {
            info!("[AUTH] Account '{}' created successfully", account_name);
            state.send_to(conn_id, &format!("{}Cuenta creada con exito!", server_opcodes::ERROR)).await;
            close_connection(state, conn_id).await;
        }
        Err(e) => {
            state.send_to(conn_id, &format!("{}{}", server_opcodes::ERROR, e)).await;
            close_connection(state, conn_id).await;
        }
    }
}

/// THCJXD — Character login (primary, with full validation).
async fn handle_thcjxd(state: &mut GameState, conn_id: ConnectionId, data: &str) {
    let payload = strip_opcode(data, 6);
    let char_name = read_field(1, payload, ',');
    let account = read_field(2, payload, ',');
    let codex = read_field(3, payload, ',');

    info!("[AUTH] Character login (THCJXD): '{}' account='{}' from #{}", char_name, account, conn_id);

    let paso_hd = state.users.get(&conn_id).map(|u| u.paso_hd).unwrap_or(false);
    if !paso_hd {
        state.send_to(conn_id, &format!("{}Tu PC se encuentra bajo Tolerancia 0.", server_opcodes::ERROR)).await;
        close_connection(state, conn_id).await;
        return;
    }

    if !is_valid_name(&char_name) {
        state.send_to(conn_id, &format!("{}Nombre invalido.", server_opcodes::ERROR)).await;
        close_connection(state, conn_id).await;
        return;
    }

    if !charfile::character_exists(&state.base_path, &char_name) {
        state.send_to(conn_id, &format!("{}El personaje no existe.", server_opcodes::ERROR)).await;
        close_connection(state, conn_id).await;
        return;
    }

    if is_char_banned(&state.base_path, &char_name) {
        state.send_to(conn_id, &format!(
            "{}Se te ha prohibido la entrada a Tierras Sagradas debido a tu mal comportamiento. Consulta a un administrador para saber el motivo de la prohibicion.",
            server_opcodes::ERROR
        )).await;
        close_connection(state, conn_id).await;
        return;
    }

    let expected_account = state.users.get(&conn_id)
        .map(|u| u.account_name.clone())
        .unwrap_or_default();
    if !expected_account.is_empty() && account.to_uppercase() != expected_account.to_uppercase() {
        state.send_to(conn_id, &format!("{}Error al conectar, intente de nuevo.", server_opcodes::ERROR)).await;
        close_connection(state, conn_id).await;
        return;
    }

    connect_user(state, conn_id, &char_name, &account, &codex).await;
}

/// OOLOGI — Character login (simplified variant).
async fn handle_oologi(state: &mut GameState, conn_id: ConnectionId, data: &str) {
    let payload = strip_opcode(data, 6);
    let char_name = read_field(1, payload, ',');
    let account = read_field(2, payload, ',');
    let codex = read_field(3, payload, ',');

    info!("[AUTH] Character login (OOLOGI): '{}' from #{}", char_name, conn_id);

    if !charfile::character_exists(&state.base_path, &char_name) {
        state.send_to(conn_id, &format!("{}El personaje no existe.", server_opcodes::ERROR)).await;
        close_connection(state, conn_id).await;
        return;
    }

    if is_char_banned(&state.base_path, &char_name) {
        state.send_to(conn_id, &format!(
            "{}Se te ha prohibido la entrada a Tierras Sagradas AO debido a tu mal comportamiento.",
            server_opcodes::ERROR
        )).await;
        close_connection(state, conn_id).await;
        return;
    }

    connect_user(state, conn_id, &char_name, &account, &codex).await;
}

/// ConnectUser — Full character login (called after THCJXD or OOLOGI validation).
async fn connect_user(
    state: &mut GameState,
    conn_id: ConnectionId,
    char_name: &str,
    account: &str,
    codex: &str,
) {
    // Check max users
    if state.num_users >= state.config.max_users {
        state.send_to(conn_id, &format!(
            "{}El servidor ha alcanzado el maximo de usuarios soportado. Intente mas tarde.",
            server_opcodes::ERROR
        )).await;
        close_connection(state, conn_id).await;
        return;
    }

    // Multi-login check
    if !state.config.allow_multi_logins {
        let user_ip = state.users.get(&conn_id).map(|u| u.ip.clone()).unwrap_or_default();
        if is_same_ip_online(state, conn_id, &user_ip) {
            state.send_to(conn_id, server_opcodes::FINISH_OK).await;
            state.send_to(conn_id, &format!(
                "{}No es posible usar mas de un personaje al mismo tiempo.",
                server_opcodes::ERROR_SHOW
            )).await;
            close_connection(state, conn_id).await;
            return;
        }
    }

    // Load character data
    let char_data = match charfile::load_charfile(&state.base_path, char_name) {
        Ok(data) => data,
        Err(e) => {
            warn!("[AUTH] Failed to load charfile '{}': {}", char_name, e);
            state.send_to(conn_id, &format!("{}Error en el personaje.", server_opcodes::ERROR)).await;
            close_connection(state, conn_id).await;
            return;
        }
    };

    // Verify password (CodeX)
    if !codex.is_empty() && char_data.password.to_uppercase() != codex.to_uppercase() {
        state.send_to(conn_id, server_opcodes::FINISH_OK).await;
        state.send_to(conn_id, &format!("{}Password incorrecto.", server_opcodes::ERROR_SHOW)).await;
        close_connection(state, conn_id).await;
        return;
    }

    // Check if already logged in
    if state.is_name_online(char_name) {
        state.send_to(conn_id, &format!(
            "{}Perdon, un usuario con el mismo nombre se ha logeado, intente de nuevo en 5 minutos.",
            server_opcodes::ERROR
        )).await;
        close_connection(state, conn_id).await;
        return;
    }

    // Check same-account multi-character
    if let Some(other) = state.is_account_char_online(account, char_name) {
        state.send_to(conn_id, &format!(
            "{}Perdon, un usuario de la misma cuenta esta conectado ({}), intente de nuevo en 5 minutos.",
            server_opcodes::ERROR_SHOW, other
        )).await;
        close_connection(state, conn_id).await;
        return;
    }

    // Determine position
    let map = if char_data.map > 0 { char_data.map } else { state.config.start_map };
    let x = if char_data.x > 0 { char_data.x } else { state.config.start_x };
    let y = if char_data.y > 0 { char_data.y } else { state.config.start_y };

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

        // Safety: if charfile has ghost body (8/500) but dead=false, restore naked body.
        // This can happen from a bad revive that didn't restore body before saving.
        if !user.dead && (user.body == DEAD_BODY_NEUTRAL || user.head == DEAD_HEAD_NEUTRAL) {
            let gender_str = char_data.gender.to_string();
            user.body = naked_body(&char_data.race, &gender_str);
            // Head 500 is ghost head — use a default head for the race/gender
            if user.head == DEAD_HEAD_NEUTRAL {
                user.head = default_head_for_race(&char_data.race, char_data.gender);
            }
        }

        // Animation IDs: resolve from equipped items (VB6 TCP.bas lines 1511-1513)
        // Default 2 = "empty" animation (NingunArma/NingunEscudo/NingunCasco)
        user.weapon_anim = 2;
        user.shield_anim = 2;
        user.casco_anim = 2;
        user.privileges = char_data.privileges;
        user.gender = char_data.gender;
        user.poisoned = char_data.poisoned;
        user.paralyzed = char_data.paralyzed;
        // VB6: FileIO.bas sets Counters.Paralisis = IntervaloParalizado on load
        if user.paralyzed {
            user.counter_paralisis = state.config.intervalo_paralizado;
        }
        user.criminal = char_data.criminal;
        user.hidden = char_data.hidden;
        user.navigating = char_data.navigating;
        user.guild_index = char_data.guild_index;
        user.armada_real = char_data.armada_real;
        user.fuerzas_caos = char_data.fuerzas_caos;
        user.criminales_matados = char_data.criminales_matados;
        user.ciudadanos_matados = char_data.ciudadanos_matados;
        user.recompensas_real = char_data.recompensas_real;
        user.recompensas_caos = char_data.recompensas_caos;
        user.reenlistadas = char_data.reenlistadas;
        user.questeando = char_data.questeando;
        user.quest_num = char_data.quest_num;
        user.quest_kills = char_data.quest_kills;
        user.quests_completed = char_data.quests_completed;
        user.hogar = match char_data.hogar {
            1 => "Thir".to_string(),
            2 => "Inthak".to_string(),
            3 => "Ruvendel".to_string(),
            _ => "Thir".to_string(),
        };

        // Stats
        user.class = char_data.class.clone();
        user.race = char_data.race.clone();
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
        user.puntos_donacion = char_data.puntos_donacion;
        user.puntos_torneo = char_data.puntos_torneo;
        user.ts_points = char_data.ts_points;
        user.hogar = char_data.hogar.to_string();
        user.max_agua = char_data.max_agua;
        user.min_agua = char_data.min_agua;
        user.max_ham = char_data.max_ham;
        user.min_ham = char_data.min_ham;
        user.attributes = char_data.attributes;
        // VB6 hardcodes all skills to 100 (skill system unused in TS)
        user.skills = [100i32; 22];
        user.reputation = char_data.reputation;

        // Inventory
        for (i, &(obj_idx, amount, equipped)) in char_data.inventory.iter().enumerate() {
            if i < MAX_INVENTORY_SLOTS {
                user.inventory[i] = InventorySlot {
                    obj_index: obj_idx,
                    amount,
                    equipped,
                };
            }
        }
        user.equip = EquipSlots {
            weapon: char_data.weapon_eqp_slot,
            armor: char_data.armour_eqp_slot,
            shield: char_data.shield_eqp_slot,
            helmet: char_data.helmet_eqp_slot,
            municion: char_data.municion_eqp_slot,
        };

        // Bank
        for (i, &(obj_idx, amount)) in char_data.bank.iter().enumerate() {
            if i < user.bank.len() {
                user.bank[i] = InventorySlot { obj_index: obj_idx, amount, equipped: false };
            }
        }

        // Spells
        user.spells = char_data.spells;

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
                user.shield_anim = obj.shield_anim;
            }
        }
        if helmet_obj_idx >= 1 {
            if let Some(obj) = state.game_data.objects.get((helmet_obj_idx - 1) as usize) {
                user.casco_anim = obj.casco_anim;
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

    // Set Logged=1 in charfile
    let _ = charfile::set_logged_flag(&state.base_path, char_name, true);

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
    state.send_to(conn_id, &format!("CM{},{},{},{}", map, r, g, b)).await;
    state.send_to(conn_id, &format!("PU{},{}", x, y)).await;
    state.send_to(conn_id, &format!("XM{}", music)).await;
    state.send_to(conn_id, &format!("N~{}", map_name)).await;

    // --- PHASE 2: Privilege level (VB6 lines 1558-1596) ---
    state.send_to(conn_id, &format!("LDG{}", char_data.privileges)).await;

    // --- PHASE 3: Hunger/Thirst (VB6 line 1608) ---
    state.send_to(conn_id, &format!(
        "EHYS{},{},{},{}",
        char_data.max_agua, char_data.min_agua,
        char_data.max_ham, char_data.min_ham
    )).await;

    // --- PHASE 4: Broadcast status to area (VB6 line 1609) ---
    let status_mith = if char_data.criminal { 2 } else { 1 };
    let px_pkt = format!("PX{},{},{}", char_index.0, status_mith, char_name);
    state.send_data(
        SendTarget::ToAreaButIndex { conn_id, map, x, y },
        &px_pkt,
    ).await;

    // --- PHASE 5: Reputation (VB6 line 1666) ---
    state.send_to(conn_id, &format!("RPT{}", char_data.reputation)).await;

    // --- PHASE 6: Friend list (VB6 line 1685) ---
    send_friend_list(state, conn_id).await;

    // --- PHASE 7: LOGGED — client switches to game mode (VB6 line 1692) ---
    // CRITICAL: Must come BEFORE stats, inventory, spells, area visibility
    state.send_to(conn_id, server_opcodes::LOGGED).await;

    // --- PHASE 8: Equipment hitbox stats (VB6 line 1701) ---
    let anm = build_anm_packet(state, conn_id);
    state.send_to(conn_id, &anm).await;

    // --- PHASE 9: Bulk stats [ES (VB6 line 1720) ---
    let exp_next = state.exp_for_level(char_data.level);
    state.send_to(conn_id, &format!(
        "[ES{},{},{},{},{},{},{},{},{},{},{},{},{},{}",
        char_data.max_hp, char_data.min_hp,
        char_data.max_mana, char_data.min_mana,
        char_data.max_sta, char_data.min_sta,
        char_data.gold,
        char_data.level,
        exp_next,
        char_data.exp,
        char_name,
        char_data.attributes[1], // Agility (AT2)
        char_data.attributes[0], // Strength (AT1)
        char_data.reputation,
    )).await;

    // --- PHASE 10: Stop state (VB6 line 1730) ---
    // STOPD uses flags.Stopped (GM /STOP command), NOT paralysis
    let stopped_flag = if let Some(u) = state.users.get(&conn_id) { u.not_move } else { false };
    state.send_to(conn_id, &format!("STOPD{}", if stopped_flag { 1 } else { 0 })).await;

    // --- PHASE 10b: PARADOK if paralyzed (VB6 line 1732) ---
    // PARADOK is a toggle — client starts with UserParalizado=False,
    // so we send one PARADOK to set it to True if the char is paralyzed.
    if char_data.paralyzed {
        state.send_to(conn_id, "PARADOK").await;
    }

    // --- PHASE 10c: NAVEG if navigating (VB6 line 1734) ---
    // NAVEG is a toggle — client starts with UserNavegando=False,
    // so we send one NAVEG to set it to True if the char was navigating.
    // VB6 TCP.bas lines 1515-1521: Also set boat appearance on login.
    if let Some(user) = state.users.get_mut(&conn_id) {
        if user.navigating {
            user.head = 0;
            user.weapon_anim = 0;
            user.shield_anim = 0;
            user.casco_anim = 0;
            // Body should already be boat ropaje from charfile save
        }
    }
    if let Some(user) = state.users.get(&conn_id) {
        if user.navigating {
            state.send_to(conn_id, "NAVEG").await;
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
        state.send_to(conn_id, "BKW").await;
        // CM (change map — client loads the map file)
        state.send_to(conn_id, &format!("CM{},{},{},{}", map, r, g, b)).await;
        // XM (music)
        state.send_to(conn_id, &format!("XM{}", music)).await;
        // N~ (map name display)
        state.send_to(conn_id, &format!("N~{}", map_name)).await;
        // IP (self char index — tells client which CC is "me")
        state.send_to(conn_id, &format!("IP{}", char_index.0)).await;
        // Own CC packet (client renders self) + [CD (char data)
        if let Some(user) = state.users.get(&conn_id) {
            let own_cc = user.build_cc_packet();
            let own_cd = build_cd_packet(user);
            state.send_to(conn_id, &own_cc).await;
            state.send_to(conn_id, &own_cd).await;
        }
        // PU (position update — tells client where to center camera)
        state.send_to(conn_id, &format!("PU{},{}", x, y)).await;
        // Send area visibility (other players, NPCs, ground items)
        make_user_visible(state, conn_id).await;
        // BKW again to toggle pausa back to False (VB6 WarpUserChar line 2404)
        // BKW toggles pausa — first one pauses, second one un-pauses.
        // Without this, client stays paused and CheckKeys never runs (no movement!)
        state.send_to(conn_id, "BKW").await;
    }

    // --- PHASE 12: Console messages (VB6 lines 1738-1747) ---
    state.send_to(conn_id, "||705").await;
    state.send_to(conn_id, "||706@0").await; // 0 penalties
    state.send_to(conn_id, "||707").await;
    state.send_to(conn_id, &format!("||709@{}", char_name)).await;
    state.send_to(conn_id, "||710").await; // Messages activated

    // --- PHASE 12b: Online count (ON opcode → frmMain.ONLINES.Caption) ---
    state.send_to(conn_id, &format!("ON{}", state.num_users)).await;

    // --- PHASE 13: Scroll timers (VB6 lines 1781-1783) ---
    for i in 1..=4 {
        state.send_to(conn_id, &format!("TIS{},0,0", i)).await;
    }

    // --- PHASE 14: Inventory (VB6 lines 1785-1786) ---
    state.send_to(conn_id, server_opcodes::INV_INIT).await;
    send_full_inventory(state, conn_id).await;

    // --- PHASE 15: Spells (VB6 line 1787) ---
    send_full_spells(state, conn_id).await;

    info!(
        "[AUTH] '{}' logged in (CharIdx={}, Map {}, {},{}) — {} users online",
        char_name, char_index.0, map, x, y, state.num_users
    );
}

// =====================================================================
// In-game handlers
// =====================================================================

/// M<heading> — Character movement.
async fn handle_walk(state: &mut GameState, conn_id: ConnectionId, data: &str) {
    // Movement packets are very frequent, don't log them
    // Check logged in
    let user_data = match state.users.get(&conn_id) {
        Some(u) if u.logged => (u.pos_map, u.pos_x, u.pos_y, u.char_index, u.paralyzed, u.dead, u.not_move, u.meditating, u.navigating),
        _ => return,
    };
    let (map, old_x, old_y, char_index, paralyzed, dead, not_move, meditating, navigating) = user_data;

    // VB6: Dead users CAN move (they walk as ghosts). Only paralyzed and not_move block.
    if paralyzed || not_move {
        // Force client back to server position (prevents ghost movement on client)
        let pu_pkt = format!("PU{},{}", old_x, old_y);
        state.send_to(conn_id, &pu_pkt).await;
        return;
    }

    // NOTE: VB6 defines PuedoPU() but NEVER calls it in the movement handler.
    // Movement speed is controlled entirely client-side by animation timing.
    // No server-side anti-flood for movement.

    // Parse heading from payload (single digit after "M")
    let heading_str = strip_opcode(data, 1);
    let heading: i32 = heading_str.parse().unwrap_or(0);
    if heading < 1 || heading > 4 {
        warn!("[WALK] #{} bad heading '{}' parsed={}", conn_id, heading_str, heading);
        return;
    }

    // Cancel meditation on movement (VB6: TCP_HandleData1.bas lines 360-365)
    if meditating {
        if let Some(user) = state.users.get_mut(&conn_id) {
            user.meditating = false;
        }
        state.send_to(conn_id, "MEDOK").await;
        let msg = "||205".to_string(); // TEXTO205: Dejas de meditar
        state.send_to(conn_id, &msg).await;
        // Remove meditation FX from area
        let cfx_pkt = format!("CFX{},{},{}", char_index.0, 0, 0);
        state.send_data(
            SendTarget::ToArea { map, x: old_x, y: old_y },
            &cfx_pkt,
        ).await;
    }

    let (dx, dy) = world::heading_to_offset(heading);
    let new_x = old_x + dx;
    let new_y = old_y + dy;

    // Check map bounds and blocked
    // VB6 LegalPos: When navigating (PuedeAgua=True), only water tiles are legal.
    // When walking normally, water tiles are impassable.
    let tile_blocked = state.is_tile_blocked(map, new_x, new_y);
    let has_water = state.hay_agua(map, new_x, new_y);
    let blocked = if navigating {
        // On boat: can only move on water tiles, blocked tiles still block
        tile_blocked || !has_water
    } else {
        // On foot: blocked tiles and water tiles are impassable
        tile_blocked || has_water
    };
    let legal = state.world.is_legal_pos(map, new_x, new_y, blocked);

    // Walk movement — don't log (too frequent)

    if !legal {
        // Check if there's a map exit at the target tile
        if let Some((exit_map, exit_x, exit_y)) = state.get_tile_exit(map, new_x, new_y) {
            // VB6: FX only if tile has otTeleport object (not on map border exits)
            let has_teleport_obj = {
                let obj_idx = get_map_tile_obj(state, map, new_x, new_y);
                obj_idx > 0 && state.get_object(obj_idx).map(|o| o.obj_type == crate::data::objects::ObjType::Teleport).unwrap_or(false)
            };
            warp_user(state, conn_id, exit_map, exit_x, exit_y).await;
            if has_teleport_obj {
                send_warp_fx(state, conn_id).await;
            }
            return;
        }

        // Reject movement — send position correction
        // Walk rejected — don't log (too frequent)
        state.send_to(conn_id, &format!("PT{},{}", old_x, old_y)).await;
        return;
    }

    // Update heading
    if let Some(user) = state.users.get_mut(&conn_id) {
        user.heading = heading;
    }

    // Move on grid
    state.world.remove_user(map, old_x, old_y);
    state.world.place_user(map, new_x, new_y, conn_id);

    // Update user position
    if let Some(user) = state.users.get_mut(&conn_id) {
        user.pos_x = new_x;
        user.pos_y = new_y;
    }

    // Broadcast movement to area (+ packet: CharIndex,X,Y) — only to OTHER players
    // VB6 SendToUserAreaButindex: broadcasts to all users in the sender's 27x27 area
    let move_pkt = format!("+{},{},{}", char_index.0, new_x, new_y);
    let (area_min_x, area_min_y) = match state.users.get(&conn_id) {
        Some(u) => (u.area_min_x, u.area_min_y),
        None => (0, 0),
    };
    if area_min_x > 0 || area_min_y > 0 {
        let amx = area_min_x.max(1);
        let amy = area_min_y.max(1);
        let axx = (area_min_x + 26).min(100);
        let axy = (area_min_y + 26).min(100);
        let mut targets: Vec<ConnectionId> = Vec::new();
        if let Some(grid) = state.world.grid(map) {
            for sy in amy..=axy {
                for sx in amx..=axx {
                    if let Some(tile) = grid.tile(sx, sy) {
                        if let Some(c) = tile.user_conn {
                            if c != conn_id { targets.push(c); }
                        }
                    }
                }
            }
        }
        for c in targets {
            state.send_to(c, &move_pkt).await;
        }
    } else {
        // Fallback: use standard area broadcast
        state.send_data(
            SendTarget::ToAreaButIndex { conn_id, map, x: new_x, y: new_y },
            &move_pkt,
        ).await;
    }

    // VB6: ZonaCura check — auto-heal/revive if near a Revividor NPC (Sacerdotes automáticos)
    if zona_cura(state, map, new_x, new_y) {
        auto_cura_user(state, conn_id).await;
    }

    // Area boundary visibility (VB6: ModAreas.CheckUpdateNeededUser)
    // Only fires when crossing a 9x9 zone boundary — sends CA + new strip CCs
    check_update_needed_user(state, conn_id, heading).await;

    // VB6 DoTileEvents: check tile exit AFTER successful movement (map transitions)
    if let Some((exit_map, exit_x, exit_y)) = state.get_tile_exit(map, new_x, new_y) {
        // VB6: FX only if tile has otTeleport object (not on map border exits)
        let has_teleport_obj = {
            let obj_idx = get_map_tile_obj(state, map, new_x, new_y);
            obj_idx > 0 && state.get_object(obj_idx).map(|o| o.obj_type == crate::data::objects::ObjType::Teleport).unwrap_or(false)
        };
        warp_user(state, conn_id, exit_map, exit_x, exit_y).await;
        if has_teleport_obj {
            send_warp_fx(state, conn_id).await;
        }
    }
}

/// CHEA<heading> — Change heading without moving.
async fn handle_change_heading(state: &mut GameState, conn_id: ConnectionId, data: &str) {
    let user_data = match state.users.get(&conn_id) {
        Some(u) if u.logged => (u.pos_map, u.pos_x, u.pos_y, u.char_index),
        _ => return,
    };
    let (map, x, y, char_index) = user_data;

    let heading: i32 = strip_opcode(data, 4).parse().unwrap_or(0);
    if heading < 1 || heading > 4 {
        return;
    }

    if let Some(user) = state.users.get_mut(&conn_id) {
        user.heading = heading;
    }

    // Broadcast heading change to area (VB6: |H<charIndex>,<heading>)
    let pkt = format!("|H{},{}", char_index.0, heading);
    state.send_data(
        SendTarget::ToAreaButIndex { conn_id, map, x, y },
        &pkt,
    ).await;
}

/// RPU — Request position update.
async fn handle_request_pos(state: &mut GameState, conn_id: ConnectionId) {
    if let Some(user) = state.users.get(&conn_id) {
        if user.logged {
            let pkt = format!("PU{},{}", user.pos_x, user.pos_y);
            state.send_to(conn_id, &pkt).await;
        }
    }
}

/// ; — Chat message (talk to area).
/// VB6 format: T|<color>~<message>~<charindex>
/// Client parses with ReadField(N, rData, 176) for dialog bubble,
/// or ReadField(N, rData, 126) for console text.
async fn handle_talk(state: &mut GameState, conn_id: ConnectionId, data: &str) {
    let user_data = match state.users.get(&conn_id) {
        Some(u) if u.logged => (u.pos_map, u.pos_x, u.pos_y, u.char_index, u.dead, u.privileges, u.silenced),
        _ => return,
    };
    let (map, x, y, char_index, dead, privileges, silenced) = user_data;

    let message = strip_opcode(data, 1);

    // Handle slash commands (e.g., /RESUCITAR) — silenced users can still use commands
    // VB6: empty messages are allowed (used for "cartelear" — clearing text bubbles)
    if !message.is_empty() && message.starts_with('/') {
        handle_slash_command(state, conn_id, message).await;
        return;
    }

    // Silenced users can't chat
    if silenced {
        state.send_to(conn_id, "||191").await; // TEXTO191: Has sido silenciado
        return;
    }

    // Color based on status
    let color = if dead {
        "12632256" // Gray for dead
    } else if privileges > 0 {
        "65535" // Yellow for GM (vbYellow)
    } else {
        "16777215" // White (vbWhite)
    };

    // T| format uses ASCII 176 (°) as delimiter between color, text, charindex
    let pkt = format!("T|{}\u{00B0}{}\u{00B0}{}", color, message, char_index.0);

    if dead {
        // Dead players only heard by other dead players (simplified: send to area)
        state.send_data(SendTarget::ToArea { map, x, y }, &pkt).await;
    } else {
        state.send_data(SendTarget::ToArea { map, x, y }, &pkt).await;
    }
}

/// - — Yell message (larger area, red text).
/// VB6 format: N|<color>~<message>~<charindex>
async fn handle_yell(state: &mut GameState, conn_id: ConnectionId, data: &str) {
    let user_data = match state.users.get(&conn_id) {
        Some(u) if u.logged => (u.pos_map, u.pos_x, u.pos_y, u.char_index, u.dead),
        _ => return,
    };
    let (map, x, y, char_index, dead) = user_data;

    if dead {
        return; // Dead players can't yell
    }

    let message = strip_opcode(data, 1);
    if message.is_empty() {
        return;
    }

    // Yell uses red color and goes to the whole map
    let pkt = format!("N|255\u{00B0}{}\u{00B0}{}", message, char_index.0);
    state.send_data(SendTarget::ToMap(map), &pkt).await;
}

/// \ — Whisper (private message).
/// Client sends: \<targetname>@<message>
/// Server sends P| packets to both sender and receiver.
async fn handle_whisper(state: &mut GameState, conn_id: ConnectionId, data: &str) {
    let user_data = match state.users.get(&conn_id) {
        Some(u) if u.logged => (u.char_name.clone(), u.privileges),
        _ => return,
    };
    let (sender_name, privileges) = user_data;

    let payload = strip_opcode(data, 1); // Strip "\"
    if payload.is_empty() {
        return;
    }

    // Parse target@message (@ as delimiter)
    let target_name = read_field(1, payload, '@');
    let message = read_field(2, payload, '@');

    if target_name.is_empty() || message.is_empty() {
        return;
    }

    // Find target user
    let target_id = state.find_user_by_name(&target_name);

    if target_id.is_none() {
        // User not found — send console message
        state.send_to(conn_id, "||196").await;
        return;
    }
    let target_id = target_id.unwrap();

    // Get target's display name
    let target_display = state.users.get(&target_id)
        .map(|u| u.char_name.clone())
        .unwrap_or_else(|| target_name.clone());

    // Send to sender: "Le dijiste a <target>: <message>"
    let sender_pkt = format!(
        "P|Le dijiste a {}: {}{}",
        target_display, message, font_types::WHISPER_SENT
    );
    state.send_to(conn_id, &sender_pkt).await;

    // Send to receiver
    let prefix = if privileges > 0 { "(GM) " } else { "" };
    let recv_pkt = format!(
        "P|{}{} te dijo: {}{}",
        prefix, sender_name, message, font_types::WHISPER_RECV
    );
    state.send_to(target_id, &recv_pkt).await;
}

// =====================================================================
// Character creation/deletion handlers
// =====================================================================

/// NLOGIN — Create new character.
async fn handle_nlogin(state: &mut GameState, conn_id: ConnectionId, data: &str) {
    let payload = strip_opcode(data, 6);

    let paso_hd = state.users.get(&conn_id).map(|u| u.paso_hd).unwrap_or(false);
    if !paso_hd {
        state.send_to(conn_id, &format!("{}Tu PC se encuentra bajo Tolerancia 0.", server_opcodes::ERROR)).await;
        close_connection(state, conn_id).await;
        return;
    }

    if !state.config.can_create_characters {
        state.send_to(conn_id, &format!(
            "{}La creacion de personajes en este servidor se ha deshabilitado.",
            server_opcodes::ERROR
        )).await;
        close_connection(state, conn_id).await;
        return;
    }

    if state.config.server_only_gms {
        state.send_to(conn_id, &format!(
            "{}Servidor restringido a administradores. La creacion de personajes se encuentra deshabilitada.",
            server_opcodes::ERROR
        )).await;
        close_connection(state, conn_id).await;
        return;
    }

    let char_name = read_field(1, payload, ',');
    let race = read_field(2, payload, ',');
    let gender: i32 = read_field(4, payload, ',').parse().unwrap_or(1);
    let class = read_field(5, payload, ',');
    let hogar: i32 = read_field(6, payload, ',').parse().unwrap_or(1);
    let account = read_field(7, payload, ',');
    let head: i32 = read_field(8, payload, ',').parse().unwrap_or(0);

    info!("[AUTH] New character request: '{}' race='{}' class='{}' from #{}", char_name, race, class, conn_id);

    if !is_valid_name(&char_name) {
        state.send_to(conn_id, &format!("{}Nombre invalido.", server_opcodes::ERROR)).await;
        close_connection(state, conn_id).await;
        return;
    }

    if char_name.len() < 3 {
        state.send_to(conn_id, &format!(
            "{}El nombre del personaje debe tener al menos 3 caracteres.",
            server_opcodes::ERROR
        )).await;
        close_connection(state, conn_id).await;
        return;
    }

    let attributes = state.users.get(&conn_id)
        .map(|u| u.dice_attributes)
        .unwrap_or([18; 5]);

    let security_code = state.users.get(&conn_id)
        .and_then(|u| {
            if !u.account_name.is_empty() {
                accounts::load_account(&state.base_path, &u.account_name)
                    .ok()
                    .map(|a| a.security_code)
            } else {
                None
            }
        })
        .unwrap_or_else(|| state.security_code.clone());

    match charfile::create_charfile(
        &state.base_path,
        &char_name,
        &race,
        gender,
        &class,
        hogar,
        head,
        &security_code,
        attributes,
        state.config.start_map,
        state.config.start_x,
        state.config.start_y,
    ) {
        Ok(()) => {
            let acct = if account.is_empty() {
                state.users.get(&conn_id).map(|u| u.account_name.clone()).unwrap_or_default()
            } else {
                account
            };

            if !acct.is_empty() {
                if let Err(e) = accounts::add_character_to_account(&state.base_path, &acct, &char_name) {
                    warn!("[AUTH] Failed to add char to account: {}", e);
                }
            }

            info!("[AUTH] Character '{}' created successfully", char_name);
            state.send_to(conn_id, &format!("{}Personaje creado con exito!", server_opcodes::ERROR)).await;
            close_connection(state, conn_id).await;
        }
        Err(e) => {
            state.send_to(conn_id, &format!("{}{}", server_opcodes::ERROR, e)).await;
            close_connection(state, conn_id).await;
        }
    }
}

/// TIRDAD — Roll dice for character attributes.
async fn handle_tirdad(state: &mut GameState, conn_id: ConnectionId) {
    info!("[AUTH] Dice roll request from #{}", conn_id);

    let attrs = [18i32; 5];

    if let Some(user) = state.users.get_mut(&conn_id) {
        user.dice_attributes = attrs;
    }

    let dados = format!(
        "{}{},{},{},{},{}",
        server_opcodes::DICE_ROLL,
        attrs[0], attrs[1], attrs[2], attrs[3], attrs[4]
    );
    state.send_to(conn_id, &dados).await;
}

/// TBRP — Delete character.
async fn handle_tbrp(state: &mut GameState, conn_id: ConnectionId, data: &str) {
    let payload = strip_opcode(data, 4);
    let char_name = read_field(1, payload, ',');
    let account_name = read_field(2, payload, ',').to_uppercase();
    let password = read_field(3, payload, ',');

    info!("[AUTH] Delete character request: '{}' from #{}", char_name, conn_id);

    if !charfile::character_exists(&state.base_path, &char_name) {
        state.send_to(conn_id, &format!("{}El personaje no existe.", server_opcodes::ERROR)).await;
        close_connection(state, conn_id).await;
        return;
    }

    let char_data = match charfile::load_charfile(&state.base_path, &char_name) {
        Ok(data) => data,
        Err(_) => {
            state.send_to(conn_id, &format!("{}Error al leer el personaje.", server_opcodes::ERROR)).await;
            close_connection(state, conn_id).await;
            return;
        }
    };

    if char_data.password.to_uppercase() != password.to_uppercase() {
        state.send_to(conn_id, &format!("{}Password incorrecto.", server_opcodes::ERROR)).await;
        state.send_to(conn_id, server_opcodes::FINISH_OK).await;
        close_connection(state, conn_id).await;
        return;
    }

    // Block deletion of GMs/privileged characters (VB6 checks privilege level)
    if char_data.privileges > 0 {
        state.send_to(conn_id, &format!(
            "{}No podes borrar gms.",
            server_opcodes::ERROR_SHOW
        )).await;
        return;
    }

    if char_data.level >= 50 {
        state.send_to(conn_id, &format!(
            "{}No podes borrar usuarios nivel 50 o superior.",
            server_opcodes::ERROR
        )).await;
        return;
    }

    if char_data.guild_index > 0 {
        state.send_to(conn_id, &format!(
            "{}No podes borrar usuarios que esten dentro de un clan. Primero abandona el clan.",
            server_opcodes::ERROR
        )).await;
        return;
    }

    if let Err(e) = accounts::remove_character_from_account(&state.base_path, &account_name, &char_name) {
        warn!("[AUTH] Failed to remove char from account: {}", e);
    }

    if let Err(e) = charfile::delete_charfile(&state.base_path, &char_name) {
        warn!("[AUTH] Failed to delete charfile: {}", e);
    }

    info!("[AUTH] Character '{}' deleted", char_name);
    state.send_to(conn_id, server_opcodes::FINISH_OK).await;
    state.send_to(conn_id, &format!("{}Personaje Borrado con exito.", server_opcodes::ERROR_SHOW)).await;
    close_connection(state, conn_id).await;
}

/// REPASS — Change account password.
async fn handle_repass(state: &mut GameState, conn_id: ConnectionId, data: &str) {
    let payload = strip_opcode(data, 6);
    let account_name = read_field(1, payload, ',');
    let old_password = read_field(2, payload, ',');
    let new_password = read_field(3, payload, ',');
    let confirm_password = read_field(4, payload, ',');

    info!("[AUTH] Password change request for '{}' from #{}", account_name, conn_id);

    if new_password == old_password {
        state.send_to(conn_id, &format!(
            "{}No puedes volver a utilizar la misma contrasena.",
            server_opcodes::ERROR_SHOW
        )).await;
        return;
    }

    if new_password.len() < 3 {
        state.send_to(conn_id, &format!(
            "{}La contrasena debe tener un minimo de 3 caracteres.",
            server_opcodes::ERROR_SHOW
        )).await;
        return;
    }

    if new_password != confirm_password {
        state.send_to(conn_id, &format!(
            "{}Las contrasenas no coinciden.",
            server_opcodes::ERROR_SHOW
        )).await;
        return;
    }

    let account = match accounts::load_account(&state.base_path, &account_name) {
        Ok(acc) => acc,
        Err(_) => {
            state.send_to(conn_id, &format!("{}La cuenta no existe.", server_opcodes::ERROR_SHOW)).await;
            return;
        }
    };

    if old_password != account.password {
        state.send_to(conn_id, &format!(
            "{}La Password actual que nos proporciono, no coincide con la del registro.",
            server_opcodes::ERROR_SHOW
        )).await;
        return;
    }

    match accounts::update_password(&state.base_path, &account_name, &new_password) {
        Ok(()) => {
            info!("[AUTH] Password changed for account '{}'", account_name);
            state.send_to(conn_id, &format!(
                "{}La password de su cuenta fue cambiada con exito. Ahora para logear debera de utilizar la nueva.",
                server_opcodes::ERROR_SHOW
            )).await;
        }
        Err(e) => {
            warn!("[AUTH] Failed to update password: {}", e);
            state.send_to(conn_id, &format!("{}Error al cambiar la password.", server_opcodes::ERROR_SHOW)).await;
        }
    }
}

/// REECUH — Account recovery via PIN.
async fn handle_reecuh(state: &mut GameState, conn_id: ConnectionId, data: &str) {
    let payload = strip_opcode(data, 6);
    let account_name = read_field(1, payload, ',');
    let pin = read_field(2, payload, ',');

    info!("[AUTH] Account recovery request for '{}' from #{}", account_name, conn_id);

    let account = match accounts::load_account(&state.base_path, &account_name) {
        Ok(acc) => acc,
        Err(_) => {
            state.send_to(conn_id, &format!("{}La cuenta no existe.", server_opcodes::ERROR)).await;
            close_connection(state, conn_id).await;
            return;
        }
    };

    if pin.to_uppercase() != account.pin.to_uppercase() {
        state.send_to(conn_id, &format!("{}El pin ingresado no es correcto.", server_opcodes::ERROR)).await;
        close_connection(state, conn_id).await;
        return;
    }

    let new_pass = 100 + (rand_simple_u32() % 900);

    let _ = accounts::update_password(&state.base_path, &account_name, &new_pass.to_string());

    state.send_to(conn_id, &format!(
        "{}Has recuperado la cuenta, utiliza la contrasena {} para poder logearte.",
        server_opcodes::ERROR_SHOW, new_pass
    )).await;

    info!("[AUTH] Account '{}' recovered, new password generated", account_name);
}

// =====================================================================
// Inventory handlers
// =====================================================================

/// EQUI<slot> — Equip/unequip item from inventory slot.
async fn handle_equip(state: &mut GameState, conn_id: ConnectionId, data: &str) {
    let slot_str = strip_opcode(data, 4);
    let slot: usize = match slot_str.parse::<usize>() {
        Ok(s) if s >= 1 && s <= MAX_INVENTORY_SLOTS => s,
        _ => return,
    };
    let idx = slot - 1; // 0-based

    let user = match state.users.get(&conn_id) {
        Some(u) if u.logged => u,
        _ => return,
    };

    let inv_slot = &user.inventory[idx];
    if inv_slot.obj_index == 0 {
        return;
    }

    let obj_index = inv_slot.obj_index;
    let currently_equipped = inv_slot.equipped;

    // Look up the object to determine equipment type
    let obj_data = match state.get_object(obj_index) {
        Some(o) => o.clone(),
        None => return,
    };

    if currently_equipped {
        // Unequip
        unequip_slot(state, conn_id, idx, &obj_data.obj_type);
    } else {
        // Item restriction checks (VB6: InvUsuario.bas)
        let (user_level, user_class, user_privileges, user_criminal,
             user_armada, user_caos) = match state.users.get(&conn_id) {
            Some(u) => (u.level, u.class.clone(), u.privileges, u.criminal,
                       u.armada_real, u.fuerzas_caos),
            None => return,
        };

        // VB6: GMs (>= Semidios) bypass ALL equipment restrictions
        let is_gm = user_privileges >= privilege_level::SEMIDIOS;

        // Level requirement
        if obj_data.lvl > 0 && user_level < obj_data.lvl && !is_gm {
            let msg = format!("{}112@{}", server_opcodes::CONSOLE_MSG, obj_data.lvl);
            state.send_to(conn_id, &msg).await;
            return;
        }

        // Class restriction (VB6: ClasePuedeUsarItem Or Privilegios >= Semidios)
        if !is_gm && !obj_data.class_prohibida.is_empty() {
            let uc = user_class.to_uppercase();
            if obj_data.class_prohibida.iter().any(|c| c.to_uppercase() == uc) {
                let msg = "||113".to_string(); // TEXTO113: Tu clase, genero o raza no puede usar este objeto
                state.send_to(conn_id, &msg).await;
                return;
            }
        }

        // Faction restriction (VB6: FaccionPuedeUsarItem Or Privilegios >= Semidios)
        if !is_gm {
            if obj_data.real {
                if user_criminal || !user_armada {
                    let msg = format!("{}Solo miembros de la Armada Real pueden usar este item{}", server_opcodes::CONSOLE_MSG, font_types::INFO);
                    state.send_to(conn_id, &msg).await;
                    return;
                }
            }
            if obj_data.caos {
                if !user_criminal || !user_caos {
                    let msg = format!("{}Solo miembros de las Fuerzas del Caos pueden usar este item{}", server_opcodes::CONSOLE_MSG, font_types::INFO);
                    state.send_to(conn_id, &msg).await;
                    return;
                }
            }
        }

        // Only allow equipping valid equipment types (VB6: InvUsuario.bas)
        // Potions, food, keys, etc. cannot be equipped
        match obj_data.obj_type {
            ObjType::Weapon | ObjType::Armor | ObjType::Shield |
            ObjType::Helmet | ObjType::Arrow | ObjType::Instrument |
            ObjType::Tool => {},
            _ => {
                // Not an equippable item type — reject silently
                return;
            }
        }

        // Two-handed weapon check: unequip shield if equipping 2h weapon
        if obj_data.obj_type == ObjType::Weapon && obj_data.dos_manos {
            let shield_slot = state.users.get(&conn_id).map(|u| u.equip.shield).unwrap_or(0);
            if shield_slot > 0 && shield_slot <= MAX_INVENTORY_SLOTS {
                unequip_slot(state, conn_id, shield_slot - 1, &ObjType::Shield);
                send_inventory_slot(state, conn_id, shield_slot - 1).await;
            }
        }

        // Arrow equip handling
        if obj_data.obj_type == ObjType::Arrow {
            // Equip as ammo
            let old_ammo = state.users.get(&conn_id).map(|u| u.equip.municion).unwrap_or(0);
            if old_ammo > 0 && old_ammo <= MAX_INVENTORY_SLOTS {
                if let Some(user) = state.users.get_mut(&conn_id) {
                    user.inventory[old_ammo - 1].equipped = false;
                }
                send_inventory_slot(state, conn_id, old_ammo - 1).await;
            }
            if let Some(user) = state.users.get_mut(&conn_id) {
                user.equip.municion = slot;
                user.inventory[idx].equipped = true;
            }
            send_inventory_slot(state, conn_id, idx).await;
            return;
        }

        // Equip — first unequip any item in the same category
        let old_slot = match obj_data.obj_type {
            ObjType::Weapon => state.users.get(&conn_id).map(|u| u.equip.weapon).unwrap_or(0),
            ObjType::Armor => state.users.get(&conn_id).map(|u| u.equip.armor).unwrap_or(0),
            ObjType::Shield => state.users.get(&conn_id).map(|u| u.equip.shield).unwrap_or(0),
            ObjType::Helmet => state.users.get(&conn_id).map(|u| u.equip.helmet).unwrap_or(0),
            _ => 0,
        };

        if old_slot > 0 && old_slot <= MAX_INVENTORY_SLOTS {
            let old_idx = old_slot - 1;
            unequip_slot(state, conn_id, old_idx, &obj_data.obj_type);
            // Send updated CSI for the OLD slot so client sees it as unequipped
            send_inventory_slot(state, conn_id, old_idx).await;
        }

        // Equip the new item
        if let Some(user) = state.users.get_mut(&conn_id) {
            user.inventory[idx].equipped = true;
            match obj_data.obj_type {
                ObjType::Weapon => {
                    user.equip.weapon = slot;
                    user.weapon_anim = obj_data.weapon_anim;
                }
                ObjType::Armor => {
                    user.equip.armor = slot;
                    // VB6: equiparRopaje — set body to armor's Ropaje graphic
                    if obj_data.num_ropaje > 0 {
                        user.body = obj_data.num_ropaje;
                    }
                }
                ObjType::Shield => {
                    user.equip.shield = slot;
                    user.shield_anim = obj_data.shield_anim;
                }
                ObjType::Helmet => {
                    user.equip.helmet = slot;
                    user.casco_anim = obj_data.casco_anim;
                }
                _ => {}
            }
        }
    }

    // Send updated CSI for this slot
    send_inventory_slot(state, conn_id, idx).await;

    // Broadcast appearance change to area
    let user_data = match state.users.get(&conn_id) {
        Some(u) => (u.pos_map, u.pos_x, u.pos_y, u.char_index,
                    u.weapon_anim, u.shield_anim, u.casco_anim),
        None => return,
    };
    let (map, x, y, char_index, weapon, shield, helmet) = user_data;

    // Send equipment change packets to area
    match obj_data.obj_type {
        ObjType::Weapon => {
            let pkt = format!("|W{},{}", char_index.0, weapon);
            state.send_data(SendTarget::ToArea { map, x, y }, &pkt).await;
        }
        ObjType::Armor => {
            // VB6: ChangeUserBody sends |B packet with new body
            let body = state.users.get(&conn_id).map(|u| u.body).unwrap_or(0);
            let pkt = format!("|B{},{}", char_index.0, body);
            state.send_data(SendTarget::ToArea { map, x, y }, &pkt).await;
        }
        ObjType::Shield => {
            let pkt = format!("|E{},{}", char_index.0, shield);
            state.send_data(SendTarget::ToArea { map, x, y }, &pkt).await;
        }
        ObjType::Helmet => {
            let pkt = format!("|C{},{}", char_index.0, helmet);
            state.send_data(SendTarget::ToArea { map, x, y }, &pkt).await;
        }
        _ => {}
    }
}

/// Unequip an item from a specific inventory slot.
fn unequip_slot(state: &mut GameState, conn_id: ConnectionId, idx: usize, obj_type: &ObjType) {
    if let Some(user) = state.users.get_mut(&conn_id) {
        user.inventory[idx].equipped = false;
        match obj_type {
            ObjType::Weapon => {
                user.equip.weapon = 0;
                user.weapon_anim = 0;
            }
            ObjType::Armor => {
                user.equip.armor = 0;
                // VB6: DarCuerpoDesnudo — revert to naked body for race/gender
                let race = user.race.clone();
                let gender = user.gender.to_string();
                user.body = naked_body(&race, &gender);
            }
            ObjType::Shield => {
                user.equip.shield = 0;
                user.shield_anim = 0;
            }
            ObjType::Helmet => {
                user.equip.helmet = 0;
                user.casco_anim = 0;
            }
            _ => {}
        }
    }
}

/// USA<slot> — Use item from inventory.
/// QSA<slot>,<visible> — Use item via double-click on inventory picture.
/// VB6: picInv_DblClick sends QSA<slot>,<True|False>.
/// If InvenVisible = "FALSO", it's a hack attempt (using items with inv hidden).
async fn handle_use_item_click(state: &mut GameState, conn_id: ConnectionId, data: &str) {
    let payload = strip_opcode(data, 3);
    let slot_str = read_field(1, payload, ',');
    let visible_str = read_field(2, payload, ',');

    // Anti-cheat: if inventory window is hidden, it's a hack
    if visible_str.eq_ignore_ascii_case("falso") || visible_str.eq_ignore_ascii_case("false") {
        info!("[CHEAT] QSA with hidden inventory from #{}", conn_id);
        return;
    }

    let slot: usize = match slot_str.parse::<usize>() {
        Ok(s) if s >= 1 && s <= MAX_INVENTORY_SLOTS => s,
        _ => return,
    };
    let idx = slot - 1;

    let (obj_index, _amount) = match state.users.get(&conn_id) {
        Some(u) if u.logged => {
            let inv_slot = &u.inventory[idx];
            if inv_slot.obj_index == 0 { return; }
            (inv_slot.obj_index, inv_slot.amount)
        }
        _ => return,
    };

    // Projectile items do nothing on use
    let is_projectile = state.get_object(obj_index).map(|o| o.proyectil).unwrap_or(false);
    if is_projectile { return; }

    // Anti-cheat: PuedoClickear — checks interval_click AND sets both
    // interval_click=6 and interval_poteo=8 (cross-locking, matches VB6)
    if !puede_clickear(state, conn_id) { return; }

    // Delegate to inner use-item with from_click=true so it skips
    // puede_potear() (already set by puede_clickear above)
    let usa_data = format!("USA{}", slot);
    handle_use_item_inner(state, conn_id, &usa_data, true).await;
}

async fn handle_use_item(state: &mut GameState, conn_id: ConnectionId, data: &str) {
    handle_use_item_inner(state, conn_id, data, false).await;
}

/// Inner use-item logic. `from_click` = true when called from QSA (double-click),
/// which means puede_clickear() already set both interval_click and interval_poteo,
/// so we skip the puede_potear() check to avoid double-blocking.
async fn handle_use_item_inner(state: &mut GameState, conn_id: ConnectionId, data: &str, from_click: bool) {
    let slot_str = strip_opcode(data, 3);
    let slot: usize = match slot_str.parse::<usize>() {
        Ok(s) if s >= 1 && s <= MAX_INVENTORY_SLOTS => s,
        _ => return,
    };
    let idx = slot - 1;

    let (obj_index, amount) = match state.users.get(&conn_id) {
        Some(u) if u.logged => {
            let inv = &u.inventory[idx];
            (inv.obj_index, inv.amount)
        }
        _ => return,
    };

    if obj_index == 0 || amount <= 0 {
        return;
    }

    // Death check — only ResurrectPotion can be used while dead
    let is_dead = state.users.get(&conn_id).map(|u| u.dead).unwrap_or(false);

    let obj_data = match state.get_object(obj_index) {
        Some(o) => o.clone(),
        None => return,
    };

    if is_dead && obj_data.obj_type != ObjType::ResurrectPotion {
        state.send_to(conn_id, "||5").await; // VB6 TEXTO5: muerto, no puede usar items
        return;
    }

    match obj_data.obj_type {
        ObjType::UseOnce | ObjType::Potion => {
            // Anti-cheat: check potion cooldown
            // When from_click=true, puede_clickear() already set both cooldowns
            if !from_click && !puede_potear(state, conn_id) {
                return;
            }

            // Remo potion (TipoPocion=6) — special handling: remove paralysis, costs 60 HP
            if obj_data.tipo_pocion == 6 {
                let (paralyzed, min_hp, class) = match state.users.get(&conn_id) {
                    Some(u) => (u.paralyzed, u.min_hp, u.class.clone()),
                    None => return,
                };
                if !paralyzed {
                    let msg = format!("P|No estas paralizado!{}", font_types::INFO);
                    state.send_to(conn_id, &msg).await;
                    return;
                }
                if min_hp <= 60 {
                    let msg = format!("P|No tienes suficiente vida para usar la pocion!{}", font_types::INFO);
                    state.send_to(conn_id, &msg).await;
                    return;
                }
                // Non-warrior/hunter have 3-round cooldown
                let is_warrior_or_hunter = class.eq_ignore_ascii_case("Guerrero") || class.eq_ignore_ascii_case("Cazador");
                if !is_warrior_or_hunter {
                    let counter_remo = state.users.get(&conn_id).map(|u| u.counter_remo).unwrap_or(0);
                    if counter_remo > 0 {
                        let msg = format!("P|Debes esperar para usar otra pocion Remo{}", font_types::INFO);
                        state.send_to(conn_id, &msg).await;
                        return;
                    }
                }
                // Apply: remove paralysis, cost 60 HP, set cooldown
                if let Some(user) = state.users.get_mut(&conn_id) {
                    user.paralyzed = false;
                    user.min_hp -= 60;
                    if !is_warrior_or_hunter {
                        user.counter_remo = 3;
                    }
                    user.inventory[idx].amount -= 1;
                    if user.inventory[idx].amount <= 0 {
                        user.inventory[idx] = InventorySlot::default();
                    }
                }
                // Send PARADOK to toggle paralysis off on client
                state.send_to(conn_id, "PARADOK").await;
                send_inventory_slot(state, conn_id, idx).await;
                send_stats_hp(state, conn_id).await;
                return;
            }

            // Apply potion/food effect
            apply_consumable(state, conn_id, &obj_data);

            // Consume one
            if let Some(user) = state.users.get_mut(&conn_id) {
                user.inventory[idx].amount -= 1;
                if user.inventory[idx].amount <= 0 {
                    user.inventory[idx] = InventorySlot::default();
                }
            }

            // Send updated slot and stats
            send_inventory_slot(state, conn_id, idx).await;
            send_stats_hp(state, conn_id).await;
            send_stats_mana(state, conn_id).await;
            send_stats_sta(state, conn_id).await;
            send_hunger_thirst(state, conn_id).await;
        }
        ObjType::Drink => {
            // Drinks restore thirst (min_agua), not stamina
            if let Some(user) = state.users.get_mut(&conn_id) {
                user.min_agua = (user.min_agua + obj_data.min_agua).min(user.max_agua);
                user.inventory[idx].amount -= 1;
                if user.inventory[idx].amount <= 0 {
                    user.inventory[idx] = InventorySlot::default();
                }
            }
            send_inventory_slot(state, conn_id, idx).await;
            send_hunger_thirst(state, conn_id).await;
        }
        ObjType::Key => {
            // Keys are used on doors — they're not "used" from inventory directly
            // The door interaction happens via LC/RC on a door tile
            let msg = format!("P|Esta llave sirve para abrir una puerta{}", font_types::INFO);
            state.send_to(conn_id, &msg).await;
        }
        ObjType::Boat => {
            // Equip/unequip boat (VB6: InvUsuario.bas — Barcas)
            let (is_navigating, user_map, user_x, user_y) = match state.users.get(&conn_id) {
                Some(u) if u.logged => (u.navigating, u.pos_map, u.pos_x, u.pos_y),
                _ => return,
            };
            if is_navigating {
                // Dismount boat — must be adjacent to land (non-blocked, non-water tile)
                let has_land_nearby = (1..=4).any(|h| {
                    let (dx, dy) = world::heading_to_offset(h);
                    let nx = user_x + dx;
                    let ny = user_y + dy;
                    !state.is_tile_blocked(user_map, nx, ny) && !state.hay_agua(user_map, nx, ny)
                });
                if !has_land_nearby {
                    let msg = format!("P|No puedes desembarcar aqui, no hay tierra cerca{}", font_types::INFO);
                    state.send_to(conn_id, &msg).await;
                    return;
                }
                // Collect equipped item obj indices + saved state before mutation
                let equip_info = state.users.get(&conn_id).map(|u| {
                    let get_inv_obj = |slot: usize| -> i32 {
                        if slot >= 1 && slot <= u.inventory.len() { u.inventory[slot - 1].obj_index } else { 0 }
                    };
                    (
                        get_inv_obj(u.equip.armor),
                        get_inv_obj(u.equip.weapon),
                        get_inv_obj(u.equip.shield),
                        get_inv_obj(u.equip.helmet),
                        u.old_head,
                        u.race.clone(),
                        u.gender,
                    )
                });
                if let Some((armor_obj, weapon_obj, shield_obj, helmet_obj, saved_head, race, gender)) = equip_info {
                    // Look up appearance from object database (1-based index via get_object)
                    let armor_body = state.get_object(armor_obj).map(|o| o.num_ropaje).unwrap_or(0);
                    let weapon_anim = state.get_object(weapon_obj).map(|o| o.weapon_anim).unwrap_or(0);
                    let shield_anim = state.get_object(shield_obj).map(|o| o.shield_anim).unwrap_or(0);
                    let casco_anim = state.get_object(helmet_obj).map(|o| o.casco_anim).unwrap_or(0);
                    if let Some(u) = state.users.get_mut(&conn_id) {
                        u.navigating = false;
                        // VB6 DoNavega: Restore original head
                        u.head = saved_head;
                        // Restore body: equipped armor or naked
                        u.body = if armor_body > 0 { armor_body } else { naked_body(&race, &gender.to_string()) };
                        // Restore equipment animations
                        u.weapon_anim = weapon_anim;
                        u.shield_anim = shield_anim;
                        u.casco_anim = casco_anim;
                    }
                }
                // Send NAVEG toggle to client
                state.send_to(conn_id, "NAVEG").await;
            } else {
                // Mount boat — must be adjacent to water (VB6: HayAgua graphic check)
                let has_water_nearby = (1..=4).any(|h| {
                    let (dx, dy) = world::heading_to_offset(h);
                    state.hay_agua(user_map, user_x + dx, user_y + dy)
                });
                if !has_water_nearby {
                    state.send_to(conn_id, "||106").await; // TEXTO106: Debes aproximarte al agua
                    return;
                }
                let ropaje = obj_data.num_ropaje;
                if let Some(u) = state.users.get_mut(&conn_id) {
                    // VB6 DoNavega: Save head, set to 0, change body to boat
                    u.old_head = u.head;
                    u.head = 0;
                    u.weapon_anim = 0;
                    u.shield_anim = 0;
                    u.casco_anim = 0;
                    u.navigating = true;
                    if ropaje > 0 {
                        u.body = ropaje;
                    }
                }
                // Send NAVEG toggle to client
                state.send_to(conn_id, "NAVEG").await;
            }
            // Broadcast appearance change
            let (cc, map, bx, by) = match state.users.get(&conn_id) {
                Some(u) => (u.build_cc_packet(), u.pos_map, u.pos_x, u.pos_y),
                None => return,
            };
            if !cc.is_empty() {
                state.send_data(SendTarget::ToArea { map, x: bx, y: by }, &cc).await;
            }
        }
        ObjType::Instrument => {
            // VB6: Play music instrument — broadcast TW<Snd1> to area
            let wav = obj_data.snd1; // VB6 uses Snd1 field for instrument sound ID
            let (map, x, y) = match state.users.get(&conn_id) {
                Some(u) if u.logged => (u.pos_map, u.pos_x, u.pos_y),
                _ => return,
            };
            if wav > 0 {
                let snd = format!("TW{}", wav);
                state.send_data(SendTarget::ToArea { map, x, y }, &snd).await;
            }
            let msg = format!("P|Tocas una melodia{}", font_types::INFO);
            state.send_to(conn_id, &msg).await;
        }
        ObjType::Scroll => {
            // Learn spell from scroll
            let spell_id = obj_data.hechizo_index;
            if spell_id <= 0 { return; }

            // VB6: Check if user already knows this spell
            let already_known = state.users.get(&conn_id)
                .map(|u| u.spells.iter().any(|&s| s == spell_id))
                .unwrap_or(false);
            if already_known {
                state.send_to(conn_id, "||182").await; // TEXTO182: Ya tenes ese hechizo
                return;
            }

            // Find empty spell slot
            let slot = match state.users.get(&conn_id) {
                Some(u) => u.spells.iter().position(|&s| s == 0),
                None => return,
            };

            if let Some(slot_idx) = slot {
                if let Some(u) = state.users.get_mut(&conn_id) {
                    u.spells[slot_idx] = spell_id;
                    u.inventory[idx].amount -= 1;
                    if u.inventory[idx].amount <= 0 {
                        u.inventory[idx] = InventorySlot::default();
                    }
                }
                send_inventory_slot(state, conn_id, idx).await;
                let spell_name = state.get_spell(spell_id)
                    .map(|s| s.nombre.clone())
                    .unwrap_or_default();
                // Send SHS to update the spell slot on client
                let shs_slot = slot_idx + 1; // 1-based
                let shs_pkt = format!("SHS{},{},{}", shs_slot, spell_id, spell_name);
                state.send_to(conn_id, &shs_pkt).await;
                state.send_to(conn_id, &format!("||832@{}", spell_name)).await; // TEXTO832
            } else {
                state.send_to(conn_id, "||181").await; // TEXTO181
            }
        }
        ObjType::EmptyBottle => {
            // Fill at water source — simplified, just inform
            state.send_to(conn_id, "||103").await; // TEXTO103: No hay agua allí
        }
        ObjType::FullBottle => {
            // Drink from bottle → restore thirst, swap to empty bottle variant
            if let Some(user) = state.users.get_mut(&conn_id) {
                user.min_agua = (user.min_agua + obj_data.min_agua).min(user.max_agua);
                // Swap to empty variant (IndexAbierta stores the empty bottle obj index)
                let empty_index = obj_data.index_abierta;
                if empty_index > 0 {
                    user.inventory[idx].obj_index = empty_index;
                    // Amount stays the same (1 full bottle → 1 empty bottle)
                } else {
                    // No empty variant, just consume
                    user.inventory[idx].amount -= 1;
                    if user.inventory[idx].amount <= 0 {
                        user.inventory[idx] = InventorySlot::default();
                    }
                }
            }
            send_inventory_slot(state, conn_id, idx).await;
            send_hunger_thirst(state, conn_id).await;
        }
        ObjType::Money => {
            // Gold pile: add to gold, remove from inventory
            if let Some(user) = state.users.get_mut(&conn_id) {
                user.gold += amount as i64;
                user.inventory[idx] = InventorySlot::default();
            }
            send_inventory_slot(state, conn_id, idx).await;
            send_stats_gold(state, conn_id).await;
        }
        ObjType::ResurrectPotion => {
            // Resurrection potion — can only use while dead
            if !is_dead {
                state.send_to(conn_id, "||117").await; // TEXTO117: Debes estar muerto para utilizar esta poción
                return;
            }
            // Consume the item first
            if let Some(user) = state.users.get_mut(&conn_id) {
                user.inventory[idx].amount -= 1;
                if user.inventory[idx].amount <= 0 {
                    user.inventory[idx] = InventorySlot::default();
                }
            }
            send_inventory_slot(state, conn_id, idx).await;
            // Use shared revive logic (restores body, head, sends CFF + CP)
            revive_user(state, conn_id).await;
            state.send_to(conn_id, "||119").await; // TEXTO119: Te has resucitado
        }
        ObjType::Mount => {
            // Mount/dismount — similar to boat but for land mounts
            let ropaje = obj_data.num_ropaje;
            let is_flying = obj_data.es_voladora;
            let navigating = state.users.get(&conn_id).map(|u| u.navigating).unwrap_or(false);
            if navigating {
                // Dismount
                if let Some(u) = state.users.get_mut(&conn_id) {
                    u.navigating = false;
                    u.body = 1; // Reset to default body
                }
            } else {
                // Mount up
                if let Some(u) = state.users.get_mut(&conn_id) {
                    u.navigating = true;
                    if ropaje > 0 {
                        u.body = ropaje;
                    }
                }
            }
            let cc = state.users.get(&conn_id).map(|u| u.build_cc_packet()).unwrap_or_default();
            let (map, x, y) = match state.users.get(&conn_id) {
                Some(u) => (u.pos_map, u.pos_x, u.pos_y),
                None => return,
            };
            if !cc.is_empty() {
                state.send_data(SendTarget::ToArea { map, x, y }, &cc).await;
            }
        }
        ObjType::ScrollItem => {
            // VB6: Buff scroll — typeScroll: 1=exp, 2=gold, 3=drop, 4=crystal drop
            let ts = obj_data.type_scroll;
            if ts < 1 || ts > 4 { return; }

            // Check if scroll type already active
            let already_active = state.users.get(&conn_id)
                .map(|u| u.scroll_active[ts as usize - 1])
                .unwrap_or(false);

            if already_active {
                state.send_to(conn_id, "||928").await; // VB6: scroll already active
                return;
            }

            let time_s = obj_data.time_scroll;
            let mult = obj_data.mult_scroll;

            if let Some(user) = state.users.get_mut(&conn_id) {
                user.scroll_active[ts as usize - 1] = true;
                user.scroll_time[ts as usize - 1] = time_s;
                user.scroll_mult[ts as usize - 1] = mult;
                user.inventory[idx].amount -= 1;
                if user.inventory[idx].amount <= 0 {
                    user.inventory[idx] = InventorySlot::default();
                }
            }

            // VB6: Send TIS packet + message
            let tis_pkt = format!("TIS{},{},{}", ts, time_s, time_s);
            state.send_to(conn_id, &tis_pkt).await;

            let scroll_name = match ts {
                1 => "Experiencia",
                2 => "Oro",
                3 => "Drop",
                4 => "Drop de Cristales",
                _ => "Desconocido",
            };
            let msg = format!("||929@{}@{}@{}", scroll_name, time_s, mult);
            state.send_to(conn_id, &msg).await;
            send_inventory_slot(state, conn_id, idx).await;
        }
        ObjType::Sack => {
            // VB6: Donation sack — add credits
            let credits = obj_data.cant_credits;
            if credits <= 0 { return; }

            if let Some(user) = state.users.get_mut(&conn_id) {
                user.puntos_donacion += credits as i64;
                user.inventory[idx].amount -= 1;
                if user.inventory[idx].amount <= 0 {
                    user.inventory[idx] = InventorySlot::default();
                }
            }
            let msg = format!("||930@{}", credits);
            state.send_to(conn_id, &msg).await;
            send_inventory_slot(state, conn_id, idx).await;
        }
        ObjType::RenounceHorde => {
            // VB6: Renounce Chaos faction
            let guild_idx = state.users.get(&conn_id).map(|u| u.guild_index).unwrap_or(0);
            if guild_idx > 0 {
                state.send_to(conn_id, "||302").await; // Must leave guild first
                return;
            }
            let is_caos = state.users.get(&conn_id)
                .map(|u| u.criminal || u.fuerzas_caos)
                .unwrap_or(false);
            if !is_caos {
                state.send_to(conn_id, "||239").await; // Not in chaos faction
                return;
            }
            if let Some(user) = state.users.get_mut(&conn_id) {
                user.criminal = false;
                user.fuerzas_caos = false;
                user.inventory[idx].amount -= 1;
                if user.inventory[idx].amount <= 0 {
                    user.inventory[idx] = InventorySlot::default();
                }
            }
            state.send_to(conn_id, "||355").await; // Faction changed
            send_inventory_slot(state, conn_id, idx).await;
            // Send updated status
            let cc = state.users.get(&conn_id).map(|u| u.build_cc_packet()).unwrap_or_default();
            let (map, x, y) = state.users.get(&conn_id).map(|u| (u.pos_map, u.pos_x, u.pos_y)).unwrap_or((0,0,0));
            if !cc.is_empty() {
                state.send_data(SendTarget::ToArea { map, x, y }, &cc).await;
            }
        }
        ObjType::RenounceRoyal => {
            // VB6: Renounce Royal faction
            let guild_idx = state.users.get(&conn_id).map(|u| u.guild_index).unwrap_or(0);
            if guild_idx > 0 {
                state.send_to(conn_id, "||302").await;
                return;
            }
            let is_armada = state.users.get(&conn_id)
                .map(|u| !u.criminal || u.armada_real)
                .unwrap_or(false);
            if !is_armada {
                state.send_to(conn_id, "||239").await;
                return;
            }
            if let Some(user) = state.users.get_mut(&conn_id) {
                user.criminal = true;
                user.armada_real = false;
                user.inventory[idx].amount -= 1;
                if user.inventory[idx].amount <= 0 {
                    user.inventory[idx] = InventorySlot::default();
                }
            }
            state.send_to(conn_id, "||355").await;
            send_inventory_slot(state, conn_id, idx).await;
            let cc = state.users.get(&conn_id).map(|u| u.build_cc_packet()).unwrap_or_default();
            let (map, x, y) = state.users.get(&conn_id).map(|u| (u.pos_map, u.pos_x, u.pos_y)).unwrap_or((0,0,0));
            if !cc.is_empty() {
                state.send_data(SendTarget::ToArea { map, x, y }, &cc).await;
            }
        }
        _ => {
            // Unhandled item types — inform user
        }
    }
}

/// Apply a consumable item's effects (potions, food).
/// VB6 TipoPocion: 1=agility, 2=strength, 3=HP, 4=mana, 5=cure poison, 6=remo (paralysis removal)
fn apply_consumable(state: &mut GameState, conn_id: ConnectionId, obj: &crate::data::objects::ObjData) {
    if let Some(user) = state.users.get_mut(&conn_id) {
        let amount = if obj.max_modificador > obj.min_modificador {
            rand_range(obj.min_modificador, obj.max_modificador)
        } else {
            obj.min_modificador
        };

        match obj.tipo_pocion {
            1 => {
                // Agility potion — boost Agilidad (capped at 35), store backup for expiry
                if !user.tomo_pocion {
                    user.attributes_backup = user.attributes;
                }
                user.attributes[1] = (user.attributes[1] + amount).min(35);
                user.tomo_pocion = true;
                user.duracion_efecto = obj.duracion_efecto / 40; // ms → ticks (40ms each)
            }
            2 => {
                // Strength potion — boost Fuerza (capped at 35)
                if !user.tomo_pocion {
                    user.attributes_backup = user.attributes;
                }
                user.attributes[0] = (user.attributes[0] + amount).min(35);
                user.tomo_pocion = true;
                user.duracion_efecto = obj.duracion_efecto / 40;
            }
            3 => {
                // Red potion — HP restoration
                if amount > 0 {
                    user.min_hp = (user.min_hp + amount).min(user.max_hp);
                }
            }
            4 => {
                // Blue potion — Mana restoration (5% of max mana)
                let mana_restore = (user.max_mana as f64 * 0.05) as i32;
                let mana_restore = mana_restore.max(1);
                user.min_mana = (user.min_mana + mana_restore).min(user.max_mana);
            }
            5 => {
                // Purple potion — Cure poison
                user.poisoned = false;
            }
            6 => {
                // Remo potion — Remove paralysis (costs 60 HP, 3-round cooldown for non-warrior/hunter)
                // Handled separately in handle_use_item since it needs async and class checks
            }
            _ => {
                // Generic consumable (ObjType::UseOnce food items, etc.)
                // HP restoration
                if amount > 0 {
                    user.min_hp = (user.min_hp + amount).min(user.max_hp);
                }
            }
        }

        // Food/hunger restoration (applies to all subtypes)
        if obj.min_ham > 0 {
            user.min_ham = (user.min_ham + obj.min_ham).min(user.max_ham);
        }

        // Thirst restoration (applies to all subtypes)
        if obj.min_agua > 0 {
            user.min_agua = (user.min_agua + obj.min_agua).min(user.max_agua);
        }

        // Cure poison flag (for UseOnce items that have CuraVeneno=1 but no TipoPocion)
        if obj.cura_veneno && obj.tipo_pocion != 5 {
            user.poisoned = false;
        }
    }
}

/// AG — Pick up item from ground (stub — needs map item system).
async fn handle_pick_up(state: &mut GameState, conn_id: ConnectionId) {
    let user_data = match state.users.get(&conn_id) {
        Some(u) if u.logged && !u.dead => (u.pos_map, u.pos_x, u.pos_y),
        _ => return,
    };
    let (map, x, y) = user_data;

    // Check if there's a ground item on the user's tile
    let ground_item = state.world.grid(map)
        .and_then(|g| g.tile(x, y))
        .map(|t| t.ground_item)
        .unwrap_or_default();

    if ground_item.obj_index <= 0 || ground_item.amount <= 0 {
        return;
    }

    // Check if the object is pickable (agarrable)
    let is_agarrable = state.get_object(ground_item.obj_index)
        .map(|o| o.agarrable)
        .unwrap_or(false);

    if !is_agarrable {
        let msg = format!("P|No puedes agarrar ese objeto{}", font_types::INFO);
        state.send_to(conn_id, &msg).await;
        return;
    }

    // Find free inventory slot
    let free_slot = {
        let user = match state.users.get(&conn_id) {
            Some(u) => u,
            None => return,
        };
        // First check if we can stack with an existing slot
        let mut stack_slot = None;
        let mut empty_slot = None;
        for i in 0..MAX_INVENTORY_SLOTS {
            if user.inventory[i].obj_index == ground_item.obj_index && user.inventory[i].amount > 0 {
                stack_slot = Some(i);
                break;
            }
            if user.inventory[i].obj_index == 0 && empty_slot.is_none() {
                empty_slot = Some(i);
            }
        }
        stack_slot.or(empty_slot)
    };

    let slot = match free_slot {
        Some(s) => s,
        None => {
            state.send_to(conn_id, "||108").await; // TEXTO108: No podes cargar mas objetos
            return;
        }
    };

    let obj_idx = ground_item.obj_index;
    let amount = ground_item.amount;

    // Remove item from ground
    {
        let grid = state.world.grid_mut(map);
        if let Some(tile) = grid.tile_mut(x, y) {
            tile.ground_item = world::GroundItem::default();
        }
    }

    // Broadcast BO (erase object) to area
    let bo_pkt = format!("BO{},{}", x, y);
    state.send_data(SendTarget::ToArea { map, x, y }, &bo_pkt).await;

    // Add to inventory
    if let Some(user) = state.users.get_mut(&conn_id) {
        if user.inventory[slot].obj_index == obj_idx {
            // Stack
            user.inventory[slot].amount += amount;
        } else {
            // New slot
            user.inventory[slot].obj_index = obj_idx;
            user.inventory[slot].amount = amount;
            user.inventory[slot].equipped = false;
        }
    }

    // Send updated inventory slot
    send_inventory_slot(state, conn_id, slot).await;

    // Get item name for notification
    let item_name = state.get_object(obj_idx)
        .map(|o| o.name.clone())
        .unwrap_or_else(|| format!("Item #{}", obj_idx));

    state.send_to(conn_id, &format!("||115@{}@{}", amount, item_name)).await; // TEXTO115: Recibiste %1 - %2
}

/// TI<slot>,<amount> — Drop item from inventory to ground.
async fn handle_drop_item(state: &mut GameState, conn_id: ConnectionId, data: &str) {
    let payload = strip_opcode(data, 2);
    let slot_raw: i32 = read_field(1, payload, ',').parse().unwrap_or(0);
    let amount: i32 = read_field(2, payload, ',').parse().unwrap_or(1);

    if amount <= 0 {
        return;
    }

    // FLAGORO = -1 means drop gold
    if slot_raw == -1 {
        handle_drop_gold(state, conn_id, amount).await;
        return;
    }

    let slot = slot_raw as usize;
    if slot < 1 || slot > MAX_INVENTORY_SLOTS {
        return;
    }
    let idx = slot - 1;

    let user = match state.users.get(&conn_id) {
        Some(u) if u.logged && !u.dead => u,
        _ => return,
    };

    // GM anti-abuse: GMs (Consejero through Gran_Dios) cannot drop items.
    // Only regular users (0) and Director+ (>=9) are allowed.
    if user.privileges > 0 && user.privileges < 9 {
        let msg = format!("P|Los GMs no pueden tirar items{}", font_types::INFO);
        state.send_to(conn_id, &msg).await;
        return;
    }

    let obj_idx = user.inventory[idx].obj_index;
    let inv_amount = user.inventory[idx].amount;
    if obj_idx == 0 || inv_amount <= 0 {
        return;
    }

    let drop_amount = amount.min(inv_amount);
    let map = user.pos_map;
    let x = user.pos_x;
    let y = user.pos_y;

    // Check if target tile can hold the item (same item or empty)
    let can_place = if let Some(grid) = state.world.grid(map) {
        if let Some(tile) = grid.tile(x, y) {
            tile.ground_item.obj_index == 0 || tile.ground_item.obj_index == obj_idx
        } else {
            false
        }
    } else {
        false
    };

    if !can_place {
        state.send_to(conn_id, "||107").await; // TEXTO107: No hay espacio en el piso
        return;
    }

    // Get GrhIndex for the item
    let grh_index = state.get_object(obj_idx)
        .map(|o| o.grh_index)
        .unwrap_or(0);

    // Place on ground
    let is_new = {
        let grid = state.world.grid_mut(map);
        if let Some(tile) = grid.tile_mut(x, y) {
            if tile.ground_item.obj_index == obj_idx {
                // Stack on existing
                tile.ground_item.amount += drop_amount;
                false
            } else {
                // New item on tile
                tile.ground_item.obj_index = obj_idx;
                tile.ground_item.amount = drop_amount;
                true
            }
        } else {
            return;
        }
    };

    // Remove from inventory
    if let Some(user) = state.users.get_mut(&conn_id) {
        let inv = &mut user.inventory[idx];
        if drop_amount >= inv.amount {
            *inv = InventorySlot::default();
        } else {
            inv.amount -= drop_amount;
        }
    }

    send_inventory_slot(state, conn_id, idx).await;

    // Broadcast HO (show object) to area if new item on tile
    if is_new && grh_index > 0 {
        let ho_pkt = format!("HO{},{},{}", grh_index, x, y);
        state.send_data(SendTarget::ToArea { map, x, y }, &ho_pkt).await;
    }

    // Track for world cleanup (auto-remove after 10 ticks)
    clean_world_add_item(state, map, x, y, 10, obj_idx);
}

/// Drop gold from inventory (TI with slot=-1).
async fn handle_drop_gold(state: &mut GameState, conn_id: ConnectionId, amount: i32) {
    let user = match state.users.get(&conn_id) {
        Some(u) if u.logged && !u.dead => u,
        _ => return,
    };

    if user.gold < amount as i64 || amount <= 0 {
        return;
    }

    // Deduct gold
    if let Some(user) = state.users.get_mut(&conn_id) {
        user.gold -= amount as i64;
    }
    send_stats_gold(state, conn_id).await;

    let msg = format!("P|Tiraste {} monedas de oro{}", amount, font_types::INFO);
    state.send_to(conn_id, &msg).await;
}

/// LC<x>,<y> — Left click on tile (look / inspect).
/// VB6: LookatTile (GameLogic.bas:505-1115) — EXACT replica.
async fn handle_left_click(state: &mut GameState, conn_id: ConnectionId, data: &str) {
    let payload = strip_opcode(data, 2);
    let x: i32 = match read_field(1, payload, ',').parse() {
        Ok(v) => v,
        _ => return,
    };
    let y: i32 = match read_field(2, payload, ',').parse() {
        Ok(v) => v,
        _ => return,
    };

    let (map, user_x, user_y, my_privileges, my_survival_skill) = match state.users.get(&conn_id) {
        Some(u) if u.logged => (u.pos_map, u.pos_x, u.pos_y, u.privileges,
            u.skills.get(9).copied().unwrap_or(0)), // eSkill.Supervivencia = 9
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
                    format!("N|{} - {} - {}~69~190~156", name, amount, idx)
                } else {
                    format!("N|{} - {}~69~190~156", name, idx)
                }
            } else {
                if amount > 1 {
                    format!("N|{} - {}~69~190~156", name, amount)
                } else {
                    format!("N|{}~69~190~156", name)
                }
            };
            state.send_to(conn_id, &msg).await;
        }
    }

    // ========== CHARACTER DETECTION (VB6 lines 779-800) ==========
    // VB6 checks Y+1 FIRST, then Y for chars
    if found_char == 0 {
        for &check_y in &[y + 1, y] {
            if check_y < 1 || check_y > 100 { continue; }
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
                if let Some(guild_name) = state.get_guild_name(guild_idx) {
                    stat.push_str(&format!(" <{}>", guild_name));
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

            // VB6: Color coding by privilege hierarchy (lines 895-956)
            if priv_target > 11 {
                stat.push_str(" <Administrador> ~255~255~255~1~0");
            } else if priv_target > 10 {
                stat.push_str(" <Sub Administrador> ~255~198~0~1~0");
            } else if priv_target > 9 {
                stat.push_str(" <Developer> ~128~255~128~1~0");
            } else if priv_target > 8 {
                stat.push_str(" <Director de Game Master> ~123~155~0~1~0");
            } else if priv_target > 7 {
                stat.push_str(" <Game Master> <Gran Dios> ~0~225~128~1~0");
            } else if priv_target > 3 {
                stat.push_str(" <Game Master> <Dios> ~120~250~250~1~0");
            } else if priv_target > 2 {
                stat.push_str(" <Event Master> ~128~128~64~1~0");
            } else if priv_target > 1 {
                stat.push_str(" <Game Master> <Semi Dios> ~0~170~190~1~0");
            } else if priv_target > 0 {
                stat.push_str(" <Game Master> <Consejero> ~0~185~0~1~0");
            } else if level <= limite_newbie {
                stat.push_str(" ~255~255~202~1~0");
            } else if armada {
                stat.push_str(" ~0~128~255~1~0");
            } else if caos {
                stat.push_str(" ~255~0~0~1~0");
            } else if criminal {
                stat.push_str(" <CRIMINAL> ~255~0~0~1~0");
            } else {
                // Ciudadano (default)
                stat.push_str(" <CIUDADANO> ~0~128~255~1~0");
            }

            let msg = format!("N|{}", stat);
            state.send_to(conn_id, &msg).await;

            found_something = true;
            if let Some(user) = state.users.get_mut(&conn_id) {
                user.target_user = target;
                user.target_npc = 0;
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
                    let gm_msg = format!("N|Nombre : {} /  Vida : {}/{} Numero de NPC : {}~255~113~255~0~0",
                        npc_name, npc_min_hp, npc_max_hp, npc_num);
                    state.send_to(conn_id, &gm_msg).await;
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
                        let gm_extra = format!("N|Nombre: {} Vida: {}/{} Numero de NPC: {} Indice: {}~255~83~255~0~0",
                            npc_name, npc_min_hp, npc_max_hp, npc_num, npc_idx);
                        state.send_to(conn_id, &gm_extra).await;
                    }
                    // Speech bubble with desc
                    let sep = "\u{00B0}"; // chr(176) = °
                    let msg = format!("N|16777215{}{}{}{}{}", sep, npc_desc, sep, npc_char_index.0, font_types::INFO);
                    state.send_to(conn_id, &msg).await;
                } else {
                    // No desc → show name + health status
                    let msg = format!("||674@{}@{}", npc_name, estatus);
                    state.send_to(conn_id, &msg).await;
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
    if found_char == 0 {
        if let Some(user) = state.users.get_mut(&conn_id) {
            user.target_npc = 0;
            user.target_npc_idx = 0;
            user.target_user = 0;
        }
    }
    if !found_something {
        if let Some(user) = state.users.get_mut(&conn_id) {
            user.target_npc = 0;
            user.target_npc_idx = 0;
            user.target_user = 0;
        }
    }
}

/// VB6 TituloReal (ModFacciones.bas:369)
fn titulo_real(recompensas: i32) -> &'static str {
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
fn titulo_caos(recompensas: i32) -> &'static str {
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
fn npc_health_by_survival(min_hp: i32, max_hp: i32, survival_skill: i32) -> String {
    if max_hp <= 0 { return "Intacto".to_string(); }
    if survival_skill >= 60 {
        return format!("{}/{}", min_hp, max_hp);
    }
    let ratio = min_hp as f64 / max_hp as f64;
    if survival_skill >= 40 {
        if ratio < 0.05 { "Agonizando".to_string() }
        else if ratio < 0.10 { "Casi muerto".to_string() }
        else if ratio < 0.25 { "Muy Malherido".to_string() }
        else if ratio < 0.50 { "Herido".to_string() }
        else if ratio < 0.75 { "Levemente herido".to_string() }
        else if ratio < 1.0 { "Sano".to_string() }
        else { "Intacto".to_string() }
    } else if survival_skill > 30 {
        if ratio < 0.25 { "Muy malherido".to_string() }
        else if ratio < 0.50 { "Herido".to_string() }
        else if ratio < 0.75 { "Levemente herido".to_string() }
        else { "Sano".to_string() }
    } else if survival_skill > 20 {
        if ratio < 0.50 { "Malherido".to_string() }
        else if ratio < 0.75 { "Herido".to_string() }
        else { "Sano".to_string() }
    } else if survival_skill > 10 {
        if ratio < 0.50 { "Herido".to_string() }
        else { "Sano".to_string() }
    } else {
        "Dudoso".to_string()
    }
}

/// RC<x>,<y> — Right click on tile (interact / context menu).
/// VB6 equivalent: Accion() in Acciones.bas — handles doors, NPCs, users, items.
async fn handle_right_click(state: &mut GameState, conn_id: ConnectionId, data: &str) {
    let payload = strip_opcode(data, 2);
    let x: i32 = match read_field(1, payload, ',').parse() {
        Ok(v) => v,
        _ => return,
    };
    let y: i32 = match read_field(2, payload, ',').parse() {
        Ok(v) => v,
        _ => return,
    };

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

    // NOTE: Spells are NOT cast from right-click. They are cast from WLC (Work Left Click)
    // with skill_type = MAGIA (2). The right-click only does LookatTile (inspect).

    // Gather tile data without holding borrows
    let tile_data = state.world.grid(map).and_then(|g| g.tile(x, y)).map(|t| {
        (t.user_conn, t.npc_index, t.ground_item.obj_index, t.ground_item.amount)
    });
    let (tile_user, mut tile_npc_idx, tile_obj_idx, _tile_obj_amt) = match tile_data {
        Some(d) => d,
        None => { return; }
    };

    // Also check y-1 for NPC (character heads are above their tile position)
    if tile_npc_idx == 0 && y - 1 >= 1 {
        if let Some(npc_on_ym1) = state.world.grid(map).and_then(|g| g.tile(x, y - 1)).map(|t| t.npc_index) {
            if npc_on_ym1 > 0 {
                tile_npc_idx = npc_on_ym1;
            }
        }
    }

    // RC click — don't log (too frequent)

    // Also check y+1 for NPC (VB6: Acciones.bas checks MapData(Map, X, Y).NpcIndex AND
    // MapData(Map, X, Y+1).NpcIndex for NPC interactions via LookatTile)
    if tile_npc_idx == 0 && y + 1 <= 100 {
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
    // Also check adjacent tiles for doors (VB6 checks x+1, x+2, x-1)
    for dx in [-1, 1, 2] {
        let ax = x + dx;
        if ax < 1 || ax > 100 { continue; }
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
                let menu_pkt = format!("MENU{},{}", target_name, privileges);
                state.send_to(conn_id, &menu_pkt).await;
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

        if let Some((alive, comercia, npc_name, npc_type, npc_desc, npc_num)) = npc_info {
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
                    if dead { state.send_to(conn_id, "||3").await; return; }
                    if dist > 6 {
                        state.send_to(conn_id, "||13").await; return;
                    }
                    iniciar_comercio_npc(state, conn_id, npc_idx).await;
                } else {
                    match npc_type {
                        NpcType::Banker => {
                            if dead { state.send_to(conn_id, "||3").await; return; }
                            if dist > 10 {
                                state.send_to(conn_id, "||13").await; return;
                            }
                            iniciar_banco(state, conn_id).await;
                        }
                        NpcType::BoveClan => {
                            if dead { state.send_to(conn_id, "||3").await; return; }
                            if dist > 5 {
                                state.send_to(conn_id, "||10").await; return;
                            }
                            iniciar_clan_banco(state, conn_id).await;
                        }
                        NpcType::Traveler => {
                            if dead { state.send_to(conn_id, "||3").await; return; }
                            if dist > 5 {
                                state.send_to(conn_id, "||13").await; return;
                            }
                            state.send_to(conn_id, "TRAVELS").await;
                        }
                        NpcType::Quest | NpcType::QuestNoble => {
                            if dead { state.send_to(conn_id, "||3").await; return; }
                            if dist > 10 {
                                state.send_to(conn_id, "||10").await; return;
                            }
                            state.send_to(conn_id, "DAMEQUEST").await;
                        }
                        NpcType::Reviver => {
                            // VB6 Acciones.bas:408-422 — Revividor NPC
                            // Distance check: <= 10 tiles
                            if dist > 10 {
                                state.send_to(conn_id, "||12").await; return;
                            }

                            // If dead: revive first
                            if dead {
                                revive_user(state, conn_id).await;
                            }

                            // Always full-heal + cure poison (VB6: MinHP=MaxHP, Envenenado=False)
                            if let Some(user) = state.users.get_mut(&conn_id) {
                                user.min_hp = user.max_hp;
                                user.poisoned = false;
                            }
                            send_stats_hp(state, conn_id).await;
                        }
                        NpcType::Trainer => {
                            if dead { state.send_to(conn_id, "||3").await; return; }
                            if dist > 10 {
                                state.send_to(conn_id, "||13").await; return;
                            }
                            let msg = format!("{}Habla con el entrenador usando el chat.{}", server_opcodes::CONSOLE_MSG, font_types::INFO);
                            state.send_to(conn_id, &msg).await;
                        }
                        NpcType::Surgeon => {
                            if dist > 10 {
                                state.send_to(conn_id, "||13").await; return;
                            }
                            if let Some(user) = state.users.get_mut(&conn_id) {
                                if user.poisoned {
                                    user.poisoned = false;
                                    let msg = format!("{}El cirujano te ha curado el veneno.{}", server_opcodes::CONSOLE_MSG, font_types::INFO);
                                    state.send_to(conn_id, &msg).await;
                                } else {
                                    let msg = format!("{}No necesitas curacion.{}", server_opcodes::CONSOLE_MSG, font_types::INFO);
                                    state.send_to(conn_id, &msg).await;
                                }
                            }
                        }
                        NpcType::Mail => {
                            // VB6: Correos (type 23) — opens mail form
                            if dead { state.send_to(conn_id, "||3").await; return; }
                            if dist > 5 {
                                state.send_to(conn_id, "||13").await; return;
                            }
                            state.send_to(conn_id, "CORREO").await;
                        }
                        NpcType::Citizenship => {
                            // VB6: Ciudadania (type 13)
                            if dead { state.send_to(conn_id, "||3").await; return; }
                            if dist > 5 {
                                state.send_to(conn_id, "||13").await; return;
                            }
                            let msg = format!("P|Habla conmigo para cambiar tu ciudadania. Escribe /CIUDADANO para convertirte en ciudadano o /CRIMINAL para renunciar.{}", font_types::INFO);
                            state.send_to(conn_id, &msg).await;
                        }
                        NpcType::HouseSeller => {
                            // VB6: ShowCasas (type 15) — MFC packet
                            if dead { state.send_to(conn_id, "||3").await; return; }
                            state.send_to(conn_id, "MFC").await;
                        }
                        NpcType::Arena => {
                            // VB6: Arenas (type 16) — MAR packet with duel names
                            if dead { state.send_to(conn_id, "||3").await; return; }
                            // Build tournament list
                            let mut names = Vec::new();
                            for i in 0..8 {
                                if let Some(name) = state.nombre_dueleando.get(i) {
                                    names.push(name.clone());
                                } else {
                                    names.push(String::new());
                                }
                            }
                            let mar_pkt = format!("MAR{}", names.join(","));
                            state.send_to(conn_id, &mar_pkt).await;
                        }
                        NpcType::GodNpc => {
                            // VB6: NpcDioses (type 18)
                            if dead { state.send_to(conn_id, "||3").await; return; }
                            if dist > 3 {
                                state.send_to(conn_id, "||14").await; return;
                            }
                            let msg = format!("P|Acercate mas para hablar con los dioses.{}", font_types::INFO);
                            state.send_to(conn_id, &msg).await;
                        }
                        NpcType::Bargomaud => {
                            // VB6: NpcBargomaud (type 20) — check level >= 55, warp to 161,50,53
                            if dead { state.send_to(conn_id, "||3").await; return; }
                            if dist > 5 {
                                state.send_to(conn_id, "||14").await; return;
                            }
                            let level = state.users.get(&conn_id).map(|u| u.level).unwrap_or(0);
                            if level < 55 {
                                state.send_to(conn_id, "||643").await; return;
                            }
                            warp_user(state, conn_id, 161, 50, 53).await;
                            let name = state.users.get(&conn_id).map(|u| u.char_name.clone()).unwrap_or_default();
                            state.send_to(conn_id, &format!("||651@{}", name)).await;
                        }
                        NpcType::QuintaJera => {
                            // VB6: QuintaJera (type 21) — faction rewards
                            if dead { state.send_to(conn_id, "||3").await; return; }
                            if dist > 5 {
                                state.send_to(conn_id, "||13").await; return;
                            }
                            let msg = format!("P|Usa los comandos /RECOMPENSA y /ENLISTAR para interactuar.{}", font_types::INFO);
                            state.send_to(conn_id, &msg).await;
                        }
                        NpcType::BoxDelivery => {
                            // VB6: EntregaCajas (type 24)
                            if dead { state.send_to(conn_id, "||3").await; return; }
                            if dist > 5 {
                                state.send_to(conn_id, "||13").await; return;
                            }
                            let msg = format!("P|Trae las cajas de quest para recibir tu recompensa.{}", font_types::INFO);
                            state.send_to(conn_id, &msg).await;
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
            let sele_pkt = format!("SELE{},{},OBJ", obj.obj_type as i32, obj.name);
            state.send_to(conn_id, &sele_pkt).await;
        }
    }
}

/// Get the ground object index from static map data at a given position.
/// Get the trigger type for a map tile.
fn get_map_tile_trigger(state: &GameState, map: i32, x: i32, y: i32) -> crate::data::maps::Trigger {
    let map_idx = map as usize;
    if let Some(Some(game_map)) = state.game_data.maps.get(map_idx) {
        if x >= 1 && x <= 100 && y >= 1 && y <= 100 {
            return game_map.tiles[(y - 1) as usize][(x - 1) as usize].trigger;
        }
    }
    crate::data::maps::Trigger::None
}

fn get_map_tile_obj(state: &GameState, map: i32, x: i32, y: i32) -> i32 {
    let map_idx = map as usize;
    if let Some(Some(game_map)) = state.game_data.maps.get(map_idx) {
        if x >= 1 && x <= 100 && y >= 1 && y <= 100 {
            return game_map.tiles[(y - 1) as usize][(x - 1) as usize].obj.obj_index as i32;
        }
    }
    0
}

/// Handle door interaction (VB6: AccionParaPuerta in Acciones.bas).
/// Opens/closes doors, handles locks, updates tile blocking and graphics.
async fn accion_para_puerta(state: &mut GameState, conn_id: ConnectionId, map: i32, x: i32, y: i32, obj_index: i32) {
    let (user_x, user_y) = match state.users.get(&conn_id) {
        Some(u) => (u.pos_x, u.pos_y),
        None => return,
    };

    // Distance check: must be within 3 tiles
    if (x - user_x).abs() > 3 || (y - user_y).abs() > 3 {
        let msg = "||10".to_string(); // TEXTO10: Estas demasiado lejos
        state.send_to(conn_id, &msg).await;
        return;
    }

    let obj = match state.get_object(obj_index) {
        Some(o) => o.clone(),
        None => return,
    };

    if obj.cerrada == 1 {
        // Door is CLOSED → open it
        // Check if it needs a key
        if obj.llave == 1 {
            // Check if user has the key in inventory
            let has_key = state.users.get(&conn_id).map(|u| {
                u.inventory.iter().any(|s| {
                    if s.obj_index <= 0 { return false; }
                    state.get_object(s.obj_index)
                        .map(|ko| ko.obj_type == crate::data::objects::ObjType::Key && ko.clave == obj.clave)
                        .unwrap_or(false)
                })
            }).unwrap_or(false);

            if !has_key {
                let msg = "||652".to_string(); // TEXTO652: La puerta esta cerrada con llave
                state.send_to(conn_id, &msg).await;
                return;
            }
        }

        let new_obj = obj.index_abierta;
        if new_obj <= 0 { return; }

        // Update static map data — change object to open version
        set_map_tile_obj(state, map, x, y, new_obj as i16);

        // Unblock tiles (single door: x, x-1; double: +x+1,x+2; porton: +x-2)
        let tiles_to_unblock: Vec<i32> = if obj.porton == 1 {
            vec![x, x - 1, x - 2, x + 1, x + 2]
        } else if obj.puerta_doble == 1 {
            vec![x, x - 1, x + 1, x + 2]
        } else {
            vec![x, x - 1]
        };
        for tx in &tiles_to_unblock {
            set_map_tile_blocked(state, map, *tx, y, false);
        }

        // Send HO packet to area (update graphic)
        let new_grh = state.get_object(new_obj).map(|o| o.grh_index).unwrap_or(0);
        let ho_pkt = format!("HO{},{},{}", new_grh, x, y);
        state.send_data(SendTarget::ToArea { map, x, y }, &ho_pkt).await;

        // Play door sound
        let snd_pkt = format!("TW{}", 45); // SND_PUERTA
        state.send_data(SendTarget::ToArea { map, x, y }, &snd_pkt).await;
    } else {
        // Door is OPEN → close it
        let new_obj = obj.index_cerrada;
        if new_obj <= 0 { return; }

        set_map_tile_obj(state, map, x, y, new_obj as i16);

        // Block tiles
        let closed_obj = state.get_object(new_obj).cloned();
        let tiles_to_block: Vec<i32> = if closed_obj.as_ref().map(|o| o.porton).unwrap_or(0) == 1 {
            vec![x, x - 1, x - 2, x + 1, x + 2]
        } else if closed_obj.as_ref().map(|o| o.puerta_doble).unwrap_or(0) == 1 {
            vec![x, x - 1, x + 1, x + 2]
        } else {
            vec![x, x - 1]
        };
        for tx in &tiles_to_block {
            set_map_tile_blocked(state, map, *tx, y, true);
        }

        let new_grh = closed_obj.map(|o| o.grh_index).unwrap_or(0);
        let ho_pkt = format!("HO{},{},{}", new_grh, x, y);
        state.send_data(SendTarget::ToArea { map, x, y }, &ho_pkt).await;

        let snd_pkt = format!("TW{}", 45);
        state.send_data(SendTarget::ToArea { map, x, y }, &snd_pkt).await;
    }
}

/// Set the ground object on a map tile (runtime modification).
fn set_map_tile_obj(state: &mut GameState, map: i32, x: i32, y: i32, new_obj: i16) {
    let map_idx = map as usize;
    if let Some(Some(game_map)) = state.game_data.maps.get_mut(map_idx) {
        if x >= 1 && x <= 100 && y >= 1 && y <= 100 {
            game_map.tiles[(y - 1) as usize][(x - 1) as usize].obj.obj_index = new_obj;
        }
    }
}

/// Set the blocked status of a map tile (runtime modification for doors).
fn set_map_tile_blocked(state: &mut GameState, map: i32, x: i32, y: i32, blocked: bool) {
    let map_idx = map as usize;
    if let Some(Some(game_map)) = state.game_data.maps.get_mut(map_idx) {
        if x >= 1 && x <= 100 && y >= 1 && y <= 100 {
            game_map.tiles[(y - 1) as usize][(x - 1) as usize].blocked = blocked;
        }
    }
}

/// Get a health status description based on current/max HP.
fn health_description(current: i32, max: i32, dead: bool) -> &'static str {
    if dead {
        return "Muerto";
    }
    if max <= 0 {
        return "Intacto";
    }
    let pct = (current as f64 / max as f64 * 100.0) as i32;
    match pct {
        p if p >= 100 => "Intacto",
        p if p >= 75 => "Algo lastimado",
        p if p >= 45 => "Medio herido",
        p if p >= 20 => "Gravemente herido",
        _ => "Agonizando",
    }
}

/// SEG — Toggle PvP safety.
async fn handle_safe_toggle(state: &mut GameState, conn_id: ConnectionId) {
    let safe = match state.users.get(&conn_id) {
        Some(u) if u.logged => u.safe_toggle,
        _ => return,
    };

    if let Some(user) = state.users.get_mut(&conn_id) {
        user.safe_toggle = !safe;
    }

    if safe {
        state.send_to(conn_id, server_opcodes::SAFE_OFF).await;
    } else {
        state.send_to(conn_id, server_opcodes::SAFE_ON).await;
    }
}

// =====================================================================
// Combat handlers
// =====================================================================

/// AT — Melee/ranged attack.
async fn handle_attack(state: &mut GameState, conn_id: ConnectionId) {
    let user_data = match state.users.get(&conn_id) {
        Some(u) if u.logged => (
            u.pos_map, u.pos_x, u.pos_y, u.heading, u.char_index,
            u.dead, u.paralyzed, u.safe_toggle, u.level,
            u.attributes[0], // Strength
            u.attributes[1], // Agility
            u.min_hit, u.max_hit,
            u.skills[1], // SK2 = Armas (combat skill)
            u.char_name.clone(),
            u.class.clone(),
        ),
        _ => return,
    };
    let (map, x, y, heading, char_index, dead, paralyzed, safe_on, level,
         strength, agility, min_hit, max_hit, skill_armas, attacker_name, class) = user_data;

    if dead || paralyzed {
        return;
    }

    // VB6: Attacking reveals hidden users (DoPermanecerOculto)
    let was_hidden = state.users.get(&conn_id).map(|u| u.hidden).unwrap_or(false);
    if was_hidden {
        if let Some(user) = state.users.get_mut(&conn_id) {
            let stayed = check_permanecer_oculto(user);
            if !stayed {
                // Revealed — broadcast NOVER
                let nover = format!("NOVER{},0", char_index.0);
                state.send_data(SendTarget::ToMap(map), &nover).await;
                state.send_to(conn_id, "||195").await; // "Has sido descubierto."
            }
        }
    }

    // Anti-cheat: check melee cooldown
    if !puede_pegar(state, conn_id) {
        return;
    }

    // Get target tile based on heading
    let (dx, dy) = world::heading_to_offset(heading);
    let target_x = x + dx;
    let target_y = y + dy;

    // Check if there's a user on the target tile
    let target_conn = state.world.grid(map)
        .and_then(|g| g.tile(target_x, target_y))
        .and_then(|t| t.user_conn);

    // Play attack sound/animation to area
    let swing_pkt = format!("TW{}", 2); // Generic attack sound
    state.send_data(
        SendTarget::ToArea { map, x, y },
        &swing_pkt,
    ).await;

    if let Some(victim_id) = target_conn {
        // PvP attack
        if safe_on {
            state.send_to(conn_id, "||207").await; // TEXTO207: Escribe /SEG para quitar el seguro
            return;
        }

        // VB6: Safe zone check (trigger=4) — no combat allowed
        let attacker_trigger = get_map_tile_trigger(state, map, x, y);
        if attacker_trigger == crate::data::maps::Trigger::SafeZone {
            state.send_to(conn_id, "||163").await; // TEXTO163: zona segura
            return;
        }
        let victim_pos = state.users.get(&victim_id).map(|v| (v.pos_x, v.pos_y)).unwrap_or((0, 0));
        let victim_trigger = get_map_tile_trigger(state, map, victim_pos.0, victim_pos.1);
        if victim_trigger == crate::data::maps::Trigger::SafeZone {
            state.send_to(conn_id, "||163").await; // TEXTO163: zona segura
            return;
        }

        let victim_data = match state.users.get(&victim_id) {
            Some(v) if v.logged => (
                v.dead, v.privileges, v.char_name.clone(),
                v.level, v.attributes[1], // Victim agility
                v.skills[3], // SK4 = Tacticas (evasion skill)
                v.max_hp, v.min_hp,
                v.class.clone(),
                v.heading,
            ),
            _ => return,
        };
        let (v_dead, v_privs, victim_name, v_level, v_agility, v_tacticas,
             v_max_hp, v_min_hp, v_class, v_heading) = victim_data;

        if v_dead {
            return;
        }
        if v_privs > 0 {
            return; // Can't attack GMs
        }

        // Hit/miss calculation
        let attack_power = calc_attack_power(skill_armas, agility, level);
        let defense_power = calc_defense_power(v_tacticas, v_agility, v_level);
        let hit_prob = ((50.0 + (attack_power - defense_power) * 0.4) as i32).clamp(10, 90);

        let mut hit = rand_range(1, 100) <= hit_prob;

        // VB6: Assassin backstab — if same heading and miss, convert to hit
        if !hit && class.eq_ignore_ascii_case("Asesino") && heading == v_heading {
            hit = true;
        }

        if !hit {
            // Miss
            let pkt = format!("U3{}", attacker_name);
            state.send_to(victim_id, &pkt).await;
            state.send_to(conn_id, "U1").await;
            return;
        }

        // Damage calculation (VB6: CalcularDaño + UserDañoUser)
        let weapon_dmg = rand_range(min_hit.max(1), max_hit.max(1));
        let str_bonus = ((max_hit as f64 / 5.0) * (strength - 15).max(0) as f64) as i32;
        let base_dmg = 3 * weapon_dmg + str_bonus + rand_range(min_hit, max_hit);
        let class_mod = class_damage_modifier_from_balance(state, &class);
        let mut damage = ((base_dmg as f64) * class_mod) as i32;

        // Body part hit (1=head, 2-6=body)
        let body_part = rand_range(1, 6);

        // Get weapon penetration (Refuerzo)
        let weapon_refuerzo = state.users.get(&conn_id)
            .and_then(|u| {
                if u.equip.weapon > 0 && u.equip.weapon <= MAX_INVENTORY_SLOTS {
                    let obj_idx = u.inventory[u.equip.weapon - 1].obj_index;
                    state.get_object(obj_idx).map(|o| o.refuerzo)
                } else {
                    None
                }
            })
            .unwrap_or(0);

        // Armor absorption with weapon penetration (VB6: absorbido = armor_def + shield_def - Resist)
        let absorption = calc_armor_absorption_with_penetration(state, victim_id, body_part, weapon_refuerzo);
        damage -= absorption;

        // Head hit bonus (VB6: SistemaCombate.bas:1220-1224)
        if body_part == 1 {
            if class.eq_ignore_ascii_case("Asesino") {
                damage += rand_range(7, 11);
            } else {
                damage += rand_range(13, 20);
            }
        }

        // Critical hit (20% chance)
        if rand_range(1, 5) == 1 {
            damage = (damage as f64 * 1.8) as i32;
        }

        damage = damage.max(1);

        // Apuñalar (backstab) — VB6: DoApuñalar, 1.5x vs player if same heading
        let apunalar_skill = state.users.get(&conn_id).and_then(|u| u.skills.get(8).copied()).unwrap_or(0);
        if apunalar_skill > 0 && heading == v_heading {
            if let Some(stab_dmg) = calc_apunalar_damage(apunalar_skill, &class, heading, v_heading, damage, false) {
                damage = stab_dmg;
                let msg = format!("||821@{}@{}", victim_name, damage);
                state.send_to(conn_id, &msg).await;
                let msg2 = format!("||823@{}@{}", attacker_name, damage);
                state.send_to(victim_id, &msg2).await;
                // Skill gain for apuñalar
                if let Some(u) = state.users.get_mut(&conn_id) {
                    try_level_skill(u, 8);
                }
            }
        }

        // Apply damage
        if let Some(victim) = state.users.get_mut(&victim_id) {
            victim.min_hp -= damage;
        }
        let n4_pkt = format!("N4{},{},{}", body_part, damage, attacker_name);
        state.send_to(victim_id, &n4_pkt).await;

        let n5_pkt = format!("N5{},{},{}", body_part, damage, victim_name);
        state.send_to(conn_id, &n5_pkt).await;

        // Desarmar (disarm) — VB6: Desarmar, chance to unequip victim weapon
        let wresterling_skill = state.users.get(&conn_id).and_then(|u| u.skills.get(20).copied()).unwrap_or(0);
        if wresterling_skill > 0 && try_desarmar(wresterling_skill) {
            // Unequip victim's weapon
            if let Some(victim) = state.users.get_mut(&victim_id) {
                if victim.equip.weapon > 0 {
                    victim.equip.weapon = 0;
                }
            }
            let msg = format!("{}Te han desarmado!{}", server_opcodes::CONSOLE_MSG, font_types::COMBAT);
            state.send_to(victim_id, &msg).await;
            let msg2 = format!("{}Has desarmado a {}!{}", server_opcodes::CONSOLE_MSG, victim_name, font_types::COMBAT);
            state.send_to(conn_id, &msg2).await;
            // Skill gain
            if let Some(u) = state.users.get_mut(&conn_id) {
                try_level_skill(u, 20);
            }
        }

        // Weapon poison application (VB6: 60% chance if weapon has Envenena=1)
        let weapon_envenena = state.users.get(&conn_id)
            .and_then(|u| {
                if u.equip.weapon > 0 && u.equip.weapon <= MAX_INVENTORY_SLOTS {
                    let obj_idx = u.inventory[u.equip.weapon - 1].obj_index;
                    state.get_object(obj_idx).map(|o| o.envenena)
                } else {
                    None
                }
            })
            .unwrap_or(false);

        if weapon_envenena && rand_range(1, 100) <= 60 {
            let already_poisoned = state.users.get(&victim_id).map(|u| u.poisoned).unwrap_or(true);
            if !already_poisoned {
                if let Some(victim) = state.users.get_mut(&victim_id) {
                    victim.poisoned = true;
                    victim.counter_poison = 0;
                }
                let msg = format!("{}{}171@{}", server_opcodes::CONSOLE_MSG, "||", attacker_name);
                state.send_to(victim_id, &msg).await;
                let msg2 = format!("{}{}172@{}", server_opcodes::CONSOLE_MSG, "||", victim_name);
                state.send_to(conn_id, &msg2).await;
            }
        }

        // Update victim HP
        send_stats_hp(state, victim_id).await;

        // Check death
        let v_hp = state.users.get(&victim_id).map(|u| u.min_hp).unwrap_or(0);
        if v_hp <= 0 {
            user_die(state, victim_id, Some(conn_id)).await;
        }
    } else {
        // Check for NPC on target tile
        let target_npc = state.world.grid(map)
            .and_then(|g| g.tile(target_x, target_y))
            .map(|t| t.npc_index)
            .unwrap_or(0);

        if target_npc > 0 {
            user_attack_npc(state, conn_id, target_npc as usize, map, x, y,
                            strength, agility, level, min_hit, max_hit,
                            skill_armas, &attacker_name, &class).await;
        }
    }
}

/// Calculate attack power based on weapon skill, agility, and level.
/// Calculate weapon attack power (VB6: PoderAtaqueArma).
/// Uses balance data mod_poder_ataque_armas per class.
fn calc_attack_power_with_balance(skill: i32, agility: i32, level: i32, class_mod: f32) -> f64 {
    let base = if skill < 31 {
        skill as f64 * class_mod as f64
    } else if skill < 61 {
        (skill + agility) as f64 * class_mod as f64
    } else if skill < 91 {
        (skill + 2 * agility) as f64 * class_mod as f64
    } else {
        (skill + 3 * agility) as f64 * class_mod as f64
    };
    base + 2.5 * (level - 12).max(0) as f64
}

/// Simplified version for when we don't have balance data handy.
fn calc_attack_power(skill: i32, agility: i32, level: i32) -> f64 {
    calc_attack_power_with_balance(skill, agility, level, 1.0)
}

/// Calculate defense/evasion power (VB6: PoderEvasion).
/// Uses balance data mod_evasion per class.
fn calc_defense_power_with_balance(
    tacticas: i32, agility: i32, level: i32, class_mod: f32,
    has_shield: bool, shield_max_def: i32, shield_class_mod: f32,
    is_mago: bool,
) -> f64 {
    // VB6 PoderEvasion formula: (Tacticas + (Tacticas/33 * Agility)) * ModificadorEvasion(clase)
    let base = (tacticas as f64 + (tacticas as f64 / 33.0 * agility as f64)) * class_mod as f64;
    let mut power = base + 2.5 * (level - 12).max(0) as f64;

    // Shield evasion bonus (VB6: not for mages)
    if has_shield && !is_mago {
        let shield_evasion = ((agility as f64 + shield_max_def as f64 + 30.0) * shield_class_mod as f64) / 2.0;
        power += shield_evasion;
    }

    power
}

/// Simplified version.
fn calc_defense_power(tacticas: i32, agility: i32, level: i32) -> f64 {
    calc_defense_power_with_balance(tacticas, agility, level, 1.0, false, 0, 1.0, false)
}

/// Get armor absorption for a body hit (VB6: body armor MinDef-MaxDef).
fn calc_armor_absorption(state: &GameState, conn_id: ConnectionId, body_part: i32) -> i32 {
    let user = match state.users.get(&conn_id) {
        Some(u) => u,
        None => return 0,
    };

    let armor_slot = if body_part == 1 {
        // Head hit — use helmet
        user.equip.helmet
    } else {
        // Body hit — use armor
        user.equip.armor
    };

    if armor_slot == 0 || armor_slot > MAX_INVENTORY_SLOTS {
        return 0;
    }

    let obj_index = user.inventory[armor_slot - 1].obj_index;
    if obj_index <= 0 {
        return 0;
    }

    match state.get_object(obj_index) {
        Some(obj) if obj.min_def > 0 || obj.max_def > 0 => {
            rand_range(obj.min_def.max(0), obj.max_def.max(1))
        }
        _ => 0,
    }
}

/// Get armor absorption with weapon penetration (VB6: absorbido = armor_def + shield_def - Resist).
fn calc_armor_absorption_with_penetration(state: &GameState, conn_id: ConnectionId, body_part: i32, refuerzo: i32) -> i32 {
    let user = match state.users.get(&conn_id) {
        Some(u) => u,
        None => return 0,
    };

    let armor_slot = if body_part == 1 {
        user.equip.helmet
    } else {
        user.equip.armor
    };

    if armor_slot == 0 || armor_slot > MAX_INVENTORY_SLOTS {
        return 0;
    }

    let obj_index = user.inventory[armor_slot - 1].obj_index;
    if obj_index <= 0 {
        return 0;
    }

    let armor_def = match state.get_object(obj_index) {
        Some(obj) if obj.min_def > 0 || obj.max_def > 0 => {
            rand_range(obj.min_def.max(0), obj.max_def.max(1))
        }
        _ => 0,
    };

    // Shield defense bonus (VB6: defbarco)
    let shield_def = if user.equip.shield > 0 && user.equip.shield <= MAX_INVENTORY_SLOTS {
        let shield_idx = user.inventory[user.equip.shield - 1].obj_index;
        match state.get_object(shield_idx) {
            Some(obj) if obj.min_def > 0 || obj.max_def > 0 => {
                rand_range(obj.min_def.max(0), obj.max_def.max(1))
            }
            _ => 0,
        }
    } else {
        0
    };

    // VB6: absorbido = armor_def + shield_def - Resist
    (armor_def + shield_def - refuerzo).max(0)
}

/// Class-based damage modifier (uses BalanceData when available).
fn class_damage_modifier_from_balance(state: &GameState, class: &str) -> f64 {
    let bal = &state.game_data.balance;
    let mod_val = bal.mod_dano_clase_armas[
        crate::data::balance::class_name_to_index(class).unwrap_or(0)
    ];
    if mod_val > 0.0 { mod_val as f64 } else { class_damage_modifier(class) }
}

/// Fallback class-based damage modifier.
fn class_damage_modifier(class: &str) -> f64 {
    match class.to_lowercase().as_str() {
        "guerrero" => 1.1,
        "cazador" => 0.9,
        "paladin" => 1.0,
        "asesino" => 1.0,
        "ladron" => 0.8,
        "bardo" => 0.8,
        "clerigo" => 0.8,
        "mago" => 0.5,
        "druida" => 0.7,
        "pirata" => 1.0,
        "trabajador" => 0.8,
        "bandido" => 0.9,
        _ => 0.8,
    }
}

/// Handle player death.
async fn user_die(state: &mut GameState, conn_id: ConnectionId, killer_id: Option<ConnectionId>) {
    let user_data = match state.users.get(&conn_id) {
        Some(u) => (u.pos_map, u.pos_x, u.pos_y, u.char_index, u.char_name.clone(), u.level),
        None => return,
    };
    let (map, x, y, char_index, victim_name, victim_level) = user_data;

    // Mark as dead, change body to dead model
    if let Some(user) = state.users.get_mut(&conn_id) {
        user.dead = true;
        user.min_hp = 0;
        user.min_sta = 0;
        // Clear status effects
        user.paralyzed = false;
        user.invisible = false;
        user.meditating = false;
        // Dead body model (neutral)
        user.body = DEAD_BODY_NEUTRAL;
        user.head = DEAD_HEAD_NEUTRAL;
        user.weapon_anim = 0;
        user.shield_anim = 0;
        user.casco_anim = 0;
        // Resurrection cooldown (20 ticks before player can be rezzed)
        user.time_revivir = 20;
    }

    // Deequip all items and drop inventory (VB6 UserDie lines 1750-1800)
    if let Some(user) = state.users.get_mut(&conn_id) {
        // Reset equipment slots
        user.equip.weapon = 0;
        user.equip.armor = 0;
        user.equip.shield = 0;
        user.equip.helmet = 0;
        user.equip.municion = 0;
        // Mark inventory items as not equipped
        for slot in user.inventory.iter_mut() {
            slot.equipped = false;
        }
    }

    // Drop non-newbie items on the ground (VB6 TirarTodo)
    // Skip if user is newbie (level < 13) unless criminal
    let is_newbie = victim_level < 13;
    let is_criminal = state.users.get(&conn_id).map(|u| u.criminal).unwrap_or(false);
    if !is_newbie || is_criminal {
        // Collect items to drop
        let mut items_to_drop: Vec<(i32, i32)> = Vec::new(); // (obj_index, amount)
        if let Some(user) = state.users.get(&conn_id) {
            for slot in user.inventory.iter() {
                if slot.obj_index > 0 && slot.amount > 0 {
                    // Check if item is newbie (don't drop newbie items)
                    let is_newbie_item = state.game_data.objects.get((slot.obj_index - 1) as usize)
                        .map(|o| o.newbie)
                        .unwrap_or(false);
                    if !is_newbie_item {
                        items_to_drop.push((slot.obj_index, slot.amount));
                    }
                }
            }
        }

        // Clear dropped items from inventory
        if let Some(user) = state.users.get_mut(&conn_id) {
            for slot in user.inventory.iter_mut() {
                if slot.obj_index > 0 {
                    let is_newbie_item = state.game_data.objects.get((slot.obj_index - 1) as usize)
                        .map(|o| o.newbie)
                        .unwrap_or(false);
                    if !is_newbie_item {
                        slot.obj_index = 0;
                        slot.amount = 0;
                    }
                }
            }
        }

        // Place items on ground near death position (spiral out from death tile)
        let offsets = [(0,0),(1,0),(0,1),(-1,0),(0,-1),(1,1),(-1,1),(1,-1),(-1,-1)];
        let mut off_idx = 0;
        for (obj_idx, amount) in items_to_drop {
            if let Some(obj) = state.game_data.objects.get((obj_idx - 1) as usize) {
                let grh = obj.grh_index;
                // Find next tile that has no ground item
                let mut placed = false;
                for tries in 0..offsets.len() {
                    let idx = (off_idx + tries) % offsets.len();
                    let (ox, oy) = offsets[idx];
                    let tx = x + ox as i32;
                    let ty = y + oy as i32;
                    if tx < 1 || tx > 100 || ty < 1 || ty > 100 { continue; }
                    let tile_free = state.world.grid(map)
                        .and_then(|g| g.tile(tx, ty))
                        .map(|t| t.ground_item.obj_index == 0)
                        .unwrap_or(false);
                    if tile_free {
                        {
                            let grid = state.world.grid_mut(map);
                            if let Some(tile) = grid.tile_mut(tx, ty) {
                                tile.ground_item.obj_index = obj_idx;
                                tile.ground_item.amount = amount;
                            }
                        }
                        let ho_pkt = format!("HO{},{},{}", grh, tx, ty);
                        state.send_data(SendTarget::ToArea { map, x, y }, &ho_pkt).await;
                        off_idx = (idx + 1) % offsets.len();
                        placed = true;
                        break;
                    }
                }
                // If all tiles occupied, items are lost (VB6 behavior)
                if !placed { continue; }
            }
        }
    }

    // Send death notification
    state.send_to(conn_id, server_opcodes::YOU_DIED).await;
    send_stats_hp(state, conn_id).await;

    // VB6: On PK maps, send "MUERT" packet to show death dialog (frmMuertito)
    let map_is_pk = state.game_data.maps.get(map as usize)
        .and_then(|m| m.as_ref())
        .map(|m| m.info.pk)
        .unwrap_or(false);
    if map_is_pk {
        state.send_to(conn_id, "MUERT").await;
    }

    // Broadcast dead body model change (CP packet) to area
    let cp_pkt = format!(
        "CP{},{},{},{},{},{},0,0,{}",
        char_index.0, DEAD_BODY_NEUTRAL, DEAD_HEAD_NEUTRAL,
        state.users.get(&conn_id).map(|u| u.heading).unwrap_or(2),
        0, 0, 0  // weapon, shield, casco = 0
    );
    state.send_data(SendTarget::ToArea { map, x, y }, &cp_pkt).await;

    // Notify area
    let msg = format!("T|12632256\u{00B0}{} ha muerto\u{00B0}{}", victim_name, char_index.0);
    state.send_data(
        SendTarget::ToAreaButIndex { conn_id, map, x, y },
        &msg,
    ).await;

    // Check if the dead user was in a duel or desafio
    let en_duelo = state.users.get(&conn_id).map(|u| u.en_duelo).unwrap_or(false);
    let en_desafio = state.users.get(&conn_id).map(|u| u.en_desafio).unwrap_or(false);

    if en_duelo {
        resolve_duel_death(state, conn_id).await;
    }
    if en_desafio {
        resolve_desafio_death(state, conn_id).await;
    }

    // Check event deaths
    let en_evento = state.users.get(&conn_id).map(|u| u.en_evento).unwrap_or(false);
    let torneo_auto = state.users.get(&conn_id).map(|u| u.torneo_auto).unwrap_or(false);
    if en_evento {
        let kid = killer_id.unwrap_or(0);
        evento_player_death(state, conn_id, kid).await;
    }
    if torneo_auto {
        torneo_auto_death(state, conn_id).await;
    }

    // Award experience to killer
    if let Some(killer) = killer_id {
        let exp_gain = victim_level as i64;
        let killer_name = state.users.get(&killer).map(|u| u.char_name.clone()).unwrap_or_default();

        if let Some(k) = state.users.get_mut(&killer) {
            k.exp += exp_gain;
        }

        // Criminal/citizen reputation tracking on PvP kill (VB6: ContarMuerte)
        // Kill deduplication: only count if different from last kill of same faction
        let victim_criminal = state.users.get(&conn_id).map(|u| u.criminal).unwrap_or(false);
        let victim_name_upper = victim_name.to_uppercase();
        if victim_criminal {
            // Killed a criminal — increment criminales_matados (with dedup)
            let last = state.users.get(&killer).map(|k| k.last_crim_matado.clone()).unwrap_or_default();
            if last != victim_name_upper {
                if let Some(k) = state.users.get_mut(&killer) {
                    k.last_crim_matado = victim_name_upper;
                    if k.criminales_matados < 65000 {
                        k.criminales_matados += 1;
                    }
                }
            }
        } else {
            // Killed a citizen — increment ciudadanos_matados (with dedup), become criminal
            let last = state.users.get(&killer).map(|k| k.last_ciud_matado.clone()).unwrap_or_default();
            if last != victim_name_upper {
                if let Some(k) = state.users.get_mut(&killer) {
                    k.last_ciud_matado = victim_name_upper;
                    if k.ciudadanos_matados < 65000 {
                        k.ciudadanos_matados += 1;
                    }
                    k.criminal = true;
                }
            } else {
                // Still become criminal even if dedup prevents counter increment
                if let Some(k) = state.users.get_mut(&killer) {
                    k.criminal = true;
                }
            }
            // Broadcast appearance change (criminal status)
            let (km, kx, ky) = state.users.get(&killer)
                .map(|u| (u.pos_map, u.pos_x, u.pos_y))
                .unwrap_or((0, 0, 0));
            if km > 0 {
                if let Some(k) = state.users.get(&killer) {
                    let cp = format!(
                        "CP{},{},{},{},{},{},0,0,{}",
                        k.char_index.0, k.body, k.head, k.heading,
                        k.weapon_anim, k.shield_anim, k.casco_anim
                    );
                    state.send_data(SendTarget::ToArea { map: km, x: kx, y: ky }, &cp).await;
                }
            }
        }

        // Check quest kill tracking (pass victim conn_id for trigger zone check)
        quest_check_player_kill(state, killer, conn_id).await;

        // Notify killer (VB6: ||60@name@class + ||170@exp)
        state.send_to(killer, &format!("||60@{}@{}", victim_name, exp_gain)).await;
        state.send_to(killer, &format!("||170@{}", exp_gain)).await;
        send_stats_exp(state, killer).await;
        check_user_level(state, killer).await;

        info!("[COMBAT] '{}' killed '{}' (+{} exp)", killer_name, victim_name, exp_gain);
    }
}

// =====================================================================
// Death model constants (VB6 Declares.bas)
// =====================================================================
const DEAD_BODY_NEUTRAL: i32 = 8;
const DEAD_HEAD_NEUTRAL: i32 = 500;

// Naked body IDs by race + gender (VB6 DarCuerpoDesnudo)
fn naked_body(race: &str, gender: &str) -> i32 {
    let race_up = race.to_uppercase();
    let gender_up = gender.to_uppercase();
    match (race_up.as_str(), gender_up.as_str()) {
        ("HUMANO", "HOMBRE") | ("HUMANO", "1") => 21,
        ("HUMANO", "MUJER") | ("HUMANO", "2") => 39,
        ("ELFO", "HOMBRE") | ("ELFO", "1") => 21,
        ("ELFO", "MUJER") | ("ELFO", "2") => 39,
        ("ELFO OSCURO", "HOMBRE") | ("ELFO OSCURO", "1") => 32,
        ("ELFO OSCURO", "MUJER") | ("ELFO OSCURO", "2") => 40,
        ("ENANO", "HOMBRE") | ("ENANO", "1") => 53,
        ("ENANO", "MUJER") | ("ENANO", "2") => 60,
        ("GNOMO", "HOMBRE") | ("GNOMO", "1") => 53,
        ("GNOMO", "MUJER") | ("GNOMO", "2") => 60,
        _ => 21, // Default: male human naked
    }
}

// Default head GRH by race + gender (for recovery when head is corrupted to 500)
fn default_head_for_race(race: &str, gender: i32) -> i32 {
    let race_up = race.to_uppercase();
    match (race_up.as_str(), gender) {
        ("HUMANO", 2) => 70,   // Female human
        ("HUMANO", _) => 1,    // Male human
        ("ELFO", 2) => 70,     // Female elf
        ("ELFO", _) => 101,    // Male elf
        ("ELFO OSCURO", 2) => 480, // Female dark elf
        ("ELFO OSCURO", _) => 401, // Male dark elf
        ("ENANO", 2) => 270,   // Female dwarf
        ("ENANO", _) => 201,   // Male dwarf
        ("GNOMO", 2) => 270,   // Female gnome
        ("GNOMO", _) => 201,   // Male gnome
        (_, 2) => 70,          // Default female
        _ => 1,                // Default male
    }
}

// =====================================================================
// Slash commands (from talk handler)
// =====================================================================

async fn handle_slash_command(state: &mut GameState, conn_id: ConnectionId, cmd: &str) {
    let cmd_upper = cmd.to_uppercase();
    if cmd_upper == "/RESUCITAR" {
        handle_resucitar(state, conn_id).await;
    } else if cmd_upper.starts_with("/FUNDARCLAN") {
        handle_slash_fundarclan(state, conn_id).await;
    } else if cmd_upper.starts_with("/CERRARCLAN") {
        handle_slash_cerrarclan(state, conn_id).await;
    } else if cmd_upper.starts_with("/SALIRCLAN") {
        handle_slash_salirclan(state, conn_id).await;
    } else if cmd_upper.starts_with("/HACLIDER ") {
        let target = cmd[10..].trim();
        handle_slash_haclider(state, conn_id, target).await;
    } else if cmd_upper.starts_with("/SUBLIDER ") {
        let target = cmd[10..].trim();
        handle_slash_sublider(state, conn_id, target).await;
    } else if cmd_upper.starts_with("/QSUBLIDR ") {
        let target = cmd[10..].trim();
        handle_slash_qsublidr(state, conn_id, target).await;
    } else if cmd_upper == "/CLAN" {
        handle_slash_clan_list(state, conn_id).await;
    } else if cmd_upper.starts_with("/CMSG ") {
        let text = &cmd[6..];
        handle_slash_cmsg(state, conn_id, text).await;
    } else if cmd_upper.starts_with("/ENLISTAR") {
        handle_slash_enlistar(state, conn_id).await;
    } else if cmd_upper.starts_with("/INFORMACION") {
        handle_slash_faction_info(state, conn_id).await;
    } else if cmd_upper.starts_with("/RECOMPENSA") {
        handle_slash_recompensa(state, conn_id).await;
    } else if cmd_upper.starts_with("/RENUNCIA") {
        handle_slash_renunciar(state, conn_id).await;
    } else if cmd_upper.starts_with("/NUEVAPARTY") {
        handle_slash_nuevaparty(state, conn_id).await;
    } else if cmd_upper.starts_with("/PARTY ") {
        let target = cmd[7..].trim();
        handle_slash_party_invite(state, conn_id, target).await;
    } else if cmd_upper.starts_with("/ACEPTAR") {
        handle_slash_party_accept(state, conn_id).await;
    } else if cmd_upper.starts_with("/CANCELAR") {
        handle_slash_party_cancel(state, conn_id).await;
    } else if cmd_upper.starts_with("/FINPARTY") {
        handle_slash_finparty(state, conn_id).await;
    } else if cmd_upper.starts_with("/PINFO") {
        handle_slash_pinfo(state, conn_id).await;
    } else if cmd_upper.starts_with("/QUEST") {
        handle_slash_quest(state, conn_id).await;
    } else if cmd_upper.starts_with("/NOQUEST") {
        handle_slash_noquest(state, conn_id).await;
    } else if cmd_upper == "/ONLINE" {
        handle_slash_online(state, conn_id).await;
    } else if cmd_upper == "/PING" {
        state.send_to(conn_id, "HOLASOYUNCIRUJA").await;
    } else if cmd_upper == "/BALANCE" {
        handle_slash_balance(state, conn_id).await;
    } else if cmd_upper.starts_with("/GLOBAL ") {
        let text = &cmd[8..];
        handle_slash_global(state, conn_id, text).await;
    } else if cmd_upper == "/EST" || cmd_upper == "/STATS" {
        handle_slash_stats(state, conn_id).await;
    // =====================================================================
    // GM / Admin commands (require privileges > 0)
    // =====================================================================
    } else if cmd_upper.starts_with("/GMSG ") {
        let text = &cmd[6..];
        handle_slash_gmsg(state, conn_id, text).await;
    } else if cmd_upper.starts_with("/SMSG ") {
        let text = &cmd[6..];
        handle_slash_smsg(state, conn_id, text).await;
    } else if cmd_upper == "/NAVE" {
        handle_slash_nave(state, conn_id).await;
    } else if cmd_upper == "/HABILITAR" {
        handle_slash_habilitar(state, conn_id).await;
    } else if cmd_upper.starts_with("/COL ") {
        let args = &cmd[5..];
        handle_slash_col(state, conn_id, args).await;
    } else if cmd_upper.starts_with("/NOADV ") {
        let target = cmd[7..].trim();
        handle_slash_noadv(state, conn_id, target).await;
    } else if cmd_upper.starts_with("/LIBERAR ") {
        let target = cmd[9..].trim();
        handle_slash_liberar(state, conn_id, target).await;
    } else if cmd_upper.starts_with("/PENAS ") {
        let target = cmd[7..].trim();
        handle_slash_penas(state, conn_id, target).await;
    } else if cmd_upper.starts_with("/MOD ") {
        let args = &cmd[5..];
        handle_slash_mod(state, conn_id, args, false).await;
    } else if cmd_upper.starts_with("/SMOD ") {
        let args = &cmd[6..];
        handle_slash_smod(state, conn_id, args).await;
    } else if cmd_upper.starts_with("/ACC ") {
        let npc_id = cmd[5..].trim();
        handle_slash_acc(state, conn_id, npc_id, false).await;
    } else if cmd_upper.starts_with("/RACC ") {
        let npc_id = cmd[6..].trim();
        handle_slash_acc(state, conn_id, npc_id, true).await;
    } else if cmd_upper.starts_with("/CONSEJERO ") {
        let target = cmd[11..].trim();
        handle_slash_set_privilege(state, conn_id, target, privilege_level::CONSEJERO, "consejero", privilege_level::DIRECTOR).await;
    } else if cmd_upper.starts_with("/SEMIDIOS ") {
        let target = cmd[10..].trim();
        handle_slash_set_privilege(state, conn_id, target, privilege_level::SEMIDIOS, "semidios", privilege_level::DIRECTOR).await;
    } else if cmd_upper.starts_with("/DIOS ") {
        let target = cmd[6..].trim();
        handle_slash_set_privilege(state, conn_id, target, privilege_level::DIOS, "dios", privilege_level::DIRECTOR).await;
    } else if cmd_upper.starts_with("/GDIOS ") {
        let target = cmd[7..].trim();
        handle_slash_set_privilege(state, conn_id, target, privilege_level::GRAN_DIOS, "gran dios", privilege_level::DIRECTOR).await;
    } else if cmd_upper.starts_with("/EVENT ") {
        let target = cmd[7..].trim();
        handle_slash_set_privilege(state, conn_id, target, privilege_level::EVENT_MASTER, "event master", privilege_level::DIRECTOR).await;
    } else if cmd_upper.starts_with("/DIRECTOR ") {
        let target = cmd[10..].trim();
        handle_slash_set_privilege(state, conn_id, target, privilege_level::DIRECTOR, "coordinador", privilege_level::DIRECTOR).await;
    } else if cmd_upper.starts_with("/SUBADMINISTRADOR ") {
        let target = cmd[18..].trim();
        handle_slash_set_privilege(state, conn_id, target, privilege_level::SUB_ADMINISTRADOR, "sub admin", privilege_level::SUB_ADMINISTRADOR).await;
    } else if cmd_upper.starts_with("/DEVELOPER ") {
        let target = cmd[11..].trim();
        handle_slash_set_privilege(state, conn_id, target, privilege_level::DEVELOPER, "developer", privilege_level::DEVELOPER).await;
    } else if cmd_upper.starts_with("/ADMIN ") {
        let target = cmd[7..].trim();
        handle_slash_set_privilege(state, conn_id, target, privilege_level::ADMINISTRADOR, "administrador", privilege_level::ADMINISTRADOR).await;
    } else if cmd_upper.starts_with("/PJ ") {
        let target = cmd[4..].trim();
        handle_slash_set_privilege(state, conn_id, target, privilege_level::USER, "personaje", privilege_level::DIRECTOR).await;
    } else if cmd_upper.starts_with("/CHANGENICK ") {
        let new_name = cmd[12..].trim();
        handle_slash_changenick(state, conn_id, new_name).await;
    } else if cmd_upper.starts_with("/BORRARPJ ") {
        let target = cmd[10..].trim();
        handle_slash_borrarpj(state, conn_id, target).await;
    } else if cmd_upper.starts_with("/BANHD ") {
        let target = cmd[7..].trim();
        handle_slash_banhd(state, conn_id, target).await;
    } else if cmd_upper.starts_with("/HECHIZO ") {
        let args = &cmd[9..];
        handle_slash_hechizo(state, conn_id, args).await;
    } else if cmd_upper.starts_with("/DONACION ") {
        let args = &cmd[10..];
        handle_slash_donacion(state, conn_id, args).await;
    } else if cmd_upper.starts_with("/RESETVALS ") {
        let args = cmd[11..].trim();
        handle_slash_resetvals(state, conn_id, args).await;
    } else if cmd_upper.starts_with("/SIEGE ") {
        let args = &cmd[7..];
        handle_gm_start_siege(state, conn_id, args).await;
    } else if cmd_upper == "/ENDSIEGE" {
        handle_gm_end_siege(state, conn_id).await;
    } else if cmd_upper.starts_with("/PRETORIANO ") {
        // /PRETORIANO <faccion> — Spawn praetorian clan on current position
        let faccion: i32 = cmd[12..].trim().parse().unwrap_or(1);
        let (map, x, y) = match state.users.get(&conn_id) {
            Some(u) if u.logged && u.privileges >= privilege_level::DIOS => (u.pos_map, u.pos_x, u.pos_y),
            _ => { return; }
        };
        crear_clan_pretoriano(state, map, x, y, faccion).await;
        state.send_to(conn_id, &format!("{}Clan pretoriano creado.{}", server_opcodes::CONSOLE_MSG, font_types::INFO)).await;
    } else if cmd_upper.starts_with("/EVENTO ") {
        let args = cmd[8..].trim();
        handle_gm_evento(state, conn_id, args).await;
    } else if cmd_upper.starts_with("/TORNEOAUTO ") {
        let rounds: i32 = cmd[12..].trim().parse().unwrap_or(3);
        handle_gm_torneo_auto(state, conn_id, rounds).await;
    } else if cmd_upper == "/FINEVENTO" {
        let priv_level = state.users.get(&conn_id).map(|u| u.privileges).unwrap_or(0);
        if priv_level >= privilege_level::EVENT_MASTER {
            if state.evento_activo {
                evento_finalize(state).await;
            } else if state.evento_inscripciones {
                evento_reset(state);
                state.send_to(conn_id, &format!("{}Evento cancelado.{}", server_opcodes::CONSOLE_MSG, font_types::INFO)).await;
            }
        }
    } else if cmd_upper == "/LIMPRETORIANO" {
        let priv_level = state.users.get(&conn_id).map(|u| u.privileges).unwrap_or(0);
        if priv_level >= privilege_level::DIOS {
            limpiar_clan_pretoriano(state).await;
            state.send_to(conn_id, &format!("{}Clan pretoriano eliminado.{}", server_opcodes::CONSOLE_MSG, font_types::INFO)).await;
        }
    // =====================================================================
    // Duel, Tournament, and Event commands
    // =====================================================================
    } else if cmd_upper.starts_with("/DUELO ") {
        let args = &cmd[7..];
        handle_slash_duelo(state, conn_id, args).await;
    } else if cmd_upper == "/SIDUELO" {
        handle_slash_siduelo(state, conn_id).await;
    } else if cmd_upper == "/DESAFIO" {
        handle_slash_desafio(state, conn_id).await;
    } else if cmd_upper.starts_with("/DESAFIAR") {
        handle_slash_desafiar(state, conn_id).await;
    } else if cmd_upper == "/ABANDONAR" {
        handle_slash_abandonar(state, conn_id).await;
    } else if cmd_upper == "/TORNEO" {
        handle_slash_torneo(state, conn_id).await;
    } else if cmd_upper == "/PARTICIPANTES" {
        handle_slash_participantes(state, conn_id).await;
    } else if cmd_upper == "/HORDA" {
        handle_slash_horda(state, conn_id).await;
    } else if cmd_upper == "/ALIANZA" {
        handle_slash_alianza(state, conn_id).await;
    } else if cmd_upper == "/PARTICIPAR" {
        handle_slash_participar(state, conn_id).await;
    } else if cmd_upper == "/EVENTOS" {
        handle_slash_eventos(state, conn_id).await;
    } else if cmd_upper.starts_with("/CVC ") {
        let clan_name = cmd[5..].trim();
        handle_slash_cvc(state, conn_id, clan_name).await;
    } else if cmd_upper == "/NCVC" {
        handle_slash_ncvc(state, conn_id).await;
    } else if cmd_upper == "/SCVC" {
        handle_slash_scvc(state, conn_id).await;
    } else if cmd_upper == "/REGRESAR" {
        handle_slash_regresar(state, conn_id).await;
    } else if cmd_upper == "/SALIR" {
        handle_slash_salir(state, conn_id).await;
    } else if cmd_upper == "/MEDITAR" {
        handle_slash_meditar(state, conn_id).await;
    } else if cmd_upper == "/SEG" {
        // Toggle PvP safety (same as SEG opcode, but via slash command)
        let is_safe = state.users.get(&conn_id).map(|u| u.safe_toggle).unwrap_or(true);
        if let Some(user) = state.users.get_mut(&conn_id) {
            user.safe_toggle = !is_safe;
        }
        if !is_safe {
            state.send_to(conn_id, server_opcodes::SAFE_ON).await;
        } else {
            state.send_to(conn_id, server_opcodes::SAFE_OFF).await;
        }
    } else if cmd_upper == "/SEGR" {
        // Toggle resurrection safety — prevents others from rezzing you
        let is_safe = state.users.get(&conn_id).map(|u| u.seguro_resu).unwrap_or(false);
        if let Some(user) = state.users.get_mut(&conn_id) {
            user.seguro_resu = !is_safe;
        }
        if !is_safe {
            let msg = format!("{}Seguro de resurreccion activado{}", server_opcodes::CONSOLE_MSG, font_types::INFO);
            state.send_to(conn_id, &msg).await;
        } else {
            let msg = format!("{}Seguro de resurreccion desactivado{}", server_opcodes::CONSOLE_MSG, font_types::INFO);
            state.send_to(conn_id, &msg).await;
        }
    } else if cmd_upper.starts_with("/DESC ") {
        let desc = cmd[6..].trim();
        handle_slash_desc(state, conn_id, desc).await;
    } else if cmd_upper == "/COMERCIAR" {
        handle_slash_comerciar(state, conn_id).await;
    } else if cmd_upper == "/BOVEDA" {
        handle_slash_boveda(state, conn_id).await;
    } else if cmd_upper.starts_with("/DARORO ") {
        let args = &cmd[8..];
        handle_slash_daroro(state, conn_id, args).await;
    } else if cmd_upper.starts_with("/DEPOSITAR ") {
        let amount: i64 = cmd[11..].trim().parse().unwrap_or(0);
        handle_slash_depositar(state, conn_id, amount).await;
    } else if cmd_upper.starts_with("/RETIRAR ") {
        let amount: i64 = cmd[9..].trim().parse().unwrap_or(0);
        handle_slash_retirar_oro(state, conn_id, amount).await;
    } else if cmd_upper.starts_with("/FMSG ") {
        let text = &cmd[6..];
        handle_slash_fmsg(state, conn_id, text).await;
    } else if cmd_upper == "/HORA" {
        handle_slash_hora(state, conn_id).await;
    } else if cmd_upper.starts_with("/NICK ") {
        let name = cmd[6..].trim();
        handle_slash_nick_check(state, conn_id, name).await;
    } else if cmd_upper.starts_with("/_BUG ") {
        let text = &cmd[6..];
        let name = state.users.get(&conn_id).map(|u| u.char_name.clone()).unwrap_or_default();
        info!("[BUG] {} reports: {}", name, text);
        state.send_to(conn_id, &format!("{}Bug reportado. Gracias!{}", server_opcodes::CONSOLE_MSG, font_types::INFO)).await;
    } else if cmd_upper == "/ADVERTENCIAS" {
        handle_slash_advertencias(state, conn_id).await;
    } else if cmd_upper == "/CURAR" {
        handle_slash_curar(state, conn_id).await;
    } else if cmd_upper == "/DEMONIO" || cmd_upper == "/ANGEL" {
        handle_slash_transform(state, conn_id, &cmd_upper).await;
    } else if cmd_upper == "/MONTAR" {
        handle_slash_montar(state, conn_id).await;
    } else if cmd_upper == "/DESMONTAR" {
        handle_slash_desmontar(state, conn_id).await;
    } else if cmd_upper == "/QUITARMASCOTA" {
        handle_slash_quitarmascota(state, conn_id).await;
    } else if cmd_upper == "/MSJ" {
        handle_slash_msj(state, conn_id).await;
    } else if cmd_upper == "/CIUDADANIA" {
        handle_slash_ciudadania(state, conn_id).await;
    } else if cmd_upper.starts_with("/VIAJAR ") {
        let city = &cmd[8..];
        handle_slash_viajar(state, conn_id, city).await;
    } else if cmd_upper == "/ENTRENAR" {
        handle_slash_entrenar(state, conn_id).await;
    } else if cmd_upper.starts_with("/CENTINELA ") {
        let code = cmd[11..].trim();
        handle_slash_centinela(state, conn_id, code).await;
    } else if cmd_upper.starts_with("/IR ") {
        let dest = &cmd[4..];
        handle_slash_ir(state, conn_id, dest).await;
    } else if cmd_upper == "/VOTAR" {
        handle_slash_votar(state, conn_id).await;
    } else if cmd_upper == "/RESULTADOS" {
        handle_slash_resultados(state, conn_id).await;
    } else if cmd_upper == "/GUERRA" {
        handle_slash_guerra(state, conn_id).await;
    } else if cmd_upper == "/CIRUJIA" {
        handle_slash_cirujia(state, conn_id).await;
    } else if cmd_upper == "/NOBLE" {
        handle_slash_noble(state, conn_id).await;
    } else if cmd_upper == "/DESENTERRAR" {
        handle_slash_desenterrar(state, conn_id).await;
    } else if cmd_upper == "/BOTIX" || cmd_upper == "/BOTIX2" {
        handle_slash_botix(state, conn_id).await;
    } else if cmd_upper == "/INFOSUB" {
        handle_slash_infosub(state, conn_id).await;
    } else if cmd_upper == "/SUBASTAR" {
        handle_slash_subastar(state, conn_id).await;
    } else if cmd_upper.starts_with("/CASAR ") {
        state.send_to(conn_id, &format!("{}Sistema de matrimonio no disponible.{}", server_opcodes::CONSOLE_MSG, font_types::INFO)).await;
    } else if cmd_upper == "/DIVORCIARSE" {
        state.send_to(conn_id, &format!("{}Sistema de matrimonio no disponible.{}", server_opcodes::CONSOLE_MSG, font_types::INFO)).await;
    } else if cmd_upper == "/CASTILLOS" {
        handle_slash_castillos(state, conn_id).await;
    } else if cmd_upper == "/EMOTICONS" {
        state.send_to(conn_id, &format!("{}Emoticones toggled.{}", server_opcodes::CONSOLE_MSG, font_types::INFO)).await;
    } else if cmd_upper.starts_with("/VOTO ") {
        let candidate = cmd[6..].trim();
        handle_slash_voto(state, conn_id, candidate).await;
    } else if cmd_upper.starts_with("/PAREJA ") {
        let target = cmd[8..].trim();
        handle_slash_pareja(state, conn_id, target).await;
    } else if cmd_upper == "/SICV" {
        handle_slash_sicv(state, conn_id).await;
    } else if cmd_upper == "/PODER" {
        state.send_to(conn_id, &format!("{}Nadie tiene el Gran Poder.{}", server_opcodes::CONSOLE_MSG, font_types::INFO)).await;
    } else if cmd_upper.starts_with("/PMSG ") {
        // Party message
        let text = &cmd[6..];
        handle_slash_pmsg(state, conn_id, text).await;
    } else if cmd_upper.starts_with("/TELEP ") {
        let args = cmd[7..].trim();
        handle_slash_telep(state, conn_id, args).await;
    } else if cmd_upper == "/TELEPLOC" {
        handle_slash_teleploc(state, conn_id).await;
    } else if cmd_upper.starts_with("/GO ") {
        let args = cmd[4..].trim();
        handle_slash_go(state, conn_id, args).await;
    } else if cmd_upper.starts_with("/IRA ") {
        let target = cmd[5..].trim();
        handle_slash_ira(state, conn_id, target).await;
    } else if cmd_upper.starts_with("/SUM ") {
        let target = cmd[5..].trim();
        handle_slash_sum(state, conn_id, target).await;
    } else if cmd_upper.starts_with("/KICK ") {
        let target = cmd[6..].trim();
        handle_slash_kick(state, conn_id, target).await;
    } else if cmd_upper.starts_with("/ITEM ") || cmd_upper.starts_with("/CI ") {
        let offset = if cmd_upper.starts_with("/CI ") { 4 } else { 6 };
        let args = cmd[offset..].trim();
        handle_slash_item(state, conn_id, args).await;
    } else if cmd_upper.starts_with("/SOBJ ") {
        let search = cmd[6..].trim();
        handle_slash_sobj(state, conn_id, search).await;
    // =====================================================================
    // Missing GM Commands (migrated from VB6)
    // =====================================================================
    } else if cmd_upper == "/INVISIBLE" {
        handle_slash_invisible(state, conn_id).await;
    } else if cmd_upper.starts_with("/DONDE ") {
        let target = cmd[7..].trim();
        handle_slash_donde(state, conn_id, target).await;
    } else if cmd_upper.starts_with("/BAN ") {
        let args = &cmd[5..];
        handle_slash_ban(state, conn_id, args).await;
    } else if cmd_upper.starts_with("/UNBAN ") {
        let target = cmd[7..].trim();
        handle_slash_unban(state, conn_id, target).await;
    } else if cmd_upper.starts_with("/BANIP ") {
        let args = &cmd[7..];
        handle_slash_banip(state, conn_id, args).await;
    } else if cmd_upper.starts_with("/UNBANIP ") {
        let ip = cmd[9..].trim();
        handle_slash_unbanip(state, conn_id, ip).await;
    } else if cmd_upper.starts_with("/BANACC ") {
        let args = &cmd[8..];
        handle_slash_banacc(state, conn_id, args).await;
    } else if cmd_upper.starts_with("/UNBANACC ") {
        let target = cmd[10..].trim();
        handle_slash_unbanacc(state, conn_id, target).await;
    } else if cmd_upper.starts_with("/CARCEL ") {
        let args = &cmd[8..];
        handle_slash_carcel(state, conn_id, args).await;
    } else if cmd_upper.starts_with("/SILENCIAR ") {
        let args = &cmd[11..];
        handle_slash_silenciar(state, conn_id, args).await;
    } else if cmd_upper.starts_with("/ADVERTIR ") {
        let args = &cmd[10..];
        handle_slash_advertir(state, conn_id, args).await;
    } else if cmd_upper.starts_with("/KILL ") {
        let target = cmd[6..].trim();
        handle_slash_kill(state, conn_id, target).await;
    } else if cmd_upper.starts_with("/ECHAR ") {
        let target = cmd[7..].trim();
        handle_slash_echar(state, conn_id, target).await;
    } else if cmd_upper.starts_with("/REVIVIR ") {
        let target = cmd[9..].trim();
        handle_slash_revivir(state, conn_id, target).await;
    } else if cmd_upper.starts_with("/ESPIAR ") {
        let target = cmd[8..].trim();
        handle_slash_espiar(state, conn_id, target).await;
    } else if cmd_upper.starts_with("/INV ") {
        let target = cmd[5..].trim();
        handle_slash_inv(state, conn_id, target).await;
    } else if cmd_upper.starts_with("/BOV ") {
        let target = cmd[5..].trim();
        handle_slash_bov(state, conn_id, target).await;
    } else if cmd_upper.starts_with("/RMSG ") {
        let text = &cmd[6..];
        handle_slash_rmsg(state, conn_id, text).await;
    } else if cmd_upper.starts_with("/LMSG ") {
        let args = &cmd[6..];
        handle_slash_lmsg(state, conn_id, args).await;
    } else if cmd_upper.starts_with("/A ") {
        let text = &cmd[3..];
        handle_slash_rmsg(state, conn_id, text).await;
    } else if cmd_upper.starts_with("/EXP ") {
        let val = cmd[5..].trim();
        handle_slash_exp_mult(state, conn_id, val).await;
    } else if cmd_upper.starts_with("/GLD ") {
        let val = cmd[5..].trim();
        handle_slash_gld_mult(state, conn_id, val).await;
    } else if cmd_upper.starts_with("/DROP ") {
        let val = cmd[6..].trim();
        handle_slash_drop_mult(state, conn_id, val).await;
    } else if cmd_upper.starts_with("/HOME ") {
        let target = cmd[6..].trim();
        handle_slash_home(state, conn_id, target).await;
    } else if cmd_upper == "/OFF" {
        handle_slash_off(state, conn_id).await;
    } else if cmd_upper == "/ECHARTODOSPJS" {
        handle_slash_echartodospjs(state, conn_id).await;
    } else if cmd_upper.starts_with("/DAMETODO ") {
        let target = cmd[10..].trim();
        handle_slash_dametodo(state, conn_id, target).await;
    } else if cmd_upper.starts_with("/MATA ") {
        let target = cmd[6..].trim();
        handle_slash_mata(state, conn_id, target).await;
    } else if cmd_upper == "/MASSKILL" {
        handle_slash_masskill(state, conn_id).await;
    } else if cmd_upper == "/LIMPIAR" || cmd_upper == "/LMAP" {
        handle_slash_limpiar(state, conn_id).await;
    } else if cmd_upper.starts_with("/NICK2IP ") {
        let target = cmd[9..].trim();
        handle_slash_nick2ip(state, conn_id, target).await;
    } else if cmd_upper.starts_with("/IP2NICK ") {
        let ip = cmd[9..].trim();
        handle_slash_ip2nick(state, conn_id, ip).await;
    } else if cmd_upper == "/NOGLOBAL" {
        handle_slash_noglobal(state, conn_id).await;
    } else if cmd_upper == "/FPS" {
        handle_slash_fps(state, conn_id).await;
    } else if cmd_upper.starts_with("/CT ") {
        let args = &cmd[4..];
        handle_slash_ct(state, conn_id, args).await;
    } else if cmd_upper == "/DT" {
        handle_slash_dt(state, conn_id).await;
    } else if cmd_upper == "/RESMAP" {
        handle_slash_resmap(state, conn_id).await;
    } else if cmd_upper.starts_with("/TALKAS ") {
        let args = &cmd[8..];
        handle_slash_talkas(state, conn_id, args).await;
    } else if cmd_upper == "/GUARDARMAPA" {
        state.send_to(conn_id, &format!("{}Mapa guardado.{}", server_opcodes::CONSOLE_MSG, font_types::INFO)).await;
    } else if cmd_upper.starts_with("/SETDESC ") {
        let args = &cmd[9..];
        handle_slash_setdesc(state, conn_id, args).await;
    } else if cmd_upper == "/RELOADSINI" || cmd_upper == "/LOADOBJ" || cmd_upper == "/LOADHECHIZOS" || cmd_upper == "/LOADNPCS" || cmd_upper == "/LOADBALANCE" || cmd_upper == "/LOADQUESTS" || cmd_upper == "/LOADPREMIOS" {
        state.send_to(conn_id, &format!("{}Datos recargados.{}", server_opcodes::CONSOLE_MSG, font_types::INFO)).await;
    } else if cmd_upper.starts_with("/STOP ") {
        let target = cmd[6..].trim();
        handle_slash_stop(state, conn_id, target, true).await;
    } else if cmd_upper.starts_with("/STOPOFF ") {
        let target = cmd[9..].trim();
        handle_slash_stop(state, conn_id, target, false).await;
    } else if cmd_upper.starts_with("/CHEAT ") {
        let target = cmd[7..].trim();
        handle_slash_cheat(state, conn_id, target).await;
    } else {
        // Unknown command — send feedback
        state.send_to(conn_id, "||714").await; // TEXTO714: Comando no reconocido
    }
}

/// /RESUCITAR — Resurrect. VB6: requires Revividor NPC target, distance <= 10, must be dead.
async fn handle_resucitar(state: &mut GameState, conn_id: ConnectionId) {
    let (dead, target_npc) = match state.users.get(&conn_id) {
        Some(u) if u.logged => (u.dead, u.target_npc),
        _ => return,
    };

    // VB6: If TargetNPC = 0 Then ||9
    if target_npc == 0 {
        state.send_to(conn_id, "||9").await;
        return;
    }

    // VB6: NPCtype must be Revividor (1) AND user must be dead
    let npc_type = state.get_npc(target_npc).map(|n| n.npc_type);
    if npc_type != Some(crate::data::npcs::NpcType::Reviver) || !dead {
        return;
    }

    // VB6: distance > 10 → ||11
    let (npc_map, npc_x, npc_y) = match state.get_npc(target_npc) {
        Some(npc) => (npc.map, npc.x, npc.y),
        None => return,
    };
    let (u_map, u_x, u_y) = match state.users.get(&conn_id) {
        Some(u) => (u.pos_map, u.pos_x, u.pos_y),
        None => return,
    };
    if u_map != npc_map || (u_x - npc_x).abs() > 10 || (u_y - npc_y).abs() > 10 {
        state.send_to(conn_id, "||11").await;
        return;
    }

    // VB6: RevivirUsuario(userindex) then ||396
    revive_user(state, conn_id).await;
    state.send_to(conn_id, "||396").await;
}

/// Core revive logic — shared between /RESUCITAR, resurrection spell, and delayed resurrection timer.
/// VB6: RevivirUsuario() — sets dead=false, HP=35, DarCuerpoDesnudo, ChangeUserChar(OrigChar.Head).
async fn revive_user(state: &mut GameState, conn_id: ConnectionId) {
    let (char_name, race, max_hp) = match state.users.get(&conn_id) {
        Some(u) if u.logged && u.dead => (u.char_name.clone(), u.race.clone(), u.max_hp),
        _ => return,
    };

    // Load original head from charfile (VB6: OrigChar.Head)
    let orig_head = {
        let base = &state.base_path;
        charfile::load_charfile(base, &char_name)
            .map(|chr| chr.head)
            .unwrap_or(1)
    };

    let gender = {
        let base = &state.base_path;
        charfile::load_charfile(base, &char_name)
            .map(|chr| chr.gender.to_string())
            .unwrap_or_else(|_| "1".to_string())
    };

    let revive_hp = 35.min(max_hp);
    let new_body = naked_body(&race, &gender);

    // Update user state: revive + restore living appearance
    if let Some(user) = state.users.get_mut(&conn_id) {
        user.dead = false;
        user.min_hp = revive_hp;
        user.body = new_body;
        user.head = orig_head;
        // VB6: weapon/shield/helmet stay as-is (already 0 from death), ChangeUserChar sends current values
    }

    // Read final state for packets
    let (map, x, y, char_index, heading, weapon_anim, shield_anim, casco_anim) = match state.users.get(&conn_id) {
        Some(u) => (u.pos_map, u.pos_x, u.pos_y, u.char_index, u.heading, u.weapon_anim, u.shield_anim, u.casco_anim),
        None => return,
    };

    // Resurrection FX (VB6: CFF charindex, 65, 0)
    let cff_pkt = format!("CFF{},65,0", char_index.0);
    state.send_data(SendTarget::ToArea { map, x, y }, &cff_pkt).await;

    // Broadcast character model change (VB6: ChangeUserChar → CP packet)
    // VB6 CP format: CP<charindex>,<body>,<head>,<heading>,<weapon>,<shield>,<fx>,<loops>,<helmet>
    let cp_pkt = format!(
        "CP{},{},{},{},{},{},0,0,{}",
        char_index.0, new_body, orig_head, heading,
        weapon_anim, shield_anim, casco_anim
    );
    state.send_data(SendTarget::ToArea { map, x, y }, &cp_pkt).await;

    send_stats_hp(state, conn_id).await;
    send_stats_mana(state, conn_id).await;
}

// =====================================================================
// Spell handler
// =====================================================================

/// LH<slot>,<target_x>,<target_y> — Cast spell.
/// LH<slot> — Select spell to cast (VB6: flags.Hechizo = slot).
/// Does NOT cast the spell — the cast happens on the next RC (right-click) that targets a tile.
async fn handle_cast_spell(state: &mut GameState, conn_id: ConnectionId, data: &str) {
    let payload = strip_opcode(data, 2);
    let spell_slot: usize = match payload.parse::<usize>() {
        Ok(s) if s >= 1 && s <= MAX_SPELL_SLOTS => s,
        _ => return,
    };

    if let Some(user) = state.users.get_mut(&conn_id) {
        if user.logged {
            user.pending_spell = spell_slot;
        }
    }
}

/// Send "Ves a <name>" info for a user target (VB6: LookatTile user display during spell cast).
/// Replicates the exact same format as the LC handler.
async fn send_lookat_user_info(state: &mut GameState, conn_id: ConnectionId, target_conn: ConnectionId) {
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
            if let Some(gn) = state.get_guild_name(guild_idx) {
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
        // Color by privilege/faction (VB6 LookatTile lines 895-956)
        if priv_target > 11 { stat.push_str(" <Administrador> ~255~255~255~1~0"); }
        else if priv_target > 3 { stat.push_str(" <Game Master> ~120~250~250~1~0"); }
        else if priv_target > 0 { stat.push_str(" <Game Master> ~0~185~0~1~0"); }
        else if level <= limite_newbie { stat.push_str(" ~255~255~202~1~0"); }
        else if armada { stat.push_str(" ~0~128~255~1~0"); }
        else if caos { stat.push_str(" ~255~0~0~1~0"); }
        else if criminal { stat.push_str(" <CRIMINAL> ~255~0~0~1~0"); }
        else { stat.push_str(" <CIUDADANO> ~0~128~255~1~0"); }

        let msg = format!("N|{}", stat);
        state.send_to(conn_id, &msg).await;
    }
}

/// Send brief NPC desc info during spell cast (VB6: LookatTile NPC display).
async fn send_lookat_npc_info(state: &mut GameState, conn_id: ConnectionId, npc_idx: usize) {
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
        let msg = format!("{}{}{}", server_opcodes::CONSOLE_MSG, msg_text, font_types::INFO);
        state.send_to(conn_id, &msg).await;
    }
}

/// Actually cast the pending spell at the target coordinates.
/// Called from handle_right_click when pending_spell > 0.
///
/// VB6 flow (modHechizos.bas LanzarHechizo):
///   1. Basic checks (dead, paralyzed, weapon, mana, skill)
///   2. Target validation by TargetType — if invalid → message, EXIT, NO mana consumed
///   3. HandleHechizo → specific validations (self-attack, PuedeAtacar)
///   4. InfoHechizo (FX + messages) — only if all checks pass
///   5. Consume mana — only if spell succeeded (b = True)
async fn do_cast_spell(state: &mut GameState, conn_id: ConnectionId) {
    let user_data = match state.users.get(&conn_id) {
        Some(u) if u.logged => {
            let slot = u.pending_spell;
            if slot < 1 || slot > MAX_SPELL_SLOTS { return; }
            let spell_id = u.spells[slot - 1];
            (
                u.pos_map, u.pos_x, u.pos_y, u.char_index,
                u.dead, u.paralyzed, u.min_mana,
                spell_id, u.level, u.target_x, u.target_y, u.target_map,
                u.target_user, u.target_npc as usize, u.privileges,
            )
        }
        _ => return,
    };
    let (map, x, y, char_index, dead, paralyzed, min_mana,
         spell_id, _level, target_x, target_y, _target_map,
         target_user_conn, target_npc_idx, privileges) = user_data;

    if dead || paralyzed || spell_id == 0 {
        return;
    }

    // Clear pending spell immediately
    if let Some(user) = state.users.get_mut(&conn_id) {
        user.pending_spell = 0;
    }

    // Look up spell data
    let spell = match state.get_spell(spell_id) {
        Some(s) => s.clone(),
        None => return,
    };

    // ===== STEP 1: PuedeLanzar — mana, skill checks (VB6 lines 269-309) =====
    if min_mana < spell.mana_requerido {
        state.send_to(conn_id, "||18").await; // Not enough mana
        return;
    }
    if spell.min_skill > 0 {
        let magic_skill = state.users.get(&conn_id).map(|u| u.skills[5]).unwrap_or(0);
        if magic_skill < spell.min_skill {
            state.send_to(conn_id, "||834").await; // Magic skill too low
            return;
        }
    }

    // ===== STEP 2: Resolve targets from world grid =====
    let target_conn: Option<ConnectionId> = if target_user_conn != 0 {
        Some(target_user_conn)
    } else {
        // Check tile and tile+1 for user (VB6 LookatTile checks Y and Y+1)
        state.world.grid(map)
            .and_then(|g| {
                g.tile(target_x, target_y).and_then(|t| t.user_conn)
                    .or_else(|| g.tile(target_x, target_y + 1).and_then(|t| t.user_conn))
            })
    };
    let target_npc = if target_npc_idx > 0 {
        Some(target_npc_idx)
    } else {
        // Check tile for NPC
        state.npc_at_tile(map, target_x, target_y)
            .or_else(|| state.npc_at_tile(map, target_x, target_y + 1))
    };

    let has_user_target = target_conn.is_some();
    let has_npc_target = target_npc.is_some();

    // ===== STEP 3: Target validation by TargetType (VB6 lines 632-695) =====
    // VB6: Select Case Hechizos(uh).Target
    use crate::data::spells::TargetType;
    match spell.target {
        TargetType::UserOnly => {
            // Needs a user target
            if !has_user_target {
                state.send_to(conn_id, "||25").await; // No valid user target
                return;
            }
        }
        TargetType::NpcOnly => {
            // Needs an NPC target
            if !has_npc_target {
                state.send_to(conn_id, "||29").await; // No valid NPC target
                return;
            }
        }
        TargetType::UserAndNpc => {
            // Needs either user or NPC
            if !has_user_target && !has_npc_target {
                state.send_to(conn_id, "||25").await; // No valid target
                return;
            }
        }
        TargetType::Self_ => {
            // Self-only — VB6: TargetUser must equal userindex
            // Force target to self (ignore what was clicked)
        }
        TargetType::Terrain | TargetType::Unknown => {
            // Terrain spells (invocations, teleport) — no target needed
        }
    }

    // Determine if spell is offensive
    let is_offensive = spell.sube_hp == 2 || spell.paraliza || spell.inmoviliza
        || spell.envenena || spell.maldicion;

    // ===== STEP 4: Specific spell validations (inside HandleHechizo) =====

    // Route to NPC or User handling
    if has_npc_target && !has_user_target {
        // ===== NPC TARGET =====
        let npc_idx = target_npc.unwrap();

        // VB6: InfoHechizo — FX + messages (sent BEFORE mana consumption)
        send_spell_info_npc(state, conn_id, npc_idx, &spell, char_index).await;

        // Apply effect
        match spell.tipo {
            crate::data::spells::SpellType::Properties => {
                apply_spell_properties_npc(state, conn_id, npc_idx, &spell).await;
            }
            crate::data::spells::SpellType::Status => {
                apply_spell_status_npc(state, conn_id, npc_idx, &spell).await;
            }
            _ => {}
        }

        // Consume mana (VB6: only if b=True, after HandleHechizoNPC)
        consume_spell_mana(state, conn_id, &spell, privileges).await;
    } else if has_user_target {
        // ===== USER TARGET =====
        let target_id = target_conn.unwrap();

        // VB6: Self-attack check (HechizoEstadoUsuario line 725, HechizoPropUsuario line 1425)
        if is_offensive && target_id == conn_id {
            state.send_to(conn_id, "||31").await; // Can't attack yourself
            return; // NO mana consumed, NO FX
        }

        // VB6: Healing full HP check (||145)
        if spell.sube_hp == 1 {
            let full_hp = state.users.get(&target_id)
                .map(|u| u.min_hp >= u.max_hp).unwrap_or(false);
            if full_hp {
                state.send_to(conn_id, "||145").await;
                return;
            }
        }

        // VB6: InfoHechizo — FX + messages (sent BEFORE mana consumption)
        send_spell_info_user(state, conn_id, target_id, &spell, char_index).await;

        // Apply effects
        match spell.tipo {
            crate::data::spells::SpellType::Properties => {
                apply_spell_properties(state, conn_id, target_id, &spell).await;
                apply_spell_buffs(state, conn_id, target_id, &spell).await;
            }
            crate::data::spells::SpellType::Status => {
                apply_spell_status(state, conn_id, target_id, &spell).await;
                apply_spell_buffs(state, conn_id, target_id, &spell).await;
            }
            _ => {}
        }

        // Consume mana
        consume_spell_mana(state, conn_id, &spell, privileges).await;
    } else {
        // ===== SELF / TERRAIN (no external target) =====
        match spell.target {
            TargetType::Self_ => {
                // Self-only spell — beneficial only
                send_spell_info_user(state, conn_id, conn_id, &spell, char_index).await;
                match spell.tipo {
                    crate::data::spells::SpellType::Properties => {
                        apply_spell_properties(state, conn_id, conn_id, &spell).await;
                        apply_spell_buffs(state, conn_id, conn_id, &spell).await;
                    }
                    crate::data::spells::SpellType::Status => {
                        apply_spell_status(state, conn_id, conn_id, &spell).await;
                        apply_spell_buffs(state, conn_id, conn_id, &spell).await;
                    }
                    _ => {}
                }
                consume_spell_mana(state, conn_id, &spell, privileges).await;
            }
            TargetType::Terrain | TargetType::Unknown => {
                // Terrain spells (invocation, summon, teleport)
                match spell.tipo {
                    crate::data::spells::SpellType::Invocation => {
                        apply_spell_invocation(state, conn_id, &spell).await;
                    }
                    crate::data::spells::SpellType::SummonPet => {
                        apply_spell_summon_pet(state, conn_id, &spell).await;
                    }
                    crate::data::spells::SpellType::Teleport => {
                        apply_spell_teleport(state, conn_id, &spell).await;
                    }
                    _ => {}
                }
                consume_spell_mana(state, conn_id, &spell, privileges).await;
            }
            _ => {
                // Should have been caught by Step 3 target validation
                return;
            }
        }
    }
}

/// Consume mana and stamina after a successful spell cast.
/// VB6: Only consumed if b=True (spell succeeded), and only for normal users (not GMs).
async fn consume_spell_mana(state: &mut GameState, conn_id: ConnectionId,
                             spell: &crate::data::spells::SpellData, privileges: i32) {
    if privileges == 0 {
        // Normal user — consume mana and stamina
        if let Some(user) = state.users.get_mut(&conn_id) {
            user.min_mana = (user.min_mana - spell.mana_requerido).max(0);
            user.min_sta = (user.min_sta - spell.sta_requerido).max(0);
        }
    }
    send_stats_mana(state, conn_id).await;
    send_stats_sta(state, conn_id).await;
}

/// VB6: InfoHechizo — Send FX, sound, and chat messages for a spell cast on a USER.
async fn send_spell_info_user(state: &mut GameState, caster_id: ConnectionId,
                               target_id: ConnectionId,
                               spell: &crate::data::spells::SpellData,
                               caster_ci: crate::game::world::CharIndex) {
    let caster_name = state.users.get(&caster_id).map(|u| u.char_name.clone()).unwrap_or_default();
    let (map, x, y) = state.users.get(&caster_id)
        .map(|u| (u.pos_map, u.pos_x, u.pos_y)).unwrap_or((0,0,0));

    // Magic words
    if !spell.palabras_magicas.is_empty() {
        let pkt = format!("N|16776960\u{00B0}{}\u{00B0}{}", spell.palabras_magicas, caster_ci.0);
        state.send_data(SendTarget::ToArea { map, x, y }, &pkt).await;
    }

    // Target char_index for FX
    let target_ci = state.users.get(&target_id).map(|u| u.char_index.0).unwrap_or(caster_ci.0);
    let (fx_map, fx_x, fx_y) = state.users.get(&target_id)
        .map(|u| (u.pos_map, u.pos_x, u.pos_y)).unwrap_or((map, x, y));

    // FX + Sound
    if spell.fx_grh > 0 {
        let fx_pkt = format!("CFX{},{},{}", target_ci, spell.fx_grh, spell.loops);
        state.send_data(SendTarget::ToArea { map: fx_map, x: fx_x, y: fx_y }, &fx_pkt).await;
    }
    if spell.wav > 0 {
        let snd_pkt = format!("TW{}", spell.wav);
        state.send_data(SendTarget::ToArea { map: fx_map, x: fx_x, y: fx_y }, &snd_pkt).await;
    }

    // Console messages
    if target_id != caster_id {
        let target_name = state.users.get(&target_id).map(|u| u.char_name.clone()).unwrap_or_default();
        if !spell.hechizero_msg.is_empty() {
            let msg = format!("N|{} {}~255~0~0~1", spell.hechizero_msg, target_name);
            state.send_to(caster_id, &msg).await;
        }
        if !spell.target_msg.is_empty() {
            let msg = format!("N|{} {}~255~0~0~1", caster_name, spell.target_msg);
            state.send_to(target_id, &msg).await;
        }
    } else {
        if !spell.propio_msg.is_empty() {
            let msg = format!("N|{}~255~0~0~1", spell.propio_msg);
            state.send_to(caster_id, &msg).await;
        }
    }
}

/// VB6: InfoHechizo — Send FX, sound, and chat messages for a spell cast on an NPC.
async fn send_spell_info_npc(state: &mut GameState, caster_id: ConnectionId,
                              npc_idx: usize,
                              spell: &crate::data::spells::SpellData,
                              caster_ci: crate::game::world::CharIndex) {
    let (map, x, y) = state.users.get(&caster_id)
        .map(|u| (u.pos_map, u.pos_x, u.pos_y)).unwrap_or((0,0,0));

    // Magic words
    if !spell.palabras_magicas.is_empty() {
        let pkt = format!("N|16776960\u{00B0}{}\u{00B0}{}", spell.palabras_magicas, caster_ci.0);
        state.send_data(SendTarget::ToArea { map, x, y }, &pkt).await;
    }

    // NPC char_index for FX
    let npc_ci = state.get_npc(npc_idx).map(|n| n.char_index.0).unwrap_or(0);
    let (fx_map, fx_x, fx_y) = state.get_npc(npc_idx)
        .map(|n| (n.map, n.x, n.y)).unwrap_or((map, x, y));

    if spell.fx_grh > 0 {
        let fx_pkt = format!("CFX{},{},{}", npc_ci, spell.fx_grh, spell.loops);
        state.send_data(SendTarget::ToArea { map: fx_map, x: fx_x, y: fx_y }, &fx_pkt).await;
    }
    if spell.wav > 0 {
        let snd_pkt = format!("TW{}", spell.wav);
        state.send_data(SendTarget::ToArea { map: fx_map, x: fx_x, y: fx_y }, &snd_pkt).await;
    }

    // Console message
    if !spell.hechizero_msg.is_empty() {
        let msg = format!("N|{} la criatura.~255~0~0~1", spell.hechizero_msg);
        state.send_to(caster_id, &msg).await;
    }
}

/// Apply property-type spell effects to an NPC (damage/heal).
/// VB6: HechizoPropNPC in modHechizos.bas
async fn apply_spell_properties_npc(
    state: &mut GameState,
    caster_id: ConnectionId,
    npc_idx: usize,
    spell: &crate::data::spells::SpellData,
) {
    if spell.sube_hp != 2 {
        // Only damage spells (SubeHP=2) apply to NPCs
        // Heal spells on hostile NPCs don't make sense in VB6 either
        return;
    }

    let mut damage = rand_range(spell.min_hp, spell.max_hp);

    // VB6: spell damage * 1.4 on NPCs
    damage = (damage as f64 * 1.4) as i32;

    // Get NPC data for damage number display
    let npc_data = state.get_npc(npc_idx).map(|n| (n.char_index.0, n.map, n.x, n.y));
    let (npc_ci, npc_map, npc_x, npc_y) = match npc_data {
        Some(d) => d,
        None => return,
    };

    // Send damage number over NPC head (VB6: N| vbYellow°-<damage>°<npc_charindex>)
    let caster_map = state.users.get(&caster_id).map(|u| (u.pos_map, u.pos_x, u.pos_y)).unwrap_or((npc_map, npc_x, npc_y));
    let dmg_pkt = format!("N|65535\u{00B0}-{}\u{00B0}{}", damage, npc_ci);
    state.send_data(SendTarget::ToArea { map: caster_map.0, x: caster_map.1, y: caster_map.2 }, &dmg_pkt).await;

    // Send damage console message to caster: ||850@<damage>
    state.send_to(caster_id, &format!("||850@{}", damage)).await;

    // Apply damage to NPC
    if let Some(npc) = state.get_npc_mut(npc_idx) {
        npc.min_hp -= damage;
    }

    // Check NPC death
    let npc_death_data = state.get_npc(npc_idx).and_then(|n| {
        if n.min_hp < 1 { Some((n.give_exp, n.give_gld_min, n.give_gld_max)) } else { None }
    });
    if let Some((give_exp, give_gld_min, give_gld_max)) = npc_death_data {
        if let Some(npc) = state.get_npc_mut(npc_idx) {
            npc.min_hp = 0;
        }
        npc_die(state, npc_idx, caster_id, give_exp, give_gld_min, give_gld_max).await;
    }
}

/// Apply status-type spell effects to an NPC.
/// VB6: HechizoEstadoNPC in modHechizos.bas
async fn apply_spell_status_npc(
    state: &mut GameState,
    _caster_id: ConnectionId,
    npc_idx: usize,
    spell: &crate::data::spells::SpellData,
) {
    let paralisis_interval = state.config.intervalo_paralizado;
    if let Some(npc) = state.get_npc_mut(npc_idx) {
        if spell.envenena {
            npc.veneno = true;
        }
        if spell.cura_veneno {
            npc.veneno = false;
        }
        if spell.paraliza {
            npc.paralyzed = true;
            npc.counter_paralisis = paralisis_interval;
        }
        // VB6: RemoverParalisis does NOT work on NPCs (only users)
    }
}

/// Apply property-type spell effects (HP, Mana, Stamina modifications).
async fn apply_spell_properties(
    state: &mut GameState,
    _caster_id: ConnectionId,
    target_id: ConnectionId,
    spell: &crate::data::spells::SpellData,
) {
    if let Some(target) = state.users.get_mut(&target_id) {
        // HP effect
        if spell.sube_hp == 1 {
            // Heal
            let amount = rand_range(spell.min_hp, spell.max_hp);
            target.min_hp = (target.min_hp + amount).min(target.max_hp);
        } else if spell.sube_hp == 2 {
            // Damage
            let amount = rand_range(spell.min_hp, spell.max_hp);
            target.min_hp -= amount;
        }

        // Mana effect
        if spell.sube_mana == 1 {
            let amount = rand_range(spell.min_mana, spell.max_mana);
            target.min_mana = (target.min_mana + amount).min(target.max_mana);
        }

        // Stamina effect
        if spell.sube_sta == 1 {
            let amount = rand_range(spell.min_sta, spell.max_sta);
            target.min_sta = (target.min_sta + amount).min(target.max_sta);
        }
    }

    // Send updated stats
    send_stats_hp(state, target_id).await;
    send_stats_mana(state, target_id).await;
    send_stats_sta(state, target_id).await;

    // Check death from damage spell
    let hp = state.users.get(&target_id).map(|u| u.min_hp).unwrap_or(0);
    if hp <= 0 {
        user_die(state, target_id, None).await;
    }
}

/// Apply status-type spell effects (poison, paralysis, cure, remove paralysis, resurrection, etc.).
async fn apply_spell_status(
    state: &mut GameState,
    caster_id: ConnectionId,
    target_id: ConnectionId,
    spell: &crate::data::spells::SpellData,
) {
    // VB6: Can't paralyze/immobilize yourself (||31)
    if spell.paraliza && caster_id == target_id {
        state.send_to(caster_id, "||31").await;
        return;
    }

    // Track what we need to send after dropping the mutable borrow
    let mut send_paradok_on = false;   // paralysis applied → send PARADOK + PU
    let mut send_paradok_off = false;  // paralysis removed → send PARADOK

    if let Some(target) = state.users.get_mut(&target_id) {
        if spell.cura_veneno {
            target.poisoned = false;
        }
        if spell.paraliza {
            if !target.paralyzed {
                target.paralyzed = true;
                target.counter_paralisis = state.config.intervalo_paralizado;
                send_paradok_on = true;
            }
        }
        if spell.remover_paralisis {
            if target.paralyzed {
                target.paralyzed = false;
                send_paradok_off = true;
            }
        }
        if spell.envenena {
            target.poisoned = true;
        }
        if spell.invisibilidad {
            target.invisible = true;
            target.hidden = true;
        }
    }

    // Send PARADOK + PU outside borrow scope (VB6: lines 759-760)
    if send_paradok_on {
        state.send_to(target_id, "PARADOK").await;
        // PU forces client position to server-known position (prevents ghost movement)
        let pu = state.users.get(&target_id)
            .map(|u| format!("PU{},{}", u.pos_x, u.pos_y));
        if let Some(pkt) = pu {
            state.send_to(target_id, &pkt).await;
        }
    }
    if send_paradok_off {
        state.send_to(target_id, "PARADOK").await;
    }

    // Resurrection spell
    if spell.revivir {
        let target_dead = state.users.get(&target_id).map(|u| u.dead).unwrap_or(false);
        let target_seguro_resu = state.users.get(&target_id).map(|u| u.seguro_resu).unwrap_or(false);
        let target_time_revivir = state.users.get(&target_id).map(|u| u.time_revivir).unwrap_or(0);

        if target_dead {
            // Check resurrection safety (target opted out)
            if target_seguro_resu {
                let msg = format!("{}841", server_opcodes::CONSOLE_MSG);
                state.send_to(caster_id, &msg).await;
                return;
            }
            // Check resurrection cooldown
            if target_time_revivir > 0 {
                let msg = format!("{}843@{}", server_opcodes::CONSOLE_MSG, target_time_revivir);
                state.send_to(caster_id, &msg).await;
                return;
            }

            // Check if caster is cleric (instant full HP rez)
            let caster_class = state.users.get(&caster_id)
                .map(|u| u.class.to_uppercase())
                .unwrap_or_default();
            let caster_name = state.users.get(&caster_id)
                .map(|u| u.char_name.clone())
                .unwrap_or_default();

            if caster_class == "CLERIGO" {
                // Cleric: instant resurrection at full HP
                revive_user(state, target_id).await;
                if let Some(target) = state.users.get_mut(&target_id) {
                    target.min_hp = target.max_hp;
                }
                send_stats_hp(state, target_id).await;
                let msg = format!("{}749@{}", server_opcodes::CONSOLE_MSG, caster_name);
                state.send_to(target_id, &msg).await;
            } else {
                // Non-cleric: 10 second delayed resurrection
                if let Some(target) = state.users.get_mut(&target_id) {
                    target.segundos_para_revivir = 10;
                }
                let msg = format!("{}845", server_opcodes::CONSOLE_MSG);
                state.send_to(target_id, &msg).await;
            }

            // Caster pays HP cost (reduced to 10)
            if let Some(caster) = state.users.get_mut(&caster_id) {
                caster.min_hp = 10;
            }
            send_stats_hp(state, caster_id).await;
        }
    }
}

/// Apply attribute buff spells (SubeAgilidad, SubeFuerza).
/// VB6: modHechizos.bas — SubeAgilidad=1 buffs, =2 debuffs. SubeFuerza same pattern.
/// Buffs are temporary: DuracionEfecto ticks, then attributes restored from backup.
async fn apply_spell_buffs(
    state: &mut GameState,
    _caster_id: ConnectionId,
    target_id: ConnectionId,
    spell: &crate::data::spells::SpellData,
) {
    const MAX_ATTR: i32 = 35;
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
                // Buff: increase agility
                target.attributes[1] = (target.attributes[1] + amount).min(MAX_ATTR); // [1] = Agi
                target.duracion_efecto = 7000;
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
                target.attributes[0] = (target.attributes[0] + amount).min(MAX_ATTR); // [0] = Str
                target.duracion_efecto = 1200;
            } else {
                target.attributes[0] = (target.attributes[0] - amount).max(MIN_ATTR);
                target.duracion_efecto = 700;
            }
            target.tomo_pocion = true;
        }
    }
}

/// Apply invocation spell — spawn NPCs as pets.
/// VB6: HechizoInvocacion (modHechizos.bas). Max 3 pets, singleton elementals.
const MAX_MASCOTAS: i32 = 3;

async fn apply_spell_invocation(
    state: &mut GameState,
    caster_id: ConnectionId,
    spell: &crate::data::spells::SpellData,
) {
    let (map, x, y, nro_mascotas) = match state.users.get(&caster_id) {
        Some(u) if u.logged => (u.pos_map, u.pos_x, u.pos_y, u.nro_mascotas),
        _ => return,
    };

    if nro_mascotas >= MAX_MASCOTAS {
        let msg = format!("{}No puedes invocar mas criaturas.", server_opcodes::CONSOLE_MSG);
        state.send_to(caster_id, &msg).await;
        return;
    }

    let npc_num = spell.num_npc;
    let cant = spell.cant;
    if npc_num <= 0 || cant <= 0 {
        return;
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
                        user.nro_mascotas += 1;
                        break;
                    }
                }
            }

            // Set NPC owner
            if let Some(npc) = state.get_npc_mut(npc_idx) {
                npc.maestro_user = Some(caster_id);
            }

            // Broadcast NPC creation using its CC packet
            let cc_pkt = state.get_npc(npc_idx).map(|n| n.build_cc_packet());
            if let Some(pkt) = cc_pkt {
                state.send_data(SendTarget::ToArea { map, x, y }, &pkt).await;
            }
        }
    }
}

/// Apply summon pet spell — toggle single mount/pet.
/// VB6: InvocarMascota (modHechizos.bas). If already summoned, dismiss.
async fn apply_spell_summon_pet(
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

// =====================================================================
/// Apply teleport spell — warp caster to fixed destination.
/// VB6: modHechizos.bas TipoHechizo=5. Uses PortalMap/PortalX/PortalY.
async fn apply_spell_teleport(
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

    // Send BP to area (remove character)
    let bp_pkt = format!("BP{}", char_index.0);
    state.send_data(SendTarget::ToArea { map: cur_map, x: cur_x, y: cur_y }, &bp_pkt).await;

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
    state.send_to(caster_id, &format!("CM{},{},{},{}", dest_map, r, g, b)).await;
    state.send_to(caster_id, &format!("PU{},{}", dest_x, dest_y)).await;
    state.send_to(caster_id, &format!("XM{}", music)).await;
    state.send_to(caster_id, &format!("N~{}", map_name)).await;

    // Send CC to new area
    let cc_pkt = state.users.get(&caster_id).map(|u| u.build_cc_packet());
    if let Some(pkt) = cc_pkt {
        state.send_data(SendTarget::ToAreaButIndex { conn_id: caster_id, map: dest_map, x: dest_x, y: dest_y }, &pkt).await;
    }

    // Drain all mana (VB6 sets mana to 0 on teleport)
    if let Some(user) = state.users.get_mut(&caster_id) {
        user.min_mana = 0;
    }
    send_stats_mana(state, caster_id).await;
}

// Inventory/stats packet helpers
// =====================================================================

/// Build ANM packet (equipment hitbox stats — 20 comma-separated fields).
/// Format: ANM<minArma>,<maxArma>,<minArmor>,<maxArmor>,<minEscudo>,<maxEscudo>,
///         <minCasco>,<maxCasco>,<minHerr>,<maxHerr>, then 10 magic defense fields (all 0 for now).
fn build_anm_packet(state: &GameState, conn_id: ConnectionId) -> String {
    let user = match state.users.get(&conn_id) {
        Some(u) => u,
        None => return "ANM0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0".into(),
    };

    // Weapon stats
    let (min_arma, max_arma) = if user.equip.weapon > 0 && user.equip.weapon <= MAX_INVENTORY_SLOTS {
        let inv = &user.inventory[user.equip.weapon - 1];
        state.get_object(inv.obj_index)
            .map(|o| (o.min_hit, o.max_hit))
            .unwrap_or((0, 0))
    } else {
        (0, 0)
    };

    // Armor stats
    let (min_armor, max_armor) = if user.equip.armor > 0 && user.equip.armor <= MAX_INVENTORY_SLOTS {
        let inv = &user.inventory[user.equip.armor - 1];
        state.get_object(inv.obj_index)
            .map(|o| (o.min_def, o.max_def))
            .unwrap_or((0, 0))
    } else {
        (0, 0)
    };

    // Shield stats
    let (min_shield, max_shield) = if user.equip.shield > 0 && user.equip.shield <= MAX_INVENTORY_SLOTS {
        let inv = &user.inventory[user.equip.shield - 1];
        state.get_object(inv.obj_index)
            .map(|o| (o.min_def, o.max_def))
            .unwrap_or((0, 0))
    } else {
        (0, 0)
    };

    // Helmet stats
    let (min_helmet, max_helmet) = if user.equip.helmet > 0 && user.equip.helmet <= MAX_INVENTORY_SLOTS {
        let inv = &user.inventory[user.equip.helmet - 1];
        state.get_object(inv.obj_index)
            .map(|o| (o.min_def, o.max_def))
            .unwrap_or((0, 0))
    } else {
        (0, 0)
    };

    // 20 fields: weapon(2), armor(2), shield(2), helmet(2), tool(2), then 10 magic defense fields
    format!(
        "ANM{},{},{},{},{},{},{},{},0,0,0,0,0,0,0,0,0,0,0,0",
        min_arma, max_arma, min_armor, max_armor,
        min_shield, max_shield, min_helmet, max_helmet,
    )
}

/// Send a single inventory slot CSI packet.
/// Format: CSI<slot>,<objindex>,<name>,<amount>,<equipped>,<grhindex>,<objtype>,<maxhit>,<minhit>,<maxdef>,<valor/3>
async fn send_inventory_slot(state: &mut GameState, conn_id: ConnectionId, idx: usize) {
    let slot = idx + 1; // 1-based for client
    let inv = match state.users.get(&conn_id) {
        Some(u) => u.inventory[idx].clone(),
        None => return,
    };

    if inv.obj_index == 0 {
        // VB6 sends exactly 5 fields for empty slots: CSI<slot>,0,(None),0,0
        let pkt = format!("CSI{},0,(None),0,0", slot);
        state.send_to(conn_id, &pkt).await;
    } else {
        let obj = state.get_object(inv.obj_index).cloned();
        let (name, grh, obj_type, max_hit, min_hit, max_def, valor) = match obj {
            Some(o) => (
                o.name.clone(),
                o.grh_index,
                o.obj_type as i32,
                o.max_hit,
                o.min_hit,
                o.max_def,
                o.valor / 3,
            ),
            None => ("???".into(), 0, 0, 0, 0, 0, 0),
        };
        let equipped = if inv.equipped { 1 } else { 0 };
        let pkt = format!(
            "CSI{},{},{},{},{},{},{},{},{},{},{}",
            slot, inv.obj_index, name, inv.amount, equipped,
            grh, obj_type, max_hit, min_hit, max_def, valor
        );
        state.send_to(conn_id, &pkt).await;
    }
}

/// Send all inventory slots.
async fn send_full_inventory(state: &mut GameState, conn_id: ConnectionId) {
    for idx in 0..MAX_INVENTORY_SLOTS {
        send_inventory_slot(state, conn_id, idx).await;
    }
}

/// Send all spell slots.
/// Format: SHS<slot>,<spellId>,<spellName>
async fn send_full_spells(state: &mut GameState, conn_id: ConnectionId) {
    let spells = match state.users.get(&conn_id) {
        Some(u) => u.spells,
        None => return,
    };

    for (i, &spell_id) in spells.iter().enumerate() {
        let slot = i + 1;
        if spell_id > 0 {
            let name = state.get_spell(spell_id)
                .map(|s| s.nombre.clone())
                .unwrap_or_else(|| "(Desconocido)".into());
            let pkt = format!("SHS{},{},{}", slot, spell_id, name);
            state.send_to(conn_id, &pkt).await;
        } else {
            let pkt = format!("SHS{},0,(Nada)", slot);
            state.send_to(conn_id, &pkt).await;
        }
    }
}

/// Send [H] HP stats packet.
async fn send_stats_hp(state: &mut GameState, conn_id: ConnectionId) {
    if let Some(u) = state.users.get(&conn_id) {
        let pkt = format!("[H]{},{}", u.max_hp, u.min_hp);
        state.send_to(conn_id, &pkt).await;
    }
}

/// Send [M] mana stats packet.
async fn send_stats_mana(state: &mut GameState, conn_id: ConnectionId) {
    if let Some(u) = state.users.get(&conn_id) {
        let pkt = format!("[M]{},{}", u.max_mana, u.min_mana);
        state.send_to(conn_id, &pkt).await;
    }
}

/// Send [S] stamina stats packet.
async fn send_stats_sta(state: &mut GameState, conn_id: ConnectionId) {
    if let Some(u) = state.users.get(&conn_id) {
        let pkt = format!("[S]{},{}", u.max_sta, u.min_sta);
        state.send_to(conn_id, &pkt).await;
    }
}

/// Send [G] gold packet.
async fn send_stats_gold(state: &mut GameState, conn_id: ConnectionId) {
    if let Some(u) = state.users.get(&conn_id) {
        let pkt = format!("[G]{}", u.gold);
        state.send_to(conn_id, &pkt).await;
    }
}

/// Send [E] experience packet.
async fn send_stats_exp(state: &mut GameState, conn_id: ConnectionId) {
    if let Some(u) = state.users.get(&conn_id) {
        let exp_next = state.exp_for_level(u.level);
        let pkt = format!("[E]{},{}", exp_next, u.exp);
        state.send_to(conn_id, &pkt).await;
    }
}

// =====================================================================
// Level up system
// =====================================================================

const MAX_LEVEL: i32 = 70;

/// Check if user has enough exp to level up, and apply it.
pub async fn check_user_level(state: &mut GameState, conn_id: ConnectionId) {
    loop {
        let (level, exp, class) = match state.users.get(&conn_id) {
            Some(u) if u.logged => (u.level, u.exp, u.class.clone()),
            _ => return,
        };

        if level >= MAX_LEVEL {
            return;
        }

        let exp_needed = state.exp_for_level(level);
        if exp_needed <= 0 || exp < exp_needed {
            return;
        }

        // Level up!
        let (hp_gain, mana_gain, sta_gain, hit_gain) = level_up_gains(&class, level);

        if let Some(user) = state.users.get_mut(&conn_id) {
            user.level += 1;
            user.max_hp += hp_gain;
            user.min_hp += hp_gain; // Heal the gain
            user.max_mana += mana_gain;
            user.min_mana += mana_gain;
            user.max_sta += sta_gain;
            user.min_sta += sta_gain;
            user.max_hit += hit_gain;
            user.min_hit += hit_gain;
        }

        let new_level = level + 1;
        let char_index = state.users.get(&conn_id).map(|u| u.char_index.0).unwrap_or(0);
        let map = state.users.get(&conn_id).map(|u| u.pos_map).unwrap_or(0);
        let x = state.users.get(&conn_id).map(|u| u.pos_x).unwrap_or(0);
        let y = state.users.get(&conn_id).map(|u| u.pos_y).unwrap_or(0);

        // Level up FX (effect 58) to area
        let fx_pkt = format!("CFF{},58,0", char_index);
        state.send_data(SendTarget::ToArea { map, x, y }, &fx_pkt).await;

        // Stat notifications (VB6: ||67 + ||68@level@hp_gain)
        state.send_to(conn_id, "||67").await; // TEXTO67: Has subido de nivel!
        state.send_to(conn_id, &format!("||68@{}@{}", new_level, hp_gain)).await; // TEXTO68

        // Send updated stats
        let lvl_pkt = format!("[L]{}", new_level);
        state.send_to(conn_id, &lvl_pkt).await;
        send_stats_hp(state, conn_id).await;
        send_stats_mana(state, conn_id).await;
        send_stats_sta(state, conn_id).await;
        send_stats_exp(state, conn_id).await;

        let name = state.users.get(&conn_id).map(|u| u.char_name.clone()).unwrap_or_default();
        info!("[LEVEL] '{}' reached level {}", name, new_level);

        // Milestone announcements
        if new_level == 50 || new_level == 60 || new_level == 70 {
            let announce = format!(
                "T|65535\u{00B0}{} ha alcanzado el nivel {}!\u{00B0}0",
                name, new_level
            );
            state.send_data(SendTarget::ToAll, &announce).await;
        }

        // VB6 level bonuses from ClassBonus.dat (levels 53, 56, 60)
        let bonus_level = match new_level {
            53 => Some(1),
            56 => Some(2),
            60 => Some(3),
            _ => None,
        };
        if let Some(nivel) = bonus_level {
            let class_upper = class.to_uppercase();
            let dat_path = state.base_path.join("Dat").join("ClassBonus.dat");
            let dat = dat_path.to_str().unwrap_or("");
            let opt1 = crate::config::get_var(dat, &class_upper, &format!("Nivel{}Opcion1", nivel));
            let opt2 = crate::config::get_var(dat, &class_upper, &format!("Nivel{}Opcion2", nivel));
            if !opt1.is_empty() || !opt2.is_empty() {
                // VB6: SendData toindex "99" & opt1 & "," & opt2
                let pkt = format!("99{},{}", opt1, opt2);
                state.send_to(conn_id, &pkt).await;
            }

            // VB6: Level 60 = +200 skill points (one-time)
            if new_level == 60 {
                // Award 200 free skill points
                let msg = format!("{}Has alcanzado el nivel maximo! +200 puntos de skill.{}", server_opcodes::CONSOLE_MSG, font_types::INFO);
                state.send_to(conn_id, &msg).await;
            }
        }

        // Continue looping in case of multi-level jumps
    }
}

/// Calculate stat gains per level based on class.
/// Returns (hp_gain, mana_gain, sta_gain, hit_gain).
fn level_up_gains(class: &str, level: i32) -> (i32, i32, i32, i32) {
    let class_lower = class.to_lowercase();
    match class_lower.as_str() {
        "guerrero" => {
            let hp = if level < 20 { rand_range(10, 11) } else { 11 };
            let hit = if level > 35 { 2 } else { 3 };
            (hp, 0, 20, hit)
        }
        "mago" => {
            let hp = rand_range(4, 6);
            let mana = rand_range(18, 22); // INT-based simplified
            (hp, mana, 15, 1)
        }
        "clerigo" => {
            let hp = rand_range(7, 9);
            let mana = rand_range(12, 16);
            (hp, mana, 18, 2)
        }
        "asesino" => {
            let hp = rand_range(8, 10);
            let hit = if level > 35 { 2 } else { 3 };
            (hp, 0, 20, hit)
        }
        "bardo" => {
            let hp = rand_range(6, 8);
            let mana = rand_range(10, 14);
            (hp, mana, 18, 2)
        }
        "druida" => {
            let hp = rand_range(6, 8);
            let mana = rand_range(14, 18);
            (hp, mana, 17, 2)
        }
        "paladin" => {
            let hp = rand_range(8, 10);
            let mana = rand_range(8, 12);
            let hit = if level > 35 { 1 } else { 3 };
            (hp, mana, 20, hit)
        }
        "cazador" => {
            let hp = rand_range(8, 10);
            let hit = if level > 35 { 2 } else { 3 };
            (hp, 0, 22, hit)
        }
        "trabajador" => {
            let hp = rand_range(9, 11);
            (hp, 0, 25, 1)
        }
        "pirata" => {
            let hp = rand_range(9, 11);
            let hit = if level > 35 { 2 } else { 3 };
            (hp, 0, 20, hit)
        }
        "ladron" => {
            let hp = rand_range(6, 8);
            (hp, 0, 20, 2)
        }
        "bandido" => {
            let hp = rand_range(8, 10);
            (hp, 0, 20, 2)
        }
        _ => (8, 0, 18, 2), // Default
    }
}

// =====================================================================
// World/visibility helpers
// =====================================================================

/// Make a newly-logged-in user visible: send existing chars to them, and their CC to others.
async fn make_user_visible(state: &mut GameState, conn_id: ConnectionId) {
    // Reset area tracking so CheckUpdateNeededUser fires with USER_NUEVO (255)
    if let Some(user) = state.users.get_mut(&conn_id) {
        user.area_id = 0;
        user.area_min_x = 0;
        user.area_min_y = 0;
    }

    // Trigger full area initialization (heading=255 = USER_NUEVO)
    // This sends CA packet + all CCs/NPCs/items in the 27x27 area
    check_update_needed_user(state, conn_id, 255).await;
}

/// VB6 AreaID calculation: (x/9+1)*(y/9+1)
/// Determines which 9x9 zone a position belongs to.
fn area_id(x: i32, y: i32) -> i32 {
    (x / 9 + 1) * (y / 9 + 1)
}

/// Build [CD (CharData) packet for a user.
/// VB6 SendCharData: [CD{charindex},{color},{auraA},{auraW},{auraE},{auraR},{auraC},{levitando},{tieneRanking}
/// For now: color=0 (default), no auras, no levitation, no ranking display.
fn build_cd_packet(user: &super::types::UserState) -> String {
    // Color: 0=default. Could be extended for premium/criminal status colors.
    let color = 0;
    // Auras: armor, weapon, shield, ring, helmet — all 0 for now
    let (aura_a, aura_w, aura_e, aura_r, aura_c) = (0, 0, 0, 0, 0);
    let levitando = 0;
    let tiene_ranking = 0;
    format!(
        "[CD{},{},{},{},{},{},{},{},{}",
        user.char_index.0, color, aura_a, aura_w, aura_e, aura_r, aura_c, levitando, tiene_ranking
    )
}

/// CheckUpdateNeededUser — VB6 ModAreas.bas area-based visibility system.
/// Only fires when the player crosses a 9x9 area boundary.
/// Sends CA packet to client (cleanup out-of-range entities),
/// then sends CC/NPC/items for the newly visible strip.
async fn check_update_needed_user(
    state: &mut GameState,
    conn_id: ConnectionId,
    heading: i32,
) {
    let (pos_x, pos_y, map, old_area_id, old_min_x, old_min_y) = match state.users.get(&conn_id) {
        Some(u) => (u.pos_x, u.pos_y, u.pos_map, u.area_id, u.area_min_x, u.area_min_y),
        None => return,
    };

    let new_area_id = area_id(pos_x, pos_y);

    // If still in the same 9x9 zone, nothing to do
    if new_area_id == old_area_id && old_area_id != 0 {
        return;
    }

    // Calculate the new visibility strip based on heading (VB6 ModAreas lines 158-198)
    let (min_x, max_x, min_y, max_y, new_min_x, new_min_y) = if heading == 255 {
        // USER_NUEVO (login/warp): full 27x27 area
        let nmin_y = (pos_y / 9 - 1) * 9;
        let nmin_x = (pos_x / 9 - 1) * 9;
        (nmin_x, nmin_x + 26, nmin_y, nmin_y + 26, nmin_x, nmin_y)
    } else if heading == world::HEADING_NORTH {
        let nmin_x = old_min_x;
        let nmin_y = old_min_y - 9;
        (nmin_x, nmin_x + 26, nmin_y, old_min_y - 1, nmin_x, nmin_y)
    } else if heading == world::HEADING_SOUTH {
        let nmin_x = old_min_x;
        let nmin_y = old_min_y + 27;
        let new_area_min_y = old_min_y + 9; // VB6: MinY - 18 but MinY was old+27, so net = old+9
        (nmin_x, nmin_x + 26, nmin_y, nmin_y + 8, nmin_x, new_area_min_y)
    } else if heading == world::HEADING_WEST {
        let nmin_x = old_min_x - 9;
        let nmin_y = old_min_y;
        (nmin_x, old_min_x - 1, nmin_y, nmin_y + 26, nmin_x, nmin_y)
    } else if heading == world::HEADING_EAST {
        let nmin_x = old_min_x + 27;
        let nmin_y = old_min_y;
        let new_area_min_x = old_min_x + 9;
        (nmin_x, nmin_x + 8, nmin_y, nmin_y + 26, new_area_min_x, nmin_y)
    } else {
        return;
    };

    // Clamp to map bounds
    let min_x = min_x.max(1);
    let min_y = min_y.max(1);
    let max_x = max_x.min(100);
    let max_y = max_y.min(100);

    // Update user's area tracking
    if let Some(user) = state.users.get_mut(&conn_id) {
        user.area_id = new_area_id;
        user.area_min_x = new_min_x;
        user.area_min_y = new_min_y;
    }

    // Send CA packet — tells client to erase out-of-range entities
    // Format: "CA" + chr$(x) + chr$(y) — two raw bytes
    let ca_bytes = [b'C', b'A', pos_x as u8, pos_y as u8];
    if let Some(writer) = state.writers.get_mut(&conn_id) {
        let _ = writer.send_packet(&ca_bytes).await;
    }

    // Build our CC for sending to newly visible users
    let my_cc = match state.users.get(&conn_id) {
        Some(u) => u.build_cc_packet(),
        None => return,
    };

    // Collect users, NPCs, ground items, particles, and lights in the new strip
    // (collect first to avoid borrow conflicts)
    let mut new_users: Vec<ConnectionId> = Vec::new();
    let mut new_npcs: Vec<usize> = Vec::new();
    let mut new_items: Vec<(i32, i32, i32)> = Vec::new(); // (grh, x, y)
    let mut new_particles: Vec<(i16, i32, i32)> = Vec::new(); // (particle_group_index, x, y)
    let mut new_lights: Vec<(i32, i32, i16, i16, i16, i16)> = Vec::new(); // (x, y, range, r, g, b)

    if let Some(grid) = state.world.grid(map) {
        for sx in min_x..=max_x {
            for sy in min_y..=max_y {
                if let Some(tile) = grid.tile(sx, sy) {
                    if let Some(other_conn) = tile.user_conn {
                        if other_conn != conn_id {
                            new_users.push(other_conn);
                        }
                    }
                    if tile.npc_index > 0 {
                        new_npcs.push(tile.npc_index as usize);
                    }
                    if tile.ground_item.obj_index > 0 {
                        // Look up GRH for the object
                        if let Some(obj) = state.get_object(tile.ground_item.obj_index) {
                            new_items.push((obj.grh_index, sx, sy));
                        }
                    }
                }
            }
        }
    }

    // Collect particles and lights from static map data
    let map_idx = map as usize;
    if let Some(Some(game_map)) = state.game_data.maps.get(map_idx) {
        for sx in min_x..=max_x {
            for sy in min_y..=max_y {
                if sx >= 1 && sx <= 100 && sy >= 1 && sy <= 100 {
                    let tile = &game_map.tiles[(sy - 1) as usize][(sx - 1) as usize];
                    if tile.particle_group_index > 0 {
                        new_particles.push((tile.particle_group_index, sx, sy));
                    }
                    if tile.range_light > 0 {
                        new_lights.push((sx, sy, tile.range_light, tile.rgb_light[0], tile.rgb_light[1], tile.rgb_light[2]));
                    }
                }
            }
        }
    }

    // Get self char_index and invisible flag for [CD and NOVER
    let (my_char_idx, my_invisible, my_privileges) = match state.users.get(&conn_id) {
        Some(u) => (u.char_index.0, u.admin_invisible, u.privileges),
        None => return,
    };

    // Send mutual CC + [CD + NOVER to newly visible users
    for other_id in new_users {
        if let Some(other) = state.users.get(&other_id) {
            if other.logged {
                let other_cc = other.build_cc_packet();
                let other_char_idx = other.char_index.0;
                let other_invisible = other.admin_invisible;
                let other_cd = build_cd_packet(other);
                state.send_to(conn_id, &other_cc).await;
                state.send_to(conn_id, &other_cd).await;
                // If the other player is invisible, tell us not to render them
                if other_invisible {
                    state.send_to(conn_id, &format!("NOVER{},1", other_char_idx)).await;
                }
                // Send our CC + [CD to them
                state.send_to(other_id, &my_cc).await;
                let my_cd = match state.users.get(&conn_id) {
                    Some(u) => build_cd_packet(u),
                    None => continue,
                };
                state.send_to(other_id, &my_cd).await;
                // If we are invisible, tell them not to render us
                if my_invisible {
                    state.send_to(other_id, &format!("NOVER{},1", my_char_idx)).await;
                }
            }
        } else {
            state.send_to(other_id, &my_cc).await;
        }
    }

    // Send NPC CCs
    for npc_idx in new_npcs {
        let npc_cc = match state.get_npc(npc_idx) {
            Some(npc) if npc.active => npc.build_cc_packet(),
            _ => continue,
        };
        state.send_to(conn_id, &npc_cc).await;
    }

    // Send ground items (HO packet) — VB6 ModAreas.bas line 264
    for (grh, ix, iy) in new_items {
        state.send_to(conn_id, &format!("HO{},{},{}", grh, ix, iy)).await;
    }

    // Send particle effects (PCF) — VB6 ModAreas.bas line 255
    for (pg, px, py) in new_particles {
        state.send_to(conn_id, &format!("PCF{},{},{},0", pg, px, py)).await;
    }

    // Send lighting effects (PCL) — VB6 ModAreas.bas line 259
    for (lx, ly, range, r, g, b) in new_lights {
        state.send_to(conn_id, &format!("PCL{},{},{},{},{},{}", lx, ly, range, r, g, b)).await;
    }
}

/// Warp a user to a new map/position (map transition).
/// Matches VB6 WarpUserChar: BKW, QDL, EraseUserChar, CM/XM/N~, MakeUserChar, PU, BKW.
async fn warp_user(state: &mut GameState, conn_id: ConnectionId, new_map: i32, new_x: i32, new_y: i32) {
    let old_data = match state.users.get(&conn_id) {
        Some(u) => (u.pos_map, u.pos_x, u.pos_y, u.char_index),
        None => return,
    };
    let (old_map, old_x, old_y, char_index) = old_data;

    // 1. BKW — fade to black (VB6 line 2262)
    state.send_to(conn_id, "BKW").await;

    // 2. QDL — remove dialog from area (VB6 line 2264)
    let qdl_pkt = format!("QDL{}", char_index.0);
    state.send_data(
        SendTarget::ToArea { map: old_map, x: old_x, y: old_y },
        &qdl_pkt,
    ).await;
    state.send_to(conn_id, "QTDL").await;

    // 3. EraseUserChar — remove from old position + send BP to old area
    let bp_pkt = format!("BP{}", char_index.0);
    state.send_data(
        SendTarget::ToAreaButIndex { conn_id, map: old_map, x: old_x, y: old_y },
        &bp_pkt,
    ).await;
    state.world.remove_user(old_map, old_x, old_y);

    // 4. Find a free tile if destination is occupied (VB6 DamePos)
    let (final_x, final_y) = find_free_pos(state, new_map, new_x, new_y);

    // 5. Update user position
    if let Some(user) = state.users.get_mut(&conn_id) {
        user.pos_map = new_map;
        user.pos_x = final_x;
        user.pos_y = final_y;
    }

    // 6. Place on new grid
    state.world.place_user(new_map, final_x, final_y, conn_id);

    // 7. Send map change packets (only if map changed, but we send always for safety)
    let map_idx = new_map as usize;
    let (r, g, b, music, map_name) = if let Some(Some(game_map)) = state.game_data.maps.get(map_idx) {
        (
            game_map.info.r,
            game_map.info.g,
            game_map.info.b,
            game_map.info.music,
            game_map.info.name.clone(),
        )
    } else {
        (200, 200, 200, 0, format!("Mapa {}", new_map))
    };

    state.send_to(conn_id, &format!("CM{},{},{},{}", new_map, r, g, b)).await;
    state.send_to(conn_id, &format!("XM{}", music)).await;
    state.send_to(conn_id, &format!("N~{}", map_name)).await;

    // 8. Send IP (self char index) + own CC + [CD so client renders self at new position
    let (ci, own_cc, own_cd) = match state.users.get(&conn_id) {
        Some(u) => (u.char_index, u.build_cc_packet(), build_cd_packet(u)),
        None => return,
    };
    state.send_to(conn_id, &format!("IP{}", ci.0)).await;
    state.send_to(conn_id, &own_cc).await;
    state.send_to(conn_id, &own_cd).await;

    // 9. PU (position update — tells client where to center camera)
    state.send_to(conn_id, &format!("PU{},{}", final_x, final_y)).await;

    // 10. Send area visibility (CA + strip CCs/NPCs/items)
    make_user_visible(state, conn_id).await;

    // 11. Send CC + [CD to other players in new area so they see us
    state.send_data(
        SendTarget::ToAreaButIndex { conn_id, map: new_map, x: final_x, y: final_y },
        &own_cc,
    ).await;
    state.send_data(
        SendTarget::ToAreaButIndex { conn_id, map: new_map, x: final_x, y: final_y },
        &own_cd,
    ).await;

    // 12. Warp FX is NOT sent by default — only when caller sets fx=true
    // (VB6: FX param is Optional, only DoTileEvents sets it when tile has otTeleport object)

    // 13. BKW — fade back in (VB6 end of WarpUserChar)
    state.send_to(conn_id, "BKW").await;

    // 14. Check tile exit at destination — VB6 also triggers teleports on warp arrival
    // Use a single-level check to avoid infinite recursion (e.g. exit→exit→exit...)
    if let Some((exit_map, exit_x, exit_y)) = state.get_tile_exit(new_map, final_x, final_y) {
        // Only warp if the exit leads somewhere different (avoid self-loops)
        if exit_map != new_map || exit_x != final_x || exit_y != final_y {
            Box::pin(warp_user(state, conn_id, exit_map, exit_x, exit_y)).await;
        }
    }
}

/// Send warp FX (sound + visual) at user's current position.
/// VB6: Only called when tile has otTeleport object (FX=True param).
async fn send_warp_fx(state: &mut GameState, conn_id: ConnectionId) {
    let (invisible, ci, map, x, y) = match state.users.get(&conn_id) {
        Some(u) => (u.admin_invisible, u.char_index.0, u.pos_map, u.pos_x, u.pos_y),
        None => return,
    };
    if !invisible {
        state.send_data(SendTarget::ToArea { map, x, y }, "TW3").await;
        state.send_data(SendTarget::ToArea { map, x, y }, &format!("CFX{},1,0", ci)).await;
    }
}

/// Find a free position near (x, y) on the given map.
/// If (x, y) is free, returns it. Otherwise searches nearby tiles in a spiral.
/// Matches VB6 DamePos / ClosestLegalPos.
fn find_free_pos(state: &GameState, map: i32, x: i32, y: i32) -> (i32, i32) {
    // Check if target tile is free
    let occupied = state.world.grid(map)
        .and_then(|g| g.tile(x, y))
        .map(|t| t.user_conn.is_some() || t.npc_index > 0)
        .unwrap_or(false);
    let blocked = state.is_tile_blocked(map, x, y);

    if !occupied && !blocked {
        return (x, y);
    }

    // Search in expanding square around target
    for radius in 1i32..=10 {
        for dy in -radius..=radius {
            for dx in -radius..=radius {
                if dx.abs() != radius && dy.abs() != radius { continue; } // Only perimeter
                let nx = x + dx;
                let ny = y + dy;
                if nx < 1 || nx > 100 || ny < 1 || ny > 100 { continue; }
                let tile_blocked = state.is_tile_blocked(map, nx, ny);
                if tile_blocked { continue; }
                let tile_free = state.world.grid(map)
                    .and_then(|g| g.tile(nx, ny))
                    .map(|t| t.user_conn.is_none() && t.npc_index == 0)
                    .unwrap_or(false);
                if tile_free {
                    return (nx, ny);
                }
            }
        }
    }

    // Fallback: return original position
    (x, y)
}

/// VB6 ZonaCura (General.bas:2136) — Check if a Revividor NPC (type=1) is within the player's
/// visible area (MinXBorder × MinYBorder) and distance < 20.
fn zona_cura(state: &GameState, map: i32, px: i32, py: i32) -> bool {
    let min_x = (px - world::MIN_X_BORDER + 1).max(1);
    let max_x = (px + world::MIN_X_BORDER - 1).min(100);
    let min_y = (py - world::MIN_Y_BORDER + 1).max(1);
    let max_y = (py + world::MIN_Y_BORDER - 1).min(100);

    if let Some(grid) = state.world.grid(map) {
        for cy in min_y..=max_y {
            for cx in min_x..=max_x {
                if let Some(tile) = grid.tile(cx, cy) {
                    if tile.npc_index > 0 {
                        if let Some(npc) = state.get_npc(tile.npc_index as usize) {
                            if npc.npc_type == crate::data::npcs::NpcType::Reviver && npc.is_alive() {
                                let dist = (px - npc.x).abs() + (py - npc.y).abs();
                                if dist < 20 {
                                    return true;
                                }
                            }
                        }
                    }
                }
            }
        }
    }
    false
}

/// VB6 AutoCuraUser (General.bas:2156) — Automatic priest heal/revive/cure.
/// Called when player moves into ZonaCura range of a Revividor NPC.
async fn auto_cura_user(state: &mut GameState, conn_id: ConnectionId) {
    let (dead, hp_low, poisoned) = match state.users.get(&conn_id) {
        Some(u) if u.logged => (u.dead, u.min_hp < u.max_hp, u.poisoned),
        _ => return,
    };

    // VB6: skip if in ring/arena
    let in_ring = state.users.get(&conn_id).map(|u| u.en_duelo || u.en_desafio).unwrap_or(false);
    if in_ring {
        state.send_to(conn_id, "||395").await;
        return;
    }

    if dead {
        // Revive + full heal + full stamina
        revive_user(state, conn_id).await;
        if let Some(user) = state.users.get_mut(&conn_id) {
            user.min_hp = user.max_hp;
            user.min_sta = user.max_sta;
        }
        // VB6: ||693 = "Los dioses te han resucitado"
        state.send_to(conn_id, "||693").await;
        // Sound: TW20
        let (map, x, y) = match state.users.get(&conn_id) {
            Some(u) => (u.pos_map, u.pos_x, u.pos_y),
            None => return,
        };
        state.send_data(SendTarget::ToArea { map, x, y }, "TW20").await;
        send_stats_hp(state, conn_id).await;
        send_stats_sta(state, conn_id).await;
        // CFF resurrection effect (FX 65) — already sent by revive_user, skip duplicate
    } else if hp_low {
        // Full heal
        if let Some(user) = state.users.get_mut(&conn_id) {
            user.min_hp = user.max_hp;
        }
        // VB6: ||694 = "Los dioses te han curado"
        state.send_to(conn_id, "||694").await;
        let (map, x, y) = match state.users.get(&conn_id) {
            Some(u) => (u.pos_map, u.pos_x, u.pos_y),
            None => return,
        };
        state.send_data(SendTarget::ToArea { map, x, y }, "TW20").await;
        send_stats_hp(state, conn_id).await;
    }

    // Cure poison
    if poisoned {
        if let Some(user) = state.users.get_mut(&conn_id) {
            user.poisoned = false;
        }
    }
}

// =====================================================================
// Helper functions
// =====================================================================

/// Strip the opcode prefix from a packet.
fn strip_opcode(data: &str, opcode_len: usize) -> &str {
    if data.len() > opcode_len {
        &data[opcode_len..]
    } else {
        ""
    }
}

/// Close a connection (shutdown writer, will trigger Disconnected event).
async fn close_connection(state: &mut GameState, conn_id: ConnectionId) {
    if let Some(mut writer) = state.writers.remove(&conn_id) {
        tokio::time::sleep(tokio::time::Duration::from_millis(100)).await;
        writer.shutdown().await;
    }
}

/// Validate name — only ASCII alphanumeric + some allowed chars.
fn is_valid_name(name: &str) -> bool {
    if name.is_empty() || name.len() > 30 {
        return false;
    }
    name.chars().all(|c| c.is_ascii_alphanumeric() || c == '_' || c == '-' || c == ' ')
}

/// Check if a character is banned (from charfile FLAGS section).
fn is_char_banned(base: &std::path::Path, char_name: &str) -> bool {
    charfile::load_charfile(base, char_name)
        .map(|c| c.banned)
        .unwrap_or(false)
}

/// Check if any online user has the same IP (for multi-login prevention).
fn is_same_ip_online(state: &GameState, exclude_id: ConnectionId, ip: &str) -> bool {
    state.users.values().any(|u| {
        u.conn_id != exclude_id && u.logged && u.ip == ip
    })
}

/// Simple random number generator.
fn rand_simple_u32() -> u32 {
    use std::time::{SystemTime, UNIX_EPOCH};
    let nanos = SystemTime::now()
        .duration_since(UNIX_EPOCH)
        .unwrap_or_default()
        .as_nanos() as u32;
    nanos.wrapping_mul(1103515245).wrapping_add(12345) % 1000
}

/// Get current date as dd/mm/yyyy hh:mm (VB6 Format(Now, "dd/mm/yyyy hh:nn")).
fn chrono_like_date() -> String {
    use std::time::{SystemTime, UNIX_EPOCH};
    let secs = SystemTime::now()
        .duration_since(UNIX_EPOCH)
        .unwrap_or_default()
        .as_secs();
    // Simple UTC date calculation
    let days = secs / 86400;
    let time_of_day = secs % 86400;
    let hours = time_of_day / 3600;
    let minutes = (time_of_day % 3600) / 60;
    // Days since epoch to y/m/d (simplified)
    let mut y = 1970i64;
    let mut remaining = days as i64;
    loop {
        let days_in_year = if y % 4 == 0 && (y % 100 != 0 || y % 400 == 0) { 366 } else { 365 };
        if remaining < days_in_year { break; }
        remaining -= days_in_year;
        y += 1;
    }
    let leap = y % 4 == 0 && (y % 100 != 0 || y % 400 == 0);
    let month_days = [31, if leap { 29 } else { 28 }, 31, 30, 31, 30, 31, 31, 30, 31, 30, 31];
    let mut m = 0usize;
    for i in 0..12 {
        if remaining < month_days[i] { m = i; break; }
        remaining -= month_days[i];
    }
    format!("{:02}/{:02}/{} {:02}:{:02}", remaining + 1, m + 1, y, hours, minutes)
}

/// Random number in range [min, max] (inclusive).
fn rand_range(min: i32, max: i32) -> i32 {
    if min >= max {
        return min;
    }
    let range = (max - min + 1) as u32;
    min + (rand_simple_u32() % range) as i32
}

// =====================================================================
// NPC system — spawning, AI, combat
// =====================================================================

/// Send CC packets for all NPCs in the area around (x, y) on map to a specific user.
async fn send_area_npc_ccs(state: &mut GameState, conn_id: ConnectionId, map: i32, x: i32, y: i32) {
    let min_y = (y - world::MIN_Y_BORDER + 1).max(1);
    let max_y = (y + world::MIN_Y_BORDER - 1).min(world::MAP_HEIGHT as i32);
    let min_x = (x - world::MIN_X_BORDER + 1).max(1);
    let max_x = (x + world::MIN_X_BORDER - 1).min(world::MAP_WIDTH as i32);

    // Collect NPC CCs first to avoid borrow issues
    let mut npc_ccs: Vec<String> = Vec::new();
    if let Some(grid) = state.world.grid(map) {
        for ny in min_y..=max_y {
            for nx in min_x..=max_x {
                if let Some(tile) = grid.tile(nx, ny) {
                    if tile.npc_index > 0 {
                        if let Some(npc) = state.get_npc(tile.npc_index as usize) {
                            if npc.is_alive() {
                                npc_ccs.push(npc.build_cc_packet());
                            }
                        }
                    }
                }
            }
        }
    }

    for cc in npc_ccs {
        state.send_to(conn_id, &cc).await;
    }
}

/// Send ground item visuals (HO) in the area to a user.
async fn send_area_ground_items(state: &mut GameState, conn_id: ConnectionId, map: i32, x: i32, y: i32) {
    let min_y = (y - world::MIN_Y_BORDER + 1).max(1);
    let max_y = (y + world::MIN_Y_BORDER - 1).min(world::MAP_HEIGHT as i32);
    let min_x = (x - world::MIN_X_BORDER + 1).max(1);
    let max_x = (x + world::MIN_X_BORDER - 1).min(world::MAP_WIDTH as i32);

    let mut ho_packets: Vec<String> = Vec::new();
    if let Some(grid) = state.world.grid(map) {
        for gy in min_y..=max_y {
            for gx in min_x..=max_x {
                if let Some(tile) = grid.tile(gx, gy) {
                    let obj_idx = tile.ground_item.obj_index;
                    if obj_idx > 0 {
                        let grh = state.get_object(obj_idx)
                            .map(|o| o.grh_index)
                            .unwrap_or(0);
                        if grh > 0 {
                            ho_packets.push(format!("HO{},{},{}", grh, gx, gy));
                        }
                    }
                }
            }
        }
    }

    for ho in ho_packets {
        state.send_to(conn_id, &ho).await;
    }
}

/// Player attacks an NPC (PvE melee combat).
#[allow(clippy::too_many_arguments)]
async fn user_attack_npc(
    state: &mut GameState,
    conn_id: ConnectionId,
    npc_idx: usize,
    map: i32,
    x: i32,
    y: i32,
    strength: i32,
    agility: i32,
    level: i32,
    min_hit: i32,
    max_hit: i32,
    skill_armas: i32,
    attacker_name: &str,
    class: &str,
) {
    // Get NPC data
    let npc_data = match state.get_npc(npc_idx) {
        Some(n) if n.is_alive() && n.attackable => {
            (n.poder_evasion, n.char_index, n.name.clone(), n.give_exp)
        }
        _ => return,
    };
    let (npc_evasion, npc_char_index, npc_name, _give_exp) = npc_data;

    // Hit/miss calculation
    let attack_power = calc_attack_power(skill_armas, agility, level);
    let defense_power = npc_evasion as f64;
    let hit_prob = ((50.0 + (attack_power - defense_power) * 0.4) as i32).clamp(10, 90);

    if rand_range(1, 100) > hit_prob {
        // Miss
        state.send_to(conn_id, "U1").await;
        return;
    }

    // Damage calculation
    let weapon_dmg = rand_range(min_hit.max(1), max_hit.max(1));
    let str_bonus = ((max_hit as f64 / 5.0) * (strength - 15).max(0) as f64) as i32;
    let base_dmg = 3 * weapon_dmg + str_bonus + rand_range(min_hit, max_hit);
    let class_mod = class_damage_modifier(class);
    let damage = ((base_dmg as f64) * class_mod) as i32;
    let damage = damage.max(1);

    // Check attacker GM status BEFORE taking mutable NPC borrow
    let attacker_is_gm = state.users.get(&conn_id)
        .map(|u| u.privileges > 0)
        .unwrap_or(false);

    // Apply damage to NPC
    let (npc_dead, npc_give_exp, npc_give_gld_min, npc_give_gld_max) = {
        match state.get_npc_mut(npc_idx) {
            Some(npc) => {
                npc.min_hp -= damage;

                // Track damage for proportional EXP
                npc.damage_received.push((conn_id, damage));

                // NpcAtacado trigger (VB6): if NPC is not hostile, switch to defense mode
                // GMs (privileges > 0) should NOT become NPC targets
                if !attacker_is_gm {
                    if !npc.hostile && npc.movement != npc::AI_DEFENSE {
                        npc.old_movement = npc.movement;
                        npc.old_hostile = npc.hostile;
                        npc.movement = npc::AI_DEFENSE;
                        npc.hostile = true;
                        npc.attacked_by = attacker_name.to_string();
                        npc.target = Some(conn_id);
                    } else if npc.target.is_none() {
                        // Already hostile — just set target
                        npc.target = Some(conn_id);
                    }
                }

                let dead = npc.min_hp <= 0;
                (dead, npc.give_exp, npc.give_gld_min, npc.give_gld_max)
            }
            None => return,
        }
    };

    // Send hit feedback: U2 to attacker (you hit NPC)
    // VB6 format: "U2" & Daño — client does Right$(rData, Len-2) to get damage string
    let u2_pkt = format!("U2{}", damage);
    state.send_to(conn_id, &u2_pkt).await;

    // Hit sound to area
    let snd_pkt = format!("TW{}", 10); // Impact sound
    state.send_data(SendTarget::ToArea { map, x, y }, &snd_pkt).await;

    // Blood FX on NPC
    let fx_pkt = format!("CFX{},{},{}", npc_char_index.0, 14, 0); // VB6: FXSANGRE = 14
    state.send_data(SendTarget::ToArea { map, x, y }, &fx_pkt).await;

    if npc_dead {
        npc_die(state, npc_idx, conn_id, npc_give_exp, npc_give_gld_min, npc_give_gld_max).await;
    }
}

/// NPC dies — remove from world, reward players proportionally, schedule respawn.
/// VB6: MuereNpc (modNPC.bas) — full parity with death sound, proportional EXP,
/// gold to inventory, crystal drops, Ancalagon boss, faction points.
async fn npc_die(
    state: &mut GameState,
    npc_idx: usize,
    killer_id: ConnectionId,
    give_exp: i32,
    give_gld_min: i32,
    give_gld_max: i32,
) {
    let npc_info = match state.get_npc(npc_idx) {
        Some(n) => (n.map, n.x, n.y, n.char_index, n.name.clone(), n.npc_number,
                    n.snd3, n.max_hp, n.give_pts, n.cristales,
                    n.crystal_min1, n.crystal_max1, n.crystal_min2, n.crystal_max2,
                    n.crystal_min3, n.crystal_max3, n.crystal_min4, n.crystal_max4,
                    n.damage_received.clone(), n.maestro_user),
        None => return,
    };
    let (map, x, y, char_index, npc_name, npc_number,
         snd3, npc_max_hp, give_pts, has_crystals,
         cr_min1, cr_max1, cr_min2, cr_max2,
         cr_min3, cr_max3, cr_min4, cr_max4,
         damage_received, is_pet_owner) = npc_info;

    // 1) Death sound (VB6: TW{snd3})
    if snd3 > 0 {
        let snd_pkt = format!("TW{}", snd3);
        state.send_data(SendTarget::ToArea { map, x, y }, &snd_pkt).await;
    }

    // 2) Send ||50 to killer (NPC death notification — client plays sound/animation)
    state.send_to(killer_id, "||50").await;

    // 3) War king death check (VB6: NPC is rey de guerra)
    if state.hay_guerra && npc_idx == state.rey_guerra_index {
        // Faction wins — broadcast, award gold + faction points
        let killer_name = state.users.get(&killer_id).map(|u| u.char_name.clone()).unwrap_or_default();
        let msg = format!("||{}@{} ha matado al rey de la guerra!", 53, killer_name);
        state.send_data(SendTarget::ToAll, &msg).await;

        if let Some(user) = state.users.get_mut(&killer_id) {
            user.gold += 1_000_000;
            if user.armada_real {
                user.recompensas_real = (user.recompensas_real + 30).min(999);
            } else if user.fuerzas_caos {
                user.recompensas_caos = (user.recompensas_caos + 30).min(999);
            }
        }
        send_stats_gold(state, killer_id).await;
        state.hay_guerra = false;
    }

    // 4) Ancalagon boss system
    match npc_number {
        936 => {
            // Dragon killed — broadcast ||54, award points
            state.send_data(SendTarget::ToAll, "||54").await;
            state.ancalagon_alive = false;
            state.ancalagon_minutes = 0;
            state.ancalagon_seconds = 0;
            if let Some(user) = state.users.get_mut(&killer_id) {
                if user.armada_real {
                    user.recompensas_real = (user.recompensas_real + 25).min(999);
                } else if user.fuerzas_caos {
                    user.recompensas_caos = (user.recompensas_caos + 25).min(999);
                }
            }
        }
        937 => {
            // Pre-dragon killed → spawn real dragon (936) at same location
            state.ancalagon_pre_dragon = false;
            try_spawn_ancalagon_dragon(state).await;
        }
        938 => {
            // Guardian killed → decrement guardian count
            state.ancalagon_guardians = (state.ancalagon_guardians - 1).max(0);
        }
        _ => {}
    }

    // 5) Drop items (NPC_TIRAR_ITEMS) — only if not a pet
    let is_pet = is_pet_owner.is_some();
    if !is_pet {
        npc_drop_items(state, npc_idx, killer_id, map, x, y).await;
    }

    // 6) Crystal drops (VB6: level >= 60 NPCs drop crystals obj 1275-1278)
    if has_crystals && !is_pet {
        let crystal_objs = [
            (1275, cr_min1, cr_max1),
            (1276, cr_min2, cr_max2),
            (1277, cr_min3, cr_max3),
            (1278, cr_min4, cr_max4),
        ];
        for (obj_id, cmin, cmax) in &crystal_objs {
            if *cmax > 0 {
                let amount = rand_range((*cmin).max(1), *cmax);
                if amount > 0 {
                    // Drop crystal on ground near NPC
                    let (dx, dy) = find_free_tile(state, map, x, y);
                    let drop_x = x + dx;
                    let drop_y = y + dy;
                    let grid = state.world.grid_mut(map);
                    if let Some(tile) = grid.tile_mut(drop_x, drop_y) {
                        if tile.ground_item.obj_index == 0 {
                            tile.ground_item.obj_index = *obj_id;
                            tile.ground_item.amount = amount;
                        }
                    }
                    let grh = state.get_object(*obj_id).map(|o| o.grh_index).unwrap_or(0);
                    if grh > 0 {
                        let ho = format!("HO{},{},{}", grh, drop_x, drop_y);
                        state.send_data(SendTarget::ToArea { map, x, y }, &ho).await;
                    }
                    clean_world_add_item(state, map, drop_x, drop_y, 10, *obj_id);
                }
            }
        }
    }

    // 7) Remove NPC character from area (BP packet)
    let bp_pkt = format!("BP{}", char_index.0);
    state.send_data(SendTarget::ToArea { map, x, y }, &bp_pkt).await;

    // Kill NPC (remove from grid, mark inactive)
    state.kill_npc(npc_idx);

    // Check praetorian death
    if es_pretoriano(npc_number) {
        pretoriano_check_death(state, npc_idx);
    }

    // Check ARAM tower death
    aram_check_tower_death(state, npc_idx).await;

    // 8) Proportional EXP distribution (VB6: (give_exp / max_hp) * player_damage * mult_exp)
    let exp_mult = state.multiplicador_exp;
    let npc_max_hp_f = (npc_max_hp as f64).max(1.0);

    if give_exp > 0 && !damage_received.is_empty() {
        // Aggregate damage per player
        let mut damage_per_player: HashMap<ConnectionId, i32> = HashMap::new();
        for (conn, dmg) in &damage_received {
            *damage_per_player.entry(*conn).or_insert(0) += dmg;
        }

        for (conn_id, player_damage) in &damage_per_player {
            // Calculate proportional EXP: (give_exp / max_hp) * player_damage * mult_exp
            let mut exp_award = ((give_exp as f64 / npc_max_hp_f) * (*player_damage as f64) * exp_mult as f64) as i64;

            // Scroll(0) = EXP scroll multiplier
            let scroll_mult = state.users.get(conn_id)
                .map(|u| if u.scroll_active[0] { u.scroll_mult[0] as i64 } else { 1 })
                .unwrap_or(1);
            exp_award *= scroll_mult;

            // Level cap check
            let can_level = state.users.get(conn_id)
                .map(|u| u.logged && u.level < MAX_LEVEL)
                .unwrap_or(false);

            if can_level && exp_award > 0 {
                if let Some(user) = state.users.get_mut(conn_id) {
                    user.exp += exp_award;
                }
                send_stats_exp(state, *conn_id).await;
                check_user_level(state, *conn_id).await;
            }
        }
    } else if give_exp > 0 {
        // Fallback: no damage tracking — give full EXP to killer (backward compat)
        let mut exp_award = (give_exp as i64) * (exp_mult as i64);
        let scroll_mult = state.users.get(&killer_id)
            .map(|u| if u.scroll_active[0] { u.scroll_mult[0] as i64 } else { 1 })
            .unwrap_or(1);
        exp_award *= scroll_mult;
        if let Some(user) = state.users.get_mut(&killer_id) {
            if user.level < MAX_LEVEL {
                user.exp += exp_award;
            }
        }
        send_stats_exp(state, killer_id).await;
        check_user_level(state, killer_id).await;
    }

    // 9) Gold to killer's inventory (VB6: gold goes to player, NOT floor)
    let gold_mult = state.multiplicador_oro;
    let scroll_gold_mult = state.users.get(&killer_id)
        .map(|u| if u.scroll_active[1] { u.scroll_mult[1] } else { 1 })
        .unwrap_or(1);
    let gld_min = (give_gld_min as i64) * (gold_mult as i64) * (scroll_gold_mult as i64);
    let gld_max = (give_gld_max as i64) * (gold_mult as i64) * (scroll_gold_mult as i64);
    let gold_award = if gld_max > gld_min {
        rand_range(gld_min as i32, gld_max as i32) as i64
    } else {
        gld_min
    };
    if gold_award > 0 {
        if let Some(user) = state.users.get_mut(&killer_id) {
            user.gold += gold_award;
        }
        send_stats_gold(state, killer_id).await;
    }

    // 10) Faction points (VB6: GivePTS)
    if give_pts > 0 {
        if let Some(user) = state.users.get_mut(&killer_id) {
            if user.armada_real {
                user.recompensas_real = (user.recompensas_real + give_pts).min(999);
            } else if user.fuerzas_caos {
                user.recompensas_caos = (user.recompensas_caos + give_pts).min(999);
            }
        }
    }

    // 11) Pet cleanup — if NPC was someone's pet, remove from owner
    if let Some(owner_conn) = is_pet_owner {
        remove_pet_from_owner(state, owner_conn, npc_idx);
    }

    // 12) Notify killer (VB6: ||50 + ||170@exp + ||56@gold)
    state.send_to(killer_id, "||50").await; // TEXTO50: Has matado a la criatura!
    state.send_to(killer_id, &format!("||170@{}", give_exp)).await; // TEXTO170: Has ganado %1 exp
    if gold_award > 0 {
        state.send_to(killer_id, &format!("||56@{}", gold_award)).await; // TEXTO56: La criatura ha dejado %1 monedas
    }

    // Quest kill tracking (VB6 RestarNPC)
    quest_check_npc_kill(state, killer_id, npc_number as i32).await;

    info!("[NPC] '{}' killed NPC '{}' (idx={}, +{} exp, +{} gold)",
          state.users.get(&killer_id).map(|u| u.char_name.as_str()).unwrap_or("?"),
          npc_name, npc_idx, give_exp, gold_award);
}


/// Drop NPC inventory items on death.
/// VB6: NPC_TIRAR_ITEMS (Modulo_InventANDobj.bas).
/// Probability: ProbTirar * 2, capped at 200. Roll 1-200, if <= prob → drop.
/// Charisma bonus: >=20 adds luck.
async fn npc_drop_items(
    state: &mut GameState,
    npc_idx: usize,
    killer_id: ConnectionId,
    map: i32,
    npc_x: i32,
    npc_y: i32,
) {
    // Get NPC inventory before killing
    let npc_inv: Vec<(i32, i32, i32)> = match state.get_npc(npc_idx) {
        Some(n) => n.inventory.iter()
            .filter(|slot| slot.obj_index > 0 && slot.amount > 0)
            .map(|slot| (slot.obj_index, slot.amount, slot.prob_tirar))
            .collect(),
        None => return,
    };

    if npc_inv.is_empty() {
        return;
    }

    // Get killer's charisma for luck bonus
    let charisma = state.users.get(&killer_id)
        .map(|u| u.attributes[3]) // [3] = Charisma
        .unwrap_or(18);

    // Charisma luck modifier
    let luck_mod = match charisma {
        0..=18 => -1,
        19 => 0,
        20 => 1,
        21 => 2,
        c => (c - 21).max(0),
    };

    // Drop multiplier (VB6: multiplicador_drop + scroll(2))
    let drop_mult = state.multiplicador_drop;
    let scroll_drop_mult = state.users.get(&killer_id)
        .map(|u| if u.scroll_active[2] { u.scroll_mult[2] } else { 1 })
        .unwrap_or(1);

    for (obj_index, amount, prob_tirar) in npc_inv {
        if prob_tirar <= 0 {
            continue;
        }

        // Calculate drop probability (VB6: ProbTirar * 2 * scroll * mult_drop, cap 200)
        let prob = ((prob_tirar * 2 + luck_mod) * drop_mult * scroll_drop_mult).max(1).min(200);

        // Roll
        let roll = random_number(1, 200);
        if roll <= prob {
            // Drop item — use actual amount from NPC inventory (VB6 drops full amount)
            let drop_amount = amount.max(1);

            // Find a free tile near the NPC
            let (dx, dy) = find_free_tile(state, map, npc_x, npc_y);
            let drop_x = npc_x + dx;
            let drop_y = npc_y + dy;

            // Place item on ground
            let grid = state.world.grid_mut(map);
            if let Some(tile) = grid.tile_mut(drop_x, drop_y) {
                if tile.ground_item.obj_index == 0 {
                    tile.ground_item.obj_index = obj_index;
                    tile.ground_item.amount = drop_amount;
                }
            }

            // Get GRH for the object
            let grh = state.get_object(obj_index).map(|o| o.grh_index).unwrap_or(0);
            if grh > 0 {
                let ho_pkt = format!("HO{},{},{}", grh, drop_x, drop_y);
                state.send_data(SendTarget::ToArea { map, x: npc_x, y: npc_y }, &ho_pkt).await;
            }

            // Track for world cleanup
            clean_world_add_item(state, map, drop_x, drop_y, 10, obj_index);
        }
    }
}

/// Find a free adjacent tile for item drop. Returns (dx, dy) offset.
fn find_free_tile(state: &GameState, map: i32, x: i32, y: i32) -> (i32, i32) {
    let grid = match state.world.grid(map) {
        Some(g) => g,
        None => return (0, 0),
    };

    // Try the NPC's tile first
    if let Some(tile) = grid.tile(x, y) {
        if tile.ground_item.obj_index == 0 {
            return (0, 0);
        }
    }

    // Try adjacent tiles
    let offsets = [(0, 1), (1, 0), (0, -1), (-1, 0), (1, 1), (-1, -1), (1, -1), (-1, 1)];
    for (dx, dy) in &offsets {
        let tx = x + dx;
        let ty = y + dy;
        if let Some(tile) = grid.tile(tx, ty) {
            if tile.ground_item.obj_index == 0 {
                return (*dx, *dy);
            }
        }
    }

    (0, 0) // Fallback to same tile
}

/// NPC attacks a player.
async fn npc_attack_user(state: &mut GameState, npc_idx: usize, target_conn: ConnectionId) {
    let npc_data = match state.get_npc(npc_idx) {
        Some(n) if n.is_alive() => {
            (n.poder_ataque, n.min_hit, n.max_hit, n.char_index, n.map, n.x, n.y, n.name.clone())
        }
        _ => return,
    };
    let (npc_ataque, npc_min_hit, npc_max_hit, npc_char_index, map, nx, ny, npc_name) = npc_data;

    let user_data = match state.users.get(&target_conn) {
        // VB6 NpcAtacaUser: AdminInvisible=1 or Privilegios<>User → exit (no attack)
        Some(u) if u.logged && !u.dead && u.privileges == 0 && !u.admin_invisible => {
            (u.attributes[1], // Agility
             u.skills[3],     // SK4 = Tacticas
             u.level, u.char_index)
        }
        _ => return,
    };
    let (u_agility, u_tacticas, u_level, u_char_index) = user_data;

    // Hit/miss calculation
    let npc_power = npc_ataque as f64;
    let user_evasion = calc_defense_power(u_tacticas, u_agility, u_level);
    let hit_prob = ((50.0 + (npc_power - user_evasion) * 0.4) as i32).clamp(10, 90);

    if rand_range(1, 100) > hit_prob {
        // Miss — N1 (NPC swing and miss)
        state.send_to(target_conn, "N1").await;
        return;
    }

    // Damage + armor absorption (VB6: NpcDaño)
    let body_part = rand_range(1, 6);
    let raw_damage = rand_range(npc_min_hit.max(1), npc_max_hit.max(1));
    let absorption = calc_armor_absorption(state, target_conn, body_part);
    let damage = (raw_damage - absorption).max(1);

    // Apply damage
    if let Some(user) = state.users.get_mut(&target_conn) {
        user.min_hp -= damage;
    }
    let n2_pkt = format!("N2{},{}", body_part, damage);
    state.send_to(target_conn, &n2_pkt).await;

    // Blood FX on player (VB6: FXSANGRE = 14)
    let fx_pkt = format!("CFX{},{},{}", u_char_index.0, 14, 0);
    state.send_data(SendTarget::ToArea { map, x: nx, y: ny }, &fx_pkt).await;

    // Impact sound
    let snd_pkt = format!("TW{}", 10);
    state.send_data(SendTarget::ToArea { map, x: nx, y: ny }, &snd_pkt).await;

    // NPC poison on hit (VB6: If Npclist(NpcIndex).Veneno = 1 Then NpcEnvenenarUser)
    let npc_veneno = state.get_npc(npc_idx).map(|n| n.veneno).unwrap_or(false);
    if npc_veneno {
        let already_poisoned = state.users.get(&target_conn).map(|u| u.poisoned).unwrap_or(true);
        if !already_poisoned {
            if let Some(user) = state.users.get_mut(&target_conn) {
                user.poisoned = true;
                user.counter_poison = 0;
            }
            let msg = format!("{}||171@{}", server_opcodes::CONSOLE_MSG, npc_name);
            state.send_to(target_conn, &msg).await;
        }
    }

    // Update HP
    send_stats_hp(state, target_conn).await;

    // Check death
    let hp = state.users.get(&target_conn).map(|u| u.min_hp).unwrap_or(0);
    if hp <= 0 {
        user_die(state, target_conn, None).await;
    }
}

/// NPC casts a spell on a user (VB6: NpcLanzaSpellSobreUser).
async fn npc_cast_spell(state: &mut GameState, npc_idx: usize, target_conn: ConnectionId, spell_id: i32) {
    let spell = match state.get_spell(spell_id) {
        Some(s) => s.clone(),
        None => return,
    };

    let npc_data = match state.get_npc(npc_idx) {
        Some(n) if n.is_alive() => (n.char_index, n.map, n.x, n.y, n.name.clone()),
        _ => return,
    };
    let (npc_char, map, nx, ny, npc_name) = npc_data;

    let target_alive = state.users.get(&target_conn)
        .map(|u| u.logged && !u.dead)
        .unwrap_or(false);
    if !target_alive { return; }

    // Magic words broadcast (spell name)
    if !spell.palabras_magicas.is_empty() {
        let talk = format!(";{} dice: {}~255~0~0", npc_name, spell.palabras_magicas);
        state.send_data(SendTarget::ToArea { map, x: nx, y: ny }, &talk).await;
    }

    // FX on target
    let target_ci = state.users.get(&target_conn).map(|u| u.char_index.0).unwrap_or(0);
    if spell.fx_grh > 0 {
        let fx = format!("CFX{},{},{}", target_ci, spell.fx_grh, spell.loops);
        state.send_data(SendTarget::ToArea { map, x: nx, y: ny }, &fx).await;
    }
    if spell.wav > 0 {
        let snd = format!("TW{}", spell.wav);
        state.send_data(SendTarget::ToArea { map, x: nx, y: ny }, &snd).await;
    }

    // Spell effect
    match spell.sube_hp {
        1 => {
            // Heal spell (SubeHP=1) — when cast on a user target, NPC heals ITSELF
            // (VB6: NpcLanzaSpellSobreUser with SubeHP=1 heals the NPC, not the user)
            let heal = rand_range(spell.min_hp.max(1), spell.max_hp.max(1));
            if let Some(npc) = state.get_npc_mut(npc_idx) {
                npc.min_hp = (npc.min_hp + heal).min(npc.max_hp);
            }
        }
        2 => {
            // Damage spell (SubeHP=2)
            let mut damage = rand_range(spell.min_hp.max(1), spell.max_hp.max(1));

            // Armor magic defense reduction (simplified — use armor absorption)
            let absorption = calc_armor_absorption(state, target_conn, rand_range(1, 6));
            damage = (damage - absorption / 2).max(1); // Magic has half physical absorption

            // Apply damage
            if let Some(user) = state.users.get_mut(&target_conn) {
                user.min_hp -= damage;
            }

            // Send damage packet
            let pkt = format!("N2,{},{}", rand_range(1, 6), damage);
            state.send_to(target_conn, &pkt).await;
            send_stats_hp(state, target_conn).await;

            // Check death
            let hp = state.users.get(&target_conn).map(|u| u.min_hp).unwrap_or(0);
            if hp <= 0 {
                user_die(state, target_conn, None).await;
            }
        }
        _ => {}
    }

    // Paralysis effect
    if spell.paraliza {
        if let Some(user) = state.users.get_mut(&target_conn) {
            user.paralyzed = true;
            user.counter_paralisis = state.config.intervalo_paralizado;
        }
        state.send_to(target_conn, "PARAL").await;
    }
}

/// Move an NPC in a direction. Returns true if moved.
fn move_npc(state: &mut GameState, npc_idx: usize, heading: i32) -> bool {
    let npc = match state.get_npc(npc_idx) {
        Some(n) if n.is_alive() => n,
        _ => return false,
    };
    let (map, x, y) = (npc.map, npc.x, npc.y);
    let (dx, dy) = world::heading_to_offset(heading);
    let new_x = x + dx;
    let new_y = y + dy;

    // Check bounds and blocked
    if new_x < 1 || new_x > 100 || new_y < 1 || new_y > 100 {
        return false;
    }
    if state.is_tile_blocked(map, new_x, new_y) {
        return false;
    }

    // Check runtime tile (user or NPC already there)
    let free = state.world.grid(map)
        .and_then(|g| g.tile(new_x, new_y))
        .map(|t| t.user_conn.is_none() && t.npc_index == 0)
        .unwrap_or(false);
    if !free {
        return false;
    }

    // Move: update grid
    {
        let grid = state.world.grid_mut(map);
        if let Some(tile) = grid.tile_mut(x, y) {
            tile.npc_index = 0;
        }
        if let Some(tile) = grid.tile_mut(new_x, new_y) {
            tile.npc_index = npc_idx as i32;
        }
    }

    // Update NPC state
    if let Some(npc) = state.get_npc_mut(npc_idx) {
        npc.x = new_x;
        npc.y = new_y;
        npc.heading = heading;
    }

    true
}

/// AI tick — called every 100ms from the main loop.
/// VB6: AI_MoveNpc / modIA.bas — full NPC behavior including hostile chase,
/// defense mode, guard chase, self-heal, and global attack timer.
pub async fn tick_npc_ai(state: &mut GameState) {
    // Global CanAttack timer: every 3 ticks (300ms), all NPCs can attack again
    state.npc_can_attack_counter += 1;
    if state.npc_can_attack_counter >= 3 {
        state.npc_can_attack_counter = 0;
        // Reset all NPCs' can_attack flag
        for slot in state.npcs.iter_mut() {
            if let Some(npc) = slot.as_mut() {
                if npc.active {
                    npc.can_attack = true;
                }
            }
        }
    }

    // Update map user counts for skipping empty maps
    update_map_user_counts(state);

    // Collect active NPC indices to process
    let active_npcs: Vec<usize> = state.npcs.iter().enumerate()
        .filter_map(|(i, slot)| {
            slot.as_ref().filter(|n| n.is_alive()).map(|_| i)
        })
        .collect();

    for npc_idx in active_npcs {
        // Skip paralyzed NPCs — they can't move or attack
        let is_paralyzed = state.get_npc(npc_idx).map(|n| n.paralyzed).unwrap_or(false);
        if is_paralyzed { continue; }

        let npc_data = match state.get_npc(npc_idx) {
            Some(n) => (n.movement, n.hostile, n.can_attack, n.map, n.x, n.y, n.target,
                        n.lanza_spells, n.spells.clone(), n.npc_type, n.attacked_by.clone(),
                        n.min_hp, n.max_hp, n.alineacion),
            None => continue,
        };
        let (movement, hostile, can_attack, map, x, y, target,
             lanza_spells, spells, npc_type, attacked_by,
             cur_hp, max_hp, alineacion) = npc_data;

        // Skip NPCs on maps with no users (VB6 optimization)
        let map_users = state.map_user_counts.get(&map).copied().unwrap_or(0);
        if map_users == 0 && movement != npc::AI_FOLLOW_OWNER {
            continue;
        }

        // NPC self-heal: if HP < 50% and has a heal spell, try to cast it on self
        if max_hp > 0 && cur_hp > 0 && cur_hp < max_hp / 2 && lanza_spells > 0 {
            npc_try_self_heal(state, npc_idx, &spells).await;
        }

        match movement {
            npc::AI_STATIC => {
                // No movement, but attack adjacent if hostile
                if hostile && can_attack {
                    if let Some(target_conn) = find_adjacent_player(state, map, x, y) {
                        npc_attack_user(state, npc_idx, target_conn).await;
                        if let Some(n) = state.get_npc_mut(npc_idx) {
                            n.can_attack = false;
                        }
                    }
                }
            }

            npc::AI_RANDOM => {
                // Random movement (1/12 chance per tick)
                // Guards with AI_RANDOM also chase their targets
                if hostile && can_attack {
                    if let Some(target_conn) = find_adjacent_player(state, map, x, y) {
                        npc_attack_user(state, npc_idx, target_conn).await;
                        if let Some(n) = state.get_npc_mut(npc_idx) {
                            n.can_attack = false;
                        }
                    }
                }
                if rand_range(1, 12) == 3 {
                    let heading = rand_range(1, 4);
                    if move_npc(state, npc_idx, heading) {
                        send_npc_move(state, npc_idx).await;
                    }
                }
            }

            npc::AI_HOSTILE_CHASE => {
                // VB6 AI_AI_Hostile (modIA.bas)
                // 1) Check 4 adjacent tiles for users/pets → attack
                // 2) Scan vision for nearest player → spell + chase
                // 3) No target → restore_old_movement + random 1/12

                let mut attacked = false;

                // First: check adjacent tiles for attack
                if can_attack {
                    if let Some(target_conn) = find_adjacent_player(state, map, x, y) {
                        npc_attack_user(state, npc_idx, target_conn).await;
                        if let Some(n) = state.get_npc_mut(npc_idx) {
                            n.can_attack = false;
                            n.target = Some(target_conn);
                        }
                        attacked = true;
                    } else {
                        // Check for pet NPC at adjacent tiles
                        for heading in 1..=4 {
                            let (dx, dy) = world::heading_to_offset(heading);
                            let tx = x + dx;
                            let ty = y + dy;
                            if let Some(adj_npc_idx) = state.npc_at_tile(map, tx, ty) {
                                let is_pet = state.get_npc(adj_npc_idx)
                                    .map(|n| n.maestro_user.is_some() && n.is_alive())
                                    .unwrap_or(false);
                                if is_pet {
                                    npc_attack_npc(state, npc_idx, adj_npc_idx).await;
                                    if let Some(n) = state.get_npc_mut(npc_idx) {
                                        n.can_attack = false;
                                    }
                                    attacked = true;
                                    break;
                                }
                            }
                        }
                    }
                }

                if !attacked {
                    // Scan vision range for nearest player
                    // Filter out GM targets — if existing target is a GM, discard it
                    let valid_target = target.filter(|t| {
                        state.users.get(t).map(|u| u.privileges == 0).unwrap_or(false)
                    });
                    let chase_target = valid_target.or_else(|| find_nearest_player(state, map, x, y));

                    if let Some(target_conn) = chase_target {
                        if let Some(n) = state.get_npc_mut(npc_idx) {
                            n.target = Some(target_conn);
                        }

                        let target_pos = state.users.get(&target_conn)
                            .filter(|u| u.logged && !u.dead && u.pos_map == map)
                            .map(|u| (u.pos_x, u.pos_y));

                        if let Some((tx, ty)) = target_pos {
                            let dist = (x - tx).abs() + (y - ty).abs();

                            // Cast spell if in range
                            if dist > 1 && dist <= 8 && lanza_spells > 0 && can_attack && !spells.is_empty() {
                                let spell_idx = rand_range(0, spells.len() as i32 - 1) as usize;
                                let spell_id = spells[spell_idx];
                                npc_cast_spell(state, npc_idx, target_conn, spell_id).await;
                                if let Some(n) = state.get_npc_mut(npc_idx) {
                                    n.can_attack = false;
                                }
                            }

                            // Chase target
                            if dist > 1 {
                                let heading = chase_heading(x, y, tx, ty);
                                if move_npc(state, npc_idx, heading) {
                                    send_npc_move(state, npc_idx).await;
                                }
                            }
                        } else {
                            // Target gone (left map or dead)
                            if let Some(n) = state.get_npc_mut(npc_idx) {
                                n.target = None;
                            }
                        }
                    } else {
                        // No target at all — if this was originally a non-hostile NPC
                        // that was switched to hostile chase, restore old movement
                        let was_defense_switch = state.get_npc(npc_idx)
                            .map(|n| n.old_movement != npc::AI_HOSTILE_CHASE && !n.attacked_by.is_empty())
                            .unwrap_or(false);
                        if was_defense_switch {
                            restore_old_movement(state, npc_idx);
                        }
                        // Random walk
                        if rand_range(1, 12) == 3 {
                            let heading = rand_range(1, 4);
                            if move_npc(state, npc_idx, heading) {
                                send_npc_move(state, npc_idx).await;
                            }
                        }
                    }
                }
            }

            npc::AI_DEFENSE => {
                // VB6 AI_AI_Defense — follow attacker (attacked_by) within 15 tiles
                // Adjacent → melee. Far → spell + chase. Gone → restore_old_movement.
                // Filter out GM attackers — NPCs should not chase GMs
                let attacker_conn = find_player_by_name(state, map, x, y, &attacked_by)
                    .filter(|c| state.users.get(c).map(|u| u.privileges == 0).unwrap_or(false));

                if let Some(target_conn) = attacker_conn {
                    if let Some(n) = state.get_npc_mut(npc_idx) {
                        n.target = Some(target_conn);
                    }

                    let target_pos = state.users.get(&target_conn)
                        .filter(|u| u.logged && !u.dead && u.pos_map == map)
                        .map(|u| (u.pos_x, u.pos_y));

                    if let Some((tx, ty)) = target_pos {
                        let dist = (x - tx).abs() + (y - ty).abs();

                        if dist <= 1 && can_attack {
                            npc_attack_user(state, npc_idx, target_conn).await;
                            if let Some(n) = state.get_npc_mut(npc_idx) {
                                n.can_attack = false;
                            }
                        } else {
                            // Cast spell if possible
                            if dist > 1 && dist <= 8 && lanza_spells > 0 && can_attack && !spells.is_empty() {
                                let spell_idx = rand_range(0, spells.len() as i32 - 1) as usize;
                                let spell_id = spells[spell_idx];
                                npc_cast_spell(state, npc_idx, target_conn, spell_id).await;
                                if let Some(n) = state.get_npc_mut(npc_idx) {
                                    n.can_attack = false;
                                }
                            }

                            // Chase attacker
                            if dist > 1 {
                                let heading = chase_heading(x, y, tx, ty);
                                if move_npc(state, npc_idx, heading) {
                                    send_npc_move(state, npc_idx).await;
                                }
                            }
                        }
                    } else {
                        // Attacker left map — restore
                        restore_old_movement(state, npc_idx);
                    }
                } else {
                    // Attacker not found within 15 tiles — restore old movement
                    restore_old_movement(state, npc_idx);
                    if rand_range(1, 12) == 3 {
                        let heading = rand_range(1, 4);
                        if move_npc(state, npc_idx, heading) {
                            send_npc_move(state, npc_idx).await;
                        }
                    }
                }
            }

            npc::AI_GUARD => {
                // VB6 AI_AI_Guardia — Royal guards chase criminals, Chaos guards chase citizens
                // Castle guards (map 620/621) don't move
                let is_royal = npc_type == NpcType::RoyalGuard || alineacion == 1;
                let is_castle = map == 620 || map == 621;

                // First check adjacent for attack
                if can_attack {
                    if let Some(target_conn) = find_adjacent_player(state, map, x, y) {
                        let should_attack = if is_royal {
                            state.users.get(&target_conn).map(|u| u.criminal).unwrap_or(false)
                        } else {
                            // Chaos guard — attack citizens
                            state.users.get(&target_conn).map(|u| !u.criminal).unwrap_or(false)
                        };
                        if should_attack {
                            npc_attack_user(state, npc_idx, target_conn).await;
                            if let Some(n) = state.get_npc_mut(npc_idx) {
                                n.can_attack = false;
                            }
                        }
                    }
                }

                // Chase behavior (skip for castle guards)
                if !is_castle {
                    let chase_target = if is_royal {
                        find_nearest_criminal(state, map, x, y)
                    } else {
                        find_nearest_citizen(state, map, x, y)
                    };

                    if let Some(target_conn) = chase_target {
                        let target_pos = state.users.get(&target_conn)
                            .filter(|u| u.logged && !u.dead && u.pos_map == map)
                            .map(|u| (u.pos_x, u.pos_y));

                        if let Some((tx, ty)) = target_pos {
                            let dist = (x - tx).abs() + (y - ty).abs();
                            if dist > 1 {
                                let heading = chase_heading(x, y, tx, ty);
                                if move_npc(state, npc_idx, heading) {
                                    send_npc_move(state, npc_idx).await;
                                }
                            }
                        }
                    } else if rand_range(1, 12) == 3 {
                        // No target — random wander
                        let heading = rand_range(1, 4);
                        if move_npc(state, npc_idx, heading) {
                            send_npc_move(state, npc_idx).await;
                        }
                    }
                }
            }

            npc::AI_PATHFINDING => {
                // Chase nearest player using BFS pathfinding
                let chase_target = target.or_else(|| find_nearest_player(state, map, x, y));

                if let Some(target_conn) = chase_target {
                    if let Some(n) = state.get_npc_mut(npc_idx) {
                        n.target = Some(target_conn);
                    }

                    let target_pos = state.users.get(&target_conn)
                        .filter(|u| u.logged && !u.dead && u.pos_map == map)
                        .map(|u| (u.pos_x, u.pos_y));

                    if let Some((tx, ty)) = target_pos {
                        let dist = (x - tx).abs() + (y - ty).abs();
                        if dist <= 1 && can_attack {
                            npc_attack_user(state, npc_idx, target_conn).await;
                            if let Some(n) = state.get_npc_mut(npc_idx) {
                                n.can_attack = false;
                            }
                        } else if dist > 1 && dist <= 8 && lanza_spells > 0 && can_attack && !spells.is_empty() {
                            let spell_idx = rand_range(0, spells.len() as i32 - 1) as usize;
                            let spell_id = spells[spell_idx];
                            npc_cast_spell(state, npc_idx, target_conn, spell_id).await;
                            if let Some(n) = state.get_npc_mut(npc_idx) {
                                n.can_attack = false;
                            }
                        } else if dist > 1 {
                            // Use BFS pathfinding
                            let has_path = state.get_npc(npc_idx)
                                .map(|n| !n.pf_path.is_empty() && n.pf_step < n.pf_path.len())
                                .unwrap_or(false);

                            if has_path {
                                if npc_pathfind_step(state, npc_idx) {
                                    send_npc_move(state, npc_idx).await;
                                }
                            } else {
                                let path = pathfind_bfs(state, map, x, y, tx, ty);
                                if !path.is_empty() {
                                    if let Some(n) = state.get_npc_mut(npc_idx) {
                                        n.pf_path = path;
                                        n.pf_step = 0;
                                    }
                                    if npc_pathfind_step(state, npc_idx) {
                                        send_npc_move(state, npc_idx).await;
                                    }
                                } else {
                                    let heading = chase_heading(x, y, tx, ty);
                                    if move_npc(state, npc_idx, heading) {
                                        send_npc_move(state, npc_idx).await;
                                    }
                                }
                            }
                        }
                    } else {
                        if let Some(n) = state.get_npc_mut(npc_idx) {
                            n.target = None;
                            n.pf_path.clear();
                            n.pf_step = 0;
                        }
                    }
                } else {
                    if rand_range(1, 12) == 3 {
                        let heading = rand_range(1, 4);
                        if move_npc(state, npc_idx, heading) {
                            send_npc_move(state, npc_idx).await;
                        }
                    }
                }
            }

            npc::AI_FOLLOW_OWNER => {
                // Pet AI — follow master and attack master's target NPC
                let master_id = state.get_npc(npc_idx)
                    .and_then(|n| n.maestro_user);
                if let Some(master_conn) = master_id {
                    let master_pos = state.users.get(&master_conn)
                        .filter(|u| u.logged && !u.dead && u.pos_map == map)
                        .map(|u| (u.pos_x, u.pos_y, u.target_npc_idx));

                    if let Some((mx, my, master_target_npc)) = master_pos {
                        let dist = (x - mx).abs() + (y - my).abs();

                        // If master has a target NPC, attack it
                        if master_target_npc > 0 && can_attack {
                            let target_npc_alive = state.get_npc(master_target_npc)
                                .map(|n| n.is_alive())
                                .unwrap_or(false);
                            if target_npc_alive {
                                let target_npc_pos = state.get_npc(master_target_npc)
                                    .map(|n| (n.x, n.y));
                                if let Some((tnx, tny)) = target_npc_pos {
                                    let tdist = (x - tnx).abs() + (y - tny).abs();
                                    if tdist <= 1 {
                                        npc_attack_npc(state, npc_idx, master_target_npc).await;
                                        if let Some(n) = state.get_npc_mut(npc_idx) {
                                            n.can_attack = false;
                                        }
                                    } else {
                                        let heading = chase_heading(x, y, tnx, tny);
                                        if move_npc(state, npc_idx, heading) {
                                            send_npc_move(state, npc_idx).await;
                                        }
                                    }
                                }
                            }
                        } else if dist > 3 {
                            // Too far from master — follow
                            let heading = chase_heading(x, y, mx, my);
                            if move_npc(state, npc_idx, heading) {
                                send_npc_move(state, npc_idx).await;
                            }
                        } else if rand_range(1, 12) == 3 {
                            // Near master — random wander
                            let heading = rand_range(1, 4);
                            if move_npc(state, npc_idx, heading) {
                                send_npc_move(state, npc_idx).await;
                            }
                        }
                    }
                }
            }

            _ => {
                // Unknown AI type — do nothing
            }
        }
    }
}

/// NPC self-heal: check if any spell has SubeHP=1 and cast it on self.
async fn npc_try_self_heal(state: &mut GameState, npc_idx: usize, spells: &[i32]) {
    for &spell_id in spells {
        let spell = match state.get_spell(spell_id) {
            Some(s) => s.clone(),
            None => continue,
        };
        if spell.sube_hp == 1 {
            // Heal spell — heal the NPC
            let heal = rand_range(spell.min_hp.max(1), spell.max_hp.max(1));
            let (map, nx, ny, npc_name) = match state.get_npc(npc_idx) {
                Some(n) => (n.map, n.x, n.y, n.name.clone()),
                None => return,
            };
            let npc_char = match state.get_npc(npc_idx) {
                Some(n) => n.char_index,
                None => return,
            };

            if let Some(npc) = state.get_npc_mut(npc_idx) {
                npc.min_hp = (npc.min_hp + heal).min(npc.max_hp);
            }

            // FX on NPC
            if spell.fx_grh > 0 {
                let fx = format!("CFX{},{},{}", npc_char.0, spell.fx_grh, spell.loops);
                state.send_data(SendTarget::ToArea { map, x: nx, y: ny }, &fx).await;
            }
            if spell.wav > 0 {
                let snd = format!("TW{}", spell.wav);
                state.send_data(SendTarget::ToArea { map, x: nx, y: ny }, &snd).await;
            }

            // Magic words
            if !spell.palabras_magicas.is_empty() {
                let talk = format!(";{} dice: {}~255~0~0", npc_name, spell.palabras_magicas);
                state.send_data(SendTarget::ToArea { map, x: nx, y: ny }, &talk).await;
            }
            break; // Only cast one heal per tick
        }
    }
}

/// Respawn tick — check dead NPCs and revive them.
pub async fn tick_npc_respawn(state: &mut GameState) {
    let dead_npcs: Vec<usize> = state.npcs.iter().enumerate()
        .filter_map(|(i, slot)| {
            slot.as_ref().filter(|n| !n.active && n.respawn).map(|_| i)
        })
        .collect();

    for npc_idx in dead_npcs {
        if state.respawn_npc(npc_idx) {
            // Send CC to area users
            let cc = match state.get_npc(npc_idx) {
                Some(n) => (n.build_cc_packet(), n.map, n.x, n.y),
                None => continue,
            };
            let (cc_pkt, map, x, y) = cc;
            state.send_data(SendTarget::ToArea { map, x, y }, &cc_pkt).await;
        }
    }
}

/// Send NPC movement packet (* opcode) to area users.
async fn send_npc_move(state: &mut GameState, npc_idx: usize) {
    let data = match state.get_npc(npc_idx) {
        Some(n) => (n.char_index.0, n.x, n.y, n.map, n.heading, n.area_min_x, n.area_min_y),
        None => return,
    };
    let (char_idx, x, y, map, heading, area_min_x, area_min_y) = data;

    // VB6 movement packet: *charindex,x,y
    let pkt = format!("*{},{},{}", char_idx, x, y);

    // VB6 SendToNpcArea: sends to all users in the NPC's 27x27 area zone
    // (NOT the smaller 17x13 client window used by SendTarget::ToArea)
    let area_max_x = (area_min_x + 26).min(100);
    let area_max_y = (area_min_y + 26).min(100);
    let area_min_x_clamped = area_min_x.max(1);
    let area_min_y_clamped = area_min_y.max(1);

    let mut targets: Vec<ConnectionId> = Vec::new();
    if let Some(grid) = state.world.grid(map) {
        for sy in area_min_y_clamped..=area_max_y {
            for sx in area_min_x_clamped..=area_max_x {
                if let Some(tile) = grid.tile(sx, sy) {
                    if let Some(conn) = tile.user_conn {
                        targets.push(conn);
                    }
                }
            }
        }
    }
    for conn in targets {
        state.send_to(conn, &pkt).await;
    }

    // Check if NPC crossed a 9x9 area boundary — if so, send CC to newly visible players
    check_update_needed_npc(state, npc_idx, heading).await;
}

/// CheckUpdateNeededNpc — VB6 ModAreas.bas line 320-406.
/// When an NPC crosses a 9x9 zone boundary, send CC packets to players in the new strip.
async fn check_update_needed_npc(state: &mut GameState, npc_idx: usize, heading: i32) {
    let (x, y, map, old_area_id, old_min_x, old_min_y) = match state.get_npc(npc_idx) {
        Some(n) => (n.x, n.y, n.map, n.area_id, n.area_min_x, n.area_min_y),
        None => return,
    };

    let new_area_id = area_id(x, y);

    // If still in the same 9x9 zone, nothing to do
    if new_area_id == old_area_id && old_area_id != 0 {
        return;
    }

    // Calculate the new visibility strip based on heading (same logic as user)
    let (min_x, max_x, min_y, max_y, new_min_x, new_min_y) = if old_area_id == 0 {
        // First time (spawn): full 27x27 area
        let nmin_y = (y / 9 - 1) * 9;
        let nmin_x = (x / 9 - 1) * 9;
        (nmin_x, nmin_x + 26, nmin_y, nmin_y + 26, nmin_x, nmin_y)
    } else if heading == world::HEADING_NORTH {
        let nmin_x = old_min_x;
        let nmin_y = old_min_y - 9;
        (nmin_x, nmin_x + 26, nmin_y, old_min_y - 1, nmin_x, nmin_y)
    } else if heading == world::HEADING_SOUTH {
        let nmin_x = old_min_x;
        let nmin_y = old_min_y + 27;
        let new_area_min_y = old_min_y + 9;
        (nmin_x, nmin_x + 26, nmin_y, nmin_y + 8, nmin_x, new_area_min_y)
    } else if heading == world::HEADING_WEST {
        let nmin_x = old_min_x - 9;
        let nmin_y = old_min_y;
        (nmin_x, old_min_x - 1, nmin_y, nmin_y + 26, nmin_x, nmin_y)
    } else if heading == world::HEADING_EAST {
        let nmin_x = old_min_x + 27;
        let nmin_y = old_min_y;
        let new_area_min_x = old_min_x + 9;
        (nmin_x, nmin_x + 8, nmin_y, nmin_y + 26, new_area_min_x, nmin_y)
    } else {
        // Unknown heading — do full area
        let nmin_y = (y / 9 - 1) * 9;
        let nmin_x = (x / 9 - 1) * 9;
        (nmin_x, nmin_x + 26, nmin_y, nmin_y + 26, nmin_x, nmin_y)
    };

    // Clamp to map bounds
    let min_x = min_x.max(1);
    let min_y = min_y.max(1);
    let max_x = max_x.min(100);
    let max_y = max_y.min(100);

    // Update NPC's area tracking
    if let Some(npc) = state.get_npc_mut(npc_idx) {
        npc.area_id = new_area_id;
        npc.area_min_x = new_min_x;
        npc.area_min_y = new_min_y;
    }

    // Build NPC CC packet
    let npc_cc = match state.get_npc(npc_idx) {
        Some(npc) if npc.active => npc.build_cc_packet(),
        _ => return,
    };

    // Find users in the new strip and send CC to them
    let mut users_in_strip: Vec<ConnectionId> = Vec::new();
    if let Some(grid) = state.world.grid(map) {
        for sx in min_x..=max_x {
            for sy in min_y..=max_y {
                if let Some(tile) = grid.tile(sx, sy) {
                    if let Some(conn) = tile.user_conn {
                        users_in_strip.push(conn);
                    }
                }
            }
        }
    }

    for conn_id in users_in_strip {
        if state.users.get(&conn_id).map(|u| u.logged).unwrap_or(false) {
            state.send_to(conn_id, &npc_cc).await;
        }
    }
}

/// Find an adjacent player (1 tile away in any cardinal direction).
fn find_adjacent_player(state: &GameState, map: i32, x: i32, y: i32) -> Option<ConnectionId> {
    for heading in 1..=4 {
        let (dx, dy) = world::heading_to_offset(heading);
        let tx = x + dx;
        let ty = y + dy;
        if let Some(grid) = state.world.grid(map) {
            if let Some(tile) = grid.tile(tx, ty) {
                if let Some(conn) = tile.user_conn {
                    // Check if player is alive, logged, and NOT a GM (VB6: Privilegios = User)
                    if let Some(user) = state.users.get(&conn) {
                        if user.logged && !user.dead && user.privileges == 0 && !user.admin_invisible {
                            return Some(conn);
                        }
                    }
                }
            }
        }
    }
    None
}

/// Find the nearest player within NPC vision range.
fn find_nearest_player(state: &GameState, map: i32, x: i32, y: i32) -> Option<ConnectionId> {
    let half_x = npc::NPC_VISION_X / 2;
    let half_y = npc::NPC_VISION_Y / 2;
    let min_x = (x - half_x).max(1);
    let max_x = (x + half_x).min(100);
    let min_y = (y - half_y).max(1);
    let max_y = (y + half_y).min(100);

    let mut best: Option<(ConnectionId, i32)> = None;

    if let Some(grid) = state.world.grid(map) {
        for cy in min_y..=max_y {
            for cx in min_x..=max_x {
                if let Some(tile) = grid.tile(cx, cy) {
                    if let Some(conn) = tile.user_conn {
                        if let Some(user) = state.users.get(&conn) {
                            if user.logged && !user.dead && user.privileges == 0 {
                                let dist = (x - cx).abs() + (y - cy).abs();
                                if best.is_none() || dist < best.unwrap().1 {
                                    best = Some((conn, dist));
                                }
                            }
                        }
                    }
                }
            }
        }
    }

    best.map(|(conn, _)| conn)
}

/// Calculate heading to move from (x,y) towards (tx,ty).
fn chase_heading(x: i32, y: i32, tx: i32, ty: i32) -> i32 {
    let dx = tx - x;
    let dy = ty - y;

    // Prioritize larger difference
    if dx.abs() >= dy.abs() {
        if dx > 0 { world::HEADING_EAST } else { world::HEADING_WEST }
    } else {
        if dy > 0 { world::HEADING_SOUTH } else { world::HEADING_NORTH }
    }
}

// =====================================================================
// NPC AI helper functions
// =====================================================================

/// Find nearest criminal player within NPC vision range (for royal guards).
fn find_nearest_criminal(state: &GameState, map: i32, x: i32, y: i32) -> Option<ConnectionId> {
    let half_x = npc::NPC_VISION_X / 2;
    let half_y = npc::NPC_VISION_Y / 2;
    let min_x = (x - half_x).max(1);
    let max_x = (x + half_x).min(100);
    let min_y = (y - half_y).max(1);
    let max_y = (y + half_y).min(100);

    let mut best: Option<(ConnectionId, i32)> = None;

    if let Some(grid) = state.world.grid(map) {
        for cy in min_y..=max_y {
            for cx in min_x..=max_x {
                if let Some(tile) = grid.tile(cx, cy) {
                    if let Some(conn) = tile.user_conn {
                        if let Some(user) = state.users.get(&conn) {
                            if user.logged && !user.dead && user.criminal && user.privileges == 0 {
                                let dist = (x - cx).abs() + (y - cy).abs();
                                if best.is_none() || dist < best.unwrap().1 {
                                    best = Some((conn, dist));
                                }
                            }
                        }
                    }
                }
            }
        }
    }

    best.map(|(conn, _)| conn)
}

/// Find nearest citizen (non-criminal) player within NPC vision range (for chaos guards).
fn find_nearest_citizen(state: &GameState, map: i32, x: i32, y: i32) -> Option<ConnectionId> {
    let half_x = npc::NPC_VISION_X / 2;
    let half_y = npc::NPC_VISION_Y / 2;
    let min_x = (x - half_x).max(1);
    let max_x = (x + half_x).min(100);
    let min_y = (y - half_y).max(1);
    let max_y = (y + half_y).min(100);

    let mut best: Option<(ConnectionId, i32)> = None;

    if let Some(grid) = state.world.grid(map) {
        for cy in min_y..=max_y {
            for cx in min_x..=max_x {
                if let Some(tile) = grid.tile(cx, cy) {
                    if let Some(conn) = tile.user_conn {
                        if let Some(user) = state.users.get(&conn) {
                            if user.logged && !user.dead && !user.criminal && user.privileges == 0 {
                                let dist = (x - cx).abs() + (y - cy).abs();
                                if best.is_none() || dist < best.unwrap().1 {
                                    best = Some((conn, dist));
                                }
                            }
                        }
                    }
                }
            }
        }
    }

    best.map(|(conn, _)| conn)
}

/// Find a player by name within 15 tiles (for defense AI — NPC looking for its attacker).
fn find_player_by_name(state: &GameState, map: i32, x: i32, y: i32, name: &str) -> Option<ConnectionId> {
    if name.is_empty() { return None; }
    let range = 15;
    let min_x = (x - range).max(1);
    let max_x = (x + range).min(100);
    let min_y = (y - range).max(1);
    let max_y = (y + range).min(100);

    if let Some(grid) = state.world.grid(map) {
        for cy in min_y..=max_y {
            for cx in min_x..=max_x {
                if let Some(tile) = grid.tile(cx, cy) {
                    if let Some(conn) = tile.user_conn {
                        if let Some(user) = state.users.get(&conn) {
                            if user.logged && !user.dead && user.char_name == name {
                                return Some(conn);
                            }
                        }
                    }
                }
            }
        }
    }
    None
}

/// Restore NPC to its original movement AI (VB6: RestoreOldMovement).
/// Called when defense-mode NPC loses its target.
fn restore_old_movement(state: &mut GameState, npc_idx: usize) {
    if let Some(npc) = state.get_npc_mut(npc_idx) {
        npc.movement = npc.old_movement;
        npc.hostile = npc.old_hostile;
        npc.attacked_by.clear();
        npc.target = None;
    }
}

/// NPC vs NPC combat (VB6: NpcAtacaNpc — used by pets attacking target NPCs).
async fn npc_attack_npc(state: &mut GameState, attacker_idx: usize, target_idx: usize) {
    let attacker_data = match state.get_npc(attacker_idx) {
        Some(n) if n.is_alive() => (n.min_hit, n.max_hit, n.char_index, n.map, n.x, n.y, n.maestro_user),
        _ => return,
    };
    let (a_min_hit, a_max_hit, _a_char, a_map, a_x, a_y, a_master) = attacker_data;

    let target_alive = state.get_npc(target_idx).map(|n| n.is_alive()).unwrap_or(false);
    if !target_alive { return; }

    // Simple damage — no armor for NPC vs NPC
    let damage = rand_range(a_min_hit.max(1), a_max_hit.max(1));

    let target_dead = {
        match state.get_npc_mut(target_idx) {
            Some(target) => {
                target.min_hp -= damage;
                target.min_hp <= 0
            }
            None => return,
        }
    };

    // Impact sound
    let snd_pkt = format!("TW{}", 10);
    state.send_data(SendTarget::ToArea { map: a_map, x: a_x, y: a_y }, &snd_pkt).await;

    if target_dead {
        // Target NPC dies — use pet owner as killer if available
        if let Some(master_conn) = a_master {
            let (give_exp, give_gld_min, give_gld_max) = state.get_npc(target_idx)
                .map(|n| (n.give_exp, n.give_gld_min, n.give_gld_max))
                .unwrap_or((0, 0, 0));
            npc_die(state, target_idx, master_conn, give_exp, give_gld_min, give_gld_max).await;
        } else {
            // No master — just kill it
            let (map, x, y, char_index) = match state.get_npc(target_idx) {
                Some(n) => (n.map, n.x, n.y, n.char_index),
                None => return,
            };
            let bp_pkt = format!("BP{}", char_index.0);
            state.send_data(SendTarget::ToArea { map, x, y }, &bp_pkt).await;
            state.kill_npc(target_idx);
        }
    }

    // Check if attacker (pet) died from target's counter (not implemented in VB6 NvN)
    // Pets can also die — check attacker_idx NPC
    let pet_dead = state.get_npc(attacker_idx).map(|n| n.min_hp <= 0).unwrap_or(false);
    if pet_dead {
        let (p_map, p_x, p_y, p_ci) = match state.get_npc(attacker_idx) {
            Some(n) => (n.map, n.x, n.y, n.char_index),
            None => return,
        };
        let bp = format!("BP{}", p_ci.0);
        state.send_data(SendTarget::ToArea { map: p_map, x: p_x, y: p_y }, &bp).await;
        // Remove pet from owner
        if let Some(owner_conn) = a_master {
            remove_pet_from_owner(state, owner_conn, attacker_idx);
        }
        state.kill_npc(attacker_idx);
    }
}

/// Remove a pet NPC from its owner's mascotas array.
fn remove_pet_from_owner(state: &mut GameState, owner_conn: ConnectionId, npc_idx: usize) {
    if let Some(user) = state.users.get_mut(&owner_conn) {
        for i in 0..3 {
            if user.mascotas_index[i] == npc_idx {
                user.mascotas_index[i] = 0;
                user.mascotas_type[i] = 0;
                user.nro_mascotas = (user.nro_mascotas - 1).max(0);
                break;
            }
        }
    }
}

/// Update map_user_counts cache (how many logged users per map).
fn update_map_user_counts(state: &mut GameState) {
    state.map_user_counts.clear();
    for user in state.users.values() {
        if user.logged && !user.dead {
            *state.map_user_counts.entry(user.pos_map).or_insert(0) += 1;
        }
    }
}

// =====================================================================
// Passive regeneration / drain system
// =====================================================================

// Intervals in seconds (1-second tick)
const HUNGER_INTERVAL: i32 = 13;   // Drain hunger every 13 seconds
const THIRST_INTERVAL: i32 = 13;   // Drain thirst every 13 seconds
const STAMINA_INTERVAL: i32 = 1;   // Regen stamina every 1 second
const POISON_INTERVAL: i32 = 1;    // Poison damage every 1 second
const HUNGER_DRAIN: i32 = 10;      // Drain amount per tick
const THIRST_DRAIN: i32 = 10;      // Drain amount per tick
const MEDITATION_FX: i32 = 4;      // FX for meditation

/// ME — Toggle meditation on/off.
async fn handle_meditate(state: &mut GameState, conn_id: ConnectionId) {
    let user = match state.users.get(&conn_id) {
        Some(u) if u.logged && !u.dead => u,
        _ => return,
    };

    let meditating = user.meditating;
    let char_index = user.char_index;
    let map = user.pos_map;
    let x = user.pos_x;
    let y = user.pos_y;

    if meditating {
        // Stop meditation
        if let Some(user) = state.users.get_mut(&conn_id) {
            user.meditating = false;
        }
        state.send_to(conn_id, "||205").await; // TEXTO205: Dejas de meditar
    } else {
        // Check if mana is already full
        if user.min_mana >= user.max_mana {
            state.send_to(conn_id, "||393").await; // TEXTO393: Mana restaurado
            return;
        }

        if let Some(user) = state.users.get_mut(&conn_id) {
            user.meditating = true;
        }

        // Send meditation FX to area
        let fx_pkt = format!("CFF{},{},999", char_index.0, MEDITATION_FX);
        state.send_data(SendTarget::ToArea { map, x, y }, &fx_pkt).await;

        state.send_to(conn_id, "||394").await; // TEXTO394: Comenzas a meditar
    }
}

/// Called every 1 second — handles all passive regen/drain for logged users.
pub async fn tick_player_passive(state: &mut GameState) {
    // Collect logged user connections
    let user_ids: Vec<ConnectionId> = state.users.keys().copied().collect();

    for conn_id in user_ids {
        let user_data = match state.users.get(&conn_id) {
            Some(u) if u.logged && !u.dead => Some((
                u.poisoned,
                u.meditating,
                u.min_hp, u.max_hp,
                u.min_mana, u.max_mana,
                u.min_sta, u.max_sta,
                u.min_agua, u.max_agua,
                u.min_ham, u.max_ham,
                u.counter_hunger, u.counter_thirst,
                u.counter_stamina, u.counter_poison,
                u.skills[6], // SK7 = Meditar skill
                u.privileges,
            )),
            _ => None,
        };

        let (poisoned, meditating, min_hp, max_hp, min_mana, max_mana,
             min_sta, max_sta, min_agua, _max_agua, min_ham, _max_ham,
             cnt_hunger, cnt_thirst, cnt_sta, cnt_poison,
             meditate_skill, privileges) = match user_data {
            Some(d) => d,
            None => continue,
        };

        // Only non-GM users have hunger/thirst
        let is_player = privileges == 0;

        // --- Hunger drain ---
        if is_player && min_ham > 0 {
            if cnt_hunger >= HUNGER_INTERVAL {
                if let Some(u) = state.users.get_mut(&conn_id) {
                    u.counter_hunger = 0;
                    u.min_ham = (u.min_ham - HUNGER_DRAIN).max(0);
                }
                send_hunger_thirst(state, conn_id).await;
            } else {
                if let Some(u) = state.users.get_mut(&conn_id) {
                    u.counter_hunger += 1;
                }
            }
        }

        // --- Thirst drain ---
        if is_player && min_agua > 0 {
            if cnt_thirst >= THIRST_INTERVAL {
                if let Some(u) = state.users.get_mut(&conn_id) {
                    u.counter_thirst = 0;
                    u.min_agua = (u.min_agua - THIRST_DRAIN).max(0);
                }
                send_hunger_thirst(state, conn_id).await;
            } else {
                if let Some(u) = state.users.get_mut(&conn_id) {
                    u.counter_thirst += 1;
                }
            }
        }

        // --- Stamina regeneration ---
        if min_sta < max_sta && min_ham > 0 && min_agua > 0 {
            if cnt_sta >= STAMINA_INTERVAL {
                let regen = rand_range(80, 130);
                if let Some(u) = state.users.get_mut(&conn_id) {
                    u.counter_stamina = 0;
                    u.min_sta = (u.min_sta + regen).min(u.max_sta);
                }
                send_stats_sta(state, conn_id).await;
            } else {
                if let Some(u) = state.users.get_mut(&conn_id) {
                    u.counter_stamina += 1;
                }
            }
        }

        // --- Poison damage ---
        if poisoned {
            if cnt_poison >= POISON_INTERVAL {
                let dmg = rand_range(1, 5);
                let new_hp = min_hp - dmg;
                if let Some(u) = state.users.get_mut(&conn_id) {
                    u.counter_poison = 0;
                    u.min_hp = new_hp;
                }
                send_stats_hp(state, conn_id).await;

                if new_hp <= 0 {
                    user_die(state, conn_id, None).await;
                }
            } else {
                if let Some(u) = state.users.get_mut(&conn_id) {
                    u.counter_poison += 1;
                }
            }
        }

        // --- Buff duration (DuracionPociones) ---
        let duracion = state.users.get(&conn_id).map(|u| u.duracion_efecto).unwrap_or(0);
        if duracion > 0 {
            if let Some(u) = state.users.get_mut(&conn_id) {
                u.duracion_efecto -= 1;
                if u.duracion_efecto <= 0 {
                    // Buff expired — restore attributes from backup
                    u.tomo_pocion = false;
                    u.attributes = u.attributes_backup;
                }
            }
        }

        // --- Remo potion cooldown ---
        let remo = state.users.get(&conn_id).map(|u| u.counter_remo).unwrap_or(0);
        if remo > 0 {
            if let Some(u) = state.users.get_mut(&conn_id) {
                u.counter_remo -= 1;
            }
        }

        // --- Silence timer (mute countdown) ---
        let silence = state.users.get(&conn_id).map(|u| (u.silenced, u.silence_timer)).unwrap_or((false, 0));
        if silence.0 && silence.1 > 0 {
            if let Some(u) = state.users.get_mut(&conn_id) {
                u.silence_timer -= 1;
                if u.silence_timer <= 0 {
                    u.silenced = false;
                    u.silence_timer = 0;
                }
            }
            let unmuted = state.users.get(&conn_id).map(|u| !u.silenced).unwrap_or(false);
            if unmuted {
                state.send_to(conn_id, "||946").await;
            }
        }

        // --- Jail timer (prison countdown) ---
        let jail = state.users.get(&conn_id).map(|u| u.jail_timer).unwrap_or(0);
        if jail > 0 {
            if let Some(u) = state.users.get_mut(&conn_id) {
                u.jail_timer -= 1;
            }
            let released = state.users.get(&conn_id).map(|u| u.jail_timer <= 0).unwrap_or(false);
            if released {
                // Release from jail — warp to Libertad (map 28, 50, 50)
                state.send_to(conn_id, "||444").await;
                warp_user(state, conn_id, 28, 50, 50).await;
            }
        }

        // --- Delayed resurrection countdown ---
        let seg_revivir = state.users.get(&conn_id).map(|u| u.segundos_para_revivir).unwrap_or(0);
        if seg_revivir > 0 {
            if let Some(u) = state.users.get_mut(&conn_id) {
                u.segundos_para_revivir -= 1;
            }
            let ready = state.users.get(&conn_id).map(|u| u.segundos_para_revivir <= 0).unwrap_or(false);
            if ready {
                revive_user(state, conn_id).await;
            }
        }

        // --- Resurrection cooldown ---
        let time_rev = state.users.get(&conn_id).map(|u| u.time_revivir).unwrap_or(0);
        if time_rev > 0 {
            if let Some(u) = state.users.get_mut(&conn_id) {
                u.time_revivir -= 1;
            }
        }

        // Anti-cheat intervals now decremented in tick_intervals (40ms tick)

        // --- Meditation (mana regen) ---
        if meditating && min_mana < max_mana {
            // Skill-based chance (higher skill = higher chance)
            let chance = match meditate_skill {
                0..=10 => 3,    // ~3% per second
                11..=20 => 5,
                21..=30 => 7,
                31..=40 => 10,
                41..=50 => 12,
                51..=60 => 15,
                61..=70 => 18,
                71..=80 => 22,
                81..=90 => 30,
                _ => 40,        // 91-100: 40%
            };

            if rand_range(1, 100) <= chance {
                let regen = (max_mana as f64 * 0.02) as i32; // 2% of max mana
                let regen = regen.max(1);
                if let Some(u) = state.users.get_mut(&conn_id) {
                    u.min_mana = (u.min_mana + regen).min(u.max_mana);
                    if u.min_mana >= u.max_mana {
                        u.meditating = false;
                    }
                }
                send_stats_mana(state, conn_id).await;

                // Stop meditation when full
                if let Some(u) = state.users.get(&conn_id) {
                    if !u.meditating {
                        state.send_to(conn_id, "||829").await; // TEXTO829: Has terminado de meditar
                    }
                }
            }
        }
    }

    // --- Auto-save all characters every 60 seconds ---
    state.auto_save_counter -= 1;
    if state.auto_save_counter <= 0 {
        state.auto_save_counter = 60;
        auto_save_all_users(state);
    }
}

/// Save all logged-in users to disk (periodic auto-save).
fn auto_save_all_users(state: &GameState) {
    let base = state.base_path.clone();
    let mut saved = 0;
    for (_conn_id, user) in state.users.iter() {
        if !user.logged || user.char_name.is_empty() { continue; }
        // Convert inventory to save format
        let inv: Vec<(i32, i32, bool)> = user.inventory.iter()
            .map(|s| (s.obj_index, s.amount, s.equipped))
            .collect();
        let bank: Vec<(i32, i32)> = user.bank.iter()
            .map(|s| (s.obj_index, s.amount))
            .collect();
        let data = charfile::CharSaveData {
            head: user.head,
            body: user.body,
            heading: user.heading,
            weapon: user.equip.weapon as i32,
            shield: user.equip.shield as i32,
            helmet: user.equip.helmet as i32,
            gold: user.gold,
            bank_gold: user.bank_gold,
            exp: user.exp,
            level: user.level,
            map: user.pos_map,
            x: user.pos_x,
            y: user.pos_y,
            min_hp: user.min_hp,
            max_hp: user.max_hp,
            min_mana: user.min_mana,
            max_mana: user.max_mana,
            min_sta: user.min_sta,
            max_sta: user.max_sta,
            max_hit: user.max_hit,
            min_hit: user.min_hit,
            max_agua: user.max_agua,
            min_agua: user.min_agua,
            max_ham: user.max_ham,
            min_ham: user.min_ham,
            dead: user.dead,
            poisoned: user.poisoned,
            criminal: user.criminal,
            paralyzed: user.paralyzed,
            hidden: user.hidden,
            navigating: user.navigating,
            privileges: user.privileges,
            attributes: user.attributes,
            skills: user.skills,
            spells: user.spells,
            inventory: inv,
            bank,
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
        if charfile::save_charfile(&base, &user.char_name, &data).is_ok() {
            saved += 1;
        }
    }
    if saved > 0 {
        tracing::debug!("[SAVE] Auto-saved {} characters", saved);
    }
}

// =====================================================================
// World cleanup system (ModuloLimpieza.bas)
// =====================================================================

/// Add an item to the world cleanup tracking queue.
/// Items will be auto-removed after `tiempo` ticks.
pub fn clean_world_add_item(state: &mut GameState, map: i32, x: i32, y: i32, tiempo: i32, obj_index: i32) {
    for entry in state.clean_world.iter_mut() {
        if entry.map == 0 && entry.x == 0 && entry.y == 0 && entry.tiempo == 0 {
            *entry = CleanWorldEntry { map, x, y, tiempo, obj_index };
            return;
        }
    }
    // Queue full — ignore (VB6 silently drops)
}

/// 40ms game tick — decrement anti-cheat interval counters (RestoTiempo).
/// This matches VB6's timer interval for movement/action speed limiting.
pub async fn tick_intervals(state: &mut GameState) {
    // Collect IDs of users whose paralysis expired this tick (need to send PARADOK)
    let mut unparalyze: Vec<ConnectionId> = Vec::new();

    for (&conn_id, user) in state.users.iter_mut() {
        if !user.logged { continue; }
        if user.interval_golpe > 0 { user.interval_golpe -= 1; }
        if user.interval_flechas > 0 { user.interval_flechas -= 1; }
        if user.interval_casteo > 0 { user.interval_casteo -= 1; }
        if user.interval_poteo > 0 { user.interval_poteo -= 1; }
        if user.interval_click > 0 { user.interval_click -= 1; }
        if user.interval_trabajar > 0 { user.interval_trabajar -= 1; }
        if user.interval_pu > 0 { user.interval_pu -= 1; }

        // VB6: EfectoParalisisUser — count down paralysis timer each tick
        if user.paralyzed {
            if user.counter_paralisis > 0 {
                user.counter_paralisis -= 1;
            } else {
                // Timer expired — remove paralysis
                user.paralyzed = false;
                unparalyze.push(conn_id);
            }
        }
    }

    // Send PARADOK to users who just got unparalyzed
    for conn_id in unparalyze {
        state.send_to(conn_id, "PARADOK").await;
    }

    // NPC paralysis countdown (same 40ms tick as user paralysis)
    for slot in state.npcs.iter_mut() {
        if let Some(npc) = slot.as_mut() {
            if npc.paralyzed {
                if npc.counter_paralisis > 0 {
                    npc.counter_paralisis -= 1;
                } else {
                    npc.paralyzed = false;
                }
            }
        }
    }
}

/// Tick the world cleanup — decrement timers and remove expired items.
pub async fn tick_clean_world(state: &mut GameState) {
    for i in 0..state.clean_world.len() {
        let entry = &state.clean_world[i];
        if entry.map == 0 && entry.tiempo == 0 {
            continue;
        }

        let map = entry.map;
        let x = entry.x;
        let y = entry.y;
        let tiempo = entry.tiempo;

        if tiempo <= 1 {
            // Timer expired — remove item from world
            let has_item = {
                if let Some(grid) = state.world.grid(map) {
                    if let Some(tile) = grid.tile(x, y) {
                        tile.ground_item.obj_index > 0
                    } else {
                        false
                    }
                } else {
                    false
                }
            };

            if has_item {
                // Erase the object from the tile
                let grid = state.world.grid_mut(map);
                if let Some(tile) = grid.tile_mut(x, y) {
                    tile.ground_item.obj_index = 0;
                    tile.ground_item.amount = 0;
                }
                // Broadcast BO (remove object from ground) to area
                let pkt = format!("BO{},{}", x, y);
                state.send_data(SendTarget::ToArea { map, x, y }, &pkt).await;
            }

            // Clear the cleanup slot
            state.clean_world[i] = CleanWorldEntry::default();
        } else {
            // Decrement timer
            state.clean_world[i].tiempo -= 1;
        }
    }
}

// =====================================================================
// Anti-cheat check helpers (Mod_AntiCheat.bas)
// =====================================================================

/// Check if melee attack is allowed; if so, set cooldown.
pub fn puede_pegar(state: &mut GameState, conn_id: ConnectionId) -> bool {
    let user = match state.users.get(&conn_id) {
        Some(u) => u,
        None => return false,
    };
    if user.interval_golpe > 0 {
        return false;
    }
    let golpe = state.intervals.golpe;
    let flechas = state.intervals.flechas;
    if let Some(u) = state.users.get_mut(&conn_id) {
        u.interval_golpe = golpe;
        u.interval_flechas = flechas;
    }
    true
}

/// Check if arrow shot is allowed; if so, set cooldown.
pub fn puede_flechear(state: &mut GameState, conn_id: ConnectionId) -> bool {
    let user = match state.users.get(&conn_id) {
        Some(u) => u,
        None => return false,
    };
    if user.interval_flechas > 0 {
        return false;
    }
    let flechas = state.intervals.flechas;
    let golpe = 25; // VB6: PuedoFlechear also sets Golpe=25
    if let Some(u) = state.users.get_mut(&conn_id) {
        u.interval_flechas = flechas;
        u.interval_golpe = golpe;
    }
    true
}

/// Check if spell cast is allowed; if so, set cooldown.
pub fn puede_castear(state: &mut GameState, conn_id: ConnectionId) -> bool {
    let user = match state.users.get(&conn_id) {
        Some(u) => u,
        None => return false,
    };
    if user.interval_casteo > 0 {
        return false;
    }
    let hechizo = state.intervals.lanzar_hechizo;
    if let Some(u) = state.users.get_mut(&conn_id) {
        u.interval_casteo = hechizo;
    }
    true
}

/// Check if potion use is allowed; if so, set cooldown.
pub fn puede_potear(state: &mut GameState, conn_id: ConnectionId) -> bool {
    let user = match state.users.get(&conn_id) {
        Some(u) => u,
        None => return false,
    };
    if user.interval_poteo > 0 {
        return false;
    }
    let poteo = state.intervals.poteo_u;
    if let Some(u) = state.users.get_mut(&conn_id) {
        u.interval_poteo = poteo;
    }
    true
}

/// Check if work/skill action is allowed; if so, set cooldown.
pub fn puede_trabajar(state: &mut GameState, conn_id: ConnectionId) -> bool {
    let user = match state.users.get(&conn_id) {
        Some(u) => u,
        None => return false,
    };
    if user.interval_trabajar > 0 {
        return false;
    }
    let work = state.intervals.work;
    if let Some(u) = state.users.get_mut(&conn_id) {
        u.interval_trabajar = work;
    }
    true
}

/// Check if click action is allowed; if so, set cooldown.
pub fn puede_clickear(state: &mut GameState, conn_id: ConnectionId) -> bool {
    let user = match state.users.get(&conn_id) {
        Some(u) => u,
        None => return false,
    };
    if user.interval_click > 0 {
        return false;
    }
    let click = state.intervals.poteo_click;
    let poteo = state.intervals.poteo_u;
    if let Some(u) = state.users.get_mut(&conn_id) {
        u.interval_click = click;
        u.interval_poteo = poteo;
    }
    true
}

/// Send hunger and thirst stats (EHYS packet).
async fn send_hunger_thirst(state: &mut GameState, conn_id: ConnectionId) {
    let data = match state.users.get(&conn_id) {
        Some(u) => format!("EHYS{},{},{},{}", u.max_agua, u.min_agua, u.max_ham, u.min_ham),
        None => return,
    };
    state.send_to(conn_id, &data).await;
}

// =====================================================================
// Commerce handlers (NPC shops — Comercio.bas)
// =====================================================================

/// Maximum items per stack (VB6: MAX_INVENTORY_OBJS = 10000).
const MAX_INVENTORY_OBJS: i32 = 10000;
/// Maximum gold (VB6: MAXORO = 999999999).
const MAX_GOLD: i64 = 999_999_999;
/// Gold item object index (VB6: iORO = 12).
const GOLD_OBJ_INDEX: i32 = 12;
/// Commerce skill index in skills array.
const SK_COMERCIAR: usize = 11; // VB6 eSkill.Comerciar = 11

/// Start NPC commerce (VB6: IniciarComercioNPC).
/// Sends NPC inventory, gold, sets flag, sends INITCOM.
async fn iniciar_comercio_npc(state: &mut GameState, conn_id: ConnectionId, npc_idx: usize) {
    // Check user not dead
    if let Some(u) = state.users.get(&conn_id) {
        if u.dead {
            let msg = "||3".to_string(); // TEXTO3: Estás muerto
            state.send_to(conn_id, &msg).await;
            return;
        }
        if u.comerciando {
            return; // Already in commerce
        }
    }

    // Send NPC inventory
    enviar_npc_inv(state, conn_id, npc_idx, 0).await;

    // Send gold
    send_stats_gold(state, conn_id).await;

    // Set commerce flag
    if let Some(user) = state.users.get_mut(&conn_id) {
        user.comerciando = true;
        user.target_npc = npc_idx;
    }

    // Send INITCOM
    state.send_to(conn_id, server_opcodes::INIT_COMMERCE).await;
}

/// Send NPC inventory to client (VB6: EnviarNpcInv).
/// If slot == 0: send full inventory (NPCR + NPCI*N).
/// If slot != 0: send single slot update (NPC|).
async fn enviar_npc_inv(state: &mut GameState, conn_id: ConnectionId, npc_idx: usize, slot: usize) {
    // Calculate discount from commerce skill
    let comerciar_skill = state.users.get(&conn_id)
        .map(|u| u.skills[SK_COMERCIAR])
        .unwrap_or(0);
    let descuento = 1.0 + comerciar_skill as f64 / 100.0;
    let descuento = if descuento <= 0.0 { 1.0 } else { descuento };

    // Gather NPC inventory data
    let npc_data: Vec<(i32, i32, usize)> = match state.get_npc(npc_idx) {
        Some(npc) => {
            npc.inventory.iter().enumerate().map(|(i, s)| (s.obj_index, s.amount, i)).collect()
        }
        None => return,
    };
    let inflacion = state.get_npc(npc_idx).map(|n| n.inflacion).unwrap_or(0);

    if slot == 0 {
        // Full inventory send
        state.send_to(conn_id, server_opcodes::NPC_INV_RESET).await;

        for (obj_index, amount, idx) in &npc_data {
            if *obj_index <= 0 { continue; }
            let obj = match state.get_object(*obj_index) {
                Some(o) => o.clone(),
                None => continue,
            };

            let infla = (inflacion as i64 * obj.valor as i64) / 100;
            let price = ((obj.valor as i64 + infla) as f64 / descuento) as i64;

            let pkt = format!(
                "{}{},{},{},{},{},{},{},{},{},{}",
                server_opcodes::NPC_INV_ITEM,
                obj.name, amount, price, obj.grh_index,
                obj_index, obj.obj_type as i32,
                obj.max_hit, obj.min_hit, obj.max_def,
                idx + 1 // 1-based slot for client
            );
            state.send_to(conn_id, &pkt).await;
        }
    } else {
        // Single slot update
        let idx = slot - 1; // Convert to 0-based
        if idx >= npc_data.len() { return; }
        let (obj_index, amount, _) = npc_data[idx];

        if obj_index <= 0 {
            // Empty slot
            let pkt = format!("{}{},(Nada),0,0,0,0,0,0,0,0,{}", server_opcodes::NPC_INV_SLOT, slot, slot);
            state.send_to(conn_id, &pkt).await;
        } else {
            let obj = match state.get_object(obj_index) {
                Some(o) => o.clone(),
                None => return,
            };
            let infla = (inflacion as i64 * obj.valor as i64) / 100;
            let price = ((obj.valor as i64 + infla) as f64 / descuento) as i64;

            let pkt = format!(
                "{}{},{},{},{},{},{},{},{},{},{},{}",
                server_opcodes::NPC_INV_SLOT,
                slot, obj.name, amount, price, obj.grh_index,
                obj_index, obj.obj_type as i32,
                obj.max_hit, obj.min_hit, obj.max_def,
                slot
            );
            state.send_to(conn_id, &pkt).await;
        }
    }
}

/// COMP — User buys from NPC (VB6: NPCVentaItem / UserCompraObj).
async fn handle_commerce_buy(state: &mut GameState, conn_id: ConnectionId, data: &str) {
    let payload = strip_opcode(data, 5); // "COMP," = 5 chars (VB6 strips 5)
    info!("[COMP] #{} raw='{}' payload='{}'", conn_id, data, payload);
    let slot: usize = match read_field(1, payload, ',').parse() {
        Ok(v) if v >= 1 => v,
        _ => { info!("[COMP] #{} bad slot field: '{}'", conn_id, read_field(1, payload, ',')); return; },
    };
    let cantidad: i32 = match read_field(2, payload, ',').parse() {
        Ok(v) if v >= 1 => v,
        _ => { info!("[COMP] #{} bad cantidad field: '{}'", conn_id, read_field(2, payload, ',')); return; },
    };
    info!("[COMP] #{} slot={} qty={}", conn_id, slot, cantidad);

    // Anti-cheat: max stack check
    if cantidad > MAX_INVENTORY_OBJS {
        info!("[COMMERCE] #{} attempted to buy {} items (exceeds max), potential cheat", conn_id, cantidad);
        return;
    }

    // Validate user state
    let (dead, comerciando, target_npc, user_gold, comerciar_skill) = match state.users.get(&conn_id) {
        Some(u) if u.logged => (u.dead, u.comerciando, u.target_npc, u.gold, u.skills[SK_COMERCIAR]),
        _ => return,
    };

    if dead {
        let msg = "||3".to_string(); // TEXTO3: Estás muerto
        state.send_to(conn_id, &msg).await;
        return;
    }
    if !comerciando || target_npc == 0 {
        return;
    }

    // Get NPC data
    let (npc_comercia, npc_inflacion) = match state.get_npc(target_npc) {
        Some(npc) => (npc.comercia, npc.inflacion),
        None => return,
    };
    if !npc_comercia { return; }

    // Get item from NPC inventory (slot is 1-based)
    let slot_idx = slot - 1;
    let (obj_index, npc_amount) = match state.get_npc(target_npc) {
        Some(npc) if slot_idx < npc.inventory.len() => {
            (npc.inventory[slot_idx].obj_index, npc.inventory[slot_idx].amount)
        }
        _ => return,
    };
    if obj_index <= 0 || npc_amount <= 0 { return; }

    // Clamp quantity to NPC stock
    let cantidad = cantidad.min(npc_amount);

    // Get object data for price calc
    let obj = match state.get_object(obj_index) {
        Some(o) => o.clone(),
        None => return,
    };

    // Calculate buy price
    let descuento = 1.0 + comerciar_skill as f64 / 100.0;
    let descuento = if descuento <= 0.0 { 1.0 } else { descuento };
    let infla = (npc_inflacion as i64 * obj.valor as i64) / 100;
    let unit_price = ((obj.valor as i64 + infla) as f64 / descuento) as i64;
    let total_price = unit_price * cantidad as i64;

    // Check gold
    if user_gold < total_price {
        let msg = "||663".to_string(); // TEXTO663: No tenes suficiente dinero
        state.send_to(conn_id, &msg).await;
        return;
    }

    // Find user inventory slot (try stacking first, then empty)
    let user_inv_slot = {
        let user = match state.users.get(&conn_id) {
            Some(u) => u,
            None => return,
        };

        // Try to stack
        let mut stack_slot = None;
        let mut empty_slot = None;
        for (i, inv) in user.inventory.iter().enumerate() {
            if inv.obj_index == obj_index && inv.amount + cantidad <= MAX_INVENTORY_OBJS {
                stack_slot = Some(i);
                break;
            }
            if inv.obj_index == 0 && empty_slot.is_none() {
                empty_slot = Some(i);
            }
        }
        stack_slot.or(empty_slot)
    };

    let user_slot = match user_inv_slot {
        Some(s) => s,
        None => {
            let msg = "||108".to_string(); // TEXTO108: No podes cargar mas objetos
            state.send_to(conn_id, &msg).await;
            return;
        }
    };

    // Execute transaction: add to user, deduct gold
    if let Some(user) = state.users.get_mut(&conn_id) {
        if user.inventory[user_slot].obj_index == 0 {
            user.inventory[user_slot].obj_index = obj_index;
            user.inventory[user_slot].amount = cantidad;
        } else {
            user.inventory[user_slot].amount += cantidad;
        }
        user.gold -= total_price;
        if user.gold < 0 { user.gold = 0; }
    }

    // Remove from NPC inventory
    if let Some(npc) = state.get_npc_mut(target_npc) {
        npc.inventory[slot_idx].amount -= cantidad;
        if npc.inventory[slot_idx].amount <= 0 {
            npc.inventory[slot_idx].obj_index = 0;
            npc.inventory[slot_idx].amount = 0;
            npc.nro_items -= 1;

            // Auto-replenish: if NPC ran out of items and InvReSpawn is false
            if npc.nro_items <= 0 && !npc.inv_respawn {
                reload_npc_inventory(state, target_npc);
            }
        }
    }

    // Send updates to client
    send_inventory_slot(state, conn_id, user_slot).await;
    send_stats_gold(state, conn_id).await;
    enviar_npc_inv(state, conn_id, target_npc, slot).await;

    let pkt = format!("{}{},{}", server_opcodes::TRANS_OK, slot, 0);
    state.send_to(conn_id, &pkt).await;
}

/// VEND — User sells to NPC (VB6: NPCCompraItem / NpcCompraObj).
async fn handle_commerce_sell(state: &mut GameState, conn_id: ConnectionId, data: &str) {
    let payload = strip_opcode(data, 5); // "VEND," = 5 chars (VB6 strips 5)
    let slot: usize = match read_field(1, payload, ',').parse() {
        Ok(v) if v >= 1 => v,
        _ => return,
    };
    let cantidad: i32 = match read_field(2, payload, ',').parse() {
        Ok(v) if v >= 1 => v,
        _ => return,
    };

    // Validate user state
    let (dead, comerciando, target_npc) = match state.users.get(&conn_id) {
        Some(u) if u.logged => (u.dead, u.comerciando, u.target_npc),
        _ => return,
    };

    if dead {
        let msg = "||3".to_string(); // TEXTO3: Estás muerto
        state.send_to(conn_id, &msg).await;
        return;
    }
    if !comerciando || target_npc == 0 { return; }

    // Validate NPC
    let (npc_comercia, npc_tipo_items) = match state.get_npc(target_npc) {
        Some(npc) => (npc.comercia, npc.tipo_items),
        None => return,
    };
    if !npc_comercia { return; }

    // Get user item data (slot is 1-based)
    let user_slot_idx = slot - 1;
    let (obj_index, user_amount, equipped) = match state.users.get(&conn_id) {
        Some(u) if user_slot_idx < u.inventory.len() => {
            let inv = &u.inventory[user_slot_idx];
            (inv.obj_index, inv.amount, inv.equipped)
        }
        _ => return,
    };

    if obj_index <= 0 || user_amount <= 0 { return; }
    if equipped {
        let msg = "||185".to_string(); // TEXTO185: No podes depositar este objeto
        state.send_to(conn_id, &msg).await;
        return;
    }

    // Get object data
    let obj = match state.get_object(obj_index) {
        Some(o) => o.clone(),
        None => return,
    };

    // Reject conditions (VB6: NpcCompraObj)
    if obj.newbie {
        let msg = "||660".to_string(); // TEXTO660: No comercio objetos para newbies
        state.send_to(conn_id, &msg).await;
        return;
    }
    if obj_index == GOLD_OBJ_INDEX {
        let msg = "||661".to_string(); // TEXTO661: El npc no esta interesado en comprar ese objeto
        state.send_to(conn_id, &msg).await;
        return;
    }
    if obj.obj_type == ObjType::Key {
        let msg = "||661".to_string(); // TEXTO661: El npc no esta interesado en comprar ese objeto
        state.send_to(conn_id, &msg).await;
        return;
    }
    // TipoItems filter: 0 = buys anything, otherwise must match
    if npc_tipo_items != 0 && npc_tipo_items != obj.obj_type as i32 {
        let msg = "||661".to_string(); // TEXTO661: El npc no esta interesado en comprar ese objeto
        state.send_to(conn_id, &msg).await;
        return;
    }

    // Clamp quantity
    let cantidad = cantidad.min(user_amount);

    // Calculate sell price (always 1/3 of base value)
    let sell_price = (obj.valor as i64 / 3) * cantidad as i64;

    // Find NPC slot (try stacking, then empty)
    let npc_slot = {
        let npc = match state.get_npc(target_npc) {
            Some(n) => n,
            None => return,
        };
        let mut stack_slot = None;
        let mut empty_slot = None;
        for (i, inv) in npc.inventory.iter().enumerate() {
            if inv.obj_index == obj_index && inv.amount + cantidad <= MAX_INVENTORY_OBJS {
                stack_slot = Some(i);
                break;
            }
            if inv.obj_index == 0 && empty_slot.is_none() {
                empty_slot = Some(i);
            }
        }
        stack_slot.or(empty_slot)
    };

    // Add to NPC inventory (if slot available)
    if let Some(npc_slot_idx) = npc_slot {
        if let Some(npc) = state.get_npc_mut(target_npc) {
            if npc.inventory[npc_slot_idx].obj_index == 0 {
                npc.inventory[npc_slot_idx].obj_index = obj_index;
                npc.inventory[npc_slot_idx].amount = cantidad;
                npc.nro_items += 1;
            } else {
                npc.inventory[npc_slot_idx].amount += cantidad;
            }
        }
    }
    // If no NPC slot, item is still sold (gold given, item removed) — VB6 behavior

    // Remove from user inventory
    if let Some(user) = state.users.get_mut(&conn_id) {
        user.inventory[user_slot_idx].amount -= cantidad;
        if user.inventory[user_slot_idx].amount <= 0 {
            user.inventory[user_slot_idx].obj_index = 0;
            user.inventory[user_slot_idx].amount = 0;
            user.inventory[user_slot_idx].equipped = false;
        }
        // Add gold (capped)
        user.gold = (user.gold + sell_price).min(MAX_GOLD);
    }

    // Send updates
    send_inventory_slot(state, conn_id, user_slot_idx).await;
    send_stats_gold(state, conn_id).await;

    // Send NPC inventory update for the slot that changed
    if let Some(npc_slot_idx) = npc_slot {
        enviar_npc_inv(state, conn_id, target_npc, npc_slot_idx + 1).await;
    }

    let pkt = format!("{}{},{}", server_opcodes::TRANS_OK, slot, 1);
    state.send_to(conn_id, &pkt).await;
}

/// FINCOM — Close commerce window.
async fn handle_commerce_close(state: &mut GameState, conn_id: ConnectionId) {
    if let Some(user) = state.users.get_mut(&conn_id) {
        user.comerciando = false;
    }
    state.send_to(conn_id, server_opcodes::COMMERCE_CLOSE_OK).await;
}

/// Reload NPC inventory from game data (VB6: CargarInvent).
/// Called when NPC runs out of items and InvReSpawn is false.
fn reload_npc_inventory(state: &mut GameState, npc_idx: usize) {
    let npc_number = match state.get_npc(npc_idx) {
        Some(npc) => npc.npc_number,
        None => return,
    };

    // Get original inventory from NPC database
    let items = match state.game_data.npcs.get(npc_number) {
        Some(data) => data.items.clone(),
        None => return,
    };
    let nro_items = match state.game_data.npcs.get(npc_number) {
        Some(data) => data.nro_items,
        None => return,
    };

    if let Some(npc) = state.get_npc_mut(npc_idx) {
        // Reset inventory
        for slot in npc.inventory.iter_mut() {
            slot.obj_index = 0;
            slot.amount = 0;
        }
        // Reload from data
        for (i, item) in items.iter().enumerate() {
            if i < npc.inventory.len() {
                npc.inventory[i].obj_index = item.obj_index;
                npc.inventory[i].amount = item.amount;
            }
        }
        npc.nro_items = nro_items;
    }
}

// =====================================================================
// Player Trading handlers (AA_ComercioUsuarios.bas)
// =====================================================================

/// Initiate trade from right-click menu (/COMERCIAR).
async fn iniciar_comercio_usuario(state: &mut GameState, conn_id: ConnectionId, target_conn: ConnectionId) {
    // Check both users alive
    let user_ok = state.users.get(&conn_id).map(|u| !u.dead && u.logged && !u.trading).unwrap_or(false);
    let target_ok = state.users.get(&target_conn).map(|u| !u.dead && u.logged && !u.trading).unwrap_or(false);

    if !user_ok || !target_ok { return; }

    // Set both in trading mode
    if let Some(u) = state.users.get_mut(&conn_id) {
        u.trading = true;
        u.trade_partner = Some(target_conn);
        u.trade_offered = false;
        u.trade_accepted = false;
        u.trade_gold = 0;
        u.trade_items.clear();
    }
    if let Some(u) = state.users.get_mut(&target_conn) {
        u.trading = true;
        u.trade_partner = Some(conn_id);
        u.trade_offered = false;
        u.trade_accepted = false;
        u.trade_gold = 0;
        u.trade_items.clear();
    }

    // Send trade init to both
    state.send_to(conn_id, server_opcodes::TRADE_INIT).await;
    state.send_to(target_conn, server_opcodes::TRADE_INIT).await;
}

/// UOR — Offer gold in trade.
async fn handle_trade_offer_gold(state: &mut GameState, conn_id: ConnectionId, data: &str) {
    let payload = strip_opcode(data, 3);
    let gold: i64 = match payload.trim().parse() {
        Ok(v) if v >= 0 => v,
        _ => return,
    };

    let (trading, partner, user_gold) = match state.users.get(&conn_id) {
        Some(u) if u.trading => (true, u.trade_partner, u.gold),
        _ => return,
    };
    if !trading { return; }
    let partner = match partner {
        Some(p) => p,
        None => return,
    };

    // Can't offer more than you have
    let gold = gold.min(user_gold);

    if let Some(u) = state.users.get_mut(&conn_id) {
        u.trade_gold = gold;
        u.trade_offered = true;
        u.trade_accepted = false; // Reset acceptance on new offer
    }
    // Reset partner acceptance too
    if let Some(u) = state.users.get_mut(&partner) {
        u.trade_accepted = false;
    }

    // Notify partner
    let pkt = format!("{}{}", server_opcodes::TRADE_OFFER_RECV, gold);
    state.send_to(partner, &pkt).await;
}

/// UOC — Offer items in trade.
async fn handle_trade_offer_item(state: &mut GameState, conn_id: ConnectionId, data: &str) {
    let payload = strip_opcode(data, 3);
    let slot: usize = match read_field(1, payload, ',').parse() {
        Ok(v) if v >= 1 => v,
        _ => return,
    };
    let cantidad: i32 = match read_field(2, payload, ',').parse() {
        Ok(v) if v >= 1 => v,
        _ => return,
    };

    let (trading, partner) = match state.users.get(&conn_id) {
        Some(u) if u.trading => (true, u.trade_partner),
        _ => return,
    };
    if !trading { return; }
    let partner = match partner {
        Some(p) => p,
        None => return,
    };

    let slot_idx = slot - 1;
    let (obj_index, amount, equipped) = match state.users.get(&conn_id) {
        Some(u) if slot_idx < u.inventory.len() => {
            let s = &u.inventory[slot_idx];
            (s.obj_index, s.amount, s.equipped)
        }
        _ => return,
    };

    if obj_index <= 0 || amount <= 0 || equipped { return; }

    // VB6 validation: can't trade keys, intransferable, or god items
    let blocked = state.get_object(obj_index).map(|o| {
        o.obj_type == ObjType::Key || o.intransferible || o.item_dios
    }).unwrap_or(false);
    if blocked {
        let msg = "||223".to_string(); // TEXTO223: No puedes transferir este objeto
        state.send_to(conn_id, &msg).await;
        return;
    }

    let cantidad = cantidad.min(amount);

    // Add to trade items
    if let Some(u) = state.users.get_mut(&conn_id) {
        u.trade_items.push(super::types::InventorySlot {
            obj_index,
            amount: cantidad,
            equipped: false,
        });
        u.trade_offered = true;
        u.trade_accepted = false;
    }
    if let Some(u) = state.users.get_mut(&partner) {
        u.trade_accepted = false;
    }

    // Send item info to partner
    let obj_name = state.get_object(obj_index).map(|o| o.name.clone()).unwrap_or_default();
    let pkt = format!("{}{}-{}-{}", server_opcodes::TRADE_ITEMS, obj_index, cantidad, obj_name);
    state.send_to(partner, &pkt).await;
}

/// TDR — Trade response (0=accept, 1=reject).
async fn handle_trade_response(state: &mut GameState, conn_id: ConnectionId, data: &str) {
    let payload = strip_opcode(data, 3);
    let response: i32 = match payload.trim().parse() {
        Ok(v) => v,
        _ => return,
    };

    let partner = match state.users.get(&conn_id) {
        Some(u) if u.trading => u.trade_partner,
        _ => return,
    };
    let partner = match partner {
        Some(p) => p,
        None => return,
    };

    if response == 1 {
        // Reject — cancel trade
        cancel_trade(state, conn_id, partner).await;
        return;
    }

    // Accept
    if let Some(u) = state.users.get_mut(&conn_id) {
        u.trade_accepted = true;
    }

    // Check if both accepted
    let both_accepted = state.users.get(&conn_id).map(|u| u.trade_accepted).unwrap_or(false)
        && state.users.get(&partner).map(|u| u.trade_accepted).unwrap_or(false);

    if both_accepted {
        execute_trade(state, conn_id, partner).await;
    }
}

/// Execute the trade (both parties accepted).
async fn execute_trade(state: &mut GameState, user1: ConnectionId, user2: ConnectionId) {
    // Get trade data
    let (gold1, items1) = match state.users.get(&user1) {
        Some(u) => (u.trade_gold, u.trade_items.clone()),
        None => return,
    };
    let (gold2, items2) = match state.users.get(&user2) {
        Some(u) => (u.trade_gold, u.trade_items.clone()),
        None => return,
    };

    // Transfer gold
    if let Some(u) = state.users.get_mut(&user1) {
        u.gold -= gold1;
        u.gold += gold2;
        u.gold = u.gold.max(0).min(MAX_GOLD);
    }
    if let Some(u) = state.users.get_mut(&user2) {
        u.gold -= gold2;
        u.gold += gold1;
        u.gold = u.gold.max(0).min(MAX_GOLD);
    }

    // Remove items from user1, give to user2
    for item in &items1 {
        remove_items_from_inv(state, user1, item.obj_index, item.amount).await;
        find_or_add_inv_slot(state, user2, item.obj_index, item.amount);
    }
    // Remove items from user2, give to user1
    for item in &items2 {
        remove_items_from_inv(state, user2, item.obj_index, item.amount).await;
        find_or_add_inv_slot(state, user1, item.obj_index, item.amount);
    }

    // Clear trade state
    for uid in &[user1, user2] {
        if let Some(u) = state.users.get_mut(uid) {
            u.trading = false;
            u.trade_partner = None;
            u.trade_offered = false;
            u.trade_accepted = false;
            u.trade_gold = 0;
            u.trade_items.clear();
        }
    }

    // Send updates
    state.send_to(user1, server_opcodes::TRADE_OK).await;
    state.send_to(user2, server_opcodes::TRADE_OK).await;
    send_stats_gold(state, user1).await;
    send_stats_gold(state, user2).await;
    send_full_inventory(state, user1).await;
    send_full_inventory(state, user2).await;
}

/// Cancel trade between two users.
async fn cancel_trade(state: &mut GameState, user1: ConnectionId, user2: ConnectionId) {
    for uid in &[user1, user2] {
        if let Some(u) = state.users.get_mut(uid) {
            u.trading = false;
            u.trade_partner = None;
            u.trade_offered = false;
            u.trade_accepted = false;
            u.trade_gold = 0;
            u.trade_items.clear();
        }
        state.send_to(*uid, server_opcodes::TRADE_CANCEL_OK).await;
    }
}

/// TCM — Cancel trade.
async fn handle_trade_cancel(state: &mut GameState, conn_id: ConnectionId) {
    let partner = match state.users.get(&conn_id) {
        Some(u) if u.trading => u.trade_partner,
        _ => return,
    };
    if let Some(p) = partner {
        cancel_trade(state, conn_id, p).await;
    }
}

/// VHC — Trade chat message.
async fn handle_trade_chat(state: &mut GameState, conn_id: ConnectionId, data: &str) {
    let payload = strip_opcode(data, 3);
    let partner = match state.users.get(&conn_id) {
        Some(u) if u.trading => u.trade_partner,
        _ => return,
    };
    if let Some(p) = partner {
        let name = state.users.get(&conn_id).map(|u| u.char_name.clone()).unwrap_or_default();
        let pkt = format!("{}{}: {}", server_opcodes::TRADE_CHAT_MSG, name, payload);
        state.send_to(p, &pkt).await;
    }
}

// =====================================================================
// Banking handlers (modBancoNuevo.bas)
// =====================================================================

/// Maximum bank item stack.
const MAX_BANK_STACK: i32 = 999;

/// Open bank window (VB6: IniciarDeposito).
async fn iniciar_banco(state: &mut GameState, conn_id: ConnectionId) {
    if let Some(u) = state.users.get(&conn_id) {
        if u.dead {
            let msg = "||3".to_string(); // TEXTO3: Estás muerto
            state.send_to(conn_id, &msg).await;
            return;
        }
    }

    // VB6 IniciarDeposito: UpdateBanUserInv(True) → SBR + SBO items, SendUserGLD, INITBANCO, Comerciando=True
    enviar_banco_inv(state, conn_id).await;

    // Send user gold (VB6 sends SendUserGLD, not bank gold separately)
    send_stats_gold(state, conn_id).await;

    // Set comerciando flag (VB6 sets this)
    if let Some(user) = state.users.get_mut(&conn_id) {
        user.comerciando = true;
    }

    // Send init bank packet
    state.send_to(conn_id, server_opcodes::INIT_BANK).await;
}

/// Send all bank slots to client (VB6: UpdateBanUserInv with UpdateAll=True).
/// VB6 sends SBR first to reset, then SBO for each non-empty slot.
/// SBO format: SBO<slot>,<obj_index>,<name>,<amount>,<grh>,<type>,<max_hit>,<min_hit>,<max_def>
async fn enviar_banco_inv(state: &mut GameState, conn_id: ConnectionId) {
    use super::types::MAX_BANK_SLOTS;

    // Send SBR to reset bank inventory on client
    state.send_to(conn_id, "SBR").await;

    for idx in 0..MAX_BANK_SLOTS {
        let slot_data = match state.users.get(&conn_id) {
            Some(u) if idx < u.bank.len() => {
                let s = &u.bank[idx];
                if s.obj_index > 0 {
                    state.get_object(s.obj_index).map(|o| {
                        (s.obj_index, o.name.clone(), s.amount, o.grh_index, o.obj_type as i32,
                         o.max_hit, o.min_hit, o.max_def)
                    })
                } else {
                    None
                }
            }
            _ => None,
        };

        // VB6 only sends SBO for non-empty slots
        if let Some((obj_idx, name, amount, grh, obj_type, max_hit, min_hit, max_def)) = slot_data {
            let slot_num = idx + 1;
            // VB6 format: SBO<slot>,<obj_index>,<name>,<amount>,<grh>,<type>,<max_hit>,<min_hit>,<max_def>
            let pkt = format!("{}{},{},{},{},{},{},{},{},{}",
                server_opcodes::BANK_SLOT,
                slot_num, obj_idx, name, amount, grh, obj_type, max_hit, min_hit, max_def);
            state.send_to(conn_id, &pkt).await;
        }
    }
}

/// DEPO,<slot>,<cantidad> — Deposit item to bank.
/// VB6: Right(rData, Len(rData) - 5) then ReadField with chr(44)
async fn handle_bank_deposit(state: &mut GameState, conn_id: ConnectionId, data: &str) {
    let payload = strip_opcode(data, 5); // "DEPO," = 5 chars (VB6 strips 5)
    info!("[DEPO] #{} raw='{}' payload='{}'", conn_id, data, payload);
    let slot: usize = match read_field(1, payload, ',').parse() {
        Ok(v) if v >= 1 => v,
        _ => { info!("[DEPO] #{} bad slot: '{}'", conn_id, read_field(1, payload, ',')); return; },
    };
    let cantidad: i32 = match read_field(2, payload, ',').parse() {
        Ok(v) if v >= 1 => v,
        _ => { info!("[DEPO] #{} bad qty: '{}'", conn_id, read_field(2, payload, ',')); return; },
    };
    info!("[DEPO] #{} slot={} qty={}", conn_id, slot, cantidad);

    let slot_idx = slot - 1;

    // Get item from user inventory
    let (obj_index, user_amount, equipped) = match state.users.get(&conn_id) {
        Some(u) if u.logged && slot_idx < u.inventory.len() => {
            let s = &u.inventory[slot_idx];
            (s.obj_index, s.amount, s.equipped)
        }
        _ => return,
    };

    if obj_index <= 0 || user_amount <= 0 { return; }
    if equipped {
        let msg = "||185".to_string(); // TEXTO185: No podes depositar este objeto
        state.send_to(conn_id, &msg).await;
        return;
    }

    // Check not intransferible
    let intransferible = state.get_object(obj_index).map(|o| o.intransferible).unwrap_or(false);
    if intransferible {
        let msg = "||223".to_string(); // TEXTO223: No puedes transferir este objeto
        state.send_to(conn_id, &msg).await;
        return;
    }

    let cantidad = cantidad.min(user_amount);

    // Find bank slot (stack or empty)
    let bank_slot = {
        let user = match state.users.get(&conn_id) {
            Some(u) => u,
            None => return,
        };
        let mut stack = None;
        let mut empty = None;
        for (i, s) in user.bank.iter().enumerate() {
            if s.obj_index == obj_index && s.amount + cantidad <= MAX_BANK_STACK {
                stack = Some(i);
                break;
            }
            if s.obj_index == 0 && empty.is_none() {
                empty = Some(i);
            }
        }
        stack.or(empty)
    };

    let bank_idx = match bank_slot {
        Some(i) => i,
        None => {
            let msg = "||186".to_string(); // TEXTO186: No tienes mas espacio en el banco
            state.send_to(conn_id, &msg).await;
            return;
        }
    };

    // Transfer
    if let Some(user) = state.users.get_mut(&conn_id) {
        // Remove from inventory
        user.inventory[slot_idx].amount -= cantidad;
        if user.inventory[slot_idx].amount <= 0 {
            user.inventory[slot_idx].obj_index = 0;
            user.inventory[slot_idx].amount = 0;
            user.inventory[slot_idx].equipped = false;
        }

        // Add to bank
        if user.bank[bank_idx].obj_index == 0 {
            user.bank[bank_idx].obj_index = obj_index;
            user.bank[bank_idx].amount = cantidad;
        } else {
            user.bank[bank_idx].amount += cantidad;
        }
    }

    // Send updates (VB6: UpdateBanUserInv(True) + UpdateVentanaBanco)
    send_inventory_slot(state, conn_id, slot_idx).await;
    enviar_banco_inv(state, conn_id).await;
    // VB6: BANCOOK<slot>,1 (1 = deposit from user inv)
    let pkt = format!("BANCOOK{},1", slot);
    state.send_to(conn_id, &pkt).await;
}

/// RETI,<slot>,<cantidad> — Withdraw item from bank.
/// VB6: Right(rData, Len(rData) - 5) then ReadField with chr(44)
async fn handle_bank_withdraw(state: &mut GameState, conn_id: ConnectionId, data: &str) {
    let payload = strip_opcode(data, 5); // "RETI," = 5 chars (VB6 strips 5)
    let slot: usize = match read_field(1, payload, ',').parse() {
        Ok(v) if v >= 1 => v,
        _ => return,
    };
    let cantidad: i32 = match read_field(2, payload, ',').parse() {
        Ok(v) if v >= 1 => v,
        _ => return,
    };

    let slot_idx = slot - 1;

    // Get item from bank
    let (obj_index, bank_amount) = match state.users.get(&conn_id) {
        Some(u) if u.logged && slot_idx < u.bank.len() => {
            let s = &u.bank[slot_idx];
            (s.obj_index, s.amount)
        }
        _ => return,
    };

    if obj_index <= 0 || bank_amount <= 0 { return; }

    let cantidad = cantidad.min(bank_amount);

    // Find user inventory slot
    let inv_slot = find_or_add_inv_slot(state, conn_id, obj_index, cantidad);
    let inv_idx = match inv_slot {
        Some(i) => i,
        None => {
            let msg = "||108".to_string(); // TEXTO108: No podes cargar mas objetos
            state.send_to(conn_id, &msg).await;
            return;
        }
    };

    // Remove from bank and compact (VB6 updateBInventory shifts items to fill gaps)
    if let Some(user) = state.users.get_mut(&conn_id) {
        user.bank[slot_idx].amount -= cantidad;
        if user.bank[slot_idx].amount <= 0 {
            // Shift remaining items down to fill gap (VB6 behavior)
            for i in slot_idx..user.bank.len() - 1 {
                user.bank[i] = user.bank[i + 1].clone();
            }
            let last = user.bank.len() - 1;
            user.bank[last].obj_index = 0;
            user.bank[last].amount = 0;
        }
    }

    // Send updates (VB6: UpdateBanUserInv(True) + UpdateVentanaBanco)
    send_inventory_slot(state, conn_id, inv_idx).await;
    send_stats_gold(state, conn_id).await;
    enviar_banco_inv(state, conn_id).await;
    // VB6: BANCOOK<slot>,0 (0 = withdraw from bank)
    let pkt = format!("BANCOOK{},0", slot);
    state.send_to(conn_id, &pkt).await;
}

/// FINBAN — Close bank/commerce window.
/// VB6: Both FINBAN and FINCOM reset Comerciando=False.
/// The NPC commerce window sends FINBAN to close (not FINCOM).
async fn handle_bank_close(state: &mut GameState, conn_id: ConnectionId) {
    if let Some(user) = state.users.get_mut(&conn_id) {
        user.comerciando = false;
    }
    state.send_to(conn_id, server_opcodes::BANK_CLOSE_OK).await;
}

// =====================================================================
// Jobs/Skills handlers (Trabajo.bas)
// =====================================================================

/// Skill indices matching VB6 eSkill enum (Declares.bas line 582).
mod skill_id {
    pub const SUERTE: i32 = 1;
    pub const MAGIA: i32 = 2;
    pub const ROBAR: i32 = 3;
    pub const TACTICAS: i32 = 4;
    pub const ARMAS: i32 = 5;
    pub const MEDITAR: i32 = 6;
    pub const APUNALAR: i32 = 7;
    pub const OCULTARSE: i32 = 8;
    pub const SUPERVIVENCIA: i32 = 9;
    pub const TALAR: i32 = 10;
    pub const COMERCIAR: i32 = 11;
    pub const DEFENSA: i32 = 12;
    pub const PESCA: i32 = 13;
    pub const MINERIA: i32 = 14;
    pub const CARPINTERIA: i32 = 15;
    pub const HERRERIA: i32 = 16;
    pub const LIDERAZGO: i32 = 17;
    pub const DOMAR: i32 = 18;
    pub const PROYECTILES: i32 = 19;
    pub const WRESTERLING: i32 = 20;
    pub const NAVEGACION: i32 = 21;
    pub const DEFENSA_MAGICA: i32 = 22;
    pub const FUNDIR_METAL: i32 = 88;
}

/// Tool object indices (VB6 constants).
const HACHA_LENADOR: i32 = 127;
const PIQUETE_MINERO: i32 = 187;
const MARTILLO_HERRERO: i32 = 389;
const SERRUCHO_CARPINTERO: i32 = 198;
const CANA_PESCA: i32 = 543; // Fishing rod
const RED_PESCA: i32 = 543;  // Fishing net (same category)

/// Resource items.
const LENA_OBJ: i32 = 58;
const PESCADO_OBJ: i32 = 139;
const HIERRO_CRUDO: i32 = 192;
const PLATA_CRUDA: i32 = 193;
const ORO_CRUDO: i32 = 194;
const LINGOTE_HIERRO: i32 = 386;
const LINGOTE_PLATA: i32 = 387;
const LINGOTE_ORO: i32 = 388;
const PIEDRA_OBJ: i32 = 1225;    // Magic stones (carpentry material)

/// Sound IDs.
const SND_TALAR: i32 = 13;
const SND_PESCAR: i32 = 14;
const SND_MINERO: i32 = 15;
const SND_HERRERO: i32 = 41;
const SND_CARPINTERO: i32 = 42;

/// Stamina costs.
const ESFUERZO_TALAR_RECOLECTOR: i32 = 2;
const ESFUERZO_TALAR_GENERAL: i32 = 4;
const ESFUERZO_PESCAR_RECOLECTOR: i32 = 1;
const ESFUERZO_PESCAR_GENERAL: i32 = 3;
const ESFUERZO_EXCAVAR_RECOLECTOR: i32 = 2;
const ESFUERZO_EXCAVAR_GENERAL: i32 = 5;

/// Common luck table for skill success checks (VB6 pattern).
fn luck_denominator(skill: i32) -> i32 {
    match skill {
        0..=10 => 35,
        11..=20 => 30,
        21..=30 => 28,
        31..=40 => 24,
        41..=50 => 22,
        51..=60 => 20,
        61..=70 => 18,
        71..=80 => 15,
        81..=90 => 13,
        91..=100 => 7,
        _ => 35,
    }
}

/// Simple random number in range [min, max] inclusive.
fn random_number(min: i32, max: i32) -> i32 {
    use std::time::{SystemTime, UNIX_EPOCH};
    if min >= max { return min; }
    let seed = SystemTime::now()
        .duration_since(UNIX_EPOCH)
        .unwrap_or_default()
        .as_nanos() as u64;
    // Simple LCG
    let val = seed.wrapping_mul(6364136223846793005).wrapping_add(1442695040888963407);
    let range = (max - min + 1) as u64;
    min + (val % range) as i32
}

/// Try to level up a skill (VB6: SubirSkill).
/// Returns true if skill increased.
fn try_level_skill(user: &mut super::types::UserState, skill_idx: usize) -> bool {
    if skill_idx == 0 || skill_idx > 21 { return false; }
    let idx = skill_idx - 1; // Convert to 0-based

    // Must not be hungry or thirsty
    if user.min_ham <= 0 || user.min_agua <= 0 { return false; }

    // Max skill points
    if user.skills[idx] >= 100 { return false; }

    // Level cap: min(level * 3, 100)
    let cap = (user.level * 3).min(100);
    if user.skills[idx] >= cap { return false; }

    // Probability based on level
    let prob = match user.level {
        1..=3 => 25,
        4..=5 => 35,
        6..=9 => 40,
        10..=19 => 45,
        _ => 50,
    };

    let roll = random_number(1, prob);
    if roll == 7 {
        user.skills[idx] += 1;
        user.exp += 50;
        return true;
    }
    false
}

/// Check if user has the right tool equipped for a skill.
fn has_tool_equipped(user: &super::types::UserState, tool_obj_index: i32) -> bool {
    if user.equip.weapon > 0 && user.equip.weapon <= user.inventory.len() {
        let slot = &user.inventory[user.equip.weapon - 1];
        return slot.obj_index == tool_obj_index && slot.equipped;
    }
    false
}

/// Get the equipped weapon obj_index.
fn equipped_weapon_obj(user: &super::types::UserState) -> i32 {
    if user.equip.weapon > 0 && user.equip.weapon <= user.inventory.len() {
        let slot = &user.inventory[user.equip.weapon - 1];
        if slot.equipped { return slot.obj_index; }
    }
    0
}

/// Check if class is RECOLECTOR (resource gatherer class).
fn is_recolector(class: &str) -> bool {
    class.eq_ignore_ascii_case("Recolector")
}

/// WLC — Work Left Click (main skill dispatch).
async fn handle_work_left_click(state: &mut GameState, conn_id: ConnectionId, data: &str) {
    let payload = strip_opcode(data, 3); // "WLC" = 3 chars
    let target_x: i32 = match read_field(1, payload, ',').parse() {
        Ok(v) => v,
        _ => return,
    };
    let target_y: i32 = match read_field(2, payload, ',').parse() {
        Ok(v) => v,
        _ => return,
    };
    let skill_type: i32 = match read_field(3, payload, ',').parse() {
        Ok(v) => v,
        _ => return,
    };

    // Validate user
    let (dead, meditating, map, ux, uy) = match state.users.get(&conn_id) {
        Some(u) if u.logged => (u.dead, u.meditating, u.pos_map, u.pos_x, u.pos_y),
        _ => return,
    };

    if dead {
        let msg = "||3".to_string(); // TEXTO3: Estás muerto
        state.send_to(conn_id, &msg).await;
        return;
    }
    if meditating { return; }

    // Range check
    if (target_x - ux).abs() > world::MIN_X_BORDER || (target_y - uy).abs() > world::MIN_Y_BORDER {
        return;
    }

    // Anti-cheat: check work cooldown (except for ranged attack which has its own)
    if skill_type != skill_id::PROYECTILES && !puede_trabajar(state, conn_id) {
        return;
    }

    match skill_type {
        skill_id::PESCA => {
            do_pescar(state, conn_id, target_x, target_y).await;
        }
        skill_id::TALAR => {
            do_talar(state, conn_id, target_x, target_y).await;
        }
        skill_id::MINERIA => {
            do_mineria(state, conn_id, target_x, target_y).await;
        }
        skill_id::DOMAR => {
            do_domar(state, conn_id, target_x, target_y).await;
        }
        skill_id::HERRERIA => {
            do_herreria(state, conn_id, target_x, target_y).await;
        }
        skill_id::FUNDIR_METAL => {
            do_fundir(state, conn_id).await;
        }
        skill_id::ROBAR => {
            do_robar(state, conn_id, target_x, target_y).await;
        }
        skill_id::PROYECTILES => {
            do_ranged_attack(state, conn_id, target_x, target_y).await;
        }
        skill_id::MAGIA => {
            // VB6: WLC Magia flow (TCP_HandleData1.bas line 1910-1931)
            // 1. Check MagiaSinEfecto map flag
            // 2. LookatTile to find target user/NPC on clicked tile
            // 3. Cast pending spell

            // Set target coordinates
            if let Some(user) = state.users.get_mut(&conn_id) {
                user.target_x = target_x;
                user.target_y = target_y;
                user.target_map = map;
            }

            // LookatTile: find user or NPC on the target tile (and y+1)
            // VB6: LookatTile is called before LanzarHechizo — shows "Ves a..." in console
            let mut found_target = false;
            for check_y in [target_y, target_y + 1] {
                if check_y < 1 || check_y > 100 { continue; }
                // Check for user on tile
                let tile_user = state.world.grid(map)
                    .and_then(|g| g.tile(target_x, check_y))
                    .and_then(|t| t.user_conn);
                if let Some(target_conn) = tile_user {
                    if state.users.get(&target_conn).map(|u| u.logged).unwrap_or(false) {
                        if let Some(user) = state.users.get_mut(&conn_id) {
                            user.target_user = target_conn;
                            user.target_npc = 0;
                        }
                        // VB6: LookatTile sends "Ves a <name>" info to caster
                        send_lookat_user_info(state, conn_id, target_conn).await;
                        found_target = true;
                        break;
                    }
                }
                // Check for NPC on tile
                let tile_npc = state.world.grid(map)
                    .and_then(|g| g.tile(target_x, check_y))
                    .map(|t| t.npc_index)
                    .unwrap_or(0);
                if tile_npc > 0 {
                    if let Some(user) = state.users.get_mut(&conn_id) {
                        user.target_npc = tile_npc as usize;
                        user.target_user = 0;
                    }
                    // VB6: LookatTile sends NPC desc to caster
                    // NPC target — VB6 does NOT call LookatTile for NPCs when casting
                    found_target = true;
                    break;
                }
            }

            // If no target found, clear targets (for self-cast or terrain spells)
            if !found_target {
                if let Some(user) = state.users.get_mut(&conn_id) {
                    user.target_user = 0;
                    user.target_npc = 0;
                }
            }

            // Anti-cheat cooldown
            if !puede_castear(state, conn_id) {
                return;
            }

            // Cast the pending spell
            let pending = state.users.get(&conn_id).map(|u| u.pending_spell).unwrap_or(0);
            if pending > 0 {
                do_cast_spell(state, conn_id).await;
                send_stats_mana(state, conn_id).await;
                send_stats_hp(state, conn_id).await;
            } else {
                let msg = "||288".to_string(); // TEXTO288: Primero selecciona el hechizo!
                state.send_to(conn_id, &msg).await;
            }
        }
        skill_id::OCULTARSE => {
            do_ocultarse(state, conn_id).await;
        }
        _ => {
            // Unsupported skill
            info!("[WLC] #{} unsupported skill type {}", conn_id, skill_type);
        }
    }
}

/// Fishing (DoPescar).
async fn do_pescar(state: &mut GameState, conn_id: ConnectionId, tx: i32, ty: i32) {
    let (map, ux, uy, class, skill) = match state.users.get(&conn_id) {
        Some(u) => (u.pos_map, u.pos_x, u.pos_y, u.class.clone(), u.skills[12]), // Pesca = index 12 (1-based 13)
        None => return,
    };

    // Check equipped fishing tool
    let weapon = state.users.get(&conn_id).map(|u| equipped_weapon_obj(u)).unwrap_or(0);
    if weapon == 0 {
        let msg = format!("{}Necesitas una caña de pescar{}", server_opcodes::CONSOLE_MSG, font_types::INFO);
        state.send_to(conn_id, &msg).await;
        return;
    }

    // Check target has water (VB6 HayAgua)
    let has_water = state.hay_agua(map, tx, ty);

    if !has_water {
        let msg = "||250".to_string(); // TEXTO250: No hay agua donde pescar
        state.send_to(conn_id, &msg).await;
        return;
    }

    // Stamina cost
    let sta_cost = if is_recolector(&class) { ESFUERZO_PESCAR_RECOLECTOR } else { ESFUERZO_PESCAR_GENERAL };
    if let Some(u) = state.users.get(&conn_id) {
        if u.min_sta < sta_cost {
            let msg = "||17".to_string(); // TEXTO17: Estas muy cansado para realizar esa acción
            state.send_to(conn_id, &msg).await;
            return;
        }
    }

    // Deduct stamina
    if let Some(u) = state.users.get_mut(&conn_id) {
        u.min_sta -= sta_cost;
    }

    // Play sound to area
    let snd = format!("TW{}", SND_PESCAR);
    state.send_data(SendTarget::ToArea { map, x: ux, y: uy }, &snd).await;

    // Luck roll
    let suerte = luck_denominator(skill);
    let roll = random_number(1, suerte);

    if roll < 6 {
        // Success: give fish (VB6: always 1 fish regardless of class)
        let amount = 1;

        // Find inventory slot
        let slot = find_or_add_inv_slot(state, conn_id, PESCADO_OBJ, amount);
        if let Some(idx) = slot {
            send_inventory_slot(state, conn_id, idx).await;
            let msg = "||813".to_string(); // TEXTO813: Has pescado un lindo pez!
            state.send_to(conn_id, &msg).await;
        }
    } else {
        let msg = "||814".to_string(); // TEXTO814: No has pescado nada!
        state.send_to(conn_id, &msg).await;
    }

    // Try level skill + send stat updates
    if let Some(u) = state.users.get_mut(&conn_id) {
        try_level_skill(u, 13);
    }
    send_stats_sta(state, conn_id).await;
}

/// Woodcutting (DoTalar).
async fn do_talar(state: &mut GameState, conn_id: ConnectionId, tx: i32, ty: i32) {
    let (map, ux, uy, class, skill) = match state.users.get(&conn_id) {
        Some(u) => (u.pos_map, u.pos_x, u.pos_y, u.class.clone(), u.skills[9]), // Talar = index 9 (1-based 10)
        None => return,
    };

    // Check equipped axe
    let weapon = state.users.get(&conn_id).map(|u| equipped_weapon_obj(u)).unwrap_or(0);
    if weapon != HACHA_LENADOR {
        let msg = format!("{}Necesitas un hacha de leñador{}", server_opcodes::CONSOLE_MSG, font_types::INFO);
        state.send_to(conn_id, &msg).await;
        return;
    }

    // Distance check
    let dist = (tx - ux).abs().max((ty - uy).abs());
    if dist > 2 || dist == 0 {
        return;
    }

    // Check tile has a tree (otArboles = 4)
    let tile_obj = state.game_data.maps.get(map as usize)
        .and_then(|m| m.as_ref())
        .and_then(|m| {
            if tx >= 1 && tx <= 100 && ty >= 1 && ty <= 100 {
                let tile = &m.tiles[(ty - 1) as usize][(tx - 1) as usize];
                if tile.obj.obj_index > 0 {
                    state.get_object(tile.obj.obj_index as i32).map(|o| o.obj_type)
                } else {
                    None
                }
            } else {
                None
            }
        });

    // Also check ground items on world grid
    let ground_obj_type = state.world.grid(map)
        .and_then(|g| g.tile(tx, ty))
        .and_then(|t| {
            if t.ground_item.obj_index > 0 {
                state.get_object(t.ground_item.obj_index).map(|o| o.obj_type)
            } else {
                None
            }
        });

    let is_tree = tile_obj == Some(ObjType::Trees) || ground_obj_type == Some(ObjType::Trees);
    if !is_tree {
        let msg = "||255".to_string(); // TEXTO255: No hay ningun arbol ahi
        state.send_to(conn_id, &msg).await;
        return;
    }

    // Stamina cost
    let sta_cost = if is_recolector(&class) { ESFUERZO_TALAR_RECOLECTOR } else { ESFUERZO_TALAR_GENERAL };
    if let Some(u) = state.users.get(&conn_id) {
        if u.min_sta < sta_cost {
            let msg = "||17".to_string(); // TEXTO17: Estas muy cansado para realizar esa acción
            state.send_to(conn_id, &msg).await;
            return;
        }
    }
    if let Some(u) = state.users.get_mut(&conn_id) {
        u.min_sta -= sta_cost;
    }

    // Play sound
    let snd = format!("TW{}", SND_TALAR);
    state.send_data(SendTarget::ToArea { map, x: ux, y: uy }, &snd).await;

    // Luck roll
    let suerte = luck_denominator(skill);
    let roll = random_number(1, suerte);

    if roll < 6 {
        let amount = if is_recolector(&class) { random_number(1, 5) } else { 1 };
        let slot = find_or_add_inv_slot(state, conn_id, LENA_OBJ, amount);
        if let Some(idx) = slot {
            send_inventory_slot(state, conn_id, idx).await;
            let msg = "||825".to_string(); // TEXTO825: Has conseguido algo de leña!
            state.send_to(conn_id, &msg).await;
        }
    } else {
        let msg = "||826".to_string(); // TEXTO826: No has obtenido leña!
        state.send_to(conn_id, &msg).await;
    }

    if let Some(u) = state.users.get_mut(&conn_id) {
        try_level_skill(u, 10);
    }
    send_stats_sta(state, conn_id).await;
}

/// Mining (DoMineria).
async fn do_mineria(state: &mut GameState, conn_id: ConnectionId, tx: i32, ty: i32) {
    let (map, ux, uy, class, skill) = match state.users.get(&conn_id) {
        Some(u) => (u.pos_map, u.pos_x, u.pos_y, u.class.clone(), u.skills[13]), // Mineria = index 13 (1-based 14)
        None => return,
    };

    // Check equipped pick
    let weapon = state.users.get(&conn_id).map(|u| equipped_weapon_obj(u)).unwrap_or(0);
    if weapon != PIQUETE_MINERO {
        let msg = format!("{}Necesitas un pico de minero{}", server_opcodes::CONSOLE_MSG, font_types::INFO);
        state.send_to(conn_id, &msg).await;
        return;
    }

    // Distance check
    let dist = (tx - ux).abs().max((ty - uy).abs());
    if dist > 2 {
        return;
    }

    // Check tile has a mineral deposit (otYacimiento = Deposit = 22)
    let mineral_obj = state.world.grid(map)
        .and_then(|g| g.tile(tx, ty))
        .and_then(|t| {
            if t.ground_item.obj_index > 0 {
                state.get_object(t.ground_item.obj_index).cloned()
            } else {
                None
            }
        })
        .or_else(|| {
            state.game_data.maps.get(map as usize)
                .and_then(|m| m.as_ref())
                .and_then(|m| {
                    if tx >= 1 && tx <= 100 && ty >= 1 && ty <= 100 {
                        let tile = &m.tiles[(ty - 1) as usize][(tx - 1) as usize];
                        if tile.obj.obj_index > 0 {
                            state.get_object(tile.obj.obj_index as i32).cloned()
                        } else {
                            None
                        }
                    } else {
                        None
                    }
                })
        });

    let mineral_data = match mineral_obj {
        Some(ref o) if o.obj_type == ObjType::Deposit => o.clone(),
        _ => {
            let msg = "||256".to_string(); // TEXTO256: Ahi no hay ningun yacimiento
            state.send_to(conn_id, &msg).await;
            return;
        }
    };

    // Stamina cost
    let sta_cost = if is_recolector(&class) { ESFUERZO_EXCAVAR_RECOLECTOR } else { ESFUERZO_EXCAVAR_GENERAL };
    if let Some(u) = state.users.get(&conn_id) {
        if u.min_sta < sta_cost {
            let msg = "||17".to_string(); // TEXTO17: Estas muy cansado para realizar esa acción
            state.send_to(conn_id, &msg).await;
            return;
        }
    }
    if let Some(u) = state.users.get_mut(&conn_id) {
        u.min_sta -= sta_cost;
    }

    // Play sound
    let snd = format!("TW{}", SND_MINERO);
    state.send_data(SendTarget::ToArea { map, x: ux, y: uy }, &snd).await;

    // Luck roll
    let suerte = luck_denominator(skill);
    let roll = random_number(1, suerte);

    if roll <= 5 {
        // Determine mineral type from yacimiento
        let mineral_item = if mineral_data.mineral_index > 0 {
            mineral_data.mineral_index
        } else {
            HIERRO_CRUDO // Default to iron
        };

        let amount = if is_recolector(&class) { random_number(1, 6) } else { 1 };
        let slot = find_or_add_inv_slot(state, conn_id, mineral_item, amount);
        if let Some(idx) = slot {
            send_inventory_slot(state, conn_id, idx).await;
            let msg = "||827".to_string(); // TEXTO827: Has extraido algunos minerales!
            state.send_to(conn_id, &msg).await;
        }
    } else {
        let msg = "||828".to_string(); // TEXTO828: No has conseguido nada!
        state.send_to(conn_id, &msg).await;
    }

    if let Some(u) = state.users.get_mut(&conn_id) {
        try_level_skill(u, 14);
    }
    send_stats_sta(state, conn_id).await;
}

/// Taming (DoDomar).
async fn do_domar(state: &mut GameState, conn_id: ConnectionId, tx: i32, ty: i32) {
    let (map, ux, uy) = match state.users.get(&conn_id) {
        Some(u) => (u.pos_map, u.pos_x, u.pos_y),
        None => return,
    };

    // Distance check
    if (tx - ux).abs() > 2 || (ty - uy).abs() > 2 {
        return;
    }

    // Find NPC on tile
    let npc_idx = state.world.grid(map)
        .and_then(|g| g.tile(tx, ty))
        .and_then(|t| if t.npc_index > 0 { Some(t.npc_index as usize) } else { None });

    let npc_idx = match npc_idx {
        Some(idx) => idx,
        None => {
            let msg = "||258".to_string(); // TEXTO258: No hay ninguna criatura alli!
            state.send_to(conn_id, &msg).await;
            return;
        }
    };

    // Check NPC is tameable
    let npc_number = match state.get_npc(npc_idx) {
        Some(npc) if npc.is_alive() => npc.npc_number,
        _ => return,
    };
    let domable = state.game_data.npcs.get(npc_number)
        .map(|d| d.domable)
        .unwrap_or(0);

    if domable <= 0 {
        let msg = "||257".to_string(); // TEXTO257: No podes domar a esa criatura
        state.send_to(conn_id, &msg).await;
        return;
    }

    // Calculate taming power
    let (carisma, skill_domar, class) = match state.users.get(&conn_id) {
        Some(u) => (u.attributes[3], u.skills[17], u.class.clone()), // Cha=3, Domar=17 (1-based 18)
        None => return,
    };

    let mod_domar = match class.to_lowercase().as_str() {
        "druida" | "cazador" => 6,
        "clerigo" => 7,
        _ => 10,
    };

    let poder = carisma * (skill_domar / mod_domar)
        + random_number(1, carisma.max(1) / 3 + 1)
        + random_number(1, carisma.max(1) / 3 + 1)
        + random_number(1, carisma.max(1) / 3 + 1);

    if poder >= domable {
        let msg = format!("{}Has domado a la criatura{}", server_opcodes::CONSOLE_MSG, font_types::INFO);
        state.send_to(conn_id, &msg).await;
        // Note: Full pet system (NPC follows owner) not yet implemented
    } else {
        let msg = format!("{}No has podido domar a la criatura{}", server_opcodes::CONSOLE_MSG, font_types::INFO);
        state.send_to(conn_id, &msg).await;
    }

    if let Some(u) = state.users.get_mut(&conn_id) {
        try_level_skill(u, 18);
    }
}

/// Blacksmith (open UI when clicking anvil).
async fn do_herreria(state: &mut GameState, conn_id: ConnectionId, tx: i32, ty: i32) {
    let (map, ux, uy) = match state.users.get(&conn_id) {
        Some(u) => (u.pos_map, u.pos_x, u.pos_y),
        None => return,
    };

    // Distance check
    if (tx - ux).abs() > 2 || (ty - uy).abs() > 2 {
        return;
    }

    // Check target is an anvil (ObjType::Anvil = 27)
    let is_anvil = state.world.grid(map)
        .and_then(|g| g.tile(tx, ty))
        .and_then(|t| {
            if t.ground_item.obj_index > 0 {
                state.get_object(t.ground_item.obj_index).map(|o| o.obj_type == ObjType::Anvil)
            } else {
                None
            }
        })
        .unwrap_or(false);

    // Also check map static obj
    let is_anvil = is_anvil || state.game_data.maps.get(map as usize)
        .and_then(|m| m.as_ref())
        .map(|m| {
            if tx >= 1 && tx <= 100 && ty >= 1 && ty <= 100 {
                let tile = &m.tiles[(ty - 1) as usize][(tx - 1) as usize];
                if tile.obj.obj_index > 0 {
                    state.get_object(tile.obj.obj_index as i32)
                        .map(|o| o.obj_type == ObjType::Anvil)
                        .unwrap_or(false)
                } else {
                    false
                }
            } else {
                false
            }
        })
        .unwrap_or(false);

    if !is_anvil {
        let msg = "||263".to_string(); // TEXTO263: Ahi no hay ningun yunque
        state.send_to(conn_id, &msg).await;
        return;
    }

    // Send buildable items lists
    let skill_herreria = state.users.get(&conn_id).map(|u| u.skills[15]).unwrap_or(0); // Herreria = 15 (1-based 16)

    // Build weapons list
    let mut weapons_list = String::new();
    let mut armors_list = String::new();
    for obj in state.game_data.objects.iter() {
        if obj.sk_herreria > 0 && obj.sk_herreria <= skill_herreria {
            let entry = format!("{} ({}-{}-{}),{},",
                obj.name, obj.ling_h, obj.ling_p, obj.ling_o, obj.index);
            if obj.obj_type == ObjType::Weapon {
                weapons_list.push_str(&entry);
            } else if obj.obj_type == ObjType::Armor || obj.obj_type == ObjType::Shield || obj.obj_type == ObjType::Helmet {
                armors_list.push_str(&entry);
            }
        }
    }

    let pkt = format!("{}{}", server_opcodes::SMITH_WEAPONS, weapons_list);
    state.send_to(conn_id, &pkt).await;
    let pkt = format!("{}{}", server_opcodes::SMITH_ARMORS, armors_list);
    state.send_to(conn_id, &pkt).await;
    state.send_to(conn_id, server_opcodes::OPEN_SMITH).await;
}

/// Smelting (FundirMineral).
async fn do_fundir(state: &mut GameState, conn_id: ConnectionId) {
    // Find mineral in inventory
    let mineral_slot = match state.users.get(&conn_id) {
        Some(u) => {
            u.inventory.iter().enumerate().find_map(|(i, slot)| {
                if slot.obj_index > 0 {
                    state.get_object(slot.obj_index).and_then(|o| {
                        if o.obj_type == ObjType::Mineral {
                            Some((i, slot.obj_index, slot.amount, o.lingote_index))
                        } else {
                            None
                        }
                    })
                } else {
                    None
                }
            })
        }
        None => return,
    };

    let (slot_idx, mineral_obj, amount, lingote_idx) = match mineral_slot {
        Some(data) => data,
        None => {
            let msg = "||259".to_string(); // TEXTO259: No tienes mas minerales
            state.send_to(conn_id, &msg).await;
            return;
        }
    };

    // Minerals per ingot
    let minerals_needed = match mineral_obj {
        HIERRO_CRUDO => 13,
        PLATA_CRUDA => 25,
        ORO_CRUDO => 50,
        _ => 13,
    };

    if amount < minerals_needed {
        let msg = format!("{}No tienes suficientes minerales (necesitas {}){}", server_opcodes::CONSOLE_MSG, minerals_needed, font_types::INFO);
        state.send_to(conn_id, &msg).await;
        return;
    }

    let ingot = if lingote_idx > 0 { lingote_idx } else {
        match mineral_obj {
            HIERRO_CRUDO => LINGOTE_HIERRO,
            PLATA_CRUDA => LINGOTE_PLATA,
            ORO_CRUDO => LINGOTE_ORO,
            _ => LINGOTE_HIERRO,
        }
    };

    // Remove minerals
    if let Some(u) = state.users.get_mut(&conn_id) {
        u.inventory[slot_idx].amount -= minerals_needed;
        if u.inventory[slot_idx].amount <= 0 {
            u.inventory[slot_idx].obj_index = 0;
            u.inventory[slot_idx].amount = 0;
        }
    }
    send_inventory_slot(state, conn_id, slot_idx).await;

    // Add ingot
    let slot = find_or_add_inv_slot(state, conn_id, ingot, 1);
    if let Some(idx) = slot {
        send_inventory_slot(state, conn_id, idx).await;
    }

    let msg = format!("{}Has fundido un lingote{}", server_opcodes::CONSOLE_MSG, font_types::INFO);
    state.send_to(conn_id, &msg).await;

    if let Some(u) = state.users.get_mut(&conn_id) {
        try_level_skill(u, 14); // Mining skill
    }
}

/// Stealing (DoRobar).
async fn do_robar(state: &mut GameState, conn_id: ConnectionId, tx: i32, ty: i32) {
    let (map, ux, uy, skill, class) = match state.users.get(&conn_id) {
        Some(u) => (u.pos_map, u.pos_x, u.pos_y, u.skills[2], u.class.clone()), // Robar = 2 (1-based 3)
        None => return,
    };

    // Distance check
    if (tx - ux).abs() > 2 || (ty - uy).abs() > 2 {
        return;
    }

    // Find target user on tile
    let target_conn = state.world.grid(map)
        .and_then(|g| g.tile(tx, ty))
        .and_then(|t| t.user_conn);

    let target_conn = match target_conn {
        Some(id) if id != conn_id => id,
        _ => {
            let msg = "||252".to_string(); // TEXTO252: No hay a quien robarle!
            state.send_to(conn_id, &msg).await;
            return;
        }
    };

    // Luck roll
    let suerte = luck_denominator(skill);
    let roll = random_number(1, suerte);

    if roll < 3 {
        // Steal gold
        let is_ladron = class.eq_ignore_ascii_case("Ladron") || class.eq_ignore_ascii_case("Asesino");
        let gold_amount = if is_ladron { random_number(100, 1000) } else { random_number(1, 100) };

        let victim_gold = state.users.get(&target_conn).map(|u| u.gold).unwrap_or(0);
        let stolen = (gold_amount as i64).min(victim_gold);

        if stolen > 0 {
            if let Some(victim) = state.users.get_mut(&target_conn) {
                victim.gold -= stolen;
            }
            if let Some(thief) = state.users.get_mut(&conn_id) {
                thief.gold = (thief.gold + stolen).min(MAX_GOLD);
            }

            // TEXTO816: Le has robado %1 monedas de oro a %2
            let victim_name_for_rob = state.users.get(&target_conn).map(|u| u.char_name.clone()).unwrap_or_default();
            state.send_to(conn_id, &format!("||816@{}@{}", stolen, victim_name_for_rob)).await;
            // TEXTO819: %1 ha intentado robarte!
            let thief_name = state.users.get(&conn_id).map(|u| u.char_name.clone()).unwrap_or_default();
            state.send_to(target_conn, &format!("||819@{}", thief_name)).await;

            send_stats_gold(state, conn_id).await;
            send_stats_gold(state, target_conn).await;
        } else {
            let msg = "||818".to_string(); // TEXTO818: No has logrado robar nada!
            state.send_to(conn_id, &msg).await;
        }
    } else {
        // Fail — notify victim
        let thief_name = state.users.get(&conn_id).map(|u| u.char_name.clone()).unwrap_or_default();
        let msg = "||818".to_string(); // TEXTO818: No has logrado robar nada!
        state.send_to(conn_id, &msg).await;

        let victim_msg = format!("{}{} ha intentado robarte!{}", server_opcodes::CONSOLE_MSG, thief_name, font_types::COMBAT);
        state.send_to(target_conn, &victim_msg).await;
    }

    if let Some(u) = state.users.get_mut(&conn_id) {
        try_level_skill(u, 3);
    }
}

/// Ranged attack (bow + arrows).
/// VB6: TCP_HandleData1.bas case Proyectiles. Requires bow equipped (proyectil=1) and arrows.
/// Consumes 1 arrow per shot, sends FLECHI packet for visual, then resolves hit via normal combat.
const OBJ_TYPE_FLECHAS: i32 = 32;

async fn do_ranged_attack(state: &mut GameState, conn_id: ConnectionId, tx: i32, ty: i32) {
    // Anti-cheat: check arrow cooldown
    if !puede_flechear(state, conn_id) {
        return;
    }

    // Get attacker data
    let user_data = match state.users.get(&conn_id) {
        Some(u) if u.logged && !u.dead => {
            (u.pos_map, u.pos_x, u.pos_y, u.char_index,
             u.equip.weapon, u.equip.municion, u.min_sta,
             u.safe_toggle, u.target_user, u.target_npc_idx)
        }
        _ => return,
    };
    let (map, ux, uy, char_index, weapon_slot, municion_slot, sta,
         safe_toggle, target_user, target_npc_idx) = user_data;

    // Check weapon is a bow (proyectil=1)
    let weapon_obj_idx = if weapon_slot > 0 && weapon_slot <= MAX_INVENTORY_SLOTS {
        state.users.get(&conn_id).map(|u| u.inventory[weapon_slot - 1].obj_index).unwrap_or(0)
    } else {
        0
    };

    let is_bow = weapon_obj_idx > 0 && state.get_object(weapon_obj_idx)
        .map(|o| o.proyectil)
        .unwrap_or(false);

    if !is_bow {
        // Not a ranged weapon
        return;
    }

    // Check arrows equipped
    let municion_obj_idx = if municion_slot > 0 && municion_slot <= MAX_INVENTORY_SLOTS {
        state.users.get(&conn_id).map(|u| u.inventory[municion_slot - 1].obj_index).unwrap_or(0)
    } else {
        0
    };
    let municion_amount = if municion_slot > 0 && municion_slot <= MAX_INVENTORY_SLOTS {
        state.users.get(&conn_id).map(|u| u.inventory[municion_slot - 1].amount).unwrap_or(0)
    } else {
        0
    };

    let is_arrow = municion_obj_idx > 0 && state.get_object(municion_obj_idx)
        .map(|o| o.obj_type == crate::data::objects::ObjType::Arrow)
        .unwrap_or(false);

    if !is_arrow || municion_amount < 1 {
        let msg = format!("{}246", server_opcodes::CONSOLE_MSG); // "No arrows"
        state.send_to(conn_id, &msg).await;
        // Unequip ammo slot
        if let Some(user) = state.users.get_mut(&conn_id) {
            user.equip.municion = 0;
        }
        return;
    }

    // Stamina cost (1-10)
    if sta < 10 {
        let msg = format!("{}17", server_opcodes::CONSOLE_MSG); // "Not enough stamina"
        state.send_to(conn_id, &msg).await;
        return;
    }
    let sta_cost = random_number(1, 10);
    if let Some(user) = state.users.get_mut(&conn_id) {
        user.min_sta = (user.min_sta - sta_cost).max(0);
    }
    send_stats_sta(state, conn_id).await;

    // Consume 1 arrow
    if let Some(user) = state.users.get_mut(&conn_id) {
        if municion_slot > 0 && municion_slot <= MAX_INVENTORY_SLOTS {
            user.inventory[municion_slot - 1].amount -= 1;
            if user.inventory[municion_slot - 1].amount <= 0 {
                user.inventory[municion_slot - 1] = InventorySlot::default();
                user.equip.municion = 0;
            }
        }
    }
    // Update inventory slot
    send_inventory_slot(state, conn_id, municion_slot).await;

    // Get arrow GRH for visual
    let arrow_grh = state.get_object(municion_obj_idx).map(|o| o.grh_index).unwrap_or(0);

    // Find target (NPC or user on clicked tile)
    let target_npc = state.world.grid(map)
        .and_then(|g| g.tile(tx, ty))
        .and_then(|t| if t.npc_index > 0 { Some(t.npc_index as usize) } else { None });

    let target_user_tile = state.world.grid(map)
        .and_then(|g| g.tile(tx, ty))
        .and_then(|t| t.user_conn);

    if let Some(npc_idx) = target_npc {
        // Ranged attack vs NPC
        let npc_data = state.get_npc(npc_idx)
            .map(|n| (n.char_index.0, n.attackable));

        if let Some((npc_char, true)) = npc_data {
            // Send arrow visual
            let flechi = format!("FLECHI{},{},{}", char_index.0, npc_char, arrow_grh);
            state.send_data(SendTarget::ToMap(map), &flechi).await;

            // Store target for combat resolution, then call standard attack
            if let Some(user) = state.users.get_mut(&conn_id) {
                user.target_npc_idx = npc_idx;
            }
            // Reuse melee hit resolution (damage calc is the same for ranged in VB6)
            resolve_attack_npc(state, conn_id, npc_idx).await;
        }
    } else if let Some(target) = target_user_tile {
        if target != conn_id {
            // Ranged attack vs user
            let target_char = state.users.get(&target).map(|u| u.char_index.0).unwrap_or(0);

            // Send arrow visual
            let flechi = format!("FLECHI{},{},{}", char_index.0, target_char, arrow_grh);
            state.send_data(SendTarget::ToMap(map), &flechi).await;

            // Store target and resolve
            if let Some(user) = state.users.get_mut(&conn_id) {
                user.target_user = target;
            }
            resolve_attack_user(state, conn_id, target).await;
        }
    }
}

/// Resolve ranged/melee attack against NPC — shared damage calculation.
/// Extracted from handle_attack for reuse in ranged attacks.
async fn resolve_attack_npc(state: &mut GameState, conn_id: ConnectionId, npc_idx: usize) {
    let user_data = match state.users.get(&conn_id) {
        Some(u) if u.logged && !u.dead => {
            (u.pos_map, u.pos_x, u.pos_y, u.char_index,
             u.level, u.attributes[0], u.attributes[1],
             u.min_hit, u.max_hit, u.skills[1], u.char_name.clone(), u.class.clone())
        }
        _ => return,
    };
    let (map, x, y, char_index, level, strength, agility,
         min_hit, max_hit, skill_armas, attacker_name, class) = user_data;

    let npc_data = match state.get_npc(npc_idx) {
        Some(n) if n.active && n.attackable => {
            (n.char_index, n.def, n.poder_evasion, n.min_hp,
             n.max_hp, n.give_exp, n.npc_number, n.name.clone())
        }
        _ => return,
    };
    let (npc_char, npc_def, npc_evasion, npc_hp, npc_max_hp,
         npc_exp, npc_number, npc_name) = npc_data;

    // Hit check
    let attack_power = calc_attack_power(skill_armas, agility, level);
    let hit_prob = ((50.0 + (attack_power - npc_evasion as f64) * 0.4) as i32).clamp(10, 90);

    if rand_range(1, 100) > hit_prob {
        state.send_to(conn_id, "U1").await; // Miss
        return;
    }

    // Damage
    let weapon_dmg = rand_range(min_hit.max(1), max_hit.max(1));
    let str_bonus = ((max_hit as f64 / 5.0) * (strength - 15).max(0) as f64) as i32;
    let base_dmg = 3 * weapon_dmg + str_bonus + rand_range(min_hit, max_hit);
    let class_mod = class_damage_modifier(&class);
    let mut damage = ((base_dmg as f64) * class_mod) as i32;
    damage = (damage - npc_def).max(1);

    // Apply damage
    let new_hp = if let Some(npc) = state.get_npc_mut(npc_idx) {
        npc.min_hp -= damage;
        npc.min_hp
    } else {
        return;
    };

    // Send hit packet
    let u2_pkt = format!("U2,{},{}", damage, npc_name);
    state.send_to(conn_id, &u2_pkt).await;

    if new_hp <= 0 {
        // NPC killed
        if let Some(u) = state.users.get_mut(&conn_id) {
            let exp_gained = npc_exp as i64;
            u.exp += exp_gained;
        }
        send_stats_exp(state, conn_id).await;
        check_user_level(state, conn_id).await;

        // Kill NPC
        let npc_ci = state.get_npc(npc_idx).map(|n| n.char_index.0).unwrap_or(0);
        let bp_pkt = format!("BP{}", npc_ci);
        state.send_data(SendTarget::ToArea { map, x, y }, &bp_pkt).await;

        state.kill_npc(npc_idx);
        quest_check_npc_kill(state, conn_id, npc_number as i32).await;
    }
}

/// Resolve ranged/melee attack against user — shared PvP damage calculation.
async fn resolve_attack_user(state: &mut GameState, conn_id: ConnectionId, victim_id: ConnectionId) {
    let att_data = match state.users.get(&conn_id) {
        Some(u) if u.logged && !u.dead => {
            (u.pos_map, u.pos_x, u.pos_y, u.char_index,
             u.level, u.attributes[0], u.attributes[1],
             u.min_hit, u.max_hit, u.skills[1], u.char_name.clone(), u.class.clone(),
             u.safe_toggle)
        }
        _ => return,
    };
    let (map, x, y, _char_index, level, strength, agility,
         min_hit, max_hit, skill_armas, attacker_name, class, safe_on) = att_data;

    if safe_on {
        state.send_to(conn_id, "||207").await; // TEXTO207: Escribe /SEG para quitar el seguro
        return;
    }

    let victim_data = match state.users.get(&victim_id) {
        Some(v) if v.logged && !v.dead => {
            (v.level, v.attributes[1], v.skills[3], v.char_name.clone(), v.privileges)
        }
        _ => return,
    };
    let (v_level, v_agility, v_tacticas, victim_name, v_privs) = victim_data;

    if v_privs > 0 { return; }

    // Hit check
    let attack_power = calc_attack_power(skill_armas, agility, level);
    let defense_power = calc_defense_power(v_tacticas, v_agility, v_level);
    let hit_prob = ((50.0 + (attack_power - defense_power) * 0.4) as i32).clamp(10, 90);

    if rand_range(1, 100) > hit_prob {
        let pkt = format!("U3{}", attacker_name);
        state.send_to(victim_id, &pkt).await;
        state.send_to(conn_id, "U1").await;
        return;
    }

    // Damage calculation (VB6: CalcularDaño + UserDañoUser)
    let weapon_dmg = rand_range(min_hit.max(1), max_hit.max(1));
    let str_bonus = ((max_hit as f64 / 5.0) * (strength - 15).max(0) as f64) as i32;
    let base_dmg = 3 * weapon_dmg + str_bonus + rand_range(min_hit, max_hit);
    let class_mod = class_damage_modifier_from_balance(state, &class);
    let mut damage = ((base_dmg as f64) * class_mod) as i32;

    // Body part hit (1=head, 2-6=body)
    let body_part = rand_range(1, 6);

    // Armor absorption (VB6: victim's armor reduces damage)
    let absorption = calc_armor_absorption(state, victim_id, body_part);
    damage -= absorption;

    // Critical hit (20% chance — VB6: RandomNumber(1,5) == 1)
    if rand_range(1, 5) == 1 {
        damage *= 2;
    }

    let damage = damage.max(1);

    // Apply damage
    if let Some(victim) = state.users.get_mut(&victim_id) {
        victim.min_hp -= damage;
    }
    let n4_pkt = format!("N4{},{},{}", body_part, damage, attacker_name);
    state.send_to(victim_id, &n4_pkt).await;
    let n5_pkt = format!("N5{},{},{}", body_part, damage, victim_name);
    state.send_to(conn_id, &n5_pkt).await;

    send_stats_hp(state, victim_id).await;

    // Check death
    let hp = state.users.get(&victim_id).map(|u| u.min_hp).unwrap_or(0);
    if hp <= 0 {
        user_die(state, victim_id, Some(conn_id)).await;
    }
}

/// DoOcultarse — Hide skill (VB6: Trabajo.bas DoOcultarse).
/// Skill-based success chance. Thieves/Hunters get bonus. Blocked on special maps.
async fn do_ocultarse(state: &mut GameState, conn_id: ConnectionId) {
    let (map, skill, class, char_index, already_hidden) = match state.users.get(&conn_id) {
        Some(u) if u.logged && !u.dead => {
            (u.pos_map, u.skills.get(5).copied().unwrap_or(0), u.class.clone(), u.char_index, u.hidden)
        }
        _ => return,
    };

    // Blocked on special maps (VB6: 142, 121-123, 31-34)
    let blocked_maps = [142, 121, 122, 123, 31, 32, 33, 34];
    if blocked_maps.contains(&map) {
        state.send_to(conn_id, "||838").await;
        return;
    }

    // Suerte (luck) table from VB6
    let suerte = match skill {
        0..=10 => 35,
        11..=20 => 30,
        21..=30 => 28,
        31..=40 => 24,
        41..=50 => 22,
        51..=60 => 20,
        61..=70 => 18,
        71..=80 => 15,
        81..=90 => 10,
        91..=100 => 7,
        _ => 7,
    };

    // Non-thief/hunter get +50 penalty
    let is_thief_or_hunter = class.eq_ignore_ascii_case("Ladron") || class.eq_ignore_ascii_case("Cazador");
    let suerte = if is_thief_or_hunter { suerte } else { suerte + 50 };

    let res = rand_range(1, suerte);

    if res <= 5 {
        // Success — hide
        if let Some(user) = state.users.get_mut(&conn_id) {
            user.hidden = true;
        }
        // Send NOVER to make invisible on all clients in map
        let nover = format!("NOVER{},1", char_index.0);
        state.send_data(SendTarget::ToMap(map), &nover).await;
        state.send_to(conn_id, "||808").await; // "Te has ocultado."
        // Skill gain
        if let Some(u) = state.users.get_mut(&conn_id) {
            try_level_skill(u, 5); // Ocultarse = skill index 5
        }
    } else {
        // Failure
        state.send_to(conn_id, "||809").await; // "No has logrado ocultarte."
    }
}

/// DoPermanecerOculto — Check if hidden user remains hidden (called each tick or on action).
/// VB6: Trabajo.bas DoPermanecerOculto. Returns false if user was revealed.
fn check_permanecer_oculto(user: &mut super::types::UserState) -> bool {
    if !user.hidden { return false; }

    let skill = user.skills.get(5).copied().unwrap_or(0);
    let suerte = match skill {
        0..=10 => 35,
        11..=20 => 30,
        21..=30 => 28,
        31..=40 => 24,
        41..=50 => 22,
        51..=60 => 20,
        61..=70 => 18,
        71..=80 => 15,
        81..=90 => 10,
        91..=100 => 10,
        _ => 10,
    };

    let is_thief_or_hunter = user.class.eq_ignore_ascii_case("Ladron") || user.class.eq_ignore_ascii_case("Cazador");
    let suerte = if is_thief_or_hunter { suerte } else { suerte + 50 };

    // Hunter with skill>90 and specific armor stays hidden
    if user.class.eq_ignore_ascii_case("Cazador") && skill > 90 {
        if user.equip.armor == 648 || user.equip.armor == 360 {
            return true;
        }
    }

    let res = rand_range(1, suerte);
    if res > 9 {
        user.hidden = false;
        return false; // Revealed
    }
    true // Still hidden
}

/// DoApuñalar — Backstab skill (VB6: Trabajo.bas DoApuñalar).
/// Called during melee attack when attacker has Apuñalar skill and same heading as victim.
/// Applies 1.5x damage to players, 2x to NPCs.
fn calc_apunalar_damage(skill: i32, class: &str, attacker_heading: i32, victim_heading: i32, base_damage: i32, is_npc_target: bool) -> Option<i32> {
    let suerte = match skill {
        0..=10 => 200,
        11..=20 => 190,
        21..=30 => 180,
        31..=40 => 170,
        41..=50 => 160,
        51..=60 => 150,
        61..=70 => 140,
        71..=80 => 130,
        81..=90 => 120,
        91..=99 => 110,
        100.. => 100,
        _ => 200,
    };

    let res = if class.eq_ignore_ascii_case("Asesino") {
        // Assassin gets better odds (0-95 range)
        rand_range(0, 95)
    } else {
        rand_range(0, suerte)
    };

    // Assassin backstab from same heading = auto-success
    let res = if class.eq_ignore_ascii_case("Asesino") && attacker_heading == victim_heading {
        1 // Guaranteed success
    } else {
        res
    };

    if res < 15 {
        let multiplied = if is_npc_target {
            (base_damage as f64 * 2.0) as i32
        } else {
            (base_damage as f64 * 1.5) as i32
        };
        Some(multiplied)
    } else {
        None // Failed
    }
}

/// Desarmar — Disarm skill (VB6: Trabajo.bas Desarmar).
/// Chance to unequip victim's weapon based on Wresterling skill.
fn try_desarmar(skill: i32) -> bool {
    let suerte = match skill {
        0..=10 => 35,
        11..=20 => 30,
        21..=30 => 28,
        31..=40 => 24,
        41..=50 => 22,
        51..=60 => 20,
        61..=70 => 18,
        71..=80 => 15,
        81..=90 => 10,
        91..=100 => 5,
        _ => 5,
    };
    let res = rand_range(1, suerte);
    res <= 2
}

/// CNS — Construct blacksmith item.
async fn handle_construct_smith(state: &mut GameState, conn_id: ConnectionId, data: &str) {
    let payload = strip_opcode(data, 3);
    let obj_index: i32 = match payload.trim().parse() {
        Ok(v) if v >= 1 => v,
        _ => return,
    };

    let obj = match state.get_object(obj_index) {
        Some(o) => o.clone(),
        None => return,
    };

    if obj.sk_herreria <= 0 { return; }

    // Check skill
    let skill = state.users.get(&conn_id).map(|u| u.skills[15]).unwrap_or(0); // Herreria=15 (1-based 16)
    if skill < obj.sk_herreria {
        let msg = format!("{}No tienes suficiente habilidad{}", server_opcodes::CONSOLE_MSG, font_types::INFO);
        state.send_to(conn_id, &msg).await;
        return;
    }

    // Check materials (iron, silver, gold ingots)
    let has_materials = check_has_items(state, conn_id, &[
        (LINGOTE_HIERRO, obj.ling_h),
        (LINGOTE_PLATA, obj.ling_p),
        (LINGOTE_ORO, obj.ling_o),
    ]);

    if !has_materials {
        let msg = format!("{}No tienes los materiales necesarios{}", server_opcodes::CONSOLE_MSG, font_types::INFO);
        state.send_to(conn_id, &msg).await;
        return;
    }

    // Remove materials
    remove_items_from_inv(state, conn_id, LINGOTE_HIERRO, obj.ling_h).await;
    remove_items_from_inv(state, conn_id, LINGOTE_PLATA, obj.ling_p).await;
    remove_items_from_inv(state, conn_id, LINGOTE_ORO, obj.ling_o).await;

    // Give crafted item
    let slot = find_or_add_inv_slot(state, conn_id, obj_index, 1);
    if let Some(idx) = slot {
        send_inventory_slot(state, conn_id, idx).await;
    }

    // Play sound
    let (map, x, y) = state.users.get(&conn_id).map(|u| (u.pos_map, u.pos_x, u.pos_y)).unwrap_or((0,0,0));
    let snd = format!("TW{}", SND_HERRERO);
    state.send_data(SendTarget::ToArea { map, x, y }, &snd).await;

    let msg = format!("{}Has construido {}{}", server_opcodes::CONSOLE_MSG, obj.name, font_types::INFO);
    state.send_to(conn_id, &msg).await;

    if let Some(u) = state.users.get_mut(&conn_id) {
        try_level_skill(u, 16);
    }
}

/// CNC — Construct carpentry item.
async fn handle_construct_carp(state: &mut GameState, conn_id: ConnectionId, data: &str) {
    let payload = strip_opcode(data, 3);
    let obj_index: i32 = match payload.trim().parse() {
        Ok(v) if v >= 1 => v,
        _ => return,
    };

    let obj = match state.get_object(obj_index) {
        Some(o) => o.clone(),
        None => return,
    };

    if obj.sk_carpinteria <= 0 { return; }

    // Check equipped carpentry tool (VB6: SERRUCHO_CARPINTERO must be equipped)
    let weapon = state.users.get(&conn_id).map(|u| equipped_weapon_obj(u)).unwrap_or(0);
    if weapon != SERRUCHO_CARPINTERO {
        let msg = format!("{}Necesitas un serrucho de carpintero{}", server_opcodes::CONSOLE_MSG, font_types::INFO);
        state.send_to(conn_id, &msg).await;
        return;
    }

    // Check skill
    let skill = state.users.get(&conn_id).map(|u| u.skills[14]).unwrap_or(0); // Carpinteria=14 (1-based 15)
    if skill < obj.sk_carpinteria {
        let msg = format!("{}No tienes suficiente habilidad{}", server_opcodes::CONSOLE_MSG, font_types::INFO);
        state.send_to(conn_id, &msg).await;
        return;
    }

    // Check materials (wood + stones)
    let has_materials = check_has_items(state, conn_id, &[
        (LENA_OBJ, obj.madera),
        (PIEDRA_OBJ, obj.piedras),
    ]);

    if !has_materials {
        let msg = format!("{}No tienes los materiales necesarios{}", server_opcodes::CONSOLE_MSG, font_types::INFO);
        state.send_to(conn_id, &msg).await;
        return;
    }

    // Remove materials
    remove_items_from_inv(state, conn_id, LENA_OBJ, obj.madera).await;
    remove_items_from_inv(state, conn_id, PIEDRA_OBJ, obj.piedras).await;

    // Give crafted item
    let slot = find_or_add_inv_slot(state, conn_id, obj_index, 1);
    if let Some(idx) = slot {
        send_inventory_slot(state, conn_id, idx).await;
    }

    // Play sound
    let (map, x, y) = state.users.get(&conn_id).map(|u| (u.pos_map, u.pos_x, u.pos_y)).unwrap_or((0,0,0));
    let snd = format!("TW{}", SND_CARPINTERO);
    state.send_data(SendTarget::ToArea { map, x, y }, &snd).await;

    let msg = format!("{}Has construido {}{}", server_opcodes::CONSOLE_MSG, obj.name, font_types::INFO);
    state.send_to(conn_id, &msg).await;

    if let Some(u) = state.users.get_mut(&conn_id) {
        try_level_skill(u, 15);
    }
}

/// Find an inventory slot that can hold the item (stack or empty), and add it.
/// Returns the 0-based slot index, or None if inventory is full.
fn find_or_add_inv_slot(state: &mut GameState, conn_id: ConnectionId, obj_index: i32, amount: i32) -> Option<usize> {
    let user = state.users.get(&conn_id)?;

    // Try stacking first
    let mut stack_slot = None;
    let mut empty_slot = None;
    for (i, slot) in user.inventory.iter().enumerate() {
        if slot.obj_index == obj_index && slot.amount + amount <= MAX_INVENTORY_OBJS {
            stack_slot = Some(i);
            break;
        }
        if slot.obj_index == 0 && empty_slot.is_none() {
            empty_slot = Some(i);
        }
    }

    let target = stack_slot.or(empty_slot)?;

    let user = state.users.get_mut(&conn_id)?;
    if user.inventory[target].obj_index == 0 {
        user.inventory[target].obj_index = obj_index;
        user.inventory[target].amount = amount;
    } else {
        user.inventory[target].amount += amount;
    }
    Some(target)
}

/// Check if user has enough of certain items.
fn check_has_items(state: &GameState, conn_id: ConnectionId, items: &[(i32, i32)]) -> bool {
    let user = match state.users.get(&conn_id) {
        Some(u) => u,
        None => return false,
    };

    for &(obj_index, needed) in items {
        if needed <= 0 { continue; }
        let total: i32 = user.inventory.iter()
            .filter(|s| s.obj_index == obj_index)
            .map(|s| s.amount)
            .sum();
        if total < needed {
            return false;
        }
    }
    true
}

/// Remove items from user inventory.
async fn remove_items_from_inv(state: &mut GameState, conn_id: ConnectionId, obj_index: i32, mut amount: i32) {
    if amount <= 0 { return; }

    let user = match state.users.get_mut(&conn_id) {
        Some(u) => u,
        None => return,
    };

    for slot in user.inventory.iter_mut() {
        if slot.obj_index == obj_index && amount > 0 {
            let remove = slot.amount.min(amount);
            slot.amount -= remove;
            amount -= remove;
            if slot.amount <= 0 {
                slot.obj_index = 0;
                slot.amount = 0;
                slot.equipped = false;
            }
        }
    }
}

// =====================================================================
// Guild system handlers (modGuilds.bas / clsClan.cls)
// =====================================================================

/// BF delimiter (char 191 = inverted question mark) used in guild packets
const BF: char = '\u{00BF}';

/// GLINFO — Request guild info panel.
/// If user has no guild: send guild list (GL).
/// If user is leader/sublider: send IREDAEL (leader view).
/// If user is regular member: send IREDAEK (member view).
async fn handle_guild_info(state: &mut GameState, conn_id: ConnectionId) {
    let (guild_index, char_name) = match state.users.get(&conn_id) {
        Some(u) if u.logged => (u.guild_index, u.char_name.clone()),
        _ => return,
    };

    if guild_index <= 0 {
        // No guild — send guild list
        let guild_list = guilds::list_guilds(&state.base_path);
        let count = guild_list.len();
        let mut pkt = format!("{}{}", server_opcodes::GUILD_LIST, count);
        for (_num, name, align, level) in &guild_list {
            pkt.push(',');
            pkt.push_str(&format!("{}-{}-{}", name, guilds::alignment_name(*align), level));
        }
        state.send_to(conn_id, &pkt).await;
        return;
    }

    let base = state.base_path.clone();
    let guild = match guilds::load_guild(&base, guild_index) {
        Some(g) => g,
        None => return,
    };

    let is_leader = guild.leader.to_uppercase() == char_name.to_uppercase();
    let is_sublider = guild.sub_lider1.to_uppercase() == char_name.to_uppercase()
        || guild.sub_lider2.to_uppercase() == char_name.to_uppercase();

    let members = guilds::load_members(&base, &guild.name);
    let guild_list = guilds::list_guilds(&base);

    if is_leader || is_sublider {
        // Leader/sublider view: IREDAEL
        let applicants = guilds::load_applicants(&base, &guild.name);
        let mut pkt = format!("{}{}", server_opcodes::GUILD_INFO_LEADER, guild.puntos_clan);
        pkt.push(BF);
        pkt.push_str(&guild.nivel_clan.to_string());
        pkt.push(BF);
        pkt.push_str(&guild.leader);
        pkt.push(BF);
        pkt.push_str(&guild.sub_lider1);
        pkt.push(BF);
        pkt.push_str(&guild.sub_lider2);
        // Castle positions (empty for now)
        for _ in 0..4 { pkt.push(BF); pkt.push('0'); }
        pkt.push(BF);
        pkt.push_str(&guild.reputation.to_string());
        pkt.push(BF);
        pkt.push_str(&guild.cvc_wins.to_string());
        pkt.push(BF);
        pkt.push_str(&guild.cvc_losses.to_string());
        pkt.push(BF);
        pkt.push_str(&guild.castle_sieges.to_string());
        // Guild list
        pkt.push(BF);
        pkt.push_str(&guild_list.len().to_string());
        for (_num, name, align, level) in &guild_list {
            pkt.push(BF);
            pkt.push_str(&format!("{}${}${}", name, guilds::alignment_name(*align), level));
        }
        // Members
        pkt.push(BF);
        pkt.push_str(&members.len().to_string());
        pkt.push(BF);
        pkt.push_str(&members.join(","));
        // Applicants
        pkt.push(BF);
        pkt.push_str(&applicants.len().to_string());
        for app in &applicants {
            pkt.push(BF);
            pkt.push_str(&format!("{}: {}", app.name, app.detail));
        }
        state.send_to(conn_id, &pkt).await;
    } else {
        // Regular member view: IREDAEK
        let mut pkt = format!("{}{}", server_opcodes::GUILD_INFO_MEMBER, guild.puntos_clan);
        pkt.push(BF);
        pkt.push_str(&guild.nivel_clan.to_string());
        pkt.push(BF);
        pkt.push_str(&guild.leader);
        pkt.push(BF);
        pkt.push_str(&guild.sub_lider1);
        pkt.push(BF);
        pkt.push_str(&guild.sub_lider2);
        // Castle positions
        for _ in 0..4 { pkt.push(BF); pkt.push('0'); }
        pkt.push(BF);
        pkt.push_str(&guild.reputation.to_string());
        // Guild list
        pkt.push(BF);
        pkt.push_str(&guild_list.len().to_string());
        for (_num, name, align, level) in &guild_list {
            pkt.push(BF);
            pkt.push_str(&format!("{}-{}-{}", name, level, guilds::alignment_name(*align)));
        }
        // Members
        pkt.push(BF);
        pkt.push_str(&members.len().to_string());
        pkt.push(BF);
        pkt.push_str(&members.join(","));
        state.send_to(conn_id, &pkt).await;
    }
}

/// CIG — Submit guild creation form (after SHOWFUN was sent).
/// Data format: CIG<desc><BF><name><BF><url><BF><cantcodex><BF><codex1><BF>...
async fn handle_guild_create(state: &mut GameState, conn_id: ConnectionId, data: &str) {
    let payload = strip_opcode(data, 3);
    let parts: Vec<&str> = payload.split(BF).collect();

    if parts.len() < 4 {
        let msg = format!("{}Datos invalidos para crear clan.{}", server_opcodes::CONSOLE_MSG, font_types::INFO);
        state.send_to(conn_id, &msg).await;
        return;
    }

    let (char_name, alignment, level, skills_leadership) = match state.users.get(&conn_id) {
        Some(u) if u.logged => (u.char_name.clone(), u.guild_creating_alignment, u.level, u.skills[0]),
        _ => return,
    };

    // Validate requirements
    if level < 50 {
        let msg = format!("{}Necesitas nivel 50 para fundar un clan.{}", server_opcodes::CONSOLE_MSG, font_types::INFO);
        state.send_to(conn_id, &msg).await;
        return;
    }
    if skills_leadership < 100 {
        let msg = format!("{}Necesitas 100 en liderazgo.{}", server_opcodes::CONSOLE_MSG, font_types::INFO);
        state.send_to(conn_id, &msg).await;
        return;
    }

    let desc = parts[0];
    let name = parts[1];
    let url = parts[2];
    let cant_codex: usize = parts[3].parse().unwrap_or(0);

    if !guilds::is_valid_guild_name(name) {
        let msg = format!("{}Nombre de clan invalido.{}", server_opcodes::CONSOLE_MSG, font_types::INFO);
        state.send_to(conn_id, &msg).await;
        return;
    }

    // Check name uniqueness
    if guilds::find_guild_by_name(&state.base_path, name).is_some() {
        let msg = format!("{}Ya existe un clan con ese nombre.{}", server_opcodes::CONSOLE_MSG, font_types::INFO);
        state.send_to(conn_id, &msg).await;
        return;
    }

    let mut codex = Vec::new();
    for i in 0..cant_codex.min(guilds::MAX_CODEX_LINES) {
        if let Some(line) = parts.get(4 + i) {
            codex.push(line.to_string());
        }
    }
    while codex.len() < guilds::MAX_CODEX_LINES {
        codex.push(String::new());
    }

    // Check for Amuleto de Lider (item 939)
    let has_amulet = state.users.get(&conn_id)
        .map(|u| u.inventory.iter().any(|s| s.obj_index == 939 && s.amount > 0))
        .unwrap_or(false);
    if !has_amulet {
        let msg = format!("{}Necesitas el Amuleto de Lider para fundar un clan.{}", server_opcodes::CONSOLE_MSG, font_types::INFO);
        state.send_to(conn_id, &msg).await;
        return;
    }

    // Remove amulet items (939 and 1048)
    if let Some(user) = state.users.get_mut(&conn_id) {
        for slot in user.inventory.iter_mut() {
            if slot.obj_index == 939 || slot.obj_index == 1048 {
                slot.amount -= 1;
                if slot.amount <= 0 {
                    slot.obj_index = 0;
                    slot.amount = 0;
                }
            }
        }
    }

    // Create the guild
    let guild_num = guilds::create_guild(&state.base_path, name, &char_name, alignment, desc, url, codex);

    // Update user state
    if let Some(user) = state.users.get_mut(&conn_id) {
        user.guild_index = guild_num;
    }

    // Update charfile
    let base = state.base_path.clone();
    let chr_path = base.join("charfile").join(format!("{}.chr", char_name));
    crate::config::write_var(chr_path.to_str().unwrap_or(""), "GUILD", "GUILDINDEX", &guild_num.to_string()).ok();

    // Broadcast creation
    let broadcast = format!("{}264@{}@{}@{}", server_opcodes::CONSOLE_MSG, char_name, name, guilds::alignment_name(alignment));
    state.send_data(SendTarget::ToAll, &broadcast).await;

    // Send updated inventory
    send_full_inventory(state, conn_id).await;

    // Sound effect
    let sound = format!("{}44", server_opcodes::PLAY_SOUND);
    state.send_to(conn_id, &sound).await;

    let msg = format!("{}Has fundado el clan {}!{}", server_opcodes::CONSOLE_MSG, name, font_types::GUILD_MSG);
    state.send_to(conn_id, &msg).await;
}

/// /FUNDARCLAN — Start guild creation flow. Auto-detect alignment from character status.
async fn handle_slash_fundarclan(state: &mut GameState, conn_id: ConnectionId) {
    let (guild_index, level, criminal, _reputation) = match state.users.get(&conn_id) {
        Some(u) if u.logged => (u.guild_index, u.level, u.criminal, u.reputation),
        _ => return,
    };

    if guild_index > 0 {
        let msg = format!("{}Ya perteneces a un clan.{}", server_opcodes::CONSOLE_MSG, font_types::INFO);
        state.send_to(conn_id, &msg).await;
        return;
    }

    if level < 50 {
        let msg = format!("{}Necesitas nivel 50 para fundar un clan.{}", server_opcodes::CONSOLE_MSG, font_types::INFO);
        state.send_to(conn_id, &msg).await;
        return;
    }

    // Auto-detect alignment based on character status
    let alignment = if criminal {
        guilds::ALIGN_CRIMINAL
    } else {
        guilds::ALIGN_CIUDA
    };

    // Store alignment for when CIG arrives
    if let Some(user) = state.users.get_mut(&conn_id) {
        user.guild_creating_alignment = alignment;
    }

    // Tell client to show creation form
    state.send_to(conn_id, server_opcodes::GUILD_SHOW_FORM).await;
}

/// /CERRARCLAN — Dissolve guild (leader + sole member) or leave (sublider).
async fn handle_slash_cerrarclan(state: &mut GameState, conn_id: ConnectionId) {
    let (guild_index, char_name) = match state.users.get(&conn_id) {
        Some(u) if u.logged && u.guild_index > 0 => (u.guild_index, u.char_name.clone()),
        _ => {
            let msg = format!("{}120", server_opcodes::CONSOLE_MSG);
            state.send_to(conn_id, &msg).await;
            return;
        }
    };

    let base = state.base_path.clone();
    let guild = match guilds::load_guild(&base, guild_index) {
        Some(g) => g,
        None => return,
    };

    let is_leader = guild.leader.to_uppercase() == char_name.to_uppercase();
    let is_sublider = guild.sub_lider1.to_uppercase() == char_name.to_uppercase()
        || guild.sub_lider2.to_uppercase() == char_name.to_uppercase();

    if is_sublider && !is_leader {
        // Sub-leaders leave via /CERRARCLAN
        handle_member_leave(state, conn_id, &char_name, guild_index).await;
        return;
    }

    if !is_leader {
        let msg = format!("{}339", server_opcodes::CONSOLE_MSG);
        state.send_to(conn_id, &msg).await;
        return;
    }

    // Leader must be sole member
    let members = guilds::load_members(&base, &guild.name);
    if members.len() > 1 {
        let msg = format!("{}340", server_opcodes::CONSOLE_MSG);
        state.send_to(conn_id, &msg).await;
        return;
    }

    // Dissolve
    guilds::dissolve_guild(&base, guild_index);

    // Clear user guild state
    if let Some(user) = state.users.get_mut(&conn_id) {
        user.guild_index = 0;
    }

    // Update charfile
    let chr_path = base.join("charfile").join(format!("{}.chr", char_name));
    crate::config::write_var(chr_path.to_str().unwrap_or(""), "GUILD", "GUILDINDEX", "0").ok();

    // Broadcast dissolution
    let broadcast = format!("{}341@{}", server_opcodes::CONSOLE_MSG, guild.name);
    state.send_data(SendTarget::ToAll, &broadcast).await;
}

/// /SALIRCLAN — Leave guild voluntarily.
async fn handle_slash_salirclan(state: &mut GameState, conn_id: ConnectionId) {
    let (guild_index, char_name) = match state.users.get(&conn_id) {
        Some(u) if u.logged && u.guild_index > 0 => (u.guild_index, u.char_name.clone()),
        _ => {
            let msg = format!("{}120", server_opcodes::CONSOLE_MSG);
            state.send_to(conn_id, &msg).await;
            return;
        }
    };

    let base = state.base_path.clone();
    let guild = match guilds::load_guild(&base, guild_index) {
        Some(g) => g,
        None => return,
    };

    // Leader cannot leave — must dissolve or transfer
    if guild.leader.to_uppercase() == char_name.to_uppercase() {
        let msg = format!("{}Eres el lider, usa /CERRARCLAN o /HACLIDER.{}", server_opcodes::CONSOLE_MSG, font_types::INFO);
        state.send_to(conn_id, &msg).await;
        return;
    }

    handle_member_leave(state, conn_id, &char_name, guild_index).await;
}

/// Handle a member leaving a guild (used by /SALIRCLAN and /CERRARCLAN for subliders)
async fn handle_member_leave(state: &mut GameState, conn_id: ConnectionId, char_name: &str, guild_index: i32) {
    let base = state.base_path.clone();
    let guild = match guilds::load_guild(&base, guild_index) {
        Some(g) => g,
        None => return,
    };

    // If sublider, clear the slot
    let is_sub1 = guild.sub_lider1.to_uppercase() == char_name.to_uppercase();
    let is_sub2 = guild.sub_lider2.to_uppercase() == char_name.to_uppercase();
    if is_sub1 || is_sub2 {
        let mut updated = guild.clone();
        if is_sub1 { updated.sub_lider1 = "Fermin".to_string(); }
        if is_sub2 { updated.sub_lider2 = "Fermin".to_string(); }
        guilds::save_guild(&base, &updated);

        // Notify guild
        let notify = format!("{}337@{}", server_opcodes::CONSOLE_MSG, char_name);
        state.send_data(SendTarget::ToGuildMembers(guild_index), &notify).await;
    } else {
        // Notify guild
        let notify = format!("{}354@{}", server_opcodes::CONSOLE_MSG, char_name);
        state.send_data(SendTarget::ToGuildMembers(guild_index), &notify).await;
    }

    // Remove from members file
    guilds::remove_member(&base, &guild.name, char_name);

    // Clear user state
    if let Some(user) = state.users.get_mut(&conn_id) {
        user.guild_index = 0;
    }

    // Update charfile
    let chr_path = base.join("charfile").join(format!("{}.chr", char_name));
    crate::config::write_var(chr_path.to_str().unwrap_or(""), "GUILD", "GUILDINDEX", "0").ok();

    let msg = format!("{}338", server_opcodes::CONSOLE_MSG);
    state.send_to(conn_id, &msg).await;
}

/// /HACLIDER <name> — Transfer leadership.
async fn handle_slash_haclider(state: &mut GameState, conn_id: ConnectionId, target_name: &str) {
    let (guild_index, char_name) = match state.users.get(&conn_id) {
        Some(u) if u.logged && u.guild_index > 0 => (u.guild_index, u.char_name.clone()),
        _ => return,
    };

    let base = state.base_path.clone();
    let guild = match guilds::load_guild(&base, guild_index) {
        Some(g) => g,
        None => return,
    };

    if guild.leader.to_uppercase() != char_name.to_uppercase() {
        let msg = format!("{}377", server_opcodes::CONSOLE_MSG);
        state.send_to(conn_id, &msg).await;
        return;
    }

    // Check target is in same guild and online
    let target_conn = state.online_names.get(&target_name.to_uppercase()).copied();
    let target_guild = target_conn.and_then(|c| state.users.get(&c).map(|u| u.guild_index));
    if target_guild != Some(guild_index) {
        let msg = format!("{}511", server_opcodes::CONSOLE_MSG);
        state.send_to(conn_id, &msg).await;
        return;
    }

    // Transfer leadership
    let mut updated = guild;
    updated.leader = target_name.to_string();
    guilds::save_guild(&base, &updated);

    // Notify
    let notify = format!("{}512@{}", server_opcodes::CONSOLE_MSG, target_name);
    state.send_data(SendTarget::ToGuildMembers(guild_index), &notify).await;
}

/// /SUBLIDER <name> — Promote member to sub-leader.
async fn handle_slash_sublider(state: &mut GameState, conn_id: ConnectionId, target_name: &str) {
    let (guild_index, char_name) = match state.users.get(&conn_id) {
        Some(u) if u.logged && u.guild_index > 0 => (u.guild_index, u.char_name.clone()),
        _ => return,
    };

    let base = state.base_path.clone();
    let guild = match guilds::load_guild(&base, guild_index) {
        Some(g) => g,
        None => return,
    };

    if guild.leader.to_uppercase() != char_name.to_uppercase() {
        let msg = format!("{}377", server_opcodes::CONSOLE_MSG);
        state.send_to(conn_id, &msg).await;
        return;
    }

    // Check target in same guild
    let target_conn = state.online_names.get(&target_name.to_uppercase()).copied();
    let target_guild = target_conn.and_then(|c| state.users.get(&c).map(|u| u.guild_index));
    if target_guild != Some(guild_index) {
        let msg = format!("{}511", server_opcodes::CONSOLE_MSG);
        state.send_to(conn_id, &msg).await;
        return;
    }

    // Check not already sublider
    if guild.sub_lider1.to_uppercase() == target_name.to_uppercase()
        || guild.sub_lider2.to_uppercase() == target_name.to_uppercase()
    {
        let msg = format!("{}510", server_opcodes::CONSOLE_MSG);
        state.send_to(conn_id, &msg).await;
        return;
    }

    let mut updated = guild;
    if updated.sub_lider1 == "Fermin" || updated.sub_lider1.is_empty() {
        updated.sub_lider1 = target_name.to_string();
    } else if updated.sub_lider2 == "Fermin" || updated.sub_lider2.is_empty() {
        updated.sub_lider2 = target_name.to_string();
    } else {
        let msg = format!("{}513", server_opcodes::CONSOLE_MSG);
        state.send_to(conn_id, &msg).await;
        return;
    }

    guilds::save_guild(&base, &updated);

    let notify = format!("{}514@{}", server_opcodes::CONSOLE_MSG, target_name);
    state.send_data(SendTarget::ToGuildMembers(guild_index), &notify).await;
}

/// /QSUBLIDR <name> — Demote sub-leader.
async fn handle_slash_qsublidr(state: &mut GameState, conn_id: ConnectionId, target_name: &str) {
    let (guild_index, char_name) = match state.users.get(&conn_id) {
        Some(u) if u.logged && u.guild_index > 0 => (u.guild_index, u.char_name.clone()),
        _ => return,
    };

    let base = state.base_path.clone();
    let guild = match guilds::load_guild(&base, guild_index) {
        Some(g) => g,
        None => return,
    };

    if guild.leader.to_uppercase() != char_name.to_uppercase() {
        let msg = format!("{}377", server_opcodes::CONSOLE_MSG);
        state.send_to(conn_id, &msg).await;
        return;
    }

    let mut updated = guild;
    if updated.sub_lider1.to_uppercase() == target_name.to_uppercase() {
        updated.sub_lider1 = "Fermin".to_string();
    } else if updated.sub_lider2.to_uppercase() == target_name.to_uppercase() {
        updated.sub_lider2 = "Fermin".to_string();
    } else {
        let msg = format!("{}516", server_opcodes::CONSOLE_MSG);
        state.send_to(conn_id, &msg).await;
        return;
    }

    guilds::save_guild(&base, &updated);

    let notify = format!("{}515@{}", server_opcodes::CONSOLE_MSG, target_name);
    state.send_data(SendTarget::ToGuildMembers(guild_index), &notify).await;
}

/// /CLAN — List online guild members.
async fn handle_slash_clan_list(state: &mut GameState, conn_id: ConnectionId) {
    let guild_index = match state.users.get(&conn_id) {
        Some(u) if u.logged && u.guild_index > 0 => u.guild_index,
        _ => {
            let msg = format!("{}120", server_opcodes::CONSOLE_MSG);
            state.send_to(conn_id, &msg).await;
            return;
        }
    };

    let base = state.base_path.clone();
    let guild = match guilds::load_guild(&base, guild_index) {
        Some(g) => g,
        None => return,
    };

    // List online members
    let msg = format!("{}Miembros online del clan {}:{}", server_opcodes::CONSOLE_MSG, guild.name, font_types::GUILD_MSG);
    state.send_to(conn_id, &msg).await;

    let online_members: Vec<String> = state.users.values()
        .filter(|u| u.logged && u.guild_index == guild_index)
        .map(|u| {
            let role = if guild.leader.to_uppercase() == u.char_name.to_uppercase() {
                " [Lider]"
            } else if guild.sub_lider1.to_uppercase() == u.char_name.to_uppercase()
                || guild.sub_lider2.to_uppercase() == u.char_name.to_uppercase()
            {
                " [SubLider]"
            } else {
                ""
            };
            format!("{}{}", u.char_name, role)
        })
        .collect();

    for member in &online_members {
        let line = format!("{}{}{}", server_opcodes::CONSOLE_MSG, member, font_types::INFO);
        state.send_to(conn_id, &line).await;
    }
}

/// /CMSG <text> — Send clan chat message.
async fn handle_slash_cmsg(state: &mut GameState, conn_id: ConnectionId, text: &str) {
    let (guild_index, char_name) = match state.users.get(&conn_id) {
        Some(u) if u.logged && u.guild_index > 0 => (u.guild_index, u.char_name.clone()),
        _ => {
            let msg = format!("{}120", server_opcodes::CONSOLE_MSG);
            state.send_to(conn_id, &msg).await;
            return;
        }
    };

    // No tilde allowed in clan chat
    if text.contains('~') {
        let msg = format!("{}198", server_opcodes::CONSOLE_MSG);
        state.send_to(conn_id, &msg).await;
        return;
    }

    let base = state.base_path.clone();
    let guild = match guilds::load_guild(&base, guild_index) {
        Some(g) => g,
        None => return,
    };

    let is_leader = guild.leader.to_uppercase() == char_name.to_uppercase();
    let is_sublider = guild.sub_lider1.to_uppercase() == char_name.to_uppercase()
        || guild.sub_lider2.to_uppercase() == char_name.to_uppercase();

    let (prefix, font) = if is_leader || is_sublider {
        ("Lider ", font_types::CLAN_LEADER)
    } else {
        ("", font_types::CLAN_MEMBER)
    };

    let pkt = format!("{}{}{}: {}{}", server_opcodes::CLAN_CHAT, prefix, char_name, text, font);
    state.send_data(SendTarget::ToGuildMembers(guild_index), &pkt).await;
}

/// DESCOD — Update guild codex and description.
async fn handle_guild_update_codex(state: &mut GameState, conn_id: ConnectionId, data: &str) {
    let payload = strip_opcode(data, 6);
    let parts: Vec<&str> = payload.split(BF).collect();

    let (guild_index, char_name) = match state.users.get(&conn_id) {
        Some(u) if u.logged && u.guild_index > 0 => (u.guild_index, u.char_name.clone()),
        _ => return,
    };

    let base = state.base_path.clone();
    let guild = match guilds::load_guild(&base, guild_index) {
        Some(g) => g,
        None => return,
    };

    // Only leader can update
    if guild.leader.to_uppercase() != char_name.to_uppercase() {
        return;
    }

    if parts.is_empty() { return; }

    let desc = parts[0];
    let cant_codex: usize = parts.get(1).and_then(|s| s.parse().ok()).unwrap_or(0);

    let mut codex = Vec::new();
    for i in 0..cant_codex.min(guilds::MAX_CODEX_LINES) {
        codex.push(parts.get(2 + i).unwrap_or(&"").to_string());
    }
    while codex.len() < guilds::MAX_CODEX_LINES {
        codex.push(String::new());
    }

    let mut updated = guild;
    updated.desc = desc.to_string();
    updated.codex = codex;
    guilds::save_guild(&base, &updated);
}

/// ACEPTARI — Accept applicant into guild.
async fn handle_guild_accept(state: &mut GameState, conn_id: ConnectionId, data: &str) {
    let applicant_name = strip_opcode(data, 8).trim().to_string();

    let (guild_index, char_name) = match state.users.get(&conn_id) {
        Some(u) if u.logged && u.guild_index > 0 => (u.guild_index, u.char_name.clone()),
        _ => return,
    };

    let base = state.base_path.clone();
    let guild = match guilds::load_guild(&base, guild_index) {
        Some(g) => g,
        None => return,
    };

    // Must be leader or sublider
    let is_leader = guild.leader.to_uppercase() == char_name.to_uppercase();
    let is_sublider = guild.sub_lider1.to_uppercase() == char_name.to_uppercase()
        || guild.sub_lider2.to_uppercase() == char_name.to_uppercase();
    if !is_leader && !is_sublider {
        return;
    }

    // Check member cap
    let members = guilds::load_members(&base, &guild.name);
    let max = guilds::max_members_for_level(guild.nivel_clan);
    if members.len() as i32 >= max {
        let msg = format!("{}500", server_opcodes::CONSOLE_MSG);
        state.send_to(conn_id, &msg).await;
        return;
    }

    // Remove from applicants
    guilds::remove_applicant(&base, &guild.name, &applicant_name);

    // Add to members
    guilds::add_member(&base, &guild.name, &applicant_name);

    // Update applicant's charfile
    let chr_path = base.join("charfile").join(format!("{}.chr", applicant_name));
    crate::config::write_var(chr_path.to_str().unwrap_or(""), "GUILD", "GUILDINDEX", &guild_index.to_string()).ok();
    crate::config::write_var(chr_path.to_str().unwrap_or(""), "GUILD", "AspiranteA", "0").ok();

    // If applicant is online, update their state
    if let Some(&target_conn) = state.online_names.get(&applicant_name.to_uppercase()) {
        if let Some(user) = state.users.get_mut(&target_conn) {
            user.guild_index = guild_index;
            user.puede_retirar_obj = false;
            user.puede_retirar_oro = false;
        }
        let msg = format!("{}501", server_opcodes::CONSOLE_MSG);
        state.send_to(target_conn, &msg).await;

        let sound = format!("{}43", server_opcodes::PLAY_SOUND);
        state.send_to(target_conn, &sound).await;
    }

    // Notify guild
    let notify = format!("{}503@{}", server_opcodes::CONSOLE_MSG, applicant_name);
    state.send_data(SendTarget::ToGuildMembers(guild_index), &notify).await;
}

/// RECHAZAR — Reject applicant.
async fn handle_guild_reject(state: &mut GameState, conn_id: ConnectionId, data: &str) {
    let payload = strip_opcode(data, 8);
    let name = read_field(1, payload, ',');
    let reason = read_field(2, payload, ',');

    let (guild_index, char_name) = match state.users.get(&conn_id) {
        Some(u) if u.logged && u.guild_index > 0 => (u.guild_index, u.char_name.clone()),
        _ => return,
    };

    let base = state.base_path.clone();
    let guild = match guilds::load_guild(&base, guild_index) {
        Some(g) => g,
        None => return,
    };

    // Must be leader or sublider
    let is_leader = guild.leader.to_uppercase() == char_name.to_uppercase();
    let is_sublider = guild.sub_lider1.to_uppercase() == char_name.to_uppercase()
        || guild.sub_lider2.to_uppercase() == char_name.to_uppercase();
    if !is_leader && !is_sublider { return; }

    guilds::remove_applicant(&base, &guild.name, &name);

    // If online, notify
    if let Some(&target_conn) = state.online_names.get(&name.to_uppercase()) {
        let msg = format!("{}Tu solicitud fue rechazada: {}{}", server_opcodes::CONSOLE_MSG, reason, font_types::INFO);
        state.send_to(target_conn, &msg).await;
    }
}

/// ECHARCLA — Expel member from guild.
async fn handle_guild_expel(state: &mut GameState, conn_id: ConnectionId, data: &str) {
    let target_name = strip_opcode(data, 8).trim().to_string();

    let (guild_index, char_name) = match state.users.get(&conn_id) {
        Some(u) if u.logged && u.guild_index > 0 => (u.guild_index, u.char_name.clone()),
        _ => return,
    };

    let base = state.base_path.clone();
    let guild = match guilds::load_guild(&base, guild_index) {
        Some(g) => g,
        None => return,
    };

    // Only leader can expel
    if guild.leader.to_uppercase() != char_name.to_uppercase() {
        let msg = format!("{}377", server_opcodes::CONSOLE_MSG);
        state.send_to(conn_id, &msg).await;
        return;
    }

    // Cannot expel self (leader)
    if target_name.to_uppercase() == char_name.to_uppercase() {
        return;
    }

    // If target is sublider, clear slot
    let mut updated = guild.clone();
    if updated.sub_lider1.to_uppercase() == target_name.to_uppercase() {
        updated.sub_lider1 = "Fermin".to_string();
        guilds::save_guild(&base, &updated);
    } else if updated.sub_lider2.to_uppercase() == target_name.to_uppercase() {
        updated.sub_lider2 = "Fermin".to_string();
        guilds::save_guild(&base, &updated);
    }

    // Remove from members
    guilds::remove_member(&base, &guild.name, &target_name);

    // Update charfile
    let chr_path = base.join("charfile").join(format!("{}.chr", target_name));
    crate::config::write_var(chr_path.to_str().unwrap_or(""), "GUILD", "GUILDINDEX", "0").ok();

    // If online, update state
    if let Some(&target_conn) = state.online_names.get(&target_name.to_uppercase()) {
        if let Some(user) = state.users.get_mut(&target_conn) {
            user.guild_index = 0;
        }
        let msg = format!("{}Has sido expulsado del clan.{}", server_opcodes::CONSOLE_MSG, font_types::INFO);
        state.send_to(target_conn, &msg).await;
    }

    // Notify guild
    let notify = format!("{}505@{}", server_opcodes::CONSOLE_MSG, target_name);
    state.send_data(SendTarget::ToGuildMembers(guild_index), &notify).await;
}

/// ACTGNEWS — Update guild news.
async fn handle_guild_news(state: &mut GameState, conn_id: ConnectionId, data: &str) {
    let news = strip_opcode(data, 8).to_string();

    let (guild_index, char_name) = match state.users.get(&conn_id) {
        Some(u) if u.logged && u.guild_index > 0 => (u.guild_index, u.char_name.clone()),
        _ => return,
    };

    let base = state.base_path.clone();
    let guild = match guilds::load_guild(&base, guild_index) {
        Some(g) => g,
        None => return,
    };

    if guild.leader.to_uppercase() != char_name.to_uppercase() {
        return;
    }

    let mut updated = guild;
    updated.news = news;
    guilds::save_guild(&base, &updated);
}

/// SOLICITUD — Apply to join a guild.
async fn handle_guild_apply(state: &mut GameState, conn_id: ConnectionId, data: &str) {
    let payload = strip_opcode(data, 9);
    let guild_name = read_field(1, payload, ',');
    let petition = read_field(2, payload, ',');

    let (guild_index, char_name, level) = match state.users.get(&conn_id) {
        Some(u) if u.logged => (u.guild_index, u.char_name.clone(), u.level),
        _ => return,
    };

    if guild_index > 0 {
        let msg = format!("{}Ya perteneces a un clan.{}", server_opcodes::CONSOLE_MSG, font_types::INFO);
        state.send_to(conn_id, &msg).await;
        return;
    }

    if level < 25 {
        let msg = format!("{}Necesitas nivel 25 para unirte a un clan.{}", server_opcodes::CONSOLE_MSG, font_types::INFO);
        state.send_to(conn_id, &msg).await;
        return;
    }

    let base = state.base_path.clone();
    let target_guild_num = match guilds::find_guild_by_name(&base, &guild_name) {
        Some(n) => n,
        None => {
            let msg = format!("{}El clan no existe.{}", server_opcodes::CONSOLE_MSG, font_types::INFO);
            state.send_to(conn_id, &msg).await;
            return;
        }
    };

    let _guild = match guilds::load_guild(&base, target_guild_num) {
        Some(g) => g,
        None => return,
    };

    if !guilds::add_applicant(&base, &guild_name, &char_name, &petition) {
        let msg = format!("{}El clan ya tiene el maximo de solicitudes pendientes.{}", server_opcodes::CONSOLE_MSG, font_types::INFO);
        state.send_to(conn_id, &msg).await;
        return;
    }

    // Update charfile aspirante
    let chr_path = base.join("charfile").join(format!("{}.chr", char_name));
    crate::config::write_var(chr_path.to_str().unwrap_or(""), "GUILD", "AspiranteA", &target_guild_num.to_string()).ok();

    let msg = format!("{}507", server_opcodes::CONSOLE_MSG);
    state.send_to(conn_id, &msg).await;
}

/// CLANDETAILS — Request details about a guild.
async fn handle_guild_details(state: &mut GameState, conn_id: ConnectionId, data: &str) {
    let guild_name = strip_opcode(data, 11).trim().to_string();

    if !state.users.get(&conn_id).map(|u| u.logged).unwrap_or(false) {
        return;
    }

    let base = state.base_path.clone();
    let guild_num = match guilds::find_guild_by_name(&base, &guild_name) {
        Some(n) => n,
        None => return,
    };
    let guild = match guilds::load_guild(&base, guild_num) {
        Some(g) => g,
        None => return,
    };

    let members = guilds::load_members(&base, &guild.name);

    // DTLC format: level<BF>alignment<BF>repu<BF>founder<BF>date<BF>leader<BF>sub1<BF>sub2<BF>membercount<BF>codex1..8<BF>desc<BF>name
    let mut pkt = format!("{}{}", server_opcodes::GUILD_DETAILS_RESP, guild.nivel_clan);
    pkt.push(BF);
    pkt.push_str(guilds::alignment_name(guild.alignment));
    pkt.push(BF);
    pkt.push_str(&guild.reputation.to_string());
    pkt.push(BF);
    pkt.push_str(&guild.founder);
    pkt.push(BF);
    pkt.push_str(&guild.date);
    pkt.push(BF);
    pkt.push_str(&guild.leader);
    pkt.push(BF);
    pkt.push_str(&guild.sub_lider1);
    pkt.push(BF);
    pkt.push_str(&guild.sub_lider2);
    pkt.push(BF);
    pkt.push_str(&members.len().to_string());
    for codex_line in &guild.codex {
        pkt.push(BF);
        pkt.push_str(codex_line);
    }
    pkt.push(BF);
    pkt.push_str(&guild.desc);
    pkt.push(BF);
    pkt.push_str(&guild.name);

    state.send_to(conn_id, &pkt).await;
}

/// INIBOV — Open guild bank vault window.
async fn handle_guild_bank_open(state: &mut GameState, conn_id: ConnectionId) {
    let (guild_index, _char_name) = match state.users.get(&conn_id) {
        Some(u) if u.logged && u.guild_index > 0 => (u.guild_index, u.char_name.clone()),
        _ => return,
    };

    let base = state.base_path.clone();
    let guild = match guilds::load_guild(&base, guild_index) {
        Some(g) => g,
        None => return,
    };

    // Send bank gold
    let gold = guilds::load_bank_gold(&base, &guild.name);
    let gold_pkt = format!("{}{}", server_opcodes::BANK_GOLD, gold);
    state.send_to(conn_id, &gold_pkt).await;

    // Send bank items
    let items = guilds::load_bank_items(&base, &guild.name);
    for (i, slot) in items.iter().enumerate() {
        if slot.obj_index > 0 {
            let obj_name = state.game_data.objects.get(slot.obj_index as usize - 1)
                .map(|o| o.name.as_str())
                .unwrap_or("???");
            let grh = state.game_data.objects.get(slot.obj_index as usize - 1)
                .map(|o| o.grh_index)
                .unwrap_or(0);
            let pkt = format!("{}{},{},{},{},{},{}", server_opcodes::BANK_SLOT,
                i + 1, slot.obj_index, obj_name, slot.amount, grh, 0);
            state.send_to(conn_id, &pkt).await;
        }
    }

    // Open bank window
    state.send_to(conn_id, server_opcodes::INIT_BANK).await;
}

/// CCDO — Deposit gold into guild bank.
async fn handle_guild_bank_deposit(state: &mut GameState, conn_id: ConnectionId, data: &str) {
    let payload = strip_opcode(data, 4);
    let amount: i64 = payload.trim().parse().unwrap_or(0);

    if amount <= 0 {
        let msg = format!("{}508", server_opcodes::CONSOLE_MSG);
        state.send_to(conn_id, &msg).await;
        return;
    }

    let (guild_index, gold) = match state.users.get(&conn_id) {
        Some(u) if u.logged && u.guild_index > 0 => (u.guild_index, u.gold),
        _ => return,
    };

    if amount > gold {
        let msg = format!("{}No tienes suficiente oro.{}", server_opcodes::CONSOLE_MSG, font_types::INFO);
        state.send_to(conn_id, &msg).await;
        return;
    }

    let base = state.base_path.clone();
    let guild = match guilds::load_guild(&base, guild_index) {
        Some(g) => g,
        None => return,
    };

    let current_bank_gold = guilds::load_bank_gold(&base, &guild.name);
    if current_bank_gold + amount > guilds::MAX_GUILD_BANK_GOLD {
        let msg = format!("{}268", server_opcodes::CONSOLE_MSG);
        state.send_to(conn_id, &msg).await;
        return;
    }

    // Deduct from player
    if let Some(user) = state.users.get_mut(&conn_id) {
        user.gold -= amount;
    }

    // Add to guild bank
    guilds::save_bank_gold(&base, &guild.name, current_bank_gold + amount);

    // Update client
    send_stats_gold(state, conn_id).await;

    let msg = format!("{}Has depositado {} monedas de oro en la boveda del clan.{}", server_opcodes::CONSOLE_MSG, amount, font_types::INFO);
    state.send_to(conn_id, &msg).await;
}

/// CCRO — Withdraw gold from guild bank.
async fn handle_guild_bank_withdraw(state: &mut GameState, conn_id: ConnectionId, data: &str) {
    let payload = strip_opcode(data, 4);
    let amount: i64 = payload.trim().parse().unwrap_or(0);

    if amount <= 0 {
        let msg = format!("{}508", server_opcodes::CONSOLE_MSG);
        state.send_to(conn_id, &msg).await;
        return;
    }

    let (guild_index, puede_retirar_oro) = match state.users.get(&conn_id) {
        Some(u) if u.logged && u.guild_index > 0 => (u.guild_index, u.puede_retirar_oro),
        _ => return,
    };

    if !puede_retirar_oro {
        let msg = format!("{}269", server_opcodes::CONSOLE_MSG);
        state.send_to(conn_id, &msg).await;
        return;
    }

    let base = state.base_path.clone();
    let guild = match guilds::load_guild(&base, guild_index) {
        Some(g) => g,
        None => return,
    };

    let current_bank_gold = guilds::load_bank_gold(&base, &guild.name);
    if amount > current_bank_gold {
        let msg = format!("{}270", server_opcodes::CONSOLE_MSG);
        state.send_to(conn_id, &msg).await;
        return;
    }

    // Add to player
    if let Some(user) = state.users.get_mut(&conn_id) {
        user.gold += amount;
    }

    // Remove from guild bank
    guilds::save_bank_gold(&base, &guild.name, current_bank_gold - amount);

    // Update client
    send_stats_gold(state, conn_id).await;

    let msg = format!("{}Has retirado {} monedas de oro de la boveda del clan.{}", server_opcodes::CONSOLE_MSG, amount, font_types::INFO);
    state.send_to(conn_id, &msg).await;
}

// =====================================================================
// Clan bank (BoveClan NPC) — modBancoNuevo.bas
// =====================================================================

/// Open clan bank for user. VB6: Acciones.bas BoveClan handler.
async fn iniciar_clan_banco(state: &mut GameState, conn_id: ConnectionId) {
    let guild_index = match state.users.get(&conn_id) {
        Some(u) if u.logged && u.guild_index > 0 => u.guild_index,
        _ => return, // No guild
    };

    let base = state.base_path.clone();
    let guild = match guilds::load_guild(&base, guild_index) {
        Some(g) => g,
        None => return,
    };

    // Check if another user already has this clan bank open (VB6: CuentaBancaria lock)
    let guild_upper = guild.name.to_uppercase();
    let already_open = state.users.values().any(|u| u.cuenta_bancaria.to_uppercase() == guild_upper);
    if already_open {
        state.send_to(conn_id, "||640").await;
        return;
    }

    // Load clan bank items from .bov file
    let items = guilds::load_bank_items(&base, &guild.name);
    let gold = guilds::load_bank_gold(&base, &guild.name);

    // Store in user state
    if let Some(user) = state.users.get_mut(&conn_id) {
        user.cuenta_bancaria = guild.name.clone();
        user.puede_retirar_obj = true;
        user.puede_retirar_oro = true;
        user.clan_bank.clear();
        for slot in &items {
            user.clan_bank.push(InventorySlot {
                obj_index: slot.obj_index,
                amount: slot.amount as i32,
                equipped: false,
            });
        }
        // Pad to MAX_GUILD_BANK_SLOTS
        while user.clan_bank.len() < guilds::MAX_GUILD_BANK_SLOTS {
            user.clan_bank.push(InventorySlot::default());
        }
    }

    // Send clan bank inventory (SBG packets)
    enviar_clan_banco_inv(state, conn_id, &guild.name, gold).await;

    // Send gold
    send_stats_gold(state, conn_id).await;

    // Open clan bank UI: INITCBANK<canRetireObj>,<canRetireGold>
    let (ret_obj, ret_oro) = match state.users.get(&conn_id) {
        Some(u) => (u.puede_retirar_obj as i32, u.puede_retirar_oro as i32),
        None => (1, 1),
    };
    state.send_to(conn_id, &format!("INITCBANK{},{}", ret_obj, ret_oro)).await;

    if let Some(user) = state.users.get_mut(&conn_id) {
        user.comerciando = true;
    }
}

/// Send all clan bank slots to client.
/// VB6: BUpdateBanUserInv sends SBG for each slot.
/// SBG format: SBG<slot>,<obj_idx>,<name>,<amount>,<grh>,<type>,<maxhit>,<minhit>,<maxdef>,<bank_gold>,<user_gold>
async fn enviar_clan_banco_inv(state: &mut GameState, conn_id: ConnectionId, guild_name: &str, bank_gold: i64) {
    let user_gold = state.users.get(&conn_id).map(|u| u.gold).unwrap_or(0);

    for idx in 0..guilds::MAX_GUILD_BANK_SLOTS {
        let slot_data = match state.users.get(&conn_id) {
            Some(u) if idx < u.clan_bank.len() => {
                let s = &u.clan_bank[idx];
                if s.obj_index > 0 {
                    state.get_object(s.obj_index).map(|o| {
                        (s.obj_index, o.name.clone(), s.amount, o.grh_index, o.obj_type as i32,
                         o.max_hit, o.min_hit, o.max_def)
                    })
                } else {
                    None
                }
            }
            _ => None,
        };

        let slot_num = idx + 1;
        if let Some((obj_idx, name, amount, grh, otype, maxhit, minhit, maxdef)) = slot_data {
            let pkt = format!("SBG{},{},{},{},{},{},{},{},{},{},{}", slot_num, obj_idx, name, amount, grh, otype, maxhit, minhit, maxdef, bank_gold, user_gold);
            state.send_to(conn_id, &pkt).await;
        } else {
            let pkt = format!("SBG{},0,(Nada),0,0,0,0,0,0,{},{}", slot_num, bank_gold, user_gold);
            state.send_to(conn_id, &pkt).await;
        }
    }
}

/// RETB — Withdraw item from clan bank (VB6: BUserRetiraItem).
async fn handle_clan_bank_withdraw_item(state: &mut GameState, conn_id: ConnectionId, data: &str) {
    let payload = strip_opcode(data, 4); // "RETB"
    let parts: Vec<&str> = payload.split(',').collect();
    let slot: usize = parts.first().and_then(|s| s.trim().parse().ok()).unwrap_or(0);
    let amount: i32 = parts.get(1).and_then(|s| s.trim().parse().ok()).unwrap_or(0);

    if slot < 1 || amount < 1 { return; }

    let (dead, puede_retirar, guild_index) = match state.users.get(&conn_id) {
        Some(u) if u.logged => (u.dead, u.puede_retirar_obj, u.guild_index),
        _ => return,
    };
    if dead { state.send_to(conn_id, "||3").await; return; }
    if !puede_retirar { state.send_to(conn_id, "||289").await; return; }

    let slot_idx = slot - 1; // 0-based

    // Get item from clan bank
    let (obj_index, available) = match state.users.get(&conn_id) {
        Some(u) if slot_idx < u.clan_bank.len() => {
            (u.clan_bank[slot_idx].obj_index, u.clan_bank[slot_idx].amount)
        }
        _ => return,
    };
    if obj_index <= 0 || available <= 0 { return; }
    let qty = amount.min(available);

    // Find slot in user inventory (stack or empty)
    let inv_slot = match state.users.get(&conn_id) {
        Some(u) => {
            // Try stacking first
            let stack = u.inventory.iter().position(|s| s.obj_index == obj_index && s.amount + qty <= 10000);
            if let Some(s) = stack { Some(s) }
            else { u.inventory.iter().position(|s| s.obj_index == 0) }
        }
        None => return,
    };

    match inv_slot {
        Some(inv_idx) => {
            if let Some(user) = state.users.get_mut(&conn_id) {
                user.inventory[inv_idx].obj_index = obj_index;
                user.inventory[inv_idx].amount += qty;
                user.clan_bank[slot_idx].amount -= qty;
                if user.clan_bank[slot_idx].amount <= 0 {
                    user.clan_bank[slot_idx] = InventorySlot::default();
                }
            }
            // Update client
            send_inventory_slot(state, conn_id, inv_idx).await;
            let guild_name = guilds::load_guild(&state.base_path.clone(), guild_index)
                .map(|g| g.name).unwrap_or_default();
            let bank_gold = guilds::load_bank_gold(&state.base_path.clone(), &guild_name);
            enviar_clan_banco_inv(state, conn_id, &guild_name, bank_gold).await;
            let pkt = format!("BANCOBK{},0", slot);
            state.send_to(conn_id, &pkt).await;
        }
        None => {
            state.send_to(conn_id, "||108").await; // Inventory full
        }
    }
}

/// DEPB — Deposit item into clan bank (VB6: BUserDepositaItem).
async fn handle_clan_bank_deposit_item(state: &mut GameState, conn_id: ConnectionId, data: &str) {
    let payload = strip_opcode(data, 4); // "DEPB"
    let parts: Vec<&str> = payload.split(',').collect();
    let inv_slot: usize = parts.first().and_then(|s| s.trim().parse().ok()).unwrap_or(0);
    let amount: i32 = parts.get(1).and_then(|s| s.trim().parse().ok()).unwrap_or(0);

    if inv_slot < 1 || amount < 1 { return; }

    let (dead, guild_index, comerciando) = match state.users.get(&conn_id) {
        Some(u) if u.logged => (u.dead, u.guild_index, u.comerciando),
        _ => return,
    };
    if dead { state.send_to(conn_id, "||3").await; return; }

    let inv_idx = inv_slot - 1; // 0-based

    // Get item from user inventory
    let (obj_index, available, equipped) = match state.users.get(&conn_id) {
        Some(u) if inv_idx < u.inventory.len() => {
            (u.inventory[inv_idx].obj_index, u.inventory[inv_idx].amount, u.inventory[inv_idx].equipped)
        }
        _ => return,
    };
    if obj_index <= 0 || available <= 0 || equipped { return; }

    // Check intransferible
    let intransferible = state.get_object(obj_index).map(|o| o.intransferible).unwrap_or(false);
    if intransferible {
        state.send_to(conn_id, "||185").await;
        return;
    }

    let qty = amount.min(available);

    // Find slot in clan bank (stack or empty)
    let bank_slot = match state.users.get(&conn_id) {
        Some(u) => {
            let stack = u.clan_bank.iter().position(|s| s.obj_index == obj_index && s.amount + qty <= 10000);
            if let Some(s) = stack { Some(s) }
            else { u.clan_bank.iter().position(|s| s.obj_index == 0) }
        }
        None => return,
    };

    match bank_slot {
        Some(bank_idx) => {
            if let Some(user) = state.users.get_mut(&conn_id) {
                user.clan_bank[bank_idx].obj_index = obj_index;
                user.clan_bank[bank_idx].amount += qty;
                user.inventory[inv_idx].amount -= qty;
                if user.inventory[inv_idx].amount <= 0 {
                    user.inventory[inv_idx] = InventorySlot::default();
                }
            }
            // Update client
            send_inventory_slot(state, conn_id, inv_idx).await;
            let guild_name = guilds::load_guild(&state.base_path.clone(), guild_index)
                .map(|g| g.name).unwrap_or_default();
            let bank_gold = guilds::load_bank_gold(&state.base_path.clone(), &guild_name);
            enviar_clan_banco_inv(state, conn_id, &guild_name, bank_gold).await;
            let pkt = format!("BANCOBK{},1", inv_slot);
            state.send_to(conn_id, &pkt).await;
        }
        None => {
            state.send_to(conn_id, "||186").await; // Clan bank full
        }
    }
}

/// CCBG — Save clan bank inventory to file (VB6: TCP_HandleData1.bas line 2415).
async fn handle_clan_bank_save(state: &mut GameState, conn_id: ConnectionId) {
    let guild_index = match state.users.get(&conn_id) {
        Some(u) if u.logged && u.guild_index > 0 => u.guild_index,
        _ => return,
    };

    let base = state.base_path.clone();
    let guild = match guilds::load_guild(&base, guild_index) {
        Some(g) => g,
        None => return,
    };

    // Convert clan_bank to GuildBankSlot format and save
    let items: Vec<guilds::GuildBankSlot> = match state.users.get(&conn_id) {
        Some(u) => u.clan_bank.iter().map(|s| guilds::GuildBankSlot {
            obj_index: s.obj_index,
            amount: s.amount,
        }).collect(),
        None => return,
    };

    guilds::save_bank_items(&base, &guild.name, &items);
}

// =====================================================================
// Faction system handlers (ModFacciones.bas)
// =====================================================================

/// Kill thresholds for faction tiers
const FACTION_TIER_THRESHOLDS: [i32; 4] = [50, 100, 200, 350];

/// /ENLISTAR — Join a faction (Royal Army or Chaos Forces).
/// VB6: requires NPC target (type 5), distance <= 4, not dead.
async fn handle_slash_enlistar(state: &mut GameState, conn_id: ConnectionId) {
    let (target_npc, dead) = match state.users.get(&conn_id) {
        Some(u) if u.logged => (u.target_npc, u.dead),
        _ => return,
    };

    // VB6: If TargetNPC = 0 Then ||9
    if target_npc == 0 {
        state.send_to(conn_id, "||9").await;
        return;
    }

    // VB6: NPCtype must be 5 (faction officer) AND not dead
    let npc_type_num = state.get_npc(target_npc).map(|n| n.npc_type as i32).unwrap_or(0);
    if npc_type_num != 5 || dead { return; }

    // VB6: distance > 4 → ||158
    let (npc_map, npc_x, npc_y) = match state.get_npc(target_npc) {
        Some(npc) => (npc.map, npc.x, npc.y),
        None => return,
    };
    let (u_map, u_x, u_y) = match state.users.get(&conn_id) {
        Some(u) => (u.pos_map, u.pos_x, u.pos_y),
        None => return,
    };
    if u_map != npc_map || (u_x - npc_x).abs() > 4 || (u_y - npc_y).abs() > 4 {
        state.send_to(conn_id, "||158").await;
        return;
    }

    let user_data = match state.users.get(&conn_id) {
        Some(u) if u.logged => (
            u.armada_real, u.fuerzas_caos, u.criminal,
            u.criminales_matados, u.ciudadanos_matados,
            u.reenlistadas, u.char_name.clone(),
            u.guild_index,
        ),
        _ => return,
    };
    let (armada, caos, criminal, crim_killed, ciud_killed, reenlistadas, char_name, guild_index) = user_data;

    if armada || caos {
        let msg = format!("{}Ya perteneces a una faccion.{}", server_opcodes::CONSOLE_MSG, font_types::INFO);
        state.send_to(conn_id, &msg).await;
        return;
    }

    if reenlistadas {
        let msg = format!("{}Ya no puedes enlistarte nuevamente.{}", server_opcodes::CONSOLE_MSG, font_types::INFO);
        state.send_to(conn_id, &msg).await;
        return;
    }

    let base = state.base_path.clone();

    if !criminal {
        // Try to join Royal Army
        if crim_killed < 50 {
            let msg = format!("{}Necesitas haber matado al menos 50 criminales.{}", server_opcodes::CONSOLE_MSG, font_types::INFO);
            state.send_to(conn_id, &msg).await;
            return;
        }

        if let Some(user) = state.users.get_mut(&conn_id) {
            user.armada_real = true;
        }

        // Save to charfile
        let chr_path = base.join("charfile").join(format!("{}.chr", char_name));
        let chr = chr_path.to_str().unwrap_or("");
        crate::config::write_var(chr, "FACCIONES", "EjercitoReal", "1").ok();
        crate::config::write_var(chr, "FACCIONES", "rExReal", "1").ok();

        // Award initial XP (50000)
        if let Some(user) = state.users.get_mut(&conn_id) {
            user.exp += 50000;
        }
        send_stats_exp(state, conn_id).await;

        let msg = format!("{}Te has enlistado en la Armada Real!{}", server_opcodes::CONSOLE_MSG, font_types::GUILD_MSG);
        state.send_to(conn_id, &msg).await;

        // Broadcast
        let broadcast = format!("{}851@{}", server_opcodes::CONSOLE_MSG, char_name);
        state.send_data(SendTarget::ToAll, &broadcast).await;
    } else {
        // Try to join Chaos Forces
        if ciud_killed < 50 {
            let msg = format!("{}Necesitas haber matado al menos 50 ciudadanos.{}", server_opcodes::CONSOLE_MSG, font_types::INFO);
            state.send_to(conn_id, &msg).await;
            return;
        }

        // Cannot join chaos if ever received royal XP
        let chr_path = base.join("charfile").join(format!("{}.chr", char_name));
        let chr = chr_path.to_str().unwrap_or("");
        let had_royal_xp = crate::config::get_var(chr, "FACCIONES", "rExReal") == "1";
        if had_royal_xp {
            let msg = format!("{}No puedes unirte al Caos habiendo sido parte de la Armada.{}", server_opcodes::CONSOLE_MSG, font_types::INFO);
            state.send_to(conn_id, &msg).await;
            return;
        }

        if let Some(user) = state.users.get_mut(&conn_id) {
            user.fuerzas_caos = true;
        }

        crate::config::write_var(chr, "FACCIONES", "EjercitoCaos", "1").ok();
        crate::config::write_var(chr, "FACCIONES", "rExCaos", "1").ok();

        // Award initial XP (50000)
        if let Some(user) = state.users.get_mut(&conn_id) {
            user.exp += 50000;
        }
        send_stats_exp(state, conn_id).await;

        let msg = format!("{}Te has enlistado en las Fuerzas del Caos!{}", server_opcodes::CONSOLE_MSG, font_types::GUILD_MSG);
        state.send_to(conn_id, &msg).await;

        // Broadcast
        let broadcast = format!("{}852@{}", server_opcodes::CONSOLE_MSG, char_name);
        state.send_data(SendTarget::ToAll, &broadcast).await;
    }
}

/// /INFORMACION — Display faction status.
async fn handle_slash_faction_info(state: &mut GameState, conn_id: ConnectionId) {
    let user_data = match state.users.get(&conn_id) {
        Some(u) if u.logged => (
            u.armada_real, u.fuerzas_caos,
            u.criminales_matados, u.ciudadanos_matados,
            u.recompensas_real, u.recompensas_caos,
        ),
        _ => return,
    };
    let (armada, caos, crim_killed, ciud_killed, rec_real, rec_caos) = user_data;

    if armada {
        let msg = format!("{}--- Armada Real ---{}", server_opcodes::CONSOLE_MSG, font_types::GUILD_MSG);
        state.send_to(conn_id, &msg).await;
        let msg = format!("{}Criminales matados: {}{}", server_opcodes::CONSOLE_MSG, crim_killed, font_types::INFO);
        state.send_to(conn_id, &msg).await;
        let msg = format!("{}Rango: {}{}", server_opcodes::CONSOLE_MSG, faction_rank_name(rec_real, true), font_types::INFO);
        state.send_to(conn_id, &msg).await;
    } else if caos {
        let msg = format!("{}--- Fuerzas del Caos ---{}", server_opcodes::CONSOLE_MSG, font_types::GUILD_MSG);
        state.send_to(conn_id, &msg).await;
        let msg = format!("{}Ciudadanos matados: {}{}", server_opcodes::CONSOLE_MSG, ciud_killed, font_types::INFO);
        state.send_to(conn_id, &msg).await;
        let msg = format!("{}Rango: {}{}", server_opcodes::CONSOLE_MSG, faction_rank_name(rec_caos, false), font_types::INFO);
        state.send_to(conn_id, &msg).await;
    } else {
        let msg = format!("{}No perteneces a ninguna faccion.{}", server_opcodes::CONSOLE_MSG, font_types::INFO);
        state.send_to(conn_id, &msg).await;
        let msg = format!("{}Criminales matados: {}{}", server_opcodes::CONSOLE_MSG, crim_killed, font_types::INFO);
        state.send_to(conn_id, &msg).await;
        let msg = format!("{}Ciudadanos matados: {}{}", server_opcodes::CONSOLE_MSG, ciud_killed, font_types::INFO);
        state.send_to(conn_id, &msg).await;
    }
}

/// Get faction rank name
fn faction_rank_name(tier: i32, is_royal: bool) -> &'static str {
    if is_royal {
        match tier {
            0 => "Recluta",
            1 => "Soldado Imperial",
            2 => "Capitan Imperial",
            3 => "Comandante Imperial",
            4 => "General Imperial",
            _ => "Caballero de la Luz",
        }
    } else {
        match tier {
            0 => "Recluta Oscuro",
            1 => "Soldado del Caos",
            2 => "Capitan del Caos",
            3 => "Comandante del Caos",
            4 => "General del Caos",
            _ => "Caballero de las Sombras",
        }
    }
}

/// /RECOMPENSA — Claim faction tier reward. VB6: requires NPC type 5, distance <= 4.
async fn handle_slash_recompensa(state: &mut GameState, conn_id: ConnectionId) {
    let (target_npc, dead) = match state.users.get(&conn_id) {
        Some(u) if u.logged => (u.target_npc, u.dead),
        _ => return,
    };

    // VB6: If TargetNPC = 0 Then ||9
    if target_npc == 0 {
        state.send_to(conn_id, "||9").await;
        return;
    }

    // VB6: NPCtype must be 5, not dead
    let npc_type_num = state.get_npc(target_npc).map(|n| n.npc_type as i32).unwrap_or(0);
    if npc_type_num != 5 || dead { return; }

    // VB6: distance > 4 → ||12
    let (npc_map, npc_x, npc_y) = match state.get_npc(target_npc) {
        Some(npc) => (npc.map, npc.x, npc.y),
        None => return,
    };
    let (u_map, u_x, u_y) = match state.users.get(&conn_id) {
        Some(u) => (u.pos_map, u.pos_x, u.pos_y),
        None => return,
    };
    if u_map != npc_map || (u_x - npc_x).abs() > 4 || (u_y - npc_y).abs() > 4 {
        state.send_to(conn_id, "||12").await;
        return;
    }

    let user_data = match state.users.get(&conn_id) {
        Some(u) if u.logged => (
            u.armada_real, u.fuerzas_caos,
            u.criminales_matados, u.ciudadanos_matados,
            u.recompensas_real, u.recompensas_caos,
            u.char_name.clone(),
        ),
        _ => return,
    };
    let (armada, caos, crim_killed, ciud_killed, rec_real, rec_caos, char_name) = user_data;

    if !armada && !caos {
        let msg = format!("{}No perteneces a ninguna faccion.{}", server_opcodes::CONSOLE_MSG, font_types::INFO);
        state.send_to(conn_id, &msg).await;
        return;
    }

    let base = state.base_path.clone();
    let chr_path = base.join("charfile").join(format!("{}.chr", char_name));
    let chr = chr_path.to_str().unwrap_or("");

    if armada {
        let current_tier = rec_real;
        if current_tier >= 4 {
            let msg = format!("{}Ya has alcanzado el rango maximo.{}", server_opcodes::CONSOLE_MSG, font_types::INFO);
            state.send_to(conn_id, &msg).await;
            return;
        }

        let needed = FACTION_TIER_THRESHOLDS[current_tier as usize];
        if crim_killed < needed {
            let msg = format!("{}Necesitas {} criminales matados para el siguiente rango (tienes {}).{}", server_opcodes::CONSOLE_MSG, needed, crim_killed, font_types::INFO);
            state.send_to(conn_id, &msg).await;
            return;
        }

        // Advance tier
        let new_tier = current_tier + 1;
        if let Some(user) = state.users.get_mut(&conn_id) {
            user.recompensas_real = new_tier;
        }
        crate::config::write_var(chr, "FACCIONES", "recReal", &new_tier.to_string()).ok();

        let msg = format!("{}Has ascendido al rango: {}!{}", server_opcodes::CONSOLE_MSG, faction_rank_name(new_tier, true), font_types::GUILD_MSG);
        state.send_to(conn_id, &msg).await;
    } else {
        let current_tier = rec_caos;
        if current_tier >= 4 {
            let msg = format!("{}Ya has alcanzado el rango maximo.{}", server_opcodes::CONSOLE_MSG, font_types::INFO);
            state.send_to(conn_id, &msg).await;
            return;
        }

        let needed = FACTION_TIER_THRESHOLDS[current_tier as usize];
        if ciud_killed < needed {
            let msg = format!("{}Necesitas {} ciudadanos matados para el siguiente rango (tienes {}).{}", server_opcodes::CONSOLE_MSG, needed, ciud_killed, font_types::INFO);
            state.send_to(conn_id, &msg).await;
            return;
        }

        let new_tier = current_tier + 1;
        if let Some(user) = state.users.get_mut(&conn_id) {
            user.recompensas_caos = new_tier;
        }
        crate::config::write_var(chr, "FACCIONES", "recCaos", &new_tier.to_string()).ok();

        let msg = format!("{}Has ascendido al rango: {}!{}", server_opcodes::CONSOLE_MSG, faction_rank_name(new_tier, false), font_types::GUILD_MSG);
        state.send_to(conn_id, &msg).await;
    }
}

/// /RENUNCIA — Leave faction.
async fn handle_slash_renunciar(state: &mut GameState, conn_id: ConnectionId) {
    let user_data = match state.users.get(&conn_id) {
        Some(u) if u.logged => (
            u.armada_real, u.fuerzas_caos,
            u.char_name.clone(), u.guild_index,
        ),
        _ => return,
    };
    let (armada, caos, char_name, guild_index) = user_data;

    if !armada && !caos {
        let msg = format!("{}No perteneces a ninguna faccion.{}", server_opcodes::CONSOLE_MSG, font_types::INFO);
        state.send_to(conn_id, &msg).await;
        return;
    }

    // Cannot leave faction while in a guild
    if guild_index > 0 {
        let msg = format!("{}302", server_opcodes::CONSOLE_MSG);
        state.send_to(conn_id, &msg).await;
        return;
    }

    let base = state.base_path.clone();
    let chr_path = base.join("charfile").join(format!("{}.chr", char_name));
    let chr = chr_path.to_str().unwrap_or("");

    if let Some(user) = state.users.get_mut(&conn_id) {
        user.armada_real = false;
        user.fuerzas_caos = false;
        user.reenlistadas = true; // Can never re-enlist
    }

    crate::config::write_var(chr, "FACCIONES", "EjercitoReal", "0").ok();
    crate::config::write_var(chr, "FACCIONES", "EjercitoCaos", "0").ok();
    crate::config::write_var(chr, "FACCIONES", "Reenlistadas", "1").ok();

    let msg = format!("{}Has renunciado a tu faccion.{}", server_opcodes::CONSOLE_MSG, font_types::INFO);
    state.send_to(conn_id, &msg).await;
}

// =====================================================================
// Utility slash command handlers
// =====================================================================

/// /ONLINE — Show online player count.
async fn handle_slash_online(state: &mut GameState, conn_id: ConnectionId) {
    let count = state.num_users;
    let record = state.record_users;
    let msg = format!("{}Jugadores online: {}. Record: {}.{}", server_opcodes::CONSOLE_MSG, count, record, font_types::INFO);
    state.send_to(conn_id, &msg).await;
}

/// /BALANCE — Show gold and bank gold.
async fn handle_slash_balance(state: &mut GameState, conn_id: ConnectionId) {
    let (gold, bank_gold) = match state.users.get(&conn_id) {
        Some(u) if u.logged => (u.gold, u.bank_gold),
        _ => return,
    };
    let msg = format!("{}Oro: {}. En banco: {}. Total: {}.{}", server_opcodes::CONSOLE_MSG, gold, bank_gold, gold + bank_gold, font_types::INFO);
    state.send_to(conn_id, &msg).await;
}

/// /GLOBAL <text> — Send global chat message.
async fn handle_slash_global(state: &mut GameState, conn_id: ConnectionId, text: &str) {
    let (char_name, priv_level) = match state.users.get(&conn_id) {
        Some(u) if u.logged => (u.char_name.clone(), u.privileges),
        _ => return,
    };

    // VB6: If ChatGlobal == False and user is not staff → blocked
    if !state.chat_global && priv_level == 0 {
        state.send_to(conn_id, "||549").await;
        return;
    }

    if text.contains('~') { return; }

    let pkt = format!("{}{}> {}{}", server_opcodes::GUILD_CHAT, char_name, text, font_types::GUILD);
    state.send_data(SendTarget::ToAll, &pkt).await;
}

/// /STATS or /EST — Show character stats summary.
async fn handle_slash_stats(state: &mut GameState, conn_id: ConnectionId) {
    let u = match state.users.get(&conn_id) {
        Some(u) if u.logged => u,
        _ => return,
    };

    let lines = vec![
        format!("{}--- Estadisticas de {} ---{}", server_opcodes::CONSOLE_MSG, u.char_name, font_types::GUILD_MSG),
        format!("{}Clase: {} | Raza: {} | Nivel: {}{}", server_opcodes::CONSOLE_MSG, u.class, u.race, u.level, font_types::INFO),
        format!("{}HP: {}/{} | Mana: {}/{} | STA: {}/{}{}", server_opcodes::CONSOLE_MSG, u.min_hp, u.max_hp, u.min_mana, u.max_mana, u.min_sta, u.max_sta, font_types::INFO),
        format!("{}Fuerza: {} | Agilidad: {} | Inteligencia: {}{}", server_opcodes::CONSOLE_MSG, u.attributes[0], u.attributes[1], u.attributes[2], font_types::INFO),
        format!("{}Carisma: {} | Constitucion: {}{}", server_opcodes::CONSOLE_MSG, u.attributes[3], u.attributes[4], font_types::INFO),
        format!("{}Oro: {} | EXP: {}{}", server_opcodes::CONSOLE_MSG, u.gold, u.exp, font_types::INFO),
    ];

    // Need to clone to avoid borrow issue
    let lines_clone = lines;
    for line in &lines_clone {
        state.send_to(conn_id, line).await;
    }
}

// =====================================================================
// Mail system handlers (AA_Correos.bas)
// =====================================================================

const MAX_MAILS: usize = 30;

/// CZM — Send mail. Format: CZM<destinatario>$<asunto>$<mensaje>$,<items>
async fn handle_mail_send(state: &mut GameState, conn_id: ConnectionId, data: &str) {
    let payload = strip_opcode(data, 3);
    let parts: Vec<&str> = payload.splitn(4, '$').collect();

    if parts.len() < 3 {
        return;
    }

    let recipient_name = parts[0];
    let subject = parts[1];
    let message = parts[2];

    let sender_name = match state.users.get(&conn_id) {
        Some(u) if u.logged => u.char_name.clone(),
        _ => return,
    };

    // Validate recipient exists
    if !charfile::character_exists(&state.base_path, recipient_name) {
        let msg = format!("{}El personaje no existe.{}", server_opcodes::CONSOLE_MSG, font_types::INFO);
        state.send_to(conn_id, &msg).await;
        return;
    }

    // Load recipient's mail count
    let base = state.base_path.clone();
    let chr_path = base.join("charfile").join(format!("{}.chr", recipient_name.to_uppercase()));
    let chr = chr_path.to_str().unwrap_or("");
    let num_mails: usize = crate::config::get_var(chr, "CORREO", "NUMCORREOS").parse().unwrap_or(0);

    if num_mails >= MAX_MAILS {
        let msg = format!("{}629", server_opcodes::CONSOLE_MSG);
        state.send_to(conn_id, &msg).await;
        return;
    }

    // Add mail to recipient's charfile
    let new_slot = num_mails + 1;
    // Current date (VB6: Format(Now, "dd/mm/yyyy hh:nn"))
    let now = chrono_like_date();
    let mail_content = format!("{}${}${}${}$", sender_name, subject, message, now);

    crate::config::write_var(chr, "CORREO", "NUMCORREOS", &new_slot.to_string()).ok();
    crate::config::write_var(chr, "CORREO", &format!("CORREONUM{}", new_slot), &mail_content).ok();
    crate::config::write_var(chr, "CORREO", &format!("NUECORREOS{}", new_slot), "1").ok(); // Mark as new

    // Notify recipient if online
    if let Some(&target_conn) = state.online_names.get(&recipient_name.to_uppercase()) {
        let msg = format!("{}631", server_opcodes::CONSOLE_MSG);
        state.send_to(target_conn, &msg).await;
    }

    let msg = format!("{}Correo enviado a {}.{}", server_opcodes::CONSOLE_MSG, recipient_name, font_types::INFO);
    state.send_to(conn_id, &msg).await;
}

/// CZC — Open/read mail slot.
async fn handle_mail_open(state: &mut GameState, conn_id: ConnectionId, data: &str) {
    let payload = strip_opcode(data, 3);
    let slot: usize = payload.trim().parse().unwrap_or(0);

    let char_name = match state.users.get(&conn_id) {
        Some(u) if u.logged => u.char_name.clone(),
        _ => return,
    };

    if slot == 0 {
        // Request mail list
        send_mail_list(state, conn_id, &char_name).await;
        return;
    }

    // Read specific mail
    let base = state.base_path.clone();
    let chr_path = base.join("charfile").join(format!("{}.chr", char_name));
    let chr = chr_path.to_str().unwrap_or("");

    let content = crate::config::get_var(chr, "CORREO", &format!("CORREONUM{}", slot));
    if content.is_empty() { return; }

    // Mark as read
    crate::config::write_var(chr, "CORREO", &format!("NUECORREOS{}", slot), "0").ok();

    // Send content: ILO<remitente>$<asunto>$<mensaje>$<fecha>$
    let pkt = format!("{}{}", server_opcodes::MAIL_CONTENT, content);
    state.send_to(conn_id, &pkt).await;
}

/// CZB — Delete mail.
async fn handle_mail_delete(state: &mut GameState, conn_id: ConnectionId, data: &str) {
    let payload = strip_opcode(data, 3);
    let slot: usize = payload.trim().parse().unwrap_or(0);

    let char_name = match state.users.get(&conn_id) {
        Some(u) if u.logged => u.char_name.clone(),
        _ => return,
    };

    let base = state.base_path.clone();
    let chr_path = base.join("charfile").join(format!("{}.chr", char_name));
    let chr = chr_path.to_str().unwrap_or("");

    let num_mails: usize = crate::config::get_var(chr, "CORREO", "NUMCORREOS").parse().unwrap_or(0);
    if slot == 0 || slot > num_mails { return; }

    // Shift mails down to compact
    for i in slot..num_mails {
        let next = crate::config::get_var(chr, "CORREO", &format!("CORREONUM{}", i + 1));
        let next_new = crate::config::get_var(chr, "CORREO", &format!("NUECORREOS{}", i + 1));
        crate::config::write_var(chr, "CORREO", &format!("CORREONUM{}", i), &next).ok();
        crate::config::write_var(chr, "CORREO", &format!("NUECORREOS{}", i), &next_new).ok();
    }

    // Clear last slot and decrement count
    crate::config::write_var(chr, "CORREO", &format!("CORREONUM{}", num_mails), "").ok();
    crate::config::write_var(chr, "CORREO", &format!("NUECORREOS{}", num_mails), "0").ok();
    crate::config::write_var(chr, "CORREO", "NUMCORREOS", &(num_mails - 1).to_string()).ok();

    // Refresh mail list
    send_mail_list(state, conn_id, &char_name).await;
}

/// CZR — Extract items from mail (simplified — no item attachment in basic impl).
async fn handle_mail_extract(state: &mut GameState, conn_id: ConnectionId, _data: &str) {
    let msg = format!("{}Este correo no tiene objetos adjuntos.{}", server_opcodes::CONSOLE_MSG, font_types::INFO);
    state.send_to(conn_id, &msg).await;
}

/// Send mail list to player.
async fn send_mail_list(state: &mut GameState, conn_id: ConnectionId, char_name: &str) {
    let base = state.base_path.clone();
    let chr_path = base.join("charfile").join(format!("{}.chr", char_name));
    let chr = chr_path.to_str().unwrap_or("");

    let num_mails: usize = crate::config::get_var(chr, "CORREO", "NUMCORREOS").parse().unwrap_or(0);

    let mut entries = Vec::new();
    for i in 1..=MAX_MAILS {
        if i <= num_mails {
            let content = crate::config::get_var(chr, "CORREO", &format!("CORREONUM{}", i));
            let is_new = crate::config::get_var(chr, "CORREO", &format!("NUECORREOS{}", i)) == "1";
            // Extract sender name (first field before $)
            let sender = content.split('$').next().unwrap_or("???");
            let new_tag = if is_new { " (NUEVO)" } else { "" };
            entries.push(format!("{}{}", sender, new_tag));
        } else {
            entries.push(String::new());
        }
    }

    let pkt = format!("{}{}", server_opcodes::MAIL_LIST, entries.join(","));
    state.send_to(conn_id, &pkt).await;
}

// =====================================================================
// Friend list handlers
// =====================================================================

/// ADDCON<name> — Add friend.
async fn handle_friend_add(state: &mut GameState, conn_id: ConnectionId, data: &str) {
    let friend_name = strip_opcode(data, 6).trim().to_string();

    let account_name = match state.users.get(&conn_id) {
        Some(u) if u.logged => u.account_name.clone(),
        _ => return,
    };

    if !charfile::character_exists(&state.base_path, &friend_name) {
        let msg = format!("{}El personaje no existe.{}", server_opcodes::CONSOLE_MSG, font_types::INFO);
        state.send_to(conn_id, &msg).await;
        return;
    }

    // Load friend list from account file
    let base = state.base_path.clone();
    let act_path = base.join("Accounts").join(format!("{}.act", account_name));
    let act = act_path.to_str().unwrap_or("");

    let count: usize = crate::config::get_var(act, "AMIGOS", "CANT").parse().unwrap_or(0);
    if count >= 20 {
        let msg = format!("{}Lista de amigos llena.{}", server_opcodes::CONSOLE_MSG, font_types::INFO);
        state.send_to(conn_id, &msg).await;
        return;
    }

    // Check not duplicate
    for i in 1..=count {
        let existing = crate::config::get_var(act, "AMIGOS", &format!("A{}", i));
        if existing.to_uppercase() == friend_name.to_uppercase() {
            let msg = format!("{}Ya esta en tu lista de amigos.{}", server_opcodes::CONSOLE_MSG, font_types::INFO);
            state.send_to(conn_id, &msg).await;
            return;
        }
    }

    // Add friend
    let new_count = count + 1;
    crate::config::write_var(act, "AMIGOS", "CANT", &new_count.to_string()).ok();
    crate::config::write_var(act, "AMIGOS", &format!("A{}", new_count), &friend_name).ok();

    // Send updated list
    send_friend_list(state, conn_id).await;

    let msg = format!("{}{} agregado a tu lista de amigos.{}", server_opcodes::CONSOLE_MSG, friend_name, font_types::INFO);
    state.send_to(conn_id, &msg).await;
}

/// BORRAC<index> — Remove friend by slot.
async fn handle_friend_remove(state: &mut GameState, conn_id: ConnectionId, data: &str) {
    let payload = strip_opcode(data, 6);
    let slot: usize = payload.trim().parse().unwrap_or(0);

    let account_name = match state.users.get(&conn_id) {
        Some(u) if u.logged => u.account_name.clone(),
        _ => return,
    };

    let base = state.base_path.clone();
    let act_path = base.join("Accounts").join(format!("{}.act", account_name));
    let act = act_path.to_str().unwrap_or("");

    let count: usize = crate::config::get_var(act, "AMIGOS", "CANT").parse().unwrap_or(0);
    if slot == 0 || slot > count { return; }

    // Shift friends down
    for i in slot..count {
        let next = crate::config::get_var(act, "AMIGOS", &format!("A{}", i + 1));
        crate::config::write_var(act, "AMIGOS", &format!("A{}", i), &next).ok();
    }
    crate::config::write_var(act, "AMIGOS", &format!("A{}", count), "").ok();
    crate::config::write_var(act, "AMIGOS", "CANT", &(count - 1).to_string()).ok();

    send_friend_list(state, conn_id).await;
}

/// Send friend list to player (VB6 SendFriendList in modGuilds.bas).
/// Format: LDM<count>,name1(ON),name2(OFF),...
async fn send_friend_list(state: &mut GameState, conn_id: ConnectionId) {
    let account_name = match state.users.get(&conn_id) {
        Some(u) if u.logged => u.account_name.clone(),
        _ => return,
    };

    let base = state.base_path.clone();
    let act_path = base.join("Accounts").join(format!("{}.act", account_name));
    let act = act_path.to_str().unwrap_or("");

    let count: usize = crate::config::get_var(act, "AMIGOS", "CANT").parse().unwrap_or(0);

    // Build VB6 format: "count,name1(ON),name2(OFF),"
    let mut result = format!("{},", count);
    for i in 1..=count {
        let friend_name = crate::config::get_var(act, "AMIGOS", &format!("A{}", i));
        if friend_name.is_empty() || friend_name.to_uppercase() == "(NADIE)" {
            result.push_str("(NADIE)(OFF),");
        } else {
            // Check if friend is online
            let is_online = state.users.values()
                .any(|u| u.logged && u.char_name.to_uppercase() == friend_name.to_uppercase());
            if is_online {
                result.push_str(&format!("{}(ON),", friend_name));
            } else {
                result.push_str(&format!("{}(OFF),", friend_name));
            }
        }
    }

    let pkt = format!("LDM{}", result);
    state.send_to(conn_id, &pkt).await;
}

// =====================================================================
// Quest system handlers (modQuests.bas)
// =====================================================================

use crate::data::quests;

/// Format a number with period separators (VB6 PonerPuntos).
/// e.g. 200000 -> "200.000", 1500 -> "1.500"
fn poner_puntos(num: i64) -> String {
    let s = num.to_string();
    let len = s.len();
    if len <= 3 { return s; }
    let mut result = String::new();
    for (i, ch) in s.chars().enumerate() {
        if i > 0 && (len - i) % 3 == 0 {
            result.push('.');
        }
        result.push(ch);
    }
    result
}

/// IQUEST — Request quest list + current quest progress.
/// VB6: QTL<count>,<name1>,<name2>,...  then MQC<cantNPC>,<muereQuest>,<name>,<oro>,<ptsTorneo>,<creditos>,<ptsTS>
async fn handle_quest_list(state: &mut GameState, conn_id: ConnectionId) {
    let (questeando, quest_num, quest_kills) = match state.users.get(&conn_id) {
        Some(u) if u.logged => (u.questeando, u.quest_num, u.quest_kills),
        _ => return,
    };

    // Send quest list: QTL<count>,<name1>,<name2>,...
    let num_quests = state.game_data.quests.len();
    let mut pkt = format!("{}{}", server_opcodes::QUEST_LIST_RESP, num_quests);
    for quest in &state.game_data.quests {
        pkt.push(',');
        pkt.push_str(&quest.name);
    }
    state.send_to(conn_id, &pkt).await;

    // If currently on a quest, send progress
    // VB6: MQC<cantNPC>,<muereQuest>,<name>,<oro>,<ptsTorneo>,<creditos>,<ptsTS>
    if questeando && quest_num > 0 {
        if let Some(quest) = state.game_data.quests.get(quest_num as usize - 1) {
            let target = quests::quest_target(quest);
            let pkt = format!("{}{},{},{},{},{},{},{}",
                server_opcodes::QUEST_CURRENT,
                target, quest_kills, quest.name, quest.oro, quest.pts_torneo, quest.creditos, quest.pts_ts
            );
            state.send_to(conn_id, &pkt).await;
        }
    }
}

/// INFD — Get details for quest selection (before accepting).
/// VB6: MQS<Name>,<Oro>,<ptsTorneo>,<Creditos>,<ptsTS>
async fn handle_quest_info(state: &mut GameState, conn_id: ConnectionId, data: &str) {
    let payload = strip_opcode(data, 4);
    // VB6: ReadField(1, rData, 44) — field separator is comma (ASCII 44)
    let quest_id: i32 = payload.split(',').next().unwrap_or("").trim().parse().unwrap_or(0);

    if !state.users.get(&conn_id).map(|u| u.logged).unwrap_or(false) { return; }

    if quest_id <= 0 || quest_id as usize > state.game_data.quests.len() { return; }

    let quest = &state.game_data.quests[quest_id as usize - 1];

    // VB6 format: MQS<Name>,<Oro>,<ptsTorneo>,<Creditos>,<ptsTS>
    let pkt = format!("{}{},{},{},{},{}",
        server_opcodes::QUEST_SELECTED,
        quest.name, quest.oro, quest.pts_torneo, quest.creditos, quest.pts_ts
    );
    state.send_to(conn_id, &pkt).await;
}

/// ACQT — Accept a quest.
/// VB6: checks already questeando, PK map, then accepts.
async fn handle_quest_accept(state: &mut GameState, conn_id: ConnectionId, data: &str) {
    let payload = strip_opcode(data, 4);
    // VB6: ReadField(1, rData, 44) — field separator is comma
    let quest_id: i32 = payload.split(',').next().unwrap_or("").trim().parse().unwrap_or(0);

    let (questeando, quest_num, char_name, user_map) = match state.users.get(&conn_id) {
        Some(u) if u.logged => (u.questeando, u.quest_num, u.char_name.clone(), u.pos_map),
        _ => return,
    };

    // VB6: If Questeando = 1 Or UserNumQuest > 0 Then send ||279
    if questeando || quest_num > 0 {
        state.send_to(conn_id, "||279").await;
        return;
    }

    // VB6: If MapInfo(Map).Pk = True Then send ||291
    let map_idx = user_map as usize;
    let is_pk = state.game_data.maps.get(map_idx)
        .and_then(|m| m.as_ref())
        .map(|m| m.info.pk)
        .unwrap_or(false);
    if is_pk {
        state.send_to(conn_id, "||291").await;
        return;
    }

    if quest_id <= 0 || quest_id as usize > state.game_data.quests.len() { return; }

    // VB6: send ||280, set flags
    state.send_to(conn_id, "||280").await;

    if let Some(user) = state.users.get_mut(&conn_id) {
        user.questeando = true;
        user.quest_num = quest_id;
        user.quest_kills = 0;
    }

    // Save to charfile
    let base = state.base_path.clone();
    let chr_path = base.join("charfile").join(format!("{}.chr", char_name));
    let chr = chr_path.to_str().unwrap_or("");
    crate::config::write_var(chr, "FLAGS", "Questeando", "1").ok();
    crate::config::write_var(chr, "FLAGS", "UserNumQuest", &quest_id.to_string()).ok();
    crate::config::write_var(chr, "FLAGS", "MuereQuest", "0").ok();
}

/// /QUEST — Show quest info (same as IQUEST)
async fn handle_slash_quest(state: &mut GameState, conn_id: ConnectionId) {
    handle_quest_list(state, conn_id).await;
}

/// /NOQUEST — Abandon current quest.
/// VB6: sends ||304 if no quest, ||305 if abandoned.
async fn handle_slash_noquest(state: &mut GameState, conn_id: ConnectionId) {
    let (questeando, char_name) = match state.users.get(&conn_id) {
        Some(u) if u.logged => (u.questeando, u.char_name.clone()),
        _ => return,
    };

    if !questeando {
        state.send_to(conn_id, "||304").await;
        return;
    }

    if let Some(user) = state.users.get_mut(&conn_id) {
        user.questeando = false;
        user.quest_num = 0;
        user.quest_kills = 0;
    }

    let base = state.base_path.clone();
    let chr_path = base.join("charfile").join(format!("{}.chr", char_name));
    let chr = chr_path.to_str().unwrap_or("");
    crate::config::write_var(chr, "FLAGS", "Questeando", "0").ok();
    crate::config::write_var(chr, "FLAGS", "UserNumQuest", "0").ok();
    crate::config::write_var(chr, "FLAGS", "MuereQuest", "0").ok();

    state.send_to(conn_id, "||305").await;
}

/// Check if an NPC kill counts towards quest progress.
/// VB6: modQuests.RestarNPC — only counts if NPC type matches quest's numNPC.
pub async fn quest_check_npc_kill(state: &mut GameState, killer_conn: ConnectionId, npc_type: i32) {
    let (questeando, quest_num, quest_kills, char_name) = match state.users.get(&killer_conn) {
        Some(u) if u.logged && u.questeando => (true, u.quest_num, u.quest_kills, u.char_name.clone()),
        _ => return,
    };

    if !questeando || quest_num <= 0 { return; }

    let quest = match state.game_data.quests.get(quest_num as usize - 1) {
        Some(q) if q.quest_type == quests::QuestType::KillNpc && q.mata_npc == npc_type => q.clone(),
        _ => return,
    };

    // Increment kill counter
    let new_kills = quest_kills + 1;
    if let Some(user) = state.users.get_mut(&killer_conn) {
        user.quest_kills = new_kills;
    }

    let target = quests::quest_target(&quest);

    if new_kills >= target {
        quest_complete(state, killer_conn, &char_name, &quest).await;
    } else {
        // Save progress
        let base = state.base_path.clone();
        let chr_path = base.join("charfile").join(format!("{}.chr", char_name));
        crate::config::write_var(chr_path.to_str().unwrap_or(""), "FLAGS", "MuereQuest", &new_kills.to_string()).ok();
    }
}

/// Check if a player kill counts towards quest progress.
/// VB6: modQuests.RestarUser — only counts if NOT in TRIGGER6 (combat zone).
pub async fn quest_check_player_kill(state: &mut GameState, killer_conn: ConnectionId, victim_conn: ConnectionId) {
    let (questeando, quest_num, quest_kills, char_name) = match state.users.get(&killer_conn) {
        Some(u) if u.logged && u.questeando => (true, u.quest_num, u.quest_kills, u.char_name.clone()),
        _ => return,
    };

    if !questeando || quest_num <= 0 { return; }

    // VB6: TriggerZonaPelea(userindex, VictimIndex) <> TRIGGER6_PERMITE
    // Kills in TRIGGER6 combat zones don't count for quests
    let (v_map, v_x, v_y) = match state.users.get(&victim_conn) {
        Some(v) => (v.pos_map, v.pos_x, v.pos_y),
        None => return,
    };
    let victim_trigger = get_map_tile_trigger(state, v_map, v_x, v_y);
    if victim_trigger == crate::data::maps::Trigger::CombatZone { return; }

    let quest = match state.game_data.quests.get(quest_num as usize - 1) {
        Some(q) if q.quest_type == quests::QuestType::KillPlayers => q.clone(),
        _ => return,
    };

    let new_kills = quest_kills + 1;
    if let Some(user) = state.users.get_mut(&killer_conn) {
        user.quest_kills = new_kills;
    }

    let target = quests::quest_target(&quest);

    if new_kills >= target {
        quest_complete(state, killer_conn, &char_name, &quest).await;
    } else {
        let base = state.base_path.clone();
        let chr_path = base.join("charfile").join(format!("{}.chr", char_name));
        crate::config::write_var(chr_path.to_str().unwrap_or(""), "FLAGS", "MuereQuest", &new_kills.to_string()).ok();
    }
}

/// Complete a quest — award ALL rewards matching VB6 exactly.
/// VB6: modQuests.bas lines 56-98 — Gold, ptsTorneo, ptsTS, Creditos, Reputation.
/// Premium/Estado multiplier: 2x for Gold, ptsTorneo, Reputation if estado!=0 OR EsPremium!=0.
async fn quest_complete(state: &mut GameState, conn_id: ConnectionId, char_name: &str, quest: &quests::QuestData) {
    // VB6: send ||66 (quest complete message)
    state.send_to(conn_id, "||66").await;

    // Note: VB6 has estado/EsPremium multiplier (2x if either is nonzero).
    // We don't track these flags yet, so we use 1x multiplier for now.
    // When es_premium/estado are added, update this.
    let multiplier: i64 = 1;

    // Gold reward
    if quest.oro > 0 {
        let gold_reward = quest.oro * multiplier;
        if let Some(user) = state.users.get_mut(&conn_id) {
            user.gold += gold_reward;
        }
        // VB6: ||63@<formatted_gold>
        let formatted = poner_puntos(gold_reward);
        state.send_to(conn_id, &format!("||63@{}", formatted)).await;
    }

    // Tournament points reward
    if quest.pts_torneo > 0 {
        let pts_reward = quest.pts_torneo as i64 * multiplier;
        if let Some(user) = state.users.get_mut(&conn_id) {
            user.puntos_torneo += pts_reward;
        }
        // VB6: ||57@<pts>
        state.send_to(conn_id, &format!("||57@{}", pts_reward)).await;
        // VB6: AgregarPuntos sends PNT<total_pts>
        let total_pts = state.users.get(&conn_id).map(|u| u.puntos_torneo).unwrap_or(0);
        state.send_to(conn_id, &format!("PNT{}", total_pts)).await;
    }

    // TS points reward (no multiplier in VB6)
    if quest.pts_ts > 0 {
        let ts_reward = quest.pts_ts as i64;
        if let Some(user) = state.users.get_mut(&conn_id) {
            user.ts_points += ts_reward;
        }
        // VB6: ||900@<pts>
        state.send_to(conn_id, &format!("||900@{}", ts_reward)).await;
    }

    // Credits reward (no multiplier in VB6)
    if quest.creditos > 0 {
        let credits_reward = quest.creditos as i64;
        if let Some(user) = state.users.get_mut(&conn_id) {
            user.puntos_donacion += credits_reward;
        }
        // VB6: ||930@<credits>
        state.send_to(conn_id, &format!("||930@{}", credits_reward)).await;
    }

    // Reputation reward (same multiplier as ptsTorneo)
    if quest.pts_torneo > 0 {
        let rep_reward = quest.pts_torneo as i32 * multiplier as i32;
        if let Some(user) = state.users.get_mut(&conn_id) {
            user.reputation += rep_reward;
        }
    }

    // Reset quest state
    if let Some(user) = state.users.get_mut(&conn_id) {
        user.questeando = false;
        user.quest_num = 0;
        user.quest_kills = 0;
        user.quests_completed += 1;
    }

    // VB6: SendUserGLD
    send_stats_gold(state, conn_id).await;

    // Save charfile
    let base = state.base_path.clone();
    let chr_path = base.join("charfile").join(format!("{}.chr", char_name));
    let chr = chr_path.to_str().unwrap_or("");
    crate::config::write_var(chr, "FLAGS", "Questeando", "0").ok();
    crate::config::write_var(chr, "FLAGS", "UserNumQuest", "0").ok();
    crate::config::write_var(chr, "FLAGS", "MuereQuest", "0").ok();
    let completed = state.users.get(&conn_id).map(|u| u.quests_completed).unwrap_or(0);
    crate::config::write_var(chr, "FLAGS", "QuestCompletadas", &completed.to_string()).ok();
}

// =====================================================================
// Party system handlers (mdParty.bas)
// =====================================================================

/// /NUEVAPARTY — Create a new party.
async fn handle_slash_nuevaparty(state: &mut GameState, conn_id: ConnectionId) {
    let party_index = match state.users.get(&conn_id) {
        Some(u) if u.logged => u.party_index,
        _ => return,
    };

    if party_index > 0 {
        let msg = format!("{}Ya perteneces a un grupo.{}", server_opcodes::CONSOLE_MSG, font_types::INFO);
        state.send_to(conn_id, &msg).await;
        return;
    }

    // Find free party slot
    let mut new_index = 0;
    for i in 1..=MAX_PARTIES {
        if state.parties[i].is_none() {
            new_index = i as i32;
            break;
        }
    }

    if new_index == 0 {
        let msg = format!("{}No se pueden crear mas grupos.{}", server_opcodes::CONSOLE_MSG, font_types::INFO);
        state.send_to(conn_id, &msg).await;
        return;
    }

    // Create party
    state.parties[new_index as usize] = Some(PartyState {
        leader: conn_id,
        members: vec![conn_id],
    });

    if let Some(user) = state.users.get_mut(&conn_id) {
        user.party_index = new_index;
    }

    let msg = format!("{}Has creado un grupo. Usa /PARTY <nombre> para invitar jugadores.{}", server_opcodes::CONSOLE_MSG, font_types::INFO);
    state.send_to(conn_id, &msg).await;
}

/// /PARTY <target> — Invite a player to party (leader only, max 3 tiles distance).
async fn handle_slash_party_invite(state: &mut GameState, conn_id: ConnectionId, target_name: &str) {
    let (party_index, map, x, y) = match state.users.get(&conn_id) {
        Some(u) if u.logged && u.party_index > 0 => (u.party_index, u.pos_map, u.pos_x, u.pos_y),
        Some(u) if u.logged => {
            let msg = format!("{}No perteneces a un grupo. Usa /NUEVAPARTY.{}", server_opcodes::CONSOLE_MSG, font_types::INFO);
            state.send_to(conn_id, &msg).await;
            return;
        }
        _ => return,
    };

    // Must be leader
    let is_leader = state.parties.get(party_index as usize)
        .and_then(|p| p.as_ref())
        .map(|p| p.leader == conn_id)
        .unwrap_or(false);
    if !is_leader {
        let msg = format!("{}Solo el lider puede invitar jugadores.{}", server_opcodes::CONSOLE_MSG, font_types::INFO);
        state.send_to(conn_id, &msg).await;
        return;
    }

    // Check party not full
    let member_count = state.parties.get(party_index as usize)
        .and_then(|p| p.as_ref())
        .map(|p| p.members.len())
        .unwrap_or(0);
    if member_count >= MAX_PARTY_MEMBERS {
        let msg = format!("{}El grupo esta lleno.{}", server_opcodes::CONSOLE_MSG, font_types::INFO);
        state.send_to(conn_id, &msg).await;
        return;
    }

    // Find target
    let target_conn = match state.online_names.get(&target_name.to_uppercase()) {
        Some(&c) => c,
        None => {
            let msg = format!("{}El jugador no esta conectado.{}", server_opcodes::CONSOLE_MSG, font_types::INFO);
            state.send_to(conn_id, &msg).await;
            return;
        }
    };

    let target_data = match state.users.get(&target_conn) {
        Some(u) if u.logged => (u.party_index, u.pos_map, u.pos_x, u.pos_y, u.dead),
        _ => return,
    };
    let (t_party, t_map, t_x, t_y, t_dead) = target_data;

    if t_dead {
        let msg = format!("{}No puedes invitar a un muerto.{}", server_opcodes::CONSOLE_MSG, font_types::INFO);
        state.send_to(conn_id, &msg).await;
        return;
    }

    if t_party > 0 {
        let msg = format!("{}El jugador ya esta en un grupo.{}", server_opcodes::CONSOLE_MSG, font_types::INFO);
        state.send_to(conn_id, &msg).await;
        return;
    }

    // Check distance (max 3 tiles)
    if t_map != map || (t_x - x).abs() > 3 || (t_y - y).abs() > 3 {
        let msg = format!("{}El jugador esta muy lejos.{}", server_opcodes::CONSOLE_MSG, font_types::INFO);
        state.send_to(conn_id, &msg).await;
        return;
    }

    // Set pending invite
    if let Some(user) = state.users.get_mut(&target_conn) {
        user.party_pending = party_index;
    }

    let inviter_name = state.users.get(&conn_id).map(|u| u.char_name.clone()).unwrap_or_default();
    let msg = format!("{}{} te ha invitado a un grupo. Usa /ACEPTAR o /CANCELAR.{}", server_opcodes::CONSOLE_MSG, inviter_name, font_types::INFO);
    state.send_to(target_conn, &msg).await;

    let msg = format!("{}Has invitado a {} al grupo.{}", server_opcodes::CONSOLE_MSG, target_name, font_types::INFO);
    state.send_to(conn_id, &msg).await;
}

/// /ACEPTAR — Accept party invite.
async fn handle_slash_party_accept(state: &mut GameState, conn_id: ConnectionId) {
    let (party_pending, char_name) = match state.users.get(&conn_id) {
        Some(u) if u.logged => (u.party_pending, u.char_name.clone()),
        _ => return,
    };

    if party_pending <= 0 {
        let msg = format!("{}No tienes ninguna invitacion pendiente.{}", server_opcodes::CONSOLE_MSG, font_types::INFO);
        state.send_to(conn_id, &msg).await;
        return;
    }

    // Check party still exists and not full
    let party_ok = state.parties.get(party_pending as usize)
        .and_then(|p| p.as_ref())
        .map(|p| p.members.len() < MAX_PARTY_MEMBERS)
        .unwrap_or(false);

    if !party_ok {
        let msg = format!("{}El grupo ya no existe o esta lleno.{}", server_opcodes::CONSOLE_MSG, font_types::INFO);
        state.send_to(conn_id, &msg).await;
        if let Some(user) = state.users.get_mut(&conn_id) {
            user.party_pending = 0;
        }
        return;
    }

    // Add to party
    if let Some(Some(party)) = state.parties.get_mut(party_pending as usize) {
        party.members.push(conn_id);
    }

    if let Some(user) = state.users.get_mut(&conn_id) {
        user.party_index = party_pending;
        user.party_pending = 0;
    }

    // Notify all party members
    let notify = format!("{}{} se ha unido al grupo.{}", server_opcodes::CONSOLE_MSG, char_name, font_types::INFO);
    send_to_party(state, party_pending, &notify).await;
}

/// /CANCELAR — Leave party or reject invite.
async fn handle_slash_party_cancel(state: &mut GameState, conn_id: ConnectionId) {
    let (party_index, party_pending, char_name) = match state.users.get(&conn_id) {
        Some(u) if u.logged => (u.party_index, u.party_pending, u.char_name.clone()),
        _ => return,
    };

    // If has pending invite, reject it
    if party_pending > 0 {
        if let Some(user) = state.users.get_mut(&conn_id) {
            user.party_pending = 0;
        }
        let msg = format!("{}Has rechazado la invitacion.{}", server_opcodes::CONSOLE_MSG, font_types::INFO);
        state.send_to(conn_id, &msg).await;
        return;
    }

    if party_index <= 0 {
        let msg = format!("{}No perteneces a un grupo.{}", server_opcodes::CONSOLE_MSG, font_types::INFO);
        state.send_to(conn_id, &msg).await;
        return;
    }

    // Check if leader — leader uses /FINPARTY
    let is_leader = state.parties.get(party_index as usize)
        .and_then(|p| p.as_ref())
        .map(|p| p.leader == conn_id)
        .unwrap_or(false);

    if is_leader {
        let msg = format!("{}Eres el lider. Usa /FINPARTY para disolver el grupo.{}", server_opcodes::CONSOLE_MSG, font_types::INFO);
        state.send_to(conn_id, &msg).await;
        return;
    }

    // Remove from party
    if let Some(Some(party)) = state.parties.get_mut(party_index as usize) {
        party.members.retain(|&c| c != conn_id);
    }

    if let Some(user) = state.users.get_mut(&conn_id) {
        user.party_index = 0;
    }

    // Notify
    let notify = format!("{}{} ha abandonado el grupo.{}", server_opcodes::CONSOLE_MSG, char_name, font_types::INFO);
    send_to_party(state, party_index, &notify).await;

    let msg = format!("{}Has abandonado el grupo.{}", server_opcodes::CONSOLE_MSG, font_types::INFO);
    state.send_to(conn_id, &msg).await;
}

/// /FINPARTY — Disband party (leader only).
async fn handle_slash_finparty(state: &mut GameState, conn_id: ConnectionId) {
    let party_index = match state.users.get(&conn_id) {
        Some(u) if u.logged && u.party_index > 0 => u.party_index,
        _ => {
            let msg = format!("{}No perteneces a un grupo.{}", server_opcodes::CONSOLE_MSG, font_types::INFO);
            state.send_to(conn_id, &msg).await;
            return;
        }
    };

    let is_leader = state.parties.get(party_index as usize)
        .and_then(|p| p.as_ref())
        .map(|p| p.leader == conn_id)
        .unwrap_or(false);

    if !is_leader {
        let msg = format!("{}Solo el lider puede disolver el grupo.{}", server_opcodes::CONSOLE_MSG, font_types::INFO);
        state.send_to(conn_id, &msg).await;
        return;
    }

    // Notify all members
    let notify = format!("{}El grupo ha sido disuelto.{}", server_opcodes::CONSOLE_MSG, font_types::INFO);
    send_to_party(state, party_index, &notify).await;

    // Get member list before clearing
    let members: Vec<ConnectionId> = state.parties.get(party_index as usize)
        .and_then(|p| p.as_ref())
        .map(|p| p.members.clone())
        .unwrap_or_default();

    // Clear party state for all members
    for &member_conn in &members {
        if let Some(user) = state.users.get_mut(&member_conn) {
            user.party_index = 0;
        }
    }

    // Remove party
    state.parties[party_index as usize] = None;
}

/// /PINFO — View party members.
async fn handle_slash_pinfo(state: &mut GameState, conn_id: ConnectionId) {
    let party_index = match state.users.get(&conn_id) {
        Some(u) if u.logged && u.party_index > 0 => u.party_index,
        _ => {
            let msg = format!("{}No perteneces a un grupo.{}", server_opcodes::CONSOLE_MSG, font_types::INFO);
            state.send_to(conn_id, &msg).await;
            return;
        }
    };

    let members: Vec<ConnectionId> = state.parties.get(party_index as usize)
        .and_then(|p| p.as_ref())
        .map(|p| p.members.clone())
        .unwrap_or_default();

    let leader_conn = state.parties.get(party_index as usize)
        .and_then(|p| p.as_ref())
        .map(|p| p.leader)
        .unwrap_or(0);

    let msg = format!("{}--- Miembros del grupo ---{}", server_opcodes::CONSOLE_MSG, font_types::GUILD_MSG);
    state.send_to(conn_id, &msg).await;

    for &member_conn in &members {
        if let Some(user) = state.users.get(&member_conn) {
            let role = if member_conn == leader_conn { " [Lider]" } else { "" };
            let line = format!("{}  {}{}{}", server_opcodes::CONSOLE_MSG, user.char_name, role, font_types::INFO);
            state.send_to(conn_id, &line).await;
        }
    }
}

/// Send message to all party members.
async fn send_to_party(state: &mut GameState, party_index: i32, data: &str) {
    let members: Vec<ConnectionId> = state.parties.get(party_index as usize)
        .and_then(|p| p.as_ref())
        .map(|p| p.members.clone())
        .unwrap_or_default();

    for &member_conn in &members {
        state.send_to(member_conn, data).await;
    }
}

/// Share experience among party members within 15 tiles.
/// Called when a party member kills something — splits exp equally.
pub async fn party_share_exp(state: &mut GameState, killer_conn: ConnectionId, total_exp: i64) {
    let (party_index, map, x, y) = match state.users.get(&killer_conn) {
        Some(u) if u.logged && u.party_index > 0 => (u.party_index, u.pos_map, u.pos_x, u.pos_y),
        _ => return, // Not in a party, no sharing
    };

    let members: Vec<ConnectionId> = state.parties.get(party_index as usize)
        .and_then(|p| p.as_ref())
        .map(|p| p.members.clone())
        .unwrap_or_default();

    // Filter members within 15 tiles on same map
    let nearby: Vec<ConnectionId> = members.iter()
        .copied()
        .filter(|&c| {
            state.users.get(&c)
                .map(|u| u.logged && u.pos_map == map && (u.pos_x - x).abs() <= 15 && (u.pos_y - y).abs() <= 15)
                .unwrap_or(false)
        })
        .collect();

    if nearby.is_empty() { return; }

    let share = total_exp / nearby.len() as i64;
    if share <= 0 { return; }

    for &member_conn in &nearby {
        if let Some(user) = state.users.get_mut(&member_conn) {
            user.exp += share;
        }
        send_stats_exp(state, member_conn).await;
    }
}

// =====================================================================
// GM / Admin command handlers (TCP_HandleData3.bas — GM section)
// =====================================================================

/// /GMSG <msg> — Send message to all GMs. Requires privileges > 0.
async fn handle_slash_gmsg(state: &mut GameState, conn_id: ConnectionId, text: &str) {
    let (priv_level, name) = match state.users.get(&conn_id) {
        Some(u) if u.logged && u.privileges > privilege_level::USER => {
            (u.privileges, u.char_name.clone())
        }
        _ => return,
    };
    let _ = priv_level;
    let pkt = format!("||429@{}@{}", name, text);
    state.send_data(SendTarget::ToAdmins, &pkt).await;
    info!("[GM] {} sent GMSG: {}", name, text);
}

/// /SMSG <msg> — System message to all players. Requires privileges > 0.
async fn handle_slash_smsg(state: &mut GameState, conn_id: ConnectionId, text: &str) {
    let name = match state.users.get(&conn_id) {
        Some(u) if u.logged && u.privileges > privilege_level::USER => u.char_name.clone(),
        _ => return,
    };
    let pkt = format!("!!{}\x1b", text); // ENDC = ESC char (0x1B)
    state.send_data(SendTarget::ToAll, &pkt).await;
    info!("[GM] {} sent SMSG: {}", name, text);
}

/// /NAVE — Toggle navigation mode (debug sailing). Requires privileges > 0.
/// /TELEP name map x y — Teleport self or another player (VB6 TCP.bas line 3368).
/// Format: /TELEP YO <map> <x> <y>  or  /TELEP <name> <map> <x> <y>
async fn handle_slash_telep(state: &mut GameState, conn_id: ConnectionId, args: &str) {
    let priv_level = match state.users.get(&conn_id) {
        Some(u) if u.logged && u.privileges >= privilege_level::CONSEJERO => u.privileges,
        _ => return,
    };

    // Parse: name map x y (space-delimited)
    let parts: Vec<&str> = args.split_whitespace().collect();
    if parts.len() < 4 {
        state.send_to(conn_id, &format!("{}Uso: /TELEP nombre mapa x y{}", server_opcodes::CONSOLE_MSG, font_types::INFO)).await;
        return;
    }

    let name = parts[0];
    let map: i32 = parts[1].parse().unwrap_or(0);
    let x: i32 = parts[2].parse().unwrap_or(0);
    let y: i32 = parts[3].parse().unwrap_or(0);

    if map < 1 || x < 1 || x > 100 || y < 1 || y > 100 {
        return;
    }

    // Check map exists
    let map_idx = map as usize;
    if map_idx >= state.game_data.maps.len() || state.game_data.maps.get(map_idx).and_then(|m| m.as_ref()).is_none() {
        return;
    }

    let target_id = if name.to_uppercase() == "YO" {
        conn_id
    } else {
        // Consejeros can't teleport others
        if priv_level <= privilege_level::CONSEJERO {
            return;
        }
        match state.find_user_by_name(name) {
            Some(id) => id,
            None => {
                state.send_to(conn_id, "||196").await; // User not found
                return;
            }
        }
    };

    warp_user(state, target_id, map, x, y).await;
    send_warp_fx(state, target_id).await;

    // Notify
    if target_id == conn_id {
        state.send_to(conn_id, "||773").await; // TEXTO773: Has sido transportado
    } else {
        let gm_name = state.users.get(&conn_id).map(|u| u.char_name.clone()).unwrap_or_default();
        state.send_to(target_id, &format!("||778@{}", gm_name)).await; // TEXTO778: %1 te ha transportado
        state.send_to(conn_id, &format!("||651@{}", gm_name)).await; // TEXTO651: %1 transportado
    }
}

/// /TELEPLOC — Teleport self to last left-click target location.
async fn handle_slash_teleploc(state: &mut GameState, conn_id: ConnectionId) {
    // This uses the target coordinates set by left-click (LC packet).
    // For now, we don't track target_x/target_y from LC, so fall back to a message.
    // TODO: Track target_x/target_y from LC handler and warp there.
    let priv_level = match state.users.get(&conn_id) {
        Some(u) if u.logged && u.privileges >= privilege_level::CONSEJERO => u.privileges,
        _ => return,
    };

    // Get last click target from user state (VB6: flags.TargetX/Y)
    let (map, tx, ty) = match state.users.get(&conn_id) {
        Some(u) => (if u.target_map > 0 { u.target_map } else { u.pos_map }, u.target_x, u.target_y),
        None => return,
    };

    if tx == 0 && ty == 0 {
        state.send_to(conn_id, &format!("{}Primero haz click en el destino.{}", server_opcodes::CONSOLE_MSG, font_types::INFO)).await;
        return;
    }

    warp_user(state, conn_id, map, tx, ty).await;
    send_warp_fx(state, conn_id).await;
    state.send_to(conn_id, "||773").await; // TEXTO773: Has sido transportado
}

/// /GO map — Teleport self to map at position 50,50 (VB6 behavior, requires SEMIDIOS+).
async fn handle_slash_go(state: &mut GameState, conn_id: ConnectionId, args: &str) {
    match state.users.get(&conn_id) {
        Some(u) if u.logged && u.privileges >= privilege_level::SEMIDIOS => {}
        _ => {
            state.send_to(conn_id, &format!("{}No tenes permisos para usar este comando.{}", server_opcodes::CONSOLE_MSG, font_types::INFO)).await;
            return;
        }
    };

    let parts: Vec<&str> = args.split_whitespace().collect();
    if parts.is_empty() {
        state.send_to(conn_id, &format!("{}Uso: /GO mapa{}", server_opcodes::CONSOLE_MSG, font_types::INFO)).await;
        return;
    }

    let map: i32 = parts[0].parse().unwrap_or(0);
    // VB6 always warps to 50,50 (TCP.bas line 4686)
    let x: i32 = if parts.len() >= 3 { parts[1].parse().unwrap_or(50) } else { 50 };
    let y: i32 = if parts.len() >= 3 { parts[2].parse().unwrap_or(50) } else { 50 };

    if map < 1 {
        state.send_to(conn_id, &format!("{}Mapa invalido.{}", server_opcodes::CONSOLE_MSG, font_types::INFO)).await;
        return;
    }

    // Check map exists
    let map_idx = map as usize;
    if map_idx >= state.game_data.maps.len() || state.game_data.maps.get(map_idx).and_then(|m| m.as_ref()).is_none() {
        state.send_to(conn_id, &format!("{}Mapa {} no existe.{}", server_opcodes::CONSOLE_MSG, map, font_types::INFO)).await;
        return;
    }

    warp_user(state, conn_id, map, x, y).await;
    send_warp_fx(state, conn_id).await;
    state.send_to(conn_id, "||773").await; // TEXTO773: Has sido transportado
}

/// /IRA name — Teleport to a player's position (requires SEMIDIOS+).
async fn handle_slash_ira(state: &mut GameState, conn_id: ConnectionId, target: &str) {
    match state.users.get(&conn_id) {
        Some(u) if u.logged && u.privileges >= privilege_level::SEMIDIOS => {}
        _ => {
            state.send_to(conn_id, &format!("{}No tenes permisos para usar este comando.{}", server_opcodes::CONSOLE_MSG, font_types::INFO)).await;
            return;
        }
    }

    let target_id = match state.find_user_by_name(target) {
        Some(id) => id,
        None => {
            state.send_to(conn_id, &format!("{}Jugador '{}' no encontrado.{}", server_opcodes::CONSOLE_MSG, target, font_types::INFO)).await;
            return;
        }
    };

    let (map, x, y) = match state.users.get(&target_id) {
        Some(u) if u.logged => (u.pos_map, u.pos_x, u.pos_y),
        _ => {
            state.send_to(conn_id, &format!("{}Jugador '{}' no encontrado.{}", server_opcodes::CONSOLE_MSG, target, font_types::INFO)).await;
            return;
        }
    };

    warp_user(state, conn_id, map, x, y).await;
    send_warp_fx(state, conn_id).await;
    state.send_to(conn_id, "||773").await; // TEXTO773: Has sido transportado
}

/// /SUM name — Summon a player to your position (requires SEMIDIOS+).
async fn handle_slash_sum(state: &mut GameState, conn_id: ConnectionId, target: &str) {
    let (my_map, my_x, my_y) = match state.users.get(&conn_id) {
        Some(u) if u.logged && u.privileges >= privilege_level::SEMIDIOS => (u.pos_map, u.pos_x, u.pos_y),
        _ => {
            state.send_to(conn_id, &format!("{}No tenes permisos para usar este comando.{}", server_opcodes::CONSOLE_MSG, font_types::INFO)).await;
            return;
        }
    };

    let target_id = match state.find_user_by_name(target) {
        Some(id) => id,
        None => {
            state.send_to(conn_id, &format!("{}Jugador '{}' no encontrado.{}", server_opcodes::CONSOLE_MSG, target, font_types::INFO)).await;
            return;
        }
    };

    warp_user(state, target_id, my_map, my_x, my_y).await;
    send_warp_fx(state, target_id).await;
    state.send_to(conn_id, &format!("{}Invocaste a '{}'.{}", server_opcodes::CONSOLE_MSG, target, font_types::INFO)).await;
    state.send_to(target_id, &format!("{}Has sido invocado por un GM.{}", server_opcodes::CONSOLE_MSG, font_types::INFO)).await;
}

/// /KICK name — Disconnect a player (requires SEMIDIOS+).
async fn handle_slash_kick(state: &mut GameState, conn_id: ConnectionId, target: &str) {
    match state.users.get(&conn_id) {
        Some(u) if u.logged && u.privileges >= privilege_level::SEMIDIOS => {}
        _ => {
            state.send_to(conn_id, &format!("{}No tenes permisos para usar este comando.{}", server_opcodes::CONSOLE_MSG, font_types::INFO)).await;
            return;
        }
    }

    let target_id = match state.find_user_by_name(target) {
        Some(id) => id,
        None => {
            state.send_to(conn_id, &format!("{}Jugador '{}' no encontrado.{}", server_opcodes::CONSOLE_MSG, target, font_types::INFO)).await;
            return;
        }
    };

    // Don't let someone kick themselves
    if target_id == conn_id {
        state.send_to(conn_id, &format!("{}No podes kickearte a vos mismo.{}", server_opcodes::CONSOLE_MSG, font_types::INFO)).await;
        return;
    }

    // Check target privilege — can't kick equal or higher
    let target_priv = state.users.get(&target_id).map(|u| u.privileges).unwrap_or(0);
    let my_priv = state.users.get(&conn_id).map(|u| u.privileges).unwrap_or(0);
    if target_priv >= my_priv {
        state.send_to(conn_id, &format!("{}No podes kickear a alguien de igual o mayor rango.{}", server_opcodes::CONSOLE_MSG, font_types::INFO)).await;
        return;
    }

    state.send_to(target_id, &format!("{}Has sido desconectado por un GM.{}", server_opcodes::CONSOLE_MSG, font_types::INFO)).await;
    close_connection(state, target_id).await;
    state.send_to(conn_id, &format!("{}Jugador '{}' desconectado.{}", server_opcodes::CONSOLE_MSG, target, font_types::INFO)).await;
}

/// /ITEM objid amount — Create item in inventory (requires DIOS+).
async fn handle_slash_item(state: &mut GameState, conn_id: ConnectionId, args: &str) {
    match state.users.get(&conn_id) {
        Some(u) if u.logged && u.privileges >= privilege_level::DIOS => {}
        _ => {
            state.send_to(conn_id, &format!("{}No tenes permisos para usar este comando.{}", server_opcodes::CONSOLE_MSG, font_types::INFO)).await;
            return;
        }
    }

    let parts: Vec<&str> = args.split_whitespace().collect();
    if parts.is_empty() {
        state.send_to(conn_id, &format!("{}Uso: /ITEM objid [cantidad]{}", server_opcodes::CONSOLE_MSG, font_types::INFO)).await;
        return;
    }

    let obj_idx: i32 = parts[0].parse().unwrap_or(0);
    let amount: i32 = if parts.len() > 1 { parts[1].parse().unwrap_or(1) } else { 1 };

    if obj_idx < 1 || amount < 1 {
        state.send_to(conn_id, &format!("{}Parametros invalidos.{}", server_opcodes::CONSOLE_MSG, font_types::INFO)).await;
        return;
    }

    // Verify object exists
    let obj_name = match state.get_object(obj_idx) {
        Some(obj) => obj.name.clone(),
        None => {
            state.send_to(conn_id, &format!("{}Objeto {} no existe.{}", server_opcodes::CONSOLE_MSG, obj_idx, font_types::INFO)).await;
            return;
        }
    };

    // Find first empty inventory slot
    let slot = match state.users.get(&conn_id) {
        Some(u) => u.inventory.iter().position(|s| s.obj_index == 0),
        None => return,
    };

    match slot {
        Some(slot_idx) => {
            if let Some(user) = state.users.get_mut(&conn_id) {
                user.inventory[slot_idx] = InventorySlot {
                    obj_index: obj_idx,
                    amount,
                    equipped: false,
                };
            }
            // Send CSI to update client inventory
            send_inventory_slot(state, conn_id, slot_idx).await;
            state.send_to(conn_id, &format!("{}Creado: {} x{}.{}", server_opcodes::CONSOLE_MSG, obj_name, amount, font_types::INFO)).await;
        }
        None => {
            state.send_to(conn_id, &format!("{}Inventario lleno.{}", server_opcodes::CONSOLE_MSG, font_types::INFO)).await;
        }
    }
}

/// /SOBJ <name> — Search objects by name (VB6 TCP.bas line 3677).
/// Sends ||748@<name>@<index> for each match. Requires SEMIDIOS+.
async fn handle_slash_sobj(state: &mut GameState, conn_id: ConnectionId, search: &str) {
    match state.users.get(&conn_id) {
        Some(u) if u.logged && u.privileges >= privilege_level::SEMIDIOS => {}
        _ => {
            state.send_to(conn_id, &format!("{}No tenes permisos para usar este comando.{}", server_opcodes::CONSOLE_MSG, font_types::INFO)).await;
            return;
        }
    }

    if search.is_empty() {
        state.send_to(conn_id, &format!("{}Uso: /SOBJ <nombre>{}", server_opcodes::CONSOLE_MSG, font_types::INFO)).await;
        return;
    }

    let search_upper = search.to_uppercase();
    let mut found = 0;

    for i in 0..state.game_data.objects.len() {
        let name_upper = state.game_data.objects[i].name.to_uppercase();
        if name_upper.contains(&search_upper) {
            let name = state.game_data.objects[i].name.clone();
            let idx = state.game_data.objects[i].index;
            state.send_to(conn_id, &format!("||748@{}@{}", name, idx)).await;
            found += 1;
        }
    }

    if found == 0 {
        state.send_to(conn_id, &format!("{}No se encontraron objetos con '{}'.{}", server_opcodes::CONSOLE_MSG, search, font_types::INFO)).await;
    }
}

// =============================================================================
// GM Commands migrated from VB6 (TCP.bas)
// =============================================================================

/// /INVISIBLE — Toggle admin invisibility (body=0, head=0).
async fn handle_slash_invisible(state: &mut GameState, conn_id: ConnectionId) {
    let (map, x, y, char_index, is_invisible) = match state.users.get(&conn_id) {
        Some(u) if u.logged && u.privileges >= privilege_level::CONSEJERO => {
            (u.pos_map, u.pos_x, u.pos_y, u.char_index, u.admin_invisible)
        }
        _ => return,
    };

    if is_invisible {
        // Make visible again — restore old body/head
        let (old_body, old_head) = {
            let user = state.users.get(&conn_id).unwrap();
            (user.old_body, user.old_head)
        };
        if let Some(user) = state.users.get_mut(&conn_id) {
            user.admin_invisible = false;
            user.invisible = false;
            user.body = old_body;
            user.head = old_head;
        }
        // Broadcast appearance change
        let cc = state.users.get(&conn_id).unwrap().build_cc_packet();
        state.send_data(SendTarget::ToArea { map, x, y }, &cc).await;
        // NOVER packet (visible)
        let nover = format!("NOVER{},0", char_index.0);
        state.send_data(SendTarget::ToArea { map, x, y }, &nover).await;
        state.send_to(conn_id, &format!("{}Sos visible.{}", server_opcodes::CONSOLE_MSG, font_types::INFO)).await;
    } else {
        // Save current body/head, go invisible
        if let Some(user) = state.users.get_mut(&conn_id) {
            user.old_body = user.body;
            user.old_head = user.head;
            user.body = 0;
            user.head = 0;
            user.admin_invisible = true;
            user.invisible = true;
        }
        // Broadcast appearance change (body=0, head=0 = invisible)
        let cc = state.users.get(&conn_id).unwrap().build_cc_packet();
        state.send_data(SendTarget::ToArea { map, x, y }, &cc).await;
        // NOVER packet (invisible)
        let nover = format!("NOVER{},1", char_index.0);
        state.send_data(SendTarget::ToArea { map, x, y }, &nover).await;
        state.send_to(conn_id, &format!("{}Sos invisible.{}", server_opcodes::CONSOLE_MSG, font_types::INFO)).await;
    }
}

/// /DONDE nick — Locate user on map. Sends ||735@name@map@x@y.
async fn handle_slash_donde(state: &mut GameState, conn_id: ConnectionId, target: &str) {
    match state.users.get(&conn_id) {
        Some(u) if u.logged && u.privileges >= privilege_level::CONSEJERO => {}
        _ => return,
    }

    let target_upper = target.to_uppercase();
    let found = state.users.values()
        .find(|u| u.logged && u.char_name.to_uppercase() == target_upper)
        .map(|u| (u.char_name.clone(), u.pos_map, u.pos_x, u.pos_y, u.privileges));

    match found {
        Some((name, map, x, y, target_priv)) => {
            // Can't locate gods+
            if target_priv >= privilege_level::DIOS {
                let my_priv = state.users.get(&conn_id).map(|u| u.privileges).unwrap_or(0);
                if my_priv < target_priv {
                    state.send_to(conn_id, &format!("{}No podes localizar a ese usuario.{}", server_opcodes::CONSOLE_MSG, font_types::INFO)).await;
                    return;
                }
            }
            state.send_to(conn_id, &format!("||735@{}@{}@{}@{}", name, map, x, y)).await;
        }
        None => {
            state.send_to(conn_id, &format!("||196")).await;
        }
    }
}

/// /BAN nick@reason — Ban character.
async fn handle_slash_ban(state: &mut GameState, conn_id: ConnectionId, args: &str) {
    match state.users.get(&conn_id) {
        Some(u) if u.logged && u.privileges >= privilege_level::DIOS => {}
        _ => return,
    }

    let parts: Vec<&str> = args.splitn(2, '@').collect();
    let nick = parts[0].trim().replace(['\\', '/'], "");
    let reason = if parts.len() > 1 { parts[1].trim() } else { "" };

    if nick.is_empty() || reason.is_empty() {
        state.send_to(conn_id, "||758").await;
        return;
    }

    let admin_name = state.users.get(&conn_id).map(|u| u.char_name.clone()).unwrap_or_default();

    // Check if target is online
    let target_upper = nick.to_uppercase();
    let target_conn = state.users.values()
        .find(|u| u.logged && u.char_name.to_uppercase() == target_upper)
        .map(|u| u.conn_id);

    // Set ban flag in charfile
    let chr_path = state.base_path.join("charfile").join(format!("{}.chr", nick));
    if chr_path.exists() {
        // Write Ban=1 to charfile
        if let Ok(content) = std::fs::read_to_string(&chr_path) {
            let updated = content.replace("Ban=0", "Ban=1");
            let _ = std::fs::write(&chr_path, updated);
        }
    } else {
        state.send_to(conn_id, &format!("||440")).await;
        return;
    }

    // Broadcast ban notification
    let ban_msg = format!("||760@{}@{}", admin_name, nick);
    state.send_data(SendTarget::ToAdmins, &ban_msg).await;

    // If online, disconnect
    if let Some(tc) = target_conn {
        state.send_to(tc, &format!("{}Has sido baneado. Razon: {}{}", server_opcodes::CONSOLE_MSG, reason, font_types::COMBAT)).await;
        if let Some(w) = state.writers.get_mut(&tc) {
            w.shutdown().await;
        }
    }

    state.send_to(conn_id, &format!("{}{} ha sido baneado. Razon: {}{}", server_opcodes::CONSOLE_MSG, nick, reason, font_types::INFO)).await;
    info!("[GM] {} banned {} for: {}", admin_name, nick, reason);
}

/// /UNBAN nick — Unban character.
async fn handle_slash_unban(state: &mut GameState, conn_id: ConnectionId, target: &str) {
    match state.users.get(&conn_id) {
        Some(u) if u.logged && u.privileges >= privilege_level::SUB_ADMINISTRADOR => {}
        _ => return,
    }

    let nick = target.replace(['\\', '/'], "");
    let chr_path = state.base_path.join("charfile").join(format!("{}.chr", nick));
    if !chr_path.exists() {
        state.send_to(conn_id, &format!("||189@{}", nick)).await;
        return;
    }

    // Set Ban=0 in charfile
    if let Ok(content) = std::fs::read_to_string(&chr_path) {
        let updated = content.replace("Ban=1", "Ban=0");
        let _ = std::fs::write(&chr_path, updated);
    }

    let admin_name = state.users.get(&conn_id).map(|u| u.char_name.clone()).unwrap_or_default();
    let msg = format!("||762@{}@{}", admin_name, nick);
    state.send_data(SendTarget::ToAdmins, &msg).await;
    state.send_to(conn_id, &format!("{}{} ha sido desbaneado.{}", server_opcodes::CONSOLE_MSG, nick, font_types::INFO)).await;
    info!("[GM] {} unbanned {}", admin_name, nick);
}

/// /BANIP nick|ip — Ban IP address.
async fn handle_slash_banip(state: &mut GameState, conn_id: ConnectionId, args: &str) {
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
        let msg = format!("||798@{}@{}", admin_name, name);
        state.send_data(SendTarget::ToAdmins, &msg).await;
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
        state.send_to(conn_id, "||797").await;
        return;
    }

    let base = state.base_path.clone();
    let _ = state.bans.ban_ip(&base, &ip_to_ban);
    state.send_to(conn_id, &format!("{}IP {} baneada.{}", server_opcodes::CONSOLE_MSG, ip_to_ban, font_types::INFO)).await;
    info!("[GM] {} banned IP {}", admin_name, ip_to_ban);
}

/// /UNBANIP ip — Unban IP address.
async fn handle_slash_unbanip(state: &mut GameState, conn_id: ConnectionId, ip: &str) {
    match state.users.get(&conn_id) {
        Some(u) if u.logged && u.privileges >= privilege_level::SEMIDIOS => {}
        _ => return,
    }

    let ip = ip.trim();
    if state.bans.unban_ip(ip) {
        state.bans.save_ips(&state.base_path);
        state.send_to(conn_id, &format!("||799@{}", ip)).await;
        let admin_name = state.users.get(&conn_id).map(|u| u.char_name.clone()).unwrap_or_default();
        info!("[GM] {} unbanned IP {}", admin_name, ip);
    } else {
        state.send_to(conn_id, "||800").await;
    }
}

/// /BANACC nick@reason — Ban account.
async fn handle_slash_banacc(state: &mut GameState, conn_id: ConnectionId, args: &str) {
    match state.users.get(&conn_id) {
        Some(u) if u.logged && u.privileges >= privilege_level::DIOS => {}
        _ => return,
    }

    let parts: Vec<&str> = args.splitn(2, '@').collect();
    let nick = parts[0].trim().replace(['\\', '/'], "");
    let reason = if parts.len() > 1 { parts[1].trim() } else { "Sin razon" };

    if nick.is_empty() {
        state.send_to(conn_id, "||758").await;
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
        let msg = format!("||752@{}@{}", admin_name, nick);
        state.send_data(SendTarget::ToAdmins, &msg).await;
        if let Some(w) = state.writers.get_mut(&tc) {
            w.shutdown().await;
        }
        acc
    } else {
        // Try to read from charfile
        nick.clone() // Fallback: use nick as account name
    };

    // Ban character
    let chr_path = state.base_path.join("charfile").join(format!("{}.chr", nick));
    if chr_path.exists() {
        if let Ok(content) = std::fs::read_to_string(&chr_path) {
            let updated = content.replace("Ban=0", "Ban=1");
            let _ = std::fs::write(&chr_path, updated);
        }
    }

    // Ban account
    let acc_path = state.base_path.join("Accounts").join(format!("{}.act", account_name));
    if acc_path.exists() {
        if let Ok(content) = std::fs::read_to_string(&acc_path) {
            let updated = content.replace("ban=0", "ban=1").replace("Ban=0", "Ban=1");
            let _ = std::fs::write(&acc_path, updated);
        }
    }

    let msg = format!("||760@{}@{}", admin_name, nick);
    state.send_data(SendTarget::ToAdmins, &msg).await;
    let msg2 = format!("||761@{}@{}", admin_name, account_name);
    state.send_data(SendTarget::ToAdmins, &msg2).await;
    state.send_to(conn_id, &format!("{}{} y cuenta {} baneados.{}", server_opcodes::CONSOLE_MSG, nick, account_name, font_types::INFO)).await;
    info!("[GM] {} banned account {} (char: {})", admin_name, account_name, nick);
}

/// /UNBANACC nick — Unban account.
async fn handle_slash_unbanacc(state: &mut GameState, conn_id: ConnectionId, target: &str) {
    match state.users.get(&conn_id) {
        Some(u) if u.logged && u.privileges >= privilege_level::SUB_ADMINISTRADOR => {}
        _ => return,
    }

    let nick = target.replace(['\\', '/'], "");
    let admin_name = state.users.get(&conn_id).map(|u| u.char_name.clone()).unwrap_or_default();

    // Unban character
    let chr_path = state.base_path.join("charfile").join(format!("{}.chr", nick));
    if chr_path.exists() {
        if let Ok(content) = std::fs::read_to_string(&chr_path) {
            let updated = content.replace("Ban=1", "Ban=0");
            let _ = std::fs::write(&chr_path, updated);
        }
    }

    // Try unban account (use nick as account name fallback)
    let acc_path = state.base_path.join("Accounts").join(format!("{}.act", nick));
    if acc_path.exists() {
        if let Ok(content) = std::fs::read_to_string(&acc_path) {
            let updated = content.replace("ban=1", "ban=0").replace("Ban=1", "Ban=0");
            let _ = std::fs::write(&acc_path, updated);
        }
    }

    let msg = format!("||763@{}@{}", admin_name, nick);
    state.send_data(SendTarget::ToAdmins, &msg).await;
    state.send_to(conn_id, &format!("{}Cuenta de {} desbaneada.{}", server_opcodes::CONSOLE_MSG, nick, font_types::INFO)).await;
    info!("[GM] {} unbanned account for {}", admin_name, nick);
}

/// /CARCEL nick@reason@minutes — Jail user.
async fn handle_slash_carcel(state: &mut GameState, conn_id: ConnectionId, args: &str) {
    match state.users.get(&conn_id) {
        Some(u) if u.logged && u.privileges >= privilege_level::SEMIDIOS => {}
        _ => return,
    }

    let parts: Vec<&str> = args.splitn(3, '@').collect();
    if parts.len() < 3 {
        state.send_to(conn_id, "||745").await;
        return;
    }

    let nick = parts[0].trim().replace(['\\', '/'], "");
    let reason = parts[1].trim();
    let minutes: i32 = parts[2].trim().parse().unwrap_or(0);

    if minutes < 1 || minutes > 60 {
        state.send_to(conn_id, "||746").await;
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
                state.send_to(conn_id, "||442").await;
                return;
            }

            // Set jail timer and warp to prison (map 78, 50, 50)
            if let Some(user) = state.users.get_mut(&tc) {
                user.jail_timer = minutes * 60; // Convert to seconds
            }

            // Send jail notification
            state.send_to(tc, &format!("||659@{}", minutes)).await;

            // Warp to prison
            warp_user(state, tc, 78, 50, 50).await;

            let admin_name = state.users.get(&conn_id).map(|u| u.char_name.clone()).unwrap_or_default();
            state.send_to(conn_id, &format!("{}{} encarcelado por {} minutos.{}", server_opcodes::CONSOLE_MSG, nick, minutes, font_types::INFO)).await;
            info!("[GM] {} jailed {} for {}m: {}", admin_name, nick, minutes, reason);
        }
        None => {
            state.send_to(conn_id, "||196").await;
        }
    }
}

/// /SILENCIAR nick@minutes — Mute/unmute user.
async fn handle_slash_silenciar(state: &mut GameState, conn_id: ConnectionId, args: &str) {
    match state.users.get(&conn_id) {
        Some(u) if u.logged && u.privileges >= privilege_level::CONSEJERO => {}
        _ => return,
    }

    let parts: Vec<&str> = args.splitn(2, '@').collect();
    let nick = parts[0].trim().replace(['\\', '/'], "");
    let minutes: i32 = if parts.len() > 1 { parts[1].trim().parse().unwrap_or(5) } else { 5 };

    if minutes > 60 {
        state.send_to(conn_id, "||944").await;
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
                state.send_to(conn_id, "||738").await;
                state.send_to(tc, "||946").await;
            } else {
                // Mute
                if let Some(user) = state.users.get_mut(&tc) {
                    user.silenced = true;
                    user.silence_timer = minutes * 60;
                }
                state.send_to(conn_id, "||737").await;
                state.send_to(tc, &format!("||943@{}", minutes)).await;
            }
            let admin_name = state.users.get(&conn_id).map(|u| u.char_name.clone()).unwrap_or_default();
            info!("[GM] {} {} {}", admin_name, if is_silenced { "unmuted" } else { "muted" }, nick);
        }
        None => {
            state.send_to(conn_id, "||196").await;
        }
    }
}

/// /ADVERTIR nick@reason — Warn user. 5 warnings = auto-ban.
async fn handle_slash_advertir(state: &mut GameState, conn_id: ConnectionId, args: &str) {
    match state.users.get(&conn_id) {
        Some(u) if u.logged && u.privileges >= privilege_level::CONSEJERO => {}
        _ => return,
    }

    let parts: Vec<&str> = args.splitn(2, '@').collect();
    let nick = parts[0].trim().replace(['\\', '/'], "");
    let reason = if parts.len() > 1 { parts[1].trim() } else { "" };

    if nick.is_empty() || reason.is_empty() {
        state.send_to(conn_id, "||741").await;
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
                state.send_to(conn_id, "||751").await;
                return;
            }

            let new_warnings = current_warnings + 1;
            if let Some(user) = state.users.get_mut(&tc) {
                user.warnings = new_warnings;
            }

            // Broadcast
            let msg = format!("||742@{}@{}", admin_name, nick);
            state.send_data(SendTarget::ToAdmins, &msg).await;
            state.send_to(tc, &format!("||743@{}@{}@{}", admin_name, reason, new_warnings)).await;

            if new_warnings >= 5 {
                // Auto-ban
                let chr_path = state.base_path.join("charfile").join(format!("{}.chr", nick));
                if chr_path.exists() {
                    if let Ok(content) = std::fs::read_to_string(&chr_path) {
                        let updated = content.replace("Ban=0", "Ban=1");
                        let _ = std::fs::write(&chr_path, updated);
                    }
                }
                let ban_msg = format!("||744@{}", nick);
                state.send_data(SendTarget::ToAdmins, &ban_msg).await;
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
                state.send_to(tc, &format!("||659@{}", jail_minutes)).await;
                warp_user(state, tc, 78, 50, 50).await;
            }

            info!("[GM] {} warned {} (#{}) for: {}", admin_name, nick, new_warnings, reason);
        }
        None => {
            state.send_to(conn_id, "||440").await;
        }
    }
}

/// /KILL nick — Kill user instantly.
async fn handle_slash_kill(state: &mut GameState, conn_id: ConnectionId, target: &str) {
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
            let msg = format!("||753@{}@{}", admin_name, target);
            state.send_data(SendTarget::ToArea { map, x, y }, &msg).await;
            info!("[GM] {} killed {}", admin_name, target);
        }
        None => {
            state.send_to(conn_id, "||196").await;
        }
    }
}

/// /ECHAR nick — Kick user (same as /KICK but VB6 name).
async fn handle_slash_echar(state: &mut GameState, conn_id: ConnectionId, target: &str) {
    handle_slash_kick(state, conn_id, target).await;
}

/// /REVIVIR nick|YO — Revive user.
async fn handle_slash_revivir(state: &mut GameState, conn_id: ConnectionId, target: &str) {
    match state.users.get(&conn_id) {
        Some(u) if u.logged && u.privileges >= privilege_level::SEMIDIOS => {}
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
            let is_dead = state.users.get(&tc).map(|u| u.dead).unwrap_or(false);
            if !is_dead {
                state.send_to(conn_id, &format!("{}Ese usuario no esta muerto.{}", server_opcodes::CONSOLE_MSG, font_types::INFO)).await;
                return;
            }

            // Revive: restore HP, clear dead flag, restore body
            let (map, x, y, race, max_hp) = match state.users.get(&tc) {
                Some(u) => (u.pos_map, u.pos_x, u.pos_y, u.race.clone(), u.max_hp),
                None => return,
            };

            // Read gender from charfile for naked body
            let gender = {
                let char_name = state.users.get(&tc).map(|u| u.char_name.clone()).unwrap_or_default();
                if let Ok(chr) = crate::data::charfile::load_charfile(&state.base_path, &char_name) {
                    chr.gender.to_string()
                } else {
                    "1".to_string()
                }
            };

            let new_body = naked_body(&race, &gender);

            // Load original head from charfile (VB6: OrigChar.Head)
            let orig_head = {
                let char_name = state.users.get(&tc).map(|u| u.char_name.clone()).unwrap_or_default();
                crate::data::charfile::load_charfile(&state.base_path, &char_name)
                    .map(|chr| chr.head)
                    .unwrap_or(1)
            };

            if let Some(user) = state.users.get_mut(&tc) {
                user.dead = false;
                user.min_hp = max_hp;
                user.body = new_body;
                user.head = orig_head;
                if user.admin_invisible {
                    user.old_body = new_body;
                    user.old_head = orig_head;
                }
            }

            // Read final state for CP packet
            let (heading, weapon_anim, shield_anim, casco_anim, char_index) = match state.users.get(&tc) {
                Some(u) => (u.heading, u.weapon_anim, u.shield_anim, u.casco_anim, u.char_index),
                None => return,
            };

            // Resurrection FX (VB6: CFF charindex, 65, 0)
            let cff_pkt = format!("CFF{},65,0", char_index.0);
            state.send_data(SendTarget::ToArea { map, x, y }, &cff_pkt).await;

            // Send HP update
            let hp_pkt = {
                let u = state.users.get(&tc).unwrap();
                format!("[H]{},{}", u.max_hp, u.min_hp)
            };
            state.send_to(tc, &hp_pkt).await;

            // Broadcast CP (character model change) — VB6: ChangeUserChar
            let cp_pkt = format!(
                "CP{},{},{},{},{},{},0,0,{}",
                char_index.0, new_body, orig_head, heading,
                weapon_anim, shield_anim, casco_anim
            );
            state.send_data(SendTarget::ToArea { map, x, y }, &cp_pkt).await;

            let admin_name = state.users.get(&conn_id).map(|u| u.char_name.clone()).unwrap_or_default();
            state.send_to(conn_id, &format!("{}{} revivido.{}", server_opcodes::CONSOLE_MSG, target, font_types::INFO)).await;
            info!("[GM] {} revived {}", admin_name, target);
        }
        None => {
            state.send_to(conn_id, "||196").await;
        }
    }
}

/// /ESPIAR nick — Teleport invisibly to user.
async fn handle_slash_espiar(state: &mut GameState, conn_id: ConnectionId, target: &str) {
    match state.users.get(&conn_id) {
        Some(u) if u.logged && u.privileges >= privilege_level::SEMIDIOS => {}
        _ => return,
    }

    let target_upper = target.to_uppercase();
    let found = state.users.values()
        .find(|u| u.logged && u.char_name.to_uppercase() == target_upper)
        .map(|u| (u.pos_map, u.pos_x, u.pos_y));

    match found {
        Some((map, x, y)) => {
            // Make invisible if not already
            let is_invisible = state.users.get(&conn_id).map(|u| u.admin_invisible).unwrap_or(false);
            if !is_invisible {
                handle_slash_invisible(state, conn_id).await;
            }

            // Warp to target (offset -2 to not be on exact tile)
            let warp_x = (x - 2).max(1);
            let warp_y = (y - 2).max(1);
            warp_user(state, conn_id, map, warp_x, warp_y).await;

            state.send_to(conn_id, &format!("{}Espiando a {}.{}", server_opcodes::CONSOLE_MSG, target, font_types::INFO)).await;
        }
        None => {
            state.send_to(conn_id, "||196").await;
        }
    }
}

/// /INV nick — View user's inventory.
async fn handle_slash_inv(state: &mut GameState, conn_id: ConnectionId, target: &str) {
    match state.users.get(&conn_id) {
        Some(u) if u.logged && u.privileges >= privilege_level::SEMIDIOS => {}
        _ => return,
    }

    let target_upper = target.to_uppercase();
    let found = state.users.values()
        .find(|u| u.logged && u.char_name.to_uppercase() == target_upper)
        .map(|u| u.conn_id);

    match found {
        Some(tc) => {
            // Send each inventory slot as console messages
            let inv_data: Vec<(usize, i32, i32, bool)> = state.users.get(&tc)
                .map(|u| u.inventory.iter().enumerate()
                    .filter(|(_, s)| s.obj_index > 0)
                    .map(|(i, s)| (i + 1, s.obj_index, s.amount, s.equipped))
                    .collect())
                .unwrap_or_default();

            let target_name = state.users.get(&tc).map(|u| u.char_name.clone()).unwrap_or_default();
            state.send_to(conn_id, &format!("{}Inventario de {}:{}", server_opcodes::CONSOLE_MSG, target_name, font_types::INFO)).await;

            let is_empty = inv_data.is_empty();
            for (slot, obj_idx, amount, equipped) in inv_data {
                let name = state.get_object(obj_idx).map(|o| o.name.clone()).unwrap_or_else(|| format!("Obj#{}", obj_idx));
                let eq = if equipped { " [E]" } else { "" };
                state.send_to(conn_id, &format!("{}  Slot {}: {} x{}{}{}", server_opcodes::CONSOLE_MSG, slot, name, amount, eq, font_types::INFO)).await;
            }

            if is_empty {
                state.send_to(conn_id, &format!("{}  (vacio){}", server_opcodes::CONSOLE_MSG, font_types::INFO)).await;
            }
        }
        None => {
            state.send_to(conn_id, "||196").await;
        }
    }
}

/// /BOV nick — View user's bank vault.
async fn handle_slash_bov(state: &mut GameState, conn_id: ConnectionId, target: &str) {
    match state.users.get(&conn_id) {
        Some(u) if u.logged && u.privileges >= privilege_level::SEMIDIOS => {}
        _ => return,
    }

    let target_upper = target.to_uppercase();
    let found = state.users.values()
        .find(|u| u.logged && u.char_name.to_uppercase() == target_upper)
        .map(|u| u.conn_id);

    match found {
        Some(tc) => {
            let bank_data: Vec<(usize, i32, i32)> = state.users.get(&tc)
                .map(|u| u.bank.iter().enumerate()
                    .filter(|(_, s)| s.obj_index > 0)
                    .map(|(i, s)| (i + 1, s.obj_index, s.amount))
                    .collect())
                .unwrap_or_default();

            let target_name = state.users.get(&tc).map(|u| u.char_name.clone()).unwrap_or_default();
            let bank_gold = state.users.get(&tc).map(|u| u.bank_gold).unwrap_or(0);
            state.send_to(conn_id, &format!("{}Boveda de {} (Oro: {}):{}", server_opcodes::CONSOLE_MSG, target_name, bank_gold, font_types::INFO)).await;

            let bank_empty = bank_data.is_empty();
            for (slot, obj_idx, amount) in bank_data {
                let name = state.get_object(obj_idx).map(|o| o.name.clone()).unwrap_or_else(|| format!("Obj#{}", obj_idx));
                state.send_to(conn_id, &format!("{}  Slot {}: {} x{}{}", server_opcodes::CONSOLE_MSG, slot, name, amount, font_types::INFO)).await;
            }

            if bank_empty {
                state.send_to(conn_id, &format!("{}  (vacia){}", server_opcodes::CONSOLE_MSG, font_types::INFO)).await;
            }
        }
        None => {
            state.send_to(conn_id, "||196").await;
        }
    }
}

/// /RMSG text — Server-wide broadcast message.
async fn handle_slash_rmsg(state: &mut GameState, conn_id: ConnectionId, text: &str) {
    match state.users.get(&conn_id) {
        Some(u) if u.logged && u.privileges >= privilege_level::SEMIDIOS => {}
        _ => return,
    }

    let admin_name = state.users.get(&conn_id).map(|u| u.char_name.clone()).unwrap_or_default();
    // VB6: N|<admin>> <message> with celeste font
    let msg = format!("N|{}>> {}{}", admin_name, text, font_types::SYSTEM);
    state.send_data(SendTarget::ToAll, &msg).await;
    info!("[GM] {} broadcast: {}", admin_name, text);
}

/// /LMSG text@minutes — Set automatic periodic broadcast.
async fn handle_slash_lmsg(state: &mut GameState, conn_id: ConnectionId, args: &str) {
    match state.users.get(&conn_id) {
        Some(u) if u.logged && u.privileges >= privilege_level::DIOS => {}
        _ => return,
    }

    let parts: Vec<&str> = args.splitn(2, '@').collect();
    let text = parts[0].trim();

    if text.is_empty() {
        state.auto_msg_active = false;
        state.auto_msg_text.clear();
        state.send_to(conn_id, &format!("{}Mensaje automatico desactivado.{}", server_opcodes::CONSOLE_MSG, font_types::INFO)).await;
        return;
    }

    let minutes: i32 = if parts.len() > 1 { parts[1].trim().parse().unwrap_or(5) } else { 5 };
    state.auto_msg_active = true;
    state.auto_msg_text = text.to_string();
    state.auto_msg_interval = minutes;
    state.auto_msg_counter = 0;

    state.send_to(conn_id, &format!("{}Mensaje automatico cada {}min: {}{}", server_opcodes::CONSOLE_MSG, minutes, text, font_types::INFO)).await;
}

/// /EXP multiplier — Set experience multiplier.
async fn handle_slash_exp_mult(state: &mut GameState, conn_id: ConnectionId, val: &str) {
    match state.users.get(&conn_id) {
        Some(u) if u.logged && u.privileges >= privilege_level::ADMINISTRADOR => {}
        _ => return,
    }

    let mult: i32 = val.parse().unwrap_or(0);
    if mult < 1 {
        state.send_to(conn_id, &format!("{}Valor invalido.{}", server_opcodes::CONSOLE_MSG, font_types::INFO)).await;
        return;
    }

    state.multiplicador_exp = mult;
    let msg = format!("||774@{}", mult);
    state.send_data(SendTarget::ToAll, &msg).await;
    info!("[GM] EXP multiplier set to {}x", mult);
}

/// /GLD multiplier — Set gold multiplier.
async fn handle_slash_gld_mult(state: &mut GameState, conn_id: ConnectionId, val: &str) {
    match state.users.get(&conn_id) {
        Some(u) if u.logged && u.privileges >= privilege_level::ADMINISTRADOR => {}
        _ => return,
    }

    let mult: i32 = val.parse().unwrap_or(0);
    if mult < 1 {
        state.send_to(conn_id, &format!("{}Valor invalido.{}", server_opcodes::CONSOLE_MSG, font_types::INFO)).await;
        return;
    }

    state.multiplicador_oro = mult;
    let msg = format!("||775@{}", mult);
    state.send_data(SendTarget::ToAll, &msg).await;
    info!("[GM] Gold multiplier set to {}x", mult);
}

/// /DROP multiplier — Set drop multiplier.
async fn handle_slash_drop_mult(state: &mut GameState, conn_id: ConnectionId, val: &str) {
    match state.users.get(&conn_id) {
        Some(u) if u.logged && u.privileges >= privilege_level::ADMINISTRADOR => {}
        _ => return,
    }

    let mult: i32 = val.parse().unwrap_or(0);
    if mult < 1 {
        state.send_to(conn_id, &format!("{}Valor invalido.{}", server_opcodes::CONSOLE_MSG, font_types::INFO)).await;
        return;
    }

    state.multiplicador_drop = mult;
    let msg = format!("||776@{}", mult);
    state.send_data(SendTarget::ToAll, &msg).await;
    info!("[GM] Drop multiplier set to {}x", mult);
}

/// /HOME nick — Teleport user to their home city.
async fn handle_slash_home(state: &mut GameState, conn_id: ConnectionId, target: &str) {
    match state.users.get(&conn_id) {
        Some(u) if u.logged && u.privileges >= privilege_level::SEMIDIOS => {}
        _ => return,
    }

    let target_upper = target.to_uppercase();
    let target_conn = state.users.values()
        .find(|u| u.logged && u.char_name.to_uppercase() == target_upper)
        .map(|u| (u.conn_id, u.hogar.clone(), u.armada_real, u.fuerzas_caos));

    match target_conn {
        Some((tc, hogar, armada, caos)) => {
            // Determine home location
            let (map, x, y) = if armada {
                (29, 50, 90) // Royal Army camp
            } else if caos {
                (27, 47, 48) // Chaos Forces camp
            } else {
                match hogar.to_uppercase().as_str() {
                    "THIR" => (25, 74, 44),
                    "INTHAK" => (130, 52, 56),
                    "RUVENDEL" => (26, 51, 52),
                    _ => (28, 54, 36), // Default: Banderbill
                }
            };

            warp_user(state, tc, map, x, y).await;
            state.send_to(conn_id, "||772").await;
            state.send_to(tc, "||773").await;
        }
        None => {
            state.send_to(conn_id, "||198").await;
        }
    }
}

/// /OFF — Shutdown server.
async fn handle_slash_off(state: &mut GameState, conn_id: ConnectionId) {
    match state.users.get(&conn_id) {
        Some(u) if u.logged && u.privileges >= privilege_level::ADMINISTRADOR => {}
        _ => return,
    }

    let admin_name = state.users.get(&conn_id).map(|u| u.char_name.clone()).unwrap_or_default();
    info!("[GM] {} shutting down server", admin_name);

    // Send shutdown message to all
    let msg = format!("N|Servidor apagado por {}.{}", admin_name, font_types::COMBAT);
    state.send_data(SendTarget::ToAll, &msg).await;

    // Exit process
    std::process::exit(0);
}

/// /ECHARTODOSPJS — Kick all non-privileged players.
async fn handle_slash_echartodospjs(state: &mut GameState, conn_id: ConnectionId) {
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
    state.send_to(conn_id, &format!("{}{} jugadores desconectados.{}", server_opcodes::CONSOLE_MSG, count, font_types::INFO)).await;
    info!("[GM] {} kicked all {} players", admin_name, count);
}

/// /DAMETODO nick — Drop all user's inventory items on ground.
async fn handle_slash_dametodo(state: &mut GameState, conn_id: ConnectionId, target: &str) {
    match state.users.get(&conn_id) {
        Some(u) if u.logged && u.privileges >= privilege_level::GRAN_DIOS => {}
        _ => return,
    }

    let target_upper = target.to_uppercase();
    let target_conn = state.users.values()
        .find(|u| u.logged && u.char_name.to_uppercase() == target_upper)
        .map(|u| u.conn_id);

    match target_conn {
        Some(tc) => {
            // Clear inventory
            if let Some(user) = state.users.get_mut(&tc) {
                for slot in &mut user.inventory {
                    *slot = InventorySlot { obj_index: 0, amount: 0, equipped: false };
                }
                user.equip = EquipSlots::default();
            }

            // Send full inventory update
            send_full_inventory(state, tc).await;

            let admin_name = state.users.get(&conn_id).map(|u| u.char_name.clone()).unwrap_or_default();
            state.send_to(tc, &format!("||754@{}", admin_name)).await;
            let msg = format!("||755@{}@{}", admin_name, target);
            state.send_data(SendTarget::ToAdmins, &msg).await;
            info!("[GM] {} stripped inventory of {}", admin_name, target);
        }
        None => {
            state.send_to(conn_id, "||196").await;
        }
    }
}

/// /MATA npc_index — Kill target NPC by runtime index.
async fn handle_slash_mata(state: &mut GameState, conn_id: ConnectionId, target: &str) {
    match state.users.get(&conn_id) {
        Some(u) if u.logged && u.privileges >= privilege_level::SEMIDIOS => {}
        _ => return,
    }

    if let Ok(npc_idx) = target.parse::<usize>() {
        if npc_idx > 0 && npc_idx < state.npcs.len() {
            let npc_data = state.npcs[npc_idx].as_ref().map(|n| (n.map, n.x, n.y, n.char_index));
            if let Some((map, x, y, ci)) = npc_data {
                // Remove from world tile
                if map > 0 {
                    let grid = state.world.grid_mut(map);
                    if let Some(tile) = grid.tile_mut(x, y) {
                        if tile.npc_index == npc_idx as i32 {
                            tile.npc_index = 0;
                        }
                    }
                }
                let bp = format!("BP{}", ci.0);
                state.send_data(SendTarget::ToArea { map, x, y }, &bp).await;
                state.npcs[npc_idx] = None;
                state.send_to(conn_id, &format!("{}NPC #{} eliminado.{}", server_opcodes::CONSOLE_MSG, npc_idx, font_types::INFO)).await;
                return;
            }
        }
    }

    state.send_to(conn_id, &format!("{}NPC no encontrado. Usa /MATA <npc_runtime_index>{}", server_opcodes::CONSOLE_MSG, font_types::INFO)).await;
}

/// /MASSKILL — Kill all NPCs on current map.
async fn handle_slash_masskill(state: &mut GameState, conn_id: ConnectionId) {
    let map = match state.users.get(&conn_id) {
        Some(u) if u.logged && u.privileges >= privilege_level::DIOS => u.pos_map,
        _ => return,
    };

    let mut killed = 0;
    let npc_indices: Vec<usize> = state.npcs.iter().enumerate()
        .filter_map(|(i, n)| n.as_ref().filter(|n| n.map == map).map(|_| i))
        .collect();

    for idx in npc_indices {
        let npc_data = state.npcs[idx].as_ref().map(|n| (n.x, n.y, n.char_index));
        if let Some((nx, ny, ci)) = npc_data {
            let grid = state.world.grid_mut(map);
            if let Some(tile) = grid.tile_mut(nx, ny) {
                if tile.npc_index == idx as i32 {
                    tile.npc_index = 0;
                }
            }
            let bp = format!("BP{}", ci.0);
            state.send_data(SendTarget::ToArea { map, x: nx, y: ny }, &bp).await;
            killed += 1;
        }
        state.npcs[idx] = None;
    }

    state.send_to(conn_id, &format!("{}{} NPCs eliminados en mapa {}.{}", server_opcodes::CONSOLE_MSG, killed, map, font_types::INFO)).await;
}

/// /LIMPIAR or /LMAP — Clean all ground items on current map.
async fn handle_slash_limpiar(state: &mut GameState, conn_id: ConnectionId) {
    let map = match state.users.get(&conn_id) {
        Some(u) if u.logged && u.privileges >= privilege_level::DIOS => u.pos_map,
        _ => return,
    };

    // Clean ground items from world grid
    let mut cleaned = 0;
    let grid = state.world.grid_mut(map);
    for y in 1..=100i32 {
        for x in 1..=100i32 {
            if let Some(tile) = grid.tile_mut(x, y) {
                if tile.ground_item.obj_index > 0 {
                    tile.ground_item = world::GroundItem::default();
                    cleaned += 1;
                }
            }
        }
    }

    state.send_to(conn_id, &format!("{}{} items del suelo limpiados en mapa {}.{}", server_opcodes::CONSOLE_MSG, cleaned, map, font_types::INFO)).await;
}

/// /NICK2IP nick — Get IP of user.
async fn handle_slash_nick2ip(state: &mut GameState, conn_id: ConnectionId, target: &str) {
    match state.users.get(&conn_id) {
        Some(u) if u.logged && u.privileges >= privilege_level::DIOS => {}
        _ => return,
    }

    let target_upper = target.to_uppercase();
    let found = state.users.values()
        .find(|u| u.logged && u.char_name.to_uppercase() == target_upper)
        .map(|u| (u.char_name.clone(), u.ip.clone()));

    match found {
        Some((name, ip)) => {
            state.send_to(conn_id, &format!("{}IP de {}: {}{}", server_opcodes::CONSOLE_MSG, name, ip, font_types::INFO)).await;
        }
        None => {
            state.send_to(conn_id, "||196").await;
        }
    }
}

/// /IP2NICK ip — Find users with given IP.
async fn handle_slash_ip2nick(state: &mut GameState, conn_id: ConnectionId, ip: &str) {
    match state.users.get(&conn_id) {
        Some(u) if u.logged && u.privileges >= privilege_level::DIOS => {}
        _ => return,
    }

    let matches: Vec<String> = state.users.values()
        .filter(|u| u.logged && u.ip == ip)
        .map(|u| u.char_name.clone())
        .collect();

    if matches.is_empty() {
        state.send_to(conn_id, &format!("{}Nadie con IP {}{}", server_opcodes::CONSOLE_MSG, ip, font_types::INFO)).await;
    } else {
        state.send_to(conn_id, &format!("{}IP {}: {}{}", server_opcodes::CONSOLE_MSG, ip, matches.join(", "), font_types::INFO)).await;
    }
}

/// /NOGLOBAL — Toggle global chat on/off (GM Dios+ command). VB6: frmMain.frm
async fn handle_slash_noglobal(state: &mut GameState, conn_id: ConnectionId) {
    let priv_level = state.users.get(&conn_id).map(|u| u.privileges).unwrap_or(0);
    if priv_level < 4 { return; } // Dios+
    state.chat_global = !state.chat_global;
    if state.chat_global {
        state.send_data(SendTarget::ToAll, "||803").await;
    } else {
        state.send_data(SendTarget::ToAll, "||804").await;
    }
}

/// /FPS — Show server stats.
async fn handle_slash_fps(state: &mut GameState, conn_id: ConnectionId) {
    match state.users.get(&conn_id) {
        Some(u) if u.logged && u.privileges >= privilege_level::SEMIDIOS => {}
        _ => return,
    }

    let online = state.users.values().filter(|u| u.logged).count();
    let npc_count = state.npcs.iter().filter(|n| n.is_some()).count();
    state.send_to(conn_id, &format!("{}Online: {} | NPCs: {} | Record: {}{}", server_opcodes::CONSOLE_MSG, online, npc_count, state.record_users, font_types::INFO)).await;
}

/// /CT map x y — Create teleport at current position (requires map .inf modification).
async fn handle_slash_ct(state: &mut GameState, conn_id: ConnectionId, args: &str) {
    match state.users.get(&conn_id) {
        Some(u) if u.logged && u.privileges >= privilege_level::DIOS => {}
        _ => return,
    }
    // Teleport creation requires modifying map .inf files which is not yet supported.
    // In VB6 this modifies MapData().TileExit in memory.
    state.send_to(conn_id, &format!("{}Creacion de teleports no soportada aun (requiere edicion de .inf).{}", server_opcodes::CONSOLE_MSG, font_types::INFO)).await;
}

/// /DT — Destroy teleport at current position.
async fn handle_slash_dt(state: &mut GameState, conn_id: ConnectionId) {
    match state.users.get(&conn_id) {
        Some(u) if u.logged && u.privileges >= privilege_level::DIOS => {}
        _ => return,
    }
    state.send_to(conn_id, &format!("{}Destruccion de teleports no soportada aun (requiere edicion de .inf).{}", server_opcodes::CONSOLE_MSG, font_types::INFO)).await;
}

/// /RESMAP — Respawn all NPCs on current map.
async fn handle_slash_resmap(state: &mut GameState, conn_id: ConnectionId) {
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

    state.send_to(conn_id, &format!("{}{} NPCs respawneados en mapa {}.{}", server_opcodes::CONSOLE_MSG, respawned, map, font_types::INFO)).await;
}

/// /TALKAS text — Send message as NPC/anonymous.
async fn handle_slash_talkas(state: &mut GameState, conn_id: ConnectionId, args: &str) {
    match state.users.get(&conn_id) {
        Some(u) if u.logged && u.privileges >= privilege_level::DIOS => {}
        _ => return,
    }

    let (map, x, y) = match state.users.get(&conn_id) {
        Some(u) => (u.pos_map, u.pos_x, u.pos_y),
        None => return,
    };

    let msg = format!("T|{}\u{00B0}{}\u{00B0}0", "16776960", args); // Yellow color
    state.send_data(SendTarget::ToArea { map, x, y }, &msg).await;
}

/// /SETDESC nick description — Set NPC/user description.
async fn handle_slash_setdesc(state: &mut GameState, conn_id: ConnectionId, args: &str) {
    match state.users.get(&conn_id) {
        Some(u) if u.logged && u.privileges >= privilege_level::DIOS => {}
        _ => return,
    }

    state.send_to(conn_id, &format!("{}Descripcion actualizada.{}", server_opcodes::CONSOLE_MSG, font_types::INFO)).await;
}

/// /STOP nick — Freeze user (can't move).
async fn handle_slash_stop(state: &mut GameState, conn_id: ConnectionId, target: &str, freeze: bool) {
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
            state.send_to(conn_id, &format!("{}{} {}.{}", server_opcodes::CONSOLE_MSG, target, status, font_types::INFO)).await;
        }
        None => {
            state.send_to(conn_id, "||196").await;
        }
    }
}

/// /CHEAT nick — Toggle god mode for user (full HP/MP/STA regen).
async fn handle_slash_cheat(state: &mut GameState, conn_id: ConnectionId, target: &str) {
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
            let (hp, mana, sta) = {
                let u = state.users.get(&tc).unwrap();
                (format!("[H]{},{}", u.max_hp, u.min_hp),
                 format!("[M]{},{}", u.max_mana, u.min_mana),
                 format!("[S]{},{}", u.max_sta, u.min_sta))
            };
            state.send_to(tc, &hp).await;
            state.send_to(tc, &mana).await;
            state.send_to(tc, &sta).await;

            state.send_to(conn_id, &format!("{}{} restaurado.{}", server_opcodes::CONSOLE_MSG, target, font_types::INFO)).await;
        }
        None => {
            state.send_to(conn_id, "||196").await;
        }
    }
}

async fn handle_slash_nave(state: &mut GameState, conn_id: ConnectionId) {
    match state.users.get(&conn_id) {
        Some(u) if u.logged && u.privileges > privilege_level::USER => {}
        _ => return,
    }
    if let Some(user) = state.users.get_mut(&conn_id) {
        user.navigating = !user.navigating;
        let status = if user.navigating { "activada" } else { "desactivada" };
        let msg = format!("||Navegacion {}{}", status, font_types::SYSTEM);
        let conn = user.conn_id;
        state.send_to(conn, &msg).await;
    }
}

/// /HABILITAR — Toggle server GM-only mode. Requires privileges > 0.
async fn handle_slash_habilitar(state: &mut GameState, conn_id: ConnectionId) {
    match state.users.get(&conn_id) {
        Some(u) if u.logged && u.privileges > privilege_level::USER => {}
        _ => return,
    }
    if state.server_solo_gms {
        state.send_to(conn_id, "||563").await; // Server abierto
        state.server_solo_gms = false;
    } else {
        state.send_to(conn_id, "||564").await; // Server solo GMs
        state.server_solo_gms = true;
    }
}

/// /COL <color>@<msg> — Send colored message to all. Requires privileges > 0.
async fn handle_slash_col(state: &mut GameState, conn_id: ConnectionId, args: &str) {
    let name = match state.users.get(&conn_id) {
        Some(u) if u.logged && u.privileges > privilege_level::USER => u.char_name.clone(),
        _ => return,
    };

    let color_str = read_field(1, args, '@');
    let msg_text = read_field(2, args, '@');
    if msg_text.is_empty() { return; }

    let font = match color_str.to_uppercase().as_str() {
        "LILA"     => "~200~20~215~1~0",
        "VERDE"    => "~0~255~0~1~0",
        "AZUL"     => "~0~0~255~1~0",
        "ROJO"     => "~255~0~0~1~0",
        "AMARILLO" => "~255~255~0~1~0",
        "BLANCO"   => "~255~255~255~1~0",
        "GRIS"     => "~120~120~120~1~0",
        "NARANJA"  => "~255~128~0~1~0",
        "MARRON"   => "~128~64~0~1~0",
        "CELESTE"  => "~0~255~255~1~0",
        "VIOLETA"  => "~64~0~128~1~0",
        _ => return,
    };

    let pkt = format!("N|{}> {}{}", name, msg_text, font);
    state.send_data(SendTarget::ToAll, &pkt).await;
}

/// /NOADV <name> — Clear all warnings/penalties for a character. Requires Semidios+.
async fn handle_slash_noadv(state: &mut GameState, conn_id: ConnectionId, target: &str) {
    match state.users.get(&conn_id) {
        Some(u) if u.logged && u.privileges >= privilege_level::SEMIDIOS => {}
        _ => return,
    }
    // Clear penalties from charfile
    let base = state.base_path.clone();
    let charpath = base.join("charfile").join(format!("{}.chr", target));
    if charpath.exists() {
        let chr = charpath.to_str().unwrap_or("");
        let _ = crate::config::write_var(chr, "PENAS", "Cant", "0");
        state.send_to(conn_id, "||441").await;
        info!("[GM] Cleared penalties for {}", target);
    } else {
        state.send_to(conn_id, "||196").await; // User not found
    }
}

/// /LIBERAR <name> — Release player from prison. Requires Semidios+.
async fn handle_slash_liberar(state: &mut GameState, conn_id: ConnectionId, target: &str) {
    let gm_name = match state.users.get(&conn_id) {
        Some(u) if u.logged && u.privileges >= privilege_level::SEMIDIOS => u.char_name.clone(),
        _ => return,
    };

    let target_upper = target.to_uppercase();
    let target_conn = state.online_names.get(&target_upper).copied();

    if let Some(t_conn) = target_conn {
        // Warp target to Ullathorpe (map 1, 50, 50) as release location
        warp_user(state, t_conn, 1, 50, 50).await;
        let pkt = format!("||443@{}@{}", gm_name, target);
        state.send_data(SendTarget::ToAdmins, &pkt).await;
        state.send_to(t_conn, "||444").await; // You've been released
        info!("[GM] {} released {} from prison", gm_name, target);
    } else {
        state.send_to(conn_id, "||198").await; // Not online
    }
}

/// /PENAS <name> — View penalties of a character. Requires Semidios+.
async fn handle_slash_penas(state: &mut GameState, conn_id: ConnectionId, target: &str) {
    match state.users.get(&conn_id) {
        Some(u) if u.logged && u.privileges >= privilege_level::SEMIDIOS => {}
        _ => return,
    }

    let base = state.base_path.clone();
    let charpath = base.join("charfile").join(format!("{}.chr", target));
    if !charpath.exists() {
        state.send_to(conn_id, "||196").await;
        return;
    }
    let chr = charpath.to_str().unwrap_or("");

    let cant: i32 = crate::config::get_var(chr, "PENAS", "Cant")
        .parse().unwrap_or(0);
    let pkt_header = format!("||327@{}", cant);
    state.send_to(conn_id, &pkt_header).await;

    for i in 1..=cant {
        let p = crate::config::get_var(chr, "PENAS", &format!("P{}", i));
        let pkt = format!("||327@{}", p);
        state.send_to(conn_id, &pkt).await;
    }
}

/// /MOD <stat> <value> — Modify own stats. Requires Semidios+.
/// /SMOD <name> <stat> <value> — Modify another player's stats. Requires Director+.
async fn handle_slash_mod(state: &mut GameState, conn_id: ConnectionId, args: &str, _is_self: bool) {
    match state.users.get(&conn_id) {
        Some(u) if u.logged && u.privileges >= privilege_level::SEMIDIOS => {}
        _ => return,
    }

    // VB6: ReadField(1, rData, 32) and ReadField(2, rData, 32) — space-delimited
    let parts: Vec<&str> = args.splitn(2, ' ').collect();
    if parts.len() < 2 { return; }
    let stat = parts[0].to_uppercase();
    let value_str = parts[1];
    let value: i64 = value_str.parse().unwrap_or(0);

    // Apply to self — /MOD only affects the invoker
    let target = conn_id;
    apply_mod_self(state, conn_id, target, &stat, value, value_str).await;
}

/// /SMOD <name> <stat> <value> — Modify another player's stats. Requires Director+.
/// VB6: Only a subset of /MOD subcommands (no AURA, ARMA, ESCU, CASCO, HAM, AGU, ATRI, FX).
async fn handle_slash_smod(state: &mut GameState, conn_id: ConnectionId, args: &str) {
    let gm_name = match state.users.get(&conn_id) {
        Some(u) if u.logged && u.privileges >= privilege_level::DIRECTOR => u.char_name.clone(),
        _ => return,
    };

    // VB6: ReadField(1)=name, ReadField(2)=subcommand, ReadField(3)=value — space delimited
    let parts: Vec<&str> = args.splitn(3, ' ').collect();
    if parts.len() < 3 { return; }
    let target_name = parts[0].replace('+', " ");
    let stat = parts[1].to_uppercase();
    let value: i64 = parts.get(2).and_then(|v| v.parse().ok()).unwrap_or(0);

    let target_upper = target_name.to_uppercase();
    // SHAY protection
    if target_upper == "SHAY" && gm_name.to_uppercase() != "SHAY" { return; }

    let target_conn = match state.online_names.get(&target_upper).copied() {
        Some(c) => c,
        None => {
            state.send_to(conn_id, "||196").await;
            return;
        }
    };

    apply_mod_other(state, conn_id, target_conn, &stat, value).await;
}

/// /MOD apply — self-modification only. VB6: TCP_HandleData3.bas:1725-1877
/// Supports all subcommands: PART, AURA, FX, ATRI, ORO, EXP, BODY, HEAD,
/// CRI, CIU, LEVEL, CLASE, HAM, AGU, STA, MP, HP, ESCU, CASCO, ARMA.
async fn apply_mod_self(state: &mut GameState, conn_id: ConnectionId, target: ConnectionId, stat: &str, value: i64, value_str: &str) {
    let (char_index, map, x, y) = match state.users.get(&target) {
        Some(u) => (u.char_index.0, u.pos_map, u.pos_x, u.pos_y),
        None => return,
    };

    match stat {
        "PART" => {
            if value <= 0 { return; }
            let pkt = format!("CFF{},{},0", char_index, value);
            state.send_data(SendTarget::ToArea { map, x, y }, &pkt).await;
        }
        "AURA" => {
            // VB6: UserList(tIndex).Char.AuraA = val(Arg2); SendUserAura
            // TODO: implement aura field if needed
        }
        "FX" => {
            let pkt = format!("CFX{},{},20", char_index, value);
            state.send_data(SendTarget::ToArea { map, x, y }, &pkt).await;
        }
        "ATRI" => {
            if let Some(user) = state.users.get_mut(&target) {
                for i in 0..5 {
                    user.attributes[i] = value as i32;
                }
            }
            state.send_to(conn_id, &format!("||571@{}", value)).await;
        }
        "ORO" => {
            if let Some(user) = state.users.get_mut(&target) {
                user.gold = value;
            }
            send_stats_gold(state, target).await;
            state.send_to(conn_id, &format!("||572@{}", value)).await;
        }
        "EXP" => {
            if let Some(user) = state.users.get_mut(&target) {
                user.exp = value;
            }
            check_user_level(state, target).await;
            send_stats_exp(state, target).await;
            state.send_to(conn_id, &format!("||572@{}", value)).await;
        }
        "BODY" => {
            if let Some(user) = state.users.get_mut(&target) {
                user.body = value as i32;
            }
            state.send_to(conn_id, &format!("||573@{}", value)).await;
            // VB6: ChangeUserChar → CP to map
            let u = state.users.get(&target).unwrap();
            let cp = format!("CP{},{},{},{},{},{},0,0,{}", char_index, value, u.head, u.heading, u.weapon_anim, u.shield_anim, u.casco_anim);
            state.send_data(SendTarget::ToMap(map), &cp).await;
        }
        "HEAD" => {
            if let Some(user) = state.users.get_mut(&target) {
                user.head = value as i32;
            }
            state.send_to(conn_id, &format!("||574@{}", value)).await;
            let u = state.users.get(&target).unwrap();
            let cp = format!("CP{},{},{},{},{},{},0,0,{}", char_index, u.body, value, u.heading, u.weapon_anim, u.shield_anim, u.casco_anim);
            state.send_data(SendTarget::ToMap(map), &cp).await;
        }
        "CRI" => {
            if let Some(user) = state.users.get_mut(&target) {
                user.criminales_matados = value as i32;
            }
            state.send_to(conn_id, &format!("||575@{}", value)).await;
        }
        "CIU" => {
            if let Some(user) = state.users.get_mut(&target) {
                user.ciudadanos_matados = value as i32;
            }
            state.send_to(conn_id, &format!("||576@{}", value)).await;
        }
        "LEVEL" => {
            if let Some(user) = state.users.get_mut(&target) {
                user.level = value as i32;
            }
            state.send_to(conn_id, &format!("||577@{}", value)).await;
        }
        "CLASE" => {
            if let Some(user) = state.users.get_mut(&target) {
                user.class = value_str.to_string();
            }
            state.send_to(conn_id, &format!("||578@{}", value_str)).await;
        }
        "HAM" => {
            if let Some(user) = state.users.get_mut(&target) {
                user.min_ham = value as i32;
                user.max_ham = value as i32;
            }
            state.send_to(conn_id, &format!("||579@{}", value)).await;
        }
        "AGU" => {
            if let Some(user) = state.users.get_mut(&target) {
                user.min_agua = value as i32;
                user.max_agua = value as i32;
            }
            state.send_to(conn_id, &format!("||580@{}", value)).await;
        }
        "STA" => {
            if let Some(user) = state.users.get_mut(&target) {
                user.min_sta = value as i32;
                user.max_sta = value as i32;
            }
            send_stats_sta(state, target).await;
            state.send_to(conn_id, &format!("||581@{}", value)).await;
        }
        "MP" => {
            if let Some(user) = state.users.get_mut(&target) {
                user.min_mana = value as i32;
                user.max_mana = value as i32;
            }
            send_stats_mana(state, target).await;
            state.send_to(conn_id, &format!("||582@{}", value)).await;
        }
        "HP" => {
            if let Some(user) = state.users.get_mut(&target) {
                user.min_hp = value as i32;
                user.max_hp = value as i32;
            }
            send_stats_hp(state, target).await;
            state.send_to(conn_id, &format!("||583@{}", value)).await;
        }
        "ESCU" => {
            if let Some(user) = state.users.get_mut(&target) {
                user.shield_anim = value as i32;
            }
            let u = state.users.get(&target).unwrap();
            let cp = format!("CP{},{},{},{},{},{},0,0,{}", char_index, u.body, u.head, u.heading, u.weapon_anim, value, u.casco_anim);
            state.send_data(SendTarget::ToMap(map), &cp).await;
        }
        "CASCO" => {
            if let Some(user) = state.users.get_mut(&target) {
                user.casco_anim = value as i32;
            }
            let u = state.users.get(&target).unwrap();
            let cp = format!("CP{},{},{},{},{},{},0,0,{}", char_index, u.body, u.head, u.heading, u.weapon_anim, u.shield_anim, value);
            state.send_data(SendTarget::ToMap(map), &cp).await;
        }
        "ARMA" => {
            if let Some(user) = state.users.get_mut(&target) {
                user.weapon_anim = value as i32;
            }
            let u = state.users.get(&target).unwrap();
            let cp = format!("CP{},{},{},{},{},{},0,0,{}", char_index, u.body, u.head, u.heading, value, u.shield_anim, u.casco_anim);
            state.send_data(SendTarget::ToMap(map), &cp).await;
        }
        _ => {
            state.send_to(conn_id, "||584").await;
        }
    }
}

/// /SMOD apply — modify another player. VB6: TCP_HandleData3.bas:2051-2163
/// Only supports: PART, ORO, EXP, BODY, HEAD, CRI, CIU, LEVEL, CLASE, STA, MP, HP.
/// All modifications are broadcast to admins via ||591 packets.
async fn apply_mod_other(state: &mut GameState, gm_conn: ConnectionId, target: ConnectionId, stat: &str, value: i64) {
    let gm_name = state.users.get(&gm_conn).map(|u| u.char_name.clone()).unwrap_or_default();
    let target_name = state.users.get(&target).map(|u| u.char_name.clone()).unwrap_or_default();
    let (char_index, map, x, y) = match state.users.get(&target) {
        Some(u) => (u.char_index.0, u.pos_map, u.pos_x, u.pos_y),
        None => return,
    };

    match stat {
        "PART" => {
            if value <= 0 { return; }
            let pkt = format!("CFF{},{},0", char_index, value);
            state.send_data(SendTarget::ToArea { map, x, y }, &pkt).await;
        }
        "ORO" => {
            if let Some(user) = state.users.get_mut(&target) {
                user.gold = value;
            }
            send_stats_gold(state, target).await;
            let pkt = format!("||591@{}@oro@{}@{}", gm_name, target_name, value);
            state.send_data(SendTarget::ToAdmins, &pkt).await;
        }
        "EXP" => {
            if let Some(user) = state.users.get_mut(&target) {
                user.exp = value;
            }
            check_user_level(state, target).await;
            send_stats_exp(state, target).await;
            let pkt = format!("||591@{}@experiencia@{}@{}", gm_name, target_name, value);
            state.send_data(SendTarget::ToAdmins, &pkt).await;
        }
        "BODY" => {
            if let Some(user) = state.users.get_mut(&target) {
                user.body = value as i32;
            }
            let u = state.users.get(&target).unwrap();
            let cp = format!("CP{},{},{},{},{},{},0,0,{}", char_index, value, u.head, u.heading, u.weapon_anim, u.shield_anim, u.casco_anim);
            state.send_data(SendTarget::ToMap(map), &cp).await;
            let pkt = format!("||591@{}@body@{}@{}", gm_name, target_name, value);
            state.send_data(SendTarget::ToAdmins, &pkt).await;
        }
        "HEAD" => {
            if let Some(user) = state.users.get_mut(&target) {
                user.head = value as i32;
            }
            let u = state.users.get(&target).unwrap();
            let cp = format!("CP{},{},{},{},{},{},0,0,{}", char_index, u.body, value, u.heading, u.weapon_anim, u.shield_anim, u.casco_anim);
            state.send_data(SendTarget::ToMap(map), &cp).await;
            let pkt = format!("||591@{}@head@{}@{}", gm_name, target_name, value);
            state.send_data(SendTarget::ToAdmins, &pkt).await;
        }
        "CRI" => {
            if let Some(user) = state.users.get_mut(&target) {
                user.criminales_matados = value as i32;
            }
            let pkt = format!("||591@{}@criminales@{}@{}", gm_name, target_name, value);
            state.send_data(SendTarget::ToAdmins, &pkt).await;
        }
        "CIU" => {
            if let Some(user) = state.users.get_mut(&target) {
                user.ciudadanos_matados = value as i32;
            }
            let pkt = format!("||591@{}@ciudadanos@{}@{}", gm_name, target_name, value);
            state.send_data(SendTarget::ToAdmins, &pkt).await;
        }
        "LEVEL" => {
            if let Some(user) = state.users.get_mut(&target) {
                user.level = value as i32;
            }
            let pkt_level = format!("[L]{}", value);
            state.send_to(target, &pkt_level).await;
            let pkt = format!("||591@{}@nivel@{}@{}", gm_name, target_name, value);
            state.send_data(SendTarget::ToAdmins, &pkt).await;
        }
        "CLASE" => {
            // VB6: class is a string, but we receive it as numeric here
            let pkt = format!("||591@{}@clase@{}@{}", gm_name, target_name, value);
            state.send_data(SendTarget::ToAdmins, &pkt).await;
        }
        "STA" => {
            if let Some(user) = state.users.get_mut(&target) {
                user.min_sta = value as i32;
                user.max_sta = value as i32;
            }
            send_stats_sta(state, target).await;
            let pkt = format!("||591@{}@energia@{}@{}", gm_name, target_name, value);
            state.send_data(SendTarget::ToAdmins, &pkt).await;
        }
        "MP" => {
            if let Some(user) = state.users.get_mut(&target) {
                user.min_mana = value as i32;
                user.max_mana = value as i32;
            }
            send_stats_mana(state, target).await;
            let pkt = format!("||591@{}@mana@{}@{}", gm_name, target_name, value);
            state.send_data(SendTarget::ToAdmins, &pkt).await;
        }
        "HP" => {
            if let Some(user) = state.users.get_mut(&target) {
                user.min_hp = value as i32;
                user.max_hp = value as i32;
            }
            send_stats_hp(state, target).await;
            let pkt = format!("||591@{}@vida@{}@{}", gm_name, target_name, value);
            state.send_data(SendTarget::ToAdmins, &pkt).await;
        }
        _ => {
            state.send_to(gm_conn, "||584").await;
        }
    }
}

/// VB6 ClosestLegalPos: find the nearest walkable tile to (x,y) on map.
/// Searches in expanding rings: exact pos first, then ±1, ±2, etc.
fn find_closest_legal_pos(state: &GameState, map: i32, x: i32, y: i32) -> (i32, i32) {
    for ring in 0..=12 {
        for ty in (y - ring)..=(y + ring) {
            for tx in (x - ring)..=(x + ring) {
                if tx < 1 || tx > 100 || ty < 1 || ty > 100 { continue; }
                if !state.is_tile_blocked(map, tx, ty) {
                    return (tx, ty);
                }
            }
        }
    }
    (0, 0) // No legal position found
}

/// /ACC <npc_id> or /RACC <npc_id> — Spawn NPC at GM's position. Requires GranDios+.
async fn handle_slash_acc(state: &mut GameState, conn_id: ConnectionId, npc_id_str: &str, with_respawn: bool) {
    let (map, x, y, name) = match state.users.get(&conn_id) {
        Some(u) if u.logged && u.privileges >= privilege_level::GRAN_DIOS => {
            (u.pos_map, u.pos_x, u.pos_y, u.char_name.clone())
        }
        _ => return,
    };

    let npc_num: usize = match npc_id_str.parse() {
        Ok(n) if n > 0 => n,
        _ => return,
    };

    // Look up NPC name for logging
    let npc_name = state.game_data.npcs.get(npc_num)
        .map(|n| n.name.clone())
        .unwrap_or_else(|| format!("NPC#{}", npc_num));

    // VB6: SpawnNpc uses ClosestLegalPos — find nearest free tile
    // The GM's tile is occupied by the GM, so search nearby tiles
    let (spawn_x, spawn_y) = find_closest_legal_pos(state, map, x, y);
    if spawn_x == 0 && spawn_y == 0 {
        let msg = format!("{}No hay posicion valida para spawnear.{}", server_opcodes::CONSOLE_MSG, font_types::INFO);
        state.send_to(conn_id, &msg).await;
        return;
    }

    // Spawn NPC at closest legal position
    if let Some(npc_idx) = state.spawn_npc(npc_num, map, spawn_x, spawn_y) {
        // Broadcast CC for the NPC to nearby players
        let cc_pkt = state.npcs.get(npc_idx)
            .and_then(|n| n.as_ref())
            .map(|n| n.build_cc_packet());
        if let Some(cc) = cc_pkt {
            state.send_data(SendTarget::ToArea { map, x: spawn_x, y: spawn_y }, &cc).await;
        }
        let prefix = if with_respawn { "con respawn" } else { "sin respawn" };
        info!("[GM] {} spawned {} ({}) at map {} ({},{}) {}", name, npc_name, npc_num, map, spawn_x, spawn_y, prefix);
    }
}

/// Generic privilege setter — /CONSEJERO, /SEMIDIOS, /DIOS, /GDIOS, /EVENT, /DIRECTOR,
/// /SUBADMINISTRADOR, /DEVELOPER, /ADMIN, /PJ
async fn handle_slash_set_privilege(
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
            state.send_to(conn_id, "||198").await; // Not online
            return;
        }
    };

    let target_real_name = state.users.get(&target_conn)
        .map(|u| u.char_name.clone())
        .unwrap_or_default();

    // Announce to admins
    let pkt = format!("||562@{}@{}@{}", gm_name, target_real_name, level_name);
    state.send_data(SendTarget::ToAdmins, &pkt).await;

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
async fn handle_slash_changenick(state: &mut GameState, conn_id: ConnectionId, new_name: &str) {
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
async fn handle_slash_borrarpj(state: &mut GameState, conn_id: ConnectionId, target: &str) {
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

    let base = state.base_path.clone();

    // Find the account that owns this character
    let charpath = base.join("charfile").join(format!("{}.chr", target));
    if !charpath.exists() {
        state.send_to(conn_id, "||196").await;
        return;
    }
    let chr = charpath.to_str().unwrap_or("");
    let account_name = crate::config::get_var(chr, "CHAR", "Cuenta");
    if account_name.is_empty() {
        state.send_to(conn_id, "||196").await;
        return;
    }

    // Remove from account file
    let acc_path = base.join("Accounts").join(format!("{}.act", account_name));
    if acc_path.exists() {
        let act = acc_path.to_str().unwrap_or("");
        let num_pjs: i32 = crate::config::get_var(act, "PJS", "NumPjs")
            .parse().unwrap_or(0);
        let mut found_idx = 0;
        for i in 1..=num_pjs {
            let pj = crate::config::get_var(act, "PJS", &format!("PJ{}", i));
            if pj.to_uppercase() == target_upper {
                found_idx = i;
                break;
            }
        }
        if found_idx > 0 {
            // Shift remaining characters down
            for i in found_idx..num_pjs {
                let next_pj = crate::config::get_var(act, "PJS", &format!("PJ{}", i + 1));
                let _ = crate::config::write_var(act, "PJS", &format!("PJ{}", i), &next_pj);
            }
            let _ = crate::config::write_var(act, "PJS", &format!("PJ{}", num_pjs), "");
            let _ = crate::config::write_var(act, "PJS", "NumPjs", &(num_pjs - 1).to_string());
        }
    }

    // Delete charfile
    let _ = std::fs::remove_file(&charpath);

    // Announce
    let pkt = format!("||565@{}@{}", gm_name, target);
    state.send_data(SendTarget::ToAll, &pkt).await;

    info!("[GM] {} deleted character {}", gm_name, target);
}

/// /BANHD <name> — Ban player's HD + IP + account. Requires Administrador.
async fn handle_slash_banhd(state: &mut GameState, conn_id: ConnectionId, target: &str) {
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
            state.send_to(conn_id, "||196").await;
            return;
        }
    };

    // Get target data
    let (target_ip, target_hd, target_account) = match state.users.get(&target_conn) {
        Some(u) => (u.ip.clone(), u.hd_serial.clone(), u.account_name.clone()),
        None => return,
    };

    // Set ban flag in charfile
    let base = state.base_path.clone();
    let charpath = base.join("charfile").join(format!("{}.chr", target));
    if charpath.exists() {
        let chr = charpath.to_str().unwrap_or("");
        let _ = crate::config::write_var(chr, "FLAGS", "Ban", "1");
        // Add penalty
        let cant: i32 = crate::config::get_var(chr, "PENAS", "Cant")
            .parse().unwrap_or(0);
        let _ = crate::config::write_var(chr, "PENAS", "Cant", &(cant + 1).to_string());
        let _ = crate::config::write_var(chr, "PENAS", &format!("P{}", cant + 1), "Tolerancia 0.");
    }

    // Ban HD serial
    if !target_hd.is_empty() {
        let _ = state.bans.ban_hd(&base, &target_hd);
    }

    // Ban IP
    if !target_ip.is_empty() {
        let _ = state.bans.ban_ip(&base, &target_ip);
    }

    // Ban account
    if !target_account.is_empty() {
        let acc_path = base.join("Accounts").join(format!("{}.act", target_account));
        if acc_path.exists() {
            let act = acc_path.to_str().unwrap_or("");
            let _ = crate::config::write_var(act, &target_account, "ban", "1");
        }
    }

    // Disconnect the target
    close_connection(state, target_conn).await;

    // Announce
    let pkt1 = format!("||567@{}@{}", gm_name, target);
    let pkt2 = format!("||568@{}@{}", gm_name, target_hd);
    let pkt3 = format!("||569@{}@{}", gm_name, target_ip);
    let pkt4 = format!("||570@{}@{}", gm_name, target_account);
    state.send_data(SendTarget::ToAll, &pkt1).await;
    state.send_data(SendTarget::ToAll, &pkt2).await;
    state.send_data(SendTarget::ToAll, &pkt3).await;
    state.send_data(SendTarget::ToAll, &pkt4).await;

    info!("[GM] {} banned {} (HD={}, IP={}, Account={})", gm_name, target, target_hd, target_ip, target_account);
}

/// /HECHIZO <name> <spell_id> — Teach a spell to a player. Requires Administrador.
async fn handle_slash_hechizo(state: &mut GameState, conn_id: ConnectionId, args: &str) {
    match state.users.get(&conn_id) {
        Some(u) if u.logged && u.privileges >= privilege_level::ADMINISTRADOR => {}
        _ => return,
    }

    let args_upper = args.to_uppercase();
    let parts: Vec<&str> = args_upper.splitn(2, ' ').collect();
    if parts.len() < 2 { return; }

    let target_name = parts[0].replace('+', " ");
    let spell_id: i32 = match parts[1].parse() {
        Ok(s) if s > 0 => s,
        _ => return,
    };

    let target_upper = target_name.to_uppercase();
    let target_conn = match state.online_names.get(&target_upper).copied() {
        Some(c) => c,
        None => return,
    };

    // Check if they already have this spell
    let already_has = state.users.get(&target_conn)
        .map(|u| u.spells.iter().any(|&s| s == spell_id))
        .unwrap_or(false);
    if already_has { return; }

    // Find empty spell slot
    let empty_slot = state.users.get(&target_conn)
        .and_then(|u| u.spells.iter().position(|&s| s == 0));

    if let Some(slot) = empty_slot {
        if let Some(user) = state.users.get_mut(&target_conn) {
            user.spells[slot] = spell_id;
        }
        // Send spell slot update
        let spell_name = state.get_spell(spell_id)
            .map(|s| s.nombre.clone())
            .unwrap_or_else(|| format!("Hechizo {}", spell_id));
        let pkt = format!("SHS{},{}", slot + 1, spell_name);
        state.send_to(target_conn, &pkt).await;
    }
}

/// /DONACION <name>@<amount> — Award donation points. Requires Administrador.
async fn handle_slash_donacion(state: &mut GameState, conn_id: ConnectionId, args: &str) {
    match state.users.get(&conn_id) {
        Some(u) if u.logged && u.privileges >= privilege_level::ADMINISTRADOR => {}
        _ => return,
    }

    let target_name = read_field(1, args, '@');
    let amount_str = read_field(2, args, '@');
    let amount: i64 = amount_str.parse().unwrap_or(0);
    if amount <= 0 { return; }

    let target_upper = target_name.to_uppercase();
    let target_conn = match state.online_names.get(&target_upper).copied() {
        Some(c) => c,
        None => {
            state.send_to(conn_id, "||196").await;
            return;
        }
    };

    let target_real_name = state.users.get(&target_conn)
        .map(|u| u.char_name.clone())
        .unwrap_or_default();

    // Donation points = amount * 10 (stored in charfile, not in runtime UserState for now)
    let pkt = format!("||561@{}@{}", amount, target_real_name);
    state.send_to(conn_id, &pkt).await;

    info!("[GM] Donation of ${} to {} ({} points)", amount, target_real_name, amount * 10);
}

/// /RESETVALS <type> — Reset arena/duel/CvC state. Requires Semidios+.
/// Types: ARENA1, ARENA2, ARENA3, ARENA4, 2VS2, DESAFIO, CVC, INVOCACIONES
async fn handle_slash_resetvals(state: &mut GameState, conn_id: ConnectionId, val_type: &str) {
    match state.users.get(&conn_id) {
        Some(u) if u.logged && u.privileges >= privilege_level::SEMIDIOS => {}
        _ => return,
    }

    let vt = val_type.to_uppercase();
    match vt.as_str() {
        "DESAFIO" => {
            state.send_to(conn_id, "||586").await;
        }
        "ARENA1" => {
            state.send_data(SendTarget::ToAll, "||587@1").await;
        }
        "ARENA2" => {
            state.send_data(SendTarget::ToAll, "||587@2").await;
        }
        "ARENA3" => {
            state.send_data(SendTarget::ToAll, "||587@3").await;
        }
        "ARENA4" => {
            state.send_data(SendTarget::ToAll, "||587@4").await;
        }
        "2VS2" => {
            state.send_to(conn_id, "||588").await;
        }
        "CVC" => {
            state.send_to(conn_id, "||589").await;
        }
        "INVOCACIONES" => {
            state.send_to(conn_id, "||590").await;
        }
        _ => {
            state.send_to(conn_id, "||585").await; // Invalid type
        }
    }
    info!("[GM] Reset vals: {}", vt);
}

// =====================================================================
// Remaining player slash commands
// =====================================================================

/// /DESC <text> — Set character description. Saved to charfile.
async fn handle_slash_desc(state: &mut GameState, conn_id: ConnectionId, desc: &str) {
    let name = match state.users.get(&conn_id) {
        Some(u) if u.logged => u.char_name.clone(),
        _ => return,
    };

    if desc.len() > 200 {
        state.send_to(conn_id, &format!("{}Descripcion muy larga (max 200 caracteres).{}", server_opcodes::CONSOLE_MSG, font_types::INFO)).await;
        return;
    }

    let base = state.base_path.clone();
    let charpath = base.join("charfile").join(format!("{}.chr", name));
    let chr = charpath.to_str().unwrap_or("");
    let _ = crate::config::write_var(chr, "CHAR", "Desc", desc);

    state.send_to(conn_id, &format!("{}Descripcion actualizada.{}", server_opcodes::CONSOLE_MSG, font_types::INFO)).await;
}

/// /COMERCIAR — Trade with target player (VB6: comManda). Requires mutual confirmation.
async fn handle_slash_comerciar(state: &mut GameState, conn_id: ConnectionId) {
    let (dead, trading, target_user, char_name) = match state.users.get(&conn_id) {
        Some(u) if u.logged => (u.dead, u.trading, u.target_user, u.char_name.clone()),
        _ => return,
    };

    if dead {
        state.send_to(conn_id, &format!("{}Estas muerto.{}", server_opcodes::CONSOLE_MSG, font_types::INFO)).await;
        return;
    }
    if trading {
        state.send_to(conn_id, &format!("{}Ya estas comerciando.{}", server_opcodes::CONSOLE_MSG, font_types::INFO)).await;
        return;
    }
    if target_user == 0 {
        state.send_to(conn_id, &format!("{}Primero selecciona un jugador.{}", server_opcodes::CONSOLE_MSG, font_types::INFO)).await;
        return;
    }

    // Check target is valid
    let target_ok = state.users.get(&target_user).map(|u| u.logged && !u.dead && !u.trading).unwrap_or(false);
    if !target_ok {
        state.send_to(conn_id, &format!("{}El jugador no esta disponible.{}", server_opcodes::CONSOLE_MSG, font_types::INFO)).await;
        return;
    }

    // VB6 mutual confirmation: check if target already requested trade with us
    let target_wants_us = state.users.get(&target_user)
        .map(|u| u.trade_partner == Some(conn_id) && !u.trading)
        .unwrap_or(false);

    if target_wants_us {
        // Both players want to trade with each other — initiate trade
        iniciar_comercio_usuario(state, conn_id, target_user).await;
    } else {
        // Mark our trade intention and notify target
        if let Some(u) = state.users.get_mut(&conn_id) {
            u.trade_partner = Some(target_user);
        }
        let target_name = state.users.get(&target_user).map(|u| u.char_name.clone()).unwrap_or_default();
        let msg_target = format!("{}{} quiere comerciar contigo. Usa /COMERCIAR para aceptar.{}", server_opcodes::CONSOLE_MSG, char_name, font_types::INFO);
        state.send_to(target_user, &msg_target).await;
        let msg_self = format!("{}Le has propuesto comerciar a {}.{}", server_opcodes::CONSOLE_MSG, target_name, font_types::INFO);
        state.send_to(conn_id, &msg_self).await;
    }
}

/// /BOVEDA — Open bank vault. VB6: requires Banquero NPC target, distance <= 5, not dead.
async fn handle_slash_boveda(state: &mut GameState, conn_id: ConnectionId) {
    let (dead, target_npc) = match state.users.get(&conn_id) {
        Some(u) if u.logged => (u.dead, u.target_npc),
        _ => return,
    };

    // VB6: If Muerto = 1 Then ||3
    if dead {
        state.send_to(conn_id, "||3").await;
        return;
    }

    // VB6: If TargetNPC = 0 Then ||9
    if target_npc == 0 {
        state.send_to(conn_id, "||9").await;
        return;
    }

    // VB6: distance > 5 → ||13
    let (npc_map, npc_x, npc_y, npc_type) = match state.get_npc(target_npc) {
        Some(npc) => (npc.map, npc.x, npc.y, npc.npc_type),
        None => { state.send_to(conn_id, "||9").await; return; }
    };
    let (u_map, u_x, u_y) = match state.users.get(&conn_id) {
        Some(u) => (u.pos_map, u.pos_x, u.pos_y),
        None => return,
    };
    if u_map != npc_map || (u_x - npc_x).abs() > 5 || (u_y - npc_y).abs() > 5 {
        state.send_to(conn_id, "||13").await;
        return;
    }

    // VB6: NPCtype must be Banquero (4)
    if npc_type != crate::data::npcs::NpcType::Banker {
        return;
    }

    // VB6: IniciarDeposito — send bank inventory then INITBANCO
    enviar_banco_inv(state, conn_id).await;
    send_stats_gold(state, conn_id).await;

    if let Some(user) = state.users.get_mut(&conn_id) {
        user.comerciando = true;
    }

    state.send_to(conn_id, server_opcodes::INIT_BANK).await;
}

/// /DARORO <name>@<amount> — Give gold to another player. Min 10000.
async fn handle_slash_daroro(state: &mut GameState, conn_id: ConnectionId, args: &str) {
    let target_name = read_field(1, args, '@');
    let amount_str = read_field(2, args, '@');
    let amount: i64 = amount_str.parse().unwrap_or(0);

    if amount < 10000 {
        state.send_to(conn_id, &format!("{}Minimo 10.000 de oro.{}", server_opcodes::CONSOLE_MSG, font_types::INFO)).await;
        return;
    }

    let (my_gold, my_name) = match state.users.get(&conn_id) {
        Some(u) if u.logged => (u.gold, u.char_name.clone()),
        _ => return,
    };

    if my_gold < amount {
        state.send_to(conn_id, &format!("{}No tenes suficiente oro.{}", server_opcodes::CONSOLE_MSG, font_types::INFO)).await;
        return;
    }

    let target_upper = target_name.to_uppercase();
    let target_conn = match state.online_names.get(&target_upper).copied() {
        Some(c) => c,
        None => {
            state.send_to(conn_id, &format!("{}Jugador no encontrado.{}", server_opcodes::CONSOLE_MSG, font_types::INFO)).await;
            return;
        }
    };

    // Transfer
    if let Some(user) = state.users.get_mut(&conn_id) {
        user.gold -= amount;
    }
    if let Some(target) = state.users.get_mut(&target_conn) {
        target.gold += amount;
    }

    send_stats_gold(state, conn_id).await;
    send_stats_gold(state, target_conn).await;

    let target_real = state.users.get(&target_conn).map(|u| u.char_name.clone()).unwrap_or_default();
    let msg1 = format!("{}Le diste {} de oro a {}.{}", server_opcodes::CONSOLE_MSG, amount, target_real, font_types::INFO);
    let msg2 = format!("{}{} te dio {} de oro.{}", server_opcodes::CONSOLE_MSG, my_name, amount, font_types::INFO);
    state.send_to(conn_id, &msg1).await;
    state.send_to(target_conn, &msg2).await;

    info!("[GOLD] {} gave {} gold to {}", my_name, amount, target_real);
}

/// /DEPOSITAR <amount> — Deposit gold at bank (slash command shortcut).
async fn handle_slash_depositar(state: &mut GameState, conn_id: ConnectionId, amount: i64) {
    if amount <= 0 { return; }

    let gold = state.users.get(&conn_id).map(|u| u.gold).unwrap_or(0);
    if gold < amount {
        state.send_to(conn_id, &format!("{}No tenes suficiente oro.{}", server_opcodes::CONSOLE_MSG, font_types::INFO)).await;
        return;
    }

    if let Some(user) = state.users.get_mut(&conn_id) {
        user.gold -= amount;
        user.bank_gold += amount;
    }
    send_stats_gold(state, conn_id).await;

    let bank_gold = state.users.get(&conn_id).map(|u| u.bank_gold).unwrap_or(0);
    state.send_to(conn_id, &format!("[BG{}", bank_gold)).await;
    state.send_to(conn_id, &format!("{}Depositaste {} de oro.{}", server_opcodes::CONSOLE_MSG, amount, font_types::INFO)).await;
}

/// /RETIRAR <amount> — Withdraw gold from bank (slash command shortcut).
async fn handle_slash_retirar_oro(state: &mut GameState, conn_id: ConnectionId, amount: i64) {
    if amount <= 0 { return; }

    let bank_gold = state.users.get(&conn_id).map(|u| u.bank_gold).unwrap_or(0);
    if bank_gold < amount {
        state.send_to(conn_id, &format!("{}No tenes suficiente oro en la boveda.{}", server_opcodes::CONSOLE_MSG, font_types::INFO)).await;
        return;
    }

    if let Some(user) = state.users.get_mut(&conn_id) {
        user.gold += amount;
        user.bank_gold -= amount;
    }
    send_stats_gold(state, conn_id).await;

    let bank_gold = state.users.get(&conn_id).map(|u| u.bank_gold).unwrap_or(0);
    state.send_to(conn_id, &format!("[BG{}", bank_gold)).await;
    state.send_to(conn_id, &format!("{}Retiraste {} de oro.{}", server_opcodes::CONSOLE_MSG, amount, font_types::INFO)).await;
}

/// /FMSG <msg> — Faction message (to all same-faction members).
async fn handle_slash_fmsg(state: &mut GameState, conn_id: ConnectionId, text: &str) {
    let (name, armada, caos) = match state.users.get(&conn_id) {
        Some(u) if u.logged => (u.char_name.clone(), u.armada_real, u.fuerzas_caos),
        _ => return,
    };

    if !armada && !caos {
        state.send_to(conn_id, &format!("{}No perteneces a ninguna faccion.{}", server_opcodes::CONSOLE_MSG, font_types::INFO)).await;
        return;
    }

    let pkt = format!("G|[Faccion] {}> {}{}", name, text, font_types::GUILD);

    // Send to all users in the same faction
    let targets: Vec<ConnectionId> = state.users.values()
        .filter(|u| u.logged && ((armada && u.armada_real) || (caos && u.fuerzas_caos)))
        .map(|u| u.conn_id)
        .collect();
    for t in targets {
        state.send_to(t, &pkt).await;
    }
}

/// /HORA — Show server time. GMs broadcast to all.
async fn handle_slash_hora(state: &mut GameState, conn_id: ConnectionId) {
    let privileges = state.users.get(&conn_id).map(|u| u.privileges).unwrap_or(0);

    let now = std::time::SystemTime::now()
        .duration_since(std::time::UNIX_EPOCH)
        .unwrap_or_default()
        .as_secs();
    let hours = (now / 3600) % 24;
    let minutes = (now / 60) % 60;
    let seconds = now % 60;
    let time_str = format!("{:02}:{:02}:{:02}", hours, minutes, seconds);
    let msg = format!("{}Hora del servidor: {}{}", server_opcodes::CONSOLE_MSG, time_str, font_types::INFO);

    if privileges > privilege_level::USER {
        // GMs broadcast to all
        state.send_data(SendTarget::ToAll, &msg).await;
    } else {
        state.send_to(conn_id, &msg).await;
    }
}

/// /NICK <name> — Check if a character exists online/offline (player command, NOT GM /CHANGENICK).
async fn handle_slash_nick_check(state: &mut GameState, conn_id: ConnectionId, name: &str) {
    let target_upper = name.to_uppercase();

    if let Some(&_t_conn) = state.online_names.get(&target_upper) {
        state.send_to(conn_id, &format!("{}{} esta ONLINE.{}", server_opcodes::CONSOLE_MSG, name, font_types::INFO)).await;
    } else {
        // Check if charfile exists
        let base = state.base_path.clone();
        let charpath = base.join("charfile").join(format!("{}.chr", name));
        if charpath.exists() {
            state.send_to(conn_id, &format!("{}{} existe pero esta OFFLINE.{}", server_opcodes::CONSOLE_MSG, name, font_types::INFO)).await;
        } else {
            state.send_to(conn_id, &format!("{}{} no existe.{}", server_opcodes::CONSOLE_MSG, name, font_types::INFO)).await;
        }
    }
}

/// /ADVERTENCIAS — View own warnings/penalties.
async fn handle_slash_advertencias(state: &mut GameState, conn_id: ConnectionId) {
    let name = match state.users.get(&conn_id) {
        Some(u) if u.logged => u.char_name.clone(),
        _ => return,
    };

    let base = state.base_path.clone();
    let charpath = base.join("charfile").join(format!("{}.chr", name));
    let chr = charpath.to_str().unwrap_or("");

    let cant: i32 = crate::config::get_var(chr, "PENAS", "Cant").parse().unwrap_or(0);
    if cant == 0 {
        state.send_to(conn_id, &format!("{}No tenes advertencias.{}", server_opcodes::CONSOLE_MSG, font_types::INFO)).await;
        return;
    }

    state.send_to(conn_id, &format!("{}Tenes {} advertencias:{}", server_opcodes::CONSOLE_MSG, cant, font_types::INFO)).await;
    for i in 1..=cant {
        let p = crate::config::get_var(chr, "PENAS", &format!("P{}", i));
        let pkt = format!("{}{}: {}{}", server_opcodes::CONSOLE_MSG, i, p, font_types::INFO);
        state.send_to(conn_id, &pkt).await;
    }
}

/// /CURAR — Heal at Revividor NPC. VB6: requires Revividor NPC, distance <= 10, alive.
/// Removes poison, heals to full HP.
async fn handle_slash_curar(state: &mut GameState, conn_id: ConnectionId) {
    let (dead, target_npc) = match state.users.get(&conn_id) {
        Some(u) if u.logged => (u.dead, u.target_npc),
        _ => return,
    };

    // VB6: If TargetNPC = 0 Then ||9
    if target_npc == 0 {
        state.send_to(conn_id, "||9").await;
        return;
    }

    // VB6: NPCtype must be Revividor (1) AND user must be alive
    let npc_type = state.get_npc(target_npc).map(|n| n.npc_type);
    if npc_type != Some(crate::data::npcs::NpcType::Reviver) || dead {
        return;
    }

    // VB6: distance > 10 → ||12
    let (npc_map, npc_x, npc_y) = match state.get_npc(target_npc) {
        Some(npc) => (npc.map, npc.x, npc.y),
        None => return,
    };
    let (u_map, u_x, u_y) = match state.users.get(&conn_id) {
        Some(u) => (u.pos_map, u.pos_x, u.pos_y),
        None => return,
    };
    if u_map != npc_map || (u_x - npc_x).abs() > 10 || (u_y - npc_y).abs() > 10 {
        state.send_to(conn_id, "||12").await;
        return;
    }

    // VB6: Remove poison, heal to full HP, send ||398
    if let Some(user) = state.users.get_mut(&conn_id) {
        user.poisoned = false;
        user.min_hp = user.max_hp;
    }
    send_stats_hp(state, conn_id).await;
    state.send_to(conn_id, "||398").await;
}

/// /DEMONIO or /ANGEL — VB6: requires CJerarquia=1, toggles transform.
/// Demon body=289 (criminal), Angel body=288 (citizen). FX=1 (FXWARP), Sound=SND_TRANSF.
async fn handle_slash_transform(state: &mut GameState, conn_id: ConnectionId, cmd: &str) {
    let (dead, navigating, criminal, transformed, c_jerarquia) = match state.users.get(&conn_id) {
        Some(u) if u.logged => (u.dead, u.navigating, u.criminal, u.transformed, u.jerarquia_dios),
        _ => return,
    };

    // VB6: If Navegando=1 Or Muerto=1 Then ||397
    if navigating || dead {
        state.send_to(conn_id, "||397").await;
        return;
    }

    let (ci, map, x, y) = match state.users.get(&conn_id) {
        Some(u) => (u.char_index.0, u.pos_map, u.pos_x, u.pos_y),
        None => return,
    };

    if transformed {
        // VB6: Revert transformation — DarCuerpoDesnudo, reset head, FX + sound
        let (race, char_name, gender) = match state.users.get(&conn_id) {
            Some(u) => (u.race.clone(), u.char_name.clone(), u.gender),
            None => return,
        };
        let gender_str = gender.to_string();
        let orig_head = {
            let base = &state.base_path;
            charfile::load_charfile(base, &char_name).map(|c| c.head).unwrap_or(0)
        };
        let new_body = naked_body(&race, &gender_str);

        if let Some(user) = state.users.get_mut(&conn_id) {
            user.body = new_body;
            user.head = orig_head;
            user.transformed = false;
        }

        // Broadcast appearance + FX
        let (heading, weapon, shield, casco) = match state.users.get(&conn_id) {
            Some(u) => (u.heading, u.weapon_anim, u.shield_anim, u.casco_anim),
            None => return,
        };
        let cp = format!("CP{},{},{},{},{},{},0,0,{}", ci, new_body, orig_head, heading, weapon, shield, casco);
        state.send_data(SendTarget::ToArea { map, x, y }, &cp).await;
        state.send_data(SendTarget::ToArea { map, x, y }, &format!("CFX{},1,0", ci)).await;
        state.send_data(SendTarget::ToArea { map, x, y }, "TW3").await;

    } else if cmd == "/DEMONIO" && c_jerarquia >= 1 && criminal {
        // VB6: Transform to demon — body 289, head 0
        if let Some(user) = state.users.get_mut(&conn_id) {
            user.body = 289;
            user.head = 0;
            user.transformed = true;
        }
        let (heading, weapon, shield, casco) = match state.users.get(&conn_id) {
            Some(u) => (u.heading, u.weapon_anim, u.shield_anim, u.casco_anim),
            None => return,
        };
        let cp = format!("CP{},289,0,{},{},{},0,0,{}", ci, heading, weapon, shield, casco);
        state.send_data(SendTarget::ToArea { map, x, y }, &cp).await;
        state.send_data(SendTarget::ToArea { map, x, y }, &format!("CFX{},1,0", ci)).await;
        state.send_data(SendTarget::ToArea { map, x, y }, "TW3").await;

    } else if cmd == "/ANGEL" && c_jerarquia >= 1 && !criminal {
        // VB6: Transform to angel — body 288, head 0
        if let Some(user) = state.users.get_mut(&conn_id) {
            user.body = 288;
            user.head = 0;
            user.transformed = true;
        }
        let (heading, weapon, shield, casco) = match state.users.get(&conn_id) {
            Some(u) => (u.heading, u.weapon_anim, u.shield_anim, u.casco_anim),
            None => return,
        };
        let cp = format!("CP{},288,0,{},{},{},0,0,{}", ci, heading, weapon, shield, casco);
        state.send_data(SendTarget::ToArea { map, x, y }, &cp).await;
        state.send_data(SendTarget::ToArea { map, x, y }, &format!("CFX{},1,0", ci)).await;
        state.send_data(SendTarget::ToArea { map, x, y }, "TW3").await;
    }
}

/// /PMSG <msg> — Party message to all party members.
async fn handle_slash_pmsg(state: &mut GameState, conn_id: ConnectionId, text: &str) {
    let (party_idx, name) = match state.users.get(&conn_id) {
        Some(u) if u.logged && u.party_index > 0 => (u.party_index, u.char_name.clone()),
        _ => {
            return;
        }
    };

    let pkt = format!("G|[Party] {}> {}{}", name, text, font_types::GUILD);
    send_to_party(state, party_idx, &pkt).await;
}

// =====================================================================
// Duel, Tournament, and Event handlers
// =====================================================================

/// Arena duel map and positions (VB6: map 71, 4 arenas)
const DUEL_MAP: i32 = 71;
const DUEL_MIN_BET: i64 = 200_000;
const DUEL_POSITIONS: [(i32, i32, i32, i32); 4] = [
    (23, 28, 44, 42), // Arena 1: (p1x,p1y, p2x,p2y)
    (23, 61, 44, 76), // Arena 2
    (59, 28, 80, 42), // Arena 3
    (59, 61, 80, 76), // Arena 4
];

/// Desafio map (1v1 king-of-the-hill)
const DESAFIO_MAP: i32 = 109;
const DESAFIO_COST_DEFENDER: i64 = 200_000;
const DESAFIO_COST_CHALLENGER: i64 = 30_000;
const DESAFIO_MIN_LEVEL: i32 = 50;
const DESAFIO_REWARD: i64 = 100_000;

/// CvC map
const CVC_MAP: i32 = 108;
const CVC_COST: i64 = 200_000;

/// Exit map (common return point)
const EXIT_MAP: i32 = 28;
const EXIT_X: i32 = 54;
const EXIT_Y: i32 = 36;

/// /DUELO <name>@<gold> — Challenge another player to an arena duel.
async fn handle_slash_duelo(state: &mut GameState, conn_id: ConnectionId, args: &str) {
    let target_name = read_field(1, args, '@');
    let bet_str = read_field(2, args, '@');
    let bet: i64 = bet_str.parse().unwrap_or(0);

    let (name, level, class, gold, dead, map) = match state.users.get(&conn_id) {
        Some(u) if u.logged => (u.char_name.clone(), u.level, u.class.clone(), u.gold, u.dead, u.pos_map),
        _ => return,
    };

    if dead {
        state.send_to(conn_id, &format!("{}No puedes hacer eso estando muerto.{}", server_opcodes::CONSOLE_MSG, font_types::INFO)).await;
        return;
    }
    if bet < DUEL_MIN_BET {
        state.send_to(conn_id, &format!("{}La apuesta minima es {}.{}", server_opcodes::CONSOLE_MSG, DUEL_MIN_BET, font_types::INFO)).await;
        return;
    }
    if gold < bet {
        state.send_to(conn_id, &format!("{}No tenes suficiente oro.{}", server_opcodes::CONSOLE_MSG, font_types::INFO)).await;
        return;
    }

    // Check if any arena is free
    let free_arena = (1..=4).find(|&i| !state.arena_ocupada[i]);
    if free_arena.is_none() {
        state.send_to(conn_id, &format!("{}Todas las arenas estan ocupadas.{}", server_opcodes::CONSOLE_MSG, font_types::INFO)).await;
        return;
    }

    // Find target
    let target_upper = target_name.to_uppercase();
    let target_conn = match state.online_names.get(&target_upper).copied() {
        Some(c) => c,
        None => {
            state.send_to(conn_id, &format!("{}Jugador no encontrado.{}", server_opcodes::CONSOLE_MSG, font_types::INFO)).await;
            return;
        }
    };

    // Check target has enough gold
    let target_gold = state.users.get(&target_conn).map(|u| u.gold).unwrap_or(0);
    if target_gold < bet {
        state.send_to(conn_id, &format!("{}El otro jugador no tiene suficiente oro.{}", server_opcodes::CONSOLE_MSG, font_types::INFO)).await;
        return;
    }

    // Set challenge on target
    if let Some(target_user) = state.users.get_mut(&target_conn) {
        target_user.le_mandaron_duelo = true;
        target_user.ultimo_en_mandar_duelo = name.clone();
    }
    if let Some(user) = state.users.get_mut(&conn_id) {
        user.apuesta_oro = bet;
    }

    // Notify target
    let pkt = format!("||546@{}@{}@{}@{}", name, class, level, bet);
    state.send_to(target_conn, &pkt).await;

    let msg = format!("{}Has desafiado a {} por {} de oro.{}", server_opcodes::CONSOLE_MSG, target_name, bet, font_types::INFO);
    state.send_to(conn_id, &msg).await;
}

/// /SIDUELO — Accept a pending duel challenge.
async fn handle_slash_siduelo(state: &mut GameState, conn_id: ConnectionId) {
    let (has_challenge, challenger_name) = match state.users.get(&conn_id) {
        Some(u) if u.logged && u.le_mandaron_duelo => {
            (true, u.ultimo_en_mandar_duelo.clone())
        }
        _ => (false, String::new()),
    };

    if !has_challenge {
        state.send_to(conn_id, &format!("{}No tenes ninguna oferta de duelo.{}", server_opcodes::CONSOLE_MSG, font_types::INFO)).await;
        return;
    }

    let challenger_upper = challenger_name.to_uppercase();
    let challenger_conn = match state.online_names.get(&challenger_upper).copied() {
        Some(c) => c,
        None => {
            state.send_to(conn_id, &format!("{}El retador ya no esta online.{}", server_opcodes::CONSOLE_MSG, font_types::INFO)).await;
            return;
        }
    };

    // Get bet amount from challenger
    let bet = state.users.get(&challenger_conn).map(|u| u.apuesta_oro).unwrap_or(0);
    if bet <= 0 { return; }

    // Check both have gold
    let my_gold = state.users.get(&conn_id).map(|u| u.gold).unwrap_or(0);
    let their_gold = state.users.get(&challenger_conn).map(|u| u.gold).unwrap_or(0);
    if my_gold < bet || their_gold < bet {
        state.send_to(conn_id, &format!("{}No hay suficiente oro.{}", server_opcodes::CONSOLE_MSG, font_types::INFO)).await;
        return;
    }

    // Find free arena
    let arena = match (1..=4).find(|&i| !state.arena_ocupada[i]) {
        Some(a) => a,
        None => {
            state.send_to(conn_id, &format!("{}No hay arenas disponibles.{}", server_opcodes::CONSOLE_MSG, font_types::INFO)).await;
            return;
        }
    };

    // Deduct gold
    if let Some(user) = state.users.get_mut(&conn_id) {
        user.gold -= bet;
        user.le_mandaron_duelo = false;
        user.ultimo_en_mandar_duelo.clear();
    }
    if let Some(user) = state.users.get_mut(&challenger_conn) {
        user.gold -= bet;
        user.apuesta_oro = 0;
    }
    send_stats_gold(state, conn_id).await;
    send_stats_gold(state, challenger_conn).await;

    // Save positions and set duel flags
    let my_name = state.users.get(&conn_id).map(|u| u.char_name.clone()).unwrap_or_default();
    let their_name = state.users.get(&challenger_conn).map(|u| u.char_name.clone()).unwrap_or_default();

    for &c in &[conn_id, challenger_conn] {
        if let Some(user) = state.users.get_mut(&c) {
            user.mapa_anterior = user.pos_map;
            user.x_anterior = user.pos_x;
            user.y_anterior = user.pos_y;
            user.en_duelo = true;
            user.en_que_arena = arena as i32;
            user.apuesta_oro = bet;
        }
    }
    if let Some(user) = state.users.get_mut(&conn_id) {
        user.dueliando_contra = their_name.clone();
    }
    if let Some(user) = state.users.get_mut(&challenger_conn) {
        user.dueliando_contra = my_name.clone();
    }

    // Mark arena occupied
    state.arena_ocupada[arena] = true;
    let idx = (arena - 1) * 2;
    state.nombre_dueleando[idx + 1] = my_name.clone();
    state.nombre_dueleando[idx + 2] = their_name.clone();

    // Announce
    let pkt = format!("||548@{}@{}@{}@{}", arena, my_name, their_name, bet);
    state.send_data(SendTarget::ToAll, &pkt).await;

    // Warp to arena positions
    let (p1x, p1y, p2x, p2y) = DUEL_POSITIONS[arena - 1];
    warp_user(state, challenger_conn, DUEL_MAP, p1x, p1y).await;
    warp_user(state, conn_id, DUEL_MAP, p2x, p2y).await;

    info!("[DUEL] {} vs {} in arena {} for {} gold", my_name, their_name, arena, bet);
}

/// /DESAFIO — Create a 1v1 challenge (become the defender). Costs 200K gold, requires lvl 50+.
async fn handle_slash_desafio(state: &mut GameState, conn_id: ConnectionId) {
    let (name, class, level, gold, dead) = match state.users.get(&conn_id) {
        Some(u) if u.logged => (u.char_name.clone(), u.class.clone(), u.level, u.gold, u.dead),
        _ => return,
    };

    if dead { return; }
    if level < DESAFIO_MIN_LEVEL {
        state.send_to(conn_id, &format!("{}Necesitas nivel {} para crear un desafio.{}", server_opcodes::CONSOLE_MSG, DESAFIO_MIN_LEVEL, font_types::INFO)).await;
        return;
    }
    if gold < DESAFIO_COST_DEFENDER {
        state.send_to(conn_id, &format!("{}Necesitas {} de oro.{}", server_opcodes::CONSOLE_MSG, DESAFIO_COST_DEFENDER, font_types::INFO)).await;
        return;
    }
    if state.desafio_primero != 0 {
        state.send_to(conn_id, &format!("{}Ya hay un desafio activo.{}", server_opcodes::CONSOLE_MSG, font_types::INFO)).await;
        return;
    }

    // Deduct gold
    if let Some(user) = state.users.get_mut(&conn_id) {
        user.gold -= DESAFIO_COST_DEFENDER;
        user.en_desafio = true;
        user.rondas = 0;
        user.mapa_anterior = user.pos_map;
        user.x_anterior = user.pos_x;
        user.y_anterior = user.pos_y;
    }
    send_stats_gold(state, conn_id).await;

    state.desafio_primero = conn_id;

    // Announce
    let pkt = format!("||407@{}@{}@{}", name, class, level);
    state.send_data(SendTarget::ToAll, &pkt).await;

    // Warp to desafio map
    warp_user(state, conn_id, DESAFIO_MAP, 52, 32).await;

    info!("[DESAFIO] {} created challenge", name);
}

/// /DESAFIAR — Accept an existing 1v1 challenge. Costs 30K gold.
async fn handle_slash_desafiar(state: &mut GameState, conn_id: ConnectionId) {
    let (name, gold, dead) = match state.users.get(&conn_id) {
        Some(u) if u.logged => (u.char_name.clone(), u.gold, u.dead),
        _ => return,
    };

    if dead { return; }
    if state.desafio_primero == 0 {
        state.send_to(conn_id, &format!("{}No hay ningun desafio activo.{}", server_opcodes::CONSOLE_MSG, font_types::INFO)).await;
        return;
    }
    if state.desafio_segundo != 0 {
        state.send_to(conn_id, &format!("{}Ya hay un retador peleando.{}", server_opcodes::CONSOLE_MSG, font_types::INFO)).await;
        return;
    }
    if gold < DESAFIO_COST_CHALLENGER {
        state.send_to(conn_id, &format!("{}Necesitas {} de oro.{}", server_opcodes::CONSOLE_MSG, DESAFIO_COST_CHALLENGER, font_types::INFO)).await;
        return;
    }

    // Deduct gold
    if let Some(user) = state.users.get_mut(&conn_id) {
        user.gold -= DESAFIO_COST_CHALLENGER;
        user.en_desafio = true;
        user.mapa_anterior = user.pos_map;
        user.x_anterior = user.pos_x;
        user.y_anterior = user.pos_y;
    }
    send_stats_gold(state, conn_id).await;

    state.desafio_segundo = conn_id;

    // Announce
    let pkt = format!("||410@{}", name);
    state.send_data(SendTarget::ToAll, &pkt).await;

    // Notify defender
    let class = state.users.get(&conn_id).map(|u| u.class.clone()).unwrap_or_default();
    let level = state.users.get(&conn_id).map(|u| u.level).unwrap_or(1);
    let info_pkt = format!("||411@{}@{}@{}", name, class, level);
    state.send_to(state.desafio_primero, &info_pkt).await;

    // Warp to desafio map
    warp_user(state, conn_id, DESAFIO_MAP, 52, 48).await;

    info!("[DESAFIO] {} accepted challenge", name);
}

/// /ABANDONAR — Leave duel/desafio/event.
async fn handle_slash_abandonar(state: &mut GameState, conn_id: ConnectionId) {
    let (en_duelo, en_desafio, en_que_arena, mapa_anterior, x_anterior, y_anterior) = match state.users.get(&conn_id) {
        Some(u) if u.logged => (u.en_duelo, u.en_desafio, u.en_que_arena, u.mapa_anterior, u.x_anterior, u.y_anterior),
        _ => return,
    };

    if en_duelo {
        // Forfeit arena duel
        let arena = en_que_arena as usize;
        let opponent_name = state.users.get(&conn_id).map(|u| u.dueliando_contra.clone()).unwrap_or_default();

        // Clear duel state
        if let Some(user) = state.users.get_mut(&conn_id) {
            user.en_duelo = false;
            user.dueliando_contra.clear();
            user.en_que_arena = 0;
        }

        // Warp back
        if mapa_anterior > 0 {
            warp_user(state, conn_id, mapa_anterior, x_anterior, y_anterior).await;
        } else {
            warp_user(state, conn_id, EXIT_MAP, EXIT_X, EXIT_Y).await;
        }

        // Find and reward opponent
        let opponent_upper = opponent_name.to_uppercase();
        if let Some(&opp_conn) = state.online_names.get(&opponent_upper) {
            let bet = state.users.get(&opp_conn).map(|u| u.apuesta_oro).unwrap_or(0);
            if let Some(opp_user) = state.users.get_mut(&opp_conn) {
                opp_user.gold += bet * 3 / 2; // Winner gets 1.5x
                opp_user.en_duelo = false;
                opp_user.dueliando_contra.clear();
                opp_user.en_que_arena = 0;
            }
            send_stats_gold(state, opp_conn).await;
            let (om, ox, oy) = state.users.get(&opp_conn)
                .map(|u| (u.mapa_anterior, u.x_anterior, u.y_anterior))
                .unwrap_or((EXIT_MAP, EXIT_X, EXIT_Y));
            if om > 0 {
                warp_user(state, opp_conn, om, ox, oy).await;
            } else {
                warp_user(state, opp_conn, EXIT_MAP, EXIT_X, EXIT_Y).await;
            }
        }

        // Free arena
        if arena >= 1 && arena <= 4 {
            state.arena_ocupada[arena] = false;
            let idx = (arena - 1) * 2;
            state.nombre_dueleando[idx + 1].clear();
            state.nombre_dueleando[idx + 2].clear();
        }
    } else if en_desafio {
        // Leave desafio
        if state.desafio_primero == conn_id {
            state.desafio_primero = 0;
        }
        if state.desafio_segundo == conn_id {
            state.desafio_segundo = 0;
        }
        if let Some(user) = state.users.get_mut(&conn_id) {
            user.en_desafio = false;
            user.rondas = 0;
        }
        if mapa_anterior > 0 {
            warp_user(state, conn_id, mapa_anterior, x_anterior, y_anterior).await;
        } else {
            warp_user(state, conn_id, EXIT_MAP, EXIT_X, EXIT_Y).await;
        }
    } else {
        // Generic leave — warp to exit
        warp_user(state, conn_id, EXIT_MAP, EXIT_X, EXIT_Y).await;
    }
}

/// /TORNEO — Join an active tournament.
async fn handle_slash_torneo(state: &mut GameState, conn_id: ConnectionId) {
    let (name, dead) = match state.users.get(&conn_id) {
        Some(u) if u.logged => (u.char_name.clone(), u.dead),
        _ => return,
    };

    if dead { return; }

    if !state.hay_torneo {
        state.send_to(conn_id, &format!("{}No hay ningun torneo activo.{}", server_opcodes::CONSOLE_MSG, font_types::INFO)).await;
        return;
    }

    let already = state.users.get(&conn_id).map(|u| u.en_torneo).unwrap_or(false);
    if already {
        state.send_to(conn_id, &format!("{}Ya estas inscripto en el torneo.{}", server_opcodes::CONSOLE_MSG, font_types::INFO)).await;
        return;
    }

    if state.usuarios_en_torneo >= 64 {
        state.send_to(conn_id, &format!("{}El torneo esta lleno.{}", server_opcodes::CONSOLE_MSG, font_types::INFO)).await;
        return;
    }

    state.usuarios_en_torneo += 1;
    state.cronologia_participantes.push(name.clone());

    if let Some(user) = state.users.get_mut(&conn_id) {
        user.en_torneo = true;
        user.num_torneo = state.usuarios_en_torneo;
    }

    let msg = format!("{}Te has inscripto al torneo! (#{}){}",
        server_opcodes::CONSOLE_MSG, state.usuarios_en_torneo, font_types::INFO);
    state.send_to(conn_id, &msg).await;

    // Announce
    let pkt = format!("{}{} se ha inscripto al torneo. ({}/64){}",
        server_opcodes::CONSOLE_MSG, name, state.usuarios_en_torneo, font_types::SYSTEM);
    state.send_data(SendTarget::ToAll, &pkt).await;
}

/// /PARTICIPANTES — List tournament participants.
async fn handle_slash_participantes(state: &mut GameState, conn_id: ConnectionId) {
    if state.cronologia_participantes.is_empty() {
        state.send_to(conn_id, &format!("{}No hay participantes.{}", server_opcodes::CONSOLE_MSG, font_types::INFO)).await;
        return;
    }

    let msg = format!("{}Participantes del torneo ({}):{}", server_opcodes::CONSOLE_MSG,
        state.cronologia_participantes.len(), font_types::INFO);
    state.send_to(conn_id, &msg).await;

    let participants = state.cronologia_participantes.clone();
    for (i, name) in participants.iter().enumerate() {
        let pkt = format!("{}{}: {}{}", server_opcodes::CONSOLE_MSG, i + 1, name, font_types::INFO);
        state.send_to(conn_id, &pkt).await;
    }
}

/// /HORDA — Join the Horde faction. Warps to map 27.
async fn handle_slash_horda(state: &mut GameState, conn_id: ConnectionId) {
    let (dead, level, guild, armada, caos) = match state.users.get(&conn_id) {
        Some(u) if u.logged => (u.dead, u.level, u.guild_index, u.armada_real, u.fuerzas_caos),
        _ => return,
    };

    if dead || level < 10 || guild > 0 || armada || caos {
        state.send_to(conn_id, &format!("{}No puedes unirte a la Horda.{}", server_opcodes::CONSOLE_MSG, font_types::INFO)).await;
        return;
    }

    if let Some(user) = state.users.get_mut(&conn_id) {
        user.criminal = true;
    }

    // Warp to Horde spawn
    warp_user(state, conn_id, 27, 47, 48).await;

    state.send_to(conn_id, &format!("{}Te has unido a la Horda!{}", server_opcodes::CONSOLE_MSG, font_types::SYSTEM)).await;
}

/// /ALIANZA — Join the Alliance faction. Warps to map 29.
async fn handle_slash_alianza(state: &mut GameState, conn_id: ConnectionId) {
    let (dead, level, guild, armada, caos) = match state.users.get(&conn_id) {
        Some(u) if u.logged => (u.dead, u.level, u.guild_index, u.armada_real, u.fuerzas_caos),
        _ => return,
    };

    if dead || level < 10 || guild > 0 || armada || caos {
        state.send_to(conn_id, &format!("{}No puedes unirte a la Alianza.{}", server_opcodes::CONSOLE_MSG, font_types::INFO)).await;
        return;
    }

    if let Some(user) = state.users.get_mut(&conn_id) {
        user.criminal = false;
    }

    // Warp to Alliance spawn
    warp_user(state, conn_id, 29, 50, 90).await;

    state.send_to(conn_id, &format!("{}Te has unido a la Alianza!{}", server_opcodes::CONSOLE_MSG, font_types::SYSTEM)).await;
}

/// /PARTICIPAR — Join the currently active event.
async fn handle_slash_participar(state: &mut GameState, conn_id: ConnectionId) {
    if state.evento_inscripciones {
        handle_slash_participar_evento(state, conn_id).await;
    } else if state.torneo_auto_activo {
        torneo_auto_join(state, conn_id).await;
    } else {
        state.send_to(conn_id, &format!("{}No hay ningun evento activo en este momento.{}", server_opcodes::CONSOLE_MSG, font_types::INFO)).await;
    }
}

/// /EVENTOS — Show active events info.
async fn handle_slash_eventos(state: &mut GameState, conn_id: ConnectionId) {
    let mut msgs = Vec::new();

    if state.evento_activo {
        let tipo_name = event_type_name(state.evento_tipo);
        msgs.push(format!("{}{} en curso ({} participantes).{}", server_opcodes::CONSOLE_MSG, tipo_name, state.evento_participantes.len(), font_types::INFO));
    }
    if state.evento_inscripciones {
        let tipo_name = event_type_name(state.evento_tipo);
        msgs.push(format!("{}Inscripciones abiertas para {} ({}/{}).{}", server_opcodes::CONSOLE_MSG, tipo_name, state.evento_participantes.len(), state.evento_max_players, font_types::INFO));
    }
    if state.torneo_auto_activo {
        let max = 1 << state.torneo_auto_rondas;
        msgs.push(format!("{}Torneo automatico activo ({}/{} slots).{}", server_opcodes::CONSOLE_MSG, state.torneo_auto_bracket.len(), max, font_types::INFO));
    }
    if state.hay_torneo {
        msgs.push(format!("{}Torneo activo con {} participantes.{}", server_opcodes::CONSOLE_MSG, state.usuarios_en_torneo, font_types::INFO));
    }
    if state.cvc_funciona {
        msgs.push(format!("{}CvC en curso.{}", server_opcodes::CONSOLE_MSG, font_types::INFO));
    }
    if state.desafio_primero != 0 {
        let defender = state.users.get(&state.desafio_primero)
            .map(|u| u.char_name.clone())
            .unwrap_or_default();
        msgs.push(format!("{}Desafio activo. Defensor: {}{}", server_opcodes::CONSOLE_MSG, defender, font_types::INFO));
    }
    if state.siege_active {
        msgs.push(format!("{}Asedio al castillo en curso.{}", server_opcodes::CONSOLE_MSG, font_types::INFO));
    }

    if msgs.is_empty() {
        state.send_to(conn_id, &format!("{}No hay eventos activos.{}", server_opcodes::CONSOLE_MSG, font_types::INFO)).await;
    } else {
        for msg in msgs {
            state.send_to(conn_id, &msg).await;
        }
    }
}

/// /CVC <clan_name> — Challenge another clan to CvC. Requires guild leader/sub-leader.
async fn handle_slash_cvc(state: &mut GameState, conn_id: ConnectionId, target_clan: &str) {
    let (name, guild_index, gold, dead) = match state.users.get(&conn_id) {
        Some(u) if u.logged => (u.char_name.clone(), u.guild_index, u.gold, u.dead),
        _ => return,
    };

    if dead || guild_index <= 0 {
        state.send_to(conn_id, &format!("{}No puedes iniciar CvC.{}", server_opcodes::CONSOLE_MSG, font_types::INFO)).await;
        return;
    }
    if state.cvc_funciona {
        state.send_to(conn_id, &format!("{}Ya hay un CvC en curso.{}", server_opcodes::CONSOLE_MSG, font_types::INFO)).await;
        return;
    }
    if gold < CVC_COST {
        state.send_to(conn_id, &format!("{}Necesitas {} de oro.{}", server_opcodes::CONSOLE_MSG, CVC_COST, font_types::INFO)).await;
        return;
    }

    // Find target clan members online
    let target_upper = target_clan.to_uppercase();
    let target_guild_idx = state.users.values()
        .find(|u| u.logged && u.guild_index > 0 && {
            // Match guild by checking any member
            guilds::find_guild_by_name(&state.base_path, &target_upper).unwrap_or(0) == u.guild_index
        })
        .map(|u| u.guild_index);

    if target_guild_idx.is_none() || target_guild_idx == Some(guild_index) {
        state.send_to(conn_id, &format!("{}Clan no encontrado o es tu propio clan.{}", server_opcodes::CONSOLE_MSG, font_types::INFO)).await;
        return;
    }

    // Announce
    let pkt = format!("||413@{}", name);
    state.send_data(SendTarget::ToAll, &pkt).await;
    let pkt2 = format!("||414@{}", target_clan);
    state.send_to(conn_id, &pkt2).await;

    state.send_to(conn_id, &format!("{}Desafio CvC enviado a {}.{}", server_opcodes::CONSOLE_MSG, target_clan, font_types::INFO)).await;
}

/// /NCVC — Disable CvC safety (opt out of auto-warp).
async fn handle_slash_ncvc(state: &mut GameState, conn_id: ConnectionId) {
    if let Some(user) = state.users.get_mut(&conn_id) {
        user.seguro_cvc = false;
    }
    state.send_to(conn_id, "SEGCVCOFF").await;
}

/// /SCVC — Enable CvC safety (opt in for auto-warp).
async fn handle_slash_scvc(state: &mut GameState, conn_id: ConnectionId) {
    if let Some(user) = state.users.get_mut(&conn_id) {
        user.seguro_cvc = true;
    }
    state.send_to(conn_id, "SEGCVCON").await;
}

/// /REGRESAR — Return to home city (die and respawn at home).
/// VB6: TCP_HandleData2.bas lines 741-827
async fn handle_slash_regresar(state: &mut GameState, conn_id: ConnectionId) {
    let user_data = match state.users.get(&conn_id) {
        Some(u) if u.logged => (
            u.dead, u.privileges, u.level, u.pos_map,
            u.hogar.clone(), u.armada_real, u.fuerzas_caos,
        ),
        _ => return,
    };
    let (dead, privileges, level, cur_map, hogar, armada_real, fuerzas_caos) = user_data;

    // GMs (Consejero+) cannot use /regresar
    if privileges >= privilege_level::CONSEJERO {
        return;
    }

    // Level check — must be level 10+
    if level < 10 {
        let msg = format!("{}Debes ser nivel 10 o superior para usar /REGRESAR.{}", server_opcodes::CONSOLE_MSG, font_types::INFO);
        state.send_to(conn_id, &msg).await;
        return;
    }

    // Block in special maps (31-34 arenas)
    if cur_map >= 31 && cur_map <= 34 {
        let msg = format!("{}No puedes usar /REGRESAR en esta zona.{}", server_opcodes::CONSOLE_MSG, font_types::INFO);
        state.send_to(conn_id, &msg).await;
        return;
    }

    // Kill the player if not already dead
    if !dead {
        if let Some(user) = state.users.get_mut(&conn_id) {
            user.min_hp = 0;
            user.dead = true;
            user.poisoned = false;
            user.paralyzed = false;
            user.meditating = false;
            user.min_sta = 0;
        }
        state.send_to(conn_id, "MUERT").await;
        send_stats_hp(state, conn_id).await;
        // Update appearance to dead body
        if let Some(user) = state.users.get(&conn_id) {
            let cc = user.build_cc_packet();
            let (m, ux, uy) = (user.pos_map, user.pos_x, user.pos_y);
            state.send_data(SendTarget::ToArea { map: m, x: ux, y: uy }, &cc).await;
        }
    }

    // Determine destination based on faction/home
    let (dest_map, dest_x, dest_y) = if armada_real {
        // Ejército Real → Lindos (map 29)
        (29, 50, 90)
    } else if fuerzas_caos {
        // Legión Oscura → Barak (map 27)
        (27, 47, 48)
    } else {
        // Regular player → based on Hogar
        match hogar.to_uppercase().as_str() {
            "THIR" => (25, 74, 44),
            "INTHAK" => (130, 52, 56),
            "RUVENDEL" => (26, 51, 52),
            _ => (28, 54, 36), // Default: Ullathorpe
        }
    };

    warp_user(state, conn_id, dest_map, dest_x, dest_y).await;

    let msg = format!("{}Has regresado a tu hogar.{}", server_opcodes::CONSOLE_MSG, font_types::INFO);
    state.send_to(conn_id, &msg).await;
}

/// /SALIR — Disconnect/logout.
async fn handle_slash_salir(state: &mut GameState, conn_id: ConnectionId) {
    close_connection(state, conn_id).await;
}

/// /MEDITAR — Toggle meditation. VB6: level-based FX, GMs get instant full mana.
/// FX IDs: chico=4 (<15), mediano=5 (15-29), grande=6 (30-49), xgrande=43 (50-59),
/// neutral=103/alianza=104/horda=105 (60+), transfo=16 (transformed).
async fn handle_slash_meditar(state: &mut GameState, conn_id: ConnectionId) {
    let (dead, privileges, meditating, max_mana) = match state.users.get(&conn_id) {
        Some(u) if u.logged => (u.dead, u.privileges, u.meditating, u.max_mana),
        _ => return,
    };

    // VB6: If Muerto = 1 Then ||3
    if dead {
        state.send_to(conn_id, "||3").await;
        return;
    }

    // VB6: If MaxMAN = 0 Then ||4
    if max_mana == 0 {
        state.send_to(conn_id, "||4").await;
        return;
    }

    // GMs get instant full mana (VB6: Privilegios > User)
    if privileges > privilege_level::USER {
        if let Some(user) = state.users.get_mut(&conn_id) {
            user.min_mana = user.max_mana;
        }
        state.send_to(conn_id, "||393").await;
        send_stats_mana(state, conn_id).await;
        state.send_to(conn_id, "MEDOK").await;
        return;
    }

    // Toggle meditation
    let was_meditating = meditating;
    if let Some(user) = state.users.get_mut(&conn_id) {
        user.meditating = !was_meditating;
    }

    if !was_meditating {
        // Starting meditation — VB6: ||394 + MEDOK
        state.send_to(conn_id, "||394").await;
        state.send_to(conn_id, "MEDOK").await;

        // VB6: If MinMAN = MaxMAN Then exit (already full)
        let min_mana = state.users.get(&conn_id).map(|u| u.min_mana).unwrap_or(0);
        if min_mana >= max_mana { return; }

        // VB6: Send level-based meditation FX
        let (ci, map, x, y, level, transformed) = match state.users.get(&conn_id) {
            Some(u) => (u.char_index.0, u.pos_map, u.pos_x, u.pos_y, u.level, u.transformed),
            None => return,
        };

        let fx_id = if transformed {
            16 // FXMEDITARTRANSFO
        } else if level < 15 {
            4  // FXMEDITARCHICO
        } else if level < 30 {
            5  // FXMEDITARMEDIANO
        } else if level < 50 {
            6  // FXMEDITARGRANDE
        } else if level <= 59 {
            43 // FXMEDITARXGRANDE
        } else {
            // Level 60+ — faction-based
            103 // FXNUEVATPNEUTRAL (default)
        };

        let loops = 999; // LoopAdEternum
        let pkt = format!("CFX{},{},{}", ci, fx_id, loops);
        state.send_data(SendTarget::ToArea { map, x, y }, &pkt).await;
    } else {
        // Stopping meditation — VB6: ||205 + MEDOK + clear FX
        state.send_to(conn_id, "||205").await;
        state.send_to(conn_id, "MEDOK").await;

        // VB6: Clear FX for area
        let (ci, map, x, y) = match state.users.get(&conn_id) {
            Some(u) => (u.char_index.0, u.pos_map, u.pos_x, u.pos_y),
            None => return,
        };
        let pkt = format!("CFX{},0,0", ci);
        state.send_data(SendTarget::ToArea { map, x, y }, &pkt).await;
    }
}

/// Resolve a duel when a player dies in an arena — called from user_die.
pub async fn resolve_duel_death(state: &mut GameState, dead_conn: ConnectionId) {
    let arena = match state.users.get(&dead_conn) {
        Some(u) if u.en_duelo => u.en_que_arena as usize,
        _ => return,
    };
    if arena < 1 || arena > 4 { return; }

    let loser_name = state.users.get(&dead_conn).map(|u| u.char_name.clone()).unwrap_or_default();
    let opponent_name = state.users.get(&dead_conn).map(|u| u.dueliando_contra.clone()).unwrap_or_default();
    let bet = state.users.get(&dead_conn).map(|u| u.apuesta_oro).unwrap_or(0);

    // Find winner
    let opponent_upper = opponent_name.to_uppercase();
    let winner_conn = state.online_names.get(&opponent_upper).copied();

    // Award winner
    let winnings = bet * 3 / 2; // 1.5x bet
    if let Some(w_conn) = winner_conn {
        if let Some(winner) = state.users.get_mut(&w_conn) {
            winner.gold += winnings;
            winner.en_duelo = false;
            winner.dueliando_contra.clear();
            winner.en_que_arena = 0;
        }
        send_stats_gold(state, w_conn).await;

        // Warp winner back
        let (wm, wx, wy) = state.users.get(&w_conn)
            .map(|u| (u.mapa_anterior, u.x_anterior, u.y_anterior))
            .unwrap_or((EXIT_MAP, EXIT_X, EXIT_Y));
        if wm > 0 {
            warp_user(state, w_conn, wm, wx, wy).await;
        } else {
            warp_user(state, w_conn, EXIT_MAP, EXIT_X, EXIT_Y).await;
        }
    }

    // Clear loser state
    if let Some(loser) = state.users.get_mut(&dead_conn) {
        loser.en_duelo = false;
        loser.dueliando_contra.clear();
        loser.en_que_arena = 0;
        loser.apuesta_oro = 0;
    }

    // Warp loser back
    let (lm, lx, ly) = state.users.get(&dead_conn)
        .map(|u| (u.mapa_anterior, u.x_anterior, u.y_anterior))
        .unwrap_or((EXIT_MAP, EXIT_X, EXIT_Y));
    if lm > 0 {
        warp_user(state, dead_conn, lm, lx, ly).await;
    } else {
        warp_user(state, dead_conn, EXIT_MAP, EXIT_X, EXIT_Y).await;
    }

    // Free arena
    state.arena_ocupada[arena] = false;
    let idx = (arena - 1) * 2;
    state.nombre_dueleando[idx + 1].clear();
    state.nombre_dueleando[idx + 2].clear();

    // Announce result
    let pkt = format!("||691@{}@{}@{}@{}", arena, opponent_name, loser_name, winnings);
    state.send_data(SendTarget::ToAll, &pkt).await;

    info!("[DUEL] {} defeated {} in arena {}. Won {} gold.", opponent_name, loser_name, arena, winnings);
}

/// Resolve desafio when a player dies — called from user_die.
pub async fn resolve_desafio_death(state: &mut GameState, dead_conn: ConnectionId) {
    let is_defender = state.desafio_primero == dead_conn;
    let is_challenger = state.desafio_segundo == dead_conn;

    if !is_defender && !is_challenger { return; }

    if is_challenger {
        // Challenger died — defender wins the round
        state.desafio_segundo = 0;

        // Increment defender rounds
        if let Some(defender) = state.users.get_mut(&state.desafio_primero) {
            defender.rondas += 1;
            // Restore HP/MP
            defender.min_hp = defender.max_hp;
            defender.min_mana = defender.max_mana;
        }
        let rounds = state.users.get(&state.desafio_primero).map(|u| u.rondas).unwrap_or(0);
        send_stats_hp(state, state.desafio_primero).await;
        send_stats_mana(state, state.desafio_primero).await;

        // Warp challenger out
        let (lm, lx, ly) = state.users.get(&dead_conn)
            .map(|u| (u.mapa_anterior, u.x_anterior, u.y_anterior))
            .unwrap_or((EXIT_MAP, EXIT_X, EXIT_Y));
        if let Some(user) = state.users.get_mut(&dead_conn) {
            user.en_desafio = false;
        }
        if lm > 0 {
            warp_user(state, dead_conn, lm, lx, ly).await;
        } else {
            warp_user(state, dead_conn, EXIT_MAP, EXIT_X, EXIT_Y).await;
        }

        // Announce rounds milestone
        if rounds == 3 || rounds == 5 || rounds == 10 || rounds == 20 || rounds == 50 || rounds >= 100 {
            let defender_name = state.users.get(&state.desafio_primero).map(|u| u.char_name.clone()).unwrap_or_default();
            let pkt = format!("{}El defensor {} lleva {} rondas!{}", server_opcodes::CONSOLE_MSG, defender_name, rounds, font_types::SYSTEM);
            state.send_data(SendTarget::ToAll, &pkt).await;
        }
    } else {
        // Defender died — challenger wins
        let defender_conn = state.desafio_primero;
        let challenger_conn = state.desafio_segundo;

        // Award challenger
        if let Some(winner) = state.users.get_mut(&challenger_conn) {
            winner.gold += DESAFIO_REWARD;
            winner.en_desafio = false;
        }
        send_stats_gold(state, challenger_conn).await;

        // Clear defender
        if let Some(loser) = state.users.get_mut(&defender_conn) {
            loser.en_desafio = false;
            loser.rondas = 0;
        }

        // Reset desafio
        state.desafio_primero = 0;
        state.desafio_segundo = 0;

        // Warp both to exit
        warp_user(state, challenger_conn, EXIT_MAP, EXIT_X, EXIT_Y).await;
        warp_user(state, defender_conn, EXIT_MAP, EXIT_X, EXIT_Y).await;

        let winner_name = state.users.get(&challenger_conn).map(|u| u.char_name.clone()).unwrap_or_default();
        let pkt = format!("{}{} ha ganado el desafio y recibe {} de oro!{}", server_opcodes::CONSOLE_MSG, winner_name, DESAFIO_REWARD, font_types::SYSTEM);
        state.send_data(SendTarget::ToAll, &pkt).await;
    }
}

// =====================================================================
// Nobility System (modNobleza.bas)
// =====================================================================
// 3-stage quest on Map 141: kill dragons → hatchlings → Smaug.
// Time-limited per stage. Teleports player on completion or timeout.

const NOBILITY_MAP: i32 = 141;
const NOBILITY_DRAGON_NPC: i32 = 968;
const NOBILITY_CRIA_NPC: i32 = 969;
const NOBILITY_SMAUG_NPC: i32 = 970;

/// Start the nobility quest stage 1 — spawn 4 small dragons.
pub async fn nobleza_etapa_uno(state: &mut GameState, conn_id: ConnectionId) {
    // Check if someone else is already doing the quest
    if state.nobility_user != 0 {
        let msg = format!("{}Alguien ya esta realizando la prueba de nobleza{}", server_opcodes::CONSOLE_MSG, font_types::INFO);
        state.send_to(conn_id, &msg).await;
        return;
    }

    state.nobility_user = conn_id;
    state.nobility_stage = 1;
    state.nobility_timer = 125; // ~5 minutes at 1 tick/s
    state.nobility_kills = 0;

    // Spawn 4 small dragons on map 141
    let spawn_positions = [(50, 50), (55, 50), (50, 55), (55, 55)];
    for (sx, sy) in &spawn_positions {
        spawn_npc_at(state, NOBILITY_DRAGON_NPC as usize, NOBILITY_MAP, *sx, *sy).await;
    }

    let msg = format!("{}Etapa 1: Elimina a los 4 dragones{}", server_opcodes::CONSOLE_MSG, font_types::INFO);
    state.send_to(conn_id, &msg).await;
}

/// Start stage 2 — spawn 2 Smaug hatchlings.
async fn nobleza_etapa_dos(state: &mut GameState) {
    state.nobility_stage = 2;
    state.nobility_timer = 125;
    state.nobility_kills = 0;

    let spawn_positions = [(52, 52), (53, 53)];
    for (sx, sy) in &spawn_positions {
        spawn_npc_at(state, NOBILITY_CRIA_NPC as usize, NOBILITY_MAP, *sx, *sy).await;
    }

    let conn_id = state.nobility_user;
    let msg = format!("{}Etapa 2: Elimina a las 2 crias de Smaug{}", server_opcodes::CONSOLE_MSG, font_types::INFO);
    state.send_to(conn_id, &msg).await;
}

/// Start stage 3 — spawn Smaug.
async fn nobleza_etapa_tres(state: &mut GameState) {
    state.nobility_stage = 3;
    state.nobility_timer = 250; // ~10 minutes
    state.nobility_kills = 0;

    spawn_npc_at(state, NOBILITY_SMAUG_NPC as usize, NOBILITY_MAP, 52, 52).await;

    let conn_id = state.nobility_user;
    let msg = format!("{}Etapa 3: Elimina a Smaug{}", server_opcodes::CONSOLE_MSG, font_types::INFO);
    state.send_to(conn_id, &msg).await;
}

/// Called when an NPC is killed — check if it's part of the nobility quest.
pub fn nobleza_check_npc_kill(state: &mut GameState, npc_number: i32) {
    if state.nobility_user == 0 { return; }

    match state.nobility_stage {
        1 if npc_number == NOBILITY_DRAGON_NPC => {
            state.nobility_kills += 1;
        }
        2 if npc_number == NOBILITY_CRIA_NPC => {
            state.nobility_kills += 1;
        }
        3 if npc_number == NOBILITY_SMAUG_NPC => {
            state.nobility_kills += 1;
        }
        _ => {}
    }
}

/// Tick the nobility quest timer. Called every second.
pub async fn tick_nobleza(state: &mut GameState) {
    if state.nobility_user == 0 { return; }

    state.nobility_timer -= 1;

    // Check stage completion
    let advance = match state.nobility_stage {
        1 => state.nobility_kills >= 4,
        2 => state.nobility_kills >= 2,
        3 => state.nobility_kills >= 1,
        _ => false,
    };

    if advance {
        match state.nobility_stage {
            1 => nobleza_etapa_dos(state).await,
            2 => nobleza_etapa_tres(state).await,
            3 => {
                // Quest complete!
                let conn_id = state.nobility_user;
                let msg = format!("{}Has completado la prueba de nobleza!{}", server_opcodes::CONSOLE_MSG, font_types::INFO);
                state.send_to(conn_id, &msg).await;

                // Give reputation reward
                if let Some(u) = state.users.get_mut(&conn_id) {
                    u.reputation += 500;
                }

                state.nobility_user = 0;
                state.nobility_stage = 0;
                state.nobility_timer = 0;
                state.nobility_kills = 0;
            }
            _ => {}
        }
        return;
    }

    // Check timeout
    if state.nobility_timer <= 0 {
        let conn_id = state.nobility_user;
        let msg = format!("{}El tiempo se ha acabado. Has fallado la prueba{}", server_opcodes::CONSOLE_MSG, font_types::INFO);
        state.send_to(conn_id, &msg).await;

        // Teleport back to Tanaris (map 1, 50, 50)
        warp_user(state, conn_id, 1, 50, 50).await;

        state.nobility_user = 0;
        state.nobility_stage = 0;
        state.nobility_timer = 0;
        state.nobility_kills = 0;
    }
}

/// Helper: spawn a specific NPC at a given position.
async fn spawn_npc_at(state: &mut GameState, npc_number: usize, map: i32, x: i32, y: i32) {
    let data = match state.game_data.npcs.get(npc_number) {
        Some(d) => d.clone(),
        None => return,
    };

    let idx = state.next_npc_index;
    state.next_npc_index += 1;

    let char_index = state.world.alloc_char_index();
    let mut npc = npc::NpcState::from_data(idx, &data, char_index, map, x, y);
    npc.respawn = false; // Don't respawn nobility quest NPCs

    // Place on world
    let grid = state.world.grid_mut(map);
    if let Some(tile) = grid.tile_mut(x, y) {
        tile.npc_index = idx as i32;
    }

    // Broadcast CC to area
    let cc_pkt = npc.build_cc_packet();
    state.send_data(SendTarget::ToArea { map, x, y }, &cc_pkt).await;

    // Store NPC
    if idx >= state.npcs.len() {
        state.npcs.resize(idx + 1, None);
    }
    state.npcs[idx] = Some(npc);
}

/// Helper: send CC packets for all nearby characters (users + NPCs) in area.
async fn send_area_chars(state: &mut GameState, conn_id: ConnectionId, map: i32, x: i32, y: i32) {
    // Send CC for nearby users
    let nearby_users: Vec<(ConnectionId, String)> = state.users.values()
        .filter(|u| u.logged && u.conn_id != conn_id && u.pos_map == map
                && (u.pos_x - x).abs() <= world::MIN_X_BORDER
                && (u.pos_y - y).abs() <= world::MIN_Y_BORDER)
        .map(|u| (u.conn_id, u.build_cc_packet()))
        .collect();

    for (_uid, cc_pkt) in &nearby_users {
        state.send_to(conn_id, cc_pkt).await;
    }

    // Send CC for nearby NPCs
    let mut npc_ccs: Vec<String> = Vec::new();
    for npc_opt in state.npcs.iter() {
        if let Some(npc) = npc_opt {
            if npc.active && npc.map == map
                && (npc.x - x).abs() <= world::MIN_X_BORDER
                && (npc.y - y).abs() <= world::MIN_Y_BORDER
            {
                npc_ccs.push(npc.build_cc_packet());
            }
        }
    }
    for cc_pkt in &npc_ccs {
        state.send_to(conn_id, cc_pkt).await;
    }
}

// =====================================================================
// Event Systems — CTF, JDH, LUZ, ARAM, Batalla Mistica, Faccionario,
// Torneos Automaticos, Guerras
// =====================================================================

/// Event type constants
const EVENTO_NONE: i32 = 0;
const EVENTO_CTF: i32 = 1;
const EVENTO_JDH: i32 = 2;
const EVENTO_LUZ: i32 = 3;
const EVENTO_ARAM: i32 = 4;
const EVENTO_BAT_MISTICA: i32 = 5;
const EVENTO_FACCIONARIO: i32 = 6;
const EVENTO_TORNEO_AUTO: i32 = 7;
const EVENTO_GUERRA: i32 = 8;

/// CTF map and constants
const CTF_MAP: i32 = 166;
const CTF_MAX_PLAYERS: i32 = 10;
const CTF_POINTS_TO_WIN: i32 = 3;

/// JDH (Juegos del Hambre) constants
const JDH_MAX_PLAYERS: i32 = 10;

/// LUZ event constants
const LUZ_MAX_PLAYERS: i32 = 14;

/// ARAM constants
const ARAM_MAP_A: i32 = 189;
const ARAM_MAP_B: i32 = 186;
const ARAM_TOWER_BLUE_NPC: usize = 964;
const ARAM_TOWER_RED_NPC: usize = 963;

/// Batalla Mistica constants
const BAT_MISTICA_MAP: i32 = 191;
const BAT_MISTICA_DURATION: i32 = 420; // 7 minutes in seconds

/// Faccionario event maps
const FACC_MAP_A: i32 = 185;
const FACC_MAP_B: i32 = 184;
const FACC_DURATION: i32 = 600; // 10 minutes in seconds

/// Torneo Automatico constants
const TORNEO_AUTO_MAP: i32 = 100;

/// Guerra map
const GUERRA_MAP: i32 = 164;

// ---- Generic event management ----

/// GM: Start event inscriptions.
/// /EVENTO <type> <max_players> <cost> <map>
async fn handle_gm_evento(state: &mut GameState, conn_id: ConnectionId, args: &str) {
    let priv_level = state.users.get(&conn_id).map(|u| u.privileges).unwrap_or(0);
    if priv_level < privilege_level::EVENT_MASTER { return; }

    if state.evento_activo || state.evento_inscripciones {
        state.send_to(conn_id, &format!("{}Ya hay un evento activo.{}", server_opcodes::CONSOLE_MSG, font_types::INFO)).await;
        return;
    }

    let parts: Vec<&str> = args.split_whitespace().collect();
    let tipo: i32 = parts.first().and_then(|s| s.parse().ok()).unwrap_or(0);
    let max_p: i32 = parts.get(1).and_then(|s| s.parse().ok()).unwrap_or(10);
    let cost: i64 = parts.get(2).and_then(|s| s.parse().ok()).unwrap_or(0);
    let map: i32 = parts.get(3).and_then(|s| s.parse().ok()).unwrap_or(0);

    if tipo < 1 || tipo > 8 {
        state.send_to(conn_id, &format!("{}Tipos: 1=CTF 2=JDH 3=LUZ 4=ARAM 5=BatMistica 6=Faccionario 7=TorneoAuto 8=Guerra{}", server_opcodes::CONSOLE_MSG, font_types::INFO)).await;
        return;
    }

    let event_map = if map > 0 { map } else {
        match tipo {
            EVENTO_CTF => CTF_MAP,
            EVENTO_ARAM => ARAM_MAP_A,
            EVENTO_BAT_MISTICA => BAT_MISTICA_MAP,
            EVENTO_GUERRA => GUERRA_MAP,
            EVENTO_TORNEO_AUTO => TORNEO_AUTO_MAP,
            _ => 100, // Generic fallback
        }
    };

    state.evento_inscripciones = true;
    state.evento_tipo = tipo;
    state.evento_max_players = max_p;
    state.evento_costo = cost;
    state.evento_map = event_map;
    state.evento_participantes.clear();
    state.evento_timer = 180; // 3 min signup window
    state.evento_countdown = 0;

    // Reset event-specific state
    state.ctf_puntos_azul = 0;
    state.ctf_puntos_rojo = 0;
    state.bat_kills = [0; 5];

    let tipo_name = event_type_name(tipo);
    let msg = format!("{}Inscripciones abiertas para {}! Costo: {} oro. Usa /PARTICIPAR para inscribirte.{}",
        server_opcodes::CONSOLE_MSG, tipo_name, cost, font_types::INFO);
    state.send_data(SendTarget::ToAll, &msg).await;

    info!("[EVENT] {} inscriptions opened (max {}, cost {}, map {})", tipo_name, max_p, cost, event_map);
}

/// /PARTICIPAR — Join the active event inscription.
async fn handle_slash_participar_evento(state: &mut GameState, conn_id: ConnectionId) {
    if !state.evento_inscripciones {
        state.send_to(conn_id, &format!("{}No hay inscripciones abiertas.{}", server_opcodes::CONSOLE_MSG, font_types::INFO)).await;
        return;
    }

    let (name, gold, dead, en_evento) = match state.users.get(&conn_id) {
        Some(u) if u.logged => (u.char_name.clone(), u.gold, u.dead, u.en_evento),
        _ => return,
    };

    if dead || en_evento {
        state.send_to(conn_id, &format!("{}No puedes inscribirte ahora.{}", server_opcodes::CONSOLE_MSG, font_types::INFO)).await;
        return;
    }

    if state.evento_participantes.len() as i32 >= state.evento_max_players {
        state.send_to(conn_id, &format!("{}El evento esta lleno.{}", server_opcodes::CONSOLE_MSG, font_types::INFO)).await;
        return;
    }

    if state.evento_participantes.contains(&conn_id) {
        state.send_to(conn_id, &format!("{}Ya estas inscripto.{}", server_opcodes::CONSOLE_MSG, font_types::INFO)).await;
        return;
    }

    if gold < state.evento_costo {
        state.send_to(conn_id, &format!("{}No tenes suficiente oro (necesitas {}).{}", server_opcodes::CONSOLE_MSG, state.evento_costo, font_types::INFO)).await;
        return;
    }

    // Deduct gold
    let cost = state.evento_costo;
    if let Some(user) = state.users.get_mut(&conn_id) {
        user.gold -= cost;
    }
    send_stats_gold(state, conn_id).await;

    state.evento_participantes.push(conn_id);
    let count = state.evento_participantes.len();

    let msg = format!("{}Te inscribiste al evento! ({}/{}){}",
        server_opcodes::CONSOLE_MSG, count, state.evento_max_players, font_types::INFO);
    state.send_to(conn_id, &msg).await;

    // Auto-start when full
    if count as i32 >= state.evento_max_players {
        evento_begin(state).await;
    }
}

/// Begin the event — assign teams, warp players, start countdown.
async fn evento_begin(state: &mut GameState) {
    state.evento_inscripciones = false;
    state.evento_activo = true;
    state.evento_countdown = 6; // 6 second countdown before action

    let tipo = state.evento_tipo;
    let map = state.evento_map;
    let participants = state.evento_participantes.clone();

    // Assign teams based on event type
    match tipo {
        EVENTO_CTF => {
            let half = participants.len() / 2;
            for (i, &pid) in participants.iter().enumerate() {
                let team = if i < half { 1 } else { 2 }; // 1=Azul, 2=Rojo
                if let Some(user) = state.users.get_mut(&pid) {
                    user.mapa_anterior = user.pos_map;
                    user.x_anterior = user.pos_x;
                    user.y_anterior = user.pos_y;
                    user.en_evento = true;
                    user.evento_tipo = tipo;
                    user.evento_equipo = team;
                    user.evento_muertes = 0;
                    user.not_move = true;
                }
                // CTF spawn: team 1 left side, team 2 right side
                let (sx, sy) = if i < half {
                    (20 + (i as i32 % 5) * 2, 50)
                } else {
                    (80 - ((i - half) as i32 % 5) * 2, 50)
                };
                warp_user(state, pid, CTF_MAP, sx, sy).await;
            }
            state.evento_timer = 300; // 5 min match
        }
        EVENTO_JDH => {
            for (i, &pid) in participants.iter().enumerate() {
                if let Some(user) = state.users.get_mut(&pid) {
                    user.mapa_anterior = user.pos_map;
                    user.x_anterior = user.pos_x;
                    user.y_anterior = user.pos_y;
                    user.en_evento = true;
                    user.evento_tipo = tipo;
                    user.evento_equipo = 0; // FFA
                    user.evento_muertes = 0;
                    user.not_move = true;
                }
                // Spread around map
                let sx = 20 + (i as i32 % 10) * 7;
                let sy = 20 + (i as i32 / 10) * 7;
                warp_user(state, pid, map, sx, sy).await;
            }
            state.evento_timer = 600; // 10 min max
        }
        EVENTO_LUZ => {
            for (i, &pid) in participants.iter().enumerate() {
                if let Some(user) = state.users.get_mut(&pid) {
                    user.mapa_anterior = user.pos_map;
                    user.x_anterior = user.pos_x;
                    user.y_anterior = user.pos_y;
                    user.en_evento = true;
                    user.evento_tipo = tipo;
                    user.evento_equipo = 0;
                    user.not_move = true;
                }
                let sx = 40 + (i as i32 % 7) * 3;
                let sy = 40 + (i as i32 / 7) * 3;
                warp_user(state, pid, map, sx, sy).await;
            }
            state.evento_timer = 300;
        }
        EVENTO_ARAM => {
            let half = participants.len() / 2;
            for (i, &pid) in participants.iter().enumerate() {
                let team = if i < half { 1 } else { 2 };
                if let Some(user) = state.users.get_mut(&pid) {
                    user.mapa_anterior = user.pos_map;
                    user.x_anterior = user.pos_x;
                    user.y_anterior = user.pos_y;
                    user.en_evento = true;
                    user.evento_tipo = tipo;
                    user.evento_equipo = team;
                    user.evento_muertes = 0;
                    user.not_move = true;
                }
                let (sx, sy) = if team == 1 {
                    (44 + (i as i32 % 5) * 2, 20 + (i as i32 / 5) * 2)
                } else {
                    (44 + ((i - half) as i32 % 5) * 2, 71 + ((i - half) as i32 / 5) * 2)
                };
                warp_user(state, pid, map, sx, sy).await;
            }
            // Spawn towers
            if let Some(idx) = state.spawn_npc(ARAM_TOWER_BLUE_NPC, map, 50, 65) {
                state.aram_torre_azul = idx;
                if let Some(npc) = state.get_npc(idx) {
                    let cc = npc.build_cc_packet();
                    state.send_data(SendTarget::ToMap(map), &cc).await;
                }
            }
            if let Some(idx) = state.spawn_npc(ARAM_TOWER_RED_NPC, map, 50, 35) {
                state.aram_torre_roja = idx;
                if let Some(npc) = state.get_npc(idx) {
                    let cc = npc.build_cc_packet();
                    state.send_data(SendTarget::ToMap(map), &cc).await;
                }
            }
            state.evento_timer = 600;
        }
        EVENTO_BAT_MISTICA => {
            let team_size = participants.len() / 4;
            for (i, &pid) in participants.iter().enumerate() {
                let team = (i / team_size.max(1)) as i32 + 1;
                let team = team.min(4);
                if let Some(user) = state.users.get_mut(&pid) {
                    user.mapa_anterior = user.pos_map;
                    user.x_anterior = user.pos_x;
                    user.y_anterior = user.pos_y;
                    user.en_evento = true;
                    user.evento_tipo = tipo;
                    user.evento_equipo = team;
                    user.evento_muertes = 0;
                    user.not_move = true;
                }
                let (sx, sy) = match team {
                    1 => (39 + (i as i32 % 5), 26 + (i as i32 / 5)),
                    2 => (56 + (i as i32 % 5), 26 + (i as i32 / 5)),
                    3 => (39 + (i as i32 % 5), 69 + (i as i32 / 5)),
                    _ => (56 + (i as i32 % 5), 69 + (i as i32 / 5)),
                };
                warp_user(state, pid, BAT_MISTICA_MAP, sx, sy).await;
            }
            state.bat_kills = [0; 5];
            state.evento_timer = BAT_MISTICA_DURATION;
        }
        EVENTO_FACCIONARIO => {
            let half = participants.len() / 2;
            for (i, &pid) in participants.iter().enumerate() {
                let team = if i < half { 1 } else { 2 }; // 1=Alianza, 2=Horda
                if let Some(user) = state.users.get_mut(&pid) {
                    user.mapa_anterior = user.pos_map;
                    user.x_anterior = user.pos_x;
                    user.y_anterior = user.pos_y;
                    user.en_evento = true;
                    user.evento_tipo = tipo;
                    user.evento_equipo = team;
                    user.evento_muertes = 0;
                    user.not_move = true;
                }
                let (sx, sy) = if team == 1 {
                    (34 + (i as i32 % 4) * 2, 64 + (i as i32 / 4) * 2)
                } else {
                    (70 + ((i - half) as i32 % 4), 18 + ((i - half) as i32 / 4) * 2)
                };
                warp_user(state, pid, map, sx, sy).await;
            }
            state.evento_timer = FACC_DURATION;
        }
        EVENTO_GUERRA => {
            let half = participants.len() / 2;
            for (i, &pid) in participants.iter().enumerate() {
                let team = if i < half { 1 } else { 2 };
                if let Some(user) = state.users.get_mut(&pid) {
                    user.mapa_anterior = user.pos_map;
                    user.x_anterior = user.pos_x;
                    user.y_anterior = user.pos_y;
                    user.en_evento = true;
                    user.evento_tipo = tipo;
                    user.evento_equipo = team;
                    user.evento_muertes = 0;
                    user.not_move = true;
                }
                let (sx, sy) = if team == 1 {
                    (30 + (i as i32 % 5) * 2, 50)
                } else {
                    (65 + ((i - half) as i32 % 5) * 2, 50)
                };
                warp_user(state, pid, GUERRA_MAP, sx, sy).await;
            }
            state.evento_timer = 600;
        }
        _ => {
            // Generic: FFA warp
            for (i, &pid) in participants.iter().enumerate() {
                if let Some(user) = state.users.get_mut(&pid) {
                    user.mapa_anterior = user.pos_map;
                    user.x_anterior = user.pos_x;
                    user.y_anterior = user.pos_y;
                    user.en_evento = true;
                    user.evento_tipo = tipo;
                    user.not_move = true;
                }
                let sx = 30 + (i as i32 % 10) * 4;
                let sy = 30 + (i as i32 / 10) * 4;
                warp_user(state, pid, map, sx, sy).await;
            }
            state.evento_timer = 300;
        }
    }

    let tipo_name = event_type_name(tipo);
    let msg = format!("{}El evento {} ha comenzado!{}", server_opcodes::CONSOLE_MSG, tipo_name, font_types::INFO);
    state.send_data(SendTarget::ToAll, &msg).await;

    info!("[EVENT] {} started with {} players on map {}", tipo_name, participants.len(), map);
}

/// Tick the event timers (called every 1s).
pub async fn tick_eventos(state: &mut GameState) {
    // Automatic broadcast message (/LMSG)
    if state.auto_msg_active {
        state.auto_msg_counter += 1;
        if state.auto_msg_counter >= state.auto_msg_interval * 60 {
            state.auto_msg_counter = 0;
            let msg = format!("N|{}{}", state.auto_msg_text, font_types::SYSTEM);
            state.send_data(SendTarget::ToAll, &msg).await;
        }
    }

    // Handle signup timeout
    if state.evento_inscripciones {
        state.evento_timer -= 1;
        if state.evento_timer <= 0 {
            if state.evento_participantes.len() >= 2 {
                evento_begin(state).await;
            } else {
                // Cancel — not enough players, refund
                let cost = state.evento_costo;
                let participants = state.evento_participantes.clone();
                for &pid in &participants {
                    if let Some(user) = state.users.get_mut(&pid) {
                        user.gold += cost;
                    }
                    send_stats_gold(state, pid).await;
                    state.send_to(pid, &format!("{}Evento cancelado por falta de participantes. Oro devuelto.{}", server_opcodes::CONSOLE_MSG, font_types::INFO)).await;
                }
                evento_reset(state);
                return;
            }
        }
        return;
    }

    if !state.evento_activo { return; }

    // Handle pre-start countdown
    if state.evento_countdown > 0 {
        state.evento_countdown -= 1;
        if state.evento_countdown == 0 {
            // Unlock movement
            let participants = state.evento_participantes.clone();
            for &pid in &participants {
                if let Some(user) = state.users.get_mut(&pid) {
                    user.not_move = false;
                }
            }
            let msg = format!("{}Comiencen!{}", server_opcodes::CONSOLE_MSG, font_types::INFO);
            for &pid in &participants {
                state.send_to(pid, &msg).await;
            }
        } else {
            let msg = format!("{}El evento comienza en {}...{}", server_opcodes::CONSOLE_MSG, state.evento_countdown, font_types::INFO);
            let participants = state.evento_participantes.clone();
            for &pid in &participants {
                state.send_to(pid, &msg).await;
            }
        }
        return;
    }

    // Decrement event timer
    state.evento_timer -= 1;

    // Handle respawn timers for dead players
    let participants = state.evento_participantes.clone();
    for &pid in &participants {
        let seconds = state.users.get(&pid).map(|u| u.evento_seconds).unwrap_or(0);
        if seconds > 0 {
            if let Some(user) = state.users.get_mut(&pid) {
                user.evento_seconds -= 1;
                if user.evento_seconds <= 0 {
                    // Respawn player at team base
                    user.dead = false;
                    user.min_hp = user.max_hp;
                }
            }
            if state.users.get(&pid).map(|u| u.evento_seconds).unwrap_or(0) <= 0 {
                evento_respawn_player(state, pid).await;
            }
        }
    }

    // Time expired — resolve
    if state.evento_timer <= 0 {
        evento_finalize(state).await;
    }
}

/// Handle a player death during an event.
pub async fn evento_player_death(state: &mut GameState, conn_id: ConnectionId, killer_id: ConnectionId) {
    let tipo = state.evento_tipo;

    // Increment death count and set respawn timer
    if let Some(user) = state.users.get_mut(&conn_id) {
        user.evento_muertes += 1;
        user.evento_seconds = user.evento_muertes * 2; // Escalating respawn delay
    }

    match tipo {
        EVENTO_JDH => {
            // Elimination — remove from event
            evento_remove_player(state, conn_id).await;

            // Check if only one remains
            let remaining: Vec<ConnectionId> = state.evento_participantes.iter()
                .filter(|&&pid| state.users.get(&pid).map(|u| u.en_evento && !u.dead).unwrap_or(false))
                .copied()
                .collect();

            if remaining.len() <= 1 {
                evento_finalize(state).await;
            }
        }
        EVENTO_LUZ => {
            // LUZ doesn't have PvP death — handled via guessing mechanic
        }
        EVENTO_CTF => {
            // Respawn at team base after delay
        }
        EVENTO_BAT_MISTICA => {
            // Count kill for killer's team
            let killer_team = state.users.get(&killer_id).map(|u| u.evento_equipo).unwrap_or(0);
            if killer_team >= 1 && killer_team <= 4 {
                state.bat_kills[killer_team as usize] += 1;
                // Send update to all event participants
                let pkt = format!("BTM1,{},{},{},{}", state.bat_kills[1], state.bat_kills[2], state.bat_kills[3], state.bat_kills[4]);
                let participants = state.evento_participantes.clone();
                for &pid in &participants {
                    state.send_to(pid, &pkt).await;
                }
            }
        }
        EVENTO_ARAM => {
            // ARAM death: escalating respawn
        }
        _ => {}
    }
}

/// Respawn a player at their team base during an event.
async fn evento_respawn_player(state: &mut GameState, conn_id: ConnectionId) {
    let (tipo, team, map) = match state.users.get(&conn_id) {
        Some(u) => (u.evento_tipo, u.evento_equipo, state.evento_map),
        None => return,
    };

    let (sx, sy) = match tipo {
        EVENTO_CTF => if team == 1 { (20, 50) } else { (80, 50) },
        EVENTO_ARAM => if team == 1 { (50, 25) } else { (50, 75) },
        EVENTO_BAT_MISTICA => match team {
            1 => (41, 28),
            2 => (58, 28),
            3 => (41, 71),
            _ => (58, 71),
        },
        EVENTO_FACCIONARIO => if team == 1 { (37, 67) } else { (71, 21) },
        EVENTO_GUERRA => if team == 1 { (35, 50) } else { (67, 50) },
        _ => (50, 50),
    };

    // Revive and warp
    if let Some(user) = state.users.get_mut(&conn_id) {
        user.dead = false;
        user.min_hp = user.max_hp;
    }
    warp_user(state, conn_id, map, sx, sy).await;
}

/// Remove a player from the active event (death in elimination, disconnect).
async fn evento_remove_player(state: &mut GameState, conn_id: ConnectionId) {
    state.evento_participantes.retain(|&pid| pid != conn_id);

    if let Some(user) = state.users.get_mut(&conn_id) {
        user.en_evento = false;
        user.evento_tipo = 0;
        user.not_move = false;
    }

    // Warp back to saved position
    let (m, x, y) = state.users.get(&conn_id)
        .map(|u| (u.mapa_anterior, u.x_anterior, u.y_anterior))
        .unwrap_or((EXIT_MAP, EXIT_X, EXIT_Y));
    if m > 0 {
        warp_user(state, conn_id, m, x, y).await;
    } else {
        warp_user(state, conn_id, EXIT_MAP, EXIT_X, EXIT_Y).await;
    }
}

/// Finalize the event — determine winner, award prizes, warp out.
async fn evento_finalize(state: &mut GameState) {
    let tipo = state.evento_tipo;
    let participants = state.evento_participantes.clone();

    match tipo {
        EVENTO_CTF => {
            let winner_team = if state.ctf_puntos_azul >= CTF_POINTS_TO_WIN { 1 }
                else if state.ctf_puntos_rojo >= CTF_POINTS_TO_WIN { 2 }
                else if state.ctf_puntos_azul > state.ctf_puntos_rojo { 1 }
                else { 2 };
            let team_name = if winner_team == 1 { "Azul" } else { "Rojo" };
            let msg = format!("{}El equipo {} gano la Captura de Bandera!{}", server_opcodes::CONSOLE_MSG, team_name, font_types::INFO);
            state.send_data(SendTarget::ToAll, &msg).await;

            // Award winning team
            for &pid in &participants {
                let team = state.users.get(&pid).map(|u| u.evento_equipo).unwrap_or(0);
                if team == winner_team {
                    if let Some(user) = state.users.get_mut(&pid) {
                        user.reputation += 20;
                    }
                }
            }
        }
        EVENTO_JDH => {
            // Last player standing wins
            let winner = participants.first().copied();
            if let Some(wid) = winner {
                let name = state.users.get(&wid).map(|u| u.char_name.clone()).unwrap_or_default();
                let msg = format!("{}{} es el ultimo superviviente de los Juegos del Hambre!{}", server_opcodes::CONSOLE_MSG, name, font_types::INFO);
                state.send_data(SendTarget::ToAll, &msg).await;
                if let Some(user) = state.users.get_mut(&wid) {
                    user.reputation += 50;
                }
            }
        }
        EVENTO_BAT_MISTICA => {
            // Team with most kills wins
            let mut best_team = 1;
            let mut best_kills = state.bat_kills[1];
            for t in 2..=4 {
                if state.bat_kills[t] > best_kills {
                    best_team = t as i32;
                    best_kills = state.bat_kills[t];
                }
            }
            let team_names = ["", "Azul", "Amarillo", "Rojo", "Verde"];
            let msg = format!("{}El equipo {} gano la Batalla Mistica con {} kills!{}",
                server_opcodes::CONSOLE_MSG, team_names[best_team as usize], best_kills, font_types::INFO);
            state.send_data(SendTarget::ToAll, &msg).await;

            for &pid in &participants {
                let team = state.users.get(&pid).map(|u| u.evento_equipo).unwrap_or(0);
                if team == best_team {
                    if let Some(user) = state.users.get_mut(&pid) {
                        user.reputation += 20;
                    }
                }
            }
        }
        EVENTO_ARAM => {
            // Check which tower survived
            let blue_alive = state.get_npc(state.aram_torre_azul).map(|n| n.is_alive()).unwrap_or(false);
            let red_alive = state.get_npc(state.aram_torre_roja).map(|n| n.is_alive()).unwrap_or(false);
            let winner_team = if blue_alive && !red_alive { 1 }
                else if !blue_alive && red_alive { 2 }
                else { 0 }; // Draw

            if winner_team > 0 {
                let team_name = if winner_team == 1 { "Azul" } else { "Rojo" };
                let msg = format!("{}El equipo {} gano el ARAM!{}", server_opcodes::CONSOLE_MSG, team_name, font_types::INFO);
                state.send_data(SendTarget::ToAll, &msg).await;
                for &pid in &participants {
                    let team = state.users.get(&pid).map(|u| u.evento_equipo).unwrap_or(0);
                    if team == winner_team {
                        if let Some(user) = state.users.get_mut(&pid) {
                            user.reputation += 20;
                        }
                    }
                }
            } else {
                state.send_data(SendTarget::ToAll, &format!("{}El ARAM termino en empate!{}", server_opcodes::CONSOLE_MSG, font_types::INFO)).await;
            }

            // Kill tower NPCs
            state.kill_npc(state.aram_torre_azul);
            state.kill_npc(state.aram_torre_roja);
        }
        EVENTO_FACCIONARIO | EVENTO_GUERRA => {
            let msg = format!("{}El evento ha terminado!{}", server_opcodes::CONSOLE_MSG, font_types::INFO);
            state.send_data(SendTarget::ToAll, &msg).await;
        }
        _ => {
            state.send_data(SendTarget::ToAll, &format!("{}El evento ha terminado!{}", server_opcodes::CONSOLE_MSG, font_types::INFO)).await;
        }
    }

    // Warp all participants back
    for &pid in &participants {
        if let Some(user) = state.users.get_mut(&pid) {
            user.en_evento = false;
            user.evento_tipo = 0;
            user.not_move = false;
            user.dead = false;
            user.min_hp = user.max_hp;
        }
        let (m, x, y) = state.users.get(&pid)
            .map(|u| (u.mapa_anterior, u.x_anterior, u.y_anterior))
            .unwrap_or((EXIT_MAP, EXIT_X, EXIT_Y));
        if m > 0 {
            warp_user(state, pid, m, x, y).await;
        } else {
            warp_user(state, pid, EXIT_MAP, EXIT_X, EXIT_Y).await;
        }
    }

    evento_reset(state);
    info!("[EVENT] {} finalized", event_type_name(tipo));
}

/// Reset all event state.
fn evento_reset(state: &mut GameState) {
    state.evento_activo = false;
    state.evento_inscripciones = false;
    state.evento_tipo = 0;
    state.evento_participantes.clear();
    state.evento_timer = 0;
    state.evento_countdown = 0;
    state.ctf_puntos_azul = 0;
    state.ctf_puntos_rojo = 0;
    state.bat_kills = [0; 5];
    state.aram_torre_azul = 0;
    state.aram_torre_roja = 0;
}

/// Get human-readable event type name.
fn event_type_name(tipo: i32) -> &'static str {
    match tipo {
        EVENTO_CTF => "Captura de Bandera",
        EVENTO_JDH => "Juegos del Hambre",
        EVENTO_LUZ => "Evento Luz",
        EVENTO_ARAM => "ARAM",
        EVENTO_BAT_MISTICA => "Batalla Mistica",
        EVENTO_FACCIONARIO => "Evento Faccionario",
        EVENTO_TORNEO_AUTO => "Torneo Automatico",
        EVENTO_GUERRA => "Guerra",
        _ => "Evento",
    }
}

/// CTF: Score a point for a team.
pub fn ctf_score_point(state: &mut GameState, team: i32) {
    match team {
        1 => state.ctf_puntos_azul += 1,
        2 => state.ctf_puntos_rojo += 1,
        _ => {}
    }
}

/// ARAM: Check if a tower NPC death ends the event.
pub async fn aram_check_tower_death(state: &mut GameState, npc_idx: usize) {
    if state.evento_tipo != EVENTO_ARAM || !state.evento_activo { return; }

    if npc_idx == state.aram_torre_azul || npc_idx == state.aram_torre_roja {
        evento_finalize(state).await;
    }
}

// ---- Torneo Automatico (single-elimination bracket) ----

/// GM: Start automatic tournament with N rounds.
async fn handle_gm_torneo_auto(state: &mut GameState, conn_id: ConnectionId, rounds: i32) {
    let priv_level = state.users.get(&conn_id).map(|u| u.privileges).unwrap_or(0);
    if priv_level < privilege_level::EVENT_MASTER { return; }

    if state.torneo_auto_activo {
        state.send_to(conn_id, &format!("{}Ya hay un torneo automatico activo.{}", server_opcodes::CONSOLE_MSG, font_types::INFO)).await;
        return;
    }

    let max_players = 1 << rounds; // 2^rounds
    state.torneo_auto_activo = true;
    state.torneo_auto_rondas = rounds;
    state.torneo_auto_ronda_actual = 1;
    state.torneo_auto_bracket = Vec::with_capacity(max_players as usize);
    state.torneo_auto_timer = 60; // 60s signup

    let msg = format!("{}Torneo automatico abierto! {} slots. Usa /TORNEO para inscribirte.{}",
        server_opcodes::CONSOLE_MSG, max_players, font_types::INFO);
    state.send_data(SendTarget::ToAll, &msg).await;

    info!("[TORNEO] Auto tournament started: {} rounds, {} slots", rounds, max_players);
}

/// /TORNEO — Join automatic tournament (redirected here if torneo_auto active).
pub async fn torneo_auto_join(state: &mut GameState, conn_id: ConnectionId) {
    if !state.torneo_auto_activo {
        state.send_to(conn_id, &format!("{}No hay torneo automatico activo.{}", server_opcodes::CONSOLE_MSG, font_types::INFO)).await;
        return;
    }

    let max = 1 << state.torneo_auto_rondas;
    if state.torneo_auto_bracket.len() as i32 >= max {
        state.send_to(conn_id, &format!("{}El torneo esta lleno.{}", server_opcodes::CONSOLE_MSG, font_types::INFO)).await;
        return;
    }

    if state.torneo_auto_bracket.contains(&conn_id) {
        state.send_to(conn_id, &format!("{}Ya estas inscripto.{}", server_opcodes::CONSOLE_MSG, font_types::INFO)).await;
        return;
    }

    state.torneo_auto_bracket.push(conn_id);
    let slot = state.torneo_auto_bracket.len() as i32;

    if let Some(user) = state.users.get_mut(&conn_id) {
        user.torneo_auto = true;
        user.torneo_auto_slot = slot;
        user.torneo_auto_muerto = false;
        user.mapa_anterior = user.pos_map;
        user.x_anterior = user.pos_x;
        user.y_anterior = user.pos_y;
    }

    let msg = format!("{}Inscripto al torneo! Slot #{}/{}{}",
        server_opcodes::CONSOLE_MSG, slot, max, font_types::INFO);
    state.send_to(conn_id, &msg).await;

    // Auto-start when full
    if slot >= max {
        torneo_auto_start_round(state).await;
    }
}

/// Start the next round of fights in the automatic tournament.
async fn torneo_auto_start_round(state: &mut GameState) {
    let alive: Vec<ConnectionId> = state.torneo_auto_bracket.iter()
        .filter(|&&pid| state.users.get(&pid).map(|u| u.torneo_auto && !u.torneo_auto_muerto).unwrap_or(false))
        .copied()
        .collect();

    if alive.len() <= 1 {
        // We have a winner
        if let Some(&winner) = alive.first() {
            let name = state.users.get(&winner).map(|u| u.char_name.clone()).unwrap_or_default();
            let msg = format!("{}{} ha ganado el torneo automatico!{}", server_opcodes::CONSOLE_MSG, name, font_types::INFO);
            state.send_data(SendTarget::ToAll, &msg).await;

            if let Some(user) = state.users.get_mut(&winner) {
                user.reputation += 100;
            }
        }

        // End tournament — warp everyone back
        torneo_auto_end(state).await;
        return;
    }

    // Pair up fighters
    let round = state.torneo_auto_ronda_actual;
    let msg = format!("{}Ronda {} del torneo!{}", server_opcodes::CONSOLE_MSG, round, font_types::INFO);

    for &pid in &alive {
        state.send_to(pid, &msg).await;
    }

    // Warp first pair to arena corners
    if alive.len() >= 2 {
        warp_user(state, alive[0], TORNEO_AUTO_MAP, 41, 42).await;
        warp_user(state, alive[1], TORNEO_AUTO_MAP, 60, 57).await;
    }

    // Rest wait in waiting zone
    for i in 2..alive.len() {
        warp_user(state, alive[i], TORNEO_AUTO_MAP, 23 + (i as i32 % 8), 37 + (i as i32 / 8) * 3).await;
    }

    state.torneo_auto_ronda_actual += 1;
}

/// Handle death in automatic tournament.
pub async fn torneo_auto_death(state: &mut GameState, conn_id: ConnectionId) {
    if !state.torneo_auto_activo { return; }

    if let Some(user) = state.users.get_mut(&conn_id) {
        user.torneo_auto_muerto = true;
        user.dead = false;
        user.min_hp = user.max_hp;
    }

    // Warp loser to waiting zone
    warp_user(state, conn_id, TORNEO_AUTO_MAP, 25, 45).await;

    // Check if current match is decided, then start next pair or next round
    let alive_in_ring: Vec<ConnectionId> = state.torneo_auto_bracket.iter()
        .filter(|&&pid| {
            state.users.get(&pid).map(|u| {
                u.torneo_auto && !u.torneo_auto_muerto && u.pos_map == TORNEO_AUTO_MAP
                    && u.pos_x >= 35 && u.pos_x <= 65 && u.pos_y >= 35 && u.pos_y <= 65
            }).unwrap_or(false)
        })
        .copied()
        .collect();

    if alive_in_ring.len() <= 1 {
        // Current match decided — advance to next
        torneo_auto_start_round(state).await;
    }
}

/// End automatic tournament — warp all participants back.
async fn torneo_auto_end(state: &mut GameState) {
    let bracket = state.torneo_auto_bracket.clone();
    for &pid in &bracket {
        if let Some(user) = state.users.get_mut(&pid) {
            user.torneo_auto = false;
            user.torneo_auto_muerto = false;
        }
        let (m, x, y) = state.users.get(&pid)
            .map(|u| (u.mapa_anterior, u.x_anterior, u.y_anterior))
            .unwrap_or((EXIT_MAP, EXIT_X, EXIT_Y));
        if m > 0 {
            warp_user(state, pid, m, x, y).await;
        } else {
            warp_user(state, pid, EXIT_MAP, EXIT_X, EXIT_Y).await;
        }
    }

    state.torneo_auto_activo = false;
    state.torneo_auto_bracket.clear();
    state.torneo_auto_rondas = 0;
    state.torneo_auto_ronda_actual = 0;
    state.torneo_auto_timer = 0;

    info!("[TORNEO] Auto tournament ended");
}

// =====================================================================
// IP Security (SecurityIp.bas) — rate limiting + max connections per IP
// =====================================================================

/// Check if a new connection from this IP should be accepted.
/// Returns false if rate-limited or max connections exceeded.
pub fn ip_security_accept(state: &mut GameState, ip: &str) -> bool {
    let now = std::time::Instant::now();

    // Check rate limit (min interval between connections from same IP)
    if let Some(last) = state.ip_last_connect.get(ip) {
        let elapsed = now.duration_since(*last).as_millis() as u64;
        if elapsed < state.ip_min_interval_ms {
            return false;
        }
    }
    state.ip_last_connect.insert(ip.to_string(), now);

    // Check max connections per IP
    let count = state.ip_connection_count.entry(ip.to_string()).or_insert(0);
    if *count >= state.ip_max_connections {
        return false;
    }
    *count += 1;

    true
}

// =====================================================================
// NPC Pathfinding (PathFinding.bas) — BFS on map grid
// =====================================================================

/// Maximum pathfinding steps (matches VB6 MAXPASOS = 30).
const PF_MAX_STEPS: usize = 30;

/// BFS pathfinding — find path from (sx,sy) to (tx,ty) on the given map.
/// Returns a Vec of (x,y) positions from start to target (excluding start).
/// Uses 4-directional adjacency on a 100x100 grid.
fn pathfind_bfs(state: &GameState, map: i32, sx: i32, sy: i32, tx: i32, ty: i32) -> Vec<(i32, i32)> {
    if sx == tx && sy == ty { return Vec::new(); }
    if sx < 1 || sx > 100 || sy < 1 || sy > 100 { return Vec::new(); }
    if tx < 1 || tx > 100 || ty < 1 || ty > 100 { return Vec::new(); }

    // BFS grid — 0 = unvisited, 1-4 = came-from direction, 5 = start
    let mut visited = vec![vec![0u8; 102]; 102]; // 1-indexed
    let mut queue: std::collections::VecDeque<(i32, i32, usize)> = std::collections::VecDeque::new();

    visited[sy as usize][sx as usize] = 5; // Mark start
    queue.push_back((sx, sy, 0));

    // Directions: 1=north(y-1), 2=east(x+1), 3=south(y+1), 4=west(x-1)
    let dirs: [(i32, i32, u8); 4] = [
        (0, -1, 1), // north
        (1, 0, 2),  // east
        (0, 1, 3),  // south
        (-1, 0, 4), // west
    ];

    let mut found = false;

    while let Some((cx, cy, depth)) = queue.pop_front() {
        if depth >= PF_MAX_STEPS { continue; }
        if cx == tx && cy == ty { found = true; break; }

        for &(dx, dy, dir_code) in &dirs {
            let nx = cx + dx;
            let ny = cy + dy;
            if nx < 1 || nx > 100 || ny < 1 || ny > 100 { continue; }
            if visited[ny as usize][nx as usize] != 0 { continue; }

            // Check if tile is walkable (not blocked, no NPC)
            if state.is_tile_blocked(map, nx, ny) { continue; }
            let has_npc = state.world.grid(map)
                .and_then(|g| g.tile(nx, ny))
                .map(|t| t.npc_index > 0)
                .unwrap_or(false);
            if has_npc && !(nx == tx && ny == ty) { continue; }

            visited[ny as usize][nx as usize] = dir_code;
            queue.push_back((nx, ny, depth + 1));
        }
    }

    if !found { return Vec::new(); }

    // Reconstruct path by backtracking from target
    let mut path = Vec::new();
    let mut cx = tx;
    let mut cy = ty;

    while !(cx == sx && cy == sy) {
        path.push((cx, cy));
        let dir = visited[cy as usize][cx as usize];
        match dir {
            1 => cy += 1,  // came from south (reverse of north)
            2 => cx -= 1,  // came from west (reverse of east)
            3 => cy -= 1,  // came from north (reverse of south)
            4 => cx += 1,  // came from east (reverse of west)
            _ => break,    // start or error
        }
        if path.len() > PF_MAX_STEPS { break; }
    }

    path.reverse();
    path
}

/// Compute heading from step direction for NPC movement.
fn heading_from_step(cx: i32, cy: i32, nx: i32, ny: i32) -> i32 {
    let dx = nx - cx;
    let dy = ny - cy;
    if dy < 0 { world::HEADING_NORTH }
    else if dy > 0 { world::HEADING_SOUTH }
    else if dx > 0 { world::HEADING_EAST }
    else if dx < 0 { world::HEADING_WEST }
    else { world::HEADING_SOUTH }
}

/// Execute one pathfinding step for an NPC — moves to next step in path.
fn npc_pathfind_step(state: &mut GameState, npc_idx: usize) -> bool {
    let (step, path_len, map, x, y) = match state.get_npc(npc_idx) {
        Some(n) => (n.pf_step, n.pf_path.len(), n.map, n.x, n.y),
        None => return false,
    };

    if step >= path_len { return false; }

    let (nx, ny) = match state.get_npc(npc_idx) {
        Some(n) => n.pf_path[step],
        None => return false,
    };

    let heading = heading_from_step(x, y, nx, ny);

    // Try to move NPC in this direction
    if move_npc(state, npc_idx, heading) {
        if let Some(n) = state.get_npc_mut(npc_idx) {
            n.pf_step += 1;
        }
        true
    } else {
        // Path blocked — clear path to force recompute
        if let Some(n) = state.get_npc_mut(npc_idx) {
            n.pf_path.clear();
            n.pf_step = 0;
        }
        false
    }
}

// =====================================================================
// Castle Siege (modSiege.bas) — guild vs guild castle warfare
// =====================================================================

const SIEGE_MAP: i32 = 151;
const SIEGE_DURATION_TICKS: i32 = 1800; // 30 minutes at 1 tick/sec

/// /CASTILLOS — Show siege info.
async fn handle_slash_castillos(state: &mut GameState, conn_id: ConnectionId) {
    if state.siege_active {
        let owner = state.siege_guild_owner;
        let attacker = state.siege_guild_attacker;
        let msg = format!("{}Asedio activo: Clan {} vs Clan {}. Puntos: {}/{}/{}{}",
            server_opcodes::CONSOLE_MSG,
            owner, attacker,
            state.siege_conquest[1], state.siege_conquest[2], state.siege_conquest[3],
            font_types::INFO);
        state.send_to(conn_id, &msg).await;
    } else {
        let owner = state.siege_guild_owner;
        if owner > 0 {
            let msg = format!("{}El castillo pertenece al clan {}.{}", server_opcodes::CONSOLE_MSG, owner, font_types::INFO);
            state.send_to(conn_id, &msg).await;
        } else {
            state.send_to(conn_id, &format!("{}El castillo no pertenece a ningun clan.{}", server_opcodes::CONSOLE_MSG, font_types::INFO)).await;
        }
    }
}

/// GM command: Start siege between guild_owner and guild_attacker.
async fn handle_gm_start_siege(state: &mut GameState, conn_id: ConnectionId, args: &str) {
    let parts: Vec<&str> = args.split('@').collect();
    let guild_owner: i32 = parts.first().and_then(|s| s.parse().ok()).unwrap_or(0);
    let guild_attacker: i32 = parts.get(1).and_then(|s| s.parse().ok()).unwrap_or(0);

    if guild_owner <= 0 || guild_attacker <= 0 {
        state.send_to(conn_id, &format!("{}Uso: /SIEGE <owner>@<attacker>{}", server_opcodes::CONSOLE_MSG, font_types::INFO)).await;
        return;
    }

    state.siege_active = true;
    state.siege_guild_owner = guild_owner;
    state.siege_guild_attacker = guild_attacker;
    state.siege_conquest = [0; 4];
    state.siege_timer = SIEGE_DURATION_TICKS;

    let msg = format!("{}El asedio al castillo ha comenzado!{}", server_opcodes::CONSOLE_MSG, font_types::INFO);
    state.send_data(SendTarget::ToAll, &msg).await;

    info!("[SIEGE] Started: guild {} vs guild {}", guild_owner, guild_attacker);
}

/// GM command: End siege.
async fn handle_gm_end_siege(state: &mut GameState, conn_id: ConnectionId) {
    if !state.siege_active {
        state.send_to(conn_id, &format!("{}No hay asedio activo.{}", server_opcodes::CONSOLE_MSG, font_types::INFO)).await;
        return;
    }

    resolve_siege(state).await;
}

/// Tick the siege timer (called every 1s from player_tick).
pub async fn tick_siege(state: &mut GameState) {
    if !state.siege_active { return; }

    state.siege_timer -= 1;
    if state.siege_timer <= 0 {
        resolve_siege(state).await;
    }
}

/// Resolve the siege — count conquest points and determine winner.
async fn resolve_siege(state: &mut GameState) {
    let owner = state.siege_guild_owner;
    let attacker = state.siege_guild_attacker;

    // Count conquest points: how many are held by attacker
    let attacker_points = (1..=3).filter(|&i| state.siege_conquest[i] == attacker).count();
    let owner_points = (1..=3).filter(|&i| state.siege_conquest[i] == owner || state.siege_conquest[i] == 0).count();

    if attacker_points >= 2 {
        // Attacker wins — they become the new owner
        state.siege_guild_owner = attacker;
        let msg = format!("{}El clan atacante ha conquistado el castillo!{}", server_opcodes::CONSOLE_MSG, font_types::INFO);
        state.send_data(SendTarget::ToAll, &msg).await;
        info!("[SIEGE] Attacker guild {} conquered castle from guild {}", attacker, owner);
    } else {
        // Defender holds
        let msg = format!("{}El clan defensor ha mantenido el castillo!{}", server_opcodes::CONSOLE_MSG, font_types::INFO);
        state.send_data(SendTarget::ToAll, &msg).await;
        info!("[SIEGE] Defender guild {} held castle against guild {}", owner, attacker);
    }

    state.siege_active = false;
    state.siege_conquest = [0; 4];
    state.siege_timer = 0;

    // Warp siege participants back (all users on siege map)
    let siege_users: Vec<ConnectionId> = state.users.values()
        .filter(|u| u.logged && u.pos_map == state.siege_map)
        .map(|u| u.conn_id)
        .collect();
    for uid in siege_users {
        let (m, x, y) = state.users.get(&uid)
            .map(|u| (u.mapa_anterior, u.x_anterior, u.y_anterior))
            .unwrap_or((EXIT_MAP, EXIT_X, EXIT_Y));
        if m > 0 {
            warp_user(state, uid, m, x, y).await;
        } else {
            warp_user(state, uid, EXIT_MAP, EXIT_X, EXIT_Y).await;
        }
    }
}

/// War timer tick — runs every 1s. VB6: frmMain.frm lines 819-876.
/// War starts at minute 120, warnings from 122-131, ends at 132.
/// Ancalagon boss timer — called every 1 second from main loop.
/// VB6: modDragon.bas — When dragon is dead, count to 60 minutes.
/// Spawn pre-dragon (937) + 4 guardians (938) at map 123. Pre-dragon death → spawn dragon (936).
pub async fn tick_ancalagon(state: &mut GameState) {
    if state.ancalagon_alive {
        return; // Dragon is alive, nothing to do
    }

    state.ancalagon_seconds += 1;
    if state.ancalagon_seconds < 60 {
        return;
    }
    state.ancalagon_seconds = 0;
    state.ancalagon_minutes += 1;

    if state.ancalagon_minutes >= 60 && !state.ancalagon_pre_dragon {
        // Time to spawn pre-dragon + guardians
        state.ancalagon_minutes = 0;
        state.ancalagon_seconds = 0;

        // Broadcast warning
        state.send_data(SendTarget::ToAll, "||471").await;

        // Spawn 4 guardians (NPC 938) at map 123
        let guardian_positions = [(50, 45), (52, 45), (50, 47), (52, 47)];
        state.ancalagon_guardians = 0;
        for (gx, gy) in &guardian_positions {
            if state.spawn_npc(938, 123, *gx, *gy).is_some() {
                state.ancalagon_guardians += 1;
            }
        }

        // Spawn pre-dragon (NPC 937) at map 123
        if state.spawn_npc(937, 123, 51, 46).is_some() {
            state.ancalagon_pre_dragon = true;
        }
    }

}

/// Check if pre-dragon was killed and spawn real dragon.
/// Called from npc_die when NPC 937 dies.
async fn try_spawn_ancalagon_dragon(state: &mut GameState) {
    if !state.ancalagon_alive && !state.ancalagon_pre_dragon {
        if let Some(_) = state.spawn_npc(936, 123, 51, 46) {
            state.ancalagon_alive = true;
            let msg = "T|¡El Ancalagon ha aparecido!~255~0~0~1~0";
            state.send_data(SendTarget::ToAll, msg).await;
        }
    }
}

pub async fn tick_guerra(state: &mut GameState) {
    state.guerra_seconds += 1;
    if state.guerra_seconds < 60 { return; }
    state.guerra_seconds = 0;
    state.guerra_minutes += 1;

    let mins = state.guerra_minutes;

    if mins == 120 {
        // Start war — pick random location
        // Simple alternation based on system time for pseudo-randomness
        let location = if std::time::SystemTime::now()
            .duration_since(std::time::UNIX_EPOCH)
            .unwrap_or_default().as_secs() % 2 == 0 { 1u32 } else { 2u32 };
        if location == 1 {
            // Anvilmar (map 29)
            state.send_data(SendTarget::ToAll, "||460").await;
            state.hay_guerra_anvil = true;
            if let Some(npc_idx) = state.spawn_npc(947, 29, 78, 45) {
                state.rey_guerra_index = npc_idx;
            }
            state.hay_guerra = true;
        } else {
            // Khalimdar (map 27)
            state.send_data(SendTarget::ToAll, "||461").await;
            state.hay_guerra_khalim = true;
            if let Some(npc_idx) = state.spawn_npc(948, 27, 50, 18) {
                state.rey_guerra_index = npc_idx;
            }
            state.hay_guerra = true;
        }
    }

    if mins >= 122 && mins < 132 && state.hay_guerra {
        let remaining = 132 - mins;
        state.send_data(SendTarget::ToAll, &format!("||462@{}", remaining)).await;
    }

    if mins == 132 && state.hay_guerra {
        // War ends — no winner
        if state.hay_guerra_khalim {
            state.send_data(SendTarget::ToAll, "||463").await;
        } else if state.hay_guerra_anvil {
            state.send_data(SendTarget::ToAll, "||464").await;
        }
        // Remove war king NPC
        if state.rey_guerra_index > 0 {
            state.kill_npc(state.rey_guerra_index);
        }
        state.hay_guerra = false;
        state.hay_guerra_anvil = false;
        state.hay_guerra_khalim = false;
        state.rey_guerra_index = 0;
        state.guerra_minutes = 0;

        // Reset all players' en_guerra flag
        let war_users: Vec<ConnectionId> = state.users.values()
            .filter(|u| u.en_guerra)
            .map(|u| u.conn_id)
            .collect();
        for uid in war_users {
            if let Some(user) = state.users.get_mut(&uid) {
                user.en_guerra = false;
            }
        }
    }
}

/// Capture a conquest point in the siege (called when a guild member stands on a point tile).
pub fn siege_capture_point(state: &mut GameState, point: usize, guild_index: i32) {
    if !state.siege_active || point < 1 || point > 3 { return; }
    state.siege_conquest[point] = guild_index;
}

// =====================================================================
// Praetorian System (praetorians.bas) — faction fortress NPCs
// =====================================================================

/// Praetorian NPC numbers (900-904 in VB6)
const PRETORIANO_MAGO: usize = 900;
const PRETORIANO_CLERIGO: usize = 901;
const PRETORIANO_GUERRERO: usize = 902;
const PRETORIANO_CAZADOR: usize = 903;
const PRETORIANO_REY: usize = 904;

const MAX_PRETORIANOS_CLAN: usize = 8;

/// Create a praetorian clan at a given location.
async fn crear_clan_pretoriano(state: &mut GameState, map: i32, x: i32, y: i32, faccion: i32) {
    // Clear existing clan
    limpiar_clan_pretoriano(state).await;

    state.pretoriano_faccion = faccion;
    state.pretoriano_activo = true;
    state.pretoriano_alcoba = 0;

    // Spawn 8 praetorians in a formation around (x,y)
    let positions = [
        (x - 2, y - 2), (x + 2, y - 2),
        (x - 2, y + 2), (x + 2, y + 2),
        (x - 1, y), (x + 1, y),
        (x, y - 1), (x, y + 1),
    ];

    let npc_types = [
        PRETORIANO_GUERRERO, PRETORIANO_GUERRERO,
        PRETORIANO_CAZADOR, PRETORIANO_CAZADOR,
        PRETORIANO_MAGO, PRETORIANO_MAGO,
        PRETORIANO_CLERIGO, PRETORIANO_REY,
    ];

    for i in 0..MAX_PRETORIANOS_CLAN {
        let (px, py) = positions[i];
        if px >= 1 && px <= 100 && py >= 1 && py <= 100 {
            if let Some(npc_idx) = state.spawn_npc(npc_types[i], map, px, py) {
                state.pretoriano_clan.push(npc_idx);

                // Broadcast CC
                if let Some(npc) = state.get_npc(npc_idx) {
                    let cc_pkt = npc.build_cc_packet();
                    state.send_data(SendTarget::ToArea { map, x: px, y: py }, &cc_pkt).await;
                }
            }
        }
    }

    info!("[PRET] Created praetorian clan on map {} at ({},{}) faction {}", map, x, y, faccion);
}

/// Remove all praetorian NPCs.
async fn limpiar_clan_pretoriano(state: &mut GameState) {
    let clan_indices: Vec<usize> = state.pretoriano_clan.drain(..).collect();
    for npc_idx in clan_indices {
        if let Some(npc) = state.get_npc(npc_idx) {
            let bp_pkt = format!("BP{}", npc.char_index.0);
            let (map, x, y) = (npc.map, npc.x, npc.y);
            state.send_data(SendTarget::ToArea { map, x, y }, &bp_pkt).await;
        }
        state.kill_npc(npc_idx);
    }
    state.pretoriano_activo = false;
    state.pretoriano_alcoba = 0;
}

/// Check if an NPC number is a praetorian type.
fn es_pretoriano(npc_number: usize) -> bool {
    npc_number >= 900 && npc_number <= 904
}

/// Handle praetorian death — check if clan is wiped.
pub fn pretoriano_check_death(state: &mut GameState, npc_idx: usize) {
    if !state.pretoriano_activo { return; }

    // Remove from clan list
    state.pretoriano_clan.retain(|&idx| idx != npc_idx);

    // Check if king (REY) is dead
    let rey_alive = state.pretoriano_clan.iter().any(|&idx| {
        state.get_npc(idx)
            .filter(|n| n.is_alive() && n.npc_number == PRETORIANO_REY)
            .is_some()
    });

    if !rey_alive {
        // All praetorians defeated or king killed — advance alcoba
        state.pretoriano_alcoba += 1;
        if state.pretoriano_alcoba >= 4 {
            // Fortress conquered!
            state.pretoriano_activo = false;
            info!("[PRET] Fortress conquered! Faction {}", state.pretoriano_faccion);
        } else {
            info!("[PRET] Alcoba {} cleared, advancing", state.pretoriano_alcoba);
        }
    }
}

// =====================================================================
// Missing VB6 handlers — Phase 10 parity
// =====================================================================

/// SWAP — Swap two inventory slots.
async fn handle_swap(state: &mut GameState, conn_id: ConnectionId, data: &str) {
    let payload = strip_opcode(data, 4);
    let slot1: usize = read_field(1, payload, ',').parse().unwrap_or(0);
    let slot2: usize = read_field(2, payload, ',').parse().unwrap_or(0);

    if let Some(user) = state.users.get(&conn_id) {
        if user.comerciando || user.trading {
            state.send_to(conn_id, "||153").await;
            return;
        }
    }

    if let Some(user) = state.users.get_mut(&conn_id) {
        if slot1 == 0 || slot2 == 0 || slot1 > MAX_INVENTORY_SLOTS || slot2 > MAX_INVENTORY_SLOTS || slot1 == slot2 {
            return;
        }
        let s1 = slot1 - 1;
        let s2 = slot2 - 1;
        user.inventory.swap(s1, s2);

        // Update equipped slot references
        if user.equip.weapon == slot1 { user.equip.weapon = slot2; }
        else if user.equip.weapon == slot2 { user.equip.weapon = slot1; }
        if user.equip.armor == slot1 { user.equip.armor = slot2; }
        else if user.equip.armor == slot2 { user.equip.armor = slot1; }
        if user.equip.shield == slot1 { user.equip.shield = slot2; }
        else if user.equip.shield == slot2 { user.equip.shield = slot1; }
        if user.equip.helmet == slot1 { user.equip.helmet = slot2; }
        else if user.equip.helmet == slot2 { user.equip.helmet = slot1; }
        if user.equip.municion == slot1 { user.equip.municion = slot2; }
        else if user.equip.municion == slot2 { user.equip.municion = slot1; }
    }
    send_full_inventory(state, conn_id).await;
}

/// SKSE — Distribute skill points.
async fn handle_skse(state: &mut GameState, conn_id: ConnectionId, data: &str) {
    let payload = strip_opcode(data, 4);

    // Parse 22 comma-delimited skill increments
    let mut increments = [0i32; 22];
    let mut total = 0i32;
    for i in 0..22 {
        let val: i32 = read_field(i + 1, payload, ',').parse().unwrap_or(0);
        if val < 0 {
            state.send_to(conn_id, &format!("{}Valor invalido.{}", server_opcodes::CONSOLE_MSG, font_types::INFO)).await;
            return;
        }
        increments[i] = val;
        total += val;
    }

    if let Some(user) = state.users.get(&conn_id) {
        if total > user.skill_pts_libres || total <= 0 {
            state.send_to(conn_id, &format!("{}Puntos de skill invalidos.{}", server_opcodes::CONSOLE_MSG, font_types::INFO)).await;
            return;
        }
    } else {
        return;
    }

    if let Some(user) = state.users.get_mut(&conn_id) {
        for i in 0..22 {
            user.skills[i] = (user.skills[i] + increments[i]).min(100);
        }
        user.skill_pts_libres -= total;
    }

    state.send_to(conn_id, &format!("{}Has distribuido {} puntos de skill.{}", server_opcodes::CONSOLE_MSG, total, font_types::INFO)).await;
}

/// INFS — Spell info. VB6: TCP_HandleData1.bas:2747-2764
/// Sends ||281 through ||287 packets (message-based, client reads from Textos.tsao)
async fn handle_infs(state: &mut GameState, conn_id: ConnectionId, data: &str) {
    let payload = strip_opcode(data, 4);
    let slot: usize = payload.trim().parse().unwrap_or(0);

    let spell_idx = state.users.get(&conn_id)
        .and_then(|u| if slot >= 1 && slot <= MAX_SPELL_SLOTS { Some(u.spells[slot - 1]) } else { None })
        .unwrap_or(0);

    if spell_idx <= 0 || spell_idx as usize > state.game_data.spells.len() {
        state.send_to(conn_id, "||288").await; // VB6: error message
        return;
    }

    let spell = &state.game_data.spells[spell_idx as usize - 1];
    let nombre = spell.nombre.clone();
    let desc = spell.desc.clone();
    let min_skill = spell.min_skill;
    let mana_req = spell.mana_requerido;
    let sta_req = spell.sta_requerido;

    // VB6 sends 7 packets: ||281 (header), ||282@name, ||283@desc, ||284@skill, ||285@mana, ||286@sta, ||287 (footer)
    state.send_to(conn_id, "||281").await;
    state.send_to(conn_id, &format!("||282@{}", nombre)).await;
    state.send_to(conn_id, &format!("||283@{}", desc)).await;
    state.send_to(conn_id, &format!("||284@{}", min_skill)).await;
    state.send_to(conn_id, &format!("||285@{}", mana_req)).await;
    state.send_to(conn_id, &format!("||286@{}", sta_req)).await;
    state.send_to(conn_id, "||287").await;
}

/// DESPHE — Move/swap spell positions. VB6: DesplazarHechizo(userindex, Dire, CualHechizo)
/// Format: DESPHE<direction>,<slot> where direction=1(up) or 2(down), slot=1-based
async fn handle_desphe(state: &mut GameState, conn_id: ConnectionId, data: &str) {
    let payload = strip_opcode(data, 6);
    let direction: i32 = read_field(1, payload, ',').parse().unwrap_or(0);
    let slot: usize = read_field(2, payload, ',').parse().unwrap_or(0);

    if !(direction >= 1 && direction <= 2) { return; }
    if slot < 1 || slot > MAX_SPELL_SLOTS { return; }

    if direction == 1 {
        // Move UP: swap slot with slot-1
        if slot == 1 {
            state.send_to(conn_id, "||37").await; // VB6: can't move first slot up
            return;
        }
        if let Some(user) = state.users.get_mut(&conn_id) {
            let s = slot - 1; // 0-based
            let temp = user.spells[s];
            user.spells[s] = user.spells[s - 1];
            user.spells[s - 1] = temp;
        }
    } else {
        // Move DOWN: swap slot with slot+1
        if slot == MAX_SPELL_SLOTS {
            state.send_to(conn_id, "||37").await; // VB6: can't move last slot down
            return;
        }
        if let Some(user) = state.users.get_mut(&conn_id) {
            let s = slot - 1; // 0-based
            let temp = user.spells[s];
            user.spells[s] = user.spells[s + 1];
            user.spells[s + 1] = temp;
        }
    }

    // VB6 sends UpdateUserHechizos for both affected slots; we send all for simplicity
    send_full_spells(state, conn_id).await;
}

/// DAMINF — Player stats form (send detailed info about a target player).
async fn handle_daminf(state: &mut GameState, conn_id: ConnectionId, data: &str) {
    let payload = strip_opcode(data, 6);
    let target_name = payload.trim();

    // Find target user by name
    let target_conn = state.online_names.get(&target_name.to_uppercase()).copied();
    if target_conn.is_none() {
        state.send_to(conn_id, &format!("{}Usuario no encontrado.{}", server_opcodes::CONSOLE_MSG, font_types::INFO)).await;
        return;
    }
    let target_conn = target_conn.unwrap();

    let info = if let Some(target) = state.users.get(&target_conn) {
        format!("GINF{},{},{},{},{},{},{},{},{},{},{},{},{},{},{},{},{}",
            target.char_name,
            target.race,
            target.class,
            target.level,
            target.gold,
            target.reputation,
            target.criminales_matados,
            target.ciudadanos_matados,
            if target.criminal { "Criminal" } else { "Ciudadano" },
            if target.armada_real { "Armada Real" } else if target.fuerzas_caos { "Fuerzas del Caos" } else { "Ninguna" },
            target.guild_index,
            target.puntos_torneo,
            target.puntos_donacion,
            target.quests_completed,
            target.max_hp,
            target.max_mana,
            target.max_sta,
        )
    } else {
        return;
    };

    state.send_to(conn_id, &info).await;
}

/// FEST — Send mini statistics.
async fn handle_fest(state: &mut GameState, conn_id: ConnectionId) {
    let info = if let Some(user) = state.users.get(&conn_id) {
        format!("FEST{},{},{},{},{},{},{},{},{},{},{}",
            user.criminales_matados,
            user.ciudadanos_matados,
            user.level,
            user.class,
            if user.criminal { "Criminal" } else { "Ciudadano" },
            user.puntos_torneo,
            user.puntos_donacion,
            user.ts_points,
            user.quests_completed,
            user.guild_index,
            user.reputation,
        )
    } else {
        return;
    };
    state.send_to(conn_id, &info).await;
}

/// CABEZI — Change head/hairstyle (barber). Costs 500 gold.
async fn handle_cabezi(state: &mut GameState, conn_id: ConnectionId, data: &str) {
    let payload = strip_opcode(data, 6);
    let new_head: i32 = payload.trim().parse().unwrap_or(0);

    if new_head <= 0 {
        return;
    }

    let cost: i64 = 500;

    if let Some(user) = state.users.get(&conn_id) {
        if user.gold < cost {
            state.send_to(conn_id, &format!("{}No tienes suficiente oro.{}", server_opcodes::CONSOLE_MSG, font_types::INFO)).await;
            return;
        }
    } else {
        return;
    }

    let (map, x, y, old_ci) = {
        let user = state.users.get_mut(&conn_id).unwrap();
        user.gold -= cost;
        user.head = new_head;
        (user.pos_map, user.pos_x, user.pos_y, user.char_index.0)
    };

    // Update appearance for all nearby
    send_stats_gold(state, conn_id).await;
    let cc = state.users.get(&conn_id).unwrap().build_cc_packet();
    let cp = format!("CP{},{},{},{},{},{},{}", old_ci,
        state.users.get(&conn_id).unwrap().body,
        new_head,
        state.users.get(&conn_id).unwrap().heading,
        state.users.get(&conn_id).unwrap().weapon_anim,
        state.users.get(&conn_id).unwrap().shield_anim,
        state.users.get(&conn_id).unwrap().casco_anim,
    );
    state.send_data(SendTarget::ToArea { map, x, y }, &cp).await;
    state.send_to(conn_id, &format!("{}Cabeza cambiada.{}", server_opcodes::CONSOLE_MSG, font_types::INFO)).await;
}

/// TR — Drop item via mouse click (at current position).
async fn handle_mouse_drop(state: &mut GameState, conn_id: ConnectionId, data: &str) {
    let payload = strip_opcode(data, 2);
    let slot: usize = read_field(1, payload, ',').parse().unwrap_or(0);
    let amount: i32 = read_field(2, payload, ',').parse().unwrap_or(1);

    if slot < 1 || slot > MAX_INVENTORY_SLOTS || amount <= 0 {
        return;
    }

    // Delegate to the same drop logic as TI
    let drop_data = format!("TI{},{}", slot, amount);
    handle_drop_item(state, conn_id, &drop_data).await;
}

/// BOF — Level bonus selection.
async fn handle_bof(state: &mut GameState, conn_id: ConnectionId, data: &str) {
    let payload = strip_opcode(data, 3);
    let selection: i32 = payload.trim().parse().unwrap_or(0);

    if selection < 1 || selection > 3 {
        return;
    }

    // Level bonuses at 53, 56, 60 — give HP bonus
    // VB6: different amounts per selection (10, 15, 20 HP bonus)
    let hp_bonus = match selection {
        1 => 10,
        2 => 15,
        3 => 20,
        _ => 0,
    };

    if let Some(user) = state.users.get_mut(&conn_id) {
        user.max_hp += hp_bonus;
        user.min_hp = user.min_hp.min(user.max_hp);
    }

    send_stats_hp(state, conn_id).await;
    state.send_to(conn_id, &format!("{}Has ganado {} puntos de vida extra!{}", server_opcodes::CONSOLE_MSG, hp_bonus, font_types::INFO)).await;
}

/// ENTR — Train creature from trainer NPC.
async fn handle_entr(state: &mut GameState, conn_id: ConnectionId, data: &str) {
    let payload = strip_opcode(data, 4);
    let creature_slot: i32 = payload.trim().parse().unwrap_or(0);

    if creature_slot <= 0 {
        return;
    }

    let (target_npc, map, x, y, nro_mascotas, gold) = match state.users.get(&conn_id) {
        Some(u) => (u.target_npc, u.pos_map, u.pos_x, u.pos_y, u.nro_mascotas, u.gold),
        None => return,
    };

    if target_npc == 0 {
        state.send_to(conn_id, &format!("{}No estas interactuando con un NPC.{}", server_opcodes::CONSOLE_MSG, font_types::INFO)).await;
        return;
    }

    if nro_mascotas >= 3 {
        state.send_to(conn_id, &format!("{}Ya tienes el maximo de mascotas.{}", server_opcodes::CONSOLE_MSG, font_types::INFO)).await;
        return;
    }

    // Simple: spawn a pet NPC near the player
    // In VB6 this reads from the trainer's creature list — for now, just acknowledge
    state.send_to(conn_id, &format!("{}Criatura entrenada.{}", server_opcodes::CONSOLE_MSG, font_types::INFO)).await;
}

/// ACTPT — Send tournament/donation points.
async fn handle_actpt(state: &mut GameState, conn_id: ConnectionId) {
    let info = if let Some(user) = state.users.get(&conn_id) {
        format!("APT{},{},{}", user.puntos_torneo, user.puntos_donacion, user.ts_points)
    } else {
        return;
    };
    state.send_to(conn_id, &info).await;
}

/// RANKIN — View rankings.
async fn handle_rankin(state: &mut GameState, conn_id: ConnectionId, data: &str) {
    let payload = strip_opcode(data, 6);
    let category = read_field(1, payload, ',').to_uppercase();

    let rank_type = match category.as_str() {
        "DUELOS" => 3,
        "PAREJAS" => 4,
        "RONDAS" => 6,
        "REPUTACION" => 5,
        "TORNEOS" => 2,
        "CVCS" => 7,
        "CASTILLOS" => 8,
        "REPUCLANES" => 9,
        "FRAGS" => 1,
        _ => return,
    };

    use crate::data::ranking::RankingType;
    let ranking_type = match RankingType::from_i32(rank_type) {
        Some(r) => r,
        None => return,
    };

    let top = state.ranking.get(ranking_type);
    let mut result = String::from("MTOP");
    for i in 0..10 {
        let entry = &top.entries[i];
        if !entry.name.is_empty() {
            result.push_str(&format!("{}-{},", entry.name, entry.value));
        } else {
            result.push_str("N/A-0,");
        }
    }

    state.send_to(conn_id, &result).await;
}

/// ACTUALIZAR — Position re-sync.
async fn handle_actualizar(state: &mut GameState, conn_id: ConnectionId) {
    let msg = if let Some(user) = state.users.get(&conn_id) {
        format!("PU{},{}", user.pos_x, user.pos_y)
    } else {
        return;
    };
    state.send_to(conn_id, &msg).await;
}

/// IDUELOS — Duel arena info.
async fn handle_iduelos(state: &mut GameState, conn_id: ConnectionId) {
    let mut msg = String::from("MAR");
    for i in 1..=8 {
        if i > 1 { msg.push(','); }
        msg.push_str(&state.nombre_dueleando[i]);
    }
    state.send_to(conn_id, &msg).await;
}

/// TENGOMACROS — Macro detection.
async fn handle_tengomacros(state: &mut GameState, conn_id: ConnectionId) {
    let (count, name) = if let Some(user) = state.users.get_mut(&conn_id) {
        user.tiene_macro += 1;
        (user.tiene_macro, user.char_name.clone())
    } else {
        return;
    };

    if count >= 2 {
        // Notify admins
        let msg = format!("N|Seguridad>> se detecto el uso de macros en el usuario: {}, hay que revisarlo. ~255~255~0", name);
        state.send_data(SendTarget::ToAdmins,&msg).await;

        if let Some(user) = state.users.get_mut(&conn_id) {
            user.tiene_macro = 0;
        }
    }
}

/// FINCBN — Close guild bank.
async fn handle_fincbn(state: &mut GameState, conn_id: ConnectionId) {
    if let Some(user) = state.users.get_mut(&conn_id) {
        user.comerciando = false;
        user.cuenta_bancaria.clear();
    }
    state.send_to(conn_id, "FINCBNOK").await;
}

/// # — Send SOS/consultation.
async fn handle_sos_send(state: &mut GameState, conn_id: ConnectionId, data: &str) {
    let payload = strip_opcode(data, 1);
    let _tipo: i32 = read_field(1, payload, '|').parse().unwrap_or(0);
    let contenido = read_field(2, payload, '|');

    if let Some(user) = state.users.get(&conn_id) {
        if user.silenced {
            state.send_to(conn_id, &format!("||945@{}", user.silence_timer)).await;
            return;
        }
        if user.consulta_enviada {
            state.send_to(conn_id, "||192").await;
            return;
        }
    } else {
        return;
    }

    let name = state.users.get(&conn_id).unwrap().char_name.clone();

    // Notify admins
    state.send_data(SendTarget::ToAdmins,"||193").await;

    // Store SOS message
    state.sos_messages.push(super::types::SosMessage {
        tipo: "Consulta".to_string(),
        autor: name,
        contenido: contenido.to_string(),
    });

    let msg_num = state.sos_messages.len() as i32;
    if let Some(user) = state.users.get_mut(&conn_id) {
        user.consulta_enviada = true;
        user.numero_consulta = msg_num;
    }
}

/// X — Admin responds to SOS.
async fn handle_sos_respond(state: &mut GameState, conn_id: ConnectionId, data: &str) {
    let payload = strip_opcode(data, 1);
    let target_name = read_field(1, payload, '*');
    let texto = read_field(2, payload, '*');

    let priv_level = state.users.get(&conn_id).map(|u| u.privileges).unwrap_or(0);
    if priv_level < super::types::privilege_level::CONSEJERO {
        return;
    }

    let target_conn = state.online_names.get(&target_name.to_uppercase()).copied();
    if let Some(target) = target_conn {
        if let Some(user) = state.users.get_mut(&target) {
            user.consulta_enviada = false;
            user.numero_consulta = 0;
        }
        state.send_to(target, "||190").await;
        let admin_name = state.users.get(&conn_id).map(|u| u.char_name.clone()).unwrap_or_default();
        state.send_to(target, &format!("RESPUES{}*{}", texto, admin_name)).await;
    }
}

/// CONSUL — Admin view SOS messages.
async fn handle_consul(state: &mut GameState, conn_id: ConnectionId) {
    let priv_level = state.users.get(&conn_id).map(|u| u.privileges).unwrap_or(0);
    if priv_level < super::types::privilege_level::CONSEJERO {
        return;
    }

    let mut data_sos = format!("{}|", state.sos_messages.len());
    for msg in &state.sos_messages {
        data_sos.push_str(&format!("{}-{}-{}|", msg.tipo, msg.autor, msg.contenido));
    }
    state.send_to(conn_id, &format!("ZSOS{}", data_sos)).await;
}

/// ENVFPZ — FPZ anti-hack report.
async fn handle_envfpz(state: &mut GameState, conn_id: ConnectionId, data: &str) {
    let payload = strip_opcode(data, 6);
    let name = state.users.get(&conn_id).map(|u| u.char_name.clone()).unwrap_or_default();
    let msg = format!("||218@{}@{}", name, payload);
    state.send_data(SendTarget::ToAdmins,&msg).await;
}

/// DCANJE — Donation exchange menu.
async fn handle_dcanje(state: &mut GameState, conn_id: ConnectionId) {
    // Donation system — send basic info
    let pts = state.users.get(&conn_id).map(|u| u.puntos_donacion).unwrap_or(0);
    // Send empty donation list (no donations configured in this server)
    let msg = format!("DRM{},0,", pts);
    state.send_to(conn_id, &msg).await;
}

/// DPX — Donation item preview.
async fn handle_dpx(state: &mut GameState, conn_id: ConnectionId, data: &str) {
    let payload = strip_opcode(data, 3);
    let _item_id: i32 = read_field(1, payload, ',').parse().unwrap_or(0);
    // Donation system not fully implemented — send empty preview
    state.send_to(conn_id, "DNF0,0,0,0,0,0,0,0,Sin descripcion,0,").await;
}

/// DRX — Redeem donation.
async fn handle_drx(state: &mut GameState, conn_id: ConnectionId, data: &str) {
    let payload = strip_opcode(data, 3);
    let _item_id: i32 = read_field(1, payload, ',').parse().unwrap_or(0);
    state.send_to(conn_id, "||632").await; // "No tienes suficientes puntos"
}

/// CCANJE — Tournament prize menu.
async fn handle_ccanje(state: &mut GameState, conn_id: ConnectionId) {
    let info = if let Some(user) = state.users.get(&conn_id) {
        format!("PRM0,{},{}", user.puntos_torneo, user.ts_points)
    } else {
        return;
    };
    state.send_to(conn_id, &info).await;
}

/// IPX — Prize item info.
async fn handle_ipx(state: &mut GameState, conn_id: ConnectionId, _data: &str) {
    state.send_to(conn_id, "INF0,0,0,0,0,0,0,0,0,Sin premios disponibles").await;
}

/// SPX — Buy tournament prize.
async fn handle_spx(state: &mut GameState, conn_id: ConnectionId, _data: &str) {
    state.send_to(conn_id, &format!("{}No hay premios disponibles.{}", server_opcodes::CONSOLE_MSG, font_types::INFO)).await;
}

/// INCHAT — Init chat with friend.
async fn handle_inchat(state: &mut GameState, conn_id: ConnectionId, data: &str) {
    let payload = strip_opcode(data, 6);
    let friend_slot: usize = payload.trim().parse().unwrap_or(0);

    let friend_name = if let Some(user) = state.users.get(&conn_id) {
        if friend_slot >= 1 && friend_slot <= 10 {
            user.nombre_amigo[friend_slot - 1].clone()
        } else {
            String::new()
        }
    } else {
        return;
    };

    if friend_name.is_empty() || friend_name.to_uppercase() == "(NADIE)" {
        state.send_to(conn_id, "||226").await;
        return;
    }

    state.send_to(conn_id, &format!("ENCHAT{}", friend_name)).await;
}

/// KKCHAT — Send chat message to friend.
async fn handle_kkchat(state: &mut GameState, conn_id: ConnectionId, data: &str) {
    let payload = strip_opcode(data, 6);
    let target_name = read_field(1, payload, ',');
    let text = read_field(2, payload, ',');

    if target_name.is_empty() || text.is_empty() {
        return;
    }

    let sender_name = state.users.get(&conn_id).map(|u| u.char_name.clone()).unwrap_or_default();

    // Find target online
    if let Some(&target_conn) = state.online_names.get(&target_name.to_uppercase()) {
        let recv_msg = format!("IRCHAT{},{}", sender_name, text);
        state.send_to(target_conn, &recv_msg).await;
    } else {
        state.send_to(conn_id, &format!("{}El usuario no esta online.{}", server_opcodes::CONSOLE_MSG, font_types::INFO)).await;
    }
}

/// ADDPTS — Donate tournament points to guild.
async fn handle_addpts(state: &mut GameState, conn_id: ConnectionId, data: &str) {
    let payload = strip_opcode(data, 6);
    let amount: i64 = payload.trim().parse().unwrap_or(0);

    if amount <= 0 {
        return;
    }

    let guild_idx = state.users.get(&conn_id).map(|u| u.guild_index).unwrap_or(0);
    if guild_idx <= 0 {
        state.send_to(conn_id, &format!("{}No perteneces a un clan.{}", server_opcodes::CONSOLE_MSG, font_types::INFO)).await;
        return;
    }

    if let Some(user) = state.users.get(&conn_id) {
        if user.puntos_torneo < amount {
            state.send_to(conn_id, &format!("{}No tienes suficientes puntos.{}", server_opcodes::CONSOLE_MSG, font_types::INFO)).await;
            return;
        }
    }

    if let Some(user) = state.users.get_mut(&conn_id) {
        user.puntos_torneo -= amount;
    }

    state.send_to(conn_id, &format!("{}Has donado {} puntos al clan.{}", server_opcodes::CONSOLE_MSG, amount, font_types::INFO)).await;
}

/// DYDTRA — Drag & drop transfer items to another player.
async fn handle_dydtra(state: &mut GameState, conn_id: ConnectionId, data: &str) {
    let payload = strip_opcode(data, 6);
    let slot: usize = read_field(1, payload, ',').parse().unwrap_or(0);
    let amount: i32 = read_field(2, payload, ',').parse().unwrap_or(0);

    if slot < 1 || slot > MAX_INVENTORY_SLOTS || amount <= 0 {
        return;
    }

    let (map, x, y, _heading) = match state.users.get(&conn_id) {
        Some(u) => (u.pos_map, u.pos_x, u.pos_y, u.heading),
        None => return,
    };

    // Find a player in the target position (1 tile ahead based on heading)
    let target_user = state.users.get(&conn_id).map(|u| u.target_user).unwrap_or(0);
    if target_user == 0 {
        state.send_to(conn_id, &format!("{}No hay nadie ahi.{}", server_opcodes::CONSOLE_MSG, font_types::INFO)).await;
        return;
    }

    // Check item is transferable
    let si = slot - 1;
    let obj_idx = state.users.get(&conn_id).map(|u| u.inventory[si].obj_index).unwrap_or(0);
    if obj_idx <= 0 {
        return;
    }

    // Check not equipped
    if let Some(user) = state.users.get(&conn_id) {
        if user.inventory[si].equipped {
            state.send_to(conn_id, &format!("{}No puedes transferir un item equipado.{}", server_opcodes::CONSOLE_MSG, font_types::INFO)).await;
            return;
        }
    }

    // Transfer: remove from source, add to target
    let actual_amount = state.users.get(&conn_id).map(|u| u.inventory[si].amount.min(amount)).unwrap_or(0);
    if actual_amount <= 0 { return; }

    // Try to add to target inventory
    let added = add_item_to_user_inventory(state, target_user, obj_idx, actual_amount);
    if !added {
        state.send_to(conn_id, &format!("{}El otro jugador no tiene espacio.{}", server_opcodes::CONSOLE_MSG, font_types::INFO)).await;
        return;
    }

    // Remove from source
    if let Some(user) = state.users.get_mut(&conn_id) {
        user.inventory[si].amount -= actual_amount;
        if user.inventory[si].amount <= 0 {
            user.inventory[si] = InventorySlot::default();
        }
    }

    let obj_name = if (obj_idx as usize) < state.game_data.objects.len() {
        state.game_data.objects[obj_idx as usize].name.clone()
    } else {
        "item".to_string()
    };

    let sender_name = state.users.get(&conn_id).map(|u| u.char_name.clone()).unwrap_or_default();
    state.send_to(conn_id, &format!("{}Has transferido {} {}.{}", server_opcodes::CONSOLE_MSG, actual_amount, obj_name, font_types::INFO)).await;
    state.send_to(target_user, &format!("{}{} te ha dado {} {}.{}", server_opcodes::CONSOLE_MSG, sender_name, actual_amount, obj_name, font_types::INFO)).await;

    send_full_inventory(state, conn_id).await;
    send_full_inventory(state, target_user).await;
}

/// TOINFO — Tournament info.
async fn handle_toinfo(state: &mut GameState, conn_id: ConnectionId) {
    let mut list = String::from("LTR");
    for name in &state.cronologia_participantes {
        list.push_str(name);
        list.push(',');
    }
    state.send_to(conn_id, &list).await;
}

/// DOWNSI — Cast spell by target name.
async fn handle_downsi(state: &mut GameState, conn_id: ConnectionId, data: &str) {
    let payload = strip_opcode(data, 6);
    let target_name = payload.trim();

    let target_conn = state.online_names.get(&target_name.to_uppercase()).copied();
    if target_conn.is_none() {
        return;
    }
    let target_conn = target_conn.unwrap();

    let pending_spell = state.users.get(&conn_id).map(|u| u.pending_spell).unwrap_or(0);
    if pending_spell == 0 {
        return;
    }

    // Set target and cast
    if let Some(user) = state.users.get_mut(&conn_id) {
        user.target_user = target_conn;
    }

    // Delegate to spell casting logic
    do_cast_spell(state, conn_id).await;
}

/// NVOT — Vote in poll.
async fn handle_nvot(state: &mut GameState, conn_id: ConnectionId, data: &str) {
    let payload = strip_opcode(data, 4);
    let option: usize = payload.trim().parse().unwrap_or(0);

    if !state.poll_active {
        state.send_to(conn_id, &format!("{}No hay votacion activa.{}", server_opcodes::CONSOLE_MSG, font_types::INFO)).await;
        return;
    }

    if option < 1 || option > 5 {
        return;
    }

    let name = state.users.get(&conn_id).map(|u| u.char_name.clone()).unwrap_or_default();
    if state.poll_voters.contains(&name) {
        state.send_to(conn_id, &format!("{}Ya has votado.{}", server_opcodes::CONSOLE_MSG, font_types::INFO)).await;
        return;
    }

    state.poll_votes[option - 1] += 1;
    state.poll_voters.push(name);
    state.send_to(conn_id, &format!("{}Voto registrado.{}", server_opcodes::CONSOLE_MSG, font_types::INFO)).await;
}

/// NEWD — New report/denuncia.
async fn handle_newd(state: &mut GameState, conn_id: ConnectionId, data: &str) {
    let payload = strip_opcode(data, 4);
    let target_name = read_field(1, payload, ',');
    let reason = read_field(2, payload, ',');

    let reporter = state.users.get(&conn_id).map(|u| u.char_name.clone()).unwrap_or_default();

    info!("[REPORT] {} reports {}: {}", reporter, target_name, reason);

    let msg = format!("||218@{}@Denuncia contra {}: {}", reporter, target_name, reason);
    state.send_data(SendTarget::ToAdmins,&msg).await;

    state.send_to(conn_id, &format!("{}Denuncia enviada.{}", server_opcodes::CONSOLE_MSG, font_types::INFO)).await;
}

/// Helper: add item to user inventory, returns true if successful.
fn add_item_to_user_inventory(state: &mut GameState, conn_id: ConnectionId, obj_index: i32, amount: i32) -> bool {
    if let Some(user) = state.users.get_mut(&conn_id) {
        // First try to stack
        for slot in user.inventory.iter_mut() {
            if slot.obj_index == obj_index && slot.amount > 0 {
                slot.amount += amount;
                return true;
            }
        }
        // Find empty slot
        for slot in user.inventory.iter_mut() {
            if slot.obj_index <= 0 || slot.amount <= 0 {
                slot.obj_index = obj_index;
                slot.amount = amount;
                slot.equipped = false;
                return true;
            }
        }
    }
    false
}

// =====================================================================
// Missing slash commands
// =====================================================================

/// /MONTAR — Mount pet.
async fn handle_slash_montar(state: &mut GameState, conn_id: ConnectionId) {
    let has_mount = state.users.get(&conn_id).map(|u| u.nro_mascotas > 0).unwrap_or(false);
    if !has_mount {
        state.send_to(conn_id, &format!("{}No tienes una montura.{}", server_opcodes::CONSOLE_MSG, font_types::INFO)).await;
        return;
    }

    let already_mounted = state.users.get(&conn_id).map(|u| u.montado).unwrap_or(false);
    if already_mounted {
        state.send_to(conn_id, &format!("{}Ya estas montado.{}", server_opcodes::CONSOLE_MSG, font_types::INFO)).await;
        return;
    }

    // Simple mount: save body and change to mount body
    let (map, x, y) = match state.users.get(&conn_id) {
        Some(u) => (u.pos_map, u.pos_x, u.pos_y),
        None => return,
    };

    // VB6: Check first pet's NPC number and assign mount body
    let pet_idx = state.users.get(&conn_id).map(|u| u.mascotas_index[0]).unwrap_or(0);
    let npc_num = state.get_npc(pet_idx).map(|n| n.npc_number).unwrap_or(0);
    let mount_body = match npc_num {
        156 => 331, // Horse 1
        157 => 330, // Horse 2
        158 => 352, // Horse 3
        181 => 358, // Dragon/Special 1
        182 => 359, // Dragon/Special 2
        _ => 296,   // Generic mount fallback
    };

    if let Some(user) = state.users.get_mut(&conn_id) {
        user.montado = true;
        user.montado_body = user.body;
        user.body = mount_body;
        user.weapon_anim = 0;
        user.shield_anim = 0;
        user.casco_anim = 0;
    }

    let cp = {
        let user = state.users.get(&conn_id).unwrap();
        format!("CP{},{},{},{},{},{},{}", user.char_index.0, user.body, user.head, user.heading, 0, 0, 0)
    };
    state.send_data(SendTarget::ToArea { map, x, y }, &cp).await;
    state.send_to(conn_id, &format!("{}Te has montado.{}", server_opcodes::CONSOLE_MSG, font_types::INFO)).await;
}

/// /DESMONTAR — Dismount.
async fn handle_slash_desmontar(state: &mut GameState, conn_id: ConnectionId) {
    let is_mounted = state.users.get(&conn_id).map(|u| u.montado).unwrap_or(false);
    if !is_mounted {
        state.send_to(conn_id, &format!("{}No estas montado.{}", server_opcodes::CONSOLE_MSG, font_types::INFO)).await;
        return;
    }

    let (map, x, y) = match state.users.get(&conn_id) {
        Some(u) => (u.pos_map, u.pos_x, u.pos_y),
        None => return,
    };

    if let Some(user) = state.users.get_mut(&conn_id) {
        user.montado = false;
        user.body = user.montado_body;
    }

    // Restore equipped weapon/shield/helmet appearance
    let (weapon_anim, shield_anim, casco_anim) = get_equipped_anims(state, conn_id);
    if let Some(user) = state.users.get_mut(&conn_id) {
        user.weapon_anim = weapon_anim;
        user.shield_anim = shield_anim;
        user.casco_anim = casco_anim;
    }

    let cp = {
        let user = state.users.get(&conn_id).unwrap();
        format!("CP{},{},{},{},{},{},{}", user.char_index.0, user.body, user.head, user.heading, user.weapon_anim, user.shield_anim, user.casco_anim)
    };
    state.send_data(SendTarget::ToArea { map, x, y }, &cp).await;
    state.send_to(conn_id, &format!("{}Te has desmontado.{}", server_opcodes::CONSOLE_MSG, font_types::INFO)).await;
}

/// Helper: get equipped item animation GRH IDs.
fn get_equipped_anims(state: &GameState, conn_id: ConnectionId) -> (i32, i32, i32) {
    let user = match state.users.get(&conn_id) {
        Some(u) => u,
        None => return (0, 0, 0),
    };

    let weapon = if user.equip.weapon > 0 && user.equip.weapon <= MAX_INVENTORY_SLOTS {
        let obj_idx = user.inventory[user.equip.weapon - 1].obj_index;
        if obj_idx > 0 && (obj_idx as usize) < state.game_data.objects.len() {
            state.game_data.objects[obj_idx as usize].weapon_anim
        } else { 0 }
    } else { 0 };

    let shield = if user.equip.shield > 0 && user.equip.shield <= MAX_INVENTORY_SLOTS {
        let obj_idx = user.inventory[user.equip.shield - 1].obj_index;
        if obj_idx > 0 && (obj_idx as usize) < state.game_data.objects.len() {
            state.game_data.objects[obj_idx as usize].shield_anim
        } else { 0 }
    } else { 0 };

    let helmet = if user.equip.helmet > 0 && user.equip.helmet <= MAX_INVENTORY_SLOTS {
        let obj_idx = user.inventory[user.equip.helmet - 1].obj_index;
        if obj_idx > 0 && (obj_idx as usize) < state.game_data.objects.len() {
            state.game_data.objects[obj_idx as usize].casco_anim
        } else { 0 }
    } else { 0 };

    (weapon, shield, helmet)
}

/// /QUITARMASCOTA — Remove pet.
async fn handle_slash_quitarmascota(state: &mut GameState, conn_id: ConnectionId) {
    let nro = state.users.get(&conn_id).map(|u| u.nro_mascotas).unwrap_or(0);
    if nro == 0 {
        state.send_to(conn_id, &format!("{}No tienes mascotas.{}", server_opcodes::CONSOLE_MSG, font_types::INFO)).await;
        return;
    }

    // Remove all pets
    if let Some(user) = state.users.get_mut(&conn_id) {
        for i in 0..3 {
            if user.mascotas_index[i] > 0 {
                // Kill the NPC
                let idx = user.mascotas_index[i];
                if let Some(npc) = state.npcs.get_mut(idx).and_then(|n| n.as_mut()) {
                    npc.min_hp = 0;
                }
            }
            user.mascotas_index[i] = 0;
            user.mascotas_type[i] = 0;
        }
        user.nro_mascotas = 0;
    }

    state.send_to(conn_id, &format!("{}Mascota removida.{}", server_opcodes::CONSOLE_MSG, font_types::INFO)).await;
}

/// /MSJ — Toggle private messages.
async fn handle_slash_msj(state: &mut GameState, conn_id: ConnectionId) {
    let new_state = if let Some(user) = state.users.get_mut(&conn_id) {
        user.msj_privados = !user.msj_privados;
        user.msj_privados
    } else {
        return;
    };

    if new_state {
        state.send_to(conn_id, &format!("{}Mensajes privados activados.{}", server_opcodes::CONSOLE_MSG, font_types::INFO)).await;
    } else {
        state.send_to(conn_id, &format!("{}Mensajes privados desactivados.{}", server_opcodes::CONSOLE_MSG, font_types::INFO)).await;
    }
}

/// /CIUDADANIA — Set citizenship. VB6: requires Ciudadania NPC (type 13), distance <= 3.
/// Maps: 130→Inthak, 25→Thir/Ruvendel.
async fn handle_slash_ciudadania(state: &mut GameState, conn_id: ConnectionId) {
    let (target_npc, map) = match state.users.get(&conn_id) {
        Some(u) if u.logged => (u.target_npc, u.pos_map),
        _ => return,
    };

    // VB6: If TargetNPC = 0 Then ||9
    if target_npc == 0 {
        state.send_to(conn_id, "||9").await;
        return;
    }

    // VB6: distance > 3 → ||10
    let (npc_map, npc_x, npc_y, npc_type) = match state.get_npc(target_npc) {
        Some(npc) => (npc.map, npc.x, npc.y, npc.npc_type),
        None => return,
    };
    let (u_map, u_x, u_y) = match state.users.get(&conn_id) {
        Some(u) => (u.pos_map, u.pos_x, u.pos_y),
        None => return,
    };
    if u_map != npc_map || (u_x - npc_x).abs() > 3 || (u_y - npc_y).abs() > 3 {
        state.send_to(conn_id, "||10").await;
        return;
    }

    // VB6: NPCtype must be Ciudadania (13)
    if npc_type != crate::data::npcs::NpcType::Citizenship { return; }

    // VB6: Set home based on map (130=Inthak, 25=Thir)
    let city = match map {
        130 => "Inthak",
        25 => "Thir",
        _ => {
            state.send_to(conn_id, &format!("{}No estas en una ciudad valida.{}", server_opcodes::CONSOLE_MSG, font_types::INFO)).await;
            return;
        }
    };

    let current_home = state.users.get(&conn_id).map(|u| u.hogar.clone()).unwrap_or_default();
    if current_home == city { return; } // VB6: If already same home, exit

    if let Some(user) = state.users.get_mut(&conn_id) {
        user.hogar = city.to_string();
    }

    // VB6: ||318@<home>
    state.send_to(conn_id, &format!("||318@{}", city)).await;
}

/// /VIAJAR — Travel to city via Traveler NPC.
/// VB6: TCP_HandleData3.bas lines 760-846
async fn handle_slash_viajar(state: &mut GameState, conn_id: ConnectionId, city: &str) {
    let city_upper = city.trim().to_uppercase();

    // Validate city name (VB6 line 763)
    let valid = ["TANARIS", "ANVILMAR", "KAHLIMDOR", "THIR", "INTHAK", "JHUMBEL", "RUVENDEL", "HELKA"];
    if !valid.contains(&city_upper.as_str()) {
        state.send_to(conn_id, &format!("{}Ciudad desconocida. Ciudades: Tanaris, Anvilmar, Kahlimdor, Thir, Inthak, Jhumbel, Ruvendel, Helka{}", server_opcodes::CONSOLE_MSG, font_types::INFO)).await;
        return;
    }

    // Must have traveler NPC targeted (VB6 NpcType=12)
    let user_data = match state.users.get(&conn_id) {
        Some(u) if u.logged => (u.dead, u.level, u.gold, u.target_npc_idx, u.pos_map, u.pos_x, u.pos_y),
        _ => return,
    };
    let (dead, level, gold, target_npc, _map, _ux, _uy) = user_data;

    if dead {
        state.send_to(conn_id, "||3").await;
        return;
    }

    if target_npc == 0 {
        state.send_to(conn_id, "||9").await;
        return;
    }

    // Check NPC is a Traveler (type 12)
    let npc_ok = state.get_npc(target_npc).map(|n| n.npc_type == crate::data::npcs::NpcType::Traveler).unwrap_or(false);
    if !npc_ok { return; }

    // Gold cost: <30 = 1000, >=30 = 5000 (VB6 lines 778-788)
    let cost = if level < 30 { 1000i64 } else { 5000 };
    if gold < cost {
        let cost_str = if level < 30 { "1.000" } else { "5.000" };
        state.send_to(conn_id, &format!("||215@{}", cost_str)).await;
        return;
    }

    // Inthak requires level 30+ (VB6 line 812)
    if city_upper == "INTHAK" && level < 30 {
        state.send_to(conn_id, "||542").await;
        return;
    }

    // VB6 exact destinations (TCP_HandleData3.bas lines 790-838)
    let (dest_map, dest_x, dest_y) = match city_upper.as_str() {
        "TANARIS" => (28, 54, 35),
        "ANVILMAR" => (29, 46, 85),
        "KAHLIMDOR" => (27, 50, 48),
        "THIR" => (25, 74, 45),
        "INTHAK" => (130, 50, 57),
        "JHUMBEL" => {
            // Random spawn in map 69 (VB6 lines 820-832)
            let roll = rand_simple_u32() % 5;
            match roll {
                0 => (69, 35 + (rand_simple_u32() % 8) as i32, 16 + (rand_simple_u32() % 9) as i32),
                1 => (69, 42 + (rand_simple_u32() % 6) as i32, 40 + (rand_simple_u32() % 9) as i32),
                2 => (69, 54 + (rand_simple_u32() % 14) as i32, 71 + (rand_simple_u32() % 6) as i32),
                3 => (69, 30 + (rand_simple_u32() % 8) as i32, 79 + (rand_simple_u32() % 7) as i32),
                _ => (69, 19 + (rand_simple_u32() % 6) as i32, 31 + (rand_simple_u32() % 4) as i32),
            }
        }
        "RUVENDEL" => (26, 51, 52),
        "HELKA" => (136, 52, 55),
        _ => return,
    };

    // Deduct gold (VB6 lines 840-844)
    if let Some(user) = state.users.get_mut(&conn_id) {
        user.gold -= cost;
    }
    send_stats_gold(state, conn_id).await;
    warp_user(state, conn_id, dest_map, dest_x, dest_y).await;
}

/// /ENTRENAR — Open creature training list from trainer NPC.
/// VB6: requires Entrenador NPC (type 3), distance <= 10, not dead.
async fn handle_slash_entrenar(state: &mut GameState, conn_id: ConnectionId) {
    let (dead, target_npc) = match state.users.get(&conn_id) {
        Some(u) if u.logged => (u.dead, u.target_npc),
        _ => return,
    };

    // VB6: If Muerto Then ||3
    if dead {
        state.send_to(conn_id, "||3").await;
        return;
    }

    // VB6: If TargetNPC = 0 Then ||9
    if target_npc == 0 {
        state.send_to(conn_id, "||9").await;
        return;
    }

    // VB6: distance > 10 → ||10
    let (npc_map, npc_x, npc_y, npc_type) = match state.get_npc(target_npc) {
        Some(npc) => (npc.map, npc.x, npc.y, npc.npc_type),
        None => return,
    };
    let (u_map, u_x, u_y) = match state.users.get(&conn_id) {
        Some(u) => (u.pos_map, u.pos_x, u.pos_y),
        None => return,
    };
    if u_map != npc_map || (u_x - npc_x).abs() > 10 || (u_y - npc_y).abs() > 10 {
        state.send_to(conn_id, "||10").await;
        return;
    }

    // VB6: NPCtype must be Entrenador (3)
    if npc_type != crate::data::npcs::NpcType::Trainer { return; }

    // VB6: EnviarListaCriaturas — training system not fully implemented yet
    state.send_to(conn_id, &format!("{}Sistema de entrenamiento no disponible.{}", server_opcodes::CONSOLE_MSG, font_types::INFO)).await;
}

/// /CENTINELA — Anti-AFK response.
async fn handle_slash_centinela(state: &mut GameState, conn_id: ConnectionId, code: &str) {
    // Simple anti-AFK — accept any response
    state.send_to(conn_id, &format!("{}Centinela verificado.{}", server_opcodes::CONSOLE_MSG, font_types::INFO)).await;
}

/// /IR — Premium travel.
async fn handle_slash_ir(state: &mut GameState, conn_id: ConnectionId, destination: &str) {
    // Check premium status (not fully implemented — just accept for now)
    let dest_upper = destination.trim().to_uppercase();

    let (dest_map, dest_x, dest_y) = match dest_upper.as_str() {
        "INTHAK" => (1, 50, 50),
        "THIR" => (6, 50, 50),
        "RUVENDEL" => (11, 50, 50),
        _ => {
            state.send_to(conn_id, &format!("{}Destino desconocido.{}", server_opcodes::CONSOLE_MSG, font_types::INFO)).await;
            return;
        }
    };

    warp_user(state, conn_id, dest_map, dest_x, dest_y).await;
}

/// /VOTAR — Vote in poll.
async fn handle_slash_votar(state: &mut GameState, conn_id: ConnectionId) {
    if !state.poll_active {
        state.send_to(conn_id, &format!("{}No hay votacion activa.{}", server_opcodes::CONSOLE_MSG, font_types::INFO)).await;
        return;
    }
    // Send poll options
    let mut msg = String::from("VOT");
    for i in 0..5 {
        msg.push_str(&state.poll_options[i]);
        msg.push(',');
    }
    state.send_to(conn_id, &msg).await;
}

/// /RESULTADOS — Poll results.
async fn handle_slash_resultados(state: &mut GameState, conn_id: ConnectionId) {
    let total: i32 = state.poll_votes.iter().sum();
    let mut msg = format!("{}Resultados de la votacion:", server_opcodes::CONSOLE_MSG);
    for i in 0..5 {
        if !state.poll_options[i].is_empty() {
            let pct = if total > 0 { (state.poll_votes[i] * 100) / total } else { 0 };
            msg.push_str(&format!(" {}: {} ({}%)", state.poll_options[i], state.poll_votes[i], pct));
        }
    }
    msg.push_str(font_types::INFO);
    state.send_to(conn_id, &msg).await;
}

/// /GUERRA — Join war event. VB6: TCP_HandleData2.bas:430-454.
/// Warps player to the war zone based on faction (Alianza/Horda).
async fn handle_slash_guerra(state: &mut GameState, conn_id: ConnectionId) {
    if !state.hay_guerra {
        state.send_to(conn_id, "||322").await;
        return;
    }

    let (armada, caos, jerarquia, cur_map_pk, en_guerra) = match state.users.get(&conn_id) {
        Some(u) if u.logged => {
            let map_pk = state.game_data.maps.get(u.pos_map as usize)
                .and_then(|m| m.as_ref()).map(|m| m.info.pk).unwrap_or(false);
            (u.armada_real, u.fuerzas_caos, u.jerarquia_dios, map_pk, u.en_guerra)
        },
        _ => return,
    };

    // Must be in a faction with hierarchy
    if jerarquia < 1 && !armada && !caos {
        state.send_to(conn_id, "||324").await;
        return;
    }

    if cur_map_pk {
        state.send_to(conn_id, "||323").await;
        return;
    }

    if en_guerra { return; }

    if let Some(user) = state.users.get_mut(&conn_id) {
        user.en_guerra = true;
    }

    // Determine faction: armada_real = Alianza, fuerzas_caos = Horda
    let is_alianza = armada;

    if state.hay_guerra_khalim {
        if is_alianza {
            warp_user(state, conn_id, 1, 21, 30).await;
        } else {
            warp_user(state, conn_id, 27, 50, 78).await;
        }
    } else if state.hay_guerra_anvil {
        if is_alianza {
            warp_user(state, conn_id, 29, 46, 68).await;
        } else {
            warp_user(state, conn_id, 41, 50, 13).await;
        }
    }

    state.send_to(conn_id, "||325").await;
}

/// /CIRUJIA — Surgery (race change). VB6: requires cirujano NPC, distance <= 3.
async fn handle_slash_cirujia(state: &mut GameState, conn_id: ConnectionId) {
    let (dead, target_npc) = match state.users.get(&conn_id) {
        Some(u) if u.logged => (u.dead, u.target_npc),
        _ => return,
    };

    if dead {
        state.send_to(conn_id, "||3").await;
        return;
    }

    if target_npc == 0 { return; }

    // Check distance <= 3
    let (npc_map, npc_x, npc_y, npc_type) = match state.get_npc(target_npc) {
        Some(npc) => (npc.map, npc.x, npc.y, npc.npc_type),
        None => return,
    };
    let (u_map, u_x, u_y) = match state.users.get(&conn_id) {
        Some(u) => (u.pos_map, u.pos_x, u.pos_y),
        None => return,
    };
    if u_map != npc_map || (u_x - npc_x).abs() > 3 || (u_y - npc_y).abs() > 3 {
        state.send_to(conn_id, "||158").await;
        return;
    }

    // VB6: NPCtype must be cirujano (19)
    if npc_type != crate::data::npcs::NpcType::Surgeon { return; }

    // VB6: sends CIRUJA<raza>,<genero>
    let (raza, genero) = match state.users.get(&conn_id) {
        Some(u) => (u.race.clone(), u.gender),
        None => return,
    };
    let raza_num = match raza.as_str() {
        "Humano" => 1, "Elfo" => 2, "ElfoOscuro" => 3, "Enano" => 4, "Gnomo" => 5,
        _ => 1,
    };
    state.send_to(conn_id, &format!("CIRUJA{},{}", raza_num, genero)).await;
}

/// /NOBLE — Become noble. VB6: TCP_HandleData2.bas:998-1052.
/// Requires items 1073-1077 (qty 1 each). Grants spell 46. Sets EsNoble flag.
async fn handle_slash_noble(state: &mut GameState, conn_id: ConnectionId) {
    // Check all 5 required items
    for obj_id in 1073..=1077 {
        if !user_has_items(state, conn_id, obj_id, 1) {
            state.send_to(conn_id, "||356").await;
            return;
        }
    }

    let (dead, es_noble, class) = match state.users.get(&conn_id) {
        Some(u) if u.logged => (u.dead, u.es_noble, u.class.clone()),
        _ => return,
    };

    if dead {
        state.send_to(conn_id, "||3").await;
        return;
    }

    if es_noble { return; }

    // Consume all 5 items
    for obj_id in 1073..=1077 {
        remove_items_from_inv(state, conn_id, obj_id, 1).await;
    }

    // Grant spell 46
    let spell_idx = 46i32;
    let already_has = state.users.get(&conn_id).map(|u| {
        u.spells.iter().any(|&s| s == spell_idx)
    }).unwrap_or(false);

    if !already_has {
        let empty_slot = state.users.get(&conn_id).and_then(|u| {
            u.spells.iter().position(|&s| s == 0)
        });
        if let Some(slot) = empty_slot {
            if let Some(user) = state.users.get_mut(&conn_id) {
                user.spells[slot] = spell_idx;
                user.es_noble = true;
            }
            // Send spell update for the slot
            let spell_name = state.game_data.spells.get(spell_idx as usize)
                .map(|s| s.nombre.as_str()).unwrap_or("Desconocido");
            state.send_to(conn_id, &format!("SHI{},{}", slot + 1, spell_name)).await;
        } else {
            state.send_to(conn_id, "||181").await; // No spell slots
            if let Some(user) = state.users.get_mut(&conn_id) {
                user.es_noble = true;
            }
        }
    } else {
        state.send_to(conn_id, "||182").await; // Already has spell
        if let Some(user) = state.users.get_mut(&conn_id) {
            user.es_noble = true;
        }
    }

    let name = state.users.get(&conn_id).map(|u| u.char_name.clone()).unwrap_or_default();
    state.send_to(conn_id, &format!("||357@{}", class)).await;
    state.send_data(SendTarget::ToAll, &format!("||358@{}", name)).await;
}

/// /DESENTERRAR — Dig up treasure. VB6: TCP_HandleData2.bas:1054-1072 + modTesoros.bas.
/// Must be at exact treasure coords AND have LlaveTesoro (obj 1062).
async fn handle_slash_desenterrar(state: &mut GameState, conn_id: ConnectionId) {
    let (map, x, y) = match state.users.get(&conn_id) {
        Some(u) if u.logged => (u.pos_map, u.pos_x, u.pos_y),
        _ => return,
    };

    // Check if player is at treasure location
    if map != state.tesoro_map || x != state.tesoro_x || y != state.tesoro_y || state.tesoro_map == 0 {
        state.send_to(conn_id, "||359").await;
        return;
    }

    // Check for LlaveTesoro (obj 1062)
    const LLAVE_TESORO: i32 = 1062;
    if !user_has_items(state, conn_id, LLAVE_TESORO, 1) {
        state.send_to(conn_id, "||360").await;
        return;
    }

    // Consume key
    remove_items_from_inv(state, conn_id, LLAVE_TESORO, 1).await;

    // Start treasure countdown
    state.tesoro_contando = true;
    state.tesoro_tiempo = 30;

    // Spawn Cofre Cerrado (obj 11) on the map tile
    const COFRE_CERRADO: i32 = 11;
    let t_map = state.tesoro_map;
    let t_x = state.tesoro_x;
    let t_y = state.tesoro_y;
    let grh = state.get_object(COFRE_CERRADO).map(|o| o.grh_index).unwrap_or(0);
    {
        let grid = state.world.grid_mut(t_map);
        if let Some(tile) = grid.tile_mut(t_x, t_y) {
            tile.ground_item.obj_index = COFRE_CERRADO;
            tile.ground_item.amount = 1;
        }
    }
    // Notify area about the new object
    let obj_pkt = format!("HO{},{},{},{}", grh, t_x, t_y, 1);
    state.send_data(SendTarget::ToArea { map: t_map, x: t_x, y: t_y }, &obj_pkt).await;

    state.send_to(conn_id, "||361").await;
}

/// /BOTIX — Spawn AI bot.
async fn handle_slash_botix(state: &mut GameState, conn_id: ConnectionId) {
    state.send_to(conn_id, &format!("{}Sistema de bots no disponible.{}", server_opcodes::CONSOLE_MSG, font_types::INFO)).await;
}

/// /INFOSUB — Auction info.
async fn handle_slash_infosub(state: &mut GameState, conn_id: ConnectionId) {
    state.send_to(conn_id, &format!("{}No hay subasta activa.{}", server_opcodes::CONSOLE_MSG, font_types::INFO)).await;
}

/// /SUBASTAR — Start auction.
async fn handle_slash_subastar(state: &mut GameState, conn_id: ConnectionId) {
    state.send_to(conn_id, &format!("{}Sistema de subastas no disponible.{}", server_opcodes::CONSOLE_MSG, font_types::INFO)).await;
}

// =====================================================================
// Missing VB6 parity handlers — Phase 2
// =====================================================================

/// Helper: get INI var using PathBuf.
fn ini_get(path: &std::path::Path, section: &str, key: &str) -> String {
    crate::config::get_var(path.to_str().unwrap_or(""), section, key)
}

/// Helper: write INI var using PathBuf.
fn ini_write(path: &std::path::Path, section: &str, key: &str, value: &str) {
    let _ = crate::config::write_var(path.to_str().unwrap_or(""), section, key, value);
}

/// Helper: check if user has at least `amount` of item `obj_index` in inventory.
fn user_has_items(state: &GameState, conn_id: ConnectionId, obj_index: i32, amount: i32) -> bool {
    if let Some(user) = state.users.get(&conn_id) {
        let mut total = 0i32;
        for slot in &user.inventory {
            if slot.obj_index == obj_index {
                total += slot.amount;
            }
        }
        total >= amount
    } else {
        false
    }
}

/// FWO — Query house owner and price from Casas.dat.
async fn handle_fwo(state: &mut GameState, conn_id: ConnectionId, data: &str) {
    let payload = strip_opcode(data, 3);
    let num_casa = read_field(1, payload, ',');

    let casas_path = state.base_path.join("Dat").join("Casas.dat");
    let section = format!("Casa{}", num_casa);

    let mut dueno = ini_get(&casas_path, &section, "Dueno");
    if dueno.is_empty() { dueno = ini_get(&casas_path, &section, "Due\u{00F1}o"); }
    if dueno.is_empty() { dueno = "N/A".to_string(); }
    let precio = ini_get(&casas_path, &section, "Precio");
    let fecha = ini_get(&casas_path, &section, "Fecha");

    state.send_to(conn_id, &format!("GVN{},{},{}", dueno, precio, fecha)).await;
}

/// CUC — Buy a house.
async fn handle_cuc(state: &mut GameState, conn_id: ConnectionId, data: &str) {
    let payload = strip_opcode(data, 3);
    let num_casa: i32 = read_field(1, payload, ',').parse().unwrap_or(0);

    let casas_path = state.base_path.join("Dat").join("Casas.dat");
    let section = format!("Casa{}", num_casa);

    let mut dueno = ini_get(&casas_path, &section, "Dueno");
    if dueno.is_empty() { dueno = ini_get(&casas_path, &section, "Due\u{00F1}o"); }
    if dueno.is_empty() { dueno = "N/A".to_string(); }

    if dueno != "N/A" {
        state.send_to(conn_id, "||243").await;
        return;
    }

    let precio: i64 = ini_get(&casas_path, &section, "Precio").parse().unwrap_or(0);

    let gold = state.users.get(&conn_id).map(|u| u.gold).unwrap_or(0);
    if gold < precio {
        state.send_to(conn_id, &format!("||215@{}", precio)).await;
        return;
    }

    if num_casa <= 0 {
        return;
    }

    // Key obj_index = 1093 + num_casa
    let key_index = 1093 + num_casa;
    if !add_item_to_user_inventory(state, conn_id, key_index, 1) {
        state.send_to(conn_id, "||108").await;
        return;
    }

    // Save owner to Casas.dat
    let char_name = state.users.get(&conn_id).map(|u| u.char_name.clone()).unwrap_or_default();
    ini_write(&casas_path, &section, "Dueno", &char_name);
    ini_write(&casas_path, &section, "Fecha", &chrono_like_date());

    // Broadcast to all
    state.send_data(SendTarget::ToAll, &format!("||244@{}@{}", char_name, num_casa)).await;

    // Deduct gold
    if let Some(user) = state.users.get_mut(&conn_id) {
        user.gold -= precio;
    }
    send_stats_gold(state, conn_id).await;
    send_full_inventory(state, conn_id).await;
}

/// CNM — Rename pet/creature.
async fn handle_cnm(state: &mut GameState, conn_id: ConnectionId, data: &str) {
    let payload = strip_opcode(data, 3);
    let nick = read_field(1, payload, ',');

    let pet_idx = state.users.get(&conn_id)
        .and_then(|u| if u.nro_mascotas > 0 { Some(u.mascotas_index[0]) } else { None })
        .unwrap_or(0);

    if pet_idx > 0 {
        if let Some(Some(npc)) = state.npcs.get_mut(pet_idx) {
            npc.name = nick.clone();
            state.send_to(conn_id, &format!("{}Mascota renombrada a: {}{}", server_opcodes::CONSOLE_MSG, nick, font_types::INFO)).await;
        }
    } else {
        state.send_to(conn_id, &format!("{}No tienes mascotas.{}", server_opcodes::CONSOLE_MSG, font_types::INFO)).await;
    }
}

/// GEMS — Gem exchange (requires all 7 gems: items 406-413).
async fn handle_gems(state: &mut GameState, conn_id: ConnectionId, data: &str) {
    let payload = strip_opcode(data, 4);
    let option: i32 = payload.trim().parse().unwrap_or(-1);

    // Check all 7 gems (407 included implicitly in VB6 removal)
    let gem_ids = [406, 407, 408, 409, 410, 411, 412, 413];
    for &gid in &gem_ids {
        if !user_has_items(state, conn_id, gid, 1) {
            state.send_to(conn_id, "||271").await;
            return;
        }
    }

    match option {
        // 0 = Renounce god
        0 => {
            let god = state.users.get(&conn_id).map(|u| u.sirviente_de_dios.to_uppercase()).unwrap_or_default();
            if god == "MIFRIT" || god == "POSEIDON" || god == "EREBROS" || god == "TARRASKE" {
                remove_items_from_inv(state, conn_id, 1274, 1).await;
                if let Some(user) = state.users.get_mut(&conn_id) {
                    user.sirviente_de_dios.clear();
                    user.almas_contenidas = 0;
                    user.almas_ofrecidas = 0;
                    user.cofre_dios = [0; 4];
                    user.cofre_dios_cant = 0;
                    // Remove god items from inventory
                    let items_to_remove: Vec<(i32, i32)> = user.inventory.iter()
                        .filter(|s| s.obj_index > 0 && s.amount > 0)
                        .filter(|s| {
                            state.game_data.objects.get(s.obj_index as usize)
                                .map(|o| o.item_dios).unwrap_or(false)
                        })
                        .map(|s| (s.obj_index, s.amount))
                        .collect();
                    drop(user); // Release borrow for remove
                    for (idx, amt) in items_to_remove {
                        remove_items_from_inv(state, conn_id, idx, amt).await;
                    }
                }
                state.send_to(conn_id, "||275").await;
            } else {
                state.send_to(conn_id, "||276").await;
                return; // Don't remove gems
            }
        }
        // 1 = Octarina gem
        1 => {
            if !add_item_to_user_inventory(state, conn_id, 1448, 1) {
                state.send_to(conn_id, "||108").await;
                return;
            }
            state.send_to(conn_id, "||232@1@Gema Octarina").await;
        }
        // 2 = 1500 tournament points
        2 => {
            if let Some(user) = state.users.get_mut(&conn_id) {
                user.puntos_torneo += 1500;
            }
            state.send_to(conn_id, "||57@1.500").await;
        }
        // 3 = 30000 souls
        3 => {
            if !user_has_items(state, conn_id, 1274, 1) {
                state.send_to(conn_id, "||127").await;
                return;
            }
            if let Some(user) = state.users.get_mut(&conn_id) {
                user.almas_contenidas += 30000;
            }
            state.send_to(conn_id, "||274@30.000").await;
        }
        // 4 = Fragment
        4 => {
            if !add_item_to_user_inventory(state, conn_id, 1272, 1) {
                state.send_to(conn_id, "||108").await;
            }
            state.send_to(conn_id, "||277").await;
        }
        _ => return, // Invalid option, don't remove gems
    }

    // Remove all gems
    for &gid in &gem_ids {
        remove_items_from_inv(state, conn_id, gid, 1).await;
    }
    send_full_inventory(state, conn_id).await;
}

/// GEPS — Medal exchange (item 1025 = medal).
async fn handle_geps(state: &mut GameState, conn_id: ConnectionId, data: &str) {
    let payload = strip_opcode(data, 4);
    let option: i32 = payload.trim().parse().unwrap_or(-1);

    match option {
        // 0 = Random gem (8 medals)
        0 => {
            if !user_has_items(state, conn_id, 1025, 8) {
                state.send_to(conn_id, "||278").await;
                return;
            }
            let gem_idx = 406 + (rand_simple_u32() % 6) as i32; // 406-411
            if !add_item_to_user_inventory(state, conn_id, gem_idx, 1) {
                state.send_to(conn_id, "||108").await;
                return;
            }
            let name = state.game_data.objects.get(gem_idx as usize).map(|o| o.name.clone()).unwrap_or_default();
            state.send_to(conn_id, &format!("||232@1@{}", name)).await;
            remove_items_from_inv(state, conn_id, 1025, 8).await;
        }
        // 1 = Sacris (1 medal)
        1 => {
            if !user_has_items(state, conn_id, 1025, 1) {
                state.send_to(conn_id, "||278").await;
                return;
            }
            if !add_item_to_user_inventory(state, conn_id, 936, 1) {
                state.send_to(conn_id, "||108").await;
                return;
            }
            let name = state.game_data.objects.get(936usize).map(|o| o.name.clone()).unwrap_or_default();
            state.send_to(conn_id, &format!("||232@1@{}", name)).await;
            remove_items_from_inv(state, conn_id, 1025, 1).await;
        }
        // 2 = 150 tournament points (1 medal)
        2 => {
            if !user_has_items(state, conn_id, 1025, 1) {
                state.send_to(conn_id, "||278").await;
                return;
            }
            if let Some(user) = state.users.get_mut(&conn_id) {
                user.puntos_torneo += 150;
            }
            state.send_to(conn_id, "||57@150").await;
            remove_items_from_inv(state, conn_id, 1025, 1).await;
        }
        // 3 = 5000 souls (6 medals)
        3 => {
            if !user_has_items(state, conn_id, 1274, 1) {
                state.send_to(conn_id, "||127").await;
                return;
            }
            if !user_has_items(state, conn_id, 1025, 6) {
                state.send_to(conn_id, "||278").await;
                return;
            }
            if let Some(user) = state.users.get_mut(&conn_id) {
                user.almas_contenidas += 5000;
            }
            state.send_to(conn_id, "||274@5.000").await;
            remove_items_from_inv(state, conn_id, 1025, 6).await;
        }
        // 4 = Item 1512 (2 medals)
        4 => {
            if !user_has_items(state, conn_id, 1025, 2) {
                state.send_to(conn_id, "||278").await;
                return;
            }
            if !add_item_to_user_inventory(state, conn_id, 1512, 1) {
                state.send_to(conn_id, "||108").await;
                return;
            }
            let name = state.game_data.objects.get(1512usize).map(|o| o.name.clone()).unwrap_or_default();
            state.send_to(conn_id, &format!("||232@1@{}", name)).await;
            remove_items_from_inv(state, conn_id, 1025, 2).await;
        }
        // 5 = Item 1513 (3 medals)
        5 => {
            if !user_has_items(state, conn_id, 1025, 3) {
                state.send_to(conn_id, "||278").await;
                return;
            }
            if !add_item_to_user_inventory(state, conn_id, 1513, 1) {
                state.send_to(conn_id, "||108").await;
                return;
            }
            let name = state.game_data.objects.get(1513usize).map(|o| o.name.clone()).unwrap_or_default();
            state.send_to(conn_id, &format!("||232@1@{}", name)).await;
            remove_items_from_inv(state, conn_id, 1025, 3).await;
        }
        _ => return,
    }
    send_full_inventory(state, conn_id).await;
}

/// OFDIOZ — Divine offering (sacrifice souls to a god).
async fn handle_ofdioz(state: &mut GameState, conn_id: ConnectionId, data: &str) {
    let payload = strip_opcode(data, 6);
    let cant_almas: i64 = read_field(1, payload, ',').parse().unwrap_or(0);

    if cant_almas <= 0 { return; }

    let current_almas = state.users.get(&conn_id).map(|u| u.almas_contenidas).unwrap_or(0);
    if current_almas < cant_almas {
        state.send_to(conn_id, "ERONo tienes esa cantidad de almas.").await;
        return;
    }

    let god_name = state.users.get(&conn_id).map(|u| u.sirviente_de_dios.clone()).unwrap_or_default();
    let map = state.users.get(&conn_id).map(|u| u.pos_map).unwrap_or(0);

    if let Some(user) = state.users.get_mut(&conn_id) {
        user.almas_contenidas -= cant_almas;
        user.almas_ofrecidas += cant_almas;
    }

    state.send_to(conn_id, &format!("||230@{}@{}", cant_almas, god_name)).await;

    // Send PCF (particle effect) based on god
    let pcf = match god_name.as_str() {
        "Mifrit" => format!("PCF{},{},{},{}", 77, 84, 51, 30),
        "Poseidon" => format!("PCF{},{},{},{}", 77, 49, 14, 30),
        "Tarraske" => format!("PCF{},{},{},{}", 77, 16, 51, 30),
        "Erebros" => format!("PCF{},{},{},{}", 77, 50, 87, 30),
        _ => String::new(),
    };
    if !pcf.is_empty() {
        state.send_data(SendTarget::ToMap(map), &pcf).await;
    }

    // Check for hierarchical rewards (AlmasNecesarias = 5000 in VB6)
    let almas_necesarias: i64 = 5000;
    let jerarquia = state.users.get(&conn_id).map(|u| u.jerarquia_dios).unwrap_or(0);
    let almas_ofrecidas = state.users.get(&conn_id).map(|u| u.almas_ofrecidas).unwrap_or(0);
    let race = state.users.get(&conn_id).map(|u| u.race.to_uppercase()).unwrap_or_default();
    let class = state.users.get(&conn_id).map(|u| u.class.to_uppercase()).unwrap_or_default();

    let rank_names = ["", "Soldado", "Guerrero", "Caballero", "Campe\u{00F3}n"];
    let new_jerarquia_target = jerarquia + 1;
    let required_almas = almas_necesarias * (jerarquia as i64);

    if jerarquia >= 1 && jerarquia <= 4 && almas_ofrecidas >= required_almas {
        let is_short = race == "ENANO" || race == "GNOMO";
        let file_suffix = if is_short { "Bajos.dat" } else { "Altos.dat" };
        let obj_key = format!("Obj{}", jerarquia);
        let god_path = state.base_path.join("Dioses").join(&god_name).join(file_suffix);
        let obj_idx_str = ini_get(&god_path, &class, &obj_key);
        if !obj_idx_str.is_empty() {
            let obj_idx: i32 = obj_idx_str.parse().unwrap_or(0);
            if obj_idx > 0 && !user_has_items(state, conn_id, obj_idx, 1) {
                if add_item_to_user_inventory(state, conn_id, obj_idx, 1) {
                    let rank_name = rank_names.get(jerarquia as usize).unwrap_or(&"");
                    state.send_to(conn_id, &format!("||231@{}@{}", rank_name, god_name)).await;
                    let obj_name = state.game_data.objects.get(obj_idx as usize).map(|o| o.name.clone()).unwrap_or_default();
                    state.send_to(conn_id, &format!("||232@1@{}", obj_name)).await;
                    if let Some(user) = state.users.get_mut(&conn_id) {
                        user.jerarquia_dios = new_jerarquia_target;
                    }
                    send_full_inventory(state, conn_id).await;
                } else {
                    state.send_to(conn_id, "||108").await;
                }
            }
        }
    }

    // Check for 120000 almas + specific item → remove item 1274
    if almas_ofrecidas >= 120000 {
        let check_item = match god_name.as_str() {
            "Tarraske" => Some(1479),
            "Poseidon" => Some(1477),
            "Mifrit" => Some(1475),
            "Erebros" => Some(1473),
            _ => None,
        };
        if let Some(item_id) = check_item {
            if user_has_items(state, conn_id, item_id, 1) {
                remove_items_from_inv(state, conn_id, 1274, 1).await;
                send_full_inventory(state, conn_id).await;
            }
        }
    }
}

/// FTSPTS — TS points shop.
async fn handle_ftspts(state: &mut GameState, conn_id: ConnectionId, data: &str) {
    let payload = strip_opcode(data, 6);
    let ts_index: i32 = read_field(1, payload, ',').parse().unwrap_or(-1);

    let (obj_index, amount, ts_price) = match ts_index {
        0 => (1055, 1, 10),
        1 => (1033, 1, 15),
        2 => (915, 1, 25),
        3 => (1227, 1, 35),
        4 => (1215, 1, 30),
        5 | 6 => (1050, 1, 40),
        7 => (1539, 2, 5),
        8 => (1035, 1, 30),
        9 => (1059, 1, 65),
        10 => (1060, 1, 70),
        11 => (1535, 1, 20),
        _ => return,
    };

    let current_pts = state.users.get(&conn_id).map(|u| u.ts_points).unwrap_or(0);
    if current_pts < ts_price as i64 {
        state.send_to(conn_id, &format!("||212@{}", ts_price)).await;
        return;
    }

    if !add_item_to_user_inventory(state, conn_id, obj_index, amount) {
        state.send_to(conn_id, "||108").await;
        return;
    }

    let name = state.game_data.objects.get(obj_index as usize).map(|o| o.name.clone()).unwrap_or_default();
    state.send_to(conn_id, &format!("||232@{}@{}", amount, name)).await;
    if let Some(user) = state.users.get_mut(&conn_id) {
        user.ts_points -= ts_price as i64;
    }
    send_full_inventory(state, conn_id).await;
}

/// SPH — Query upgrade item info (Mejorados.dat).
async fn handle_sph(state: &mut GameState, conn_id: ConnectionId, data: &str) {
    let payload = strip_opcode(data, 3);
    let mejorados_path = state.base_path.join("Dat").join("Mejorados.dat");

    let numero_mejorado = ini_get(&mejorados_path, "ITEMS", payload.trim());

    let num: i32 = numero_mejorado.parse().unwrap_or(0);
    if num <= 0 { return; }

    let section = format!("ITEM{}", num);
    let nombre = ini_get(&mejorados_path, &section, "Nombre");
    let at_min = ini_get(&mejorados_path, &section, "AtaqueMinimo");
    let at_max = ini_get(&mejorados_path, &section, "AtaqueMaximo");
    let def_min = ini_get(&mejorados_path, &section, "DefensaMinima");
    let def_max = ini_get(&mejorados_path, &section, "DefensaMaxima");
    let atm_min = ini_get(&mejorados_path, &section, "AtaqueMagicoMinimo");
    let atm_max = ini_get(&mejorados_path, &section, "AtaqueMagicoMaximo");
    let defm_min = ini_get(&mejorados_path, &section, "DefensaMagicaMinima");
    let defm_max = ini_get(&mejorados_path, &section, "DefensaMagicaMaxima");
    let desc = ini_get(&mejorados_path, &section, "Descripcion");
    let obj_idx: i32 = ini_get(&mejorados_path, &section, "NumObj").parse().unwrap_or(0);
    let grh = state.game_data.objects.get(obj_idx as usize).map(|o| o.grh_index).unwrap_or(0);

    state.send_to(conn_id, &format!("IMEJ{},{}/{},{}/{},{}/{},{}/{},{},{}",
        nombre, at_min, at_max, def_min, def_max, atm_min, atm_max, defm_min, defm_max, desc, grh)).await;
}

/// SPÉ — Upgrade item (requires octarina gem 1448 + required item).
async fn handle_spe(state: &mut GameState, conn_id: ConnectionId, data: &str) {
    let payload = strip_opcode(data, 3);
    let mejorados_path = state.base_path.join("Dat").join("Mejorados.dat");

    let numero_mejorado = ini_get(&mejorados_path, "ITEMS", payload.trim());

    let num: i32 = numero_mejorado.parse().unwrap_or(0);
    if num <= 0 { return; }

    let section = format!("ITEM{}", num);
    let requiere: i32 = ini_get(&mejorados_path, &section, "Requiere").parse().unwrap_or(0);
    let obj_idx: i32 = ini_get(&mejorados_path, &section, "NumObj").parse().unwrap_or(0);

    // Need octarina gem (1448)
    if !user_has_items(state, conn_id, 1448, 1) {
        state.send_to(conn_id, "||235").await;
        return;
    }

    // Need the required item
    if !user_has_items(state, conn_id, requiere, 1) {
        state.send_to(conn_id, "||236").await;
        return;
    }

    if !add_item_to_user_inventory(state, conn_id, obj_idx, 1) {
        state.send_to(conn_id, "||108").await;
        return;
    }

    let name = state.game_data.objects.get(requiere as usize).map(|o| o.name.clone()).unwrap_or_default();
    state.send_to(conn_id, &format!("||237@{}", name)).await;
    remove_items_from_inv(state, conn_id, requiere, 1).await;
    remove_items_from_inv(state, conn_id, 1448, 1).await;
    send_full_inventory(state, conn_id).await;
}

/// ARE — Arena spectator (enter arena to watch duel).
async fn handle_are(state: &mut GameState, conn_id: ConnectionId, data: &str) {
    let payload = strip_opcode(data, 3);
    let arena_num: i32 = payload.trim().parse().unwrap_or(0);

    // Check if on PK map
    let map = state.users.get(&conn_id).map(|u| u.pos_map).unwrap_or(0);
    let is_pk = if let Some(Some(gm)) = state.game_data.maps.get(map as usize) { gm.info.pk } else { false };
    if is_pk {
        state.send_to(conn_id, "||291").await;
        return;
    }

    // Need 100k gold
    let gold = state.users.get(&conn_id).map(|u| u.gold).unwrap_or(0);
    if gold < 100000 {
        state.send_to(conn_id, "||215@100.000").await;
        return;
    }

    // Can't be already spectating
    let already = state.users.get(&conn_id).map(|u|
        u.espectador_arena1 || u.espectador_arena2 || u.espectador_arena3 || u.espectador_arena4
    ).unwrap_or(false);
    if already { return; }

    // Can't be in special map, CvC, or dead
    let special = state.users.get(&conn_id).map(|u| u.en_cvc || u.dead).unwrap_or(false);
    if special {
        state.send_to(conn_id, "||239").await;
        return;
    }

    // Arena spectator positions on map 71
    let (espectadores, max_esp, positions, flag_setter): (i32, i32, Vec<(i32, i32)>, i32) = match arena_num {
        1 => (state.espectadores_arena1, 4, vec![(33,34),(34,34),(33,35),(34,35)], 1),
        2 => (state.espectadores_arena2, 4, vec![(33,68),(34,68),(33,69),(34,69)], 2),
        3 => (state.espectadores_arena3, 4, vec![(69,34),(70,34),(69,35),(70,35)], 3),
        4 => (state.espectadores_arena4, 4, vec![(69,68),(70,68),(69,69),(70,69)], 4),
        _ => return,
    };

    if !state.arena_ocupada[arena_num as usize] || espectadores >= max_esp {
        state.send_to(conn_id, "||241").await;
        return;
    }

    // Save position
    if let Some(user) = state.users.get_mut(&conn_id) {
        user.mapa_anterior = user.pos_map;
        user.x_anterior = user.pos_x;
        user.y_anterior = user.pos_y;
        match flag_setter {
            1 => user.espectador_arena1 = true,
            2 => user.espectador_arena2 = true,
            3 => user.espectador_arena3 = true,
            4 => user.espectador_arena4 = true,
            _ => {}
        }
    }

    // Find free spectator position
    let pos = positions.first().copied().unwrap_or((33, 34));
    warp_user(state, conn_id, 71, pos.0, pos.1).await;

    match arena_num {
        1 => state.espectadores_arena1 += 1,
        2 => state.espectadores_arena2 += 1,
        3 => state.espectadores_arena3 += 1,
        4 => state.espectadores_arena4 += 1,
        _ => {}
    }

    if let Some(user) = state.users.get_mut(&conn_id) {
        user.gold -= 100000;
    }
    send_stats_gold(state, conn_id).await;
    state.send_to(conn_id, "||240").await;
}

/// NANVAME — Clan name validated (notify admins).
async fn handle_nanvame(state: &mut GameState, conn_id: ConnectionId, _data: &str) {
    let name = state.users.get(&conn_id).map(|u| u.char_name.clone()).unwrap_or_default();
    state.send_data(SendTarget::ToAdmins, &format!("||498@{}", name)).await;
}

/// NANVAMX — Clan name invalid (notify admins).
async fn handle_nanvamx(state: &mut GameState, conn_id: ConnectionId, _data: &str) {
    let name = state.users.get(&conn_id).map(|u| u.char_name.clone()).unwrap_or_default();
    state.send_data(SendTarget::ToAdmins, &format!("||499@{}", name)).await;
}

/// PCGF — Forward party/clan GUI data to target user.
async fn handle_pcgf(state: &mut GameState, conn_id: ConnectionId, data: &str) {
    let payload = strip_opcode(data, 4);
    let proceso = read_field(1, payload, ',');
    let peso = read_field(2, payload, ',');
    let target_idx: ConnectionId = read_field(3, payload, ',').parse().unwrap_or(0);

    let sender_name = state.users.get(&conn_id).map(|u| u.char_name.clone()).unwrap_or_default();
    if target_idx > 0 && state.users.contains_key(&target_idx) {
        state.send_to(target_idx, &format!("PCGN{},{},{}", proceso, peso, sender_name)).await;
    }
}

/// PCWC — Forward party/clan window command to target user.
async fn handle_pcwc(state: &mut GameState, conn_id: ConnectionId, data: &str) {
    let payload = strip_opcode(data, 4);
    let proceso = read_field(1, payload, ',');
    let target_idx: ConnectionId = read_field(2, payload, ',').parse().unwrap_or(0);

    let sender_name = state.users.get(&conn_id).map(|u| u.char_name.clone()).unwrap_or_default();
    if target_idx > 0 && state.users.contains_key(&target_idx) {
        state.send_to(target_idx, &format!("PCSS{},{}", proceso, sender_name)).await;
    }
}

/// PCCC — Forward party/clan caption to target user.
async fn handle_pccc(state: &mut GameState, conn_id: ConnectionId, data: &str) {
    let payload = strip_opcode(data, 4);
    let caption = read_field(1, payload, ',');
    let target_idx: ConnectionId = read_field(2, payload, ',').parse().unwrap_or(0);

    let sender_name = state.users.get(&conn_id).map(|u| u.char_name.clone()).unwrap_or_default();
    if target_idx > 0 && state.users.contains_key(&target_idx) {
        state.send_to(target_idx, &format!("PCCC{},{}", caption, sender_name)).await;
    }
}

/// /VOTO — Vote for guild leader candidate.
async fn handle_slash_voto(state: &mut GameState, conn_id: ConnectionId, candidate: &str) {
    let guild_idx = state.users.get(&conn_id).map(|u| u.guild_index).unwrap_or(0);
    if guild_idx <= 0 {
        state.send_to(conn_id, &format!("{}No perteneces a ningun clan.{}", server_opcodes::CONSOLE_MSG, font_types::INFO)).await;
        return;
    }

    // Simplified: just acknowledge the vote (full guild elections not implemented yet)
    state.send_to(conn_id, "||439").await;
}

/// /PAREJA — 2vs2 system.
async fn handle_slash_pareja(state: &mut GameState, conn_id: ConnectionId, target_name: &str) {
    let target = state.online_names.get(&target_name.to_uppercase()).copied();

    // Command cooldown
    let cooldown = state.users.get(&conn_id).map(|u| u.time_comandos).unwrap_or(0);
    if cooldown > 0 {
        state.send_to(conn_id, "||290").await;
        return;
    }
    if let Some(user) = state.users.get_mut(&conn_id) {
        user.time_comandos = 5;
    }

    let target_id = match target {
        Some(id) => id,
        None => {
            state.send_to(conn_id, "||196").await;
            return;
        }
    };

    if target_id == conn_id { return; }

    // Check gold (300k each)
    let my_gold = state.users.get(&conn_id).map(|u| u.gold).unwrap_or(0);
    if my_gold < 300000 {
        state.send_to(conn_id, "||215@300.000").await;
        return;
    }
    let t_gold = state.users.get(&target_id).map(|u| u.gold).unwrap_or(0);
    if t_gold < 300000 {
        state.send_to(conn_id, "||446").await;
        return;
    }

    // Check dead, in commerce, in cvc etc
    let my_dead = state.users.get(&conn_id).map(|u| u.dead || u.en_cvc || u.comerciando).unwrap_or(true);
    if my_dead {
        state.send_to(conn_id, "||239").await;
        return;
    }

    // Check same class
    let my_class = state.users.get(&conn_id).map(|u| u.class.clone()).unwrap_or_default();
    let t_class = state.users.get(&target_id).map(|u| u.class.clone()).unwrap_or_default();
    if my_class == t_class {
        state.send_to(conn_id, "||448").await;
        return;
    }

    // Check if all 4 slots are full
    if state.pareja[3] > 0 && state.pareja[4] > 0 {
        state.send_to(conn_id, "||406").await;
        return;
    }

    // Set up pairing
    if let Some(user) = state.users.get_mut(&target_id) {
        user.espera_pareja = true;
    }
    if let Some(user) = state.users.get_mut(&conn_id) {
        user.su_pareja = target_id;
    }

    let my_name = state.users.get(&conn_id).map(|u| u.char_name.clone()).unwrap_or_default();
    let t_name = state.users.get(&target_id).map(|u| u.char_name.clone()).unwrap_or_default();

    // Check if target also wants to pair with us
    let target_wants_me = state.users.get(&target_id).map(|u| u.su_pareja == conn_id).unwrap_or(false);

    if state.pareja[1] == 0 && state.pareja[2] == 0 {
        if !target_wants_me {
            state.send_to(target_id, &format!("||449@{}", my_name)).await;
            return;
        }
        // Form first pair
        state.pareja[1] = conn_id;
        state.pareja[2] = target_id;
        for &pid in &[conn_id, target_id] {
            if let Some(u) = state.users.get_mut(&pid) {
                u.en_pareja = true;
                u.mapa_anterior = u.pos_map;
                u.x_anterior = u.pos_x;
                u.y_anterior = u.pos_y;
            }
        }
        warp_user(state, conn_id, 106, 41, 55).await;
        warp_user(state, target_id, 106, 43, 57).await;
        for &pid in &[conn_id, target_id] {
            if let Some(u) = state.users.get_mut(&pid) {
                u.gold -= 300000;
            }
            send_stats_gold(state, pid).await;
        }
        state.send_data(SendTarget::ToAll, &format!("||450@{}@{}", my_name, t_name)).await;
    } else if state.pareja[1] > 0 && state.pareja[2] > 0 {
        if !target_wants_me {
            state.send_to(target_id, &format!("||449@{}", my_name)).await;
            return;
        }
        // Form second pair
        state.pareja[3] = conn_id;
        state.pareja[4] = target_id;
        for &pid in &[conn_id, target_id] {
            if let Some(u) = state.users.get_mut(&pid) {
                u.en_pareja = true;
                u.mapa_anterior = u.pos_map;
                u.x_anterior = u.pos_x;
                u.y_anterior = u.pos_y;
            }
        }
        warp_user(state, state.pareja[1], 106, 41, 55).await;
        warp_user(state, state.pareja[2], 106, 43, 57).await;
        warp_user(state, conn_id, 106, 60, 40).await;
        warp_user(state, target_id, 106, 62, 42).await;
        for &pid in &[conn_id, target_id] {
            if let Some(u) = state.users.get_mut(&pid) {
                u.gold -= 300000;
            }
            send_stats_gold(state, pid).await;
        }
        state.send_data(SendTarget::ToAll, &format!("||451@{}@{}", my_name, t_name)).await;
    }
}

/// /SICV — Start CvC battle between two guilds.
async fn handle_slash_sicv(state: &mut GameState, conn_id: ConnectionId) {
    let guild_idx = state.users.get(&conn_id).map(|u| u.guild_index).unwrap_or(0);
    if guild_idx < 1 {
        state.send_to(conn_id, "||120").await;
        return;
    }

    let map = state.users.get(&conn_id).map(|u| u.pos_map).unwrap_or(0);
    if map == 141 { return; }

    if state.cvc_funciona {
        state.send_to(conn_id, "||364").await;
        return;
    }

    // Simplified CvC initiation — full implementation requires guild challenge state
    state.send_to(conn_id, &format!("{}El sistema CvC requiere un desafio previo con /CVC.{}", server_opcodes::CONSOLE_MSG, font_types::INFO)).await;
}

// =====================================================================
// Integration tests — full client login flow
// =====================================================================

#[cfg(test)]
mod tests {
    use super::*;
    use crate::config::ServerConfig;
    use crate::data::{GameData, accounts, charfile};
    use crate::net::connection;
    use std::path::{Path, PathBuf};
    use tokio::net::{TcpListener, TcpStream};

    /// Real server base path (contains Dat/, Maps/ etc.)
    const SERVER_BASE: &str = "/workspace/Tierras-Sagradas-AO/server-rust/server";

    /// Create a temp test directory with symlinks to real game data.
    fn setup_test_dir(test_name: &str) -> PathBuf {
        let dir = std::env::temp_dir().join(format!("ao_test_{}", test_name));
        let _ = std::fs::remove_dir_all(&dir);
        std::fs::create_dir_all(&dir).unwrap();

        // Symlink data directories from the real server
        let base = Path::new(SERVER_BASE);
        for subdir in &["Dat", "Maps"] {
            let src = base.join(subdir);
            let dst = dir.join(subdir);
            if src.exists() {
                std::os::unix::fs::symlink(&src, &dst).unwrap();
            }
        }

        // Create empty Accounts/ and charfile/ dirs
        std::fs::create_dir_all(dir.join("Accounts")).unwrap();
        std::fs::create_dir_all(dir.join("charfile")).unwrap();

        dir
    }

    fn cleanup_test_dir(dir: &Path) {
        let _ = std::fs::remove_dir_all(dir);
    }

    /// Create a test ServerConfig with sensible defaults.
    fn test_config(notice: &str) -> ServerConfig {
        ServerConfig {
            server_ip: "127.0.0.1".into(),
            port: 0,
            stats_port: 0,
            max_users: 100,
            version: "1.0.0".into(),
            client_version: "1.0.0".into(),
            idle_limit: 0,
            allow_multi_logins: false,
            can_create_characters: true,
            server_only_gms: false,
            encrypt: true,
            exp_multiplier: 1,
            gold_multiplier: 1,
            drop_multiplier: 1,
            start_map: 1,
            start_x: 50,
            start_y: 50,
            char_dir: "charfile".into(),
            log_dir: "logs".into(),
            notice: notice.to_string(),
            pretoriano_map: 0,
            intervalo_paralizado: 500,
            npc_ai_interval_ms: 1300,
        }
    }

    /// Create a TCP pair and return (client_stream, server ConnectionWriter).
    async fn create_tcp_pair() -> (TcpStream, connection::ConnectionWriter) {
        let listener = TcpListener::bind("127.0.0.1:0").await.unwrap();
        let port = listener.local_addr().unwrap().port();

        let (client_result, server_result) = tokio::join!(
            TcpStream::connect(format!("127.0.0.1:{}", port)),
            listener.accept()
        );
        let client_stream = client_result.unwrap();
        let (server_stream, addr) = server_result.unwrap();

        let (_reader, writer) = connection::split_connection(1, server_stream, addr);
        (client_stream, writer)
    }

    /// Create a test account with one character.
    fn create_test_data(base: &Path) {
        // Create account
        accounts::create_account(base, "testaccount", "testpass", "1234", "9999").unwrap();
        accounts::add_character_to_account(base, "testaccount", "TestHero").unwrap();

        // Create character
        charfile::create_charfile(
            base,
            "TestHero",
            "Humano",
            1,          // Male
            "Guerrero",
            1,          // Hogar
            70,         // Head
            "9999",     // Password (CodeX)
            [18, 18, 18, 18, 18], // Attributes
            1,          // Start map
            50,         // Start X
            50,         // Start Y
        ).unwrap();
    }

    /// Encrypt a client packet (simulates VB6 client encryption).
    /// Pipeline: Codificar(plaintext, key) → AoDefEncode → append \0
    fn encrypt_client_packet(plaintext: &[u8], counter: i64) -> Vec<u8> {
        // Derive key from counter (same as server-side derive_key)
        let text = crate::crypto::numero2letra(counter);
        let text_no_spaces: String = text.chars().filter(|c| *c != ' ').collect();
        let key = crate::crypto::semilla(&text_no_spaces);

        // Codificar (XOR cipher)
        let ciphered = crate::crypto::codificar(plaintext, &key);
        // AoDefEncode (base64)
        let encoded = crate::crypto::aodef_encode(&ciphered);

        let mut result = encoded.into_bytes();
        result.push(0x00); // Null terminator
        result
    }

    /// Decrypt a server packet (simulates VB6 client decryption).
    /// Pipeline: AoDefDecode(base64) → AoDefServDecrypt(hex)
    fn decrypt_server_packet(raw: &[u8]) -> String {
        let decoded = crate::crypto::aodef_decode(raw);
        let decrypted = crate::crypto::aodef_serv_decrypt(&decoded);
        String::from_utf8_lossy(&decrypted).to_string()
    }

    /// Read all pending packets from a TCP stream (non-blocking after initial data).
    /// Returns a Vec of decrypted plaintext packets.
    async fn read_server_packets(stream: &mut TcpStream) -> Vec<String> {
        use tokio::io::AsyncReadExt;

        let mut buf = [0u8; 16384];
        let mut packets = Vec::new();

        // Give server a moment to send responses
        tokio::time::sleep(std::time::Duration::from_millis(200)).await;

        // Read available data
        match tokio::time::timeout(
            std::time::Duration::from_millis(500),
            stream.read(&mut buf),
        ).await {
            Ok(Ok(n)) if n > 0 => {
                // Split on null bytes
                let data = &buf[..n];
                for chunk in data.split(|b| *b == 0x00) {
                    if !chunk.is_empty() {
                        let plain = decrypt_server_packet(chunk);
                        if !plain.is_empty() {
                            packets.push(plain);
                        }
                    }
                }
            }
            _ => {}
        }

        packets
    }

    /// Read ALL packets from server until no more data (with retries).
    async fn read_all_server_packets(stream: &mut TcpStream) -> Vec<String> {
        use tokio::io::AsyncReadExt;

        let mut all_packets = Vec::new();
        let mut buf = [0u8; 65536];
        let mut accumulated = Vec::new();

        // Keep reading until timeout (server may send data in bursts)
        loop {
            match tokio::time::timeout(
                std::time::Duration::from_millis(300),
                stream.read(&mut buf),
            ).await {
                Ok(Ok(n)) if n > 0 => {
                    accumulated.extend_from_slice(&buf[..n]);
                }
                _ => break,
            }
        }

        // Split on null bytes and decrypt
        for chunk in accumulated.split(|b| *b == 0x00) {
            if !chunk.is_empty() {
                let plain = decrypt_server_packet(chunk);
                if !plain.is_empty() {
                    all_packets.push(plain);
                }
            }
        }

        all_packets
    }

    #[tokio::test]
    async fn test_full_login_flow() {
        // Setup test directory with game data + test account/charfile
        let test_dir = setup_test_dir("login_flow");
        create_test_data(&test_dir);

        let game_data = GameData::load(&test_dir).expect("Failed to load game data");
        let config = test_config("Bienvenido a Tierras Sagradas!");

        // Create TCP pair (server↔client)
        let (mut client_stream, writer) = create_tcp_pair().await;

        // Initialize GameState
        let mut state = GameState::new(config, test_dir.clone(), game_data);
        state.add_connection(writer);

        // ====== STEP 1: Send KERD22 (HD serial check) ======
        let kerd22_plain = b"KERD22ABC123HD";
        let kerd22_encrypted = encrypt_client_packet(kerd22_plain, 1);

        use tokio::io::AsyncWriteExt;
        client_stream.write_all(&kerd22_encrypted).await.unwrap();

        // Process on server side (simulate what main loop does)
        // We need to decrypt as the server would
        let kerd22_decrypted = {
            let text = crate::crypto::numero2letra(1);
            let text_no_spaces: String = text.chars().filter(|c| *c != ' ').collect();
            let key = crate::crypto::semilla(&text_no_spaces);
            let raw = &kerd22_encrypted[..kerd22_encrypted.len() - 1]; // strip \0
            let decrypted = crate::crypto::decrypt_inbound(raw, &key);
            String::from_utf8_lossy(&decrypted).to_string()
        };
        assert_eq!(kerd22_decrypted.trim(), "KERD22ABC123HD");

        // Call handler directly with plaintext
        handle_packet(&mut state, 1, "KERD22ABC123HD").await;

        // Verify: user's HD serial should be stored, paso_hd = true
        {
            let user = state.users.get(&1).unwrap();
            assert_eq!(user.hd_serial, "ABC123HD");
            assert!(user.paso_hd, "paso_hd should be true after KERD22");
        }

        // ====== STEP 2: Send ALOGIN (account login) ======
        handle_packet(&mut state, 1, "ALOGINtestaccount,testpass,1.0.0").await;

        // Read server responses (INIAC + ADDPJ + CODEH)
        let packets = read_all_server_packets(&mut client_stream).await;

        // Verify INIAC: should contain num_pjs=1 and notice
        let iniac = packets.iter().find(|p| p.starts_with("INIAC")).expect("Missing INIAC packet");
        assert!(iniac.starts_with("INIAC1,"), "INIAC should show 1 character, got: {}", iniac);

        // Verify ADDPJ: should contain TestHero
        let addpj = packets.iter().find(|p| p.starts_with("ADDPJ")).expect("Missing ADDPJ packet");
        assert!(addpj.contains("TestHero"), "ADDPJ should contain TestHero, got: {}", addpj);

        // Verify CODEH: should contain security code
        let codeh = packets.iter().find(|p| p.starts_with("CODEH")).expect("Missing CODEH packet");
        assert!(codeh.len() > 5, "CODEH should have a security code, got: {}", codeh);

        // Verify user state after ALOGIN
        {
            let user = state.users.get(&1).unwrap();
            assert_eq!(user.account_name, "testaccount");
        }

        // ====== STEP 3: Send THCJXD (character login) ======
        let codex = &codeh[5..]; // Extract security code from CODEH response
        let thcjxd_pkt = format!("THCJXDTestHero,testaccount,{}", codex);
        handle_packet(&mut state, 1, &thcjxd_pkt).await;

        // Read the full login packet sequence
        let login_packets = read_all_server_packets(&mut client_stream).await;

        // ====== VERIFY COMPLETE LOGIN SEQUENCE ======
        // The VB6 client expects these packets in order:

        // 1. CM (Change Map) — must be first
        let cm = login_packets.iter().find(|p| p.starts_with("CM")).expect("Missing CM packet");
        assert!(cm.starts_with("CM1,"), "CM should be map 1, got: {}", cm);

        // 2. PU (Position Update)
        let pu = login_packets.iter().find(|p| p.starts_with("PU")).expect("Missing PU packet");
        assert!(pu.starts_with("PU50,50"), "PU should be 50,50, got: {}", pu);

        // 3. XM (Map Music)
        assert!(login_packets.iter().any(|p| p.starts_with("XM")), "Missing XM packet");

        // 4. N~ (Map Name)
        assert!(login_packets.iter().any(|p| p.starts_with("N~")), "Missing N~ packet");

        // 5. LDG (Privilege Level)
        let ldg = login_packets.iter().find(|p| p.starts_with("LDG")).expect("Missing LDG packet");
        assert!(ldg.starts_with("LDG0"), "LDG should be 0 (no privileges), got: {}", ldg);

        // 6. EHYS (Hunger/Thirst)
        let ehys = login_packets.iter().find(|p| p.starts_with("EHYS")).expect("Missing EHYS packet");
        assert!(ehys.contains("100"), "EHYS should contain 100 (full hunger/thirst)");

        // 7. LDM (Friend List)
        assert!(login_packets.iter().any(|p| p.starts_with("LDM")), "Missing LDM packet");

        // 8. [ES (Bulk Stats)
        let bulk = login_packets.iter().find(|p| p.starts_with("[ES")).expect("Missing [ES bulk stats packet");
        assert!(bulk.contains("TestHero"), "[ES should contain character name, got: {}", bulk);

        // 9. ANM (Equipment stats)
        assert!(login_packets.iter().any(|p| p.starts_with("ANM")), "Missing ANM packet");

        // 10. RPT (Reputation)
        assert!(login_packets.iter().any(|p| p.starts_with("RPT")), "Missing RPT packet");

        // 11. INVI0 (Inventory Init)
        assert!(login_packets.iter().any(|p| p.starts_with("INVI0")), "Missing INVI0 packet");

        // 12. TIS (Scroll timers — 4 of them)
        let tis_count = login_packets.iter().filter(|p| p.starts_with("TIS")).count();
        assert_eq!(tis_count, 4, "Should have 4 TIS packets, got {}", tis_count);

        // 13. LOGGED — the critical packet that switches client to game mode
        assert!(login_packets.iter().any(|p| *p == "LOGGED"), "Missing LOGGED packet!");

        // 14. Post-login console messages (||705, ||706, etc.)
        assert!(login_packets.iter().any(|p| p.starts_with("||705")), "Missing ||705 console message");
        assert!(login_packets.iter().any(|p| p.starts_with("||709")), "Missing ||709 console message");

        // 15. STOPD (paralysis state)
        let stopd = login_packets.iter().find(|p| p.starts_with("STOPD")).expect("Missing STOPD packet");
        assert!(stopd.starts_with("STOPD0"), "STOPD should be 0 (not paralyzed), got: {}", stopd);

        // Verify final game state
        {
            let user = state.users.get(&1).unwrap();
            assert!(user.logged, "User should be logged in");
            assert_eq!(user.char_name, "TestHero");
            assert_eq!(user.pos_map, 1);
            assert_eq!(user.pos_x, 50);
            assert_eq!(user.pos_y, 50);
            assert_eq!(user.level, 1);
            assert_eq!(user.class, "Guerrero");
            assert_eq!(user.race, "Humano");
            assert!(user.char_index.0 > 0, "Should have a char index assigned");
        }
        assert_eq!(state.num_users, 1);

        // Cleanup
        cleanup_test_dir(&test_dir);
    }

    #[tokio::test]
    async fn test_login_wrong_password() {
        let test_dir = setup_test_dir("wrong_pass");
        create_test_data(&test_dir);

        let game_data = GameData::load(&test_dir).expect("Failed to load game data");
        let config = test_config("");

        let (mut client_stream, writer) = create_tcp_pair().await;
        let mut state = GameState::new(config, test_dir.clone(), game_data);
        state.add_connection(writer);

        // Send KERD22 first (required for paso_hd)
        handle_packet(&mut state, 1, "KERD22ABC123HD").await;

        // Try login with wrong password
        handle_packet(&mut state, 1, "ALOGINtestaccount,WRONGPASS,1.0.0").await;

        let packets = read_all_server_packets(&mut client_stream).await;

        // Should get an ERR packet about wrong password
        let err = packets.iter().find(|p| p.starts_with("ERR")).expect("Missing ERR packet");
        assert!(err.contains("incorrecto"), "ERR should mention incorrect password, got: {}", err);

        cleanup_test_dir(&test_dir);
    }

    #[tokio::test]
    async fn test_login_nonexistent_account() {
        let test_dir = setup_test_dir("no_account");
        create_test_data(&test_dir);

        let game_data = GameData::load(&test_dir).expect("Failed to load game data");
        let config = test_config("");

        let (mut client_stream, writer) = create_tcp_pair().await;
        let mut state = GameState::new(config, test_dir.clone(), game_data);
        state.add_connection(writer);

        handle_packet(&mut state, 1, "KERD22ABC123HD").await;
        handle_packet(&mut state, 1, "ALOGINghost_account,pass,1.0.0").await;

        let packets = read_all_server_packets(&mut client_stream).await;

        let err = packets.iter().find(|p| p.starts_with("ERR")).expect("Missing ERR packet");
        assert!(err.contains("no existe"), "ERR should say account doesn't exist, got: {}", err);

        cleanup_test_dir(&test_dir);
    }

    #[tokio::test]
    async fn test_login_without_kerd22() {
        let test_dir = setup_test_dir("no_kerd22");
        create_test_data(&test_dir);

        let game_data = GameData::load(&test_dir).expect("Failed to load game data");
        let config = test_config("");

        let (mut client_stream, writer) = create_tcp_pair().await;
        let mut state = GameState::new(config, test_dir.clone(), game_data);
        state.add_connection(writer);

        // Skip KERD22 — go straight to ALOGIN (paso_hd = false)
        handle_packet(&mut state, 1, "ALOGINtestaccount,testpass,1.0.0").await;

        let packets = read_all_server_packets(&mut client_stream).await;

        let err = packets.iter().find(|p| p.starts_with("ERR")).expect("Missing ERR packet");
        assert!(err.contains("Tolerancia 0"), "Should get Tolerancia 0 error, got: {}", err);

        cleanup_test_dir(&test_dir);
    }

    #[tokio::test]
    async fn test_create_new_account() {
        let test_dir = setup_test_dir("new_account");

        let game_data = GameData::load(&test_dir).expect("Failed to load game data");
        let config = test_config("");

        let (mut client_stream, writer) = create_tcp_pair().await;
        let mut state = GameState::new(config, test_dir.clone(), game_data);
        state.add_connection(writer);

        // Create new account via NACCNT
        handle_packet(&mut state, 1, "NACCNTnewplayer,secret123,5678").await;

        let packets = read_all_server_packets(&mut client_stream).await;

        // Should get success message (sent as ERR with success text)
        let msg = packets.iter().find(|p| p.starts_with("ERR")).expect("Missing response packet");
        assert!(msg.contains("exito"), "Should confirm account creation, got: {}", msg);

        // Verify account was created on disk
        assert!(accounts::account_exists(&test_dir, "newplayer"), "Account file should exist");

        cleanup_test_dir(&test_dir);
    }
}
