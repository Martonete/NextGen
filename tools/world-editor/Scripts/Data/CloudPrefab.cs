#nullable enable
using System.Collections.Generic;
using System.Text;
using Godot;

namespace AOWorldEditor.Data;

/// <summary>
/// Reusable "cloud cluster" prefab: style (color, density, size, speed)
/// PLUS a pattern of relative tile offsets that define the cloud's shape.
/// Saved by the user after painting a cloud formation they like, then
/// stamped onto the map at a click position.
///
/// Example: "Niebla DV" = green + dark + 24 tiles in an irregular blob.
/// Once saved, the user picks it and clicks on the map to paste the
/// whole blob at that anchor position (each relative tile is offset
/// from the click).
/// </summary>
public class CloudPrefab
{
    public string Name = "Nube";
    // Style (same as SmokePrefab / PaintedFogLayer)
    public int Density = 160;
    public int R = 128;
    public int G = 140;
    public int B = 160;
    public int SpeedX = 5;
    public int SpeedY = 2;
    public int Size = 512;
    /// <summary>Relative tile offsets from the anchor (click) position.
    /// (0,0) is the click tile. Other offsets are stamped around it.</summary>
    public List<Vector2I> RelativeTiles = new();

    public CloudPrefab Clone()
    {
        var c = new CloudPrefab
        {
            Name = Name,
            Density = Density,
            R = R, G = G, B = B,
            SpeedX = SpeedX, SpeedY = SpeedY,
            Size = Size,
        };
        c.RelativeTiles.AddRange(RelativeTiles);
        return c;
    }

    /// <summary>Serialize to the .aofog file format. Format:
    ///   Cloud=name|density|r|g|b|sx|sy|size
    ///   CT=dx,dy        (one line per relative tile)
    /// </summary>
    public string SerializeHeader() =>
        $"{Name.Replace('|', ' ')}|{Density}|{R}|{G}|{B}|{SpeedX}|{SpeedY}|{Size}";

    public string Serialize()
    {
        var sb = new StringBuilder();
        sb.Append("Cloud=").AppendLine(SerializeHeader());
        foreach (var t in RelativeTiles)
            sb.Append("CT=").Append(t.X).Append(',').AppendLine(t.Y.ToString());
        return sb.ToString();
    }

    public static CloudPrefab? TryParseHeader(string line)
    {
        var parts = line.Split('|');
        if (parts.Length < 7) return null;
        if (!int.TryParse(parts[1], out var d)) return null;
        if (!int.TryParse(parts[2], out var r)) return null;
        if (!int.TryParse(parts[3], out var g)) return null;
        if (!int.TryParse(parts[4], out var b)) return null;
        if (!int.TryParse(parts[5], out var sx)) return null;
        if (!int.TryParse(parts[6], out var sy)) return null;
        int size = 512;
        if (parts.Length >= 8) int.TryParse(parts[7], out size);
        if (size <= 0) size = 512;
        return new CloudPrefab
        {
            Name = parts[0], Density = d, R = r, G = g, B = b,
            SpeedX = sx, SpeedY = sy, Size = size,
        };
    }

    /// <summary>Built-in starter cloud prefabs. 3 small example blobs the
    /// user can use as starting points before designing their own.</summary>
    public static readonly List<CloudPrefab> BuiltIn = MakeBuiltIns();

    private static List<CloudPrefab> MakeBuiltIns()
    {
        // Small roundish blob (radius 2)
        var small = new CloudPrefab
        {
            Name = "Nube pequeña",
            Density = 160, R = 200, G = 200, B = 220,
            SpeedX = 4, SpeedY = 2, Size = 512,
        };
        for (int dy = -2; dy <= 2; dy++)
            for (int dx = -2; dx <= 2; dx++)
                if (dx * dx + dy * dy <= 4) small.RelativeTiles.Add(new Vector2I(dx, dy));

        // Medium stretched cloud (5×3 ellipse)
        var medium = new CloudPrefab
        {
            Name = "Nube mediana",
            Density = 180, R = 180, G = 180, B = 200,
            SpeedX = 5, SpeedY = 2, Size = 768,
        };
        for (int dy = -2; dy <= 2; dy++)
            for (int dx = -4; dx <= 4; dx++)
            {
                float nx = dx / 4f, ny = dy / 2f;
                if (nx * nx + ny * ny <= 1.0f) medium.RelativeTiles.Add(new Vector2I(dx, dy));
            }

        // Large dense mass (radius 5)
        var large = new CloudPrefab
        {
            Name = "Nube grande",
            Density = 220, R = 120, G = 130, B = 150,
            SpeedX = 3, SpeedY = 1, Size = 1024,
        };
        for (int dy = -5; dy <= 5; dy++)
            for (int dx = -5; dx <= 5; dx++)
                if (dx * dx + dy * dy <= 25) large.RelativeTiles.Add(new Vector2I(dx, dy));

        return new List<CloudPrefab> { small, medium, large };
    }
}
