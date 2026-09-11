#nullable enable
using System;

namespace AOWorldEditor.Editor;

/// <summary>
/// Verbatim port of client/Scripts/Rendering/SceneryMath.TreeOpacity so "Vista de
/// juego" fades a canopy exactly where the real client would.
/// </summary>
public static class SceneryMath
{
    // Spatial fade starts just before the canopy covers the player's torso.
    // Coordinates are world pixels.
    public static float TreeOpacity(float left, float top, float width, float height,
        float playerX, float playerY, bool foreground, float minimum)
    {
        if (!foreground || width <= 0 || height <= 0) return 1f;
        float edge = Math.Min(Math.Min(playerX - left, left + width - playerX),
            Math.Min(playerY - top, top + height - playerY));
        float t = Math.Clamp((edge + 16f) / 32f, 0f, 1f);
        t = t * t * (3f - 2f * t);
        return 1f - (1f - Math.Clamp(minimum, 0.2f, 1f)) * t;
    }
}
