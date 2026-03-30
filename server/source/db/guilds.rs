// Guild persistence — PostgreSQL.
//
// Replaces data/guilds.rs (INI file I/O).

use sqlx::{PgPool, Postgres, Transaction};

pub const MAX_GUILDS: i32 = 1000;
pub const MAX_CODEX_LINES: usize = 8;
pub const MAX_ASPIRANTES: usize = 10;
pub const MAX_ANTIFACCION: i32 = 5;
pub const MAX_GUILD_BANK_SLOTS: usize = 40;
pub const MAX_GUILD_BANK_GOLD: i64 = 999_999_999;

pub const ALIGN_LEGION: i32 = 1;
pub const ALIGN_CRIMINAL: i32 = 2;
pub const ALIGN_NEUTRO: i32 = 3;
pub const ALIGN_CIUDA: i32 = 4;
pub const ALIGN_ARMADA: i32 = 5;
pub const ALIGN_MASTER: i32 = 6;

pub const REL_GUERRA: i32 = -1;
pub const REL_PAZ: i32 = 0;
pub const REL_ALIADOS: i32 = 1;

pub fn max_members_for_level(level: i32) -> i32 {
    level * 4
}

pub fn alignment_name(align: i32) -> &'static str {
    match align {
        ALIGN_LEGION => "Legion oscura",
        ALIGN_CRIMINAL => "Criminal",
        ALIGN_NEUTRO => "Neutro",
        ALIGN_CIUDA => "Legal",
        ALIGN_ARMADA => "Armada Real",
        ALIGN_MASTER => "Game Masters",
        _ => "Neutro",
    }
}

pub fn alignment_from_name(name: &str) -> i32 {
    match name.to_lowercase().as_str() {
        "legion oscura" => ALIGN_LEGION,
        "criminal" => ALIGN_CRIMINAL,
        "neutro" => ALIGN_NEUTRO,
        "legal" => ALIGN_CIUDA,
        "armada real" => ALIGN_ARMADA,
        "game masters" => ALIGN_MASTER,
        _ => ALIGN_NEUTRO,
    }
}

pub fn is_valid_guild_name(name: &str) -> bool {
    if name.is_empty() || name.len() > 40 {
        return false;
    }
    name.chars().all(|c| c.is_ascii_alphabetic() || c == ' ')
}

/// Guild info.
#[derive(Debug, Clone)]
pub struct GuildInfo {
    pub guild_number: i32,
    pub name: String,
    pub founder: String,
    pub date: String,
    pub alignment: i32,
    pub antifaccion: i32,
    pub leader: String,
    pub sub_lider1: String,
    pub sub_lider2: String,
    pub url: String,
    pub desc: String,
    pub news: String,
    pub codex: Vec<String>,
    pub nivel_clan: i32,
    pub puntos_clan: i32,
    pub cvc_wins: i32,
    pub cvc_losses: i32,
    pub castle_sieges: i32,
    pub reputation: i32,
    pub elecciones_abiertas: bool,
}

#[derive(Debug, Clone)]
pub struct GuildApplicant {
    pub name: String,
    pub detail: String,
}

#[derive(Debug, Clone, Default)]
pub struct GuildBankSlot {
    pub obj_index: i32,
    pub amount: i32,
}

/// Internal DB ID for a guild.
async fn get_guild_db_id(pool: &PgPool, guild_number: i32) -> Option<i32> {
    sqlx::query_scalar::<_, i32>(
        "SELECT id FROM guilds WHERE guild_number = $1 AND NOT dissolved"
    )
    .bind(guild_number)
    .fetch_optional(pool)
    .await
    .ok()
    .flatten()
}

/// Get total number of guilds.
pub async fn get_num_guilds(pool: &PgPool) -> i32 {
    sqlx::query_scalar::<_, i64>("SELECT COUNT(*) FROM guilds")
        .fetch_one(pool)
        .await
        .unwrap_or(0) as i32
}

