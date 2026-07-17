//! Guild system handlers: guild management, codex, member management.
//! Extracted from mod.rs to reduce file size.

use super::send_full_inventory;
use crate::db::guilds;
use crate::game::types::{GameState, SendTarget};
use crate::net::ConnectionId;
use crate::protocol::{binary_packets, fields::read_field, font_index};
use tracing::warn;

// =====================================================================
// Guild system handlers (modGuilds.bas / clsClan.cls)
// =====================================================================

/// Re-send CC packet for user to area (VB6: RefreshCharStatus).
/// Called after guild join/leave so the clan tag updates for nearby players.
async fn refresh_user_cc(state: &mut GameState, conn_id: ConnectionId) {
    let (cc_pkt, map, x, y) = match state.users.get(&conn_id) {
        Some(u) if u.logged => (u.build_cc_binary(), u.pos_map, u.pos_x, u.pos_y),
        _ => return,
    };
    state.send_data_bytes(SendTarget::ToArea { map, x, y }, &cc_pkt);
}

/// Clear guild state from user and refresh CC.
async fn clear_user_guild(state: &mut GameState, conn_id: ConnectionId) {
    if let Some(user) = state.users.get_mut(&conn_id) {
        user.guild_index = 0;
        user.guild_name.clear();
        user.seguro_clan = true;
    }
    refresh_user_cc(state, conn_id).await;
}

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
        for (_num, name, align, _level) in &guild_list {
            names.push(',');
            names.push_str(&format!("{}-{}-1", name, guilds::alignment_name(*align)));
        }
        let pkt = binary_packets::write_guild_list(&names);
        state.send_bytes(conn_id, &pkt);
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
        data.push_str("1");
        data.push(BF);
        data.push_str(&guild.leader);
        data.push(BF);
        data.push_str(&guild.sub_lider1);
        data.push(BF);
        data.push_str(&guild.sub_lider2);
        // Castle positions — VB6 sends 4 castle map numbers here.
        // Not implemented: no castle ownership data in DB schema yet.
        // When castles are added, query guild_castles table for owned positions.
        for _ in 0..4 {
            data.push(BF);
            data.push('0');
        }
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
        for (_num, name, align, _level) in &guild_list {
            data.push(BF);
            data.push_str(&format!("{}${}$1", name, guilds::alignment_name(*align)));
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
        state.send_bytes(conn_id, &binary);
    } else {
        // Regular member view: IREDAEK
        let mut data = guild.puntos_clan.to_string();
        data.push(BF);
        data.push_str("1");
        data.push(BF);
        data.push_str(&guild.leader);
        data.push(BF);
        data.push_str(&guild.sub_lider1);
        data.push(BF);
        data.push_str(&guild.sub_lider2);
        // Castle positions
        for _ in 0..4 {
            data.push(BF);
            data.push('0');
        }
        data.push(BF);
        data.push_str(&guild.reputation.to_string());
        // Guild list
        data.push(BF);
        data.push_str(&guild_list.len().to_string());
        for (_num, name, align, _level) in &guild_list {
            data.push(BF);
            data.push_str(&format!("{}-1-{}", name, guilds::alignment_name(*align)));
        }
        // Members
        data.push(BF);
        data.push_str(&members.len().to_string());
        data.push(BF);
        data.push_str(&members.join(","));
        let binary = binary_packets::write_guild_info_member(&data);
        state.send_bytes(conn_id, &binary);
    }
}

