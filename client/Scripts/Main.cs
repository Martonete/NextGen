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
    private const string ServerHost = "127.0.0.1";
    private const int ServerPort = 5028;

    private readonly GameData _gameData = new();
    private readonly GameState _state = new();
    private AoTcpClient? _tcp;
    private PacketHandler? _packetHandler;
    private InputHandler? _inputHandler;
    private WorldRenderer? _worldRenderer;
    private GrhAnimator _animator = new();

    private bool _connecting;
    private int _packetCount;

    // UI nodes
    private PanelContainer? _loginPanel;
    private PanelContainer? _charSelectPanel;
    private LineEdit? _accountInput;
    private LineEdit? _passwordInput;
    private Button? _connectButton;
    private Label? _statusLabel;
    private ItemList? _charList;
    private Button? _enterButton;
    private Label? _noticeLabel;

    // Track screen transitions
    private Screen _lastScreen = Screen.Login;

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

        // Grab UI nodes
        _loginPanel = GetNode<PanelContainer>("UI/LoginPanel");
        _charSelectPanel = GetNode<PanelContainer>("UI/CharSelectPanel");
        _accountInput = GetNode<LineEdit>("UI/LoginPanel/VBox/AccountInput");
        _passwordInput = GetNode<LineEdit>("UI/LoginPanel/VBox/PasswordInput");
        _connectButton = GetNode<Button>("UI/LoginPanel/VBox/ConnectButton");
        _statusLabel = GetNode<Label>("UI/LoginPanel/VBox/StatusLabel");
        _charList = GetNode<ItemList>("UI/CharSelectPanel/VBox/CharList");
        _enterButton = GetNode<Button>("UI/CharSelectPanel/VBox/EnterButton");
        _noticeLabel = GetNode<Label>("UI/CharSelectPanel/VBox/NoticeLabel");

        // Wire signals
        _connectButton.Pressed += OnConnectPressed;
        _enterButton.Pressed += OnEnterPressed;

        // Show login screen
        _loginPanel.Visible = true;
        _charSelectPanel.Visible = false;
    }

    private void OnConnectPressed()
    {
        string account = _accountInput!.Text.Trim();
        string password = _passwordInput!.Text.Trim();

        if (string.IsNullOrEmpty(account) || string.IsNullOrEmpty(password))
        {
            _statusLabel!.Text = "Ingrese cuenta y password";
            return;
        }

        _state.AccountName = account;
        _state.LoginError = "";
        _connectButton!.Disabled = true;
        _statusLabel!.Text = "Conectando...";

        _tcp = new AoTcpClient();
        _inputHandler = new InputHandler(_tcp, _state);
        _connecting = true;

        _ = ConnectAndLogin(account, password);
    }

    private async Task ConnectAndLogin(string account, string password)
    {
        try
        {
            GD.Print($"[MAIN] Connecting to {ServerHost}:{ServerPort}...");
            await _tcp!.ConnectAsync(ServerHost, ServerPort);
            _connecting = false;
            GD.Print("[MAIN] Connected! Sending login...");

            _statusLabel!.Text = "Enviando login...";

            await Task.Delay(100);
            _tcp.SendPacket("KERD22");

            await Task.Delay(50);
            _tcp.SendPacket($"ALOGIN{account},{password},0");
            GD.Print("[MAIN] Sent: ALOGIN (comma-separated with version)");
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[MAIN] Connection failed: {ex.Message}");
            _connecting = false;
            _statusLabel!.Text = $"Error: {ex.Message}";
            _connectButton!.Disabled = false;
        }
    }

    private void OnEnterPressed()
    {
        if (_charList!.IsAnythingSelected())
        {
            int[] selected = _charList.GetSelectedItems();
            if (selected.Length > 0 && selected[0] < _state.CharacterList.Count)
            {
                var charPreview = _state.CharacterList[selected[0]];
                string charName = charPreview.Name;
                string account = _state.AccountName;
                string code = _state.SecurityCode;

                _enterButton!.Disabled = true;
                _noticeLabel!.Text = "Entrando al mundo...";

                _tcp!.SendPacket($"OOLOGI{charName},{account},{code}");
                GD.Print($"[MAIN] Sent: OOLOGI{charName},{account},{code}");
            }
        }
        else
        {
            _noticeLabel!.Text = "Seleccione un personaje";
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
            if (_packetCount <= 20)
            {
                string preview = pkt.Length > 80 ? pkt[..80] + "..." : pkt;
                string hex = "";
                for (int i = 0; i < Math.Min(20, pkt.Length); i++)
                    hex += ((int)pkt[i]).ToString("X2") + " ";
                GD.Print($"[PKT #{_packetCount}] len={pkt.Length} text=\"{preview}\" hex=[{hex.Trim()}]");
            }
            _packetHandler.HandlePacket(pkt);
        }

        // React to screen transitions driven by PacketHandler
        if (_state.CurrentScreen != _lastScreen)
        {
            HandleScreenChange(_state.CurrentScreen);
            _lastScreen = _state.CurrentScreen;
        }

        // Show login errors
        if (!string.IsNullOrEmpty(_state.LoginError) && _state.CurrentScreen == Screen.Login)
        {
            _statusLabel!.Text = _state.LoginError;
            _connectButton!.Disabled = false;
            _state.LoginError = "";
        }

        // Handle map loading when requested
        if (_state.NeedMapLoad)
        {
            _state.NeedMapLoad = false;
            LoadCurrentMap();
        }

        // After LOGGED, transition to game
        if (_state.IsLogged && _state.CurrentScreen != Screen.Game)
        {
            _state.CurrentScreen = Screen.Game;
            HandleScreenChange(Screen.Game);
            _lastScreen = Screen.Game;
        }

        // Update animations
        _animator.Update((float)delta, _gameData);

        // Update smooth movement
        UpdateMovement((float)delta);

        if (_state.CurrentScreen == Screen.Game)
            _inputHandler?.Process(delta);
    }

    private void HandleScreenChange(Screen newScreen)
    {
        GD.Print($"[MAIN] Screen → {newScreen}");

        switch (newScreen)
        {
            case Screen.Login:
                _loginPanel!.Visible = true;
                _charSelectPanel!.Visible = false;
                break;

            case Screen.CharSelect:
                _loginPanel!.Visible = false;
                _charSelectPanel!.Visible = true;
                PopulateCharList();
                break;

            case Screen.Game:
                _loginPanel!.Visible = false;
                _charSelectPanel!.Visible = false;
                GD.Print("[MAIN] Entered game world");
                break;
        }
    }

    private void PopulateCharList()
    {
        _charList!.Clear();
        foreach (var ch in _state.CharacterList)
        {
            string label = $"{ch.Name} — Lvl {ch.Level} ({ch.Class})";
            if (ch.Dead) label += " [MUERTO]";
            _charList.AddItem(label);
        }
        if (!string.IsNullOrEmpty(_state.ServerNotice))
            _noticeLabel!.Text = _state.ServerNotice;
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event is InputEventMouseButton mb && mb.Pressed && mb.ButtonIndex == MouseButton.Left)
        {
            if (_state.CurrentScreen == Screen.Game)
                _inputHandler?.HandleClick(mb.Position, _state.UserPosX, _state.UserPosY);
        }
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

    private const float ScrollSpeed = 6f;

    public override void _ExitTree()
    {
        _tcp?.Dispose();
    }
}
