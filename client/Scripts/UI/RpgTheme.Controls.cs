using Godot;

namespace ArgentumNextgen.UI;

public static partial class RpgTheme
{
    // =========================================================================
    // LABELS
    // =========================================================================

    public static Label CreateTitleLabel(string text, int fontSize = 22)
    {
        var label = new Label();
        label.Text = text;
        label.HorizontalAlignment = HorizontalAlignment.Center;
        label.VerticalAlignment = VerticalAlignment.Center;
        label.AddThemeFontSizeOverride("font_size", fontSize);
        label.AddThemeColorOverride("font_color", new Color(0.95f, 0.9f, 0.75f));
        label.AddThemeColorOverride("font_shadow_color", new Color(0, 0, 0, 0.9f));
        label.AddThemeConstantOverride("shadow_offset_x", 2);
        label.AddThemeConstantOverride("shadow_offset_y", 2);
        return label;
    }

    public static Label CreateInfoLabel(string text, int fontSize = 14)
    {
        var label = new Label();
        label.Text = text;
        label.AddThemeFontSizeOverride("font_size", fontSize);
        label.AddThemeColorOverride("font_color", new Color(0.85f, 0.8f, 0.65f));
        label.AddThemeColorOverride("font_shadow_color", new Color(0, 0, 0, 0.6f));
        label.AddThemeConstantOverride("shadow_offset_x", 1);
        label.AddThemeConstantOverride("shadow_offset_y", 1);
        return label;
    }

    public static HBoxContainer CreateLabeledValue(string labelText, string valueText,
                                                    int labelSize = 13, int valueSize = 13)
    {
        var row = CreateRow();
        var label = CreateInfoLabel(labelText + ":", labelSize);
        label.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        row.AddChild(label);
        var value = CreateTitleLabel(valueText, valueSize);
        value.HorizontalAlignment = HorizontalAlignment.Right;
        row.AddChild(value);
        return row;
    }

    // =========================================================================
    // TEXTURES / ICONS
    // =========================================================================

    public static TextureRect CreateIcon(string textureName, Vector2? iconSize = null)
    {
        var texRect = new TextureRect();
        texRect.Texture = GetTex(textureName);
        texRect.ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize;
        texRect.StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered;
        texRect.MouseFilter = Control.MouseFilterEnum.Ignore;
        if (iconSize.HasValue)
            texRect.CustomMinimumSize = iconSize.Value;
        return texRect;
    }

    public static VBoxContainer CreateTexturePreview(string textureName, Vector2 previewSize,
                                                      string labelText = "")
    {
        var col = CreateColumn(2);
        col.Alignment = BoxContainer.AlignmentMode.Center;
        var texRect = new TextureRect();
        texRect.Texture = GetTex(textureName);
        texRect.ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize;
        texRect.StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered;
        texRect.CustomMinimumSize = previewSize;
        texRect.MouseFilter = Control.MouseFilterEnum.Ignore;
        var center = new CenterContainer();
        center.AddChild(texRect);
        col.AddChild(center);
        string displayName = !string.IsNullOrEmpty(labelText) ? labelText : textureName.GetFile().GetBaseName();
        var lbl = CreateInfoLabel(displayName, 8);
        lbl.HorizontalAlignment = HorizontalAlignment.Center;
        col.AddChild(lbl);
        return col;
    }

    public static NinePatchRect CreateNinePatch(string textureName, Vector4? margins = null,
        NinePatchRect.AxisStretchMode axisStretchH = NinePatchRect.AxisStretchMode.Stretch,
        NinePatchRect.AxisStretchMode axisStretchV = NinePatchRect.AxisStretchMode.Stretch)
    {
        var m = margins ?? new Vector4(16, 16, 16, 16);
        var np = new NinePatchRect();
        np.Texture = GetTex(textureName);
        np.PatchMarginLeft = (int)m.X;
        np.PatchMarginTop = (int)m.Y;
        np.PatchMarginRight = (int)m.Z;
        np.PatchMarginBottom = (int)m.W;
        np.AxisStretchHorizontal = axisStretchH;
        np.AxisStretchVertical = axisStretchV;
        np.MouseFilter = Control.MouseFilterEnum.Ignore;
        return np;
    }

