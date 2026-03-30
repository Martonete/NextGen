using Godot;
using System;
using System.Collections.Generic;

namespace ArgentumNextgen.UI;

/// <summary>
/// Centralized RPG UI theme system — factory methods for creating themed controls.
/// Port of rpg_theme.gd from the Classic_RPG_UI project.
/// Static utility class (no Node/autoload required).
/// </summary>
public static class RpgTheme
{
    public const string AssetsPath = "res://Data/UI/";

    // =========================================================================
    // SPACING & MARGIN CONSTANTS
    // =========================================================================

    public const int SpacingSm = 4;
    public const int SpacingMd = 8;
    public const int SpacingLg = 12;
    public const int SpacingXl = 16;

    // Form margins (with title bar — BaseForm)
    public const int FormMarginTop = 58;
    public const int FormMarginLeft = 30;
    public const int FormMarginRight = 30;
    public const int FormMarginBottom = 20;

    // Panel margins (no title bar)
    public const int PanelMarginTop = 20;
    public const int PanelMarginLeft = 30;
    public const int PanelMarginRight = 30;
    public const int PanelMarginBottom = 20;

    // =========================================================================
    // DATA TYPES
    // =========================================================================

    public record struct PanelStyleData(string? Stretched = null, string? Texture = null, Vector4? NpMargins = null);
    public record struct CheckboxStyleData(string Frame, string Fill);
    public record struct MiniButtonData(string Normal, string Hover);

    // =========================================================================
    // PANEL STYLES
    // =========================================================================

    public static readonly Dictionary<string, PanelStyleData> PanelStyles = new()
    {
        ["big_bar"]       = new(Stretched: "big_bar.png"),
        ["info_window"]   = new(Texture: "info_window.png",             NpMargins: new Vector4(16, 16, 16, 16)),
        ["info_window_2"] = new(Texture: "info_window_2.png",           NpMargins: new Vector4(16, 16, 16, 16)),
        ["dark_card"]     = new(Texture: "little_background_frame.png", NpMargins: new Vector4(10, 10, 10, 10)),
        ["dialoge"]       = new(Texture: "dialoge_frame.png",           NpMargins: new Vector4(16, 16, 16, 16)),
        ["inventory"]     = new(Texture: "inventory_frame.png",         NpMargins: new Vector4(16, 16, 16, 16)),
        ["skill"]         = new(Texture: "skill_frame.png",             NpMargins: new Vector4(12, 12, 12, 12)),
        ["thin"]          = new(Texture: "Thin_frame.png",              NpMargins: new Vector4(8, 8, 8, 8)),
        ["red_vert"]      = new(Texture: "Red_vert_panel.png",          NpMargins: new Vector4(12, 12, 12, 12)),
    };

    // =========================================================================
    // CHECKBOX STYLES
    // =========================================================================

    public static readonly Dictionary<string, CheckboxStyleData> CheckboxStyles = new()
    {
        ["default"]            = new("Round_fr.png",                  "update/Background_r_green.png"),
        ["glow_round_blue"]    = new("update/RoundFrame_blue.png",    "update/Background_r_blue.png"),
        ["glow_round_green"]   = new("update/RoundFrame_green.png",   "update/Background_r_green.png"),
        ["glow_round_grey"]    = new("update/RoundFrame_grey.png",    "update/Background_r_grey.png"),
        ["glow_round_purple"]  = new("update/RoundFrame_purple.png",  "update/Background_r_purple.png"),
        ["glow_round_red"]     = new("update/RoundFrame_red.png",     "update/Background_r_red.png"),
        ["glow_square_blue"]   = new("update/Frame_blue.png",         "update/Background_blue.png"),
        ["glow_square_green"]  = new("update/Frame_green.png",        "update/Background_green.png"),
        ["glow_square_grey"]   = new("update/Frame_grey.png",         "update/Background_grey.png"),
        ["glow_square_purple"] = new("update/Frame_purple.png",       "update/Background_purple.png"),
        ["glow_square_red"]    = new("update/Frame_red.png",          "update/Background_red.png"),
    };

