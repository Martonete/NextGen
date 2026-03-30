#nullable enable
using System;
using System.IO;
using System.Text;
using Godot;

namespace AOWorldEditor.Data;

/// <summary>
/// Loads and saves AO binary map files.
/// Supports both legacy (.map + .inf + .dat) 100x100 format
/// and new (.aomap + .aoinf + .dat) dynamic-size format.
/// </summary>
public static class MapLoader
{
    private const int MapHeaderSize = 273;
    private const int InfHeaderSize = 10;

    // .aomap/.aoinf header: magic(6) + version(2) + width(2) + height(2) + flags(4) = 16 bytes
    private static readonly byte[] AoMapMagic = Encoding.ASCII.GetBytes("AOMAP\0");
    private static readonly byte[] AoInfMagic = Encoding.ASCII.GetBytes("AOINF\0");

    public static MapData Load(string mapDir, int mapNumber)
    {
        // Check for new format first (.aomap)
        string aomapFile = Path.Combine(mapDir, $"Mapa{mapNumber}.aomap");
        if (File.Exists(aomapFile))
        {
            var mapData = LoadAoMapFile(aomapFile);
            mapData.MapNumber = mapNumber;

            string aoinfFile = Path.Combine(mapDir, $"Mapa{mapNumber}.aoinf");
            if (File.Exists(aoinfFile))
                LoadAoInfFile(aoinfFile, mapData);

            string datFile = Path.Combine(mapDir, $"Mapa{mapNumber}.dat");
            if (File.Exists(datFile))
                LoadDatFile(datFile, mapData, mapNumber);

            return mapData;
        }

        // Legacy fallback (100x100)
        var legacy = new MapData();
        legacy.MapNumber = mapNumber;

        string mapFile = Path.Combine(mapDir, $"Mapa{mapNumber}.map");
        string infFile = Path.Combine(mapDir, $"Mapa{mapNumber}.inf");
        string datFileLegacy = Path.Combine(mapDir, $"Mapa{mapNumber}.dat");

        if (File.Exists(mapFile))
            LoadMapFile(mapFile, legacy);
        if (File.Exists(infFile))
            LoadInfFile(infFile, legacy);
        if (File.Exists(datFileLegacy))
            LoadDatFile(datFileLegacy, legacy, mapNumber);

        return legacy;
    }

    /// <summary>
    /// Load a .aomap file from an arbitrary path (not necessarily in the editor's map directory).
    /// Loads the companion .aoinf from the same directory if it exists.
    /// For legacy .map files (Int16 layers), use LegacyMapLoader.Load instead.
    /// </summary>
    public static MapData LoadStandalone(string mapFilePath)
    {
        string ext = Path.GetExtension(mapFilePath).ToLowerInvariant();
        if (ext != ".aomap")
            throw new NotSupportedException(
                $"LoadStandalone only handles .aomap files. For legacy .map files use LegacyMapLoader.Load. Path: {mapFilePath}");

        string dir = Path.GetDirectoryName(mapFilePath) ?? "";
        string baseName = Path.GetFileNameWithoutExtension(mapFilePath);

        var mapData = LoadAoMapFile(mapFilePath);

        string aoinfPath = Path.Combine(dir, baseName + ".aoinf");
        if (File.Exists(aoinfPath))
            LoadAoInfFile(aoinfPath, mapData);

        // .dat (metadata: name, music, ambient) is intentionally not loaded —
        // Insert Map only needs tile data, not map-level properties.

        return mapData;
    }

    /// Save all map files to the given directory.
    /// Uses .aomap/.aoinf format for maps larger than 100x100, legacy .map/.inf otherwise.
    public static void Save(string mapDir, MapData mapData)
    {
        Save(mapDir, mapData, mapOnly: false);
    }

    /// Save map files. If mapOnly=true, only writes the map file (client-side graphics).
    public static void Save(string mapDir, MapData mapData, bool mapOnly)
    {
        // Always save as .aomap format (Int32 layers) — legacy .map is deprecated
        string aomapFile = Path.Combine(mapDir, $"Mapa{mapData.MapNumber}.aomap");
        SaveAoMapFile(aomapFile, mapData);

        if (!mapOnly)
        {
            string aoinfFile = Path.Combine(mapDir, $"Mapa{mapData.MapNumber}.aoinf");
            string datFile = Path.Combine(mapDir, $"Mapa{mapData.MapNumber}.dat");
            SaveAoInfFile(aoinfFile, mapData);
            SaveDatFile(datFile, mapData);
        }
    }

    #region Load — New Format (.aomap / .aoinf)

