# Deployment & Configuration

## 1. PostgreSQL Setup

The server uses **PostgreSQL** for persistent storage. Migrations run automatically on startup.

**Docker:** PostgreSQL is included in `docker-compose.yml` -- no manual setup needed.

**From source:** Create the database as shown in the [README](../../README.md) platform-specific instructions.

---

## 2. Database Schema (auto-migrated)

| Table | Description |
|-------|-------------|
| `accounts` | Login credentials, banking, ban status |
| `account_bank` | Account-level bank storage (max 40 slots) |
| `characters` | Full character state (stats, position, skills, flags, equipment) |
| `character_inventory` | Inventory items (max 25 slots, tracks equipped status) |
| `character_bank` | Character bank items (max 40 slots) |
| `guilds` | Guild metadata, level, points, castle ownership |
| `guild_members` | Guild membership |
| `guild_applicants` | Pending guild applications |
| `guild_bank_items` | Guild bank storage (not active) |
| `banned_ips` | IP ban list |
| `banned_hds` | Hardware ID ban list |
| `rankings` | Leaderboard data (9 categories x 10 positions) |

---

## 3. server.ini Reference

The server reads `server/server.ini` on startup. All settings are in INI format.

### Network

| Setting | Default | Description |
|---------|---------|-------------|
| `ServerIp` | `127.0.0.1` | Bind address (use `0.0.0.0` for external access) |
| `StartPort` | `5028` | Game server TCP port |
| `MaxUsers` | `400` | Maximum concurrent player connections |

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
| `Version` | `0.14.0` | Server version string |
| `ClientVersion` | `1.0.0` | Expected client version |
| `Notice` | (text) | Login screen notice message |

---

## 4. Privilege Levels (GM hierarchy)

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

---

## 5. Intervalos.ini

Located at `server/dat/Intervalos.ini`. Controls anti-speedhack timers (in server ticks of 40ms each):

| Setting | Default (ticks) | Approx. ms | Description |
|---------|----------------|------------|-------------|
| `Golpe` | `37` | ~1480ms | Melee attack cooldown |
| `Flechas` | `28` | ~1120ms | Ranged attack cooldown |
| `LanzarHechizo` | `13` | ~520ms | Spell cast cooldown |
| `PoteoU` | `8` | ~320ms | Potion use cooldown |
| `PoteoClick` | `6` | ~240ms | Potion click cooldown |
| `Work` | `10` | ~400ms | Crafting/resource cooldown |

---

## 6. Environment Variables

| Variable | Default | Description |
|----------|---------|-------------|
| `DATABASE_URL` | `postgres://ao:ao_secret@localhost:5432/ao_server` | PostgreSQL connection string |
| `POSTGRES_PASSWORD` | `ao_secret` | Used by docker-compose for the PostgreSQL container |
| `RUST_LOG` | `ao_server=info` | Log verbosity (`debug`, `info`, `warn`, `error`) |

---

## 7. Backup & Restore

```bash
# Backup (Docker)
docker compose exec postgres pg_dump -U ao ao_server > backup.sql

# Backup (local)
pg_dump -U ao ao_server > backup.sql

# Restore
psql -U ao ao_server < backup.sql
```
