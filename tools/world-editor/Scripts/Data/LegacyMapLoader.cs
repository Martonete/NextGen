#nullable enable
using System;
using System.IO;
using Godot;

namespace AOWorldEditor.Data;

/// <summary>
/// Format families for legacy AO .map files.
/// All legacy formats use Int16 GRH indices (max ~32K).
/// </summary>
public enum LegacyMapFormat
{
    AutoDetect,
    Fixed_099z,      // 0.99z: fixed 13 bytes/tile, no byFlags
    Variable_Int16,  // 11.5 - 13.3: byFlags + Int16 layers
}

/// <summary>
/// Loads legacy AO .map and .inf files from any version (0.99z through 13.3).
/// Converts Int16 GRH indices to Int32 for compatibility with the editor's MapData format.
/// </summary>
public static class LegacyMapLoader
{
    private const int MapHeaderSize = 273;
    private const int InfHeaderSize = 10;

    // 0.99z fixed format: bloqueado(1) + grafs1-4(8) + trigger(2) + t1(2) = 13
    private const int FixedTileSize = 13;
    // 0.99z fixed .inf: dest(6) + npc(2) + obj(4) + t1(2) + t2(2) = 16
    private const int FixedInfTileSize = 16;

    /// <summary>
    /// Load a legacy .map file with optional companion .inf from the same directory.
    /// </summary>
    public static MapData Load(string mapPath, LegacyMapFormat format)
    {
        if (format == LegacyMapFormat.AutoDetect)
            format = DetectFormat(mapPath);

        var mapData = new MapData(); // 100x100 default

        switch (format)
        {
            case LegacyMapFormat.Fixed_099z:
                LoadMapFixed(mapPath, mapData);
                break;
            case LegacyMapFormat.Variable_Int16:
                LoadMapVariable(mapPath, mapData);
                break;
            default:
                throw new InvalidDataException($"Unknown legacy format: {format}");
        }

        // Try loading companion .inf
        string dir = Path.GetDirectoryName(mapPath) ?? "";
        string baseName = Path.GetFileNameWithoutExtension(mapPath);
        string infPath = Path.Combine(dir, baseName + ".inf");
        if (File.Exists(infPath))
        {
            switch (format)
            {
                case LegacyMapFormat.Fixed_099z:
                    LoadInfFixed(infPath, mapData);
                    break;
                case LegacyMapFormat.Variable_Int16:
                    LoadInfVariable(infPath, mapData);
                    break;
            }
        }

        return mapData;
    }

    /// <summary>
    /// Auto-detect the format based on file size heuristic.
    /// </summary>
    public static LegacyMapFormat DetectFormat(string mapPath)
    {
        long fileSize = new FileInfo(mapPath).Length;

        if (fileSize < MapHeaderSize + 1)
            throw new InvalidDataException($"File too small to be a valid .map ({fileSize} bytes): {mapPath}");

        // 0.99z fixed: header(273) + 13 bytes * 10,000 tiles = 130,273
        if (fileSize == MapHeaderSize + (long)FixedTileSize * 10_000)
            return LegacyMapFormat.Fixed_099z;

        // Otherwise assume variable Int16 (covers 11.5 through 13.3)
        return LegacyMapFormat.Variable_Int16;
    }

    /// <summary>
    /// Returns a user-friendly description for the detected format.
    /// </summary>
    public static string FormatLabel(LegacyMapFormat format) => format switch
    {
        LegacyMapFormat.Fixed_099z => "0.99z (fijo, 13 bytes/tile)",
        LegacyMapFormat.Variable_Int16 => "11.5 - 13.3 (variable, Int16)",
        _ => "Desconocido",
    };

    #region .map — Fixed format (0.99z)

