# Coleccion Vigilia

Se agregaron seis auras y seis particulas sin reemplazar las existentes.

## Auras para armas

Previsualizacion local dentro del juego: `/aura` comienza en 97 y cada uso pasa
a la siguiente aura disponible. `/aura 102` elige un ID, `/aura anterior`
retrocede y `/aura off` restaura la vista del equipo. Durante la prueba se muestra
solo el aura elegida sobre el personaje y su reflejo. Solo la ve el propio jugador;
no cambia objetos ni envia comandos al servidor. La prueba termina al quitarla
o al recrearse el personaje (por ejemplo, al volver a entrar). Requiere tener las
auras visibles en Opciones y estar fuera de un barco. `/auras` conserva el visor existente.

En el Dateador, asignar el campo `CreaAura` del objeto al ID deseado:

| ID | Nombre | Forma |
|---|---|---|
| 97 | Eclipse de Obsidiana | Orbitas violetas cruzadas |
| 98 | Espinas de Jade | Envolvente vegetal y rombos verdes |
| 99 | Corona Solar | Arcos dorados giratorios |
| 100 | Agujas de Escarcha | Cristales celestes ascendentes |
| 101 | Orbita Astral | Orbitas azules amplias |
| 102 | Ascua Carmesi | Ascuas geometricas ascendentes |

No se modificaron las armas existentes. Sincronizar los datos de objetos del
servidor y cliente mediante el flujo habitual del Dateador al asignarlas.
Recargar los datos del servidor y volver a equipar para ver una asignacion nueva.

Definiciones: `resources/data/INIT/Auras.dat`. Los campos `ProceduralStyle`
(1 orbitas, 2 petalos, 3 arcos, 4 cristales), `Radius`, `Height`, `CycleMs`,
`Details`, `Opacity` y ambos colores permiten variaciones sin nuevos graficos.
Las auras clasicas conservan su ruta de dibujo. Las nuevas usan geometria
acotada, sin emisores adicionales ni texturas generadas por frame.

## Particulas de mapa

Reabrir el World Editor y buscar **Vigilia** en el catalogo de particulas.
Se colocan y guardan como cualquier particula existente.

| ID | Nombre |
|---|---|
| 108 | Polen de Jade |
| 109 | Ceniza Carmesi |
| 110 | Niebla Astral |
| 111 | Agujas de Escarcha |
| 112 | Luciernagas Solares |
| 113 | Susurros del Eclipse |

Definiciones: `resources/data/INIT/Particles.ini`. Entre 12 y 20 particulas
por emisor; usan el sprite existente 27452 y desvanecimiento/escala por vida.
Los colores del INI respetan la convencion BGR del motor legado.
El entorno local carga estos archivos sueltos sobre los paquetes. Para distribuir
solo archivos aopak se debe regenerar el paquete INIT con el flujo habitual.

## Correcciones y optimizacion del WE

Las vistas previas ocultas y el viewport oculto no siguen procesando cada frame.
La paleta y el mapa ahora respetan desvanecimiento, escala por vida y rotacion
visual. El cliente ahora carga esos parametros del INI, que antes ignoraba.
No se promete una mejora de FPS global sin medir un mapa representativo.

## Verificacion

Compilacion cliente y WE: cero errores y advertencias.
`client/test/render/AuraCollectionSmoke.tscn`: carga, simulacion y captura visual.
`tools/world-editor/ParticleCatalogSmoke.tscn`: carga, simulacion y round-trip de
guardado/recarga en user://, sin sobrescribir las definiciones fuente.
Ambas pruebas ejecutadas correctamente en Godot 4.4 .NET.
