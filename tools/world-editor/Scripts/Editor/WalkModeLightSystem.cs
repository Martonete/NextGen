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

        // Advanced lights → ADDITIVE per-tile accumulator (NOT MAX-blend
        // toward ambient). MAX-blend silently zero-outs any channel where
        // the light color ≤ ambient — torches (B=180) against bright map
        // ambient (B=180) become invisible. Additive on top of the MAX-
        // blend ensures advanced lights always brighten the scene by their
        // Energy contribution, regardless of ambient.
        float[,] addR = new float[_mapWidth + 1, _mapHeight + 1];
        float[,] addG = new float[_mapWidth + 1, _mapHeight + 1];
        float[,] addB = new float[_mapWidth + 1, _mapHeight + 1];
        foreach (var ml in map.LightData.Lights)
        {
            if (ml.X < 1 || ml.Y < 1 || ml.X > _mapWidth || ml.Y > _mapHeight) continue;
            ApplyAdditiveLight(addR, addG, addB, ml);
        }

        // Final tile = own ambient + legacy-light excess (MAX-blend lift)
        //            + advanced-light additive contribution.
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
                    MathF.Min(1f, ambR[x, y] + MathF.Max(0, litAvgR - ambAvgR) + addR[x, y]),
                    MathF.Min(1f, ambG[x, y] + MathF.Max(0, litAvgG - ambAvgG) + addG[x, y]),
                    MathF.Min(1f, ambB[x, y] + MathF.Max(0, litAvgB - ambAvgB) + addB[x, y]));
            }
    }

    /// <summary>Quadratic-falloff additive contribution from one advanced
    /// light into a per-tile accumulator. Scales by <c>Energy</c> so the
    /// user's energy slider directly controls visible brightness above
    /// ambient. No shadows (VB6 client parity).</summary>
    private void ApplyAdditiveLight(
        float[,] addR, float[,] addG, float[,] addB, MapLight ml)
    {
        float energy = MathF.Max(0f, ml.Energy);
        if (energy <= 0f) return;
        float lr = ml.R / 255f * energy;
        float lg = ml.G / 255f * energy;
        float lb = ml.B / 255f * energy;
        float rangePx = ml.Radius * TileSize;
        if (rangePx <= 0f) return;
        float centerX = ml.X * TileSize + HalfTile;
        float centerY = ml.Y * TileSize + HalfTile;

        int rangeTiles = (int)MathF.Ceiling(ml.Radius) + 1;
        int x1 = Math.Max(1, ml.X - rangeTiles), x2 = Math.Min(_mapWidth, ml.X + rangeTiles);
        int y1 = Math.Max(1, ml.Y - rangeTiles), y2 = Math.Min(_mapHeight, ml.Y + rangeTiles);

        for (int ty = y1; ty <= y2; ty++)
            for (int tx = x1; tx <= x2; tx++)
            {
                // Sample at tile CENTER (matches DrawTileGrh coords used
                // for light positions: tileX*32 + 16).
                float dx = (tx * TileSize + HalfTile) - centerX;
                float dy = (ty * TileSize + HalfTile) - centerY;
                float dist = MathF.Sqrt(dx * dx + dy * dy);
                if (dist >= rangePx) continue;
                float t = dist / rangePx;
                // Quadratic falloff (1-t)^2 — stronger center, soft edge.
                // Matches the shader's `exp(-t²×3)` halo character roughly.
                float f = (1f - t) * (1f - t);
                addR[tx, ty] += lr * f;
                addG[tx, ty] += lg * f;
                addB[tx, ty] += lb * f;
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
