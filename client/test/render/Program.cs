using System;
using ArgentumNextgen.Rendering;

static class Program
{
    static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
        Console.WriteLine("PASS: " + message);
    }
    static void Main()
    {
        WalkContinuityTests.Run();
        var roof = new RoofFadeState();
        roof.Reset(2);
        roof.Update(1, 1f);
        float before = roof.GetOpacity(1);
        roof.Update(2, 0.016f);
        Check(roof.GetOpacity(1) > before && roof.GetOpacity(1) < 0.3f,
            "Previous roof returns gradually when entering another building");
        Check(roof.GetOpacity(2) < 1f, "New building starts fading independently");
        roof.Update(1, 0.016f);
        Check(roof.GetOpacity(1) < 0.3f, "Rapid re-entry does not flash opaque");
        roof.Update(0, 1f);
        Check(roof.GetOpacity(1) == 1f && roof.GetOpacity(2) == 1f, "Outside restores all roofs");
        roof.Update(2, 1f);
        roof.Reset(1);
        Check(roof.GetOpacity(1) == 1f && roof.GetOpacity(2) == 1f, "Map change clears previous region IDs");
        var slow = new RoofFadeState();
        var fast = new RoofFadeState();
        slow.Reset(1); fast.Reset(1);
        for (int i = 0; i < 15; i++) slow.Update(1, 1f / 30);
        for (int i = 0; i < 120; i++) fast.Update(1, 1f / 240);
        Check(Math.Abs(slow.GetOpacity(1) - fast.GetOpacity(1)) < 0.0001f, "Roof transition matches at 30 and 240 FPS");
        Check(SceneryMath.TreeOpacity(0, 0, 128, 160, 64, 80, false, 0.47f) == 1f, "Trees behind the player stay opaque");
        Check(SceneryMath.TreeOpacity(0, 0, 128, 160, 300, 80, true, 0.47f) == 1f, "Non-overlapping canopies stay opaque");
        Check(Math.Abs(SceneryMath.TreeOpacity(0, 0, 128, 160, 64, 80, true, 0.47f) - 0.47f) < 0.001f, "Overlapping canopy respects transparency setting");
        float edge = SceneryMath.TreeOpacity(0, 0, 128, 160, 0, 80, true, 0.47f);
        Check(edge > 0.47f && edge < 1f, "Canopy edge fades continuously");
        for (int i = 0; i < 10000; i++)
            if (Math.Abs(SceneryMath.WindOffset(i / 60.0, 50, 50, 512)) > 2.601f)
                throw new Exception("Wind exceeded 2.6 pixels");
        Check(true, "Wind stays bounded for large sprites and long sessions");
        ReactiveEffectsTests.Run();
        foreach (var (level, tier) in new[] { (1, 0), (12, 0), (13, 1), (24, 1), (25, 2), (34, 2), (35, 3), (49, 3), (50, 4), (99, 4) })
            Check(MeditationStyle.ForLevel(level).Tier == tier, $"Meditation tier at level {level}");
        for (int level = 1; level <= 50; level++)
        {
            var beforeStyle = MeditationStyle.ForLevel(level - 1);
            var style = MeditationStyle.ForLevel(level);
            if (style.Radius < beforeStyle.Radius || style.MoteInterval > beforeStyle.MoteInterval)
                throw new Exception("Meditation progression decreased");
        }
        Check(ArgentumNextgen.Game.LevelUpShortcut.TryCommand(49, out var command, out _) && command == "/SUBIRNIVEL", "F9 requests a self level-up without a GM requirement");
        Check(!ArgentumNextgen.Game.LevelUpShortcut.TryCommand(50, out _, out _), "F9 stops at level 50");
        Check(!ArgentumNextgen.Game.LevelUpShortcut.TryCommand(0, out _, out _), "F9 requires an active character level");
    }
}
