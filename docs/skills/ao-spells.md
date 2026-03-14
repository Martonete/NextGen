---
name: ao-spells
description: Use when modifying spell casting, spell effects (damage/heal/status/buff/summon/teleport), mana costs, or adding new spells to the Rust server.
---

# AO Spell System Reference

Source: `server/source/game/handlers/spells.rs`

---

## 1. Cast Flow (LH -> RC)

### Step 1: Select Spell

`LH<slot>` sets `user.pending_spell = slot` (1..MAX_SPELL_SLOTS).
Does NOT cast -- waits for right-click.

### Step 2: Right-Click Triggers `do_cast_spell`

Called from `handle_right_click` when `pending_spell > 0`. Clears pending immediately.

### Step 3: PuedeLanzar Checks

| Check | Condition | Msg ID |
|-------|-----------|--------|
| Dead | `dead == true` | (silent return) |
| Weapon | Must have weapon equipped | 26 |
| Mana | `min_mana < spell.mana_requerido` | 18 |
| Stamina | `min_sta < spell.sta_requerido` | 18 |
| Magic skill | `skills[5] < spell.min_skill` | 834 |
| Staff power | Mago only: staff_power < spell.need_staff | 835 |

**Note**: Paralyzed users CAN cast spells (VB6 parity).

### Step 4: Resolve Targets

Checks world grid at target_x, target_y AND target_y+1 (VB6 LookatTile behavior):
- User target: `tile.user_conn`
- NPC target: `npc_at_tile(map, x, y)`

### Step 5: Target Validation by TargetType

| TargetType | Requirement |
|------------|-------------|
| UserOnly | Must have user target |
| NpcOnly | Must have NPC target |
| UserAndNpc | Must have either |
| Self_ | Forced to caster (ignores click) |
| Terrain | No target needed (invocation, teleport) |

### Step 6: Offensive Spell Checks

A spell is offensive if: `sube_hp==2 OR sube_ham==2 OR sube_sed==2 OR paraliza OR inmoviliza OR envenena OR maldicion`

Blocked by:
- Safe zone (attacker or victim tile trigger == SafeZone)
- Self-attack (`target == caster` for offensive)
- Dead target / admin target
- Clan safe (`seguro_clan` on + same clan)
- Full HP target for heals (`sube_hp==1`, msg 145)
- RemoverParalisis on non-paralyzed target (silent fail)

### Step 7: Apply Effects + Consume Mana

Order: InfoHechizo (FX/sound/messages) -> apply effect -> consume mana.
Mana is consumed ONLY after successful application.

---

## 2. Spell Types

### Properties (SpellType::Properties)

Modifies HP, Mana, Stamina, Hunger, Thirst:

| Field | Value 1 | Value 2 |
|-------|---------|---------|
| sube_hp | Heal | Damage |
| sube_mana | Restore | -- |
| sube_sta | Restore | -- |
| sube_ham | Restore | Drain |
| sube_sed | Restore | Drain |

Each uses `rand(spell.min_X, spell.max_X)` for the amount.

### Status (SpellType::Status)

| Flag | Effect |
|------|--------|
| envenena | Set poisoned=true |
| cura_veneno | Set poisoned=false |
| paraliza | Set paralyzed=true, duration=config.intervalo_paralizado |
| inmoviliza | Set paralyzed+immobilized=true |
| remover_paralisis | Clear paralyzed+immobilized |
| maldicion | Set cursed=true |
| remover_maldicion | Clear cursed |
| bendicion | Set blessed=true |
| estupidez | Set stunned=true, duration=intervalo_paralizado |
| remover_estupidez | Clear stunned |
| ceguera | Set blind=true, duration=intervalo_paralizado/3 |
| invisibilidad | Set invisible+hidden, counter=0 |
| revivir | Resurrection (see below) |

### Buffs (via `apply_spell_buffs`)

| Field | Value 1 | Value 2 |
|-------|---------|---------|
| sube_agilidad | Buff agility | Debuff agility |
| sube_fuerza | Buff strength | Debuff strength |
| sube_carisma | Buff charisma | Debuff charisma |

- Buff duration: **1200 ticks** (~48s at 40ms)
- Debuff duration: **700 ticks** (~28s at 40ms)
- Cap: `base_attribute * 2`, max 50
- Backup saved on first buff (`attributes_backup`)

### Invocation (SpellType::Invocation)

Spawns NPC pets at caster position.
- Max pets: **3** (`MAX_MASCOTAS`)
- Elemental singletons: only 1 of each type allowed
- Blocked on maps with `invocar_sin_efecto=true`

### SummonPet (SpellType::SummonPet)

Toggle: if pet of same type exists, dismiss it. Otherwise spawn like Invocation.

### Teleport (SpellType::Teleport)

Warps caster to `spell.portal_map/portal_x/portal_y`.
- Drains ALL mana to 0
- Sends ChangeMap + PosUpdate + PlayMidi + MapName packets

---

## 3. Damage Formula (`calc_spell_damage`)

