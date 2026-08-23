using System.Drawing;

namespace GrhTool;

/// <summary>
/// Locates sprite regions inside a sheet by scanning for gaps of transparent
/// pixels. Rendered art is not laid out on an exact pixel grid, so a fixed
/// stride cannot be assumed - bands are found, not calculated.
/// </summary>
public static class SheetScanner
{
    /// <summary>Alpha at or below this counts as empty.</summary>
    private const int AlphaThreshold = 8;

    /// <summary>
    /// These sheets carry no alpha channel at all - they use pure black as the
    /// transparency key, the original AO convention. Scanning by alpha alone
    /// sees one solid block and returns a single region for a 22-frame body.
    /// </summary>
    private const int BlackThreshold = 8;

    public sealed record Region(int X, int Y, int W, int H);

    /// <summary>
    /// Splits a sheet into a fixed grid of cells, keeping only those that hold
    /// pixels. Calibration against the indexed catalogue showed band scanning
    /// collapses tiled terrain into one region - adjacent tiles leave no empty
    /// gap to cut on - so grid-based sheets must be divided arithmetically.
    /// </summary>
    public static List<Region> Grid(string pngPath, int cell = 32)
    {
        using var bmp = new Bitmap(pngPath);
        var mask = BuildMask(bmp);
        var regions = new List<Region>();
        for (int y = 0; y + cell <= bmp.Height; y += cell)
        {
            for (int x = 0; x + cell <= bmp.Width; x += cell)
            {
                if (CellHasPixels(mask, bmp.Width, x, y, cell))
                    regions.Add(new Region(x, y, cell, cell));
            }
        }
        return regions;
    }

    /// <summary>
    /// Splits into a grid of rows x cols covering the whole sheet. Used for
    /// directional strips (bodies, heads, NPCs) whose frames are evenly spaced
    /// but may touch, which defeats gap detection.
    /// </summary>
    public static List<Region> UniformGrid(string pngPath, int rows, int cols)
    {
        using var bmp = new Bitmap(pngPath);
        int w = bmp.Width / cols, h = bmp.Height / rows;
        var regions = new List<Region>();
        for (int r = 0; r < rows; r++)
            for (int c = 0; c < cols; c++)
                regions.Add(new Region(c * w, r * h, w, h));
        return regions;
    }

    /// <summary>
    /// The player-body layout: a 150x180 sheet of 25x45 cells laid out 6/6/5/5,
    /// one row per facing. Rows are ragged, so a uniform 4x6 split does not
    /// reproduce it - the last two rows hold five frames, not six.
    /// </summary>
    public static readonly int[] BodyRowLengths = { 6, 6, 5, 5 };

    public static List<Region> BodyGrid(int cellW = 25, int cellH = 45)
    {
        var regions = new List<Region>();
        for (int row = 0; row < BodyRowLengths.Length; row++)
            for (int col = 0; col < BodyRowLengths[row]; col++)
                regions.Add(new Region(col * cellW, row * cellH, cellW, cellH));
        return regions;
    }

    private static bool CellHasPixels(bool[] mask, int width, int x0, int y0, int cell)
    {
        for (int y = y0; y < y0 + cell; y++)
            for (int x = x0; x < x0 + cell; x++)
                if (mask[y * width + x]) return true;
        return false;
    }

    /// <summary>
    /// Regions found row-major: horizontal bands first, then columns inside each
    /// band. A uniform grid yields rows x cols regions in reading order.
    /// </summary>
    public static List<Region> Scan(string pngPath)
    {
        using var bmp = new Bitmap(pngPath);
        var mask = BuildMask(bmp);
        var regions = new List<Region>();

        foreach (var (y0, y1) in Bands(y => RowHasPixels(mask, bmp.Width, y), bmp.Height))
        {
            foreach (var (x0, x1) in Bands(x => ColHasPixels(mask, bmp.Width, x, y0, y1), bmp.Width))
            {
                // Tighten vertically: a row band spans the tallest sprite in it,
                // so a shorter neighbour would otherwise carry blank padding.
                int top = y0, bottom = y1;
                while (top < bottom && !SpanHasPixels(mask, bmp.Width, x0, x1, top)) top++;
                while (bottom > top && !SpanHasPixels(mask, bmp.Width, x0, x1, bottom)) bottom--;
                regions.Add(new Region(x0, top, x1 - x0 + 1, bottom - top + 1));
            }
        }
        return regions;
    }

    private static bool[] BuildMask(Bitmap bmp)
    {
        var mask = new bool[bmp.Width * bmp.Height];
        for (int y = 0; y < bmp.Height; y++)
        {
            for (int x = 0; x < bmp.Width; x++)
            {
                var c = bmp.GetPixel(x, y);
                bool empty = c.A <= AlphaThreshold
                          || (c.R <= BlackThreshold && c.G <= BlackThreshold && c.B <= BlackThreshold);
                mask[y * bmp.Width + x] = !empty;
            }
        }
        return mask;
    }

    private static bool RowHasPixels(bool[] mask, int width, int y)
    {
        int off = y * width;
        for (int x = 0; x < width; x++) if (mask[off + x]) return true;
        return false;
    }

    private static bool ColHasPixels(bool[] mask, int width, int x, int y0, int y1)
    {
        for (int y = y0; y <= y1; y++) if (mask[y * width + x]) return true;
        return false;
    }

    private static bool SpanHasPixels(bool[] mask, int width, int x0, int x1, int y)
    {
        int off = y * width;
        for (int x = x0; x <= x1; x++) if (mask[off + x]) return true;
        return false;
    }

    /// <summary>Contiguous runs where <paramref name="occupied"/> holds.</summary>
    private static List<(int, int)> Bands(Func<int, bool> occupied, int length)
    {
        var bands = new List<(int, int)>();
        bool inBand = false;
        int start = 0;
        for (int i = 0; i < length; i++)
        {
            bool has = occupied(i);
            if (has && !inBand) { inBand = true; start = i; }
            else if (!has && inBand) { inBand = false; bands.Add((start, i - 1)); }
        }
        if (inBand) bands.Add((start, length - 1));
        return bands;
    }
}
