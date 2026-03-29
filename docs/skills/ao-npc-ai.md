---
name: ao-npc-ai
description: Use when modifying NPC AI behaviors, aggro/vision, NPC combat, drop tables, pet system, spawn/respawn, or Pretoriano faction NPCs.
---

# AO NPC & AI System Reference

Source: `server/source/game/npc.rs`, `server/source/game/handlers/npcs.rs`

---

## 1. AI Types

| Constant | Value | Behavior |
|----------|-------|----------|
| AI_STATIC | 1 | No movement |
| AI_RANDOM | 2 | Random walk (1/12 chance per tick) |
| AI_HOSTILE_CHASE | 3 | Chase and attack nearby players |
| AI_DEFENSE | 4 | Follow attacker (triggered when non-hostile NPC is hit) |
| AI_GUARD | 5 | Attack criminals in range |
| AI_NPC_OBJETO | 6 | Stationary turret (no movement, attacks) |
| AI_FOLLOW_OWNER | 8 | Pet follows owner |
| AI_NPC_ATACA_NPC | 9 | Pet that targets other NPCs |
| AI_PATHFINDING | 10 | Chase using BFS pathfinding |

### Pretoriano AI Types (Faction NPCs)

| Constant | Value | Role |
|----------|-------|------|
| AI_SACERDOTE_PRETORIANO | 20 | Healer/support |
| AI_GUERRERO_PRETORIANO | 21 | Melee warrior |
| AI_MAGO_PRETORIANO | 22 | Spell caster |
| AI_CAZADOR_PRETORIANO | 23 | Ranged hunter |
| AI_REY_PRETORIANO | 24 | King (heals allies, fights when alone) |

---

## 2. Vision Range

```
NPC_VISION_X = 16    // half_x = 8 tiles
NPC_VISION_Y = 12    // half_y = 6 tiles
```

Aggro detection uses `+/- NPC_VISION/2` matching VB6 `RANGO_VISION_X=8, RANGO_VISION_Y=6`.

---

## 3. NPC State (`NpcState` struct)

### Identity

| Field | Type | Description |
|-------|------|-------------|
| index | NpcIndex (usize) | Runtime index in global NPC list |
| npc_number | usize | NPC database number (e.g., 500 = Murcielago) |
| char_index | CharIndex | Shared with players for CC/BP/MP packets |
| name | String | Display name |
| desc | String | Description text |

### Appearance

| Field | Type | Description |
|-------|------|-------------|
| body | i32 | Body GRH |
| head | i32 | Head GRH |
| heading | i32 | Facing direction (default 3=south) |
| weapon_anim | i32 | Weapon animation GRH |
| shield_anim | i32 | Shield animation GRH |
| casco_anim | i32 | Helmet animation GRH |
| aura | i32 | Aura effect (dynamically set, e.g., pre-dragon=937 gets aura 3) |

### Position

| Field | Type | Description |
|-------|------|-------------|
| map, x, y | i32 | Current position |
| orig_map, orig_x, orig_y | i32 | Spawn position (for respawn) |

### AI

| Field | Type | Description |
|-------|------|-------------|
| movement | i32 | Current AI type (AI_STATIC..AI_PATHFINDING) |
| hostile | bool | Will attack players on sight |
| npc_type | NpcType | Category (Dragon, CastleKing, etc.) |
| can_attack | bool | Attack capability flag |
| target | Option<ConnectionId> | Current player target |
| target_npc | usize | Current NPC target (pet vs NPC combat) |

### Stats

| Field | Type | Description |
|-------|------|-------------|
| max_hp | i32 | Maximum HP (non-hostile NPCs: 0 = always alive) |
| min_hp | i32 | Current HP (starts at max_hp) |
| max_hit, min_hit | i32 | Damage range |
| def | i32 | Physical defense |
| def_m | i32 | Magic defense vs spells |
| poder_ataque | i32 | Attack power (used in hit probability) |
| poder_evasion | i32 | Evasion power |
| alineacion | i32 | Alignment value |

### Economy

