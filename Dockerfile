# ── Stage 1: Build ───────────────────────────────────────────────
FROM rust:1.85-slim AS builder

WORKDIR /build

# Cache dependencies: copy manifests first, then do a dummy build
COPY Cargo.toml Cargo.lock ./
RUN mkdir src && echo "fn main() {}" > src/main.rs \
    && cargo build --release \
    && rm -rf src target/release/deps/ao_server* target/release/ao-server

# Now copy real source and build
COPY src/ src/
RUN cargo build --release

# ── Stage 2: Runtime ────────────────────────────────────────────
FROM debian:bookworm-slim

RUN apt-get update \
    && apt-get install -y --no-install-recommends ca-certificates \
    && rm -rf /var/lib/apt/lists/* \
    && groupadd -r ao && useradd -r -g ao -d /app ao

WORKDIR /app

# Copy binary
COPY --from=builder /build/target/release/ao-server /app/ao-server

# Copy server data (read-only game data)
COPY server/Maps/       /app/server/Maps/
COPY server/Dat/        /app/server/Dat/
COPY server/Dioses/     /app/server/Dioses/
COPY server/WorldBackUp/ /app/server/WorldBackUp/
COPY server/Server.ini  /app/server/Server.ini
COPY server/Configuracion.ini /app/server/Configuracion.ini
COPY server/Facciones.ini /app/server/Facciones.ini
COPY server/HD.ini      /app/server/HD.ini

# Create stateful directories (will be mounted as volumes)
RUN mkdir -p /app/server/charfile /app/server/Accounts /app/server/guilds /app/server/logs /app/server/Logs \
    && chown -R ao:ao /app

# Volumes for persistent state
VOLUME ["/app/server/charfile", "/app/server/Accounts", "/app/server/guilds", "/app/server/logs"]

EXPOSE 5028 7669

# Health check: TCP connect to game port
HEALTHCHECK --interval=30s --timeout=5s --start-period=10s --retries=3 \
    CMD bash -c '</dev/tcp/localhost/5028' || exit 1

USER ao
WORKDIR /app/server

CMD ["/app/ao-server", "."]
