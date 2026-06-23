# Discrepancias VB6 13.3 vs Rust Server + Godot Client

Auditoria completa realizada el 2026-04-01.
Comparacion archivo por archivo del servidor VB6 13.3 original contra el servidor Rust y cliente Godot C#.

---

## CRITICOS (rompen gameplay)

### 1. Intervalos 2x mas lentos (HP regen, hambre, sed)
- **Causa raiz**: VB6 ticks son de 50ms (20Hz). La conversion a segundos deberia ser `ticks / 20`, pero el Rust divide por 10.
- **HP regen sin descansar**: VB6 = 80s (`1600 * 50ms`), Rust = 160s
- **HP regen descansando**: VB6 = 5s (`100 * 50ms`), Rust = 10s
- **Hambre**: VB6 = 325s (`6500 * 50ms`), Rust = 650s
- **Sed**: VB6 = 300s (`6000 * 50ms`), Rust = 600s
- **Archivos**: `server/source/game/handlers/ticks/player.rs` (constantes `HP_REGEN_INTERVAL`, `HUNGER_INTERVAL`, `THIRST_INTERVAL`)
- **Fix**: Dividir todas las constantes de intervalo por 2.

### 2. Stamina en ataques melee no implementada
- **VB6**: Requiere minimo 10 stamina para atacar. Descuenta `RandomNumber(1, 10)` por ataque (hit o miss).
- **Rust**: Solo descuenta stamina en ataques a distancia (proyectiles). Melee no chequea ni descuenta.
- **Archivos**: `server/source/game/handlers/combat.rs` (funcion `handle_attack` / `puede_pegar`)
- **Fix**: Agregar check `if min_sta < 10 { return }` y `min_sta -= rand_range(1, 10)` en el handler de ataque melee.

### 3. XP proporcional por golpe (no al killer)
- **VB6**: `CalcularDarExp` se llama en CADA golpe. XP = `Dano * (NPC.GiveEXP / NPC.MaxHP)`. Distribuye XP proporcionalmente al dano de cada jugador.
- **Rust**: Da el `give_exp` completo al killer en `npc_die`. Los spells si dan XP proporcional por golpe, pero melee no.
- **Archivos**: `server/source/game/handlers/npcs.rs` (funcion `npc_die`), `server/source/game/handlers/combat.rs`
- **Fix**: Implementar XP proporcional en cada golpe melee, igual que ya funciona en spells.

### 4. HP de resurreccion incorrecto
- **VB6**: `MinHP = UserAtributos(Constitucion)` (tipicamente 15-22 segun atributo CON).
- **Rust**: `min(35, max_hp)` (fijo 35).
- **Archivos**: `server/source/game/handlers/` (buscar `revive_user` o `resurrect`)
- **Fix**: Cambiar a `min_hp = user.attributes[CONSTITUCION]`.

### 5. Bonus de nivel 50 se activa en nivel 255
- **VB6**: `If ELV = 50 Then` da anuncio global + 50 skill points bonus.
- **Rust**: `if new_level == 255` (MAX_LEVEL) — bug, deberia ser 50.
- **Archivos**: `server/source/game/handlers/` (buscar `check_user_level` o `level_up`)
- **Fix**: Cambiar `255` a `50`.

### 6. Sistema criminal no replicado
- **VB6**: Criminal se calcula dinamicamente: `L = (-AsesinoRep - BandidoRep + BurguesRep - LadronesRep + NobleRep + PlebeRep) / 6`. Si `L < 0` es criminal.
  - Atacar ciudadano: `BandidoRep += 100`, `NobleRep *= 0.5`
  - Matar ciudadano: `AsesinoRep += 2000`
  - Atacar criminal: `NobleRep += 5`
- **Rust**: Solo un `criminal: bool` que se setea en `true`. Los 6 campos de reputacion se almacenan pero NUNCA se actualizan en PvP.
- **Archivos**: `server/source/game/handlers/combat.rs`, `server/source/game/types.rs`
- **Fix**: Implementar la formula de 6 campos y las actualizaciones de rep en cada evento PvP.

### 7. Indice de skill Comerciar incorrecto
- **VB6**: `eSkill.Comerciar = 10` (1-based), que es indice 9 en array 0-based.
- **Rust**: `SK_COMERCIAR = 11` en `commerce.rs` linea 14 — lee el skill equivocado.
- **Archivos**: `server/source/game/handlers/commerce.rs` linea 14
- **Fix**: Cambiar `SK_COMERCIAR` al indice correcto (verificar mapping 1-based vs 0-based).

---

## HIGH (diferencia significativa)

### 8. NPC restock inmediato (Rust) vs vaciado completo (VB6)
- **VB6**: Items non-crucial se agotan. Solo se recargan cuando TODO el inventario esta vacio Y `InvReSpawn != 1`. Items `Crucial=1` se recargan individualmente.
- **Rust**: Cada slot se restockea inmediatamente al vaciarse. NPCs tienen stock infinito efectivamente.
- **Archivos**: `server/source/game/handlers/commerce.rs` (handler de compra)

