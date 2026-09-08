using System;
using Godot;

namespace ArgentumNextgen.Rendering;

public partial class WorldRenderer
{
    private const float ApocalypseDuration = 0.7f;

    private void UpdateApocalypseEffects(float delta)
    {
        if (_state == null) return;
        foreach (var ch in _state.Characters.Values)
        {
            if (ch.ApocalypseTime < 0) continue;
            if (_state.MapData != _reactiveMap || ch.Invisible || delta > 0.5f)
            { ch.ApocalypseTime = -1; continue; }
            if (!_state.Paused && float.IsFinite(delta)) ch.ApocalypseTime += Math.Max(0, delta);
            if (ch.ApocalypseTime >= ApocalypseDuration) ch.ApocalypseTime = -1;
        }
    }

    // Analytic, bounded particles: no emitter nodes, per-frame arrays or random flicker.
    // A repeated cast restarts the impact instead of accumulating unlimited emitters.
    private void DrawApocalypseEffects(CanvasItem canvas, bool glow)
    {
        if (_state == null || !_state.Config.ShowReactiveEffects || !_state.Config.ShowParticles) return;
        foreach (var ch in _state.Characters.Values)
        {
            float age = ch.ApocalypseTime;
            if (age < 0 || ch.Invisible || ch.FovAlpha <= 0.1f) continue;
            Vector2 feet = ReactiveToScreen(new System.Numerics.Vector2(
                ch.PosX * TileSize + ch.MoveOffsetX + 16, ch.PosY * TileSize + ch.MoveOffsetY + 27));
            if (!ReactiveOnScreen(feet)) continue;
            float t = age / ApocalypseDuration;
            float fade = (1 - t) * ch.FovAlpha;
            float eruption = 1 - MathF.Exp(-age * 32);
            Vector2 core = feet + new Vector2(0, -23);
            Color fire = new(1, 0.19f, 0.025f, fade);
            Color ember = new(1, 0.62f, 0.12f, fade);
            if (!glow)
            {
                // Irregular soot clouds, not a magic circle or symmetrical aura.
                for (int i = 0; i < 9; i++)
                {
                    float angle = i * 2.39996323f;
                    Vector2 at = feet + new Vector2(MathF.Cos(angle) * 15 * eruption,
                        MathF.Sin(angle) * 8 * eruption - age * (5 + i));
                    DrawReactiveGlowSprite(canvas, at, 12 + t * 8,
                        new Color(0.09f, 0.055f, 0.045f, fade * 0.65f), 0.75f);
                }
                continue;
            }
            float flash = Math.Max(0, 1 - age / 0.16f);
            DrawReactiveGlowSprite(canvas, core, 30, new Color(1, 0.85f, 0.5f, flash * ch.FovAlpha));
            // Overlapping fire lobes form a compact fireball, not long radial spikes.
            float blast = Math.Max(0, 1 - age / 0.4f) * ch.FovAlpha;
            DrawReactiveGlowSprite(canvas, core, 16 + 13 * eruption,
                new Color(1, 0.25f, 0.025f, blast * 0.8f));
            for (int i = 0; i < 11; i++)
            {
                float seed = (i * 0.618033989f) % 1;
                float angle = i * 2.39996323f;
                float burn = Math.Max(0, 1 - age / (0.28f + seed * 0.14f)) * ch.FovAlpha;
                Vector2 direction = new(MathF.Cos(angle), MathF.Sin(angle));
                Vector2 lobe = core + direction * (6 + eruption * (9 + seed * 5));
                float radius = (9 + seed * 5) * (0.65f + 0.35f * eruption);
                DrawReactiveGlowSprite(canvas, lobe, radius * 1.3f, new Color(fire, burn * 0.75f));
                DrawReactiveGlowSprite(canvas, lobe - direction * 3, radius * 0.7f,
                    new Color(1, 0.64f, 0.12f, burn * 0.85f));
            }
            int count = _state.Config.PerformanceLevel < 2 ? 12 : _state.Config.PerformanceLevel == 2 ? 24 : 40;
            for (int i = 0; i < count; i++)
            {
                float seed = (i * 0.618033989f) % 1;
                float angle = i * 2.39996323f;
                Vector2 velocity = new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * (65 + seed * 45);
                // Strong drag arrests the burst within ~30 px instead of spreading.
                float travel = (1 - MathF.Exp(-age * 4)) / 4;
                Vector2 point = core + velocity * travel + new Vector2(0, 18 * age * age);
                Vector2 tail = (velocity * MathF.Exp(-age * 4) + new Vector2(0, 36 * age)) * 0.035f;
                Color tint = fire.Lerp(ember, seed * (1 - t));
                DrawReactiveGlowSprite(canvas, point, 4 + seed * 3, new Color(tint, fade * 0.25f));
                canvas.DrawLine(point, point - tail, tint, 1 + seed * 2, true);
                // Angular, tumbling cinders instead of round floating magic motes.
                Vector2 edge = Vector2.FromAngle(i + age * (4 + seed * 8)) * (1 + seed * 2);
                canvas.DrawLine(point - edge, point + edge, new Color(1, 0.4f, 0.08f, fade), 1.5f, true);
            }
        }
    }
}
