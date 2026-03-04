# Argentum Online 13.3 (Oficial) — Sistema de Renderizado DX8

Referencia técnica del pipeline de renderizado Direct3D 8 de la versión oficial 13.3.
Basado en análisis del código fuente en `13.3-argentum/client/CODIGO/`.

## Índice

1. [Arquitectura General](#1-arquitectura-general)
2. [Game Loop y Timing](#2-game-loop-y-timing)
3. [Pipeline de Render (5 Pasadas)](#3-pipeline-de-render-5-pasadas)
4. [Renderizado de Personajes](#4-renderizado-de-personajes)
5. [Sistema de Texturas](#5-sistema-de-texturas)
6. [Iluminación y Sombras](#6-iluminación-y-sombras)
7. [Ciclo Día/Noche](#7-ciclo-díanoche)
8. [Efectos Visuales (FX)](#8-efectos-visuales-fx)
9. [Sistema de Partículas](#9-sistema-de-partículas)
10. [Proyectiles](#10-proyectiles)
11. [Agua y Reflejos](#11-agua-y-reflejos)
12. [Weather (Niebla)](#12-weather-niebla)
13. [Texto y Diálogos](#13-texto-y-diálogos)
14. [Alpha Blending y Blend Modes](#14-alpha-blending-y-blend-modes)
15. [Zoom](#15-zoom)
16. [Terreno con Elevación](#16-terreno-con-elevación)
17. [Optimizaciones](#17-optimizaciones)

---

## 1. Arquitectura General

La 13.3 contiene **dos renderers en paralelo**: un legacy DirectDraw7 (`TileEngine.bas`) y el activo **Direct3D8** (`modEngine.bas` + `modDx8graphics.bas` + `modDx8_HDC.bas`). Solo el DX8 está activo.

### Device Setup (`modDx8graphics.bas:43-102`)

```
Windowed mode, Software vertex processing
BackBuffer: 800x600, D3DFMT_X8R8G8B8
SwapEffect: D3DSWAPEFFECT_COPY (no VSync)
Z-Buffer: Disabled (painter's algorithm)
Texture filtering: D3DTEXF_NONE (pixel-perfect)
Texture addressing: D3DTADDRESS_MIRROR
```

### Vertex Format (`modEngine.bas:26`)

```
FVF = D3DFVF_XYZRHW | D3DFVF_TEX1 | D3DFVF_DIFFUSE | D3DFVF_SPECULAR
```

Pre-transformed 2D vertices: X, Y, Z, RHW, Color, Specular, TU, TV (32 bytes/vértice).

### Viewport

| Propiedad | Valor |
|-----------|-------|
| Backbuffer | 800x600 |
| Game viewport | 544x416 (17x13 tiles) |
| Tile size | 32x32 px |
| Render target | `frmMain.MainViewPic.hWnd` |

---

## 2. Game Loop y Timing

### Entry Point (`modEngine.bas:1703-1796`)

```
Engine_Show_NextFrame():
  1. Update scroll offsets (movement interpolation)
  2. Engine_BeginScene (clear backbuffer)
  3. ConvertCPtoTP (mouse-to-tile)
  4. If UserCiego → black screen, else → RenderScreen()
  5. Dialogos.Render (chat bubbles)
  6. DialogosClanes.Draw (guild dialogs)
  7. Engine_FPS_Update
  8. Engine_EndScene (present)
  9. Water polygon animation
  10. Ambient light check (cada 1000ms)
  11. timerElapsedTime = GetElapsedTime()
  12. timerTicksPerFrame = timerElapsedTime * engineBaseSpeed
```

### High-Precision Timer (`modEngine.bas:1799-1822`)

Usa `QueryPerformanceCounter`/`QueryPerformanceFrequency`:
```vb
GetElapsedTime = (start_time - end_time) / timer_freq * 1000  ' ms
```

**Frame-rate independent**: todo movimiento se escala por `timerTicksPerFrame`.

### FPS

- **Sin cap explícito** en la versión DX8 (la DX7 legacy tenía cap a 100 FPS)
- Contador: incrementa `FramesPerSecCounter` cada frame, resetea cada 1000ms

### Constantes de Movimiento

| Constante | Valor |
|-----------|-------|
| `EngineBaseSpeed` | 0.0172 |
| `ScrollPixelsPerFrame` | 8 px |
| Tile transit time | ~233ms (a ~17fps interno) |

---

## 3. Pipeline de Render (5 Pasadas)

`RenderScreen()` en `modEngine.bas:1291-1562`:

### Pasada 1: Layer 1 — Suelo
```
For Y/X in screen bounds:
    Engine_Render_Layer1(tile) → con deformación de polígonos para agua
```
Incluye offsets de terreno (montañas) y vertex lighting per-tile.

### Pasada 2: Reflejos en Agua
```
For each character on water:
    Engine_Char_Water() → sprite invertido, alpha=100
```

### Pasada 3: Layer 2 — Decoración de Suelo
```
For Y/X in full buffer:
    Draw MapData(X,Y).Graphic(2)  — animated transparent tiles
```

### Pasada 4: Objetos + Personajes + Layer 3 (Combinada)
```
For Y/X in buffer (painter's algorithm Y-first):
    1. ObjGrh (items en el suelo)
    2. Char_Render (personaje completo)
    3. Graphic(3) (copas de árboles, techos bajos)
```

### Pasada 5: Proyectiles + Daño
```
For each projectile: draw with rotation
For each damage number: draw floating text
```

### Post-render: Layer 4 — Techos
```
If NOT under roof:
    Draw Graphic(4) with variable alpha (bTechoAB)
```

---

## 4. Renderizado de Personajes

### `Engine_Char_Render()` (`modEngine.bas:1824-2004`)

#### Orden de Dibujado

**La 13.3 NO varía el orden por heading** — siempre es:
```
1. Body
2. Head (at HeadOffset)
3. Helmet (at HeadOffset + OFFSET_HEAD)  — OFFSET_HEAD = -34px
4. Weapon
5. Shield
6. Name text
7. FX effect
```

Esto difiere de versiones anteriores que variaban body/weapon/shield por dirección.

#### Movimiento

```
MoveOffset += 8 * Sgn(direction) * timerTicksPerFrame
Walk animation starts on movement, stops when offset reaches 0
AnimTime: weapon/shield continue after movement stops
```

#### Struct `Char`

```
Body, Head, Casco, Arma, Escudo: animation data per heading
Heading: 1=N, 2=E, 3=S, 4=W
MoveOffsetX/Y: sub-tile interpolation (Single)
FxIndex + fX: active spell effect
Aura: integer field (exists but NOT rendered in 13.3)
Invisible, Muerto: state flags
```

---

## 5. Sistema de Texturas

### `clsTextureManager.cls`

| Propiedad | Valor |
|-----------|-------|
| Hash buckets | 2000 |
| Key | fileIndex mod 2000 |
| Memory limit | 512MB (default), 256MB (min) |
| Eviction | LRU (oldest LastAccess) |
| Pool | D3DPOOL_MANAGED |
| Filter | D3DX_FILTER_NONE |

### Dual Format Loading

1. Busca `.bmp` en `Graficos/` (color key: black = `0xFF000000`)
2. Fallback a `.png` en `GraphicsHD/` (alpha channel nativo, key: `0x0`)

Esto permite **HD graphics override** — misma indexación, texturas de mayor resolución.

---

## 6. Iluminación y Sombras

### Per-Vertex Tile Lighting

Cada tile tiene `light_value(0..3)` — 4 colores ARGB para las 4 esquinas.
En `Device_Textured_Render`, si `light_value = 0`, usa `Base_Light` (ambient).

### Sombras (`modEngine.bas:2251-2267`)

Vertex-skewed parallelogram:
```vb
temp_verts(0).X = X + (Width * 0.5)    ' top-left shifted right
temp_verts(0).Y = Y - (Height * 0.5)   ' and up
```
Crea una sombra proyectada como paralelogramo.

### HD Light Textures (`modDx8_HDC.bas`)

```
SHADOW_HD = 23651
LIGHT1_HD = 23652
LIGHT2_HD = 23656
RADIUS_HD = 23653
```
`Render_Radio()`: overlay radial de viñeta (3 PNGs superpuestos).

---

## 7. Ciclo Día/Noche

### `modDx8_ambient.bas:1-304`

Colores por franja horaria:

| Hora | Período | RGB |
|------|---------|-----|
| 06:00-08:00 | Amanecer | 193, 176, 121 |
| 08:00-16:00 | Día | 255, 255, 255 |
| 16:00-19:30 | Tarde | 215, 213, 215 |
| 19:30-19:35 | Anochecer | 187, 189, 192 |
| 19:35-23:00 | Noche | 169, 169, 197 |
| 23:00-03:00 | Noche profunda | 159, 155, 197 |

### Transición

`Ambient_Fade()`: incrementa/decrementa R, G, B en +/-1 por llamada hasta que `ColorActual == ColorFinal`. Se ejecuta cada frame cuando `Fade=True`. Resultado: transición gradual entre franjas.

---

## 8. Efectos Visuales (FX)

### Aplicación (`modEngine.bas:2053-2068`)

```vb
SetCharacterFx(CharIndex, fxIndex, Loops)
    .FxIndex = fxIndex
    InitGrh(.fX, FxData(fxIndex).Animacion)
    .fX.Loops = Loops
```

### Renderizado

Dibujados con `AlphaColor` (~27% opacidad) en la posición del personaje + `FxData.OffsetX/Y`.
Cuando `.fX.Started = 0` (animación terminada), `FxIndex` se limpia.

### Datos

Cargados de `Init\Fxs.ind`: `Animacion` (GrhIndex) + `OffsetX` + `OffsetY` por entrada.

---

## 9. Sistema de Partículas

**Vestigial en la 13.3.** Variables declaradas en `Declares.bas:1006-1031`:
```vb
Public ParticleTexture(1 To 15) As Direct3DTexture8
Public ParticleOffsetX/Y As Long
```

Usadas solo por el sistema de niebla para tracking de scroll parallax. **No hay emitter/renderer de partículas standalone**. La infraestructura de vbGore nunca fue completada.

---

## 10. Proyectiles

### `modDx8Gore.bas:29-131` + `modEngine.bas:1457-1510`

```
Type Projectile:
    X, Y: current position (pixels, world-space)
    tX, tY: target position
    RotateSpeed: rotation speed
    Rotate: current angle
    Grh: sprite
```

Per frame:
1. Calcular ángulo al target: `Engine_GetAngle(X,Y, tX,tY)`
2. Mover: `X += Sin(angle) * elapsed * 0.8`, `Y -= Cos(angle) * elapsed * 0.8`
3. Rotar: `Rotate += RotateSpeed * elapsed * 0.01`
4. Culling fuera de pantalla
5. Draw con rotación 2D (vertex transform manual)
6. Eliminar cuando dist < 20px del target

### Rotación 2D (`modEngine.bas:2228-2248`)

```vb
CenterX = X + Width/2
CenterY = Y + Height/2
For each vertex:
    NewX = CenterX + (v.X - CenterX) * -Cos - (v.Y - CenterY) * -Sin
    NewY = CenterY + (v.Y - CenterY) * -Cos + (v.X - CenterX) * -Sin
```

---

## 11. Agua y Reflejos

### Deformación de Polígonos (`modEngine.bas:1752-1782`, `modDx8graphics.bas:255-314`)

Dos contadores oscilantes `polygonCount(0)` y `polygonCount(1)`:
- Amplitud: ±4 pixels
- Velocidad: `4 * 0.042` por frame
- Patrón checkerboard: `X mod 2` y `Y mod 2` determinan qué vértices se desplazan
- Tiles adyacentes no-agua se ignoran (`POLYGON_IGNORE_TOP/LOWER`)

### Detección de Agua (`General.bas:1105-1112`)

```
GrhIndex in [1505-1520] OR [5665-5680] OR [13547-13562]
AND Layer2 == 0
```

### Reflejos de Personajes (`modEngine.bas:2006-2051`)

`Engine_Char_Water()`: sprite invertido (angle=359), alpha=100, dibujado 2 tiles abajo.
Verifica `MapData(.Pos.Y + 2).WaterEffect = 1`.

---

## 12. Weather (Niebla)

### `modDx8_ambient.bas:44-121`

Dos capas de niebla con parallax independiente:

| Capa | GrhIndex | Velocidad | Dirección |
|------|----------|-----------|-----------|
| 1 | 23655 | 0.018-0.028 + random | Normal |
| 2 | 23654 | 0.037-0.047 + random | Opuesta |

- Tiles de 512x512px, tileados sobre el viewport
- Alpha: 100/255 cada capa
- Solo cuando `WeatherDoFog = True`
- Lluvia: declarada en DX7 legacy pero **comentada** en DX8

---

## 13. Texto y Diálogos

### Bitmap Font System (`modDx8_Fonts.bas:1-322`)

- Textura: `Data\texdefault.bmp`
- Métricas: `Data\FontData.dat`
- Vertex arrays pre-cacheados para 256 chars ASCII
- Render: shadow pass (offset -1,-1, alpha-40) + main pass
- **Sin batching**: cada carácter = 1 `DrawIndexedPrimitiveUP`

### Chat Bubbles (`clsDialogs.cls`)

- Max 100 diálogos simultáneos
- Wrap: 18 chars/línea
- Lifetime: 2000ms + 100ms/carácter
- Fade-in/fade-out transitions
- Binary search por CharIndex: O(log n)

### Damage Numbers (`modDx8Gore.bas:134-196`)

- Display time: 2000ms
- Float up: `Y = Counter * 0.02 + baseY`
- Fade: `alpha = 10 + Counter * 0.09`

---

## 14. Alpha Blending y Blend Modes

### Default

```
SrcBlend = D3DBLEND_SRCALPHA
DestBlend = D3DBLEND_INVSRCALPHA
```

### Modos PNG (`modDx8_HDC.bas:80-92`)

| Alpha param | Src | Dest | Efecto |
|-------------|-----|------|--------|
| 1 | ZERO | SRCCOLOR | Multiply |
| 2 | SRCALPHA | SRCCOLOR | Alpha+Multiply |
| 3 | SRCCOLOR | ONE | Additive (parcial) |
| 4 | ONE | ONE | Additive (completo) |

### Color Arrays Predefinidos

```
DefaultColor = White (255,255,255,255) — sin tint
ShadowColor = 0 (all black)
AlphaColor = ARGB(70, 255, 255, 255) — 27% opacidad
```

---

## 15. Zoom

### `modDx8graphics.bas:382-425`

Modifica `MainScreenRect` (rect fuente para Present):
- **Zoom In**: reduce bottom/right (min 367/491)
- **Zoom Out**: aumenta bottom/right (max 459/583)
- **Normal**: match `MainViewPic.ScaleHeight/Width`

Offset compensatorio: `(ScreenHeight - MainScreenRect.bottom) / 2`.

---

## 16. Terreno con Elevación

### `modMath.bas:139-177`

`Generate_Mountain()`: genera elevación sinusoidal por vértice.
Los offsets `MapBlock.Offset(0..3)` desplazan vértices en Y, creando colinas y montañas.
También aplica darkening proporcional al offset (sombra de relieve).

---

## 17. Optimizaciones

### Presentes

- LRU texture cache con hash table (2000 buckets, 512MB limit)
- Indexed primitives (`DrawIndexedPrimitiveUP`)
- Pre-cached font vertex arrays
- Tile buffer para scroll suave
- Screen culling para proyectiles y damage text
- D3DPOOL_MANAGED (runtime maneja VRAM/system transfers)
- No Z-buffer (innecesario para 2D)
- No texture filtering (pixel-perfect)
- Binary search para diálogos
- Dual BMP/PNG con HD override path

### Ausentes

- Sin sprite batching (cada tile/char = 1 draw call individual)
- Sin texture atlas (cada archivo = 1 textura separada)
- Sin text batching (cada carácter = 1 draw call)
- Sin dirty rect tracking (full redraw cada frame)
- Sin frustum culling en layers 2-4 (iteran buffer completo)
- Sin frame rate limiter en DX8

---

## Archivos Clave

| Archivo | Responsabilidad |
|---------|----------------|
| `modEngine.bas` | Core: tipos, data loading, RenderScreen, CharRender, proyectiles, agua, geometría |
| `modDx8graphics.bas` | Device init, render states, Layer1, FPS, zoom, BeginScene/EndScene |
| `modDx8_HDC.bas` | PNG con blend modes, HD graphics, radial light overlay |
| `modDx8_ambient.bas` | Día/noche, fog, weather states |
| `modDx8_Fonts.bas` | Bitmap font system |
| `modDx8Gore.bas` | Proyectiles, damage numbers, angle math |
| `modMath.bas` | Ángulos, terrain generation |
| `clsTextureManager.cls` | LRU texture cache |
| `clsDialogs.cls` | Chat bubbles |
| `Declares.bas` | Constantes, tipos, globals |
| `MainTimer.cls` | Cooldown timers (8 independientes) |
