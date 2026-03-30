#nullable enable
using System;
using Godot;
using AOWorldEditor.Data;

namespace AOWorldEditor.Editor;

/// <summary>
/// Modal popup for editing zone properties: name, type, bounds, restrictions, ambient, spawns.
/// </summary>
public partial class ZoneEditPopup : Window
{
    public ZoneInfo? Zone;
    public MapZoneData? ZoneData;
    public Action? OnSaved;
    /// <summary>Called when user clicks "Seleccionar en mapa" — host should activate Select tool and wire selection back.</summary>
    public Action? OnRequestMapSelect;

    private LineEdit? _nameEdit;
    private OptionButton? _typeOption;
    private VBoxContainer? _spawnListContainer;
    private SpinBox? _x1Spin, _y1Spin, _x2Spin, _y2Spin;
    private CheckBox? _seguraCheck, _newbieCheck, _sinMagiaCheck, _sinInviCheck;
    private CheckBox? _sinMascotasCheck, _sinResucitarCheck, _sinOcultarCheck, _sinInvocarCheck;
    private CheckBox? _combatZoneCheck, _soloClanes, _soloFaccion;
    private SpinBox? _minLevelSpin, _maxLevelSpin;
    private SpinBox? _musicaSpin;
    private CheckBox? _lluviaCheck, _nieveCheck, _nieblaCheck;
    private SpinBox? _ambRSpin, _ambGSpin, _ambBSpin;
    private SpinBox? _salidaMapSpin, _salidaXSpin, _salidaYSpin;

