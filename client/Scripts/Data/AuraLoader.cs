using System;
using System.IO;
using System.Text;
using ArgentumNextgen.Data.Resources;
using Godot;

namespace ArgentumNextgen.Data;

/// <summary>
/// VB6 tAuras type — loaded from Auras.dat INI file.
/// </summary>
public class AuraData
{
    public int ProceduralStyle; // 0=legacy GRH, 1=orbits, 2=petals, 3=solar, 4=crystals, 5=static GM warp, 6=wisps
    public int Radius = 22, Height = 40, CycleMs = 3000, Details = 5, Opacity = 65;
    public int GrhIndex;   // Animation GRH
    public byte R, G, B;   // Base color
    public byte RojoF, VerdeF, AzulF; // Pulsing start color
    public bool Giratoria; // Rotates?
    public int Offset;     // Y-axis offset from character head
}

public static class AuraLoader
{
    public static AuraData[] Load(IResourceProvider resources)
    {
        const string relativePath = "INIT/Auras.dat";
        if (!resources.Exists(relativePath))
        {
            GD.PrintErr($"[AURA] File not found: {relativePath}");
            return new AuraData[] { new() };
        }

        var ini = SimpleIni.Parse(Encoding.UTF8.GetString(resources.ReadBytes(relativePath)));
        int count = ini.GetInt("INIT", "NumAuras", 0);
        GD.Print($"[AURA] Loading {count} auras from Auras.dat");

        var auras = new AuraData[count + 1];
        for (int i = 0; i <= count; i++)
            auras[i] = new AuraData();

        for (int i = 1; i <= count; i++)
        {
            string section = $"AURA{i}";
            auras[i].ProceduralStyle = Math.Clamp(ini.GetInt(section, "ProceduralStyle", 0), 0, 6);
            auras[i].Radius = Math.Clamp(ini.GetInt(section, "Radius", 22), 8, 36);
            auras[i].Height = Math.Clamp(ini.GetInt(section, "Height", 40), 12, 64);
            auras[i].CycleMs = Math.Clamp(ini.GetInt(section, "CycleMs", 3000), 800, 12000);
            auras[i].Details = Math.Clamp(ini.GetInt(section, "Details", 5), 3, 8);
            auras[i].Opacity = Math.Clamp(ini.GetInt(section, "Opacity", 65), 0, 100);
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
