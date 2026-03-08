using System;
using Godot;
using ArgentumNextgen.Game;

namespace ArgentumNextgen.Game;

/// <summary>
/// Per-vertex tile lighting matching VB6 clsLight.cls exactly.
/// Each tile has 4 corner colors (light_value[0..3]).
/// VB6 corner layout:
///   light_value(0) = SW corner (x*32,     y*32+32)
///   light_value(1) = NW corner (x*32,     y*32)
///   light_value(2) = SE corner (x*32+32,  y*32+32)
///   light_value(3) = NE corner (x*32+32,  y*32)
///
/// VB6 LightCalculate: D3DXColorLerp(LightColor, AmbientColor, dist/range).
/// When dist > range, returns the existing tile value (no change).
/// Multiple lights: last one written wins (VB6 doesn't do max — the commented
/// line `If TileLight > LightCalculate` is disabled in the VB6 source).
///
/// For Godot rendering, we average the 4 corners into a single modulate color
/// since DrawTextureRectRegion doesn't support per-vertex colors.
/// </summary>
public class LightSystem
{
    private const int MapSize = 100;
    private const int TileSize = 32;

    /// <summary>
    /// Recalculate tile light colors for all active lights.
    /// TileLightColors[x, y, 0..3] = 4 corner colors per tile.
    /// When lights are active, WorldRenderer.Modulate is set to white so
    /// the light system controls ALL coloring (ambient + light brightening).
    /// </summary>
    public void RecalculateLights(GameState state)
    {
        // Allocate grid if needed (1-indexed, 101x101, 4 corners)
        if (state.TileLightColors == null || state.TileLightColors.GetLength(2) != 4)
            state.TileLightColors = new Color[MapSize + 1, MapSize + 1, 4];

        var grid = state.TileLightColors;

        // Use the map's actual RGB as ambient. When lights are active,
        // WorldRenderer.Modulate = white, so the light system handles all tinting.
        // Unlit tiles get the map ambient; lit tiles are brighter via MAX blending.
        float AmbR = state.MapColorR / 255f;
        float AmbG = state.MapColorG / 255f;
        float AmbB = state.MapColorB / 255f;
        var ambient = new Color(AmbR, AmbG, AmbB, 1f);

        // Initialize all tiles to AMBIENT (not black).
        // In VB6, unlit tiles use base_light which is the ambient color.
        // This prevents black tiles at light edges and discontinuities.
        for (int y = 1; y <= MapSize; y++)
            for (int x = 1; x <= MapSize; x++)
                for (int c = 0; c < 4; c++)
                    grid[x, y, c] = ambient;

        // Apply each active light (VB6: LightRender per light)
        foreach (var light in state.MapLights)
        {
            if (!light.Active || light.Range <= 0) continue;

            float lightR = light.R / 255f;
            float lightG = light.G / 255f;
            float lightB = light.B / 255f;

            int rangePx = light.Range * TileSize;
            float lightPxX = light.X * TileSize;
            float lightPxY = light.Y * TileSize;

            int minX = light.X - light.Range;
            int maxX = light.X + light.Range;
            int minY = light.Y - light.Range;
            int maxY = light.Y + light.Range;

            for (int ty = minY; ty <= maxY; ty++)
            {
                for (int tx = minX; tx <= maxX; tx++)
                {
                    if (tx < 1 || tx > MapSize || ty < 1 || ty > MapSize) continue;

                    // VB6 corner positions and their light_value indices:
                    // light_value(1) = NW: (tx*32,     ty*32)
                    // light_value(3) = NE: (tx*32+32,  ty*32)
                    // light_value(0) = SW: (tx*32,     ty*32+32)
                    // light_value(2) = SE: (tx*32+32,  ty*32+32)
                    float tileX = tx * TileSize;
                    float tileY = ty * TileSize;

                    // Corner 1 (NW) → index 1
                    grid[tx, ty, 1] = CalcCorner(lightPxX, lightPxY, tileX, tileY,
                        rangePx, grid[tx, ty, 1], lightR, lightG, lightB, AmbR, AmbG, AmbB);

                    // Corner 3 (NE) → index 3
                    grid[tx, ty, 3] = CalcCorner(lightPxX, lightPxY, tileX + TileSize, tileY,
                        rangePx, grid[tx, ty, 3], lightR, lightG, lightB, AmbR, AmbG, AmbB);

                    // Corner 0 (SW) → index 0
                    grid[tx, ty, 0] = CalcCorner(lightPxX, lightPxY, tileX, tileY + TileSize,
                        rangePx, grid[tx, ty, 0], lightR, lightG, lightB, AmbR, AmbG, AmbB);

                    // Corner 2 (SE) → index 2
                    grid[tx, ty, 2] = CalcCorner(lightPxX, lightPxY, tileX + TileSize, tileY + TileSize,
                        rangePx, grid[tx, ty, 2], lightR, lightG, lightB, AmbR, AmbG, AmbB);
                }
            }
        }
    }

