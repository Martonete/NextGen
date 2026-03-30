#nullable enable
namespace ArgentumNextgen.Data.Resources;

/// <summary>
/// Abstraction for reading game resource files.
/// Implementations: FileResourceProvider (loose files), AopakResourceProvider (encrypted archives, future).
/// </summary>
public interface IResourceProvider
{
    /// <summary>Read entire file as byte array. Throws if not found.</summary>
    byte[] ReadBytes(string relativePath);

    /// <summary>Check if a resource exists.</summary>
    bool Exists(string relativePath);

    /// <summary>Read a PNG file and return as Godot Image.</summary>
    Godot.Image? ReadImage(string relativePath);

    /// <summary>The base path (for logging/debugging). May be empty for archive providers.</summary>
    string BasePath { get; }
}
