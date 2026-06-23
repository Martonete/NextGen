/// INI file parser compatible with VB6's GetPrivateProfileString / WritePrivateProfileString.
///
/// Handles:
/// - [Section] headers
/// - Key=Value pairs
/// - Case-insensitive section and key lookup (VB6 behavior)
/// - Comments starting with ; or '
/// - Preserves original formatting for write-back
use std::collections::HashMap;
use std::path::Path;

/// Parsed INI file.
#[derive(Debug, Clone)]
pub struct IniFile {
    /// Sections mapped by lowercase name → (key_lowercase → value)
    sections: HashMap<String, HashMap<String, String>>,
    /// Original file path for write-back
    path: Option<std::path::PathBuf>,
}

impl IniFile {
    /// Load an INI file from disk.
    /// Supports both UTF-8 and Latin-1 (Windows-1252) encoding,
    /// which is what VB6 applications typically produce.
    pub fn load(path: &Path) -> Result<Self, std::io::Error> {
        let bytes = std::fs::read(path)?;
        // Detect encoding: UTF-16 LE BOM (FF FE), UTF-16 BE BOM (FE FF), UTF-8, Latin-1
        let content = if bytes.len() >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE {
            // UTF-16 LE: decode pairs of bytes (skip 2-byte BOM)
            let u16s: Vec<u16> = bytes[2..]
                .chunks_exact(2)
                .map(|pair| u16::from_le_bytes([pair[0], pair[1]]))
                .collect();
            String::from_utf16_lossy(&u16s)
        } else if bytes.len() >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF {
            // UTF-16 BE: decode pairs of bytes (skip 2-byte BOM)
            let u16s: Vec<u16> = bytes[2..]
                .chunks_exact(2)
                .map(|pair| u16::from_be_bytes([pair[0], pair[1]]))
                .collect();
            String::from_utf16_lossy(&u16s)
        } else {
            // Try UTF-8 first, fall back to Latin-1 (ISO-8859-1)
            match String::from_utf8(bytes.clone()) {
                Ok(s) => s,
                Err(_) => {
                    // Latin-1: each byte maps directly to its Unicode code point
                    bytes.iter().map(|&b| b as char).collect()
                }
            }
        };
        let mut ini = Self::parse(&content);
        ini.path = Some(path.to_path_buf());
        Ok(ini)
    }

    /// Parse INI content from a string.
    pub fn parse(content: &str) -> Self {
        let mut sections: HashMap<String, HashMap<String, String>> = HashMap::new();
        let mut current_section = String::new();

        for line in content.lines() {
            let trimmed = line.trim();

            // Skip empty lines and comments
            if trimmed.is_empty() || trimmed.starts_with(';') || trimmed.starts_with('\'') {
                continue;
            }

            // Section header
            if trimmed.starts_with('[') {
                if let Some(end) = trimmed.find(']') {
                    current_section = trimmed[1..end].to_lowercase();
                    sections.entry(current_section.clone()).or_default();
                }
                continue;
            }

            // Key=Value pair
            if let Some(eq_pos) = trimmed.find('=') {
                let key = trimmed[..eq_pos].trim().to_lowercase();
                let raw_value = trimmed[eq_pos + 1..].trim();
                // Strip VB6 inline comments: " '" (space + apostrophe) starts a comment
                // But preserve apostrophes inside values (e.g. Name=O'Riley)
                let value = if let Some(comment_pos) = raw_value.find(" '") {
                    raw_value[..comment_pos].trim().to_string()
                } else {
                    raw_value.to_string()
                };
                sections
                    .entry(current_section.clone())
                    .or_default()
                    .insert(key, value);
            }
        }

        Self {
            sections,
            path: None,
        }
    }

    /// Get a value from a section (case-insensitive).
    ///
    /// Mirrors VB6: `GetVar(filepath, section, key)`
    pub fn get(&self, section: &str, key: &str) -> Option<String> {
        self.sections
            .get(&section.to_lowercase())
            .and_then(|s| s.get(&key.to_lowercase()))
            .cloned()
    }

    /// Set a value in a section.
    pub fn set(&mut self, section: &str, key: &str, value: &str) {
        self.sections
            .entry(section.to_lowercase())
            .or_default()
            .insert(key.to_lowercase(), value.to_string());
    }

    /// Get all keys in a section.
    pub fn keys(&self, section: &str) -> Vec<String> {
        self.sections
            .get(&section.to_lowercase())
            .map(|s| s.keys().cloned().collect())
            .unwrap_or_default()
    }

    /// Get all section names.
    pub fn section_names(&self) -> Vec<String> {
        self.sections.keys().cloned().collect()
    }

