# Tierras Sagradas AO — Server Migration Roadmap (VB6 → Rust)

## Status: Phase 8 DONE

---

## Phase 1: Foundation ✅ COMPLETE
- [x] Project structure (Cargo.toml, modules)
- [x] Crypto: AoDefServEncrypt/Decrypt (hex encoding) — `aodef_encrypt.rs`
- [x] Crypto: AoDefEncode/Decode (Base64) — `aodef_base64.rs`
- [x] Crypto: Codificar/DeCodificar (XOR stream cipher with seed) — `aodef_cipher.rs`
- [x] Crypto: Numero2Letra (number→Spanish words for key derivation) — `aodef_converter.rs`
- [x] Crypto: Full encrypt/decrypt pipelines — `crypto/mod.rs`
- [x] Net: Packet framing (null-byte terminated) — `packet_framing.rs`
- [x] Net: Connection split (reader/writer) with per-packet key rotation — `connection.rs`
- [x] Net: TCP listener with async accept and event channel — `listener.rs`
- [x] Config: INI parser (UTF-8, Latin-1, UTF-16 LE/BE support) — `config/ini.rs`
- [x] Config: ServerConfig loader from Server.ini — `config/mod.rs`
- [x] Protocol: Field parser (ReadField equivalent) — `protocol/fields.rs`
- [x] Protocol: Client & server opcode constants — `protocol/mod.rs`
- [x] Main: Event loop with connection tracking — `main.rs`
- [x] 42 unit tests passing
- [x] Server starts, loads config, listens on port 5028

