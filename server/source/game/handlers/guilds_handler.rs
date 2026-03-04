//! Guild system handlers: guild management, clan bank, codex, member management.
//! Extracted from mod.rs to reduce file size.

use tracing::info;
use crate::net::ConnectionId;
use crate::game::types::{GameState, SendTarget, InventorySlot};
use crate::protocol::{font_index, fields::read_field, binary_packets};
use crate::db::guilds;
use crate::data::objects::ObjData;
use super::common::*;
use super::{send_inventory_slot, send_full_inventory};

// =====================================================================
// Guild system handlers (modGuilds.bas / clsClan.cls)
// =====================================================================

/// BF delimiter (char 191 = inverted question mark) used in guild packets
const BF: char = '\u{00BF}';

/// GLINFO — Request guild info panel.
/// If user has no guild: send guild list (GL).
/// If user is leader/sublider: send IREDAEL (leader view).
/// If user is regular member: send IREDAEK (member view).
pub(super) async fn handle_guild_info(state: &mut GameState, conn_id: ConnectionId) {
    let (guild_index, char_name) = match state.users.get(&conn_id) {
        Some(u) if u.logged => (u.guild_index, u.char_name.clone()),
        _ => return,
    };

    if guild_index <= 0 {
        // No guild — send guild list
        let guild_list = guilds::list_guilds(&state.pool).await;
        let count = guild_list.len();
        let mut names = format!("{}", count);
        for (_num, name, align, level) in &guild_list {
            names.push(',');
            names.push_str(&format!("{}-{}-{}", name, guilds::alignment_name(*align), level));
        }
        let pkt = binary_packets::write_guild_list(&names);
        state.send_bytes(conn_id, &pkt).await;
        return;
    }


    let guild = match guilds::load_guild(&state.pool, guild_index).await {
        Some(g) => g,
        None => return,
    };

    let is_leader = guild.leader.to_uppercase() == char_name.to_uppercase();
    let is_sublider = guild.sub_lider1.to_uppercase() == char_name.to_uppercase()
        || guild.sub_lider2.to_uppercase() == char_name.to_uppercase();

    let members = guilds::load_members(&state.pool, &guild.name).await;
    let guild_list = guilds::list_guilds(&state.pool).await;

    if is_leader || is_sublider {
        // Leader/sublider view: IREDAEL
        let applicants = guilds::load_applicants(&state.pool, &guild.name).await;
        let mut data = guild.puntos_clan.to_string();
        data.push(BF);
        data.push_str(&guild.nivel_clan.to_string());
        data.push(BF);
        data.push_str(&guild.leader);
        data.push(BF);
        data.push_str(&guild.sub_lider1);
        data.push(BF);
        data.push_str(&guild.sub_lider2);
        // Castle positions (empty for now)
        for _ in 0..4 { data.push(BF); data.push('0'); }
        data.push(BF);
        data.push_str(&guild.reputation.to_string());
        data.push(BF);
        data.push_str(&guild.cvc_wins.to_string());
        data.push(BF);
        data.push_str(&guild.cvc_losses.to_string());
        data.push(BF);
        data.push_str(&guild.castle_sieges.to_string());
        // Guild list
        data.push(BF);
        data.push_str(&guild_list.len().to_string());
        for (_num, name, align, level) in &guild_list {
            data.push(BF);
            data.push_str(&format!("{}${}${}", name, guilds::alignment_name(*align), level));
        }
        // Members
        data.push(BF);
        data.push_str(&members.len().to_string());
        data.push(BF);
        data.push_str(&members.join(","));
        // Applicants
        data.push(BF);
        data.push_str(&applicants.len().to_string());
        for app in &applicants {
            data.push(BF);
            data.push_str(&format!("{}: {}", app.name, app.detail));
        }
        let binary = binary_packets::write_guild_info_leader(&data);
        state.send_bytes(conn_id, &binary).await;
    } else {
        // Regular member view: IREDAEK
        let mut data = guild.puntos_clan.to_string();
        data.push(BF);
        data.push_str(&guild.nivel_clan.to_string());
        data.push(BF);
        data.push_str(&guild.leader);
        data.push(BF);
        data.push_str(&guild.sub_lider1);
        data.push(BF);
        data.push_str(&guild.sub_lider2);
        // Castle positions
        for _ in 0..4 { data.push(BF); data.push('0'); }
        data.push(BF);
        data.push_str(&guild.reputation.to_string());
        // Guild list
        data.push(BF);
        data.push_str(&guild_list.len().to_string());
        for (_num, name, align, level) in &guild_list {
            data.push(BF);
            data.push_str(&format!("{}-{}-{}", name, level, guilds::alignment_name(*align)));
        }
        // Members
        data.push(BF);
        data.push_str(&members.len().to_string());
        data.push(BF);
        data.push_str(&members.join(","));
        let binary = binary_packets::write_guild_info_member(&data);
        state.send_bytes(conn_id, &binary).await;
    }
}

