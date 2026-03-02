using Godot;
using TierrasSagradasAO.Game;

namespace TierrasSagradasAO.UI;

/// <summary>
/// VB6 frmOpcionesNew — Options panel with 3 tabs: Juego, Controles, Render.
/// Uses a temporary GameConfig copy for Cancel support.
/// Saves to Data/INIT/Options.tsao on accept.
/// </summary>
public partial class OptionsPanel : PanelContainer
{
    private const int PanelW = 400;
    private const int PanelH = 480;

    private GameState? _state;
    private GameConfig? _config;
    private GameConfig? _tempConfig;
    private string _dataPath = "";

    // Tab containers
    private Control? _gameTab;
    private Control? _controlsTab;
    private Control? _renderTab;
    private Button? _gameTabBtn;
    private Button? _controlsTabBtn;
    private Button? _renderTabBtn;
    private int _activeTab;

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
    private OptionButton? _optFpsLimit;

    // ── Controls tab controls ──
    private Button? _btnKeyConfig;
    private CheckBox? _chkMouseDClick;
    private CheckBox? _chkMouseRClick;
    private CheckBox? _chkMouseContext;

    // ── Render tab controls ──
    private HSlider? _sldPerformance;
    private Label? _lblPerformance;
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

    // Callback for when config is applied (Main.cs hooks into this)
    public event System.Action? OnConfigApplied;

    // Callback to open the key binding panel
    public event System.Action? OnOpenKeyBinds;

    public void Init(GameState state, GameConfig config, string dataPath)
    {
        _state = state;
        _config = config;
        _dataPath = dataPath;
    }

    public override void _Ready()
    {
        CustomMinimumSize = new Vector2(PanelW, PanelH);
        Size = new Vector2(PanelW, PanelH);
        MouseFilter = MouseFilterEnum.Stop;

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

        // Title
        var title = new Label();
        title.Text = "Opciones";
        title.HorizontalAlignment = HorizontalAlignment.Center;
        title.AddThemeFontSizeOverride("font_size", 16);
        title.AddThemeColorOverride("font_color", new Color(1f, 0.85f, 0.4f));
        root.AddChild(title);
        root.AddChild(Spacer(4));

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
        root.AddChild(Spacer(6));

        // Separator
        var sep = new HSeparator();
        sep.AddThemeConstantOverride("separation", 2);
        root.AddChild(sep);
        root.AddChild(Spacer(4));

        // Tab content area (ScrollContainer for overflow)
        var scroll = new ScrollContainer();
        scroll.SizeFlagsVertical = SizeFlags.ExpandFill;
        scroll.HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled;

        var tabHost = new Control();
        tabHost.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        tabHost.SizeFlagsVertical = SizeFlags.ExpandFill;

        _gameTab = BuildGameTab();
        _controlsTab = BuildControlsTab();
        _renderTab = BuildRenderTab();

        tabHost.AddChild(_gameTab);
        tabHost.AddChild(_controlsTab);
        tabHost.AddChild(_renderTab);

        scroll.AddChild(tabHost);
        root.AddChild(scroll);

        root.AddChild(Spacer(6));

        // Bottom buttons
        var btnRow = new HBoxContainer();
        btnRow.Alignment = BoxContainer.AlignmentMode.Center;

        var saveBtn = new Button();
        saveBtn.Text = "Aceptar";
        saveBtn.CustomMinimumSize = new Vector2(100, 32);
        saveBtn.Pressed += OnAccept;
        btnRow.AddChild(saveBtn);

        btnRow.AddChild(Spacer(10, true));

        var cancelBtn = new Button();
        cancelBtn.Text = "Cancelar";
        cancelBtn.CustomMinimumSize = new Vector2(100, 32);
        cancelBtn.Pressed += OnCancel;
        btnRow.AddChild(cancelBtn);

        root.AddChild(btnRow);
        AddChild(root);

        SetTab(0);
        Visible = false;
    }

    // ── Tab builders ──────────────────────────────────────

