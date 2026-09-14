# Túnica Guarnieri

Objeto **1676**, cuerpo **518**, textura `resources/data/Graficos/45006.png`.
32 cuadros distintos: 8 por dirección, orden norte/este/sur/oeste.
Duración del ciclo: 444 ms, igual al catálogo actual del Nigromante; no cambia la velocidad de desplazamiento.
Defensa 16–22, como la túnica del Nigromante. No agrega restricciones de clase/raza.

Aura **104**, **Vigilia Guarnieri**: cinco fuegos amatista con estelas cortas,
órbita dividida entre las pasadas trasera y delantera. Estilo procedural 6,
sin emisores ni asignaciones de arreglos por cuadro. Se configura en `INIT/Auras.dat`.

Para obtenerla con un GM: `/ITEM 1676 1`, o buscar `Guarnieri` con `/ITEMS Guarnieri`.
Para probar solo el aura: `/aura 104`; terminar la previsualización: `/aura off`.
Reabrir el cliente y reiniciar el servidor si estaban abiertos al actualizar los datos.

El arte se generó con la herramienta integrada de imágenes y se corrigió su
transparencia con la misma herramienta. `guarnieri-source.png` conserva el PNG
con alfa. El importador empaqueta y reduce los cuadros al atlas de 544×256:
celdas de 64×64, cuello anclado, más un icono de 32×32.

Se agregan los GRH 33535–33566 (cuadros), 33567–33570 (ciclos), 33571 (icono).
El importador comprueba colisiones y conserva los registros existentes.
Los tres `obj.dat` (servidor, recursos y override local del cliente) incluyen el objeto.
El World Editor consume los recursos fuente y puede resolver el objeto y su gráfico.

Verificación reproducible después de compilar el cliente:

```powershell
& '../Godot_4.4_mono/Godot_v4.4-stable_mono_win64/Godot_v4.4-stable_mono_win64_console.exe' --path client --audio-driver Dummy res://test/render/GuarnieriSmoke.tscn
```

`-- --import` se usa una sola vez sobre el catálogo anterior; aborta si los IDs
ya existen. `-- --refresh-art` reempaqueta exclusivamente el arte Guarnieri y su
anclaje de cabeza tras comprobar el cuerpo propietario. La ejecución normal
solo verifica y captura vistas equipadas en las cuatro direcciones.

Prompt de generación: ver [prompt.md](prompt.md).
