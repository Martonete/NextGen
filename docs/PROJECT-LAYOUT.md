# Project Layout

```
argentum-nextgen/
  server/
    source/          Rust server source code
      main.rs          Entry point
      data/            Data loaders (objects, spells, NPCs, maps)
      game/            Game logic (combat, movement, AI, handlers)
      net/             TCP networking and packet protocol
    dat/             Game data files (Obj.dat, NPCs.dat, Hechizos.dat, etc.)
    maps/            Map files (.map, .inf, .dat)
    charfile/        Character save files (legacy format)
    migrations/      PostgreSQL schema migrations
    server.ini       Server configuration
    logs/            Runtime logs
  client/
    Scenes/          Godot scenes (.tscn)
    Scripts/         C# source code
      Main.cs            Game loop and packet dispatch
      GameState.cs       Global game state
      PacketHandler.cs   120+ packet handlers
      InputHandler.cs    Keyboard/mouse input
      Rendering/
        CharRenderer.cs    Character rendering (body, head, weapon, shield, helmet, aura, FX)
        WorldRenderer.cs   4-pass tile rendering, lights, particles
    Data/
      INIT/            Asset indices, config files, auras, particles
      Graficos/        Sprite sheets (PNG)
      Maps/            Client-side map files
    project.godot    Godot 4.4 C# project file
  Cargo.toml         Rust dependencies
  Makefile           Build commands for server + client
  Dockerfile         Multi-stage Docker build (Rust 1.85 → Debian slim)
  docker-compose.yml PostgreSQL + ao-server orchestration
  .env.example       Database connection template
```
