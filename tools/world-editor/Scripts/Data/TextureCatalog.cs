#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Godot;

namespace AOWorldEditor.Data;

/// <summary>
/// A single texture reference from indices.ini.
/// Ancho/Alto define the tile pattern size (e.g. 4x4 = 16 consecutive GRH indices).
/// If Ancho=0 and Alto=0, it's a single GRH.
/// </summary>
public class TextureRef
{
    public int Index;        // REFERENCIA number
    public string Name = ""; // Display name
    public int GrhIndex;     // Starting GRH index
    public int TileWidth;    // Pattern width in tiles (0 = single)
    public int TileHeight;   // Pattern height in tiles (0 = single)
    public int Layer;         // Preferred layer (from Capa field, 0 = unspecified)
    public string Category = "Otros"; // From Type= field in indices.ini

    public int[] GetGrhIndices()
    {
        int w = Math.Max(TileWidth, 1);
        int h = Math.Max(TileHeight, 1);
        var indices = new int[w * h];
        for (int i = 0; i < indices.Length; i++)
            indices[i] = GrhIndex + i;
        return indices;
    }

    public int GetGrhAt(int tileX, int tileY)
    {
        int w = Math.Max(TileWidth, 1);
        return GrhIndex + tileY * w + tileX;
    }
}

/// <summary>
/// Parses indices.ini and organizes texture references into categories.
/// Each [REFERENCIA*] section must have a Type= field with its category.
/// </summary>
public class TextureCatalog
{
    // Preferred display order for categories
    private static readonly string[] PreferredOrder =
    {
        "Terreno", "Dungeons", "Techos", "Estructuras",
        "Naturaleza", "Objetos", "Otros"
    };

    public List<TextureRef> AllRefs { get; } = new();
    public Dictionary<string, List<TextureRef>> Categories { get; } = new();
    public List<string> CategoryOrder { get; } = new();

    public static TextureCatalog LoadFromFile(string path)
    {
        if (!File.Exists(path))
        {
            GD.PrintErr($"[Catalog] indices.ini not found: {path}");
            return new TextureCatalog();
        }
        return LoadFromLines(File.ReadAllLines(path));
    }

    public static TextureCatalog LoadFromLines(string[] lines)
    {
        var catalog = new TextureCatalog();
        var sections = ParseIni(lines);

        int refCount = 0;
        if (sections.TryGetValue("INIT", out var initSection))
            if (initSection.TryGetValue("Referencias", out var refStr))
                int.TryParse(refStr, out refCount);

        GD.Print($"[Catalog] Loading {refCount} references from indices.ini");

        for (int i = 0; i <= refCount + 10; i++)
        {
            string sectionName = $"REFERENCIA{i}";
            if (!sections.TryGetValue(sectionName, out var sec)) continue;

            string name = sec.GetValueOrDefault("Nombre", "");
            string type = sec.GetValueOrDefault("Type", "Otros");
            int grhIndex = 0, ancho = 0, alto = 0, capa = 0;
            if (sec.TryGetValue("GrhIndice", out var g)) int.TryParse(g, out grhIndex);
            if (sec.TryGetValue("Ancho", out var w)) int.TryParse(w, out ancho);
            if (sec.TryGetValue("Alto", out var h)) int.TryParse(h, out alto);
            if (sec.TryGetValue("Capa", out var c)) int.TryParse(c, out capa);

            if (grhIndex <= 0) continue;

            var texRef = new TextureRef
            {
                Index = i,
                Name = name,
                GrhIndex = grhIndex,
                TileWidth = ancho,
                TileHeight = alto,
                Layer = capa,
                Category = type,
            };

            catalog.AllRefs.Add(texRef);

            if (!catalog.Categories.ContainsKey(type))
                catalog.Categories[type] = new List<TextureRef>();
            catalog.Categories[type].Add(texRef);
        }

        // Build category order: preferred first, then any extras alphabetically
        foreach (var cat in PreferredOrder)
        {
            if (catalog.Categories.ContainsKey(cat))
                catalog.CategoryOrder.Add(cat);
        }
        foreach (var cat in catalog.Categories.Keys)
        {
            if (!catalog.CategoryOrder.Contains(cat))
                catalog.CategoryOrder.Add(cat);
        }

        GD.Print($"[Catalog] Loaded {catalog.AllRefs.Count} refs in {catalog.Categories.Count} categories");
        return catalog;
    }

    /// <summary>
    /// Adds a new category if it doesn't already exist.
    /// </summary>
    public void AddCategory(string name)
    {
        if (Categories.ContainsKey(name)) return;
        Categories[name] = new List<TextureRef>();
        CategoryOrder.Add(name);
    }

    /// <summary>
    /// Rebuilds AllRefs from CategoryOrder + per-category lists.
    /// Renumbers TextureRef.Index sequentially starting from 0.
    /// </summary>
    public void RebuildAllRefsFromCategories()
    {
        AllRefs.Clear();
        int idx = 0;
        foreach (var cat in CategoryOrder)
        {
            if (!Categories.TryGetValue(cat, out var refs)) continue;
            foreach (var r in refs)
            {
                r.Index = idx++;
                AllRefs.Add(r);
            }
        }
    }

    /// <summary>
    /// Writes the catalog back to an indices.ini file in the original format.
    /// </summary>
    public void SaveToFile(string path)
    {
        var sb = new StringBuilder();
        sb.AppendLine("[INIT]");
        sb.AppendLine($"Referencias={AllRefs.Count}");
        sb.AppendLine();

        foreach (var texRef in AllRefs)
        {
            sb.AppendLine($"[REFERENCIA{texRef.Index}]");
            sb.AppendLine($"Nombre={texRef.Name}");
            sb.AppendLine($"GrhIndice={texRef.GrhIndex}");
            sb.AppendLine($"Ancho={texRef.TileWidth}");
            sb.AppendLine($"Alto={texRef.TileHeight}");
            sb.AppendLine($"Capa={texRef.Layer}");
            sb.AppendLine($"Type={texRef.Category}");
            sb.AppendLine();
        }

        File.WriteAllText(path, sb.ToString());
        GD.Print($"[Catalog] Saved {AllRefs.Count} refs to {path}");
    }

    private static Dictionary<string, Dictionary<string, string>> ParseIni(string[] lines)
    {
        var sections = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
        string currentSection = "";

        foreach (var rawLine in lines)
        {
            var line = rawLine.Trim();
            if (line.Length == 0 || line[0] == ';' || line[0] == '#') continue;

            if (line[0] == '[' && line.Contains(']'))
            {
                currentSection = line.Substring(1, line.IndexOf(']') - 1).Trim();
                if (!sections.ContainsKey(currentSection))
                    sections[currentSection] = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                continue;
            }

            int eq = line.IndexOf('=');
            if (eq > 0 && currentSection.Length > 0)
            {
                string key = line.Substring(0, eq).Trim();
                string val = line.Substring(eq + 1).Trim();
                sections[currentSection][key] = val;
            }
        }

        return sections;
    }
}
