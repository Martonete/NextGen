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
    // .aomap/.aoinf header: magic(6) + version(2) + width(2) + height(2) + flags(4) = 16 bytes
    private static readonly byte[] AoMapMagic = Encoding.ASCII.GetBytes("AOMAP\0");
    private static readonly byte[] AoInfMagic = Encoding.ASCII.GetBytes("AOINF\0");

    public static MapData Load(string mapDir, int mapNumber)
    {
        string aomapFile = Path.Combine(mapDir, $"Mapa{mapNumber}.aomap");
        string mapFile = Path.Combine(mapDir, $"Mapa{mapNumber}.map");
        string datFile = Path.Combine(mapDir, $"Mapa{mapNumber}.dat");

        // Prefer the new format, but keep legacy fallback alive. Some maps still
        // exist only as .map/.inf, and stale/corrupt .aomap files should not make
        // the editor unusable when a valid legacy map is beside them.
        if (File.Exists(aomapFile))
        {
            try
            {
                var mapData = LoadAoMapFile(aomapFile);
                mapData.MapNumber = mapNumber;

                string aoinfFile = Path.Combine(mapDir, $"Mapa{mapNumber}.aoinf");
                if (File.Exists(aoinfFile))
                {
                    try
                    {
                        LoadAoInfFile(aoinfFile, mapData);
                    }
                    catch (Exception ex)
                    {
                        GD.PrintErr($"[MapLoader] Could not load {aoinfFile}: {ex.Message}");
                    }
                }

                if (File.Exists(datFile))
                    LoadDatFile(datFile, mapData, mapNumber);

                return mapData;
            }
            catch (Exception ex)
            {
                GD.PrintErr($"[MapLoader] Could not load {aomapFile}: {ex.Message}");
                if (!File.Exists(mapFile))
                    throw;
            }
        }

        if (File.Exists(mapFile))
        {
            var legacy = LegacyMapLoader.Load(mapFile, LegacyMapFormat.AutoDetect);
            legacy.MapNumber = mapNumber;
            if (File.Exists(datFile))
                LoadDatFile(datFile, legacy, mapNumber);
            return legacy;
        }

        throw new FileNotFoundException($"Mapa{mapNumber} no existe en {mapDir}");
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

    /// Save map files. If mapOnly=true, skips only .dat metadata; .aoinf is still
    /// written because the client needs it for exits, static objects, and NPCs.
    public static void Save(string mapDir, MapData mapData, bool mapOnly)
    {
        // Always save as .aomap format (Int32 layers) — legacy .map is deprecated
        string aomapFile = Path.Combine(mapDir, $"Mapa{mapData.MapNumber}.aomap");
        SaveAoMapFile(aomapFile, mapData);

        string aoinfFile = Path.Combine(mapDir, $"Mapa{mapData.MapNumber}.aoinf");
        SaveAoInfFile(aoinfFile, mapData);

        if (!mapOnly)
        {
            string datFile = Path.Combine(mapDir, $"Mapa{mapData.MapNumber}.dat");
            SaveDatFile(datFile, mapData);
        }

        // Remove legacy .map/.inf files if they exist to avoid stale files being loaded
        // on next open (LoadFromDir prefers .aomap but falls back to .map if .aomap is absent)
        string legacyMap = Path.Combine(mapDir, $"Mapa{mapData.MapNumber}.map");
        string legacyInf = Path.Combine(mapDir, $"Mapa{mapData.MapNumber}.inf");
        if (File.Exists(legacyMap)) File.Delete(legacyMap);
        if (File.Exists(legacyInf)) File.Delete(legacyInf);
    }

    #region Load — New Format (.aomap / .aoinf)

    private static MapData LoadAoMapFile(string path)
    {
        return LoadAoMapData(File.ReadAllBytes(path));
    }

    private static MapData LoadAoMapData(byte[] fileData)
    {
        using var reader = new BinaryReader(new MemoryStream(fileData));

        // Validate magic "AOMAP\0" (6 bytes)
        byte[] magic = reader.ReadBytes(6);
        for (int i = 0; i < 6; i++)
        {
            if (magic[i] != AoMapMagic[i])
                throw new InvalidDataException("Invalid .aomap magic bytes.");
        }

        ushort version = reader.ReadUInt16();
        if (version != 1)
            throw new InvalidDataException($"Unsupported .aomap version {version}.");

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
        LoadAoInfData(File.ReadAllBytes(path), mapData);
    }

    private static void LoadAoInfData(byte[] fileData, MapData mapData)
    {
        using var reader = new BinaryReader(new MemoryStream(fileData));

        // Validate magic "AOINF\0" (6 bytes)
        byte[] magic = reader.ReadBytes(6);
        for (int i = 0; i < 6; i++)
        {
            if (magic[i] != AoInfMagic[i])
                throw new InvalidDataException("Invalid .aoinf magic bytes.");
        }

        ushort version = reader.ReadUInt16();
        if (version != 1)
            throw new InvalidDataException($"Unsupported .aoinf version {version}.");

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

    #region Metadata (.dat)

    private static void LoadDatFile(string path, MapData mapData, int mapNumber)
    {
        string targetSection = $"MAPA{mapNumber}";
        bool inTargetSection = false;

        try
        {
            foreach (string rawLine in File.ReadLines(path, Encoding.Latin1))
            {
                string line = rawLine.Trim();
                if (line.Length == 0 || line[0] == ';' || line[0] == '#')
                    continue;

                if (line[0] == '[' && line[^1] == ']')
                {
                    bool wasInTargetSection = inTargetSection;
                    string section = line[1..^1].Trim();
                    inTargetSection = string.Equals(section, targetSection, StringComparison.OrdinalIgnoreCase);
                    if (wasInTargetSection && !inTargetSection)
                        break;
                    continue;
                }

                if (!inTargetSection)
                    continue;

                int eq = line.IndexOf('=');
                if (eq <= 0)
                    continue;

                string key = line[..eq].Trim();
                string value = line[(eq + 1)..].Trim();
                ApplyDatValue(mapData, key, value);
            }
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[MapLoader] Could not load metadata {path}: {ex.Message}");
        }
    }

    private static void ApplyDatValue(MapData mapData, string key, string value)
    {
        switch (key.ToUpperInvariant())
        {
            case "NAME":
                mapData.Name = value;
                break;
            case "MUSICNUM":
                mapData.MusicNum = ParseInt(value, mapData.MusicNum);
                break;
            case "PK":
                mapData.PkEnabled = ParseInt(value, mapData.PkEnabled ? 0 : 1) == 0;
                break;
            case "BACKUP":
                mapData.BackUp = ParseInt(value, mapData.BackUp ? 1 : 0) == 1;
                break;
            case "TERRENO":
                mapData.Terreno = value.Length > 0 ? value : mapData.Terreno;
                break;
            case "ZONA":
                mapData.Zona = value.Length > 0 ? value : mapData.Zona;
                break;
            case "R":
                mapData.AmbientR = ParseByte(value, mapData.AmbientR);
                break;
            case "G":
                mapData.AmbientG = ParseByte(value, mapData.AmbientG);
                break;
            case "B":
                mapData.AmbientB = ParseByte(value, mapData.AmbientB);
                break;
        }
    }

    private static int ParseInt(string value, int fallback)
    {
        return int.TryParse(value, out int parsed) ? parsed : fallback;
    }

    private static byte ParseByte(string value, byte fallback)
    {
        int parsed = ParseInt(value, fallback);
        return (byte)Math.Clamp(parsed, 0, 255);
    }

    #endregion

    #region Save — New Format (.aomap / .aoinf)

    private static void SaveAoMapFile(string path, MapData mapData)
    {
        File.WriteAllBytes(path, BuildAoMapBytes(mapData));
    }

    internal static byte[] BuildAoMapBytes(MapData mapData)
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

        writer.Flush();
        return ms.ToArray();
    }

    private static void SaveAoInfFile(string path, MapData mapData)
    {
        File.WriteAllBytes(path, BuildAoInfBytes(mapData));
    }

    internal static byte[] BuildAoInfBytes(MapData mapData)
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

        writer.Flush();
        return ms.ToArray();
    }

    #endregion

    #region Save Metadata (.dat)

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
        // Write with Latin-1 encoding to match LoadDatFile.
        File.WriteAllLines(path, lines, Encoding.Latin1);
    }

    #endregion
}
