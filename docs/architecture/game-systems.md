# Game Systems

## 1. Account & Authentication

### Account Files (`Accounts/*.act`)
INI format with sections: `[INIT]` (password, email, PIN, security code, ban status, HD serial), `[PERSONAJE#]` (character names).

### Login Flow
1. Client sends `HardwareCheck` with HD serial → server checks ban list
2. Client sends `AccountLogin` with account/password/version → server validates credentials
3. Server sends `INIAC` (num chars + notice), `AddCharPreview` (per character), `CODEH` (security code)
4. Client sends `CharacterLogin`/`CharacterSelect` with character name → server loads charfile
5. Server sends full login sequence: stats, inventory, spells, map data, LOGGED

### Character Creation
Client sends `CreateCharacter` with race, gender, class, head, homeland, attributes. Server validates (attribute total = 210, class/race restrictions), creates charfile, then auto-logs in.

### Security
- IP ban list (`BanIps.dat`) — checked on connection
- HD serial ban — checked on `HardwareCheck`
- Multi-login prevention — same account can't login twice
- IP rate limiting — max connections per IP, connection cooldown
- Version check — client version must match server config

## 2. Character System

### Character File (`charfile/*.chr`)
INI format with sections:
- `[INIT]` — name, race, gender, class, homeland, head, body, heading, level, EXP
- `[STATS]` — HP, mana, stamina, hit, hunger, thirst, gold, bank gold
- `[ATRIBUTOS]` — strength, agility, intelligence, charisma, constitution
- `[SKILLS]` — 20 skills (0-100 each)
- `[FLAGS]` — dead, poisoned, paralyzed, criminal, hidden, navigating, privileges
- `[INVENTORY]` — 25 slots (object index, amount, equipped flag)
- `[BANCOINVENTORY]` — 40 bank slots (object index, amount)
- `[HECHIZOS]` — learned spell slots
- `[REPUTATION]` — 7 reputation categories
- `[FACCIONES]` — army/chaos enrollment, kills, re-enlistments

### Classes
Warrior, Mage, Cleric, Assassin, Bard, Druid, Paladin, Hunter, Fisher, Lumberjack, Miner, Blacksmith, Carpenter, Pirate, Thief, Bandit, Worker

### Races
Human, Elf, DarkElf, Dwarf, Gnome

### Attributes (5)
Strength, Agility, Intelligence, Charisma, Constitution — total must be 210 at creation.

### Skills (20)
Magic, Armed Combat, Ranged Combat, Unarmed Combat, Stabbing, Stealing, Mining, Logging, Fishing, Swimming, Sailing, Lockpicking, Taming, Hiding, Survival, Trading, Leadership, Smelting, Carpentry, Blacksmithing

## 3. World & Maps

### Map Format
Each map has 3 files:
- `Mapa<N>.map` — binary: 100×100 tile grid with 4 graphic layers + blocked flag
- `Mapa<N>.inf` — binary: exits, NPCs, objects per tile
- `Mapa<N>.dat` — INI: metadata (name, music, PK flag, backup flag, triggers)

### Map Layers (per tile)
| Layer | Field | Purpose |
|-------|-------|---------|
| Graphic[0] | `graphic[0]` | Base terrain (grass, water, stone) |
| Graphic[1] | `graphic[1]` | Overlay (water edges, paths) |
| Graphic[2] | `graphic[2]` | Objects on ground (trees, rocks) |
| Graphic[3] | `graphic[3]` | Roof/ceiling layer |
| Blocked | `blocked` | Tile walkability flag |

### Tile Triggers
| Value | Meaning | Behavior |
|-------|---------|----------|
| 0 | None | Normal tile |
| 1 | Safe Zone | No PvP, no stealing, no aggro |
| 2 | Anti-Trigger | Prevents certain actions |
| 3 | Under Roof | Interior tile |
| 4 | Safe Zone (PvP blocked) | PvP prevention on specific tiles |
| 5 | Arena Entrance | Arena/duel access point |
| 6 | Combat Zone (ZONAPELEA) | Forced PvP zone |
| 7 | Special | Context-dependent |

