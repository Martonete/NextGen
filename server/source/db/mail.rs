// Mail system persistence — PostgreSQL (character_mail table).

use sqlx::PgPool;

const MAX_MAILS: usize = 20;

/// A mail entry.
#[derive(Debug, Clone)]
pub struct MailEntry {
    pub id: i32,
    pub sender: String,
    pub subject: String,
    pub message: String,
    pub sent_at: String,
    pub is_new: bool,
}

/// Count mails for a character.
pub async fn count_mails(pool: &PgPool, char_id: i32) -> usize {
    let count: i64 = sqlx::query_scalar(
        "SELECT COUNT(*) FROM character_mail WHERE character_id = $1"
    )
    .bind(char_id)
    .fetch_one(pool)
    .await
    .unwrap_or(0);
    count as usize
}

/// Count mails for a character by name.
pub async fn count_mails_by_name(pool: &PgPool, char_name: &str) -> usize {
    let count: i64 = sqlx::query_scalar(
        "SELECT COUNT(*) FROM character_mail cm
         JOIN characters c ON c.id = cm.character_id
         WHERE UPPER(c.name) = UPPER($1)"
    )
    .bind(char_name)
    .fetch_one(pool)
    .await
    .unwrap_or(0);
    count as usize
}

/// Send mail to a character (by name). Returns error if inbox full.
pub async fn send_mail(
    pool: &PgPool,
    recipient_name: &str,
    sender: &str,
    subject: &str,
    message: &str,
    date: &str,
) -> Result<(), String> {
    // Get character id
    let char_id: i32 = sqlx::query_scalar(
        "SELECT id FROM characters WHERE UPPER(name) = UPPER($1)"
    )
    .bind(recipient_name)
    .fetch_optional(pool)
    .await
    .map_err(|e| format!("DB error: {}", e))?
    .ok_or_else(|| "Character not found".to_string())?;

    let count = count_mails(pool, char_id).await;
    if count >= MAX_MAILS {
        return Err("Inbox full".into());
    }

    sqlx::query(
        "INSERT INTO character_mail (character_id, sender, subject, message, sent_at, is_new)
         VALUES ($1, $2, $3, $4, $5, TRUE)"
    )
    .bind(char_id)
    .bind(sender)
    .bind(subject)
    .bind(message)
    .bind(date)
    .execute(pool)
    .await
    .map_err(|e| format!("DB error sending mail: {}", e))?;

    Ok(())
}

/// Load mail list for a character (ordered by creation, oldest first).
pub async fn load_mails(pool: &PgPool, char_id: i32) -> Vec<MailEntry> {
    let rows: Vec<(i32, String, String, String, String, bool)> = sqlx::query_as(
        "SELECT id, sender, subject, message, sent_at, is_new
         FROM character_mail WHERE character_id = $1
         ORDER BY id ASC"
    )
    .bind(char_id)
    .fetch_all(pool)
    .await
    .unwrap_or_default();

    rows.into_iter().map(|(id, sender, subject, message, sent_at, is_new)| {
        MailEntry { id, sender, subject, message, sent_at, is_new }
    }).collect()
}

/// Mark a mail as read.
pub async fn mark_read(pool: &PgPool, mail_id: i32) {
    let _ = sqlx::query(
        "UPDATE character_mail SET is_new = FALSE WHERE id = $1"
    )
    .bind(mail_id)
    .execute(pool)
    .await;
}

/// Delete a mail by id.
pub async fn delete_mail(pool: &PgPool, mail_id: i32) {
    let _ = sqlx::query(
        "DELETE FROM character_mail WHERE id = $1"
    )
    .bind(mail_id)
    .execute(pool)
    .await;
}

/// Check if character has new (unread) mail.
pub async fn has_new_mail(pool: &PgPool, char_id: i32) -> bool {
    let count: i64 = sqlx::query_scalar(
        "SELECT COUNT(*) FROM character_mail WHERE character_id = $1 AND is_new = TRUE"
    )
    .bind(char_id)
    .fetch_one(pool)
    .await
    .unwrap_or(0);
    count > 0
}
