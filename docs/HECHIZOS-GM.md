# Hechizos para GM

Todos los personajes con privilegios mayores que cero pueden aprender hechizos
mediante pergaminos sin requisito de clase mágica, hambre o sed, y lanzarlos
sin arma equipada, habilidad mágica mínima o potencia de báculo. Se conserva
la exención previa de maná/energía y se permite Mimetiza sobre NPC sin ser druida.

`/HECHIZO ID` agrega un hechizo existente al primer espacio libre del propio
libro de cualquier GM. No borra hechizos, no duplica entradas ni aumenta el
límite del libro. Por ejemplo: 23 Descarga Eléctrica, 24 Inmovilizar, 25 Apocalipsis.
La sintaxis anterior `/HECHIZO Nombre ID` sigue disponible; dar a otro personaje
requiere Administrador, como antes. Los IDs inexistentes se rechazan.

No cambian los requisitos de jugadores normales ni las validaciones de muerte,
alcance, objetivo, zonas seguras, inmunidades, mascotas o cooldowns.
La prueba `gm_spell_access_keeps_player_restrictions` usa datos locales y una
base de datos no conectada: verifica aprendizaje, lanzamiento GM sin recursos,
bloqueo del jugador y permisos para otorgar a terceros.
