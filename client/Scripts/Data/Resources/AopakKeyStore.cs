#nullable enable
using System;

namespace ArgentumNextgen.Data.Resources;

/// <summary>
/// Obfuscated storage for the Application Master Key.
/// The AMK is split into 3 XOR-masked fragments scattered across this class.
/// This is NOT cryptographic security — it's anti-strings obfuscation.
/// A determined reverse engineer can still recover the key from the binary.
/// The goal is to prevent casual extraction via `strings` or hex editors.
/// </summary>
internal static class AopakKeyStore
{
    // Fragment 1: bytes 0-10 (11 bytes), XOR-masked
    private static readonly byte[] _f1 = { 0x0C, 0xF3, 0x0A, 0xFA, 0x78, 0x6C, 0xDD, 0x83, 0xA5, 0xEA, 0xCF };
    private static readonly byte[] _m1 = { 0xED, 0xE5, 0x2C, 0x66, 0x7F, 0xC5, 0xAC, 0xE3, 0x29, 0xD9, 0x02 };

    // Fragment 2: bytes 11-21 (11 bytes), XOR-masked
    private static readonly byte[] _f2 = { 0x44, 0x23, 0xB8, 0x3F, 0x6A, 0x6D, 0x72, 0x60, 0xD4, 0x33, 0x5F };
    private static readonly byte[] _m2 = { 0xF9, 0xC8, 0x0D, 0x25, 0x2C, 0xC6, 0xD4, 0x36, 0xD8, 0x1C, 0x3E };

    // Fragment 3: bytes 22-31 (10 bytes), XOR-masked
    private static readonly byte[] _f3 = { 0xED, 0xAC, 0xF9, 0xE8, 0x29, 0x9F, 0x3D, 0xDC, 0xD0, 0x7E };
    private static readonly byte[] _m3 = { 0xB7, 0xC6, 0x7D, 0x14, 0xE3, 0x2C, 0x5B, 0xAB, 0x94, 0x0E };

    /// <summary>Reconstruct the AMK from obfuscated fragments.</summary>
    public static byte[] GetAmk()
    {
        byte[] amk = new byte[32];

        // Unmask fragment 1 → bytes 0-10
        for (int i = 0; i < _f1.Length; i++)
            amk[i] = (byte)(_f1[i] ^ _m1[i]);

        // Unmask fragment 2 → bytes 11-21
        for (int i = 0; i < _f2.Length; i++)
            amk[11 + i] = (byte)(_f2[i] ^ _m2[i]);

        // Unmask fragment 3 → bytes 22-31
        for (int i = 0; i < _f3.Length; i++)
            amk[22 + i] = (byte)(_f3[i] ^ _m3[i]);

        return amk;
    }
}
