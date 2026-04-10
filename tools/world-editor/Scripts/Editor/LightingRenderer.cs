#nullable enable
using System;
using Godot;
using AOWorldEditor.Data;

namespace AOWorldEditor.Editor;

/// <summary>
/// Replicates the client's lighting pipeline for the world editor.
/// Mirrors client/Scripts/Game/LightSystem.cs + WorldRenderer.cs shader.
///
/// Pipeline:
/// 1. For each MapTile with HasLight, compute per-corner colors via radial
///    falloff from light RGB to ambient (D3DXColorLerp equivalent).
/// 2. Write corners to a (W+1)×(H+1) RGB8 Image.
/// 3. Upload as ImageTexture and bind to a ShaderMaterial that multiplies
///    COLOR.rgb by the sampled lightmap texel in the fragment shader.
/// </summary>
public class LightingRenderer
{
    private const int TileSize = 32;

    // ── Lightmap shader (identical to client WorldRenderer) ─────────────
    private const string LightmapShaderCode = @"
shader_type canvas_item;
uniform sampler2D lightmap : filter_linear, repeat_disable;
uniform vec2 world_origin;
uniform vec2 map_size_px;
uniform bool use_lightmap;
varying vec2 v_world_px;

void vertex() {
    v_world_px = VERTEX + world_origin;
}

void fragment() {
    if (use_lightmap) {
        vec2 uv = (v_world_px - 32.0) / map_size_px;
        vec3 light = texture(lightmap, uv).rgb;
        COLOR.rgb *= light;
    }
}
";

    public ShaderMaterial Material { get; }
    private readonly ImageTexture _texture;
    private Image _image;
    private int _lastWidth;
    private int _lastHeight;

    public LightingRenderer()
    {
        var shader = new Shader { Code = LightmapShaderCode };
        Material = new ShaderMaterial { Shader = shader };
        _image = Image.CreateEmpty(2, 2, false, Image.Format.Rgb8);
        _texture = ImageTexture.CreateFromImage(_image);
        Material.SetShaderParameter("lightmap", _texture);
        Material.SetShaderParameter("use_lightmap", false);
        Material.SetShaderParameter("map_size_px", new Vector2(3200f, 3200f));
        Material.SetShaderParameter("world_origin", Vector2.Zero);
    }

    /// <summary>Enable or disable lightmap sampling in the shader.</summary>
    public void SetEnabled(bool enabled)
    {
        Material.SetShaderParameter("use_lightmap", enabled);
    }

    /// <summary>
    /// Rebuild the lightmap from the map's tile light data plus a base ambient color.
    /// Call whenever the map changes, a light is placed/erased, or ambient changes.
    /// </summary>
    public void Rebuild(MapData map, int ambR, int ambG, int ambB)
    {
        if (map == null) return;

        int w = map.Width;
        int h = map.Height;
        int sizeX = w + 1;
        int sizeY = h + 1;

        // Ambient baseline [0,1]
        float ambRf = ambR / 255f;
        float ambGf = ambG / 255f;
        float ambBf = ambB / 255f;

        // Per-corner colors: 4 corners per tile (NW=1, NE=3, SW=0, SE=2)
        // Store as flat float array sized (w+1) * (h+1) * 3 (RGB).
        // Initialize all to ambient.
        float[] corners = new float[sizeX * sizeY * 3];
        for (int i = 0; i < corners.Length; i += 3)
        {
            corners[i] = ambRf;
            corners[i + 1] = ambGf;
            corners[i + 2] = ambBf;
        }

        // Apply each light's contribution using radial falloff + MAX blend.
        for (int ly = 1; ly <= h; ly++)
        {
            for (int lx = 1; lx <= w; lx++)
            {
                ref var tile = ref map.Tiles[lx, ly];
                if (!tile.HasLight) continue;

                float lightR = tile.LightR / 255f;
                float lightG = tile.LightG / 255f;
                float lightB = tile.LightB / 255f;
                int range = tile.LightRange;
                int rangePx = range * TileSize;

                // Light center in world pixels (tile's center)
                float cx = (lx - 1) * TileSize + TileSize / 2f;
                float cy = (ly - 1) * TileSize + TileSize / 2f;

                // Bounding box of affected tile corners
                int minTx = Math.Max(0, lx - 1 - range);
                int maxTx = Math.Min(w, lx - 1 + range + 1);
                int minTy = Math.Max(0, ly - 1 - range);
                int maxTy = Math.Min(h, ly - 1 + range + 1);

                for (int py = minTy; py <= maxTy; py++)
                {
                    for (int px = minTx; px <= maxTx; px++)
                    {
                        // Corner position in world pixels
                        float cornerX = px * TileSize;
                        float cornerY = py * TileSize;

                        float dx = cx - cornerX;
                        float dy = cy - cornerY;
                        float dist = MathF.Sqrt(dx * dx + dy * dy);
                        if (dist > rangePx) continue;

                        float factor = dist / rangePx;
                        float r = lightR + (ambRf - lightR) * factor;
                        float g = lightG + (ambGf - lightG) * factor;
                        float b = lightB + (ambBf - lightB) * factor;

                        int idx = (py * sizeX + px) * 3;
                        // MAX blend — lights only brighten
                        if (r > corners[idx]) corners[idx] = r;
                        if (g > corners[idx + 1]) corners[idx + 1] = g;
                        if (b > corners[idx + 2]) corners[idx + 2] = b;
                    }
                }
            }
        }

        // Convert to RGB8 byte array for the texture
        byte[] pixels = new byte[sizeX * sizeY * 3];
        for (int i = 0; i < corners.Length; i++)
        {
            float v = corners[i];
            if (v < 0f) v = 0f;
            if (v > 1f) v = 1f;
            pixels[i] = (byte)(v * 255f);
        }

        // Update or recreate the image
        if (_lastWidth != sizeX || _lastHeight != sizeY)
        {
            _image = Image.CreateFromData(sizeX, sizeY, false, Image.Format.Rgb8, pixels);
            _texture.SetImage(_image);
            _lastWidth = sizeX;
            _lastHeight = sizeY;
        }
        else
        {
            _image.SetData(sizeX, sizeY, false, Image.Format.Rgb8, pixels);
            _texture.Update(_image);
        }

        Material.SetShaderParameter("lightmap", _texture);
        Material.SetShaderParameter("map_size_px", new Vector2(w * TileSize, h * TileSize));
    }

    /// <summary>
    /// Update the shader world origin so sampling aligns with the currently rendered area.
    /// For MapViewport (uses DrawSetTransform with CameraOffset + Zoom), origin must be zero
    /// because VERTEX is already in world space after the transform.
    /// For WalkModePanel (draws relative to viewport), origin is the top-left tile's world pixel.
    /// </summary>
    public void SetWorldOrigin(Vector2 originPx)
    {
        Material.SetShaderParameter("world_origin", originPx);
    }
}
