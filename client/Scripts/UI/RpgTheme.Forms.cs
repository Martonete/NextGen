using Godot;
using System;

namespace ArgentumNextgen.UI;

public static partial class RpgTheme
{
    // =========================================================================
    // TAB BAR
    // =========================================================================

    /// <summary>
    /// Create a themed tab bar. Returns an HBoxContainer with styled tab buttons.
    /// Use GetMeta("buttons") to get the Button[] array, and call SetTabBarActive() to switch tabs.
    /// The onTabChanged callback receives the new tab index.
    /// </summary>
    public static HBoxContainer CreateTabBar(string[] tabNames, Action<int>? onTabChanged = null)
    {
        var bar = new HBoxContainer();
        bar.AddThemeConstantOverride("separation", 4);
        bar.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;

        var controls = new Control[tabNames.Length];
        for (int i = 0; i < tabNames.Length; i++)
        {
            int idx = i;
            var btn = new TextureButton();
            btn.MouseDefaultCursorShape = Control.CursorShape.PointingHand;
            btn.TextureNormal = GetTex("mid_button.png");
            btn.TextureHover = GetTex("mid_button_on.png");
            btn.TexturePressed = GetTex("mid_button_on.png");
            btn.StretchMode = TextureButton.StretchModeEnum.Scale;
            btn.IgnoreTextureSize = true;
            btn.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            btn.CustomMinimumSize = new Vector2(0, 28);

            var label = new Label();
            label.Text = tabNames[i];
            label.HorizontalAlignment = HorizontalAlignment.Center;
            label.VerticalAlignment = VerticalAlignment.Center;
            label.AddThemeFontSizeOverride("font_size", 12);
            label.AddThemeColorOverride("font_color", new Color(0.9f, 0.85f, 0.7f));
            label.AddThemeColorOverride("font_shadow_color", new Color(0, 0, 0, 0.8f));
            label.AddThemeConstantOverride("shadow_offset_x", 1);
            label.AddThemeConstantOverride("shadow_offset_y", 1);
            label.MouseFilter = Control.MouseFilterEnum.Ignore;
            btn.AddChild(label);
            FillParent(label);

            btn.Pressed += () =>
            {
                SetTabBarActive(bar, idx);
                onTabChanged?.Invoke(idx);
            };
            bar.AddChild(btn);
            controls[i] = btn;
        }

        bar.SetMeta("buttons", Variant.From(new Godot.Collections.Array(controls)));
        bar.SetMeta("active", 0);

        bar.Ready += () => SetTabBarActive(bar, 0);

        return bar;
    }

    /// <summary>
    /// Set the active tab in a tab bar created by CreateTabBar().
    /// Active tab: full brightness. Inactive: dimmed (same as Inventario/Hechizos tabs).
    /// </summary>
    public static void SetTabBarActive(HBoxContainer tabBar, int activeIndex)
    {
        tabBar.SetMeta("active", activeIndex);
        for (int i = 0; i < tabBar.GetChildCount(); i++)
        {
            if (tabBar.GetChild(i) is not TextureButton btn) continue;
            btn.Modulate = i == activeIndex ? Colors.White : new Color(0.6f, 0.55f, 0.5f);
        }
    }

    // =========================================================================
    // ITEM LIST
    // =========================================================================

