# Caminata: continuidad visual

La cámara conserva su desplazamiento fraccionario, igual que el personaje. Se
elimina el desajuste de hasta medio píxel causado por redondear solamente la cámara.
Se mantiene el filtro de texturas existente, sin agregar desenfoque ni rebote.

El ciclo de piernas, equipo, sombras y reflejos continúa entre baldosas y al girar.
El frame en que termina el desplazamiento también cuenta para la animación. Una
pausa visual de hasta 65 ms conserva la última pose sin seguir animando las piernas;
una detención real vuelve al reposo. Este estado es exclusivamente visual y no
prolonga `Moving` ni la meditación.

No se introduce anticipación de posición ni una cámara que persiga con retraso.

## Cadencia: método de ArgentumOnlineGodot

El paso se rige por el cliente de referencia (`engine/character/character.gd`):
`const Speed = 120.0` aplicado con `move_toward`. Es decir **120 px/s**, una
baldosa de 32 px en **266,7 ms**. Reemplaza al `8 * 0.0172` píxeles/ms de VB6
(232,6 ms) y al límite de delta de 50 ms.

La integración es a tick fijo con acumulador, y eso es lo que garantiza que la
velocidad sea idéntica en cualquier monitor: el paso termina siempre en un
número entero de ticks y el resto se arrastra al cuadro siguiente. Avanzar por
delta de cuadro perdería la fracción sobrante en cada baldosa —a 20 FPS, hasta
50 ms por paso, casi un 20% de velocidad de caminata.

El tick corre a **240 Hz** (0,5 px, 64 ticks por baldosa) en vez de los 60 Hz de
la referencia, que los hereda de `_physics_process`. La tasa en sí es invisible
mientras la baldosa se divida exacta, y a 240 Hz un monitor de 144 recibe una
posición nueva por cuadro en lugar de repetir imágenes. La animación de piernas
avanza con el tiempo real de cuadro, porque allá el ciclo es un `AnimatedSprite2D`
y `_physics_process` solo elige entre caminar y reposo.

El orden de lectura del teclado también es el de `_CheckKeys`: Oeste, Este,
Norte, Sur, y gana la primera tecla que esté apretada. Un paso solo arranca
cuando el anterior terminó; se quitó la condición extra de `PendingMoves < 2`.

Se conservan tres cosas que la referencia no puede darnos porque su servidor no
es el nuestro: el giro local inmediato contra una traba (el nuestro difunde el
rumbo con `ToAreaButIndex`, sin devolvérselo al que gira), la predicción local
del propio paso (el servidor tampoco reenvía tu `CharacterMove`) y las reglas de
`LegalPos` de VB6, que deben coincidir con la validación del servidor.

Verificación: tests puros de continuidad a 20/30/60/144/240 FPS y prueba offline
`WalkMovementSmoke` dentro de `RenderSmoke.tscn`, que ejecuta el `UpdateMovement`
real sobre 40 pasos por frecuencia, con cuatro direcciones. Comprueba la duración
de 266,7 ms, posición relativa cámara/personaje, ausencia de pose de reposo entre
pasos, limpieza de pendientes y detención. No se conecta ni guarda personajes.

## Armaduras recortadas (nigromante y similares)

Se registran una sola vez al cargar las tiras horizontales con recortes variables.
El anclaje se reconstruye a partir de la celda original de la lámina, no del centro
de cada recorte: así una capa que se ensancha no desplaza el cuello respecto de la
cabeza. Las tiras uniformes y las distribuciones no reconocidas conservan el
comportamiento previo. No se modifican los índices ni las imágenes fuente.

Cuerpo y sombras/reflejos comparten la corrección. Arma y escudo usan la fase
normalizada del cuerpo en vez de reutilizar su número de cuadro; cambiar a un
cuerpo con otra cantidad de cuadros también conserva la fase.

`ArmorWalkSmoke.tscn` valida las cuatro direcciones del cuerpo 512 y produce una
lámina de los 32 cuadros para inspección visual. Las pruebas de desplazamiento
incluyen cuerpos 1, 512 y 513, con la misma duración de pasos que antes.

### Encaje de cabeza en las dos túnicas

Los cuerpos 512 y 513 registrados reciben un ajuste visual de cabeza/casco de
2 píxeles hacia abajo para asentar el cuello dentro del cuello de la prenda.
Se aplica también a sombras compuestas y reflejos, sin desplazar el cuerpo ni
cambiar la cadencia. No afecta otras armaduras, monturas, navegación o muertos.
`ArmorWalkSmoke.tscn -- --orange` permite inspeccionar la túnica naranja; sin
argumentos se inspecciona el nigromante. Capturas `armor-head-512.png` y
`armor-head-513.png` en el directorio de usuario de Godot.
