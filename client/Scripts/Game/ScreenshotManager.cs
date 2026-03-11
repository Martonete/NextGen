using Godot;

namespace ArgentumNextgen.Game;

/// <summary>
/// Captures screenshots and saves them as PNG files.
/// Bound to GameAction.Screenshot key (default: F).
/// Files saved to user://Screenshots/ with timestamp names.
/// </summary>
public static class ScreenshotManager
{
    /// <summary>
    /// Capture the current viewport and save as PNG.
    /// Returns the save path, or null on failure.
    /// </summary>
    public static string? CaptureScreenshot()
    {
        var viewport = (Engine.GetMainLoop() as SceneTree)?.Root;
        if (viewport == null)
        {
            GD.PrintErr("[SCREENSHOT] No viewport available");
            return null;
        }

        var image = viewport.GetTexture().GetImage();
        if (image == null)
        {
            GD.PrintErr("[SCREENSHOT] Failed to get viewport image");
            return null;
        }

        string dir = "user://Screenshots";
        if (!DirAccess.DirExistsAbsolute(dir))
            DirAccess.MakeDirRecursiveAbsolute(dir);

        string timestamp = System.DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
        string path = $"{dir}/screenshot_{timestamp}.png";
        var err = image.SavePng(path);
        if (err != Error.Ok)
        {
            GD.PrintErr($"[SCREENSHOT] Failed to save: {err}");
            return null;
        }

        GD.Print($"[SCREENSHOT] Saved to {path}");
        return path;
    }
}
