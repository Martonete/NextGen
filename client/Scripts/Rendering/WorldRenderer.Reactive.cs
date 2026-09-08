using System;
using Godot;
using ArgentumNextgen.Data;
using ArgentumNextgen.Game;
using NumericsVector = System.Numerics.Vector2;

namespace ArgentumNextgen.Rendering;

public partial class WorldRenderer
{
    private readonly ReactiveEffects _reactiveEffects = new();
    private MapData? _reactiveMap;
    private ReactiveGroundLayer? _waterEffectsLayer, _groundEffectsLayer;
    private ImageTexture? _reactiveGlowTexture;
    private readonly Vector2[] _reactiveRingPoints = new Vector2[41];

    private void UpdateReactiveEffects(float delta)
    {
        UpdateApocalypseEffects(delta);
        UpdateElectricDischargeEffects(delta);
        UpdateBindingEffects(delta);
        UpdateWeaponImpacts(delta);
        if (_state?.MapData != _reactiveMap)
        {
            _reactiveEffects.Clear();
            _reactiveMap = _state?.MapData;
        }
        if (_state?.MapData == null || !_state.Config.ShowReactiveEffects
            || (!_state.Config.ShowParticles && !_state.Config.ShowAuras))
        {
            _reactiveEffects.Clear();
            return;
        }
        // Prune visibility even when paused; no timer or motion advances while paused.
        _reactiveEffects.BeginFrame(_state.Paused ? 0 : delta,
            _state.Config.PerformanceLevel < 2 ? 64 : _state.Config.PerformanceLevel == 2 ? 192 : 512);
        if (_state.Characters.TryGetValue(_state.UserCharIndex, out var self)) ObserveReactiveCharacter(self);
        foreach (var entry in _state.Characters)
            if (entry.Key != _state.UserCharIndex) ObserveReactiveCharacter(entry.Value);
        _reactiveEffects.EndFrame();
    }

    private void ObserveReactiveCharacter(Character ch)
    {
        if (_state?.MapData == null) return;
        var map = _state.MapData;
        bool visible = !ch.Invisible && !ch.Dead && ch.FovAlpha > 0.1f
            && Math.Abs(ch.PosX - _state.UserPosX) <= HalfWindowTileWidth + 2
            && Math.Abs(ch.PosY - _state.UserPosY) <= HalfWindowTileHeight + 2;
        var position = new NumericsVector(ch.PosX * TileSize + ch.MoveOffsetX + 16,
            ch.PosY * TileSize + ch.MoveOffsetY + 27);
        // Resolve the surface under the interpolated feet, not the destination tile.
        int tx = (int)MathF.Floor(position.X / TileSize);
        int ty = (int)MathF.Floor(position.Y / TileSize);
        if (tx < 1 || ty < 1 || tx > map.Width || ty > map.Height) visible = false;
        bool water = false, dust = false;
        if (visible)
        {
            ref var tile = ref map.Tiles[tx, ty];
            bool wet = IsWaterGrh(tile.Layer1);
            water = wet && _state.Config.ShowWaterEffect;
            dust = !wet && tile.Layer1 > 0 && tile.Layer2 == 0
                && tile.Trigger != 1 && tile.Trigger != 2 && tile.Trigger != 4 && !ch.Navigating;
        }
        _reactiveEffects.Observe(ch.CharIndex, position, visible, water, dust,
            ch.CharIndex == _state.UserCharIndex && _state.Meditating,
            ch.HitEffectSequence, _state.Config.ShowParticles, _state.Config.ShowAuras,
            ch.Moving || ch.TranslationActive || (ch.CharIndex == _state.UserCharIndex && _state.UserMoving),
            ch.CharIndex == _state.UserCharIndex ? _state.Level : 1);
    }

    private Vector2 ReactiveToScreen(NumericsVector position) => new(
        position.X - (_frameUserX - HalfWindowTileWidth) * TileSize + _framePixelOffsetX,
        position.Y - (_frameUserY - HalfWindowTileHeight) * TileSize + _framePixelOffsetY);

