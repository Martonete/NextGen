using System;

namespace ArgentumNextgen.Game;

/// <summary>
/// View ranges used by the in-game renderer.
///
/// The AO Libre renderer uses three independent rectangles: the core used for
/// interaction/runtime objects, a slightly larger creature range, and the
/// visibly clear area in the peripheral-darkness shader. Values are scaled
/// from its 1452x987 RenderScreen; they do not alter window resolution.
/// </summary>
public static class VisionRange
{
    // Reference viewport and ranges from ArgentumOnlineGodot/aolibre
    // engine/map_container.gd. They are ratios, never a requested resolution.
    private const float ReferenceViewportW = 1452f;
    private const float ReferenceViewportH = 987f;
    private const float ReferenceCoreW = 765f;
    private const float ReferenceCoreH = 637f;
    private const float ReferenceCreatureW = 829f;
    private const float ReferenceCreatureH = 733f;
    private const float ReferenceFogW = 1452f * (0.6423f - 0.3577f);
    private const float ReferenceFogH = 987f * (0.8166f - 0.1832f);

    public static int CoreWidth => ScaleX(ReferenceCoreW);
    public static int CoreHeight => ScaleY(ReferenceCoreH);

    public static int CreatureWidth => ScaleX(ReferenceCreatureW);
    public static int CreatureHeight => ScaleY(ReferenceCreatureH);

    // This intentionally differs from CoreWidth. AO Libre's peripheral fog
    // shader uses its own central UV rectangle (0.3577..0.6423 / 0.1832..0.8166).
    public static int FogWidth => ScaleX(ReferenceFogW);
    public static int FogHeight => ScaleY(ReferenceFogH);

    /// <summary>
    /// True when a character tile is inside the larger creature rectangle.
    /// This preserves AO Libre's deliberate vertical asymmetry: the rectangle
    /// begins one tile above the core and has its full configured height.
    /// </summary>
    public static bool IsInsideCreatureView(int charX, int charY, int userX, int userY)
    {
        // VB6 RenderScreen does not fade or clip creatures to a central area.
        if (ResolutionManager.FullscreenWorld)
        {
            int dx = Math.Abs(charX - userX);
            int dy = Math.Abs(charY - userY);
            return dx <= ResolutionManager.HalfTilesX + 1
                && dy <= ResolutionManager.HalfTilesY + 1;
        }

        float x = ResolutionManager.ViewportW * 0.5f
                  + (charX - userX) * ResolutionManager.TileSize;
        float y = ResolutionManager.ViewportH * 0.5f
                  + (charY - userY) * ResolutionManager.TileSize;
        float left = (ResolutionManager.ViewportW - CreatureWidth) * 0.5f;
        float top = (ResolutionManager.ViewportH - CoreHeight) * 0.5f
                    - ResolutionManager.TileSize;
        return x >= left && x < left + CreatureWidth
            && y >= top && y < top + CreatureHeight;
    }

    private static int ScaleX(float referencePixels)
        => Math.Clamp((int)MathF.Round(referencePixels * ResolutionManager.ViewportW / ReferenceViewportW),
            1, ResolutionManager.ViewportW);

    private static int ScaleY(float referencePixels)
        => Math.Clamp((int)MathF.Round(referencePixels * ResolutionManager.ViewportH / ReferenceViewportH),
            1, ResolutionManager.ViewportH);
}
