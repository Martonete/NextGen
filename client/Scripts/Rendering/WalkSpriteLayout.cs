using System;
using System.Numerics;

namespace ArgentumNextgen.Rendering;

/// <summary>Registers tightly cropped frames back onto their original horizontal cells.</summary>
public static class WalkSpriteLayout
{
    public readonly record struct Crop(int File, int X, int Y, int Width, int Height);

    public static Vector2[]? Register(Crop[] frames)
    {
        if (frames.Length < 4) return null;
        var first = frames[0];
        bool variable = false;
        var strides = new float[frames.Length - 1];
        for (int i = 1; i < frames.Length; i++)
        {
            var f = frames[i];
            // Only a single, ordered strip; legacy packed/multi-row animations
            // retain their authored centering rather than guessing a layout.
            if (f.File != first.File || f.X <= frames[i - 1].X || Math.Abs(f.Y - first.Y) > 3)
                return null;
            variable |= f.Width != first.Width || f.Height != first.Height;
            strides[i - 1] = f.X + f.Width * .5f - frames[i - 1].X - frames[i - 1].Width * .5f;
        }
        if (!variable) return null;
        Array.Sort(strides);
        int pitch = (int)Math.Round(strides[strides.Length / 2] / 8f) * 8;
        if (pitch < 32 || pitch > 256) return null;
        int origin = first.X / pitch * pitch;
        var offsets = new Vector2[frames.Length];
        for (int i = 0; i < frames.Length; i++)
        {
            var f = frames[i];
            int cell = origin + i * pitch;
            // Cloth may extend a few pixels beyond its nominal source cell.
            if (f.Width <= 0 || f.Height <= 0 || f.X < cell - 3 || f.X + f.Width > cell + pitch + 3
                || Math.Abs(f.Y + f.Height - first.Y - first.Height) > 3)
                return null;
            // The head is anchored to the cell center, not the crop center.
            // Restore that absolute anchor as well as inter-frame continuity.
            offsets[i] = new Vector2(f.X - cell + f.Width / 2 - pitch / 2,
                f.Y - first.Y + f.Height - first.Height);
        }
        return offsets;
    }

    public static int Frame(float frame, int sourceCount, int targetCount)
    {
        if (sourceCount <= 1 || targetCount <= 1) return 0;
        float phase = (frame % sourceCount + sourceCount) % sourceCount / sourceCount;
        return Math.Min(targetCount - 1, (int)(phase * targetCount));
    }
}
