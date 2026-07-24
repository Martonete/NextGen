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
public partial class StatsPanel : RpgBaseForm
{
    private GameState? _state;
    private AoTcpClient? _tcp;

    private bool _dirty = true;

    // Tab system
    private HBoxContainer? _tabBar;
    private VBoxContainer? _infoTab;
    private VBoxContainer? _attribTab;
    private VBoxContainer? _skillsTab;
    private VBoxContainer? _fameTab;
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
    private ProgressBar? _barStr;
    private ProgressBar? _barAgi;
    private ProgressBar? _barInt;
    private ProgressBar? _barCon;
    private ProgressBar? _barCha;

    // ── Skills tab controls ──
    private Label[] _lblSkillValues = new Label[22];
    private ProgressBar[] _skillBars = new ProgressBar[22];
    // XP progress bars: show PorcentajeSkills (0-99) — how close to next level
    private ProgressBar[] _xpBars = new ProgressBar[22];
    private Button[] _btnSkillUp = new Button[22];
    private Label? _lblSkillPoints;
    private TextureButton? _btnApplySkills;
    private int[] _pendingSkillIncrements = new int[22];

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

    public StatsPanel() : base("Estadísticas", new Vector2(380, 480), "v2") { }

    public void Init(GameState state, AoTcpClient? tcp = null)
    {
        _state = state;
        _tcp = tcp;
    }

    protected override void BuildContent()
    {
        var root = RpgTheme.CreateColumn(RpgTheme.SpacingSm);
        ContentContainer.AddChild(root);

        // ── Tab bar ──
        _tabBar = RpgTheme.CreateTabBar(
            new[] { "Info", "Atributos", "Skills", "Fama" },
            OnTabChanged
        );
        root.AddChild(_tabBar);

        // ── Tab content pages ──
        _infoTab = BuildInfoTab();
        _attribTab = BuildAttribTab();
        _skillsTab = BuildSkillsTab();
        _fameTab = BuildFameTab();

        root.AddChild(_infoTab);
        root.AddChild(_attribTab);
        root.AddChild(_skillsTab);
        root.AddChild(_fameTab);

        // Initial tab selection (defer so Ready has completed for tab bar styling)
        CallDeferred(MethodName.DeferredSetTab);
    }

    private void DeferredSetTab()
    {
        SetTab(0);
    }

    // ── Tab switching ──────────────────────────────────────────

    private void OnTabChanged(int index)
    {
        SetTab(index);
    }

    private void SetTab(int index)
    {
        _dirty = true;
        _activeTab = index;
        if (_infoTab != null) _infoTab.Visible = index == 0;
        if (_attribTab != null) _attribTab.Visible = index == 1;
        if (_skillsTab != null) _skillsTab.Visible = index == 2;
        if (_fameTab != null) _fameTab.Visible = index == 3;

        if (_tabBar != null)
            RpgTheme.SetTabBarActive(_tabBar, index);

        RefreshCurrentTab();
    }

    // ── Info tab ──────────────────────────────────────────────

    private VBoxContainer BuildInfoTab()
    {
        var page = RpgTheme.CreateColumn(0);
        page.SizeFlagsVertical = SizeFlags.ExpandFill;

        var scrollArea = RpgTheme.CreateScrollArea(RpgTheme.SpacingSm);
        page.AddChild(scrollArea);
        var vbox = scrollArea.GetMeta("content").As<VBoxContainer>();

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

        vbox.AddChild(RpgTheme.CreateSeparator());
        _lblFreeSkillPts = AddInfoRow(vbox, "Puntos libres:");
        _lblFreeSkillPts!.AddThemeColorOverride("font_color", new Color(0.3f, 1f, 0.3f));

        return page;
    }

    // ── Attributes tab ────────────────────────────────────────

    private VBoxContainer BuildAttribTab()
    {
        var page = RpgTheme.CreateColumn(0);
        page.SizeFlagsVertical = SizeFlags.ExpandFill;

        var scrollArea = RpgTheme.CreateScrollArea(RpgTheme.SpacingMd);
        page.AddChild(scrollArea);
        var vbox = scrollArea.GetMeta("content").As<VBoxContainer>();

        vbox.AddChild(RpgTheme.CreateSpacer(8));

        AddSectionHeader(vbox, "Atributos");
        (_lblStr, _barStr) = AddAttribRow(vbox, "Fuerza");
        (_lblAgi, _barAgi) = AddAttribRow(vbox, "Agilidad");
        (_lblInt, _barInt) = AddAttribRow(vbox, "Inteligencia");
        (_lblCon, _barCon) = AddAttribRow(vbox, "Constitucion");
        (_lblCha, _barCha) = AddAttribRow(vbox, "Carisma");

        return page;
    }

