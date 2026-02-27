# Tierras Sagradas AO — Rust Server

Drop-in replacement for the VB6 Argentum Online server, written in Rust. Full protocol compatibility with the original VB6 client.

## Project Layout

```
src/          Rust source code
server/       Runtime data (maps, objects, NPCs, config, charfiles)
docs/         Design docs and protocol references
```

## Quick Start

```bash
# Build release binary and copy to server/
make build

# Run the server (builds first)
make run

# Dev mode (cargo run, no copy step)
make dev

# Run tests
make test
```

## Docker

```bash
# Build image and start server
make docker-run

# Or manually
docker build -t ao-server .
docker compose up -d

# Stop
make docker-stop
```

Persistent data (charfiles, accounts, guilds, logs) is stored in named Docker volumes and survives container restarts.

Ports exposed: `5028` (game), `7669` (stats).

## Requirements

- Rust 1.85+ (edition 2024, async/await, tokio)
- Game data files in `server/` (Dat/, Maps/, Server.ini, etc.)
- Docker (optional, for containerized deployment)
