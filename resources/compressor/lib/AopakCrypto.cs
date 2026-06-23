using System.Security.Cryptography;

namespace AoPak;

public static class AopakCrypto
{
    private const string HkdfInfo = "AOPAK-ARCHIVE-KEY";

    /// <summary>
    /// Derive per-archive key: HKDF-SHA256(AMK, salt=ArchiveId, info="AOPAK-ARCHIVE-KEY", length=32)
    /// </summary>
    public static byte[] DeriveArchiveKey(byte[] amk, byte[] archiveId)
    {
        var infoBytes = System.Text.Encoding.ASCII.GetBytes(HkdfInfo);
        return HKDF.DeriveKey(HashAlgorithmName.SHA256, amk, 32, archiveId, infoBytes);
    }

    /// <summary>
    /// Derive per-entry key: SHA256(archiveKey || entryIndex_4bytes || entryIV)
    /// </summary>
    public static byte[] DeriveEntryKey(byte[] archiveKey, int entryIndex, byte[] entryIV)
    {
        var indexBytes = BitConverter.GetBytes(entryIndex); // 4 bytes, little-endian
        var combined = new byte[archiveKey.Length + 4 + entryIV.Length];
        Buffer.BlockCopy(archiveKey, 0, combined, 0, archiveKey.Length);
        Buffer.BlockCopy(indexBytes, 0, combined, archiveKey.Length, 4);
        Buffer.BlockCopy(entryIV, 0, combined, archiveKey.Length + 4, entryIV.Length);
        return SHA256.HashData(combined);
    }

    /// <summary>AES-256-CBC encrypt with PKCS7 padding.</summary>
    public static byte[] Encrypt(byte[] data, byte[] key, byte[] iv)
    {
        using var aes = Aes.Create();
        aes.KeySize = 256;
        aes.Mode = CipherMode.CBC;
        aes.Padding = PaddingMode.PKCS7;
        aes.Key = key;
        aes.IV = iv;

        using var encryptor = aes.CreateEncryptor();
        return encryptor.TransformFinalBlock(data, 0, data.Length);
    }

    /// <summary>AES-256-CBC decrypt with PKCS7 padding.</summary>
    public static byte[] Decrypt(byte[] data, byte[] key, byte[] iv)
    {
        using var aes = Aes.Create();
        aes.KeySize = 256;
        aes.Mode = CipherMode.CBC;
        aes.Padding = PaddingMode.PKCS7;
        aes.Key = key;
        aes.IV = iv;

        using var decryptor = aes.CreateDecryptor();
        return decryptor.TransformFinalBlock(data, 0, data.Length);
    }

    /// <summary>Generate a random 16-byte IV.</summary>
    public static byte[] GenerateIV()
    {
        var iv = new byte[16];
        RandomNumberGenerator.Fill(iv);
        return iv;
    }

    /// <summary>Generate a random 16-byte ArchiveId (GUID-sized).</summary>
    public static byte[] GenerateArchiveId()
    {
        var id = new byte[16];
        RandomNumberGenerator.Fill(id);
        return id;
    }
}
