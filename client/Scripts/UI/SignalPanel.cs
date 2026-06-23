using Godot;
using ArgentumNextgen.Game;

namespace ArgentumNextgen.UI;

/// <summary>
/// VB6 frmCartel — Shows a sign/signal overlay with text and optional graphic.
/// Triggered by ShowSignal packet (ID 58). Auto-hides after a few seconds.
/// </summary>
public partial class SignalPanel : Control
{
    private Label? _textLabel;
    private TextureRect? _grhImage;
    private float _timer;
    private const float DisplayDuration = 8.0f;

    public override void _Ready()
    {
        Visible = false;
        ZIndex = RpgBaseForm.ZDialog;

        // Semi-transparent dark background
        var bg = new ColorRect
        {
            Color = new Color(0, 0, 0, 0.75f),
            CustomMinimumSize = new Vector2(300, 120),
            SizeFlagsHorizontal = SizeFlags.ShrinkCenter,
        };
        AddChild(bg);

        var vbox = new VBoxContainer();
        vbox.SetAnchorsPreset(LayoutPreset.FullRect);
        vbox.AddThemeConstantOverride("separation", 8);
        bg.AddChild(vbox);

        // GRH image placeholder (optional)
        _grhImage = new TextureRect
        {
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
            CustomMinimumSize = new Vector2(64, 64),
            SizeFlagsHorizontal = SizeFlags.ShrinkCenter,
            Visible = false,
        };
        vbox.AddChild(_grhImage);

        // Text label
        _textLabel = new Label
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            CustomMinimumSize = new Vector2(280, 0),
        };
        _textLabel.AddThemeColorOverride("font_color", Colors.White);
        _textLabel.AddThemeFontSizeOverride("font_size", 14);
        vbox.AddChild(_textLabel);

        // Center on screen
        SetAnchorsPreset(LayoutPreset.Center);
    }

    public void ShowSignal(string text, int grh)
    {
        if (_textLabel != null) _textLabel.Text = text;
        // GRH image rendering would require GameData lookup — skip for now, text is enough
        _grhImage!.Visible = false;
        _timer = DisplayDuration;
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
}
