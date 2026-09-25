# Forja Negra y Corona Invernal

Seis piezas independientes, equipables y combinables. Arte creado con la
herramienta integrada de imágenes; los PNG fuente conservan transparencia real.
Los prompts completos están en [prompts.md](prompts.md).

| Objeto | Nombre | Índice de equipo | PNG final | GRH icono |
|---|---|---|---|---|
| 1677 | Armadura del Juramento Volcanico | Cuerpo 519 | 45007 | 33608 |
| 1678 | Armadura del Centinela Glacial | Cuerpo 520 | 45008 | 33648 |
| 1679 | Escudo de la Caldera | Escudo 47 | 45009 | 33672 |
| 1680 | Escudo de la Estrella Boreal | Escudo 48 | 45010 | 33712 |
| 1681 | Yelmo de la Forja Negra | Casco 113 | 45011 | 33736 |
| 1682 | Yelmo de la Corona Invernal | Casco 114 | 45012 | 33776 |

Los PNG finales están en `resources/data/Graficos/`. Se conservan además los seis
fuentes y sus atlas en esta carpeta. Armaduras: ocho cuadros por dirección,
escudos: cuatro por dirección, cascos: cuatro vistas estáticas. Ciclo de 444 ms;
se conserva la velocidad de desplazamiento. El cliente sincroniza los cuadros de
los escudos con la fase de las armaduras mediante el sistema de equipo existente.

Los tres catálogos de objetos (servidor, recursos y override del cliente) tienen
las mismas entradas. Defensa: armaduras 28–35, escudos 5–8, cascos 10–20.
No agregan restricciones de clase ni raza, ni bonificaciones de conjunto.

Como GM: `/ITEM 1677 1` (cambiar el número hasta 1682). También se pueden localizar
por nombre con `/ITEMS`. Reiniciar el cliente para cargar los nuevos catálogos.

Verificación offline después de `dotnet build client/ArgentumNextgen.csproj --no-restore`:

```powershell
& '../Godot_4.4_mono/Godot_v4.4-stable_mono_win64/Godot_v4.4-stable_mono_win64_console.exe' --path client --audio-driver Dummy res://test/render/ForgeFrostSmoke.tscn
```

Comprueba entradas activas, correspondencia cliente/servidor, dimensiones, alfa,
cuadros distintos y movimiento a 20/30/60/144/240 FPS. Captura ocho previews con
equipo completo, sin equipo y con piezas cruzadas en las cuatro direcciones.
`-- --import` sirve solo para la primera importación sobre el catálogo con la
Guarnieri; aborta ante colisiones. `-- --refresh-art` reempaqueta estos seis atlas
tras comprobar que sus GRH pertenecen a las texturas esperadas.

## Ajuste de reposo y escudos

El cuadro frontal 0 usa `volcanic-idle.png` / `glacial-idle.png`, dibujados con
ambos pies apoyados y separados. Se registra sobre el mismo cuello y a 44 px de
alto. Los otros 31 cuadros de cada armadura permanecen intactos; la actualización
verifica esa conservación antes de copiar los atlas. No cambia el ciclo ni la velocidad.
Los escudos suben 4 px y se acercan al centro: anclas X norte/este/sur/oeste
25/34/38/30 dentro de la celda, conservando el balanceo.

`-- --idle` captura `idle-preview.png` con personajes realmente detenidos. La
verificación comprueba que el cuadro frontal tiene dos botas separadas al pie.
Los gráficos finales actualizados son `45007.png` a `45010.png`.
