# Asset System

## Graphics System (Graficos.ind)

### Binary GRH Database

**Location**: `client/Data/INIT/Graficos.ind` (532 KB, ~32,824 entries)

### Header Detection (auto-detect 3 formats)

The loader tries:
1. **Without MiCabecera**: offset 0 → Version(i32) + Count(i32)
2. **With MiCabecera**: skip 263 bytes → Version(i32) + Count(i32)
3. **Fallback**: skip 1 flag byte → Version(i32) + Count(i32)

Actual file: Version=447, Count=32,824.

### Entry Formats

**Static GRH** (NumFrames = 1):
```
GrhIndex    i32     Which GRH number
NumFrames   i16     = 1 (static)
FileNum     i32     PNG file number (e.g., 1 → "1.png")
SX          i16     Source X pixel in texture
SY          i16     Source Y pixel in texture
PixelWidth  i16     Sprite width in pixels
PixelHeight i16     Sprite height in pixels
--- Total: 18 bytes ---
```

**Animated GRH** (NumFrames > 1):
```
GrhIndex    i32     Which GRH number
NumFrames   i16     > 1 (animation)
Frames[N]   i32×N   Frame GRH indices (each points to a static GRH)
Speed       f32     Animation speed (ms per full cycle)
--- Total: 10 + (4 × NumFrames) bytes ---
```

**Computed fields** (at load time):
```
TileWidth  = PixelWidth / 32.0    (1.0 = one 32px tile)
TileHeight = PixelHeight / 32.0
```

For animations, dimensions are inherited from the first frame's static GRH.

### VB6 GrhData Structure
```vb6
Type GrhData
    sX As Integer               ' Source X in texture
    sY As Integer               ' Source Y in texture
    FileNum As Long             ' PNG/BMP file number
    pixelWidth As Integer       ' Sprite width
    pixelHeight As Integer      ' Sprite height
    TileWidth As Single         ' Width in tiles (pixels/32)
    TileHeight As Single        ' Height in tiles (pixels/32)
    NumFrames As Integer        ' 1=static, >1=animated
    Frames() As Long            ' Frame GRH indices
    Speed As Single             ' Animation speed
End Type
```

---

## Texture Files

**Directory**: `client/Data/Graficos/` (3,291 PNG files, ~90 MB total)

### Naming Convention
```
FileNum → "{FileNum}.png"
Example: FileNum=1 → "1.png", FileNum=10019 → "10019.png"
```

### Sprite Sheet Layout
Each PNG contains one or more sprites. A GRH entry defines a rectangular region:
```
Texture "5.png" (256×256):
  GRH 100: SX=0,   SY=0,   W=32, H=32  (top-left tile)
  GRH 101: SX=32,  SY=0,   W=32, H=32  (next tile)
  GRH 102: SX=0,   SY=32,  W=64, H=96  (large sprite)
```

### Color Key Transparency
AO uses black (0,0,0) as the transparency color:
- Pixels with R,G,B <= 3 → set alpha to 0 (handles JPEG compression artifacts)
- All other pixels → alpha = 255

### Texture Caching
- LRU cache: 256 textures (client), 512 (editor)
- Loaded on demand via `TextureManager.GetTexture(fileNum)`

---

## Animation System

### Two Animation Modes

**A) Looping Tile Animations** (water, terrain, fire):
- Global clock (`_globalTimeMs`), reset on map change
- Frame = `(globalTime × numFrames / speed) % numFrames`
- All tiles with same GRH are perfectly synchronized

**B) One-Shot FX Animations** (spell hits, effects):
- Per-instance counter, advances independently
- Stops at last frame (no loop)
- Frame = `min(accumulated, numFrames - 1)`

### Water Slowdown
`GetCurrentFrameSlowed()` divides time by a factor for smoother water animation.

---

## Texture Catalog (indices.ini)

**Location**: `client/Data/INIT/indices.ini` (35 KB, 644 declared references, ~496 loaded)

### Format
```ini
[INIT]
Referencias=644

[REFERENCIA0]
Nombre=(PRD) Terreno        ; Display name with category prefix
GrhIndice=6000              ; Starting GRH index
Ancho=4                     ; Pattern width in tiles (0=single)
Alto=4                      ; Pattern height in tiles (0=single)
```

### Multi-Tile Patterns
A reference with Ancho=4, Alto=4 starting at GrhIndice=6000 maps to 16 consecutive GRHs:
```
Row-major layout:
  (0,0)=6000  (1,0)=6001  (2,0)=6002  (3,0)=6003
  (0,1)=6004  (1,1)=6005  (2,1)=6006  (3,1)=6007
  (0,2)=6008  (1,2)=6009  (2,2)=6010  (3,2)=6011
  (0,3)=6012  (1,3)=6013  (2,3)=6014  (3,3)=6015

Formula: GrhAt(px, py) = GrhIndice + (py × Ancho) + px
```

