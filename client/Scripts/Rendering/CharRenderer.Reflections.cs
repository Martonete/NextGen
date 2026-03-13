using System;
using Godot;
using ArgentumNextgen.Data;
using ArgentumNextgen.Game;

namespace ArgentumNextgen.Rendering;

/// <summary>
/// CharRenderer partial: water reflections and reflected aura collection.
/// </summary>
public static partial class CharRenderer
{
    /// <summary>
    /// Water reflection: flip the canvas Y around the character's feet, then draw
    /// using the exact same functions as the normal character. No hardcoded offsets.
    /// DrawSetTransform(scale.Y=-1) mirrors all subsequent draw calls automatically.
    /// </summary>
    public static void DrawReflection(
        Node2D canvas, Character ch, Vector2 pos, Vector2 headOffset,
        int heading, GameData data, GrhAnimator animator)
    {
        // Mirror axis: character's feet (sprite bottom = pos.Y + TileSize).
        // Tall sprites (mounts) have transparent padding at the bottom,
        // so we pull the mirror line up proportionally to the extra height.
        float mirrorAdj = 0f;
        if (ch.Mounted)
        {
            // Resolve body GRH to get actual pixel height
            if (ch.Body > 0 && ch.Body < data.Bodies.Length)
            {
                var body = data.Bodies[ch.Body];
                int bodyGrh = (heading >= 1 && heading <= 4) ? body.Walk[heading] : 0;
                if (bodyGrh > 0)
                {
                    var resolved = data.ResolveGrh(bodyGrh, 0);
                    if (resolved != null && resolved.PixelHeight > TileSize)
                    {
                        // The taller the sprite, the more transparent padding at bottom.
                        // Pull mirror up by ~15% of the extra height.
                        float extraH = resolved.PixelHeight - TileSize;
                        mirrorAdj = -(extraH * 0.15f);
                    }
                }
            }
        }
        else if (ch.Head <= 0) // no head: boat
            mirrorAdj = -2f;
        else
        {
            float absHo = -headOffset.Y > 1f ? -headOffset.Y : 1f;
            if (absHo >= 28f) // tall races (human/elf/dark elf)
                mirrorAdj = -2f;
            // short races (bajos): 0 — already good
        }
        float mirrorY = pos.Y + TileSize - 2f + mirrorAdj;

        // Set Y-flip transform: any draw at Y appears at (2*mirrorY - Y)
        canvas.DrawSetTransform(new Vector2(0f, mirrorY * 2f), 0f, new Vector2(1f, -1f));

        // Reflection alpha: body/head most transparent, equipment more visible, helmet most opaque
        Color reflColor = new Color(1f, 1f, 1f, 0.30f);
        Color equipColor = new Color(1f, 1f, 1f, 112f / 255f);
        Color helmetColor = new Color(1f, 1f, 1f, 0.50f);

        DrawCharParts(canvas, ch, pos, headOffset, heading, data, animator,
                      null, reflColor, equipColor, helmetColor);

        // Reset transform
        canvas.DrawSetTransform(Vector2.Zero, 0f, Vector2.One);
    }

    /// <summary>
    /// Draw reflected FX overlays (up to 3 simultaneous) on water.
    /// Uses the same Y-flip mirror as DrawReflection. Called from WorldRenderer PASS 1.5.
    /// Does NOT advance frame counters — those are advanced in DrawFx/DrawCharacter.
    /// </summary>
    public static void DrawReflectionFx(
        Node2D canvas, Character ch, Vector2 pos, Vector2 headOffset,
        int heading, GameData data, GrhAnimator animator)
    {
        // Check if there's anything to draw
        bool hasAnyFx = false;
        for (int i = 0; i < 3; i++)
        {
            if (ch.ActiveFxSlots[i] > 0) { hasAnyFx = true; break; }
        }
        if (!hasAnyFx) return;

        // Compute mirrorY — same logic as DrawReflection
        float mirrorAdj = 0f;
        if (ch.Mounted)
        {
            if (ch.Body > 0 && ch.Body < data.Bodies.Length)
            {
                var body = data.Bodies[ch.Body];
                int bodyGrh = (heading >= 1 && heading <= 4) ? body.Walk[heading] : 0;
                if (bodyGrh > 0)
                {
                    var resolved = data.ResolveGrh(bodyGrh, 0);
                    if (resolved != null && resolved.PixelHeight > TileSize)
                    {
                        float extraH = resolved.PixelHeight - TileSize;
                        mirrorAdj = -(extraH * 0.15f);
                    }
                }
            }
        }
        else if (ch.Head <= 0)
            mirrorAdj = -2f;
        else
        {
            float absHo = -headOffset.Y > 1f ? -headOffset.Y : 1f;
            if (absHo >= 28f)
                mirrorAdj = -2f;
        }
        float mirrorY = pos.Y + TileSize - 2f + mirrorAdj;

        // Reflection alpha for FX (same as equipment reflection)
        Color fxReflColor = new Color(1f, 1f, 1f, 112f / 255f);

        // Set Y-flip transform
        canvas.DrawSetTransform(new Vector2(0f, mirrorY * 2f), 0f, new Vector2(1f, -1f));

        // FX slot reflections (read-only — no frame counter changes)
        for (int i = 0; i < 3; i++)
        {
            int fxIdx = ch.ActiveFxSlots[i];
            if (fxIdx <= 0 || fxIdx >= data.Fxs.Length) continue;

            var fx = data.Fxs[fxIdx];
            if (fx.Animacion <= 0) continue;

            int grhIndex = fx.Animacion;
            if (grhIndex <= 0 || grhIndex >= data.Grhs.Length) continue;
            var grh = data.Grhs[grhIndex];

            int frame;
            if (grh.NumFrames <= 1)
            {
                frame = 0;
            }
            else
            {
                // Read current frame from the slot counter (already advanced by DrawFx)
                frame = (int)ch.FxFrameCounter[i];
                if (frame >= grh.NumFrames) frame = grh.NumFrames - 1;
                if (frame < 0) frame = 0;
            }

            Vector2 fxPos = pos + new Vector2(fx.OffsetX, fx.OffsetY);
            DrawGrh(canvas, data, grhIndex, frame, fxPos, true, fxReflColor);
        }

        // Reset transform
        canvas.DrawSetTransform(Vector2.Zero, 0f, Vector2.One);
    }

