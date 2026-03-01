using System;
using Godot;
using TierrasSagradasAO.Game;

namespace TierrasSagradasAO.Game;

/// <summary>
/// Per-vertex tile lighting system matching VB6 clsLight.cls.
/// Calculates a Color for each tile based on distance to light sources.
/// Multiple lights: brightest per-channel wins (max, not additive).
/// </summary>
public class LightSystem
{
    private const int MapSize = 100;
    private const int TileSize = 32;

    /// <summary>
    /// Recalculate tile light colors for all active lights.
    /// Stores per-tile average color in TileLightColors[x, y, 0].
    /// For v1 simplicity we compute a single color per tile (average of 4 corners).
    /// </summary>
    public void RecalculateLights(GameState state)
    {
        // Allocate grid if needed (1-indexed, 101x101, 4 corners but we use index 0 for average)
        if (state.TileLightColors == null)
            state.TileLightColors = new Color[MapSize + 1, MapSize + 1, 1];

        var ambient = state.AmbientLightColor;
        var grid = state.TileLightColors;

        // Reset all tiles to ambient
        for (int y = 1; y <= MapSize; y++)
            for (int x = 1; x <= MapSize; x++)
                grid[x, y, 0] = ambient;

        // Apply each active light
        foreach (var light in state.MapLights)
        {
            if (!light.Active || light.Range <= 0) continue;

            float lightR = light.R / 255f;
            float lightG = light.G / 255f;
            float lightB = light.B / 255f;

            int rangePx = light.Range * TileSize;
            int minX = Math.Max(1, light.X - light.Range);
            int maxX = Math.Min(MapSize, light.X + light.Range);
            int minY = Math.Max(1, light.Y - light.Range);
            int maxY = Math.Min(MapSize, light.Y + light.Range);

            float lightCenterX = light.X * TileSize + TileSize / 2f;
            float lightCenterY = light.Y * TileSize + TileSize / 2f;

            for (int ty = minY; ty <= maxY; ty++)
            {
                for (int tx = minX; tx <= maxX; tx++)
                {
                    // Tile center pixel position
                    float tileCX = tx * TileSize + TileSize / 2f;
                    float tileCY = ty * TileSize + TileSize / 2f;

                    float dx = tileCX - lightCenterX;
                    float dy = tileCY - lightCenterY;
                    float dist = MathF.Sqrt(dx * dx + dy * dy);

                    float factor = Math.Clamp(dist / rangePx, 0f, 1f);

                    // Lerp from light color to ambient by factor
                    float r = lightR + (ambient.R - lightR) * factor;
                    float g = lightG + (ambient.G - lightG) * factor;
                    float b = lightB + (ambient.B - lightB) * factor;

                    // Max per-channel (multiple lights → brightest wins)
                    var current = grid[tx, ty, 0];
                    grid[tx, ty, 0] = new Color(
                        Math.Max(current.R, r),
                        Math.Max(current.G, g),
                        Math.Max(current.B, b),
                        1f
                    );
                }
            }
        }
    }

    /// <summary>
    /// Get the light color for a specific tile. Returns white if no lighting data.
    /// </summary>
    public static Color GetTileLight(GameState state, int x, int y)
    {
        if (state.TileLightColors == null) return Colors.White;
        if (x < 1 || x > MapSize || y < 1 || y > MapSize) return Colors.White;
        return state.TileLightColors[x, y, 0];
    }
}