    private VBoxContainer BuildGameTab()
    {
        var vbox = new VBoxContainer();

        // -- Audio section --
        vbox.AddChild(SectionLabel("Audio"));

        _chkMusic = MakeCheck("Música habilitada");
        vbox.AddChild(_chkMusic);

        var musicVolRow = new HBoxContainer();
        musicVolRow.AddChild(SmallLabel("Volumen música:"));
        _sldMusicVol = MakeSlider(0, 100, 70);
        _sldMusicVol.ValueChanged += v => { if (_lblMusicVol != null) _lblMusicVol.Text = $"{(int)v}%"; };
        musicVolRow.AddChild(_sldMusicVol);
        _lblMusicVol = SmallLabel("70%", 40);
        musicVolRow.AddChild(_lblMusicVol);
        vbox.AddChild(musicVolRow);

        _chkSfx = MakeCheck("Efectos de sonido");
        vbox.AddChild(_chkSfx);

        var sfxVolRow = new HBoxContainer();
        sfxVolRow.AddChild(SmallLabel("Volumen FX:"));
        _sldSfxVol = MakeSlider(0, 100, 100);
        _sldSfxVol.ValueChanged += v => { if (_lblSfxVol != null) _lblSfxVol.Text = $"{(int)v}%"; };
        sfxVolRow.AddChild(_sldSfxVol);
        _lblSfxVol = SmallLabel("100%", 40);
        sfxVolRow.AddChild(_lblSfxVol);
        vbox.AddChild(sfxVolRow);

        vbox.AddChild(Spacer(8));

        // -- Chat section --
        vbox.AddChild(SectionLabel("Chat / Consola"));

        _chkGlobalChat = MakeCheck("Mostrar mensajes globales");
        vbox.AddChild(_chkGlobalChat);

        _chkPrivateChat = MakeCheck("Mostrar mensajes privados");
        vbox.AddChild(_chkPrivateChat);

        _chkBuffTimers = MakeCheck("Mostrar contadores de buffs");
        vbox.AddChild(_chkBuffTimers);

        _chkContactSignIn = MakeCheck("Notificar conexión de contactos");
        vbox.AddChild(_chkContactSignIn);

        _chkContactSignOut = MakeCheck("Notificar desconexión de contactos");
        vbox.AddChild(_chkContactSignOut);

        _chkChatSound = MakeCheck("Alerta sonora de mensajes");
        vbox.AddChild(_chkChatSound);

        vbox.AddChild(Spacer(8));

        // -- FPS section --
        vbox.AddChild(SectionLabel("Rendimiento"));

        var fpsRow = new HBoxContainer();
        fpsRow.AddChild(SmallLabel("Límite FPS:"));
        _optFpsLimit = new OptionButton();
        _optFpsLimit.AddItem("18 FPS", 0);
        _optFpsLimit.AddItem("32 FPS", 1);
        _optFpsLimit.AddItem("65 FPS", 2);
        _optFpsLimit.AddItem("Sin límite", 3);
        _optFpsLimit.CustomMinimumSize = new Vector2(120, 0);
        _optFpsLimit.AddThemeFontSizeOverride("font_size", 11);
        fpsRow.AddChild(_optFpsLimit);
        vbox.AddChild(fpsRow);

        return vbox;
    }

    private VBoxContainer BuildControlsTab()
    {
        var vbox = new VBoxContainer();

        vbox.AddChild(SectionLabel("Configuración de Teclas"));

        _btnKeyConfig = new Button();
        _btnKeyConfig.Text = "Configurar Teclas";
        _btnKeyConfig.CustomMinimumSize = new Vector2(250, 32);
        _btnKeyConfig.Pressed += () => OnOpenKeyBinds?.Invoke();
        vbox.AddChild(_btnKeyConfig);

        vbox.AddChild(Spacer(12));
        vbox.AddChild(SectionLabel("Ratón"));

        _chkMouseDClick = MakeCheck("Doble click para interactuar");
        vbox.AddChild(_chkMouseDClick);

        _chkMouseRClick = MakeCheck("Click derecho como doble click");
        vbox.AddChild(_chkMouseRClick);

        _chkMouseContext = MakeCheck("Menú contextual al hacer click derecho");
        vbox.AddChild(_chkMouseContext);

        return vbox;
    }

    private VBoxContainer BuildRenderTab()
    {
        var vbox = new VBoxContainer();

        // Performance preset slider
        vbox.AddChild(SectionLabel("Calidad Gráfica"));

        var perfRow = new HBoxContainer();
        perfRow.AddChild(SmallLabel("Mínimo"));
        _sldPerformance = MakeSlider(0, 4, 2);
        _sldPerformance.Step = 1;
        _sldPerformance.CustomMinimumSize = new Vector2(180, 0);
        _sldPerformance.ValueChanged += OnPerformanceChanged;
        perfRow.AddChild(_sldPerformance);
        perfRow.AddChild(SmallLabel("Máximo"));
        _lblPerformance = SmallLabel("Medio", 60);
        _lblPerformance.AddThemeColorOverride("font_color", new Color(1f, 0.85f, 0.4f));
        perfRow.AddChild(_lblPerformance);
        vbox.AddChild(perfRow);

        vbox.AddChild(Spacer(8));

        // -- Effects section --
        vbox.AddChild(SectionLabel("Efectos Visuales"));

        _chkAuras = MakeCheck("Mostrar auras");
        vbox.AddChild(_chkAuras);

        _chkParticles = MakeCheck("Mostrar partículas");
        vbox.AddChild(_chkParticles);

        _chkShadows = MakeCheck("Sombras de personajes");
        vbox.AddChild(_chkShadows);

        _chkNpcShadows = MakeCheck("Sombras de NPCs");
        vbox.AddChild(_chkNpcShadows);

        _chkLights = MakeCheck("Iluminación dinámica");
        vbox.AddChild(_chkLights);

        _chkReflections = MakeCheck("Reflejos en agua");
        vbox.AddChild(_chkReflections);

        _chkDayNight = MakeCheck("Efectos día/noche");
        vbox.AddChild(_chkDayNight);

        _chkNames = MakeCheck("Mostrar nombres de personajes");
        vbox.AddChild(_chkNames);

        vbox.AddChild(Spacer(8));

        // -- Transparency section --
        vbox.AddChild(SectionLabel("Transparencia"));

        _chkTreeTransparency = MakeCheck("Transparencia en árboles/techos");
        vbox.AddChild(_chkTreeTransparency);

        _chkDeadTransparency = MakeCheck("Transparencia en personajes muertos");
        vbox.AddChild(_chkDeadTransparency);

        vbox.AddChild(Spacer(8));

        // -- Interface section --
        vbox.AddChild(SectionLabel("Interfaz"));

        _chkMinimap = MakeCheck("Mostrar minimapa");
        vbox.AddChild(_chkMinimap);

        _chkMinimapPos = MakeCheck("Mostrar posición en minimapa");
        vbox.AddChild(_chkMinimapPos);

        _chkDeathDialog = MakeCheck("Mostrar cartel de muerte");
        vbox.AddChild(_chkDeathDialog);

        return vbox;
    }

