#nullable enable
using System;
using AOWorldEditor.Data;
using Godot;

namespace AOWorldEditor.Editor;

/// <summary>
/// Sidebar panel shown when the Humo tool is active. Exposes all the
/// configurable parameters of the painted humo: color, density, animation
/// toggle, animation speed. Writes back to MapData.PaintedFog* fields
/// and fires OnChanged so the ZoneFogRenderer can rebuild its mask.
/// </summary>
public partial class HumoConfigPanel : PanelContainer
{
    public MapData? Map;
    public Action? OnChanged;

    private SpinBox? _densitySpin;
    private SpinBox? _rSpin, _gSpin, _bSpin;
    private CheckBox? _animatedCheck;
    private SpinBox? _speedXSpin, _speedYSpin;
    private ColorRect? _colorPreview;
    private VBoxContainer? _speedBox;

    public override void _Ready()
    {
        CustomMinimumSize = new Vector2(260, 0);
        AddThemeStyleboxOverride("panel", EditorTheme.FlatBox(EditorTheme.BG_PANEL, 4, 8, 6, EditorTheme.BORDER, 1));

        var vbox = new VBoxContainer();
        vbox.AddThemeConstantOverride("separation", 6);
        AddChild(vbox);

        vbox.AddChild(EditorTheme.Heading("Humo (pintar)"));

        var help = EditorTheme.MakeLabel("Click izquierdo: pintar.  Click derecho: borrar.", EditorTheme.TEXT_SECONDARY, EditorTheme.FONT_SM);
        help.AutowrapMode = TextServer.AutowrapMode.Word;
        vbox.AddChild(help);

        // Density
        var densRow = new HBoxContainer();
        densRow.AddThemeConstantOverride("separation", 6);
        AddSpin(densRow, "Densidad:", 0, 255, Map?.PaintedFogDensity ?? 160, out _densitySpin);
        vbox.AddChild(densRow);

        vbox.AddChild(EditorTheme.MakeLabel("Color del humo:", EditorTheme.TEXT_SECONDARY, EditorTheme.FONT_SM));

        var colRow = new HBoxContainer();
        colRow.AddThemeConstantOverride("separation", 4);
        AddSpin(colRow, "R:", 0, 255, Map?.PaintedFogR ?? 128, out _rSpin);
        AddSpin(colRow, "G:", 0, 255, Map?.PaintedFogG ?? 140, out _gSpin);
        AddSpin(colRow, "B:", 0, 255, Map?.PaintedFogB ?? 160, out _bSpin);
        vbox.AddChild(colRow);

        _colorPreview = new ColorRect
        {
            CustomMinimumSize = new Vector2(0, 28),
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            Color = new Color((Map?.PaintedFogR ?? 128) / 255f, (Map?.PaintedFogG ?? 140) / 255f, (Map?.PaintedFogB ?? 160) / 255f),
        };
        vbox.AddChild(_colorPreview);

        // Animated toggle
        _animatedCheck = new CheckBox
        {
            Text = "Animar humo (nubes en movimiento)",
            ButtonPressed = Map?.PaintedFogAnimated ?? false,
        };
        vbox.AddChild(_animatedCheck);

        // Speed (only relevant when animated)
        _speedBox = new VBoxContainer();
        _speedBox.AddThemeConstantOverride("separation", 4);
        _speedBox.AddChild(EditorTheme.MakeLabel("Velocidad:", EditorTheme.TEXT_SECONDARY, EditorTheme.FONT_SM));
        var spdRow = new HBoxContainer();
        spdRow.AddThemeConstantOverride("separation", 6);
        AddSpin(spdRow, "X:", -100, 100, Map?.PaintedFogSpeedX ?? 5, out _speedXSpin);
        AddSpin(spdRow, "Y:", -100, 100, Map?.PaintedFogSpeedY ?? 2, out _speedYSpin);
        _speedBox.AddChild(spdRow);
        vbox.AddChild(_speedBox);
        _speedBox.Visible = Map?.PaintedFogAnimated ?? false;

        // Wire up
        _densitySpin!.ValueChanged += (_) => ApplyFromUi();
        _rSpin!.ValueChanged += (_) => ApplyFromUi();
        _gSpin!.ValueChanged += (_) => ApplyFromUi();
        _bSpin!.ValueChanged += (_) => ApplyFromUi();
        _animatedCheck.Toggled += (on) => { if (_speedBox != null) _speedBox.Visible = on; ApplyFromUi(); };
        _speedXSpin!.ValueChanged += (_) => ApplyFromUi();
        _speedYSpin!.ValueChanged += (_) => ApplyFromUi();
    }

    /// <summary>Refresh spin values from Map state (e.g. after loading a new map).</summary>
    public void RefreshFromMap()
    {
        if (Map == null) return;
        if (_densitySpin != null) _densitySpin.Value = Map.PaintedFogDensity;
        if (_rSpin != null) _rSpin.Value = Map.PaintedFogR;
        if (_gSpin != null) _gSpin.Value = Map.PaintedFogG;
        if (_bSpin != null) _bSpin.Value = Map.PaintedFogB;
        if (_animatedCheck != null) _animatedCheck.ButtonPressed = Map.PaintedFogAnimated;
        if (_speedXSpin != null) _speedXSpin.Value = Map.PaintedFogSpeedX;
        if (_speedYSpin != null) _speedYSpin.Value = Map.PaintedFogSpeedY;
        if (_speedBox != null) _speedBox.Visible = Map.PaintedFogAnimated;
        UpdateColorPreview();
    }

    private void ApplyFromUi()
    {
        if (Map == null) return;
        Map.PaintedFogDensity = (int)(_densitySpin?.Value ?? 160);
        Map.PaintedFogR = (int)(_rSpin?.Value ?? 128);
        Map.PaintedFogG = (int)(_gSpin?.Value ?? 140);
        Map.PaintedFogB = (int)(_bSpin?.Value ?? 160);
        Map.PaintedFogAnimated = _animatedCheck?.ButtonPressed ?? false;
        Map.PaintedFogSpeedX = (int)(_speedXSpin?.Value ?? 5);
        Map.PaintedFogSpeedY = (int)(_speedYSpin?.Value ?? 2);
        UpdateColorPreview();
        OnChanged?.Invoke();
    }

    private void UpdateColorPreview()
    {
        if (_colorPreview == null || Map == null) return;
        _colorPreview.Color = new Color(Map.PaintedFogR / 255f, Map.PaintedFogG / 255f, Map.PaintedFogB / 255f);
    }

    private static void AddSpin(HBoxContainer parent, string label, int min, int max, int val, out SpinBox spin)
    {
        var lbl = EditorTheme.MakeLabel(label, EditorTheme.TEXT_SECONDARY, EditorTheme.FONT_SM);
        parent.AddChild(lbl);
        spin = new SpinBox
        {
            MinValue = min,
            MaxValue = max,
            Step = 1,
            Value = val,
            CustomMinimumSize = new Vector2(64, 0),
        };
        parent.AddChild(spin);
    }
}
