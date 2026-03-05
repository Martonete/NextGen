namespace AOWorldEditor.Data;

/// <summary>
/// Graphics resource entry from Graficos.ind.
/// Static GRHs have NumFrames=1 with direct texture coords.
/// Animations have NumFrames>1 with frame indices and speed.
/// </summary>
public class GrhData
{
    public short NumFrames;
    public int FileNum;     // PNG file number (e.g. 1 = "1.png")
    public short SX;        // Source X in texture atlas
    public short SY;        // Source Y in texture atlas
    public short PixelWidth;
    public short PixelHeight;
    public float TileWidth;  // PixelWidth / 32
    public float TileHeight; // PixelHeight / 32
    public int[]? Frames;    // Frame GRH indices (self-reference for static)
    public float Speed;      // Animation speed (only for NumFrames > 1)

    public bool IsAnimation => NumFrames > 1;
    public bool IsValid => NumFrames > 0 && (FileNum > 0 || (Frames != null && Frames.Length > 0));

    /// <summary>
    /// True if this GRH is a 1x1 tile (32x32 pixels) — typical terrain tile.
    /// </summary>
    public bool IsSingleTile => PixelWidth == 32 && PixelHeight == 32;

    /// <summary>
    /// True if this GRH fits as a ground tile (1x1 or 2x2 = 64x64).
    /// </summary>
    public bool IsGroundTile => (PixelWidth == 32 && PixelHeight == 32) ||
                                 (PixelWidth == 64 && PixelHeight == 64);
}
