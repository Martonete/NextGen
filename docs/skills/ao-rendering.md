---
name: ao-rendering
description: Use when modifying the 6-pass rendering pipeline, character draw order, GPU lightmap, water reflections, roof fade, or particle layers in the Godot client.
---

# AO Rendering Pipeline Reference

Source: `client/Scripts/Rendering/WorldRenderer.cs`, `CharRenderer.cs`

Source: `client/Scripts/Rendering/WorldRenderer.cs`, `client/Scripts/Rendering/CharRenderer.cs`,
`client/Scripts/Rendering/CharRenderer.Drawing.cs`

---

## 1. Render Pass Architecture

WorldRenderer uses child Node2D layers, each with specific z-index and blend mode.
Parent `_Draw()` runs first, then children draw in z/tree order.

| Pass | Layer Node | Z-Index | Blend | What It Draws |
|------|-----------|---------|-------|---------------|
| 1 | WorldRenderer._Draw() | 0 | Normal | Water tiles only (GRH 1505-1520) |
| 1.5 | ReflectionBodyLayer | 0 | Normal | Character reflections on water (Y-flipped) |
| 1.5a | ReflectedAuraLayer | 0 | Additive | Reflected auras (behind reflected body) |
| 1b | NonWaterMaskLayer | 0 | Normal+Shader | Non-water L1 tiles (covers reflection overflow) |
| 2 | Layer2Layer | 0 | Normal+Shader | Layer 2 tiles (borders, transitions, coverage) |
| 2.5 | AuraAdditiveLayer | 0 | Additive | Normal character auras |
| 3 | ContentLayer | 0 | Normal | Ground objects + characters + Layer 3 (Y-sorted) |
| 3.5 | DialogOverlayLayer | 1 | Normal | Dialog text above characters |
| 4 | AdditiveParticleLayer | 2 | Additive | Map + character particles |
| 5 | RoofLayer | 3 | Normal+Shader | Roof tiles (Layer 4) with alpha fade |
| 6 | FloatingTextLayer | 4 | Normal | Damage/heal floating numbers |
| 7 | WeatherRenderer | 5 | Normal | Rain, lightning overlays |

**Draw order within same z-index**: Determined by child tree order. Layers added
earlier in `Init()` draw first.

---

## 2. Pass 1: Water Tiles

Only tiles with `Layer1` in water GRH ranges are drawn:
```
if (tile.Layer1 < 1505 || tile.Layer1 > 1520) continue; // skip non-water
```

Bounds: `frameL1MinX..frameL1MaxX` / `frameL1MinY..frameL1MaxY` (visible + 2 margin).

Non-water L1 tiles are NOT drawn here. They are drawn once by NonWaterMaskLayer
(Pass 1b), avoiding the double-draw that previously killed FPS.

---

## 3. Pass 1.5: Water Reflections

For each visible character (not invisible), check if any tile below (Y+1 to Y+5)
and within horizontal range has water:

- **Horizontal check range**: +/- 2 tiles (or +/- 3 if mounted)
- **Vertical check range**: Y+1 to min(100, Y+5)
- Water defined as L1 GRH 1505-1520

If water found, the character is queued for reflection:
1. **Reflected auras** -> `ReflectedAuraLayer` (additive blend, behind body)
2. **Reflected body + FX** -> `ReflectionBodyLayer` (Y-flipped via DrawSetTransform)

The NonWaterMaskLayer (Pass 1b) then masks reflections to water-only areas.

---

## 4. Pass 2: Layer 2

Layer 2 tiles are drawn with the lightmap shader applied. These are border
transitions, cliff edges, and terrain overlays that occlude reflections at edges.

---

## 5. Pass 3: Content (Y-Sorted Depth)

Ground objects, characters, and Layer 3 tiles are drawn Y-sorted for proper
depth ordering. Characters render their full composition here (see Section 7).

---

## 6. Pass 4: Roof Fade

Roof tiles (Layer 4) fade based on player trigger position:

```csharp
short trigger = mapData.Tiles[userX, userY].Trigger;
bool underRoof = trigger == 1 || trigger == 2 || trigger == 4;

if (underRoof)
    _roofAlpha -= RoofFadeRate;  // 6.0 per frame, clamp to 0
else
    _roofAlpha += RoofFadeRate;  // 6.0 per frame, clamp to 255
```

Roof draw color: `new Color(1, 1, 1, _roofAlpha / 255f)`.

---

## 7. Character Rendering (CharRenderer)

### Heading-Dependent Draw Order

VB6 `dibujarPersonaje()` changes equipment draw order per heading to ensure
correct visual layering:

| Heading | Direction | Draw Order (back to front) |
|---------|-----------|---------------------------|
| 1 | North | Weapon -> Shield -> Body -> Head -> Helmet |
| 2 | East | Shield -> Body -> Head -> Helmet -> Weapon |
| 3 | South | Body -> Head -> Helmet -> Weapon -> Shield |
| 4 | West | Weapon -> Body -> Head -> Helmet -> Shield |

### Character Components

Each character is composed of multiple sprites:

1. **Shadow** — parallelogram projection from body feet
2. **Body** — walking animation (`Bodies[ch.Body].Walk[heading]`)
3. **Head** — static per heading (`Heads[ch.Head].Head[heading]`)
4. **Helmet** — at head position + OFFSET_HEAD=-34 (`Cascos[ch.CascoAnim].Head[heading]`)
5. **Weapon** — walk animation synced with body (`Weapons[ch.WeaponAnim].Walk[heading]`)
6. **Shield** — walk animation synced with body (`Shields[ch.ShieldAnim].Walk[heading]`)
7. **Auras** — additive blend, separate layer (see Section 9)
8. **FX overlays** — animation cycling with loop count
9. **Particles** — character-attached, additive blend layer
10. **Name/Clan** — bitmap font text above head
11. **Dialog bubble** — timed text, queued to overlay layer

