# Tierras Sagradas AO — Rust Server + Godot Client

Full rewrite of Argentum Online: **Rust server** (drop-in replacement for VB6 server) + **Godot 4 C# client** (replacing VB6 DirectX8 client). Protocol-compatible with original VB6 clients.

## Project Layout

```
src/           Rust server source
server/        Runtime data (maps, objects, NPCs, config, charfiles)
client/        Godot 4 + C# client (Scripts/, Data/, Scenes/)
docs/          Design docs and protocol references
```

## Server

```bash
# Build + run
make build && make run

# Dev mode
make dev

# Tests
make test
```

### Docker

```bash
make docker-run    # Build + start
make docker-stop   # Stop
```

Ports: `5028` (game), `7669` (stats).

## Client

The Godot client is a pixel-perfect reimplementation of the VB6 DirectX8 rendering engine:

- **Viewport**: 534×408, 4-pass tile renderer (ground, objects, chars, roofs)
- **Rendering**: Body, head, weapon, shield, helmet, auras, FX, particles, shadows
- **UI**: Full frmMain recreation (chat, inventory, spells, stats, minimap, death panel)
- **Protocol**: Full 4-layer encrypt/decrypt, 120+ packet handlers
- **Movement**: Client-side prediction with server-authoritative correction

Open `client/` in Godot 4.x with .NET support, build and run.

## Requirements

- **Server**: Rust 1.85+, game data in `server/` (Dat/, Maps/, Server.ini)
- **Client**: Godot 4.x with .NET, game data in `client/Data/` (INIT/, Graficos/, Maps/)
- **Docker**: Optional for server deployment
