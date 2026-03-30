using Godot;
using System.Collections.Generic;

namespace ArgentumNextgen.UI;

public static partial class RpgTheme
{
    // =========================================================================
    // CACHE
    // =========================================================================

    private static readonly Dictionary<string, Texture2D> _texCache = new();
    private static readonly Dictionary<string, ImageTexture> _scaledTexCache = new();

    // =========================================================================
    // CORE HELPERS
    // =========================================================================

    public static Texture2D GetTex(string filename)
    {
        if (!_texCache.TryGetValue(filename, out var tex))
        {
            string resPath = AssetsPath + filename;
            // Load PNG via Image.Load — works with res:// paths, no .import needed
            var img = new Image();
            var err = img.Load(resPath);
            if (err == Error.Ok)
            {
                tex = ImageTexture.CreateFromImage(img);
            }
            else
            {
                // Fallback: try absolute filesystem path
                string globalPath = ProjectSettings.GlobalizePath(resPath);
                err = img.Load(globalPath);
                if (err == Error.Ok)
                    tex = ImageTexture.CreateFromImage(img);
                else
                    GD.PrintErr($"[RpgTheme] FAILED to load: {resPath} (err={err})");
            }
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
