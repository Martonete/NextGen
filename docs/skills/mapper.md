# Skill: Argentum Online Map System

How maps work in Argentum Online. Complete byte-level format spec.

## Overview

Each map consists of 3 files:

| File | Purpose | Format |
|------|---------|--------|
| `MapaN.map` | Tile graphics, blocking, triggers | Binary |
| `MapaN.inf` | Exits, NPCs, objects | Binary |
| `MapaN.dat` | Metadata (name, music, flags) | INI text |

**Dimensions**: 100x100 tiles, 1-indexed (X=1..100, Y=1..100).
**Endianness**: Little-Endian (VB6/Windows standard).
**Total maps**: Configured in `dat/Map.dat` → `[INIT] NumMaps=193`.

---

## .MAP File Format

### Header (273 bytes)

| Offset | Size | Type | Field |
|--------|------|------|-------|
| 0 | 2 | i16 LE | MapVersion |
| 2 | 255 | ASCII | Description (fixed-length, "Argentum Online by Noland Studios...") |
| 257 | 4 | i32 LE | CRC |
| 261 | 4 | i32 LE | MagicWord |
| 265 | 8 | — | Reserved |

### Tile Data

After header, 10,000 tiles in **row-major order**: Y=1→100, X=1→100 per row.

Each tile is variable-length:

```
[ByFlags: 1 byte]
[Graphic[1]: i16]          ← always present (ground layer)
[Graphic[2]: i16]          ← if ByFlags & 0x02
[Graphic[3]: i16]          ← if ByFlags & 0x04
[Graphic[4]: i16]          ← if ByFlags & 0x08
[Trigger: i16]             ← if ByFlags & 0x10
[ParticleGroupIndex: i16]  ← if ByFlags & 0x20 (Rust extension)
[RangeLight: i16 + RGB: 3×i16] ← if ByFlags & 0x40 (Rust extension)
```

**ByFlags bits:**

| Bit | Hex | Meaning |
|-----|-----|---------|
| 0 | 0x01 | Blocked (movement) |
| 1 | 0x02 | Has Layer 2 graphic |
| 2 | 0x04 | Has Layer 3 graphic |
| 3 | 0x08 | Has Layer 4 graphic |
| 4 | 0x10 | Has Trigger |
| 5 | 0x20 | Has ParticleGroup (Rust only) |
| 6 | 0x40 | Has Lighting (Rust only) |

**Graphic layers:**
- Layer 1: Ground (always present)
- Layer 2: Ground overlay (water edges, paths)
- Layer 3: Objects/furniture (above characters)
- Layer 4: Rooftops/ceilings

**Graphic index** = i16 pointing to grh resource. 0 = empty/transparent.

---

## .INF File Format

### Header (10 bytes)

All zeros. Skip.

### Tile Data

Same row-major order as .map. Variable-length per tile:

```
[ByFlags: 1 byte]
[TileExit: 3×i16]   ← if ByFlags & 0x01 (Map, X, Y)
[NpcIndex: i16]      ← if ByFlags & 0x02
[ObjIndex: i16 + Amount: i16] ← if ByFlags & 0x04
```

**ByFlags bits:**

| Bit | Hex | Meaning | Size |
|-----|-----|---------|------|
| 0 | 0x01 | TileExit (teleport) | 6 bytes: dest_map(i16) + dest_x(i16) + dest_y(i16) |
| 1 | 0x02 | NPC spawn | 2 bytes: npc_number(i16) |
| 2 | 0x04 | Object on ground | 4 bytes: obj_index(i16) + amount(i16) |

### TileExit Rules

When a player steps on a tile with TileExit:
1. Server teleports player to `(dest_map, dest_x, dest_y)`
2. **Critical**: dest coords must NOT land on another exit tile, or infinite loop occurs
3. Correct pattern: exit at border Y=N → destination Y=N±2 (one tile past the opposite exit row)

Example (Map 28 ↔ Map 18):
```
Map 28, Y=94 (south exit) → Map 18, Y=8  (one tile south of Map 18's north exit at Y=7)
Map 18, Y=7  (north exit) → Map 28, Y=93 (one tile north of Map 28's south exit at Y=94)
```

### NPC Spawning

When `NpcIndex > 0`:
- Server instantiates NPC from NPCs.dat template
- If NPC has `PosOrig=1`, this tile is its respawn point
- Otherwise, respawn defined in NPC template

---

## .DAT File Format (INI)

```ini
[MapaN]
Name=Ciudad de Tanaris
MusicNum=5
Pk=0
MagiaSinEfecto=0
InviSinEfecto=0
ResuSinEfecto=0
OcultarSinEfecto=0
InvocarSinEfecto=0
NoEncriptarMP=0
Terreno=BOSQUE
Zona=CAMPO
Restringir=No
BackUp=0
RoboNpcsPermitido=0
```

