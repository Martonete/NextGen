using System;
using System.Collections.Generic;
using ArgentumNextgen.Data;

namespace ArgentumNextgen.Rendering;

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

	// Reusable buffer for iterating _fxStates keys — avoids a new List<int> allocation every frame
	private readonly List<int> _keysBuffer = new();

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
	// Wrap period: LCM-friendly value (100 * 200 * 300 * 400 * 500 * 600 = divisible by all common speeds).
	// 3_600_000_000 ms = 1000 hours. Double has 15-digit precision → safe up to ~10^15 ms (31K years).
	// At 1000h, precision is ~0.001ms — no visible glitch. No animation discontinuity because
	// frame calc already uses (globalTime * N / speed) % N, making the wrap transparent.
	private const double ModuloPeriod = 3_600_000_000.0;

	public void Update(float delta, GameData data)
	{
		float deltaMs = delta * 1000f;
		_globalTimeMs += deltaMs;
		if (_globalTimeMs >= ModuloPeriod)
			_globalTimeMs -= ModuloPeriod;

		// Advance one-shot FX animations and remove completed ones
		if (_fxStates.Count > 0)
		{
			_keysBuffer.Clear();
			_keysBuffer.AddRange(_fxStates.Keys);
			var keys = _keysBuffer;
			foreach (int key in keys)
			{
				if (key <= 0 || key >= data.Grhs.Length)
				{
					_fxStates.Remove(key);
					continue;
				}
				var grh = data.Grhs[key];
				if (grh.NumFrames <= 1)
				{
					_fxStates.Remove(key);
					continue;
				}

				var state = _fxStates[key];
				float speed = grh.Speed > 0 ? grh.Speed : 100f;
				state.FrameCounter += deltaMs * grh.NumFrames / speed;

				if (state.FrameCounter >= grh.NumFrames)
				{
					if (!state.Loop)
					{
						// One-shot completed — remove from dictionary
						_fxStates.Remove(key);
						continue;
					}
					// Looping FX: wrap counter to prevent float precision loss over long sessions
					state.FrameCounter %= grh.NumFrames;
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