/// CIG — Submit guild creation form (after SHOWFUN was sent).
/// Data format: <desc><BF><name><BF><url><BF><cantcodex><BF><codex1><BF>...
pub(super) async fn handle_guild_create(
    state: &mut GameState,
    conn_id: ConnectionId,
    payload: &str,
) {
    let parts: Vec<&str> = payload.split(BF).collect();

    if parts.len() < 4 {
        state.send_console(
            conn_id,
            "Datos invalidos para crear clan.",
            font_index::INFO,
        );
        return;
    }

    let (char_name, alignment, level, skills_leadership) = match state.users.get(&conn_id) {
        Some(u) if u.logged => (
            u.char_name.clone(),
            u.guild_creating_alignment,
            u.level,
            u.skills[16], // LIDERAZGO = skill_id 17 → index 16 (0-based)
        ),
        _ => return,
    };

    // Validate requirements — VB6 13.3 parity: nivel >= 25, liderazgo >= 90.
    // (Amuleto de Lider item 939 is an added mechanic kept below, not part of VB6.)
    if level < 25 {
        state.send_console(
            conn_id,
            "Necesitas nivel 25 para fundar un clan.",
            font_index::INFO,
        );
        return;
    }
    if skills_leadership < 90 {
        state.send_console(conn_id, "Necesitas 90 de liderazgo.", font_index::INFO);
        return;
    }

    let desc = parts[0];
    let name = parts[1];
    let url = parts[2];
    let cant_codex: usize = parts[3].parse().unwrap_or(0);

    if !guilds::is_valid_guild_name(name) {
        state.send_console(conn_id, "Nombre de clan invalido.", font_index::INFO);
        return;
    }

    // Check name uniqueness
    if guilds::find_guild_by_name(&state.pool, name)
        .await
        .is_some()
    {
        state.send_console(
            conn_id,
            "Ya existe un clan con ese nombre.",
            font_index::INFO,
        );
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
    let has_amulet = state
        .users
        .get(&conn_id)
        .map(|u| {
            u.inventory
                .iter()
                .any(|s| s.obj_index == 939 && s.amount > 0)
        })
        .unwrap_or(false);
    if !has_amulet {
        state.send_console(
            conn_id,
            "Necesitas el Amuleto de Lider para fundar un clan.",
            font_index::INFO,
        );
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
    let guild_num =
        guilds::create_guild(&state.pool, name, &char_name, alignment, desc, url, codex).await;

    // Update user state
    if let Some(user) = state.users.get_mut(&conn_id) {
        user.guild_index = guild_num;
        user.guild_name = name.to_string();
    }

    // Update character guild in DB
    crate::db::charfile::update_guild_index(&state.pool, &char_name, guild_num)
        .await
        .ok();

    // Broadcast creation
    let args = format!(
        "{}@{}@{}",
        char_name,
        name,
        guilds::alignment_name(alignment)
    );
    state.send_msg_id_to(SendTarget::ToAll, 264, &args);

    // Re-send CC with clan tag to nearby players
    refresh_user_cc(state, conn_id).await;

    // Send updated inventory
    send_full_inventory(state, conn_id).await;

    // Sound effect
    let sound = binary_packets::write_play_wave(44, 0, 0);
    state.send_bytes(conn_id, &sound);

    state.send_console(
        conn_id,
        &format!("Has fundado el clan {}!", name),
        font_index::GUILD_MSG,
    );
}

/// /FUNDARCLAN — Start guild creation flow. Auto-detect alignment from character status.
pub(super) async fn handle_slash_fundarclan(state: &mut GameState, conn_id: ConnectionId) {
    let (guild_index, level, criminal, _reputation, liderazgo) = match state.users.get(&conn_id) {
        Some(u) if u.logged => (
            u.guild_index,
            u.level,
            u.criminal,
            u.reputation,
            u.skills[16],
        ),
        _ => return,
    };

    if guild_index > 0 {
        state.send_console(conn_id, "Ya perteneces a un clan.", font_index::INFO);
        return;
    }

    if level < 25 {
        state.send_console(
            conn_id,
            "Necesitas nivel 25 para fundar un clan.",
            font_index::INFO,
        );
        return;
    }

    // VB6 13.3 parity: Liderazgo >= 90 required to found a guild.
    if liderazgo < 90 {
        state.send_console(
            conn_id,
            "Necesitas 90 de liderazgo para fundar un clan.",
            font_index::INFO,
        );
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
    state.send_bytes(conn_id, &pkt);
}

/// /CERRARCLAN — Dissolve guild (leader + sole member) or leave (sublider).
pub(super) async fn handle_slash_cerrarclan(state: &mut GameState, conn_id: ConnectionId) {
    let (guild_index, char_name) = match state.users.get(&conn_id) {
        Some(u) if u.logged && u.guild_index > 0 => (u.guild_index, u.char_name.clone()),
        _ => {
            state.send_msg_id(conn_id, 120, "");
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
        state.send_msg_id(conn_id, 339, "");
        return;
    }

    // Leader must be sole member
    let members = guilds::load_members(&state.pool, &guild.name).await;
    if members.len() > 1 {
        state.send_msg_id(conn_id, 340, "");
        return;
    }

    // Dissolve
    guilds::dissolve_guild(&state.pool, guild_index).await;

    // Clear user guild state and refresh CC
    clear_user_guild(state, conn_id).await;

    // Update character guild in DB
    crate::db::charfile::update_guild_index(&state.pool, &char_name, 0)
        .await
        .ok();

    // Broadcast dissolution
    state.send_msg_id_to(SendTarget::ToAll, 341, &guild.name);
}

/// /SALIRCLAN — Leave guild voluntarily.
pub(super) async fn handle_slash_salirclan(state: &mut GameState, conn_id: ConnectionId) {
    let (guild_index, char_name) = match state.users.get(&conn_id) {
        Some(u) if u.logged && u.guild_index > 0 => (u.guild_index, u.char_name.clone()),
        _ => {
            state.send_msg_id(conn_id, 120, "");
            return;
        }
    };

    let guild = match guilds::load_guild(&state.pool, guild_index).await {
        Some(g) => g,
        None => return,
    };

    // Leader cannot leave — must dissolve or transfer
    if guild.leader.to_uppercase() == char_name.to_uppercase() {
        state.send_console(
            conn_id,
            "Eres el lider, usa /CERRARCLAN o /HACLIDER.",
            font_index::INFO,
        );
        return;
    }

    handle_member_leave(state, conn_id, &char_name, guild_index).await;
}

/// Handle a member leaving a guild (used by /SALIRCLAN and /CERRARCLAN for subliders)
pub(super) async fn handle_member_leave(
    state: &mut GameState,
    conn_id: ConnectionId,
    char_name: &str,
    guild_index: i32,
) {
    // Validate: the calling connection must match char_name OR be a GM
    let caller_ok = state
        .users
        .get(&conn_id)
        .map(|u| u.char_name.to_uppercase() == char_name.to_uppercase() || u.privileges > 0)
        .unwrap_or(false);
    if !caller_ok {
        warn!(
            "[GUILD] handle_member_leave: conn #{} tried to remove '{}' but is not that character or a GM",
            conn_id, char_name
        );
        return;
    }

    let guild = match guilds::load_guild(&state.pool, guild_index).await {
        Some(g) => g,
        None => return,
    };

    // Validate: char_name must actually be a member of this guild
    let members = guilds::load_members(&state.pool, &guild.name).await;
    let is_member = members
        .iter()
        .any(|m| m.to_uppercase() == char_name.to_uppercase());
    if !is_member {
        warn!(
            "[GUILD] handle_member_leave: '{}' is not a member of guild '{}'",
            char_name, guild.name
        );
        return;
    }

    // If sublider, clear the slot
    let is_sub1 = guild.sub_lider1.to_uppercase() == char_name.to_uppercase();
    let is_sub2 = guild.sub_lider2.to_uppercase() == char_name.to_uppercase();
    if is_sub1 || is_sub2 {
        let mut updated = guild.clone();
        if is_sub1 {
            updated.sub_lider1 = String::new();
        }
        if is_sub2 {
            updated.sub_lider2 = String::new();
        }
        guilds::save_guild(&state.pool, &updated).await;

        // Notify guild
        state.send_msg_id_to(SendTarget::ToGuildMembers(guild_index), 337, char_name);
    } else {
        // Notify guild
        state.send_msg_id_to(SendTarget::ToGuildMembers(guild_index), 354, char_name);
    }

    // Remove from members file
    guilds::remove_member(&state.pool, &guild.name, char_name).await;

    // Clear user state and refresh CC
    clear_user_guild(state, conn_id).await;

    // Update character guild in DB
    crate::db::charfile::update_guild_index(&state.pool, char_name, 0)
        .await
        .ok();

    state.send_msg_id(conn_id, 338, "");
}

/// /HACLIDER <name> — Transfer leadership.
pub(super) async fn handle_slash_haclider(
    state: &mut GameState,
    conn_id: ConnectionId,
    target_name: &str,
) {
    let (guild_index, char_name) = match state.users.get(&conn_id) {
        Some(u) if u.logged && u.guild_index > 0 => (u.guild_index, u.char_name.clone()),
        _ => return,
    };

    let guild = match guilds::load_guild(&state.pool, guild_index).await {
        Some(g) => g,
        None => return,
    };

    if guild.leader.to_uppercase() != char_name.to_uppercase() {
        state.send_msg_id(conn_id, 377, "");
        return;
    }

    // Check target is in same guild and online
    let target_conn = state.online_names.get(&target_name.to_uppercase()).copied();
    let target_guild = target_conn.and_then(|c| state.users.get(&c).map(|u| u.guild_index));
    if target_guild != Some(guild_index) {
        state.send_msg_id(conn_id, 511, "");
        return;
    }

    // Transfer leadership
    let mut updated = guild;
    updated.leader = target_name.to_string();
    guilds::save_guild(&state.pool, &updated).await;

    // Notify
    state.send_msg_id_to(SendTarget::ToGuildMembers(guild_index), 512, target_name);
}

/// /SUBLIDER <name> — Promote member to sub-leader.
pub(super) async fn handle_slash_sublider(
    state: &mut GameState,
    conn_id: ConnectionId,
    target_name: &str,
) {
    let (guild_index, char_name) = match state.users.get(&conn_id) {
        Some(u) if u.logged && u.guild_index > 0 => (u.guild_index, u.char_name.clone()),
        _ => return,
    };

    let guild = match guilds::load_guild(&state.pool, guild_index).await {
        Some(g) => g,
        None => return,
    };

    if guild.leader.to_uppercase() != char_name.to_uppercase() {
        state.send_msg_id(conn_id, 377, "");
        return;
    }

    // Check target in same guild
    let target_conn = state.online_names.get(&target_name.to_uppercase()).copied();
    let target_guild = target_conn.and_then(|c| state.users.get(&c).map(|u| u.guild_index));
    if target_guild != Some(guild_index) {
        state.send_msg_id(conn_id, 511, "");
        return;
    }

    // Check not already sublider
    if guild.sub_lider1.to_uppercase() == target_name.to_uppercase()
        || guild.sub_lider2.to_uppercase() == target_name.to_uppercase()
    {
        state.send_msg_id(conn_id, 510, "");
        return;
    }

    let mut updated = guild;
    if updated.sub_lider1.is_empty() {
        updated.sub_lider1 = target_name.to_string();
    } else if updated.sub_lider2.is_empty() {
        updated.sub_lider2 = target_name.to_string();
    } else {
        state.send_msg_id(conn_id, 513, "");
        return;
    }

    guilds::save_guild(&state.pool, &updated).await;

    state.send_msg_id_to(SendTarget::ToGuildMembers(guild_index), 514, target_name);
}

/// /QSUBLIDR <name> — Demote sub-leader.
pub(super) async fn handle_slash_qsublidr(
    state: &mut GameState,
    conn_id: ConnectionId,
    target_name: &str,
) {
    let (guild_index, char_name) = match state.users.get(&conn_id) {
        Some(u) if u.logged && u.guild_index > 0 => (u.guild_index, u.char_name.clone()),
        _ => return,
    };

    let guild = match guilds::load_guild(&state.pool, guild_index).await {
        Some(g) => g,
        None => return,
    };

    if guild.leader.to_uppercase() != char_name.to_uppercase() {
        state.send_msg_id(conn_id, 377, "");
        return;
    }

    let mut updated = guild;
    if updated.sub_lider1.to_uppercase() == target_name.to_uppercase() {
        updated.sub_lider1 = String::new();
    } else if updated.sub_lider2.to_uppercase() == target_name.to_uppercase() {
        updated.sub_lider2 = String::new();
    } else {
        state.send_msg_id(conn_id, 516, "");
        return;
    }

    guilds::save_guild(&state.pool, &updated).await;

    state.send_msg_id_to(SendTarget::ToGuildMembers(guild_index), 515, target_name);
}

/// /CLAN — List online guild members.
pub(super) async fn handle_slash_clan_list(state: &mut GameState, conn_id: ConnectionId) {
    let guild_index = match state.users.get(&conn_id) {
        Some(u) if u.logged && u.guild_index > 0 => u.guild_index,
        _ => {
            state.send_msg_id(conn_id, 120, "");
            return;
        }
    };

    let guild = match guilds::load_guild(&state.pool, guild_index).await {
        Some(g) => g,
        None => return,
    };

    // List online members
    state.send_console(
        conn_id,
        &format!("Miembros online del clan {}:", guild.name),
        font_index::GUILD_MSG,
    );

    let online_members: Vec<String> = state
        .users
        .values()
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
        state.send_console(conn_id, member, font_index::INFO);
    }
}

/// /SEGUROCLAN — Toggle clan safe mode (prevents attacking clanmates).
pub(super) async fn handle_slash_seguroclan(state: &mut GameState, conn_id: ConnectionId) {
    let _guild_index = match state.users.get(&conn_id) {
        Some(u) if u.logged && u.guild_index > 0 => u.guild_index,
        _ => {
            state.send_console(conn_id, "No perteneces a ningun clan.", font_index::INFO);
            return;
        }
    };

    let new_state = if let Some(user) = state.users.get_mut(&conn_id) {
        user.seguro_clan = !user.seguro_clan;
        user.seguro_clan
    } else {
        return;
    };

    if new_state {
        state.send_console(
            conn_id,
            "Seguro de clan ACTIVADO. No podras atacar a miembros de tu clan.",
            font_index::GUILD_MSG,
        );
    } else {
        state.send_console(
            conn_id,
            "Seguro de clan DESACTIVADO. Ahora puedes atacar a miembros de tu clan.",
            font_index::GUILD_MSG,
        );
    }
}

/// /CMSG <text> — Send clan chat message.
pub(super) async fn handle_slash_cmsg(state: &mut GameState, conn_id: ConnectionId, text: &str) {
    let (guild_index, char_name, map, pos_x, pos_y, char_index) = match state.users.get(&conn_id) {
        Some(u) if u.logged && u.guild_index > 0 => (
            u.guild_index,
            u.char_name.clone(),
            u.pos_map,
            u.pos_x,
            u.pos_y,
            u.char_index,
        ),
        _ => {
            state.send_msg_id(conn_id, 120, "");
            return;
        }
    };

    // No tilde allowed in clan chat
    if text.contains('~') {
        state.send_msg_id(conn_id, 198, "");
        return;
    }

    let guild = match guilds::load_guild(&state.pool, guild_index).await {
        Some(g) => g,
        None => return,
    };

    let is_leader = guild.leader.to_uppercase() == char_name.to_uppercase();
    let is_sublider = guild.sub_lider1.to_uppercase() == char_name.to_uppercase()
        || guild.sub_lider2.to_uppercase() == char_name.to_uppercase();

    let prefix = if is_leader || is_sublider {
        "Lider "
    } else {
        ""
    };

    let msg = format!("{}{}: {}", prefix, char_name, text);
    state.send_guild_chat_to(SendTarget::ToGuildMembers(guild_index), &msg);

    // VB6 13.3 parity: /CMSG also sends yellow overhead bubble to nearby clanmates
    let bubble_pkt = binary_packets::write_chat_over_head(text, char_index.0 as i16, 65535); // vbYellow
    let area_users = state.get_area_users(map, pos_x, pos_y, conn_id);
    for other_id in area_users {
        if let Some(other) = state.users.get(&other_id) {
            if other.guild_index == guild_index {
                state.send_bytes(other_id, &bubble_pkt);
            }
        }
    }
    state.send_bytes(conn_id, &bubble_pkt); // self
}

/// DESCOD — Update guild codex and description.
pub(super) async fn handle_guild_update_codex(
    state: &mut GameState,
    conn_id: ConnectionId,
    payload: &str,
) {
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

    if parts.is_empty() {
        return;
    }

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
pub(super) async fn handle_guild_accept(state: &mut GameState, conn_id: ConnectionId, name: &str) {
    let applicant_name = name.trim().to_string();

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

    // Check member cap (fixed max 50)
    let members = guilds::load_members(&state.pool, &guild.name).await;
    if members.len() >= 50 {
        state.send_msg_id(conn_id, 500, "");
        return;
    }

    // Prevent adding someone who is already a member
    let already_member = members
        .iter()
        .any(|m| m.to_uppercase() == applicant_name.to_uppercase());
    if already_member {
        warn!(
            "[GUILD] handle_guild_accept: '{}' is already a member of guild '{}'",
            applicant_name, guild.name
        );
        guilds::remove_applicant(&state.pool, &guild.name, &applicant_name).await;
        return;
    }

    // Remove from applicants
    guilds::remove_applicant(&state.pool, &guild.name, &applicant_name).await;

    // Add to members
    guilds::add_member(&state.pool, &guild.name, &applicant_name).await;

    // Update applicant's guild in DB
    crate::db::charfile::update_guild_index(&state.pool, &applicant_name, guild_index)
        .await
        .ok();

    // If applicant is online, update their state and refresh CC
    if let Some(&target_conn) = state.online_names.get(&applicant_name.to_uppercase()) {
        if let Some(user) = state.users.get_mut(&target_conn) {
            user.guild_index = guild_index;
            user.guild_name = guild.name.clone();
        }
        state.send_msg_id(target_conn, 501, "");
        refresh_user_cc(state, target_conn).await;

        let sound = binary_packets::write_play_wave(43, 0, 0);
        state.send_bytes(target_conn, &sound);
    }

    // Notify guild
    state.send_msg_id_to(
        SendTarget::ToGuildMembers(guild_index),
        503,
        &applicant_name,
    );
}

/// RECHAZAR — Reject applicant.
pub(super) async fn handle_guild_reject(state: &mut GameState, conn_id: ConnectionId, name: &str) {
    let reject_name = read_field(1, name, ',');
    let reason = read_field(2, name, ',');

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

    guilds::remove_applicant(&state.pool, &guild.name, &reject_name).await;

    // If online, notify
    if let Some(&target_conn) = state.online_names.get(&reject_name.to_uppercase()) {
        state.send_console(
            target_conn,
            &format!("Tu solicitud fue rechazada: {}", reason),
            font_index::INFO,
        );
    }
}

/// ECHARCLA — Expel member from guild.
pub(super) async fn handle_guild_expel(state: &mut GameState, conn_id: ConnectionId, name: &str) {
    let target_name = name.trim().to_string();

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
        state.send_msg_id(conn_id, 377, "");
        return;
    }

    // Cannot expel self (leader)
    if target_name.to_uppercase() == char_name.to_uppercase() {
        return;
    }

    // If target is sublider, clear slot (save once)
    let mut updated = guild.clone();
    let mut sub_changed = false;
    if updated.sub_lider1.to_uppercase() == target_name.to_uppercase() {
        updated.sub_lider1 = String::new();
        sub_changed = true;
    } else if updated.sub_lider2.to_uppercase() == target_name.to_uppercase() {
        updated.sub_lider2 = String::new();
        sub_changed = true;
    }
    if sub_changed {
        guilds::save_guild(&state.pool, &updated).await;
    }

    // Remove from members
    guilds::remove_member(&state.pool, &guild.name, &target_name).await;

    // Update character guild in DB
    crate::db::charfile::update_guild_index(&state.pool, &target_name, 0)
        .await
        .ok();

    // If online, update state and refresh CC
    if let Some(&target_conn) = state.online_names.get(&target_name.to_uppercase()) {
        clear_user_guild(state, target_conn).await;
        state.send_console(
            target_conn,
            "Has sido expulsado del clan.",
            font_index::INFO,
        );
    }

    // Notify guild
    state.send_msg_id_to(SendTarget::ToGuildMembers(guild_index), 505, &target_name);
}

/// ACTGNEWS — Update guild news.
pub(super) async fn handle_guild_news(state: &mut GameState, conn_id: ConnectionId, payload: &str) {
    let news = payload.to_string();

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
pub(super) async fn handle_guild_apply(
    state: &mut GameState,
    conn_id: ConnectionId,
    payload: &str,
) {
    let guild_name = read_field(1, payload, ',');
    let petition = read_field(2, payload, ',');

    let (guild_index, char_name, level) = match state.users.get(&conn_id) {
        Some(u) if u.logged => (u.guild_index, u.char_name.clone(), u.level),
        _ => return,
    };

    if guild_index > 0 {
        state.send_console(conn_id, "Ya perteneces a un clan.", font_index::INFO);
        return;
    }

    if level < 25 {
        state.send_console(
            conn_id,
            "Necesitas nivel 25 para unirte a un clan.",
            font_index::INFO,
        );
        return;
    }

    let target_guild_num = match guilds::find_guild_by_name(&state.pool, &guild_name).await {
        Some(n) => n,
        None => {
            state.send_console(conn_id, "El clan no existe.", font_index::INFO);
            return;
        }
    };

    let _guild = match guilds::load_guild(&state.pool, target_guild_num).await {
        Some(g) => g,
        None => return,
    };

    if !guilds::add_applicant(&state.pool, &guild_name, &char_name, &petition).await {
        state.send_console(
            conn_id,
            "El clan ya tiene el maximo de solicitudes pendientes.",
            font_index::INFO,
        );
        return;
    }

    state.send_msg_id(conn_id, 507, "");
}

/// CLANDETAILS — Request details about a guild.
pub(super) async fn handle_guild_details(state: &mut GameState, conn_id: ConnectionId, name: &str) {
    let guild_name = name.trim().to_string();

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
    let mut data = "1".to_string();
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
    state.send_bytes(conn_id, &binary);
}

// =====================================================================
// Guild Diplomacy — War / Peace / Alliance (VB6: modGuilds.bas)
// =====================================================================

/// Guild relation constants (VB6: RELACIONES_GUILD enum)
pub(crate) const GUILD_REL_WAR: i32 = -1;
pub(crate) const GUILD_REL_PEACE: i32 = 0;
pub(crate) const GUILD_REL_ALLIANCE: i32 = 1;

/// Get normalized key for guild relation lookup (smaller index first)
fn guild_pair(a: i32, b: i32) -> (i32, i32) {
    if a <= b { (a, b) } else { (b, a) }
}

/// Get relation between two guilds (default: peace)
pub fn get_guild_relation(state: &GameState, guild_a: i32, guild_b: i32) -> i32 {
    if guild_a == guild_b {
        return GUILD_REL_ALLIANCE;
    } // Same guild = allies
    state
        .guild_relations
        .get(&guild_pair(guild_a, guild_b))
        .copied()
        .unwrap_or(GUILD_REL_PEACE)
}

/// Set relation between two guilds
fn set_guild_relation(state: &mut GameState, guild_a: i32, guild_b: i32, relation: i32) {
    state
        .guild_relations
        .insert(guild_pair(guild_a, guild_b), relation);
    let pool = state.pool.clone();
    tokio::spawn(async move {
        crate::db::guilds::save_guild_relation(&pool, guild_a, guild_b, relation).await;
    });
}

/// /DECLARARGUERRA <clan> — Declare war on another guild (leader only).
pub(super) async fn handle_slash_declararguerra(
    state: &mut GameState,
    conn_id: ConnectionId,
    target_guild_name: &str,
) {
    let (guild_index, char_name) = match state.users.get(&conn_id) {
        Some(u) if u.logged && u.guild_index > 0 => (u.guild_index, u.char_name.clone()),
        _ => {
            state.send_console(conn_id, "No perteneces a un clan.", font_index::INFO);
            return;
        }
    };

    let guild = match guilds::load_guild(&state.pool, guild_index).await {
        Some(g) => g,
        None => return,
    };

    if guild.leader.to_uppercase() != char_name.to_uppercase() {
        state.send_console(
            conn_id,
            "Solo el lider puede declarar la guerra.",
            font_index::INFO,
        );
        return;
    }

    let target_num = match guilds::find_guild_by_name(&state.pool, target_guild_name).await {
        Some(n) => n,
        None => {
            state.send_console(conn_id, "El clan no existe.", font_index::INFO);
            return;
        }
    };

    if target_num == guild_index {
        state.send_console(
            conn_id,
            "No puedes declarar la guerra a tu propio clan.",
            font_index::INFO,
        );
        return;
    }

    let current = get_guild_relation(state, guild_index, target_num);
    if current == GUILD_REL_WAR {
        state.send_console(
            conn_id,
            "Ya estan en guerra con ese clan.",
            font_index::INFO,
        );
        return;
    }

    // Set war — immediate bilateral
    set_guild_relation(state, guild_index, target_num, GUILD_REL_WAR);

    // Cancel any pending proposals between the two guilds
    state.guild_proposals.remove(&(guild_index, target_num));
    state.guild_proposals.remove(&(target_num, guild_index));

    // Notify both guilds
    let msg_us = format!("Se ha declarado la guerra al clan {}!", target_guild_name);
    let msg_them = format!("El clan {} les ha declarado la guerra!", guild.name);
    state.send_console_to(
        SendTarget::ToGuildMembers(guild_index),
        &msg_us,
        font_index::FIGHT,
    );
    state.send_console_to(
        SendTarget::ToGuildMembers(target_num),
        &msg_them,
        font_index::FIGHT,
    );
}

/// /PROPONERPAZ <clan> — Propose peace to a guild at war (leader only).
pub(super) async fn handle_slash_proponerpaz(
    state: &mut GameState,
    conn_id: ConnectionId,
    target_guild_name: &str,
) {
    let (guild_index, char_name, my_guild_name) = match state.users.get(&conn_id) {
        Some(u) if u.logged && u.guild_index > 0 => {
            (u.guild_index, u.char_name.clone(), u.guild_name.clone())
        }
        _ => {
            state.send_console(conn_id, "No perteneces a un clan.", font_index::INFO);
            return;
        }
    };

    let guild = match guilds::load_guild(&state.pool, guild_index).await {
        Some(g) => g,
        None => return,
    };

    if guild.leader.to_uppercase() != char_name.to_uppercase() {
        state.send_console(
            conn_id,
            "Solo el lider puede proponer la paz.",
            font_index::INFO,
        );
        return;
    }

    let target_num = match guilds::find_guild_by_name(&state.pool, target_guild_name).await {
        Some(n) => n,
        None => {
            state.send_console(conn_id, "El clan no existe.", font_index::INFO);
            return;
        }
    };

    let current = get_guild_relation(state, guild_index, target_num);
    if current != GUILD_REL_WAR {
        state.send_console(
            conn_id,
            "Solo puedes proponer paz a un clan en guerra.",
            font_index::INFO,
        );
        return;
    }

    // Check if target already proposed peace to us — if so, accept
    if state.guild_proposals.get(&(target_num, guild_index)) == Some(&0) {
        // Accept peace
        set_guild_relation(state, guild_index, target_num, GUILD_REL_PEACE);
        state.guild_proposals.remove(&(target_num, guild_index));

        let msg_us = format!("Se ha firmado la paz con el clan {}!", target_guild_name);
        let msg_them = format!("El clan {} ha aceptado la propuesta de paz!", my_guild_name);
        state.send_console_to(
            SendTarget::ToGuildMembers(guild_index),
            &msg_us,
            font_index::GUILD_MSG,
        );
        state.send_console_to(
            SendTarget::ToGuildMembers(target_num),
            &msg_them,
            font_index::GUILD_MSG,
        );
        return;
    }

    // Store proposal
    state.guild_proposals.insert((guild_index, target_num), 0); // 0 = peace

    let msg_them = format!(
        "El clan {} ha propuesto la paz. El lider puede usar /PROPONERPAZ {} para aceptar.",
        my_guild_name, my_guild_name
    );
    state.send_console_to(
        SendTarget::ToGuildMembers(target_num),
        &msg_them,
        font_index::GUILD_MSG,
    );
    state.send_console(
        conn_id,
        &format!("Has propuesto la paz al clan {}.", target_guild_name),
        font_index::INFO,
    );
}

/// /PROPONERALIAR <clan> — Propose alliance to a peaceful guild (leader only).
pub(super) async fn handle_slash_proponeraliar(
    state: &mut GameState,
    conn_id: ConnectionId,
    target_guild_name: &str,
) {
    let (guild_index, char_name, my_guild_name) = match state.users.get(&conn_id) {
        Some(u) if u.logged && u.guild_index > 0 => {
            (u.guild_index, u.char_name.clone(), u.guild_name.clone())
        }
        _ => {
            state.send_console(conn_id, "No perteneces a un clan.", font_index::INFO);
            return;
        }
    };

    let guild = match guilds::load_guild(&state.pool, guild_index).await {
        Some(g) => g,
        None => return,
    };

    if guild.leader.to_uppercase() != char_name.to_uppercase() {
        state.send_console(
            conn_id,
            "Solo el lider puede proponer alianza.",
            font_index::INFO,
        );
        return;
    }

    let target_num = match guilds::find_guild_by_name(&state.pool, target_guild_name).await {
        Some(n) => n,
        None => {
            state.send_console(conn_id, "El clan no existe.", font_index::INFO);
            return;
        }
    };

    let current = get_guild_relation(state, guild_index, target_num);
    if current != GUILD_REL_PEACE {
        state.send_console(
            conn_id,
            "Solo puedes proponer alianza a un clan en paz.",
            font_index::INFO,
        );
        return;
    }

    // Check if target already proposed alliance to us — if so, accept
    if state.guild_proposals.get(&(target_num, guild_index)) == Some(&1) {
        // Accept alliance
        set_guild_relation(state, guild_index, target_num, GUILD_REL_ALLIANCE);
        state.guild_proposals.remove(&(target_num, guild_index));

        let msg_us = format!(
            "Se ha formado una alianza con el clan {}!",
            target_guild_name
        );
        let msg_them = format!(
            "El clan {} ha aceptado la propuesta de alianza!",
            my_guild_name
        );
        state.send_console_to(
            SendTarget::ToGuildMembers(guild_index),
            &msg_us,
            font_index::GUILD_MSG,
        );
        state.send_console_to(
            SendTarget::ToGuildMembers(target_num),
            &msg_them,
            font_index::GUILD_MSG,
        );
        return;
    }

    // Store proposal
    state.guild_proposals.insert((guild_index, target_num), 1); // 1 = alliance

    let msg_them = format!(
        "El clan {} ha propuesto una alianza. El lider puede usar /PROPONERALIAR {} para aceptar.",
        my_guild_name, my_guild_name
    );
    state.send_console_to(
        SendTarget::ToGuildMembers(target_num),
        &msg_them,
        font_index::GUILD_MSG,
    );
    state.send_console(
        conn_id,
        &format!("Has propuesto alianza al clan {}.", target_guild_name),
        font_index::INFO,
    );
}

/// /ROMPERALIANZA <clan> — Break alliance with a guild (leader only).
pub(super) async fn handle_slash_romperalianza(
    state: &mut GameState,
    conn_id: ConnectionId,
    target_guild_name: &str,
) {
    let (guild_index, char_name, my_guild_name) = match state.users.get(&conn_id) {
        Some(u) if u.logged && u.guild_index > 0 => {
            (u.guild_index, u.char_name.clone(), u.guild_name.clone())
        }
        _ => {
            state.send_console(conn_id, "No perteneces a un clan.", font_index::INFO);
            return;
        }
    };

    let guild = match guilds::load_guild(&state.pool, guild_index).await {
        Some(g) => g,
        None => return,
    };

    if guild.leader.to_uppercase() != char_name.to_uppercase() {
        state.send_console(
            conn_id,
            "Solo el lider puede romper alianzas.",
            font_index::INFO,
        );
        return;
    }

    let target_num = match guilds::find_guild_by_name(&state.pool, target_guild_name).await {
        Some(n) => n,
        None => {
            state.send_console(conn_id, "El clan no existe.", font_index::INFO);
            return;
        }
    };

    let current = get_guild_relation(state, guild_index, target_num);
    if current != GUILD_REL_ALLIANCE {
        state.send_console(conn_id, "No estan aliados con ese clan.", font_index::INFO);
        return;
    }

    // Revert to peace
    set_guild_relation(state, guild_index, target_num, GUILD_REL_PEACE);

    let msg_us = format!("Se ha roto la alianza con el clan {}.", target_guild_name);
    let msg_them = format!("El clan {} ha roto la alianza.", my_guild_name);
    state.send_console_to(
        SendTarget::ToGuildMembers(guild_index),
        &msg_us,
        font_index::INFO,
    );
    state.send_console_to(
        SendTarget::ToGuildMembers(target_num),
        &msg_them,
        font_index::INFO,
    );
}

/// /RELACIONES — Show current guild diplomacy status (all relations).
pub(super) async fn handle_slash_relaciones(state: &mut GameState, conn_id: ConnectionId) {
    let guild_index = match state.users.get(&conn_id) {
        Some(u) if u.logged && u.guild_index > 0 => u.guild_index,
        _ => {
            state.send_console(conn_id, "No perteneces a un clan.", font_index::INFO);
            return;
        }
    };

    let mut wars = Vec::new();
    let mut allies = Vec::new();

    for (&(a, b), &rel) in &state.guild_relations {
        if a != guild_index && b != guild_index {
            continue;
        }
        let other = if a == guild_index { b } else { a };
        let other_name = guilds::load_guild(&state.pool, other)
            .await
            .map(|g| g.name)
            .unwrap_or_else(|| format!("Clan #{}", other));

        match rel {
            GUILD_REL_WAR => wars.push(other_name),
            GUILD_REL_ALLIANCE => allies.push(other_name),
            _ => {}
        }
    }

    state.send_console(
        conn_id,
        "--- Relaciones del clan ---",
        font_index::GUILD_MSG,
    );
    if wars.is_empty() {
        state.send_console(conn_id, "En guerra con: nadie", font_index::INFO);
    } else {
        state.send_console(
            conn_id,
            &format!("En guerra con: {}", wars.join(", ")),
            font_index::FIGHT,
        );
    }
    if allies.is_empty() {
        state.send_console(conn_id, "Aliados con: nadie", font_index::INFO);
    } else {
        state.send_console(
            conn_id,
            &format!("Aliados con: {}", allies.join(", ")),
            font_index::GUILD_MSG,
        );
    }
}

/// VB6 13.3 parity: at level 25, expel user from faction guild (Armada/Caos pretoriano guilds).
/// Faction guilds are identified by alignment ALIGN_ARMADA (5) or ALIGN_LEGION (1).
/// Called from check_user_level when new_level == 25.
pub(super) async fn expel_from_faction_guild_at_25(state: &mut GameState, conn_id: ConnectionId) {
    let (guild_index, char_name) = match state.users.get(&conn_id) {
        Some(u) if u.logged && u.guild_index > 0 => (u.guild_index, u.char_name.clone()),
        _ => return,
    };

    let guild = match guilds::load_guild(&state.pool, guild_index).await {
        Some(g) => g,
        None => return,
    };

    // Only expel from faction guilds (Armada Real or Legion del Caos).
    if guild.alignment != guilds::ALIGN_ARMADA && guild.alignment != guilds::ALIGN_LEGION {
        return;
    }

    // If sublider, clear the slot before removal.
    let is_sub1 = guild.sub_lider1.to_uppercase() == char_name.to_uppercase();
    let is_sub2 = guild.sub_lider2.to_uppercase() == char_name.to_uppercase();
    if is_sub1 || is_sub2 {
        let mut updated = guild.clone();
        if is_sub1 {
            updated.sub_lider1 = String::new();
        }
        if is_sub2 {
            updated.sub_lider2 = String::new();
        }
        guilds::save_guild(&state.pool, &updated).await;
    }

    // Remove from members DB and clear guild_index in charfile.
    guilds::remove_member(&state.pool, &guild.name, &char_name).await;
    crate::db::charfile::update_guild_index(&state.pool, &char_name, 0)
        .await
        .ok();

    // Update online user state and refresh CC.
    clear_user_guild(state, conn_id).await;

    // Notify the player.
    state.send_console(
        conn_id,
        "Has alcanzado el nivel 25 y has sido expulsado del clan de faccion.",
        font_index::INFO,
    );
}
