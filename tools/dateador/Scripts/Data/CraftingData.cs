#nullable enable
using System.Collections.Generic;
using System.IO;

namespace AODateador.Data;

public class CraftingData
{
    public List<int> SmithWeapons { get; } = new();
    public List<int> SmithArmors { get; } = new();
    public List<int> CarpenterItems { get; } = new();

    public static CraftingData Load(string datDir)
    {
        var data = new CraftingData();

        LoadList(
            Path.Combine(datDir, "ArmasHerrero.dat"),
            "INIT", "NumArmas",
            data.SmithWeapons);

        LoadList(
            Path.Combine(datDir, "ArmadurasHerrero.dat"),
            "INIT", "NumArmaduras",
            data.SmithArmors);

        LoadList(
            Path.Combine(datDir, "ObjCarpintero.dat"),
            "INIT", "NumOBJs",
            data.CarpenterItems);

        return data;
    }

    public void Save(string datDir)
    {
        SaveList(
            Path.Combine(datDir, "ArmasHerrero.dat"),
            "INIT", "NumArmas",
            SmithWeapons);

        SaveList(
            Path.Combine(datDir, "ArmadurasHerrero.dat"),
            "INIT", "NumArmaduras",
            SmithArmors);

        SaveList(
            Path.Combine(datDir, "ObjCarpintero.dat"),
            "INIT", "NumOBJs",
            CarpenterItems);
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static void LoadList(string path, string initSection, string countKey, List<int> target)
    {
        var ini = IniFile.Load(path);
        int count = ini.GetInt(initSection, countKey);

        for (int i = 1; i <= count; i++)
        {
            var sec = i.ToString();
            if (!ini.HasSection(sec)) continue;

            // Each numbered section contains a single index key (first key found).
            foreach (var key in ini.Keys(sec))
            {
                if (int.TryParse(ini.GetString(sec, key), out int itemIndex))
                    target.Add(itemIndex);
                break;
            }
        }
    }

    private static void SaveList(string path, string initSection, string countKey, List<int> items)
    {
        var ini = new IniFile();
        ini.Set(initSection, countKey, items.Count);

        for (int i = 0; i < items.Count; i++)
        {
            var sec = (i + 1).ToString();
            ini.Set(sec, "Indice", items[i]);
        }

        ini.Save(path);
    }
}
