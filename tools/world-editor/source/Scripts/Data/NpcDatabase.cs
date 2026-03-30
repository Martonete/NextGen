#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Godot;

namespace AOWorldEditor.Data;

/// <summary>
/// Minimal NPC record for the editor — just what we need to display and place NPCs.
/// </summary>
public class NpcRecord
{
    public int Number;
    public string Name = "";
    public int Body;
    public int Head;
    public int Heading;
    public int NpcType; // 0=Common, 1=Reviver, etc.
    public bool Hostile;
    public bool Comercia;
    public string Desc = "";

    /// Human-readable type label for the editor palette.
    public string TypeLabel => NpcType switch
    {
        0 => "Común",
        1 => "Revividor",
        2 => "Guardia Real",
        3 => "Entrenador",
        4 => "Banquero",
        5 => "Noble",
        6 => "Dragón",
        7 => "Timbero",
        8 => "Guardia Caos",
        9 => "Renuncia",
        10 => "Rey Castillo",
        11 => "Quest",
        12 => "Viajero",
        13 => "Ciudadanía",
        14 => "Inscripción",
        15 => "Inmobiliaria",
        16 => "Arena",
        17 => "Noble Quest",
        18 => "God NPC",
        19 => "Cirujano",
        20 => "Bargomaud",
        21 => "Quinta Jera",
        22 => "Bove Clan",
        23 => "Correo",
        24 => "Box Delivery",
        _ => $"Tipo {NpcType}",
    };
}

/// <summary>
/// Loads and provides access to NPC data from NPCs.dat + NPCs-HOSTILES.dat.
/// </summary>
public class NpcDatabase
{
    public readonly List<NpcRecord> All = new();
    private readonly Dictionary<int, NpcRecord> _byNumber = new();

    public NpcRecord? Get(int npcNumber)
    {
        _byNumber.TryGetValue(npcNumber, out var rec);
        return rec;
    }

    /// <summary>
    /// Load NPC database from a server dat/ directory.
    /// Reads NPCs.dat and NPCs-HOSTILES.dat if present.
    /// </summary>
    public static NpcDatabase Load(string datDir)
    {
        var db = new NpcDatabase();

        string npcDat = Path.Combine(datDir, "NPCs.dat");
        if (File.Exists(npcDat))
            LoadFile(npcDat, db);

        string hostileDat = Path.Combine(datDir, "NPCs-HOSTILES.dat");
        if (File.Exists(hostileDat))
            LoadFile(hostileDat, db);

        db.All.Sort((a, b) => a.Number.CompareTo(b.Number));
        GD.Print($"[NpcDatabase] Loaded {db.All.Count} NPCs");
        return db;
    }

    private static void LoadFile(string path, NpcDatabase db)
    {
        var sections = ParseIniFile(path);

        foreach (var (secName, props) in sections)
        {
            if (!secName.StartsWith("NPC", StringComparison.OrdinalIgnoreCase)) continue;
            if (!int.TryParse(secName.AsSpan(3), out int num) || num <= 0) continue;

            var rec = new NpcRecord { Number = num };

            if (props.TryGetValue("Name", out var name)) rec.Name = name;
            if (props.TryGetValue("Desc", out var desc)) rec.Desc = desc;
            if (props.TryGetValue("Body", out var body)) int.TryParse(body, out rec.Body);
            if (props.TryGetValue("Head", out var head)) int.TryParse(head, out rec.Head);
            if (props.TryGetValue("Heading", out var heading)) int.TryParse(heading, out rec.Heading);
            if (props.TryGetValue("NpcType", out var ntype)) int.TryParse(ntype, out rec.NpcType);
            if (props.TryGetValue("Hostile", out var hostile)) rec.Hostile = hostile == "1";
            if (props.TryGetValue("Comercia", out var com)) rec.Comercia = com == "1";

            if (!db._byNumber.ContainsKey(num))
            {
                db._byNumber[num] = rec;
                db.All.Add(rec);
            }
        }
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
