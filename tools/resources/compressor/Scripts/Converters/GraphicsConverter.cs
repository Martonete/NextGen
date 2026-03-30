#nullable enable
using System;
using System.IO;
using Godot;

namespace AOResourceConverter.Converters;

/// <summary>
/// Converts BMP graphics to PNG with black color key transparency.
/// Pixels with RGB <= BlackThreshold become fully transparent.
/// </summary>
public static class GraphicsConverter
{
    private const int BlackThreshold = 3;

    public struct ConvertResult
    {
        public int Total;
        public int Converted;
        public int Skipped;
        public int Errors;
    }

    /// <summary>
    /// Convert all BMP files in inputDir to PNG in outputDir with color key transparency.
    /// Calls onProgress(current, total, fileName) for each file.
    /// </summary>
    public static ConvertResult Convert(
        string inputDir, string outputDir,
        Action<int, int, string>? onProgress = null)
    {
        var result = new ConvertResult();

        if (!Directory.Exists(inputDir))
            throw new DirectoryNotFoundException($"Input directory not found: {inputDir}");

        Directory.CreateDirectory(outputDir);

        var bmpFiles = Directory.GetFiles(inputDir, "*.bmp", SearchOption.TopDirectoryOnly);
        // Also grab .BMP (case-insensitive on Windows, explicit on Linux)
        var bmpFilesUpper = Directory.GetFiles(inputDir, "*.BMP", SearchOption.TopDirectoryOnly);
        var allFiles = new System.Collections.Generic.HashSet<string>(bmpFiles);
        foreach (var f in bmpFilesUpper) allFiles.Add(f);

        var fileList = new string[allFiles.Count];
        allFiles.CopyTo(fileList);
        Array.Sort(fileList);

        result.Total = fileList.Length;

        for (int i = 0; i < fileList.Length; i++)
        {
            string filePath = fileList[i];
            string fileName = Path.GetFileNameWithoutExtension(filePath);
            string outPath = Path.Combine(outputDir, fileName + ".png");

            onProgress?.Invoke(i + 1, result.Total, fileName);

            // Skip if PNG already exists and is newer than BMP
            if (File.Exists(outPath) && File.GetLastWriteTime(outPath) > File.GetLastWriteTime(filePath))
            {
                result.Skipped++;
                continue;
            }

            try
            {
                ConvertFile(filePath, outPath);
                result.Converted++;
            }
            catch (Exception ex)
            {
                GD.PrintErr($"[GFX] Error converting {fileName}: {ex.Message}");
                result.Errors++;
            }
        }

        return result;
    }

    /// <summary>
    /// Convert a single BMP file to PNG with black color key.
    /// </summary>
    public static void ConvertFile(string bmpPath, string pngPath)
    {
        byte[] rawBytes = File.ReadAllBytes(bmpPath);
        var image = new Image();
        var err = image.LoadBmpFromBuffer(rawBytes);
        if (err != Error.Ok)
            throw new InvalidDataException($"Failed to load BMP: {err}");

        ApplyBlackColorKey(image);

        err = image.SavePng(pngPath);
        if (err != Error.Ok)
            throw new IOException($"Failed to save PNG: {err}");
    }

    /// <summary>
    /// Apply black color key: pixels with R,G,B all <= threshold become transparent.
    /// Matches the client's TextureManager.ApplyBlackColorKeyFast behavior.
    /// </summary>
    private static void ApplyBlackColorKey(Image image)
    {
        int width = image.GetWidth();
        int height = image.GetHeight();

        // Ensure RGBA8 format for pixel access
        if (image.GetFormat() != Image.Format.Rgba8)
            image.Convert(Image.Format.Rgba8);

        byte[] data = image.GetData();

        for (int i = 0; i < data.Length; i += 4)
        {
            if (data[i] <= BlackThreshold && data[i + 1] <= BlackThreshold && data[i + 2] <= BlackThreshold)
            {
                data[i + 3] = 0; // Set alpha to 0
            }
        }

        var newImage = Image.CreateFromData(width, height, false, Image.Format.Rgba8, data);
        image.CopyFrom(newImage);
    }
}