/// Load guild info by guild number.
pub async fn load_guild(pool: &PgPool, guild_num: i32) -> Option<GuildInfo> {
    use sqlx::Row;

    let row = sqlx::query(
        "SELECT guild_number, name, founder, date, alignment, antifaccion,
                leader, sub_lider1, sub_lider2, url, description, news,
                codex, nivel_clan, puntos_clan, cvc_wins, cvc_losses,
                castle_sieges, reputation, elecciones_abiertas
         FROM guilds WHERE guild_number = $1 AND NOT dissolved"
    )
    .bind(guild_num)
    .fetch_optional(pool)
    .await
    .ok()
    .flatten()?;

    Some(GuildInfo {
        guild_number: row.get("guild_number"),
        name: row.get("name"),
        founder: row.get("founder"),
        date: row.get("date"),
        alignment: row.get("alignment"),
        antifaccion: row.get("antifaccion"),
        leader: row.get("leader"),
        sub_lider1: row.get("sub_lider1"),
        sub_lider2: row.get("sub_lider2"),
        url: row.get("url"),
        desc: row.get("description"),
        news: row.get("news"),
        codex: row.get("codex"),
        nivel_clan: row.get("nivel_clan"),
        puntos_clan: row.get("puntos_clan"),
        cvc_wins: row.get("cvc_wins"),
        cvc_losses: row.get("cvc_losses"),
        castle_sieges: row.get("castle_sieges"),
        reputation: row.get("reputation"),
        elecciones_abiertas: row.get("elecciones_abiertas"),
    })
}

/// Find guild number by name.
pub async fn find_guild_by_name(pool: &PgPool, name: &str) -> Option<i32> {
    sqlx::query_scalar::<_, i32>(
        "SELECT guild_number FROM guilds WHERE UPPER(name) = UPPER($1) AND NOT dissolved"
    )
    .bind(name)
    .fetch_optional(pool)
    .await
    .ok()
    .flatten()
}

/// Load guild members.
pub async fn load_members(pool: &PgPool, guild_name: &str) -> Vec<String> {
    let db_id = match sqlx::query_scalar::<_, i32>(
        "SELECT id FROM guilds WHERE UPPER(name) = UPPER($1) AND NOT dissolved"
    ).bind(guild_name).fetch_optional(pool).await {
        Ok(Some(id)) => id,
        _ => return Vec::new(),
    };

    sqlx::query_scalar::<_, String>(
        "SELECT char_name FROM guild_members WHERE guild_id = $1 ORDER BY char_name"
    )
    .bind(db_id)
    .fetch_all(pool)
    .await
    .unwrap_or_default()
}

/// Load guild applicants.
pub async fn load_applicants(pool: &PgPool, guild_name: &str) -> Vec<GuildApplicant> {
    let db_id = match sqlx::query_scalar::<_, i32>(
        "SELECT id FROM guilds WHERE UPPER(name) = UPPER($1) AND NOT dissolved"
    ).bind(guild_name).fetch_optional(pool).await {
        Ok(Some(id)) => id,
        _ => return Vec::new(),
    };

    let rows: Vec<(String, String)> = sqlx::query_as(
        "SELECT char_name, detail FROM guild_applicants WHERE guild_id = $1"
    )
    .bind(db_id)
    .fetch_all(pool)
    .await
    .unwrap_or_default();

    rows.into_iter().map(|(name, detail)| GuildApplicant { name, detail }).collect()
}

/// Save guild info.
pub async fn save_guild(pool: &PgPool, guild: &GuildInfo) {
    if let Err(e) = sqlx::query(
        "UPDATE guilds SET
            name = $2, founder = $3, date = $4, alignment = $5, antifaccion = $6,
            leader = $7, sub_lider1 = $8, sub_lider2 = $9, url = $10,
            description = $11, news = $12, codex = $13,
            nivel_clan = $14, puntos_clan = $15, cvc_wins = $16, cvc_losses = $17,
            castle_sieges = $18, reputation = $19, elecciones_abiertas = $20
         WHERE guild_number = $1"
    )
    .bind(guild.guild_number)
    .bind(&guild.name).bind(&guild.founder).bind(&guild.date)
    .bind(guild.alignment).bind(guild.antifaccion)
    .bind(&guild.leader).bind(&guild.sub_lider1).bind(&guild.sub_lider2)
    .bind(&guild.url).bind(&guild.desc).bind(&guild.news)
    .bind(&guild.codex)
    .bind(guild.nivel_clan).bind(guild.puntos_clan)
    .bind(guild.cvc_wins).bind(guild.cvc_losses)
    .bind(guild.castle_sieges).bind(guild.reputation)
    .bind(guild.elecciones_abiertas)
    .execute(pool)
    .await
    {
        tracing::error!("Guild DB error: {e}");
    }
}

