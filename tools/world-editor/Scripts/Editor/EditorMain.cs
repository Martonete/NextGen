#nullable enable
using System;
using System.IO;
using Godot;
using AOWorldEditor.Data;

namespace AOWorldEditor.Editor;

public partial class EditorMain : Control
{
    // Data
    private GrhData[]? _grhs;
    private TextureManager? _textures;
    private TextureCatalog? _catalog;
    private MapData? _map;
    private ParticleEngine? _particles;
    private int[]? _objGrhs;
    private int[]? _npcBodyGrhs;
    private int[]? _npcHeadOfsX;
    private int[]? _npcHeadOfsY;
    private int[]? _npcBodies;
    private int[]? _npcHeads;
    private int[]? _headGrhs;
    private string _dataPath = "";

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
    private PopupMenu? _viewMenu;

    // Status bar
    private HBoxContainer? _statusBar;
    private Label? _statusLabel;
    private Label? _coordLabel;
    private Label? _layerLabel;
    private Label? _toolLabel;

    // Map navigation bar
    private HBoxContainer? _mapNavBar;
    private SpinBox? _mapNumSpin;
    private Button[] _mapNavButtons = Array.Empty<Button>();
    private const int NavButtonCount = 11; // show ±5 maps around current

    // Tool bar (Excalidraw-style)
    private HBoxContainer? _toolBar;
    private Button[] _toolBarButtons = Array.Empty<Button>();

    // Tile info panel (bottom of left sidebar)
    private VBoxContainer? _tileInfoPanel;
    private Label? _tileInfoLabel;

    // Map properties dialog
    private AcceptDialog? _mapPropsDialog;
    private LineEdit? _mapNameEdit;
    private SpinBox? _mapMusicSpin;
    private CheckBox? _mapPkCheck;
    private SpinBox? _mapAmbR, _mapAmbG, _mapAmbB;

    // Unsaved changes dialog
    private ConfirmationDialog? _unsavedDialog;
    private Action? _pendingAfterSaveCheck;

    private const float PaletteWidth = 300;
    private const float StatusHeight = 24;
    private const float NavBarHeight = 28;
    private const float ToolBarHeight = 32;
    private const float TileInfoHeight = 110;

    public override void _Ready()
    {
        // Wire dirty tracking
        _undo.Changed += () => _state.MarkDirty();
        _state.DirtyChanged += OnDirtyChanged;
        _state.ExitFollowRequested += OnExitFollow;

        BuildUI();
        TryAutoDetectDataPath();
    }

