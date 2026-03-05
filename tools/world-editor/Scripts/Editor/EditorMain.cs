using System;
using System.IO;
using Godot;
using AOWorldEditor.Data;

namespace AOWorldEditor.Editor;

/// <summary>
/// Main editor controller. Builds UI, loads data, handles file operations.
/// All layout is manual (Position + Size in _Process) — no containers for the main areas.
/// </summary>
public partial class EditorMain : Control
{
    // Data
    private GrhData[]? _grhs;
    private TextureManager? _textures;
    private TextureCatalog? _catalog;
    private MapData? _map;
    private string _dataPath = "";
    private string _mapDir = "";

    // Editor
    private readonly EditorState _state = new();
    private readonly UndoManager _undo = new();

    // UI components
    private MenuBar? _menuBar;
    private TilePalette? _palette;
    private MapViewport? _viewport;
    private Window? _propsWindow;
    private TilePropertiesPanel? _propsPanel;
    private FileDialog? _openDialog;
    private FileDialog? _saveDialog;
    private FileDialog? _dataPathDialog;
    private Label? _statusLabel;
    private Label? _coordLabel;
    private Label? _layerLabel;
    private Label? _toolLabel;
    private HBoxContainer? _statusBar;

    // Map properties dialog
    private AcceptDialog? _mapPropsDialog;
    private LineEdit? _mapNameEdit;
    private SpinBox? _mapMusicSpin;
    private CheckBox? _mapPkCheck;
    private SpinBox? _mapAmbR, _mapAmbG, _mapAmbB;

    private const float PaletteWidth = 300;
    private const float StatusHeight = 24;

    public override void _Ready()
    {
        BuildUI();
        TryAutoDetectDataPath();
    }

    private void BuildUI()
    {
        // ─── Menu bar (direct child, will be positioned manually) ───
        _menuBar = new MenuBar();

        var fileMenu = new PopupMenu { Name = "Archivo" };
        fileMenu.AddItem("Nuevo Mapa (Ctrl+N)", 0);
        fileMenu.AddItem("Abrir Mapa... (Ctrl+O)", 1);
        fileMenu.AddSeparator();
        fileMenu.AddItem("Guardar (Ctrl+S)", 2);
        fileMenu.AddItem("Guardar Como...", 3);
        fileMenu.AddSeparator();
        fileMenu.AddItem("Propiedades del Mapa...", 4);
        fileMenu.AddItem("Propiedades de Tile (T)", 5);
        fileMenu.AddSeparator();
        fileMenu.AddItem("Configurar Ruta de Datos...", 6);
        fileMenu.IdPressed += OnFileMenuId;
        _menuBar.AddChild(fileMenu);

        var editMenu = new PopupMenu { Name = "Editar" };
        editMenu.AddItem("Deshacer (Ctrl+Z)", 0);
        editMenu.AddItem("Rehacer (Ctrl+Y)", 1);
        editMenu.AddSeparator();
        editMenu.AddItem("Copiar (Ctrl+C)", 2);
        editMenu.AddItem("Pegar (Ctrl+V)", 3);
        editMenu.IdPressed += OnEditMenuId;
        _menuBar.AddChild(editMenu);

        var viewMenu = new PopupMenu { Name = "Ver" };
        viewMenu.AddCheckItem("Grilla (G)", 0);
        viewMenu.AddCheckItem("Bloqueados", 1);
        viewMenu.AddCheckItem("Salidas", 2);
        viewMenu.AddSeparator();
        viewMenu.AddCheckItem("Capa 1", 3);
        viewMenu.AddCheckItem("Capa 2", 4);
        viewMenu.AddCheckItem("Capa 3", 5);
        viewMenu.AddCheckItem("Capa 4", 6);
        for (int i = 0; i <= 6; i++) viewMenu.SetItemChecked(i, true);
        viewMenu.IdPressed += OnViewMenuId;
        _menuBar.AddChild(viewMenu);

        AddChild(_menuBar);

        // ─── Tile palette (direct child) ───
        _palette = new TilePalette { State = _state };
        AddChild(_palette);

        // ─── Map viewport (direct child) ───
        _viewport = new MapViewport
        {
            Map = _map,
            Grhs = _grhs,
            Textures = _textures,
            State = _state,
            Undo = _undo,
            ClipContents = true,
        };
        AddChild(_viewport);

        // ─── Status bar (direct child) ───
        _statusBar = new HBoxContainer();
        _statusBar.AddThemeConstantOverride("separation", 16);

        _toolLabel = new Label { Text = "Pintar" };
        _toolLabel.AddThemeFontSizeOverride("font_size", 11);
        _toolLabel.AddThemeColorOverride("font_color", new Color(0.5f, 1f, 0.5f));
        _statusBar.AddChild(_toolLabel);

        _layerLabel = new Label { Text = "Capa 1" };
        _layerLabel.AddThemeFontSizeOverride("font_size", 11);
        _layerLabel.AddThemeColorOverride("font_color", new Color(0.7f, 0.8f, 1f));
        _statusBar.AddChild(_layerLabel);

        _coordLabel = new Label { Text = "(0, 0)" };
        _coordLabel.AddThemeFontSizeOverride("font_size", 11);
        _statusBar.AddChild(_coordLabel);

        _statusLabel = new Label { Text = "Listo" };
        _statusLabel.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        _statusLabel.HorizontalAlignment = HorizontalAlignment.Right;
        _statusLabel.AddThemeFontSizeOverride("font_size", 11);
        _statusLabel.AddThemeColorOverride("font_color", new Color(0.6f, 0.6f, 0.6f));
        _statusBar.AddChild(_statusLabel);

        AddChild(_statusBar);

        // ─── Tile Properties as floating Window ───
        _propsWindow = new Window
        {
            Title = "Propiedades de Tile",
            Size = new Vector2I(280, 500),
            Visible = false,
            Exclusive = false,
            AlwaysOnTop = true,
            Unresizable = false,
        };
        _propsWindow.CloseRequested += () => _propsWindow.Visible = false;
        AddChild(_propsWindow);

        _propsPanel = new TilePropertiesPanel
        {
            Map = _map,
            State = _state,
            Undo = _undo,
        };
        _propsWindow.AddChild(_propsPanel);

        // ─── File dialogs ───
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

        BuildMapPropsDialog();
    }

