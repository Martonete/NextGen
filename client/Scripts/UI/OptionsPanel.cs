using Godot;
using System;
using System.Collections.Generic;
using ArgentumNextgen.Game;
using ArgentumNextgen.Network;

namespace ArgentumNextgen.UI;

/// <summary>
/// VB6 frmOpcionesNew — Options panel with 4 tabs: Juego, Controles, Render, Clan.
/// Changes apply immediately. Draggable by title bar. X button to close.
/// Saves to Data/INIT/Options.ao on every change.
/// Now extends RpgBaseForm for consistent RPG-themed look.
/// </summary>
public partial class OptionsPanel : RpgBaseForm
{
    private const int PanelW = 420;

    private GameState? _state;
    private GameConfig? _config;
    private string _dataPath = "";

    private AoTcpClient? _tcp;

    // Tab system
    private HBoxContainer? _tabBar;
    private Control? _gameTab;
    private Control? _renderTab;
    private Control? _clanTab;
    private int _activeTab;

    // ── Clan tab controls ──
    private VBoxContainer? _clanContent;
    private bool _clanTabRequested;

    // ── Game tab controls ──
    private Button? _chkMusic;
    private Button? _chkSfx;
    private HSlider? _sldMusicVol;
    private HSlider? _sldSfxVol;
    private Label? _lblMusicVol;
    private Label? _lblSfxVol;
    private Button? _chkGlobalChat;
    private Button? _chkPrivateChat;
    private Button? _chkBuffTimers;
    // _chkChatSound removed (Alerta sonora removed from UI)
    private Button? _chkVsync;
    private OptionButton? _optFpsLimit;

    // ── Controls tab controls ──
    private TextureButton? _btnKeyConfig;
    private Button? _chkMouseDClick;
    private Button? _chkMouseRClick;
    private Button? _chkMouseContext;
    private Button? _chkBlockWalkOnChat;
    private Button? _chkDragWindow;

    // ── Render tab controls (Display) ──
    private Button? _chkFullscreen;
    private HBoxContainer? _aspectRow;
    private OptionButton? _optAspect;
    private HBoxContainer? _resolutionRow;
    private OptionButton? _optResolution;

    // ── Render tab controls ──
    private OptionButton? _optPerformance;
    private Button? _chkAuras;
    private Button? _chkParticles;
    private Button? _chkShadows;
    private Button? _chkReflections;
    private Button? _chkDayNight;
    private Button? _chkNames;
    private Button? _chkLights;
    private Button? _chkTreeTransparency;
    private HSlider? _sldTreeTransparency;
    private HBoxContainer? _treeSliderWrap;
    private Button? _chkDeadTransparency;
    private HSlider? _sldDeadTransparency;
    private HBoxContainer? _deadSliderWrap;
    private Button? _chkFormTransparency;
    private HSlider? _sldFormTransparency;
    private HBoxContainer? _formSliderWrap;
    private Button? _chkMinimap;

    // Suppress initial load toggles from triggering saves
    private bool _loading;

    // Callback for when config is applied (Main.cs hooks into this)
    public event System.Action? OnConfigApplied;

    // Callback to open the key binding panel
    public event System.Action? OnOpenKeyBinds;

    public OptionsPanel() : base("Opciones", new Vector2(630, 500), "v2") { }

    public void Init(GameState state, GameConfig config, string dataPath, AoTcpClient? tcp = null)
    {
        _state = state;
        _config = config;
        _dataPath = dataPath;
        _tcp = tcp;
    }

    protected override void BuildContent()
    {
        var root = RpgTheme.CreateColumn();
        root.SizeFlagsVertical = SizeFlags.ExpandFill;

        // Tab bar — 2 tabs (Controles merged into Juego, Clan has own panel)
        _tabBar = RpgTheme.CreateTabBar(
            new[] { "Juego", "Video" },
            idx => SetTab(idx)
        );
        root.AddChild(_tabBar);
        root.AddChild(RpgTheme.CreateSeparator());
        root.AddChild(RpgTheme.CreateSpacer(4));

        // Tab content area — scroll with custom scrollbar in padding area
        var scrollArea = RpgTheme.CreateScrollArea();
        root.AddChild(scrollArea);
        var tabHost = scrollArea.GetMeta("content").As<VBoxContainer>();

        _gameTab = BuildGameTab();
        _renderTab = BuildRenderTab();
        tabHost.AddChild(_gameTab);
        tabHost.AddChild(_renderTab);

        ContentContainer.AddChild(root);

        SetTab(0);
    }

