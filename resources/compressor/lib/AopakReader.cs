using System.IO.Compression;
using System.Security.Cryptography;

namespace AoPak;

public class AopakReader : IDisposable
{
    private readonly FileStream _stream;
    private readonly BinaryReader _reader;
    private readonly AopakHeader _header;
    private readonly Dictionary<string, (AopakTocEntry Entry, int Index)> _toc;
    private readonly byte[] _archiveKey;
    private bool _disposed;

    public AopakHeader Header => _header;

    public AopakReader(string filePath, byte[] amk)
    {
        _stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        _reader = new BinaryReader(_stream);

        // 1. Read the 64-byte plaintext header
        _header = AopakHeader.Read(_reader);

        // 2. Validate magic bytes
        if (!_header.IsValid())
            throw new InvalidDataException($"Invalid AOPAK archive: bad magic or version in '{filePath}'.");

        // 3. Derive archive key from AMK + ArchiveId
        _archiveKey = AopakCrypto.DeriveArchiveKey(amk, _header.ArchiveId);

        // 4. Validate header bounds before allocating
        const int MaxTocSize = 64 * 1024 * 1024; // 64 MB
        const uint MaxEntryCount = 1_000_000;

        if (_header.TocSize > MaxTocSize)
            throw new InvalidDataException($"TOC size {_header.TocSize} exceeds maximum ({MaxTocSize}).");
        if (_header.EntryCount > MaxEntryCount)
            throw new InvalidDataException($"Entry count {_header.EntryCount} exceeds maximum ({MaxEntryCount}).");

        // 5. Seek to TocOffset, read TocSize bytes
        _stream.Seek(_header.TocOffset, SeekOrigin.Begin);
        var encryptedToc = _reader.ReadBytes((int)_header.TocSize);

        // 6. Decrypt TOC using archive key + TocIV
        var tocBytes = AopakCrypto.Decrypt(encryptedToc, _archiveKey, _header.TocIV);

        // 7. Parse TOC entries into dictionary
        _toc = new Dictionary<string, (AopakTocEntry, int)>(StringComparer.OrdinalIgnoreCase);
        using var tocStream = new MemoryStream(tocBytes);
        using var tocReader = new BinaryReader(tocStream);

        for (int i = 0; i < _header.EntryCount; i++)
        {
            var entry = AopakTocEntry.Read(tocReader);
            _toc[entry.Name] = (entry, i);
        }
    }

    public bool Contains(string name) => _toc.ContainsKey(name);

    /// <summary>Returns true if the entry exists and is marked as a tombstone (deleted).</summary>
    public bool IsTombstone(string name)
    {
        return _toc.TryGetValue(name, out var item) &&
               item.Entry.EncryptionScheme == EncryptionScheme.Tombstone;
    }

    public IReadOnlyCollection<string> GetEntryNames() => _toc.Keys;

    /// <summary>
    /// Read, decrypt, decompress, and verify a single entry. Returns original bytes.
    /// </summary>
    public byte[] ReadEntry(string name)
    {
        if (!_toc.TryGetValue(name, out var item))
            throw new KeyNotFoundException($"Entry '{name}' not found in archive.");

        var (entry, index) = item;

        // Tombstoned entries are deleted — treat as not found
        if (entry.EncryptionScheme == EncryptionScheme.Tombstone)
            throw new KeyNotFoundException($"Entry '{name}' has been deleted (tombstone).");

        // 1. Validate offsets before seeking
        if (entry.DataOffset < AopakHeader.HeaderSize || entry.DataOffset >= _stream.Length)
            throw new InvalidDataException($"Entry '{name}' has invalid data offset ({entry.DataOffset}).");
        if ((long)entry.DataSize > _stream.Length - entry.DataOffset)
            throw new InvalidDataException($"Entry '{name}' data size ({entry.DataSize}) extends beyond archive end.");

        // 2. Seek and read encrypted data
        _stream.Seek(entry.DataOffset, SeekOrigin.Begin);
        var encryptedData = _reader.ReadBytes((int)entry.DataSize);

        // 3. Derive entry key
        var entryKey = AopakCrypto.DeriveEntryKey(_archiveKey, index, entry.EntryIV);

        // 4. Decrypt with AES-256-CBC
        var decryptedData = AopakCrypto.Decrypt(encryptedData, entryKey, entry.EntryIV);

        // 5. Decompress if needed
        byte[] originalData;
        if (entry.CompressionScheme == CompressionScheme.Zlib)
        {
            originalData = ZlibDecompress(decryptedData);
        }
        else
        {
            originalData = decryptedData;
        }

        // 6. Verify SHA-256 hash
        var actualHash = SHA256.HashData(originalData);
        if (!actualHash.AsSpan().SequenceEqual(entry.ContentHash))
            throw new InvalidDataException($"Content hash mismatch for entry '{name}'. Archive may be corrupt.");

        return originalData;
    }

    private static byte[] ZlibDecompress(byte[] compressed)
    {
        // ZLibStream requires the zlib header (RFC 1950). DeflateStream does raw deflate.
        // .NET 8 ZLibStream handles zlib-wrapped deflate.
        using var input = new MemoryStream(compressed);
        using var zlib = new ZLibStream(input, CompressionMode.Decompress);
        using var output = new MemoryStream();
        zlib.CopyTo(output);
        return output.ToArray();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _reader.Dispose();
        _stream.Dispose();
        Array.Clear(_archiveKey, 0, _archiveKey.Length);
    }
}
