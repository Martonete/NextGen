using Godot;
using System.Collections.Generic;
using ArgentumNextgen.Data.Resources;

namespace ArgentumNextgen.UI;

public static partial class RpgTheme
{
    // =========================================================================
    // CACHE
    // =========================================================================

    private static readonly Dictionary<string, Texture2D> _texCache = new();
    private static readonly Dictionary<string, ImageTexture> _scaledTexCache = new();

    /// <summary>
    /// Injected resource provider for loading UI assets from .aopak archives.
    /// Must be set early (e.g., in Main.cs after ResourceProviderFactory.Create).
    /// When null, falls back to direct filesystem loading from AssetsPath.
    /// </summary>
    public static IResourceProvider? ResourceProvider { get; set; }

    // =========================================================================
    // CORE HELPERS
    // =========================================================================

    public static Texture2D GetTex(string filename)
    {
        if (!_texCache.TryGetValue(filename, out var tex))
        {
            Image? img = null;

            // Try IResourceProvider first (reads from ui.aopak via "UI/<filename>")
            if (ResourceProvider != null)
            {
                string aopakPath = "UI/" + filename;
                try
                {
                    img = ResourceProvider.ReadImage(aopakPath);
                }
                catch
                {
                    // Entry not found in aopak — fall through to filesystem
                }
            }

            // Fallback: direct filesystem load (res://Data/UI/...)
            if (img == null)
            {
                string resPath = AssetsPath + filename;
                img = new Image();
                var err = img.Load(resPath);
                if (err != Error.Ok)
                {
                    string globalPath = ProjectSettings.GlobalizePath(resPath);
                    err = img.Load(globalPath);
                    if (err != Error.Ok)
                    {
                        GD.PrintErr($"[RpgTheme] FAILED to load: {resPath} (err={err})");
                        img = null;
                    }
                }
            }

            if (img != null)
                tex = ImageTexture.CreateFromImage(img);

            _texCache[filename] = tex!;
        }
        return tex!;
    }

    private static ImageTexture GetScaledTex(string filename, Vector2I targetSize)
    {
        var key = filename + targetSize.ToString();
        if (!_scaledTexCache.TryGetValue(key, out var tex))
        {
            var img = (Image)GetTex(filename).GetImage().Duplicate();
            img.Resize(targetSize.X, targetSize.Y, Image.Interpolation.Lanczos);
            tex = ImageTexture.CreateFromImage(img);
            _scaledTexCache[key] = tex;
        }
        return tex;
    }

    /// <summary>Get a texture from the asset catalog by category and name.</summary>
    public static Texture2D? GetAsset(string category, string assetName)
    {
        if (category == "mini_buttons" && MiniButtons.TryGetValue(assetName, out var mb))
            return GetTex(mb.Normal);
        if (Assets.TryGetValue(category, out var cat) && cat.TryGetValue(assetName, out var path))
            return GetTex(path);
        GD.PushWarning($"RpgTheme: asset not found: {category}/{assetName}");
        return null;
    }

    public static string GetAssetPath(string category, string assetName)
    {
        if (category == "mini_buttons" && MiniButtons.TryGetValue(assetName, out var mb))
            return mb.Normal;
        if (Assets.TryGetValue(category, out var cat) && cat.TryGetValue(assetName, out var path))
            return path;
        return "";
    }
}
