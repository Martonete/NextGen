.PHONY: build run dev test clean docker-build docker-run docker-stop docker-logs \
       client client-build client-run client-editor client-clean client-export client-dist \
       pack-ui pack-fonts pack-init pack-maps pack-graficos pack-sounds pack-all help

.DEFAULT_GOAL := help

# Auto-detect dotnet binary (override with: make pack-ui DOTNET=/my/dotnet)
DOTNET ?= $(shell command -v dotnet 2>/dev/null || echo "/workspace/.dotnet/dotnet")
AOPAK_CLI = resources/compressor/lib/CLI/AoPakCli.csproj

# Auto-detect Godot binary (override with: make client GODOT=/my/path)
GODOT ?= $(shell command -v godot 2>/dev/null \
         || ls /opt/godot/Godot_v*mono*linux*x86_64 2>/dev/null | head -1 \
         || ls /opt/godot/Godot_v*linux*x86_64 2>/dev/null | head -1 \
         || echo "")

# ─── Server ───────────────────────────────────────────────────────

build:
	cargo build --release
	cp target/release/ao-server server/ao-server

run: build
	cd server && ./ao-server .

dev:
	cargo run -- ./server

test:
	cargo test

clean:
	cargo clean
	rm -f server/ao-server

# ─── Server (Docker) ─────────────────────────────────────────────

docker-build:
	docker build -t ao-server .

docker-run: docker-build
	docker compose up -d

docker-stop:
	docker compose down

docker-logs:
	docker compose logs -f ao-server

# ─── Client (Godot + C#) ─────────────────────────────────────────

client-build:
	cd client && dotnet build

client-run: client-build
	@test -n "$(GODOT)" || (echo "ERROR: Godot not found. Install it or run: make client-run GODOT=/path/to/godot" && exit 1)
	cd client && "$(GODOT)" --path .

client: client-run

client-editor:
	@test -n "$(GODOT)" || (echo "ERROR: Godot not found. Install it or run: make client-run GODOT=/path/to/godot" && exit 1)
	cd client && "$(GODOT)" --path . --editor

client-clean:
	cd client && dotnet clean

# ─── Client Export (Distribution) ───────────────────────────────

# Export standalone .exe (requires Godot export templates installed)
# Install templates: Godot Editor → Editor → Manage Export Templates → Download
client-export: client-build
	@test -n "$(GODOT)" || (echo "ERROR: Godot not found. Install it or run: make client-export GODOT=/path/to/godot" && exit 1)
	@mkdir -p client/build
	cd client && "$(GODOT)" --headless --export-release "Windows Desktop" build/ArgentumNextgen.exe
	@echo ""
	@echo "  Export complete → client/build/"

# Build a ready-to-distribute folder (exe + Data/) and zip it
client-dist: client-export
	@rm -rf dist/ArgentumNextgen
	@mkdir -p dist/ArgentumNextgen
	cp -r client/build/* dist/ArgentumNextgen/
	cp -r client/Data dist/ArgentumNextgen/
	cd dist && zip -r ArgentumNextgen.zip ArgentumNextgen/
	@echo ""
	@echo "  Distribution ready → dist/ArgentumNextgen.zip"
	@echo "  Contents:"
	@echo "    ArgentumNextgen.exe    (game executable)"
	@echo "    *.dll                    (.NET runtime + game assemblies)"
	@echo "    Data/                    (graphics, maps, config)"

# ─── Resource Packing (AoPak) ────────────────────────────────────

pack-ui:
	$(DOTNET) run --project $(AOPAK_CLI) -- pack resources/data/UI client/Data/ui.aopak

pack-fonts:
	$(DOTNET) run --project $(AOPAK_CLI) -- pack resources/data/Fonts client/Data/fonts.aopak

pack-init:
	$(DOTNET) run --project $(AOPAK_CLI) -- pack resources/data/INIT client/Data/init.aopak

pack-maps:
	$(DOTNET) run --project $(AOPAK_CLI) -- pack resources/data/Maps client/Data/maps.aopak

pack-graficos:
	$(DOTNET) run --project $(AOPAK_CLI) -- pack resources/data/Graficos client/Data/graficos.aopak

pack-sounds:
	$(DOTNET) run --project $(AOPAK_CLI) -- pack resources/data/Sounds client/Data/sounds.aopak

pack-all: pack-ui pack-fonts pack-init pack-maps pack-graficos pack-sounds

# ─── Help ─────────────────────────────────────────────────────────

help:
	@echo ""
	@echo "  Argentum Nextgen"
	@echo "  ────────────────────────────────────────"
	@echo ""
	@echo "  Server"
	@echo "    make build          Compile Rust server (release)"
	@echo "    make run            Build + run server locally"
	@echo "    make dev            Run server in dev mode (cargo run)"
	@echo "    make test           Run server tests"
	@echo "    make clean          Clean server build artifacts"
	@echo ""
	@echo "  Server (Docker)"
	@echo "    make docker-run     Build image + docker compose up"
	@echo "    make docker-stop    docker compose down"
	@echo "    make docker-logs    Follow server logs"
	@echo ""
	@echo "  Client"
	@echo "    make client         Build C# + run game (shortcut)"
	@echo "    make client-build   Compile C# only (dotnet build)"
	@echo "    make client-run     Build C# + run game"
	@echo "    make client-editor  Open Godot editor"
	@echo "    make client-clean   Clean C# build artifacts"
	@echo ""
	@echo "  Client Distribution"
	@echo "    make client-export  Export standalone .exe (Windows)"
	@echo "    make client-dist    Export + package as .zip with Data/"
	@echo ""
	@echo "  Resource Packing (AoPak)"
	@echo "    make pack-ui        Pack resources/data/UI → client/Data/ui.aopak"
	@echo "    make pack-fonts     Pack resources/data/Fonts → client/Data/fonts.aopak"
	@echo "    make pack-init      Pack resources/data/INIT → client/Data/init.aopak"
	@echo "    make pack-maps      Pack resources/data/Maps → client/Data/maps.aopak"
	@echo "    make pack-graficos  Pack resources/data/Graficos → client/Data/graficos.aopak"
	@echo "    make pack-sounds    Pack resources/data/Sounds → client/Data/sounds.aopak"
	@echo "    make pack-all       Pack all resources (ui+fonts+init+maps+graficos+sounds)"
	@echo "    Override dotnet:    make pack-ui DOTNET=/path/to/dotnet"
	@echo ""
