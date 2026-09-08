using System;

namespace ArgentumNextgen.Rendering;

/// <summary>Shared level boundaries and emission/drawing budget for each meditation.</summary>
public readonly record struct MeditationStyle(int Tier, float Radius, int Runes, float MoteInterval, float MoteLifetime)
{
    public static MeditationStyle ForLevel(int level) => Math.Clamp(level, 1, 50) switch
    {
        < 13 => new(0, 31, 8, 0.07f, 1.2f),
        < 25 => new(1, 38, 10, 0.055f, 1.4f),
        < 35 => new(2, 45, 12, 0.045f, 1.6f),
        < 50 => new(3, 53, 16, 0.032f, 1.8f),
        _ => new(4, 65, 20, 0.02f, 2.1f)
    };
}
