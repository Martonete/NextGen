-- Migration 006: Pet persistence columns
ALTER TABLE characters ADD COLUMN IF NOT EXISTS pet_count INTEGER NOT NULL DEFAULT 0;
ALTER TABLE characters ADD COLUMN IF NOT EXISTS pet_types TEXT NOT NULL DEFAULT '';
