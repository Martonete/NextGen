using System;
using Godot;

namespace ArgentumNextgen.Game;

/// <summary>
/// Manages dynamic resolution from 800x600 (default) to 1920x1080.
/// No Godot stretch mode — the window is the actual size and all UI
/// positions are scaled by ScaleFactor. The SubViewport grows to show
/// more tiles at higher resolutions.
/// </summary>
public static class ResolutionManager
{
	// Design resolution (base for all position calculations)
	public const int DesignWidth = 800;
	public const int DesignHeight = 600;

	// Design viewport (game render area at 800x600)
	public const int DesignViewportX = 13;
	public const int DesignViewportY = 149;
	public const int DesignViewportW = 544;  // 17 * 32
	public const int DesignViewportH = 416;  // 13 * 32

	// Core visible tile area (server-synced, never changes)
	public const int CoreTilesX = 17;
	public const int CoreTilesY = 13;
	public const int CoreHalfX = 8;
	public const int CoreHalfY = 6;

	/// <summary>Current scale factor (1.0 at 800x600).</summary>
	public static float Scale { get; private set; } = 1.0f;

	/// <summary>Current window width.</summary>
	public static int WindowWidth { get; private set; } = DesignWidth;
	/// <summary>Current window height.</summary>
	public static int WindowHeight { get; private set; } = DesignHeight;

	// Extra tiles visible beyond the core 17x13
	public static int ExtraTilesX { get; private set; } = 0;
	public static int ExtraTilesY { get; private set; } = 0;

	public static int RenderTilesX => CoreTilesX + ExtraTilesX * 2;
	public static int RenderTilesY => CoreTilesY + ExtraTilesY * 2;
	public static int HalfRenderTilesX => CoreHalfX + ExtraTilesX;
	public static int HalfRenderTilesY => CoreHalfY + ExtraTilesY;

	// Actual SubViewport pixel size
	public static int ViewportPixelW => RenderTilesX * 32;
	public static int ViewportPixelH => RenderTilesY * 32;

	// Actual SubViewportContainer position and size (scaled from design)
	public static int ContainerX => S(DesignViewportX);
	public static int ContainerY => S(DesignViewportY);
	public static int ContainerW => S(DesignViewportW);
	public static int ContainerH => S(DesignViewportH);

	/// <summary>Callback to rebuild the entire UI after resolution change.</summary>
	public static Action? OnResolutionChanged;

	/// <summary>Scale a design-space value to current resolution.</summary>
	public static int S(float designValue) => (int)(designValue * Scale);

	/// <summary>Scale a design-space value to current resolution (float).</summary>
	public static float Sf(float designValue) => designValue * Scale;

	/// <summary>
	/// Apply a resolution. Resizes the window, computes scale factor and
	/// extra tiles, then fires OnResolutionChanged for UI rebuild.
	/// </summary>
	public static void ApplyResolution(int width, int height)
	{
		WindowWidth = width;
		WindowHeight = height;
		Scale = width / (float)DesignWidth;

		// Resize OS window and center on screen
		DisplayServer.WindowSetSize(new Vector2I(width, height));
		CenterWindow(width, height);

		// Resize the root viewport to match actual window size.
		// Without this, Godot's internal viewport stays at 800x600 (from project.godot)
		// and FULL_RECT anchored Controls only fill 800x600 inside the larger window.
		var tree = Engine.GetMainLoop() as SceneTree;
		if (tree?.Root != null)
		{
			tree.Root.Size = new Vector2I(width, height);
			// Also set content scale so UI coordinate space matches pixel space
			tree.Root.ContentScaleSize = new Vector2I(0, 0); // disable content scaling
		}

		// Calculate extra tiles at this resolution
		// At 800x600 (scale=1.0): container=544x416, tiles=17x13, extra=0
		// At 1920x1080 (scale=2.4): container=1305x998, tiles=40x31, extra=~12x9
		int containerW = S(DesignViewportW);
		int containerH = S(DesignViewportH);
		int totalTilesX = containerW / 32;
		int totalTilesY = containerH / 32;

		// Ensure odd (center tile)
		if (totalTilesX % 2 == 0) totalTilesX--;
		if (totalTilesY % 2 == 0) totalTilesY--;
		if (totalTilesX < CoreTilesX) totalTilesX = CoreTilesX;
		if (totalTilesY < CoreTilesY) totalTilesY = CoreTilesY;

		ExtraTilesX = (totalTilesX - CoreTilesX) / 2;
		ExtraTilesY = (totalTilesY - CoreTilesY) / 2;

		GD.Print($"[RESOLUTION] {width}x{height} scale={Scale:F2} " +
				 $"extra={ExtraTilesX}x{ExtraTilesY} render={RenderTilesX}x{RenderTilesY} " +
				 $"container={containerW}x{containerH} viewport={ViewportPixelW}x{ViewportPixelH}");

		OnResolutionChanged?.Invoke();
	}

	/// <summary>Center the window on the primary monitor.</summary>
	private static void CenterWindow(int w, int h)
	{
		var screen = DisplayServer.ScreenGetSize();
		int px = Math.Max(0, (screen.X - w) / 2);
		int py = Math.Max(0, (screen.Y - h) / 2);
		DisplayServer.WindowSetPosition(new Vector2I(px, py));
	}
}
