---
name: ao-skills-crafting
description: Use when modifying skill leveling, resource gathering (fish/wood/mine), crafting (smith/carp/smelt), taming, stealing, hiding, or the WLC handler flow.
---

# AO Skills & Crafting System Reference

Source: `server/source/game/handlers/skills/mod.rs`, `craft.rs`, `resource.rs`, `combat.rs`, `stealth.rs`

---

## 1. Skill IDs

| ID | Constant | Skill |
|----|----------|-------|
| 1 | SUERTE | Luck |
| 2 | MAGIA | Magic |
| 3 | ROBAR | Stealing |
| 4 | TACTICAS | Tactics |
| 5 | ARMAS | Armed Combat |
| 6 | MEDITAR | Meditation |
| 7 | APUNALAR | Backstabbing |
| 8 | OCULTARSE | Hiding |
| 9 | SUPERVIVENCIA | Survival |
| 10 | TALAR | Woodcutting |
| 11 | COMERCIAR | Trading |
| 12 | DEFENSA | Defense |
| 13 | PESCA | Fishing |
| 14 | MINERIA | Mining |
| 15 | CARPINTERIA | Carpentry |
| 16 | HERRERIA | Blacksmithing |
| 17 | LIDERAZGO | Leadership |
| 18 | DOMAR | Taming |
| 19 | PROYECTILES | Ranged Combat |
| 20 | WRESTERLING | Wrestling |
| 21 | NAVEGACION | Sailing |
| 22 | DEFENSA_MAGICA | Magic Defense |
| 88 | FUNDIR_METAL | Smelting (special) |

Note: Array indices are 0-based internally; IDs above are 1-based constants.

---

## 2. Skill Leveling

### Luck Denominator (polynomial)

Used for resource extraction (fishing, mining, woodcutting):
```
suerte = Int(-0.00125 * skill^2 - 0.3 * skill + 49)
suerte = max(1, suerte)
```

### Luck Denominator (lookup table)

Used for steal, meditate, and other skills:

| Skill Range | Denominator |
|-------------|-------------|
| 0-10 | 35 |
| 11-20 | 30 |
| 21-30 | 28 |
| 31-40 | 24 |
| 41-50 | 22 |
| 51-60 | 20 |
| 61-70 | 18 |
| 71-80 | 15 |
| 81-90 | 10 |
| 91-99 | 7 |
| 100+ | 5 |

### ELU Calculation (Skill XP Threshold)

```
ELU = Int(200 * 1.05^skill_level)
```

Where ELU_SKILL_INICIAL = 200.

### Skill XP Gain (`try_level_skill_with_hit`)

```
On success (hit=true):  +50 XP (EXP_ACIERTO_SKILL)
On failure (hit=false): +20 XP (EXP_FALLO_SKILL)
```

When `exp_skills[idx] >= elu_skills[idx]`:
- skill level +1
- character EXP +50
- recalculate ELU for new level

### Skill Level Cap by Character Level

| Char Level | Max Skill |
|------------|-----------|
| 1-2 | 3 |
| 3 | 5 |
| 4 | 7 |
| 5 | 10 |
| 6 | 13 |
| 7 | 15 |
| 8 | 17 |
| 9 | 20 |
| 10 | 23 |
| 11 | 25 |
| 12 | 27 |
| 13 | 30 |
| 14 | 33 |
| 15 | 35 |
| 16 | 37 |
| 17 | 40 |
| 18 | 43 |
| 19 | 45 |
| 20 | 47 |
| 21+ | 100 (no cap) |

### Blocked by

- Skill already at MAXSKILLPOINTS (100)
- Hungry or thirsty (`min_ham <= 0 || min_agua <= 0`)
- Skill at level cap for character level

---

## 3. Resource Gathering

### Max Items Extractable (Worker bonus)

```
max_items = max(1, Int((level - 2) * 0.2)) + 1
```

Worker class gets `rand(1, max_items)`, other classes get exactly 1.

### Fishing (`do_pescar`)

