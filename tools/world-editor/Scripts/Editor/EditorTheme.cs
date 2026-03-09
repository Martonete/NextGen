#nullable enable
using System;
using Godot;

namespace AOWorldEditor.Editor;

/// <summary>
/// Centralized theme system for the World Editor.
/// All colors, font sizes, StyleBoxes, and widget factories in one place.
/// </summary>
public static class EditorTheme
{
    // ── Background colors ──────────────────────────────────────────
    public static readonly Color BG_DARK       = new(0.11f, 0.11f, 0.13f);
    public static readonly Color BG_PANEL      = new(0.15f, 0.15f, 0.17f);
    public static readonly Color BG_HEADER     = new(0.17f, 0.17f, 0.20f);
    public static readonly Color BG_INPUT      = new(0.13f, 0.13f, 0.15f);
    public static readonly Color BG_HOVER      = new(0.22f, 0.22f, 0.28f);
    public static readonly Color BG_SELECTED   = new(0.25f, 0.35f, 0.55f);
    public static readonly Color BG_SECTION    = new(0.14f, 0.14f, 0.16f);

    // ── Text colors ────────────────────────────────────────────────
    public static readonly Color TEXT_PRIMARY   = new(0.90f, 0.90f, 0.92f);
    public static readonly Color TEXT_SECONDARY = new(0.65f, 0.65f, 0.70f);
    public static readonly Color TEXT_MUTED     = new(0.44f, 0.44f, 0.48f);
    public static readonly Color TEXT_ACCENT    = new(0.40f, 0.82f, 1.0f);
    public static readonly Color TEXT_SUCCESS   = new(0.40f, 0.92f, 0.50f);
    public static readonly Color TEXT_WARNING   = new(0.95f, 0.78f, 0.25f);
    public static readonly Color TEXT_DANGER    = new(1.0f, 0.42f, 0.42f);

    // ── Accent / UI colors ─────────────────────────────────────────
    public static readonly Color ACCENT        = new(0.35f, 0.65f, 1.0f);
    public static readonly Color BORDER        = new(0.24f, 0.24f, 0.28f);

    // ── Tool button states ─────────────────────────────────────────
    public static readonly Color BG_TOOL_NORMAL = new(0.16f, 0.16f, 0.19f);
    public static readonly Color BG_TOOL_HOVER  = new(0.22f, 0.22f, 0.28f);
    public static readonly Color BG_TOOL_ACTIVE = new(0.25f, 0.42f, 0.68f);

    // ── Button variants ────────────────────────────────────────────
    public static readonly Color BG_BTN_PRIMARY   = new(0.22f, 0.42f, 0.68f);
    public static readonly Color BG_BTN_PRIMARY_H = new(0.28f, 0.50f, 0.78f);
    public static readonly Color BG_BTN_SUCCESS   = new(0.18f, 0.45f, 0.22f);
    public static readonly Color BG_BTN_SUCCESS_H = new(0.24f, 0.55f, 0.28f);
    public static readonly Color BG_BTN_DANGER    = new(0.55f, 0.18f, 0.18f);
    public static readonly Color BG_BTN_DANGER_H  = new(0.65f, 0.25f, 0.25f);
    public static readonly Color BG_BTN_GHOST     = new(0.18f, 0.18f, 0.22f);
    public static readonly Color BG_BTN_GHOST_H   = new(0.25f, 0.25f, 0.30f);

    // ── Layer colors (1-indexed, so index 0 unused) ────────────────
    public static readonly Color[] LAYER_COLORS = {
        Colors.Transparent,              // 0: unused
        new(0.40f, 0.85f, 0.40f),       // 1: Terreno (green)
        new(0.40f, 0.70f, 1.0f),        // 2: Mascara (blue)
        new(1.0f, 0.70f, 0.30f),        // 3: Objetos (orange)
        new(0.75f, 0.45f, 1.0f),        // 4: Techos (purple)
    };

