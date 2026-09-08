using System;
using Godot;

namespace ArgentumNextgen.Rendering;

public partial class WorldRenderer
{
    private void UpdateWeaponImpacts(float delta)
    {
        if (_state == null) return;
        var impacts = _state.WeaponImpacts;
        if (!_state.IsLogged || !_state.Config.ShowParticles || !_state.Config.ShowReactiveEffects
            || !float.IsFinite(delta) || delta > .5f)
        { impacts.Clear(); return; }
        for (int i = impacts.Count - 1; i >= 0; i--)
        {
            var hit = impacts[i];
            if (!_state.Paused) hit.Age += Math.Max(0, delta);
            if (hit.Map != _state.MapData || hit.Owner.Invisible || hit.Age >= hit.Duration)
                impacts.RemoveAt(i);
        }
    }

    private void DrawWeaponImpacts(CanvasItem canvas)
    {
        if (_state == null || !_state.Config.ShowParticles || !_state.Config.ShowReactiveEffects) return;
        int drawn = 0;
        int limit = _state.Config.PerformanceLevel < 2 ? 24 : 64;
        foreach (var hit in _state.WeaponImpacts)
        {
            if (hit.Owner.Invisible || hit.Map != _state.MapData) continue;
            Vector2 pos = ReactiveToScreen(new(hit.Position.X, hit.Position.Y));
            if (!ReactiveOnScreen(pos) || hit.Owner.FovAlpha <= .1f) continue;
            if (++drawn > limit) break;
            DrawWeaponImpactShape(canvas, pos, hit.Kind, hit.Heading,
                hit.Age / hit.Duration, hit.Critical, hit.Owner.FovAlpha, hit.Direction);
        }
    }

    // Purely local geometry: no textures, emitter nodes, random state or frame allocations.
    internal static void DrawWeaponImpactShape(CanvasItem canvas, Vector2 pos, int kind,
        int heading, float t, bool critical, float opacity = 1, Vector2 direction = default)
    {
        t = Math.Clamp(t, 0, 1);
        float fade = (1 - t) * opacity;
        Vector2 forward = heading switch { 1 => Vector2.Up, 2 => Vector2.Right,
            4 => Vector2.Left, _ => Vector2.Down };
        if (direction.LengthSquared() > .1f) forward = direction.Normalized();
        Vector2 side = new(-forward.Y, forward.X);
        Color edge = critical ? new Color(1, .75f, .25f, fade) : new Color(.68f, .83f, .9f, fade);
        Color core = new(1, .96f, .8f, fade * .9f);
        if (kind == 201) // Sword: swept blade, open crescent rather than a surrounding ring.
        {
            float sweep = t * 1.4f;
            Vector2 previous = pos + side * -20 + forward * (-6 + sweep * 10);
            for (int i = 1; i <= 12; i++)
            {
                float u = i / 12f;
                Vector2 next = pos + side * (-20 + 40 * u)
                    + forward * (-6 + sweep * 10 + MathF.Sin(u * MathF.PI) * 12);
                canvas.DrawLine(previous, next, edge, 1 + MathF.Sin(u * MathF.PI) * 2, true);
                if (critical) canvas.DrawLine(previous - forward * 4, next - forward * 4, core, 1, true);
                previous = next;
            }
        }
        else if (kind == 202) // Heavy: compressed impact facets, no flying debris.
        {
            float size = 7 + MathF.Sin(t * MathF.PI) * 6;
            for (int i = 0; i < 5; i++)
            {
                float a = i * MathF.Tau / 5;
                Vector2 dir = new(MathF.Cos(a), MathF.Sin(a));
                canvas.DrawLine(pos + dir * 3, pos + dir * size, new Color(1,.6f,.27f,fade), critical ? 3 : 2, true);
            }
            canvas.DrawLine(pos - side * 6 - forward * 14, pos + forward * 5, edge, 3, true);
        }
        else if (kind == 203 || kind == 204) // Dagger thrust / arrow bite.
        {
            float length = kind == 203 ? 25 : 17;
            Vector2 tip = pos + forward * (t * 7);
            canvas.DrawLine(tip - forward * length, tip, edge, kind == 203 ? 2 : 1, true);
            canvas.DrawLine(tip, tip - forward * 5 + side * 4, core, 1, true);
            canvas.DrawLine(tip, tip - forward * 5 - side * 4, core, 1, true);
            if (critical) canvas.DrawLine(tip - forward * length + side * 3, tip + side * 3, core, 1, true);
        }
        else if (kind == 205) // Bowstring recoil on the shooter, on confirmed successful shot.
        {
            pos += forward * 12;
            Vector2 middle = pos - forward * (MathF.Sin(t * MathF.PI * 3) * 3);
            canvas.DrawLine(pos - side * 9, middle, edge, 1, true);
            canvas.DrawLine(middle, pos + side * 9, edge, 1, true);
        }
        else // Unarmed / unknown weapon: neutral compact contact mark.
        {
            canvas.DrawLine(pos - side * 5, pos + side * 5, edge, 2, true);
            canvas.DrawLine(pos - forward * 5, pos + forward * 5, edge, 1, true);
        }
        if (critical)
            canvas.DrawArc(pos, 7 + t * 3, -.8f, .8f, 8, core, 1.5f, true);
    }
}
