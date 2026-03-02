# Tierras Sagradas AO — Server Migration Roadmap (VB6 → Rust)

## Current Status: ~90% VB6 Parity

All core game systems are implemented and functional. Server: 55 Rust files, 35.5K LOC, 228 protocol opcodes, 90 GM handlers, 65 tests. Client: 38 C# files, 16.4K LOC, 159 packet handlers. See [06-MIGRATION-STATUS.md](06-MIGRATION-STATUS.md) for detailed tracking.

## Documentation Index

| Document | Description |
|----------|-------------|
| [01-ARCHITECTURE.md](01-ARCHITECTURE.md) | Module structure, event loop, data flow, 16 handler files |
| [02-PROTOCOL.md](02-PROTOCOL.md) | Encryption, packet format, 228 opcodes |
| [03-GAME-SYSTEMS.md](03-GAME-SYSTEMS.md) | All game systems (combat, spells, crafting, guilds, etc.) |
| [04-DATA-FORMATS.md](04-DATA-FORMATS.md) | File formats (maps, objects, charfiles, accounts) |
| [05-VB6-PARITY.md](05-VB6-PARITY.md) | VB6-specific behaviors and gotchas |
| [06-MIGRATION-STATUS.md](06-MIGRATION-STATUS.md) | What's done, what's remaining, codebase stats |
| [BANKING_SYSTEM_VB6.md](BANKING_SYSTEM_VB6.md) | VB6 banking system reference |
| [TRADING_PROTOCOL.md](TRADING_PROTOCOL.md) | Player trading protocol reference |
| [MISSING_HANDLERS.md](MISSING_HANDLERS.md) | VB6 handlers not yet implemented (88 items) |

## Remaining Work

### Server — Not Yet Implemented
- Housing system (buy/query houses: CUC/FWO)
- Pet rename (CNM)
- Gem/medal exchange (GEMS/GEPS)
- Divine offering system (OFDIOZ)
- TS points shop (FTSPTS)
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
- Donation/tournament prize exchange (DCANJE/CCANJE/DPX/DRX)

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

## Key Compatibility Notes

1. **Packet format MUST be byte-identical** to VB6 output
2. **Encryption chain**: Server→Client = AoDefServEncrypt → AoDefEncode + null terminator
3. **Key rotation**: counter increments per packet, wraps at 999,999
4. **INI files**: read/write in Latin-1 encoding (Windows-1252)
5. **Obj.dat**: UTF-16 LE encoded INI (exception — all others are Latin-1)
6. **Map .map/.inf files**: binary little-endian format
7. **String encoding**: VB6 uses ANSI strings — Rust handles Latin-1 ↔ UTF-8 conversion
8. **Text codes**: Use `||NNN` format for client-side text rendering (Textos.tsao)
9. **Directory names**: All lowercase (`dat/`, `maps/`, `dioses/`, `server.ini`)