    private bool ReactiveOwnerVisible(int owner) => _state != null && _state.Config.ShowReactiveEffects
        && _state.Characters.TryGetValue(owner, out var ch) && !ch.Invisible && !ch.Dead && ch.FovAlpha > 0.1f;

    private bool ReactiveOnScreen(Vector2 point) => point.X > -70 && point.Y > -90
        && point.X < ViewportWidth + 70 && point.Y < ViewportHeight + 90;

    public void DrawReactiveGround(CanvasItem canvas, bool water)
    {
        if (!water) DrawApocalypseEffects(canvas, false);
        if (_state == null || !_state.Config.ShowReactiveEffects) return;
        if (_state.Config.ShowParticles)
        {
            foreach (ref readonly var p in _reactiveEffects.Particles)
            {
                if (!ReactiveOwnerVisible(p.Owner)) continue;
                Vector2 pos = ReactiveToScreen(p.Position);
                if (!ReactiveOnScreen(pos)) continue;
                float t = p.Progress;
                float fade = (1 - t) * Math.Min(1, t * 14);
                if (water && _state.Config.ShowWaterEffect)
                {
                    if (p.Kind == ReactiveEffectKind.Wake)
                    {
                        float radius = 4 + p.Size * t;
                        DrawReactiveRing(canvas, pos, radius, 0.42f, new Color(0.65f, 0.9f, 1, fade * 0.55f), 1);
                        DrawReactiveRing(canvas, pos, radius * 0.72f, 0.42f, new Color(0.28f, 0.67f, 0.8f, fade * 0.25f), 1);
                    }
                    else if (p.Kind == ReactiveEffectKind.Splash)
                        canvas.DrawLine(pos, pos + new Vector2(0, -2.5f), new Color(0.8f, 0.95f, 1, fade * 0.8f), 1, true);
                }
                else if (!water)
                {
                    if (p.Kind == ReactiveEffectKind.Dust)
                        DrawReactiveGlowSprite(canvas, pos, p.Size * (0.6f + t), new Color(0.73f, 0.63f, 0.44f, fade * 0.5f));
                    else if (p.Kind == ReactiveEffectKind.Pulse && _state.Config.ShowAuras)
                        DrawReactiveRing(canvas, pos, 8 + p.Size * t, 0.45f, new Color(0.35f, 0.88f, 1, fade * 0.6f), 1.5f);
                }
            }
        }
        if (water || !_state.Config.ShowAuras) return;
        foreach (var actor in _reactiveEffects.Actors)
        {
            if (actor.Meditation <= 0 || !ReactiveOwnerVisible(actor.Id)) continue;
            Vector2 center = ReactiveToScreen(actor.Position);
            if (ReactiveOnScreen(center)) DrawMeditationSigil(canvas, center, actor.Meditation, actor.Level);
        }
    }

    private static (Color primary, Color accent) MeditationPalette(int tier) => tier switch
    {
        1 => (new Color(0.2f, 1, 0.65f), new Color(0.85f, 1, 0.5f)),
        2 => (new Color(0.66f, 0.4f, 1), new Color(0.45f, 0.85f, 1)),
        3 => (new Color(1, 0.35f, 0.2f), new Color(1, 0.83f, 0.35f)),
        4 => (new Color(0.55f, 0.85f, 1), new Color(1, 0.85f, 0.42f)),
        _ => (new Color(0.26f, 0.78f, 1), new Color(1, 0.78f, 0.34f))
    };