    // =========================================================================
    // BUTTONS
    // =========================================================================

    public static TextureButton CreateRpgButton(string text, bool isLong = true, int fontSize = 18)
    {
        var btn = new TextureButton();
        btn.MouseDefaultCursorShape = Control.CursorShape.PointingHand;
        btn.FocusMode = Control.FocusModeEnum.None; // Never grab keyboard focus — prevents arrow keys from being captured by Godot focus navigation
        if (isLong)
        {
            btn.TextureNormal = GetTex("long_button.png");
            btn.TextureHover = GetTex("long_button_on.png");
            btn.TexturePressed = GetTex("long_button_on.png");
            btn.TextureDisabled = GetTex("long_button_off.png");
        }
        else
        {
            btn.TextureNormal = GetTex("mid_button.png");
            btn.TextureHover = GetTex("mid_button_on.png");
            btn.TexturePressed = GetTex("mid_button_on.png");
            btn.TextureDisabled = GetTex("mid_button_off.png");
        }
        btn.StretchMode = TextureButton.StretchModeEnum.Scale;
        btn.IgnoreTextureSize = true;

        var label = new Label();
        label.Text = text;
        label.HorizontalAlignment = HorizontalAlignment.Center;
        label.VerticalAlignment = VerticalAlignment.Center;
        label.ClipText = true;
        label.AddThemeFontSizeOverride("font_size", fontSize);
        label.AddThemeColorOverride("font_color", new Color(0.9f, 0.85f, 0.7f));
        label.AddThemeColorOverride("font_shadow_color", new Color(0, 0, 0, 0.8f));
        label.AddThemeConstantOverride("shadow_offset_x", 1);
        label.AddThemeConstantOverride("shadow_offset_y", 1);
        label.MouseFilter = Control.MouseFilterEnum.Ignore;
        btn.AddChild(label);
        FillParent(label);

        btn.ButtonDown += () => { btn.Modulate = new Color(0.8f, 0.75f, 0.7f); };
        btn.ButtonUp += () => { btn.Modulate = new Color(1, 1, 1); };

        return btn;
    }

    /// <summary>Create an RPG button with an icon to the left of the text.</summary>
    public static TextureButton CreateRpgButtonWithIcon(string text, string iconFile, bool isLong = true, int fontSize = 18, int iconSize = 16)
    {
        var btn = CreateRpgButton("", isLong, fontSize);
        // Remove the empty label that CreateRpgButton added
        foreach (var child in btn.GetChildren())
            if (child is Label) { child.QueueFree(); break; }

        // HBox with icon + label, centered
        var hbox = new HBoxContainer();
        hbox.Alignment = BoxContainer.AlignmentMode.Center;
        hbox.AddThemeConstantOverride("separation", 4);
        hbox.MouseFilter = Control.MouseFilterEnum.Ignore;
        btn.AddChild(hbox);
        FillParent(hbox);

        var icon = new TextureRect();
        icon.Texture = GetTex("Icons/" + iconFile);
        icon.ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize;
        icon.StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered;
        icon.CustomMinimumSize = new Vector2(iconSize, iconSize);
        icon.MouseFilter = Control.MouseFilterEnum.Ignore;
        hbox.AddChild(icon);

        var label = new Label();
        label.Text = text;
        label.VerticalAlignment = VerticalAlignment.Center;
        label.AddThemeFontSizeOverride("font_size", fontSize);
        label.AddThemeColorOverride("font_color", new Color(0.9f, 0.85f, 0.7f));
        label.AddThemeColorOverride("font_shadow_color", new Color(0, 0, 0, 0.8f));
        label.AddThemeConstantOverride("shadow_offset_x", 1);
        label.AddThemeConstantOverride("shadow_offset_y", 1);
        label.MouseFilter = Control.MouseFilterEnum.Ignore;
        hbox.AddChild(label);

        return btn;
    }

