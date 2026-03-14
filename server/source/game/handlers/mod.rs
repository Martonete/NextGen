mod common;
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
    make_user_visible, check_update_needed_user, warp_user, warp_user_inner,
    warp_user_exact, warp_mascotas, mover_casper, send_warp_fx,
};
pub(crate) use leveling::check_user_level;
pub(crate) use auth::connect_user;
// Re-export quest/party functions called from other modules
pub use quests_party::party_share_exp;
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

use std::collections::HashMap;
use tracing::{info, warn};

use crate::net::ConnectionId;
use crate::protocol::{client_opcodes, font_index, fields::read_field};
use crate::protocol::byte_queue::ByteQueue;
use crate::protocol::packets::ClientPacketID;
use crate::protocol::binary_packets;
use crate::db::{accounts, charfile, guilds};
use crate::db::password;
use crate::data::objects::ObjType;
use crate::data::maps::Trigger;
use super::class_race::{PlayerClass, PlayerRace};
use super::types::{GameState, UserState, SendTarget, InventorySlot, EquipSlots, PartyState, CleanWorldEntry, privilege_level, MAX_INVENTORY_SLOTS, MAX_NORMAL_INVENTORY_SLOTS, MAX_SPELL_SLOTS, MAX_PARTY_MEMBERS, MAX_PARTIES};
use super::world;
use super::npc;
use crate::data::npcs::NpcType;

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

    // Bridge: read binary fields, reconstruct text for existing handlers.
    match packet_id {
        // Pre-login
        ClientPacketID::HardwareCheck => {
            let hd = bq.read_ascii_string().unwrap_or_default();
            let text = format!("KERD22{}", hd);
            handle_packet(state, conn_id, &text).await;
        }
        ClientPacketID::AccountLogin => {
            let account = bq.read_ascii_string().unwrap_or_default();
            let password = bq.read_ascii_string().unwrap_or_default();
            let text = format!("ALOGIN{},{}", account, password);
            handle_packet(state, conn_id, &text).await;
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
            let gender_str = if gender == 1 { "Hombre" } else { "Mujer" };
            let text = format!("NLOGIN{},{},0,{},{},{},{},{}", char_name, race_name, gender_str, class_name, homeland, account, head);
            handle_packet(state, conn_id, &text).await;
        }
        ClientPacketID::CharacterLogin => {
            let char_name = bq.read_ascii_string().unwrap_or_default();
            let account = bq.read_ascii_string().unwrap_or_default();
            let codex = bq.read_ascii_string().unwrap_or_default();
            let text = format!("OOLOGI{},{},{}", char_name, account, codex);
            handle_packet(state, conn_id, &text).await;
        }
        ClientPacketID::CharacterSelect => {
            let char_name = bq.read_ascii_string().unwrap_or_default();
            let account = bq.read_ascii_string().unwrap_or_default();
            let codex = bq.read_ascii_string().unwrap_or_default();
            let text = format!("THCJXD{},{},{}", char_name, account, codex);
            handle_packet(state, conn_id, &text).await;
        }
        ClientPacketID::CreateAccount => {
            let account = bq.read_ascii_string().unwrap_or_default();
            let password = bq.read_ascii_string().unwrap_or_default();
            let pin = bq.read_ascii_string().unwrap_or_default();
            let text = format!("NACCNT{},{},{}", account, password, pin);
            handle_packet(state, conn_id, &text).await;
        }
        ClientPacketID::ChangePassword => {
            let old_pass = bq.read_ascii_string().unwrap_or_default();
            let new_pass = bq.read_ascii_string().unwrap_or_default();
            // Handler expects: REPASS<account>,<old>,<new>,<confirm>
            let account = state.users.get(&conn_id)
                .map(|u| u.account_name.clone())
                .unwrap_or_default();
            let text = format!("REPASS{},{},{},{}", account, old_pass, new_pass, new_pass);
            handle_packet(state, conn_id, &text).await;
        }
        ClientPacketID::AccountRecovery => {
            let account = bq.read_ascii_string().unwrap_or_default();
            let pin = bq.read_ascii_string().unwrap_or_default();
            let text = format!("REECUH{},{}", account, pin);
            handle_packet(state, conn_id, &text).await;
        }
        ClientPacketID::RollDice => {
            handle_packet(state, conn_id, "TIRDAD").await;
        }
        ClientPacketID::DeleteCharacter => {
            let char_name = bq.read_ascii_string().unwrap_or_default();
            // Handler expects: TBRP<name>,<account>,<password(codex)>
            // Binary client only sends name. Account is from connection state.
            // For security verification, load the charfile password (codex) server-side
            // since the client is already authenticated via AccountLogin.
            let account = state.users.get(&conn_id)
                .map(|u| u.account_name.clone())
                .unwrap_or_default();
            let codex = charfile::load_charfile(&state.pool, &char_name).await
                .map(|c| c.password).unwrap_or_default();
            let text = format!("TBRP{},{},{}", char_name, account, codex);
            handle_packet(state, conn_id, &text).await;
        }

        // Movement
        ClientPacketID::Walk => {
            let heading = bq.read_byte().unwrap_or(1);
            let text = format!("M{}", heading);
            handle_packet(state, conn_id, &text).await;
        }
        ClientPacketID::ChangeHeading => {
            let heading = bq.read_byte().unwrap_or(1);
            let text = format!("CHEA{}", heading);
            handle_packet(state, conn_id, &text).await;
        }
        ClientPacketID::RequestPos => {
            handle_packet(state, conn_id, "RPU").await;
        }
        ClientPacketID::SyncPosition => {
            handle_packet(state, conn_id, "ACTUALIZAR").await;
        }

        // Combat
        ClientPacketID::Attack => {
            handle_packet(state, conn_id, "AT").await;
        }
        ClientPacketID::CastSpell => {
            let slot = bq.read_byte().unwrap_or(0);
            let text = format!("LH{}", slot);
            handle_packet(state, conn_id, &text).await;
        }
        ClientPacketID::LeftClick => {
            let x = bq.read_byte().unwrap_or(0);
            let y = bq.read_byte().unwrap_or(0);
            let text = format!("LC{},{}", x, y);
            handle_packet(state, conn_id, &text).await;
        }
        ClientPacketID::RightClick => {
            let x = bq.read_byte().unwrap_or(0);
            let y = bq.read_byte().unwrap_or(0);
            let text = format!("RC{},{}", x, y);
            handle_packet(state, conn_id, &text).await;
        }
        ClientPacketID::WorkLeftClick => {
            let x = bq.read_byte().unwrap_or(0);
            let y = bq.read_byte().unwrap_or(0);
            let skill = bq.read_byte().unwrap_or(0);
            let text = format!("WLC{},{},{}", x, y, skill);
            handle_packet(state, conn_id, &text).await;
        }

        // Chat
        ClientPacketID::Talk => {
            let msg = bq.read_ascii_string().unwrap_or_default();
            let text = format!(";{}", msg);
            handle_packet(state, conn_id, &text).await;
        }
        ClientPacketID::Yell => {
            let msg = bq.read_ascii_string().unwrap_or_default();
            let text = format!("-{}", msg);
            handle_packet(state, conn_id, &text).await;
        }
        ClientPacketID::Whisper => {
            let target = bq.read_ascii_string().unwrap_or_default();
            let msg = bq.read_ascii_string().unwrap_or_default();
            let text = format!("\\{}@{}", target, msg);
            handle_packet(state, conn_id, &text).await;
        }
        ClientPacketID::SlashCommand => {
            let cmd = bq.read_ascii_string().unwrap_or_default();
            // Slash commands go through the talk handler as ";/<cmd>"
            let text = format!(";{}", cmd);
            handle_packet(state, conn_id, &text).await;
        }

        // Items
        ClientPacketID::PickUp => {
            handle_packet(state, conn_id, "AG").await;
        }
        ClientPacketID::DropItem => {
            let slot = bq.read_byte().unwrap_or(0);
            let amount = bq.read_integer().unwrap_or(0);
            let text = format!("TI{},{}", slot, amount);
            handle_packet(state, conn_id, &text).await;
        }
        ClientPacketID::UseItem => {
            let slot = bq.read_byte().unwrap_or(0);
            let text = format!("USA{}", slot);
            handle_packet(state, conn_id, &text).await;
        }
        ClientPacketID::UseItemClick => {
            let slot = bq.read_byte().unwrap_or(0);
            let text = format!("QSA{}", slot);
            handle_packet(state, conn_id, &text).await;
        }
        ClientPacketID::EquipItem => {
            let slot = bq.read_byte().unwrap_or(0);
            let text = format!("EQUI{}", slot);
            handle_packet(state, conn_id, &text).await;
        }
        ClientPacketID::SwapItems => {
            let from = bq.read_byte().unwrap_or(0);
            let to = bq.read_byte().unwrap_or(0);
            let text = format!("SWAP{},{}", from, to);
            handle_packet(state, conn_id, &text).await;
        }
        ClientPacketID::MouseDrop => {
            let slot = bq.read_byte().unwrap_or(0);
            let amount = bq.read_integer().unwrap_or(0);
            let text = format!("TR{},{}", slot, amount);
            handle_packet(state, conn_id, &text).await;
        }

        // Skills
        ClientPacketID::UseSkill => {
            let skill_id = bq.read_byte().unwrap_or(0);
            let text = format!("UK{}", skill_id);
            handle_packet(state, conn_id, &text).await;
        }
        ClientPacketID::SkillSet => {
            // SKSE sends comma-separated skill points
            let mut parts = Vec::new();
            for _ in 0..20 {
                parts.push(bq.read_byte().unwrap_or(0).to_string());
            }
            let text = format!("SKSE{}", parts.join(","));
            handle_packet(state, conn_id, &text).await;
        }
        ClientPacketID::Meditate => {
            handle_packet(state, conn_id, "ME").await;
        }
        ClientPacketID::SafeToggle => {
            handle_packet(state, conn_id, "SEG").await;
        }
        ClientPacketID::MacroDetect => {
            handle_packet(state, conn_id, "TENGOMACROS").await;
        }
        ClientPacketID::LevelBonus => {
            let bonus = bq.read_byte().unwrap_or(0);
            let text = format!("BOF{}", bonus);
            handle_packet(state, conn_id, &text).await;
        }

        // Spells
        ClientPacketID::SpellInfo => {
            let slot = bq.read_byte().unwrap_or(0);
            let text = format!("INFS{}", slot);
            handle_packet(state, conn_id, &text).await;
        }
        ClientPacketID::MoveSpell => {
            let from = bq.read_byte().unwrap_or(0);
            let to = bq.read_byte().unwrap_or(0);
            let text = format!("DESPHE{},{}", from, to);
            handle_packet(state, conn_id, &text).await;
        }
        ClientPacketID::CastByName => {
            let target = bq.read_ascii_string().unwrap_or_default();
            let text = format!("DOWNSI{}", target);
            handle_packet(state, conn_id, &text).await;
        }

        // Commerce
        ClientPacketID::CommerceBuy => {
            let slot = bq.read_byte().unwrap_or(0);
            let amount = bq.read_integer().unwrap_or(0);
            let text = format!("COMP,{},{}", slot, amount);
            handle_packet(state, conn_id, &text).await;
        }
        ClientPacketID::CommerceSell => {
            let slot = bq.read_byte().unwrap_or(0);
            let amount = bq.read_integer().unwrap_or(0);
            let text = format!("VEND,{},{}", slot, amount);
            handle_packet(state, conn_id, &text).await;
        }
        ClientPacketID::CommerceClose => {
            handle_packet(state, conn_id, "FINCOM").await;
        }
        ClientPacketID::TradeOfferGold => {
            let amount = bq.read_long().unwrap_or(0);
            let text = format!("UOR{}", amount);
            handle_packet(state, conn_id, &text).await;
        }
        ClientPacketID::TradeOfferItem => {
            let slot = bq.read_byte().unwrap_or(0);
            let amount = bq.read_integer().unwrap_or(0);
            let text = format!("UOC{},{}", slot, amount);
            handle_packet(state, conn_id, &text).await;
        }
        ClientPacketID::TradeResponse => {
            let response = bq.read_byte().unwrap_or(0);
            let text = format!("TDR{}", response);
            handle_packet(state, conn_id, &text).await;
        }
        ClientPacketID::TradeCancel => {
            handle_packet(state, conn_id, "TCM").await;
        }
        ClientPacketID::TradeChat => {
            let msg = bq.read_ascii_string().unwrap_or_default();
            let text = format!("VHC{}", msg);
            handle_packet(state, conn_id, &text).await;
        }

        // Banking
        ClientPacketID::BankDeposit => {
            let slot = bq.read_byte().unwrap_or(0);
            let amount = bq.read_integer().unwrap_or(0);
            let text = format!("DEPO,{},{}", slot, amount);
            handle_packet(state, conn_id, &text).await;
        }
        ClientPacketID::BankWithdraw => {
            let slot = bq.read_byte().unwrap_or(0);
            let amount = bq.read_integer().unwrap_or(0);
            let text = format!("RETI,{},{}", slot, amount);
            handle_packet(state, conn_id, &text).await;
        }
        ClientPacketID::BankClose => {
            handle_packet(state, conn_id, "FINBAN").await;
        }

        // Crafting
        ClientPacketID::ConstructSmith => {
            let item = bq.read_integer().unwrap_or(0);
            let text = format!("CNS{}", item);
            handle_packet(state, conn_id, &text).await;
        }
        ClientPacketID::ConstructCarp => {
            let item = bq.read_integer().unwrap_or(0);
            let text = format!("CNC{}", item);
            handle_packet(state, conn_id, &text).await;
        }
        ClientPacketID::TrainCreature => {
            let pet = bq.read_byte().unwrap_or(0);
            let text = format!("ENTR{}", pet);
            handle_packet(state, conn_id, &text).await;
        }

        // Guild
        ClientPacketID::GuildInfo => {
            handle_packet(state, conn_id, "GLINFO").await;
        }
        ClientPacketID::GuildCreate => {
            let data_str = bq.read_ascii_string().unwrap_or_default();
            let text = format!("CIG{}", data_str);
            handle_packet(state, conn_id, &text).await;
        }
        ClientPacketID::GuildUpdateCodex => {
            let data_str = bq.read_ascii_string().unwrap_or_default();
            let text = format!("DESCOD{}", data_str);
            handle_packet(state, conn_id, &text).await;
        }
        ClientPacketID::GuildAccept => {
            let name = bq.read_ascii_string().unwrap_or_default();
            let text = format!("ACEPTARI{}", name);
            handle_packet(state, conn_id, &text).await;
        }
        ClientPacketID::GuildReject => {
            let name = bq.read_ascii_string().unwrap_or_default();
            let text = format!("RECHAZAR{}", name);
            handle_packet(state, conn_id, &text).await;
        }
        ClientPacketID::GuildExpel => {
            let name = bq.read_ascii_string().unwrap_or_default();
            let text = format!("ECHARCLA{}", name);
            handle_packet(state, conn_id, &text).await;
        }
        ClientPacketID::GuildNews => {
            let data_str = bq.read_ascii_string().unwrap_or_default();
            let text = format!("ACTGNEWS{}", data_str);
            handle_packet(state, conn_id, &text).await;
        }
        ClientPacketID::GuildApply => {
            let data_str = bq.read_ascii_string().unwrap_or_default();
            let text = format!("SOLICITUD{}", data_str);
            handle_packet(state, conn_id, &text).await;
        }
        ClientPacketID::GuildDetails => {
            let name = bq.read_ascii_string().unwrap_or_default();
            let text = format!("CLANDETAILS{}", name);
            handle_packet(state, conn_id, &text).await;
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
            let text = format!("DAMINF{}", data_str);
            handle_packet(state, conn_id, &text).await;
        }
        ClientPacketID::MiniStats => {
            handle_packet(state, conn_id, "FEST").await;
        }
        ClientPacketID::HeadChange => {
            let data_str = bq.read_ascii_string().unwrap_or_default();
            let text = format!("CABEZI{}", data_str);
            handle_packet(state, conn_id, &text).await;
        }
        ClientPacketID::Rankings => {
            let data_str = bq.read_ascii_string().unwrap_or_default();
            let text = format!("RANKIN{}", data_str);
            handle_packet(state, conn_id, &text).await;
        }

        // Misc — all use the generic string bridge
        ClientPacketID::HouseQuery => { bridge_string(bq, state, conn_id, "FWO").await; }
        ClientPacketID::HouseBuy => { bridge_string(bq, state, conn_id, "CUC").await; }
        ClientPacketID::PetRename => { bridge_string(bq, state, conn_id, "CNM").await; }
        ClientPacketID::DragDrop => { bridge_string(bq, state, conn_id, "DYDTRA").await; }
        ClientPacketID::Vote => { bridge_string(bq, state, conn_id, "NVOT").await; }
        ClientPacketID::Report => { bridge_string(bq, state, conn_id, "NEWD").await; }
        ClientPacketID::SosView => { handle_packet(state, conn_id, "CONSUL").await; }
        ClientPacketID::SosSend => { bridge_string(bq, state, conn_id, "#").await; }
        ClientPacketID::SosRespond => { bridge_string(bq, state, conn_id, "X").await; }
    }

    PacketResult::Ok
}

