//! Inventory handlers — thin re-export module.
//!
//! Split into focused sub-modules:
//! - `equip` — equip/unequip logic
//! - `use_item` — use item (potions, food, keys, scrolls, etc.)
//! - `ground` — pickup, drop items, drop gold
//! - `click` — left click (look at), right click (interact)
//! - `doors` — door interaction, forum system, safe toggle

mod equip;
mod use_item;
mod ground;
mod click;
mod doors;

pub(super) use equip::*;
pub(super) use use_item::*;
pub(super) use ground::*;
pub(super) use click::*;
pub(super) use doors::*;