    // ── Open / Close ──────────────────────────────────────

    public void Open()
    {
        if (_state == null || _config == null) return;

        // Create temp copy for editing
        _tempConfig = _config.Clone();
        LoadControlsFromConfig(_tempConfig);

        _state.OptionsPanelOpen = true;
        Visible = true;
        SetTab(0);
    }

    public void Close()
    {
        if (_state != null)
            _state.OptionsPanelOpen = false;
        _tempConfig = null;
        Visible = false;
    }

    // ── Accept / Cancel ───────────────────────────────────

    private void OnAccept()
    {
        if (_config == null || _tempConfig == null) return;

        // Read UI controls into temp config
        SaveControlsToConfig(_tempConfig);

        // Apply temp → permanent
        _config.CopyFrom(_tempConfig);
        _config.Save(_dataPath);

        // Notify Main.cs to apply changes to renderers/sound
        OnConfigApplied?.Invoke();

        Close();

        _state?.ChatMessages.Enqueue(new ChatMessage
        {
            Text = "Opciones guardadas correctamente.",
            Color = "00FF00"
        });
    }

    private void OnCancel()
    {
        // Discard temp changes
        Close();
    }

    // ── Performance preset ────────────────────────────────

    private void OnPerformanceChanged(double value)
    {
        int level = (int)value;
        string[] labels = { "Mínimo", "Bajo", "Medio", "Alto", "Máximo" };
        if (_lblPerformance != null)
            _lblPerformance.Text = labels[System.Math.Clamp(level, 0, 4)];

        // Auto-configure render checkboxes when preset changes
        if (_tempConfig == null) return;
        _tempConfig.ApplyPerformancePreset(level);
        LoadRenderChecks(_tempConfig);
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

        // FPS dropdown
        if (_optFpsLimit != null)
        {
            int sel = cfg.FpsLimit switch
            {
                18 => 0,
                32 => 1,
                65 => 2,
                _ => 3 // unlimited
            };
            _optFpsLimit.Selected = sel;
        }

        // Controls tab
        SetCheck(_chkMouseDClick, cfg.MouseDoubleClick);
        SetCheck(_chkMouseRClick, cfg.MouseRightClick);
        SetCheck(_chkMouseContext, cfg.MouseContextMenu);

        // Render tab
        if (_sldPerformance != null) _sldPerformance.Value = cfg.PerformanceLevel;
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

        cfg.FpsLimit = (_optFpsLimit?.Selected ?? 2) switch
        {
            0 => 18,
            1 => 32,
            2 => 65,
            _ => 0
        };

        // Controls tab
        cfg.MouseDoubleClick = IsChecked(_chkMouseDClick);
        cfg.MouseRightClick = IsChecked(_chkMouseRClick);
        cfg.MouseContextMenu = IsChecked(_chkMouseContext);

        // Render tab
        cfg.PerformanceLevel = (int)(_sldPerformance?.Value ?? 2);
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

    // ── UI helpers ────────────────────────────────────────

    private static Button MakeTabButton(string text)
    {
        var btn = new Button();
        btn.Text = text;
        btn.CustomMinimumSize = new Vector2(100, 28);
        btn.AddThemeFontSizeOverride("font_size", 12);
        return btn;
    }

    private static CheckBox MakeCheck(string text)
    {
        var cb = new CheckBox();
        cb.Text = text;
        cb.AddThemeFontSizeOverride("font_size", 11);
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
