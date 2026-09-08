# Recuperacion del catalogo grafico

El rebase publicado como `9261dd8e` mezclo los indices regenerados remotos con
mapas y assets de la numeracion local. Los IDs seguian siendo validos, pero
resolvian a imagenes distintas: terreno convertido en laminas de personajes,
objetos incorrectos y perdida de las altas 1670..1675.

Se recupera el conjunto de datos compatible de `0aa7f57b`: INIT del cliente y
fuente, mapas fuente/servidor, NPCs y objetos del servidor, graficos.aopak e
init.aopak. maps.aopak ya correspondia a esa revision. Se conserva el codigo
de las herramientas remotas y el soporte de indices anchos.

`resources/data/INIT/GrhCatalog.json` adapta el nuevo lector de metadatos a los
IDs originales de agua, arboles e inventario. El conjunto tiene 99426 como
maximo indice, 517 cuerpos, 85 armas y 1675 objetos, incluyendo las seis piezas
de Vigilia. No copiar encima el GrhCatalog del catalogo renumerado.

## Validacion

Compilar el cliente y ejecutar las escenas offline:

- `res://test/render/CatalogRecoverySmoke.tscn`: Tanaris 28 (33,61), inventario
  con objetos originales/nuevos y fondo animado de login. Guarda capturas en
  `user://catalog-recovery` que deben inspeccionarse visualmente.
- `res://test/render/ImportFiveRelics.tscn` **sin argumentos de importacion**:
  verifica las cinco altas adicionales y sus 80 cuadros de movimiento.

La compilacion sola no detecta este fallo. Ante una futura reindexacion hay
que migrar mapas, objetos, cuerpos, armas, escudos, efectos, particulas,
metadatos y archivos empaquetados juntos, verificando el proveedor compuesto
real: primero client/Data, luego resources/data y finalmente los paquetes.
Reiniciar cliente y servidor despues de cambiar los datos que cargan al inicio.
