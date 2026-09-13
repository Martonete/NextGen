using Godot;

namespace ArgentumNextgen.UI;

/// <summary>Compact four-button action plate, visually paired with Quickbar.</summary>
public partial class HudActionBar : Control
{
    public const int ButtonWidth = 84;
    public const int ButtonHeight = 34;
    public const int Gap = 4;
    public const int Pad = 5;
    public const int BarWidth = Pad * 2 + ButtonWidth * 4 + Gap * 3;
    public const int BarHeight = Pad * 2 + ButtonHeight;

    private static readonly Color FrameFill = new(0.055f, 0.045f, 0.035f);
    private static readonly Color FrameLine = new(0.72f, 0.58f, 0.34f);
    private static readonly Color FrameInner = new(0.32f, 0.25f, 0.15f);

    public override void _Ready()
    {
        Size = new Vector2(BarWidth, BarHeight);
        MouseFilter = MouseFilterEnum.Ignore;
    }

    public void Attach(params TextureButton?[] buttons)
    {
        for (int i = 0; i < buttons.Length && i < 4; i++)
        {
            var button = buttons[i];
            if (button == null) continue;
            button.Reparent(this, false);
            button.Position = new Vector2(Pad + i * (ButtonWidth + Gap), Pad);
            button.Size = new Vector2(ButtonWidth, ButtonHeight);
            button.AddThemeFontSizeOverride("font_size", 11);
        }
    }

    public override void _Draw()
    {
        var rect = new Rect2(Vector2.Zero, new Vector2(BarWidth, BarHeight));
        DrawRect(rect, FrameFill);
        DrawRect(rect.Grow(-0.5f), FrameLine, false, 1);
        DrawRect(rect.Grow(-2.5f), FrameInner, false, 1);
    }
}
