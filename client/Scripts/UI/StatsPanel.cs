using Godot;
using System;
using ArgentumNextgen.Game;
using ArgentumNextgen.Network;

namespace ArgentumNextgen.UI;

/// <summary>
/// VB6 frmEstadisticas — Stats panel with 4 tabs: Info, Atributos, Skills, Fama.
/// Shows character details, attributes, skill levels with progress bars,
/// fame/reputation, and allows distributing free skill points.
/// Toggle with F5 or sidebar "Estadisticas" button.
/// </summary>
public partial class StatsPanel : PanelContainer
{
    private const int PanelW = 380;
    private const int PanelH = 480;
    private const int TitleBarH = 28;
    private const int TabBarH = 28;

    private GameState? _state;
    private AoTcpClient? _tcp;

    // Dragging state
    private bool _dragging;
    private Vector2 _dragOffset;

    // Tab system
    private Control? _infoTab;
    private Control? _attribTab;
    private Control? _skillsTab;
    private Control? _fameTab;
    private Button? _infoTabBtn;
    private Button? _attribTabBtn;
    private Button? _skillsTabBtn;
    private Button? _fameTabBtn;
    private int _activeTab;

    // ── Info tab controls ──
    private Label? _lblCharName;
    private Label? _lblClass;
    private Label? _lblRace;
    private Label? _lblLevel;
    private Label? _lblExp;
    private Label? _lblGold;
    private Label? _lblHp;
    private Label? _lblMana;
    private Label? _lblSta;
    private Label? _lblHunger;
    private Label? _lblThirst;
    private Label? _lblFaction;
    private Label? _lblFreeSkillPts;

    // ── Attrib tab controls ──
    private Label? _lblStr;
    private Label? _lblAgi;
    private Label? _lblInt;
    private Label? _lblCon;
    private Label? _lblCha;

    // ── Skills tab controls ──
    private Label[] _lblSkillValues = new Label[22];
    private ProgressBar[] _skillBars = new ProgressBar[22];
    private Button[] _btnSkillUp = new Button[22];
    private Label? _lblSkillPoints;
    private Button? _btnApplySkills;
    private int[] _pendingSkillIncrements = new int[22];
    private ScrollContainer? _skillScrollContainer;

    // ── Fame tab controls ──
    private Label? _lblFameAsesino;
    private Label? _lblFameBandido;
    private Label? _lblFameBurgues;
    private Label? _lblFameLadron;
    private Label? _lblFameNoble;
    private Label? _lblFamePlebe;
    private Label? _lblReputation;
    private Label? _lblStatus;
    private Label? _lblCrimMatados;
    private Label? _lblCiudMatados;

    // VB6 skill names (indices 1-22, server array is 0-based but skill_id starts at 1)
    private static readonly string[] SkillNames = new string[]
    {
        "Suerte",          // 1  - SUERTE (not shown but exists)
        "Magia",           // 2  - MAGIA
        "Robar",           // 3  - ROBAR
        "Tacticas",        // 4  - TACTICAS
        "Armas",           // 5  - ARMAS
        "Meditar",         // 6  - MEDITAR
        "Apunalar",        // 7  - APUNALAR
        "Ocultarse",       // 8  - OCULTARSE
        "Supervivencia",   // 9  - SUPERVIVENCIA
        "Talar",           // 10 - TALAR
        "Comerciar",       // 11 - COMERCIAR
        "Defensa",         // 12 - DEFENSA
        "Pesca",           // 13 - PESCA
        "Mineria",         // 14 - MINERIA
        "Carpinteria",     // 15 - CARPINTERIA
        "Herreria",        // 16 - HERRERIA
        "Liderazgo",       // 17 - LIDERAZGO
        "Domar",           // 18 - DOMAR
        "Proyectiles",     // 19 - PROYECTILES
        "Wrestling",       // 20 - WRESTLING
        "Navegacion",      // 21 - NAVEGACION
        "Def. Magica",     // 22 - DEFENSA_MAGICA
    };

