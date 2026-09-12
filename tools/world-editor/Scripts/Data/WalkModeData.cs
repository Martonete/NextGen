#nullable enable
using System;
using System.IO;
using Godot;

namespace AOWorldEditor.Data;

public class BodyAnimData
{
    public int[] Walk = new int[5]; // [1]=N [2]=E [3]=S [4]=W, [0] unused
    public short HeadOffsetX;
    public short HeadOffsetY;
}

public class HeadAnimData
{
    public int[] Head = new int[5]; // [1]=N [2]=E [3]=S [4]=W, [0] unused
}

/// <summary>
/// Full four-heading body/head tables, shared by Modo Caminata and the in-viewport
/// character preview. Goes through GameDataLoader.ReadTableHeader so the wide
/// (Int32 GRH, "-32000" magic) layout parses the same as the legacy Int16 one —
/// the old Seek(263)+ReadInt16 path silently mis-read wide-format catalogues.
/// </summary>
public static class WalkModeData
{
    public static BodyAnimData[] LoadBodies(string personajesPath)
    {
        if (!File.Exists(personajesPath)) return Array.Empty<BodyAnimData>();

        using var reader = new BinaryReader(new MemoryStream(File.ReadAllBytes(personajesPath)));
        var (count, wide) = GameDataLoader.ReadTableHeader(reader);
        if (count <= 0) return Array.Empty<BodyAnimData>();

        var bodies = new BodyAnimData[count + 1];
        for (int i = 0; i <= count; i++)
            bodies[i] = new BodyAnimData();

        int entrySize = (wide ? 4 : 2) * 4 + 4;
        for (int i = 1; i <= count; i++)
        {
            if (reader.BaseStream.Position + entrySize > reader.BaseStream.Length) break;
            bodies[i].Walk[1] = GameDataLoader.ReadGrh(reader, wide); // North
            bodies[i].Walk[2] = GameDataLoader.ReadGrh(reader, wide); // East
            bodies[i].Walk[3] = GameDataLoader.ReadGrh(reader, wide); // South
            bodies[i].Walk[4] = GameDataLoader.ReadGrh(reader, wide); // West
            bodies[i].HeadOffsetX = reader.ReadInt16();
            bodies[i].HeadOffsetY = reader.ReadInt16();
        }

        GD.Print($"[WalkMode] Loaded {count} body animations ({(wide ? "wide" : "legacy")})");
        return bodies;
    }

    public static HeadAnimData[] LoadHeads(string cabezasPath)
    {
        if (!File.Exists(cabezasPath)) return Array.Empty<HeadAnimData>();

        using var reader = new BinaryReader(new MemoryStream(File.ReadAllBytes(cabezasPath)));
        var (count, wide) = GameDataLoader.ReadTableHeader(reader);
        if (count <= 0) return Array.Empty<HeadAnimData>();

        var heads = new HeadAnimData[count + 1];
        for (int i = 0; i <= count; i++)
            heads[i] = new HeadAnimData();

        int entrySize = (wide ? 4 : 2) * 4;
        for (int i = 1; i <= count; i++)
        {
            if (reader.BaseStream.Position + entrySize > reader.BaseStream.Length) break;
            heads[i].Head[1] = GameDataLoader.ReadGrh(reader, wide); // North
            heads[i].Head[2] = GameDataLoader.ReadGrh(reader, wide); // East
            heads[i].Head[3] = GameDataLoader.ReadGrh(reader, wide); // South
            heads[i].Head[4] = GameDataLoader.ReadGrh(reader, wide); // West
        }

        GD.Print($"[WalkMode] Loaded {count} head animations ({(wide ? "wide" : "legacy")})");
        return heads;
    }
}
