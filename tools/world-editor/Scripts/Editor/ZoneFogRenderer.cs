#nullable enable
using System.Collections.Generic;
using AOWorldEditor.Data;
using Godot;

namespace AOWorldEditor.Editor;

/// <summary>
/// Fog renderer with independent painted-smoke layers + one zone niebla layer.
///
/// Architecture:
/// - ONE shared Shader (fog_overlay.gdshader)
/// - ONE shared NoiseTexture2D
/// - ONE shared 1x1 transparent canvas texture (fallback invisible if
///   shader compile fails — unlike ColorRect's opaque-white fallback)
///
/// For painted smoke:
/// - A POOL of Sprite2Ds. Each pool entry corresponds to a PaintedFogLayer
///   and has its own ShaderMaterial + per-layer mask texture.
/// - Changing one layer's style (color/density) only affects tiles in that
///   layer — each sprite reads uniforms from its owning layer independently.
///
/// For zone niebla:
/// - ONE Sprite2D with a shared mask (all zones with Niebla=true go into
///   the same mask). Changing a zone's fog fields affects all zone fog.
///
/// All sprites live under a shared Node2D (_worldLayer) transformed each
/// frame by camera offset + zoom.
/// </summary>
public class ZoneFogRenderer
{
    private const int TileSize = 32;

    private Node2D? _worldLayer;
    private Shader? _shader;
    private NoiseTexture2D? _noiseTexture;
    private ImageTexture? _canvasTexture;

    // --- Humo (painted smoke) — pool of sprites, one per layer ---
    private readonly List<Sprite2D> _humoSprites = new();
    private readonly List<Image> _humoMaskImages = new();
    private readonly List<ImageTexture?> _humoMaskTextures = new();

    // --- Niebla de zona (single combined sprite for all zones with Niebla) ---
    private Sprite2D? _zoneSprite;
    private Image? _zoneMaskImage;
    private ImageTexture? _zoneMaskTexture;

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

        var canvasImg = Image.CreateEmpty(1, 1, false, Image.Format.Rgba8);
        canvasImg.SetPixel(0, 0, new Color(0, 0, 0, 0));
        _canvasTexture = ImageTexture.CreateFromImage(canvasImg);

        _worldLayer = new Node2D { Name = "ZoneFogWorldLayer" };
        parent.AddChild(_worldLayer);

        // Zone sprite (always one, even if empty)
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