    /// <summary>
    /// 0.99z format: 273-byte header, then 10,000 tiles of 13 bytes each (fixed size).
    /// VB6 Type TileMap (from mdlLeeMapas.bas):
    ///   bloqueado As Byte      ' offset +0  (1 byte)
    ///   grafs1    As Integer   ' offset +1  (2 bytes) — Layer 1 ground
    ///   grafs2    As Integer   ' offset +3  (2 bytes) — Layer 2
    ///   grafs3    As Integer   ' offset +5  (2 bytes) — Layer 3
    ///   grafs4    As Integer   ' offset +7  (2 bytes) — Layer 4
    ///   trigger   As Integer   ' offset +9  (2 bytes) — Trigger zone ID
    ///   t1        As Integer   ' offset +11 (2 bytes) — Unused padding
    /// Total: 13 bytes per tile.
    /// </summary>
    private static void LoadMapFixed(string path, MapData mapData)
    {
        byte[] fileData = File.ReadAllBytes(path);
        using var reader = new BinaryReader(new MemoryStream(fileData));
        reader.BaseStream.Seek(MapHeaderSize, SeekOrigin.Begin);

        for (int y = 1; y <= 100; y++)
        {
            for (int x = 1; x <= 100; x++)
            {
                if (reader.BaseStream.Position + FixedTileSize > reader.BaseStream.Length)
                    return;

                ref var tile = ref mapData.Tiles[x, y];

                byte bloqueado = reader.ReadByte();
                tile.Blocked = bloqueado != 0;
                tile.Layer1 = reader.ReadInt16();  // Int16 → stored as Int32 in MapTile
                tile.Layer2 = reader.ReadInt16();
                tile.Layer3 = reader.ReadInt16();
                tile.Layer4 = reader.ReadInt16();
                tile.Trigger = reader.ReadInt16();
                reader.ReadInt16(); // t1 — unused padding
            }
        }
    }

    #endregion

    #region .map — Variable format (11.5 - 13.3)

    /// <summary>
    /// 11.5-13.3 format: 273-byte header, then 10,000 variable-length tiles.
    /// byFlags(1) + Layer1(Int16 always) + optional layers/trigger/particle (all Int16).
    /// </summary>
    private static void LoadMapVariable(string path, MapData mapData)
    {
        byte[] fileData = File.ReadAllBytes(path);
        using var reader = new BinaryReader(new MemoryStream(fileData));
        reader.BaseStream.Seek(MapHeaderSize, SeekOrigin.Begin);

        for (int y = 1; y <= 100; y++)
        {
            for (int x = 1; x <= 100; x++)
            {
                if (reader.BaseStream.Position >= reader.BaseStream.Length)
                    return;

                byte byFlags = reader.ReadByte();
                ref var tile = ref mapData.Tiles[x, y];

                tile.Blocked = (byFlags & 1) != 0;
                tile.Layer1 = reader.ReadInt16();  // Always present, Int16

                tile.Layer2 = (byFlags & 2) != 0 ? reader.ReadInt16() : 0;
                tile.Layer3 = (byFlags & 4) != 0 ? reader.ReadInt16() : 0;
                tile.Layer4 = (byFlags & 8) != 0 ? reader.ReadInt16() : 0;
                tile.Trigger = (byFlags & 16) != 0 ? reader.ReadInt16() : (short)0;
                tile.ParticleGroup = (byFlags & 32) != 0 ? reader.ReadInt16() : (short)0;
            }
        }
    }

    #endregion

    #region .inf — Fixed format (0.99z)

    /// <summary>
    /// 0.99z .inf: 10-byte header, then 10,000 tiles of 16 bytes each.
    /// dest_map(2) + dest_x(2) + dest_y(2) + npc(2) + obj_ind(2) + obj_cant(2) + t1(2) + t2(2)
    /// </summary>
    private static void LoadInfFixed(string path, MapData mapData)
    {
        byte[] fileData = File.ReadAllBytes(path);
        using var reader = new BinaryReader(new MemoryStream(fileData));
        reader.BaseStream.Seek(InfHeaderSize, SeekOrigin.Begin);

        for (int y = 1; y <= 100; y++)
        {
            for (int x = 1; x <= 100; x++)
            {
                if (reader.BaseStream.Position + FixedInfTileSize > reader.BaseStream.Length)
                    return;

                ref var tile = ref mapData.Tiles[x, y];

                tile.ExitMap = reader.ReadInt16();
                tile.ExitX = reader.ReadInt16();
                tile.ExitY = reader.ReadInt16();
                tile.NpcIndex = reader.ReadInt16();
                tile.ObjIndex = reader.ReadInt16();
                tile.ObjAmount = reader.ReadInt16();
                reader.ReadInt16(); // t1 — reserved
                reader.ReadInt16(); // t2 — reserved
            }
        }
    }

    #endregion

    #region .inf — Variable format (11.5 - 13.3)

    /// <summary>
    /// 11.5-13.3 .inf: 10-byte header, then 10,000 variable-length tiles.
    /// byFlags(1) + optional exit(6)/npc(2)/obj(4), all Int16.
    /// </summary>
    private static void LoadInfVariable(string path, MapData mapData)
    {
        byte[] fileData = File.ReadAllBytes(path);
        if (fileData.Length < InfHeaderSize + 1) return;

        using var reader = new BinaryReader(new MemoryStream(fileData));
        reader.BaseStream.Seek(InfHeaderSize, SeekOrigin.Begin);

        for (int y = 1; y <= 100; y++)
        {
            for (int x = 1; x <= 100; x++)
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
}
