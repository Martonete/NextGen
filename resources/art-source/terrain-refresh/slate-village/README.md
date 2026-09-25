# Terreno para las casas de pizarra

Modo: generación integrada de imágenes, dos materiales originales nuevos.
Salidas generadas: `grass.png` y `earth.png`. Copias de la versión anterior en `before/`.
PNG integrados: `resources/data/Graficos/6000.png` hasta `6009.png`.

Reproducir con `tools/Import-SlateVillageTerrain.ps1` (también invocado por
Import-TerrainRefresh y Compile-TerrainMaterials). Validar con Test-TerrainAssets.
El importador adapta los materiales generados y usa exactamente la misma cobertura
de tierra calculada desde los originales que la versión precedente. No utiliza
contornos generados para decidir el trazado. Mantiene sombras de depresiones,
dimensiones, píxeles de clave negra y alpha. Comparte materiales entre todas las
piezas, sin crear mapas nuevos ni modificar índices, colisiones o shaders.

No se recompilaron paquetes de distribución: cliente local y editor usan PNG fuente.

Validación: Test-TerrainAssets PASS en las diez láminas (dimensiones, alpha y
clave negra píxel a píxel). HouseArtSmoke y CatalogRecoverySmoke PASS en el
render real; inspección de Tanaris exterior, interior y curvas. Capturas finales
en `tanaris-preview.png` y `tanaris-curves.png`. No se afirma validación visual
exhaustiva de todos los mapas que reutilizan estas láminas.

## Prompt grass.png

Use case stylized-concept. Production game texture for Argentum Online, top-down orthographic ground material, seamless tileable square filled edge-to-edge with refined dark moss-green SHORT grass. Reimagined premium classic hand-painted 2D RPG pixel-art material to accompany teal slate roofs and limestone medieval houses. Tiny readable clustered grass blades, rich muted forest and olive greens, subtle soft soil visible between blades only. UNIFORM even luminosity and density across entire square, fine detail but quiet low contrast at game scale. Seamlessly repeating on all edges, no large patches or clumps, no bare brown areas, no directionality, no vignette, no lighting gradient, no shadows, no flowers, no stones, no paths, no border, no text, no objects. All surface grass, square texture only.

## Prompt earth.png

Use case stylized-concept. Production seamless tileable square ground texture for Argentum Online classic top-down 2D RPG. Edge-to-edge compacted worn earthen village footpath MATERIAL ONLY, no actual path shape or borders. Fine warm muted umber and taupe clay, tiny embedded dull sand grains and very small scattered flat pebbles, subtly hand-painted pixel-art clusters, soft low-contrast variation. Beautiful original surface compatible with dark green grass and teal slate medieval houses. Completely uniform overhead diffuse illumination, perfectly flat orthographic view. No grass, no vegetation, no large stones, no cobblestone paving, no cracks networks, no highlights, no vignette, no shadows, no wheel ruts, no straight lines, no tile borders, no text. Restrained medium-dark earthy palette, not orange or bright beige. Even density everywhere, designed for continuous repeating ground at small scale.

## Ajuste final de grass.png (edición integrada de grass-draft.png)

Edit this seamless grass material only. Keep same full square uniform grass coverage, no paths or objects. CRITICAL make individual grass blades FOUR TIMES SMALLER and far more densely packed, very fine delicate tiny grass carpet, not large fern leaves. Darken the overall green by about 25 percent and strongly reduce the bright yellow highlights. Muted deep forest moss green, restrained low contrast, natural quiet classic RPG pixel texture. Keep absolutely even texture density and luminosity across the whole sheet, seamless edges, no broad patches, no artificial lines, no vignette. This texture appears directly at small size next to a 48-pixel tall game character. Fine short grass rather than big plants.
