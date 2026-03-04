# Comparativa de Renderizado: 13.3 Oficial vs TSAO (v11.5) vs Godot Client

Análisis arquitectónico enfocado en **estructura, optimización y patrones** relevantes
para identificar mejoras que podríamos incorporar al cliente Godot.

---

## Tabla Comparativa Rápida

| Aspecto | 13.3 Oficial | TSAO v11.5 | Godot Client |
|---------|-------------|------------|--------------|
| **API gráfica** | Direct3D 8 | Direct3D 8 | Godot 4 CanvasItem |
| **Viewport** | 544x416 (17x13) | 800x600 (~25x19) | 534x408 (17x13) |
| **Render passes** | 5 (L1→Reflejos→L2→Chars+L3→Proyectiles) + L4 | 5 (L1→Flechas→L2→Chars+L3→Particles) + L4 | 8 layers (Node2D children) |
| **FPS control** | Sin cap | Sin cap | VSync + Engine.MaxFps configurable |
| **Timer** | QPF/QPC (sub-ms) | QPF/QPC (sub-ms) | Godot delta (double) |
| **Texture cache** | LRU 2000 buckets, 512MB | LRU 337 buckets, 16MB | LRU 256 entries |
| **Draw calls** | Individual DrawIndexedPrimitiveUP | Individual DrawIndexedPrimitiveUP | Individual DrawTextureRectRegion |
| **Sprite batch** | No | Existe (clsBatch) pero sin usar | No (Godot maneja internamente) |
| **Char draw order** | Fijo (body→head→helm→weapon→shield) | Variable por heading (4 órdenes) | Variable por heading (4 órdenes) |
| **Sombras** | Vertex-skewed parallelogram | Vertex-skewed parallelogram | DrawPolygon parallelogram |
| **Iluminación** | Per-vertex 4-corner + ambient | Per-vertex 4-corner + ambient | Per-tile average (1 color) |
| **Día/noche** | 6 franjas horarias + fade gradual | base_light global | Modulate color |
| **Agua** | Polygon deformation (vertex Y wave) | Grh animation estándar | Grh animation estándar |
| **Reflejos agua** | Sprite invertido, alpha=100, 2 tiles abajo | Sprite invertido, alpha=100, 1 tile abajo | Sprite invertido, alpha=60-125, layers dedicados |
| **Niebla** | 2 capas parallax (512px tiles) | No existe | No existe |
| **Partículas** | Vestigial (solo vars) | Completo (INI-driven, map+char bound) | Port completo del TSAO |
| **Auras** | Campo existe, NO se renderiza | 5 slots + rotación + additive blend | 6 slots + rotación + additive + reflejos |
| **FX** | 1 slot, alpha 27% | 3 slots + emoticones | 3 slots + emoticones |
| **Proyectiles** | Rotación + angle-based movement | Rotación + angle-based movement | Lineal sin rotación |
| **Texto** | Bitmap font custom | Bitmap font custom (3 tamaños) | Bitmap font custom (3 tamaños) |
| **Diálogos** | 100 max, 18 chars/line, binary search | 200 max, 24 chars/line, binary search | Inline en CharRenderer |
| **Blend modes** | 4 modos (multiply, additive, etc.) | 2 modos (normal + additive) | 2 modos (normal + additive via layers) |
| **Zoom** | Sí (modifica Present rect) | No | No (pero SubViewport escalable) |
| **Terrain elevation** | Sí (vertex Y offset, sinusoidal) | No | No |
| **HD graphics** | Dual path BMP/PNG override | Archivo .tsao con PNG | PNG directo filesystem |
| **Weather** | Fog dual-layer, rain (legacy) | No | No |

---

## Análisis Detallado: ¿Qué Mejoras Rescatar de la 13.3?

### MEJORAS RELEVANTES (Arquitectura/Optimización)

---

### 1. Per-Vertex Tile Lighting vs Per-Tile Average

**13.3**: 4 colores por tile (`light_value[0..3]`), uno por vértice. El hardware DX8 interpola suavemente entre vértices, produciendo gradientes de luz naturales en cada tile.

**TSAO**: Igual — 4 colores por tile, interpolación DX8.

**Godot**: `LightSystem.cs` calcula 4 colores por esquina, pero luego los **promedia** en `GetTileLight()` y aplica como un solo `Modulate` color por tile. Resultado: iluminación "blocky", sin gradientes.

