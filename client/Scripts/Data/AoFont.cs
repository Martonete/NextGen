using System;
using System.IO;
using Godot;

namespace TierrasSagradasAO.Data;

/// <summary>
/// VB6-exact bitmap font renderer. Loads fontX.dat (header + char widths)
/// and fontX.png (glyph spritesheet). Renders text character-by-character
/// using DrawTextureRectRegion for pixel-perfect match with the VB6 client.
///
/// DAT format (273 bytes):
///   [0..3]   uint32 LE  BitmapWidth  (256)
///   [4..7]   uint32 LE  BitmapHeight (256)
///   [8..11]  uint32 LE  CellWidth    (16, 32, or 20)
///   [12..15] uint32 LE  CellHeight   (16, 32, or 20)
///   [16]     byte       BaseCharOffset
///   [17..272] byte[256] CharWidth per ASCII code
///
/// VB6: CharHeight = CellHeight - 4 (used for line spacing)
/// VB6: color key = 0xFF000000 (black pixels = transparent)
/// </summary>
public class AoFont
{
    public int BitmapWidth;
    public int BitmapHeight;
    public int CellWidth;
    public int CellHeight;
    public int CharHeight; // CellHeight - 4 (VB6 line height)
    public byte BaseCharOffset;
    public byte[] CharWidths = new byte[256];

    public int RowPitch;    // chars per row = BitmapWidth / CellWidth
    public float ColFactor; // CellWidth / BitmapWidth
    public float RowFactor; // CellHeight / BitmapHeight

    public Texture2D? Texture;

    /// <summary>
    /// Load a font from .dat + .png files.
    /// VB6: Engine_Init_FontSettings + Engine_Init_FontTextures
    /// </summary>
    public static AoFont? Load(string datPath, string pngPath)
    {
        if (!File.Exists(datPath))
        {
            GD.PrintErr($"[FONT] DAT not found: {datPath}");
            return null;
        }
        if (!File.Exists(pngPath))
        {
            GD.PrintErr($"[FONT] PNG not found: {pngPath}");
            return null;
        }

        var font = new AoFont();
        byte[] data = File.ReadAllBytes(datPath);
        if (data.Length < 273)
        {
            GD.PrintErr($"[FONT] DAT too small: {data.Length} bytes (expected 273)");
            return null;
        }

        font.BitmapWidth = BitConverter.ToInt32(data, 0);
        font.BitmapHeight = BitConverter.ToInt32(data, 4);
        font.CellWidth = BitConverter.ToInt32(data, 8);
        font.CellHeight = BitConverter.ToInt32(data, 12);
        font.BaseCharOffset = data[16];
        Array.Copy(data, 17, font.CharWidths, 0, 256);

        // VB6: CharHeight = CellHeight - 4
        font.CharHeight = font.CellHeight - 4;
        font.RowPitch = font.BitmapWidth / font.CellWidth;
        font.ColFactor = (float)font.CellWidth / font.BitmapWidth;
        font.RowFactor = (float)font.CellHeight / font.BitmapHeight;

        // Load image texture with color key (black = transparent)
        // font files may be BMP with .png extension — try LoadFromFile first,
        // then fall back to loading raw bytes as BMP if PNG parse fails.
        var image = Image.LoadFromFile(pngPath);
        if (image == null)
        {
            // Godot's LoadFromFile detects format by magic bytes and may fail
            // for BMP files with .png extension. Try loading as BMP explicitly.
            var rawBytes = File.ReadAllBytes(pngPath);
            image = new Image();
            var err = image.LoadBmpFromBuffer(rawBytes);
            if (err != Error.Ok)
            {
                // Also try as actual PNG buffer in case LoadFromFile had a path issue
                err = image.LoadPngFromBuffer(rawBytes);
            }
            if (err != Error.Ok)
            {
                GD.PrintErr($"[FONT] Failed to load image: {pngPath}");
                return null;
            }
        }

        // VB6 uses D3DX_FILTER_POINT and color key 0xFF000000 (opaque black = transparent)
        ApplyColorKey(image);

        font.Texture = ImageTexture.CreateFromImage(image);
        GD.Print($"[FONT] Loaded: {datPath} ({font.CellWidth}x{font.CellHeight}, base={font.BaseCharOffset})");
        return font;
    }

    /// <summary>
    /// Apply VB6 color key: pure black (0,0,0) → transparent.
    /// All other pixels remain unchanged (greyscale glyphs + dark borders).
    ///
    /// VB6 renders text by multiplying texture_color × vertex_color:
    ///   bright glyph (224,224,224) × text_color → colored text (slightly dimmed)
    ///   dark border (4,4,4) × text_color → near-black outline (the classic AO look)
    ///   black bg (0,0,0) → invisible (color key)
    ///
    /// Godot's DrawTextureRectRegion modulate does the exact same multiplication,
    /// so keeping original pixel values gives pixel-perfect VB6 matching.
    /// </summary>
    private static void ApplyColorKey(Image image)
    {
        int w = image.GetWidth();
        int h = image.GetHeight();
        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                Color c = image.GetPixel(x, y);
                // VB6 color key 0xFF000000: only exact black becomes transparent
                if (c.R < 0.004f && c.G < 0.004f && c.B < 0.004f)
                {
                    image.SetPixel(x, y, new Color(0, 0, 0, 0));
                }
            }
        }
    }

    /// <summary>
    /// Get the pixel width of a text string.
    /// VB6: Engine_GetTextWidth — sums CharWidth for each character.
    /// </summary>
    public int GetTextWidth(string text)
    {
        int width = 0;
        for (int i = 0; i < text.Length; i++)
        {
            int ascii = (int)text[i];
            if (ascii >= 0 && ascii < 256)
                width += CharWidths[ascii];
        }
        return width;
    }

    /// <summary>
    /// Draw text at the given position. Y = top of text (VB6 convention).
    /// VB6: Engine_Render_Text — draws each character as a textured quad.
    ///
    /// In Godot we use DrawTextureRectRegion per character, with modulate
    /// for color tinting. The font texture has white glyphs on transparent
    /// background (after color key processing), so modulate = desired color.
    /// </summary>
    public void DrawText(CanvasItem canvas, int x, int y, string text,
                         Color color, bool center = false)
    {
        if (Texture == null || string.IsNullOrEmpty(text)) return;

        // VB6: if Center, X -= textWidth * 0.5
        if (center)
            x -= GetTextWidth(text) / 2;

        int curX = x;
        for (int i = 0; i < text.Length; i++)
        {
            int ascii = (int)text[i];
            if (ascii < 0 || ascii >= 256) continue;

            // Calculate source rect in spritesheet
            // VB6: Row = (ascii - BaseCharOffset) \ RowPitch
            int charIdx = ascii - BaseCharOffset;
            if (charIdx < 0) charIdx = 0;
            int row = charIdx / RowPitch;
            int col = charIdx - (row * RowPitch);

            float srcX = col * CellWidth;
            float srcY = row * CellHeight;

            var srcRect = new Rect2(srcX, srcY, CellWidth, CellHeight);
            var destRect = new Rect2(curX, y, CellWidth, CellHeight);

            canvas.DrawTextureRectRegion(Texture, destRect, srcRect, color);

            // VB6: advance by character width (variable-width font)
            curX += CharWidths[ascii];
        }
    }
}
