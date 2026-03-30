using System;
using System.IO;
using ArgentumNextgen.Data.Resources;
using Godot;

namespace ArgentumNextgen.Data;

/// <summary>
/// Loads Personajes.ind, Cabezas.ind, Cascos.ind, Fxs.ind binary files.
/// Format: MiCabecera(263) + Count(2 bytes Integer) + entries
/// </summary>
public static class BodyLoader
{
    private const int MiCabeceraSize = 263;

    /// <summary>
    /// Load Personajes.ind — body animations per direction + head offset.
    /// Per entry: 4×2 bytes (Walk GRH per direction) + 2 bytes HeadOffsetX + 2 bytes HeadOffsetY = 12 bytes
    /// </summary>
    public static BodyData[] LoadBodies(IResourceProvider resources)
    {
        byte[] fileData = resources.ReadBytes("INIT/Personajes.ind");
        using var reader = new BinaryReader(new MemoryStream(fileData));

        reader.BaseStream.Seek(MiCabeceraSize, SeekOrigin.Begin);
        short count = reader.ReadInt16();

        GD.Print($"[BODY] Loading {count} bodies");
        var bodies = new BodyData[count + 1];
        for (int i = 0; i <= count; i++)
            bodies[i] = new BodyData();

        for (int i = 1; i <= count; i++)
        {
            bodies[i].Walk[1] = reader.ReadUInt16(); // North
            bodies[i].Walk[2] = reader.ReadUInt16(); // East
            bodies[i].Walk[3] = reader.ReadUInt16(); // South
            bodies[i].Walk[4] = reader.ReadUInt16(); // West
            bodies[i].HeadOffsetX = reader.ReadInt16();
            bodies[i].HeadOffsetY = reader.ReadInt16();
        }

        GD.Print($"[BODY] Loaded {count} bodies");
        return bodies;
    }

    /// <summary>
    /// Load Cabezas.ind — head GRH per direction.
    /// Per entry: 8 bytes (4×Int16 directional GRHs). VB6 reads count entries, ignores rest of file.
    /// </summary>
    public static HeadData[] LoadHeads(IResourceProvider resources)
    {
        byte[] fileData = resources.ReadBytes("INIT/Cabezas.ind");
        using var reader = new BinaryReader(new MemoryStream(fileData));

        reader.BaseStream.Seek(MiCabeceraSize, SeekOrigin.Begin);
        short count = reader.ReadInt16();

        GD.Print($"[HEAD] Loading {count} heads");
        var heads = new HeadData[count + 1];
        for (int i = 0; i <= count; i++)
            heads[i] = new HeadData();

        for (int i = 1; i <= count; i++)
        {
            heads[i].Head = new int[5]; // 1-indexed
            heads[i].Head[1] = reader.ReadUInt16();
            heads[i].Head[2] = reader.ReadUInt16();
            heads[i].Head[3] = reader.ReadUInt16();
            heads[i].Head[4] = reader.ReadUInt16();
        }

        GD.Print($"[HEAD] Loaded {count} heads");
        return heads;
    }

    /// <summary>
    /// Load Cascos.ind — helmet GRH per direction.
    /// Per entry: 8 bytes (4×Int16 directional GRHs). VB6 reads count entries, ignores rest of file.
    /// </summary>
    public static HeadData[] LoadCascos(IResourceProvider resources)
    {
        byte[] fileData = resources.ReadBytes("INIT/Cascos.ind");
        using var reader = new BinaryReader(new MemoryStream(fileData));

        reader.BaseStream.Seek(MiCabeceraSize, SeekOrigin.Begin);
        short count = reader.ReadInt16();

        GD.Print($"[CASCO] Loading {count} helmets");
        var cascos = new HeadData[count + 1];
        for (int i = 0; i <= count; i++)
            cascos[i] = new HeadData();

        for (int i = 1; i <= count; i++)
        {
            cascos[i].Head = new int[5];
            cascos[i].Head[1] = reader.ReadUInt16();
            cascos[i].Head[2] = reader.ReadUInt16();
            cascos[i].Head[3] = reader.ReadUInt16();
            cascos[i].Head[4] = reader.ReadUInt16();
        }

        GD.Print($"[CASCO] Loaded {count} helmets");
        return cascos;
    }
}

public class BodyData
{
    public int[] Walk = new int[5];  // GRH indices per direction [1..4], 0 unused
    public short HeadOffsetX;
    public short HeadOffsetY;
}

public class HeadData
{
    public int[] Head = new int[5];  // GRH indices per direction [1..4], 0 unused
}
