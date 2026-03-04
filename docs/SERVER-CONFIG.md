# Server Configuration Reference

## server.ini

The server reads `server/server.ini` on startup. All settings are in INI format.

### Network

| Setting | Default | Description |
|---------|---------|-------------|
| `ServerIp` | `127.0.0.1` | Bind address (use `0.0.0.0` for external access) |
| `StartPort` | `5028` | Game server TCP port |
| `MaxUsers` | `400` | Maximum concurrent player connections |
| `Encriptar` | `1` | Enable packet encryption (`0` = plaintext, `1` = encrypted) |

### Game

| Setting | Default | Description |
|---------|---------|-------------|
| `PuedeCrearPersonajes` | `1` | Allow new character creation |
| `AllowMultiLogins` | `1` | Allow multiple characters from same account |
| `IdleLimit` | `10` | Minutes before idle players are disconnected |
| `ServerSoloGMs` | `0` | Restrict server to GM accounts only |
| `Testing` | `0` | Enable testing mode (relaxed restrictions) |

### Multipliers

| Setting | Default | Description |
|---------|---------|-------------|
| `MultiplicadordeExp` | `1` | Experience gain multiplier (e.g., `2` = double XP) |
| `MultiplicadordeOro` | `1` | Gold drop multiplier |
| `MultiplicadordeDrop` | `1` | Item drop rate multiplier |

### Starting Position

| Setting | Default | Description |
|---------|---------|-------------|
| `StartMap` | `89` | Map number for new characters |
| `StartX` | `78` | X coordinate for new characters |
| `StartY` | `85` | Y coordinate for new characters |

### Version

| Setting | Default | Description |
|---------|---------|-------------|
| `Version` | `0.11.5` | Server version string |
| `ClientVersion` | `1.0.1` | Expected client version |
| `Notice` | (text) | Login screen notice message |

## Privilege Levels (GM hierarchy)

Configured in `server.ini` under named sections. Hierarchy from highest to lowest:

| Level | Section | Permission scope |
|-------|---------|-----------------|
| Admin | `[Administradores]` | All commands, server management |
| SubAdmin | `[SubAdministradores]` | Most commands, no server shutdown |
| Desarrollador | `[Desarrolladores]` | Debug commands, item/NPC creation |
| Director | `[Directores]` | Event management, moderate commands |
| Gran Dios | `[GranDioses]` | Advanced moderation |
| Dios | `[Dioses]` | Standard moderation |
| Events | `[Events]` | Event-only commands |
| SemiDios | `[SemiDioses]` | Limited moderation |
| Consejero | `[Consejeros]` | Help and advisory role |
| RolesMaster | `[RolesMasters]` | RP event management |

Each section lists the character names with that privilege level:
```ini
[Administradores]
Count=1
1=Shay
```

## Intervalos.ini

Located at `server/dat/Intervalos.ini`. Controls anti-speedhack timers (in server ticks of 40ms each):

| Setting | Default (ticks) | Approx. ms | Description |
|---------|----------------|------------|-------------|
| `Golpe` | `37` | ~1480ms | Melee attack cooldown |
| `Flechas` | `28` | ~1120ms | Ranged attack cooldown |
| `LanzarHechizo` | `13` | ~520ms | Spell cast cooldown |
| `PoteoU` | `8` | ~320ms | Potion use cooldown |
| `PoteoClick` | `6` | ~240ms | Potion click cooldown |
| `Work` | `10` | ~400ms | Crafting/resource cooldown |

## Environment Variables

| Variable | Default | Description |
|----------|---------|-------------|
| `DATABASE_URL` | `postgres://ao:ao_secret@localhost:5432/ao_server` | PostgreSQL connection string |
| `POSTGRES_PASSWORD` | `ao_secret` | Used by docker-compose for the PostgreSQL container |
| `RUST_LOG` | `ao_server=info` | Log verbosity (`debug`, `info`, `warn`, `error`) |
