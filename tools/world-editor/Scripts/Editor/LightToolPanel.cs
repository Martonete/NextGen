#nullable enable
using System;
using System.Collections.Generic;
using AOWorldEditor.Data;
using Godot;

namespace AOWorldEditor.Editor;

/// <summary>
/// "Luz avanzada" tool sidebar panel.
///
/// Mirrors the structure of <see cref="HumoConfigPanel"/> but edits a single
/// selected <see cref="MapLight"/> rather than a painted layer. The user
/// places lights by clicking on the viewport (handled by the integrator) and
/// the panel exposes its full property set: type, color, energy, radius,
/// direction, cone, flicker, pulse, shadows.
///
/// The dropdown at the top has three sections:
///  - Current lights (★) — one entry per <see cref="MapLight"/> already
///    placed on the map. Selecting one makes it the active light; edits to
///    the sliders modify ONLY that light.
///  - Built-in assets (◆) — <see cref="LightAsset.BuiltIn"/> templates.
///    Selecting one creates a NEW <see cref="MapLight"/> at map center seeded
///    from the template.
///  - User assets (✦) — same as built-ins but user-saved.
///
/// All slider edits funnel through <see cref="ApplyFromUi"/> which mutates
/// the active light in-place and fires <see cref="OnChanged"/> so the
/// integrator can repaint and mark the map dirty.
/// </summary>
public partial class LightToolPanel : PanelContainer
{
    public MapData? Map;
    public int SelectedLightIndex = -1;
    public Action? OnChanged;

    // ── Top section ───────────────────────────────────────────────────
    private OptionButton? _lightOption;
    private Button? _deleteLightBtn;
    private LineEdit? _newAssetNameEdit;
    private Button? _saveAssetBtn;
    private Label? _saveStatusLabel;
    private Label? _emptyHintLabel;

    // ── Editing area (hidden when no light selected) ──────────────────
    private VBoxContainer? _editArea;
    private OptionButton? _typeOption;
    private SpinBox? _rSpin, _gSpin, _bSpin;
    private ColorRect? _colorPreview;
    private SpinBox? _energySpin;
    private SpinBox? _radiusSpin;
    private HBoxContainer? _directionRow;
    private SpinBox? _directionSpin;
    private HBoxContainer? _coneRow;
    private SpinBox? _coneSpin;
    private SpinBox? _flickerSpin;
    private SpinBox? _pulseSpin;
    private CheckBox? _shadowsCheck;

    // Entry types in the dropdown
    private enum EntryType { CurrentLight, BuiltInAsset, UserAsset }
    private readonly List<(EntryType type, int index)> _dropdownEntries = new();
    private bool _suppressChangeEvents;

    public override void _Ready()
    {
        CustomMinimumSize = new Vector2(260, 0);
        AddThemeStyleboxOverride("panel", EditorTheme.FlatBox(EditorTheme.BG_PANEL, 4, 8, 6, EditorTheme.BORDER, 1));

        var scroll = new ScrollContainer
        {
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            SizeFlagsVertical = SizeFlags.ExpandFill,
        };
        AddChild(scroll);

        var vbox = new VBoxContainer
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
        };
        vbox.AddThemeConstantOverride("separation", 6);
        scroll.AddChild(vbox);

        // ── Heading ───────────────────────────────────────────────────
        vbox.AddChild(EditorTheme.Heading("Luz avanzada"));
        var help = EditorTheme.MakeLabel(
            "Click en el mapa para colocar una luz. Doble-click para editarla. Cada luz es independiente.",
            EditorTheme.TEXT_SECONDARY, EditorTheme.FONT_SM);
        help.AutowrapMode = TextServer.AutowrapMode.Word;
        vbox.AddChild(help);

        // ── Light/asset dropdown ──────────────────────────────────────
        vbox.AddChild(EditorTheme.MakeLabel(
            "Luz activa / copiar desde asset:",
            EditorTheme.TEXT_SECONDARY, EditorTheme.FONT_SM));
        _lightOption = new OptionButton
        {
            CustomMinimumSize = new Vector2(0, 24),
        };
        vbox.AddChild(_lightOption);
        _lightOption.ItemSelected += OnDropdownSelected;

        _deleteLightBtn = new Button { Text = "🗑 Eliminar luz actual" };
        _deleteLightBtn.Pressed += OnDeleteCurrentLight;
        vbox.AddChild(_deleteLightBtn);

