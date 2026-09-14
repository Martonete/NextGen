using System;

namespace ArgentumNextgen.Game;

/// <summary>AO20 <c>MainTimer.cls</c>: the client-side action timers. Each timer is a
/// start tick plus an interval; <see cref="Check"/> answers "has the interval elapsed?" and
/// (by default) restarts it. Intervals come from the server's <c>Intervals</c> packet.</summary>
public enum TimersIndex
{
    Attack = 1,
    UseItemWithU = 2,
    UseItemWithDblClick = 3,
    SendRPU = 4,
    CastSpell = 5,
    Arrows = 6,
    CastAttack = 7,
    AttackSpell = 8,
    AttackUse = 9,
    Walk = 10,
    Drop = 11,
}

/// <summary>Values of AO20's <c>t_Intervals</c> (<c>gIntervals</c>), milliseconds.</summary>
public sealed class GameIntervals
{
    public int Hit = 1165, Bow = 1200, Magic = 1230, ExtractWork = 4000, BuildWork = 500, Walk = 210,
        DropItem = 400, UseItemKey = 380, UseItemClick = 276, HitMagic = 800, MagicHit = 800,
        HitUseItem = 800, Hide = 500, Talk = 300, LeftClick = 80, Meditate = 10;
}

public sealed class MainTimer
{
    private const int Count = 11;
    /// <summary>AO20 <c>INT_SENTRPU</c>: request-position-update cooldown.</summary>
    public const int IntSentRpu = 5000;

    private struct Timer { public long StartTick; public long Interval; public bool Run; }
    private readonly Timer[] _timers = new Timer[Count + 1];

    /// <summary>Clock source (ms). Defaults to the process tick counter; tests inject their own.</summary>
    public Func<long> Now = () => Environment.TickCount64;

    /// <summary>Starts with the AO20 default intervals so actions work even before the
    /// server's Intervals packet arrives (or against a server that never sends it).</summary>
    public MainTimer() => ApplyIntervals(new GameIntervals());

    public void SetInterval(TimersIndex index, long intervalMs)
    {
        int i = (int)index;
        if (i < 1 || i > Count) return;
        _timers[i].Interval = intervalMs;
    }

    public long GetInterval(TimersIndex index)
    {
        int i = (int)index;
        return i < 1 || i > Count ? 0 : _timers[i].Interval;
    }

    public void Start(TimersIndex index)
    {
        int i = (int)index;
        if (i < 1 || i > Count) return;
        _timers[i].Run = true;
    }

    public void Pause(TimersIndex index)
    {
        int i = (int)index;
        if (i < 1 || i > Count) return;
        _timers[i].Run = false;
    }

    /// <summary>AO20 <c>Check</c>: true when the interval has elapsed since the last restart.
    /// A timer that was never started always answers false, exactly like the VB6 class.</summary>
    public bool Check(TimersIndex index, bool restart = true)
    {
        int i = (int)index;
        if (i < 1 || i > Count) return false;
        ref var t = ref _timers[i];
        if (!t.Run) return false;
        long now = Now();
        if (now - t.StartTick >= t.Interval)
        {
            if (restart) t.StartTick = now;
            return true;
        }
        return false;
    }

    public void Restart(TimersIndex index)
    {
        int i = (int)index;
        if (i < 1 || i > Count) return;
        _timers[i].StartTick = Now();
    }

    /// <summary>AO20 <c>HandleIntervals</c> tail: set every interval and start the timers.</summary>
    public void ApplyIntervals(GameIntervals iv)
    {
        SetInterval(TimersIndex.Attack, iv.Hit);
        SetInterval(TimersIndex.Arrows, iv.Bow);
        SetInterval(TimersIndex.CastSpell, iv.Magic);
        SetInterval(TimersIndex.UseItemWithU, iv.UseItemKey);
        SetInterval(TimersIndex.UseItemWithDblClick, iv.UseItemClick);
        SetInterval(TimersIndex.SendRPU, IntSentRpu);
        SetInterval(TimersIndex.CastAttack, iv.MagicHit);
        SetInterval(TimersIndex.AttackSpell, iv.HitMagic);
        SetInterval(TimersIndex.AttackUse, iv.HitUseItem);
        SetInterval(TimersIndex.Drop, iv.DropItem);
        SetInterval(TimersIndex.Walk, iv.Walk);
        for (int i = 1; i <= Count; i++) _timers[i].Run = true;
    }

    /// <summary>AO20 <c>HandleVelocidadToggle</c>: the walk timer scales with speed.</summary>
    public void ApplyWalkSpeed(int walkIntervalMs, float speeding)
    {
        const float lowestWalkInterval = 0.0000001f;
        SetInterval(TimersIndex.Walk, (long)(walkIntervalMs / (speeding > 0 ? speeding : lowestWalkInterval)));
    }
}
