#nullable enable
using System;
using System.IO;
using Godot;

namespace AOResourceConverter.Converters;

/// <summary>
/// Converts INIT folder files from legacy AO versions to NextGen format.
/// - 0.12.3: Merges Graficos1/2/3.ind into single Graficos.ind
/// - All versions: Copies compatible .ind/.dat files (format is already readable by NextGen client)
/// - Validates expected files are present
/// </summary>
public static class InitConverter
{
    public struct ConvertResult
    {
        public int Total;
        public int Converted;
        public int Skipped;
        public int Errors;
        public string[] Missing;
    }

    /// <summary>
    /// Validate and convert INIT files from inputDir to outputDir.
    /// </summary>
    public static ConvertResult Convert(
        string inputDir, string outputDir, AoVersion version,
        Action<int, int, string>? onProgress = null)
    {
        var result = new ConvertResult();

        if (!Directory.Exists(inputDir))
            throw new DirectoryNotFoundException($"INIT directory not found: {inputDir}");

        Directory.CreateDirectory(outputDir);

        var expected = VersionConfig.ExpectedInits(version);
        var missing = new System.Collections.Generic.List<string>();

        // Validate
        foreach (string file in expected)
        {
            if (!FindFileCaseInsensitive(inputDir, file, out _))
                missing.Add(file);
        }
        result.Missing = missing.ToArray();

        // Determine files to process
        var filesToProcess = new System.Collections.Generic.List<InitTask>();

        if (version == AoVersion.V123)
        {
            // Special: merge Graficos1/2/3.ind → Graficos.ind
            if (FindFileCaseInsensitive(inputDir, "Graficos1.ind", out string g1) &&
                FindFileCaseInsensitive(inputDir, "Graficos2.ind", out string g2) &&
                FindFileCaseInsensitive(inputDir, "Graficos3.ind", out string g3))
            {
                filesToProcess.Add(new InitTask("Graficos.ind", MergeSources: new[] { g1, g2, g3 }));
            }
        }

        // Copy all other expected files directly (format is compatible)
        foreach (string file in expected)
        {
            // Skip the Graficos split files for 0.12.3 (handled by merge above)
            if (version == AoVersion.V123 &&
                (file.Equals("Graficos1.ind", StringComparison.OrdinalIgnoreCase) ||
                 file.Equals("Graficos2.ind", StringComparison.OrdinalIgnoreCase) ||
                 file.Equals("Graficos3.ind", StringComparison.OrdinalIgnoreCase)))
                continue;

            if (FindFileCaseInsensitive(inputDir, file, out string foundPath))
                filesToProcess.Add(new InitTask(file, CopySource: foundPath));
        }

        result.Total = filesToProcess.Count;

        for (int i = 0; i < filesToProcess.Count; i++)
        {
            var task = filesToProcess[i];
            onProgress?.Invoke(i + 1, result.Total, task.Destination);

            try
            {
                string outPath = Path.Combine(outputDir, task.Destination);

                if (task.MergeSources != null)
                    MergeGraficosInd(task.MergeSources[0], task.MergeSources[1], task.MergeSources[2], outPath);
                else if (task.CopySource != null)
                    File.Copy(task.CopySource, outPath, true);

                result.Converted++;
            }
            catch (Exception ex)
            {
                GD.PrintErr($"[INIT] Error converting {task.Destination}: {ex.Message}");
                result.Errors++;
            }
        }

        return result;
    }

    /// <summary>
    /// Merge three Graficos .ind files into a single one.
    /// Format: MiCabecera(263) + Version(4) + Count(4) + sparse entries.
    /// Each entry starts with GrhIndex(int32) so the reader resolves by index, not position.
    /// Concatenating the three entry streams works because entries are self-indexing.
    /// Count = max of the three counts (used as array size hint; reader expands dynamically).
    /// </summary>
    private static void MergeGraficosInd(string path1, string path2, string path3, string outPath)
    {
        const int MiCabeceraSize = 263;

        byte[] data1 = File.ReadAllBytes(path1);
        byte[] data2 = File.ReadAllBytes(path2);
        byte[] data3 = File.ReadAllBytes(path3);

        // Each file: MiCabecera(263) + Version(4) + Count(4) + entries
        // We need to find the max count and merge all entries

        using var ms = new MemoryStream();

        // Use header from file 1 (MiCabecera + Version)
        ms.Write(data1, 0, MiCabeceraSize + 4); // 263 + 4 = 267 bytes (cabecera + version)

        // Read counts from all three files
        int count1 = BitConverter.ToInt32(data1, MiCabeceraSize + 4);
        int count2 = BitConverter.ToInt32(data2, MiCabeceraSize + 4);
        int count3 = BitConverter.ToInt32(data3, MiCabeceraSize + 4);
        int maxCount = Math.Max(count1, Math.Max(count2, count3));

        // Write the max count
        ms.Write(BitConverter.GetBytes(maxCount), 0, 4);

        // Write entry streams from all three files (skip their headers)
        int entriesOffset = MiCabeceraSize + 8; // 263 + 4 (version) + 4 (count)
        if (data1.Length > entriesOffset)
            ms.Write(data1, entriesOffset, data1.Length - entriesOffset);
        if (data2.Length > entriesOffset)
            ms.Write(data2, entriesOffset, data2.Length - entriesOffset);
        if (data3.Length > entriesOffset)
            ms.Write(data3, entriesOffset, data3.Length - entriesOffset);

        File.WriteAllBytes(outPath, ms.ToArray());
        GD.Print($"[INIT] Merged Graficos 1+2+3 → {outPath} (counts: {count1}+{count2}+{count3}, max: {maxCount})");
    }

    /// <summary>
    /// Find a file in a directory with case-insensitive matching (for Linux).
    /// </summary>
    private static bool FindFileCaseInsensitive(string dir, string fileName, out string foundPath)
    {
        foundPath = Path.Combine(dir, fileName);
        if (File.Exists(foundPath)) return true;

        // Try case-insensitive search
        try
        {
            foreach (var file in Directory.GetFiles(dir))
            {
                if (string.Equals(Path.GetFileName(file), fileName, StringComparison.OrdinalIgnoreCase))
                {
                    foundPath = file;
                    return true;
                }
            }
        }
        catch { }

        foundPath = "";
        return false;
    }
}

/// <summary>Typed task for INIT conversion: either a copy or a merge operation.</summary>
internal sealed record InitTask(
    string Destination,
    string? CopySource = null,
    string[]? MergeSources = null);
