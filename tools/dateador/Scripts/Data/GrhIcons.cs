#nullable enable
using System;
using Godot;

namespace AODateador.Data;

/// <summary>
/// Turns a GRH index into a drawable texture cropped to that sprite's region.
///
/// The same few lines are duplicated in three places across the repo
/// (ObjectPalette, SheetPalette, ItemSearchPanel); this is the one copy the
/// dateador uses.
/// </summary>
public static class GrhIcons
{
    /// <summary>
    /// Texture for one GRH, or null when the index, sheet or region is unusable.
    /// Animated GRHs resolve to their first frame — a still icon, not a loop.
    /// </summary>
    public static Texture2D? Get(GrhData[]? grhs, TextureManager? textures, int grhIndex)
    {
        if (grhs == null || textures == null) return null;
        if (grhIndex <= 0 || grhIndex >= grhs.Length) return null;

        var grh = grhs[grhIndex];
        if (grh.NumFrames > 1 && grh.Frames is { Length: > 0 })
        {
            int first = grh.Frames[0];
            if (first > 0 && first < grhs.Length) grh = grhs[first];
        }
        if (grh.FileNum <= 0) return null;

        var sheet = textures.GetTexture(grh.FileNum);
        if (sheet == null) return null;
        if (!TryGetSafeRegion(grh, sheet, out var region)) return null;

        return new AtlasTexture { Atlas = sheet, Region = region };
    }

    /// <summary>
    /// Clamps a GRH's region to the sheet it points at. Indexes can outlive the
    /// art they were cut from, and an out-of-bounds region crashes the draw.
    /// </summary>
    public static bool TryGetSafeRegion(GrhData grh, Texture2D texture, out Rect2 region)
    {
        region = default;
        if (grh.SX < 0 || grh.SY < 0 || grh.PixelWidth <= 0 || grh.PixelHeight <= 0)
            return false;

        int width = Math.Min(grh.PixelWidth, texture.GetWidth() - grh.SX);
        int height = Math.Min(grh.PixelHeight, texture.GetHeight() - grh.SY);
        if (width <= 0 || height <= 0) return false;

        region = new Rect2(grh.SX, grh.SY, width, height);
        return true;
    }
}