    // ── Tab builders ──────────────────────────────────────

    private VBoxContainer BuildGameTab()
    {
        var cols = RpgTheme.CreateRow(RpgTheme.SpacingXl);

        // -- Left column: Audio --
        var leftCol = RpgTheme.CreateColumn();
        leftCol.SizeFlagsHorizontal = SizeFlags.ExpandFill;

        leftCol.AddChild(RpgTheme.CreateTitleLabel("Audio", 15));

        // Musica: [label] [slider] [%] [checkbox]
        var musicRow = RpgTheme.CreateRow(RpgTheme.SpacingSm);
        var musicLabel = RpgTheme.CreateInfoLabel("Musica", 13);
        RpgTheme.SetMinW(musicLabel, 70);
        musicRow.AddChild(musicLabel);
        _sldMusicVol = RpgTheme.CreateRpgSlider(70, 0, 100, 40);
        _sldMusicVol.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        musicRow.AddChild(_sldMusicVol);
        _lblMusicVol = RpgTheme.CreateInfoLabel("70%", 11);
        musicRow.AddChild(_lblMusicVol);
        _sldMusicVol.ValueChanged += v => { _lblMusicVol.Text = $"{(int)v}%"; ApplyImmediate(); };
        _chkMusic = RpgTheme.CreateRpgCheckbox("default", true);
        _chkMusic.Toggled += on => { SetSliderEnabled(_sldMusicVol, _lblMusicVol, on); ApplyImmediate(); };
        musicRow.AddChild(_chkMusic);
        leftCol.AddChild(musicRow);

        // Efectos: [label] [slider] [%] [checkbox]
        var sfxRow = RpgTheme.CreateRow(RpgTheme.SpacingSm);
        var sfxLabel = RpgTheme.CreateInfoLabel("Efectos", 13);
        RpgTheme.SetMinW(sfxLabel, 70);
        sfxRow.AddChild(sfxLabel);
        _sldSfxVol = RpgTheme.CreateRpgSlider(100, 0, 100, 40);
        _sldSfxVol.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        sfxRow.AddChild(_sldSfxVol);
        _lblSfxVol = RpgTheme.CreateInfoLabel("100%", 11);
        sfxRow.AddChild(_lblSfxVol);
        _sldSfxVol.ValueChanged += v => { _lblSfxVol.Text = $"{(int)v}%"; ApplyImmediate(); };
        _chkSfx = RpgTheme.CreateRpgCheckbox("default", true);
        _chkSfx.Toggled += on => { SetSliderEnabled(_sldSfxVol, _lblSfxVol, on); ApplyImmediate(); };
        sfxRow.AddChild(_chkSfx);
        leftCol.AddChild(sfxRow);

        cols.AddChild(leftCol);

        // Vertical separator between columns
        var sep = new VSeparator();
        sep.AddThemeConstantOverride("separation", 4);
        cols.AddChild(sep);

        // -- Right column: Chat --
        var rightCol = RpgTheme.CreateColumn();
        rightCol.SizeFlagsHorizontal = SizeFlags.ExpandFill;

        rightCol.AddChild(RpgTheme.CreateTitleLabel("Chat / Consola", 15));

        var globalChatRow = RpgTheme.CreateRpgCheckboxRow("Mensajes globales");
        _chkGlobalChat = GetCheckboxFromRow(globalChatRow);
        _chkGlobalChat.Toggled += _ => ApplyImmediate();
        rightCol.AddChild(globalChatRow);

        var privateChatRow = RpgTheme.CreateRpgCheckboxRow("Mensajes privados");
        _chkPrivateChat = GetCheckboxFromRow(privateChatRow);
        _chkPrivateChat.Toggled += _ => ApplyImmediate();
        rightCol.AddChild(privateChatRow);

        cols.AddChild(rightCol);

        var vbox = RpgTheme.CreateColumn();
        vbox.AddChild(cols);

        vbox.AddChild(RpgTheme.CreateSeparator());

        // -- Controls section --
        var ctrlCols = RpgTheme.CreateRow(RpgTheme.SpacingXl);

        // Left column: Funciones (mouse options + walk/drag toggles)
        var ctrlLeft = RpgTheme.CreateColumn();
        ctrlLeft.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        ctrlLeft.AddChild(RpgTheme.CreateTitleLabel("Funciones", 15));

        var dclickRow = RpgTheme.CreateRpgCheckboxRow("Doble click interactuar");
        _chkMouseDClick = GetCheckboxFromRow(dclickRow);
        _chkMouseDClick.Toggled += _ => ApplyImmediate();
        ctrlLeft.AddChild(dclickRow);

        var rclickRow = RpgTheme.CreateRpgCheckboxRow("Usar click derecho como doble click");
        _chkMouseRClick = GetCheckboxFromRow(rclickRow);
        _chkMouseRClick.Toggled += _ => ApplyImmediate();
        ctrlLeft.AddChild(rclickRow);

        var contextRow = RpgTheme.CreateRpgCheckboxRow("Menu contextual");
        _chkMouseContext = GetCheckboxFromRow(contextRow);
        _chkMouseContext.Toggled += _ => ApplyImmediate();
        ctrlLeft.AddChild(contextRow);

        var walkBlockRow = RpgTheme.CreateRpgCheckboxRow("Bloquear caminata al escribir");
        _chkBlockWalkOnChat = GetCheckboxFromRow(walkBlockRow);
        _chkBlockWalkOnChat.Toggled += _ => ApplyImmediate();
        ctrlLeft.AddChild(walkBlockRow);

        var dragWindowRow = RpgTheme.CreateRpgCheckboxRow("Mover la pantalla en modo ventana");
        _chkDragWindow = GetCheckboxFromRow(dragWindowRow);
        _chkDragWindow.Toggled += _ => ApplyImmediate();
        ctrlLeft.AddChild(dragWindowRow);

        ctrlCols.AddChild(ctrlLeft);

        var ctrlSep = new VSeparator();
        ctrlSep.AddThemeConstantOverride("separation", 4);
        ctrlCols.AddChild(ctrlSep);

        // Right column: Teclas
        var ctrlRight = RpgTheme.CreateColumn();
        ctrlRight.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        ctrlRight.AddChild(RpgTheme.CreateTitleLabel("Teclas", 15));
        _btnKeyConfig = RpgTheme.CreateRpgButton("Configurar Teclas", false, 13);
        _btnKeyConfig.CustomMinimumSize = new Vector2(0, 34);
        _btnKeyConfig.Pressed += () => OnOpenKeyBinds?.Invoke();
        ctrlRight.AddChild(_btnKeyConfig);

        ctrlCols.AddChild(ctrlRight);
        vbox.AddChild(ctrlCols);

        return vbox;
    }

