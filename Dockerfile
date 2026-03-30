# ── Stage 1: Build ───────────────────────────────────────────────
FROM rust:1.85-slim AS builder

WORKDIR /build

# Cache dependencies: copy manifests first, then do a dummy build
COPY server/source/Cargo.toml server/source/Cargo.lock ./
RUN mkdir -p server/source && echo "fn main() {}" > main.rs \
    && cargo build --release \
    && rm -rf main.rs target/release/deps/ao_server* target/release/ao-server

# Now copy real source and build
COPY server/source/ ./
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

# Server data (Maps, Dat, config) is bind-mounted from the host via docker-compose.
# Only the binary lives inside the image.
RUN mkdir -p /app/server \
    && chown -R ao:ao /app

EXPOSE 5028

# Health check: TCP connect to game port
HEALTHCHECK --interval=30s --timeout=5s --start-period=10s --retries=3 \
    CMD bash -c '</dev/tcp/localhost/5028' || exit 1

USER ao
WORKDIR /app/server

CMD ["/app/ao-server", "."]
