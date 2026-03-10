#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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
    public string Category = "Otros"; // Extracted category

    /// <summary>
    /// Returns all GRH indices that compose this texture pattern.
    /// For a 4x4 pattern starting at GrhIndex G: G, G+1, G+2, ..., G+15.
    /// For single tiles (0x0): just [GrhIndex].
    /// </summary>
    public int[] GetGrhIndices()
    {
        int w = Math.Max(TileWidth, 1);
        int h = Math.Max(TileHeight, 1);
        var indices = new int[w * h];
        for (int i = 0; i < indices.Length; i++)
            indices[i] = GrhIndex + i;
        return indices;
    }

    /// <summary>
    /// Get the GRH index at a specific tile position within the pattern.
    /// </summary>
    public int GetGrhAt(int tileX, int tileY)
    {
        int w = Math.Max(TileWidth, 1);
        return GrhIndex + tileY * w + tileX;
    }
}

/// <summary>
/// Parses indices.ini and organizes texture references into categories.
/// Categories are auto-extracted from name prefixes like (PRD), (ARBOL), (CASA), etc.
/// </summary>
public class TextureCatalog
{
    public List<TextureRef> AllRefs { get; } = new();
    public Dictionary<string, List<TextureRef>> Categories { get; } = new();
    public List<string> CategoryOrder { get; } = new();

    // Category mapping from name prefixes to display names.
    // ORDER MATTERS: first match wins. Consolidated into ~14 logical groups.
    private static readonly (string prefix, string display)[] CategoryMap = new[]
    {
        // --- High-priority keywords (override parent categories) ---
        ("TECHO", "Techos"),
        ("COSTA", "Costas"),
        ("PISO", "Pisos"),
        ("PIDO", "Pisos"),             // typo in indices.ini
        ("PARED", "Estructuras"),
        ("PUERTA", "Estructuras"),
        ("MURALLA", "Estructuras"),
        ("CERCO", "Estructuras"),
        ("BARANDA", "Estructuras"),

        // --- Terrain (all ground/surface types) ---
        ("(PRD)", "Terreno"),
        ("(PRADERA)", "Terreno"),
        ("(ESP)", "Terreno"),
        ("(DESFILADERO)", "Terreno"),
        ("DESFILADERO", "Terreno"),
        ("PASTO", "Terreno"),
        ("(AGUA)", "Terreno"),
        ("CASCADA", "Terreno"),
        ("PECES", "Terreno"),
        ("(NIEVE)", "Terreno"),
        ("NEVADO", "Terreno"),
        ("(DESIERTO)", "Terreno"),
        ("DESIERTO", "Terreno"),
        ("DECIERTO", "Terreno"),       // typo
        ("(LAVA)", "Terreno"),
        ("LAVA", "Terreno"),
        ("(INFERNO)", "Terreno"),
        ("INFERNO", "Terreno"),
        ("ANCIENT", "Terreno"),
        ("(DUNGEON)", "Terreno"),
        ("(CAVERNA)", "Terreno"),
        ("(CLOACA)", "Terreno"),
        ("CUEVAS", "Terreno"),
        ("CUEVA", "Terreno"),
        ("CATAS", "Terreno"),

        // --- Nature ---
        ("(ARBOL)", "Naturaleza"),
        ("(ROCA)", "Naturaleza"),
        ("HOJAS", "Naturaleza"),
        ("(MONTAÑA)", "Naturaleza"),
        ("MONTAÍA", "Naturaleza"),     // encoding variant

        // --- Buildings & Interiors ---
        ("HERRERIA", "Edificios"),
        ("MINERIA", "Edificios"),
        ("PESCADERIA", "Edificios"),
        ("POCIONES", "Edificios"),
        ("PROPIEDADES", "Edificios"),
        ("PROVISIONES", "Edificios"),
        ("ARMERIA", "Edificios"),
        ("SASTRE", "Edificios"),
        ("CARPINTERO", "Edificios"),
        ("MERCADO", "Edificios"),
        ("CARPA", "Edificios"),
        ("IGLESIA", "Edificios"),
        ("(CASA)", "Edificios"),
        ("CASA(", "Edificios"),

        // --- Decorations & Objects ---
        ("(OBJ)", "Objetos"),
        ("CARTEL", "Objetos"),
        ("MONUMENTO", "Objetos"),
        ("LAPIDA", "Objetos"),
        ("BANCO", "Objetos"),
        ("MUEBLE", "Objetos"),
        ("(FOGATA)", "Objetos"),
        ("(ENTRADA)", "Objetos"),
        ("RECIPIENTE", "Objetos"),
        ("HORNO", "Objetos"),
        ("POSTE", "Objetos"),
        ("PALCO", "Objetos"),
        ("ALCANTARILLA", "Objetos"),
        ("ANIMACI", "Objetos"),        // ANIMACIÓN

        // --- Cemetery ---
        ("CEMENTERIO", "Cementerio"),

        // --- Ships ---
        ("GALEON", "Barcos"),
        ("GALERA", "Barcos"),
        ("BARCA", "Barcos"),
        ("BARCO", "Barcos"),

        // --- Coliseum ---
        ("(COLISEO)", "Coliseo"),
    };

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
            int grhIndex = 0, ancho = 0, alto = 0, capa = 0;
            if (sec.TryGetValue("GrhIndice", out var g)) int.TryParse(g, out grhIndex);
            if (sec.TryGetValue("Ancho", out var w)) int.TryParse(w, out ancho);
            if (sec.TryGetValue("Alto", out var h)) int.TryParse(h, out alto);
            if (sec.TryGetValue("Capa", out var c)) int.TryParse(c, out capa);

            if (grhIndex <= 0) continue;

            string category = ExtractCategory(name);
            // Override layer for specific categories
            int layer = capa;
            if (category == "Costas") layer = 2;
            else if (category == "Techos") layer = 4;

            var texRef = new TextureRef
            {
                Index = i,
                Name = name,
                GrhIndex = grhIndex,
                TileWidth = ancho,
                TileHeight = alto,
                Layer = layer,
                Category = category,
            };

            catalog.AllRefs.Add(texRef);

            if (!catalog.Categories.ContainsKey(texRef.Category))
            {
                catalog.Categories[texRef.Category] = new List<TextureRef>();
                catalog.CategoryOrder.Add(texRef.Category);
            }
            catalog.Categories[texRef.Category].Add(texRef);
        }

        GD.Print($"[Catalog] Loaded {catalog.AllRefs.Count} refs in {catalog.Categories.Count} categories");
        return catalog;
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

    private static string ExtractCategory(string name)
    {
        string upper = name.ToUpperInvariant();
        foreach (var (prefix, display) in CategoryMap)
        {
            if (upper.Contains(prefix.ToUpperInvariant()))
                return display;
        }

        // Try to extract from parenthetical prefix
        if (name.StartsWith("("))
        {
            int end = name.IndexOf(')');
            if (end > 1)
                return name.Substring(1, end - 1);
        }

        return "Otros";
    }
}
