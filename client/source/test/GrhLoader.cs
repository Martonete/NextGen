using System;
using System.IO;
using Godot;

namespace ArgentumNextgen.Data;

/// <summary>
/// Parses Graficos.ind binary file.
/// Two formats supported:
///   With MiCabecera: MiCabecera(263) + Version(4) + Count(4) + entries
///   Without MiCabecera: Version(4) + Count(4) + entries
/// Auto-detected by reading first bytes.
/// </summary>
public static class GrhLoader
{
    private const int MiCabeceraSize = 263; // 255 desc + 4 crc + 4 magic

    public static GrhData[] Load(string path)
    {
        byte[] fileData = File.ReadAllBytes(path);
        using var reader = new BinaryReader(new MemoryStream(fileData));

        // Auto-detect header: try without MiCabecera first
        int version = reader.ReadInt32();
        int grhCount = reader.ReadInt32();

        // Sanity check: if count is unreasonable, try with MiCabecera
        if (grhCount <= 0 || grhCount > 100000)
        {
            reader.BaseStream.Seek(MiCabeceraSize, SeekOrigin.Begin);
            version = reader.ReadInt32();
            grhCount = reader.ReadInt32();
        }

        // Still bad? Try alternate: first 4 bytes might be a flag byte
        if (grhCount <= 0 || grhCount > 100000)
        {
            reader.BaseStream.Seek(0, SeekOrigin.Begin);
            reader.ReadByte(); // skip 1 flag byte
            version = reader.ReadInt32();
            grhCount = reader.ReadInt32();
        }

        GD.Print($"[GRH] Loading {grhCount} graphics (version {version}, offset {reader.BaseStream.Position})");

        // Allocate array (indices can be sparse, use max + margin)
        var grhs = new GrhData[grhCount + 1];
        for (int i = 0; i < grhs.Length; i++)
            grhs[i] = new GrhData();

        int loaded = 0;
        while (reader.BaseStream.Position < reader.BaseStream.Length - 4)
        {
            int grhIndex;
            try
            {
                grhIndex = reader.ReadInt32();
            }
            catch (EndOfStreamException)
            {
                break;
            }

            if (grhIndex <= 0) break;
            if (grhIndex >= grhs.Length)
            {
                // Expand array
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
                // Animation: read frame indices + speed
                var frames = new int[numFrames];
                for (int f = 0; f < numFrames; f++)
                {
                    frames[f] = reader.ReadInt32();
                }
                grhs[grhIndex].Frames = frames;
                grhs[grhIndex].Speed = reader.ReadSingle(); // VB6 Single = 4-byte float

                // Inherit dimensions from first frame
                if (frames[0] > 0 && frames[0] < grhs.Length)
                {
                    var firstFrame = grhs[frames[0]];
                    grhs[grhIndex].PixelWidth = firstFrame.PixelWidth;
                    grhs[grhIndex].PixelHeight = firstFrame.PixelHeight;
                    grhs[grhIndex].FileNum = firstFrame.FileNum;
                    grhs[grhIndex].SX = firstFrame.SX;
                    grhs[grhIndex].SY = firstFrame.SY;
                }
            }
            else
            {
                // Static GRH
                grhs[grhIndex].FileNum = reader.ReadInt32();
                grhs[grhIndex].SX = reader.ReadInt16();
                grhs[grhIndex].SY = reader.ReadInt16();
                grhs[grhIndex].PixelWidth = reader.ReadInt16();
                grhs[grhIndex].PixelHeight = reader.ReadInt16();
                grhs[grhIndex].TileWidth = grhs[grhIndex].PixelWidth / 32f;
                grhs[grhIndex].TileHeight = grhs[grhIndex].PixelHeight / 32f;
                grhs[grhIndex].Frames = new int[] { grhIndex }; // Self-reference
            }

            loaded++;
        }

        // Second pass: resolve animation dimensions for entries whose first frame was loaded after them
        for (int i = 1; i < grhs.Length; i++)
        {
            var iFrames = grhs[i].Frames;
            if (grhs[i].NumFrames > 1 && grhs[i].PixelWidth == 0 && iFrames != null)
            {
                int firstIdx = iFrames[0];
                if (firstIdx > 0 && firstIdx < grhs.Length)
                {
                    grhs[i].PixelWidth = grhs[firstIdx].PixelWidth;
                    grhs[i].PixelHeight = grhs[firstIdx].PixelHeight;
                    grhs[i].FileNum = grhs[firstIdx].FileNum;
                    grhs[i].SX = grhs[firstIdx].SX;
                    grhs[i].SY = grhs[firstIdx].SY;
                }
            }
        }

        GD.Print($"[GRH] Loaded {loaded} graphics");
        return grhs;
    }
}

public class GrhData
{
    public short NumFrames;
    public int FileNum;
    public short SX;
    public short SY;
    public short PixelWidth;
    public short PixelHeight;
    public float TileWidth;
    public float TileHeight;
    public int[]? Frames;
    public float Speed;
}
