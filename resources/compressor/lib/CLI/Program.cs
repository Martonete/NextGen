using AoPak;
using AoPak.CLI;
using System.Security.Cryptography;
using System.Text;

// Usage:
// aopak pack <input-dir> [<output.aopak>] [--outputDir <dir>] [--key <passphrase>]
// aopak unpack <input.aopak> <output-dir> [--key <passphrase>]
// aopak list <input.aopak> [--key <passphrase>]
// aopak verify <input.aopak> [--key <passphrase>]
// aopak patch --old <OldDir> --new <NewDir> --output <patch.aopak> --layer <N> [--key <passphrase>]
// aopak squash --dir <ArchiveDir> --output <NewBase.aopak> [--key <passphrase>]
// aopak manifest --dir <ArchiveDir> [--sign] [--key <passphrase>]
// aopak keygen --key <passphrase>

if (args.Length < 1)
{
    PrintUsage();
    return 1;
}

string command = args[0].ToLowerInvariant();

// keygen does not require an AMK — handle before GetAmk()
if (command == "keygen")
    return KeyGenerator.Run(args);

byte[] amk = GetAmk(args);

try
{
    switch (command)
    {
        case "pack":
            return Pack(args, amk);
        case "unpack":
            return Unpack(args, amk);
        case "list":
            return List(args, amk);
        case "verify":
            return Verify(args, amk);
        case "patch":
            return Patch(args, amk);
        case "squash":
            return Squash(args, amk);
        case "manifest":
            return Manifest(args, amk);
        default:
            Console.Error.WriteLine($"Unknown command: {command}");
            PrintUsage();
            return 1;
    }
}
catch (Exception ex)
{
    Console.Error.WriteLine($"ERROR: {ex.Message}");
    return 1;
}

// --- Command implementations ---

static int Pack(string[] args, byte[] amk)
{
    if (args.Length < 2) { Console.Error.WriteLine("Usage: aopak pack <input-dir> [<output.aopak>] [--outputDir <dir>]"); return 1; }
    string inputDir = args[1];

    if (!Directory.Exists(inputDir)) { Console.Error.WriteLine($"Directory not found: {inputDir}"); return 1; }

    // Determine output file path
    string outputFile;
    string? outputDir = null;
    for (int i = 2; i < args.Length - 1; i++)
    {
        if (args[i] == "--outputDir") { outputDir = args[i + 1]; i++; }
    }

    if (outputDir != null)
    {
        // --outputDir mode: derive filename from input dir name (Maps → maps.aopak)
        string dirName = Path.GetFileName(Path.GetFullPath(inputDir).TrimEnd(Path.DirectorySeparatorChar));
        outputFile = Path.Combine(outputDir, dirName.ToLowerInvariant() + ".aopak");
    }
    else if (args.Length >= 3 && !args[2].StartsWith("--"))
    {
        // Explicit output path: aopak pack <input> <output.aopak>
        outputFile = args[2];
    }
    else
    {
        // Default: output alongside the input dir's parent (resources/data/Maps → resources/maps.aopak)
        string parentDir = Path.GetDirectoryName(Path.GetFullPath(inputDir)) ?? ".";
        string dirName = Path.GetFileName(Path.GetFullPath(inputDir).TrimEnd(Path.DirectorySeparatorChar));
        outputFile = Path.Combine(parentDir, dirName.ToLowerInvariant() + ".aopak");
    }

    Console.WriteLine($"Packing {inputDir} → {outputFile}");
    AopakWriter.Pack(inputDir, outputFile, amk, progress: (cur, total, name) =>
    {
        Console.Write($"\r[{cur}/{total}] {name}".PadRight(80));
    });
    Console.WriteLine();
    Console.WriteLine($"Done: {outputFile}");
    return 0;
}

