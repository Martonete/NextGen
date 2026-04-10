// Account persistence — PostgreSQL.

use sqlx::PgPool;

/// Account data loaded from the database.
#[derive(Debug, Clone)]
pub struct AccountData {
    pub id: i32,
    pub name: String,
    pub password_hash: String,
    pub pin: String,
    pub banned: bool,
    pub ban_reason: String,
    pub bank_gold: i64,
    pub security_code: String,
    pub num_pjs: usize,
    pub characters: Vec<String>,
}

/// Check if an account exists.
pub async fn account_exists(pool: &PgPool, account_name: &str) -> bool {
    let result =
        sqlx::query_scalar::<_, i64>("SELECT COUNT(*) FROM accounts WHERE UPPER(name) = UPPER($1)")
            .bind(account_name)
            .fetch_one(pool)
            .await;

    matches!(result, Ok(count) if count > 0)
}

/// Load an account by name.
pub async fn load_account(pool: &PgPool, account_name: &str) -> Result<AccountData, String> {
    let row = sqlx::query_as::<_, (i32, String, String, String, bool, String, i64, String)>(
        "SELECT id, name, password_hash, pin, banned, ban_reason, bank_gold, security_code
         FROM accounts WHERE UPPER(name) = UPPER($1)",
    )
    .bind(account_name)
    .fetch_optional(pool)
    .await
    .map_err(|e| format!("DB error loading account: {}", e))?
    .ok_or_else(|| "Account not found".to_string())?;

    let (id, name, password_hash, pin, banned, ban_reason, bank_gold, security_code) = row;

    // Load character names
    let chars: Vec<(String,)> =
        sqlx::query_as("SELECT name FROM characters WHERE account_id = $1 ORDER BY slot")
            .bind(id)
            .fetch_all(pool)
            .await
            .map_err(|e| format!("DB error loading characters: {}", e))?;

    let characters: Vec<String> = chars.into_iter().map(|(n,)| n).collect();
    let num_pjs = characters.len();

    Ok(AccountData {
        id,
        name,
        password_hash,
        pin,
        banned,
        ban_reason,
        bank_gold,
        security_code,
        num_pjs,
        characters,
    })
}

/// Create a new account. Password should already be hashed.
pub async fn create_account(
    pool: &PgPool,
    account_name: &str,
    password_hash: &str,
    pin: &str,
    security_code: &str,
) -> Result<i32, String> {
    // Check uniqueness
    if account_exists(pool, account_name).await {
        return Err("El nombre de la cuenta ya esta siendo utilizado por otro usuario.".into());
    }

    let id: (i32,) = sqlx::query_as(
        "INSERT INTO accounts (name, password_hash, pin, security_code)
         VALUES ($1, $2, $3, $4) RETURNING id",
    )
    .bind(account_name)
    .bind(password_hash)
    .bind(pin)
    .bind(security_code)
    .fetch_one(pool)
    .await
    .map_err(|e| format!("DB error creating account: {}", e))?;

    Ok(id.0)
}

/// Update account password.
pub async fn update_password(
    pool: &PgPool,
    account_name: &str,
    new_password_hash: &str,
) -> Result<(), String> {
    sqlx::query(
        "UPDATE accounts SET password_hash = $1, updated_at = NOW() WHERE UPPER(name) = UPPER($2)",
    )
    .bind(new_password_hash)
    .bind(account_name)
    .execute(pool)
    .await
    .map_err(|e| format!("DB error updating password: {}", e))?;

    Ok(())
}

/// Ban/unban an account.
pub async fn set_account_banned(
    pool: &PgPool,
    account_name: &str,
    banned: bool,
    reason: &str,
) -> Result<(), String> {
    sqlx::query(
        "UPDATE accounts SET banned = $1, ban_reason = $2, updated_at = NOW()
         WHERE UPPER(name) = UPPER($3)",
    )
    .bind(banned)
    .bind(reason)
    .bind(account_name)
    .execute(pool)
    .await
    .map_err(|e| format!("DB error banning account: {}", e))?;

    Ok(())
}
