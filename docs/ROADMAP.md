# Tierras Sagradas AO — Server Migration Roadmap (VB6 → Rust)

## Current Status: Phase 10+ (VB6 Parity)

All core game systems are implemented and functional. See [06-MIGRATION-STATUS.md](06-MIGRATION-STATUS.md) for detailed completion tracking.

## Documentation Index

| Document | Description |
|----------|-------------|
| [01-ARCHITECTURE.md](01-ARCHITECTURE.md) | Module structure, event loop, data flow |
| [02-PROTOCOL.md](02-PROTOCOL.md) | Encryption, packet format, all opcodes |
| [03-GAME-SYSTEMS.md](03-GAME-SYSTEMS.md) | All game systems (combat, spells, crafting, guilds, etc.) |
| [04-DATA-FORMATS.md](04-DATA-FORMATS.md) | File formats (maps, objects, charfiles, accounts) |
| [05-VB6-PARITY.md](05-VB6-PARITY.md) | VB6-specific behaviors and gotchas |
| [06-MIGRATION-STATUS.md](06-MIGRATION-STATUS.md) | What's done, what's remaining |
| [BANKING_SYSTEM_VB6.md](BANKING_SYSTEM_VB6.md) | VB6 banking system reference |
| [TRADING_PROTOCOL.md](TRADING_PROTOCOL.md) | Player trading protocol reference |

## Remaining Work

### Not Yet Implemented
- Housing system (buy/query houses)
- Pet rename
- Gem/medal exchange
- Divine offering system
- TS points shop
- Item upgrade system (Mejorados)
- Arena spectating
- Drag & drop transfer
- FPZ anti-hack reporting
- Vote/poll system
- Report/denuncia system

### Quality of Life
- Graceful shutdown with full charfile save
- Stats HTTP endpoint (port 7669)
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
