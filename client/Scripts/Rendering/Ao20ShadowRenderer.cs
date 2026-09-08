using System;
using System.Collections.Generic;
using Godot;
using ArgentumNextgen.Data;
using ArgentumNextgen.Game;

namespace ArgentumNextgen.Rendering;

/// <summary>
/// Faithful port of AO20's clsBatch.DrawShadow. AO20 first builds an alpha
/// silhouette and then draws it four times as a skewed, flattened quad.
/// </summary>
internal static class Ao20ShadowRenderer
{
    internal const int CompositeSize = 256;
    internal static readonly Vector2 CompositeAnchor = new(112f, 224f);
    // Slightly stronger than AO20's raw output: preserves its light gradient
    // while making silhouettes readable against bright grass/water.
    private const float ShadowOpacityMultiplier = 1.20f;

    internal readonly struct SpritePart
    {
        public SpritePart(GrhData resolved, Texture2D texture, Vector2 position)
        {
            Resolved = resolved;
            Texture = texture;
            Position = position;
        }

        public GrhData Resolved { get; }
        public Texture2D Texture { get; }
        public Vector2 Position { get; }
    }

    internal readonly struct CornerColors
    {
        // AO20 MapData.light_value: SW, NW, SE, NE.
        public CornerColors(Color sw, Color nw, Color se, Color ne)
        {
            Sw = sw; Nw = nw; Se = se; Ne = ne;
        }

        public Color Sw { get; }
        public Color Nw { get; }
        public Color Se { get; }
        public Color Ne { get; }
    }

    private sealed class CompositeCache
    {
        public readonly ImageTexture Texture;
        public readonly LinkedListNode<CompositeKey> Node;
        public CompositeCache(ImageTexture texture, CompositeKey key)
        { Texture = texture; Node = new(key); }
    }

    // Exact value keys, not just a hash: collisions cannot substitute another outfit.
    private readonly record struct PartKey(ulong Texture, int X, int Y, int W, int H, Vector2 Position);
    private readonly record struct CompositeKey(int Count, PartKey A, PartKey B, PartKey C, PartKey D, PartKey E);
    private const int CompositeLimit = 256; // <=64 MiB GPU; CPU composites are disposed immediately.
    private static readonly Dictionary<CompositeKey, CompositeCache> _characterComposites = new();
    private static readonly LinkedList<CompositeKey> _compositeLru = new();
    internal static int CachedCompositeCount => _characterComposites.Count;
    internal static long CompositeBuilds { get; private set; }
    private static readonly Dictionary<ulong, Image> _sourceImages = new();
    private static readonly Vector2[] _vertices = new Vector2[4];
    private static readonly Vector2[] _uvs = new Vector2[4];
    private static readonly Color[] _colors = new Color[4];

    /// <summary>Returns the current AO20-style four-corner light for a tile.</summary>
    internal static CornerColors GetLightCorners(GameState state, int x, int y)
    {
        if ((state.Config?.ShowLights ?? true) && state.TileLightColors != null)
        {
            return new CornerColors(
                state.TileLightColors.Get(x, y, 0), state.TileLightColors.Get(x, y, 1),
                state.TileLightColors.Get(x, y, 2), state.TileLightColors.Get(x, y, 3));
        }

        byte r = state.ZoneAmbientR != 0 || state.ZoneAmbientG != 0 || state.ZoneAmbientB != 0
            ? state.ZoneAmbientR : (byte)state.MapColorR;
        byte g = state.ZoneAmbientR != 0 || state.ZoneAmbientG != 0 || state.ZoneAmbientB != 0
            ? state.ZoneAmbientG : (byte)state.MapColorG;
        byte b = state.ZoneAmbientR != 0 || state.ZoneAmbientG != 0 || state.ZoneAmbientB != 0
            ? state.ZoneAmbientB : (byte)state.MapColorB;
        var ambient = new Color(r / 255f, g / 255f, b / 255f, 1f);
        return new CornerColors(ambient, ambient, ambient, ambient);
    }

