-- Argentum Nextgen — Initial schema
-- Replaces INI file storage for accounts, characters, guilds, bans, rankings.

-- Accounts
CREATE TABLE IF NOT EXISTS accounts (
    id            SERIAL PRIMARY KEY,
    name          VARCHAR(30) NOT NULL UNIQUE,
    password_hash VARCHAR(255) NOT NULL,
    pin           VARCHAR(10) NOT NULL DEFAULT '',
    banned        BOOLEAN NOT NULL DEFAULT FALSE,
    ban_reason    VARCHAR(255) NOT NULL DEFAULT '',
    bank_gold     BIGINT NOT NULL DEFAULT 0,
    security_code VARCHAR(30) NOT NULL DEFAULT '',
    created_at    TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at    TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

-- Account friend list (max 20 slots)
CREATE TABLE IF NOT EXISTS account_friends (
    account_id  INT NOT NULL REFERENCES accounts(id) ON DELETE CASCADE,
    slot        SMALLINT NOT NULL,
    friend_name VARCHAR(30) NOT NULL DEFAULT '',
    PRIMARY KEY (account_id, slot)
);

-- Account bank inventory (max 40 slots)
CREATE TABLE IF NOT EXISTS account_bank (
    account_id INT NOT NULL REFERENCES accounts(id) ON DELETE CASCADE,
    slot       SMALLINT NOT NULL,
    obj_index  INT NOT NULL DEFAULT 0,
    amount     INT NOT NULL DEFAULT 0,
    PRIMARY KEY (account_id, slot)
);

-- Characters
CREATE TABLE IF NOT EXISTS characters (
    id              SERIAL PRIMARY KEY,
    account_id      INT NOT NULL REFERENCES accounts(id) ON DELETE CASCADE,
    name            VARCHAR(30) NOT NULL UNIQUE,
    slot            SMALLINT NOT NULL DEFAULT 0,

    -- Appearance
    head            INT NOT NULL DEFAULT 0,
    body            INT NOT NULL DEFAULT 0,
    heading         INT NOT NULL DEFAULT 0,
    weapon          INT NOT NULL DEFAULT 0,
    shield          INT NOT NULL DEFAULT 0,
    helmet          INT NOT NULL DEFAULT 0,

    -- Identity
    class           VARCHAR(30) NOT NULL DEFAULT '',
    race            VARCHAR(30) NOT NULL DEFAULT '',
    gender          INT NOT NULL DEFAULT 1,
    hogar           INT NOT NULL DEFAULT 0,

    -- Position
    map             INT NOT NULL DEFAULT 1,
    x               INT NOT NULL DEFAULT 50,
    y               INT NOT NULL DEFAULT 50,

    -- Stats
    level           INT NOT NULL DEFAULT 1,
    exp             BIGINT NOT NULL DEFAULT 0,
    max_hp          INT NOT NULL DEFAULT 0,
    min_hp          INT NOT NULL DEFAULT 0,
    max_mana        INT NOT NULL DEFAULT 0,
    min_mana        INT NOT NULL DEFAULT 0,
    max_sta         INT NOT NULL DEFAULT 0,
    min_sta         INT NOT NULL DEFAULT 0,
    max_hit         INT NOT NULL DEFAULT 1,
    min_hit         INT NOT NULL DEFAULT 1,
    max_agua        INT NOT NULL DEFAULT 100,
    min_agua        INT NOT NULL DEFAULT 100,
    max_ham         INT NOT NULL DEFAULT 100,
    min_ham         INT NOT NULL DEFAULT 100,
    gold            BIGINT NOT NULL DEFAULT 0,
    bank_gold       BIGINT NOT NULL DEFAULT 0,
    skill_pts_libres INT NOT NULL DEFAULT 10,

    -- Attributes (Str, Agi, Int, Cha, Con)
    attributes      INT[5] NOT NULL DEFAULT '{0,0,0,0,0}',

    -- Skills (22 skills)
    skills          INT[22] NOT NULL DEFAULT '{0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0}',

    -- Spells (20 slots)
    spells          INT[20] NOT NULL DEFAULT '{0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0}',

    -- Flags
    banned          BOOLEAN NOT NULL DEFAULT FALSE,
    dead            BOOLEAN NOT NULL DEFAULT FALSE,
    poisoned        BOOLEAN NOT NULL DEFAULT FALSE,
    paralyzed       BOOLEAN NOT NULL DEFAULT FALSE,
    hidden          BOOLEAN NOT NULL DEFAULT FALSE,
    navigating      BOOLEAN NOT NULL DEFAULT FALSE,
    criminal        BOOLEAN NOT NULL DEFAULT FALSE,
    privileges      INT NOT NULL DEFAULT 0,
    logged          BOOLEAN NOT NULL DEFAULT FALSE,

    -- Equipment slots (which inventory slot holds each piece)
    weapon_eqp_slot INT NOT NULL DEFAULT 0,
    armour_eqp_slot INT NOT NULL DEFAULT 0,
    shield_eqp_slot INT NOT NULL DEFAULT 0,
    helmet_eqp_slot INT NOT NULL DEFAULT 0,
    municion_eqp_slot INT NOT NULL DEFAULT 0,

    -- Guild
    guild_index     INT NOT NULL DEFAULT 0,

    -- Reputation
    reputation      INT NOT NULL DEFAULT 0,

    -- Factions
    armada_real     BOOLEAN NOT NULL DEFAULT FALSE,
    fuerzas_caos    BOOLEAN NOT NULL DEFAULT FALSE,
    criminales_matados INT NOT NULL DEFAULT 0,
    ciudadanos_matados INT NOT NULL DEFAULT 0,
    recompensas_real INT NOT NULL DEFAULT 0,
    recompensas_caos INT NOT NULL DEFAULT 0,
    reenlistadas    BOOLEAN NOT NULL DEFAULT FALSE,

    -- Quests
    questeando      BOOLEAN NOT NULL DEFAULT FALSE,
    quest_num       INT NOT NULL DEFAULT 0,
    quest_kills     INT NOT NULL DEFAULT 0,
    quests_completed INT NOT NULL DEFAULT 0,

    -- Points
    puntos_donacion BIGINT NOT NULL DEFAULT 0,
    puntos_torneo   BIGINT NOT NULL DEFAULT 0,
    ts_points       BIGINT NOT NULL DEFAULT 0,

    created_at      TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at      TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

-- Character inventory (max 25 slots)
CREATE TABLE IF NOT EXISTS character_inventory (
    character_id INT NOT NULL REFERENCES characters(id) ON DELETE CASCADE,
    slot         SMALLINT NOT NULL,
    obj_index    INT NOT NULL DEFAULT 0,
    amount       INT NOT NULL DEFAULT 0,
    equipped     BOOLEAN NOT NULL DEFAULT FALSE,
    PRIMARY KEY (character_id, slot)
);

-- Character bank (max 40 slots)
CREATE TABLE IF NOT EXISTS character_bank (
    character_id INT NOT NULL REFERENCES characters(id) ON DELETE CASCADE,
    slot         SMALLINT NOT NULL,
    obj_index    INT NOT NULL DEFAULT 0,
    amount       INT NOT NULL DEFAULT 0,
    PRIMARY KEY (character_id, slot)
);

-- Guilds
CREATE TABLE IF NOT EXISTS guilds (
    id                  SERIAL PRIMARY KEY,
    guild_number        INT NOT NULL UNIQUE,
    name                VARCHAR(60) NOT NULL,
    founder             VARCHAR(30) NOT NULL DEFAULT '',
    date                VARCHAR(30) NOT NULL DEFAULT '',
    alignment           INT NOT NULL DEFAULT 3,
    antifaccion         INT NOT NULL DEFAULT 0,
    leader              VARCHAR(30) NOT NULL DEFAULT '',
    sub_lider1          VARCHAR(30) NOT NULL DEFAULT 'Fermin',
    sub_lider2          VARCHAR(30) NOT NULL DEFAULT 'Fermin',
    url                 VARCHAR(255) NOT NULL DEFAULT '',
    description         TEXT NOT NULL DEFAULT '',
    news                TEXT NOT NULL DEFAULT '',
    codex               TEXT[8] NOT NULL DEFAULT '{"","","","","","","",""}',
    nivel_clan          INT NOT NULL DEFAULT 1,
    puntos_clan         INT NOT NULL DEFAULT 0,
    cvc_wins            INT NOT NULL DEFAULT 0,
    cvc_losses          INT NOT NULL DEFAULT 0,
    castle_sieges        INT NOT NULL DEFAULT 0,
    reputation          INT NOT NULL DEFAULT 0,
    elecciones_abiertas BOOLEAN NOT NULL DEFAULT FALSE,
    dissolved           BOOLEAN NOT NULL DEFAULT FALSE,
    bank_gold           BIGINT NOT NULL DEFAULT 0,
    created_at          TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

-- Guild members
CREATE TABLE IF NOT EXISTS guild_members (
    guild_id  INT NOT NULL REFERENCES guilds(id) ON DELETE CASCADE,
    char_name VARCHAR(30) NOT NULL,
    PRIMARY KEY (guild_id, char_name)
);

-- Guild applicants
CREATE TABLE IF NOT EXISTS guild_applicants (
    guild_id  INT NOT NULL REFERENCES guilds(id) ON DELETE CASCADE,
    char_name VARCHAR(30) NOT NULL,
    detail    TEXT NOT NULL DEFAULT '',
    PRIMARY KEY (guild_id, char_name)
);

-- Guild bank items (max 40 slots)
CREATE TABLE IF NOT EXISTS guild_bank_items (
    guild_id  INT NOT NULL REFERENCES guilds(id) ON DELETE CASCADE,
    slot      SMALLINT NOT NULL,
    obj_index INT NOT NULL DEFAULT 0,
    amount    INT NOT NULL DEFAULT 0,
    PRIMARY KEY (guild_id, slot)
);

-- Bans
CREATE TABLE IF NOT EXISTS banned_ips (
    ip VARCHAR(45) PRIMARY KEY
);

CREATE TABLE IF NOT EXISTS banned_hds (
    hd VARCHAR(100) PRIMARY KEY
);

-- Rankings (9 categories × 10 positions)
CREATE TABLE IF NOT EXISTS rankings (
    category VARCHAR(20) NOT NULL,
    position SMALLINT NOT NULL,
    name     VARCHAR(60) NOT NULL DEFAULT '',
    value    BIGINT NOT NULL DEFAULT 0,
    PRIMARY KEY (category, position)
);

-- Indexes for common lookups
CREATE INDEX IF NOT EXISTS idx_characters_account_id ON characters(account_id);
CREATE INDEX IF NOT EXISTS idx_characters_name_upper ON characters(UPPER(name));
CREATE INDEX IF NOT EXISTS idx_accounts_name_upper ON accounts(UPPER(name));
CREATE INDEX IF NOT EXISTS idx_guilds_guild_number ON guilds(guild_number);