| Property | Value |
|----------|-------|
| Tool | Any weapon equipped (rod check by obj_index) |
| Target | Water tile (HayAgua check) |
| Stamina | Worker: 1, Other: 3 |
| Threshold | roll <= **6** |
| Output | PESCADO_OBJ (139) |
| Skill index | 12 (Pesca, 1-based 13) |
| Sound | SND_PESCAR (14) |

### Woodcutting (`do_talar`)

| Property | Value |
|----------|-------|
| Tool | HACHA_LENADOR (127) |
| Target | Tile with ObjType::Trees |
| Distance | 1-2 tiles |
| Stamina | Worker: 2, Other: 4 |
| Threshold | roll <= **6** |
| Output | LENA_OBJ (58) |
| Skill index | 9 (Talar, 1-based 10) |
| Sound | SND_TALAR (13) |

### Mining (`do_mineria`)

| Property | Value |
|----------|-------|
| Tool | PIQUETE_MINERO (187) |
| Target | Tile with ObjType::Deposit |
| Distance | up to 2 tiles |
| Stamina | Worker: 2, Other: 5 |
| Threshold | roll <= **5** (harder) |
| Output | Deposit's mineral_index (default HIERRO_CRUDO=192) |
| Skill index | 13 (Mineria, 1-based 14) |
| Sound | SND_MINERO (15) |

### Luck Roll Pattern (all three)

```
suerte = luck_denominator(skill)     // polynomial formula
roll = rand(1, suerte)
success = roll <= threshold           // 6 for fish/wood, 5 for mining
```

---

## 4. Crafting

### Smithing (`do_herreria` -> `handle_construct_smith`)

1. Click anvil tile (ObjType::Anvil, distance <= 2)
2. Server sends craftable weapons + armors lists (filtered by skill)
3. Lists sourced from `ArmasHerrero.dat` / `ArmadurasHerrero.dat`
4. Player selects item, sends CNS<obj_index>
5. Validate: skill >= obj.sk_herreria, has materials
6. Materials: `obj.ling_h` iron, `obj.ling_p` silver, `obj.ling_o` gold ingots
7. Remove materials, give item, play SND_HERRERO (41)

### Carpentry (`do_carpinteria` -> `handle_construct_carp`)

1. Double-click equipped SERRUCHO_CARPINTERO (198)
2. Server sends craftable items list (from `ObjCarpintero.dat`)
3. Player selects item, sends CNC<obj_index>
4. Validate: skill >= obj.sk_carpinteria, SERRUCHO equipped
5. Materials: `obj.madera` wood + `obj.piedras` stones
6. Remove materials, give item, play SND_CARPINTERO (42)

### Smelting (`do_fundir`)

Mineral-to-ingot conversion:

| Mineral | Obj ID | Per Ingot | Ingot | Obj ID |
|---------|--------|-----------|-------|--------|
| Hierro Crudo | 192 | 14 | Lingote Hierro | 386 |
| Plata Cruda | 193 | 20 | Lingote Plata | 387 |
| Oro Crudo | 194 | 35 | Lingote Oro | 388 |

Levels Mining skill (index 14) + grants crafting reputation.

### Weapon Melting (`do_fundir_arma`)

- Requires: MARTILLO_HERRERO (389) equipped, target is ObjType::Weapon
- Skill check: user Herreria >= obj.sk_herreria
- **Random yield**: 10-25% of original crafting materials (ling_h/ling_p/ling_o)
- Returns ingots proportionally

### Item Upgrades (`do_upgrade`)

- Item must have `upgrade` field pointing to target obj_index
- Two paths based on equipped tool:

| Path | Tool | Skill | Materials |
|------|------|-------|-----------|
| Herreria | MARTILLO_HERRERO (389) | SK16 | Iron/Silver/Gold ingots |
| Carpinteria | SERRUCHO_CARPINTERO (198) | SK15 | Wood + Stones |

**Material formula**: `needed = upgrade_mats - (current_mats * 0.5)`

Stamina: Worker=2, Other=6.

### Campfire (`handle_crear_fogata`)