/// Save members list (replace all) — runs inside the provided transaction.
async fn save_members_tx(
    tx: &mut Transaction<'_, Postgres>,
    db_id: i32,
    members: &[String],
) -> Result<(), sqlx::Error> {
    sqlx::query("DELETE FROM guild_members WHERE guild_id = $1")
        .bind(db_id)
        .execute(&mut **tx)
        .await?;

    for name in members {
        sqlx::query(
            "INSERT INTO guild_members (guild_id, char_name) VALUES ($1, $2)
             ON CONFLICT DO NOTHING",
        )
        .bind(db_id)
        .bind(name)
        .execute(&mut **tx)
        .await?;
    }
    Ok(())
}

/// Save members list (replace all).
pub async fn save_members(pool: &PgPool, guild_name: &str, members: &[String]) {
    let db_id = match sqlx::query_scalar::<_, i32>(
        "SELECT id FROM guilds WHERE UPPER(name) = UPPER($1) AND NOT dissolved"
    ).bind(guild_name).fetch_optional(pool).await {
        Ok(Some(id)) => id,
        _ => return,
    };

    let mut tx = match pool.begin().await {
        Ok(tx) => tx,
        Err(e) => { tracing::error!("Guild DB error: {e}"); return; }
    };

    if let Err(e) = save_members_tx(&mut tx, db_id, members).await {
        tracing::error!("Guild DB error: {e}");
        return;
    }

    if let Err(e) = tx.commit().await {
        tracing::error!("Guild DB error: {e}");
    }
}

/// Add member.
pub async fn add_member(pool: &PgPool, guild_name: &str, char_name: &str) {
    let db_id = match sqlx::query_scalar::<_, i32>(
        "SELECT id FROM guilds WHERE UPPER(name) = UPPER($1) AND NOT dissolved"
    ).bind(guild_name).fetch_optional(pool).await {
        Ok(Some(id)) => id,
        _ => return,
    };

    if let Err(e) = sqlx::query(
        "INSERT INTO guild_members (guild_id, char_name) VALUES ($1, $2)
         ON CONFLICT DO NOTHING"
    )
    .bind(db_id).bind(char_name)
    .execute(pool).await
    {
        tracing::error!("Guild DB error: {e}");
    }
}

/// Remove member.
pub async fn remove_member(pool: &PgPool, guild_name: &str, char_name: &str) {
    let db_id = match sqlx::query_scalar::<_, i32>(
        "SELECT id FROM guilds WHERE UPPER(name) = UPPER($1) AND NOT dissolved"
    ).bind(guild_name).fetch_optional(pool).await {
        Ok(Some(id)) => id,
        _ => return,
    };

    if let Err(e) = sqlx::query(
        "DELETE FROM guild_members WHERE guild_id = $1 AND UPPER(char_name) = UPPER($2)"
    )
    .bind(db_id).bind(char_name)
    .execute(pool).await
    {
        tracing::error!("Guild DB error: {e}");
    }
}

/// Save applicants (replace all) — runs inside the provided transaction.
async fn save_applicants_tx(
    tx: &mut Transaction<'_, Postgres>,
    db_id: i32,
    applicants: &[GuildApplicant],
) -> Result<(), sqlx::Error> {
    sqlx::query("DELETE FROM guild_applicants WHERE guild_id = $1")
        .bind(db_id)
        .execute(&mut **tx)
        .await?;

    for app in applicants {
        sqlx::query(
            "INSERT INTO guild_applicants (guild_id, char_name, detail) VALUES ($1, $2, $3)",
        )
        .bind(db_id)
        .bind(&app.name)
        .bind(&app.detail)
        .execute(&mut **tx)
        .await?;
    }
    Ok(())
}

/// Save applicants (replace all).
pub async fn save_applicants(pool: &PgPool, guild_name: &str, applicants: &[GuildApplicant]) {
    let db_id = match sqlx::query_scalar::<_, i32>(
        "SELECT id FROM guilds WHERE UPPER(name) = UPPER($1) AND NOT dissolved"
    ).bind(guild_name).fetch_optional(pool).await {
        Ok(Some(id)) => id,
        _ => return,
    };

    let mut tx = match pool.begin().await {
        Ok(tx) => tx,
        Err(e) => { tracing::error!("Guild DB error: {e}"); return; }
    };

    if let Err(e) = save_applicants_tx(&mut tx, db_id, applicants).await {
        tracing::error!("Guild DB error: {e}");
        return;
    }

    if let Err(e) = tx.commit().await {
        tracing::error!("Guild DB error: {e}");
    }
}

