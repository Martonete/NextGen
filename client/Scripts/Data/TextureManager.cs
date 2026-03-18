using System;
using System.Collections.Generic;
using Godot;

namespace ArgentumNextgen.Data;

/// <summary>
/// LRU cache of textures loaded from Graficos/ folder.
/// Files are numbered PNGs: 1.png, 2.png, etc.
/// AO sprites use black (0,0,0) as color key — converted to transparent on load.
/// Uses fast byte-buffer processing and skips PNGs that already have alpha.
/// </summary>
public class TextureManager
{
    private readonly string _graficosPath;
    private readonly Dictionary<int, Texture2D> _cache = new();
    private readonly LinkedList<int> _lruOrder = new();
    private readonly Dictionary<int, LinkedListNode<int>> _lruNodes = new(); // O(1) LRU removal
    private const int MaxCacheSize = 4096;

    // Black color key threshold: pixels with R,G,B all <= this value become transparent.
    // Higher value (18) also removes dark shadow pixels from indexed PNGs that cause
    // flicker in windowed mode due to Windows compositor antialiasing.
    private const byte BlackThreshold = 18;

    // Preload progress (poll from UI)
    public int PreloadTotal { get; private set; }
    public int PreloadDone { get; private set; }
    public bool PreloadFinished { get; private set; }

    public TextureManager(string graficosPath)
    {
        _graficosPath = graficosPath;
    }

    /// <summary>
    /// Reset preload state so a new PreloadAll / TickPreload cycle can run.
    /// Call this on map change or when textures need re-evaluation.
    /// </summary>
    public void ResetPreload()
    {
        PreloadFinished = false;
        PreloadTotal = 0;
        PreloadDone = 0;
    }

    /// <summary>
    /// Preload all textures referenced by GrhData. Returns an enumerator for
    /// time-budgeted incremental loading (call TickPreload from _Process).
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
    /// Time-budgeted preload tick. Returns true when done.
    /// </summary>
    public bool TickPreload(IEnumerator<int> iter, double budgetMs = 8.0)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();

        while (sw.Elapsed.TotalMilliseconds < budgetMs)
        {
            if (!iter.MoveNext())
            {
                PreloadFinished = true;
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Get texture by file number. Returns cached texture or loads on demand.
    /// </summary>
    public Texture2D? GetTexture(int fileNum)
    {
        if (fileNum <= 0) return null;

        if (_cache.TryGetValue(fileNum, out var cached))
        {
            // Bump LRU — O(1)
            if (_lruNodes.TryGetValue(fileNum, out var node))
            {
                _lruOrder.Remove(node);
                var newNode = _lruOrder.AddFirst(fileNum);
                _lruNodes[fileNum] = newNode;
            }
            return cached;
        }

        return LoadAndCache(fileNum);
    }

    /// <summary>
    /// Get the source rect for a GRH within its texture.
    /// </summary>
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
    /// Skips images that already have meaningful alpha (PNG with transparency).
    /// Uses SetData to avoid double allocation.
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
        _cache.Clear();
        _lruOrder.Clear();
        _lruNodes.Clear();
    }
}
