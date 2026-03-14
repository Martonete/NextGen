//! Inventory, Commerce, Crafting, Toggles/Status, Guild, Signals binary packets.

use crate::protocol::byte_queue::ByteQueue;
use crate::protocol::packets::ServerPacketID;

// ── Inventory / Spells ─────────────────────────────────────

/// ID 47: Change inventory slot.
pub fn write_change_inventory_slot(
    slot: u8, obj_index: i16, name: &str, amount: i16,
    equipped: bool, grh_index: i16, obj_type: u8,
    max_hit: i16, min_hit: i16, max_def: i16, min_def: i16, value: f32,
) -> Vec<u8> {
    let mut pkt = ByteQueue::new();
    pkt.write_byte(ServerPacketID::ChangeInventorySlot.to_byte());
    pkt.write_byte(slot);
    pkt.write_integer(obj_index);
    pkt.write_ascii_string(name);
    pkt.write_integer(amount);
    pkt.write_boolean(equipped);
    pkt.write_integer(grh_index);
    pkt.write_byte(obj_type);
    pkt.write_integer(max_hit);
    pkt.write_integer(min_hit);
    pkt.write_integer(max_def);
    pkt.write_integer(min_def);
    pkt.write_single(value);
    pkt.into_bytes()
}

/// ID 48: Change bank slot.
pub fn write_change_bank_slot(
    slot: u8, obj_index: i16, name: &str, amount: i16,
    equipped: bool, grh_index: i16, obj_type: u8,
    max_hit: i16, min_hit: i16, max_def: i16, min_def: i16, value: f32,
) -> Vec<u8> {
    let mut pkt = ByteQueue::new();
    pkt.write_byte(ServerPacketID::ChangeBankSlot.to_byte());
    pkt.write_byte(slot);
    pkt.write_integer(obj_index);
    pkt.write_ascii_string(name);
    pkt.write_integer(amount);
    pkt.write_boolean(equipped);
    pkt.write_integer(grh_index);
    pkt.write_byte(obj_type);
    pkt.write_integer(max_hit);
    pkt.write_integer(min_hit);
    pkt.write_integer(max_def);
    pkt.write_integer(min_def);
    pkt.write_single(value);
    pkt.into_bytes()
}

/// ID 49: Change spell slot.
pub fn write_change_spell_slot(slot: u8, spell_index: i16, name: &str) -> Vec<u8> {
    let mut pkt = ByteQueue::new();
    pkt.write_byte(ServerPacketID::ChangeSpellSlot.to_byte());
    pkt.write_byte(slot);
    pkt.write_integer(spell_index);
    pkt.write_ascii_string(name);
    pkt.into_bytes()
}

/// ID 59: Change NPC inventory slot.
pub fn write_change_npc_inv_slot(
    slot: u8, name: &str, amount: i16, value: f32,
    grh_index: i16, obj_index: i16, obj_type: u8,
    max_hit: i16, min_hit: i16, max_def: i16, min_def: i16,
) -> Vec<u8> {
    let mut pkt = ByteQueue::new();
    pkt.write_byte(ServerPacketID::ChangeNPCInventorySlot.to_byte());
    pkt.write_byte(slot);
    pkt.write_ascii_string(name);
    pkt.write_integer(amount);
    pkt.write_single(value);
    pkt.write_integer(grh_index);
    pkt.write_integer(obj_index);
    pkt.write_byte(obj_type);
    pkt.write_integer(max_hit);
    pkt.write_integer(min_hit);
    pkt.write_integer(max_def);
    pkt.write_integer(min_def);
    pkt.into_bytes()
}

// ── Sound / Music ──────────────────────────────────────────

/// ID 37: Play music.
pub fn write_play_midi(midi_index: u8) -> Vec<u8> {
    let mut pkt = ByteQueue::new();
    pkt.write_byte(ServerPacketID::PlayMusic.to_byte());
    pkt.write_byte(midi_index);
    pkt.into_bytes()
}

/// ID 39: Play wave (sound effect).
pub fn write_play_wave(wave_index: u8, x: u8, y: u8) -> Vec<u8> {
    let mut pkt = ByteQueue::new();
    pkt.write_byte(ServerPacketID::PlayWave.to_byte());
    pkt.write_byte(wave_index);
    pkt.write_byte(x);
    pkt.write_byte(y);
    pkt.into_bytes()
}

