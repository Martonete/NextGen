#nullable enable
using System.Collections.Generic;
using AOWorldEditor.Data;
using Godot;

namespace AOWorldEditor.Editor;

/// <summary>
/// Two-mode fog renderer:
///
/// 1. <b>Humo (painted smoke)</b> — solid-fill fog painted per-tile by the
///    user via the Humo tool. Uses `fog_humo.gdshader`. No noise, no
///    animation, no player break — just a clean saturated tile fill with
///    the map's humo color and density.
///
/// 2. <b>Niebla de zona (zone fog)</b> — animated world-anchored noise
///    fog that covers tiles inside a zone with Niebla=true. Uses
///    `fog_zone.gdshader`. Supports free_smoke multi-directional swirl
///    and player break (local dispersion around the character).
///
/// Each mode has its own Sprite2D + ShaderMaterial + R8 mask texture.
/// Both sprites live under a shared Node2D (`_worldLayer`) that's
/// transformed each frame by the camera offset+zoom. Using Sprite2D
/// with a 1x1 transparent canvas texture means fallback rendering
/// (if a shader fails to compile) is INVISIBLE rather than opaque
/// white like ColorRect would give.
///
/// The masks are rebuilt only when the map changes (MarkDirty()) so
/// frame cost is minimal.
/// </summary>
public class ZoneFogRenderer
{
    private const int TileSize = 32;

    private Node2D? _worldLayer;
    private NoiseTexture2D? _noiseTexture;
    private ImageTexture? _canvasTexture; // 1x1 transparent shared between sprites

    // --- Humo (painted smoke) ---
    private Sprite2D? _humoSprite;
    private Shader? _humoShader;
    private Image? _humoMaskImage;
    private ImageTexture? _humoMaskTexture;

    // --- Niebla de zona (animated noise fog) ---
    private Sprite2D? _zoneSprite;
    private Shader? _zoneShader;
    private Image? _zoneMaskImage;
    private ImageTexture? _zoneMaskTexture;

    // Shared mask state
    private int _maskW, _maskH;
    private bool _maskDirty = true;

    public void AttachTo(Node parent)
    {
        _humoShader = GD.Load<Shader>("res://Shaders/fog_humo.gdshader");
        _zoneShader = GD.Load<Shader>("res://Shaders/fog_zone.gdshader");
        GD.Print($"[ZoneFogRenderer] AttachTo: humo_shader={(_humoShader != null ? "OK" : "NULL")} zone_shader={(_zoneShader != null ? "OK" : "NULL")}");

        if (_humoShader == null && _zoneShader == null)
        {
            GD.PushWarning("[ZoneFogRenderer] both fog shaders missing — fog disabled");
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

        // Shared 1x1 transparent canvas for both sprites
        var canvasImg = Image.CreateEmpty(1, 1, false, Image.Format.Rgba8);
        canvasImg.SetPixel(0, 0, new Color(0, 0, 0, 0));
        _canvasTexture = ImageTexture.CreateFromImage(canvasImg);

        _worldLayer = new Node2D { Name = "ZoneFogWorldLayer" };
        parent.AddChild(_worldLayer);

        if (_humoShader != null)
        {
            var humoMat = new ShaderMaterial { Shader = _humoShader };
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
        }

        if (_zoneShader != null)
        {
            var zoneMat = new ShaderMaterial { Shader = _zoneShader };
            zoneMat.SetShaderParameter("noise_texture", _noiseTexture);
            _zoneSprite = new Sprite2D
            {
                Texture = _canvasTexture,
                Centered = false,
                Material = zoneMat,
                Visible = false,
                TextureFilter = CanvasItem.TextureFilterEnum.Nearest,
                ZIndex = 1, // drawn above the humo sprite so layered zones overlap correctly
            };
            _worldLayer.AddChild(_zoneSprite);
        }
    }

    /// <summary>Mark both masks as stale. Call after any map edit that
    /// could affect fog coverage: PaintFog, EraseFog, zone edit, layer paint.</summary>
    public void MarkDirty() => _maskDirty = true;

    /// <summary>Per-frame update — positions the world layer under the camera,
    /// rebuilds masks if dirty, and pushes shader uniforms for both modes.</summary>
    public void Update(
        Vector2 cameraOffset,
        float zoom,
        IReadOnlyList<ZoneInfo> zones,
        MapData? map,
        Vector2 playerWorldPx)
    {
        if (_worldLayer == null) return;
        if (map == null)
        {
            if (_humoSprite != null) _humoSprite.Visible = false;
            if (_zoneSprite != null) _zoneSprite.Visible = false;
            return;
        }

        _worldLayer.Position = cameraOffset;
        _worldLayer.Scale = new Vector2(zoom, zoom);

        // Determine which fog modes are active this frame. Density requirement
        // is dropped — if a zone has Niebla=true we render it even when
        // NieblaDensity happens to be 0 (falls back to a default visible value).
        bool hasPaintedFog = map.PaintedFogTiles.Count > 0;
        bool hasZoneFog = false;
        for (int i = 0; i < zones.Count; i++)
        {
            if (zones[i].Niebla) { hasZoneFog = true; break; }
        }

        // Rebuild both masks if dirty
        if (_maskDirty || _maskW != map.Width || _maskH != map.Height)
        {
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
                sm.SetShaderParameter("humo_mask", _humoMaskTexture);
                sm.SetShaderParameter("map_tile_size", mapSize);
                sm.SetShaderParameter("rect_world_origin", Vector2.Zero);
                sm.SetShaderParameter("rect_world_size", spriteScale);
                sm.SetShaderParameter("humo_density", dens / 255f);
                sm.SetShaderParameter("humo_color",
                    new Color(map.PaintedFogR / 255f, map.PaintedFogG / 255f, map.PaintedFogB / 255f, 1f));
                sm.SetShaderParameter("humo_animated", map.PaintedFogAnimated ? 1.0f : 0.0f);
                sm.SetShaderParameter("humo_speed",
                    new Vector2(map.PaintedFogSpeedX / 100f, map.PaintedFogSpeedY / 100f));
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
                // Use the first zone with niebla as the style source. Defaults
                // (density=90, color=128/140/160, speed=5/2) kick in if the
                // zone has Niebla=true but the fog params are 0 (e.g., older
                // saved zones from before those fields existed).
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
                sm.SetShaderParameter("zone_mask", _zoneMaskTexture);
                sm.SetShaderParameter("map_tile_size", mapSize);
                sm.SetShaderParameter("rect_world_origin", Vector2.Zero);
                sm.SetShaderParameter("rect_world_size", spriteScale);
                sm.SetShaderParameter("density", dens / 255f);
                sm.SetShaderParameter("fog_color", new Color(r / 255f, g / 255f, b / 255f, 1f));
                sm.SetShaderParameter("speed", new Vector2(sx / 100f, sy / 100f));
            }
        }
    }

