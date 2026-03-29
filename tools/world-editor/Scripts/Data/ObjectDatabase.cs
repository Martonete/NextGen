#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Godot;

namespace AOWorldEditor.Data;

/// <summary>
/// Minimal object record for the editor — name, GRH, type for display and placement.
/// </summary>
public class ObjRecord
{
    public int Number;
    public string Name = "";
    public int GrhIndex;
    public int ObjType;

    /// Human-readable type label for the editor palette.
    public string TypeLabel => ObjType switch
    {
        1 => "Comida",
        2 => "Arma",
        3 => "Armadura",
        4 => "Árbol",
        5 => "Dinero",
        6 => "Puerta",
        7 => "Contenedor",
        8 => "Cartel",
        9 => "Llave",
        10 => "Foro",
        11 => "Poción",
        12 => "Libro",
        13 => "Bebida",
        14 => "Leña",
        15 => "Fogata",
        16 => "Escudo",
        17 => "Casco",
        18 => "Anillo",
        19 => "Teleport",
        20 => "Mueble",
        21 => "Joya",
        22 => "Yacimiento",
        23 => "Metal",
        24 => "Pergamino",
        25 => "Aura",
        26 => "Instrumento",
        27 => "Yunque",
        28 => "Fragua",
        29 => "Gema",
        30 => "Flor",
        31 => "Barco",
        32 => "Flecha",
        33 => "Lingote",
        34 => "Cualquier",
        35 => "Mina",
        36 => "Mineral",
        37 => "Montura",
        _ => $"Tipo {ObjType}",
    };

    /// Category for filtering in the palette.
    public string Category => ObjType switch
    {
        2 or 16 or 17 or 32 => "Equipamiento",
        1 or 11 or 13 => "Consumibles",
        3 or 18 or 21 => "Vestimenta",
        4 or 8 or 20 or 30 => "Decoración",
        6 or 9 => "Puertas/Llaves",
        _ => "Otros",
    };
}

/// <summary>
/// Loads and provides access to object data from Obj.dat.
/// </summary>
public class ObjectDatabase
{
    public readonly List<ObjRecord> All = new();
    private readonly Dictionary<int, ObjRecord> _byNumber = new();

    public ObjRecord? Get(int objNumber)
    {
        _byNumber.TryGetValue(objNumber, out var rec);
        return rec;
    }

    /// <summary>
    /// Load object database from Obj.dat.
    /// </summary>
    public static ObjectDatabase Load(string objDatPath)
    {
        var db = new ObjectDatabase();
        if (!File.Exists(objDatPath)) return db;

        var sections = ParseIniFile(objDatPath);

        int numObjs = 0;
        if (sections.TryGetValue("INIT", out var init))
            if (init.TryGetValue("NumOBJs", out var n))
                int.TryParse(n, out numObjs);

        for (int i = 1; i <= numObjs; i++)
        {
            if (!sections.TryGetValue($"OBJ{i}", out var sec)) continue;

            var rec = new ObjRecord { Number = i };

            if (sec.TryGetValue("Name", out var name)) rec.Name = name;
            if (sec.TryGetValue("GrhIndex", out var grh)) int.TryParse(grh, out rec.GrhIndex);
            if (sec.TryGetValue("ObjType", out var otype)) int.TryParse(otype, out rec.ObjType);

            if (rec.GrhIndex > 0) // Only include objects with a visual
            {
                db._byNumber[i] = rec;
                db.All.Add(rec);
            }
        }

        db.All.Sort((a, b) => a.Number.CompareTo(b.Number));
        GD.Print($"[ObjectDatabase] Loaded {db.All.Count} objects with visuals");
        return db;
    }

    private static Dictionary<string, Dictionary<string, string>> ParseIniFile(string path)
    {
        var sections = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);

        string[] lines;
        try
        {
            var raw = File.ReadAllBytes(path);
            if (raw.Length >= 2 && raw[0] == 0xFF && raw[1] == 0xFE)
                lines = Encoding.Unicode.GetString(raw).Split('\n');
            else
                lines = Encoding.UTF8.GetString(raw).Split('\n');
        }
        catch { return sections; }

        string currentSection = "";
        foreach (var rawLine in lines)
        {
            var line = rawLine.Trim().TrimEnd('\r');
            if (line.Length == 0 || line[0] == '\'') continue;

            if (line[0] == '[')
            {
                int end = line.IndexOf(']');
                if (end > 1)
                {
                    currentSection = line[1..end].Trim();
                    if (!sections.ContainsKey(currentSection))
                        sections[currentSection] = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                }
                continue;
            }

            int eq = line.IndexOf('=');
            if (eq > 0 && currentSection.Length > 0)
            {
                string key = line[..eq].Trim();
                string val = line[(eq + 1)..].Trim();
                int comment = val.IndexOf('\'');
                if (comment >= 0) val = val[..comment].Trim();
                if (sections.ContainsKey(currentSection))
                    sections[currentSection][key] = val;
            }
        }

        return sections;
    }
}
