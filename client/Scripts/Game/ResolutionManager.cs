using System;
using Godot;

namespace ArgentumNextgen.Game;

/// <summary>
/// Manages dynamic resolution. UI elements scale proportionally via UIScale.
/// The game viewport fills the remaining space with tile-aligned dimensions.
/// At 800x600: UIScale=1.0, everything pixel-identical to original.
/// </summary>
public static class ResolutionManager
{
    // Design constants (800x600 base)
    public const int DesignWidth = 800;
    public const int DesignHeight = 600;
    public const int DesignLeftMargin = 13;
    public const int DesignTopMargin = 149;
    public const int DesignSidebarWidth = 240;
    public const int DesignBottomBarHeight = 35;
    public const int DesignSidebarGap = 3;
    public const int DesignViewportW = 544; // 17*32
    public const int DesignViewportH = 416; // 13*32
    public const int CoreHalfX = 8;
    public const int CoreHalfY = 6;
    public const int TileSize = 32;

    // ── UIScale: scales ALL UI elements (sidebar, labels, fonts, margins) ──
    // Based on height ratio for balanced growth (height is the tighter constraint)
    public static float UIScale { get; private set; } = 1.0f;

    /// <summary>Scale a design-space pixel value by UIScale.</summary>
    public static int S(int designValue) => (int)(designValue * UIScale);
    public static float Sf(float designValue) => designValue * UIScale;

    // ── Computed layout values (in actual pixels) ──
    public static int WindowWidth { get; private set; } = DesignWidth;
    public static int WindowHeight { get; private set; } = DesignHeight;

    // Scaled margins
    public static int LeftMargin { get; private set; } = DesignLeftMargin;
    public static int TopMargin { get; private set; } = DesignTopMargin;
    public static int ActualSidebarWidth { get; private set; } = DesignSidebarWidth;
    public static int ActualBottomBarHeight { get; private set; } = DesignBottomBarHeight;

    // Viewport (tile-aligned, fills remaining space)
    public static int ViewportW { get; private set; } = DesignViewportW;
    public static int ViewportH { get; private set; } = DesignViewportH;
    public static int ViewportPixelW => ViewportW;
    public static int ViewportPixelH => ViewportH;
    public static int TilesX { get; private set; } = 17;
    public static int TilesY { get; private set; } = 13;
    public static int RenderTilesX => TilesX;
    public static int RenderTilesY => TilesY;
    public static int HalfTilesX { get; private set; } = CoreHalfX;
    public static int HalfTilesY { get; private set; } = CoreHalfY;
    public static int HalfRenderTilesX => HalfTilesX;
    public static int HalfRenderTilesY => HalfTilesY;
    public static int HalfRenderX => HalfTilesX;
    public static int HalfRenderY => HalfTilesY;
    public static int ExtraTilesX { get; private set; } = 0;
    public static int ExtraTilesY { get; private set; } = 0;

    // UI anchor positions
    public static int SidebarX { get; private set; } = 560;
    public static int BottomBarY { get; private set; } = 565;
    public static int ConsoleRight { get; private set; } = 547;

    public static Action? OnResolutionChanged;

    public static void ApplyResolution(int width, int height)
    {
        WindowWidth = width;
        WindowHeight = height;

        // UIScale based on height (more balanced than width for AO's layout)
        UIScale = height / (float)DesignHeight;

        // Scale margins and sidebar
        LeftMargin = S(DesignLeftMargin);
        TopMargin = S(DesignTopMargin);
        ActualSidebarWidth = S(DesignSidebarWidth);
        ActualBottomBarHeight = S(DesignBottomBarHeight);
        int sidebarGap = S(DesignSidebarGap);

        // Viewport fills remaining space, tile-aligned
        int availW = width - ActualSidebarWidth - LeftMargin - sidebarGap;
        int availH = height - TopMargin - ActualBottomBarHeight;
        TilesX = availW / TileSize;
        TilesY = availH / TileSize;
        if (TilesX % 2 == 0) TilesX--;
        if (TilesY % 2 == 0) TilesY--;

        // Shrink margins slightly to fit +2 tiles when leftover is close to 2 tiles
        int leftoverH = availH - TilesY * TileSize;
        int extraNeededH = 2 * TileSize - leftoverH;
        int minTopMargin = S(128); // console area minimum (~128 design px)
        if (extraNeededH > 0 && TopMargin - extraNeededH >= minTopMargin)
        {
            TilesY += 2;
            TopMargin -= extraNeededH;
        }

        int leftoverW = availW - TilesX * TileSize;
        int extraNeededW = 2 * TileSize - leftoverW;
        int minLeftMargin = Math.Max(4, S(4));
        if (extraNeededW > 0 && LeftMargin - extraNeededW >= minLeftMargin)
        {
            TilesX += 2;
            LeftMargin -= extraNeededW;
        }

        TilesX = Math.Max(17, TilesX);
        TilesY = Math.Max(13, TilesY);

        ViewportW = TilesX * TileSize;
        ViewportH = TilesY * TileSize;
        HalfTilesX = TilesX / 2;
        HalfTilesY = TilesY / 2;
        ExtraTilesX = (TilesX - 17) / 2;
        ExtraTilesY = (TilesY - 13) / 2;

        // Anchor positions
        SidebarX = LeftMargin + ViewportW + sidebarGap;
        BottomBarY = TopMargin + ViewportH;
        ConsoleRight = SidebarX - sidebarGap - S(10);

        // Resize window
        DisplayServer.WindowSetSize(new Vector2I(width, height));
        var screen = DisplayServer.ScreenGetSize();
        DisplayServer.WindowSetPosition(new Vector2I(
            Math.Max(0, (screen.X - width) / 2),
            Math.Max(0, (screen.Y - height) / 2)));

        GD.Print($"[RES] {width}x{height} UIScale={UIScale:F2} " +
                 $"viewport={ViewportW}x{ViewportH} tiles={TilesX}x{TilesY} " +
                 $"sidebar={ActualSidebarWidth}px@{SidebarX} extra={ExtraTilesX},{ExtraTilesY}");

        OnResolutionChanged?.Invoke();
    }

    public static readonly (int w, int h, string label)[] Presets = new[]
    {
        (800, 600, "800x600"),
        (1024, 768, "1024x768"),
        (1152, 864, "1152x864"),
        (1280, 720, "1280x720"),
        (1280, 960, "1280x960"),
        (1366, 768, "1366x768"),
        (1600, 900, "1600x900"),
        (1920, 1080, "1920x1080"),
    };
}