    private void BuildUI()
    {
        // --- Menu bar ---
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
        editMenu.AddItem("Cortar (Ctrl+X)", 4);
        editMenu.AddItem("Copiar (Ctrl+C)", 2);
        editMenu.AddItem("Pegar (Ctrl+V)", 3);
        editMenu.AddSeparator();
        editMenu.AddItem("Eliminar (Supr)", 5);
        editMenu.IdPressed += OnEditMenuId;
        _menuBar.AddChild(editMenu);

        _viewMenu = new PopupMenu { Name = "Ver" };
        _viewMenu.AddCheckItem("Grilla (G)", 0);
        _viewMenu.AddCheckItem("Bloqueados", 1);
        _viewMenu.AddCheckItem("Salidas", 2);
        _viewMenu.AddSeparator();
        _viewMenu.AddCheckItem("Capa 1", 3);
        _viewMenu.AddCheckItem("Capa 2", 4);
        _viewMenu.AddCheckItem("Capa 3", 5);
        _viewMenu.AddCheckItem("Capa 4", 6);
        _viewMenu.AddSeparator();
        _viewMenu.AddCheckItem("NPCs", 7);
        _viewMenu.AddCheckItem("Objetos", 8);
        _viewMenu.AddCheckItem("Particulas", 9);
        _viewMenu.AddCheckItem("Luces", 10);
        for (int id = 0; id <= 10; id++)
        {
            int idx = _viewMenu.GetItemIndex(id);
            if (idx >= 0) _viewMenu.SetItemChecked(idx, true);
        }
        _viewMenu.IdPressed += OnViewMenuId;
        _menuBar.AddChild(_viewMenu);

        AddChild(_menuBar);

        // --- Map navigation bar ---
        _mapNavBar = new HBoxContainer();
        _mapNavBar.AddThemeConstantOverride("separation", 2);

        var navLabel = new Label { Text = "Mapa:" };
        navLabel.AddThemeFontSizeOverride("font_size", 11);
        _mapNavBar.AddChild(navLabel);

        // Left arrow
        var btnPrev = new Button { Text = "<", CustomMinimumSize = new Vector2(28, 0) };
        btnPrev.Pressed += () => NavigateMapOffset(-NavButtonCount);
        _mapNavBar.AddChild(btnPrev);

        // Map number buttons
        _mapNavButtons = new Button[NavButtonCount];
        for (int i = 0; i < NavButtonCount; i++)
        {
            var btn = new Button
            {
                CustomMinimumSize = new Vector2(48, 0),
                SizeFlagsHorizontal = SizeFlags.ShrinkCenter,
            };
            btn.AddThemeFontSizeOverride("font_size", 10);
            int capturedIdx = i;
            btn.Pressed += () => OnNavButtonPressed(capturedIdx);
            _mapNavBar.AddChild(btn);
            _mapNavButtons[i] = btn;
        }

        // Right arrow
        var btnNext = new Button { Text = ">", CustomMinimumSize = new Vector2(28, 0) };
        btnNext.Pressed += () => NavigateMapOffset(NavButtonCount);
        _mapNavBar.AddChild(btnNext);

        // Separator
        _mapNavBar.AddChild(new VSeparator());

        // Quick jump
        var goLabel = new Label { Text = "Ir a:" };
        goLabel.AddThemeFontSizeOverride("font_size", 11);
        _mapNavBar.AddChild(goLabel);

        _mapNumSpin = new SpinBox
        {
            MinValue = 1, MaxValue = 999, Value = 1,
            CustomMinimumSize = new Vector2(70, 0),
        };
        _mapNumSpin.AddThemeFontSizeOverride("font_size", 11);
        _mapNavBar.AddChild(_mapNumSpin);

        var goBtn = new Button { Text = "Ir" };
        goBtn.Pressed += () => RequestLoadMap((int)_mapNumSpin.Value);
        _mapNavBar.AddChild(goBtn);

        AddChild(_mapNavBar);

        // --- Tool bar (Excalidraw-style) ---
        _toolBar = new HBoxContainer();
        _toolBar.AddThemeConstantOverride("separation", 2);

        var toolDefs = new (EditorTool tool, string icon, string label, string shortcut)[]
        {
            (EditorTool.Hand,    "\u270b", "Mano",        "H"),
            (EditorTool.Paint,   "\u270f", "Pintar",      "P"),
            (EditorTool.Erase,   "\u232b", "Borrar",      "E"),
            (EditorTool.Select,  "\u25a1", "Seleccionar", "R"),
            (EditorTool.Move,    "\u2725", "Mover",       "M"),
            (EditorTool.Pick,    "\u261d", "Agarrar",     "V"),
            (EditorTool.Fill,    "\u25a8", "Rellenar",    "F"),
            (EditorTool.Eyedrop, "\u25ce", "Cuentagotas", "I"),
            (EditorTool.Block,   "\u2298", "Bloquear",    "B"),
        };
        _toolBarButtons = new Button[toolDefs.Length];
        for (int i = 0; i < toolDefs.Length; i++)
        {
            var (tool, icon, label, shortcut) = toolDefs[i];
            var btn = new Button
            {
                Text = icon,
                TooltipText = $"{label} ({shortcut})",
                ToggleMode = true,
                CustomMinimumSize = new Vector2(ToolBarHeight, ToolBarHeight - 4),
            };
            btn.AddThemeFontSizeOverride("font_size", 16);
            var capturedTool = tool;
            btn.Pressed += () =>
            {
                _state.ActiveTool = capturedTool;
                _state.Pick.Clear();
                SyncToolBar();
            };
            _toolBar.AddChild(btn);
            _toolBarButtons[i] = btn;
        }

        // Separator + property tools
        _toolBar.AddChild(new VSeparator());
        var propToolDefs = new (EditorTool tool, string icon, string label)[]
        {
            (EditorTool.Light,   "\u2600", "Luz"),
            (EditorTool.Exit,    "\u2197", "Salida"),
            (EditorTool.Npc,     "\u265f", "NPC"),
            (EditorTool.Object,  "\u25c6", "Objeto"),
            (EditorTool.Trigger, "\u26a1", "Trigger"),
        };
        var extButtons = new Button[propToolDefs.Length];
        for (int i = 0; i < propToolDefs.Length; i++)
        {
            var (tool, icon, label) = propToolDefs[i];
            var btn = new Button
            {
                Text = icon,
                TooltipText = label,
                ToggleMode = true,
                CustomMinimumSize = new Vector2(ToolBarHeight, ToolBarHeight - 4),
            };
            btn.AddThemeFontSizeOverride("font_size", 16);
            btn.AddThemeColorOverride("font_color", new Color(0.8f, 0.8f, 0.7f));
            var capturedTool = tool;
            btn.Pressed += () =>
            {
                _state.ActiveTool = capturedTool;
                _state.Pick.Clear();
                SyncToolBar();
            };
            _toolBar.AddChild(btn);
            extButtons[i] = btn;
        }
        // Merge all tool buttons for sync
        var allBtns = new Button[_toolBarButtons.Length + extButtons.Length];
        Array.Copy(_toolBarButtons, allBtns, _toolBarButtons.Length);
        Array.Copy(extButtons, 0, allBtns, _toolBarButtons.Length, extButtons.Length);
        _toolBarButtons = allBtns;

        // Layer selector in toolbar
        _toolBar.AddChild(new VSeparator());
        var tbLayerLabel = new Label { Text = "Capa:" };
        tbLayerLabel.AddThemeFontSizeOverride("font_size", 11);
        _toolBar.AddChild(tbLayerLabel);

        AddChild(_toolBar);
        SyncToolBar();

        // --- Tile palette (left sidebar) ---
        _palette = new TilePalette { State = _state };
        AddChild(_palette);

        // --- Tile info panel (bottom of left sidebar) ---
        _tileInfoPanel = new VBoxContainer();
        _tileInfoPanel.AddThemeConstantOverride("separation", 1);

        var infoHeader = new Label { Text = "Info del Tile" };
        infoHeader.AddThemeFontSizeOverride("font_size", 11);
        infoHeader.AddThemeColorOverride("font_color", new Color(0.7f, 0.85f, 1f));
        _tileInfoPanel.AddChild(infoHeader);

        _tileInfoLabel = new Label();
        _tileInfoLabel.AddThemeFontSizeOverride("font_size", 10);
        _tileInfoLabel.AddThemeColorOverride("font_color", new Color(0.75f, 0.75f, 0.75f));
        _tileInfoPanel.AddChild(_tileInfoLabel);

        AddChild(_tileInfoPanel);

        // --- Map viewport ---
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

        // --- Status bar ---
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

        // --- Tile Properties floating Window ---
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

        // --- File dialogs ---
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
        BuildUnsavedDialog();
    }

