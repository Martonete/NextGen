#nullable enable
using System.Collections.Generic;
using AOWorldEditor.Data;
using Godot;

namespace AOWorldEditor.Editor;

/// <summary>
/// World-space fog renderer using a single ColorRect covering the whole
/// map and a per-tile mask texture to control where fog appears.
///
/// - Zones with Niebla=true mark their tile rect as "has fog"
/// - Painted fog tiles (MapData.PaintedFogTiles) also mark their tile
/// - Tiles with content on Layer 2/3/4 are SUBTRACTED from the mask
///   so objects like trees/buildings poke through the fog cleanly
/// - A `player_world_pos` uniform makes the fog fade locally around the
///   character, giving a smoke-breaking effect as the player walks through
///
/// Mask regeneration is gated by a dirty flag — callers invoke MarkDirty()
/// when they modify the map in a way that affects fog (paint, zone edit,
/// layer change, etc.) so we don't re-upload the GPU texture every frame.
/// </summary>
public class ZoneFogRenderer
{
    private const int TileSize = 32;

    private Node2D? _worldLayer;
    private ColorRect? _fogRect;
    private Shader? _shader;
    private NoiseTexture2D? _noiseTexture;

    // Mask state
    private Image? _maskImage;
    private ImageTexture? _maskTexture;
    private int _maskW, _maskH;
    private bool _maskDirty = true;

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

        var mat = new ShaderMaterial { Shader = _shader };
        mat.SetShaderParameter("noise_texture", _noiseTexture);

