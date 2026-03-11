#nullable enable
using System;
using System.Collections.Generic;
using Godot;

namespace AOWorldEditor.Data;

/// <summary>
/// LRU cache of textures from Graficos/ folder.
/// Black (0,0,0) color key → transparent.
/// Supports bulk preload at startup to avoid runtime freezes.
/// </summary>
public class TextureManager
{
    private readonly string _graficosPath;
    private readonly Dictionary<int, Texture2D> _cache = new();
    private readonly LinkedList<int> _lruOrder = new();
    private readonly Dictionary<int, LinkedListNode<int>> _lruNodes = new(); // O(1) LRU removal
    private const int MaxCacheSize = 4096; // Large cache — preloaded textures stay resident
    private const byte BlackThreshold = 3;

    // Preload progress (poll from UI)
    public int PreloadTotal { get; private set; }
    public int PreloadDone { get; private set; }
    public bool PreloadFinished { get; private set; }

    public TextureManager(string graficosPath)
    {
        _graficosPath = graficosPath;
    }

    /// <summary>
    /// Collect all unique FileNums from GrhData and preload them.
    /// Call once after GrhLoader.Load(). Returns an enumerator that
    /// loads one texture per iteration (call from _Process for non-blocking UI).
    /// </summary>
    public IEnumerator<int> PreloadAll(GrhData[] grhs)
    {
        var fileNums = new HashSet<int>();
        for (int i = 1; i < grhs.Length; i++)
        {
            int fn = grhs[i].FileNum;
            if (fn > 0) fileNums.Add(fn);
        }

        PreloadTotal = fileNums.Count;
        PreloadDone = 0;
        PreloadFinished = false;

        foreach (int fn in fileNums)
        {
            LoadAndCache(fn);
            PreloadDone++;
            yield return fn;
        }

        PreloadFinished = true;
        GD.Print($"[TextureManager] Preloaded {PreloadDone} textures ({_cache.Count} cached)");
    }

    public Texture2D? GetTexture(int fileNum)
    {
        if (fileNum <= 0) return null;

        if (_cache.TryGetValue(fileNum, out var cached))
        {
            // Bump LRU — O(1) using node lookup
            if (_lruNodes.TryGetValue(fileNum, out var node))
            {
                _lruOrder.Remove(node);
                var newNode = _lruOrder.AddFirst(fileNum);
                _lruNodes[fileNum] = newNode;
            }
            return cached;
        }

        // Cache miss — load on demand (fallback for files not in GrhData)
        return LoadAndCache(fileNum);
    }

    public Rect2 GetGrhRect(GrhData grh)
    {
        return new Rect2(grh.SX, grh.SY, grh.PixelWidth, grh.PixelHeight);
    }

    private Texture2D? LoadAndCache(int fileNum)
    {
        if (_cache.TryGetValue(fileNum, out var existing))
            return existing;

        string filePath = System.IO.Path.Combine(_graficosPath, $"{fileNum}.png");
        if (!System.IO.File.Exists(filePath))
            return null;

        var image = Image.LoadFromFile(filePath);
        if (image == null) return null;

        ApplyBlackColorKeyFast(image);
        var texture = ImageTexture.CreateFromImage(image);

        // Evict LRU if over capacity
        if (_cache.Count >= MaxCacheSize && _lruOrder.Count > 0)
        {
            int evict = _lruOrder.Last!.Value;
            _lruOrder.RemoveLast();
            _lruNodes.Remove(evict);
            _cache.Remove(evict);
        }

        _cache[fileNum] = texture;
        var newNode = _lruOrder.AddFirst(fileNum);
        _lruNodes[fileNum] = newNode;
        return texture;
    }

    /// <summary>
    /// Fast black color key using raw byte buffer instead of per-pixel Get/SetPixel.
    /// ~20-50x faster on large images (2048x2048 = 4M pixels → 16MB buffer).
    /// </summary>
    private static void ApplyBlackColorKeyFast(Image image)
    {
        if (image.GetFormat() != Image.Format.Rgba8)
            image.Convert(Image.Format.Rgba8);

        byte[] data = image.GetData();
        int len = data.Length;

        for (int i = 0; i < len; i += 4)
        {
            if (data[i] <= BlackThreshold &&     // R
                data[i + 1] <= BlackThreshold &&  // G
                data[i + 2] <= BlackThreshold)    // B
            {
                data[i + 3] = 0; // A = transparent
            }
        }

        var result = Image.CreateFromData(
            image.GetWidth(), image.GetHeight(),
            false, Image.Format.Rgba8, data);
        image.CopyFrom(result);
    }
}
