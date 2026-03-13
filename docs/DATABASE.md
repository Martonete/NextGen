# Database

## PostgreSQL Setup

The server uses **PostgreSQL** for persistent storage. Migrations run automatically on startup.

**Docker:** PostgreSQL is included in `docker-compose.yml` — no manual setup needed.

**From source:** Create the database as shown in the [README](../README.md) platform-specific instructions.

## Schema (auto-migrated)

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

## Backup & Restore

```bash
# Backup (Docker)
docker compose exec postgres pg_dump -U ao ao_server > backup.sql

# Backup (local)
pg_dump -U ao ao_server > backup.sql

# Restore
psql -U ao ao_server < backup.sql
```
