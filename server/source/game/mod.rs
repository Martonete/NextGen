// Game module — core game logic.
//
// Contains:
// - types: UserState, GameState (per-connection and global state)
// - world: map grids, area visibility, character index management
// - handlers: packet routing and login flow implementation

pub mod types;
pub mod world;
pub mod npc;
#[allow(dead_code, unused_variables, unused_imports)]
pub mod handlers;
