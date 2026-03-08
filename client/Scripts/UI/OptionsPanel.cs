using Godot;
using System;
using System.Collections.Generic;
using TierrasSagradasAO.Game;
using TierrasSagradasAO.Network;

namespace TierrasSagradasAO.UI;

/// <summary>
/// VB6 frmOpcionesNew — Options panel with 3 tabs: Juego, Controles, Render.
/// Changes apply immediately. Draggable by title bar. X button to close.
/// Saves to Data/INIT/Options.tsao on every change.
/// </summary>
public partial class OptionsPanel : PanelContainer
{
    private const int PanelW = 420;
    private const int MaxPanelH = 560; // cap so panel never exceeds 600px window
    private const int TitleBarH = 28;

    private GameState? _state;
    private GameConfig? _config;
    private string _dataPath = "";

    // Dragging state
    private bool _dragging;
    private Vector2 _dragOffset;

    private AoTcpClient? _tcp;

    // Tab containers
    private Control? _gameTab;
    private Control? _controlsTab;
    private Control? _renderTab;
    private Control? _clanTab;
    private Button? _gameTabBtn;
    private Button? _controlsTabBtn;
    private Button? _renderTabBtn;
    private Button? _clanTabBtn;
    private int _activeTab;

    // ── Clan tab controls ──
    private VBoxContainer? _clanContent;
    private bool _clanTabRequested;

    // ── Game tab controls ──
    private CheckBox? _chkMusic;
    private CheckBox? _chkSfx;
    private HSlider? _sldMusicVol;
    private HSlider? _sldSfxVol;
    private Label? _lblMusicVol;
    private Label? _lblSfxVol;
    private CheckBox? _chkGlobalChat;
    private CheckBox? _chkPrivateChat;
    private CheckBox? _chkBuffTimers;
    private CheckBox? _chkContactSignIn;
    private CheckBox? _chkContactSignOut;
    private CheckBox? _chkChatSound;
    private CheckBox? _chkVsync;
    private OptionButton? _optFpsLimit;

    // ── Controls tab controls ──
    private Button? _btnKeyConfig;
    private CheckBox? _chkMouseDClick;
    private CheckBox? _chkMouseRClick;
    private CheckBox? _chkMouseContext;

    // ── Render tab controls (Display) ──
    private CheckBox? _chkFullscreen;
    private HBoxContainer? _aspectRow;
    private OptionButton? _optAspect;

    // ── Render tab controls ──
    private OptionButton? _optPerformance;
    private CheckBox? _chkAuras;
    private CheckBox? _chkParticles;
    private CheckBox? _chkShadows;
    private CheckBox? _chkNpcShadows;
    private CheckBox? _chkReflections;
    private CheckBox? _chkDayNight;
    private CheckBox? _chkNames;
    private CheckBox? _chkLights;
    private CheckBox? _chkTreeTransparency;
    private CheckBox? _chkDeadTransparency;
    private CheckBox? _chkMinimap;
    private CheckBox? _chkMinimapPos;
    private CheckBox? _chkDeathDialog;

    // Suppress initial load toggles from triggering saves
    private bool _loading;

    // Callback for when config is applied (Main.cs hooks into this)
    public event System.Action? OnConfigApplied;

    // Callback to open the key binding panel
    public event System.Action? OnOpenKeyBinds;

    public void Init(GameState state, GameConfig config, string dataPath, AoTcpClient? tcp = null)
    {
        _state = state;
        _config = config;
        _dataPath = dataPath;
        _tcp = tcp;
    }

    public override void _Ready()
    {
        CustomMinimumSize = new Vector2(PanelW, 0);
        Size = new Vector2(PanelW, MaxPanelH);
        MouseFilter = MouseFilterEnum.Stop;
        ClipContents = true;

        // Panel style
        var style = new StyleBoxFlat();
        style.BgColor = new Color(0.10f, 0.10f, 0.15f, 0.97f);
        style.BorderColor = new Color(0.55f, 0.48f, 0.28f);
        style.SetBorderWidthAll(2);
        style.SetCornerRadiusAll(4);
        style.SetContentMarginAll(8);
        AddThemeStyleboxOverride("panel", style);

        var root = new VBoxContainer();
        root.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);

        // Title bar with close button
        var titleBar = new HBoxContainer();
        titleBar.CustomMinimumSize = new Vector2(0, TitleBarH);

        var title = new Label();
        title.Text = "Opciones";
        title.HorizontalAlignment = HorizontalAlignment.Center;
        title.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        title.AddThemeFontSizeOverride("font_size", 16);
        title.AddThemeColorOverride("font_color", new Color(1f, 0.85f, 0.4f));
        titleBar.AddChild(title);

        var closeBtn = new Button();
        closeBtn.Text = "X";
        closeBtn.CustomMinimumSize = new Vector2(28, 28);
        closeBtn.AddThemeFontSizeOverride("font_size", 13);
        closeBtn.FocusMode = FocusModeEnum.None;
        var closeBtnStyle = new StyleBoxFlat();
        closeBtnStyle.BgColor = new Color(0.6f, 0.15f, 0.15f, 0.9f);
        closeBtnStyle.SetCornerRadiusAll(3);
        closeBtn.AddThemeStyleboxOverride("normal", closeBtnStyle);
        var closeBtnHover = new StyleBoxFlat();
        closeBtnHover.BgColor = new Color(0.8f, 0.2f, 0.2f, 0.9f);
        closeBtnHover.SetCornerRadiusAll(3);
        closeBtn.AddThemeStyleboxOverride("hover", closeBtnHover);
        closeBtn.Pressed += Close;
        titleBar.AddChild(closeBtn);

        root.AddChild(titleBar);
        root.AddChild(Spacer(2));

        // Tab buttons row
        var tabRow = new HBoxContainer();
        tabRow.Alignment = BoxContainer.AlignmentMode.Center;

        _gameTabBtn = MakeTabButton("Juego");
        _gameTabBtn.Pressed += () => SetTab(0);
        tabRow.AddChild(_gameTabBtn);

        _controlsTabBtn = MakeTabButton("Controles");
        _controlsTabBtn.Pressed += () => SetTab(1);
        tabRow.AddChild(_controlsTabBtn);

        _renderTabBtn = MakeTabButton("Render");
        _renderTabBtn.Pressed += () => SetTab(2);
        tabRow.AddChild(_renderTabBtn);

        root.AddChild(tabRow);
        root.AddChild(Spacer(4));

        // Separator
        var sep = new HSeparator();
        sep.AddThemeConstantOverride("separation", 2);
        root.AddChild(sep);
        root.AddChild(Spacer(4));

        // Tab content area — scroll if content exceeds max height
        var scroll = new ScrollContainer();
        scroll.SizeFlagsVertical = SizeFlags.ExpandFill;
        scroll.HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled;
        scroll.VerticalScrollMode = ScrollContainer.ScrollMode.Auto;

        var tabHost = new VBoxContainer();
        tabHost.SizeFlagsHorizontal = SizeFlags.ExpandFill;

