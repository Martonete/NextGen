---
name: ao-sprite-indexing
description: Use when modifying Graficos.ind loading, GRH animation system, texture management, sprite sheets, or water/lava tile GRH patterns in the Godot client.
---

# AO Sprite & GRH Indexing Reference

Source: `client/Scripts/Data/GrhLoader.cs`, `TextureManager.cs`

Source: `client/Scripts/Data/GrhLoader.cs`, `client/Scripts/Data/TextureManager.cs`

---

## 1. Graficos.ind Binary Format

The GRH database is a single binary file containing 207,145 sprite entries, all of
them from the pixel-art catalogue (sheets 100000+). Legacy art was dropped and the
numbering restarted at 1 — see `docs/catalog/remap.json` for the old-to-new map.
Three header formats exist; `GrhLoader.Load()` auto-detects which one is present.

### Header Detection (3 formats)

| Format | Offset | Layout |
|--------|--------|--------|
| **No MiCabecera** | 0 | `Version(i32) + Count(i32) + entries` |
| **With MiCabecera** | 263 | `MiCabecera(263 bytes) + Version(i32) + Count(i32) + entries` |
| **Flag byte** | 1 | `FlagByte(1) + Version(i32) + Count(i32) + entries` |

**Auto-detect logic** (from `GrhLoader.Load()`): each candidate offset (0, 263, 1)
is tried in turn, and the one whose following bytes parse as a valid GRH record
wins. Note it does **not** judge by how large `Count` is: an earlier version
rejected `Count > 100000` and fell through to a wrong offset once the catalogue
grew, so `Count` is treated purely as a hint for the initial array size.

Current values: `Version=447`, `Count=207145`.

**MiCabeceraSize** = 263 bytes (255 desc + 4 CRC + 4 magic).

### Entry Stream

After the header, entries are read sequentially until EOF. Each entry starts with
`GrhIndex(i32)`. Reading stops when `GrhIndex <= 0` or EOF.

Array is allocated as `GrhData[Count + 1]` (sparse indices). If an index exceeds
the array, it is dynamically expanded by +100 slots.

---

## 2. GrhData Structure

```csharp
public class GrhData
{
    public short NumFrames;     // 1 = static sprite, >1 = animated
    public int FileNum;         // texture file number ({FileNum}.png)
    public short SX;            // source X in texture (pixels)
    public short SY;            // source Y in texture (pixels)
    public short PixelWidth;    // sprite width (pixels)
    public short PixelHeight;   // sprite height (pixels)
    public float TileWidth;     // PixelWidth / 32.0
    public float TileHeight;    // PixelHeight / 32.0
    public int[]? Frames;       // animation frame GRH indices (null for uninitialized)
    public float Speed;         // animation speed (ms per full cycle)
}
```

### Key Fields

- **NumFrames=1**: Static sprite. `Frames = [self-index]` (self-reference).
- **NumFrames>1**: Animated sprite. `Frames[0..N-1]` point to static GRH entries.
- **TileWidth/TileHeight**: Computed as `PixelWidth / 32f`. Used for centering
  multi-tile sprites (bodies, trees, buildings).
- **Speed**: Only present in animated entries. Milliseconds for one full animation cycle.

---

## 3. Binary Entry Formats

### Static GRH Entry (18 bytes)

```
GrhIndex    i32    4 bytes    sparse index into array
NumFrames   i16    2 bytes    always 1
FileNum     i32    4 bytes    texture file number
SX          i16    2 bytes    source X
SY          i16    2 bytes    source Y
PixelWidth  i16    2 bytes    width
PixelHeight i16    2 bytes    height
```

After reading, `TileWidth = PixelWidth / 32f` and `TileHeight = PixelHeight / 32f`.
`Frames` is set to `[GrhIndex]` (self-reference for uniform frame access).

### Animated GRH Entry (10 + 4*N bytes)

```
GrhIndex    i32    4 bytes    sparse index
NumFrames   i16    2 bytes    N > 1
Frames[N]   i32*N  4*N bytes  indices of static GRH frames
Speed       f32    4 bytes    VB6 Single (IEEE 754 float)
```