    /// <summary>
    /// VB6 LightCalculate: lerp from light color to ambient by distance factor.
    /// If dist > range, return existing value unchanged.
    /// Light center uses (lightX + 16, lightY + 16) — center of the light's tile.
    /// Uses MAX blending: lights can only brighten above existing value.
    /// </summary>
    private static Color CalcCorner(
        float lightX, float lightY,
        float cornerX, float cornerY,
        int rangePx,
        Color existing,
        float lightR, float lightG, float lightB,
        float ambR, float ambG, float ambB)
    {
        // VB6: XDist = LightX + 16 - XCoord
        float dx = (lightX + 16f) - cornerX;
        float dy = (lightY + 16f) - cornerY;
        float dist = MathF.Sqrt(dx * dx + dy * dy);

        if (dist > rangePx)
            return existing; // VB6: returns TileLight unchanged

        float factor = dist / rangePx;

        // Lerp from LightColor (close) to map ambient (far)
        float r = lightR + (ambR - lightR) * factor;
        float g = lightG + (ambG - lightG) * factor;
        float b = lightB + (ambB - lightB) * factor;

        // MAX blending: lights can only brighten above existing value.
        r = Math.Max(existing.R, r);
        g = Math.Max(existing.G, g);
        b = Math.Max(existing.B, b);

        return new Color(r, g, b, 1f);
    }

    /// <summary>
    /// Get the average light color for a tile (average of 4 corners).
    /// Used for PASS 3 objects/trees that need per-tile modulate (not covered by GPU shader).
    /// </summary>
    public static Color GetTileLight(GameState state, int x, int y)
    {
        if (state.TileLightColors == null) return Colors.White;
        if (x < 1 || x > MapSize || y < 1 || y > MapSize) return Colors.White;

        var c0 = state.TileLightColors[x, y, 0];
        var c1 = state.TileLightColors[x, y, 1];
        var c2 = state.TileLightColors[x, y, 2];
        var c3 = state.TileLightColors[x, y, 3];

        float r = (c0.R + c1.R + c2.R + c3.R) * 0.25f;
        float g = (c0.G + c1.G + c2.G + c3.G) * 0.25f;
        float b = (c0.B + c1.B + c2.B + c3.B) * 0.25f;

        return new Color(r, g, b, 1f);
    }

    /// <summary>
    /// Build a 101×101 lightmap Image from TileLightColors.
    /// Each pixel represents a tile corner. GPU bilinear interpolation
    /// between corner pixels produces smooth per-vertex-equivalent lighting.
    ///
    /// Pixel layout:
    ///   pixel(x, y) = NW corner of tile (x+1, y+1)
    ///   Right edge (x=100): NE corner of tile (100, y+1)
    ///   Bottom edge (y=100): SW corner of tile (x+1, 100)
    ///   Corner (100,100): SE corner of tile (100, 100)
    /// </summary>
    public static Image BuildLightmapImage(Color[,,] tileLightColors)
    {
        const int Size = MapSize + 1; // 101
        var img = Image.CreateEmpty(Size, Size, false, Image.Format.Rgb8);

        for (int py = 0; py < Size; py++)
        {
            for (int px = 0; px < Size; px++)
            {
                Color c;
                if (px < MapSize && py < MapSize)
                {
                    // NW corner of tile (px+1, py+1) = index 1
                    c = tileLightColors[px + 1, py + 1, 1];
                }
                else if (px == MapSize && py < MapSize)
                {
                    // Right edge: NE corner of tile (100, py+1) = index 3
                    c = tileLightColors[MapSize, py + 1, 3];
                }
                else if (px < MapSize && py == MapSize)
                {
                    // Bottom edge: SW corner of tile (px+1, 100) = index 0
                    c = tileLightColors[px + 1, MapSize, 0];
                }
                else
                {
                    // Corner (100,100): SE corner of tile (100, 100) = index 2
                    c = tileLightColors[MapSize, MapSize, 2];
                }

                img.SetPixel(px, py, new Color(c.R, c.G, c.B, 1f));
            }
        }

        return img;
    }
}
