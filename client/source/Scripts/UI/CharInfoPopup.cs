using Godot;
using ArgentumNextgen.Game;

namespace ArgentumNextgen.UI;

/// <summary>
/// Character info popup — shows character details from FullCharInfo (ID 245).
/// Triggered by /MIRAR command or "Ver Info" context menu option.
/// Auto-closes after 10 seconds. Appears centered on screen.
/// </summary>
public partial class CharInfoPopup : Control
{
    private const int PanelW = 280;
    private const int PanelH = 260;
    private const float AutoHideSeconds = 10.0f;

    private GameState? _state;

    private Label? _nameLabel;
    private Label? _levelClassLabel;
    private Label? _raceLabel;
    private Label? _factionLabel;
    private Label? _guildLabel;
    private Label? _statusLabel;
    private Label? _statsLabel;
    private Label? _killsLabel;
    private float _timer;

    public void Init(GameState state)
    {
        _state = state;
    }

    public override void _Ready()
    {
        Visible = false;
        CustomMinimumSize = new Vector2(PanelW, PanelH);
        Size = new Vector2(PanelW, PanelH);
        MouseFilter = MouseFilterEnum.Stop;
        ZIndex = RpgBaseForm.ZDialog;

        // Background: solid dark + NinePatch frame
        var solidBg = new ColorRect();
        solidBg.Color = new Color(0.06f, 0.06f, 0.10f, 0.95f);
        solidBg.MouseFilter = MouseFilterEnum.Ignore;
        AddChild(solidBg);
        RpgTheme.FillParent(solidBg);

        var frameBg = RpgTheme.CreateNinePatch("info_window.png", new Vector4(16, 16, 16, 16));
        AddChild(frameBg);
        RpgTheme.FillParent(frameBg);

        // Content margin
        var margin = new MarginContainer();
        margin.MouseFilter = MouseFilterEnum.Ignore;
        margin.AddThemeConstantOverride("margin_top", RpgTheme.PanelMarginTop);
        margin.AddThemeConstantOverride("margin_left", RpgTheme.PanelMarginLeft);
        margin.AddThemeConstantOverride("margin_right", RpgTheme.PanelMarginRight);
        margin.AddThemeConstantOverride("margin_bottom", RpgTheme.PanelMarginBottom);
        AddChild(margin);
        RpgTheme.FillParent(margin);

        var col = RpgTheme.CreateColumn(RpgTheme.SpacingSm);
        margin.AddChild(col);

        // Header row: character name + close button
        var headerRow = RpgTheme.CreateRow(RpgTheme.SpacingSm);
        col.AddChild(headerRow);

        _nameLabel = RpgTheme.CreateTitleLabel("", 15);
        _nameLabel.HorizontalAlignment = HorizontalAlignment.Left;
        _nameLabel.ClipText = true;
        _nameLabel.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        headerRow.AddChild(_nameLabel);

        var closeBtn = RpgTheme.CreateMiniButton("Mini_exit.png", "Mini_exit_t.png", new Vector2(22, 22));
        closeBtn.Pressed += () => { Visible = false; };
        headerRow.AddChild(closeBtn);

        // Separator
        col.AddChild(RpgTheme.CreateSeparator());

        // Info labels
        _levelClassLabel = RpgTheme.CreateInfoLabel("", 11);
        _levelClassLabel.ClipText = true;
        col.AddChild(_levelClassLabel);

        _raceLabel = RpgTheme.CreateInfoLabel("", 11);
        _raceLabel.ClipText = true;
        col.AddChild(_raceLabel);

        _factionLabel = RpgTheme.CreateInfoLabel("", 11);
        _factionLabel.ClipText = true;
        col.AddChild(_factionLabel);

        _guildLabel = RpgTheme.CreateInfoLabel("", 11);
        _guildLabel.ClipText = true;
        col.AddChild(_guildLabel);

        _statusLabel = RpgTheme.CreateInfoLabel("", 11);
        _statusLabel.ClipText = true;
        col.AddChild(_statusLabel);

        _statsLabel = RpgTheme.CreateInfoLabel("", 11);
        _statsLabel.ClipText = true;
        col.AddChild(_statsLabel);

        _killsLabel = RpgTheme.CreateInfoLabel("", 11);
        _killsLabel.ClipText = true;
        col.AddChild(_killsLabel);

        // "Cerrar" button at bottom, centered
        var footerRow = RpgTheme.CreateRow();
        footerRow.Alignment = BoxContainer.AlignmentMode.Center;
        col.AddChild(footerRow);

        var cerrarBtn = RpgTheme.CreateRpgButton("Cerrar", false, 12);
        cerrarBtn.CustomMinimumSize = new Vector2(80, 26);
        cerrarBtn.Pressed += () => { Visible = false; };
        footerRow.AddChild(cerrarBtn);
    }

    /// <summary>
    /// Show the character info popup with the given data.
    /// Resets the auto-hide timer and centers on screen.
    /// </summary>
    public void ShowInfo(CharInfoData info)
    {
        if (_nameLabel == null || info == null) return;

        _nameLabel.Text = info.Name;
        _levelClassLabel!.Text = $"Nivel {info.Level} - {info.ClassName}";
        _raceLabel!.Text = $"Raza: {info.Race}";

        // Faction
        if (info.Faction == "Ninguna")
            _factionLabel!.Text = "Faccion: Ninguna";
        else
            _factionLabel!.Text = $"Faccion: {info.Faction}";

        // Guild
        if (info.GuildIndex > 0)
            _guildLabel!.Text = $"Clan: #{info.GuildIndex}";
        else
            _guildLabel!.Text = "Clan: Ninguno";

        // Status with color
        _statusLabel!.Text = $"Estado: {info.Status}";
        if (info.Status == "Criminal")
            _statusLabel.AddThemeColorOverride("font_color", new Color(1.0f, 0.3f, 0.3f));
        else
            _statusLabel.AddThemeColorOverride("font_color", new Color(0.3f, 0.7f, 1.0f));

        // Stats
        _statsLabel!.Text = $"HP: {info.MaxHp}  Mana: {info.MaxMana}  Sta: {info.MaxSta}";

        // Kills
        _killsLabel!.Text = $"Criminales: {info.CrimMatados}  Ciudadanos: {info.CiudMatados}";

        _timer = AutoHideSeconds;

        // Center on screen
        var viewport = GetViewportRect().Size;
        Position = new Vector2(
            (viewport.X - PanelW) / 2,
            (viewport.Y - PanelH) / 2
        );

        Visible = true;
    }

    public override void _Process(double delta)
    {
        if (!Visible) return;

        _timer -= (float)delta;
        if (_timer <= 0)
        {
            Visible = false;
        }
    }

    public override void _GuiInput(InputEvent @event)
    {
        // Consume mouse events to prevent click-through, reset timer on interaction
        if (@event is InputEventMouseButton mb && mb.Pressed)
        {
            _timer = AutoHideSeconds;
            GetViewport().SetInputAsHandled();
        }
    }
}
