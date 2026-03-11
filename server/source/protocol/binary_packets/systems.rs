//! System packets: MultiMessage, GM Panel, NPC Trade, Pre-login, Crafting lists, Navigation.

use crate::protocol::byte_queue::ByteQueue;
use crate::protocol::packets::{ServerPacketID, MultiMessageID};

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

