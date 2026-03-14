---
name: ao-combat
description: Use when modifying melee/ranged combat, damage formulas, PvP rules, death/resurrection, or critical hit mechanics in the Rust server.
---

# AO Combat System Reference

Source: `server/source/game/handlers/combat.rs`, `server/source/game/constants.rs`

---

## 1. Attack Power Formulas

### `poder_ataque_arma(skill, agility, level, class_mod) -> i64`

Melee weapon attack power. Also used by `poder_ataque_proyectil` and `poder_ataque_wrestling`
(identical formula, different skill/class_mod inputs).

```
if skill < 31:   temp = skill * class_mod
if skill < 61:   temp = (skill + agility) * class_mod
if skill < 91:   temp = (skill + 2*agility) * class_mod
if skill >= 91:  temp = (skill + 3*agility) * class_mod

result = temp + 2.5 * max(0, level - 12)
```

- `skill` = UserSkills index (Armas=1, Proyectiles=5, Wrestling=20)
- `class_mod` = balance.class_mod_ataque_armas/proyectiles/wrestling per class
- Arithmetic uses f64 cast to i64 (truncation, matching VB6 Long*Single)

### `poder_evasion(tacticas, agility, level, class_mod) -> i64`

```
temp = (tacticas + tacticas/33 * agility) * class_mod
result = temp + 2.5 * max(0, level - 12)
```

- `tacticas` = UserSkills[3] (SK4 = Tacticas)
- `class_mod` = balance.class_mod_evasion per class

### `poder_evasion_escudo(skill_defensa, class_mod_escudo) -> i64`

```
result = (skill_defensa * class_mod_escudo) / 2
```

- Only added when victim has shield equipped
- `class_mod_escudo` = balance.class_mod_escudo per class

---

## 2. Hit Probability

```
prob_exito = clamp(50 + (attack_power - victim_evasion) * 0.4, 10, 90)
```

- Meditation penalty: victim evasion reduced by 25% (`prob_evadir *= 0.75`)
- Shield evasion is added to base evasion before this calculation
- Roll: `rand(1..100) <= prob_exito` = hit

### Shield Block (on miss)

Separate roll after a miss when victim has shield:

```
suma_skills = max(1, defensa + tacticas)
prob_rechazo = clamp(100 * defensa / suma_skills, 10, 90)
```

- Success: plays SND_ESCUDO (37), levels Defensa skill (hit=true)
- Failure: still attempts Defensa skill gain (hit=false)

---

## 3. Damage Calculation (`calcular_dano`)

### With weapon (obj_index > 0)

```
dano_arma     = rand(weapon_min_hit, weapon_max_hit)
dano_max_arma = weapon_max_hit

if ranged + has_ammo:
    dano_arma += rand(ammo_min_hit, ammo_max_hit)

dano_usuario = rand(user_min_hit, user_max_hit)
raw = 3 * dano_arma + (dano_max_arma / 5 * max(0, strength - 15)) + dano_usuario
damage = raw * class_mod_damage
```

### Wrestling (unarmed, obj_index = 0)

```
base = rand(4, 9)   // if ring is_guante: base += ring hit range
damage = (3 * base + (max_base / 5 * max(0, STR-15)) + rand(user_min, user_max)) * class_mod_wrestling
```

### Dragon Slayer (ESPADA_VIKINGA = 402)

- Always deals exactly **1 damage** in PvP (ignores all formulas)
- vs NPCs: full damage to Dragons, 1 to everything else
- Consumed on dragon kill

### Weapon Penetration (Refuerzo)

```
damage += weapon.refuerzo    // added to final damage (PvP only, not in NPC combat)
```

---

## 4. Armor Absorption (PvP)

### Body part roll

```
lugar = rand(BODY_PART_HEAD=1, BODY_PART_TORSO=6)
```

### Head hit (lugar = 1)

```
defense = rand(helmet.min_def, helmet.max_def)
```

### Body hit (lugar = 2..6)

```
defense = rand(armor.min_def, armor.max_def) + rand(shield.min_def, shield.max_def)
```

### NPC combat armor (with penetration)

```
absorption = rand(armor.min_def, armor.max_def) + rand(shield.min_def, shield.max_def) - weapon.refuerzo
absorption = max(0, absorption)
```

---

## 5. Special Attacks

### Backstab (`do_apunalar`)

**Condition**: attacker_heading == victim_heading (behind target), Apunalar skill > 0

**Luck formula** (polynomial per class):
```
Asesino:   suerte = ((0.00003*s - 0.002)*s + 0.098)*s + 4.25
Cle/Pal/Pi:suerte = ((0.000003*s + 0.0006)*s + 0.0107)*s + 4.93
Bardo:     suerte = ((0.000002*s + 0.0002)*s + 0.032)*s + 4.81
Other:     suerte = 0.0361*s + 4.39
```

**Damage multiplier** (on success):
| Target | Asesino | Other classes |
|--------|---------|---------------|
| NPC    | 2.0x    | 2.0x          |
| Player | 1.4x    | 1.5x          |

- Assassin ignores NPC defense for backstab base damage

### Critical Hit (`do_golpe_critico`)

**Only**: Bandido class + ESPADA_VIKINGA (402) equipped.

