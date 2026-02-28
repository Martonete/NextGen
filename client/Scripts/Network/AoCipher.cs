using System;
using System.Text;

namespace TierrasSagradasAO.Network;

/// <summary>
/// XOR stream cipher matching VB6 AoDefender: Codificar/DeCodificar + Numero2Letra + Semilla.
/// Packet counter rotates 0→999999, per-packet key derivation.
/// </summary>
public class AoCipher
{
    private int _packetCounter;

    public AoCipher()
    {
        _packetCounter = 0;
    }

    /// <summary>
    /// Encrypt data before sending to server.
    /// Returns encrypted bytes and advances the packet counter.
    /// </summary>
    public byte[] Codificar(byte[] data)
    {
        _packetCounter++;
        if (_packetCounter > 999999)
            _packetCounter = 0;

        string keyText = Numero2Letra(_packetCounter).Replace(" ", "");
        string seedStr = Semilla(keyText);
        return CodificarWithSeed(data, seedStr);
    }

    /// <summary>
    /// Decrypt data received from server.
    /// Server outbound does NOT use XOR cipher — only hex+base64.
    /// This is used if the server sends XOR-encrypted packets.
    /// </summary>
    public byte[] DeCodificar(byte[] data, string seed)
    {
        return DeCodificarWithSeed(data, seed);
    }

    public static byte[] CodificarWithSeed(byte[] data, string seed)
    {
        var parts = seed.Split(',');
        int s1 = int.Parse(parts[0]);
        int s2 = int.Parse(parts[1]);

        byte[] result = new byte[data.Length];
        for (int i = 0; i < data.Length; i++)
        {
            int pos = i + 1; // 1-based
            s1 -= pos;
            s2 += pos;

            if (pos % 2 == 0)
            {
                // Even position: byte = (byte - s1) & 0xFF
                result[i] = (byte)((data[i] - s1) & 0xFF);
            }
            else
            {
                // Odd position: byte = (byte + s2) & 0xFF
                result[i] = (byte)((data[i] + s2) & 0xFF);
            }
        }
        return result;
    }

    public static byte[] DeCodificarWithSeed(byte[] data, string seed)
    {
        var parts = seed.Split(',');
        int s1 = int.Parse(parts[0]);
        int s2 = int.Parse(parts[1]);

        byte[] result = new byte[data.Length];
        for (int i = 0; i < data.Length; i++)
        {
            int pos = i + 1;
            s1 -= pos;
            s2 += pos;

            if (pos % 2 == 0)
            {
                result[i] = (byte)((data[i] + s1) & 0xFF);
            }
            else
            {
                result[i] = (byte)((data[i] - s2) & 0xFF);
            }
        }
        return result;
    }

    /// <summary>
    /// Generate seed pair from key string.
    /// seed1 += ascii(c) * (i+1), seed2 += ascii(c) * (len - i)
    /// </summary>
    public static string Semilla(string key)
    {
        byte[] bytes = Encoding.ASCII.GetBytes(key);
        int len = bytes.Length;
        long s1 = 0, s2 = 0;

        for (int i = 0; i < len; i++)
        {
            s1 += bytes[i] * (long)(i + 1);
            s2 += bytes[i] * (long)(len - i);
        }

        return $"{s1},{s2}";
    }

    /// <summary>
    /// Convert number to Spanish words with ZiPPy/NoPPy suffixes.
    /// Matches VB6 Numero2Letra exactly.
    /// </summary>
    public static string Numero2Letra(int n)
    {
        if (n == 0) return "cero ZiPPyes";

        string result = ConvertGroup(n);
        result = result.Trim();

        // Remove trailing spaces and add suffix
        string noSpaces = result.Replace("  ", " ").Trim();

        if (n == 1)
            return noSpaces + " ZiPPy";
        else
            return noSpaces + " ZiPPyes";
    }

    private static string ConvertGroup(int n)
    {
        if (n == 0) return "";

        string[] unidades = { "", "un", "dos", "tres", "cuatro", "cinco",
                              "seis", "siete", "ocho", "nueve" };
        string[] especiales = { "diez", "once", "doce", "trece", "catorce",
                                "quince", "dieciseis", "diecisiete", "dieciocho", "diecinueve" };
        string[] decenas = { "", "diez", "veinte", "treinta", "cuarenta", "cincuenta",
                             "sesenta", "setenta", "ochenta", "noventa" };
        string[] centenas = { "", "ciento", "doscientos", "trescientos", "cuatrocientos",
                              "quinientos", "seiscientos", "setecientos", "ochocientos", "novecientos" };

        var sb = new StringBuilder();

        if (n >= 1000000)
        {
            int millones = n / 1000000;
            if (millones == 1)
                sb.Append("un millon ");
            else
            {
                sb.Append(ConvertGroup(millones));
                sb.Append(" millones ");
            }
            n %= 1000000;
        }

        if (n >= 1000)
        {
            int miles = n / 1000;
            if (miles == 1)
                sb.Append("mil ");
            else
            {
                sb.Append(ConvertGroup(miles));
                sb.Append(" mil ");
            }
            n %= 1000;
        }

        if (n >= 100)
        {
            if (n == 100)
            {
                sb.Append("cien");
                return sb.ToString();
            }
            sb.Append(centenas[n / 100]);
            sb.Append(' ');
            n %= 100;
        }

        if (n >= 20)
        {
            sb.Append(decenas[n / 10]);
            int u = n % 10;
            if (u > 0)
            {
                sb.Append(" y ");
                sb.Append(unidades[u]);
            }
        }
        else if (n >= 10)
        {
            sb.Append(especiales[n - 10]);
        }
        else if (n > 0)
        {
            sb.Append(unidades[n]);
        }

        return sb.ToString();
    }
}
