#nullable enable
using System.Collections.Generic;
using AOWorldEditor.Data;
using Godot;

namespace AOWorldEditor.Editor;

/// <summary>
/// World-space per-zone fog shader renderer. Creates one ColorRect per zone
/// with niebla enabled, positioned at the zone's world rect (X1..X2, Y1..Y2
/// in tile coords × 32 px). The parent Node2D is transformed by the camera
/// each frame so the fog rects stay glued to the tiles they cover —
/// walking/panning moves the rects with the world, and the pattern stays
/// fixed to those tiles.
///
/// Usage:
/// - Construct once, call AttachTo(Node) in _Ready
/// - Each frame call Update(cameraOffset, zoom, zones)
/// - Cleanup calls QueueFree on everything
/// </summary>
public class ZoneFogRenderer
{
    private const int TileSize = 32;
    // Extend each fog rect by this many pixels on each side so the fog
    // spills past the zone boundary. Combined with the shader's edge-fade
    // the result is a soft, organic falloff instead of a hard rectangle.
    private const float BleedPadPx = 128f;

    private Node2D? _worldLayer;
    private readonly List<ColorRect> _pool = new();
    private Shader? _shader;
    private NoiseTexture2D? _noiseTexture;

    /// <summary>Attach once from the owning Node's _Ready method.</summary>
    public void AttachTo(Node parent)
    {
        _shader = GD.Load<Shader>("res://Shaders/fog_overlay.gdshader");
        if (_shader == null)
        {
            GD.PushWarning("[ZoneFogRenderer] fog_overlay.gdshader not found — fog disabled");
            return;
        }

        var fnl = new FastNoiseLite();
        fnl.Seed = 42;
        _noiseTexture = new NoiseTexture2D
        {
            Noise = fnl,
            Width = 256,
            Height = 256,
            Seamless = true,
        };

        _worldLayer = new Node2D { Name = "ZoneFogWorldLayer" };
        parent.AddChild(_worldLayer);
    }

    /// <summary>
    /// Call each frame. cameraOffset + zoom position the world layer so children
    /// use world pixel coordinates. Renders BOTH zone fog (X1..X2 Y1..Y2 rects)
    /// AND per-tile painted fog blobs from MapData.PaintedFogTiles.
    /// </summary>
    public void Update(Vector2 cameraOffset, float zoom, IReadOnlyList<ZoneInfo> zones, MapData? map)
    {
        if (_worldLayer == null) return;

        _worldLayer.Position = cameraOffset;
        _worldLayer.Scale = new Vector2(zoom, zoom);

        int idx = 0;

        // 1) Zone fog — one ColorRect per zone with niebla
        for (int i = 0; i < zones.Count; i++)
        {
            var zone = zones[i];
            if (!zone.Niebla || zone.NieblaDensity <= 0) continue;

            float innerX = (zone.X1 - 1) * (float)TileSize;
            float innerY = (zone.Y1 - 1) * (float)TileSize;
            float innerW = (zone.X2 - zone.X1 + 1) * (float)TileSize;
            float innerH = (zone.Y2 - zone.Y1 + 1) * (float)TileSize;
            PlaceRect(idx++, innerX, innerY, innerW, innerH,
                zone.NieblaDensity, zone.NieblaR, zone.NieblaG, zone.NieblaB,
                zone.NieblaSpeedX, zone.NieblaSpeedY);
        }

        // 2) Painted fog — one small ColorRect per tile. Each tile is a
        // single-tile inner rect, padded to 288x288 by the bleed, so the
        // soft alpha fade from the shader turns each stamp into a fog blob.
        // Adjacent painted tiles overlap → cluster becomes one large cloud.
        if (map != null && map.PaintedFogTiles.Count > 0 && map.PaintedFogDensity > 0)
        {
            foreach (var t in map.PaintedFogTiles)
            {
                float innerX = (t.X - 1) * (float)TileSize;
                float innerY = (t.Y - 1) * (float)TileSize;
                PlaceRect(idx++, innerX, innerY, TileSize, TileSize,
                    map.PaintedFogDensity, map.PaintedFogR, map.PaintedFogG, map.PaintedFogB,
                    map.PaintedFogSpeedX, map.PaintedFogSpeedY);
            }
        }

        // Hide unused pool entries
        for (int i = idx; i < _pool.Count; i++)
            _pool[i].Visible = false;
    }

    /// <summary>Position and configure a pooled fog rect at world coords.</summary>
    private void PlaceRect(int poolIdx, float innerX, float innerY, float innerW, float innerH,
        int density, int r, int g, int b, int speedX, int speedY)
    {
        var rect = Acquire(poolIdx);
        // Padded rect — fog bleeds BleedPadPx outside the inner bounds on each side
        float x = innerX - BleedPadPx;
        float y = innerY - BleedPadPx;
        float w = innerW + BleedPadPx * 2f;
        float h = innerH + BleedPadPx * 2f;
        rect.Position = new Vector2(x, y);
        rect.Size = new Vector2(w, h);
        rect.Visible = true;

        if (rect.Material is ShaderMaterial sm)
        {
            sm.SetShaderParameter("density", density / 255f);
            sm.SetShaderParameter("fog_color", new Color(r / 255f, g / 255f, b / 255f, 1f));
            sm.SetShaderParameter("speed", new Vector2(speedX / 100f, speedY / 100f));
            float rectMax = Mathf.Max(w, h);
            sm.SetShaderParameter("noise_scale", Mathf.Max(0.5f, rectMax / 512f));
            sm.SetShaderParameter("edge_fade_x", BleedPadPx / w);
            sm.SetShaderParameter("edge_fade_y", BleedPadPx / h);
        }
    }

    private ColorRect Acquire(int index)
    {
        while (_pool.Count <= index)
        {
            var mat = new ShaderMaterial { Shader = _shader };
            if (_noiseTexture != null)
                mat.SetShaderParameter("noise_texture", _noiseTexture);
            var rect = new ColorRect
            {
                MouseFilter = Control.MouseFilterEnum.Ignore,
                Material = mat,
                Visible = false,
            };
            _worldLayer!.AddChild(rect);
            _pool.Add(rect);
        }
        return _pool[index];
    }

    public void Cleanup()
    {
        foreach (var r in _pool)
            if (GodotObject.IsInstanceValid(r)) r.QueueFree();
        _pool.Clear();
        if (_worldLayer != null && GodotObject.IsInstanceValid(_worldLayer))
            _worldLayer.QueueFree();
        _worldLayer = null;
        _shader = null;
        _noiseTexture = null;
    }
}
