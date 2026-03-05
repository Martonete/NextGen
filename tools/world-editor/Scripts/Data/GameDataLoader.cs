using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Godot;

namespace AOWorldEditor.Data;

/// <summary>
/// Loads minimal game data (object GRH indices, NPC body indices) for map preview.
/// Handles both UTF-8 and UTF-16LE encoded INI files (VB6 legacy).
/// </summary>
public static class GameDataLoader
{
    /// <summary>
    /// Load object GRH indices from Obj.dat. Returns array indexed by object number (1-based).
    /// </summary>
    public static int[] LoadObjectGrhs(string objDatPath)
    {
        if (!File.Exists(objDatPath)) return Array.Empty<int>();

        var sections = ParseIniFile(objDatPath);

        int numObjs = 0;
        if (sections.TryGetValue("INIT", out var init))
            if (init.TryGetValue("NumOBJs", out var n))
                int.TryParse(n, out numObjs);

        if (numObjs <= 0) return Array.Empty<int>();

        var grhs = new int[numObjs + 1];
        for (int i = 1; i <= numObjs; i++)
        {
            if (sections.TryGetValue($"OBJ{i}", out var sec))
                if (sec.TryGetValue("GrhIndex", out var g))
                    int.TryParse(g, out grhs[i]);
        }

        GD.Print($"[GameData] Loaded {numObjs} object GRH indices");
        return grhs;
    }

    /// <summary>
    /// Load NPC body GRH indices from NPCs.dat. Returns array indexed by NPC number (1-based).
    /// Body is a body animation index — maps to walking animation GRHs.
    /// </summary>
    public static int[] LoadNpcBodies(string npcDatPath)
    {
        if (!File.Exists(npcDatPath)) return Array.Empty<int>();

        var sections = ParseIniFile(npcDatPath);

        int numNpcs = 0;
        if (sections.TryGetValue("INIT", out var init))
            if (init.TryGetValue("NumNPCs", out var n))
                int.TryParse(n, out numNpcs);

        if (numNpcs <= 0) return Array.Empty<int>();

        var bodies = new int[numNpcs + 1];
        for (int i = 1; i <= numNpcs; i++)
        {
            if (sections.TryGetValue($"NPC{i}", out var sec))
                if (sec.TryGetValue("Body", out var b))
                    int.TryParse(b, out bodies[i]);
        }

        GD.Print($"[GameData] Loaded {numNpcs} NPC body indices");
        return bodies;
    }

    /// <summary>
    /// Load Personajes.ind binary — returns south-facing walk GRH for each body index.
    /// Format: 263B header + i16 count + (i16[4] walk + i16 headOfsX + i16 headOfsY) per entry.
    /// </summary>
    public static int[] LoadBodyGrhs(string personajesPath)
    {
        if (!File.Exists(personajesPath)) return Array.Empty<int>();

        var data = File.ReadAllBytes(personajesPath);
        using var reader = new BinaryReader(new MemoryStream(data));

        reader.BaseStream.Seek(263, SeekOrigin.Begin); // MiCabecera
        short count = reader.ReadInt16();
        if (count <= 0) return Array.Empty<int>();

        var bodies = new int[count + 1];
        for (int i = 1; i <= count; i++)
        {
            reader.ReadInt16(); // Walk North
            reader.ReadInt16(); // Walk East
            bodies[i] = reader.ReadInt16(); // Walk South — used for preview
            reader.ReadInt16(); // Walk West
            reader.ReadInt16(); // HeadOffsetX
            reader.ReadInt16(); // HeadOffsetY
        }

        GD.Print($"[GameData] Loaded {count} body south-walk GRHs");
        return bodies;
    }

    private static Dictionary<string, Dictionary<string, string>> ParseIniFile(string path)
    {
        var sections = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);

        // Try UTF-8 first, fall back to UTF-16LE (VB6 saves some files as UTF-16)
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
                // Strip inline comments (VB6 style ' comment)
                int comment = val.IndexOf('\'');
                if (comment >= 0) val = val[..comment].Trim();
                if (sections.ContainsKey(currentSection))
                    sections[currentSection][key] = val;
            }
        }

        return sections;
    }
}
