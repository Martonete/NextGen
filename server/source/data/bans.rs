// Ban system — loads and checks banned HDs and IPs.
//
// Files:
//   dat/BanHds.dat  — one HD serial per line
//   dat/BanIps.dat  — one IP address per line

use std::collections::HashSet;
use std::path::Path;

/// Loaded ban lists.
#[derive(Debug, Clone)]
pub struct BanList {
    pub banned_hds: HashSet<String>,
    pub banned_ips: HashSet<String>,
}

impl BanList {
    /// Load ban lists from the dat/ directory.
    pub fn load(base: &Path) -> Self {
        let banned_hds = load_lines(&base.join("dat").join("BanHds.dat"));
        let banned_ips = load_lines(&base.join("dat").join("BanIps.dat"));

        tracing::info!(
            "Ban lists loaded: {} HDs, {} IPs",
            banned_hds.len(),
            banned_ips.len()
        );

        Self { banned_hds, banned_ips }
    }

    /// Check if a hardware serial is banned.
    pub fn is_hd_banned(&self, hd: &str) -> bool {
        self.banned_hds.contains(&hd.to_uppercase())
    }

    /// Check if an IP address is banned.
    pub fn is_ip_banned(&self, ip: &str) -> bool {
        self.banned_ips.contains(ip)
    }

    /// Add an HD to the ban list and persist to disk.
    pub fn ban_hd(&mut self, base: &Path, hd: &str) -> Result<(), std::io::Error> {
        let hd_upper = hd.to_uppercase();
        self.banned_hds.insert(hd_upper.clone());
        append_line(&base.join("dat").join("BanHds.dat"), &hd_upper)
    }

    /// Add an IP to the ban list and persist to disk.
    pub fn ban_ip(&mut self, base: &Path, ip: &str) -> Result<(), std::io::Error> {
        self.banned_ips.insert(ip.to_string());
        append_line(&base.join("dat").join("BanIps.dat"), ip)
    }

    /// Remove an IP from the ban list and rewrite disk file.
    pub fn unban_ip(&mut self, ip: &str) -> bool {
        self.banned_ips.remove(ip)
    }

}

/// Load non-empty, trimmed lines from a file into a HashSet.
fn load_lines(path: &Path) -> HashSet<String> {
    match std::fs::read_to_string(path) {
        Ok(content) => content
            .lines()
            .map(|l| l.trim().to_uppercase())
            .filter(|l| !l.is_empty())
            .collect(),
        Err(_) => HashSet::new(),
    }
}

/// Append a line to a file (creating it if needed).
fn append_line(path: &Path, line: &str) -> Result<(), std::io::Error> {
    use std::io::Write;
    if let Some(parent) = path.parent() {
        std::fs::create_dir_all(parent)?;
    }
    let mut file = std::fs::OpenOptions::new()
        .create(true)
        .append(true)
        .open(path)?;
    writeln!(file, "{}", line)
}