static int Unpack(string[] args, byte[] amk)
{
    if (args.Length < 3) { Console.Error.WriteLine("Usage: aopak unpack <input.aopak> <output-dir>"); return 1; }
    string inputFile = args[1];
    string outputDir = args[2];

    using var reader = new AopakReader(inputFile, amk);
    var names = reader.GetEntryNames();
    Console.WriteLine($"Unpacking {names.Count} entries → {outputDir}");

    int i = 0;
    foreach (var name in names)
    {
        if (reader.IsTombstone(name)) continue;
        i++;
        Console.Write($"\r[{i}/{names.Count}] {name}".PadRight(80));

        byte[] data = reader.ReadEntry(name);
        string outPath = Path.GetFullPath(Path.Combine(outputDir, name));
        string rootPath = Path.GetFullPath(outputDir) + Path.DirectorySeparatorChar;
        if (!outPath.StartsWith(rootPath, StringComparison.Ordinal))
        {
            Console.Error.WriteLine($"  SKIP: {name} (path traversal detected)");
            continue;
        }
        Directory.CreateDirectory(Path.GetDirectoryName(outPath)!);
        File.WriteAllBytes(outPath, data);
    }
    Console.WriteLine();
    Console.WriteLine("Done.");
    return 0;
}

static int List(string[] args, byte[] amk)
{
    if (args.Length < 2) { Console.Error.WriteLine("Usage: aopak list <input.aopak>"); return 1; }
    string inputFile = args[1];

    using var reader = new AopakReader(inputFile, amk);
    var names = reader.GetEntryNames();
    Console.WriteLine($"Archive: {inputFile} ({names.Count} entries, Layer={reader.Header.LayerPriority}, Part={reader.Header.SplitPartIndex})");
    Console.WriteLine();
    foreach (var name in names)
        Console.WriteLine(reader.IsTombstone(name) ? $"  [TOMBSTONE] {name}" : $"  {name}");
    return 0;
}

static int Verify(string[] args, byte[] amk)
{
    if (args.Length < 2) { Console.Error.WriteLine("Usage: aopak verify <input.aopak>"); return 1; }
    string inputFile = args[1];

    using var reader = new AopakReader(inputFile, amk);
    var names = reader.GetEntryNames();
    Console.WriteLine($"Verifying {names.Count} entries...");

    int ok = 0, fail = 0;
    foreach (var name in names)
    {
        if (reader.IsTombstone(name)) continue;
        try
        {
            reader.ReadEntry(name); // This verifies hash internally
            ok++;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"  FAIL: {name} — {ex.Message}");
            fail++;
        }
    }
    Console.WriteLine($"Result: {ok} OK, {fail} FAILED");
    return fail > 0 ? 1 : 0;
}


static int Patch(string[] args, byte[] amk)
{
    string? oldDir = null, newDir = null, outputFile = null;
    ushort layer = 1;

    for (int i = 1; i < args.Length - 1; i++)
    {
        switch (args[i])
        {
            case "--old":    oldDir     = args[++i]; break;
            case "--new":    newDir     = args[++i]; break;
            case "--output": outputFile = args[++i]; break;
            case "--layer":  layer      = ushort.Parse(args[++i]); break;
        }
    }

    if (oldDir == null || newDir == null || outputFile == null)
    {
        Console.Error.WriteLine("Usage: aopak patch --old <OldDir> --new <NewDir> --output <patch.aopak> --layer <N>");
        return 1;
    }
    if (!Directory.Exists(oldDir)) { Console.Error.WriteLine($"Directory not found: {oldDir}"); return 1; }
    if (!Directory.Exists(newDir)) { Console.Error.WriteLine($"Directory not found: {newDir}"); return 1; }

    var oldFiles = CollectFiles(oldDir);
    var newFiles = CollectFiles(newDir);

    var changedOrAdded = new List<string>();
    var deleted = new List<string>();

    foreach (var (rel, newPath) in newFiles)
    {
        if (!oldFiles.TryGetValue(rel, out var oldPath))
        {
            changedOrAdded.Add(rel);
        }
        else
        {
            var oldHash = SHA256.HashData(File.ReadAllBytes(oldPath));
            var newHash = SHA256.HashData(File.ReadAllBytes(newPath));
            if (!oldHash.AsSpan().SequenceEqual(newHash))
                changedOrAdded.Add(rel);
        }
    }

    foreach (var rel in oldFiles.Keys)
    {
        if (!newFiles.ContainsKey(rel))
            deleted.Add(rel);
    }

    Console.WriteLine($"Patch: {changedOrAdded.Count} changed/added, {deleted.Count} deleted");

    if (changedOrAdded.Count == 0 && deleted.Count == 0)
    {
        Console.WriteLine("No changes detected. Patch archive not written.");
        return 0;
    }

    Console.WriteLine($"Writing patch archive with layer={layer}...");

    var tempDir = Path.Combine(Path.GetTempPath(), $"aopak-patch-{Guid.NewGuid():N}");
    Directory.CreateDirectory(tempDir);

    try
    {
        foreach (var rel in changedOrAdded)
        {
            var src = newFiles[rel];
            var dst = Path.Combine(tempDir, rel.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(dst)!);
            File.Copy(src, dst, overwrite: true);
        }

        if (changedOrAdded.Count > 0)
        {
            AopakWriter.Pack(tempDir, outputFile, amk, layerPriority: layer, progress: (cur, total, name) =>
            {
                Console.Write($"\r[{cur}/{total}] {name}".PadRight(80));
            });
            Console.WriteLine();
        }
        else
        {
            // No changed files but have deletions — create minimal archive with just tombstones
            // Pack an empty directory first (AopakWriter handles zero entries)
            AopakWriter.Pack(tempDir, outputFile, amk, layerPriority: layer);
        }

        if (deleted.Count > 0)
        {
            Console.WriteLine($"Adding {deleted.Count} tombstone(s) for deleted files...");
            foreach (var rel in deleted)
            {
                AopakWriter.AddTombstone(outputFile, rel, amk);
                Console.WriteLine($"  tombstone: {rel}");
            }
        }
    }
    finally
    {
        Directory.Delete(tempDir, recursive: true);
    }

    Console.WriteLine("Done.");
    return 0;
}

