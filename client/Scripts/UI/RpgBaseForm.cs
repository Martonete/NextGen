using Godot;
using ArgentumNextgen.Game;

namespace ArgentumNextgen.UI;

/// <summary>
/// Base class for all RPG form dialogs.
/// Subclasses override BuildContent() to add their UI into ContentContainer.
/// Supports four styles: "v1" (info_window frame), "v2" (big_bar stretched),
/// "v3" (dark bg + info_window frame), "v4" (frame only, no title bar).
/// </summary>
public partial class RpgBaseForm : Control
{
    // Z-index layers (standardized across the game)
    public const int ZPanel = 1;
    public const int ZTooltip = 2;
    public const int ZContextMenu = 5;
    public const int ZDialog = 10;
    public const int ZLoading = 20;
    public const int ZBlind = 100;

    // Global form transparency (0.0-1.0), applied via Modulate
    private static float _globalFormAlpha = 1.0f;
    private static readonly System.Collections.Generic.List<RpgBaseForm> _allForms = new();

    public string TitleText { get; set; } = "Formulario";
    public Vector2 FormSize { get; set; } = new Vector2(500, 400);
    public string FormStyle { get; set; } = "v1";
    public bool Draggable { get; set; } = true;
    public bool ShowCloseButton { get; set; } = true;
    public bool CloseOnEscape { get; set; } = false;
    public MarginContainer ContentContainer { get; private set; } = null!;

    private bool _isDragging;
    private Vector2 _dragOffset;

    public RpgBaseForm() { }

    public RpgBaseForm(string title = "Formulario", Vector2? size = null, string style = "v2")
    {
        TitleText = title;
        FormSize = size ?? new Vector2(500, 400);
        FormStyle = style;
    }

    public override void _Ready()
    {
        _allForms.Add(this);
        BuildForm();
    }

    public override void _ExitTree()
    {
        _allForms.Remove(this);
    }

    /// <summary>
    /// Set global form transparency (0.0-1.0) and update all existing forms.
    /// </summary>
    public static void ApplyGlobalAlpha(float alpha)
    {
        _globalFormAlpha = System.Math.Clamp(alpha, 0.4f, 1.0f);
        for (int i = _allForms.Count - 1; i >= 0; i--)
        {
            var form = _allForms[i];
            if (form != null && IsInstanceValid(form))
                form.Modulate = new Color(1, 1, 1, _globalFormAlpha);
            else
                _allForms.RemoveAt(i);
        }
    }

    /// <summary>Scale factor for pre-game forms (login, char select).
    /// Grows slower than UIScale to avoid oversized windows.</summary>
    public static float FormScale => 1f + (ResolutionManager.UIScale - 1f) * 0.5f;

    private void BuildForm()
    {
        Visible = false;
        CustomMinimumSize = FormSize;
        Size = FormSize;
        // Scale the entire form proportionally (fonts, inputs, buttons all scale together)
        float s = FormScale;
        Scale = new Vector2(s, s);
        ClipContents = true;
        MouseFilter = MouseFilterEnum.Stop;

        switch (FormStyle)
        {
            case "v2": BuildV2(); break;
            case "v3": BuildV3(); break;
            case "v4": BuildV4(); break;
            default:   BuildV1(); break;
        }

        BuildContent();

        // --- Close button (TOP z-order) ---
        if (ShowCloseButton)
        {
            var closeBtn = RpgTheme.CreateMiniButton("Mini_exit.png", "Mini_exit_t.png", new Vector2(28, 28));
            closeBtn.Pressed += HideForm;
            AddChild(closeBtn);
            closeBtn.AnchorLeft = 1.0f;
            closeBtn.AnchorRight = 1.0f;
            closeBtn.AnchorTop = 0.0f;
            closeBtn.AnchorBottom = 0.0f;
            closeBtn.OffsetLeft = -38;
            closeBtn.OffsetTop = 13;
            closeBtn.OffsetRight = -8;
            closeBtn.OffsetBottom = 36;
        }
    }

