// Database module — PostgreSQL persistence layer.
//
// Replaces file-based INI storage for accounts, characters, guilds, bans, rankings.
// Uses sqlx with PgPool for async connection pooling.

pub mod accounts;
pub mod charfile;
pub mod guilds;
pub mod bans;
pub mod password;

use sqlx::PgPool;
use sqlx::postgres::PgPoolOptions;

/// Initialize the database connection pool and run migrations.
pub async fn init_pool(database_url: &str) -> Result<PgPool, String> {
    let pool = PgPoolOptions::new()
        .max_connections(10)
        .connect(database_url)
        .await
        .map_err(|e| format!("Failed to connect to database: {}", e))?;

    tracing::info!("Connected to PostgreSQL");

    // Run embedded migrations
    sqlx::migrate!("./server/migrations")
        .run(&pool)
        .await
        .map_err(|e| format!("Failed to run migrations: {}", e))?;

    tracing::info!("Database migrations applied");

    Ok(pool)
}
