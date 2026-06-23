using Godot;
using System;

namespace ArgentumNextgen.UI;

public static partial class RpgTheme
{
    // =========================================================================
    // SIZE / ALIGNMENT HELPERS (chainable)
    // =========================================================================

    /// <summary>Fill parent using anchors (equivalent to full_rect anchor preset).</summary>
    public static void FillParent(Control node)
    {
        node.AnchorLeft = 0f;
        node.AnchorTop = 0f;
        node.AnchorRight = 1f;
        node.AnchorBottom = 1f;
        node.OffsetLeft = 0;
        node.OffsetTop = 0;
        node.OffsetRight = 0;
        node.OffsetBottom = 0;
    }

    public static T ExpandH<T>(T node) where T : Control
    {
        node.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        return node;
    }

    public static T ExpandV<T>(T node) where T : Control
    {
        node.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
        return node;
    }

    public static T Expand<T>(T node) where T : Control
    {
        node.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        node.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
        return node;
    }

    public static T SetMinW<T>(T node, float w) where T : Control
    {
        var s = node.CustomMinimumSize;
        s.X = w;
        node.CustomMinimumSize = s;
        return node;
    }

    public static T SetMinH<T>(T node, float h) where T : Control
    {
        var s = node.CustomMinimumSize;
        s.Y = h;
        node.CustomMinimumSize = s;
        return node;
    }

    // =========================================================================
    // LAYOUT HELPERS
    // =========================================================================

    public static HBoxContainer CreateRow(int spacing = SpacingMd)
    {
        var hbox = new HBoxContainer();
        hbox.AddThemeConstantOverride("separation", spacing);
        hbox.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        return hbox;
    }

    public static VBoxContainer CreateColumn(int spacing = SpacingMd)
    {
        var vbox = new VBoxContainer();
        vbox.AddThemeConstantOverride("separation", spacing);
        vbox.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        return vbox;
    }

    public static GridContainer CreateGrid(int columns, int hSpacing = SpacingLg, int vSpacing = SpacingSm)
    {
        var grid = new GridContainer();
        grid.Columns = columns;
        grid.AddThemeConstantOverride("h_separation", hSpacing);
        grid.AddThemeConstantOverride("v_separation", vSpacing);
        grid.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        return grid;
    }

    public static VBoxContainer CreateSection(string title, int spacing = SpacingMd)
    {
        var section = CreateColumn(spacing);
        section.AddChild(CreateTitleLabel(title, 15));
        return section;
    }

    // =========================================================================
    // SCROLL AREA (custom scrollbar in padding area)
    // =========================================================================

