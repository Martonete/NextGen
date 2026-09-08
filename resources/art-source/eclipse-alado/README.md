# Armadura del Eclipse Alado

Armadura nueva: objeto **1670**, cuerpo **514**, aura **97**.
Atlas de juego: `resources/data/Graficos/45000.png` (RGBA 288 x 256).
GRH estaticos 33400..33415; animaciones N/E/S/O 33416..33419;
icono de inventario 33420. Cuatro cuadros por direccion, ciclo de 444 ms.
La velocidad de desplazamiento no cambia. Defensa 28..35, valor 180000,
sin restricciones adicionales de clase/raza respecto de una armadura generica.

## Probar

Reabrir el cliente para cargar los indices. Como Administrador, ejecutar
`/LOADOBJ` para recargar los objetos del servidor sin reiniciarlo, y luego
`/ITEM 1670 1`. Equipar desde el inventario. Si habia una previsualizacion
manual de aura activa, quitarla con `/aura off` para ver la del equipo.
Tambien se encuentra como "Armadura del Eclipse Alado" en el buscador de items.
No se agrego a tiendas ni tablas de drops.

## Arte y preparacion

Arte generado con la herramienta integrada de imagenes (habilidad imagegen),
con iteraciones para la espalda y la transparencia. `source.png` es el PNG
seleccionado con alfa real; `preview.png` muestra los 16 cuadros renderizados
por el cliente con cabeza y aura. El atlas se normaliza por cuello y apoyo
de pies, con escalado nearest-neighbor y celdas de 64 x 64. La cabeza pertenece
al personaje, no esta dibujada dentro de la armadura.

Prompts utilizados:

1. Hoja de sprites 4 x 4 de una armadura sin cabeza, de acero oscuro y plata,
   con cristales violetas y alas de plumas oscuras bordeadas de violeta, estilo
   Argentum Online. Filas norte, este, sur, oeste; cuatro fases de caminata,
   cuello y pies alineados; fondo con alfa real, sin armas, escudo, texto ni grilla.
2. Corregir solo la fila superior para mostrar espalda, placa posterior y
   nacimiento de alas; preservar las otras tres filas, posiciones y transparencia.
   Alternar la zancada lateral en el tercer cuadro de la segunda fila.
3. "BACKGROUND EXTRACTION ONLY. Remove the baked white/gray checkerboard entirely
   and output this exact armor animation sheet with TRUE TRANSPARENT ALPHA channel,
   alpha zero outside armor silhouettes and in gaps. Do not draw any checkerboard.
   Preserve ALL armor pixels, same 16 headless poses, same 4 by 4 cell positions,
   same dimensions, no resizing, no change to subject. Production PNG sprite sheet
   on actual transparency."

## Verificacion y reproduccion

Compilacion C# y escena `res://test/render/ImportWingedArmor.tscn` verifican
carga de cuerpo/objeto/animaciones y producen la captura. La opcion `--import`
es exclusivamente para la importacion inicial: comprueba colisiones de IDs,
conserva indices anteriores en `original-index/` y se niega a reemplazar un atlas
ya existente. No ejecutar esa opcion de nuevo sobre el catalogo importado.

Para distribuir con paquetes, regenerar los paquetes de graficos e INIT con
el flujo habitual; el entorno local consume estos archivos sueltos.
