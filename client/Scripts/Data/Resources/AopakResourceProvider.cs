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
    private readonly Dictionary<string, AopakReader> _readers = new();
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

        // Open all .aopak files in the data directory
        foreach (var file in System.IO.Directory.GetFiles(dataPath, "*.aopak"))
        {
            try
            {
                var reader = new AopakReader(file, amk);
                _readers[System.IO.Path.GetFileName(file)] = reader;
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
        foreach (var reader in _readers.Values)
        {
            if (reader.Contains(relativePath))
                return true;
        }
        return false;
    }

    /// <summary>
    /// Returns true if the entry exists in this provider and is a tombstone (intentionally deleted).
    /// Used by CompositeResourceProvider to stop fallthrough to lower-priority providers.
    /// </summary>
    public bool IsTombstone(string relativePath)
    {
        foreach (var reader in _readers.Values)
        {
            if (reader.IsTombstone(relativePath))
                return true;
        }
        return false;
    }

    public byte[] ReadBytes(string relativePath)
    {
        foreach (var reader in _readers.Values)
        {
            if (reader.Contains(relativePath))
                return reader.ReadEntry(relativePath);
        }
        throw new System.IO.FileNotFoundException($"[AoPak] Entry not found: {relativePath}");
    }

    public Image? ReadImage(string relativePath)
    {
        if (!Exists(relativePath)) return null;

        byte[] data = ReadBytes(relativePath);
        var image = new Image();
        var error = image.LoadPngFromBuffer(data);
        if (error != Error.Ok)
        {
            GD.PrintErr($"[AoPak] Failed to load image {relativePath}: {error}");
            return null;
        }
        return image;
    }

    public void Dispose()
    {
        foreach (var reader in _readers.Values)
            reader.Dispose();
        _readers.Clear();
        Array.Clear(_amk, 0, _amk.Length);
    }
}