| Field | Type | Description |
|-------|------|-------------|
| give_exp | i32 | EXP awarded (distributed proportionally per hit) |
| give_gld | i32 | Fixed gold (unused in favor of min/max) |
| give_gld_min | i32 | Minimum gold drop |
| give_gld_max | i32 | Maximum gold drop |

### Commerce

| Field | Type | Description |
|-------|------|-------------|
| comercia | bool | NPC can trade |
| inflacion | i32 | Price inflation percentage |
| tipo_items | i32 | Item category filter |
| inv_respawn | bool | Inventory regenerates |
| inventory | Vec<NpcInvSlot> | Up to 25 slots (MAX_NPC_INV_SLOTS) |
| nro_items | i32 | Number of inventory items |

### Movement Constraints

| Field | Type | Description |
|-------|------|-------------|
| agua_valida | bool | Can walk on water tiles |
| tierra_invalida | bool | Can ONLY walk on water tiles |

### Status Effects

| Field | Type | Description |
|-------|------|-------------|
| veneno | bool | Poisons players on hit |
| paralyzed | bool | Currently paralyzed |
| counter_paralisis | i32 | Paralysis ticks remaining (decremented each game tick) |

### Spells

| Field | Type | Description |
|-------|------|-------------|
| lanza_spells | i32 | Spell casting capability level |
| spells | Vec<i32> | Available spell indices |
| ataca_doble | bool | 50% chance spell vs melee each AI tick |

### Pet/Summon

| Field | Type | Description |
|-------|------|-------------|
| maestro_user | Option<ConnectionId> | Owner player (None = wild NPC) |
| counter_perdio_npc | i32 | Inactivity timer (450 ticks = 18s at 40ms) |

### Pathfinding

| Field | Type | Description |
|-------|------|-------------|
| pf_path | Vec<(i32, i32)> | BFS computed path |
| pf_step | usize | Current step in path |

### Defense AI State

| Field | Type | Description |
|-------|------|-------------|
| old_movement | i32 | Saved AI type before AI_DEFENSE switch |
| old_hostile | bool | Saved hostile flag |
| attacked_by | String | Name of triggering attacker |

### Sounds

| Field | Type | Description |
|-------|------|-------------|
| snd1 | i32 | Attack sound |
| snd2 | i32 | Hit/hurt sound (fallback: SND_IMPACTO2=12) |
| snd3 | i32 | Death sound |

### Damage Tracking

```
damage_received: Vec<(ConnectionId, i32)>
```

Appended on every hit for proportional EXP distribution.

---

## 4. NPC Inventory

```rust
pub struct NpcInvSlot {
    pub obj_index: i32,
    pub amount: i32,
    pub prob_tirar: i32,   // Drop probability
}
```

- MAX_NPC_INV_SLOTS = 25
- Padded to full 25 slots on spawn

---

## 5. NPC Combat

### `puede_atacar_npc` (Attack Validation)

Blocks attack if:
- NPC dead or not attackable
- Attacker is dead
- Attacker privilege 1..8 (Consejero..GranDios)
- Horde faction attacking NPCs 617/948
- Alliance faction attacking NPCs 618/947
- Castle King: attacker not in guild, or attacking own guild's castle

### `user_attack_npc` (Melee PvE)

1. Call `puede_atacar_npc` validation
2. Calculate attack power (weapon type -> skill -> class mod)
3. Hit probability: `clamp(50 + (atk - npc_evasion) * 0.4, 10, 90)`
4. On hit: `calcular_dano` with class damage modifier
5. Boat damage bonus (if navigating)
6. Dragon Slayer check (1 damage to non-dragons)
7. Subtract NPC defense: `damage = max(0, base - npc.def)`
8. Track damage: `npc.damage_received.push((conn_id, damage))`
9. NpcAtacado trigger: non-hostile NPC switches to AI_DEFENSE
10. Backstab, critical, throat cut (same as PvP but vs NPC)
11. Per-hit EXP: `(give_exp / max_hp) * damage * exp_multiplier`
12. Check NPC death

### `npc_attack_user` (NPC Attacks Player)

