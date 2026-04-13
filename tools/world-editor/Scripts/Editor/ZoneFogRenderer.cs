#nullable enable
using System.Collections.Generic;
using AOWorldEditor.Data;
using Godot;

namespace AOWorldEditor.Editor;

/// <summary>
/// Fog renderer with independent painted-smoke layers + one zone niebla layer.
///
/// Sprites are PANEL-SIZED (not map-sized) and use shader uniforms to
/// describe which world region to sample. This avoids precision / culling
/// issues that came from stretching a 1x1 canvas to e.g. 32000×32000 px
/// for a 1000x1000 map, especially in the embedded walk-mode Window.
///
/// Each painted layer gets its own Sprite2D + ShaderMaterial + mask texture.
/// Zone niebla uses a single combined sprite. All sprites are children of
/// the owning Control (MapViewport or WalkModePanel) and are positioned at
/// (0, 0) with Scale = panel size.
///
/// The caller computes `worldOrigin` and `worldSize` based on its own view
/// setup — MapViewport uses camera offset + zoom; WalkModePanel uses the
/// character position and smooth-move offset.
/// </summary>
public class ZoneFogRenderer
{
    private const int TileSize = 32;

    private Node? _parent;
    private Shader? _shader;
    private NoiseTexture2D? _noiseTexture;
    private ImageTexture? _canvasTexture;

    // --- Humo (painted smoke) — pool of sprites, one per layer ---
    private readonly List<Sprite2D> _humoSprites = new();
    private readonly List<Image> _humoMaskImages = new();
    private readonly List<ImageTexture?> _humoMaskTextures = new();

    // --- Niebla de zona ---
    private Sprite2D? _zoneSprite;
    private Image? _zoneMaskImage;
    private ImageTexture? _zoneMaskTexture;

    private int _maskW, _maskH;
    private bool _maskDirty = true;

    public void AttachTo(Node parent)
    {
        _parent = parent;
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
        parent.AddChild(_zoneSprite);

        GD.Print("[ZoneFogRenderer] AttachTo complete");
    }

    public void MarkDirty() => _maskDirty = true;

    /// <summary>
    /// Call each frame. `panelSize` is the Control's local size in pixels.
    /// `worldOrigin` is the world pixel coordinate at panel (0, 0).
    /// `worldSize` is how much world area is visible across the panel.
    /// The shader samples world via UV mapped to world via these.
    /// </summary>
    public void Update(
        Vector2 panelSize,
        Vector2 worldOrigin,
        Vector2 worldSize,
        IReadOnlyList<ZoneInfo> zones,
        MapData? map,
        Vector2 playerWorldPx)
    {
        if (_shader == null || _parent == null) return;
        if (map == null)
        {
            HideAllSprites();
            return;
        }

        int neededHumoSprites = map.PaintedFogLayers.Count;
        bool hasZoneFog = false;
        for (int i = 0; i < zones.Count; i++)
        {
            if (zones[i].Niebla) { hasZoneFog = true; break; }
        }

        // Safety: rebuild zone mask if it's missing even though we need it
        // (e.g., zones were loaded after the initial dirty flag was cleared).
        bool zoneMaskMissing = hasZoneFog && _zoneMaskTexture == null;
        if (_maskDirty || _maskW != map.Width || _maskH != map.Height || zoneMaskMissing)
        {
            RebuildHumoLayerMasks(map);
            if (hasZoneFog) RebuildZoneMask(map, zones);
            _maskW = map.Width;
            _maskH = map.Height;
            _maskDirty = false;
            GD.Print($"[ZoneFogRenderer] Rebuilt — layers={map.PaintedFogLayers.Count} hasZone={hasZoneFog} zoneTex={(_zoneMaskTexture != null ? "OK" : "NULL")}");
        }

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
            sprite.Scale = panelSize;
            sprite.Visible = show;
            if (!show || sprite.Material is not ShaderMaterial sm) continue;

            sm.SetShaderParameter("fog_mask", tex!);
            sm.SetShaderParameter("map_tile_size", mapSize);
            sm.SetShaderParameter("rect_world_origin", worldOrigin);
            sm.SetShaderParameter("rect_world_size", worldSize);
            sm.SetShaderParameter("density", layer.Density / 255f);
            sm.SetShaderParameter("fog_color",
                new Color(layer.R / 255f, layer.G / 255f, layer.B / 255f, 1f));
            sm.SetShaderParameter("speed",
                new Vector2(layer.SpeedX / 100f, layer.SpeedY / 100f));
            sm.SetShaderParameter("noise_scale", (float)(layer.Size > 0 ? layer.Size : 512));
            sm.SetShaderParameter("free_smoke", map.FogFreeSmoke ? 1.0f : 0.0f);
            // Per-layer randomization to break visual sync with other layers
            // that share the same speed/size. The seed is hashed two ways
            // for the noise UV offset (vec2) and once for the wind-angle
            // rotation (small ±0.5 rad jitter ≈ ±28°).
            int seed = layer.RandomSeed;
            float seedX = (seed * 0.1234f) % 7.0f;
            float seedY = (seed * 0.5678f) % 7.0f;
            float angleOffset = ((seed % 100) / 100f - 0.5f) * 1.0f;
            sm.SetShaderParameter("noise_seed", new Vector2(seedX, seedY));
            sm.SetShaderParameter("wind_angle_offset", angleOffset);
        }

        // --- Zone sprite ---
        if (_zoneSprite != null)
        {
            _zoneSprite.Position = Vector2.Zero;
            _zoneSprite.Scale = panelSize;
            _zoneSprite.Visible = hasZoneFog && _zoneMaskTexture != null;
            if (_zoneSprite.Visible && _zoneSprite.Material is ShaderMaterial sm && _zoneMaskTexture != null)
            {
                // NOTE: this picks the FIRST niebla zone and uses its style
                // for the whole combined zone-fog sprite. If two overlapping
                // niebla zones have different Density/Color/Size, only the
                // first one in iteration order is honoured. Per-zone style
                // rendering would require one sprite+mask per zone, not one
                // combined mask. This is a deliberate simplification — if it
                // becomes a real problem, the fix is to move the renderer
                // from a "one zone sprite" to a "pool like humo layers".
                int dens = 90, r = 128, g = 140, b = 160, sx = 5, sy = 2, size = 512;
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
                        size = z.NieblaSize > 0 ? z.NieblaSize : 512;
                        break;
                    }
                }
                sm.SetShaderParameter("fog_mask", _zoneMaskTexture);
                sm.SetShaderParameter("map_tile_size", mapSize);
                sm.SetShaderParameter("rect_world_origin", worldOrigin);
                sm.SetShaderParameter("rect_world_size", worldSize);
                sm.SetShaderParameter("density", dens / 255f);
                sm.SetShaderParameter("fog_color", new Color(r / 255f, g / 255f, b / 255f, 1f));
                sm.SetShaderParameter("speed", new Vector2(sx / 100f, sy / 100f));
                sm.SetShaderParameter("noise_scale", (float)size);
                // Zone fog uses a fixed seed (no per-zone variation needed
                // since only one zone fog sprite is rendered at a time).
                sm.SetShaderParameter("noise_seed", new Vector2(0f, 0f));
                sm.SetShaderParameter("wind_angle_offset", 0f);
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
            _parent?.AddChild(sprite);
            _humoSprites.Add(sprite);
            _humoMaskImages.Add(null!);
            _humoMaskTextures.Add(null);
        }
    }

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
        _parent = null;
        _zoneMaskImage = null;
        _zoneMaskTexture = null;
        _canvasTexture = null;
        _shader = null;
        _noiseTexture = null;
    }
}
