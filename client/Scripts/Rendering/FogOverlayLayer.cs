using Godot;
using ArgentumNextgen.Game;

namespace ArgentumNextgen.Rendering;

/// <summary>
/// Draws darkness over tiles outside the core 17x13 viewport.
/// The entire extra zone is solid dark (MaxAlpha). Only a thin ~1 tile
/// transition strip at the core boundary fades from transparent to dark,
/// creating a clean edge effect without revealing the tiles behind.
/// Only active when resolution > 800x600.
/// </summary>
public partial class FogOverlayLayer : Node2D
{
    private const float MaxAlpha = 0.55f;
    // Transition width in design pixels (~1 tile = 32px, at 1/4 res = 8 texels)
    private const float TransitionPx = 32f;
    private ImageTexture? _fogTex;
    private int _cachedVpW, _cachedVpH;

    public override void _Draw()
    {
        int extraX = ResolutionManager.ExtraTilesX;
        int extraY = ResolutionManager.ExtraTilesY;
        if (extraX <= 0 && extraY <= 0) return;

        int vpW = ResolutionManager.ViewportW;
        int vpH = ResolutionManager.ViewportH;

        if (_fogTex == null || _cachedVpW != vpW || _cachedVpH != vpH)
        {
            _fogTex = BuildFogTexture(vpW, vpH);
            _cachedVpW = vpW;
            _cachedVpH = vpH;
        }

        DrawTextureRect(_fogTex, new Rect2(0, 0, vpW, vpH), false);
    }

    /// <summary>
    /// Build fog texture: transparent inside core, thin gradient at core edge,
    /// then flat MaxAlpha everywhere else. Built at 1/4 resolution.
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
        // Transition width in texture pixels (1/4 of design pixels)
        float transition = TransitionPx / 4f;

        var img = Image.CreateEmpty(texW, texH, false, Image.Format.Rgba8);

        for (int py = 0; py < texH; py++)
        {
            for (int px = 0; px < texW; px++)
            {
                // Distance from the core edge (0 = inside core, >0 = outside)
                float dx = System.Math.Max(0, System.Math.Abs(px - centerX) - halfCoreW);
                float dy = System.Math.Max(0, System.Math.Abs(py - centerY) - halfCoreH);
                float dist = System.Math.Max(dx, dy);

                float alpha;
                if (dist <= 0f)
                {
                    // Inside core: fully transparent
                    alpha = 0f;
                }
                else if (dist < transition)
                {
                    // Transition strip: smooth ramp from 0 → MaxAlpha
                    float t = dist / transition;
                    // Smooth-step for clean transition
                    t = t * t * (3f - 2f * t);
                    alpha = t * MaxAlpha;
                }
                else
                {
                    // Beyond transition: solid dark
                    alpha = MaxAlpha;
                }

                img.SetPixel(px, py, new Color(0, 0, 0, alpha));
            }
        }

        return ImageTexture.CreateFromImage(img);
    }
}
