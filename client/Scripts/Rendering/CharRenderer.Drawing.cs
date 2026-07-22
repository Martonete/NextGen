using System;
using Godot;
using ArgentumNextgen.Data;
using ArgentumNextgen.Game;

namespace ArgentumNextgen.Rendering;

/// <summary>
/// CharRenderer partial: DrawGrh, DrawName, DrawDialog, text helpers, color utilities.
/// </summary>
public static partial class CharRenderer
{
    // Shared in-world font: vector, anti-aliased, with fallbacks.
    // Lazy-initialized on first draw (always on main thread inside _Draw).
    private static Font? _inWorldFont;
    private static Font GetInWorldFont()
    {
        if (_inWorldFont != null) return _inWorldFont;
        var f = new SystemFont();
        f.FontNames = new string[] { "Segoe UI", "Verdana", "Tahoma", "Arial" };
        f.FontWeight = 700;
        f.MultichannelSignedDistanceField = true;
        _inWorldFont = f;
        return f;
    }

    private const int NameFontSize   = 11;
    private const int DialogFontSize = 10;

    /// <summary>
    /// Draw name + clan/rank above character. Vector font with drop shadow.
    /// </summary>
    private static void DrawName(Node2D canvas, Character ch, Vector2 pos, GameData data, GameState? state = null)
    {
        if (string.IsNullOrEmpty(ch.Name)) return;

        // VB6: name format is "Nick<ClanTag"
        string nick = ch.Name;
        string clan = "";
        int ltPos = ch.Name.IndexOf('<');
        if (ltPos >= 0)
        {
            nick = ch.Name[..ltPos];
            clan = ch.Name[ltPos..];
        }

        Color nameColor = GetNameColor(ch);

        int centerX = (int)pos.X + 16;
        int nickY   = (int)pos.Y + 30;

        float baseAlpha = ch.Dead      ? 80f / 255f
                        : ch.Invisible ? ch.TransparenciaBody / 100f
                        : 1f;
        Color nickColor = new Color(nameColor.R, nameColor.G, nameColor.B, baseAlpha * ch.FovAlpha);

        var font  = GetInWorldFont();
        float asc = font.GetAscent(NameFontSize);

        DrawStringCentered(canvas, font, NameFontSize, centerX, nickY + asc, nick, nickColor);

        int tagY = (int)pos.Y + 45;
        if (ch.Privileges > 0)
            DrawStringCentered(canvas, font, NameFontSize, centerX, tagY + asc, GetRankString(ch.Privileges), nickColor);
        else if (clan.Length > 0)
            DrawStringCentered(canvas, font, NameFontSize, centerX, tagY + asc, clan, nickColor);
    }

    // Draw a string centered at (cx, baselineY) with a dark drop-shadow.
    private static void DrawStringCentered(Node2D canvas, Font font, int size, float cx, float baselineY, string text, Color color)
    {
        float w = font.GetStringSize(text, HorizontalAlignment.Left, -1, size).X;
        float x = cx - w / 2f;
        Color shadow = new Color(0f, 0f, 0f, color.A * 0.75f);
        canvas.DrawString(font, new Vector2(x + 1, baselineY + 1), text, HorizontalAlignment.Left, -1, size, shadow);
        canvas.DrawString(font, new Vector2(x,     baselineY),     text, HorizontalAlignment.Left, -1, size, color);
    }

