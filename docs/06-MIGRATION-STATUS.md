# Migration Status — VB6 to Rust

## Completed Systems

### Phase 1: Foundation
- [x] Crypto: 4-layer encrypt/decrypt matching VB6 exactly
- [x] TCP: Async listener, connection split, packet framing (null-terminated)
- [x] Config: INI parser with UTF-8/Latin-1/UTF-16 LE/BE support
- [x] ServerConfig: All Server.ini fields loaded
- [x] Protocol: Field parser (ReadField), client/server opcode constants
- [x] Main: Event loop with timer ticks

### Phase 2: Accounts & Authentication
- [x] Account file CRUD (*.act)
- [x] Character file CRUD (*.chr) — full save/load including all fields
- [x] HD serial ban check (KERD22)
- [x] Account login (ALOGIN → INIAC, ADDPJ, CODEH)
- [x] Account creation (NACCNT)
- [x] Password change (REPASS)
- [x] Account recovery (REECUH — PIN-based)
- [x] Character selection (OOLOGI/THCJXD → full login sequence)
- [x] Character creation (NLOGIN)
- [x] Character deletion (TBRP)
- [x] Dice roll (TIRDAD)
- [x] Ban system (IP + HD)
- [x] Multi-login prevention
- [x] IP rate limiting

### Phase 3: World Data
- [x] Objects database — 1,664 items (Obj.dat, UTF-16 LE)
- [x] Spells database — 65 spells (Hechizos.dat)
- [x] NPC database — 396 NPCs (NPCs.dat + NPCs-HOSTILES.dat)
- [x] Map loader — 178 maps (binary .map/.inf + INI .dat)
- [x] Experience table — 50 levels (Experiencia.dat)
- [x] Class balance data (ClassBonus.dat)

### Phase 4: World State
- [x] World grid — per-map tile occupancy tracking
- [x] Area visibility — 9×9 zone system (matching VB6 ModAreas.bas)
- [x] Character index pool — shared between players and NPCs
- [x] SendTarget variants — ToArea, ToMap, ToAll, ToIndex, etc.

### Phase 5: Movement & Chat
- [x] Walk (M opcode) with anti-flood (interval_pu=6 @ 40ms)
- [x] Map exits (border transitions + teleport tiles)
- [x] Talk (;), Whisper (\\), Yell (-) with font styling
- [x] Heading change (CHEA)
- [x] Position request (RPU)
- [x] Meditation (ME) toggle with cancellation on other actions

### Phase 6: Combat
- [x] Melee PvP — hit/damage/armor formulas matching VB6
- [x] Ranged PvP — bow/crossbow with ammo consumption
- [x] PvE — player vs NPC combat
- [x] NPC vs player — NPC attacks during AI tick
- [x] Critical hits (1.8× multiplier)
- [x] Backstab (1.5× player, 2× NPC)
- [x] Disarm (Wrestling skill)
- [x] Safe toggle (SEG)
- [x] Safe zone enforcement (trigger=1, 4)
- [x] Combat zone (trigger=6)
- [x] Criminal system (PvP reputation)
- [x] Kill deduplication
- [x] Death (ghost body/head, MUERT packet)
- [x] Resurrection (spell, potion HP=35, GM)

### Phase 7: Spell System
- [x] Cast spell (LH) with mana cost/validation
- [x] Damage spells (fire, lightning, etc.)
- [x] Healing spells (HP/mana recovery)
- [x] Buff/debuff (paralysis, poison, invisibility)
- [x] Summon NPC (pet creation)
- [x] Resurrection spell
- [x] Poison/paralysis removal spells

### Phase 8: Inventory & Items
- [x] 25-slot inventory
- [x] Equip/unequip (weapon, armor, shield, helmet, ammo)
- [x] Use items (potions, scrolls, boats, instruments, keys, food)
- [x] Pick up / drop items
- [x] Swap inventory slots
- [x] Item ground persistence (WorldBackUp)
- [x] Auto-cleanup timer (10 minutes)
- [x] Potion system: 6 subtypes (agility, strength, HP, mana, poison cure, remo)
- [x] Scroll system (learn spells)
- [x] Boat system (mount/dismount, water navigation, appearance changes)
- [x] Weapon poison (60% on Envenena weapons)
- [x] Weapon penetration (Refuerzo)

### Phase 9: NPCs & AI
- [x] NPC spawning — 1,510 NPCs from map data
- [x] AI tick (100ms configurable) — movement for all AI types
- [x] Hostile chase AI — aggro detection, pathfinding
- [x] Defense AI — react to attacker
- [x] Guard AI — attack criminals
- [x] Pet AI — follow owner
- [x] Random walk AI
- [x] NPC respawn (30s timer, original position)
- [x] NPC combat (melee, spell casting)
- [x] NPC poison on hit (Veneno flag)
- [x] Proportional EXP distribution (damage tracking)
- [x] NPC commerce (buy/sell with inflation)
- [x] Pathfinding (BFS)

### Phase 10: Advanced Systems
- [x] Banking — 40-slot personal bank, deposit/withdraw items and gold
- [x] Trading — player-to-player item/gold exchange with validation
- [x] Crafting — Fishing, Logging, Mining, Smelting, Blacksmithing, Carpentry
- [x] Taming — pet capture via skill check
- [x] Stealing — steal gold/items from players
- [x] Hiding — stealth with NOVER packets
- [x] Guilds — create, apply, accept, expel, codex, guild bank
- [x] Parties — group formation, shared EXP
- [x] Quests — accept, track objectives, turn in for rewards
- [x] Factions — Armada Real / Fuerzas del Caos enrollment, ranking
- [x] Tournaments — bracket system, auto-tournament events
- [x] Events — CTF, Hunger Games, Arena, Faction War, etc.
- [x] GM Commands — 90 handler functions (100+ commands)
- [x] Text code system — ~80 messages migrated to ||NNN format
- [x] Level bonuses — ClassBonus.dat at levels 53, 56, 60
- [x] Ranking system
- [x] Mail system

