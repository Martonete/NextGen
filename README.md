# Argentum Online — Rust Server + Godot Client

Full rewrite of Argentum Online: **Rust server** (drop-in replacement for VB6 server) + **Godot 4 C# client** (replacing VB6 DirectX8 client). Based on the [Tierras Sagradas AO](https://github.com/cyphercr0w/Tierras-Sagradas-AO) mod. Protocol-compatible with original VB6 clients.

---

## Table of Contents

1. [Project Layout](#project-layout)
2. [Dev Environment Setup (Windows)](#dev-environment-setup-windows) — **start here**
3. [Dev Environment Setup (Linux)](#dev-environment-setup-linux)
4. [Dev Environment Setup (macOS)](#dev-environment-setup-macos)
5. [Server via Docker](#server-via-docker)
6. [Makefile Commands](#makefile-commands)
7. [Client Distribution (standalone .exe)](#client-distribution-standalone-exe)
8. [Server Deployment (VPS)](#server-deployment-vps)
9. [Ports](#ports)
10. [Troubleshooting](#troubleshooting)
11. [Further Documentation](#further-documentation)

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

## Dev Environment Setup (Windows)

This is the primary development flow. Follow these steps in order.

### Step 1 — Install prerequisites

You need **4 things** installed. Install them in this order:

| # | Tool | Version | Download |
|---|------|---------|----------|
| 1 | **.NET SDK** | **8.0** (not 9.0!) | https://dotnet.microsoft.com/download/dotnet/8.0 |
| 2 | **Godot** | **4.4 .NET** | https://godotengine.org/download/archive/4.4-stable/ |
| 3 | **Rust** | 1.85+ | https://rustup.rs/ |
| 4 | **PostgreSQL** | 15+ | https://www.postgresql.org/download/windows/ |

> **CRITICAL — Read carefully:**
>
> - **.NET SDK must be version 8.0.** Godot 4.4 does NOT support .NET 9. If you have .NET 9 installed, that's fine — install 8.0 alongside it (they coexist without conflicts).
> - **Godot must be the .NET variant.** The standard Godot download **cannot compile C#** and will not work. Look for files named `Godot_v4.4-stable_mono_win64.exe`. If you don't see `C#` under `Project → Tools` in Godot, you have the wrong version.
> - **Rust also needs Visual Studio C++ Build Tools:** download from https://visualstudio.microsoft.com/visual-cpp-build-tools/ and select "Desktop development with C++" workload.

Verify after installation (restart terminal first):

```powershell
dotnet --version    # Must show 8.x.x
rustc --version     # Must show 1.85.x or later
psql --version      # Must show 15.x or later
```

### Step 2 — Set up PostgreSQL

```powershell
psql -U postgres
# Enter the password you set during installation

CREATE USER ao WITH PASSWORD 'ao_secret';
CREATE DATABASE ao_server OWNER ao;
\q
```

### Step 3 — Clone and configure

```powershell
git clone https://github.com/cyphercr0w/argentum-nextgen.git
cd argentum-nextgen

copy .env.example .env
# Edit .env if you changed the database password:
# DATABASE_URL=postgres://ao:ao_secret@localhost:5432/ao_server
```

### Step 4 — Build and run the server

```powershell
cargo build --release
copy target\release\ao-server.exe server\
cd server
.\ao-server.exe .
# Should show: "Listening on 0.0.0.0:5028"
```

Leave this terminal open — the server needs to be running for the client.

### Step 5 — Build and run the client

Open a **new terminal**:

```powershell
cd argentum-nextgen\client

# 1. Build C# assemblies (MUST do this before opening Godot)
dotnet build

# 2. Open the project in Godot
# Either double-click project.godot or:
& "C:\path\to\Godot_v4.4-stable_mono_win64.exe" --path .
```

Inside Godot:
1. If this is the first time: `Project` → `Tools` → `C#` → `Create C# Solution`
2. Press **F5** to run the game

> **Every time you modify C# code**, run `dotnet build` in `client/` before running in Godot. Godot runs compiled assemblies, not source files.

### Common errors on first setup

| Error | Cause | Fix |
|-------|-------|-----|
| "Failed to load due to missing dependencies" on Main.cs | C# not compiled | Run `dotnet build` in `client/` first |
| No `C#` option under `Project → Tools` | Wrong Godot version | Download the **.NET** variant of Godot 4.4 |
| "Failed to build project" in MSBuild panel | .NET version mismatch | Install .NET SDK **8.0** (not 9.0) |
| `dotnet --version` shows 9.x | .NET 9 installed but not 8 | Install .NET 8.0 SDK alongside it |

---

## Dev Environment Setup (Linux)

### Step 1 — Install prerequisites

```bash
# Rust
curl --proto '=https' --tlsv1.2 -sSf https://sh.rustup.rs | sh
source $HOME/.cargo/env

# .NET 8.0 SDK
sudo apt install -y dotnet-sdk-8.0   # Ubuntu/Debian
# Or see: https://learn.microsoft.com/en-us/dotnet/core/install/linux

# PostgreSQL
sudo apt install -y postgresql postgresql-client build-essential

# Godot 4.4 .NET — download from:
# https://godotengine.org/download/archive/4.4-stable/
# Extract and add to PATH:
export PATH="/opt/godot:$PATH"
```

### Step 2 — Set up PostgreSQL

```bash
sudo -u postgres psql -c "CREATE USER ao WITH PASSWORD 'ao_secret';"
sudo -u postgres psql -c "CREATE DATABASE ao_server OWNER ao;"
```

### Step 3 — Clone, build, and run

```bash
git clone https://github.com/cyphercr0w/argentum-nextgen.git
cd argentum-nextgen

cp .env.example .env
make build && make run
# Server: "Listening on 0.0.0.0:5028"

# In another terminal:
cd client && dotnet build
godot --path .    # Opens editor, press F5 to play
```

---

## Dev Environment Setup (macOS)

### Step 1 — Install prerequisites

```bash
# Rust
curl --proto '=https' --tlsv1.2 -sSf https://sh.rustup.rs | sh
source $HOME/.cargo/env

# .NET 8.0 SDK
brew install dotnet@8

# PostgreSQL
brew install postgresql@17
brew services start postgresql@17

# Godot 4.4 .NET — download from:
# https://godotengine.org/download/archive/4.4-stable/
export PATH="/Applications/Godot_mono.app/Contents/MacOS:$PATH"
```

### Step 2 — Set up PostgreSQL

```bash
psql postgres -c "CREATE USER ao WITH PASSWORD 'ao_secret';"
psql postgres -c "CREATE DATABASE ao_server OWNER ao;"
```

### Step 3 — Clone, build, and run

```bash
git clone https://github.com/cyphercr0w/argentum-nextgen.git
cd argentum-nextgen

cp .env.example .env
make build && make run
# Server: "Listening on 0.0.0.0:5028"

# In another terminal:
cd client && dotnet build
make client    # or: godot --path .
```

---

## Server via Docker

If you only want the **server** running quickly (any OS), Docker handles everything:

```bash
# Install Docker Desktop: https://www.docker.com/products/docker-desktop/
# Linux: sudo apt install -y docker.io docker-compose-v2

git clone https://github.com/cyphercr0w/argentum-nextgen.git
cd argentum-nextgen

docker compose up -d
docker compose logs -f ao-server
# Should show: "Listening on 0.0.0.0:5028"
```

Docker manages Rust compilation, PostgreSQL, and networking automatically. No need to install Rust or PostgreSQL separately.

```bash
docker compose down          # Stop
docker compose up -d         # Start
docker compose logs -f       # Logs
docker compose down -v       # Reset (delete database)
docker compose build         # Rebuild after code changes
```

> **Note:** Docker only runs the server. You still need Godot + .NET 8.0 SDK on your machine for the client.

---

## Makefile Commands

All commands run from the repo root. `make help` for the full list.

| Command | Description |
|---------|-------------|
| `make build` | Compile Rust server (release) + copy to `server/` |
| `make run` | Build + run server |
| `make dev` | Dev mode (`cargo run`, faster compile) |
| `make test` | Run 68 unit tests |
| `make clean` | Clean all build artifacts |
| `make docker-run` | Docker build + start |
| `make docker-stop` | Docker stop |
| `make docker-logs` | Docker follow logs |
| `make client` | Build C# + run game |
| `make client-build` | Compile C# only |
| `make client-editor` | Open Godot editor |
| `make client-clean` | Clean C# artifacts |
| `make client-dist` | Export standalone .exe zip |

Override Godot path: `make client GODOT=/path/to/godot`

---

## Client Distribution (standalone .exe)

To share the client with players who don't have Godot or .NET installed.

### Architecture: EXE + PCK + Data

| Component | Size | Changes? | Description |
|-----------|------|----------|-------------|
| `TierrasSagradasAO.exe` | ~82MB | Rarely | Godot engine + .NET runtime |
| `TierrasSagradasAO.pck` | ~180KB | Every code change | Compiled scenes + scripts |
| `data_TierrasSagradasAO_*/` | ~150MB | Rarely | .NET runtime DLLs |
| `Data/` | ~217MB | Only when assets change | Game assets (sprites, sounds, maps) |

`Data/` is loaded from the filesystem at runtime (not packed). All loaders use `System.IO.File`.

### Export steps (Windows)

Prerequisites: Godot 4.4 .NET + .NET 8.0 SDK + Export Templates (Godot → `Editor` → `Manage Export Templates` → `Download and Install`).

Before exporting, set server IP in `client/Scripts/Main.cs`:
```csharp
private const string ServerHost = "123.45.67.89"; // Your server's public IP
```

First-time only: open Godot → `Project` → `Tools` → `C#` → `Create C# Solution`.

```powershell
cd client
dotnet build
mkdir build -Force
& "C:\path\to\Godot_v4.4-stable_mono_win64.exe" --headless --export-release "Windows Desktop" build\TierrasSagradasAO.exe
Copy-Item -Recurse Data build\
Compress-Archive -Path build\* -DestinationPath ..\TierrasSagradasAO.zip
```

Or with Make: `make client-dist`

### Updating (after code changes)

Re-export and send **only `TierrasSagradasAO.pck`** (~180KB). Players replace their `.pck` and done.

| What changed | What to send | Size |
|---|---|---|
| C# code, scenes, UI | `.pck` only | ~180KB |
| Game assets | Specific `Data/` files | Variable |
| Godot version upgrade | Full re-export | ~230MB |
| First install | Everything (zip) | ~450MB |

### Patch PCK (advanced)

Drop `patch.pck` or `patch_001.pck` next to the `.exe` — loaded automatically on startup via `ProjectSettings.LoadResourcePack()`.

---

## Server Deployment (VPS)

```bash
# 1. Install Docker
curl -fsSL https://get.docker.com | sh
sudo usermod -aG docker $USER && newgrp docker

# 2. Clone and start
git clone git@github.com:cyphercr0w/argentum-nextgen.git
cd argentum-nextgen
docker compose up -d

# 3. Open firewall
sudo ufw allow 5028/tcp
```

Ensure port **5028 TCP** is also open in your VPS provider's firewall/security group.

---

## Ports

| Port | Protocol | Description |
|------|----------|-------------|
| `5028` | TCP | Game server (client connects here) |
| `5432` | TCP | PostgreSQL (Docker internal, not exposed by default) |

---

## Troubleshooting

### Client

| Problem | Cause | Fix |
|---------|-------|-----|
| "Falló la carga debido a dependencias faltantes" | C# not compiled | Run `dotnet build` in `client/` before opening Godot |
| No `C#` option in `Project → Tools` | Standard Godot (not .NET) | Download **Godot 4.4 .NET** variant |
| "Failed to build project" / MSBuild error | Wrong .NET version | Install **.NET SDK 8.0** (Godot 4.4 doesn't support .NET 9) |
| `dotnet --version` shows 9.x only | .NET 8 not installed | Install .NET 8.0 alongside 9 (they coexist) |
| "Connection refused" | Server not running | Start server first |
| Assets missing / white textures | Missing `Data/` folder | Ensure `client/Data/` has `INIT/`, `Graficos/`, `Maps/` |
| Changes not taking effect | Stale assemblies | Run `dotnet build` after every code change |
| Low FPS | Default settings | F10 → Render tab → adjust |

### Server

| Problem | Fix |
|---------|-----|
| "Listening on 0.0.0.0:5028" never appears | Check PostgreSQL is running and `DATABASE_URL` is correct |
| "connection refused" to database | `sudo systemctl start postgresql` or `docker compose up -d postgres` |
| Port 5028 in use | `netstat -tlnp \| grep 5028` → kill the process |
| Docker build fails | Ensure Docker has 4GB+ RAM |
| Database migration errors | `psql -U ao -c "DROP DATABASE ao_server; CREATE DATABASE ao_server OWNER ao;"` |

### Quick checks

```bash
# Server running?
nc -z localhost 5028

# Database OK?
psql -U ao -d ao_server -c "SELECT count(*) FROM accounts;"

# Docker status
docker compose ps

# Full reset (Docker)
docker compose down -v && docker compose up -d --build
```

---

## Further Documentation

Detailed docs in [`docs/`](docs/):

| Document | Description |
|----------|-------------|
| [Architecture](docs/01-ARCHITECTURE.md) | Module structure, event loop, data flow |
| [Protocol](docs/02-PROTOCOL.md) | Encryption, packet format, 228 opcodes |
| [Game Systems](docs/03-GAME-SYSTEMS.md) | Combat, spells, crafting, guilds, GM commands |
| [Data Formats](docs/04-DATA-FORMATS.md) | File formats (maps, objects, charfiles, accounts) |
| [VB6 Parity](docs/05-VB6-PARITY.md) | VB6-specific behaviors and gotchas |
| [Migration Status](docs/06-MIGRATION-STATUS.md) | What's done, what's remaining, codebase stats |
| [Server Config](docs/SERVER-CONFIG.md) | server.ini, Intervalos.ini, environment variables |
| [Database](docs/DATABASE.md) | PostgreSQL schema, backup/restore |
| [Roadmap](docs/ROADMAP.md) | Remaining work and priorities |
| [Linux Deploy](docs/LINUX_DEPLOY.md) | Linux deployment guide |
| [VB6 Reference](docs/vb6-reference/) | Banking and trading protocol references |

---

This project is licensed under the [GNU Affero General Public License v3.0](LICENSE) (AGPL-3.0), consistent with the original Argentum Online open source release by Pablo Márquez (Morgolock).