        _gameTab = BuildGameTab();
        _controlsTab = BuildControlsTab();
        _renderTab = BuildRenderTab();
        tabHost.AddChild(_gameTab);
        tabHost.AddChild(_controlsTab);
        tabHost.AddChild(_renderTab);

        scroll.AddChild(tabHost);
        root.AddChild(scroll);

        AddChild(root);

        SetTab(0);
        Visible = false;
    }

    // ── Dragging + click-through prevention ─────────────

    public override void _GuiInput(InputEvent @event)
    {
        if (@event is InputEventMouseButton mb)
        {
            if (mb.ButtonIndex == MouseButton.Left)
            {
                if (mb.Pressed)
                {
                    // Start drag if clicking in title bar area
                    if (mb.Position.Y <= TitleBarH)
                    {
                        _dragging = true;
                        _dragOffset = mb.GlobalPosition - GlobalPosition;
                    }
                }
                else
                {
                    _dragging = false;
                }
            }
            // Consume all mouse clicks so they don't pass through
            AcceptEvent();
        }
        else if (@event is InputEventMouseMotion mm)
        {
            if (_dragging)
            {
                GlobalPosition = mm.GlobalPosition - _dragOffset;
                AcceptEvent();
            }
        }
    }

    // ── Tab builders ──────────────────────────────────────

    private VBoxContainer BuildGameTab()
    {
        var vbox = new VBoxContainer();

        // -- Audio section --
        vbox.AddChild(SectionLabel("Audio"));

        _chkMusic = MakeCheck("Musica habilitada");
        _chkMusic.Toggled += _ => ApplyImmediate();
        vbox.AddChild(_chkMusic);

        var musicVolRow = new HBoxContainer();
        musicVolRow.AddChild(SmallLabel("Volumen musica:"));
        _sldMusicVol = MakeSlider(0, 100, 70);
        _sldMusicVol.ValueChanged += v =>
        {
            if (_lblMusicVol != null) _lblMusicVol.Text = $"{(int)v}%";
            ApplyImmediate();
        };
        musicVolRow.AddChild(_sldMusicVol);
        _lblMusicVol = SmallLabel("70%", 40);
        musicVolRow.AddChild(_lblMusicVol);
        vbox.AddChild(musicVolRow);

        _chkSfx = MakeCheck("Efectos de sonido");
        _chkSfx.Toggled += _ => ApplyImmediate();
        vbox.AddChild(_chkSfx);

        var sfxVolRow = new HBoxContainer();
        sfxVolRow.AddChild(SmallLabel("Volumen FX:"));
        _sldSfxVol = MakeSlider(0, 100, 100);
        _sldSfxVol.ValueChanged += v =>
        {
            if (_lblSfxVol != null) _lblSfxVol.Text = $"{(int)v}%";
            ApplyImmediate();
        };
        sfxVolRow.AddChild(_sldSfxVol);
        _lblSfxVol = SmallLabel("100%", 40);
        sfxVolRow.AddChild(_lblSfxVol);
        vbox.AddChild(sfxVolRow);

        vbox.AddChild(Spacer(8));

        // -- Chat section --
        vbox.AddChild(SectionLabel("Chat / Consola"));

        _chkGlobalChat = MakeCheck("Mostrar mensajes globales");
        _chkGlobalChat.Toggled += _ => ApplyImmediate();
        vbox.AddChild(_chkGlobalChat);

        _chkPrivateChat = MakeCheck("Mostrar mensajes privados");
        _chkPrivateChat.Toggled += _ => ApplyImmediate();
        vbox.AddChild(_chkPrivateChat);

        _chkBuffTimers = MakeCheck("Mostrar contadores de buffs");
        _chkBuffTimers.Toggled += _ => ApplyImmediate();
        vbox.AddChild(_chkBuffTimers);

        _chkContactSignIn = MakeCheck("Notificar conexion de contactos");
        _chkContactSignIn.Toggled += _ => ApplyImmediate();
        vbox.AddChild(_chkContactSignIn);

        _chkContactSignOut = MakeCheck("Notificar desconexion de contactos");
        _chkContactSignOut.Toggled += _ => ApplyImmediate();
        vbox.AddChild(_chkContactSignOut);

        _chkChatSound = MakeCheck("Alerta sonora de mensajes");
        _chkChatSound.Toggled += _ => ApplyImmediate();
        vbox.AddChild(_chkChatSound);

        vbox.AddChild(Spacer(8));

        // -- Performance section --
        vbox.AddChild(SectionLabel("Rendimiento"));

        _chkVsync = MakeCheck("V-Sync (sincronizar con monitor)");
        _chkVsync.Toggled += _ => ApplyImmediate();
        vbox.AddChild(_chkVsync);

        var fpsRow = new HBoxContainer();
        fpsRow.AddChild(SmallLabel("Limite FPS:"));
        _optFpsLimit = new OptionButton();
        _optFpsLimit.AddItem("60 FPS", 0);
        _optFpsLimit.AddItem("120 FPS", 1);
        _optFpsLimit.AddItem("144 FPS", 2);
        _optFpsLimit.AddItem("165 FPS", 3);
        _optFpsLimit.AddItem("240 FPS", 4);
        _optFpsLimit.AddItem("Sin limite", 5);
        _optFpsLimit.CustomMinimumSize = new Vector2(120, 0);
        _optFpsLimit.AddThemeFontSizeOverride("font_size", 11);
        _optFpsLimit.ItemSelected += _ => ApplyImmediate();
        fpsRow.AddChild(_optFpsLimit);
        vbox.AddChild(fpsRow);

        return vbox;
    }

    private VBoxContainer BuildControlsTab()
    {
        var vbox = new VBoxContainer();

        vbox.AddChild(SectionLabel("Configuracion de Teclas"));

        _btnKeyConfig = new Button();
        _btnKeyConfig.Text = "Configurar Teclas";
        _btnKeyConfig.CustomMinimumSize = new Vector2(250, 32);
        _btnKeyConfig.Pressed += () => OnOpenKeyBinds?.Invoke();
        vbox.AddChild(_btnKeyConfig);

        vbox.AddChild(Spacer(12));
        vbox.AddChild(SectionLabel("Raton"));

        _chkMouseDClick = MakeCheck("Doble click para interactuar");
        _chkMouseDClick.Toggled += _ => ApplyImmediate();
        vbox.AddChild(_chkMouseDClick);

        _chkMouseRClick = MakeCheck("Click derecho como doble click");
        _chkMouseRClick.Toggled += _ => ApplyImmediate();
        vbox.AddChild(_chkMouseRClick);

        _chkMouseContext = MakeCheck("Menu contextual al hacer click derecho");
        _chkMouseContext.Toggled += _ => ApplyImmediate();
        vbox.AddChild(_chkMouseContext);

        return vbox;
    }

    private VBoxContainer BuildRenderTab()
    {
        var vbox = new VBoxContainer();

        // -- Display section --
        vbox.AddChild(SectionLabel("Pantalla"));

        _chkFullscreen = MakeCheck("Pantalla Completa");
        _chkFullscreen.Toggled += on =>
        {
            if (_aspectRow != null) _aspectRow.Visible = on;
            ApplyImmediate();
        };
        vbox.AddChild(_chkFullscreen);

        _aspectRow = new HBoxContainer();
        _aspectRow.AddChild(SmallLabel("Aspecto:"));
        _optAspect = new OptionButton();
        _optAspect.AddItem("Ratio 4:3", 0);
        _optAspect.AddItem("Ratio 16:9", 1);
        _optAspect.CustomMinimumSize = new Vector2(220, 0);
        _optAspect.AddThemeFontSizeOverride("font_size", 11);
        _optAspect.Disabled = true;
        _aspectRow.AddChild(_optAspect);
        vbox.AddChild(_aspectRow);

        vbox.AddChild(Spacer(8));

        // Performance preset as dropdown selector
        vbox.AddChild(SectionLabel("Calidad Grafica"));

        var qualityRow = new HBoxContainer();
        qualityRow.AddChild(SmallLabel("Preset:"));
        _optPerformance = new OptionButton();
        _optPerformance.AddItem("Minimo", 0);
        _optPerformance.AddItem("Bajo", 1);
        _optPerformance.AddItem("Medio", 2);
        _optPerformance.AddItem("Alto", 3);
        _optPerformance.AddItem("Maximo", 4);
        _optPerformance.CustomMinimumSize = new Vector2(140, 0);
        _optPerformance.AddThemeFontSizeOverride("font_size", 11);
        _optPerformance.ItemSelected += OnPerformanceSelected;
        qualityRow.AddChild(_optPerformance);
        vbox.AddChild(qualityRow);

        vbox.AddChild(Spacer(8));

        // -- Effects section in two columns --
        vbox.AddChild(SectionLabel("Efectos Visuales"));

        var cols = new HBoxContainer();
        cols.SizeFlagsHorizontal = SizeFlags.ExpandFill;

        // Left column
        var leftCol = new VBoxContainer();
        leftCol.SizeFlagsHorizontal = SizeFlags.ExpandFill;

        _chkAuras = MakeCheck("Auras");
        _chkAuras.Toggled += _ => ApplyImmediate();
        leftCol.AddChild(_chkAuras);

        _chkParticles = MakeCheck("Particulas");
        _chkParticles.Toggled += _ => ApplyImmediate();
        leftCol.AddChild(_chkParticles);

        _chkShadows = MakeCheck("Sombras PJ");
        _chkShadows.Toggled += _ => ApplyImmediate();
        leftCol.AddChild(_chkShadows);

        _chkNpcShadows = MakeCheck("Sombras NPC");
        _chkNpcShadows.Toggled += _ => ApplyImmediate();
        leftCol.AddChild(_chkNpcShadows);

        _chkLights = MakeCheck("Luces");
        _chkLights.Toggled += _ => ApplyImmediate();
        leftCol.AddChild(_chkLights);

        _chkTreeTransparency = MakeCheck("Transp. arboles");
        _chkTreeTransparency.Toggled += _ => ApplyImmediate();
        leftCol.AddChild(_chkTreeTransparency);

        cols.AddChild(leftCol);

        // Right column
        var rightCol = new VBoxContainer();
        rightCol.SizeFlagsHorizontal = SizeFlags.ExpandFill;

        _chkReflections = MakeCheck("Reflejos agua");
        _chkReflections.Toggled += _ => ApplyImmediate();
        rightCol.AddChild(_chkReflections);

        _chkDayNight = MakeCheck("Dia/Noche");
        _chkDayNight.Toggled += _ => ApplyImmediate();
        rightCol.AddChild(_chkDayNight);

        _chkNames = MakeCheck("Nombres");
        _chkNames.Toggled += _ => ApplyImmediate();
        rightCol.AddChild(_chkNames);

        _chkDeadTransparency = MakeCheck("Transp. muertos");
        _chkDeadTransparency.Toggled += _ => ApplyImmediate();
        rightCol.AddChild(_chkDeadTransparency);

        _chkMinimap = MakeCheck("Minimapa");
        _chkMinimap.Toggled += _ => ApplyImmediate();
        rightCol.AddChild(_chkMinimap);

        _chkMinimapPos = MakeCheck("Pos. minimapa");
        _chkMinimapPos.Toggled += _ => ApplyImmediate();
        rightCol.AddChild(_chkMinimapPos);

        cols.AddChild(rightCol);
        vbox.AddChild(cols);

        vbox.AddChild(Spacer(6));

        _chkDeathDialog = MakeCheck("Mostrar cartel de muerte");
        _chkDeathDialog.Toggled += _ => ApplyImmediate();
        vbox.AddChild(_chkDeathDialog);

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
        Visible = true;
        SetTab(0);
    }

    public void Close()
    {
        if (_state != null)
            _state.OptionsPanelOpen = false;
        Visible = false;
    }

    // ── Immediate apply on any control change ────────────

    private void ApplyImmediate()
    {
        if (_loading || _config == null) return;

        SaveControlsToConfig(_config);
        _config.Save(_dataPath);
        OnConfigApplied?.Invoke();
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
        if (_controlsTab != null) _controlsTab.Visible = idx == 1;
        if (_renderTab != null) _renderTab.Visible = idx == 2;

        // Highlight active tab button
        var activeColor = new Color(1f, 0.85f, 0.4f);
        var inactiveColor = new Color(0.7f, 0.7f, 0.7f);
        if (_gameTabBtn != null) _gameTabBtn.Modulate = idx == 0 ? activeColor : inactiveColor;
        if (_controlsTabBtn != null) _controlsTabBtn.Modulate = idx == 1 ? activeColor : inactiveColor;
        if (_renderTabBtn != null) _renderTabBtn.Modulate = idx == 2 ? activeColor : inactiveColor;
    }

    // ── Config ↔ Controls sync ────────────────────────────

    private void LoadControlsFromConfig(GameConfig cfg)
    {
        // Game tab
        SetCheck(_chkMusic, cfg.MusicEnabled);
        SetCheck(_chkSfx, cfg.SfxEnabled);
        if (_sldMusicVol != null) _sldMusicVol.Value = cfg.MusicVolume;
        if (_sldSfxVol != null) _sldSfxVol.Value = cfg.SfxVolume;
        if (_lblMusicVol != null) _lblMusicVol.Text = $"{cfg.MusicVolume}%";
        if (_lblSfxVol != null) _lblSfxVol.Text = $"{cfg.SfxVolume}%";

        SetCheck(_chkGlobalChat, cfg.ShowGlobalChat);
        SetCheck(_chkPrivateChat, cfg.ShowPrivateChat);
        SetCheck(_chkBuffTimers, cfg.ShowBuffTimers);
        SetCheck(_chkContactSignIn, cfg.ContactSignIn);
        SetCheck(_chkContactSignOut, cfg.ContactSignOut);
        SetCheck(_chkChatSound, cfg.ChatSoundAlert);

        // V-Sync & FPS
        SetCheck(_chkVsync, cfg.VsyncEnabled);
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

        // Controls tab
        SetCheck(_chkMouseDClick, cfg.MouseDoubleClick);
        SetCheck(_chkMouseRClick, cfg.MouseRightClick);
        SetCheck(_chkMouseContext, cfg.MouseContextMenu);

        // Render tab — Display
        SetCheck(_chkFullscreen, cfg.Fullscreen);
        if (_aspectRow != null) _aspectRow.Visible = cfg.Fullscreen;
        if (_optAspect != null) _optAspect.Selected = cfg.AspectRatioMode;

        // Render tab
        if (_optPerformance != null) _optPerformance.Selected = cfg.PerformanceLevel;
        LoadRenderChecks(cfg);
    }

    private void LoadRenderChecks(GameConfig cfg)
    {
        SetCheck(_chkAuras, cfg.ShowAuras);
        SetCheck(_chkParticles, cfg.ShowParticles);
        SetCheck(_chkShadows, cfg.ShowShadows);
        SetCheck(_chkNpcShadows, cfg.ShowNpcShadows);
        SetCheck(_chkReflections, cfg.ShowReflections);
        SetCheck(_chkDayNight, cfg.ShowDayNight);
        SetCheck(_chkNames, cfg.ShowNames);
        SetCheck(_chkLights, cfg.ShowLights);
        SetCheck(_chkTreeTransparency, cfg.TreeRoofTransparency);
        SetCheck(_chkDeadTransparency, cfg.DeadCharTransparency);
        SetCheck(_chkMinimap, cfg.ShowMinimap);
        SetCheck(_chkMinimapPos, cfg.ShowMinimapPosition);
        SetCheck(_chkDeathDialog, cfg.ShowDeathDialog);
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
        cfg.ContactSignIn = IsChecked(_chkContactSignIn);
        cfg.ContactSignOut = IsChecked(_chkContactSignOut);
        cfg.ChatSoundAlert = IsChecked(_chkChatSound);

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

        // Render tab — Display
        cfg.Fullscreen = IsChecked(_chkFullscreen);
        cfg.AspectRatioMode = _optAspect?.Selected ?? 0;

        // Render tab
        cfg.PerformanceLevel = _optPerformance?.Selected ?? 2;
        cfg.ShowAuras = IsChecked(_chkAuras);
        cfg.ShowParticles = IsChecked(_chkParticles);
        cfg.ShowShadows = IsChecked(_chkShadows);
        cfg.ShowNpcShadows = IsChecked(_chkNpcShadows);
        cfg.ShowReflections = IsChecked(_chkReflections);
        cfg.ShowDayNight = IsChecked(_chkDayNight);
        cfg.ShowNames = IsChecked(_chkNames);
        cfg.ShowLights = IsChecked(_chkLights);
        cfg.TreeRoofTransparency = IsChecked(_chkTreeTransparency);
        cfg.DeadCharTransparency = IsChecked(_chkDeadTransparency);
        cfg.ShowMinimap = IsChecked(_chkMinimap);
        cfg.ShowMinimapPosition = IsChecked(_chkMinimapPos);
        cfg.ShowDeathDialog = IsChecked(_chkDeathDialog);
    }

    // ── Clan tab ──────────────────────────────────────────

    private VBoxContainer BuildClanTab()
    {
        var vbox = new VBoxContainer();
        _clanContent = new VBoxContainer();
        _clanContent.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        vbox.AddChild(_clanContent);

        // Initial placeholder
        var placeholder = new Label();
        placeholder.Text = "Cargando informacion del clan...";
        placeholder.AddThemeFontSizeOverride("font_size", 11);
        _clanContent.AddChild(placeholder);

        return vbox;
    }

    /// <summary>
    /// Called from Main.cs when server responds with guild info.
    /// Rebuilds clan tab content based on GuildInfoType.
    /// </summary>
    public void UpdateClanContent()
    {
        if (_clanContent == null || _state == null) return;

        // Clear existing content
        foreach (var child in _clanContent.GetChildren())
            child.QueueFree();

        string guildName = _state.UserGuildName;
        string infoType = _state.GuildInfoType;

        if (infoType == "Leader")
            BuildClanLeaderContent();
        else if (infoType == "Member")
            BuildClanMemberContent();
        else if (infoType == "Details")
            BuildClanDetailsContent();
        else
            BuildClanNoGuildContent();
    }

    /// <summary>Open options directly on the Clanes tab.</summary>
    public void OpenClanTab()
    {
        if (_state == null || _config == null) return;
        _loading = true;
        LoadControlsFromConfig(_config);
        _loading = false;
        _state.OptionsPanelOpen = true;
        Visible = true;
        _clanTabRequested = false; // allow re-request
        SetTab(3);
    }

    // ── No Guild View (VB6: frmGuildMember without a guild) ──

    private void BuildClanNoGuildContent()
    {
        if (_clanContent == null || _state == null) return;

        _clanContent.AddChild(SectionLabel("Sin Clan"));

        var infoLabel = new Label();
        infoLabel.Text = "No perteneces a ningun clan.";
        infoLabel.AddThemeFontSizeOverride("font_size", 11);
        _clanContent.AddChild(infoLabel);

        _clanContent.AddChild(Spacer(8));

        // Found clan button
        var foundBtn = new Button();
        foundBtn.Text = "Fundar Clan";
        foundBtn.CustomMinimumSize = new Vector2(200, 30);
        foundBtn.AddThemeFontSizeOverride("font_size", 12);
        foundBtn.Pressed += () =>
        {
            if (_tcp != null)
                _tcp.SendPacket(ClientPackets.WriteTalk("/FUNDARCLAN"));
        };
        _clanContent.AddChild(foundBtn);

        _clanContent.AddChild(Spacer(8));

        // Guild list
        _clanContent.AddChild(SectionLabel("Clanes Disponibles"));

        // Search filter
        var searchRow = new HBoxContainer();
        searchRow.AddChild(SmallLabel("Buscar:"));
        var searchEdit = new LineEdit();
        searchEdit.PlaceholderText = "Filtrar clanes...";
        searchEdit.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        searchEdit.AddThemeFontSizeOverride("font_size", 11);
        searchRow.AddChild(searchEdit);
        _clanContent.AddChild(searchRow);

        var guildList = new ItemList();
        guildList.CustomMinimumSize = new Vector2(0, 160);
        guildList.AddThemeFontSizeOverride("font_size", 11);
        _clanContent.AddChild(guildList);

        // Parse guild list data
        var guilds = new List<string>();
        var listData = _state.GuildListData;
        if (!string.IsNullOrEmpty(listData))
        {
            var parts = listData.Split(',');
            for (int i = 1; i < parts.Length; i++)
            {
                guilds.Add(parts[i]);
                var fields = parts[i].Split('-');
                string display = fields.Length >= 3 ? $"{fields[0]}  [{fields[1]}]  Nv.{fields[2]}" : parts[i];
                guildList.AddItem(display);
            }
        }

        // Filter functionality
        searchEdit.TextChanged += filter =>
        {
            guildList.Clear();
            for (int i = 0; i < guilds.Count; i++)
            {
                if (string.IsNullOrEmpty(filter) || guilds[i].Contains(filter, StringComparison.OrdinalIgnoreCase))
                {
                    var fields = guilds[i].Split('-');
                    string display = fields.Length >= 3 ? $"{fields[0]}  [{fields[1]}]  Nv.{fields[2]}" : guilds[i];
                    guildList.AddItem(display);
                }
            }
        };

        // Details button
        var detailsBtn = new Button();
        detailsBtn.Text = "Ver Detalles";
        detailsBtn.AddThemeFontSizeOverride("font_size", 11);
        detailsBtn.Pressed += () =>
        {
            if (_tcp == null) return;
            var sel = guildList.GetSelectedItems();
            if (sel.Length == 0) return;
            string itemText = guildList.GetItemText(sel[0]);
            string gName = itemText.Split(' ')[0].Trim();
            _tcp.SendPacket(ClientPackets.WriteGuildDetails(gName));
        };
        _clanContent.AddChild(detailsBtn);

        _clanContent.AddChild(Spacer(4));

        // Petition + apply
        _clanContent.AddChild(SmallLabel("Solicitud de ingreso:"));
        var petitionEdit = new LineEdit();
        petitionEdit.PlaceholderText = "Escribe tu solicitud...";
        petitionEdit.AddThemeFontSizeOverride("font_size", 11);
        _clanContent.AddChild(petitionEdit);

        var applyBtn = new Button();
        applyBtn.Text = "Enviar Solicitud";
        applyBtn.AddThemeFontSizeOverride("font_size", 11);
        applyBtn.Pressed += () =>
        {
            if (_tcp == null) return;
            var sel = guildList.GetSelectedItems();
            if (sel.Length == 0) return;
            string itemText = guildList.GetItemText(sel[0]);
            string gName = itemText.Split(' ')[0].Trim();
            string petition = petitionEdit.Text.Trim();
            if (string.IsNullOrEmpty(petition)) petition = "Solicito ingresar.";
            _tcp.SendPacket(ClientPackets.WriteGuildApply($"{gName},{petition}"));
        };
        _clanContent.AddChild(applyBtn);
    }

    // ── Leader View (VB6: frmGuildLeader) ──

    private const char BF = '\u00BF';

    private void BuildClanLeaderContent()
    {
        if (_clanContent == null || _state == null) return;

        _clanContent.AddChild(SectionLabel("Administracion del Clan"));

        var data = _state.GuildInfoData;
        var parts = data.Split(BF);

        // Parse leader data
        string guildPoints = parts.Length > 0 ? parts[0] : "0";
        string guildLevel = parts.Length > 1 ? parts[1] : "1";
        string leader = parts.Length > 2 ? parts[2] : "?";
        string sub1 = parts.Length > 3 ? parts[3] : "";
        string sub2 = parts.Length > 4 ? parts[4] : "";
        string reputation = parts.Length > 9 ? parts[9] : "0";

        // Info
        var infoLabel = new Label();
        infoLabel.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        infoLabel.CustomMinimumSize = new Vector2(PanelW - 40, 0);
        infoLabel.AddThemeFontSizeOverride("font_size", 11);
        string infoText = $"Nivel: {guildLevel} | Puntos: {guildPoints}\n" +
            $"Lider: {leader}\n";
        // Only show sub-líderes if at least one is set
        bool hasSub1 = !string.IsNullOrEmpty(sub1) && sub1 != "default" && sub1 != "Fermin";
        bool hasSub2 = !string.IsNullOrEmpty(sub2) && sub2 != "default" && sub2 != "Fermin";
        if (hasSub1 || hasSub2)
        {
            var subs = new List<string>();
            if (hasSub1) subs.Add(sub1);
            if (hasSub2) subs.Add(sub2);
            infoText += $"Sub-lideres: {string.Join(", ", subs)}\n";
        }
        infoText += $"Reputacion: {reputation}";
        infoLabel.Text = infoText;
        _clanContent.AddChild(infoLabel);

        _clanContent.AddChild(Spacer(6));

        // Guild list (other clans)
        int idx = 13;
        var guildNames = new List<string>();
        if (idx < parts.Length)
        {
            int guildCount = 0;
            int.TryParse(parts[idx], out guildCount);
            idx++;
            for (int i = 0; i < guildCount && idx < parts.Length; i++, idx++)
                guildNames.Add(parts[idx]);
        }

        // Two columns: left=clans, right=members
        var cols = new HBoxContainer();
        cols.SizeFlagsHorizontal = SizeFlags.ExpandFill;

        // Left column — Clans
        var leftCol = new VBoxContainer();
        leftCol.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        leftCol.AddChild(SmallLabel("Clanes:"));

        var filterClans = new LineEdit();
        filterClans.PlaceholderText = "Filtrar...";
        filterClans.AddThemeFontSizeOverride("font_size", 10);
        leftCol.AddChild(filterClans);

        var guildList = new ItemList();
        guildList.CustomMinimumSize = new Vector2(0, 100);
        guildList.AddThemeFontSizeOverride("font_size", 10);
        foreach (var g in guildNames)
        {
            // Leader format: name$align$level
            var gf = g.Split('$');
            guildList.AddItem(gf.Length >= 1 ? gf[0] : g);
        }
        leftCol.AddChild(guildList);
        cols.AddChild(leftCol);

        filterClans.TextChanged += filter =>
        {
            guildList.Clear();
            foreach (var g in guildNames)
            {
                var gf = g.Split('$');
                string name = gf.Length >= 1 ? gf[0] : g;
                if (string.IsNullOrEmpty(filter) || name.Contains(filter, StringComparison.OrdinalIgnoreCase))
                    guildList.AddItem(name);
            }
        };

        // Right column — Members
        var rightCol = new VBoxContainer();
        rightCol.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        rightCol.AddChild(SmallLabel("Miembros:"));

        var filterMembers = new LineEdit();
        filterMembers.PlaceholderText = "Filtrar...";
        filterMembers.AddThemeFontSizeOverride("font_size", 10);
        rightCol.AddChild(filterMembers);

        var memberList = new ItemList();
        memberList.CustomMinimumSize = new Vector2(0, 100);
        memberList.AddThemeFontSizeOverride("font_size", 10);
        rightCol.AddChild(memberList);
        cols.AddChild(rightCol);

        // Parse members
        var memberNames = new List<string>();
        if (idx < parts.Length)
        {
            int memberCount = 0;
            int.TryParse(parts[idx], out memberCount);
            idx++;
            if (idx < parts.Length)
            {
                var mems = parts[idx].Split(',', StringSplitOptions.RemoveEmptyEntries);
                foreach (var m in mems) memberNames.Add(m.Trim());
                idx++;
            }
        }
        foreach (var m in memberNames) memberList.AddItem(m);

        filterMembers.TextChanged += filter =>
        {
            memberList.Clear();
            foreach (var m in memberNames)
                if (string.IsNullOrEmpty(filter) || m.Contains(filter, StringComparison.OrdinalIgnoreCase))
                    memberList.AddItem(m);
        };

        _clanContent.AddChild(cols);

        // News
        _clanContent.AddChild(Spacer(4));
        _clanContent.AddChild(SmallLabel("Noticias del clan:"));

        var newsEdit = new TextEdit();
        newsEdit.CustomMinimumSize = new Vector2(0, 50);
        newsEdit.AddThemeFontSizeOverride("font_size", 10);
        newsEdit.Text = _state.GuildNewsText;
        _clanContent.AddChild(newsEdit);

        var updateNewsBtn = new Button();
        updateNewsBtn.Text = "Actualizar Noticias";
        updateNewsBtn.AddThemeFontSizeOverride("font_size", 11);
        updateNewsBtn.Pressed += () =>
        {
            if (_tcp != null)
                _tcp.SendPacket(ClientPackets.WriteGuildNews(newsEdit.Text));
        };
        _clanContent.AddChild(updateNewsBtn);

        _clanContent.AddChild(Spacer(4));

        // Solicitudes
        _clanContent.AddChild(SmallLabel("Solicitudes pendientes:"));
        var applicantList = new ItemList();
        applicantList.CustomMinimumSize = new Vector2(0, 60);
        applicantList.AddThemeFontSizeOverride("font_size", 10);

        var applicants = new List<string>();
        if (idx < parts.Length)
        {
            int appCount = 0;
            int.TryParse(parts[idx], out appCount);
            idx++;
            for (int i = 0; i < appCount && idx < parts.Length; i++, idx++)
            {
                applicants.Add(parts[idx]);
                applicantList.AddItem(parts[idx]);
            }
        }
        _clanContent.AddChild(applicantList);

        var appBtnRow = new HBoxContainer();
        var acceptBtn = new Button();
        acceptBtn.Text = "Aceptar";
        acceptBtn.AddThemeFontSizeOverride("font_size", 11);
        acceptBtn.Pressed += () =>
        {
            if (_tcp == null) return;
            var sel = applicantList.GetSelectedItems();
            if (sel.Length == 0) return;
            string name = applicantList.GetItemText(sel[0]).Trim();
            _tcp.SendPacket(ClientPackets.WriteGuildAccept(name));
            _clanTabRequested = false;
            _tcp.SendPacket(ClientPackets.WriteGuildInfo());
        };
        appBtnRow.AddChild(acceptBtn);

        var rejectBtn = new Button();
        rejectBtn.Text = "Rechazar";
        rejectBtn.AddThemeFontSizeOverride("font_size", 11);
        rejectBtn.Pressed += () =>
        {
            if (_tcp == null) return;
            var sel = applicantList.GetSelectedItems();
            if (sel.Length == 0) return;
            string name = applicantList.GetItemText(sel[0]).Trim();
            _tcp.SendPacket(ClientPackets.WriteGuildReject($"{name},Rechazado"));
            _clanTabRequested = false;
            _tcp.SendPacket(ClientPackets.WriteGuildInfo());
        };
        appBtnRow.AddChild(rejectBtn);
        _clanContent.AddChild(appBtnRow);

        _clanContent.AddChild(Spacer(4));

        // Action buttons row
        var actionRow = new HBoxContainer();

        var detailsClanBtn = new Button();
        detailsClanBtn.Text = "Detalles Clan";
        detailsClanBtn.AddThemeFontSizeOverride("font_size", 10);
        detailsClanBtn.Pressed += () =>
        {
            if (_tcp == null) return;
            var sel = guildList.GetSelectedItems();
            if (sel.Length == 0) return;
            string name = guildList.GetItemText(sel[0]).Trim();
            _tcp.SendPacket(ClientPackets.WriteGuildDetails(name));
        };
        actionRow.AddChild(detailsClanBtn);

        var expelBtn = new Button();
        expelBtn.Text = "Expulsar";
        expelBtn.AddThemeFontSizeOverride("font_size", 10);
        expelBtn.Pressed += () =>
        {
            if (_tcp == null) return;
            var sel = memberList.GetSelectedItems();
            if (sel.Length == 0) return;
            string name = memberList.GetItemText(sel[0]).Trim();
            _tcp.SendPacket(ClientPackets.WriteGuildExpel(name));
            _clanTabRequested = false;
            _tcp.SendPacket(ClientPackets.WriteGuildInfo());
        };
        actionRow.AddChild(expelBtn);

        var closeGuildBtn = new Button();
        closeGuildBtn.Text = "Cerrar Clan";
        closeGuildBtn.AddThemeFontSizeOverride("font_size", 10);
        closeGuildBtn.AddThemeColorOverride("font_color", new Color(1f, 0.4f, 0.4f));
        closeGuildBtn.Pressed += () =>
        {
            if (_tcp != null)
            {
                _tcp.SendPacket(ClientPackets.WriteTalk("/CERRARCLAN"));
                _clanTabRequested = false;
                CallDeferred(nameof(RefreshClanTab));
            }
        };
        actionRow.AddChild(closeGuildBtn);

        _clanContent.AddChild(actionRow);
    }

    // ── Member View (VB6: frmGuildMember) ──

    private void BuildClanMemberContent()
    {
        if (_clanContent == null || _state == null) return;

        _clanContent.AddChild(SectionLabel("Mi Clan"));

        var data = _state.GuildInfoData;
        var parts = data.Split(BF);

        string guildPoints = parts.Length > 0 ? parts[0] : "0";
        string guildLevel = parts.Length > 1 ? parts[1] : "1";
        string leader = parts.Length > 2 ? parts[2] : "?";
        string sub1 = parts.Length > 3 ? parts[3] : "";
        string sub2 = parts.Length > 4 ? parts[4] : "";
        string reputation = parts.Length > 9 ? parts[9] : "0";

        var infoLabel = new Label();
        infoLabel.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        infoLabel.CustomMinimumSize = new Vector2(PanelW - 40, 0);
        infoLabel.AddThemeFontSizeOverride("font_size", 11);
        string infoText = $"Clan: {_state.UserGuildName}\n" +
            $"Nivel: {guildLevel} | Puntos: {guildPoints}\n" +
            $"Lider: {leader}\n";
        bool hasSub1 = !string.IsNullOrEmpty(sub1) && sub1 != "default" && sub1 != "Fermin";
        bool hasSub2 = !string.IsNullOrEmpty(sub2) && sub2 != "default" && sub2 != "Fermin";
        if (hasSub1 || hasSub2)
        {
            var subs = new List<string>();
            if (hasSub1) subs.Add(sub1);
            if (hasSub2) subs.Add(sub2);
            infoText += $"Sub-lideres: {string.Join(", ", subs)}\n";
        }
        infoText += $"Reputacion: {reputation}";
        infoLabel.Text = infoText;
        _clanContent.AddChild(infoLabel);

        _clanContent.AddChild(Spacer(6));

        // Two columns: clans list + members
        var cols = new HBoxContainer();
        cols.SizeFlagsHorizontal = SizeFlags.ExpandFill;

        // Left — other clans
        var leftCol = new VBoxContainer();
        leftCol.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        leftCol.AddChild(SmallLabel("Clanes:"));

        var searchClans = new LineEdit();
        searchClans.PlaceholderText = "Filtrar...";
        searchClans.AddThemeFontSizeOverride("font_size", 10);
        leftCol.AddChild(searchClans);

        var clanList = new ItemList();
        clanList.CustomMinimumSize = new Vector2(0, 120);
        clanList.AddThemeFontSizeOverride("font_size", 10);
        leftCol.AddChild(clanList);

        // Parse guild list from member data
        int parseIdx = 10;
        var guildNames = new List<string>();
        if (parseIdx < parts.Length)
        {
            int guildCount = 0;
            int.TryParse(parts[parseIdx], out guildCount);
            parseIdx++;
            for (int i = 0; i < guildCount && parseIdx < parts.Length; i++, parseIdx++)
            {
                guildNames.Add(parts[parseIdx]);
                var gf = parts[parseIdx].Split('-');
                clanList.AddItem(gf.Length >= 1 ? gf[0] : parts[parseIdx]);
            }
        }

        searchClans.TextChanged += filter =>
        {
            clanList.Clear();
            foreach (var g in guildNames)
            {
                var gf = g.Split('-');
                string n = gf.Length >= 1 ? gf[0] : g;
                if (string.IsNullOrEmpty(filter) || n.Contains(filter, StringComparison.OrdinalIgnoreCase))
                    clanList.AddItem(n);
            }
        };

        cols.AddChild(leftCol);

        // Right — members
        var rightCol = new VBoxContainer();
        rightCol.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        rightCol.AddChild(SmallLabel("Miembros:"));

        var memberList = new ItemList();
        memberList.CustomMinimumSize = new Vector2(0, 140);
        memberList.AddThemeFontSizeOverride("font_size", 10);
        rightCol.AddChild(memberList);

        var memberNames = new List<string>();
        if (parseIdx < parts.Length)
        {
            int memberCount = 0;
            int.TryParse(parts[parseIdx], out memberCount);
            parseIdx++;
            if (parseIdx < parts.Length)
            {
                var mems = parts[parseIdx].Split(',', StringSplitOptions.RemoveEmptyEntries);
                foreach (var m in mems) memberNames.Add(m.Trim());
            }
        }
        foreach (var m in memberNames) memberList.AddItem(m);

        var countLabel = SmallLabel($"Total: {memberNames.Count}");
        rightCol.AddChild(countLabel);
        cols.AddChild(rightCol);

        _clanContent.AddChild(cols);

        _clanContent.AddChild(Spacer(4));

        // Buttons
        var btnRow = new HBoxContainer();

        var detailsBtn = new Button();
        detailsBtn.Text = "Detalles Clan";
        detailsBtn.AddThemeFontSizeOverride("font_size", 11);
        detailsBtn.Pressed += () =>
        {
            if (_tcp == null) return;
            var sel = clanList.GetSelectedItems();
            if (sel.Length == 0) return;
            string name = clanList.GetItemText(sel[0]).Trim();
            _tcp.SendPacket(ClientPackets.WriteGuildDetails(name));
        };
        btnRow.AddChild(detailsBtn);

        var newsBtn = new Button();
        newsBtn.Text = "Noticias";
        newsBtn.AddThemeFontSizeOverride("font_size", 11);
        newsBtn.Pressed += () =>
        {
            // Request news from server (will come as a console/chat message)
            if (_tcp != null)
                _tcp.SendPacket(ClientPackets.WriteTalk("/NOTICIAS"));
        };
        btnRow.AddChild(newsBtn);

        var leaveBtn = new Button();
        leaveBtn.Text = "Abandonar Clan";
        leaveBtn.AddThemeFontSizeOverride("font_size", 11);
        leaveBtn.AddThemeColorOverride("font_color", new Color(1f, 0.4f, 0.4f));
        leaveBtn.Pressed += () =>
        {
            if (_tcp != null)
            {
                _tcp.SendPacket(ClientPackets.WriteTalk("/SALIRCLAN"));
                _clanTabRequested = false;
                // Delay refresh — server needs to process
                CallDeferred(nameof(RefreshClanTab));
            }
        };
        btnRow.AddChild(leaveBtn);

        _clanContent.AddChild(btnRow);
    }

    // ── Details View (VB6: frmGuildBrief) ──

    private void BuildClanDetailsContent()
    {
        if (_clanContent == null || _state == null) return;

        _clanContent.AddChild(SectionLabel("Detalles del Clan"));

        var data = _state.GuildInfoData;
        var parts = data.Split(BF);

        string level = parts.Length > 0 ? parts[0] : "?";
        string alignment = parts.Length > 1 ? parts[1] : "?";
        string repu = parts.Length > 2 ? parts[2] : "0";
        string founder = parts.Length > 3 ? parts[3] : "?";
        string date = parts.Length > 4 ? parts[4] : "?";
        string clanLeader = parts.Length > 5 ? parts[5] : "?";
        string sub1 = parts.Length > 6 ? parts[6] : "";
        string sub2 = parts.Length > 7 ? parts[7] : "";
        string memberCount = parts.Length > 8 ? parts[8] : "0";

        // Codex (indices 9-16)
        var codexLines = new List<string>();
        for (int i = 9; i < Math.Min(parts.Length, 17); i++)
            if (!string.IsNullOrWhiteSpace(parts[i])) codexLines.Add(parts[i]);

        string desc = parts.Length > 17 ? parts[17] : "";
        string clanName = parts.Length > 18 ? parts[18] : "";

        var detailsLabel = new Label();
        detailsLabel.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        detailsLabel.CustomMinimumSize = new Vector2(PanelW - 40, 0);
        detailsLabel.AddThemeFontSizeOverride("font_size", 11);
        string detailsText = $"Nombre: {clanName}\n" +
            $"Nivel: {level} | Alineacion: {alignment}\n" +
            $"Fundador: {founder} | Fecha: {date}\n" +
            $"Lider: {clanLeader}\n";
        bool dHasSub1 = !string.IsNullOrEmpty(sub1) && sub1 != "default" && sub1 != "Fermin";
        bool dHasSub2 = !string.IsNullOrEmpty(sub2) && sub2 != "default" && sub2 != "Fermin";
        if (dHasSub1 || dHasSub2)
        {
            var subs = new List<string>();
            if (dHasSub1) subs.Add(sub1);
            if (dHasSub2) subs.Add(sub2);
            detailsText += $"Sub-lideres: {string.Join(", ", subs)}\n";
        }
        detailsText += $"Miembros: {memberCount} | Reputacion: {repu}";
        detailsLabel.Text = detailsText;
        _clanContent.AddChild(detailsLabel);

        if (!string.IsNullOrEmpty(desc))
        {
            _clanContent.AddChild(Spacer(4));
            _clanContent.AddChild(SmallLabel("Descripcion:"));
            var descLabel = new Label();
            descLabel.AutowrapMode = TextServer.AutowrapMode.WordSmart;
            descLabel.CustomMinimumSize = new Vector2(PanelW - 40, 0);
            descLabel.AddThemeFontSizeOverride("font_size", 11);
            descLabel.Text = desc;
            _clanContent.AddChild(descLabel);
        }

        if (codexLines.Count > 0)
        {
            _clanContent.AddChild(Spacer(4));
            _clanContent.AddChild(SmallLabel("Codex:"));
            foreach (var line in codexLines)
            {
                var codexLabel = new Label();
                codexLabel.Text = $"  - {line}";
                codexLabel.AddThemeFontSizeOverride("font_size", 10);
                _clanContent.AddChild(codexLabel);
            }
        }

        _clanContent.AddChild(Spacer(8));

        // Buttons row
        var btnRow = new HBoxContainer();

        // Solicitar ingreso (only if no guild)
        if (string.IsNullOrEmpty(_state.UserGuildName))
        {
            var applyBtn = new Button();
            applyBtn.Text = "Solicitar Ingreso";
            applyBtn.AddThemeFontSizeOverride("font_size", 11);
            applyBtn.Pressed += () =>
            {
                if (_tcp != null && !string.IsNullOrEmpty(clanName))
                    _tcp.SendPacket(ClientPackets.WriteGuildApply($"{clanName},Solicito ingresar."));
            };
            btnRow.AddChild(applyBtn);
        }

        var backBtn = new Button();
        backBtn.Text = "Volver";
        backBtn.AddThemeFontSizeOverride("font_size", 11);
        backBtn.Pressed += () =>
        {
            _clanTabRequested = false;
            if (_tcp != null) _tcp.SendPacket(ClientPackets.WriteGuildInfo());
        };
        btnRow.AddChild(backBtn);

        _clanContent.AddChild(btnRow);
    }

    private void RefreshClanTab()
    {
        if (_tcp != null)
        {
            _clanTabRequested = false;
            _tcp.SendPacket(ClientPackets.WriteGuildInfo());
        }
    }

    // ── UI helpers ────────────────────────────────────────

    private static Button MakeTabButton(string text)
    {
        var btn = new Button();
        btn.Text = text;
        btn.CustomMinimumSize = new Vector2(90, 28);
        btn.AddThemeFontSizeOverride("font_size", 11);
        return btn;
    }

    // Shared checkbox icon textures (created once, reused for all checkboxes)
    private static Texture2D? _checkUncheckedTex;
    private static Texture2D? _checkCheckedTex;

    private static void EnsureCheckIcons()
    {
        if (_checkUncheckedTex != null) return;

        // Unchecked: 16x16 box with light border and dark fill
        var uncheckedImg = Image.CreateEmpty(16, 16, false, Image.Format.Rgba8);
        var border = new Color(0.7f, 0.65f, 0.5f);       // warm light border
        var fill = new Color(0.18f, 0.18f, 0.22f);        // dark fill (visible against panel bg)
        uncheckedImg.Fill(fill);
        for (int i = 0; i < 16; i++)
        {
            uncheckedImg.SetPixel(i, 0, border);
            uncheckedImg.SetPixel(i, 15, border);
            uncheckedImg.SetPixel(0, i, border);
            uncheckedImg.SetPixel(15, i, border);
        }
        _checkUncheckedTex = ImageTexture.CreateFromImage(uncheckedImg);

        // Checked: same box with a bright checkmark
        var checkedImg = Image.CreateEmpty(16, 16, false, Image.Format.Rgba8);
        checkedImg.Fill(fill);
        for (int i = 0; i < 16; i++)
        {
            checkedImg.SetPixel(i, 0, border);
            checkedImg.SetPixel(i, 15, border);
            checkedImg.SetPixel(0, i, border);
            checkedImg.SetPixel(15, i, border);
        }
        // Draw checkmark (simple diagonal lines)
        var check = new Color(1f, 0.85f, 0.3f); // gold checkmark
        // Short stroke: (3,8)→(6,11)
        checkedImg.SetPixel(3, 8, check); checkedImg.SetPixel(4, 9, check);
        checkedImg.SetPixel(5, 10, check); checkedImg.SetPixel(6, 11, check);
        // Long stroke: (6,11)→(12,5)
        checkedImg.SetPixel(7, 10, check); checkedImg.SetPixel(8, 9, check);
        checkedImg.SetPixel(9, 8, check); checkedImg.SetPixel(10, 7, check);
        checkedImg.SetPixel(11, 6, check); checkedImg.SetPixel(12, 5, check);
        // Thicken by 1px vertically
        checkedImg.SetPixel(3, 7, check); checkedImg.SetPixel(4, 8, check);
        checkedImg.SetPixel(5, 9, check); checkedImg.SetPixel(6, 10, check);
        checkedImg.SetPixel(7, 9, check); checkedImg.SetPixel(8, 8, check);
        checkedImg.SetPixel(9, 7, check); checkedImg.SetPixel(10, 6, check);
        checkedImg.SetPixel(11, 5, check); checkedImg.SetPixel(12, 4, check);
        _checkCheckedTex = ImageTexture.CreateFromImage(checkedImg);
    }

    private static CheckBox MakeCheck(string text)
    {
        EnsureCheckIcons();
        var cb = new CheckBox();
        cb.Text = text;
        cb.AddThemeFontSizeOverride("font_size", 11);
        cb.AddThemeIconOverride("unchecked", _checkUncheckedTex);
        cb.AddThemeIconOverride("checked", _checkCheckedTex);
        return cb;
    }

    private static HSlider MakeSlider(int min, int max, int value)
    {
        var slider = new HSlider();
        slider.MinValue = min;
        slider.MaxValue = max;
        slider.Value = value;
        slider.Step = 1;
        slider.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        slider.CustomMinimumSize = new Vector2(120, 0);
        return slider;
    }

    private static Label SectionLabel(string text)
    {
        var lbl = new Label();
        lbl.Text = text;
        lbl.AddThemeFontSizeOverride("font_size", 13);
        lbl.AddThemeColorOverride("font_color", new Color(0.85f, 0.75f, 0.45f));
        return lbl;
    }

    private static Label SmallLabel(string text, int minWidth = 0)
    {
        var lbl = new Label();
        lbl.Text = text;
        lbl.AddThemeFontSizeOverride("font_size", 11);
        if (minWidth > 0) lbl.CustomMinimumSize = new Vector2(minWidth, 0);
        return lbl;
    }

    private static Control Spacer(int size, bool horizontal = false)
    {
        var s = new Control();
        s.CustomMinimumSize = horizontal ? new Vector2(size, 0) : new Vector2(0, size);
        return s;
    }

    private static void SetCheck(CheckBox? cb, bool val)
    {
        if (cb != null) cb.ButtonPressed = val;
    }

    private static bool IsChecked(CheckBox? cb) => cb?.ButtonPressed ?? false;
}
