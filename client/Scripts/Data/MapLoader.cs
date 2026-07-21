using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using ArgentumNextgen.Data.Resources;
using Godot;

namespace ArgentumNextgen.Data;

/// <summary>
/// Parses .map + .inf (legacy 100x100) and .aomap + .aoinf (dynamic size) binary files.
/// Legacy: Map header(273) + tiles(100x100 variable, Int16 layers)
///         Inf header(10) + tiles(100x100 variable)
/// New:    AOMAP header(16) + tiles(WxH variable), same ByFlags format
///         AOINF header(16) + tiles(WxH variable), same ByFlags format
/// </summary>
public static class MapLoader
{
    private const int MapHeaderSize = 273; // 2(version) + 255(desc) + 4(crc) + 4(magic) + 4×Integer(8)
    private const int InfHeaderSize = 10; // 5 × Int16

    // .aomap/.aoinf header: magic(6) + version(2) + width(2) + height(2) + flags(4) = 16 bytes
    private const int AoMapHeaderSize = 16;
    private static readonly byte[] AoMapMagic = Encoding.ASCII.GetBytes("AOMAP\0");
    private static readonly byte[] AoInfMagic = Encoding.ASCII.GetBytes("AOINF\0");

    public static MapData Load(IResourceProvider resources, int mapNumber)
    {
        // Check for new format first (.aomap)
        string aomapRelPath = $"Maps/Mapa{mapNumber}.aomap";
        if (resources.Exists(aomapRelPath))
        {
            var mapData = LoadAoMapFile(resources, aomapRelPath);
            string aoinfRelPath = $"Maps/Mapa{mapNumber}.aoinf";
            if (resources.Exists(aoinfRelPath))
                LoadAoInfFile(resources, aoinfRelPath, mapData);
            LoadFogMetadata(resources, mapNumber, mapData);
            return mapData;
        }

        // Legacy fallback (100x100)
        var legacy = new MapData(100, 100);

        string mapRelPath = $"Maps/Mapa{mapNumber}.map";
        string infRelPath = $"Maps/Mapa{mapNumber}.inf";

        if (resources.Exists(mapRelPath))
            LoadMapFile(resources, mapRelPath, legacy);

        if (resources.Exists(infRelPath))
            LoadInfFile(resources, infRelPath, legacy);

        LoadFogMetadata(resources, mapNumber, legacy);
        return legacy;
    }

    private static void LoadFogMetadata(IResourceProvider resources, int mapNumber, MapData mapData)
    {
        string fogRelPath = $"Maps/Mapa{mapNumber}.aofog";
        mapData.FogFreeSmoke = false;
        if (!resources.Exists(fogRelPath))
            return;

        try
        {
            using var reader = new StringReader(Encoding.UTF8.GetString(resources.ReadBytes(fogRelPath)));
            string? rawLine;
            while ((rawLine = reader.ReadLine()) != null)
            {
                string line = rawLine.Trim();
                if (line.Length == 0 || line[0] == ';' || line[0] == '#')
                    continue;

                int eq = line.IndexOf('=');
                if (eq <= 0)
                    continue;

                string key = line[..eq].Trim();
                if (!key.Equals("FreeSmoke", StringComparison.OrdinalIgnoreCase))
                    continue;

                string value = line[(eq + 1)..].Trim();
                mapData.FogFreeSmoke = value == "1" || value.Equals("true", StringComparison.OrdinalIgnoreCase);
                return;
            }
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[MapLoader] Could not load fog metadata {fogRelPath}: {ex.Message}");
        }
    }

    // ── New format loaders ─────────────────────────────────────────

