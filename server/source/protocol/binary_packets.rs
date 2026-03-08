/// Binary packet builders for all Server→Client packets (13.3 protocol).
///
/// Each function builds a complete binary packet using ByteQueue.
/// Packet format: [PacketID: 1 byte] [Field₁] [Field₂] ... [Fieldₙ]

use super::byte_queue::ByteQueue;
use super::packets::{ServerPacketID, MultiMessageID};

// ── Auth / Login ───────────────────────────────────────────

/// ID 0: Login successful.
pub fn write_logged(class: u8) -> Vec<u8> {
    let mut pkt = ByteQueue::new();
    pkt.write_byte(ServerPacketID::Logged.to_byte());
    pkt.write_byte(class);
    pkt.into_bytes()
}

/// ID 4: Disconnect client.
pub fn write_disconnect() -> Vec<u8> {
    let mut pkt = ByteQueue::new();
    pkt.write_byte(ServerPacketID::Disconnect.to_byte());
    pkt.into_bytes()
}

/// ID 55: Error message (disconnects after showing).
pub fn write_error_msg(msg: &str) -> Vec<u8> {
    let mut pkt = ByteQueue::new();
    pkt.write_byte(ServerPacketID::ErrorMsg.to_byte());
    pkt.write_ascii_string(msg);
    pkt.into_bytes()
}

/// ID 26: Show message box.
pub fn write_message_box(msg: &str) -> Vec<u8> {
    let mut pkt = ByteQueue::new();
    pkt.write_byte(ServerPacketID::ShowMessageBox.to_byte());
    pkt.write_ascii_string(msg);
    pkt.into_bytes()
}

/// ID 27: User index in server.
pub fn write_user_index_in_server(index: i16) -> Vec<u8> {
    let mut pkt = ByteQueue::new();
    pkt.write_byte(ServerPacketID::UserIndexInServer.to_byte());
    pkt.write_integer(index);
    pkt.into_bytes()
}

/// ID 28: User char index in server.
pub fn write_user_char_index(index: i16) -> Vec<u8> {
    let mut pkt = ByteQueue::new();
    pkt.write_byte(ServerPacketID::UserCharIndexInServer.to_byte());
    pkt.write_integer(index);
    pkt.into_bytes()
}

/// ID 67: Dice roll result for character creation.
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
pub fn write_pos_update(x: u8, y: u8) -> Vec<u8> {
    let mut pkt = ByteQueue::new();
    pkt.write_byte(ServerPacketID::PosUpdate.to_byte());
    pkt.write_byte(x);
    pkt.write_byte(y);
    pkt.into_bytes()
}

/// ID 41: Area changed.
pub fn write_area_changed(x: u8, y: u8) -> Vec<u8> {
    let mut pkt = ByteQueue::new();
    pkt.write_byte(ServerPacketID::AreaChanged.to_byte());
    pkt.write_byte(x);
    pkt.write_byte(y);
    pkt.into_bytes()
}

// ── Character ──────────────────────────────────────────────

