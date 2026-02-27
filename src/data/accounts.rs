// Account file I/O — reads and writes Accounts/*.act (INI format).
//
// Account file structure:
// [AccountName]     → password, PIN, Ban, Motivo, BANCO
// [SEGURIDAD]       → CodeX (security code)
// [PJS]             → NumPjs, PJ1..PJ10 (character names)
// [BancoInventory]  → CantidadItems, Obj1..Obj40
// [AMIGOS]          → A1..A20 (friend list)

use std::path::{Path, PathBuf};
use crate::config::IniFile;

const MAX_PJS: usize = 10;
const MAX_BANK_SLOTS: usize = 40;
const MAX_FRIENDS: usize = 20;

/// Account data loaded from an .act file.
#[derive(Debug, Clone)]
pub struct AccountData {
    pub name: String,
    pub password: String,
    pub pin: String,
    pub banned: bool,
    pub ban_reason: String,
    pub banco: i64,
    pub security_code: String,
    pub num_pjs: usize,
    pub characters: Vec<String>, // PJ1..PJ10 (non-empty names only kept in order)
}

/// Resolve the path to an account file.
pub fn account_path(base: &Path, account_name: &str) -> PathBuf {
    base.join("Accounts").join(format!("{}.act", account_name.to_uppercase()))
}

/// Check if an account exists on disk.
pub fn account_exists(base: &Path, account_name: &str) -> bool {
    account_path(base, account_name).exists()
}

/// Load an account from its .act file.
pub fn load_account(base: &Path, account_name: &str) -> Result<AccountData, String> {
    let path = account_path(base, account_name);
    let ini = IniFile::load(&path)
        .map_err(|e| format!("Failed to load account '{}': {}", account_name, e))?;

    // The main section uses the account name as header
    let section = account_name;

    let password = ini.get(section, "password").unwrap_or_default();
    let pin = ini.get(section, "PIN").unwrap_or_default();
    let banned = ini.get(section, "Ban").map(|s| s == "1").unwrap_or(false);
    let ban_reason = ini.get(section, "Motivo").unwrap_or_default();
    let banco = ini.get(section, "BANCO")
        .and_then(|s| s.parse().ok())
        .unwrap_or(0);

    let security_code = ini.get("SEGURIDAD", "CodeX").unwrap_or_default();

    let num_pjs = ini.get("PJS", "NumPjs")
        .and_then(|s| s.parse().ok())
        .unwrap_or(0usize);

    let mut characters = Vec::new();
    for i in 1..=MAX_PJS {
        let pj = ini.get("PJS", &format!("PJ{}", i)).unwrap_or_default();
        characters.push(pj);
    }

    Ok(AccountData {
        name: account_name.to_string(),
        password,
        pin,
        banned,
        ban_reason,
        banco,
        security_code,
        num_pjs,
        characters,
    })
}

/// Create a new account file on disk.
/// Returns Ok(()) on success, Err with message if account already exists or write fails.
pub fn create_account(
    base: &Path,
    account_name: &str,
    password: &str,
    pin: &str,
    security_code: &str,
) -> Result<(), String> {
    let path = account_path(base, account_name);

    if path.exists() {
        return Err("El nombre de la cuenta ya esta siendo utilizado por otro usuario.".into());
    }

    // Ensure Accounts directory exists
    if let Some(parent) = path.parent() {
        std::fs::create_dir_all(parent)
            .map_err(|e| format!("Failed to create Accounts directory: {}", e))?;
    }

    // Build the file content matching VB6's format exactly
    let mut lines = Vec::new();

    // [AccountName] section
    lines.push(format!("[{}]", account_name));
    lines.push(format!("password={}", password));
    lines.push(format!("PIN={}", pin));
    lines.push("Ban=0".into());
    lines.push("BANCO=0".into());

    // [SEGURIDAD] section
    lines.push("[SEGURIDAD]".into());
    lines.push(format!("CodeX={}", security_code));

    // [PJS] section
    lines.push("[PJS]".into());
    lines.push("NumPjs=0".into());
    for i in 1..=MAX_PJS {
        lines.push(format!("PJ{}=", i));
    }

    // [BancoInventory] section
    lines.push("[BancoInventory]".into());
    lines.push("CantidadItems=0".into());
    for i in 1..=MAX_BANK_SLOTS {
        lines.push(format!("Obj{}=0-0", i));
    }

    // [AMIGOS] section
    lines.push("[AMIGOS]".into());
    for i in 1..=MAX_FRIENDS {
        lines.push(format!("A{}=(Nadie)", i));
    }

    // Write with CRLF line endings (VB6 compat)
    let content = lines.join("\r\n") + "\r\n";
    std::fs::write(&path, content)
        .map_err(|e| format!("Failed to write account file: {}", e))?;

    Ok(())
}

