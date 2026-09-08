using System;

namespace ArgentumNextgen.Rendering;

/// <summary>Independent roof transitions, including rapid re-entry and map changes.</summary>
public sealed class RoofFadeState
{
    private float[] _opacity = Array.Empty<float>();
    public void Reset(int regionCount)
    {
        _opacity = new float[regionCount + 1];
        Array.Fill(_opacity, 1f);
    }

    public float GetOpacity(int region) => region > 0 && region < _opacity.Length ? _opacity[region] : 1f;

    public void Update(int activeRegion, float seconds)
    {
        float step = Math.Max(0f, seconds) * (400f / 255f);
        for (int region = 1; region < _opacity.Length; region++)
        {
            float target = region == activeRegion ? 45f / 255f : 1f;
            _opacity[region] += Math.Clamp(target - _opacity[region], -step, step);
        }
    }
}
