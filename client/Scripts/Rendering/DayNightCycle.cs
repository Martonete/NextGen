using System;
using Godot;
using ArgentumNextgen.Game;

namespace ArgentumNextgen.Rendering;

/// <summary>
/// AO20 global daylight controller.
///
/// AO20 does not put a translucent colour rectangle over the screen.  It gives every
/// world vertex the current <c>global_light</c> RGB, which multiplies the source sprite.
/// This node keeps that exact 24-hour palette and exposes the resulting multiplier to
/// WorldRenderer.  It remains a transparent Control only to preserve the existing UI tree.
/// </summary>
public partial class DayNightCycle : ColorRect
{
    // ModMetereologia.bas: LIGHT_TRANSITION_DURATION = 5000.
    private const float LightTransitionDurationSeconds = 5f;

    // Palette transcribed from AO20 ModMetereologia.IniciarMeteorologia().
    // The VB array is stored offset by one (DayColors(23) is 00:00), so this table is
    // deliberately indexed by the human-readable hour instead.
    private static readonly Color[] Ao20HourLights =
    {
        Rgb(120, 120, 120), // 00
        Rgb(120, 120, 120), // 01
        Rgb(120, 120, 120), // 02
        Rgb(120, 120, 120), // 03
        Rgb(120, 120, 120), // 04
        Rgb(138, 138, 138), // 05
        Rgb(156, 156, 145), // 06
        Rgb(170, 170, 155), // 07
        Rgb(185, 185, 185), // 08
        Rgb(200, 200, 200), // 09
        Rgb(220, 220, 220), // 10
        Rgb(235, 235, 235), // 11
        Rgb(245, 245, 245), // 12
        Rgb(255, 255, 255), // 13
        Rgb(255, 255, 255), // 14
        Rgb(255, 255, 255), // 15
        Rgb(245, 245, 245), // 16
        Rgb(230, 230, 230), // 17
        Rgb(220, 220, 220), // 18
        Rgb(200, 200, 180), // 19
        Rgb(180, 160, 160), // 20
        Rgb(160, 160, 160), // 21
        Rgb(140, 140, 140), // 22
        Rgb(120, 120, 140), // 23
    };

    // The three current server commands are artistic anchors rather than a live HORA
    // packet. Keep them near AO20's palette, while making the requested daytime a
    // little softer and sunset slightly warmer. The full table above stays untouched
    // for real clock updates.
    private static readonly Color ForcedDayLight = Ao20HourLights[12];       // AO20 12:00, 245/245/245
    private static readonly Color ForcedEveningLight = Rgb(190, 155, 135);   // subtly warmer than AO20 20:00

    private GameState? _state;
    private Color _currentLight = Colors.White;
    private Color _lastLight = Colors.White;
    private Color _nextLight = Colors.White;
    private float _lightTransition = 1f;
    private int _currentHour = 13;
    private bool _enabled = true;

    /// <summary>Raised whenever AO20's global_light changes.</summary>
    public Action<Color>? OnGlobalLightChanged { get; set; }

    public Color CurrentLight => _enabled ? _currentLight : Colors.White;

    public bool Enabled
    {
        get => _enabled;
        set
        {
            if (_enabled == value) return;
            _enabled = value;
            PublishLight();
        }
    }

    public void Init(GameState state)
    {
        _state = state;
        _currentHour = 13;
        _currentLight = Ao20HourLights[_currentHour];
        _lastLight = _currentLight;
        _nextLight = _currentLight;
        _lightTransition = 1f;
        MouseFilter = MouseFilterEnum.Ignore;
        Color = new Color(0f, 0f, 0f, 0f);
    }

    public override void _Ready()
    {
        // No screen overlay: AO20 applies global_light to the world draw calls.
        MouseFilter = MouseFilterEnum.Ignore;
        Color = new Color(0f, 0f, 0f, 0f);
    }

    /// <summary>
    /// Selects an AO20 clock hour.  Updates use the same 5-second LerpRGBA transition
    /// performed by frmMain.UpdateLight_Timer.
    /// </summary>
    public void SetHour(int hour)
    {
        SetTargetLight(Mathf.Clamp(hour, 0, 23), Ao20HourLights[Mathf.Clamp(hour, 0, 23)]);
    }

    private void SetTargetLight(int hour, Color target)
    {
        _currentHour = hour;
        _lastLight = _currentLight;
        _nextLight = target;
        _lightTransition = 0f;

        if (_state != null)
        {
            _state.GameHour = _currentHour;
            // AO20 uses TimeIndex 0..2 (00:00 through 05:59) as its night band.
            _state.IsNight = _currentHour < 6;
        }

        PublishLight();
    }

    /// <summary>
    /// NOC currently transports three server-controlled states.  Their anchors are
    /// actual AO20 palette values: daylight 13:00, sunset 20:00 and night 00:00.
    /// </summary>
    public void SetPhase(byte phase)
    {
        switch (phase)
        {
            case 1:
                SetTargetLight(20, ForcedEveningLight);
                break;
            case 2:
                SetTargetLight(0, Ao20HourLights[0]);
                break;
            default:
                SetTargetLight(12, ForcedDayLight);
                break;
        }
    }

    public int CurrentHour => _currentHour;

    public override void _Process(double delta)
    {
        if (_state == null || _lightTransition >= 1f) return;

        _lightTransition = Mathf.Min(1f, _lightTransition + (float)delta / LightTransitionDurationSeconds);
        _currentLight = _lastLight.Lerp(_nextLight, _lightTransition);
        PublishLight();
    }

    private void PublishLight()
    {
        // Keep this node wholly transparent; only WorldRenderer consumes the colour.
        Color = new Color(0f, 0f, 0f, 0f);
        OnGlobalLightChanged?.Invoke(CurrentLight);
    }

    private static Color Rgb(byte r, byte g, byte b) => new(r / 255f, g / 255f, b / 255f, 1f);
}