    public void Init(GameState state, AoTcpClient? tcp = null)
    {
        _state = state;
        _tcp = tcp;
    }

    public override void _Ready()
    {
        CustomMinimumSize = new Vector2(PanelW, PanelH);
        Size = new Vector2(PanelW, PanelH);

        // Dark semi-transparent background
        var style = new StyleBoxFlat();
        style.BgColor = new Color(0.08f, 0.08f, 0.12f, 0.92f);
        style.BorderColor = new Color(0.4f, 0.35f, 0.25f, 1f);
        style.SetBorderWidthAll(2);
        style.SetCornerRadiusAll(3);
        AddThemeStyleboxOverride("panel", style);

        var root = new VBoxContainer();
        root.SetAnchorsPreset(LayoutPreset.FullRect);
        root.AddThemeConstantOverride("separation", 0);
        AddChild(root);

        // ── Title bar ──
        var titleBar = new HBoxContainer();
        titleBar.CustomMinimumSize = new Vector2(0, TitleBarH);
        var titleBg = new StyleBoxFlat();
        titleBg.BgColor = new Color(0.15f, 0.12f, 0.08f, 1f);
        titleBg.SetCornerRadiusAll(3);
        titleBar.AddThemeStyleboxOverride("panel", titleBg);
        root.AddChild(titleBar);

        var titleLabel = new Label();
        titleLabel.Text = "  Estadisticas";
        titleLabel.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        titleLabel.AddThemeColorOverride("font_color", new Color(0.9f, 0.8f, 0.5f));
        titleLabel.AddThemeFontSizeOverride("font_size", 13);
        titleBar.AddChild(titleLabel);

        var closeBtn = new Button();
        closeBtn.Text = "X";
        closeBtn.CustomMinimumSize = new Vector2(28, 24);
        closeBtn.Pressed += Close;
        titleBar.AddChild(closeBtn);

        // ── Tab bar ──
        var tabBar = new HBoxContainer();
        tabBar.CustomMinimumSize = new Vector2(0, TabBarH);
        tabBar.AddThemeConstantOverride("separation", 2);
        root.AddChild(tabBar);

        _infoTabBtn = CreateTabButton("Info", 0);
        _attribTabBtn = CreateTabButton("Atributos", 1);
        _skillsTabBtn = CreateTabButton("Skills", 2);
        _fameTabBtn = CreateTabButton("Fama", 3);
        tabBar.AddChild(_infoTabBtn);
        tabBar.AddChild(_attribTabBtn);
        tabBar.AddChild(_skillsTabBtn);
        tabBar.AddChild(_fameTabBtn);

        // ── Tab content area ──
        var contentArea = new Control();
        contentArea.SizeFlagsVertical = SizeFlags.ExpandFill;
        contentArea.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        contentArea.CustomMinimumSize = new Vector2(PanelW - 8, PanelH - TitleBarH - TabBarH - 8);
        root.AddChild(contentArea);

        _infoTab = BuildInfoTab();
        _attribTab = BuildAttribTab();
        _skillsTab = BuildSkillsTab();
        _fameTab = BuildFameTab();

        contentArea.AddChild(_infoTab);
        contentArea.AddChild(_attribTab);
        contentArea.AddChild(_skillsTab);
        contentArea.AddChild(_fameTab);

        SetTab(0);
    }

    // ── Tab building ──────────────────────────────────────────

    private Button CreateTabButton(string label, int tabIndex)
    {
        var btn = new Button();
        btn.Text = label;
        btn.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        btn.CustomMinimumSize = new Vector2(0, TabBarH);
        btn.AddThemeFontSizeOverride("font_size", 11);
        btn.Pressed += () => SetTab(tabIndex);
        return btn;
    }

