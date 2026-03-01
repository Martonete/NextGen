using System;
using Godot;
using TierrasSagradasAO.Data;
using TierrasSagradasAO.Game;

namespace TierrasSagradasAO.Rendering;

/// <summary>
/// Renders a character with heading-dependent layer order matching VB6 exactly.
/// VB6 dibujarPersonaje() changes draw order per heading:
///   Heading 1 (N):  Arma → Escudo → Body → Head
///   Heading 2 (E):  Escudo → Body → Head → Arma
///   Heading 3 (S):  Body → Head → Arma → Escudo
///   Heading 4 (W):  Arma → Body → Head → Escudo
///
/// All character components use Center=1 in VB6 (bodies are multi-tile sprites).
/// </summary>
public static class CharRenderer
{
    private const int TileSize = 32;

    public static void DrawCharacter(
        Node2D canvas,
        Character ch,
        Vector2 screenPos,
        GameData data,
        GrhAnimator animator)
    {
        int heading = ch.Heading;
        if (heading < 1 || heading > 4) heading = 3;

        // Pre-resolve body data for head offset
        BodyData? body = null;
        if (ch.Body > 0 && ch.Body < data.Bodies.Length)
            body = data.Bodies[ch.Body];

        Vector2 headOffset = body != null
            ? new Vector2(body.HeadOffsetX, body.HeadOffsetY)
            : new Vector2(0, -30);

        // Shadow beneath character (VB6: Draw_Grh_Sombra, offset X-6)
        DrawShadow(canvas, ch, screenPos, heading, data, animator);

        // Heading-dependent draw order (VB6: dibujarPersonaje)
        switch (heading)
        {
            case 1: // North
                DrawWeapon(canvas, ch, screenPos, headOffset, heading, data, animator);
                DrawShield(canvas, ch, screenPos, headOffset, heading, data, animator);
                DrawBody(canvas, ch, screenPos, heading, data, animator);
                DrawHead(canvas, ch, screenPos, headOffset, heading, data);
                DrawHelmet(canvas, ch, screenPos, headOffset, heading, data);
                break;

            case 2: // East
                DrawShield(canvas, ch, screenPos, headOffset, heading, data, animator);
                DrawBody(canvas, ch, screenPos, heading, data, animator);
                DrawHead(canvas, ch, screenPos, headOffset, heading, data);
                DrawHelmet(canvas, ch, screenPos, headOffset, heading, data);
                DrawWeapon(canvas, ch, screenPos, headOffset, heading, data, animator);
                break;

            case 3: // South (default)
                DrawBody(canvas, ch, screenPos, heading, data, animator);
                DrawHead(canvas, ch, screenPos, headOffset, heading, data);
                DrawHelmet(canvas, ch, screenPos, headOffset, heading, data);
                DrawWeapon(canvas, ch, screenPos, headOffset, heading, data, animator);
                DrawShield(canvas, ch, screenPos, headOffset, heading, data, animator);
                break;

            case 4: // West
                DrawWeapon(canvas, ch, screenPos, headOffset, heading, data, animator);
                DrawBody(canvas, ch, screenPos, heading, data, animator);
                DrawHead(canvas, ch, screenPos, headOffset, heading, data);
                DrawHelmet(canvas, ch, screenPos, headOffset, heading, data);
                DrawShield(canvas, ch, screenPos, headOffset, heading, data, animator);
                break;
        }

        // FX overlays (up to 3 simultaneous)
        DrawFx(canvas, ch, screenPos, data, animator);

        // Name + clan above head
        DrawName(canvas, ch, screenPos);
    }

    private static void DrawShadow(
        Node2D canvas, Character ch, Vector2 pos, int heading,
        GameData data, GrhAnimator animator)
    {
        // Simple ellipse shadow at character's feet (replaces body-clone which
        // looked like a dark "skeleton" underneath the real body sprite).
        // Use DrawSetTransform to squash a circle into an oval.
        Vector2 shadowCenter = pos + new Vector2(TileSize / 2, TileSize - 4);
        canvas.DrawSetTransform(shadowCenter, 0, new Vector2(1f, 0.45f));
        canvas.DrawCircle(Vector2.Zero, 12f, new Color(0, 0, 0, 0.25f));
        canvas.DrawSetTransform(Vector2.Zero); // reset transform
    }

