#nullable enable
using System.Collections.Generic;
using AOWorldEditor.Data;
using Godot;

namespace AOWorldEditor.Editor;

/// <summary>
/// Custom shader-based light renderer that replaces Godot's built-in Light2D.
///
/// Uses a single panel-sized <see cref="Sprite2D"/> with a custom shader
/// (<c>light_overlay.gdshader</c>) that raycasts each pixel toward each
/// light source through an occlusion mask built from L3 GRH sprite alphas.
/// This gives pixel-accurate shadows matching the visible graphic silhouettes
/// — no rectangular artifacts, no AABB limitations.
///
/// Architecture mirrors <see cref="ZoneFogRenderer"/>: AttachTo → per-frame
/// Update → Cleanup. The mask rebuilds when L3/Blocked tiles change; per-frame
/// cost is only the uniform upload + shader execution.
/// </summary>
public class LightRenderer
{
    private const int TileSize = 32;

    /// <summary>Mask resolution: each tile produces this many mask pixels per
    /// axis. 16 means the mask is (mapW*16 × mapH*16). For a 100×100 map:
    /// 1600×1600 = 2.5MB L8 — reasonable, and gives sharp silhouettes on
    /// tree canopies and other detailed sprites. Each mask pixel covers
    /// 2×2 world pixels.</summary>
    private const int MaskPixelsPerTile = 16;

    /// <summary>Ratio of world pixels to mask pixels. With 16 mask-px per tile
    /// and 32 world-px per tile, each mask pixel covers 2×2 world pixels.</summary>
    private const int MaskDownsample = TileSize / MaskPixelsPerTile;

    private Node? _parent;
    private Shader? _shader;
    private Sprite2D? _sprite;
    private ShaderMaterial? _material;

    // 1×1 transparent canvas texture — same trick as ZoneFogRenderer so the
    // sprite is invisible if the shader fails to compile.
    private ImageTexture? _canvasTexture;

    // ── Occlusion mask ──
    private Image? _maskImage;
    private ImageTexture? _maskTexture;
    private bool _maskDirty = true;
    private int _lastMapW = -1;
    private int _lastMapH = -1;
    private System.WeakReference<MapData>? _lastMap;

    // ── Graphics resources for mask generation ──
    private GrhData[]? _grhs;
    private TextureManager? _textures;

    // ── Animation ──
    private float _elapsedTime;
    private readonly RandomNumberGenerator _rng = new();

    // ── Uniform scratch arrays (reused per-frame, avoid GC) ──
    private readonly float[] _lightData = new float[32 * 4]; // vec4 × 32
    private readonly float[] _lightColorData = new float[32 * 4];

    public void AttachTo(Node parent)
    {
        _parent = parent;
        _shader = GD.Load<Shader>("res://Shaders/light_overlay.gdshader");
        if (_shader == null)
        {
            GD.PushWarning("[LightRenderer] light_overlay.gdshader not found — lights disabled");
            return;
        }

        // 1×1 transparent canvas texture (fallback if shader compile fails).
        var canvasImg = Image.CreateEmpty(1, 1, false, Image.Format.Rgba8);
        canvasImg.SetPixel(0, 0, new Color(0, 0, 0, 0));
        _canvasTexture = ImageTexture.CreateFromImage(canvasImg);

        _material = new ShaderMaterial { Shader = _shader };

        _sprite = new Sprite2D
        {
            Texture = _canvasTexture,
            Centered = false,
            Material = _material,
            Visible = false,
            TextureFilter = CanvasItem.TextureFilterEnum.Nearest,
            ZIndex = 2, // above fog (ZIndex 1)
        };
        parent.AddChild(_sprite);

        _rng.Randomize();
    }

    /// <summary>Inject graphics resources for mask generation. Call after
    /// <see cref="AttachTo"/> and whenever the texture pack changes.</summary>
    public void SetGraphicsResources(GrhData[]? grhs, TextureManager? textures)
    {
        _grhs = grhs;
        _textures = textures;
        _maskDirty = true;
    }

    public void SetVisible(bool visible)
    {
        if (_sprite != null) _sprite.Visible = visible;
    }

    /// <summary>Set the ambient darkness color. With multiply blending,
    /// unlit pixels become scene × ambient. A dark value like (0.08, 0.06, 0.12)
    /// gives a night-blue darkness. (1, 1, 1) = daylight (no darkening).</summary>
    public void SetAmbient(Color c)
    {
        _ambient = new Vector3(c.R, c.G, c.B);
    }

    private Vector3 _ambient = new(0.4f, 0.4f, 0.5f);

    public void MarkDirty() => _maskDirty = true;

