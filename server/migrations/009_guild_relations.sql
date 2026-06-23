-- Guild diplomacy relations — persists war/peace/alliance across restarts.

CREATE TABLE IF NOT EXISTS guild_relations (
    guild_a INTEGER NOT NULL,
    guild_b INTEGER NOT NULL,
    relation INTEGER NOT NULL DEFAULT 0,
    PRIMARY KEY (guild_a, guild_b)
);
