#nullable enable
using AOWorldEditor.Data;
using Godot;
using System;

namespace AOWorldEditor.Editor;

/// <summary>
/// CPU-based per-tile lighting for walk mode. Produces a Color per tile
/// used as modulate in DrawTextureRectRegion calls. Does NOT depend on
/// Godot's GPU 2D lighting pipeline (PointLight2D/CanvasModulate),
/// which doesn't work reliably in sub-Window contexts.
///
/// Algorithm is an exact port of client LightSystem.cs:
/// - Per-tile zone-aware ambient (each tile uses its zone's RGB or map default)
/// - 4-corner light interpolation with MAX blending
/// - Averaged to single Color per tile
/// </summary>
public class WalkModeLightSystem
{
    private const int TileSize = 32;
    private const float HalfTile = TileSize / 2f;

    private Color[,]? _tileColors;
    private int _mapWidth, _mapHeight;

    public void Recalculate(MapData map, MapZoneData? zones)
    {
        _mapWidth = map.Width;
        _mapHeight = map.Height;
        _tileColors = new Color[_mapWidth + 1, _mapHeight + 1];

        // Step 1: Per-tile ambient — direct from zone or map default (no interpolation)
        float[,] ambR = new float[_mapWidth + 1, _mapHeight + 1];
        float[,] ambG = new float[_mapWidth + 1, _mapHeight + 1];
        float[,] ambB = new float[_mapWidth + 1, _mapHeight + 1];

        for (int y = 1; y <= _mapHeight; y++)
            for (int x = 1; x <= _mapWidth; x++)
            {
                float r = map.AmbientR, g = map.AmbientG, b = map.AmbientB;
                if (zones != null)
                {
                    var zone = zones.GetZoneAt(x, y);
                    if (zone != null && (zone.AmbientR > 0 || zone.AmbientG > 0 || zone.AmbientB > 0))
                    { r = zone.AmbientR; g = zone.AmbientG; b = zone.AmbientB; }
                }
                ambR[x, y] = r / 255f; ambG[x, y] = g / 255f; ambB[x, y] = b / 255f;
                // Initialize tile color to its own ambient (no corner shift)
                _tileColors[x, y] = new Color(ambR[x, y], ambG[x, y], ambB[x, y]);
            }

        // Step 2: Light sources — two grids (ambient-only and with-lights)
        // to isolate light contribution from zone ambient bleeding.
        bool hasLegacy = false;
        for (int y = 1; y <= _mapHeight && !hasLegacy; y++)
            for (int x = 1; x <= _mapWidth && !hasLegacy; x++)
                hasLegacy = map.Tiles[x, y].HasLight;
        bool hasAdvanced = map.LightData.Lights.Count > 0;
        if (!hasLegacy && !hasAdvanced) return; // ambient-only

        // Grid A: corners with ambient only (for measuring baseline bleed)
        // Grid B: corners with ambient + lights
        float[,] aR = new float[_mapWidth + 2, _mapHeight + 2];
        float[,] aG = new float[_mapWidth + 2, _mapHeight + 2];
        float[,] aB = new float[_mapWidth + 2, _mapHeight + 2];
        float[,] lR = new float[_mapWidth + 2, _mapHeight + 2];
        float[,] lG = new float[_mapWidth + 2, _mapHeight + 2];
        float[,] lB = new float[_mapWidth + 2, _mapHeight + 2];

        for (int cy = 1; cy <= _mapHeight + 1; cy++)
            for (int cx = 1; cx <= _mapWidth + 1; cx++)
            {
                int tx = Math.Min(cx, _mapWidth), ty = Math.Min(cy, _mapHeight);
                float r = ambR[tx, ty], g = ambG[tx, ty], b = ambB[tx, ty];
                aR[cx, cy] = r; aG[cx, cy] = g; aB[cx, cy] = b;
                lR[cx, cy] = r; lG[cx, cy] = g; lB[cx, cy] = b;
            }

        // Apply legacy per-tile lights to grid B
        for (int ly = 1; ly <= _mapHeight; ly++)
            for (int lx = 1; lx <= _mapWidth; lx++)
            {
                ref var tile = ref map.Tiles[lx, ly];
                if (!tile.HasLight) continue;
                ApplyLight(ambR, ambG, ambB, lR, lG, lB,
                    lx, ly,
                    tile.LightR / 255f, tile.LightG / 255f, tile.LightB / 255f,
                    tile.LightRange);
            }

        // Apply advanced lights (MapData.LightData) the same way. No shadows
        // in walk mode — matches VB6 client parity. Energy scales color.
        foreach (var ml in map.LightData.Lights)
        {
            if (ml.X < 1 || ml.Y < 1 || ml.X > _mapWidth || ml.Y > _mapHeight) continue;
            float energy = MathF.Max(0f, ml.Energy);
            float lr = Math.Clamp(ml.R / 255f * energy, 0f, 1f);
            float lg = Math.Clamp(ml.G / 255f * energy, 0f, 1f);
            float lb = Math.Clamp(ml.B / 255f * energy, 0f, 1f);
            int range = (int)MathF.Ceiling(ml.Radius);
            if (range < 1) range = 1;
            ApplyLight(ambR, ambG, ambB, lR, lG, lB, ml.X, ml.Y, lr, lg, lb, range);
        }

        // Final tile = own ambient + light excess (corner avg WITH lights - corner avg WITHOUT lights).
        // This eliminates zone ambient bleeding from corner averaging while keeping smooth light gradients.
        for (int y = 1; y <= _mapHeight; y++)
            for (int x = 1; x <= _mapWidth; x++)
            {
                float ambAvgR = (aR[x, y] + aR[x + 1, y] + aR[x, y + 1] + aR[x + 1, y + 1]) * 0.25f;
                float ambAvgG = (aG[x, y] + aG[x + 1, y] + aG[x, y + 1] + aG[x + 1, y + 1]) * 0.25f;
                float ambAvgB = (aB[x, y] + aB[x + 1, y] + aB[x, y + 1] + aB[x + 1, y + 1]) * 0.25f;
                float litAvgR = (lR[x, y] + lR[x + 1, y] + lR[x, y + 1] + lR[x + 1, y + 1]) * 0.25f;
                float litAvgG = (lG[x, y] + lG[x + 1, y] + lG[x, y + 1] + lG[x + 1, y + 1]) * 0.25f;
                float litAvgB = (lB[x, y] + lB[x + 1, y] + lB[x, y + 1] + lB[x + 1, y + 1]) * 0.25f;

                _tileColors[x, y] = new Color(
                    MathF.Min(1f, ambR[x, y] + MathF.Max(0, litAvgR - ambAvgR)),
                    MathF.Min(1f, ambG[x, y] + MathF.Max(0, litAvgG - ambAvgG)),
                    MathF.Min(1f, ambB[x, y] + MathF.Max(0, litAvgB - ambAvgB)));
            }
    }

