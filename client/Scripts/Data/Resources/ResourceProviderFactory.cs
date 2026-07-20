#nullable enable
using System.Collections.Generic;
using System.IO;
using AoPak;
using Godot;

namespace ArgentumNextgen.Data.Resources;

public static class ResourceProviderFactory
{
    /// <summary>
    /// Create the appropriate resource provider based on context.
    /// 1. If resources.aoman manifest exists: build a CompositeResourceProvider from all listed archives.
    /// 2. Else if .aopak files exist: single AopakResourceProvider (legacy, no manifest).
    /// 3. Else: FileResourceProvider (loose files).
    /// </summary>
    public static IResourceProvider Create(string dataPath)
    {
        // Check for .aopak archives (works in both editor and release)
        string manifestPath = Path.Combine(dataPath, AopakManifest.ManifestFileName);

        if (File.Exists(manifestPath))
        {
            byte[] amk = GetApplicationMasterKey();
            return CreateFromManifest(dataPath, manifestPath, amk);
        }

        bool hasArchives = Directory.Exists(dataPath) &&
            Directory.GetFiles(dataPath, "*.aopak").Length > 0;
        if (hasArchives)
        {
            byte[] amk = GetApplicationMasterKey();
            var providers = CreateLooseOverrideProviders(dataPath);
            providers.Add(new AopakResourceProvider(dataPath, amk));
            GD.Print($"[Resources] Loose override provider enabled over archives: {providers.Count - 1} folder(s)");
            return new CompositeResourceProvider(dataPath, providers);
        }

        // Fallback to loose files even in release (graceful degradation)
        return new FileResourceProvider(dataPath);
    }

    private static IResourceProvider CreateFromManifest(string dataPath, string manifestPath, byte[] amk)
    {
        List<AopakManifest.ArchiveEntry> entries;
        try
        {
            entries = AopakManifest.Read(manifestPath, amk);
        }
        catch (System.Exception ex)
        {
            GD.PrintErr($"[AoPak] Failed to read manifest: {ex.Message}. Falling back to directory scan.");
            return new AopakResourceProvider(dataPath, amk);
        }

        // Sort by layer descending (highest priority first)
        entries.Sort((a, b) => b.Layer.CompareTo(a.Layer));

        var providers = CreateLooseOverrideProviders(dataPath);
        int looseProviderCount = providers.Count;
        foreach (var entry in entries)
        {
            string archivePath = Path.Combine(dataPath, entry.FileName);
            if (!File.Exists(archivePath))
            {
                GD.PrintErr($"[AoPak] Manifest references missing archive: {entry.FileName}");
                continue;
            }

            try
            {
                providers.Add(new AopakResourceProvider(archivePath, amk, singleFile: true));
            }
            catch (System.Exception ex)
            {
                GD.PrintErr($"[AoPak] Failed to open {entry.FileName}: {ex.Message}");
            }
        }

        if (providers.Count == 0)
        {
            GD.PrintErr("[AoPak] Manifest listed no usable archives. Falling back to loose files.");
            return new FileResourceProvider(dataPath);
        }

        GD.Print($"[AoPak] Composite provider: {providers.Count - looseProviderCount} archive(s) from manifest, {looseProviderCount} loose override folder(s).");
        return new CompositeResourceProvider(dataPath, providers);
    }

    private static List<IResourceProvider> CreateLooseOverrideProviders(string dataPath)
    {
        var providers = new List<IResourceProvider>();
        AddFileProviderIfPresent(providers, dataPath);

        string? sourceDataPath = FindSourceDataPath(dataPath);
        if (sourceDataPath != null
            && !Path.GetFullPath(sourceDataPath).Equals(Path.GetFullPath(dataPath), System.StringComparison.OrdinalIgnoreCase))
        {
            AddFileProviderIfPresent(providers, sourceDataPath);
        }

        return providers;
    }

    private static void AddFileProviderIfPresent(List<IResourceProvider> providers, string path)
    {
        if (!Directory.Exists(path))
            return;

        providers.Add(new FileResourceProvider(path));
        GD.Print($"[Resources] Loose resources enabled: {path}");
    }

    private static string? FindSourceDataPath(string dataPath)
    {
        string? fromDataPath = FindSourceDataPathFrom(dataPath);
        if (fromDataPath != null)
            return fromDataPath;

        string projectPath = ProjectSettings.GlobalizePath("res://");
        return FindSourceDataPathFrom(projectPath);
    }

    private static string? FindSourceDataPathFrom(string startPath)
    {
        var dir = new DirectoryInfo(Path.GetFullPath(startPath));
        for (int depth = 0; dir != null && depth < 8; depth++, dir = dir.Parent)
        {
            string candidate = Path.Combine(dir.FullName, "resources", "data");
            if (Directory.Exists(candidate))
                return candidate;
        }

        return null;
    }

    /// <summary>
    /// Get the Application Master Key.
    /// In release builds, reconstructed from obfuscated fragments in AopakKeyStore.
    /// In development, derived from a fixed passphrase.
    /// </summary>
    private static byte[] GetApplicationMasterKey()
    {
#if RELEASE
        return AopakKeyStore.GetAmk();
#else
        // Development key — NEVER ship in release builds
        byte[] passphrase = System.Text.Encoding.UTF8.GetBytes("argentum-nextgen-dev-key-2026");
        return System.Security.Cryptography.SHA256.HashData(passphrase);
#endif
    }
}
