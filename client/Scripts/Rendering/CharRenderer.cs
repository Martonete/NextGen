using System;
using System.Collections.Generic;
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
        GrhAnimator animator,
        float deltaMs = 0f,
        GameState? state = null)
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

        // Debug: log character draw info once (with rendering diagnostics)
        if (!ch._debugLogged)
        {
            ch._debugLogged = true;
            string bodyInfo = "N/A";
            if (ch.Body > 0 && ch.Body < data.Bodies.Length)
            {
                var b = data.Bodies[ch.Body];
                int walkGrh = b.Walk[heading];
                var resolved = walkGrh > 0 ? data.ResolveGrh(walkGrh, 0) : null;
                bool hasTex = resolved != null && resolved.FileNum > 0 &&
                              data.Textures?.GetTexture(resolved.FileNum) != null;
                bodyInfo = $"Walk[{heading}]={walkGrh} fileNum={resolved?.FileNum ?? 0} hasTex={hasTex}";
            }
            else
            {
                bodyInfo = ch.Body <= 0 ? "body<=0" : $"body>={data.Bodies.Length} (out of range)";
            }
            Godot.GD.Print($"[CHAR] '{ch.Name}' idx={ch.CharIndex}: body={ch.Body}({bodyInfo}) head={ch.Head} weapon={ch.WeaponAnim} shield={ch.ShieldAnim} casco={ch.CascoAnim}");
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
        DrawFx(canvas, ch, screenPos, data, animator, deltaMs);

        // Character-attached particles
        if (state != null)
            DrawCharParticles(canvas, ch, screenPos, state, data);

        // Name + clan above head (VB6: uses font1 bitmap font, toggled by N key)
        if (state == null || state.ShowNames)
            DrawName(canvas, ch, screenPos, data);

        // Dialog bubble (VB6: cDialogos.Render)
        DrawDialog(canvas, ch, screenPos, headOffset, data);
    }

    private static void DrawShadow(
        Node2D canvas, Character ch, Vector2 pos, int heading,
        GameData data, GrhAnimator animator)
    {
        // VB6 shadow is a body-clone rendered with special DX8 blending.
        // We draw a simple dark oval.
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
        int frame = ch.Moving ? (int)ch.WalkFrame : 0;

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

        // VB6: heading 1 gets X-1 adjustment
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
        if (ch.CascoAnim <= 2 || ch.CascoAnim >= data.Cascos.Length) return;
        var casco = data.Cascos[ch.CascoAnim];
        if (casco.Head[heading] == 0) return;

        float xAdj = (heading >= 1 && heading <= 3) ? 1f : 0f;
        Vector2 helmetPos = bodyPos + new Vector2(headOffset.X + xAdj, headOffset.Y + 1);

        DrawGrh(canvas, data, casco.Head[heading], 0, helmetPos, true);
    }

    private static void DrawWeapon(
        Node2D canvas, Character ch, Vector2 bodyPos, Vector2 headOffset,
        int heading, GameData data, GrhAnimator animator)
    {
        if (ch.WeaponAnim <= 0 || ch.WeaponAnim >= data.Weapons.Length) return;
        var weapon = data.Weapons[ch.WeaponAnim];
        int grhIndex = weapon.Walk[heading];
        if (grhIndex <= 0) return;

        // VB6: dibArm → Draw_Grh at PixelOffsetX + HeadOffset.X, PixelOffsetY + HeadOffset.Y + 38, center=1
        int frame = ch.Moving ? (int)ch.WalkFrame : 0;
        Vector2 weaponPos = bodyPos + new Vector2(headOffset.X, headOffset.Y + 38);
        DrawGrh(canvas, data, grhIndex, frame, weaponPos, true);
    }

    private static void DrawShield(
        Node2D canvas, Character ch, Vector2 bodyPos, Vector2 headOffset,
        int heading, GameData data, GrhAnimator animator)
    {
        if (ch.ShieldAnim <= 0 || ch.ShieldAnim >= data.Shields.Length) return;
        var shield = data.Shields[ch.ShieldAnim];
        int grhIndex = shield.Walk[heading];
        if (grhIndex <= 0) return;

        // VB6: dibEsc → Draw_Grh at PixelOffsetX + HeadOffset.X, PixelOffsetY + HeadOffset.Y + 38, center=1
        int frame = ch.Moving ? (int)ch.WalkFrame : 0;
        Vector2 shieldPos = bodyPos + new Vector2(headOffset.X, headOffset.Y + 38);
        DrawGrh(canvas, data, grhIndex, frame, shieldPos, true);
    }

    private static void DrawFx(
        Node2D canvas, Character ch, Vector2 pos,
        GameData data, GrhAnimator animator, float deltaMs)
    {
        for (int i = 0; i < 3; i++)
        {
            int fxIdx = ch.ActiveFxSlots[i];
            if (fxIdx <= 0 || fxIdx >= data.Fxs.Length) continue;

            var fx = data.Fxs[fxIdx];
            if (fx.Animacion <= 0) continue;

            // Resolve GRH to get numFrames and speed
            int grhIndex = fx.Animacion;
            if (grhIndex <= 0 || grhIndex >= data.Grhs.Length) continue;
            var grh = data.Grhs[grhIndex];
            int numFrames = grh.NumFrames;
            float speed = grh.Speed > 0 ? grh.Speed : 100f;

            if (numFrames <= 1)
            {
                // Static FX — just draw it
                Vector2 fxPos = pos + new Vector2(fx.OffsetX, fx.OffsetY);
                DrawGrh(canvas, data, grhIndex, 0, fxPos, true,
                        new Color(1, 1, 1, 150f / 255f));
                continue;
            }

            // Advance per-slot frame counter
            ch.FxFrameCounter[i] += deltaMs * numFrames / speed;

            // Check for animation cycle completion
            if (ch.FxFrameCounter[i] >= numFrames)
            {
                if (ch.FxLoops[i] == -1)
                {
                    // Infinite loop — wrap around
                    ch.FxFrameCounter[i] %= numFrames;
                }
                else
                {
                    ch.FxLoops[i]--;
                    if (ch.FxLoops[i] <= 0)
                    {
                        // Animation finished — clear slot
                        ch.ActiveFxSlots[i] = 0;
                        ch.FxLoops[i] = 0;
                        ch.FxFrameCounter[i] = 0;
                        continue;
                    }
                    // More loops remaining — wrap around
                    ch.FxFrameCounter[i] %= numFrames;
                }
            }

            int frame = (int)ch.FxFrameCounter[i];
            Vector2 fxDrawPos = pos + new Vector2(fx.OffsetX, fx.OffsetY);
            DrawGrh(canvas, data, grhIndex, frame, fxDrawPos, true,
                    new Color(1, 1, 1, 150f / 255f));
        }
    }

    /// <summary>
    /// Render character-attached particle streams centered on the character sprite.
    /// Particles are queued onto the additive blend layer (WorldRenderer) for proper VB6 glow.
    /// </summary>
    private static void DrawCharParticles(Node2D canvas, Character ch, Vector2 pos,
                                           GameState state, GameData data)
    {
        // Find char index for this character in state.Characters
        int charIdx = -1;
        foreach (var kvp in state.Characters)
        {
            if (ReferenceEquals(kvp.Value, ch))
            {
                charIdx = kvp.Key;
                break;
            }
        }
        if (charIdx < 0) return;

        // Get the WorldRenderer to queue additive draws
        var worldRenderer = canvas as WorldRenderer;

        foreach (var stream in state.MapParticles)
        {
            if (!stream.Active || stream.CharIndex != charIdx) continue;
            if (stream.DefIndex < 1 || stream.DefIndex >= state.ParticleDefs.Length) continue;

            // Center particles on character tile
            Vector2 charCenter = pos + new Vector2(TileSize / 2f, TileSize / 2f);

            foreach (var p in stream.Particles)
            {
                if (!p.Alive || p.GrhIndex <= 0) continue;
                var color = new Color(p.ColR / 255f, p.ColG / 255f, p.ColB / 255f, p.Alpha);
                Vector2 pPos = charCenter + new Vector2(p.X, p.Y);

                // Use animated GRH frame (VB6: particles animate)
                int frame = 0;
                if (data.Grhs != null && p.GrhIndex > 0 && p.GrhIndex < data.Grhs.Length)
                {
                    var grh = data.Grhs[p.GrhIndex];
                    if (grh.NumFrames > 1)
                    {
                        // Use global animation clock
                        long now = System.Environment.TickCount64;
                        float speed = grh.Speed > 0 ? grh.Speed : 100f;
                        frame = (int)(now / speed % grh.NumFrames);
                    }
                }

                if (worldRenderer != null)
                {
                    // Queue onto additive blend layer for proper glow
                    worldRenderer.QueueCharParticleDraw(p.GrhIndex, frame, pPos, color);
                }
                else
                {
                    // Fallback: draw directly (no additive blend)
                    DrawGrh(canvas, data, p.GrhIndex, frame, pPos, true, color);
                }
            }
        }
    }

    /// <summary>
    /// VB6: Draw name at PixelOffsetX+16, PixelOffsetY+30 (DT_CENTER)
    /// and clan/rank at PixelOffsetY+45.
    /// Uses font1 (bitmap font) for pixel-perfect match.
    /// </summary>
    private static void DrawName(Node2D canvas, Character ch, Vector2 pos, GameData data)
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

        // VB6: dead or in water → alpha 80, else 255
        byte alpha = (ch.Dead) ? (byte)80 : (byte)255;
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
                                    Vector2 headOffset, GameData data)
    {
        if (string.IsNullOrEmpty(ch.DialogText)) return;

        var font = data.Fonts[1]; // font1 for dialog
        if (font == null) return;

        long now = System.Environment.TickCount64;
        long elapsed = now - ch.DialogStartMs;

        // VB6 Sube logic (runs every frame, inside lifeTime >= 292 check)
        if (ch.DialogDurationMs >= 292)
        {
            if (ch.DialogRiseCounter > 0)
                ch.DialogRiseCounter--;
            if (ch.DialogRiseCounter > 0)
            {
                // VB6: Desvanecimiento += 12 while Sube > 0
                ch.DialogAlpha = Math.Min(255, ch.DialogAlpha + 12);
            }
        }

        // VB6: check lifetime → set Tiempito
        if (elapsed >= ch.DialogDurationMs && !ch.DialogFading)
            ch.DialogFading = true;

        // VB6: Desvanecimiento -= 10 while fading, remove at <= 9
        if (ch.DialogFading)
        {
            ch.DialogAlpha = Math.Max(0, ch.DialogAlpha - 10);
            if (ch.DialogAlpha <= 9)
            {
                ch.DialogText = "";
                return;
            }
        }

        byte alpha = (byte)Math.Clamp(ch.DialogAlpha, 0, 255);
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

        // VB6 font size for dialog: usedFont.Size (VB6 StdFont)
        // The VB6 cDialogos uses a StdFont, while Engine_Text_Draw uses bitmap font.
        // In the Render() method, offset calculation uses usedFont.size.
        // Typical VB6 AO dialog font size ≈ 8-10pt. We use font1.CharHeight (12).
        int fontSize = font.CharHeight;

        // VB6 Y calculation:
        // UpdateDialogPos: .Y = (PixelOffsetY + HeadOffset.Y) - (UBound(textLine) * 3)
        // Render: if Sube > 0: .Y += Sube / 1.2
        // Draw: .Y + offset + 2, offset starts at -(fontSize+2)*UBound(textLine)
        int baseY = (int)(pos.Y + headOffset.Y) - ((numLines - 1) * 3);
        if (ch.DialogRiseCounter > 0)
            baseY += (int)(ch.DialogRiseCounter / 1.2f);

        // VB6: offset starts at -(usedFont.size + 2) * UBound(.textLine())
        int offset = -(fontSize + 2) * (numLines - 1);

        // VB6: X = PixelOffsetX + HeadOffset.X - 168 - (MAX_LENGTH/2)*3 + 171
        //      = PixelOffsetX + HeadOffset.X - 33
        // But VB6 Engine_Text_Draw with DT_LEFT just draws at X. No centering.
        // The X offset of -33 + manual padding in FormatChat centers single-line text.
        // We'll center each line manually for cleaner rendering.
        int centerX = (int)(pos.X + headOffset.X) - 33 + 171;
        // Simplify: centerX ≈ pos.X + headOffset.X + 138... that's way off screen.
        // Actually: VB6 passes X = PixelOffsetX + HeadOffset.X - 168 to UpdateDialogPos
        // .X = X - 36 = PixelOffsetX + HeadOffset.X - 204
        // Draw at .X + 171 = PixelOffsetX + HeadOffset.X - 204 + 171 = PixelOffsetX + HeadOffset.X - 33
        // Hmm, HeadOffset.X is typically 0 to 4. So X = tileX - 33 + maybe 2 = tileX - 31.
        // But text is drawn from that X, left-aligned. For 24-char max, each char ~7px = 168px total.
        // Center of tile = tileX + 16. Text centered means start at tileX + 16 - 84 = tileX - 68.
        // VB6 FormatChat pads single-line text with spaces for centering.
        // Let's use the tile center and center the text properly.
        int textCenterX = (int)pos.X + 16;

        for (int i = 0; i < numLines; i++)
        {
            string line = lines[i];
            int lineY = baseY + offset + 2;

            font.DrawText(canvas, textCenterX, lineY, line, color, center: true);

            // VB6: offset += usedFont.size + 5
            offset += fontSize + 5;
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
