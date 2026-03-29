#nullable enable
using System;
using System.IO;
using System.Text;
using Godot;

namespace AOResourceConverter.Converters;

/// <summary>
/// Batch converts legacy .map/.inf/.dat files to .aomap/.aoinf/.dat (NextGen format).
/// Handles both 0.99z fixed format and 11.5-13.3 variable Int16 format.
/// Output uses .aomap format: AOMAP header + byFlags + Int32 layers.
/// </summary>
public static class MapConverter
{
    private const int LegacyHeaderSize = 273;
    private const int InfHeaderSize = 10;
    private const int FixedTileSize = 13;   // 0.99z: 1+2+2+2+2+2+2
    private const int FixedInfTileSize = 16; // 0.99z: 2+2+2+2+2+2+2+2

    private static readonly byte[] AoMapMagic = Encoding.ASCII.GetBytes("AOMAP\0");
    private static readonly byte[] AoInfMagic = Encoding.ASCII.GetBytes("AOINF\0");

    public struct ConvertResult
    {
        public int Total;
        public int Converted;
        public int Skipped;
        public int Errors;
    }

    /// <summary>
    /// Convert all legacy .map files in inputDir to .aomap in outputDir.
    /// Also converts companion .inf → .aoinf and copies .dat files.
    /// </summary>
    public static ConvertResult Convert(
        string inputDir, string outputDir, AoVersion version,
        Action<int, int, string>? onProgress = null)
    {
        var result = new ConvertResult();
        var format = VersionConfig.GetMapFormat(version);

        if (!Directory.Exists(inputDir))
            throw new DirectoryNotFoundException($"Maps directory not found: {inputDir}");

        Directory.CreateDirectory(outputDir);

        // Find all .map files (case-insensitive on Linux)
        var mapSet = new System.Collections.Generic.HashSet<string>(
            Directory.GetFiles(inputDir, "*.map", SearchOption.TopDirectoryOnly));
        foreach (var f in Directory.GetFiles(inputDir, "*.MAP", SearchOption.TopDirectoryOnly))
            mapSet.Add(f);
        var mapFiles = new string[mapSet.Count];
        mapSet.CopyTo(mapFiles);
        Array.Sort(mapFiles, StringComparer.OrdinalIgnoreCase);
        result.Total = mapFiles.Length;

        for (int i = 0; i < mapFiles.Length; i++)
        {
            string mapFile = mapFiles[i];
            string baseName = Path.GetFileNameWithoutExtension(mapFile);

            onProgress?.Invoke(i + 1, result.Total, baseName);

            try
            {
                // Parse map number from filename (e.g., "Mapa1" → 1)
                int mapNum = ExtractMapNumber(baseName);
                if (mapNum <= 0)
                {
                    result.Skipped++;
                    continue;
                }

                // Convert .map → .aomap
                ConvertMapFile(mapFile, outputDir, mapNum, format);

                // Convert .inf → .aoinf (if exists)
                if (FindFileCI(inputDir, $"{baseName}.inf", out string infFile))
                    ConvertInfFile(infFile, outputDir, mapNum, format);

                // Copy .dat as-is (metadata is INI text, no conversion needed)
                if (FindFileCI(inputDir, $"{baseName}.dat", out string datFile))
                    File.Copy(datFile, Path.Combine(outputDir, $"Mapa{mapNum}.dat"), true);

                result.Converted++;
            }
            catch (Exception ex)
            {
                GD.PrintErr($"[MapConv] Error converting {baseName}: {ex.Message}");
                result.Errors++;
            }
        }

        return result;
    }

    private static int ExtractMapNumber(string baseName)
    {
        // Handle "Mapa1", "MAPA1", "mapa1" etc.
        string upper = baseName.ToUpperInvariant();
        if (!upper.StartsWith("MAPA")) return -1;
        if (int.TryParse(upper.Substring(4), out int num)) return num;
        return -1;
    }