    /// <summary>
    /// Create a themed ItemList with RPG-style scrollbar and colors.
    /// </summary>
    public static ItemList CreateRpgItemList(float minWidth = 200f, float minHeight = 150f,
                                              bool allowMultiSelect = false)
    {
        var list = new ItemList();
        list.CustomMinimumSize = new Vector2(minWidth, minHeight);
        list.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        list.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
        list.SelectMode = allowMultiSelect ? ItemList.SelectModeEnum.Multi : ItemList.SelectModeEnum.Single;
        list.AllowReselect = true;

        var panelStyle = new StyleBoxFlat();
        panelStyle.BgColor = new Color(0.08f, 0.07f, 0.06f, 0.9f);
        panelStyle.BorderColor = new Color(0.45f, 0.38f, 0.25f, 0.9f);
        panelStyle.SetBorderWidthAll(2);
        panelStyle.SetCornerRadiusAll(2);
        panelStyle.ContentMarginLeft = 4; panelStyle.ContentMarginRight = 4;
        panelStyle.ContentMarginTop = 4;  panelStyle.ContentMarginBottom = 4;
        list.AddThemeStyleboxOverride("panel", panelStyle);

        list.AddThemeFontSizeOverride("font_size", 12);
        list.AddThemeColorOverride("font_color", new Color(0.85f, 0.8f, 0.65f));
        list.AddThemeColorOverride("font_hovered_color", new Color(0.95f, 0.9f, 0.75f));
        list.AddThemeColorOverride("font_selected_color", new Color(1f, 0.95f, 0.8f));

        var selectedStyle = new StyleBoxFlat();
        selectedStyle.BgColor = new Color(0.25f, 0.20f, 0.12f, 0.9f);
        selectedStyle.BorderColor = new Color(0.6f, 0.5f, 0.3f, 0.8f);
        selectedStyle.SetBorderWidthAll(1);
        selectedStyle.SetCornerRadiusAll(1);
        list.AddThemeStyleboxOverride("selected", selectedStyle);
        list.AddThemeStyleboxOverride("selected_focus", (StyleBoxFlat)selectedStyle.Duplicate());

        var hoverStyle = new StyleBoxFlat();
        hoverStyle.BgColor = new Color(0.18f, 0.15f, 0.10f, 0.7f);
        hoverStyle.SetBorderWidthAll(0);
        list.AddThemeStyleboxOverride("hovered", hoverStyle);

        list.AddThemeStyleboxOverride("focus", new StyleBoxEmpty());

        list.Ready += () =>
        {
            var vbar = list.GetVScrollBar();
            if (vbar != null) ApplyScrollbarTheme(vbar);
        };

        return list;
    }

    // =========================================================================
    // TEXT EDIT (multiline)
    // =========================================================================

    /// <summary>
    /// Create a themed multiline TextEdit.
    /// </summary>
    public static TextEdit CreateRpgTextEdit(string placeholder = "", float minWidth = 200f,
                                              float minHeight = 100f, bool readOnly = false)
    {
        var edit = new TextEdit();
        edit.PlaceholderText = placeholder;
        edit.CustomMinimumSize = new Vector2(minWidth, minHeight);
        edit.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        edit.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
        edit.Editable = !readOnly;
        edit.WrapMode = TextEdit.LineWrappingMode.Boundary;

        var styleNormal = new StyleBoxFlat();
        styleNormal.BgColor = new Color(0.08f, 0.07f, 0.05f, 0.9f);
        styleNormal.BorderColor = new Color(0.45f, 0.38f, 0.25f, 0.9f);
        styleNormal.SetBorderWidthAll(2);
        styleNormal.SetCornerRadiusAll(2);
        styleNormal.ContentMarginLeft = 8;  styleNormal.ContentMarginRight = 8;
        styleNormal.ContentMarginTop = 6;   styleNormal.ContentMarginBottom = 6;
        edit.AddThemeStyleboxOverride("normal", styleNormal);

        var styleFocus = (StyleBoxFlat)styleNormal.Duplicate();
        styleFocus.BorderColor = new Color(0.65f, 0.55f, 0.35f, 1f);
        edit.AddThemeStyleboxOverride("focus", styleFocus);

        var styleReadOnly = (StyleBoxFlat)styleNormal.Duplicate();
        styleReadOnly.BgColor = new Color(0.06f, 0.05f, 0.04f, 0.85f);
        edit.AddThemeStyleboxOverride("read_only", styleReadOnly);

        edit.AddThemeFontSizeOverride("font_size", 12);
        edit.AddThemeColorOverride("font_color", new Color(0.85f, 0.8f, 0.65f));
        edit.AddThemeColorOverride("font_placeholder_color", new Color(0.5f, 0.47f, 0.4f));
        edit.AddThemeColorOverride("caret_color", new Color(0.9f, 0.85f, 0.7f));
        edit.AddThemeColorOverride("selection_color", new Color(0.4f, 0.35f, 0.2f, 0.5f));

        edit.Ready += () =>
        {
            var vbar = edit.GetVScrollBar();
            if (vbar != null) ApplyScrollbarTheme(vbar);
        };

        return edit;
    }

    // =========================================================================
    // TOOLTIP PANEL
    // =========================================================================

