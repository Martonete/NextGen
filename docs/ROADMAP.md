# Argentum Nextgen — Server Migration Roadmap (VB6 → Rust)

## Current Status: ~90% VB6 Parity

All core game systems are implemented and functional. Server: 55 Rust files, 35.5K LOC, 228 protocol opcodes, 90 GM handlers, 68 tests. Client: 38 C# files, 16.4K LOC, 159 packet handlers. See [06-MIGRATION-STATUS.md](06-MIGRATION-STATUS.md) for detailed tracking.

## Documentation Index

| Document | Description |
|----------|-------------|
| [01-ARCHITECTURE.md](01-ARCHITECTURE.md) | Module structure, event loop, data flow, 16 handler files |
| [02-PROTOCOL.md](02-PROTOCOL.md) | Encryption, packet format, 228 opcodes |
| [03-GAME-SYSTEMS.md](03-GAME-SYSTEMS.md) | All game systems (combat, spells, crafting, guilds, etc.) |
| [04-DATA-FORMATS.md](04-DATA-FORMATS.md) | File formats (maps, objects, charfiles, accounts) |
| [05-VB6-PARITY.md](05-VB6-PARITY.md) | VB6-specific behaviors and gotchas |
| [06-MIGRATION-STATUS.md](06-MIGRATION-STATUS.md) | What's done, what's remaining, codebase stats |
| [SERVER-CONFIG.md](SERVER-CONFIG.md) | server.ini, Intervalos.ini, environment variables |
| [DATABASE.md](DATABASE.md) | PostgreSQL schema, backup/restore |
| [vb6-reference/](vb6-reference/) | VB6 banking and trading protocol references |

## Remaining Work

### Server — Not Yet Implemented
- Housing system (buy/query houses: CUC/FWO)
- Pet rename (CNM)
- Gem/medal exchange (GEMS/GEPS)
- Divine offering system (OFDIOZ)
- Item upgrade system (Mejorados: SPH/SP+special)
- Arena spectating (ARE)
- Drag & drop transfer (DYDTRA)
- FPZ anti-hack reporting (ENVFPZ)
- Vote/poll system (NVOT)
- Report/denuncia system (NEWD)
- SOS consultation system (#/X/CONSUL)
- Duel system (/DUELO, /SIDUELO)
- Clan vs Clan (/SICV, /CVC)
- Mount system (/MONTAR, /DESMONTAR)
- Noble status (/NOBLE)
- Surgery/race change (/CIRUJIA)
- Marriage system (/CASAR, /DIVORCIARSE)
### Client — Not Yet Implemented
- Guild UI (frmGuildInfo)
- Quest UI
- Particle additive blending
- Arrow projectile rendering

### Quality of Life
- Graceful shutdown with full charfile save
- Configuration hot-reload
- Comprehensive GM action logging
- Stress testing (400+ concurrent users)
- Integration tests with real VB6 client

## Removed Features

The following features were evaluated and intentionally removed from the project:

- **Emoticon system** — FX overlays used for gameplay effects only, emoticons stripped
- **Friend system** — `account_friends` table dropped (migration 007), opcodes removed
- **Guild bank** — table exists but feature not active
- **TSAO-specific point systems** — `puntos_donacion`, `puntos_torneo`, `ts_points` and related exchange opcodes (FTSPTS, DCANJE/CCANJE/DPX/DRX) removed as they were custom to Tierras Sagradas, not part of standard AO 13.3

## Key Compatibility Notes

1. **Packet format MUST be byte-identical** to VB6 output
2. **Encryption chain**: Server→Client = AoDefServEncrypt → AoDefEncode + null terminator
3. **Key rotation**: counter increments per packet, wraps at 999,999
4. **INI files**: read/write in Latin-1 encoding (Windows-1252)
5. **Obj.dat**: UTF-16 LE encoded INI (exception — all others are Latin-1)
6. **Map .map/.inf files**: binary little-endian format
7. **String encoding**: VB6 uses ANSI strings — Rust handles Latin-1 ↔ UTF-8 conversion
8. **Text codes**: Use `||NNN` format for client-side text rendering (Textos.ao)
9. **Directory names**: All lowercase (`dat/`, `maps/`, `dioses/`, `server.ini`)
