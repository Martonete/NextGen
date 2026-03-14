# Architecture Documentation

Technical reference for the Tierras Sagradas AO engine — Rust server + Godot 4 C# client.

| Document | Lines | Description |
|----------|-------|-------------|
| [server-architecture.md](server-architecture.md) | ~120 | Module structure, event loop (40ms/100ms/1s/30s/60s ticks), data flow, key design decisions |
| [protocol.md](protocol.md) | ~215 | 4-layer encryption, packet format, 228 opcodes (client + server), text codes, area visibility |
| [game-systems.md](game-systems.md) | ~380 | 16 systems: accounts, characters, maps, NPCs, combat, spells, inventory, banking, trading, crafting, guilds, quests, factions, events, GM commands, anti-cheat |
| [data-formats.md](data-formats.md) | ~360 | File format specs: server.ini, Obj.dat (1,664 items), Hechizos.dat (65 spells), NPCs.dat (396 NPCs), .map/.inf/.dat, charfiles, accounts, exp table, quests, guilds |
| [rendering.md](rendering.md) | ~250 | 6-pass rendering pipeline, layer system (L1-L4), character composition, GPU lightmap, water reflections, roof fade, trigger system |
| [assets.md](assets.md) | ~350 | Graficos.ind (32,824 GRHs), texture files (3,291 PNGs), animation system, texture catalog, particles (105), auras (96), FX, constants |
| [deployment.md](deployment.md) | ~130 | PostgreSQL setup, schema (12 tables), server.ini config, privilege levels, Intervalos.ini timers, environment variables, backup/restore |
| [project-layout.md](project-layout.md) | ~40 | Directory structure for server (source, dat, maps, charfile, migrations) and client (scenes, scripts, data) |
