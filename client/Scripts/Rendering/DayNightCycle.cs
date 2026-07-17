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

    // Which reference tint we're transitioning toward — drives color, not just alpha,
    // so evening reads as a warm dusk instead of "a bit less night-blue".
    private Color _targetTint = DayTint;
    private Color _currentTint = DayTint;

    // Transition speed (alpha/color units per second)
    private const float TransitionSpeed = 0.15f;

    // Hour-to-darkness mapping (realistic curve):
    // 0-4: deep night, 5-7: dawn (warming up from dark), 8-16: full day,
    // 17-19: golden hour / dusk (warm, moderately dark), 20-23: night.
    private static readonly float[] HourAlpha = new float[24]
    {
        0.60f, 0.62f, 0.60f, 0.55f, 0.48f,           // 00-04: deep night
        0.38f, 0.24f, 0.10f,                            // 05-07: dawn brightening
        0.05f, 0.03f, 0.02f, 0.02f, 0.02f,             // 08-12: day (faint warm haze, not a flat 0)
        0.02f, 0.03f, 0.05f, 0.10f,                     // 13-16: afternoon, warming toward dusk
        0.22f, 0.34f, 0.30f,                            // 17-19: golden hour peak → fading dusk
        0.42f, 0.50f, 0.58f, 0.60f,                     // 20-23: night falling
    };

    // Which tint applies at each hour: 0=day (warm haze), 1=evening (golden dusk), 2=night (cool violet-blue).
    private static readonly byte[] HourTintKind = new byte[24]
    {
        2, 2, 2, 2, 2,       // 00-04: night
        1, 1, 1,              // 05-07: dawn reads warm, like sunrise
        0, 0, 0, 0, 0,       // 08-12: day
        0, 0, 0, 1,           // 13-16: day → warming into dusk
        1, 1, 1,              // 17-19: golden hour / dusk
        2, 2, 2, 2,          // 20-23: night
    };

    // Night tint: deep blue with a faint violet cast, like real moonlight rather than flat black-blue.
    private static readonly Color NightTint = new Color(0.05f, 0.06f, 0.16f, 1f);
    // Evening tint: saturated golden-hour orange — the warm low-sun glow, not just "dim brown".
    private static readonly Color EveningTint = new Color(0.55f, 0.24f, 0.07f, 1f);
    // Day tint: faint warm haze (real daylight is never a perfectly neutral "no filter").
    private static readonly Color DayTint = new Color(0.30f, 0.20f, 0.10f, 1f);

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
        ResolutionManager.OnResolutionChanged += OnResolutionChanged;
    }

    public override void _ExitTree()
    {
        ResolutionManager.OnResolutionChanged -= OnResolutionChanged;
    }

    private void OnResolutionChanged()
    {
        Position = new Vector2(ResolutionManager.LeftMargin - 5, ResolutionManager.TopMargin - 5);
        Size = new Vector2(ResolutionManager.ViewportW + 10, ResolutionManager.ViewportH + 10);
    }

    /// <summary>
    /// Set the current in-game hour (0-23). Called when server sends time update.
    /// </summary>
    public void SetHour(int hour)
    {
        _currentHour = Mathf.Clamp(hour, 0, 23);
        _targetAlpha = _enabled ? HourAlpha[_currentHour] : 0f;
        _targetTint = HourTintKind[_currentHour] switch
        {
            1 => EveningTint,
            2 => NightTint,
            _ => DayTint,
        };

        // Update IsNight flag in game state
        if (_state != null)
            _state.IsNight = _currentHour >= 20 || _currentHour < 6;
    }

    /// <summary>
    /// Set day/evening/night directly from the server's forced-phase NOC packet
    /// (0=day, 1=evening, 2=night). The server tracks a phase, not a live clock,
    /// so we map each phase to a representative hour and reuse SetHour's
    /// smooth alpha+color transition and tint selection.
    /// </summary>
    public void SetPhase(byte phase)
    {
        SetHour(phase switch
        {
            1 => 18, // evening
            2 => 22, // night
            _ => 12, // day
        });
    }

    /// <summary>
    /// Get the current hour for display purposes.
    /// </summary>
    public int CurrentHour => _currentHour;

    public override void _Process(double delta)
    {
        if (_state == null || !Visible) return;

        float targetAlpha = _enabled ? _targetAlpha : 0f;
        float t = TransitionSpeed * (float)delta;

        float prevAlpha = _currentAlpha;
        _currentAlpha = Mathf.MoveToward(_currentAlpha, targetAlpha, t);
        _currentTint = _currentTint.Lerp(_targetTint, Mathf.Clamp(t * 3f, 0f, 1f));

        if (Math.Abs(_currentAlpha - prevAlpha) > 0.0001f || _currentTint != Color)
        {
            Color = new Color(_currentTint.R, _currentTint.G, _currentTint.B, _currentAlpha);
        }
    }
}