    private (Label valLabel, ProgressBar bar) AddAttribRow(VBoxContainer parent, string name)
    {
        var hbox = RpgTheme.CreateRow(RpgTheme.SpacingMd);
        parent.AddChild(hbox);

        var nameLabel = RpgTheme.CreateInfoLabel(name, 12);
        nameLabel.CustomMinimumSize = new Vector2(130, 0);
        hbox.AddChild(nameLabel);

        var bar = RpgTheme.CreateRpgProgressBar(120, 18, new Color(0.6f, 0.5f, 0.2f));
        bar.MaxValue = 50; // VB6 max attrib ~50
        bar.ClipContents = false;
        hbox.AddChild(bar);

        // Value label centered INSIDE the progress bar
        var valLabel = RpgTheme.CreateInfoLabel("0", 12);
        valLabel.HorizontalAlignment = HorizontalAlignment.Center;
        valLabel.VerticalAlignment = VerticalAlignment.Center;
        valLabel.AddThemeColorOverride("font_color", Colors.White);
        valLabel.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        bar.AddChild(valLabel);

        return (valLabel, bar);
    }

    // ── Skills tab ────────────────────────────────────────────

    private VBoxContainer BuildSkillsTab()
    {
        var page = RpgTheme.CreateColumn(RpgTheme.SpacingSm);
        page.SizeFlagsVertical = SizeFlags.ExpandFill;

        // Header with skill points
        var header = RpgTheme.CreateRow(RpgTheme.SpacingMd);
        page.AddChild(header);

        var ptsLabel = RpgTheme.CreateInfoLabel("Puntos disponibles:", 11);
        header.AddChild(ptsLabel);

        _lblSkillPoints = RpgTheme.CreateInfoLabel("0", 11);
        _lblSkillPoints.AddThemeColorOverride("font_color", new Color(0.3f, 1f, 0.3f));
        _lblSkillPoints.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        header.AddChild(_lblSkillPoints);

        _btnApplySkills = RpgTheme.CreateRpgButton("Aplicar", false, 11);
        _btnApplySkills.CustomMinimumSize = new Vector2(70, 24);
        _btnApplySkills.Pressed += OnApplySkillsPressed;
        header.AddChild(_btnApplySkills);

        // Scrollable skill list
        var skillScrollArea = RpgTheme.CreateScrollArea(2);
        page.AddChild(skillScrollArea);
        var vbox = skillScrollArea.GetMeta("content").As<VBoxContainer>();

        for (int i = 0; i < 22; i++)
        {
            var row = RpgTheme.CreateRow(RpgTheme.SpacingSm);
            row.ClipContents = false;
            vbox.AddChild(row);

            // Skill name — expands to fill available space
            var nameLabel = RpgTheme.CreateInfoLabel(SkillNames[i], 10);
            nameLabel.CustomMinimumSize = new Vector2(80, 0);
            nameLabel.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            nameLabel.ClipText = true;
            row.AddChild(nameLabel);

            // Skill level progress bar — fixed width, no expand
            var bar = RpgTheme.CreateRpgProgressBar(70, 14, new Color(0.2f, 0.5f, 0.8f));
            bar.MaxValue = 100;
            bar.SizeFlagsHorizontal = SizeFlags.ShrinkCenter;
            bar.ClipContents = false;
            row.AddChild(bar);
            _skillBars[i] = bar;

            // Value label centered INSIDE the skill level bar
            var valLabel = RpgTheme.CreateInfoLabel("0", 10);
            valLabel.HorizontalAlignment = HorizontalAlignment.Center;
            valLabel.VerticalAlignment = VerticalAlignment.Center;
            valLabel.AddThemeColorOverride("font_color", Colors.White);
            valLabel.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
            bar.AddChild(valLabel);
            _lblSkillValues[i] = valLabel;

            // XP progress bar — shows PorcentajeSkills (0-99, progress toward next level)
            // Orange/yellow tint to distinguish from the blue skill level bar
            var xpBar = RpgTheme.CreateRpgProgressBar(40, 7, new Color(0.8f, 0.55f, 0.1f));
            xpBar.MaxValue = 99;
            xpBar.Value = 0;
            xpBar.SizeFlagsHorizontal = SizeFlags.ShrinkCenter;
            row.AddChild(xpBar);
            _xpBars[i] = xpBar;

            // [+] button
            int skillIdx = i;
            var upBtn = new Button();
            upBtn.Text = "+";
            upBtn.CustomMinimumSize = new Vector2(20, 18);
            upBtn.AddThemeFontSizeOverride("font_size", 10);
            upBtn.FocusMode = FocusModeEnum.None;
            upBtn.Pressed += () => OnSkillUpPressed(skillIdx);
            row.AddChild(upBtn);
            _btnSkillUp[i] = upBtn;
        }

        return page;
    }

    // ── Fame tab ──────────────────────────────────────────────

