using System;
using System.IO;
using Godot;
using AOWorldEditor.Data;

namespace AOWorldEditor.Editor;

/// <summary>
/// Main editor controller. Builds UI, loads data, handles file operations.
/// </summary>
public partial class EditorMain : Control
{
    // Data
    private GrhData[]? _grhs;
    private TextureManager? _textures;
    private TextureCatalog? _catalog;
    private MapData? _map;
    private string _dataPath = "";  // Path to client Data/ folder
    private string _mapDir = "";    // Path where maps are loaded/saved

    // Editor
    private readonly EditorState _state = new();
    private readonly UndoManager _undo = new();
    private bool _unsavedChanges;

    // UI components
    private ToolBar? _toolBar;
    private TilePalette? _palette;
    private MapViewport? _viewport;
    private TilePropertiesPanel? _propsPanel;
    private FileDialog? _openDialog;
    private FileDialog? _saveDialog;
    private FileDialog? _dataPathDialog;
    private Label? _statusLabel;
    private AcceptDialog? _mapNumberDialog;
    private SpinBox? _mapNumberSpin;

    // Map properties dialog
    private AcceptDialog? _mapPropsDialog;
    private LineEdit? _mapNameEdit;
    private SpinBox? _mapMusicSpin;
    private CheckBox? _mapPkCheck;
    private SpinBox? _mapAmbR, _mapAmbG, _mapAmbB;

    public override void _Ready()
    {
        // Full window layout
        AnchorsPreset = (int)LayoutPreset.FullRect;

        BuildUI();
        TryAutoDetectDataPath();
    }

