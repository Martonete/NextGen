#nullable enable
using System;
using System.Collections.Generic;
using AOWorldEditor.Data;
using Godot;

namespace AOWorldEditor.Editor;

/// <summary>
/// Sidebar panel shown when the Humo tool is active. Lets users pick a
/// named smoke prefab, tweak density/color manually, or save the current
/// style as a new custom prefab. Built-in prefabs are always available;
/// user-defined prefabs are stored per-map in MapData.UserFogPrefabs.
/// </summary>
public partial class HumoConfigPanel : PanelContainer
{
    public MapData? Map;
    public Action? OnChanged;

    private OptionButton? _prefabOption;
    private LineEdit? _newPrefabNameEdit;
    private Button? _savePrefabBtn;
    private SpinBox? _densitySpin;
    private SpinBox? _rSpin, _gSpin, _bSpin;
    private ColorRect? _colorPreview;

    // Snapshots of the current dropdown contents: built-ins first, then user prefabs.
    private readonly List<SmokePrefab> _currentPrefabs = new();
    private bool _suppressChangeEvents;

    public override void _Ready()
    {
        CustomMinimumSize = new Vector2(260, 0);
        AddThemeStyleboxOverride("panel", EditorTheme.FlatBox(EditorTheme.BG_PANEL, 4, 8, 6, EditorTheme.BORDER, 1));

        var vbox = new VBoxContainer();
        vbox.AddThemeConstantOverride("separation", 6);
        AddChild(vbox);

        vbox.AddChild(EditorTheme.Heading("Humo (pintar)"));
        var help = EditorTheme.MakeLabel("Click izq: pintar.  Click der: borrar.", EditorTheme.TEXT_SECONDARY, EditorTheme.FONT_SM);
        help.AutowrapMode = TextServer.AutowrapMode.Word;
        vbox.AddChild(help);

        // --- Prefab dropdown ---
        vbox.AddChild(EditorTheme.MakeLabel("Prefabricado:", EditorTheme.TEXT_SECONDARY, EditorTheme.FONT_SM));
        _prefabOption = new OptionButton();
        _prefabOption.CustomMinimumSize = new Vector2(0, 24);
        vbox.AddChild(_prefabOption);
        _prefabOption.ItemSelected += OnPrefabSelected;

        // --- Save-as-prefab row ---
        var saveRow = new HBoxContainer();
        saveRow.AddThemeConstantOverride("separation", 4);
        _newPrefabNameEdit = new LineEdit
        {
            PlaceholderText = "Nombre del prefab...",
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
        };
        saveRow.AddChild(_newPrefabNameEdit);
        _savePrefabBtn = new Button { Text = "+ Guardar" };
        _savePrefabBtn.Pressed += OnSavePrefab;
        saveRow.AddChild(_savePrefabBtn);
        vbox.AddChild(saveRow);

        vbox.AddChild(new HSeparator());

        // --- Density ---
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

        _densitySpin!.ValueChanged += (_) => ApplyFromUi();
        _rSpin!.ValueChanged += (_) => ApplyFromUi();
        _gSpin!.ValueChanged += (_) => ApplyFromUi();
        _bSpin!.ValueChanged += (_) => ApplyFromUi();

        RebuildPrefabDropdown();
    }

    /// <summary>Rebuild the dropdown after a new prefab is added or a map is loaded.</summary>
    public void RebuildPrefabDropdown()
    {
        if (_prefabOption == null) return;
        _suppressChangeEvents = true;
        _prefabOption.Clear();
        _currentPrefabs.Clear();

        // (custom placeholder so users know they can just tweak sliders)
        _prefabOption.AddItem("-- personalizado --");
        _currentPrefabs.Add(null!); // sentinel — index 0 is "no prefab selected"

        foreach (var p in SmokePrefab.BuiltIn)
        {
            _prefabOption.AddItem("◆ " + p.Name);
            _currentPrefabs.Add(p);
        }
        if (Map != null)
        {
            foreach (var p in Map.UserFogPrefabs)
            {
                _prefabOption.AddItem("★ " + p.Name);
                _currentPrefabs.Add(p);
            }
        }
        _prefabOption.Select(0);
        _suppressChangeEvents = false;
    }

    /// <summary>Push current Map state into the UI (called on map load or tool activation).</summary>
    public void RefreshFromMap()
    {
        if (Map == null) return;
        _suppressChangeEvents = true;
        if (_densitySpin != null) _densitySpin.Value = Map.PaintedFogDensity;
        if (_rSpin != null) _rSpin.Value = Map.PaintedFogR;
        if (_gSpin != null) _gSpin.Value = Map.PaintedFogG;
        if (_bSpin != null) _bSpin.Value = Map.PaintedFogB;
        UpdateColorPreview();
        _suppressChangeEvents = false;
        RebuildPrefabDropdown();
    }

    private void OnPrefabSelected(long index)
    {
        if (_suppressChangeEvents) return;
        if (index < 0 || index >= _currentPrefabs.Count) return;
        var p = _currentPrefabs[(int)index];
        if (p == null) return; // "-- personalizado --" — no change
        if (Map == null) return;

        _suppressChangeEvents = true;
        if (_densitySpin != null) _densitySpin.Value = p.Density;
        if (_rSpin != null) _rSpin.Value = p.R;
        if (_gSpin != null) _gSpin.Value = p.G;
        if (_bSpin != null) _bSpin.Value = p.B;
        _suppressChangeEvents = false;
        ApplyFromUi();
    }

    private void OnSavePrefab()
    {
        if (Map == null) return;
        string name = _newPrefabNameEdit?.Text?.Trim() ?? "";
        if (string.IsNullOrEmpty(name))
        {
            GD.Print("[HumoConfigPanel] Save prefab: name is empty");
            return;
        }
        var p = new SmokePrefab
        {
            Name = name,
            Density = (int)(_densitySpin?.Value ?? 160),
            R = (int)(_rSpin?.Value ?? 128),
            G = (int)(_gSpin?.Value ?? 140),
            B = (int)(_bSpin?.Value ?? 160),
            SpeedX = 5,
            SpeedY = 2,
        };
        Map.UserFogPrefabs.Add(p);
        if (_newPrefabNameEdit != null) _newPrefabNameEdit.Text = "";
        RebuildPrefabDropdown();
        GD.Print($"[HumoConfigPanel] Saved prefab '{name}' ({p.R},{p.G},{p.B}) density={p.Density}");
    }

    private void ApplyFromUi()
    {
        if (_suppressChangeEvents) return;
        if (Map == null)
        {
            GD.Print("[HumoConfigPanel] ApplyFromUi: Map is NULL — edit lost");
            return;
        }
        Map.PaintedFogDensity = (int)(_densitySpin?.Value ?? 160);
        Map.PaintedFogR = (int)(_rSpin?.Value ?? 128);
        Map.PaintedFogG = (int)(_gSpin?.Value ?? 140);
        Map.PaintedFogB = (int)(_bSpin?.Value ?? 160);
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