    /// <summary>Draw a regular resolved GRH through AO20's shadow geometry.</summary>
    internal static void DrawGrhShadow(CanvasItem canvas, GameData data, int grhIndex, int frame,
        Vector2 position, bool center, CornerColors light, Vector2 offset = default)
    {
        var resolved = data.ResolveGrh(grhIndex, frame);
        if (resolved == null || resolved.FileNum <= 0) return;
        var texture = data.Textures?.GetTexture(resolved.FileNum);
        if (texture == null) return;

        Vector2 drawPosition = GetDrawPosition(resolved, position, center);
        DrawTextureShadow(canvas, texture, resolved.SX, resolved.SY, resolved.PixelWidth,
            resolved.PixelHeight, drawPosition, light, offset);
    }

    /// <summary>
    /// Build the exact 256x256 AO20 composed character texture (alpha only matters
    /// for its shadow) and project that texture once. The cache is keyed by
    /// exact frame composition, shared across characters and full animation cycles.
    /// </summary>
    internal static void DrawCharacterShadow(CanvasItem canvas, int characterId,
        IReadOnlyList<SpritePart> parts, Vector2 screenPosition, CornerColors light)
    {
        if (parts.Count == 0 || parts.Count > 5) return;

        var key = BuildKey(parts);
        if (!_characterComposites.TryGetValue(key, out var cache))
        {
            if (_characterComposites.Count >= CompositeLimit)
            {
                var oldest = _compositeLru.First!;
                _characterComposites[oldest.Value].Texture.Dispose();
                _characterComposites.Remove(oldest.Value);
                _compositeLru.RemoveFirst();
            }
            using var composite = Image.CreateEmpty(CompositeSize, CompositeSize, false, Image.Format.Rgba8);
            foreach (var part in parts)
                BlendPart(composite, part);
            cache = new CompositeCache(ImageTexture.CreateFromImage(composite), key);
            CompositeBuilds++;
            _characterComposites.Add(key, cache);
            _compositeLru.AddLast(cache.Node);
        }
        else if (cache.Node != _compositeLru.Last)
        {
            _compositeLru.Remove(cache.Node);
            _compositeLru.AddLast(cache.Node);
        }

        // VB6 PresentComposedTexture: x = x - 256/2 + 16, y = y - 256 + 32.
        DrawTextureShadow(canvas, cache.Texture, 0, 0, CompositeSize, CompositeSize,
            screenPosition - CompositeAnchor, light, Vector2.Zero);
    }

    internal static void ClearCharacterCache()
    {
        foreach (var cache in _characterComposites.Values) cache.Texture.Dispose();
        _characterComposites.Clear();
        _compositeLru.Clear();
        foreach (var image in _sourceImages.Values) image.Dispose();
        _sourceImages.Clear();
    }

    internal static bool HasLayer3Shadow(int grhIndex) =>
        grhIndex == 5624 || grhIndex == 5625 || grhIndex == 5626 || grhIndex == 5627 || grhIndex == 51716;

    private static Vector2 GetDrawPosition(GrhData resolved, Vector2 position, bool center)
    {
        if (!center) return position;
        float x = position.X;
        float y = position.Y;
        if (resolved.TileWidth != 1f && resolved.TileWidth > 0)
            x -= (int)(resolved.TileWidth * 16f) - 16;
        if (resolved.TileHeight != 1f && resolved.TileHeight > 0)
            y -= (int)(resolved.TileHeight * 32f) - 32;
        return new Vector2(x, y);
    }

    private static CompositeKey BuildKey(IReadOnlyList<SpritePart> parts)
    {
        PartKey Part(int i)
        {
            if (i >= parts.Count) return default;
            var p = parts[i];
            return new(p.Texture.GetInstanceId(), p.Resolved.SX, p.Resolved.SY,
                p.Resolved.PixelWidth, p.Resolved.PixelHeight, p.Position);
        }
        return new(parts.Count, Part(0), Part(1), Part(2), Part(3), Part(4));
    }

