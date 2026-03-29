# Rendering Pipeline

## Rendering Pipeline (4-Pass System)

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

## Layer System

| Layer | Name | Purpose | Centered | Alpha | Special |
|-------|------|---------|----------|-------|---------|
| L1 | Ground | Terrain, water, floor | No | 1.0 | Water (1505-1520) separate pass |
| L2 | Mask | Borders, paths, shadows, water edges | No | 1.0 | Overlays L1, clips reflections |
| L3 | Objects | Trees, furniture, decorations | Yes | 1.0 or 0.47 | Transparent near player if tree |
| L4 | Roof | Ceilings, rooftops | Yes | 0-1.0 | Fades when player under roof |

### Layer 2 (Mask) — What Makes It Special
Layer2 is NOT just "another ground layer". It serves as:
- **Border blending**: Smooth transitions between terrain types (grass->sand, grass->water)
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

## Character Rendering

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

## Light System

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

## Trigger System

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

## Water Rendering

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
- Range: +/-2 tiles horizontal (+/-3 if mounted)
- Y-flipped sprite at half-brightness
- Auras reflected separately (additive blend layer)
