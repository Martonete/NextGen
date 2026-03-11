#nullable enable
using System;
using System.IO;
using Godot;

namespace AOWorldEditor.Data;

public class BodyAnimData
{
    public short[] Walk = new short[5]; // [1]=N [2]=E [3]=S [4]=W, [0] unused
    public short HeadOffsetX;
    public short HeadOffsetY;
}

public class HeadAnimData
{
    public short[] Head = new short[5]; // [1]=N [2]=E [3]=S [4]=W, [0] unused
}

public static class WalkModeData
{
    private const int MiCabeceraSize = 263;

    public static BodyAnimData[] LoadBodies(string personajesPath)
    {
        if (!File.Exists(personajesPath)) return Array.Empty<BodyAnimData>();

        var data = File.ReadAllBytes(personajesPath);
        using var reader = new BinaryReader(new MemoryStream(data));

        reader.BaseStream.Seek(MiCabeceraSize, SeekOrigin.Begin);
        short count = reader.ReadInt16();
        if (count <= 0) return Array.Empty<BodyAnimData>();

        var bodies = new BodyAnimData[count + 1];
        for (int i = 0; i <= count; i++)
            bodies[i] = new BodyAnimData();

        for (int i = 1; i <= count; i++)
        {
            if (reader.BaseStream.Position + 12 > reader.BaseStream.Length) break;
            bodies[i].Walk[1] = reader.ReadInt16(); // North
            bodies[i].Walk[2] = reader.ReadInt16(); // East
            bodies[i].Walk[3] = reader.ReadInt16(); // South
            bodies[i].Walk[4] = reader.ReadInt16(); // West
            bodies[i].HeadOffsetX = reader.ReadInt16();
            bodies[i].HeadOffsetY = reader.ReadInt16();
        }

        GD.Print($"[WalkMode] Loaded {count} body animations");
        return bodies;
    }

    public static HeadAnimData[] LoadHeads(string cabezasPath)
    {
        if (!File.Exists(cabezasPath)) return Array.Empty<HeadAnimData>();

        var data = File.ReadAllBytes(cabezasPath);
        using var reader = new BinaryReader(new MemoryStream(data));

        reader.BaseStream.Seek(MiCabeceraSize, SeekOrigin.Begin);
        short count = reader.ReadInt16();
        if (count <= 0) return Array.Empty<HeadAnimData>();

        var heads = new HeadAnimData[count + 1];
        for (int i = 0; i <= count; i++)
            heads[i] = new HeadAnimData();

        for (int i = 1; i <= count; i++)
        {
            if (reader.BaseStream.Position + 8 > reader.BaseStream.Length) break;
            heads[i].Head[1] = reader.ReadInt16(); // North
            heads[i].Head[2] = reader.ReadInt16(); // East
            heads[i].Head[3] = reader.ReadInt16(); // South
            heads[i].Head[4] = reader.ReadInt16(); // West
        }

        GD.Print($"[WalkMode] Loaded {count} head animations");
        return heads;
    }
}