    public override void _Ready()
    {
        Title = Zone != null ? $"Editar Zona: {Zone.Name}" : "Nueva Zona";
        Size = new Vector2I(420, 640);
        Exclusive = true;
        CloseRequested += Hide;

        var bg = new PanelContainer();
        bg.AddThemeStyleboxOverride("panel", EditorTheme.FlatBox(EditorTheme.BG_PANEL, 0, 0, 0));
        bg.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        AddChild(bg);

        var scroll = new ScrollContainer();
        scroll.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        scroll.HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled;
        bg.AddChild(scroll);

        var margin = new MarginContainer();
        margin.AddThemeConstantOverride("margin_left", 16);
        margin.AddThemeConstantOverride("margin_right", 16);
        margin.AddThemeConstantOverride("margin_top", 12);
        margin.AddThemeConstantOverride("margin_bottom", 12);
        margin.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        scroll.AddChild(margin);

        var vbox = new VBoxContainer();
        vbox.AddThemeConstantOverride("separation", 6);
        vbox.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        margin.AddChild(vbox);

        if (Zone == null) { QueueFree(); return; }

        // === Identity ===
        vbox.AddChild(EditorTheme.SectionLabel("Identidad"));
        AddLabeledEdit(vbox, "Nombre:", Zone.Name, out _nameEdit);

        var typeRow = new HBoxContainer();
        typeRow.AddThemeConstantOverride("separation", 8);
        typeRow.AddChild(EditorTheme.MakeLabel("Tipo:", EditorTheme.TEXT_SECONDARY, EditorTheme.FONT_SM));
        _typeOption = new OptionButton();
        _typeOption.AddItem("Neutral", 0);
        _typeOption.AddItem("Segura", 1);
        _typeOption.AddItem("PvP", 2);
        _typeOption.AddItem("Dungeon", 3);
        _typeOption.AddItem("Arena", 4);
        _typeOption.Selected = (int)Zone.Type;
        _typeOption.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        typeRow.AddChild(_typeOption);
        vbox.AddChild(typeRow);

        // === Bounds ===
        vbox.AddChild(EditorTheme.MakeHSeparator());
        vbox.AddChild(EditorTheme.SectionLabel("Bounds (tiles)"));
        var boundsRow1 = new HBoxContainer();
        boundsRow1.AddThemeConstantOverride("separation", 8);
        AddSpinWithLabel(boundsRow1, "X1:", 1, 1000, Zone.X1, out _x1Spin);
        AddSpinWithLabel(boundsRow1, "Y1:", 1, 1000, Zone.Y1, out _y1Spin);
        vbox.AddChild(boundsRow1);
        var boundsRow2 = new HBoxContainer();
        boundsRow2.AddThemeConstantOverride("separation", 8);
        AddSpinWithLabel(boundsRow2, "X2:", 1, 1000, Zone.X2, out _x2Spin);
        AddSpinWithLabel(boundsRow2, "Y2:", 1, 1000, Zone.Y2, out _y2Spin);
        vbox.AddChild(boundsRow2);

        var selectBtn = EditorTheme.MakeButton("Seleccionar en mapa");
        selectBtn.Pressed += () =>
        {
            Hide(); // minimize popup so user can see the map
            OnRequestMapSelect?.Invoke();
        };
        vbox.AddChild(selectBtn);

        // === Restrictions ===
        vbox.AddChild(EditorTheme.MakeHSeparator());
        vbox.AddChild(EditorTheme.SectionLabel("Restricciones"));
        _seguraCheck = AddCheck(vbox, "Zona Segura", Zone.Segura);
        _newbieCheck = AddCheck(vbox, "Newbie (nivel ≤12)", Zone.Newbie);
        _combatZoneCheck = AddCheck(vbox, "Combat Zone (ring)", Zone.CombatZone);
        _sinMagiaCheck = AddCheck(vbox, "Sin Magia", Zone.SinMagia);
        _sinInviCheck = AddCheck(vbox, "Sin Invisibilidad", Zone.SinInvi);
        _sinMascotasCheck = AddCheck(vbox, "Sin Mascotas", Zone.SinMascotas);
        _sinResucitarCheck = AddCheck(vbox, "Sin Resucitar", Zone.SinResucitar);
        _sinOcultarCheck = AddCheck(vbox, "Sin Ocultarse", Zone.SinOcultar);
        _sinInvocarCheck = AddCheck(vbox, "Sin Invocar", Zone.SinInvocar);
        _soloClanes = AddCheck(vbox, "Solo Clanes", Zone.SoloClanes);
        _soloFaccion = AddCheck(vbox, "Solo Faccion", Zone.SoloFaccion);

        // === Level ===
        vbox.AddChild(EditorTheme.MakeHSeparator());
        vbox.AddChild(EditorTheme.SectionLabel("Nivel"));
        var levelRow = new HBoxContainer();
        levelRow.AddThemeConstantOverride("separation", 8);
        AddSpinWithLabel(levelRow, "Min:", 0, 50, Zone.MinLevel, out _minLevelSpin);
        AddSpinWithLabel(levelRow, "Max:", 0, 50, Zone.MaxLevel, out _maxLevelSpin);
        vbox.AddChild(levelRow);

        // === Ambient ===
        vbox.AddChild(EditorTheme.MakeHSeparator());
        vbox.AddChild(EditorTheme.SectionLabel("Ambiente"));
        var musicRow = new HBoxContainer();
        musicRow.AddThemeConstantOverride("separation", 8);
        AddSpinWithLabel(musicRow, "Musica:", 0, 999, Zone.Musica, out _musicaSpin);
        vbox.AddChild(musicRow);

        var weatherRow = new HBoxContainer();
        weatherRow.AddThemeConstantOverride("separation", 12);
        _lluviaCheck = AddCheckInline(weatherRow, "Lluvia", Zone.Lluvia);
        _nieveCheck = AddCheckInline(weatherRow, "Nieve", Zone.Nieve);
        _nieblaCheck = AddCheckInline(weatherRow, "Niebla", Zone.Niebla);
        vbox.AddChild(weatherRow);

        var ambRow = new HBoxContainer();
        ambRow.AddThemeConstantOverride("separation", 4);
        AddSpinWithLabel(ambRow, "R:", 0, 255, Zone.AmbientR, out _ambRSpin);
        AddSpinWithLabel(ambRow, "G:", 0, 255, Zone.AmbientG, out _ambGSpin);
        AddSpinWithLabel(ambRow, "B:", 0, 255, Zone.AmbientB, out _ambBSpin);
        vbox.AddChild(ambRow);

        // === Exit ===
        vbox.AddChild(EditorTheme.MakeHSeparator());
        vbox.AddChild(EditorTheme.SectionLabel("Salida (muerte/expulsion)"));
        var exitRow = new HBoxContainer();
        exitRow.AddThemeConstantOverride("separation", 4);
        AddSpinWithLabel(exitRow, "Mapa:", 0, 999, Zone.SalidaMap, out _salidaMapSpin);
        AddSpinWithLabel(exitRow, "X:", 0, 1000, Zone.SalidaX, out _salidaXSpin);
        AddSpinWithLabel(exitRow, "Y:", 0, 1000, Zone.SalidaY, out _salidaYSpin);
        vbox.AddChild(exitRow);

        // === NPC Spawns ===
        vbox.AddChild(EditorTheme.MakeHSeparator());
        var spawnHeader = new HBoxContainer();
        spawnHeader.AddThemeConstantOverride("separation", 8);
        spawnHeader.AddChild(EditorTheme.SectionLabel("NPC Spawns"));
        var addSpawnBtn = EditorTheme.MakeButton("+ Agregar", OnAddSpawn);
        addSpawnBtn.CustomMinimumSize = new Vector2(80, 24);
        spawnHeader.AddChild(addSpawnBtn);
        vbox.AddChild(spawnHeader);

        _spawnListContainer = new VBoxContainer();
        _spawnListContainer.AddThemeConstantOverride("separation", 4);
        vbox.AddChild(_spawnListContainer);
        RebuildSpawnList();

        // === Buttons ===
        vbox.AddChild(new Control { CustomMinimumSize = new Vector2(0, 8) }); // spacer
        var btnRow = new HBoxContainer();
        btnRow.AddThemeConstantOverride("separation", 8);
        btnRow.Alignment = BoxContainer.AlignmentMode.End;
        var cancelBtn = EditorTheme.MakeButton("Cancelar", () => Hide());
        btnRow.AddChild(cancelBtn);
        var saveBtn = EditorTheme.PrimaryButton("Guardar", OnSave);
        btnRow.AddChild(saveBtn);
        vbox.AddChild(btnRow);
    }

