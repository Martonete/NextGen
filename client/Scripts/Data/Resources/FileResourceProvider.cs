#nullable enable
using System.IO;
using Godot;

namespace ArgentumNextgen.Data.Resources;

public class FileResourceProvider : IResourceProvider
{
    private readonly string _basePath;

    public string BasePath => _basePath;

    public FileResourceProvider(string basePath)
    {
        _basePath = basePath;
    }

    public byte[] ReadBytes(string relativePath)
    {
        string fullPath = Path.Combine(_basePath, relativePath);
        return File.ReadAllBytes(fullPath);
    }

    public bool Exists(string relativePath)
    {
        string fullPath = Path.Combine(_basePath, relativePath);
        return File.Exists(fullPath);
    }

    public Image? ReadImage(string relativePath)
    {
        string fullPath = Path.Combine(_basePath, relativePath);
        if (!File.Exists(fullPath)) return null;
        return Image.LoadFromFile(fullPath);
    }
}
