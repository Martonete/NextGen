using Godot;
using ArgentumNextgen.Game;

namespace ArgentumNextgen.UI;

/// <summary>
/// NPC dialog panel — shows NPC speech/description in a small fixed panel
/// at bottom-center of screen. Auto-hides after 8 seconds.
/// Triggered when an NPC ChatOverHead is received (NpcNumber > 0).
/// </summary>
public partial class NpcDialogPanel : Control
{
    private const int PanelW = 300;
    private const int PanelH = 120;
    private const float AutoHideSeconds = 8.0f;

    private GameState? _state;

    private Label? _nameLabel;
    private RichTextLabel? _textLabel;
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
        ZIndex = RpgBaseForm.ZTooltip;

        // Background: solid dark + NinePatch frame
        var solidBg = new ColorRect();
        solidBg.Color = new Color(0.06f, 0.06f, 0.10f, 0.92f);
        solidBg.MouseFilter = MouseFilterEnum.Ignore;
        AddChild(solidBg);
        RpgTheme.FillParent(solidBg);

        var frameBg = RpgTheme.CreateNinePatch("info_window.png", new Vector4(16, 16, 16, 16));
        AddChild(frameBg);
        RpgTheme.FillParent(frameBg);

        // Content margin
        var margin = new MarginContainer();
        margin.MouseFilter = MouseFilterEnum.Ignore;
        margin.AddThemeConstantOverride("margin_top", RpgTheme.SpacingMd);
        margin.AddThemeConstantOverride("margin_left", RpgTheme.SpacingLg);
        margin.AddThemeConstantOverride("margin_right", RpgTheme.SpacingLg);
        margin.AddThemeConstantOverride("margin_bottom", RpgTheme.SpacingMd);
        AddChild(margin);
        RpgTheme.FillParent(margin);

        var col = RpgTheme.CreateColumn(RpgTheme.SpacingSm);
        margin.AddChild(col);

        // Header row: NPC name + close button
        var headerRow = RpgTheme.CreateRow(RpgTheme.SpacingSm);
        col.AddChild(headerRow);

        _nameLabel = RpgTheme.CreateTitleLabel("", 13);
        _nameLabel.HorizontalAlignment = HorizontalAlignment.Left;
        _nameLabel.ClipText = true;
        _nameLabel.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        headerRow.AddChild(_nameLabel);

        var closeBtn = RpgTheme.CreateMiniButton("Mini_exit.png", "Mini_exit_t.png", new Vector2(22, 22));
        closeBtn.Pressed += () => { Visible = false; };
        headerRow.AddChild(closeBtn);

        // Dialog text (white, word-wrapped)
        _textLabel = new RichTextLabel();
        _textLabel.BbcodeEnabled = false;
        _textLabel.ScrollActive = true;
        _textLabel.SizeFlagsVertical = SizeFlags.ExpandFill;
        _textLabel.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        _textLabel.AddThemeColorOverride("default_color", new Color(0.9f, 0.9f, 0.9f));
        _textLabel.AddThemeFontSizeOverride("normal_font_size", 12);
        col.AddChild(_textLabel);

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
    /// Show the NPC dialog with the given name and text.
    /// Resets the auto-hide timer.
    /// </summary>
    public void ShowDialog(string npcName, string dialogText)
    {
        if (_nameLabel == null || _textLabel == null) return;

        _nameLabel.Text = npcName;
        _textLabel.Text = dialogText;
        _timer = AutoHideSeconds;
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
        // Consume mouse events to prevent click-through
        if (@event is InputEventMouseButton mb && mb.Pressed)
        {
            // Reset timer on interaction
            _timer = AutoHideSeconds;
            GetViewport().SetInputAsHandled();
        }
    }
}