        _fogRect = new ColorRect
        {
            MouseFilter = Control.MouseFilterEnum.Ignore,
            Material = mat,
            Visible = false,
        };
        _worldLayer.AddChild(_fogRect);
    }

    /// <summary>Mark the tile mask as stale. Call after any map edit that
    /// could affect fog coverage: PaintFog, EraseFog, zone edit, layer paint.</summary>
    public void MarkDirty() => _maskDirty = true;

    /// <summary>
    /// Call each frame. Positions the world layer under the camera, rebuilds
    /// the mask if dirty, and pushes all shader uniforms.
    /// </summary>
    public void Update(
        Vector2 cameraOffset,
        float zoom,
        IReadOnlyList<ZoneInfo> zones,
        MapData? map,
        Vector2 playerWorldPx)
    {
        if (_worldLayer == null || _fogRect == null || _shader == null) return;
        // TEMP DEBUG: hard-disable the fog rect to prove whether it's the
        // source of the white screen. If user reports the map is visible
        // again, the fog rect (shader or ColorRect fallback) is the problem.
        _fogRect.Visible = false;
        return;
        #pragma warning disable CS0162 // Unreachable code
        if (map == null) { _fogRect.Visible = false; return; }

        _worldLayer.Position = cameraOffset;
        _worldLayer.Scale = new Vector2(zoom, zoom);

        // Determine whether there's any fog to render at all
        bool hasPaintedFog = map.PaintedFogTiles.Count > 0 && map.PaintedFogDensity > 0;
        bool hasZoneFog = false;
        for (int i = 0; i < zones.Count; i++)
        {
            if (zones[i].Niebla && zones[i].NieblaDensity > 0) { hasZoneFog = true; break; }
        }
        if (!hasPaintedFog && !hasZoneFog)
        {
            _fogRect.Visible = false;
            return;
        }

        // Rebuild mask if dirty or map size changed
        if (_maskDirty || _maskW != map.Width || _maskH != map.Height)
        {
            RebuildMask(map, zones);
            _maskDirty = false;
        }

        // Cover the whole map — one rect, world coordinates
        float worldW = map.Width * TileSize;
        float worldH = map.Height * TileSize;
        _fogRect.Position = Vector2.Zero;
        _fogRect.Size = new Vector2(worldW, worldH);
        _fogRect.Visible = true;

        if (_fogRect.Material is ShaderMaterial sm)
        {
            // Global style — use map's painted-fog settings, or the first zone
            // with niebla as fallback. Per-zone colors would need a separate
            // color mask texture; keep it simple for now.
            int density = map.PaintedFogDensity;
            int r = map.PaintedFogR, g = map.PaintedFogG, b = map.PaintedFogB;
            int sx = map.PaintedFogSpeedX, sy = map.PaintedFogSpeedY;
            if (!hasPaintedFog && hasZoneFog)
            {
                for (int i = 0; i < zones.Count; i++)
                {
                    var z = zones[i];
                    if (z.Niebla && z.NieblaDensity > 0)
                    {
                        density = z.NieblaDensity;
                        r = z.NieblaR; g = z.NieblaG; b = z.NieblaB;
                        sx = z.NieblaSpeedX; sy = z.NieblaSpeedY;
                        break;
                    }
                }
            }

            sm.SetShaderParameter("density", density / 255f);
            sm.SetShaderParameter("fog_color", new Color(r / 255f, g / 255f, b / 255f, 1f));
            sm.SetShaderParameter("speed", new Vector2(sx / 100f, sy / 100f));
            if (_maskTexture != null)
                sm.SetShaderParameter("fog_mask", _maskTexture);
            sm.SetShaderParameter("map_tile_size", new Vector2(map.Width, map.Height));
            sm.SetShaderParameter("rect_world_origin", Vector2.Zero);
            sm.SetShaderParameter("rect_world_size", new Vector2(worldW, worldH));
            sm.SetShaderParameter("player_world_pos", playerWorldPx);
            sm.SetShaderParameter("player_break_radius", 144f);
            sm.SetShaderParameter("free_smoke", map.FogFreeSmoke ? 1.0f : 0.0f);
        }
        #pragma warning restore CS0162
    }

    /// <summary>Build the R8 mask image: white for tiles that should show fog,
    /// black elsewhere. Subtracts tiles occluded by layer 2/3/4 content.</summary>
    private void RebuildMask(MapData map, IReadOnlyList<ZoneInfo> zones)
    {
        int W = map.Width, H = map.Height;

        if (_maskImage == null || _maskImage.GetWidth() != W || _maskImage.GetHeight() != H)
        {
            _maskImage = Image.CreateEmpty(W, H, false, Image.Format.R8);
            _maskTexture = null; // recreate to pick up new size
        }
        _maskImage.Fill(Colors.Black);

        // 1) Zone fog — set pixels for tiles inside every zone with niebla
        for (int i = 0; i < zones.Count; i++)
        {
            var z = zones[i];
            if (!z.Niebla || z.NieblaDensity <= 0) continue;
            int x1 = Mathf.Max(1, z.X1), x2 = Mathf.Min(W, z.X2);
            int y1 = Mathf.Max(1, z.Y1), y2 = Mathf.Min(H, z.Y2);
            for (int y = y1; y <= y2; y++)
                for (int x = x1; x <= x2; x++)
                    _maskImage.SetPixel(x - 1, y - 1, Colors.White);
        }

        // 2) Painted fog tiles
        if (map.PaintedFogDensity > 0)
        {
            foreach (var t in map.PaintedFogTiles)
            {
                if (t.X >= 1 && t.X <= W && t.Y >= 1 && t.Y <= H)
                    _maskImage.SetPixel(t.X - 1, t.Y - 1, Colors.White);
            }
        }

        // 3) Subtract tiles with layer 2/3/4 content — objects break the fog
        for (int y = 1; y <= H; y++)
        {
            for (int x = 1; x <= W; x++)
            {
                ref var tile = ref map.Tiles[x, y];
                if (tile.Layer2 != 0 || tile.Layer3 != 0 || tile.Layer4 != 0)
                    _maskImage.SetPixel(x - 1, y - 1, Colors.Black);
            }
        }

        // Upload
        if (_maskTexture == null)
            _maskTexture = ImageTexture.CreateFromImage(_maskImage);
        else
            _maskTexture.Update(_maskImage);

        _maskW = W;
        _maskH = H;
    }

    public void Cleanup()
    {
        if (_fogRect != null && GodotObject.IsInstanceValid(_fogRect)) _fogRect.QueueFree();
        _fogRect = null;
        if (_worldLayer != null && GodotObject.IsInstanceValid(_worldLayer)) _worldLayer.QueueFree();
        _worldLayer = null;
        _maskImage = null;
        _maskTexture = null;
        _shader = null;
        _noiseTexture = null;
    }
}