        // ── Save-as-asset section ─────────────────────────────────────
        vbox.AddChild(new HSeparator());
        vbox.AddChild(EditorTheme.MakeLabel(
            "Guardar como asset reutilizable:",
            EditorTheme.TEXT_SECONDARY, EditorTheme.FONT_SM));
        var saveRow = new HBoxContainer();
        saveRow.AddThemeConstantOverride("separation", 4);
        _newAssetNameEdit = new LineEdit
        {
            PlaceholderText = "Nombre del asset (ej: Vela roja)...",
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
        };
        saveRow.AddChild(_newAssetNameEdit);
        _saveAssetBtn = new Button { Text = "💾 Guardar" };
        _saveAssetBtn.Pressed += OnSaveAsset;
        saveRow.AddChild(_saveAssetBtn);
        vbox.AddChild(saveRow);

        _saveStatusLabel = EditorTheme.MakeLabel("", EditorTheme.TEXT_SECONDARY, EditorTheme.FONT_SM);
        _saveStatusLabel.Visible = false;
        vbox.AddChild(_saveStatusLabel);

        vbox.AddChild(new HSeparator());

        // ── Empty hint (when nothing selected) ────────────────────────
        _emptyHintLabel = EditorTheme.MakeLabel(
            "(clickeá en el mapa para colocar una luz)",
            EditorTheme.TEXT_MUTED, EditorTheme.FONT_SM);
        _emptyHintLabel.AutowrapMode = TextServer.AutowrapMode.Word;
        vbox.AddChild(_emptyHintLabel);

        // ── Editing area ──────────────────────────────────────────────
        _editArea = new VBoxContainer();
        _editArea.AddThemeConstantOverride("separation", 6);
        vbox.AddChild(_editArea);

        // Type
        _editArea.AddChild(EditorTheme.MakeLabel("Tipo:", EditorTheme.TEXT_SECONDARY, EditorTheme.FONT_SM));
        _typeOption = new OptionButton
        {
            CustomMinimumSize = new Vector2(0, 24),
        };
        _typeOption.AddItem("Omni (puntual)", 0);
        _typeOption.AddItem("Spot (dirigida)", 1);
        _typeOption.AddItem("Direccional (sol)", 2);
        _typeOption.ItemSelected += OnTypeSelected;
        _editArea.AddChild(_typeOption);

        // Color
        _editArea.AddChild(EditorTheme.MakeLabel("Color:", EditorTheme.TEXT_SECONDARY, EditorTheme.FONT_SM));
        var colRow = new HBoxContainer();
        colRow.AddThemeConstantOverride("separation", 4);
        AddIntSpin(colRow, "R:", 0, 255, 1, 255, out _rSpin);
        AddIntSpin(colRow, "G:", 0, 255, 1, 220, out _gSpin);
        AddIntSpin(colRow, "B:", 0, 255, 1, 180, out _bSpin);
        _editArea.AddChild(colRow);

        _colorPreview = new ColorRect
        {
            CustomMinimumSize = new Vector2(0, 28),
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            Color = new Color(1f, 220f / 255f, 180f / 255f),
        };
        _editArea.AddChild(_colorPreview);

        // Energy
        var energyRow = new HBoxContainer();
        energyRow.AddThemeConstantOverride("separation", 6);
        AddFloatSpin(energyRow, "Energía:", 0.0, 10.0, 0.1, 1.0, out _energySpin);
        _editArea.AddChild(energyRow);

        // Radius
        var radiusRow = new HBoxContainer();
        radiusRow.AddThemeConstantOverride("separation", 6);
        AddFloatSpin(radiusRow, "Radio (tiles):", 1.0, 30.0, 0.5, 6.0, out _radiusSpin);
        _editArea.AddChild(radiusRow);

        // Direction (visible for Spot / Directional)
        _directionRow = new HBoxContainer();
        _directionRow.AddThemeConstantOverride("separation", 6);
        AddFloatSpin(_directionRow, "Dirección (°):", 0.0, 359.0, 1.0, 0.0, out _directionSpin);
        _editArea.AddChild(_directionRow);

        // Cone (visible for Spot only)
        _coneRow = new HBoxContainer();
        _coneRow.AddThemeConstantOverride("separation", 6);
        AddFloatSpin(_coneRow, "Cono (°):", 10.0, 180.0, 1.0, 60.0, out _coneSpin);
        _editArea.AddChild(_coneRow);

        // Flicker
        var flickerRow = new HBoxContainer();
        flickerRow.AddThemeConstantOverride("separation", 6);
        AddIntSpin(flickerRow, "Flicker %:", 0, 100, 1, 0, out _flickerSpin);
        _editArea.AddChild(flickerRow);