    // =========================================================================
    // MINI BUTTONS DATA
    // =========================================================================

    public static readonly Dictionary<string, MiniButtonData> MiniButtons = new()
    {
        ["exit"]         = new("Mini_exit.png",        "Mini_exit_t.png"),
        ["add"]          = new("Mini_add.png",         "Mini_add_t.png"),
        ["help"]         = new("Mini_help.png",        "Mini_help_t.png"),
        ["guild"]        = new("Mini_guild.png",       "Mini_guild_t.png"),
        ["skip"]         = new("Mini_skip.png",        "Mini_skip_t.png"),
        ["speak"]        = new("Mini_speak.png",       "Mini_speak_t.png"),
        ["arrow_top"]    = new("Mini_arrow_top.png",   "Mini_arrow_top.png"),
        ["arrow_bot"]    = new("Mini_arrow_bot.png",   "Mini_arrow_bot.png"),
        ["arrow_top2"]   = new("Mini_arrow_top2.png",  "Mini_arrow_top2_t.png"),
        ["arrow_bot2"]   = new("Mini_arrow_bot2.png",  "Mini_arrow_bot2_t.png"),
        ["arrow_left2"]  = new("Mini_arrow_left2.png", "Mini_arrow_left2_t.png"),
        ["arrow_right2"] = new("Mini_arrow_right2.png","Mini_arrow_right2_t.png"),
    };

    // =========================================================================
    // ASSET CATALOG
    // =========================================================================

