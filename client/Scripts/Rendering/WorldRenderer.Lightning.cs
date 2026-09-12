using System;
using Godot;

namespace ArgentumNextgen.Rendering;

public partial class WorldRenderer
{
    private const float LightningDuration = 0.70f;

    // Real lightning is one tall, angular channel that holds its shape while it
    // flickers; it does not wiggle. Three strokes share the flash: the return stroke
    // and two dimmer re-strikes, each with its own channel geometry.
    private const float LightningMainStroke = 0.05f;
    private const float LightningRestrike1 = 0.21f;
    private const float LightningRestrike2 = 0.36f;
    private const float LightningChannelHeight = 150f;   // px above the head
    private const int LightningSegments = 11;

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
    /// Relámpago (HECHIZO12 / FX 102): a tall sky-to-target strike. Faint stepped leader,
    /// blinding return stroke with ground flash and shock ring, two re-strikes with fresh
    /// channels, then residual arcs crawling over the target. Analytic, no allocations.
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

            DrawLightningShape(canvas, feet, age, ch.FovAlpha, _state.Config.PerformanceLevel);
        }
    }

    // Deterministic noise in [-1, 1]; the channel must be identical every frame of a stroke.
    private static float LightningNoise(int seed, int i)
    {
        float v = MathF.Sin(seed * 91.7f + i * 47.3f + 12.9f) * 43758.5453f;
        return (v - MathF.Floor(v)) * 2f - 1f;
    }

    private static float LightningPulse(float age, float start, float peak, float decay)
        => age < start ? 0f : peak * MathF.Exp(-(age - start) * decay);

    internal void DrawLightningShape(CanvasItem canvas, Vector2 feet, float age, float alpha, int performanceLevel)
    {
        Vector2 chest = feet + new Vector2(0, -20);
        Vector2 head = feet + new Vector2(0, -38);
        float top = head.Y - LightningChannelHeight;

        // ── Stroke envelope ──
        int stroke;
        float power;
        bool leader = age < LightningMainStroke;
        if (leader)
        {
            // Stepped leader: thin, dim, flickering, only the upper part of the channel.
            stroke = 0;
            power = 0.22f + 0.12f * MathF.Sin(age * 260f);
        }
        else if (age < LightningRestrike1)
        {
            stroke = 0;
            power = LightningPulse(age, LightningMainStroke, 1.0f, 13f);
        }
        else if (age < LightningRestrike2)
        {
            stroke = 1;
            power = LightningPulse(age, LightningRestrike1, 0.75f, 15f);
        }
        else
        {
            stroke = 2;
            power = LightningPulse(age, LightningRestrike2, 0.55f, 15f);
        }
        power *= alpha;

        // Brightest instant of the whole flash, used for the ground flash and shock ring.
        float mainFlash = LightningPulse(age, LightningMainStroke, 1f, 11f) * alpha;

        // ── 1. Channel ──
        if (power > 0.015f)
        {
            float leaderReach = leader ? Math.Clamp(age / LightningMainStroke, 0.15f, 1f) : 1f;
            int seed = stroke * 7 + 3;

            var halo = new Color(0.12f, 0.42f, 1.0f, power * 0.40f);
            var sheath = new Color(0.55f, 0.85f, 1.0f, power * 0.80f);
            var core = new Color(0.96f, 0.99f, 1.0f, power);
            float haloW = leader ? 4f : 9f, sheathW = leader ? 1.8f : 3.6f, coreW = leader ? 0.9f : 1.7f;

            // Sky origin sits slightly off-centre so the strike reads as coming from the storm, not the head.
            float originX = chest.X + LightningNoise(seed, 0) * 34f;
            Vector2 prev = new(originX, top);
            DrawReactiveGlowSprite(canvas, prev, leader ? 10f : 22f, new Color(0.35f, 0.7f, 1f, power * 0.45f));

            int reach = (int)MathF.Ceiling(LightningSegments * leaderReach);
            for (int s = 1; s <= reach; s++)
            {
                float frac = (float)s / LightningSegments;
                float y = Mathf.Lerp(top, chest.Y, frac);
                // Angular kinks with amplitude that shrinks toward the target so it lands on the chest.
                float amp = 26f * (1f - frac) + 4f;
                float x = s == LightningSegments
                    ? chest.X
                    : Mathf.Lerp(originX, chest.X, frac) + LightningNoise(seed, s) * amp;
                Vector2 curr = new(x, y);

                canvas.DrawLine(prev, curr, halo, haloW, true);
                canvas.DrawLine(prev, curr, sheath, sheathW, true);
                canvas.DrawLine(prev, curr, core, coreW, true);

                // Branches: leave the channel at a kink and die out in the air.
                bool branchHere = !leader && (s == 3 || s == 6 || (s == 8 && performanceLevel >= 2));
                if (branchHere)
                {
                    float dir = LightningNoise(seed, 20 + s) >= 0 ? 1f : -1f;
                    Vector2 bPrev = curr;
                    float bPower = power * 0.55f;
                    int bSegs = 3;
                    for (int b = 1; b <= bSegs; b++)
                    {
                        float bx = bPrev.X + dir * (9f + 5f * LightningNoise(seed, 40 + s * 3 + b));
                        float by = bPrev.Y + 8f + 6f * MathF.Abs(LightningNoise(seed, 60 + s * 3 + b));
                        Vector2 bCurr = new(bx, by);
                        float taper = 1f - (float)(b - 1) / bSegs;
                        canvas.DrawLine(bPrev, bCurr, halo with { A = halo.A * 0.6f * taper }, 5f * taper + 1f, true);
                        canvas.DrawLine(bPrev, bCurr, new Color(0.75f, 0.92f, 1f, bPower * taper), 1.2f, true);
                        bPrev = bCurr;
                    }
                }

                prev = curr;
            }

            if (!leader)
            {
                // Contact point: white-hot bloom that saturates the torso.
                DrawReactiveGlowSprite(canvas, chest, 30f, new Color(0.45f, 0.80f, 1.0f, power * 0.75f));
                DrawReactiveGlowSprite(canvas, chest, 15f, new Color(1f, 1f, 1f, power * 0.95f));
            }
        }

        // ── 2. Ground flash and shock ring (main stroke only) ──
        if (mainFlash > 0.01f)
        {
            DrawReactiveGlowSprite(canvas, feet, 64f, new Color(0.25f, 0.55f, 1.0f, mainFlash * 0.45f), 0.45f);
            DrawReactiveGlowSprite(canvas, feet, 28f, new Color(0.85f, 0.95f, 1.0f, mainFlash * 0.8f), 0.45f);
        }
        float ringAge = age - LightningMainStroke;
        if (ringAge >= 0 && ringAge < 0.30f)
        {
            float rp = ringAge / 0.30f;
            float radius = 8f + 30f * (1f - (1f - rp) * (1f - rp));
            float ringAlpha = (1f - rp) * alpha;
            DrawReactiveRing(canvas, feet, radius, 0.38f, new Color(0.40f, 0.82f, 1.0f, ringAlpha * 0.7f), 2.2f);
            DrawReactiveRing(canvas, feet, radius * 0.8f, 0.38f, new Color(0.9f, 0.97f, 1.0f, ringAlpha * 0.45f), 1f);
        }

        // ── 3. Sparks thrown from the contact point ──
        float sparkAge = age - LightningMainStroke;
        if (sparkAge >= 0)
        {
            int sparkCount = performanceLevel < 2 ? 8 : performanceLevel == 2 ? 14 : 20;
            float sparkFade = Math.Max(0, 1f - sparkAge / 0.36f) * alpha;
            for (int i = 0; i < sparkCount && sparkFade > 0.01f; i++)
            {
                float seedA = (i * 0.618033989f) % 1f;
                float seedB = (i * 0.324717957f) % 1f;
                float angle = MathF.PI * (0.9f + seedA * 1.2f);          // mostly upward and sideways
                float speed = 45f + seedB * 95f;
                Vector2 vel = new(MathF.Cos(angle) * speed, MathF.Sin(angle) * speed);
                Vector2 pos = chest + vel * sparkAge + new Vector2(0, 260f * sparkAge * sparkAge);
                Vector2 tail = (vel + new Vector2(0, 520f * sparkAge)) * (0.018f + seedA * 0.02f);
                canvas.DrawLine(pos, pos - tail, new Color(0.85f, 0.96f, 1f, sparkFade), 1.2f, true);
                DrawReactiveGlowSprite(canvas, pos, 3f + seedB * 2f, new Color(0.3f, 0.65f, 1f, sparkFade * 0.35f));
            }
        }

        // ── 4. Residual arcs crawling over the target while the flash dies ──
        if (age > 0.12f)
        {
            float residual = MathF.Min(1f, (age - 0.12f) / 0.1f) * Math.Max(0, 1f - (age - 0.12f) / (LightningDuration - 0.12f)) * alpha;
            int phase = (int)(age / 0.06f);
            float flick = 0.6f + 0.4f * MathF.Exp(-(age % 0.06f) * 50f);
            float arcPower = residual * flick;
            if (arcPower > 0.02f)
            {
                DrawReactiveGlowSprite(canvas, chest, 20f, new Color(0.15f, 0.45f, 1f, arcPower * 0.35f));
                int arcs = performanceLevel < 2 ? 2 : 3;
                for (int a = 0; a < arcs; a++)
                {
                    float side = (a + phase) % 2 == 0 ? -1f : 1f;
                    float yStart = -34f + 6f * LightningNoise(phase, a);
                    Vector2 p0 = feet + new Vector2(side * 4f, yStart);
                    Vector2 p1 = feet + new Vector2(side * (10f + 4f * LightningNoise(phase, 10 + a)), yStart + 10f);
                    Vector2 p2 = feet + new Vector2(side * (6f + 5f * LightningNoise(phase, 20 + a)), yStart + 20f);
                    Vector2 p3 = feet + new Vector2(side * 3f, yStart + 28f);
                    canvas.DrawLine(p0, p1, new Color(0.1f, 0.4f, 1f, arcPower * 0.35f), 3f, true);
                    canvas.DrawLine(p0, p1, new Color(0.7f, 0.92f, 1f, arcPower), 1f, true);
                    canvas.DrawLine(p1, p2, new Color(0.1f, 0.4f, 1f, arcPower * 0.35f), 3f, true);
                    canvas.DrawLine(p1, p2, new Color(0.7f, 0.92f, 1f, arcPower), 1f, true);
                    canvas.DrawLine(p2, p3, new Color(0.1f, 0.4f, 1f, arcPower * 0.3f), 2.5f, true);
                    canvas.DrawLine(p2, p3, new Color(0.7f, 0.92f, 1f, arcPower * 0.8f), 0.9f, true);
                }
            }
        }
    }
}