    private void SetTab(int index)
    {
        _activeTab = index;
        if (_infoTab != null) _infoTab.Visible = index == 0;
        if (_attribTab != null) _attribTab.Visible = index == 1;
        if (_skillsTab != null) _skillsTab.Visible = index == 2;
        if (_fameTab != null) _fameTab.Visible = index == 3;

        // Highlight active tab button
        var normalColor = new Color(0.7f, 0.7f, 0.7f);
        var activeColor = new Color(0.9f, 0.8f, 0.5f);
        _infoTabBtn?.AddThemeColorOverride("font_color", index == 0 ? activeColor : normalColor);
        _attribTabBtn?.AddThemeColorOverride("font_color", index == 1 ? activeColor : normalColor);
        _skillsTabBtn?.AddThemeColorOverride("font_color", index == 2 ? activeColor : normalColor);
        _fameTabBtn?.AddThemeColorOverride("font_color", index == 3 ? activeColor : normalColor);

        RefreshCurrentTab();
    }

    // ── Info tab ──────────────────────────────────────────────

    private Control BuildInfoTab()
    {
        var scroll = new ScrollContainer();
        scroll.SetAnchorsPreset(LayoutPreset.FullRect);
        scroll.HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled;

        var vbox = new VBoxContainer();
        vbox.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        vbox.AddThemeConstantOverride("separation", 4);
        scroll.AddChild(vbox);

        AddSectionHeader(vbox, "Personaje");
        _lblCharName = AddInfoRow(vbox, "Nombre:");
        _lblClass = AddInfoRow(vbox, "Clase:");
        _lblRace = AddInfoRow(vbox, "Raza:");
        _lblLevel = AddInfoRow(vbox, "Nivel:");

        AddSectionHeader(vbox, "Experiencia");
        _lblExp = AddInfoRow(vbox, "EXP:");
        _lblGold = AddInfoRow(vbox, "Oro:");

        AddSectionHeader(vbox, "Vitalidad");
        _lblHp = AddInfoRow(vbox, "Vida:");
        _lblMana = AddInfoRow(vbox, "Mana:");
        _lblSta = AddInfoRow(vbox, "Energia:");
        _lblHunger = AddInfoRow(vbox, "Hambre:");
        _lblThirst = AddInfoRow(vbox, "Sed:");

        AddSectionHeader(vbox, "Faccion");
        _lblFaction = AddInfoRow(vbox, "Estado:");

        AddSeparator(vbox);
        _lblFreeSkillPts = AddInfoRow(vbox, "Puntos libres:");
        _lblFreeSkillPts!.AddThemeColorOverride("font_color", new Color(0.3f, 1f, 0.3f));

        return scroll;
    }

    // ── Attributes tab ────────────────────────────────────────

    private Control BuildAttribTab()
    {
        var scroll = new ScrollContainer();
        scroll.SetAnchorsPreset(LayoutPreset.FullRect);
        scroll.HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled;

        var vbox = new VBoxContainer();
        vbox.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        vbox.AddThemeConstantOverride("separation", 8);
        scroll.AddChild(vbox);

        // Spacer
        var spacer = new Control();
        spacer.CustomMinimumSize = new Vector2(0, 8);
        vbox.AddChild(spacer);

        AddSectionHeader(vbox, "Atributos");
        _lblStr = AddAttribRow(vbox, "Fuerza");
        _lblAgi = AddAttribRow(vbox, "Agilidad");
        _lblInt = AddAttribRow(vbox, "Inteligencia");
        _lblCon = AddAttribRow(vbox, "Constitucion");
        _lblCha = AddAttribRow(vbox, "Carisma");

        return scroll;
    }

