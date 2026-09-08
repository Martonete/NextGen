# Ventanas del HUD y accesos rápidos

- **Mochila**, **Estado**, **Macros**: botones superiores para mostrar u ocultar
  las tres ventanas. Arrastrar desde la cabecera; la cruz oculta sin perder la posición.
- La disposición se guarda localmente en `user://floating-hud.cfg`.
- La barra tiene diez slots, inicialmente asociados a 1–9 y 0.
- Seleccionar un hechizo en la lista o un objeto en el inventario, hacer click
  en un slot y elegir **Equipar hechizo seleccionado** o **Equipar objeto seleccionado**.
  Si no hay selección válida, el menú permanece abierto y explica qué falta.
- **Cambiar tecla** captura una tecla; Escape cancela. Se rechazan duplicados,
  modificadores y teclas ya usadas por movimiento, acciones o paneles.
- La tecla del slot prepara el hechizo para apuntar normalmente, o equipa el objeto.
  No repite automáticamente. Un objeto ya equipado no se desequipa al pulsar otra vez.
- **Vaciar slot** elimina la asignación. Los accesos se guardan por cuenta/personaje
  en archivos `user://quickbar-<hash>.cfg`, sin contraseñas ni cambios del servidor.
- Las asignaciones usan IDs, no posiciones: reordenar la mochila o los hechizos
  no las rompe. Los recursos ausentes se muestran atenuados.
- No se activan al escribir, durante formularios modales, pausado o muerto.
  Las macros numéricas anteriores solo se suprimen para teclas ocupadas por la nueva barra.

## Verificación

Compilar el cliente y ejecutar `res://test/render/FloatingHudSmoke.tscn` en Godot.
La prueba monta el HUD real sin conectarse: valida selección por ID, reordenamiento,
objetos ausentes, bloqueo por chat, reasignación, tecla reservada, persistencia y
mostrar/ocultar. Produce capturas `user://floating-hud-smoke.png` y
`user://floating-hud-menu-smoke.png`. Utiliza un perfil de prueba separado.