    public static Control CreateScrollArea(int spacing = SpacingMd)
    {
        var wrapper = new Control();
        wrapper.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
        wrapper.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;

        var scroll = new ScrollContainer();
        scroll.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
        scroll.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        scroll.VerticalScrollMode = ScrollContainer.ScrollMode.ShowNever;
        scroll.HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled;
        wrapper.AddChild(scroll);
        FillParent(scroll);

        var content = new VBoxContainer();
        content.AddThemeConstantOverride("separation", spacing);
        content.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        scroll.AddChild(content);

        wrapper.SetMeta("content", content);
        wrapper.SetMeta("scroll", scroll);

        var customBar = new VScrollBar();
        customBar.CustomMinimumSize = new Vector2(12, 0);
        customBar.Visible = false;
        wrapper.AddChild(customBar);

        scroll.Ready += () =>
        {
            var hbar = scroll.GetHScrollBar();
            if (hbar != null) ApplyScrollbarTheme(hbar);
            ApplyScrollbarTheme(customBar);

            var internalBar = scroll.GetVScrollBar();
            if (internalBar == null) return;

            customBar.AnchorLeft = 1f;
            customBar.AnchorRight = 1f;
            customBar.AnchorTop = 0f;
            customBar.AnchorBottom = 1f;
            customBar.OffsetTop = 6;
            customBar.OffsetBottom = -6;

            int baseMargin = PanelMarginRight;
            int borderW = 16;
            int barW = 12;
            int contentGap = 10;
            int borderGap = 6;
            int scrollMargin = borderW + borderGap + barW + contentGap; // 44
            int barOffset = contentGap;

            // Walk up to find nearest MarginContainer ancestor
            MarginContainer? mc = null;
            Node p = wrapper.GetParent();
            while (p != null && p is not MarginContainer)
                p = p.GetParent();
            mc = p as MarginContainer;

            bool prevVisible = false;

            void SyncBar()
            {
                bool needScroll = internalBar.MaxValue > internalBar.Page;
                customBar.Visible = needScroll;

                if (needScroll)
                {
                    customBar.MinValue = internalBar.MinValue;
                    customBar.MaxValue = internalBar.MaxValue;
                    customBar.Page = internalBar.Page;
                    customBar.Value = internalBar.Value;
                }

                if (needScroll != prevVisible)
                {
                    prevVisible = needScroll;
                    if (mc != null)
                    {
                        if (needScroll)
                        {
                            mc.AddThemeConstantOverride("margin_left", scrollMargin);
                            mc.AddThemeConstantOverride("margin_right", scrollMargin);
                        }
                        else
                        {
                            mc.AddThemeConstantOverride("margin_left", baseMargin);
                            mc.AddThemeConstantOverride("margin_right", baseMargin);
                        }
                    }
                    customBar.OffsetLeft = barOffset;
                    customBar.OffsetRight = barOffset + barW;
                }
            }

            internalBar.Changed += SyncBar;
            internalBar.ValueChanged += (double val) => customBar.Value = val;
            customBar.ValueChanged += (double val) => scroll.ScrollVertical = (int)val;
            Callable.From(new Action(SyncBar)).CallDeferred();
        };

        return wrapper;
    }

    // =========================================================================
    // SCROLLBAR THEME
    // =========================================================================

    public static void StyleScrollbar(ScrollContainer scroll)
    {
        var vbar = scroll.GetVScrollBar();
        if (vbar != null) ApplyScrollbarTheme(vbar);
        var hbar = scroll.GetHScrollBar();
        if (hbar != null) ApplyScrollbarTheme(hbar);
    }

    public static void ApplyScrollbarTheme(ScrollBar bar)
    {
        bar.CustomMinimumSize = new Vector2(12, 0);

        var track = new StyleBoxFlat();
        track.BgColor = new Color(0.12f, 0.10f, 0.08f, 0.6f);
        track.BorderColor = new Color(0.4f, 0.35f, 0.25f, 0.5f);
        track.SetBorderWidthAll(1);
        track.SetCornerRadiusAll(3);
        track.ContentMarginLeft = 2; track.ContentMarginRight = 2;
        track.ContentMarginTop = 2;  track.ContentMarginBottom = 2;
        bar.AddThemeStyleboxOverride("scroll", track);

        var grabber = new StyleBoxFlat();
        grabber.BgColor = new Color(0.45f, 0.38f, 0.22f, 0.85f);
        grabber.BorderColor = new Color(0.65f, 0.55f, 0.35f, 0.8f);
        grabber.SetBorderWidthAll(1);
        grabber.SetCornerRadiusAll(2);
        bar.AddThemeStyleboxOverride("grabber", grabber);

        var grabberHl = new StyleBoxFlat();
        grabberHl.BgColor = new Color(0.55f, 0.47f, 0.28f, 0.95f);
        grabberHl.BorderColor = new Color(0.8f, 0.7f, 0.45f, 0.9f);
        grabberHl.SetBorderWidthAll(1);
        grabberHl.SetCornerRadiusAll(2);
        bar.AddThemeStyleboxOverride("grabber_highlight", grabberHl);
        bar.AddThemeStyleboxOverride("grabber_pressed", (StyleBoxFlat)grabberHl.Duplicate());
    }

    // =========================================================================
    // FOOTER / SEPARATOR / SPACER
    // =========================================================================

