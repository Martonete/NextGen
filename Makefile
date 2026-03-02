.PHONY: build run dev test clean docker-build docker-run docker-stop docker-logs \
       client client-build client-run client-editor client-clean help

.DEFAULT_GOAL := help

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
	cd client && godot --path .

client: client-run

client-editor:
	cd client && godot --path . --editor

client-clean:
	cd client && dotnet clean

# ─── Help ─────────────────────────────────────────────────────────

help:
	@echo ""
	@echo "  Tierras Sagradas AO"
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
