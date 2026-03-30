using System.Text;

namespace AoPak;

public enum CompressionScheme : byte
{
    None = 0,
    Zlib = 1,
}

public enum EncryptionScheme : byte
{
    AesCbc = 1,
    Tombstone = 0xFF,
}

/// <summary>
/// 64-byte plaintext header at the start of every .aopak file.
/// </summary>
public struct AopakHeader
{
    public const string MagicString = "AOPAK\0";
    public const int HeaderSize = 64;
    public const ushort CurrentVersion = 1;

    public string Magic;           // 6 bytes
    public ushort Version;         // 2 bytes  → offset 6
    public byte[] ArchiveId;       // 16 bytes → offset 8
    public uint EntryCount;        // 4 bytes  → offset 24
    public long TocOffset;         // 8 bytes  → offset 28
    public uint TocSize;           // 4 bytes  → offset 36
    public byte[] TocIV;           // 16 bytes → offset 40
    public ushort LayerPriority;   // 2 bytes  → offset 56
    public ushort SplitPartIndex;  // 2 bytes  → offset 58
    // Reserved: 2 bytes           →           offset 60 → total 62... pad to 64

    public void Write(BinaryWriter writer)
    {
        // Magic: 6 bytes
        var magicBytes = Encoding.ASCII.GetBytes(MagicString);
        writer.Write(magicBytes);                // 6

        writer.Write(Version);                   // 2  → 8
        writer.Write(ArchiveId);                 // 16 → 24
        writer.Write(EntryCount);                // 4  → 28
        writer.Write(TocOffset);                 // 8  → 36
        writer.Write(TocSize);                   // 4  → 40
        writer.Write(TocIV);                     // 16 → 56
        writer.Write(LayerPriority);             // 2  → 58
        writer.Write(SplitPartIndex);            // 2  → 60
        writer.Write((ushort)0);                 // 2 reserved → 62
        writer.Write((ushort)0);                 // padding → 64  (extra 2 to reach 64)
    }

    public static AopakHeader Read(BinaryReader reader)
    {
        var header = new AopakHeader();

        var magicBytes = reader.ReadBytes(6);
        header.Magic = Encoding.ASCII.GetString(magicBytes);

        header.Version = reader.ReadUInt16();
        header.ArchiveId = reader.ReadBytes(16);
        header.EntryCount = reader.ReadUInt32();
        header.TocOffset = reader.ReadInt64();
        header.TocSize = reader.ReadUInt32();
        header.TocIV = reader.ReadBytes(16);
        header.LayerPriority = reader.ReadUInt16();
        header.SplitPartIndex = reader.ReadUInt16();
        reader.ReadUInt16(); // reserved
        reader.ReadUInt16(); // padding

        return header;
    }

    public bool IsValid()
    {
        return Magic == MagicString && Version == CurrentVersion;
    }
}

/// <summary>
/// One entry in the table of contents (stored encrypted).
/// </summary>
public class AopakTocEntry
{
    public string Name = string.Empty;       // relative path, e.g. "Graficos/123.png"
    public long DataOffset;                  // byte offset in archive file
    public uint DataSize;                    // encrypted size on disk
    public uint OriginalSize;               // uncompressed size
    public byte[] EntryIV = new byte[16];   // AES IV for this entry
    public byte[] ContentHash = new byte[32]; // SHA-256 of original uncompressed data
    public CompressionScheme CompressionScheme;
    public EncryptionScheme EncryptionScheme;

    public void Write(BinaryWriter writer)
    {
        var nameBytes = Encoding.UTF8.GetBytes(Name);
        writer.Write((ushort)nameBytes.Length);
        writer.Write(nameBytes);
        writer.Write(DataOffset);
        writer.Write(DataSize);
        writer.Write(OriginalSize);
        writer.Write(EntryIV);
        writer.Write(ContentHash);
        writer.Write((byte)CompressionScheme);
        writer.Write((byte)EncryptionScheme);
    }

    public static AopakTocEntry Read(BinaryReader reader)
    {
        var entry = new AopakTocEntry();

        ushort nameLen = reader.ReadUInt16();
        const ushort MaxNameLength = 4096;
        if (nameLen > MaxNameLength)
            throw new InvalidDataException($"Entry name length {nameLen} exceeds maximum.");
        var nameBytes = reader.ReadBytes(nameLen);
        entry.Name = Encoding.UTF8.GetString(nameBytes);
        entry.DataOffset = reader.ReadInt64();
        entry.DataSize = reader.ReadUInt32();
        entry.OriginalSize = reader.ReadUInt32();
        entry.EntryIV = reader.ReadBytes(16);
        entry.ContentHash = reader.ReadBytes(32);
        entry.CompressionScheme = (CompressionScheme)reader.ReadByte();
        entry.EncryptionScheme = (EncryptionScheme)reader.ReadByte();

        return entry;
    }
}
