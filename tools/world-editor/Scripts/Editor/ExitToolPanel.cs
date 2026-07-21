#nullable enable
using System;
using AOWorldEditor.Data;
using Godot;

namespace AOWorldEditor.Editor;

/// <summary>
/// Sidebar editor for AO tile exits. Exits live in the map info layer
/// (.aoinf/.inf), separately from visual layers and regular tile triggers.
/// </summary>
public partial class ExitToolPanel : PanelContainer
{
    public MapData? Map;
    public UndoManager? Undo;
    public Action? OnChanged;
    public Action<string>? OnStatus;
    public Action<int, int, int>? OnFollowRequested;

    private Label? _sourceLabel;
    private SpinBox? _mapSpin;
    private SpinBox? _xSpin;
    private SpinBox? _ySpin;
    private OptionButton? _triggerSelect;
    private Button? _applyButton;
    private Button? _clearButton;
    private Button? _followButton;

    private int _sourceX = -1;
    private int _sourceY = -1;

    public override void _Ready()
    {
        CustomMinimumSize = new Vector2(260, 0);
        AddThemeStyleboxOverride("panel", EditorTheme.FlatBox(EditorTheme.BG_PANEL, 4, 8, 6, EditorTheme.BORDER, 1));

        var vbox = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        vbox.AddThemeConstantOverride("separation", 6);
        AddChild(vbox);

        vbox.AddChild(EditorTheme.Heading("Salida"));

        _sourceLabel = EditorTheme.MakeLabel("Origen: sin tile", EditorTheme.TEXT_SECONDARY, EditorTheme.FONT_SM);
        _sourceLabel.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        vbox.AddChild(_sourceLabel);

        var grid = new GridContainer { Columns = 2 };
        grid.AddThemeConstantOverride("h_separation", 8);
        grid.AddThemeConstantOverride("v_separation", 4);
        grid.AddChild(EditorTheme.MakeLabel("Mapa:", EditorTheme.TEXT_SECONDARY, EditorTheme.FONT_SM));
        _mapSpin = EditorTheme.MakeSpinBox(0, 999, 1, 0);
        grid.AddChild(_mapSpin);
        grid.AddChild(EditorTheme.MakeLabel("X:", EditorTheme.TEXT_SECONDARY, EditorTheme.FONT_SM));
        _xSpin = EditorTheme.MakeSpinBox(0, 1000, 1, 0);
        grid.AddChild(_xSpin);
        grid.AddChild(EditorTheme.MakeLabel("Y:", EditorTheme.TEXT_SECONDARY, EditorTheme.FONT_SM));
        _ySpin = EditorTheme.MakeSpinBox(0, 1000, 1, 0);
        grid.AddChild(_ySpin);
        vbox.AddChild(grid);

        vbox.AddChild(EditorTheme.MakeLabel("Trigger del tile:", EditorTheme.TEXT_SECONDARY, EditorTheme.FONT_SM));
        _triggerSelect = new OptionButton { CustomMinimumSize = new Vector2(0, 24) };
        _triggerSelect.AddItem("0: Sin trigger", 0);
        _triggerSelect.AddItem("1: Indoor", 1);
        _triggerSelect.AddItem("3: Posicion invalida", 3);
        _triggerSelect.AddItem("4: Zona segura", 4);
        _triggerSelect.AddItem("5: Anti-bloqueo", 5);
        _triggerSelect.AddItem("6: Zona de combate", 6);
        vbox.AddChild(_triggerSelect);

        _applyButton = EditorTheme.PrimaryButton("Aplicar salida", ApplyExit);
        vbox.AddChild(_applyButton);

        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 6);
        _clearButton = EditorTheme.DangerButton("Borrar");
        _clearButton.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        _clearButton.Pressed += ClearExit;
        row.AddChild(_clearButton);

        _followButton = EditorTheme.MakeButton("Ir");
        _followButton.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        _followButton.Pressed += FollowExit;
        row.AddChild(_followButton);
        vbox.AddChild(row);

