using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using ArgentumNextgen.Data.Resources;
using Godot;

namespace ArgentumNextgen.Data;

public class ObjInfo
{
    public int Index;
    public string Name = "";
    public int ObjType;
    public int GrhIndex;
    public int Value;
    public int MinHit;
    public int MaxHit;
    public int MinDef;
    public int MaxDef;
    public int MinMod;
    public int MaxMod;
    public int HechizoIndex;
    public int MochilaType;
    public bool Newbie;
    public bool Agarrable;
}

public static class ObjectLoader
{
    public static string LastSourcePath { get; private set; } = "";

    private static readonly string[] CandidatePaths =
    {
        "INIT/obj.dat",
        "INIT/Obj.dat",
        "obj.dat",
        "Obj.dat",
    };

    public static ObjInfo[] Load(IResourceProvider resources)
    {
        string? loosePath = FindLooseObjDat(resources.BasePath);
        string? path = null;
        string text;

        if (loosePath != null)
        {
            path = loosePath;
            text = DecodeObjDat(File.ReadAllBytes(loosePath));
        }
        else
        {
            foreach (string candidate in CandidatePaths)
            {
                if (resources.Exists(candidate))
                {
                    path = candidate;
                    break;
                }
            }

            if (path == null)
            {
                GD.PrintErr("[OBJ] obj.dat not found in client resources or local server/dat");
                return new ObjInfo[] { new() };
            }

            text = DecodeObjDat(resources.ReadBytes(path));
        }

        var objects = new List<ObjInfo> { new() };
        ObjInfo? current = null;

        foreach (string rawLine in text.Replace("\r", "").Split('\n'))
        {
            string line = rawLine.Trim();
            if (line.Length == 0 || line.StartsWith("'") || line.StartsWith(";"))
                continue;

            if (line.StartsWith("[OBJ", StringComparison.OrdinalIgnoreCase) && line.EndsWith("]"))
            {
                if (current != null)
                    AddObject(objects, current);

                string idText = line[4..^1];
                current = int.TryParse(idText, out int id)
                    ? new ObjInfo { Index = id }
                    : null;
                continue;
            }

            if (current == null) continue;
            int eq = line.IndexOf('=');
            if (eq <= 0) continue;

            string key = line[..eq].Trim();
            string value = line[(eq + 1)..].Trim();
            ApplyField(current, key, value);
        }

        if (current != null)
            AddObject(objects, current);

        LastSourcePath = path ?? "";
        GD.Print($"[OBJ] Loaded {objects.Count - 1} objects from {path}");
        return objects.ToArray();
    }

    public static ObjInfo[] LoadFromKnownLoosePaths()
    {
        string? path = FindLooseObjDat("");
        if (path == null)
        {
            GD.PrintErr("[OBJ] obj.dat not found in known loose paths");
            return new ObjInfo[] { new() };
        }

        return LoadFromFile(path);
    }

    public static ObjInfo[] LoadFromFile(string path)
    {
        string text = DecodeObjDat(File.ReadAllBytes(path));
        ObjInfo[] objects = Parse(text);
        LastSourcePath = path;
        GD.Print($"[OBJ] Loaded {CountObjects(objects)} objects from {path}");
        return objects;
    }

    public static int CountObjects(ObjInfo[]? objects)
    {
        if (objects == null) return 0;
        int count = 0;
        for (int i = 1; i < objects.Length; i++)
        {
            var obj = objects[i];
            if (obj != null && obj.Index > 0 && !string.IsNullOrWhiteSpace(obj.Name))
                count++;
        }
        return count;
    }

    private static string? FindLooseObjDat(string basePath)
    {
        string? fromBase = FindLooseObjDatFrom(basePath);
        if (fromBase != null)
            return fromBase;

        string projectPath = ProjectSettings.GlobalizePath("res://");
        string? fromProject = FindLooseObjDatFrom(projectPath);
        if (fromProject != null)
            return fromProject;

        string? fromApp = FindLooseObjDatFrom(AppContext.BaseDirectory);
        if (fromApp != null)
            return fromApp;

        string? fromCwd = FindLooseObjDatFrom(Directory.GetCurrentDirectory());
        if (fromCwd != null)
            return fromCwd;

        string documents = System.Environment.GetFolderPath(System.Environment.SpecialFolder.MyDocuments);
        return FindLooseObjDatFrom(Path.Combine(documents, "AORust", "argentum-nextgen"));
    }