static int Squash(string[] args, byte[] amk)
{
    string? archiveDir = null, outputFile = null;

    for (int i = 1; i < args.Length - 1; i++)
    {
        switch (args[i])
        {
            case "--dir":    archiveDir = args[++i]; break;
            case "--output": outputFile = args[++i]; break;
        }
    }

    if (archiveDir == null || outputFile == null)
    {
        Console.Error.WriteLine("Usage: aopak squash --dir <ArchiveDir> --output <NewBase.aopak>");
        return 1;
    }
    if (!Directory.Exists(archiveDir)) { Console.Error.WriteLine($"Directory not found: {archiveDir}"); return 1; }

    string manifestPath = Path.Combine(archiveDir, AopakManifest.ManifestFileName);
    List<AopakManifest.ArchiveEntry> entries;

    if (File.Exists(manifestPath))
    {
        entries = AopakManifest.Read(manifestPath, amk);
        entries.Sort((a, b) => b.Layer.CompareTo(a.Layer));
        Console.WriteLine($"Squashing {entries.Count} archive(s) from manifest...");
    }
    else
    {
        entries = Directory.GetFiles(archiveDir, "*.aopak")
            .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
            .Select(f => new AopakManifest.ArchiveEntry(Path.GetFileName(f), "unknown", 0, 0))
            .ToList();
        Console.WriteLine($"No manifest found. Squashing {entries.Count} archive(s)...");
    }

    var merged = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
    var tombstoned = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    foreach (var entry in entries)
    {
        string archivePath = Path.Combine(archiveDir, entry.FileName);
        if (!File.Exists(archivePath))
        {
            Console.Error.WriteLine($"  SKIP (missing): {entry.FileName}");
            continue;
        }

        using var reader = new AopakReader(archivePath, amk);
        foreach (var name in reader.GetEntryNames())
        {
            if (merged.ContainsKey(name) || tombstoned.Contains(name))
                continue;

            if (reader.IsTombstone(name))
            {
                tombstoned.Add(name);
                continue;
            }

            try
            {
                merged[name] = reader.ReadEntry(name);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"  WARN: could not read '{name}' from {entry.FileName}: {ex.Message}");
            }
        }
    }

    Console.WriteLine($"Merged: {merged.Count} entries ({tombstoned.Count} tombstoned/skipped)");
    Console.WriteLine($"Writing squashed archive → {outputFile}");

    var tempDir = Path.Combine(Path.GetTempPath(), $"aopak-squash-{Guid.NewGuid():N}");
    Directory.CreateDirectory(tempDir);
    try
    {
        int idx = 0;
        foreach (var (name, data) in merged)
        {
            idx++;
            var dst = Path.Combine(tempDir, name.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(dst)!);
            File.WriteAllBytes(dst, data);
        }

        AopakWriter.Pack(tempDir, outputFile, amk, layerPriority: 0, progress: (cur, total, name) =>
        {
            Console.Write($"\r  packing [{cur}/{total}] {name}".PadRight(80));
        });
        Console.WriteLine();
    }
    finally
    {
        Directory.Delete(tempDir, recursive: true);
    }

    Console.WriteLine("Done.");
    return 0;
}

