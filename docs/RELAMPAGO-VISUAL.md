# Relámpago: rayo de tormenta sobre el objetivo

Efecto procedural de 0,70 s (`CreateFX 102`, exclusivo de `HECHIZO12`): un rayo alto y
anguloso y compacto (~60 px sobre la cabeza, unos 2 tiles) e impacta en el torso.

## Secuencia
1. **Líder escalonado (0–0,05 s):** canal fino y tenue que baja parpadeando desde arriba.
2. **Descarga principal (0,05 s):** canal a plena potencia — halo azul, vaina celeste y
   núcleo blanco — con 1–2 ramas laterales que mueren en el aire, bloom blanco en el
   pecho, destello sobre el piso y anillo de choque que se expande.
3. **Dos re-descargas (0,21 s y 0,36 s):** cada una con **su propia geometría de canal**
   y menor intensidad (75 % y 55 %), como el parpadeo de un rayo real.
4. **Arcos residuales (0,12–0,70 s):** chispas que saltan desde el pecho y 2–3 arcos
   recorriendo el cuerpo mientras se apaga.

## Decisiones de diseño
- **La forma se mantiene fija durante cada descarga**; la versión anterior movía la
  geometría cada 40 ms y se leía como un garabato. El ruido es determinista
  (`LightningNoise(seed, i)`), sin estado aleatorio.
- Quiebres angulosos con amplitud decreciente hacia el objetivo: nace desplazado del
  centro (viene de la tormenta) y aterriza exactamente en el pecho.

## Integración
- Sin cambios de protocolo ni de servidor. Reemplaza el sprite clásico cuando *Mundo
  Reactivo* y *Partículas* están habilitados; si no, se conserva el FX clásico.
- `WorldRenderer.Lightning.cs`, pasada aditiva de glow; sigue la posición interpolada,
  respeta niebla/visibilidad/pausa, 0 allocations por frame. `ClearFX` (FX 0) lo cancela.
- Calidad según `PerformanceLevel`: 8/14/20 chispas, tercera rama y tercer arco a partir de Med.

## Verificación
`res://test/render/LightningSmoke.tscn` genera `user://lightning-strip.png`: una tira de
9 instantes del efecto sobre un personaje para ajustar el aspecto sin correr la suite completa.
