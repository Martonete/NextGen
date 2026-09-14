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
            DrawParalysisLock(canvas, ch, center, front);
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

    /// <summary>
    /// Paralizar: an ice lock closing on the victim, not a shackle strapped over them.
    /// Built from the vocabulary already used elsewhere so it reads as the same family:
    /// the flat static ground ring of the warp aura (RunicAuraRenderer style 5), the
    /// hard decay envelopes and glow sprites of Apocalipsis/Descarga, and the flattened
    /// elliptical rings of the meditation sigil. Fully analytic — no randomness, no
    /// per-frame allocation, and every stroke is bounded by the same fade as the rope.
    /// </summary>
    private void DrawParalysisLock(CanvasItem canvas, Character ch, Vector2 center, bool front)
    {
        if (_state == null) return;
        float age = ch.BindingTime;
        // The lock slams shut in the first frames, then holds rigid for the rest.
        float snap = Math.Min(1, age / 0.14f);
        float ease = 1 - MathF.Pow(1 - snap, 3);
        float alpha = Math.Min(1, age * 35) * Math.Min(1, (BindingDuration - age) / 0.32f) * ch.FovAlpha;
        if (alpha <= 0.01f) return;

        Vector2 feet = center + new Vector2(0, 23);
        float radius = 21 - 5 * ease; // tightens as it catches
        // Near-white ice. The outline stays dark enough to hold the silhouette against
        // bright grass, but the body and highlight are pushed close to white so the
        // crystals read as glare rather than as painted blue glass.
        var deep = new Color(0.10f, 0.21f, 0.31f, alpha);
        var iron = new Color(0.68f, 0.85f, 0.96f, alpha);
        var frost = new Color(0.95f, 0.99f, 1f, alpha);

        // Cold sheen pooled under the victim. Drawn only on the back pass so it lifts the
        // ice off the ground without washing over the character sprite.
        if (!front)
            DrawReactiveGlowSprite(canvas, feet, radius + 14,
                new Color(0.80f, 0.93f, 1f, alpha * 0.22f), 0.45f);

        // Half the ellipse per pass, so the ring genuinely encircles the character
        // instead of floating in front of them.
        float start = front ? 0 : MathF.PI;
        DrawReactiveRing(canvas, feet, radius, 0.42f, deep, 4.5f, start, MathF.PI);
        DrawReactiveRing(canvas, feet, radius, 0.42f, iron, 2.2f, start, MathF.PI);
        DrawReactiveRing(canvas, feet, radius - 4, 0.42f,
            new Color(frost, alpha * 0.5f * ease), 1f, start, MathF.PI);

        // Crystal shards rooted on the ring, driven upward as the lock closes.
        int shards = _state.Config.PerformanceLevel < 2 ? 6 : 9;
        for (int i = 0; i < shards; i++)
        {
            float a = i * MathF.Tau / shards + 0.31f;
            float sin = MathF.Sin(a);
            if (front != (sin > 0)) continue; // each shard belongs to one side only

            float seed = (i * 0.618033989f) % 1;
            Vector2 root = feet + new Vector2(MathF.Cos(a) * radius, sin * radius * 0.42f);
            Vector2 tip = root + new Vector2(MathF.Cos(a) * 3, -(10 + seed * 9) * ease);
            Vector2 side = new(2.2f + seed * 1.3f, 0);
            canvas.DrawLine(root - side, tip, deep, 3.2f, true);
            canvas.DrawLine(root + side, tip, deep, 3.2f, true);
            canvas.DrawLine(root - side, tip, iron, 1.6f, true);
            canvas.DrawLine(root + side, tip, iron, 1.6f, true);
            canvas.DrawLine(root.Lerp(tip, 0.35f), tip, new Color(frost, alpha * 0.9f), 1f, true);
            // Specular pop on the point of each crystal.
            DrawReactiveGlowSprite(canvas, tip, 3.6f + seed * 1.6f,
                new Color(1, 1, 1, alpha * 0.55f * ease));
        }

        // ── The cage closing overhead ──
        // Ribs climb from the ground ring and bow outward at waist height, so they hug
        // the silhouette instead of cutting across the sprite. They grow with `ease`,
        // which is what makes the ice read as closing *over* the victim.
        // Only four, thin and translucent: enough to close the shape overhead without
        // caging the sprite. Anything denser turns the victim into a wireframe blob.
        Vector2 crown = center + new Vector2(0, -26);
        float crownRadius = (radius - 4) * 0.42f;
        var rib = new Color(frost, alpha * 0.45f);
        var ribEdge = new Color(deep, alpha * 0.3f);
        for (int i = 0; i < 4; i++)
        {
            float a = i * MathF.Tau / 4 + 0.5f;
            float sin = MathF.Sin(a), cos = MathF.Cos(a);
            if (front != (sin > 0)) continue;

            float baseY = feet.Y + sin * radius * 0.42f;
            float topY = crown.Y + sin * crownRadius * 0.42f;
            Vector2 previous = new(feet.X + cos * radius, baseY);
            for (int s = 1; s <= 5; s++)
            {
                float t = s / 5f * ease;
                float bulge = 1 + 0.1f * MathF.Sin(t * MathF.PI);
                Vector2 next = new(feet.X + cos * Mathf.Lerp(radius, crownRadius, t) * bulge,
                    Mathf.Lerp(baseY, topY, t));
                canvas.DrawLine(previous, next, ribEdge, 1.8f, true);
                canvas.DrawLine(previous, next, rib, 0.9f, true);
                previous = next;
            }
        }

        // Crown: a small lid above the head, only there once the ribs have met.
        float lid = ease * ease;
        if (lid > 0.02f)
        {
            DrawReactiveRing(canvas, crown, crownRadius, 0.42f,
                new Color(frost, alpha * lid * 0.7f), 1.2f, start, MathF.PI);
            DrawReactiveGlowSprite(canvas, crown, 7,
                new Color(1, 1, 1, alpha * lid * 0.35f), 0.5f);
        }

        // One glint travelling the ring reads as a solid iced surface, not a flat decal.
        float glintAngle = start + MathF.PI * Math.Min(1, age / 0.55f);
        DrawReactiveGlowSprite(canvas,
            feet + new Vector2(MathF.Cos(glintAngle) * radius, MathF.Sin(glintAngle) * radius * 0.42f),
            9, new Color(1, 1, 1, alpha * 0.7f * ease));

        if (!front) return;

        // The catch itself: a cold flash that is gone in a sixth of a second.
        float flash = Math.Max(0, 1 - age / 0.17f) * ch.FovAlpha;
        if (flash > 0.01f)
        {
            DrawReactiveGlowSprite(canvas, feet, 30 + 10 * ease,
                new Color(0.88f, 0.96f, 1, flash * 0.6f), 0.45f);
            DrawReactiveGlowSprite(canvas, center, 17, new Color(1, 1, 1, flash * 0.6f));
        }

        // Rigid frost streaks hugging the silhouette. Straight and still on purpose: the
        // body is locked, so nothing here flows the way the meditation strands do. They
        // ride the outer edge rather than the chest, so the character stays readable.
        for (int i = -1; i <= 1; i += 2)
        {
            Vector2 low = center + new Vector2(i * 10, 15);
            Vector2 high = low + new Vector2(-i * 2f, -13 * ease);
            canvas.DrawLine(low, high, new Color(deep, alpha * 0.4f), 1.8f, true);
            canvas.DrawLine(low, high, new Color(frost, alpha * 0.55f), 0.9f, true);
        }

        // Small faceted lozenge at the chest, finishing exactly when the lock lands.
        Vector2 core = center + new Vector2(0, 4);
        Vector2 up = new(0, -4.2f * ease), right = new(2.5f * ease, 0);
        var mark = new Color(frost, alpha * 0.95f);
        canvas.DrawLine(core + up, core + right, mark, 1.2f, true);
        canvas.DrawLine(core + right, core - up, mark, 1.2f, true);
        canvas.DrawLine(core - up, core - right, mark, 1.2f, true);
        canvas.DrawLine(core - right, core + up, mark, 1.2f, true);
    }
}
