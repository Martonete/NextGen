#nullable enable
using System;
using System.Collections.Generic;
using AOWorldEditor.Data;

namespace AOWorldEditor.Editor;

/// <summary>
/// Port of the client's roof handling (WorldRenderer.BuildRoofRegions + RoofFadeState):
/// connected groups of L4/indoor-trigger tiles are one "region", and only the region
/// the player stands in fades out. WalkModePanel's single global fade is not faithful —
/// it dims every roof on screen at once — so "Vista de juego" uses this instead.
/// </summary>
public sealed class RoofRegions
{
    private static readonly (int dx, int dy)[] FloodDirs = { (1, 0), (-1, 0), (0, 1), (0, -1) };

    private int[,]? _region; // 1-based, 0 = not a roof tile
    private float[] _opacity = Array.Empty<float>();

    public bool IsBuilt => _region != null;

    public static bool IsRoofTrigger(short trigger) => trigger == 1 || trigger == 2 || trigger == 4;

    public void Invalidate()
    {
        _region = null;
        _opacity = Array.Empty<float>();
    }

    public void Build(MapData map)
    {
        int w = map.Width, h = map.Height;
        _region = new int[w + 1, h + 1];
        int next = 0;
        var queue = new Queue<(int x, int y)>();

        for (int y = 1; y <= h; y++)
            for (int x = 1; x <= w; x++)
            {
                if (_region[x, y] != 0) continue;
                ref var tile = ref map.Tiles[x, y];
                if (!IsRoofTrigger(tile.Trigger) && tile.Layer4 <= 0) continue;

                next++;
                _region[x, y] = next;
                queue.Enqueue((x, y));
                while (queue.Count > 0)
                {
                    var (cx, cy) = queue.Dequeue();
                    foreach (var (dx, dy) in FloodDirs)
                    {
                        int nx = cx + dx, ny = cy + dy;
                        if (nx < 1 || nx > w || ny < 1 || ny > h || _region[nx, ny] != 0) continue;
                        ref var nt = ref map.Tiles[nx, ny];
                        if (!IsRoofTrigger(nt.Trigger) && nt.Layer4 <= 0) continue;
                        _region[nx, ny] = next;
                        queue.Enqueue((nx, ny));
                    }
                }
            }

        _opacity = new float[next + 1];
        Array.Fill(_opacity, 1f);
    }

    public int RegionAt(int x, int y)
        => _region != null && x >= 1 && y >= 1 && x < _region.GetLength(0) && y < _region.GetLength(1)
            ? _region[x, y] : 0;

    public float GetOpacity(int region)
        => region > 0 && region < _opacity.Length ? _opacity[region] : 1f;

    /// <summary>Client numbers: ease at 400/255 per second toward 45/255 for the active region, 1 for the rest.</summary>
    public void Update(int activeRegion, float seconds)
    {
        float step = Math.Max(0f, seconds) * (400f / 255f);
        for (int region = 1; region < _opacity.Length; region++)
        {
            float target = region == activeRegion ? 45f / 255f : 1f;
            _opacity[region] += Math.Clamp(target - _opacity[region], -step, step);
        }
    }
}