    private void OnSave()
    {
        if (Zone == null) return;

        Zone.Name = _nameEdit?.Text ?? Zone.Name;
        Zone.Type = (ZoneType)(_typeOption?.Selected ?? 0);
        Zone.X1 = (int)(_x1Spin?.Value ?? Zone.X1);
        Zone.Y1 = (int)(_y1Spin?.Value ?? Zone.Y1);
        Zone.X2 = (int)(_x2Spin?.Value ?? Zone.X2);
        Zone.Y2 = (int)(_y2Spin?.Value ?? Zone.Y2);
        Zone.Segura = _seguraCheck?.ButtonPressed ?? false;
        Zone.Newbie = _newbieCheck?.ButtonPressed ?? false;
        Zone.CombatZone = _combatZoneCheck?.ButtonPressed ?? false;
        Zone.SinMagia = _sinMagiaCheck?.ButtonPressed ?? false;
        Zone.SinInvi = _sinInviCheck?.ButtonPressed ?? false;
        Zone.SinMascotas = _sinMascotasCheck?.ButtonPressed ?? false;
        Zone.SinResucitar = _sinResucitarCheck?.ButtonPressed ?? false;
        Zone.SinOcultar = _sinOcultarCheck?.ButtonPressed ?? false;
        Zone.SinInvocar = _sinInvocarCheck?.ButtonPressed ?? false;
        Zone.SoloClanes = _soloClanes?.ButtonPressed ?? false;
        Zone.SoloFaccion = _soloFaccion?.ButtonPressed ?? false;
        Zone.MinLevel = (int)(_minLevelSpin?.Value ?? 0);
        Zone.MaxLevel = (int)(_maxLevelSpin?.Value ?? 0);
        Zone.Musica = (int)(_musicaSpin?.Value ?? 0);
        Zone.Lluvia = _lluviaCheck?.ButtonPressed ?? false;
        Zone.Nieve = _nieveCheck?.ButtonPressed ?? false;
        Zone.Niebla = _nieblaCheck?.ButtonPressed ?? false;
        Zone.AmbientR = (int)(_ambRSpin?.Value ?? 0);
        Zone.AmbientG = (int)(_ambGSpin?.Value ?? 0);
        Zone.AmbientB = (int)(_ambBSpin?.Value ?? 0);
        Zone.SalidaMap = (int)(_salidaMapSpin?.Value ?? 0);
        Zone.SalidaX = (int)(_salidaXSpin?.Value ?? 0);
        Zone.SalidaY = (int)(_salidaYSpin?.Value ?? 0);

        // Auto-set segura based on type
        if (Zone.Type == ZoneType.Safe) Zone.Segura = true;

        OnSaved?.Invoke();
        Hide();
    }

