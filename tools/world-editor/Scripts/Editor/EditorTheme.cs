#nullable enable
using System;
using Godot;

namespace AOWorldEditor.Editor;

/// <summary>
/// Centralized theme system for the World Editor.
/// Professional dark theme with programmatic vector icons.
/// </summary>
public static class EditorTheme
{
    // ── Background colors (warm dark tones) ──────────────────────────
    public static readonly Color BG_DARK       = new(0.112f, 0.114f, 0.128f);
    public static readonly Color BG_PANEL      = new(0.148f, 0.152f, 0.170f);
    public static readonly Color BG_HEADER     = new(0.168f, 0.172f, 0.195f);
    public static readonly Color BG_INPUT      = new(0.125f, 0.128f, 0.148f);
    public static readonly Color BG_HOVER      = new(0.200f, 0.208f, 0.250f);
    public static readonly Color BG_SELECTED   = new(0.220f, 0.320f, 0.520f);
    public static readonly Color BG_SECTION    = new(0.135f, 0.138f, 0.158f);

    // ── Text colors ──────────────────────────────────────────────────
    public static readonly Color TEXT_PRIMARY   = new(0.92f, 0.92f, 0.94f);
    public static readonly Color TEXT_SECONDARY = new(0.62f, 0.64f, 0.70f);
    public static readonly Color TEXT_MUTED     = new(0.42f, 0.43f, 0.48f);
    public static readonly Color TEXT_ACCENT    = new(0.290f, 0.620f, 1.0f);
    public static readonly Color TEXT_SUCCESS   = new(0.35f, 0.85f, 0.45f);
    public static readonly Color TEXT_WARNING   = new(0.95f, 0.78f, 0.25f);
    public static readonly Color TEXT_DANGER    = new(1.0f, 0.40f, 0.40f);

    // ── Accent / UI colors ───────────────────────────────────────────
    public static readonly Color ACCENT        = new(0.290f, 0.620f, 1.0f);   // #4A9EFF
    public static readonly Color ACCENT_DIM    = new(0.200f, 0.440f, 0.760f);
    public static readonly Color BORDER        = new(0.22f, 0.23f, 0.27f);
    public static readonly Color BORDER_SUBTLE = new(0.19f, 0.20f, 0.24f);

    // ── Tool button states ───────────────────────────────────────────
    public static readonly Color BG_TOOL_NORMAL = new(0.155f, 0.160f, 0.182f);
    public static readonly Color BG_TOOL_HOVER  = new(0.195f, 0.202f, 0.240f);
    public static readonly Color BG_TOOL_ACTIVE = new(0.220f, 0.400f, 0.660f);

    // ── Button variants ──────────────────────────────────────────────
    public static readonly Color BG_BTN_PRIMARY   = new(0.200f, 0.400f, 0.660f);
    public static readonly Color BG_BTN_PRIMARY_H = new(0.250f, 0.470f, 0.750f);
    public static readonly Color BG_BTN_SUCCESS   = new(0.16f, 0.42f, 0.20f);
    public static readonly Color BG_BTN_SUCCESS_H = new(0.22f, 0.52f, 0.26f);
    public static readonly Color BG_BTN_DANGER    = new(0.52f, 0.16f, 0.16f);
    public static readonly Color BG_BTN_DANGER_H  = new(0.62f, 0.22f, 0.22f);
    public static readonly Color BG_BTN_GHOST     = new(0.17f, 0.175f, 0.20f);
    public static readonly Color BG_BTN_GHOST_H   = new(0.22f, 0.228f, 0.27f);

    // ── Layer colors (1-indexed, so index 0 unused) ──────────────────
    public static readonly Color[] LAYER_COLORS = {
        Colors.Transparent,              // 0: unused
        new(0.40f, 0.85f, 0.40f),       // 1: Terreno (green)
        new(0.40f, 0.70f, 1.0f),        // 2: Mascara (blue)
        new(1.0f, 0.70f, 0.30f),        // 3: Objetos (orange)
        new(0.75f, 0.45f, 1.0f),        // 4: Techos (purple)
    };

    // ── Overlay colors (viewport) ────────────────────────────────────
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

    // ── Font sizes ───────────────────────────────────────────────────
    public const int FONT_SM = 12;
    public const int FONT_MD = 14;
    public const int FONT_LG = 16;
    public const int FONT_XL = 18;

    // ── Icon drawing constants ───────────────────────────────────────
    private const float ICON_LINE_WIDTH = 1.6f;

    // ══════════════════════════════════════════════════════════════════
    // StyleBox factories
    // ══════════════════════════════════════════════════════════════════

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