/// CIG — Submit guild creation form (after SHOWFUN was sent).
/// Data format: CIG<desc><BF><name><BF><url><BF><cantcodex><BF><codex1><BF>...
pub(super) async fn handle_guild_create(state: &mut GameState, conn_id: ConnectionId, data: &str) {
    let payload = strip_opcode(data, 3);
    let parts: Vec<&str> = payload.split(BF).collect();

    if parts.len() < 4 {
        state.send_console(conn_id, "Datos invalidos para crear clan.", font_index::INFO).await;
        return;
    }

    let (char_name, alignment, level, skills_leadership) = match state.users.get(&conn_id) {
        Some(u) if u.logged => (u.char_name.clone(), u.guild_creating_alignment, u.level, u.skills[0]),
        _ => return,
    };

    // Validate requirements
    if level < 50 {
        state.send_console(conn_id, "Necesitas nivel 50 para fundar un clan.", font_index::INFO).await;
        return;
    }
    if skills_leadership < 100 {
        state.send_console(conn_id, "Necesitas 100 en liderazgo.", font_index::INFO).await;
        return;
    }

    let desc = parts[0];
    let name = parts[1];
    let url = parts[2];
    let cant_codex: usize = parts[3].parse().unwrap_or(0);

    if !guilds::is_valid_guild_name(name) {
        state.send_console(conn_id, "Nombre de clan invalido.", font_index::INFO).await;
        return;
    }

    // Check name uniqueness
    if guilds::find_guild_by_name(&state.pool, name).await.is_some() {
        state.send_console(conn_id, "Ya existe un clan con ese nombre.", font_index::INFO).await;
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
        state.send_console(conn_id, "Necesitas el Amuleto de Lider para fundar un clan.", font_index::INFO).await;
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
    let guild_num = guilds::create_guild(&state.pool, name, &char_name, alignment, desc, url, codex).await;

    // Update user state
    if let Some(user) = state.users.get_mut(&conn_id) {
        user.guild_index = guild_num;
    }

    // Update character guild in DB
    crate::db::charfile::update_guild_index(&state.pool, &char_name, guild_num).await.ok();

    // Broadcast creation
    let args = format!("{}@{}@{}", char_name, name, guilds::alignment_name(alignment));
    state.send_msg_id_to(SendTarget::ToAll, 264, &args).await;

    // Send updated inventory
    send_full_inventory(state, conn_id).await;

    // Sound effect
    let sound = binary_packets::write_play_wave(44, 0, 0);
    state.send_bytes(conn_id, &sound).await;

    state.send_console(conn_id, &format!("Has fundado el clan {}!", name), font_index::GUILD_MSG).await;
}

/// /FUNDARCLAN — Start guild creation flow. Auto-detect alignment from character status.
pub(super) async fn handle_slash_fundarclan(state: &mut GameState, conn_id: ConnectionId) {
    let (guild_index, level, criminal, _reputation) = match state.users.get(&conn_id) {
        Some(u) if u.logged => (u.guild_index, u.level, u.criminal, u.reputation),
        _ => return,
    };

    if guild_index > 0 {
        state.send_console(conn_id, "Ya perteneces a un clan.", font_index::INFO).await;
        return;
    }

    if level < 50 {
        state.send_console(conn_id, "Necesitas nivel 50 para fundar un clan.", font_index::INFO).await;
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
    let pkt = binary_packets::write_show_guild_fundation_form();
    state.send_bytes(conn_id, &pkt).await;
}

/// /CERRARCLAN — Dissolve guild (leader + sole member) or leave (sublider).
pub(super) async fn handle_slash_cerrarclan(state: &mut GameState, conn_id: ConnectionId) {
    let (guild_index, char_name) = match state.users.get(&conn_id) {
        Some(u) if u.logged && u.guild_index > 0 => (u.guild_index, u.char_name.clone()),
        _ => {
            state.send_msg_id(conn_id, 120, "").await;
            return;
        }
    };


    let guild = match guilds::load_guild(&state.pool, guild_index).await {
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
        state.send_msg_id(conn_id, 339, "").await;
        return;
    }

    // Leader must be sole member
    let members = guilds::load_members(&state.pool, &guild.name).await;
    if members.len() > 1 {
        state.send_msg_id(conn_id, 340, "").await;
        return;
    }

    // Dissolve
    guilds::dissolve_guild(&state.pool, guild_index).await;

    // Clear user guild state
    if let Some(user) = state.users.get_mut(&conn_id) {
        user.guild_index = 0;
    }

    // Update character guild in DB
    crate::db::charfile::update_guild_index(&state.pool, &char_name, 0).await.ok();

    // Broadcast dissolution
    state.send_msg_id_to(SendTarget::ToAll, 341, &guild.name).await;
}

/// /SALIRCLAN — Leave guild voluntarily.
pub(super) async fn handle_slash_salirclan(state: &mut GameState, conn_id: ConnectionId) {
    let (guild_index, char_name) = match state.users.get(&conn_id) {
        Some(u) if u.logged && u.guild_index > 0 => (u.guild_index, u.char_name.clone()),
        _ => {
            state.send_msg_id(conn_id, 120, "").await;
            return;
        }
    };


    let guild = match guilds::load_guild(&state.pool, guild_index).await {
        Some(g) => g,
        None => return,
    };

    // Leader cannot leave — must dissolve or transfer
    if guild.leader.to_uppercase() == char_name.to_uppercase() {
        state.send_console(conn_id, "Eres el lider, usa /CERRARCLAN o /HACLIDER.", font_index::INFO).await;
        return;
    }

    handle_member_leave(state, conn_id, &char_name, guild_index).await;
}

/// Handle a member leaving a guild (used by /SALIRCLAN and /CERRARCLAN for subliders)
pub(super) async fn handle_member_leave(state: &mut GameState, conn_id: ConnectionId, char_name: &str, guild_index: i32) {

    let guild = match guilds::load_guild(&state.pool, guild_index).await {
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
        guilds::save_guild(&state.pool, &updated).await;

        // Notify guild
        state.send_msg_id_to(SendTarget::ToGuildMembers(guild_index), 337, char_name).await;
    } else {
        // Notify guild
        state.send_msg_id_to(SendTarget::ToGuildMembers(guild_index), 354, char_name).await;
    }

    // Remove from members file
    guilds::remove_member(&state.pool, &guild.name, char_name).await;

    // Clear user state
    if let Some(user) = state.users.get_mut(&conn_id) {
        user.guild_index = 0;
    }

    // Update character guild in DB
    crate::db::charfile::update_guild_index(&state.pool, char_name, 0).await.ok();

    state.send_msg_id(conn_id, 338, "").await;
}

/// /HACLIDER <name> — Transfer leadership.
pub(super) async fn handle_slash_haclider(state: &mut GameState, conn_id: ConnectionId, target_name: &str) {
    let (guild_index, char_name) = match state.users.get(&conn_id) {
        Some(u) if u.logged && u.guild_index > 0 => (u.guild_index, u.char_name.clone()),
        _ => return,
    };


    let guild = match guilds::load_guild(&state.pool, guild_index).await {
        Some(g) => g,
        None => return,
    };

    if guild.leader.to_uppercase() != char_name.to_uppercase() {
        state.send_msg_id(conn_id, 377, "").await;
        return;
    }

    // Check target is in same guild and online
    let target_conn = state.online_names.get(&target_name.to_uppercase()).copied();
    let target_guild = target_conn.and_then(|c| state.users.get(&c).map(|u| u.guild_index));
    if target_guild != Some(guild_index) {
        state.send_msg_id(conn_id, 511, "").await;
        return;
    }

    // Transfer leadership
    let mut updated = guild;
    updated.leader = target_name.to_string();
    guilds::save_guild(&state.pool, &updated).await;

    // Notify
    state.send_msg_id_to(SendTarget::ToGuildMembers(guild_index), 512, target_name).await;
}

/// /SUBLIDER <name> — Promote member to sub-leader.
pub(super) async fn handle_slash_sublider(state: &mut GameState, conn_id: ConnectionId, target_name: &str) {
    let (guild_index, char_name) = match state.users.get(&conn_id) {
        Some(u) if u.logged && u.guild_index > 0 => (u.guild_index, u.char_name.clone()),
        _ => return,
    };


    let guild = match guilds::load_guild(&state.pool, guild_index).await {
        Some(g) => g,
        None => return,
    };

    if guild.leader.to_uppercase() != char_name.to_uppercase() {
        state.send_msg_id(conn_id, 377, "").await;
        return;
    }

    // Check target in same guild
    let target_conn = state.online_names.get(&target_name.to_uppercase()).copied();
    let target_guild = target_conn.and_then(|c| state.users.get(&c).map(|u| u.guild_index));
    if target_guild != Some(guild_index) {
        state.send_msg_id(conn_id, 511, "").await;
        return;
    }

    // Check not already sublider
    if guild.sub_lider1.to_uppercase() == target_name.to_uppercase()
        || guild.sub_lider2.to_uppercase() == target_name.to_uppercase()
    {
        state.send_msg_id(conn_id, 510, "").await;
        return;
    }

    let mut updated = guild;
    if updated.sub_lider1 == "Fermin" || updated.sub_lider1.is_empty() {
        updated.sub_lider1 = target_name.to_string();
    } else if updated.sub_lider2 == "Fermin" || updated.sub_lider2.is_empty() {
        updated.sub_lider2 = target_name.to_string();
    } else {
        state.send_msg_id(conn_id, 513, "").await;
        return;
    }

    guilds::save_guild(&state.pool, &updated).await;

    state.send_msg_id_to(SendTarget::ToGuildMembers(guild_index), 514, target_name).await;
}

/// /QSUBLIDR <name> — Demote sub-leader.
pub(super) async fn handle_slash_qsublidr(state: &mut GameState, conn_id: ConnectionId, target_name: &str) {
    let (guild_index, char_name) = match state.users.get(&conn_id) {
        Some(u) if u.logged && u.guild_index > 0 => (u.guild_index, u.char_name.clone()),
        _ => return,
    };


    let guild = match guilds::load_guild(&state.pool, guild_index).await {
        Some(g) => g,
        None => return,
    };

    if guild.leader.to_uppercase() != char_name.to_uppercase() {
        state.send_msg_id(conn_id, 377, "").await;
        return;
    }

    let mut updated = guild;
    if updated.sub_lider1.to_uppercase() == target_name.to_uppercase() {
        updated.sub_lider1 = "Fermin".to_string();
    } else if updated.sub_lider2.to_uppercase() == target_name.to_uppercase() {
        updated.sub_lider2 = "Fermin".to_string();
    } else {
        state.send_msg_id(conn_id, 516, "").await;
        return;
    }

    guilds::save_guild(&state.pool, &updated).await;

    state.send_msg_id_to(SendTarget::ToGuildMembers(guild_index), 515, target_name).await;
}

/// /CLAN — List online guild members.
pub(super) async fn handle_slash_clan_list(state: &mut GameState, conn_id: ConnectionId) {
    let guild_index = match state.users.get(&conn_id) {
        Some(u) if u.logged && u.guild_index > 0 => u.guild_index,
        _ => {
            state.send_msg_id(conn_id, 120, "").await;
            return;
        }
    };


    let guild = match guilds::load_guild(&state.pool, guild_index).await {
        Some(g) => g,
        None => return,
    };

    // List online members
    state.send_console(conn_id, &format!("Miembros online del clan {}:", guild.name), font_index::GUILD_MSG).await;

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
        state.send_console(conn_id, member, font_index::INFO).await;
    }
}

/// /CMSG <text> — Send clan chat message.
pub(super) async fn handle_slash_cmsg(state: &mut GameState, conn_id: ConnectionId, text: &str) {
    let (guild_index, char_name) = match state.users.get(&conn_id) {
        Some(u) if u.logged && u.guild_index > 0 => (u.guild_index, u.char_name.clone()),
        _ => {
            state.send_msg_id(conn_id, 120, "").await;
            return;
        }
    };

    // No tilde allowed in clan chat
    if text.contains('~') {
        state.send_msg_id(conn_id, 198, "").await;
        return;
    }


    let guild = match guilds::load_guild(&state.pool, guild_index).await {
        Some(g) => g,
        None => return,
    };

    let is_leader = guild.leader.to_uppercase() == char_name.to_uppercase();
    let is_sublider = guild.sub_lider1.to_uppercase() == char_name.to_uppercase()
        || guild.sub_lider2.to_uppercase() == char_name.to_uppercase();

    let prefix = if is_leader || is_sublider { "Lider " } else { "" };

    let msg = format!("{}{}: {}", prefix, char_name, text);
    state.send_guild_chat_to(SendTarget::ToGuildMembers(guild_index), &msg).await;
}

/// DESCOD — Update guild codex and description.
pub(super) async fn handle_guild_update_codex(state: &mut GameState, conn_id: ConnectionId, data: &str) {
    let payload = strip_opcode(data, 6);
    let parts: Vec<&str> = payload.split(BF).collect();

    let (guild_index, char_name) = match state.users.get(&conn_id) {
        Some(u) if u.logged && u.guild_index > 0 => (u.guild_index, u.char_name.clone()),
        _ => return,
    };


    let guild = match guilds::load_guild(&state.pool, guild_index).await {
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
    guilds::save_guild(&state.pool, &updated).await;
}

/// ACEPTARI — Accept applicant into guild.
pub(super) async fn handle_guild_accept(state: &mut GameState, conn_id: ConnectionId, data: &str) {
    let applicant_name = strip_opcode(data, 8).trim().to_string();

    let (guild_index, char_name) = match state.users.get(&conn_id) {
        Some(u) if u.logged && u.guild_index > 0 => (u.guild_index, u.char_name.clone()),
        _ => return,
    };


    let guild = match guilds::load_guild(&state.pool, guild_index).await {
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
    let members = guilds::load_members(&state.pool, &guild.name).await;
    let max = guilds::max_members_for_level(guild.nivel_clan);
    if members.len() as i32 >= max {
        state.send_msg_id(conn_id, 500, "").await;
        return;
    }

    // Remove from applicants
    guilds::remove_applicant(&state.pool, &guild.name, &applicant_name).await;

    // Add to members
    guilds::add_member(&state.pool, &guild.name, &applicant_name).await;

    // Update applicant's guild in DB
    crate::db::charfile::update_guild_index(&state.pool, &applicant_name, guild_index).await.ok();

    // If applicant is online, update their state
    if let Some(&target_conn) = state.online_names.get(&applicant_name.to_uppercase()) {
        if let Some(user) = state.users.get_mut(&target_conn) {
            user.guild_index = guild_index;
            user.puede_retirar_obj = false;
            user.puede_retirar_oro = false;
        }
        state.send_msg_id(target_conn, 501, "").await;

        let sound = binary_packets::write_play_wave(43, 0, 0);
        state.send_bytes(target_conn, &sound).await;
    }

    // Notify guild
    state.send_msg_id_to(SendTarget::ToGuildMembers(guild_index), 503, &applicant_name).await;
}

/// RECHAZAR — Reject applicant.
pub(super) async fn handle_guild_reject(state: &mut GameState, conn_id: ConnectionId, data: &str) {
    let payload = strip_opcode(data, 8);
    let name = read_field(1, payload, ',');
    let reason = read_field(2, payload, ',');

    let (guild_index, char_name) = match state.users.get(&conn_id) {
        Some(u) if u.logged && u.guild_index > 0 => (u.guild_index, u.char_name.clone()),
        _ => return,
    };


    let guild = match guilds::load_guild(&state.pool, guild_index).await {
        Some(g) => g,
        None => return,
    };

    // Must be leader or sublider
    let is_leader = guild.leader.to_uppercase() == char_name.to_uppercase();
    let is_sublider = guild.sub_lider1.to_uppercase() == char_name.to_uppercase()
        || guild.sub_lider2.to_uppercase() == char_name.to_uppercase();
    if !is_leader && !is_sublider { return; }

    guilds::remove_applicant(&state.pool, &guild.name, &name).await;

    // If online, notify
    if let Some(&target_conn) = state.online_names.get(&name.to_uppercase()) {
        state.send_console(target_conn, &format!("Tu solicitud fue rechazada: {}", reason), font_index::INFO).await;
    }
}

/// ECHARCLA — Expel member from guild.
pub(super) async fn handle_guild_expel(state: &mut GameState, conn_id: ConnectionId, data: &str) {
    let target_name = strip_opcode(data, 8).trim().to_string();

    let (guild_index, char_name) = match state.users.get(&conn_id) {
        Some(u) if u.logged && u.guild_index > 0 => (u.guild_index, u.char_name.clone()),
        _ => return,
    };


    let guild = match guilds::load_guild(&state.pool, guild_index).await {
        Some(g) => g,
        None => return,
    };

    // Only leader can expel
    if guild.leader.to_uppercase() != char_name.to_uppercase() {
        state.send_msg_id(conn_id, 377, "").await;
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
        guilds::save_guild(&state.pool, &updated).await;
    } else if updated.sub_lider2.to_uppercase() == target_name.to_uppercase() {
        updated.sub_lider2 = "Fermin".to_string();
        guilds::save_guild(&state.pool, &updated).await;
    }

    // Remove from members
    guilds::remove_member(&state.pool, &guild.name, &target_name).await;

    // Update character guild in DB
    crate::db::charfile::update_guild_index(&state.pool, &target_name, 0).await.ok();

    // If online, update state
    if let Some(&target_conn) = state.online_names.get(&target_name.to_uppercase()) {
        if let Some(user) = state.users.get_mut(&target_conn) {
            user.guild_index = 0;
        }
        state.send_console(target_conn, "Has sido expulsado del clan.", font_index::INFO).await;
    }

    // Notify guild
    state.send_msg_id_to(SendTarget::ToGuildMembers(guild_index), 505, &target_name).await;
}

/// ACTGNEWS — Update guild news.
pub(super) async fn handle_guild_news(state: &mut GameState, conn_id: ConnectionId, data: &str) {
    let news = strip_opcode(data, 8).to_string();

    let (guild_index, char_name) = match state.users.get(&conn_id) {
        Some(u) if u.logged && u.guild_index > 0 => (u.guild_index, u.char_name.clone()),
        _ => return,
    };


    let guild = match guilds::load_guild(&state.pool, guild_index).await {
        Some(g) => g,
        None => return,
    };

    if guild.leader.to_uppercase() != char_name.to_uppercase() {
        return;
    }

    let mut updated = guild;
    updated.news = news;
    guilds::save_guild(&state.pool, &updated).await;
}

/// SOLICITUD — Apply to join a guild.
pub(super) async fn handle_guild_apply(state: &mut GameState, conn_id: ConnectionId, data: &str) {
    let payload = strip_opcode(data, 9);
    let guild_name = read_field(1, payload, ',');
    let petition = read_field(2, payload, ',');

    let (guild_index, char_name, level) = match state.users.get(&conn_id) {
        Some(u) if u.logged => (u.guild_index, u.char_name.clone(), u.level),
        _ => return,
    };

    if guild_index > 0 {
        state.send_console(conn_id, "Ya perteneces a un clan.", font_index::INFO).await;
        return;
    }

    if level < 25 {
        state.send_console(conn_id, "Necesitas nivel 25 para unirte a un clan.", font_index::INFO).await;
        return;
    }


    let target_guild_num = match guilds::find_guild_by_name(&state.pool, &guild_name).await {
        Some(n) => n,
        None => {
            state.send_console(conn_id, "El clan no existe.", font_index::INFO).await;
            return;
        }
    };

    let _guild = match guilds::load_guild(&state.pool, target_guild_num).await {
        Some(g) => g,
        None => return,
    };

    if !guilds::add_applicant(&state.pool, &guild_name, &char_name, &petition).await {
        state.send_console(conn_id, "El clan ya tiene el maximo de solicitudes pendientes.", font_index::INFO).await;
        return;
    }

    state.send_msg_id(conn_id, 507, "").await;
}

/// CLANDETAILS — Request details about a guild.
pub(super) async fn handle_guild_details(state: &mut GameState, conn_id: ConnectionId, data: &str) {
    let guild_name = strip_opcode(data, 11).trim().to_string();

    if !state.users.get(&conn_id).map(|u| u.logged).unwrap_or(false) {
        return;
    }


    let guild_num = match guilds::find_guild_by_name(&state.pool, &guild_name).await {
        Some(n) => n,
        None => return,
    };
    let guild = match guilds::load_guild(&state.pool, guild_num).await {
        Some(g) => g,
        None => return,
    };

    let members = guilds::load_members(&state.pool, &guild.name).await;

    // DTLC format: level<BF>alignment<BF>repu<BF>founder<BF>date<BF>leader<BF>sub1<BF>sub2<BF>membercount<BF>codex1..8<BF>desc<BF>name
    let mut data = guild.nivel_clan.to_string();
    data.push(BF);
    data.push_str(guilds::alignment_name(guild.alignment));
    data.push(BF);
    data.push_str(&guild.reputation.to_string());
    data.push(BF);
    data.push_str(&guild.founder);
    data.push(BF);
    data.push_str(&guild.date);
    data.push(BF);
    data.push_str(&guild.leader);
    data.push(BF);
    data.push_str(&guild.sub_lider1);
    data.push(BF);
    data.push_str(&guild.sub_lider2);
    data.push(BF);
    data.push_str(&members.len().to_string());
    for codex_line in &guild.codex {
        data.push(BF);
        data.push_str(codex_line);
    }
    data.push(BF);
    data.push_str(&guild.desc);
    data.push(BF);
    data.push_str(&guild.name);

    let binary = binary_packets::write_guild_details(&data);
    state.send_bytes(conn_id, &binary).await;
}

/// INIBOV — Open guild bank vault window.
pub(super) async fn handle_guild_bank_open(state: &mut GameState, conn_id: ConnectionId) {
    let (guild_index, _char_name) = match state.users.get(&conn_id) {
        Some(u) if u.logged && u.guild_index > 0 => (u.guild_index, u.char_name.clone()),
        _ => return,
    };


    let guild = match guilds::load_guild(&state.pool, guild_index).await {
        Some(g) => g,
        None => return,
    };

    // Send bank gold
    let gold = guilds::load_bank_gold(&state.pool, &guild.name).await;
    let gold_pkt = binary_packets::write_update_bank_gold(gold as i32);
    state.send_bytes(conn_id, &gold_pkt).await;

    // Send bank items
    let items = guilds::load_bank_items(&state.pool, &guild.name).await;
    for (i, slot) in items.iter().enumerate() {
        if slot.obj_index > 0 {
            let obj_name = state.game_data.objects.get(slot.obj_index as usize - 1)
                .map(|o| o.name.as_str())
                .unwrap_or("???");
            let grh = state.game_data.objects.get(slot.obj_index as usize - 1)
                .map(|o| o.grh_index)
                .unwrap_or(0);
            let pkt = binary_packets::write_change_bank_slot(
                (i + 1) as u8, slot.obj_index as i16, obj_name, slot.amount as i16, false, grh as i16, 0, 0, 0, 0, 0.0,
            );
            state.send_bytes(conn_id, &pkt).await;
        }
    }

    // Open bank window
    let pkt = binary_packets::write_bank_init(gold as i32);
    state.send_bytes(conn_id, &pkt).await;
}

/// CCDO — Deposit gold into guild bank.
pub(super) async fn handle_guild_bank_deposit(state: &mut GameState, conn_id: ConnectionId, data: &str) {
    let payload = strip_opcode(data, 4);
    let amount: i64 = payload.trim().parse().unwrap_or(0);

    if amount <= 0 {
        state.send_msg_id(conn_id, 508, "").await;
        return;
    }

    let (guild_index, gold) = match state.users.get(&conn_id) {
        Some(u) if u.logged && u.guild_index > 0 => (u.guild_index, u.gold),
        _ => return,
    };

    if amount > gold {
        state.send_console(conn_id, "No tienes suficiente oro.", font_index::INFO).await;
        return;
    }


    let guild = match guilds::load_guild(&state.pool, guild_index).await {
        Some(g) => g,
        None => return,
    };

    let current_bank_gold = guilds::load_bank_gold(&state.pool, &guild.name).await;
    if current_bank_gold + amount > guilds::MAX_GUILD_BANK_GOLD {
        state.send_msg_id(conn_id, 268, "").await;
        return;
    }

    // Deduct from player
    if let Some(user) = state.users.get_mut(&conn_id) {
        user.gold -= amount;
    }

    // Add to guild bank
    guilds::save_bank_gold(&state.pool, &guild.name, current_bank_gold + amount).await;

    // Update client
    send_stats_gold(state, conn_id).await;

    state.send_console(conn_id, &format!("Has depositado {} monedas de oro en la boveda del clan.", amount), font_index::INFO).await;
}

/// CCRO — Withdraw gold from guild bank.
pub(super) async fn handle_guild_bank_withdraw(state: &mut GameState, conn_id: ConnectionId, data: &str) {
    let payload = strip_opcode(data, 4);
    let amount: i64 = payload.trim().parse().unwrap_or(0);

    if amount <= 0 {
        state.send_msg_id(conn_id, 508, "").await;
        return;
    }

    let (guild_index, puede_retirar_oro) = match state.users.get(&conn_id) {
        Some(u) if u.logged && u.guild_index > 0 => (u.guild_index, u.puede_retirar_oro),
        _ => return,
    };

    if !puede_retirar_oro {
        state.send_msg_id(conn_id, 269, "").await;
        return;
    }


    let guild = match guilds::load_guild(&state.pool, guild_index).await {
        Some(g) => g,
        None => return,
    };

    let current_bank_gold = guilds::load_bank_gold(&state.pool, &guild.name).await;
    if amount > current_bank_gold {
        state.send_msg_id(conn_id, 270, "").await;
        return;
    }

    // Add to player
    if let Some(user) = state.users.get_mut(&conn_id) {
        user.gold += amount;
    }

    // Remove from guild bank
    guilds::save_bank_gold(&state.pool, &guild.name, current_bank_gold - amount).await;

    // Update client
    send_stats_gold(state, conn_id).await;

    state.send_console(conn_id, &format!("Has retirado {} monedas de oro de la boveda del clan.", amount), font_index::INFO).await;
}

// =====================================================================
// Clan bank (BoveClan NPC) — modBancoNuevo.bas
// =====================================================================

/// Open clan bank for user. VB6: Acciones.bas BoveClan handler.
pub(super) async fn iniciar_clan_banco(state: &mut GameState, conn_id: ConnectionId) {
    let guild_index = match state.users.get(&conn_id) {
        Some(u) if u.logged && u.guild_index > 0 => u.guild_index,
        _ => return, // No guild
    };


    let guild = match guilds::load_guild(&state.pool, guild_index).await {
        Some(g) => g,
        None => return,
    };

    // Check if another user already has this clan bank open (VB6: CuentaBancaria lock)
    let guild_upper = guild.name.to_uppercase();
    let already_open = state.users.values().any(|u| u.cuenta_bancaria.to_uppercase() == guild_upper);
    if already_open {
        state.send_msg_id(conn_id, 640, "").await;
        return;
    }

    // Load clan bank items from .bov file
    let items = guilds::load_bank_items(&state.pool, &guild.name).await;
    let gold = guilds::load_bank_gold(&state.pool, &guild.name).await;

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
    let pkt = binary_packets::write_guild_bank_init(ret_obj != 0, ret_oro != 0);
    state.send_bytes(conn_id, &pkt).await;

    if let Some(user) = state.users.get_mut(&conn_id) {
        user.comerciando = true;
    }
}

/// Send all clan bank slots to client.
/// VB6: BUpdateBanUserInv sends SBG for each slot.
/// SBG format: SBG<slot>,<obj_idx>,<name>,<amount>,<grh>,<type>,<maxhit>,<minhit>,<maxdef>,<bank_gold>,<user_gold>
pub(super) async fn enviar_clan_banco_inv(state: &mut GameState, conn_id: ConnectionId, guild_name: &str, bank_gold: i64) {
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
            let pkt = binary_packets::write_guild_bank_slot_data(
                slot_num as u8, obj_idx as i16, &name, amount as i16,
                grh as i16, otype as u8, maxhit as i16, minhit as i16, maxdef as i16,
                bank_gold, user_gold,
            );
            state.send_bytes(conn_id, &pkt).await;
        } else {
            let pkt = binary_packets::write_guild_bank_slot_data(
                slot_num as u8, 0, "(Nada)", 0,
                0, 0, 0, 0, 0,
                bank_gold, user_gold,
            );
            state.send_bytes(conn_id, &pkt).await;
        }
    }
}

/// RETB — Withdraw item from clan bank (VB6: BUserRetiraItem).
pub(super) async fn handle_clan_bank_withdraw_item(state: &mut GameState, conn_id: ConnectionId, data: &str) {
    let payload = strip_opcode(data, 4); // "RETB"
    let parts: Vec<&str> = payload.split(',').collect();
    let slot: usize = parts.first().and_then(|s| s.trim().parse().ok()).unwrap_or(0);
    let amount: i32 = parts.get(1).and_then(|s| s.trim().parse().ok()).unwrap_or(0);

    if slot < 1 || amount < 1 { return; }

    let (dead, puede_retirar, guild_index) = match state.users.get(&conn_id) {
        Some(u) if u.logged => (u.dead, u.puede_retirar_obj, u.guild_index),
        _ => return,
    };
    if dead { state.send_msg_id(conn_id, 3, "").await; return; }
    if !puede_retirar { state.send_msg_id(conn_id, 289, "").await; return; }

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
            let guild_name = guilds::load_guild(&state.pool, guild_index).await
                .map(|g| g.name).unwrap_or_default();
            let bank_gold = guilds::load_bank_gold(&state.pool, &guild_name).await;
            enviar_clan_banco_inv(state, conn_id, &guild_name, bank_gold).await;
            let pkt = binary_packets::write_guild_bank_slot(slot as u8, 0);
            state.send_bytes(conn_id, &pkt).await;
        }
        None => {
            state.send_msg_id(conn_id, 108, "").await; // Inventory full
        }
    }
}

