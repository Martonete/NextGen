using Godot;
using System;
using System.Threading.Tasks;
using TierrasSagradasAO.Data;
using TierrasSagradasAO.Game;
using TierrasSagradasAO.Network;
using TierrasSagradasAO.Rendering;

namespace TierrasSagradasAO;

public partial class Main : Node2D
{
    // === HARDCODED FOR TESTING — replace with UI later ===
    private const string ServerHost = "127.0.0.1";
    private const int ServerPort = 5028;
    private const string TestAccount = "test";
    private const string TestPassword = "test";
    private const string TestCharName = "TestChar";
    // =====================================================

    private readonly GameData _gameData = new();
    private readonly GameState _state = new();
    private AoTcpClient? _tcp;
    private PacketHandler? _packetHandler;
    private InputHandler? _inputHandler;
    private WorldRenderer? _worldRenderer;
    private GrhAnimator _animator = new();

    private bool _connecting;
    private bool _loginSent;
    private bool _charSelectSent;
    private bool _offlineMode;
    private double _loginTimeout;
    private int _packetCount;

    public override void _Ready()
    {
        GD.Print("=== Tierras Sagradas AO — Godot 4 Client ===");

        string dataPath;
        if (OS.HasFeature("editor"))
            dataPath = ProjectSettings.GlobalizePath("res://Data");
        else
            dataPath = System.IO.Path.Combine(
                System.IO.Path.GetDirectoryName(OS.GetExecutablePath()) ?? ".",
                "Data"
            );

        GD.Print($"[MAIN] Data path: {dataPath}");

        _gameData.LoadAll(dataPath);

        if (!_gameData.IsLoaded)
        {
            GD.PrintErr("[MAIN] Failed to load game data — aborting");
            return;
        }

        // Setup renderer
        _worldRenderer = new WorldRenderer();
        _worldRenderer.Init(_state, _gameData, _animator);
        var gameWorldNode = GetNode<Node2D>("GameWorld");
        gameWorldNode.AddChild(_worldRenderer);

        // Setup packet handler
        _packetHandler = new PacketHandler(_state);

        // Start TCP connection
        _tcp = new AoTcpClient();
        _inputHandler = new InputHandler(_tcp, _state);
        _connecting = true;
        _loginTimeout = 5.0; // 5 second timeout for login

        _ = ConnectAndLogin();
    }

    private async Task ConnectAndLogin()
    {
        try
        {
            GD.Print($"[MAIN] Connecting to {ServerHost}:{ServerPort}...");
            await _tcp!.ConnectAsync(ServerHost, ServerPort);
            _connecting = false;
            GD.Print("[MAIN] Connected! Sending login...");

            await Task.Delay(100);

            _tcp.SendPacket("KERD22");
            GD.Print("[MAIN] Sent: KERD22");

            await Task.Delay(50);
            _tcp.SendPacket($"ALOGIN{TestAccount}\n{TestPassword}");
            _loginSent = true;
            GD.Print("[MAIN] Sent: ALOGIN (login packet)");
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[MAIN] Connection failed: {ex.Message}");
            _connecting = false;
            EnterOfflineMode();
        }
    }

