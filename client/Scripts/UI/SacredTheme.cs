using Godot;

namespace ArgentumNextgen.UI;

/// <summary>Shared, resolution-independent visual identity. Resources are cached;
/// ornaments redraw only on resize, not every game frame.</summary>
public static class SacredTheme
{
    public static readonly Color Ink = new("111519"), Paper = new("eee4ce"),
        Bronze = new("b99a66"), Muted = new("a5a79e"), Edge = new("4c483c"),
        Jade = new("7fae9b"), Danger = new("db9184");
    private static Font? _display;
    public static Font Display => _display ??= new SystemFont
    { FontNames = new[] { "Palatino Linotype", "Georgia", "Times New Roman" }, FontWeight = 600 };

    public static StyleBoxFlat Surface(Color fill, Color border, int padding = 8)
    {
        var s = new StyleBoxFlat { BgColor = fill, BorderColor = border };
        s.SetBorderWidthAll(1);
        s.SetCornerRadiusAll(2);
        s.ContentMarginLeft = s.ContentMarginRight = padding;
        s.ContentMarginTop = s.ContentMarginBottom = padding;
        return s;
    }

    public static SacredFrame Frame(bool entry = false)
        => new() { Entry = entry, MouseFilter = Control.MouseFilterEnum.Ignore };

    public static void StyleInput(LineEdit input)
    {
        input.AddThemeFontOverride("font", GameFonts.AlegreyaRegular);
        input.AddThemeColorOverride("font_color", Paper);
        input.AddThemeColorOverride("font_placeholder_color", Muted);
        input.AddThemeColorOverride("caret_color", Bronze);
        input.AddThemeColorOverride("selection_color", new Color("546657"));
        input.AddThemeStyleboxOverride("normal", Surface(new Color("0b1013"), Edge, 10));
        var focus = Surface(new Color("161e21"), Bronze, 10);
        focus.BorderWidthBottom = 2;
        input.AddThemeStyleboxOverride("focus", focus);
        input.AddThemeStyleboxOverride("read_only", Surface(Ink, Edge, 10));
    }

    public static void StyleButton(Button button, bool primary)
    {
        button.AddThemeFontOverride("font", GameFonts.AlegreyaBold);
        button.AddThemeColorOverride("font_color", primary ? Ink : Paper);
        button.AddThemeColorOverride("font_hover_color", primary ? Ink : Colors.White);
        button.AddThemeColorOverride("font_pressed_color", Paper);
        button.AddThemeColorOverride("font_disabled_color", Muted);
        button.AddThemeStyleboxOverride("normal", Surface(primary ? Bronze : new Color("20272a"), primary ? new Color("e3c996") : Edge));
        button.AddThemeStyleboxOverride("hover", Surface(primary ? new Color("dcc294") : new Color("303d3e"), Bronze));
        button.AddThemeStyleboxOverride("pressed", Surface(new Color("39443e"), Bronze));
        button.AddThemeStyleboxOverride("disabled", Surface(new Color("1a2023"), new Color("343a3b")));
        var focus = Surface(Colors.Transparent, Paper, 0);
        focus.SetBorderWidthAll(2);
        button.AddThemeStyleboxOverride("focus", focus);
    }

    public static Button CloseButton()
    {
        var b = new Button { Text = "×", FocusMode = Control.FocusModeEnum.None,
            MouseDefaultCursorShape = Control.CursorShape.PointingHand, TooltipText = "Cerrar" };
        StyleButton(b, false);
        foreach (string state in new[] { "normal", "hover", "pressed", "disabled" })
        {
            var s = Surface(state == "hover" ? new Color("45352e") : Ink, Edge, 0);
            b.AddThemeStyleboxOverride(state, s);
        }
        b.AddThemeFontSizeOverride("font_size", 17);
        return b;
    }
}
