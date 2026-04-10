// Ban system — PostgreSQL-backed with in-memory cache.
//
// Bans are loaded into memory at startup for O(1) lookups.
// Mutations write to both memory and DB.

use sqlx::PgPool;
use std::collections::HashSet;

/// Ban list with in-memory cache.
#[derive(Debug, Clone)]
pub struct BanList {
    pub banned_hds: HashSet<String>,
    pub banned_ips: HashSet<String>,
}

impl BanList {
    /// Load ban lists from the database.
    pub async fn load(pool: &PgPool) -> Self {
        let banned_ips: Vec<String> = sqlx::query_scalar("SELECT ip FROM banned_ips")
            .fetch_all(pool)
            .await
            .unwrap_or_default();

        let banned_hds: Vec<String> = sqlx::query_scalar("SELECT hd FROM banned_hds")
            .fetch_all(pool)
            .await
            .unwrap_or_default();

        tracing::info!(
            "Ban lists loaded: {} HDs, {} IPs",
            banned_hds.len(),
            banned_ips.len()
        );

        Self {
            banned_hds: banned_hds.into_iter().collect(),
            banned_ips: banned_ips.into_iter().collect(),
        }
    }

    /// Check if a hardware serial is banned.
    pub fn is_hd_banned(&self, hd: &str) -> bool {
        self.banned_hds.contains(&hd.to_uppercase())
    }

    /// Check if an IP address is banned.
    pub fn is_ip_banned(&self, ip: &str) -> bool {
        self.banned_ips.contains(ip)
    }

    /// Add an HD to the ban list (memory + DB).
    pub async fn ban_hd(&mut self, pool: &PgPool, hd: &str) -> Result<(), String> {
        let hd_upper = hd.to_uppercase();
        self.banned_hds.insert(hd_upper.clone());
        sqlx::query("INSERT INTO banned_hds (hd) VALUES ($1) ON CONFLICT DO NOTHING")
            .bind(&hd_upper)
            .execute(pool)
            .await
            .map_err(|e| format!("DB error banning HD: {}", e))?;
        Ok(())
    }

    /// Add an IP to the ban list (memory + DB).
    pub async fn ban_ip(&mut self, pool: &PgPool, ip: &str) -> Result<(), String> {
        self.banned_ips.insert(ip.to_string());
        sqlx::query("INSERT INTO banned_ips (ip) VALUES ($1) ON CONFLICT DO NOTHING")
            .bind(ip)
            .execute(pool)
            .await
            .map_err(|e| format!("DB error banning IP: {}", e))?;
        Ok(())
    }

    /// Remove an IP from the ban list (memory + DB).
    pub async fn unban_ip(&mut self, pool: &PgPool, ip: &str) -> bool {
        let removed = self.banned_ips.remove(ip);
        if removed {
            let _ = sqlx::query("DELETE FROM banned_ips WHERE ip = $1")
                .bind(ip)
                .execute(pool)
                .await;
        }
        removed
    }
}