    private VBoxContainer BuildFameTab()
    {
        var page = RpgTheme.CreateColumn(0);
        page.SizeFlagsVertical = SizeFlags.ExpandFill;

        var scrollArea = RpgTheme.CreateScrollArea(RpgTheme.SpacingMd);
        page.AddChild(scrollArea);
        var vbox = scrollArea.GetMeta("content").As<VBoxContainer>();

        vbox.AddChild(RpgTheme.CreateSpacer(8));

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

        return page;
    }

    // ── UI helpers ────────────────────────────────────────────

    private void AddSectionHeader(VBoxContainer parent, string text)
    {
        parent.AddChild(RpgTheme.CreateSeparator());
        parent.AddChild(RpgTheme.CreateTitleLabel(text, 12));
    }

    private Label AddInfoRow(VBoxContainer parent, string label)
    {
        var hbox = RpgTheme.CreateRow(RpgTheme.SpacingMd);
        parent.AddChild(hbox);

        var nameLabel = RpgTheme.CreateInfoLabel(label, 11);
        nameLabel.CustomMinimumSize = new Vector2(120, 0);
        hbox.AddChild(nameLabel);

        var valLabel = RpgTheme.CreateInfoLabel("-", 11);
        valLabel.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        valLabel.AddThemeColorOverride("font_color", Colors.White);
        hbox.AddChild(valLabel);

        return valLabel;
    }

    // ── Open / Close ──────────────────────────────────────────

    public void Open()
    {
        if (_state == null) return;
        _dirty = true;
        _state.StatsPanelOpen = true;
        ShowForm();
        ResetPendingSkills();
        RefreshCurrentTab();
    }

    public override void HideForm()
    {
        if (_state != null)
            _state.StatsPanelOpen = false;
        base.HideForm();
    }

    public void Close() => HideForm();

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
        _lblLevel?.SetDeferred("text", $"{_state.Level} / {GameState.MaxLevel}");
        bool atMaxLevel = _state.Level >= GameState.MaxLevel || _state.ExpNext <= 0;
        _lblExp?.SetDeferred("text", atMaxLevel ? "Nivel Maximo" : $"{_state.Exp} / {_state.ExpNext}");
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

        UpdateAttribLabel(_lblStr, _barStr, _state.Strength);
        UpdateAttribLabel(_lblAgi, _barAgi, _state.Agility);
        UpdateAttribLabel(_lblInt, _barInt, _state.Intelligence);
        UpdateAttribLabel(_lblCon, _barCon, _state.Constitution);
        UpdateAttribLabel(_lblCha, _barCha, _state.Charisma);
    }

    private void UpdateAttribLabel(Label? label, ProgressBar? bar, int value)
    {
        if (label != null) label.Text = value.ToString();
        if (bar != null) bar.Value = value;
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
                string text = pending > 0
                    ? $"{skillVal}+{pending}/{GameState.MaxSkillLevel}"
                    : $"{skillVal}/{GameState.MaxSkillLevel}";
                _lblSkillValues[i].Text = text;
                _lblSkillValues[i].AddThemeColorOverride("font_color",
                    pending > 0 ? new Color(0.3f, 1f, 0.3f) : Colors.White);
            }

            if (_skillBars[i] != null)
            {
                _skillBars[i].MaxValue = GameState.MaxSkillLevel;
                _skillBars[i].Value = displayVal;
            }

            // XP progress bar — show actual server XP%, but hide when pending changes are applied
            if (_xpBars[i] != null)
            {
                int pct = (i < _state.SkillPct.Length) ? _state.SkillPct[i] : 0;
                _xpBars[i].Value = pending > 0 ? 0 : pct;
                _xpBars[i].Visible = skillVal < GameState.MaxSkillLevel;
            }

            if (_btnSkillUp[i] != null)
                _btnSkillUp[i].Visible = (hasPoints || pending > 0) && displayVal < GameState.MaxSkillLevel;
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
        if (currentSkill + _pendingSkillIncrements[skillIndex] >= GameState.MaxSkillLevel) return;

        _pendingSkillIncrements[skillIndex]++;
        RefreshSkillsTab();
    }

    private void OnApplySkillsPressed()
    {
        if (_state == null || _tcp == null) return;
        int total = TotalPendingIncrements();
        if (total <= 0) return;

        // Server expects one increment byte per VB6 skill slot.
        var points = new byte[22];
        for (int i = 0; i < 22; i++)
        {
            points[i] = (byte)_pendingSkillIncrements[i];
        }

        _tcp.SendPacket(ClientPackets.WriteSkillSet(points));
        ResetPendingSkills();
        RefreshSkillsTab();

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

    // ── Per-frame update ──────────────────────────────────────

    public void MarkDirty() => _dirty = true;

    public override void _Process(double delta)
    {
        if (!Visible || _state == null) return;
        _dirty = false;
        RefreshCurrentTab();
    }
}
