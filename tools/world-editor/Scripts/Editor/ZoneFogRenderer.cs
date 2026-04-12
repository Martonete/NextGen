#nullable enable
using System.Collections.Generic;
using AOWorldEditor.Data;
using Godot;

namespace AOWorldEditor.Editor;

/// <summary>
/// Unified fog renderer with two sprites sharing a single shader:
///
/// 1. <b>Humo (painted smoke)</b> — animated world-space fog on tiles
///    painted by the user via the Humo tool. Uses a dedicated mask
///    (MapData.PaintedFogTiles) and the map's PaintedFog* color/density
///    fields.
///
/// 2. <b>Niebla de zona (zone fog)</b> — animated world-space fog on
///    tiles inside zones with Niebla=true. Uses a dedicated mask
///    (built from zone bounds) and the first matching zone's color/density.
///
/// Both sprites use the same `fog_overlay.gdshader` — they're visually
/// similar (animated noise clouds) but independent because each has its
/// own ShaderMaterial instance, its own mask texture, and its own uniform
/// values. Layer 3 (objects) and Layer 4 (roofs) tiles are subtracted
/// from both masks so trees/buildings/roofs cleanly break the fog.
///
/// Sprite2D with a 1x1 transparent canvas texture gives an invisible
/// fallback if the shader ever fails to compile (unlike ColorRect whose
/// fallback is opaque white).
/// </summary>
public class ZoneFogRenderer
{
    private const int TileSize = 32;

    private Node2D? _worldLayer;
    private Shader? _shader;
    private NoiseTexture2D? _noiseTexture;
    private ImageTexture? _canvasTexture; // 1x1 transparent shared between sprites

    // --- Humo (painted smoke) ---
    private Sprite2D? _humoSprite;
    private Image? _humoMaskImage;
    private ImageTexture? _humoMaskTexture;

    // --- Niebla de zona (animated noise fog) ---
    private Sprite2D? _zoneSprite;
    private Image? _zoneMaskImage;
    private ImageTexture? _zoneMaskTexture;

    // Shared mask state
    private int _maskW, _maskH;
    private bool _maskDirty = true;

    public void AttachTo(Node parent)
    {
        _shader = GD.Load<Shader>("res://Shaders/fog_overlay.gdshader");
        GD.Print($"[ZoneFogRenderer] AttachTo: shader={(_shader != null ? "OK" : "NULL")}");
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

        // 1x1 transparent canvas: fallback invisible if shader compile fails
        var canvasImg = Image.CreateEmpty(1, 1, false, Image.Format.Rgba8);
        canvasImg.SetPixel(0, 0, new Color(0, 0, 0, 0));
        _canvasTexture = ImageTexture.CreateFromImage(canvasImg);

        _worldLayer = new Node2D { Name = "ZoneFogWorldLayer" };
        parent.AddChild(_worldLayer);

        // Humo sprite
        var humoMat = new ShaderMaterial { Shader = _shader };
        humoMat.SetShaderParameter("noise_texture", _noiseTexture);
        _humoSprite = new Sprite2D
        {
            Texture = _canvasTexture,
            Centered = false,
            Material = humoMat,
            Visible = false,
            TextureFilter = CanvasItem.TextureFilterEnum.Nearest,
        };
        _worldLayer.AddChild(_humoSprite);

        // Zone sprite (ZIndex +1 so it draws above humo if they overlap)
        var zoneMat = new ShaderMaterial { Shader = _shader };
        zoneMat.SetShaderParameter("noise_texture", _noiseTexture);
        _zoneSprite = new Sprite2D
        {
            Texture = _canvasTexture,
            Centered = false,
            Material = zoneMat,
            Visible = false,
            TextureFilter = CanvasItem.TextureFilterEnum.Nearest,
            ZIndex = 1,
        };
        _worldLayer.AddChild(_zoneSprite);

        GD.Print($"[ZoneFogRenderer] AttachTo complete: humo + zone sprites created");
    }

    /// <summary>Mark both masks as stale — call after any edit that could
    /// affect fog (paint, zone edit, layer paint, map load).</summary>
    public void MarkDirty() => _maskDirty = true;

