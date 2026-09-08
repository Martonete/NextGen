# Descarga Eléctrica

Impacto procedural de 0,55 segundos: rayos cortos ramificados blanco-azulados
rodean el cuerpo y cambian su recorrido cada 75 ms. Sin chispas expulsadas:
también se omite el siguiente impacto genérico 106 enviado después de FX 11
y se ocultan las chispas reactivas de daño mientras está activa la descarga.
No usa círculos, fuego ni columnas ascendentes. Los destellos son locales,
sin parpadeos de pantalla completa.

HECHIZO23 usa exclusivamente FX 11 en server/dat/Hechizos.dat. Se intercepta
CreateFX 11 en el cliente, sin cambios de protocolo, daño o servidor. Con
Mundo reactivo o partículas desactivados se mantiene el sprite clásico.

WorldRenderer.ElectricDischarge.cs usa la capa aditiva existente, debajo de
techos. Sigue al objetivo interpolado; respeta visibilidad, pausa y calidad
(3/5 rayos). No crea nodos ni arrays por frame. Un temporizador
por personaje impide acumular emisores al repetir lanzamientos. ClearFX,
invisibilidad, cambio de mapa y bloqueos largos del render cancelan el efecto.

RenderSmoke verifica activación mediante paquetes, independencia respecto de
Apocalipsis, repetición, expiración, cancelación y fallback clásico; captura
electric-impact.png y electric-arcs.png para revisión visual.
