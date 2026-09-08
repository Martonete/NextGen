# Caminata: continuidad visual

La cámara conserva su desplazamiento fraccionario, igual que el personaje. Se
elimina el desajuste de hasta medio píxel causado por redondear solamente la cámara.
Se mantiene el filtro de texturas existente, sin agregar desenfoque ni rebote.

El ciclo de piernas, equipo, sombras y reflejos continúa entre baldosas y al girar.
El frame en que termina el desplazamiento también cuenta para la animación. Una
pausa visual de hasta 65 ms conserva la última pose sin seguir animando las piernas;
una detención real vuelve al reposo. Este estado es exclusivamente visual y no
prolonga `Moving` ni la meditación.

Sin cambios en velocidad (`8 * 0.0172` píxeles/ms), límite de delta de 50 ms,
orden de lectura de teclado, duración de cada paso, colisiones ni paquetes.
No se introduce anticipación de posición ni una cámara que persiga con retraso.

Verificación: tests puros de continuidad a 20/30/60/144/240 FPS y prueba offline
`WalkMovementSmoke` dentro de `RenderSmoke.tscn`, que ejecuta el `UpdateMovement`
real sobre 40 pasos por frecuencia, con cuatro direcciones. Comprueba duración
original, posición relativa cámara/personaje, ausencia de pose de reposo entre
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