    public void Update(
        Vector2 cameraOffset,
        float zoom,
        IReadOnlyList<ZoneInfo> zones,
        MapData? map,
        Vector2 playerWorldPx)
    {
        if (_worldLayer == null || _shader == null) return;
        if (map == null)
        {
            if (_humoSprite != null) _humoSprite.Visible = false;
            if (_zoneSprite != null) _zoneSprite.Visible = false;
            return;
        }

        _worldLayer.Position = cameraOffset;
        _worldLayer.Scale = new Vector2(zoom, zoom);

        bool hasPaintedFog = map.PaintedFogTiles.Count > 0;
        bool hasZoneFog = false;
        for (int i = 0; i < zones.Count; i++)
        {
            if (zones[i].Niebla) { hasZoneFog = true; break; }
        }

        // Rebuild masks if dirty or map size changed
        if (_maskDirty || _maskW != map.Width || _maskH != map.Height)
        {
            int nieblaCount = 0;
            for (int i = 0; i < zones.Count; i++) if (zones[i].Niebla) nieblaCount++;
            GD.Print($"[ZoneFogRenderer] Rebuilding — total_zones={zones.Count} niebla_zones={nieblaCount} painted_tiles={map.PaintedFogTiles.Count} density={map.PaintedFogDensity} color=({map.PaintedFogR},{map.PaintedFogG},{map.PaintedFogB})");
            if (hasPaintedFog) RebuildHumoMask(map);
            if (hasZoneFog) RebuildZoneMask(map, zones);
            _maskW = map.Width;
            _maskH = map.Height;
            _maskDirty = false;
        }

        float worldW = map.Width * TileSize;
        float worldH = map.Height * TileSize;
        var spriteScale = new Vector2(worldW, worldH);
        var mapSize = new Vector2(map.Width, map.Height);

        // --- Humo sprite ---
        if (_humoSprite != null)
        {
            _humoSprite.Position = Vector2.Zero;
            _humoSprite.Scale = spriteScale;
            _humoSprite.Visible = hasPaintedFog && _humoMaskTexture != null;
            if (_humoSprite.Visible && _humoSprite.Material is ShaderMaterial sm && _humoMaskTexture != null)
            {
                int dens = map.PaintedFogDensity > 0 ? map.PaintedFogDensity : 160;
                sm.SetShaderParameter("fog_mask", _humoMaskTexture);
                sm.SetShaderParameter("map_tile_size", mapSize);
                sm.SetShaderParameter("rect_world_origin", Vector2.Zero);
                sm.SetShaderParameter("rect_world_size", spriteScale);
                sm.SetShaderParameter("density", dens / 255f);
                sm.SetShaderParameter("fog_color",
                    new Color(map.PaintedFogR / 255f, map.PaintedFogG / 255f, map.PaintedFogB / 255f, 1f));
                sm.SetShaderParameter("speed",
                    new Vector2(map.PaintedFogSpeedX / 100f, map.PaintedFogSpeedY / 100f));
                sm.SetShaderParameter("free_smoke", map.FogFreeSmoke ? 1.0f : 0.0f);
            }
        }

        // --- Zone sprite ---
        if (_zoneSprite != null)
        {
            _zoneSprite.Position = Vector2.Zero;
            _zoneSprite.Scale = spriteScale;
            _zoneSprite.Visible = hasZoneFog && _zoneMaskTexture != null;
            if (_zoneSprite.Visible && _zoneSprite.Material is ShaderMaterial sm && _zoneMaskTexture != null)
            {
                // Use the first zone with Niebla as style source. Defaults kick
                // in if the fog fields happen to be zero (old saves).
                int dens = 90, r = 128, g = 140, b = 160, sx = 5, sy = 2;
                for (int i = 0; i < zones.Count; i++)
                {
                    var z = zones[i];
                    if (z.Niebla)
                    {
                        dens = z.NieblaDensity > 0 ? z.NieblaDensity : 90;
                        r = z.NieblaR > 0 ? z.NieblaR : 128;
                        g = z.NieblaG > 0 ? z.NieblaG : 140;
                        b = z.NieblaB > 0 ? z.NieblaB : 160;
                        sx = z.NieblaSpeedX;
                        sy = z.NieblaSpeedY;
                        break;
                    }
                }
                sm.SetShaderParameter("fog_mask", _zoneMaskTexture);
                sm.SetShaderParameter("map_tile_size", mapSize);
                sm.SetShaderParameter("rect_world_origin", Vector2.Zero);
                sm.SetShaderParameter("rect_world_size", spriteScale);
                sm.SetShaderParameter("density", dens / 255f);
                sm.SetShaderParameter("fog_color", new Color(r / 255f, g / 255f, b / 255f, 1f));
                sm.SetShaderParameter("speed", new Vector2(sx / 100f, sy / 100f));
                sm.SetShaderParameter("free_smoke", map.FogFreeSmoke ? 1.0f : 0.0f);
            }
        }
    }