    private void BuildUI()
    {
        var mainVBox = new VBoxContainer();
        mainVBox.AnchorsPreset = (int)LayoutPreset.FullRect;
        AddChild(mainVBox);

        // Menu bar
        var menuBar = new MenuBar();
        var fileMenu = new PopupMenu { Name = "Archivo" };
        fileMenu.AddItem("Nuevo Mapa", 0);
        fileMenu.AddItem("Abrir Mapa...", 1);
        fileMenu.AddSeparator();
        fileMenu.AddItem("Guardar", 2);
        fileMenu.AddItem("Guardar Como...", 3);
        fileMenu.AddSeparator();
        fileMenu.AddItem("Propiedades del Mapa...", 4);
        fileMenu.AddSeparator();
        fileMenu.AddItem("Configurar Ruta de Datos...", 5);
        fileMenu.IdPressed += OnFileMenuId;
        menuBar.AddChild(fileMenu);

        var editMenu = new PopupMenu { Name = "Editar" };
        editMenu.AddItem("Deshacer (Ctrl+Z)", 0);
        editMenu.AddItem("Rehacer (Ctrl+Y)", 1);
        editMenu.AddSeparator();
        editMenu.AddItem("Copiar Seleccion (Ctrl+C)", 2);
        editMenu.AddItem("Pegar (Ctrl+V)", 3);
        editMenu.IdPressed += OnEditMenuId;
        menuBar.AddChild(editMenu);

        mainVBox.AddChild(menuBar);

        // Toolbar
        _toolBar = new ToolBar { State = _state };
        _toolBar.NewMapRequested += OnNewMap;
        _toolBar.OpenMapRequested += OnOpenMap;
        _toolBar.SaveMapRequested += OnSaveMap;
        _toolBar.SaveAsMapRequested += OnSaveAsMap;
        _toolBar.UndoRequested += () => { _undo.Undo(_map!); _viewport?.QueueRedraw(); };
        _toolBar.RedoRequested += () => { _undo.Redo(_map!); _viewport?.QueueRedraw(); };
        mainVBox.AddChild(_toolBar);

        // Main content: palette | viewport | properties
        var hbox = new HSplitContainer();
        hbox.SizeFlagsVertical = SizeFlags.ExpandFill;
        mainVBox.AddChild(hbox);

        // Left: Tile palette
        _palette = new TilePalette { State = _state };
        hbox.AddChild(_palette);

        // Center: Map viewport (in a SubViewportContainer for proper rendering)
        var viewportPanel = new Panel();
        viewportPanel.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        viewportPanel.ClipContents = true;
        hbox.AddChild(viewportPanel);

        _viewport = new MapViewport
        {
            Map = _map,
            Grhs = _grhs,
            Textures = _textures,
            State = _state,
            Undo = _undo,
        };
        viewportPanel.AddChild(_viewport);

        // Right: Tile properties
        _propsPanel = new TilePropertiesPanel
        {
            Map = _map,
            State = _state,
            Undo = _undo,
        };
        hbox.AddChild(_propsPanel);

        // Status bar
        var statusBar = new HBoxContainer();
        _statusLabel = new Label { Text = "Listo. Configure la ruta de datos para comenzar." };
        _statusLabel.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        _statusLabel.AddThemeFontSizeOverride("font_size", 11);
        statusBar.AddChild(_statusLabel);
        mainVBox.AddChild(statusBar);

        // File dialogs
        _openDialog = new FileDialog
        {
            FileMode = FileDialog.FileModeEnum.OpenFile,
            Access = FileDialog.AccessEnum.Filesystem,
            Title = "Abrir Mapa",
            Size = new Vector2I(600, 400),
        };
        _openDialog.AddFilter("*.map", "Mapa AO");
        _openDialog.FileSelected += OnMapFileSelected;
        AddChild(_openDialog);

        _saveDialog = new FileDialog
        {
            FileMode = FileDialog.FileModeEnum.SaveFile,
            Access = FileDialog.AccessEnum.Filesystem,
            Title = "Guardar Mapa Como",
            Size = new Vector2I(600, 400),
        };
        _saveDialog.AddFilter("*.map", "Mapa AO");
        _saveDialog.FileSelected += OnSaveFileSelected;
        AddChild(_saveDialog);

        _dataPathDialog = new FileDialog
        {
            FileMode = FileDialog.FileModeEnum.OpenDir,
            Access = FileDialog.AccessEnum.Filesystem,
            Title = "Seleccionar carpeta Data/ del cliente",
            Size = new Vector2I(600, 400),
        };
        _dataPathDialog.DirSelected += OnDataPathSelected;
        AddChild(_dataPathDialog);

        // Map number dialog for new/open
        _mapNumberDialog = new AcceptDialog { Title = "Numero de Mapa" };
        var numVBox = new VBoxContainer();
        numVBox.AddChild(new Label { Text = "Numero de mapa:" });
        _mapNumberSpin = new SpinBox { MinValue = 1, MaxValue = 999, Value = 1 };
        numVBox.AddChild(_mapNumberSpin);
        _mapNumberDialog.AddChild(numVBox);
        AddChild(_mapNumberDialog);

        // Map properties dialog
        BuildMapPropsDialog();
    }

    private void BuildMapPropsDialog()
    {
        _mapPropsDialog = new AcceptDialog { Title = "Propiedades del Mapa", Size = new Vector2I(400, 300) };
        var vbox = new VBoxContainer();

        var grid = new GridContainer { Columns = 2 };
        grid.AddChild(new Label { Text = "Nombre:" });
        _mapNameEdit = new LineEdit { CustomMinimumSize = new Vector2(200, 0) };
        grid.AddChild(_mapNameEdit);

        grid.AddChild(new Label { Text = "Musica:" });
        _mapMusicSpin = new SpinBox { MinValue = 0, MaxValue = 999 };
        grid.AddChild(_mapMusicSpin);

        grid.AddChild(new Label { Text = "PvP:" });
        _mapPkCheck = new CheckBox();
        grid.AddChild(_mapPkCheck);

        grid.AddChild(new Label { Text = "Ambient R:" });
        _mapAmbR = new SpinBox { MinValue = 0, MaxValue = 255, Value = 180 };
        grid.AddChild(_mapAmbR);

        grid.AddChild(new Label { Text = "Ambient G:" });
        _mapAmbG = new SpinBox { MinValue = 0, MaxValue = 255, Value = 180 };
        grid.AddChild(_mapAmbG);

        grid.AddChild(new Label { Text = "Ambient B:" });
        _mapAmbB = new SpinBox { MinValue = 0, MaxValue = 255, Value = 180 };
        grid.AddChild(_mapAmbB);

        vbox.AddChild(grid);
        _mapPropsDialog.AddChild(vbox);
        _mapPropsDialog.Confirmed += OnMapPropsConfirmed;
        AddChild(_mapPropsDialog);
    }

