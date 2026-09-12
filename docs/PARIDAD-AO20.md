# Paridad Argentum 20 — caminata, input, velocidades, tiempos y combate

Desde 2026-09-12 el movimiento, la respuesta del teclado, las velocidades, todos los
intervalos entre acciones y las fórmulas de combate replican **Argentum 20** (ao-org),
reemplazando en esos sistemas la paridad VB6 13.3. Fuente: el código AO20 local en
`C:\AO20\Argentum Client\CODIGO` y `C:\AO20\Argentum Server\Codigo`.

Fuera de alcance (no existe acá y no se inventó): `Modifiers`/EffectsOverTime, Retos, equipos,
`Improved-Hit-Chance`, perforación de armadura, tags elementales, stun de proyectiles, cleave,
multishot, nudillos, pólvora, anticheat de contadores de paquetes.

## Mapa AO20 → este repo

| AO20 | Acá |
|---|---|
| `engine.bas` ShowNextFrame / Char_Render (.Moving) | `client/Scripts/Main.Gameplay.cs` `UpdateMovement` |
| `TileEngine_Chars.bas` ApplySpeedingToChar | `Main.Gameplay.cs` `UpdateWalkAnimation` (clamp 40–220 ms) |
| `General.bas` Check_Keys / keysMovementPressedQueue / MoveTo | `client/Scripts/Game/InputHandler.cs` `Process`, `MoveTo` |
| `MainTimer.cls`, `HandleIntervals`, `HandleVelocidadToggle` | `client/Scripts/Game/MainTimer.cs`, `PacketHandler.Combat.cs` |
| `modBindKeys.Accionar` (KeyUp), `ModGameplayUI` click de hechizo/flecha | `InputHandler.cs` `AttackKeyUp`, `HandleSpellClick` (`GameConfig.SpellCastMode`) |
| `Protocol.bas` HandleWalk, `Modulo_UsUaRiOs.bas` MoveUserChar | `server/source/game/handlers/movement.rs` `handle_walk` |
| `ActualizarVelocidadDeUsuario`, `UpdateNpcSpeed` | `server/source/game/handlers/speed.rs` |
| `modNuevoTimer.bas` IntervaloPermite* | `server/source/game/handlers/common.rs` `intervalo_permite_*` |
| `intervalos.ini` | `server/dat/Intervalos.ini` (mismas claves, ms) |
| `SistemaCombate.bas` fórmulas | `server/source/game/handlers/combat_ao20.rs` (puras, con tests) |
| `UsuarioAtacaUsuario` / `UsuarioImpacto` / `UserDamageToUser` / `UserDañoEspecial` | `combat.rs` `usuario_ataca_usuario`, `combat_pvp.rs` helpers |
| `PuedeAtacar` | `combat.rs` `puede_atacar` |
| `UsuarioAtacaNpc` / `UserImpactoNpc` / `UserDamageNpc` | `npcs.rs` `user_attack_npc` |
| `NpcAtacaUser` / `NpcImpacto` / `NpcDamage` | `npcs.rs` `npc_attack_user` |
| `Balance.dat` MOD* (12 clases), `[BACKSTAB]`, `[EXTRA]` | `server/dat/Balance.dat` (secciones AO20 al final), `data/balance.rs` |

## Caminata (cliente)

- `timerTicksPerFrame = elapsedMs * 0.018`; cada frame la cámara y cada personaje avanzan
  `8.5 * ticks * Speeding` px. Un tile (32 px) tarda **~209 ms** a velocidad 1, integrado con el
  delta real del frame; el sobrante del último frame se descarta (igual que AO20). Por eso a
  60 FPS un paso son 13 frames (216,7 ms), a 144 FPS 31 (215 ms), a 30 FPS 7 (233 ms).
- Orden por frame: primero avanza el movimiento (render), después se leen las teclas
  (`Check_Keys`). Un paso que termina en el frame N es seguido por el siguiente en el mismo
  frame, sin pose de reposo intermedia. La primera frame sin paso congela la serie walk (Idle).
- Piernas: ms por frame = `(Grh.speed \ NumFrames) / Speeding`, clamp [40, 220].
- Cola de teclas: se mueve según la **última** tecla de movimiento apretada (flechas o WASD).
- `MoveTo`: `LegalPos` + `CanMove` (paralizado/inmovilizado/stun). Trabado → `ChangeHeading`
  solo si cambia el rumbo y pasaron 120 ms. Descansando → un `/DESCANSAR` y no camina.
- Velocidad: `VelocidadToggle` (propia) y `SpeedingAct` (área); `CharacterCreate` trae el
  `Speeding` del personaje al final del paquete.

## Acciones (cliente, MainTimer)

