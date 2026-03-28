#nullable enable
using System;
using System.IO;
using System.IO.Compression;
using System.Text;
using Godot;

namespace AOResourceConverter.Converters;

/// <summary>
/// Extracts BMP files from a Graphics.AO archive (0.12.3 format).
/// Format: FILEHEADER(12) + INFOHEADER[N](28 each) + zlib-compressed BMP data.
/// After extraction, BMPs are converted to PNG with black color key.
/// </summary>
public static class GraphicsAOExtractor
{
    private const int FileHeaderSize = 12;
    private const int InfoHeaderSize = 28;
    private const int FileNameFieldSize = 16;
    private const int BlackThreshold = 3;

    public struct ExtractResult
    {
        public int Total;
        public int Extracted;
        public int Errors;
    }

    /// <summary>
    /// Extract all images from Graphics.AO, decompress, apply color key, save as PNG.
    /// </summary>
    public static ExtractResult ExtractAndConvert(
        string aoFilePath, string outputDir,
        Action<int, int, string>? onProgress = null)
    {
        var result = new ExtractResult();

        if (!File.Exists(aoFilePath))
            throw new FileNotFoundException($"Graphics.AO not found: {aoFilePath}");

        Directory.CreateDirectory(outputDir);

        byte[] fileData = File.ReadAllBytes(aoFilePath);
        using var reader = new BinaryReader(new MemoryStream(fileData));

        // FILEHEADER: NumFiles(i32) + FileSize(i32) + FileVersion(i32) = 12 bytes
        int numFiles = reader.ReadInt32();
        int totalFileSize = reader.ReadInt32();
        int fileVersion = reader.ReadInt32();

        GD.Print($"[GraphicsAO] Files: {numFiles}, Size: {totalFileSize}, Version: {fileVersion}");

        if (numFiles <= 0 || numFiles > 100_000)
            throw new InvalidDataException($"Invalid file count in Graphics.AO: {numFiles}");

        result.Total = numFiles;

        // Read all INFOHEADERs: CompSize(i32) + Offset1Based(i32) + FileName(16) + UncompSize(i32) = 28 bytes
        var entries = new InfoHeader[numFiles];
        for (int i = 0; i < numFiles; i++)
        {
            entries[i].CompressedSize = reader.ReadInt32();
            entries[i].Offset1Based = reader.ReadInt32();
            var nameBytes = reader.ReadBytes(FileNameFieldSize);
            entries[i].FileName = Encoding.ASCII.GetString(nameBytes).TrimEnd('\0').Trim();
            entries[i].UncompressedSize = reader.ReadInt32();
        }

        // Extract each file
        for (int i = 0; i < numFiles; i++)
        {
            ref var entry = ref entries[i];
            string baseName = Path.GetFileNameWithoutExtension(entry.FileName);
            string outPath = Path.Combine(outputDir, baseName + ".png");

            onProgress?.Invoke(i + 1, numFiles, entry.FileName);

            try
            {
                // Seek to compressed data (convert 1-based offset to 0-based)
                int offset0 = entry.Offset1Based - 1;
                if (offset0 < 0 || offset0 + entry.CompressedSize > fileData.Length)
                {
                    GD.PrintErr($"[GraphicsAO] Invalid offset for {entry.FileName}: {offset0}");
                    result.Errors++;
                    continue;
                }

                byte[] bmpBytes;
                if (entry.CompressedSize < entry.UncompressedSize)
                {
                    // Decompress with zlib (deflate with 2-byte zlib header)
                    bmpBytes = ZlibDecompress(fileData, offset0, entry.CompressedSize, entry.UncompressedSize);
                }
                else
                {
                    // Stored uncompressed
                    bmpBytes = new byte[entry.CompressedSize];
                    Array.Copy(fileData, offset0, bmpBytes, 0, entry.CompressedSize);
                }

                // Load BMP, apply color key, save as PNG
                var image = new Image();
                var err = image.LoadBmpFromBuffer(bmpBytes);
                if (err != Error.Ok)
                {
                    GD.PrintErr($"[GraphicsAO] Failed to decode BMP {entry.FileName}: {err}");
                    result.Errors++;
                    continue;
                }

                ApplyBlackColorKey(image);

                err = image.SavePng(outPath);
                if (err != Error.Ok)
                {
                    GD.PrintErr($"[GraphicsAO] Failed to save PNG {outPath}: {err}");
                    result.Errors++;
                    continue;
                }

                result.Extracted++;
            }
            catch (Exception ex)
            {
                GD.PrintErr($"[GraphicsAO] Error processing {entry.FileName}: {ex.Message}");
                result.Errors++;
            }
        }

        return result;
    }

    /// <summary>
    /// Decompress zlib data (2-byte header + deflate stream).
    /// </summary>
    private static byte[] ZlibDecompress(byte[] source, int offset, int compressedSize, int uncompressedSize)
    {
        // Skip 2-byte zlib header (CMF + FLG)
        using var compressedStream = new MemoryStream(source, offset + 2, compressedSize - 2);
        using var deflateStream = new DeflateStream(compressedStream, CompressionMode.Decompress);

        var output = new byte[uncompressedSize];
        int totalRead = 0;
        while (totalRead < uncompressedSize)
        {
            int read = deflateStream.Read(output, totalRead, uncompressedSize - totalRead);
            if (read == 0)
                throw new InvalidDataException(
                    $"Decompression incomplete: expected {uncompressedSize} bytes, got {totalRead}");
            totalRead += read;
        }

        return output;
    }

    private static void ApplyBlackColorKey(Image image)
    {
        if (image.GetFormat() != Image.Format.Rgba8)
            image.Convert(Image.Format.Rgba8);

        int width = image.GetWidth();
        int height = image.GetHeight();
        byte[] data = image.GetData();

        for (int i = 0; i < data.Length; i += 4)
        {
            if (data[i] <= BlackThreshold && data[i + 1] <= BlackThreshold && data[i + 2] <= BlackThreshold)
                data[i + 3] = 0;
        }

        var newImage = Image.CreateFromData(width, height, false, Image.Format.Rgba8, data);
        image.CopyFrom(newImage);
    }

    private struct InfoHeader
    {
        public int CompressedSize;
        public int Offset1Based;
        public string FileName;
        public int UncompressedSize;
    }
}
