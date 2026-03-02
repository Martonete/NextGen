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
    /// <summary>
    /// Faithful port of VB6 AoDefenderConverter.cls Numero2Letra.
    /// Must match server's numero2letra() EXACTLY or cipher desyncs.
    /// Called as: Numero2Letra(counter, , 2, "ZiPPy", "NoPPy", 1, 0)
    /// SexoMoneda=Masculino(sexo1="", sexo2="os")
    /// </summary>
    public static string Numero2Letra(int n)
    {
        if (n == 0) return "cero ZiPPyes";

        string words = UnNumero(n.ToString());
        string moneda = (n == 1) ? " ZiPPy" : " ZiPPyes";
        return words + moneda;
    }

    /// <summary>
    /// Port of VB6 UnNumero — group-based digit processing.
    /// Matches server's un_numero() exactly.
    /// </summary>
    private static string UnNumero(string strNum)
    {
        // SexoMoneda = Masculino → sexo1="", sexo2="os"
        string sexo1 = "";
        string sexo2 = "os";

        string[] unidad = { "", "un", "dos", "tres", "cuatro", "cinco",
                            "seis", "siete", "ocho", "nueve" };
        string[] decena = { "", "diez", "veinte", "treinta", "cuarenta",
                            "cincuenta", "sesenta", "setenta", "ochenta", "noventa" };
        string[] centena = { "", "ciento", "doscient" + sexo2, "trescient" + sexo2,
                             "cuatrocient" + sexo2, "quinient" + sexo2, "seiscient" + sexo2,
                             "setecient" + sexo2, "ochocient" + sexo2, "novecient" + sexo2, "cien" };
        string[] deci = { "", "dieci", "veinti", "treinta y ", "cuarenta y ",
                          "cincuenta y ", "sesenta y ", "setenta y ", "ochenta y ", "noventa y " };
        string[] otros = { "", "1", "2", "3", "4", "5", "6", "7", "8", "9",
                           "10", "once", "doce", "trece", "catorce", "quince" };

        long dblNumero = long.TryParse(strNum, out long v) ? Math.Abs(v) : 0;
        if (dblNumero < 1) return "cero";

        bool millon = dblNumero > 999999;
        bool millones = dblNumero > 1999999;

        // Pad to 12 digits, split into groups of 3 (right to left)
        string padded = dblNumero.ToString("D12");
        string[] strN = new string[4];
        for (int i = 0; i < 4; i++)
            strN[i] = padded.Substring(12 - (i + 1) * 3, 3);
        // strN[0] = ones group, strN[1] = thousands, strN[2] = millions, strN[3] = billions

        // Find maxVez (highest non-"000" group)
        int maxVez = 4;
        for (int k = 3; k >= 0; k--)
        {
            if (strN[k] == "000")
                maxVez--;
            else
                break;
        }

        string strB = "";

        for (int vez = 0; vez < maxVez; vez++)
        {
            string s = strN[vez];
            string strU = "", strD = "", strC = "";

            int lastTwo = int.Parse(s.Substring(1, 2));
            char lastOneCh = s[2];

            if (lastOneCh == '0')
            {
                // Units digit is 0 → use decena
                int k = lastTwo / 10;
                strD = k < decena.Length ? decena[k] : "";
            }
            else if (lastTwo > 10 && lastTwo < 16)
            {
                // 11-15 → use otros
                strD = lastTwo < otros.Length ? otros[lastTwo] : "";
            }
            else
            {
                // Normal: unit + deci prefix
                int unitIdx = lastOneCh - '0';
                strU = unitIdx < unidad.Length ? unidad[unitIdx] : "";
                int tensDigit = s[1] - '0';
                strD = tensDigit < deci.Length ? deci[tensDigit] : "";
            }

            // Hundreds
            int hundredsDigit = s[0] - '0';
            if (hundredsDigit > 0)
            {
                int k = hundredsDigit;
                // Parche: if hundreds=1 and group is exactly 100, use centena[10]="cien"
                if (k == 1)
                {
                    int groupVal = int.Parse(s);
                    if (groupVal == 100) k = 10;
                }
                strC = (k < centena.Length ? centena[k] : "") + " ";
            }

            // VB6: If strU = "uno" And Left$(strB, 4) = " mil" Then strU = ""
            if (strU == "uno" && strB.StartsWith(" mil"))
                strU = "";

            strB = strC + strD + strU + " " + strB;

            // Add " mil " between groups
            if (vez == 0 || vez == 2)
            {
                if (vez + 1 < strN.Length && strN[vez + 1] != "000")
                    strB = " mil " + strB;
            }
            if (vez == 1 && millon)
            {
                if (millones)
                    strB = " millones " + strB;
                else
                    strB = "un millon " + strB;
            }
        }

        // Trim and clean up double spaces
        strB = strB.Trim();
        while (strB.Contains("  "))
            strB = strB.Replace("  ", " ");

        // VB6: If Right$(strB, 3) = "uno" Then replace last "o" with sexo1
        if (strB.EndsWith("uno"))
            strB = strB.Substring(0, strB.Length - 1) + sexo1;

        // VB6: prefix corrections
        string prefix1 = "un" + sexo1 + " un";
        if (strB.StartsWith(prefix1))
            strB = strB.Substring(3 + sexo1.Length);
        if (strB.StartsWith("un un"))
            strB = strB.Substring(3);

        string prefix2 = "un" + sexo1 + " mil ";
        if (strB.StartsWith(prefix2))
            strB = strB.Substring(3 + sexo1.Length);
        string exactMil = "un" + sexo1 + " mil";
        if (strB == exactMil)
            strB = strB.Substring(3 + sexo1.Length);
        if (strB.StartsWith("un mil "))
            strB = strB.Substring(3);

        // VB6: trailing "ciento" → "cien"
        if (strB.EndsWith("ciento"))
            strB = strB.Substring(0, strB.Length - 2);

        return strB.Trim();
    }
}
