using System;
using System.Text;

namespace TierrasSagradasAO.Network;

/// <summary>
/// Crypto layers matching VB6 AoDefender encryption.
/// Outbound (client→server): Codificar(XOR) → AoDefEncode(base64)
/// Inbound (server→client): AoDefDecode(base64) → AoDefServDecrypt(hex)
/// Note: server outbound uses hex+base64 (no XOR), client outbound uses XOR+base64 (no hex).
/// </summary>
public static class AoEncryption
{
    private const string Base64Chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789+/";

    /// <summary>
    /// Full outbound pipeline: plaintext → XOR cipher → base64
    /// Server expects: AoDefDecode(base64) → DeCodificar(XOR, key)
    /// </summary>
    public static byte[] EncryptOutbound(byte[] plaintext, AoCipher cipher)
    {
        byte[] xored = cipher.Codificar(plaintext);
        byte[] b64 = AoDefEncode(xored);
        return b64;
    }

    /// <summary>
    /// Full inbound pipeline: base64 → hex → plaintext
    /// Server does NOT use XOR cipher on outbound, only hex+base64.
    /// </summary>
    public static byte[] DecryptInbound(byte[] data)
    {
        byte[] decoded = AoDefDecode(data);
        byte[] unhexed = AoDefServDecrypt(decoded);
        return unhexed;
    }

    /// <summary>
    /// Hex encode: each byte → 2 uppercase hex chars.
    /// </summary>
    public static byte[] AoDefServEncrypt(byte[] data)
    {
        byte[] result = new byte[data.Length * 2];
        for (int i = 0; i < data.Length; i++)
        {
            byte hi = (byte)(data[i] >> 4);
            byte lo = (byte)(data[i] & 0x0F);
            result[i * 2] = ConvToHex(hi);
            result[i * 2 + 1] = ConvToHex(lo);
        }
        return result;
    }

    /// <summary>
    /// Hex decode: pairs of hex chars → bytes.
    /// </summary>
    public static byte[] AoDefServDecrypt(byte[] data)
    {
        if (data.Length % 2 != 0)
        {
            // Handle odd length by ignoring last char
            byte[] trimmed = new byte[data.Length - 1];
            Array.Copy(data, trimmed, trimmed.Length);
            data = trimmed;
        }

        byte[] result = new byte[data.Length / 2];
        for (int i = 0; i < result.Length; i++)
        {
            result[i] = ConvToInt(data[i * 2], data[i * 2 + 1]);
        }
        return result;
    }

    /// <summary>
    /// Base64 encode with CRLF line wrapping every 72 chars.
    /// Matches VB6 AoDefEncode exactly.
    /// </summary>
    public static byte[] AoDefEncode(byte[] data)
    {
        var sb = new StringBuilder();
        int lineLen = 0;

        int i = 0;
        while (i < data.Length)
        {
            int remaining = data.Length - i;

            byte b0 = data[i];
            byte b1 = remaining > 1 ? data[i + 1] : (byte)0;
            byte b2 = remaining > 2 ? data[i + 2] : (byte)0;

            sb.Append(Base64Chars[(b0 >> 2) & 0x3F]);
            sb.Append(Base64Chars[((b0 << 4) | (b1 >> 4)) & 0x3F]);

            if (remaining > 1)
                sb.Append(Base64Chars[((b1 << 2) | (b2 >> 6)) & 0x3F]);
            else
                sb.Append('=');

            if (remaining > 2)
                sb.Append(Base64Chars[b2 & 0x3F]);
            else
                sb.Append('=');

            lineLen += 4;
            if (lineLen >= 72)
            {
                sb.Append("\r\n");
                lineLen = 0;
            }

            i += 3;
        }

        return Encoding.ASCII.GetBytes(sb.ToString());
    }

    /// <summary>
    /// Base64 decode, stripping CRLF.
    /// Matches VB6 AoDefDecode exactly.
    /// </summary>
    public static byte[] AoDefDecode(byte[] data)
    {
        // Strip CR/LF
        var clean = new StringBuilder();
        foreach (byte b in data)
        {
            if (b != '\r' && b != '\n')
                clean.Append((char)b);
        }

        string s = clean.ToString();
        // Remove padding for length calc
        int padCount = 0;
        if (s.EndsWith("==")) padCount = 2;
        else if (s.EndsWith("=")) padCount = 1;

        int outLen = (s.Length / 4) * 3 - padCount;
        byte[] result = new byte[outLen];
        int outIdx = 0;

        for (int j = 0; j < s.Length; j += 4)
        {
            int c0 = Base64CharToVal(s[j]);
            int c1 = j + 1 < s.Length ? Base64CharToVal(s[j + 1]) : 0;
            int c2 = j + 2 < s.Length ? Base64CharToVal(s[j + 2]) : 0;
            int c3 = j + 3 < s.Length ? Base64CharToVal(s[j + 3]) : 0;

            if (outIdx < outLen) result[outIdx++] = (byte)((c0 << 2) | (c1 >> 4));
            if (outIdx < outLen) result[outIdx++] = (byte)((c1 << 4) | (c2 >> 2));
            if (outIdx < outLen) result[outIdx++] = (byte)((c2 << 6) | c3);
        }

        return result;
    }

    private static byte ConvToHex(byte x)
    {
        return x > 9 ? (byte)(x + 55) : (byte)(x + (byte)'0');
    }

    private static byte ConvToInt(byte hi, byte lo)
    {
        int h = char.IsDigit((char)hi) ? (hi - '0') * 16 : (hi - 55) * 16;
        int l = char.IsDigit((char)lo) ? lo - '0' : lo - 55;
        return (byte)(h + l);
    }

    private static int Base64CharToVal(char c)
    {
        if (c == '=') return 0;
        int idx = Base64Chars.IndexOf(c);
        return idx >= 0 ? idx : 0;
    }
}