Dimensions are inherited from `Frames[0]` (first frame's static GRH).

---

## 4. Two-Pass Loading

### Pass 1: Read all entries

Static GRHs get their dimensions directly from the binary data. Animated GRHs
attempt to inherit from their first frame, but this only works if `Frames[0]` was
loaded before the current entry (entries are sparse, not ordered).

### Pass 2: Resolve unresolved animations

```csharp
for (int i = 1; i < grhs.Length; i++)
{
    if (grhs[i].NumFrames > 1 && grhs[i].PixelWidth == 0 && grhs[i].Frames != null)
    {
        int firstIdx = grhs[i].Frames[0];
        // Copy PixelWidth, PixelHeight, FileNum, SX, SY, TileWidth, TileHeight
        // from grhs[firstIdx]
    }
}
```

This resolves forward-reference cases where an animation entry appears before
its first frame in the file.

---

## 5. GRH Resolution at Draw Time

`GameData.ResolveGrh(grhIndex, frame)` resolves an animated GRH to its concrete
static frame:

1. If `NumFrames == 1` -> return the GRH itself (static).
2. If `NumFrames > 1` -> return `Grhs[Frames[frame % NumFrames]]`.
3. Frame index comes from either walk animation counters or global time-based cycling.

**Animation frame cycling** (time-based):
```
frame = (int)(globalTimeMs / Speed % NumFrames)
```

---

## 6. Texture Manager

Source: `client/Scripts/Data/TextureManager.cs`

### File Layout

- Path: `client/Data/Graficos/`
- Naming: `{FileNum}.png` (e.g., `1.png`, `3291.png`)
- Total: ~3,291 PNG files

### LRU Cache

- Max cache size: **4,096 textures** (constant `MaxCacheSize`)
- O(1) LRU eviction via `LinkedList<int>` + `Dictionary<int, LinkedListNode<int>>`
- Textures loaded on demand via `GetTexture(fileNum)`

### Black Color Key (Transparency)

VB6 used magenta/black as the transparent color key. The Godot client processes
every loaded PNG:

```
BlackThreshold = 3
For each pixel: if (R <= 3 && G <= 3 && B <= 3) -> Alpha = 0
```

This handles both exact black `(0,0,0)` and JPEG compression artifacts where
near-black pixels (1-3) also need transparency. Applied via `ApplyBlackColorKeyFast()`.

PNGs that already have an alpha channel are also processed (some VB6 sprites
mix both mechanisms).

### Texture Addressing (WRAP mode)

VB6/D3D8 uses WRAP texture addressing: UV coordinates beyond 1.0 wrap around.
The client replicates this with modulo: `sx = sx % texW; sy = sy % texH`.

Example: A 32x32 texture with `SX=32` wraps to `SX=0`.

---

## 7. Water Animation Pattern

Water GRHs are **no longer hardcoded**. `WorldRenderer.IsWaterGrh()` reads the
ranges from `INIT/GrhCatalog.json`, which `grhtool special` fills in by finding
tiles that are both animated and blue — two independent signals, because a false
positive does more than look wrong: `InputHandler` uses this to decide whether a
tile can be walked on.

```csharp
public static bool IsWaterGrh(int g) => _catalog?.IsWater(g) ?? false;
```

Regenerate after any renumbering:

    grhtool special <ind> <GrhCatalog.json> <graficosDir> --apply

---

## 8. Key Constants

| Constant | Value | Source |
|----------|-------|--------|
| TileSize | 32 | WorldRenderer.cs, CharRenderer.cs |
| Water / tree GRHs | (data) | `INIT/GrhCatalog.json` — no longer hardcoded |
| MaxGRHs | 207,145 | Graficos.ind header |
| Version | 447 | Graficos.ind header |
| MiCabeceraSize | 263 | GrhLoader.cs |
| MaxCacheSize | 4,096 | TextureManager.cs |
| BlackThreshold | 3 | TextureManager.cs |

### Ceilings

| Limit | Where | Notes |
|-------|-------|-------|
| none | Personajes/Cabezas/Cascos.ind | **lifted** — see below |
| 32,767 | Fxs.ind `Animacion` | signed Int16 — animations are numbered 1..3103 to stay under it |
| 32,767 | legacy `.map` layers | signed Int16 (the reason those maps were archived) |

**The UInt16 cap was removed.** Personajes/Cabezas/Cascos originally stored GRH
numbers as UInt16, capping the catalogue at 65,535 — not enough to index 3094
sheets of terrain cell by cell. Nothing outside the client and `tools/` reads
these files (the server never opens them, and no GRH crosses the wire), so the
fields were widened to Int32.

Both layouts are still readable, told apart by a marker at offset 263 where the
legacy format keeps its Int16 count:

    legacy: MiCabecera(263) + Count(Int16)              + records with UInt16 GRHs
    wide:   MiCabecera(263) + Magic(-32000, Int32) + Count(Int32) + records with Int32 GRHs

Readers: `client/Scripts/Data/BodyLoader.cs`,
`tools/world-editor/Scripts/Data/GameDataLoader.cs`. Writer:
`tools/grhtool/EntityTables.cs`.

---

## 9. Common Pitfalls

1. **Forward references**: Animated GRHs may reference frames loaded later.
   Always use the two-pass approach.
2. **Sparse indices**: GRH array indices are not contiguous. Always bounds-check
   before accessing `Grhs[index]`.
3. **WRAP addressing**: Source rectangles can exceed texture dimensions. Apply
   modulo before clipping: `sx = sx % texW`.
4. **Color key on JPEGs**: Black threshold of 3 catches JPEG artifacts. Never
   use exact `(0,0,0)` comparison.
5. **Self-referencing frames**: Static GRHs have `Frames = [self]`. Code that
   iterates frames works uniformly for both static and animated GRHs.
6. **Speed=0 fallback**: If an animated GRH has `Speed=0`, use a default of
   `100f` ms to avoid division by zero.