### 9. Venta de armadura faccionaria bloqueada
- **VB6**: Items `Real=1` se venden a NPCs con nombre "SR". Items `Caos=1` a NPCs "SC".
- **Rust**: TODOS los items faccionarios bloqueados de venta a cualquier NPC.
- **Archivos**: `server/source/game/handlers/commerce.rs` lineas 356-360

### 10. Bank stack limit 999 vs 10,000
- **VB6**: `MAX_INVENTORY_OBJS = 10000` para stacks en banco.
- **Rust**: `MAX_BANK_STACK = 999`.
- **Archivos**: `server/source/game/handlers/` (constantes de banco)

### 11. Barco sin check de nivel
- **VB6**: Requiere nivel 25 (o 20 para Pirata/Trabajador con Pesca=100) para abordar.
- **Rust**: Solo chequea skill de navegacion, no nivel.
- **Archivos**: `server/source/game/handlers/inventory/use_item.rs` (branch Boat)

### 12. Scrolls sin check max_mana > 0
- **VB6**: Solo clases con mana (magos) pueden aprender hechizos de scrolls.
- **Rust**: Cualquier clase puede aprender.
- **Archivos**: `server/source/game/handlers/inventory/use_item.rs` (branch Scroll)

### 13. Chat de muertos visible para todos
- **VB6**: Chat de muertos va a `ToDeadArea` (solo visible para otros muertos).
- **Rust**: Va a `ToArea` (visible para todos).
- **Archivos**: `server/source/game/handlers/` (handler de chat/talk)

### 14. /SEGURO bloquea todo PvP
- **VB6**: Seguro solo previene atacar CIUDADANOS. Atacar criminales sigue permitido con seguro on.
- **Rust**: `safe_toggle` bloquea TODO PvP sin distincion.
- **Archivos**: `server/source/game/handlers/combat.rs` (check de safe antes de atacar)

### 15. Guild creation requirements diferentes
- **VB6**: Nivel >= 25, Liderazgo >= 90. 6 tipos de alineamiento (Royal, Legion, Neutral, GM, Ciudadano, Criminal).
- **Rust**: Nivel >= 50, requiere Amuleto de Lider (item 939), no chequea Liderazgo. Solo 2 alineamientos (Ciudadano/Criminal auto-detectado).
- **Archivos**: `server/source/game/handlers/` (buscar `fundarclan`)

### 16. Paralisis sin reduccion por distancia
- **VB6**: Si el caster sale del rango de vision y la victima no es clase magica, la paralisis se reduce a 37 ticks (~1.85s).
- **Rust**: No implementado.
- **Archivos**: `server/source/game/handlers/ticks/player.rs`

### 17. Invisibilidad se limpia al atacar
- **VB6**: La invisibilidad por hechizo NO se limpia al atacar (el timer corre independiente).
- **Rust**: Ataque limpia tanto `hidden` como `invisible`.
- **Archivos**: `server/source/game/handlers/combat.rs` lineas 427-458

---

## MEDIUM

### 18. Distancia de comercio NPC
- **VB6**: Max 3 tiles. **Rust**: Max 6 tiles (comercio), 10 tiles (banco click).
- **Archivos**: `server/source/game/handlers/inventory/click.rs`

### 19. Distancia de trade entre jugadores
- **VB6**: Max 3 tiles. **Rust**: Sin limite de distancia.
- **Archivos**: `server/source/game/handlers/player_commands.rs`

### 20. Gold drop cap
- **VB6**: Protocolo rechaza drops > 10,000. **Rust**: Acepta hasta 500,000.
- **Archivos**: `server/source/game/handlers/inventory/ground.rs`

### 21. Drop mientras navega
- **VB6**: Bloqueado. **Rust**: Permitido.
- **Archivos**: `server/source/game/handlers/inventory/ground.rs`

### 22. UseOnce (comida) restaura HP
- **VB6**: UseOnce solo restaura hambre, NUNCA HP. **Rust**: UseOnce con min_modificador > 0 da HP.
- **Archivos**: `server/source/game/handlers/inventory/use_item.rs`

### 23. Equip agrega check de nivel
- **VB6**: `EquiparInvItem` NO tiene check de nivel. **Rust**: Chequea `obj.lvl > user.level`.
- **Archivos**: `server/source/game/handlers/inventory/equip.rs`

### 24. Flechas sin check clase/faccion
- **VB6**: Flechas pasan por `ClasePuedeUsarItem` y `FaccionPuedeUsarItem`. **Rust**: No chequea.
- **Archivos**: `server/source/game/handlers/inventory/equip.rs`

### 25. Yell a todo el mapa
- **VB6**: Yell va a `ToPCArea` (radio estandar). **Rust**: Va a `ToMap` (mapa entero).
- **Archivos**: `server/source/game/handlers/` (handler de yell)

