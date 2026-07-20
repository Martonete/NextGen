# AGENTS.md — argentum-nextgen

Guía para agentes (y humanos) que trabajan en este repo. Todo lo de acá está verificado contra
el código real; donde la documentación del repo contradice al código, se marca con ⚠️.

> Idioma: Martín trabaja en **español**. Respondé en español.

---

## Qué es el proyecto

**Argentum Online reescrito desde cero**: MMORPG 2D argentino clásico (2002, originalmente en
Visual Basic 6). Este proyecto lo reimplementa con **servidor en Rust** + **cliente en Godot/C#**,
manteniendo **paridad de comportamiento con la versión VB6 13.3**.

- Upstream: `https://github.com/cyphercr0w/argentum-nextgen`. Este repo tiene trabajo propio
  adicional encima (no es un clon limpio).
- Paridad VB6: ver `docs/DISCREPANCIAS-VB6-13.3.md` — auditoría de 50 discrepancias, marcada
  como resuelta al 2026-07-06.
- Licencia: AGPL-3.0.

---

## Cómo levantar todo (flujo real de Martín)

**Servidor** (desde la raíz del repo):
```powershell
docker compose up -d --build ao-server
```
Verificar: `docker compose logs -f ao-server` → debe mostrar `Listening on 0.0.0.0:5028`.

**Cliente**:
```powershell
powershell -ExecutionPolicy Bypass -File 'C:\Users\marti\Documents\AORust\argentum-nextgen\Open-LocalClient.ps1'
```
(hace `dotnet build` en `client/` y abre Godot 4.4 mono desde `..\Godot_4.4_mono\...`)

Scripts auxiliares: `Start-LocalServer.ps1` (compose up sin `--build`), `Open-WorldEditor.ps1`.

> `dotnet build` en `client/` hay que correrlo **cada vez que se cambia código C#** antes de abrir Godot.

---

## Stack y versiones

| Componente | Versión real | Fuente |
|---|---|---|
| Server | Rust **edition 2024** (requiere rustc ≥1.85), crate único `ao-server` | `Cargo.toml` |
| Runtime server | tokio 1 (full), sqlx 0.8 (postgres+tls-rustls+migrate), argon2 0.5, dotenvy, tracing, thiserror, socket2 | `Cargo.toml` |
| DB | PostgreSQL **17** (alpine, en Docker) | `docker-compose.yml` |
| Cliente engine | Proyecto en **Godot 4.3** (`config/features=("4.3","C#")`) pero se **abre con binario Godot 4.4 .NET** (retrocompatible) | `client/project.godot:15`, `Open-LocalClient.ps1` |
| Cliente .NET | **net8.0**, C# 10, Godot.NET.Sdk 4.3.0 | `client/ArgentumNextgen.csproj` |
| Renderer | `gl_compatibility` (OpenGL), viewport 800×600 no redimensionable | `client/project.godot` |

⚠️ **.NET SDK debe ser 8.0, NO 9.0.** El README pide Godot 4.4 pero el proyecto está declarado en
4.3 (funciona igual con binario 4.4).

`server.ini [INIT]` declara `Version=0.14.0` / `ClientVersion=1.0.0`, pero **no se validan en el
handshake** (solo se loguean al arrancar).

---

## Arranque del servidor (secuencia real — `server/source/main.rs`)

1. Logging (`RUST_LOG`, default `ao_server=info`).
2. Base path = arg 1 CLI, o `./server` si no se pasa (en Docker: `/app/server`).
3. Carga `server.ini` — **obligatorio**, `exit(1)` si falla.
4. Carga datos de juego (`data::GameData::load`) desde `server/dat/` y `server/maps/`:
   - **Obligatorios (exit 1 si faltan/corruptos):** `Experiencia.dat`, `obj.dat`, `Hechizos.dat`, `NPCs.dat`, `NPCs-HOSTILES.dat`.
   - **Tolerantes (warn, no aborta):** mapas individuales, `Balance.dat`, recetas de crafting, `Intervalos.ini`.
