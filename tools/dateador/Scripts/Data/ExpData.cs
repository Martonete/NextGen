#nullable enable
using System.IO;

namespace AODateador.Data;

public class ExpTable
{
    public const int MaxLevel = 255;

    /// <summary>
    /// Experience required per level. Index 1..255 are valid; index 0 is unused.
    /// </summary>
    public long[] Levels { get; } = new long[MaxLevel + 1];

    public static ExpTable Load(string datDir)
    {
        var table = new ExpTable();
        var path = Path.Combine(datDir, "Experiencia.dat");
        var ini = IniFile.Load(path);

        for (int lvl = 1; lvl <= MaxLevel; lvl++)
            table.Levels[lvl] = ini.GetLong("EXPERIENCIA", $"Nivel{lvl}");

        return table;
    }

    public void Save(string datDir)
    {
        var path = Path.Combine(datDir, "Experiencia.dat");
        var ini = new IniFile();

        for (int lvl = 1; lvl <= MaxLevel; lvl++)
            ini.Set("EXPERIENCIA", $"Nivel{lvl}", Levels[lvl]);

        ini.Save(path);
    }
}