        GD.Print("[ZoneFogRenderer] AttachTo complete");
    }

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
            HideAllSprites();
            return;
        }

        _worldLayer.Position = cameraOffset;
        _worldLayer.Scale = new Vector2(zoom, zoom);

        int neededHumoSprites = map.PaintedFogLayers.Count;
        bool hasZoneFog = false;
        for (int i = 0; i < zones.Count; i++)
        {
            if (zones[i].Niebla) { hasZoneFog = true; break; }
        }

        // Rebuild masks if dirty or map size changed
        if (_maskDirty || _maskW != map.Width || _maskH != map.Height)
        {
            RebuildHumoLayerMasks(map);
            if (hasZoneFog) RebuildZoneMask(map, zones);
            _maskW = map.Width;
            _maskH = map.Height;
            _maskDirty = false;
            GD.Print($"[ZoneFogRenderer] Rebuilt — layers={map.PaintedFogLayers.Count} zones_niebla={hasZoneFog}");
        }

        float worldW = map.Width * TileSize;
        float worldH = map.Height * TileSize;
        var spriteScale = new Vector2(worldW, worldH);
        var mapSize = new Vector2(map.Width, map.Height);

        // --- Humo sprites (pool, one per layer) ---
        EnsureHumoPool(neededHumoSprites);
        for (int i = 0; i < _humoSprites.Count; i++)
        {
            var sprite = _humoSprites[i];
            if (i >= neededHumoSprites)
            {
                sprite.Visible = false;
                continue;
            }
            var layer = map.PaintedFogLayers[i];
            var tex = _humoMaskTextures[i];
            bool show = layer.Tiles.Count > 0 && tex != null;

            sprite.Position = Vector2.Zero;
            sprite.Scale = spriteScale;
            sprite.Visible = show;
            if (!show || sprite.Material is not ShaderMaterial sm) continue;

            sm.SetShaderParameter("fog_mask", tex!);
            sm.SetShaderParameter("map_tile_size", mapSize);
            sm.SetShaderParameter("rect_world_origin", Vector2.Zero);
            sm.SetShaderParameter("rect_world_size", spriteScale);
            sm.SetShaderParameter("density", layer.Density / 255f);
            sm.SetShaderParameter("fog_color",
                new Color(layer.R / 255f, layer.G / 255f, layer.B / 255f, 1f));
            sm.SetShaderParameter("speed",
                new Vector2(layer.SpeedX / 100f, layer.SpeedY / 100f));
            sm.SetShaderParameter("free_smoke", map.FogFreeSmoke ? 1.0f : 0.0f);
        }

        // --- Zone sprite ---
        if (_zoneSprite != null)
        {
            _zoneSprite.Position = Vector2.Zero;
            _zoneSprite.Scale = spriteScale;
            _zoneSprite.Visible = hasZoneFog && _zoneMaskTexture != null;
            if (_zoneSprite.Visible && _zoneSprite.Material is ShaderMaterial sm && _zoneMaskTexture != null)
            {
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

    private void EnsureHumoPool(int count)
    {
        while (_humoSprites.Count < count)
        {
            var mat = new ShaderMaterial { Shader = _shader };
            mat.SetShaderParameter("noise_texture", _noiseTexture);
            var sprite = new Sprite2D
            {
                Texture = _canvasTexture,
                Centered = false,
                Material = mat,
                Visible = false,
                TextureFilter = CanvasItem.TextureFilterEnum.Nearest,
            };
            _worldLayer?.AddChild(sprite);
            _humoSprites.Add(sprite);
            _humoMaskImages.Add(null!);
            _humoMaskTextures.Add(null);
        }
    }

    /// <summary>Rebuild one R8 mask per painted layer. Cost is O(sum of
    /// painted tiles) — no full-map iteration.</summary>
    private void RebuildHumoLayerMasks(MapData map)
    {
        int W = map.Width, H = map.Height;
        EnsureHumoPool(map.PaintedFogLayers.Count);

        for (int i = 0; i < map.PaintedFogLayers.Count; i++)
        {
            var layer = map.PaintedFogLayers[i];
            var img = _humoMaskImages[i];
            bool needsNew = img == null || img.GetWidth() != W || img.GetHeight() != H;
            if (needsNew)
            {
                img = Image.CreateEmpty(W, H, false, Image.Format.R8);
                _humoMaskImages[i] = img;
                _humoMaskTextures[i] = null;
            }
            img.Fill(Colors.Black);

            foreach (var t in layer.Tiles)
            {
                if (t.X < 1 || t.X > W || t.Y < 1 || t.Y > H) continue;
                ref var tile = ref map.Tiles[t.X, t.Y];
                if (tile.Layer3 != 0 || tile.Layer4 != 0) continue;
                img.SetPixel(t.X - 1, t.Y - 1, Colors.White);
            }

            if (_humoMaskTextures[i] == null)
                _humoMaskTextures[i] = ImageTexture.CreateFromImage(img);
            else
                _humoMaskTextures[i]!.Update(img);
        }
    }

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

    private void HideAllSprites()
    {
        foreach (var s in _humoSprites) s.Visible = false;
        if (_zoneSprite != null) _zoneSprite.Visible = false;
    }

    public void Cleanup()
    {
        foreach (var s in _humoSprites)
            if (GodotObject.IsInstanceValid(s)) s.QueueFree();
        _humoSprites.Clear();
        _humoMaskImages.Clear();
        _humoMaskTextures.Clear();
        if (_zoneSprite != null && GodotObject.IsInstanceValid(_zoneSprite)) _zoneSprite.QueueFree();
        _zoneSprite = null;
        if (_worldLayer != null && GodotObject.IsInstanceValid(_worldLayer)) _worldLayer.QueueFree();
        _worldLayer = null;
        _zoneMaskImage = null;
        _zoneMaskTexture = null;
        _canvasTexture = null;
        _shader = null;
        _noiseTexture = null;
    }
}