    /// <summary>
    /// Manual layout — called every frame to size/position all UI elements.
    /// </summary>
    private void DoLayout()
    {
        var win = GetViewportRect().Size;

        // Menu bar: top, full width
        float menuH = _menuBar?.GetMinimumSize().Y ?? 30;
        if (_menuBar != null)
        {
            _menuBar.Position = Vector2.Zero;
            _menuBar.Size = new Vector2(win.X, menuH);
        }

        float contentTop = menuH;
        float contentBottom = win.Y - StatusHeight;
        float contentH = contentBottom - contentTop;

        // Palette: left side, fixed width
        if (_palette != null)
        {
            _palette.Position = new Vector2(0, contentTop);
            _palette.Size = new Vector2(PaletteWidth, contentH);
        }

        // Viewport: right of palette, fills everything else
        if (_viewport != null)
        {
            float vpX = PaletteWidth + 2;
            _viewport.Position = new Vector2(vpX, contentTop);
            _viewport.Size = new Vector2(win.X - vpX, contentH);
        }

        // Status bar: bottom
        if (_statusBar != null)
        {
            _statusBar.Position = new Vector2(0, contentBottom);
            _statusBar.Size = new Vector2(win.X, StatusHeight);
        }
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
        string[] candidates = {
            "../../client/Data",
            "../../../client/Data",
            Path.Combine(OS.GetUserDataDir(), "Data"),
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

        SetStatus("Data/ no encontrada. Archivo > Configurar Ruta de Datos");
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
            SetStatus($"ERROR: {graficosInd} no encontrado");
            return;
        }

        _grhs = GrhLoader.Load(graficosInd);
        _textures = new TextureManager(graficosDir);

        if (File.Exists(indicesIni))
        {
            _catalog = TextureCatalog.LoadFromFile(indicesIni);
            SetStatus($"{_grhs.Length} GRHs, {_catalog.AllRefs.Count} texturas, {_catalog.Categories.Count} categorias");
        }
        else
        {
            _catalog = new TextureCatalog();
            SetStatus($"{_grhs.Length} GRHs (indices.ini no encontrado)");
        }

        _mapDir = mapsDir;

        // Prefer server maps dir (has .inf/.dat with NPCs/objects/exits)
        string serverMapsDir = Path.GetFullPath(Path.Combine(dataPath, "..", "server", "maps"));
        if (Directory.Exists(serverMapsDir))
        {
            _mapDir = serverMapsDir;
            GD.Print($"[Editor] Using server maps: {serverMapsDir}");
        }

        _palette!.Grhs = _grhs;
        _palette.Textures = _textures;
        _palette.Catalog = _catalog;
        _palette.Rebuild();

        _viewport!.Grhs = _grhs;
        _viewport.Textures = _textures;

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
            case 5: ToggleTileProperties(); break;
            case 6: _dataPathDialog?.Popup(); break;
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

    private void OnViewMenuId(long id)
    {
        switch (id)
        {
            case 0: _state.ShowGrid = !_state.ShowGrid; break;
            case 1: _state.ShowBlocked = !_state.ShowBlocked; break;
            case 2: _state.ShowExits = !_state.ShowExits; break;
            case 3: _state.ShowLayer1 = !_state.ShowLayer1; break;
            case 4: _state.ShowLayer2 = !_state.ShowLayer2; break;
            case 5: _state.ShowLayer3 = !_state.ShowLayer3; break;
            case 6: _state.ShowLayer4 = !_state.ShowLayer4; break;
        }
        _viewport?.QueueRedraw();
    }

    private void OnNewMap()
    {
        CreateNewMap(1);
    }

    private void CreateNewMap(int mapNumber)
    {
        _map = new MapData();
        _map.MapNumber = mapNumber;
        _map.Name = $"Mapa {mapNumber}";

        for (int y = 1; y <= 100; y++)
            for (int x = 1; x <= 100; x++)
                _map.Tiles[x, y].Layer1 = 1;

        _undo.Clear();
        UpdateViewport();
        SetStatus($"Mapa {mapNumber} (100x100)");
    }

    private void OnOpenMap()
    {
        if (!string.IsNullOrEmpty(_mapDir) && Directory.Exists(_mapDir))
            _openDialog!.CurrentDir = _mapDir;
        _openDialog!.Popup();
    }

    private void OnMapFileSelected(string path)
    {
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
                SetStatus($"Mapa {mapNum} cargado");
                return;
            }
        }
        SetStatus($"ERROR: nombre invalido '{filename}'");
    }

    private void OnSaveMap()
    {
        if (_map == null) return;
        if (string.IsNullOrEmpty(_mapDir)) { OnSaveAsMap(); return; }
        MapLoader.Save(_mapDir, _map);
        SetStatus($"Mapa {_map.MapNumber} guardado");
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
        SetStatus($"Mapa {_map.MapNumber} guardado");
    }

    private void OnDataPathSelected(string path) => LoadDataPath(path);

    #endregion

    #region Map Properties / Tile Properties

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
        SetStatus($"Props: {_map.Name}");
    }

    private void ToggleTileProperties()
    {
        if (_propsWindow == null) return;
        _propsWindow.Visible = !_propsWindow.Visible;
        if (_propsWindow.Visible)
            _propsWindow.Position = new Vector2I(
                (int)GetViewportRect().Size.X - 300, 60);
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

    private static readonly string[] ToolNames = {
        "Pintar", "Borrar", "Seleccionar", "Mover", "Rellenar",
        "Cuentagotas", "Bloquear", "Luz", "Salida", "NPC", "Objeto", "Trigger"
    };

    public override void _UnhandledKeyInput(InputEvent @event)
    {
        if (@event is not InputEventKey key || !key.Pressed) return;

        bool ctrl = key.CtrlPressed;

        if (ctrl)
        {
            switch (key.Keycode)
            {
                case Key.Z: _undo.Undo(_map!); _viewport?.QueueRedraw(); break;
                case Key.Y: _undo.Redo(_map!); _viewport?.QueueRedraw(); break;
                case Key.S: OnSaveMap(); break;
                case Key.O: OnOpenMap(); break;
                case Key.N: OnNewMap(); break;
                case Key.C: _state.CopySelection(_map!); SetStatus("Copiado"); break;
                case Key.V: PasteClipboard(); break;
            }
        }
        else
        {
            switch (key.Keycode)
            {
                case Key.P: _state.ActiveTool = EditorTool.Paint; break;
                case Key.E: _state.ActiveTool = EditorTool.Erase; break;
                case Key.R: _state.ActiveTool = EditorTool.Select; break;
                case Key.M: _state.ActiveTool = EditorTool.Move; break;
                case Key.F: _state.ActiveTool = EditorTool.Fill; break;
                case Key.I: _state.ActiveTool = EditorTool.Eyedrop; break;
                case Key.B: _state.ActiveTool = EditorTool.Block; break;
                case Key.G: _state.ShowGrid = !_state.ShowGrid; _viewport?.QueueRedraw(); break;
                case Key.T: ToggleTileProperties(); break;
                case Key.Key1: _state.ActiveLayer = 1; break;
                case Key.Key2: _state.ActiveLayer = 2; break;
                case Key.Key3: _state.ActiveLayer = 3; break;
                case Key.Key4: _state.ActiveLayer = 4; break;
            }
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
        // Manual layout every frame (handles window resize)
        DoLayout();

        // Open tile properties when a tile is clicked with property tools
        if (_state.ShowTileProperties && _propsPanel != null && _map != null)
        {
            _propsPanel.LoadTile(_state.PropTileX, _state.PropTileY);
            _state.ShowTileProperties = false;
            if (_propsWindow != null && !_propsWindow.Visible)
            {
                _propsWindow.Visible = true;
                _propsWindow.Position = new Vector2I(
                    (int)GetViewportRect().Size.X - 300, 60);
            }
        }

        // Update status bar coords
        if (_viewport != null)
        {
            var mousePos = _viewport.GetLocalMousePosition();
            int tx = (int)((mousePos.X - _state.CameraOffset.X) / _state.Zoom / 32);
            int ty = (int)((mousePos.Y - _state.CameraOffset.Y) / _state.Zoom / 32);
            _coordLabel!.Text = $"({tx}, {ty})";
        }

        int toolIdx = (int)_state.ActiveTool;
        _toolLabel!.Text = toolIdx < ToolNames.Length ? ToolNames[toolIdx] : "?";
        _layerLabel!.Text = $"Capa {_state.ActiveLayer}";

        _viewport?.QueueRedraw();
    }

    #endregion
}
