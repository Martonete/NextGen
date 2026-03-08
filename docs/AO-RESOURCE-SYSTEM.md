# Argentum Online — Complete Resource System Reference

This document describes every data format, rendering pipeline, and resource relationship
in the Argentum Online engine (VB6 original + Godot C# port + Rust server).

---

## Table of Contents

1. [Graphics System (Graficos.ind)](#1-graphics-system)
2. [Texture Files (Graficos/*.png)](#2-texture-files)
3. [Animation System (GrhAnimator)](#3-animation-system)
4. [Map Binary Format (.map / .inf / .dat)](#4-map-binary-format)
5. [Rendering Pipeline (4-Pass System)](#5-rendering-pipeline)
6. [Layer System (L1-L4)](#6-layer-system)
7. [Texture Catalog (indices.ini)](#7-texture-catalog)
8. [Character Rendering](#8-character-rendering)
9. [Light System](#9-light-system)
10. [Particle System](#10-particle-system)
11. [Trigger System](#11-trigger-system)
12. [Water Rendering](#12-water-rendering)
13. [Aura System](#13-aura-system)
14. [FX / Effects System](#14-fx-system)
15. [All Data Files Reference](#15-all-data-files)
16. [Constants & Limits](#16-constants)

---

## 1. Graphics System

### Graficos.ind — Binary GRH Database

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

## 2. Texture Files

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
- Pixels with R,G,B ≤ 3 → set alpha to 0 (handles JPEG compression artifacts)
- All other pixels → alpha = 255

### Texture Caching
- LRU cache: 256 textures (client), 512 (editor)
- Loaded on demand via `TextureManager.GetTexture(fileNum)`

---

## 3. Animation System

### Two Animation Modes

**A) Looping Tile Animations** (water, terrain, fire):
- Global clock (`_globalTimeMs`), reset on map change
- Frame = `(globalTime × numFrames / speed) % numFrames`
- All tiles with same GRH are perfectly synchronized

**B) One-Shot FX Animations** (spell hits, emoticons):
- Per-instance counter, advances independently
- Stops at last frame (no loop)
- Frame = `min(accumulated, numFrames - 1)`

### Water Slowdown
`GetCurrentFrameSlowed()` divides time by a factor for smoother water animation.

---

## 4. Map Binary Format

### .map File (Tile Graphics + Terrain)

**Header: 273 bytes**
```
Offset  Size  Field
0       2     MapVersion (i16 LE)
2       255   Description (ASCII, "Argentum Online by Noland Studios...")
257     4     CRC (i32 LE)
261     4     MagicWord (i32 LE)
265     8     Reserved (zeros)
```

**Tile Data: 100×100, row-major (Y=1→100, X=1→100)**

Each tile is variable-length:
```
ByFlags     1 byte      Bitfield controlling which fields follow
Layer1      i16 LE      ALWAYS present (ground terrain GRH index)
Layer2      i16 LE      If ByFlags & 0x02 (mask/overlay GRH)
Layer3      i16 LE      If ByFlags & 0x04 (objects/trees GRH)
Layer4      i16 LE      If ByFlags & 0x08 (roof GRH)
Trigger     i16 LE      If ByFlags & 0x10 (trigger type)
Particles   i16 LE      If ByFlags & 0x20 (particle group index)
LightRange  i16 LE      If ByFlags & 0x40 (light source range)
LightR      i16 LE      If ByFlags & 0x40 (light red 0-255)
LightG      i16 LE      If ByFlags & 0x40 (light green)
LightB      i16 LE      If ByFlags & 0x40 (light blue)
```

**ByFlags bits:**
| Bit | Mask | Field |
|-----|------|-------|
| 0   | 0x01 | Blocked (movement collision) |
| 1   | 0x02 | Has Layer2 |
| 2   | 0x04 | Has Layer3 |
| 3   | 0x08 | Has Layer4 |
| 4   | 0x10 | Has Trigger |
| 5   | 0x20 | Has ParticleGroup |
| 6   | 0x40 | Has Light source (range + RGB) |

**IMPORTANT**: Cannot seek to arbitrary tile — must read sequentially from header due to variable-length encoding.

### .inf File (Exits, NPCs, Objects)

**Header: 10 bytes** (5 × i16, all zeros — skip)

**Tile Data: 100×100, same row-major order**
```
ByFlags     1 byte
ExitMap     i16 LE      If ByFlags & 0x01 (destination map number)
ExitX       i16 LE      If ByFlags & 0x01 (destination X)
ExitY       i16 LE      If ByFlags & 0x01 (destination Y)
NpcIndex    i16 LE      If ByFlags & 0x02 (NPC spawn ID)
ObjIndex    i16 LE      If ByFlags & 0x04 (ground item ID)
ObjAmount   i16 LE      If ByFlags & 0x04 (item quantity)
```

### .dat File (Map Metadata — INI text)
```ini
[MapaN]
Name=Ciudad de Tanaris
MusicNum=5
Pk=0                    ; 0=PvP enabled, 1=safe (inverted!)
Terreno=BOSQUE          ; BOSQUE, NIEVE, DESIERTO, CIUDAD
Zona=CAMPO              ; CAMPO, CIUDAD, DUNGEON
BackUp=1
R=180                   ; Ambient red (0-255)
G=180                   ; Ambient green
B=180                   ; Ambient blue
MagiaSinEfecto=0
InviSinEfecto=0
ResuSinEfecto=0
NoEncriptarMP=0
Restringir=No
```

---

## 5. Rendering Pipeline

The client uses a multi-pass rendering system matching VB6's `RenderScreen`:

### Pass 1: Water (L1 water tiles only)
- Draws Layer1 tiles with GRH indices **1505-1520** (water graphics)
- Non-water L1 drawn in Pass 1b to avoid double-draw

### Pass 1.5: Water Reflections
- Characters on tiles adjacent to water get Y-flipped reflection
- Detection: checks tiles Y+1 to Y+5 for water
- Drawn BEFORE the non-water mask (so mask clips reflections on land)

### Pass 1b: Non-Water L1 Mask
- Redraws all Layer1 tiles that are NOT water (GRH < 1505 or > 1520)
- Clips reflection overflow onto land areas
- Uses lightmap shader for ambient tinting

### Pass 2: Layer 2 (Overlay/Mask)
- Draws Layer2 tiles (borders, paths, water edges, detail overlays)
- Alpha transparency blending
- Lightmap shader applied

### Pass 3: Content Layer (Objects + Characters + L3)
- **Ground objects**: Centered, tree transparency near player (47% opacity)
- **Characters/NPCs**: Full composite (body + head + weapon + shield + helmet + aura + FX)
- **Layer 3**: Objects/trees above characters, centered, tree transparency
- Draw order: Y-sorted (top to bottom) for proper depth

### Additive Particle Layer (z=2)
- Map-attached and character-attached particles
- Additive blend mode (brightens, never darkens)

### Pass 4: Roof (L4 with fade)
- Draws Layer4 tiles (roofs/ceilings)
- **Roof fade**: When player is under roof (trigger 1/2/4), alpha decreases at 6.0/frame
- Range: 0-255, transition takes ~0.7 seconds at 60 FPS
- Lightmap shader applied

---

## 6. Layer System

| Layer | Name | Purpose | Centered | Alpha | Special |
|-------|------|---------|----------|-------|---------|
| L1 | Ground | Terrain, water, floor | No | 1.0 | Water (1505-1520) separate pass |
| L2 | Mask | Borders, paths, shadows, water edges | No | 1.0 | Overlays L1, clips reflections |
| L3 | Objects | Trees, furniture, decorations | Yes | 1.0 or 0.47 | Transparent near player if tree |
| L4 | Roof | Ceilings, rooftops | Yes | 0-1.0 | Fades when player under roof |

### Layer 2 (Mask) — What Makes It Special
Layer2 is NOT just "another ground layer". It serves as:
- **Border blending**: Smooth transitions between terrain types (grass→sand, grass→water)
- **Path overlay**: Roads, trails overlaid on base terrain
- **Water edge**: Shoreline graphics where water meets land
- **Shadow/detail**: Ground shadows from objects, decorative details
- **Reflection clipper**: Non-water L1 mask prevents reflections on land

### Layer 3 — Multi-Tile Objects
Objects taller than 1 tile (trees, large furniture) use `center=true`:
```csharp
drawX += (TileSize - grh.PixelWidth) / 2f;   // Center horizontally
drawY += (TileSize - grh.PixelHeight);        // Anchor at bottom
```

### Layer 4 — Roof Fade Logic
```
Player on trigger 1/2/4 → _roofAlpha -= 6.0/frame → clamp to 0
Player elsewhere       → _roofAlpha += 6.0/frame → clamp to 255
Draw L4 with Color(1, 1, 1, _roofAlpha/255)
```

---

## 7. Texture Catalog (indices.ini)

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
| (MONTAÑA) | Montaña (mountain) | ~5 |
| (LAVA) | Lava | ~5 |
| (INFERNO) | Inferno | ~10 |
| (CLOACA) | Cloaca (sewers) | ~9 |

### Why Some Textures Don't Load
- 644 declared but ~496 actually loaded: some REFERENCIA sections are missing from the file (gaps in numbering)
- The loader iterates `refCount + 10` beyond declared count as safety margin
- All loaded GrhIndice values are within valid range (max ~27,912 < 32,824 GRHs)

---

## 8. Character Rendering

### Composition (5 layers per character)
A character is NOT a single sprite — it's composited from:
1. **Body** (Personajes.ind): Walk animation per direction, defines head offset
2. **Head** (Cabezas.ind): 4 directional sprites
3. **Helmet** (Cascos.ind): Optional, overlays head
4. **Weapon** (Armas.dat): Walk animation per direction
5. **Shield** (Escudos.dat): Walk animation per direction

### Draw Order Per Heading
```
North (1): Weapon → Shield → Body → Head
East  (2): Shield → Body → Head → Weapon
South (3): Body → Head → Weapon → Shield
West  (4): Weapon → Body → Head → Shield
```

### Data Files

**Personajes.ind** (Binary, MiCabecera + entries):
```
Per body (12 bytes):
  Walk[1]       i16     North walk animation GRH
  Walk[2]       i16     East
  Walk[3]       i16     South
  Walk[4]       i16     West
  HeadOffsetX   i16     Head X position relative to body
  HeadOffsetY   i16     Head Y position (typically -38)
```
Count: 53 body types.

**Cabezas.ind / Cascos.ind** (Binary, MiCabecera + entries):
```
Per head (8 bytes):
  Head[1]       i16     North GRH
  Head[2]       i16     East
  Head[3]       i16     South
  Head[4]       i16     West
```
Count: 400 heads, 45 helmets.

**Armas.dat / Escudos.dat** (INI text):
```ini
[INIT]
NumArmas=83

[Arma1]
Dir1=4719       ; North GRH
Dir2=4721       ; East
Dir3=4718       ; South
Dir4=4720       ; West
```
Count: 83 weapons, 46 shields.

### Dead Characters
- Ghost head: GRH 500/501/511/512 (directional)
- Body: 8 (skeleton/ghost)
- Transparency pulse: alpha oscillates 0-100

---

## 9. Light System

### Per-Tile Corner Colors
Each tile has 4 corner light values (matching VB6 per-vertex lighting):
```
Corner 0: SW (tx×32, ty×32+32)
Corner 1: NW (tx×32, ty×32)
Corner 2: SE (tx×32+32, ty×32+32)
Corner 3: NE (tx×32+32, ty×32)
```

### Light Calculation (VB6 algorithm)
```
For each light source:
  For each visible tile corner:
    distance = euclidean(light_center, corner_pixel)
    if distance <= range:
      corner_color = lerp(light_color, ambient_color, distance/range)
      final = MAX(existing_corner, calculated)   // lights only brighten
```

### GPU Lightmap Shader (Godot port)
- 101×101 pixel Image (one pixel per tile corner)
- Bilinear filtering provides smooth interpolation = identical to VB6 vertex colors
- Rebuilt only when `LightsDirty` flag set
- Applied to terrain layers (L1 mask, L2, L4) via ShaderMaterial
- Content layer (chars/NPCs) draws at full brightness with inverse modulate

### Map Ambient Colors
- Exterior maps: RGB 180,180,180
- Dungeons: Preserved per-map (darker values like 30,30,30)
- Sent to client in ChangeMap packet

---

## 10. Particle System

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

## 11. Trigger System

| Value | Name | Effect | Rendering |
|-------|------|--------|-----------|
| 0 | None | Default | Normal |
| 1 | Indoor | Under roof | Roof (L4) fades to transparent |
| 2 | Reserved | Unused | Roof fades |
| 3 | InvalidPos | NPCs can't walk | No visual |
| 4 | SafeZone | No theft/combat | Roof fades |
| 5 | AntiBlock | Anti-picketing | No visual |
| 6 | CombatZone | Items don't drop | No visual |
| 7 | NoElevation | Prevents flight | No visual |

Triggers 1, 2, 4 activate the roof fade system (L4 alpha → 0).

---

## 12. Water Rendering

### Water Detection
```csharp
bool IsWater(tile) = (tile.Layer1 >= 1505 && tile.Layer1 <= 1520) && tile.Layer2 <= 0;
```
Water tiles are GRH indices 1505-1520 with NO Layer2 overlay.

### Rendering Specifics
1. Water drawn in **separate Pass 1** (before everything)
2. Reflections rendered in **Pass 1.5** (Y-flipped characters)
3. Non-water L1 mask in **Pass 1b** clips reflections on land
4. Water animation uses `GetCurrentFrameSlowed()` for smooth movement

### Reflection System
- Check tiles Y+1 to Y+5 below character for water
- Range: ±2 tiles horizontal (±3 if mounted)
- Y-flipped sprite at half-brightness
- Auras reflected separately (additive blend layer)

---

## 13. Aura System

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

## 14. FX System

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
- Emoticons (displayed above character head)
- One-shot animations (play once, stop at last frame)

---

## 15. All Data Files Reference

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
- Sizes range from 32×32 to 2048×2048
- Total: ~90 MB

### Server Data (server/dat/)

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
| Cofres.dat | 4.5K | — | Treasure chests |
| QUESTS.DAT | 3.4K | — | Quest definitions |
| AreasStats.dat | 20K | — | Zone loot tables |

### Server Maps (server/maps/)
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

## 16. Constants & Limits

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
