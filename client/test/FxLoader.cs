using System;
using System.IO;
using Godot;

namespace ArgentumNextgen.Data;

/// <summary>
/// Loads Fxs.ind binary file.
/// Format: MiCabecera(263) + Count(2) + entries of 6 bytes each.
/// </summary>
public static class FxLoader
{
    private const int MiCabeceraSize = 263;

    public static FxData[] Load(string path)
    {
        byte[] fileData = File.ReadAllBytes(path);
        using var reader = new BinaryReader(new MemoryStream(fileData));

        reader.BaseStream.Seek(MiCabeceraSize, SeekOrigin.Begin);
        short count = reader.ReadInt16();

        GD.Print($"[FX] Loading {count} effects");
        var fxs = new FxData[count + 1];
        for (int i = 0; i <= count; i++)
            fxs[i] = new FxData();

        for (int i = 1; i <= count; i++)
        {
            fxs[i].Animacion = reader.ReadInt16();
            fxs[i].OffsetX = reader.ReadInt16();
            fxs[i].OffsetY = reader.ReadInt16();
        }

        GD.Print($"[FX] Loaded {count} effects");
        return fxs;
    }
}

public class FxData
{
    public short Animacion;
    public short OffsetX;
    public short OffsetY;
}
