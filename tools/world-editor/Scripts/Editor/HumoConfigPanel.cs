#nullable enable
using System;
using System.Collections.Generic;
using AOWorldEditor.Data;
using Godot;

namespace AOWorldEditor.Editor;

/// <summary>
/// Humo tool sidebar panel.
///
/// The dropdown at the top has three sections:
///  - Current layers (★) — one entry per painted layer that already
///    exists on the map. Selecting one makes it the active layer; edits
///    to the sliders modify ONLY that layer's style.
///  - Built-in prefabs (◆) — templates. Selecting one creates a NEW
///    empty layer seeded from the template.
///  - User prefabs (✦) — same as built-ins but user-saved.
///
/// This way the user can paint red smoke, then create a new layer from
/// "Niebla azul" and paint blue smoke — both colors coexist independently.
/// Editing one doesn't affect the other.
/// </summary>
public partial class HumoConfigPanel : PanelContainer
{
    public MapData? Map;
    public Action? OnChanged;

    private OptionButton? _layerOption;
    private LineEdit? _newPrefabNameEdit;
    private Button? _savePrefabBtn;
    private Button? _deleteLayerBtn;
    private SpinBox? _densitySpin;
    private SpinBox? _rSpin, _gSpin, _bSpin;
    private ColorRect? _colorPreview;
    private CheckBox? _freeSmokeCheck;

    // Entry types in the dropdown
    private enum EntryType { CurrentLayer, BuiltInPrefab, UserPrefab }
    private readonly List<(EntryType type, int index)> _dropdownEntries = new();
    private bool _suppressChangeEvents;

    public override void _Ready()
    {
        CustomMinimumSize = new Vector2(260, 0);
        AddThemeStyleboxOverride("panel", EditorTheme.FlatBox(EditorTheme.BG_PANEL, 4, 8, 6, EditorTheme.BORDER, 1));

        var vbox = new VBoxContainer();
        vbox.AddThemeConstantOverride("separation", 6);
        AddChild(vbox);

        vbox.AddChild(EditorTheme.Heading("Humo (pintar)"));
        var help = EditorTheme.MakeLabel("Click izq: pintar.  Click der: borrar.  Cada capa es independiente.", EditorTheme.TEXT_SECONDARY, EditorTheme.FONT_SM);
        help.AutowrapMode = TextServer.AutowrapMode.Word;
        vbox.AddChild(help);

        vbox.AddChild(EditorTheme.MakeLabel("Capa / prefab:", EditorTheme.TEXT_SECONDARY, EditorTheme.FONT_SM));
        _layerOption = new OptionButton();
        _layerOption.CustomMinimumSize = new Vector2(0, 24);
        vbox.AddChild(_layerOption);
        _layerOption.ItemSelected += OnDropdownSelected;

        // Delete current layer button
        _deleteLayerBtn = new Button { Text = "🗑 Eliminar capa actual" };
        _deleteLayerBtn.Pressed += OnDeleteCurrentLayer;
        vbox.AddChild(_deleteLayerBtn);

        // Save-as-prefab row
        var saveRow = new HBoxContainer();
        saveRow.AddThemeConstantOverride("separation", 4);
        _newPrefabNameEdit = new LineEdit
        {
            PlaceholderText = "Guardar estilo como prefab...",
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
        };
        saveRow.AddChild(_newPrefabNameEdit);
        _savePrefabBtn = new Button { Text = "+" };
        _savePrefabBtn.Pressed += OnSavePrefab;
        saveRow.AddChild(_savePrefabBtn);
        vbox.AddChild(saveRow);

        vbox.AddChild(new HSeparator());

        // Density
        var densRow = new HBoxContainer();
        densRow.AddThemeConstantOverride("separation", 6);
        AddSpin(densRow, "Densidad:", 0, 255, 160, out _densitySpin);
        vbox.AddChild(densRow);

        vbox.AddChild(EditorTheme.MakeLabel("Color del humo:", EditorTheme.TEXT_SECONDARY, EditorTheme.FONT_SM));
        var colRow = new HBoxContainer();
        colRow.AddThemeConstantOverride("separation", 4);
        AddSpin(colRow, "R:", 0, 255, 128, out _rSpin);
        AddSpin(colRow, "G:", 0, 255, 140, out _gSpin);
        AddSpin(colRow, "B:", 0, 255, 160, out _bSpin);
        vbox.AddChild(colRow);

        _colorPreview = new ColorRect
        {
            CustomMinimumSize = new Vector2(0, 28),
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            Color = new Color(128 / 255f, 140 / 255f, 160 / 255f),
        };
        vbox.AddChild(_colorPreview);

        _freeSmokeCheck = new CheckBox
        {
            Text = "Humo libre (flota en su lugar)",
            ButtonPressed = false,
        };
        _freeSmokeCheck.Toggled += (_) => ApplyFromUi();
        vbox.AddChild(_freeSmokeCheck);

        _densitySpin!.ValueChanged += (_) => ApplyFromUi();
        _rSpin!.ValueChanged += (_) => ApplyFromUi();
        _gSpin!.ValueChanged += (_) => ApplyFromUi();
        _bSpin!.ValueChanged += (_) => ApplyFromUi();
    }

