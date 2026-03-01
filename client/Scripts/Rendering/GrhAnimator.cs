using System;
using System.Collections.Generic;
using TierrasSagradasAO.Data;

namespace TierrasSagradasAO.Rendering;

/// <summary>
/// Manages animation state for GRH indices.
/// Tracks frame counters and resolves current frame.
/// VB6 formula: FrameCounter += elapsedMs * NumFrames / Speed
/// (no 0.7 multiplier — that only applies to character walk bodies via movementSpeed)
/// VB6 uses 1-indexed frames; Godot uses 0-indexed (offset handled by array indexing).
/// </summary>
public class GrhAnimator
{
    private readonly Dictionary<int, AnimState> _states = new();

    public struct AnimState
    {
        public float FrameCounter;
        public bool Started;
        public bool Loop;
    }

    /// <summary>
    /// Start animating a GRH index. If already animating, does nothing.
    /// </summary>
    public void StartAnim(int grhIndex, bool loop = true)
    {
        if (!_states.ContainsKey(grhIndex))
        {
            _states[grhIndex] = new AnimState { FrameCounter = 0, Started = true, Loop = loop };
        }
    }

    /// <summary>
    /// Advance all animations by delta time.
    /// VB6 Grh_Render: FrameCounter += elapsedMs * NumFrames / Speed
    /// Loop: FrameCounter = (FrameCounter Mod NumFrames) + 1 (1-indexed)
    /// Godot equivalent: FrameCounter %= NumFrames (0-indexed)
    /// </summary>
    public void Update(float delta, GameData data)
    {
        float deltaMs = delta * 1000f;
        var keys = new List<int>(_states.Keys);

        foreach (int key in keys)
        {
            if (key <= 0 || key >= data.Grhs.Length) continue;

            var grh = data.Grhs[key];
            if (grh.NumFrames <= 1) continue;

            var state = _states[key];

            float speed = grh.Speed > 0 ? grh.Speed : 100f;
            state.FrameCounter += deltaMs * grh.NumFrames / speed;

            if (state.FrameCounter >= grh.NumFrames)
            {
                if (state.Loop)
                    state.FrameCounter %= grh.NumFrames;
                else
                    state.FrameCounter = grh.NumFrames - 1;
            }

            _states[key] = state;
        }
    }

    /// <summary>
    /// Get current frame index for an animated GRH.
    /// </summary>
    public int GetCurrentFrame(int grhIndex)
    {
        if (_states.TryGetValue(grhIndex, out var state))
            return (int)state.FrameCounter;
        return 0;
    }

    /// <summary>
    /// Clear all animation states (e.g., on map change).
    /// </summary>
    public void Clear()
    {
        _states.Clear();
    }
}