```
suerte = (((0.00000003*s + 0.000006)*s + 0.000107)*s + 0.0893) * 100
damage = base_damage * 0.75
```

- `s` = Wrestling skill
- Roll: `rand(1..100) <= suerte`

### Throat Cut (`do_acuchillar`)

**Only**: Pirata class + weapon.acuchilla flag.

```
chance = PROB_ACUCHILLAR (20%)
damage = base_damage * DANO_ACUCHILLAR (0.2)
```

- PvP: only on ranged (projectile) attacks
- PvE: on melee attacks

### Disarm (`do_desequipar`)

**Condition**: GUANTE_HURTO (873) ring + unarmed.

```
prob = wrestling * 0.2 + level * 0.66
```

Removes (in order): shield -> weapon -> helmet

### Hand Immobilization (`DoHandInmo`)

**Only**: Ladron class + GUANTE_HURTO (873) ring.

```
prob = wrestling / 4
duration = config.intervalo_paralizado / 2
```

### Weapon Disarm (`try_desarmar`)

**Only**: Ladron class. Lookup table based on Wrestling skill:

| Skill    | Denominator |
|----------|-------------|
| 0-10     | 35          |
| 11-20    | 30          |
| 21-30    | 28          |
| 31-40    | 24          |
| 41-50    | 22          |
| 51-60    | 20          |
| 61-70    | 18          |
| 71-80    | 15          |
| 81-90    | 10          |
| 91-100   | 5           |

Success: `rand(1, denominator) <= 2`

---

## 6. PvP Attack Flow (`handle_attack`)

1. Dead check, reveal hidden/invisible
2. Block ranged weapons from melee (`proyectil` flag)
3. Anti-flood cooldown (`puede_pegar`)
4. Get target tile from heading offset
5. Safety checks: safe_toggle, clan safe, safe zone (duel bypasses)
6. Hit/miss roll (attack_power vs evasion)
7. On miss: shield block sub-roll, skill gains (attacker weapon, victim Tacticas)
8. On hit: calculate damage, body part roll, armor absorption
9. Special attacks: DoDesequipar, DoHandInmo, DoApunalar, DoGolpeCritico, DoAcuchillar, Desarmar
10. Poison application: 60% if weapon.envenena
11. Fire Elemental react (pet retaliates)
12. Check death

---

## 7. Death & Resurrection

### `user_die(conn_id, killer_id)`

- Set dead=true, HP=0, STA=0
- Clear all status effects (paralyzed, invisible, poison, mounted, etc.)
- Ghost body: `DEAD_BODY_NEUTRAL=8`, head `DEAD_HEAD_NEUTRAL=500`
- Navigating: `iFragataFantasmal=87` instead
- Weapon/shield/helmet/armor/ring all unequipped
- Drop all non-newbie items on ground (newbie = level < 13, unless criminal)
- Resurrection cooldown: `time_revivir = 20`
- Clear duel state

### Resurrection (spell)

| Caster class | Effect |
|-------------|--------|
| Clerigo     | Instant revive at full HP |
| Other       | 10-second delayed revive (`segundos_para_revivir = 10`) |

**HP cost to caster**: `hp * (1 - target_level * 0.015)`, minimum 1 HP

Both paths reset: hunger=0, thirst=0, mana=0, stamina=0

**Blocked by**: seguro_resu (target opted out), time_revivir > 0 (cooldown)

---

## 8. Combat Constants

| Constant | Value | Description |
|----------|-------|-------------|
| `BODY_PART_HEAD` | 1 | Head hit identifier |
| `BODY_PART_TORSO` | 6 | Body hit range upper bound |
| `PROB_ACUCHILLAR` | 20 | Pirate throat cut chance (%) |
| `DANO_ACUCHILLAR` | 0.2 | Throat cut damage multiplier |
| `ESPADA_VIKINGA` | 402 | Dragon Slayer sword obj_index |
| `GUANTE_HURTO` | 873 | Pickpocket gloves obj_index |
| `DEAD_BODY_NEUTRAL` | 8 | Ghost body GRH |
| `DEAD_HEAD_NEUTRAL` | 500 | Ghost head GRH |
| `NINGUN_ARMA` | 2 | No weapon equipped anim |
| `NINGUN_ESCUDO` | 2 | No shield equipped anim |

### Fallback Class Damage Modifiers

| Class | Modifier |
|-------|----------|
| Guerrero | 1.1 |
| Cazador | 0.9 |
| Paladin | 1.0 |
| Asesino | 1.0 |
| Ladron | 0.8 |
| Bardo | 0.8 |
| Clerigo | 0.8 |
| Mago | 0.5 |
| Druida | 0.7 |
| Pirata | 1.0 |
| Trabajador | 0.8 |
| Bandido | 0.9 |

---

## 9. Skill Index Reference (Combat)

| Index | Skill | Usage |
|-------|-------|-------|
| 1 | Armas | Melee weapon attacks |
| 3 | Tacticas | Evasion (victim), levels on dodge/hit |
| 4 | Defensa | Shield block chance |
| 5 | Proyectiles | Ranged weapon attacks |
| 8 | Apunalar | Backstab attempts |
| 20 | Wrestling | Unarmed attacks, disarm, critical formula |