    public Color GetTileLight(int x, int y)
    {
        if (_tileColors == null || x < 1 || y < 1 || x > _mapWidth || y > _mapHeight)
            return Colors.White;
        return _tileColors[x, y];
    }

    /// <summary>Apply one light source to the corner-grid B using the same
    /// linear-falloff MAX-blend the client LightSystem uses. Shared between
    /// legacy per-tile lights and advanced MapLight sources so both render
    /// VB6-faithfully in walk mode (no shadows — that matches the client).</summary>
    private void ApplyLight(
        float[,] ambR, float[,] ambG, float[,] ambB,
        float[,] lR, float[,] lG, float[,] lB,
        int lx, int ly, float lr, float lg, float lb, int rangeTiles)
    {
        float rangePx = rangeTiles * TileSize;
        float centerX = lx * TileSize + HalfTile;
        float centerY = ly * TileSize + HalfTile;
        int range = rangeTiles + 1;
        int x1 = Math.Max(1, lx - range), x2 = Math.Min(_mapWidth + 1, lx + range + 1);
        int y1 = Math.Max(1, ly - range), y2 = Math.Min(_mapHeight + 1, ly + range + 1);

        for (int cy = y1; cy <= y2; cy++)
            for (int cx = x1; cx <= x2; cx++)
            {
                float dx = cx * TileSize - centerX;
                float dy = cy * TileSize - centerY;
                float dist = MathF.Sqrt(dx * dx + dy * dy);
                if (dist > rangePx) continue;
                float f = dist / rangePx;
                int atx = Math.Clamp(cx, 1, _mapWidth), aty = Math.Clamp(cy, 1, _mapHeight);
                float ar = ambR[atx, aty], ag = ambG[atx, aty], ab = ambB[atx, aty];
                lR[cx, cy] = MathF.Max(lR[cx, cy], lr + (ar - lr) * f);
                lG[cx, cy] = MathF.Max(lG[cx, cy], lg + (ag - lg) * f);
                lB[cx, cy] = MathF.Max(lB[cx, cy], lb + (ab - lb) * f);
            }
    }
}
