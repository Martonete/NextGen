#nullable enable
using System;
using System.Collections.Generic;
using System.Diagnostics;
using Godot;

namespace AOWorldEditor.Data;

/// <summary>
/// LRU cache of textures from Graficos/ folder (loose files).
/// Black (0,0,0) color key → transparent.
/// Supports bulk preload at startup with time-budgeted batching
/// to avoid frame drops on large textures (2048x2048).
/// </summary>
public class TextureManager
{
    private readonly string _graficosPath;
    private readonly Dictionary<int, Texture2D> _cache = new();
    private readonly Dictionary<int, Image> _imageCache = new(); // CPU-side cache to avoid GetImage() stalls
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

    /// <summary>
    /// Time-budgeted preload tick. Loads textures until the frame budget is exhausted.
    /// Returns true when preload is complete.
    /// </summary>
    public bool TickPreload(IEnumerator<int> iter, double budgetMs = 8.0)
    {
        var sw = Stopwatch.StartNew();

        while (sw.Elapsed.TotalMilliseconds < budgetMs)
        {
            if (!iter.MoveNext())
            {
                PreloadFinished = true;
                GD.Print($"[TextureManager] Preloaded {PreloadDone} textures ({_cache.Count} cached)");
                return true; // done
            }
        }

        return false; // more to load
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

    /// <summary>
    /// Get the CPU-side Image for a texture, from cache. No GPU readback.
    /// </summary>
    public Image? GetImageCached(int fileNum)
    {
        if (fileNum <= 0) return null;
        if (_imageCache.TryGetValue(fileNum, out var img)) return img;
        // Fallback: load texture (which also caches the image)
        LoadAndCache(fileNum);
        _imageCache.TryGetValue(fileNum, out img);
        return img;
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
        Image? image = Image.LoadFromFile(filePath);

        if (image == null) return null;

        ApplyBlackColorKeyFast(image);
        _imageCache[fileNum] = image; // Cache CPU-side image to avoid GetImage() GPU stalls
        var texture = ImageTexture.CreateFromImage(image);

        // Evict LRU if over capacity
        if (_cache.Count >= MaxCacheSize && _lruOrder.Count > 0)
        {
            int evict = _lruOrder.Last!.Value;
            _lruOrder.RemoveLast();
            _lruNodes.Remove(evict);
            _cache.Remove(evict);
            _imageCache.Remove(evict);
        }

        _cache[fileNum] = texture;
        var newNode = _lruOrder.AddFirst(fileNum);
        _lruNodes[fileNum] = newNode;
        return texture;
    }

    /// <summary>
    /// Fast black color key using raw byte buffer.
    /// Matches the game client exactly: skips PNGs with existing alpha.
    /// </summary>
    private static void ApplyBlackColorKeyFast(Image image)
    {
        bool needsConvert = image.GetFormat() != Image.Format.Rgba8;
        if (needsConvert)
            image.Convert(Image.Format.Rgba8);

        byte[] data = image.GetData();
        int len = data.Length;

        // If the image already had alpha, check if it has any non-opaque pixels.
        // If yes, the PNG has proper transparency — skip color key.
        if (!needsConvert)
        {
            for (int i = 3; i < len; i += 4)
            {
                if (data[i] < 250)
                    return; // image has real transparency, don't touch it
            }
        }

        // Apply black color key: (R,G,B) near (0,0,0) → alpha=0
        bool modified = false;
        for (int i = 0; i < len; i += 4)
        {
            if (data[i] <= BlackThreshold &&
                data[i + 1] <= BlackThreshold &&
                data[i + 2] <= BlackThreshold)
            {
                data[i + 3] = 0;
                modified = true;
            }
        }

        if (modified)
        {
            image.SetData(image.GetWidth(), image.GetHeight(),
                false, Image.Format.Rgba8, data);
        }
    }

    /// <summary>Free all cached textures to release GPU memory.</summary>
    public void Cleanup()
    {
        foreach (var texture in _cache.Values)
            SafeDispose(texture);
        foreach (var image in _imageCache.Values)
            SafeDispose(image);

        _cache.Clear();
        _imageCache.Clear();
        _lruOrder.Clear();
        _lruNodes.Clear();
    }

    private static void SafeDispose(GodotObject? obj)
    {
        if (obj == null) return;
        if (!GodotObject.IsInstanceValid(obj)) return;
        obj.Dispose();
    }
}
