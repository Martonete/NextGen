mod common;
mod gm_commands;
mod ticks;
mod events;
mod commerce;
mod skills;
mod guilds_handler;
mod spells;
mod quests_party;
mod combat;
mod npcs;
mod social;
mod player_commands;
mod misc_handlers;
mod inventory;
use common::*;
use gm_commands::*;
use ticks::*;
use events::*;
use commerce::*;
use skills::*;
use guilds_handler::*;
use spells::*;
use quests_party::*;
use combat::*;
use npcs::*;
use social::*;
use player_commands::*;
use misc_handlers::*;
use inventory::*;
// Re-export quest/party functions called from other modules
pub use quests_party::{quest_check_npc_kill, quest_check_player_kill, party_share_exp};
// Re-export friend broadcast called from main.rs disconnect handler
pub use social::broadcast_friend_disconnect;
// Re-export tick functions called from main.rs
pub use ticks::{
    tick_npc_ai, tick_npc_respawn, tick_player_passive,
    tick_intervals, tick_clean_world,
};
// Re-export event functions called from main.rs and other modules
pub use events::{
    tick_nobleza, tick_eventos, tick_siege, tick_ancalagon, tick_guerra,
    cvc_player_disconnect, resolve_duel_death, resolve_desafio_death,
    evento_player_death, nobleza_etapa_uno, aram_check_tower_death,
    torneo_auto_join, torneo_auto_death, pretoriano_check_death,
    ip_security_accept,
};

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
use crate::db::{accounts, charfile, guilds};
use crate::db::password;
use crate::data::objects::ObjType;
use crate::data::maps::Trigger;
use super::types::{GameState, UserState, SendTarget, InventorySlot, EquipSlots, PartyState, CleanWorldEntry, privilege_level, MAX_INVENTORY_SLOTS, MAX_SPELL_SLOTS, MAX_PARTY_MEMBERS, MAX_PARTIES};
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
        info!("[DISPATCH] #{} FINCOM", conn_id);
        handle_commerce_close(state, conn_id).await;
    } else if data.starts_with(client_opcodes::COMMERCE_BUY) {
        info!("[DISPATCH] #{} COMP raw='{}'", conn_id, data);
        handle_commerce_buy(state, conn_id, data).await;
    } else if data.starts_with(client_opcodes::COMMERCE_SELL) {
        info!("[DISPATCH] #{} VEND raw='{}'", conn_id, data);
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
    } else if data.starts_with(client_opcodes::GUILD_BANK_PERMS_QUERY) {
        handle_vlkg(state, conn_id, data).await;
    } else if data.starts_with(client_opcodes::GUILD_BANK_PERMS_SET) {
        handle_bovc(state, conn_id, data).await;
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
    } else if data.starts_with(client_opcodes::USE_SKILL) {
        handle_uk(state, conn_id, data).await;
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

    if !accounts::account_exists(&state.pool, &account_name).await {
        state.send_to(conn_id, &format!("{}La cuenta no existe.", server_opcodes::ERROR)).await;
        close_connection(state, conn_id).await;
        return;
    }

    let account = match accounts::load_account(&state.pool, &account_name).await {
        Ok(acc) => acc,
        Err(e) => {
            warn!("[AUTH] Failed to load account '{}': {}", account_name, e);
            state.send_to(conn_id, &format!("{}Error al leer la cuenta.", server_opcodes::ERROR)).await;
            close_connection(state, conn_id).await;
            return;
        }
    };

    if !password::verify_password(&password, &account.password_hash) {
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
        user.account_id = account.id;
    }

    let iniac = format!("INIAC{},{}", account.num_pjs, state.notice);
    state.send_to(conn_id, &iniac).await;

    for i in 0..account.num_pjs {
        let pj_name = &account.characters[i];
        if pj_name.is_empty() {
            continue;
        }
        match charfile::load_char_preview(&state.pool, pj_name).await {
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

    let password_hash = match password::hash_password(&password) {
        Ok(h) => h,
        Err(e) => {
            warn!("[AUTH] Failed to hash password: {}", e);
            state.send_to(conn_id, &format!("{}Error interno del servidor.", server_opcodes::ERROR)).await;
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

    if !charfile::character_exists(&state.pool, &char_name).await {
        state.send_to(conn_id, &format!("{}El personaje no existe.", server_opcodes::ERROR)).await;
        close_connection(state, conn_id).await;
        return;
    }

    if is_char_banned(&state.pool, &char_name).await {
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

    if !charfile::character_exists(&state.pool, &char_name).await {
        state.send_to(conn_id, &format!("{}El personaje no existe.", server_opcodes::ERROR)).await;
        close_connection(state, conn_id).await;
        return;
    }

    if is_char_banned(&state.pool, &char_name).await {
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
    let char_data = match charfile::load_charfile(&state.pool, char_name).await {
        Ok(data) => data,
        Err(e) => {
            warn!("[AUTH] Failed to load character '{}': {}", char_name, e);
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
            // Send BP to area to remove the old character
            if let Some(old_user) = state.users.get(&old_id) {
                let bp_pkt = format!("BP{}", old_user.char_index.0);
                let (map, x, y) = (old_user.pos_map, old_user.pos_x, old_user.pos_y);
                state.send_data(
                    SendTarget::ToAreaButIndex { conn_id: old_id, map, x, y },
                    &bp_pkt,
                ).await;
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
        user.navigating = char_data.navigating;
        user.barco_slot = char_data.barco_slot as usize;

        // Safety: if charfile has invalid body (0=invisible, 8=ghost) but dead=false,
        // restore naked body. body=0 can happen if a GM disconnects while invisible.
        // SKIP this check if navigating — boat state has head=0, body=boat ropaje (valid).
        if !user.dead && !char_data.navigating && (user.body <= 0 || user.body == DEAD_BODY_NEUTRAL || user.head <= 0 || user.head == DEAD_HEAD_NEUTRAL) {
            let gender_str = char_data.gender.to_string();
            user.body = naked_body(&char_data.race, &gender_str);
            // Head 500 is ghost head, head 0 is invisible — use a default for race/gender
            if user.head <= 0 || user.head == DEAD_HEAD_NEUTRAL {
                user.head = default_head_for_race(&char_data.race, char_data.gender);
            }
        }

        // Animation IDs: resolve from equipped items (VB6 TCP.bas lines 1511-1513)
        // Default 2 = "empty" animation (NingunArma/NingunEscudo/NingunCasco)
        user.weapon_anim = 2;
        user.shield_anim = 2;
        user.casco_anim = 2;
        user.privileges = char_data.privileges;
        user.saved_privileges = char_data.privileges;
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
    state.online_names.insert(char_name.to_uppercase(), conn_id);
    state.num_users += 1;
    if state.num_users > state.record_users {
        state.record_users = state.num_users;
    }

    // Place on world grid
    state.world.place_user(map, x, y, conn_id);

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
    // Notify other users who have this player as a friend
    broadcast_friend_connect(state, conn_id).await;

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
                user.weapon_anim = common::NINGUN_ARMA;
                user.shield_anim = common::NINGUN_ESCUDO;
                user.casco_anim = common::NINGUN_CASCO;
                if boat_ropaje > 0 {
                    user.body = boat_ropaje;
                }
            }
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

        // Door BQ/HO sync is handled by make_user_visible() → check_update_needed_user()

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
    // VB6 client sends gender as string ("Hombre"/"Mujer"), not integer
    let gender_str = read_field(4, payload, ',');
    let gender: i32 = if gender_str.to_lowercase().contains("ombre") { 1 } else { 2 };
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

    let account_id = state.users.get(&conn_id)
        .map(|u| u.account_id)
        .unwrap_or(0);

    if account_id == 0 {
        state.send_to(conn_id, &format!("{}Debes iniciar sesion primero.", server_opcodes::ERROR)).await;
        close_connection(state, conn_id).await;
        return;
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
    ).await {
        Ok(_char_id) => {
            info!("[AUTH] Character '{}' created successfully — auto-logging in", char_name);
            // Auto-login after creation (VB6 behavior: enter game directly)
            connect_user(state, conn_id, &char_name, &account, "").await;
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

    if !charfile::character_exists(&state.pool, &char_name).await {
        state.send_to(conn_id, &format!("{}El personaje no existe.", server_opcodes::ERROR)).await;
        close_connection(state, conn_id).await;
        return;
    }

    let char_data = match charfile::load_charfile(&state.pool, &char_name).await {
        Ok(data) => data,
        Err(_) => {
            state.send_to(conn_id, &format!("{}Error al leer el personaje.", server_opcodes::ERROR)).await;
            close_connection(state, conn_id).await;
            return;
        }
    };

    // Verify password (security_code from account)
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

    if let Err(e) = charfile::delete_charfile(&state.pool, &char_name).await {
        warn!("[AUTH] Failed to delete character: {}", e);
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

    let account = match accounts::load_account(&state.pool, &account_name).await {
        Ok(acc) => acc,
        Err(_) => {
            state.send_to(conn_id, &format!("{}La cuenta no existe.", server_opcodes::ERROR_SHOW)).await;
            return;
        }
    };

    if !password::verify_password(&old_password, &account.password_hash) {
        state.send_to(conn_id, &format!(
            "{}La Password actual que nos proporciono, no coincide con la del registro.",
            server_opcodes::ERROR_SHOW
        )).await;
        return;
    }

    let new_hash = match password::hash_password(&new_password) {
        Ok(h) => h,
        Err(_) => {
            state.send_to(conn_id, &format!("{}Error interno.", server_opcodes::ERROR_SHOW)).await;
            return;
        }
    };

    match accounts::update_password(&state.pool, &account_name, &new_hash).await {
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

    let account = match accounts::load_account(&state.pool, &account_name).await {
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

    if let Ok(hash) = password::hash_password(&new_pass.to_string()) {
        let _ = accounts::update_password(&state.pool, &account_name, &hash).await;
    }

    state.send_to(conn_id, &format!(
        "{}Has recuperado la cuenta, utiliza la contrasena {} para poder logearte.",
        server_opcodes::ERROR_SHOW, new_pass
    )).await;

    info!("[AUTH] Account '{}' recovered, new password generated", account_name);
}

// Inventory handlers — moved to inventory.rs

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
        // Toggle PvP + clan safety (VB6: Seguro AND SeguroClan toggled together)
        let is_safe = state.users.get(&conn_id).map(|u| u.safe_toggle).unwrap_or(true);
        if let Some(user) = state.users.get_mut(&conn_id) {
            user.safe_toggle = !is_safe;
            user.seguro_cvc = !is_safe;
        }
        if !is_safe {
            state.send_to(conn_id, server_opcodes::SAFE_ON).await;
        } else {
            state.send_to(conn_id, server_opcodes::SAFE_OFF).await;
        }
    } else if cmd_upper == "/SEGR" {
        // Toggle resurrection safety — prevents others from rezzing you (VB6: /SEGR)
        let is_safe = state.users.get(&conn_id).map(|u| u.seguro_resu).unwrap_or(false);
        if let Some(user) = state.users.get_mut(&conn_id) {
            user.seguro_resu = !is_safe;
        }
        if !is_safe {
            state.send_to(conn_id, server_opcodes::SAFE_RESU_ON).await;
        } else {
            state.send_to(conn_id, server_opcodes::SAFE_RESU_OFF).await;
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
        // VB6: Toggle emoticons flag (no server response)
        if let Some(u) = state.users.get_mut(&conn_id) {
            u.emoticons = !u.emoticons;
        }
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
        // VB6: Admin only. Saves current map to disk (GrabarMapa).
        let is_admin = state.users.get(&conn_id).map(|u| u.privileges >= privilege_level::ADMINISTRADOR).unwrap_or(false);
        if !is_admin { return; }
        // Map saving not implemented (maps are loaded read-only from binary files)
        state.send_to(conn_id, &format!("{}Mapa guardado.{}", server_opcodes::CONSOLE_MSG, font_types::INFO)).await;
    } else if cmd_upper.starts_with("/SETDESC ") {
        let args = &cmd[9..];
        handle_slash_setdesc(state, conn_id, args).await;
    } else if cmd_upper == "/RELOADSINI" || cmd_upper == "/LOADOBJ" || cmd_upper == "/LOADHECHIZOS" || cmd_upper == "/LOADNPCS" || cmd_upper == "/LOADBALANCE" || cmd_upper == "/LOADQUESTS" || cmd_upper == "/LOADPREMIOS" {
        // VB6: Admin-only reload commands. Not hot-reloadable in Rust, but acknowledge.
        let is_admin = state.users.get(&conn_id).map(|u| u.privileges >= privilege_level::ADMINISTRADOR).unwrap_or(false);
        if !is_admin { return; }
        state.send_to(conn_id, &format!("{}Datos recargados.{}", server_opcodes::CONSOLE_MSG, font_types::INFO)).await;
    } else if cmd_upper.starts_with("/STOP ") {
        let target = cmd[6..].trim();
        handle_slash_stop(state, conn_id, target, true).await;
    } else if cmd_upper.starts_with("/STOPOFF ") {
        let target = cmd[9..].trim();
        handle_slash_stop(state, conn_id, target, false).await;
    } else if cmd_upper.starts_with("/MODMAPINFO ") {
        // VB6: GM command to modify map properties (PK, PART, LUZ, RGB)
        let is_gm = state.users.get(&conn_id).map(|u| u.privileges >= privilege_level::DIOS).unwrap_or(false);
        if !is_gm { return; }
        let args = &cmd[12..];
        handle_slash_modmapinfo(state, conn_id, args).await;
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

    // Load original head from DB (VB6: OrigChar.Head)
    let (orig_head, gender) = match charfile::load_charfile(&state.pool, &char_name).await {
        Ok(chr) => (chr.head, chr.gender.to_string()),
        Err(_) => (1, "1".to_string()),
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

// Inventory/stats packet helpers
// =====================================================================

/// Build ANM packet (equipment hitbox stats — 20 comma-separated fields).
/// Format: ANM<minArma>,<maxArma>,<minArmor>,<maxArmor>,<minEscudo>,<maxEscudo>,
///         <minCasco>,<maxCasco>,<minHerr>,<maxHerr>, then 10 magic defense fields (all 0 for now).
pub(crate) fn build_anm_packet(state: &GameState, conn_id: ConnectionId) -> String {
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
// send_stats_hp, send_stats_mana, send_stats_sta, send_stats_gold, send_stats_exp — moved to common.rs

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
            user.exp = 0; // VB6 parity: reset exp to 0 on each level up
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

// area_id, build_cd_packet — moved to common.rs

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
    let mut new_door_bqs: Vec<String> = Vec::new(); // BQ packets for door blocked state
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

    // Collect particles, lights, and static .inf objects from static map data
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
                    // VB6: Send HO for static .inf objects (doors, furniture, etc.)
                    // The client can't resolve ObjIndex→GRH, so server must send HO.
                    let oi = tile.obj.obj_index as usize;
                    if oi >= 1 {
                        if let Some(obj) = state.game_data.objects.get(oi - 1) {
                            if obj.grh_index > 0 {
                                new_items.push((obj.grh_index, sx, sy));
                            }

                            // VB6 ModAreas.bas:273-300 — send BQ for door tiles + adjacent tiles
                            // This ensures correct blocked state regardless of what .map file says
                            if obj.obj_type == crate::data::objects::ObjType::Door {
                                let blocked_at = |ty: i32, tx: i32| -> i32 {
                                    if tx >= 1 && tx <= 100 && ty >= 1 && ty <= 100 {
                                        if game_map.tiles[(ty - 1) as usize][(tx - 1) as usize].blocked { 1 } else { 0 }
                                    } else { 0 }
                                };

                                // Always send BQ for door tile + x-1 (single door minimum)
                                new_door_bqs.push(format!("BQ{},{},{}", sx, sy, blocked_at(sy, sx)));
                                new_door_bqs.push(format!("BQ{},{},{}", sx - 1, sy, blocked_at(sy, sx - 1)));

                                if obj.puerta_doble == 1 {
                                    new_door_bqs.push(format!("BQ{},{},{}", sx + 1, sy, blocked_at(sy, sx + 1)));
                                    new_door_bqs.push(format!("BQ{},{},{}", sx + 2, sy, blocked_at(sy, sx + 2)));
                                } else if obj.porton == 1 || obj.reja_forta == 1 {
                                    for dx in [-2i32, -1, 0, 1, 2] {
                                        new_door_bqs.push(format!("BQ{},{},{}", sx + dx, sy, blocked_at(sy, sx + dx)));
                                    }
                                }

                                // Special objects 1472/1470: always force unblocked (VB6 line 292-298)
                                if oi == 1472 || oi == 1470 {
                                    for dx in [-2i32, -1, 0, 1, 2] {
                                        new_door_bqs.push(format!("BQ{},{},0", sx + dx, sy));
                                    }
                                }
                            }
                        }
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

    // Send door BQ packets — VB6 ModAreas.bas lines 273-300
    for bq in new_door_bqs {
        state.send_to(conn_id, &bq).await;
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
/// VB6: WarpMascotas — teleport user's pets to the new map.
/// Persistent pets are removed from old map and respawned at master's new position.
async fn warp_mascotas(state: &mut GameState, owner_conn: ConnectionId, new_map: i32, new_x: i32, new_y: i32) {
    use crate::game::npc::{ELEMENTAL_AGUA, ELEMENTAL_FUEGO, ELEMENTAL_TIERRA, AI_FOLLOW_OWNER};

    let pets = match state.users.get(&owner_conn) {
        Some(u) => (u.mascotas_index, u.mascotas_type, u.nro_mascotas),
        None => return,
    };
    let (pet_indices, pet_types, _nro) = pets;

    // Collect pet info before mutation
    let mut pets_to_move: Vec<(usize, i32)> = Vec::new(); // (slot_index, npc_type)
    for i in 0..3 {
        let idx = pet_indices[i];
        if idx == 0 { continue; }
        let npc_type = pet_types[i];
        if npc_type <= 0 { continue; }

        // Check if pet is alive and on the OLD map (not already on the new map)
        let pet_alive = state.get_npc(idx).map(|n| n.is_alive() && n.map != new_map).unwrap_or(false);
        if pet_alive {
            pets_to_move.push((i, npc_type));

            // Remove old NPC from world (send BP to area)
            let old_data = state.get_npc(idx).map(|n| (n.char_index, n.map, n.x, n.y));
            if let Some((ci, omap, ox, oy)) = old_data {
                let bp = format!("BP{}", ci.0);
                state.send_data(SendTarget::ToArea { map: omap, x: ox, y: oy }, &bp).await;
            }
            state.kill_npc(idx);

            // Clear old slot
            if let Some(user) = state.users.get_mut(&owner_conn) {
                user.mascotas_index[i] = 0;
                user.mascotas_type[i] = 0;
                user.nro_mascotas = (user.nro_mascotas - 1).max(0);
            }
        }
    }

    // Respawn pets at new position
    for (slot, npc_type) in pets_to_move {
        if let Some(new_idx) = state.spawn_npc(npc_type as usize, new_map, new_x, new_y) {
            // Link to owner
            if let Some(npc) = state.get_npc_mut(new_idx) {
                npc.maestro_user = Some(owner_conn);
                npc.movement = AI_FOLLOW_OWNER;
            }

            // Update user tracking
            if let Some(user) = state.users.get_mut(&owner_conn) {
                user.mascotas_index[slot] = new_idx;
                user.mascotas_type[slot] = npc_type;
                user.nro_mascotas += 1;

                // Restore elemental flags
                match npc_type {
                    ELEMENTAL_AGUA => user.ele_de_agua = true,
                    ELEMENTAL_FUEGO => user.ele_de_fuego = true,
                    ELEMENTAL_TIERRA => user.ele_de_tierra = true,
                    _ => {}
                }
            }

            // Broadcast new NPC to area
            let cc_pkt = state.get_npc(new_idx).map(|n| n.build_cc_packet());
            if let Some(pkt) = cc_pkt {
                state.send_data(SendTarget::ToArea { map: new_map, x: new_x, y: new_y }, &pkt).await;
            }
        }
    }
}

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

    // Door BQ/HO sync is handled by make_user_visible() → check_update_needed_user()

    // 13. BKW — fade back in (VB6 end of WarpUserChar)
    state.send_to(conn_id, "BKW").await;

    // 14. Warp pets to new map (VB6: WarpMascotas)
    warp_mascotas(state, conn_id, new_map, final_x, final_y).await;

    // 15. Check tile exit at destination — VB6 also triggers teleports on warp arrival
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
// find_free_pos — moved to common.rs

// zona_cura — moved to common.rs

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
// Helper functions — NPC system moved to npcs.rs
// =====================================================================

// =====================================================================
// Anti-cheat check helpers (Mod_AntiCheat.bas)
// =====================================================================

/// Check if melee attack is allowed; if so, set cooldown.
// puede_pegar, puede_flechear, puede_castear, puede_potear,
// puede_trabajar, puede_clickear — moved to common.rs

/// Send hunger and thirst stats (EHYS packet).
// send_hunger_thirst — moved to common.rs

// Factions, mail, friends — moved to social.rs
// Player slash commands — moved to player_commands.rs
// Misc handlers — moved to misc_handlers.rs

// =====================================================================
// Integration tests — full client login flow
// =====================================================================

// Integration tests disabled — require PostgreSQL. TODO: add DB-backed tests.
// See git history for the original file-based test suite.
#[cfg(test)]
mod tests {
    #[test]
    fn placeholder() {
        // Tests require a running PostgreSQL instance.
        // Run with: DATABASE_URL=postgres://... cargo test
    }
}

#[cfg(all(test, feature = "_db_integration_tests"))]
mod db_tests {
    use super::*;
    use crate::config::ServerConfig;
    use crate::data::GameData;
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