    #region Data Loading

    private void TryAutoDetectDataPath()
    {
        // Try common relative paths
        string[] candidates = {
            "../../client/Data",                  // tools/world-editor → client/Data
            "../../../client/Data",               // fallback
            Path.Combine(OS.GetUserDataDir(), "Data"), // Godot user data
        };

        foreach (var candidate in candidates)
        {
            string full = Path.GetFullPath(candidate);
            if (Directory.Exists(full) && File.Exists(Path.Combine(full, "INIT", "Graficos.ind")))
            {
                LoadDataPath(full);
                return;
            }
        }

        SetStatus("No se encontro la carpeta Data/. Use Archivo > Configurar Ruta de Datos.");
    }

    private void LoadDataPath(string dataPath)
    {
        _dataPath = dataPath;
        string graficosInd = Path.Combine(dataPath, "INIT", "Graficos.ind");
        string graficosDir = Path.Combine(dataPath, "Graficos");
        string indicesIni = Path.Combine(dataPath, "INIT", "indices.ini");
        string mapsDir = Path.Combine(dataPath, "Maps");

        if (!File.Exists(graficosInd))
        {
            SetStatus($"ERROR: No se encontro {graficosInd}");
            return;
        }

        // Load GRH data
        _grhs = GrhLoader.Load(graficosInd);
        _textures = new TextureManager(graficosDir);

        // Load texture catalog
        if (File.Exists(indicesIni))
        {
            _catalog = TextureCatalog.LoadFromFile(indicesIni);
            SetStatus($"Cargado: {_grhs.Length} GRHs, {_catalog.AllRefs.Count} texturas en {_catalog.Categories.Count} categorias");
        }
        else
        {
            _catalog = new TextureCatalog();
            SetStatus($"Cargado: {_grhs.Length} GRHs. (indices.ini no encontrado — palette vacio)");
        }

        // Set map directory
        _mapDir = mapsDir;

        // Update UI components
        _palette!.Grhs = _grhs;
        _palette.Textures = _textures;
        _palette.Catalog = _catalog;
        _palette.Rebuild();

        _viewport!.Grhs = _grhs;
        _viewport.Textures = _textures;

        // Create default empty map
        CreateNewMap(1);
    }

    #endregion

    #region File Operations

    private void OnFileMenuId(long id)
    {
        switch (id)
        {
            case 0: OnNewMap(); break;
            case 1: OnOpenMap(); break;
            case 2: OnSaveMap(); break;
            case 3: OnSaveAsMap(); break;
            case 4: ShowMapProperties(); break;
            case 5: _dataPathDialog?.Popup(); break;
        }
    }

    private void OnEditMenuId(long id)
    {
        switch (id)
        {
            case 0: _undo.Undo(_map!); _viewport?.QueueRedraw(); break;
            case 1: _undo.Redo(_map!); _viewport?.QueueRedraw(); break;
            case 2: _state.CopySelection(_map!); break;
            case 3: PasteClipboard(); break;
        }
    }

    private void OnNewMap()
    {
        _mapNumberDialog!.Confirmed += () =>
        {
            CreateNewMap((int)_mapNumberSpin!.Value);
            _mapNumberDialog.Confirmed -= null!; // one-shot
        };
        _mapNumberDialog.PopupCentered();
    }

