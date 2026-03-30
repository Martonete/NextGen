#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using AoPak;
using Godot;

namespace AOWorldEditor.Data;

/// <summary>
/// Door object data for walk mode preview.
/// </summary>
public class DoorInfo
{
    public int ObjType;         // 6=Door (otPuertas) in VB6
    public int IndexAbierta;    // Object index when open
    public int IndexCerrada;    // Object index when closed
    public int IndexCerradaLlave; // Object index when locked-closed
    public int PuertaDoble;     // 1=double door
    public int Porton;          // 1=grand gate
    public int Abierta;         // VB6: 0=open, 1=closed (inverted logic)
    public int Llave;           // 1=locked
    public int GrhIndex;        // Current visual GRH
}

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
    /// Load NPC body and head indices from NPCs.dat.
    /// Returns (bodies, heads) arrays indexed by NPC number (1-based).
    /// </summary>
    public static (int[] bodies, int[] heads) LoadNpcData(string npcDatPath)
    {
        if (!File.Exists(npcDatPath))
            return (Array.Empty<int>(), Array.Empty<int>());

        var sections = ParseIniFile(npcDatPath);

        int numNpcs = 0;
        if (sections.TryGetValue("INIT", out var init))
            if (init.TryGetValue("NumNPCs", out var n))
                int.TryParse(n, out numNpcs);

        if (numNpcs <= 0)
            return (Array.Empty<int>(), Array.Empty<int>());

        var bodies = new int[numNpcs + 1];
        var heads = new int[numNpcs + 1];
        for (int i = 1; i <= numNpcs; i++)
        {
            if (sections.TryGetValue($"NPC{i}", out var sec))
            {
                if (sec.TryGetValue("Body", out var b))
                    int.TryParse(b, out bodies[i]);
                if (sec.TryGetValue("Head", out var h))
                    int.TryParse(h, out heads[i]);
            }
        }

        GD.Print($"[GameData] Loaded {numNpcs} NPC body+head indices");
        return (bodies, heads);
    }

    /// <summary>Load Personajes.ind from an AopakReader (INIT/Personajes.ind entry).</summary>
    public static (int[] grhs, int[] headOfsX, int[] headOfsY) LoadBodyData(AopakReader initsReader)
    {
        const string entryName = "INIT/Personajes.ind";
        if (!initsReader.Contains(entryName))
            return (Array.Empty<int>(), Array.Empty<int>(), Array.Empty<int>());
        var data = initsReader.ReadEntry(entryName);
        return ParseBodyData(data);
    }

    /// <summary>
    /// Load Personajes.ind binary — returns south-facing walk GRH and head offsets.
    /// Format: 263B header + i16 count + (i16[4] walk + i16 headOfsX + i16 headOfsY) per entry.
    /// </summary>
    public static (int[] grhs, int[] headOfsX, int[] headOfsY) LoadBodyData(string personajesPath)
    {
        if (!File.Exists(personajesPath))
            return (Array.Empty<int>(), Array.Empty<int>(), Array.Empty<int>());
        return ParseBodyData(File.ReadAllBytes(personajesPath));
    }

    private static (int[] grhs, int[] headOfsX, int[] headOfsY) ParseBodyData(byte[] data)
    {
        using var reader = new BinaryReader(new MemoryStream(data));

        reader.BaseStream.Seek(263, SeekOrigin.Begin); // MiCabecera
        short count = reader.ReadInt16();
        if (count <= 0)
            return (Array.Empty<int>(), Array.Empty<int>(), Array.Empty<int>());

        var grhs = new int[count + 1];
        var ofsX = new int[count + 1];
        var ofsY = new int[count + 1];
        for (int i = 1; i <= count; i++)
        {
            reader.ReadInt16(); // Walk North
            reader.ReadInt16(); // Walk East
            grhs[i] = reader.ReadInt16(); // Walk South — used for preview
            reader.ReadInt16(); // Walk West
            ofsX[i] = reader.ReadInt16(); // HeadOffsetX
            ofsY[i] = reader.ReadInt16(); // HeadOffsetY
        }

        GD.Print($"[GameData] Loaded {count} body data (GRH + head offsets)");
        return (grhs, ofsX, ofsY);
    }

    /// <summary>Load Cabezas.ind from an AopakReader (INIT/Cabezas.ind entry).</summary>
    public static int[] LoadHeadGrhs(AopakReader initsReader)
    {
        const string entryName = "INIT/Cabezas.ind";
        if (!initsReader.Contains(entryName)) return Array.Empty<int>();
        return ParseHeadGrhs(initsReader.ReadEntry(entryName));
    }

    /// <summary>
    /// Load Cabezas.ind binary — returns south-facing head GRH for each head index.
    /// Format: 263B header + i16 count + (i16[4] per direction) per entry.
    /// </summary>
    public static int[] LoadHeadGrhs(string cabezasPath)
    {
        if (!File.Exists(cabezasPath)) return Array.Empty<int>();
        return ParseHeadGrhs(File.ReadAllBytes(cabezasPath));
    }

    private static int[] ParseHeadGrhs(byte[] data)
    {
        using var reader = new BinaryReader(new MemoryStream(data));

        reader.BaseStream.Seek(263, SeekOrigin.Begin);
        short count = reader.ReadInt16();
        if (count <= 0) return Array.Empty<int>();

        var heads = new int[count + 1];
        for (int i = 1; i <= count; i++)
        {
            reader.ReadInt16(); // North
            reader.ReadInt16(); // East
            heads[i] = reader.ReadInt16(); // South — used for preview
            reader.ReadInt16(); // West
        }

        GD.Print($"[GameData] Loaded {count} head south-facing GRHs");
        return heads;
    }

    /// <summary>
    /// Load door-relevant data from Obj.dat. Returns dictionary of obj_index -> DoorInfo
    /// for objects with ObjType=10 (Door).
    /// </summary>
    public static Dictionary<int, DoorInfo> LoadDoorData(string objDatPath)
    {
        var doors = new Dictionary<int, DoorInfo>();
        if (!File.Exists(objDatPath)) return doors;

        var sections = ParseIniFile(objDatPath);

        int numObjs = 0;
        if (sections.TryGetValue("INIT", out var init))
            if (init.TryGetValue("NumOBJs", out var n))
                int.TryParse(n, out numObjs);

        for (int i = 1; i <= numObjs; i++)
        {
            if (!sections.TryGetValue($"OBJ{i}", out var sec)) continue;

            int objType = 0;
            if (sec.TryGetValue("ObjType", out var ot))
                int.TryParse(ot, out objType);

            if (objType != 6) continue; // Only doors (ObjType 6 = otPuertas in VB6)

            var door = new DoorInfo { ObjType = objType };
            if (sec.TryGetValue("IndexAbierta", out var ia)) int.TryParse(ia, out door.IndexAbierta);
            if (sec.TryGetValue("IndexCerrada", out var ic)) int.TryParse(ic, out door.IndexCerrada);
            if (sec.TryGetValue("IndexCerradaLlave", out var icl)) int.TryParse(icl, out door.IndexCerradaLlave);
            if (sec.TryGetValue("PuertaDoble", out var pd)) int.TryParse(pd, out door.PuertaDoble);
            if (sec.TryGetValue("Porton", out var po)) int.TryParse(po, out door.Porton);
            if (sec.TryGetValue("abierta", out var ab)) int.TryParse(ab, out door.Abierta);
            if (sec.TryGetValue("Llave", out var ll)) int.TryParse(ll, out door.Llave);
            if (sec.TryGetValue("GrhIndex", out var gi)) int.TryParse(gi, out door.GrhIndex);
            doors[i] = door;
        }

        GD.Print($"[GameData] Loaded {doors.Count} door definitions");
        return doors;
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
