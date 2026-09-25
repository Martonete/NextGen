# Casas de Tanaris: pizarra y piedra

Arte generado con la herramienta integrada de imágenes, a partir de las láminas originales.
Integración local: `resources/data/Graficos/5501.png`, `5502.png`, `5505.png`.
Los originales recuperables están en `originals/`; las salidas de generación, en `generated/`.

## Composición preservada

| GRH | PNG | Rectángulo indexado | Función |
|---|---|---|---|
| 5590 | 5501 | 0,0,256,96 | Pared interior, capa 3 |
| 5589 | 5502 | 0,0,256,160 | Fachada y laterales, capa 3 |
| 5591 | 5505 | 0,0,256,320 | Techo ocultable, capa 4 |

Se conservan las láminas completas de 256×256, 256×256 y 512×512 respectivamente.
El GRH 5588 (5500, jardinera) no se modifica; tampoco puertas, suelo, índices,
carteles, posiciones ni colisiones. Los reemplazos son globales allí donde se usan los PNG.
El proveedor local y el editor leen los recursos fuente. No se regeneró el paquete de distribución.

## Prompts utilizados

### 5505
Use case: precise-object-edit. Edit target: attached Argentum Online house ROOF sprite sheet. Repaint this exact game asset, not a full house, with gorgeous original dark teal-blue slate shingles, weathered copper ridge detailing, carved dark wooden eaves, pale limestone chimney. Classic finely textured pixel-art 2D RPG, same overhead front-facing perspective, no isometric rotation. CRITICAL preserve exact canvas proportions, scale, silhouette, position and ALL empty space: image is 512x512 sheet; actual roof occupies x0..255 y49..319, whole right half and bottom are empty. Chimney at x56..88 y49..108, ridge near x128 y76, eaves near y270, small lower roof strip ends y319. Do not enlarge or center the object. Keep every edge and roof plane at identical coordinates. This will replace a 256x320 indexed rectangle at top-left in game. Make it visibly more beautiful and different from brown tiles while keeping geometry. Empty pixels/background pure black (#000000), no environment, no text, no cast shadow outside sprite. One replacement sheet.

### 5502
Use case precise-object-edit. Repaint this exact Argentum Online modular house front wall asset sheet in premium classic 2D RPG pixel art. Preserve layout exactly. Square 256x256 canvas. Wall side posts x9..18 and237..246 from y0..165, thin horizontal wooden beam at y87..96; everything else above y87 pure black empty, NOT a roof. Front stone wall x19..237 y97..159 with doorway opening x145..209 y101..159 remaining PURE BLACK. Same doorway position, dimensions, wall extent and silhouette, do not center/enlarge. Redesign stones as pale weathered warm limestone, beautifully carved deep walnut beams, elegant warm amber wall lantern in the existing x77..96 y106..143 position. Restrained copper fittings, fine textured medieval craftsmanship. Strong depth shaded under lintel, no new door, no landscape, no floor, no extra roof, no text. Black empty pixels stay #000000; preserve bottom empty 90 pixels. Paired architecture uses dark teal slate roofs.

### 5501
Use case precise-object-edit. Repaint exact modular Argentum Online interior back wall sprite on square256x256 sheet. Preserve original geometry/position/scale: wall x9..246 y27..98, fireplace chimney at x54..88 from y21 down to97, completely empty black sheet below y99. Do not center or enlarge. Original front facing overhead RPG perspective. Beautiful warm ivory plaster between carved deep walnut timber uprights and diagonal corner braces; fireplace pale warm limestone matching medieval exterior wall, tiny amber fire at same existing x57..84 y92..98 location, weathered copper decorative fittings. Fine detailed classic fantasy pixel art, distinctive crafted architecture, subdued warm palette compatible with dark teal slate roof. Exact same silhouette, no extra props/floor/roof/text. All empty background solid pure black #000000.

## Reproducir y verificar

Compilar cliente con `dotnet build client/ArgentumNextgen.csproj --no-restore`.
`client/test/render/HouseArtImport.tscn` realiza únicamente el ajuste de resolución
y máscara de espacios vacíos original, fuera del juego. Es idempotente y sobrescribe
solo los tres PNG indicados.
`client/test/render/HouseArtSmoke.tscn` prueba el render real sin conexión, captura
`exterior.png` e `interior.png`, y sale. Ejecutar con Godot .NET gráfico, no headless.
Se comprobó visualmente el ensamblaje y la ocultación del techo. No es una prueba
de navegación de servidor ni de todos los mapas que reutilicen estas piezas.