    /// <summary>
    /// Per-frame update. Rebuilds the occlusion mask when dirty, then uploads
    /// the light array + camera mapping as shader uniforms.
    /// </summary>
    public void Update(
        Vector2 panelSize,
        Vector2 worldOrigin,
        Vector2 worldSize,
        MapData? map,
        float deltaTime,
        Vector2? characterWorldPx = null)
    {
        if (_parent == null || _sprite == null || _material == null || _shader == null) return;

        // Visibility gate
        bool parentVisible = _parent is not CanvasItem ci || ci.Visible;
        _sprite.Visible = parentVisible;
        if (!parentVisible) return;

        if (map == null)
        {
            _sprite.Visible = false;
            return;
        }

        _elapsedTime += deltaTime;

        // Detect map identity change
        bool mapChanged = false;
        if (_lastMap == null || !_lastMap.TryGetTarget(out var prev) || !ReferenceEquals(prev, map))
        {
            _lastMap = new System.WeakReference<MapData>(map);
            mapChanged = true;
        }
        if (_lastMapW != map.Width || _lastMapH != map.Height || mapChanged)
        {
            _lastMapW = map.Width;
            _lastMapH = map.Height;
            _maskDirty = true;
        }

        // ── Mask rebuild ──
        if (_maskDirty)
        {
            RebuildOcclusionMask(map);
            _maskDirty = false;
        }

        // ── Sprite sizing (panel-sized, same trick as fog) ──
        // Always visible when the tool is active — even with 0 lights the
        // ambient darkening still applies (the multiply blend dims the scene
        // to the ambient color everywhere).
        _sprite.Position = Vector2.Zero;
        _sprite.Scale = panelSize;
        _sprite.Visible = true;

        // ── Upload uniforms ──
        _material.SetShaderParameter("occlusion_mask", _maskTexture!);
        // Must match the mask dimensions: (Width+1) tiles × 32 px/tile.
        _material.SetShaderParameter("map_pixel_size",
            new Vector2((map.Width + 1) * TileSize, (map.Height + 1) * TileSize));
        _material.SetShaderParameter("rect_world_origin", worldOrigin);
        _material.SetShaderParameter("rect_world_size", worldSize);
        _material.SetShaderParameter("ambient", _ambient);

        // Light array
        var lights = map.LightData.Lights;
        int count = Mathf.Min(lights.Count, 32);
        for (int i = 0; i < count; i++)
        {
            var ml = lights[i];
            int b = i * 4;
            // Tile center in DrawTileGrh coordinates: tileX*32 + 16.
            // DrawTileGrh uses tileX * TileSize as the LEFT edge of the tile,
            // so center = tileX * 32 + 16 = (tileX + 0.5) * 32.
            // Pre-multiply energy × flicker × pulse into lights[i].w so the
            // shader doesn't need a separate float[] array (which Godot
            // silently rejects — only vec4[] arrays are supported).
            float energy = ml.Energy;
            if (ml.PulseHz > 0f)
                energy *= 0.7f + 0.3f * Mathf.Sin(_elapsedTime * ml.PulseHz * Mathf.Tau);
            if (ml.FlickerPct > 0)
                energy *= 1f - (ml.FlickerPct / 100f) * _rng.RandfRange(0f, 1f);

            _lightData[b]     = (ml.X + 0.5f) * TileSize;
            _lightData[b + 1] = (ml.Y + 0.5f) * TileSize;
            _lightData[b + 2] = ml.Radius * TileSize;
            _lightData[b + 3] = Mathf.Max(energy, 0f);

            _lightColorData[b]     = ml.R / 255f;
            _lightColorData[b + 1] = ml.G / 255f;
            _lightColorData[b + 2] = ml.B / 255f;
            _lightColorData[b + 3] = 1f;
        }
        // Zero-out remaining slots
        for (int i = count; i < 32; i++)
        {
            int b = i * 4;
            _lightData[b] = _lightData[b + 1] = _lightData[b + 2] = _lightData[b + 3] = 0f;
            _lightColorData[b] = _lightColorData[b + 1] = _lightColorData[b + 2] = 0f;
            _lightColorData[b + 3] = 1f;
        }

        _material.SetShaderParameter("lights", _lightData);
        _material.SetShaderParameter("light_colors", _lightColorData);
        _material.SetShaderParameter("num_lights", count);

        // Character occluder
        if (characterWorldPx.HasValue)
        {
            _material.SetShaderParameter("character_pos", characterWorldPx.Value);
            _material.SetShaderParameter("character_half", 10f);
        }
        else
        {
            _material.SetShaderParameter("character_pos", new Vector2(-1f, -1f));
        }
    }