    // BuildControlsTab removed — merged into BuildGameTab

    private HBoxContainer? _fpsRow;

    private VBoxContainer BuildRenderTab()
    {
        var mainCols = RpgTheme.CreateRow(RpgTheme.SpacingXl);

        // ═══ LEFT COLUMN: Pantalla + Transparencias ═══
        var leftCol = RpgTheme.CreateColumn();
        leftCol.SizeFlagsHorizontal = SizeFlags.ExpandFill;

        leftCol.AddChild(RpgTheme.CreateTitleLabel("Pantalla", 15));

        var qualityRow = RpgTheme.CreateRpgDropdownRow("Calidad:",
            new[] { "Minimo", "Bajo", "Medio", "Alto", "Maximo" }, 100);
        _optPerformance = GetDropdownFromRow(qualityRow);
        _optPerformance.ItemSelected += OnPerformanceSelected;
        leftCol.AddChild(qualityRow);

        var fullscreenRow = RpgTheme.CreateRpgCheckboxRow("Pantalla Completa");
        _chkFullscreen = GetCheckboxFromRow(fullscreenRow);
        _chkFullscreen.Toggled += on =>
        {
            if (_aspectRow != null) _aspectRow.Visible = on;
            ApplyImmediate();
        };
        leftCol.AddChild(fullscreenRow);

        _aspectRow = RpgTheme.CreateRpgDropdownRow("Aspecto:",
            new[] { "4:3", "16:9" }, 100);
        _optAspect = GetDropdownFromRow(_aspectRow);
        _optAspect.ItemSelected += _ => ApplyImmediate();
        leftCol.AddChild(_aspectRow);

        // Resolution preset dropdown
        var resLabels = new string[ResolutionManager.Presets.Length];
        for (int i = 0; i < ResolutionManager.Presets.Length; i++)
            resLabels[i] = ResolutionManager.Presets[i].label;
        _resolutionRow = RpgTheme.CreateRpgDropdownRow("Resolucion:", resLabels, 120);
        _optResolution = GetDropdownFromRow(_resolutionRow);
        _optResolution.ItemSelected += OnResolutionSelected;
        leftCol.AddChild(_resolutionRow);

        _fpsRow = RpgTheme.CreateRpgDropdownRow("FPS:",
            new[] { "60", "120", "144", "165", "240", "Sin limite" }, 100);
        _optFpsLimit = GetDropdownFromRow(_fpsRow);
        _optFpsLimit.ItemSelected += _ => ApplyImmediate();
        leftCol.AddChild(_fpsRow);

        var vsyncRow = RpgTheme.CreateRpgCheckboxRow("V-Sync");
        _chkVsync = GetCheckboxFromRow(vsyncRow);
        _chkVsync.Toggled += on =>
        {
            if (_fpsRow != null)
            {
                _fpsRow.Modulate = on ? new Color(1, 1, 1, 0.3f) : Colors.White;
                if (_optFpsLimit != null)
                {
                    _optFpsLimit.Disabled = on;
                    if (on) SelectMonitorRefreshRate();
                }
            }
            ApplyImmediate();
        };
        leftCol.AddChild(vsyncRow);

        // ── Transparencias ──
        leftCol.AddChild(RpgTheme.CreateSpacer(6));
        leftCol.AddChild(RpgTheme.CreateTitleLabel("Transparencias", 15));

        // Arboles: [label] [slider + %] [checkbox]
        var treeTransRow = RpgTheme.CreateRow(RpgTheme.SpacingSm);
        var treeLabel = RpgTheme.CreateInfoLabel("Arboles", 13);
        treeLabel.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        RpgTheme.SetMinW(treeLabel, 60);
        treeTransRow.AddChild(treeLabel);
        _treeSliderWrap = RpgTheme.CreateRow(RpgTheme.SpacingSm);
        _treeSliderWrap.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        _sldTreeTransparency = RpgTheme.CreateRpgSlider(47, 35, 100, 40);
        _sldTreeTransparency.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        _treeSliderWrap.AddChild(_sldTreeTransparency);
        var lblTreeAlpha = RpgTheme.CreateInfoLabel("47%", 11);
        _treeSliderWrap.AddChild(lblTreeAlpha);
        _sldTreeTransparency.ValueChanged += v => { lblTreeAlpha.Text = $"{(int)v}%"; ApplyImmediate(); };
        treeTransRow.AddChild(_treeSliderWrap);
        _chkTreeTransparency = RpgTheme.CreateRpgCheckbox("default", true);
        _chkTreeTransparency.Toggled += on => { SetSliderWrapEnabled(_sldTreeTransparency, _treeSliderWrap, on); ApplyImmediate(); };
        treeTransRow.AddChild(_chkTreeTransparency);
        leftCol.AddChild(treeTransRow);

        // Muertos: [label] [slider + %] [checkbox]
        var deadTransRow = RpgTheme.CreateRow(RpgTheme.SpacingSm);
        var deadLabel = RpgTheme.CreateInfoLabel("Muertos", 13);
        deadLabel.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        RpgTheme.SetMinW(deadLabel, 60);
        deadTransRow.AddChild(deadLabel);
        _deadSliderWrap = RpgTheme.CreateRow(RpgTheme.SpacingSm);
        _deadSliderWrap.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        _sldDeadTransparency = RpgTheme.CreateRpgSlider(47, 35, 100, 40);
        _sldDeadTransparency.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        _deadSliderWrap.AddChild(_sldDeadTransparency);
        var lblDeadAlpha = RpgTheme.CreateInfoLabel("47%", 11);
        _deadSliderWrap.AddChild(lblDeadAlpha);
        _sldDeadTransparency.ValueChanged += v => { lblDeadAlpha.Text = $"{(int)v}%"; ApplyImmediate(); };
        deadTransRow.AddChild(_deadSliderWrap);
        _chkDeadTransparency = RpgTheme.CreateRpgCheckbox("default", true);
        _chkDeadTransparency.Toggled += on => { SetSliderWrapEnabled(_sldDeadTransparency, _deadSliderWrap, on); ApplyImmediate(); };
        deadTransRow.AddChild(_chkDeadTransparency);
        leftCol.AddChild(deadTransRow);

        // Formularios: [label] [slider + %] [checkbox]
        var formTransRow = RpgTheme.CreateRow(RpgTheme.SpacingSm);
        var formLabel = RpgTheme.CreateInfoLabel("Formularios", 13);
        formLabel.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        RpgTheme.SetMinW(formLabel, 80);
        formTransRow.AddChild(formLabel);
        _formSliderWrap = RpgTheme.CreateRow(RpgTheme.SpacingSm);
        _formSliderWrap.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        _sldFormTransparency = RpgTheme.CreateRpgSlider(90, 40, 100, 40);
        _sldFormTransparency.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        _formSliderWrap.AddChild(_sldFormTransparency);
        var lblFormAlpha = RpgTheme.CreateInfoLabel("90%", 11);
        _formSliderWrap.AddChild(lblFormAlpha);
        _sldFormTransparency.ValueChanged += v => { lblFormAlpha.Text = $"{(int)v}%"; ApplyImmediate(); };
        formTransRow.AddChild(_formSliderWrap);
        _chkFormTransparency = RpgTheme.CreateRpgCheckbox("default", true);
        _chkFormTransparency.Toggled += on => { SetSliderWrapEnabled(_sldFormTransparency, _formSliderWrap, on); ApplyImmediate(); };
        formTransRow.AddChild(_chkFormTransparency);
        leftCol.AddChild(formTransRow);

        mainCols.AddChild(leftCol);

        var videoSep = new VSeparator();
        videoSep.AddThemeConstantOverride("separation", 4);
        mainCols.AddChild(videoSep);

        // ═══ RIGHT COLUMN: Efectos Visuales ═══
        var rightCol = RpgTheme.CreateColumn();
        rightCol.SizeFlagsHorizontal = SizeFlags.ExpandFill;

        rightCol.AddChild(RpgTheme.CreateTitleLabel("Efectos", 15));

        var aurasRow = RpgTheme.CreateRpgCheckboxRow("Auras");
        _chkAuras = GetCheckboxFromRow(aurasRow);
        _chkAuras.Toggled += _ => ApplyImmediate();
        rightCol.AddChild(aurasRow);

        var particlesRow = RpgTheme.CreateRpgCheckboxRow("Particulas");
        _chkParticles = GetCheckboxFromRow(particlesRow);
        _chkParticles.Toggled += _ => ApplyImmediate();
        rightCol.AddChild(particlesRow);

        var shadowsRow = RpgTheme.CreateRpgCheckboxRow("Sombras");
        _chkShadows = GetCheckboxFromRow(shadowsRow);
        _chkShadows.Toggled += _ => ApplyImmediate();
        rightCol.AddChild(shadowsRow);

        var lightsRow = RpgTheme.CreateRpgCheckboxRow("Luces");
        _chkLights = GetCheckboxFromRow(lightsRow);
        _chkLights.Toggled += _ => ApplyImmediate();
        rightCol.AddChild(lightsRow);

        var reflectionsRow = RpgTheme.CreateRpgCheckboxRow("Reflejos agua");
        _chkReflections = GetCheckboxFromRow(reflectionsRow);
        _chkReflections.Toggled += _ => ApplyImmediate();
        rightCol.AddChild(reflectionsRow);

        var dayNightRow = RpgTheme.CreateRpgCheckboxRow("Dia/Noche");
        _chkDayNight = GetCheckboxFromRow(dayNightRow);
        _chkDayNight.Toggled += _ => ApplyImmediate();
        rightCol.AddChild(dayNightRow);

        var namesRow = RpgTheme.CreateRpgCheckboxRow("Nombres");
        _chkNames = GetCheckboxFromRow(namesRow);
        _chkNames.Toggled += _ => ApplyImmediate();
        rightCol.AddChild(namesRow);

        var buffTimersRow = RpgTheme.CreateRpgCheckboxRow("Contadores de buffs");
        _chkBuffTimers = GetCheckboxFromRow(buffTimersRow);
        _chkBuffTimers.Toggled += _ => ApplyImmediate();
        rightCol.AddChild(buffTimersRow);

        var minimapRow = RpgTheme.CreateRpgCheckboxRow("Minimapa");
        _chkMinimap = GetCheckboxFromRow(minimapRow);
        _chkMinimap.Toggled += _ => ApplyImmediate();
        rightCol.AddChild(minimapRow);

        mainCols.AddChild(rightCol);

        var vbox = RpgTheme.CreateColumn();
        vbox.AddChild(mainCols);

        return vbox;
    }

