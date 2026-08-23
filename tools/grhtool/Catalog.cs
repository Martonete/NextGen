using System.Text.Json;
using System.Text.Json.Serialization;

namespace GrhTool;

/// <summary>What a sheet is used for. Drives entity-table assignment and the editor palette.</summary>
public enum GrhClass
{
    Unknown,
    Tile,       // 32x32 ground
    TileBlock,  // 4x4 or 8x8 grid of tiles
    Body,       // 4-direction walk cycle
    Head,       // 4-direction head strip
    Helmet,     // 4-direction helmet/hat strip
    Npc,        // creature sheet: frame counts differ from player bodies
    Weapon,     // 4-direction weapon overlay, same 150x180 layout as a body
    Shield,     // 4-direction shield overlay
    Equip4Dir,  // weapon or shield - identical geometry, disambiguated by hand
    Prop,       // single large region: structure, tree, furniture
    Item,       // 32x32 inventory art
    Fx,         // animated effect
}

public sealed class SheetInfo
{
    public int Sheet { get; set; }
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public GrhClass Class { get; set; }
    /// <summary>"auto" when derived from geometry, "manual" once a human corrected it.</summary>
    public string Source { get; set; } = "auto";
    public string Confidence { get; set; } = "low";
    public int Regions { get; set; }
    public string Signature { get; set; } = "";
    public List<int> Grhs { get; set; } = new();
}

public sealed class Catalog
{
    public string Generated { get; set; } = "";
    public List<SheetInfo> Sheets { get; set; } = new();
    /// <summary>GRH ranges the renderer treats as water. Replaces WorldRenderer.IsWaterGrh.</summary>
    public List<int[]> WaterRanges { get; set; } = new();
    /// <summary>GRHs drawn as tree canopies (roof transparency). Replaces WorldRenderer.IsTree.</summary>
    public List<int> Trees { get; set; } = new();
    /// <summary>Named UI graphics that were hardcoded constants.</summary>
    public Dictionary<string, int> Ui { get; set; } = new();

    private static readonly JsonSerializerOptions Opts = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public void Save(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(this, Opts));
    }

    public static Catalog Load(string path) =>
        JsonSerializer.Deserialize<Catalog>(File.ReadAllText(path), Opts)
        ?? throw new InvalidDataException($"No se pudo leer {path}");

    /// <summary>
    /// Category implied by the sheet number. The new catalogue is grouped in
    /// blocks of 1000, and the block is a stronger signal than shape: 102xxx and
    /// 118xxx share geometry with weapon strips but were confirmed by eye to be
    /// heads and helmets. Returns null where the block is mixed and geometry
    /// should decide.
    /// </summary>
    private static GrhClass? ClassFromSheetNumber(int sheet) => (sheet / 1000) switch
    {
        102 => GrhClass.Head,    // verified visually: faces, hair, 4 facings
        118 => GrhClass.Helmet,  // verified visually: helms, mage hats, hoods
        104 => GrhClass.Npc,     // verified visually: beasts, birds, horses, guards
        // 130xxx are multi-piece prop atlases: trees, walls, ruins, bridges.
        // Several large independent pieces per sheet, so shape alone reads as noise.
        130 => GrhClass.Prop,
        119 or 123 => GrhClass.Tile,
        // These blocks all use the 150x180 body layout, so geometry alone calls
        // every one of them a body. Rendering them apart shows what they are:
        // 124xxx are the bare bodies, and the rest are equipment overlays drawn
        // on top of one - weapons swing in 116/121/122, shields sit in 120.
        124 => GrhClass.Body,
        116 or 121 or 122 => GrhClass.Weapon,
        120 => GrhClass.Shield,
        _ => null,
    };

    /// <summary>
    /// Classifies sheets from the geometry already present in the index, not from
    /// pixel inspection, with the sheet-number block overriding shape where it is
    /// known. The signatures were measured against the 2636 indexed sheets.
    /// </summary>
    public static SheetInfo Classify(int sheet, List<GrhEntry> statics)
    {
        var info = new SheetInfo
        {
            Sheet = sheet,
            Regions = statics.Count,
            Grhs = statics.Select(s => s.Index).OrderBy(i => i).ToList(),
        };

        var sizes = statics.Select(s => (s.Width, s.Height)).ToList();
        var dominant = sizes.GroupBy(s => s).OrderByDescending(g => g.Count()).First();
        bool uniform = dominant.Count() == sizes.Count;
        var (w, h) = dominant.Key;
        info.Signature = $"{statics.Count}x{w}x{h}" + (uniform ? "" : " (mixto)");

        int n = statics.Count;
        var byGeometry = (n, w, h, uniform) switch
        {
            // 22 regions of 25x45: a 150x180 body sheet, rows of 6/6/5/5 frames.
            (22, 25, 45, _) => (GrhClass.Body, "high"),
            (4, 17, 50, _) => (GrhClass.Equip4Dir, "high"),
            (4, 17, 16, _) or (4, 17, 28, _) => (GrhClass.Helmet, "medium"),
            (1, 32, 32, _) => (GrhClass.Tile, "high"),
            (16, 32, 32, true) => (GrhClass.TileBlock, "high"),
            (64, 32, 32, true) => (GrhClass.TileBlock, "high"),
            (1, _, _, _) when w >= 128 && h >= 64 => (GrhClass.Prop, "medium"),
            (1, _, _, _) => (GrhClass.Item, "low"),
            _ when uniform && w == 32 && h == 32 => (GrhClass.TileBlock, "medium"),
            // Several uniform regions that are not tiles: a directional strip of
            // some kind. Only the sheet block can say what it belongs to.
            _ when uniform && n >= 4 => (GrhClass.Npc, "low"),
            // Mixed regions, none tile-sized: an atlas of independent scenery
            // pieces (trees, walls, ruins) packed onto one sheet.
            _ when !uniform && w >= 48 && h >= 48 => (GrhClass.Prop, "low"),
            _ => (GrhClass.Unknown, "low"),
        };

        // The block wins when it is known: 102xxx looks like a weapon strip but
        // holds heads, so trusting shape there mis-files 234 sheets at once.
        // These blocks were each checked against rendered contact sheets, so the
        // block verdict is high-confidence on its own, not only when shape agrees.
        var fromNumber = ClassFromSheetNumber(sheet);
        if (fromNumber is GrhClass known)
            (info.Class, info.Confidence) = (known, "high");
        else
            (info.Class, info.Confidence) = byGeometry;

        return info;
    }
}