**Impacto**: ALTO. La diferencia visual es significativa — en la 13.3 y TSAO, una antorcha junto a un tile oscuro crea un gradiente suave; en Godot, se ve un salto abrupto de color.

**Solución propuesta**: Usar `DrawTextureRectRegion` con un override de vertex colors, o usar un shader simple que interpole 4 colores por tile. Godot's `CanvasItem.DrawMesh()` o un custom `ShaderMaterial` con 4 uniforms per tile podrían lograr esto. Alternativamente, un lightmap texture que se renderiza como overlay.

**Prioridad**: ALTA — mejora visual grande, complejidad media.

---

### 2. Water Polygon Deformation

**13.3**: Los tiles de agua deforman vértices con una onda sinusoidal (amplitud ±4px, patrón checkerboard). Dos osciladores en contrafase crean un efecto de oleaje convincente a costo casi cero.

**TSAO**: No tiene — el agua es simplemente una animación de frames estándar.

**Godot**: Igual que TSAO — animación de frames.

**Impacto**: MEDIO-ALTO. Visualmente notable, especialmente en mapas con grandes cuerpos de agua.

**Solución propuesta**: Shader en el tile de agua que desplace UV o vértices con `sin(TIME)`. En Godot, un `ShaderMaterial` en los tiles de Layer 1 que detecte water tiles y aplique vertex displacement. Muy eficiente con GPU.

**Prioridad**: MEDIA — mejora visual atractiva, baja complejidad con shaders.

---

### 3. Ciclo Día/Noche con Franjas y Fade Gradual

**13.3**: 6 franjas horarias con colores específicos (amanecer dorado, atardecer rosado, noche azulada). Transición gradual pixel a pixel de RGB hasta alcanzar el target. Se revisa cada 1000ms, fade es continuo.

**TSAO**: Solo un `base_light` global estático — cambia por comando del servidor pero sin ciclo automático.

**Godot**: `Modulate` color en WorldRenderer. Puede setearse por mapa pero no hay ciclo automático.

**Impacto**: MEDIO. Agrega atmósfera significativa. El servidor ya podría enviar el estado de hora — solo falta el sistema visual en el cliente.

**Solución propuesta**: Implementar tabla de colores por hora + interpolación lineal en `WorldRenderer._Process()`. Chequear hora cada ~1 segundo, interpolar `Modulate` gradualmente. 50 líneas de código aproximadamente.

**Prioridad**: MEDIA — buen costo/beneficio.

---

### 4. Sistema de Proyectiles con Rotación

**13.3**: Los proyectiles (flechas) tienen:
- Movimiento basado en ángulo trigonométrico hacia el target
- Rotación visual del sprite (vertex transform 2D)
- Velocidad escalada por elapsed time
- Culling fuera de pantalla
- Eliminación por proximidad (< 20px del target)

**TSAO**: Sistema similar — `Flechas_list` con angle-based movement y rotación.

**Godot**: Proyectiles lineales sin rotación. Interpolan posición en línea recta, sin sprite rotation.

**Impacto**: MEDIO. Las flechas girando en el aire se ven mucho más naturales.

**Solución propuesta**: Calcular ángulo `atan2(target - pos)` y aplicar como rotación del sprite al dibujar. Agregar campo `Rotation` a `ArrowProjectile`. En `DrawTextureRectRegion`, pasar el ángulo como transform. ~30 líneas.

**Prioridad**: MEDIA — mejora visual fácil.

---

### 5. Fog/Weather System (Dual-Layer Parallax)

**13.3**: Dos capas de niebla con velocidades y direcciones independientes. Tiles de 512px tileados sobre viewport, alpha=100/255 cada capa. Crea profundidad atmosférica.

**TSAO**: No existe.

**Godot**: No existe.

**Impacto**: MEDIO. Útil para mapas especiales (bosques, pantanos, dungeons).

**Solución propuesta**: Un `Node2D` overlay con dos `Sprite2D` grandes, UV offset animado por `_Process()`. En Godot se puede hacer con `CanvasLayer` + `ParallaxBackground` o con shader de scroll UV. Activar por flag de mapa o comando de servidor.

**Prioridad**: BAJA-MEDIA — feature atmosférico, no crítico.

---

