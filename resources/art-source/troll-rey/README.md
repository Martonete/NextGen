# Gorath, Rey de la Cienaga

Jefe hostil NPC 973, cuerpo 522, textura `resources/data/Graficos/45014.png`.
32 cuadros N/E/S/O, 8 por dirección. Celdas 256×352; figura de aproximadamente
300 píxeles, tres veces el troll 972. Ciclo 1100 ms, reposo en primer cuadro.
GRH 33813–33844: cuadros; 33845–33848: ciclos.

Vida 95000, golpe 150–220, defensa 55, defensa mágica 30. Persigue y ataca
cuerpo a cuerpo con la IA existente. EXP base 120000; oro base 6000–10000.
No tiene animaciones separadas de ataque/muerte. Su ocupación lógica sigue
siendo un tile, como los NPCs originales; la escala visual no cambia el protocolo.
No se agregaron spawns permanentes a mapas.

Reabrir cliente/editor. Administrador: `/LOADNPCS`, después `/ACC 973`.
Los personajes GM no son objetivos de la IA: probar agresión con personaje normal.

El renderer calcula márgenes según las dimensiones de los cuerpos cargados para
mantener visibles los gigantes cuyos pies quedan debajo del borde de pantalla.
Se conserva el orden de profundidad por los pies. La visibilidad sigue sujeta
al área que envía el servidor y a la niebla de guerra.

Verificado: compilación sin errores, importación y referencias con cargadores
reales y ocho capturas con CharRenderer en cuatro direcciones, comparadas con
el troll anterior. Escena: `res://test/render/TrollBossPreview.tscn`.
Importación ya realizada con `TrollImport.tscn -- --boss`; no repetir.
Queda pendiente probar combate y límites de visibilidad conectado al servidor.

## Arte y prompt

Generado con la herramienta integrada de imágenes. `source.png` conserva el
original; el atlas de juego mantiene alfa y se escala uniformemente, con pies
anclados. Prompt: production Argentum Online dark medieval 2D RPG sprite atlas,
8 columns × 4 rows, 32 full-body frames of one gigantic troll king. North/back,
east/right, south/front, west/left; eight sequential heavy walking poses per row,
first frame neutral with both feet apart. Fixed scale and ground baseline,
no overlap. Dark olive moss skin, immense hunched torso, jagged slate rock crown,
shoulder boulders, tusks, amber eyes, rocky spine, rope belt with skull trophies,
oxblood loincloth, thick legs and broad feet. Monumental stone war maul with
bronze bands held low in right hand. Crisp hand-painted Argentum sprite style,
slightly elevated orthographic RPG camera. Pure black color-key background,
no gradient, checkerboard, floor, text, labels or grid. Consistent anatomy and
equipment in all frames. Requested 2048×1536; actual output 1448×1086 is sliced
proportionally into the 8×4 grid by the importer.
