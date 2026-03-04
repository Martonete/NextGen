# Argentum Online — Rust Server + Godot Client

Full rewrite of Argentum Online: **Rust server** (drop-in replacement for VB6 server) + **Godot 4 C# client** (replacing VB6 DirectX8 client). Based on the [Tierras Sagradas AO](https://github.com/cyphercr0w/Tierras-Sagradas-AO) mod. Protocol-compatible with original VB6 clients.

---

## Table of Contents

- [Project Layout](#project-layout)
- [Requirements](#requirements)
- [Quick Start — Docker (recommended)](#quick-start--docker-recommended)
- [Quick Start — Build from Source](#quick-start--build-from-source)
  - [Windows](#windows-from-source)
  - [macOS](#macos-from-source)
  - [Linux](#linux-from-source)
- [Client Setup (Godot 4.4 + C#)](#client-setup-godot-43--c)
  - [Windows](#client-on-windows)
  - [macOS](#client-on-macos)
  - [Linux](#client-on-linux)
- [Makefile Commands](#makefile-commands)
- [Client Distribution (standalone .exe)](#client-distribution-standalone-exe)
- [Server Configuration Reference](#server-configuration-reference)
- [Database](#database)
- [Game Data Files](#game-data-files)
- [Architecture Overview](#architecture-overview)
- [GM Commands](#gm-commands)
- [Troubleshooting](#troubleshooting)

---

## Project Layout

```
argentum-nextgen/
  server/
    source/          Rust server source code
      main.rs          Entry point
      data/            Data loaders (objects, spells, NPCs, maps)
      game/            Game logic (combat, movement, AI, handlers)
      net/             TCP networking and packet protocol
    dat/             Game data files (Obj.dat, NPCs.dat, Hechizos.dat, etc.)
    maps/            Map files (.map, .inf, .dat)
    charfile/        Character save files (legacy format)
    migrations/      PostgreSQL schema migrations
    server.ini       Server configuration
    logs/            Runtime logs
  client/
    Scenes/          Godot scenes (.tscn)
    Scripts/         C# source code
      Main.cs            Game loop and packet dispatch
      GameState.cs       Global game state
      PacketHandler.cs   120+ packet handlers
      InputHandler.cs    Keyboard/mouse input
      Rendering/
        CharRenderer.cs    Character rendering (body, head, weapon, shield, helmet, aura, FX)
        WorldRenderer.cs   4-pass tile rendering, lights, particles
    Data/
      INIT/            Asset indices, config files, auras, particles
      Graficos/        Sprite sheets (PNG)
      Maps/            Client-side map files
    project.godot    Godot 4.4 C# project file
  Cargo.toml         Rust dependencies
  Makefile           Build commands for server + client
  Dockerfile         Multi-stage Docker build (Rust 1.85 → Debian slim)
  docker-compose.yml PostgreSQL + ao-server orchestration
  .env.example       Database connection template
```

---

## Requirements

### Server (Docker — recommended)

| Requirement | Version | Notes |
|-------------|---------|-------|
| Docker Desktop | Latest | Includes Docker Compose v2 |

That's it. Docker handles Rust compilation, PostgreSQL, and networking.

### Server (from source)

| Requirement | Version | Notes |
|-------------|---------|-------|
| Rust | 1.85+ | Install via [rustup.rs](https://rustup.rs/) |
| PostgreSQL | 15+ | Any recent version works (15, 16, 17) |
| Make | Any | Pre-installed on macOS/Linux. Windows: see below |

### Client

| Requirement | Version | Notes |
|-------------|---------|-------|
| Godot | 4.4 **.NET** | Must be the .NET variant — standard Godot won't work |
| .NET SDK | 8.0 | Required for C# compilation |

---

## Quick Start — Docker (recommended)

Works on **Windows, macOS, and Linux** identically.

### 1. Install Docker Desktop

- **Windows**: https://www.docker.com/products/docker-desktop/ — enable WSL2 backend during install
- **macOS**: https://www.docker.com/products/docker-desktop/ — Apple Silicon and Intel supported
- **Linux**: Install Docker Engine + Docker Compose plugin:
  ```bash
  # Ubuntu/Debian
  sudo apt update && sudo apt install -y docker.io docker-compose-v2
  sudo usermod -aG docker $USER
  # Log out and back in for group change to take effect
  ```

### 2. Clone and start

```bash
git clone https://github.com/cyphercr0w/argentum-nextgen.git
cd argentum-nextgen

# Start server + database (first run downloads images and compiles — ~3-5 min)
docker compose up -d

# Verify it's running
docker compose logs -f ao-server
# Should show: "Listening on 0.0.0.0:5028"
```

### 3. Done

The server is running on `localhost:5028`. PostgreSQL is managed automatically inside Docker.

**Common commands:**
```bash
docker compose down          # Stop everything
docker compose up -d         # Start again
docker compose logs -f       # View logs
docker compose down -v       # Stop + delete database volume (fresh start)
docker compose build         # Rebuild after code changes
```

---

## Quick Start — Build from Source

### Windows (from source)

#### 1. Install Rust

Download and run the installer from https://rustup.rs/. Choose the default installation. This installs `rustc`, `cargo`, and `rustup`.

You also need the **Visual Studio C++ Build Tools**:
- Download from https://visualstudio.microsoft.com/visual-cpp-build-tools/
- Select "Desktop development with C++" workload

Restart your terminal after installation.

```powershell
rustc --version
# Should show 1.85.x or later
```

#### 2. Install PostgreSQL

Download from https://www.postgresql.org/download/windows/ and run the installer.

During installation:
- Remember the password you set for the `postgres` superuser
- Default port `5432` is fine
- Add PostgreSQL to PATH when prompted

After installation, open a terminal:
```powershell
psql -U postgres
# Enter the password you set during installation

CREATE USER ao WITH PASSWORD 'ao_secret';
CREATE DATABASE ao_server OWNER ao;
\q
```

#### 3. Install Make (optional)

Install via [Chocolatey](https://chocolatey.org/):
```powershell
choco install make
```

Or use `cargo` commands directly (see below).

#### 4. Clone, configure, and run

```powershell
git clone https://github.com/cyphercr0w/argentum-nextgen.git
cd argentum-nextgen

# Configure database connection
copy .env.example .env
# Edit .env if you changed the password:
# DATABASE_URL=postgres://ao:ao_secret@localhost:5432/ao_server

# Build (choose one)
make build                   # If Make is installed
cargo build --release        # Without Make

# Copy binary to server/ directory
copy target\release\ao-server.exe server\

# Run
cd server
.\ao-server.exe .
```

### macOS (from source)

#### 1. Install Rust

```bash
curl --proto '=https' --tlsv1.2 -sSf https://sh.rustup.rs | sh
source $HOME/.cargo/env
rustc --version
```

#### 2. Install PostgreSQL

```bash
# Via Homebrew (recommended)
brew install postgresql@17
brew services start postgresql@17

# Create database and user
psql postgres -c "CREATE USER ao WITH PASSWORD 'ao_secret';"
psql postgres -c "CREATE DATABASE ao_server OWNER ao;"
```

#### 3. Clone, configure, and run

```bash
git clone https://github.com/cyphercr0w/argentum-nextgen.git
cd argentum-nextgen

cp .env.example .env
# Edit .env if needed

make build && make run
```

### Linux (from source)

#### 1. Install Rust

```bash
curl --proto '=https' --tlsv1.2 -sSf https://sh.rustup.rs | sh
source $HOME/.cargo/env
rustc --version
```

#### 2. Install PostgreSQL

```bash
# Ubuntu/Debian
sudo apt update
sudo apt install -y postgresql postgresql-client build-essential

# Fedora/RHEL
sudo dnf install -y postgresql-server postgresql postgresql-devel gcc make
sudo postgresql-setup --initdb
sudo systemctl start postgresql

# Arch
sudo pacman -S postgresql gcc make
sudo -u postgres initdb -D /var/lib/postgres/data
sudo systemctl start postgresql
```

Create database and user:
```bash
sudo -u postgres psql -c "CREATE USER ao WITH PASSWORD 'ao_secret';"
sudo -u postgres psql -c "CREATE DATABASE ao_server OWNER ao;"
```

#### 3. Clone, configure, and run

```bash
git clone https://github.com/cyphercr0w/argentum-nextgen.git
cd argentum-nextgen

cp .env.example .env
make build && make run
```

---

## Client Setup (Godot 4.4 + C#)

The client requires two things: **Godot 4.4 .NET** and **.NET 8.0 SDK**.

### Install .NET 8.0 SDK

| Platform | Instructions |
|----------|-------------|
| **Windows** | Download installer from https://dotnet.microsoft.com/download/dotnet/8.0 |
| **macOS** | Download installer, or: `brew install dotnet@8` |
| **Linux** | `sudo apt install -y dotnet-sdk-8.0` (Ubuntu/Debian) or see https://learn.microsoft.com/en-us/dotnet/core/install/linux |

Verify:
```bash
dotnet --version
# Should show 8.x.x
```

### Install Godot 4.4 (.NET version)

Download **Godot 4.4 — .NET** from: https://godotengine.org/download/archive/4.4-stable/

> **IMPORTANT:** You must download the **.NET** variant. The standard version cannot compile C# scripts and **will not work**.

| Platform | File to download |
|----------|-----------------|
| **Windows** | `Godot_v4.4-stable_mono_win64.exe` |
| **macOS** | `Godot_v4.4-stable_mono_macos.universal.zip` |
| **Linux** | `Godot_v4.4-stable_mono_linux_x86_64.zip` |

### Client on Windows

```powershell
# Option A: From command line
cd client
dotnet build
# Then open Godot editor → open project.godot → press F5

# Option B: Using Make
make client           # builds C# + launches game
make client-editor    # opens Godot editor
```

### Client on macOS

```bash
# Add Godot to PATH (adjust path to where you extracted it)
export PATH="/Applications/Godot_mono.app/Contents/MacOS:$PATH"

make client           # builds + runs
make client-editor    # opens editor
```

### Client on Linux

```bash
# Add Godot to PATH (adjust path)
export PATH="/opt/godot:$PATH"

make client           # builds + runs
make client-editor    # opens editor

# Or manually:
cd client && dotnet build
godot --path .
```

### Client connection

The client connects to `127.0.0.1:5028` by default. Make sure the server is running before launching the client.

> **Important:** Run `make client-build` (or `dotnet build` in `client/`) every time you pull new changes or modify C# code. Godot runs the **compiled** assemblies, not source files directly.

---

## Makefile Commands

All commands run from the repo root. Type `make help` for the full list.

### Server

| Command | Description |
|---------|-------------|
| `make build` | Compile Rust server (release) and copy binary to `server/` |
| `make run` | Build + run server locally |
| `make dev` | Run server in dev mode (`cargo run`, faster compile, debug symbols) |
| `make test` | Run all server tests (68 unit tests) |
| `make clean` | Clean all build artifacts (server + client) |

### Server (Docker)

| Command | Description |
|---------|-------------|
| `make docker-run` | Build Docker image + `docker compose up -d` |
| `make docker-stop` | `docker compose down` |
| `make docker-logs` | Follow server logs in real time |

### Client

| Command | Description |
|---------|-------------|
| `make client` | Build C# + run game (shortcut) |
| `make client-build` | Compile C# only (`dotnet build`) |
| `make client-run` | Build C# + run game |
| `make client-editor` | Open Godot editor |
| `make client-clean` | Clean C# build artifacts |

You can override the Godot path: `make client GODOT=/path/to/godot`

### Typical workflow

```bash
# First time / after git pull:
make docker-run       # start server + database
make client           # compile + play

# After code changes:
make client-build     # recompile C# only (fast)

# Server code changes (Docker):
docker compose build && docker compose up -d

# Server code changes (local):
make run              # rebuilds + runs

# Run everything at once:
make docker-run client-run
```

---

## Client Distribution (standalone .exe)

To share the client with players, you export a standalone executable. Players don't need to install Godot, .NET, or anything else.

### Architecture: EXE + PCK + Data

The client uses a **split build** to enable lightweight updates:

| Component | Size | Changes? | Description |
|-----------|------|----------|-------------|
| `TierrasSagradasAO.exe` | ~82MB | Never (unless Godot version changes) | Godot engine + .NET runtime |
| `TierrasSagradasAO.pck` | ~180KB | Every code change | Compiled scenes + scripts |
| `data_TierrasSagradasAO_*/` | ~150MB | Never (unless Godot version changes) | .NET runtime DLLs |
| `Data/` | ~217MB | Only when assets change | Game assets (sprites, sounds, maps, config) |

**Key point:** `Data/` is loaded from the filesystem at runtime, NOT from the PCK. The `.gdignore` file in `Data/` prevents Godot from importing/packing these assets. All loaders (`TextureManager`, `SoundManager`, `MapLoader`, etc.) use `System.IO.File` directly.

### Prerequisites (one-time setup)

You need these on **your** machine (the one doing the export):

1. **Godot 4.4 .NET** (already installed if you develop)
2. **.NET 8.0 SDK** (already installed if you develop)
3. **Godot Export Templates** — open Godot editor → `Editor` → `Manage Export Templates` (`Gestionar plantillas de exportación` in Spanish) → `Download and Install` (~800MB, one time)
4. **Make** (optional) — simplifies the process to a single command

> **Note:** You do NOT need Rust installed to export the client. Rust is only for the server.

### Configure the server IP

Before exporting, set the server IP in `client/Scripts/Main.cs` line 15:

```csharp
private const string ServerHost = "123.45.67.89"; // Your server's public IP
```

For local testing use `127.0.0.1`. For production, use the VPS public IP.

### First-time setup

If this is the first time exporting (or after a fresh clone), you need a `.sln` file:

1. Open Godot editor with the client project
2. `Project` → `Tools` → `C#` → `Create C# Solution` (`Proyecto` → `Herramientas` → `C#` → `Crear solución de C#`)

Also, if a `client/test/` folder exists, delete it — it contains standalone test files that conflict with the export:

```powershell
# Windows
Remove-Item -Recurse -Force client\test

# Linux/macOS
rm -rf client/test
```

### Export settings (already configured)

The export preset (`client/export_presets.cfg`) is pre-configured:

- **`embed_pck=false`** — generates a separate `.pck` file instead of embedding it in the .exe
- **`export_filter="selected_scenes"`** — only packs `Scenes/Main.tscn` and its dependencies (scripts)
- **`exclude_filter="test/*, Data/*"`** — excludes test files and all game data from the PCK
- **`Data/.gdignore`** — prevents Godot from importing assets into `.godot/imported/`

> **Do NOT change these settings.** They ensure the PCK stays small (~180KB) for fast updates.

### Option A: Using Make (Linux/macOS, or Windows with Make installed)

```bash
make client-dist
```

This generates `dist/TierrasSagradasAO.zip` — ready to send.

To install Make on Windows: `choco install make` ([Chocolatey](https://chocolatey.org/))

### Option B: Manual (Windows PowerShell, no Make)

```powershell
cd client

# 1. Compile C#
dotnet build

# 2. Create build folder
mkdir build -Force

# 3. Export (generates .exe + .pck + data_*/ separately)
& "C:\path\to\Godot_v4.4-stable_mono_win64.exe" --headless --export-release "Windows Desktop" build\TierrasSagradasAO.exe

# 4. Copy game data alongside the exe (only needed ONCE, or after Data/ changes)
Copy-Item -Recurse Data build\

# 5. Zip for distribution
Compress-Archive -Path build\* -DestinationPath ..\TierrasSagradasAO.zip
```

### Option C: Manual (Linux/macOS, no Make)

```bash
cd client

# 1. Compile C#
dotnet build

# 2. Create build folder
mkdir -p build

# 3. Export (generates .exe + .pck + data_*/ separately)
godot --headless --export-release "Windows Desktop" build/TierrasSagradasAO.exe

# 4. Copy game data (only needed ONCE, or after Data/ changes)
cp -r Data build/Data

# 5. Zip
cd build && zip -r ../../TierrasSagradasAO.zip . && cd ..
```

### What the player receives (first install)

```
TierrasSagradasAO/
  TierrasSagradasAO.exe              Godot engine (~82MB)
  TierrasSagradasAO.pck              Game logic — scenes + scripts (~180KB)
  data_TierrasSagradasAO_windows/    .NET runtime DLLs (~150MB)
  Data/
    Graficos/                        Sprites and UI textures (~90MB)
    INIT/                            Config files, auras, particles, fonts (~1MB)
    Maps/                            Client-side map data (~7MB)
    Sounds/                          Sound effects and music (~113MB)
```

The player just unzips and runs `TierrasSagradasAO.exe`. No installation needed.

Source code (`.cs` files) is **not** included — it's compiled into the PCK.

### Updating the client (after code changes)

When you change code (C# logic, UI, bug fixes, etc.) you only need to re-export and send the `.pck`:

```powershell
cd client
dotnet build
& "C:\path\to\Godot_v4.4-stable_mono_win64.exe" --headless --export-release "Windows Desktop" build\TierrasSagradasAO.exe
```

Then send **only `TierrasSagradasAO.pck`** (~180KB) to the tester. They replace their `.pck` and done.

| What changed | What to send | Size |
|---|---|---|
| C# code, scenes, UI logic | `TierrasSagradasAO.pck` | ~180KB |
| Game assets (new map, sound, sprite) | The specific file(s) for `Data/` | Variable |
| Godot engine version upgrade | Full re-export (.exe + data_*/) | ~230MB |
| First install | Everything (zip) | ~450MB |

### Patch PCK system (advanced)

The client supports **patch PCK files** for delta updates without replacing the base `.pck`:

1. Export a patch PCK containing only modified resources
2. Name it `patch.pck` (or `patch_001.pck`, `patch_002.pck` for ordered patches)
3. Place it next to the `.exe`

On startup, `Main.cs` loads patch files automatically via `ProjectSettings.LoadResourcePack()`. Later patches override earlier ones. This is useful for hotfixes without replacing the full PCK.

### Server deployment (VPS)

To run the server on a VPS for players to connect:

```bash
# 1. Generate SSH key and add to GitHub
ssh-keygen -t ed25519 -C "your@email.com" -f ~/.ssh/id_ed25519 -N ""
cat ~/.ssh/id_ed25519.pub
# Copy the output → GitHub → Settings → SSH Keys → New SSH key

# 2. Install Docker
curl -fsSL https://get.docker.com | sh
sudo usermod -aG docker $USER
newgrp docker

# 3. Clone and start server
git clone git@github.com:cyphercr0w/argentum-nextgen.git
cd argentum-nextgen
docker compose up -d

# 4. Verify
docker compose logs -f ao-server
# Should show: "Listening on 0.0.0.0:5028"

# 5. Open firewall port
sudo ufw allow 5028/tcp    # Ubuntu
```

Make sure port **5028 TCP** is also open in your VPS provider's firewall/security group.

---

## Server Configuration Reference

### server.ini

The server reads `server/server.ini` on startup. All settings are in INI format.

#### Network

| Setting | Default | Description |
|---------|---------|-------------|
| `ServerIp` | `127.0.0.1` | Bind address (use `0.0.0.0` for external access) |
| `StartPort` | `5028` | Game server TCP port |
| `MaxUsers` | `400` | Maximum concurrent player connections |
| `Encriptar` | `1` | Enable packet encryption (`0` = plaintext, `1` = encrypted) |

#### Game

| Setting | Default | Description |
|---------|---------|-------------|
| `PuedeCrearPersonajes` | `1` | Allow new character creation |
| `AllowMultiLogins` | `1` | Allow multiple characters from same account |
| `IdleLimit` | `10` | Minutes before idle players are disconnected |
| `ServerSoloGMs` | `0` | Restrict server to GM accounts only |
| `Testing` | `0` | Enable testing mode (relaxed restrictions) |
| `CamaraLenta` | `0` | Slow motion mode (debugging) |

#### Multipliers

| Setting | Default | Description |
|---------|---------|-------------|
| `MultiplicadordeExp` | `1` | Experience gain multiplier (e.g., `2` = double XP) |
| `MultiplicadordeOro` | `1` | Gold drop multiplier |
| `MultiplicadordeDrop` | `1` | Item drop rate multiplier |

#### Starting Position

| Setting | Default | Description |
|---------|---------|-------------|
| `StartMap` | `89` | Map number for new characters |
| `StartX` | `78` | X coordinate for new characters |
| `StartY` | `85` | Y coordinate for new characters |

#### Version

| Setting | Default | Description |
|---------|---------|-------------|
| `Version` | `0.11.5` | Server version string |
| `ClientVersion` | `1.0.1` | Expected client version |
| `Notice` | (text) | Login screen notice message |

#### Privilege Levels (GM hierarchy)

Configured in `server.ini` under named sections. Hierarchy from highest to lowest:

| Level | Section | Permission scope |
|-------|---------|-----------------|
| Admin | `[Administradores]` | All commands, server management |
| SubAdmin | `[SubAdministradores]` | Most commands, no server shutdown |
| Desarrollador | `[Desarrolladores]` | Debug commands, item/NPC creation |
| Director | `[Directores]` | Event management, moderate commands |
| Gran Dios | `[GranDioses]` | Advanced moderation |
| Dios | `[Dioses]` | Standard moderation |
| Events | `[Events]` | Event-only commands |
| SemiDios | `[SemiDioses]` | Limited moderation |
| Consejero | `[Consejeros]` | Help and advisory role |
| RolesMaster | `[RolesMasters]` | RP event management |

Each section lists the character names with that privilege level:
```ini
[Administradores]
Count=1
1=Shay
```

### Intervalos.ini

Located at `server/dat/Intervalos.ini`. Controls anti-speedhack timers (in server ticks of 40ms each):

| Setting | Default (ticks) | Approx. ms | Description |
|---------|----------------|------------|-------------|
| `Golpe` | `37` | ~1480ms | Melee attack cooldown |
| `Flechas` | `28` | ~1120ms | Ranged attack cooldown |
| `LanzarHechizo` | `13` | ~520ms | Spell cast cooldown |
| `PoteoU` | `8` | ~320ms | Potion use cooldown |
| `PoteoClick` | `6` | ~240ms | Potion click cooldown |
| `Work` | `10` | ~400ms | Crafting/resource cooldown |

### Environment Variables

| Variable | Default | Description |
|----------|---------|-------------|
| `DATABASE_URL` | `postgres://ao:ao_secret@localhost:5432/ao_server` | PostgreSQL connection string |
| `POSTGRES_PASSWORD` | `ao_secret` | Used by docker-compose for the PostgreSQL container |
| `RUST_LOG` | `ao_server=info` | Log verbosity (`debug`, `info`, `warn`, `error`) |

---

## Database

### PostgreSQL Setup

The server uses **PostgreSQL** for persistent storage. Migrations run automatically on startup.

**Docker:** PostgreSQL is included in `docker-compose.yml` — no manual setup needed.

**From source:** Create the database as shown in the platform-specific instructions above.

### Schema (auto-migrated)

| Table | Description |
|-------|-------------|
| `accounts` | Login credentials, banking, ban status |
| `account_friends` | Friend list (max 20 per account) |
| `account_bank` | Account-level bank storage (max 40 slots) |
| `characters` | Full character state (stats, position, skills, flags, equipment) |
| `character_inventory` | Inventory items (max 25 slots, tracks equipped status) |
| `character_bank` | Character bank items (max 40 slots) |
| `guilds` | Guild metadata, level, points, castle ownership |
| `guild_members` | Guild membership |
| `guild_applicants` | Pending guild applications |
| `guild_bank_items` | Guild bank storage |
| `banned_ips` | IP ban list |
| `banned_hds` | Hardware ID ban list |
| `rankings` | Leaderboard data (9 categories x 10 positions) |

### Backup & Restore

```bash
# Backup (Docker)
docker compose exec postgres pg_dump -U ao ao_server > backup.sql

# Backup (local)
pg_dump -U ao ao_server > backup.sql

# Restore
psql -U ao ao_server < backup.sql
```

---

## Game Data Files

All game data lives in `server/dat/`. These are INI-format files loaded on server startup.

| File | Description |
|------|-------------|
| `Obj.dat` | Item definitions (650+ items: weapons, armor, potions, keys, boats, etc.) |
| `NPCs.dat` | NPC definitions (stats, AI type, loot tables, spells, appearance) |
| `Hechizos.dat` | Spell definitions (100+ spells: damage, healing, buffs, summons) |
| `Body.dat` | Character body sprite mappings |
| `Head.dat` | Character head sprite mappings |
| `Experiencia.dat` | XP required per level (1-50) |
| `Balance.dat` | Combat balance parameters |
| `ClassBonus.dat` | Class attribute bonuses per level |
| `Cofres.dat` | Treasure chest loot tables |
| `QUESTS.DAT` | Quest definitions |
| `MisionesDiarias.dat` | Daily mission templates |
| `Casas.dat` | House/property definitions |
| `Ciudades.Dat` | City spawn points |
| `Intervalos.ini` | Anti-speedhack timers |
| `Motd.ini` | Message of the Day (shown at login) |
| `facciones.ini` | Faction rank thresholds and config |
| `configuracion.ini` | Guild levels, castle ownership, invocation NPCs |

### Map Files

Maps are stored in `server/maps/` as three files per map:

| Extension | Description |
|-----------|-------------|
| `.map` | Tile data (walkability, triggers, exits) |
| `.inf` | Static objects (NPCs, ground items, teleports) |
| `.dat` | Tile graphics (layers 1-4) |

---

## Architecture Overview

### Server

```
TCP Connection (port 5028)
  ↓
Packet Decryption (AoDefDecode → DeCodificar XOR)
  ↓
Text Protocol Parsing (Latin-1 encoded commands)
  ↓
Handler Dispatch (game/handlers/mod.rs)
  ↓
Game Logic (combat, movement, spells, inventory, etc.)
  ↓
State Mutation (GameState — players, NPCs, world grid)
  ↓
Response Packets → Encryption → TCP Send
```

**Game Loop Timers:**

| Timer | Interval | Purpose |
|-------|----------|---------|
| Game tick | 40ms | Anti-cheat cooldown decrements, movement validation |
| NPC AI | ~1300ms | NPC movement, aggro detection, spell casting |
| Respawn | 30s | Revive dead NPCs at spawn points |
| Player passive | 1s | Hunger/thirst drain, stamina regen, poison damage |
| World cleanup | 60s | Remove old ground items (10 min TTL) |

**Key technologies:**
- **Tokio** — async runtime for TCP and timers
- **SQLx** — async PostgreSQL with compile-time query checking
- **Argon2** — password hashing

### Client

```
Godot 4.4 Main Loop (60 FPS)
  ↓
InputHandler.cs — keyboard/mouse → packet commands
  ↓
TCP Connection → Encrypt → Send
  ↓
Receive → Decrypt → PacketHandler.cs (120+ handlers)
  ↓
GameState.cs — update world state, characters, UI
  ↓
Rendering Pipeline:
  WorldRenderer.cs — 4-pass tiles (floor, objects, roofs, translucent)
  CharRenderer.cs  — body/head/weapon/shield/helmet/aura/FX/shadow/names
```

**Rendering:** Fixed 800x600 viewport, 32px tiles, GL Compatibility renderer.

---

## GM Commands

GM commands are typed in the chat window with a `/` prefix. Access depends on privilege level.

### Movement & Teleport

| Command | Level | Description |
|---------|-------|-------------|
| `/IRA <map> <x> <y>` | SemiDios+ | Teleport to coordinates |
| `/IRCIUDAD <city>` | SemiDios+ | Teleport to a city |
| `/IRCERCA <player>` | SemiDios+ | Teleport near a player |
| `/TRAER <player>` | Dios+ | Summon a player to your position |
| `/SEGUIR` | SemiDios+ | Toggle invisible follow mode |

### Items & Objects

| Command | Level | Description |
|---------|-------|-------------|
| `/CI <obj> <amount>` | Dios+ | Create item in inventory |
| `/HACERITEM <obj>@<amount>` | GranDios+ | Create item on the ground |
| `/DEST` | Dios+ | Destroy floor object at your position |
| `/MASSDEST` | Admin | Destroy all non-fixture floor objects in visible area |
| `/LIMPIAR <map>` | Admin | Clean all dropped items on a map (preserves fixtures) |

### NPCs

| Command | Level | Description |
|---------|-------|-------------|
| `/ACC <npc>` | Dios+ | Spawn NPC at your position |
| `/MASSPAWN <npc> <count>` | Admin | Spawn multiple NPCs |
| `/RNPC` | Dios+ | Remove NPC at your position |
| `/RMATA` | Admin | Kill all NPCs on your map |
| `/NENE <map>` | SemiDios+ | List hostile NPCs on a map |
| `/RESETINV` | Dios+ | Reset targeted NPC's inventory to defaults |
| `/NPCAURA <npc_index> <aura>` | Admin | Set aura on a live NPC |

### Players

| Command | Level | Description |
|---------|-------|-------------|
| `/MOD <stat> <value>` | Admin | Modify player attribute |
| `/SETLEVEL <level>` | Admin | Set player level |
| `/REVIVIR <player>` | SemiDios+ | Revive a dead player |
| `/MATAR <player>` | Dios+ | Kill a player |
| `/BAN <player>` | Dios+ | Ban a player |
| `/KICK <player>` | SemiDios+ | Disconnect a player |
| `/NAVE <player>` | Consejero+ | Check player navigation status |

### Events

| Command | Level | Description |
|---------|-------|-------------|
| `/REY` | Admin | Spawn Ancalagon event (pre-dragon + 4 guardians on map 123) |
| `/LLUVIA` | Dios+ | Toggle rain |
| `/NOCHE` | Dios+ | Toggle night mode |

### Server

| Command | Level | Description |
|---------|-------|-------------|
| `/SAVE` | Admin | Force save all characters |
| `/RELOADNPCS` | Admin | Reload NPC database |
| `/RELOADOBJS` | Admin | Reload object database |
| `/INFO` | SemiDios+ | Show server info (uptime, players, etc.) |

> This is a partial list. Over 100 GM commands are implemented. Type `/HELP` in-game for the full list.

---

## Troubleshooting

### Server

| Problem | Solution |
|---------|----------|
| "Listening on 0.0.0.0:5028" never appears | Check PostgreSQL is running and `DATABASE_URL` is correct |
| "connection refused" to database | Start PostgreSQL: `sudo systemctl start postgresql` or `docker compose up -d postgres` |
| Port 5028 already in use | Kill existing process: `lsof -i :5028` / `netstat -tlnp \| grep 5028` |
| Docker build fails on Rust compile | Ensure Docker has at least 4GB RAM allocated |
| "permission denied" on Linux | Add user to docker group: `sudo usermod -aG docker $USER` |
| Server crashes on startup | Check `server/logs/` for details. Ensure all `.dat` files exist in `server/dat/` |
| Database migration errors | Drop and recreate: `psql -U ao -c "DROP DATABASE ao_server; CREATE DATABASE ao_server OWNER ao;"` |

### Client

| Problem | Solution |
|---------|----------|
| "Unable to find .NET SDK" | Install .NET 8.0 SDK, restart terminal and Godot |
| C# build errors on first open | Run `make client-build` or `dotnet build` in `client/` |
| "Connection refused" | Start the server first (`make docker-run` or `make run`) |
| Assets missing / white textures | Ensure `client/Data/` contains `INIT/`, `Graficos/`, `Maps/` folders with game assets |
| Changes not taking effect | Run `make client-build` before launching — Godot runs compiled assemblies |
| Godot says "not a .NET project" | You downloaded the wrong Godot version — get the **.NET** variant |
| Low FPS / rendering issues | Open in-game options (F10) → Render tab → adjust settings |
| Can't find Godot binary | Set path: `make client GODOT=/path/to/godot` |

### General

```bash
# Check server is running
curl -v telnet://localhost:5028   # or: nc -z localhost 5028

# Check database
psql -U ao -d ao_server -c "SELECT count(*) FROM accounts;"

# Docker: check container status
docker compose ps

# Docker: rebuild from scratch
docker compose down -v && docker compose up -d --build

# View server logs (Docker)
docker compose logs -f ao-server

# View server logs (local)
tail -f server/logs/*.log
```

---

## Ports

| Port | Protocol | Description |
|------|----------|-------------|
| `5028` | TCP | Game server (client connects here) |
| `5432` | TCP | PostgreSQL (Docker internal — not exposed to host by default) |

---

## License

Private repository. All rights reserved.