    public static VBoxContainer CreateFooterRow(int spacing = SpacingLg)
    {
        var footer = new VBoxContainer();
        footer.AddThemeConstantOverride("separation", SpacingMd);
        footer.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        footer.AddChild(new HSeparator());

        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", spacing);
        row.Alignment = BoxContainer.AlignmentMode.Center;
        row.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        footer.AddChild(row);
        footer.SetMeta("row", row);
        return footer;
    }

    public static HSeparator CreateSeparator() => new();

    public static Control CreateSpacer(float height = 8f, float width = 0f)
    {
        var spacer = new Control();
        spacer.CustomMinimumSize = new Vector2(width, height);
        return spacer;
    }

    // =========================================================================
    // CARDS (sub-panels)
    // =========================================================================

    public static PanelContainer CreateCard(string bgTexture = "little_background_frame.png",
                                             Vector4? npMargins = null)
    {
        var m = npMargins ?? new Vector4(10, 10, 10, 10);
        var card = new PanelContainer();
        card.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;

        var style = new StyleBoxTexture();
        style.Texture = GetTex(bgTexture);
        style.TextureMarginLeft = m.X;
        style.TextureMarginTop = m.Y;
        style.TextureMarginRight = m.Z;
        style.TextureMarginBottom = m.W;
        style.ContentMarginLeft = SpacingXl;
        style.ContentMarginTop = SpacingXl;
        style.ContentMarginRight = SpacingXl;
        style.ContentMarginBottom = SpacingXl;
        card.AddThemeStyleboxOverride("panel", style);
        return card;
    }

    // =========================================================================
    // STYLED PANELS (NinePatch frame + margins + integrated scroll)
    // =========================================================================

    public static Control CreateStyledPanel(string styleName, Vector2 panelSize,
                                             bool darkBg = false, string title = "", int titleSize = 16)
    {
        var panel = new Control();
        panel.CustomMinimumSize = panelSize;
        panel.Size = panelSize;
        panel.ClipContents = true;

        if (darkBg)
        {
            var solidBg = new ColorRect();
            solidBg.Color = new Color(0.15f, 0.14f, 0.13f, 0.95f);
            solidBg.MouseFilter = Control.MouseFilterEnum.Ignore;
            panel.AddChild(solidBg);
            FillParent(solidBg);
        }

        var styleData = PanelStyles.GetValueOrDefault(styleName, PanelStyles["info_window"]);

        if (styleData.Stretched != null)
        {
            var texRect = new TextureRect();
            texRect.Texture = GetTex(styleData.Stretched);
            texRect.ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize;
            texRect.StretchMode = TextureRect.StretchModeEnum.Scale;
            texRect.MouseFilter = Control.MouseFilterEnum.Ignore;
            panel.AddChild(texRect);
            FillParent(texRect);
        }
        else if (styleData.Texture != null)
        {
            var np = CreateNinePatch(styleData.Texture, styleData.NpMargins ?? new Vector4(16, 16, 16, 16));
            panel.AddChild(np);
            FillParent(np);
        }

        var marginC = new MarginContainer();
        marginC.AddThemeConstantOverride("margin_top", PanelMarginTop);
        marginC.AddThemeConstantOverride("margin_left", PanelMarginLeft);
        marginC.AddThemeConstantOverride("margin_right", PanelMarginRight);
        marginC.AddThemeConstantOverride("margin_bottom", PanelMarginBottom);
        marginC.MouseFilter = Control.MouseFilterEnum.Ignore;
        panel.AddChild(marginC);
        FillParent(marginC);

        var mainCol = CreateColumn(SpacingMd);
        marginC.AddChild(mainCol);

        if (!string.IsNullOrEmpty(title))
        {
            mainCol.AddChild(CreateTitleLabel(title, titleSize));
            mainCol.AddChild(CreateSeparator());
        }

        var scrollArea = CreateScrollArea();
        mainCol.AddChild(scrollArea);
        var scrollContent = scrollArea.GetMeta("content").As<VBoxContainer>();

        panel.SetMeta("content", scrollContent);
        return panel;
    }
}