    public static readonly Dictionary<string, Dictionary<string, string>> Assets = new()
    {
        ["buttons"] = new()
        {
            ["long_normal"] = "long_button.png",     ["long_hover"] = "long_button_on.png",
            ["long_disabled"] = "long_button_off.png",["mid_normal"] = "mid_button.png",
            ["mid_hover"] = "mid_button_on.png",     ["mid_disabled"] = "mid_button_off.png",
            ["mid_vert"] = "mid_button_vert.png",    ["mini"] = "mini_button.png",
            ["inventory"] = "inventory_button.png",  ["inventory2"] = "inventory_button2.png",
            ["inventory2_long"] = "inventory_button2_long.png",
            ["option"] = "option_button.png",        ["plus"] = "Plus.png", ["minus"] = "Minus.png",
        },
        ["frames"] = new()
        {
            ["info_window"] = "info_window.png",           ["info_window_2"] = "info_window_2.png",
            ["dialoge"] = "dialoge_frame.png",             ["inventory"] = "inventory_frame.png",
            ["inventory_little"] = "inventory_frame_little.png",
            ["inventory_little_long"] = "inventory_frame_little_long.png",
            ["inventory_little_long_r"] = "inventory_frame_little_long_r.png",
            ["inventory_little_off"] = "inventory_frame_little_off.png",
            ["inventory_little_ready"] = "inventory_frame_little_ready.png",
            ["inventory_little_vert"] = "inventory_frame_little_vert.png",
            ["name"] = "name_frame.png",                   ["name_ready"] = "name_frame_ready.png",
            ["name_mid_ready"] = "name_frame_mid_ready.png",
            ["longbutton"] = "longbutton_frame.png",       ["midbutton"] = "midbutton_frame.png",
            ["scroll"] = "scroll_frame.png",               ["skill"] = "skill_frame.png",
            ["thin"] = "Thin_frame.png",                   ["round"] = "Round_fr.png",
            ["little_round"] = "little_round_frame.png",   ["little_round2"] = "little_round_frame2.png",
            ["little_round2_elite"] = "little_round_frame2_elite.png",
            ["little_background"] = "little_background_frame.png",
            ["long_loading"] = "long_frame_loading.png",   ["update_empty"] = "update/Frame_empty.png",
        },
        ["bars"] = new()
        {
            ["big_bar"] = "big_bar.png",             ["big_bar_bg"] = "big_bar_bg.png",
            ["big_bar_frame"] = "big_bar_frame.png", ["basic"] = "basic_bar.png",
            ["basic2"] = "basic_bar2.png",           ["basic2_elite"] = "basic_bar2_elite.png",
            ["mid"] = "mid_bar.png",                 ["mid_frame"] = "mid_bar_frame.png",
            ["lil"] = "lil_bar.png",                 ["inventory"] = "Inventory_bar.png",
            ["skill"] = "skill_bar.png",             ["skill_bg"] = "skill_bar_background.png",
            ["red_vert_panel"] = "Red_vert_panel.png",
        },
        ["lines"] = new()
        {
            ["hp"] = "Hp_line.png",         ["hp2"] = "Hp_line2.png",       ["hp_frame"] = "Hp_frame.png",
            ["mana"] = "Mana_line.png",     ["mana2"] = "Mana_line2.png",
            ["energy"] = "energy_line.png", ["energy2"] = "energy_line2.png",
            ["venom"] = "Venom_line.png",   ["xp"] = "xp_line.png",
            ["xp_bar"] = "xp_bar.png",     ["xp_bg"] = "xp_background.png",
            ["long"] = "long_line.png",     ["long2"] = "long_line2.png",
            ["long2_elite"] = "long_line2_elite.png", ["option"] = "option_line.png",
        },
        ["portrait"] = new()
        {
            ["frame"] = "Hero_icon_frame.png",       ["frame_bg"] = "Hero_icon_frame_bg.png",
            ["frame_elite"] = "Hero_icon_frame_elite.png", ["frame_rare"] = "Hero_icon_frame_rare.png",
        },
        ["silhouettes"] = new()
        {
            ["warrior_man"] = "Silhouette/warrior_silhouette_man.png",
            ["warrior_woman"] = "Silhouette/warrior_silhouette_woman.png",
            ["mage_man"] = "Silhouette/Mage_silhouette_man.png",
            ["mage_woman"] = "Silhouette/Mage_silhouette_woman.png",
            ["archer_man"] = "Silhouette/archer_silhouette_man.png",
            ["archer_woman"] = "Silhouette/archer_silhouette_woman.png",
            ["assassin_man"] = "Silhouette/assassin_silhouette_man.png",
            ["assassin_woman"] = "Silhouette/assassin_silhouette_woman.png",
            ["berserk_man"] = "Silhouette/berserk_silhouette_man.png",
            ["berserk_woman"] = "Silhouette/berserk_silhouette_woman.png",
            ["druid_man"] = "Silhouette/druid_silhouette_man.png",
            ["druid_woman"] = "Silhouette/druid_silhouette_woman.png",
            ["shadowmage_man"] = "Silhouette/shadowmage_silhouette_man.png",
            ["shadowmage_woman"] = "Silhouette/shadowmage_silhouette_woman.png",
        },
        ["icons"] = new()
        {
            ["bag"] = "Icons/Bag.png",         ["equip"] = "Icons/Equip.png",
            ["fight"] = "Icons/Fight.png",     ["honor"] = "Icons/Honor.png",
            ["inventory"] = "Icons/Inventory.png", ["menu"] = "Icons/Menu.png",
            ["options"] = "Icons/Options.png", ["profession"] = "Icons/Profession.png",
            ["quest"] = "Icons/Quest.png",     ["scull"] = "Icons/Scull.png",
            ["scull2"] = "Icons/Scull2.png",   ["exit"] = "Icons/exit.png",
            ["greed"] = "Icons/greed.png",     ["need"] = "Icons/need.png",
            ["runes"] = "Icons/runes.png",     ["skills"] = "Icons/skills.png",
            ["trade"] = "Icons/trade.png",
        },
        ["slot_backgrounds"] = new()
        {
            ["rune"] = "frame_backgrounds/Rune_background.png",
            ["arrows"] = "frame_backgrounds/arrows_background.png",
            ["back"] = "frame_backgrounds/back_background.png",
            ["boots"] = "frame_backgrounds/boots_background.png",
            ["bracers"] = "frame_backgrounds/bracers_background.png",
            ["chest"] = "frame_backgrounds/chest_background.png",
            ["gloves"] = "frame_backgrounds/gloves_background.png",
            ["head"] = "frame_backgrounds/head_background.png",
            ["helm"] = "frame_backgrounds/helm_background.png",
            ["lock"] = "frame_backgrounds/lock_background.png",
            ["melee"] = "frame_backgrounds/melee_background.png",
            ["neck"] = "frame_backgrounds/neck_background.png",
            ["pants"] = "frame_backgrounds/pants_background.png",
            ["potion"] = "frame_backgrounds/potion_background.png",
            ["range"] = "frame_backgrounds/range_background.png",
            ["ring"] = "frame_backgrounds/ring_background.png",
            ["shield"] = "frame_backgrounds/shield_background.png",
            ["shoulder"] = "frame_backgrounds/shoulder_background.png",
            ["skill"] = "frame_backgrounds/skill_background.png",
            ["trinket"] = "frame_backgrounds/trinket_background.png",
            ["wand"] = "frame_backgrounds/wand_background.png",
        },
        ["backgrounds"] = new()
        {
            ["paper_01"] = "Paper_01.png", ["paper_02"] = "Paper_02.png",
            ["pattern"] = "pattern.png",   ["minimap"] = "minimap.png",
        },
        ["special_items"] = new()
        {
            ["book"] = "update/Book.png",        ["destruction"] = "update/Destruction.png",
            ["guitar"] = "update/Guitar.png",    ["ink"] = "update/Ink.png",
        },
    };

