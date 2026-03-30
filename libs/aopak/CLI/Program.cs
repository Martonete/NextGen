using AoPak;
using System.Security.Cryptography;
using System.Text;

// Usage:
// aopak pack <input-dir> <output.aopak> [--key <passphrase>]
// aopak unpack <input.aopak> <output-dir> [--key <passphrase>]
// aopak list <input.aopak> [--key <passphrase>]
// aopak verify <input.aopak> [--key <passphrase>]

if (args.Length < 2)
{
    PrintUsage();
    return 1;
}

string command = args[0].ToLowerInvariant();
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
    if (args.Length < 3) { Console.Error.WriteLine("Usage: aopak pack <input-dir> <output.aopak>"); return 1; }
    string inputDir = args[1];
    string outputFile = args[2];

    if (!Directory.Exists(inputDir)) { Console.Error.WriteLine($"Directory not found: {inputDir}"); return 1; }

    Console.WriteLine($"Packing {inputDir} → {outputFile}");
    AopakWriter.Pack(inputDir, outputFile, amk, progress: (cur, total, name) =>
    {
        Console.Write($"\r[{cur}/{total}] {name}".PadRight(80));
    });
    Console.WriteLine();
    Console.WriteLine("Done.");
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
        Console.WriteLine($"  {name}");
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
    Console.WriteLine("  aopak pack <input-dir> <output.aopak> [--key <passphrase>]");
    Console.WriteLine("  aopak unpack <input.aopak> <output-dir> [--key <passphrase>]");
    Console.WriteLine("  aopak list <input.aopak> [--key <passphrase>]");
    Console.WriteLine("  aopak verify <input.aopak> [--key <passphrase>]");
}
