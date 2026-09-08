# Cripta de la Vigilia — mapa 205

Dungeon PvE de 100×100, con 2310 tiles transitables conectados, ocho salas,
18 enemigos, cuatro arquetipos nuevos y un jefe. No requiere cuenta GM.

## Cómo entrar y salir

- Tanaris (mapa 28): pisar **55,48**, al pie del nuevo obelisco.
- Llegada: mapa 205, **50,87**, en el vestíbulo sin enemigos iniciales.
- Regreso: pisar **50,89**, entre las dos lámparas del sur. Sale a Tanaris
  **54,48**, sin aterrizar en otro teletransporte.
- El mapa queda disponible en World Editor como Mapa205.aomap.

## Recorrido

Vestíbulo → Galería de la Vigilia → Sala del Juramento → Trono del Custodio.
El Osario y la Capilla Rota forman la ruta occidental; el Depósito Funerario
y la Guardia Sepulcral forman la oriental. Ambas vuelven al recorrido principal.
Las rutas laterales contienen reservas de pociones azules y rojas. Los cofres
abiertos son decoración, no contenedores interactivos.

La iluminación pasa de ámbar a tonos fríos; el trono tiene niebla tenue.
Las lámparas iluminan cinco tiles, no toda la sala. Las tumbas y obeliscos
están bloqueados; los pasillos mantienen tres tiles de ancho.

## Enemigos y recompensas

Templates aislados en `server/dat/NPCs-VIGILIA.dat` (1100–1103), cargados junto
a las bases originales sin modificar sus NPC. Todos reaparecen mediante el
sistema existente.

| Enemigo | HP | Papel |
|---|---:|---|
| Centinela de la Vigilia | 450 | Primera línea cuerpo a cuerpo |
| Sepulturero Maldito | 800 | Guardia resistente |
| Acólito del Silencio | 650 | Descarga Eléctrica |
| Custodio de la Última Vigilia | 6500 | Jefe: Paralizar y Descarga Eléctrica |

El jefe tiene oro base 1800–2500, experiencia base 35000 y pociones en su
tabla de botín. Se aplican los multiplicadores y probabilidades existentes
del servidor; el inventario de botín no implica entrega garantizada de todo.
Recomendado inicialmente para personajes equipados de nivel 30–40 o un grupo;
esta recomendación es provisional hasta probar el combate con jugadores.
PvP desactivado tanto en el mapa como en sus ocho zonas; las criaturas sí atacan.

## Verificación y mantenimiento

- `cargo test --bin ao-server vigilia_map`: carga con parser real, conectividad,
  templates, 18 spawns, entrada/regreso, muerte y reaparición del jefe.
- `DungeonWorkshop.tscn -- --preview-vigilia`: comprueba gráficos y recarga,
  captura las ocho salas, entrada y plano con el renderer real. No conecta cuentas.
- `--build-vigilia` crea los binarios si faltan; rechaza reemplazar un dungeon
  diferente. `--refresh-vigilia` **sobrescribe solo el mapa 205 generado**:
  no usar después de editarlo a mano sin respaldarlo.
- `VigiliaBuilder` conserva un diseño reproducible; no se ejecuta al jugar.
- Los recursos nuevos se distribuyen como archivos sueltos en
  `resources/data/Maps`, mediante el proveedor de overrides existente. Se preservó
  el `maps.aopak` que ya tenía cambios del usuario. No hay nuevos GRH ni texturas.
- Tanaris cambia solamente dos registros: obelisco/bloqueo en 55,47 y salida
  en 55,48. El resto de los bytes de los archivos se conserva. Copias originales:
  `%APPDATA%/Godot/app_userdata/Argentum Nextgen/dungeon-workshop/town-backup`.

Capturas en `%APPDATA%/Godot/app_userdata/Argentum Nextgen/dungeon-workshop`.
No se realizó una sesión multijugador ni una prueba completa de balance.