    // =========================================================================
    // CACHE
    // =========================================================================

    private static readonly Dictionary<string, Texture2D> _texCache = new();
    private static readonly Dictionary<string, ImageTexture> _scaledTexCache = new();

    // =========================================================================
    // CORE HELPERS
    // =========================================================================

    public static Texture2D GetTex(string filename)
    {
        if (!_texCache.TryGetValue(filename, out var tex))
        {
            string resPath = AssetsPath + filename;
            // Load PNG via Image.Load — works with res:// paths, no .import needed
            var img = new Image();
            var err = img.Load(resPath);
            if (err == Error.Ok)
            {
                tex = ImageTexture.CreateFromImage(img);
            }
            else
            {
                // Fallback: try absolute filesystem path
                string globalPath = ProjectSettings.GlobalizePath(resPath);
                err = img.Load(globalPath);
                if (err == Error.Ok)
                    tex = ImageTexture.CreateFromImage(img);
                else
                    GD.PrintErr($"[RpgTheme] FAILED to load: {resPath} (err={err})");
            }
            _texCache[filename] = tex!;
        }
        return tex!;
    }

    private static ImageTexture GetScaledTex(string filename, Vector2I targetSize)
    {
        var key = filename + targetSize.ToString();
        if (!_scaledTexCache.TryGetValue(key, out var tex))
        {
            var img = (Image)GetTex(filename).GetImage().Duplicate();
            img.Resize(targetSize.X, targetSize.Y, Image.Interpolation.Lanczos);
            tex = ImageTexture.CreateFromImage(img);
            _scaledTexCache[key] = tex;
        }
        return tex;
    }

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

    /// <summary>Get a texture from the asset catalog by category and name.</summary>
    public static Texture2D? GetAsset(string category, string assetName)
    {
        if (category == "mini_buttons" && MiniButtons.TryGetValue(assetName, out var mb))
            return GetTex(mb.Normal);
        if (Assets.TryGetValue(category, out var cat) && cat.TryGetValue(assetName, out var path))
            return GetTex(path);
        GD.PushWarning($"RpgTheme: asset not found: {category}/{assetName}");
        return null;
    }

    public static string GetAssetPath(string category, string assetName)
    {
        if (category == "mini_buttons" && MiniButtons.TryGetValue(assetName, out var mb))
            return mb.Normal;
        if (Assets.TryGetValue(category, out var cat) && cat.TryGetValue(assetName, out var path))
            return path;
        return "";
    }

    // =========================================================================
    // SIZE / ALIGNMENT HELPERS (chainable)
    // =========================================================================

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
