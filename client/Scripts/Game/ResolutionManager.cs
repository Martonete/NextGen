using System;
using Godot;

namespace ArgentumNextgen.Game;

/// <summary>
/// Manages dynamic resolution from 800x600 (default) to 1920x1080.
/// Uses Godot canvas_items stretch for UI scaling. The SubViewport is
/// resized independently to render more tiles at higher resolutions.
/// </summary>
public static class ResolutionManager
{
	public const int DesignWidth = 800;
	public const int DesignHeight = 600;
	public const int DesignViewportW = 544;
	public const int DesignViewportH = 416;

	public const int CoreTilesX = 17;
	public const int CoreTilesY = 13;
	public const int CoreHalfX = 8;
	public const int CoreHalfY = 6;

	public static float Scale { get; private set; } = 1.0f;
	public static int WindowWidth { get; private set; } = DesignWidth;
	public static int WindowHeight { get; private set; } = DesignHeight;

	public static int ExtraTilesX { get; private set; } = 0;
	public static int ExtraTilesY { get; private set; } = 0;

	public static int RenderTilesX => CoreTilesX + ExtraTilesX * 2;
	public static int RenderTilesY => CoreTilesY + ExtraTilesY * 2;
	public static int HalfRenderTilesX => CoreHalfX + ExtraTilesX;
	public static int HalfRenderTilesY => CoreHalfY + ExtraTilesY;

	public static int ViewportPixelW => RenderTilesX * 32;
	public static int ViewportPixelH => RenderTilesY * 32;

	public static int S(float v) => (int)(v * Scale);
	public static float Sf(float v) => v * Scale;

	public static Action? OnResolutionChanged;

	public static void ApplyResolution(int width, int height)
	{
		WindowWidth = width;
		WindowHeight = height;
		Scale = width / (float)DesignWidth;

		// Resize OS window and center on screen
		DisplayServer.WindowSetSize(new Vector2I(width, height));
		var screen = DisplayServer.ScreenGetSize();
		DisplayServer.WindowSetPosition(new Vector2I(
			Math.Max(0, (screen.X - width) / 2),
			Math.Max(0, (screen.Y - height) / 2)));

		// Tell Godot the new content scale size so canvas_items stretch
		// maps the 800x600 design space to the actual window size.
		// This is what makes ALL UI scale automatically.
		var tree = Engine.GetMainLoop() as SceneTree;
		if (tree?.Root != null)
			tree.Root.ContentScaleSize = new Vector2I(DesignWidth, DesignHeight);

		// Calculate extra tiles at this resolution
		int actualW = (int)(DesignViewportW * Scale);
		int actualH = (int)(DesignViewportH * Scale);
		int totalTilesX = actualW / 32;
		int totalTilesY = actualH / 32;
		if (totalTilesX % 2 == 0) totalTilesX--;
		if (totalTilesY % 2 == 0) totalTilesY--;
		if (totalTilesX < CoreTilesX) totalTilesX = CoreTilesX;
		if (totalTilesY < CoreTilesY) totalTilesY = CoreTilesY;

		ExtraTilesX = (totalTilesX - CoreTilesX) / 2;
		ExtraTilesY = (totalTilesY - CoreTilesY) / 2;

		GD.Print($"[RESOLUTION] {width}x{height} scale={Scale:F2} " +
				 $"extra={ExtraTilesX}x{ExtraTilesY} render={RenderTilesX}x{RenderTilesY}");

		OnResolutionChanged?.Invoke();
	}
}