    private void DoLayout()
    {
        var win = GetViewportRect().Size;

        float menuH = _menuBar?.GetMinimumSize().Y ?? 30;
        if (_menuBar != null)
        {
            _menuBar.Position = Vector2.Zero;
            _menuBar.Size = new Vector2(win.X, menuH);
        }

        float navTop = menuH;
        if (_mapNavBar != null)
        {
            _mapNavBar.Position = new Vector2(0, navTop);
            _mapNavBar.Size = new Vector2(win.X, NavBarHeight);
        }

        float tbTop = navTop + NavBarHeight;
        if (_toolBar != null)
        {
            _toolBar.Position = new Vector2(0, tbTop);
            _toolBar.Size = new Vector2(win.X, ToolBarHeight);
        }

        float contentTop = tbTop + ToolBarHeight;
        float contentBottom = win.Y - StatusHeight;
        float contentH = contentBottom - contentTop;

        // Left sidebar: palette + tile info
        float tileInfoH = Math.Min(TileInfoHeight, contentH * 0.2f);
        float paletteH = contentH - tileInfoH;

        if (_palette != null)
        {
            _palette.Position = new Vector2(0, contentTop);
            _palette.Size = new Vector2(PaletteWidth, paletteH);
        }

        if (_tileInfoPanel != null)
        {
            _tileInfoPanel.Position = new Vector2(4, contentTop + paletteH);
            _tileInfoPanel.Size = new Vector2(PaletteWidth - 4, tileInfoH);
        }

        // Viewport
        if (_viewport != null)
        {
            float vpX = PaletteWidth + 2;
            _viewport.Position = new Vector2(vpX, contentTop);
            _viewport.Size = new Vector2(win.X - vpX, contentH);
        }

        // Status bar
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

    private void BuildUnsavedDialog()
    {
        _unsavedDialog = new ConfirmationDialog
        {
            Title = "Cambios sin guardar",
            DialogText = "El mapa tiene cambios sin guardar. ¿Qué desea hacer?",
            Size = new Vector2I(400, 150),
            OkButtonText = "Guardar",
        };
        _unsavedDialog.AddButton("Descartar", true, "discard");
        _unsavedDialog.Confirmed += () =>
        {
            // "Guardar" pressed
            OnSaveMap();
            _pendingAfterSaveCheck?.Invoke();
            _pendingAfterSaveCheck = null;
        };
        _unsavedDialog.CustomAction += (action) =>
        {
            if (action == "discard")
            {
                _unsavedDialog.Hide();
                _pendingAfterSaveCheck?.Invoke();
                _pendingAfterSaveCheck = null;
            }
        };
        _unsavedDialog.Canceled += () => { _pendingAfterSaveCheck = null; };
        AddChild(_unsavedDialog);
    }

    #region Data Loading

    private void TryAutoDetectDataPath()
    {
        // Try relative paths from the editor project
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

        _state.MapDir = mapsDir;

        // Find server directory
        string serverDir = "";
        foreach (var rel in new[] { "..", "../.." })
        {
            string candidate = Path.GetFullPath(Path.Combine(dataPath, rel, "server"));
            if (Directory.Exists(candidate)) { serverDir = candidate; break; }
        }

        // Prefer server maps dir
        if (serverDir.Length > 0)
        {
            string serverMapsDir = Path.Combine(serverDir, "maps");
            if (Directory.Exists(serverMapsDir))
            {
                _state.MapDir = serverMapsDir;
                GD.Print($"[Editor] Using server maps: {serverMapsDir}");
            }
        }

        // Load particle definitions
        string particlesIni = Path.Combine(dataPath, "INIT", "Particles.ini");
        _particles = new ParticleEngine();
        if (File.Exists(particlesIni))
            _particles.LoadDefinitions(particlesIni);

        // Load body + head data
        string personajesInd = Path.Combine(dataPath, "INIT", "Personajes.ind");
        (_npcBodyGrhs, _npcHeadOfsX, _npcHeadOfsY) = GameDataLoader.LoadBodyData(personajesInd);

        string cabezasInd = Path.Combine(dataPath, "INIT", "Cabezas.ind");
        _headGrhs = GameDataLoader.LoadHeadGrhs(cabezasInd);

        // Load object and NPC data from server dat/
        if (serverDir.Length > 0)
        {
            string datDir = Path.Combine(serverDir, "dat");
            string objDat = Path.Combine(datDir, "Obj.dat");
            _objGrhs = GameDataLoader.LoadObjectGrhs(objDat);
            string npcDat = Path.Combine(datDir, "NPCs.dat");
            (_npcBodies, _npcHeads) = GameDataLoader.LoadNpcData(npcDat);
            GD.Print($"[Editor] Server data: {datDir}");
        }

        // Push data to palette
        _palette!.Grhs = _grhs;
        _palette.Textures = _textures;
        _palette.Catalog = _catalog;
        _palette.Rebuild();

        // Push data to viewport
        SyncViewportData();

        // Scan available maps and update nav bar
        _state.ScanAvailableMaps(_state.MapDir);
        GD.Print($"[Editor] Found {_state.AvailableMaps.Count} maps in {_state.MapDir}");

        CreateNewMap(1);
    }

    private void SyncViewportData()
    {
        if (_viewport == null) return;
        _viewport.Grhs = _grhs;
        _viewport.Textures = _textures;
        _viewport.Particles = _particles;
        _viewport.ObjGrhs = _objGrhs;
        _viewport.NpcBodies = _npcBodies;
        _viewport.NpcHeads = _npcHeads;
        _viewport.NpcBodyGrhs = _npcBodyGrhs;
        _viewport.NpcHeadOfsX = _npcHeadOfsX;
        _viewport.NpcHeadOfsY = _npcHeadOfsY;
        _viewport.HeadGrhs = _headGrhs;
    }

    #endregion

    #region File Operations

    private void OnFileMenuId(long id)
    {
        switch (id)
        {
            case 0: RequestNewMap(); break;
            case 1: RequestOpenMap(); break;
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
            case 2: _state.CopySelection(_map!); SetStatus("Copiado"); break;
            case 3: PasteClipboard(); break;
            case 4: CutSelection(); break;
            case 5: DeleteSelection(); break;
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
            case 7: _state.ShowNpcs = !_state.ShowNpcs; break;
            case 8: _state.ShowObjects = !_state.ShowObjects; break;
            case 9: _state.ShowParticles = !_state.ShowParticles; break;
            case 10: _state.ShowLights = !_state.ShowLights; break;
        }

        if (_viewMenu != null)
        {
            bool val = id switch
            {
                0 => _state.ShowGrid, 1 => _state.ShowBlocked, 2 => _state.ShowExits,
                3 => _state.ShowLayer1, 4 => _state.ShowLayer2,
                5 => _state.ShowLayer3, 6 => _state.ShowLayer4,
                7 => _state.ShowNpcs, 8 => _state.ShowObjects,
                9 => _state.ShowParticles, 10 => _state.ShowLights,
                _ => false
            };
            int idx = _viewMenu.GetItemIndex((int)id);
            if (idx >= 0) _viewMenu.SetItemChecked(idx, val);
        }

        _viewport?.QueueRedraw();
    }

    /// <summary>
    /// Checks for unsaved changes before executing an action.
    /// If dirty, shows the save/discard/cancel dialog.
    /// If clean, executes immediately.
    /// </summary>
    private void CheckDirtyThen(Action action)
    {
        if (_state.IsDirty)
        {
            _pendingAfterSaveCheck = action;
            _unsavedDialog!.DialogText =
                $"Mapa {_state.CurrentMapNumber} tiene cambios sin guardar.\n¿Qué desea hacer?";
            _unsavedDialog.PopupCentered();
        }
        else
        {
            action();
        }
    }

    private void RequestNewMap()
    {
        CheckDirtyThen(() => CreateNewMap(_state.CurrentMapNumber));
    }

    private void RequestOpenMap()
    {
        CheckDirtyThen(() =>
        {
            if (!string.IsNullOrEmpty(_state.MapDir) && Directory.Exists(_state.MapDir))
                _openDialog!.CurrentDir = _state.MapDir;
            _openDialog!.Popup();
        });
    }

    private void RequestLoadMap(int mapNumber)
    {
        if (mapNumber == _state.CurrentMapNumber && _map != null) return;
        CheckDirtyThen(() => LoadMapByNumber(mapNumber));
    }

    private void CreateNewMap(int mapNumber)
    {
        _map = new MapData();
        _map.MapNumber = mapNumber;
        _map.Name = $"Mapa {mapNumber}";

        for (int y = 1; y <= 100; y++)
            for (int x = 1; x <= 100; x++)
                _map.Tiles[x, y].Layer1 = 1;

        _state.CurrentMapNumber = mapNumber;
        _undo.Clear();
        _state.ResetDirty();
        UpdateViewport();
        UpdateNavBar();
        SetStatus($"Mapa {mapNumber} nuevo (100x100)");
    }

    private void LoadMapByNumber(int mapNumber)
    {
        if (string.IsNullOrEmpty(_state.MapDir)) return;
        string mapFile = Path.Combine(_state.MapDir, $"Mapa{mapNumber}.map");
        if (!File.Exists(mapFile))
        {
            SetStatus($"Mapa{mapNumber}.map no existe en {_state.MapDir}");
            return;
        }

        _map = MapLoader.Load(_state.MapDir, mapNumber);
        _state.CurrentMapNumber = mapNumber;
        _undo.Clear();
        _state.ResetDirty();
        UpdateViewport();
        UpdateNavBar();
        if (_mapNumSpin != null) _mapNumSpin.Value = mapNumber;
        SetStatus($"Mapa {mapNumber} cargado — {_map.Name}");
    }

    private void OnMapFileSelected(string path)
    {
        string filename = Path.GetFileNameWithoutExtension(path);
        if (filename.StartsWith("Mapa", StringComparison.OrdinalIgnoreCase))
        {
            string numStr = filename.Substring(4);
            if (int.TryParse(numStr, out int mapNum))
            {
                string dir = Path.GetDirectoryName(path) ?? _state.MapDir;
                _state.MapDir = dir;
                _state.ScanAvailableMaps(dir);
                _map = MapLoader.Load(dir, mapNum);
                _state.CurrentMapNumber = mapNum;
                _undo.Clear();
                _state.ResetDirty();
                UpdateViewport();
                UpdateNavBar();
                if (_mapNumSpin != null) _mapNumSpin.Value = mapNum;
                SetStatus($"Mapa {mapNum} cargado — {_map.Name}");
                return;
            }
        }
        SetStatus($"ERROR: nombre invalido '{filename}'");
    }

    private void OnSaveMap()
    {
        if (_map == null) return;
        if (string.IsNullOrEmpty(_state.MapDir)) { OnSaveAsMap(); return; }
        MapLoader.Save(_state.MapDir, _map);
        _state.ResetDirty();
        _state.ScanAvailableMaps(_state.MapDir);
        UpdateNavBar();
        SetStatus($"Mapa {_map.MapNumber} guardado");
    }

    private void OnSaveAsMap()
    {
        if (!string.IsNullOrEmpty(_state.MapDir))
            _saveDialog!.CurrentDir = _state.MapDir;
        _saveDialog!.CurrentFile = $"Mapa{_map?.MapNumber ?? 1}.map";
        _saveDialog.Popup();
    }

    private void OnSaveFileSelected(string path)
    {
        if (_map == null) return;
        string dir = Path.GetDirectoryName(path) ?? _state.MapDir;
        _state.MapDir = dir;
        MapLoader.Save(dir, _map);
        _state.ResetDirty();
        _state.ScanAvailableMaps(dir);
        UpdateNavBar();
        SetStatus($"Mapa {_map.MapNumber} guardado en {dir}");
    }

    private void OnDataPathSelected(string path) => LoadDataPath(path);

    private void OnExitFollow(int mapNum, int x, int y)
    {
        RequestLoadMap(mapNum);
    }

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
        _state.MarkDirty();
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

    private void DeleteSelection()
    {
        if (_map == null || !_state.HasSelection) return;

        _undo.BeginBatch("Delete");
        for (int y = _state.SelY1; y <= _state.SelY2; y++)
            for (int x = _state.SelX1; x <= _state.SelX2; x++)
            {
                if (!_map.InBounds(x, y)) continue;
                var before = _map.Tiles[x, y];
                _map.Tiles[x, y] = new MapTile { Layer1 = 1 };
                _undo.RecordTileChange(x, y, before, _map.Tiles[x, y]);
            }
        _undo.EndBatch();
        _state.ClearSelection();
        _viewport?.QueueRedraw();
        SetStatus("Seleccion eliminada");
    }

    private void CutSelection()
    {
        if (_map == null || !_state.HasSelection) return;
        _state.CopySelection(_map);
        DeleteSelection();
        SetStatus("Cortado");
    }

    private void PasteClipboard()
    {
        if (_map == null || _state.Clipboard == null || !_state.HasSelection) return;

        _undo.BeginBatch("Paste");
        for (int y = 0; y < _state.ClipHeight; y++)
            for (int x = 0; x < _state.ClipWidth; x++)
            {
                int dx = _state.SelX1 + x;
                int dy = _state.SelY1 + y;
                if (!_map.InBounds(dx, dy)) continue;
                var before = _map.Tiles[dx, dy];
                _map.Tiles[dx, dy] = _state.Clipboard[x + 1, y + 1];
                _undo.RecordTileChange(dx, dy, before, _map.Tiles[dx, dy]);
            }
        _undo.EndBatch();
        _viewport?.QueueRedraw();
    }

    #endregion

    #region Map Navigation Bar

    private int _navOffset; // offset for paging through maps

    private void UpdateNavBar()
    {
        if (_mapNavButtons.Length == 0) return;
        int center = _state.CurrentMapNumber + _navOffset;
        int half = NavButtonCount / 2;

        for (int i = 0; i < NavButtonCount; i++)
        {
            int mapNum = center - half + i;
            var btn = _mapNavButtons[i];
            if (mapNum < 1)
            {
                btn.Text = "";
                btn.Disabled = true;
                btn.Modulate = Colors.White;
                continue;
            }

            btn.Text = mapNum.ToString();
            btn.Disabled = false;

            if (mapNum == _state.CurrentMapNumber)
            {
                btn.Modulate = new Color(0.5f, 1f, 0.5f); // Green = current
            }
            else if (_state.AvailableMaps.Contains(mapNum))
            {
                btn.Modulate = new Color(0.9f, 0.9f, 1f); // Bright = exists
            }
            else
            {
                btn.Modulate = new Color(0.4f, 0.4f, 0.4f); // Dim = doesn't exist
            }
        }
    }

    private void OnNavButtonPressed(int buttonIndex)
    {
        int center = _state.CurrentMapNumber + _navOffset;
        int half = NavButtonCount / 2;
        int mapNum = center - half + buttonIndex;
        if (mapNum < 1) return;

        if (_state.AvailableMaps.Contains(mapNum))
            RequestLoadMap(mapNum);
        else
            SetStatus($"Mapa{mapNum}.map no existe");
    }

    private void NavigateMapOffset(int delta)
    {
        _navOffset += delta;
        UpdateNavBar();
    }

    #endregion

    #region Input

    private static readonly string[] ToolNames = {
        "Mano", "Pintar", "Borrar", "Seleccionar", "Mover", "Agarrar",
        "Rellenar", "Cuentagotas", "Bloquear", "Luz", "Salida", "NPC", "Objeto", "Trigger"
    };

    public override void _UnhandledKeyInput(InputEvent @event)
    {
        if (@event is not InputEventKey key || !key.Pressed) return;

        bool ctrl = key.CtrlPressed;
        EditorTool? newTool = null;

        if (ctrl)
        {
            switch (key.Keycode)
            {
                case Key.Z: _undo.Undo(_map!); _viewport?.QueueRedraw(); break;
                case Key.Y: _undo.Redo(_map!); _viewport?.QueueRedraw(); break;
                case Key.S: OnSaveMap(); break;
                case Key.O: RequestOpenMap(); break;
                case Key.N: RequestNewMap(); break;
                case Key.X: CutSelection(); break;
                case Key.C: _state.CopySelection(_map!); SetStatus("Copiado"); break;
                case Key.V: PasteClipboard(); break;
            }
        }
        else
        {
            switch (key.Keycode)
            {
                case Key.H: newTool = EditorTool.Hand; break;
                case Key.P: newTool = EditorTool.Paint; break;
                case Key.E: newTool = EditorTool.Erase; break;
                case Key.R: newTool = EditorTool.Select; break;
                case Key.M: newTool = EditorTool.Move; break;
                case Key.V: newTool = EditorTool.Pick; break;
                case Key.F: newTool = EditorTool.Fill; break;
                case Key.I: newTool = EditorTool.Eyedrop; break;
                case Key.B: newTool = EditorTool.Block; break;
                case Key.G: _state.ShowGrid = !_state.ShowGrid; _viewport?.QueueRedraw(); break;
                case Key.T: ToggleTileProperties(); break;
                case Key.Key1: _state.ActiveLayer = 1; break;
                case Key.Key2: _state.ActiveLayer = 2; break;
                case Key.Key3: _state.ActiveLayer = 3; break;
                case Key.Key4: _state.ActiveLayer = 4; break;
                case Key.Delete:
                    DeleteSelection();
                    break;
                case Key.Escape:
                    _state.ClearSelection();
                    _state.Pick.Clear();
                    _viewport?.QueueRedraw();
                    break;
            }
        }

        if (newTool.HasValue)
        {
            _state.ActiveTool = newTool.Value;
            _state.Pick.Clear();
            SyncToolBar();
        }
    }

    #endregion

    #region Helpers

    // Maps toolbar button index to EditorTool
    private static readonly EditorTool[] ToolBarOrder = {
        EditorTool.Hand, EditorTool.Paint, EditorTool.Erase,
        EditorTool.Select, EditorTool.Move, EditorTool.Pick,
        EditorTool.Fill, EditorTool.Eyedrop, EditorTool.Block,
        // property tools (after separator)
        EditorTool.Light, EditorTool.Exit, EditorTool.Npc,
        EditorTool.Object, EditorTool.Trigger,
    };

    private void SyncToolBar()
    {
        for (int i = 0; i < _toolBarButtons.Length && i < ToolBarOrder.Length; i++)
        {
            bool active = _state.ActiveTool == ToolBarOrder[i];
            _toolBarButtons[i].ButtonPressed = active;
        }
    }

    private void OnDirtyChanged(bool isDirty)
    {
        UpdateTitle();
    }

    private void UpdateTitle()
    {
        string title = $"AO World Editor — Mapa {_state.CurrentMapNumber}";
        if (_map != null && !string.IsNullOrEmpty(_map.Name))
            title += $" ({_map.Name})";
        if (_state.IsDirty)
            title += " *";
        GetWindow().Title = title;
    }

    private void UpdateViewport()
    {
        if (_viewport != null)
        {
            _viewport.Map = _map;
            _viewport.QueueRedraw();

            if (_map != null)
            {
                float mapCenterX = (_map.Width / 2f + 1) * 32;
                float mapCenterY = (_map.Height / 2f + 1) * 32;
                float vpW = _viewport.Size.X > 0 ? _viewport.Size.X : 800;
                float vpH = _viewport.Size.Y > 0 ? _viewport.Size.Y : 600;
                _state.CameraOffset = new Vector2(
                    vpW / 2f - mapCenterX * _state.Zoom,
                    vpH / 2f - mapCenterY * _state.Zoom);
            }
        }
        if (_propsPanel != null)
            _propsPanel.Map = _map;
        if (_particles != null && _map != null)
        {
            _particles.BuildStreamsFromMap(_map);
            int pCount = 0, lCount = 0, nCount = 0, oCount = 0;
            for (int y = 1; y <= _map.Height; y++)
                for (int x = 1; x <= _map.Width; x++)
                {
                    if (_map.Tiles[x, y].ParticleGroup > 0) pCount++;
                    if (_map.Tiles[x, y].HasLight) lCount++;
                    if (_map.Tiles[x, y].HasNpc) nCount++;
                    if (_map.Tiles[x, y].HasObject) oCount++;
                }
            GD.Print($"[Editor] Map features: {nCount} NPCs, {oCount} objects, {pCount} particles, {lCount} lights");
        }
        UpdateTitle();
    }

    private void SetStatus(string msg)
    {
        if (_statusLabel != null) _statusLabel.Text = msg;
        GD.Print($"[Editor] {msg}");
    }

    private void UpdateTileInfo()
    {
        if (_tileInfoLabel == null || _map == null || !_state.HoverValid) return;
        int x = _state.HoverX, y = _state.HoverY;
        if (!_map.InBounds(x, y))
        {
            _tileInfoLabel.Text = "Fuera del mapa";
            return;
        }

        ref var tile = ref _map.Tiles[x, y];
        var lines = new System.Text.StringBuilder();
        lines.AppendLine($"Tile ({x}, {y})");
        lines.AppendLine($"L1:{tile.Layer1}  L2:{tile.Layer2}  L3:{tile.Layer3}  L4:{tile.Layer4}");
        if (tile.Blocked) lines.AppendLine("Bloqueado");
        if (tile.Trigger > 0) lines.AppendLine($"Trigger: {tile.Trigger}");
        if (tile.HasExit) lines.AppendLine($"Exit: M{tile.ExitMap} ({tile.ExitX},{tile.ExitY})");
        if (tile.HasNpc) lines.AppendLine($"NPC: {tile.NpcIndex}");
        if (tile.HasObject) lines.AppendLine($"Obj: {tile.ObjIndex} x{tile.ObjAmount}");
        if (tile.HasLight) lines.AppendLine($"Luz: R{tile.LightRange} ({tile.LightR},{tile.LightG},{tile.LightB})");
        if (tile.ParticleGroup > 0) lines.AppendLine($"Particula: {tile.ParticleGroup}");
        _tileInfoLabel.Text = lines.ToString().TrimEnd();
    }

    public override void _Process(double delta)
    {
        DoLayout();

        // Open tile properties when clicked with property tools
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

        _particles?.Update((float)delta);

        // Update status bar
        _coordLabel!.Text = _state.HoverValid ? $"({_state.HoverX}, {_state.HoverY})" : "";
        int toolIdx = (int)_state.ActiveTool;
        _toolLabel!.Text = toolIdx < ToolNames.Length ? ToolNames[toolIdx] : "?";
        _layerLabel!.Text = $"Capa {_state.ActiveLayer}";

        // Update tile info
        UpdateTileInfo();

        _palette?.SyncLayerUI();
        SyncToolBar();
        _viewport?.QueueRedraw();
    }

    public override void _Notification(int what)
    {
        // Intercept window close to check for unsaved changes
        if (what == NotificationWMCloseRequest)
        {
            if (_state.IsDirty)
            {
                CheckDirtyThen(() => GetTree().Quit());
                GetTree().AutoAcceptQuit = false;
            }
        }
    }

    #endregion
}
