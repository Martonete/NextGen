using System;
using Godot;

namespace ArgentumNextgen.Game;

/// <summary>
/// Static utility class that manages resolution changes and computes
/// how many extra tiles are visible beyond the core 17x13 VB6 viewport.
/// The core area is always the server-synced 17x13 tile window; extra
/// tiles at higher resolutions are darkened by a fog overlay.
/// </summary>
public static class ResolutionManager
{
	// Design resolution (always 800x600)
	public const int DesignWidth = 800;
	public const int DesignHeight = 600;

	// Design viewport (game render area in design space)
	public const int DesignViewportW = 544;  // 17 tiles * 32
	public const int DesignViewportH = 416;  // 13 tiles * 32

	// Core visible tile area (server-synced, never changes)
	public const int CoreTilesX = 17;
	public const int CoreTilesY = 13;
	public const int CoreHalfX = 8;  // (17-1)/2
	public const int CoreHalfY = 6;  // (13-1)/2

	public static float ScaleFactor { get; private set; } = 1.0f;

	// Extra tiles visible at current resolution (outside the core 17x13)
	public static int ExtraTilesX { get; private set; } = 0;
	public static int ExtraTilesY { get; private set; } = 0;

	// Total tiles rendered at current resolution
	public static int RenderTilesX => CoreTilesX + ExtraTilesX * 2;
	public static int RenderTilesY => CoreTilesY + ExtraTilesY * 2;

	// Half render tiles from center (used by WorldRenderer for frame bounds)
	public static int HalfRenderTilesX => CoreHalfX + ExtraTilesX;
	public static int HalfRenderTilesY => CoreHalfY + ExtraTilesY;

	// Callback to resize SubViewport when resolution changes
	public static Action<int, int>? OnSubViewportResize;

	/// <summary>
	/// Apply a new resolution: resize the window, compute scale factor,
	/// calculate extra tiles, and resize the SubViewport.
	/// </summary>
	public static void ApplyResolution(int width, int height)
	{
		// Set window size and center on screen
		DisplayServer.WindowSetSize(new Vector2I(width, height));
		var screenSize = DisplayServer.ScreenGetSize();
		int posX = Math.Max(0, (screenSize.X - width) / 2);
		int posY = Math.Max(0, (screenSize.Y - height) / 2);
		DisplayServer.WindowSetPosition(new Vector2I(posX, posY));

		ScaleFactor = width / (float)DesignWidth;

		// At 800x600 (scale=1.0): viewport = 544x416 = exactly 17x13 tiles → 0 extra
		// At 1920x1080 (scale=2.4): viewport = 1305x998 pixels → ~40x31 tiles → 12x9 extra
		float vpPixelsW = DesignViewportW * ScaleFactor;
		float vpPixelsH = DesignViewportH * ScaleFactor;
		int totalTilesX = (int)(vpPixelsW / 32);
		int totalTilesY = (int)(vpPixelsH / 32);

		// Ensure odd (center tile)
		if (totalTilesX % 2 == 0) totalTilesX--;
		if (totalTilesY % 2 == 0) totalTilesY--;

		// At 800x600: totalTilesX = 544/32 = 17, extra = (17-17)/2 = 0 ✓
		ExtraTilesX = Math.Max(0, (totalTilesX - CoreTilesX) / 2);
		ExtraTilesY = Math.Max(0, (totalTilesY - CoreTilesY) / 2);

		GD.Print($"[RESOLUTION] Applied {width}x{height} (scale={ScaleFactor:F2}) " +
				 $"extra={ExtraTilesX}x{ExtraTilesY} render={RenderTilesX}x{RenderTilesY}");

		// Resize SubViewport to render the correct number of tiles
		int svpW = RenderTilesX * 32;
		int svpH = RenderTilesY * 32;
		OnSubViewportResize?.Invoke(svpW, svpH);
	}
}
