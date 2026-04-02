//! Core binary packets: Auth/Login, Map/Position, Character, Objects, Chat, Stats.

use crate::protocol::byte_queue::ByteQueue;
use crate::protocol::packets::ServerPacketID;

// ── Auth / Login ───────────────────────────────────────────

/// ID 0: Login successful. Includes coord cipher seed for anti-cheat.
pub fn write_logged(class: u8, coord_seed: u32) -> Vec<u8> {
    let mut pkt = ByteQueue::new();
    pkt.write_byte(ServerPacketID::Logged.to_byte());
    pkt.write_byte(class);
    pkt.write_long(coord_seed as i32);
    pkt.into_bytes()
}

/// ID 2: Error message (disconnects after showing).
pub fn write_error_msg(msg: &str) -> Vec<u8> {
    let mut pkt = ByteQueue::new();
    pkt.write_byte(ServerPacketID::ErrorMsg.to_byte());
    pkt.write_ascii_string(msg);
    pkt.into_bytes()
}

/// ID 5: User char index in server.
pub fn write_user_char_index(index: i16) -> Vec<u8> {
    let mut pkt = ByteQueue::new();
    pkt.write_byte(ServerPacketID::UserCharIndexInServer.to_byte());
    pkt.write_integer(index);
    pkt.into_bytes()
}

/// ID 59: Dice roll result for character creation.
pub fn write_dice_roll(str_: u8, agi: u8, int: u8, con: u8, cha: u8) -> Vec<u8> {
    let mut pkt = ByteQueue::new();
    pkt.write_byte(ServerPacketID::DiceRoll.to_byte());
    pkt.write_byte(str_);
    pkt.write_byte(agi);
    pkt.write_byte(int);
    pkt.write_byte(con);
    pkt.write_byte(cha);
    pkt.into_bytes()
}

// ── Map / Position ─────────────────────────────────────────

/// ID 21: Change map.
pub fn write_change_map(map_num: i16, map_version: i16, r: u8, g: u8, b: u8) -> Vec<u8> {
    let mut pkt = ByteQueue::new();
    pkt.write_byte(ServerPacketID::ChangeMap.to_byte());
    pkt.write_integer(map_num);
    pkt.write_integer(map_version);
    pkt.write_byte(r);
    pkt.write_byte(g);
    pkt.write_byte(b);
    pkt.into_bytes()
}

/// ID 22: Position update.
pub fn write_pos_update(x: i16, y: i16) -> Vec<u8> {
    let mut pkt = ByteQueue::new();
    pkt.write_byte(ServerPacketID::PosUpdate.to_byte());
    pkt.write_integer(x);
    pkt.write_integer(y);
    pkt.into_bytes()
}

/// ID 40: Area changed.
pub fn write_area_changed(x: i16, y: i16) -> Vec<u8> {
    let mut pkt = ByteQueue::new();
    pkt.write_byte(ServerPacketID::AreaChanged.to_byte());
    pkt.write_integer(x);
    pkt.write_integer(y);
    pkt.into_bytes()
}

// ── Character ──────────────────────────────────────────────

/// ID 29: Character create (appears in view).
pub fn write_character_create(
    char_index: i16, body: i16, head: i16, heading: u8,
    x: i16, y: i16, weapon: i16, shield: i16, helmet: i16,
    fx_index: i16, fx_loops: i16, name: &str,
    nick_color: u8, privileges: u8,
) -> Vec<u8> {
    let mut pkt = ByteQueue::new();
    pkt.write_byte(ServerPacketID::CharacterCreate.to_byte());
    pkt.write_integer(char_index);
    pkt.write_integer(body);
    pkt.write_integer(head);
    pkt.write_byte(heading);
    pkt.write_integer(x);
    pkt.write_integer(y);
    pkt.write_integer(weapon);
    pkt.write_integer(shield);
    pkt.write_integer(helmet);
    pkt.write_integer(fx_index);
    pkt.write_integer(fx_loops);
    pkt.write_ascii_string(name);
    pkt.write_byte(nick_color);
    pkt.write_byte(privileges);
    pkt.into_bytes()
}

/// ID 30: Character remove.
pub fn write_character_remove(char_index: i16) -> Vec<u8> {
    let mut pkt = ByteQueue::new();
    pkt.write_byte(ServerPacketID::CharacterRemove.to_byte());
    pkt.write_integer(char_index);
    pkt.into_bytes()
}