    private static void BlendPart(Image target, SpritePart part)
    {
        int texW = part.Texture.GetWidth(), texH = part.Texture.GetHeight();
        if (!TryGetSourceRect(part.Resolved.SX, part.Resolved.SY, part.Resolved.PixelWidth,
                              part.Resolved.PixelHeight, texW, texH, out var source))
            return;

        ulong id = part.Texture.GetInstanceId();
        if (!_sourceImages.TryGetValue(id, out var image))
        {
            // Source readbacks are only needed on cache misses; avoid retaining
            // every equipment atlas visited during a long play session.
            if (_sourceImages.Count >= 16)
            {
                foreach (var old in _sourceImages.Values) old.Dispose();
                _sourceImages.Clear();
            }
            image = part.Texture.GetImage();
            _sourceImages[id] = image;
        }

        var destination = new Vector2I((int)Math.Round(part.Position.X), (int)Math.Round(part.Position.Y));
        target.BlendRect(image, source, destination);
    }

    private static void DrawTextureShadow(CanvasItem canvas, Texture2D texture, int sx, int sy,
        int width, int height, Vector2 position, CornerColors light, Vector2 offset)
    {
        int texW = texture.GetWidth(), texH = texture.GetHeight();
        if (!TryGetSourceRect(sx, sy, width, height, texW, texH, out var source)) return;

        float u0 = (source.Position.X + 0.25f) / texW;
        float v0 = (source.Position.Y + 0.25f) / texH;
        float u1 = (source.Position.X + source.Size.X) / texW;
        float v1 = (source.Position.Y + source.Size.Y) / texH;
        float w = source.Size.X, h = source.Size.Y;

        // Godot polygon order: BL, BR, TR, TL. Map AO20's colors accordingly.
        _colors[0] = ShadowColor(light.Sw);
        _colors[1] = ShadowColor(light.Se);
        _colors[2] = ShadowColor(light.Ne);
        _colors[3] = ShadowColor(light.Nw);
        _uvs[0] = new Vector2(u0, v1);
        _uvs[1] = new Vector2(u1, v1);
        _uvs[2] = new Vector2(u1, v0);
        _uvs[3] = new Vector2(u0, v0);

        // clsBatch.DrawShadow repeats the same flattened parallelogram in a
        // 1px cardinal cross to soften the silhouette's edge.
        ReadOnlySpan<Vector2> nudges = stackalloc Vector2[]
        {
            new(0, -1), new(1, 0), new(0, 1), new(-1, 0)
        };
        foreach (Vector2 nudge in nudges)
        {
            Vector2 origin = position + offset + nudge;
            _vertices[0] = new Vector2(origin.X, origin.Y + h - 2f);             // BL / SW
            _vertices[1] = new Vector2(origin.X + w, origin.Y + h - 2f);         // BR / SE
            _vertices[2] = new Vector2(origin.X + w + h * 0.25f, origin.Y + h * 0.25f - 2f); // TR / NE
            _vertices[3] = new Vector2(origin.X + h * 0.25f, origin.Y + h * 0.25f - 2f);     // TL / NW
            canvas.DrawPolygon(_vertices, _colors, _uvs, texture);
        }
    }

    private static Color ShadowColor(Color light)
    {
        // VB6: (0.2126 * R + 0.7152 * G + 0.0722 * B)^2 * 0.000625.
        // VB stores alpha as [0,255]; Godot stores it normalized.
        float luminance = 0.2126f * (light.R * 255f) + 0.7152f * (light.G * 255f) + 0.0722f * (light.B * 255f);
        float alpha = Mathf.Clamp(luminance * luminance * 0.000625f / 255f * ShadowOpacityMultiplier, 0f, 1f);
        return new Color(0f, 0f, 0f, alpha);
    }

    private static bool TryGetSourceRect(int sx, int sy, int width, int height, int texW, int texH, out Rect2I rect)
    {
        rect = default;
        if (texW <= 0 || texH <= 0 || width <= 0 || height <= 0) return false;
        sx %= texW; if (sx < 0) sx += texW;
        sy %= texH; if (sy < 0) sy += texH;
        width = Math.Min(width, texW - sx);
        height = Math.Min(height, texH - sy);
        if (width <= 0 || height <= 0) return false;
        rect = new Rect2I(sx, sy, width, height);
        return true;
    }
}
