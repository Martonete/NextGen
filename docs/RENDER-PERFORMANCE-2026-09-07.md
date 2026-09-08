# Rendimiento de sombras: mediciones y cambios

## Cuello de botella confirmado

Ao20ShadowRenderer retenía una sola composición por personaje. Cada cambio de
cuadro borraba una imagen de 256×256, recomponía todas las capas y actualizaba
la textura GPU, incluso al repetir un cuadro ya visto. Los personajes con el
mismo aspecto tampoco compartían resultados.

Ahora se reutilizan composiciones por clave exacta de capas, recortes y anclajes.
La luz y la posición de pantalla se aplican al dibujar: no invalidan la silueta.
LRU de 256 texturas (64 MiB máximos de imágenes GPU base, sin contar overhead).
Las imágenes CPU temporales se liberan inmediatamente. Se liberan explícitamente
las texturas e imágenes al limpiar la caché; antes se vaciaba solo el diccionario.
Las lecturas de atlas fuente también quedan limitadas a 16 imágenes.

## Prueba reproducible

`res://test/render/ShadowPerfSmoke.tscn`, Godot 4.4 .NET, Compatibility, RTX 3070.
1024 solicitudes de sombra: 16 personajes, ocho cuadros del nigromante,
mismas capas y geometría, dos pasadas en el mismo proceso.

| CPU de preparación/envío | Antes | Después |
|---|---:|---:|
| Primera pasada | 253,21 ms | 53,99–60,48 ms |
| Segunda pasada | 228,47 ms | 42,51–43,90 ms |
| Asignaciones administradas segunda pasada | 65.576 B | 40 B |

Los 40 B corresponden al cronómetro de la prueba. Se comprueba que hay solo ocho
composiciones para los 16 personajes, cero reconstrucciones en la segunda pasada,
límite de 256 entradas y vaciado correcto. No son FPS globales ni una medición
de GPU; el beneficio real depende de cuántos personajes se dibujen, el mapa y
el resto de las capas. No se demuestra que éste sea el único origen de la caída
reportada por el usuario.

Se ensayó agrupar líneas de espada y electricidad: fue más lento en la prueba
local (electricidad 52,88→64,18 ms), por lo que se descartó. Los efectos mantienen
su dibujo y calidad previos. No se cambian velocidad, límite de FPS ni opciones
del usuario. RenderSmoke y pruebas de composición/caché pasaron.
