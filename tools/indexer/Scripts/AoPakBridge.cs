using Godot;
using AoPak;
using System;
using System.Security.Cryptography;
using System.Text;

namespace AoIndexer;

/// <summary>
/// C# bridge for GDScript to access .aopak archives.
/// Add as autoload in project.godot or instantiate from GDScript.
/// </summary>
public partial class AoPakBridge : Node
{
    private AopakReader? _reader;
    private string? _archivePath;
    private byte[]? _amk;

    /// <summary>Open an .aopak archive for reading.</summary>
    public bool OpenArchive(string path)
    {
        try
        {
            _amk = GetAmk();
            _reader = new AopakReader(path, _amk);
            _archivePath = path;
            GD.Print($"[AoPak] Opened: {path} ({_reader.GetEntryNames().Count} entries)");
            return true;
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[AoPak] Failed to open {path}: {ex.Message}");
            return false;
        }
    }

    /// <summary>Check if an entry exists in the open archive.</summary>
    public bool HasEntry(string name)
    {
        return _reader?.Contains(name) ?? false;
    }

    /// <summary>Read an entry as byte array.</summary>
    public byte[] ReadEntry(string name)
    {
        if (_reader == null) return Array.Empty<byte>();
        try
        {
            return _reader.ReadEntry(name);
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[AoPak] ReadEntry failed for {name}: {ex.Message}");
            return Array.Empty<byte>();
        }
    }

    /// <summary>Read a PNG entry and return as Image.</summary>
    public Image? ReadImage(string name)
    {
        byte[] data = ReadEntry(name);
        if (data.Length == 0) return null;
        var image = new Image();
        var error = image.LoadPngFromBuffer(data);
        return error == Error.Ok ? image : null;
    }

    /// <summary>Write/update an entry in the archive.</summary>
    public bool WriteEntry(string name, byte[] data)
    {
        if (_archivePath == null || _amk == null) return false;
        try
        {
            AopakWriter.UpdateEntry(_archivePath, name, data, _amk);
            // Reopen reader to pick up changes
            _reader?.Dispose();
            _reader = new AopakReader(_archivePath, _amk);
            return true;
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[AoPak] WriteEntry failed for {name}: {ex.Message}");
            return false;
        }
    }

    /// <summary>Get all entry names as a string array.</summary>
    public string[] ListEntries()
    {
        if (_reader == null) return Array.Empty<string>();
        var names = _reader.GetEntryNames();
        var result = new string[names.Count];
        int i = 0;
        foreach (var name in names) result[i++] = name;
        return result;
    }

    /// <summary>Close the archive.</summary>
    public void CloseArchive()
    {
        _reader?.Dispose();
        _reader = null;
        _archivePath = null;
    }

    public override void _ExitTree()
    {
        CloseArchive();
        base._ExitTree();
    }

    private static byte[] GetAmk()
    {
        var envKey = Environment.GetEnvironmentVariable("AOPAK_KEY");
        if (!string.IsNullOrEmpty(envKey))
            return SHA256.HashData(Encoding.UTF8.GetBytes(envKey));
        return SHA256.HashData(Encoding.UTF8.GetBytes("argentum-nextgen-dev-key-2026"));
    }
}
