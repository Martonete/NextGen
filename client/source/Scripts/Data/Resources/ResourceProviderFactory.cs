#nullable enable

namespace ArgentumNextgen.Data.Resources;

public static class ResourceProviderFactory
{
    /// <summary>
    /// Create the appropriate resource provider based on context.
    /// Auto-detects .aopak archives in the data directory; falls back to loose files.
    /// </summary>
    public static IResourceProvider Create(string dataPath)
    {
        // Check for .aopak archives
        bool hasArchives = System.IO.Directory.GetFiles(dataPath, "*.aopak").Length > 0;

        if (hasArchives)
        {
            // Load AMK (Application Master Key)
            // For now, use a development key. In release, this will be extracted
            // from obfuscated binary locations.
            byte[] amk = GetApplicationMasterKey();
            return new AopakResourceProvider(dataPath, amk);
        }

        // Fallback to loose files
        return new FileResourceProvider(dataPath);
    }

    /// <summary>
    /// Get the Application Master Key.
    /// TODO Phase 5: Extract from obfuscated binary locations.
    /// For development, uses a fixed key derived from a passphrase.
    /// </summary>
    private static byte[] GetApplicationMasterKey()
    {
#if RELEASE
        // Phase 5: load from obfuscated binary locations
        throw new InvalidOperationException("Production AMK not configured. Build with obfuscated key source.");
#else
        // Development key — NEVER ship in release builds
        byte[] passphrase = System.Text.Encoding.UTF8.GetBytes("argentum-nextgen-dev-key-2026");
        return System.Security.Cryptography.SHA256.HashData(passphrase);
#endif
    }
}
