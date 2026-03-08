using System;
using System.Collections.Generic;
using Godot;

namespace ArgentumNextgen.Data;

/// <summary>
/// LRU cache of textures loaded from Graficos/ folder.
/// Files are numbered PNGs: 1.png, 2.png, etc.
/// AO sprites use black (0,0,0) as color key — converted to transparent on load.
/// </summary>
public class TextureManager
{
    private readonly string _graficosPath;
    private readonly Dictionary<int, Texture2D> _cache = new();
    private readonly LinkedList<int> _lruOrder = new();
    private const int MaxCacheSize = 256;

    // Black color key threshold: pixels with R,G,B all <= this value become transparent.
    // Using a small threshold (not just exact 0,0,0) to catch near-black compression artifacts.
    private const byte BlackThreshold = 3;

    public TextureManager(string graficosPath)
    {
        _graficosPath = graficosPath;
    }

    /// <summary>
    /// Get texture by file number. Lazy loads with black-to-alpha conversion and caches.
    /// </summary>
    public Texture2D? GetTexture(int fileNum)
    {
        if (fileNum <= 0) return null;

        if (_cache.TryGetValue(fileNum, out var cached))
        {
            _lruOrder.Remove(fileNum);
            _lruOrder.AddFirst(fileNum);
            return cached;
        }

        // Load from disk
        string filePath = System.IO.Path.Combine(_graficosPath, $"{fileNum}.png");
        if (!System.IO.File.Exists(filePath))
            return null;

        var image = Image.LoadFromFile(filePath);
        if (image == null) return null;

        // Apply black color key transparency
        ApplyBlackColorKey(image);

        var texture = ImageTexture.CreateFromImage(image);

        // Evict LRU if cache full
        if (_cache.Count >= MaxCacheSize && _lruOrder.Count > 0)
        {
            int evict = _lruOrder.Last!.Value;
            _lruOrder.RemoveLast();
            _cache.Remove(evict);
        }

        _cache[fileNum] = texture;
        _lruOrder.AddFirst(fileNum);
        return texture;
    }

    /// <summary>
    /// Convert black pixels (color key) to transparent.
    /// AO sprites use pure black (0,0,0) as the transparency color.
    /// Small threshold handles JPEG/PNG compression artifacts near black.
    /// </summary>
    private static void ApplyBlackColorKey(Image image)
    {
        // Ensure we can read/write RGBA pixels
        if (image.GetFormat() != Image.Format.Rgba8)
            image.Convert(Image.Format.Rgba8);

        int width = image.GetWidth();
        int height = image.GetHeight();

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                Color pixel = image.GetPixel(x, y);

                // Check if pixel is black (or near-black from compression)
                byte r = (byte)(pixel.R * 255);
                byte g = (byte)(pixel.G * 255);
                byte b = (byte)(pixel.B * 255);

                if (r <= BlackThreshold && g <= BlackThreshold && b <= BlackThreshold)
                {
                    image.SetPixel(x, y, new Color(0, 0, 0, 0));
                }
            }
        }
    }

    /// <summary>
    /// Get the source rect for a GRH within its texture.
    /// </summary>
    public Rect2 GetGrhRect(GrhData grh)
    {
        return new Rect2(grh.SX, grh.SY, grh.PixelWidth, grh.PixelHeight);
    }
}
