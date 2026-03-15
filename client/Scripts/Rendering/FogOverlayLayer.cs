using Godot;
using ArgentumNextgen.Game;

namespace ArgentumNextgen.Rendering;

/// <summary>
/// Draws a smooth darkness overlay on tiles outside the core 17x13 viewport.
/// Uses a precomputed gradient texture for smooth edges (no blocky tiles).
/// Radial gradient with ease-out curve — near-black at edges, transparent at core.
/// Only active when resolution > 800x600.
/// </summary>
public partial class FogOverlayLayer : Node2D
{
    private const float MaxAlpha = 0.92f;
    private ImageTexture? _fogTex;
    private int _cachedVpW, _cachedVpH;

    public override void _Draw()
    {
        int extraX = ResolutionManager.ExtraTilesX;
        int extraY = ResolutionManager.ExtraTilesY;
        if (extraX <= 0 && extraY <= 0) return;

        int vpW = ResolutionManager.ViewportW;
        int vpH = ResolutionManager.ViewportH;

        // Rebuild texture if viewport changed
        if (_fogTex == null || _cachedVpW != vpW || _cachedVpH != vpH)
        {
            _fogTex = BuildFogTexture(vpW, vpH);
            _cachedVpW = vpW;
            _cachedVpH = vpH;
        }

        DrawTextureRect(_fogTex, new Rect2(0, 0, vpW, vpH), false);
    }

    /// <summary>
    /// Build a smooth radial fog texture. Fully transparent inside the core
    /// 544x416 area, smooth ease-out gradient to MaxAlpha at the viewport edges.
    /// Built at 1/4 resolution — GPU bilinear filtering smooths it.
    /// Uses radial (Euclidean) distance for natural circular falloff.
    /// </summary>
    private static ImageTexture BuildFogTexture(int vpW, int vpH)
    {
        int texW = vpW / 4;
        int texH = vpH / 4;
        int coreW = 544 / 4;  // 136
        int coreH = 416 / 4;  // 104
        float centerX = texW / 2f;
        float centerY = texH / 2f;
        float halfCoreW = coreW / 2f;
        float halfCoreH = coreH / 2f;

        // Fog zone dimensions (from core edge to viewport edge)
        float fogZoneW = centerX - halfCoreW;
        float fogZoneH = centerY - halfCoreH;

        var img = Image.CreateEmpty(texW, texH, false, Image.Format.Rgba8);

        for (int py = 0; py < texH; py++)
        {
            for (int px = 0; px < texW; px++)
            {
                // Distance from the core edge (0 = inside core, >0 = outside)
                float dx = System.Math.Max(0, System.Math.Abs(px - centerX) - halfCoreW);
                float dy = System.Math.Max(0, System.Math.Abs(py - centerY) - halfCoreH);

                // Normalized distance using radial (Euclidean) for smooth circular falloff
                float fx = fogZoneW > 0 ? dx / fogZoneW : 0;
                float fy = fogZoneH > 0 ? dy / fogZoneH : 0;
                float f = System.MathF.Sqrt(fx * fx + fy * fy);
                f = System.Math.Clamp(f, 0f, 1f);

                // Ease-out curve: starts fast, slows near full darkness
                // f² gives a more aggressive darkening near the core edge
                float alpha = f * f * MaxAlpha;
                img.SetPixel(px, py, new Color(0, 0, 0, alpha));
            }
        }

        return ImageTexture.CreateFromImage(img);
    }
}
