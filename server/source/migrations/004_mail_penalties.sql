-- Mail system (replaces CORREO section in .chr files)
CREATE TABLE IF NOT EXISTS character_mail (
    id           SERIAL PRIMARY KEY,
    character_id INT NOT NULL REFERENCES characters(id) ON DELETE CASCADE,
    sender       VARCHAR(30) NOT NULL DEFAULT '',
    subject      VARCHAR(100) NOT NULL DEFAULT '',
    message      TEXT NOT NULL DEFAULT '',
    sent_at      VARCHAR(30) NOT NULL DEFAULT '',
    is_new       BOOLEAN NOT NULL DEFAULT TRUE,
    created_at   TIMESTAMPTZ NOT NULL DEFAULT NOW()
);
CREATE INDEX IF NOT EXISTS idx_character_mail_char ON character_mail(character_id);

-- Penalties system (replaces PENAS section in .chr files)
CREATE TABLE IF NOT EXISTS character_penalties (
    id           SERIAL PRIMARY KEY,
    character_id INT NOT NULL REFERENCES characters(id) ON DELETE CASCADE,
    penalty_text TEXT NOT NULL DEFAULT '',
    created_at   TIMESTAMPTZ NOT NULL DEFAULT NOW()
);
CREATE INDEX IF NOT EXISTS idx_character_penalties_char ON character_penalties(character_id);