    /// Save the INI file to disk. Writes all sections and key=value pairs.
    pub fn save(&self, path: &Path) -> Result<(), std::io::Error> {
        // Ensure parent directory exists
        if let Some(parent) = path.parent() {
            std::fs::create_dir_all(parent)?;
        }
        let mut content = String::new();
        // Sort sections for deterministic output
        let mut section_names: Vec<&String> = self.sections.keys().collect();
        section_names.sort();
        for section in section_names {
            if let Some(keys) = self.sections.get(section) {
                content.push_str(&format!("[{}]\n", section));
                let mut key_names: Vec<&String> = keys.keys().collect();
                key_names.sort();
                for key in key_names {
                    if let Some(value) = keys.get(key) {
                        content.push_str(&format!("{}={}\n", key, value));
                    }
                }
                content.push('\n');
            }
        }
        std::fs::write(path, content)
    }
}

impl Default for IniFile {
    fn default() -> Self {
        Self {
            sections: HashMap::new(),
            path: None,
        }
    }
}

/// Convenience function matching VB6's GetVar.
///
/// `get_var("path/file.ini", "SECTION", "Key")` → value or empty string
pub fn get_var(path: &str, section: &str, key: &str) -> String {
    IniFile::load(Path::new(path))
        .ok()
        .and_then(|ini| ini.get(section, key))
        .unwrap_or_default()
}

/// Convenience function matching VB6's WriteVar.
///
/// Reads the file, updates the key, and writes it back.
pub fn write_var(path: &str, section: &str, key: &str, value: &str) -> Result<(), std::io::Error> {
    let file_path = Path::new(path);

    // Read existing content or start empty
    let content = std::fs::read_to_string(file_path).unwrap_or_default();
    let lines: Vec<&str> = content.lines().collect();

    let section_lower = section.to_lowercase();
    let key_lower = key.to_lowercase();

    let mut result = Vec::new();
    let mut in_target_section = false;
    let mut key_written = false;
    let mut section_found = false;

    for line in &lines {
        let trimmed = line.trim();

        if trimmed.starts_with('[') {
            // If we were in target section and didn't write the key, add it now
            if in_target_section && !key_written {
                result.push(format!("{}={}", key, value));
                key_written = true;
            }

            if let Some(end) = trimmed.find(']') {
                let sec_name = trimmed[1..end].to_lowercase();
                in_target_section = sec_name == section_lower;
                if in_target_section {
                    section_found = true;
                }
            }
            result.push(line.to_string());
            continue;
        }

        if in_target_section {
            if let Some(eq_pos) = trimmed.find('=') {
                let k = trimmed[..eq_pos].trim().to_lowercase();
                if k == key_lower {
                    result.push(format!("{}={}", &trimmed[..eq_pos].trim(), value));
                    key_written = true;
                    continue;
                }
            }
        }

        result.push(line.to_string());
    }

    // Handle edge cases
    if !section_found {
        result.push(format!("[{}]", section));
        result.push(format!("{}={}", key, value));
    } else if !key_written {
        // Section exists but key wasn't found or written
        // Insert before next section or at end
        result.push(format!("{}={}", key, value));
    }

    std::fs::write(file_path, result.join("\r\n") + "\r\n")
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn parse_basic() {
        let content = "[INIT]\nServerIp=127.0.0.1\nPort=5028\n[OTHER]\nFoo=Bar";
        let ini = IniFile::parse(content);
        assert_eq!(ini.get("INIT", "ServerIp"), Some("127.0.0.1".into()));
        assert_eq!(ini.get("INIT", "Port"), Some("5028".into()));
        assert_eq!(ini.get("OTHER", "Foo"), Some("Bar".into()));
    }

    #[test]
    fn case_insensitive() {
        let content = "[Init]\nServerIP=127.0.0.1";
        let ini = IniFile::parse(content);
        assert_eq!(ini.get("init", "serverip"), Some("127.0.0.1".into()));
        assert_eq!(ini.get("INIT", "SERVERIP"), Some("127.0.0.1".into()));
    }

    #[test]
    fn missing_values() {
        let content = "[INIT]\nPort=5028";
        let ini = IniFile::parse(content);
        assert_eq!(ini.get("INIT", "Missing"), None);
        assert_eq!(ini.get("NOSECTION", "Port"), None);
    }

    #[test]
    fn skip_comments() {
        let content = "[INIT]\n;This is a comment\n'Also a comment\nPort=5028";
        let ini = IniFile::parse(content);
        assert_eq!(ini.get("INIT", "Port"), Some("5028".into()));
    }

    #[test]
    fn values_with_equals() {
        // Some values might contain = sign
        let content = "[INIT]\nNotice=Welcome to AO = fun!";
        let ini = IniFile::parse(content);
        assert_eq!(
            ini.get("INIT", "Notice"),
            Some("Welcome to AO = fun!".into())
        );
    }
}
