#nullable enable
using System;
using System.IO;
using Godot;

namespace AOWorldEditor.Data;

/// <summary>
/// Loads and saves AO binary map files (.map + .inf + .dat).
/// </summary>
public static class MapLoader
{
    private const int MapHeaderSize = 273;
    private const int InfHeaderSize = 10;

    public static MapData Load(string mapDir, int mapNumber)
    {
        var mapData = new MapData();
        mapData.MapNumber = mapNumber;

        string mapFile = Path.Combine(mapDir, $"Mapa{mapNumber}.map");
        string infFile = Path.Combine(mapDir, $"Mapa{mapNumber}.inf");
        string datFile = Path.Combine(mapDir, $"Mapa{mapNumber}.dat");

        if (File.Exists(mapFile))
            LoadMapFile(mapFile, mapData);
        if (File.Exists(infFile))
            LoadInfFile(infFile, mapData);
        if (File.Exists(datFile))
            LoadDatFile(datFile, mapData, mapNumber);

        return mapData;
    }

    /// Save all map files (.map, .inf, .dat) to the given directory.
    public static void Save(string mapDir, MapData mapData)
    {
        Save(mapDir, mapData, mapOnly: false);
    }

    /// Save map files. If mapOnly=true, only writes .map (client-side graphics).
    public static void Save(string mapDir, MapData mapData, bool mapOnly)
    {
        string mapFile = Path.Combine(mapDir, $"Mapa{mapData.MapNumber}.map");
        SaveMapFile(mapFile, mapData);

        if (!mapOnly)
        {
            string infFile = Path.Combine(mapDir, $"Mapa{mapData.MapNumber}.inf");
            string datFile = Path.Combine(mapDir, $"Mapa{mapData.MapNumber}.dat");
            SaveInfFile(infFile, mapData);
            SaveDatFile(datFile, mapData);
        }
    }

    #region Load

