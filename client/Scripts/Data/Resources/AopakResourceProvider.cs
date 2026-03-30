#nullable enable
using System;
using System.Collections.Generic;
using AoPak;
using Godot;

namespace ArgentumNextgen.Data.Resources;

/// <summary>
/// Reads game resources from encrypted .aopak archives.
/// Wraps one or more AopakReader instances.
/// </summary>
public class AopakResourceProvider : IResourceProvider, IDisposable
{
    // Each archive maps: prefix to strip → reader
    // e.g. "Graficos/" → graphics.aopak reader
    private readonly List<(string Prefix, AopakReader Reader)> _prefixedReaders = new();
    private readonly Dictionary<string, AopakReader> _readers = new();
    private readonly byte[] _amk;

    // Archive filename → directory prefix the client uses in relative paths
    private static readonly Dictionary<string, string> ArchivePrefixMap = new(StringComparer.OrdinalIgnoreCase)
    {
        { "graphics.aopak", "Graficos/" },
        { "inits.aopak", "INIT/" },
        { "maps.aopak", "Maps/" },
        { "sounds.aopak", "Sounds/" },
    };

    public string BasePath { get; }

    /// <summary>
    /// Create provider from a directory containing .aopak files.
    /// Opens all .aopak files found in the directory.
    /// </summary>
    public AopakResourceProvider(string dataPath, byte[] amk)
    {
        BasePath = dataPath;
        _amk = amk;

        foreach (var file in System.IO.Directory.GetFiles(dataPath, "*.aopak"))
        {
            try
            {
                var reader = new AopakReader(file, amk);
                string fileName = System.IO.Path.GetFileName(file);
                _readers[fileName] = reader;

                if (ArchivePrefixMap.TryGetValue(fileName, out string? prefix))
                    _prefixedReaders.Add((prefix, reader));
            }
            catch (Exception ex)
            {
                GD.PrintErr($"[AoPak] Failed to open {file}: {ex.Message}");
            }
        }

        GD.Print($"[AoPak] Loaded {_readers.Count} archive(s) from {dataPath}");
    }

    /// <summary>
    /// Create provider from a single .aopak file path.
    /// Used by CompositeResourceProvider when building from a manifest.
    /// </summary>
    public AopakResourceProvider(string archiveFilePath, byte[] amk, bool singleFile)
    {
        BasePath = System.IO.Path.GetDirectoryName(archiveFilePath) ?? string.Empty;
        _amk = amk;

        if (!singleFile)
            throw new ArgumentException("Use the directory constructor when singleFile is false.");

        try
        {
            var reader = new AopakReader(archiveFilePath, amk);
            _readers[System.IO.Path.GetFileName(archiveFilePath)] = reader;
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[AoPak] Failed to open {archiveFilePath}: {ex.Message}");
            throw;
        }
    }

    public bool Exists(string relativePath)
    {
        string entryName = ResolveEntryName(relativePath, out _);
        return entryName != null;
    }

    public bool IsTombstone(string relativePath)
    {
        foreach (var (prefix, reader) in _prefixedReaders)
        {
            if (relativePath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                string stripped = relativePath[prefix.Length..];
                if (reader.IsTombstone(stripped))
                    return true;
            }
        }
        // Also check without prefix stripping (direct match)
        foreach (var reader in _readers.Values)
        {
            if (reader.IsTombstone(relativePath))
                return true;
        }
        return false;
    }

    public byte[] ReadBytes(string relativePath)
    {
        string? entryName = ResolveEntryName(relativePath, out var reader);
        if (entryName != null && reader != null)
            return reader.ReadEntry(entryName);
        throw new System.IO.FileNotFoundException($"[AoPak] Entry not found: {relativePath}");
    }

    public Image? ReadImage(string relativePath)
    {
        string? entryName = ResolveEntryName(relativePath, out var reader);
        if (entryName == null || reader == null) return null;

        byte[] data = reader.ReadEntry(entryName);
        var image = new Image();
        var error = image.LoadPngFromBuffer(data);
        if (error != Error.Ok)
        {
            GD.PrintErr($"[AoPak] Failed to load image {relativePath}: {error}");
            return null;
        }
        return image;
    }

    /// <summary>
    /// Resolve a client relative path (e.g. "Graficos/1.png") to the entry name
    /// inside the appropriate archive (e.g. "1.png" in graphics.aopak).
    /// </summary>
    private string? ResolveEntryName(string relativePath, out AopakReader? foundReader)
    {
        // Try prefix-stripped match first (Graficos/1.png → 1.png in graphics.aopak)
        foreach (var (prefix, reader) in _prefixedReaders)
        {
            if (relativePath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                string stripped = relativePath[prefix.Length..];
                if (reader.Contains(stripped))
                {
                    foundReader = reader;
                    return stripped;
                }
            }
        }
        // Fallback: direct match across all readers
        foreach (var reader in _readers.Values)
        {
            if (reader.Contains(relativePath))
            {
                foundReader = reader;
                return relativePath;
            }
        }
        foundReader = null;
        return null;
    }

    public void Dispose()
    {
        foreach (var reader in _readers.Values)
            reader.Dispose();
        _readers.Clear();
        Array.Clear(_amk, 0, _amk.Length);
    }
}
