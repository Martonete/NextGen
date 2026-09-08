using System;
using System.Collections.Generic;
using System.Numerics;

namespace ArgentumNextgen.Rendering;

public enum ReactiveEffectKind { Dust, Wake, Splash, Spark, Arcane, Pulse }

public struct ReactiveParticle
{
    public int Owner;
    public int MeditationTier;
    public ReactiveEffectKind Kind;
    public Vector2 Origin, Velocity;
    public float Age, Lifetime, Size, Seed;
    public readonly float Progress => Math.Clamp(Age / Lifetime, 0f, 1f);
    public readonly Vector2 Position => Origin + Velocity * Age
        + new Vector2(0, Kind is ReactiveEffectKind.Spark or ReactiveEffectKind.Splash ? 45f * Age * Age : 0);
}

public sealed class ReactiveActor
{
    public int Id;
    public Vector2 Position;
    public float Meditation;
    public int Level = 1;
    internal float StepDistance, MoteTime;
    internal uint HitSequence;
    internal int Frame;
    internal bool Water, Foot;
    internal bool MeditationInterrupted;
    internal bool HasMeditationParticles;
}

/// <summary>
/// Bounded, render-independent cosmetic simulation. World pixels never follow the camera.
/// No particles/nodes are allocated while emitting. Drawing never advances simulation.
/// </summary>
public sealed class ReactiveEffects
{
    public const int Capacity = 512;
    public const int ActorCapacity = 192;
    private readonly ReactiveParticle[] _particles = new ReactiveParticle[Capacity];
    private readonly Dictionary<int, ReactiveActor> _actors = new(ActorCapacity);
    private readonly List<int> _expiredActors = new(ActorCapacity);
    private int _count, _frame, _limit = Capacity;
    private float _delta;
    private uint _random = 0xA0712026;
    public double Time { get; private set; }
    public int Count => _count;
    public int ActorCount => _actors.Count;
    public ReadOnlySpan<ReactiveParticle> Particles => _particles.AsSpan(0, _count);
    public Dictionary<int, ReactiveActor>.ValueCollection Actors => _actors.Values;

    public void Clear()
    {
        _count = 0;
        _actors.Clear();
        _expiredActors.Clear();
    }

    public void BeginFrame(float delta, int limit)
    {
        _delta = float.IsFinite(delta) ? Math.Max(0, delta) : 0;
        Time += _delta;
        _frame++;
        _limit = Math.Clamp(limit, 0, Capacity);
        // A long stall should not replay a burst of missed footsteps on resuming.
        if (_delta > 0.5f) { Clear(); _delta = 0; }
        _count = Math.Min(_count, _limit);
        for (int i = _count - 1; i >= 0; i--)
        {
            _particles[i].Age += _delta;
            if (_particles[i].Age >= _particles[i].Lifetime) RemoveAt(i);
        }
    }

