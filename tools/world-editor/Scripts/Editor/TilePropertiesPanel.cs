#nullable enable
using Godot;
using AOWorldEditor.Data;

namespace AOWorldEditor.Editor;

/// <summary>
/// Panel for editing individual tile properties:
/// blocked, trigger, light, exit, NPC, object.
/// Themed with EditorTheme for consistent dark UI.
/// </summary>
public partial class TilePropertiesPanel : PanelContainer
{
    public MapData? Map;
    public EditorState? State;
    public UndoManager? Undo;
    /// <summary>Fired after ApplyChanges so listeners can refresh viewport / occluders.</summary>
    public System.Action? OnTileChanged;

    private Label? _titleLabel;
    private CheckBox? _blockedCheck;
    private CheckBox? _animatedWaterCheck;
    private OptionButton? _triggerSelect;
    private SpinBox? _lightRange, _lightR, _lightG, _lightB;
    private SpinBox? _exitMap, _exitX, _exitY;
    private SpinBox? _npcIndex;
    private SpinBox? _objIndex, _objAmount;
    private Button? _clearLayer3Btn, _clearNpcBtn, _clearObjBtn;
    private Button? _applyBtn;

    private int _tileX, _tileY;

    public override void _Ready()
    {
        CustomMinimumSize = new Vector2(260, 0);
        AddThemeStyleboxOverride("panel", EditorTheme.FlatBox(EditorTheme.BG_PANEL, 4, 8, 6, EditorTheme.BORDER, 1));

        var vbox = new VBoxContainer();
        vbox.AddThemeConstantOverride("separation", 4);
        AddChild(vbox);

        _titleLabel = EditorTheme.Heading("Tile Properties");
        vbox.AddChild(_titleLabel);

        // Blocked
        _blockedCheck = new CheckBox { Text = "Bloqueado" };
        _blockedCheck.AddThemeFontSizeOverride("font_size", EditorTheme.FONT_SM);
        _blockedCheck.AddThemeColorOverride("font_color", EditorTheme.TEXT_DANGER);
        vbox.AddChild(_blockedCheck);

        _animatedWaterCheck = new CheckBox { Text = "Agua animada" };
        _animatedWaterCheck.TooltipText = "El cliente aplica ondas AO20 a este tile de L1";
        _animatedWaterCheck.AddThemeFontSizeOverride("font_size", EditorTheme.FONT_SM);
        _animatedWaterCheck.AddThemeColorOverride("font_color", EditorTheme.TEXT_ACCENT);
        vbox.AddChild(_animatedWaterCheck);

        // Trigger
        vbox.AddChild(EditorTheme.MakeHSeparator());
        vbox.AddChild(EditorTheme.SectionLabel("Trigger"));
        _triggerSelect = new OptionButton();
        _triggerSelect.AddThemeFontSizeOverride("font_size", EditorTheme.FONT_SM);
        _triggerSelect.AddItem("Ninguno", 0);
        _triggerSelect.AddItem("Indoor", 1);
        _triggerSelect.AddItem("InvalidPos", 3);
        _triggerSelect.AddItem("SafeZone", 4);
        _triggerSelect.AddItem("AntiBlock", 5);
        _triggerSelect.AddItem("CombatZone", 6);
        vbox.AddChild(_triggerSelect);

        // Light
        vbox.AddChild(EditorTheme.MakeHSeparator());
        vbox.AddChild(EditorTheme.SectionLabel("Luz"));
        var lightGrid = new GridContainer { Columns = 2 };
        lightGrid.AddThemeConstantOverride("h_separation", 8);
        lightGrid.AddThemeConstantOverride("v_separation", 4);
        lightGrid.AddChild(EditorTheme.MakeLabel("Rango:", EditorTheme.TEXT_SECONDARY, EditorTheme.FONT_SM));
        _lightRange = EditorTheme.MakeSpinBox(0, 20, 1); lightGrid.AddChild(_lightRange);
        lightGrid.AddChild(EditorTheme.MakeLabel("R:", EditorTheme.TEXT_DANGER, EditorTheme.FONT_SM));
        _lightR = EditorTheme.MakeSpinBox(0, 255, 1); lightGrid.AddChild(_lightR);
        lightGrid.AddChild(EditorTheme.MakeLabel("G:", EditorTheme.TEXT_SUCCESS, EditorTheme.FONT_SM));
        _lightG = EditorTheme.MakeSpinBox(0, 255, 1); lightGrid.AddChild(_lightG);
        lightGrid.AddChild(EditorTheme.MakeLabel("B:", EditorTheme.TEXT_ACCENT, EditorTheme.FONT_SM));
        _lightB = EditorTheme.MakeSpinBox(0, 255, 1); lightGrid.AddChild(_lightB);
        vbox.AddChild(lightGrid);

        // Exit
        vbox.AddChild(EditorTheme.MakeHSeparator());
        vbox.AddChild(EditorTheme.SectionLabel("Salida"));
        var exitGrid = new GridContainer { Columns = 2 };
        exitGrid.AddThemeConstantOverride("h_separation", 8);
        exitGrid.AddThemeConstantOverride("v_separation", 4);
        exitGrid.AddChild(EditorTheme.MakeLabel("Mapa:", EditorTheme.TEXT_SECONDARY, EditorTheme.FONT_SM));
        _exitMap = EditorTheme.MakeSpinBox(0, 999, 1); exitGrid.AddChild(_exitMap);
        exitGrid.AddChild(EditorTheme.MakeLabel("X:", EditorTheme.TEXT_SECONDARY, EditorTheme.FONT_SM));
        _exitX = EditorTheme.MakeSpinBox(0, 100, 1); exitGrid.AddChild(_exitX);
        exitGrid.AddChild(EditorTheme.MakeLabel("Y:", EditorTheme.TEXT_SECONDARY, EditorTheme.FONT_SM));
        _exitY = EditorTheme.MakeSpinBox(0, 100, 1); exitGrid.AddChild(_exitY);
        vbox.AddChild(exitGrid);

        // Layer 3 decoration. This is especially useful after stamping a building:
        // structural L1/L2/L4 art stays in place while interior decoration can go.
        vbox.AddChild(EditorTheme.MakeHSeparator());
        vbox.AddChild(EditorTheme.SectionLabel("Decoración L3"));
        _clearLayer3Btn = new Button { Text = "Quitar L3" };
        _clearLayer3Btn.AddThemeFontSizeOverride("font_size", EditorTheme.FONT_SM);
        _clearLayer3Btn.Pressed += ClearLayer3;
        vbox.AddChild(_clearLayer3Btn);

        // NPC
        vbox.AddChild(EditorTheme.MakeHSeparator());
        vbox.AddChild(EditorTheme.SectionLabel("NPC"));
        var npcBox = new HBoxContainer();
        npcBox.AddChild(EditorTheme.MakeLabel("Index:", EditorTheme.TEXT_SECONDARY, EditorTheme.FONT_SM));
        _npcIndex = EditorTheme.MakeSpinBox(0, 9999, 1);
        _npcIndex.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        npcBox.AddChild(_npcIndex);
        vbox.AddChild(npcBox);
        _clearNpcBtn = new Button { Text = "Quitar NPC" };
        _clearNpcBtn.AddThemeFontSizeOverride("font_size", EditorTheme.FONT_SM);
        _clearNpcBtn.Pressed += ClearNpc;
        vbox.AddChild(_clearNpcBtn);

        // Object
        vbox.AddChild(EditorTheme.MakeHSeparator());
        vbox.AddChild(EditorTheme.SectionLabel("Objeto"));
        var objGrid = new GridContainer { Columns = 2 };
        objGrid.AddThemeConstantOverride("h_separation", 8);
        objGrid.AddThemeConstantOverride("v_separation", 4);
        objGrid.AddChild(EditorTheme.MakeLabel("Index:", EditorTheme.TEXT_SECONDARY, EditorTheme.FONT_SM));
        _objIndex = EditorTheme.MakeSpinBox(0, 9999, 1); objGrid.AddChild(_objIndex);
        objGrid.AddChild(EditorTheme.MakeLabel("Cant:", EditorTheme.TEXT_SECONDARY, EditorTheme.FONT_SM));
        _objAmount = EditorTheme.MakeSpinBox(0, 9999, 1); objGrid.AddChild(_objAmount);
        vbox.AddChild(objGrid);
        _clearObjBtn = new Button { Text = "Quitar objeto" };
        _clearObjBtn.AddThemeFontSizeOverride("font_size", EditorTheme.FONT_SM);
        _clearObjBtn.Pressed += ClearObject;
        vbox.AddChild(_clearObjBtn);

        // Separator
        vbox.AddChild(EditorTheme.MakeHSeparator());

        // Apply button
        _applyBtn = EditorTheme.SuccessButton("Aplicar", ApplyChanges);
        _applyBtn.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        vbox.AddChild(_applyBtn);
    }

