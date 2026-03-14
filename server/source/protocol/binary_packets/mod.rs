//! Binary packet builders for all Server->Client packets (13.3 protocol).
//!
//! Each function builds a complete binary packet using ByteQueue.
//! Packet format: [PacketID: 1 byte] [Field1] [Field2] ... [Fieldn]
//!
//! Split into focused sub-modules:
//! - `core` — Auth/Login, Map/Position, Character, Objects, Chat, Stats
//! - `inventory` — Inventory/Spells, Commerce/Bank/Trade, Crafting, Toggles, Guild, Signals
//! - `systems` — MultiMessage, GM Panel, NPC Trade, Pre-login, Navigation, Work
//! - `social` — Chat system, Guild info/bank, Quests, Auction, Cosmetic

mod core;
mod inventory;
mod systems;
mod social;

pub use self::core::*;
pub use inventory::*;
pub use systems::*;
pub use social::*;