5. `dotenvy::dotenv()` + lee `DATABASE_URL` (fallback hardcodeado `postgres://ao:ao_secret@localhost:5432/ao_server`).
6. `db::init_pool` → **corre migraciones sqlx automáticamente** (`server/migrations/001..009`). No hay que correrlas a mano.
7. Carga bans/guilds desde DB, inicializa `GameState`, spawnea NPCs/objetos de mapa.
8. `TcpServer::start("0.0.0.0", port, max_users)` → escucha en `0.0.0.0:5028`.
9. Loop de eventos con timers: game tick 40ms, AI, respawn, passives, cleanup, security.

Puerto **5028** sale de `server.ini [INIT] StartPort` (no de env var).

---

## Base de datos

- `docker-compose.yml` levanta 3 servicios:
  - `postgres` — PG17-alpine, user `ao` / pass `ao_secret` / db `ao_server`. **NO expone 5432 al host** (solo accesible en la red interna de Docker).
  - `ao-server` — build local, mapea `5028:5028`, bind-mount `./server:/app/server`, `DATABASE_URL` apunta al host `postgres`.
  - `pgadmin` — `5050:5050`, login `admin@admin.com` / `admin`.
- `.env`: `DATABASE_URL` (localhost:5432, pensado para binario nativo) + `POSTGRES_PASSWORD`.
- Migraciones sqlx se aplican **solas** al arrancar (`server/source/db/mod.rs`).

⚠️ El **binario nativo** (`cargo run -- ./server`) **no conecta** con la config default porque el
compose no expone 5432 al host. Por eso el camino operativo es **Docker**. Para inspeccionar la DB
desde Windows con herramientas externas, hay que agregar `ports: ["5432:5432"]` al servicio postgres.

---

## Protocolo de red

⚠️ **Fuente de verdad = `docs/skills/ao-protocol.md`.** El archivo `docs/architecture/protocol.md`
describe un protocolo de **texto VB6 con cifrado XOR** que es **legado y NO refleja el código actual**.

- **Binario 13.3 sobre TCP crudo**: cada paquete = 1 byte opcode + campos tipados **little-endian**.
  Sin framing, sin cifrado de transporte, sin length-prefix — paquetes auto-delimitados. Cliente
  desactiva Nagle y hace batching de salientes por frame.
- **ByteQueue** (`client/Scripts/Network/ByteQueue.cs` ↔ `server/source/protocol/byte_queue.rs`):
  espejos exactos, LE, strings en **Latin-1 (ISO-8859-1)** con prefijo de longitud i16.
- **CoordCipher** (`CoordCipher.cs` ↔ `coord_cipher.rs`): XorShift32 con seed 32-bit **generado por
  el server en login** y enviado en el paquete `Logged` (ID 0). Solo protege X/Y de 3 paquetes de
  click: LeftClick(22), RightClick(23), WorkLeftClick(24).
  - ⚠️ El seed se lee de `/dev/urandom` (**Linux-only**). En Windows falla → seed forzado a `1` →
    cipher determinista/débil. Como el server corre en Docker (Linux) esto está OK en la práctica.
- **PacketIds coinciden cliente/server** por diseño ("must match exactly"). Server→Client ~130
  opcodes; Client→Server ~90. `MultiMessage`(104) empaqueta 24 sub-mensajes de combate.
  `GenericText`(255) es fallback de texto.
- **Login (flujo real):** TCP connect → `HardwareCheck`(0) → `AccountLogin`(1) → server responde
  `InitAccount`(67) + `AddCharPreview`(64)×N + `SecurityCode`(65) → `CharacterLogin`(3) →
  `connect_user` dispara ~16 fases, pivote en `Logged`(0) (entrega charClass + coordSeed) →
  stats/inventario/hechizos → **EN JUEGO**.
- **No hay negociación de versión** en el handshake (el cliente no la envía, el server no la valida).
- Cliente conecta hardcodeado a `127.0.0.1:5028` (`client/Scripts/Main.cs:15-16`).

---

## Arquitectura

**Server (`server/source/`):**
- `game/handlers/` — un handler por sistema: `combat`, `combat_npc`, `combat_pvp`, `commerce`,
  `guilds_handler`, `leveling`, `movement`, `npcs`, `quests_party`, `slash_commands`, `social`,
  `spell_offensive`/`spell_support`, `gm_*`, `auth_*`, más `parity_gameplay.rs`/`parity_gm.rs`.
