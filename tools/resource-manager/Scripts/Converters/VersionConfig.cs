#nullable enable
using System.Collections.Generic;

namespace AOResourceConverter.Converters;

public enum AoVersion
{
    V099z,
    V115,
    V123,
    V133,
}

/// <summary>
/// Version-specific path conventions and expected files for each AO release.
/// </summary>
public static class VersionConfig
{
    public static string Label(AoVersion v) => v switch
    {
        AoVersion.V099z => "0.99z",
        AoVersion.V115  => "0.11.5",
        AoVersion.V123  => "0.12.3",
        AoVersion.V133  => "0.13.3",
        _ => "Unknown",
    };

    /// <summary>Expected INIT .ind/.dat files for each version.</summary>
    public static string[] ExpectedInits(AoVersion v) => v switch
    {
        AoVersion.V099z => new[]
        {
            "Graficos.ind", "Personajes.ind", "Cabezas.ind", "Cascos.ind",
            "Fxs.ind", "Armas.dat", "Escudos.dat",
        },
        AoVersion.V115 => new[]
        {
            "Graficos.ind", "Personajes.ind", "Cabezas.ind", "Cascos.ind",
            "Fxs.ind", "Armas.dat", "Escudos.dat",
        },
        AoVersion.V123 => new[]
        {
            // 0.12.3 splits Graficos.ind into 3 parts
            "Graficos1.ind", "Graficos2.ind", "Graficos3.ind",
            "Personajes.ind", "Cabezas.ind", "Cascos.ind",
            "Fxs.ind", "Armas.dat", "Escudos.dat",
        },
        AoVersion.V133 => new[]
        {
            "Graficos.ind", "Personajes.ind", "Cabezas.ind", "Cascos.ind",
            "Fxs.ind", "Armas.dat", "Escudos.dat",
        },
        _ => System.Array.Empty<string>(),
    };

    /// <summary>Expected server Dat files common to all versions.</summary>
    public static string[] ExpectedDats(AoVersion v)
    {
        var common = new List<string>
        {
            "NPCs.dat", "Hechizos.dat", "Map.dat",
            "ArmasHerrero.dat", "ArmadurasHerrero.dat", "ObjCarpintero.dat",
        };

        // obj.dat has inconsistent casing across versions
        common.Add(v == AoVersion.V099z ? "OBJ.DAT" : "obj.dat");

        if (v >= AoVersion.V123)
            common.Add("Balance.dat");
        if (v == AoVersion.V133)
        {
            common.Add("Pretorianos.dat");
            common.Add("ArmadurasFaccionarias.dat");
        }

        return common.ToArray();
    }

    /// <summary>Whether this version stores graphics in a Graphics.AO archive instead of loose BMPs.</summary>
    public static bool UsesGraphicsArchive(AoVersion v) => v == AoVersion.V123;

    /// <summary>Map tile format for this version.</summary>
    public static MapTileFormat GetMapFormat(AoVersion v) => v switch
    {
        AoVersion.V099z => MapTileFormat.Fixed_099z,
        _ => MapTileFormat.Variable_Int16,
    };
}

public enum MapTileFormat
{
    Fixed_099z,      // 13 bytes/tile, no byFlags
    Variable_Int16,  // byFlags + Int16 layers
}