// ── Commerce / Bank / Trade ────────────────────────────────

/// ID 5: Commerce end.
pub fn write_commerce_end() -> Vec<u8> {
    let mut pkt = ByteQueue::new();
    pkt.write_byte(ServerPacketID::CommerceEnd.to_byte());
    pkt.into_bytes()
}

/// ID 6: Bank end.
pub fn write_bank_end() -> Vec<u8> {
    let mut pkt = ByteQueue::new();
    pkt.write_byte(ServerPacketID::BankEnd.to_byte());
    pkt.into_bytes()
}

/// ID 7: Commerce init.
pub fn write_commerce_init() -> Vec<u8> {
    let mut pkt = ByteQueue::new();
    pkt.write_byte(ServerPacketID::CommerceInit.to_byte());
    pkt.into_bytes()
}

/// ID 8: Bank init.
pub fn write_bank_init(bank_gold: i32) -> Vec<u8> {
    let mut pkt = ByteQueue::new();
    pkt.write_byte(ServerPacketID::BankInit.to_byte());
    pkt.write_long(bank_gold);
    pkt.into_bytes()
}

/// ID 9: User commerce init.
pub fn write_user_commerce_init(other_name: &str) -> Vec<u8> {
    let mut pkt = ByteQueue::new();
    pkt.write_byte(ServerPacketID::UserCommerceInit.to_byte());
    pkt.write_ascii_string(other_name);
    pkt.into_bytes()
}

/// ID 10: User commerce end.
pub fn write_user_commerce_end() -> Vec<u8> {
    let mut pkt = ByteQueue::new();
    pkt.write_byte(ServerPacketID::UserCommerceEnd.to_byte());
    pkt.into_bytes()
}

/// ID 12: Commerce chat message.
pub fn write_commerce_chat(chat: &str) -> Vec<u8> {
    let mut pkt = ByteQueue::new();
    pkt.write_byte(ServerPacketID::CommerceChat.to_byte());
    pkt.write_ascii_string(chat);
    pkt.into_bytes()
}

/// ID 84: Trade OK.
pub fn write_trade_ok() -> Vec<u8> {
    let mut pkt = ByteQueue::new();
    pkt.write_byte(ServerPacketID::TradeOK.to_byte());
    pkt.into_bytes()
}

/// ID 85: Bank OK.
pub fn write_bank_ok() -> Vec<u8> {
    let mut pkt = ByteQueue::new();
    pkt.write_byte(ServerPacketID::BankOK.to_byte());
    pkt.into_bytes()
}

// ── Crafting ───────────────────────────────────────────────

/// ID 13: Show blacksmith form.
pub fn write_show_blacksmith_form() -> Vec<u8> {
    let mut pkt = ByteQueue::new();
    pkt.write_byte(ServerPacketID::ShowBlacksmithForm.to_byte());
    pkt.into_bytes()
}

/// ID 14: Show carpenter form.
pub fn write_show_carpenter_form() -> Vec<u8> {
    let mut pkt = ByteQueue::new();
    pkt.write_byte(ServerPacketID::ShowCarpenterForm.to_byte());
    pkt.into_bytes()
}

/// ID 46: Work request target.
pub fn write_work_request_target(skill_type: u8) -> Vec<u8> {
    let mut pkt = ByteQueue::new();
    pkt.write_byte(ServerPacketID::WorkRequestTarget.to_byte());
    pkt.write_byte(skill_type);
    pkt.into_bytes()
}

// ── Toggles / Status ───────────────────────────────────────

/// ID 1: Remove dialogs.
pub fn write_remove_dialogs() -> Vec<u8> {
    let mut pkt = ByteQueue::new();
    pkt.write_byte(ServerPacketID::RemoveDialogs.to_byte());
    pkt.into_bytes()
}

/// ID 2: Remove char dialog.
pub fn write_remove_char_dialog(char_index: i16) -> Vec<u8> {
    let mut pkt = ByteQueue::new();
    pkt.write_byte(ServerPacketID::RemoveCharDialog.to_byte());
    pkt.write_integer(char_index);
    pkt.into_bytes()
}

/// ID 3: Navigate toggle.
pub fn write_navigate_toggle() -> Vec<u8> {
    let mut pkt = ByteQueue::new();
    pkt.write_byte(ServerPacketID::NavigateToggle.to_byte());
    pkt.into_bytes()
}

