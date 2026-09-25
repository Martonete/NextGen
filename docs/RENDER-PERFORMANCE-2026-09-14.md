# Rendimiento de render — 2026-09-14

Objetivo: duplicar los FPS en juego (estaba clavado en ~80). Medición de referencia con
`WaterPerfSmoke.tscn` (mapa 27, 1920x1080, vsync off, solo WorldRenderer):

| Etapa | ms/frame | FPS | draw calls |
|---|---|---|---|
| Antes | 6,55 | 153 | 385 |
| L1+L2 retenidas | 4,24 | 236 | 385 |
| + L1 agrupada por textura | 3,56 | 281 | 115 |
| + rango L1 ajustado, techos por fila, capas vacías sin redibujar | 3,18 | 315 | 115 |
| + sombras en un solo triangle array | 3,39 | 295 | 112 |

## Cambios

- **Capas estáticas retenidas** (`WorldRenderer.UpdateStaticLayerCache`): L1 no-agua y L2
  estáticas se dibujan una vez por tile-paso (translación sub-tile en el scroll) en vez de
  ~5000 `DrawTextureRectRegion` por frame. Objetos de piso y L3 siguen dinámicos
  (`_staticContentCacheActive = false`): los muta el servidor. La máscara de reflejos sobre
  tierra ahora solo redibuja los ~35 tiles alrededor de cada personaje reflejado.
- **L1 retenida agrupada por textura** (`DrawStaticGround`): los tiles de piso no se solapan,
  así que se emiten por `FileNum` y el batcher de compat los fusiona: 385 → ~115 draw calls.
- **Tamaño de textura cacheado** (`TextureManager.TryGetTexture`): antes cada sprite hacía
  dos llamadas nativas `GetWidth/GetHeight` (~14k por frame).
- `GrhAnimator.GetCurrentFrame`: los GRH estáticos salen antes de sondear el diccionario de FX.
- Pasadas L1 usan el rango `_frameL1*` (±3) en vez del buffer de sprites altos; la pasada de
  techos salta filas/mapas sin capa 4.
- Capas hijas que solo muestran colas (auras, reflejos, diálogos, partículas, techos) no se
  redibujan mientras la cola está vacía.
- Sombras AO20: las 4 copias desplazadas van en **un** `CanvasItemAddTriangleArray` en vez de
  4 `DrawPolygon`.
- Fuera del renderer: etiquetas del HUD a 10 Hz comparando contra caché (no contra `Label.Text`
  nativo), `StatsPanel` a 4 Hz, `SafeZoneBorderLayer` deja de redibujar al salir de la zona,
  uniforms constantes de la niebla solo al cambiar de zona, sin `new[]` por frame en el input.

## Pendiente (si hace falta más)

- Hornear L1+L2 en texturas por chunk (1–3 draw calls para el piso y sin overdraw).
- Agua: cachear textura/tamaño por GRH y bits de vecinos por tile.
- `Camera2D.Zoom` en vez del `SubViewport` + upscale (una copia de pantalla completa menos).
