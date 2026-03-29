# Project Layout

```
argentum-nextgen/
  server/
    source/          Rust server source code
      main.rs          Entry point
      config/          INI parser, ServerConfig
      crypto/          4-layer encryption (legacy text protocol)
      data/            Data loaders (objects, spells, NPCs, maps)
      db/              PostgreSQL persistence (accounts, chars, guilds, bans)
      game/            Game logic (combat, movement, AI, handlers)
      net/             TCP networking and packet protocol
      protocol/        Packet IDs, binary builders, field parsers
    dat/             Game data files (Obj.dat, NPCs.dat, Hechizos.dat, etc.)
    maps/            Map files (.map, .inf, .dat)
    migrations/      PostgreSQL schema migrations
    server.ini       Server configuration
    logs/            Runtime logs
  client/
    Scenes/          Godot scenes (.tscn)
    Scripts/         C# source code
      Main.cs            Game loop and packet dispatch
      GameState.cs       Global game state
      Game/              Input routing, sound, panel sync
      Network/           TCP client, packet handlers, ByteQueue
      Rendering/         WorldRenderer, CharRenderer, particles, weather
      UI/                All UI forms and panels (RpgBaseForm, RpgTheme)
      Data/              Data loaders (GRH, heads, bodies, fonts)
    Data/
      INIT/            Asset indices, config files, auras, particles
      Graficos/        Sprite sheets (PNG)
      Maps/            Client-side map files
    project.godot    Godot 4.4 C# project file
  Cargo.toml         Rust dependencies
  Dockerfile         Multi-stage Docker build (Rust 1.85 → Debian slim)
  docker-compose.yml PostgreSQL + ao-server orchestration
  .env.example       Database connection template
```

## Codebase Stats

**Server — 78 Rust files, 38,097 LOC**

| File | Lines | Description |
|------|------:|-------------|
| `game/types.rs` | 1,534 | UserState, GameState, IntervalSettings |
| `game/handlers/spells.rs` | 1,462 | Spell casting and effects |
| `game/handlers/combat.rs` | 1,337 | Melee/ranged PvP, damage formulas |
| `game/handlers/guilds_handler.rs` | 1,178 | Guild operations and diplomacy |
| `game/handlers/auth.rs` | 1,151 | Login flow, account creation |
| `game/handlers/npcs.rs` | 1,140 | NPC combat, drops, AI reactions |
| `game/handlers/commerce.rs` | 1,079 | Trading, shops, banking |
| `game/handlers/ticks/npc_ai.rs` | 1,077 | NPC AI movement and targeting |
| `db/charfile.rs` | 1,036 | Character persistence (PostgreSQL) |
| `game/handlers/common.rs` | 995 | Shared utilities, cooldown checks |
| `game/handlers/gm_items.rs` | 923 | GM item/NPC spawn commands |
| `game/handlers/gm_moderation.rs` | 886 | GM moderation commands |
| `game/handlers/gm_query.rs` | 875 | GM query/info commands |
| `game/handlers/inventory/use_item.rs` | 864 | Item usage (potions, boats, scrolls) |
| `game/handlers/mod.rs` | 808 | Packet dispatcher routing |
| `game/handlers/parity_gm.rs` | 773 | VB6 parity GM handlers |
| `game/handlers/player_commands.rs` | 772 | /cmd handlers (gambling, travel) |
| `game/handlers/skills/craft.rs` | 765 | Smithing, carpentry, smelting |
| `game/handlers/inventory/click.rs` | 710 | Left/right click handlers |
| `game/handlers/slash_commands.rs` | 695 | Chat slash commands |

**Client — 105 C# files, 35,313 LOC**

| File | Lines | Description |
|------|------:|-------------|
| `UI/RpgTheme.cs` | 1,729 | Centralized UI theme factory |
| `Main.cs` | 907 | Game loop, scene management |
| `Network/PacketHandler.Movement.cs` | 866 | Movement packet handlers |
| `Network/PacketHandler.cs` | 856 | Text packet dispatch |
| `Network/PacketHandler.Binary.cs` | 845 | Binary packet dispatch |
| `Main.Gameplay.cs` | 779 | Login flow, connection |
| `Network/PacketHandler.Combat.cs` | 751 | Combat packet handlers |
| `UI/OptionsPanel.cs` | 720 | Options/settings panel |
| `Rendering/CharRenderer.cs` | 691 | Character rendering (5 layers) |
| `Network/PacketIds.cs` | 679 | Opcode constants (client + server) |
| `Rendering/WorldRenderer.cs` | 676 | 6-pass world rendering |
| `Game/SoundManager.cs` | 662 | Positional audio system |
| `Network/PacketHandler.Social.cs` | 658 | Chat, mail, guild packets |
| `UI/OptionsPanel.Clan.cs` | 652 | Clan/guild management UI |
| `Game/GameState.cs` | 644 | Global game state |
| `Network/ClientPackets.cs` | 607 | Outbound packet builders |
| `Network/PacketHandler.TextMap.cs` | 586 | Map/world text packets |
| `UI/VaultPanel.cs` | 579 | Bank vault UI |
| `UI/GmPanel.cs` | 564 | GM administration panel |
| `UI/StatsPanel.cs` | 556 | Character stats panel |

**Combined: 183 files, 73,410 LOC**
