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

The server reads game data from `server/` (Dat/, Maps/, Server.ini) and listens on port `5028`.

#### Makefile commands

| Command | Description |
|---------|-------------|
| `make build` | Compile release binary and copy to `server/` |
| `make run` | Build + run the server |
| `make dev` | Run in dev mode (debug build, faster compile) |
| `make test` | Run all tests (65 unit tests) |
| `make docker-run` | Build Docker image + start with compose |
| `make docker-stop` | Stop Docker containers |

---

## Client Setup (Godot 4.3 + C#)

The client uses **Godot 4.3 with .NET (C#) support**. You need two things:

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

```
1. Open Godot 4.3 .NET
2. Click "Import" → navigate to client/ → select project.godot → "Import & Edit"
3. Wait for Godot to import assets (first time takes a minute)
4. Godot will auto-build the C# solution on first open
5. Press F5 (or the Play button ▶) to run
```

The client connects to `127.0.0.1:5028` by default — make sure the server is running first.

### Troubleshooting

| Problem | Solution |
|---------|----------|
| "Unable to find .NET SDK" | Install .NET 6.0 SDK, restart Godot |
| C# build errors on first open | Build → Build Solution (or Ctrl+Shift+B) |
| "Connection refused" | Start the server first (`make run` or `docker compose up -d`) |
| Assets missing / white textures | Make sure `client/Data/` has INIT/, Graficos/, Maps/ folders |

---

## Server Configuration

The server reads `server/Server.ini` on startup. Key settings:

| Setting | Default | Description |
|---------|---------|-------------|
| `StartPort` | `5028` | Game server port |
| `StartPortEstadisticas` | `7669` | Stats endpoint port |
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
| `7669` | TCP | Stats endpoint |
| `5432` | TCP | PostgreSQL (Docker only, not exposed by default) |
