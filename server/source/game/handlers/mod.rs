pub(crate) mod common;
mod gm_teleport;
mod gm_moderation;
mod gm_items;
mod gm_server;
mod gm_query;
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
mod misc_packets;
mod misc_slash;
mod inventory;
mod auth;
mod movement;
mod warp;
mod leveling;
mod parity_gameplay;
mod parity_gm;
use common::*;
use gm_teleport::*;
use gm_moderation::*;
use gm_items::*;
use gm_server::*;
use gm_query::*;
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
use misc_packets::*;
use misc_slash::*;
use parity_gameplay::*;
use parity_gm::*;
use inventory::*;
// Re-export functions from new submodules so sibling modules can use `super::fn_name`
pub(crate) use warp::{
    make_user_visible, check_update_needed_user, warp_user,
    warp_user_exact, mover_casper, send_warp_fx,
};
pub(crate) use leveling::check_user_level;
// Re-export quest/party functions called from other modules
// Re-export tick functions called from main.rs
pub use ticks::{
    tick_npc_ai, tick_npc_respawn, tick_player_passive,
    tick_intervals, tick_clean_world, tick_security,
    auto_save_all_users, build_char_save_data,
};
pub(crate) use ticks::remove_pet_from_owner;
// Re-export event functions called from main.rs and other modules
pub use events::{
    pretoriano_check_death, ip_security_accept,
};

// Packet handlers — processes decrypted client packets.
//
// Pre-login flow:
//   1. Client sends HardwareCheck + HD serial → server checks ban
//   2. Client sends AccountLogin + account,password → server validates, sends INIAC + ADDPJ + CODEH
//      OR client sends CreateAccount + account,password,pin → server creates account
//   3. Client sends CharacterSelect/CharacterLogin + charname,account,codex → server loads character, sends LOGGED
//      OR client sends CreateCharacter + charname,race,gender,class,... → server creates character
//   4. Client sends RollDice → server sends dice roll (during char creation)
//   5. Client sends DeleteCharacter → server deletes character
//
// In-game flow:
//   M<heading> — movement
//   CHEA<heading> — change heading
//   RPU — request position update
//   ; — chat message (talk)

// Many functions/constants are declared for VB6 parity but not yet wired up.

use tracing::info;

use crate::net::ConnectionId;
use crate::protocol::{client_opcodes, font_index, fields::read_field};
use crate::protocol::byte_queue::ByteQueue;
use crate::protocol::packets::ClientPacketID;
use crate::protocol::binary_packets;
use crate::db::charfile;
use super::types::{GameState, privilege_level};
use super::world;

// ─── BINARY PROTOCOL DISPATCH ──────────────────────────────────────────────
// Modern 13.3-style binary protocol: 1-byte opcode + typed fields via ByteQueue.
// Entry point: handle_packet_stream (called from network layer)
// ────────────────────────────────────────────────────────────────────────────

/// Decode coordinate-bearing packet using the per-connection rolling cipher.
/// Returns None if cipher is not active (pre-login) or decoding fails validation.
fn decode_coords(state: &mut GameState, conn_id: ConnectionId, enc_x: i16, enc_y: i16) -> Option<(i16, i16)> {
    let (map, _map_w, _map_h) = state.users.get(&conn_id)
        .map(|u| (u.pos_map, 0i32, 0i32))
        .unwrap_or((0, 0, 0));
    let (grid_w, grid_h) = state.grid_dimensions(map);

    if let Some(user) = state.users.get_mut(&conn_id) {
        if let Some(cipher) = user.coord_cipher.as_mut() {
            return cipher.decode_tolerant(enc_x, enc_y, grid_w, grid_h);
        }
    }
    None // No cipher = reject (must be logged in to send coordinate packets)
}

/// Process all binary packets in a TCP data chunk.
///
/// Prepends any leftover bytes from previous reads, processes complete packets
/// in a loop, and saves any remaining partial packet bytes.
pub async fn handle_packet_stream(state: &mut GameState, conn_id: ConnectionId, new_data: &[u8]) {
    // Prepend leftover data from previous reads
    let data = if let Some(mut prev) = state.recv_buffers.remove(&conn_id) {
        prev.extend_from_slice(new_data);
        prev
    } else {
        new_data.to_vec()
    };

    if data.is_empty() {
        return;
    }

    // Recv buffer size cap — drop connection if accumulated buffer exceeds 64KB.
    // Defense against partial-packet flooding (sending incomplete packets to bloat memory).
    if data.len() > crate::net::connection::MAX_RECV_BUFFER {
        tracing::warn!(
            "[SEC] Connection #{} recv buffer overflow ({} bytes), disconnecting",
            conn_id, data.len()
        );
        state.security_kick_queue.push(conn_id);
        return;
    }

    let mut bq = ByteQueue::from_bytes(&data);
    let max_pps = state.max_packets_per_second;

    // Loop through all complete packets in the buffer
    loop {
        if bq.remaining() == 0 {
            break;
        }

        // Per-connection packet rate limiting.
        // Increment counter and check against max_packets_per_second.
        // If exceeded, stop processing — remaining data stays in buffer
        // and tick_security() will handle strike/kick logic.
        let count = state.packet_counts.entry(conn_id).or_insert(0);
        *count += 1;
        if *count > max_pps {
            // Over rate limit — save remaining bytes for next window
            let leftover = bq.read_remaining();
            if !leftover.is_empty() {
                state.recv_buffers.insert(conn_id, leftover);
            }
            tracing::debug!(
                "[SEC] Connection #{} exceeded packet rate ({}/s), throttled",
                conn_id, max_pps
            );
            break;
        }

        // Save position before attempting to read a packet.
        // If we can't read a complete packet, restore and save leftovers.
        let saved_pos = bq.save_position();

        match handle_one_packet(state, conn_id, &mut bq).await {
            PacketResult::Ok => {
                // Packet processed successfully, continue to next
            }
            PacketResult::Incomplete => {
                // Not enough data for a complete packet — save leftovers
                bq.restore_position(saved_pos);
                let leftover = bq.read_remaining();
                if !leftover.is_empty() {
                    state.recv_buffers.insert(conn_id, leftover);
                }
                break;
            }
            PacketResult::Error => {
                // Invalid data — discard remaining buffer
                break;
            }
        }
    }
}

