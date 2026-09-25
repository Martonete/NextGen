# Troll Coloso de la Cienaga

NPC 972, cuerpo 521, textura `resources/data/Graficos/45013.png`.
GRH 33777–33808: cuadros; 33809–33812: ciclos norte/este/sur/oeste.
Atlas 768×512, celdas 96×128, ocho cuadros por dirección, ciclo 800 ms.
Los pies se anclan a la misma línea de suelo; escala uniforme de todos los cuadros.
El primer cuadro sirve de reposo. Cabeza y garrote están integrados en el cuerpo.
El motor usa estas cuatro animaciones de caminata; no hay pistas independientes de ataque o muerte.

Hostil: persigue y ataca cuerpo a cuerpo (Movement=3). Vida 8500,
daño 90–135, defensa 35, defensa mágica 15, experiencia base 14000,
oro base 650–1100. Las recompensas aplican los multiplicadores del servidor.
No domesticable, terrestre, con respawn cuando se coloca como spawn de mapa.

Para probar: reabrir cliente/editor y, como GM, `/LOADNPCS` seguido de
`/ACC 972`. Para probar agresión usar un personaje normal: los GM son ignorados
por la IA existente. Disponible en el catálogo NPC del World Editor.
No se agregan spawns a mapas existentes.

Arte generado y corregido con la herramienta integrada de imágenes.
`source.png` conserva la salida con fondo negro de color key; el atlas final
tiene alfa real. La importación usa el umbral negro del cliente y escalado nearest.
El importador offline `TrollImport.tscn` registra datos y comprueba referencias
con los cargadores reales; rechaza IDs ocupados y overrides diferentes.
Ya fue ejecutado: no se debe repetir sobre el catálogo actualizado.

Verificado: compilación C# sin avisos ni errores, carga Godot de las cuatro
animaciones y 32 referencias, catálogos sincronizados y alfa del atlas.
Pendiente de prueba manual: combate en una sesión de jugador contra el servidor.

## Prompt empleado

Production game sprite sheet for Argentum Online classic 2D medieval fantasy
MMORPG. Canvas 1536x1024, precise 8 columns x 4 rows of 192x256 cells.
Exactly 32 full-body sprites of the same giant hostile troll. Row 1 north/back,
row 2 east/right profile, row 3 south/front, row 4 west/left profile.
Eight chronological phases of a looping heavy walking cycle per row; both feet
alternate plant, lift and swing, first frame neutral with two feet apart.
Fixed scale and floor baseline, centered within each cell; no overlap.
Massive hunched troll with moss-dark olive skin, cracked slate stone plates on
back and shoulders, amber eyes, lower tusks, bald craggy head, thick long arms,
short strong legs and broad feet. Ragged leather loincloth, rope belt and heavy
stone club in right hand. Crisp hand-painted pixel-art, classic Argentum look,
slightly elevated RPG camera, not diamond isometric. Head integrated into body.
Consistent anatomy, clothes, club and colors in all 32 frames. No text, aura,
particles or watermark. Transparent background requested initially.

Final background correction: replace only the checkerboard background with
perfectly flat pure black RGB(0,0,0) for the game's black color key, including
limb gaps. Preserve all 32 poses and the exact atlas geometry. No gradient,
floor or glow. The importer converts this color key to alpha.