    // ── Open / Close ──────────────────────────────────────

    public void Open()
    {
        if (_state == null || _config == null) return;

        _loading = true;
        LoadControlsFromConfig(_config);
        _loading = false;

        _state.OptionsPanelOpen = true;
        ShowForm();
        SetTab(_activeTab);
    }

    public override void HideForm()
    {
        if (_state != null)
            _state.OptionsPanelOpen = false;
        base.HideForm();
    }

    public void Close() => HideForm();

    // ── Immediate apply on any control change ────────────

    private void ApplyImmediate()
    {
        if (_loading || _config == null) return;

        SaveControlsToConfig(_config);
        _config.Save(_dataPath);
        OnConfigApplied?.Invoke();
    }

    // ── Resolution change ─────────────────────────────────

    /// <summary>Callback for resolution change requiring scene reload.</summary>
    public event System.Action? OnResolutionChanged;

    private void OnResolutionSelected(long index)
    {
        if (_loading || _config == null) return;
        int idx = (int)index;
        if (idx < 0 || idx >= ResolutionManager.Presets.Length) return;

        var (w, h, _) = ResolutionManager.Presets[idx];
        _config.ResolutionWidth = w;
        _config.ResolutionHeight = h;
        _config.Save(_dataPath);

        // Apply new resolution and reload scene to rebuild all UI positions
        ResolutionManager.ApplyResolution(w, h);
        OnResolutionChanged?.Invoke();
    }

