// Game module — core game logic.
//
// Contains:
// - types: UserState, GameState (per-connection and global state)
// - world: map grids, area visibility, character index management
// - handlers: packet routing and login flow implementation

pub mod class_race;
pub mod constants;
pub mod handlers;
pub mod npc;
pub mod types;
pub mod world;
pub mod zones;