    // ── Overlay colors (viewport) ──────────────────────────────────
    public static readonly Color OVERLAY_GRID      = new(1f, 1f, 1f, 0.08f);
    public static readonly Color OVERLAY_BLOCKED   = new(1f, 0f, 0f, 0.25f);
    public static readonly Color OVERLAY_EXIT      = new(0f, 1f, 0f, 0.20f);
    public static readonly Color OVERLAY_SELECTION  = new(0.2f, 0.6f, 1f, 0.25f);
    public static readonly Color OVERLAY_HOVER     = new(1f, 1f, 1f, 0.15f);
    public static readonly Color OVERLAY_LIGHT     = new(1f, 1f, 0.5f, 0.30f);
    public static readonly Color OVERLAY_NPC       = new(1f, 0.6f, 0.2f, 0.35f);
    public static readonly Color OVERLAY_OBJECT    = new(0.7f, 0.3f, 1f, 0.35f);
    public static readonly Color OVERLAY_PARTICLE  = new(0.3f, 1f, 0.8f, 0.30f);
    public static readonly Color OVERLAY_TRIGGER   = new(0.8f, 0.8f, 0.3f, 0.25f);

    // ── Font sizes ─────────────────────────────────────────────────
    public const int FONT_SM = 11;
    public const int FONT_MD = 13;
    public const int FONT_LG = 15;
    public const int FONT_XL = 16;

    // ══════════════════════════════════════════════════════════════
    // StyleBox factories
    // ══════════════════════════════════════════════════════════════

    public static StyleBoxFlat FlatBox(Color bg, int corner = 4, int marginH = 8, int marginV = 4,
        Color? border = null, int borderWidth = 0)
    {
        var sb = new StyleBoxFlat();
        sb.BgColor = bg;
        sb.CornerRadiusTopLeft = corner;
        sb.CornerRadiusTopRight = corner;
        sb.CornerRadiusBottomLeft = corner;
        sb.CornerRadiusBottomRight = corner;
        sb.ContentMarginLeft = marginH;
        sb.ContentMarginRight = marginH;
        sb.ContentMarginTop = marginV;
        sb.ContentMarginBottom = marginV;
        if (border.HasValue && borderWidth > 0)
        {
            sb.BorderColor = border.Value;
            sb.BorderWidthTop = borderWidth;
            sb.BorderWidthBottom = borderWidth;
            sb.BorderWidthLeft = borderWidth;
            sb.BorderWidthRight = borderWidth;
        }
        return sb;
    }

    public static PanelContainer StyledPanel(int margin = 4)
    {
        var panel = new PanelContainer();
        panel.AddThemeStyleboxOverride("panel", FlatBox(BG_PANEL, 3, margin, margin, BORDER, 1));
        return panel;
    }

    public static PanelContainer SectionBox(int margin = 6)
    {
        var panel = new PanelContainer();
        panel.AddThemeStyleboxOverride("panel", FlatBox(BG_SECTION, 3, margin, margin));
        return panel;
    }

    // ══════════════════════════════════════════════════════════════
    // Widget factories
    // ══════════════════════════════════════════════════════════════

    public static Label MakeLabel(string text, Color? color = null, int fontSize = FONT_MD)
    {
        var lbl = new Label { Text = text };
        lbl.AddThemeFontSizeOverride("font_size", fontSize);
        if (color.HasValue)
            lbl.AddThemeColorOverride("font_color", color.Value);
        return lbl;
    }

    public static Label Heading(string text)
        => MakeLabel(text, TEXT_ACCENT, FONT_LG);

    public static Label SectionLabel(string text)
        => MakeLabel(text.ToUpper(), TEXT_MUTED, FONT_SM);