    public void LoadTile(int x, int y)
    {
        if (Map == null || !Map.InBounds(x, y)) return;

        _tileX = x;
        _tileY = y;
        ref var tile = ref Map.Tiles[x, y];

        _titleLabel!.Text = $"Tile ({x}, {y})";
        _blockedCheck!.ButtonPressed = tile.Blocked;
        _animatedWaterCheck!.ButtonPressed = tile.AnimatedWater;

        // Map trigger value to option index
        int trigIdx = tile.Trigger switch { 0 => 0, 1 => 1, 3 => 2, 4 => 3, 5 => 4, 6 => 5, _ => 0 };
        _triggerSelect!.Selected = trigIdx;

        _lightRange!.Value = tile.LightRange;
        _lightR!.Value = tile.LightR;
        _lightG!.Value = tile.LightG;
        _lightB!.Value = tile.LightB;

        _exitMap!.Value = tile.ExitMap;
        _exitX!.Value = tile.ExitX;
        _exitY!.Value = tile.ExitY;

        _npcIndex!.Value = tile.NpcIndex;
        _objIndex!.Value = tile.ObjIndex;
        _objAmount!.Value = tile.ObjAmount;
        _clearLayer3Btn!.Disabled = tile.Layer3 == 0;
        _clearNpcBtn!.Disabled = tile.NpcIndex == 0;
        _clearObjBtn!.Disabled = tile.ObjIndex == 0;
    }