- Requires: 3+ firewood (LENA_FOGATA=58) in inventory
- Blocked in safe zones
- Success chance by Survival skill:
  - skill < 6: 33% (1 in 3)
  - skill <= 34: 50% (1 in 2)
  - skill > 34: 100% (always)
- Places FOGATA_OBJ (63) on ground, auto-cleanup after 180 ticks

### Crafting Reputation

All crafting actions grant VL_PROLETA = +2 reputation (non-criminals only).
Cap: MAX_REP = 500,000.

---

## 5. Combat Skills

### Taming (`do_domar`)

**Formula**:
```
puntos_domar = Charisma * skill_domar
puntos_requeridos = NPC.domable * modifier
success = (puntos_requeridos <= puntos_domar) AND (rand(1,5) == 1)
```

**Flute modifiers** (ring slot):

| Ring | Modifier | Effect |
|------|----------|--------|
| FLAUTA_ELFICA (1050) | 0.8 | 20% easier |
| FLAUTA_MAGICA (208) | 0.89 | 11% easier |
| None | 1.0 | No bonus |

- Max pets: 3 (MAX_MASCOTAS)
- NPC must have `domable > 0`
- On success: `npc.maestro_user = Some(conn_id)`, hostile=false, target cleared
- Skill index: 17 (Domar, 1-based 18)
- Charisma attribute index: 3

### Ranged Attack (`do_ranged_attack`)

| Property | Value |
|----------|-------|
| Range | MAXDISTANCIAARCO = **18 tiles** |
| Stamina | min 10 required, 1-10 consumed (random) |
| Ammo | Bow+arrows (consume from ammo slot) or throwing weapon (consume weapon) |
| Skill index | 19 (Proyectiles) |

**Damage formula** (same as calcular_dano ranged path):
```
bow_dmg   = rand(bow.min_hit, bow.max_hit)
ammo_dmg  = rand(arrow.min_hit, arrow.max_hit)
weapon_dmg = bow_dmg + ammo_dmg
str_bonus = (total_max_hit / 5) * max(0, strength - 15)
user_dmg  = rand(user.min_hit, user.max_hit)
base      = 3 * weapon_dmg + str_bonus + user_dmg
damage    = base * class_mod_dano_proyectiles
```

Poison: 60% chance if arrow.envenena. No generic critical in ranged PvP.

---

## 6. Stealth Skills

### Stealing (`do_robar`)

| Property | Value |
|----------|-------|
| Stamina cost | 15 |
| Distance | up to 2 tiles |
| Luck | lookup table (same as try_desarmar) |
| Success | `rand(1, suerte) < 3` |

On success, 50/50 split:

**Item theft** (Ladron class only):
- Pick random non-equipped, non-newbie, non-key item
- Steal 5-10% of stack, minimum 1

**Gold theft**:
| Condition | Amount |
|-----------|--------|
| Ladron + GUANTE_HURTO (873) | `rand(level*50, level*100)` |
| Ladron without gloves | `rand(level*25, level*50)` |
| Other class | `rand(1, 100)` |

Victim is notified on both success and failure.

### Hiding (`do_ocultarse`)

**Success formula** (polynomial):
```
suerte = (((0.000002*s - 0.0002)*s + 0.0064)*s + 0.1124) * 100
success = rand(1, 100) <= suerte
```

**Duration formula** (polynomial on remaining = 100 - skill):
```
duration = (-0.000001 * remaining^3 + 0.00009229 * remaining^2 - 0.0088 * remaining + 0.9571) * config.intervalo_oculto
```

- Bandido: duration halved (`duration / 2`)
- Clanmates see semi-transparent (SetInvisible), others see removal (BP)
- Blocked on maps with `ocultar_sin_efecto=true`
- Can't hide while navigating (except Pirata)
- Using hide while spell-invisible breaks the spell first

**Hunter indefinite hide**: Cazador with skill > 90 + armor obj 648 or 360 stays hidden indefinitely (timer never expires in `check_permanecer_oculto`).

### Disarm (`try_desarmar`)