    private Label AddAttribRow(VBoxContainer parent, string name)
    {
        var hbox = new HBoxContainer();
        hbox.AddThemeConstantOverride("separation", 8);
        parent.AddChild(hbox);

        var nameLabel = new Label();
        nameLabel.Text = name;
        nameLabel.CustomMinimumSize = new Vector2(130, 0);
        nameLabel.AddThemeColorOverride("font_color", new Color(0.8f, 0.75f, 0.6f));
        nameLabel.AddThemeFontSizeOverride("font_size", 12);
        hbox.AddChild(nameLabel);

        var bar = new ProgressBar();
        bar.MinValue = 0;
        bar.MaxValue = 50; // VB6 max attrib ~50
        bar.CustomMinimumSize = new Vector2(120, 18);
        bar.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        bar.ShowPercentage = false;
        var barStyle = new StyleBoxFlat();
        barStyle.BgColor = new Color(0.15f, 0.15f, 0.2f);
        bar.AddThemeStyleboxOverride("background", barStyle);
        var fillStyle = new StyleBoxFlat();
        fillStyle.BgColor = new Color(0.6f, 0.5f, 0.2f);
        bar.AddThemeStyleboxOverride("fill", fillStyle);
        hbox.AddChild(bar);

        var valLabel = new Label();
        valLabel.Text = "0";
        valLabel.CustomMinimumSize = new Vector2(30, 0);
        valLabel.HorizontalAlignment = HorizontalAlignment.Right;
        valLabel.AddThemeColorOverride("font_color", Colors.White);
        valLabel.AddThemeFontSizeOverride("font_size", 12);
        hbox.AddChild(valLabel);

        // Store bar reference on the label for updates
        valLabel.SetMeta("bar", bar.GetPath());

        return valLabel;
    }

    // ── Skills tab ────────────────────────────────────────────

    private Control BuildSkillsTab()
    {
        var outer = new VBoxContainer();
        outer.SetAnchorsPreset(LayoutPreset.FullRect);
        outer.AddThemeConstantOverride("separation", 4);

        // Header with skill points
        var header = new HBoxContainer();
        header.AddThemeConstantOverride("separation", 8);
        outer.AddChild(header);

        var ptsLabel = new Label();
        ptsLabel.Text = "Puntos disponibles:";
        ptsLabel.AddThemeColorOverride("font_color", new Color(0.8f, 0.75f, 0.6f));
        ptsLabel.AddThemeFontSizeOverride("font_size", 11);
        header.AddChild(ptsLabel);

        _lblSkillPoints = new Label();
        _lblSkillPoints.Text = "0";
        _lblSkillPoints.AddThemeColorOverride("font_color", new Color(0.3f, 1f, 0.3f));
        _lblSkillPoints.AddThemeFontSizeOverride("font_size", 11);
        _lblSkillPoints.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        header.AddChild(_lblSkillPoints);

        _btnApplySkills = new Button();
        _btnApplySkills.Text = "Aplicar";
        _btnApplySkills.CustomMinimumSize = new Vector2(60, 22);
        _btnApplySkills.AddThemeFontSizeOverride("font_size", 10);
        _btnApplySkills.Pressed += OnApplySkillsPressed;
        header.AddChild(_btnApplySkills);

        // Scrollable skill list
        _skillScrollContainer = new ScrollContainer();
        _skillScrollContainer.SizeFlagsVertical = SizeFlags.ExpandFill;
        _skillScrollContainer.HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled;
        outer.AddChild(_skillScrollContainer);

        var vbox = new VBoxContainer();
        vbox.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        vbox.AddThemeConstantOverride("separation", 2);
        _skillScrollContainer.AddChild(vbox);

        for (int i = 0; i < 22; i++)
        {
            var row = new HBoxContainer();
            row.AddThemeConstantOverride("separation", 4);
            vbox.AddChild(row);

            // Skill name
            var nameLabel = new Label();
            nameLabel.Text = SkillNames[i];
            nameLabel.CustomMinimumSize = new Vector2(100, 0);
            nameLabel.AddThemeColorOverride("font_color", new Color(0.8f, 0.75f, 0.6f));
            nameLabel.AddThemeFontSizeOverride("font_size", 10);
            row.AddChild(nameLabel);

            // Progress bar
            var bar = new ProgressBar();
            bar.MinValue = 0;
            bar.MaxValue = 100;
            bar.CustomMinimumSize = new Vector2(100, 16);
            bar.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            bar.ShowPercentage = false;
            var barBg = new StyleBoxFlat();
            barBg.BgColor = new Color(0.12f, 0.12f, 0.18f);
            bar.AddThemeStyleboxOverride("background", barBg);
            var barFill = new StyleBoxFlat();
            barFill.BgColor = new Color(0.2f, 0.5f, 0.8f);
            bar.AddThemeStyleboxOverride("fill", barFill);
            row.AddChild(bar);
            _skillBars[i] = bar;

            // Value label
            var valLabel = new Label();
            valLabel.Text = "0";
            valLabel.CustomMinimumSize = new Vector2(28, 0);
            valLabel.HorizontalAlignment = HorizontalAlignment.Right;
            valLabel.AddThemeColorOverride("font_color", Colors.White);
            valLabel.AddThemeFontSizeOverride("font_size", 10);
            row.AddChild(valLabel);
            _lblSkillValues[i] = valLabel;

            // + button (only visible when skill points available)
            int skillIdx = i; // capture for closure
            var upBtn = new Button();
            upBtn.Text = "+";
            upBtn.CustomMinimumSize = new Vector2(22, 18);
            upBtn.AddThemeFontSizeOverride("font_size", 10);
            upBtn.Pressed += () => OnSkillUpPressed(skillIdx);
            row.AddChild(upBtn);
            _btnSkillUp[i] = upBtn;
        }

        return outer;
    }

