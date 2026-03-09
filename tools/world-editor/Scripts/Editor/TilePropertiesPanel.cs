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

    private Label? _titleLabel;
    private CheckBox? _blockedCheck;
    private OptionButton? _triggerSelect;
    private SpinBox? _lightRange, _lightR, _lightG, _lightB;
    private SpinBox? _exitMap, _exitX, _exitY;
    private SpinBox? _npcIndex;
    private SpinBox? _objIndex, _objAmount;
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

        // NPC
        vbox.AddChild(EditorTheme.MakeHSeparator());
        vbox.AddChild(EditorTheme.SectionLabel("NPC"));
        var npcBox = new HBoxContainer();
        npcBox.AddChild(EditorTheme.MakeLabel("Index:", EditorTheme.TEXT_SECONDARY, EditorTheme.FONT_SM));
        _npcIndex = EditorTheme.MakeSpinBox(0, 9999, 1);
        _npcIndex.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        npcBox.AddChild(_npcIndex);
        vbox.AddChild(npcBox);

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
    }

    private void ApplyChanges()
    {
        if (Map == null || !Map.InBounds(_tileX, _tileY)) return;

        var before = Map.Tiles[_tileX, _tileY];
        ref var tile = ref Map.Tiles[_tileX, _tileY];

        tile.Blocked = _blockedCheck!.ButtonPressed;

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
    }
}
