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
    /// <summary>
    /// VB6: Draw name at PixelOffsetX+16, PixelOffsetY+30 (DT_CENTER)
    /// and clan/rank at PixelOffsetY+45.
    /// Uses font1 (bitmap font) for pixel-perfect match.
    /// </summary>
    private static void DrawName(Node2D canvas, Character ch, Vector2 pos, GameData data, GameState? state = null)
    {
        if (string.IsNullOrEmpty(ch.Name)) return;

        var font = data.Fonts[1]; // font1 for names
        if (font == null) return;

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

        // VB6: Engine_Text_Draw(PixelOffsetX + 16, PixelOffsetY + 30, Line, color, alpha, DT_CENTER)
        // X+16 = center of tile, DT_CENTER subtracts half text width
        // Y+30 = top of text (bitmap font uses top-Y, same as AoFont.DrawText)
        int centerX = (int)pos.X + 16;
        int nickY = (int)pos.Y + 30;

        // VB6: dead -> alpha 80, invisible -> pulsing alpha, else 255
        byte alpha = ch.Dead ? (byte)80
                   : ch.Invisible ? (byte)(ch.TransparenciaBody * 255 / 100)
                   : (byte)255;
        Color nickColor = new Color(nameColor.R, nameColor.G, nameColor.B, alpha / 255f);
        font.DrawText(canvas, centerX, nickY, nick, nickColor, center: true);

        // VB6: rank badge at Y+45 for admins, clan for non-admins
        int tagY = (int)pos.Y + 45;

        if (ch.Privileges > 0)
        {
            string rank = GetRankString(ch.Privileges);
            font.DrawText(canvas, centerX, tagY, rank, nickColor, center: true);
        }
        else if (clan.Length > 0)
        {
            font.DrawText(canvas, centerX, tagY, clan, nickColor, center: true);
        }

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
        if (string.IsNullOrEmpty(ch.DialogText)) return;

        var font = data.Fonts[1]; // font1 for dialog
        if (font == null) return;

        long now = System.Environment.TickCount64;
        long elapsed = now - ch.DialogStartMs;

        // Delta-time factor: VB6 ran at ~60fps -> 16.67ms per frame.
        // All per-frame increments are scaled by (deltaMs / 16.67).
        float dtFactor = deltaMs / 16.667f;

        // VB6 Sube logic: decrements 1 per frame (60/sec), fades in +12/frame (720/sec)
        if (ch.DialogDurationMs >= 292)
        {
            if (ch.DialogRiseCounter > 0)
                ch.DialogRiseCounter = Math.Max(0, ch.DialogRiseCounter - dtFactor);
            if (ch.DialogRiseCounter > 0)
            {
                ch.DialogAlpha = Math.Min(255f, ch.DialogAlpha + 12f * dtFactor);
            }
        }

        // VB6: check lifetime -> set Tiempito
        if (elapsed >= ch.DialogDurationMs && !ch.DialogFading)
            ch.DialogFading = true;

        // VB6: fade-out -10/frame (600/sec), remove at <= 9
        if (ch.DialogFading)
        {
            ch.DialogAlpha = Math.Max(0, ch.DialogAlpha - 10f * dtFactor);
            if (ch.DialogAlpha <= 9f)
            {
                ch.DialogText = "";
                return;
            }
        }

        byte alpha = (byte)Math.Clamp((int)ch.DialogAlpha, 0, 255);
        if (alpha == 0) return;

        var lines = WrapText(ch.DialogText, 24);
        int numLines = lines.Length;

        // Parse hex color
        Color color;
        if (ch.DialogColor.Length == 6)
        {
            int r = Convert.ToInt32(ch.DialogColor[..2], 16);
            int g = Convert.ToInt32(ch.DialogColor[2..4], 16);
            int b = Convert.ToInt32(ch.DialogColor[4..6], 16);
            color = new Color(r / 255f, g / 255f, b / 255f, alpha / 255f);
        }
        else
        {
            color = new Color(1, 1, 1, alpha / 255f);
        }

        int fontSize = font.CharHeight;

        // VB6: Y = PixelOffsetY + HeadOffset.Y + OFFSET_HEAD - (numLines * 3)
        // OFFSET_HEAD = -34 ("34 pixels of head GRH that overlap with body")
        const int OffsetHead = -34;
        int baseY = (int)(pos.Y + headOffset.Y) + OffsetHead - ((numLines - 1) * 3);
        if (ch.DialogRiseCounter > 0)
            baseY += (int)(ch.DialogRiseCounter / 1.2f);

        int textCenterX = (int)pos.X + 16;

        // Queue to overlay layer (above all characters) or draw directly as fallback
        if (worldRenderer != null)
        {
            worldRenderer.QueueDialogDraw(lines, textCenterX, baseY, fontSize, color);
        }
        else
        {
            int offset = -(fontSize + 2) * (numLines - 1);
            for (int i = 0; i < numLines; i++)
            {
                int lineY = baseY + offset + 2;
                font.DrawText(canvas, textCenterX, lineY, lines[i], color, center: true);
                offset += fontSize + 5;
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
    private static Color GetNameColor(Character ch)
    {
        if (ch.Privileges > 0)
        {
            return ch.Privileges switch
            {
                1 => new Color(0 / 255f, 185 / 255f, 0 / 255f),       // Consejero
                2 => new Color(0 / 255f, 170 / 255f, 190 / 255f),     // Semidios
                3 => new Color(128 / 255f, 128 / 255f, 64 / 255f),    // Event Master
                4 => new Color(120 / 255f, 250 / 255f, 250 / 255f),   // Dios
                5 => new Color(180 / 255f, 180 / 255f, 180 / 255f),   // Rol Master
                6 => new Color(140 / 255f, 0 / 255f, 0 / 255f),       // Caos
                7 => new Color(0 / 255f, 64 / 255f, 128 / 255f),      // Consejo Bander
                8 => new Color(0 / 255f, 255 / 255f, 128 / 255f),     // Gran Dios
                9 => new Color(123 / 255f, 55 / 255f, 0 / 255f),      // Director
                10 => new Color(128 / 255f, 255 / 255f, 128 / 255f),  // Developer
                11 => new Color(255 / 255f, 198 / 255f, 0 / 255f),    // Sub Admin
                12 => new Color(255 / 255f, 255 / 255f, 255 / 255f),  // Administrador
                _ => new Color(180 / 255f, 180 / 255f, 180 / 255f),
            };
        }

        if (ch.Criminal)
            return new Color(1.0f, 0.0f, 0.0f);

        return new Color(0 / 255f, 128 / 255f, 255 / 255f);
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
}