    private void DrawMeditationSigil(CanvasItem canvas, Vector2 center, float charge, int level)
    {
        var style = MeditationStyle.ForLevel(level);
        var palette = MeditationPalette(style.Tier);
        double time = _reactiveEffects.Time;
        float pulse = 0.85f + 0.15f * (float)Math.Sin(time * 2.4);
        float radius = style.Radius * (0.65f + 0.35f * charge);
        var blue = new Color(palette.primary, charge * pulse * 0.68f);
        var gold = new Color(palette.accent, charge * 0.8f);
        DrawReactiveGlowSprite(canvas, center, radius + 4, new Color(palette.primary, charge * 0.13f), 0.45f);
        DrawReactiveRing(canvas, center, radius, 0.45f, blue, 1.4f);
        DrawReactiveRing(canvas, center, radius - 5, 0.45f, new Color(blue, blue.A * 0.45f), 1);
        DrawReactiveRing(canvas, center, 15, 0.45f, gold, 1, (float)((time * -0.4) % Math.Tau), MathF.PI * 1.4f);
        // Eight rotating rune marks, with a counter-rotating broken inner circle.
        for (int i = 0; i < style.Runes; i++)
        {
            float angle = (float)((time * 0.25) % Math.Tau) + i * MathF.Tau / style.Runes;
            Vector2 radial = new(MathF.Cos(angle), MathF.Sin(angle) * 0.45f);
            Vector2 tangent = new(-MathF.Sin(angle), MathF.Cos(angle) * 0.45f);
            Vector2 tip = center + radial * (radius - 1);
            Vector2 root = center + radial * (radius - 7);
            canvas.DrawLine(tip, root - tangent * 2.5f, gold, 1, true);
            canvas.DrawLine(tip, root + tangent * 2.5f, gold, 1, true);
            canvas.DrawLine(root - tangent * 2.5f, root + tangent * 2.5f, blue, 1, true);
        }
        for (int ring = 0; ring < style.Tier; ring++)
        {
            float start = (float)((time * (ring % 2 == 0 ? -0.45 : 0.32)) % Math.Tau) + ring;
            DrawReactiveRing(canvas, center, radius - 12 - ring * 5, 0.45f,
                new Color(palette.primary, charge * 0.42f), 1.2f, start, MathF.PI * 1.6f);
        }
        if (style.Tier >= 2)
            for (int i = 0; i < style.Tier * 2; i++)
            {
                float angle = (float)((time * -0.55) % Math.Tau) + i * MathF.Tau / (style.Tier * 2);
                var at = center + new Vector2(MathF.Cos(angle) * (radius + 4), MathF.Sin(angle) * (radius + 4) * 0.45f);
                canvas.DrawLine(at - new Vector2(3, 0), at + new Vector2(3, 0), gold, 1, true);
                canvas.DrawLine(at - new Vector2(0, 4), at + new Vector2(0, 4), gold, 1, true);
            }
    }

    private void DrawMeditationAscension(CanvasItem canvas)
    {
        if (_state == null || !_state.Config.ShowReactiveEffects || !_state.Config.ShowAuras) return;
        foreach (var actor in _reactiveEffects.Actors)
        {
            if (actor.Level < 50 || actor.Meditation <= 0 || !ReactiveOwnerVisible(actor.Id)) continue;
            Vector2 center = ReactiveToScreen(actor.Position);
            if (!ReactiveOnScreen(center)) continue;
            float phase = (float)((_reactiveEffects.Time * 1.5) % Math.Tau);
            float charge = actor.Meditation;
            var palette = MeditationPalette(4);
            // Two ascending strands weave around the character, never the UI.
            for (int strand = 0; strand < 2; strand++)
            {
                Color tint = strand == 0 ? palette.primary : palette.accent;
                for (int i = 0; i < _reactiveRingPoints.Length; i++)
                {
                    float t = i / (float)(_reactiveRingPoints.Length - 1);
                    float angle = phase + strand * MathF.PI + t * MathF.Tau * 1.25f;
                    _reactiveRingPoints[i] = center + new Vector2(MathF.Sin(angle) * (33 - t * 12), -t * 78 * charge);
                }
                canvas.DrawPolyline(_reactiveRingPoints, new Color(tint, charge * 0.3f), 1.4f, true);
                for (int i = 8; i < _reactiveRingPoints.Length; i += 10)
                {
                    Vector2 at = _reactiveRingPoints[i];
                    DrawReactiveGlowSprite(canvas, at, 7, new Color(tint, charge * 0.48f));
                    canvas.DrawCircle(at, 1.4f, new Color(1, 0.96f, 0.8f, charge * 0.8f));
                }
            }
            Vector2 crown = center + new Vector2(0, -78 * charge);
            DrawReactiveRing(canvas, crown, 17, 0.24f, new Color(palette.accent, charge * 0.6f), 1.3f);
            for (int i = -2; i <= 2; i++)
            {
                var at = crown + new Vector2(i * 6, -2);
                canvas.DrawLine(at, at + new Vector2(0, -6 + System.Math.Abs(i)), new Color(palette.accent, charge * 0.6f), 1, true);
            }
        }
    }

