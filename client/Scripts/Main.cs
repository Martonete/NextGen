using Godot;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using TierrasSagradasAO.Data;
using TierrasSagradasAO.Game;
using TierrasSagradasAO.Network;
using TierrasSagradasAO.Rendering;

namespace TierrasSagradasAO;

public partial class Main : Control
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

    // Login UI nodes
    private PanelContainer? _loginPanel;
    private PanelContainer? _charSelectPanel;
    private LineEdit? _accountInput;
    private LineEdit? _passwordInput;
    private Button? _connectButton;
    private Label? _statusLabel;
    private ItemList? _charList;
    private Button? _enterButton;
    private Label? _noticeLabel;

    // Game UI nodes
    private Control? _gameUI;
    private TextureRect? _backgroundImage;
    private RichTextLabel? _console;
    private LineEdit? _chatInput;
    private ProgressBar? _hpBar;
    private ProgressBar? _manaBar;
    private ProgressBar? _staBar;
    private ProgressBar? _aguaBar;
    private ProgressBar? _hamBar;
    private ProgressBar? _expBar;
    private Label? _goldLabel;
    private Label? _levelLabel;
    private Label? _nameLabel;
    private Label? _mapLabel;
    private Label? _onlineLabel;
    private Label? _coordsLabel;
    private GridContainer? _inventoryGrid;

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
        _state.TextMessages = _gameData.TextMessages;

        if (!_gameData.IsLoaded)
        {
            GD.PrintErr("[MAIN] Failed to load game data — aborting");
            return;
        }

        // Setup renderer inside the SubViewport
        _worldRenderer = new WorldRenderer();
        _worldRenderer.Init(_state, _gameData, _animator);
        var gameWorldNode = GetNode<Node2D>("GameViewportContainer/GameViewport/GameWorld");
        gameWorldNode.AddChild(_worldRenderer);

        // Setup packet handler
        _packetHandler = new PacketHandler(_state);

        // Grab Login UI nodes
        _loginPanel = GetNode<PanelContainer>("UILayer/LoginPanel");
        _charSelectPanel = GetNode<PanelContainer>("UILayer/CharSelectPanel");
        _accountInput = GetNode<LineEdit>("UILayer/LoginPanel/VBox/AccountInput");
        _passwordInput = GetNode<LineEdit>("UILayer/LoginPanel/VBox/PasswordInput");
        _connectButton = GetNode<Button>("UILayer/LoginPanel/VBox/ConnectButton");
        _statusLabel = GetNode<Label>("UILayer/LoginPanel/VBox/StatusLabel");
        _charList = GetNode<ItemList>("UILayer/CharSelectPanel/VBox/CharList");
        _enterButton = GetNode<Button>("UILayer/CharSelectPanel/VBox/EnterButton");
        _noticeLabel = GetNode<Label>("UILayer/CharSelectPanel/VBox/NoticeLabel");

        // Grab Game UI nodes
        _gameUI = GetNode<Control>("GameUI");
        _backgroundImage = GetNode<TextureRect>("GameUI/BackgroundImage");
        _console = GetNode<RichTextLabel>("GameUI/Console");
        _chatInput = GetNode<LineEdit>("GameUI/ChatInput");
        _chatInput.Visible = false; // VB6: chat input hidden by default, shown on Enter
        // Remove all background/border styling — VB6 chat input is borderless
        var emptyBox = new StyleBoxEmpty();
        _chatInput.AddThemeStyleboxOverride("normal", emptyBox);
        _chatInput.AddThemeStyleboxOverride("focus", emptyBox);
        _chatInput.AddThemeStyleboxOverride("read_only", emptyBox);
        _hpBar = GetNode<ProgressBar>("GameUI/HPBar");
        _manaBar = GetNode<ProgressBar>("GameUI/ManaBar");
        _staBar = GetNode<ProgressBar>("GameUI/StaBar");
        _aguaBar = GetNode<ProgressBar>("GameUI/AguaBar");
        _hamBar = GetNode<ProgressBar>("GameUI/HamBar");
        _expBar = GetNode<ProgressBar>("GameUI/ExpBar");
        _goldLabel = GetNode<Label>("GameUI/GoldLabel");
        _levelLabel = GetNode<Label>("GameUI/LevelLabel");
        _nameLabel = GetNode<Label>("GameUI/NameLabel");
        _mapLabel = GetNode<Label>("GameUI/MapLabel");
        _onlineLabel = GetNode<Label>("GameUI/OnlineLabel");
        _coordsLabel = GetNode<Label>("GameUI/CoordsLabel");
        _inventoryGrid = GetNode<GridContainer>("GameUI/InventoryGrid");

        // Load Principal.jpg background
        LoadBackgroundImage(dataPath);

        // Wire signals
        _connectButton.Pressed += OnConnectPressed;
        _enterButton.Pressed += OnEnterPressed;
        _chatInput.TextSubmitted += OnChatSubmitted;

        // Show login screen
        _loginPanel.Visible = true;
        _charSelectPanel.Visible = false;
        _gameUI.Visible = false;
    }

    private void LoadBackgroundImage(string dataPath)
    {
        string principalPath = System.IO.Path.Combine(dataPath, "..", "Data", "GRAFICOS", "Principal", "Principal.jpg");
        // Try several paths
        string[] candidates = new[]
        {
            System.IO.Path.Combine(dataPath, "GRAFICOS", "Principal", "Principal.jpg"),
            System.IO.Path.Combine(dataPath, "..", "..", "Cliente", "Data", "GRAFICOS", "Principal", "Principal.jpg"),
            // Absolute fallback
            "/workspace/Tierras-Sagradas-AO/Cliente/Data/GRAFICOS/Principal/Principal.jpg",
        };

        foreach (string path in candidates)
        {
            if (System.IO.File.Exists(path))
            {
                try
                {
                    var image = new Image();
                    image.Load(path);
                    var tex = ImageTexture.CreateFromImage(image);
                    _backgroundImage!.Texture = tex;
                    GD.Print($"[MAIN] Loaded Principal.jpg from {path}");
                    return;
                }
                catch (Exception ex)
                {
                    GD.PrintErr($"[MAIN] Failed to load Principal.jpg: {ex.Message}");
                }
            }
        }

        GD.Print("[MAIN] Principal.jpg not found — using dark background");
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

    /// <summary>
    /// VB6 chat behavior:
    /// - Enter with text → send message, clear input, hide input
    /// - Enter with empty → clear any leftover text, hide input (double-Enter clears)
    /// Text is NOT added to console here — the server echoes it back as a T| packet.
    /// </summary>
    private void OnChatSubmitted(string text)
    {
        if (!string.IsNullOrWhiteSpace(text) && _tcp != null)
        {
            _tcp.SendPacket($";{text}");
        }
        // Always hide + clear on submit (VB6: Enter closes the input)
        _chatInput!.Text = "";
        _chatInput.Visible = false;
        _chatInput.ReleaseFocus();
        _state.ChatActive = false;
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

        if (_state.CurrentScreen == Screen.Game)
        {
            // VB6 order: CheckKeys BEFORE ShowNextFrame.
            // Input runs first so there's a 1-frame gap between scroll completion
            // and the next move — matching VB6's timer tick behavior.
            _inputHandler?.Process(delta);
            UpdateGameUI();
            UpdateConsoleMessages();
        }

        // Movement update AFTER input (VB6: ShowNextFrame after CheckKeys)
        UpdateMovement((float)delta);
    }

    private void HandleScreenChange(Screen newScreen)
    {
        GD.Print($"[MAIN] Screen → {newScreen}");

        switch (newScreen)
        {
            case Screen.Login:
                _loginPanel!.Visible = true;
                _charSelectPanel!.Visible = false;
                _gameUI!.Visible = false;
                break;

            case Screen.CharSelect:
                _loginPanel!.Visible = false;
                _charSelectPanel!.Visible = true;
                _gameUI!.Visible = false;
                PopulateCharList();
                break;

            case Screen.Game:
                _loginPanel!.Visible = false;
                _charSelectPanel!.Visible = false;
                _gameUI!.Visible = true;
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

    /// <summary>
    /// Update stats bars, labels, and inventory from GameState.
    /// </summary>
    private void UpdateGameUI()
    {
        if (_hpBar == null) return;

        // Stats bars
        _hpBar.MaxValue = _state.MaxHp > 0 ? _state.MaxHp : 1;
        _hpBar.Value = _state.MinHp;

        _manaBar!.MaxValue = _state.MaxMana > 0 ? _state.MaxMana : 1;
        _manaBar.Value = _state.MinMana;

        _staBar!.MaxValue = _state.MaxSta > 0 ? _state.MaxSta : 1;
        _staBar.Value = _state.MinSta;

        _aguaBar!.MaxValue = _state.MaxAgua > 0 ? _state.MaxAgua : 1;
        _aguaBar.Value = _state.MinAgua;

        _hamBar!.MaxValue = _state.MaxHam > 0 ? _state.MaxHam : 1;
        _hamBar.Value = _state.MinHam;

        _expBar!.MaxValue = _state.ExpNext > 0 ? _state.ExpNext : 1;
        _expBar.Value = _state.Exp;

        // Labels
        _goldLabel!.Text = $"Oro: {_state.Gold}";
        _levelLabel!.Text = $"Lvl: {_state.Level}";
        _nameLabel!.Text = _state.UserName;
        _mapLabel!.Text = $"Mapa: {_state.CurrentMap} {_state.MapName}";
        _onlineLabel!.Text = $"Online: {_state.OnlineCount}";
        _coordsLabel!.Text = $"Pos: {_state.UserPosX},{_state.UserPosY}";
    }

    /// <summary>
    /// Drain chat message queue and append to console RichTextLabel.
    /// </summary>
    private void UpdateConsoleMessages()
    {
        if (_console == null) return;

        while (_state.ChatMessages.Count > 0)
        {
            var msg = _state.ChatMessages.Dequeue();
            _console.AppendText($"[color=#{msg.Color}]{msg.Text}[/color]\n");
        }
    }

    public override void _Input(InputEvent @event)
    {
        if (_state.CurrentScreen != Screen.Game) return;

        // VB6: Enter toggles chat input visibility
        if (@event is InputEventKey key && key.Pressed && !key.Echo
            && key.Keycode == Key.Enter
            && _chatInput != null && !_chatInput.Visible)
        {
            _chatInput.Visible = true;
            _chatInput.Text = "";
            _chatInput.GrabFocus();
            _state.ChatActive = true;
            GetViewport().SetInputAsHandled();
            return;
        }

        // Mouse clicks on the game viewport area
        if (@event is InputEventMouseButton mb && mb.Pressed)
        {
            // Translate click position relative to the game viewport (0,124) with 534x408 size
            float clickX = mb.Position.X;
            float clickY = mb.Position.Y - 124;

            // Only handle clicks within the game viewport area
            if (clickX >= 0 && clickX < 534 && clickY >= 0 && clickY < 408)
            {
                var viewPos = new Vector2(clickX, clickY);
                if (mb.ButtonIndex == MouseButton.Left)
                    _inputHandler?.HandleLeftClick(viewPos, _state.UserPosX, _state.UserPosY);
                else if (mb.ButtonIndex == MouseButton.Right)
                    _inputHandler?.HandleRightClick(viewPos, _state.UserPosX, _state.UserPosY);
            }
        }
    }

    /// <summary>
    /// VB6-accurate movement interpolation.
    /// timerTicksPerFrame = deltaMs * EngineBaseSpeed (0.0172)
    /// scrollPixels = ScrollPixelsPerFrame (8) * timerTicksPerFrame
    /// Full tile (32px) ≈ 14 frames ≈ 233ms at 60fps.
    ///
    /// Delta is capped to prevent lag spikes from completing scrolls in one frame,
    /// which would let the client send moves faster than intended.
    /// VB6 timer was fixed ~17ms; we allow up to 50ms (3 frames) for flexibility.
    /// </summary>
    private void UpdateMovement(float delta)
    {
        // Cap delta to prevent lag-spike acceleration (VB6 timer was ~17ms fixed)
        float deltaMs = Math.Min(delta * 1000f, 50f);
        float ticksPerFrame = deltaMs * EngineBaseSpeed;
        float scrollPixels = ScrollPixelsPerFrame * ticksPerFrame;

        // Camera scroll (VB6 ShowNextFrame → OffsetCounterX/Y)
        if (_state.UserMoving)
        {
            _state.ScreenOffsetX += ScrollPixelsPerFrame * _state.AddToUserPosX * ticksPerFrame;
            _state.ScreenOffsetY += ScrollPixelsPerFrame * _state.AddToUserPosY * ticksPerFrame;

            // Complete when offset reaches a full tile (32px)
            bool doneX = _state.AddToUserPosX == 0 || Math.Abs(_state.ScreenOffsetX) >= 32f;
            bool doneY = _state.AddToUserPosY == 0 || Math.Abs(_state.ScreenOffsetY) >= 32f;

            if (doneX && doneY)
            {
                _state.ScreenOffsetX = 0;
                _state.ScreenOffsetY = 0;
                _state.AddToUserPosX = 0;
                _state.AddToUserPosY = 0;
                _state.UserMoving = false;

                // Scroll completed — assume server accepted the move
                if (_state.PendingMoves > 0)
                    _state.PendingMoves--;

                // Sync: force self char's MoveOffset to complete too (avoid 1-frame glitch)
                if (_state.Characters.TryGetValue(_state.UserCharIndex, out var selfCh))
                {
                    selfCh.MoveOffsetX = 0;
                    selfCh.MoveOffsetY = 0;
                    selfCh.Moving = false;
                    selfCh.ScrollDirectionX = 0;
                    selfCh.ScrollDirectionY = 0;
                }
            }
        }

        // Character sprite interpolation + per-character walk animation
        foreach (var kvp in _state.Characters)
        {
            var ch = kvp.Value;

            // Advance walk animation frame only while Moving (VB6: per-char FrameCounter)
            if (ch.Moving && ch.Body > 0 && ch.Body < _gameData.Bodies.Length)
            {
                int heading = ch.Heading;
                if (heading < 1 || heading > 4) heading = 3;
                int walkGrh = _gameData.Bodies[ch.Body].Walk[heading];
                if (walkGrh > 0 && walkGrh < _gameData.Grhs.Length)
                {
                    var grh = _gameData.Grhs[walkGrh];
                    if (grh.NumFrames > 1)
                    {
                        float speed = grh.Speed > 0 ? grh.Speed : 100f;
                        ch.WalkFrame += (deltaMs * grh.NumFrames / speed) * 0.7f;
                        if (ch.WalkFrame >= grh.NumFrames)
                            ch.WalkFrame %= grh.NumFrames;
                    }
                }
            }

            if (!ch.Moving && ch.MoveOffsetX == 0 && ch.MoveOffsetY == 0)
                continue;

            // Interpolate X using ScrollDirection
            if (ch.MoveOffsetX != 0)
            {
                ch.MoveOffsetX += scrollPixels * ch.ScrollDirectionX;
                // Complete when offset crosses zero (moved past destination)
                if ((ch.ScrollDirectionX > 0 && ch.MoveOffsetX >= 0) ||
                    (ch.ScrollDirectionX < 0 && ch.MoveOffsetX <= 0) ||
                    ch.ScrollDirectionX == 0)
                {
                    ch.MoveOffsetX = 0;
                }
            }

            // Interpolate Y using ScrollDirection
            if (ch.MoveOffsetY != 0)
            {
                ch.MoveOffsetY += scrollPixels * ch.ScrollDirectionY;
                if ((ch.ScrollDirectionY > 0 && ch.MoveOffsetY >= 0) ||
                    (ch.ScrollDirectionY < 0 && ch.MoveOffsetY <= 0) ||
                    ch.ScrollDirectionY == 0)
                {
                    ch.MoveOffsetY = 0;
                }
            }

            if (ch.MoveOffsetX == 0 && ch.MoveOffsetY == 0)
            {
                ch.Moving = false;
                ch.ScrollDirectionX = 0;
                ch.ScrollDirectionY = 0;
            }
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
            _animator.Clear(); // Resets global clock — all tile anims restart from frame 0

            GD.Print($"[MAIN] Map {_state.CurrentMap} loaded OK");
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[MAIN] Failed to load map {_state.CurrentMap}: {ex.Message}");
        }
    }

    // VB6 movement constants
    private const float EngineBaseSpeed = 0.0172f;   // VB6 timerTicksPerFrame = deltaMs * 0.0172
    private const float ScrollPixelsPerFrame = 8f;   // VB6 ScrollPixelsPerFrameX/Y

    public override void _ExitTree()
    {
        _tcp?.Dispose();
    }
}
