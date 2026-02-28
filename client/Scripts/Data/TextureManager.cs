using System;
using System.Collections.Generic;
using Godot;

namespace TierrasSagradasAO.Data;

/// <summary>
/// LRU cache of textures loaded from Graficos/ folder.
/// Files are numbered PNGs: 1.png, 2.png, etc.
/// </summary>
public class TextureManager
{
    private readonly string _graficosPath;
    private readonly Dictionary<int, Texture2D> _cache = new();
    private readonly LinkedList<int> _lruOrder = new();
    private const int MaxCacheSize = 256;

    public TextureManager(string graficosPath)
    {
        _graficosPath = graficosPath;
    }

    /// <summary>
    /// Get texture by file number. Lazy loads and caches.
    /// </summary>
    public Texture2D? GetTexture(int fileNum)
    {
        if (fileNum <= 0) return null;

        if (_cache.TryGetValue(fileNum, out var cached))
        {
            // Move to front of LRU
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
    /// Get the source rect for a GRH within its texture.
    /// </summary>
    public Rect2 GetGrhRect(GrhData grh)
    {
        return new Rect2(grh.SX, grh.SY, grh.PixelWidth, grh.PixelHeight);
    }
}