    /// <summary>
    /// Create a styled tooltip panel (PanelContainer with RPG colors, no scroll).
    /// </summary>
    public static PanelContainer CreateRpgTooltipPanel(float maxWidth = 200f)
    {
        var panel = new PanelContainer();
        panel.CustomMinimumSize = new Vector2(0, 0);
        panel.Visible = false;
        panel.ZIndex = 2;
        panel.MouseFilter = Control.MouseFilterEnum.Ignore;

        var style = new StyleBoxFlat();
        style.BgColor = new Color(0.06f, 0.05f, 0.04f, 0.95f);
        style.BorderColor = new Color(0.55f, 0.45f, 0.3f, 0.9f);
        style.SetBorderWidthAll(2);
        style.SetCornerRadiusAll(3);
        style.ContentMarginLeft = 8; style.ContentMarginRight = 8;
        style.ContentMarginTop = 6;  style.ContentMarginBottom = 6;
        panel.AddThemeStyleboxOverride("panel", style);

        var content = new VBoxContainer();
        content.AddThemeConstantOverride("separation", 4);
        content.MouseFilter = Control.MouseFilterEnum.Ignore;
        panel.AddChild(content);

        panel.SetMeta("content", content);
        return panel;
    }

    // =========================================================================
    // CONTEXT MENU
    // =========================================================================

    /// <summary>
    /// Create a styled context menu container.
    /// Add items via CreateRpgContextMenuItem().
    /// </summary>
    public static PanelContainer CreateRpgContextMenu(float minWidth = 140f)
    {
        var panel = new PanelContainer();
        panel.CustomMinimumSize = new Vector2(minWidth, 0);
        panel.Visible = false;
        panel.ZIndex = 5;

        var style = new StyleBoxFlat();
        style.BgColor = new Color(0.10f, 0.09f, 0.07f, 0.97f);
        style.BorderColor = new Color(0.55f, 0.45f, 0.3f, 0.95f);
        style.SetBorderWidthAll(2);
        style.SetCornerRadiusAll(3);
        style.ContentMarginLeft = 4; style.ContentMarginRight = 4;
        style.ContentMarginTop = 4;  style.ContentMarginBottom = 4;
        panel.AddThemeStyleboxOverride("panel", style);

        var vbox = new VBoxContainer();
        vbox.AddThemeConstantOverride("separation", 1);
        panel.AddChild(vbox);

        panel.SetMeta("content", vbox);
        return panel;
    }

    /// <summary>
    /// Create a styled context menu item (button).
    /// </summary>
    public static Button CreateRpgContextMenuItem(string text, Action? onPressed = null)
    {
        var btn = new Button();
        btn.Text = text;
        btn.Flat = true;
        btn.CustomMinimumSize = new Vector2(0, 26);
        btn.Alignment = HorizontalAlignment.Left;
        btn.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        btn.AddThemeFontSizeOverride("font_size", 12);
        btn.FocusMode = Control.FocusModeEnum.None;

        btn.AddThemeColorOverride("font_color", new Color(0.85f, 0.8f, 0.65f));
        btn.AddThemeColorOverride("font_hover_color", new Color(0.95f, 0.9f, 0.75f));
        btn.AddThemeColorOverride("font_pressed_color", new Color(1f, 0.95f, 0.8f));

        var hoverStyle = new StyleBoxFlat();
        hoverStyle.BgColor = new Color(0.22f, 0.18f, 0.12f, 0.9f);
        hoverStyle.SetCornerRadiusAll(2);
        hoverStyle.ContentMarginLeft = 8; hoverStyle.ContentMarginRight = 8;
        btn.AddThemeStyleboxOverride("hover", hoverStyle);

        var emptyStyle = new StyleBoxEmpty();
        emptyStyle.ContentMarginLeft = 8; emptyStyle.ContentMarginRight = 8;
        btn.AddThemeStyleboxOverride("normal", emptyStyle);
        btn.AddThemeStyleboxOverride("pressed", (StyleBoxFlat)hoverStyle.Duplicate());
        btn.AddThemeStyleboxOverride("focus", new StyleBoxEmpty());

        if (onPressed != null)
            btn.Pressed += onPressed;

        return btn;
    }

    // =========================================================================
    // CONFIRM DIALOG
    // =========================================================================

