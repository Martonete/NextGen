#nullable enable
using System.Collections.Generic;
using System.Globalization;

namespace AOWorldEditor.Data;

/// <summary>
/// Named reusable light preset, the light-system analogue of <see cref="SmokePrefab"/>.
/// Users pick a preset from the panel to seed a new <see cref="MapLight"/>, or save
/// a tweaked light back as a custom user asset. Plain primitives only — no Godot
/// types — so the data is portable and serializable.
/// </summary>
public class LightAsset
{
    public string Name = "Antorcha";
    public LightType Type = LightType.Omni;
    public int R = 255;
    public int G = 180;
    public int B = 80;
    public float Energy = 1.2f;
    public float Radius = 5.0f;
    public float DirectionDeg = 0f;
    public float ConeDegrees = 60f;
    public int FlickerPct = 35;
    public float PulseHz = 0f;
    public bool ShadowsEnabled = true;

    /// <summary>Built-in presets that are always available in the dropdown,
    /// independent of the current map. Users pick them as starting points or
    /// place them directly.</summary>
    public static readonly List<LightAsset> BuiltIn = new()
    {
        new LightAsset { Name = "Antorcha",            Type = LightType.Omni,        R = 255, G = 180, B = 80,  Energy = 1.2f, Radius = 5f,  DirectionDeg = 0f,   ConeDegrees = 60f, FlickerPct = 35, PulseHz = 0f,    ShadowsEnabled = true },
        new LightAsset { Name = "Vela",                Type = LightType.Omni,        R = 255, G = 220, B = 140, Energy = 0.6f, Radius = 2f,  DirectionDeg = 0f,   ConeDegrees = 60f, FlickerPct = 55, PulseHz = 0f,    ShadowsEnabled = true },
        new LightAsset { Name = "Fogata",              Type = LightType.Omni,        R = 255, G = 140, B = 40,  Energy = 1.6f, Radius = 6f,  DirectionDeg = 0f,   ConeDegrees = 60f, FlickerPct = 45, PulseHz = 0f,    ShadowsEnabled = true },
        new LightAsset { Name = "Lámpara mágica azul", Type = LightType.Omni,        R = 120, G = 180, B = 255, Energy = 1.4f, Radius = 7f,  DirectionDeg = 0f,   ConeDegrees = 60f, FlickerPct = 0,  PulseHz = 1.5f,  ShadowsEnabled = true },
        new LightAsset { Name = "Reflector",           Type = LightType.Spot,        R = 255, G = 255, B = 255, Energy = 2.0f, Radius = 14f, DirectionDeg = 0f,   ConeDegrees = 40f, FlickerPct = 0,  PulseHz = 0f,    ShadowsEnabled = true },
        new LightAsset { Name = "Luna",                Type = LightType.Directional, R = 180, G = 200, B = 230, Energy = 0.5f, Radius = 5f,  DirectionDeg = 270f, ConeDegrees = 60f, FlickerPct = 0,  PulseHz = 0f,    ShadowsEnabled = true },
        new LightAsset { Name = "Sol",                 Type = LightType.Directional, R = 255, G = 245, B = 220, Energy = 1.0f, Radius = 5f,  DirectionDeg = 270f, ConeDegrees = 60f, FlickerPct = 0,  PulseHz = 0f,    ShadowsEnabled = true },
        new LightAsset { Name = "Runa infernal",       Type = LightType.Omni,        R = 255, G = 60,  B = 30,  Energy = 1.0f, Radius = 4f,  DirectionDeg = 0f,   ConeDegrees = 60f, FlickerPct = 20, PulseHz = 2.5f,  ShadowsEnabled = true },
    };

    public LightAsset Clone() => new LightAsset
    {
        Name = Name,
        Type = Type,
        R = R, G = G, B = B,
        Energy = Energy,
        Radius = Radius,
        DirectionDeg = DirectionDeg,
        ConeDegrees = ConeDegrees,
        FlickerPct = FlickerPct,
        PulseHz = PulseHz,
        ShadowsEnabled = ShadowsEnabled,
    };

    /// <summary>Serialize to a single .aolight line.
    /// Format (12 fields, no x/y):
    /// name|type|r|g|b|energy|radius|dir|cone|flicker|pulse|shadows
    /// Pipes in <see cref="Name"/> are replaced with spaces. Floats use
    /// InvariantCulture "G" formatting.</summary>
    public string Serialize()
    {
        var ic = CultureInfo.InvariantCulture;
        string safeName = Name.Replace('|', ' ');
        return string.Join("|",
            safeName,
            ((int)Type).ToString(ic),
            R.ToString(ic),
            G.ToString(ic),
            B.ToString(ic),
            Energy.ToString("G", ic),
            Radius.ToString("G", ic),
            DirectionDeg.ToString("G", ic),
            ConeDegrees.ToString("G", ic),
            FlickerPct.ToString(ic),
            PulseHz.ToString("G", ic),
            ShadowsEnabled ? "1" : "0");
    }

    /// <summary>Backward-compatible parser. Missing trailing fields fall back to
    /// the constructor defaults. Returns null only when the line is empty.</summary>
    public static LightAsset? TryParse(string line)
    {
        if (string.IsNullOrEmpty(line)) return null;
        var parts = line.Split('|');
        if (parts.Length < 1) return null;

        var ic = CultureInfo.InvariantCulture;
        var a = new LightAsset { Name = parts[0] };

        if (parts.Length >= 2 && int.TryParse(parts[1], NumberStyles.Integer, ic, out int t))
            a.Type = (LightType)t;
        if (parts.Length >= 3 && int.TryParse(parts[2], NumberStyles.Integer, ic, out int r)) a.R = r;
        if (parts.Length >= 4 && int.TryParse(parts[3], NumberStyles.Integer, ic, out int g)) a.G = g;
        if (parts.Length >= 5 && int.TryParse(parts[4], NumberStyles.Integer, ic, out int b)) a.B = b;
        if (parts.Length >= 6 && float.TryParse(parts[5], NumberStyles.Float, ic, out float energy))
            a.Energy = energy;
        if (parts.Length >= 7 && float.TryParse(parts[6], NumberStyles.Float, ic, out float radius))
            a.Radius = radius;
        if (parts.Length >= 8 && float.TryParse(parts[7], NumberStyles.Float, ic, out float dir))
            a.DirectionDeg = dir;
        if (parts.Length >= 9 && float.TryParse(parts[8], NumberStyles.Float, ic, out float cone))
            a.ConeDegrees = cone;
        if (parts.Length >= 10 && int.TryParse(parts[9], NumberStyles.Integer, ic, out int flicker))
            a.FlickerPct = flicker;
        if (parts.Length >= 11 && float.TryParse(parts[10], NumberStyles.Float, ic, out float pulse))
            a.PulseHz = pulse;
        if (parts.Length >= 12)
            a.ShadowsEnabled = parts[11] == "1";

        return a;
    }
}
