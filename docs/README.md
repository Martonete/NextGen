# Tierras Sagradas AO — Documentation

## Operations

| Document | Description |
|----------|-------------|
| [LINUX_DEPLOY.md](LINUX_DEPLOY.md) | Linux deployment guide (Docker, .NET, Godot) |
| [CLIENT-BUILD.md](CLIENT-BUILD.md) | Client build & export guide |
| [TROUBLESHOOTING.md](TROUBLESHOOTING.md) | Common issues and quick fixes |

## Architecture

Detailed technical documentation of the server, client, and data formats.

| Document | Description |
|----------|-------------|
| [server-architecture.md](architecture/server-architecture.md) | Rust server modules, event loop, data flow |
| [protocol.md](architecture/protocol.md) | Packet format, opcodes, encryption pipeline |
| [game-systems.md](architecture/game-systems.md) | Combat, spells, NPCs, guilds, quests, events |
| [data-formats.md](architecture/data-formats.md) | .map/.inf/.dat, charfiles, Obj.dat, NPCs.dat |
| [rendering.md](architecture/rendering.md) | 6-pass pipeline, layers, lightmap, reflections |
| [assets.md](architecture/assets.md) | GRH system, textures, animations, particles, auras |
| [deployment.md](architecture/deployment.md) | PostgreSQL, server.ini, privileges, environment |
| [project-layout.md](architecture/project-layout.md) | Folder structure |

## Skills

Actionable reference documents for working with specific subsystems.

| Skill | Description |
|-------|-------------|
| [ao-combat.md](skills/ao-combat.md) | Damage formulas, attack power, PvP, death/revive |
| [ao-spells.md](skills/ao-spells.md) | Spell types, cast flow, damage formula, mana costs |
| [ao-npc-ai.md](skills/ao-npc-ai.md) | AI types, aggro, spawn, drops, pets |
| [ao-skills-crafting.md](skills/ao-skills-crafting.md) | 22 skills, gathering, crafting, stealth |
| [ao-sprite-indexing.md](skills/ao-sprite-indexing.md) | Graficos.ind, GRH format, texture loading |
| [ao-ui-kit.md](skills/ao-ui-kit.md) | RpgBaseForm styles, RpgTheme factory, panel creation |
| [ao-rendering.md](skills/ao-rendering.md) | WorldRenderer passes, CharRenderer draw order |
| [ao-protocol.md](skills/ao-protocol.md) | ByteQueue types, packet format, adding new packets |
| [mapper.md](skills/mapper.md) | Map file format, .inf exits/NPCs, patching |