    #region .map → .aomap

    private static void ConvertMapFile(string mapFile, string outDir, int mapNum, MapTileFormat format)
    {
        byte[] fileData = File.ReadAllBytes(mapFile);
        using var reader = new BinaryReader(new MemoryStream(fileData));
        reader.BaseStream.Seek(LegacyHeaderSize, SeekOrigin.Begin);

        // Read all tiles into arrays
        int w = 100, h = 100;
        var layers1 = new int[w * h];
        var layers2 = new int[w * h];
        var layers3 = new int[w * h];
        var layers4 = new int[w * h];
        var blocked = new bool[w * h];
        var triggers = new short[w * h];
        var particles = new short[w * h];
        var lightRange = new short[w * h];
        var lightR = new short[w * h];
        var lightG = new short[w * h];
        var lightB = new short[w * h];

        for (int idx = 0; idx < w * h; idx++)
        {
            if (reader.BaseStream.Position >= reader.BaseStream.Length) break;

            if (format == MapTileFormat.Fixed_099z)
            {
                // Fixed: bloqueado(1) + grafs1-4(4×2) + trigger(2) + t1(2) = 13
                blocked[idx] = reader.ReadByte() != 0;
                layers1[idx] = reader.ReadInt16();
                layers2[idx] = reader.ReadInt16();
                layers3[idx] = reader.ReadInt16();
                layers4[idx] = reader.ReadInt16();
                triggers[idx] = reader.ReadInt16();
                reader.ReadInt16(); // t1 padding
            }
            else
            {
                // Variable: byFlags + Layer1(Int16 always) + optional layers
                byte byFlags = reader.ReadByte();
                blocked[idx] = (byFlags & 1) != 0;
                layers1[idx] = reader.ReadInt16();
                if ((byFlags & 2) != 0) layers2[idx] = reader.ReadInt16();
                if ((byFlags & 4) != 0) layers3[idx] = reader.ReadInt16();
                if ((byFlags & 8) != 0) layers4[idx] = reader.ReadInt16();
                if ((byFlags & 16) != 0) triggers[idx] = reader.ReadInt16();
                if ((byFlags & 32) != 0) particles[idx] = reader.ReadInt16();
                // Legacy Int16 maps don't have light data (bits 5-6 are particle only)
            }
        }

        // Write .aomap
        string outPath = Path.Combine(outDir, $"Mapa{mapNum}.aomap");
        using var ms = new MemoryStream();
        using var writer = new BinaryWriter(ms);

        // Header: magic(6) + version(2) + width(2) + height(2) + flags(4) = 16
        writer.Write(AoMapMagic);
        writer.Write((ushort)1);
        writer.Write((ushort)w);
        writer.Write((ushort)h);
        writer.Write((uint)0);

        for (int idx = 0; idx < w * h; idx++)
        {
            byte byFlags = 0;
            if (blocked[idx]) byFlags |= 1;
            if (layers2[idx] != 0) byFlags |= 2;
            if (layers3[idx] != 0) byFlags |= 4;
            if (layers4[idx] != 0) byFlags |= 8;
            if (triggers[idx] != 0) byFlags |= 16;
            if (particles[idx] != 0) byFlags |= 32;
            bool hasLight = lightRange[idx] != 0;
            if (hasLight) byFlags |= 64;

            writer.Write(byFlags);
            writer.Write(layers1[idx]); // Int32 in .aomap
            if (layers2[idx] != 0) writer.Write(layers2[idx]);
            if (layers3[idx] != 0) writer.Write(layers3[idx]);
            if (layers4[idx] != 0) writer.Write(layers4[idx]);
            if (triggers[idx] != 0) writer.Write(triggers[idx]);
            if (particles[idx] != 0) writer.Write(particles[idx]);
            if (hasLight)
            {
                writer.Write(lightRange[idx]);
                writer.Write(lightR[idx]);
                writer.Write(lightG[idx]);
                writer.Write(lightB[idx]);
            }
        }

        File.WriteAllBytes(outPath, ms.ToArray());
    }