    // ── Performance preset ────────────────────────────────

    private void OnPerformanceSelected(long index)
    {
        if (_loading || _config == null) return;

        int level = (int)index;
        _config.PerformanceLevel = level;
        _config.ApplyPerformancePreset(level);

        // Reload render checkboxes to reflect preset
        _loading = true;
        LoadRenderChecks(_config);
        _loading = false;

        _config.Save(_dataPath);
        OnConfigApplied?.Invoke();
    }

    // ── Tab switching ─────────────────────────────────────

    private void SetTab(int idx)
    {
        _activeTab = idx;
        if (_gameTab != null) _gameTab.Visible = idx == 0;
        if (_renderTab != null) _renderTab.Visible = idx == 1;

        if (_tabBar != null)
            RpgTheme.SetTabBarActive(_tabBar, idx);
    }

    // ── Config <-> Controls sync ────────────────────────────

    private void LoadControlsFromConfig(GameConfig cfg)
    {
        // Game tab
        SetCheck(_chkMusic, cfg.MusicEnabled);
        SetCheck(_chkSfx, cfg.SfxEnabled);
        if (_sldMusicVol != null) _sldMusicVol.Value = cfg.MusicVolume;
        if (_sldSfxVol != null) _sldSfxVol.Value = cfg.SfxVolume;
        if (_lblMusicVol != null) _lblMusicVol.Text = $"{cfg.MusicVolume}%";
        if (_lblSfxVol != null) _lblSfxVol.Text = $"{cfg.SfxVolume}%";
        SetSliderEnabled(_sldMusicVol, _lblMusicVol, cfg.MusicEnabled);
        SetSliderEnabled(_sldSfxVol, _lblSfxVol, cfg.SfxEnabled);

        SetCheck(_chkGlobalChat, cfg.ShowGlobalChat);
        SetCheck(_chkPrivateChat, cfg.ShowPrivateChat);
        SetCheck(_chkBuffTimers, cfg.ShowBuffTimers);
        // Contact sign-in/out removed (friend system removed)
        // ChatSoundAlert not in UI (removed)

        // V-Sync & FPS
        if (_optFpsLimit != null)
        {
            int sel = cfg.FpsLimit switch
            {
                60 => 0,
                120 => 1,
                144 => 2,
                165 => 3,
                240 => 4,
                _ => 5 // unlimited
            };
            _optFpsLimit.Selected = sel;
        }
        SetCheck(_chkVsync, cfg.VsyncEnabled);
        // V-Sync disables FPS selector and shows monitor refresh rate
        if (_fpsRow != null)
        {
            _fpsRow.Modulate = cfg.VsyncEnabled ? new Color(1, 1, 1, 0.3f) : Colors.White;
            if (_optFpsLimit != null)
            {
                _optFpsLimit.Disabled = cfg.VsyncEnabled;
                if (cfg.VsyncEnabled) SelectMonitorRefreshRate();
            }
        }

        // Controls tab
        SetCheck(_chkMouseDClick, cfg.MouseDoubleClick);
        SetCheck(_chkMouseRClick, cfg.MouseRightClick);
        SetCheck(_chkMouseContext, cfg.MouseContextMenu);
        SetCheck(_chkBlockWalkOnChat, cfg.BlockWalkOnChat);
        SetCheck(_chkDragWindow, cfg.DragWindowEnabled);

        // Render tab — Display
        SetCheck(_chkFullscreen, cfg.Fullscreen);
        if (_aspectRow != null) _aspectRow.Visible = cfg.Fullscreen;
        if (_optAspect != null) _optAspect.Selected = cfg.AspectRatioMode;

        // Resolution preset
        if (_optResolution != null)
        {
            int resSel = 0;
            for (int i = 0; i < ResolutionManager.Presets.Length; i++)
            {
                if (ResolutionManager.Presets[i].w == cfg.ResolutionWidth &&
                    ResolutionManager.Presets[i].h == cfg.ResolutionHeight)
                {
                    resSel = i;
                    break;
                }
            }
            _optResolution.Selected = resSel;
        }

        // Render tab
        if (_optPerformance != null) _optPerformance.Selected = cfg.PerformanceLevel;
        LoadRenderChecks(cfg);
    }