Intervalos del paquete `Intervals` (16 Int32 del server): Hit 1165, Bow 1200, Magic 1230,
Walk 210, Drop 400, UseItemKey 380, UseItemClick 276, HitMagic 800, MagicHit 800, HitUseItem 800,
LeftClick 80, etc. Se aplican defaults iguales hasta que llega el paquete.

- **Atacar** (en KeyUp de Ctrl/tecla): `Check(CastAttack,false)` → rate-limit `Hit` → `WriteAttack`
  → `Restart(AttackSpell)`.
- **Hechizo** (click con "Lanzar"): `Check(AttackSpell,false)` → `Check(CastSpell)` →
  `Restart(CastAttack)`. `SpellCastMode` en `Options.ao`: 0 Bloqueo (default), 1 BloqueoLanzar (sin
  gate, reinicia timers), 2 SinBloqueo (manda igual y avisa).
- **Flechas**: AttackSpell, CastAttack, Arrows → reinicia Attack, CastSpell, Arrows.
- **Usar** `U` / equipar `E`: `UseItemWithU`; doble click / barra de macros: `UseItemWithDblClick`;
  tirar: `Drop`; `L`: `SendRPU` (5 s); click en el mundo: 200 ms.

## Server

- `Intervalos.ini` en formato AO20 (ms). Timers por usuario en `Instant`, con los cruces exactos:
  atacar sella golpe→magia y golpe→usar; arco sella golpe y hechizo; magia→golpe lee el sello
  del hechizo; hechizo sella magia→golpe.
- `handle_walk`: paralizado → aviso una vez + PosUpdate; comerciando → nada; meditando se corta;
  descansar se corta; el viaje a casa **se cancela** (no bloquea); oculto se revela salvo
  Ladrón/Bandido. El chequeo de speed-hack (30 pasos / 5,8 s, 2 strikes) ahora escala con
  `IntervaloCaminar / speeding` (+3 pasos de tolerancia).
- Velocidad: normal 1, muerto 1.4, barco/montura `Velocidad` del objeto (default 1), armadura con
  `Velocidad≠1`. NPCs: `210 / IntervaloNpcAI` (deslizan el tile durante todo su intervalo).
- Combate: `AttackPower = (Skill + 3*Skill/100*Agi) * MOD + 2.5*max(ELV-12,0)`; evasión ídem con
  Tácticas y `MODEVASION`; escudo `(Defensa*MODESCUDO/2) * Porcentaje/100`;
  `Prob = clamp(5,95, 50 + (PA-PE)*0.4)` (meditando −25 % de evasión); rechazo de escudo
  `clamp(10,90, Porcentaje * Defensa/(Defensa+Tácticas))`; daño
  `(3*Arma + MaxArma*0.2*max(0,Fuerza-15) + Usuario) * MOD` (+ barco/montura); zona 1..8
  (cabeza=casco, resto armadura+escudo; 7-8 se re-rollean); daño mínimo 0; crítico Bandido con
  nudillos; apuñalar `(Asesino o Apuñalar≥10) y arma.Apuñala` con chance de `[BACKSTAB]`
  (0 en el Balance.dat de AO20 → hay que setearlo para que apuñale) y bonus `MODAPUNALAR`;
  desequipar Bandido/Ladrón sin arma; efectos del arma (veneno 30 %, paralizar 10 %,
  estupidez 13 %). Contra NPC: `MinHitToNPC/MaxHitToNPC`, crítico 0.33, apuñalar
  `MODAPUNALARNPCMIN..MAX`. NPC → usuario: zona 1..6, casco o armadura+escudo, barco, montura,
  la meditación se corta solo si el daño supera `MinHp/100*INT*Meditar/100*12/(rnd(0..5)+7)`.
- `obj.dat` acepta las claves AO20 opcionales `Porcentaje` (escudo, default 100), `WeaponType`
  (derivado de Apuñala/proyectil si falta), `MinHitToNPC/MaxHitToNPC`, `Velocidad`,
  `ExtraCritAndStabChance`, `Estupidiza`, `Incinera`, `Paraliza`.

## Verificación

- Server: `cargo test` — `combat_ao20` (12 tests de fórmulas con valores a mano),
  `speed` (muerto/barco/montura/armadura, NPC), `interval_tests`, `load_real_balance`
  (tablas AO20, `MODESCUDO Mago=0`, `[BACKSTAB]`).
- Cliente: `client/test/render` (`WalkContinuityTests`), `WalkMovementSmoke` dentro de
  `RenderSmoke.tscn` (40 pasos a 20/30/60/144/240 FPS con la cadencia AO20).
- Manual: `docker compose up -d --build ao-server`; caminar y cambiar de tecla a mitad de paso
  (gana la última), golpe→hechizo bloqueado 800 ms, poción tras golpe bloqueada 800 ms, barco
  más rápido, fantasma 1.4×, PvP con escudo (rechazos) y daga (apuñalar con `[BACKSTAB]` > 0).