enum PacketResult {
    Ok,
    Incomplete,
    Error,
}

/// Route a single binary packet from the ByteQueue.
///
/// 13.3-style binary protocol: reads 1-byte opcode, then typed fields via ByteQueue.
/// Bridge layer: converts binary fields to the text format existing handlers expect.
async fn handle_one_packet(state: &mut GameState, conn_id: ConnectionId, bq: &mut ByteQueue) -> PacketResult {
    let packet_id_byte = match bq.read_byte() {
        Ok(b) => b,
        Err(_) => return PacketResult::Incomplete,
    };

    let packet_id = match ClientPacketID::from_byte(packet_id_byte) {
        Some(id) => id,
        None => {
            info!("[PKT] Unknown binary packet ID {} from #{}", packet_id_byte, conn_id);
            return PacketResult::Error;
        }
    };

    // Bridge: read binary fields, call typed handlers directly.
    match packet_id {
        // Pre-login
        ClientPacketID::HardwareCheck => {
            let hd = bq.read_ascii_string().unwrap_or_default();
            auth::handle_hardware_check(state, conn_id, &hd).await;
        }
        ClientPacketID::AccountLogin => {
            let account = bq.read_ascii_string().unwrap_or_default();
            let password = bq.read_ascii_string().unwrap_or_default();
            auth::handle_account_login(state, conn_id, &account, &password).await;
        }
        ClientPacketID::CreateCharacter => {
            // Read all fields from binary
            let char_name = bq.read_ascii_string().unwrap_or_default();
            let race_id = bq.read_byte().unwrap_or(0);
            let gender = bq.read_byte().unwrap_or(0);
            let class_id = bq.read_byte().unwrap_or(0);
            let head = bq.read_integer().unwrap_or(0);
            let homeland = bq.read_byte().unwrap_or(0);
            let account = bq.read_ascii_string().unwrap_or_default();
            // Client sends byte IDs — convert to name strings matching client ClassNames/RaceNames arrays
            let race_name = match race_id {
                1 => "Humano", 2 => "Elfo", 3 => "Elfo Oscuro", 4 => "Enano", 5 => "Gnomo",
                _ => "Humano",
            };
            // VB6 eClass: 1=Mago,2=Clerigo,3=Guerrero,4=Asesino,5=Ladron,6=Bardo,
            //   7=Druida,8=Bandido,9=Paladin,10=Cazador,11=Trabajador,12=Pirata
            let class_name = match class_id {
                1 => "Mago", 2 => "Clerigo", 3 => "Guerrero", 4 => "Asesino",
                5 => "Ladron", 6 => "Bardo", 7 => "Druida", 8 => "Bandido",
                9 => "Paladin", 10 => "Cazador", 11 => "Trabajador", 12 => "Pirata",
                _ => "Guerrero",
            };
            let gender_i32: i32 = if gender == 1 { 1 } else { 2 };
            auth::handle_create_character(state, conn_id, &char_name, race_name, gender_i32, class_name, homeland as i32, &account, head as i32).await;
        }
        ClientPacketID::CharacterLogin => {
            let char_name = bq.read_ascii_string().unwrap_or_default();
            let account = bq.read_ascii_string().unwrap_or_default();
            let codex = bq.read_ascii_string().unwrap_or_default();
            auth::handle_character_login(state, conn_id, &char_name, &account, &codex).await;
        }
        ClientPacketID::CharacterSelect => {
            let char_name = bq.read_ascii_string().unwrap_or_default();
            let account = bq.read_ascii_string().unwrap_or_default();
            let codex = bq.read_ascii_string().unwrap_or_default();
            auth::handle_character_select(state, conn_id, &char_name, &account, &codex).await;
        }
        ClientPacketID::CreateAccount => {
            let account = bq.read_ascii_string().unwrap_or_default();
            let password = bq.read_ascii_string().unwrap_or_default();
            let pin = bq.read_ascii_string().unwrap_or_default();
            auth::handle_create_account(state, conn_id, &account, &password, &pin).await;
        }
        ClientPacketID::ChangePassword => {
            let old_pass = bq.read_ascii_string().unwrap_or_default();
            let new_pass = bq.read_ascii_string().unwrap_or_default();
            let account = state.users.get(&conn_id)
                .map(|u| u.account_name.clone())
                .unwrap_or_default();
            auth::handle_change_password(state, conn_id, &account, &old_pass, &new_pass, &new_pass).await;
        }
        ClientPacketID::AccountRecovery => {
            let account = bq.read_ascii_string().unwrap_or_default();
            let pin = bq.read_ascii_string().unwrap_or_default();
            auth::handle_account_recovery(state, conn_id, &account, &pin).await;
        }
        ClientPacketID::RollDice => {
            auth::handle_roll_dice(state, conn_id).await;
        }
        ClientPacketID::DeleteCharacter => {
            let char_name = bq.read_ascii_string().unwrap_or_default();
            // Binary client only sends name. Account is from connection state.
            // For security verification, load the charfile password (codex) server-side
            // since the client is already authenticated via AccountLogin.
            let account = state.users.get(&conn_id)
                .map(|u| u.account_name.clone())
                .unwrap_or_default();
            let codex = charfile::load_charfile(&state.pool, &char_name).await
                .map(|c| c.password).unwrap_or_default();
            auth::handle_delete_character(state, conn_id, &char_name, &account, &codex).await;
        }

        // Movement
        ClientPacketID::Walk => {
            let heading = bq.read_byte().unwrap_or(1);
            movement::handle_walk(state, conn_id, heading as i32).await;
        }
        ClientPacketID::ChangeHeading => {
            let heading = bq.read_byte().unwrap_or(1);
            movement::handle_change_heading(state, conn_id, heading as i32).await;
        }
        ClientPacketID::RequestPos => {
            movement::handle_request_pos(state, conn_id).await;
        }
        ClientPacketID::SyncPosition => {
            handle_actualizar(state, conn_id).await;
        }

        // Combat
        ClientPacketID::Attack => {
            handle_attack(state, conn_id).await;
        }
        ClientPacketID::CastSpell => {
            let slot = bq.read_byte().unwrap_or(0);
            handle_cast_spell(state, conn_id, slot as usize).await;
        }
        ClientPacketID::LeftClick => {
            let enc_x = bq.read_integer().unwrap_or(0);
            let enc_y = bq.read_integer().unwrap_or(0);
            if let Some((x, y)) = decode_coords(state, conn_id, enc_x, enc_y) {
                handle_left_click(state, conn_id, x as i32, y as i32).await;
            }
        }
        ClientPacketID::RightClick => {
            let enc_x = bq.read_integer().unwrap_or(0);
            let enc_y = bq.read_integer().unwrap_or(0);
            if let Some((x, y)) = decode_coords(state, conn_id, enc_x, enc_y) {
                handle_right_click(state, conn_id, x as i32, y as i32).await;
            }
        }
        ClientPacketID::WorkLeftClick => {
            let enc_x = bq.read_integer().unwrap_or(0);
            let enc_y = bq.read_integer().unwrap_or(0);
            let skill = bq.read_byte().unwrap_or(0);
            if let Some((x, y)) = decode_coords(state, conn_id, enc_x, enc_y) {
                handle_work_left_click(state, conn_id, x as i32, y as i32, skill as i32).await;
            }
        }

        // Chat
        ClientPacketID::Talk => {
            let msg = bq.read_ascii_string().unwrap_or_default();
            if msg.starts_with('/') {
                let logged = state.users.get(&conn_id).map(|u| u.logged).unwrap_or(false);
                if logged { handle_slash_command(state, conn_id, &msg).await; }
            } else {
                social::handle_talk(state, conn_id, &msg).await;
            }
        }
        ClientPacketID::Yell => {
            let msg = bq.read_ascii_string().unwrap_or_default();
            social::handle_yell(state, conn_id, &msg).await;
        }
        ClientPacketID::Whisper => {
            let target = bq.read_ascii_string().unwrap_or_default();
            let msg = bq.read_ascii_string().unwrap_or_default();
            social::handle_whisper(state, conn_id, &target, &msg).await;
        }
        ClientPacketID::SlashCommand => {
            let cmd = bq.read_ascii_string().unwrap_or_default();
            if cmd.starts_with('/') {
                let logged = state.users.get(&conn_id).map(|u| u.logged).unwrap_or(false);
                if logged { handle_slash_command(state, conn_id, &cmd).await; }
            } else {
                social::handle_talk(state, conn_id, &cmd).await;
            }
        }

        // Items
        ClientPacketID::PickUp => {
            handle_pick_up(state, conn_id).await;
        }
        ClientPacketID::DropItem => {
            let slot = bq.read_byte().unwrap_or(0);
            let amount = bq.read_integer().unwrap_or(0);
            // FLAGORO: slot 0xFF (255 as u8 = -1 as i8) means drop gold
            if slot == 0xFF {
                handle_drop_gold(state, conn_id, amount as i32).await;
            } else {
                handle_drop_item(state, conn_id, slot as usize, amount as i32).await;
            }
        }
        ClientPacketID::UseItem => {
            let slot = bq.read_byte().unwrap_or(0);
            handle_use_item(state, conn_id, slot as usize).await;
        }
        ClientPacketID::UseItemClick => {
            let slot = bq.read_byte().unwrap_or(0);
            handle_use_item_click(state, conn_id, slot as usize).await;
        }
        ClientPacketID::EquipItem => {
            let slot = bq.read_byte().unwrap_or(0);
            handle_equip(state, conn_id, slot as usize).await;
        }
        ClientPacketID::SwapItems => {
            let from = bq.read_byte().unwrap_or(0);
            let to = bq.read_byte().unwrap_or(0);
            handle_swap(state, conn_id, from as usize, to as usize).await;
        }
        ClientPacketID::MouseDrop => {
            let slot = bq.read_byte().unwrap_or(0);
            let amount = bq.read_integer().unwrap_or(0);
            handle_mouse_drop(state, conn_id, slot as usize, amount as i32).await;
        }

        // Skills
        ClientPacketID::UseSkill => {
            let skill_id = bq.read_byte().unwrap_or(0);
            handle_uk(state, conn_id, skill_id as i32).await;
        }
        ClientPacketID::SkillSet => {
            // Read 20 bytes into array, pad remaining with 0
            let mut increments = [0i32; 22];
            for i in 0..20 {
                increments[i] = bq.read_byte().unwrap_or(0) as i32;
            }
            handle_skse(state, conn_id, &increments).await;
        }
        ClientPacketID::Meditate => {
            handle_meditate(state, conn_id).await;
        }
        ClientPacketID::SafeToggle => {
            handle_safe_toggle(state, conn_id).await;
        }
        ClientPacketID::MacroDetect => {
            handle_tengomacros(state, conn_id).await;
        }
        ClientPacketID::LevelBonus => {
            let bonus = bq.read_byte().unwrap_or(0);
            handle_bof(state, conn_id, bonus as i32).await;
        }

        // Spells
        ClientPacketID::SpellInfo => {
            let slot = bq.read_byte().unwrap_or(0);
            handle_infs(state, conn_id, slot as usize).await;
        }
        ClientPacketID::MoveSpell => {
            let from = bq.read_byte().unwrap_or(0);
            let to = bq.read_byte().unwrap_or(0);
            handle_desphe(state, conn_id, from as i32, to as usize).await;
        }
        ClientPacketID::CastByName => {
            let target = bq.read_ascii_string().unwrap_or_default();
            handle_downsi(state, conn_id, &target).await;
        }

        // Commerce
        ClientPacketID::CommerceBuy => {
            let slot = bq.read_byte().unwrap_or(0);
            let amount = bq.read_integer().unwrap_or(0);
            handle_commerce_buy(state, conn_id, slot as usize, amount as i32).await;
        }
        ClientPacketID::CommerceSell => {
            let slot = bq.read_byte().unwrap_or(0);
            let amount = bq.read_integer().unwrap_or(0);
            handle_commerce_sell(state, conn_id, slot as usize, amount as i32).await;
        }
        ClientPacketID::CommerceClose => {
            handle_commerce_close(state, conn_id).await;
        }
        ClientPacketID::TradeOfferGold => {
            let amount = bq.read_long().unwrap_or(0);
            handle_trade_offer_gold(state, conn_id, amount as i64).await;
        }
        ClientPacketID::TradeOfferItem => {
            let slot = bq.read_byte().unwrap_or(0);
            let amount = bq.read_integer().unwrap_or(0);
            handle_trade_offer_item(state, conn_id, slot as usize, amount as i32).await;
        }
        ClientPacketID::TradeResponse => {
            let response = bq.read_byte().unwrap_or(0);
            handle_trade_response(state, conn_id, response as i32).await;
        }
        ClientPacketID::TradeCancel => {
            handle_trade_cancel(state, conn_id).await;
        }
        ClientPacketID::TradeChat => {
            let msg = bq.read_ascii_string().unwrap_or_default();
            handle_trade_chat(state, conn_id, &msg).await;
        }

        // Banking — route to guild bank or personal bank based on open state.
        // Slot 0 is the gold sentinel: deposit/withdraw gold instead of an item.
        ClientPacketID::BankDeposit => {
            let slot = bq.read_byte().unwrap_or(0);
            let amount = bq.read_integer().unwrap_or(0);
            let is_guild = state.users.get(&conn_id).map(|u| u.guild_bank_open).unwrap_or(false);
            if is_guild {
                if slot == 0 {
                    handle_guild_bank_deposit_gold(state, conn_id, amount as i32).await;
                } else {
                    handle_guild_bank_deposit(state, conn_id, slot as usize, amount as i32).await;
                }
            } else {
                handle_bank_deposit(state, conn_id, slot as usize, amount as i32).await;
            }
        }
        ClientPacketID::BankWithdraw => {
            let slot = bq.read_byte().unwrap_or(0);
            let amount = bq.read_integer().unwrap_or(0);
            let is_guild = state.users.get(&conn_id).map(|u| u.guild_bank_open).unwrap_or(false);
            if is_guild {
                if slot == 0 {
                    handle_guild_bank_withdraw_gold(state, conn_id, amount as i32).await;
                } else {
                    handle_guild_bank_withdraw(state, conn_id, slot as usize, amount as i32).await;
                }
            } else {
                handle_bank_withdraw(state, conn_id, slot as usize, amount as i32).await;
            }
        }
        ClientPacketID::BankClose => {
            let is_guild = state.users.get(&conn_id).map(|u| u.guild_bank_open).unwrap_or(false);
            if is_guild {
                handle_guild_bank_close(state, conn_id).await;
            } else {
                handle_bank_close(state, conn_id).await;
            }
        }

        // Crafting
        ClientPacketID::ConstructSmith => {
            let item = bq.read_integer().unwrap_or(0);
            handle_construct_smith(state, conn_id, item as i32).await;
        }
        ClientPacketID::ConstructCarp => {
            let item = bq.read_integer().unwrap_or(0);
            handle_construct_carp(state, conn_id, item as i32).await;
        }
        ClientPacketID::TrainCreature => {
            let pet = bq.read_byte().unwrap_or(0);
            handle_entr(state, conn_id, pet as i32).await;
        }

        // Guild
        ClientPacketID::GuildInfo => {
            handle_guild_info(state, conn_id).await;
        }
        ClientPacketID::GuildCreate => {
            let data_str = bq.read_ascii_string().unwrap_or_default();
            handle_guild_create(state, conn_id, &data_str).await;
        }
        ClientPacketID::GuildUpdateCodex => {
            let data_str = bq.read_ascii_string().unwrap_or_default();
            handle_guild_update_codex(state, conn_id, &data_str).await;
        }
        ClientPacketID::GuildAccept => {
            let name = bq.read_ascii_string().unwrap_or_default();
            handle_guild_accept(state, conn_id, &name).await;
        }
        ClientPacketID::GuildReject => {
            let name = bq.read_ascii_string().unwrap_or_default();
            handle_guild_reject(state, conn_id, &name).await;
        }
        ClientPacketID::GuildExpel => {
            let name = bq.read_ascii_string().unwrap_or_default();
            handle_guild_expel(state, conn_id, &name).await;
        }
        ClientPacketID::GuildNews => {
            let data_str = bq.read_ascii_string().unwrap_or_default();
            handle_guild_news(state, conn_id, &data_str).await;
        }
        ClientPacketID::GuildApply => {
            let data_str = bq.read_ascii_string().unwrap_or_default();
            handle_guild_apply(state, conn_id, &data_str).await;
        }
        ClientPacketID::GuildDetails => {
            let name = bq.read_ascii_string().unwrap_or_default();
            handle_guild_details(state, conn_id, &name).await;
        }

        // Forum
        ClientPacketID::ForumPost => {
            let msg_type = bq.read_byte().unwrap_or(0);
            let title = bq.read_ascii_string().unwrap_or_default();
            let body = bq.read_ascii_string().unwrap_or_default();
            inventory::handle_forum_post(state, conn_id, msg_type, title, body).await;
        }


        // Player info
        ClientPacketID::PlayerInfo => {
            let data_str = bq.read_ascii_string().unwrap_or_default();
            handle_daminf(state, conn_id, &data_str).await;
        }
        ClientPacketID::MiniStats => {
            handle_fest(state, conn_id).await;
        }
        ClientPacketID::HeadChange => {
            let data_str = bq.read_ascii_string().unwrap_or_default();
            handle_cabezi(state, conn_id, data_str.parse().unwrap_or(0)).await;
        }
        ClientPacketID::Rankings => {
            let data_str = bq.read_ascii_string().unwrap_or_default();
            handle_rankings(state, conn_id, &data_str).await;
        }

        // Misc
        ClientPacketID::HouseQuery => {
            let data_str = bq.read_ascii_string().unwrap_or_default();
            handle_fwo(state, conn_id, &data_str).await;
        }
        ClientPacketID::HouseBuy => {
            let data_str = bq.read_ascii_string().unwrap_or_default();
            handle_cuc(state, conn_id, &data_str).await;
        }
        ClientPacketID::PetRename => {
            let data_str = bq.read_ascii_string().unwrap_or_default();
            handle_cnm(state, conn_id, &data_str).await;
        }
        ClientPacketID::Vote => {
            let data_str = bq.read_ascii_string().unwrap_or_default();
            handle_slash_voto(state, conn_id, &data_str).await;
        }
        ClientPacketID::DragDrop => {
            let data_str = bq.read_ascii_string().unwrap_or_default();
            let slot: usize = read_field(1, &data_str, ',').parse().unwrap_or(0);
            let amount: i32 = read_field(2, &data_str, ',').parse().unwrap_or(0);
            handle_dydtra(state, conn_id, slot, amount).await;
        }
        ClientPacketID::Report => {
            let data_str = bq.read_ascii_string().unwrap_or_default();
            let target_name = read_field(1, &data_str, ',');
            let reason = read_field(2, &data_str, ',');
            handle_newd(state, conn_id, &target_name, &reason).await;
        }
        ClientPacketID::SosView => {
            handle_consul(state, conn_id).await;
        }
        ClientPacketID::SosSend => {
            let data_str = bq.read_ascii_string().unwrap_or_default();
            let contenido = read_field(2, &data_str, '|');
            handle_sos_send(state, conn_id, &contenido).await;
        }
        ClientPacketID::SosRespond => {
            let data_str = bq.read_ascii_string().unwrap_or_default();
            let target_name = read_field(1, &data_str, '*');
            let texto = read_field(2, &data_str, '*');
            handle_sos_respond(state, conn_id, &target_name, &texto).await;
        }
    }

    PacketResult::Ok
}