### Category Prefixes
| Prefix | Category | Count |
|--------|----------|-------|
| (PRD) | Pradera (grassland) | ~57 |
| (ROCA) | Rocas (rocks) | ~30 |
| (CASA) | Casas (houses) | ~15 |
| (NIEVE) | Nieve (snow) | ~12 |
| (DESIERTO) | Desierto (desert) | ~10 |
| (AGUA) | Agua (water) | ~8 |
| (PISO)/(PIDO) | Pisos (floors) | ~100 |
| (TECHO) | Techos (roofs) | ~20 |
| (PARED) | Paredes (walls) | ~15 |
| (DUNGEON) | Dungeon | ~10 |
| (ARBOL) | Arboles (trees) | ~8 |
| (MONTANA) | Montana (mountain) | ~5 |
| (LAVA) | Lava | ~5 |
| (INFERNO) | Inferno | ~10 |
| (CLOACA) | Cloaca (sewers) | ~9 |

### Why Some Textures Don't Load
- 644 declared but ~496 actually loaded: some REFERENCIA sections are missing from the file (gaps in numbering)
- The loader iterates `refCount + 10` beyond declared count as safety margin
- All loaded GrhIndice values are within valid range (max ~27,912 < 32,824 GRHs)

---

## Particle System

**File**: `client/Data/INIT/Particles.ini` (47 KB, 105 effects)

### Definition Format
```ini
[1]
Name=Fountain
NumOfParticles=20           ; Particles per emission
X1=0 Y1=0 X2=0 Y2=0       ; Spawn area bounds
Angle=0                     ; Emission angle
VecX1=-20 VecX2=20          ; Velocity X range
VecY1=-10 VecY2=-50         ; Velocity Y range
Life1=10 Life2=50           ; Lifetime range
Friction=8                  ; Velocity damping
Gravity=1                   ; Apply gravity?
Grav_Strength=2             ; Gravity acceleration
Bounce_Strength=-5          ; Bounce restitution
Spin=1                      ; Rotation enabled
AlphaBlend=1                ; Alpha transparency
Speed=0.5                   ; Speed multiplier
NumGrhs=1                   ; Number of GRH variations
Grh_List=27452,             ; GRH indices (comma-separated)
ColorSet1=255,0,0           ; Color 1 RGB
ColorSet2=255,171,168       ; Color 2 RGB
ColorSet3=255,0,0           ; Color 3 RGB
ColorSet4=255,190,190       ; Color 4 RGB
```

### Rendering
- Map particles: drawn by AdditiveParticleLayer (additive blend, z=2)
- Character particles: drawn in ContentLayer via CharRenderer
- Both use additive blending (brightens only)

---

## Aura System

**File**: `client/Data/INIT/Auras.dat` (11 KB, 96 auras)

### Format
```ini
[AURA1]
GrhIndex=27573      ; Animation GRH
Rojo=255            ; Base red (0-255)
Verde=0             ; Base green
Azul=0              ; Base blue
RojoF=0             ; Pulse start red
VerdeF=0            ; Pulse start green
AzulF=0             ; Pulse start blue
Giratoria=1         ; Rotates? (0/1)
offset=0            ; Y-axis offset from head
```

### Rendering
- Position: `characterPos + headOffset + (0, 72 - aura.Offset)`
- Additive blend (D3DBLEND_ONE/ONE in VB6, CanvasItemMaterial.Add in Godot)
- Rotation: 0.004 radians/frame for `Giratoria=1`
- Examples: Red aura, Blue protective, Wings, Rays, Galaxy

---

## FX System

**File**: `client/Data/INIT/Fxs.ind` (1.1 KB, binary)

### Format
```
MiCabecera (263 bytes)
Count       i16
Per FX (6 bytes):
  Animacion   i16     GRH animation index
  OffsetX     i16     X offset from character head
  OffsetY     i16     Y offset from character head
```

### Usage
- Spell impact effects, combat hit flashes
- One-shot animations (play once, stop at last frame)

---

## All Data Files Reference

### Client Data (client/Data/INIT/)

| File | Size | Format | Entries | Purpose |
|------|------|--------|---------|---------|
| Graficos.ind | 532K | Binary | 32,824 | All graphics/animations |
| indices.ini | 35K | INI | 644 | Tile texture catalog |
| Personajes.ind | 6.3K | Binary | 53 | Body walk animations |
| Cabezas.ind | 8.4K | Binary | 400 | Head sprites |
| Cascos.ind | 2.1K | Binary | 45 | Helmet sprites |
| Armas.dat | 4.3K | INI | 83 | Weapon animations |
| Escudos.dat | 2.4K | INI | 46 | Shield animations |
| Fxs.ind | 1.1K | Binary | ~20 | Combat/spell effects |
| Auras.dat | 11K | INI | 96 | Aura effects |
| Particles.ini | 47K | INI | 105 | Particle definitions |
| Textos.ao | 73K | INI | 983 | UI message strings |

