-- Migration 005: Add description column if missing (was in 001 but may not exist on older DBs)
ALTER TABLE characters ADD COLUMN IF NOT EXISTS description TEXT NOT NULL DEFAULT '';