1. Face target (update heading, send CP packet)
2. Skip if target has privileges > 0 or admin_invisible
3. Calculate evasion: `poder_evasion(tacticas, agility, level, class_mod) + shield_evasion`
4. Hit probability: `clamp(50 + (npc_ataque - user_evasion) * 0.4, 10, 90)`
5. On miss: shield block sub-roll, Tacticas skill gain
6. On hit: NPC damage = `rand(npc.min_hit, npc.max_hit) - armor_absorption`

---

## 6. NPC Death (`npc_die`)

1. Play death sound (snd3)
2. Send msg 50 to killer (death notification)
3. Drop items (if not a pet) via `npc_drop_items`
4. Remove character from area (BP packet)
5. Kill NPC (remove from grid, mark inactive)
6. Check Pretoriano death
7. Drop gold on floor: `rand(give_gld_min, give_gld_max) * multiplicador_oro`
8. Pet cleanup: remove from owner's pet slots

### `npc_drop_items`

```
prob = min(200, (prob_tirar * 2 + luck_mod) * drop_mult)
roll = rand(1, 200)
drop if roll <= prob
```

Charisma luck modifier:
| Charisma | Modifier |
|----------|----------|
| 0-18 | -1 |
| 19 | 0 |
| 20 | +1 |
| 21 | +2 |
| 22+ | charisma - 21 |

Items drop on free adjacent tiles. Full stack amount dropped.

---

## 7. Elemental NPCs

| Constant | NPC Number | Element |
|----------|-----------|---------|
| ELEMENTAL_AGUA | 92 | Water |
| ELEMENTAL_FUEGO | 93 | Fire |
| ELEMENTAL_TIERRA | 94 | Earth |

- **Singleton**: only 1 of each type per caster
- Tracked by: `user.ele_de_agua`, `user.ele_de_fuego`, `user.ele_de_tierra`
- Immune to paralysis spells
- Fire Elemental: reacts to PvP attacks on owner (`fire_elemental_react`)

---

## 8. Pet System

- Maximum pets: **3** (`MAX_MASCOTAS`)
- Stored in: `user.mascotas_index[0..3]`, `user.mascotas_type[0..3]`
- Counter: `user.nro_mascotas`
- Inactivity timer: `counter_perdio_npc` = 450 ticks (18s at 40ms)
- On death: removed from owner via `remove_pet_from_owner`
- Taming: assigns `npc.maestro_user = Some(conn_id)`, clears hostile/target

---

## 9. Spawn System

- NPCs spawned from map data with unique runtime NpcIndex
- Share CharIndex pool with players (for CC/BP/MP packets)
- `NpcState::from_data`: copies all NpcData fields, starts at full HP
- Default heading: 3 (south) if not specified
- `is_alive()`: `active && (max_hp == 0 || min_hp > 0)`
  - Non-hostile NPCs (shops/bankers) have max_hp=0: always "alive"

### Respawn

- Respawns at `orig_map/orig_x/orig_y` if tile is free
- Respawn interval: ~30s (configurable)

---

## 10. Area Visibility

### `send_area_npc_ccs`

Sends CC (CharacterCreate) packets for all alive NPCs in the area around a position.
Area bounds: `(x +/- MIN_X_BORDER, y +/- MIN_Y_BORDER)` clamped to map edges.

### `send_area_ground_items`

Sends HO (ObjectCreate) packets for all ground items in the area.

---

## 11. Defense AI Trigger (`NpcAtacado`)

When a non-hostile NPC is attacked:

1. Save: `old_movement = movement`, `old_hostile = hostile`
2. Switch: `movement = AI_DEFENSE`, `hostile = true`
3. Set: `attacked_by = attacker_name`, `target = Some(attacker_conn)`

If already hostile: update target to latest attacker (priority to most recent).
GMs (privileges > 0) do NOT become NPC targets.

---

## 12. Binary Packets

### `build_cc_binary` (CharacterCreate)

Sends: char_index, body, head, heading, x, y, weapon_anim, shield_anim, casco_anim.
NPCs: no name, no nick_color, no privileges in CC.

### `build_cd_binary` (CharData)

Sends: char_index, color=0, aura fields, levitando=false, ranking=0.
