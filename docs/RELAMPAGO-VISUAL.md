# Relámpago: descarga fulminante celestial

Efecto procedural de 0,44 segundos sobre el objetivo: un rayo de plasma eléctrico
compacto que desciende desde justo por encima de la cabeza (~75px) e impacta en el torso,
con filamentos gemelos entrelazados (*streamers*), nodos de resplandor suave en los vértices,
arcos envolventes que abrazan la silueta del personaje, anillo de choque electromagnético
y chispas ionizadas en el suelo.

## Identidad visual y proporciones
- **Escala compacta:** Nace a la altura del personaje (~75px arriba de su centro) evitando
  trazos excesivamente largos que invadan la pantalla.
- **Filamentos dobles entrelazados:** Núcleo de plasma blanco puro envuelto en una vaina
  cian brillante y una estela secundaria que le da volumen y dinamismo.
- **Nodos de voltaje y arcos corporales:** Destellos radiales suaves en las curvas del rayo
  y dos micro-arcos que abrazan la silueta del personaje al impactar.
- **Colores:** Azul zafiro profundo de halo (`#1A60FF`), cian eléctrico de alto voltaje (`#59E0FF`)
  y núcleo blanco incandescente (`#F2FDFF`).

## Integración con el protocolo
- Se activa al recibir `CreateFX 102`, exclusivo de `HECHIZO12` en `server/dat/Hechizos.dat`.
- Sin cambios en el protocolo, daño o lógica de servidor.
- Reemplaza el sprite clásico cuando *Mundo Reactivo* y *Partículas* están habilitados;
  con partículas desactivadas se conserva el sprite clásico original.

## Rendimiento y capas
- Implementado en `WorldRenderer.Lightning.cs` dentro del paso aditivo de glow.
- Sigue la posición interpolada del objetivo; respeta visibilidad, niebla de guerra y pausa.
- Totalmente analítico: 0 allocations por frame (sin nodos Godot ni arrays temporales).
- Escala de calidad según `PerformanceLevel` (6/12/18 chispas de impacto y ramificaciones adaptativas).
- Temporizador independiente por personaje (`ch.LightningTime`); se cancela con `ClearFX` (FX 0).