    private void CreateNewMap(int mapNumber)
    {
        _map = new MapData();
        _map.MapNumber = mapNumber;
        _map.Name = $"Mapa {mapNumber}";

        // Fill with default terrain (GRH 1 = empty/grass)
        for (int y = 1; y <= 100; y++)
            for (int x = 1; x <= 100; x++)
                _map.Tiles[x, y].Layer1 = 1;

        _undo.Clear();
        UpdateViewport();
        SetStatus($"Nuevo mapa {mapNumber} creado (100x100)");
    }

    private void OnOpenMap()
    {
        if (string.IsNullOrEmpty(_mapDir) || !Directory.Exists(_mapDir))
        {
            _openDialog?.Popup();
            return;
        }
        _openDialog!.CurrentDir = _mapDir;
        _openDialog.Popup();
    }

    private void OnMapFileSelected(string path)
    {
        // Extract map number from filename
        string filename = Path.GetFileNameWithoutExtension(path);
        if (filename.StartsWith("Mapa", StringComparison.OrdinalIgnoreCase))
        {
            string numStr = filename.Substring(4);
            if (int.TryParse(numStr, out int mapNum))
            {
                string dir = Path.GetDirectoryName(path) ?? _mapDir;
                _map = MapLoader.Load(dir, mapNum);
                _undo.Clear();
                UpdateViewport();
                SetStatus($"Mapa {mapNum} cargado desde {dir}");
                return;
            }
        }
        SetStatus($"ERROR: No se pudo determinar el numero de mapa de '{filename}'");
    }

    private void OnSaveMap()
    {
        if (_map == null) return;
        if (string.IsNullOrEmpty(_mapDir))
        {
            OnSaveAsMap();
            return;
        }

        MapLoader.Save(_mapDir, _map);
        _unsavedChanges = false;
        SetStatus($"Mapa {_map.MapNumber} guardado en {_mapDir}");
    }

    private void OnSaveAsMap()
    {
        if (!string.IsNullOrEmpty(_mapDir))
            _saveDialog!.CurrentDir = _mapDir;
        _saveDialog!.CurrentFile = $"Mapa{_map?.MapNumber ?? 1}.map";
        _saveDialog.Popup();
    }

    private void OnSaveFileSelected(string path)
    {
        if (_map == null) return;
        string dir = Path.GetDirectoryName(path) ?? _mapDir;
        _mapDir = dir;
        MapLoader.Save(dir, _map);
        _unsavedChanges = false;
        SetStatus($"Mapa {_map.MapNumber} guardado en {dir}");
    }

    private void OnDataPathSelected(string path)
    {
        LoadDataPath(path);
    }

    #endregion

    #region Map Properties

    private void ShowMapProperties()
    {
        if (_map == null) return;
        _mapNameEdit!.Text = _map.Name;
        _mapMusicSpin!.Value = _map.MusicNum;
        _mapPkCheck!.ButtonPressed = _map.PkEnabled;
        _mapAmbR!.Value = _map.AmbientR;
        _mapAmbG!.Value = _map.AmbientG;
        _mapAmbB!.Value = _map.AmbientB;
        _mapPropsDialog!.PopupCentered();
    }

    private void OnMapPropsConfirmed()
    {
        if (_map == null) return;
        _map.Name = _mapNameEdit!.Text;
        _map.MusicNum = (int)_mapMusicSpin!.Value;
        _map.PkEnabled = _mapPkCheck!.ButtonPressed;
        _map.AmbientR = (byte)_mapAmbR!.Value;
        _map.AmbientG = (byte)_mapAmbG!.Value;
        _map.AmbientB = (byte)_mapAmbB!.Value;
        SetStatus($"Propiedades actualizadas: {_map.Name}");
    }

    #endregion

    #region Clipboard