## Phase 2: Account & Authentication System ✅ COMPLETE
- [x] Account file loader (Accounts/*.act) — read/write/create — `data/accounts.rs`
- [x] Character file loader (charfile/*.chr) — read/write/create — `data/charfile.rs`
- [x] HD serial check handler (KERD22 opcode) — `game/handlers.rs`
- [x] Account login handler (ALOGIN → ConnectAccount) — sends INIAC, ADDPJ, CODEH
- [x] New account creation (NACCNT → CreateAccount)
- [x] Password change (REPASS)
- [x] Account recovery (REECUH) — PIN-based, generates random password
- [x] Character selection and login (OOLOGI, THCJXD → ConnectUser)
- [x] New character creation (NLOGIN → ConnectNewUser)
- [x] Character deletion (TBRP) — with level/guild checks
- [x] Dice roll for attributes (TIRDAD) — sends DADOS packet
- [x] Ban system (IP + HD checks) — `data/bans.rs`
- [x] Multi-login prevention (same IP + same account checks)
- [x] GameState with connection tracking — `game/types.rs`
- [x] 47 unit tests passing

## Phase 3: World Data Loading ✅ COMPLETE
- [x] Experience table loader (Dat/Experiencia.dat) — `data/experience.rs`
- [x] Objects database loader (Dat/Obj.dat — UTF-16 LE, 1664 items) — `data/objects.rs`
- [x] Spells database loader (Dat/Hechizos.dat — 65 spells) — `data/spells.rs`
- [x] NPC database loader (Dat/NPCs.dat + NPCs-HOSTILES.dat) — `data/npcs.rs`
- [x] Map loader (binary .map/.inf + INI .dat, 180 maps) — `data/maps.rs`
- [x] GameData struct loaded at startup — `data/mod.rs`
- [x] INI parser: added UTF-16 LE/BE BOM detection
- [x] 53 unit tests passing

## Phase 4: Core Game Loop & Movement ✅ COMPLETE
- [x] World state: map grids (100x100 per map), char index allocation — `game/world.rs`
- [x] Area visibility system (±8 X, ±6 Y = client 17x13 viewport) — `game/world.rs`
- [x] SendData routing (ToIndex, ToAll, ToMap, ToArea, ToAreaButIndex, ToMapButIndex) — `game/types.rs`
- [x] Movement handler (M opcode — heading, collision, grid update, area broadcast) — `game/handlers.rs`
- [x] Heading change (CHEA opcode)
- [x] Position request (RPU → PU)
- [x] Character rendering on login (CC packet to/from area users)
- [x] Character removal on disconnect (BP packet to area)
- [x] Map transitions via tile exits (warp with CM/PU/XM/N~ + CC exchange)
- [x] Chat system: area-based talk (;) — `game/handlers.rs`
- [x] Position correction on blocked movement (PT packet)
- [x] Per-user in-game state (position, heading, body, head, equipment anims)
- [x] 57 unit tests passing

## Phase 5: Chat & Communication ✅ COMPLETE
- [x] Talk (;) — T| packet with color and charindex to area (VB6 format: T|color°text°charindex)
- [x] Whisper (\) — P| packet to sender/receiver with font types
- [x] Yell (-) — N| packet to whole map (red text)
- [x] Font type system (~r~g~b~bold~italic format)
- [x] Color-coded messages (GM=yellow, dead=gray, normal=white)
- [ ] Guild chat (needs guild system)
- [ ] Party chat (needs party system)
- [ ] Console messages (|| with message ID templates)

## Phase 6: Combat System ✅ COMPLETE
- [x] Melee attack (AT opcode) — PvP with hit/miss/damage
- [x] Hit/miss calculation (skill-based attack power vs defense power, clamped 10-90%)
- [x] Damage calculation (weapon + strength bonus × class modifier)
- [x] Class-specific combat modifiers (12 classes)
- [x] PvP safety toggle (SEG → SEGON/SEGOFF)
- [x] Death system (user_die: stats reset, death notification, area broadcast)
- [x] Experience gain from kills
- [ ] Ranged attack (bows, MAXDISTANCIAARCO = 18)
- [ ] NPC combat
- [ ] Armor absorption (partially implemented)
- [ ] Criminal/citizen system

## Phase 7: Spell System ✅ COMPLETE
- [x] Spell casting (LH opcode) — mana check, skill check, targeting
- [x] Property spells: HP heal/damage, Mana restore, Stamina restore
- [x] Status spells: cure poison, paralyze, poison, invisibility
- [x] Magic words broadcast (T| to area)
- [x] Visual effects (CFX packet) and sound effects (TW packet)
- [x] Spell slots sent on login (SHS packets)
- [ ] Spell cooldowns
- [ ] NPC spell casting
- [ ] Summon/teleport/bubble spell types

## Phase 8: Inventory & Items ✅ COMPLETE
- [x] Inventory system (25 slots, loaded from charfile Obj1..Obj25 format)
- [x] CSI packet format (slot, objindex, name, amount, equipped, grh, type, stats, value)
- [x] Equip/unequip items (EQUI) — weapon, armor, shield, helmet
- [x] Equipment appearance broadcast (|W, |E, |C packets to area)
- [x] Item use (USA) — potions, food, drinks with stat restoration
- [x] Drop items (TI) — remove from inventory
- [x] Bulk stats on login ([ES packet with HP/Mana/Sta/Gold/Level/Exp/Name/Attrs)
- [x] Individual stat updates ([H], [M], [S], [G], [E] packets)
- [x] Inventory sent on login (INVI0 signal + all CSI slots)
- [x] Spell slots sent on login (SHS packets)
- [ ] Pick up items (AG — needs map item system)
- [ ] Item restrictions (class, level checks)
- [ ] Equipment stat bonuses applied to character

## Phase 9: NPC & AI
- [ ] NPC spawning on maps
- [ ] NPC AI ticks (100ms timer)
- [ ] Hostile NPC behavior (aggro, attack, chase)
- [ ] Pathfinding (PathFinding.bas port)
- [ ] NPC shops / vendors
- [ ] NPC dialogue
- [ ] Pet/mount system

## Phase 10: Advanced Systems
- [ ] Banking (40 slots, deposit/withdraw)
- [ ] Player-to-player trading
- [ ] Guild system (create, join, leave, wars, alliances)
- [ ] Quest system (31 quests, daily missions)
- [ ] Faction system (4 gods, ranks, equipment)
- [ ] Crafting (blacksmith — weapons, armor)
- [ ] Housing system
- [ ] Mail system (Correos)
- [ ] Tournament system
- [ ] Duel system
- [ ] CvC (Clan vs Clan)
- [ ] Events (wars, hunger games, etc.)
- [ ] World backup system

## Phase 11: GM & Admin Tools
- [ ] GM commands (all from TCP_HandleData4.bas)
- [ ] Player monitoring
- [ ] Kick/ban
- [ ] Teleport
- [ ] Item creation
- [ ] NPC spawning
- [ ] Server announcements

## Phase 12: Testing & Polish
- [ ] Integration tests with real VB6 client
- [ ] Stress testing (400 concurrent connections)
- [ ] Edge cases (malformed packets, reconnection, timeout)
- [ ] Performance profiling
- [ ] World save/load reliability
- [ ] Logging and monitoring

---

## Key Compatibility Notes

1. **Packet format MUST be byte-identical** to VB6 output
2. **Encryption chain**: Server→Client = AoDefServEncrypt→AoDefEncode+ENDC
3. **Key rotation**: counter increments per packet, wraps at 999999
4. **INI files**: read/write in Latin-1 encoding (Windows-1252)
5. **Obj.dat**: UTF-16 LE encoded INI (only exception — all others are Latin-1)
6. **Map .map/.inf files**: binary — little-endian, ByFlags bitfield per tile
7. **String encoding**: VB6 uses ANSI strings — Rust must handle Latin-1↔UTF-8
8. **Numeric precision**: VB6 Integer=16bit, Long=32bit, Double=64bit
