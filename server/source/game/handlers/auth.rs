//! Authentication handlers — split into focused sub-modules.
//!
//! - `auth_login`: hardware check, account login/creation, character select/login, connect_user
//! - `auth_char`: character creation/deletion, dice roll, password change, account recovery

#[path = "auth_login.rs"]
mod auth_login;
#[path = "auth_char.rs"]
mod auth_char;

pub(crate) use auth_login::*;
pub(crate) use auth_char::*;