    // ══════════════════════════════════════════════════════════════════
    // Widget factories
    // ══════════════════════════════════════════════════════════════════

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
        btn.AddThemeStyleboxOverride("normal", FlatBox(BG_BTN_GHOST, 4, 8, 3));
        btn.AddThemeStyleboxOverride("hover", FlatBox(BG_BTN_GHOST_H, 4, 8, 3, BORDER_SUBTLE, 1));
        btn.AddThemeStyleboxOverride("pressed", FlatBox(BG_BTN_PRIMARY, 4, 8, 3));
        if (textColor.HasValue)
            btn.AddThemeColorOverride("font_color", textColor.Value);
        if (cb != null) btn.Pressed += cb;
        return btn;
    }

    public static Button PrimaryButton(string text, Action? cb = null)
    {
        var btn = new Button { Text = text };
        btn.AddThemeFontSizeOverride("font_size", FONT_MD);
        btn.AddThemeStyleboxOverride("normal", FlatBox(BG_BTN_PRIMARY, 5, 12, 6));
        btn.AddThemeStyleboxOverride("hover", FlatBox(BG_BTN_PRIMARY_H, 5, 12, 6, ACCENT_DIM, 1));
        btn.AddThemeStyleboxOverride("pressed", FlatBox(ACCENT, 5, 12, 6));
        btn.AddThemeColorOverride("font_color", Colors.White);
        if (cb != null) btn.Pressed += cb;
        return btn;
    }

    public static Button SuccessButton(string text, Action? cb = null)
    {
        var btn = new Button { Text = text };
        btn.AddThemeFontSizeOverride("font_size", FONT_MD);
        btn.AddThemeStyleboxOverride("normal", FlatBox(BG_BTN_SUCCESS, 5, 12, 6));
        btn.AddThemeStyleboxOverride("hover", FlatBox(BG_BTN_SUCCESS_H, 5, 12, 6));
        btn.AddThemeColorOverride("font_color", Colors.White);
        if (cb != null) btn.Pressed += cb;
        return btn;
    }

    public static Button DangerButton(string text, Action? cb = null)
    {
        var btn = new Button { Text = text };
        btn.AddThemeFontSizeOverride("font_size", FONT_SM);
        btn.AddThemeStyleboxOverride("normal", FlatBox(BG_BTN_DANGER, 4, 8, 4));
        btn.AddThemeStyleboxOverride("hover", FlatBox(BG_BTN_DANGER_H, 4, 8, 4));
        btn.AddThemeColorOverride("font_color", Colors.White);
        if (cb != null) btn.Pressed += cb;
        return btn;
    }

    /// <summary>
    /// Creates a tool toggle button: 36x36 square, vector icon only, tooltip for label.
    /// Replaces the old stacked emoji+text layout with a clean programmatic vector icon.
    /// </summary>
    public static Button ToolToggle(string icon, string label, string tooltip)
    {
        var btn = new Button
        {
            TooltipText = tooltip,
            ToggleMode = true,
            CustomMinimumSize = new Vector2(36, 36),
            Text = "",
            ClipText = true,
        };
        btn.AddThemeStyleboxOverride("normal",  FlatBox(BG_TOOL_NORMAL, 6, 0, 0));
        btn.AddThemeStyleboxOverride("hover",   FlatBox(BG_TOOL_HOVER, 6, 0, 0, BORDER, 1));
        btn.AddThemeStyleboxOverride("pressed", FlatBox(BG_TOOL_ACTIVE, 6, 0, 0, ACCENT_DIM, 1));

        var iconCanvas = new ToolIconCanvas(icon);
        iconCanvas.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        iconCanvas.MouseFilter = Control.MouseFilterEnum.Ignore;
        btn.AddChild(iconCanvas);

        return btn;
    }

    /// <summary>
    /// Creates a compact 36x36 icon-only tool toggle button (vector icon, no text).
    /// </summary>
    public static Button ToolToggleCompact(string icon, string tooltip)
    {
        var btn = new Button
        {
            TooltipText = tooltip,
            ToggleMode = true,
            CustomMinimumSize = new Vector2(36, 36),
            Text = "",
            ClipText = true,
        };
        btn.AddThemeStyleboxOverride("normal",  FlatBox(BG_TOOL_NORMAL, 6, 0, 0));
        btn.AddThemeStyleboxOverride("hover",   FlatBox(BG_TOOL_HOVER, 6, 0, 0, BORDER, 1));
        btn.AddThemeStyleboxOverride("pressed", FlatBox(BG_TOOL_ACTIVE, 6, 0, 0, ACCENT_DIM, 1));

        var iconCanvas = new ToolIconCanvas(icon);
        iconCanvas.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        iconCanvas.MouseFilter = Control.MouseFilterEnum.Ignore;
        btn.AddChild(iconCanvas);

        return btn;
    }

    /// <summary>
    /// Creates a compact action button (36x36, vector icon, non-toggle) for file ops / undo / redo.
    /// </summary>
    public static Button ActionButtonCompact(string icon, string tooltip, Action? cb = null)
    {
        var btn = new Button
        {
            TooltipText = tooltip,
            ToggleMode = false,
            CustomMinimumSize = new Vector2(36, 36),
            Text = "",
            ClipText = true,
        };
        btn.AddThemeStyleboxOverride("normal",  FlatBox(BG_TOOL_NORMAL, 6, 0, 0));
        btn.AddThemeStyleboxOverride("hover",   FlatBox(BG_TOOL_HOVER, 6, 0, 0, BORDER, 1));
        btn.AddThemeStyleboxOverride("pressed", FlatBox(BG_TOOL_ACTIVE, 6, 0, 0));

        var iconCanvas = new ToolIconCanvas(icon);
        iconCanvas.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        iconCanvas.MouseFilter = Control.MouseFilterEnum.Ignore;
        btn.AddChild(iconCanvas);

        if (cb != null) btn.Pressed += cb;
        return btn;
    }

    /// <summary>
    /// Creates a subtle vertical separator line for toolbar groups (1px, muted).
    /// </summary>
    public static Control ToolBarGroupSeparator()
    {
        var container = new Control { CustomMinimumSize = new Vector2(9, 36) };
        var line = new ColorRect
        {
            Color = BORDER_SUBTLE,
            CustomMinimumSize = new Vector2(1, 20),
        };
        line.SetAnchorsPreset(Control.LayoutPreset.Center);
        line.Size = new Vector2(1, 20);
        line.Position = new Vector2(4f, 8);
        container.AddChild(line);
        return container;
    }

    /// <summary>
    /// Creates a subtle group background panel for a set of toolbar buttons.
    /// </summary>
    public static PanelContainer ToolBarGroup()
    {
        var panel = new PanelContainer();
        var bg = new Color(BG_PANEL.R + 0.015f, BG_PANEL.G + 0.015f, BG_PANEL.B + 0.015f);
        panel.AddThemeStyleboxOverride("panel", FlatBox(bg, 5, 2, 2));
        return panel;
    }

    /// <summary>
    /// Compact layer tab for toolbar — pill-shaped, color-coded.
    /// Active: filled pill with layer color. Inactive: subtle outline pill.
    /// </summary>
    public static Button LayerTabCompact(int layerNum, Action? cb = null)
    {
        var color = layerNum >= 1 && layerNum <= 4 ? LAYER_COLORS[layerNum] : TEXT_PRIMARY;
        string label = layerNum switch
        {
            1 => "L1",
            2 => "L2",
            3 => "L3",
            4 => "L4",
            _ => $"L{layerNum}"
        };
        string fullName = layerNum switch
        {
            1 => "Terreno",
            2 => "Mascara",
            3 => "Objetos",
            4 => "Techos",
            _ => ""
        };
        var btn = new Button
        {
            Text = label,
            ToggleMode = true,
            CustomMinimumSize = new Vector2(44, 28),
            TooltipText = $"Capa {layerNum} ({fullName}) — tecla {layerNum}",
        };
        btn.AddThemeFontSizeOverride("font_size", FONT_SM);

        // Normal: pill with subtle colored border
        var normal = FlatBox(BG_TOOL_NORMAL, 14, 6, 2, color with { A = 0.30f }, 1);
        btn.AddThemeStyleboxOverride("normal", normal);
        btn.AddThemeColorOverride("font_color", color with { A = 0.60f });

        // Hover
        var hover = FlatBox(BG_TOOL_HOVER, 14, 6, 2, color with { A = 0.50f }, 1);
        btn.AddThemeStyleboxOverride("hover", hover);
        btn.AddThemeColorOverride("font_hover_color", color);

        // Pressed/Active: filled pill with layer color
        var darkColor = color * 0.40f;
        darkColor.A = 1.0f;
        var pressed = FlatBox(darkColor, 14, 6, 2, color with { A = 0.70f }, 1);
        btn.AddThemeStyleboxOverride("pressed", pressed);
        btn.AddThemeColorOverride("font_pressed_color", Colors.White);

        if (cb != null) btn.Pressed += cb;
        return btn;
    }

    /// <summary>
    /// Navigation button for map nav bar — compact pill style.
    /// </summary>
    public static void StyleNavButtonCompact(Button btn, bool isCurrent, bool exists)
    {
        if (isCurrent)
        {
            btn.AddThemeStyleboxOverride("normal", FlatBox(ACCENT, 12, 2, 1));
            btn.AddThemeStyleboxOverride("hover", FlatBox(BG_BTN_PRIMARY_H, 12, 2, 1));
            btn.AddThemeColorOverride("font_color", Colors.White);
            btn.Modulate = Colors.White;
        }
        else if (exists)
        {
            btn.AddThemeStyleboxOverride("normal", FlatBox(BG_BTN_GHOST, 12, 2, 1));
            btn.AddThemeStyleboxOverride("hover", FlatBox(BG_BTN_GHOST_H, 12, 2, 1, BORDER_SUBTLE, 1));
            btn.AddThemeColorOverride("font_color", TEXT_PRIMARY);
            btn.Modulate = Colors.White;
        }
        else
        {
            btn.AddThemeStyleboxOverride("normal", FlatBox(BG_BTN_GHOST, 12, 2, 1));
            btn.AddThemeStyleboxOverride("hover", FlatBox(BG_BTN_GHOST_H, 12, 2, 1));
            btn.AddThemeColorOverride("font_color", TEXT_MUTED);
            btn.Modulate = new Color(0.6f, 0.6f, 0.6f);
        }
    }

    /// <summary>
    /// Status bar pill -- small label with colored background.
    /// </summary>
    public static PanelContainer StatusPill(Label label, Color pillBg)
    {
        var panel = new PanelContainer();
        panel.AddThemeStyleboxOverride("panel", FlatBox(pillBg, 8, 6, 1));
        panel.AddChild(label);
        return panel;
    }

    /// <summary>
    /// Layer tab button -- pill-shaped, color-coded.
    /// Active: filled pill with layer color. Inactive: subtle outline pill.
    /// </summary>
    public static Button LayerTab(int layerNum, Action? cb = null)
    {
        var color = layerNum >= 1 && layerNum <= 4 ? LAYER_COLORS[layerNum] : TEXT_PRIMARY;
        string shortName = layerNum switch
        {
            1 => "L1",
            2 => "L2",
            3 => "L3",
            4 => "L4",
            _ => $"L{layerNum}"
        };
        string fullName = layerNum switch
        {
            1 => "Terreno",
            2 => "Mascara",
            3 => "Objetos",
            4 => "Techos",
            _ => ""
        };

        var btn = new Button
        {
            Text = shortName,
            ToggleMode = true,
            CustomMinimumSize = new Vector2(52, 28),
            TooltipText = $"Capa {layerNum} ({fullName}) — tecla {layerNum}",
        };
        btn.AddThemeFontSizeOverride("font_size", FONT_SM);

        // Normal: pill with subtle colored border
        var normal = FlatBox(BG_TOOL_NORMAL, 14, 10, 3, color with { A = 0.30f }, 1);
        btn.AddThemeStyleboxOverride("normal", normal);
        btn.AddThemeColorOverride("font_color", color with { A = 0.65f });

        // Hover: brighter pill
        var hover = FlatBox(BG_TOOL_HOVER, 14, 10, 3, color with { A = 0.50f }, 1);
        btn.AddThemeStyleboxOverride("hover", hover);
        btn.AddThemeColorOverride("font_hover_color", color);

        // Pressed/Active: filled pill with layer color
        var fillColor = color * 0.40f;
        fillColor.A = 1.0f;
        var pressed = FlatBox(fillColor, 14, 10, 3, color with { A = 0.70f }, 1);
        btn.AddThemeStyleboxOverride("pressed", pressed);
        btn.AddThemeColorOverride("font_pressed_color", Colors.White);

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
        sep.AddThemeConstantOverride("separation", 4);
        sep.AddThemeStyleboxOverride("separator",
            FlatBox(BORDER_SUBTLE, 0, 0, 0));
        return sep;
    }

    public static VSeparator MakeVSeparator()
    {
        var sep = new VSeparator();
        sep.AddThemeConstantOverride("separation", 8);
        sep.AddThemeStyleboxOverride("separator",
            FlatBox(BORDER_SUBTLE, 0, 0, 0));
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
            btn.AddThemeStyleboxOverride("normal", FlatBox(BG_BTN_SUCCESS, 4, 4, 2));
            btn.AddThemeStyleboxOverride("hover", FlatBox(BG_BTN_SUCCESS_H, 4, 4, 2));
            btn.AddThemeColorOverride("font_color", Colors.White);
            btn.Modulate = Colors.White;
        }
        else if (exists)
        {
            btn.AddThemeStyleboxOverride("normal", FlatBox(BG_BTN_GHOST, 4, 4, 2));
            btn.AddThemeStyleboxOverride("hover", FlatBox(BG_BTN_GHOST_H, 4, 4, 2, BORDER_SUBTLE, 1));
            btn.AddThemeColorOverride("font_color", TEXT_PRIMARY);
            btn.Modulate = Colors.White;
        }
        else
        {
            btn.AddThemeStyleboxOverride("normal", FlatBox(BG_BTN_GHOST, 4, 4, 2));
            btn.AddThemeStyleboxOverride("hover", FlatBox(BG_BTN_GHOST_H, 4, 4, 2));
            btn.AddThemeColorOverride("font_color", TEXT_MUTED);
            btn.Modulate = new Color(0.6f, 0.6f, 0.6f);
        }
    }

    // ══════════════════════════════════════════════════════════════════
    // Programmatic Vector Icon Drawing
    // ══════════════════════════════════════════════════════════════════

    /// <summary>
    /// Draws a professional vector icon by name onto a CanvasItem.
    /// Icons are designed to resemble Photoshop/Figma toolbar icons.
    /// </summary>
    public static void DrawIcon(Control canvas, string iconName, Vector2 center, float size, Color color)
    {
        float s = size * 0.5f;
        float w = ICON_LINE_WIDTH;

        switch (iconName)
        {
            case "hand":     DrawIconHand(canvas, center, s, color, w); break;
            case "pencil":   DrawIconPencil(canvas, center, s, color, w); break;
            case "eraser":   DrawIconEraser(canvas, center, s, color, w); break;
            case "select":   DrawIconSelect(canvas, center, s, color, w); break;
            case "move":     DrawIconMove(canvas, center, s, color, w); break;
            case "fill":     DrawIconFill(canvas, center, s, color, w); break;
            case "pick":     DrawIconPick(canvas, center, s, color, w); break;
            case "eyedrop":  DrawIconEyedrop(canvas, center, s, color, w); break;
            case "block":    DrawIconBlock(canvas, center, s, color, w); break;
            case "light":    DrawIconLight(canvas, center, s, color, w); break;
            case "exit":     DrawIconExit(canvas, center, s, color, w); break;
            case "npc":      DrawIconNpc(canvas, center, s, color, w); break;
            case "object":   DrawIconObject(canvas, center, s, color, w); break;
            case "trigger":  DrawIconTrigger(canvas, center, s, color, w); break;
            case "save":     DrawIconSave(canvas, center, s, color, w); break;
            case "undo":     DrawIconUndo(canvas, center, s, color, w); break;
            case "redo":     DrawIconRedo(canvas, center, s, color, w); break;
            case "folder":   DrawIconFolder(canvas, center, s, color, w); break;
            case "file_new": DrawIconFileNew(canvas, center, s, color, w); break;
        }
    }

    // --- Hand: open palm with five fingers ---
    private static void DrawIconHand(Control c, Vector2 p, float s, Color col, float w)
    {
        float palmW = s * 0.58f;
        float palmH = s * 0.48f;
        float palmY = p.Y + s * 0.10f;

        // Palm (rounded rect outline)
        c.DrawLine(new Vector2(p.X - palmW, palmY - palmH * 0.2f),
                   new Vector2(p.X - palmW, palmY + palmH), col, w);
        c.DrawLine(new Vector2(p.X - palmW, palmY + palmH),
                   new Vector2(p.X + palmW, palmY + palmH), col, w);
        c.DrawLine(new Vector2(p.X + palmW, palmY + palmH),
                   new Vector2(p.X + palmW, palmY - palmH * 0.2f), col, w);

        // Five fingers as lines rising from palm top
        float[] xOff = { -0.48f, -0.24f, 0f, 0.24f, 0.48f };
        float[] heights = { 0.48f, 0.70f, 0.78f, 0.66f, 0.40f };
        for (int i = 0; i < 5; i++)
        {
            float fx = p.X + xOff[i] * s;
            float baseY = palmY - palmH * 0.2f;
            float topY = baseY - heights[i] * s;
            c.DrawLine(new Vector2(fx, baseY), new Vector2(fx, topY), col, w);
            c.DrawCircle(new Vector2(fx, topY), w * 0.55f, col);
        }
        // Connect finger bases
        c.DrawLine(new Vector2(p.X - 0.48f * s, palmY - palmH * 0.2f),
                   new Vector2(p.X + 0.48f * s, palmY - palmH * 0.2f), col, w);
    }

    // --- Pencil: angled pencil with sharp tip ---
    private static void DrawIconPencil(Control c, Vector2 p, float s, Color col, float w)
    {
        var tip = p + new Vector2(-s * 0.58f, s * 0.58f);
        var bodyA = p + new Vector2(-s * 0.28f, s * 0.28f);
        var bodyB = p + new Vector2(s * 0.38f, -s * 0.38f);
        var top   = p + new Vector2(s * 0.52f, -s * 0.52f);

        float bw = s * 0.16f;
        var perp = new Vector2(-0.707f, -0.707f) * bw;

        // Body sides
        c.DrawLine(bodyA + perp, bodyB + perp, col, w);
        c.DrawLine(bodyA - perp, bodyB - perp, col, w);
        // Body top
        c.DrawLine(bodyB + perp, bodyB - perp, col, w);

        // Tip triangle
        c.DrawLine(bodyA + perp, tip, col, w);
        c.DrawLine(bodyA - perp, tip, col, w);
        c.DrawLine(bodyA + perp, bodyA - perp, col, w);

        // Eraser cap
        var capPerp = perp * 0.85f;
        c.DrawLine(bodyB + perp, top + capPerp, col, w);
        c.DrawLine(bodyB - perp, top - capPerp, col, w);
        c.DrawLine(top + capPerp, top - capPerp, col, w);

        // Tip point
        c.DrawCircle(tip, w * 0.7f, col);
    }

    // --- Eraser: tilted rectangular eraser ---
    private static void DrawIconEraser(Control c, Vector2 p, float s, Color col, float w)
    {
        float angle = 0.38f;
        float cos = MathF.Cos(angle), sin = MathF.Sin(angle);
        float hw = s * 0.68f, hh = s * 0.32f;

        Vector2 Rot(float x, float y) => p + new Vector2(x * cos - y * sin, x * sin + y * cos);

        var tl = Rot(-hw, -hh);
        var tr = Rot(hw, -hh);
        var br = Rot(hw, hh);
        var bl = Rot(-hw, hh);

        c.DrawLine(tl, tr, col, w);
        c.DrawLine(tr, br, col, w);
        c.DrawLine(br, bl, col, w);
        c.DrawLine(bl, tl, col, w);

        // Divider near eraser end
        var d1 = Rot(-hw * 0.38f, -hh);
        var d2 = Rot(-hw * 0.38f, hh);
        c.DrawLine(d1, d2, col, w);

        // Subtle wipe marks below
        for (int i = -1; i <= 1; i++)
        {
            var m1 = Rot(-hw + hw * 0.12f, hh + s * 0.10f + i * s * 0.07f);
            var m2 = Rot(hw * 0.15f, hh + s * 0.10f + i * s * 0.07f);
            c.DrawLine(m1, m2, col with { A = col.A * 0.35f }, w * 0.5f);
        }
    }

    // --- Select: dashed rectangle with corner grips ---
    private static void DrawIconSelect(Control c, Vector2 p, float s, Color col, float w)
    {
        float hs = s * 0.62f;
        var tl = p + new Vector2(-hs, -hs);
        var tr = p + new Vector2(hs, -hs);
        var br = p + new Vector2(hs, hs);
        var bl = p + new Vector2(-hs, hs);

        DrawDashedLine(c, tl, tr, col, w, 4);
        DrawDashedLine(c, tr, br, col, w, 4);
        DrawDashedLine(c, br, bl, col, w, 4);
        DrawDashedLine(c, bl, tl, col, w, 4);

        // Corner grips
        float gSz = w * 1.0f;
        c.DrawCircle(tl, gSz, col);
        c.DrawCircle(tr, gSz, col);
        c.DrawCircle(br, gSz, col);
        c.DrawCircle(bl, gSz, col);
    }

    // --- Move: 4-directional arrow cross ---
    private static void DrawIconMove(Control c, Vector2 p, float s, Color col, float w)
    {
        float len = s * 0.62f;
        float arr = s * 0.20f;

        c.DrawLine(p + new Vector2(0, -len), p + new Vector2(0, len), col, w);
        c.DrawLine(p + new Vector2(-len, 0), p + new Vector2(len, 0), col, w);

        // Up arrow
        c.DrawLine(p + new Vector2(0, -len), p + new Vector2(-arr, -len + arr), col, w);
        c.DrawLine(p + new Vector2(0, -len), p + new Vector2(arr, -len + arr), col, w);
        // Down
        c.DrawLine(p + new Vector2(0, len), p + new Vector2(-arr, len - arr), col, w);
        c.DrawLine(p + new Vector2(0, len), p + new Vector2(arr, len - arr), col, w);
        // Left
        c.DrawLine(p + new Vector2(-len, 0), p + new Vector2(-len + arr, -arr), col, w);
        c.DrawLine(p + new Vector2(-len, 0), p + new Vector2(-len + arr, arr), col, w);
        // Right
        c.DrawLine(p + new Vector2(len, 0), p + new Vector2(len - arr, -arr), col, w);
        c.DrawLine(p + new Vector2(len, 0), p + new Vector2(len - arr, arr), col, w);
    }

    // --- Fill: paint bucket with drip ---
    private static void DrawIconFill(Control c, Vector2 p, float s, Color col, float w)
    {
        float bTop = s * 0.32f, bBot = s * 0.46f;
        float bH = s * 0.52f;
        var tl = p + new Vector2(-bTop, -bH * 0.25f);
        var tr = p + new Vector2(bTop, -bH * 0.25f);
        var br = p + new Vector2(bBot, bH * 0.75f);
        var bl = p + new Vector2(-bBot, bH * 0.75f);

        c.DrawLine(tl, tr, col, w);
        c.DrawLine(tr, br, col, w);
        c.DrawLine(br, bl, col, w);
        c.DrawLine(bl, tl, col, w);

        // Handle arc
        var hS = p + new Vector2(-bTop * 0.35f, -bH * 0.25f);
        var hT = p + new Vector2(0, -bH * 0.78f);
        var hE = p + new Vector2(bTop * 0.35f, -bH * 0.25f);
        c.DrawLine(hS, hT, col, w);
        c.DrawLine(hT, hE, col, w);

        // Paint drip
        var drip = p + new Vector2(s * 0.52f, s * 0.22f);
        c.DrawCircle(drip, s * 0.12f, col);
        c.DrawLine(drip + new Vector2(-s * 0.08f, -s * 0.08f),
                   drip + new Vector2(0, -s * 0.22f), col, w);
        c.DrawLine(drip + new Vector2(s * 0.08f, -s * 0.08f),
                   drip + new Vector2(0, -s * 0.22f), col, w);
    }

    // --- Pick: pointing hand (index finger down) ---
    private static void DrawIconPick(Control c, Vector2 p, float s, Color col, float w)
    {
        // Index finger
        var fTop = p + new Vector2(0, -s * 0.72f);
        var fBot = p + new Vector2(0, s * 0.12f);
        c.DrawLine(fTop, fBot, col, w * 1.2f);
        c.DrawCircle(fBot, w * 0.7f, col);

        // Curled fingers
        for (int i = 0; i < 3; i++)
        {
            float yBase = s * 0.18f + i * s * 0.16f;
            var a = p + new Vector2(s * 0.06f, yBase - s * 0.06f);
            var m = p + new Vector2(s * 0.32f, yBase);
            var b = p + new Vector2(s * 0.06f, yBase + s * 0.06f);
            c.DrawLine(a, m, col, w * 0.85f);
            c.DrawLine(m, b, col, w * 0.85f);
        }

        // Thumb
        c.DrawLine(p + new Vector2(-s * 0.04f, s * 0.16f),
                   p + new Vector2(-s * 0.35f, s * 0.32f), col, w);
    }

    // --- Eyedrop: eyedropper pipette ---
    private static void DrawIconEyedrop(Control c, Vector2 p, float s, Color col, float w)
    {
        var tip = p + new Vector2(-s * 0.56f, s * 0.56f);
        var bodyBot = p + new Vector2(-s * 0.18f, s * 0.18f);
        var bodyTop = p + new Vector2(s * 0.22f, -s * 0.22f);
        var top = p + new Vector2(s * 0.42f, -s * 0.42f);

        float bw = s * 0.13f;
        var perp = new Vector2(-0.707f, -0.707f) * bw;

        // Body
        c.DrawLine(bodyBot + perp, bodyTop + perp, col, w);
        c.DrawLine(bodyBot - perp, bodyTop - perp, col, w);

        // Tip
        c.DrawLine(bodyBot + perp, tip, col, w);
        c.DrawLine(bodyBot - perp, tip, col, w);
        c.DrawCircle(tip, w * 0.6f, col);

        // Bulb
        var bulbPerp = perp * 1.5f;
        c.DrawLine(bodyTop + perp, top + bulbPerp, col, w);
        c.DrawLine(bodyTop - perp, top - bulbPerp, col, w);
        c.DrawLine(top + bulbPerp, top - bulbPerp, col, w);

        // Fill indicator
        c.DrawCircle((bodyTop + top) * 0.5f, s * 0.06f, col);
    }

    // --- Block: prohibition sign (circle + diagonal) ---
    private static void DrawIconBlock(Control c, Vector2 p, float s, Color col, float w)
    {
        float r = s * 0.60f;
        DrawCircleOutline(c, p, r, col, w, 24);
        float d = r * 0.707f;
        c.DrawLine(p + new Vector2(-d, -d), p + new Vector2(d, d), col, w * 1.15f);
    }

    // --- Light: sun with radiating rays ---
    private static void DrawIconLight(Control c, Vector2 p, float s, Color col, float w)
    {
        float innerR = s * 0.26f;
        float outerR = s * 0.58f;

        DrawCircleOutline(c, p, innerR, col, w, 16);

        for (int i = 0; i < 8; i++)
        {
            float angle = i * MathF.Tau / 8;
            var inner = p + new Vector2(MathF.Cos(angle) * (innerR + s * 0.06f),
                                        MathF.Sin(angle) * (innerR + s * 0.06f));
            var outer = p + new Vector2(MathF.Cos(angle) * outerR,
                                        MathF.Sin(angle) * outerR);
            c.DrawLine(inner, outer, col, w * 0.85f);
        }
    }

    // --- Exit: door frame with outward arrow ---
    private static void DrawIconExit(Control c, Vector2 p, float s, Color col, float w)
    {
        float dw = s * 0.38f, dh = s * 0.66f;
        float dx = -s * 0.12f;

        // Door frame
        c.DrawLine(p + new Vector2(dx - dw, -dh), p + new Vector2(dx + dw, -dh), col, w);
        c.DrawLine(p + new Vector2(dx + dw, -dh), p + new Vector2(dx + dw, dh), col, w);
        c.DrawLine(p + new Vector2(dx + dw, dh), p + new Vector2(dx - dw, dh), col, w);
        c.DrawLine(p + new Vector2(dx - dw, dh), p + new Vector2(dx - dw, -dh), col, w);

        // Knob
        c.DrawCircle(p + new Vector2(dx + dw * 0.45f, 0), s * 0.055f, col);

        // Arrow out
        float ax = p.X + s * 0.18f;
        float aLen = s * 0.48f;
        float aHead = s * 0.16f;
        c.DrawLine(new Vector2(ax, p.Y), new Vector2(ax + aLen, p.Y), col, w);
        c.DrawLine(new Vector2(ax + aLen, p.Y),
                   new Vector2(ax + aLen - aHead, p.Y - aHead), col, w);
        c.DrawLine(new Vector2(ax + aLen, p.Y),
                   new Vector2(ax + aLen - aHead, p.Y + aHead), col, w);
    }

    // --- NPC: person silhouette (head + torso + limbs) ---
    private static void DrawIconNpc(Control c, Vector2 p, float s, Color col, float w)
    {
        float headR = s * 0.20f;
        var headC = p + new Vector2(0, -s * 0.38f);
        DrawCircleOutline(c, headC, headR, col, w, 14);

        var neck = p + new Vector2(0, -s * 0.18f);
        var waist = p + new Vector2(0, s * 0.22f);
        c.DrawLine(neck, waist, col, w);

        // Arms
        c.DrawLine(neck + new Vector2(0, s * 0.06f),
                   p + new Vector2(-s * 0.36f, s * 0.10f), col, w);
        c.DrawLine(neck + new Vector2(0, s * 0.06f),
                   p + new Vector2(s * 0.36f, s * 0.10f), col, w);

        // Legs
        c.DrawLine(waist, p + new Vector2(-s * 0.26f, s * 0.65f), col, w);
        c.DrawLine(waist, p + new Vector2(s * 0.26f, s * 0.65f), col, w);
    }

    // --- Object: isometric cube ---
    private static void DrawIconObject(Control c, Vector2 p, float s, Color col, float w)
    {
        float hs = s * 0.48f;
        float iso = s * 0.28f;

        var ftl = p + new Vector2(-hs, -hs + iso);
        var ftr = p + new Vector2(hs, -hs + iso);
        var fbr = p + new Vector2(hs, hs);
        var fbl = p + new Vector2(-hs, hs);

        // Front face
        c.DrawLine(ftl, ftr, col, w);
        c.DrawLine(ftr, fbr, col, w);
        c.DrawLine(fbr, fbl, col, w);
        c.DrawLine(fbl, ftl, col, w);

        // Top face
        var ttl = ftl + new Vector2(iso * 0.55f, -iso);
        var ttr = ftr + new Vector2(iso * 0.55f, -iso);
        c.DrawLine(ftl, ttl, col, w);
        c.DrawLine(ftr, ttr, col, w);
        c.DrawLine(ttl, ttr, col, w);

        // Right side
        var rbr = fbr + new Vector2(iso * 0.55f, -iso);
        c.DrawLine(ttr, rbr, col, w);
        c.DrawLine(rbr, fbr, col, w);
    }

    // --- Trigger: lightning bolt ---
    private static void DrawIconTrigger(Control c, Vector2 p, float s, Color col, float w)
    {
        var pts = new Vector2[]
        {
            p + new Vector2(s * 0.10f, -s * 0.72f),
            p + new Vector2(-s * 0.14f, -s * 0.08f),
            p + new Vector2(s * 0.14f, -s * 0.04f),
            p + new Vector2(-s * 0.10f, s * 0.72f),
        };

        for (int i = 0; i < pts.Length - 1; i++)
            c.DrawLine(pts[i], pts[i + 1], col, w * 1.15f);

        c.DrawCircle(pts[0], w * 0.55f, col);
        c.DrawCircle(pts[^1], w * 0.55f, col);
    }

    // --- Save: floppy disk ---
    private static void DrawIconSave(Control c, Vector2 p, float s, Color col, float w)
    {
        float hs = s * 0.58f;
        // Outer rect with top-right corner cut
        var tl = p + new Vector2(-hs, -hs);
        var tr = p + new Vector2(hs - hs * 0.3f, -hs);
        var cut = p + new Vector2(hs, -hs + hs * 0.3f);
        var br = p + new Vector2(hs, hs);
        var bl = p + new Vector2(-hs, hs);
        c.DrawLine(tl, tr, col, w);
        c.DrawLine(tr, cut, col, w);
        c.DrawLine(cut, br, col, w);
        c.DrawLine(br, bl, col, w);
        c.DrawLine(bl, tl, col, w);

        // Label window (top)
        float lw = hs * 0.55f;
        c.DrawLine(p + new Vector2(-lw, -hs), p + new Vector2(-lw, -hs * 0.35f), col, w * 0.8f);
        c.DrawLine(p + new Vector2(-lw, -hs * 0.35f), p + new Vector2(lw, -hs * 0.35f), col, w * 0.8f);
        c.DrawLine(p + new Vector2(lw, -hs * 0.35f), p + new Vector2(lw, -hs), col, w * 0.8f);

        // Disk hub (bottom center rect)
        float dw = hs * 0.45f, dh = hs * 0.35f;
        c.DrawLine(p + new Vector2(-dw, hs * 0.20f), p + new Vector2(dw, hs * 0.20f), col, w * 0.8f);
        c.DrawLine(p + new Vector2(dw, hs * 0.20f), p + new Vector2(dw, hs), col, w * 0.8f);
        c.DrawLine(p + new Vector2(-dw, hs * 0.20f), p + new Vector2(-dw, hs), col, w * 0.8f);
    }

    // --- Undo: circular counterclockwise arrow inside ring ---
    private static void DrawIconUndo(Control c, Vector2 p, float s, Color col, float w)
    {
        float outerR = s * 0.82f;
        float arrowR = s * 0.46f;

        // Outer ring
        int circSegs = 24;
        for (int i = 0; i < circSegs; i++)
        {
            float a1 = i * MathF.Tau / circSegs;
            float a2 = (i + 1) * MathF.Tau / circSegs;
            c.DrawLine(
                p + new Vector2(MathF.Cos(a1) * outerR, MathF.Sin(a1) * outerR),
                p + new Vector2(MathF.Cos(a2) * outerR, MathF.Sin(a2) * outerR),
                col, w);
        }

        // Inner arrow arc (~280° counterclockwise, gap at top-left)
        float startAngle = -MathF.PI * 0.6f;
        float sweep = MathF.PI * 1.55f;
        int arcSegs = 16;
        for (int i = 0; i < arcSegs; i++)
        {
            float a1 = startAngle + i * sweep / arcSegs;
            float a2 = startAngle + (i + 1) * sweep / arcSegs;
            c.DrawLine(
                p + new Vector2(MathF.Cos(a1) * arrowR, MathF.Sin(a1) * arrowR),
                p + new Vector2(MathF.Cos(a2) * arrowR, MathF.Sin(a2) * arrowR),
                col, w * 1.6f);
        }

        // Arrowhead at arc start (points counterclockwise)
        var tip = p + new Vector2(MathF.Cos(startAngle) * arrowR, MathF.Sin(startAngle) * arrowR);
        float ah = s * 0.26f;
        float tang = startAngle - MathF.PI * 0.5f;
        c.DrawLine(tip, tip + new Vector2(MathF.Cos(tang + 0.45f), MathF.Sin(tang + 0.45f)) * ah, col, w * 1.6f);
        c.DrawLine(tip, tip + new Vector2(MathF.Cos(tang - 0.65f), MathF.Sin(tang - 0.65f)) * ah, col, w * 1.6f);
    }

    // --- Redo: circular clockwise arrow inside ring (mirror of undo) ---
    private static void DrawIconRedo(Control c, Vector2 p, float s, Color col, float w)
    {
        float outerR = s * 0.82f;
        float arrowR = s * 0.46f;

        // Outer ring
        int circSegs = 24;
        for (int i = 0; i < circSegs; i++)
        {
            float a1 = i * MathF.Tau / circSegs;
            float a2 = (i + 1) * MathF.Tau / circSegs;
            c.DrawLine(
                p + new Vector2(MathF.Cos(a1) * outerR, MathF.Sin(a1) * outerR),
                p + new Vector2(MathF.Cos(a2) * outerR, MathF.Sin(a2) * outerR),
                col, w);
        }

        // Inner arrow arc (~280° clockwise, gap at top-right)
        float startAngle = -MathF.PI * 0.4f;
        float sweep = -MathF.PI * 1.55f;
        int arcSegs = 16;
        for (int i = 0; i < arcSegs; i++)
        {
            float a1 = startAngle + i * sweep / arcSegs;
            float a2 = startAngle + (i + 1) * sweep / arcSegs;
            c.DrawLine(
                p + new Vector2(MathF.Cos(a1) * arrowR, MathF.Sin(a1) * arrowR),
                p + new Vector2(MathF.Cos(a2) * arrowR, MathF.Sin(a2) * arrowR),
                col, w * 1.6f);
        }

        // Arrowhead at arc start (points clockwise)
        var tip = p + new Vector2(MathF.Cos(startAngle) * arrowR, MathF.Sin(startAngle) * arrowR);
        float ah = s * 0.26f;
        float tang = startAngle + MathF.PI * 0.5f;
        c.DrawLine(tip, tip + new Vector2(MathF.Cos(tang - 0.45f), MathF.Sin(tang - 0.45f)) * ah, col, w * 1.6f);
        c.DrawLine(tip, tip + new Vector2(MathF.Cos(tang + 0.65f), MathF.Sin(tang + 0.65f)) * ah, col, w * 1.6f);
    }

    // --- Folder: simple folder shape (rectangle with tab on top-left) ---
    private static void DrawIconFolder(Control c, Vector2 p, float s, Color col, float w)
    {
        float hs = s * 0.58f;
        float tabW = hs * 0.45f;
        float tabH = hs * 0.25f;

        // Main body
        var tl = p + new Vector2(-hs, -hs * 0.4f);
        var tr = p + new Vector2(hs, -hs * 0.4f);
        var br = p + new Vector2(hs, hs);
        var bl = p + new Vector2(-hs, hs);

        // Tab on top-left
        var tabTL = tl + new Vector2(0, -tabH);
        var tabTR = tl + new Vector2(tabW, -tabH);
        var tabBR = tl + new Vector2(tabW + tabH * 0.5f, 0);

        c.DrawLine(tabTL, tabTR, col, w);
        c.DrawLine(tabTR, tabBR, col, w);
        c.DrawLine(tabTL, tl, col, w);

        // Body outline
        c.DrawLine(tl, tr, col, w);
        c.DrawLine(tr, br, col, w);
        c.DrawLine(br, bl, col, w);
        c.DrawLine(bl, tl, col, w);
    }

    // --- FileNew: document with + sign ---
    private static void DrawIconFileNew(Control c, Vector2 p, float s, Color col, float w)
    {
        float hs = s * 0.52f;
        float fold = hs * 0.35f;

        // Document outline with folded corner
        var tl = p + new Vector2(-hs, -hs);
        var tr = p + new Vector2(hs - fold, -hs);
        var foldPt = p + new Vector2(hs, -hs + fold);
        var br = p + new Vector2(hs, hs);
        var bl = p + new Vector2(-hs, hs);

        c.DrawLine(tl, tr, col, w);
        c.DrawLine(tr, foldPt, col, w);
        c.DrawLine(foldPt, br, col, w);
        c.DrawLine(br, bl, col, w);
        c.DrawLine(bl, tl, col, w);

        // Fold crease
        c.DrawLine(tr, tr + new Vector2(0, fold), col, w * 0.7f);
        c.DrawLine(tr + new Vector2(0, fold), foldPt, col, w * 0.7f);

        // Plus sign in center
        float ps = hs * 0.35f;
        var center = p + new Vector2(0, s * 0.1f);
        c.DrawLine(center + new Vector2(0, -ps), center + new Vector2(0, ps), col, w);
        c.DrawLine(center + new Vector2(-ps, 0), center + new Vector2(ps, 0), col, w);
    }

    // ── Drawing utilities ────────────────────────────────────────────

    private static void DrawDashedLine(Control c, Vector2 from, Vector2 to,
        Color col, float w, int dashes)
    {
        var dir = to - from;
        float totalLen = dir.Length();
        if (totalLen < 0.01f) return;
        dir /= totalLen;

        float dashLen = totalLen / (dashes * 2 - 1);
        for (int i = 0; i < dashes; i++)
        {
            float start = i * dashLen * 2;
            float end = start + dashLen;
            c.DrawLine(from + dir * start, from + dir * MathF.Min(end, totalLen), col, w);
        }
    }

    private static void DrawCircleOutline(Control c, Vector2 center, float radius,
        Color col, float w, int segments)
    {
        for (int i = 0; i < segments; i++)
        {
            float a1 = i * MathF.Tau / segments;
            float a2 = (i + 1) * MathF.Tau / segments;
            c.DrawLine(
                center + new Vector2(MathF.Cos(a1) * radius, MathF.Sin(a1) * radius),
                center + new Vector2(MathF.Cos(a2) * radius, MathF.Sin(a2) * radius),
                col, w);
        }
    }

    /// <summary>
    /// Maps unicode icon strings (used by callers) to internal vector icon names.
    /// </summary>
    internal static string ResolveIconName(string unicodeIcon)
    {
        return unicodeIcon switch
        {
            "\u270b" => "hand",       // hand emoji
            "\u270f" => "pencil",     // pencil
            "\u232b" => "eraser",     // erase symbol
            "\u25a1" => "select",     // white square
            "\u21c6" => "move",       // left right arrows
            "\u25a8" => "fill",       // square with fill
            "\u261d" => "pick",       // pointing up
            "\u25ce" => "eyedrop",    // bullseye
            "\u2298" => "block",      // circled minus
            "\u2600" => "light",      // sun
            "\u2197" => "exit",       // north east arrow
            "\u265f" => "npc",        // chess pawn
            "\u25c6" => "object",     // black diamond
            "\u26a1" => "trigger",    // lightning
            "\U0001F4BE" => "save",   // floppy disk 💾
            "\U0001F4C2" => "folder", // open folder 📂
            "\U0001F4C1" => "folder", // folder 📁
            "\u21B6" => "undo",       // undo arrow
            "\u21B7" => "redo",       // redo arrow
            _ => unicodeIcon.Length > 0 ? ResolveByContent(unicodeIcon) : "pencil",
        };
    }

    /// <summary>
    /// Fallback: try to match icon name from text content for action buttons.
    /// </summary>
    private static string ResolveByContent(string text)
    {
        string lower = text.ToLowerInvariant();
        // File operations
        if (lower.Contains("guardar") || lower.Contains("save")) return "save";
        if (lower.Contains("deshacer") || lower.Contains("undo")) return "undo";
        if (lower.Contains("rehacer") || lower.Contains("redo")) return "redo";
        if (lower.Contains("abrir") || lower.Contains("open")) return "folder";
        if (lower.Contains("nuevo") || lower.Contains("new")) return "file_new";
        // Tool names (Spanish)
        if (lower.Contains("pintar") || lower.Contains("paint")) return "pencil";
        if (lower.Contains("borrar") || lower.Contains("erase")) return "eraser";
        if (lower.Contains("seleccionar") || lower.Contains("select")) return "select";
        if (lower.Contains("mover") || lower.Contains("move")) return "move";
        if (lower.Contains("rellenar") || lower.Contains("fill")) return "fill";
        if (lower.Contains("cuentagotas") || lower.Contains("eyedrop")) return "eyedrop";
        if (lower.Contains("bloquear") || lower.Contains("block")) return "block";
        if (lower.Contains("luz") || lower.Contains("light")) return "light";
        if (lower.Contains("salida") || lower.Contains("exit")) return "exit";
        if (lower.Contains("trigger")) return "trigger";
        return "pencil"; // final fallback
    }

    /// <summary>Returns a color for zone type labels in the zone panel list.</summary>
    public static Color GetZoneBorderColor(string zoneType) => zoneType switch
    {
        "Safe" => new Color(0.2f, 0.8f, 0.2f),
        "PvP" or "CombatZone" => new Color(0.9f, 0.2f, 0.2f),
        "Indoor" => new Color(0.6f, 0.6f, 0.9f),
        "AntiBlock" => new Color(0.9f, 0.7f, 0.2f),
        _ => new Color(0.7f, 0.7f, 0.7f),
    };
}

