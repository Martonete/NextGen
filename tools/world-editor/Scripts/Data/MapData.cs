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

    // ── Painted fog ──
    // Each layer has its own style (color, density, speed) + tiles.
    // Changing one layer's style doesn't affect the others — users can
    // have different-colored smokes on the same map.
    public List<PaintedFogLayer> PaintedFogLayers = new();
    /// <summary>Index of the currently-active layer. New paints and style
    /// edits from the panel go into this layer. -1 = no layer selected.</summary>
    public int ActiveFogLayerIndex = -1;

    // User-defined named smoke presets (templates used to seed new layers).
    public List<SmokePrefab> UserFogPrefabs = new();
    /// <summary>User-saved cloud prefabs: style + relative tile pattern,
    /// stamped onto the map with one click. Different from SmokePrefab
    /// (style only) — these also carry a SHAPE (relative tile offsets).</summary>
    public List<CloudPrefab> UserCloudPrefabs = new();
    // Global map-level toggle: when true the shader uses a bounded
    // oscillation offset so the pattern "floats in place" without drift.
    public bool FogFreeSmoke = false;

    // ── Advanced lights (new system, separate from per-tile LightRange) ──
    // Loaded from / saved to MapaN.aolight. Stays empty for legacy maps.
    public MapLightData LightData = new();

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

    /// <summary>Save painted fog data to `Mapa{N}.aofog`. Format:
    ///   FreeSmoke=0/1
    ///   Prefab=name|density|r|g|b|sx|sy  (user-saved templates, 0..N)
    ///   Layer=name|density|r|g|b|sx|sy   (starts a new layer)
    ///   T=x,y                             (tile in current layer)
    ///   Layer=...
    ///   T=...
    /// </summary>
    public void SavePaintedFog(string dir)
    {
        if (string.IsNullOrEmpty(dir) || MapNumber <= 0) return;
        string path = Path.Combine(dir, $"Mapa{MapNumber}.aofog");
        int totalTiles = 0;
        foreach (var l in PaintedFogLayers) totalTiles += l.Tiles.Count;
        if (totalTiles == 0 && UserFogPrefabs.Count == 0
            && UserCloudPrefabs.Count == 0 && !FogFreeSmoke)
        {
            if (File.Exists(path)) File.Delete(path);
            return;
        }
        var sb = new StringBuilder();
        sb.AppendLine($"FreeSmoke={(FogFreeSmoke ? 1 : 0)}");
        foreach (var p in UserFogPrefabs)
            sb.AppendLine($"Prefab={p.Serialize()}");
        // Cloud prefabs: header line + one CT line per relative tile
        foreach (var c in UserCloudPrefabs)
        {
            sb.AppendLine($"Cloud={c.SerializeHeader()}");
            foreach (var rt in c.RelativeTiles)
                sb.AppendLine($"CT={rt.X},{rt.Y}");
        }
        foreach (var layer in PaintedFogLayers)
        {
            if (layer.Tiles.Count == 0) continue;
            sb.AppendLine($"Layer={layer.SerializeStyle()}");
            foreach (var t in layer.Tiles)
                sb.AppendLine($"T={t.X},{t.Y}");
        }
        File.WriteAllText(path, sb.ToString());
    }

    /// <summary>Load painted fog data from `Mapa{N}.aofog` if it exists.
    /// Supports BOTH the new layered format and the old flat format
    /// (one global style + one big PaintedFogTiles set) for backward compat.</summary>
    public void LoadPaintedFog(string dir)
    {
        if (string.IsNullOrEmpty(dir) || MapNumber <= 0) return;
        string path = Path.Combine(dir, $"Mapa{MapNumber}.aofog");
        PaintedFogLayers.Clear();
        UserFogPrefabs.Clear();
        UserCloudPrefabs.Clear();
        ActiveFogLayerIndex = -1;
        FogFreeSmoke = false;
        if (!File.Exists(path)) return;

        // Track old-format state and convert to a single layer at the end
        int legacyDensity = 160, legacyR = 128, legacyG = 140, legacyB = 160, legacySX = 5, legacySY = 2;
        var legacyTiles = new HashSet<Vector2I>();
        bool sawLayer = false;
        PaintedFogLayer? currentLayer = null;
        CloudPrefab? currentCloud = null;

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
                case "FREESMOKE": FogFreeSmoke = val == "1" || val.ToLowerInvariant() == "true"; break;
                case "PREFAB":
                    var pf = SmokePrefab.TryParse(val);
                    if (pf != null) UserFogPrefabs.Add(pf);
                    break;
                case "CLOUD":
                    var cp = CloudPrefab.TryParseHeader(val);
                    if (cp != null)
                    {
                        UserCloudPrefabs.Add(cp);
                        currentCloud = cp;
                        currentLayer = null; // switch context away from layer
                    }
                    break;
                case "CT":
                    var ctParts = val.Split(',');
                    if (currentCloud != null && ctParts.Length == 2
                        && int.TryParse(ctParts[0], out var ctx)
                        && int.TryParse(ctParts[1], out var cty))
                    {
                        currentCloud.RelativeTiles.Add(new Vector2I(ctx, cty));
                    }
                    break;
                case "LAYER":
                    var nl = PaintedFogLayer.TryParseStyle(val);
                    if (nl != null)
                    {
                        sawLayer = true;
                        PaintedFogLayers.Add(nl);
                        currentLayer = nl;
                        currentCloud = null; // switch context away from cloud
                    }
                    break;
                case "T":
                    var parts = val.Split(',');
                    if (parts.Length == 2
                        && int.TryParse(parts[0], out var tx)
                        && int.TryParse(parts[1], out var ty))
                    {
                        var tile = new Vector2I(tx, ty);
                        if (currentLayer != null) currentLayer.Tiles.Add(tile);
                        else legacyTiles.Add(tile);
                    }
                    break;
                // Old-format style header — only used if no Layer= lines appear
                case "DENSITY": if (int.TryParse(val, out var d)) legacyDensity = d; break;
                case "R": if (int.TryParse(val, out var r)) legacyR = r; break;
                case "G": if (int.TryParse(val, out var g)) legacyG = g; break;
                case "B": if (int.TryParse(val, out var b)) legacyB = b; break;
                case "SPEEDX": if (int.TryParse(val, out var sx)) legacySX = sx; break;
                case "SPEEDY": if (int.TryParse(val, out var sy)) legacySY = sy; break;
            }
        }

        // If there were no Layer= lines, migrate the old-format flat style
        // + tile list into a single layer called "Humo".
        if (!sawLayer && legacyTiles.Count > 0)
        {
            var legacy = new PaintedFogLayer
            {
                Name = "Humo",
                Density = legacyDensity, R = legacyR, G = legacyG, B = legacyB,
                SpeedX = legacySX, SpeedY = legacySY,
                Tiles = legacyTiles,
            };
            PaintedFogLayers.Add(legacy);
        }

        if (PaintedFogLayers.Count > 0)
            ActiveFogLayerIndex = 0;
    }

    /// <summary>Save advanced-light data to `Mapa{N}.aolight`. Legacy
    /// per-tile lights (LightRange/R/G/B in MapTile) are unaffected — they
    /// continue to live in the .aomap file as before.</summary>
    public void SaveLightData(string dir)
    {
        if (string.IsNullOrEmpty(dir) || MapNumber <= 0) return;
        LightData.Save(dir, MapNumber);
    }

    /// <summary>Load advanced-light data from `Mapa{N}.aolight` if it exists.
    /// Missing file → empty LightData (legacy maps show no advanced lights).</summary>
    public void LoadLightData(string dir)
    {
        if (string.IsNullOrEmpty(dir) || MapNumber <= 0)
        {
            LightData = new MapLightData();
            return;
        }
        LightData = MapLightData.Load(dir, MapNumber);
    }
}
