using System;
using Godot;
using ArgentumNextgen.Game;

namespace ArgentumNextgen.Rendering;

public partial class WorldRenderer
{
    private const float BindingDuration = 1.1f;

    private void UpdateBindingEffects(float delta)
    {
        if (_state == null) return;
        foreach (var ch in _state.Characters.Values)
        {
            if (ch.BindingTime < 0) continue;
            if (_state.MapData != _reactiveMap || ch.Invisible || ch.Dead || delta > 0.5f)
            { ch.BindingTime = -1; continue; }
            if (!_state.Paused && float.IsFinite(delta)) ch.BindingTime += Math.Max(0, delta);
            if (ch.BindingTime >= BindingDuration) ch.BindingTime = -1;
        }
    }

    // Split at the body so the rope actually wraps the victim. Normal blending
    // preserves forest green instead of bleaching it into neon on the additive layer.
    private void DrawBindingEffect(CanvasItem canvas, Character ch, Vector2 center, bool front)
    {
        if (_state == null || !_state.Config.ShowReactiveEffects || !_state.Config.ShowParticles
            || ch.BindingTime < 0 || ch.Invisible || ch.Dead || ch.FovAlpha <= 0.1f) return;
        float age = ch.BindingTime;
        if (ch.BindingIsParalysis)
        {
            DrawParalysisClasp(canvas, ch, center, front);
            return;
        }
        float close = 1 - MathF.Pow(1 - Math.Min(1, age / 0.3f), 3);
        float alpha = Math.Min(1, age * 25) * Math.Min(1, (BindingDuration - age) / 0.3f) * ch.FovAlpha;
        float radius = 30 - 16 * close;
        for (int turn = 0; turn < 3; turn++)
        {
            Vector2 middle = center + new Vector2(0, (turn - 1) * 9);
            float start = front ? 0 : MathF.PI;
            for (int i = 0; i < _reactiveRingPoints.Length; i++)
            {
                float angle = start + i * MathF.PI / (_reactiveRingPoints.Length - 1);
                _reactiveRingPoints[i] = middle + new Vector2(MathF.Cos(angle) * radius,
                    MathF.Sin(angle) * 5 + MathF.Cos(angle) * (turn % 2 == 0 ? 3 : -3));
            }
            canvas.DrawPolyline(_reactiveRingPoints, new Color(0.015f, 0.1f, 0.035f, alpha), 4.5f, true);
            canvas.DrawPolyline(_reactiveRingPoints, new Color(0.045f, 0.32f, 0.12f, alpha), 2.5f, true);
        }
        if (!front) return;
        // Tight central crossing and two short hanging ends finish the binding.
        Vector2 knot = center + new Vector2(0, 5);
        var green = new Color(0.06f, 0.34f, 0.13f, alpha * close);
        canvas.DrawLine(knot + new Vector2(-4, -4), knot + new Vector2(4, 4), green, 3, true);
        canvas.DrawLine(knot + new Vector2(4, -4), knot + new Vector2(-4, 4), green, 3, true);
        canvas.DrawLine(knot, knot + new Vector2(-5, 15), green, 2, true);
        canvas.DrawLine(knot, knot + new Vector2(7, 12), green, 2, true);
    }

    private void DrawParalysisClasp(CanvasItem canvas, Character ch, Vector2 center, bool front)
    {
        float age = ch.BindingTime;
        float snap = Math.Min(1, age / 0.16f);
        float alpha = Math.Min(1, age * 35) * Math.Min(1, (BindingDuration - age) / 0.3f) * ch.FovAlpha;
        float radius = 14 + 15 * (1 - snap) * (1 - snap);
        // Angular iron-blue shackles: a hard clamp rather than a flowing green rope.
        for (int band = 0; band < 2; band++)
        {
            Vector2 middle = center + new Vector2(0, band == 0 ? -9 : 9);
            Vector2 previous = middle + new Vector2(radius, 0);
            for (int i = 1; i <= 4; i++)
            {
                float a = i * MathF.PI / 4;
                Vector2 next = middle + new Vector2(MathF.Cos(a) * radius,
                    MathF.Sin(a) * (front ? 5 : -5));
                canvas.DrawLine(previous, next, new Color(0.025f, 0.07f, 0.12f, alpha), 5, true);
                canvas.DrawLine(previous, next, new Color(0.16f, 0.29f, 0.38f, alpha), 2.5f, true);
                previous = next;
            }
        }
        if (!front) return;
        var iron = new Color(0.12f, 0.23f, 0.32f, alpha * snap);
        // Central rigid spine and small latch visibly lock the two bands together.
        canvas.DrawLine(center + new Vector2(0, -6), center + new Vector2(0, 14), iron, 3, true);
        canvas.DrawRect(new Rect2(center + new Vector2(-4, -1), new Vector2(8, 8)), iron);
        canvas.DrawLine(center + new Vector2(0, 1), center + new Vector2(0, 5),
            new Color(0.025f, 0.06f, 0.1f, alpha * snap), 1.5f, true);
    }
}
