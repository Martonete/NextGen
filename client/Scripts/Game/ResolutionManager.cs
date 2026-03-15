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
	public const int DesignViewportX = 13;   // SubViewportContainer X
	public const int DesignViewportY = 149;  // SubViewportContainer Y
	public const int DesignViewportW = 544;  // 17 tiles * 32
	public const int DesignViewportH = 416;  // 13 tiles * 32

	// Core visible tile area (server-synced, never changes)
	public const int CoreTilesX = 17; // same as VB6
	public const int CoreTilesY = 13;

	public static float ScaleFactor { get; private set; } = 1.0f;

	// Extra tiles visible at current resolution (outside the core 17x13)
	public static int ExtraTilesX { get; private set; } = 0;
	public static int ExtraTilesY { get; private set; } = 0;

	// Total tiles rendered at current resolution
	public static int RenderTilesX => CoreTilesX + ExtraTilesX * 2;
	public static int RenderTilesY => CoreTilesY + ExtraTilesY * 2;

	// Half render tiles from center (used by WorldRenderer for frame bounds)
	public static int HalfRenderTilesX => (RenderTilesX - 1) / 2;
	public static int HalfRenderTilesY => (RenderTilesY - 1) / 2;

	/// <summary>
	/// Apply a new resolution: resize the window, compute scale factor,
	/// and calculate how many extra tiles are visible.
	/// </summary>
	public static void ApplyResolution(int width, int height)
	{
		// Set window size and center on screen
		var windowSize = new Vector2I(width, height);
		DisplayServer.WindowSetSize(windowSize);

		var screenSize = DisplayServer.ScreenGetSize();
		int posX = Math.Max(0, (screenSize.X - width) / 2);
		int posY = Math.Max(0, (screenSize.Y - height) / 2);
		DisplayServer.WindowSetPosition(new Vector2I(posX, posY));

		ScaleFactor = width / (float)DesignWidth;

		// Calculate extra tiles visible at this resolution.
		// The SubViewportContainer scales proportionally with the window.
		// At 800x600: viewport = 544x416 = 17x13 tiles
		// At 1600x1200 (2x): viewport area = 1088x832 pixels = 34x26 tiles
		float vpPixelsW = DesignViewportW * ScaleFactor;
		float vpPixelsH = DesignViewportH * ScaleFactor;
		int totalTilesX = (int)(vpPixelsW / 32) + 1;
		int totalTilesY = (int)(vpPixelsH / 32) + 1;

		// Ensure total tiles are odd (so there's a center tile)
		if (totalTilesX % 2 == 0) totalTilesX++;
		if (totalTilesY % 2 == 0) totalTilesY++;

		ExtraTilesX = Math.Max(0, (totalTilesX - CoreTilesX) / 2);
		ExtraTilesY = Math.Max(0, (totalTilesY - CoreTilesY) / 2);

		GD.Print($"[RESOLUTION] Applied {width}x{height} (scale={ScaleFactor:F2}) " +
				 $"extra={ExtraTilesX}x{ExtraTilesY} render={RenderTilesX}x{RenderTilesY}");
	}
}