    /// <summary>Build R8 mask for painted humo tiles. Only iterates the
    /// painted tile set (not the whole map) — Layer 3/4 occlusion is
    /// checked per-tile inline, so the cost is O(paint count) instead of
    /// O(map size). Critical for performance on large maps (1000×1000).</summary>
    private void RebuildHumoMask(MapData map)
    {
        int W = map.Width, H = map.Height;
        if (_humoMaskImage == null || _humoMaskImage.GetWidth() != W || _humoMaskImage.GetHeight() != H)
        {
            _humoMaskImage = Image.CreateEmpty(W, H, false, Image.Format.R8);
            _humoMaskTexture = null;
        }
        _humoMaskImage.Fill(Colors.Black);

        foreach (var t in map.PaintedFogTiles)
        {
            if (t.X < 1 || t.X > W || t.Y < 1 || t.Y > H) continue;
            ref var tile = ref map.Tiles[t.X, t.Y];
            // Skip tiles occluded by L3 (objects) or L4 (roofs)
            if (tile.Layer3 != 0 || tile.Layer4 != 0) continue;
            _humoMaskImage.SetPixel(t.X - 1, t.Y - 1, Colors.White);
        }

        if (_humoMaskTexture == null)
            _humoMaskTexture = ImageTexture.CreateFromImage(_humoMaskImage);
        else
            _humoMaskTexture.Update(_humoMaskImage);
    }

    /// <summary>Build R8 mask for zone niebla tiles. L3/L4 occlusion checked
    /// inline while iterating each zone's rect — cost is O(sum of zone areas)
    /// instead of O(whole map). Much faster on large maps.</summary>
    private void RebuildZoneMask(MapData map, IReadOnlyList<ZoneInfo> zones)
    {
        int W = map.Width, H = map.Height;
        if (_zoneMaskImage == null || _zoneMaskImage.GetWidth() != W || _zoneMaskImage.GetHeight() != H)
        {
            _zoneMaskImage = Image.CreateEmpty(W, H, false, Image.Format.R8);
            _zoneMaskTexture = null;
        }
        _zoneMaskImage.Fill(Colors.Black);

        for (int i = 0; i < zones.Count; i++)
        {
            var z = zones[i];
            if (!z.Niebla) continue;
            int x1 = Mathf.Max(1, z.X1), x2 = Mathf.Min(W, z.X2);
            int y1 = Mathf.Max(1, z.Y1), y2 = Mathf.Min(H, z.Y2);
            for (int y = y1; y <= y2; y++)
            {
                for (int x = x1; x <= x2; x++)
                {
                    ref var tile = ref map.Tiles[x, y];
                    if (tile.Layer3 != 0 || tile.Layer4 != 0) continue;
                    _zoneMaskImage.SetPixel(x - 1, y - 1, Colors.White);
                }
            }
        }

        if (_zoneMaskTexture == null)
            _zoneMaskTexture = ImageTexture.CreateFromImage(_zoneMaskImage);
        else
            _zoneMaskTexture.Update(_zoneMaskImage);
    }

    public void Cleanup()
    {
        if (_humoSprite != null && GodotObject.IsInstanceValid(_humoSprite)) _humoSprite.QueueFree();
        _humoSprite = null;
        if (_zoneSprite != null && GodotObject.IsInstanceValid(_zoneSprite)) _zoneSprite.QueueFree();
        _zoneSprite = null;
        if (_worldLayer != null && GodotObject.IsInstanceValid(_worldLayer)) _worldLayer.QueueFree();
        _worldLayer = null;
        _humoMaskImage = null;
        _humoMaskTexture = null;
        _zoneMaskImage = null;
        _zoneMaskTexture = null;
        _canvasTexture = null;
        _shader = null;
        _noiseTexture = null;
    }
}
