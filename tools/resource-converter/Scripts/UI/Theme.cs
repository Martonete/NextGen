#nullable enable
using Godot;

namespace AOResourceConverter.UI;

public static class Theme
{
    public static readonly Color BG_DARK       = new(0.112f, 0.114f, 0.128f);
    public static readonly Color BG_PANEL      = new(0.148f, 0.152f, 0.170f);
    public static readonly Color BG_INPUT      = new(0.125f, 0.128f, 0.148f);
    public static readonly Color BG_SELECTED   = new(0.220f, 0.320f, 0.520f);
    public static readonly Color TEXT_PRIMARY   = new(0.92f, 0.92f, 0.94f);
    public static readonly Color TEXT_SECONDARY = new(0.62f, 0.64f, 0.70f);
    public static readonly Color TEXT_MUTED     = new(0.42f, 0.43f, 0.48f);
    public static readonly Color TEXT_SUCCESS   = new(0.35f, 0.85f, 0.45f);
    public static readonly Color TEXT_DANGER    = new(1.0f, 0.40f, 0.40f);
    public static readonly Color ACCENT         = new(0.290f, 0.620f, 1.0f);
    public static readonly Color BORDER         = new(0.22f, 0.23f, 0.27f);

    public const int FONT_SM = 12;
    public const int FONT_MD = 14;
    public const int FONT_LG = 16;
    public const int FONT_XL = 20;

    public static Label MakeLabel(string text, Color? color = null, int fontSize = FONT_MD)
    {
        var label = new Label { Text = text };
        label.AddThemeFontSizeOverride("font_size", fontSize);
        if (color.HasValue)
            label.AddThemeColorOverride("font_color", color.Value);
        return label;
    }

    public static Label Heading(string text)
    {
        var label = MakeLabel(text, TEXT_PRIMARY, FONT_XL);
        label.HorizontalAlignment = HorizontalAlignment.Center;
        return label;
    }

    public static Label SectionLabel(string text) =>
        MakeLabel(text, TEXT_SECONDARY, FONT_SM);

    public static Button PrimaryButton(string text)
    {
        var btn = new Button { Text = text };
        btn.AddThemeFontSizeOverride("font_size", FONT_MD);
        btn.CustomMinimumSize = new Vector2(0, 36);
        var style = new StyleBoxFlat();
        style.BgColor = new Color(0.200f, 0.400f, 0.660f);
        style.CornerRadiusBottomLeft = style.CornerRadiusBottomRight =
            style.CornerRadiusTopLeft = style.CornerRadiusTopRight = 4;
        btn.AddThemeStyleboxOverride("normal", style);
        var hover = (StyleBoxFlat)style.Duplicate();
        hover.BgColor = new Color(0.250f, 0.470f, 0.750f);
        btn.AddThemeStyleboxOverride("hover", hover);
        return btn;
    }

    public static Button MakeButton(string text)
    {
        var btn = new Button { Text = text };
        btn.AddThemeFontSizeOverride("font_size", FONT_MD);
        btn.CustomMinimumSize = new Vector2(0, 32);
        return btn;
    }

    public static OptionButton MakeOptionButton()
    {
        var opt = new OptionButton();
        opt.AddThemeFontSizeOverride("font_size", FONT_MD);
        opt.CustomMinimumSize = new Vector2(0, 32);
        return opt;
    }

    public static ProgressBar MakeProgressBar()
    {
        var bar = new ProgressBar();
        bar.CustomMinimumSize = new Vector2(0, 24);
        bar.ShowPercentage = true;
        return bar;
    }

    public static HSeparator Sep() => new();
}
