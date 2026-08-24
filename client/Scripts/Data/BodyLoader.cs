using System;
using System.IO;
using ArgentumNextgen.Data.Resources;
using Godot;

namespace ArgentumNextgen.Data;

/// <summary>
/// Loads Personajes.ind, Cabezas.ind, Cascos.ind, Fxs.ind binary files.
///
/// Two layouts are supported, distinguished by a marker where the legacy format
/// keeps its count:
///   legacy: MiCabecera(263) + Count(Int16) + records with UInt16 GRHs
///   wide:   MiCabecera(263) + Magic(Int32) + Count(Int32) + records with Int32 GRHs
///
/// The legacy UInt16 capped the catalogue at 65535 GRHs, which is not enough to
/// index 3094 sheets of terrain cell by cell. Nothing outside the client reads
/// these files, so the wide layout lifts the cap.
/// </summary>
public static class BodyLoader
{
    private const int MiCabeceraSize = 263;

    /// <summary>Marker written by grhtool where the legacy count would sit.</summary>
    private const int WideFormatMagic = -32000;

    /// <summary>
    /// Positions the reader after the count and reports which layout this file
    /// uses, so each record is read at the right width.
    /// </summary>
    private static (int count, bool wide) ReadTableHeader(BinaryReader reader)
    {
        reader.BaseStream.Seek(MiCabeceraSize, SeekOrigin.Begin);
        int marker = reader.ReadInt32();
        if (marker == WideFormatMagic)
            return (reader.ReadInt32(), true);

        // Legacy: the count is the first 2 bytes of what we just read.
        reader.BaseStream.Seek(MiCabeceraSize, SeekOrigin.Begin);
        return (reader.ReadInt16(), false);
    }

    private static int ReadGrh(BinaryReader reader, bool wide)
        => wide ? reader.ReadInt32() : reader.ReadUInt16();

    /// <summary>
    /// Load Personajes.ind — body animations per direction + head offset.
    /// Per entry: 4×2 bytes (Walk GRH per direction) + 2 bytes HeadOffsetX + 2 bytes HeadOffsetY = 12 bytes
    /// </summary>
    public static BodyData[] LoadBodies(IResourceProvider resources)
    {
        byte[] fileData = resources.ReadBytes("INIT/Personajes.ind");
        using var reader = new BinaryReader(new MemoryStream(fileData));

        var (count, wide) = ReadTableHeader(reader);

        GD.Print($"[BODY] Loading {count} bodies ({(wide ? "32-bit" : "legacy")})");
        var bodies = new BodyData[count + 1];
        for (int i = 0; i <= count; i++)
            bodies[i] = new BodyData();

        for (int i = 1; i <= count; i++)
        {
            bodies[i].Walk[1] = ReadGrh(reader, wide); // North
            bodies[i].Walk[2] = ReadGrh(reader, wide); // East
            bodies[i].Walk[3] = ReadGrh(reader, wide); // South
            bodies[i].Walk[4] = ReadGrh(reader, wide); // West
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

        var (count, wide) = ReadTableHeader(reader);

        GD.Print($"[HEAD] Loading {count} heads ({(wide ? "32-bit" : "legacy")})");
        var heads = new HeadData[count + 1];
        for (int i = 0; i <= count; i++)
            heads[i] = new HeadData();

        for (int i = 1; i <= count; i++)
        {
            heads[i].Head = new int[5]; // 1-indexed
            heads[i].Head[1] = ReadGrh(reader, wide);
            heads[i].Head[2] = ReadGrh(reader, wide);
            heads[i].Head[3] = ReadGrh(reader, wide);
            heads[i].Head[4] = ReadGrh(reader, wide);
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

        var (count, wide) = ReadTableHeader(reader);

        GD.Print($"[CASCO] Loading {count} helmets ({(wide ? "32-bit" : "legacy")})");
        var cascos = new HeadData[count + 1];
        for (int i = 0; i <= count; i++)
            cascos[i] = new HeadData();

        for (int i = 1; i <= count; i++)
        {
            cascos[i].Head = new int[5];
            cascos[i].Head[1] = ReadGrh(reader, wide);
            cascos[i].Head[2] = ReadGrh(reader, wide);
            cascos[i].Head[3] = ReadGrh(reader, wide);
            cascos[i].Head[4] = ReadGrh(reader, wide);
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
