using System;
using System.IO;
using Godot;

namespace ArgentumNextgen.Data;

/// <summary>
/// Parses .map + .inf binary files for map tile data.
/// Map: header(273) + reserved(16) + tiles(100x100 variable)
/// Inf: header(10) + tiles(100x100 variable)
/// </summary>
public static class MapLoader
{
    private const int MapHeaderSize = 271; // 2(version) + 255(desc) + 4(crc) + 4(magic) + 4+4(reserved)
    private const int InfHeaderSize = 10; // 5 × Int16

    public static MapData Load(string mapDir, int mapNumber)
    {
        var mapData = new MapData();

        string mapFile = Path.Combine(mapDir, $"Mapa{mapNumber}.map");
        string infFile = Path.Combine(mapDir, $"Mapa{mapNumber}.inf");

        if (File.Exists(mapFile))
            LoadMapFile(mapFile, mapData);

        if (File.Exists(infFile))
            LoadInfFile(infFile, mapData);

        return mapData;
    }

    private static void LoadMapFile(string path, MapData mapData)
    {
        byte[] fileData = File.ReadAllBytes(path);
        using var reader = new BinaryReader(new MemoryStream(fileData));

        // Skip header: version(2) + MiCabecera(263) + reserved(4+4+4+4=16) = 281
        // VB6: MapVersion(Integer=2) + MiCabecera(263) + 4 reserved Longs(16) = 281
        reader.BaseStream.Seek(281, SeekOrigin.Begin);

        // Read tiles Y=1..100, X=1..100
        for (int y = 1; y <= 100; y++)
        {
            for (int x = 1; x <= 100; x++)
            {
                if (reader.BaseStream.Position >= reader.BaseStream.Length)
                    return;

                byte byFlags = reader.ReadByte();
                ref var tile = ref mapData.Tiles[x, y];

                tile.Blocked = (byFlags & 1) != 0;

                // Layer 1 always present
                tile.Layer1 = reader.ReadInt16();

                // Layer 2
                if ((byFlags & 2) != 0)
                    tile.Layer2 = reader.ReadInt16();
                else
                    tile.Layer2 = 0;

                // Layer 3
                if ((byFlags & 4) != 0)
                    tile.Layer3 = reader.ReadInt16();
                else
                    tile.Layer3 = 0;

                // Layer 4
                if ((byFlags & 8) != 0)
                    tile.Layer4 = reader.ReadInt16();
                else
                    tile.Layer4 = 0;

                // Trigger
                if ((byFlags & 16) != 0)
                    tile.Trigger = reader.ReadInt16();
                else
                    tile.Trigger = 0;

                // Particle
                if ((byFlags & 32) != 0)
                    tile.ParticleGroup = reader.ReadInt16();
                else
                    tile.ParticleGroup = 0;

                // Light
                if ((byFlags & 64) != 0)
                {
                    tile.LightRange = reader.ReadInt16();
                    tile.LightR = reader.ReadInt16();
                    tile.LightG = reader.ReadInt16();
                    tile.LightB = reader.ReadInt16();
                }
            }
        }
    }

    private static void LoadInfFile(string path, MapData mapData)
    {
        byte[] fileData = File.ReadAllBytes(path);
        using var reader = new BinaryReader(new MemoryStream(fileData));

        // Skip header: 5 × Int16 = 10 bytes
        reader.BaseStream.Seek(InfHeaderSize, SeekOrigin.Begin);

        for (int y = 1; y <= 100; y++)
        {
            for (int x = 1; x <= 100; x++)
            {
                if (reader.BaseStream.Position >= reader.BaseStream.Length)
                    return;

                byte byFlags = reader.ReadByte();
                ref var tile = ref mapData.Tiles[x, y];

                // Bit 0: TileExit
                if ((byFlags & 1) != 0)
                {
                    tile.ExitMap = reader.ReadInt16();
                    tile.ExitX = reader.ReadInt16();
                    tile.ExitY = reader.ReadInt16();
                }

                // Bit 1: NPC
                if ((byFlags & 2) != 0)
                {
                    tile.NpcIndex = reader.ReadInt16();
                }

                // Bit 2: Object
                if ((byFlags & 4) != 0)
                {
                    tile.ObjIndex = reader.ReadInt16();
                    tile.ObjAmount = reader.ReadInt16();
                }
            }
        }
    }
}

public class MapData
{
    public MapTile[,] Tiles = new MapTile[101, 101]; // 1-indexed, 100x100
}

public struct MapTile
{
    public bool Blocked;
    public short Layer1;  // GRH index
    public short Layer2;
    public short Layer3;
    public short Layer4;
    public short Trigger;
    public short ParticleGroup;
    public short LightRange;
    public short LightR;
    public short LightG;
    public short LightB;

    // .inf data
    public short ExitMap;
    public short ExitX;
    public short ExitY;
    public short NpcIndex;
    public short ObjIndex;
    public short ObjAmount;
}
