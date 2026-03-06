#nullable enable
using System;
using System.IO;
using Godot;

namespace AOWorldEditor.Data;

/// <summary>
/// Parses Graficos.ind binary file (same format as client).
/// Auto-detects header variants (with/without MiCabecera).
/// </summary>
public static class GrhLoader
{
    private const int MiCabeceraSize = 263;

    public static GrhData[] Load(string path)
    {
        byte[] fileData = File.ReadAllBytes(path);
        using var reader = new BinaryReader(new MemoryStream(fileData));

        int version = reader.ReadInt32();
        int grhCount = reader.ReadInt32();

        if (grhCount <= 0 || grhCount > 100000)
        {
            reader.BaseStream.Seek(MiCabeceraSize, SeekOrigin.Begin);
            version = reader.ReadInt32();
            grhCount = reader.ReadInt32();
        }

        if (grhCount <= 0 || grhCount > 100000)
        {
            reader.BaseStream.Seek(0, SeekOrigin.Begin);
            reader.ReadByte();
            version = reader.ReadInt32();
            grhCount = reader.ReadInt32();
        }

        GD.Print($"[GRH] Loading {grhCount} graphics (version {version})");

        var grhs = new GrhData[grhCount + 1];
        for (int i = 0; i < grhs.Length; i++)
            grhs[i] = new GrhData();

        int loaded = 0;
        while (reader.BaseStream.Position < reader.BaseStream.Length - 4)
        {
            int grhIndex;
            try { grhIndex = reader.ReadInt32(); }
            catch (EndOfStreamException) { break; }

            if (grhIndex <= 0) break;
            if (grhIndex >= grhs.Length)
            {
                var newGrhs = new GrhData[grhIndex + 100];
                Array.Copy(grhs, newGrhs, grhs.Length);
                for (int i = grhs.Length; i < newGrhs.Length; i++)
                    newGrhs[i] = new GrhData();
                grhs = newGrhs;
            }

            short numFrames = reader.ReadInt16();
            grhs[grhIndex].NumFrames = numFrames;

            if (numFrames > 1)
            {
                var frames = new int[numFrames];
                for (int f = 0; f < numFrames; f++)
                    frames[f] = reader.ReadInt32();
                grhs[grhIndex].Frames = frames;
                grhs[grhIndex].Speed = reader.ReadSingle();

                if (frames[0] > 0 && frames[0] < grhs.Length)
                {
                    var first = grhs[frames[0]];
                    grhs[grhIndex].PixelWidth = first.PixelWidth;
                    grhs[grhIndex].PixelHeight = first.PixelHeight;
                    grhs[grhIndex].FileNum = first.FileNum;
                    grhs[grhIndex].SX = first.SX;
                    grhs[grhIndex].SY = first.SY;
                    grhs[grhIndex].TileWidth = first.TileWidth;
                    grhs[grhIndex].TileHeight = first.TileHeight;
                }
            }
            else
            {
                grhs[grhIndex].FileNum = reader.ReadInt32();
                grhs[grhIndex].SX = reader.ReadInt16();
                grhs[grhIndex].SY = reader.ReadInt16();
                grhs[grhIndex].PixelWidth = reader.ReadInt16();
                grhs[grhIndex].PixelHeight = reader.ReadInt16();
                grhs[grhIndex].TileWidth = grhs[grhIndex].PixelWidth / 32f;
                grhs[grhIndex].TileHeight = grhs[grhIndex].PixelHeight / 32f;
                grhs[grhIndex].Frames = new int[] { grhIndex };
            }
            loaded++;
        }

        // Second pass: resolve animation dimensions
        for (int i = 1; i < grhs.Length; i++)
        {
            if (grhs[i].NumFrames > 1 && grhs[i].PixelWidth == 0 && grhs[i].Frames != null)
            {
                int firstIdx = grhs[i].Frames[0];
                if (firstIdx > 0 && firstIdx < grhs.Length)
                {
                    grhs[i].PixelWidth = grhs[firstIdx].PixelWidth;
                    grhs[i].PixelHeight = grhs[firstIdx].PixelHeight;
                    grhs[i].FileNum = grhs[firstIdx].FileNum;
                    grhs[i].SX = grhs[firstIdx].SX;
                    grhs[i].SY = grhs[firstIdx].SY;
                    grhs[i].TileWidth = grhs[firstIdx].TileWidth;
                    grhs[i].TileHeight = grhs[firstIdx].TileHeight;
                }
            }
        }

        GD.Print($"[GRH] Loaded {loaded} graphics");
        return grhs;
    }
}
