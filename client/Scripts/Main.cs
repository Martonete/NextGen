using Godot;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
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

    // Login UI nodes
    private PanelContainer? _loginPanel;
    private PanelContainer? _charSelectPanel;
    private LineEdit? _accountInput;
    private LineEdit? _passwordInput;
    private Button? _connectButton;
    private Label? _statusLabel;
    private CheckBox? _rememberCheck;
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
    private bool _showingSpells;

    // Commerce panel (frmComerciar)
    private CommercePanel? _commercePanel;
    private bool _lastComerciando;

    // Trade panel (frmComerciarUsu — player-to-player)
    private TradePanel? _tradePanel;
    private bool _lastTrading;

    // Bank panels (frmBanco + frmNuevoBancoObj)
    private BankPanel? _bankPanel;
    private VaultPanel? _vaultPanel;
    private GuildBankPanel? _guildBankPanel;
    private CraftPanel? _craftPanel;
    private bool _lastBanqueando;

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

    // Character info popup (shows /MIRAR results centered on screen)
    private CharInfoPopup? _charInfoPopup;

    // Minimap panel (shows player/NPC dots on 100x100 tile grid)
    private MinimapPanel? _minimapPanel;

    // Blind screen overlay (VB6: frmMain goes black when blinded)
    private ColorRect? _blindOverlay;
    private float _blindAlpha;

    // Tooltip panel (floating item/spell info on hover)
    private TooltipPanel? _tooltipPanel;

    // Context menu (right-click on characters in game world)
    private ContextMenu? _contextMenu;

    // Window mode startup dialog
    private PanelContainer? _windowModeDialog;

    // Saved window mode before minimize (to restore fullscreen properly)
    private DisplayServer.WindowMode _preMiniMode;
    private bool _restoringFullscreen;

    // Escape menu (in-game)
    private PanelContainer? _escapeMenu;

    // Message dialog (VB6: Mensaje form — modal error/info dialog)
    private PanelContainer? _mensajeDialog;
    private Label? _mensajeLabel;

    // Chat tab filter buttons (above console)
    private HBoxContainer? _chatTabBar;
    private Button[] _chatTabButtons = new Button[7]; // All + 6 ChatType values

    // Drop quantity dialog (VB6: frmCantidad)
    private PanelContainer? _dropDialog;
    private Label? _dropDialogLabel;
    private LineEdit? _dropDialogInput;
    private Button? _dropDialogOk;
    private Button? _dropDialogAll;
    private Button? _dropDialogCancel;


    // Account creation panel
    private PanelContainer? _accountCreatePanel;
    private LineEdit? _acctNameInput;
    private LineEdit? _acctPasswordInput;
    private LineEdit? _acctPasswordConfirmInput;
    private LineEdit? _acctPinInput;
    private LineEdit? _acctPinConfirmInput;
    private Label? _acctErrorLabel;
    private Button? _acctCreateButton;
    private Button? _acctBackButton;
    private double _acctSuccessTimer;

    // Character creation panel
    private PanelContainer? _charCreatePanel;
    private LineEdit? _charCreateNameInput;
    private Button[]? _raceButtons;
    private Button[]? _genderButtons;
    private Button[]? _classButtons;
    private Button[]? _factionButtons;
    private Label? _charCreateHeadLabel;
    private Node2D? _charCreateHeadPreview;
    private Label? _charCreateError;
    private Button? _charCreateCreateBtn;
    private Button? _charSelectCreateBtn; // "Crear Personaje" on CharSelect screen
    private Button? _charSelectDeleteBtn; // "Borrar Personaje" on CharSelect screen

    // Delete character confirmation dialog
    private PanelContainer? _deleteConfirmDialog;
    private Label? _deleteConfirmLabel;
    private LineEdit? _deleteConfirmInput;
    private int _deleteConfirmCode;

    // Track screen transitions
    private Screen _lastScreen = Screen.Login;
    // Track double-click to avoid sending LC on the release after a dbl-click
    private bool _dblClickHandled;

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
        _accountInput = GetNode<LineEdit>("UILayer/LoginPanel/VBox/AccountInput");
        _passwordInput = GetNode<LineEdit>("UILayer/LoginPanel/VBox/PasswordInput");
        _connectButton = GetNode<Button>("UILayer/LoginPanel/VBox/ConnectButton");
        _statusLabel = GetNode<Label>("UILayer/LoginPanel/VBox/StatusLabel");
        _rememberCheck = GetNode<CheckBox>("UILayer/LoginPanel/VBox/RememberCheck");
        _charList = GetNode<ItemList>("UILayer/CharSelectPanel/VBox/CharList");
        _enterButton = GetNode<Button>("UILayer/CharSelectPanel/VBox/EnterButton");
        _noticeLabel = GetNode<Label>("UILayer/CharSelectPanel/VBox/NoticeLabel");

        // Grab Game UI nodes
        _gameUI = GetNode<Control>("GameUI");
        _backgroundImage = GetNode<TextureRect>("GameUI/BackgroundImage");
        _console = GetNode<RichTextLabel>("GameUI/Console");
        // Add bottom padding so text doesn't touch the edge
        var consoleStyle = new StyleBoxEmpty();
        consoleStyle.ContentMarginLeft = 2;
        consoleStyle.ContentMarginRight = 2;
        consoleStyle.ContentMarginTop = 1;
        consoleStyle.ContentMarginBottom = 6;
        _console.AddThemeStyleboxOverride("normal", consoleStyle);

        // Chat tab filter bar — sits above the console (Y 4-22)
        _chatTabBar = new HBoxContainer();
        _chatTabBar.Position = new Vector2(10, 4);
        _chatTabBar.Size = new Vector2(547, 18);
        _chatTabBar.AddThemeConstantOverride("separation", 2);
        _gameUI.AddChild(_chatTabBar);
        string[] tabLabels = { "Todo", "Global", "Party", "Clan", "Whisper", "Combat", "Sistema" };
        int[] tabFilters = { -1, (int)ChatType.Global, (int)ChatType.Party, (int)ChatType.Clan, (int)ChatType.Whisper, (int)ChatType.Combat, (int)ChatType.System };
        for (int i = 0; i < tabLabels.Length; i++)
        {
            int filterVal = tabFilters[i];
            var tabBtn = new Button();
            tabBtn.Text = tabLabels[i];
            tabBtn.CustomMinimumSize = new Vector2(0, 16);
            tabBtn.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            tabBtn.AddThemeFontSizeOverride("font_size", 8);
            tabBtn.Pressed += () => SetChatFilter(filterVal);
            _chatTabBar.AddChild(tabBtn);
            _chatTabButtons[i] = tabBtn;
        }
        UpdateChatTabHighlight();

        _chatInput = GetNode<LineEdit>("GameUI/ChatInput");
        _chatInput.Visible = false; // VB6: chat input hidden by default, shown on Enter
        _chatInput.MaxLength = 160; // VB6: frmMain.frm txtChat MaxLength=160
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

        // === Inventory & Spells UI (VB6-exact pixel positions, twips÷15) ===

        // InvEqu panel background — VB6: InvEqu at (581,125) 198×282 pixels
        _invEquImage = new TextureRect();
        _invEquImage.Position = new Vector2(581, 125);
        _invEquImage.Size = new Vector2(198, 282);
        _invEquImage.StretchMode = TextureRect.StretchModeEnum.Scale;
        _invEquImage.MouseFilter = Control.MouseFilterEnum.Ignore;
        _gameUI.AddChild(_invEquImage);

        // Tab buttons — VB6 13.3: Label4 "Inventario" (592,128,93,29), Label7 "Hechizos" (688,128,75,30)
        _invTabButton = CreateInvisibleButton(592, 128, 93, 29);
        _gameUI.AddChild(_invTabButton);
        _invTabButton.Pressed += () => { _soundManager?.PlaySound(SoundManager.SND_CLICK); OnInventoryTabPressed(); };

        _spellTabButton = CreateInvisibleButton(688, 128, 75, 30);
        _gameUI.AddChild(_spellTabButton);
        _spellTabButton.Pressed += () => { _soundManager?.PlaySound(SoundManager.SND_CLICK); OnSpellTabPressed(); };

        // Inventory panel — VB6: picInv at (600,160) 160×128 pixels
        _inventoryPanel = new InventoryPanel();
        _inventoryPanel.Position = new Vector2(600, 160);
        _inventoryPanel.Size = new Vector2(160, 128);
        _inventoryPanel.MouseFilter = Control.MouseFilterEnum.Stop;
        _inventoryPanel.FocusMode = Control.FocusModeEnum.None;
        _gameUI.AddChild(_inventoryPanel);

        // Item name tooltip — VB6: ItemName at (584,337,161,25)
        // Palatino Linotype 6.75pt Normal (400), ForeColor &H0000FFFF& = RGB(255,255,0) yellow
        _itemNameLabel = new Label();
        _itemNameLabel.Position = new Vector2(584, 339);
        _itemNameLabel.Size = new Vector2(161, 25);
        _itemNameLabel.HorizontalAlignment = HorizontalAlignment.Center;
        _itemNameLabel.AddThemeColorOverride("font_color", new Color(1f, 1f, 0f)); // Yellow
        _itemNameLabel.AddThemeFontSizeOverride("font_size", 11);
        ApplyFont(_itemNameLabel, "Palatino Linotype", 400); // Normal weight
        _itemNameLabel.AutowrapMode = TextServer.AutowrapMode.Word;
        _gameUI.AddChild(_itemNameLabel);
        _inventoryPanel.TooltipLabel = _itemNameLabel;
        // RichTooltip is set after _tooltipPanel creation (below)

        // DyD toggle — VB6: DyD at (541,338,21,21) — image toggles between on/off
        // Load via Image.Load() (filesystem), not ResourceLoader (requires Godot import)
        _dydOffTex = LoadJpgTexture(System.IO.Path.Combine(_dataPath, "Graficos", "DyD_off.jpg"));
        _dydOnTex = LoadJpgTexture(System.IO.Path.Combine(_dataPath, "Graficos", "DyD_on.jpg"));
        _dydToggle = new TextureButton();
        _dydToggle.Position = new Vector2(541, 338);
        _dydToggle.Size = new Vector2(21, 21);
        _dydToggle.StretchMode = TextureButton.StretchModeEnum.Scale;
        _dydToggle.TextureNormal = _dydOffTex;
        _dydToggle.Pressed += () => {
            _soundManager?.PlaySound(SoundManager.SND_CLICK);
            _inventoryPanel!.DyDEnabled = !_inventoryPanel.DyDEnabled;
            _dydToggle.TextureNormal = _inventoryPanel.DyDEnabled ? _dydOnTex : _dydOffTex;
        };
        _gameUI.AddChild(_dydToggle);

        // Spell panel — VB6: hlst at (585,165,164,159), initially hidden
        _spellPanel = new SpellPanel();
        _spellPanel.Position = new Vector2(585, 165);
        _spellPanel.Size = new Vector2(164, 159);
        _spellPanel.MouseFilter = Control.MouseFilterEnum.Stop;
        _spellPanel.FocusMode = Control.FocusModeEnum.None;
        _spellPanel.Visible = false;
        _gameUI.AddChild(_spellPanel);

        // LANZAR button — VB6 13.3: CmdLanzar at (584, 352, 77, 25)
        _lanzarButton = CreateInvisibleButton(584, 352, 77, 25);
        _lanzarButton.Visible = false;
        _gameUI.AddChild(_lanzarButton);
        _lanzarButton.Pressed += OnLanzarPressed;

        // INFO button — VB6 13.3: cmdInfo at (712, 352, 57, 27)
        _infoButton = CreateInvisibleButton(712, 352, 57, 27);
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

        // === Bottom bar labels — VB6 13.3 exact positions ===
        // lblArmor(78,576,57,17), lblHelm(196,576,57,17), lblShielder(342,576,57,17), lblWeapon(464,576,57,17)
        _armorLabel = CreateStatLabel(78, 576, 57, 17, Colors.White, 8);
        _gameUI.AddChild(_armorLabel);

        _helmLabel = CreateStatLabel(196, 576, 57, 17, Colors.White, 8);
        _gameUI.AddChild(_helmLabel);

        _shieldLabel = CreateStatLabel(342, 576, 57, 17, Colors.White, 8);
        _gameUI.AddChild(_shieldLabel);

        _weaponLabel = CreateStatLabel(464, 576, 57, 17, Colors.White, 8);
        _gameUI.AddChild(_weaponLabel);

        // Fuerza: VB6 13.3 lblStrg at (648, 415, 14, 14), green
        _fuerzaLabel = CreateStatLabel(648, 415, 14, 14, new Color(0, 1, 0), 9);
        _gameUI.AddChild(_fuerzaLabel);

        // Agilidad: VB6 13.3 lblDext at (608, 415, 14, 14), yellow
        _agilidadLabel = CreateStatLabel(608, 415, 14, 14, new Color(1f, 1f, 0f), 9);
        _gameUI.AddChild(_agilidadLabel);

        // Reputation: VB6 Cambria 8.25 Normal (Weight=400, NOT bold), White
        _repLabel = CreateStatLabel(616, 52, 32, 12, Colors.White, 10, "Cambria", 400);
        _gameUI.AddChild(_repLabel);

        // FPS: VB6 13.3 lblFPS at (444, 4, 37, 12), Tahoma Bold
        _fpsLabel = CreateStatLabel(444, 4, 37, 12, Colors.White, 7);
        _gameUI.AddChild(_fpsLabel);

        // Macro status indicator — small green label next to FPS
        _macroStatusLabel = new Label();
        _macroStatusLabel.Position = new Vector2(484, 4);
        _macroStatusLabel.Size = new Vector2(60, 12);
        _macroStatusLabel.AddThemeColorOverride("font_color", new Color(0.3f, 1f, 0.3f));
        _macroStatusLabel.AddThemeFontSizeOverride("font_size", 7);
        ApplyFont(_macroStatusLabel, "Tahoma", 700);
        _macroStatusLabel.Visible = false;
        _macroStatusLabel.MouseFilter = Control.MouseFilterEnum.Ignore;
        _gameUI.AddChild(_macroStatusLabel);


        // VB6 13.3 sidebar buttons (no minimap)
        // imgMapa(682,445,93,20), imgGrupo(681,466,94,21), imgOpciones(681,485,95,22),
        // imgEstadisticas(681,507,95,24), imgClanes(683,532,92,26)
        var mapaButton = CreateInvisibleButton(682, 445, 93, 20);
        _gameUI.AddChild(mapaButton);
        mapaButton.Pressed += () => { _soundManager?.PlaySound(SoundManager.SND_CLICK); _minimapPanel?.Toggle(); };

        var grupoButton = CreateInvisibleButton(681, 466, 94, 21);
        _gameUI.AddChild(grupoButton);
        grupoButton.Pressed += () => { _soundManager?.PlaySound(SoundManager.SND_CLICK); _partyPanel?.TogglePanel(); };

        var opcionesButton = CreateInvisibleButton(681, 485, 95, 22);
        _gameUI.AddChild(opcionesButton);
        opcionesButton.Pressed += () =>
        {
            _soundManager?.PlaySound(SoundManager.SND_CLICK);
            if (_optionsPanel != null)
            {
                if (_state.OptionsPanelOpen)
                    _optionsPanel.Close();
                else
                    _optionsPanel.Open();
            }
        };

        var estadisticasButton = CreateInvisibleButton(681, 507, 95, 24);
        _gameUI.AddChild(estadisticasButton);
        estadisticasButton.Pressed += () =>
        {
            _soundManager?.PlaySound(SoundManager.SND_CLICK);
            if (_statsPanel != null)
            {
                if (_state.StatsPanelOpen)
                    _statsPanel.Close();
                else
                    _statsPanel.Open();
            }
        };

        var clanesButton = CreateInvisibleButton(683, 532, 92, 26);
        _gameUI.AddChild(clanesButton);
        clanesButton.Pressed += () => { _soundManager?.PlaySound(SoundManager.SND_CLICK); OnClanesButtonPressed(); };

        // Minimize button — VB6: Image5 at (768, 0, 19, 17)
        var minimizeButton = CreateInvisibleButton(768, 0, 19, 17);
        _gameUI.AddChild(minimizeButton);
        minimizeButton.Pressed += OnMinimizePressed;

        // Close/Menu button — VB6: Image4 at (784, 0, 18, 17)
        var closeMenuButton = CreateInvisibleButton(784, 0, 18, 17);
        _gameUI.AddChild(closeMenuButton);
        closeMenuButton.Pressed += () =>
        {
            if (_state.EscapeMenuOpen)
                HideEscapeMenu();
            else
                ShowEscapeMenu();
        };

        // Commerce panel (frmComerciar) — centered on game viewport (544×416 at 8,144)
        _commercePanel = new CommercePanel();
        // Center: 8 + (544 - 445) / 2 = 57, y: 144 + (416 - 486) / 2 ≈ 109
        _commercePanel.Position = new Vector2(57, 109);
        _commercePanel.Visible = false;
        _gameUI.AddChild(_commercePanel);

        // Trade panel (frmComerciarUsu — player-to-player) — centered on game viewport
        _tradePanel = new TradePanel();
        // Center: 8 + (544 - 420) / 2 = 70, y: 144 + (416 - 420) / 2 = 142
        _tradePanel.Position = new Vector2(70, 142);
        _tradePanel.Visible = false;
        _gameUI.AddChild(_tradePanel);

        // Bank panel (frmBanco) — centered on viewport
        _bankPanel = new BankPanel();
        // Center: 8 + (544 - 165) / 2 = 197, y: 144 + (416 - 196) / 2 = 254
        _bankPanel.Position = new Vector2(197, 254);
        _bankPanel.Visible = false;
        _bankPanel.OnOpenVault += OnBankOpenVault;
        _gameUI.AddChild(_bankPanel);

        // Vault panel (frmNuevoBancoObj) — centered on viewport
        _vaultPanel = new VaultPanel();
        // Center: 8 + (544 - 450) / 2 = 55, y: 144 + (416 - 527) / 2 ≈ 88
        _vaultPanel.Position = new Vector2(55, 88);
        _vaultPanel.Visible = false;
        _gameUI.AddChild(_vaultPanel);

        // Guild bank panel (frmBovClan) — centered on viewport
        _guildBankPanel = new GuildBankPanel();
        _guildBankPanel.Position = new Vector2(55, 88);
        _guildBankPanel.Visible = false;
        _gameUI.AddChild(_guildBankPanel);

        // Craft panel (frmHerrero / frmCarpintero)
        _craftPanel = new CraftPanel();
        _craftPanel.Visible = false;
        _gameUI.AddChild(_craftPanel);

        // Guild panel (frmGuildInfo) — centered on viewport
        _guildPanel = new GuildPanel();
        _guildPanel.Position = new Vector2(60, 100);
        _guildPanel.Visible = false;
        _gameUI.AddChild(_guildPanel);

        // Guild foundation panel (frmGuildFoundation) — centered on viewport
        _guildFoundationPanel = new GuildFoundationPanel();
        _guildFoundationPanel.Position = new Vector2(80, 80);
        _guildFoundationPanel.Visible = false;
        _gameUI.AddChild(_guildFoundationPanel);

        // Forum panel (frmForo) — centered on viewport
        _forumPanel = new ForumPanel();
        _forumPanel.Position = new Vector2(20, 80);
        _forumPanel.Visible = false;
        _gameUI.AddChild(_forumPanel);

        // Friend list panel — right side of viewport
        _friendListPanel = new FriendListPanel();
        _friendListPanel.Position = new Vector2(240, 60);
        _friendListPanel.Visible = false;
        _gameUI.AddChild(_friendListPanel);

        // Mail panel — centered on viewport
        _mailPanel = new MailPanel();
        _mailPanel.Position = new Vector2(30, 50);
        _mailPanel.Visible = false;
        _gameUI.AddChild(_mailPanel);

        // Party panel (frmGrupo) — right side of viewport
        _partyPanel = new PartyPanel();
        _partyPanel.Position = new Vector2(480, 150);
        _partyPanel.Visible = false;
        _gameUI.AddChild(_partyPanel);

        // Travel panel (frmViajar) — centered on viewport
        _travelPanel = new TravelPanel();
        // Center: 8 + (544 - 450) / 2 = 55, y: 144 + (416 - 350) / 2 = 177
        _travelPanel.Position = new Vector2(55, 177);
        _travelPanel.Visible = false;
        _gameUI.AddChild(_travelPanel);

        // Death panel (frmMuertito) — centered on viewport
        _deathPanel = new DeathPanel();
        // Center: 8 + (544 - 263) / 2 = 148, y: 144 + (416 - 100) / 2 = 302
        _deathPanel.Position = new Vector2(148, 302);
        _deathPanel.Visible = false;
        _gameUI.AddChild(_deathPanel);

        // Macro panel (frmMakro) — centered on viewport
        _macroPanel = new MacroPanel();
        _macroPanel.Position = new Vector2(8 + (544 - 280) / 2, 144 + (416 - 380) / 2);
        _macroPanel.Visible = false;
        _gameUI.AddChild(_macroPanel);
        _macroPanel.Init(_state, _dataPath);

        // Stats panel (frmEstadisticas) — centered on viewport
        _statsPanel = new StatsPanel();
        _statsPanel.Position = new Vector2(8 + (544 - 380) / 2, 20);
        _statsPanel.Visible = false;
        _gameUI.AddChild(_statsPanel);
        _statsPanel.Init(_state);

        // NPC dialog panel — fixed at bottom-center
        _npcDialogPanel = new NpcDialogPanel();
        // Bottom-center: x = 8 + (544 - 300) / 2 = 130, y = viewport bottom - panel height - margin
        _npcDialogPanel.Position = new Vector2(8 + (544 - 300) / 2, 144 + 416 - 120 - 10);
        _npcDialogPanel.Visible = false;
        _gameUI.AddChild(_npcDialogPanel);
        _npcDialogPanel.Init(_state);

        // Character info popup — centered (position set dynamically on show)
        _charInfoPopup = new CharInfoPopup();
        _charInfoPopup.Visible = false;
        _gameUI.AddChild(_charInfoPopup);

        // Options panel (frmOpcionesNew) — centered on viewport
        _optionsPanel = new OptionsPanel();
        _optionsPanel.Position = new Vector2(8 + (544 - 420) / 2, 20);
        _optionsPanel.Visible = false;
        _gameUI.AddChild(_optionsPanel);
        _optionsPanel.Init(_state, _state.Config, _dataPath);
        _optionsPanel.OnConfigApplied += ApplyConfigToSystems;

        // Key binding panel (frmTeclas) — centered on viewport
        _keyBindPanel = new KeyBindPanel();
        _keyBindPanel.Position = new Vector2(8 + (544 - 420) / 2, 144 + (416 - 500) / 2);
        _keyBindPanel.Visible = false;
        _gameUI.AddChild(_keyBindPanel);
        _keyBindPanel.Init(_state, _state.Keys, _dataPath);

        // Wire the "Configurar Teclas" button in OptionsPanel to open KeyBindPanel
        _optionsPanel.OnOpenKeyBinds += () =>
        {
            _optionsPanel.Close();
            if (_keyBindPanel != null && !_state.KeyBindPanelOpen)
            {
                _state.KeyBindPanelOpen = true;
                _keyBindPanel.Open();
            }
        };

        // Tooltip panel — floating item info (added last so it renders on top)
        _tooltipPanel = new TooltipPanel();
        _gameUI.AddChild(_tooltipPanel);

        // Wire tooltip to all panels with item/spell slots
        _inventoryPanel.RichTooltip = _tooltipPanel;
        _spellPanel.RichTooltip = _tooltipPanel;
        _commercePanel.RichTooltip = _tooltipPanel;
        _vaultPanel.RichTooltip = _tooltipPanel;
        _tradePanel.RichTooltip = _tooltipPanel;
        _guildBankPanel.RichTooltip = _tooltipPanel;

        // Context menu — right-click on characters in game world
        _contextMenu = new ContextMenu();
        _gameUI.AddChild(_contextMenu);

        // Minimap panel — positioned above the sidebar buttons area
        // VB6 imgMapa button is at (682, 445). Place minimap above it.
        _minimapPanel = new MinimapPanel();
        _minimapPanel.Init(_state);
        // Position: right-aligned with sidebar, above the buttons
        _minimapPanel.Position = new Vector2(635, 300);
        _minimapPanel.Visible = _state.Config.ShowMinimap;
        _gameUI.AddChild(_minimapPanel);

        // Quest panel (frmQuest) — centered on viewport
        _questPanel = new QuestPanel();
        _questPanel.Position = new Vector2(8 + (544 - 560) / 2, 50);
        _questPanel.Visible = false;
        _gameUI.AddChild(_questPanel);

        // Trainer/Pet panel (frmEntrenador) — right of viewport
        _trainerPanel = new TrainerPanel();
        _trainerPanel.Position = new Vector2(150, 80);
        _trainerPanel.Visible = false;
        _gameUI.AddChild(_trainerPanel);

        // Blind screen overlay (VB6: frmMain goes black when blinded)
        // Covers entire window — fades in/out smoothly over ~0.3s
        _blindOverlay = new ColorRect();
        _blindOverlay.Color = new Color(0, 0, 0, 0);
        _blindOverlay.Position = Vector2.Zero;
        _blindOverlay.Size = new Vector2(800, 600);
        _blindOverlay.MouseFilter = Control.MouseFilterEnum.Ignore;
        _blindOverlay.Visible = true;
        _blindOverlay.ZIndex = 100; // Above game content but below modal dialogs
        _gameUI.AddChild(_blindOverlay);

        // Window mode startup dialog
        CreateWindowModeDialog();

        // Escape menu (in-game)
        CreateEscapeMenu();

        // Message dialog (VB6: Mensaje form)
        CreateMensajeDialog();

        // Drop quantity dialog (VB6: frmCantidad)
        CreateDropDialog();


        // "Crear Personaje" button on CharSelect screen
        _charSelectCreateBtn = new Button();
        _charSelectCreateBtn.Text = "Crear Personaje";
        _charSelectCreateBtn.CustomMinimumSize = new Vector2(0, 32);
        _charSelectCreateBtn.Pressed += OnCharSelectCreatePressed;
        var charSelectVBox = _charList!.GetParent();
        charSelectVBox.AddChild(_charSelectCreateBtn);

        // "Borrar Personaje" button on CharSelect screen
        _charSelectDeleteBtn = new Button();
        _charSelectDeleteBtn.Text = "Borrar Personaje";
        _charSelectDeleteBtn.CustomMinimumSize = new Vector2(0, 32);
        _charSelectDeleteBtn.AddThemeColorOverride("font_color", new Color(1f, 0.3f, 0.3f));
        _charSelectDeleteBtn.Pressed += OnDeleteCharPressed;
        charSelectVBox.AddChild(_charSelectDeleteBtn);

        // Delete character confirmation dialog
        CreateDeleteConfirmDialog();

        // "Desconectar" button on CharSelect screen
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
        var loginVBox = _connectButton!.GetParent();
        // Insert after ConnectButton, before StatusLabel
        int connectIdx = _connectButton.GetIndex();
        loginVBox.AddChild(crearCuentaBtn);
        loginVBox.MoveChild(crearCuentaBtn, connectIdx + 1);

        // Account creation panel
        CreateAccountCreatePanel();

        // Character creation panel
        CreateCharCreatePanel();

        // Load Principal.jpg background
        LoadBackgroundImage(dataPath);

        // Wire signals
        _connectButton.Pressed += OnConnectPressed;
        _enterButton.Pressed += OnEnterPressed;
        _charList.ItemActivated += OnCharListDoubleClick;
        _chatInput.TextSubmitted += OnChatSubmitted;
        _accountInput.TextSubmitted += (_) => OnConnectPressed();
        _passwordInput.TextSubmitted += (_) => OnConnectPressed();

        // Show window mode dialog first — login panel hidden until choice is made
        _loginPanel.Visible = false;
        _charSelectPanel.Visible = false;
        _charCreatePanel!.Visible = false;
        _accountCreatePanel!.Visible = false;
        _gameUI.Visible = false;

        // Load remembered account (XOR-encrypted file)
        LoadRememberedAccount();

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
        else if (_windowModeDialog != null)
        {
            // First launch — show window mode dialog
            _windowModeDialog.Visible = true;
            GD.Print("[MAIN] Window mode dialog shown");
            CallDeferred(MethodName.CenterWindowModeDialog);
        }
    }

    private void CenterWindowModeDialog()
    {
        if (_windowModeDialog == null) return;
        var screenSize = GetViewportRect().Size;
        _windowModeDialog.Position = new Vector2(
            (screenSize.X - _windowModeDialog.Size.X) / 2,
            (screenSize.Y - _windowModeDialog.Size.Y) / 2
        );
        GD.Print($"[MAIN] Window mode dialog centered at {_windowModeDialog.Position}, screen={screenSize}");
    }

    private void FocusAccountInput()
    {
        if (_accountInput == null) return;
        if (!string.IsNullOrEmpty(_accountInput.Text))
        {
            // Account pre-filled → focus password
            _passwordInput?.GrabFocus();
        }
        else
        {
            _accountInput.GrabFocus();
        }
    }

    // Simple XOR key for account remember file (not security-critical, just obfuscation)
    private const string RememberXorKey = "TierrasSagradas2024";
    private const string RememberFileName = "remembered.dat";

    private string GetRememberFilePath()
    {
        return System.IO.Path.Combine(_dataPath, RememberFileName);
    }

    private void LoadRememberedAccount()
    {
        string path = GetRememberFilePath();
        if (!System.IO.File.Exists(path)) return;

        try
        {
            byte[] encrypted = System.IO.File.ReadAllBytes(path);
            string decrypted = XorCrypt(encrypted);
            if (!string.IsNullOrEmpty(decrypted))
            {
                _accountInput!.Text = decrypted;
                _rememberCheck!.ButtonPressed = true;
            }
        }
        catch (Exception ex)
        {
            GD.Print($"[MAIN] Failed to load remembered account: {ex.Message}");
        }
    }

    private void SaveRememberedAccount(string account)
    {
        string path = GetRememberFilePath();
        try
        {
            if (_rememberCheck != null && _rememberCheck.ButtonPressed && !string.IsNullOrEmpty(account))
            {
                byte[] encrypted = XorCrypt(account);
                System.IO.File.WriteAllBytes(path, encrypted);
            }
            else
            {
                // Not remembering → delete file if exists
                if (System.IO.File.Exists(path))
                    System.IO.File.Delete(path);
            }
        }
        catch (Exception ex)
        {
            GD.Print($"[MAIN] Failed to save remembered account: {ex.Message}");
        }
    }

    private static byte[] XorCrypt(string plainText)
    {
        byte[] textBytes = System.Text.Encoding.UTF8.GetBytes(plainText);
        byte[] keyBytes = System.Text.Encoding.UTF8.GetBytes(RememberXorKey);
        byte[] result = new byte[textBytes.Length];
        for (int i = 0; i < textBytes.Length; i++)
            result[i] = (byte)(textBytes[i] ^ keyBytes[i % keyBytes.Length]);
        return result;
    }

    private static string XorCrypt(byte[] encrypted)
    {
        byte[] keyBytes = System.Text.Encoding.UTF8.GetBytes(RememberXorKey);
        byte[] result = new byte[encrypted.Length];
        for (int i = 0; i < encrypted.Length; i++)
            result[i] = (byte)(encrypted[i] ^ keyBytes[i % keyBytes.Length]);
        return System.Text.Encoding.UTF8.GetString(result);
    }

    /// <summary>
    /// Apply bold font to a Label (VB6: all labels use Font.Weight=700).
    /// Uses a SystemFont with weight 700 overriding the theme font.
    /// </summary>
    /// Apply a specific font to a label (VB6 parity: exact font family + weight).
    private static void ApplyFont(Label label, string fontName = "Tahoma", int weight = 700)
    {
        var font = new SystemFont();
        font.FontNames = new string[] { fontName };
        font.FontWeight = weight;
        font.MultichannelSignedDistanceField = true;
        label.AddThemeFontOverride("font", font);
    }

    private static Label CreateStatLabel(float x, float y, float w, float h, Color color, int fontSize,
                                          string fontName = "Tahoma", int weight = 700)
    {
        var label = new Label();
        label.Position = new Vector2(x, y);
        label.Size = new Vector2(w, h);
        label.HorizontalAlignment = HorizontalAlignment.Center;
        label.AddThemeColorOverride("font_color", color);
        label.AddThemeFontSizeOverride("font_size", fontSize);
        label.MouseFilter = Control.MouseFilterEnum.Ignore;
        ApplyFont(label, fontName, weight);
        return label;
    }

    /// <summary>
    /// Create a fully invisible button (flat, no text, empty styleboxes).
    /// VB6 visuals are baked into Principal.jpg — this is just a hit-detect area.
    /// If usePointerCursor is true, shows hand cursor on hover (VB6 MousePointer=99).
    /// </summary>
    private static Button CreateInvisibleButton(float x, float y, float w, float h, bool usePointerCursor = true)
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
        if (usePointerCursor)
            btn.MouseDefaultCursorShape = Control.CursorShape.PointingHand;
        btn.FocusMode = Control.FocusModeEnum.None; // Never steal keyboard focus
        return btn;
    }

    /// <summary>
    /// Called when BankPanel's "Abrir Bóveda" button is clicked.
    /// Opens VaultPanel (the item vault).
    /// </summary>
    private void OnBankOpenVault()
    {
        _vaultPanel?.OpenVault();
    }

    /// <summary>
    /// Toggle work macro — auto-repeats UseItem on the currently selected inventory slot.
    /// VB6: tmrTrabajo timer for fishing/mining/woodcutting/smelting auto-repeat.
    /// </summary>
    private void HandleWorkMacroToggle()
    {
        if (_state.WorkMacro.Active)
        {
            _state.WorkMacro.Stop();
            _state.ChatMessages.Enqueue(new ChatMessage
            {
                Text = ">>MACRO DE TRABAJO DESACTIVADO<<",
                Color = "FF0000"
            });
        }
        else
        {
            int slot = _state.SelectedInvSlot;
            if (slot < 0 || slot >= 25 || _state.Inventory[slot].ObjIndex <= 0)
            {
                _state.ChatMessages.Enqueue(new ChatMessage
                {
                    Text = "Selecciona un objeto de trabajo primero.",
                    Color = "FF0000"
                });
                return;
            }
            byte serverSlot = (byte)(slot + 1);
            _state.WorkMacro.Start(() =>
            {
                _tcp?.SendPacket(ClientPackets.WriteUseItem(serverSlot));
            });
            _state.ChatMessages.Enqueue(new ChatMessage
            {
                Text = $">>MACRO DE TRABAJO ACTIVADO<< ({_state.Inventory[slot].Name})",
                Color = "00FF00"
            });
        }
    }

    /// <summary>
    /// Toggle spell training macro — auto-repeats CastSpell for the selected spell slot.
    /// </summary>
    private void HandleSpellMacroToggle()
    {
        if (_state.SpellMacro.Active)
        {
            _state.SpellMacro.Stop();
            _state.ChatMessages.Enqueue(new ChatMessage
            {
                Text = ">>MACRO DE HECHIZOS DESACTIVADO<<",
                Color = "FF0000"
            });
        }
        else
        {
            // SpellPanel stores selected spell — find it
            int spellSlot = -1;
            for (int i = 0; i < 20; i++)
            {
                if (_state.Spells[i].SpellId > 0)
                {
                    spellSlot = i;
                    break;
                }
            }
            // Use first available spell if no specific one selected
            if (spellSlot < 0)
            {
                _state.ChatMessages.Enqueue(new ChatMessage
                {
                    Text = "No tienes hechizos disponibles.",
                    Color = "FF0000"
                });
                return;
            }
            byte slot = (byte)(spellSlot + 1);
            string spellName = _state.Spells[spellSlot].Name;
            _state.SpellMacro.Start(() =>
            {
                _tcp?.SendPacket(ClientPackets.WriteCastSpell(slot));
            });
            _state.ChatMessages.Enqueue(new ChatMessage
            {
                Text = $">>MACRO DE HECHIZOS ACTIVADO<< ({spellName})",
                Color = "00FF00"
            });
        }
    }

    private void OnInventoryTabPressed()
    {
        _showingSpells = false;
        _tooltipPanel?.Hide();
        _inventoryPanel!.Visible = true;
        _itemNameLabel!.Visible = true;
        _dydToggle!.Visible = true;
        // Sync DyD button texture with current state
        _dydToggle.TextureNormal = _inventoryPanel.DyDEnabled ? _dydOnTex : _dydOffTex;
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
        _tooltipPanel?.Hide();
        _inventoryPanel!.CancelDrag();
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
        // VB6: dead players can't cast — server sends "Estas muerto" but client should ignore
        if (_state.Dead) return;

        _spellPanel!.CastSelected();
        if (_state.UsingSkill > 0)
        {
            // VB6: frmMain.MousePointer = 2 (crosshair cursor)
            Input.SetDefaultCursorShape(Input.CursorShape.Cross);
            // VB6: AddtoRichTextBox MENSAJE_TRABAJO_MAGIA (Declares.bas:586)
            _state.ChatMessages.Enqueue(new ChatMessage
            {
                Text = "Haz click sobre el objetivo...",
                Color = "6464B4" // VB6: 100,100,120 → RGB hex
            });
        }
    }

    private void LoadBackgroundImage(string dataPath)
    {
        // All Principal assets live in the Godot client's own Data/Graficos/Principal/
        string principalDir = System.IO.Path.Combine(dataPath, "Graficos", "Principal");
        string principalPath = System.IO.Path.Combine(principalDir, "Principal.jpg");

        GD.Print($"[MAIN] Looking for Principal.jpg at {principalPath}");

        if (!System.IO.File.Exists(principalPath))
        {
            GD.Print("[MAIN] Principal.jpg not found — using dark background");
            return;
        }

        // Load Principal.jpg
        try
        {
            var image = new Image();
            image.Load(principalPath);
            _backgroundImage!.Texture = ImageTexture.CreateFromImage(image);
            GD.Print($"[MAIN] Loaded Principal.jpg from {principalDir}");
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[MAIN] Failed to load Principal.jpg: {ex.Message}");
        }

        // Load InvEqu textures (inventory / spells panel backgrounds)
        _invEquInvTexture = LoadJpgTexture(System.IO.Path.Combine(principalDir, "CentroInventario.jpg"));
        _invEquSpellTexture = LoadJpgTexture(System.IO.Path.Combine(principalDir, "CentroHechizos.jpg"));

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
        if (_connecting) return;

        string account = _accountInput!.Text.Trim();
        string password = _passwordInput!.Text.Trim();

        if (string.IsNullOrEmpty(account) || string.IsNullOrEmpty(password))
        {
            _statusLabel!.Text = "Ingrese cuenta y password";
            return;
        }

        // Save or clear remembered account
        SaveRememberedAccount(account);

        _state.AccountName = account;
        _state.LoginError = "";
        _connectButton!.Disabled = true;
        _statusLabel!.Text = "Conectando...";

        _tcp = new AoTcpClient();
        _packetHandler = new PacketHandler(_state);
        _packetHandler.OnMapLoad = LoadCurrentMap;
        if (_soundManager != null)
        {
            _packetHandler.OnPlaySound = (id) => _soundManager.PlaySound(id);
            _packetHandler.OnPlayMusic = (id) => _soundManager.PlayMusic(id);
        }
        _inputHandler = new InputHandler(_tcp, _state, _state.Keys, GetViewport());
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
            _tcp.SendPacket(ClientPackets.WriteKerd22());

            await Task.Delay(50);
            _tcp.SendPacket(ClientPackets.WriteAlogin(account, password));
            GD.Print("[MAIN] Sent: ALOGIN (binary)");

            // Timeout: if server doesn't respond within 8 seconds, abort.
            // Don't check IsConnected — server may have dropped the connection,
            // which is exactly the case we need to handle.
            _ = Task.Run(async () =>
            {
                await Task.Delay(8000);
                if (_state.CurrentScreen == Screen.Login && !_connecting
                    && _statusLabel!.Text == "Enviando login...")
                {
                    CallDeferred(nameof(LoginTimeout));
                }
            });
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[MAIN] Connection failed: {ex}");
            _connecting = false;
            _tcp?.Dispose();
            _tcp = null;
            _inputHandler = null;

            _statusLabel!.Text = FriendlyConnectionError(ex);
            _connectButton!.Disabled = false;
        }
    }

    private void LoginTimeout()
    {
        GD.PrintErr("[MAIN] Login timeout — server did not respond");
        _tcp?.Dispose();
        _tcp = null;
        _inputHandler = null;
        _statusLabel!.Text = "Error: El servidor no respondió.";
        _connectButton!.Disabled = false;
    }

    private static string FriendlyConnectionError(Exception ex) => ex switch
    {
        System.Net.Sockets.SocketException se => se.SocketErrorCode switch
        {
            System.Net.Sockets.SocketError.ConnectionRefused => "El servidor no está disponible. Intentá de nuevo en unos segundos.",
            System.Net.Sockets.SocketError.TimedOut => "No se pudo conectar: el servidor no responde.",
            System.Net.Sockets.SocketError.HostNotFound => "No se encontró el servidor. Verificá tu conexión.",
            System.Net.Sockets.SocketError.NetworkUnreachable => "Red no disponible. Verificá tu conexión a internet.",
            _ => $"Error de conexión: {se.SocketErrorCode}",
        },
        OperationCanceledException => "No se pudo conectar: el servidor no responde.",
        NullReferenceException => "El servidor no está disponible. Intentá de nuevo en unos segundos.",
        _ => $"Error de conexión: {ex.Message}",
    };

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

                _tcp!.SendPacket(ClientPackets.WriteOologi(charName, account, code));
                GD.Print($"[MAIN] Sent: OOLOGI {charName}");
            }
        }
        else
        {
            _noticeLabel!.Text = "Seleccione un personaje";
        }
    }

    /// <summary>
    /// Double-click on character list → enter game (same as clicking Enter button).
    /// </summary>
    private void OnCharListDoubleClick(long index)
    {
        OnEnterPressed();
    }

    /// <summary>
    /// VB6 chat behavior:
    /// - Enter with text → send message with mode prefix, clear input, hide input
    /// - Enter with empty/space → send ";  " to clear text above head (VB6 carteleo behavior)
    /// Text is NOT added to console here — the server echoes it back as a T| packet.
    /// </summary>
    private void OnChatSubmitted(string text)
    {
        if (_tcp != null)
        {
            if (text.StartsWith("/"))
            {
                // /PING: record timestamp before sending
                if (text.Equals("/PING", System.StringComparison.OrdinalIgnoreCase))
                {
                    _state.PingSentMs = Time.GetTicksMsec();
                }
                // /MACRO: toggle work macro (auto-repeat UseItem on selected slot)
                else if (text.Equals("/MACRO", System.StringComparison.OrdinalIgnoreCase))
                {
                    HandleWorkMacroToggle();
                    // Don't send to server — client-only command
                    _chatInput!.Text = "";
                    _chatInput.Visible = false;
                    _chatInput.ReleaseFocus();
                    _state.ChatActive = false;
                    return;
                }
                // /MACROSP: toggle spell training macro
                else if (text.Equals("/MACROSP", System.StringComparison.OrdinalIgnoreCase))
                {
                    HandleSpellMacroToggle();
                    _chatInput!.Text = "";
                    _chatInput.Visible = false;
                    _chatInput.ReleaseFocus();
                    _state.ChatActive = false;
                    return;
                }
                // /PASSWD: open change password panel
                else if (text.Equals("/PASSWD", System.StringComparison.OrdinalIgnoreCase))
                {
                    _state.ShowChangePassword = true;
                    _chatInput!.Text = "";
                    _chatInput.Visible = false;
                    _chatInput.ReleaseFocus();
                    _state.ChatActive = false;
                    return;
                }
                // Slash commands: send as Talk with the /command
                _tcp.SendPacket(ClientPackets.WriteTalk(text));
            }
            else if (!string.IsNullOrEmpty(text) && text.Trim().Length > 0)
            {
                // Normal message with current chat mode
                byte[] chatPkt = _state.ChatModePrefix switch
                {
                    "-" => ClientPackets.WriteYell(text),
                    "\\" => ClientPackets.WriteWhisper("", text), // whisper target parsed server-side
                    _ => ClientPackets.WriteTalk(text),
                };
                _tcp.SendPacket(chatPkt);
            }
            else
            {
                // Empty/whitespace: send space to clear text above head (VB6 carteleo)
                _tcp.SendPacket(ClientPackets.WriteTalk(" "));
            }
        }
        // Always hide + clear on submit (VB6: Enter closes the input)
        _chatInput!.Text = "";
        _chatInput.Visible = false;
        _chatInput.ReleaseFocus();
        _state.ChatActive = false;
    }

    /// <summary>
    /// VB6 TalkMode(): Switch chat mode prefix.
    /// Modes: 0=normal, 1=yell, 2=clan, 3=global, 4=party, 5=faction, 6=gm/report, 7=whisper
    /// </summary>
    private void SetChatMode(int mode)
    {
        _state.ChatMode = mode;
        switch (mode)
        {
            case 0: _state.ChatModePrefix = ";"; break;        // Normal talk
            case 1: _state.ChatModePrefix = "-"; break;        // Yell/Gritar
            case 2: _state.ChatModePrefix = ";/cmsg "; break;  // Clan chat
            case 3: _state.ChatModePrefix = ";/GLOBAL "; break; // Global chat
            case 4: _state.ChatModePrefix = ";/pmsg "; break;  // Party chat
            case 5: _state.ChatModePrefix = ";/FMSG "; break;  // Faction chat
            case 6: _state.ChatModePrefix = ";/gmsg "; break;  // GM report
            case 7:                                              // Whisper
                _state.ChatModePrefix = $"\\{_state.WhisperTarget}@";
                break;
            default: _state.ChatModePrefix = ";"; break;
        }
        string[] modeNames = { "Normal", "Gritar", "Clan", "Global", "Party", "Facción", "GM", "Privado" };
        string modeName = mode >= 0 && mode < modeNames.Length ? modeNames[mode] : "Normal";
        _state.ChatMessages.Enqueue(new ChatMessage
        {
            Text = $"Modo de habla: {modeName}",
            Color = "FFFFFF"
        });
    }

    /// <summary>
    /// Create the Mensaje dialog (VB6: Mensaje form).
    /// Modal dialog with message text and "Aceptar" button. VB6 shows this for
    /// ERR, ERO, and !! packets. Enter key also dismisses.
    /// </summary>
    private void CreateMensajeDialog()
    {
        _mensajeDialog = new PanelContainer();
        _mensajeDialog.Size = new Vector2(340, 160);
        // Center on window (544x416 game viewport offset at 8,144)
        _mensajeDialog.Visible = false;
        _mensajeDialog.ZIndex = 200; // Above everything

        var bg = new StyleBoxFlat();
        bg.BgColor = new Color(0.08f, 0.08f, 0.12f, 0.97f);
        bg.BorderColor = new Color(0.55f, 0.45f, 0.3f);
        bg.SetBorderWidthAll(2);
        bg.SetContentMarginAll(14);
        bg.SetCornerRadiusAll(4);
        _mensajeDialog.AddThemeStyleboxOverride("panel", bg);

        var vbox = new VBoxContainer();
        vbox.AddThemeConstantOverride("separation", 10);
        _mensajeDialog.AddChild(vbox);

        _mensajeLabel = new Label();
        _mensajeLabel.Text = "";
        _mensajeLabel.HorizontalAlignment = HorizontalAlignment.Center;
        _mensajeLabel.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _mensajeLabel.AddThemeColorOverride("font_color", Colors.White);
        _mensajeLabel.AddThemeFontSizeOverride("font_size", 12);
        vbox.AddChild(_mensajeLabel);

        var acceptBtn = new Button();
        acceptBtn.Text = "Aceptar";
        acceptBtn.CustomMinimumSize = new Vector2(100, 30);
        acceptBtn.AddThemeFontSizeOverride("font_size", 12);
        acceptBtn.SizeFlagsHorizontal = Control.SizeFlags.ShrinkCenter;
        acceptBtn.Pressed += OnMensajeAccept;
        vbox.AddChild(acceptBtn);

        // Add to UILayer (CanvasLayer 10) so it renders above everything,
        // including LoginPanel and CharSelectPanel which are also on UILayer.
        GetNode<CanvasLayer>("UILayer").AddChild(_mensajeDialog);
    }

    private void ShowMensaje(string text)
    {
        if (_mensajeDialog == null || _mensajeLabel == null) return;

        _mensajeLabel.Text = text;

        // VB6: auto-adjust font size for long messages
        if (text.Length > 120)
            _mensajeLabel.AddThemeFontSizeOverride("font_size", 10);
        else if (text.Length > 75)
            _mensajeLabel.AddThemeFontSizeOverride("font_size", 11);
        else
            _mensajeLabel.AddThemeFontSizeOverride("font_size", 12);

        // Fixed size based on text length — reset anchors to prevent stretch
        int height = text.Length > 120 ? 200 : (text.Length > 75 ? 180 : 160);
        _mensajeDialog.ResetSize();
        _mensajeDialog.SetAnchorsPreset(Control.LayoutPreset.TopLeft);
        _mensajeDialog.Size = new Vector2(340, height);

        // Center on screen
        var screenSize = GetViewportRect().Size;
        _mensajeDialog.Position = new Vector2(
            (screenSize.X - 340) / 2,
            (screenSize.Y - height) / 2
        );

        _mensajeDialog.Visible = true;
    }

    private void OnMensajeAccept()
    {
        if (_mensajeDialog != null)
            _mensajeDialog.Visible = false;
    }

    /// <summary>
    /// Creates the startup dialog asking "windowed or fullscreen?"
    /// Shown before login panel; login is hidden until a choice is made.
    /// </summary>
    private void CreateWindowModeDialog()
    {
        _windowModeDialog = new PanelContainer();
        _windowModeDialog.Size = new Vector2(360, 140);
        _windowModeDialog.Visible = false;
        _windowModeDialog.ZIndex = 200;

        var bg = new StyleBoxFlat();
        bg.BgColor = new Color(0.08f, 0.08f, 0.12f, 0.97f);
        bg.BorderColor = new Color(0.55f, 0.45f, 0.3f);
        bg.SetBorderWidthAll(2);
        bg.SetContentMarginAll(14);
        bg.SetCornerRadiusAll(4);
        _windowModeDialog.AddThemeStyleboxOverride("panel", bg);

        var vbox = new VBoxContainer();
        vbox.AddThemeConstantOverride("separation", 12);
        _windowModeDialog.AddChild(vbox);

        var label = new Label();
        label.Text = "¿Deseas ejecutar en modo ventana?";
        label.HorizontalAlignment = HorizontalAlignment.Center;
        label.AddThemeColorOverride("font_color", Colors.White);
        label.AddThemeFontSizeOverride("font_size", 13);
        vbox.AddChild(label);

        var btnBox = new HBoxContainer();
        btnBox.AddThemeConstantOverride("separation", 16);
        btnBox.Alignment = BoxContainer.AlignmentMode.Center;
        vbox.AddChild(btnBox);

        var yesBtn = new Button();
        yesBtn.Text = "Sí (Ventana)";
        yesBtn.CustomMinimumSize = new Vector2(130, 34);
        yesBtn.AddThemeFontSizeOverride("font_size", 12);
        yesBtn.Pressed += () => OnWindowModeChosen(true);
        btnBox.AddChild(yesBtn);

        var noBtn = new Button();
        noBtn.Text = "No (Pantalla Completa)";
        noBtn.CustomMinimumSize = new Vector2(160, 34);
        noBtn.AddThemeFontSizeOverride("font_size", 12);
        noBtn.Pressed += () => OnWindowModeChosen(false);
        btnBox.AddChild(noBtn);

        // Add to UILayer (CanvasLayer 10) so it renders above game background
        // and is on the same layer as LoginPanel (which starts hidden).
        var uiLayer = GetNode<CanvasLayer>("UILayer");
        uiLayer.AddChild(_windowModeDialog);
    }

    private void OnWindowModeChosen(bool windowed)
    {
        // Save choice to config so next launch skips the dialog
        _state.Config.Fullscreen = !windowed;
        if (!windowed)
            _state.Config.AspectRatioMode = 1; // default 16:9

        if (!windowed)
        {
            GetTree().Root.ContentScaleAspect = Window.ContentScaleAspectEnum.Keep;
            DisplayServer.WindowSetMode(DisplayServer.WindowMode.Fullscreen);
        }
        else
        {
            DisplayServer.WindowSetFlag(DisplayServer.WindowFlags.ResizeDisabled, false);
            var winSize = new Vector2I(800, 600);
            DisplayServer.WindowSetSize(winSize);
            var screenSize = DisplayServer.ScreenGetSize();
            DisplayServer.WindowSetPosition(new Vector2I(
                (screenSize.X - winSize.X) / 2,
                (screenSize.Y - winSize.Y) / 2));
            GetTree().Root.ContentScaleAspect = Window.ContentScaleAspectEnum.Keep;
        }

        // Persist so next launch auto-applies
        _state.Config.Save(_dataPath);

        if (_windowModeDialog != null)
            _windowModeDialog.Visible = false;

        // Now show the login panel
        if (_loginPanel != null)
            _loginPanel.Visible = true;

        CallDeferred(MethodName.FocusAccountInput);
    }

    /// <summary>
    /// Creates the in-game escape menu with Cerrar Sesión / Salir / Volver.
    /// </summary>
    private void CreateEscapeMenu()
    {
        _escapeMenu = new PanelContainer();
        _escapeMenu.Size = new Vector2(260, 200);
        _escapeMenu.Visible = false;
        _escapeMenu.ZIndex = 200;

        var bg = new StyleBoxFlat();
        bg.BgColor = new Color(0.08f, 0.08f, 0.12f, 0.97f);
        bg.BorderColor = new Color(0.55f, 0.45f, 0.3f);
        bg.SetBorderWidthAll(2);
        bg.SetContentMarginAll(14);
        bg.SetCornerRadiusAll(4);
        _escapeMenu.AddThemeStyleboxOverride("panel", bg);

        var vbox = new VBoxContainer();
        vbox.AddThemeConstantOverride("separation", 10);
        _escapeMenu.AddChild(vbox);

        var title = new Label();
        title.Text = "Menú";
        title.HorizontalAlignment = HorizontalAlignment.Center;
        title.AddThemeColorOverride("font_color", Colors.White);
        title.AddThemeFontSizeOverride("font_size", 15);
        vbox.AddChild(title);

        var sep = new HSeparator();
        vbox.AddChild(sep);

        var logoutBtn = new Button();
        logoutBtn.Text = "Cerrar Sesión";
        logoutBtn.CustomMinimumSize = new Vector2(0, 34);
        logoutBtn.AddThemeFontSizeOverride("font_size", 12);
        logoutBtn.Pressed += OnEscapeMenuLogout;
        vbox.AddChild(logoutBtn);

        var quitBtn = new Button();
        quitBtn.Text = "Salir del Juego";
        quitBtn.CustomMinimumSize = new Vector2(0, 34);
        quitBtn.AddThemeFontSizeOverride("font_size", 12);
        quitBtn.Pressed += () => GetTree().Quit();
        vbox.AddChild(quitBtn);

        var backBtn = new Button();
        backBtn.Text = "Volver";
        backBtn.CustomMinimumSize = new Vector2(0, 34);
        backBtn.AddThemeFontSizeOverride("font_size", 12);
        backBtn.Pressed += HideEscapeMenu;
        vbox.AddChild(backBtn);

        AddChild(_escapeMenu);
    }

    private void OnMinimizePressed()
    {
        _preMiniMode = DisplayServer.WindowGetMode();
        _restoringFullscreen = false;
        DisplayServer.WindowSetMode(DisplayServer.WindowMode.Minimized);
    }

    public override void _Notification(int what)
    {
        // When the window is restored from minimize, re-apply fullscreen after
        // a short delay so the window manager finishes its restore animation.
        // Guard against re-entrancy: fullscreen transition itself fires focus events.
        if (what == NotificationWMWindowFocusIn
            && _preMiniMode == DisplayServer.WindowMode.Fullscreen
            && DisplayServer.WindowGetMode() != DisplayServer.WindowMode.Fullscreen
            && !_restoringFullscreen)
        {
            _restoringFullscreen = true;
            GetTree().CreateTimer(0.3).Timeout += RestoreFullscreen;
        }
    }

    private void RestoreFullscreen()
    {
        var cfg = _state.Config;
        GetTree().Root.ContentScaleAspect = cfg.AspectRatioMode == 1
            ? Window.ContentScaleAspectEnum.Keep
            : Window.ContentScaleAspectEnum.Keep;
        DisplayServer.WindowSetMode(DisplayServer.WindowMode.Fullscreen);
        // Reset after a short settle so future minimizes work
        GetTree().CreateTimer(0.5).Timeout += () => _restoringFullscreen = false;
    }

    private void ShowEscapeMenu()
    {
        if (_escapeMenu == null) return;
        var screenSize = GetViewportRect().Size;
        _escapeMenu.Position = new Vector2(
            (screenSize.X - _escapeMenu.Size.X) / 2,
            (screenSize.Y - _escapeMenu.Size.Y) / 2
        );
        _escapeMenu.Visible = true;
        _state.EscapeMenuOpen = true;
    }

    private void HideEscapeMenu()
    {
        if (_escapeMenu != null)
            _escapeMenu.Visible = false;
        _state.EscapeMenuOpen = false;
    }

    private void OnEscapeMenuLogout()
    {
        HideEscapeMenu();
        _tcp?.SendPacket(ClientPackets.WriteTalk("/salir"));
        HandleDisconnect("");
    }

    private void OnClanesButtonPressed()
    {
        // VB6 imgClanes_Click: sends WriteRequestGuildLeaderInfo
        _tcp?.SendPacket(ClientPackets.WriteGuildInfo());
    }

    /// <summary>
    /// Create the drop quantity dialog (VB6: frmCantidad).
    /// Centered on the game viewport. Has a numeric input, OK/All/Cancel buttons.
    /// </summary>
    private void CreateDropDialog()
    {
        _dropDialog = new PanelContainer();
        _dropDialog.Size = new Vector2(200, 110);
        // Center on game viewport: x=8+(544-200)/2=180, y=144+(416-110)/2=297
        _dropDialog.Position = new Vector2(180, 297);
        _dropDialog.Visible = false;

        var bg = new StyleBoxFlat();
        bg.BgColor = new Color(0.12f, 0.12f, 0.18f, 0.95f);
        bg.BorderColor = new Color(0.4f, 0.4f, 0.6f);
        bg.SetBorderWidthAll(1);
        bg.SetContentMarginAll(8);
        _dropDialog.AddThemeStyleboxOverride("panel", bg);

        var vbox = new VBoxContainer();
        vbox.AddThemeConstantOverride("separation", 6);
        _dropDialog.AddChild(vbox);

        _dropDialogLabel = new Label();
        _dropDialogLabel.Text = "Cantidad a tirar:";
        _dropDialogLabel.HorizontalAlignment = HorizontalAlignment.Center;
        _dropDialogLabel.AddThemeColorOverride("font_color", Colors.White);
        _dropDialogLabel.AddThemeFontSizeOverride("font_size", 12);
        vbox.AddChild(_dropDialogLabel);

        _dropDialogInput = new LineEdit();
        _dropDialogInput.Text = "1";
        _dropDialogInput.Alignment = HorizontalAlignment.Center;
        _dropDialogInput.FocusMode = Control.FocusModeEnum.Click;
        _dropDialogInput.AddThemeFontSizeOverride("font_size", 12);
        _dropDialogInput.TextSubmitted += (_) => OnDropDialogOk();
        vbox.AddChild(_dropDialogInput);

        var hbox = new HBoxContainer();
        hbox.AddThemeConstantOverride("separation", 4);
        hbox.Alignment = BoxContainer.AlignmentMode.Center;
        vbox.AddChild(hbox);

        _dropDialogOk = new Button();
        _dropDialogOk.Text = "Tirar";
        _dropDialogOk.CustomMinimumSize = new Vector2(55, 24);
        _dropDialogOk.AddThemeFontSizeOverride("font_size", 11);
        _dropDialogOk.Pressed += OnDropDialogOk;
        hbox.AddChild(_dropDialogOk);

        _dropDialogAll = new Button();
        _dropDialogAll.Text = "Todo";
        _dropDialogAll.CustomMinimumSize = new Vector2(55, 24);
        _dropDialogAll.AddThemeFontSizeOverride("font_size", 11);
        _dropDialogAll.Pressed += OnDropDialogAll;
        hbox.AddChild(_dropDialogAll);

        _dropDialogCancel = new Button();
        _dropDialogCancel.Text = "X";
        _dropDialogCancel.CustomMinimumSize = new Vector2(30, 24);
        _dropDialogCancel.AddThemeFontSizeOverride("font_size", 11);
        _dropDialogCancel.Pressed += OnDropDialogCancel;
        hbox.AddChild(_dropDialogCancel);

        _gameUI!.AddChild(_dropDialog);
    }

    private void OnDropDialogOk()
    {
        if (_dropDialogInput == null || _tcp == null) return;
        int qty = 0;
        int.TryParse(_dropDialogInput.Text, out qty);
        if (qty > 0)
        {
            int slot = _state.DropDialogSlot;
            _tcp.SendPacket(ClientPackets.WriteDropItem((byte)(slot + 1), (short)qty));
        }
        CloseDropDialog();
    }

    private void OnDropDialogAll()
    {
        if (_tcp == null) return;
        int slot = _state.DropDialogSlot;
        if (slot >= 0 && slot < 25)
        {
            int qty = _state.Inventory[slot].Amount;
            _tcp.SendPacket(ClientPackets.WriteDropItem((byte)(slot + 1), (short)qty));
        }
        CloseDropDialog();
    }

    private void OnDropDialogCancel()
    {
        CloseDropDialog();
    }

    private void CloseDropDialog()
    {
        _state.DropDialogOpen = false;
        if (_dropDialog != null)
            _dropDialog.Visible = false;
    }

    /// <summary>
    /// Handle inventory drag & drop that ended outside the inventory panel.
    /// Routes to trade panel, vault panel, guild bank, or ground drop based on drop position.
    /// </summary>
    private void OnInventoryDropOutside(int slot, Vector2 globalPos)
    {
        if (_tcp == null || slot < 0 || slot >= 25) return;
        var item = _state.Inventory[slot];
        if (item.ObjIndex <= 0) return;

        byte slot1 = (byte)(slot + 1); // server uses 1-indexed slots

        // Check if drop landed on the trade panel (player-to-player trade)
        if (_state.Trading && _tradePanel != null && _tradePanel.Visible)
        {
            var tradeRect = new Rect2(_tradePanel.GlobalPosition, _tradePanel.Size);
            if (tradeRect.HasPoint(globalPos))
            {
                _tcp.SendPacket(ClientPackets.WriteTradeOfferItem(slot1, (short)item.Amount));
                return;
            }
        }

        // Check if drop landed on the vault panel (item bank/boveda)
        if (_state.BovedaAbierta && _vaultPanel != null && _vaultPanel.Visible)
        {
            var vaultRect = new Rect2(_vaultPanel.GlobalPosition, _vaultPanel.Size);
            if (vaultRect.HasPoint(globalPos))
            {
                _tcp.SendPacket(ClientPackets.WriteBankDeposit(slot1, (short)item.Amount));
                return;
            }
        }

        // Check if drop landed on the guild bank panel
        if (_state.ShowGuildBank && _guildBankPanel != null && _guildBankPanel.Visible)
        {
            var gbRect = new Rect2(_guildBankPanel.GlobalPosition, _guildBankPanel.Size);
            if (gbRect.HasPoint(globalPos))
            {
                _tcp.SendPacket(ClientPackets.WriteGuildBankDepositItem(slot1, (short)item.Amount));
                return;
            }
        }

        // Not on any panel — drop on ground.
        // If ItemSafety is on and amount > 1, show quantity dialog. Otherwise drop whole stack.
        if (_state.ItemSafety && item.Amount > 1)
        {
            _state.DropDialogSlot = slot;
            _state.DropDialogOpen = true;
            if (_dropDialog != null)
            {
                _dropDialog.Visible = true;
                _dropDialogLabel!.Text = $"Tirar {item.Name}:";
                _dropDialogInput!.Text = "1";
                _dropDialogInput.GrabFocus();
                _dropDialogInput.SelectAll();
            }
        }
        else
        {
            _tcp.SendPacket(ClientPackets.WriteDropItem(slot1, (short)item.Amount));
        }
    }

    // =====================================================================
    // Character Creation Panel
    // =====================================================================

    private static readonly string[] RaceNames = { "Humano", "Elfo", "Elfo Oscuro", "Enano", "Gnomo" };
    private static readonly string[] GenderNames = { "Hombre", "Mujer" };
    // VB6 eClass order: 1=Mago,2=Clerigo,3=Guerrero,4=Asesino,5=Ladron,6=Bardo,
    //   7=Druida,8=Bandido,9=Paladin,10=Cazador,11=Trabajador,12=Pirata
    private static readonly string[] ClassNames = { "Mago", "Clerigo", "Guerrero", "Asesino", "Ladron", "Bardo", "Druida", "Bandido", "Paladin", "Cazador", "Trabajador", "Pirata" };
    private static readonly string[] FactionNames = { "Armada Real", "Fuerzas del Caos" };

    // Head ranges per race (1-5) and gender (1=M, 2=F) from VB6 DameOpciones
    private static (int min, int max) GetHeadRange(int race, int gender)
    {
        return (race, gender) switch
        {
            (1, 1) => (1, 30),     // Humano Male
            (1, 2) => (70, 76),    // Humano Female
            (2, 1) => (101, 113),  // Elfo Male
            (2, 2) => (170, 176),  // Elfo Female
            (3, 1) => (202, 209),  // Elfo Oscuro Male
            (3, 2) => (270, 280),  // Elfo Oscuro Female
            (4, 1) => (301, 305),  // Enano Male
            (4, 2) => (370, 373),  // Enano Female
            (5, 1) => (401, 406),  // Gnomo Male
            (5, 2) => (470, 474),  // Gnomo Female
            _ => (1, 30),
        };
    }

    private void OnCrearCuentaPressed()
    {
        _state.CurrentScreen = Screen.AccountCreate;
        HandleScreenChange(Screen.AccountCreate);
        _lastScreen = Screen.AccountCreate;
    }

    private void CreateAccountCreatePanel()
    {
        _accountCreatePanel = new PanelContainer();
        _accountCreatePanel.Size = new Vector2(400, 420);
        _accountCreatePanel.Position = new Vector2(200, 90);
        _accountCreatePanel.Visible = false;
        _accountCreatePanel.ZIndex = 1;

        var bg = new StyleBoxFlat();
        bg.BgColor = new Color(0.08f, 0.08f, 0.14f, 0.95f);
        bg.BorderColor = new Color(0.4f, 0.35f, 0.2f);
        bg.SetBorderWidthAll(2);
        bg.SetContentMarginAll(12);
        _accountCreatePanel.AddThemeStyleboxOverride("panel", bg);

        var vbox = new VBoxContainer();
        vbox.AddThemeConstantOverride("separation", 6);
        _accountCreatePanel.AddChild(vbox);

        // Title
        var title = new Label();
        title.Text = "Crear Cuenta";
        title.HorizontalAlignment = HorizontalAlignment.Center;
        title.AddThemeColorOverride("font_color", new Color(0.9f, 0.8f, 0.4f));
        title.AddThemeFontSizeOverride("font_size", 16);
        ApplyFont(title);
        vbox.AddChild(title);

        // Account name
        var nameLabel = new Label();
        nameLabel.Text = "Nombre de cuenta:";
        nameLabel.AddThemeColorOverride("font_color", Colors.White);
        nameLabel.AddThemeFontSizeOverride("font_size", 11);
        vbox.AddChild(nameLabel);

        _acctNameInput = new LineEdit();
        _acctNameInput.PlaceholderText = "3-15 caracteres";
        _acctNameInput.MaxLength = 15;
        _acctNameInput.CustomMinimumSize = new Vector2(0, 28);
        _acctNameInput.AddThemeFontSizeOverride("font_size", 12);
        vbox.AddChild(_acctNameInput);

        // Password
        var passLabel = new Label();
        passLabel.Text = "Contraseña:";
        passLabel.AddThemeColorOverride("font_color", Colors.White);
        passLabel.AddThemeFontSizeOverride("font_size", 11);
        vbox.AddChild(passLabel);

        _acctPasswordInput = new LineEdit();
        _acctPasswordInput.PlaceholderText = "4-15 caracteres";
        _acctPasswordInput.MaxLength = 15;
        _acctPasswordInput.Secret = true;
        _acctPasswordInput.CustomMinimumSize = new Vector2(0, 28);
        _acctPasswordInput.AddThemeFontSizeOverride("font_size", 12);
        vbox.AddChild(_acctPasswordInput);

        // Confirm password
        var passConfirmLabel = new Label();
        passConfirmLabel.Text = "Repetir contraseña:";
        passConfirmLabel.AddThemeColorOverride("font_color", Colors.White);
        passConfirmLabel.AddThemeFontSizeOverride("font_size", 11);
        vbox.AddChild(passConfirmLabel);

        _acctPasswordConfirmInput = new LineEdit();
        _acctPasswordConfirmInput.MaxLength = 15;
        _acctPasswordConfirmInput.Secret = true;
        _acctPasswordConfirmInput.CustomMinimumSize = new Vector2(0, 28);
        _acctPasswordConfirmInput.AddThemeFontSizeOverride("font_size", 12);
        vbox.AddChild(_acctPasswordConfirmInput);

        // PIN
        var pinLabel = new Label();
        pinLabel.Text = "PIN:";
        pinLabel.AddThemeColorOverride("font_color", Colors.White);
        pinLabel.AddThemeFontSizeOverride("font_size", 11);
        vbox.AddChild(pinLabel);

        _acctPinInput = new LineEdit();
        _acctPinInput.PlaceholderText = "4-5 dígitos";
        _acctPinInput.MaxLength = 5;
        _acctPinInput.Secret = true;
        _acctPinInput.CustomMinimumSize = new Vector2(0, 28);
        _acctPinInput.AddThemeFontSizeOverride("font_size", 12);
        vbox.AddChild(_acctPinInput);

        // Confirm PIN
        var pinConfirmLabel = new Label();
        pinConfirmLabel.Text = "Repetir PIN:";
        pinConfirmLabel.AddThemeColorOverride("font_color", Colors.White);
        pinConfirmLabel.AddThemeFontSizeOverride("font_size", 11);
        vbox.AddChild(pinConfirmLabel);

        _acctPinConfirmInput = new LineEdit();
        _acctPinConfirmInput.MaxLength = 5;
        _acctPinConfirmInput.Secret = true;
        _acctPinConfirmInput.CustomMinimumSize = new Vector2(0, 28);
        _acctPinConfirmInput.AddThemeFontSizeOverride("font_size", 12);
        vbox.AddChild(_acctPinConfirmInput);

        // Error/status label
        _acctErrorLabel = new Label();
        _acctErrorLabel.Text = "";
        _acctErrorLabel.HorizontalAlignment = HorizontalAlignment.Center;
        _acctErrorLabel.AddThemeColorOverride("font_color", new Color(1f, 0.4f, 0.4f));
        _acctErrorLabel.AddThemeFontSizeOverride("font_size", 11);
        _acctErrorLabel.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        vbox.AddChild(_acctErrorLabel);

        // Buttons row
        var btnBox = new HBoxContainer();
        btnBox.AddThemeConstantOverride("separation", 8);
        btnBox.Alignment = BoxContainer.AlignmentMode.Center;
        vbox.AddChild(btnBox);

        _acctCreateButton = new Button();
        _acctCreateButton.Text = "Crear Cuenta";
        _acctCreateButton.CustomMinimumSize = new Vector2(140, 32);
        _acctCreateButton.Pressed += OnAccountCreatePressed;
        btnBox.AddChild(_acctCreateButton);

        _acctBackButton = new Button();
        _acctBackButton.Text = "Volver";
        _acctBackButton.CustomMinimumSize = new Vector2(100, 32);
        _acctBackButton.Pressed += OnAccountCreateBack;
        btnBox.AddChild(_acctBackButton);

        GetNode("UILayer").AddChild(_accountCreatePanel);
    }

    private void OnAccountCreatePressed()
    {
        string name = _acctNameInput!.Text.Trim();
        string pass = _acctPasswordInput!.Text;
        string passConfirm = _acctPasswordConfirmInput!.Text;
        string pin = _acctPinInput!.Text;
        string pinConfirm = _acctPinConfirmInput!.Text;

        // Validate account name
        if (name.Length < 3 || name.Length > 15)
        {
            _acctErrorLabel!.Text = "El nombre debe tener entre 3 y 15 caracteres.";
            return;
        }
        foreach (char c in name)
        {
            if (!char.IsLetterOrDigit(c))
            {
                _acctErrorLabel!.Text = "El nombre solo puede contener letras y números.";
                return;
            }
        }

        // Validate password
        if (pass.Length < 4 || pass.Length > 15)
        {
            _acctErrorLabel!.Text = "La contraseña debe tener entre 4 y 15 caracteres.";
            return;
        }
        if (pass != passConfirm)
        {
            _acctErrorLabel!.Text = "Las contraseñas no coinciden.";
            return;
        }

        // Validate PIN
        if (pin.Length < 4 || pin.Length > 5)
        {
            _acctErrorLabel!.Text = "El PIN debe tener 4 o 5 dígitos.";
            return;
        }
        foreach (char c in pin)
        {
            if (!char.IsDigit(c))
            {
                _acctErrorLabel!.Text = "El PIN solo puede contener dígitos.";
                return;
            }
        }
        if (pin != pinConfirm)
        {
            _acctErrorLabel!.Text = "Los PINs no coinciden.";
            return;
        }

        _acctCreateButton!.Disabled = true;
        _acctErrorLabel!.Text = "Conectando...";
        _acctErrorLabel.AddThemeColorOverride("font_color", new Color(0.8f, 0.8f, 0.8f));
        _acctSuccessTimer = 0;

        _state.CreateAccountName = name;
        _state.CreateAccountPassword = pass;
        _state.CreateAccountPin = pin;

        _ = ConnectAndCreateAccount(name, pass, pin);
    }

    private async Task ConnectAndCreateAccount(string account, string password, string pin)
    {
        try
        {
            // Dispose any existing connection
            _tcp?.Dispose();

            _tcp = new AoTcpClient();
            _packetHandler = new PacketHandler(_state);
            _packetHandler.OnMapLoad = LoadCurrentMap;
            _connecting = true;

            GD.Print($"[MAIN] Connecting for account creation...");
            await _tcp.ConnectAsync(ServerHost, ServerPort);
            _connecting = false;
            GD.Print("[MAIN] Connected! Sending NACCNT...");

            await Task.Delay(100);
            _tcp.SendPacket(ClientPackets.WriteKerd22());

            await Task.Delay(50);
            _tcp.SendPacket(ClientPackets.WriteNaccnt(account, password, pin));
            GD.Print("[MAIN] Sent: NACCNT (binary)");
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[MAIN] Account creation connection failed: {ex}");
            _connecting = false;
            _tcp?.Dispose();
            _tcp = null;
            _acctErrorLabel!.Text = FriendlyConnectionError(ex);
            _acctErrorLabel.AddThemeColorOverride("font_color", new Color(1f, 0.4f, 0.4f));
            _acctCreateButton!.Disabled = false;
        }
    }

    private void OnAccountCreateBack()
    {
        // Disconnect if we connected for account creation
        _tcp?.Dispose();
        _tcp = null;
        _packetHandler = null;
        _connecting = false;
        _acctSuccessTimer = 0;

        _state.CurrentScreen = Screen.Login;
        HandleScreenChange(Screen.Login);
        _lastScreen = Screen.Login;
    }

    private void ResetAccountCreateForm()
    {
        _acctNameInput!.Text = "";
        _acctPasswordInput!.Text = "";
        _acctPasswordConfirmInput!.Text = "";
        _acctPinInput!.Text = "";
        _acctPinConfirmInput!.Text = "";
        _acctErrorLabel!.Text = "";
        _acctErrorLabel.AddThemeColorOverride("font_color", new Color(1f, 0.4f, 0.4f));
        _acctCreateButton!.Disabled = false;
        _acctSuccessTimer = 0;
    }

    private void CreateCharCreatePanel()
    {
        _charCreatePanel = new PanelContainer();
        _charCreatePanel.Size = new Vector2(420, 520);
        // Center on screen (assume 800x600 base)
        _charCreatePanel.Position = new Vector2(190, 40);
        _charCreatePanel.Visible = false;

        var bg = new StyleBoxFlat();
        bg.BgColor = new Color(0.08f, 0.08f, 0.14f, 0.95f);
        bg.BorderColor = new Color(0.4f, 0.35f, 0.2f);
        bg.SetBorderWidthAll(2);
        bg.SetContentMarginAll(12);
        _charCreatePanel.AddThemeStyleboxOverride("panel", bg);

        var vbox = new VBoxContainer();
        vbox.AddThemeConstantOverride("separation", 6);
        _charCreatePanel.AddChild(vbox);

        // Title
        var title = new Label();
        title.Text = "Crear Personaje";
        title.HorizontalAlignment = HorizontalAlignment.Center;
        title.AddThemeColorOverride("font_color", new Color(0.9f, 0.8f, 0.4f));
        title.AddThemeFontSizeOverride("font_size", 16);
        ApplyFont(title);
        vbox.AddChild(title);

        // Name input
        var nameLabel = new Label();
        nameLabel.Text = "Nombre:";
        nameLabel.AddThemeColorOverride("font_color", Colors.White);
        nameLabel.AddThemeFontSizeOverride("font_size", 11);
        vbox.AddChild(nameLabel);

        _charCreateNameInput = new LineEdit();
        _charCreateNameInput.PlaceholderText = "4-15 caracteres";
        _charCreateNameInput.MaxLength = 15;
        _charCreateNameInput.CustomMinimumSize = new Vector2(0, 28);
        _charCreateNameInput.AddThemeFontSizeOverride("font_size", 12);
        vbox.AddChild(_charCreateNameInput);

        // Race selector
        AddSectionLabel(vbox, "Raza:");
        var raceBox = new HBoxContainer();
        raceBox.AddThemeConstantOverride("separation", 3);
        vbox.AddChild(raceBox);
        _raceButtons = CreateToggleGroup(raceBox, RaceNames, OnRaceSelected);

        // Gender selector
        AddSectionLabel(vbox, "Genero:");
        var genderBox = new HBoxContainer();
        genderBox.AddThemeConstantOverride("separation", 3);
        vbox.AddChild(genderBox);
        _genderButtons = CreateToggleGroup(genderBox, GenderNames, OnGenderSelected);

        // Class selector (2 rows of 4)
        AddSectionLabel(vbox, "Clase:");
        var classGrid = new GridContainer();
        classGrid.Columns = 4;
        classGrid.AddThemeConstantOverride("h_separation", 3);
        classGrid.AddThemeConstantOverride("v_separation", 3);
        vbox.AddChild(classGrid);
        _classButtons = CreateToggleGroup(classGrid, ClassNames, OnClassSelected);

        // Faction selector
        AddSectionLabel(vbox, "Faccion:");
        var factionBox = new HBoxContainer();
        factionBox.AddThemeConstantOverride("separation", 3);
        vbox.AddChild(factionBox);
        _factionButtons = CreateToggleGroup(factionBox, FactionNames, OnFactionSelected);

        // Head selector with preview
        AddSectionLabel(vbox, "Cabeza:");
        var headRow = new HBoxContainer();
        headRow.AddThemeConstantOverride("separation", 6);
        headRow.Alignment = BoxContainer.AlignmentMode.Center;
        vbox.AddChild(headRow);

        var headLeftBtn = new Button();
        headLeftBtn.Text = "<";
        headLeftBtn.CustomMinimumSize = new Vector2(32, 32);
        headLeftBtn.Pressed += OnHeadPrev;
        headRow.AddChild(headLeftBtn);

        // Head preview area — a SubViewportContainer with a small Node2D canvas
        var headPreviewContainer = new SubViewportContainer();
        headPreviewContainer.CustomMinimumSize = new Vector2(64, 64);
        headPreviewContainer.Stretch = true;
        headRow.AddChild(headPreviewContainer);

        var headViewport = new SubViewport();
        headViewport.Size = new Vector2I(64, 64);
        headViewport.TransparentBg = true;
        headViewport.RenderTargetUpdateMode = SubViewport.UpdateMode.Always;
        headPreviewContainer.AddChild(headViewport);

        _charCreateHeadPreview = new Node2D();
        _charCreateHeadPreview.Draw += DrawCharCreateHead;
        headViewport.AddChild(_charCreateHeadPreview);

        var headRightBtn = new Button();
        headRightBtn.Text = ">";
        headRightBtn.CustomMinimumSize = new Vector2(32, 32);
        headRightBtn.Pressed += OnHeadNext;
        headRow.AddChild(headRightBtn);

        _charCreateHeadLabel = new Label();
        _charCreateHeadLabel.Text = "1";
        _charCreateHeadLabel.AddThemeColorOverride("font_color", Colors.White);
        _charCreateHeadLabel.AddThemeFontSizeOverride("font_size", 11);
        headRow.AddChild(_charCreateHeadLabel);

        // Error label
        _charCreateError = new Label();
        _charCreateError.Text = "";
        _charCreateError.AddThemeColorOverride("font_color", new Color(1f, 0.3f, 0.3f));
        _charCreateError.AddThemeFontSizeOverride("font_size", 11);
        _charCreateError.HorizontalAlignment = HorizontalAlignment.Center;
        _charCreateError.AutowrapMode = TextServer.AutowrapMode.Word;
        vbox.AddChild(_charCreateError);

        // Buttons row
        var btnRow = new HBoxContainer();
        btnRow.AddThemeConstantOverride("separation", 8);
        btnRow.Alignment = BoxContainer.AlignmentMode.Center;
        vbox.AddChild(btnRow);

        _charCreateCreateBtn = new Button();
        _charCreateCreateBtn.Text = "Crear";
        _charCreateCreateBtn.CustomMinimumSize = new Vector2(100, 32);
        _charCreateCreateBtn.Pressed += OnCharCreateConfirm;
        btnRow.AddChild(_charCreateCreateBtn);

        var backBtn = new Button();
        backBtn.Text = "Volver";
        backBtn.CustomMinimumSize = new Vector2(100, 32);
        backBtn.Pressed += OnCharCreateBack;
        btnRow.AddChild(backBtn);

        GetNode("UILayer").AddChild(_charCreatePanel);
    }

    private static void AddSectionLabel(VBoxContainer vbox, string text)
    {
        var label = new Label();
        label.Text = text;
        label.AddThemeColorOverride("font_color", new Color(0.7f, 0.7f, 0.7f));
        label.AddThemeFontSizeOverride("font_size", 10);
        vbox.AddChild(label);
    }

    private static Button[] CreateToggleGroup(Container parent, string[] labels, Action<int> onSelected)
    {
        var buttons = new Button[labels.Length];
        for (int i = 0; i < labels.Length; i++)
        {
            int idx = i;
            var btn = new Button();
            btn.Text = labels[i];
            btn.ToggleMode = true;
            btn.CustomMinimumSize = new Vector2(0, 26);
            btn.AddThemeFontSizeOverride("font_size", 10);
            btn.Pressed += () => onSelected(idx);
            parent.AddChild(btn);
            buttons[i] = btn;
        }
        return buttons;
    }

    private static void SetToggleSelection(Button[] buttons, int selectedIndex)
    {
        for (int i = 0; i < buttons.Length; i++)
            buttons[i].ButtonPressed = (i == selectedIndex);
    }

    private void ResetCharCreateForm()
    {
        _state.CreateCharName = "";
        _state.CreateCharRace = 1;
        _state.CreateCharGender = 1;
        _state.CreateCharClass = 1;
        _state.CreateCharFaction = 1;
        _charCreateNameInput!.Text = "";
        _charCreateError!.Text = "";
        SetToggleSelection(_raceButtons!, 0);
        SetToggleSelection(_genderButtons!, 0);
        SetToggleSelection(_classButtons!, 0);
        SetToggleSelection(_factionButtons!, 0);
        UpdateHeadRange();
    }

    private void UpdateHeadRange()
    {
        var (min, max) = GetHeadRange(_state.CreateCharRace, _state.CreateCharGender);
        _state.CreateCharHeadMin = min;
        _state.CreateCharHeadMax = max;
        _state.CreateCharHead = min;
        UpdateHeadPreview();
    }

    private void UpdateHeadPreview()
    {
        if (_charCreateHeadLabel != null)
            _charCreateHeadLabel.Text = _state.CreateCharHead.ToString();

        // Render head sprite in preview
        if (_charCreateHeadPreview == null || _gameData == null) return;

        // Clear previous draw by queuing redraw (Node2D uses _Draw)
        _charCreateHeadPreview.QueueRedraw();
    }

    /// <summary>
    /// Called every frame to draw the head preview in the char create panel.
    /// We connect to _charCreateHeadPreview's Draw signal instead.
    /// </summary>
    private void DrawCharCreateHead()
    {
        if (_charCreateHeadPreview == null) return;
        int headIdx = _state.CreateCharHead;
        if (headIdx <= 0 || headIdx >= _gameData.Heads.Length) return;

        var head = _gameData.Heads[headIdx];
        // Draw south-facing (heading 3)
        if (head.Head[3] == 0) return;

        // Draw centered in 64x64 viewport
        CharRenderer.DrawGrh(_charCreateHeadPreview, _gameData, head.Head[3], 0,
            new Vector2(32, 32), true);
    }

    private void OnRaceSelected(int idx)
    {
        _state.CreateCharRace = idx + 1;
        SetToggleSelection(_raceButtons!, idx);
        UpdateHeadRange();
    }

    private void OnGenderSelected(int idx)
    {
        _state.CreateCharGender = idx + 1;
        SetToggleSelection(_genderButtons!, idx);
        UpdateHeadRange();
    }

    private void OnClassSelected(int idx)
    {
        _state.CreateCharClass = idx + 1;
        SetToggleSelection(_classButtons!, idx);
    }

    private void OnFactionSelected(int idx)
    {
        _state.CreateCharFaction = idx + 1;
        SetToggleSelection(_factionButtons!, idx);
    }

    private void OnHeadPrev()
    {
        if (_state.CreateCharHead > _state.CreateCharHeadMin)
            _state.CreateCharHead--;
        else
            _state.CreateCharHead = _state.CreateCharHeadMax;
        UpdateHeadPreview();
    }

    private void OnHeadNext()
    {
        if (_state.CreateCharHead < _state.CreateCharHeadMax)
            _state.CreateCharHead++;
        else
            _state.CreateCharHead = _state.CreateCharHeadMin;
        UpdateHeadPreview();
    }

    private void OnCharSelectCreatePressed()
    {
        _state.CurrentScreen = Screen.CharCreate;
        HandleScreenChange(Screen.CharCreate);
        _lastScreen = Screen.CharCreate;
    }

    private void OnCharCreateBack()
    {
        _state.CurrentScreen = Screen.CharSelect;
        HandleScreenChange(Screen.CharSelect);
        _lastScreen = Screen.CharSelect;
    }

    private void CreateDeleteConfirmDialog()
    {
        _deleteConfirmDialog = new PanelContainer();
        _deleteConfirmDialog.Size = new Vector2(280, 140);
        _deleteConfirmDialog.Position = new Vector2(127, 258);
        _deleteConfirmDialog.Visible = false;
        _deleteConfirmDialog.ZIndex = 100;

        var bg = new StyleBoxFlat();
        bg.BgColor = new Color(0.15f, 0.08f, 0.08f, 0.95f);
        bg.BorderColor = new Color(0.8f, 0.2f, 0.2f);
        bg.SetBorderWidthAll(1);
        bg.SetContentMarginAll(10);
        _deleteConfirmDialog.AddThemeStyleboxOverride("panel", bg);

        var vbox = new VBoxContainer();
        vbox.AddThemeConstantOverride("separation", 6);
        _deleteConfirmDialog.AddChild(vbox);

        _deleteConfirmLabel = new Label();
        _deleteConfirmLabel.Text = "";
        _deleteConfirmLabel.HorizontalAlignment = HorizontalAlignment.Center;
        _deleteConfirmLabel.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _deleteConfirmLabel.AddThemeColorOverride("font_color", Colors.White);
        _deleteConfirmLabel.AddThemeFontSizeOverride("font_size", 12);
        vbox.AddChild(_deleteConfirmLabel);

        _deleteConfirmInput = new LineEdit();
        _deleteConfirmInput.PlaceholderText = "Codigo";
        _deleteConfirmInput.Alignment = HorizontalAlignment.Center;
        _deleteConfirmInput.FocusMode = Control.FocusModeEnum.Click;
        _deleteConfirmInput.AddThemeFontSizeOverride("font_size", 12);
        _deleteConfirmInput.TextSubmitted += (_) => OnDeleteConfirm();
        vbox.AddChild(_deleteConfirmInput);

        var hbox = new HBoxContainer();
        hbox.AddThemeConstantOverride("separation", 6);
        hbox.Alignment = BoxContainer.AlignmentMode.Center;
        vbox.AddChild(hbox);

        var confirmBtn = new Button();
        confirmBtn.Text = "Confirmar";
        confirmBtn.CustomMinimumSize = new Vector2(80, 28);
        confirmBtn.AddThemeFontSizeOverride("font_size", 11);
        confirmBtn.Pressed += OnDeleteConfirm;
        hbox.AddChild(confirmBtn);

        var cancelBtn = new Button();
        cancelBtn.Text = "Cancelar";
        cancelBtn.CustomMinimumSize = new Vector2(80, 28);
        cancelBtn.AddThemeFontSizeOverride("font_size", 11);
        cancelBtn.Pressed += OnDeleteConfirmCancel;
        hbox.AddChild(cancelBtn);

        GetNode<CanvasLayer>("UILayer").AddChild(_deleteConfirmDialog);
    }

    private void OnDeleteCharPressed()
    {
        if (!_charList!.IsAnythingSelected())
        {
            _noticeLabel!.Text = "Seleccione un personaje";
            return;
        }

        var rng = new Random();
        _deleteConfirmCode = rng.Next(1000, 10000);
        _deleteConfirmLabel!.Text = $"Esta accion no podra ser revertida.\nIngresa el codigo {_deleteConfirmCode} para confirmar.";
        _deleteConfirmInput!.Text = "";
        _deleteConfirmDialog!.Visible = true;
        _deleteConfirmInput.GrabFocus();
    }

    private void OnDeleteConfirm()
    {
        if (_deleteConfirmInput == null || _tcp == null) return;

        if (!int.TryParse(_deleteConfirmInput.Text.Trim(), out int inputCode) || inputCode != _deleteConfirmCode)
        {
            _deleteConfirmLabel!.Text = $"Codigo incorrecto. Ingresa {_deleteConfirmCode} para confirmar.";
            _deleteConfirmLabel.AddThemeColorOverride("font_color", new Color(1f, 0.4f, 0.4f));
            _deleteConfirmInput.Text = "";
            return;
        }

        int[] selected = _charList!.GetSelectedItems();
        if (selected.Length == 0 || selected[0] >= _state.CharacterList.Count) return;

        string charName = _state.CharacterList[selected[0]].Name;
        string account = _state.AccountName;
        string code = _state.SecurityCode;

        _tcp.SendPacket(ClientPackets.WriteTbrp(charName));
        GD.Print($"[MAIN] Sent: TBRP {charName}");

        _deleteConfirmDialog!.Visible = false;
        _noticeLabel!.Text = "Eliminando personaje...";
    }

    private void OnDeleteConfirmCancel()
    {
        _deleteConfirmDialog!.Visible = false;
        _deleteConfirmLabel!.AddThemeColorOverride("font_color", Colors.White);
    }

    private void OnCharCreateConfirm()
    {
        // Validate name
        string name = _charCreateNameInput!.Text.Trim();
        if (name.Length < 4 || name.Length > 15)
        {
            _charCreateError!.Text = "El nombre debe tener entre 4 y 15 caracteres.";
            return;
        }

        // Only letters and single spaces
        bool lastWasSpace = false;
        foreach (char c in name)
        {
            if (c == ' ')
            {
                if (lastWasSpace)
                {
                    _charCreateError!.Text = "El nombre no puede tener espacios consecutivos.";
                    return;
                }
                lastWasSpace = true;
            }
            else if (!char.IsLetter(c))
            {
                _charCreateError!.Text = "El nombre solo puede contener letras y espacios.";
                return;
            }
            else
            {
                lastWasSpace = false;
            }
        }

        if (name.StartsWith(' ') || name.EndsWith(' '))
        {
            _charCreateError!.Text = "El nombre no puede empezar o terminar con espacio.";
            return;
        }

        _charCreateError!.Text = "";
        _charCreateCreateBtn!.Disabled = true;

        // Build NLOGIN packet: NLOGIN{name},{race},{gender},{gender},{class},1,{account},{head},{faction}
        string raceName = RaceNames[_state.CreateCharRace - 1];
        string genderName = GenderNames[_state.CreateCharGender - 1];
        string className = ClassNames[_state.CreateCharClass - 1];
        int head = _state.CreateCharHead;
        string account = _state.AccountName;

        _tcp!.SendPacket(ClientPackets.WriteNlogin(
            name, (byte)_state.CreateCharRace, (byte)_state.CreateCharGender,
            (byte)_state.CreateCharClass, (short)head, (byte)_state.CreateCharFaction, account));
        GD.Print($"[MAIN] Sent: NLOGIN {name} (binary)");
    }


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
                _statusLabel!.Text = "Error: El servidor cerró la conexión.";
                _connectButton!.Disabled = false;
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
                _statusLabel!.Text = _state.LoginError;
                _connectButton!.Disabled = false;
            }
            else if (_state.CurrentScreen == Screen.CharCreate)
            {
                _charCreateError!.Text = _state.LoginError;
                _charCreateCreateBtn!.Disabled = false;
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
                    _acctErrorLabel!.Text = msg;
                    _acctErrorLabel.AddThemeColorOverride("font_color", new Color(0.4f, 1f, 0.4f));
                    _acctSuccessTimer = 2.0;
                }
                else
                {
                    _acctErrorLabel!.Text = msg;
                    _acctErrorLabel.AddThemeColorOverride("font_color", new Color(1f, 0.4f, 0.4f));
                    _acctCreateButton!.Disabled = false;
                }
            }
            _state.LoginError = "";
        }

        // Show Mensaje dialog (VB6: Mensaje form — ERR, ERO, !! packets)
        if (!string.IsNullOrEmpty(_state.MensajeText))
        {
            ShowMensaje(_state.MensajeText);
            _state.MensajeText = "";
        }

        // Dismiss Mensaje dialog with Enter key
        if (_mensajeDialog != null && _mensajeDialog.Visible)
        {
            if (Input.IsActionJustPressed("ui_accept"))
                OnMensajeAccept();
        }

        // Account creation success → auto-switch to login after timer
        if (_acctSuccessTimer > 0 && _state.CurrentScreen == Screen.AccountCreate)
        {
            _acctSuccessTimer -= delta;
            if (_acctSuccessTimer <= 0)
            {
                _tcp?.Dispose();
                _tcp = null;
                _packetHandler = null;
                _connecting = false;

                _state.CurrentScreen = Screen.Login;
                HandleScreenChange(Screen.Login);
                _lastScreen = Screen.Login;
                _statusLabel!.Text = "Cuenta creada. Ingrese sus datos.";
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
            UpdateConsoleMessages();

            // Update floating tooltip position each frame
            _tooltipPanel?.UpdatePosition();

            // Commerce panel state tracking
            if (_state.Comerciando != _lastComerciando)
            {
                _lastComerciando = _state.Comerciando;
                if (_state.Comerciando)
                {
                    _commercePanel?.OpenShop();
                    _soundManager?.PlaySound(SoundManager.SND_PANELOPEN);
                }
                else
                {
                    _commercePanel?.CloseShop();
                    _soundManager?.PlaySound(SoundManager.SND_PANELCLOSE);
                }
            }

            // Trade panel state tracking (player-to-player)
            if (_state.Trading != _lastTrading)
            {
                _lastTrading = _state.Trading;
                if (_state.Trading)
                {
                    _tradePanel?.OpenTrade();
                    _soundManager?.PlaySound(SoundManager.SND_PANELOPEN);
                }
                else
                {
                    _tradePanel?.CloseTrade();
                    _soundManager?.PlaySound(SoundManager.SND_PANELCLOSE);
                }
            }
            // Consume TradeJustOpened flag (set by PacketHandler on trade init)
            if (_state.TradeJustOpened)
            {
                _state.TradeJustOpened = false;
                _tradePanel?.OpenTrade();
                _soundManager?.PlaySound(SoundManager.SND_PANELOPEN);
            }

            // Bank panel state tracking
            if (_state.Banqueando != _lastBanqueando)
            {
                _lastBanqueando = _state.Banqueando;
                if (_state.Banqueando)
                {
                    // Only open BankPanel if vault isn't already open
                    if (!_state.BovedaAbierta)
                    {
                        _bankPanel?.OpenBank();
                        _soundManager?.PlaySound(SoundManager.SND_PANELOPEN);
                    }
                }
                else
                {
                    _bankPanel?.CloseBank();
                    _vaultPanel?.CloseVault();
                    _state.BovedaAbierta = false;
                    _soundManager?.PlaySound(SoundManager.SND_PANELCLOSE);
                }
            }

            // Travel panel state tracking
            if (_state.ShowTravelPanel)
            {
                _state.ShowTravelPanel = false;
                _travelPanel?.OpenTravel();
            }

            // Guild panel — open standalone clan panel
            if (_state.ShowGuildPanel)
            {
                _state.ShowGuildPanel = false;
                string viewType = string.IsNullOrEmpty(_state.GuildInfoType) ? "List" : _state.GuildInfoType;
                _guildPanel?.ShowView(viewType);
            }
            if (_state.ShowGuildFoundation)
            {
                _state.ShowGuildFoundation = false;
                _optionsPanel?.Close();
                _guildFoundationPanel?.Show();
            }

            // Forum panel — open from ShowForumForm packet
            if (_state.ShowForumPanel)
            {
                _state.ShowForumPanel = false;
                _forumPanel?.ShowForum();
            }

            // Friend list panel — open from FriendList/FriendDialog packet
            if (_state.ShowFriendListPanel)
            {
                _state.ShowFriendListPanel = false;
                _friendListPanel?.ShowPanel();
            }

            // Mail panel — open from MailList/MailOpenTrigger packet
            if (_state.ShowMailPanel)
            {
                _state.ShowMailPanel = false;
                _mailPanel?.ShowPanel();
            }

            // Party panel — open from ShowPartyForm packet
            if (_state.ShowPartyPanel)
            {
                _state.ShowPartyPanel = false;
                _partyPanel?.OpenPanel();
            }

            // Guild bank panel
            if (_state.ShowGuildBank)
            {
                _state.ShowGuildBank = false;
                _guildBankPanel?.OpenGuildBank();
            }

            // Craft panels (blacksmith / carpenter)
            if (_state.ShowBlacksmithForm)
            {
                _state.ShowBlacksmithForm = false;
                _craftPanel?.ShowBlacksmith();
            }
            if (_state.ShowCarpenterForm)
            {
                _state.ShowCarpenterForm = false;
                _craftPanel?.ShowCarpenter();
            }

            // Quest panel — handle quest data from server
            if (!string.IsNullOrEmpty(_state.QuestDataTag))
            {
                string tag = _state.QuestDataTag;
                string payload = _state.QuestDataPayload;
                _state.QuestDataTag = "";
                _state.QuestDataPayload = "";
                _questPanel?.HandleQuestData(tag, payload);
            }
            if (_state.ShowQuestPanel)
            {
                _state.ShowQuestPanel = false;
                _questPanel?.OpenPanel();
            }

            // Trainer panel — open from TrainerCreatureList packet
            if (_state.ShowTrainerPanel)
            {
                _state.ShowTrainerPanel = false;
                string creatures = _state.TrainerCreatureData;
                _state.TrainerCreatureData = "";
                _trainerPanel?.OpenTrainer(creatures);
            }

            // NPC dialog panel — show when NPC speaks via ChatOverHead
            if (_state.ShowNpcDialog)
            {
                _state.ShowNpcDialog = false;
                _npcDialogPanel?.ShowDialog(_state.NpcDialogName, _state.NpcDialogText);
            }

            // Character info popup — show from FullCharInfo packet (/MIRAR)
            if (_state.ShowCharInfo)
            {
                _state.ShowCharInfo = false;
                if (_state.CharInfoCurrent != null)
                    _charInfoPopup?.ShowInfo(_state.CharInfoCurrent);
            }

            // Blind screen overlay — smooth fade in/out
            if (_blindOverlay != null)
            {
                float targetAlpha = _state.UserBlind ? 0.95f : 0.0f;
                float fadeSpeed = (float)delta * 3.3f; // ~0.3s transition
                _blindAlpha = Mathf.MoveToward(_blindAlpha, targetAlpha, fadeSpeed);
                _blindOverlay.Color = new Color(0, 0, 0, _blindAlpha);
            }

            // Death panel — show when player dies, hide on revive
            if (_state.ShowDeathPanel)
            {
                _state.ShowDeathPanel = false;
                if (_state.Config?.ShowDeathDialog ?? true)
                    _deathPanel?.Show();
            }
            if (!_state.Dead && _deathPanel != null && _deathPanel.Visible)
            {
                _deathPanel.Hide();
            }

            // Drop quantity dialog (VB6: frmCantidad)
            if (_state.DropDialogOpen && _dropDialog != null && !_dropDialog.Visible)
            {
                int slot = _state.DropDialogSlot;
                string itemName = (slot >= 0 && slot < 25) ? _state.Inventory[slot].Name : "item";
                _dropDialogLabel!.Text = $"Tirar: {itemName}";
                _dropDialogInput!.Text = "1";
                _dropDialog.Visible = true;
                _dropDialogInput.GrabFocus();
                _dropDialogInput.SelectAll();
            }
        }

        // Movement update AFTER input (VB6: ShowNextFrame after CheckKeys)
        UpdateMovement((float)delta);
    }

    private void HandleScreenChange(Screen newScreen)
    {
        GD.Print($"[MAIN] Screen → {newScreen}");

        // Always hide delete confirm dialog on screen change
        if (_deleteConfirmDialog != null) _deleteConfirmDialog.Visible = false;

        switch (newScreen)
        {
            case Screen.Login:
                _loginPanel!.Visible = true;
                _charSelectPanel!.Visible = false;
                _charCreatePanel!.Visible = false;
                _accountCreatePanel!.Visible = false;
                _gameUI!.Visible = false;
                break;

            case Screen.CharSelect:
                _loginPanel!.Visible = false;
                _charSelectPanel!.Visible = true;
                _charCreatePanel!.Visible = false;
                _accountCreatePanel!.Visible = false;
                _gameUI!.Visible = false;
                _enterButton!.Disabled = false;
                _noticeLabel!.Text = "";
                PopulateCharList();
                break;

            case Screen.AccountCreate:
                _loginPanel!.Visible = false;
                _charSelectPanel!.Visible = false;
                _charCreatePanel!.Visible = false;
                _accountCreatePanel!.Visible = true;
                _gameUI!.Visible = false;
                ResetAccountCreateForm();
                break;

            case Screen.CharCreate:
                _loginPanel!.Visible = false;
                _charSelectPanel!.Visible = false;
                _charCreatePanel!.Visible = true;
                _accountCreatePanel!.Visible = false;
                _gameUI!.Visible = false;
                ResetCharCreateForm();
                break;

            case Screen.Game:
                _loginPanel!.Visible = false;
                _charSelectPanel!.Visible = false;
                _charCreatePanel!.Visible = false;
                _accountCreatePanel!.Visible = false;
                _gameUI!.Visible = true;
                // Initialize inventory/spell panels with TCP (only available after connect)
                if (_tcp != null)
                {
                    _inventoryPanel!.Init(_state, _gameData, _tcp);
                    _inventoryPanel!.OnDropOutside += OnInventoryDropOutside;
                    _spellPanel!.Init(_state, _gameData, _tcp);
                    _commercePanel!.Init(_state, _gameData, _tcp);
                    _tradePanel!.Init(_state, _gameData, _tcp);
                    _bankPanel!.Init(_state, _gameData, _tcp);
                    _vaultPanel!.Init(_state, _gameData, _tcp);
                    _guildBankPanel!.Init(_state, _gameData, _tcp);
                    _craftPanel!.Init(_state, _gameData, _tcp);
                    _travelPanel!.Init(_state, _tcp, _dataPath);
                    _deathPanel!.Init(_state, _tcp, _dataPath);
                    _guildPanel!.Init(_state, _tcp);
                    _guildFoundationPanel!.Init(_state, _tcp);
                    _forumPanel!.Init(_state, _tcp);
                    _friendListPanel!.Init(_state, _tcp);
                    _mailPanel!.Init(_state, _tcp);
                    _partyPanel!.Init(_state, _tcp);
                    _questPanel!.Init(_state, _tcp);
                    _trainerPanel!.Init(_state, _tcp);
                    _optionsPanel!.Init(_state, _state.Config, _dataPath, _tcp);
                    _statsPanel!.Init(_state, _tcp);
                    _charInfoPopup!.Init(_state);
                    _contextMenu!.Init(_state, _tcp);
                    _contextMenu!.OnWhisper += (name) =>
                    {
                        // Activate chat in whisper mode
                        _state.ChatMode = 7;
                        _state.ChatModePrefix = "\\";
                        _state.WhisperTarget = name;
                        if (_chatInput != null)
                        {
                            _chatInput.Visible = true;
                            _chatInput.GrabFocus();
                            _state.ChatActive = true;
                        }
                    };
                    _contextMenu!.OnAddFriend += (name) =>
                    {
                        _tcp.SendPacket(ClientPackets.WriteTalk($"/AGREGAR {name}"));
                    };
                }
                GD.Print("[MAIN] Entered game world");
                break;
        }
    }

    /// <summary>
    /// Apply GameConfig values to all subsystems (called after options are saved).
    /// </summary>
    private void ApplyConfigToSystems()
    {
        var cfg = _state.Config;

        // Sync ShowNames to GameState (used by CharRenderer directly)
        _state.ShowNames = cfg.ShowNames;

        // Apply audio settings
        if (_soundManager != null)
        {
            _soundManager.MusicEnabled = cfg.MusicEnabled;
            _soundManager.SoundEnabled = cfg.SfxEnabled;
            _soundManager.SetMusicVolume(cfg.MusicVolume);
            _soundManager.SetSfxVolume(cfg.SfxVolume);
        }

        // Apply V-Sync
        DisplayServer.WindowSetVsyncMode(
            cfg.VsyncEnabled ? DisplayServer.VSyncMode.Enabled : DisplayServer.VSyncMode.Disabled);

        // Apply FPS limit
        Engine.MaxFps = cfg.FpsLimit > 0 ? cfg.FpsLimit : 0;

        // Apply display mode
        if (cfg.Fullscreen)
        {
            GetTree().Root.ContentScaleAspect = Window.ContentScaleAspectEnum.Keep;
            DisplayServer.WindowSetMode(DisplayServer.WindowMode.Fullscreen);
        }
        else
        {
            DisplayServer.WindowSetFlag(DisplayServer.WindowFlags.ResizeDisabled, false);
            DisplayServer.WindowSetMode(DisplayServer.WindowMode.Windowed);
            var winSize = new Vector2I(800, 600);
            DisplayServer.WindowSetSize(winSize);
            var screenSize = DisplayServer.ScreenGetSize();
            DisplayServer.WindowSetPosition(new Vector2I(
                (screenSize.X - winSize.X) / 2,
                (screenSize.Y - winSize.Y) / 2));
            GetTree().Root.ContentScaleAspect = Window.ContentScaleAspectEnum.Keep;
        }

        GD.Print($"[CFG] Applied config: VSync={cfg.VsyncEnabled}, FPS={cfg.FpsLimit}, Music={cfg.MusicEnabled}, Fullscreen={cfg.Fullscreen}, Aspect={cfg.AspectRatioMode}");
    }

    /// <summary>
    /// VB6 Socket1_Disconnect: clean up everything and return to login.
    /// Called when TCP connection is lost (server close, error, timeout).
    /// </summary>
    private void HandleDisconnect(string message)
    {
        GD.Print($"[MAIN] Disconnect: {message}");

        // Clean up TCP resources (VB6: Socket1.Disconnect + Socket1.Cleanup)
        _tcp?.Dispose();
        _tcp = null;
        _packetHandler = null;
        _inputHandler = null;
        _connecting = false;
        _packetCount = 0;

        // Reset all game state (VB6: clear logged, skills, attributes, etc.)
        ResetGameState();

        // Hide chat input if visible
        if (_chatInput != null)
        {
            _chatInput.Visible = false;
            _chatInput.Text = "";
            _chatInput.ReleaseFocus();
        }

        // Clear console
        _console?.Clear();

        // Clear minimap
        // Close escape menu
        HideEscapeMenu();

        // Close commerce, bank, guild, and travel panels
        _commercePanel?.CloseShop();
        _lastComerciando = false;
        _tradePanel?.CloseTrade();
        _lastTrading = false;
        _bankPanel?.CloseBank();
        _vaultPanel?.CloseVault();
        _guildBankPanel?.CloseGuildBank();
        _craftPanel?.ClosePanel();
        _guildPanel?.Hide();
        _guildFoundationPanel?.Hide();
        _friendListPanel?.Hide();
        _mailPanel?.Hide();
        _partyPanel?.Hide();
        _questPanel?.Hide();
        _trainerPanel?.Hide();
        _lastBanqueando = false;
        _travelPanel?.CloseTravel();
        _deathPanel?.Hide();
        CloseDropDialog();
        _blindAlpha = 0;
        if (_blindOverlay != null) _blindOverlay.Color = new Color(0, 0, 0, 0);

        // Reset char create button state
        if (_charCreateCreateBtn != null)
            _charCreateCreateBtn.Disabled = false;

        // Reset account create state
        if (_acctCreateButton != null)
            _acctCreateButton.Disabled = false;
        _acctSuccessTimer = 0;
        if (_accountCreatePanel != null)
            _accountCreatePanel.Visible = false;

        // Reset spell/inventory tab to default (inventory)
        OnInventoryTabPressed();

        // Switch to login screen with error message
        _state.CurrentScreen = Screen.Login;
        HandleScreenChange(Screen.Login);
        _lastScreen = Screen.Login;
        _statusLabel!.Text = message;
        _connectButton!.Disabled = false;

        // VB6: frmConnect.MousePointer = 1 (normal cursor)
        Input.SetDefaultCursorShape(Input.CursorShape.Arrow);
    }

    /// <summary>
    /// Reset GameState to defaults (VB6: Socket1_Disconnect cleanup).
    /// Clears all character data, stats, inventory, spells, flags.
    /// </summary>
    private void ResetGameState()
    {
        _state.IsLogged = false;
        _state.Paused = false;
        _state.LoginError = "";
        _state.ServerNotice = "";
        _state.SecurityCode = "";
        _state.CharacterList.Clear();
        _state.SelectedCharIndex = -1;

        // Map
        _state.CurrentMap = 0;
        _state.MapName = "";
        _state.MapColorR = 200;
        _state.MapColorG = 200;
        _state.MapColorB = 200;
        _state.MapData = null;
        _state.NeedMapLoad = false;

        // Position & movement
        _state.UserPosX = 0;
        _state.UserPosY = 0;
        _state.UserCharIndex = 0;
        _state.UserName = "";
        _state.UserParalyzed = false;
        _state.ParalysisTimer = 0;
        _state.UserNavigating = false;
        _state.UserStopped = false;
        _state.UsingSkill = 0;
        _state.ChatActive = false;
        _state.Comerciando = false;
        _state.Trading = false;
        _state.Dead = false;
        _state.ShowDeathPanel = false;
        _state.Resting = false;
        _state.Meditating = false;
        _state.SafeMode = false;
        _state.ItemSafety = true; // Re-enable on reconnect (VB6: ISItem starts true)
        _state.SeguroResu = false;
        _state.DropDialogOpen = false;
        _state.ShowTravelPanel = false;
        _state.UserMoving = false;
        _state.AddToUserPosX = 0;
        _state.AddToUserPosY = 0;
        _state.ScreenOffsetX = 0;
        _state.ScreenOffsetY = 0;
        _state.PtCooldownFrames = 0;
        _state.PendingMoves = 0;

        // Characters & objects
        _state.Characters.Clear();
        _state.GroundObjects.Clear();

        // Stats
        _state.MaxHp = 0; _state.MinHp = 0;
        _state.MaxMana = 0; _state.MinMana = 0;
        _state.MaxSta = 0; _state.MinSta = 0;
        _state.MaxAgua = 0; _state.MinAgua = 0;
        _state.MaxHam = 0; _state.MinHam = 0;
        _state.Gold = 0;
        _state.Level = 0;
        _state.Exp = 0; _state.ExpNext = 0;
        _state.Reputation = 0;
        _state.Privileges = 0;
        _state.MusicId = 0;
        _state.OnlineCount = 0;
        _state.Strength = 0;
        _state.Agility = 0;
        _state.AttackMin = 0; _state.AttackMax = 0;
        _state.DefenseMin = 0; _state.DefenseMax = 0;
        _state.MagDefMin = 0; _state.MagDefMax = 0;
        _state.WeaponEqpSlot = 0; _state.ArmourEqpSlot = 0;
        _state.ShieldEqpSlot = 0; _state.HelmEqpSlot = 0;
        _state.WeaponLabel = "0/0"; _state.ArmourLabel = "0/0";
        _state.ShieldLabel = "0/0"; _state.HelmLabel = "0/0";

        // Inventory & spells
        for (int i = 0; i < 25; i++)
            _state.Inventory[i] = new InventorySlot();
        for (int i = 0; i < 20; i++)
            _state.Spells[i] = new SpellSlot();

        // Commerce
        _state.NpcShopCount = 0;
        for (int i = 0; i < 50; i++)
            _state.NpcShopItems[i] = new NpcShopItem();

        // Bank
        _state.BankItemCount = 0;
        _state.BankGold = 0;
        _state.Banqueando = false;
        _state.BovedaAbierta = false;

        // Guild Bank
        _state.ShowGuildBank = false;
        _state.GuildBankGold = 0;
        _state.GuildBankCanObj = false;
        _state.GuildBankCanGold = false;
        for (int i = 0; i < _state.GuildBankItems.Length; i++)
            _state.GuildBankItems[i] = new GuildBankSlot();
        for (int i = 0; i < 40; i++)
            _state.BankItems[i] = new BankItem();

        // Chat queue and history
        _state.ChatMessages.Clear();
        _state.ChatHistory.Clear();
        _state.ActiveChatFilter = -1;
        _state.ChatFilterDirty = false;
        UpdateChatTabHighlight();
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

        _expLabel!.Text = $"{_state.Exp}/{_state.ExpNext}";
        _goldLabel!.Text = _state.Gold.ToString("N0", System.Globalization.CultureInfo.InvariantCulture).Replace(",", ".");
        _levelLabel!.Text = $"{_state.Level}";
        _nameLabel!.Text = _state.UserName;
        _onlineLabel!.Text = $"{_state.OnlineCount}";
        // VB6: Coord.Caption = NombreMapa & " (" & Map & "," & X & "," & Y & ")"
        _coordsLabel!.Text = $"{_state.MapName} ({_state.CurrentMap},{_state.UserPosX},{_state.UserPosY})";

        // GM button visibility
        if (_btnCastiGM != null) _btnCastiGM.Visible = _state.Privileges >= 1;

        // Combat stat labels
        // VB6: Per-equipment bottom bar labels (updated on ChangeInventorySlot)
        _armorLabel!.Text = _state.ArmourLabel;
        _helmLabel!.Text = _state.HelmLabel;
        _shieldLabel!.Text = _state.ShieldLabel;
        _weaponLabel!.Text = _state.WeaponLabel;
        _fuerzaLabel!.Text = $"{_state.Strength}";
        _agilidadLabel!.Text = $"{_state.Agility}";

        // Reputation: negative = red with "- " prefix, positive = white
        if (_state.Reputation < 0)
        {
            _repLabel!.Text = $"- {Math.Abs(_state.Reputation)}";
            _repLabel.AddThemeColorOverride("font_color", new Color(1, 0, 0));
        }
        else
        {
            _repLabel!.Text = $"{_state.Reputation}";
            _repLabel.AddThemeColorOverride("font_color", Colors.White);
        }

        // FPS
        _fpsLabel!.Text = $"{Engine.GetFramesPerSecond()}";

        // Macro status indicator
        if (_macroStatusLabel != null)
        {
            bool workActive = _state.WorkMacro.Active;
            bool spellActive = _state.SpellMacro.Active;
            if (workActive || spellActive)
            {
                _macroStatusLabel.Text = workActive ? "MACRO" : "MACROSP";
                _macroStatusLabel.Visible = true;
            }
            else
            {
                _macroStatusLabel.Visible = false;
            }
        }

        // Update minimap party member names from PartyPanel
        if (_minimapPanel != null && _partyPanel != null)
        {
            _minimapPanel.PartyMemberNames.Clear();
            foreach (var m in _partyPanel.Members)
            {
                if (!string.IsNullOrEmpty(m.Name))
                    _minimapPanel.PartyMemberNames.Add(m.Name);
            }
        }
    }

    /// <summary>
    /// <summary>
    /// Move active arrows toward targets and remove on arrival.
    /// VB6: arrows fly from shooter to target tile, rendered as a GRH.
    /// </summary>
    private void UpdateArrowProjectiles(float delta)
    {
        if (_state.ActiveArrows.Count == 0) return;
        float pixelsPerSec = 320f; // ~10 tiles/sec at 32px/tile
        for (int i = _state.ActiveArrows.Count - 1; i >= 0; i--)
        {
            var a = _state.ActiveArrows[i];
            if (!a.Active) { _state.ActiveArrows.RemoveAt(i); continue; }

            // Update target position from live character data (target may be moving)
            if (_state.Characters.TryGetValue((short)a.TargetCharIndex, out var tgt))
            {
                a.TargetX = tgt.PosX * 32f + 16f;
                a.TargetY = tgt.PosY * 32f + 16f;
            }

            float dx = a.TargetX - a.X;
            float dy = a.TargetY - a.Y;
            float dist = MathF.Sqrt(dx * dx + dy * dy);
            if (dist < 4f)
            {
                _state.ActiveArrows.RemoveAt(i);
                continue;
            }
            float move = pixelsPerSec * delta;
            if (move >= dist) { _state.ActiveArrows.RemoveAt(i); continue; }
            a.X += dx / dist * move;
            a.Y += dy / dist * move;
        }
        _worldRenderer?.QueueRedraw(); // ensure arrows are redrawn
    }

    /// <summary>
    /// Drain chat message queue, store in history, and display filtered in console.
    /// </summary>
    private void UpdateConsoleMessages()
    {
        if (_console == null) return;

        bool hadNew = false;
        while (_state.ChatMessages.Count > 0)
        {
            var msg = _state.ChatMessages.Dequeue();
            // Feed to party panel for state tracking (non-destructive)
            _partyPanel?.TryParsePartyMessage(msg.Text);
            // Store in history
            _state.ChatHistory.Add(msg);
            if (_state.ChatHistory.Count > GameState.MaxChatHistory)
                _state.ChatHistory.RemoveAt(0);

            // Append to console if it passes the active filter
            if (PassesChatFilter(msg, _state.ActiveChatFilter))
            {
                _console.AppendText($"[b][color=#{msg.Color}]{msg.Text}[/color][/b]\n");
            }
            hadNew = true;
        }

        // If chat filter changed, rebuild console from history
        if (_state.ChatFilterDirty)
        {
            _state.ChatFilterDirty = false;
            RebuildConsoleFromHistory();
            hadNew = true;
        }

        // Auto-scroll to bottom when new messages arrive
        if (hadNew)
        {
            _console.ScrollFollowing = true;
            var vbar = _console.GetVScrollBar();
            vbar.Value = vbar.MaxValue;
        }
    }

    /// <summary>
    /// Check if a message passes the active chat tab filter.
    /// </summary>
    private static bool PassesChatFilter(ChatMessage msg, int filter)
    {
        if (filter < 0) return true; // "All" tab
        return (int)msg.Type == filter;
    }

    /// <summary>
    /// Rebuild the console RichTextLabel from stored chat history with current filter.
    /// </summary>
    private void RebuildConsoleFromHistory()
    {
        if (_console == null) return;
        _console.Clear();
        foreach (var msg in _state.ChatHistory)
        {
            if (PassesChatFilter(msg, _state.ActiveChatFilter))
            {
                _console.AppendText($"[b][color=#{msg.Color}]{msg.Text}[/color][/b]\n");
            }
        }
    }

    /// <summary>
    /// Set the active chat tab filter and trigger console rebuild.
    /// </summary>
    private void SetChatFilter(int filter)
    {
        if (_state.ActiveChatFilter == filter) return;
        _state.ActiveChatFilter = filter;
        _state.ChatFilterDirty = true;
        UpdateChatTabHighlight();
    }

    /// <summary>
    /// Update chat tab button colors to highlight the active filter.
    /// </summary>
    private void UpdateChatTabHighlight()
    {
        int[] tabFilters = { -1, (int)ChatType.Global, (int)ChatType.Party, (int)ChatType.Clan, (int)ChatType.Whisper, (int)ChatType.Combat, (int)ChatType.System };
        var activeColor = new Color(0.9f, 0.8f, 0.5f);
        var normalColor = new Color(0.7f, 0.7f, 0.7f);
        for (int i = 0; i < _chatTabButtons.Length; i++)
        {
            if (_chatTabButtons[i] != null)
            {
                bool active = tabFilters[i] == _state.ActiveChatFilter;
                _chatTabButtons[i].AddThemeColorOverride("font_color", active ? activeColor : normalColor);
            }
        }
    }

    public override void _Input(InputEvent @event)
    {
        // Escape on login → quit game
        if (@event is InputEventKey escKey && escKey.Pressed && !escKey.Echo
            && escKey.Keycode == Key.Escape
            && _state.CurrentScreen == Screen.Login)
        {
            GetTree().Quit();
            return;
        }

        // Escape on char select → disconnect back to login
        if (@event is InputEventKey escCharKey && escCharKey.Pressed && !escCharKey.Echo
            && escCharKey.Keycode == Key.Escape
            && _state.CurrentScreen == Screen.CharSelect)
        {
            HandleDisconnect("");
            return;
        }

        // Enter on login screen → trigger connect button
        if (@event is InputEventKey enterLoginKey && enterLoginKey.Pressed && !enterLoginKey.Echo
            && (enterLoginKey.Keycode == Key.Enter || enterLoginKey.Keycode == Key.KpEnter)
            && _state.CurrentScreen == Screen.Login
            && _connectButton != null && !_connectButton.Disabled)
        {
            _connectButton.EmitSignal("pressed");
            return;
        }

        // Enter on char select → connect with selected character
        if (@event is InputEventKey enterCharKey && enterCharKey.Pressed && !enterCharKey.Echo
            && (enterCharKey.Keycode == Key.Enter || enterCharKey.Keycode == Key.KpEnter)
            && _state.CurrentScreen == Screen.CharSelect)
        {
            OnEnterPressed();
            return;
        }

        if (_state.CurrentScreen != Screen.Game) return;

        // Chat input handling
        if (@event is InputEventKey key && key.Pressed && !key.Echo && _chatInput != null)
        {
            // Escape: close dialogs, chat input, or disconnect to login
            if (key.Keycode == Key.Escape)
            {
                if (_state.KeyBindPanelOpen)
                {
                    _keyBindPanel?.Close();
                    _state.KeyBindPanelOpen = false;
                    GetViewport().SetInputAsHandled();
                    return;
                }
                if (_state.StatsPanelOpen)
                {
                    _statsPanel?.Close();
                    GetViewport().SetInputAsHandled();
                    return;
                }
                if (_state.OptionsPanelOpen)
                {
                    _optionsPanel?.Close();
                    GetViewport().SetInputAsHandled();
                    return;
                }
                if (_state.MacroPanelOpen)
                {
                    _macroPanel?.Close();
                    GetViewport().SetInputAsHandled();
                    return;
                }
                if (_questPanel != null && _questPanel.Visible)
                {
                    _questPanel.Hide();
                    GetViewport().SetInputAsHandled();
                    return;
                }
                if (_trainerPanel != null && _trainerPanel.Visible)
                {
                    _trainerPanel.Hide();
                    GetViewport().SetInputAsHandled();
                    return;
                }
                if (_state.Comerciando)
                {
                    _tcp?.SendPacket(ClientPackets.WriteCommerceClose());
                    GetViewport().SetInputAsHandled();
                    return;
                }
                if (_state.Trading)
                {
                    _tcp?.SendPacket(ClientPackets.WriteTradeCancel());
                    GetViewport().SetInputAsHandled();
                    return;
                }
                if (_state.Banqueando || _state.BovedaAbierta)
                {
                    _tcp?.SendPacket(ClientPackets.WriteBankClose());
                    GetViewport().SetInputAsHandled();
                    return;
                }
                if (_state.DropDialogOpen)
                {
                    CloseDropDialog();
                    GetViewport().SetInputAsHandled();
                    return;
                }
                if (_state.EscapeMenuOpen)
                {
                    HideEscapeMenu();
                    GetViewport().SetInputAsHandled();
                    return;
                }
                if (_state.ChatActive)
                {
                    _chatInput.Text = "";
                    _chatInput.Visible = false;
                    _chatInput.ReleaseFocus();
                    _state.ChatActive = false;
                    GetViewport().SetInputAsHandled();
                    return;
                }
                // No dialog open → show escape menu
                ShowEscapeMenu();
                GetViewport().SetInputAsHandled();
                return;
            }

            // Block game keys when any form is open or a text field has focus.
            // Escape (above) is still allowed so panels can be closed.
            // This prevents Enter from toggling chat, F9/F10 from opening panels,
            // and numpad from switching chat modes while typing in a form input.
            //
            // IMPORTANT: Do NOT call SetInputAsHandled() when a LineEdit has focus —
            // Godot processes _Input() BEFORE GUI input routing, so consuming the
            // event here would prevent the LineEdit from receiving keystrokes.
            var focusOwner = GetViewport().GuiGetFocusOwner();
            bool uiTextFocused = focusOwner is LineEdit && focusOwner != _chatInput;
            if (_state.AnyFormOpen || uiTextFocused)
            {
                return;
            }

            // Enter: open chat input, or submit if already open (fallback for TextSubmitted)
            if (key.Keycode == Key.Enter || key.Keycode == Key.KpEnter)
            {
                if (_state.ChatActive && _chatInput.Visible)
                {
                    // Chat already open → submit (fallback if TextSubmitted doesn't fire)
                    OnChatSubmitted(_chatInput.Text);
                    GetViewport().SetInputAsHandled();
                    return;
                }
                else if (!_chatInput.Visible)
                {
                    // Open chat input (VB6: preserves previous text)
                    _chatInput.Visible = true;
                    _chatInput.GrabFocus();
                    _chatInput.SelectAll();
                    _state.ChatActive = true;
                    GetViewport().SetInputAsHandled();
                    return;
                }
            }

            // Numpad 0-8: chat mode switching (VB6: HablaNumerico)
            if (!_state.ChatActive && key.Keycode >= Key.Kp0 && key.Keycode <= Key.Kp8)
            {
                int mode = (int)key.Keycode - (int)Key.Kp0;
                SetChatMode(mode);
                GetViewport().SetInputAsHandled();
                return;
            }

            // F5: toggle stats panel (VB6: frmEstadisticas)
            if (key.Keycode == Key.F5 && !_state.ChatActive)
            {
                if (_statsPanel != null)
                {
                    if (_state.StatsPanelOpen)
                        _statsPanel.Close();
                    else
                        _statsPanel.Open();
                }
                GetViewport().SetInputAsHandled();
                return;
            }

            // F9: toggle macro panel (VB6: frmMakro)
            if (key.Keycode == Key.F9 && !_state.ChatActive)
            {
                if (_macroPanel != null)
                {
                    if (_state.MacroPanelOpen)
                        _macroPanel.Close();
                    else
                        _macroPanel.Open();
                }
                GetViewport().SetInputAsHandled();
                return;
            }

            // F10: toggle options panel (VB6: frmOpcionesNew)
            if (key.Keycode == Key.F10 && !_state.ChatActive)
            {
                if (_optionsPanel != null)
                {
                    if (_state.OptionsPanelOpen)
                        _optionsPanel.Close();
                    else
                        _optionsPanel.Open();
                }
                GetViewport().SetInputAsHandled();
                return;
            }

            // F12: toggle fullscreen/windowed
            if (key.Keycode == Key.F12)
            {
                bool goFullscreen = DisplayServer.WindowGetMode() != DisplayServer.WindowMode.Fullscreen;
                if (goFullscreen)
                {
                    GetTree().Root.ContentScaleAspect = _state.Config.AspectRatioMode == 1
                        ? Window.ContentScaleAspectEnum.Keep
                        : Window.ContentScaleAspectEnum.Keep;
                    DisplayServer.WindowSetMode(DisplayServer.WindowMode.Fullscreen);
                }
                else
                {
                    DisplayServer.WindowSetFlag(DisplayServer.WindowFlags.ResizeDisabled, false);
                    DisplayServer.WindowSetMode(DisplayServer.WindowMode.Windowed);
                    var winSize = new Vector2I(800, 600);
                    DisplayServer.WindowSetSize(winSize);
                    var screenSize = DisplayServer.ScreenGetSize();
                    DisplayServer.WindowSetPosition(new Vector2I(
                        (screenSize.X - winSize.X) / 2,
                        (screenSize.Y - winSize.Y) / 2));
                    GetTree().Root.ContentScaleAspect = Window.ContentScaleAspectEnum.Keep;
                }
                _state.Config.Fullscreen = goFullscreen;
                _state.Config.Save(_dataPath);
                GetViewport().SetInputAsHandled();
                return;
            }

        }

        // VB6 Form_KeyUp: attack fires on key RELEASE, not key down
        _inputHandler?.HandleInputEvent(@event);

        // Block mouse clicks on game world when any modal panel is open
        if (_state.Comerciando || _state.Banqueando || _state.BovedaAbierta
            || _state.Trading
            || _state.MacroPanelOpen || _state.OptionsPanelOpen || _state.KeyBindPanelOpen
            || _state.ShowTravelPanel)
        {
            if (@event is InputEventMouseButton) return;
        }

        // Mouse clicks on the game viewport area.
        // VB6: renderer_Click fires on mouse RELEASE, Form_DblClick on second click.
        // Godot: DoubleClick flag is only set on the PRESS event, not release.
        // So we handle double-click on press, and single-click on release.
        if (@event is InputEventMouseButton mb)
        {
            // Translate click position relative to the game viewport (8,144) with 544x416 size
            float clickX = mb.Position.X - 8;
            float clickY = mb.Position.Y - 144;

            // Only handle clicks within the game viewport area
            if (clickX >= 0 && clickX < 544 && clickY >= 0 && clickY < 416)
            {
                var viewPos = new Vector2(clickX, clickY);

                // Close context menu on any left-click in viewport
                if (mb.Pressed && mb.ButtonIndex == MouseButton.Left && _contextMenu != null && _contextMenu.IsOpen)
                {
                    _contextMenu.CloseMenu();
                }

                // Right-click PRESS in viewport: try to open context menu on character tile.
                // If a character is found, the menu opens and the event is consumed.
                // If no character, fall through to VB6 behavior (LC+RC on release).
                if (mb.Pressed && mb.ButtonIndex == MouseButton.Right)
                {
                    int tileX = _state.UserPosX + (int)clickX / 32 - 8;
                    int tileY = _state.UserPosY + (int)clickY / 32 - 6;
                    if (tileX >= 1 && tileX <= 100 && tileY >= 1 && tileY <= 100)
                    {
                        if (_contextMenu != null && _contextMenu.TryOpen(mb.Position, tileX, tileY))
                        {
                            GetViewport().SetInputAsHandled();
                            return;
                        }
                    }
                }

                // On PRESS: handle double-click, shift+click (GM teleport)
                // Modifier flags (ShiftPressed) are only reliable on press, not release.
                if (mb.Pressed && mb.ButtonIndex == MouseButton.Left)
                {
                    if (mb.DoubleClick)
                    {
                        // VB6 Form_DblClick sends RC (interact: open/close doors, use objects).
                        _inputHandler?.HandleRightClick(viewPos, _state.UserPosX, _state.UserPosY);
                        _dblClickHandled = true;
                        GetViewport().SetInputAsHandled();
                        return;
                    }

                    if (mb.ShiftPressed && _state.Privileges >= 1)
                    {
                        // VB6 GM: Shift+Click → /TELEP YO mapa,x,y
                        _inputHandler?.HandleGmTeleport(viewPos, _state.UserPosX, _state.UserPosY, _state.CurrentMap);
                        _dblClickHandled = true; // reuse flag to skip the release
                        GetViewport().SetInputAsHandled();
                        return;
                    }
                }

                // On release: handle single clicks
                if (!mb.Pressed)
                {
                    // Skip the release after a double-click or shift+click (already handled on press)
                    if (_dblClickHandled && mb.ButtonIndex == MouseButton.Left)
                    {
                        _dblClickHandled = false;
                        return;
                    }

                    if (mb.ButtonIndex == MouseButton.Left)
                    {
                        if (_state.UsingSkill > 0)
                        {
                            // VB6: Form_Click when UsingSkill > 0 → WLC (spell targeting)
                            _inputHandler?.HandleSpellClick(viewPos, _state.UserPosX, _state.UserPosY);
                            Input.SetDefaultCursorShape(Input.CursorShape.Arrow);
                        }
                        else
                        {
                            // VB6 renderer_Click left → LC (inspect tile)
                            _inputHandler?.HandleLeftClick(viewPos, _state.UserPosX, _state.UserPosY);
                        }
                    }
                    else if (mb.ButtonIndex == MouseButton.Right && _state.Config.MouseRightClick)
                    {
                        // VB6: right-click sends BOTH LC + RC (DobleClick=1 path)
                        _inputHandler?.HandleLeftClick(viewPos, _state.UserPosX, _state.UserPosY);
                        _inputHandler?.HandleRightClick(viewPos, _state.UserPosX, _state.UserPosY);
                    }
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

            // Load particles and lights embedded in tile data (byFlags bits 5/6)
            LoadTileParticlesAndLights(_state);

            GD.Print($"[MAIN] Map {_state.CurrentMap} loaded OK");
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[MAIN] Failed to load map {_state.CurrentMap}: {ex.Message}");
        }
    }

    private void LoadTileParticlesAndLights(GameState state)
    {
        if (state.MapData == null) return;
        var map = state.MapData;
        for (int y = 1; y <= 100; y++)
        {
            for (int x = 1; x <= 100; x++)
            {
                ref var tile = ref map.Tiles[x, y];
                if (tile.ParticleGroup > 0)
                    ParticleSystem.CreateMapStream(state, tile.ParticleGroup, x, y);
                if (tile.LightRange > 0)
                {
                    state.MapLights.Add(new MapLight
                    {
                        X = x, Y = y, Range = tile.LightRange,
                        R = (byte)tile.LightR, G = (byte)tile.LightG, B = (byte)tile.LightB,
                        Active = true
                    });
                    state.LightsDirty = true;
                }
            }
        }
    }

    // VB6 movement constants
    private const float EngineBaseSpeed = 0.0172f;   // VB6 timerTicksPerFrame = deltaMs * 0.0172
    private const float ScrollPixelsPerFrame = 8f;   // VB6 ScrollPixelsPerFrameX/Y

    public override void _ExitTree()
    {
        _tcp?.Dispose();
    }

    /// <summary>
    /// Load patch PCK files from the exe directory.
    /// Files named "patch.pck" or "patch_*.pck" override base resources,
    /// allowing delta updates without replacing the full game.
    /// Patches are loaded in alphabetical order so later patches win.
    /// </summary>
    private void LoadPatchPacks(string exeDir)
    {
        // Single patch file
        string singlePatch = System.IO.Path.Combine(exeDir, "patch.pck");
        if (System.IO.File.Exists(singlePatch))
        {
            bool ok = ProjectSettings.LoadResourcePack(singlePatch, true);
            GD.Print($"[PATCH] patch.pck: {(ok ? "loaded" : "FAILED")}");
        }

        // Numbered patches: patch_001.pck, patch_002.pck, etc.
        string[] patchFiles;
        try
        {
            patchFiles = System.IO.Directory.GetFiles(exeDir, "patch_*.pck");
            System.Array.Sort(patchFiles);
        }
        catch { return; }

        foreach (string pf in patchFiles)
        {
            bool ok = ProjectSettings.LoadResourcePack(pf, true);
            string name = System.IO.Path.GetFileName(pf);
            GD.Print($"[PATCH] {name}: {(ok ? "loaded" : "FAILED")}");
        }
    }
}
