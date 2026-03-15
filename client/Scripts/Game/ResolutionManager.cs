using System;
using Godot;

namespace ArgentumNextgen.Game;

/// <summary>
/// Manages dynamic resolution scaling. The window grows to the selected resolution,
/// Godot canvas_items stretch scales the 800x600 UI, and the SubViewport renders
/// more tiles to fill the scaled game area.
///
/// At 800x600 (Scale=1.0): identical to original (17x13 tiles, 544x416 SubViewport).
/// At higher resolutions: SubViewport grows, WorldRenderer draws more tiles,
/// fog overlay darkens the extra tiles at the edges.
/// </summary>
public static class ResolutionManager
{
	public const int DesignWidth = 800;
	public const int DesignHeight = 600;

	// Core viewport constants (VB6 original: 17x13 tiles in 544x416 pixels)
	public const int CoreTilesX = 17;
	public const int CoreTilesY = 13;
	public const int CoreHalfX = 8;  // (17-1)/2
	public const int CoreHalfY = 6;  // (13-1)/2

	/// <summary>Current scale factor (1.0 at 800x600).</summary>
	public static float Scale { get; private set; } = 1.0f;

	/// <summary>Extra tiles on EACH side beyond the core 17x13.</summary>
	public static int ExtraTilesX { get; private set; }

	/// <summary>Extra tiles on EACH side beyond the core 17x13.</summary>
	public static int ExtraTilesY { get; private set; }

	/// <summary>Total tiles rendered horizontally (17 + 2 * ExtraTilesX).</summary>
	public static int RenderTilesX => CoreTilesX + ExtraTilesX * 2;

	/// <summary>Total tiles rendered vertically (13 + 2 * ExtraTilesY).</summary>
	public static int RenderTilesY => CoreTilesY + ExtraTilesY * 2;

	/// <summary>Half render width in tiles (for centering).</summary>
	public static int HalfRenderX => RenderTilesX / 2;

	/// <summary>Half render height in tiles (for centering).</summary>
	public static int HalfRenderY => RenderTilesY / 2;

	/// <summary>SubViewport pixel width (RenderTilesX * 32).</summary>
	public static int ViewportPixelW => RenderTilesX * 32;

	/// <summary>SubViewport pixel height (RenderTilesY * 32).</summary>
	public static int ViewportPixelH => RenderTilesY * 32;

	/// <summary>Fired after resolution values are recalculated.</summary>
	public static Action? OnResolutionChanged;

	/// <summary>
	/// Compute resolution parameters and resize the window.
	/// The SubViewportContainer at 544x416 design space is scaled by Godot
	/// to 544*Scale x 416*Scale actual pixels. The SubViewport renders at
	/// ViewportPixelW x ViewportPixelH to fill that space with 32px tiles.
	/// </summary>
	public static void ApplyResolution(int width, int height)
	{
		Scale = width / (float)DesignWidth;

		// Resize window
		DisplayServer.WindowSetSize(new Vector2I(width, height));

		// Center on screen
		var screen = DisplayServer.ScreenGetSize();
		DisplayServer.WindowSetPosition(new Vector2I(
			Math.Max(0, (screen.X - width) / 2),
			Math.Max(0, (screen.Y - height) / 2)));

		// The SubViewportContainer at 544x416 design space is scaled to actual pixels
		int actualW = (int)(544 * Scale);
		int actualH = (int)(416 * Scale);

		// How many 32px tiles fit in the scaled area (must be odd for centering)
		int totalTilesX = actualW / 32;
		int totalTilesY = actualH / 32;
		if (totalTilesX % 2 == 0) totalTilesX--;
		if (totalTilesY % 2 == 0) totalTilesY--;
		totalTilesX = Math.Max(CoreTilesX, totalTilesX);
		totalTilesY = Math.Max(CoreTilesY, totalTilesY);

		ExtraTilesX = (totalTilesX - CoreTilesX) / 2;
		ExtraTilesY = (totalTilesY - CoreTilesY) / 2;

		GD.Print($"[RES] Applied {width}x{height} Scale={Scale:F2} " +
				 $"Tiles={RenderTilesX}x{RenderTilesY} Extra={ExtraTilesX},{ExtraTilesY} " +
				 $"VP={ViewportPixelW}x{ViewportPixelH}");

		OnResolutionChanged?.Invoke();
	}
}