    /// <summary>Build the R8 mask for painted humo tiles. Subtracts layer
    /// 2/3/4-occluded tiles so objects poke through cleanly.</summary>
    private void RebuildHumoMask(MapData map)
    {
        int W = map.Width, H = map.Height;
        if (_humoMaskImage == null || _humoMaskImage.GetWidth() != W || _humoMaskImage.GetHeight() != H)
        {
            _humoMaskImage = Image.CreateEmpty(W, H, false, Image.Format.R8);
            _humoMaskTexture = null;
        }
        _humoMaskImage.Fill(Colors.Black);

        // Painted tiles → white
        foreach (var t in map.PaintedFogTiles)
        {
            if (t.X >= 1 && t.X <= W && t.Y >= 1 && t.Y <= H)
                _humoMaskImage.SetPixel(t.X - 1, t.Y - 1, Colors.White);
        }

        // Subtract layer 2/3/4 content — trees/buildings break the fog
        for (int y = 1; y <= H; y++)
        {
            for (int x = 1; x <= W; x++)
            {
                ref var tile = ref map.Tiles[x, y];
                if (tile.Layer2 != 0 || tile.Layer3 != 0 || tile.Layer4 != 0)
                    _humoMaskImage.SetPixel(x - 1, y - 1, Colors.Black);
            }
        }

        if (_humoMaskTexture == null)
            _humoMaskTexture = ImageTexture.CreateFromImage(_humoMaskImage);
        else
            _humoMaskTexture.Update(_humoMaskImage);
    }

    /// <summary>Build the R8 mask for zone niebla tiles. Subtracts layer
    /// 2/3/4-occluded tiles.</summary>
    private void RebuildZoneMask(MapData map, IReadOnlyList<ZoneInfo> zones)
    {
        int W = map.Width, H = map.Height;
        if (_zoneMaskImage == null || _zoneMaskImage.GetWidth() != W || _zoneMaskImage.GetHeight() != H)
        {
            _zoneMaskImage = Image.CreateEmpty(W, H, false, Image.Format.R8);
            _zoneMaskTexture = null;
        }
        _zoneMaskImage.Fill(Colors.Black);

        // Zones with niebla → white inside their rect (density check dropped;
        // any zone with Niebla=true is rendered using default density if needed)
        for (int i = 0; i < zones.Count; i++)
        {
            var z = zones[i];
            if (!z.Niebla) continue;
            int x1 = Mathf.Max(1, z.X1), x2 = Mathf.Min(W, z.X2);
            int y1 = Mathf.Max(1, z.Y1), y2 = Mathf.Min(H, z.Y2);
            for (int y = y1; y <= y2; y++)
                for (int x = x1; x <= x2; x++)
                    _zoneMaskImage.SetPixel(x - 1, y - 1, Colors.White);
        }

        // Subtract layer 2/3/4 content
        for (int y = 1; y <= H; y++)
        {
            for (int x = 1; x <= W; x++)
            {
                ref var tile = ref map.Tiles[x, y];
                if (tile.Layer2 != 0 || tile.Layer3 != 0 || tile.Layer4 != 0)
                    _zoneMaskImage.SetPixel(x - 1, y - 1, Colors.Black);
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
        _humoShader = null;
        _zoneShader = null;
        _noiseTexture = null;
    }
}