// ══════════════════════════════════════════════════════════════════════
// ToolIconCanvas -- lightweight Control that draws a vector icon
// ══════════════════════════════════════════════════════════════════════

/// <summary>
/// A Control child that renders a vector tool icon via _Draw().
/// Added as a child of ToolToggle / ActionButtonCompact buttons.
/// </summary>
public partial class ToolIconCanvas : Control
{
    private readonly string _iconName;
    private bool _lastHovered;
    private bool _lastPressed;

    public ToolIconCanvas(string unicodeIcon)
    {
        _iconName = EditorTheme.ResolveIconName(unicodeIcon);
        MouseFilter = MouseFilterEnum.Ignore; // Don't absorb clicks — let parent Button handle them
        Godot.GD.Print($"[ToolIcon] '{unicodeIcon}' → '{_iconName}'");
    }

    public override void _Draw()
    {
        var center = Size * 0.5f;
        float iconSize = MathF.Min(Size.X, Size.Y) * 0.50f;

        // Color adapts to parent button state
        var color = EditorTheme.TEXT_SECONDARY;
        if (GetParent() is Button btn)
        {
            if (btn.ButtonPressed)
                color = Colors.White;
            else if (btn.IsHovered())
                color = EditorTheme.TEXT_PRIMARY;
        }

        EditorTheme.DrawIcon(this, _iconName, center, iconSize, color);
    }

    public override void _Process(double delta)
    {
        // Only redraw when state actually changes to avoid constant redraws
        if (GetParent() is Button btn)
        {
            bool hovered = btn.IsHovered();
            bool pressed = btn.ButtonPressed;
            if (hovered != _lastHovered || pressed != _lastPressed)
            {
                _lastHovered = hovered;
                _lastPressed = pressed;
                QueueRedraw();
            }
        }
    }
}