/// ID 29: Character create (appears in view).
pub fn write_character_create(
    char_index: i16, body: i16, head: i16, heading: u8,
    x: u8, y: u8, weapon: i16, shield: i16, helmet: i16,
    fx_index: i16, fx_loops: i16, name: &str,
    nick_color: u8, privileges: u8,
) -> Vec<u8> {
    let mut pkt = ByteQueue::new();
    pkt.write_byte(ServerPacketID::CharacterCreate.to_byte());
    pkt.write_integer(char_index);
    pkt.write_integer(body);
    pkt.write_integer(head);
    pkt.write_byte(heading);
    pkt.write_byte(x);
    pkt.write_byte(y);
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
pub fn write_character_move(char_index: i16, x: u8, y: u8) -> Vec<u8> {
    let mut pkt = ByteQueue::new();
    pkt.write_byte(ServerPacketID::CharacterMove.to_byte());
    pkt.write_integer(char_index);
    pkt.write_byte(x);
    pkt.write_byte(y);
    pkt.into_bytes()
}

/// ID 33: Force character move (forced direction).
pub fn write_force_char_move(direction: u8) -> Vec<u8> {
    let mut pkt = ByteQueue::new();
    pkt.write_byte(ServerPacketID::ForceCharMove.to_byte());
    pkt.write_byte(direction);
    pkt.into_bytes()
}

/// ID 34: Character change (appearance update).
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

/// ID 44: Create FX on character.
pub fn write_create_fx(char_index: i16, fx_index: i16, fx_loops: i16) -> Vec<u8> {
    let mut pkt = ByteQueue::new();
    pkt.write_byte(ServerPacketID::CreateFX.to_byte());
    pkt.write_integer(char_index);
    pkt.write_integer(fx_index);
    pkt.write_integer(fx_loops);
    pkt.into_bytes()
}

// ── Objects on ground ──────────────────────────────────────

/// ID 35: Object create on ground.
pub fn write_object_create(x: u8, y: u8, grh_index: i16) -> Vec<u8> {
    let mut pkt = ByteQueue::new();
    pkt.write_byte(ServerPacketID::ObjectCreate.to_byte());
    pkt.write_byte(x);
    pkt.write_byte(y);
    pkt.write_integer(grh_index);
    pkt.into_bytes()
}

/// ID 36: Object delete from ground.
pub fn write_object_delete(x: u8, y: u8) -> Vec<u8> {
    let mut pkt = ByteQueue::new();
    pkt.write_byte(ServerPacketID::ObjectDelete.to_byte());
    pkt.write_byte(x);
    pkt.write_byte(y);
    pkt.into_bytes()
}

/// ID 37: Block position update.
pub fn write_block_position(x: u8, y: u8, blocked: bool) -> Vec<u8> {
    let mut pkt = ByteQueue::new();
    pkt.write_byte(ServerPacketID::BlockPosition.to_byte());
    pkt.write_byte(x);
    pkt.write_byte(y);
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

/// ID 111: Chat yell (larger area with color).
pub fn write_chat_yell(char_index: i16, msg: &str, color: i32) -> Vec<u8> {
    let mut pkt = ByteQueue::new();
    pkt.write_byte(ServerPacketID::ChatYell.to_byte());
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

/// ID 114: Clan/party chat.
pub fn write_chat_clan(msg: &str, font_index: u8) -> Vec<u8> {
    let mut pkt = ByteQueue::new();
    pkt.write_byte(ServerPacketID::ChatClan.to_byte());
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

/// ID 17: Update HP (VB6: [H]MaxHP,MinHP).
pub fn write_update_hp(max_hp: i16, min_hp: i16) -> Vec<u8> {
    let mut pkt = ByteQueue::new();
    pkt.write_byte(ServerPacketID::UpdateHP.to_byte());
    pkt.write_integer(max_hp);
    pkt.write_integer(min_hp);
    pkt.into_bytes()
}

/// ID 16: Update Mana (VB6: [M]MaxMAN,MinMAN).
pub fn write_update_mana(max_mana: i16, min_mana: i16) -> Vec<u8> {
    let mut pkt = ByteQueue::new();
    pkt.write_byte(ServerPacketID::UpdateMana.to_byte());
    pkt.write_integer(max_mana);
    pkt.write_integer(min_mana);
    pkt.into_bytes()
}

/// ID 15: Update Stamina (VB6: [E]MaxSta,MinSta).
pub fn write_update_sta(max_sta: i16, min_sta: i16) -> Vec<u8> {
    let mut pkt = ByteQueue::new();
    pkt.write_byte(ServerPacketID::UpdateSta.to_byte());
    pkt.write_integer(max_sta);
    pkt.write_integer(min_sta);
    pkt.into_bytes()
}

/// ID 18: Update Gold.
pub fn write_update_gold(gold: i32) -> Vec<u8> {
    let mut pkt = ByteQueue::new();
    pkt.write_byte(ServerPacketID::UpdateGold.to_byte());
    pkt.write_long(gold);
    pkt.into_bytes()
}

/// ID 19: Update Bank Gold.
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

/// ID 45: Update full user stats (login bulk).
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

/// ID 50: Attributes.
pub fn write_atributes(str_: u8, agi: u8, int: u8, con: u8, cha: u8) -> Vec<u8> {
    let mut pkt = ByteQueue::new();
    pkt.write_byte(ServerPacketID::Atributes.to_byte());
    pkt.write_byte(str_);
    pkt.write_byte(agi);
    pkt.write_byte(int);
    pkt.write_byte(con);
    pkt.write_byte(cha);
    pkt.into_bytes()
}

/// ID 71: Send skills (20 values).
pub fn write_send_skills(skills: &[u8; 20]) -> Vec<u8> {
    let mut pkt = ByteQueue::new();
    pkt.write_byte(ServerPacketID::SendSkills.to_byte());
    for &s in skills {
        pkt.write_byte(s);
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

/// ID 61: Fame values.
pub fn write_fame(values: &[i32; 7]) -> Vec<u8> {
    let mut pkt = ByteQueue::new();
    pkt.write_byte(ServerPacketID::Fame.to_byte());
    for &v in values {
        pkt.write_long(v);
    }
    pkt.into_bytes()
}

/// ID 62: Mini stats.
pub fn write_mini_stats(gold: i32, exp: i32, level: u8, class: u8) -> Vec<u8> {
    let mut pkt = ByteQueue::new();
    pkt.write_byte(ServerPacketID::MiniStats.to_byte());
    pkt.write_long(gold);
    pkt.write_long(exp);
    // Additional fields vary per implementation
    pkt.into_bytes()
}

/// ID 63: Level up notification.
pub fn write_level_up(skill_points: i16) -> Vec<u8> {
    let mut pkt = ByteQueue::new();
    pkt.write_byte(ServerPacketID::LevelUp.to_byte());
    pkt.write_integer(skill_points);
    pkt.into_bytes()
}

/// ID 100: Update strength and dexterity.
pub fn write_update_str_dex(str_: u8, agi: u8) -> Vec<u8> {
    let mut pkt = ByteQueue::new();
    pkt.write_byte(ServerPacketID::UpdateStrengthAndDexterity.to_byte());
    pkt.write_byte(str_);
    pkt.write_byte(agi);
    pkt.into_bytes()
}

// ── Inventory / Spells ─────────────────────────────────────

/// ID 47: Change inventory slot.
pub fn write_change_inventory_slot(
    slot: u8, obj_index: i16, name: &str, amount: i16,
    equipped: bool, grh_index: i16, obj_type: u8,
    max_hit: i16, min_hit: i16, def: i16, value: f32,
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
    pkt.write_integer(def);
    pkt.write_single(value);
    pkt.into_bytes()
}

/// ID 48: Change bank slot.
pub fn write_change_bank_slot(
    slot: u8, obj_index: i16, name: &str, amount: i16,
    equipped: bool, grh_index: i16, obj_type: u8,
    max_hit: i16, min_hit: i16, def: i16, value: f32,
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
    pkt.write_integer(def);
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
    max_hit: i16, min_hit: i16, def: i16,
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
    pkt.write_integer(def);
    pkt.into_bytes()
}

// ── Sound / Music ──────────────────────────────────────────

/// ID 38: Play MIDI.
pub fn write_play_midi(midi_index: u8) -> Vec<u8> {
    let mut pkt = ByteQueue::new();
    pkt.write_byte(ServerPacketID::PlayMIDI.to_byte());
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

/// ID 11: User offer confirm.
pub fn write_user_offer_confirm() -> Vec<u8> {
    let mut pkt = ByteQueue::new();
    pkt.write_byte(ServerPacketID::UserOfferConfirm.to_byte());
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

/// ID 86: Change user trade slot.
pub fn write_change_user_trade_slot(
    offer_slot: u8, obj_index: i16, amount: i32, grh_index: i16,
    obj_type: u8, max_hit: i16, min_hit: i16, def: i16,
    value: i32, name: &str,
) -> Vec<u8> {
    let mut pkt = ByteQueue::new();
    pkt.write_byte(ServerPacketID::ChangeUserTradeSlot.to_byte());
    pkt.write_byte(offer_slot);
    pkt.write_integer(obj_index);
    pkt.write_long(amount);
    pkt.write_integer(grh_index);
    pkt.write_byte(obj_type);
    pkt.write_integer(max_hit);
    pkt.write_integer(min_hit);
    pkt.write_integer(def);
    pkt.write_long(value);
    pkt.write_ascii_string(name);
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

/// ID 105: Stop working.
pub fn write_stop_working() -> Vec<u8> {
    let mut pkt = ByteQueue::new();
    pkt.write_byte(ServerPacketID::StopWorking.to_byte());
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
    pkt.write_byte(ServerPacketID::RestOK.to_byte());
    pkt.into_bytes()
}

/// ID 56: Blind.
pub fn write_blind() -> Vec<u8> {
    let mut pkt = ByteQueue::new();
    pkt.write_byte(ServerPacketID::Blind.to_byte());
    pkt.into_bytes()
}

/// ID 57: Dumb.
pub fn write_dumb() -> Vec<u8> {
    let mut pkt = ByteQueue::new();
    pkt.write_byte(ServerPacketID::Dumb.to_byte());
    pkt.into_bytes()
}

/// ID 68: Meditate toggle.
pub fn write_meditate_toggle() -> Vec<u8> {
    let mut pkt = ByteQueue::new();
    pkt.write_byte(ServerPacketID::MeditateToggle.to_byte());
    pkt.into_bytes()
}

/// ID 69: Blind no more.
pub fn write_blind_no_more() -> Vec<u8> {
    let mut pkt = ByteQueue::new();
    pkt.write_byte(ServerPacketID::BlindNoMore.to_byte());
    pkt.into_bytes()
}

/// ID 70: Dumb no more.
pub fn write_dumb_no_more() -> Vec<u8> {
    let mut pkt = ByteQueue::new();
    pkt.write_byte(ServerPacketID::DumbNoMore.to_byte());
    pkt.into_bytes()
}

/// ID 82: Paralize OK.
pub fn write_paralize_ok(duration_secs: i16) -> Vec<u8> {
    let mut pkt = ByteQueue::new();
    pkt.write_byte(ServerPacketID::ParalizeOK.to_byte());
    pkt.write_integer(duration_secs); // 0 = toggle off, >0 = paralysis countdown in seconds
    pkt.into_bytes()
}

/// ID 87: Send night.
pub fn write_send_night(is_night: bool) -> Vec<u8> {
    let mut pkt = ByteQueue::new();
    pkt.write_byte(ServerPacketID::SendNight.to_byte());
    pkt.write_boolean(is_night);
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

/// ID 103: Add slots (inventory expansion).
pub fn write_add_slots(max_slots: u8) -> Vec<u8> {
    let mut pkt = ByteQueue::new();
    pkt.write_byte(ServerPacketID::AddSlots.to_byte());
    pkt.write_byte(max_slots);
    pkt.into_bytes()
}

/// ID 106: Cancel offer item.
pub fn write_cancel_offer_item(slot: u8) -> Vec<u8> {
    let mut pkt = ByteQueue::new();
    pkt.write_byte(ServerPacketID::CancelOfferItem.to_byte());
    pkt.write_byte(slot);
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

/// ID 73: Guild news.
pub fn write_guild_news(news: &str, motd: &str, codex: &str) -> Vec<u8> {
    let mut pkt = ByteQueue::new();
    pkt.write_byte(ServerPacketID::GuildNews.to_byte());
    pkt.write_ascii_string(news);
    pkt.write_ascii_string(motd);
    pkt.write_ascii_string(codex);
    pkt.into_bytes()
}

/// ID 81: Show guild fundation form.
pub fn write_show_guild_fundation_form() -> Vec<u8> {
    let mut pkt = ByteQueue::new();
    pkt.write_byte(ServerPacketID::ShowGuildFundationForm.to_byte());
    pkt.into_bytes()
}

/// ID 98: Show guild align form.
pub fn write_show_guild_align() -> Vec<u8> {
    let mut pkt = ByteQueue::new();
    pkt.write_byte(ServerPacketID::ShowGuildAlign.to_byte());
    pkt.into_bytes()
}

// ── Signals / Forum / NPC lists ────────────────────────────

/// ID 58: Show signal.
pub fn write_show_signal(text: &str, grh_index: i16) -> Vec<u8> {
    let mut pkt = ByteQueue::new();
    pkt.write_byte(ServerPacketID::ShowSignal.to_byte());
    pkt.write_ascii_string(text);
    pkt.write_integer(grh_index);
    pkt.into_bytes()
}

/// ID 72: Trainer creature list.
pub fn write_trainer_creature_list(creatures: &str) -> Vec<u8> {
    let mut pkt = ByteQueue::new();
    pkt.write_byte(ServerPacketID::TrainerCreatureList.to_byte());
    pkt.write_ascii_string(creatures);
    pkt.into_bytes()
}

/// ID 90: Spawn list.
pub fn write_spawn_list(creatures: &str) -> Vec<u8> {
    let mut pkt = ByteQueue::new();
    pkt.write_byte(ServerPacketID::SpawnList.to_byte());
    pkt.write_ascii_string(creatures);
    pkt.into_bytes()
}

/// ID 99: Show party form.
pub fn write_show_party_form(party_type: u8) -> Vec<u8> {
    let mut pkt = ByteQueue::new();
    pkt.write_byte(ServerPacketID::ShowPartyForm.to_byte());
    pkt.write_byte(party_type);
    pkt.into_bytes()
}

// ── MultiMessage ───────────────────────────────────────────

/// ID 104: MultiMessage with no extra payload.
pub fn write_multi_msg_simple(sub_type: MultiMessageID) -> Vec<u8> {
    let mut pkt = ByteQueue::new();
    pkt.write_byte(ServerPacketID::MultiMessage.to_byte());
    pkt.write_byte(sub_type.to_byte());
    pkt.into_bytes()
}

/// ID 104, sub 12: NPC hit user.
pub fn write_multi_npc_hit_user(body_part: u8, damage: i16) -> Vec<u8> {
    let mut pkt = ByteQueue::new();
    pkt.write_byte(ServerPacketID::MultiMessage.to_byte());
    pkt.write_byte(MultiMessageID::NPCHitUser.to_byte());
    pkt.write_byte(body_part);
    pkt.write_integer(damage);
    pkt.into_bytes()
}

/// ID 104, sub 13: User hit NPC.
pub fn write_multi_user_hit_npc(damage: i32) -> Vec<u8> {
    let mut pkt = ByteQueue::new();
    pkt.write_byte(ServerPacketID::MultiMessage.to_byte());
    pkt.write_byte(MultiMessageID::UserHitNPC.to_byte());
    pkt.write_long(damage);
    pkt.into_bytes()
}

/// ID 104, sub 14: User attacked swing (missed).
pub fn write_multi_user_attacked_swing(attacker_index: i16) -> Vec<u8> {
    let mut pkt = ByteQueue::new();
    pkt.write_byte(ServerPacketID::MultiMessage.to_byte());
    pkt.write_byte(MultiMessageID::UserAttackedSwing.to_byte());
    pkt.write_integer(attacker_index);
    pkt.into_bytes()
}

/// ID 104, sub 15: User hit by user.
pub fn write_multi_user_hitted_by_user(attacker_index: i16, body_part: u8, damage: i16) -> Vec<u8> {
    let mut pkt = ByteQueue::new();
    pkt.write_byte(ServerPacketID::MultiMessage.to_byte());
    pkt.write_byte(MultiMessageID::UserHittedByUser.to_byte());
    pkt.write_integer(attacker_index);
    pkt.write_byte(body_part);
    pkt.write_integer(damage);
    pkt.into_bytes()
}

/// ID 104, sub 16: User hit user (attacker receives).
pub fn write_multi_user_hitted_user(victim_index: i16, body_part: u8, damage: i16) -> Vec<u8> {
    let mut pkt = ByteQueue::new();
    pkt.write_byte(ServerPacketID::MultiMessage.to_byte());
    pkt.write_byte(MultiMessageID::UserHittedUser.to_byte());
    pkt.write_integer(victim_index);
    pkt.write_byte(body_part);
    pkt.write_integer(damage);
    pkt.into_bytes()
}

/// ID 104, sub 18: Have killed user.
pub fn write_multi_have_killed_user(killed_index: i16, exp_gained: i32) -> Vec<u8> {
    let mut pkt = ByteQueue::new();
    pkt.write_byte(ServerPacketID::MultiMessage.to_byte());
    pkt.write_byte(MultiMessageID::HaveKilledUser.to_byte());
    pkt.write_integer(killed_index);
    pkt.write_long(exp_gained);
    pkt.into_bytes()
}

/// ID 104, sub 19: User kill (killed by other user).
pub fn write_multi_user_kill(killer_index: i16) -> Vec<u8> {
    let mut pkt = ByteQueue::new();
    pkt.write_byte(ServerPacketID::MultiMessage.to_byte());
    pkt.write_byte(MultiMessageID::UserKill.to_byte());
    pkt.write_integer(killer_index);
    pkt.into_bytes()
}

/// ID 104, sub 21: Go home.
pub fn write_multi_go_home(distance: u8, time: i16, home_city: &str) -> Vec<u8> {
    let mut pkt = ByteQueue::new();
    pkt.write_byte(ServerPacketID::MultiMessage.to_byte());
    pkt.write_byte(MultiMessageID::GoHome.to_byte());
    pkt.write_byte(distance);
    pkt.write_integer(time);
    pkt.write_ascii_string(home_city);
    pkt.into_bytes()
}

// ── GM Panel ───────────────────────────────────────────────

/// ID 91: Show SOS form.
pub fn write_show_sos_form(sos_list: &str) -> Vec<u8> {
    let mut pkt = ByteQueue::new();
    pkt.write_byte(ServerPacketID::ShowSOSForm.to_byte());
    pkt.write_ascii_string(sos_list);
    pkt.into_bytes()
}

/// ID 92: Show MOTD edition form.
pub fn write_show_motd_edition_form(motd: &str) -> Vec<u8> {
    let mut pkt = ByteQueue::new();
    pkt.write_byte(ServerPacketID::ShowMOTDEditionForm.to_byte());
    pkt.write_ascii_string(motd);
    pkt.into_bytes()
}

/// ID 93: Show GM panel form.
pub fn write_show_gm_panel_form() -> Vec<u8> {
    let mut pkt = ByteQueue::new();
    pkt.write_byte(ServerPacketID::ShowGMPanelForm.to_byte());
    pkt.into_bytes()
}

/// ID 94: User name list.
pub fn write_user_name_list(names: &str) -> Vec<u8> {
    let mut pkt = ByteQueue::new();
    pkt.write_byte(ServerPacketID::UserNameList.to_byte());
    pkt.write_ascii_string(names);
    pkt.into_bytes()
}

/// ID 77: Character info.
pub fn write_character_info(
    name: &str, race: u8, class: u8, gender: u8, level: u8,
    gold: i32, bank_gold: i32, reputation: i32,
    description: &str, guild_name: &str,
) -> Vec<u8> {
    let mut pkt = ByteQueue::new();
    pkt.write_byte(ServerPacketID::CharacterInfo.to_byte());
    pkt.write_ascii_string(name);
    pkt.write_byte(race);
    pkt.write_byte(class);
    pkt.write_byte(gender);
    pkt.write_byte(level);
    pkt.write_long(gold);
    pkt.write_long(bank_gold);
    pkt.write_long(reputation);
    pkt.write_ascii_string(description);
    pkt.write_ascii_string(guild_name);
    pkt.into_bytes()
}

// ── Safe Toggle ───────────────────────────────────────────

/// ID 130: Safe mode on.
pub fn write_safe_on() -> Vec<u8> {
    let mut pkt = ByteQueue::new();
    pkt.write_byte(ServerPacketID::SafeOn.to_byte());
    pkt.into_bytes()
}

/// ID 131: Safe mode off.
pub fn write_safe_off() -> Vec<u8> {
    let mut pkt = ByteQueue::new();
    pkt.write_byte(ServerPacketID::SafeOff.to_byte());
    pkt.into_bytes()
}

// ── NPC Inventory / Trade (legacy binary) ─────────────────

/// ID 170: Reset NPC inventory display on client.
pub fn write_npc_inv_reset() -> Vec<u8> {
    let mut pkt = ByteQueue::new();
    pkt.write_byte(ServerPacketID::NpcInvReset.to_byte());
    pkt.into_bytes()
}

/// ID 174: NPC commerce transaction OK (buy/sell confirmation).
/// slot = 1-based NPC inv slot, trade_type = 0 (buy) or 1 (sell).
pub fn write_trans_ok(slot: u8, trade_type: u8) -> Vec<u8> {
    let mut pkt = ByteQueue::new();
    pkt.write_byte(ServerPacketID::TransOK.to_byte());
    pkt.write_byte(slot);
    pkt.write_byte(trade_type);
    pkt.into_bytes()
}

/// ID 175: Commerce window close confirmation.
pub fn write_commerce_close_ok() -> Vec<u8> {
    let mut pkt = ByteQueue::new();
    pkt.write_byte(ServerPacketID::CommerceCloseOK.to_byte());
    pkt.into_bytes()
}

/// ID 168: Bank window close confirmation.
pub fn write_bank_close_ok() -> Vec<u8> {
    let mut pkt = ByteQueue::new();
    pkt.write_byte(ServerPacketID::BankCloseOK.to_byte());
    pkt.into_bytes()
}

/// ID 181: Trade offer received (gold amount from partner).
pub fn write_trade_offer_recv(gold: i32) -> Vec<u8> {
    let mut pkt = ByteQueue::new();
    pkt.write_byte(ServerPacketID::TradeOfferRecv.to_byte());
    pkt.write_long(gold);
    pkt.into_bytes()
}

/// ID 182: Trade item info sent to partner.
pub fn write_trade_items(obj_index: i16, amount: i16, name: &str) -> Vec<u8> {
    let mut pkt = ByteQueue::new();
    pkt.write_byte(ServerPacketID::TradeItems.to_byte());
    pkt.write_integer(obj_index);
    pkt.write_integer(amount);
    pkt.write_ascii_string(name);
    pkt.into_bytes()
}

/// ID 185: Trade cancel confirmation.
pub fn write_trade_cancel_ok() -> Vec<u8> {
    let mut pkt = ByteQueue::new();
    pkt.write_byte(ServerPacketID::TradeCancelOK.to_byte());
    pkt.into_bytes()
}

/// ID 98: Character data (auras, color, levitating, ranking).
pub fn write_char_data(
    char_index: i16, color: u8, aura_a: i16, aura_w: i16,
    aura_e: i16, aura_r: i16, aura_c: i16, levitando: bool,
    ranking: u8,
) -> Vec<u8> {
    let mut pkt = ByteQueue::new();
    pkt.write_byte(ServerPacketID::CharData.to_byte());
    pkt.write_integer(char_index);
    pkt.write_byte(color);
    pkt.write_integer(aura_a);
    pkt.write_integer(aura_w);
    pkt.write_integer(aura_e);
    pkt.write_integer(aura_r);
    pkt.write_integer(aura_c);
    pkt.write_boolean(levitando);
    pkt.write_byte(ranking);
    pkt.into_bytes()
}

/// ID 99: Aura update broadcast.
pub fn write_aura_update(
    char_index: i16, aura_a: i16, aura_w: i16,
    aura_e: i16, aura_r: i16, aura_c: i16,
) -> Vec<u8> {
    let mut pkt = ByteQueue::new();
    pkt.write_byte(ServerPacketID::AuraUpdate.to_byte());
    pkt.write_integer(char_index);
    pkt.write_integer(aura_a);
    pkt.write_integer(aura_w);
    pkt.write_integer(aura_e);
    pkt.write_integer(aura_r);
    pkt.write_integer(aura_c);
    pkt.into_bytes()
}

// ── Pre-login / Account ───────────────────────────────────

/// ID 55: Error show (stays connected, unlike ErrorMsg which disconnects).
pub fn write_error_show(msg: &str) -> Vec<u8> {
    let mut pkt = ByteQueue::new();
    pkt.write_byte(ServerPacketID::ErrorShow.to_byte());
    pkt.write_ascii_string(msg);
    pkt.into_bytes()
}

/// ID 77: Finish OK (operation completed).
pub fn write_finish_ok() -> Vec<u8> {
    let mut pkt = ByteQueue::new();
    pkt.write_byte(ServerPacketID::FinishOK.to_byte());
    pkt.into_bytes()
}

/// ID 67: Init account (sends char count + notice/MOTD).
pub fn write_init_account(num_chars: u8, notice: &str) -> Vec<u8> {
    let mut pkt = ByteQueue::new();
    pkt.write_byte(ServerPacketID::InitAccount.to_byte());
    pkt.write_byte(num_chars);
    pkt.write_ascii_string(notice);
    pkt.into_bytes()
}

/// ID 64: Add character to selection list (typed binary fields).
pub fn write_add_pj(name: &str, index: u8, head: i16, body: i16, weapon: i16, shield: i16, helmet: i16, level: u8, class: &str, dead: bool, race: &str) -> Vec<u8> {
    let mut pkt = ByteQueue::new();
    pkt.write_byte(ServerPacketID::AddPJ.to_byte());
    pkt.write_ascii_string(name);
    pkt.write_byte(index);
    pkt.write_integer(head);
    pkt.write_integer(body);
    pkt.write_integer(weapon);
    pkt.write_integer(shield);
    pkt.write_integer(helmet);
    pkt.write_byte(level);
    pkt.write_ascii_string(class);
    pkt.write_boolean(dead);
    pkt.write_ascii_string(race);
    pkt.into_bytes()
}

/// ID 65: Security code (codex for password verification).
pub fn write_security_code(code: &str) -> Vec<u8> {
    let mut pkt = ByteQueue::new();
    pkt.write_byte(ServerPacketID::SecurityCode.to_byte());
    pkt.write_ascii_string(code);
    pkt.into_bytes()
}

/// ID 74: Privilege level.
pub fn write_privilege_level(level: u8) -> Vec<u8> {
    let mut pkt = ByteQueue::new();
    pkt.write_byte(ServerPacketID::PrivilegeLevel.to_byte());
    pkt.write_byte(level);
    pkt.into_bytes()
}

/// ID 96: Map music.
pub fn write_map_music(music_id: u8) -> Vec<u8> {
    let mut pkt = ByteQueue::new();
    pkt.write_byte(ServerPacketID::MapMusic.to_byte());
    pkt.write_byte(music_id);
    pkt.into_bytes()
}

/// ID 97: Map name display.
pub fn write_map_name(name: &str) -> Vec<u8> {
    let mut pkt = ByteQueue::new();
    pkt.write_byte(ServerPacketID::MapName.to_byte());
    pkt.write_ascii_string(name);
    pkt.into_bytes()
}

/// ID 132: Safe resurrection on.
pub fn write_safe_resu_on() -> Vec<u8> {
    let mut pkt = ByteQueue::new();
    pkt.write_byte(ServerPacketID::SafeResuOn.to_byte());
    pkt.into_bytes()
}

/// ID 133: Safe resurrection off.
pub fn write_safe_resu_off() -> Vec<u8> {
    let mut pkt = ByteQueue::new();
    pkt.write_byte(ServerPacketID::SafeResuOff.to_byte());
    pkt.into_bytes()
}

/// ID 88: Pong response.
pub fn write_pong_response() -> Vec<u8> {
    let mut pkt = ByteQueue::new();
    pkt.write_byte(ServerPacketID::Pong.to_byte());
    pkt.into_bytes()
}

/// ID 78: Dead flag.
pub fn write_dead() -> Vec<u8> {
    let mut pkt = ByteQueue::new();
    pkt.write_byte(ServerPacketID::Dead.to_byte());
    pkt.into_bytes()
}

/// ID 81: Navigate toggle.
pub fn write_navigate() -> Vec<u8> {
    let mut pkt = ByteQueue::new();
    pkt.write_byte(ServerPacketID::NavigateToggle.to_byte());
    pkt.into_bytes()
}

// ── Blacksmith / Carpenter lists ───────────────────────────

/// ID 51/52/53: Buildable items list (weapons/armors/carpentry).
/// Format: count, then per item: name(string), grhIndex(int16), ...
pub fn write_craft_list(packet_id: ServerPacketID, items: &[(String, i16)]) -> Vec<u8> {
    let mut pkt = ByteQueue::new();
    pkt.write_byte(packet_id.to_byte());
    pkt.write_integer(items.len() as i16);
    for (name, grh) in items {
        pkt.write_ascii_string(name);
        pkt.write_integer(*grh);
    }
    pkt.into_bytes()
}

// ── Level / Reputation / Timer ────────────────────────────

/// ID 125: Level update (StatLevel).
pub fn write_level_update(level: u8) -> Vec<u8> {
    let mut pkt = ByteQueue::new();
    pkt.write_byte(ServerPacketID::StatLevel.to_byte());
    pkt.write_byte(level);
    pkt.into_bytes()
}

/// ID 233: Reputation data (RptData).
pub fn write_reputation(rep: i32) -> Vec<u8> {
    let mut pkt = ByteQueue::new();
    pkt.write_byte(ServerPacketID::RptData.to_byte());
    pkt.write_long(rep);
    pkt.into_bytes()
}

/// ID 246: Timer/scroll info (TIS).
pub fn write_timer_info(id: u8, time1: i32, time2: i32) -> Vec<u8> {
    let mut pkt = ByteQueue::new();
    pkt.write_byte(ServerPacketID::TimerInfo.to_byte());
    pkt.write_byte(id);
    pkt.write_long(time1);
    pkt.write_long(time2);
    pkt.into_bytes()
}

// ── Stop Dancing / Animation ──────────────────────────────

/// ID 220: Stop dancing flag.
pub fn write_stop_dancing(flag: bool) -> Vec<u8> {
    let mut pkt = ByteQueue::new();
    pkt.write_byte(ServerPacketID::StopDancing.to_byte());
    pkt.write_boolean(flag);
    pkt.into_bytes()
}

/// ID 225: Animation data (ANM).
pub fn write_anim_data(data: &str) -> Vec<u8> {
    let mut pkt = ByteQueue::new();
    pkt.write_byte(ServerPacketID::AnimData.to_byte());
    pkt.write_ascii_string(data);
    pkt.into_bytes()
}

// ── Arrow / Heading / Navigation ──────────────────────────

/// ID 108: Arrow projectile (FLECHI).
pub fn write_arrow(src: i16, tgt: i16, grh: i16) -> Vec<u8> {
    let mut pkt = ByteQueue::new();
    pkt.write_byte(ServerPacketID::Arrow.to_byte());
    pkt.write_integer(src);
    pkt.write_integer(tgt);
    pkt.write_integer(grh);
    pkt.into_bytes()
}

/// ID 109: Navigation broadcast (NVG).
pub fn write_navigate_broadcast(ci: i16, flag: bool) -> Vec<u8> {
    let mut pkt = ByteQueue::new();
    pkt.write_byte(ServerPacketID::NavigateBroadcast.to_byte());
    pkt.write_integer(ci);
    pkt.write_boolean(flag);
    pkt.into_bytes()
}

/// ID 107: Heading change broadcast (|H).
pub fn write_heading_change(ci: i16, heading: u8) -> Vec<u8> {
    let mut pkt = ByteQueue::new();
    pkt.write_byte(ServerPacketID::HeadingChange.to_byte());
    pkt.write_integer(ci);
    pkt.write_byte(heading);
    pkt.into_bytes()
}

// ── Work / Mount / Levitate ───────────────────────────────

/// ID 155: Work mode (T01).
pub fn write_work_mode(skill: u8) -> Vec<u8> {
    let mut pkt = ByteQueue::new();
    pkt.write_byte(ServerPacketID::WorkMode.to_byte());
    pkt.write_byte(skill);
    pkt.into_bytes()
}

/// ID 142: User mount (USM).
pub fn write_user_mount(ci: i16, flag: bool) -> Vec<u8> {
    let mut pkt = ByteQueue::new();
    pkt.write_byte(ServerPacketID::UserMount.to_byte());
    pkt.write_integer(ci);
    pkt.write_boolean(flag);
    pkt.into_bytes()
}

/// ID 143: Levitate (MVOL).
pub fn write_levitate(ci: i16, flag: bool) -> Vec<u8> {
    let mut pkt = ByteQueue::new();
    pkt.write_byte(ServerPacketID::Levitate.to_byte());
    pkt.write_integer(ci);
    pkt.write_boolean(flag);
    pkt.into_bytes()
}

/// ID 144: Class options (99).
pub fn write_class_options(opt1: u8, opt2: u8) -> Vec<u8> {
    let mut pkt = ByteQueue::new();
    pkt.write_byte(ServerPacketID::ClassOptions.to_byte());
    pkt.write_byte(opt1);
    pkt.write_byte(opt2);
    pkt.into_bytes()
}

// ── Friends ───────────────────────────────────────────────

/// ID 235: Friend online (KFM).
pub fn write_friend_online(name: &str) -> Vec<u8> {
    let mut pkt = ByteQueue::new();
    pkt.write_byte(ServerPacketID::KfmData.to_byte());
    pkt.write_ascii_string(name);
    pkt.into_bytes()
}

/// ID 236: Friend offline (DFM).
pub fn write_friend_offline(name: &str) -> Vec<u8> {
    let mut pkt = ByteQueue::new();
    pkt.write_byte(ServerPacketID::DfmData.to_byte());
    pkt.write_ascii_string(name);
    pkt.into_bytes()
}

/// ID 210: Friend list (LDM).
pub fn write_friend_list(data: &str) -> Vec<u8> {
    let mut pkt = ByteQueue::new();
    pkt.write_byte(ServerPacketID::FriendList.to_byte());
    pkt.write_ascii_string(data);
    pkt.into_bytes()
}

/// ID 253: Friend dialog trigger (MFC).
pub fn write_friend_dialog() -> Vec<u8> {
    let mut pkt = ByteQueue::new();
    pkt.write_byte(ServerPacketID::FriendDialog.to_byte());
    pkt.into_bytes()
}

// ── Mail ──────────────────────────────────────────────────

/// ID 205: Mail list (IFO).
pub fn write_mail_list(data: &str) -> Vec<u8> {
    let mut pkt = ByteQueue::new();
    pkt.write_byte(ServerPacketID::MailList.to_byte());
    pkt.write_ascii_string(data);
    pkt.into_bytes()
}

/// ID 208: Mail content.
pub fn write_mail_content(data: &str) -> Vec<u8> {
    let mut pkt = ByteQueue::new();
    pkt.write_byte(ServerPacketID::MailContent.to_byte());
    pkt.write_ascii_string(data);
    pkt.into_bytes()
}

/// ID 252: Mail open trigger (CORREO).
pub fn write_mail_open() -> Vec<u8> {
    let mut pkt = ByteQueue::new();
    pkt.write_byte(ServerPacketID::MailOpenTrigger.to_byte());
    pkt.into_bytes()
}

// ── Chat system (ENCHAT / IRCHAT) ─────────────────────────

/// ID 228: Enter chat room (ENCHAT).
pub fn write_enchat(name: &str) -> Vec<u8> {
    let mut pkt = ByteQueue::new();
    pkt.write_byte(ServerPacketID::EnchatData.to_byte());
    pkt.write_ascii_string(name);
    pkt.into_bytes()
}

/// ID 229: IRC-style chat message (IRCHAT).
pub fn write_irchat(name: &str, msg: &str) -> Vec<u8> {
    let mut pkt = ByteQueue::new();
    pkt.write_byte(ServerPacketID::IrchatData.to_byte());
    pkt.write_ascii_string(name);
    pkt.write_ascii_string(msg);
    pkt.into_bytes()
}

// ── Festival / Full char info ─────────────────────────────

/// ID 227: Festival data (FEST).
pub fn write_fest_data(data: &str) -> Vec<u8> {
    let mut pkt = ByteQueue::new();
    pkt.write_byte(ServerPacketID::FestData.to_byte());
    pkt.write_ascii_string(data);
    pkt.into_bytes()
}

/// ID 245: Full character info (FINI).
pub fn write_full_char_info(data: &str) -> Vec<u8> {
    let mut pkt = ByteQueue::new();
    pkt.write_byte(ServerPacketID::FullCharInfo.to_byte());
    pkt.write_ascii_string(data);
    pkt.into_bytes()
}

// ── Guild info / details / bank perms ─────────────────────

/// ID 195: Guild bank permissions response (KHEKD).
pub fn write_guild_bank_perms(can_obj: bool, can_gold: bool) -> Vec<u8> {
    let mut pkt = ByteQueue::new();
    pkt.write_byte(ServerPacketID::GuildBankPermsResp.to_byte());
    pkt.write_boolean(can_obj);
    pkt.write_boolean(can_gold);
    pkt.into_bytes()
}

/// ID 191: Guild info for leader (IREDAEL).
pub fn write_guild_info_leader(data: &str) -> Vec<u8> {
    let mut pkt = ByteQueue::new();
    pkt.write_byte(ServerPacketID::GuildInfoLeader.to_byte());
    pkt.write_ascii_string(data);
    pkt.into_bytes()
}

/// ID 192: Guild info for member (IREDAEK).
pub fn write_guild_info_member(data: &str) -> Vec<u8> {
    let mut pkt = ByteQueue::new();
    pkt.write_byte(ServerPacketID::GuildInfoMember.to_byte());
    pkt.write_ascii_string(data);
    pkt.into_bytes()
}

/// ID 194: Guild details response (DTLC).
pub fn write_guild_details(data: &str) -> Vec<u8> {
    let mut pkt = ByteQueue::new();
    pkt.write_byte(ServerPacketID::GuildDetailsResp.to_byte());
    pkt.write_ascii_string(data);
    pkt.into_bytes()
}

// ── Guild bank ────────────────────────────────────────────

/// ID 247: Guild bank init (INITCBANK).
pub fn write_guild_bank_init(can_obj: bool, can_gold: bool) -> Vec<u8> {
    let mut pkt = ByteQueue::new();
    pkt.write_byte(ServerPacketID::GuildBankInitResp.to_byte());
    pkt.write_boolean(can_obj);
    pkt.write_boolean(can_gold);
    pkt.into_bytes()
}

/// ID 249: Guild bank gold (SBG).
pub fn write_guild_bank_gold(gold: i32) -> Vec<u8> {
    let mut pkt = ByteQueue::new();
    pkt.write_byte(ServerPacketID::GuildBankGoldResp.to_byte());
    pkt.write_long(gold);
    pkt.into_bytes()
}

/// ID 248: Guild bank slot (BANCOBK).
pub fn write_guild_bank_slot(slot: u8, obj_type: u8) -> Vec<u8> {
    let mut pkt = ByteQueue::new();
    pkt.write_byte(ServerPacketID::GuildBankSlotResp.to_byte());
    pkt.write_byte(slot);
    pkt.write_byte(obj_type);
    pkt.into_bytes()
}

// ── SOS ───────────────────────────────────────────────────

/// ID 232: SOS data (ZSOS).
pub fn write_sos_data(data: &str) -> Vec<u8> {
    let mut pkt = ByteQueue::new();
    pkt.write_byte(ServerPacketID::ZsosData.to_byte());
    pkt.write_ascii_string(data);
    pkt.into_bytes()
}

// ── Menu / Select ─────────────────────────────────────────

/// ID 221: Menu data (MENU).
pub fn write_menu_data(name: &str, priv_: u8) -> Vec<u8> {
    let mut pkt = ByteQueue::new();
    pkt.write_byte(ServerPacketID::MenuData.to_byte());
    pkt.write_ascii_string(name);
    pkt.write_byte(priv_);
    pkt.into_bytes()
}

/// ID 222: Select data (SELE).
pub fn write_select_data(data: &str) -> Vec<u8> {
    let mut pkt = ByteQueue::new();
    pkt.write_byte(ServerPacketID::SelectData.to_byte());
    pkt.write_ascii_string(data);
    pkt.into_bytes()
}

/// ID 254: Arena duel list (MAR packet — 8 comma-separated duel slot names).
pub fn write_arena_data(names: &str) -> Vec<u8> {
    let mut pkt = ByteQueue::new();
    pkt.write_byte(ServerPacketID::ArenaData.to_byte());
    pkt.write_ascii_string(names);
    pkt.into_bytes()
}

// ── Triggers (simple no-payload packets) ──────────────────

/// ID 251: Open travels panel (TRAVELS).
pub fn write_travels() -> Vec<u8> {
    let mut pkt = ByteQueue::new();
    pkt.write_byte(ServerPacketID::TravelsOpen.to_byte());
    pkt.into_bytes()
}

/// ID 203: Quest NPC list trigger (DAMEQUEST).
pub fn write_quest_npc_list() -> Vec<u8> {
    let mut pkt = ByteQueue::new();
    pkt.write_byte(ServerPacketID::QuestNpcList.to_byte());
    pkt.into_bytes()
}

/// ID 146: Inventory init (INVI0).
pub fn write_inv_init() -> Vec<u8> {
    let mut pkt = ByteQueue::new();
    pkt.write_byte(ServerPacketID::InvInit.to_byte());
    pkt.into_bytes()
}

// ── Quest data ────────────────────────────────────────────

/// ID 200: Quest list data (QTL).
pub fn write_quest_list_data(data: &str) -> Vec<u8> {
    let mut pkt = ByteQueue::new();
    pkt.write_byte(ServerPacketID::QuestListResp.to_byte());
    pkt.write_ascii_string(data);
    pkt.into_bytes()
}

/// ID 201: Quest current data (MQC).
pub fn write_quest_current_data(data: &str) -> Vec<u8> {
    let mut pkt = ByteQueue::new();
    pkt.write_byte(ServerPacketID::QuestCurrent.to_byte());
    pkt.write_ascii_string(data);
    pkt.into_bytes()
}

/// ID 202: Quest selected data (MQS).
pub fn write_quest_selected_data(data: &str) -> Vec<u8> {
    let mut pkt = ByteQueue::new();
    pkt.write_byte(ServerPacketID::QuestSelected.to_byte());
    pkt.write_ascii_string(data);
    pkt.into_bytes()
}

// ── Tournament / Response ─────────────────────────────────

/// ID 176: Tournament points (PNT).
pub fn write_tournament_points(pts: i32) -> Vec<u8> {
    let mut pkt = ByteQueue::new();
    pkt.write_byte(ServerPacketID::TournamentPoints.to_byte());
    pkt.write_long(pts);
    pkt.into_bytes()
}

/// ID 177: Response text (RESPUES).
pub fn write_response(text: &str, name: &str) -> Vec<u8> {
    let mut pkt = ByteQueue::new();
    pkt.write_byte(ServerPacketID::ResponseMsg.to_byte());
    pkt.write_ascii_string(text);
    pkt.write_ascii_string(name);
    pkt.into_bytes()
}

// ── Auction ───────────────────────────────────────────────

/// ID 178: Auction init (INITSUB).
pub fn write_auction_init() -> Vec<u8> {
    let mut pkt = ByteQueue::new();
    pkt.write_byte(ServerPacketID::AuctionInit.to_byte());
    pkt.into_bytes()
}

/// ID 179: Auction bid info (GVN).
pub fn write_auction_bid(data: &str) -> Vec<u8> {
    let mut pkt = ByteQueue::new();
    pkt.write_byte(ServerPacketID::AuctionBid.to_byte());
    pkt.write_ascii_string(data);
    pkt.into_bytes()
}

/// ID 237: Auction list (APT).
pub fn write_auction_list(data: &str) -> Vec<u8> {
    let mut pkt = ByteQueue::new();
    pkt.write_byte(ServerPacketID::AuctionList.to_byte());
    pkt.write_ascii_string(data);
    pkt.into_bytes()
}

// ── Cosmetic ──────────────────────────────────────────────

/// ID 238: Cosmetic surgery (CIRUJA).
pub fn write_cosmetic_surgery(raza: u8, genero: u8) -> Vec<u8> {
    let mut pkt = ByteQueue::new();
    pkt.write_byte(ServerPacketID::CosmeticSurgery.to_byte());
    pkt.write_byte(raza);
    pkt.write_byte(genero);
    pkt.into_bytes()
}

/// ID 239: Cosmetic image data (IMEJ).
pub fn write_cosmetic_image(data: &str) -> Vec<u8> {
    let mut pkt = ByteQueue::new();
    pkt.write_byte(ServerPacketID::CosmeticImage.to_byte());
    pkt.write_ascii_string(data);
    pkt.into_bytes()
}

/// ID 240: Cosmetic PCGN data.
pub fn write_cosmetic_pcgn(data: &str) -> Vec<u8> {
    let mut pkt = ByteQueue::new();
    pkt.write_byte(ServerPacketID::CosmeticPcgn.to_byte());
    pkt.write_ascii_string(data);
    pkt.into_bytes()
}

/// ID 241: Cosmetic PCSS data.
pub fn write_cosmetic_pcss(data: &str) -> Vec<u8> {
    let mut pkt = ByteQueue::new();
    pkt.write_byte(ServerPacketID::CosmeticPcss.to_byte());
    pkt.write_ascii_string(data);
    pkt.into_bytes()
}

/// ID 242: Cosmetic PCCC data.
pub fn write_cosmetic_pccc(data: &str) -> Vec<u8> {
    let mut pkt = ByteQueue::new();
    pkt.write_byte(ServerPacketID::CosmeticPccc.to_byte());
    pkt.write_ascii_string(data);
    pkt.into_bytes()
}

// ── Particle / Light ──────────────────────────────────────

/// ID 243: Create particle (PCF).
pub fn write_particle_create(particle_group: i16, x: u8, y: u8, layer: u8) -> Vec<u8> {
    let mut pkt = ByteQueue::new();
    pkt.write_byte(ServerPacketID::ParticleCreate.to_byte());
    pkt.write_integer(particle_group);
    pkt.write_byte(x);
    pkt.write_byte(y);
    pkt.write_byte(layer);
    pkt.into_bytes()
}

/// ID 211: Character particle stream (CFF/PCB).
/// Wire: i16 charIndex, i16 particleStreamId
/// particleStreamId=0 clears all character particles.
pub fn write_char_particle_create(char_index: i16, particle_stream_id: i16) -> Vec<u8> {
    let mut pkt = ByteQueue::new();
    pkt.write_byte(ServerPacketID::CharParticleCreate.to_byte());
    pkt.write_integer(char_index);
    pkt.write_integer(particle_stream_id);
    pkt.into_bytes()
}

/// ID 244: Create light (PCL).
pub fn write_light_create(x: u8, y: u8, range: u8, r: u8, g: u8, b: u8) -> Vec<u8> {
    let mut pkt = ByteQueue::new();
    pkt.write_byte(ServerPacketID::LightCreate.to_byte());
    pkt.write_byte(x);
    pkt.write_byte(y);
    pkt.write_byte(range);
    pkt.write_byte(r);
    pkt.write_byte(g);
    pkt.write_byte(b);
    pkt.into_bytes()
}

// ── Additional event / GM / crafting packets ──────────────────────────

/// ID 163: Battle team kill scores (BTM1 — BatallaMistica event).
pub fn write_battle_team_scores(t1: i32, t2: i32, t3: i32, t4: i32) -> Vec<u8> {
    let mut pkt = ByteQueue::new();
    pkt.write_byte(ServerPacketID::BattleTeamScores.to_byte());
    pkt.write_long(t1);
    pkt.write_long(t2);
    pkt.write_long(t3);
    pkt.write_long(t4);
    pkt.into_bytes()
}

/// ID 164: Ambient map RGB color (PCR).
pub fn write_ambient_color(r: u8, g: u8, b: u8) -> Vec<u8> {
    let mut pkt = ByteQueue::new();
    pkt.write_byte(ServerPacketID::AmbientColor.to_byte());
    pkt.write_byte(r);
    pkt.write_byte(g);
    pkt.write_byte(b);
    pkt.into_bytes()
}

/// ID 197: Full guild bank slot data (SBG).
/// slot is 1-based. Pass obj_idx=0 for empty slots.
#[allow(clippy::too_many_arguments)]
pub fn write_guild_bank_slot_data(
    slot: u8, obj_idx: i16, name: &str, amount: i16,
    grh: i16, obj_type: u8, max_hit: i16, min_hit: i16, max_def: i16,
    bank_gold: i64, user_gold: i64,
) -> Vec<u8> {
    let mut pkt = ByteQueue::new();
    pkt.write_byte(ServerPacketID::GuildBankSlotData.to_byte());
    pkt.write_byte(slot);
    pkt.write_integer(obj_idx);
    pkt.write_ascii_string(name);
    pkt.write_integer(amount);
    pkt.write_integer(grh);
    pkt.write_byte(obj_type);
    pkt.write_integer(max_hit);
    pkt.write_integer(min_hit);
    pkt.write_integer(max_def);
    pkt.write_long(bank_gold as i32);
    pkt.write_long(user_gold as i32);
    pkt.into_bytes()
}

/// Craft list item (for blacksmith weapons/armors and carpenter).
pub struct CraftItem {
    pub name: String,
    pub grh_index: i16,
    pub mat1: i16,       // LingH (smith) or Madera (carp)
    pub mat2: i16,       // LingP (smith) or MaderaElfica (carp)
    pub mat3: i16,       // LingO (smith) or 0 (carp)
    pub obj_index: i16,
    pub upgrade: i16,
}

/// ID 158: Buildable weapons list for blacksmith (VB6 13.3 binary format).
pub fn write_smith_weapons(items: &[CraftItem]) -> Vec<u8> {
    let mut pkt = ByteQueue::new();
    pkt.write_byte(ServerPacketID::SmithWeapons.to_byte());
    pkt.write_integer(items.len() as i16);
    for item in items {
        pkt.write_ascii_string(&item.name);
        pkt.write_integer(item.grh_index);
        pkt.write_integer(item.mat1);
        pkt.write_integer(item.mat2);
        pkt.write_integer(item.mat3);
        pkt.write_integer(item.obj_index);
        pkt.write_integer(item.upgrade);
    }
    pkt.into_bytes()
}

/// ID 223: Mini top / ranking data (MTOP).
pub fn write_mini_top_data(data: &str) -> Vec<u8> {
    let mut pkt = ByteQueue::new();
    pkt.write_byte(ServerPacketID::MiniTopData.to_byte());
    pkt.write_ascii_string(data);
    pkt.into_bytes()
}

/// ID 159: Buildable armors/shields/helmets list for blacksmith (VB6 13.3 binary format).
pub fn write_smith_armors(items: &[CraftItem]) -> Vec<u8> {
    let mut pkt = ByteQueue::new();
    pkt.write_byte(ServerPacketID::SmithArmors.to_byte());
    pkt.write_integer(items.len() as i16);
    for item in items {
        pkt.write_ascii_string(&item.name);
        pkt.write_integer(item.grh_index);
        pkt.write_integer(item.mat1);
        pkt.write_integer(item.mat2);
        pkt.write_integer(item.mat3);
        pkt.write_integer(item.obj_index);
        pkt.write_integer(item.upgrade);
    }
    pkt.into_bytes()
}

/// ID 160: Buildable items list for carpenter (VB6 13.3 binary format).
pub fn write_carp_items(items: &[CraftItem]) -> Vec<u8> {
    let mut pkt = ByteQueue::new();
    pkt.write_byte(ServerPacketID::CarpItems.to_byte());
    pkt.write_integer(items.len() as i16);
    for item in items {
        pkt.write_ascii_string(&item.name);
        pkt.write_integer(item.grh_index);
        pkt.write_integer(item.mat1);   // Madera
        pkt.write_integer(item.mat2);   // MaderaElfica
        pkt.write_integer(item.obj_index);
        pkt.write_integer(item.upgrade);
    }
    pkt.into_bytes()
}