    private void BuildV1()
    {
        // --- Layer 0: Solid background ---
        var solidBg = new ColorRect();
        solidBg.Color = new Color(0.10f, 0.09f, 0.08f, 1.0f);
        solidBg.MouseFilter = MouseFilterEnum.Ignore;
        AddChild(solidBg);
        RpgTheme.FillParent(solidBg);

        // --- Layer 1: NinePatch frame ---
        var frameBg = RpgTheme.CreateNinePatch("info_window.png", new Vector4(16, 16, 16, 16));
        AddChild(frameBg);
        RpgTheme.FillParent(frameBg);

        // --- Title: name_frame centered on top border ---
        AddTitleFrame();

        // --- Content area ---
        ContentContainer = new MarginContainer();
        ContentContainer.MouseFilter = MouseFilterEnum.Ignore;
        ContentContainer.AddThemeConstantOverride("margin_top", RpgTheme.FormMarginTop);
        ContentContainer.AddThemeConstantOverride("margin_left", RpgTheme.FormMarginLeft);
        ContentContainer.AddThemeConstantOverride("margin_right", RpgTheme.FormMarginRight);
        ContentContainer.AddThemeConstantOverride("margin_bottom", RpgTheme.FormMarginBottom);
        AddChild(ContentContainer);
        RpgTheme.FillParent(ContentContainer);
    }

    private void BuildV3()
    {
        // --- Layer 0: Solid dark background ---
        var solidBg = new ColorRect();
        solidBg.Color = new Color(0.15f, 0.14f, 0.13f, 1.0f);
        solidBg.MouseFilter = MouseFilterEnum.Ignore;
        AddChild(solidBg);
        RpgTheme.FillParent(solidBg);

        // --- Layer 1: NinePatch frame ---
        var frameBg = RpgTheme.CreateNinePatch("info_window.png", new Vector4(16, 16, 16, 16));
        AddChild(frameBg);
        RpgTheme.FillParent(frameBg);

        // --- Title: name_frame centered on top border ---
        AddTitleFrame();

        // --- Content area ---
        ContentContainer = new MarginContainer();
        ContentContainer.MouseFilter = MouseFilterEnum.Ignore;
        ContentContainer.AddThemeConstantOverride("margin_top", RpgTheme.FormMarginTop);
        ContentContainer.AddThemeConstantOverride("margin_left", RpgTheme.FormMarginLeft);
        ContentContainer.AddThemeConstantOverride("margin_right", RpgTheme.FormMarginRight);
        ContentContainer.AddThemeConstantOverride("margin_bottom", RpgTheme.FormMarginBottom);
        AddChild(ContentContainer);
        RpgTheme.FillParent(ContentContainer);
    }

    private void BuildV2()
    {
        // --- big_bar.png stretched as background ---
        var bg = new TextureRect();
        bg.Texture = RpgTheme.GetTex("big_bar.png");
        bg.ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize;
        bg.StretchMode = TextureRect.StretchModeEnum.Scale;
        bg.Modulate = new Color(1f, 1f, 1f, 1f);
        bg.MouseFilter = MouseFilterEnum.Ignore;
        AddChild(bg);
        RpgTheme.FillParent(bg);

        // --- Title: name_frame centered on top border ---
        AddTitleFrame();

        // --- Content area (V2 has thicker borders → larger margins) ---
        ContentContainer = new MarginContainer();
        ContentContainer.MouseFilter = MouseFilterEnum.Ignore;
        ContentContainer.AddThemeConstantOverride("margin_top", 54);
        ContentContainer.AddThemeConstantOverride("margin_left", 36);
        ContentContainer.AddThemeConstantOverride("margin_right", 36);
        ContentContainer.AddThemeConstantOverride("margin_bottom", 38);
        AddChild(ContentContainer);
        RpgTheme.FillParent(ContentContainer);
    }

    private void BuildV4()
    {
        // --- Layer 0: Solid background ---
        var solidBg = new ColorRect();
        solidBg.Color = new Color(0.10f, 0.09f, 0.08f, 1.0f);
        solidBg.MouseFilter = MouseFilterEnum.Ignore;
        AddChild(solidBg);
        RpgTheme.FillParent(solidBg);

        // --- Layer 1: NinePatch frame (no title bar) ---
        var frameBg = RpgTheme.CreateNinePatch("info_window.png", new Vector4(16, 16, 16, 16));
        AddChild(frameBg);
        RpgTheme.FillParent(frameBg);

        // --- Content area (uses panel margins since no title bar) ---
        ContentContainer = new MarginContainer();
        ContentContainer.MouseFilter = MouseFilterEnum.Ignore;
        ContentContainer.AddThemeConstantOverride("margin_top", RpgTheme.PanelMarginTop);
        ContentContainer.AddThemeConstantOverride("margin_left", RpgTheme.PanelMarginLeft);
        ContentContainer.AddThemeConstantOverride("margin_right", RpgTheme.PanelMarginRight);
        ContentContainer.AddThemeConstantOverride("margin_bottom", RpgTheme.PanelMarginBottom);
        AddChild(ContentContainer);
        RpgTheme.FillParent(ContentContainer);
    }