Ladron class only. Uses Wrestling skill.

```
success = rand(1, denominator) <= 2
```

Same lookup table as luck_denominator_lookup (see Section 2).

---

## 7. Tool Constants

| Constant | Value | Description |
|----------|-------|-------------|
| HACHA_LENADOR | 127 | Lumberjack axe |
| HACHA_LENA_ELFICA | 1005 | Elven lumberjack axe |
| HACHA_LENADOR_NEWBIE | 565 | Newbie lumberjack axe |
| PIQUETE_MINERO | 187 | Mining pick |
| PIQUETE_MINERO_NEWBIE | 566 | Newbie mining pick |
| MARTILLO_HERRERO | 389 | Blacksmith hammer |
| MARTILLO_HERRERO_NEWBIE | 567 | Newbie blacksmith hammer |
| SERRUCHO_CARPINTERO | 198 | Carpenter saw |
| SERRUCHO_CARPINTERO_NEWBIE | 564 | Newbie carpenter saw |
| CANA_PESCA | 543 | Fishing rod |
| CANA_PESCA_NEWBIE | 468 | Newbie fishing rod |

---

## 8. Resource Constants

| Constant | Value | Description |
|----------|-------|-------------|
| LENA_OBJ | 58 | Firewood |
| PESCADO_OBJ | 139 | Fish |
| HIERRO_CRUDO | 192 | Raw iron ore |
| PLATA_CRUDA | 193 | Raw silver ore |
| ORO_CRUDO | 194 | Raw gold ore |
| LINGOTE_HIERRO | 386 | Iron ingot |
| LINGOTE_PLATA | 387 | Silver ingot |
| LINGOTE_ORO | 388 | Gold ingot |
| PIEDRA_OBJ | 1225 | Magic stones (carpentry) |
| FOGATA_OBJ | 63 | Lit campfire |

---

## 9. Stamina Costs

| Action | Worker | Other |
|--------|--------|-------|
| Fishing | 1 | 3 |
| Woodcutting | 2 | 4 |
| Mining | 2 | 5 |
| Stealing | 15 | 15 |
| Ranged attack | 1-10 (random) | 1-10 (random) |
| Upgrade (smith/carp) | 2 | 6 |

---

## 10. Sound IDs

| Constant | Value | Action |
|----------|-------|--------|
| SND_TALAR | 13 | Woodcutting |
| SND_PESCAR | 14 | Fishing |
| SND_MINERO | 15 | Mining |
| SND_HERRERO | 41 | Blacksmithing |
| SND_CARPINTERO | 42 | Carpentry |

---

## 11. Work Left Click Dispatch (`handle_work_left_click`)

WLC packet format: `WLC<target_x>,<target_y>,<skill_type>`

| Skill Type | Handler |
|------------|---------|
| PESCA (13) | do_pescar |
| TALAR (10) | do_talar |
| MINERIA (14) | do_mineria |
| DOMAR (18) | do_domar |
| HERRERIA (16) | do_herreria |
| FUNDIR_METAL (88) | do_fundir |
| ROBAR (3) | do_robar |
| PROYECTILES (19) | do_ranged_attack |
| MAGIA (2) | do_cast_spell |
| OCULTARSE (8) | do_ocultarse |

Anti-cheat: `puede_trabajar` cooldown for all except Proyectiles (has own cooldown).
Range check: `|target - user| <= MIN_X/Y_BORDER`.

---

## 12. Survival Skill — Health Status Text

`health_status_text(current_hp, max_hp, survival_skill)` returns:

| Skill | Ranges |
|-------|--------|
| 0-10 | "Dudoso" (always) |
| 11-20 | <50%="Herido", else "Sano" |
| 21-30 | <25%="Malherido", <75%="Herido", else "Sano" |
| 31-40 | <15%="Muy malherido", <50%="Herido", <85%="Levemente herido", else "Sano" |
| 41+ | <5%="Agonizando", <15%="Casi muerto", <30%="Muy malherido", <50%="Herido", <75%="Levemente herido", <95%="Sano", else "Intacto" |