    private static MapData LoadAoMapFile(IResourceProvider resources, string relativePath)
    {
        byte[] fileData = resources.ReadBytes(relativePath);
        string path = relativePath;
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

                // Layer 1 always present
                tile.Layer1 = reader.ReadInt32();

                if ((byFlags & 2) != 0)
                    tile.Layer2 = reader.ReadInt32();
                else
                    tile.Layer2 = 0;

                if ((byFlags & 4) != 0)
                    tile.Layer3 = reader.ReadInt32();
                else
                    tile.Layer3 = 0;

                if ((byFlags & 8) != 0)
                    tile.Layer4 = reader.ReadInt32();
                else
                    tile.Layer4 = 0;

                if ((byFlags & 16) != 0)
                    tile.Trigger = reader.ReadInt16();
                else
                    tile.Trigger = 0;

                if ((byFlags & 32) != 0)
                    tile.ParticleGroup = reader.ReadInt16();
                else
                    tile.ParticleGroup = 0;

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

    private static void LoadAoInfFile(IResourceProvider resources, string relativePath, MapData mapData)
    {
        byte[] fileData = resources.ReadBytes(relativePath);
        string path = relativePath;
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
                    tile.Blocked = false;
                }

                if ((byFlags & 2) != 0)
                {
                    tile.NpcIndex = reader.ReadInt16();
                }

                if ((byFlags & 4) != 0)
                {
                    tile.ObjIndex = reader.ReadInt16();
                    tile.ObjAmount = reader.ReadInt16();
                }
            }
        }
    }

    // ── Legacy format loaders ──────────────────────────────────────

    private static void LoadMapFile(IResourceProvider resources, string relativePath, MapData mapData)
    {
        byte[] fileData = resources.ReadBytes(relativePath);
        using var reader = new BinaryReader(new MemoryStream(fileData));

        // Skip header: version(2) + MiCabecera(263) + 4 reserved Integers(2 each = 8) = 273
        // VB6: MapVersion(Integer=2) + MiCabecera(263) + 4 × tempint(Integer=2 bytes) = 273
        reader.BaseStream.Seek(273, SeekOrigin.Begin);

        // Read tiles Y=1..100, X=1..100
        for (int y = 1; y <= mapData.Height; y++)
        {
            for (int x = 1; x <= mapData.Width; x++)
            {
                if (reader.BaseStream.Position >= reader.BaseStream.Length)
                    return;

                byte byFlags = reader.ReadByte();
                ref var tile = ref mapData.Tiles[x, y];

                tile.Blocked = (byFlags & 1) != 0;

                // Layer 1 always present. Legacy VB6 .map stores graphic layers as UInt16.
                tile.Layer1 = reader.ReadUInt16();

                // Layer 2
                if ((byFlags & 2) != 0)
                    tile.Layer2 = reader.ReadUInt16();
                else
                    tile.Layer2 = 0;

                // Layer 3
                if ((byFlags & 4) != 0)
                    tile.Layer3 = reader.ReadUInt16();
                else
                    tile.Layer3 = 0;

                // Layer 4
                if ((byFlags & 8) != 0)
                    tile.Layer4 = reader.ReadUInt16();
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

    private static void LoadInfFile(IResourceProvider resources, string relativePath, MapData mapData)
    {
        byte[] fileData = resources.ReadBytes(relativePath);
        using var reader = new BinaryReader(new MemoryStream(fileData));

        // Skip header: 5 × Int16 = 10 bytes
        reader.BaseStream.Seek(InfHeaderSize, SeekOrigin.Begin);

        for (int y = 1; y <= mapData.Height; y++)
        {
            for (int x = 1; x <= mapData.Width; x++)
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
                    tile.Blocked = false;
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

/// <summary>
/// Chunk-based tile storage. Tiles are grouped into 100x100 chunks
/// that are lazily allocated on first access, keeping memory proportional
/// to the area actually used instead of Width*Height.
/// The indexer accepts 1-based coordinates so callers work exactly like
/// the old MapTile[,] 2D array.
/// </summary>
public class ChunkedTiles
{
    private const int ChunkSize = 100;
    private readonly Dictionary<(int, int), MapTile[]> _chunks = new();

    /// <summary>
    /// Decompose 1-based tile coordinates into chunk key + flat local index.
    /// </summary>
    private static (int cx, int cy, int local) Resolve(int x, int y)
    {
        int cx = (x - 1) / ChunkSize;
        int cy = (y - 1) / ChunkSize;
        int lx = (x - 1) % ChunkSize;
        int ly = (y - 1) % ChunkSize;
        return (cx, cy, ly * ChunkSize + lx);
    }

    /// <summary>
    /// Indexer — transparently resolves chunks.
    /// Auto-creates the chunk on first access (needed during map loading).
    /// Returns by ref so callers can do <c>ref var tile = ref tiles[x, y];</c>.
    /// </summary>
    public ref MapTile this[int x, int y]
    {
        get
        {
            var (cx, cy, local) = Resolve(x, y);
            var key = (cx, cy);
            if (!_chunks.TryGetValue(key, out var chunk))
            {
                chunk = new MapTile[ChunkSize * ChunkSize];
                _chunks[key] = chunk;
            }
            return ref chunk[local];
        }
    }

    /// <summary>
    /// Read-only tile access — returns default MapTile if chunk not loaded.
    /// Does NOT create chunks. Use this for read-only operations like minimap.
    /// </summary>
    public MapTile Get(int x, int y)
    {
        var (cx, cy, local) = Resolve(x, y);
        if (_chunks.TryGetValue((cx, cy), out var chunk))
            return chunk[local];
        return default;
    }

    /// <summary>
    /// Check whether the chunk containing the given tile has been loaded.
    /// </summary>
    public bool Has(int x, int y)
    {
        var (cx, cy, _) = Resolve(x, y);
        return _chunks.ContainsKey((cx, cy));
    }

    /// <summary>Number of chunks currently allocated.</summary>
    public int LoadedChunks => _chunks.Count;
}

public class MapData
{
    public ChunkedTiles Tiles;
    public int Width;
    public int Height;
    public bool FogFreeSmoke;

    public MapData(int width, int height)
    {
        Width = width;
        Height = height;
        Tiles = new ChunkedTiles();
    }
}

public struct MapTile
{
    public bool Blocked;
    public int Layer1;  // GRH index (read as UInt16 to support indices > 32767)
    public int Layer2;
    public int Layer3;
    public int Layer4;
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
