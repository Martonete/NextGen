using Godot;
using System;
using System.Collections.Generic;
using ArgentumNextgen.Data;
using ArgentumNextgen.Game;
using ArgentumNextgen.Network;
using ArgentumNextgen.Rendering;
using ArgentumNextgen.UI;

namespace ArgentumNextgen;

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
    private ParticleSystem _particleSystem = new();
    private LightSystem _lightSystem = new();
    private SoundManager? _soundManager;

    private bool _connecting;
    private int _packetCount;

    // Login controller (extracted to LoginController.cs)
    private UI.LoginController? _loginController;

    // Login UI nodes (still referenced by Main for screen transitions)
    private PanelContainer? _loginPanel;
    private PanelContainer? _charSelectPanel;
    private ItemList? _charList;
    private Button? _enterButton;
    private Label? _noticeLabel;

    // Chat system (extracted to ChatSystem.cs)
    private ChatSystem? _chatSystem;

    // Inventory/spell tab UI (extracted to InventoryUI.cs)
    private UI.InventoryUI? _inventoryUI;

    // Game HUD updater (extracted to GameUIUpdater.cs)
    private GameUIUpdater? _gameUIUpdater;

    // Panel state synchronizer (extracted to PanelStateSync.cs)
    private PanelStateSync? _panelSync;

    // Input router (extracted to InputRouter.cs)
    private InputRouter? _inputRouter;

    // Game UI nodes
    private Control? _gameUI;
    private TextureRect? _backgroundImage;
    private Label? _goldLabel;
    private Label? _levelLabel;
    private Label? _nameLabel;
    private Label? _onlineLabel;
    private Label? _coordsLabel;
    private Label? _expLabel;
    private Control? _btnCastiGM;

    // Bottom bar combat/attribute labels (VB6: lblArmor, lblHelm, lblShielder, lblWeapon)
    private Label? _armorLabel;    // Body armor def
    private Label? _helmLabel;     // Helmet def
    private Label? _shieldLabel;   // Shield def
    private Label? _weaponLabel;   // Weapon hit
    private Label? _fuerzaLabel;
    private Label? _agilidadLabel;
    private Label? _repLabel;
    private Label? _fpsLabel;
    private Label? _macroStatusLabel; // Shows "MACRO" when work/spell macro is active

    private string _dataPath = ""; // cached for macro file I/O

    // Custom stat bar overlay (draws colored fill rects at VB6 positions)
    private StatBarOverlay? _statBarOverlay;

    // InvEqu panel background (CentroInventario / CentroHechizos)
    private TextureRect? _invEquImage;
    private ImageTexture? _invEquInvTexture;
    private ImageTexture? _invEquSpellTexture;

    // Inventory & Spells UI (VB6-accurate positions)
    private InventoryPanel? _inventoryPanel;
    private SpellPanel? _spellPanel;
    private Button? _invTabButton;
    private Button? _spellTabButton;
    private Label? _itemNameLabel;
    private TextureButton? _dydToggle;
    private Texture2D? _dydOffTex;
    private Texture2D? _dydOnTex;
    private Button? _lanzarButton;
    private Button? _infoButton;
    private Button? _spellUpButton;
    private Button? _spellDownButton;
    // Commerce panel (frmComerciar)
    private CommercePanel? _commercePanel;

    // Trade panel (frmComerciarUsu — player-to-player)
    private TradePanel? _tradePanel;

    // Bank panels (frmBanco + frmNuevoBancoObj)
    private BankPanel? _bankPanel;
    private VaultPanel? _vaultPanel;
    private GuildBankPanel? _guildBankPanel;
    private CraftPanel? _craftPanel;

    // Guild panels
    private GuildPanel? _guildPanel;
    private GuildFoundationPanel? _guildFoundationPanel;

    // Forum panel (frmForo)
    private ForumPanel? _forumPanel;

    // Friend list panel
    private FriendListPanel? _friendListPanel;

    // Mail panel
    private MailPanel? _mailPanel;

    // Party panel (frmGrupo)
    private PartyPanel? _partyPanel;

    // Travel panel (frmViajar)
    private TravelPanel? _travelPanel;

    // Death panel (frmMuertito)
    private DeathPanel? _deathPanel;

    // Macro panel (frmMakro)
    private MacroPanel? _macroPanel;

    // Stats panel (frmEstadisticas)
    private StatsPanel? _statsPanel;

    // Options panel (frmOpcionesNew)
    private OptionsPanel? _optionsPanel;

    // Key binding panel (frmTeclas)
    private KeyBindPanel? _keyBindPanel;

    // Quest panel (frmQuest)
    private QuestPanel? _questPanel;

    // Trainer/Pet panel (frmEntrenador)
    private TrainerPanel? _trainerPanel;

    // NPC dialog panel (shows NPC speech in fixed bottom-center panel)
    private NpcDialogPanel? _npcDialogPanel;

    // Change password panel
    private ChangePasswordPanel? _changePasswordPanel;

    // Character info popup (shows /MIRAR results centered on screen)
    private CharInfoPopup? _charInfoPopup;

    // Minimap panel (shows player/NPC dots on 100x100 tile grid)
    private MinimapPanel? _minimapPanel;

    // Blind screen overlay (VB6: frmMain goes black when blinded)
    private ColorRect? _blindOverlay;

    // Tooltip panel (floating item/spell info on hover)
    private TooltipPanel? _tooltipPanel;

    // Context menu (right-click on characters in game world)
    private ContextMenu? _contextMenu;

    // GM Panel (frmPanelGM)
    private GmPanel? _gmPanel;

    // Spawn List (frmSpawnList)
    private SpawnListPanel? _spawnListPanel;

    // SOS/Help panel (GM help requests)
    private SosPanel? _sosPanel;

    // MOTD Editor (guild message of the day)
    private MotdEditorPanel? _motdEditorPanel;

    // Guild alignment picker (during foundation)
    private GuildAlignmentPanel? _guildAlignmentPanel;

    // Peace proposal panel (guild diplomacy)
    private PeaceProposalPanel? _peaceProposalPanel;

    // Guild member detail panel
    private GuildMemberPanel? _guildMemberPanel;

    // Day/Night cycle overlay
    private Rendering.DayNightCycle? _dayNightCycle;

    // Loading screen
    private LoadingScreen? _loadingScreen;

    // Tutorial panel
    private TutorialPanel? _tutorialPanel;

    // Dialog manager (extracted to DialogManager.cs)
    private UI.DialogManager? _dialogManager;

    // Account creation screen (extracted to AccountCreateScreen.cs)
    private UI.AccountCreateScreen? _accountCreateScreen;

    // Character creation screen (extracted to CharCreateScreen.cs)
    private UI.CharCreateScreen? _charCreateScreen;

    // Track screen transitions
    private Screen _lastScreen = Screen.Login;

    public override void _Ready()
    {
        GD.Print("=== Argentum Nextgen — Godot 4 Client ===");

        string dataPath;
        if (OS.HasFeature("editor"))
        {
            dataPath = ProjectSettings.GlobalizePath("res://Data");
        }
        else
        {
            string exeDir = System.IO.Path.GetDirectoryName(OS.GetExecutablePath()) ?? ".";
            dataPath = System.IO.Path.Combine(exeDir, "Data");

            // Load patch PCK files (overrides base resources).
            // Drop a "patch.pck" next to the exe for delta updates.
            LoadPatchPacks(exeDir);
        }

        GD.Print($"[MAIN] Data path: {dataPath}");
        _dataPath = dataPath;
        _gameData.LoadAll(dataPath);
        _state.TextMessages = _gameData.TextMessages;

        // Load particle definitions
        string particlesPath = System.IO.Path.Combine(dataPath, "INIT", "Particles.ini");
        _particleSystem.LoadDefinitions(particlesPath, _state);

        // Load user configuration (Options.ao)
        _state.Config = GameConfig.Load(dataPath);
        _state.ShowNames = _state.Config.ShowNames;
        // Apply V-Sync
        DisplayServer.WindowSetVsyncMode(
            _state.Config.VsyncEnabled ? DisplayServer.VSyncMode.Enabled : DisplayServer.VSyncMode.Disabled);
        if (_state.Config.FpsLimit > 0)
            Engine.MaxFps = _state.Config.FpsLimit;

        // Load key bindings (Teclas.ao)
        _state.Keys = KeyBindings.Load(dataPath);

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
        _packetHandler.OnMapLoad = LoadCurrentMap;

        // Setup sound manager
        _soundManager = new SoundManager();
        AddChild(_soundManager);
        _soundManager.Init(dataPath);
        _soundManager.MusicEnabled = _state.Config.MusicEnabled;
        _soundManager.SoundEnabled = _state.Config.SfxEnabled;
        _soundManager.SetMusicVolume(_state.Config.MusicVolume);
        _soundManager.SetSfxVolume(_state.Config.SfxVolume);
        _packetHandler.OnPlaySound = (id) => _soundManager.PlaySound(id);
        _packetHandler.OnPlayMusic = (id) => _soundManager.PlayMusic(id);

        // Wire floating text callback: spawns rising damage/heal numbers above characters
        _packetHandler.OnFloatingText = (charIndex, text, colorHex) =>
        {
            var floatingLayer = _worldRenderer?.FloatingText;
            if (floatingLayer == null) return;
            var color = Color.FromHtml(colorHex);
            floatingLayer.AddText(charIndex, text, color);
        };

        // Initialize weather renderer with sound manager (rain sound)
        _worldRenderer?.InitWeather(_soundManager);

        // Set Linear texture filtering on UI layers so fonts/text scale smoothly.
        // Game viewport keeps Nearest (pixel art) via project default_texture_filter=0.
        // UILayer is a CanvasLayer (not CanvasItem), so set filter on its children.
        foreach (var child in GetNode("UILayer").GetChildren())
            if (child is CanvasItem ci) ci.TextureFilter = CanvasItem.TextureFilterEnum.Linear;
        GetNode<Control>("GameUI").TextureFilter = CanvasItem.TextureFilterEnum.Linear;

        // Grab Login UI nodes
        _loginPanel = GetNode<PanelContainer>("UILayer/LoginPanel");
        _charSelectPanel = GetNode<PanelContainer>("UILayer/CharSelectPanel");
        var _accountInput = GetNode<LineEdit>("UILayer/LoginPanel/VBox/AccountInput");
        var _passwordInput = GetNode<LineEdit>("UILayer/LoginPanel/VBox/PasswordInput");
        var _connectButton = GetNode<Button>("UILayer/LoginPanel/VBox/ConnectButton");
        var _statusLabel = GetNode<Label>("UILayer/LoginPanel/VBox/StatusLabel");
        var _rememberCheck = GetNode<CheckBox>("UILayer/LoginPanel/VBox/RememberCheck");

        // Login controller
        _loginController = new UI.LoginController(_state, _dataPath);
        _loginController.BindNodes(_loginPanel, _accountInput, _passwordInput, _connectButton, _statusLabel, _rememberCheck);
        _loginController.OnLoginRequest = (account, password) =>
        {
            _tcp = new AoTcpClient();
            _packetHandler = new PacketHandler(_state);
            _packetHandler.OnMapLoad = LoadCurrentMap;
            if (_soundManager != null)
            {
                _packetHandler.OnPlaySound = (id) => _soundManager.PlaySound(id);
                _packetHandler.OnPlayMusic = (id) => _soundManager.PlayMusic(id);
            }
            _inputHandler = new InputHandler(_tcp, _state, _state.Keys, GetViewport());
            if (_inputRouter != null) _inputRouter.InputHandler = _inputHandler;
            _inputHandler.OnToggleMusic = () =>
            {
                _state.Config.MusicEnabled = !_state.Config.MusicEnabled;
                if (_soundManager != null)
                {
                    _soundManager.MusicEnabled = _state.Config.MusicEnabled;
                    if (_soundManager.MusicEnabled && _state.MusicId > 0)
                        _soundManager.PlayMusic(_state.MusicId);
                }
                _state.Config.Save(_dataPath);
            };
            _loginController!.Connecting = true;
            _connecting = true;
            _ = ConnectAndLogin(account, password);
        };
        _charList = GetNode<ItemList>("UILayer/CharSelectPanel/VBox/CharList");
        _enterButton = GetNode<Button>("UILayer/CharSelectPanel/VBox/EnterButton");
        _noticeLabel = GetNode<Label>("UILayer/CharSelectPanel/VBox/NoticeLabel");

        // Grab Game UI nodes
        _gameUI = GetNode<Control>("GameUI");
        _backgroundImage = GetNode<TextureRect>("GameUI/BackgroundImage");

        // Chat system
        var console = GetNode<RichTextLabel>("GameUI/Console");
        var consoleStyle = new StyleBoxEmpty();
        consoleStyle.ContentMarginLeft = 2;
        consoleStyle.ContentMarginRight = 2;
        consoleStyle.ContentMarginTop = 1;
        consoleStyle.ContentMarginBottom = 6;
        console.AddThemeStyleboxOverride("normal", consoleStyle);

        var chatInput = GetNode<LineEdit>("GameUI/ChatInput");
        chatInput.Visible = false;
        chatInput.MaxLength = 160;
        var chatBox = new StyleBoxFlat();
        chatBox.BgColor = new Color(0, 0, 0, 0.85f);
        chatBox.BorderColor = new Color(0.5f, 0.5f, 0.5f, 1f);
        chatBox.SetBorderWidthAll(1);
        chatBox.SetContentMarginAll(2);
        chatInput.AddThemeStyleboxOverride("normal", chatBox);
        chatInput.AddThemeStyleboxOverride("focus", chatBox);
        chatInput.AddThemeStyleboxOverride("read_only", chatBox);

        _chatSystem = new ChatSystem(_state);
        _chatSystem.BindNodes(console, chatInput);
        _chatSystem.CreateChatTabs(_gameUI);
        _chatSystem.SendPacket = (pkt) => _tcp?.SendPacket(pkt);
        _chatSystem.OnWorkMacroToggle = () => _inventoryUI?.HandleWorkMacroToggle();
        _chatSystem.OnSpellMacroToggle = () => _inventoryUI?.HandleSpellMacroToggle();
        _chatSystem.OnPasswdCommand = () => _state.ShowChangePassword = true;
        _chatSystem.OnGmPanelToggle = () => _gmPanel?.Toggle();
        _chatSystem.OnSosPanelToggle = () => _sosPanel?.Open();
        _goldLabel = GetNode<Label>("GameUI/GoldLabel");
        _levelLabel = GetNode<Label>("GameUI/LevelLabel");
        _nameLabel = GetNode<Label>("GameUI/NameLabel");
        _onlineLabel = GetNode<Label>("GameUI/OnlineLabel");
        _coordsLabel = GetNode<Label>("GameUI/CoordsLabel");
        _expLabel = GetNode<Label>("GameUI/ExpLabel");

        // VB6 frmMain.frm exact fonts per label:
        // GldLbl: Tahoma 6pt Bold, ForeColor &H0080FFFF& = RGB(255,255,128) light yellow
        ApplyFont(_goldLabel, "Tahoma", 700);
        _goldLabel.AddThemeFontSizeOverride("font_size", 8);
        _goldLabel.AddThemeColorOverride("font_color", new Color(1f, 1f, 0.502f));

        // LvlLbl: Cambria 8.25pt Bold, White
        ApplyFont(_levelLabel, "Cambria", 700);

        // NameLabel: (not in VB6 form as label — keep current Tahoma Bold)
        ApplyFont(_nameLabel, "Tahoma", 700);

        // ONLINES: Tahoma 6pt Bold, White
        ApplyFont(_onlineLabel, "Tahoma", 700);
        _onlineLabel.AddThemeFontSizeOverride("font_size", 7);

        // Coord: Tahoma 5.25pt Bold, White
        ApplyFont(_coordsLabel, "Tahoma", 700);
        _coordsLabel.AddThemeFontSizeOverride("font_size", 9);

        // GM "CASTI GM" button — ColorRect + Label to bypass Button min height.
        _btnCastiGM = new Control();
        _btnCastiGM.Position = new Vector2(560, 581);
        _btnCastiGM.Size = new Vector2(70, 14);
        _btnCastiGM.Visible = false;
        _btnCastiGM.MouseFilter = Control.MouseFilterEnum.Stop;
        var castiBg = new ColorRect();
        castiBg.Color = new Color(0.7f, 0.1f, 0.1f);
        castiBg.MouseFilter = Control.MouseFilterEnum.Ignore;
        castiBg.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        _btnCastiGM.AddChild(castiBg);
        var castiLabel = new Label();
        castiLabel.Text = "CASTI GM";
        castiLabel.HorizontalAlignment = HorizontalAlignment.Center;
        castiLabel.VerticalAlignment = VerticalAlignment.Center;
        castiLabel.MouseFilter = Control.MouseFilterEnum.Ignore;
        castiLabel.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        castiLabel.AddThemeFontSizeOverride("font_size", 7);
        castiLabel.AddThemeColorOverride("font_color", Colors.White);
        castiLabel.ClipContents = true;
        _btnCastiGM.AddChild(castiLabel);
        _btnCastiGM.GuiInput += (InputEvent ev) =>
        {
            if (ev is InputEventMouseButton mb && mb.Pressed && mb.ButtonIndex == MouseButton.Left)
                _tcp?.SendPacket(ClientPackets.WriteTalk("/TELEP YO 104 51 51"));
        };
        GetNode("GameUI").AddChild(_btnCastiGM);

        // exp: Cambria 8.25pt Bold, White (VB6: &H8000000B& system color → white on dark UI)
        ApplyFont(_expLabel, "Cambria", 700);
        _expLabel.AddThemeColorOverride("font_color", Colors.White);

        // Custom stat bar overlay — draws colored fill rects at VB6 positions
        _statBarOverlay = new StatBarOverlay();
        _statBarOverlay.DataPath = dataPath;
        _statBarOverlay.Position = Vector2.Zero;
        _statBarOverlay.Size = new Vector2(800, 600);
        _statBarOverlay.MouseFilter = Control.MouseFilterEnum.Ignore;
        _gameUI.AddChild(_statBarOverlay);

        // Create all game panels, sidebar buttons, HUD labels, and wire subsystems
        SetupGamePanels();

        // Character creation screen (buttons + delete dialog)
        _charCreateScreen = new UI.CharCreateScreen(_state, _gameData);
        _charCreateScreen.SetupCharSelectButtons(_charList!, _noticeLabel!);
        _charCreateScreen.OnCharSelectCreate = () =>
        {
            _state.CurrentScreen = Screen.CharCreate;
            HandleScreenChange(Screen.CharCreate);
            _lastScreen = Screen.CharCreate;
        };
        _charCreateScreen.OnDeleteCharRequest = () =>
        {
            if (!_charList!.IsAnythingSelected())
            {
                _noticeLabel!.Text = "Seleccione un personaje";
                return;
            }
            _charCreateScreen.ShowDeleteConfirm(_charList!, _noticeLabel!);
        };
        _charCreateScreen.OnDeleteCharConfirmed = (_) =>
        {
            int[] selected = _charList!.GetSelectedItems();
            if (selected.Length == 0 || selected[0] >= _state.CharacterList.Count || _tcp == null) return;
            string charName = _state.CharacterList[selected[0]].Name;
            _tcp.SendPacket(ClientPackets.WriteTbrp(charName));
            GD.Print($"[MAIN] Sent: TBRP {charName}");
            _noticeLabel!.Text = "Eliminando personaje...";
        };
        _charCreateScreen.CreateDeleteConfirmDialog(GetNode<CanvasLayer>("UILayer"));

        // "Desconectar" button on CharSelect screen
        var charSelectVBox = _charList!.GetParent();
        var disconnectBtn = new Button();
        disconnectBtn.Text = "Desconectar";
        disconnectBtn.CustomMinimumSize = new Vector2(0, 32);
        disconnectBtn.Pressed += () => HandleDisconnect("");
        charSelectVBox.AddChild(disconnectBtn);

        // "Crear Cuenta" button on Login screen
        var crearCuentaBtn = new Button();
        crearCuentaBtn.Text = "Crear Cuenta";
        crearCuentaBtn.CustomMinimumSize = new Vector2(0, 32);
        crearCuentaBtn.Pressed += OnCrearCuentaPressed;
        var loginVBox = _connectButton.GetParent();
        // Insert after ConnectButton, before StatusLabel
        int connectIdx = _connectButton.GetIndex();
        loginVBox.AddChild(crearCuentaBtn);
        loginVBox.MoveChild(crearCuentaBtn, connectIdx + 1);

        // Account creation screen
        _accountCreateScreen = new UI.AccountCreateScreen(_state);
        _accountCreateScreen.CreatePanel(GetNode("UILayer"));
        _accountCreateScreen.OnCreateAccount = (name, pass, pin) => _ = ConnectAndCreateAccount(name, pass, pin);
        _accountCreateScreen.OnBack = OnAccountCreateBack;

        // Character creation panel
        _charCreateScreen!.CreatePanel(GetNode("UILayer"));
        _charCreateScreen.OnBack = () =>
        {
            _state.CurrentScreen = Screen.CharSelect;
            HandleScreenChange(Screen.CharSelect);
            _lastScreen = Screen.CharSelect;
        };
        _charCreateScreen.OnCreateCharConfirm = () =>
        {
            int head = _state.CreateCharHead;
            string account = _state.AccountName;
            _tcp!.SendPacket(ClientPackets.WriteNlogin(
                _state.CreateCharName, (byte)_state.CreateCharRace, (byte)_state.CreateCharGender,
                (byte)_state.CreateCharClass, (short)head, (byte)_state.CreateCharFaction, account));
            GD.Print("[MAIN] Sent: NLOGIN (binary)");
        };

        // Load Principal.jpg background
        LoadBackgroundImage(dataPath);

        // Wire signals
        _connectButton.Pressed += () => _loginController?.OnConnectPressed();
        _enterButton.Pressed += OnEnterPressed;
        _charList.ItemActivated += OnCharListDoubleClick;
        chatInput.TextSubmitted += (text) => _chatSystem?.OnChatSubmitted(text);
        _accountInput.TextSubmitted += (_) => _loginController?.OnConnectPressed();
        _passwordInput.TextSubmitted += (_) => _loginController?.OnConnectPressed();

        // Show window mode dialog first — login panel hidden until choice is made
        _loginPanel.Visible = false;
        _charSelectPanel.Visible = false;
        _charCreateScreen!.Panel!.Visible = false;
        _accountCreateScreen!.Panel!.Visible = false;
        _gameUI.Visible = false;

        // Load remembered account (XOR-encrypted file)
        _loginController?.LoadRememberedAccount();

        // If config was loaded from file, apply saved display preference and skip dialog
        if (_state.Config.LoadedFromFile)
        {
            GD.Print("[MAIN] Config loaded from file — applying saved display preference, skipping dialog");
            if (_state.Config.Fullscreen)
            {
                GetTree().Root.ContentScaleAspect = Window.ContentScaleAspectEnum.Keep;
                DisplayServer.WindowSetMode(DisplayServer.WindowMode.Fullscreen);
            }
            else
            {
                DisplayServer.WindowSetFlag(DisplayServer.WindowFlags.ResizeDisabled, false);
                DisplayServer.WindowSetSize(new Vector2I(800, 600));
                GetTree().Root.ContentScaleAspect = Window.ContentScaleAspectEnum.Keep;
            }
            if (_loginPanel != null)
                _loginPanel.Visible = true;
            CallDeferred(MethodName.FocusAccountInput);
        }
        else
        {
            // First launch — show window mode dialog
            _dialogManager?.ShowWindowModeDialog();
            CallDeferred(MethodName.CenterWindowModeDialog);
        }
    }

    private void CenterWindowModeDialog()
    {
        _dialogManager?.CenterWindowModeDialog(GetViewportRect().Size);
    }

    private void FocusAccountInput()
    {
        _loginController?.FocusAccountInput();
    }

    // Static helpers delegated to UIHelpers
    private static void ApplyFont(Label label, string fontName = "Tahoma", int weight = 700)
        => UI.UIHelpers.ApplyFont(label, fontName, weight);
    private static Label CreateStatLabel(float x, float y, float w, float h, Color color, int fontSize,
                                          string fontName = "Tahoma", int weight = 700)
        => UI.UIHelpers.CreateStatLabel(x, y, w, h, color, fontSize, fontName, weight);
    private static Button CreateInvisibleButton(float x, float y, float w, float h, bool usePointerCursor = true)
        => UI.UIHelpers.CreateInvisibleButton(x, y, w, h, usePointerCursor);

    private void OnInventoryTabPressed() => _inventoryUI?.OnInventoryTabPressed();
    private void OnSpellTabPressed() => _inventoryUI?.OnSpellTabPressed();
    private void OnLanzarPressed() => _inventoryUI?.OnLanzarPressed();

    private static ImageTexture? LoadJpgTexture(string path)
        => UI.UIHelpers.LoadJpgTexture(path);

    private static string FriendlyConnectionError(Exception ex)
        => UI.UIHelpers.FriendlyConnectionError(ex);

    public override void _Notification(int what)
    {
        if (what == NotificationWMWindowFocusIn && _dialogManager != null && _dialogManager.ShouldRestoreFullscreen())
        {
            _dialogManager.BeginRestoreFullscreen();
            GetTree().CreateTimer(0.3).Timeout += () => _dialogManager.RestoreFullscreen(_state.Config);
        }
    }

    private void ShowEscapeMenu() => _dialogManager?.ShowEscapeMenu(GetViewportRect().Size);
    private void HideEscapeMenu() => _dialogManager?.HideEscapeMenu();

    // Drop dialog methods moved to DialogManager.cs
    private void CloseDropDialog() => _dialogManager?.CloseDropDialog();

    private void OnInventoryDropOutside(int slot, Vector2 globalPos) =>
        _inventoryUI?.OnInventoryDropOutside(slot, globalPos);


    public override void _Process(double delta)
    {
        if (_tcp == null || _packetHandler == null) return;

        // VB6: Socket1_Disconnect — detect lost connection and return to login
        if (!_connecting && !_tcp.IsConnected)
        {
            if (_state.CurrentScreen == Screen.Login || _state.CurrentScreen == Screen.AccountCreate)
            {
                // Server dropped connection during login/account creation — reset UI immediately
                _tcp?.Dispose();
                _tcp = null;
                _packetHandler = null;
                _inputHandler = null;
                if (_loginController?.StatusLabel != null) _loginController.StatusLabel.Text = "Error: El servidor cerró la conexión.";
                if (_loginController?.ConnectButton != null) _loginController.ConnectButton.Disabled = false;
                return;
            }
            HandleDisconnect("Conexión perdida con el servidor.");
            return;
        }

        // Poll and process inbound binary data
        var dataChunks = _tcp.PollPackets();
        foreach (byte[] chunk in dataChunks)
        {
            _packetCount++;
            if (_packetCount <= 20)
            {
                string hex = "";
                for (int i = 0; i < Math.Min(20, chunk.Length); i++)
                    hex += chunk[i].ToString("X2") + " ";
                GD.Print($"[PKT #{_packetCount}] len={chunk.Length} hex=[{hex.Trim()}]");
            }
            _packetHandler.HandleBinaryData(chunk);
        }

        // React to screen transitions driven by PacketHandler
        if (_state.CurrentScreen != _lastScreen)
        {
            HandleScreenChange(_state.CurrentScreen);
            _lastScreen = _state.CurrentScreen;
        }

        // Show login errors
        if (!string.IsNullOrEmpty(_state.LoginError))
        {
            if (_state.CurrentScreen == Screen.Login)
            {
                if (_loginController?.StatusLabel != null) _loginController.StatusLabel.Text = _state.LoginError;
                if (_loginController?.ConnectButton != null) _loginController.ConnectButton.Disabled = false;
            }
            else if (_state.CurrentScreen == Screen.CharCreate)
            {
                if (_charCreateScreen?.ErrorLabel != null) _charCreateScreen.ErrorLabel.Text = _state.LoginError;
                if (_charCreateScreen?.CreateButton != null) _charCreateScreen.CreateButton.Disabled = false;
            }
            else if (_state.CurrentScreen == Screen.CharSelect)
            {
                _noticeLabel!.Text = _state.LoginError;
            }
            else if (_state.CurrentScreen == Screen.AccountCreate)
            {
                string msg = _state.LoginError;
                if (msg.Contains("exito", StringComparison.OrdinalIgnoreCase))
                {
                    _accountCreateScreen?.ShowError(msg, isSuccess: true);
                    if (_accountCreateScreen != null) _accountCreateScreen.SuccessTimer = 2.0;
                }
                else
                {
                    _accountCreateScreen?.ShowError(msg);
                    _accountCreateScreen?.EnableCreateButton();
                }
            }
            _state.LoginError = "";
        }

        // Show Mensaje dialog (VB6: Mensaje form — ERR, ERO, !! packets)
        if (!string.IsNullOrEmpty(_state.MensajeText))
        {
            _dialogManager?.ShowMensaje(_state.MensajeText, GetViewportRect().Size);
            _state.MensajeText = "";
        }

        // Dismiss Mensaje dialog with Enter key
        if (_dialogManager != null && _dialogManager.IsMensajeVisible)
        {
            if (Input.IsActionJustPressed("ui_accept"))
                _dialogManager.OnMensajeAccept();
        }

        // Account creation success → auto-switch to login after timer
        if (_accountCreateScreen != null && _accountCreateScreen.SuccessTimer > 0 && _state.CurrentScreen == Screen.AccountCreate)
        {
            _accountCreateScreen.SuccessTimer -= delta;
            if (_accountCreateScreen.SuccessTimer <= 0)
            {
                _tcp?.Dispose();
                _tcp = null;
                _packetHandler = null;
                _connecting = false;

                _state.CurrentScreen = Screen.Login;
                HandleScreenChange(Screen.Login);
                _lastScreen = Screen.Login;
                if (_loginController?.StatusLabel != null) _loginController.StatusLabel.Text = "Cuenta creada. Ingrese sus datos.";
            }
        }

        // Map loading is now handled immediately in HandleChangeMap via OnMapLoad callback.
        // This ensures BQ/HO packets apply to the correct MapData.

        // After LOGGED, transition to game
        if (_state.IsLogged && _state.CurrentScreen != Screen.Game)
        {
            _state.CurrentScreen = Screen.Game;
            HandleScreenChange(Screen.Game);
            _lastScreen = Screen.Game;
        }

        // Update animations
        _animator.Update((float)delta, _gameData);

        // Update particle simulation
        _particleSystem.Update((float)delta, _state);

        // Update arrow projectiles (move toward target, remove on arrival)
        UpdateArrowProjectiles((float)delta);

        // Recalculate lighting when dirty (lights added/removed/map changed)
        if (_state.LightsDirty)
        {
            _lightSystem.RecalculateLights(_state);
            _state.LightsDirty = false;
            _worldRenderer?.MarkLightmapDirty();
        }

        if (_state.CurrentScreen == Screen.Game)
        {
            // VB6 order: CheckKeys BEFORE ShowNextFrame.
            // Input runs first so there's a 1-frame gap between scroll completion
            // and the next move — matching VB6's timer tick behavior.
            _inputHandler?.Process(delta);

            // Update work/spell macros (auto-repeat timers)
            _state.WorkMacro.Update(delta);
            _state.SpellMacro.Update(delta);

            UpdateGameUI();
            _chatSystem?.UpdateConsoleMessages();

            // Sync panel visibility with state flags
            _panelSync?.Update((float)delta);
        }

        // Movement update AFTER input (VB6: ShowNextFrame after CheckKeys)
        UpdateMovement((float)delta);
    }

    /// <summary>
    /// Update stats bars, labels, and inventory from GameState.
    /// </summary>
    private void UpdateGameUI() => _gameUIUpdater?.UpdateGameUI();
    private void UpdateArrowProjectiles(float delta) => _gameUIUpdater?.UpdateArrowProjectiles(delta);


    public override void _Input(InputEvent @event)
    {
        // Escape on login -> quit game
        if (@event is InputEventKey escKey && escKey.Pressed && !escKey.Echo
            && escKey.Keycode == Key.Escape
            && _state.CurrentScreen == Screen.Login)
        {
            GetTree().Quit();
            return;
        }

        // Escape on char select -> disconnect back to login
        if (@event is InputEventKey escCharKey && escCharKey.Pressed && !escCharKey.Echo
            && escCharKey.Keycode == Key.Escape
            && _state.CurrentScreen == Screen.CharSelect)
        {
            HandleDisconnect("");
            return;
        }

        // Enter on login screen -> trigger connect button
        if (@event is InputEventKey enterLoginKey && enterLoginKey.Pressed && !enterLoginKey.Echo
            && (enterLoginKey.Keycode == Key.Enter || enterLoginKey.Keycode == Key.KpEnter)
            && _state.CurrentScreen == Screen.Login
            && _loginController?.ConnectButton != null && !_loginController.ConnectButton.Disabled)
        {
            _loginController.ConnectButton.EmitSignal("pressed");
            return;
        }

        // Enter on char select -> connect with selected character
        if (@event is InputEventKey enterCharKey && enterCharKey.Pressed && !enterCharKey.Echo
            && (enterCharKey.Keycode == Key.Enter || enterCharKey.Keycode == Key.KpEnter)
            && _state.CurrentScreen == Screen.CharSelect)
        {
            OnEnterPressed();
            return;
        }

        if (_state.CurrentScreen != Screen.Game) return;

        // Delegate game-screen input to InputRouter
        _inputRouter?.HandleGameInput(@event, GetViewport(), GetTree());
    }

    public override void _ExitTree()
    {
        _tcp?.Dispose();
    }
}