    private static void DrawBody(
        Node2D canvas, Character ch, Vector2 pos, int heading,
        GameData data, GrhAnimator animator)
    {
        if (ch.Body <= 0 || ch.Body >= data.Bodies.Length) return;
        var body = data.Bodies[ch.Body];
        if (body.Walk[heading] == 0) return;

        int bodyGrh = body.Walk[heading];
        // Use per-character WalkFrame (VB6: each char has its own FrameCounter)
        int frame = ch.Moving ? (int)ch.WalkFrame : 0;

        // VB6: dead characters get reduced alpha (TransparenciaBody + 45)
        // All bodies use Center=1 in VB6
        byte alpha = ch.Dead ? (byte)100 : (byte)255;
        DrawGrh(canvas, data, bodyGrh, frame, pos, true,
                alpha < 255 ? new Color(1, 1, 1, alpha / 255f) : Colors.White);
    }

    private static void DrawHead(
        Node2D canvas, Character ch, Vector2 bodyPos, Vector2 headOffset,
        int heading, GameData data)
    {
        if (ch.Head <= 0 || ch.Head >= data.Heads.Length) return;
        var head = data.Heads[ch.Head];
        if (head.Head[heading] == 0) return;

        // VB6 head position:
        // X: bodyScreenX + HeadOffset.X (minus 1 for NORTH)
        // Y: bodyScreenY + HeadOffset.Y + 1
        float xAdj = heading == 1 ? -1f : 0f;
        Vector2 headPos = bodyPos + new Vector2(headOffset.X + xAdj, headOffset.Y + 1);

        byte alpha = ch.Dead ? (byte)100 : (byte)255;
        DrawGrh(canvas, data, head.Head[heading], 0, headPos, true,
                alpha < 255 ? new Color(1, 1, 1, alpha / 255f) : Colors.White);
    }

    private static void DrawHelmet(
        Node2D canvas, Character ch, Vector2 bodyPos, Vector2 headOffset,
        int heading, GameData data)
    {
        if (ch.CascoAnim <= 0 || ch.CascoAnim >= data.Cascos.Length) return;
        var casco = data.Cascos[ch.CascoAnim];
        if (casco.Head[heading] == 0) return;

        // VB6: helmet at same position as head, heading 1,2,3 get X offset +1
        float xAdj = (heading >= 1 && heading <= 3) ? 1f : 0f;
        Vector2 helmetPos = bodyPos + new Vector2(headOffset.X + xAdj, headOffset.Y + 1);

        DrawGrh(canvas, data, casco.Head[heading], 0, helmetPos, true);
    }

    private static void DrawWeapon(
        Node2D canvas, Character ch, Vector2 bodyPos, Vector2 headOffset,
        int heading, GameData data, GrhAnimator animator)
    {
        if (ch.WeaponAnim <= 0 || ch.WeaponAnim >= data.Bodies.Length) return;
        var weapon = data.Bodies[ch.WeaponAnim];
        if (weapon.Walk[heading] == 0) return;

        int weapGrh = weapon.Walk[heading];
        // Use per-character WalkFrame (synced with body animation)
        int frame = ch.Moving ? (int)ch.WalkFrame : 0;

        // VB6: weapon drawn at bodyPos + headOffset.X, headOffset.Y + 38, centered
        Vector2 pos = bodyPos + new Vector2(headOffset.X, headOffset.Y + 38);
        DrawGrh(canvas, data, weapGrh, frame, pos, true);
    }

    private static void DrawShield(
        Node2D canvas, Character ch, Vector2 bodyPos, Vector2 headOffset,
        int heading, GameData data, GrhAnimator animator)
    {
        if (ch.ShieldAnim <= 0 || ch.ShieldAnim >= data.Bodies.Length) return;
        var shield = data.Bodies[ch.ShieldAnim];
        if (shield.Walk[heading] == 0) return;

        int shldGrh = shield.Walk[heading];
        // Use per-character WalkFrame (synced with body animation)
        int frame = ch.Moving ? (int)ch.WalkFrame : 0;

        // VB6: shield drawn at bodyPos + headOffset.X, headOffset.Y + 38, centered
        Vector2 pos = bodyPos + new Vector2(headOffset.X, headOffset.Y + 38);
        DrawGrh(canvas, data, shldGrh, frame, pos, true);
    }

