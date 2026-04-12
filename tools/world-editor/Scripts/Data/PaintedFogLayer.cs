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
    /// <summary>Optional group name (like a Photoshop folder). Empty string = root.
    /// Layers with the same Group are rendered under a collapsible header in the
    /// HumoLayersPanel. Example: "Dungeon Veril".</summary>
    public string Group = "";
    public int Density = 160;
    public int R = 128;
    public int G = 140;
    public int B = 160;
    public int SpeedX = 5;
    public int SpeedY = 2;

    /// <summary>Noise cell size in world pixels — controls the visual scale
    /// of the smoke pattern. Larger = bigger, softer clouds. Smaller =
    /// denser, more granular. 512 is the baseline. Range: 64..2048.</summary>
    public int Size = 512;

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
        Size = p.Size,
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
        Size = p.Size;
    }

    /// <summary>Serialize style header to a single .aofog line.
    /// Format: name|density|r|g|b|sx|sy|group|size
    /// Size is optional for backward compat (older files default to 512).</summary>
    public string SerializeStyle() =>
        $"{Name.Replace('|', ' ')}|{Density}|{R}|{G}|{B}|{SpeedX}|{SpeedY}|{Group.Replace('|', ' ')}|{Size}";

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
        string group = parts.Length >= 8 ? parts[7] : "";
        // Size is optional — older files only had 8 fields. Pre-seeded
        // default means discarding TryParse's bool is intentional.
        int size = 512;
        if (parts.Length >= 9) int.TryParse(parts[8], out size);
        if (size <= 0) size = 512;
        return new PaintedFogLayer
        {
            Name = parts[0], Density = d, R = r, G = g, B = b, SpeedX = sx, SpeedY = sy,
            Group = group, Size = size,
        };
    }
}