    /// <summary>
    /// Queue reflected auras to the additive layer (auras need special additive blending
    /// and can't use the DrawSetTransform flip approach).
    /// </summary>
    public static void CollectReflAuraDraws(
        WorldRenderer worldRenderer, Character ch, Vector2 pos, Vector2 headOffset,
        GameData data, double globalTimeMs)
    {
        if (data.Auras == null || data.Auras.Length <= 1) return;

        // Compute mirrorAdj matching DrawReflection so aura aligns with body
        float mirrorAdj = 0f;
        if (ch.Mounted)
        {
            if (ch.Body > 0 && ch.Body < data.Bodies.Length)
            {
                var body = data.Bodies[ch.Body];
                int heading = ch.Heading >= 1 && ch.Heading <= 4 ? ch.Heading : 3;
                int bodyGrh = body.Walk[heading];
                if (bodyGrh > 0)
                {
                    var resolved = data.ResolveGrh(bodyGrh, 0);
                    if (resolved != null && resolved.PixelHeight > TileSize)
                        mirrorAdj = -((resolved.PixelHeight - TileSize) * 0.15f);
                }
            }
        }
        else
            mirrorAdj = -4f; // all races: pull mirror 4px closer to body
        float mirrorY = pos.Y + TileSize - 2f + mirrorAdj;

        CollectSingleReflAura(worldRenderer, pos, headOffset, data, ch.AuraIndexA, ch.AuraAngleA, mirrorY, globalTimeMs);
        CollectSingleReflAura(worldRenderer, pos, headOffset, data, ch.AuraIndexW, ch.AuraAngleW, mirrorY, globalTimeMs);
        CollectSingleReflAura(worldRenderer, pos, headOffset, data, ch.AuraIndexE, ch.AuraAngleE, mirrorY, globalTimeMs);
        CollectSingleReflAura(worldRenderer, pos, headOffset, data, ch.AuraIndexR, ch.AuraAngleR, mirrorY, globalTimeMs);
        CollectSingleReflAura(worldRenderer, pos, headOffset, data, ch.AuraIndexC, ch.AuraAngleC, mirrorY, globalTimeMs);
        CollectSingleReflAura(worldRenderer, pos, headOffset, data, ch.NpcAura, ch.NpcAuraAngle, mirrorY, globalTimeMs);
    }

    private static void CollectSingleReflAura(
        WorldRenderer worldRenderer, Vector2 pos, Vector2 headOffset,
        GameData data, int auraIndex, float angle, float mirrorY, double globalTimeMs)
    {
        if (auraIndex <= 0 || auraIndex >= data.Auras.Length) return;
        var aura = data.Auras[auraIndex];
        if (aura.GrhIndex <= 0) return;

        int grhIndex = aura.GrhIndex;
        int frame = 0;
        if (grhIndex > 0 && grhIndex < data.Grhs.Length)
        {
            var grh = data.Grhs[grhIndex];
            if (grh.NumFrames > 1)
            {
                float speed = grh.Speed > 0 ? grh.Speed : 100f;
                frame = (int)(globalTimeMs / speed % grh.NumFrames);
            }
        }

        // Pass the NORMAL aura position (with Offset) + mirrorAdj-adjusted mirrorY.
        // The DrawSetTransform mirror naturally reverses the Offset direction:
        //   Offset=0  -> closest to reflected body
        //   Offset=30 -> further from reflected body
        // mirrorAdj ensures the aura's mirror line matches the body's.
        float auraX = pos.X + headOffset.X;
        float auraY = pos.Y + headOffset.Y + 72 - aura.Offset;
        Color color = new Color(aura.R / 255f, aura.G / 255f, aura.B / 255f, 0.25f);

        worldRenderer.QueueReflAuraDraw(grhIndex, frame, new Vector2(auraX, auraY), color,
                                         aura.Giratoria ? angle : 0f, mirrorY);
    }
}