### 26. Ceguera y Stun pueden stackear
- **VB6**: Comparten un counter (`Counters.Ceguera`) — no pueden estar activos simultaneamente.
- **Rust**: Counters separados (`counter_blind`, `counter_stun`) — pueden stackear.
- **Archivos**: `server/source/game/handlers/ticks/player.rs`, `server/source/game/types.rs`

### 27. Teleport sin randomizar radio
- **VB6**: Si el teleport tiene Radio > 0, randomiza destino con 5 reintentos.
- **Rust**: Siempre teleporta al punto fijo.
- **Archivos**: `server/source/game/handlers/` (handler de movimiento/teleport)

### 28. Sin restricciones de mapa en teleport
- **VB6**: Bloquea entrada a mapas newbie/armada/caos/faccion segun estado del jugador.
- **Rust**: No verifica restricciones al teleportar.
- **Archivos**: `server/source/game/handlers/` (handler de movimiento)

### 29. ZONAPELEA incompleto
- **VB6**: Si ambos jugadores estan en ZONAPELEA, combate permitido sin penalidad criminal ni drop de items. Si solo uno esta, combate bloqueado.
- **Rust**: Solo suprime drops en zona de combate. No tiene logica de permiso dual.
- **Archivos**: `server/source/game/handlers/combat.rs`

### 30. POSINVALIDA no bloquea NPCs
- **VB6**: Trigger 3 (POSINVALIDA) bloquea movimiento de NPCs. **Rust**: No enforced.
- **Archivos**: `server/source/game/handlers/ticks/npc_move.rs`

### 31. Level 25 expulsion de faccion
- **VB6**: Al llegar a nivel 25, expulsa de guild faccionaria. **Rust**: No implementado.
- **Archivos**: `server/source/game/handlers/` (level up handler)

---

## MINOR

### 32. Veneno por arma off-by-one
- VB6: 59% (`< 60`). Rust: 60% (`<= 60`).

### 33. Invisibilidad reset range
- VB6: `rand(-100, 100)`. Rust: `rand(-250, 250)`.

### 34. DoAcuchillar min damage
- Rust agrega `.max(1)`. VB6 permite 0.

### 35. Skill leveling sin gate hambre/sed
- VB6: `SubirSkill` bloqueado si `Hambre=1 OR Sed=1`. Rust siempre sube.

### 36. Meditation cancel en PvP
- VB6: Cancel al ser TARGETED (antes del hit/miss). Rust: Cancel solo despues de hit confirmado.

### 37. Consejero puede vender a NPCs
- VB6: Bloqueado. Rust: Permitido.

### 38. Pocion restaura sed
- VB6: Pociones (tipo 11) NO restauran sed. Rust si.

### 39. Equip/desequip bloqueado montado
- VB6: No tiene restriccion. Rust: Bloquea equip/desequip montado.

### 40. Puerta distancia metric
- VB6: Euclidiana <= 2.0. Rust: Chebyshev <= 3.

### 41. Guild war no persiste
- Rust: Relaciones guild (guerra/alianza) solo en memoria. Se pierden al reiniciar.

### 42. Faccion /RENUNCIA no quita armadura
- VB6: Siempre quita armadura faccionaria al salir. Rust `/RENUNCIA` no la quita.

### 43. Killer EXP formula
- VB6: `victim_level * 2` fijo. Rust: `victim_level * multiplicador_exp` configurable.

### 44. Party death XP penalty
- VB6: `ELV * -10 * CantMiembros` aplicado a miembros del party. Rust: No implementado.

### 45. Noble NPC click mata al jugador
- VB6: Click en NPC rey/demonio mata a jugadores de faccion opuesta. Rust: No implementado.

### 46. Dead yell bloqueado
- VB6: Muertos pueden gritar (a area muerta). Rust: Bloquea yell de muertos.

### 47. HOGAR/Traveling bloquea movimiento
- VB6: `If Traveling = 1 Then Exit Sub`. Rust: Cancela travel y permite moverse.

### 48. Speed-hack detection
- VB6: 30 pasos / 5800ms window, kickea al usuario. Rust: No implementado.

### 49. Guild chat overhead bubble
- VB6: `/CMSG` envia burbuja amarilla overhead a clanmates cercanos. Rust: No.

### 50. Chat color personalizable
- VB6: GMs pueden cambiar color de chat con `/CHATCOLOR`. Rust: No implementado.

---

## Resumen

| Severidad | Cantidad |
|-----------|----------|
| CRITICO | 7 |
| HIGH | 10 |
| MEDIUM | 14 |
| MINOR | 19 |
| **Total** | **50** |

Prioridad de fix: Criticos primero (especialmente #1 intervalos y #2 stamina melee que afectan toda la experiencia de juego), luego HIGH, luego MEDIUM segun necesidad.
