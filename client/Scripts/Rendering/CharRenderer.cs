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

        // Debug: log character draw info once
        if (!ch._debugLogged)
        {
            ch._debugLogged = true;
            Godot.GD.Print($"[CHAR] {ch.Name}: body={ch.Body} head={ch.Head} weapon={ch.WeaponAnim} shield={ch.ShieldAnim} casco={ch.CascoAnim}");
        }

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
        // VB6 shadow is a body-clone rendered with special DX8 blending.
        // We draw a simple dark oval without DrawSetTransform (which can
        // leave residual scale affecting subsequent draws).
        // Approximate oval with a flat wide rect + rounded edges.
        float cx = pos.X + TileSize / 2f;
        float cy = pos.Y + TileSize - 4f;
        var shadowRect = new Rect2(cx - 10f, cy - 3f, 20f, 6f);
        canvas.DrawRect(shadowRect, new Color(0, 0, 0, 0.18f));
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
        // VB6: NingunCasco=2 means "no helmet" — Cascos[1-2] are reserved/empty
        if (ch.CascoAnim <= 2 || ch.CascoAnim >= data.Cascos.Length) return;
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
        // Tierras Sagradas data: weapon Anim values in Obj.dat point to full
        // character Bodies (same Personajes.ind entries used for body sprites),
        // NOT weapon-only overlay sprites.  Drawing them as overlays would
        // render a second full character on top of the real body.
        // Skip weapon overlay drawing entirely — weapons are stats-only here.
        return;
    }

    private static void DrawShield(
        Node2D canvas, Character ch, Vector2 bodyPos, Vector2 headOffset,
        int heading, GameData data, GrhAnimator animator)
    {
        // Same as DrawWeapon — shield Anim values reference full character Bodies,
        // not shield-only overlay sprites.  Skip to avoid double-drawing.
        return;
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

            int frame = animator.GetCurrentFrame(fx.Animacion, data);

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

        // VB6: text Y is top-of-text. Godot DrawString Y is baseline.
        // Add font ascent (~11px) to convert VB6 top-Y to Godot baseline-Y.
        // VB6 nick at PixelOffsetY + 30 → Godot baseline ≈ Y + 42
        Vector2 nickSize = font.GetStringSize(nick, HorizontalAlignment.Left, -1, fontSize);
        Vector2 nickPos = pos + new Vector2((TileSize - nickSize.X) / 2, 42);
        canvas.DrawString(font, nickPos, nick, HorizontalAlignment.Left, -1, fontSize, nameColor);

        // VB6: rank badge at Y+45 for admins, clan for non-admins
        float nextY = 57f; // VB6 Y+45 + ascent ≈ 57

        // Admin rank badge (VB6: RangoPRIV)
        if (ch.Privileges > 0)
        {
            string rank = GetRankString(ch.Privileges);
            Vector2 rankSize = font.GetStringSize(rank, HorizontalAlignment.Left, -1, fontSize);
            Vector2 rankPos = pos + new Vector2((TileSize - rankSize.X) / 2, nextY);
            canvas.DrawString(font, rankPos, rank, HorizontalAlignment.Left, -1, fontSize, nameColor);
        }
        else if (clan.Length > 0)
        {
            Vector2 clanSize = font.GetStringSize(clan, HorizontalAlignment.Left, -1, fontSize);
            Vector2 clanPos = pos + new Vector2((TileSize - clanSize.X) / 2, nextY);
            canvas.DrawString(font, clanPos, clan, HorizontalAlignment.Left, -1, fontSize, nameColor);
        }
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
    /// For priv != 0: use privilege-indexed color.
    /// For priv == 0: criminal = red, citizen = light blue.
    /// </summary>
    private static Color GetNameColor(Character ch)
    {
        if (ch.Privileges > 0)
        {
            // Exact RGB values from Cliente/Data/INIT/colores.dat
            return ch.Privileges switch
            {
                1 => new Color(0 / 255f, 185 / 255f, 0 / 255f),       // Consejero - green
                2 => new Color(0 / 255f, 170 / 255f, 190 / 255f),     // Semidios - teal
                3 => new Color(128 / 255f, 128 / 255f, 64 / 255f),    // Event Master - olive
                4 => new Color(120 / 255f, 250 / 255f, 250 / 255f),   // Dios - cyan
                5 => new Color(180 / 255f, 180 / 255f, 180 / 255f),   // Rol Master - gray
                6 => new Color(140 / 255f, 0 / 255f, 0 / 255f),       // Caos - dark red
                7 => new Color(0 / 255f, 64 / 255f, 128 / 255f),      // Consejo Bander - dark blue
                8 => new Color(0 / 255f, 255 / 255f, 128 / 255f),     // Gran Dios - green
                9 => new Color(123 / 255f, 55 / 255f, 0 / 255f),      // Director - brown
                10 => new Color(128 / 255f, 255 / 255f, 128 / 255f),  // Developer - light green
                11 => new Color(255 / 255f, 198 / 255f, 0 / 255f),    // Sub Admin - gold
                12 => new Color(255 / 255f, 255 / 255f, 255 / 255f),  // Administrador - white
                _ => new Color(180 / 255f, 180 / 255f, 180 / 255f),   // Unknown - gray
            };
        }

        // colores.dat: [Cr] R=255,G=0,B=0 / [Ci] R=0,G=128,B=255
        if (ch.Criminal)
            return new Color(1.0f, 0.0f, 0.0f); // Criminal red

        return new Color(0 / 255f, 128 / 255f, 255 / 255f); // Citizen blue
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

        // Clamp source rect to actual texture dimensions.
        // Some GRH entries (e.g. water frame 2) reference regions beyond the
        // texture bounds (sprite sheet was truncated during PNG conversion).
        // Skip draws that are entirely out of bounds; clamp partial overlaps.
        int texW = texture.GetWidth();
        int texH = texture.GetHeight();

        float srcX = resolved.SX;
        float srcY = resolved.SY;
        float srcW = resolved.PixelWidth;
        float srcH = resolved.PixelHeight;

        if (srcX >= texW || srcY >= texH) return; // entirely outside

        if (srcX + srcW > texW) srcW = texW - srcX;
        if (srcY + srcH > texH) srcH = texH - srcY;
        if (srcW <= 0 || srcH <= 0) return;

        // Snap to integer pixels (VB6+DX8 pixel-perfect rendering)
        var srcRect = new Rect2(srcX, srcY, srcW, srcH);
        var destRect = new Rect2((float)Math.Round(drawX), (float)Math.Round(drawY), srcW, srcH);

        Color color = modulate ?? Colors.White;
        canvas.DrawTextureRectRegion(texture, destRect, srcRect, color);
    }
}
