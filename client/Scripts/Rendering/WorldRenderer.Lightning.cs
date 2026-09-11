using System;
using Godot;

namespace ArgentumNextgen.Rendering;

public partial class WorldRenderer
{
    private const float LightningDuration = 0.44f;

    private void UpdateLightningEffects(float delta)
    {
        if (_state == null) return;
        foreach (var ch in _state.Characters.Values)
        {
            if (ch.LightningTime < 0) continue;
            if (_state.MapData != _reactiveMap || ch.Invisible || delta > 0.5f)
            {
                ch.LightningTime = -1;
                continue;
            }
            if (!_state.Paused && float.IsFinite(delta))
                ch.LightningTime += Math.Max(0, delta);
            if (ch.LightningTime >= LightningDuration)
                ch.LightningTime = -1;
        }
    }

    /// <summary>
    /// Relámpago (HECHIZO12 / FX 102): A compact, high-voltage lightning strike descending
    /// from just above the character's head, with a braided dual-filament plasma core,
    /// soft electric glow nodes, lateral crackling tendrils, and a bright ground impact corona.
    /// Analytic, zero per-frame heap allocations.
    /// </summary>
    private void DrawLightningEffects(CanvasItem canvas)
    {
        if (_state == null || !_state.Config.ShowReactiveEffects || !_state.Config.ShowParticles) return;

        foreach (var ch in _state.Characters.Values)
        {
            float age = ch.LightningTime;
            if (age < 0 || ch.Invisible || ch.FovAlpha <= 0.1f) continue;

            Vector2 feet = ReactiveToScreen(new System.Numerics.Vector2(
                ch.PosX * TileSize + ch.MoveOffsetX + 16,
                ch.PosY * TileSize + ch.MoveOffsetY + 27));

            if (!ReactiveOnScreen(feet)) continue;

            float t = age / LightningDuration;
            float fade = (1 - t) * ch.FovAlpha;
            Vector2 chest = feet + new Vector2(0, -22);
            Vector2 head = feet + new Vector2(0, -38);

            // Strike phase: jitter shifts rapidly every 40ms for high-speed plasma crackle
            int phase = (int)(age / 0.040f);
            float crackle = 0.8f + 0.2f * MathF.Exp(-(age % 0.040f) * 60);
            float boltFade = Math.Max(0, 1 - age / 0.28f);
            float boltPower = boltFade * crackle * ch.FovAlpha;

            // Colors: deep sapphire halo, electric cyan body, and pure white-hot core
            var colorHalo = new Color(0.10f, 0.38f, 1.0f, boltPower * 0.45f);
            var colorCyan = new Color(0.35f, 0.88f, 1.0f, boltPower * 0.85f);
            var colorCore = new Color(0.95f, 0.99f, 1.0f, boltPower);

            // ── 1. Compact Lightning Bolt (Descending from ~75px above head) ──
            if (boltPower > 0.01f)
            {
                float strikeTopY = head.Y - 55f; // ~75px total height above chest
                int segments = 6;
                Vector2 prevMain = new(chest.X + MathF.Sin(phase * 17.3f) * 4f, strikeTopY);
                Vector2 prevStreamer = prevMain + new Vector2(MathF.Cos(phase * 11.2f) * 3f, 0);

                // Origin atmospheric burst
                DrawReactiveGlowSprite(canvas, prevMain, 14f, new Color(0.3f, 0.75f, 1.0f, boltPower * 0.6f));

                for (int s = 1; s <= segments; s++)
                {
                    float frac = (float)s / segments;
                    float currY = Mathf.Lerp(strikeTopY, chest.Y, frac);
                    float currX = chest.X;

                    if (s < segments)
                    {
                        float jitter = MathF.Sin(s * 14.17f + phase * 27.31f) * (8.5f * (1f - frac * 0.3f));
                        currX += jitter;
                    }

                    Vector2 currMain = new(currX, currY);
                    Vector2 currStreamer = currMain + new Vector2(MathF.Sin(s * 7.7f + phase * 13.1f) * 3.5f, 0);

                    // Multi-layer main filament: deep glow, bright cyan sheath, crisp white core
                    canvas.DrawLine(prevMain, currMain, colorHalo, 6.0f, true);
                    canvas.DrawLine(prevMain, currMain, colorCyan, 2.6f, true);
                    canvas.DrawLine(prevMain, currMain, colorCore, 1.2f, true);

                    // Braided secondary streamer (subtle twin plasma filament)
                    canvas.DrawLine(prevStreamer, currStreamer, colorHalo with { A = colorHalo.A * 0.5f }, 3.5f, true);
                    canvas.DrawLine(prevStreamer, currStreamer, colorCyan with { A = colorCyan.A * 0.7f }, 1.0f, true);

                    // Soft plasma node at elbow vertices
                    if (s == 2 || s == 4)
                    {
                        DrawReactiveGlowSprite(canvas, currMain, 9f, new Color(0.35f, 0.85f, 1.0f, boltPower * 0.45f));
                    }

                    // Lateral branching tendrils
                    if (s == 2 || (s == 4 && _state.Config.PerformanceLevel >= 2))
                    {
                        float forkSign = (s + phase) % 2 == 0 ? 1f : -1f;
                        float forkAngle = forkSign * (0.65f + 0.25f * MathF.Sin(s * 5.3f + phase));
                        Vector2 forkDir = new Vector2(MathF.Cos(forkAngle), MathF.Abs(MathF.Sin(forkAngle))).Normalized();
                        Vector2 forkTip = currMain + forkDir * (12f + 3f * MathF.Sin(phase * 7.1f));

                        float forkPower = boltPower * 0.6f;
                        canvas.DrawLine(currMain, forkTip, colorHalo, 3.5f, true);
                        canvas.DrawLine(currMain, forkTip, new Color(0.6f, 0.92f, 1.0f, forkPower), 1.0f, true);
                        DrawReactiveGlowSprite(canvas, forkTip, 5f, new Color(0.2f, 0.6f, 1.0f, forkPower * 0.4f));
                    }

                    prevMain = currMain;
                    prevStreamer = currStreamer;
                }

                // Impact flash at target head/chest
                DrawReactiveGlowSprite(canvas, chest, 24f, new Color(0.5f, 0.9f, 1.0f, boltPower * 0.85f));
                DrawReactiveGlowSprite(canvas, chest, 12f, new Color(1.0f, 1.0f, 1.0f, boltPower * 0.95f));

                // Crackling wrap-around arc hugging the body
                for (int a = 0; a < 2; a++)
                {
                    float side = a == 0 ? -1f : 1f;
                    Vector2 arcStart = chest + new Vector2(side * 3f, -4f);
                    Vector2 arcMid = chest + new Vector2(side * 10f, 8f + MathF.Sin(phase * 9f + a) * 3f);
                    Vector2 arcEnd = feet + new Vector2(side * 6f, -3f);

                    float bodyArcPower = boltPower * 0.7f;
                    canvas.DrawLine(arcStart, arcMid, colorHalo, 3.0f, true);
                    canvas.DrawLine(arcStart, arcMid, new Color(0.65f, 0.92f, 1.0f, bodyArcPower), 1.1f, true);
                    canvas.DrawLine(arcMid, arcEnd, colorHalo, 2.5f, true);
                    canvas.DrawLine(arcMid, arcEnd, new Color(0.65f, 0.92f, 1.0f, bodyArcPower * 0.8f), 0.9f, true);
                }
            }

            // ── 2. Ground Impact Corona & Shockwave ──
            float flash = Math.Max(0, 1 - age / 0.12f) * ch.FovAlpha;
            if (flash > 0.01f)
            {
                DrawReactiveGlowSprite(canvas, feet, 26f, new Color(0.20f, 0.60f, 1.0f, flash * 0.75f), 0.5f);
                DrawReactiveGlowSprite(canvas, feet, 14f, new Color(0.85f, 0.96f, 1.0f, flash * 0.90f), 0.5f);
            }

            // Expanding ground ionization ellipse
            float ringProgress = Math.Min(1, age / 0.20f);
            float ringRadius = 11f + 7f * ringProgress;
            float ringAlpha = Math.Max(0, 1 - age / 0.24f) * ch.FovAlpha;
            if (ringAlpha > 0.01f)
            {
                DrawReactiveRing(canvas, feet, ringRadius, 0.36f,
                    new Color(0.35f, 0.82f, 1.0f, ringAlpha * 0.60f), 1.5f);
                DrawReactiveRing(canvas, feet, ringRadius - 2f, 0.36f,
                    new Color(0.80f, 0.95f, 1.0f, ringAlpha * 0.35f), 0.8f);
            }

            // ── 3. Atmospheric Discharge Sparks ──
            int sparkCount = _state.Config.PerformanceLevel < 2 ? 6 : _state.Config.PerformanceLevel == 2 ? 12 : 18;
            for (int i = 0; i < sparkCount; i++)
            {
                float seed = (i * 0.618033989f) % 1;
                float angle = -MathF.PI * 0.5f + (seed - 0.5f) * MathF.PI * 0.90f;
                float speed = 65f + seed * 45f;
                float travel = (1 - MathF.Exp(-age * 6.0f)) / 6.0f;

                Vector2 sparkPos = feet + new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * (speed * travel)
                                  + new Vector2(0, 16f * age * age);
                Vector2 sparkTail = new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * (speed * MathF.Exp(-age * 6.0f) * 0.024f);

                Color sparkColor = new(0.80f, 0.95f, 1.0f, fade);
                canvas.DrawLine(sparkPos, sparkPos - sparkTail, sparkColor, 1.1f, true);
                DrawReactiveGlowSprite(canvas, sparkPos, 3.5f + seed * 2f,
                    new Color(0.20f, 0.60f, 1.0f, fade * 0.30f));
            }
        }
    }
}
