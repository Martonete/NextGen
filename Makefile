.PHONY: build run dev test clean docker-build docker-run docker-stop

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

docker-build:
	docker build -t ao-server .

docker-run: docker-build
	docker compose up -d

docker-stop:
	docker compose down
