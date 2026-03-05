using Godot;
using AOWorldEditor.Data;

namespace AOWorldEditor.Editor;

/// <summary>
/// Panel for editing individual tile properties:
/// blocked, trigger, light, exit, NPC, object.
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
        CustomMinimumSize = new Vector2(250, 0);

        var vbox = new VBoxContainer();
        AddChild(vbox);

        _titleLabel = new Label { Text = "Tile Properties" };
        _titleLabel.AddThemeFontSizeOverride("font_size", 14);
        vbox.AddChild(_titleLabel);

        _blockedCheck = new CheckBox { Text = "Bloqueado" };
        vbox.AddChild(_blockedCheck);

        // Trigger
        vbox.AddChild(new Label { Text = "Trigger:" });
        _triggerSelect = new OptionButton();
        _triggerSelect.AddItem("Ninguno", 0);
        _triggerSelect.AddItem("Indoor", 1);
        _triggerSelect.AddItem("InvalidPos", 3);
        _triggerSelect.AddItem("SafeZone", 4);
        _triggerSelect.AddItem("AntiBlock", 5);
        _triggerSelect.AddItem("CombatZone", 6);
        vbox.AddChild(_triggerSelect);

        // Light
        vbox.AddChild(new Label { Text = "Luz:" });
        var lightGrid = new GridContainer { Columns = 2 };
        lightGrid.AddChild(new Label { Text = "Rango:" });
        _lightRange = CreateSpinBox(0, 20, 1); lightGrid.AddChild(_lightRange);
        lightGrid.AddChild(new Label { Text = "R:" });
        _lightR = CreateSpinBox(0, 255, 1); lightGrid.AddChild(_lightR);
        lightGrid.AddChild(new Label { Text = "G:" });
        _lightG = CreateSpinBox(0, 255, 1); lightGrid.AddChild(_lightG);
        lightGrid.AddChild(new Label { Text = "B:" });
        _lightB = CreateSpinBox(0, 255, 1); lightGrid.AddChild(_lightB);
        vbox.AddChild(lightGrid);

        // Exit
        vbox.AddChild(new Label { Text = "Salida:" });
        var exitGrid = new GridContainer { Columns = 2 };
        exitGrid.AddChild(new Label { Text = "Mapa:" });
        _exitMap = CreateSpinBox(0, 999, 1); exitGrid.AddChild(_exitMap);
        exitGrid.AddChild(new Label { Text = "X:" });
        _exitX = CreateSpinBox(0, 100, 1); exitGrid.AddChild(_exitX);
        exitGrid.AddChild(new Label { Text = "Y:" });
        _exitY = CreateSpinBox(0, 100, 1); exitGrid.AddChild(_exitY);
        vbox.AddChild(exitGrid);

        // NPC
        vbox.AddChild(new Label { Text = "NPC Index:" });
        _npcIndex = CreateSpinBox(0, 9999, 1);
        vbox.AddChild(_npcIndex);

        // Object
        vbox.AddChild(new Label { Text = "Objeto:" });
        var objGrid = new GridContainer { Columns = 2 };
        objGrid.AddChild(new Label { Text = "Index:" });
        _objIndex = CreateSpinBox(0, 9999, 1); objGrid.AddChild(_objIndex);
        objGrid.AddChild(new Label { Text = "Cant:" });
        _objAmount = CreateSpinBox(0, 9999, 1); objGrid.AddChild(_objAmount);
        vbox.AddChild(objGrid);

        // GRH indices display (read-only)
        vbox.AddChild(new HSeparator());

        // Apply button
        _applyBtn = new Button { Text = "Aplicar" };
        _applyBtn.Pressed += ApplyChanges;
        vbox.AddChild(_applyBtn);
    }

    public void LoadTile(int x, int y)
    {
        if (Map == null || !Map.InBounds(x, y)) return;

        _tileX = x;
        _tileY = y;
        ref var tile = ref Map.Tiles[x, y];

        _titleLabel!.Text = $"Tile ({x}, {y}) | L1:{tile.Layer1} L2:{tile.Layer2} L3:{tile.Layer3} L4:{tile.Layer4}";
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

    private static SpinBox CreateSpinBox(double min, double max, double step)
    {
        return new SpinBox
        {
            MinValue = min,
            MaxValue = max,
            Step = step,
            CustomMinimumSize = new Vector2(80, 0)
        };
    }
}