    /// <summary>Update the bounds SpinBoxes from an external selection and re-show the popup.</summary>
    public void SetBoundsFromSelection(int x1, int y1, int x2, int y2)
    {
        if (_x1Spin != null) _x1Spin.Value = x1;
        if (_y1Spin != null) _y1Spin.Value = y1;
        if (_x2Spin != null) _x2Spin.Value = x2;
        if (_y2Spin != null) _y2Spin.Value = y2;
        Show();
    }

    // === Helpers ===

    private static void AddLabeledEdit(VBoxContainer parent, string label, string value, out LineEdit edit)
    {
        parent.AddChild(EditorTheme.MakeLabel(label, EditorTheme.TEXT_SECONDARY, EditorTheme.FONT_SM));
        edit = new LineEdit { Text = value };
        edit.AddThemeFontSizeOverride("font_size", EditorTheme.FONT_SM);
        edit.CustomMinimumSize = new Vector2(0, 28);
        parent.AddChild(edit);
    }

    private static void AddSpinWithLabel(HBoxContainer parent, string label, int min, int max, int value, out SpinBox spin)
    {
        parent.AddChild(EditorTheme.MakeLabel(label, EditorTheme.TEXT_SECONDARY, EditorTheme.FONT_SM));
        spin = new SpinBox { MinValue = min, MaxValue = max, Value = value, Step = 1 };
        spin.AddThemeFontSizeOverride("font_size", EditorTheme.FONT_SM);
        spin.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        spin.CustomMinimumSize = new Vector2(60, 0);
        parent.AddChild(spin);
    }

    private static CheckBox AddCheck(VBoxContainer parent, string label, bool value)
    {
        var chk = new CheckBox { Text = label, ButtonPressed = value };
        chk.AddThemeFontSizeOverride("font_size", EditorTheme.FONT_SM);
        parent.AddChild(chk);
        return chk;
    }

    private static CheckBox AddCheckInline(HBoxContainer parent, string label, bool value)
    {
        var chk = new CheckBox { Text = label, ButtonPressed = value };
        chk.AddThemeFontSizeOverride("font_size", EditorTheme.FONT_SM);
        parent.AddChild(chk);
        return chk;
    }

    // === NPC Spawn Management ===

    private void RebuildSpawnList()
    {
        if (_spawnListContainer == null || Zone == null || ZoneData == null) return;

        foreach (var child in _spawnListContainer.GetChildren())
            child.QueueFree();

        var zoneSpawns = ZoneData.Spawns.FindAll(s => s.ZoneId == Zone.Id);
        if (zoneSpawns.Count == 0)
        {
            var emptyLabel = EditorTheme.MakeLabel("Sin NPCs asignados.", EditorTheme.TEXT_SECONDARY, EditorTheme.FONT_XS);
            _spawnListContainer.AddChild(emptyLabel);
            return;
        }

        foreach (var spawn in zoneSpawns)
        {
            var row = new HBoxContainer();
            row.AddThemeConstantOverride("separation", 4);

            string modeStr = spawn.Mode == SpawnMode.Fixed ? "Fijo" : "Random";
            string posStr = spawn.Mode == SpawnMode.Fixed ? $"({spawn.SpawnX},{spawn.SpawnY})" : "";
            var label = EditorTheme.MakeLabel(
                $"NPC #{spawn.NpcIndex} x{spawn.Cantidad} [{modeStr}] {posStr} ({spawn.RespawnTime}s)",
                EditorTheme.TEXT_SECONDARY, EditorTheme.FONT_XS);
            label.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            row.AddChild(label);

            var spawnRef = spawn;
            var delBtn = EditorTheme.MakeButton("X", () => { ZoneData.Spawns.Remove(spawnRef); RebuildSpawnList(); });
            delBtn.CustomMinimumSize = new Vector2(24, 20);
            row.AddChild(delBtn);

            _spawnListContainer.AddChild(row);
        }
    }

