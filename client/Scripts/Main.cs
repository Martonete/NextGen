using Godot;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using TierrasSagradasAO.Data;
using TierrasSagradasAO.Game;
using TierrasSagradasAO.Network;
using TierrasSagradasAO.Rendering;
using TierrasSagradasAO.UI;

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
    private Label? _goldLabel;
    private Label? _levelLabel;
    private Label? _nameLabel;
    private Label? _mapLabel;
    private Label? _onlineLabel;
    private Label? _coordsLabel;
    private Label? _expLabel;

    // Custom stat bar overlay (draws colored fill rects at VB6 positions)
    private StatBarOverlay? _statBarOverlay;

    // InvEqu panel background (CentroNuevoInventario / CentronuevoHechizos)
    private TextureRect? _invEquImage;
    private ImageTexture? _invEquInvTexture;
    private ImageTexture? _invEquSpellTexture;

    // Inventory & Spells UI (VB6-accurate positions)
    private InventoryPanel? _inventoryPanel;
    private SpellPanel? _spellPanel;
    private Button? _invTabButton;
    private Button? _spellTabButton;
    private Label? _itemNameLabel;
    private Button? _dydToggle;
    private Button? _lanzarButton;
    private Button? _infoButton;
    private Button? _spellUpButton;
    private Button? _spellDownButton;
    private bool _showingSpells;

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
        var gameWorldNode = GetNode<Node2D>("GameUI/GameViewportContainer/GameViewport/GameWorld");
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
        // Dark background + thin border — sits below the game viewport, no overlap
        var chatBox = new StyleBoxFlat();
        chatBox.BgColor = new Color(0, 0, 0, 0.85f);
        chatBox.BorderColor = new Color(0.5f, 0.5f, 0.5f, 1f);
        chatBox.SetBorderWidthAll(1);
        chatBox.SetContentMarginAll(2);
        _chatInput.AddThemeStyleboxOverride("normal", chatBox);
        _chatInput.AddThemeStyleboxOverride("focus", chatBox);
        _chatInput.AddThemeStyleboxOverride("read_only", chatBox);
        _goldLabel = GetNode<Label>("GameUI/GoldLabel");
        _levelLabel = GetNode<Label>("GameUI/LevelLabel");
        _nameLabel = GetNode<Label>("GameUI/NameLabel");
        _mapLabel = GetNode<Label>("GameUI/MapLabel");
        _onlineLabel = GetNode<Label>("GameUI/OnlineLabel");
        _coordsLabel = GetNode<Label>("GameUI/CoordsLabel");
        _expLabel = GetNode<Label>("GameUI/ExpLabel");

        // Custom stat bar overlay — draws colored fill rects at VB6 positions
        _statBarOverlay = new StatBarOverlay();
        _statBarOverlay.Position = Vector2.Zero;
        _statBarOverlay.Size = new Vector2(800, 600);
        _statBarOverlay.MouseFilter = Control.MouseFilterEnum.Ignore;
        _gameUI.AddChild(_statBarOverlay);

        // === Inventory & Spells UI (VB6-exact pixel positions, twips÷15) ===

        // InvEqu panel background — VB6: InvEqu at (535,123,264,246)
        _invEquImage = new TextureRect();
        _invEquImage.Position = new Vector2(535, 123);
        _invEquImage.Size = new Vector2(264, 246);
        _invEquImage.StretchMode = TextureRect.StretchModeEnum.Scale;
        _invEquImage.MouseFilter = Control.MouseFilterEnum.Ignore;
        _gameUI.AddChild(_invEquImage);

        // Tab buttons — invisible flat (VB6 visuals are in Principal.jpg)
        _invTabButton = CreateInvisibleButton(536, 120, 131, 29);
        _gameUI.AddChild(_invTabButton);
        _invTabButton.Pressed += OnInventoryTabPressed;

        _spellTabButton = CreateInvisibleButton(672, 120, 125, 30);
        _gameUI.AddChild(_spellTabButton);
        _spellTabButton.Pressed += OnSpellTabPressed;

        // Inventory panel — VB6: picInv at (580,155,174,174)
        _inventoryPanel = new InventoryPanel();
        _inventoryPanel.Position = new Vector2(580, 155);
        _inventoryPanel.Size = new Vector2(174, 174);
        _inventoryPanel.MouseFilter = Control.MouseFilterEnum.Stop;
        _gameUI.AddChild(_inventoryPanel);

        // Item name tooltip — VB6: ItemName at (584,337,161,25), cyan text, centered
        _itemNameLabel = new Label();
        _itemNameLabel.Position = new Vector2(584, 337);
        _itemNameLabel.Size = new Vector2(161, 25);
        _itemNameLabel.HorizontalAlignment = HorizontalAlignment.Center;
        _itemNameLabel.AddThemeColorOverride("font_color", new Color(0, 1, 1)); // VB6 &H0000FFFF& = cyan
        _itemNameLabel.AddThemeFontSizeOverride("font_size", 9);
        _gameUI.AddChild(_itemNameLabel);
        _inventoryPanel.TooltipLabel = _itemNameLabel;

        // DyD toggle — VB6: DyD at (541,338,21,21) — invisible, visual is in background
        _dydToggle = CreateInvisibleButton(541, 338, 21, 21);
        _dydToggle.Pressed += () => {
            _inventoryPanel!.DyDEnabled = !_inventoryPanel.DyDEnabled;
        };
        _gameUI.AddChild(_dydToggle);

        // Spell panel — VB6: hlst at (585,165,164,159), initially hidden
        _spellPanel = new SpellPanel();
        _spellPanel.Position = new Vector2(585, 165);
        _spellPanel.Size = new Vector2(164, 159);
        _spellPanel.MouseFilter = Control.MouseFilterEnum.Stop;
        _spellPanel.Visible = false;
        _gameUI.AddChild(_spellPanel);

        // LANZAR button — VB6: CmdLanzar at (536,327,142,40) — invisible, visual in background
        _lanzarButton = CreateInvisibleButton(536, 327, 142, 40);
        _lanzarButton.Visible = false;
        _gameUI.AddChild(_lanzarButton);
        _lanzarButton.Pressed += OnLanzarPressed;

        // INFO button — VB6: cmdInfo at (720,336,57,27) — invisible
        _infoButton = CreateInvisibleButton(720, 336, 57, 27);
        _infoButton.Visible = false;
        _gameUI.AddChild(_infoButton);
        _infoButton.Pressed += () => _spellPanel.InfoSelected();

        // Spell move arrows — VB6: cmdMoverHechi[0] up at (766,222,15,25), [1] down at (766,247,15,25)
        _spellUpButton = CreateInvisibleButton(766, 222, 15, 25);
        _spellUpButton.Visible = false;
        _gameUI.AddChild(_spellUpButton);
        _spellUpButton.Pressed += () => _spellPanel.MoveSpell(1);

        _spellDownButton = CreateInvisibleButton(766, 247, 15, 25);
        _spellDownButton.Visible = false;
        _gameUI.AddChild(_spellDownButton);
        _spellDownButton.Pressed += () => _spellPanel.MoveSpell(2);

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

    /// <summary>
    /// Create a fully invisible button (flat, no text, empty styleboxes).
    /// VB6 visuals are baked into Principal.jpg — this is just a hit-detect area.
    /// </summary>
    private static Button CreateInvisibleButton(float x, float y, float w, float h)
    {
        var btn = new Button();
        btn.Position = new Vector2(x, y);
        btn.Size = new Vector2(w, h);
        btn.Flat = true;
        btn.Text = "";
        var empty = new StyleBoxEmpty();
        btn.AddThemeStyleboxOverride("normal", empty);
        btn.AddThemeStyleboxOverride("hover", empty);
        btn.AddThemeStyleboxOverride("pressed", empty);
        btn.AddThemeStyleboxOverride("focus", empty);
        return btn;
    }

    private void OnInventoryTabPressed()
    {
        _showingSpells = false;
        _inventoryPanel!.Visible = true;
        _itemNameLabel!.Visible = true;
        _dydToggle!.Visible = true;
        _spellPanel!.Visible = false;
        _lanzarButton!.Visible = false;
        _infoButton!.Visible = false;
        _spellUpButton!.Visible = false;
        _spellDownButton!.Visible = false;
        // Swap InvEqu background to inventory
        if (_invEquImage != null && _invEquInvTexture != null)
            _invEquImage.Texture = _invEquInvTexture;
    }

    private void OnSpellTabPressed()
    {
        _showingSpells = true;
        _inventoryPanel!.Visible = false;
        _itemNameLabel!.Visible = false;
        _dydToggle!.Visible = false;
        _spellPanel!.Visible = true;
        _lanzarButton!.Visible = true;
        _infoButton!.Visible = true;
        _spellUpButton!.Visible = true;
        _spellDownButton!.Visible = true;
        // Swap InvEqu background to spells
        if (_invEquImage != null && _invEquSpellTexture != null)
            _invEquImage.Texture = _invEquSpellTexture;
    }

    /// <summary>
    /// VB6 CmdLanzar_Click: sends LH, sets UsingSkill, changes cursor to crosshair,
    /// shows targeting message in console.
    /// </summary>
    private void OnLanzarPressed()
    {
        _spellPanel!.CastSelected();
        if (_state.UsingSkill > 0)
        {
            // VB6: frmMain.MousePointer = 2 (crosshair cursor)
            Input.SetDefaultCursorShape(Input.CursorShape.Cross);
            // VB6: AddtoRichTextBox MENSAJE_TRABAJO_MAGIA
            _state.ChatMessages.Enqueue(new ChatMessage
            {
                Text = "Haz click en el objetivo del hechizo.",
                Color = "6464B4" // VB6: 100,100,120 → RGB hex
            });
        }
    }

    private void LoadBackgroundImage(string dataPath)
    {
        // Try several paths for the Principal directory.
        // dataPath = res://Data (inside client project). Principal.jpg lives in the
        // VB6 client tree: <repo>/Cliente/Data/GRAFICOS/Principal/
        // From dataPath (server-rust/client/Data) we go ../../.. to reach repo root.
        string repoRoot = System.IO.Path.GetFullPath(System.IO.Path.Combine(dataPath, "..", "..", ".."));
        string[] principalDirs = new[]
        {
            System.IO.Path.Combine(dataPath, "GRAFICOS", "Principal"),
            System.IO.Path.Combine(repoRoot, "Cliente", "Data", "GRAFICOS", "Principal"),
            System.IO.Path.Combine(dataPath, "..", "..", "Cliente", "Data", "GRAFICOS", "Principal"),
            // Windows absolute fallback — user's known project path
            @"C:\Users\F\Desktop\Projects\Tierras-Sagradas-AO\Cliente\Data\GRAFICOS\Principal",
        };
        GD.Print($"[MAIN] Looking for Principal.jpg, repoRoot={repoRoot}");

        string? principalDir = null;
        foreach (string dir in principalDirs)
        {
            string candidate = System.IO.Path.Combine(dir, "Principal.jpg");
            string fullCandidate = System.IO.Path.GetFullPath(candidate);
            GD.Print($"[MAIN] Trying: {fullCandidate} exists={System.IO.File.Exists(fullCandidate)}");
            if (System.IO.File.Exists(fullCandidate))
            {
                principalDir = System.IO.Path.GetDirectoryName(fullCandidate)!;
                break;
            }
        }

        if (principalDir == null)
        {
            GD.Print("[MAIN] Principal.jpg not found — using dark background");
            return;
        }

        // Load Principal.jpg
        try
        {
            var image = new Image();
            image.Load(System.IO.Path.Combine(principalDir, "Principal.jpg"));
            _backgroundImage!.Texture = ImageTexture.CreateFromImage(image);
            GD.Print($"[MAIN] Loaded Principal.jpg from {principalDir}");
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[MAIN] Failed to load Principal.jpg: {ex.Message}");
        }

        // Load InvEqu textures (inventory / spells panel backgrounds)
        _invEquInvTexture = LoadJpgTexture(System.IO.Path.Combine(principalDir, "CentroNuevoInventario.jpg"));
        _invEquSpellTexture = LoadJpgTexture(System.IO.Path.Combine(principalDir, "CentronuevoHechizos.jpg"));

        // Default to inventory background
        if (_invEquImage != null && _invEquInvTexture != null)
            _invEquImage.Texture = _invEquInvTexture;
    }

    private static ImageTexture? LoadJpgTexture(string path)
    {
        if (!System.IO.File.Exists(path)) return null;
        try
        {
            var img = new Image();
            img.Load(path);
            return ImageTexture.CreateFromImage(img);
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[MAIN] Failed to load {path}: {ex.Message}");
            return null;
        }
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
                // Initialize inventory/spell panels with TCP (only available after connect)
                if (_tcp != null)
                {
                    _inventoryPanel!.Init(_state, _gameData, _tcp);
                    _spellPanel!.Init(_state, _gameData, _tcp);
                }
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
        if (_statBarOverlay == null) return;

        // Push stat values to the overlay — it draws colored fill rects
        _statBarOverlay.SetStats(
            _state.MinHp, _state.MaxHp,
            _state.MinMana, _state.MaxMana,
            _state.MinSta, _state.MaxSta,
            _state.MinAgua, _state.MaxAgua,
            _state.MinHam, _state.MaxHam,
            _state.Exp, _state.ExpNext
        );

        _expLabel!.Text = $"Exp: {_state.Exp}/{_state.ExpNext}";
        _goldLabel!.Text = $"{_state.Gold}";
        _levelLabel!.Text = $"{_state.Level}";
        _nameLabel!.Text = _state.UserName;
        _mapLabel!.Text = $"Mapa: {_state.CurrentMap} {_state.MapName}";
        _onlineLabel!.Text = $"{_state.OnlineCount}";
        _coordsLabel!.Text = $"({_state.CurrentMap},{_state.UserPosX},{_state.UserPosY})";
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
            // VB6 console uses bold font (Weight=700 in RecTxt)
            _console.AppendText($"[b][color=#{msg.Color}]{msg.Text}[/color][/b]\n");
        }
    }

    public override void _Input(InputEvent @event)
    {
        if (_state.CurrentScreen != Screen.Game) return;

        // VB6: Enter opens chat input, preserving previous text.
        // Double-Enter (open → submit empty) clears the previous message.
        if (@event is InputEventKey key && key.Pressed && !key.Echo
            && (key.Keycode == Key.Enter || key.Keycode == Key.KpEnter)
            && _chatInput != null && !_chatInput.Visible)
        {
            _chatInput.Visible = true;
            // Don't clear — VB6 preserves previous text so user can re-send or edit.
            // Submitting empty (second Enter) clears it via OnChatSubmitted.
            _chatInput.GrabFocus();
            _chatInput.SelectAll();
            _state.ChatActive = true;
            GetViewport().SetInputAsHandled();
            return;
        }

        // Mouse clicks on the game viewport area
        // VB6: renderer_Click fires on mouse RELEASE, not press.
        // Double-click sends RC (VB6: Form_DblClick).
        // Single right-click sends LC + RC (VB6: Form_Click with DobleClick=1).
        if (@event is InputEventMouseButton mb && !mb.Pressed)
        {
            // Translate click position relative to the game viewport (0,124) with 534x408 size
            float clickX = mb.Position.X;
            float clickY = mb.Position.Y - 124;

            // Only handle clicks within the game viewport area
            if (clickX >= 0 && clickX < 534 && clickY >= 0 && clickY < 408)
            {
                var viewPos = new Vector2(clickX, clickY);
                if (mb.ButtonIndex == MouseButton.Left)
                {
                    if (mb.ShiftPressed && _state.Privileges >= 2)
                    {
                        // VB6 GM: Shift+Click → /TELEP YO mapa,x,y
                        _inputHandler?.HandleGmTeleport(viewPos, _state.UserPosX, _state.UserPosY, _state.CurrentMap);
                    }
                    else if (mb.DoubleClick)
                    {
                        // VB6: double-click fires Form_Click (LC) THEN Form_DblClick (RC)
                        // Both events fire sequentially — Godot merges into one event.
                        _inputHandler?.HandleLeftClick(viewPos, _state.UserPosX, _state.UserPosY);
                        _inputHandler?.HandleRightClick(viewPos, _state.UserPosX, _state.UserPosY);
                    }
                    else if (_state.UsingSkill > 0)
                    {
                        // VB6: Form_Click when UsingSkill > 0 → WLC (spell targeting)
                        _inputHandler?.HandleSpellClick(viewPos, _state.UserPosX, _state.UserPosY);
                        // VB6: after targeting, reset cursor to arrow
                        Input.SetDefaultCursorShape(Input.CursorShape.Arrow);
                    }
                    else
                    {
                        // VB6 renderer_Click left → LC (inspect tile)
                        _inputHandler?.HandleLeftClick(viewPos, _state.UserPosX, _state.UserPosY);
                    }
                }
                else if (mb.ButtonIndex == MouseButton.Right)
                {
                    // VB6: right-click sends BOTH LC + RC (DobleClick=1 path)
                    _inputHandler?.HandleLeftClick(viewPos, _state.UserPosX, _state.UserPosY);
                    _inputHandler?.HandleRightClick(viewPos, _state.UserPosX, _state.UserPosY);
                }
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
