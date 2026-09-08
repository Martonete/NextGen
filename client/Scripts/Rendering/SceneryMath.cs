using System;

namespace ArgentumNextgen.Rendering;

public static class SceneryMath
{
    // Spatial fade starts just before the canopy covers the player's torso.
    // Coordinates are world pixels, independent of camera clamping and shake.
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

    public static float WindOffset(double seconds, int tileX, int tileY, float height)
    {
        double phase = tileX * 0.43 + tileY * 0.29;
        double gust = Math.Sin(seconds * 1.15 + phase) * 0.7
            + Math.Sin(seconds * 1.93 + phase * 1.7) * 0.3;
        return (float)gust * Math.Clamp(height * 0.012f, 0f, 2.6f);
    }
}
