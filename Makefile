.PHONY: build run dev test clean

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
