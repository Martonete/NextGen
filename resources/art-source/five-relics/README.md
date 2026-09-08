# Cinco reliquias

Cinco objetos nuevos, con arte generado por imagegen integrada y adaptado a los
indices nativos del cliente. Las tres prendas tienen 36 px desde cuello a pies,
dos mas que Eclipse Alado; HeadOffsetY=-40 mantiene la cabeza alineada.
Cada pieza tiene cuatro cuadros en cada una de las cuatro direcciones (80 en total).
Espada de 39 px, baculo de 49 px; agarre y balanceo registrados por direccion.

| Objeto | Nombre | Cuerpo / arma | Aura | Textura | GRH |
|---|---|---|---|---|---|
| 1671 | Armadura del Alba Solar | Cuerpo 515 | 99 solar | 45001 | 33430..33450 |
| 1672 | Armadura del Guardian Abisal | Cuerpo 516 | 100 escarcha | 45002 | 33451..33471 |
| 1673 | Tunica del Cartografo Astral | Cuerpo 517 | 101 astral | 45003 | 33472..33492 |
| 1674 | Espada Corazon Carmesi | Arma 84 | 102 carmesi | 45004 | 33493..33513 |
| 1675 | Baculo de la Raiz Ancestral | Arma 85 | 98 jade | 45005 | 33514..33534 |

## Probar

Reabrir cliente; como Administrador ejecutar `/LOADOBJ`. Luego:

```
/ITEM 1671 1
/ITEM 1672 1
/ITEM 1673 1
/ITEM 1674 1
/ITEM 1675 1
```

Equipar desde inventario. `/aura off` quita cualquier previsualizacion manual
para mostrar las auras reales del equipo. Tambien se encuentran por nombre
en el buscador de items. No se agregaron a tiendas ni drops.

Defensas: armaduras 28..35, tunica 16..22. Espada: ataque 18..24.
Baculo: ataque 5..9, StaffPower=1, StaffDamageBonus=10. Sin restricciones
adicionales de clase o raza; precio de catalogo 180000 cada uno.

## Archivos y verificacion

Originales transparentes: solar.png, abyss.png, astral.png, sword.png, staff.png.
Prompts exactos: [PROMPTS.md](PROMPTS.md). Copias de los atlas preparados:
*-atlas.png; assets consumidos: resources/data/Graficos/45001.png..45005.png.
Capturas del cliente: preview-0.png..preview-3.png, con las cuatro direcciones,
la cabeza, el equipo y las auras. El orden de columnas coincide con la tabla.

Importador: client/Scripts/Diagnostics/ImportFiveRelics.cs, escena
res://test/render/ImportFiveRelics.tscn. Sin argumentos ejecuta la verificacion
visual; --import es solo para la importacion inicial (rechaza IDs existentes).
--refresh-atlases regenera exclusivamente estas cinco texturas desde los originales.
Copias previas a la importacion: original-data/. No restaurar esos indices enteros
si posteriormente se agregaron mas assets.

Verificado: compilacion C# sin errores, 5 objetos/auras, 3 cuerpos, 2 armas,
80 frames e iconos validos; prueba Rust data::objects::tests aprobada.
No modifica velocidades de caminata ni reglas de combate. Para distribuir
solo aopak, reconstruir graficos e INIT mediante el empaquetador habitual.
