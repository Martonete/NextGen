-- Add all VB6 13.3 charfile fields missing from the DB schema.

-- Skill progression: current XP per skill (VB6: ExpSkills 1-22)
ALTER TABLE characters ADD COLUMN IF NOT EXISTS exp_skills INT[22] NOT NULL DEFAULT '{0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0}';

-- Kill counters (VB6: MUERTES section)
ALTER TABLE characters ADD COLUMN IF NOT EXISTS usuarios_matados INT NOT NULL DEFAULT 0;
ALTER TABLE characters ADD COLUMN IF NOT EXISTS npcs_muertos INT NOT NULL DEFAULT 0;

-- Individual reputations (VB6: REP section — 6 fields, average = Promedio)
ALTER TABLE characters ADD COLUMN IF NOT EXISTS rep_asesino INT NOT NULL DEFAULT 0;
ALTER TABLE characters ADD COLUMN IF NOT EXISTS rep_bandido INT NOT NULL DEFAULT 0;
ALTER TABLE characters ADD COLUMN IF NOT EXISTS rep_burgues INT NOT NULL DEFAULT 0;
ALTER TABLE characters ADD COLUMN IF NOT EXISTS rep_ladrones INT NOT NULL DEFAULT 0;
ALTER TABLE characters ADD COLUMN IF NOT EXISTS rep_noble INT NOT NULL DEFAULT 0;
ALTER TABLE characters ADD COLUMN IF NOT EXISTS rep_plebe INT NOT NULL DEFAULT 0;

-- Faction extended fields (VB6: FACCIONES section)
ALTER TABLE characters ADD COLUMN IF NOT EXISTS recibio_armadura_real BOOLEAN NOT NULL DEFAULT FALSE;
ALTER TABLE characters ADD COLUMN IF NOT EXISTS recibio_armadura_caos BOOLEAN NOT NULL DEFAULT FALSE;
ALTER TABLE characters ADD COLUMN IF NOT EXISTS recibio_exp_real BOOLEAN NOT NULL DEFAULT FALSE;
ALTER TABLE characters ADD COLUMN IF NOT EXISTS recibio_exp_caos BOOLEAN NOT NULL DEFAULT FALSE;
ALTER TABLE characters ADD COLUMN IF NOT EXISTS nivel_ingreso INT NOT NULL DEFAULT 0;
ALTER TABLE characters ADD COLUMN IF NOT EXISTS fecha_ingreso VARCHAR(30) NOT NULL DEFAULT '';
ALTER TABLE characters ADD COLUMN IF NOT EXISTS matados_ingreso INT NOT NULL DEFAULT 0;
ALTER TABLE characters ADD COLUMN IF NOT EXISTS next_recompensa INT NOT NULL DEFAULT 0;

-- Contact
ALTER TABLE characters ADD COLUMN IF NOT EXISTS email VARCHAR(100) NOT NULL DEFAULT '';

-- Counters persisted across sessions (VB6: COUNTERS section)
ALTER TABLE characters ADD COLUMN IF NOT EXISTS counter_pena INT NOT NULL DEFAULT 0;
ALTER TABLE characters ADD COLUMN IF NOT EXISTS skills_asignados INT NOT NULL DEFAULT 0;

-- Last map visited (VB6: FLAGS.LastMap)
ALTER TABLE characters ADD COLUMN IF NOT EXISTS last_map INT NOT NULL DEFAULT 0;

-- UpTime (seconds played, VB6: INIT.UpTime)
ALTER TABLE characters ADD COLUMN IF NOT EXISTS uptime BIGINT NOT NULL DEFAULT 0;

-- Equipment slots missing from DB (VB6: MochilaSlot, AnilloSlot)
ALTER TABLE characters ADD COLUMN IF NOT EXISTS mochila_eqp_slot INT NOT NULL DEFAULT 0;
ALTER TABLE characters ADD COLUMN IF NOT EXISTS anillo_eqp_slot INT NOT NULL DEFAULT 0;

-- Marriage (VB6: Pareja)
ALTER TABLE characters ADD COLUMN IF NOT EXISTS pareja VARCHAR(30) NOT NULL DEFAULT '';

-- Counters: SkillsAsignados tracks how many total skill points were assigned (VB6: Counters.AsignedSkills)
-- counter_pena tracks jail/penalty time (VB6: Counters.Pena)
