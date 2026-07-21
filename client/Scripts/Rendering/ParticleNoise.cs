using System;

namespace ArgentumNextgen.Rendering;

/// <summary>
/// Simple 3D value noise for particle effects.
/// Returns [0, 1]. Used for coherent beam wobble and mote drift.
/// </summary>
internal static class ParticleNoise
{
    // Permutation table (512 entries for wrapping)
    private static readonly byte[] Perm = BuildPerm();

    private static byte[] BuildPerm()
    {
        byte[] p = new byte[256];
        for (int i = 0; i < 256; i++) p[i] = (byte)i;
        // Fixed shuffle — deterministic, same every run
        var rng = new Random(0x1337C0DE);
        for (int i = 255; i > 0; i--)
        {
            int j = rng.Next(i + 1);
            (p[i], p[j]) = (p[j], p[i]);
        }
        byte[] perm = new byte[512];
        for (int i = 0; i < 512; i++) perm[i] = p[i & 255];
        return perm;
    }

    private static float Fade(float t) => t * t * t * (t * (t * 6f - 15f) + 10f);

    private static float Lerp(float a, float b, float t) => a + t * (b - a);

    private static float Grad(int hash, float x, float y, float z)
    {
        int h = hash & 15;
        float u = h < 8 ? x : y;
        float v = h < 4 ? y : (h == 12 || h == 14 ? x : z);
        return ((h & 1) == 0 ? u : -u) + ((h & 2) == 0 ? v : -v);
    }

    /// <summary>
    /// Sample 3D Perlin-style value noise at (x, y, z). Returns [0, 1].
    /// </summary>
    public static float Sample(float x, float y, float z)
    {
        int xi = (int)Math.Floor(x) & 255;
        int yi = (int)Math.Floor(y) & 255;
        int zi = (int)Math.Floor(z) & 255;
        float xf = x - MathF.Floor(x);
        float yf = y - MathF.Floor(y);
        float zf = z - MathF.Floor(z);
        float u = Fade(xf), v = Fade(yf), w = Fade(zf);

        int a  = Perm[xi]     + yi;
        int aa = Perm[a]      + zi;
        int ab = Perm[a + 1]  + zi;
        int b  = Perm[xi + 1] + yi;
        int ba = Perm[b]      + zi;
        int bb = Perm[b + 1]  + zi;

        float res = Lerp(
            Lerp(Lerp(Grad(Perm[aa],     xf,       yf,       zf),
                      Grad(Perm[ba],     xf - 1f,  yf,       zf), u),
                 Lerp(Grad(Perm[ab],     xf,       yf - 1f,  zf),
                      Grad(Perm[bb],     xf - 1f,  yf - 1f,  zf), u), v),
            Lerp(Lerp(Grad(Perm[aa + 1], xf,       yf,       zf - 1f),
                      Grad(Perm[ba + 1], xf - 1f,  yf,       zf - 1f), u),
                 Lerp(Grad(Perm[ab + 1], xf,       yf - 1f,  zf - 1f),
                      Grad(Perm[bb + 1], xf - 1f,  yf - 1f,  zf - 1f), u), v), w);

        return (res + 1f) * 0.5f; // remap [-1,1] → [0,1]
    }
}
