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
    private const int TileSize = 32;

    /// <summary>
    /// Recalculate tile light colors for all active lights.
    /// TileLightColors[x, y, 0..3] = 4 corner colors per tile.
    /// When lights are active, WorldRenderer.Modulate is set to white so
    /// the light system controls ALL coloring (ambient + light brightening).
    /// </summary>
    public void RecalculateLights(GameState state)
    {
        int mapW = state.MapData?.Width ?? 100;
        int mapH = state.MapData?.Height ?? 100;

        // Use the map's actual RGB as ambient. When lights are active,
        // WorldRenderer.Modulate = white, so the light system handles all tinting.
        // Unlit tiles get the map ambient; lit tiles are brighter via MAX blending.
        float AmbR = state.MapColorR / 255f;
        float AmbG = state.MapColorG / 255f;
        float AmbB = state.MapColorB / 255f;
        var ambient = new Color(AmbR, AmbG, AmbB, 1f);

        // Allocate chunked grid if needed, reset all allocated chunks to ambient
        if (state.TileLightColors == null)
            state.TileLightColors = new ChunkedLightColors();
        state.TileLightColors.ResetAll(ambient);

        var grid = state.TileLightColors;

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

            int minX = Math.Max(1, light.X - light.Range);
            int maxX = Math.Min(mapW, light.X + light.Range);
            int minY = Math.Max(1, light.Y - light.Range);
            int maxY = Math.Min(mapH, light.Y + light.Range);

            for (int ty = minY; ty <= maxY; ty++)
            {
                for (int tx = minX; tx <= maxX; tx++)
                {
                    // VB6 corner positions and their light_value indices:
                    // light_value(1) = NW: (tx*32,     ty*32)
                    // light_value(3) = NE: (tx*32+32,  ty*32)
                    // light_value(0) = SW: (tx*32,     ty*32+32)
                    // light_value(2) = SE: (tx*32+32,  ty*32+32)
                    float tileX = tx * TileSize;
                    float tileY = ty * TileSize;

                    // Corner 1 (NW) → index 1
                    grid.Set(tx, ty, 1, CalcCorner(lightPxX, lightPxY, tileX, tileY,
                        rangePx, grid.Get(tx, ty, 1), lightR, lightG, lightB, AmbR, AmbG, AmbB));

                    // Corner 3 (NE) → index 3
                    grid.Set(tx, ty, 3, CalcCorner(lightPxX, lightPxY, tileX + TileSize, tileY,
                        rangePx, grid.Get(tx, ty, 3), lightR, lightG, lightB, AmbR, AmbG, AmbB));

                    // Corner 0 (SW) → index 0
                    grid.Set(tx, ty, 0, CalcCorner(lightPxX, lightPxY, tileX, tileY + TileSize,
                        rangePx, grid.Get(tx, ty, 0), lightR, lightG, lightB, AmbR, AmbG, AmbB));

                    // Corner 2 (SE) → index 2
                    grid.Set(tx, ty, 2, CalcCorner(lightPxX, lightPxY, tileX + TileSize, tileY + TileSize,
                        rangePx, grid.Get(tx, ty, 2), lightR, lightG, lightB, AmbR, AmbG, AmbB));
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
        int mapW = state.MapData?.Width ?? 100;
        int mapH = state.MapData?.Height ?? 100;
        if (x < 1 || x > mapW || y < 1 || y > mapH) return Colors.White;

        var c0 = state.TileLightColors.Get(x, y, 0);
        var c1 = state.TileLightColors.Get(x, y, 1);
        var c2 = state.TileLightColors.Get(x, y, 2);
        var c3 = state.TileLightColors.Get(x, y, 3);

        float r = (c0.R + c1.R + c2.R + c3.R) * 0.25f;
        float g = (c0.G + c1.G + c2.G + c3.G) * 0.25f;
        float b = (c0.B + c1.B + c2.B + c3.B) * 0.25f;

        return new Color(r, g, b, 1f);
    }

    /// <summary>
    /// Build a (W+1)x(H+1) lightmap Image from TileLightColors.
    /// Each pixel represents a tile corner. GPU bilinear interpolation
    /// between corner pixels produces smooth per-vertex-equivalent lighting.
    ///
    /// Pixel layout:
    ///   pixel(x, y) = NW corner of tile (x+1, y+1)
    ///   Right edge (x=W): NE corner of tile (W, y+1)
    ///   Bottom edge (y=H): SW corner of tile (x+1, H)
    ///   Corner (W,H): SE corner of tile (W, H)
    /// </summary>
    public static Image BuildLightmapImage(ChunkedLightColors tileLightColors, int mapWidth, int mapHeight)
    {
        int sizeX = mapWidth + 1;
        int sizeY = mapHeight + 1;
        var img = Image.CreateEmpty(sizeX, sizeY, false, Image.Format.Rgb8);

        for (int py = 0; py < sizeY; py++)
        {
            for (int px = 0; px < sizeX; px++)
            {
                Color c;
                if (px < mapWidth && py < mapHeight)
                {
                    // NW corner of tile (px+1, py+1) = index 1
                    c = tileLightColors.Get(px + 1, py + 1, 1);
                }
                else if (px == mapWidth && py < mapHeight)
                {
                    // Right edge: NE corner of tile (W, py+1) = index 3
                    c = tileLightColors.Get(mapWidth, py + 1, 3);
                }
                else if (px < mapWidth && py == mapHeight)
                {
                    // Bottom edge: SW corner of tile (px+1, H) = index 0
                    c = tileLightColors.Get(px + 1, mapHeight, 0);
                }
                else
                {
                    // Corner (W,H): SE corner of tile (W, H) = index 2
                    c = tileLightColors.Get(mapWidth, mapHeight, 2);
                }

                img.SetPixel(px, py, new Color(c.R, c.G, c.B, 1f));
            }
        }

        return img;
    }
}