/// Add applicant.
pub async fn add_applicant(pool: &PgPool, guild_name: &str, char_name: &str, detail: &str) -> bool {
    let db_id = match sqlx::query_scalar::<_, i32>(
        "SELECT id FROM guilds WHERE UPPER(name) = UPPER($1) AND NOT dissolved"
    ).bind(guild_name).fetch_optional(pool).await {
        Ok(Some(id)) => id,
        _ => return false,
    };

    let count: i64 = sqlx::query_scalar(
        "SELECT COUNT(*) FROM guild_applicants WHERE guild_id = $1"
    )
    .bind(db_id)
    .fetch_one(pool)
    .await
    .unwrap_or(0);

    if count >= MAX_ASPIRANTES as i64 {
        return false;
    }

    sqlx::query(
        "INSERT INTO guild_applicants (guild_id, char_name, detail) VALUES ($1, $2, $3)
         ON CONFLICT DO NOTHING"
    )
    .bind(db_id).bind(char_name).bind(detail)
    .execute(pool)
    .await
    .is_ok()
}

/// Remove applicant.
pub async fn remove_applicant(pool: &PgPool, guild_name: &str, char_name: &str) {
    let db_id = match sqlx::query_scalar::<_, i32>(
        "SELECT id FROM guilds WHERE UPPER(name) = UPPER($1) AND NOT dissolved"
    ).bind(guild_name).fetch_optional(pool).await {
        Ok(Some(id)) => id,
        _ => return,
    };

    if let Err(e) = sqlx::query(
        "DELETE FROM guild_applicants WHERE guild_id = $1 AND UPPER(char_name) = UPPER($2)"
    )
    .bind(db_id).bind(char_name)
    .execute(pool).await
    {
        tracing::error!("Guild DB error: {e}");
    }
}

/// Load guild bank gold.
pub async fn load_bank_gold(pool: &PgPool, guild_name: &str) -> i64 {
    sqlx::query_scalar::<_, i64>(
        "SELECT bank_gold FROM guilds WHERE UPPER(name) = UPPER($1) AND NOT dissolved"
    )
    .bind(guild_name)
    .fetch_optional(pool)
    .await
    .ok()
    .flatten()
    .unwrap_or(0)
}

/// Save guild bank gold.
pub async fn save_bank_gold(pool: &PgPool, guild_name: &str, gold: i64) {
    if let Err(e) = sqlx::query(
        "UPDATE guilds SET bank_gold = $1 WHERE UPPER(name) = UPPER($2) AND NOT dissolved"
    )
    .bind(gold).bind(guild_name)
    .execute(pool).await
    {
        tracing::error!("Guild DB error: {e}");
    }
}

/// Load guild bank items.
pub async fn load_bank_items(pool: &PgPool, guild_name: &str) -> Vec<GuildBankSlot> {
    let db_id = match sqlx::query_scalar::<_, i32>(
        "SELECT id FROM guilds WHERE UPPER(name) = UPPER($1) AND NOT dissolved"
    ).bind(guild_name).fetch_optional(pool).await {
        Ok(Some(id)) => id,
        _ => return (0..MAX_GUILD_BANK_SLOTS).map(|_| GuildBankSlot::default()).collect(),
    };

    let rows: Vec<(i16, i32, i32)> = sqlx::query_as(
        "SELECT slot, obj_index, amount FROM guild_bank_items
         WHERE guild_id = $1 ORDER BY slot"
    )
    .bind(db_id)
    .fetch_all(pool)
    .await
    .unwrap_or_default();

    let mut items: Vec<GuildBankSlot> = (0..MAX_GUILD_BANK_SLOTS)
        .map(|_| GuildBankSlot::default())
        .collect();

    for (slot, obj_index, amount) in rows {
        let s = slot as usize;
        if s < MAX_GUILD_BANK_SLOTS {
            items[s] = GuildBankSlot { obj_index, amount };
        }
    }
    items
}