### Head Positioning

```
headPos = bodyPos + (HeadOffsetX + (mounted ? 1 : 0), HeadOffsetY + 1)
helmetPos = bodyPos + (HeadOffsetX + (mounted ? 1 : 0), HeadOffsetY + OFFSET_HEAD)
```

Where `HeadOffsetX/Y` comes from `Bodies[ch.Body]`, and `OFFSET_HEAD = -34`.

---

## 8. Shadow Rendering

Diagonal projection: light from lower-left, shadow toward upper-right.

**Constants**:
- ShearRatio = 0.3 (per pixel above feet, shift right by 0.3px)
- FlatRatio = 0.85 (per pixel above feet, compress to 0.85px)
- Shadow color: `(0, 0, 0, 0.35)`

**Projection formula** (per corner at `(px, py)`):
```
dy = feetY - py                    // distance above feet
shadowX = px + dy * ShearRatio
shadowY = feetY - dy * FlatRatio
```

All parts (body, head, helmet, weapon, shield) project from the same anchor
point (body feet) for a coherent silhouette.

Drawn as a textured polygon (DrawPolygon) with UV mapping.

---

## 9. Aura System

Auras use **additive blend** (D3DBLEND_ONE/ONE) and are drawn on dedicated layers.

### Aura Slots Per Character

- AuraIndexA, AuraIndexW, AuraIndexE, AuraIndexR, AuraIndexC, NpcAura
- Each has its own rotation angle state

### Positioning

```
auraX = charPos.X + headOffset.X
auraY = charPos.Y + headOffset.Y + 72 - aura.Offset
```

### Rotation

If `aura.Giratoria == true`:
```
angle = globalTimeMs * 0.000096 % 180.0
```
This gives ~0.096 rad/sec rotation (FPS-independent via absolute time).

### Aura Color

```
Color(aura.R/255, aura.G/255, aura.B/255, alphaOverride)
```
`alphaOverride` is reduced when character is invisible (pulses with body).

---

## 10. GPU Lightmap

A 101x101 texture (one pixel per tile corner) with bilinear filtering for
smooth per-vertex lighting identical to VB6.

### Shader

```glsl
uniform sampler2D lightmap : filter_linear, repeat_disable;
uniform vec2 world_origin;
uniform bool use_lightmap;

void fragment() {
    if (use_lightmap) {
        vec2 uv = (world_px - 32.0) / 3200.0;
        vec3 light = texture(lightmap, uv).rgb;
        COLOR.rgb *= light;
    }
}
```

### Update Trigger

- Rebuilt only when `_lightmapDirty` flag is set (via `MarkLightmapDirty()`)
- Applied to: WorldRenderer (L1), NonWaterMaskLayer, Layer2Layer, RoofLayer
- NOT applied to ContentLayer (characters use ambient modulate instead)

### Ambient Light Integration

When lights are active: parent `Modulate = White`, shader handles all tinting.
When no lights: parent `Modulate = map ambient RGB`, ContentLayer gets inverse
SelfModulate so characters render at full brightness.

---

## 11. Dead / Invisible Characters

### Dead Characters

- Body alpha: configurable via `Config.DeadTransparencyAlpha` (20-100%, default 47%)
- Name alpha: 80/255

### Invisible Characters (Self Only)

- Pulsing transparency: `TransparenciaBody` oscillates between 18 and 53
- Speed: `deltaMs * 0.035` per frame
- Alpha range: ~45/255 to ~135/255
- Auras pulse with the same alpha
- FX overlays are NOT drawn when invisible
- Name IS drawn for invisible self

Other players' invisible characters are completely skipped.

---

## 12. FX Overlay System

3 FX slots per character (`ch.ActiveFxSlots[0..2]`):

- Resolved from `data.Fxs[fxIdx]` -> GRH animation + offset
- Per-slot frame counter: `ch.FxFrameCounter[i]`
- Loop count: `ch.FxLoops[i]` (-1 = infinite, >0 = N loops remaining)
- Draw alpha: `150/255` (semi-transparent overlay)
- Position: `screenPos + (fx.OffsetX, fx.OffsetY)`

---

## 13. Key Constants

| Constant | Value | Source |
|----------|-------|--------|
| TileSize | 32 | WorldRenderer.cs |
| ViewportWidth | 544 | WorldRenderer.cs |
| ViewportHeight | 416 | WorldRenderer.cs |
| HalfWindowTileWidth | 8 | WorldRenderer.cs |
| HalfWindowTileHeight | 6 | WorldRenderer.cs |
| TileBufferSize | 9 | WorldRenderer.cs |
| RoofFadeRate | 6.0 | WorldRenderer.cs |
| OFFSET_HEAD | -34 | CharRenderer.cs |
| ShadowShearRatio | 0.3 | CharRenderer.cs |
| ShadowFlatRatio | 0.85 | CharRenderer.cs |
| ShadowAlpha | 0.35 | CharRenderer.cs |
| FxDrawAlpha | 150/255 | CharRenderer.cs |
| LightmapSize | 101x101 | WorldRenderer.cs |

---

## 14. Coordinate System

### Tile to Screen Conversion

```csharp
float px = (tileX - userX + HalfWindowTileWidth) * TileSize + pixelOffsetX;
float py = (tileY - userY + HalfWindowTileHeight) * TileSize + pixelOffsetY;
```

- `pixelOffsetX/Y` = `-ScreenOffsetX/Y` (negated because tiles shift opposite
  to movement direction)
- Map bounds: tiles 1..100 (inclusive)
- `AddToUserPosX/Y` adjusts the effective camera center during movement