    public void RefreshFromMap()
    {
        if (Map == null) return;
        _suppressChangeEvents = true;
        if (_freeSmokeCheck != null) _freeSmokeCheck.ButtonPressed = Map.FogFreeSmoke;
        _suppressChangeEvents = false;
        RebuildDropdown();
        LoadActiveLayerIntoUi();
    }

    /// <summary>Rebuild the dropdown — current layers first, then prefabs as templates.</summary>
    public void RebuildDropdown()
    {
        if (_layerOption == null || Map == null) return;
        _suppressChangeEvents = true;
        _layerOption.Clear();
        _dropdownEntries.Clear();

        // Current layers
        for (int i = 0; i < Map.PaintedFogLayers.Count; i++)
        {
            var l = Map.PaintedFogLayers[i];
            _layerOption.AddItem($"★ {l.Name}  ({l.Tiles.Count} tiles)");
            _dropdownEntries.Add((EntryType.CurrentLayer, i));
        }
        if (Map.PaintedFogLayers.Count > 0)
            _layerOption.AddSeparator("— Nuevo desde prefab —");

        // Built-in prefabs
        for (int i = 0; i < SmokePrefab.BuiltIn.Count; i++)
        {
            _layerOption.AddItem($"◆ {SmokePrefab.BuiltIn[i].Name}");
            _dropdownEntries.Add((EntryType.BuiltInPrefab, i));
        }

        // User prefabs
        for (int i = 0; i < Map.UserFogPrefabs.Count; i++)
        {
            _layerOption.AddItem($"✦ {Map.UserFogPrefabs[i].Name}");
            _dropdownEntries.Add((EntryType.UserPrefab, i));
        }

        // Select the active layer (if any)
        int selIdx = 0;
        if (Map.ActiveFogLayerIndex >= 0 && Map.ActiveFogLayerIndex < Map.PaintedFogLayers.Count)
            selIdx = Map.ActiveFogLayerIndex;
        if (_layerOption.ItemCount > 0) _layerOption.Select(selIdx);
        _suppressChangeEvents = false;
    }

    private void OnDropdownSelected(long index)
    {
        if (_suppressChangeEvents || Map == null) return;
        // Separators don't have valid indices into _dropdownEntries — they're
        // skipped in the mapping. But OptionButton fires ItemSelected for them
        // sometimes. Guard with the UI index carefully by iterating entries.
        if (index < 0) return;

        // Map OptionButton index to our entry index (skipping separators).
        // The dropdown visual order:
        //   [current layers 0..N-1] [separator?] [builtins] [user prefabs]
        // Our _dropdownEntries matches the list WITHOUT separators.
        int entryIdx = (int)index;
        if (Map.PaintedFogLayers.Count > 0 && entryIdx > Map.PaintedFogLayers.Count)
            entryIdx -= 1; // account for the separator
        if (entryIdx < 0 || entryIdx >= _dropdownEntries.Count) return;

        var (type, dataIdx) = _dropdownEntries[entryIdx];
        switch (type)
        {
            case EntryType.CurrentLayer:
                Map.ActiveFogLayerIndex = dataIdx;
                LoadActiveLayerIntoUi();
                break;
            case EntryType.BuiltInPrefab:
                CreateLayerFromPrefab(SmokePrefab.BuiltIn[dataIdx]);
                break;
            case EntryType.UserPrefab:
                CreateLayerFromPrefab(Map.UserFogPrefabs[dataIdx]);
                break;
        }
    }

