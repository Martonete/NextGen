using System;
using System.Collections.Generic;
using Godot;
using ArgentumNextgen.Data;
using ArgentumNextgen.Game;

namespace ArgentumNextgen.Rendering;

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
public static partial class CharRenderer
{
    private const int TileSize = 32;

    public static void DrawCharacter(
        Node2D canvas,
        Character ch,
        Vector2 screenPos,
        GameData data,
        GrhAnimator animator,
        float deltaMs = 0f,
        GameState? state = null,
        WorldRenderer? worldRenderer = null,
        int charTileX = 0,
        int charTileY = 0)
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
            string shieldInfo = ch.ShieldAnim > 0 && ch.ShieldAnim < data.Shields.Length
                ? $"grh={data.Shields[ch.ShieldAnim].Walk[heading]}" : "none";
            string cascoInfo = ch.CascoAnim > 0 && ch.CascoAnim < data.Cascos.Length
                ? $"grh={data.Cascos[ch.CascoAnim].Head[heading]}" : "none";
            Godot.GD.Print($"[CHAR] '{ch.Name}' idx={ch.CharIndex}: body={ch.Body}({bodyInfo}) head={ch.Head} weapon={ch.WeaponAnim} shield={ch.ShieldAnim}({shieldInfo}) casco={ch.CascoAnim}({cascoInfo})");
        }

        // Pulsing transparency for dead and invisible (self) chars.
        // Time-based: full cycle ~2s. Range 18→53 (alpha ~45→135 of 255).
        if (ch.Dead || ch.Invisible)
        {
            float speed = deltaMs * 0.035f; // 35 / 1000ms = 0.035 per ms
            if (!ch.Llegoalatransp)
            {
                ch.TransparenciaBody = Math.Min(ch.TransparenciaBody + speed, 53f);
                if (ch.TransparenciaBody >= 53f) ch.Llegoalatransp = true;
            }
            else
            {
                ch.TransparenciaBody = Math.Max(ch.TransparenciaBody - speed, 18f);
                if (ch.TransparenciaBody <= 18f) ch.Llegoalatransp = false;
            }
        }

        // Emoticon loop countdown (VB6: decrements each frame, clears at 0)
        if (ch.EmoticonIndex > 0 && ch.EmoticonLoops > 0)
        {
            ch.EmoticonLoops--;
            if (ch.EmoticonLoops <= 0)
            {
                ch.EmoticonIndex = 0;
                ch.EmoticonLoops = 0;
            }
        }

        // Emoticon (VB6: rendered before body, at FxData offset above head, alpha 150)
        // Not drawn when invisible (VB6: entire char skipped in invisible branch)
        if (!ch.Invisible && ch.EmoticonIndex > 0 && ch.EmoticonLoops > 0 && ch.EmoticonIndex < data.Fxs.Length)
        {
            var emFx = data.Fxs[ch.EmoticonIndex];
            if (emFx.Animacion > 0)
            {
                // Position above head: screenPos + headOffset + FxData offset
                Vector2 emPos = screenPos + headOffset + new Vector2(emFx.OffsetX, emFx.OffsetY);
                int emFrame = 0;
                if (emFx.Animacion > 0 && emFx.Animacion < data.Grhs.Length)
                {
                    var emGrh = data.Grhs[emFx.Animacion];
                    if (emGrh.NumFrames > 1)
                    {
                        long now = System.Environment.TickCount64;
                        float speed = emGrh.Speed > 0 ? emGrh.Speed : 100f;
                        emFrame = (int)(now / speed % emGrh.NumFrames);
                    }
                }
                DrawGrh(canvas, data, emFx.Animacion, emFrame, emPos, true,
                        new Color(1, 1, 1, 150f / 255f));
            }
        }

        // Water reflections are now drawn by WorldRenderer (PASS 1.5) between
        // Layer 1 and Layer 2, so they clip naturally to water tiles.

        // Shadow: diagonal projection (light from lower-left → shadow upper-right)
        // Single body shadow only — no separate head (avoids doubling artifacts)
        bool drawShadow = true;
        if (state?.Config != null)
        {
            bool isNpc = ch.CharIndex != state.UserCharIndex && ch.NpcNumber > 0;
            drawShadow = isNpc ? state.Config.ShowNpcShadows : state.Config.ShowShadows;
        }
        if (drawShadow && !ch.Invisible)
            DrawShadow(canvas, ch, screenPos, heading, data, animator);

        // Auras use additive blend (D3DBLEND_ONE/ONE). Draws are collected in
        // WorldRenderer._Draw() and rendered by AuraAdditiveLayer ABOVE ContentLayer (z=1 > z=0).

        // VB6: invisible self = pulsing transparency (TransparenciaBody 0-100)
        Color? invisOverride = null;
        if (ch.Invisible)
            invisOverride = new Color(1, 1, 1, ch.TransparenciaBody / 100f);

        // Heading-dependent draw order (VB6: dibujarPersonaje)
        DrawCharParts(canvas, ch, screenPos, headOffset, heading, data, animator, state,
                      colorOverride: invisOverride);

        // Mark equipment debug as logged (after first full draw)
        if (!ch._equipDebugLogged && (ch.ShieldAnim > 0 || ch.CascoAnim > 0))
            ch._equipDebugLogged = true;

        // FX overlays — not drawn when invisible (VB6: entire char skipped in invisible branch)
        if (!ch.Invisible)
            DrawFx(canvas, ch, screenPos, data, animator, deltaMs);

        // Character-attached particles — not drawn when invisible
        if (!ch.Invisible && state != null && (state.Config?.ShowParticles ?? true))
            DrawCharParticles(canvas, ch, screenPos, state, data, worldRenderer);

        // Name + clan above head (VB6: uses font1 bitmap font, toggled by N key)
        // VB6: name IS drawn for invisible self (visible to self/GMs)
        if (state == null || state.ShowNames)
            DrawName(canvas, ch, screenPos, data, state);

        // Dialog bubble — queued to overlay layer (above all characters/NPCs)
        DrawDialog(canvas, ch, screenPos, headOffset, data, deltaMs, worldRenderer);
    }

    /// <summary>
    /// Character shadow: body + head projected from a shared anchor point (body feet).
    /// Light from lower-left → shadow falls toward upper-right.
    /// Both sprites are projected through the same transform so the head
    /// shadow sits directly on top of the body shadow, forming a complete silhouette.
    /// </summary>
    private static void DrawShadow(
        Node2D canvas, Character ch, Vector2 screenPos, int heading,
        GameData data, GrhAnimator animator)
    {
        if (ch.Body <= 0 || ch.Body >= data.Bodies.Length) return;

        // Shadow projection constants
        const float ShearRatio = 0.3f;  // per pixel above feet, shift right by 0.3px
        const float FlatRatio = 0.85f;  // per pixel above feet, compress to 0.85px
        Color shadowColor = new(0, 0, 0, 0.35f);

        // Resolve body to compute the shared anchor point (feetY)
        int bodyGrh = data.Bodies[ch.Body].Walk[heading];
        if (bodyGrh <= 0) return;
        int bodyFrame = ch.Moving ? (int)ch.WalkFrame : 0;
        var bodyRes = data.ResolveGrh(bodyGrh, bodyFrame);
        if (bodyRes == null || bodyRes.FileNum <= 0) return;

        // Body draw position (with centering)
        float bodyDrawX = screenPos.X;
        float bodyDrawY = screenPos.Y;
        if (bodyRes.TileWidth != 1f && bodyRes.TileWidth > 0)
            bodyDrawX -= (int)(bodyRes.TileWidth * (TileSize / 2)) - TileSize / 2;
        if (bodyRes.TileHeight != 1f && bodyRes.TileHeight > 0)
            bodyDrawY -= (int)(bodyRes.TileHeight * TileSize) - TileSize;

        // Shared anchor: body feet (bottom of body sprite)
        float feetY = bodyDrawY + bodyRes.PixelHeight;

        // Draw body shadow
        DrawShadowProjected(canvas, bodyRes, bodyDrawX, bodyDrawY, feetY,
            ShearRatio, FlatRatio, shadowColor, data);

        // Draw head shadow (projected from same feetY)
        if (ch.Head > 0 && ch.Head < data.Heads.Length)
        {
            int headGrh = data.Heads[ch.Head].Head[heading];
            if (headGrh <= 0) return;
            var headRes = data.ResolveGrh(headGrh, 0);
            if (headRes == null || headRes.FileNum <= 0) return;

            // Head normal position = body position + headOffset
            float headDrawX = screenPos.X + data.Bodies[ch.Body].HeadOffsetX;
            float headDrawY = screenPos.Y + data.Bodies[ch.Body].HeadOffsetY;
            // Head centering (same logic as DrawGrh)
            if (headRes.TileWidth != 1f && headRes.TileWidth > 0)
                headDrawX -= (int)(headRes.TileWidth * (TileSize / 2)) - TileSize / 2;
            if (headRes.TileHeight != 1f && headRes.TileHeight > 0)
                headDrawY -= (int)(headRes.TileHeight * TileSize) - TileSize;

            DrawShadowProjected(canvas, headRes, headDrawX, headDrawY, feetY,
                ShearRatio, FlatRatio, shadowColor, data);
        }

        // Draw helmet shadow (projected from same feetY)
        if (ch.CascoAnim > 0 && ch.CascoAnim < data.Cascos.Length)
        {
            var casco = data.Cascos[ch.CascoAnim];
            if (casco.Head != null && casco.Head[heading] > 0)
            {
                var cascoRes = data.ResolveGrh(casco.Head[heading], 0);
                if (cascoRes != null && cascoRes.FileNum > 0)
                {
                    float xAdj = (heading >= 1 && heading <= 3) ? 1f : 0f;
                    float cascoDrawX = screenPos.X + data.Bodies[ch.Body].HeadOffsetX + xAdj;
                    float cascoDrawY = screenPos.Y + data.Bodies[ch.Body].HeadOffsetY;
                    if (cascoRes.TileWidth != 1f && cascoRes.TileWidth > 0)
                        cascoDrawX -= (int)(cascoRes.TileWidth * (TileSize / 2)) - TileSize / 2;
                    if (cascoRes.TileHeight != 1f && cascoRes.TileHeight > 0)
                        cascoDrawY -= (int)(cascoRes.TileHeight * TileSize) - TileSize;

                    DrawShadowProjected(canvas, cascoRes, cascoDrawX, cascoDrawY, feetY,
                        ShearRatio, FlatRatio, shadowColor, data);
                }
            }
        }

        // Draw weapon shadow (projected from same feetY)
        if (ch.WeaponAnim > 0 && ch.WeaponAnim < data.Weapons.Length)
        {
            int weapGrh = data.Weapons[ch.WeaponAnim].Walk[heading];
            if (weapGrh > 0)
            {
                int weapFrame = ch.Moving ? (int)ch.WalkFrame : 0;
                var weapRes = data.ResolveGrh(weapGrh, weapFrame);
                if (weapRes != null && weapRes.FileNum > 0)
                {
                    float weapDrawX = screenPos.X;
                    float weapDrawY = screenPos.Y;
                    if (weapRes.TileWidth != 1f && weapRes.TileWidth > 0)
                        weapDrawX -= (int)(weapRes.TileWidth * (TileSize / 2)) - TileSize / 2;
                    if (weapRes.TileHeight != 1f && weapRes.TileHeight > 0)
                        weapDrawY -= (int)(weapRes.TileHeight * TileSize) - TileSize;

                    DrawShadowProjected(canvas, weapRes, weapDrawX, weapDrawY, feetY,
                        ShearRatio, FlatRatio, shadowColor, data);
                }
            }
        }

        // Draw shield shadow (projected from same feetY)
        if (ch.ShieldAnim > 0 && ch.ShieldAnim < data.Shields.Length)
        {
            int shieldGrh = data.Shields[ch.ShieldAnim].Walk[heading];
            if (shieldGrh > 0)
            {
                int shieldFrame = ch.Moving ? (int)ch.WalkFrame : 0;
                var shieldRes = data.ResolveGrh(shieldGrh, shieldFrame);
                if (shieldRes != null && shieldRes.FileNum > 0)
                {
                    float shieldDrawX = screenPos.X;
                    float shieldDrawY = screenPos.Y;
                    if (shieldRes.TileWidth != 1f && shieldRes.TileWidth > 0)
                        shieldDrawX -= (int)(shieldRes.TileWidth * (TileSize / 2)) - TileSize / 2;
                    if (shieldRes.TileHeight != 1f && shieldRes.TileHeight > 0)
                        shieldDrawY -= (int)(shieldRes.TileHeight * TileSize) - TileSize;

                    DrawShadowProjected(canvas, shieldRes, shieldDrawX, shieldDrawY, feetY,
                        ShearRatio, FlatRatio, shadowColor, data);
                }
            }
        }
    }

    /// <summary>
    /// Project a sprite as a shadow parallelogram from a shared anchor (feetY).
    /// Each corner (px, py) is transformed:
    ///   dy = feetY - py (distance above feet)
    ///   shadowX = px + dy * shearRatio
    ///   shadowY = feetY - dy * flatRatio
    /// This ensures body + head shadows form a coherent silhouette.
    /// </summary>
    private static void DrawShadowProjected(
        Node2D canvas, GrhData resolved, float drawX, float drawY, float feetY,
        float shearRatio, float flatRatio, Color shadowColor, GameData data)
    {
        var texture = data.Textures?.GetTexture(resolved.FileNum);
        if (texture == null) return;

        int texW = texture.GetWidth(), texH = texture.GetHeight();
        int sx = resolved.SX, sy = resolved.SY;
        int pw = resolved.PixelWidth, ph = resolved.PixelHeight;
        if (texW > 0) sx %= texW;
        if (texH > 0) sy %= texH;
        if (sx + pw > texW) pw = texW - sx;
        if (sy + ph > texH) ph = texH - sy;
        if (pw <= 0 || ph <= 0) return;

        // Distance from feet for top and bottom edges of this sprite
        float topDy = feetY - drawY;             // top of sprite
        float botDy = feetY - (drawY + ph);      // bottom of sprite

        // Project 4 corners through the shadow transform
        Vector2 tl = new(drawX + topDy * shearRatio, feetY - topDy * flatRatio);
        Vector2 tr = new(drawX + pw + topDy * shearRatio, feetY - topDy * flatRatio);
        Vector2 bl = new(drawX + botDy * shearRatio, feetY - botDy * flatRatio);
        Vector2 br = new(drawX + pw + botDy * shearRatio, feetY - botDy * flatRatio);

        float u0 = (float)sx / texW;
        float u1 = (float)(sx + pw) / texW;
        float vTop = (float)sy / texH;
        float vBot = (float)(sy + ph) / texH;

        // CCW: BL → BR → TR → TL
        canvas.DrawPolygon(
            new[] { bl, br, tr, tl },
            new[] { shadowColor, shadowColor, shadowColor, shadowColor },
            new[] { new Vector2(u0, vBot), new Vector2(u1, vBot),
                    new Vector2(u1, vTop), new Vector2(u0, vTop) },
            texture);
    }

    /// <summary>
    /// Draw character parts (body, head, helmet, weapon, shield) in VB6 heading order.
    /// Used by both normal rendering and reflection (via DrawSetTransform flip).
    /// </summary>
    private static void DrawCharParts(
        Node2D canvas, Character ch, Vector2 pos, Vector2 headOffset,
        int heading, GameData data, GrhAnimator animator,
        GameState? state = null, Color? colorOverride = null,
        Color? equipColorOverride = null, Color? helmetColorOverride = null)
    {
        Color? ec = equipColorOverride ?? colorOverride;
        Color? hc = helmetColorOverride ?? ec;
        switch (heading)
        {
            case 1: // North
                DrawWeapon(canvas, ch, pos, headOffset, heading, data, animator, ec);
                DrawShield(canvas, ch, pos, headOffset, heading, data, animator, ec);
                DrawBody(canvas, ch, pos, heading, data, animator, state, colorOverride);
                DrawHead(canvas, ch, pos, headOffset, heading, data, state, colorOverride);
                DrawHelmet(canvas, ch, pos, headOffset, heading, data, hc);
                break;
            case 2: // East
                DrawShield(canvas, ch, pos, headOffset, heading, data, animator, ec);
                DrawBody(canvas, ch, pos, heading, data, animator, state, colorOverride);
                DrawHead(canvas, ch, pos, headOffset, heading, data, state, colorOverride);
                DrawHelmet(canvas, ch, pos, headOffset, heading, data, hc);
                DrawWeapon(canvas, ch, pos, headOffset, heading, data, animator, ec);
                break;
            case 3: // South
                DrawBody(canvas, ch, pos, heading, data, animator, state, colorOverride);
                DrawHead(canvas, ch, pos, headOffset, heading, data, state, colorOverride);
                DrawHelmet(canvas, ch, pos, headOffset, heading, data, hc);
                DrawWeapon(canvas, ch, pos, headOffset, heading, data, animator, ec);
                DrawShield(canvas, ch, pos, headOffset, heading, data, animator, ec);
                break;
            case 4: // West
                DrawWeapon(canvas, ch, pos, headOffset, heading, data, animator, ec);
                DrawBody(canvas, ch, pos, heading, data, animator, state, colorOverride);
                DrawHead(canvas, ch, pos, headOffset, heading, data, state, colorOverride);
                DrawHelmet(canvas, ch, pos, headOffset, heading, data, hc);
                DrawShield(canvas, ch, pos, headOffset, heading, data, animator, ec);
                break;
        }
    }

    private static void DrawBody(
        Node2D canvas, Character ch, Vector2 pos, int heading,
        GameData data, GrhAnimator animator, GameState? state = null,
        Color? colorOverride = null)
    {
        if (ch.Body <= 0 || ch.Body >= data.Bodies.Length) return;
        var body = data.Bodies[ch.Body];
        if (body.Walk[heading] == 0) return;

        int bodyGrh = body.Walk[heading];
        int frame = ch.Moving ? (int)ch.WalkFrame : 0;

        if (colorOverride.HasValue)
        {
            DrawGrh(canvas, data, bodyGrh, frame, pos, true, colorOverride.Value);
            return;
        }
        // VB6: dead alpha = TransparenciaBody + 45 (pulsing 45-145), alive = 255
        // Only pulse when DeadCharTransparency config is enabled
        byte alpha = (ch.Dead && (state?.Config?.DeadCharTransparency ?? true))
            ? (byte)(ch.TransparenciaBody + 45) : (byte)255;
        DrawGrh(canvas, data, bodyGrh, frame, pos, true,
                alpha < 255 ? new Color(1, 1, 1, alpha / 255f) : Colors.White);
    }

    private static void DrawHead(
        Node2D canvas, Character ch, Vector2 bodyPos, Vector2 headOffset,
        int heading, GameData data, GameState? state = null,
        Color? colorOverride = null)
    {
        if (ch.Head <= 0 || ch.Head >= data.Heads.Length) return;
        var head = data.Heads[ch.Head];
        if (head.Head[heading] == 0) return;

        // VB6: heading 1 gets X-1 adjustment; mounted gets X+1
        float xAdj = heading == 1 ? -1f : 0f;
        if (ch.Mounted) xAdj += 1f;
        Vector2 headPos = bodyPos + new Vector2(headOffset.X + xAdj, headOffset.Y + 1);

        if (colorOverride.HasValue)
        {
            DrawGrh(canvas, data, head.Head[heading], 0, headPos, true, colorOverride.Value);
            return;
        }
        // VB6: dead alpha = TransparenciaBody + 45 (pulsing 45-145)
        // Only pulse when DeadCharTransparency config is enabled
        byte alpha = (ch.Dead && (state?.Config?.DeadCharTransparency ?? true))
            ? (byte)(ch.TransparenciaBody + 45) : (byte)255;
        DrawGrh(canvas, data, head.Head[heading], 0, headPos, true,
                alpha < 255 ? new Color(1, 1, 1, alpha / 255f) : Colors.White);
    }

    private static void DrawHelmet(
        Node2D canvas, Character ch, Vector2 bodyPos, Vector2 headOffset,
        int heading, GameData data, Color? colorOverride = null)
    {
        if (ch.CascoAnim <= 0 || ch.CascoAnim >= data.Cascos.Length)
        {
            if (!ch._equipDebugLogged && ch.CascoAnim > 0)
            {
                Godot.GD.PrintErr($"[HELMET] '{ch.Name}' CascoAnim={ch.CascoAnim} OUT OF RANGE (max={data.Cascos.Length-1})");
            }
            return;
        }
        var casco = data.Cascos[ch.CascoAnim];
        if (casco.Head == null || casco.Head[heading] == 0)
        {
            if (!ch._equipDebugLogged)
                Godot.GD.PrintErr($"[HELMET] '{ch.Name}' CascoAnim={ch.CascoAnim} Head[{heading}]=0 (no GRH)");
            return;
        }

        int grhIdx = casco.Head[heading];
        if (!ch._equipDebugLogged)
        {
            var resolved = data.ResolveGrh(grhIdx, 0);
            bool hasTex = resolved != null && resolved.FileNum > 0 &&
                          data.Textures?.GetTexture(resolved.FileNum) != null;
            Godot.GD.Print($"[HELMET] '{ch.Name}' CascoAnim={ch.CascoAnim} heading={heading} grh={grhIdx} fileNum={resolved?.FileNum ?? 0} hasTex={hasTex}");
        }

        float xAdj = (heading >= 1 && heading <= 3) ? 1f : 0f;
        if (ch.Mounted) xAdj += 1f;
        Vector2 helmetPos = bodyPos + new Vector2(headOffset.X + xAdj, headOffset.Y);

        DrawGrh(canvas, data, grhIdx, 0, helmetPos, true, colorOverride);
    }

    private static void DrawWeapon(
        Node2D canvas, Character ch, Vector2 bodyPos, Vector2 headOffset,
        int heading, GameData data, GrhAnimator animator,
        Color? colorOverride = null)
    {
        if (ch.WeaponAnim <= 0 || ch.WeaponAnim >= data.Weapons.Length) return;
        var weapon = data.Weapons[ch.WeaponAnim];
        int grhIndex = weapon.Walk[heading];
        if (grhIndex <= 0) return;

        // VB6: Arma.WeaponWalk drawn at PixelOffsetX, PixelOffsetY (same as body), center=1
        int frame = ch.Moving ? (int)ch.WalkFrame : 0;
        DrawGrh(canvas, data, grhIndex, frame, bodyPos, true, colorOverride);
    }

    private static void DrawShield(
        Node2D canvas, Character ch, Vector2 bodyPos, Vector2 headOffset,
        int heading, GameData data, GrhAnimator animator,
        Color? colorOverride = null)
    {
        if (ch.ShieldAnim <= 0 || ch.ShieldAnim >= data.Shields.Length)
        {
            if (!ch._equipDebugLogged && ch.ShieldAnim > 0)
                Godot.GD.PrintErr($"[SHIELD] '{ch.Name}' ShieldAnim={ch.ShieldAnim} OUT OF RANGE (max={data.Shields.Length-1})");
            return;
        }
        var shield = data.Shields[ch.ShieldAnim];
        int grhIndex = shield.Walk[heading];
        if (grhIndex <= 0)
        {
            if (!ch._equipDebugLogged)
                Godot.GD.PrintErr($"[SHIELD] '{ch.Name}' ShieldAnim={ch.ShieldAnim} Walk[{heading}]=0 (no GRH)");
            return;
        }

        if (!ch._equipDebugLogged)
        {
            var resolved = data.ResolveGrh(grhIndex, 0);
            bool hasTex = resolved != null && resolved.FileNum > 0 &&
                          data.Textures?.GetTexture(resolved.FileNum) != null;
            Godot.GD.Print($"[SHIELD] '{ch.Name}' ShieldAnim={ch.ShieldAnim} heading={heading} grh={grhIndex} fileNum={resolved?.FileNum ?? 0} hasTex={hasTex}");
        }

        // VB6: Escudo.ShieldWalk drawn at PixelOffsetX, PixelOffsetY (same as body), center=1
        int frame = ch.Moving ? (int)ch.WalkFrame : 0;
        DrawGrh(canvas, data, grhIndex, frame, bodyPos, true, colorOverride);
    }

    /// <summary>
    /// Collect aura draw data for a character and queue to WorldRenderer's aura layer.
    /// The aura layer (z=1) draws ABOVE the content layer (z=0), so auras appear
    /// on top of layer 3 tiles and characters, with additive blend.
    /// Position: PixelOffsetX + HeadOffset.X, HeadOffset.Y + PixelOffsetY + 72 - offset
    /// Rotation: angle += 0.004 per frame if Giratoria, wraps at 180
    /// </summary>
    public static void CollectAuraDraws(
        WorldRenderer worldRenderer, Character ch, Vector2 pos, Vector2 headOffset,
        GameData data, float alphaOverride = 1f)
    {
        if (data.Auras == null || data.Auras.Length <= 1) return;
        if (ch.Navigating) return; // No auras while on a boat

        CollectSingleAura(worldRenderer, pos, headOffset, data, ch.AuraIndexA, ref ch.AuraAngleA, alphaOverride);
        CollectSingleAura(worldRenderer, pos, headOffset, data, ch.AuraIndexW, ref ch.AuraAngleW, alphaOverride);
        CollectSingleAura(worldRenderer, pos, headOffset, data, ch.AuraIndexE, ref ch.AuraAngleE, alphaOverride);
        CollectSingleAura(worldRenderer, pos, headOffset, data, ch.AuraIndexR, ref ch.AuraAngleR, alphaOverride);
        CollectSingleAura(worldRenderer, pos, headOffset, data, ch.AuraIndexC, ref ch.AuraAngleC, alphaOverride);
        CollectSingleAura(worldRenderer, pos, headOffset, data, ch.NpcAura, ref ch.NpcAuraAngle, alphaOverride);
    }

    private static void CollectSingleAura(
        WorldRenderer worldRenderer, Vector2 pos, Vector2 headOffset,
        GameData data, int auraIndex, ref float angle, float alphaOverride = 1f)
    {
        if (auraIndex <= 0 || auraIndex >= data.Auras.Length) return;

        var aura = data.Auras[auraIndex];
        if (aura.GrhIndex <= 0) return;

        // VB6: Giratoria — 0.004 rad/frame at ~24fps = ~0.096 rad/sec.
        // Use absolute time so rotation speed is FPS-independent.
        if (aura.Giratoria)
        {
            angle = (float)(System.Environment.TickCount64 * 0.000096 % 180.0);
        }

        // VB6 position: PixelOffsetX + HeadOffset.X, HeadOffset.Y + PixelOffsetY + 72 - offset
        float auraX = pos.X + headOffset.X;
        float auraY = pos.Y + headOffset.Y + 72 - aura.Offset;

        // VB6 color: static R,G,B. Alpha reduced when invisible (pulsing with body).
        Color color = new Color(aura.R / 255f, aura.G / 255f, aura.B / 255f, alphaOverride);

        // Resolve animated GRH frame
        int grhIndex = aura.GrhIndex;
        int frame = 0;
        if (grhIndex > 0 && grhIndex < data.Grhs.Length)
        {
            var grh = data.Grhs[grhIndex];
            if (grh.NumFrames > 1)
            {
                long now = System.Environment.TickCount64;
                float speed = grh.Speed > 0 ? grh.Speed : 100f;
                frame = (int)(now / speed % grh.NumFrames);
            }
        }

        // Queue to WorldRenderer's aura additive layer
        worldRenderer.QueueAuraDraw(grhIndex, frame, new Vector2(auraX, auraY), color,
                                     aura.Giratoria ? angle : 0f);
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
                // Static FX — draw once per loop, then clear (VB6: .Started=0 after Draw_Grh)
                if (ch.FxLoops[i] != -1)
                {
                    ch.FxLoops[i]--;
                    if (ch.FxLoops[i] <= 0)
                    {
                        ch.ActiveFxSlots[i] = 0;
                        ch.FxLoops[i] = 0;
                        ch.FxFrameCounter[i] = 0;
                        continue;
                    }
                }
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
                                           GameState state, GameData data,
                                           WorldRenderer? worldRenderer = null)
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

        foreach (var stream in state.MapParticles)
        {
            if (!stream.Active || stream.CharIndex != charIdx) continue;
            if (stream.DefIndex < 1 || stream.DefIndex >= state.ParticleDefs.Length) continue;

            // VB6: particles render at screenPos + particle offset (no centering)
            // Particle X1/Y1/X2/Y2 in Particles.ini already define the spawn offset

            foreach (var p in stream.Particles)
            {
                if (!p.Alive || p.GrhIndex <= 0) continue;
                var color = new Color(p.ColR / 255f, p.ColG / 255f, p.ColB / 255f, p.Alpha);
                Vector2 pPos = pos + new Vector2(p.X, p.Y);

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

}