    public void Observe(int id, Vector2 position, bool visible, bool water, bool dust,
        bool meditating, uint hitSequence, bool particles, bool auras, bool moving = false, int level = 1)
    {
        if (!visible || !float.IsFinite(position.X) || !float.IsFinite(position.Y))
        {
            Forget(id);
            return;
        }
        if (!_actors.TryGetValue(id, out var actor))
        {
            if (_actors.Count >= ActorCapacity) return;
            actor = new ReactiveActor { Id = id, Position = position, Water = water, HitSequence = hitSequence };
            _actors.Add(id, actor);
        }
        actor.Frame = _frame;
        actor.Level = Math.Clamp(level, 1, 50);
        var style = MeditationStyle.ForLevel(actor.Level);
        Vector2 travel = position - actor.Position;
        float distance = travel.Length();
        if (!meditating) actor.MeditationInterrupted = false;
        if (moving || distance > 0.01f)
        {
            actor.MeditationInterrupted = true;
            actor.Meditation = actor.MoteTime = 0;
            if (actor.HasMeditationParticles)
            {
                for (int i = _count - 1; i >= 0; i--)
                    if (_particles[i].Owner == id && _particles[i].Kind is ReactiveEffectKind.Arcane or ReactiveEffectKind.Pulse)
                        RemoveAt(i);
                actor.HasMeditationParticles = false;
            }
        }
        if (distance > 96f)
        {
            // Corrections/teleports must not paint a trail through intervening walls.
            RemoveOwnerParticles(id);
            actor.StepDistance = actor.MoteTime = actor.Meditation = 0;
        }
        else if (particles && _delta > 0)
        {
            if (water && !actor.Water) EmitStep(id, position, Vector2.Zero, true, ref actor.Foot);
            if (distance > 0.01f && (water || dust))
            {
                const float spacing = 18f;
                Vector2 direction = travel / distance;
                float next = spacing - actor.StepDistance;
                for (float d = next; d <= distance; d += spacing)
                    EmitStep(id, actor.Position + direction * d, direction, water, ref actor.Foot);
                actor.StepDistance = (actor.StepDistance + distance) % spacing;
            }
            else if (!water && !dust) actor.StepDistance = 0;
            if (hitSequence != actor.HitSequence)
            {
                for (int i = 0; i < 10; i++)
                {
                    float angle = Random01() * MathF.Tau;
                    Emit(id, ReactiveEffectKind.Spark, position + new Vector2(0, -24),
                        new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * (22 + Random01() * 42), 0.38f, 2f);
                }
            }
        }
        actor.Position = position;
        actor.Water = water;
        actor.HitSequence = hitSequence;
        float target = meditating && auras && !actor.MeditationInterrupted ? 1f : 0f;
        float previous = actor.Meditation;
        float fadeStep = _delta * (target > previous ? 1.5f : 3f);
        actor.Meditation = previous + Math.Clamp(target - previous, -fadeStep, fadeStep);
        if (previous == 0 && target > 0 && particles && _delta > 0)
        {
            actor.HasMeditationParticles = true;
            Emit(id, ReactiveEffectKind.Pulse, position, Vector2.Zero, 0.65f, style.Radius * 1.23f, style.Tier);
        }
        if (actor.Meditation > 0.1f && target > 0 && particles)
        {
            actor.MoteTime += _delta;
            while (actor.MoteTime >= style.MoteInterval)
            {
                actor.HasMeditationParticles = true;
                actor.MoteTime -= style.MoteInterval;
                float angle = (float)((Time * 2.3) % Math.Tau) + Random01() * MathF.Tau;
                Emit(id, ReactiveEffectKind.Arcane,
                    position + new Vector2(MathF.Cos(angle) * style.Radius * 0.77f, MathF.Sin(angle) * style.Radius * 0.35f - 3),
                    new Vector2(-MathF.Cos(angle) * 7, -23 - style.Tier * 4 - Random01() * 16),
                    style.MoteLifetime, 2.6f + style.Tier * 0.3f, style.Tier);
            }
        }
        else actor.MoteTime = 0;
    }

    public void EndFrame()
    {
        _expiredActors.Clear();
        foreach (var entry in _actors)
            if (entry.Value.Frame != _frame) _expiredActors.Add(entry.Key);
        foreach (int id in _expiredActors) Forget(id);
    }

    public void Forget(int id)
    {
        if (_actors.Remove(id)) RemoveOwnerParticles(id);
    }

    private void RemoveOwnerParticles(int id)
    {
        for (int i = _count - 1; i >= 0; i--)
            if (_particles[i].Owner == id) RemoveAt(i);
    }

    private void RemoveAt(int i) => _particles[i] = _particles[--_count];

    private void EmitStep(int id, Vector2 position, Vector2 direction, bool water, ref bool foot)
    {
        foot = !foot;
        Vector2 side = new Vector2(-direction.Y, direction.X) * (foot ? 4f : -4f);
        if (water)
        {
            Emit(id, ReactiveEffectKind.Wake, position, -direction * 4, 0.9f, 23);
            for (int i = 0; i < 3; i++)
                Emit(id, ReactiveEffectKind.Splash, position + side,
                    new Vector2((Random01() - 0.5f) * 28, -18 - Random01() * 14) - direction * 8, 0.5f, 1.5f);
        }
        else
        {
            for (int i = 0; i < 3; i++)
                Emit(id, ReactiveEffectKind.Dust, position + side,
                    new Vector2((Random01() - 0.5f) * 10, -3 - Random01() * 6) - direction * 7, 0.65f, 5 + Random01() * 3);
        }
    }

    private void Emit(int owner, ReactiveEffectKind kind, Vector2 origin, Vector2 velocity, float life, float size, int meditationTier = 0)
    {
        if (_count >= _limit) return;
        _particles[_count++] = new ReactiveParticle
        {
            Owner = owner, Kind = kind, Origin = origin, Velocity = velocity,
            MeditationTier = meditationTier,
            Lifetime = life, Size = size, Seed = Random01()
        };
    }

    private float Random01()
    {
        _random ^= _random << 13; _random ^= _random >> 17; _random ^= _random << 5;
        return (_random & 0xFFFFFF) / 16777216f;
    }
}