    // ── Fame tab ──────────────────────────────────────────────

    private Control BuildFameTab()
    {
        var scroll = new ScrollContainer();
        scroll.SetAnchorsPreset(LayoutPreset.FullRect);
        scroll.HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled;

        var vbox = new VBoxContainer();
        vbox.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        vbox.AddThemeConstantOverride("separation", 8);
        scroll.AddChild(vbox);

        var spacer = new Control();
        spacer.CustomMinimumSize = new Vector2(0, 8);
        vbox.AddChild(spacer);

        AddSectionHeader(vbox, "Reputacion");
        _lblFameNoble = AddInfoRow(vbox, "Noble:");
        _lblFamePlebe = AddInfoRow(vbox, "Plebeyo:");
        _lblFameBurgues = AddInfoRow(vbox, "Burgues:");
        _lblFameLadron = AddInfoRow(vbox, "Ladron:");
        _lblFameBandido = AddInfoRow(vbox, "Bandido:");
        _lblFameAsesino = AddInfoRow(vbox, "Asesino:");
        _lblReputation = AddInfoRow(vbox, "Promedio:");
        _lblStatus = AddInfoRow(vbox, "Estado:");

        AddSectionHeader(vbox, "Muertes");
        _lblCrimMatados = AddInfoRow(vbox, "Criminales matados:");
        _lblCiudMatados = AddInfoRow(vbox, "Ciudadanos matados:");

        return scroll;
    }

    // ── UI helpers ────────────────────────────────────────────

    private void AddSectionHeader(VBoxContainer parent, string text)
    {
        var sep = new HSeparator();
        sep.AddThemeConstantOverride("separation", 4);
        parent.AddChild(sep);

        var label = new Label();
        label.Text = text;
        label.AddThemeColorOverride("font_color", new Color(0.9f, 0.8f, 0.5f));
        label.AddThemeFontSizeOverride("font_size", 12);
        parent.AddChild(label);
    }

    private Label AddInfoRow(VBoxContainer parent, string label)
    {
        var hbox = new HBoxContainer();
        hbox.AddThemeConstantOverride("separation", 8);
        parent.AddChild(hbox);

        var nameLabel = new Label();
        nameLabel.Text = label;
        nameLabel.CustomMinimumSize = new Vector2(120, 0);
        nameLabel.AddThemeColorOverride("font_color", new Color(0.6f, 0.6f, 0.6f));
        nameLabel.AddThemeFontSizeOverride("font_size", 11);
        hbox.AddChild(nameLabel);

        var valLabel = new Label();
        valLabel.Text = "-";
        valLabel.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        valLabel.AddThemeColorOverride("font_color", Colors.White);
        valLabel.AddThemeFontSizeOverride("font_size", 11);
        hbox.AddChild(valLabel);

        return valLabel;
    }