    public static TextureButton CreateMiniButton(string iconName, string iconHover,
                                                   Vector2? btnSize = null)
    {
        var size = btnSize ?? new Vector2(36, 36);
        var btn = new TextureButton();
        btn.MouseDefaultCursorShape = Control.CursorShape.PointingHand;
        btn.TextureNormal = GetTex(iconName);
        btn.TextureHover = GetTex(iconHover);
        btn.TexturePressed = GetTex(iconHover);
        btn.IgnoreTextureSize = true;
        btn.StretchMode = TextureButton.StretchModeEnum.KeepAspectCentered;
        btn.CustomMinimumSize = size;
        return btn;
    }

    // =========================================================================
    // CHECKBOXES
    // =========================================================================

    public static Button CreateRpgCheckbox(string style = "default", bool isChecked = false,
                                            Vector2? cbSize = null)
    {
        var size = cbSize ?? new Vector2(17, 17);
        var btn = new Button();
        btn.ToggleMode = true;
        btn.ButtonPressed = isChecked;
        btn.Flat = true;
        btn.CustomMinimumSize = size;
        btn.ClipContents = true;
        btn.AddThemeStyleboxOverride("normal", new StyleBoxEmpty());
        btn.AddThemeStyleboxOverride("hover", new StyleBoxEmpty());
        btn.AddThemeStyleboxOverride("pressed", new StyleBoxEmpty());
        btn.AddThemeStyleboxOverride("focus", new StyleBoxEmpty());

        var styleData = CheckboxStyles.GetValueOrDefault(style, CheckboxStyles["default"]);

        var fillRect = new TextureRect();
        fillRect.Texture = GetTex(styleData.Fill);
        fillRect.ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize;
        fillRect.StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered;
        fillRect.TextureFilter = CanvasItem.TextureFilterEnum.Linear;
        fillRect.MouseFilter = Control.MouseFilterEnum.Ignore;
        fillRect.Visible = isChecked;
        btn.AddChild(fillRect);
        FillParent(fillRect);

        var frameRect = new TextureRect();
        frameRect.Texture = GetTex(styleData.Frame);
        frameRect.ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize;
        frameRect.StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered;
        frameRect.TextureFilter = CanvasItem.TextureFilterEnum.Linear;
        frameRect.MouseFilter = Control.MouseFilterEnum.Ignore;
        btn.AddChild(frameRect);
        FillParent(frameRect);

        btn.Toggled += (bool pressed) => fillRect.Visible = pressed;
        return btn;
    }

    public static HBoxContainer CreateRpgCheckboxRow(string labelText, string style = "default",
                                                       bool isChecked = false)
    {
        var hbox = CreateRow(SpacingLg);
        var label = CreateInfoLabel(labelText, 13);
        label.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        hbox.AddChild(label);
        hbox.AddChild(CreateRpgCheckbox(style, isChecked));
        return hbox;
    }

    // =========================================================================
    // SLIDERS
    // =========================================================================

