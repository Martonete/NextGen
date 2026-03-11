//! Social/UI packets: Friends, Mail, Chat system, Guild info/bank, Quests, Auction, Cosmetic.

use crate::protocol::byte_queue::ByteQueue;
use crate::protocol::packets::ServerPacketID;

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
