//! Social/UI packets: Chat system, Guild info/bank, Quests, Auction, Cosmetic.

use crate::protocol::byte_queue::ByteQueue;
use crate::protocol::packets::ServerPacketID;

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

// ── Triggers (simple no-payload packets) ──────────────────

/// ID 251: Open travels panel (TRAVELS).
pub fn write_travels() -> Vec<u8> {
    let mut pkt = ByteQueue::new();
    pkt.write_byte(ServerPacketID::TravelsOpen.to_byte());
    pkt.into_bytes()
}

/// ID 146: Inventory init (INVI0).
pub fn write_inv_init() -> Vec<u8> {
    let mut pkt = ByteQueue::new();
    pkt.write_byte(ServerPacketID::InvInit.to_byte());
    pkt.into_bytes()
}

// ── Tournament / Response ─────────────────────────────────

/// ID 177: Response text (RESPUES).
pub fn write_response(text: &str, name: &str) -> Vec<u8> {
    let mut pkt = ByteQueue::new();
    pkt.write_byte(ServerPacketID::ResponseMsg.to_byte());
    pkt.write_ascii_string(text);
    pkt.write_ascii_string(name);
    pkt.into_bytes()
}

/// ID 179: Auction bid info (GVN).
pub fn write_auction_bid(data: &str) -> Vec<u8> {
    let mut pkt = ByteQueue::new();
    pkt.write_byte(ServerPacketID::AuctionBid.to_byte());
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


// ── Particle / Light ──────────────────────────────────────

/// ID 243: Create particle (PCF).
pub fn write_particle_create(particle_group: i16, x: i16, y: i16, layer: u8) -> Vec<u8> {
    let mut pkt = ByteQueue::new();
    pkt.write_byte(ServerPacketID::ParticleCreate.to_byte());
    pkt.write_integer(particle_group);
    pkt.write_integer(x);
    pkt.write_integer(y);
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
pub fn write_light_create(x: i16, y: i16, range: u8, r: u8, g: u8, b: u8) -> Vec<u8> {
    let mut pkt = ByteQueue::new();
    pkt.write_byte(ServerPacketID::LightCreate.to_byte());
    pkt.write_integer(x);
    pkt.write_integer(y);
    pkt.write_byte(range);
    pkt.write_byte(r);
    pkt.write_byte(g);
    pkt.write_byte(b);
    pkt.into_bytes()
}

// ── Additional event / GM / crafting packets ──────────────────────────

/// ID 164: Ambient map RGB color (PCR).
pub fn write_ambient_color(r: u8, g: u8, b: u8) -> Vec<u8> {
    let mut pkt = ByteQueue::new();
    pkt.write_byte(ServerPacketID::AmbientColor.to_byte());
    pkt.write_byte(r);
    pkt.write_byte(g);
    pkt.write_byte(b);
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
