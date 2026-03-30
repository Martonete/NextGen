using System;
using System.IO;
using Godot;

namespace ArgentumNextgen.Data;

/// <summary>
/// VB6 tAuras type — loaded from Auras.dat INI file.
/// </summary>
public class AuraData
{
    public int GrhIndex;   // Animation GRH
    public byte R, G, B;   // Base color
    public byte RojoF, VerdeF, AzulF; // Pulsing start color
    public bool Giratoria; // Rotates?
    public int Offset;     // Y-axis offset from character head
}

public static class AuraLoader
{
    public static AuraData[] Load(string path)
    {
        if (!File.Exists(path))
        {
            GD.PrintErr($"[AURA] File not found: {path}");
            return new AuraData[] { new() };
        }

        var ini = SimpleIni.Parse(File.ReadAllText(path));
        int count = ini.GetInt("INIT", "NumAuras", 0);
        GD.Print($"[AURA] Loading {count} auras from Auras.dat");

        var auras = new AuraData[count + 1];
        for (int i = 0; i <= count; i++)
            auras[i] = new AuraData();

        for (int i = 1; i <= count; i++)
        {
            string section = $"AURA{i}";
            auras[i].GrhIndex = ini.GetInt(section, "GrhIndex", 0);
            auras[i].R = (byte)ini.GetInt(section, "Rojo", 0);
            auras[i].G = (byte)ini.GetInt(section, "Verde", 0);
            auras[i].B = (byte)ini.GetInt(section, "Azul", 0);
            auras[i].RojoF = (byte)ini.GetInt(section, "RojoF", 0);
            auras[i].VerdeF = (byte)ini.GetInt(section, "VerdeF", 0);
            auras[i].AzulF = (byte)ini.GetInt(section, "AzulF", 0);
            auras[i].Giratoria = ini.GetInt(section, "Giratoria", 0) != 0;
            auras[i].Offset = ini.GetInt(section, "Offset", 0);
        }

        GD.Print($"[AURA] Loaded {count} auras");
        return auras;
    }
}
