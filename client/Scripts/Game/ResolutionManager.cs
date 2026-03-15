using System;
using Godot;

namespace ArgentumNextgen.Game;

/// <summary>
/// Manages dynamic resolution for the game client.
/// Computes viewport size (tile-aligned), sidebar/bottom bar positions,
/// and console width based on the current window dimensions.
/// At 800x600 everything is pixel-identical to the original VB6 layout.
/// </summary>
public static class ResolutionManager
{
    // Fixed layout constants (pixels)
    public const int LeftMargin = 13;
    public const int TopMargin = 149;
    public const int SidebarWidth = 240;
    public const int BottomBarHeight = 35;
    public const int SidebarGap = 3; // gap between viewport right edge and sidebar

    // Design defaults (800x600)
    public const int DesignWidth = 800;
    public const int DesignHeight = 600;
    public const int DesignViewportW = 544; // 17*32
    public const int DesignViewportH = 416; // 13*32
    public const int DesignSidebarX = 560;  // 13 + 544 + 3
    public const int CoreHalfX = 8;
    public const int CoreHalfY = 6;
    public const int TileSize = 32;

    // Current computed values
    public static int WindowWidth { get; private set; } = DesignWidth;
    public static int WindowHeight { get; private set; } = DesignHeight;
    public static int ViewportW { get; private set; } = DesignViewportW;
    public static int ViewportH { get; private set; } = DesignViewportH;
    public static int TilesX { get; private set; } = 17;
    public static int TilesY { get; private set; } = 13;
    public static int HalfTilesX { get; private set; } = CoreHalfX;
    public static int HalfTilesY { get; private set; } = CoreHalfY;
    public static int ExtraTilesX { get; private set; } = 0;
    public static int ExtraTilesY { get; private set; } = 0;

    // UI anchor positions (computed from viewport)
    public static int SidebarX { get; private set; } = DesignSidebarX;
    public static int BottomBarY { get; private set; } = 565; // 149 + 416
    public static int ConsoleRight { get; private set; } = 547; // SidebarX - SidebarGap - 10

    /// <summary>Fires after ApplyResolution completes. UI elements should reposition.</summary>
    public static event Action? OnResolutionChanged;

    /// <summary>
    /// Compute layout metrics for a given window size and resize the OS window.
    /// </summary>
    public static void ApplyResolution(int width, int height)
    {
        WindowWidth = width;
        WindowHeight = height;

        // Calculate viewport size (fill space between margins, tile-aligned)
        int availW = width - SidebarWidth - LeftMargin - SidebarGap;
        int availH = height - TopMargin - BottomBarHeight;
        TilesX = availW / TileSize;
        TilesY = availH / TileSize;
        if (TilesX % 2 == 0) TilesX--; // must be odd for center tile
        if (TilesY % 2 == 0) TilesY--;
        TilesX = Math.Max(17, TilesX); // minimum = design size
        TilesY = Math.Max(13, TilesY);

        ViewportW = TilesX * TileSize;
        ViewportH = TilesY * TileSize;
        HalfTilesX = TilesX / 2;
        HalfTilesY = TilesY / 2;
        ExtraTilesX = (TilesX - 17) / 2;
        ExtraTilesY = (TilesY - 13) / 2;

        // Calculate anchor positions
        SidebarX = LeftMargin + ViewportW + SidebarGap;
        BottomBarY = TopMargin + ViewportH;
        ConsoleRight = SidebarX - SidebarGap - 10;

        // Resize window
        DisplayServer.WindowSetSize(new Vector2I(width, height));
        var screen = DisplayServer.ScreenGetSize();
        DisplayServer.WindowSetPosition(new Vector2I(
            Math.Max(0, (screen.X - width) / 2),
            Math.Max(0, (screen.Y - height) / 2)));

        GD.Print($"[RES] Applied {width}x{height}: viewport={ViewportW}x{ViewportH} " +
                 $"tiles={TilesX}x{TilesY} sidebar@{SidebarX} bottomBar@{BottomBarY}");

        OnResolutionChanged?.Invoke();
    }

    /// <summary>
    /// Available resolution presets. Each entry is (width, height, label).
    /// </summary>
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
