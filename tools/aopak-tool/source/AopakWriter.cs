using System.IO.Compression;
using System.Security.Cryptography;

namespace AoPak;

public static class AopakWriter
{
    // Extensions that are already well-compressed — skip zlib for these
    // Note: .wav is NOT here because WAVs are raw PCM and compress very well
    private static readonly HashSet<string> AlreadyCompressedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mp3", ".ogg",
    };

    // Extensions that benefit from zlib compression
    // (everything not in AlreadyCompressedExtensions gets compressed)

    /// <summary>
    /// Pack a directory recursively into a .aopak file.
    /// </summary>
    /// <param name="inputDir">Source directory to pack.</param>
    /// <param name="outputFile">Destination .aopak file path.</param>
    /// <param name="amk">Application Master Key (32 bytes).</param>
    /// <param name="layerPriority">0=base layer, 1+=patch layers.</param>
    /// <param name="splitPartIndex">For split archives, the part index.</param>
    /// <param name="includeExtensions">
    /// Optional whitelist of file extensions to include (e.g. [".aomap"]).
    /// When null or empty, all files are included.
    /// </param>
    /// <param name="progress">Optional progress callback: (current, total, entryName).</param>
    public static void Pack(
        string inputDir,
        string outputFile,
        byte[] amk,
        ushort layerPriority = 0,
        ushort splitPartIndex = 0,
        IReadOnlyCollection<string>? includeExtensions = null,
        Action<int, int, string>? progress = null)
    {
        if (amk.Length != 32)
            throw new ArgumentException("AMK must be exactly 32 bytes.", nameof(amk));

        var extensionFilter = includeExtensions is { Count: > 0 }
            ? new HashSet<string>(includeExtensions, StringComparer.OrdinalIgnoreCase)
            : null;

        var files = Directory
            .GetFiles(inputDir, "*", SearchOption.AllDirectories)
            .Where(f => extensionFilter == null || extensionFilter.Contains(Path.GetExtension(f)))
            .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var archiveId = AopakCrypto.GenerateArchiveId();
        var archiveKey = AopakCrypto.DeriveArchiveKey(amk, archiveId);
        var tocIV = AopakCrypto.GenerateIV();

        var tocEntries = new List<AopakTocEntry>(files.Length);

        using var output = new FileStream(outputFile, FileMode.Create, FileAccess.Write);
        using var writer = new BinaryWriter(output);

        // Write placeholder header (64 zero bytes) — will be overwritten at end
        writer.Write(new byte[AopakHeader.HeaderSize]);

        for (int i = 0; i < files.Length; i++)
        {
            var filePath = files[i];
            var relativeName = Path.GetRelativePath(inputDir, filePath)
                .Replace('\\', '/'); // normalise to forward slashes

            progress?.Invoke(i + 1, files.Length, relativeName);

            var originalData = File.ReadAllBytes(filePath);
            var contentHash = SHA256.HashData(originalData);

            // Decide compression
            var ext = Path.GetExtension(filePath);
            var useCompression = !AlreadyCompressedExtensions.Contains(ext);
            var compressionScheme = useCompression ? CompressionScheme.Zlib : CompressionScheme.None;

            var dataToEncrypt = useCompression ? ZlibCompress(originalData) : originalData;

            var entryIV = AopakCrypto.GenerateIV();
            var entryKey = AopakCrypto.DeriveEntryKey(archiveKey, i, entryIV);
            var encryptedData = AopakCrypto.Encrypt(dataToEncrypt, entryKey, entryIV);

            long dataOffset = output.Position;
            writer.Write(encryptedData);

            tocEntries.Add(new AopakTocEntry
            {
                Name = relativeName,
                DataOffset = dataOffset,
                DataSize = (uint)encryptedData.Length,
                OriginalSize = (uint)originalData.Length,
                EntryIV = entryIV,
                ContentHash = contentHash,
                CompressionScheme = compressionScheme,
                EncryptionScheme = EncryptionScheme.AesCbc,
            });
        }

        // Serialize and encrypt TOC
        long tocOffset = output.Position;
        var tocBytes = SerializeToc(tocEntries);
        var encryptedToc = AopakCrypto.Encrypt(tocBytes, archiveKey, tocIV);
        writer.Write(encryptedToc);

        // Seek back and write the real header
        output.Seek(0, SeekOrigin.Begin);
        var header = new AopakHeader
        {
            Magic = AopakHeader.MagicString,
            Version = AopakHeader.CurrentVersion,
            ArchiveId = archiveId,
            EntryCount = (uint)tocEntries.Count,
            TocOffset = tocOffset,
            TocSize = (uint)encryptedToc.Length,
            TocIV = tocIV,
            LayerPriority = layerPriority,
            SplitPartIndex = splitPartIndex,
        };
        header.Write(writer);
    }

    /// <summary>
    /// Update a single entry in an existing .aopak archive.
    /// Appends new data and rebuilds the TOC; old data remains but is unreachable.
    /// </summary>
    public static void UpdateEntry(string archiveFile, string entryName, byte[] newData, byte[] amk)
    {
        if (amk.Length != 32)
            throw new ArgumentException("AMK must be exactly 32 bytes.", nameof(amk));

        // Read existing archive fully
        AopakHeader existingHeader;
        var existingEntries = new List<AopakTocEntry>();
        byte[] archiveKey;

        using (var readStream = new FileStream(archiveFile, FileMode.Open, FileAccess.Read))
        using (var reader = new BinaryReader(readStream))
        {
            existingHeader = AopakHeader.Read(reader);
            if (!existingHeader.IsValid())
                throw new InvalidDataException("Invalid AOPAK archive.");

            archiveKey = AopakCrypto.DeriveArchiveKey(amk, existingHeader.ArchiveId);

            readStream.Seek(existingHeader.TocOffset, SeekOrigin.Begin);
            var encryptedToc = reader.ReadBytes((int)existingHeader.TocSize);
            var tocBytes = AopakCrypto.Decrypt(encryptedToc, archiveKey, existingHeader.TocIV);

            using var tocStream = new MemoryStream(tocBytes);
            using var tocReader = new BinaryReader(tocStream);
            for (int i = 0; i < existingHeader.EntryCount; i++)
                existingEntries.Add(AopakTocEntry.Read(tocReader));
        }

        // Find existing entry index (to update in-place in TOC)
        int updateIndex = existingEntries.FindIndex(e =>
            string.Equals(e.Name, entryName, StringComparison.OrdinalIgnoreCase));

        // Open for append
        using var output = new FileStream(archiveFile, FileMode.Open, FileAccess.ReadWrite);
        using var writer = new BinaryWriter(output);

        // Tombstone old entry if it exists (mark it deleted but keep data slot)
        if (updateIndex >= 0)
            existingEntries[updateIndex].EncryptionScheme = EncryptionScheme.Tombstone;

        // Append new entry data at end (before old TOC or after — seek to end of data)
        // We need to write before the current TocOffset to keep layout clean,
        // but simplest correct approach: write at existingHeader.TocOffset (overwrite old TOC area),
        // then append new TOC after.
        output.Seek(existingHeader.TocOffset, SeekOrigin.Begin);

        int newIndex = existingEntries.Count;
        var ext = Path.GetExtension(entryName);
        var useCompression = !AlreadyCompressedExtensions.Contains(ext);
        var compressionScheme = useCompression ? CompressionScheme.Zlib : CompressionScheme.None;
        var dataToEncrypt = useCompression ? ZlibCompress(newData) : newData;
        var contentHash = SHA256.HashData(newData);
        var entryIV = AopakCrypto.GenerateIV();
        var entryKey = AopakCrypto.DeriveEntryKey(archiveKey, newIndex, entryIV);
        var encryptedData = AopakCrypto.Encrypt(dataToEncrypt, entryKey, entryIV);

        long dataOffset = output.Position;
        writer.Write(encryptedData);

        existingEntries.Add(new AopakTocEntry
        {
            Name = entryName,
            DataOffset = dataOffset,
            DataSize = (uint)encryptedData.Length,
            OriginalSize = (uint)newData.Length,
            EntryIV = entryIV,
            ContentHash = contentHash,
            CompressionScheme = compressionScheme,
            EncryptionScheme = EncryptionScheme.AesCbc,
        });

        // Write new encrypted TOC
        long newTocOffset = output.Position;
        var newTocIV = AopakCrypto.GenerateIV();
        var newTocBytes = SerializeToc(existingEntries);
        var newEncryptedToc = AopakCrypto.Encrypt(newTocBytes, archiveKey, newTocIV);
        writer.Write(newEncryptedToc);

        // Truncate any trailing bytes from old data
        output.SetLength(output.Position);

        // Rewrite header
        output.Seek(0, SeekOrigin.Begin);
        var updatedHeader = new AopakHeader
        {
            Magic = AopakHeader.MagicString,
            Version = AopakHeader.CurrentVersion,
            ArchiveId = existingHeader.ArchiveId,
            EntryCount = (uint)existingEntries.Count,
            TocOffset = newTocOffset,
            TocSize = (uint)newEncryptedToc.Length,
            TocIV = newTocIV,
            LayerPriority = existingHeader.LayerPriority,
            SplitPartIndex = existingHeader.SplitPartIndex,
        };
        updatedHeader.Write(writer);
    }

    /// <summary>
    /// Append a tombstone TOC entry to an existing archive for a file that was deleted.
    /// The tombstone tells layered readers to not fall through to lower-priority archives.
    /// </summary>
    public static void AddTombstone(string archiveFile, string entryName, byte[] amk)
    {
        if (amk.Length != 32)
            throw new ArgumentException("AMK must be exactly 32 bytes.", nameof(amk));

        AopakHeader existingHeader;
        var existingEntries = new List<AopakTocEntry>();
        byte[] archiveKey;

        using (var readStream = new FileStream(archiveFile, FileMode.Open, FileAccess.Read))
        using (var reader = new BinaryReader(readStream))
        {
            existingHeader = AopakHeader.Read(reader);
            if (!existingHeader.IsValid())
                throw new InvalidDataException("Invalid AOPAK archive.");

            archiveKey = AopakCrypto.DeriveArchiveKey(amk, existingHeader.ArchiveId);

            readStream.Seek(existingHeader.TocOffset, SeekOrigin.Begin);
            var encryptedToc = reader.ReadBytes((int)existingHeader.TocSize);
            var tocBytes = AopakCrypto.Decrypt(encryptedToc, archiveKey, existingHeader.TocIV);

            using var tocStream = new MemoryStream(tocBytes);
            using var tocReader = new BinaryReader(tocStream);
            for (int i = 0; i < existingHeader.EntryCount; i++)
                existingEntries.Add(AopakTocEntry.Read(tocReader));
        }

        // Skip if tombstone already exists for this entry
        if (existingEntries.Any(e =>
            string.Equals(e.Name, entryName, StringComparison.OrdinalIgnoreCase) &&
            e.EncryptionScheme == EncryptionScheme.Tombstone))
            return;

        // Add tombstone entry (no data block — DataOffset/DataSize are zero)
        existingEntries.Add(new AopakTocEntry
        {
            Name = entryName,
            DataOffset = 0,
            DataSize = 0,
            OriginalSize = 0,
            EntryIV = new byte[16],
            ContentHash = new byte[32],
            CompressionScheme = CompressionScheme.None,
            EncryptionScheme = EncryptionScheme.Tombstone,
        });

        // Rewrite TOC with tombstone appended
        using var output = new FileStream(archiveFile, FileMode.Open, FileAccess.ReadWrite);
        using var writer = new BinaryWriter(output);

        output.Seek(existingHeader.TocOffset, SeekOrigin.Begin);
        var newTocIV = AopakCrypto.GenerateIV();
        var newTocBytes = SerializeToc(existingEntries);
        var newEncryptedToc = AopakCrypto.Encrypt(newTocBytes, archiveKey, newTocIV);
        writer.Write(newEncryptedToc);
        output.SetLength(output.Position);

        output.Seek(0, SeekOrigin.Begin);
        var updatedHeader = new AopakHeader
        {
            Magic = AopakHeader.MagicString,
            Version = AopakHeader.CurrentVersion,
            ArchiveId = existingHeader.ArchiveId,
            EntryCount = (uint)existingEntries.Count,
            TocOffset = existingHeader.TocOffset,
            TocSize = (uint)newEncryptedToc.Length,
            TocIV = newTocIV,
            LayerPriority = existingHeader.LayerPriority,
            SplitPartIndex = existingHeader.SplitPartIndex,
        };
        updatedHeader.Write(writer);
    }

    private static byte[] SerializeToc(IEnumerable<AopakTocEntry> entries)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        foreach (var entry in entries)
            entry.Write(writer);
        writer.Flush();
        return stream.ToArray();
    }

    private static byte[] ZlibCompress(byte[] data)
    {
        using var output = new MemoryStream();
        using (var zlib = new ZLibStream(output, CompressionLevel.Optimal, leaveOpen: true))
            zlib.Write(data, 0, data.Length);
        return output.ToArray();
    }
}
