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
        string manifestPath = Path.Combine(dataPath, AopakManifest.ManifestFileName);

        if (File.Exists(manifestPath))
        {
            byte[] amk = GetApplicationMasterKey();
            return CreateFromManifest(dataPath, manifestPath, amk);
        }

        bool hasArchives = Directory.GetFiles(dataPath, "*.aopak").Length > 0;
        if (hasArchives)
        {
            byte[] amk = GetApplicationMasterKey();
            return new AopakResourceProvider(dataPath, amk);
        }

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

        var providers = new List<IResourceProvider>(entries.Count);
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

        GD.Print($"[AoPak] Composite provider: {providers.Count} archive(s) from manifest.");
        return new CompositeResourceProvider(dataPath, providers);
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