        SetButtonsEnabled(false);
    }

    public void SetMap(MapData? map)
    {
        Map = map;
        if (!HasValidSource())
            ClearSelection();
        else
            LoadSelectedTile();
    }

    public void SelectTile(int x, int y)
    {
        if (Map == null || !Map.InBounds(x, y))
        {
            ClearSelection();
            return;
        }

        _sourceX = x;
        _sourceY = y;
        LoadSelectedTile();
    }

    public void ClearSelection()
    {
        _sourceX = -1;
        _sourceY = -1;
        if (_sourceLabel != null)
            _sourceLabel.Text = "Origen: sin tile";
        SetButtonsEnabled(false);
    }

    private void LoadSelectedTile()
    {
        if (!HasValidSource() || _sourceLabel == null || _mapSpin == null || _xSpin == null || _ySpin == null || _triggerSelect == null)
            return;

        ref var tile = ref Map!.Tiles[_sourceX, _sourceY];
        _sourceLabel.Text = $"Origen: ({_sourceX}, {_sourceY})";

        if (tile.HasExit)
        {
            _mapSpin.Value = tile.ExitMap;
            _xSpin.Value = tile.ExitX;
            _ySpin.Value = tile.ExitY;
        }
        else
        {
            if (_mapSpin.Value <= 0)
                _mapSpin.Value = Math.Max(Map.MapNumber, 1);
            if (_xSpin.Value <= 0)
                _xSpin.Value = _sourceX;
            if (_ySpin.Value <= 0)
                _ySpin.Value = _sourceY;
        }

        SelectTrigger(tile.Trigger);
        SetButtonsEnabled(true);
    }

    private void ApplyExit()
    {
        if (!HasValidSource() || _mapSpin == null || _xSpin == null || _ySpin == null || _triggerSelect == null)
        {
            OnStatus?.Invoke("Selecciona un tile de origen para la salida.");
            return;
        }

        int destMap = (int)_mapSpin.Value;
        int destX = (int)_xSpin.Value;
        int destY = (int)_ySpin.Value;
        if (destMap <= 0 || destX <= 0 || destY <= 0)
        {
            OnStatus?.Invoke("La salida necesita mapa, X e Y mayores a 0.");
            return;
        }

        var before = Map!.Tiles[_sourceX, _sourceY];
        ref var tile = ref Map.Tiles[_sourceX, _sourceY];
        tile.ExitMap = (short)destMap;
        tile.ExitX = (short)destX;
        tile.ExitY = (short)destY;
        tile.Trigger = (short)_triggerSelect.GetSelectedId();

        if (RecordChange(before, Map.Tiles[_sourceX, _sourceY], "Set Exit"))
        {
            OnStatus?.Invoke($"Salida ({_sourceX},{_sourceY}) -> M{destMap} ({destX},{destY})");
            OnChanged?.Invoke();
        }
    }

    public void ClearExit()
    {
        if (!HasValidSource())
        {
            OnStatus?.Invoke("Selecciona un tile de salida para borrar.");
            return;
        }

        var before = Map!.Tiles[_sourceX, _sourceY];
        ref var tile = ref Map.Tiles[_sourceX, _sourceY];
        tile.ExitMap = 0;
        tile.ExitX = 0;
        tile.ExitY = 0;

        if (RecordChange(before, Map.Tiles[_sourceX, _sourceY], "Clear Exit"))
        {
            OnStatus?.Invoke($"Salida borrada en ({_sourceX},{_sourceY})");
            OnChanged?.Invoke();
        }
        LoadSelectedTile();
    }

    private void FollowExit()
    {
        if (_mapSpin == null || _xSpin == null || _ySpin == null)
            return;

        int destMap = (int)_mapSpin.Value;
        int destX = (int)_xSpin.Value;
        int destY = (int)_ySpin.Value;
        if (destMap <= 0 || destX <= 0 || destY <= 0)
        {
            OnStatus?.Invoke("No hay destino valido para seguir.");
            return;
        }

        OnFollowRequested?.Invoke(destMap, destX, destY);
    }

    private bool RecordChange(MapTile before, MapTile after, string label)
    {
        if (before.Equals(after))
            return false;

        Undo?.BeginBatch(label);
        Undo?.RecordTileChange(_sourceX, _sourceY, before, after);
        Undo?.EndBatch();
        return true;
    }

    private bool HasValidSource()
    {
        return Map != null && _sourceX > 0 && _sourceY > 0 && Map.InBounds(_sourceX, _sourceY);
    }

    private void SetButtonsEnabled(bool enabled)
    {
        if (_applyButton != null) _applyButton.Disabled = !enabled;
        if (_clearButton != null) _clearButton.Disabled = !enabled;
        if (_followButton != null) _followButton.Disabled = !enabled;
    }

    private void SelectTrigger(short trigger)
    {
        if (_triggerSelect == null) return;
        int index = trigger switch
        {
            1 => 1,
            3 => 2,
            4 => 3,
            5 => 4,
            6 => 5,
            _ => 0,
        };
        _triggerSelect.Select(index);
    }
}
