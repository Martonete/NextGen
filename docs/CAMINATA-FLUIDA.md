# Caminata: cadencia Argentum 20

Desde 2026-09-12 la caminata replica `engine.bas` de AO20 (ver `docs/PARIDAD-AO20.md`):

- `timerTicksPerFrame = elapsedMs * 0.018`; cámara y personajes avanzan
  `8.5 * ticks * Speeding` px por frame. Un tile de 32 px tarda **~209 ms** a velocidad 1.
- Se integra con el delta real del frame y el sobrante del último frame se descarta, igual
  que AO20. A 60 FPS un paso son 13 frames (216,7 ms), a 144 FPS 31 (215 ms), a 30 FPS 7
  (233 ms). Si alguna vez molesta, el único cambio es restar 32 en vez de resetear a 0.
- Orden por frame: movimiento (render) y después teclado (`Check_Keys`), así un paso que
  termina se encadena con el siguiente sin pose de reposo. La primera frame sin paso congela
  la serie walk (Idle inmediato).
- Piernas: ms por frame = `(Grh.speed \ NumFrames) / Speeding`, clamp [40, 220]
  (`ApplySpeedingToChar`). Arma y escudo comparten la fase del cuerpo.
- Un clamp de 500 ms por frame evita teletransportes tras un stall (AO20 no lo tiene; inocuo).

Lo anterior (tick fijo a 240 Hz, 120 px/s de ArgentumOnlineGodot, gracia de 65 ms) quedó
reemplazado.

## Armaduras recortadas (nigromante y similares)

Se registran una sola vez al cargar las tiras horizontales con recortes variables.
El anclaje se reconstruye a partir de la celda original de la lámina, no del centro
de cada recorte: así una capa que se ensancha no desplaza el cuello respecto de la
cabeza. Las tiras uniformes y las distribuciones no reconocidas conservan el
comportamiento previo. No se modifican los índices ni las imágenes fuente.

Cuerpo y sombras/reflejos comparten la corrección. Arma y escudo usan la fase
normalizada del cuerpo en vez de reutilizar su número de cuadro.

Verificación: `client/test/render` (`WalkContinuityTests`) y `WalkMovementSmoke` dentro de
`RenderSmoke.tscn` (40 pasos a 20/30/60/144/240 FPS con la cadencia AO20).