### PK Flag
**Important VB6 inversion**: In the `.dat` file, `Pk=0` means PvP is ENABLED (map is PK). `Pk=1` means PvP is DISABLED (safe map). The Rust loader inverts this: `pk: !get_bool("Pk")`.

### Water Detection
VB6 `HayAgua()` checks tile graphics, not the blocked flag:
- `graphic[0]` must be in range 1505–1520 (water tile graphics)
- `graphic[1]` must be 0 (no overlay)

Water tiles affect: boat navigation, fishing, walking restrictions.

### Tile Exits
Each tile can have an exit: `(target_map, target_x, target_y)`. Used for:
- Map border transitions (walk off edge → next map)
- Teleport tiles (with visual FX)
- Dungeon entrances/exits

## 4. NPC System

### NPC Database (`NPCs.dat` + `NPCs-HOSTILES.dat`)
396 NPC definitions with stats, AI type, drops, spells, commerce inventory.

### AI Types
| ID | Type | Behavior |
|----|------|----------|
| 1 | Static | No movement |
| 2 | Random Walk | 1/12 chance to move per AI tick |
| 3 | Hostile Chase | Attack nearby players within vision range |
| 4 | Defense | Follow and attack player who attacked first |
| 5 | Guard | Attack criminal players on sight |
| 8 | Follow Owner | Pet follows summoner |
| 10 | Pathfinding | BFS pathfinding to target |

### NPC Vision Range
- X: 11 tiles, Y: 9 tiles (asymmetric)

### Spawn & Respawn
- NPCs spawn from map `.inf` data at server startup (1,510 total)
- Dead NPCs respawn at original position every 30 seconds (if tile is free)
- NPC runtime index (`NpcIndex`) and shared `CharIndex` for client rendering

### NPC Combat
- NPCs attack with melee (min_hit–max_hit range)
- Attack/evasion rolls: `poder_ataque` vs player `poder_evasion`
- Some NPCs cast spells (`lanza_spells` flag, `spells` list)
- Some NPCs poison on hit (`veneno` flag)
- NPCs can be paralyzed (counter-based, decremented per AI tick)

### NPC Commerce
NPCs with `comercia=true` open a shop window. Inventory items have inflation multiplier.

### Damage Tracking
Each NPC tracks `damage_received: Vec<(ConnectionId, i32)>` for proportional EXP distribution on kill.

## 5. Combat System

### Melee Combat
1. Anti-flood check (`interval_at` timer, 3 ticks @ 40ms)
2. Validate attacker state (alive, not paralyzed, not meditating)
3. Find target in facing tile (player or NPC)
4. Attack roll: `rand(1..100) <= hit_chance` (skill + weapon + attribute bonuses)
5. Damage roll: `rand(min_hit..max_hit)` modified by:
   - Strength bonus
   - Weapon damage range
   - Critical hit: 1.8× multiplier
   - Armor absorption (reduced by weapon `refuerzo`/penetration)
6. Apply damage, check death, distribute EXP

### Ranged Combat
- Requires equipped ranged weapon (bow/crossbow) + ammunition in municion slot
- Consumes 1 ammo per shot
- Same hit/damage formulas as melee but with different skill

### Backstab (Apuñalar)
- Assassin class: 1.5× player damage, 2× NPC damage
- Auto-success if attacker faces same direction as target (backstab angle)

### Disarm (Desarmar)
- Skill-based chance to unequip victim's weapon
- Uses Wrestling skill for probability calculation

### PvP Rules
- Safe toggle (`SEG` opcode) — players can opt out of PvP
- Safe zones (trigger=1,4) — no PvP allowed
- Combat zones (trigger=6) — forced PvP
- Criminal flag — attacking citizens makes you criminal
- Kill tracking: `criminales_matados`, `ciudadanos_matados`
- Dedup: `LastCrimMatado`/`LastCiudMatado` prevents reputation farming