### 6. Terreno con Elevación (Mountains)

**13.3**: `Generate_Mountain()` aplica offsets sinusoidales por vértice, creando colinas y montañas. También darkening proporcional al offset (sombra de relieve).

**TSAO**: No existe.

**Godot**: No existe.

**Impacto**: BAJO-MEDIO. Puramente visual para mapas específicos.

**Solución propuesta**: Si algún mapa lo usa, implementar vertex offsets al dibujar Layer 1. En Godot, un shader que lea un heightmap y desplace Y + modifique brightness.

**Prioridad**: BAJA — solo relevante si los mapas lo usan.

---

### 7. Zoom System

**13.3**: Zoom in/out modificando el source rect del Present, efectivamente escalando la porción visible del backbuffer.

**TSAO**: No existe.

**Godot**: No existe como feature de juego, pero el `SubViewportContainer` puede escalarse.

**Impacto**: BAJO. Feature de conveniencia, no arquitectural.

**Solución propuesta**: Modificar `SubViewport.Size` dinámicamente o cambiar `SubViewportContainer.Stretch` + scale factor. Cuidar que el tile buffer sea suficiente para zoom out.

**Prioridad**: BAJA — nice-to-have.

---

### 8. Múltiples Blend Modes para Gráficos HD

**13.3**: 4 blend modes (multiply, alpha+multiply, additive parcial, additive completo). Permite gráficos HD con diferentes estilos de composición.

**TSAO**: Solo normal + additive.

**Godot**: Solo normal + additive (via layers separados).

**Impacto**: BAJO. Solo relevante si se crean gráficos HD que requieran multiply blending.

**Prioridad**: BAJA — solo si se diseñan assets que lo necesiten.

---

### NO RELEVANTES (ya lo hacemos igual o mejor)

| Aspecto | Por qué no aplica |
|---------|-------------------|
| **Char draw order variable** | Godot ya varía por heading (4 órdenes) — **mejor que 13.3** que usa orden fijo |
| **Aura system** | Godot tiene 6 slots + rotación + additive + reflejos — **13.3 no renderiza auras** |
| **FX system** | Godot tiene 3 slots — **igual que TSAO, mejor que 13.3** (1 slot) |
| **Particle system** | Godot tiene port completo del TSAO — **13.3 tiene sistema vestigial** |
| **Texture cache** | Las tres usan LRU. Godot confía en el GPU management de Godot además |
| **VSync/FPS control** | Godot tiene VSync + MaxFps configurable — **mejor que ambos VB6** |
| **Timer precision** | Godot delta es double-precision — **equivalente a QPF** |
| **Water reflections** | Godot tiene layers dedicados + reflected auras — **mejor que ambos** |
| **Config system** | Godot tiene 30+ toggles + presets — **superior a ambos** |
| **Chat bubbles** | Funcional en Godot — 13.3 tiene binary search pero con 100 max vs nuestro inline |

---

## Plan de Implementación Sugerido

### Fase 1 — Alto Impacto, Complejidad Razonable
1. **Per-vertex lighting** — Reemplazar el average por interpolación real (shader o vertex colors)
2. **Ciclo día/noche** — Tabla de colores + interpolación en WorldRenderer

### Fase 2 — Mejoras Visuales
3. **Water polygon deformation** — Shader sinusoidal en tiles de agua
4. **Projectile rotation** — Ángulo + rotación de sprite

### Fase 3 — Atmosphere
5. **Fog system** — Dual-layer parallax overlay

### Fuera de scope (por ahora)
- Terrain elevation (requiere mapas diseñados para ello)
- Zoom (nice-to-have, no prioritario)
- Extra blend modes (solo si se diseñan assets HD)

---

## Conclusión

La 13.3 tiene **3 mejoras arquitecturales significativas** que no tenemos:

1. **Per-vertex lighting interpolation** — la más impactante visualmente
2. **Water polygon deformation** — efecto visual atractivo a bajo costo
3. **Day/night cycle** — atmósfera con implementación simple

Y **2 mejoras menores** interesantes:
4. Projectile rotation
5. Fog/weather system

El resto de diferencias son features que nosotros ya hacemos **igual o mejor** (auras, partículas, FX, char draw order, config, VSync). La migración a Godot nos dio ventajas naturales en muchas áreas donde la 13.3 está limitada por la API DX8.