    private void DrawReactiveGlow(CanvasItem canvas)
    {
        DrawApocalypseEffects(canvas, true);
        DrawElectricDischargeEffects(canvas);
        DrawWeaponImpacts(canvas);
        DrawMeditationAscension(canvas);
        if (_state == null || !_state.Config.ShowReactiveEffects || !_state.Config.ShowParticles) return;
        foreach (ref readonly var p in _reactiveEffects.Particles)
        {
            if (p.Kind != ReactiveEffectKind.Spark && p.Kind != ReactiveEffectKind.Arcane) continue;
            if (p.Kind == ReactiveEffectKind.Arcane && !_state.Config.ShowAuras) continue;
            if (p.Kind == ReactiveEffectKind.Spark && _state.Characters.TryGetValue(p.Owner, out var hitOwner)
                && hitOwner.ElectricDischargeTime >= 0) continue;
            if (!ReactiveOwnerVisible(p.Owner)) continue;
            Vector2 pos = ReactiveToScreen(p.Position);
            if (!ReactiveOnScreen(pos)) continue;
            float fade = MathF.Sin(p.Progress * MathF.PI);
            Color color = p.Kind == ReactiveEffectKind.Spark
                ? new Color(1, 0.65f + p.Seed * 0.25f, 0.25f, fade * 0.9f)
                : new Color(MeditationPalette(p.MeditationTier).primary.Lerp(MeditationPalette(p.MeditationTier).accent, p.Seed), fade * 0.75f);
            if (p.Kind == ReactiveEffectKind.Arcane)
                pos.X += MathF.Sin(p.Age * 5 + p.Seed * MathF.Tau) * 4;
            DrawReactiveGlowSprite(canvas, pos, p.Size * 3, new Color(color, color.A * 0.3f));
            canvas.DrawLine(pos, pos - new Vector2(p.Velocity.X, p.Velocity.Y) * 0.035f, color, 1.2f, true);
            canvas.DrawCircle(pos, 0.8f, new Color(1, 0.95f, 0.8f, fade));
        }
    }

    private void DrawReactiveRing(CanvasItem canvas, Vector2 center, float radius, float flatten,
        Color color, float width, float start = 0, float arc = MathF.Tau)
    {
        for (int i = 0; i < _reactiveRingPoints.Length; i++)
        {
            float a = start + arc * i / (_reactiveRingPoints.Length - 1);
            _reactiveRingPoints[i] = center + new Vector2(MathF.Cos(a) * radius, MathF.Sin(a) * radius * flatten);
        }
        canvas.DrawPolyline(_reactiveRingPoints, color, width, true);
    }

    private void DrawReactiveGlowSprite(CanvasItem canvas, Vector2 center, float radius, Color color, float flatten = 1)
    {
        if (_reactiveGlowTexture == null)
        {
            using var image = Image.CreateEmpty(32, 32, false, Image.Format.Rgba8);
            for (int y = 0; y < 32; y++)
                for (int x = 0; x < 32; x++)
                {
                    float d = new Vector2((x - 15.5f) / 15.5f, (y - 15.5f) / 15.5f).LengthSquared();
                    float alpha = Math.Max(0, 1 - d);
                    image.SetPixel(x, y, new Color(1, 1, 1, alpha * alpha * alpha));
                }
            _reactiveGlowTexture = ImageTexture.CreateFromImage(image);
        }
        Vector2 size = new(radius * 2, radius * 2 * flatten);
        canvas.DrawTextureRect(_reactiveGlowTexture, new Rect2(center - size / 2, size), false, color);
    }

    public override void _ExitTree()
    {
        _reactiveEffects.Clear();
        _reactiveGlowTexture?.Dispose();
        _reactiveGlowTexture = null;
    }
}