    private void LoadRenderChecks(GameConfig cfg)
    {
        SetCheck(_chkAuras, cfg.ShowAuras);
        SetCheck(_chkParticles, cfg.ShowParticles);
        SetCheck(_chkShadows, cfg.ShowShadows);
        SetCheck(_chkReflections, cfg.ShowReflections);
        SetCheck(_chkDayNight, cfg.ShowDayNight);
        SetCheck(_chkNames, cfg.ShowNames);
        SetCheck(_chkLights, cfg.ShowLights);
        SetCheck(_chkMinimap, cfg.ShowMinimap);
        // Transparencias
        SetCheck(_chkTreeTransparency, cfg.TreeRoofTransparency);
        if (_sldTreeTransparency != null) _sldTreeTransparency.Value = cfg.TreeTransparencyAlpha;
        SetSliderWrapEnabled(_sldTreeTransparency, _treeSliderWrap, cfg.TreeRoofTransparency);
        SetCheck(_chkDeadTransparency, cfg.DeadCharTransparency);
        if (_sldDeadTransparency != null) _sldDeadTransparency.Value = cfg.DeadTransparencyAlpha;
        SetSliderWrapEnabled(_sldDeadTransparency, _deadSliderWrap, cfg.DeadCharTransparency);
        SetCheck(_chkFormTransparency, cfg.FormTransparency);
        if (_sldFormTransparency != null) _sldFormTransparency.Value = cfg.FormTransparencyAlpha;
        SetSliderWrapEnabled(_sldFormTransparency, _formSliderWrap, cfg.FormTransparency);
    }