    public static Button MakeButton(string text, Action? cb = null, Color? textColor = null)
    {
        var btn = new Button { Text = text };
        btn.AddThemeFontSizeOverride("font_size", FONT_SM);
        btn.AddThemeStyleboxOverride("normal", FlatBox(BG_BTN_GHOST, 3, 8, 3));
        btn.AddThemeStyleboxOverride("hover", FlatBox(BG_BTN_GHOST_H, 3, 8, 3));
        btn.AddThemeStyleboxOverride("pressed", FlatBox(BG_BTN_PRIMARY, 3, 8, 3));
        if (textColor.HasValue)
            btn.AddThemeColorOverride("font_color", textColor.Value);
        if (cb != null) btn.Pressed += cb;
        return btn;
    }

    public static Button PrimaryButton(string text, Action? cb = null)
    {
        var btn = new Button { Text = text };
        btn.AddThemeFontSizeOverride("font_size", FONT_MD);
        btn.AddThemeStyleboxOverride("normal", FlatBox(BG_BTN_PRIMARY, 4, 10, 5));
        btn.AddThemeStyleboxOverride("hover", FlatBox(BG_BTN_PRIMARY_H, 4, 10, 5));
        btn.AddThemeStyleboxOverride("pressed", FlatBox(ACCENT, 4, 10, 5));
        btn.AddThemeColorOverride("font_color", Colors.White);
        if (cb != null) btn.Pressed += cb;
        return btn;
    }

    public static Button SuccessButton(string text, Action? cb = null)
    {
        var btn = new Button { Text = text };
        btn.AddThemeFontSizeOverride("font_size", FONT_MD);
        btn.AddThemeStyleboxOverride("normal", FlatBox(BG_BTN_SUCCESS, 4, 10, 5));
        btn.AddThemeStyleboxOverride("hover", FlatBox(BG_BTN_SUCCESS_H, 4, 10, 5));
        btn.AddThemeColorOverride("font_color", Colors.White);
        if (cb != null) btn.Pressed += cb;
        return btn;
    }

    public static Button DangerButton(string text, Action? cb = null)
    {
        var btn = new Button { Text = text };
        btn.AddThemeFontSizeOverride("font_size", FONT_SM);
        btn.AddThemeStyleboxOverride("normal", FlatBox(BG_BTN_DANGER, 3, 8, 3));
        btn.AddThemeStyleboxOverride("hover", FlatBox(BG_BTN_DANGER_H, 3, 8, 3));
        btn.AddThemeColorOverride("font_color", Colors.White);
        if (cb != null) btn.Pressed += cb;
        return btn;
    }