/// ID 31: Character move.
pub fn write_character_move(char_index: i16, x: i16, y: i16) -> Vec<u8> {
    let mut pkt = ByteQueue::new();
    pkt.write_byte(ServerPacketID::CharacterMove.to_byte());
    pkt.write_integer(char_index);
    pkt.write_integer(x);
    pkt.write_integer(y);
    pkt.into_bytes()
}

/// ID 33: Character change (appearance update).
pub fn write_character_change(
    char_index: i16, body: i16, head: i16, heading: u8,
    weapon: i16, shield: i16, helmet: i16,
    fx_index: i16, fx_loops: i16,
) -> Vec<u8> {
    let mut pkt = ByteQueue::new();
    pkt.write_byte(ServerPacketID::CharacterChange.to_byte());
    pkt.write_integer(char_index);
    pkt.write_integer(body);
    pkt.write_integer(head);
    pkt.write_byte(heading);
    pkt.write_integer(weapon);
    pkt.write_integer(shield);
    pkt.write_integer(helmet);
    pkt.write_integer(fx_index);
    pkt.write_integer(fx_loops);
    pkt.into_bytes()
}

/// ID 66: Set invisible.
pub fn write_set_invisible(char_index: i16, invisible: bool, duration_secs: i16) -> Vec<u8> {
    let mut pkt = ByteQueue::new();
    pkt.write_byte(ServerPacketID::SetInvisible.to_byte());
    pkt.write_integer(char_index);
    pkt.write_boolean(invisible);
    pkt.write_integer(duration_secs); // 0 = permanent (GM), >0 = spell countdown in seconds
    pkt.into_bytes()
}

/// ID 43: Create FX on character.
pub fn write_create_fx(char_index: i16, fx_index: i16, fx_loops: i16) -> Vec<u8> {
    let mut pkt = ByteQueue::new();
    pkt.write_byte(ServerPacketID::CreateFX.to_byte());
    pkt.write_integer(char_index);
    pkt.write_integer(fx_index);
    pkt.write_integer(fx_loops);
    pkt.into_bytes()
}

// ── Objects on ground ──────────────────────────────────────

/// ID 34: Object create on ground.
pub fn write_object_create(x: i16, y: i16, grh_index: i16) -> Vec<u8> {
    let mut pkt = ByteQueue::new();
    pkt.write_byte(ServerPacketID::ObjectCreate.to_byte());
    pkt.write_integer(x);
    pkt.write_integer(y);
    pkt.write_integer(grh_index);
    pkt.into_bytes()
}

/// ID 35: Object delete from ground.
pub fn write_object_delete(x: i16, y: i16) -> Vec<u8> {
    let mut pkt = ByteQueue::new();
    pkt.write_byte(ServerPacketID::ObjectDelete.to_byte());
    pkt.write_integer(x);
    pkt.write_integer(y);
    pkt.into_bytes()
}

/// ID 36: Block position update.
pub fn write_block_position(x: i16, y: i16, blocked: bool) -> Vec<u8> {
    let mut pkt = ByteQueue::new();
    pkt.write_byte(ServerPacketID::BlockPosition.to_byte());
    pkt.write_integer(x);
    pkt.write_integer(y);
    pkt.write_boolean(blocked);
    pkt.into_bytes()
}

// ── Chat ───────────────────────────────────────────────────

/// ID 23: Chat over head (speech bubble).
pub fn write_chat_over_head(chat: &str, char_index: i16, color: i32) -> Vec<u8> {
    let mut pkt = ByteQueue::new();
    pkt.write_byte(ServerPacketID::ChatOverHead.to_byte());
    pkt.write_ascii_string(chat);
    pkt.write_integer(char_index);
    pkt.write_long(color);
    pkt.into_bytes()
}

/// ID 24: Console message.
pub fn write_console_msg(chat: &str, font_index: u8) -> Vec<u8> {
    let mut pkt = ByteQueue::new();
    pkt.write_byte(ServerPacketID::ConsoleMsg.to_byte());
    pkt.write_ascii_string(chat);
    pkt.write_byte(font_index);
    pkt.into_bytes()
}

/// ID 110: Chat talk (area chat with color).
pub fn write_chat_talk(char_index: i16, msg: &str, color: i32) -> Vec<u8> {
    let mut pkt = ByteQueue::new();
    pkt.write_byte(ServerPacketID::ChatTalk.to_byte());
    pkt.write_integer(char_index);
    pkt.write_ascii_string(msg);
    pkt.write_long(color);
    pkt.into_bytes()
}

