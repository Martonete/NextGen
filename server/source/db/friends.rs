// Friend list persistence — PostgreSQL (account_friends table).

use sqlx::PgPool;

/// Load friend list for an account.
pub async fn load_friends(pool: &PgPool, account_id: i32) -> Vec<String> {
    let rows: Vec<(String,)> = sqlx::query_as(
        "SELECT friend_name FROM account_friends
         WHERE account_id = $1 ORDER BY slot"
    )
    .bind(account_id)
    .fetch_all(pool)
    .await
    .unwrap_or_default();

    rows.into_iter().map(|(n,)| n).collect()
}

/// Count friends for an account.
pub async fn count_friends(pool: &PgPool, account_id: i32) -> usize {
    let count: i64 = sqlx::query_scalar(
        "SELECT COUNT(*) FROM account_friends WHERE account_id = $1"
    )
    .bind(account_id)
    .fetch_one(pool)
    .await
    .unwrap_or(0);
    count as usize
}

/// Add a friend. Returns false if list is full (max 20) or duplicate.
pub async fn add_friend(pool: &PgPool, account_id: i32, friend_name: &str) -> Result<(), String> {
    let count = count_friends(pool, account_id).await;
    if count >= 20 {
        return Err("Lista de amigos llena, solo puedes agregar 20.".into());
    }

    // Check duplicate
    let exists: i64 = sqlx::query_scalar(
        "SELECT COUNT(*) FROM account_friends
         WHERE account_id = $1 AND UPPER(friend_name) = UPPER($2)"
    )
    .bind(account_id)
    .bind(friend_name)
    .fetch_one(pool)
    .await
    .unwrap_or(0);

    if exists > 0 {
        return Err("El usuario ya esta en tu lista de amigos.".into());
    }

    let new_slot = count as i16 + 1;
    sqlx::query(
        "INSERT INTO account_friends (account_id, slot, friend_name)
         VALUES ($1, $2, $3)"
    )
    .bind(account_id)
    .bind(new_slot)
    .bind(friend_name)
    .execute(pool)
    .await
    .map_err(|e| format!("DB error adding friend: {}", e))?;

    Ok(())
}

/// Remove a friend by slot (1-based). Compacts remaining slots.
pub async fn remove_friend(pool: &PgPool, account_id: i32, slot: usize) -> Result<(), String> {
    let count = count_friends(pool, account_id).await;
    if slot == 0 || slot > count {
        return Err("Invalid slot".into());
    }

    // Delete the slot
    sqlx::query(
        "DELETE FROM account_friends WHERE account_id = $1 AND slot = $2"
    )
    .bind(account_id)
    .bind(slot as i16)
    .execute(pool)
    .await
    .map_err(|e| format!("DB error: {}", e))?;

    // Compact: shift higher slots down
    for i in (slot as i16 + 1)..=(count as i16) {
        sqlx::query(
            "UPDATE account_friends SET slot = $3 WHERE account_id = $1 AND slot = $2"
        )
        .bind(account_id)
        .bind(i)
        .bind(i - 1)
        .execute(pool)
        .await
        .map_err(|e| format!("DB error compacting friends: {}", e))?;
    }

    Ok(())
}

/// Check if a character name is in an account's friend list.
pub async fn is_friend(pool: &PgPool, account_id: i32, name: &str) -> bool {
    let count: i64 = sqlx::query_scalar(
        "SELECT COUNT(*) FROM account_friends
         WHERE account_id = $1 AND UPPER(friend_name) = UPPER($2)"
    )
    .bind(account_id)
    .bind(name)
    .fetch_one(pool)
    .await
    .unwrap_or(0);
    count > 0
}

/// Check if a character name is in any account's friend list by account name.
pub async fn is_friend_of_account_name(pool: &PgPool, account_name: &str, char_name: &str) -> bool {
    let count: i64 = sqlx::query_scalar(
        "SELECT COUNT(*) FROM account_friends af
         JOIN accounts a ON a.id = af.account_id
         WHERE UPPER(a.name) = UPPER($1) AND UPPER(af.friend_name) = UPPER($2)"
    )
    .bind(account_name)
    .bind(char_name)
    .fetch_one(pool)
    .await
    .unwrap_or(0);
    count > 0
}