    public static HSlider CreateRpgSlider(float value = 50f, float minVal = 0f,
                                           float maxVal = 100f, float sliderWidth = 160f)
    {
        var slider = new HSlider();
        slider.MinValue = minVal;
        slider.MaxValue = maxVal;
        slider.Value = value;
        slider.Step = 1.0;
        slider.CustomMinimumSize = new Vector2(sliderWidth, 28);

        var trackStyle = new StyleBoxTexture();
        trackStyle.Texture = GetTex("long_line.png");
        trackStyle.TextureMarginLeft = 16; trackStyle.TextureMarginRight = 16;
        trackStyle.TextureMarginTop = 8;   trackStyle.TextureMarginBottom = 8;
        trackStyle.ContentMarginLeft = 4;  trackStyle.ContentMarginRight = 4;
        trackStyle.ContentMarginTop = 4;   trackStyle.ContentMarginBottom = 4;
        slider.AddThemeStyleboxOverride("slider", trackStyle);

        var fillStyle = new StyleBoxFlat();
        fillStyle.BgColor = new Color(0.62f, 0.47f, 0.18f, 0.85f);
        fillStyle.CornerRadiusTopLeft = 3;    fillStyle.CornerRadiusTopRight = 3;
        fillStyle.CornerRadiusBottomLeft = 3; fillStyle.CornerRadiusBottomRight = 3;
        fillStyle.ContentMarginTop = 6;   fillStyle.ContentMarginBottom = 6;
        fillStyle.ContentMarginLeft = 0;  fillStyle.ContentMarginRight = 0;
        slider.AddThemeStyleboxOverride("grabber_area", fillStyle);
        slider.AddThemeStyleboxOverride("grabber_area_highlight", fillStyle);

        var grabberTex = GetScaledTex("option_button.png", new Vector2I(24, 24));
        slider.AddThemeIconOverride("grabber", grabberTex);
        slider.AddThemeIconOverride("grabber_highlight", grabberTex);
        slider.AddThemeIconOverride("grabber_disabled", grabberTex);
        slider.AddThemeConstantOverride("grabber_offset", 2);

        return slider;
    }

    public static HBoxContainer CreateRpgSliderRow(string labelText, float value = 50f,
                                                     float sliderWidth = 140f)
    {
        var hbox = CreateRow();
        var label = CreateInfoLabel(labelText, 13);
        SetMinW(label, 80);
        label.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        hbox.AddChild(label);

        var slider = CreateRpgSlider(value, 0f, 100f, sliderWidth);
        hbox.AddChild(slider);

        var valLabel = CreateInfoLabel($"{(int)value}%", 12);
        SetMinW(valLabel, 35);
        valLabel.HorizontalAlignment = HorizontalAlignment.Right;
        hbox.AddChild(valLabel);

        slider.ValueChanged += (double newVal) => valLabel.Text = $"{(int)newVal}%";
        return hbox;
    }

    // =========================================================================
    // STAT BARS
    // =========================================================================

    public static TextureProgressBar CreateStatBar(string fillTexture, float barWidth = 200f,
                                                    float barHeight = 20f)
    {
        var bar = new TextureProgressBar();
        var frameTex = GetTex("Hp_frame.png");
        var fillTex = GetTex(fillTexture);
        bar.TextureUnder = frameTex;
        bar.TextureProgress = fillTex;
        bar.CustomMinimumSize = new Vector2(barWidth, barHeight);
        bar.NinePatchStretch = true;
        bar.StretchMarginLeft = 8;  bar.StretchMarginTop = 4;
        bar.StretchMarginRight = 8; bar.StretchMarginBottom = 4;
        if (fillTex.GetHeight() > frameTex.GetHeight() * 2)
            bar.TextureProgressOffset = Vector2.Zero;
        bar.Value = 75.0;
        return bar;
    }

    // =========================================================================
    // INVENTORY SLOTS
    // =========================================================================

    public static Control CreateInventorySlot(Vector2? slotSize = null)
    {
        var size = slotSize ?? new Vector2(72, 72);
        var slot = new Control();
        slot.CustomMinimumSize = size;
        var np = CreateNinePatch("inventory_frame_little.png", new Vector4(14, 14, 14, 14));
        slot.AddChild(np);
        FillParent(np);
        return slot;
    }

    // =========================================================================
    // INPUTS & DROPDOWNS
    // =========================================================================