    private void SaveControlsToConfig(GameConfig cfg)
    {
        // Game tab
        cfg.MusicEnabled = IsChecked(_chkMusic);
        cfg.SfxEnabled = IsChecked(_chkSfx);
        cfg.MusicVolume = (int)(_sldMusicVol?.Value ?? 70);
        cfg.SfxVolume = (int)(_sldSfxVol?.Value ?? 100);

        cfg.ShowGlobalChat = IsChecked(_chkGlobalChat);
        cfg.ShowPrivateChat = IsChecked(_chkPrivateChat);
        cfg.ShowBuffTimers = IsChecked(_chkBuffTimers);
        // ChatSoundAlert not in UI (removed)

        cfg.VsyncEnabled = IsChecked(_chkVsync);
        cfg.FpsLimit = (_optFpsLimit?.Selected ?? 5) switch
        {
            0 => 60,
            1 => 120,
            2 => 144,
            3 => 165,
            4 => 240,
            _ => 0
        };

        // Controls tab
        cfg.MouseDoubleClick = IsChecked(_chkMouseDClick);
        cfg.MouseRightClick = IsChecked(_chkMouseRClick);
        cfg.MouseContextMenu = IsChecked(_chkMouseContext);
        cfg.BlockWalkOnChat = IsChecked(_chkBlockWalkOnChat);
        cfg.DragWindowEnabled = IsChecked(_chkDragWindow);

        // Render tab — Display
        cfg.Fullscreen = IsChecked(_chkFullscreen);
        cfg.AspectRatioMode = _optAspect?.Selected ?? 0;
        // Resolution is saved directly in OnResolutionSelected (requires scene reload)

        // Render tab
        cfg.PerformanceLevel = _optPerformance?.Selected ?? 2;
        cfg.ShowAuras = IsChecked(_chkAuras);
        cfg.ShowParticles = IsChecked(_chkParticles);
        cfg.ShowShadows = IsChecked(_chkShadows);
        cfg.ShowNpcShadows = IsChecked(_chkShadows); // Single toggle controls both
        cfg.ShowReflections = IsChecked(_chkReflections);
        cfg.ShowDayNight = IsChecked(_chkDayNight);
        cfg.ShowNames = IsChecked(_chkNames);
        cfg.ShowLights = IsChecked(_chkLights);
        cfg.ShowMinimap = IsChecked(_chkMinimap);
        // Transparencias
        cfg.TreeRoofTransparency = IsChecked(_chkTreeTransparency);
        cfg.TreeTransparencyAlpha = _sldTreeTransparency != null ? (int)_sldTreeTransparency.Value : 47;
        cfg.DeadCharTransparency = IsChecked(_chkDeadTransparency);
        cfg.DeadTransparencyAlpha = _sldDeadTransparency != null ? (int)_sldDeadTransparency.Value : 47;
        cfg.FormTransparency = IsChecked(_chkFormTransparency);
        cfg.FormTransparencyAlpha = _sldFormTransparency != null ? (int)_sldFormTransparency.Value : 90;
    }

