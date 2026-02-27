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

## Requirements

- Rust 1.75+ (async/await, tokio)
- Game data files in `server/` (Dat/, Maps/, Server.ini, etc.)