### Death
- Player dies → body changes to ghost (body=8/500, head=500)
- Ghost cannot attack, use items, or interact
- Resurrection via: spell, potion (HP=35), GM command, revive NPC

## 6. Spell System

### Spell Database (`Hechizos.dat`)
65 spells with properties: mana cost, target type, effects, required class/skills.

### Cast Flow
1. Player sends `LH<slot>` → validate mana, spell known, not paralyzed
2. Target selection: self, facing tile, or named target
3. Effect application: damage, heal, buff, paralysis, poison, invisibility, summon
4. Mana deduction, cooldown check, FX broadcast

### Spell Types
- Damage (fire, lightning, etc.)
- Healing (HP/mana recovery)
- Buffs/Debuffs (paralysis, poison, invisibility)
- Summon (create pet NPC)
- Resurrection (revive dead player)
- Removal (cure poison, remove paralysis)

## 7. Inventory & Equipment

### Inventory
- 25 slots per player
- Each slot: object index, amount, equipped flag
- Operations: use, equip, drop, swap, pick up

### Equipment Slots
| Slot | Field | Types |
|------|-------|-------|
| Weapon | `equip.weapon` | Sword, Axe, Bow, Staff, etc. |
| Armor | `equip.armor` | Body armor (changes body graphic) |
| Shield | `equip.shield` | Shield (shield animation) |
| Helmet | `equip.helmet` | Helmet (casco animation) |
| Ammo | `equip.municion` | Arrows, bolts |

### Object Types
Weapon, Armor, Shield, Helmet, Food, Potion, Scroll, Key, Boat, Mount, Instrument, Teleport, Tool, Container, Gold, Gem, Mineral, Wood, Fish, Forge item, and more.

### Potion Subtypes (`tipo_pocion`)
| ID | Type | Effect |
|----|------|--------|
| 1 | Agility | Temporary agility buff |
| 2 | Strength | Temporary strength buff |
| 3 | HP | Restore hit points |
| 4 | Mana | Restore mana points |
| 5 | Poison Cure | Remove poison status |
| 6 | Remo | Remove paralysis (costs 60 HP, 3-round cooldown for non-warrior/hunter) |

### Boat System
- `ObjType::Boat` — used via `USA` opcode (double-click), NOT equippable
- Mount: requires level ≥ 30, adjacent water tile (HayAgua check in 4 directions)
- Mount effects: head=0, weapon/shield/helmet anim=0, body=boat graphic
- Movement while navigating: only water tiles passable, land tiles blocked
- Dismount: requires adjacent land tile (non-blocked, non-water)
- Dismount effects: restore original head, re-apply equipped armor/weapon/shield/helmet graphics

## 8. Banking System

### Personal Bank
- 40 slots per player
- Deposit/withdraw via banker NPC (`DEPO`/`RETI` opcodes)
- Gold deposit/withdraw separate from items

### Guild Bank
Not implemented (removed).

## 9. Trading System

### Player-to-Player Trade
1. Initiate with left-click on player
2. Offer items via `UOC` (offer item) and `UOR` (offer gold)
3. Both players must accept (`TDR` with code 0)
4. Validation: intransferable items, keys, god items blocked
5. Items swapped atomically

### NPC Commerce
1. Left-click on merchant NPC opens shop window
2. Buy (`COMP`) — price from object value × NPC inflation
3. Sell (`VEND`) — reduced price (buy/sell spread)
4. Close (`FINCOM`)

## 10. Crafting System

### Skills & Resources
| Skill | Tool | Resource | Product |
|-------|------|----------|---------|
| Fishing (Pesca) | Fishing rod | Water tile | Fish items |
| Logging (Talar) | Axe | Tree tile | Wood items |
| Mining (Mineria) | Pickaxe | Mineral tile | Ore items |
| Smelting (Fundir) | Forge NPC | Ores | Ingots |
| Blacksmithing (Herreria) | Forge | Ingots | Weapons/Armor |
| Carpentry (Carpinteria) | Workshop | Wood | Bows/Shields |

