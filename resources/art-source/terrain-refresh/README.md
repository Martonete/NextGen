# Renovacion de terreno 6000-6009

## Version actual: materiales compartidos (2026-09-09)

Las pasadas independientes descritas debajo quedaron reemplazadas. Ejecutar
`tools/Compile-TerrainMaterials.ps1` (o el entry point Import-TerrainRefresh).
Reutiliza dos muestras de las imagenes generadas archivadas: pasto de 6000
y tierra del centro del cruce. Ambas son periodicas a 32px y se comparten
entre TODAS las laminas. El pasto se oscurece 12%. La cobertura se obtiene
del contraste rojo-verde de los originales a resolucion nativa; no se escalan
ni recortan los contornos originales. Las transiciones siguen la cobertura
del arte original, no los contornos reinterpretados por generacion.

Prueba visual: Tanaris, rectas y curvas. CatalogRecoverySmoke PASS. No se
modificaron mapas, GRHs ni codigo del renderer. El patron compartido se repite
cada tile; no se afirma una textura no repetitiva.

Lo que sigue documenta el historial de intentos y sus prompts, no el proceso
de compilacion vigente.

## Correccion de empalmes

Correccion tecnica posterior: el importador recorta el rectangulo ocupado antes
de reducir y refleja las muestras del filtro en sus bordes (TileFlipXY). Asi
no interpola el relleno negro ni introduce una fila del arte viejo al resolver
el alpha del contorno. No se regenero arte para esta correccion.

Segunda pasada integrada con imagegen sobre 6005-6009, usando 6000 como
referencia visual comun. Prompt de correccion: "Use case: compositing. Image 1
EDIT TARGET road atlas. Image 2 EXACT GRASS MATERIAL SOURCE (top strip).
Fix visible seams: replace ALL grass surrounding dirt in image1 with the SAME
longer-bladed soft natural grass texture from image2, identical blade size at
native pixel scale, colors, brightness and density. Remove the tiny curly moss
rim completely, including right next to dirt. No darker or brighter green band.
Preserve dirt shape, path width, intersections, black padding and atlas layout.
No new objects or shadows. Output only edited image1."

Se mantienen los tamanos nativos. Las salidas de ambas pasadas permanecen en
generated/; sources.csv selecciona las actuales para reproducir la importacion.

Generado con la herramienta integrada imagegen (skill imagegen), modo edicion,
una llamada por lamina. Integrado en `resources/data/Graficos/6000.png` a
`6009.png`. Originales reversibles en `original/`; correspondencias de salidas
en `sources.csv`. No se modificaron mapas, indices GRH ni colisiones.

## Prompt utilizado

Use case: style-transfer. Image 1 is the EDIT TARGET, functional Argentum Online
terrain atlas {id}.png. Replace only surface artwork with refined classic
top-down pixel-art natural moss/olive green short grass with fine clustered
blades, less harsh yellow noise, and warm compacted earthen paths where existing
brown paths occur. Keep overall brightness close to original, consistent muted
natural palette. CRITICAL preserve EXACT existing path silhouettes, widths,
junctions, curves, grass/dirt boundaries, terrain depressions, all tile positions
and proportions. Retexture, never redesign geometry. Preserve original canvas
aspect and exact placement of every black padding area; pure black stays pure
black. Seamless matching boundary textures and flat even lighting, no new
objects, no flowers, rocks, text, grids, glow, borders, perspective, scene,
mockup. Return the atlas only.

Variantes: 6000 solo pasto, cuatro variantes de 128x128, sin caminos nuevos.
6000-6008: lienzo 512x512 con arte en las primeras 128 filas. 6005-6006:
solo primeras 384 columnas activas. 6009: cruce completo 128x128, mantener
cuatro esquinas de pasto redondeadas y cruz de tierra.

## Integracion y comprobaciones

`tools/Import-TerrainRefresh.ps1` adapta la resolucion generada al tamano nativo
y conserva pixel por pixel la clave negra original. No agrega costo de shaders
ni cambia dimensiones de texturas en memoria. Las pinceladas de las transiciones
son nuevas; no se promete identidad pixel a pixel de los bordes de tierra.

Prueba offline `CatalogRecoverySmoke.tscn`: PASS antes y despues. Comparacion
visual de Tanaris y carga del fondo de inicio, sin conexion al servidor.
El World Editor descubre `resources/data` como fuente primaria; reiniciar el
cliente/editor para soltar las texturas anteriores en cache.

Las capturas de la prueba usan su propio encuadre parcial, no representan
el modo de pantalla completa del cliente normal.
