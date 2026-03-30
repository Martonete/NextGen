#nullable enable

namespace ArgentumNextgen.Data.Resources;

public static class ResourceProviderFactory
{
    /// <summary>
    /// Create the appropriate resource provider based on context.
    /// Currently always returns FileResourceProvider.
    /// Future: detect .aopak files and return AopakResourceProvider.
    /// </summary>
    public static IResourceProvider Create(string dataPath)
    {
        // Future: check for .aopak files
        // if (File.Exists(Path.Combine(dataPath, "resources.aoman")))
        //     return new AopakResourceProvider(dataPath);

        return new FileResourceProvider(dataPath);
    }
}