### Crafting Flow
1. `WLC` (Work Left Click) → validate tool equipped, resource nearby
2. Skill check (random vs skill level)
3. On success: consume resource, create product, gain skill XP
4. On failure: message, possible tool breakage

### Taming (Domar)
- Use skill on NPC → chance to make it a pet
- Pet follows owner (AI type 8)
- Commanded via chat commands

### Stealing (Robar)
- Thief skill: steal gold or items from players
- Skill check against target's anti-theft ability
- Makes thief criminal if caught

## 11. Guild System

### Guild Data
- Guild name, description, codex (rules)
- Leader, members list, applicants
- Alignment (citizen/criminal guild)

### Operations
- Create guild (`CIG`) — requires minimum level and gold
- Apply (`SOLICITUD`) / Accept (`ACEPTARI`) / Reject (`RECHAZAR`)
- Expel member (`ECHARCLA`)
- Update codex/description (`DESCOD`)
- Guild news (`ACTGNEWS`)

## 12. Quest System

### Quest Data (`Quests.dat`)
Each quest has: description, requirements (level, items, kills), rewards (EXP, gold, items).

### Quest Flow
1. Talk to quest NPC → request quest list (`IQUEST`)
2. Get quest details (`INFD`)
3. Accept quest (`ACQT`)
4. Complete objectives (kill NPCs, collect items, visit locations)
5. Turn in to NPC → receive rewards

## 13. Faction System

### Two Factions
- **Armada Real** (Royal Army) — citizen faction
- **Fuerzas del Caos** (Chaos Forces) — criminal faction

### Enrollment
- Talk to faction NPC, meet requirements (level, reputation)
- Earn faction points by killing enemy faction members
- Rank progression based on accumulated kills

## 14. Event System

### Event Types
| ID | Type | Description |
|----|------|-------------|
| 1 | CTF | Capture the flag |
| 2 | JDH | Juego del Hambre (Hunger Games) |
| 3 | LUZ | Light event |
| 4 | ARAM | Arena match |
| 5 | BatMistica | Mystical Battle |
| 6 | Faccionario | Faction war |
| 7 | TorneoAuto | Automatic tournament |
| 8 | Guerra | War event |

### Event Flow
- GM starts event → players join via command
- Teleport to event map, teams assigned
- Special rules (respawn delay, restricted areas)
- Winner determined by kills/objectives → rewards

## 15. GM Command System

90 GM command handlers implemented (covering 100+ commands), requiring various privilege levels:

### Common Commands
| Command | Level | Description |
|---------|-------|-------------|
| `/ONLINE` | USER | Show online count |
| `/SALIR` | USER | Disconnect |
| `/VIAJAR` | USER | Travel to city |
| `/TELEP YO` | SEMIDIOS | Teleport self to target |
| `/TELEPLOC` | SEMIDIOS | Teleport to coordinates |
| `/GO` | SEMIDIOS | Go to player |
| `/IRA` | SEMIDIOS | Teleport to named player |
| `/BAN` | DIOS | Ban player |
| `/KICK` | DIOS | Kick player |
| `/CREAR` | DIOS | Spawn item |
| `/SPAWNEAR` | DIOS | Spawn NPC |
| `/REVIVIR` | DIOS | Resurrect player |
| `/SILENCIAR` | DIOS | Mute player |
| `/INVISIBLE` | DIOS | Toggle invisibility |

## 16. Anti-Cheat System

### Interval Timers (40ms tick)
Each action has a cooldown timer decremented every 40ms:
- `interval_pu` = 6 (movement: 240ms between moves)
- `interval_at` = 3 (attack: 120ms between attacks)
- `interval_lh` = varies (spell cooldown)

### Macro Detection
- `TENGOMACROS` opcode — client reports suspected macro use
- Server tracks suspicious patterns

### IP Security
- Max connections per IP
- Connection rate limiting
- IP ban persistence