### Client Textures (client/Data/Graficos/)
- 3,291 PNG files, naming: `{FileNum}.png`
- Sizes range from 32x32 to 2048x2048
- Total: ~90 MB

### Server Data (resources/data/dats/)

| File | Size | Entries | Purpose |
|------|------|---------|---------|
| NPCs.dat | 57K | 206 | Normal NPCs |
| NPCs-HOSTILES.dat | 59K | 400+ | Hostile monsters |
| Obj.dat | 600K | 1,664 | Items/objects (38 types) |
| Hechizos.dat | 34K | 65 | Spells |
| Balance.dat | 2.8K | 9 classes | Combat balance |
| Body.dat | 4.9K | 53 | Body metadata |
| Head.dat | 161B | 400 | Head metadata |
| Experiencia.dat | 967B | 99 levels | XP table |
| Cofres.dat | 4.5K | -- | Treasure chests |
| QUESTS.DAT | 3.4K | -- | Quest definitions |
| AreasStats.dat | 20K | -- | Zone loot tables |

### Server Maps (resources/data/Maps/)
- 194 maps with .map + .inf + .dat triplets
- Client Maps dir has .map files but few .inf (only 3)

### Binary Header: MiCabecera (263 bytes)
Used by: Graficos.ind, Personajes.ind, Cabezas.ind, Cascos.ind, Fxs.ind
```
255 bytes   Description string (ASCII)
4 bytes     CRC (i32 LE)
4 bytes     MagicWord (i32 LE)
```

---

## Constants & Limits

| Constant | Value | Purpose |
|----------|-------|---------|
| TileSize | 32 px | Standard tile dimension |
| MapWidth/Height | 100 tiles | Map dimensions (1-indexed) |
| ViewportWidth | 534 px | VB6 game viewport |
| ViewportHeight | 408 px | VB6 game viewport |
| HalfWindowTileW | 8 | Visible range (534/32/2) |
| HalfWindowTileH | 6 | Visible range (408/32/2) |
| BlackThreshold | 3 | Color key near-black tolerance |
| TextureCacheClient | 256 | LRU cache limit (client) |
| TextureCacheEditor | 512 | LRU cache limit (editor) |
| MiCabeceraSize | 263 bytes | VB6 binary header |
| MapHeaderSize | 273 bytes | .map file header |
| InfHeaderSize | 10 bytes | .inf file header |
| WaterGrhMin | 1505 | First water GRH index |
| WaterGrhMax | 1520 | Last water GRH index |
| MaxParticles | 105 | Particle definitions |
| MaxAuras | 96 | Aura definitions |
| MaxGRHs | ~32,824 | Total GRH entries |
| ScrollPixelsPerFrame | 8 | Movement smoothing |
| EngineBaseSpeed | 0.0172 | Movement timing base |
| RoofFadeSpeed | 6.0/frame | Roof alpha change rate |

---

## Data Flow Summary

```
MAP FILE (.map)
  ├── Layer1 GRH index ──→ Graficos.ind ──→ FileNum ──→ Graficos/{N}.png
  ├── Layer2 GRH index ──→ Graficos.ind ──→ ...
  ├── Layer3 GRH index ──→ Graficos.ind ──→ ...
  ├── Layer4 GRH index ──→ Graficos.ind ──→ ...
  ├── Trigger ──→ Roof fade / zone behavior
  ├── Particles ──→ Particles.ini ──→ GRH list ──→ Graficos.ind
  └── Light ──→ LightSystem ──→ Lightmap texture ──→ GPU shader

INF FILE (.inf)
  ├── Exits ──→ Map transitions (dest map/x/y)
  ├── NPC spawns ──→ NPCs.dat ──→ Body/Head/Weapon ──→ Graficos.ind
  └── Objects ──→ Obj.dat ──→ GrhIndex ──→ Graficos.ind

CHARACTER (CC packet)
  ├── Body ID ──→ Personajes.ind ──→ Walk[heading] ──→ Graficos.ind
  ├── Head ID ──→ Cabezas.ind ──→ Head[heading] ──→ Graficos.ind
  ├── Weapon ID ──→ Armas.dat ──→ Dir[heading] ──→ Graficos.ind
  ├── Shield ID ──→ Escudos.dat ──→ Dir[heading] ──→ Graficos.ind
  ├── Helmet ID ──→ Cascos.ind ──→ Head[heading] ──→ Graficos.ind
  └── Aura ID ──→ Auras.dat ──→ GrhIndex ──→ Graficos.ind
```
