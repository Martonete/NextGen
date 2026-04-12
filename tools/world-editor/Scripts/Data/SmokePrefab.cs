#nullable enable
using System.Collections.Generic;

namespace AOWorldEditor.Data;

/// <summary>
/// Named fog/smoke style preset. Users can pick one from the HumoConfigPanel
/// dropdown to apply its color/density/speed to the active paint style, or
/// save their current style as a new named prefab.
/// </summary>
public class SmokePrefab
{
    public string Name = "Humo";
    public int Density = 160;
    public int R = 128;
    public int G = 140;
    public int B = 160;
    public int SpeedX = 5;
    public int SpeedY = 2;

    /// <summary>Built-in prefabs that are always available in the dropdown,
    /// independent of the current map. Users pick them as starting points
    /// or use them directly.</summary>
    public static readonly List<SmokePrefab> BuiltIn = new()
    {
        new SmokePrefab { Name = "Humo ligero gris", Density = 120, R = 200, G = 200, B = 210, SpeedX = 4, SpeedY = 2 },
        new SmokePrefab { Name = "Niebla gris",      Density = 160, R = 128, G = 140, B = 160, SpeedX = 5, SpeedY = 2 },
        new SmokePrefab { Name = "Vapor de agua",    Density = 140, R = 180, G = 200, B = 220, SpeedX = 6, SpeedY = 3 },
        new SmokePrefab { Name = "Humo denso oscuro", Density = 220, R = 60,  G = 60,  B = 60,  SpeedX = 3, SpeedY = 1 },
        new SmokePrefab { Name = "Niebla nocturna",  Density = 200, R = 50,  G = 60,  B = 80,  SpeedX = 4, SpeedY = 2 },
        new SmokePrefab { Name = "Polvo desierto",   Density = 130, R = 180, G = 160, B = 120, SpeedX = 8, SpeedY = 1 },
        new SmokePrefab { Name = "Humo infernal",    Density = 200, R = 180, G = 60,  B = 40,  SpeedX = 5, SpeedY = 3 },
        new SmokePrefab { Name = "Niebla mágica",    Density = 150, R = 160, G = 100, B = 220, SpeedX = 3, SpeedY = 2 },
    };

    public SmokePrefab Clone() => new SmokePrefab
    {
        Name = Name,
        Density = Density,
        R = R, G = G, B = B,
        SpeedX = SpeedX, SpeedY = SpeedY,
    };

    /// <summary>Serialize to a single line for .aofog storage: name|density|r|g|b|sx|sy</summary>
    public string Serialize() => $"{Name.Replace('|', ' ')}|{Density}|{R}|{G}|{B}|{SpeedX}|{SpeedY}";

    public static SmokePrefab? TryParse(string line)
    {
        var parts = line.Split('|');
        if (parts.Length < 7) return null;
        if (!int.TryParse(parts[1], out var d)) return null;
        if (!int.TryParse(parts[2], out var r)) return null;
        if (!int.TryParse(parts[3], out var g)) return null;
        if (!int.TryParse(parts[4], out var b)) return null;
        if (!int.TryParse(parts[5], out var sx)) return null;
        if (!int.TryParse(parts[6], out var sy)) return null;
        return new SmokePrefab { Name = parts[0], Density = d, R = r, G = g, B = b, SpeedX = sx, SpeedY = sy };
    }
}