    private void AddSeparator(VBoxContainer parent)
    {
        var sep = new HSeparator();
        sep.AddThemeConstantOverride("separation", 8);
        parent.AddChild(sep);
    }

    // ── Open / Close ──────────────────────────────────────────

    public void Open()
    {
        if (_state == null) return;
        _state.StatsPanelOpen = true;
        Visible = true;
        ResetPendingSkills();
        RefreshCurrentTab();
    }

    public void Close()
    {
        if (_state != null)
            _state.StatsPanelOpen = false;
        Visible = false;
    }

    // ── Refresh ───────────────────────────────────────────────

    private void RefreshCurrentTab()
    {
        if (_state == null) return;

        switch (_activeTab)
        {
            case 0: RefreshInfoTab(); break;
            case 1: RefreshAttribTab(); break;
            case 2: RefreshSkillsTab(); break;
            case 3: RefreshFameTab(); break;
        }
    }

    private void RefreshInfoTab()
    {
        if (_state == null) return;
        _lblCharName?.SetDeferred("text", _state.UserName);
        _lblClass?.SetDeferred("text", _state.UserClassName);
        _lblRace?.SetDeferred("text", _state.UserRaceName);
        _lblLevel?.SetDeferred("text", _state.Level.ToString());
        _lblExp?.SetDeferred("text", $"{_state.Exp} / {_state.ExpNext}");
        _lblGold?.SetDeferred("text", _state.Gold.ToString("N0"));
        _lblHp?.SetDeferred("text", $"{_state.MinHp} / {_state.MaxHp}");
        _lblMana?.SetDeferred("text", $"{_state.MinMana} / {_state.MaxMana}");
        _lblSta?.SetDeferred("text", $"{_state.MinSta} / {_state.MaxSta}");
        _lblHunger?.SetDeferred("text", $"{_state.MinHam} / {_state.MaxHam}");
        _lblThirst?.SetDeferred("text", $"{_state.MinAgua} / {_state.MaxAgua}");

        string faction = "Ciudadano";
        if (_state.UserFactionReal) faction = "Armada Real";
        else if (_state.UserFactionCaos) faction = "Fuerzas del Caos";
        else if (_state.UserCriminal) faction = "Criminal";
        _lblFaction?.SetDeferred("text", faction);

        _lblFreeSkillPts?.SetDeferred("text", _state.FreeSkillPoints.ToString());
    }

    private void RefreshAttribTab()
    {
        if (_state == null) return;

        UpdateAttribLabel(_lblStr, _state.Strength);
        UpdateAttribLabel(_lblAgi, _state.Agility);
        UpdateAttribLabel(_lblInt, _state.Intelligence);
        UpdateAttribLabel(_lblCon, _state.Constitution);
        UpdateAttribLabel(_lblCha, _state.Charisma);
    }

    private void UpdateAttribLabel(Label? label, int value)
    {
        if (label == null) return;
        label.Text = value.ToString();
        // Update the associated progress bar
        var barPath = label.GetMeta("bar");
        if (barPath.VariantType != Variant.Type.Nil)
        {
            var bar = label.GetNode<ProgressBar>((NodePath)barPath.AsString());
            if (bar != null) bar.Value = value;
        }
    }

    private void RefreshSkillsTab()
    {
        if (_state == null) return;

        int remaining = _state.FreeSkillPoints - TotalPendingIncrements();
        _lblSkillPoints?.SetDeferred("text", remaining.ToString());

        bool hasPoints = remaining > 0;
        for (int i = 0; i < 22; i++)
        {
            int skillVal = (i < _state.Skills.Length) ? _state.Skills[i] : 0;
            int pending = _pendingSkillIncrements[i];
            int displayVal = skillVal + pending;

            if (_lblSkillValues[i] != null)
            {
                string text = pending > 0 ? $"{skillVal}+{pending}" : skillVal.ToString();
                _lblSkillValues[i].Text = text;
                _lblSkillValues[i].AddThemeColorOverride("font_color",
                    pending > 0 ? new Color(0.3f, 1f, 0.3f) : Colors.White);
            }

            if (_skillBars[i] != null)
                _skillBars[i].Value = displayVal;

            if (_btnSkillUp[i] != null)
                _btnSkillUp[i].Visible = hasPoints || pending > 0;
        }

        if (_btnApplySkills != null)
            _btnApplySkills.Visible = TotalPendingIncrements() > 0;
    }