    #endregion

    #region .inf → .aoinf

    private static void ConvertInfFile(string infFile, string outDir, int mapNum, MapTileFormat format)
    {
        byte[] fileData = File.ReadAllBytes(infFile);
        using var reader = new BinaryReader(new MemoryStream(fileData));
        reader.BaseStream.Seek(InfHeaderSize, SeekOrigin.Begin);

        int w = 100, h = 100;
        var exitMap = new short[w * h];
        var exitX = new short[w * h];
        var exitY = new short[w * h];
        var npcIndex = new short[w * h];
        var objIndex = new short[w * h];
        var objAmount = new short[w * h];

        for (int idx = 0; idx < w * h; idx++)
        {
            if (reader.BaseStream.Position >= reader.BaseStream.Length) break;

            if (format == MapTileFormat.Fixed_099z)
            {
                // Fixed: dest(6) + npc(2) + obj(4) + t1(2) + t2(2) = 16
                exitMap[idx] = reader.ReadInt16();
                exitX[idx] = reader.ReadInt16();
                exitY[idx] = reader.ReadInt16();
                npcIndex[idx] = reader.ReadInt16();
                objIndex[idx] = reader.ReadInt16();
                objAmount[idx] = reader.ReadInt16();
                reader.ReadInt16(); // t1
                reader.ReadInt16(); // t2
            }
            else
            {
                // Variable: byFlags + optional exit(6)/npc(2)/obj(4)
                byte byFlags = reader.ReadByte();
                if ((byFlags & 1) != 0)
                {
                    exitMap[idx] = reader.ReadInt16();
                    exitX[idx] = reader.ReadInt16();
                    exitY[idx] = reader.ReadInt16();
                }
                if ((byFlags & 2) != 0)
                    npcIndex[idx] = reader.ReadInt16();
                if ((byFlags & 4) != 0)
                {
                    objIndex[idx] = reader.ReadInt16();
                    objAmount[idx] = reader.ReadInt16();
                }
            }
        }

        // Write .aoinf
        string outPath = Path.Combine(outDir, $"Mapa{mapNum}.aoinf");
        using var ms = new MemoryStream();
        using var writer = new BinaryWriter(ms);

        writer.Write(AoInfMagic);
        writer.Write((ushort)1);
        writer.Write((ushort)w);
        writer.Write((ushort)h);
        writer.Write((uint)0);

        for (int idx = 0; idx < w * h; idx++)
        {
            byte byFlags = 0;
            if (exitMap[idx] > 0) byFlags |= 1;
            if (npcIndex[idx] > 0) byFlags |= 2;
            if (objIndex[idx] > 0) byFlags |= 4;

            writer.Write(byFlags);
            if (exitMap[idx] > 0)
            {
                writer.Write(exitMap[idx]);
                writer.Write(exitX[idx]);
                writer.Write(exitY[idx]);
            }
            if (npcIndex[idx] > 0) writer.Write(npcIndex[idx]);
            if (objIndex[idx] > 0)
            {
                writer.Write(objIndex[idx]);
                writer.Write(objAmount[idx]);
            }
        }

        File.WriteAllBytes(outPath, ms.ToArray());
    }

    #endregion

    private static bool FindFileCI(string dir, string fileName, out string foundPath)
    {
        foundPath = Path.Combine(dir, fileName);
        if (File.Exists(foundPath)) return true;
        try
        {
            foreach (var f in Directory.GetFiles(dir))
                if (string.Equals(Path.GetFileName(f), fileName, StringComparison.OrdinalIgnoreCase))
                { foundPath = f; return true; }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            GD.PrintErr($"[MapConv] Error accessing {dir}: {ex.Message}");
        }
        foundPath = "";
        return false;
    }
}
