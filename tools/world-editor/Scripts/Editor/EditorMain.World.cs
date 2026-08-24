#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using AOWorldEditor.Data;
using Godot;

namespace AOWorldEditor.Editor;

/// <summary>
/// World grid: which map sits where, and generating the exits that join them.
///
/// Kept apart from EditorMain, which is already 4500 lines, and because this is
/// self-contained — it reads the grid, edits .aoinf files and touches nothing
/// else in the editor's state.
/// </summary>
public partial class EditorMain
{
    private WorldGrid? _worldGrid;
    private WorldGridPanel? _worldPanel;

    /// <summary>
    /// Runs the stitcher's geometry checks and quits. The data layer depends on
    /// Godot types, so the tests cannot live in a plain console project — this
    /// is how they get run:
    ///
    ///     Godot --path tools/world-editor --headless -- --test-world
    ///
    /// Returns true when the flag was present, so _Ready can stop early.
    /// </summary>
    private bool RunWorldTestsIfRequested()
    {
        var args = OS.GetCmdlineUserArgs();
        if (!args.Contains("--test-world")) return false;

        int failures = Test.EdgeStitcherTests.Run();
        // The map directories are resolved much later in _Ready, so the file
        // round-trip finds them itself rather than reordering startup.
        if (args.Contains("--with-files"))
            failures += Test.EdgeStitcherTests.RunFileRoundTrip();

        // _Process keeps firing after _Ready returns early, and it assumes the
        // UI exists — stop it before quitting or the run ends in a null deref.
        SetProcess(false);
        SetProcessInput(false);
        GetTree().Quit(failures == 0 ? 0 : 1);
        return true;
    }

    /// <summary>
    /// Where World.ini lives: the INIT folder beside the maps directory
    /// (resources/data/Maps -> resources/data/INIT).
    /// </summary>
    private string WorldInitDir
    {
        get
        {
            string mapDir = _clientMapDir.Length > 0 ? _clientMapDir : _serverMapDir;
            string? dataDir = Directory.GetParent(mapDir)?.FullName;
            return dataDir is null ? "" : Path.Combine(dataDir, "INIT");
        }
    }

    private WorldGrid World()
    {
        if (_worldGrid != null) return _worldGrid;
        string init = WorldInitDir;
        // An empty grid is still usable: cells can be assigned, they just have
        // nowhere to persist to until the data path is set.
        _worldGrid = init.Length > 0 ? WorldGrid.Load(init) : new WorldGrid();
        return _worldGrid;
    }

    private void SaveWorldGrid()
    {
        string init = WorldInitDir;
        if (init.Length == 0)
        {
            SetStatus("No hay carpeta de recursos configurada; la grilla no se guardó.");
            return;
        }
        World().Save(init);
    }

    // ── Menu ──────────────────────────────────────────────────────────────

    private const int WorldMenuOpenPanel = 0;
    private const int WorldMenuStitch = 1;
    private const int WorldMenuPlaceCurrent = 2;
    private const int WorldMenuNorth = 10;
    private const int WorldMenuSouth = 11;
    private const int WorldMenuWest = 12;
    private const int WorldMenuEast = 13;

    private PopupMenu BuildWorldMenu()
    {
        var menu = new PopupMenu { Name = "Mundo" };
        menu.AddItem("Ver grilla del mundo...", WorldMenuOpenPanel);
        menu.AddItem("Poner este mapa en la grilla...", WorldMenuPlaceCurrent);
        menu.AddSeparator();
        menu.AddItem("Coser todos los bordes", WorldMenuStitch);
        menu.AddSeparator();
        menu.AddItem("Ir al mapa del Norte", WorldMenuNorth);
        menu.AddItem("Ir al mapa del Sur", WorldMenuSouth);
        menu.AddItem("Ir al mapa del Oeste", WorldMenuWest);
        menu.AddItem("Ir al mapa del Este", WorldMenuEast);
        menu.IdPressed += OnWorldMenuId;
        return menu;
    }

    private void OnWorldMenuId(long id)
    {
        switch ((int)id)
        {
            case WorldMenuOpenPanel: OpenWorldPanel(); break;
            case WorldMenuStitch: StitchWorld(); break;
            case WorldMenuPlaceCurrent: PromptPlaceCurrentMap(); break;
            case WorldMenuNorth: GoToNeighbour(WorldSide.North); break;
            case WorldMenuSouth: GoToNeighbour(WorldSide.South); break;
            case WorldMenuWest: GoToNeighbour(WorldSide.West); break;
            case WorldMenuEast: GoToNeighbour(WorldSide.East); break;
        }
    }

    // ── Panel ─────────────────────────────────────────────────────────────

    private void OpenWorldPanel()
    {
        if (_worldPanel != null && IsInstanceValid(_worldPanel))
        {
            RefreshWorldPanel();
            _worldPanel.PopupCentered();
            return;
        }

        _worldPanel = new WorldGridPanel
        {
            Grid = World(),
            AvailableMaps = _state.AvailableMaps,
            CurrentMap = _state.CurrentMapNumber,
        };
        _worldPanel.MapOpenRequested += mapNumber => RequestLoadMap(mapNumber);
        _worldPanel.MapCreateRequested += CreateMapInCell;
        _worldPanel.StitchRequested += StitchWorld;
        _worldPanel.GridChanged += SaveWorldGrid;

        AddChild(_worldPanel);
        _worldPanel.PopupCentered();
    }

