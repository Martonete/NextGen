# Mundo reactivo

Implementación del cliente, 6 de septiembre de 2026. Activada por defecto; se controla desde
**Opciones → Video → Mundo reactivo**, junto con las opciones existentes de Partículas y Auras.

## Qué incorpora

- **Pasos:** pequeñas nubes de polvo sobre terreno natural sin L2 ni triggers interiores.
  La emisión depende de la distancia recorrida (cada 18 píxeles), con alternancia de pies.
- **Agua:** ondas elípticas expansivas, estela y gotas al desplazarse sobre agua o entrar
  en ella. Se dibujan antes de las máscaras de tierra y L2 para respetar las costas.
  También respeta la opción de efecto de agua.
- **Impactos:** ráfagas doradas con trayectorias y caída al recibir el evento de golpe
  que ya activa el flash del personaje. Un contador distingue los eventos del temporizador
  visual para no repetir el impacto en cada frame.
- **Meditación:** círculo rúnico del personaje local, con ocho marcas giratorias,
  anillo interior en sentido contrario, pulso de activación y motas azules ascendentes.
  Se forma progresivamente y se desvanece al terminar estando quieto. Al empezar a moverse,
  el círculo y sus partículas se cortan inmediatamente; no reaparecen al detenerse hasta
  iniciar una nueva meditación. El servidor sigue decidiendo
  cuándo el personaje está meditando; el efecto no cambia la recuperación de maná.

Los gráficos son procedurales y reutilizan una textura radial pequeña. No requieren instalar
assets, modificar índices GRH ni reempaquetar archivos. Los efectos no alteran estadísticas,
colisiones, velocidad de movimiento, protocolo ni decisiones del servidor.

## Arquitectura y limpieza

- `ReactiveEffects.cs`: simulación independiente de Godot, posiciones en píxeles del mundo,
  máximo de 192 emisores y un arreglo reutilizable de 512 partículas. Calidad Media usa
  192 partículas; Mínima/Baja permite como máximo 64 si se habilitan manualmente partículas.
- `WorldRenderer.Reactive.cs`: conexión con personajes, configuración, mapa y dibujo.
  Agua bajo las máscaras de terreno; polvo/círculo bajo personajes; motas/chispas en la
  capa aditiva existente, bajo los techos.
- La simulación avanza desde `_Process`; dibujar no modifica edades ni genera partículas.
- Invisibilidad, muerte, salida del área visible y eliminación del personaje retiran sus
  partículas. Los cambios de mapa y la desactivación limpian toda la simulación.
- Correcciones de posición mayores a 96 píxeles no dejan estelas entre los puntos.
  Una pausa larga de más de medio segundo descarta emisiones acumuladas.
- La textura radial se libera al salir del árbol. No se crean nodos por partícula.
- También se eliminó el uso de `IEnumerable<int>` y del delegado de ordenación creado
  repetidamente en el recorrido de personajes: se conserva la lista concreta y se reutiliza
  la comparación, reduciendo asignaciones transitorias en el render.

## Pruebas

```powershell
dotnet run --project client/test/render/RenderLogicTests.csproj
dotnet build client/ArgentumNextgen.csproj
& '..\Godot_4.4_mono\Godot_v4.4-stable_mono_win64\Godot_v4.4-stable_mono_win64_console.exe' --path client --audio-driver Dummy res://test/render/RenderSmoke.tscn
```

La suite incluye los 11 casos de render anteriores y 17 casos del sistema reactivo:
inicio sin emisiones falsas, pasos, invisibilidad, teletransporte, unicidad de impactos,
eliminación de personajes, entrada al agua, ciclo de meditación, pausa, FPS, límites de
multitudes, limpieza y asignaciones de memoria.

Medición local de la simulación: 10.000 actualizaciones con emisión continua y un personaje
no asignaron bytes administrados después del calentamiento. Esa medición no incluye Godot,
el dibujo ni constituye un benchmark comparativo de FPS del cliente completo.

La escena offline usa assets reales, prueba persistencia/copia de la opción y guarda capturas
de meditación, impacto, pasos, agua e invisibilidad en `user://render-smoke`. Se inspeccionaron
las capturas con el motor OpenGL Compatibility. No se realizó una sesión multijugador.
También se comprobó el arranque normal en 1920×1080. Persisten los avisos de anclas del HUD
y las barras con imágenes ausentes registrados en `RENDER-2026-09-06.md`.

Para probar en juego: meditar quieto, interrumpir la meditación y caminar sobre pasto, navegar
y combatir. No hay requisitos de equipo. La nueva aura es local; no se
infiere el estado de meditación de otros personajes a partir de animaciones.

## Progresión de meditación y F9

| Nivel | Apariencia |
|---|---|
| 1–12 | Círculo azul original, ocho runas y partículas suaves |
| 13–24 | Verde esmeralda, diez runas y un anillo adicional |
| 25–34 | Violeta/celeste, doce runas y marcas orbitales |
| 35–49 | Fuego/dorado, dieciséis runas y mayor emisión |
| 50 | Celestial/dorado, veinte runas, doble espiral ascendente y corona luminosa |

Cada etapa aumenta el radio, la densidad y la duración de las partículas. La etapa nueva
empieza exactamente en 13, 25, 35 y 50. Todas conservan el corte inmediato al moverse.

F9 envía `/SUBIRNIVEL`: está disponible para **todos los personajes**, por decisión de Martín.
El servidor exige una sesión de personaje activa, aplica una sola subida mediante el sistema
normal (estadísticas, puntos y recompensas) y nunca pasa de 50. La tecla no se repite al
mantenerla presionada. Los macros se abren con **Ctrl+F9**. El comando no admite un nombre de
destinatario: solo afecta al emisor. El comando GM `/EDIT` conserva sus permisos anteriores.

Validación añadida: límites de las cinco etapas, atajo público y tope, capturas de niveles
1/13/25/35/50 y una prueba del servidor que sube un usuario sin privilegios desde 1 hasta 50,
comprueba que otro usuario no cambia y que repetir en nivel 50 no duplica recompensas.
