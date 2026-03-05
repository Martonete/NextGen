# Argentum Online — Next Gen

Argentum Online rewritten from scratch: **Rust server** + **Godot 4 C# client**.

---

## Requirements

| Tool | Version | Download |
|------|---------|----------|
| Docker Desktop | latest | https://www.docker.com/products/docker-desktop/ |
| Godot | **4.4 .NET** | https://godotengine.org/download/archive/4.4-stable/ |
| .NET SDK | **8.0** | https://dotnet.microsoft.com/download/dotnet/8.0 |

> Godot **must** be the .NET variant. .NET SDK **must** be 8.0 (not 9.0).

---

## Server

One command:

```bash
git clone https://github.com/cyphercr0w/argentum-nextgen.git
cd argentum-nextgen
docker compose up -d
```

Verify: `docker compose logs -f ao-server` — should show `Listening on 0.0.0.0:5028`.

---

## Client

```bash
cd client
dotnet build
```

Open `client/project.godot` in Godot and press **F5**.

> Run `dotnet build` every time you change C# code.

---

## Database Configuration

Copy and edit `.env`:

```bash
cp .env.example .env
```

```env
DATABASE_URL=postgres://ao:ao_secret@localhost:5432/ao_server
POSTGRES_PASSWORD=ao_secret
```

Docker Compose reads this file automatically. Change `ao_secret` to your own password.

---

## Documentation

Detailed docs live in [`docs/`](docs/):

| Document | Description |
|----------|-------------|
| [Architecture](docs/01-ARCHITECTURE.md) | Module structure, event loop, data flow |
| [Protocol](docs/02-PROTOCOL.md) | Packet format, opcodes |
| [Game Systems](docs/03-GAME-SYSTEMS.md) | Combat, spells, crafting, guilds, GM commands |
| [Data Formats](docs/04-DATA-FORMATS.md) | File formats (maps, objects, charfiles) |
| [VB6 Parity](docs/05-VB6-PARITY.md) | VB6-specific behaviors |
| [Migration Status](docs/06-MIGRATION-STATUS.md) | What's done, what's remaining |
| [Server Config](docs/SERVER-CONFIG.md) | server.ini, environment variables |
| [Database](docs/DATABASE.md) | PostgreSQL schema, backup/restore |
| [Linux Deploy](docs/LINUX_DEPLOY.md) | Linux/VPS deployment |
| [Roadmap](docs/ROADMAP.md) | Remaining work and priorities |
| [Project Layout](docs/PROJECT-LAYOUT.md) | Folder structure reference |
| [Client Build Guide](docs/CLIENT-BUILD.md) | Standalone .exe export, patching, distribution |
| [Troubleshooting](docs/TROUBLESHOOTING.md) | Common errors and fixes |

---

Licensed under [AGPL-3.0](LICENSE).
