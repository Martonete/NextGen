using System;
using Godot;

namespace ArgentumNextgen.Rendering;

public partial class WorldRenderer
{
    private const float ElectricDischargeDuration = 0.55f;

    private void UpdateElectricDischargeEffects(float delta)
    {
        if (_state == null) return;
        foreach (var ch in _state.Characters.Values)
        {
            if (ch.ElectricDischargeTime < 0) continue;
            if (_state.MapData != _reactiveMap || ch.Invisible || delta > 0.5f)
            { ch.ElectricDischargeTime = -1; continue; }
            if (!_state.Paused && float.IsFinite(delta)) ch.ElectricDischargeTime += Math.Max(0, delta);
            if (ch.ElectricDischargeTime >= ElectricDischargeDuration) ch.ElectricDischargeTime = -1;
        }
    }

    // Short branching arcs hug the target. Geometry changes at fixed time intervals,
    // not per rendered frame; no emitter nodes, random state or per-frame arrays.
    private void DrawElectricDischargeEffects(CanvasItem canvas)
    {
        if (_state == null || !_state.Config.ShowReactiveEffects || !_state.Config.ShowParticles) return;
        foreach (var ch in _state.Characters.Values)
        {
            float age = ch.ElectricDischargeTime;
            if (age < 0 || ch.Invisible || ch.FovAlpha <= 0.1f) continue;
            Vector2 core = ReactiveToScreen(new System.Numerics.Vector2(
                ch.PosX * TileSize + ch.MoveOffsetX + 16,
                ch.PosY * TileSize + ch.MoveOffsetY + 4));
            if (!ReactiveOnScreen(core)) continue;
            float t = age / ElectricDischargeDuration;
            float fade = (1 - t) * ch.FovAlpha;
            int phase = (int)(age / 0.075f);
            float crackle = 0.7f + 0.3f * MathF.Exp(-(age % 0.075f) * 45);
            float power = fade * crackle;
            DrawReactiveGlowSprite(canvas, core, 27, new Color(0.12f, 0.42f, 1, power * 0.32f));
            DrawReactiveGlowSprite(canvas, core, 15,
                new Color(0.75f, 0.94f, 1, Math.Max(0, 1 - age / 0.12f) * ch.FovAlpha * 0.65f));
            int arcs = _state.Config.PerformanceLevel < 2 ? 3 : 5;
            for (int arc = 0; arc < arcs; arc++)
            {
                float angle = arc * MathF.Tau / arcs + phase * 0.43f;
                Vector2 direction = new(MathF.Cos(angle), MathF.Sin(angle));
                Vector2 normal = new(-direction.Y, direction.X);
                Vector2 previous = core - direction * 16;
                for (int segment = 1; segment <= 6; segment++)
                {
                    float jitter = MathF.Sin(segment * 19.13f + arc * 7.71f + phase * 11.3f) * 8;
                    Vector2 next = core + direction * (-16 + segment * 6) + normal * jitter;
                    DrawElectricSegment(canvas, previous, next, power);
                    if (segment == 3 || segment == 5)
                    {
                        Vector2 fork = next + normal * (arc % 2 == 0 ? 9 : -9) + direction * 3;
                        DrawElectricSegment(canvas, next, fork, power * 0.65f);
                    }
                    previous = next;
                }
            }
        }
    }

    private static void DrawElectricSegment(CanvasItem canvas, Vector2 from, Vector2 to, float power)
    {
        canvas.DrawLine(from, to, new Color(0.08f, 0.35f, 1, power * 0.35f), 4, true);
        canvas.DrawLine(from, to, new Color(0.55f, 0.87f, 1, power), 1.3f, true);
    }
}