    private static void DrawFx(
        Node2D canvas, Character ch, Vector2 pos,
        GameData data, GrhAnimator animator)
    {
        for (int i = 0; i < 3; i++)
        {
            int fxIdx = ch.ActiveFxSlots[i];
            if (fxIdx <= 0 || fxIdx >= data.Fxs.Length) continue;

            var fx = data.Fxs[fxIdx];
            if (fx.Animacion <= 0) continue;

            animator.StartAnim(fx.Animacion, true);
            int frame = animator.GetCurrentFrame(fx.Animacion);

            Vector2 fxPos = pos + new Vector2(fx.OffsetX, fx.OffsetY);
            DrawGrh(canvas, data, fx.Animacion, frame, fxPos, true,
                    new Color(1, 1, 1, 150f / 255f)); // VB6: alpha 150
        }
    }

    private static void DrawName(Node2D canvas, Character ch, Vector2 pos)
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

        var font = ThemeDB.FallbackFont;
        int fontSize = 12;

        // Nick centered above character (VB6: PixelOffsetX + 16, Y + 30)
        Vector2 nickSize = font.GetStringSize(nick, HorizontalAlignment.Left, -1, fontSize);
        Vector2 nickPos = pos + new Vector2((TileSize - nickSize.X) / 2, 30);
        canvas.DrawString(font, nickPos, nick, HorizontalAlignment.Left, -1, fontSize, nameColor);

        // Clan below nick (VB6: Y + 45)
        if (clan.Length > 0)
        {
            Vector2 clanSize = font.GetStringSize(clan, HorizontalAlignment.Left, -1, fontSize);
            Vector2 clanPos = pos + new Vector2((TileSize - clanSize.X) / 2, 45);
            canvas.DrawString(font, clanPos, clan, HorizontalAlignment.Left, -1, fontSize, nameColor);
        }
    }

    private static Color GetNameColor(Character ch)
    {
        if (ch.Privileges > 0)
        {
            return ch.Privileges switch
            {
                1 => new Color(0.0f, 0.8f, 0.8f),    // Consejero - cyan
                2 => new Color(0.6f, 0.6f, 1.0f),    // Semidios - light blue
                3 => new Color(0.0f, 1.0f, 0.0f),    // Event Master - green
                4 => new Color(1.0f, 1.0f, 0.0f),    // Dios - yellow
                >= 8 => new Color(1.0f, 0.0f, 0.0f), // Admin+ - red
                _ => new Color(0.2f, 0.8f, 1.0f),    // Other GM - blue
            };
        }

        if (ch.Criminal)
            return new Color(1.0f, 0.0f, 0.0f); // Criminal red

        return new Color(0.2f, 0.5f, 1.0f); // Citizen blue
    }

    /// <summary>
    /// Draw a GRH with optional centering for multi-tile graphics and color modulation.
    /// VB6 Draw_Grh: Center=1 adjusts position for TileWidth/TileHeight > 1.
    /// </summary>
    public static void DrawGrh(
        Node2D canvas, GameData data, int grhIndex, int frame, Vector2 pos,
        bool center = false, Color? modulate = null)
    {
        var resolved = data.ResolveGrh(grhIndex, frame);
        if (resolved == null || resolved.FileNum <= 0) return;

        var texture = data.Textures?.GetTexture(resolved.FileNum);
        if (texture == null) return;

        float drawX = pos.X;
        float drawY = pos.Y;

        // VB6 centering: offset for multi-tile graphics
        if (center)
        {
            if (resolved.TileWidth != 1f && resolved.TileWidth > 0)
            {
                drawX -= (int)(resolved.TileWidth * (TileSize / 2)) - TileSize / 2;
            }
            if (resolved.TileHeight != 1f && resolved.TileHeight > 0)
            {
                drawY -= (int)(resolved.TileHeight * TileSize) - TileSize;
            }
        }

        // Snap to integer pixels (VB6+DX8 pixel-perfect rendering)
        var srcRect = new Rect2(resolved.SX, resolved.SY, resolved.PixelWidth, resolved.PixelHeight);
        var destRect = new Rect2((float)Math.Round(drawX), (float)Math.Round(drawY), resolved.PixelWidth, resolved.PixelHeight);

        Color color = modulate ?? Colors.White;
        canvas.DrawTextureRectRegion(texture, destRect, srcRect, color);
    }
}
