#nullable enable
using System;
using System.Globalization;
using Godot;

namespace AOWorldEditor.Editor;

/// <summary>
/// A compact labeled slider: "Label [====o====] 12.5" — a draggable HSlider with
/// a live, editable value box on the right. Meant to replace bare SpinBoxes in the
/// particle editor so values can be tuned by dragging, while still allowing exact
/// numeric entry. Fires OnValueChanged continuously while dragging so previews can
/// update live.
/// </summary>
public partial class LabeledSlider : VBoxContainer
{
    public Action<double>? OnValueChanged;

    private HSlider? _slider;
    private LineEdit? _valueBox;
    private double _step;
    private bool _syncing; // guard against slider<->box feedback loops

    public double Value => _slider?.Value ?? 0;

    public void Init(string label, double min, double max, double value, double step = 1)
    {
        _step = step;
        AddThemeConstantOverride("separation", 1);

        var header = new HBoxContainer();
        header.AddThemeConstantOverride("separation", 6);
        var lbl = EditorTheme.MakeLabel(label, EditorTheme.TEXT_SECONDARY, EditorTheme.FONT_SM);
        lbl.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        header.AddChild(lbl);

        _valueBox = new LineEdit
        {
            Text = Format(value),
            Alignment = HorizontalAlignment.Right,
            CustomMinimumSize = new Vector2(60, 0),
        };
        _valueBox.AddThemeFontSizeOverride("font_size", EditorTheme.FONT_SM);
        _valueBox.TextSubmitted += OnBoxSubmitted;
        _valueBox.FocusExited += () => OnBoxSubmitted(_valueBox.Text);
        header.AddChild(_valueBox);
        AddChild(header);

        _slider = new HSlider
        {
            MinValue = min,
            MaxValue = max,
            Step = step,
            Value = value,
            CustomMinimumSize = new Vector2(0, 16),
        };
        _slider.ValueChanged += OnSliderChanged;
        AddChild(_slider);
    }

    public void SetValueNoSignal(double value)
    {
        if (_slider == null) return;
        _syncing = true;
        _slider.Value = value;
        if (_valueBox != null) _valueBox.Text = Format(_slider.Value);
        _syncing = false;
    }

    private void OnSliderChanged(double v)
    {
        if (_syncing) return;
        if (_valueBox != null) _valueBox.Text = Format(v);
        OnValueChanged?.Invoke(v);
    }

    private void OnBoxSubmitted(string text)
    {
        if (_slider == null) return;
        if (!double.TryParse(text.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out double parsed))
        {
            // Reject garbage: snap the box back to the slider's current value.
            if (_valueBox != null) _valueBox.Text = Format(_slider.Value);
            return;
        }
        parsed = Math.Clamp(parsed, _slider.MinValue, _slider.MaxValue);
        _syncing = true;
        _slider.Value = parsed;                 // snaps to Step
        _syncing = false;
        if (_valueBox != null) _valueBox.Text = Format(_slider.Value);
        OnValueChanged?.Invoke(_slider.Value);
    }

    private string Format(double v)
        => _step >= 1
            ? ((long)Math.Round(v)).ToString(CultureInfo.InvariantCulture)
            : v.ToString("0.##", CultureInfo.InvariantCulture);
}
