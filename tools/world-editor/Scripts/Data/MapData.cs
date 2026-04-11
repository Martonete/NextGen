#nullable enable
using System.Collections.Generic;
using System.IO;
using System.Text;
using Godot;

namespace AOWorldEditor.Data;

/// <summary>
/// Map tile data. Mirrors the client MapTile but with editor-specific additions.
/// </summary>
public struct MapTile
{
    public bool Blocked;
    public int Layer1;  // Ground terrain GRH
    public int Layer2;  // Mask/overlay GRH (transitions, details)
    public int Layer3;  // Objects/trees GRH
    public int Layer4;  // Roof GRH
    public short Trigger; // 0=None, 1=Indoor, 3=InvalidPos, 4=SafeZone, 5=AntiBlock, 6=CombatZone
    public short ParticleGroup;
    public short LightRange;
    public short LightR;
    public short LightG;
    public short LightB;

    // .inf data
    public short ExitMap;
    public short ExitX;
    public short ExitY;
    public short NpcIndex;
    public short ObjIndex;
    public short ObjAmount;

    public bool HasLight => LightRange > 0;
    public bool HasExit => ExitMap > 0;
    public bool HasNpc => NpcIndex > 0;
    public bool HasObject => ObjIndex > 0;
}

/// <summary>
/// Represents a loaded/editable map with variable dimensions.
/// Standard AO maps are 100x100, but the editor supports larger sizes.
/// Tiles are 1-indexed (VB6 convention): [1..Width, 1..Height].
/// </summary>
public class MapData
{
    public int Width { get; private set; }
    public int Height { get; private set; }
    public MapTile[,] Tiles { get; private set; }

    // Metadata (.dat)
    public string Name = "";
    public int MusicNum;
    public bool PkEnabled; // PK=0 means PvP on (inverted in .dat)
    public bool BackUp = true;
    public string Terreno = "TIERRA";
    public string Zona = "CAMPO";
    public byte AmbientR = 180;
    public byte AmbientG = 180;
    public byte AmbientB = 180;

    public int MapNumber; // The map file number (e.g. 1 for Mapa1.map)

    // ── Painted fog (per-tile click-to-place blobs) ──
    // Each entry is a tile coord (x, y) that has been marked by the Fog paint tool.
    // Rendered as a soft world-space blob centered on that tile.
    public HashSet<Vector2I> PaintedFogTiles = new();
    // Shared style for painted fog (one set per map — users can tune once).
    public int PaintedFogDensity = 90;
    public int PaintedFogR = 128;
    public int PaintedFogG = 140;
    public int PaintedFogB = 160;
    public int PaintedFogSpeedX = 5;
    public int PaintedFogSpeedY = 2;

    public MapData(int width = 100, int height = 100)
    {
        Width = width;
        Height = height;
        Tiles = new MapTile[width + 1, height + 1]; // 1-indexed
    }

    /// <summary>
    /// Resize the map, preserving existing tile data where possible.
    /// </summary>
    public void Resize(int newWidth, int newHeight)
    {
        var newTiles = new MapTile[newWidth + 1, newHeight + 1];
        int copyW = System.Math.Min(Width, newWidth);
        int copyH = System.Math.Min(Height, newHeight);

        for (int y = 1; y <= copyH; y++)
            for (int x = 1; x <= copyW; x++)
                newTiles[x, y] = Tiles[x, y];

        Width = newWidth;
        Height = newHeight;
        Tiles = newTiles;
    }

    public bool InBounds(int x, int y) => x >= 1 && x <= Width && y >= 1 && y <= Height;

    /// <summary>Save painted fog data to `Mapa{N}.aofog` in the given directory.
    /// If no tiles are painted, any existing file is deleted so the map stays clean.</summary>
    public void SavePaintedFog(string dir)
    {
        if (string.IsNullOrEmpty(dir) || MapNumber <= 0) return;
        string path = Path.Combine(dir, $"Mapa{MapNumber}.aofog");
        if (PaintedFogTiles.Count == 0)
        {
            if (File.Exists(path)) File.Delete(path);
            return;
        }
        var sb = new StringBuilder();
        sb.AppendLine($"Density={PaintedFogDensity}");
        sb.AppendLine($"R={PaintedFogR}");
        sb.AppendLine($"G={PaintedFogG}");
        sb.AppendLine($"B={PaintedFogB}");
        sb.AppendLine($"SpeedX={PaintedFogSpeedX}");
        sb.AppendLine($"SpeedY={PaintedFogSpeedY}");
        foreach (var t in PaintedFogTiles)
            sb.AppendLine($"T={t.X},{t.Y}");
        File.WriteAllText(path, sb.ToString());
    }

    /// <summary>Load painted fog data from `Mapa{N}.aofog` if it exists. No-op if missing.</summary>
    public void LoadPaintedFog(string dir)
    {
        if (string.IsNullOrEmpty(dir) || MapNumber <= 0) return;
        string path = Path.Combine(dir, $"Mapa{MapNumber}.aofog");
        PaintedFogTiles.Clear();
        if (!File.Exists(path)) return;
        foreach (var rawLine in File.ReadAllLines(path))
        {
            var line = rawLine.Trim();
            if (line.Length == 0 || line.StartsWith("#")) continue;
            int eq = line.IndexOf('=');
            if (eq <= 0) continue;
            string key = line.Substring(0, eq).Trim();
            string val = line.Substring(eq + 1).Trim();
            switch (key.ToUpperInvariant())
            {
                case "DENSITY": if (int.TryParse(val, out var d)) PaintedFogDensity = d; break;
                case "R": if (int.TryParse(val, out var r)) PaintedFogR = r; break;
                case "G": if (int.TryParse(val, out var g)) PaintedFogG = g; break;
                case "B": if (int.TryParse(val, out var b)) PaintedFogB = b; break;
                case "SPEEDX": if (int.TryParse(val, out var sx)) PaintedFogSpeedX = sx; break;
                case "SPEEDY": if (int.TryParse(val, out var sy)) PaintedFogSpeedY = sy; break;
                case "T":
                    var parts = val.Split(',');
                    if (parts.Length == 2
                        && int.TryParse(parts[0], out var tx)
                        && int.TryParse(parts[1], out var ty))
                    {
                        PaintedFogTiles.Add(new Vector2I(tx, ty));
                    }
                    break;
            }
        }
    }
}
