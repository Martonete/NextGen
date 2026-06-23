using System;
using System.Security.Cryptography;
using System.Text;

namespace AoPak.CLI;

/// <summary>
/// Generates obfuscated C# byte array literals for AopakKeyStore.cs.
/// Usage: aopak keygen --key &lt;passphrase&gt;
/// </summary>
internal static class KeyGenerator
{
    public static int Run(string[] args)
    {
        string? passphrase = null;
        for (int i = 0; i < args.Length - 1; i++)
        {
            if (args[i] == "--key")
            {
                passphrase = args[i + 1];
                break;
            }
        }

        if (passphrase is null)
        {
            Console.Error.WriteLine("Usage: aopak keygen --key <passphrase>");
            return 1;
        }

        byte[] amk = SHA256.HashData(Encoding.UTF8.GetBytes(passphrase));

        // Generate random XOR masks
        byte[] m1 = RandomNumberGenerator.GetBytes(11);
        byte[] m2 = RandomNumberGenerator.GetBytes(11);
        byte[] m3 = RandomNumberGenerator.GetBytes(10);

        // Compute fragments: f[i] = amk[offset + i] ^ mask[i]
        byte[] f1 = new byte[11];
        byte[] f2 = new byte[11];
        byte[] f3 = new byte[10];

        for (int i = 0; i < 11; i++) f1[i] = (byte)(amk[i] ^ m1[i]);
        for (int i = 0; i < 11; i++) f2[i] = (byte)(amk[11 + i] ^ m2[i]);
        for (int i = 0; i < 10; i++) f3[i] = (byte)(amk[22 + i] ^ m3[i]);

        Console.WriteLine("// Generated AoPak KeyStore fragments");
        Console.WriteLine("// Paste these into AopakKeyStore.cs");
        Console.WriteLine();
        Console.WriteLine($"private static readonly byte[] _f1 = {{ {FormatBytes(f1)} }}; // 11 bytes");
        Console.WriteLine($"private static readonly byte[] _m1 = {{ {FormatBytes(m1)} }}; // 11 bytes");
        Console.WriteLine($"private static readonly byte[] _f2 = {{ {FormatBytes(f2)} }}; // 11 bytes");
        Console.WriteLine($"private static readonly byte[] _m2 = {{ {FormatBytes(m2)} }}; // 11 bytes");
        Console.WriteLine($"private static readonly byte[] _f3 = {{ {FormatBytes(f3)} }}; // 10 bytes");
        Console.WriteLine($"private static readonly byte[] _m3 = {{ {FormatBytes(m3)} }}; // 10 bytes");

        return 0;
    }

    private static string FormatBytes(byte[] bytes)
    {
        var sb = new StringBuilder();
        for (int i = 0; i < bytes.Length; i++)
        {
            if (i > 0) sb.Append(", ");
            sb.Append($"0x{bytes[i]:X2}");
        }
        return sb.ToString();
    }
}