/// ID 112: Chat whisper (private message).
pub fn write_chat_whisper(msg: &str, font_index: u8) -> Vec<u8> {
    let mut pkt = ByteQueue::new();
    pkt.write_byte(ServerPacketID::ChatWhisper.to_byte());
    pkt.write_ascii_string(msg);
    pkt.write_byte(font_index);
    pkt.into_bytes()
}

/// ID 129: Online count.
pub fn write_online_count(count: i16) -> Vec<u8> {
    let mut pkt = ByteQueue::new();
    pkt.write_byte(ServerPacketID::OnlineCount.to_byte());
    pkt.write_integer(count);
    pkt.into_bytes()
}

/// ID 115: Console message by text ID (Textos.ao lookup).
/// Args are @-separated strings that substitute %1, %2, ... in the template.
pub fn write_console_msg_id(msg_id: i16, args: &str) -> Vec<u8> {
    let mut pkt = ByteQueue::new();
    pkt.write_byte(ServerPacketID::ConsoleMsgId.to_byte());
    pkt.write_integer(msg_id);
    pkt.write_ascii_string(args);
    pkt.into_bytes()
}

/// ID 116: GM broadcast (!! message).
pub fn write_gm_broadcast(msg: &str) -> Vec<u8> {
    let mut pkt = ByteQueue::new();
    pkt.write_byte(ServerPacketID::GmBroadcast.to_byte());
    pkt.write_ascii_string(msg);
    pkt.into_bytes()
}

/// ID 25: Guild chat.
pub fn write_guild_chat(chat: &str) -> Vec<u8> {
    let mut pkt = ByteQueue::new();
    pkt.write_byte(ServerPacketID::GuildChat.to_byte());
    pkt.write_ascii_string(chat);
    pkt.into_bytes()
}

// ── Stats ──────────────────────────────────────────────────

/// ID 18: Update HP (VB6: [H]MaxHP,MinHP).
pub fn write_update_hp(max_hp: i16, min_hp: i16) -> Vec<u8> {
    let mut pkt = ByteQueue::new();
    pkt.write_byte(ServerPacketID::UpdateHP.to_byte());
    pkt.write_integer(max_hp);
    pkt.write_integer(min_hp);
    pkt.into_bytes()
}

/// ID 17: Update Mana (VB6: [M]MaxMAN,MinMAN).
pub fn write_update_mana(max_mana: i16, min_mana: i16) -> Vec<u8> {
    let mut pkt = ByteQueue::new();
    pkt.write_byte(ServerPacketID::UpdateMana.to_byte());
    pkt.write_integer(max_mana);
    pkt.write_integer(min_mana);
    pkt.into_bytes()
}

/// ID 16: Update Stamina (VB6: [E]MaxSta,MinSta).
pub fn write_update_sta(max_sta: i16, min_sta: i16) -> Vec<u8> {
    let mut pkt = ByteQueue::new();
    pkt.write_byte(ServerPacketID::UpdateSta.to_byte());
    pkt.write_integer(max_sta);
    pkt.write_integer(min_sta);
    pkt.into_bytes()
}

/// ID 19: Update Gold.
pub fn write_update_gold(gold: i32) -> Vec<u8> {
    let mut pkt = ByteQueue::new();
    pkt.write_byte(ServerPacketID::UpdateGold.to_byte());
    pkt.write_long(gold);
    pkt.into_bytes()
}

/// ID 102: Update Bank Gold.
pub fn write_update_bank_gold(bank_gold: i32) -> Vec<u8> {
    let mut pkt = ByteQueue::new();
    pkt.write_byte(ServerPacketID::UpdateBankGold.to_byte());
    pkt.write_long(bank_gold);
    pkt.into_bytes()
}

/// ID 20: Update Exp.
pub fn write_update_exp(exp: i32) -> Vec<u8> {
    let mut pkt = ByteQueue::new();
    pkt.write_byte(ServerPacketID::UpdateExp.to_byte());
    pkt.write_long(exp);
    pkt.into_bytes()
}