    public static LineEdit CreateRpgInput(string placeholder = "", float inputWidth = 200f)
    {
        var input = new LineEdit();
        input.PlaceholderText = placeholder;
        input.CustomMinimumSize = new Vector2(inputWidth, 28);
        input.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;

        var styleNormal = new StyleBoxFlat();
        styleNormal.BgColor = new Color(0.10f, 0.08f, 0.06f, 0.85f);
        styleNormal.BorderColor = new Color(0.45f, 0.38f, 0.25f, 0.9f);
        styleNormal.SetBorderWidthAll(2);
        styleNormal.SetCornerRadiusAll(2);
        styleNormal.ContentMarginLeft = 8;  styleNormal.ContentMarginRight = 8;
        styleNormal.ContentMarginTop = 4;   styleNormal.ContentMarginBottom = 4;
        input.AddThemeStyleboxOverride("normal", styleNormal);

        var styleFocus = (StyleBoxFlat)styleNormal.Duplicate();
        styleFocus.BorderColor = new Color(0.65f, 0.55f, 0.35f, 1f);
        input.AddThemeStyleboxOverride("focus", styleFocus);
        input.AddThemeStyleboxOverride("read_only", (StyleBoxFlat)styleNormal.Duplicate());

        input.AddThemeFontSizeOverride("font_size", 13);
        input.AddThemeColorOverride("font_color", new Color(0.9f, 0.85f, 0.7f));
        input.AddThemeColorOverride("font_placeholder_color", new Color(0.5f, 0.47f, 0.4f));
        input.AddThemeColorOverride("caret_color", new Color(0.9f, 0.85f, 0.7f));
        input.AddThemeColorOverride("selection_color", new Color(0.4f, 0.35f, 0.2f, 0.5f));

        return input;
    }

    public static HBoxContainer CreateRpgInputRow(string labelText, string placeholder = "",
                                                    float inputWidth = 140f)
    {
        var row = CreateRow();
        var label = CreateInfoLabel(labelText, 13);
        SetMinW(label, 80);
        label.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        row.AddChild(label);
        row.AddChild(CreateRpgInput(placeholder, inputWidth));
        return row;
    }

    public static OptionButton CreateRpgDropdown(string[] items, float dropdownWidth = 200f)
    {
        var dropdown = new OptionButton();
        dropdown.CustomMinimumSize = new Vector2(dropdownWidth, 28);
        dropdown.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;

        foreach (var item in items)
            dropdown.AddItem(item);

        var styleNormal = new StyleBoxFlat();
        styleNormal.BgColor = new Color(0.14f, 0.11f, 0.08f, 0.85f);
        styleNormal.BorderColor = new Color(0.45f, 0.38f, 0.25f, 0.9f);
        styleNormal.SetBorderWidthAll(2);
        styleNormal.SetCornerRadiusAll(2);
        styleNormal.ContentMarginLeft = 8;  styleNormal.ContentMarginRight = 24;
        styleNormal.ContentMarginTop = 4;   styleNormal.ContentMarginBottom = 4;
        dropdown.AddThemeStyleboxOverride("normal", styleNormal);
        dropdown.AddThemeStyleboxOverride("focus", (StyleBoxFlat)styleNormal.Duplicate());

        var styleHover = (StyleBoxFlat)styleNormal.Duplicate();
        styleHover.BorderColor = new Color(0.65f, 0.55f, 0.35f, 1f);
        dropdown.AddThemeStyleboxOverride("hover", styleHover);
        dropdown.AddThemeStyleboxOverride("pressed", (StyleBoxFlat)styleHover.Duplicate());

        dropdown.AddThemeFontSizeOverride("font_size", 13);
        dropdown.AddThemeColorOverride("font_color", new Color(0.9f, 0.85f, 0.7f));
        dropdown.AddThemeColorOverride("font_hover_color", new Color(0.95f, 0.9f, 0.75f));

        return dropdown;
    }

    public static HBoxContainer CreateRpgDropdownRow(string labelText, string[] items,
                                                       float dropdownWidth = 140f)
    {
        var row = CreateRow();
        var label = CreateInfoLabel(labelText, 13);
        SetMinW(label, 80);
        label.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        row.AddChild(label);
        row.AddChild(CreateRpgDropdown(items, dropdownWidth));
        return row;
    }

    // =========================================================================
    // TOGGLE GROUP (radio-button-like selection)
    // =========================================================================