    private void PasteClipboard()
    {
        if (_map == null || _state.Clipboard == null || !_state.HasSelection) return;

        _undo.BeginBatch("Paste");
        for (int y = 0; y < _state.ClipHeight; y++)
        {
            for (int x = 0; x < _state.ClipWidth; x++)
            {
                int dx = _state.SelX1 + x;
                int dy = _state.SelY1 + y;
                if (!_map.InBounds(dx, dy)) continue;

                var before = _map.Tiles[dx, dy];
                _map.Tiles[dx, dy] = _state.Clipboard[x + 1, y + 1];
                _undo.RecordTileChange(dx, dy, before, _map.Tiles[dx, dy]);
            }
        }
        _undo.EndBatch();
        _viewport?.QueueRedraw();
    }

    #endregion

    #region Input

    public override void _UnhandledKeyInput(InputEvent @event)
    {
        if (@event is not InputEventKey key || !key.Pressed) return;

        bool ctrl = key.CtrlPressed;

        if (ctrl)
        {
            switch (key.Keycode)
            {
                case Key.Z:
                    _undo.Undo(_map!);
                    _viewport?.QueueRedraw();
                    break;
                case Key.Y:
                    _undo.Redo(_map!);
                    _viewport?.QueueRedraw();
                    break;
                case Key.S:
                    OnSaveMap();
                    break;
                case Key.O:
                    OnOpenMap();
                    break;
                case Key.N:
                    OnNewMap();
                    break;
                case Key.C:
                    _state.CopySelection(_map!);
                    SetStatus("Seleccion copiada");
                    break;
                case Key.V:
                    PasteClipboard();
                    break;
            }
        }
        else
        {
            // Tool shortcuts
            switch (key.Keycode)
            {
                case Key.P: _state.ActiveTool = EditorTool.Paint; break;
                case Key.E: _state.ActiveTool = EditorTool.Erase; break;
                case Key.S: _state.ActiveTool = EditorTool.Select; break;
                case Key.M: _state.ActiveTool = EditorTool.Move; break;
                case Key.F: _state.ActiveTool = EditorTool.Fill; break;
                case Key.I: _state.ActiveTool = EditorTool.Eyedrop; break;
                case Key.B: _state.ActiveTool = EditorTool.Block; break;
                case Key.G: _state.ShowGrid = !_state.ShowGrid; _viewport?.QueueRedraw(); break;
                case Key.Key1: _state.ActiveLayer = 1; break;
                case Key.Key2: _state.ActiveLayer = 2; break;
                case Key.Key3: _state.ActiveLayer = 3; break;
                case Key.Key4: _state.ActiveLayer = 4; break;
            }
            _toolBar?.HighlightActiveTool();
        }
    }

    #endregion

    #region Helpers

    private void UpdateViewport()
    {
        if (_viewport != null)
        {
            _viewport.Map = _map;
            _viewport.QueueRedraw();
        }
        if (_propsPanel != null)
            _propsPanel.Map = _map;
    }

    private void SetStatus(string msg)
    {
        if (_statusLabel != null) _statusLabel.Text = msg;
        GD.Print($"[Editor] {msg}");
    }

    public override void _Process(double delta)
    {
        // Update tile properties panel when a tile is selected
        if (_state.ShowTileProperties && _propsPanel != null && _map != null)
        {
            _propsPanel.LoadTile(_state.PropTileX, _state.PropTileY);
            _state.ShowTileProperties = false;
        }

        // Update coords in toolbar from mouse position
        if (_viewport != null && _state != null)
        {
            var mousePos = _viewport.GetLocalMousePosition();
            int tx = (int)((mousePos.X - _state.CameraOffset.X) / _state.Zoom / 32);
            int ty = (int)((mousePos.Y - _state.CameraOffset.Y) / _state.Zoom / 32);
            _toolBar?.UpdateCoords(tx, ty);
        }

        // Continuous redraw for viewport (panning/zooming feel)
        _viewport?.QueueRedraw();
    }

    #endregion
}