// ─── LEGACY TEXT PROTOCOL DISPATCH ─────────────────────────────────────────
// VB6-era text/opcode protocol: string prefix matching via client_opcodes constants.
// Entry point: handle_packet (called from network layer for pre-13.3 clients)
// ────────────────────────────────────────────────────────────────────────────

/// Route a decrypted packet to the appropriate handler.
pub async fn handle_packet(state: &mut GameState, conn_id: ConnectionId, data: &str) {
    // Log for debugging — only unhandled packets and slash commands
    // (movement/attack/click packets are too frequent to log)

    // Match opcode prefix — order matters for single-char opcodes
    if data.starts_with(client_opcodes::KERD22) {
        let payload = strip_opcode(data, 6);
        let hd_serial = read_field(1, payload, ',');
        auth::handle_hardware_check(state, conn_id, &hd_serial).await;
    } else if data.starts_with(client_opcodes::ALOGIN) {
        let payload = strip_opcode(data, 6);
        let account_name = read_field(1, payload, ',');
        let password = read_field(2, payload, ',');
        auth::handle_account_login(state, conn_id, &account_name, &password).await;
    } else if data.starts_with(client_opcodes::NACCNT) {
        let payload = strip_opcode(data, 6);
        let account_name = read_field(1, payload, ',');
        let password = read_field(2, payload, ',');
        let pin = read_field(3, payload, ',');
        auth::handle_create_account(state, conn_id, &account_name, &password, &pin).await;
    } else if data.starts_with(client_opcodes::THCJXD) {
        let payload = strip_opcode(data, 6);
        let char_name = read_field(1, payload, ',');
        let account = read_field(2, payload, ',');
        let codex = read_field(3, payload, ',');
        auth::handle_character_select(state, conn_id, &char_name, &account, &codex).await;
    } else if data.starts_with(client_opcodes::OOLOGI) {
        let payload = strip_opcode(data, 6);
        let char_name = read_field(1, payload, ',');
        let account = read_field(2, payload, ',');
        let codex = read_field(3, payload, ',');
        auth::handle_character_login(state, conn_id, &char_name, &account, &codex).await;
    } else if data.starts_with(client_opcodes::NLOGIN) {
        let payload = strip_opcode(data, 6);
        let char_name = read_field(1, payload, ',');
        let race = read_field(2, payload, ',');
        let gender_str = read_field(4, payload, ',');
        let gender: i32 = if gender_str.to_lowercase().contains("ombre") { 1 } else { 2 };
        let class = read_field(5, payload, ',');
        let hogar: i32 = read_field(6, payload, ',').parse().unwrap_or(1);
        let account = read_field(7, payload, ',');
        let head: i32 = read_field(8, payload, ',').parse().unwrap_or(0);
        auth::handle_create_character(state, conn_id, &char_name, &race, gender, &class, hogar, &account, head).await;
    } else if data.starts_with(client_opcodes::TIRDAD) {
        auth::handle_roll_dice(state, conn_id).await;
    } else if data.starts_with(client_opcodes::TBRP) {
        let payload = strip_opcode(data, 4);
        let char_name = read_field(1, payload, ',');
        let account_name = read_field(2, payload, ',').to_uppercase();
        let password = read_field(3, payload, ',');
        auth::handle_delete_character(state, conn_id, &char_name, &account_name, &password).await;
    } else if data.starts_with(client_opcodes::REPASS) {
        let payload = strip_opcode(data, 6);
        let account_name = read_field(1, payload, ',');
        let old_password = read_field(2, payload, ',');
        let new_password = read_field(3, payload, ',');
        let confirm_password = read_field(4, payload, ',');
        auth::handle_change_password(state, conn_id, &account_name, &old_password, &new_password, &confirm_password).await;
    } else if data.starts_with(client_opcodes::REECUH) {
        let payload = strip_opcode(data, 6);
        let account_name = read_field(1, payload, ',');
        let pin = read_field(2, payload, ',');
        auth::handle_account_recovery(state, conn_id, &account_name, &pin).await;
    } else if data.starts_with(client_opcodes::COMMERCE_CLOSE) {
        info!("[DISPATCH] #{} FINCOM", conn_id);
        handle_commerce_close(state, conn_id).await;
    } else if data.starts_with(client_opcodes::COMMERCE_BUY) {
        info!("[DISPATCH] #{} COMP raw='{}'", conn_id, data);
        let payload = strip_opcode(data, 5); // "COMP," = 5
        let slot: usize = match read_field(1, payload, ',').parse() { Ok(v) if v >= 1 => v, _ => return };
        let amount: i32 = match read_field(2, payload, ',').parse() { Ok(v) if v >= 1 => v, _ => return };
        handle_commerce_buy(state, conn_id, slot, amount).await;
    } else if data.starts_with(client_opcodes::COMMERCE_SELL) {
        info!("[DISPATCH] #{} VEND raw='{}'", conn_id, data);
        let payload = strip_opcode(data, 5); // "VEND," = 5
        let slot: usize = match read_field(1, payload, ',').parse() { Ok(v) if v >= 1 => v, _ => return };
        let amount: i32 = match read_field(2, payload, ',').parse() { Ok(v) if v >= 1 => v, _ => return };
        handle_commerce_sell(state, conn_id, slot, amount).await;
    } else if data.starts_with(client_opcodes::TRADE_CANCEL) {
        handle_trade_cancel(state, conn_id).await;
    } else if data.starts_with(client_opcodes::TRADE_RESPONSE) {
        let response: i32 = strip_opcode(data, 3).trim().parse().unwrap_or(0);
        handle_trade_response(state, conn_id, response).await;
    } else if data.starts_with(client_opcodes::TRADE_OFFER_GOLD) {
        let gold: i64 = strip_opcode(data, 3).trim().parse().unwrap_or(0);
        handle_trade_offer_gold(state, conn_id, gold).await;
    } else if data.starts_with(client_opcodes::TRADE_OFFER_ITEM) {
        let payload = strip_opcode(data, 3);
        let slot: usize = match read_field(1, payload, ',').parse() { Ok(v) if v >= 1 => v, _ => return };
        let amount: i32 = match read_field(2, payload, ',').parse() { Ok(v) if v >= 1 => v, _ => return };
        handle_trade_offer_item(state, conn_id, slot, amount).await;
    } else if data.starts_with(client_opcodes::TRADE_CHAT) {
        let msg = strip_opcode(data, 3);
        handle_trade_chat(state, conn_id, msg).await;
    } else if data.starts_with(client_opcodes::BANK_CLOSE) {
        handle_bank_close(state, conn_id).await;
    } else if data.starts_with(client_opcodes::BANK_DEPOSIT) {
        let payload = strip_opcode(data, 5); // "DEPO," = 5
        let slot: usize = match read_field(1, payload, ',').parse() { Ok(v) if v >= 1 => v, _ => return };
        let amount: i32 = match read_field(2, payload, ',').parse() { Ok(v) if v >= 1 => v, _ => return };
        handle_bank_deposit(state, conn_id, slot, amount).await;
    } else if data.starts_with(client_opcodes::BANK_WITHDRAW) {
        let payload = strip_opcode(data, 5); // "RETI," = 5
        let slot: usize = match read_field(1, payload, ',').parse() { Ok(v) if v >= 1 => v, _ => return };
        let amount: i32 = match read_field(2, payload, ',').parse() { Ok(v) if v >= 1 => v, _ => return };
        handle_bank_withdraw(state, conn_id, slot, amount).await;
    } else if data.starts_with(client_opcodes::WORK_LEFT_CLICK) {
        let payload = strip_opcode(data, 3); // "WLC" = 3
        let target_x: i32 = match read_field(1, payload, ',').parse() { Ok(v) => v, _ => return };
        let target_y: i32 = match read_field(2, payload, ',').parse() { Ok(v) => v, _ => return };
        let skill_type: i32 = match read_field(3, payload, ',').parse() { Ok(v) => v, _ => return };
        handle_work_left_click(state, conn_id, target_x, target_y, skill_type).await;
    } else if data.starts_with(client_opcodes::GUILD_INFO) {
        handle_guild_info(state, conn_id).await;
    } else if data.starts_with(client_opcodes::GUILD_CREATE) {
        handle_guild_create(state, conn_id, strip_opcode(data, 3)).await;
    } else if data.starts_with(client_opcodes::GUILD_UPDATE_CODEX) {
        handle_guild_update_codex(state, conn_id, strip_opcode(data, 6)).await;
    } else if data.starts_with(client_opcodes::GUILD_ACCEPT) {
        handle_guild_accept(state, conn_id, strip_opcode(data, 8)).await;
    } else if data.starts_with(client_opcodes::GUILD_REJECT) {
        handle_guild_reject(state, conn_id, strip_opcode(data, 8)).await;
    } else if data.starts_with(client_opcodes::GUILD_EXPEL) {
        handle_guild_expel(state, conn_id, strip_opcode(data, 8)).await;
    } else if data.starts_with(client_opcodes::GUILD_NEWS) {
        handle_guild_news(state, conn_id, strip_opcode(data, 8)).await;
    } else if data.starts_with(client_opcodes::GUILD_APPLY) {
        handle_guild_apply(state, conn_id, strip_opcode(data, 9)).await;
    } else if data.starts_with(client_opcodes::GUILD_DETAILS) {
        handle_guild_details(state, conn_id, strip_opcode(data, 11)).await;
    } else if data.starts_with(client_opcodes::CONSTRUCT_SMITH) {
        let item_index: i32 = strip_opcode(data, 3).trim().parse().unwrap_or(0);
        handle_construct_smith(state, conn_id, item_index).await;
    } else if data.starts_with(client_opcodes::CONSTRUCT_CARP) {
        let item_index: i32 = strip_opcode(data, 3).trim().parse().unwrap_or(0);
        handle_construct_carp(state, conn_id, item_index).await;
    } else if data.starts_with(client_opcodes::EQUIP_ITEM) {
        let slot: usize = strip_opcode(data, 4).trim().parse().unwrap_or(0);
        handle_equip(state, conn_id, slot).await;
    } else if data.starts_with(client_opcodes::POSITION_UPDATE) {
        handle_actualizar(state, conn_id).await;
    } else if data.starts_with(client_opcodes::MACRO_DETECT) {
        handle_tengomacros(state, conn_id).await;
    } else if data.starts_with(client_opcodes::HOUSE_QUERY) {
        handle_fwo(state, conn_id, strip_opcode(data, 3)).await;
    } else if data.starts_with(client_opcodes::HOUSE_BUY) {
        handle_cuc(state, conn_id, strip_opcode(data, 3)).await;
    } else if data.starts_with(client_opcodes::PET_RENAME) {
        handle_cnm(state, conn_id, strip_opcode(data, 3)).await;
    } else if data.starts_with(client_opcodes::CAST_BY_NAME) {
        let target_name = strip_opcode(data, 6).trim();
        handle_downsi(state, conn_id, target_name).await;
    } else if data.starts_with(client_opcodes::MOVE_SPELL) {
        let payload = strip_opcode(data, 6);
        let direction: i32 = read_field(1, payload, ',').parse().unwrap_or(0);
        let slot: usize = read_field(2, payload, ',').parse().unwrap_or(0);
        handle_desphe(state, conn_id, direction, slot).await;
    } else if data.starts_with(client_opcodes::PLAYER_INFO) {
        let target_name = strip_opcode(data, 6).trim();
        handle_daminf(state, conn_id, target_name).await;
    } else if data.starts_with(client_opcodes::MINI_STATS) {
        handle_fest(state, conn_id).await;
    } else if data.starts_with(client_opcodes::HEAD_CHANGE) {
        let new_head: i32 = strip_opcode(data, 6).trim().parse().unwrap_or(0);
        handle_cabezi(state, conn_id, new_head).await;
    } else if data.starts_with(client_opcodes::DRAG_DROP) {
        let payload = strip_opcode(data, 6);
        let slot: usize = read_field(1, payload, ',').parse().unwrap_or(0);
        let amount: i32 = read_field(2, payload, ',').parse().unwrap_or(0);
        handle_dydtra(state, conn_id, slot, amount).await;
    } else if data.starts_with(client_opcodes::VOTE) {
        let option: usize = strip_opcode(data, 4).trim().parse().unwrap_or(0);
        handle_nvot(state, conn_id, option).await;
    } else if data.starts_with(client_opcodes::REPORT) {
        let payload = strip_opcode(data, 4);
        let target_name = read_field(1, payload, ',');
        let reason = read_field(2, payload, ',');
        handle_newd(state, conn_id, &target_name, &reason).await;
    } else if data.starts_with(client_opcodes::SOS_VIEW) {
        handle_consul(state, conn_id).await;
    } else if data.starts_with(client_opcodes::SWAP_ITEMS) {
        let payload = strip_opcode(data, 4);
        let slot1: usize = read_field(1, payload, ',').parse().unwrap_or(0);
        let slot2: usize = read_field(2, payload, ',').parse().unwrap_or(0);
        handle_swap(state, conn_id, slot1, slot2).await;
    } else if data.starts_with(client_opcodes::SKILL_SET) {
        let payload = strip_opcode(data, 4);
        let mut increments = [0i32; 22];
        for i in 0..22 {
            increments[i] = read_field(i + 1, payload, ',').parse().unwrap_or(0);
        }
        handle_skse(state, conn_id, &increments).await;
    } else if data.starts_with(client_opcodes::SPELL_INFO) {
        let slot: usize = strip_opcode(data, 4).trim().parse().unwrap_or(0);
        handle_infs(state, conn_id, slot).await;
    } else if data.starts_with(client_opcodes::LEVEL_BONUS) {
        let selection: i32 = strip_opcode(data, 3).trim().parse().unwrap_or(0);
        handle_bof(state, conn_id, selection).await;
    } else if data.starts_with(client_opcodes::TRAIN_CREATURE) {
        let creature_slot: i32 = strip_opcode(data, 4).trim().parse().unwrap_or(0);
        handle_entr(state, conn_id, creature_slot).await;
    } else if data.starts_with(client_opcodes::USE_SKILL) {
        let skill_num: i32 = strip_opcode(data, 2).trim().parse().unwrap_or(0);
        handle_uk(state, conn_id, skill_num).await;
    } else if data.starts_with(client_opcodes::SAFE_TOGGLE) {
        handle_safe_toggle(state, conn_id).await;
    } else if data.starts_with(client_opcodes::USE_ITEM_CLICK) {
        let payload = strip_opcode(data, 3);
        let slot: usize = read_field(1, payload, ',').parse().unwrap_or(0);
        handle_use_item_click(state, conn_id, slot).await;
    } else if data.starts_with(client_opcodes::USE_ITEM) {
        let slot: usize = strip_opcode(data, 3).trim().parse().unwrap_or(0);
        handle_use_item(state, conn_id, slot).await;
    } else if data.starts_with(client_opcodes::ATTACK) {
        handle_attack(state, conn_id).await;
    } else if data.starts_with(client_opcodes::PICK_UP) {
        handle_pick_up(state, conn_id).await;
    } else if data.starts_with(client_opcodes::DROP_ITEM) {
        let payload = strip_opcode(data, 2);
        let slot_raw: i32 = read_field(1, payload, ',').parse().unwrap_or(0);
        let amount: i32 = read_field(2, payload, ',').parse().unwrap_or(1);
        if slot_raw == -1 {
            handle_drop_gold(state, conn_id, amount).await;
        } else {
            handle_drop_item(state, conn_id, slot_raw as usize, amount).await;
        }
    } else if data.starts_with(client_opcodes::CAST_SPELL) {
        let spell_slot: usize = strip_opcode(data, 2).parse().unwrap_or(0);
        handle_cast_spell(state, conn_id, spell_slot).await;
    } else if data.starts_with(client_opcodes::LEFT_CLICK) {
        let payload = strip_opcode(data, 2);
        let x: i32 = match read_field(1, payload, ',').parse() { Ok(v) => v, _ => return };
        let y: i32 = match read_field(2, payload, ',').parse() { Ok(v) => v, _ => return };
        handle_left_click(state, conn_id, x, y).await;
    } else if data.starts_with(client_opcodes::RIGHT_CLICK) {
        let payload = strip_opcode(data, 2);
        let x: i32 = match read_field(1, payload, ',').parse() { Ok(v) => v, _ => return };
        let y: i32 = match read_field(2, payload, ',').parse() { Ok(v) => v, _ => return };
        handle_right_click(state, conn_id, x, y).await;
    } else if data.starts_with(client_opcodes::MEDITATE) {
        handle_meditate(state, conn_id).await;
    } else if data.starts_with(client_opcodes::CHANGE_HEADING) {
        let heading: i32 = strip_opcode(data, 4).parse().unwrap_or(0);
        movement::handle_change_heading(state, conn_id, heading).await;
    } else if data.starts_with(client_opcodes::REQUEST_POS) {
        movement::handle_request_pos(state, conn_id).await;
    } else if data.starts_with(client_opcodes::WALK) {
        let heading: i32 = strip_opcode(data, 1).parse().unwrap_or(0);
        movement::handle_walk(state, conn_id, heading).await;
    } else if data.starts_with(client_opcodes::MOUSE_DROP) {
        let payload = strip_opcode(data, 2);
        let slot: usize = read_field(1, payload, ',').parse().unwrap_or(0);
        let amount: i32 = read_field(2, payload, ',').parse().unwrap_or(1);
        handle_mouse_drop(state, conn_id, slot, amount).await;
    } else if data.starts_with(client_opcodes::YELL) {
        let message = strip_opcode(data, 1);
        social::handle_yell(state, conn_id, message).await;
    } else if data.starts_with(client_opcodes::SOS_SEND) {
        let payload = strip_opcode(data, 1);
        let contenido = read_field(2, payload, '|');
        handle_sos_send(state, conn_id, &contenido).await;
    } else if data.starts_with(client_opcodes::TALK) {
        let message = strip_opcode(data, 1);
        social::handle_talk(state, conn_id, message).await;
    } else if data.starts_with(client_opcodes::WHISPER) {
        let payload = strip_opcode(data, 1);
        let target_name = read_field(1, payload, '@');
        let message = read_field(2, payload, '@');
        social::handle_whisper(state, conn_id, &target_name, &message).await;
    } else if data.starts_with(client_opcodes::SOS_RESPOND) {
        let payload = strip_opcode(data, 1);
        let target_name = read_field(1, payload, '*');
        let texto = read_field(2, payload, '*');
        handle_sos_respond(state, conn_id, &target_name, &texto).await;
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


// ─── SHARED HELPERS ─────────────────────────────────────────────────────────

// Slash commands — extracted to slash_commands.rs for readability.
include!("slash_commands.rs");
// Tests are in a separate file to keep mod.rs focused on dispatching.
include!("tests.rs");