    /// <summary>
    /// VB6 cDialogos: speech bubble text above character head.
    /// Position: UpdateDialogPos(PixelOffsetX + HeadOffset.X - 168, PixelOffsetY + HeadOffset.Y, charindex)
    /// Then: .X = X - 36, .Y = Y - (UBound(textLine) * 3)
    /// Render: Engine_Text_Draw(.X + 171, .Y + offset + 2, ...)
    ///
    /// Net X = PixelOffsetX + HeadOffset.X - 168 - 36 + 171 = PixelOffsetX + HeadOffset.X - 33
    /// Net Y = PixelOffsetY + HeadOffset.Y - (numLinesMinusOne * 3) + Sube/1.2 + offset + 2
    ///   where offset starts at -(fontSize+2)*UBound(textLine)
    ///
    /// VB6 Sube: starts at 18, decrements to 0. Adds Sube/1.2 to Y (text starts low, rises up).
    /// VB6 Desvanecimiento: starts at 20, +12/frame while Sube>0. After lifetime: -10/frame.
    /// </summary>
    private static void DrawDialog(Node2D canvas, Character ch, Vector2 pos,
                                    Vector2 headOffset, GameData data, float deltaMs,
                                    WorldRenderer? worldRenderer = null)
    {
        // Timer advancement (DialogRiseCounter, DialogAlpha, DialogFading, DialogText clear)
        // is handled by UpdateCharacterTimers in _Process. This method only reads current state.
        if (string.IsNullOrEmpty(ch.DialogText)) return;

        // Apply FovAlpha so dialog fades with the character at viewport edge
        float combinedAlpha = ch.DialogAlpha * ch.FovAlpha;
        byte alpha = (byte)Math.Clamp((int)combinedAlpha, 0, 255);
        if (alpha == 0) return;

        // Cache WrapText result — only re-wrap when DialogText changes
        if (ch.CachedDialogText != ch.DialogText)
        {
            ch.CachedDialogLines = WrapText(ch.DialogText, 24);
            ch.CachedDialogText = ch.DialogText;
        }
        var lines = ch.CachedDialogLines!;
        int numLines = lines.Length;

        // Parse hex color
        Color color;
        if (ch.DialogColor.Length == 6)
        {
            int colorVal = 0;
            if (!int.TryParse(ch.DialogColor, System.Globalization.NumberStyles.HexNumber, null, out colorVal))
                colorVal = 0xFFFFFF;
            int r = (colorVal >> 16) & 0xFF;
            int g = (colorVal >> 8) & 0xFF;
            int b = colorVal & 0xFF;
            color = new Color(r / 255f, g / 255f, b / 255f, alpha / 255f);
        }
        else
        {
            color = new Color(1, 1, 1, alpha / 255f);
        }

        const int OffsetHead = -8;
        int baseY = (int)(pos.Y + headOffset.Y) + OffsetHead - ((numLines - 1) * 3);
        if (ch.DialogRiseCounter > 0)
            baseY += (int)(ch.DialogRiseCounter / 1.2f);

        int textCenterX = (int)pos.X + 16;

        // Queue to overlay layer (above all characters) or draw directly as fallback
        if (worldRenderer != null)
        {
            worldRenderer.QueueDialogDraw(lines, textCenterX, baseY, DialogFontSize, color);
        }
        else
        {
            var font2 = GetInWorldFont();
            float asc2 = font2.GetAscent(DialogFontSize);
            int lineSpacing = DialogFontSize + 5;
            int offset = -lineSpacing * (numLines - 1);
            for (int i = 0; i < numLines; i++)
            {
                float lineBaseY = baseY + offset + 2 + asc2;
                DrawStringCentered(canvas, font2, DialogFontSize, textCenterX, lineBaseY, lines[i], color);
                offset += lineSpacing;
            }
        }
    }

    /// <summary>
    /// Word-wrap text to maxLen characters per line (VB6: MAX_LENGTH = 24).
    /// </summary>
    private static string[] WrapText(string text, int maxLen)
    {
        var lines = new System.Collections.Generic.List<string>();
        string current = "";

        foreach (string word in text.Split(' '))
        {
            if (current.Length > 0 && current.Length + 1 + word.Length > maxLen)
            {
                lines.Add(current);
                current = word;
            }
            else
            {
                current = current.Length > 0 ? current + " " + word : word;
            }

            while (current.Length > maxLen)
            {
                lines.Add(current[..maxLen]);
                current = current[maxLen..];
            }
        }

        if (current.Length > 0)
            lines.Add(current);

        return lines.Count > 0 ? lines.ToArray() : new[] { text };
    }

    /// <summary>
    /// VB6 RangoPRIV: privilege rank badge strings.
    /// </summary>
    private static string GetRankString(int priv)
    {
        return priv switch
        {
            >= 1 and <= 8 => "<Game Master>",
            9 => "<Director de GMs>",
            10 => "<Developer>",
            11 => "<Sub Administrador>",
            12 => "<Administrador>",
            _ => "<GM>",
        };
    }

    /// <summary>
    /// VB6 name colors from colores.dat (ColoresPJ array).
    /// </summary>
    // Pre-computed name colors — avoids new Color() per character per frame
    private static readonly Color NameColorCitizen = new Color(0f, 128 / 255f, 1f);
    private static readonly Color NameColorCriminal = new Color(1f, 0f, 0f);
    private static readonly Color[] NameColorsByPrivilege = new Color[]
    {
        new Color(180 / 255f, 180 / 255f, 180 / 255f),    // 0: fallback
        new Color(0f, 185 / 255f, 0f),                     // 1: Consejero
        new Color(0f, 170 / 255f, 190 / 255f),             // 2: Semidios
        new Color(128 / 255f, 128 / 255f, 64 / 255f),     // 3: Event Master
        new Color(120 / 255f, 250 / 255f, 250 / 255f),    // 4: Dios
        new Color(180 / 255f, 180 / 255f, 180 / 255f),    // 5: Rol Master
        new Color(140 / 255f, 0f, 0f),                     // 6: Caos
        new Color(0f, 64 / 255f, 128 / 255f),              // 7: Consejo Bander
        new Color(0f, 1f, 128 / 255f),                     // 8: Gran Dios
        new Color(123 / 255f, 55 / 255f, 0f),             // 9: Director
        new Color(128 / 255f, 1f, 128 / 255f),            // 10: Developer
        new Color(1f, 198 / 255f, 0f),                     // 11: Sub Admin
        new Color(1f, 1f, 1f),                             // 12: Administrador
    };