    public override void _Process(double delta)
    {
        if (_tcp == null || _packetHandler == null) return;

        // Poll and process inbound packets
        var packets = _tcp.PollPackets();
        foreach (string pkt in packets)
        {
            _packetCount++;
            // Log first 20 packets for debugging
            if (_packetCount <= 20)
            {
                string preview = pkt.Length > 80 ? pkt[..80] + "..." : pkt;
                // Show hex of first 20 bytes for debugging encoding issues
                string hex = "";
                for (int i = 0; i < Math.Min(20, pkt.Length); i++)
                    hex += ((int)pkt[i]).ToString("X2") + " ";
                GD.Print($"[PKT #{_packetCount}] len={pkt.Length} text=\"{preview}\" hex=[{hex.Trim()}]");
            }
            _packetHandler.HandlePacket(pkt);
        }

        // Handle map loading when requested
        if (_state.NeedMapLoad)
        {
            _state.NeedMapLoad = false;
            LoadCurrentMap();
        }

        // After LOGGED, send character select (hardcoded)
        if (_state.IsLogged && !_charSelectSent)
        {
            _charSelectSent = true;
            GD.Print("[MAIN] LOGGED received! Sending character select...");
            _tcp.SendPacket($"OOLOGI{TestCharName}");
            _tcp.SendPacket("THCJXD");
        }

        // Login timeout — if no LOGGED after 5 seconds, go offline
        if (_loginSent && !_state.IsLogged && !_offlineMode)
        {
            _loginTimeout -= delta;
            if (_loginTimeout <= 0)
            {
                GD.PrintErr($"[MAIN] Login timeout! Received {_packetCount} packets but no LOGGED. Entering offline mode.");
                EnterOfflineMode();
            }
        }

        // Update animations
        _animator.Update((float)delta, _gameData);

        // Update smooth movement
        UpdateMovement((float)delta);

        if (_offlineMode)
        {
            ProcessOfflineInput();
            return;
        }

        _inputHandler?.Process(delta);
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event is InputEventMouseButton mb && mb.Pressed && mb.ButtonIndex == MouseButton.Left)
        {
            _inputHandler?.HandleClick(mb.Position, _state.UserPosX, _state.UserPosY);
        }
    }

    private void EnterOfflineMode()
    {
        _offlineMode = true;
        GD.Print("[MAIN] === OFFLINE MODE ===");
        GD.Print("[MAIN] Loading Mapa1 at (50,50). Use WASD to move camera.");

        _state.CurrentMap = 1;
        _state.NeedMapLoad = true;
        _state.UserPosX = 50;
        _state.UserPosY = 50;

        var dummy = new Character
        {
            Body = 1,
            Head = 1,
            Heading = 3,
            PosX = 50,
            PosY = 50,
            Name = "Offline Preview"
        };
        _state.UserCharIndex = 1;
        _state.Characters[1] = dummy;
    }

    private void UpdateMovement(float delta)
    {
        float scrollPixels = ScrollSpeed * delta * 60f;

        foreach (var kvp in _state.Characters)
        {
            var ch = kvp.Value;
            if (!ch.Moving && ch.MoveOffsetX == 0 && ch.MoveOffsetY == 0)
                continue;

            if (ch.MoveOffsetX > 0)
            {
                ch.MoveOffsetX -= scrollPixels;
                if (ch.MoveOffsetX < 0) ch.MoveOffsetX = 0;
            }
            else if (ch.MoveOffsetX < 0)
            {
                ch.MoveOffsetX += scrollPixels;
                if (ch.MoveOffsetX > 0) ch.MoveOffsetX = 0;
            }

            if (ch.MoveOffsetY > 0)
            {
                ch.MoveOffsetY -= scrollPixels;
                if (ch.MoveOffsetY < 0) ch.MoveOffsetY = 0;
            }
            else if (ch.MoveOffsetY < 0)
            {
                ch.MoveOffsetY += scrollPixels;
                if (ch.MoveOffsetY > 0) ch.MoveOffsetY = 0;
            }

            if (ch.MoveOffsetX == 0 && ch.MoveOffsetY == 0)
                ch.Moving = false;
        }
    }

    private void LoadCurrentMap()
    {
        string mapDir;
        if (OS.HasFeature("editor"))
            mapDir = ProjectSettings.GlobalizePath("res://Data/Maps");
        else
            mapDir = System.IO.Path.Combine(
                System.IO.Path.GetDirectoryName(OS.GetExecutablePath()) ?? ".",
                "Data", "Maps"
            );

        try
        {
            _state.MapData = MapLoader.Load(mapDir, _state.CurrentMap);
            _animator.Clear();
            GD.Print($"[MAIN] Map {_state.CurrentMap} loaded OK");
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[MAIN] Failed to load map {_state.CurrentMap}: {ex.Message}");
        }
    }

    private double _offlineMoveTimer;

    private void ProcessOfflineInput()
    {
        _offlineMoveTimer -= GetProcessDeltaTime();
        if (_offlineMoveTimer > 0) return;

        int dx = 0, dy = 0;
        if (Input.IsKeyPressed(Key.W) || Input.IsKeyPressed(Key.Up)) dy = -1;
        if (Input.IsKeyPressed(Key.S) || Input.IsKeyPressed(Key.Down)) dy = 1;
        if (Input.IsKeyPressed(Key.A) || Input.IsKeyPressed(Key.Left)) dx = -1;
        if (Input.IsKeyPressed(Key.D) || Input.IsKeyPressed(Key.Right)) dx = 1;

        if (dx == 0 && dy == 0) return;

        int newX = _state.UserPosX + dx;
        int newY = _state.UserPosY + dy;
        if (newX < 1 || newX > 100 || newY < 1 || newY > 100) return;

        _state.UserPosX = newX;
        _state.UserPosY = newY;
        if (_state.Characters.TryGetValue(_state.UserCharIndex, out var ch))
        {
            ch.PosX = newX;
            ch.PosY = newY;
        }
        _offlineMoveTimer = 0.15;
    }

    private const float ScrollSpeed = 6f;

    public override void _ExitTree()
    {
        _tcp?.Dispose();
    }
}
