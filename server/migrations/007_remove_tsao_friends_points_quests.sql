-- Remove TSAO-only tables and columns not present in VB6 13.3

-- Friends system (TSAO only)
DROP TABLE IF EXISTS account_friends;

-- Quest tracking columns (VB6 13.3 uses NPCsQuest.dat, not DB columns)
ALTER TABLE characters DROP COLUMN IF EXISTS questeando;
ALTER TABLE characters DROP COLUMN IF EXISTS quest_num;
ALTER TABLE characters DROP COLUMN IF EXISTS quest_kills;
ALTER TABLE characters DROP COLUMN IF EXISTS quests_completed;

-- TSAO point systems
ALTER TABLE characters DROP COLUMN IF EXISTS puntos_donacion;
ALTER TABLE characters DROP COLUMN IF EXISTS puntos_torneo;
ALTER TABLE characters DROP COLUMN IF EXISTS ts_points;
