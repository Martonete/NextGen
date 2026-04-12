#nullable enable
using System.Globalization;

namespace AOWorldEditor.Data;

/// <summary>Type of light source. Affects how the renderer spawns the
/// Godot 2D light node and how shadows are cast.</summary>
public enum LightType
{
    Omni = 0,        // PointLight2D — radiates in all directions
    Spot = 1,        // PointLight2D with directional texture cone
    Directional = 2, // DirectionalLight2D — infinite parallel rays (sun)
}

/// <summary>
/// One advanced light source placed on the map. This is the data model for
/// the NEW light system, completely independent of the legacy primitive
/// per-tile light data carried by <see cref="MapData"/>. Plain primitives
/// only — no Godot types — so the renderer can interpret the data freely.
///
/// Coordinates are 1-indexed (VB6 convention used across the codebase).
/// </summary>
public class MapLight
{
    public int X;
    public int Y;
    public LightType Type = LightType.Omni;

    // Color, 0..255 channels. Default is a warm torch-like glow.
    public int R = 255;
    public int G = 220;
    public int B = 180;

    /// <summary>Light2D.energy multiplier, 0..10.</summary>
    public float Energy = 1.0f;

    /// <summary>Visible radius, in tiles.</summary>
    public float Radius = 6.0f;

    /// <summary>Aim angle in degrees (0..359). 0 = pointing right/east.
    /// Used by <see cref="LightType.Spot"/> and <see cref="LightType.Directional"/>.</summary>
    public float DirectionDeg = 0.0f;

    /// <summary>Full cone angle in degrees (10..180). Spot only.</summary>
    public float ConeDegrees = 60.0f;

    /// <summary>Random energy jitter per frame, percent (0..100). For candles.</summary>
    public int FlickerPct = 0;

    /// <summary>Sinusoidal energy modulation in Hz (0..10). For magical pulsing lights.</summary>
    public float PulseHz = 0.0f;

    public bool ShadowsEnabled = true;

    /// <summary>Optional user label.</summary>
    public string Name = "";

    /// <summary>Deep copy.</summary>
    public MapLight Clone() => new MapLight
    {
        X = X,
        Y = Y,
        Type = Type,
        R = R, G = G, B = B,
        Energy = Energy,
        Radius = Radius,
        DirectionDeg = DirectionDeg,
        ConeDegrees = ConeDegrees,
        FlickerPct = FlickerPct,
        PulseHz = PulseHz,
        ShadowsEnabled = ShadowsEnabled,
        Name = Name,
    };

    /// <summary>Serialize to a single .aolight line.
    /// Format (14 fields):
    /// x|y|type|r|g|b|energy|radius|dir|cone|flicker|pulse|shadows|name
    /// Floats are formatted with InvariantCulture "G". Pipes in <see cref="Name"/>
    /// are replaced with spaces for safety. Shadows are serialized as "0" or "1".</summary>
    public string Serialize()
    {
        var ic = CultureInfo.InvariantCulture;
        string safeName = Name.Replace('|', ' ');
        return string.Join("|",
            X.ToString(ic),
            Y.ToString(ic),
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
            ShadowsEnabled ? "1" : "0",
            safeName);
    }

    /// <summary>Backward-compatible parser. If fewer than 14 fields are present,
    /// missing trailing fields fall back to defaults. Returns null only when the
    /// x/y coordinates can't be parsed at all (the minimum required for placement).</summary>
    public static MapLight? TryParse(string line)
    {
        if (string.IsNullOrEmpty(line)) return null;
        var parts = line.Split('|');
        if (parts.Length < 2) return null;

        var ic = CultureInfo.InvariantCulture;

        if (!int.TryParse(parts[0], NumberStyles.Integer, ic, out int x)) return null;
        if (!int.TryParse(parts[1], NumberStyles.Integer, ic, out int y)) return null;

        var l = new MapLight { X = x, Y = y };

        if (parts.Length >= 3 && int.TryParse(parts[2], NumberStyles.Integer, ic, out int t))
            l.Type = (LightType)t;

        if (parts.Length >= 4 && int.TryParse(parts[3], NumberStyles.Integer, ic, out int r)) l.R = r;
        if (parts.Length >= 5 && int.TryParse(parts[4], NumberStyles.Integer, ic, out int g)) l.G = g;
        if (parts.Length >= 6 && int.TryParse(parts[5], NumberStyles.Integer, ic, out int b)) l.B = b;

        if (parts.Length >= 7 && float.TryParse(parts[6], NumberStyles.Float, ic, out float energy))
            l.Energy = energy;
        if (parts.Length >= 8 && float.TryParse(parts[7], NumberStyles.Float, ic, out float radius))
            l.Radius = radius;
        if (parts.Length >= 9 && float.TryParse(parts[8], NumberStyles.Float, ic, out float dir))
            l.DirectionDeg = dir;
        if (parts.Length >= 10 && float.TryParse(parts[9], NumberStyles.Float, ic, out float cone))
            l.ConeDegrees = cone;

        if (parts.Length >= 11 && int.TryParse(parts[10], NumberStyles.Integer, ic, out int flicker))
            l.FlickerPct = flicker;
        if (parts.Length >= 12 && float.TryParse(parts[11], NumberStyles.Float, ic, out float pulse))
            l.PulseHz = pulse;

        if (parts.Length >= 13)
            l.ShadowsEnabled = parts[12] == "1";

        // Name may itself be empty; if more than 14 parts (e.g. user typed
        // pipes that survived sanitization round-trip in some other tool),
        // join the tail back together so we don't lose data.
        if (parts.Length >= 14)
            l.Name = parts.Length == 14 ? parts[13] : string.Join("|", parts, 13, parts.Length - 13);

        return l;
    }
}
