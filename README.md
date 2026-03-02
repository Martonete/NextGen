# Tierras Sagradas AO — Rust Server + Godot Client

Full rewrite of Argentum Online: **Rust server** (drop-in replacement for VB6 server) + **Godot 4 C# client** (replacing VB6 DirectX8 client). Protocol-compatible with original VB6 clients.

## Project Layout

```
server/source/   Rust server source code
server/          Runtime data (maps, objects, NPCs, config, charfiles)
server/migrations/ SQL migrations (PostgreSQL)
client/          Godot 4 + C# client
docs/            Design docs and protocol references
```

---

## Quick Start

### Option A: Docker (recommended — zero setup)

**Requirements:** [Docker Desktop](https://www.docker.com/products/docker-desktop/) (includes Docker Compose).

```bash
# 1. Clone the repo
git clone https://github.com/cyphercr0w/server-rust-tsao.git
cd server-rust-tsao

# 2. Start server + database
docker compose up -d

# 3. Verify it's running
docker compose logs -f ao-server
# Should show: "Listening on 0.0.0.0:5028"
```

That's it. The server is running on `localhost:5028`. PostgreSQL is managed automatically.

To stop: `docker compose down`

### Option B: Build from source (no Docker)

**Requirements:**
- [Rust 1.85+](https://rustup.rs/)
- [PostgreSQL 15+](https://www.postgresql.org/download/)

```bash
# 1. Clone the repo
git clone https://github.com/cyphercr0w/server-rust-tsao.git
cd server-rust-tsao

# 2. Set up PostgreSQL
#    Create a database and user (adjust password as needed):
psql -U postgres -c "CREATE USER ao WITH PASSWORD 'ao_secret';"
psql -U postgres -c "CREATE DATABASE ao_server OWNER ao;"

# 3. Configure the connection
cp .env.example .env
#    Edit .env if you changed the password or port:
#    DATABASE_URL=postgres://ao:ao_secret@localhost:5432/ao_server

# 4. Build and run
make build
make run
```

The server reads game data from `server/` (dat/, maps/, server.ini) and listens on port `5028`.

---

## Makefile Commands

All commands run from the repo root. Type `make` or `make help` to see the full list.

### Server

| Command | Description |
|---------|-------------|
| `make build` | Compile Rust server (release) and copy to `server/` |
| `make run` | Build + run server locally |
| `make dev` | Run server in dev mode (cargo run, faster compile) |
| `make test` | Run all server tests |
| `make clean` | Clean all build artifacts (server + client) |

### Server (Docker)

| Command | Description |
|---------|-------------|
| `make docker-run` | Build Docker image + `docker compose up` |
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

### Typical workflow

```bash
# First time / after git pull:
make docker-run       # start server
make client           # compile + play

# Just recompile after code changes:
make client-build

# Multiple targets at once:
make docker-run client-run
```

---

## Client Setup (Godot 4.3 + C#)

The client uses **Godot 4.3 with .NET (C#) support**.

### 1. Install .NET 6.0 SDK

Download and install from: https://dotnet.microsoft.com/download/dotnet/6.0

Verify:
```bash
dotnet --version
# Should show 6.x.x
```

### 2. Install Godot 4.3 (.NET version)

Download **Godot 4.3 — .NET** (NOT the standard version) from:

https://godotengine.org/download/archive/4.3-stable/

Choose the `.NET` variant for your OS (Windows/macOS/Linux). The standard version without .NET **will not work** — it can't compile C# scripts.

### 3. Open and run the client

```bash
# Option A: From command line (recommended)
make client           # builds + runs

# Option B: From Godot editor
make client-editor    # opens the editor, then press F5
```

The client connects to `127.0.0.1:5028` by default — make sure the server is running first.

> **Important:** You must run `make client-build` (or `make client`) every time you pull new changes or modify C# code. Without this step, Godot runs the **old** compiled code.

### Troubleshooting

| Problem | Solution |
|---------|----------|
| "Unable to find .NET SDK" | Install .NET 6.0 SDK, restart Godot |
| C# build errors on first open | `make client-build` or Ctrl+Shift+B in editor |
| "Connection refused" | Start the server first (`make docker-run`) |
| Assets missing / white textures | Make sure `client/Data/` has INIT/, Graficos/, Maps/ folders |
| Changes not taking effect | Run `make client-build` before launching |

---

## Server Configuration

The server reads `server/server.ini` on startup. Key settings:

| Setting | Default | Description |
|---------|---------|-------------|
| `StartPort` | `5028` | Game server port |
| `MaxUsers` | `400` | Max concurrent players |
| `Encriptar` | `1` | Enable packet encryption |
| `PuedeCrearPersonajes` | `1` | Allow character creation |

Database connection is set via `DATABASE_URL` environment variable (or `.env` file):
```
DATABASE_URL=postgres://ao:ao_secret@localhost:5432/ao_server
```

---

## Ports

| Port | Protocol | Description |
|------|----------|-------------|
| `5028` | TCP | Game server (client connects here) |
| `5432` | TCP | PostgreSQL (Docker only, not exposed by default) |
