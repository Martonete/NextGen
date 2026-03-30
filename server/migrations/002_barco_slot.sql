-- Add barco_slot column for boat persistence across login/logout
ALTER TABLE characters ADD COLUMN IF NOT EXISTS barco_slot INT NOT NULL DEFAULT 0;