static int Manifest(string[] args, byte[] amk)
{
    string? archiveDir = null;
    bool sign = false;

    for (int i = 1; i < args.Length; i++)
    {
        switch (args[i])
        {
            case "--dir":  archiveDir = args[++i]; break;
            case "--sign": sign = true; break;
        }
    }

    if (archiveDir == null)
    {
        Console.Error.WriteLine("Usage: aopak manifest --dir <ArchiveDir> [--sign]");
        return 1;
    }
    if (!Directory.Exists(archiveDir)) { Console.Error.WriteLine($"Directory not found: {archiveDir}"); return 1; }

    Console.WriteLine($"Scanning {archiveDir} for .aopak files...");
    var entries = AopakManifest.ScanDirectory(archiveDir, amk);
    Console.WriteLine($"Found {entries.Count} archive(s).");

    if (!sign)
        Console.WriteLine("NOTE: Use --sign to include HMAC. Unsigned manifest will fail HMAC verification.");

    string manifestPath = Path.Combine(archiveDir, AopakManifest.ManifestFileName);
    AopakManifest.Write(manifestPath, entries, amk);
    Console.WriteLine($"Manifest written: {manifestPath}");

    foreach (var entry in entries)
        Console.WriteLine($"  [{entry.FileName}] type={entry.Type} layer={entry.Layer} part={entry.Part}");

    return 0;
}

/// <summary>Collect all files in a directory recursively. Returns relative path → full path.</summary>
static Dictionary<string, string> CollectFiles(string dir)
{
    var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    foreach (var file in Directory.GetFiles(dir, "*", SearchOption.AllDirectories))
    {
        var rel = Path.GetRelativePath(dir, file).Replace('\\', '/');
        result[rel] = file;
    }
    return result;
}

// --- Helpers ---

static byte[] GetAmk(string[] args)
{
    // Check for env var first (preferred, doesn't leak in process listing)
    var envKey = Environment.GetEnvironmentVariable("AOPAK_KEY");
    if (!string.IsNullOrEmpty(envKey))
        return SHA256.HashData(Encoding.UTF8.GetBytes(envKey));

    // Check for --key flag (WARNING: visible in ps/cmdline)
    for (int i = 0; i < args.Length - 1; i++)
    {
        if (args[i] == "--key")
            return SHA256.HashData(Encoding.UTF8.GetBytes(args[i + 1]));
    }

#if DEBUG
    // Development fallback — NEVER available in release builds
    return SHA256.HashData(Encoding.UTF8.GetBytes("argentum-nextgen-dev-key-2026"));
#else
    Console.Error.WriteLine("ERROR: No key provided. Set AOPAK_KEY env var or use --key <passphrase>");
    Environment.Exit(1);
    return Array.Empty<byte>(); // unreachable
#endif
}

static void PrintUsage()
{
    Console.WriteLine("AoPak — Encrypted Resource Archive Tool");
    Console.WriteLine();
    Console.WriteLine("Usage:");
    Console.WriteLine("  aopak pack <input-dir> [<output.aopak>] [--outputDir <dir>] [--key <passphrase>]");
    Console.WriteLine("  aopak unpack <input.aopak> <output-dir> [--key <passphrase>]");
    Console.WriteLine("  aopak list <input.aopak> [--key <passphrase>]");
    Console.WriteLine("  aopak verify <input.aopak> [--key <passphrase>]");
    Console.WriteLine("  aopak patch --old <OldDir> --new <NewDir> --output <patch.aopak> --layer <N> [--key <passphrase>]");
    Console.WriteLine("  aopak squash --dir <ArchiveDir> --output <NewBase.aopak> [--key <passphrase>]");
    Console.WriteLine("  aopak manifest --dir <ArchiveDir> [--sign] [--key <passphrase>]");
    Console.WriteLine("  aopak keygen --key <passphrase>");
    Console.WriteLine();
    Console.WriteLine("keygen: Derives AMK from passphrase (SHA256) and outputs obfuscated");
    Console.WriteLine("        C# byte array literals to paste into AopakKeyStore.cs");
}
