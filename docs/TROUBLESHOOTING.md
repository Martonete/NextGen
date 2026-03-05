# Troubleshooting

## Client

| Problem | Cause | Fix |
|---------|-------|-----|
| "Falló la carga debido a dependencias faltantes" | C# not compiled | Run `dotnet build` in `client/` |
| No `C#` option in `Project → Tools` | Standard Godot (not .NET) | Download **Godot 4.4 .NET** variant |
| "Failed to build project" / MSBuild error | Wrong .NET version | Install **.NET SDK 8.0** (not 9.0) |
| `dotnet --version` shows 9.x only | .NET 8 not installed | Install .NET 8.0 alongside 9 (they coexist) |
| "Connection refused" | Server not running | Start server first |
| Assets missing / white textures | Missing `Data/` folder | Ensure `client/Data/` has `INIT/`, `Graficos/`, `Maps/` |
| Changes not taking effect | Stale assemblies | Run `dotnet build` after every code change |
| Low FPS | Default settings | F10 → Render tab → adjust |
| "this project contains c files but no solution file" | Missing .sln | Already in repo — do NOT use `Create C# Solution`, just `dotnet build` |
| .csproj overwritten by Godot | Godot regenerated it | `git checkout client/TierrasSagradasAO.csproj` |

## Server

| Problem | Fix |
|---------|-----|
| "Listening on 0.0.0.0:5028" never appears | Check PostgreSQL is running and `DATABASE_URL` is correct |
| "connection refused" to database | `docker compose up -d postgres` |
| Port 5028 in use | `netstat -tlnp | grep 5028` → kill the process |
| Docker build fails | Ensure Docker has 4GB+ RAM |
| Database migration errors | `docker compose down -v && docker compose up -d` |

## Quick Checks

```bash
# Server running?
nc -z localhost 5028

# Database OK?
docker compose exec postgres psql -U ao -d ao_server -c "SELECT count(*) FROM accounts;"

# Docker status
docker compose ps

# Full reset
docker compose down -v && docker compose up -d --build
```
