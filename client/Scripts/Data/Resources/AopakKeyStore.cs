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
    // TODO: Run `aopak keygen --key "argentum-nextgen-dev-key-2026"` to generate real values
    // These are placeholder zeros that MUST be replaced before release
    private static readonly byte[] _f1 = new byte[11]; // PLACEHOLDER
    private static readonly byte[] _m1 = new byte[11]; // PLACEHOLDER

    // Fragment 2: bytes 11-21 (11 bytes), XOR-masked
    // TODO: Run `aopak keygen --key "argentum-nextgen-dev-key-2026"` to generate real values
    private static readonly byte[] _f2 = new byte[11]; // PLACEHOLDER
    private static readonly byte[] _m2 = new byte[11]; // PLACEHOLDER

    // Fragment 3: bytes 22-31 (10 bytes), XOR-masked
    // TODO: Run `aopak keygen --key "argentum-nextgen-dev-key-2026"` to generate real values
    private static readonly byte[] _f3 = new byte[10]; // PLACEHOLDER
    private static readonly byte[] _m3 = new byte[10]; // PLACEHOLDER

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