    // ── UI helpers ────────────────────────────────────────

    /// <summary>Enable/disable a slider + its % label (dimmed when disabled).</summary>
    private static void SetSliderEnabled(HSlider? slider, Label? label, bool enabled)
    {
        var dim = enabled ? Colors.White : new Color(1, 1, 1, 0.3f);
        if (slider != null) { slider.Editable = enabled; slider.Modulate = dim; }
        if (label != null) label.Modulate = dim;
    }

    /// <summary>Enable/disable slider wrap container (dims slider + % label together).</summary>
    private static void SetSliderWrapEnabled(HSlider? slider, HBoxContainer? wrap, bool enabled)
    {
        if (slider != null) slider.Editable = enabled;
        if (wrap != null) wrap.Modulate = enabled ? Colors.White : new Color(1, 1, 1, 0.3f);
    }

    /// <summary>Set FPS dropdown to the closest match for the monitor refresh rate.</summary>
    private void SelectMonitorRefreshRate()
    {
        if (_optFpsLimit == null) return;
        float rate = (float)DisplayServer.ScreenGetRefreshRate();
        int hz = rate > 0 ? (int)System.Math.Round(rate) : 60;
        // Map to closest dropdown option: 60,120,144,165,240,unlimited
        int sel = hz switch
        {
            <= 75 => 0,   // 60
            <= 132 => 1,  // 120
            <= 154 => 2,  // 144
            <= 200 => 3,  // 165
            <= 300 => 4,  // 240
            _ => 5        // Sin limite
        };
        _optFpsLimit.Selected = sel;
    }

    /// <summary>Extract the Button (RpgCheckbox) from a row created by CreateRpgCheckboxRow.</summary>
    private static Button GetCheckboxFromRow(HBoxContainer row)
    {
        for (int i = row.GetChildCount() - 1; i >= 0; i--)
        {
            if (row.GetChild(i) is Button btn && btn.ToggleMode)
                return btn;
        }
        return null!;
    }

    /// <summary>Extract the HSlider from a row created by CreateRpgSliderRow.</summary>
    private static HSlider GetSliderFromRow(HBoxContainer row)
    {
        foreach (var child in row.GetChildren())
        {
            if (child is HSlider slider)
                return slider;
        }
        return null!;
    }

    /// <summary>Extract the value Label (last label child) from a row created by CreateRpgSliderRow.</summary>
    private static Label GetValueLabelFromRow(HBoxContainer row)
    {
        Label? last = null;
        foreach (var child in row.GetChildren())
        {
            if (child is Label lbl)
                last = lbl;
        }
        return last!;
    }

    /// <summary>Extract the OptionButton from a row created by CreateRpgDropdownRow.</summary>
    private static OptionButton GetDropdownFromRow(HBoxContainer row)
    {
        foreach (var child in row.GetChildren())
        {
            if (child is OptionButton opt)
                return opt;
        }
        return null!;
    }

    private static void SetCheck(Button? cb, bool val)
    {
        if (cb != null) cb.ButtonPressed = val;
    }

    private static bool IsChecked(Button? cb) => cb?.ButtonPressed ?? false;
}
