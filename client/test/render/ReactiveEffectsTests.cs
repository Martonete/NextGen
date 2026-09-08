using System;
using System.Diagnostics;
using System.Numerics;
using ArgentumNextgen.Rendering;

static class ReactiveEffectsTests
{
    static void Check(bool condition, string label)
    {
        if (!condition) throw new Exception(label);
        Console.WriteLine("PASS: " + label);
    }
    static void Observe(ReactiveEffects fx, int id, float x, bool visible = true,
        bool water = false, bool meditate = false, uint hit = 0, bool particles = true)
        => fx.Observe(id, new Vector2(x, 1600), visible, water, !water, meditate, hit, particles, true);

    public static void Run()
    {
        var fx = new ReactiveEffects();
        fx.BeginFrame(0.016f, 512); Observe(fx, 1, 1600); fx.EndFrame();
        Check(fx.Count == 0, "Spawning/login creates no false trail or impact");
        fx.BeginFrame(0.2f, 512); Observe(fx, 1, 1636); fx.EndFrame();
        Check(fx.Count == 6, "Two footsteps are emitted over 36 pixels");
        fx.BeginFrame(0.01f, 512); Observe(fx, 1, 1636, false); fx.EndFrame();
        Check(fx.Count == 0 && fx.ActorCount == 0, "Invisibility immediately removes owned particles");
        fx.BeginFrame(0.016f, 512); Observe(fx, 1, 1600); fx.EndFrame();
        fx.BeginFrame(0.016f, 512); Observe(fx, 1, 2200); fx.EndFrame();
        Check(fx.Count == 0, "Teleport never emits a trail through walls");
        fx.BeginFrame(0.016f, 512); Observe(fx, 1, 2200, hit: 1); fx.EndFrame();
        Check(fx.Count == 10, "One combat event creates one spark burst");
        fx.BeginFrame(0.016f, 512); Observe(fx, 1, 2200, hit: 1); fx.EndFrame();
        Check(fx.Count == 10, "Repeated observation does not duplicate combat event");
        fx.BeginFrame(0.016f, 512); fx.EndFrame();
        Check(fx.Count == 0 && fx.ActorCount == 0, "Character removal clears particles and tracking");
        fx.BeginFrame(0.016f, 512); Observe(fx, 1, 1600); fx.EndFrame();
        fx.BeginFrame(0.016f, 512); Observe(fx, 1, 1600, water: true); fx.EndFrame();
        Check(fx.Count == 4 && fx.Particles[0].Kind == ReactiveEffectKind.Wake, "Entering water emits a ripple and droplets");
        fx.Clear();
        for (int i = 0; i < 120; i++)
        {
            fx.BeginFrame(1f / 60, 512); Observe(fx, 1, 1600, meditate: true); fx.EndFrame();
        }
        float charge = 0;
        foreach (var actor in fx.Actors) charge = actor.Meditation;
        Check(charge == 1, "Meditation charges fully without opacity oscillation");
        Check(fx.Count > 5, "Meditation emits ascending motes continuously");
        fx.BeginFrame(1f / 240, 512);
        fx.Observe(1, new Vector2(1600, 1600), true, false, true, true, 0, true, true, moving: true);
        fx.EndFrame();
        foreach (var actor in fx.Actors) charge = actor.Meditation;
        Check(charge == 0 && fx.Count == 0, "Movement start instantly removes sigil and motes before displacement");
        fx.BeginFrame(0.016f, 512); Observe(fx, 1, 1600, meditate: true); fx.EndFrame();
        foreach (var actor in fx.Actors) charge = actor.Meditation;
        Check(charge == 0 && fx.Count == 0, "Stopping before server acknowledgement does not restart meditation effects");
        fx.BeginFrame(0.016f, 512); Observe(fx, 1, 1600); fx.EndFrame();
        fx.BeginFrame(0.2f, 512); Observe(fx, 1, 1600, meditate: true); fx.EndFrame();
        Check(fx.Count > 0, "A new meditation session restores the effect after interruption");
        fx.BeginFrame(1f / 240, 512); Observe(fx, 1, 1600.1f, meditate: true); fx.EndFrame();
        foreach (var actor in fx.Actors) charge = actor.Meditation;
        Check(charge == 0 && fx.Count == 0, "Subpixel movement also interrupts meditation immediately");
        for (int i = 0; i < 100; i++)
        {
            fx.BeginFrame(1f / 60, 512); Observe(fx, 1, 1600); fx.EndFrame();
        }
        foreach (var actor in fx.Actors) charge = actor.Meditation;
        Check(charge == 0 && fx.Count == 0, "Ending meditation fades the sigil and expires all motes");
        fx.BeginFrame(0, 512); Observe(fx, 1, 1636, meditate: true, hit: 5); fx.EndFrame();
        Check(fx.Count == 0, "Paused simulation emits no events");
        fx.BeginFrame(3, 512); Observe(fx, 1, 1800); fx.EndFrame();
        Check(fx.Count == 0, "Long stalls discard missed emissions");

        int Walk(int fps)
        {
            var sim = new ReactiveEffects();
            sim.BeginFrame(0, 512); Observe(sim, 1, 1600); sim.EndFrame();
            for (int i = 1; i <= fps / 5; i++)
            {
                sim.BeginFrame(1f / fps, 512); Observe(sim, 1, 1600 + 180f * i / fps); sim.EndFrame();
            }
            return sim.Count;
        }
        Check(Walk(30) == Walk(240) && Walk(30) == 6, "Footstep density matches at 30 and 240 FPS");
        fx.Clear();
        fx.BeginFrame(0.016f, 64);
        for (int id = 1; id <= 400; id++) Observe(fx, id, 1600);
        fx.EndFrame();
        fx.BeginFrame(0.016f, 64);
        for (int id = 1; id <= 400; id++) Observe(fx, id, 1636, hit: 1);
        fx.EndFrame();
        Check(fx.ActorCount <= ReactiveEffects.ActorCapacity && fx.Count == 64, "Crowds obey actor and quality particle caps");
        fx.Clear();
        Check(fx.Count == 0 && fx.ActorCount == 0, "Map reset clears all effect state");

        // Warm the steady path, then measure without logging in the measured section.
        for (int i = 0; i < 500; i++)
        {
            fx.BeginFrame(0.016f, 512); Observe(fx, 1, 1600 + i % 2 * 18, meditate: false); fx.EndFrame();
        }
        var timer = Stopwatch.StartNew();
        long bytes = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 10000; i++)
        {
            fx.BeginFrame(0.016f, 512); Observe(fx, 1, 1600 + i % 2 * 18); fx.EndFrame();
        }
        long allocated = GC.GetAllocatedBytesForCurrentThread() - bytes;
        timer.Stop();
        Check(allocated == 0, "Steady emission/update allocates zero managed bytes (10,000 frames)");
        Console.WriteLine($"Simulation measurement: {timer.Elapsed.TotalMilliseconds:F2} ms / 10,000 frames; {allocated} bytes");
    }
}
