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

Full documentation in [`docs/`](docs/README.md):

**Operations:** [Linux Deploy](docs/LINUX_DEPLOY.md) | [Client Build](docs/CLIENT-BUILD.md) | [Troubleshooting](docs/TROUBLESHOOTING.md)

**Architecture:** [Server](docs/architecture/server-architecture.md) | [Protocol](docs/architecture/protocol.md) | [Game Systems](docs/architecture/game-systems.md) | [Data Formats](docs/architecture/data-formats.md) | [Rendering](docs/architecture/rendering.md) | [Assets](docs/architecture/assets.md) | [Deployment](docs/architecture/deployment.md)

**Skills:** [Combat](docs/skills/ao-combat.md) | [Spells](docs/skills/ao-spells.md) | [NPCs](docs/skills/ao-npc-ai.md) | [Crafting](docs/skills/ao-skills-crafting.md) | [Rendering](docs/skills/ao-rendering.md) | [Protocol](docs/skills/ao-protocol.md) | [Sprites](docs/skills/ao-sprite-indexing.md) | [UI Kit](docs/skills/ao-ui-kit.md) | [Maps](docs/skills/mapper.md)

---

Licensed under [AGPL-3.0](LICENSE).
