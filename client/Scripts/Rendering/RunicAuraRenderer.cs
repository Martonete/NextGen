using System;
using Godot;
using ArgentumNextgen.Data;

namespace ArgentumNextgen.Rendering;

/// <summary>Small data-authored ornament loops. No emitter nodes, textures or per-frame arrays.</summary>
public static class RunicAuraRenderer
{
    private static readonly Vector2[] Curve = new Vector2[25];
    public static void Draw(CanvasItem canvas, AuraData aura, Vector2 feet, double timeMs, float alpha = 1, bool? front = null)
    {
        float phase = aura.ProceduralStyle == 5 ? 0f : (float)(timeMs % aura.CycleMs / aura.CycleMs) * MathF.Tau;
        float strength = alpha * aura.Opacity / 100f;
        Color primary = new(aura.R / 255f, aura.G / 255f, aura.B / 255f, strength);
        Color accent = new(aura.RojoF / 255f, aura.VerdeF / 255f, aura.AzulF / 255f, strength * .85f);
        float radius = aura.Radius;
        Vector2 center = feet + new Vector2(0, -aura.Height * .45f);
        if (aura.ProceduralStyle == 6)
        {
            DrawWisps(canvas, aura, feet, phase, primary, accent, front);
            return;
        }
        if (aura.ProceduralStyle == 1)
        {
            for (int ring = 0; ring < 2; ring++)
            {
                for (int i = 0; i < Curve.Length; i++)
                {
                    float a = i * MathF.Tau / (Curve.Length - 1);
                    Curve[i] = center + new Vector2(MathF.Cos(a) * radius,
                        MathF.Sin(a) * radius * .3f + MathF.Cos(a) * (ring == 0 ? 8 : -8));
                }
                DrawCurve(canvas, ring == 0 ? primary : accent, 1, front);
            }
        }
        else if (aura.ProceduralStyle == 2)
        {
            for (int i = 0; i < Curve.Length; i++)
            {
                float a = i * MathF.Tau / (Curve.Length - 1);
                float r = radius * (.72f + .28f * MathF.Cos(a * aura.Details + phase));
                Curve[i] = feet + new Vector2(MathF.Cos(a) * r, MathF.Sin(a) * r * .45f - 3);
            }
            DrawCurve(canvas, primary, 1.3f, front);
        }
        else if (aura.ProceduralStyle == 3)
        {
            for (int i = 0; i < 3; i++)
                for (int j = 0; j < 8; j++)
                {
                    float a = phase + i * MathF.Tau / 3 + j * .9f / 8;
                    float b = a + .9f / 8;
                    if (front.HasValue && (MathF.Sin((a+b)/2) >= 0) != front.Value) continue;
                    canvas.DrawLine(center + new Vector2(MathF.Cos(a), MathF.Sin(a)) * radius,
                        center + new Vector2(MathF.Cos(b), MathF.Sin(b)) * radius, primary, 1.5f, true);
                }
        }
        else if (aura.ProceduralStyle == 5)
        {
            for (int i = 0; i < Curve.Length; i++)
            {
                float a = i * MathF.Tau / (Curve.Length - 1);
                Curve[i] = feet + new Vector2(MathF.Cos(a) * radius, MathF.Sin(a) * radius * .36f - 4);
            }
            DrawCurve(canvas, primary, 1.6f, front);

            for (int i = 0; i < Curve.Length; i++)
            {
                float a = i * MathF.Tau / (Curve.Length - 1);
                Curve[i] = feet + new Vector2(MathF.Cos(a) * radius * .58f, MathF.Sin(a) * radius * .22f - 12);
            }
            DrawCurve(canvas, accent, 1f, front);
        }
        for (int i = 0; i < aura.Details; i++)
        {
            float a = phase + i * MathF.Tau / aura.Details;
            if (front.HasValue && (MathF.Sin(a) >= 0) != front.Value) continue;
            float rise = (float)((timeMs / aura.CycleMs + i / (double)aura.Details) % 1);
            Vector2 p = aura.ProceduralStyle == 4
                ? feet + new Vector2(MathF.Sin(a) * radius * .7f, -rise * aura.Height)
                : aura.ProceduralStyle == 5
                    ? feet + new Vector2(MathF.Cos(a) * radius * .78f, MathF.Sin(a) * radius * .28f - 10)
                : center + new Vector2(MathF.Cos(a) * radius, MathF.Sin(a) * radius * .4f);
            float size = aura.ProceduralStyle == 4 ? 3 : aura.ProceduralStyle == 5 ? 2.5f : 2;
            Color c = accent;
            if (aura.ProceduralStyle == 4) c.A *= MathF.Sin(rise * MathF.PI);
            canvas.DrawLine(p + new Vector2(0,-size), p + new Vector2(size,0), c, 1, true);
            canvas.DrawLine(p + new Vector2(size,0), p + new Vector2(0,size), c, 1, true);
            canvas.DrawLine(p + new Vector2(0,size), p + new Vector2(-size,0), c, 1, true);
            canvas.DrawLine(p + new Vector2(-size,0), p + new Vector2(0,-size), c, 1, true);
        }
    }

    private static void DrawWisps(CanvasItem canvas, AuraData aura, Vector2 feet,
        float phase, Color primary, Color accent, bool? front)
    {
        // Depth is evaluated for each tail segment too, keeping the body inside
        // the orbit as a wisp crosses from the back pass to the front pass.
        Vector2 Position(float angle, int index) => feet + new Vector2(
            MathF.Cos(angle) * aura.Radius,
            -aura.Height * .48f + MathF.Sin(angle) * aura.Radius * .34f
            + MathF.Sin(angle * 2 + index) * aura.Height * .13f);
        for (int wisp = 0; wisp < aura.Details; wisp++)
        {
            float angle = phase + wisp * MathF.Tau / aura.Details;
            for (int segment = 0; segment < 9; segment++)
            {
                float a = angle - segment * .065f, b = a - .065f;
                if (front.HasValue && (MathF.Sin((a + b) * .5f) >= 0) != front.Value) continue;
                Color tail = primary;
                tail.A *= (1f - segment / 9f) * .65f;
                canvas.DrawLine(Position(a, wisp), Position(b, wisp), tail, 2f, true);
            }
            if (front.HasValue && (MathF.Sin(angle) >= 0) != front.Value) continue;
            Vector2 p = Position(angle, wisp);
            Color glow = primary; glow.A *= .14f;
            canvas.DrawCircle(p, 5f, glow);
            glow.A *= 2f;
            canvas.DrawCircle(p, 3.2f, glow);
            canvas.DrawLine(p + new Vector2(MathF.Sin(phase * 3 + wisp), -4), p, primary, 2f, true);
            canvas.DrawCircle(p, 1.4f, accent);
        }
    }

    private static void DrawCurve(CanvasItem canvas, Color color, float width, bool? front)
    {
        // Parameter-space depth: the lower half is in front even for tilted rings.
        int start = front == false ? 12 : 0;
        int end = front == true ? 12 : 24;
        for (int i = start; i < end; i++)
            canvas.DrawLine(Curve[i], Curve[i+1], color, width, true);
    }
}