    private void CreateLayerFromPrefab(SmokePrefab template)
    {
        if (Map == null) return;
        var layer = PaintedFogLayer.FromPrefab(template);
        Map.PaintedFogLayers.Add(layer);
        Map.ActiveFogLayerIndex = Map.PaintedFogLayers.Count - 1;
        GD.Print($"[HumoConfigPanel] New layer '{layer.Name}' from prefab");
        RebuildDropdown();
        LoadActiveLayerIntoUi();
        OnChanged?.Invoke();
    }

    private void OnDeleteCurrentLayer()
    {
        if (Map == null || Map.ActiveFogLayerIndex < 0 || Map.ActiveFogLayerIndex >= Map.PaintedFogLayers.Count) return;
        Map.PaintedFogLayers.RemoveAt(Map.ActiveFogLayerIndex);
        Map.ActiveFogLayerIndex = Map.PaintedFogLayers.Count > 0 ? 0 : -1;
        RebuildDropdown();
        LoadActiveLayerIntoUi();
        OnChanged?.Invoke();
    }

    private void OnSavePrefab()
    {
        if (Map == null) return;
        string name = _newPrefabNameEdit?.Text?.Trim() ?? "";
        if (string.IsNullOrEmpty(name)) return;
        var prefab = new SmokePrefab
        {
            Name = name,
            Density = (int)(_densitySpin?.Value ?? 160),
            R = (int)(_rSpin?.Value ?? 128),
            G = (int)(_gSpin?.Value ?? 140),
            B = (int)(_bSpin?.Value ?? 160),
            SpeedX = 5, SpeedY = 2,
        };
        Map.UserFogPrefabs.Add(prefab);
        if (_newPrefabNameEdit != null) _newPrefabNameEdit.Text = "";
        RebuildDropdown();
        GD.Print($"[HumoConfigPanel] Saved prefab '{name}'");
    }

    private void LoadActiveLayerIntoUi()
    {
        if (Map == null || _densitySpin == null) return;
        _suppressChangeEvents = true;
        if (Map.ActiveFogLayerIndex >= 0 && Map.ActiveFogLayerIndex < Map.PaintedFogLayers.Count)
        {
            var l = Map.PaintedFogLayers[Map.ActiveFogLayerIndex];
            _densitySpin.Value = l.Density;
            _rSpin!.Value = l.R;
            _gSpin!.Value = l.G;
            _bSpin!.Value = l.B;
        }
        UpdateColorPreview();
        _suppressChangeEvents = false;
    }

    private void ApplyFromUi()
    {
        if (_suppressChangeEvents || Map == null) return;

        // free_smoke is the only global on humo (affects all layers + zone)
        Map.FogFreeSmoke = _freeSmokeCheck?.ButtonPressed ?? false;

        // Sliders edit the currently active layer's style ONLY.
        if (Map.ActiveFogLayerIndex >= 0 && Map.ActiveFogLayerIndex < Map.PaintedFogLayers.Count)
        {
            var l = Map.PaintedFogLayers[Map.ActiveFogLayerIndex];
            l.Density = (int)(_densitySpin?.Value ?? 160);
            l.R = (int)(_rSpin?.Value ?? 128);
            l.G = (int)(_gSpin?.Value ?? 140);
            l.B = (int)(_bSpin?.Value ?? 160);
        }
        UpdateColorPreview();
        OnChanged?.Invoke();
    }

    private void UpdateColorPreview()
    {
        if (_colorPreview == null || _rSpin == null || _gSpin == null || _bSpin == null) return;
        _colorPreview.Color = new Color(
            (float)_rSpin.Value / 255f,
            (float)_gSpin.Value / 255f,
            (float)_bSpin.Value / 255f);
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
