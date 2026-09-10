#nullable enable
using Godot;

namespace ArgentumNextgen.UI;

/// <summary>
/// The text faces the client draws with, matching ArgentumOnlineGodot.
///
/// Two families, because that client uses two:
///  · The console and its input override their font to Alegreya Sans
///    (game_screen.tscn, FontVariation over AlegreyaSans-Regular/Bold/Italic).
///  · Names and over-head dialogue set only a size in their LabelSettings, so
///    they fall back to Godot's own default theme face — Open Sans SemiBold.
///    ThemeDB hands us that exact font, with no file to ship.
///
/// Loaded once and cached: FontFile parses the .ttf on assignment.
/// </summary>
public static class GameFonts
{
    private const string FontDir = "Fonts/alegreya-sans/";

    private static Font? _alegreyaRegular;
    private static Font? _alegreyaBold;
    private static Font? _alegreyaItalic;
    private static Font? _alegreyaBoldItalic;
    private static Font? _inWorld;

    /// <summary>Console body text and the chat input.</summary>
    public static Font AlegreyaRegular => _alegreyaRegular ??= Load("AlegreyaSans-Regular.ttf");
    public static Font AlegreyaBold => _alegreyaBold ??= Load("AlegreyaSans-Bold.ttf");
    public static Font AlegreyaItalic => _alegreyaItalic ??= Load("AlegreyaSans-Italic.ttf");
    public static Font AlegreyaBoldItalic => _alegreyaBoldItalic ??= Load("AlegreyaSans-BoldItalic.ttf");

    /// <summary>Names and dialogue drawn over the world.</summary>
    public static Font InWorld
    {
        get
        {
            if (_inWorld != null) return _inWorld;
            _inWorld = ThemeDB.Singleton?.GetDefaultTheme()?.DefaultFont ?? ThemeDB.FallbackFont;
            if (_inWorld == null)
            {
                // Nothing should reach here; a missing face would draw nothing at all.
                var fallback = new SystemFont();
                fallback.FontNames = new[] { "Segoe UI", "Verdana", "Tahoma", "Arial" };
                _inWorld = fallback;
            }
            return _inWorld;
        }
    }

    private static Font Load(string fileName)
    {
        string relative = FontDir + fileName;
        var provider = RpgTheme.ResourceProvider;
        if (provider != null && provider.Exists(relative))
        {
            try
            {
                var file = new FontFile();
                file.Data = provider.ReadBytes(relative);
                return file;
            }
            catch (System.Exception ex)
            {
                GD.PrintErr($"[FONT] Could not read {relative}: {ex.Message}");
            }
        }

        GD.PrintErr($"[FONT] {relative} not found — falling back to the default face.");
        return InWorld;
    }
}