    private static MapData LoadAoMapFile(string path)
    {
        byte[] fileData = File.ReadAllBytes(path);
        using var reader = new BinaryReader(new MemoryStream(fileData));

        // Validate magic "AOMAP\0" (6 bytes)
        byte[] magic = reader.ReadBytes(6);
        for (int i = 0; i < 6; i++)
        {
            if (magic[i] != AoMapMagic[i])
                throw new InvalidDataException($"Invalid .aomap magic in {path}");
        }

        ushort version = reader.ReadUInt16();
        if (version != 1)
            throw new InvalidDataException($"Unsupported .aomap version {version} in {path}");

        ushort width = reader.ReadUInt16();
        ushort height = reader.ReadUInt16();
        uint flags = reader.ReadUInt32(); // reserved, skip

        var mapData = new MapData(width, height);

        // Read tiles using same ByFlags format as legacy
        for (int y = 1; y <= height; y++)
        {
            for (int x = 1; x <= width; x++)
            {
                if (reader.BaseStream.Position >= reader.BaseStream.Length)
                    return mapData;

                byte byFlags = reader.ReadByte();
                ref var tile = ref mapData.Tiles[x, y];

                tile.Blocked = (byFlags & 1) != 0;
                tile.Layer1 = reader.ReadInt32();

                tile.Layer2 = (byFlags & 2) != 0 ? reader.ReadInt32() : 0;
                tile.Layer3 = (byFlags & 4) != 0 ? reader.ReadInt32() : 0;
                tile.Layer4 = (byFlags & 8) != 0 ? reader.ReadInt32() : 0;
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

        return mapData;
    }

    private static void LoadAoInfFile(string path, MapData mapData)
    {
        byte[] fileData = File.ReadAllBytes(path);
        using var reader = new BinaryReader(new MemoryStream(fileData));

        // Validate magic "AOINF\0" (6 bytes)
        byte[] magic = reader.ReadBytes(6);
        for (int i = 0; i < 6; i++)
        {
            if (magic[i] != AoInfMagic[i])
                throw new InvalidDataException($"Invalid .aoinf magic in {path}");
        }

        ushort version = reader.ReadUInt16();
        if (version != 1)
            throw new InvalidDataException($"Unsupported .aoinf version {version} in {path}");

        ushort width = reader.ReadUInt16();
        ushort height = reader.ReadUInt16();
        uint flags = reader.ReadUInt32(); // reserved, skip

        // Validate dimensions match the map
        if (width != mapData.Width || height != mapData.Height)
            GD.PrintErr($"[MapLoader] .aoinf dimensions ({width}x{height}) don't match .aomap ({mapData.Width}x{mapData.Height})");

        int readW = Math.Min(width, mapData.Width);
        int readH = Math.Min(height, mapData.Height);

        for (int y = 1; y <= readH; y++)
        {
            for (int x = 1; x <= readW; x++)
            {
                if (reader.BaseStream.Position >= reader.BaseStream.Length)
                    return;

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

    #endregion

    #region Load — Legacy (.map / .inf)

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
                tile.Layer1 = reader.ReadInt32();
                tile.Layer2 = (byFlags & 2) != 0 ? reader.ReadInt32() : 0;
                tile.Layer3 = (byFlags & 4) != 0 ? reader.ReadInt32() : 0;
                tile.Layer4 = (byFlags & 8) != 0 ? reader.ReadInt32() : 0;
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

    #region Save — New Format (.aomap / .aoinf)

    private static void SaveAoMapFile(string path, MapData mapData)
    {
        using var ms = new MemoryStream();
        using var writer = new BinaryWriter(ms);

        // Header: magic(6) + version(2) + width(2) + height(2) + flags(4) = 16 bytes
        writer.Write(AoMapMagic);
        writer.Write((ushort)1); // version
        writer.Write((ushort)mapData.Width);
        writer.Write((ushort)mapData.Height);
        writer.Write((uint)0); // flags (reserved)

        for (int y = 1; y <= mapData.Height; y++)
        {
            for (int x = 1; x <= mapData.Width; x++)
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

    private static void SaveAoInfFile(string path, MapData mapData)
    {
        using var ms = new MemoryStream();
        using var writer = new BinaryWriter(ms);

        // Header: magic(6) + version(2) + width(2) + height(2) + flags(4) = 16 bytes
        writer.Write(AoInfMagic);
        writer.Write((ushort)1); // version
        writer.Write((ushort)mapData.Width);
        writer.Write((ushort)mapData.Height);
        writer.Write((uint)0); // flags (reserved)

        for (int y = 1; y <= mapData.Height; y++)
        {
            for (int x = 1; x <= mapData.Width; x++)
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

    #endregion

    #region Save — Legacy (.map / .inf)

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
        string section = $"MAPA{mapData.MapNumber}";
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
