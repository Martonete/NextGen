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
    private Button? _closeBtn;
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

        // Background
        var bg = new ColorRect();
        bg.Color = new Color(0.06f, 0.06f, 0.10f, 0.95f);
        bg.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        AddChild(bg);

        // Border
        var border = new ReferenceRect();
        border.EditorOnly = false;
        border.BorderColor = new Color(0.5f, 0.6f, 0.9f, 0.6f);
        border.BorderWidth = 1.0f;
        border.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        AddChild(border);

        // Close button
        _closeBtn = new Button();
        _closeBtn.Text = "X";
        _closeBtn.Position = new Vector2(PanelW - 28, 4);
        _closeBtn.Size = new Vector2(24, 22);
        _closeBtn.Pressed += () => { Visible = false; };
        AddChild(_closeBtn);

        // Character name (large, yellow)
        _nameLabel = new Label();
        _nameLabel.Position = new Vector2(10, 8);
        _nameLabel.Size = new Vector2(PanelW - 50, 24);
        _nameLabel.AddThemeFontSizeOverride("font_size", 15);
        _nameLabel.AddThemeColorOverride("font_color", new Color(1.0f, 0.85f, 0.2f));
        _nameLabel.ClipText = true;
        AddChild(_nameLabel);

        // Separator line
        var sep = new ColorRect();
        sep.Color = new Color(0.3f, 0.3f, 0.4f, 0.8f);
        sep.Position = new Vector2(10, 34);
        sep.Size = new Vector2(PanelW - 20, 1);
        AddChild(sep);

        float y = 42;
        const float rowH = 22;

        // Level + Class
        _levelClassLabel = CreateInfoLabel(ref y, rowH);

        // Race
        _raceLabel = CreateInfoLabel(ref y, rowH);

        // Faction
        _factionLabel = CreateInfoLabel(ref y, rowH);

        // Guild
        _guildLabel = CreateInfoLabel(ref y, rowH);

        // Status (Criminal/Ciudadano) — colored
        _statusLabel = CreateInfoLabel(ref y, rowH);

        // Stats (HP/Mana/Sta)
        _statsLabel = CreateInfoLabel(ref y, rowH);

        // Kills
        _killsLabel = CreateInfoLabel(ref y, rowH);

        // "Cerrar" button at bottom
        var cerrarBtn = new Button();
        cerrarBtn.Text = "Cerrar";
        cerrarBtn.Position = new Vector2(PanelW / 2 - 35, PanelH - 34);
        cerrarBtn.Size = new Vector2(70, 26);
        cerrarBtn.Pressed += () => { Visible = false; };
        AddChild(cerrarBtn);
    }

    private Label CreateInfoLabel(ref float y, float rowH)
    {
        var label = new Label();
        label.Position = new Vector2(14, y);
        label.Size = new Vector2(PanelW - 28, rowH);
        label.AddThemeFontSizeOverride("font_size", 11);
        label.AddThemeColorOverride("font_color", new Color(0.85f, 0.85f, 0.9f));
        label.ClipText = true;
        AddChild(label);
        y += rowH;
        return label;
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
