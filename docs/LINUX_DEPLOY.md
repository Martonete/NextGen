# Linux Deploy Guide — Argentum Nextgen

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

## 3. Install .NET 8.0 SDK

Required by the Godot 4.4 C# client.

```bash
curl -fsSL https://dot.net/v1/dotnet-install.sh -o dotnet-install.sh
chmod +x dotnet-install.sh
./dotnet-install.sh --channel 8.0

# Add to PATH
echo 'export DOTNET_ROOT=$HOME/.dotnet' >> ~/.bashrc
echo 'export PATH=$PATH:$DOTNET_ROOT:$DOTNET_ROOT/tools' >> ~/.bashrc
source ~/.bashrc

# Verify
dotnet --version
```

---

## 4. Install Godot 4.4 .NET

```bash
cd ~
wget https://github.com/godotengine/godot/releases/download/4.4-stable/Godot_v4.4-stable_mono_linux_x86_64.zip
unzip Godot_v4.4-stable_mono_linux_x86_64.zip
sudo mv Godot_v4.4-stable_mono_linux_x86_64 /opt/godot

# Create alias for easy access
echo 'alias godot="/opt/godot/Godot_v4.4-stable_mono_linux.x86_64"' >> ~/.bashrc
source ~/.bashrc

# Verify (opens Godot editor)
godot --version
```

---

## 5. Clone the Repository

```bash
cd ~
git clone https://github.com/cyphercr0w/argentum-nextgen.git
cd argentum-nextgen
```

---

## 6. Start the Server

```bash
cd ~/argentum-nextgen

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

## 7. Build & Run the Client

### Build (required after every code change)

```bash
cd ~/argentum-nextgen/client
dotnet build
# Should show: "Build succeeded. 0 Warning(s) 0 Error(s)"
```

> **Important**: You must run `dotnet build` every time you pull new changes or modify C# code. Without this step, Godot will run the **old** compiled code and your changes won't take effect. This applies to both editor and command-line execution.

### Quick workflow after `git pull`

```bash
cd ~/argentum-nextgen/client
git pull
dotnet build && godot --path .
```

---

## 8. Run the Client

### Option A: From command line (recommended)

```bash
cd ~/argentum-nextgen/client
dotnet build          # compile C# changes
godot --path .        # run the game
```

### Option B: From Godot editor

```bash
godot --path ~/argentum-nextgen/client/  # opens editor
```

Then press **F5** to run. The editor auto-compiles C# on build (Ctrl+Shift+B) or when you press Play.

Or manually:
1. Run `godot` in terminal
2. Click **Import** → navigate to `~/argentum-nextgen/client/` → select `project.godot`
3. Click **Import & Edit**
4. Press **Ctrl+Shift+B** to build the C# solution
5. Press **F5** to run

### Option C: Export as standalone binary

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
| Changes not taking effect | Run `dotnet build` in `client/` before launching. Godot runs pre-compiled assemblies |
| `dotnet build` fails | Check .NET 8.0 SDK is installed: `dotnet --list-sdks`. Godot 4.4 requires .NET 8 |
