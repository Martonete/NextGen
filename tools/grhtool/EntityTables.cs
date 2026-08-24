using System.Text;

namespace GrhTool;

/// <summary>
/// Writers for the entity index files: Personajes.ind, Cabezas.ind, Cascos.ind.
///
/// Layout: MiCabecera(263) + Version(Int32) + Count(Int32) + fixed records.
///
/// The original VB6 format stored GRH numbers as UInt16, capping the catalogue
/// at 65535 — a real constraint, since 3094 sheets of tiles need more than that
/// to be indexed cell by cell. Nothing outside the client and these tools reads
/// these files (the server never touches them, and no GRH crosses the wire), so
/// the fields were widened to Int32.
///
/// A version marker distinguishes the two: the old format has the entry count
/// at offset 263 as an Int16, the new one writes <see cref="FormatMagic"/>
/// there instead. Readers check for it and fall back to the 16-bit layout.
/// </summary>
public static class EntityTables
{
    /// <summary>
    /// Written where the old format kept its Int16 count. Negative, so an old
    /// reader sees a nonsensical count instead of silently misparsing.
    /// </summary>
    public const int FormatMagic = -32000;

    /// <summary>
    /// Row order inside a body sheet, confirmed by rendering the first frame of
    /// each row: row 0 faces south (chest visible), row 1 north (back), rows 2
    /// and 3 are the west and east profiles.
    /// </summary>
    public const int RowSouth = 0, RowNorth = 1, RowWest = 2, RowEast = 3;

    /// <summary>
    /// Frames per row of a body sheet. The last two rows are one frame shorter -
    /// a uniform 4x6 split does not describe this layout.
    /// </summary>
    public static readonly int[] BodyRowLengths = { 6, 6, 5, 5 };

    /// <summary>
    /// Milliseconds per walk frame, measured across the stock bodies (412 of 472
    /// agreed on this value). Speed is the duration of the whole loop.
    /// </summary>
    public const double MsPerWalkFrame = 55.5;

    /// <summary>
    /// Head sprites in this catalogue are 17x50 but only their top ~15 rows hold
    /// pixels; the rest is transparent padding. Both body and head are anchored
    /// by their base, so the renderer's centring already lifts the head by its
    /// full 50px - far above the shoulders. The offset therefore has to push it
    /// back down, which is why it is positive here and negative in the old art.
    /// </summary>
    /// <summary>Reference body height these offsets were calibrated against.</summary>
    private const int ReferenceBodyHeight = 45;

    /// <summary>
    /// Offset for a reference-height body. Chosen by rendering the sweep
    /// 11/12/13/14/15/16 against the real art: at 11 the head rests on the
    /// shoulders with the neck visible, and from 13 the chin starts sinking
    /// into the chest.
    /// </summary>
    private const int BaseHeadOffset = 11;

    /// <summary>
    /// Head anchor for a body of the given sprite height. Taller bodies push
    /// the shoulders up, so the head follows by the same amount.
    /// </summary>
    public static short HeadOffsetFor(int spriteHeight)
        => (short)(BaseHeadOffset - (spriteHeight - ReferenceBodyHeight));

    public static void WritePersonajes(string path, IReadOnlyList<(int[] dirs, short offX, short offY)> bodies)
        => WriteTable(path, bodies.Count, w =>
        {
            foreach (var (dirs, offX, offY) in bodies)
            {
                // dirs[] is indexed by sheet row; the file wants N, E, S, W.
                w.Write(dirs[RowNorth]);
                w.Write(dirs[RowEast]);
                w.Write(dirs[RowSouth]);
                w.Write(dirs[RowWest]);
                w.Write(offX);
                w.Write(offY);
            }
        });

    /// <summary>Cabezas.ind and Cascos.ind share a format: four Int32 per entry.</summary>
    public static void WriteHeadTable(string path, IReadOnlyList<int[]> entries)
        => WriteTable(path, entries.Count, w =>
        {
            foreach (var dirs in entries)
            {
                w.Write(dirs[RowNorth]);
                w.Write(dirs[RowEast]);
                w.Write(dirs[RowSouth]);
                w.Write(dirs[RowWest]);
            }
        });

    private static void WriteTable(string path, int count, Action<BinaryWriter> writeRecords)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        using var fs = File.Create(path);
        using var w = new BinaryWriter(fs);
        w.Write(SynthHeader());
        w.Write(FormatMagic); // marks the 32-bit layout
        w.Write(count);
        writeRecords(w);
    }

    /// <summary>
    /// The 263-byte MiCabecera. Synthesised rather than copied from an existing
    /// file, so tables can be generated from nothing.
    /// </summary>
    /// <summary>
    /// Writes Armas.dat / Escudos.dat. Unlike the .ind tables these are INI
    /// text, and their Dir1..Dir4 point at animated GRHs - equipment swings
    /// along with the walk cycle rather than holding a static pose.
    /// </summary>
    public static void WriteEquipIni(string path, string countKey, string sectionPrefix,
                                     IReadOnlyList<int[]> entries)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        var sb = new StringBuilder();
        sb.Append("[INIT]\r\n").Append(countKey).Append('=').Append(entries.Count).Append("\r\n\r\n");
        for (int i = 0; i < entries.Count; i++)
        {
            var d = entries[i];
            sb.Append('[').Append(sectionPrefix).Append(i + 1).Append("]\r\n");
            sb.Append("Dir1=").Append(d[RowNorth]).Append("\r\n");
            sb.Append("Dir2=").Append(d[RowEast]).Append("\r\n");
            sb.Append("Dir3=").Append(d[RowSouth]).Append("\r\n");
            sb.Append("Dir4=").Append(d[RowWest]).Append("\r\n\r\n");
        }
        File.WriteAllText(path, sb.ToString(), Encoding.ASCII);
    }

    private static byte[] SynthHeader(string description = "Argentum Nextgen")
    {
        var buf = new byte[263];
        Encoding.ASCII.GetBytes(description.PadRight(255)[..255]).CopyTo(buf, 0);
        BitConverter.GetBytes(0x47).CopyTo(buf, 255);
        BitConverter.GetBytes(5).CopyTo(buf, 259);
        return buf;
    }
}
