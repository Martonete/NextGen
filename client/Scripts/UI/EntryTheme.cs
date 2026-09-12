using Godot;

namespace ArgentumNextgen.UI;

/// <summary>Entry controls share the Tierras Sagradas identity with the game HUD.</summary>
public static class EntryTheme
{
    public static readonly Color Gold = SacredTheme.Bronze;
    public static StyleBoxFlat Box(string fill = "11181c", string border = "4c483c", int padding = 12)
    {
        var box = new StyleBoxFlat { BgColor = new Color(fill), BorderColor = new Color(border) };
        box.SetBorderWidthAll(1);
        box.SetCornerRadiusAll(2);
        box.ContentMarginLeft = box.ContentMarginRight = padding;
        box.ContentMarginTop = box.ContentMarginBottom = padding;
        return box;
    }

    public static Label Text(string text, int size = 14, bool muted = false)
    {
        var label = new Label { Text = text, MouseFilter = Control.MouseFilterEnum.Ignore };
        label.AddThemeFontSizeOverride("font_size", size);
        label.AddThemeFontOverride("font", GameFonts.AlegreyaRegular);
        label.AddThemeColorOverride("font_color", muted ? new Color("a3b1b4") : new Color("eee5d1"));
        return label;
    }

    public static VBoxContainer Header(string eyebrow, string title, string subtitle)
    {
        var column = RpgTheme.CreateColumn(5);
        var masthead = RpgTheme.CreateRow(12);
        masthead.AddChild(new SacredSeal { CustomMinimumSize = new Vector2(52, 52) });
        var wordmark = Text("TIERRAS SAGRADAS", 19);
        wordmark.AddThemeFontOverride("font", SacredTheme.Display);
        wordmark.VerticalAlignment = VerticalAlignment.Center;
        masthead.AddChild(wordmark);
        column.AddChild(masthead);
        var overline = Text(eyebrow, 11);
        overline.AddThemeColorOverride("font_color", Gold);
        column.AddChild(overline);
        var heading = Text(title, 26);
        heading.AddThemeFontOverride("font", SacredTheme.Display);
        column.AddChild(heading);
        var sub = Text(subtitle, 14, true);
        sub.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        column.AddChild(sub);
        column.AddChild(RpgTheme.CreateSpacer(4));
        return column;
    }

    public static Button Button(string text, bool primary = false)
    {
        var button = new Button { Text = text, CustomMinimumSize = new Vector2(0, 40), MouseDefaultCursorShape = Control.CursorShape.PointingHand };
        button.AddThemeFontSizeOverride("font_size", 14);
        button.AddThemeColorOverride("font_color", primary ? new Color("181b19") : new Color("eee5d1"));
        button.AddThemeStyleboxOverride("normal", Box(primary ? "c5a772" : "17252b", primary ? "e0c797" : "45565a"));
        button.AddThemeStyleboxOverride("hover", Box(primary ? "dfc18c" : "24373e", "d2b57e"));
        button.AddThemeStyleboxOverride("pressed", Box("53615a", "d2b57e"));
        button.AddThemeStyleboxOverride("disabled", Box("283236", "394448"));
        var focus = Box("00000000", "f3d49a", 0);
        focus.SetBorderWidthAll(2);
        button.AddThemeStyleboxOverride("focus", focus);
        SacredTheme.StyleButton(button, primary);
        return button;
    }

    public static LineEdit Input(string placeholder)
    {
        var input = new LineEdit { PlaceholderText = placeholder, CustomMinimumSize = new Vector2(0, 42) };
        input.AddThemeStyleboxOverride("normal", Box("0b1419", "405258", 10));
        input.AddThemeStyleboxOverride("focus", Box("101e25", "c5a772", 10));
        input.AddThemeFontSizeOverride("font_size", 15);
        input.AddThemeColorOverride("font_color", new Color("f0eadd"));
        input.AddThemeColorOverride("font_placeholder_color", new Color("829296"));
        SacredTheme.StyleInput(input);
        return input;
    }
}
