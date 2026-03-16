#nullable enable
using System;
using System.Collections.Generic;
using Godot;

namespace AOWorldEditor.Editor;

/// <summary>
/// Panel for defining and editing sub-zones within the map.
/// Zones are editor metadata with gameplay properties (PvP, Safe, etc.).
/// </summary>
public partial class ZonePanel : VBoxContainer
{
    public EditorState? State;

    // Zone list (left side)
    private ItemList? _zoneList;

    // Property editors (right side)
    private LineEdit? _nameEdit;
    private OptionButton? _typeSelect;
    private SpinBox? _x1Spin, _y1Spin, _x2Spin, _y2Spin;
    private SpinBox? _wanderSpin;
    private CheckBox? _noMagicCheck, _noInvisCheck, _noResCheck, _noHideCheck, _noSummonCheck;

    // Buttons
    private Button? _addBtn, _removeBtn;

    // Property container (hidden when no zone selected)
    private VBoxContainer? _propsContainer;

    private static readonly string[] ZoneTypes = { "Normal", "Safe", "PvP", "Arena", "Dungeon", "Town" };

    [Signal] public delegate void ZonesChangedEventHandler();

    public override void _Ready()
    {
        AddThemeConstantOverride("separation", 4);

        // Header
        var header = new HBoxContainer();
        header.AddThemeConstantOverride("separation", 4);
        var title = EditorTheme.SectionLabel("ZONES");
        title.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        header.AddChild(title);

        _addBtn = EditorTheme.MakeButton("+ Add", OnAddZone, EditorTheme.TEXT_SUCCESS);
        _addBtn.CustomMinimumSize = new Vector2(60, 24);
        header.AddChild(_addBtn);

        _removeBtn = EditorTheme.MakeButton("- Remove", OnRemoveZone, EditorTheme.TEXT_DANGER);
        _removeBtn.CustomMinimumSize = new Vector2(70, 24);
        header.AddChild(_removeBtn);

        AddChild(header);

        // Zone list
        _zoneList = new ItemList
        {
            CustomMinimumSize = new Vector2(0, 120),
            SizeFlagsVertical = SizeFlags.ShrinkBegin,
            SelectMode = ItemList.SelectModeEnum.Single,
            AllowReselect = true,
        };
        _zoneList.AddThemeFontSizeOverride("font_size", EditorTheme.FONT_SM);
        _zoneList.AddThemeColorOverride("font_color", EditorTheme.TEXT_PRIMARY);
        _zoneList.AddThemeColorOverride("font_selected_color", Colors.White);
        var listBg = EditorTheme.FlatBox(EditorTheme.BG_INPUT, 3, 2, 2, EditorTheme.BORDER_SUBTLE, 1);
        _zoneList.AddThemeStyleboxOverride("panel", listBg);
        _zoneList.ItemSelected += OnZoneSelected;
        AddChild(_zoneList);

        AddChild(EditorTheme.MakeHSeparator());

        // Properties container
        _propsContainer = new VBoxContainer();
        _propsContainer.AddThemeConstantOverride("separation", 4);

        var propsLabel = EditorTheme.SectionLabel("PROPERTIES");
        _propsContainer.AddChild(propsLabel);

        // Name
        var nameRow = new HBoxContainer();
        nameRow.AddThemeConstantOverride("separation", 4);
        nameRow.AddChild(EditorTheme.MakeLabel("Name:", EditorTheme.TEXT_SECONDARY, EditorTheme.FONT_SM));
        _nameEdit = new LineEdit { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        _nameEdit.AddThemeFontSizeOverride("font_size", EditorTheme.FONT_SM);
        _nameEdit.TextChanged += OnNameChanged;
        nameRow.AddChild(_nameEdit);
        _propsContainer.AddChild(nameRow);

        // Type
        var typeRow = new HBoxContainer();
        typeRow.AddThemeConstantOverride("separation", 4);
        typeRow.AddChild(EditorTheme.MakeLabel("Type:", EditorTheme.TEXT_SECONDARY, EditorTheme.FONT_SM));
        _typeSelect = new OptionButton { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        _typeSelect.AddThemeFontSizeOverride("font_size", EditorTheme.FONT_SM);
        for (int i = 0; i < ZoneTypes.Length; i++)
            _typeSelect.AddItem(ZoneTypes[i], i);
        _typeSelect.ItemSelected += OnTypeChanged;
        typeRow.AddChild(_typeSelect);
        _propsContainer.AddChild(typeRow);

        // Bounds
        _propsContainer.AddChild(EditorTheme.SectionLabel("BOUNDS (TILE COORDS)"));

        var boundsGrid = new GridContainer { Columns = 4 };
        boundsGrid.AddThemeConstantOverride("h_separation", 4);
        boundsGrid.AddThemeConstantOverride("v_separation", 4);

        boundsGrid.AddChild(EditorTheme.MakeLabel("X1:", EditorTheme.TEXT_SECONDARY, EditorTheme.FONT_SM));
        _x1Spin = EditorTheme.MakeSpinBox(1, 2000, 1, 1);
        _x1Spin.ValueChanged += (v) => OnBoundsChanged();
        boundsGrid.AddChild(_x1Spin);

        boundsGrid.AddChild(EditorTheme.MakeLabel("Y1:", EditorTheme.TEXT_SECONDARY, EditorTheme.FONT_SM));
        _y1Spin = EditorTheme.MakeSpinBox(1, 2000, 1, 1);
        _y1Spin.ValueChanged += (v) => OnBoundsChanged();
        boundsGrid.AddChild(_y1Spin);

        boundsGrid.AddChild(EditorTheme.MakeLabel("X2:", EditorTheme.TEXT_SECONDARY, EditorTheme.FONT_SM));
        _x2Spin = EditorTheme.MakeSpinBox(1, 2000, 1, 100);
        _x2Spin.ValueChanged += (v) => OnBoundsChanged();
        boundsGrid.AddChild(_x2Spin);

        boundsGrid.AddChild(EditorTheme.MakeLabel("Y2:", EditorTheme.TEXT_SECONDARY, EditorTheme.FONT_SM));
        _y2Spin = EditorTheme.MakeSpinBox(1, 2000, 1, 100);
        _y2Spin.ValueChanged += (v) => OnBoundsChanged();
        boundsGrid.AddChild(_y2Spin);

        _propsContainer.AddChild(boundsGrid);

        // NPC wander radius
        var wanderRow = new HBoxContainer();
        wanderRow.AddThemeConstantOverride("separation", 4);
        wanderRow.AddChild(EditorTheme.MakeLabel("NPC Wander:", EditorTheme.TEXT_SECONDARY, EditorTheme.FONT_SM));
        _wanderSpin = EditorTheme.MakeSpinBox(0, 100, 1, 0);
        _wanderSpin.TooltipText = "0 = unlimited wander radius";
        _wanderSpin.ValueChanged += (v) => OnWanderChanged();
        wanderRow.AddChild(_wanderSpin);
        _propsContainer.AddChild(wanderRow);

        _propsContainer.AddChild(EditorTheme.MakeHSeparator());

        // Flags
        _propsContainer.AddChild(EditorTheme.SectionLabel("RESTRICTIONS"));

        _noMagicCheck = MakeFlagCheck("No Magic", (v) => OnFlagChanged());
        _propsContainer.AddChild(_noMagicCheck);

        _noInvisCheck = MakeFlagCheck("No Invis", (v) => OnFlagChanged());
        _propsContainer.AddChild(_noInvisCheck);

        _noResCheck = MakeFlagCheck("No Resurrect", (v) => OnFlagChanged());
        _propsContainer.AddChild(_noResCheck);

        _noHideCheck = MakeFlagCheck("No Hide", (v) => OnFlagChanged());
        _propsContainer.AddChild(_noHideCheck);

        _noSummonCheck = MakeFlagCheck("No Summon", (v) => OnFlagChanged());
        _propsContainer.AddChild(_noSummonCheck);

        _propsContainer.Visible = false;
        AddChild(_propsContainer);
    }

    private static CheckBox MakeFlagCheck(string text, Action<bool> onChange)
    {
        var chk = new CheckBox { Text = text };
        chk.AddThemeFontSizeOverride("font_size", EditorTheme.FONT_SM);
        chk.AddThemeColorOverride("font_color", EditorTheme.TEXT_SECONDARY);
        chk.Toggled += (bool on) => onChange(on);
        return chk;
    }

    /// <summary>
    /// Rebuild the zone list UI from the state.
    /// </summary>
    public void Rebuild()
    {
        if (_zoneList == null || State == null) return;
        _zoneList.Clear();

        foreach (var zone in State.Zones)
        {
            var color = EditorTheme.GetZoneBorderColor(zone.Type);
            _zoneList.AddItem($"[{zone.Type}] {zone.Name}");
            int idx = _zoneList.ItemCount - 1;
            _zoneList.SetItemCustomFgColor(idx, color);
        }

        // Restore selection
        if (State.SelectedZoneIndex >= 0 && State.SelectedZoneIndex < State.Zones.Count)
        {
            _zoneList.Select(State.SelectedZoneIndex);
            LoadZoneProperties(State.Zones[State.SelectedZoneIndex]);
            if (_propsContainer != null) _propsContainer.Visible = true;
        }
        else
        {
            if (_propsContainer != null) _propsContainer.Visible = false;
        }
    }

    private void OnAddZone()
    {
        if (State == null) return;

        var zone = new MapZone
        {
            Name = $"Zone {State.Zones.Count + 1}",
            Type = "Normal",
            X1 = 1, Y1 = 1, X2 = 100, Y2 = 100,
        };
        State.Zones.Add(zone);
        State.SelectedZoneIndex = State.Zones.Count - 1;
        Rebuild();
        EmitSignal(SignalName.ZonesChanged);
    }

    private void OnRemoveZone()
    {
        if (State == null || State.SelectedZoneIndex < 0 || State.SelectedZoneIndex >= State.Zones.Count) return;

        State.Zones.RemoveAt(State.SelectedZoneIndex);
        if (State.SelectedZoneIndex >= State.Zones.Count)
            State.SelectedZoneIndex = State.Zones.Count - 1;
        Rebuild();
        EmitSignal(SignalName.ZonesChanged);
    }

    private void OnZoneSelected(long index)
    {
        if (State == null) return;
        State.SelectedZoneIndex = (int)index;

        if (index >= 0 && index < State.Zones.Count)
        {
            LoadZoneProperties(State.Zones[(int)index]);
            if (_propsContainer != null) _propsContainer.Visible = true;
        }
        else
        {
            if (_propsContainer != null) _propsContainer.Visible = false;
        }
        EmitSignal(SignalName.ZonesChanged);
    }

    private bool _updatingUI;

    private void LoadZoneProperties(MapZone zone)
    {
        _updatingUI = true;
        if (_nameEdit != null) _nameEdit.Text = zone.Name;
        if (_typeSelect != null)
        {
            for (int i = 0; i < ZoneTypes.Length; i++)
            {
                if (ZoneTypes[i] == zone.Type)
                {
                    _typeSelect.Selected = i;
                    break;
                }
            }
        }
        if (_x1Spin != null) _x1Spin.Value = zone.X1;
        if (_y1Spin != null) _y1Spin.Value = zone.Y1;
        if (_x2Spin != null) _x2Spin.Value = zone.X2;
        if (_y2Spin != null) _y2Spin.Value = zone.Y2;
        if (_wanderSpin != null) _wanderSpin.Value = zone.NpcWanderRadius;
        if (_noMagicCheck != null) _noMagicCheck.ButtonPressed = zone.NoMagic;
        if (_noInvisCheck != null) _noInvisCheck.ButtonPressed = zone.NoInvis;
        if (_noResCheck != null) _noResCheck.ButtonPressed = zone.NoResurrect;
        if (_noHideCheck != null) _noHideCheck.ButtonPressed = zone.NoHide;
        if (_noSummonCheck != null) _noSummonCheck.ButtonPressed = zone.NoSummon;
        _updatingUI = false;
    }

    private MapZone? GetSelectedZone()
    {
        if (State == null || State.SelectedZoneIndex < 0 || State.SelectedZoneIndex >= State.Zones.Count)
            return null;
        return State.Zones[State.SelectedZoneIndex];
    }

    private void OnNameChanged(string newText)
    {
        if (_updatingUI) return;
        var zone = GetSelectedZone();
        if (zone == null) return;
        zone.Name = newText;
        UpdateListItem(State!.SelectedZoneIndex, zone);
        EmitSignal(SignalName.ZonesChanged);
    }

    private void OnTypeChanged(long index)
    {
        if (_updatingUI) return;
        var zone = GetSelectedZone();
        if (zone == null || index < 0 || index >= ZoneTypes.Length) return;
        zone.Type = ZoneTypes[(int)index];
        UpdateListItem(State!.SelectedZoneIndex, zone);
        EmitSignal(SignalName.ZonesChanged);
    }

    private void OnBoundsChanged()
    {
        if (_updatingUI) return;
        var zone = GetSelectedZone();
        if (zone == null) return;
        zone.X1 = (int)(_x1Spin?.Value ?? 1);
        zone.Y1 = (int)(_y1Spin?.Value ?? 1);
        zone.X2 = (int)(_x2Spin?.Value ?? 100);
        zone.Y2 = (int)(_y2Spin?.Value ?? 100);
        EmitSignal(SignalName.ZonesChanged);
    }

    private void OnWanderChanged()
    {
        if (_updatingUI) return;
        var zone = GetSelectedZone();
        if (zone == null) return;
        zone.NpcWanderRadius = (int)(_wanderSpin?.Value ?? 0);
        EmitSignal(SignalName.ZonesChanged);
    }

    private void OnFlagChanged()
    {
        if (_updatingUI) return;
        var zone = GetSelectedZone();
        if (zone == null) return;
        zone.NoMagic = _noMagicCheck?.ButtonPressed ?? false;
        zone.NoInvis = _noInvisCheck?.ButtonPressed ?? false;
        zone.NoResurrect = _noResCheck?.ButtonPressed ?? false;
        zone.NoHide = _noHideCheck?.ButtonPressed ?? false;
        zone.NoSummon = _noSummonCheck?.ButtonPressed ?? false;
        EmitSignal(SignalName.ZonesChanged);
    }

    private void UpdateListItem(int index, MapZone zone)
    {
        if (_zoneList == null || index < 0 || index >= _zoneList.ItemCount) return;
        _zoneList.SetItemText(index, $"[{zone.Type}] {zone.Name}");
        _zoneList.SetItemCustomFgColor(index, EditorTheme.GetZoneBorderColor(zone.Type));
    }
}