    private void ApplyChanges()
    {
        if (Map == null || !Map.InBounds(_tileX, _tileY)) return;

        var before = Map.Tiles[_tileX, _tileY];
        ref var tile = ref Map.Tiles[_tileX, _tileY];

        tile.Blocked = _blockedCheck!.ButtonPressed;
        tile.AnimatedWater = _animatedWaterCheck!.ButtonPressed;

        int trigVal = _triggerSelect!.Selected switch { 0 => 0, 1 => 1, 2 => 3, 3 => 4, 4 => 5, 5 => 6, _ => 0 };
        tile.Trigger = (short)trigVal;

        tile.LightRange = (short)_lightRange!.Value;
        tile.LightR = (short)_lightR!.Value;
        tile.LightG = (short)_lightG!.Value;
        tile.LightB = (short)_lightB!.Value;

        tile.ExitMap = (short)_exitMap!.Value;
        tile.ExitX = (short)_exitX!.Value;
        tile.ExitY = (short)_exitY!.Value;

        tile.NpcIndex = (short)_npcIndex!.Value;
        tile.ObjIndex = (short)_objIndex!.Value;
        tile.ObjAmount = (short)_objAmount!.Value;

        Undo?.BeginBatch("Edit Properties");
        Undo?.RecordTileChange(_tileX, _tileY, before, Map.Tiles[_tileX, _tileY]);
        Undo?.EndBatch();
        OnTileChanged?.Invoke();
    }

    private void ClearLayer3()
    {
        if (Map == null || !Map.InBounds(_tileX, _tileY) || Map.Tiles[_tileX, _tileY].Layer3 == 0) return;
        var before = Map.Tiles[_tileX, _tileY];
        Map.Tiles[_tileX, _tileY].Layer3 = 0;
        Undo?.BeginBatch("Remove Layer 3");
        Undo?.RecordTileChange(_tileX, _tileY, before, Map.Tiles[_tileX, _tileY]);
        Undo?.EndBatch();
        OnTileChanged?.Invoke();
        LoadTile(_tileX, _tileY);
    }

    private void ClearNpc()
    {
        if (Map == null || !Map.InBounds(_tileX, _tileY) || Map.Tiles[_tileX, _tileY].NpcIndex == 0) return;
        var before = Map.Tiles[_tileX, _tileY];
        Map.Tiles[_tileX, _tileY].NpcIndex = 0;
        Undo?.BeginBatch("Remove NPC");
        Undo?.RecordTileChange(_tileX, _tileY, before, Map.Tiles[_tileX, _tileY]);
        Undo?.EndBatch();
        OnTileChanged?.Invoke();
        LoadTile(_tileX, _tileY);
    }

    private void ClearObject()
    {
        if (Map == null || !Map.InBounds(_tileX, _tileY) || Map.Tiles[_tileX, _tileY].ObjIndex == 0) return;
        var before = Map.Tiles[_tileX, _tileY];
        Map.Tiles[_tileX, _tileY].ObjIndex = 0;
        Map.Tiles[_tileX, _tileY].ObjAmount = 0;
        Undo?.BeginBatch("Remove Object");
        Undo?.RecordTileChange(_tileX, _tileY, before, Map.Tiles[_tileX, _tileY]);
        Undo?.EndBatch();
        OnTileChanged?.Invoke();
        LoadTile(_tileX, _tileY);
    }
}
