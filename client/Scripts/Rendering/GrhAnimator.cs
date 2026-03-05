using System;
using System.Collections.Generic;
using TierrasSagradasAO.Data;

namespace TierrasSagradasAO.Rendering;

/// <summary>
/// Manages animation frames for GRH indices.
///
/// Tile animations (water, etc.): Use a global clock so ALL animations
/// with the same Speed stay perfectly synchronized — no per-GRH state needed.
/// VB6: each tile has its own FrameCounter initialized to 1 at map load,
/// all advance at the same rate → visually identical to a global clock.
///
/// One-shot FX animations: Use per-GRH accumulated counters (started on demand).
///
/// VB6 formula: FrameCounter += elapsedMs * NumFrames / Speed
/// </summary>
public class GrhAnimator
{
    // Global clock in milliseconds, reset on map load.
    // Used for looping tile animations — guarantees perfect sync.
    private double _globalTimeMs;

    /// <summary>Global clock in milliseconds (read-only). Used by water UV scrolling.</summary>
    public double GlobalTimeMs => _globalTimeMs;

    // Per-GRH state only for one-shot (non-looping) animations like FX.
    private readonly Dictionary<int, AnimState> _fxStates = new();

    public struct AnimState
    {
        public float FrameCounter;
        public bool Loop;
    }

    /// <summary>
    /// Start a one-shot (FX) animation. Looping tile animations don't need this.
    /// </summary>
    public void StartAnim(int grhIndex, bool loop = true)
    {
        if (!loop && !_fxStates.ContainsKey(grhIndex))
        {
            _fxStates[grhIndex] = new AnimState { FrameCounter = 0, Loop = false };
        }
        // Looping anims use the global clock — no per-GRH state needed.
    }

    /// <summary>
    /// Advance the global clock and any one-shot FX animations.
    /// </summary>
    public void Update(float delta, GameData data)
    {
        float deltaMs = delta * 1000f;
        _globalTimeMs += deltaMs;

        // Advance one-shot FX animations
        if (_fxStates.Count > 0)
        {
            var keys = new List<int>(_fxStates.Keys);
            foreach (int key in keys)
            {
                if (key <= 0 || key >= data.Grhs.Length) continue;
                var grh = data.Grhs[key];
                if (grh.NumFrames <= 1) continue;

                var state = _fxStates[key];
                float speed = grh.Speed > 0 ? grh.Speed : 100f;
                state.FrameCounter += deltaMs * grh.NumFrames / speed;

                if (state.FrameCounter >= grh.NumFrames)
                {
                    // One-shot: stop at last frame
                    state.FrameCounter = grh.NumFrames - 1;
                }
                _fxStates[key] = state;
            }
        }
    }

    /// <summary>
    /// Get current frame for an animated GRH.
    /// Looping animations use the global clock for perfect sync.
    /// One-shot FX animations use their own accumulated counter.
    /// </summary>
    public int GetCurrentFrame(int grhIndex, GameData? data = null)
    {
        // Check one-shot FX first
        if (_fxStates.TryGetValue(grhIndex, out var state))
            return (int)state.FrameCounter;

        // Looping tile animation: compute frame from global clock
        if (data != null && grhIndex > 0 && grhIndex < data.Grhs.Length)
        {
            var grh = data.Grhs[grhIndex];
            if (grh.NumFrames > 1)
            {
                float speed = grh.Speed > 0 ? grh.Speed : 100f;
                // Continuous: globalTime → fractional frame → integer frame (mod numFrames)
                // This never "wraps" — modulo handles it cleanly.
                double fractionalFrame = _globalTimeMs * grh.NumFrames / speed;
                return (int)(fractionalFrame % grh.NumFrames);
            }
        }

        return 0;
    }

    /// <summary>
    /// Like GetCurrentFrame but divides the effective time by a slowdown factor.
    /// Used for water tiles where the VB6 animation rate looks choppy at 60fps.
    /// </summary>
    public int GetCurrentFrameSlowed(int grhIndex, GameData? data, float slowdownFactor)
    {
        if (data != null && grhIndex > 0 && grhIndex < data.Grhs.Length)
        {
            var grh = data.Grhs[grhIndex];
            if (grh.NumFrames > 1)
            {
                float speed = grh.Speed > 0 ? grh.Speed : 100f;
                double effectiveTime = _globalTimeMs / slowdownFactor;
                double fractionalFrame = effectiveTime * grh.NumFrames / speed;
                return (int)(fractionalFrame % grh.NumFrames);
            }
        }
        return 0;
    }

    /// <summary>
    /// Returns the fractional frame position for smooth crossfade between frames.
    /// E.g. 1.7 means 70% blend from frame 1 to frame 2.
    /// </summary>
    public double GetFractionalFrame(int grhIndex, GameData? data, float slowdownFactor)
    {
        if (data != null && grhIndex > 0 && grhIndex < data.Grhs.Length)
        {
            var grh = data.Grhs[grhIndex];
            if (grh.NumFrames > 1)
            {
                float speed = grh.Speed > 0 ? grh.Speed : 100f;
                double effectiveTime = _globalTimeMs / slowdownFactor;
                double fractionalFrame = effectiveTime * grh.NumFrames / speed;
                return fractionalFrame % grh.NumFrames;
            }
        }
        return 0;
    }

    /// <summary>
    /// Clear all state (on map change). Resets global clock.
    /// </summary>
    public void Clear()
    {
        _globalTimeMs = 0;
        _fxStates.Clear();
    }
}