    private static void LoadMapFile(string path, MapData mapData)
    {
        byte[] fileData = File.ReadAllBytes(path);
        using var reader = new BinaryReader(new MemoryStream(fileData));
        reader.BaseStream.Seek(MapHeaderSize, SeekOrigin.Begin);

        for (int y = 1; y <= 100; y++)
        {
            for (int x = 1; x <= 100; x++)
            {
                if (reader.BaseStream.Position >= reader.BaseStream.Length) return;

                byte byFlags = reader.ReadByte();
                ref var tile = ref mapData.Tiles[x, y];

                tile.Blocked = (byFlags & 1) != 0;
                tile.Layer1 = reader.ReadInt16();
                tile.Layer2 = (byFlags & 2) != 0 ? reader.ReadInt16() : (short)0;
                tile.Layer3 = (byFlags & 4) != 0 ? reader.ReadInt16() : (short)0;
                tile.Layer4 = (byFlags & 8) != 0 ? reader.ReadInt16() : (short)0;
                tile.Trigger = (byFlags & 16) != 0 ? reader.ReadInt16() : (short)0;
                tile.ParticleGroup = (byFlags & 32) != 0 ? reader.ReadInt16() : (short)0;

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
        if (fileData.Length < InfHeaderSize + 1) return;

        using var reader = new BinaryReader(new MemoryStream(fileData));
        reader.BaseStream.Seek(InfHeaderSize, SeekOrigin.Begin);

        for (int y = 1; y <= 100; y++)
        {
            for (int x = 1; x <= 100; x++)
            {
                if (reader.BaseStream.Position >= reader.BaseStream.Length) return;

                byte byFlags = reader.ReadByte();
                ref var tile = ref mapData.Tiles[x, y];

                if ((byFlags & 1) != 0)
                {
                    tile.ExitMap = reader.ReadInt16();
                    tile.ExitX = reader.ReadInt16();
                    tile.ExitY = reader.ReadInt16();
                }
                if ((byFlags & 2) != 0)
                    tile.NpcIndex = reader.ReadInt16();
                if ((byFlags & 4) != 0)
                {
                    tile.ObjIndex = reader.ReadInt16();
                    tile.ObjAmount = reader.ReadInt16();
                }
            }
        }
    }

    private static void LoadDatFile(string path, MapData mapData, int mapNumber)
    {
        var cfg = new ConfigFile();
        if (cfg.Load(path) != Error.Ok) return;

        string section = $"Mapa{mapNumber}";
        if (!cfg.HasSection(section))
        {
            section = $"MAPA{mapNumber}";
            if (!cfg.HasSection(section)) return;
        }

        mapData.Name = cfg.GetValue(section, "Name", "").ToString() ?? "";
        mapData.MusicNum = (int)cfg.GetValue(section, "MusicNum", 0);
        mapData.PkEnabled = (int)cfg.GetValue(section, "Pk", 0) == 0;
        mapData.BackUp = (int)cfg.GetValue(section, "BackUp", 1) == 1;
        mapData.Terreno = cfg.GetValue(section, "Terreno", "TIERRA").ToString() ?? "TIERRA";
        mapData.Zona = cfg.GetValue(section, "Zona", "CAMPO").ToString() ?? "CAMPO";
        mapData.AmbientR = (byte)(int)cfg.GetValue(section, "R", 180);
        mapData.AmbientG = (byte)(int)cfg.GetValue(section, "G", 180);
        mapData.AmbientB = (byte)(int)cfg.GetValue(section, "B", 180);
    }

    #endregion

    #region Save

    private static void SaveMapFile(string path, MapData mapData)
    {
        using var ms = new MemoryStream();
        using var writer = new BinaryWriter(ms);

        // Header: version(2) + desc(255) + crc(4) + magic(4) + reserved(8) = 273
        writer.Write((short)1); // MapVersion
        writer.Write(new byte[255]); // MiCabecera (desc)
        writer.Write(0); // CRC
        writer.Write(0); // MagicWord
        writer.Write(new byte[8]); // Reserved

        int w = Math.Min(mapData.Width, 100);
        int h = Math.Min(mapData.Height, 100);

        for (int y = 1; y <= h; y++)
        {
            for (int x = 1; x <= w; x++)
            {
                ref var tile = ref mapData.Tiles[x, y];
                byte byFlags = 0;
                if (tile.Blocked) byFlags |= 1;
                if (tile.Layer2 != 0) byFlags |= 2;
                if (tile.Layer3 != 0) byFlags |= 4;
                if (tile.Layer4 != 0) byFlags |= 8;
                if (tile.Trigger != 0) byFlags |= 16;
                if (tile.ParticleGroup != 0) byFlags |= 32;
                if (tile.LightRange != 0) byFlags |= 64;

                writer.Write(byFlags);
                writer.Write(tile.Layer1);
                if (tile.Layer2 != 0) writer.Write(tile.Layer2);
                if (tile.Layer3 != 0) writer.Write(tile.Layer3);
                if (tile.Layer4 != 0) writer.Write(tile.Layer4);
                if (tile.Trigger != 0) writer.Write(tile.Trigger);
                if (tile.ParticleGroup != 0) writer.Write(tile.ParticleGroup);
                if (tile.LightRange != 0)
                {
                    writer.Write(tile.LightRange);
                    writer.Write(tile.LightR);
                    writer.Write(tile.LightG);
                    writer.Write(tile.LightB);
                }
            }
        }

        File.WriteAllBytes(path, ms.ToArray());
    }

    private static void SaveInfFile(string path, MapData mapData)
    {
        using var ms = new MemoryStream();
        using var writer = new BinaryWriter(ms);

        // Header: 5 x Int16 = 10 bytes
        for (int i = 0; i < 5; i++) writer.Write((short)0);

        int w = Math.Min(mapData.Width, 100);
        int h = Math.Min(mapData.Height, 100);

        for (int y = 1; y <= h; y++)
        {
            for (int x = 1; x <= w; x++)
            {
                ref var tile = ref mapData.Tiles[x, y];
                byte byFlags = 0;
                if (tile.ExitMap > 0) byFlags |= 1;
                if (tile.NpcIndex > 0) byFlags |= 2;
                if (tile.ObjIndex > 0) byFlags |= 4;

                writer.Write(byFlags);
                if (tile.ExitMap > 0)
                {
                    writer.Write(tile.ExitMap);
                    writer.Write(tile.ExitX);
                    writer.Write(tile.ExitY);
                }
                if (tile.NpcIndex > 0) writer.Write(tile.NpcIndex);
                if (tile.ObjIndex > 0)
                {
                    writer.Write(tile.ObjIndex);
                    writer.Write(tile.ObjAmount);
                }
            }
        }

        File.WriteAllBytes(path, ms.ToArray());
    }

    private static void SaveDatFile(string path, MapData mapData)
    {
        string section = $"Mapa{mapData.MapNumber}";
        var lines = new System.Collections.Generic.List<string>
        {
            $"[{section}]",
            $"Name={mapData.Name}",
            $"MusicNum={mapData.MusicNum}",
            $"Pk={(mapData.PkEnabled ? 0 : 1)}",
            $"BackUp={(mapData.BackUp ? 1 : 0)}",
            $"Terreno={mapData.Terreno}",
            $"Zona={mapData.Zona}",
            $"RestringirNavegar=0",
            $"NoEncriptarMP=0",
            $"R={mapData.AmbientR}",
            $"G={mapData.AmbientG}",
            $"B={mapData.AmbientB}"
        };
        File.WriteAllLines(path, lines);
    }

    #endregion
}