        // Pulse
        var pulseRow = new HBoxContainer();
        pulseRow.AddThemeConstantOverride("separation", 6);
        AddFloatSpin(pulseRow, "Pulso (Hz):", 0.0, 10.0, 0.1, 0.0, out _pulseSpin);
        _editArea.AddChild(pulseRow);

        // Shadows
        _shadowsCheck = new CheckBox
        {
            Text = "Sombras activadas",
            ButtonPressed = true,
        };
        _shadowsCheck.Toggled += (_) => ApplyFromUi();
        _editArea.AddChild(_shadowsCheck);

        // Wire slider change events
        _rSpin!.ValueChanged += (_) => ApplyFromUi();
        _gSpin!.ValueChanged += (_) => ApplyFromUi();
        _bSpin!.ValueChanged += (_) => ApplyFromUi();
        _energySpin!.ValueChanged += (_) => ApplyFromUi();
        _radiusSpin!.ValueChanged += (_) => ApplyFromUi();
        _directionSpin!.ValueChanged += (_) => ApplyFromUi();
        _coneSpin!.ValueChanged += (_) => ApplyFromUi();
        _flickerSpin!.ValueChanged += (_) => ApplyFromUi();
        _pulseSpin!.ValueChanged += (_) => ApplyFromUi();

        UpdateEditAreaVisibility();
    }

    public void RefreshFromMap()
    {
        if (Map == null) return;
        RebuildDropdown();
        LoadActiveLightIntoUi();
    }

    /// <summary>Rebuild the dropdown — current lights first, then assets as templates.</summary>
    public void RebuildDropdown()
    {
        if (_lightOption == null || Map == null) return;
        _suppressChangeEvents = true;
        _lightOption.Clear();
        _dropdownEntries.Clear();

        // Current lights
        for (int i = 0; i < Map.LightData.Lights.Count; i++)
        {
            var l = Map.LightData.Lights[i];
            string label = string.IsNullOrEmpty(l.Name)
                ? $"★ Luz #{i + 1} (x={l.X}, y={l.Y})"
                : $"★ {l.Name} #{i + 1} (x={l.X}, y={l.Y})";
            _lightOption.AddItem(label);
            _dropdownEntries.Add((EntryType.CurrentLight, i));
        }
        if (Map.LightData.Lights.Count > 0)
            _lightOption.AddSeparator("— Nuevo desde asset —");

        // Built-in assets
        for (int i = 0; i < LightAsset.BuiltIn.Count; i++)
        {
            _lightOption.AddItem($"◆ {LightAsset.BuiltIn[i].Name}");
            _dropdownEntries.Add((EntryType.BuiltInAsset, i));
        }

        // User assets
        for (int i = 0; i < Map.LightData.UserAssets.Count; i++)
        {
            _lightOption.AddItem($"✦ {Map.LightData.UserAssets[i].Name}");
            _dropdownEntries.Add((EntryType.UserAsset, i));
        }

        // Select the active light (if any)
        if (SelectedLightIndex >= 0 && SelectedLightIndex < Map.LightData.Lights.Count
            && _lightOption.ItemCount > 0)
        {
            _lightOption.Select(SelectedLightIndex);
        }
        _suppressChangeEvents = false;
    }

    private void OnDropdownSelected(long index)
    {
        if (_suppressChangeEvents || Map == null) return;
        if (index < 0) return;

        // Map OptionButton index to our entry index (skipping the separator).
        // The dropdown visual order:
        //   [current lights 0..N-1] [separator?] [builtins] [user assets]
        // _dropdownEntries matches the list WITHOUT the separator.
        int entryIdx = (int)index;
        int lightCount = Map.LightData.Lights.Count;
        // Guard: if the user somehow selects the separator row itself
        // (visual index == lightCount), ignore it — otherwise we'd treat
        // it as the first built-in asset and create a spurious light.
        if (lightCount > 0 && entryIdx == lightCount) return;
        if (lightCount > 0 && entryIdx > lightCount)
            entryIdx -= 1; // account for the separator
        if (entryIdx < 0 || entryIdx >= _dropdownEntries.Count) return;

        var (type, dataIdx) = _dropdownEntries[entryIdx];
        switch (type)
        {
            case EntryType.CurrentLight:
                SelectedLightIndex = dataIdx;
                LoadActiveLightIntoUi();
                OnChanged?.Invoke();
                break;
            case EntryType.BuiltInAsset:
                CreateLightFromAsset(LightAsset.BuiltIn[dataIdx]);
                break;
            case EntryType.UserAsset:
                CreateLightFromAsset(Map.LightData.UserAssets[dataIdx]);
                break;
        }
    }

    private void CreateLightFromAsset(LightAsset template)
    {
        if (Map == null) return;
        var light = new MapLight
        {
            X = Math.Max(1, Map.Width / 2),
            Y = Math.Max(1, Map.Height / 2),
            Type = template.Type,
            R = template.R,
            G = template.G,
            B = template.B,
            Energy = template.Energy,
            Radius = template.Radius,
            DirectionDeg = template.DirectionDeg,
            ConeDegrees = template.ConeDegrees,
            FlickerPct = template.FlickerPct,
            PulseHz = template.PulseHz,
            ShadowsEnabled = template.ShadowsEnabled,
            Name = template.Name,
        };
        Map.LightData.Lights.Add(light);
        SelectedLightIndex = Map.LightData.Lights.Count - 1;
        GD.Print($"[LightToolPanel] New light '{light.Name}' from asset at ({light.X},{light.Y})");
        RebuildDropdown();
        LoadActiveLightIntoUi();
        OnChanged?.Invoke();
    }

    private void OnDeleteCurrentLight()
    {
        if (Map == null) return;
        if (SelectedLightIndex < 0 || SelectedLightIndex >= Map.LightData.Lights.Count) return;
        Map.LightData.Lights.RemoveAt(SelectedLightIndex);
        SelectedLightIndex = -1;
        RebuildDropdown();
        LoadActiveLightIntoUi();
        OnChanged?.Invoke();
    }

    private void OnSaveAsset()
    {
        if (Map == null) return;
        // Clear any stale status from a previous attempt before reporting
        // the outcome of this one.
        if (_saveStatusLabel != null) _saveStatusLabel.Visible = false;

        string name = _newAssetNameEdit?.Text?.Trim() ?? "";
        if (string.IsNullOrEmpty(name))
        {
            ShowSaveStatus("⚠ Poné un nombre para el asset", false);
            return;
        }

        var asset = new LightAsset
        {
            Name = name,
            Type = ParseTypeFromUi(),
            R = (int)(_rSpin?.Value ?? 255),
            G = (int)(_gSpin?.Value ?? 220),
            B = (int)(_bSpin?.Value ?? 180),
            Energy = (float)(_energySpin?.Value ?? 1.0),
            Radius = (float)(_radiusSpin?.Value ?? 6.0),
            DirectionDeg = (float)(_directionSpin?.Value ?? 0.0),
            ConeDegrees = (float)(_coneSpin?.Value ?? 60.0),
            FlickerPct = (int)(_flickerSpin?.Value ?? 0),
            PulseHz = (float)(_pulseSpin?.Value ?? 0.0),
            ShadowsEnabled = _shadowsCheck?.ButtonPressed ?? true,
        };
        Map.LightData.UserAssets.Add(asset);
        if (_newAssetNameEdit != null) _newAssetNameEdit.Text = "";
        RebuildDropdown();
        ShowSaveStatus($"✓ Asset '{name}' guardado — ya disponible en el dropdown", true);
        GD.Print($"[LightToolPanel] Saved light asset '{name}'");
    }

    private void ShowSaveStatus(string text, bool success)
    {
        if (_saveStatusLabel == null) return;
        _saveStatusLabel.Text = text;
        _saveStatusLabel.Visible = true;
        _saveStatusLabel.AddThemeColorOverride("font_color",
            success ? new Color(0.5f, 0.9f, 0.5f) : new Color(0.95f, 0.7f, 0.4f));
    }

    private void OnTypeSelected(long index)
    {
        if (_suppressChangeEvents) return;
        UpdateEditAreaVisibility();
        ApplyFromUi();
    }

    private void LoadActiveLightIntoUi()
    {
        if (Map == null || _typeOption == null) return;
        _suppressChangeEvents = true;

        bool hasSelection = SelectedLightIndex >= 0 && SelectedLightIndex < Map.LightData.Lights.Count;
        if (hasSelection)
        {
            var l = Map.LightData.Lights[SelectedLightIndex];
            _typeOption.Select((int)l.Type);
            if (_rSpin != null) _rSpin.Value = l.R;
            if (_gSpin != null) _gSpin.Value = l.G;
            if (_bSpin != null) _bSpin.Value = l.B;
            if (_energySpin != null) _energySpin.Value = l.Energy;
            if (_radiusSpin != null) _radiusSpin.Value = l.Radius;
            if (_directionSpin != null) _directionSpin.Value = l.DirectionDeg;
            if (_coneSpin != null) _coneSpin.Value = l.ConeDegrees;
            if (_flickerSpin != null) _flickerSpin.Value = l.FlickerPct;
            if (_pulseSpin != null) _pulseSpin.Value = l.PulseHz;
            if (_shadowsCheck != null) _shadowsCheck.ButtonPressed = l.ShadowsEnabled;
        }

        UpdateColorPreview();
        UpdateEditAreaVisibility();
        _suppressChangeEvents = false;
    }

    private void ApplyFromUi()
    {
        if (_suppressChangeEvents || Map == null) return;

        // Dismiss any stale save-asset status the moment the user starts
        // editing again — otherwise a "✓ guardado" or "⚠ poné un nombre"
        // could linger indefinitely.
        if (_saveStatusLabel != null) _saveStatusLabel.Visible = false;

        if (SelectedLightIndex >= 0 && SelectedLightIndex < Map.LightData.Lights.Count)
        {
            var l = Map.LightData.Lights[SelectedLightIndex];
            l.Type = ParseTypeFromUi();
            l.R = (int)(_rSpin?.Value ?? 255);
            l.G = (int)(_gSpin?.Value ?? 220);
            l.B = (int)(_bSpin?.Value ?? 180);
            l.Energy = (float)(_energySpin?.Value ?? 1.0);
            l.Radius = (float)(_radiusSpin?.Value ?? 6.0);
            l.DirectionDeg = (float)(_directionSpin?.Value ?? 0.0);
            l.ConeDegrees = (float)(_coneSpin?.Value ?? 60.0);
            l.FlickerPct = (int)(_flickerSpin?.Value ?? 0);
            l.PulseHz = (float)(_pulseSpin?.Value ?? 0.0);
            l.ShadowsEnabled = _shadowsCheck?.ButtonPressed ?? true;
        }
        UpdateColorPreview();
        OnChanged?.Invoke();
    }

    private LightType ParseTypeFromUi()
    {
        if (_typeOption == null) return LightType.Omni;
        int sel = _typeOption.Selected;
        if (sel < 0) return LightType.Omni;
        return (LightType)sel;
    }

    private void UpdateColorPreview()
    {
        if (_colorPreview == null || _rSpin == null || _gSpin == null || _bSpin == null) return;
        _colorPreview.Color = new Color(
            (float)_rSpin.Value / 255f,
            (float)_gSpin.Value / 255f,
            (float)_bSpin.Value / 255f);
    }

    /// <summary>Show/hide the editing area as a whole and toggle the
    /// direction/cone rows based on the currently selected light type.</summary>
    private void UpdateEditAreaVisibility()
    {
        bool hasSelection = Map != null
            && SelectedLightIndex >= 0
            && SelectedLightIndex < Map.LightData.Lights.Count;

        if (_editArea != null) _editArea.Visible = hasSelection;
        if (_emptyHintLabel != null) _emptyHintLabel.Visible = !hasSelection;
        if (_deleteLightBtn != null) _deleteLightBtn.Disabled = !hasSelection;

        if (!hasSelection) return;

        var type = ParseTypeFromUi();
        if (_directionRow != null) _directionRow.Visible = type != LightType.Omni;
        if (_coneRow != null) _coneRow.Visible = type == LightType.Spot;
    }

    // ── Helpers ───────────────────────────────────────────────────────

    private static void AddIntSpin(HBoxContainer parent, string label, int min, int max, int step, int val, out SpinBox spin)
    {
        var lbl = EditorTheme.MakeLabel(label, EditorTheme.TEXT_SECONDARY, EditorTheme.FONT_SM);
        parent.AddChild(lbl);
        spin = new SpinBox
        {
            MinValue = min,
            MaxValue = max,
            Step = step,
            Value = val,
            CustomMinimumSize = new Vector2(64, 0),
        };
        parent.AddChild(spin);
    }

    private static void AddFloatSpin(HBoxContainer parent, string label, double min, double max, double step, double val, out SpinBox spin)
    {
        var lbl = EditorTheme.MakeLabel(label, EditorTheme.TEXT_SECONDARY, EditorTheme.FONT_SM);
        parent.AddChild(lbl);
        spin = new SpinBox
        {
            MinValue = min,
            MaxValue = max,
            Step = step,
            Value = val,
            CustomMinimumSize = new Vector2(72, 0),
        };
        parent.AddChild(spin);
    }
}
