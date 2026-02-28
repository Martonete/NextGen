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
// Re-export quest/party functions called from other modules
pub use quests_party::{quest_check_npc_kill, quest_check_player_kill, party_share_exp};
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
use crate::data::{accounts, charfile};
use crate::data::objects::ObjType;
use crate::data::maps::Trigger;
use crate::data::guilds;
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
            // VB6: InvUsuario.bas Case eOBJType.otBarcos + Trabajo.bas DoNavega
            let (is_navigating, user_map, user_x, user_y) = match state.users.get(&conn_id) {
                Some(u) if u.logged => (u.navigating, u.pos_map, u.pos_x, u.pos_y),
                _ => return,
            };

            // VB6 mount check: LegalPos(adjacent, PuedeAgua=True) checks water + not blocked + no user/NPC
            // VB6 dismount: NO land check — always allowed (player may get stuck on water)
            if !is_navigating {
                // Mount — must have water tile adjacent (VB6: 4 cardinal LegalPos checks)
                let has_water_nearby = (1..=4).any(|h| {
                    let (dx, dy) = world::heading_to_offset(h);
                    let nx = user_x + dx;
                    let ny = user_y + dy;
                    // VB6 LegalPos(map, x, y, PuedeAgua=True): not blocked AND has water AND no user/NPC
                    !state.is_tile_blocked(user_map, nx, ny)
                        && state.hay_agua(user_map, nx, ny)
                        && state.world.grids.get(&user_map)
                            .map(|g| g.is_tile_free(nx, ny)).unwrap_or(false)
                });
                if !has_water_nearby {
                    state.send_to(conn_id, "||106").await; // TEXTO106
                    return;
                }
            }

            // VB6 DoNavega: If hidden, reveal first (NOVER)
            let (was_hidden, char_index_val, map_for_nover) = match state.users.get(&conn_id) {
                Some(u) => (u.hidden, u.char_index.0, u.pos_map),
                None => return,
            };
            if was_hidden {
                if let Some(u) = state.users.get_mut(&conn_id) {
                    u.hidden = false;
                }
                let nover = format!("NOVER{},0", char_index_val);
                state.send_data(SendTarget::ToMap(map_for_nover), &nover).await;
            }

            if is_navigating {
                // === DISMOUNT (VB6 DoNavega else branch) ===
                // No land check — VB6 allows dismounting anywhere
                let equip_info = state.users.get(&conn_id).map(|u| {
                    let get_inv_obj = |slot: usize| -> i32 {
                        if slot >= 1 && slot <= u.inventory.len() { u.inventory[slot - 1].obj_index } else { 0 }
                    };
                    (
                        get_inv_obj(u.equip.armor),
                        get_inv_obj(u.equip.weapon),
                        get_inv_obj(u.equip.shield),
                        get_inv_obj(u.equip.helmet),
                        u.old_head, u.dead, u.race.clone(), u.gender,
                    )
                });
                if let Some((armor_obj, weapon_obj, shield_obj, helmet_obj, saved_head, dead, race, gender)) = equip_info {
                    let armor_body = state.get_object(armor_obj).map(|o| o.num_ropaje).unwrap_or(0);
                    let weapon_anim = state.get_object(weapon_obj).map(|o| o.weapon_anim).unwrap_or(0);
                    let shield_anim = state.get_object(shield_obj).map(|o| o.shield_anim).unwrap_or(0);
                    let casco_anim = state.get_object(helmet_obj).map(|o| o.casco_anim).unwrap_or(0);
                    if let Some(u) = state.users.get_mut(&conn_id) {
                        u.navigating = false;
                        if !dead {
                            u.head = saved_head;
                            u.body = if armor_body > 0 { armor_body } else { naked_body(&race, &gender.to_string()) };
                            u.weapon_anim = weapon_anim;
                            u.shield_anim = shield_anim;
                            u.casco_anim = casco_anim;
                        } else {
                            // VB6: dead dismount → ghost body/head, no equipment
                            u.body = DEAD_BODY_NEUTRAL;
                            u.head = DEAD_HEAD_NEUTRAL;
                            u.weapon_anim = 0;
                            u.shield_anim = 0;
                            u.casco_anim = 0;
                        }
                    }
                }
            } else {
                // === MOUNT (VB6 DoNavega if branch) ===
                let ropaje = obj_data.num_ropaje;
                if let Some(u) = state.users.get_mut(&conn_id) {
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
            }

            // VB6 DoNavega packets (order matters):
            // 1. ChangeUserChar → CP packet to area (including self)
            // 2. NAVEG to self
            // 3. NVG<charindex>,<flag> to ALL players
            let (cp_pkt, nvg_pkt, map, bx, by) = match state.users.get(&conn_id) {
                Some(u) => {
                    let nav_flag = if u.navigating { 1 } else { 0 };
                    // VB6 CP format: CP<charindex>,<body>,<head>,<heading>,<weapon>,<shield>,<fx>,<loops>,<helmet>
                    let cp = format!("CP{},{},{},{},{},{},{},{},{}",
                        u.char_index.0, u.body, u.head, u.heading,
                        u.weapon_anim, u.shield_anim,
                        0, 0, // FX, loops (no active FX during boat toggle)
                        u.casco_anim,
                    );
                    let nvg = format!("NVG{},{}", u.char_index.0, nav_flag);
                    (cp, nvg, u.pos_map, u.pos_x, u.pos_y)
                }
                None => return,
            };
            // CP to area (VB6 SendToUserArea = includes self)
            state.send_data(SendTarget::ToArea { map, x: bx, y: by }, &cp_pkt).await;
            // NAVEG to self (toggle client navigation state)
            state.send_to(conn_id, "NAVEG").await;
            // NVG to all (VB6 SendTarget.ToAll)
            state.send_data(SendTarget::ToAll, &nvg_pkt).await;
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
// get_map_tile_trigger — moved to common.rs
// get_map_tile_obj — moved to common.rs

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

// set_map_tile_obj, set_map_tile_blocked, health_description — moved to common.rs

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
