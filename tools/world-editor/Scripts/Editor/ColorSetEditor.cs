#nullable enable
using System;
using Godot;

namespace AOWorldEditor.Editor;

/// <summary>
/// Editor for one Particles.ini ColorSetN entry (one of the 4 vertex-gradient
/// color slots averaged together at spawn — see ParticleEngine.SpawnParticle).
///
/// Uses a native Godot ColorPickerButton (HSV wheel, hex, eyedropper, presets) as
/// the primary control, so picking colors is visual, not numeric guesswork.
///
/// VB6 BGR quirk: the INI stores each ColorSetN as R,G,B, but the simulation reads
/// it with R and B swapped (VB6 RGB() packs 0x00BBGGRR, treated as 0xAARRGGBB
/// vertex color downstream — see ParticleEngine.SpawnParticle). The public R/G/B
/// here are the RAW ini bytes (so save stays lossless), while the picker shows and
/// edits the ACTUAL in-game color (swap applied). Convert on the boundary only.
/// </summary>
public partial class ColorSetEditor : HBoxContainer
{
    public byte R, G, B;   // raw INI bytes
    public Action? OnChanged;

    private ColorPickerButton? _picker;
    private bool _syncing;

    public void Init(string label, byte r, byte g, byte b)
    {
        R = r; G = g; B = b;
        AddThemeConstantOverride("separation", 6);

        var lbl = EditorTheme.MakeLabel(label, EditorTheme.TEXT_SECONDARY, EditorTheme.FONT_SM);
        lbl.CustomMinimumSize = new Vector2(48, 0);
        AddChild(lbl);

        _picker = new ColorPickerButton
        {
            CustomMinimumSize = new Vector2(0, 26),
            // Show the ACTUAL in-game color (raw bytes with R<->B swapped).
            Color = InGameColor(),
        };
        _picker.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        _picker.AddThemeFontSizeOverride("font_size", EditorTheme.FONT_SM);
        _picker.ColorChanged += OnPickerChanged;
        AddChild(_picker);
    }

    /// <summary>Set the raw bytes without firing OnChanged (used by "copy to all" etc.).</summary>
    public void SetRawNoSignal(byte r, byte g, byte b)
    {
        R = r; G = g; B = b;
        if (_picker == null) return;
        _syncing = true;
        _picker.Color = InGameColor();
        _syncing = false;
    }

    private void OnPickerChanged(Color inGame)
    {
        if (_syncing) return;
        // Picker holds the in-game color; unswap back to raw INI bytes.
        R = (byte)Mathf.Round(inGame.B * 255f);
        G = (byte)Mathf.Round(inGame.G * 255f);
        B = (byte)Mathf.Round(inGame.R * 255f);
        OnChanged?.Invoke();
    }

    /// <summary>Raw INI bytes → the color the player actually sees (R<->B swapped).</summary>
    private Color InGameColor() => new(B / 255f, G / 255f, R / 255f);
}