    private static string? FindLooseObjDatFrom(string? startPath)
    {
        if (string.IsNullOrWhiteSpace(startPath))
            return null;

        var dir = new DirectoryInfo(Path.GetFullPath(startPath));
        for (int depth = 0; dir != null && depth < 8; depth++, dir = dir.Parent)
        {
            foreach (string candidate in LooseCandidates(dir.FullName))
            {
                if (File.Exists(candidate))
                    return candidate;
            }
        }

        return null;
    }

    private static IEnumerable<string> LooseCandidates(string dir)
    {
        yield return Path.Combine(dir, "server", "dat", "obj.dat");
        yield return Path.Combine(dir, "client", "Data", "INIT", "obj.dat");
        yield return Path.Combine(dir, "resources", "data", "INIT", "obj.dat");
        yield return Path.Combine(dir, "Data", "INIT", "obj.dat");
        yield return Path.Combine(dir, "dat", "obj.dat");
        yield return Path.Combine(dir, "INIT", "obj.dat");
        yield return Path.Combine(dir, "obj.dat");
    }

    private static void AddObject(List<ObjInfo> objects, ObjInfo obj)
    {
        while (objects.Count <= obj.Index)
            objects.Add(new ObjInfo());
        objects[obj.Index] = obj;
    }

    private static ObjInfo[] Parse(string text)
    {
        var objects = new List<ObjInfo> { new() };
        ObjInfo? current = null;

        foreach (string rawLine in text.Replace("\r", "").Split('\n'))
        {
            string line = rawLine.Trim();
            if (line.Length == 0 || line.StartsWith("'") || line.StartsWith(";"))
                continue;

            if (line.StartsWith("[OBJ", StringComparison.OrdinalIgnoreCase) && line.EndsWith("]"))
            {
                if (current != null)
                    AddObject(objects, current);

                string idText = line[4..^1];
                current = int.TryParse(idText, out int id)
                    ? new ObjInfo { Index = id }
                    : null;
                continue;
            }

            if (current == null) continue;
            int eq = line.IndexOf('=');
            if (eq <= 0) continue;

            string key = line[..eq].Trim();
            string value = line[(eq + 1)..].Trim();
            ApplyField(current, key, value);
        }

        if (current != null)
            AddObject(objects, current);

        return objects.ToArray();
    }

    private static string DecodeObjDat(byte[] data)
    {
        if (data.Length >= 2)
        {
            if (data[0] == 0xFF && data[1] == 0xFE)
                return Encoding.Unicode.GetString(data);
            if (data[0] == 0xFE && data[1] == 0xFF)
                return Encoding.BigEndianUnicode.GetString(data);
        }

        if (data.Length >= 3 && data[0] == 0xEF && data[1] == 0xBB && data[2] == 0xBF)
            return Encoding.UTF8.GetString(data);

        int sample = Math.Min(data.Length, 512);
        int oddZeroes = 0;
        int evenZeroes = 0;
        for (int i = 0; i < sample; i++)
        {
            if (data[i] != 0) continue;
            if ((i & 1) == 0)
                evenZeroes++;
            else
                oddZeroes++;
        }

        if (sample > 16 && oddZeroes > sample / 4 && evenZeroes < sample / 16)
            return Encoding.Unicode.GetString(data);
        if (sample > 16 && evenZeroes > sample / 4 && oddZeroes < sample / 16)
            return Encoding.BigEndianUnicode.GetString(data);

        return Encoding.Latin1.GetString(data);
    }

    private static void ApplyField(ObjInfo obj, string key, string value)
    {
        switch (key.ToUpperInvariant())
        {
            case "NAME": obj.Name = value; break;
            case "OBJTYPE": obj.ObjType = ToInt(value); break;
            case "GRHINDEX": obj.GrhIndex = ToInt(value); break;
            case "VALOR": obj.Value = ToInt(value); break;
            case "MINHIT": obj.MinHit = ToInt(value); break;
            case "MAXHIT": obj.MaxHit = ToInt(value); break;
            case "MINDEF": obj.MinDef = ToInt(value); break;
            case "MAXDEF": obj.MaxDef = ToInt(value); break;
            case "MINMOD": obj.MinMod = ToInt(value); break;
            case "MAXMOD": obj.MaxMod = ToInt(value); break;
            case "HECHIZO": obj.HechizoIndex = ToInt(value); break;
            case "MOCHILATYPE": obj.MochilaType = ToInt(value); break;
            case "NEWBIE": obj.Newbie = ToBool(value); break;
            case "AGARRABLE": obj.Agarrable = ToBool(value); break;
        }
    }

    private static int ToInt(string value) => int.TryParse(value, out int result) ? result : 0;
    private static bool ToBool(string value) => value == "1" || value.Equals("true", StringComparison.OrdinalIgnoreCase);
}