/// ID 44: Update full user stats (login bulk).
pub fn write_update_user_stats(
    max_hp: i16, min_hp: i16, max_mana: i16, min_mana: i16,
    max_sta: i16, min_sta: i16, gold: i32, level: u8,
    exp_to_next: i32, current_exp: i32,
) -> Vec<u8> {
    let mut pkt = ByteQueue::new();
    pkt.write_byte(ServerPacketID::UpdateUserStats.to_byte());
    pkt.write_integer(max_hp);
    pkt.write_integer(min_hp);
    pkt.write_integer(max_mana);
    pkt.write_integer(min_mana);
    pkt.write_integer(max_sta);
    pkt.write_integer(min_sta);
    pkt.write_long(gold);
    pkt.write_byte(level);
    pkt.write_long(exp_to_next);
    pkt.write_long(current_exp);
    pkt.into_bytes()
}

/// ID 49: Send player attributes (STR, AGI, INT, CON, CHA).
pub fn write_attributes(str_: u8, agi: u8, int: u8, con: u8, cha: u8) -> Vec<u8> {
    let mut pkt = ByteQueue::new();
    pkt.write_byte(ServerPacketID::Attributes.to_byte());
    pkt.write_byte(str_);
    pkt.write_byte(agi);
    pkt.write_byte(int);
    pkt.write_byte(con);
    pkt.write_byte(cha);
    pkt.into_bytes()
}

/// ID 50: Send all 22 skills (level + XP percentage per skill).
pub fn write_send_skills(skills: &[i32], exp_skills: &[i32], elu_skills: &[i32]) -> Vec<u8> {
    let mut pkt = ByteQueue::new();
    pkt.write_byte(ServerPacketID::SendSkills.to_byte());
    for i in 0..22 {
        let level = if i < skills.len() { skills[i].clamp(0, 100) as u8 } else { 0 };
        let pct = if i < exp_skills.len() && i < elu_skills.len() && elu_skills[i] > 0 {
            ((exp_skills[i] as f64 / elu_skills[i] as f64) * 100.0).clamp(0.0, 99.0) as u8
        } else {
            0
        };
        pkt.write_byte(level);
        pkt.write_byte(pct);
    }
    pkt.into_bytes()
}

/// ID 60: Update hunger and thirst.
pub fn write_update_hunger_thirst(max_agua: u8, min_agua: u8, max_ham: u8, min_ham: u8) -> Vec<u8> {
    let mut pkt = ByteQueue::new();
    pkt.write_byte(ServerPacketID::UpdateHungerAndThirst.to_byte());
    pkt.write_byte(max_agua);
    pkt.write_byte(min_agua);
    pkt.write_byte(max_ham);
    pkt.write_byte(min_ham);
    pkt.into_bytes()
}

/// ID 63: Level up notification.
pub fn write_level_up(skill_points: i16) -> Vec<u8> {
    let mut pkt = ByteQueue::new();
    pkt.write_byte(ServerPacketID::LevelUp.to_byte());
    pkt.write_integer(skill_points);
    pkt.into_bytes()
}



/// ID 252: Zone change notification.
/// Sent when player enters a new zone or on login/map change.
/// Wire: string zone_name, byte zone_type, byte is_safe, i16 music,
///       byte lluvia, byte nieve, byte niebla,
///       i16 x1, i16 y1, i16 x2, i16 y2 (zone bounds for client fog rendering)
pub fn write_zone_change(
    zone_name: &str, zone_type: u8, is_safe: bool, music: i16,
    lluvia: bool, nieve: bool, niebla: bool,
    x1: i16, y1: i16, x2: i16, y2: i16,
) -> Vec<u8> {
    let mut pkt = ByteQueue::new();
    pkt.write_byte(ServerPacketID::ZoneChange.to_byte());
    pkt.write_ascii_string(zone_name);
    pkt.write_byte(zone_type);
    pkt.write_byte(is_safe as u8);
    pkt.write_integer(music);
    pkt.write_byte(lluvia as u8);
    pkt.write_byte(nieve as u8);
    pkt.write_byte(niebla as u8);
    pkt.write_integer(x1);
    pkt.write_integer(y1);
    pkt.write_integer(x2);
    pkt.write_integer(y2);
    pkt.into_bytes()
}

/// ID 252: Zone change to "wilderness" (no zone — map defaults).
pub fn write_zone_change_wilderness(map_name: &str, is_safe: bool, music: i16) -> Vec<u8> {
    write_zone_change(map_name, 0, is_safe, music, false, false, false, 0, 0, 0, 0)
}