    /// <summary>
    /// Create a toggle group — a set of mutually exclusive RPG-styled toggle buttons.
    /// Returns a container where only one button can be active at a time.
    /// Use GetMeta("selected") to get the current index.
    /// </summary>
    public static BoxContainer CreateRpgToggleGroup(string[] options, int defaultIndex = 0,
                                                      Action<int>? onChanged = null,
                                                      bool vertical = false)
    {
        BoxContainer container = vertical ? new VBoxContainer() : new HBoxContainer();
        container.AddThemeConstantOverride("separation", 4);
        container.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;

        var buttons = new Button[options.Length];

        for (int i = 0; i < options.Length; i++)
        {
            int idx = i;
            var btn = new Button();
            btn.Text = options[i];
            btn.ToggleMode = true;
            btn.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            btn.CustomMinimumSize = new Vector2(0, 30);
            btn.AddThemeFontSizeOverride("font_size", 12);
            btn.FocusMode = Control.FocusModeEnum.None;

            btn.Pressed += () =>
            {
                for (int j = 0; j < buttons.Length; j++)
                {
                    buttons[j].ButtonPressed = (j == idx);
                    ApplyToggleStyle(buttons[j], j == idx);
                }
                container.SetMeta("selected", idx);
                onChanged?.Invoke(idx);
            };

            buttons[i] = btn;
            container.AddChild(btn);
        }

        container.SetMeta("selected", defaultIndex);

        container.Ready += () =>
        {
            for (int i2 = 0; i2 < buttons.Length; i2++)
            {
                buttons[i2].ButtonPressed = (i2 == defaultIndex);
                ApplyToggleStyle(buttons[i2], i2 == defaultIndex);
            }
        };

        return container;
    }

    private static void ApplyToggleStyle(Button btn, bool active)
    {
        if (active)
        {
            var style = new StyleBoxFlat();
            style.BgColor = new Color(0.22f, 0.18f, 0.12f, 1f);
            style.BorderColor = new Color(0.65f, 0.55f, 0.35f, 1f);
            style.SetBorderWidthAll(2);
            style.SetCornerRadiusAll(3);
            style.ContentMarginLeft = 6; style.ContentMarginRight = 6;
            style.ContentMarginTop = 4;  style.ContentMarginBottom = 4;
            btn.AddThemeStyleboxOverride("normal", style);
            btn.AddThemeStyleboxOverride("hover", style);
            btn.AddThemeStyleboxOverride("pressed", style);
            btn.AddThemeColorOverride("font_color", new Color(0.95f, 0.9f, 0.75f));
        }
        else
        {
            var style = new StyleBoxFlat();
            style.BgColor = new Color(0.10f, 0.08f, 0.06f, 0.85f);
            style.BorderColor = new Color(0.4f, 0.35f, 0.25f, 0.7f);
            style.SetBorderWidthAll(1);
            style.SetCornerRadiusAll(3);
            style.ContentMarginLeft = 6; style.ContentMarginRight = 6;
            style.ContentMarginTop = 4;  style.ContentMarginBottom = 4;
            btn.AddThemeStyleboxOverride("normal", style);

            var hover = (StyleBoxFlat)style.Duplicate();
            hover.BgColor = new Color(0.15f, 0.12f, 0.09f, 0.9f);
            btn.AddThemeStyleboxOverride("hover", hover);
            btn.AddThemeStyleboxOverride("pressed", (StyleBoxFlat)hover.Duplicate());
            btn.AddThemeColorOverride("font_color", new Color(0.7f, 0.65f, 0.5f));
        }
    }