    private static Color GetNameColor(Character ch)
    {
        if (ch.Privileges > 0 && ch.Privileges < NameColorsByPrivilege.Length)
            return NameColorsByPrivilege[ch.Privileges];
        if (ch.Privileges > 0)
            return NameColorsByPrivilege[0];
        if (ch.Criminal)
            return NameColorCriminal;
        return NameColorCitizen;
    }

    /// <summary>
    /// Draw a GRH with optional centering for multi-tile graphics and color modulation.
    /// </summary>
    public static void DrawGrh(
        CanvasItem canvas, GameData data, int grhIndex, int frame, Vector2 pos,
        bool center = false, Color? modulate = null)
    {
        var resolved = data.ResolveGrh(grhIndex, frame);
        if (resolved == null || resolved.FileNum <= 0) return;

        var texture = data.Textures?.GetTexture(resolved.FileNum);
        if (texture == null) return;

        int texW = texture.GetWidth();
        int texH = texture.GetHeight();

        // VB6/DirectX 8 doesn't bounds-check source rects — it clamps or wraps.
        // Instead of discarding sprites that slightly exceed texture bounds,
        // clamp the source rect to fit within the texture.
        // If a specific animation frame is out of bounds, fall back to frame 0
        // (handles water/animated tiles with sparse frames).
        int sx = resolved.SX;
        int sy = resolved.SY;
        int pw = resolved.PixelWidth;
        int ph = resolved.PixelHeight;

        // VB6/D3D8 uses WRAP texture addressing: UVs beyond 1.0 wrap around.
        // This means sx=32 on a 32px texture wraps to sx=0, showing the same pixels.
        // Many map tiles rely on this behavior (e.g., 5503.png is 32x32 but GRH defs
        // reference src positions at 32,0 and 0,32 — which wrap to 0,0).
        if (texW > 0) sx = sx % texW;
        if (texH > 0) sy = sy % texH;

        if (sx + pw > texW) pw = texW - sx;   // clamp width
        if (sy + ph > texH) ph = texH - sy;   // clamp height
        if (pw <= 0 || ph <= 0) return;

        float drawX = pos.X;
        float drawY = pos.Y;

        if (center)
        {
            // Use original pixel dimensions for centering (not clamped)
            if (resolved.TileWidth != 1f && resolved.TileWidth > 0)
            {
                drawX -= (int)(resolved.TileWidth * (TileSize / 2)) - TileSize / 2;
            }
            if (resolved.TileHeight != 1f && resolved.TileHeight > 0)
            {
                drawY -= (int)(resolved.TileHeight * TileSize) - TileSize;
            }
        }

        var srcRect = new Rect2(sx, sy, pw, ph);
        var destRect = new Rect2((float)Math.Round(drawX), (float)Math.Round(drawY), pw, ph);

        Color color = modulate ?? Colors.White;
        canvas.DrawTextureRectRegion(texture, destRect, srcRect, color);
    }

    /// <summary>
    /// scale=1f (default) reproduces the original pixel-for-pixel VB6 draw exactly.
    /// Values != 1f are the extended motor's ScaleOverLife — the sprite is scaled
    /// around its own center, same anchor the angle rotation already uses.
    /// </summary>
    public static void DrawEffectGrh(
        CanvasItem canvas, GameData data, int grhIndex, int frame, Vector2 pos,
        Color? modulate = null, float angle = 0f, float scale = 1f)
    {
        var resolved = data.ResolveGrh(grhIndex, frame);
        if (resolved == null || resolved.FileNum <= 0) return;

        var texture = data.Textures?.GetTexture(resolved.FileNum);
        if (texture == null) return;

        int texW = texture.GetWidth();
        int texH = texture.GetHeight();
        int sx = resolved.SX;
        int sy = resolved.SY;
        int pw = resolved.PixelWidth;
        int ph = resolved.PixelHeight;

        if (texW > 0) sx %= texW;
        if (texH > 0) sy %= texH;
        if (sx + pw > texW) pw = texW - sx;
        if (sy + ph > texH) ph = texH - sy;
        if (pw <= 0 || ph <= 0) return;

        var srcRect = new Rect2(sx, sy, pw, ph);
        Color color = modulate ?? Colors.White;

        if (angle != 0f || scale != 1f)
        {
            var center = new Vector2((float)Math.Round(pos.X + pw / 2f), (float)Math.Round(pos.Y + ph / 2f));
            ((Node2D)canvas).DrawSetTransform(center, angle, new Vector2(scale, scale));
            canvas.DrawTextureRectRegion(texture, new Rect2(-pw / 2f, -ph / 2f, pw, ph), srcRect, color);
            ((Node2D)canvas).DrawSetTransform(Vector2.Zero, 0f, Vector2.One);
            return;
        }

        var destRect = new Rect2((float)Math.Round(pos.X), (float)Math.Round(pos.Y), pw, ph);
        canvas.DrawTextureRectRegion(texture, destRect, srcRect, color);
    }
}
