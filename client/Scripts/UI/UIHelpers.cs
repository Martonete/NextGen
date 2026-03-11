using Godot;
using System;

namespace ArgentumNextgen.UI;

/// <summary>
/// Static utility methods for UI construction (fonts, labels, buttons, textures, etc.).
/// Extracted from Main.cs — zero state, pure helpers.
/// </summary>
public static class UIHelpers
{
    // Simple XOR key for account remember file (not security-critical, just obfuscation)
    public const string RememberXorKey = "ArgentumNextgen2024";
    public const string RememberFileName = "remembered.dat";

    /// <summary>
    /// Apply a specific font to a label (VB6 parity: exact font family + weight).
    /// </summary>
    public static void ApplyFont(Label label, string fontName = "Tahoma", int weight = 700)
    {
        var font = new SystemFont();
        font.FontNames = new string[] { fontName };
        font.FontWeight = weight;
        font.MultichannelSignedDistanceField = true;
        label.AddThemeFontOverride("font", font);
    }

    /// <summary>
    /// Create a stat label at exact VB6 pixel positions.
    /// </summary>
    public static Label CreateStatLabel(float x, float y, float w, float h, Color color, int fontSize,
                                         string fontName = "Tahoma", int weight = 700)
    {
        var label = new Label();
        label.Position = new Vector2(x, y);
        label.Size = new Vector2(w, h);
        label.HorizontalAlignment = HorizontalAlignment.Center;
        label.AddThemeColorOverride("font_color", color);
        label.AddThemeFontSizeOverride("font_size", fontSize);
        label.MouseFilter = Control.MouseFilterEnum.Ignore;
        ApplyFont(label, fontName, weight);
        return label;
    }

    /// <summary>
    /// Create a fully invisible button (flat, no text, empty styleboxes).
    /// VB6 visuals are baked into Principal.jpg — this is just a hit-detect area.
    /// If usePointerCursor is true, shows hand cursor on hover (VB6 MousePointer=99).
    /// </summary>
    public static Button CreateInvisibleButton(float x, float y, float w, float h, bool usePointerCursor = true)
    {
        var btn = new Button();
        btn.Position = new Vector2(x, y);
        btn.Size = new Vector2(w, h);
        btn.Flat = true;
        btn.Text = "";
        var empty = new StyleBoxEmpty();
        btn.AddThemeStyleboxOverride("normal", empty);
        btn.AddThemeStyleboxOverride("hover", empty);
        btn.AddThemeStyleboxOverride("pressed", empty);
        btn.AddThemeStyleboxOverride("focus", empty);
        if (usePointerCursor)
            btn.MouseDefaultCursorShape = Control.CursorShape.PointingHand;
        btn.FocusMode = Control.FocusModeEnum.None; // Never steal keyboard focus
        return btn;
    }

    /// <summary>
    /// Load a JPG image from the filesystem as an ImageTexture.
    /// Uses Image.Load() (filesystem), not ResourceLoader (requires Godot import).
    /// </summary>
    public static ImageTexture? LoadJpgTexture(string path)
    {
        if (!System.IO.File.Exists(path)) return null;
        try
        {
            var img = new Image();
            img.Load(path);
            return ImageTexture.CreateFromImage(img);
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[UIHelpers] Failed to load {path}: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Convert a SocketException or other connection error into a user-friendly Spanish message.
    /// </summary>
    public static string FriendlyConnectionError(Exception ex) => ex switch
    {
        System.Net.Sockets.SocketException se => se.SocketErrorCode switch
        {
            System.Net.Sockets.SocketError.ConnectionRefused => "El servidor no está disponible. Intentá de nuevo en unos segundos.",
            System.Net.Sockets.SocketError.TimedOut => "No se pudo conectar: el servidor no responde.",
            System.Net.Sockets.SocketError.HostNotFound => "No se encontró el servidor. Verificá tu conexión.",
            System.Net.Sockets.SocketError.NetworkUnreachable => "Red no disponible. Verificá tu conexión a internet.",
            _ => $"Error de conexión: {se.SocketErrorCode}",
        },
        OperationCanceledException => "No se pudo conectar: el servidor no responde.",
        NullReferenceException => "El servidor no está disponible. Intentá de nuevo en unos segundos.",
        _ => $"Error de conexión: {ex.Message}",
    };

    /// <summary>
    /// XOR encrypt a plain text string with the remember key.
    /// </summary>
    public static byte[] XorCrypt(string plainText)
    {
        byte[] textBytes = System.Text.Encoding.UTF8.GetBytes(plainText);
        byte[] keyBytes = System.Text.Encoding.UTF8.GetBytes(RememberXorKey);
        byte[] result = new byte[textBytes.Length];
        for (int i = 0; i < textBytes.Length; i++)
            result[i] = (byte)(textBytes[i] ^ keyBytes[i % keyBytes.Length]);
        return result;
    }

    /// <summary>
    /// XOR decrypt encrypted bytes back to a string using the remember key.
    /// </summary>
    public static string XorCrypt(byte[] encrypted)
    {
        byte[] keyBytes = System.Text.Encoding.UTF8.GetBytes(RememberXorKey);
        byte[] result = new byte[encrypted.Length];
        for (int i = 0; i < encrypted.Length; i++)
            result[i] = (byte)(encrypted[i] ^ keyBytes[i % keyBytes.Length]);
        return System.Text.Encoding.UTF8.GetString(result);
    }
}