    /// <summary>
    /// Override in subclasses to add UI content into ContentContainer.
    /// </summary>
    protected virtual void BuildContent() { }

    public void ShowForm()
    {
        Visible = true;
        Modulate = new Color(1, 1, 1, _globalFormAlpha);
        // Use the logical coordinate space for centering.
        // When ContentScaleMode is active (fullscreen), ContentScaleSize defines
        // the logical space that ALL canvas items render in (including CanvasLayer).
        // When in windowed mode (ContentScaleSize=0), use the viewport rect.
        Vector2 areaSize;
        var root = GetTree()?.Root;
        if (root != null && root.ContentScaleSize != Vector2I.Zero)
        {
            areaSize = (Vector2)root.ContentScaleSize;
        }
        else
        {
            areaSize = GetViewportRect().Size;
        }
        Position = (areaSize - Size * Scale) / 2.0f;
        MoveToFront();
    }

    public void ShowForm(Vector2 atPosition)
    {
        Visible = true;
        Modulate = new Color(1, 1, 1, _globalFormAlpha);
        Position = atPosition;
        MoveToFront();
    }

    public virtual void HideForm()
    {
        Visible = false;
    }

    public void Toggle()
    {
        if (Visible)
            HideForm();
        else
            ShowForm();
    }

    public override void _UnhandledKeyInput(InputEvent @event)
    {
        if (!CloseOnEscape || !Visible) return;
        if (@event is InputEventKey key && key.Pressed && key.Keycode == Key.Escape)
        {
            HideForm();
            GetViewport().SetInputAsHandled();
        }
    }

    /// <summary>
    /// Add a name_frame_mid_ready.png title bar centered on the top border of the form.
    /// The frame sits half above, half below the top edge for a "badge" effect.
    /// </summary>
    private void AddTitleFrame()
    {
        if (string.IsNullOrEmpty(TitleText)) return;

        // Size frame to fit title text generously
        int textLen = TitleText.Length;
        float frameW = System.Math.Max(textLen * 14 + 80, 220); // wide frame, min 220px
        float frameH = 50;

        var titleBg = RpgTheme.CreateNinePatch("name_frame_mid_ready.png", new Vector4(30, 10, 30, 10));
        AddChild(titleBg);
        // Centered horizontally, straddling the top border (-8px above, +22px below)
        titleBg.AnchorLeft = 0.5f;
        titleBg.AnchorRight = 0.5f;
        titleBg.AnchorTop = 0.0f;
        titleBg.AnchorBottom = 0.0f;
        titleBg.OffsetLeft = -frameW / 2f;
        titleBg.OffsetRight = frameW / 2f;
        titleBg.OffsetTop = -8;
        titleBg.OffsetBottom = -8 + frameH;

        var titleLabel = RpgTheme.CreateTitleLabel(TitleText, 16);
        titleBg.AddChild(titleLabel);
        RpgTheme.FillParent(titleLabel);
        titleLabel.OffsetTop = 2;
        titleLabel.OffsetBottom = -2;
    }

    public override void _GuiInput(InputEvent @event)
    {
        if (!Draggable)
        {
            // Still bring to front on click
            if (@event is InputEventMouseButton mb2 && mb2.ButtonIndex == MouseButton.Left && mb2.Pressed)
                MoveToFront();
            return;
        }

        if (@event is InputEventMouseButton mb)
        {
            if (mb.ButtonIndex == MouseButton.Left)
            {
                if (mb.Pressed)
                {
                    _isDragging = true;
                    _dragOffset = mb.Position;
                    MoveToFront();
                }
                else
                {
                    _isDragging = false;
                }
                AcceptEvent();
            }
        }
        else if (@event is InputEventMouseMotion mm && _isDragging)
        {
            Position += mm.Position - _dragOffset;
            AcceptEvent();
        }
    }
}