    /// <summary>
    /// Build the occlusion mask from L3 GRH sprite alphas and Blocked tiles.
    /// Each L3 tile's GRH is sampled at the correct draw position (center-
    /// horizontal, bottom-aligned — matching <c>MapViewport.DrawTileGrh</c>
    /// with <c>center:true</c>) and its opaque pixels are stamped as white
    /// in the mask.
    ///
    /// Resolution: <see cref="MaskPixelsPerTile"/> mask pixels per tile axis.
    /// Each mask pixel covers <see cref="MaskDownsample"/>×MaskDownsample
    /// world pixels.
    /// </summary>
    private void RebuildOcclusionMask(MapData map)
    {
        // +1 tile padding: tiles are 1-indexed and DrawTileGrh uses
        // tileX * 32 (not (tileX-1)*32), so the last tile starts at
        // pixel Width*32 and can extend further. Extra tile prevents
        // boundary sprites from clipping at the mask edge.
        int maskW = (map.Width + 1) * MaskPixelsPerTile;
        int maskH = (map.Height + 1) * MaskPixelsPerTile;

        if (_maskImage == null || _maskImage.GetWidth() != maskW || _maskImage.GetHeight() != maskH)
        {
            _maskImage = Image.CreateEmpty(maskW, maskH, false, Image.Format.L8);
            _maskTexture = null;
        }
        _maskImage.Fill(Colors.Black);

        for (int ty = 1; ty <= map.Height; ty++)
        {
            for (int tx = 1; tx <= map.Width; tx++)
            {
                ref var tile = ref map.Tiles[tx, ty];

                if (tile.Layer3 != 0 && _grhs != null && _textures != null)
                {
                    StampGrhAlpha(map, tx, ty, tile.Layer3);
                }
                else if (tile.Blocked)
                {
                    // Blocked-only tile: fill the full tile area in the mask.
                    // Uses tx * MaskPixelsPerTile to match DrawTileGrh's
                    // coordinate system where tile (1,1) draws at world (32, 32).
                    int baseX = tx * MaskPixelsPerTile;
                    int baseY = ty * MaskPixelsPerTile;
                    for (int dy = 0; dy < MaskPixelsPerTile; dy++)
                        for (int dx = 0; dx < MaskPixelsPerTile; dx++)
                        {
                            int mx = baseX + dx;
                            int my = baseY + dy;
                            if (mx >= 0 && mx < maskW && my >= 0 && my < maskH)
                                _maskImage.SetPixel(mx, my, Colors.White);
                        }
                }
            }
        }

        if (_maskTexture == null)
            _maskTexture = ImageTexture.CreateFromImage(_maskImage);
        else
            _maskTexture.Update(_maskImage);
    }

    /// <summary>Sample a single L3 GRH's alpha and stamp opaque pixels into
    /// the mask at the correct draw position.</summary>
    private void StampGrhAlpha(MapData map, int tileX, int tileY, int grhIdx)
    {
        if (_grhs == null || _textures == null) return;
        if (grhIdx <= 0 || grhIdx >= _grhs.Length) return;

        var grh = _grhs[grhIdx];
        // Resolve animation → frame 0
        if (grh.NumFrames > 1 && grh.Frames != null && grh.Frames.Length > 0)
        {
            int f0 = grh.Frames[0];
            if (f0 > 0 && f0 < _grhs.Length) grh = _grhs[f0];
        }
        if (grh.PixelWidth <= 0 || grh.PixelHeight <= 0 || grh.FileNum <= 0) return;

        var srcImg = _textures.GetImageCached(grh.FileNum);
        if (srcImg == null) return;

        int imgW = srcImg.GetWidth();
        int imgH = srcImg.GetHeight();

        // Draw position — must match DrawTileGrh(grhIdx, tileX, tileY, center:true):
        //   drawX = tileX * TileSize + (TileSize - PixelWidth) / 2f
        //   drawY = tileY * TileSize + (TileSize - PixelHeight)
        // Note: tileX is 1-indexed, and DrawTileGrh uses tileX * 32 (NOT (tileX-1)*32).
        // Tile (1,1) draws at world pixel (32, 32), not (0, 0).
        float worldDrawX = tileX * TileSize + (TileSize - grh.PixelWidth) / 2f;
        float worldDrawY = tileY * TileSize + (TileSize - grh.PixelHeight);

        int maskW = (map.Width + 1) * MaskPixelsPerTile;
        int maskH = (map.Height + 1) * MaskPixelsPerTile;

        // Iterate over each mask pixel that the sprite covers.
        int maskStartX = (int)(worldDrawX / MaskDownsample);
        int maskStartY = (int)(worldDrawY / MaskDownsample);
        int maskEndX = (int)((worldDrawX + grh.PixelWidth) / MaskDownsample);
        int maskEndY = (int)((worldDrawY + grh.PixelHeight) / MaskDownsample);

        for (int my = maskStartY; my <= maskEndY; my++)
        {
            for (int mx = maskStartX; mx <= maskEndX; mx++)
            {
                if (mx < 0 || mx >= maskW || my < 0 || my >= maskH) continue;

                // Convert mask pixel → source image pixel
                float worldX = mx * MaskDownsample - worldDrawX;
                float worldY = my * MaskDownsample - worldDrawY;
                int srcX = grh.SX + (int)worldX;
                int srcY = grh.SY + (int)worldY;

                if (srcX < 0 || srcX >= imgW || srcY < 0 || srcY >= imgH) continue;

                var pixel = srcImg.GetPixel(srcX, srcY);
                if (pixel.A > 0.3f)
                    _maskImage!.SetPixel(mx, my, Colors.White);
            }
        }
    }

    public void Cleanup()
    {
        if (_sprite != null && GodotObject.IsInstanceValid(_sprite))
            _sprite.QueueFree();
        _sprite = null;
        _material = null;
        _canvasTexture = null;
        _shader = null;
        _maskImage = null;
        _maskTexture = null;
        _grhs = null;
        _textures = null;
        _parent = null;
        _lastMap = null;
        _lastMapW = -1;
        _lastMapH = -1;
    }
}
