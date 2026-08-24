#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using AOWorldEditor.Data;
using Godot;

namespace AOWorldEditor.Editor;

/// <summary>
/// The world laid out as a grid of cells, one per map.
///
/// Until now there was nowhere to see how maps relate to each other: the map
/// menu is a flat list of numbers. This shows which map sits where, which cells
/// are empty, and lets a map be placed, opened or created in a cell.
/// </summary>
public partial class WorldGridPanel : Window
{
    /// <summary>Open this map for editing.</summary>
    [Signal] public delegate void MapOpenRequestedEventHandler(int mapNumber);

    /// <summary>Create a new map and place it in this cell.</summary>
    [Signal] public delegate void MapCreateRequestedEventHandler(int col, int row);

    /// <summary>Generate the exits between every pair of neighbours.</summary>
    [Signal] public delegate void StitchRequestedEventHandler();

    /// <summary>The grid changed and should be saved.</summary>
    [Signal] public delegate void GridChangedEventHandler();

    // Injected before AddChild.
    public WorldGrid? Grid;
    public HashSet<int>? AvailableMaps;
    public int CurrentMap;

    /// <summary>
    /// Empty cells shown around the occupied ones, so there is always somewhere
    /// to grow into without having to resize anything.
    /// </summary>
    private const int Padding = 1;
    private const int CellSize = 74;

    private GridContainer? _grid;
    private Label? _info;

    public override void _Ready()
    {
        Title = "Mundo";
        Size = new Vector2I(720, 620);
        Exclusive = false;
        CloseRequested += Hide;

        var root = new PanelContainer();
        root.AddThemeStyleboxOverride("panel", EditorTheme.FlatBox(EditorTheme.BG_PANEL, 0, 0, 0));
        root.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        AddChild(root);

        var margin = new MarginContainer();
        foreach (string side in new[] { "left", "right", "top", "bottom" })
            margin.AddThemeConstantOverride($"margin_{side}", 12);
        root.AddChild(margin);

        var column = new VBoxContainer();
        column.AddThemeConstantOverride("separation", 8);
        margin.AddChild(column);

        column.AddChild(EditorTheme.MakeLabel(
            "Cada celda es un mapa. Los vecinos se conectan al coser los bordes.",
            EditorTheme.TEXT_MUTED, EditorTheme.FONT_SM));

        var scroll = new ScrollContainer { SizeFlagsVertical = Control.SizeFlags.ExpandFill };
        column.AddChild(scroll);

        _grid = new GridContainer();
        _grid.AddThemeConstantOverride("h_separation", 3);
        _grid.AddThemeConstantOverride("v_separation", 3);
        scroll.AddChild(_grid);

        _info = EditorTheme.MakeLabel("", EditorTheme.TEXT_SECONDARY, EditorTheme.FONT_SM);
        _info.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        column.AddChild(_info);

        var buttons = new HBoxContainer();
        buttons.AddThemeConstantOverride("separation", 6);
        buttons.AddChild(EditorTheme.PrimaryButton("Coser bordes",
            () => EmitSignal(SignalName.StitchRequested)));
        buttons.AddChild(EditorTheme.Spacer());
        buttons.AddChild(EditorTheme.MakeButton("Cerrar", Hide));
        column.AddChild(buttons);

        Rebuild();
    }

    public void Rebuild()
    {
        if (_grid == null || Grid == null) return;
        foreach (var child in _grid.GetChildren()) child.QueueFree();

        // Show the occupied area plus a ring of empty cells to grow into.
        var bounds = Grid.Bounds();
        int minCol, minRow, maxCol, maxRow;
        if (bounds is null)
        {
            minCol = minRow = 0;
            maxCol = maxRow = 2;
        }
        else
        {
            (minCol, minRow, maxCol, maxRow) = bounds.Value;
            minCol -= Padding; minRow -= Padding;
            maxCol += Padding; maxRow += Padding;
        }

        _grid.Columns = maxCol - minCol + 1;
        for (int row = minRow; row <= maxRow; row++)
            for (int col = minCol; col <= maxCol; col++)
                _grid.AddChild(MakeCell(new WorldCell(col, row)));

        UpdateInfo();
    }

    private Control MakeCell(WorldCell cell)
    {
        int? mapNumber = Grid!.MapAt(cell);
        bool onDisk = mapNumber is int m && (AvailableMaps?.Contains(m) ?? true);

        var button = new Button
        {
            CustomMinimumSize = new Vector2(CellSize, CellSize),
            ClipText = true,
        };
        button.AddThemeFontSizeOverride("font_size", EditorTheme.FONT_SM);

        if (mapNumber is int number)
        {
            button.Text = $"{number}\n({cell})";
            button.TooltipText = onDisk
                ? $"Mapa {number} en la celda {cell}\nClic para abrirlo · clic derecho para quitarlo de la grilla"
                : $"Mapa {number} está en la grilla pero no existe en disco";
            // The three states this styles are exactly current / exists / missing.
            EditorTheme.StyleNavButtonCompact(button, number == CurrentMap, onDisk);
        }
        else
        {
            button.Text = $"+\n({cell})";
            button.TooltipText = $"Celda {cell} vacía\nClic para elegir qué mapa va acá (nuevo o existente)";
            EditorTheme.StyleNavButtonCompact(button, false, false);
        }

        button.Pressed += () => OnCellPressed(cell, mapNumber);
        button.GuiInput += @event => OnCellInput(@event, cell, mapNumber);
        return button;
    }

    private void OnCellPressed(WorldCell cell, int? mapNumber)
    {
        if (mapNumber is int number)
            EmitSignal(SignalName.MapOpenRequested, number);
        else
            EmitSignal(SignalName.MapCreateRequested, cell.Col, cell.Row);
    }

    /// <summary>
    /// Right click takes a map off the grid. Empty cells ignore it: the create
    /// dialog already covers both placing an existing map and making a new one.
    /// </summary>
    private void OnCellInput(InputEvent @event, WorldCell cell, int? mapNumber)
    {
        if (@event is not InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Right })
            return;
        if (mapNumber is null) return;

        Grid!.Clear(cell);
        EmitSignal(SignalName.GridChanged);
        Rebuild();
    }

    private void UpdateInfo()
    {
        if (_info == null || Grid == null) return;

        int borders = Grid.Borders().Count();
        var missing = Grid.Cells.Values
            .Where(m => AvailableMaps != null && !AvailableMaps.Contains(m))
            .OrderBy(m => m).ToList();

        string text = $"{Grid.Count} mapas · {borders} bordes entre vecinos";
        if (missing.Count > 0)
            text += $"\n⚠ En la grilla pero no en disco: {string.Join(", ", missing)}";
        _info.Text = text;
    }
}
