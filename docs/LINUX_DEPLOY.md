# Linux Deploy Guide — Tierras Sagradas AO

Step-by-step guide to run the full stack (Rust server + Godot 4 C# client) on a Linux machine (Debian 12/13, Ubuntu 22.04+).

---

## 1. System Dependencies

```bash
sudo apt update
sudo apt install git build-essential ca-certificates curl gnupg unzip -y
```

---

## 2. Install Docker

```bash
# Docker GPG key
sudo install -m 0755 -d /etc/apt/keyrings
curl -fsSL https://download.docker.com/linux/debian/gpg | sudo gpg --dearmor -o /etc/apt/keyrings/docker.gpg
sudo chmod a+r /etc/apt/keyrings/docker.gpg

# Docker repository
echo \
  "deb [arch=$(dpkg --print-architecture) signed-by=/etc/apt/keyrings/docker.gpg] https://download.docker.com/linux/debian \
  $(. /etc/os-release && echo "$VERSION_CODENAME") stable" | \
  sudo tee /etc/apt/sources.list.d/docker.list > /dev/null

# Install Docker Engine + Compose
sudo apt update
sudo apt install docker-ce docker-ce-cli containerd.io docker-compose-plugin -y

# Run docker without sudo
sudo usermod -aG docker $USER
newgrp docker

# Verify
docker --version
docker compose version
```

> **Ubuntu**: Replace `https://download.docker.com/linux/debian` with `https://download.docker.com/linux/ubuntu`.

> **Debian 13 (Trixie)**: If `$VERSION_CODENAME` is not supported yet, replace it with `bookworm`.

---

## 3. Install .NET 6.0 SDK

Required by the Godot 4.3 C# client.

```bash
curl -fsSL https://dot.net/v1/dotnet-install.sh -o dotnet-install.sh
chmod +x dotnet-install.sh
./dotnet-install.sh --channel 6.0

# Add to PATH
echo 'export DOTNET_ROOT=$HOME/.dotnet' >> ~/.bashrc
echo 'export PATH=$PATH:$DOTNET_ROOT:$DOTNET_ROOT/tools' >> ~/.bashrc
source ~/.bashrc

# Verify
dotnet --version
```

---

## 4. Install Godot 4.3 .NET

```bash
cd ~
wget https://github.com/godotengine/godot/releases/download/4.3-stable/Godot_v4.3-stable_mono_linux_x86_64.zip
unzip Godot_v4.3-stable_mono_linux_x86_64.zip
sudo mv Godot_v4.3-stable_mono_linux_x86_64 /opt/godot

# Create alias for easy access
echo 'alias godot="/opt/godot/Godot_v4.3-stable_mono_linux.x86_64"' >> ~/.bashrc
source ~/.bashrc

# Verify (opens Godot editor)
godot --version
```

---

## 5. Clone the Repository

```bash
cd ~
git clone https://github.com/cyphercr0w/server-rust-tsao.git
cd server-rust-tsao
```

---

## 6. Start the Server

```bash
cd ~/server-rust-tsao

# First time: build the Rust server image + start PostgreSQL
docker compose up -d --build

# Verify it's running
docker compose logs -f ao-server
# Should show: "Listening on 0.0.0.0:5028"
# Press Ctrl+C to exit logs
```

Subsequent runs (no code changes):

```bash
docker compose up -d
```

Common commands:

| Command | Description |
|---------|-------------|
| `docker compose up -d --build` | Build and start (first time or after code changes) |
| `docker compose up -d` | Start without rebuilding |
| `docker compose logs -f ao-server` | Follow server logs |
| `docker compose down` | Stop everything |
| `docker compose restart ao-server` | Restart server only |

---

## 7. Run the Client

### Option A: From Godot editor

```bash
godot --path ~/server-rust-tsao/client/
```

Or manually:
1. Run `godot` in terminal
2. Click **Import** → navigate to `~/server-rust-tsao/client/` → select `project.godot`
3. Click **Import & Edit**
4. Wait for C# solution to compile (first time)
5. Press **F5** to run

### Option B: Export as standalone binary

1. Open the project in Godot editor
2. **Project → Export** → Add Linux preset
3. Export as `.x86_64` binary
4. Run directly: `./TierrasSagradas.x86_64`

---

## Connection

The client connects to `127.0.0.1:5028` by default. If the server runs on a different machine, edit the connection IP in the client before running.

| Port | Protocol | Service |
|------|----------|---------|
| 5028 | TCP | Game server |
| 5432 | TCP | PostgreSQL (internal, not exposed to host) |

---

## Troubleshooting

| Problem | Solution |
|---------|----------|
| `docker: permission denied` | Run `sudo usermod -aG docker $USER` then log out and back in |
| `docker compose` not found | Install `docker-compose-plugin` (not the old `docker-compose` pip package) |
| `dotnet: command not found` | Run `source ~/.bashrc` or open a new terminal |
| Godot shows black screen | Ensure GPU drivers are installed. Try `godot --rendering-driver opengl3` |
| Godot C# build errors | Run **Build → Build Solution** (Ctrl+Shift+B) in the editor |
| `Connection refused` on client | Make sure the server is running: `docker compose logs ao-server` |
| Server crashes on startup | Check `server/server.ini` exists and `dat/`, `maps/` directories have data files |
