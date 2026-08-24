#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Godot;

namespace AODateador.Data;

/// <summary>
/// Reads the sheet classification from <c>INIT/GrhCatalog.json</c> so the
/// graphic picker can offer, say, only weapon sheets when editing a weapon.
///
/// Without it the picker would list all 207145 GRHs at once. The file is
/// produced by <c>grhtool classify</c>; if it is missing the picker still
/// works, just unfiltered.
/// </summary>
public sealed class GrhCatalogIndex
{
    /// <summary>Sheet classes as written by grhtool (tools/grhtool/Catalog.cs).</summary>
    public const string ClassItem = "Item", ClassWeapon = "Weapon", ClassShield = "Shield",
                        ClassHelmet = "Helmet", ClassBody = "Body", ClassHead = "Head",
                        ClassNpc = "Npc", ClassProp = "Prop", ClassTile = "Tile";

    /// <summary>GRH indices grouped by sheet class.</summary>
    private readonly Dictionary<string, List<int>> _byClass = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Every classified GRH, for the unfiltered view.</summary>
    public List<int> AllGrhs { get; } = new();

    public bool IsLoaded => AllGrhs.Count > 0;

    public IReadOnlyList<int> ForClass(string? className)
    {
        if (className == null) return AllGrhs;
        return _byClass.TryGetValue(className, out var list) ? list : Array.Empty<int>();
    }

    /// <summary>Classes that actually have entries, in a sensible display order.</summary>
    public IEnumerable<string> Classes()
        => new[] { ClassItem, ClassWeapon, ClassShield, ClassHelmet, ClassBody,
                   ClassHead, ClassNpc, ClassProp, ClassTile }
           .Where(c => _byClass.ContainsKey(c));

    /// <summary>
    /// Sheet class that best matches an object type, or null to show everything.
    /// Inventory icons come from Item sheets whatever the object is; the
    /// equipment classes are offered as a second tab in the picker.
    /// </summary>
    public static string? ClassForObjType(int objType) => objType switch
    {
        FieldDefs.Weapon => ClassWeapon,
        FieldDefs.Shield => ClassShield,
        FieldDefs.Helmet => ClassHelmet,
        _ => ClassItem,
    };

    public static GrhCatalogIndex Load(string initDir)
    {
        var index = new GrhCatalogIndex();
        string path = Path.Combine(initDir, "GrhCatalog.json");
        if (!File.Exists(path))
        {
            GD.PushWarning($"[Catalog] No se encontró {path}; el selector no podrá filtrar por clase.");
            return index;
        }

        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllBytes(path));
            if (!doc.RootElement.TryGetProperty("Sheets", out var sheets)) return index;

            foreach (var sheet in sheets.EnumerateArray())
            {
                if (!sheet.TryGetProperty("Class", out var classProp)) continue;
                if (!sheet.TryGetProperty("Grhs", out var grhs)) continue;

                string className = classProp.GetString() ?? "Unknown";
                if (!index._byClass.TryGetValue(className, out var list))
                    index._byClass[className] = list = new List<int>();

                foreach (var g in grhs.EnumerateArray())
                {
                    int value = g.GetInt32();
                    list.Add(value);
                    index.AllGrhs.Add(value);
                }
            }

            index.AllGrhs.Sort();
            foreach (var list in index._byClass.Values) list.Sort();

            GD.Print($"[Catalog] {index.AllGrhs.Count} GRHs en {index._byClass.Count} clases");
        }
        catch (Exception ex)
        {
            GD.PushError($"[Catalog] No se pudo leer GrhCatalog.json: {ex.Message}");
        }
        return index;
    }
}