## Godot 4 Client (VB6 → Godot C#)

**38 C# source files, 16,443 LOC**

### Rendering
- [x] 4-pass tile renderer (L1 ground, L2 objects, L3 chars, L4 roofs)
- [x] GRH animation system (VB6 formula: deltaMs * numFrames / speed)
- [x] Body + head rendering with heading-dependent draw order
- [x] Weapon, shield, helmet rendering (Armas.dat, Escudos.dat, Cascos.ind)
- [x] Aura system (5 equipment slots + NPC aura, rotation, color tint)
- [x] FX overlays (3 simultaneous slots)
- [x] Character shadows (ellipse)
- [x] Dead character transparency pulsing
- [x] Dialog bubbles (FPS-independent delta-time animation)
- [x] Floating combat text (N| packets: damage numbers, miss text above characters)
- [x] Name/clan/rank labels (bitmap font)
- [x] Particle system (Particles.ini, 105 defs)
- [x] Light system (4-corner per-vertex, VB6 clsLight.cls parity)
- [x] Roof alpha fade on enter
- [x] Minimap

### UI (34 panels/components)
- [x] Login screen + character select + character create + account create
- [x] Delete character button with confirmation dialog
- [x] Full frmMain: chat console, HP/MP/STA/EXP bars, inventory, spells
- [x] Bottom bar stats (weapon, defense, magdef, str, agi, rep, fps)
- [x] Death panel (frmMuertito: Continuar/Regresar)
- [x] Commerce panel (NPC buy/sell)
- [x] Bank panel (deposit/withdraw gold)
- [x] Vault panel (item bank, 40 slots)
- [x] Travel panel (city travel)
- [x] Spell list
- [x] Macro panel (F9: 10 configurable command slots)
- [x] Options panel (3 tabs: Juego/Controles/Render, 30+ settings)
- [x] Key binding panel (22 rebindable actions, conflict detection)
- [x] Mensaje dialog (modal error/info: ERR/ERO/!! packets)
- [x] Drop quantity dialog
- [x] Chat mode system (normal, yell, clan, global, party, faction, GM, whisper)

### Network
- [x] 4-layer encrypt/decrypt (VB6 parity)
- [x] 159 packet handlers
- [x] Client-side movement prediction + PT correction
- [x] TCP disconnect detection + reconnect to login
- [x] Key cooldown system (300ms for action keys)

### Known Remaining (Client)
- [ ] Guild UI (frmGuildInfo)
- [ ] Quest UI
- [ ] Particle additive blending
- [ ] Arrow projectile rendering

## Test Coverage

**68 unit tests passing** across 20 modules:
- Crypto (26 tests): AoDefEnc cipher, base64, conversion, roundtrip
- Protocol (6 tests): Packet field parsing
- Config (5 tests): INI parser (UTF-8, UTF-16 LE/BE, Latin-1)
- Network (5 tests): Packet framing
- Data (15 tests): Object/spell/NPC/map/charfile/account loaders, balance, ranking
- Game (1 test): World state operations

## Known Remaining Work

### Not Yet Implemented
- Housing system (FWO/CUC opcodes)
- Pet rename (CNM)
- Gem/medal exchange (GEMS/GEPS)
- Divine offering (OFDIOZ)
- TS points shop (FTSPTS)
- Item upgrade system (SPH/SPÉ)
- Arena spectating (ARE)
- Drag & drop transfer (DYDTRA)
- FPZ anti-hack report (ENVFPZ)
- Vote system (NVOT)
- Report/denuncia system (NEWD)

### Quality of Life
- [ ] Comprehensive logging for all GM actions
- [ ] Graceful shutdown (save all charfiles)
- [ ] Configuration hot-reload

## Build & Deploy

```bash
# Build
cargo build --release

# Run
cp target/release/ao-server server/
cd server && ./ao-server .

# Or use Make
make build   # Build and copy
make run     # Build + run
make dev     # Cargo run (dev mode)
make test    # Run tests

# Docker
make docker-run   # Build image + start
make docker-stop  # Stop container
```

## Codebase Size

### Server (Rust)
| Component | Files | Lines | Purpose |
|-----------|-------|-------|---------|
| handlers/ | 16 | ~25,100 | All packet handlers + game logic |
| game/ (types, world, npc) | 4 | ~20,300 | Core game state, map grids, NPC AI |
| main.rs | 1 | ~14,400 | Entry point, event loop, timer ticks |
| crypto/ | 5 | ~1,500 | 4-layer encryption |
| data/ | 14 | ~3,500 | Static data loaders |
| db/ | 7 | ~2,000 | PostgreSQL persistence |
| net/ | 4 | ~800 | TCP networking |
| protocol/ | 2 | ~1,200 | 228 opcode constants |
| config/ | 2 | ~600 | INI parser + config |
| **Total** | **55** | **~35,500** | |

### Client (Godot 4 C#)
| Component | Files | Lines | Purpose |
|-----------|-------|-------|---------|
| Main.cs | 1 | ~3,160 | Game loop, UI orchestration |
| Network/ | 4 | ~3,090 | 159 packet handlers, encryption, TCP |
| UI/ | 10 | ~4,100 | Commerce, bank, vault, options, macros |
| Game/ | 6 | ~2,300 | State, input, config, particles, sound |
| Rendering/ | 3 | ~1,950 | World renderer, char renderer, GRH |
| Data/ | 10 | ~1,210 | Asset loaders (body, weapon, map, etc.) |
| **Total** | **38** | **~16,440** | |