| Key | Type | Purpose |
|-----|------|---------|
| Name | String | Display name |
| MusicNum | int | Music track ID |
| Pk | 0/1 | 0=PvP enabled, 1=safe zone |
| MagiaSinEfecto | 0/1 | Disable all magic |
| InviSinEfecto | 0/1 | Disable invisibility |
| ResuSinEfecto | 0/1 | Disable resurrection |
| OcultarSinEfecto | 0/1 | Disable hiding |
| InvocarSinEfecto | 0/1 | Disable summoning |
| NoEncriptarMP | 0/1 | Skip movement packet encrypt |
| Terreno | String | Terrain type (BOSQUE, NIEVE, DESIERTO, etc.) |
| Zona | String | Zone type (CAMPO, CIUDAD, DUNGEON) |
| Restringir | String | Class/race restriction |
| BackUp | 0/1 | Include in backups |
| RoboNpcsPermitido | 0/1 | Allow stealing from NPCs |

---

## Trigger Types

| Value | Name | Effect |
|-------|------|--------|
| 0 | None | Default |
| 1 | Indoor | Under roof (renders ceiling layer) |
| 2 | Reserved | Unused |
| 3 | InvalidPos | NPCs cannot walk here |
| 4 | SafeZone | No PvP, no theft |
| 5 | AntiBlock | Anti-picketing zone |
| 6 | CombatZone | No item drops on death, no faction changes |
| 7 | NoElevation | Prevents flight (Rust only) |

---

## Data Structures

### Rust (maps.rs)

```rust
pub struct MapTile {
    pub blocked: bool,
    pub graphic: [i16; 4],
    pub trigger: Trigger,
    pub particle_group_index: i16,
    pub range_light: i16,
    pub rgb_light: [i16; 3],
    pub tile_exit: Option<WorldPos>,
    pub npc_index: i16,
    pub obj: TileObj,
    // Runtime:
    pub user_index: i16,
    pub original_blocked: bool,
    pub original_obj_index: i16,
}

pub struct GameMap {
    pub info: MapInfo,
    pub tiles: Box<[[MapTile; 100]; 100]>,  // [y][x], 0-indexed
}
```

### VB6 (Declares.bas)

```vb6
Type MapBlock
    Blocked As Byte
    Graphic(1 To 4) As Integer
    UserIndex As Integer
    NpcIndex As Integer
    ObjInfo As Obj
    TileExit As WorldPos
    trigger As eTrigger
End Type
```

---

## Loading Algorithm

```
1. Read MapaN.map:
   - Skip 273-byte header
   - For Y=1..100, X=1..100:
     - Read ByFlags
     - Read Graphic[1] (always)
     - Conditionally read layers 2-4, trigger, particles, lighting

2. Read MapaN.inf:
   - Skip 10-byte header
   - For Y=1..100, X=1..100:
     - Read ByFlags
     - Conditionally read TileExit (6B), NPC (2B), Object (4B)
     - Instantiate NPCs where NpcIndex > 0

3. Read MapaN.dat:
   - Parse INI section [MapaN]
   - Load name, music, PK flags, terrain, etc.

4. Post-load:
   - Snapshot original_blocked + original_obj_index (door detection)
   - Register map in global array (1-indexed, slot 0 unused)
```

---

## Patching .INF Files (Python)

To modify exit tiles programmatically:

```python
import struct

def patch_exit_y(filepath, target_y_1indexed, new_dest_y):
    """Patch all TileExit destinations at a specific Y row."""
    with open(filepath, 'rb') as f:
        data = bytearray(f.read())

    pos = 10  # skip header
    patched = 0

    for tile_idx in range(10000):
        y = tile_idx // 100      # 0-indexed
        x = tile_idx % 100       # 0-indexed
        flags = data[pos]
        pos += 1

        if flags & 1:  # TileExit
            if y == target_y_1indexed - 1:  # convert to 0-indexed
                struct.pack_into('<h', data, pos + 4, new_dest_y)
                patched += 1
            pos += 6
        if flags & 2:  # NPC
            pos += 2
        if flags & 4:  # Object
            pos += 4

    with open(filepath, 'wb') as f:
        f.write(data)
    return patched
```

---

## Common Pitfalls

1. **Exit loop**: Destination lands ON an exit tile → infinite teleport loop. Always land 1 tile past the opposite border's exit row.
2. **0-indexed vs 1-indexed**: Files store tiles 0-indexed sequentially, but game coordinates are 1-indexed (1..100).
3. **Variable tile size**: Cannot seek to a specific tile — must iterate from start.
4. **Case sensitivity**: Linux needs case-insensitive file lookup (MapaN.map vs MapaN.MAP).
5. **Rust extensions** (bits 5-6 in .map ByFlags): VB6 maps won't have these — code must handle gracefully.
6. **X drift in exits**: If both directions have X+1 offset, round trips drift the player sideways. Keep X identical.
