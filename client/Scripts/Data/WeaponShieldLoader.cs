using System;
using System.IO;
using System.Text;
using ArgentumNextgen.Data.Resources;
using Godot;

namespace ArgentumNextgen.Data;

/// <summary>
/// VB6: WeaponAnimData(1..NumArmas), ShieldAnimData(1..NumEscudos)
/// Loaded from INI files: Armas.dat and Escudos.dat.
/// Each entry has Dir1-Dir4 = GRH index per heading (1=N, 2=E, 3=S, 4=W).
/// </summary>
public class WeaponAnimDirs
{
    /// <summary>GRH indices per direction [1..4], 0 unused.</summary>
    public int[] Walk = new int[5]; // 1-indexed like VB6
}

public static class WeaponShieldLoader
{
    /// <summary>
    /// Load Armas.dat — weapon animation GRHs per direction.
    /// Format: [INIT] NumArmas=N, then [Arma1]..[ArmaN] with Dir1..Dir4.
    /// </summary>
    public static WeaponAnimDirs[] LoadWeapons(IResourceProvider resources)
    {
        const string relativePath = "INIT/Armas.dat";
        if (!resources.Exists(relativePath))
        {
            GD.PrintErr($"[WEAPON] File not found: {relativePath}");
            return new WeaponAnimDirs[] { new() };
        }

        var ini = SimpleIni.Parse(Encoding.UTF8.GetString(resources.ReadBytes(relativePath)));

        int count = ini.GetInt("INIT", "NumArmas", 0);
        GD.Print($"[WEAPON] Loading {count} weapons from Armas.dat");

        var weapons = new WeaponAnimDirs[count + 1];
        for (int i = 0; i <= count; i++)
            weapons[i] = new WeaponAnimDirs();

        for (int i = 1; i <= count; i++)
        {
            string section = $"Arma{i}";
            weapons[i].Walk[1] = ini.GetInt(section, "Dir1", 0);
            weapons[i].Walk[2] = ini.GetInt(section, "Dir2", 0);
            weapons[i].Walk[3] = ini.GetInt(section, "Dir3", 0);
            weapons[i].Walk[4] = ini.GetInt(section, "Dir4", 0);
        }

        GD.Print($"[WEAPON] Loaded {count} weapons");
        return weapons;
    }

    /// <summary>
    /// Load Escudos.dat — shield animation GRHs per direction.
    /// Format: [INIT] NumEscudos=N, then [ESC1]..[ESCN] with Dir1..Dir4.
    /// </summary>
    public static WeaponAnimDirs[] LoadShields(IResourceProvider resources)
    {
        const string relativePath = "INIT/Escudos.dat";
        if (!resources.Exists(relativePath))
        {
            GD.PrintErr($"[SHIELD] File not found: {relativePath}");
            return new WeaponAnimDirs[] { new() };
        }

        var ini = SimpleIni.Parse(Encoding.UTF8.GetString(resources.ReadBytes(relativePath)));

        int count = ini.GetInt("INIT", "NumEscudos", 0);
        GD.Print($"[SHIELD] Loading {count} shields from Escudos.dat");

        var shields = new WeaponAnimDirs[count + 1];
        for (int i = 0; i <= count; i++)
            shields[i] = new WeaponAnimDirs();

        for (int i = 1; i <= count; i++)
        {
            string section = $"ESC{i}";
            shields[i].Walk[1] = ini.GetInt(section, "Dir1", 0);
            shields[i].Walk[2] = ini.GetInt(section, "Dir2", 0);
            shields[i].Walk[3] = ini.GetInt(section, "Dir3", 0);
            shields[i].Walk[4] = ini.GetInt(section, "Dir4", 0);
        }

        GD.Print($"[SHIELD] Loaded {count} shields");
        return shields;
    }
}

/// <summary>
/// Minimal INI parser for VB6 .dat files.
/// Handles [Section] + Key=Value format with case-insensitive sections.
/// </summary>
internal class SimpleIni
{
    private readonly System.Collections.Generic.Dictionary<string, System.Collections.Generic.Dictionary<string, string>> _sections = new(StringComparer.OrdinalIgnoreCase);

    public static SimpleIni Parse(string text)
    {
        var ini = new SimpleIni();
        string currentSection = "";

        foreach (string rawLine in text.Split('\n'))
        {
            string line = rawLine.Trim('\r', ' ', '\t');
            if (line.Length == 0 || line[0] == ';') continue;

            if (line[0] == '[')
            {
                int end = line.IndexOf(']');
                if (end > 1)
                    currentSection = line[1..end];
                continue;
            }

            int eq = line.IndexOf('=');
            if (eq > 0 && currentSection.Length > 0)
            {
                string key = line[..eq].Trim();
                string val = line[(eq + 1)..].Trim();

                if (!ini._sections.TryGetValue(currentSection, out var dict))
                {
                    dict = new(StringComparer.OrdinalIgnoreCase);
                    ini._sections[currentSection] = dict;
                }
                dict[key] = val;
            }
        }

        return ini;
    }

    public int GetInt(string section, string key, int fallback = 0)
    {
        if (_sections.TryGetValue(section, out var dict) && dict.TryGetValue(key, out var val))
        {
            if (int.TryParse(val, out int result))
                return result;
        }
        return fallback;
    }
}
