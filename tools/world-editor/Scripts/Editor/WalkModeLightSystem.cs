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

        // Step 2: Light sources — use corner grid for smooth radial gradients
        // Only tiles near lights get their color modified via 4-corner averaging.
        bool hasLights = false;
        for (int y = 1; y <= _mapHeight && !hasLights; y++)
            for (int x = 1; x <= _mapWidth && !hasLights; x++)
                hasLights = map.Tiles[x, y].HasLight;

        if (!hasLights) return; // No lights → ambient-only, already set

        float[,] cR = new float[_mapWidth + 2, _mapHeight + 2];
        float[,] cG = new float[_mapWidth + 2, _mapHeight + 2];
        float[,] cB = new float[_mapWidth + 2, _mapHeight + 2];

        // Initialize corners from per-tile ambient
        for (int cy = 1; cy <= _mapHeight + 1; cy++)
            for (int cx = 1; cx <= _mapWidth + 1; cx++)
            {
                int tx = Math.Min(cx, _mapWidth), ty = Math.Min(cy, _mapHeight);
                cR[cx, cy] = ambR[tx, ty]; cG[cx, cy] = ambG[tx, ty]; cB[cx, cy] = ambB[tx, ty];
            }

        // Apply lights (exact CalcCorner from client LightSystem)
        for (int ly = 1; ly <= _mapHeight; ly++)
            for (int lx = 1; lx <= _mapWidth; lx++)
            {
                ref var tile = ref map.Tiles[lx, ly];
                if (!tile.HasLight) continue;

                float lr = tile.LightR / 255f, lg = tile.LightG / 255f, lb = tile.LightB / 255f;
                float rangePx = tile.LightRange * TileSize;
                float centerX = lx * TileSize + HalfTile;
                float centerY = ly * TileSize + HalfTile;

                int range = tile.LightRange + 1;
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

                        cR[cx, cy] = MathF.Max(cR[cx, cy], lr + (ar - lr) * f);
                        cG[cx, cy] = MathF.Max(cG[cx, cy], lg + (ag - lg) * f);
                        cB[cx, cy] = MathF.Max(cB[cx, cy], lb + (ab - lb) * f);
                    }
            }

        // Average 4 corners, then MAX with tile's own ambient.
        // This ensures zone ambient is never shifted by corner averaging,
        // while lights still create smooth radial gradients.
        for (int y = 1; y <= _mapHeight; y++)
            for (int x = 1; x <= _mapWidth; x++)
            {
                float lr = (cR[x, y] + cR[x + 1, y] + cR[x, y + 1] + cR[x + 1, y + 1]) * 0.25f;
                float lg = (cG[x, y] + cG[x + 1, y] + cG[x, y + 1] + cG[x + 1, y + 1]) * 0.25f;
                float lb = (cB[x, y] + cB[x + 1, y] + cB[x, y + 1] + cB[x + 1, y + 1]) * 0.25f;
                _tileColors[x, y] = new Color(
                    MathF.Max(ambR[x, y], lr),
                    MathF.Max(ambG[x, y], lg),
                    MathF.Max(ambB[x, y], lb));
            }
    }

    public Color GetTileLight(int x, int y)
    {
        if (_tileColors == null || x < 1 || y < 1 || x > _mapWidth || y > _mapHeight)
            return Colors.White;
        return _tileColors[x, y];
    }
}