- `db/` (accounts, bans, charfile, guilds, password), `net/` (listener, connection, packet_framing),
  `protocol/` (byte_queue, coord_cipher, packets, `binary_packets/`), `game/` (world, zones, types,
  class_race, constants, npc), `data/` (loaders de `.dat`/`.ini`).

**Cliente (`client/Scripts/`):**
- `Main.cs` (+ `Main.Setup.cs`, `Main.Gameplay.cs`) — orquestador raíz.
- `Data/` — loaders (GRH, Body, WeaponShield, Map, `MapAdvancedLightsLoader`, Aura, Fx, Textos,
  fonts) + sistema `IResourceProvider`/`AopakResourceProvider`/`CompositeResourceProvider`.
- `Network/` — TcpClient, ByteQueue, CoordCipher, PacketIds, ClientPackets, `PacketHandler.*`.
- `Game/` — GameState, Character, LightSystem, ParticleSystem, ChatSystem, Input*, SoundManager, macros.
- `Rendering/` — WorldRenderer (pipeline de 4 pasadas), CharRenderer (5 capas), DayNightCycle,
  FogOverlayLayer, WeatherRenderer, GrhAnimator, FloatingTextManager.
- `UI/` — ~40 paneles sobre el sistema de theming propio `RpgTheme`.

**tools/:** `world-editor` (editor de mapas Godot standalone con su propio shader de luces y
walk-mode), `indexer`, `ExtractTsaoArchive`, `dateador`.

**resources/:** assets fuente (Graficos/Sounds/INIT/Maps) + `compressor/` (herramienta .NET que
genera los `.aopak`).

### Formato `.aopak`
Paquete propio cifrado (AES-CBC) + comprimido (Zlib), con TOC cifrada aparte, hash SHA-256 por
entrada, y soporte de layers/split/tombstone. Implementado en `resources/compressor/lib/`. El
cliente lo consume vía `IResourceProvider` (puede mezclar `.aopak` empaquetado + archivos sueltos
en disco). Los 6 paks viven en `client/Data/`: `fonts`, `graficos`, `init`, `maps`, `sounds`, `ui`.

---

## Estado actual del trabajo

- **Estado 2026-07-20:** se cerro una tanda grande de gameplay, UI, rendering y tooling:
  buscador de items GM con preview/creacion, mochila/equipamiento, preview de login con NPCs,
  `/salir` hacia seleccion de personaje, niebla configurable, drag-and-drop de inventario,
  haz de hechizo nuevo y Centinela anti-bot configurable en servidor.
- **Servidor:** Centinela quedo integrado a `tick_security`, comandos GM, respuesta de usuario y
  `TENGOMACROS`; tambien hay mejoras recientes en limpieza de conexiones, char indices, respawn
  de NPCs, comercio/crafteo, envenenar por ticks y validaciones varias.
- **Cliente:** el login backdrop usa mapa/NPCs con camara suave; UI incluye buscador de items,
  panel GM online mejorado, opciones de video para niebla, inventario con arrastre y render de
  hechizos/niebla refinado.
- **World editor/assets:** hay herramientas nuevas de particulas, pickers auxiliares, ventanas de
  mapa/color/GRH y assets/mapas regenerados para acompañar el cliente.

Nota operativa: el repo suele tener cambios masivos de assets. Antes de limpiar, resetear o borrar
archivos, confirmar siempre con Martin; las operaciones destructivas siguen siendo de alto riesgo.

---

## Documentación interna

- Índice: `docs/README.md` (nombra el proyecto "Tierras Sagradas AO").
- Skills accionables por subsistema en `docs/skills/`: combat, spells, npc-ai, crafting, rendering,
  **protocol** (el bueno), sprite-indexing, ui-kit, mapper.
- Arquitectura en `docs/architecture/`. ⚠️ `protocol.md` ahí es legado (ver sección Protocolo).
- Paridad VB6: `docs/DISCREPANCIAS-VB6-13.3.md`.
- Operaciones: `docs/LINUX_DEPLOY.md`, `docs/CLIENT-BUILD.md`, `docs/TROUBLESHOOTING.md`.
