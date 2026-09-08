# Apocalipsis: detonación ígnea

Efecto procedural de 0,70 segundos sobre el objetivo: explosión compacta y
redondeada de fuego rojo/naranja, destello inicial, hollín y fragmentos
incandescentes frenados cerca del cuerpo. Sin círculos, runas, violetas ni columnas ascendentes:
una identidad explosiva distinta de la meditación.

Se activa al recibir CreateFX 13, exclusivo de HECHIZO25 en Hechizos.dat.
El servidor ya emite ese paquete para objetivos jugadores y NPC; no cambia
el protocolo ni el daño. Reemplaza el sprite clásico cuando está habilitado;
con Mundo reactivo o partículas desactivados se conserva el sprite clásico.
Si se cambia la asignación FX del hechizo, actualizar PacketHandler.Combat.cs.

WorldRenderer.Apocalypse.cs dibuja sobre las capas existentes: suelo debajo
del personaje, luz/partículas encima y debajo de los techos. Sigue la posición
interpolada del objetivo, incluso en impactos letales mientras exista el personaje.
No dibuja sobre invisibles ni fuera del área visible.

Respeta Mundo reactivo/partículas y pausa. Calidad baja/media/alta: 12/24/40
brasas calculadas analíticamente, sin crear nodos o arrays por frame.
Cada objetivo mantiene un único temporizador; un nuevo impacto lo reinicia.
Se cancela con ClearFX, cambio de mapa, invisibilidad o una pausa larga del
render. Eliminar el personaje elimina también su estado del efecto.

Verificación: compilación C#, suite RenderLogicTests y escena RenderSmoke.
La escena inyecta paquetes CreateFX reales, comprueba exclusividad, expiración,
repetición y cancelación, y captura apocalypse-impact.png/apocalypse-embers.png.