/// Add a character name to an account's PJ list.
pub fn add_character_to_account(
    base: &Path,
    account_name: &str,
    char_name: &str,
) -> Result<(), String> {
    let path = account_path(base, account_name);
    let ini = IniFile::load(&path)
        .map_err(|e| format!("Failed to load account: {}", e))?;

    let num_pjs: usize = ini.get("PJS", "NumPjs")
        .and_then(|s| s.parse().ok())
        .unwrap_or(0);

    if num_pjs >= MAX_PJS {
        return Err("La cuenta ya tiene el maximo de personajes.".into());
    }

    let new_slot = num_pjs + 1;
    let path_str = path.to_string_lossy().to_string();

    crate::config::write_var(&path_str, "PJS", "NumPjs", &new_slot.to_string())
        .map_err(|e| format!("Failed to update NumPjs: {}", e))?;
    crate::config::write_var(&path_str, "PJS", &format!("PJ{}", new_slot), char_name)
        .map_err(|e| format!("Failed to write PJ slot: {}", e))?;

    Ok(())
}

/// Remove a character from an account's PJ list and shift remaining entries up.
pub fn remove_character_from_account(
    base: &Path,
    account_name: &str,
    char_name: &str,
) -> Result<(), String> {
    let path = account_path(base, account_name);
    let mut ini = IniFile::load(&path)
        .map_err(|e| format!("Failed to load account: {}", e))?;

    let num_pjs: usize = ini.get("PJS", "NumPjs")
        .and_then(|s| s.parse().ok())
        .unwrap_or(0);

    // Find the character slot
    let mut found_slot = None;
    for i in 1..=num_pjs {
        let pj = ini.get("PJS", &format!("PJ{}", i)).unwrap_or_default();
        if pj.to_uppercase() == char_name.to_uppercase() {
            found_slot = Some(i);
            break;
        }
    }

    let slot = found_slot.ok_or("Character not found in account.")?;
    let path_str = path.to_string_lossy().to_string();

    // Shift remaining characters up
    for i in slot..num_pjs {
        let next_pj = ini.get("PJS", &format!("PJ{}", i + 1)).unwrap_or_default();
        ini.set("PJS", &format!("PJ{}", i), &next_pj);
        crate::config::write_var(&path_str, "PJS", &format!("PJ{}", i), &next_pj)
            .map_err(|e| format!("Failed to shift PJ: {}", e))?;
    }

    // Clear last slot and decrement count
    crate::config::write_var(&path_str, "PJS", &format!("PJ{}", num_pjs), "")
        .map_err(|e| format!("Failed to clear last PJ: {}", e))?;
    crate::config::write_var(&path_str, "PJS", "NumPjs", &(num_pjs - 1).to_string())
        .map_err(|e| format!("Failed to update NumPjs: {}", e))?;

    Ok(())
}

/// Update the account password.
pub fn update_password(base: &Path, account_name: &str, new_password: &str) -> Result<(), String> {
    let path = account_path(base, account_name);
    let path_str = path.to_string_lossy().to_string();
    crate::config::write_var(&path_str, account_name, "Password", new_password)
        .map_err(|e| format!("Failed to update password: {}", e))
}

#[cfg(test)]
mod tests {
    use super::*;
    use std::fs;

    fn setup_temp_dir(name: &str) -> PathBuf {
        let dir = std::env::temp_dir().join(format!("ao_test_{}_{}", std::process::id(), name));
        let _ = fs::remove_dir_all(&dir);
        fs::create_dir_all(dir.join("Accounts")).unwrap();
        dir
    }

    #[test]
    fn create_and_load_account() {
        let base = setup_temp_dir("create_load");
        create_account(&base, "TestUser", "pass123", "1234", "CODEX99").unwrap();

        assert!(account_exists(&base, "TestUser"));

        let acc = load_account(&base, "TestUser").unwrap();
        assert_eq!(acc.password, "pass123");
        assert_eq!(acc.pin, "1234");
        assert_eq!(acc.security_code, "CODEX99");
        assert!(!acc.banned);
        assert_eq!(acc.num_pjs, 0);

        let _ = fs::remove_dir_all(&base);
    }

    #[test]
    fn create_duplicate_fails() {
        let base = setup_temp_dir("dupe");
        create_account(&base, "DupeUser", "p1", "1111", "C1").unwrap();
        let result = create_account(&base, "DupeUser", "p2", "2222", "C2");
        assert!(result.is_err());
        let _ = fs::remove_dir_all(&base);
    }

    #[test]
    fn add_and_remove_characters() {
        let base = setup_temp_dir("add_remove");
        create_account(&base, "CharTest", "pass", "pin", "code").unwrap();

        add_character_to_account(&base, "CharTest", "Warrior").unwrap();
        add_character_to_account(&base, "CharTest", "Mage").unwrap();

        let acc = load_account(&base, "CharTest").unwrap();
        assert_eq!(acc.num_pjs, 2);
        assert_eq!(acc.characters[0], "Warrior");
        assert_eq!(acc.characters[1], "Mage");

        remove_character_from_account(&base, "CharTest", "Warrior").unwrap();
        let acc = load_account(&base, "CharTest").unwrap();
        assert_eq!(acc.num_pjs, 1);
        assert_eq!(acc.characters[0], "Mage");

        let _ = fs::remove_dir_all(&base);
    }
}
