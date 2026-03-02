# Architecture Overview

## Project Goal

Drop-in replacement for the Argentum Online (AO) VB6 server, written in Rust. The VB6 client remains untouched — the Rust server must be 100% protocol-compatible.

## Source Reference

- **Original VB6 server**: `Servidor/Codigo/` — 98 files, ~63,000 lines of Visual Basic 6
- **VB6 client**: `Cliente/CODIGO/` — 158 files (read-only reference, never modified)
- **Rust server**: `server-rust/server/source/` — all new code

## Module Structure

```
server/source/
├── main.rs              # Entry point, event loop, timer ticks
├── crypto/              # Encryption/decryption (4 layers)
│   ├── aodef_encrypt.rs   # Hex encoding layer (AoDefServEncrypt)
│   ├── aodef_base64.rs    # Base64 encoding layer (AoDefEncode/Decode)
│   ├── aodef_cipher.rs    # XOR stream cipher (Codificar/DeCodificar)
│   ├── aodef_converter.rs # Key derivation (Numero2Letra → Semilla)
│   └── mod.rs             # Full encrypt/decrypt pipelines
├── net/                 # TCP networking
│   ├── listener.rs        # Async TCP accept, event channel
│   ├── connection.rs      # Per-connection read/write split, key rotation
│   ├── packet_framing.rs  # Null-byte terminated packet framing
│   └── mod.rs
├── protocol/            # Packet format definitions
│   ├── fields.rs          # ReadField parser (VB6 field extraction)
│   └── mod.rs             # Client/server opcode constants
├── config/              # Server configuration
│   ├── ini.rs             # INI parser (UTF-8, Latin-1, UTF-16 LE/BE)
│   └── mod.rs             # ServerConfig from Server.ini
├── data/                # Static game data loaders
│   ├── objects.rs         # Obj.dat — 1,664 items/objects
│   ├── spells.rs          # Hechizos.dat — 65 spells
│   ├── npcs.rs            # NPCs.dat + NPCs-HOSTILES.dat — 396 NPCs
│   ├── maps.rs            # Binary .map/.inf + INI .dat — 178 maps
│   ├── experience.rs      # Experiencia.dat — 50 level thresholds
│   ├── charfile.rs        # Character file read/write/create (.chr)
│   ├── accounts.rs        # Account file read/write/create (.act)
│   ├── bans.rs            # IP/HD ban list management
│   ├── guilds.rs          # Guild data (guildsinfo.inf)
│   ├── quests.rs          # Quest data (Quests.dat)
│   ├── balance.rs         # Class balance data (ClassBonus.dat)
│   ├── ranking.rs         # Ranking system (top players)
│   └── mod.rs             # GameData aggregate loader
└── game/                # Runtime game logic
    ├── types.rs           # UserState, GameState, SendTarget (~1,470 lines)
    ├── world.rs           # Map grids, area visibility, tile occupancy (~9,170 lines)
    ├── npc.rs             # NPC runtime state and AI constants (~9,280 lines)
    ├── mod.rs
    └── handlers/          # Packet handlers + game logic (~25,100 lines, 16 files)
        ├── mod.rs           # Packet dispatcher routing (~3,770 lines)
        ├── gm_commands.rs   # 90 GM command implementations (~3,280 lines)
        ├── misc_handlers.rs # Miscellaneous system handlers (~2,850 lines)
        ├── events.rs        # Event system & world events (~2,690 lines)
        ├── inventory.rs     # Item management & slots (~2,080 lines)
        ├── skills.rs        # Skill casting & leveling (~1,420 lines)
        ├── ticks.rs         # Server tick/update loop (~1,380 lines)
        ├── guilds_handler.rs# Guild operations & diplomacy (~1,310 lines)
        ├── spells.rs        # Spell mechanics (~1,000 lines)
        ├── npcs.rs          # NPC interaction (~975 lines)
        ├── commerce.rs      # Trading & shops (~960 lines)
        ├── social.rs        # Friends, private messages (~780 lines)
        ├── combat.rs        # Attack mechanics (~720 lines)
        ├── common.rs        # Shared utilities (~660 lines)
        ├── quests_party.rs  # Quests & parties (~660 lines)
        └── player_commands.rs # /cmd handlers (~580 lines)
```

## Event Loop Architecture

The server uses a single-threaded `tokio::select!` event loop in `main.rs`:

```
TCP Events (async):
  NewConnection → IP ban check → IP rate limit → add to state
  PacketReceived → decrypt → parse opcode → route to handler
  Disconnected → save charfile → send BP (remove char) → cleanup

Timer Ticks:
  40ms   → Anti-cheat interval decrements (interval_pu, interval_at, etc.)
  100ms  → NPC AI (movement, combat, pathfinding)
  1s     → Player passive (hunger/thirst drain, stamina regen, poison, nobleza, siege, war, events)
  30s    → NPC respawn (revive dead NPCs at original spawn points)
  60s    → World cleanup (auto-remove ground items after 10 ticks = 10 minutes)
```

## Data Flow

```
Client → [Latin-1 bytes] → [AoDefDecode (base64)] → [DeCodificar (XOR)] → PlainText
Server → PlainText → [AoDefServEncrypt (hex)] → [AoDefEncode (base64)] → [+ 0x00 terminator] → Client
```

## Key Design Decisions

1. **Single-threaded async**: Matches VB6's single-threaded model. No need for locks on game state.
2. **Text-based protocol**: VB6 client uses string-based packets, not binary. All packets are ASCII/Latin-1.
3. **HashMap for users**: `HashMap<ConnectionId, UserState>` — constant-time lookup by connection ID.
4. **Shared CharIndex pool**: Players and NPCs share the same `CharIndex` namespace for client rendering.
5. **Area-based visibility**: 9×9 zone grid (matching VB6 `ModAreas.bas`) — only send packets to nearby players.
6. **VB6-first fidelity**: Every formula, constant, and edge case mirrors the VB6 code, even when "wrong" by modern standards.

## Runtime Data

```
server/
├── server.ini           # Server configuration (port, limits, start position)
├── dat/                 # Static databases (objects, spells, NPCs, experience)
├── maps/                # Map files (.map binary + .inf binary + .dat INI)
├── charfile/            # Character save files (.chr INI format)
├── Accounts/            # Account files (.act INI format)
├── guilds/              # Guild data files
├── WorldBackUp/         # Ground item persistence
├── dioses/              # God/admin data files
└── logs/                # Server logs
```
