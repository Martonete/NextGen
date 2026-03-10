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
        bg.Color = new Color(0.06f, 0.06f, 0.10f, 0.92f);
        bg.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        AddChild(bg);

        // Border (thin outline)
        var border = new ReferenceRect();
        border.EditorOnly = false;
        border.BorderColor = new Color(0.8f, 0.7f, 0.3f, 0.6f);
        border.BorderWidth = 1.0f;
        border.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        AddChild(border);

        // NPC name label (yellow, top)
        _nameLabel = new Label();
        _nameLabel.Position = new Vector2(10, 6);
        _nameLabel.Size = new Vector2(PanelW - 50, 22);
        _nameLabel.AddThemeFontSizeOverride("font_size", 13);
        _nameLabel.AddThemeColorOverride("font_color", new Color(1.0f, 0.85f, 0.2f));
        _nameLabel.ClipText = true;
        AddChild(_nameLabel);

        // Close button
        _closeBtn = new Button();
        _closeBtn.Text = "X";
        _closeBtn.Position = new Vector2(PanelW - 28, 4);
        _closeBtn.Size = new Vector2(24, 22);
        _closeBtn.Pressed += () => { Visible = false; };
        AddChild(_closeBtn);

        // Dialog text (white, word-wrapped)
        _textLabel = new RichTextLabel();
        _textLabel.Position = new Vector2(10, 30);
        _textLabel.Size = new Vector2(PanelW - 20, PanelH - 70);
        _textLabel.BbcodeEnabled = false;
        _textLabel.ScrollActive = true;
        _textLabel.AddThemeColorOverride("default_color", new Color(0.9f, 0.9f, 0.9f));
        _textLabel.AddThemeFontSizeOverride("normal_font_size", 12);
        AddChild(_textLabel);

        // "Cerrar" button at bottom
        var cerrarBtn = new Button();
        cerrarBtn.Text = "Cerrar";
        cerrarBtn.Position = new Vector2(PanelW / 2 - 35, PanelH - 34);
        cerrarBtn.Size = new Vector2(70, 26);
        cerrarBtn.Pressed += () => { Visible = false; };
        AddChild(cerrarBtn);
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
