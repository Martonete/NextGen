// Ranking system — PostgreSQL-backed with in-memory cache.
//
// Rankings are kept in memory for fast access. Mutations write to both.
// Re-exports existing types from data::ranking (RankingData, RankingType, etc.)
// since the data structures are the same — only the persistence layer changes.

use sqlx::PgPool;
use crate::data::ranking::{RankingData, RankingType, RankingEntry, MAX_TOP};

/// Load all rankings from DB into memory.
pub async fn load_ranking(pool: &PgPool) -> RankingData {
    let mut data = RankingData::default();

    let rows: Vec<(String, i16, String, i64)> = sqlx::query_as(
        "SELECT category, position, name, value FROM rankings ORDER BY category, position"
    )
    .fetch_all(pool)
    .await
    .unwrap_or_default();

    for (category, position, name, value) in rows {
        let rt = match category.as_str() {
            "FRAGS" => RankingType::Frags,
            "TORNEOS" => RankingType::Torneos,
            "DUELOS" => RankingType::Duelos,
            "PAREJAS" => RankingType::Parejas,
            "REPUTACION" => RankingType::Reputacion,
            "RONDAS" => RankingType::Rondas,
            "CVCS" => RankingType::CVCs,
            "CASTILLOS" => RankingType::Castillos,
            "REPUCLAN" => RankingType::RepuClanes,
            _ => continue,
        };
        let pos = position as usize;
        if pos < MAX_TOP {
            let top = data.get_mut(rt);
            top.entries[pos] = RankingEntry { name, value };
        }
    }

    let total: usize = data.rankings.iter()
        .flat_map(|t| t.entries.iter())
        .filter(|e| !e.name.is_empty())
        .count();
    tracing::info!("Rankings loaded from DB: {} entries", total);

    data
}

/// Save a single ranking category to DB.
pub async fn save_ranking(pool: &PgPool, data: &RankingData, rank: RankingType) {
    let section = rank.section_name();
    let top = data.get(rank);

    for i in 0..MAX_TOP {
        let entry = &top.entries[i];
        let name = if entry.name.is_empty() { "N/A" } else { &entry.name };
        let _ = sqlx::query(
            "INSERT INTO rankings (category, position, name, value)
             VALUES ($1, $2, $3, $4)
             ON CONFLICT (category, position)
             DO UPDATE SET name = $3, value = $4"
        )
        .bind(section)
        .bind(i as i16)
        .bind(name)
        .bind(entry.value)
        .execute(pool)
        .await;
    }
}

/// Save all rankings to DB.
pub async fn save_all_rankings(pool: &PgPool, data: &RankingData) {
    for rt in RankingType::all() {
        save_ranking(pool, data, *rt).await;
    }
}
