# Discrepancias VB6 13.3 vs Rust Server + Godot Client

Auditoria original: 2026-04-01 (archivo por archivo del servidor VB6 13.3 contra Rust + Godot C#).
**Re-auditoria: 2026-07-06** — se verificaron las 50 discrepancias contra el codigo actual.

> **Estado global: RESUELTO.** De las 50 discrepancias originales, 48 ya estaban
> arregladas en la re-auditoria y las 2 restantes (#15, #26) se cerraron el 2026-07-06.
> Ademas se detecto y corrigio un bug de persistencia de buffs de atributo no listado
> originalmente (ver seccion "Fixes 2026-07-06").

Leyenda: ✅ ARREGLADO · ⚠️ ABIERTO (al momento de re-auditar)

---

## CRITICOS (rompen gameplay)

### 1. ✅ Intervalos 2x mas lentos (HP regen, hambre, sed)
Corregido. Valores VB6 correctos en `ticks/player.rs`: hambre=325s, sed=300s, HP regen=80s, descansando=5s.

### 2. ✅ Stamina en ataques melee
Implementado en `combat.rs` (~L558): requiere min 10 stamina, descuenta `rand(1,10)` por ataque.

### 3. ✅ XP proporcional por golpe
`npcs.rs` (~L527): `hit_exp = damage * NPC.GiveEXP / NPC.MaxHP` por golpe.

### 4. ✅ HP de resurreccion
`common.rs` (~L1141): `revive_hp = constitution.min(max_hp)` (ya no fijo 35).

### 5. ✅ Bonus de nivel 50
`leveling.rs` (~L188): `if new_level == 50` (ya no 255). Bonus de nivel 25 tambien presente.

### 6. ✅ Sistema criminal (6 campos de reputacion)
`combat_pvp.rs` (~L16): `recalc_criminal` recalcula estado criminal desde los 6 campos de rep en cada evento PvP.

### 7. ✅ Indice de skill Comerciar
`commerce.rs:14`: `SK_COMERCIAR = 9` (indice 0-based correcto).

---

## HIGH (diferencia significativa)

### 8. ✅ NPC restock (vaciado completo)
`commerce.rs` (~L350): restock solo cuando TODO el inventario esta vacio y `inv_respawn != true`.

### 9. ✅ Venta de armadura faccionaria
`commerce.rs`: items faccionarios vendibles a NPCs correctos (SR/SC).

### 10. ✅ Bank stack limit
`commerce.rs:1041`: `MAX_BANK_STACK = 10_000`.

### 11. ✅ Barco check de nivel
`inventory/use_item.rs` (~L315): nivel 25 (o 20 para Pirata/Trabajador con Pesca>=100).

### 12. ✅ Scrolls check max_mana
`inventory/use_item.rs` (~L622): `if max_mana == 0 { return }` — solo clases magicas.

### 13. ✅ Chat de muertos
`social.rs` (~L830): chat de muertos solo visible para muertos cercanos.

### 14. ✅ /SEGURO
`combat.rs` (~L613): seguro solo bloquea atacar no-criminales; atacar criminales permitido.

### 15. ✅ Guild creation requirements — **CERRADO 2026-07-06**
`guilds_handler.rs`: la creacion efectiva exigia nivel 50 / liderazgo 100 y leia el skill
equivocado (`skills[0]` = Magia). Corregido a nivel >= 25 / liderazgo >= 90 (VB6 13.3),
leyendo `skills[16]` (LIDERAZGO). El requisito de Amuleto de Lider (item 939) se mantiene
como mecanica agregada intencional (no forma parte de VB6).

### 16. ✅ Paralisis reduccion por distancia
`ticks/world.rs` (~L36): reduce paralisis si el caster sale de rango y la victima no es clase magica.

### 17. ✅ Invisibilidad al atacar
`combat.rs` (~L493): el ataque revela `hidden` (stealth) pero NO limpia invisibilidad por hechizo.

---

## MEDIUM

### 18. ✅ Distancia comercio NPC
`inventory/click.rs` (~L707): max 3 tiles.

### 19. ✅ Distancia trade jugadores
`player_commands.rs` (~L134): Chebyshev <= 3.

### 20. ✅ Gold drop cap
`inventory/ground.rs` (~L511): `drop_total = amount.min(10_000)`.

### 21. ✅ Drop mientras navega
`inventory/ground.rs`: bloqueado para items y oro.

### 22. ✅ UseOnce (comida) restaura HP
`inventory/use_item.rs` (~L1204): comida no restaura HP, solo pociones.

### 23. ✅ Equip check de nivel
`inventory/equip.rs`: sin check de nivel (coincide con VB6).

### 24. ✅ Flechas check clase/faccion
`inventory/equip.rs` (~L195): flechas chequean ClasePuedeUsar y FaccionPuedeUsar.

### 25. ✅ Yell alcance
`social.rs` (~L880): yell va a `ToArea` (rango de vision).

### 26. ✅ Ceguera y Stun stacking — **CERRADO 2026-07-06**
Se mantienen campos separados (`counter_stun`/`counter_blind`) por sus mensajes de expiracion
distintos, pero los 4 unicos sitios de aplicacion (2 en `npcs.rs`, 2 en `spell_offensive.rs`)
ya limpian el efecto opuesto, por lo que **nunca coexisten**. Se documento la invariante en
`types.rs` para prevenir regresiones. Funcionalmente equivalente al counter unico de VB6.

### 27. ✅ Teleport randomizar radio
`movement.rs` (~L568): `randomize_exit(...teleport_radio)`.

### 28. ✅ Restricciones de mapa en teleport
`movement.rs` (~L28): `check_teleport_restrictions` (newbie, min_level, armada/caos).

### 29. ✅ ZONAPELEA logica dual
`combat.rs` (~L661): ambos jugadores deben estar en la zona (permiso dual).

### 30. ✅ POSINVALIDA bloquea NPCs
`npcs.rs` (~L1757, LegalPosNPC): trigger 3 bloquea movimiento de NPCs.

### 31. ✅ Level 25 expulsion de faccion
`leveling.rs` (~L182): expulsa de guild faccionaria a nivel 25.

---

## MINOR

### 32. ✅ Veneno por arma off-by-one — **residuo CERRADO 2026-07-06**
Arma melee usaba `< 60` (correcto). El veneno de flechas (`skills/combat.rs:741`) usaba
`<= 60`; corregido a `< 60` para igualar al path de arma y al `UserEnvenena` de VB6.

### 33. ✅ Invisibilidad reset range
`ticks/world.rs` (~L125): `rand(-100, 100)`.

### 34. ✅ DoAcuchillar min damage
`combat.rs` (~L280): sin `.max(1)` (permite 0, como VB6).

### 35. ✅ Skill leveling gate hambre/sed
`skills/mod.rs` (~L154): bloquea leveling si `min_ham <= 1 || min_agua <= 1`.

### 36. ✅ Meditation cancel en PvP
`combat.rs` (~L780): targeting PvP cancela meditacion antes del hit/miss.

### 37. ✅ Consejero vender a NPCs
`commerce.rs` (~L427): bloqueado para CONSEJERO.

### 38. ✅ Pocion restaura sed
`inventory/use_item.rs` (~L1217): pociones no restauran sed.

### 39. ✅ Equip/desequip montado
`inventory/equip.rs` (~L168): solo restringe por navegacion, sin restriccion por montado.

### 40. ✅ Puerta distancia metric
`inventory/doors.rs` (~L22): Euclidiana `sqrt(dx^2+dy^2) > 2.0`.

### 41. ✅ Guild war persiste
`db/guilds.rs` (~L708): `save_guild_relation` persiste en tabla `guild_relations`
(migration 009); se carga al inicio con `load_all_guild_relations`.

### 42. ✅ /RENUNCIA quita armadura
`social.rs` (~L507): desequipa armadura faccionaria al renunciar.

### 43. ✅ Killer EXP formula
`combat.rs` (~L1613): `exp_gain = victim_level * 2` (fijo).

### 44. ✅ Party death XP penalty
`combat.rs` (~L1759): penalizacion `level * -10 * member_count`.

### 45. ✅ Noble NPC click mata al jugador
`inventory/click.rs` (~L914): Rey/Demonio matan jugadores de faccion opuesta.

### 46. ✅ Dead yell
`social.rs` (~L863): muertos pueden gritar solo a otros muertos cercanos.

### 47. ✅ Traveling bloquea movimiento
`movement.rs` (~L318): `if traveling { return }`.

### 48. ✅ Speed-hack detection
`movement.rs` (~L323): 30 pasos/5800ms, kickea en segundo strike (GMs exentos).

### 49. ✅ Guild chat overhead bubble
`guilds_handler.rs` (~L756): `/CMSG` envia burbuja overhead amarilla a clanmates cercanos.

### 50. ✅ Chat color /CHATCOLOR
`slash_commands.rs` (~L712): `/CHATCOLOR <r> <g> <b>` implementado; aplicado en `social.rs`.

---

## Fixes 2026-07-06 (fuera de la lista original)

### Persistencia de buffs de atributo (Fuerza/Agilidad/Carisma) — bug de exploit
- **Problema**: `build_char_save_data` (`ticks/player.rs`) guardaba `user.attributes`, que
  durante un buff activo contiene los valores modificados temporalmente. Como
  `attributes_backup` (la base) no se persiste, guardar/desconectar con un buff activo
  horneaba los valores inflados de forma permanente. Buffear + reloguear en bucle permitia
  inflar atributos hasta el cap de forma definitiva.
- **Fix**: al guardar, si `tomo_pocion` esta activo, persistir `attributes_backup` en lugar
  de `attributes`. Cubre ambos caminos de guardado (autosave periodico y disconnect). La
  reversion en runtime (expiracion del buff en `ticks/player.rs`) y la limpieza al morir
  (`combat.rs`) ya eran correctas; el unico agujero era la persistencia.

---

## Resumen

| Severidad | Total | Arreglados | Abiertos |
|-----------|-------|------------|----------|
| CRITICO | 7 | 7 | 0 |
| HIGH | 10 | 10 | 0 |
| MEDIUM | 14 | 14 | 0 |
| MINOR | 19 | 19 | 0 |
| **Total** | **50** | **50** | **0** |

Mas 1 bug de persistencia de buffs detectado y corregido el 2026-07-06 (no listado originalmente).

**Paridad VB6 13.3: completa** segun esta auditoria.
