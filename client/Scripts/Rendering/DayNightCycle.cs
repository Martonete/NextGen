using Godot;
using System;
using ArgentumNextgen.Game;

namespace ArgentumNextgen.Rendering;

/// <summary>
/// Client-side day/night cycle system.
/// Modulates world ambient lighting based on in-game hour (0-23).
/// VB6 had hours 0-23 with darkness at night (20:00-06:00).
/// Uses a ColorRect overlay on the game viewport with varying alpha.
/// Server sends current game hour via existing packet; this system
/// smoothly transitions between light levels each frame.
/// </summary>
public partial class DayNightCycle : ColorRect
{
    private GameState? _state;

    // Current and target alpha for smooth transitions
    private float _currentAlpha;
    private float _targetAlpha;
    private int _currentHour = 12; // Default: noon
    private bool _enabled = true;

    // Transition speed (alpha units per second)
    private const float TransitionSpeed = 0.15f;

    // Hour-to-darkness mapping:
    // 0-5: full night (dark), 6-8: dawn (transition), 9-17: day (bright),
    // 18-19: dusk (transition), 20-23: night (dark)
    private static readonly float[] HourAlpha = new float[24]
    {
        0.55f, 0.55f, 0.55f, 0.50f, 0.45f, 0.35f, // 00-05: night → pre-dawn
        0.25f, 0.15f, 0.05f,                         // 06-08: dawn
        0.00f, 0.00f, 0.00f, 0.00f, 0.00f, 0.00f,  // 09-14: full day
        0.00f, 0.00f, 0.00f,                         // 15-17: afternoon
        0.10f, 0.25f,                                 // 18-19: dusk
        0.40f, 0.50f, 0.55f, 0.55f,                  // 20-23: night
    };

    // Night tint color (dark blue for moonlight feel)
    private static readonly Color NightTint = new Color(0.05f, 0.05f, 0.15f, 1f);
    // Day color (transparent — no overlay)
    private static readonly Color DayTint = new Color(0f, 0f, 0f, 1f);

    public bool Enabled
    {
        get => _enabled;
        set
        {
            _enabled = value;
            if (!_enabled) _currentAlpha = 0f;
        }
    }

    /// <summary>
    /// Initialize the day/night cycle overlay.
    /// Should be added as a child of the game viewport UI layer.
    /// </summary>
    public void Init(GameState state)
    {
        _state = state;
        _currentAlpha = 0f;
        _targetAlpha = 0f;
        MouseFilter = MouseFilterEnum.Ignore;
        ZIndex = 50; // Above world, below UI panels
    }

    public override void _Ready()
    {
        // Cover the game viewport area with a small margin
        Position = new Vector2(ResolutionManager.LeftMargin - 5, ResolutionManager.TopMargin - 5);
        Size = new Vector2(ResolutionManager.ViewportW + 10, ResolutionManager.ViewportH + 10);
        Color = new Color(0, 0, 0, 0);
        MouseFilter = MouseFilterEnum.Ignore;
    }

    /// <summary>
    /// Set the current in-game hour (0-23). Called when server sends time update.
    /// </summary>
    public void SetHour(int hour)
    {
        _currentHour = Mathf.Clamp(hour, 0, 23);
        if (_enabled)
            _targetAlpha = HourAlpha[_currentHour];
        else
            _targetAlpha = 0f;

        // Update IsNight flag in game state
        if (_state != null)
            _state.IsNight = _currentHour >= 20 || _currentHour < 6;
    }

    /// <summary>
    /// Get the current hour for display purposes.
    /// </summary>
    public int CurrentHour => _currentHour;

    public override void _Process(double delta)
    {
        if (_state == null || !Visible) return;

        if (!_enabled)
        {
            if (_currentAlpha > 0.001f)
            {
                _currentAlpha = Mathf.MoveToward(_currentAlpha, 0f, TransitionSpeed * (float)delta);
                Color = new Color(NightTint.R, NightTint.G, NightTint.B, _currentAlpha);
            }
            return;
        }

        // Smooth transition toward target
        _currentAlpha = Mathf.MoveToward(_currentAlpha, _targetAlpha, TransitionSpeed * (float)delta);

        // Interpolate between day (transparent) and night (blue tint) based on alpha
        float nightFactor = _currentAlpha / 0.55f; // 0 = day, 1 = full night
        nightFactor = Mathf.Clamp(nightFactor, 0f, 1f);

        float r = Mathf.Lerp(DayTint.R, NightTint.R, nightFactor);
        float g = Mathf.Lerp(DayTint.G, NightTint.G, nightFactor);
        float b = Mathf.Lerp(DayTint.B, NightTint.B, nightFactor);

        Color = new Color(r, g, b, _currentAlpha);
    }
}
