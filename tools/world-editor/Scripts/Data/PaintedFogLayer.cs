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

    /// <summary>Random seed (0..9999) used by the shader to offset this
    /// layer's noise sample and rotate its drift direction slightly.
    /// Without it, multiple layers with identical settings move in unison
    /// (same wind direction, same noise pattern). Set automatically when
    /// a new layer is created (NewRandomSeed); 0 = no offset for backward
    /// compat with .aofog files written before this field existed.</summary>
    public int RandomSeed = 0;

    public HashSet<Vector2I> Tiles = new();

    /// <summary>Generate a new random seed in [1, 9999]. Use when creating
    /// a fresh layer so it doesn't sync visually with existing ones.</summary>
    public static int NewRandomSeed() => 1 + (int)(GD.Randi() % 9999);

    /// <summary>Seed a new layer from a prefab template. A fresh
    /// RandomSeed is assigned so this layer doesn't visually sync with
    /// any other layer that happens to share the same style.</summary>
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
        RandomSeed = NewRandomSeed(),
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
    /// Format: name|density|r|g|b|sx|sy|group|size|seed
    /// Trailing fields (size, seed) are optional for backward compat.</summary>
    public string SerializeStyle() =>
        $"{Name.Replace('|', ' ')}|{Density}|{R}|{G}|{B}|{SpeedX}|{SpeedY}|{Group.Replace('|', ' ')}|{Size}|{RandomSeed}";

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
        int size = 512;
        if (parts.Length >= 9) int.TryParse(parts[8], out size);
        if (size <= 0) size = 512;
        // RandomSeed is optional — old files without it get a freshly
        // generated seed so they immediately benefit from de-syncing.
        int seed;
        if (parts.Length < 10 || !int.TryParse(parts[9], out seed)) seed = NewRandomSeed();
        return new PaintedFogLayer
        {
            Name = parts[0], Density = d, R = r, G = g, B = b, SpeedX = sx, SpeedY = sy,
            Group = group, Size = size, RandomSeed = seed,
        };
    }
}
