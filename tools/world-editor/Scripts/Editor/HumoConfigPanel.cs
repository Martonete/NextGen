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
    private Button? _newLayerBtn;
    private LineEdit? _newLayerNameEdit;
    private SpinBox? _densitySpin;
    private SpinBox? _sizeSpin;
    private SpinBox? _brushSpin;
    private Label? _brushHelpLabel;
    /// <summary>Brush radius in tiles — read by MapViewport when painting.
    /// 0 = single tile per click; 1+ = paint a circle of that radius.
    /// Range: 0..30. Diameter = 2 * radius + 1 tiles.</summary>
    public int BrushRadius => (int)(_brushSpin?.Value ?? 0);

    // ── Cloud prefab section ──
    private OptionButton? _cloudOption;
    private LineEdit? _cloudNameEdit;
    private Button? _saveCloudBtn;
    private Button? _stampModeBtn;
    private CheckBox? _randomRotateCheck;
    private Label? _cloudStatusLabel;
    /// <summary>When true, each cloud stamp gets a random orientation
    /// (one of 4 cardinal rotations + optional horizontal flip = 8 total
    /// variants). Keeps the same shape but breaks visual repetition.</summary>
    public bool RandomRotateStamps => _randomRotateCheck?.ButtonPressed ?? true;
    /// <summary>When > -1, the Fog tool stamps the selected cloud prefab at
    /// each click instead of placing a single tile. Index into the combined
    /// BuiltIn + Map.UserCloudPrefabs list. MapViewport reads this every
    /// paint to decide between brush-radius and cloud-stamp mode.</summary>
    public int StampCloudIndex { get; private set; } = -1;
    /// <summary>Resolves StampCloudIndex into a CloudPrefab reference.
    /// Returns null if no stamp mode is active.</summary>
    public CloudPrefab? GetActiveStampCloud()
    {
        if (StampCloudIndex < 0 || Map == null) return null;
        if (StampCloudIndex < CloudPrefab.BuiltIn.Count)
            return CloudPrefab.BuiltIn[StampCloudIndex];
        int userIdx = StampCloudIndex - CloudPrefab.BuiltIn.Count;
        if (userIdx >= 0 && userIdx < Map.UserCloudPrefabs.Count)
            return Map.UserCloudPrefabs[userIdx];
        return null;
    }
    private SpinBox? _rSpin, _gSpin, _bSpin;
    private ColorRect? _colorPreview;
    private CheckBox? _freeSmokeCheck;
    private Label? _saveStatusLabel;

    // Entry types in the dropdown
    private enum EntryType { CurrentLayer, BuiltInPrefab, UserPrefab }
    private readonly List<(EntryType type, int index)> _dropdownEntries = new();
    private bool _suppressChangeEvents;

    public override void _Ready()
    {
        CustomMinimumSize = new Vector2(260, 0);
        // Fill the sidebar vertically so the inner ScrollContainer has
        // a real height to scroll within. Without this the PanelContainer
        // shrinks to its content's minimum size and the ScrollContainer
        // can't show scrollbars (see LightToolPanel for the failure mode).
        SizeFlagsVertical = SizeFlags.ExpandFill;
        AddThemeStyleboxOverride("panel", EditorTheme.FlatBox(EditorTheme.BG_PANEL, 4, 8, 6, EditorTheme.BORDER, 1));

        // ScrollContainer so the panel can grow taller than the sidebar
        // viewport — needed now that the cloud prefab section adds many
        // more controls. Vertical scroll only; horizontal disabled.
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

        vbox.AddChild(EditorTheme.Heading("Humo (pintar)"));
        var help = EditorTheme.MakeLabel(
            "Creá varias capas para mezclar colores en el mismo lugar — cada capa es independiente.",
            EditorTheme.TEXT_SECONDARY, EditorTheme.FONT_SM);
        help.AutowrapMode = TextServer.AutowrapMode.Word;
        vbox.AddChild(help);

        // --- New layer row ---
        vbox.AddChild(EditorTheme.MakeLabel("Nueva capa:", EditorTheme.TEXT_SECONDARY, EditorTheme.FONT_SM));
        var newLayerRow = new HBoxContainer();
        newLayerRow.AddThemeConstantOverride("separation", 4);
        _newLayerNameEdit = new LineEdit
        {
            PlaceholderText = "Nombre (ej: Humo rojo)...",
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
        };
        newLayerRow.AddChild(_newLayerNameEdit);
        _newLayerBtn = new Button { Text = "+ Crear" };
        _newLayerBtn.Pressed += OnCreateNewLayer;
        newLayerRow.AddChild(_newLayerBtn);
        vbox.AddChild(newLayerRow);

        vbox.AddChild(new HSeparator());

        // --- Current layer / prefab picker ---
        vbox.AddChild(EditorTheme.MakeLabel("Capa activa / copiar desde prefab:", EditorTheme.TEXT_SECONDARY, EditorTheme.FONT_SM));
        _layerOption = new OptionButton();
        _layerOption.CustomMinimumSize = new Vector2(0, 24);
        vbox.AddChild(_layerOption);
        _layerOption.ItemSelected += OnDropdownSelected;

        _deleteLayerBtn = new Button { Text = "🗑 Eliminar capa actual" };
        _deleteLayerBtn.Pressed += OnDeleteCurrentLayer;
        vbox.AddChild(_deleteLayerBtn);

        // Save-as-asset section — made more prominent so users find it easily.
        vbox.AddChild(new HSeparator());
        vbox.AddChild(EditorTheme.MakeLabel(
            "Guardar como asset reutilizable:",
            EditorTheme.TEXT_SECONDARY, EditorTheme.FONT_SM));
        var saveRow = new HBoxContainer();
        saveRow.AddThemeConstantOverride("separation", 4);
        _newPrefabNameEdit = new LineEdit
        {
            PlaceholderText = "Nombre del asset (ej: Humo rojo denso)...",
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
        };
        saveRow.AddChild(_newPrefabNameEdit);
        _savePrefabBtn = new Button { Text = "💾 Guardar" };
        _savePrefabBtn.Pressed += OnSavePrefab;
        saveRow.AddChild(_savePrefabBtn);
        vbox.AddChild(saveRow);

        _saveStatusLabel = EditorTheme.MakeLabel("", EditorTheme.TEXT_SECONDARY, EditorTheme.FONT_SM);
        _saveStatusLabel.Visible = false;
        vbox.AddChild(_saveStatusLabel);

        // ── Cloud Prefab section ──
        vbox.AddChild(new HSeparator());
        vbox.AddChild(EditorTheme.MakeLabel(
            "☁ Nubes prefabricadas:",
            EditorTheme.TEXT_SECONDARY, EditorTheme.FONT_SM));
        var cloudHelp = EditorTheme.MakeLabel(
            "Dibujá una nube en el mapa (con esta capa), ponele nombre y guardala. Después pickeala del dropdown + 🖨 Modo Stamp para pegarla en otros lugares con un click.",
            EditorTheme.TEXT_MUTED, EditorTheme.FONT_SM);
        cloudHelp.AutowrapMode = TextServer.AutowrapMode.Word;
        vbox.AddChild(cloudHelp);

        _cloudOption = new OptionButton();
        _cloudOption.CustomMinimumSize = new Vector2(0, 24);
        vbox.AddChild(_cloudOption);
        _cloudOption.ItemSelected += (_) => UpdateStampButton();

        var cloudSaveRow = new HBoxContainer();
        cloudSaveRow.AddThemeConstantOverride("separation", 4);
        _cloudNameEdit = new LineEdit
        {
            PlaceholderText = "Nombre (ej: Niebla DV)...",
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
        };
        cloudSaveRow.AddChild(_cloudNameEdit);
        _saveCloudBtn = new Button { Text = "☁+ Guardar" };
        _saveCloudBtn.Pressed += OnSaveCloud;
        cloudSaveRow.AddChild(_saveCloudBtn);
        vbox.AddChild(cloudSaveRow);

        _stampModeBtn = new Button
        {
            Text = "🖨 Modo Stamp: OFF",
            ToggleMode = true,
        };
        _stampModeBtn.Toggled += OnStampModeToggled;
        vbox.AddChild(_stampModeBtn);

        // Random rotation toggle — when ON, each stamp gets a random
        // orientation (4 cardinal rotations × optional horizontal flip
        // = 8 variants) so repeated stamps don't look identical.
        _randomRotateCheck = new CheckBox
        {
            Text = "🎲 Rotación aleatoria por stamp",
            ButtonPressed = true,
        };
        vbox.AddChild(_randomRotateCheck);

        _cloudStatusLabel = EditorTheme.MakeLabel("", EditorTheme.TEXT_SECONDARY, EditorTheme.FONT_SM);
        _cloudStatusLabel.Visible = false;
        vbox.AddChild(_cloudStatusLabel);

        vbox.AddChild(new HSeparator());

        // Density
        var densRow = new HBoxContainer();
        densRow.AddThemeConstantOverride("separation", 6);
        AddSpin(densRow, "Densidad:", 0, 255, 160, out _densitySpin);
        vbox.AddChild(densRow);

        // Size (noise cell in world pixels) — controls how big the smoke
        // clouds look. Step 16 gives fine control without feeling too jittery.
        // Max raised to 8192 so users can make REALLY big cloud formations.
        var sizeRow = new HBoxContainer();
        sizeRow.AddThemeConstantOverride("separation", 6);
        AddSpin(sizeRow, "Tamaño:", 64, 8192, 512, out _sizeSpin);
        _sizeSpin!.Step = 16;
        vbox.AddChild(sizeRow);
        var sizeHelp = EditorTheme.MakeLabel(
            "↑ más grande = nubes más amplias (hasta 8192)  |  ↓ más chico = denso/granular",
            EditorTheme.TEXT_SECONDARY, EditorTheme.FONT_SM);
        sizeHelp.AutowrapMode = TextServer.AutowrapMode.Word;
        vbox.AddChild(sizeHelp);

        // Brush radius: how many tiles get painted per click. 0 = single
        // tile. N = N-tile radius (circle) = (2N+1) tile diameter.
        // Max raised to 30 so users can paint very wide strokes (61-tile
        // diameter) for huge fog areas in a single drag.
        var brushRow = new HBoxContainer();
        brushRow.AddThemeConstantOverride("separation", 6);
        AddSpin(brushRow, "Ancho pincel:", 0, 30, 0, out _brushSpin);
        vbox.AddChild(brushRow);
        _brushHelpLabel = EditorTheme.MakeLabel(
            "0 = 1 tile por click  ·  pincel actual: 1×1 tiles",
            EditorTheme.TEXT_SECONDARY, EditorTheme.FONT_SM);
        _brushHelpLabel.AutowrapMode = TextServer.AutowrapMode.Word;
        vbox.AddChild(_brushHelpLabel);
        // Update the diameter label live as the user moves the spinner.
        _brushSpin!.ValueChanged += (_) => UpdateBrushHelpLabel();

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
        _sizeSpin!.ValueChanged += (_) => ApplyFromUi();
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
        RebuildCloudDropdown();
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

    /// <summary>Create a brand-new empty layer with the given name and
    /// default style (grey niebla). Users then paint tiles into it and
    /// tweak its color/density via the sliders — independently of any
    /// other existing layer.</summary>
    private void OnCreateNewLayer()
    {
        if (Map == null) return;
        string name = _newLayerNameEdit?.Text?.Trim() ?? "";
        if (string.IsNullOrEmpty(name)) name = $"Humo {Map.PaintedFogLayers.Count + 1}";

        // Seed the new layer from the CURRENT UI values so users can
        // pre-configure a color in the sliders and click Crear to
        // "stamp" that style as a new independent layer.
        var layer = new PaintedFogLayer
        {
            Name = name,
            Density = (int)(_densitySpin?.Value ?? 160),
            R = (int)(_rSpin?.Value ?? 128),
            G = (int)(_gSpin?.Value ?? 140),
            B = (int)(_bSpin?.Value ?? 160),
            SpeedX = 5,
            SpeedY = 2,
            Size = (int)(_sizeSpin?.Value ?? 512),
            RandomSeed = PaintedFogLayer.NewRandomSeed(),
        };
        Map.PaintedFogLayers.Add(layer);
        Map.ActiveFogLayerIndex = Map.PaintedFogLayers.Count - 1;

        if (_newLayerNameEdit != null) _newLayerNameEdit.Text = "";
        GD.Print($"[HumoConfigPanel] Created new layer '{name}' (total layers: {Map.PaintedFogLayers.Count})");
        RebuildDropdown();
        LoadActiveLayerIntoUi();
        OnChanged?.Invoke();
    }

    private void OnSavePrefab()
    {
        if (Map == null) return;
        // Clear any stale status from a previous attempt before we report
        // the outcome of this one.
        if (_saveStatusLabel != null) _saveStatusLabel.Visible = false;
        string name = _newPrefabNameEdit?.Text?.Trim() ?? "";
        if (string.IsNullOrEmpty(name))
        {
            ShowSaveStatus("⚠ Poné un nombre para el asset", false);
            return;
        }
        var prefab = new SmokePrefab
        {
            Name = name,
            Density = (int)(_densitySpin?.Value ?? 160),
            R = (int)(_rSpin?.Value ?? 128),
            G = (int)(_gSpin?.Value ?? 140),
            B = (int)(_bSpin?.Value ?? 160),
            SpeedX = 5, SpeedY = 2,
            Size = (int)(_sizeSpin?.Value ?? 512),
        };
        Map.UserFogPrefabs.Add(prefab);
        if (_newPrefabNameEdit != null) _newPrefabNameEdit.Text = "";
        RebuildDropdown();
        ShowSaveStatus($"✓ Asset '{name}' guardado — ya disponible en el dropdown", true);
        GD.Print($"[HumoConfigPanel] Saved asset '{name}'");
    }

    private void ShowSaveStatus(string text, bool success)
    {
        if (_saveStatusLabel == null) return;
        _saveStatusLabel.Text = text;
        _saveStatusLabel.Visible = true;
        _saveStatusLabel.AddThemeColorOverride("font_color",
            success ? new Color(0.5f, 0.9f, 0.5f) : new Color(0.95f, 0.7f, 0.4f));
    }

    private void LoadActiveLayerIntoUi()
    {
        if (Map == null || _densitySpin == null) return;
        _suppressChangeEvents = true;
        if (Map.ActiveFogLayerIndex >= 0 && Map.ActiveFogLayerIndex < Map.PaintedFogLayers.Count)
        {
            var l = Map.PaintedFogLayers[Map.ActiveFogLayerIndex];
            _densitySpin.Value = l.Density;
            if (_sizeSpin != null) _sizeSpin.Value = l.Size > 0 ? l.Size : 512;
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

        // Dismiss any stale save-asset status message the moment the user
        // starts editing again — otherwise a "✓ Asset guardado" or
        // "⚠ Poné un nombre" could linger indefinitely.
        if (_saveStatusLabel != null) _saveStatusLabel.Visible = false;

        // free_smoke is the only global on humo (affects all layers + zone)
        Map.FogFreeSmoke = _freeSmokeCheck?.ButtonPressed ?? false;

        // Sliders edit the currently active layer's style ONLY.
        if (Map.ActiveFogLayerIndex >= 0 && Map.ActiveFogLayerIndex < Map.PaintedFogLayers.Count)
        {
            var l = Map.PaintedFogLayers[Map.ActiveFogLayerIndex];
            l.Density = (int)(_densitySpin?.Value ?? 160);
            l.Size = (int)(_sizeSpin?.Value ?? 512);
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

    // ── Cloud Prefab handlers ──

    /// <summary>Rebuild the cloud dropdown. Call this after saving a new
    /// cloud, deleting one, or switching maps.</summary>
    public void RebuildCloudDropdown()
    {
        if (_cloudOption == null) return;
        _cloudOption.Clear();
        _cloudOption.AddItem("(ninguna)");
        // Built-in presets first
        foreach (var bc in CloudPrefab.BuiltIn)
            _cloudOption.AddItem($"◆ {bc.Name} ({bc.RelativeTiles.Count} tiles)");
        // User-saved on this map
        if (Map != null)
            foreach (var uc in Map.UserCloudPrefabs)
                _cloudOption.AddItem($"✦ {uc.Name} ({uc.RelativeTiles.Count} tiles)");

        // Re-sync selected
        int wanted = StampCloudIndex + 1; // +1 for "(ninguna)"
        if (wanted > 0 && wanted < _cloudOption.ItemCount)
            _cloudOption.Select(wanted);
        else
            _cloudOption.Select(0);
    }

    /// <summary>Save the CURRENTLY active fog layer as a named cloud prefab.
    /// Captures its style + all its tiles as relative offsets (centered on
    /// the layer's centroid). Empty name or no tiles = warning and abort.</summary>
    private void OnSaveCloud()
    {
        if (Map == null) return;
        if (_cloudStatusLabel != null) _cloudStatusLabel.Visible = false;
        string name = _cloudNameEdit?.Text?.Trim() ?? "";
        if (string.IsNullOrEmpty(name))
        {
            ShowCloudStatus("⚠ Poné un nombre", false);
            return;
        }
        if (Map.ActiveFogLayerIndex < 0 || Map.ActiveFogLayerIndex >= Map.PaintedFogLayers.Count)
        {
            ShowCloudStatus("⚠ No hay capa activa", false);
            return;
        }
        var src = Map.PaintedFogLayers[Map.ActiveFogLayerIndex];
        if (src.Tiles.Count == 0)
        {
            ShowCloudStatus("⚠ La capa activa no tiene tiles pintados", false);
            return;
        }

        // Compute centroid to anchor the relative offsets.
        int sumX = 0, sumY = 0;
        foreach (var t in src.Tiles) { sumX += t.X; sumY += t.Y; }
        int cx = sumX / src.Tiles.Count;
        int cy = sumY / src.Tiles.Count;

        var cloud = new CloudPrefab
        {
            Name = name,
            Density = src.Density, R = src.R, G = src.G, B = src.B,
            SpeedX = src.SpeedX, SpeedY = src.SpeedY, Size = src.Size,
        };
        foreach (var t in src.Tiles)
            cloud.RelativeTiles.Add(new Godot.Vector2I(t.X - cx, t.Y - cy));

        Map.UserCloudPrefabs.Add(cloud);
        if (_cloudNameEdit != null) _cloudNameEdit.Text = "";
        RebuildCloudDropdown();
        ShowCloudStatus($"✓ Nube '{name}' guardada ({cloud.RelativeTiles.Count} tiles)", true);
    }

    private void OnStampModeToggled(bool on)
    {
        if (_stampModeBtn != null)
            _stampModeBtn.Text = on ? "🖨 Modo Stamp: ON" : "🖨 Modo Stamp: OFF";
        UpdateStampButton();
    }

    /// <summary>Updates StampCloudIndex based on dropdown selection + toggle.</summary>
    private void UpdateStampButton()
    {
        if (_cloudOption == null || _stampModeBtn == null)
        {
            StampCloudIndex = -1;
            return;
        }
        if (!_stampModeBtn.ButtonPressed || _cloudOption.Selected <= 0)
        {
            StampCloudIndex = -1;
            return;
        }
        // dropdown[0] = "(ninguna)" sentinel, so subtract 1.
        StampCloudIndex = _cloudOption.Selected - 1;
    }

    /// <summary>Update the live brush diameter label so the user can see
    /// the effective tile coverage of the current brush radius without
    /// doing the math themselves. Diameter = 2 × radius + 1.</summary>
    private void UpdateBrushHelpLabel()
    {
        if (_brushHelpLabel == null) return;
        int r = BrushRadius;
        int diameter = 2 * r + 1;
        string suffix = r == 0
            ? "0 = 1 tile por click"
            : $"radio {r} → círculo de {diameter}×{diameter} tiles";
        _brushHelpLabel.Text = $"{suffix}  ·  pincel actual: {diameter}×{diameter} tiles";
    }

    private void ShowCloudStatus(string text, bool ok)
    {
        if (_cloudStatusLabel == null) return;
        _cloudStatusLabel.Text = text;
        _cloudStatusLabel.Visible = true;
        _cloudStatusLabel.AddThemeColorOverride("font_color",
            ok ? new Color(0.5f, 0.9f, 0.5f) : new Color(0.95f, 0.7f, 0.4f));
    }
}
