using System.IO.Compression;
using System.Text;

if (args.Length < 2)
{
    Console.Error.WriteLine("Usage: ExtractTsaoArchive <archive.tsao> <output-dir> [xor-key]");
    Environment.Exit(2);
    return;
}

string archivePath = args[0];
string outputDir = args[1];
byte[] xorKey = Encoding.ASCII.GetBytes(args.Length >= 3 ? args[2] : "relokard0");

Directory.CreateDirectory(outputDir);

using var fs = File.OpenRead(archivePath);
using var reader = new BinaryReader(fs, Encoding.ASCII, leaveOpen: true);

int declaredSize = reader.ReadInt32();
short count = reader.ReadInt16();
if (count <= 0)
    throw new InvalidDataException($"Invalid TSAO entry count: {count}");

var entries = new List<Entry>(count);
for (int i = 0; i < count; i++)
{
    int start = reader.ReadInt32();
    int compressedSize = reader.ReadInt32();
    string name = Encoding.ASCII.GetString(reader.ReadBytes(32)).TrimEnd('\0', ' ');
    int uncompressedSize = reader.ReadInt32();

    if (string.IsNullOrWhiteSpace(name))
        throw new InvalidDataException($"Entry {i + 1} has an empty name");
    if (start <= 0 || compressedSize <= 0 || uncompressedSize <= 0)
        throw new InvalidDataException($"Entry {name} has invalid offsets/sizes");

    entries.Add(new Entry(start, compressedSize, name, uncompressedSize));
}

int extracted = 0;
foreach (var entry in entries)
{
    fs.Position = entry.Start - 1L; // VB6 Get positions are 1-based.
    byte[] compressed = reader.ReadBytes(entry.CompressedSize);
    if (compressed.Length != entry.CompressedSize)
        throw new EndOfStreamException($"Could not read compressed data for {entry.Name}");

    if (xorKey.Length > 0 && compressed.Length >= xorKey.Length)
    {
        for (int i = 0; i < xorKey.Length; i++)
            compressed[i] ^= xorKey[i];
    }

    using var input = new MemoryStream(compressed);
    using var zlib = new ZLibStream(input, CompressionMode.Decompress);
    using var output = new MemoryStream(entry.UncompressedSize);
    zlib.CopyTo(output);

    byte[] data = output.ToArray();
    if (data.Length != entry.UncompressedSize)
        Console.Error.WriteLine($"Warning: {entry.Name} extracted {data.Length} bytes, expected {entry.UncompressedSize}");

    string safeName = entry.Name.Replace('\\', Path.DirectorySeparatorChar).Replace('/', Path.DirectorySeparatorChar);
    string target = Path.GetFullPath(Path.Combine(outputDir, safeName));
    string root = Path.GetFullPath(outputDir) + Path.DirectorySeparatorChar;
    if (!target.StartsWith(root, StringComparison.OrdinalIgnoreCase))
        throw new InvalidDataException($"Unsafe path in archive: {entry.Name}");

    Directory.CreateDirectory(Path.GetDirectoryName(target)!);
    File.WriteAllBytes(target, data);
    extracted++;
}

Console.WriteLine($"Extracted {extracted} file(s) from {Path.GetFileName(archivePath)}. Declared size={declaredSize}, actual size={fs.Length}.");

internal readonly record struct Entry(int Start, int CompressedSize, string Name, int UncompressedSize);
