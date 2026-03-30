#nullable enable
using System;
using System.Collections.Generic;
using AoPak;
using Godot;

namespace ArgentumNextgen.Data.Resources;

/// <summary>
/// Reads game resources from encrypted .aopak archives.
///
/// Naming convention: the first path segment (lowercased) + ".aopak" is the archive name,
/// and everything after the first slash is the entry name inside that archive.
///
/// Examples:
///   "Graficos/1.png"        → graficos.aopak, entry "1.png"
///   "INIT/Graficos.ind"     → init.aopak,     entry "Graficos.ind"
///   "UI/Icons/sword.png"    → ui.aopak,        entry "Icons/sword.png"
///   "Fonts/Liberation.ttf"  → fonts.aopak,     entry "Liberation.ttf"
/// </summary>
public class AopakResourceProvider : IResourceProvider, IDisposable
{
    private readonly Dictionary<string, AopakReader> _readers = new(StringComparer.OrdinalIgnoreCase);
    private readonly byte[] _amk;

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

    /// <summary>
    /// Splits "Folder/path/to/entry.ext" into ("folder.aopak", "path/to/entry.ext").
    /// Convention: first segment lowercased → archive name; remainder → entry name.
    /// </summary>
    private static (string ArchiveName, string EntryName) SplitPath(string relativePath)
    {
        int slash = relativePath.IndexOf('/');
        if (slash <= 0)
            return (relativePath.ToLowerInvariant() + ".aopak", relativePath);
        return (relativePath[..slash].ToLowerInvariant() + ".aopak",
                relativePath[(slash + 1)..]);
    }

    public bool Exists(string relativePath)
    {
        string? entryName = ResolveEntryName(relativePath, out _);
        return entryName != null;
    }

    public bool IsTombstone(string relativePath)
    {
        var (archiveName, entryName) = SplitPath(relativePath);

        if (_readers.TryGetValue(archiveName, out var reader) && reader.IsTombstone(entryName))
            return true;

        // Fallback: direct match (entries stored without a folder prefix)
        foreach (var r in _readers.Values)
        {
            if (r.IsTombstone(relativePath))
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

        string ext = System.IO.Path.GetExtension(relativePath).ToLowerInvariant();
        Error error = ext switch
        {
            ".jpg" or ".jpeg" => image.LoadJpgFromBuffer(data),
            ".webp"           => image.LoadWebpFromBuffer(data),
            _                 => image.LoadPngFromBuffer(data),
        };

        if (error != Error.Ok)
        {
            GD.PrintErr($"[AoPak] Failed to load image {relativePath}: {error}");
            return null;
        }
        return image;
    }

    /// <summary>
    /// Resolve a client relative path to the (entryName, reader) pair.
    /// Uses the naming convention: first segment → archive, remainder → entry.
    /// Falls back to direct match across all archives.
    /// </summary>
    private string? ResolveEntryName(string relativePath, out AopakReader? foundReader)
    {
        var (archiveName, entryName) = SplitPath(relativePath);

        if (_readers.TryGetValue(archiveName, out var reader) && reader.Contains(entryName))
        {
            foundReader = reader;
            return entryName;
        }

        // Fallback: direct match across all archives (for entries without a folder prefix)
        foreach (var r in _readers.Values)
        {
            if (r.Contains(relativePath))
            {
                foundReader = r;
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