    private void OnAddSpawn()
    {
        if (Zone == null || ZoneData == null) return;

        var popup = new Window();
        popup.Title = "Agregar NPC Spawn";
        popup.Size = new Vector2I(320, 240);
        popup.Exclusive = true;
        popup.CloseRequested += () => popup.QueueFree();

        var bg = new PanelContainer();
        bg.AddThemeStyleboxOverride("panel", EditorTheme.FlatBox(EditorTheme.BG_PANEL, 0, 0, 0));
        bg.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        popup.AddChild(bg);

        var margin = new MarginContainer();
        margin.AddThemeConstantOverride("margin_left", 12);
        margin.AddThemeConstantOverride("margin_right", 12);
        margin.AddThemeConstantOverride("margin_top", 12);
        margin.AddThemeConstantOverride("margin_bottom", 12);
        bg.AddChild(margin);

        var vb = new VBoxContainer();
        vb.AddThemeConstantOverride("separation", 6);
        margin.AddChild(vb);

        var r1 = new HBoxContainer(); r1.AddThemeConstantOverride("separation", 8);
        SpinBox npcIdxSpin, cantSpin, respawnSpin;
        AddSpinWithLabel(r1, "NPC #:", 1, 9999, 1, out npcIdxSpin);
        AddSpinWithLabel(r1, "Cant:", 1, 50, 1, out cantSpin);
        vb.AddChild(r1);

        var modeOpt = new OptionButton();
        modeOpt.AddItem("Random (dentro de zona)", 0);
        modeOpt.AddItem("Fijo (posicion exacta)", 1);
        modeOpt.Selected = 0;
        modeOpt.AddThemeFontSizeOverride("font_size", EditorTheme.FONT_SM);
        vb.AddChild(modeOpt);

        var posRow = new HBoxContainer(); posRow.AddThemeConstantOverride("separation", 8);
        SpinBox spawnXSpin, spawnYSpin;
        AddSpinWithLabel(posRow, "X:", 1, 1000, Zone.X1, out spawnXSpin);
        AddSpinWithLabel(posRow, "Y:", 1, 1000, Zone.Y1, out spawnYSpin);
        posRow.Visible = false;
        vb.AddChild(posRow);
        modeOpt.ItemSelected += (idx) => posRow.Visible = idx == 1;

        var r3 = new HBoxContainer(); r3.AddThemeConstantOverride("separation", 8);
        AddSpinWithLabel(r3, "Respawn (s):", 1, 3600, 30, out respawnSpin);
        vb.AddChild(r3);

        var zoneId = Zone.Id;
        var okBtn = EditorTheme.PrimaryButton("Agregar", () =>
        {
            var newSpawn = new NpcSpawnInfo
            {
                ZoneId = zoneId,
                NpcIndex = (int)npcIdxSpin.Value,
                Cantidad = (int)cantSpin.Value,
                Mode = modeOpt.Selected == 1 ? SpawnMode.Fixed : SpawnMode.Random,
                SpawnX = (int)spawnXSpin.Value,
                SpawnY = (int)spawnYSpin.Value,
                RespawnTime = (int)respawnSpin.Value,
            };
            ZoneData?.Spawns.Add(newSpawn);
            RebuildSpawnList();
            popup.QueueFree();
        });
        vb.AddChild(okBtn);

        AddChild(popup);
        popup.PopupCentered();
    }
}