/// DEPB — Deposit item into clan bank (VB6: BUserDepositaItem).
pub(super) async fn handle_clan_bank_deposit_item(state: &mut GameState, conn_id: ConnectionId, data: &str) {
    let payload = strip_opcode(data, 4); // "DEPB"
    let parts: Vec<&str> = payload.split(',').collect();
    let inv_slot: usize = parts.first().and_then(|s| s.trim().parse().ok()).unwrap_or(0);
    let amount: i32 = parts.get(1).and_then(|s| s.trim().parse().ok()).unwrap_or(0);

    if inv_slot < 1 || amount < 1 { return; }

    let (dead, guild_index, comerciando) = match state.users.get(&conn_id) {
        Some(u) if u.logged => (u.dead, u.guild_index, u.comerciando),
        _ => return,
    };
    if dead { state.send_msg_id(conn_id, 3, "").await; return; }

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
        state.send_msg_id(conn_id, 185, "").await;
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
            let guild_name = guilds::load_guild(&state.pool, guild_index).await
                .map(|g| g.name).unwrap_or_default();
            let bank_gold = guilds::load_bank_gold(&state.pool, &guild_name).await;
            enviar_clan_banco_inv(state, conn_id, &guild_name, bank_gold).await;
            let pkt = binary_packets::write_guild_bank_slot(inv_slot as u8, 1);
            state.send_bytes(conn_id, &pkt).await;
        }
        None => {
            state.send_msg_id(conn_id, 186, "").await; // Clan bank full
        }
    }
}

/// CCBG — Save clan bank inventory to file (VB6: TCP_HandleData1.bas line 2415).
pub(super) async fn handle_clan_bank_save(state: &mut GameState, conn_id: ConnectionId) {
    let guild_index = match state.users.get(&conn_id) {
        Some(u) if u.logged && u.guild_index > 0 => u.guild_index,
        _ => return,
    };


    let guild = match guilds::load_guild(&state.pool, guild_index).await {
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

    guilds::save_bank_items(&state.pool, &guild.name, &items).await;
}