```
base = rand(spell.min_hp, spell.max_hp)
damage = base + base * (3 * caster_level / 100)
```

### Staff Bonus (Mages only, when spell.staff_affected)

```
if has_staff:
    damage = damage * (staff_damage_bonus + 70) / 100
else:
    damage = damage * 0.70    // 30% penalty without staff
```

### Lute/Flute Bonus

```
if ring == LAUDELFICO(1049) or FLAUTAELFICA(1050):
    damage *= 1.04    // +4%
```

### Magic Defense Subtraction

```
helmet_def = rand(helmet.defensa_magica_min, helmet.defensa_magica_max)
ring_def   = rand(ring.defensa_magica_min, ring.defensa_magica_max)
final_damage = max(0, damage - helmet_def - ring_def)
```

### Heal Formula (`calc_spell_heal`)

Same as damage but without staff/lute bonuses:
```
heal = rand(spell.min_hp, spell.max_hp) + base * (3 * level / 100)
```

---

## 4. Mana Cost Modifiers (`consume_spell_mana`)

GMs (privileges > 0) consume no mana or stamina.

### Druid + Flauta Elfica (ring obj 1050)

| Spell type | Discount |
|------------|----------|
| Mimetiza | 50% (`cost * 0.5`) |
| Invocation | 30% (`cost * 0.7`) |
| Other (except Apocalipsis #25) | 10% (`cost * 0.9`) |
| Apocalipsis (spell index 25) | No discount |

---

## 5. Status Effect Details

### Super Anillo (obj 700)

Blocks: paralysis, immobilize, poison, curse, blindness, stun.
Message: "El Super Anillo rechaza el hechizo."

### Paralysis

- Duration: `config.intervalo_paralizado` ticks
- Sends PARADOK packet + PosUpdate (prevent ghost movement)
- RemoverParalisis: clears paralyzed+immobilized, sends PARADOK(0)

### Invisibility

- Counter starts at 0, counts up to `config.intervalo_invisible`
- Non-clanmates: character removed from screen (BP packet)
- Clanmates: see semi-transparent (SetInvisible packet)
- Self: receives SetInvisible notification
- Blocked on maps with `invi_sin_efecto=true`
- Navigating: skip SetInvisible (boat already hides)

### Blindness

Duration: `intervalo_paralizado / 3`

### Stun

Duration: `intervalo_paralizado` (same as paralysis)

---

## 6. Mimetiza (Druid Mimicry)

### On User Target

Copies: body, head, weapon_anim, shield_anim, casco_anim from target.
Saves originals in `char_mimetizado_*` fields. Sends CP to area.

### On NPC Target

Copies: body, head from NPC. Clears weapon/shield/helmet anims to 0.
Saves originals. Druid-only check.

---

## 7. Resurrection

| Caster class | Timing | HP restored |
|-------------|--------|-------------|
| Clerigo | Instant | Full (`max_hp`) |
| Other | 10s delay (`segundos_para_revivir = 10`) | Unset (delayed) |

### Caster HP Cost

```
new_hp = caster.min_hp * (1.0 - target_level * 0.015)
caster.min_hp = max(1, new_hp)
```

### Reset on resurrection (both paths)

- hunger = 0, thirst = 0, mana = 0, stamina = 0

### Blocked by

- `seguro_resu = true` (target opted out, msg 841)
- `time_revivir > 0` (resurrection cooldown, msg 843)

---

## 8. NPC Spell Interactions

### Properties on NPC

- sube_hp=1: Heal NPC (pet healing)
- sube_hp=2: Damage NPC (standard attack spell)
- NPC magic defense: `npc.def_m` subtracted from damage

### Status on NPC

- Elementals (AGUA=92, FUEGO=93, TIERRA=94) immune to paralysis
- Poison/paralysis applied same as users
- RemoverParalisis does NOT work on NPCs

### Per-hit EXP on NPC spell damage

```
exp = (npc.give_exp / npc.max_hp) * damage * multiplicador_exp
```

---

## 9. Key Constants

| Constant | Value | Description |
|----------|-------|-------------|
| SUPERANILLO | 700 | Blocks negative status effects |
| LAUDELFICO | 1049 | Laud Elfico (Bard lute) |
| FLAUTAELFICA | 1050 | Flauta Elfica (Druid flute) |
| LAUDMAGICO | 696 | Laud Magico |
| APOCALIPSIS_SPELL_INDEX | 25 | Apocalypse spell (no Druid discount) |
| MAX_SPELL_SLOTS | (from types) | Maximum spell book size |

---

## 10. Spell Dispatch Summary

```
LH<slot>        -> handle_cast_spell -> pending_spell = slot
RC (right-click) -> do_cast_spell:
  NPC target:    apply_spell_properties_npc / apply_spell_status_npc
  User target:   apply_spell_properties / apply_spell_status / apply_spell_buffs
  Self:          apply_spell_properties / apply_spell_status / apply_spell_buffs
  Terrain:       apply_spell_invocation / apply_spell_summon_pet / apply_spell_teleport
```
