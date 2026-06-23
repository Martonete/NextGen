#nullable enable
using Godot;

namespace AODateador.Editor;

/// <summary>Static dark theme with factory methods for UI widgets.</summary>
public static class DateadorTheme
{
    // ── Colors ──
    public static readonly Color BG_DARK    = new(0.112f, 0.114f, 0.128f);
    public static readonly Color BG_PANEL   = new(0.148f, 0.152f, 0.170f);
    public static readonly Color BG_SECTION = new(0.175f, 0.180f, 0.200f);
    public static readonly Color BG_INPUT   = new(0.10f, 0.10f, 0.12f);
    public static readonly Color BG_BTN     = new(0.22f, 0.23f, 0.26f);
    public static readonly Color BG_BTN_H   = new(0.28f, 0.29f, 0.32f);
    public static readonly Color BG_BTN_PRI = new(0.20f, 0.50f, 0.90f);
    public static readonly Color BG_BTN_PRI_H = new(0.25f, 0.55f, 0.95f);
    public static readonly Color BG_TAB     = new(0.16f, 0.17f, 0.19f);
    public static readonly Color BG_TAB_SEL = new(0.22f, 0.24f, 0.28f);
    public static readonly Color TEXT_PRI   = new(0.92f, 0.92f, 0.94f);
    public static readonly Color TEXT_SEC   = new(0.60f, 0.62f, 0.66f);
    public static readonly Color TEXT_DIM   = new(0.45f, 0.46f, 0.50f);
    public static readonly Color ACCENT     = new(0.29f, 0.62f, 1.0f);
    public static readonly Color BORDER     = new(0.22f, 0.23f, 0.26f);
    public static readonly Color DIRTY_RED  = new(0.95f, 0.35f, 0.30f);
    public static readonly Color SUCCESS    = new(0.30f, 0.85f, 0.45f);

    // ── Font sizes ──
    public const int FONT_XS = 10;
    public const int FONT_SM = 12;
    public const int FONT_MD = 13;
    public const int FONT_LG = 15;
    public const int FONT_XL = 18;

    // ── StyleBox factory ──
    public static StyleBoxFlat FlatBox(Color bg, int corner = 4, int mH = 8, int mV = 4,
        Color? border = null, int bw = 0)
    {
        var sb = new StyleBoxFlat { BgColor = bg };
        sb.CornerRadiusTopLeft = sb.CornerRadiusTopRight =
            sb.CornerRadiusBottomLeft = sb.CornerRadiusBottomRight = corner;
        sb.ContentMarginLeft = sb.ContentMarginRight = mH;
        sb.ContentMarginTop = sb.ContentMarginBottom = mV;
        if (border.HasValue && bw > 0)
        {
            sb.BorderColor = border.Value;
            sb.BorderWidthTop = sb.BorderWidthBottom =
                sb.BorderWidthLeft = sb.BorderWidthRight = bw;
        }
        return sb;
    }

    // ── Widget factories ──
    public static Label MakeLabel(string text, Color? color = null, int fontSize = FONT_MD)
    {
        var lbl = new Label { Text = text };
        lbl.AddThemeFontSizeOverride("font_size", fontSize);
        if (color.HasValue) lbl.AddThemeColorOverride("font_color", color.Value);
        return lbl;
    }

    public static Label Heading(string text)
        => MakeLabel(text, ACCENT, FONT_LG);

    public static Button PrimaryButton(string text, Action? cb = null)
    {
        var btn = new Button { Text = text };
        btn.AddThemeFontSizeOverride("font_size", FONT_MD);
        btn.AddThemeStyleboxOverride("normal", FlatBox(BG_BTN_PRI, 5, 12, 6));
        btn.AddThemeStyleboxOverride("hover", FlatBox(BG_BTN_PRI_H, 5, 12, 6));
        btn.AddThemeStyleboxOverride("pressed", FlatBox(BG_BTN_PRI, 5, 12, 6));
        btn.AddThemeColorOverride("font_color", Colors.White);
        if (cb != null) btn.Pressed += cb;
        return btn;
    }

    public static Button SecondaryButton(string text, Action? cb = null)
    {
        var btn = new Button { Text = text };
        btn.AddThemeFontSizeOverride("font_size", FONT_MD);
        btn.AddThemeStyleboxOverride("normal", FlatBox(BG_BTN, 4, 10, 4));
        btn.AddThemeStyleboxOverride("hover", FlatBox(BG_BTN_H, 4, 10, 4));
        btn.AddThemeColorOverride("font_color", TEXT_PRI);
        if (cb != null) btn.Pressed += cb;
        return btn;
    }

    public static LineEdit MakeLineEdit(string placeholder = "", int width = 0)
    {
        var le = new LineEdit { PlaceholderText = placeholder };
        le.AddThemeFontSizeOverride("font_size", FONT_MD);
        le.AddThemeStyleboxOverride("normal", FlatBox(BG_INPUT, 3, 6, 3, BORDER, 1));
        le.AddThemeStyleboxOverride("focus", FlatBox(BG_INPUT, 3, 6, 3, ACCENT, 1));
        le.AddThemeColorOverride("font_color", TEXT_PRI);
        le.AddThemeColorOverride("font_placeholder_color", TEXT_DIM);
        if (width > 0) le.CustomMinimumSize = new Vector2(width, 0);
        return le;
    }

    public static SpinBox MakeSpinBox(double min, double max, double step = 1, int width = 100)
    {
        var sb = new SpinBox
        {
            MinValue = min, MaxValue = max, Step = step,
            CustomMinimumSize = new Vector2(width, 0),
            Alignment = HorizontalAlignment.Right
        };
        sb.AddThemeFontSizeOverride("font_size", FONT_MD);
        return sb;
    }

    public static CheckBox MakeCheckBox(string text)
    {
        var cb = new CheckBox { Text = text };
        cb.AddThemeFontSizeOverride("font_size", FONT_MD);
        cb.AddThemeColorOverride("font_color", TEXT_PRI);
        return cb;
    }

    public static OptionButton MakeOptionButton(string[] items, int selected = 0)
    {
        var ob = new OptionButton();
        ob.AddThemeFontSizeOverride("font_size", FONT_MD);
        foreach (var item in items) ob.AddItem(item);
        if (selected >= 0 && selected < items.Length) ob.Selected = selected;
        return ob;
    }

    public static HSeparator Separator()
    {
        var sep = new HSeparator();
        sep.AddThemeConstantOverride("separation", 8);
        return sep;
    }

    /// <summary>Creates a styled panel container for sections.</summary>
    public static PanelContainer SectionPanel()
    {
        var p = new PanelContainer();
        p.AddThemeStyleboxOverride("panel", FlatBox(BG_SECTION, 6, 10, 8, BORDER, 1));
        return p;
    }

    /// <summary>Apply dark theme to an ItemList.</summary>
    public static void StyleItemList(ItemList list)
    {
        list.AddThemeFontSizeOverride("font_size", FONT_MD);
        list.AddThemeColorOverride("font_color", TEXT_PRI);
        list.AddThemeColorOverride("font_selected_color", Colors.White);
        list.AddThemeStyleboxOverride("panel", FlatBox(BG_DARK, 0, 0, 0));
        list.AddThemeStyleboxOverride("selected", FlatBox(ACCENT with { A = 0.3f }, 2, 4, 2));
        list.AddThemeStyleboxOverride("selected_focus", FlatBox(ACCENT with { A = 0.4f }, 2, 4, 2));
    }
}