    /// <summary>
    /// Create a themed confirmation dialog with Ok/Cancel buttons.
    /// Returns a Control; use ShowRpgConfirmDialog() to display.
    /// onConfirm is called when Ok is pressed, onCancel when Cancel is pressed.
    /// </summary>
    public static Control CreateRpgConfirmDialog(string message, string title = "Confirmar",
                                                   Action? onConfirm = null, Action? onCancel = null,
                                                   string okText = "Aceptar", string cancelText = "Cancelar")
    {
        var overlay = new ColorRect();
        overlay.Color = new Color(0f, 0f, 0f, 0.55f);
        overlay.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        overlay.MouseFilter = Control.MouseFilterEnum.Stop;
        overlay.Visible = false;
        overlay.ZIndex = 10;

        var panel = new Control();
        panel.CustomMinimumSize = new Vector2(360, 0);
        panel.MouseFilter = Control.MouseFilterEnum.Stop;
        overlay.AddChild(panel);

        // Background
        var bg = new ColorRect();
        bg.Color = new Color(0.10f, 0.09f, 0.08f, 0.95f);
        bg.MouseFilter = Control.MouseFilterEnum.Ignore;
        panel.AddChild(bg);
        FillParent(bg);

        // Frame
        var frame = CreateNinePatch("info_window.png", new Vector4(16, 16, 16, 16));
        panel.AddChild(frame);
        FillParent(frame);

        // Title
        var titleBar = CreateNinePatch("dialoge_frame.png", new Vector4(20, 10, 20, 10));
        panel.AddChild(titleBar);
        titleBar.AnchorLeft = 0f; titleBar.AnchorRight = 1f;
        titleBar.AnchorTop = 0f;  titleBar.AnchorBottom = 0f;
        titleBar.OffsetLeft = 8;  titleBar.OffsetTop = 4;
        titleBar.OffsetRight = -8; titleBar.OffsetBottom = 50;

        var titleLabel = CreateTitleLabel(title, 16);
        titleBar.AddChild(titleLabel);
        FillParent(titleLabel);

        // Content
        var margin = new MarginContainer();
        margin.AddThemeConstantOverride("margin_top", 58);
        margin.AddThemeConstantOverride("margin_left", 30);
        margin.AddThemeConstantOverride("margin_right", 30);
        margin.AddThemeConstantOverride("margin_bottom", 20);
        margin.MouseFilter = Control.MouseFilterEnum.Ignore;
        panel.AddChild(margin);
        FillParent(margin);

        var vbox = CreateColumn(SpacingLg);
        margin.AddChild(vbox);

        var msgLabel = CreateInfoLabel(message, 13);
        msgLabel.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        msgLabel.HorizontalAlignment = HorizontalAlignment.Center;
        vbox.AddChild(msgLabel);

        vbox.AddChild(CreateSpacer(8));

        var btnRow = CreateRow(SpacingLg);
        btnRow.Alignment = BoxContainer.AlignmentMode.Center;
        vbox.AddChild(btnRow);

        var okBtn = CreateRpgButton(okText, false, 14);
        okBtn.CustomMinimumSize = new Vector2(110, 36);
        okBtn.Pressed += () =>
        {
            overlay.Visible = false;
            onConfirm?.Invoke();
        };
        btnRow.AddChild(okBtn);

        var cancelBtn = CreateRpgButton(cancelText, false, 14);
        cancelBtn.CustomMinimumSize = new Vector2(110, 36);
        cancelBtn.Pressed += () =>
        {
            overlay.Visible = false;
            onCancel?.Invoke();
        };
        btnRow.AddChild(cancelBtn);

        overlay.SetMeta("panel", panel);
        overlay.SetMeta("message_label", msgLabel);

        // Center the panel when shown
        overlay.Ready += () =>
        {
            panel.Size = new Vector2(360, 180);
            var vpSize = overlay.GetViewportRect().Size;
            panel.Position = (vpSize - panel.Size) / 2f;
        };

        return overlay;
    }

    /// <summary>Show a confirm dialog (created by CreateRpgConfirmDialog).</summary>
    public static void ShowRpgConfirmDialog(Control dialog, string? newMessage = null)
    {
        if (newMessage != null)
        {
            var lbl = dialog.GetMeta("message_label").As<Label>();
            if (lbl != null) lbl.Text = newMessage;
        }

        dialog.Visible = true;

        // Re-center panel
        var panel = dialog.GetMeta("panel").As<Control>();
        if (panel != null)
        {
            var vpSize = dialog.GetViewportRect().Size;
            panel.Position = (vpSize - panel.Size) / 2f;
        }
    }
}