    private void RefreshWorldPanel()
    {
        if (_worldPanel == null || !IsInstanceValid(_worldPanel)) return;
        _worldPanel.Grid = World();
        _worldPanel.AvailableMaps = _state.AvailableMaps;
        _worldPanel.CurrentMap = _state.CurrentMapNumber;
        _worldPanel.Rebuild();
    }

    /// <summary>Creates a map and places it in the cell in one step.</summary>
    private void CreateMapInCell(int col, int row)
    {
        CheckDirtyThen(() =>
        {
            int mapNumber = NextFreeMapNumber();
            CreateNewMap(mapNumber);
            World().Assign(new WorldCell(col, row), mapNumber);
            SaveWorldGrid();
            // Written straight away so the grid does not point at a map that
            // only exists in memory.
            OnSaveMap();
            _state.ScanAvailableMaps(_serverMapDir.Length > 0 ? _serverMapDir : _clientMapDir);
            RefreshWorldPanel();
            SetStatus($"Mapa {mapNumber} creado en la celda {col},{row}");
        });
    }

    private void PromptPlaceCurrentMap()
    {
        int mapNumber = _state.CurrentMapNumber;
        if (mapNumber <= 0) { SetStatus("No hay un mapa abierto."); return; }

        var existing = World().CellOf(mapNumber);

        var dialog = new AcceptDialog
        {
            Title = $"Ubicar el mapa {mapNumber} en el mundo",
            OkButtonText = "Ubicar",
        };
        var box = new VBoxContainer();
        box.AddThemeConstantOverride("separation", 6);
        box.AddChild(EditorTheme.MakeLabel("Columna crece al este, fila al sur.",
            EditorTheme.TEXT_MUTED, EditorTheme.FONT_SM));

        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 8);
        var colSpin = EditorTheme.MakeSpinBox(-999, 999, 1, existing?.Col ?? 0);
        var rowSpin = EditorTheme.MakeSpinBox(-999, 999, 1, existing?.Row ?? 0);
        row.AddChild(EditorTheme.MakeLabel("Columna", EditorTheme.TEXT_SECONDARY, EditorTheme.FONT_SM));
        row.AddChild(colSpin);
        row.AddChild(EditorTheme.MakeLabel("Fila", EditorTheme.TEXT_SECONDARY, EditorTheme.FONT_SM));
        row.AddChild(rowSpin);
        box.AddChild(row);
        dialog.AddChild(box);
        dialog.AddCancelButton("Cancelar");

        dialog.Confirmed += () =>
        {
            var cell = new WorldCell((int)colSpin.Value, (int)rowSpin.Value);
            World().Assign(cell, mapNumber);
            SaveWorldGrid();
            RefreshWorldPanel();
            SetStatus($"Mapa {mapNumber} ubicado en la celda {cell}");
            dialog.QueueFree();
        };
        dialog.Canceled += () => dialog.QueueFree();

        AddChild(dialog);
        dialog.PopupCentered();
    }

    // ── Navigation ────────────────────────────────────────────────────────

    private void GoToNeighbour(WorldSide side)
    {
        int current = _state.CurrentMapNumber;
        if (World().CellOf(current) is null)
        {
            SetStatus($"El mapa {current} no está en la grilla — usá Mundo > Poner este mapa.");
            return;
        }

        if (World().NeighbourOf(current, side) is not int neighbour)
        {
            SetStatus($"No hay mapa al {side.Label()} del {current}.");
            return;
        }

        RequestLoadMap(neighbour);
    }

    // ── Stitching ─────────────────────────────────────────────────────────

    /// <summary>
    /// Generates the exits for every border of the grid, in both directions.
    ///
    /// Runs against the files on disk, not the map in memory, so the open map
    /// is saved first — otherwise unsaved edits would be silently dropped when
    /// the stitcher rewrote its .aoinf.
    /// </summary>
    private void StitchWorld()
    {
        var grid = World();
        if (grid.Count < 2)
        {
            SetStatus("Poné al menos dos mapas en la grilla antes de coser.");
            return;
        }

        CheckDirtyThen(() =>
        {
            string primary = _serverMapDir.Length > 0 ? _serverMapDir : _clientMapDir;
            if (primary.Length == 0)
            {
                SetStatus("No hay carpeta de mapas configurada.");
                return;
            }

            var mirrors = new List<string>();
            if (_clientMapDir.Length > 0 && _clientMapDir != primary) mirrors.Add(_clientMapDir);

            var result = EdgeStitcher.StitchAll(grid, primary, mirrors);

            foreach (string warning in result.Warnings)
                GD.PushWarning($"[World] {warning}");

            string summary = result.ExitsWritten == 0
                ? "Los bordes ya estaban cosidos, no hubo cambios."
                : $"{result.BordersStitched} bordes cosidos · {result.ExitsWritten} salidas · "
                  + $"mapas {string.Join(", ", result.MapsChanged)}";
            if (result.Warnings.Count > 0)
                summary += $" · {result.Warnings.Count} aviso(s), ver consola";

            SetStatus(summary);
            GD.Print($"[World] {summary}");

            // The open map's exits may have just changed on disk.
            if (result.MapsChanged.Contains(_state.CurrentMapNumber))
                LoadMapByNumber(_state.CurrentMapNumber);
        });
    }
}