    /// <summary>
    /// Create a toggle group laid out in a GridContainer (for many options, e.g. class selector).
    /// Same mutual-exclusion behavior as CreateRpgToggleGroup but in a grid.
    /// </summary>
    public static GridContainer CreateRpgToggleGrid(string[] options, int columns,
                                                       int defaultIndex = 0,
                                                       Action<int>? onChanged = null)
    {
        var container = new GridContainer();
        container.Columns = columns;
        container.AddThemeConstantOverride("h_separation", 4);
        container.AddThemeConstantOverride("v_separation", 4);
        container.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;

        var buttons = new Button[options.Length];

        for (int i = 0; i < options.Length; i++)
        {
            int idx = i;
            var btn = new Button();
            btn.Text = options[i];
            btn.ToggleMode = true;
            btn.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            btn.CustomMinimumSize = new Vector2(0, 30);
            btn.AddThemeFontSizeOverride("font_size", 12);
            btn.FocusMode = Control.FocusModeEnum.None;

            btn.Pressed += () =>
            {
                for (int j = 0; j < buttons.Length; j++)
                {
                    buttons[j].ButtonPressed = (j == idx);
                    ApplyToggleStyle(buttons[j], j == idx);
                }
                container.SetMeta("selected", idx);
                onChanged?.Invoke(idx);
            };

            buttons[i] = btn;
            container.AddChild(btn);
        }

        container.SetMeta("selected", defaultIndex);

        container.Ready += () =>
        {
            for (int i2 = 0; i2 < buttons.Length; i2++)
            {
                buttons[i2].ButtonPressed = (i2 == defaultIndex);
                ApplyToggleStyle(buttons[i2], i2 == defaultIndex);
            }
        };

        return container;
    }

    /// <summary>
    /// Programmatically set the active index on a toggle group/grid created by
    /// CreateRpgToggleGroup() or CreateRpgToggleGrid().
    /// </summary>
    public static void SetToggleGroupActive(Container container, int index)
    {
        int i = 0;
        foreach (var child in container.GetChildren())
        {
            if (child is Button btn)
            {
                btn.ButtonPressed = (i == index);
                ApplyToggleStyle(btn, i == index);
                i++;
            }
        }
        container.SetMeta("selected", index);
    }

    // =========================================================================
    // PROGRESS BAR
    // =========================================================================

    /// <summary>
    /// Create a themed ProgressBar with RPG styling (texture-based or flat).
    /// </summary>
    public static ProgressBar CreateRpgProgressBar(float minWidth = 200f, float minHeight = 22f,
                                                     Color? fillColor = null, Color? bgColor = null)
    {
        var bar = new ProgressBar();
        bar.CustomMinimumSize = new Vector2(minWidth, minHeight);
        bar.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        bar.ShowPercentage = false;
        bar.MinValue = 0;
        bar.MaxValue = 100;

        var bgStyle = new StyleBoxFlat();
        bgStyle.BgColor = bgColor ?? new Color(0.08f, 0.07f, 0.05f, 0.9f);
        bgStyle.BorderColor = new Color(0.45f, 0.38f, 0.25f, 0.8f);
        bgStyle.SetBorderWidthAll(1);
        bgStyle.SetCornerRadiusAll(2);
        bgStyle.ContentMarginLeft = 2; bgStyle.ContentMarginRight = 2;
        bgStyle.ContentMarginTop = 2;  bgStyle.ContentMarginBottom = 2;
        bar.AddThemeStyleboxOverride("background", bgStyle);

        var fillStyle = new StyleBoxFlat();
        fillStyle.BgColor = fillColor ?? new Color(0.55f, 0.45f, 0.2f, 0.95f);
        fillStyle.SetCornerRadiusAll(1);
        bar.AddThemeStyleboxOverride("fill", fillStyle);

        return bar;
    }

    /// <summary>
    /// Create a labeled progress bar row with label on left and value on right.
    /// </summary>
    public static HBoxContainer CreateRpgProgressBarRow(string labelText, float value = 0f,
                                                          float barWidth = 120f, Color? fillColor = null)
    {
        var row = CreateRow(SpacingMd);
        var label = CreateInfoLabel(labelText, 12);
        SetMinW(label, 80);
        row.AddChild(label);

        var bar = CreateRpgProgressBar(barWidth, 16, fillColor);
        bar.Value = value;
        row.AddChild(bar);

        var valLabel = CreateInfoLabel($"{(int)value}", 11);
        SetMinW(valLabel, 30);
        valLabel.HorizontalAlignment = HorizontalAlignment.Right;
        row.AddChild(valLabel);

        return row;
    }
}
