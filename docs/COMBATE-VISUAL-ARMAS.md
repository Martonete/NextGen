# Identidad visual del combate físico

Efectos de contacto confirmados por el servidor, visibles para el área del atacante:
espadas (corte curvo), hachas/martillos/mazas/bastones (impacto pesado), dagas
(estocada), proyectiles (mordida de flecha y cuerda del tirador) y contacto neutro
para armas no reconocidas o puños. La clasificación usa el arma equipada al
resolver el golpe; no depende del arma que el cliente seleccione después.

Se emiten solamente en las ramas acertadas de ataques de usuarios contra NPC y
PvP, cuerpo a cuerpo y a distancia. No se reinterpretan números flotantes ni FX
de sangre, por lo que los hechizos no producen cortes. Los ataques propios de
NPCs/bots conservan su feedback existente. No se modifican daño, azar, cadencia
ni desplazamiento. El crítico dorado corresponde al crítico real existente
(Bandido + Espada Vikinga), no a un umbral inventado de daño. Se mejora el evento
base sin duplicarlo. No se agregan críticos a otras clases ni a arcos.

Los FX 201..206 son una extensión visual de CreateFX de siete bytes, documentada
en ao-protocol.md. Requieren el cliente actualizado; se conservan sangre,
proyectil y sonidos previos. No usan slots de hechizos. La dirección del arco
aprovecha el proyectil recién recibido, incluyendo diagonales, con heading como
fallback. La cuerda señala un disparo acertado confirmado, no predice el input.

Duración 0,18–0,32 s. Instantánea del punto de contacto para que una muerte no
borre el golpe. Cancelación por mapa, desconexión, invisibilidad, opciones o
pausa larga; pausa normal congela los tiempos. Máximo 128 eventos en memoria y
24/64 visibles según calidad. Se controla con Mundo reactivo y Partículas.
Geometría sin texturas nuevas, nodos emisores ni asignaciones durante el dibujo.

Pruebas: `cargo test weapon_visual_classification`; escena offline
`res://test/render/WeaponImpactSmoke.tscn` para paquetes fragmentados, críticos,
hechizos, invisibilidad, límites, pausa, muerte, expiración, mapa y opciones.
La escena guarda `user://weapon-impacts.png` con muestras visuales.