    /// <summary>
    /// Creates a tool toggle button with stacked icon (top) and label (bottom).
    /// </summary>
    public static Button ToolToggle(string icon, string label, string tooltip)
    {
        var btn = new Button
        {
            TooltipText = tooltip,
            ToggleMode = true,
            CustomMinimumSize = new Vector2(46, 40),
        };
        btn.AddThemeStyleboxOverride("normal", FlatBox(BG_TOOL_NORMAL, 4, 2, 2));
        btn.AddThemeStyleboxOverride("hover", FlatBox(BG_TOOL_HOVER, 4, 2, 2));
        btn.AddThemeStyleboxOverride("pressed", FlatBox(BG_TOOL_ACTIVE, 4, 2, 2, ACCENT, 1));

        var vbox = new VBoxContainer();
        vbox.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        vbox.AddThemeConstantOverride("separation", -2);
        vbox.Alignment = BoxContainer.AlignmentMode.Center;
        vbox.MouseFilter = Control.MouseFilterEnum.Ignore;

        var iconLbl = new Label
        {
            Text = icon,
            HorizontalAlignment = HorizontalAlignment.Center,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        iconLbl.AddThemeFontSizeOverride("font_size", 16);
        iconLbl.AddThemeColorOverride("font_color", TEXT_PRIMARY);
        vbox.AddChild(iconLbl);

        var textLbl = new Label
        {
            Text = label,
            HorizontalAlignment = HorizontalAlignment.Center,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        textLbl.AddThemeFontSizeOverride("font_size", 8);
        textLbl.AddThemeColorOverride("font_color", TEXT_SECONDARY);
        vbox.AddChild(textLbl);

        btn.AddChild(vbox);
        return btn;
    }

    /// <summary>
    /// Layer tab button (compact, with colored left border indicator).
    /// </summary>
    public static Button LayerTab(int layerNum, Action? cb = null)
    {
        var color = layerNum >= 1 && layerNum <= 4 ? LAYER_COLORS[layerNum] : TEXT_PRIMARY;
        var btn = new Button
        {
            Text = layerNum.ToString(),
            ToggleMode = true,
            CustomMinimumSize = new Vector2(28, 26),
            TooltipText = layerNum switch
            {
                1 => "Terreno (1)",
                2 => "Mascara (2)",
                3 => "Objetos (3)",
                4 => "Techos (4)",
                _ => $"Capa {layerNum}"
            },
        };
        btn.AddThemeFontSizeOverride("font_size", FONT_SM);
        btn.AddThemeColorOverride("font_color", color);

        var normal = FlatBox(BG_TOOL_NORMAL, 3, 4, 2);
        normal.BorderWidthLeft = 2;
        normal.BorderColor = color with { A = 0.4f };
        btn.AddThemeStyleboxOverride("normal", normal);

        var hover = FlatBox(BG_TOOL_HOVER, 3, 4, 2);
        hover.BorderWidthLeft = 2;
        hover.BorderColor = color;
        btn.AddThemeStyleboxOverride("hover", hover);

        var pressed = FlatBox(BG_SELECTED, 3, 4, 2);
        pressed.BorderWidthLeft = 3;
        pressed.BorderColor = color;
        btn.AddThemeStyleboxOverride("pressed", pressed);

        if (cb != null) btn.Pressed += cb;
        return btn;
    }

    public static SpinBox MakeSpinBox(double min, double max, double step = 1, double value = 0)
    {
        var spin = new SpinBox
        {
            MinValue = min,
            MaxValue = max,
            Step = step,
            Value = value,
            CustomMinimumSize = new Vector2(72, 0),
        };
        spin.AddThemeFontSizeOverride("font_size", FONT_SM);
        return spin;
    }

    public static HSeparator MakeHSeparator()
    {
        var sep = new HSeparator();
        sep.AddThemeConstantOverride("separation", 6);
        return sep;
    }

    public static VSeparator MakeVSeparator()
    {
        var sep = new VSeparator();
        sep.AddThemeConstantOverride("separation", 6);
        return sep;
    }

    /// <summary>
    /// A spacer control that expands to fill available space.
    /// </summary>
    public static Control Spacer()
    {
        var ctrl = new Control();
        ctrl.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        return ctrl;
    }

    /// <summary>
    /// Navigation button for map nav bar, with color based on state.
    /// </summary>
    public static void StyleNavButton(Button btn, bool isCurrent, bool exists)
    {
        if (isCurrent)
        {
            btn.AddThemeStyleboxOverride("normal", FlatBox(BG_BTN_SUCCESS, 3, 4, 2));
            btn.AddThemeStyleboxOverride("hover", FlatBox(BG_BTN_SUCCESS_H, 3, 4, 2));
            btn.AddThemeColorOverride("font_color", Colors.White);
            btn.Modulate = Colors.White;
        }
        else if (exists)
        {
            btn.AddThemeStyleboxOverride("normal", FlatBox(BG_BTN_GHOST, 3, 4, 2));
            btn.AddThemeStyleboxOverride("hover", FlatBox(BG_BTN_GHOST_H, 3, 4, 2));
            btn.AddThemeColorOverride("font_color", TEXT_PRIMARY);
            btn.Modulate = Colors.White;
        }
        else
        {
            btn.AddThemeStyleboxOverride("normal", FlatBox(BG_BTN_GHOST, 3, 4, 2));
            btn.AddThemeStyleboxOverride("hover", FlatBox(BG_BTN_GHOST_H, 3, 4, 2));
            btn.AddThemeColorOverride("font_color", TEXT_MUTED);
            btn.Modulate = new Color(0.6f, 0.6f, 0.6f);
        }
    }
}
