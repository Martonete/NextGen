using System.Text;
using System.Text.Json.Serialization;

namespace GrhTool;

/// <summary>One entry of Graficos.ind. Static and animated share the record header.</summary>
public sealed class GrhEntry
{
    public int Index;
    public short NumFrames;

    // Static-only fields (NumFrames == 1).
    public int FileNum;
    public short SX, SY, Width, Height;

    // Animated-only fields (NumFrames > 1).
    public int[] Frames = Array.Empty<int>();
    public float Speed;

    [JsonIgnore] public bool IsAnimated => NumFrames > 1;

    /// <summary>Byte length of this record on disk.</summary>
    [JsonIgnore] public int RecordSize => NumFrames > 1 ? 10 + 4 * NumFrames : 18;
}

/// <summary>
/// Reader/writer for Graficos.ind.
///
/// Layout, verified byte-for-byte against the shipped file:
///   header    Version i32 @0, Count i32 @4        (this file carries no MiCabecera)
///   static    grh i32, nf i16 (=1), file i32, sx i16, sy i16, w i16, h i16   -> 18 bytes
///   animated  grh i32, nf i16 (>1), frames i32*nf, speed f32                 -> 10+4nf bytes
///
/// Entries are written back in their original order so a dump/build round-trip
/// reproduces the source file exactly; that equality is the contract that lets
/// every later phase trust the writer.
/// </summary>
public sealed class GrhIndex
{
    public const int MiCabeceraSize = 263; // 255 desc + 4 crc + 4 magic

    public int Version;
    /// <summary>The Count field as stored. Only a hint for array sizing, never a validity gate.</summary>
    public int Count;
    public List<GrhEntry> Entries = new();

    /// <summary>Bytes after the last parsed record, if any. Preserved so round-trips stay exact.</summary>
    public byte[] Trailer = Array.Empty<byte>();

    public static GrhIndex Load(string path)
    {
        byte[] data = File.ReadAllBytes(path);
        var idx = new GrhIndex();

        // Locate the header by testing whether a record parses after it, rather
        // than by judging how large Count is.
        int start = -1;
        foreach (int headerOffset in new[] { 0, MiCabeceraSize, 1 })
        {
            if (headerOffset + 8 > data.Length) continue;
            int version = BitConverter.ToInt32(data, headerOffset);
            int count = BitConverter.ToInt32(data, headerOffset + 4);
            if (count > 0 && LooksLikeEntry(data, headerOffset + 8))
            {
                idx.Version = version;
                idx.Count = count;
                start = headerOffset + 8;
                break;
            }
        }
        if (start < 0) throw new InvalidDataException($"Cabecera irreconocible en {path}");

        int pos = start;
        while (pos + 6 <= data.Length)
        {
            int grh = BitConverter.ToInt32(data, pos);
            short nf = BitConverter.ToInt16(data, pos + 4);
            if (grh <= 0 || nf < 1) break;

            int size = nf > 1 ? 10 + 4 * nf : 18;
            if (pos + size > data.Length) break;

            var e = new GrhEntry { Index = grh, NumFrames = nf };
            if (nf > 1)
            {
                e.Frames = new int[nf];
                for (int f = 0; f < nf; f++) e.Frames[f] = BitConverter.ToInt32(data, pos + 6 + 4 * f);
                e.Speed = BitConverter.ToSingle(data, pos + 6 + 4 * nf);
            }
            else
            {
                e.FileNum = BitConverter.ToInt32(data, pos + 6);
                e.SX = BitConverter.ToInt16(data, pos + 10);
                e.SY = BitConverter.ToInt16(data, pos + 12);
                e.Width = BitConverter.ToInt16(data, pos + 14);
                e.Height = BitConverter.ToInt16(data, pos + 16);
            }
            idx.Entries.Add(e);
            pos += size;
        }

        if (pos < data.Length)
            idx.Trailer = data[pos..];

        return idx;
    }

    public void Save(string path)
    {
        // Write to a temporary file and swap it in. Overwriting in place can
        // fail on Windows while the original is still memory-mapped by a reader.
        string tmp = path + ".tmp";
        SaveTo(tmp);
        File.Move(tmp, path, overwrite: true);
    }

    private void SaveTo(string path)
    {
        using var fs = File.Create(path);
        using var w = new BinaryWriter(fs);
        w.Write(Version);
        w.Write(Count);
        foreach (var e in Entries)
        {
            w.Write(e.Index);
            w.Write(e.NumFrames);
            if (e.IsAnimated)
            {
                foreach (int f in e.Frames) w.Write(f);
                w.Write(e.Speed);
            }
            else
            {
                w.Write(e.FileNum);
                w.Write(e.SX); w.Write(e.SY); w.Write(e.Width); w.Write(e.Height);
            }
        }
        if (Trailer.Length > 0) w.Write(Trailer);
    }

    private static bool LooksLikeEntry(byte[] data, int offset)
    {
        if (offset + 6 > data.Length) return false;
        int grh = BitConverter.ToInt32(data, offset);
        short nf = BitConverter.ToInt16(data, offset + 4);
        if (grh <= 0 || nf < 1) return false;
        int size = nf > 1 ? 10 + 4 * nf : 18;
        return offset + size <= data.Length;
    }

    /// <summary>
    /// The 263-byte MiCabecera used by Personajes/Cabezas/Cascos/Fxs. Synthesised
    /// rather than copied so tables can be generated without an existing file to
    /// borrow a header from.
    /// </summary>
    public static byte[] SynthMiCabecera(string description = "Argentum Nextgen")
    {
        var buf = new byte[MiCabeceraSize];
        var desc = Encoding.ASCII.GetBytes(description.PadRight(255).Substring(0, 255));
        Array.Copy(desc, buf, 255);
        BitConverter.GetBytes(0x47).CopyTo(buf, 255); // crc
        BitConverter.GetBytes(5).CopyTo(buf, 259);    // magic
        return buf;
    }
}
