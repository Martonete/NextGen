#nullable enable
using System.Collections.Generic;
using Godot;

namespace AOWorldEditor.Data;

/// <summary>
/// One independent painted-smoke "layer" on the map. Has its own style
/// (color, density, speed) AND its own set of tiles. Changing a layer's
/// style ONLY affects tiles in that layer — other layers are unaffected.
///
/// The user creates a new layer by picking a smoke prefab from the panel
/// and painting; each paint goes into the currently-active layer. Users
/// can have as many layers as they want on a map (different colors in
/// different areas: red fire smoke here, blue mist there, green poison
/// cloud over there — all independent).
/// </summary>
public class PaintedFogLayer
{
    public string Name = "Humo";
    public int Density = 160;
    public int R = 128;
    public int G = 140;
    public int B = 160;
    public int SpeedX = 5;
    public int SpeedY = 2;
    public HashSet<Vector2I> Tiles = new();

    /// <summary>Seed a new layer from a prefab template.</summary>
    public static PaintedFogLayer FromPrefab(SmokePrefab p) => new PaintedFogLayer
    {
        Name = p.Name,
        Density = p.Density,
        R = p.R,
        G = p.G,
        B = p.B,
        SpeedX = p.SpeedX,
        SpeedY = p.SpeedY,
    };

    public void CopyStyleFrom(SmokePrefab p)
    {
        Name = p.Name;
        Density = p.Density;
        R = p.R;
        G = p.G;
        B = p.B;
        SpeedX = p.SpeedX;
        SpeedY = p.SpeedY;
    }

    /// <summary>Serialize style header to a single .aofog line.
    /// Format: name|density|r|g|b|sx|sy</summary>
    public string SerializeStyle() => $"{Name.Replace('|', ' ')}|{Density}|{R}|{G}|{B}|{SpeedX}|{SpeedY}";

    public static PaintedFogLayer? TryParseStyle(string line)
    {
        var parts = line.Split('|');
        if (parts.Length < 7) return null;
        if (!int.TryParse(parts[1], out var d)) return null;
        if (!int.TryParse(parts[2], out var r)) return null;
        if (!int.TryParse(parts[3], out var g)) return null;
        if (!int.TryParse(parts[4], out var b)) return null;
        if (!int.TryParse(parts[5], out var sx)) return null;
        if (!int.TryParse(parts[6], out var sy)) return null;
        return new PaintedFogLayer
        {
            Name = parts[0], Density = d, R = r, G = g, B = b, SpeedX = sx, SpeedY = sy,
        };
    }
}
