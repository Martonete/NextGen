#nullable enable
using System.Collections.Generic;
using Godot;

namespace AOWorldEditor.Data;

/// <summary>
/// LRU cache of textures from Graficos/ folder.
/// Black (0,0,0) color key → transparent.
/// </summary>
public class TextureManager
{
    private readonly string _graficosPath;
    private readonly Dictionary<int, Texture2D> _cache = new();
    private readonly LinkedList<int> _lruOrder = new();
    private const int MaxCacheSize = 512; // Larger cache for editor
    private const byte BlackThreshold = 3;

    public TextureManager(string graficosPath)
    {
        _graficosPath = graficosPath;
    }

    public Texture2D? GetTexture(int fileNum)
    {
        if (fileNum <= 0) return null;

        if (_cache.TryGetValue(fileNum, out var cached))
        {
            _lruOrder.Remove(fileNum);
            _lruOrder.AddFirst(fileNum);
            return cached;
        }

        string filePath = System.IO.Path.Combine(_graficosPath, $"{fileNum}.png");
        if (!System.IO.File.Exists(filePath))
            return null;

        var image = Image.LoadFromFile(filePath);
        if (image == null) return null;

        ApplyBlackColorKey(image);
        var texture = ImageTexture.CreateFromImage(image);

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

    public Rect2 GetGrhRect(GrhData grh)
    {
        return new Rect2(grh.SX, grh.SY, grh.PixelWidth, grh.PixelHeight);
    }

    private static void ApplyBlackColorKey(Image image)
    {
        if (image.GetFormat() != Image.Format.Rgba8)
            image.Convert(Image.Format.Rgba8);

        int width = image.GetWidth();
        int height = image.GetHeight();

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                Color pixel = image.GetPixel(x, y);
                byte r = (byte)(pixel.R * 255);
                byte g = (byte)(pixel.G * 255);
                byte b = (byte)(pixel.B * 255);

                if (r <= BlackThreshold && g <= BlackThreshold && b <= BlackThreshold)
                    image.SetPixel(x, y, new Color(0, 0, 0, 0));
            }
        }
    }
}