/// Save guild bank items — runs inside the provided transaction.
async fn save_bank_items_tx(
    tx: &mut Transaction<'_, Postgres>,
    db_id: i32,
    items: &[GuildBankSlot],
) -> Result<(), sqlx::Error> {
    sqlx::query("DELETE FROM guild_bank_items WHERE guild_id = $1")
        .bind(db_id)
        .execute(&mut **tx)
        .await?;

    for (i, slot) in items.iter().enumerate() {
        if slot.obj_index > 0 {
            sqlx::query(
                "INSERT INTO guild_bank_items (guild_id, slot, obj_index, amount)
                 VALUES ($1, $2, $3, $4)",
            )
            .bind(db_id)
            .bind(i as i16)
            .bind(slot.obj_index)
            .bind(slot.amount)
            .execute(&mut **tx)
            .await?;
        }
    }
    Ok(())
}

/// Save guild bank items.
pub async fn save_bank_items(pool: &PgPool, guild_name: &str, items: &[GuildBankSlot]) {
    let db_id = match sqlx::query_scalar::<_, i32>(
        "SELECT id FROM guilds WHERE UPPER(name) = UPPER($1) AND NOT dissolved"
    ).bind(guild_name).fetch_optional(pool).await {
        Ok(Some(id)) => id,
        _ => return,
    };

    let mut tx = match pool.begin().await {
        Ok(tx) => tx,
        Err(e) => { tracing::error!("Guild DB error: {e}"); return; }
    };

    if let Err(e) = save_bank_items_tx(&mut tx, db_id, items).await {
        tracing::error!("Guild DB error: {e}");
        return;
    }

    if let Err(e) = tx.commit().await {
        tracing::error!("Guild DB error: {e}");
    }
}

/// Create a new guild — inner logic runs inside a transaction.
async fn create_guild_tx(
    tx: &mut Transaction<'_, Postgres>,
    name: &str,
    founder: &str,
    alignment: i32,
    desc: &str,
    url: &str,
    codex: &[String],
) -> Result<i32, sqlx::Error> {
    let max_num: i64 = sqlx::query_scalar(
        "SELECT COALESCE(MAX(guild_number), 0) FROM guilds"
    )
    .fetch_one(&mut **tx)
    .await?;

    let guild_num = (max_num + 1) as i32;

    let db_id: i32 = sqlx::query_scalar(
        "INSERT INTO guilds (
            guild_number, name, founder, date, alignment,
            leader, codex, description, url
        ) VALUES ($1, $2, $3, '01/01/2026', $4, $5, $6, $7, $8)
        RETURNING id",
    )
    .bind(guild_num)
    .bind(name)
    .bind(founder)
    .bind(alignment)
    .bind(founder)
    .bind(codex)
    .bind(desc)
    .bind(url)
    .fetch_one(&mut **tx)
    .await?;

    // Add founder as first member — within the same transaction.
    sqlx::query(
        "INSERT INTO guild_members (guild_id, char_name) VALUES ($1, $2)",
    )
    .bind(db_id)
    .bind(founder)
    .execute(&mut **tx)
    .await?;

    Ok(guild_num)
}

/// Create a new guild. Returns the guild number, or 0 on error.
pub async fn create_guild(
    pool: &PgPool,
    name: &str,
    founder: &str,
    alignment: i32,
    desc: &str,
    url: &str,
    codex: Vec<String>,
) -> i32 {
    let mut tx = match pool.begin().await {
        Ok(tx) => tx,
        Err(e) => { tracing::error!("Guild DB error: {e}"); return 0; }
    };

    match create_guild_tx(&mut tx, name, founder, alignment, desc, url, &codex).await {
        Ok(guild_num) => {
            if let Err(e) = tx.commit().await {
                tracing::error!("Guild DB error: {e}");
                return 0;
            }
            guild_num
        }
        Err(e) => {
            tracing::error!("Guild DB error: {e}");
            0
        }
    }
}

/// Dissolve a guild.
pub async fn dissolve_guild(pool: &PgPool, guild_num: i32) {
    if let Err(e) = sqlx::query(
        "UPDATE guilds SET dissolved = TRUE, leader = '', founder = '' WHERE guild_number = $1"
    )
    .bind(guild_num)
    .execute(pool).await
    {
        tracing::error!("Guild DB error: {e}");
    }
}

/// List all active guilds (number, name, alignment, level).
pub async fn list_guilds(pool: &PgPool) -> Vec<(i32, String, i32, i32)> {
    sqlx::query_as::<_, (i32, String, i32, i32)>(
        "SELECT guild_number, name, alignment, nivel_clan
         FROM guilds WHERE NOT dissolved ORDER BY guild_number"
    )
    .fetch_all(pool)
    .await
    .unwrap_or_default()
}
