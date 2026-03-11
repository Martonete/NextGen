//! Server tick handlers — orchestrator module.
//!
//! Split into focused sub-modules:
//! - `npc_ai` — NPC AI, healing, respawn
//! - `npc_move` — NPC movement packets, area updates, NPC-vs-NPC combat
//! - `player` — meditation, passive effects (regen, hunger, poison), auto-save
//! - `world` — anti-cheat intervals, world cleanup, security

mod npc_ai;
mod npc_move;
mod player;
mod world;

pub use npc_ai::*;
pub use npc_move::*;
pub use player::*;
pub use world::*;
