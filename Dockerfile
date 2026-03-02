# ── Stage 1: Build ───────────────────────────────────────────────
FROM rust:1.85-slim AS builder

WORKDIR /build

# Cache dependencies: copy manifests first, then do a dummy build
COPY Cargo.toml Cargo.lock ./
RUN mkdir -p server/source && echo "fn main() {}" > server/source/main.rs \
    && cargo build --release \
    && rm -rf server/source target/release/deps/ao_server* target/release/ao-server

# Now copy real source and build
COPY server/source/ server/source/
COPY server/migrations/ server/migrations/
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

# Copy embedded migrations (compiled into the binary via sqlx::migrate!)
# They are included at compile time, but we keep the dir for reference.

# Copy server data (read-only game data)
COPY server/Maps/       /app/server/Maps/
COPY server/Dat/        /app/server/Dat/
COPY server/Dioses/     /app/server/Dioses/
COPY server/WorldBackUp/ /app/server/WorldBackUp/
COPY server/Server.ini  /app/server/Server.ini
COPY server/Configuracion.ini /app/server/Configuracion.ini
COPY server/Facciones.ini /app/server/Facciones.ini

# Create logs directory (only stateful dir remaining — DB handles the rest)
RUN mkdir -p /app/server/logs /app/server/Logs \
    && chown -R ao:ao /app

VOLUME ["/app/server/logs"]

EXPOSE 5028

# Health check: TCP connect to game port
HEALTHCHECK --interval=30s --timeout=5s --start-period=10s --retries=3 \
    CMD bash -c '</dev/tcp/localhost/5028' || exit 1

USER ao
WORKDIR /app/server

CMD ["/app/ao-server", "."]