/// Bridge helper: reads a string from ByteQueue and forwards to text handler.
async fn bridge_string(bq: &mut ByteQueue, state: &mut GameState, conn_id: ConnectionId, opcode: &str) {
    let data_str = bq.read_ascii_string().unwrap_or_default();
    let text = format!("{}{}", opcode, data_str);
    handle_packet(state, conn_id, &text).await;
}

/// Route a decrypted packet to the appropriate handler.
pub async fn handle_packet(state: &mut GameState, conn_id: ConnectionId, data: &str) {
    // Log for debugging — only unhandled packets and slash commands
    // (movement/attack/click packets are too frequent to log)

    // Match opcode prefix — order matters for single-char opcodes
    if data.starts_with(client_opcodes::KERD22) {
        auth::handle_kerd22(state, conn_id, data).await;
    } else if data.starts_with(client_opcodes::ALOGIN) {
        auth::handle_alogin(state, conn_id, data).await;
    } else if data.starts_with(client_opcodes::NACCNT) {
        auth::handle_naccnt(state, conn_id, data).await;
    } else if data.starts_with(client_opcodes::THCJXD) {
        auth::handle_thcjxd(state, conn_id, data).await;
    } else if data.starts_with(client_opcodes::OOLOGI) {
        auth::handle_oologi(state, conn_id, data).await;
    } else if data.starts_with(client_opcodes::NLOGIN) {
        auth::handle_nlogin(state, conn_id, data).await;
    } else if data.starts_with(client_opcodes::TIRDAD) {
        auth::handle_tirdad(state, conn_id).await;
    } else if data.starts_with(client_opcodes::TBRP) {
        auth::handle_tbrp(state, conn_id, data).await;
    } else if data.starts_with(client_opcodes::REPASS) {
        auth::handle_repass(state, conn_id, data).await;
    } else if data.starts_with(client_opcodes::REECUH) {
        auth::handle_reecuh(state, conn_id, data).await;
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
    } else if data.starts_with(client_opcodes::CAST_BY_NAME) {
        handle_downsi(state, conn_id, data).await;
    } else if data.starts_with(client_opcodes::MOVE_SPELL) {
        handle_desphe(state, conn_id, data).await;
    } else if data.starts_with(client_opcodes::PLAYER_INFO) {
        handle_daminf(state, conn_id, data).await;
    } else if data.starts_with(client_opcodes::MINI_STATS) {
        handle_fest(state, conn_id).await;
    } else if data.starts_with(client_opcodes::HEAD_CHANGE) {
        handle_cabezi(state, conn_id, data).await;
    } else if data.starts_with(client_opcodes::DRAG_DROP) {
        handle_dydtra(state, conn_id, data).await;
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
        movement::handle_change_heading(state, conn_id, data).await;
    } else if data.starts_with(client_opcodes::REQUEST_POS) {
        movement::handle_request_pos(state, conn_id).await;
    } else if data.starts_with(client_opcodes::WALK) {
        movement::handle_walk(state, conn_id, data).await;
    } else if data.starts_with(client_opcodes::MOUSE_DROP) {
        handle_mouse_drop(state, conn_id, data).await;
    } else if data.starts_with(client_opcodes::YELL) {
        social::handle_yell(state, conn_id, data).await;
    } else if data.starts_with(client_opcodes::SOS_SEND) {
        handle_sos_send(state, conn_id, data).await;
    } else if data.starts_with(client_opcodes::TALK) {
        social::handle_talk(state, conn_id, data).await;
    } else if data.starts_with(client_opcodes::WHISPER) {
        social::handle_whisper(state, conn_id, data).await;
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


// Slash commands — extracted to slash_commands.rs for readability.
include!("slash_commands.rs");
// Tests are in a separate file to keep mod.rs focused on dispatching.
include!("tests.rs");
