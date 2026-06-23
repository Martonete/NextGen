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

    /// <summary>Noise cell size in world pixels — controls how big the smoke
    /// "clouds" look on screen. 512 = baseline, 256 = smaller/denser,
    /// 1024 = larger/softer. Range: 64..2048.</summary>
    public int Size = 512;

    /// <summary>Built-in prefabs that are always available in the dropdown,
    /// independent of the current map. Users pick them as starting points
    /// or use them directly.</summary>
    public static readonly List<SmokePrefab> BuiltIn = new()
    {
        new SmokePrefab { Name = "Humo ligero gris", Density = 120, R = 200, G = 200, B = 210, SpeedX = 4, SpeedY = 2, Size = 512 },
        new SmokePrefab { Name = "Niebla gris",      Density = 160, R = 128, G = 140, B = 160, SpeedX = 5, SpeedY = 2, Size = 512 },
        new SmokePrefab { Name = "Vapor de agua",    Density = 140, R = 180, G = 200, B = 220, SpeedX = 6, SpeedY = 3, Size = 384 },
        new SmokePrefab { Name = "Humo denso oscuro", Density = 220, R = 60,  G = 60,  B = 60,  SpeedX = 3, SpeedY = 1, Size = 768 },
        new SmokePrefab { Name = "Niebla nocturna",  Density = 200, R = 50,  G = 60,  B = 80,  SpeedX = 4, SpeedY = 2, Size = 640 },
        new SmokePrefab { Name = "Polvo desierto",   Density = 130, R = 180, G = 160, B = 120, SpeedX = 8, SpeedY = 1, Size = 256 },
        new SmokePrefab { Name = "Humo infernal",    Density = 200, R = 180, G = 60,  B = 40,  SpeedX = 5, SpeedY = 3, Size = 512 },
        new SmokePrefab { Name = "Niebla mágica",    Density = 150, R = 160, G = 100, B = 220, SpeedX = 3, SpeedY = 2, Size = 1024 },
    };

    public SmokePrefab Clone() => new SmokePrefab
    {
        Name = Name,
        Density = Density,
        R = R, G = G, B = B,
        SpeedX = SpeedX, SpeedY = SpeedY,
        Size = Size,
    };

    /// <summary>Serialize to a single line for .aofog storage.
    /// Format: name|density|r|g|b|sx|sy|size  (size is optional for
    /// backward compat with pre-size files).</summary>
    public string Serialize() => $"{Name.Replace('|', ' ')}|{Density}|{R}|{G}|{B}|{SpeedX}|{SpeedY}|{Size}";

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
        // Size is optional — old files (pre-size) have only 7 fields. When
        // present but unparseable, TryParse leaves `size` at the default 512
        // (the local variable is pre-seeded so discarding the bool is safe).
        int size = 512;
        if (parts.Length >= 8) int.TryParse(parts[7], out size);
        if (size <= 0) size = 512;
        return new SmokePrefab
        {
            Name = parts[0], Density = d, R = r, G = g, B = b,
            SpeedX = sx, SpeedY = sy, Size = size,
        };
    }
}
