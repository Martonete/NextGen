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

        // VB6: dead character transparency pulsing (TransparenciaBody oscillates 0→100→0)
        if (ch.Dead)
        {
            if (!ch.Llegoalatransp)
            {
                ch.TransparenciaBody++;
                if (ch.TransparenciaBody >= 100) ch.Llegoalatransp = true;
            }
            else
            {
                ch.TransparenciaBody--;
                if (ch.TransparenciaBody <= 0) ch.Llegoalatransp = false;
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
        if (ch.EmoticonIndex > 0 && ch.EmoticonLoops > 0 && ch.EmoticonIndex < data.Fxs.Length)
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

        // Water reflection (VB6: HayAgua check, body/head flipped vertically with alpha)
        if ((state?.Config?.ShowReflections ?? true) && charTileY > 0 && state?.MapData != null)
        {
            if (WorldRenderer.IsWater(state.MapData, charTileX, charTileY + 1))
                DrawReflection(canvas, ch, screenPos, headOffset, heading, data, animator);
        }

        // Shadow: diagonal projection (light from lower-left → shadow upper-right)
        // Single body shadow only — no separate head (avoids doubling artifacts)
        bool drawShadow = true;
        if (state?.Config != null)
        {
            bool isNpc = ch.CharIndex != state.UserCharIndex && ch.NpcNumber > 0;
            drawShadow = isNpc ? state.Config.ShowNpcShadows : state.Config.ShowShadows;
        }
        if (drawShadow)
            DrawShadow(canvas, ch, screenPos, heading, data, animator);

        // Auras use additive blend (D3DBLEND_ONE/ONE). Draws are collected in
        // WorldRenderer._Draw() and rendered by AuraAdditiveLayer ABOVE ContentLayer (z=1 > z=0).

        // Heading-dependent draw order (VB6: dibujarPersonaje)
        switch (heading)
        {
            case 1: // North
                DrawWeapon(canvas, ch, screenPos, headOffset, heading, data, animator);
                DrawShield(canvas, ch, screenPos, headOffset, heading, data, animator);
                DrawBody(canvas, ch, screenPos, heading, data, animator, state);
                DrawHead(canvas, ch, screenPos, headOffset, heading, data, state);
                DrawHelmet(canvas, ch, screenPos, headOffset, heading, data);
                break;

            case 2: // East
                DrawShield(canvas, ch, screenPos, headOffset, heading, data, animator);
                DrawBody(canvas, ch, screenPos, heading, data, animator, state);
                DrawHead(canvas, ch, screenPos, headOffset, heading, data, state);
                DrawHelmet(canvas, ch, screenPos, headOffset, heading, data);
                DrawWeapon(canvas, ch, screenPos, headOffset, heading, data, animator);
                break;

            case 3: // South (default)
                DrawBody(canvas, ch, screenPos, heading, data, animator, state);
                DrawHead(canvas, ch, screenPos, headOffset, heading, data, state);
                DrawHelmet(canvas, ch, screenPos, headOffset, heading, data);
                DrawWeapon(canvas, ch, screenPos, headOffset, heading, data, animator);
                DrawShield(canvas, ch, screenPos, headOffset, heading, data, animator);
                break;

            case 4: // West
                DrawWeapon(canvas, ch, screenPos, headOffset, heading, data, animator);
                DrawBody(canvas, ch, screenPos, heading, data, animator, state);
                DrawHead(canvas, ch, screenPos, headOffset, heading, data, state);
                DrawHelmet(canvas, ch, screenPos, headOffset, heading, data);
                DrawShield(canvas, ch, screenPos, headOffset, heading, data, animator);
                break;
        }

        // Mark equipment debug as logged (after first full draw)
        if (!ch._equipDebugLogged && (ch.ShieldAnim > 0 || ch.CascoAnim > 0))
            ch._equipDebugLogged = true;

        // FX overlays (up to 3 simultaneous)
        DrawFx(canvas, ch, screenPos, data, animator, deltaMs);

        // Character-attached particles (queued to WorldRenderer's additive layer)
        if (state != null && (state.Config?.ShowParticles ?? true))
            DrawCharParticles(canvas, ch, screenPos, state, data, worldRenderer);

        // Name + clan above head (VB6: uses font1 bitmap font, toggled by N key)
        if (state == null || state.ShowNames)
            DrawName(canvas, ch, screenPos, data);

        // Dialog bubble (VB6: cDialogos.Render)
        DrawDialog(canvas, ch, screenPos, headOffset, data, deltaMs);
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
    /// VB6: water reflection — body and head drawn flipped vertically below character
    /// when there's water at (tileX, tileY+1). Alpha ~100/255.
    /// Mirror axis = pos.Y + TileSize (waterline at character feet).
    /// Each component's normal draw-Y is mirrored: reflectedTop = 2*mirrorY - normalBottom.
    /// </summary>
    /// <summary>
    /// VB6 water reflection: draws body, head, helmet, weapon, shield inverted (Invert_y=True).
    /// VB6 offsets (relative to PixelOffsetX/Y):
    ///   Body:   (X, Y - HeadOffset.Y) alpha=100
    ///   Head:   (X - HeadOffset.X, Y - HeadOffset.Y + 11) alpha=100 (or +24 if dead)
    ///   Casco:  (X - HeadOffset.X + 1, Y - HeadOffset.Y + 14) alpha=150
    ///   Arma:   (X, Y + 40) alpha=100
    ///   Escudo: (X, Y + 40) alpha=100
    /// All drawn with Invert_y=True and center=1.
    /// </summary>
    private static void DrawReflection(
        Node2D canvas, Character ch, Vector2 pos, Vector2 headOffset,
        int heading, GameData data, GrhAnimator animator)
    {
        float hoX = headOffset.X, hoY = headOffset.Y;

        // When mounted, the body is the mount sprite (very tall, hoY≈-45+).
        // Head/helmet/weapon/shield gap formulas scale with |hoY|, which pushes
        // them way too far from the mount body. Clamp to normal human range (-30)
        // so the rider's head stays close to the mount in the reflection.
        float hoYForParts = (ch.Mounted && hoY < -30f) ? -30f : hoY;

        // Shift entire reflection 2px closer to character feet
        Vector2 rPos = new(pos.X, pos.Y - 2);

        // Reflected auras are collected in WorldRenderer._Draw() (timing requirement)

        // Use heading-dependent draw order (same as dibujarPersonaje)
        switch (heading)
        {
            case 1: // North
                DrawReflWeapon(canvas, ch, rPos, hoYForParts, heading, data, animator);
                DrawReflShield(canvas, ch, rPos, hoYForParts, heading, data, animator);
                DrawReflBody(canvas, ch, rPos, hoY, heading, data, animator);
                DrawReflHead(canvas, ch, rPos, hoX, hoYForParts, heading, data);
                DrawReflHelmet(canvas, ch, rPos, hoX, hoYForParts, heading, data);
                break;

            case 2: // East
                DrawReflShield(canvas, ch, rPos, hoYForParts, heading, data, animator);
                DrawReflBody(canvas, ch, rPos, hoY, heading, data, animator);
                DrawReflHead(canvas, ch, rPos, hoX, hoYForParts, heading, data);
                DrawReflHelmet(canvas, ch, rPos, hoX, hoYForParts, heading, data);
                DrawReflWeapon(canvas, ch, rPos, hoYForParts, heading, data, animator);
                break;

            case 3: // South
                DrawReflBody(canvas, ch, rPos, hoY, heading, data, animator);
                DrawReflHead(canvas, ch, rPos, hoX, hoYForParts, heading, data);
                DrawReflHelmet(canvas, ch, rPos, hoX, hoYForParts, heading, data);
                DrawReflWeapon(canvas, ch, rPos, hoYForParts, heading, data, animator);
                DrawReflShield(canvas, ch, rPos, hoYForParts, heading, data, animator);
                break;

            case 4: // West
                DrawReflWeapon(canvas, ch, rPos, hoYForParts, heading, data, animator);
                DrawReflBody(canvas, ch, rPos, hoY, heading, data, animator);
                DrawReflHead(canvas, ch, rPos, hoX, hoYForParts, heading, data);
                DrawReflHelmet(canvas, ch, rPos, hoX, hoYForParts, heading, data);
                DrawReflShield(canvas, ch, rPos, hoYForParts, heading, data, animator);
                break;
        }
    }

    /// <summary>
    /// Queue reflected auras to the additive layer (proper blending, no black bg).
    /// Position mirrors the normal aura position below the character's feet.
    /// </summary>
    public static void CollectReflAuraDraws(
        WorldRenderer worldRenderer, Character ch, Vector2 pos, Vector2 headOffset,
        GameData data)
    {
        if (data.Auras == null || data.Auras.Length <= 1) return;

        CollectSingleReflAura(worldRenderer, pos, headOffset, data, ch.AuraIndexA, ch.AuraAngleA);
        CollectSingleReflAura(worldRenderer, pos, headOffset, data, ch.AuraIndexW, ch.AuraAngleW);
        CollectSingleReflAura(worldRenderer, pos, headOffset, data, ch.AuraIndexE, ch.AuraAngleE);
        CollectSingleReflAura(worldRenderer, pos, headOffset, data, ch.AuraIndexR, ch.AuraAngleR);
        CollectSingleReflAura(worldRenderer, pos, headOffset, data, ch.AuraIndexC, ch.AuraAngleC);
        CollectSingleReflAura(worldRenderer, pos, headOffset, data, ch.NpcAura, ch.NpcAuraAngle);
    }

    private static void CollectSingleReflAura(
        WorldRenderer worldRenderer, Vector2 pos, Vector2 headOffset,
        GameData data, int auraIndex, float angle)
    {
        if (auraIndex <= 0 || auraIndex >= data.Auras.Length) return;

        var aura = data.Auras[auraIndex];
        if (aura.GrhIndex <= 0) return;

        // Place reflected aura at the same Y as the reflected body center.
        // The sprite is UV-flipped (handled by DrawPendingAuras reflected path).
        float absHo = -headOffset.Y > 1f ? -headOffset.Y : 1f;
        float bodyReflY = (absHo < 36f ? 36f : absHo) + 5f; // same clamp+offset as DrawReflBody
        float reflAuraY = pos.Y + bodyReflY;

        float auraX = pos.X + headOffset.X;

        // Reduced alpha for reflection effect
        Color color = new Color(aura.R / 255f, aura.G / 255f, aura.B / 255f, 0.3f);

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

        // Queue to the additive layer with reflected=true (UV-flipped, proper blending)
        worldRenderer.QueueAuraDraw(grhIndex, frame, new Vector2(auraX, reflAuraY), color,
                                     aura.Giratoria ? -angle : 0f, reflected: true);
    }

    private static void DrawReflBody(
        Node2D canvas, Character ch, Vector2 pos, float hoY,
        int heading, GameData data, GrhAnimator animator)
    {
        if (ch.Body <= 0 || ch.Body >= data.Bodies.Length) return;
        int bodyGrh = data.Bodies[ch.Body].Walk[heading];
        if (bodyGrh <= 0) return;
        int frame = ch.Moving ? (int)ch.WalkFrame : 0;
        // Short races clamp to 36px so body reflection doesn't clip under feet
        float absHo = -hoY > 1f ? -hoY : 1f;
        float dist = absHo < 36f ? 36f : absHo;
        // Per-body-type Y adjust so reflection aligns with feet edge
        if (ch.Head <= 0) // no head: flying mount, boat
            dist += 20f;
        else if (absHo >= 28f) // tall races (human/elf/dark elf)
            dist += 1f;
        else // short races (enano/gnomo)
            dist += 14f;
        DrawGrhFlippedY(canvas, bodyGrh, frame, pos.X, pos.Y + dist, 75f / 255f, data);
    }

    private static void DrawReflHead(
        Node2D canvas, Character ch, Vector2 pos, float hoX, float hoY,
        int heading, GameData data)
    {
        if (ch.Head <= 0 || ch.Head >= data.Heads.Length) return;
        int headGrh = data.Heads[ch.Head].Head[heading];
        if (headGrh <= 0) return;
        // Scale gap proportionally to body height (VB6 constants tuned for |hoY|=30)
        float absHo = -hoY > 1f ? -hoY : 1f;
        float yOff = ch.Dead ? (20f * absHo / 30f) : (7f * absHo / 30f);
        yOff += 5f; // global +5 offset so reflection clears character feet
        // Mounted: head needs extra distance since mount body is taller, and X+1
        float xOff = 0f;
        if (ch.Mounted) { yOff += 18f; xOff = 1f; }
        DrawGrhFlippedY(canvas, headGrh, 0, pos.X - hoX + xOff, pos.Y - hoY + yOff, 75f / 255f, data);
    }

    private static void DrawReflHelmet(
        Node2D canvas, Character ch, Vector2 pos, float hoX, float hoY,
        int heading, GameData data)
    {
        if (ch.CascoAnim <= 0 || ch.CascoAnim >= data.Cascos.Length) return;
        int hGrh = data.Cascos[ch.CascoAnim].Head[heading];
        if (hGrh <= 0) return;
        // Scale gap proportionally to body height (VB6 constant 14 tuned for |hoY|=30)
        float absHo = -hoY > 1f ? -hoY : 1f;
        float helmetGap = 14f * absHo / 30f - 3f;
        helmetGap += 2f; // global offset so reflection clears character feet (5-3 adjust)
        // Mounted: helmet needs extra distance since mount body is taller, and X+1
        float xOff = 0f;
        if (ch.Mounted) { helmetGap += 18f; xOff = 1f; }
        DrawGrhFlippedY(canvas, hGrh, 0, pos.X - hoX + 1 + xOff, pos.Y - hoY + helmetGap, 150f / 255f, data);
    }

    private static void DrawReflWeapon(
        Node2D canvas, Character ch, Vector2 pos, float hoY,
        int heading, GameData data, GrhAnimator animator)
    {
        if (ch.WeaponAnim <= 0 || ch.WeaponAnim >= data.Weapons.Length) return;
        int weapGrh = data.Weapons[ch.WeaponAnim].Walk[heading];
        if (weapGrh <= 0) return;
        // Weapon reflection Y: absHo base + 7 scaled gap (hand height)
        float absHo = -hoY > 1f ? -hoY : 1f;
        float weapY = pos.Y + absHo + 7f * absHo / 30f + 5f;
        DrawGrhFlippedY(canvas, weapGrh, ch.Moving ? (int)ch.WalkFrame : 0,
            pos.X, weapY, 75f / 255f, data);
    }

    private static void DrawReflShield(
        Node2D canvas, Character ch, Vector2 pos, float hoY,
        int heading, GameData data, GrhAnimator animator)
    {
        if (ch.ShieldAnim <= 0 || ch.ShieldAnim >= data.Shields.Length) return;
        int shldGrh = data.Shields[ch.ShieldAnim].Walk[heading];
        if (shldGrh <= 0) return;
        // Same scaling as weapon but 2px closer
        float absHo = -hoY > 1f ? -hoY : 1f;
        float shldY = pos.Y + absHo + 5f * absHo / 30f + 5f;
        DrawGrhFlippedY(canvas, shldGrh, ch.Moving ? (int)ch.WalkFrame : 0,
            pos.X, shldY, 90f / 255f, data);
    }

    /// <summary>
    /// Draw a GRH at (x, y) with center=true and Y-flipped (Invert_y).
    /// Uses DrawPolygon with flipped UV coordinates — exactly how VB6 does it.
    /// The sprite position stays identical to an unflipped draw, only the
    /// texture is rendered upside-down within the same rectangle.
    /// </summary>
    private static void DrawGrhFlippedY(
        Node2D canvas, int grhIndex, int frame,
        float x, float y, float alpha, GameData data)
    {
        var resolved = data.ResolveGrh(grhIndex, frame);
        if (resolved == null || resolved.FileNum <= 0) return;

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

        // Center=true (same as DrawGrh)
        float drawX = x;
        float drawY = y;
        if (resolved.TileWidth != 1f && resolved.TileWidth > 0)
            drawX -= (int)(resolved.TileWidth * (TileSize / 2)) - TileSize / 2;
        if (resolved.TileHeight != 1f && resolved.TileHeight > 0)
            drawY -= (int)(resolved.TileHeight * TileSize) - TileSize;

        // VB6 Invert_y: swap src.top/src.bottom in UV space.
        // Vertex positions stay the same — only texture mapping is flipped.
        float u0 = (float)sx / texW;
        float v0 = (float)sy / texH;
        float u1 = (float)(sx + pw) / texW;
        float v1 = (float)(sy + ph) / texH;

        Vector2[] verts = {
            new(drawX, drawY),            // top-left
            new(drawX + pw, drawY),       // top-right
            new(drawX + pw, drawY + ph),  // bottom-right
            new(drawX, drawY + ph),       // bottom-left
        };

        // Flipped UVs: screen top → texture bottom, screen bottom → texture top
        Vector2[] uvs = {
            new(u0, v1),  // top-left vertex → bottom-left of texture
            new(u1, v1),  // top-right vertex → bottom-right of texture
            new(u1, v0),  // bottom-right vertex → top-right of texture
            new(u0, v0),  // bottom-left vertex → top-left of texture
        };

        Color color = new(1, 1, 1, alpha);
        Color[] colors = { color, color, color, color };
        canvas.DrawPolygon(verts, colors, uvs, texture);
    }

    private static void DrawBody(
        Node2D canvas, Character ch, Vector2 pos, int heading,
        GameData data, GrhAnimator animator, GameState? state = null)
    {
        if (ch.Body <= 0 || ch.Body >= data.Bodies.Length) return;
        var body = data.Bodies[ch.Body];
        if (body.Walk[heading] == 0) return;

        int bodyGrh = body.Walk[heading];
        int frame = ch.Moving ? (int)ch.WalkFrame : 0;

        // VB6: dead alpha = TransparenciaBody + 45 (pulsing 45-145), alive = 255
        // Only pulse when DeadCharTransparency config is enabled
        byte alpha = (ch.Dead && (state?.Config?.DeadCharTransparency ?? true))
            ? (byte)(ch.TransparenciaBody + 45) : (byte)255;
        DrawGrh(canvas, data, bodyGrh, frame, pos, true,
                alpha < 255 ? new Color(1, 1, 1, alpha / 255f) : Colors.White);
    }

    private static void DrawHead(
        Node2D canvas, Character ch, Vector2 bodyPos, Vector2 headOffset,
        int heading, GameData data, GameState? state = null)
    {
        if (ch.Head <= 0 || ch.Head >= data.Heads.Length) return;
        var head = data.Heads[ch.Head];
        if (head.Head[heading] == 0) return;

        // VB6: heading 1 gets X-1 adjustment; mounted gets X+1
        float xAdj = heading == 1 ? -1f : 0f;
        if (ch.Mounted) xAdj += 1f;
        Vector2 headPos = bodyPos + new Vector2(headOffset.X + xAdj, headOffset.Y + 1);

        // VB6: dead alpha = TransparenciaBody + 45 (pulsing 45-145)
        // Only pulse when DeadCharTransparency config is enabled
        byte alpha = (ch.Dead && (state?.Config?.DeadCharTransparency ?? true))
            ? (byte)(ch.TransparenciaBody + 45) : (byte)255;
        DrawGrh(canvas, data, head.Head[heading], 0, headPos, true,
                alpha < 255 ? new Color(1, 1, 1, alpha / 255f) : Colors.White);
    }

    private static void DrawHelmet(
        Node2D canvas, Character ch, Vector2 bodyPos, Vector2 headOffset,
        int heading, GameData data)
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

        DrawGrh(canvas, data, grhIdx, 0, helmetPos, true);
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

        // VB6: dibEsc → Draw_Grh at PixelOffsetX + HeadOffset.X, PixelOffsetY + HeadOffset.Y + 38, center=1
        int frame = ch.Moving ? (int)ch.WalkFrame : 0;
        Vector2 shieldPos = bodyPos + new Vector2(headOffset.X, headOffset.Y + 38);
        DrawGrh(canvas, data, grhIndex, frame, shieldPos, true);
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
        GameData data)
    {
        if (data.Auras == null || data.Auras.Length <= 1) return;

        CollectSingleAura(worldRenderer, pos, headOffset, data, ch.AuraIndexA, ref ch.AuraAngleA);
        CollectSingleAura(worldRenderer, pos, headOffset, data, ch.AuraIndexW, ref ch.AuraAngleW);
        CollectSingleAura(worldRenderer, pos, headOffset, data, ch.AuraIndexE, ref ch.AuraAngleE);
        CollectSingleAura(worldRenderer, pos, headOffset, data, ch.AuraIndexR, ref ch.AuraAngleR);
        CollectSingleAura(worldRenderer, pos, headOffset, data, ch.AuraIndexC, ref ch.AuraAngleC);
        CollectSingleAura(worldRenderer, pos, headOffset, data, ch.NpcAura, ref ch.NpcAuraAngle);
    }

    private static void CollectSingleAura(
        WorldRenderer worldRenderer, Vector2 pos, Vector2 headOffset,
        GameData data, int auraIndex, ref float angle)
    {
        if (auraIndex <= 0 || auraIndex >= data.Auras.Length) return;

        var aura = data.Auras[auraIndex];
        if (aura.GrhIndex <= 0) return;

        // VB6: Giratoria — angle += 0.004 per frame, wraps at 180
        if (aura.Giratoria)
        {
            angle += 0.004f;
            if (angle >= 180f) angle = 0f;
        }

        // VB6 position: PixelOffsetX + HeadOffset.X, HeadOffset.Y + PixelOffsetY + 72 - offset
        float auraX = pos.X + headOffset.X;
        float auraY = pos.Y + headOffset.Y + 72 - aura.Offset;

        // VB6 color: static R,G,B (simplified — full pulsing is cosmetic polish)
        Color color = new Color(aura.R / 255f, aura.G / 255f, aura.B / 255f, 1f);

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
                                    Vector2 headOffset, GameData data, float deltaMs)
    {
        if (string.IsNullOrEmpty(ch.DialogText)) return;

        var font = data.Fonts[1]; // font1 for dialog
        if (font == null) return;

        long now = System.Environment.TickCount64;
        long elapsed = now - ch.DialogStartMs;

        // Delta-time factor: VB6 ran at ~60fps → 16.67ms per frame.
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

        // VB6: check lifetime → set Tiempito
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