    private void RefreshFameTab()
    {
        if (_state == null) return;

        _lblFameNoble?.SetDeferred("text", _state.FameNoble.ToString());
        _lblFamePlebe?.SetDeferred("text", _state.FamePlebe.ToString());
        _lblFameBurgues?.SetDeferred("text", _state.FameBurgues.ToString());
        _lblFameLadron?.SetDeferred("text", _state.FameLadron.ToString());
        _lblFameBandido?.SetDeferred("text", _state.FameBandido.ToString());
        _lblFameAsesino?.SetDeferred("text", _state.FameAsesino.ToString());
        _lblReputation?.SetDeferred("text", _state.Reputation.ToString());

        string status = _state.UserCriminal ? "Criminal" : "Ciudadano";
        _lblStatus?.SetDeferred("text", status);

        _lblCrimMatados?.SetDeferred("text", _state.FameCrimMatados.ToString());
        _lblCiudMatados?.SetDeferred("text", _state.FameCiudMatados.ToString());
    }

    // ── Skill point distribution ──────────────────────────────

    private void OnSkillUpPressed(int skillIndex)
    {
        if (_state == null) return;
        int remaining = _state.FreeSkillPoints - TotalPendingIncrements();
        if (remaining <= 0) return;

        int currentSkill = (skillIndex < _state.Skills.Length) ? _state.Skills[skillIndex] : 0;
        if (currentSkill + _pendingSkillIncrements[skillIndex] >= 100) return;

        _pendingSkillIncrements[skillIndex]++;
        RefreshSkillsTab();
    }

    private void OnApplySkillsPressed()
    {
        if (_state == null || _tcp == null) return;
        int total = TotalPendingIncrements();
        if (total <= 0) return;

        // Build the 20-byte array for WriteSkillSet (server expects 20 values for skills 1-20)
        // But server handle_skse reads 22 values — send all 22 as the protocol supports it
        var points = new byte[20];
        for (int i = 0; i < 20 && i < 22; i++)
        {
            points[i] = (byte)_pendingSkillIncrements[i];
        }

        _tcp.SendPacket(ClientPackets.WriteSkillSet(points));
        ResetPendingSkills();

        GD.Print($"[StatsPanel] Applied {total} skill points");
    }

    private void ResetPendingSkills()
    {
        for (int i = 0; i < 22; i++)
            _pendingSkillIncrements[i] = 0;
    }

    private int TotalPendingIncrements()
    {
        int total = 0;
        for (int i = 0; i < 22; i++)
            total += _pendingSkillIncrements[i];
        return total;
    }

    // ── Dragging ──────────────────────────────────────────────

    public override void _GuiInput(InputEvent @event)
    {
        if (@event is InputEventMouseButton mb)
        {
            if (mb.ButtonIndex == MouseButton.Left)
            {
                if (mb.Pressed && mb.Position.Y < TitleBarH)
                {
                    _dragging = true;
                    _dragOffset = mb.Position;
                    AcceptEvent();
                }
                else if (!mb.Pressed)
                {
                    _dragging = false;
                }
            }
        }
        else if (@event is InputEventMouseMotion mm && _dragging)
        {
            Position += mm.Relative;
            AcceptEvent();
        }
    }

    // ── Per-frame update ──────────────────────────────────────

    public override void _Process(double delta)
    {
        if (!Visible || _state == null) return;
        RefreshCurrentTab();
    }
}
