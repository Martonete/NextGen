using System.Security.Cryptography;
using System.Text;

namespace AoPak;

/// <summary>
/// Reads and writes the resources.aoman manifest file.
/// The manifest lists all .aopak archives with their type, layer, and part index.
/// It is signed with HMAC-SHA256 derived from the AMK.
/// </summary>
public static class AopakManifest
{
    public const string ManifestFileName = "resources.aoman";
    private const string HeaderLine = "# AoPak Manifest v1";
    private const string HmacPrefix = "# HMAC: ";
    private const string HkdfInfo = "AOPAK-MANIFEST-HMAC";
    private static readonly byte[] HkdfSalt = Encoding.ASCII.GetBytes("manifest");

    public record ArchiveEntry(string FileName, string Type, int Layer, int Part);

    /// <summary>
    /// Read a manifest file, verify its HMAC, and return the archive list.
    /// Throws InvalidDataException if HMAC verification fails.
    /// </summary>
    public static List<ArchiveEntry> Read(string manifestPath, byte[] amk)
    {
        var lines = File.ReadAllLines(manifestPath, Encoding.UTF8);
        return ParseAndVerify(lines, amk);
    }

    /// <summary>
    /// Write a manifest file with HMAC signature.
    /// </summary>
    public static void Write(string manifestPath, List<ArchiveEntry> entries, byte[] amk)
    {
        var contentLines = BuildContentLines(entries);
        var hmacHex = ComputeHmac(contentLines, amk);

        using var writer = new StreamWriter(manifestPath, append: false, Encoding.UTF8);
        writer.WriteLine(HeaderLine);
        writer.WriteLine($"{HmacPrefix}{hmacHex}");
        writer.WriteLine();
        foreach (var line in contentLines)
            writer.WriteLine(line);
    }

    /// <summary>
    /// Scan a directory for .aopak files, read their headers, and build an entry list.
    /// Does NOT write the manifest — call Write() afterwards.
    /// </summary>
    public static List<ArchiveEntry> ScanDirectory(string directory, byte[] amk)
    {
        var entries = new List<ArchiveEntry>();

        var files = Directory.GetFiles(directory, "*.aopak")
            .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        foreach (var filePath in files)
        {
            var fileName = Path.GetFileName(filePath);
            try
            {
                using var reader = new AopakReader(filePath, amk);
                var header = reader.Header;
                var type = InferType(fileName);
                entries.Add(new ArchiveEntry(
                    FileName: fileName,
                    Type: type,
                    Layer: header.LayerPriority,
                    Part: header.SplitPartIndex));
            }
            catch (Exception ex)
            {
                throw new InvalidDataException(
                    $"Failed to read archive header from '{fileName}': {ex.Message}", ex);
            }
        }

        return entries;
    }

    // --- Private helpers ---

    private static List<ArchiveEntry> ParseAndVerify(string[] lines, byte[] amk)
    {
        // Find HMAC line and collect content lines
        string? storedHmac = null;
        var contentLines = new List<string>();
        bool inHeader = true;

        foreach (var line in lines)
        {
            if (inHeader)
            {
                if (line.StartsWith(HmacPrefix, StringComparison.Ordinal))
                {
                    storedHmac = line.Substring(HmacPrefix.Length).Trim();
                    inHeader = false;
                    continue;
                }
                if (line == HeaderLine || line.StartsWith("# ", StringComparison.Ordinal))
                    continue;
                // First non-comment line after header section
                inHeader = false;
            }

            contentLines.Add(line);
        }

        if (storedHmac == null)
            throw new InvalidDataException("Manifest is missing HMAC signature.");

        // Verify HMAC
        var expectedHmac = ComputeHmac(contentLines, amk);
        if (!CryptographicEquals(storedHmac, expectedHmac))
            throw new InvalidDataException("Manifest HMAC verification failed. File may be tampered.");

        // Parse entries
        return ParseEntries(contentLines);
    }

    private static List<ArchiveEntry> ParseEntries(List<string> lines)
    {
        var entries = new List<ArchiveEntry>();
        string? currentFile = null;
        string? currentType = null;
        int currentLayer = 0;
        int currentPart = 0;

        foreach (var rawLine in lines)
        {
            var line = rawLine.Trim();

            if (line.StartsWith('[') && line.EndsWith(']'))
            {
                // Flush previous entry
                if (currentFile != null)
                    entries.Add(new ArchiveEntry(currentFile, currentType ?? "unknown", currentLayer, currentPart));

                currentFile = line.Substring(1, line.Length - 2);
                currentType = null;
                currentLayer = 0;
                currentPart = 0;
                continue;
            }

            if (currentFile == null || string.IsNullOrWhiteSpace(line) || line.StartsWith('#'))
                continue;

            var eqIdx = line.IndexOf('=');
            if (eqIdx < 0) continue;

            var key = line.Substring(0, eqIdx).Trim();
            var value = line.Substring(eqIdx + 1).Trim();

            switch (key)
            {
                case "type":  currentType  = value; break;
                case "layer": int.TryParse(value, out currentLayer); break;
                case "part":  int.TryParse(value, out currentPart);  break;
            }
        }

        // Flush last entry
        if (currentFile != null)
            entries.Add(new ArchiveEntry(currentFile, currentType ?? "unknown", currentLayer, currentPart));

        return entries;
    }

    private static List<string> BuildContentLines(List<ArchiveEntry> entries)
    {
        var lines = new List<string>();
        foreach (var entry in entries)
        {
            lines.Add($"[{entry.FileName}]");
            lines.Add($"type={entry.Type}");
            lines.Add($"layer={entry.Layer}");
            lines.Add($"part={entry.Part}");
            lines.Add(string.Empty);
        }
        return lines;
    }

    private static string ComputeHmac(List<string> contentLines, byte[] amk)
    {
        // Derive manifest HMAC key: HKDF(AMK, salt="manifest", info="AOPAK-MANIFEST-HMAC")
        var infoBytes = Encoding.ASCII.GetBytes(HkdfInfo);
        var hmacKey = HKDF.DeriveKey(HashAlgorithmName.SHA256, amk, 32, HkdfSalt, infoBytes);

        // HMAC over all content lines joined with newline
        var content = string.Join("\n", contentLines);
        var contentBytes = Encoding.UTF8.GetBytes(content);
        var hash = HMACSHA256.HashData(hmacKey, contentBytes);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static string InferType(string fileName)
    {
        var lower = fileName.ToLowerInvariant();
        if (lower.Contains("graphics")) return "graphics";
        if (lower.Contains("inits"))    return "inits";
        if (lower.Contains("maps"))     return "maps";
        if (lower.Contains("sounds"))   return "sounds";
        return "unknown";
    }

    /// <summary>Constant-time hex string comparison to prevent timing attacks.</summary>
    private static bool CryptographicEquals(string a, string b)
    {
        if (a.Length != b.Length) return false;
        try
        {
            var aBytes = Convert.FromHexString(a);
            var bBytes = Convert.FromHexString(b);
            return CryptographicOperations.FixedTimeEquals(aBytes, bBytes);
        }
        catch
        {
            return false;
        }
    }
}