/// ID 42: Pause toggle.
pub fn write_pause_toggle() -> Vec<u8> {
    let mut pkt = ByteQueue::new();
    pkt.write_byte(ServerPacketID::PauseToggle.to_byte());
    pkt.into_bytes()
}

/// ID 43: Rain toggle.
pub fn write_rain_toggle() -> Vec<u8> {
    let mut pkt = ByteQueue::new();
    pkt.write_byte(ServerPacketID::RainToggle.to_byte());
    pkt.into_bytes()
}

/// ID 54: Rest OK.
pub fn write_rest_ok() -> Vec<u8> {
    let mut pkt = ByteQueue::new();
    pkt.write_byte(ServerPacketID::RestToggle.to_byte());
    pkt.into_bytes()
}

/// ID 68: Meditate toggle.
pub fn write_meditate_toggle() -> Vec<u8> {
    let mut pkt = ByteQueue::new();
    pkt.write_byte(ServerPacketID::MeditateToggle.to_byte());
    pkt.into_bytes()
}

/// ID 82: Paralyze OK.
pub fn write_paralize_ok(duration_secs: i16) -> Vec<u8> {
    let mut pkt = ByteQueue::new();
    pkt.write_byte(ServerPacketID::ParalyzeOK.to_byte());
    pkt.write_integer(duration_secs); // 0 = toggle off, >0 = paralysis countdown in seconds
    pkt.into_bytes()
}

/// ID 88: Pong.
pub fn write_pong() -> Vec<u8> {
    let mut pkt = ByteQueue::new();
    pkt.write_byte(ServerPacketID::Pong.to_byte());
    pkt.into_bytes()
}

/// ID 89: Update tag and status.
pub fn write_update_tag_and_status(char_index: i16, nick_color: u8, tag: &str) -> Vec<u8> {
    let mut pkt = ByteQueue::new();
    pkt.write_byte(ServerPacketID::UpdateTagAndStatus.to_byte());
    pkt.write_integer(char_index);
    pkt.write_byte(nick_color);
    pkt.write_ascii_string(tag);
    pkt.into_bytes()
}

// ── Guild ──────────────────────────────────────────────────

/// ID 40: Guild list.
pub fn write_guild_list(guild_names: &str) -> Vec<u8> {
    let mut pkt = ByteQueue::new();
    pkt.write_byte(ServerPacketID::GuildList.to_byte());
    pkt.write_ascii_string(guild_names);
    pkt.into_bytes()
}

/// ID 83: Show guild foundation form.
pub fn write_show_guild_fundation_form() -> Vec<u8> {
    let mut pkt = ByteQueue::new();
    pkt.write_byte(ServerPacketID::ShowGuildFoundationForm.to_byte());
    pkt.into_bytes()
}

// ── Signals / Forum / NPC lists ────────────────────────────

/// ID 117: AddForumMsg — send a forum post to the client.
/// VB6: WriteByte(ForumType) + WriteASCIIString(Title) + WriteASCIIString(Author) + WriteASCIIString(Message)
pub fn write_add_forum_msg(forum_type: u8, title: &str, author: &str, message: &str) -> Vec<u8> {
    let mut pkt = ByteQueue::new();
    pkt.write_byte(ServerPacketID::AddForumMsg.to_byte());
    pkt.write_byte(forum_type);
    pkt.write_ascii_string(title);
    pkt.write_ascii_string(author);
    pkt.write_ascii_string(message);
    pkt.into_bytes()
}

/// ID 118: ShowForumForm — open the forum UI on the client.
/// VB6: WriteByte(Visibility) + WriteByte(CanMakeSticky)
pub fn write_show_forum_form(visibility: u8, can_make_sticky: u8) -> Vec<u8> {
    let mut pkt = ByteQueue::new();
    pkt.write_byte(ServerPacketID::ShowForumForm.to_byte());
    pkt.write_byte(visibility);
    pkt.write_byte(can_make_sticky);
    pkt.into_bytes()
}

/// ID 72: Trainer creature list.
pub fn write_trainer_creature_list(creatures: &str) -> Vec<u8> {
    let mut pkt = ByteQueue::new();
    pkt.write_byte(ServerPacketID::TrainerCreatureList.to_byte());
    pkt.write_ascii_string(creatures);
    pkt.into_bytes()
}


