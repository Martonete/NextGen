#nullable enable
using System;
using System.Collections.Generic;
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
    private NpcDatabase? _npcDb;
    private string _dataPath = "";
    private string _clientMapDir = "";  // client/Data/Maps/
    private string _serverMapDir = "";  // server/maps/
    private string _serverDatDir = "";  // server/dat/

    // Editor
    private readonly EditorState _state = new();
    private readonly UndoManager _undo = new();

    // UI components
    private MenuBar? _menuBar;
    private TabContainer? _sidebarTabs;
    private TilePalette? _palette;
    private NpcPalette? _npcPalette;
    private MapViewport? _viewport;
    private Window? _propsWindow;
    private TilePropertiesPanel? _propsPanel;
    private FileDialog? _openDialog;
    private FileDialog? _saveDialog;
    private FileDialog? _dataPathDialog;
    private FileDialog? _serverPathDialog;
    private PopupMenu? _viewMenu;

    // Walk mode
    private Window? _walkWindow;
    private WalkModePanel? _walkPanel;
    private BodyAnimData[]? _walkBodies;
    private HeadAnimData[]? _walkHeads;
    private Dictionary<int, DoorInfo>? _doorData;

    // Status bar
    private VBoxContainer? _statusOuter;
    private HBoxContainer? _statusBar;
    private Label? _statusLabel;
    private Label? _coordLabel;
    private Label? _layerLabel;
    private Label? _toolLabel;

    // Sidebar border
    private ColorRect? _sidebarBorder;
    private ColorRect? _headerBg; // opaque background behind toolbar+navbar to prevent viewport overflow

    // Right sidebar (selected texture preview)
    private PanelContainer? _rightSidebar;
    private TextureRect? _rightPreview;
    private Label? _rightNameLabel;
    private Label? _rightInfoLabel;

    // Tool bar (Excalidraw-style)
    private HBoxContainer? _toolBar;
    private Button[] _toolBarButtons = Array.Empty<Button>();
    private Button[] _layerTabButtons = new Button[4];

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

    // Texture preload loading screen
    private ColorRect? _loadingBg;
    private Label? _loadingTitle;
    private Label? _loadingLabel;
    private ProgressBar? _loadingBar;
    private Panel? _preloadOverlay;
    private int _preloadPhase; // 0=idle, 1=textures, 2=previews, 3=done
    private IEnumerator<int>? _texturePreloadIter;
    private IEnumerator<int>? _previewPreloadIter;
    private float _loadingFadeAlpha = 1f;
    private bool _loadingFadingOut;

    private const float PaletteWidth = 280;
    private const float StatusHeight = 28;
    private const float ToolBarHeight = 40;
    private const float TileInfoHeight = 110;
    private const float SidebarBorderWidth = 1;
    private const float RightSidebarWidth = 200;

    public override void _Ready()
    {
        // Wire dirty tracking
        _undo.Changed += () => _state.MarkDirty();
        _state.DirtyChanged += OnDirtyChanged;
        _state.ExitFollowRequested += OnExitFollow;

        BuildUI();
        BuildLoadingScreen();
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
        fileMenu.AddItem("Configurar Ruta Cliente (Data/)...", 6);
        fileMenu.AddItem("Configurar Ruta Server...", 7);
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
        _viewMenu.AddSeparator();
        _viewMenu.AddItem("Modo Caminata (F5)", 11);
        for (int id = 0; id <= 10; id++)
        {
            int idx = _viewMenu.GetItemIndex(id);
            if (idx >= 0) _viewMenu.SetItemChecked(idx, true);
        }
        _viewMenu.IdPressed += OnViewMenuId;
        _menuBar.AddChild(_viewMenu);

        AddChild(_menuBar);

        // --- Tool bar (professional grouped layout) ---
        _toolBar = new HBoxContainer();
        _toolBar.AddThemeConstantOverride("separation", 0);

        // -- Group 1: File operations --
        var fileGroup = EditorTheme.ToolBarGroup();
        var fileGroupH = new HBoxContainer();
        fileGroupH.AddThemeConstantOverride("separation", 4);
        fileGroupH.AddChild(EditorTheme.ActionButtonCompact("\ud83d\udcc2", "Abrir Mapa (Ctrl+O)", () => RequestOpenMap()));
        fileGroupH.AddChild(EditorTheme.ActionButtonCompact("\ud83d\udcbe", "Guardar (Ctrl+S)", () => OnSaveMap()));
        fileGroup.AddChild(fileGroupH);
        _toolBar.AddChild(fileGroup);

        _toolBar.AddChild(EditorTheme.ToolBarGroupSeparator());

        // -- Group 2: Undo / Redo --
        var undoGroup = EditorTheme.ToolBarGroup();
        var undoGroupH = new HBoxContainer();
        undoGroupH.AddThemeConstantOverride("separation", 4);
        undoGroupH.AddChild(EditorTheme.ActionButtonCompact("\u21a9", "Deshacer (Ctrl+Z)", () => { _undo.Undo(_map!); _viewport?.QueueRedraw(); }));
        undoGroupH.AddChild(EditorTheme.ActionButtonCompact("\u21aa", "Rehacer (Ctrl+Y)", () => { _undo.Redo(_map!); _viewport?.QueueRedraw(); }));
        undoGroup.AddChild(undoGroupH);
        _toolBar.AddChild(undoGroup);

        _toolBar.AddChild(EditorTheme.ToolBarGroupSeparator());

        // -- Group 3: Drawing tools --
        var drawGroup = EditorTheme.ToolBarGroup();
        var drawGroupH = new HBoxContainer();
        drawGroupH.AddThemeConstantOverride("separation", 4);

        var toolDefs = new (EditorTool tool, string icon, string label, string shortcut)[]
        {
            (EditorTool.Hand,    "\u270b", "Mano",        "H"),
            (EditorTool.Paint,   "\u270f", "Pintar",      "P"),
            (EditorTool.Erase,   "\u232b", "Borrar",      "E"),
            (EditorTool.Select,  "\u25a1", "Seleccionar", "R"),
            (EditorTool.Move,    "\u21c6", "Mover",       "M"),
            (EditorTool.Fill,    "\u25a8", "Rellenar",    "F"),
            (EditorTool.Pick,    "\u261d", "Agarrar",     "V"),
            (EditorTool.Eyedrop, "\u25ce", "Cuentagotas", "I"),
            (EditorTool.Block,   "\u2298", "Bloquear",    "B"),
        };
        _toolBarButtons = new Button[toolDefs.Length];
        for (int i = 0; i < toolDefs.Length; i++)
        {
            var (tool, icon, label, shortcut) = toolDefs[i];
            var btn = EditorTheme.ToolToggleCompact(icon, $"{label} ({shortcut})");
            var capturedTool = tool;
            btn.Pressed += () => SetActiveTool(capturedTool);
            drawGroupH.AddChild(btn);
            _toolBarButtons[i] = btn;
        }
        drawGroup.AddChild(drawGroupH);
        _toolBar.AddChild(drawGroup);

        _toolBar.AddChild(EditorTheme.ToolBarGroupSeparator());

        // -- Group 4: Property tools --
        var propGroup = EditorTheme.ToolBarGroup();
        var propGroupH = new HBoxContainer();
        propGroupH.AddThemeConstantOverride("separation", 4);

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
            var btn = EditorTheme.ToolToggleCompact(icon, label);
            var capturedTool = tool;
            btn.Pressed += () => SetActiveTool(capturedTool);
            propGroupH.AddChild(btn);
            extButtons[i] = btn;
        }
        propGroup.AddChild(propGroupH);
        _toolBar.AddChild(propGroup);

        // Merge all tool buttons for sync
        var allBtns = new Button[_toolBarButtons.Length + extButtons.Length];
        Array.Copy(_toolBarButtons, allBtns, _toolBarButtons.Length);
        Array.Copy(extButtons, 0, allBtns, _toolBarButtons.Length, extButtons.Length);
        _toolBarButtons = allBtns;

        _toolBar.AddChild(EditorTheme.ToolBarGroupSeparator());

        // -- Group 5: Layer tabs (compact) --
        var layerGroup = EditorTheme.ToolBarGroup();
        var layerGroupH = new HBoxContainer();
        layerGroupH.AddThemeConstantOverride("separation", 4);
        for (int li = 1; li <= 4; li++)
        {
            int capturedLayer = li;
            var layerBtn = EditorTheme.LayerTabCompact(li, () =>
            {
                _state.ActiveLayer = capturedLayer;
                SyncLayerTabs();
            });
            layerGroupH.AddChild(layerBtn);
            _layerTabButtons[li - 1] = layerBtn;
        }
        layerGroup.AddChild(layerGroupH);
        _toolBar.AddChild(layerGroup);

        AddChild(_toolBar);
        SyncToolBar();
        SyncLayerTabs();

        // Map nav bar removed — single map, auto-loads map 1

        // --- Left sidebar: TabContainer with Tiles + NPCs ---
        _sidebarTabs = new TabContainer();
        _sidebarTabs.TabAlignment = TabBar.AlignmentMode.Center;
        _sidebarTabs.AddThemeFontSizeOverride("font_size", EditorTheme.FONT_SM);
        // Style tab headers as pills
        var tabNormal = EditorTheme.FlatBox(EditorTheme.BG_TOOL_NORMAL, 8, 8, 3);
        var tabHover = EditorTheme.FlatBox(EditorTheme.BG_TOOL_HOVER, 8, 8, 3);
        var tabSelected = EditorTheme.FlatBox(EditorTheme.BG_TOOL_ACTIVE, 8, 8, 3, EditorTheme.ACCENT, 1);
        _sidebarTabs.AddThemeStyleboxOverride("tab_unselected", tabNormal);
        _sidebarTabs.AddThemeStyleboxOverride("tab_hovered", tabHover);
        _sidebarTabs.AddThemeStyleboxOverride("tab_selected", tabSelected);
        _sidebarTabs.AddThemeColorOverride("font_unselected_color", EditorTheme.TEXT_SECONDARY);
        _sidebarTabs.AddThemeColorOverride("font_hovered_color", EditorTheme.TEXT_PRIMARY);
        _sidebarTabs.AddThemeColorOverride("font_selected_color", Colors.White);
        _sidebarTabs.ClipContents = true;
        _sidebarTabs.MouseFilter = MouseFilterEnum.Stop;
        AddChild(_sidebarTabs);

        _palette = new TilePalette { Name = "Tiles", State = _state };
        _palette.LayerChanged += (layer) => SyncLayerTabs();
        _sidebarTabs.AddChild(_palette);

        _npcPalette = new NpcPalette { Name = "NPCs", State = _state };
        _npcPalette.NpcSelected += OnNpcPaletteSelected;
        _sidebarTabs.AddChild(_npcPalette);

        // --- Tile info panel (bottom of left sidebar, themed) ---
        _tileInfoPanel = new VBoxContainer();
        _tileInfoPanel.AddThemeConstantOverride("separation", 2);

        var infoHeader = EditorTheme.SectionLabel("TILE INFO");
        _tileInfoPanel.AddChild(infoHeader);

        _tileInfoLabel = EditorTheme.MakeLabel("", EditorTheme.TEXT_SECONDARY, EditorTheme.FONT_SM);
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
        _viewport.OnPendingAccept += CommitPendingPlacement;
        _viewport.OnPendingCancel += CancelPendingPlacement;
        AddChild(_viewport);

        // Opaque header background — covers viewport overflow in toolbar/navbar area
        _headerBg = new ColorRect { Color = EditorTheme.BG_PANEL, MouseFilter = MouseFilterEnum.Ignore };
        AddChild(_headerBg);

        // Sidebar right border (1px separator between sidebar and viewport)
        _sidebarBorder = new ColorRect { Color = EditorTheme.BORDER };
        AddChild(_sidebarBorder);

        // --- Right sidebar: selected texture preview ---
        _rightSidebar = new PanelContainer();
        _rightSidebar.AddThemeStyleboxOverride("panel",
            EditorTheme.FlatBox(EditorTheme.BG_PANEL, 0, 6, 4, EditorTheme.BORDER, 1));
        _rightSidebar.ClipContents = true;
        _rightSidebar.MouseFilter = MouseFilterEnum.Stop;

        var rightVBox = new VBoxContainer();
        rightVBox.AddThemeConstantOverride("separation", 6);

        var rsTitle = EditorTheme.SectionLabel("SELECTED TEXTURE");
        rightVBox.AddChild(rsTitle);

        _rightPreview = new TextureRect
        {
            CustomMinimumSize = new Vector2(128, 128),
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
            SizeFlagsHorizontal = SizeFlags.ShrinkCenter,
        };
        rightVBox.AddChild(_rightPreview);

        _rightNameLabel = EditorTheme.MakeLabel("(none)", EditorTheme.TEXT_PRIMARY, EditorTheme.FONT_SM);
        _rightNameLabel.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        rightVBox.AddChild(_rightNameLabel);

        _rightInfoLabel = EditorTheme.MakeLabel("", EditorTheme.TEXT_SECONDARY, EditorTheme.FONT_SM);
        _rightInfoLabel.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        rightVBox.AddChild(_rightInfoLabel);

        _rightSidebar.AddChild(rightVBox);
        AddChild(_rightSidebar);

        // --- Status bar (professional, with top border and pill indicators) ---
        _statusOuter = new VBoxContainer();
        _statusOuter.AddThemeConstantOverride("separation", 0);
        var statusOuter = _statusOuter;

        // Top border line (1px separator)
        var statusBorder = new ColorRect
        {
            Color = EditorTheme.BORDER,
            CustomMinimumSize = new Vector2(0, 1),
        };
        statusOuter.AddChild(statusBorder);

        _statusBar = new HBoxContainer();
        _statusBar.AddThemeConstantOverride("separation", 8);

        // Tool indicator pill
        _toolLabel = EditorTheme.MakeLabel("Pintar", Colors.White, EditorTheme.FONT_SM);
        var toolPill = EditorTheme.StatusPill(_toolLabel, EditorTheme.BG_BTN_SUCCESS);
        _statusBar.AddChild(toolPill);

        // Layer indicator pill
        _layerLabel = EditorTheme.MakeLabel("L1", Colors.White, EditorTheme.FONT_SM);
        var layerPill = EditorTheme.StatusPill(_layerLabel, new Color(0.15f, 0.15f, 0.20f));
        _statusBar.AddChild(layerPill);

        // Coordinates (monospace-style)
        _coordLabel = EditorTheme.MakeLabel("(0, 0)", EditorTheme.TEXT_PRIMARY, EditorTheme.FONT_SM);
        _statusBar.AddChild(_coordLabel);

        _statusLabel = EditorTheme.MakeLabel("Listo", EditorTheme.TEXT_MUTED, EditorTheme.FONT_SM);
        _statusLabel.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        _statusLabel.HorizontalAlignment = HorizontalAlignment.Right;
        _statusBar.AddChild(_statusLabel);

        statusOuter.AddChild(_statusBar);
        AddChild(statusOuter);

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
        _openDialog.AddFilter("*.aomap", "Mapa AO (nuevo)");
        _openDialog.AddFilter("*.map", "Mapa AO (legacy)");
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

        _serverPathDialog = new FileDialog
        {
            FileMode = FileDialog.FileModeEnum.OpenDir,
            Access = FileDialog.AccessEnum.Filesystem,
            Title = "Seleccionar carpeta server/ (contiene dat/ y maps/)",
            Size = new Vector2I(600, 400),
        };
        _serverPathDialog.DirSelected += OnServerPathSelected;
        AddChild(_serverPathDialog);

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
            _menuBar.ZIndex = 2;
        }

        float tbTop = menuH;
        if (_toolBar != null)
        {
            _toolBar.Position = new Vector2(4, tbTop);
            _toolBar.Size = new Vector2(win.X - 8, ToolBarHeight);
            _toolBar.ZIndex = 2;
        }

        float contentTop = tbTop + ToolBarHeight;
        float contentBottom = win.Y - StatusHeight;
        float contentH = contentBottom - contentTop;

        // Header background covers viewport overflow bleed in toolbar/navbar area
        // ZIndex: viewport=0 < headerBg=1 < toolbar/navbar/menu=2
        if (_headerBg != null)
        {
            _headerBg.Position = Vector2.Zero;
            _headerBg.Size = new Vector2(win.X, contentTop);
            _headerBg.ZIndex = 1;
        }

        // Left sidebar: palette + tile info
        float tileInfoH = Math.Min(TileInfoHeight, contentH * 0.2f);
        float paletteH = contentH - tileInfoH;

        if (_sidebarTabs != null)
        {
            _sidebarTabs.Position = new Vector2(0, contentTop);
            _sidebarTabs.Size = new Vector2(PaletteWidth, paletteH);
            _sidebarTabs.ZIndex = 2;
        }

        if (_tileInfoPanel != null)
        {
            _tileInfoPanel.Position = new Vector2(8, contentTop + paletteH + 4);
            _tileInfoPanel.Size = new Vector2(PaletteWidth - 12, tileInfoH - 4);
        }

        // Sidebar right border (1px line)
        if (_sidebarBorder != null)
        {
            _sidebarBorder.Position = new Vector2(PaletteWidth, contentTop);
            _sidebarBorder.Size = new Vector2(SidebarBorderWidth, contentH);
        }

        // Right sidebar
        if (_rightSidebar != null)
        {
            _rightSidebar.Position = new Vector2(win.X - RightSidebarWidth, contentTop);
            _rightSidebar.Size = new Vector2(RightSidebarWidth, contentH);
            _rightSidebar.ZIndex = 2;
        }

        // Viewport (between left and right sidebars)
        if (_viewport != null)
        {
            float vpX = PaletteWidth + SidebarBorderWidth;
            float vpW = win.X - vpX - RightSidebarWidth;
            _viewport.Position = new Vector2(vpX, contentTop);
            _viewport.Size = new Vector2(vpW, contentH);
        }

        // Status bar (outer container with border line + bar)
        if (_statusOuter != null)
        {
            _statusOuter.Position = new Vector2(0, contentBottom);
            _statusOuter.Size = new Vector2(win.X, StatusHeight);
        }

        // Loading screen overlay
        LayoutLoadingScreen();
    }

    private void BuildMapPropsDialog()
    {
        _mapPropsDialog = new AcceptDialog { Title = "Propiedades del Mapa", Size = new Vector2I(420, 320) };
        var vbox = new VBoxContainer();
        vbox.AddThemeConstantOverride("separation", 6);

        vbox.AddChild(EditorTheme.Heading("Propiedades del Mapa"));

        var grid = new GridContainer { Columns = 2 };
        grid.AddThemeConstantOverride("h_separation", 10);
        grid.AddThemeConstantOverride("v_separation", 6);

        grid.AddChild(EditorTheme.MakeLabel("Nombre:", EditorTheme.TEXT_SECONDARY));
        _mapNameEdit = new LineEdit { CustomMinimumSize = new Vector2(200, 0) };
        _mapNameEdit.AddThemeFontSizeOverride("font_size", EditorTheme.FONT_MD);
        grid.AddChild(_mapNameEdit);

        grid.AddChild(EditorTheme.MakeLabel("Musica:", EditorTheme.TEXT_SECONDARY));
        _mapMusicSpin = EditorTheme.MakeSpinBox(0, 999, 1);
        grid.AddChild(_mapMusicSpin);

        grid.AddChild(EditorTheme.MakeLabel("PvP:", EditorTheme.TEXT_SECONDARY));
        _mapPkCheck = new CheckBox();
        grid.AddChild(_mapPkCheck);

        vbox.AddChild(grid);
        vbox.AddChild(EditorTheme.MakeHSeparator());
        vbox.AddChild(EditorTheme.SectionLabel("Luz Ambiental"));

        var ambGrid = new GridContainer { Columns = 2 };
        ambGrid.AddThemeConstantOverride("h_separation", 10);
        ambGrid.AddThemeConstantOverride("v_separation", 6);

        ambGrid.AddChild(EditorTheme.MakeLabel("R:", EditorTheme.TEXT_DANGER));
        _mapAmbR = EditorTheme.MakeSpinBox(0, 255, 1, 180);
        ambGrid.AddChild(_mapAmbR);

        ambGrid.AddChild(EditorTheme.MakeLabel("G:", EditorTheme.TEXT_SUCCESS));
        _mapAmbG = EditorTheme.MakeSpinBox(0, 255, 1, 180);
        ambGrid.AddChild(_mapAmbG);

        ambGrid.AddChild(EditorTheme.MakeLabel("B:", EditorTheme.TEXT_ACCENT));
        _mapAmbB = EditorTheme.MakeSpinBox(0, 255, 1, 180);
        ambGrid.AddChild(_mapAmbB);

        vbox.AddChild(ambGrid);
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

    private void BuildLoadingScreen()
    {
        _preloadOverlay = new Panel();
        _preloadOverlay.ZIndex = 100;
        var style = new StyleBoxFlat();
        style.BgColor = new Color(0.08f, 0.08f, 0.10f, 1f);
        _preloadOverlay.AddThemeStyleboxOverride("panel", style);
        AddChild(_preloadOverlay);

        _loadingBg = new ColorRect();
        _loadingBg.Color = new Color(0.08f, 0.08f, 0.10f, 1f);
        _loadingBg.MouseFilter = MouseFilterEnum.Stop;
        _preloadOverlay.AddChild(_loadingBg);

        _loadingTitle = new Label();
        _loadingTitle.Text = "World Editor";
        _loadingTitle.HorizontalAlignment = HorizontalAlignment.Center;
        _loadingTitle.AddThemeFontSizeOverride("font_size", 24);
        _loadingTitle.AddThemeColorOverride("font_color", EditorTheme.TEXT_PRIMARY);
        _preloadOverlay.AddChild(_loadingTitle);

        _loadingLabel = new Label();
        _loadingLabel.Text = "Cargando texturas...";
        _loadingLabel.HorizontalAlignment = HorizontalAlignment.Center;
        _loadingLabel.AddThemeFontSizeOverride("font_size", 12);
        _loadingLabel.AddThemeColorOverride("font_color", EditorTheme.TEXT_SECONDARY);
        _preloadOverlay.AddChild(_loadingLabel);

        _loadingBar = new ProgressBar();
        _loadingBar.MinValue = 0;
        _loadingBar.MaxValue = 100;
        _loadingBar.Value = 0;
        _loadingBar.ShowPercentage = false;
        var barBg = new StyleBoxFlat();
        barBg.BgColor = new Color(0.15f, 0.15f, 0.20f);
        barBg.SetCornerRadiusAll(4);
        _loadingBar.AddThemeStyleboxOverride("background", barBg);
        var barFill = new StyleBoxFlat();
        barFill.BgColor = EditorTheme.ACCENT;
        barFill.SetCornerRadiusAll(4);
        _loadingBar.AddThemeStyleboxOverride("fill", barFill);
        _preloadOverlay.AddChild(_loadingBar);
    }

    private void LayoutLoadingScreen()
    {
        if (_preloadOverlay == null) return;
        var win = GetViewportRect().Size;
        _preloadOverlay.Position = Vector2.Zero;
        _preloadOverlay.Size = win;
        if (_loadingBg != null) { _loadingBg.Position = Vector2.Zero; _loadingBg.Size = win; }

        float barW = 400;
        float barH = 20;
        float cx = (win.X - barW) / 2;
        float cy = win.Y / 2;

        if (_loadingTitle != null) { _loadingTitle.Position = new Vector2(cx, cy - 80); _loadingTitle.Size = new Vector2(barW, 30); }
        if (_loadingLabel != null) { _loadingLabel.Position = new Vector2(cx, cy - 20); _loadingLabel.Size = new Vector2(barW, 24); }
        if (_loadingBar != null) { _loadingBar.Position = new Vector2(cx, cy + 20); _loadingBar.Size = new Vector2(barW, barH); }
    }

    #region Data Loading

    private void TryAutoDetectDataPath()
    {
        // Build candidate list from multiple base directories to handle
        // different working directories and project layouts.
        var candidates = new System.Collections.Generic.List<string>();

        // Relative to current working directory
        candidates.Add("../../client/Data");
        candidates.Add("../../../client/Data");
        candidates.Add("client/Data");

        // Relative to the project.godot file (res:// → filesystem path)
        string resPath = ProjectSettings.GlobalizePath("res://");
        candidates.Add(Path.Combine(resPath, "../../client/Data"));
        candidates.Add(Path.Combine(resPath, "../../../client/Data"));

        // Relative to the executable
        string exeDir = OS.GetExecutablePath().GetBaseDir();
        candidates.Add(Path.Combine(exeDir, "../../client/Data"));
        candidates.Add(Path.Combine(exeDir, "../../../client/Data"));
        candidates.Add(Path.Combine(exeDir, "Data"));

        // User data fallback
        candidates.Add(Path.Combine(OS.GetUserDataDir(), "Data"));

        foreach (var candidate in candidates)
        {
            try
            {
                string full = Path.GetFullPath(candidate);
                if (Directory.Exists(full) && File.Exists(Path.Combine(full, "INIT", "Graficos.ind")))
                {
                    LoadDataPath(full);
                    return;
                }
            }
            catch { /* invalid path, skip */ }
        }

        SetStatus("Data/ no encontrada. Configure las rutas.");
        // Hide loading bar, show setup form instead
        if (_loadingBar != null) _loadingBar.Visible = false;
        if (_loadingLabel != null) _loadingLabel.Visible = false;
        if (_loadingTitle != null) _loadingTitle.Visible = false;
        if (_preloadOverlay != null) { _preloadOverlay.Visible = true; _preloadOverlay.Modulate = Colors.White; }
        _loadingFadeAlpha = 1f;
        _preloadPhase = 0;
        CallDeferred(MethodName.ShowSetupForm);
    }

    // ── Setup form (shown when data paths not found) ──
    private Window? _setupWindow;
    private LineEdit? _setupClientPath;
    private LineEdit? _setupServerPath;

    private void ShowSetupForm()
    {
        if (_setupWindow != null) { _setupWindow.Show(); return; }

        _setupWindow = new Window();
        _setupWindow.Title = "Configuración Inicial";
        _setupWindow.Size = new Vector2I(500, 260);
        _setupWindow.Exclusive = true;
        _setupWindow.Unresizable = true;
        _setupWindow.CloseRequested += () => _setupWindow.Hide();

        var panel = new PanelContainer();
        panel.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        panel.AddThemeStyleboxOverride("panel", EditorTheme.FlatBox(EditorTheme.BG_PANEL));
        _setupWindow.AddChild(panel);

        var margin = new MarginContainer();
        margin.AddThemeConstantOverride("margin_top", 16);
        margin.AddThemeConstantOverride("margin_left", 20);
        margin.AddThemeConstantOverride("margin_right", 20);
        margin.AddThemeConstantOverride("margin_bottom", 16);
        panel.AddChild(margin);

        var vbox = new VBoxContainer();
        vbox.AddThemeConstantOverride("separation", 10);
        margin.AddChild(vbox);

        vbox.AddChild(EditorTheme.Heading("Configurar Rutas"));

        // Client path
        vbox.AddChild(EditorTheme.MakeLabel("Carpeta del Cliente"));
        var clientRow = new HBoxContainer();
        clientRow.AddThemeConstantOverride("separation", 6);
        _setupClientPath = new LineEdit { PlaceholderText = "Ej: C:/Proyecto/client" };
        _setupClientPath.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        clientRow.AddChild(_setupClientPath);
        var clientBrowse = EditorTheme.MakeButton("...");
        clientBrowse.Pressed += () =>
        {
            var dlg = new FileDialog { FileMode = FileDialog.FileModeEnum.OpenDir, Access = FileDialog.AccessEnum.Filesystem, Title = "Seleccionar carpeta Data/ del cliente" };
            dlg.DirSelected += (path) => { _setupClientPath.Text = path; dlg.QueueFree(); };
            dlg.Canceled += () => dlg.QueueFree();
            _setupWindow.AddChild(dlg);
            dlg.PopupCentered(new Vector2I(600, 400));
        };
        clientRow.AddChild(clientBrowse);
        vbox.AddChild(clientRow);

        // Server path
        vbox.AddChild(EditorTheme.MakeLabel("Carpeta del Server"));
        var serverRow = new HBoxContainer();
        serverRow.AddThemeConstantOverride("separation", 6);
        _setupServerPath = new LineEdit { PlaceholderText = "Ej: C:/Proyecto/server" };
        _setupServerPath.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        serverRow.AddChild(_setupServerPath);
        var serverBrowse = EditorTheme.MakeButton("...");
        serverBrowse.Pressed += () =>
        {
            var dlg = new FileDialog { FileMode = FileDialog.FileModeEnum.OpenDir, Access = FileDialog.AccessEnum.Filesystem, Title = "Seleccionar carpeta dat/ del server" };
            dlg.DirSelected += (path) => { _setupServerPath.Text = path; dlg.QueueFree(); };
            dlg.Canceled += () => dlg.QueueFree();
            _setupWindow.AddChild(dlg);
            dlg.PopupCentered(new Vector2I(600, 400));
        };
        serverRow.AddChild(serverBrowse);
        vbox.AddChild(serverRow);

        // Buttons
        var btnRow = new HBoxContainer();
        btnRow.AddThemeConstantOverride("separation", 10);
        btnRow.Alignment = BoxContainer.AlignmentMode.End;
        var applyBtn = EditorTheme.PrimaryButton("Aplicar");
        applyBtn.Pressed += OnSetupApply;
        btnRow.AddChild(applyBtn);
        var closeBtn = EditorTheme.MakeButton("Cerrar");
        closeBtn.Pressed += () => _setupWindow.Hide();
        btnRow.AddChild(closeBtn);
        vbox.AddChild(btnRow);

        AddChild(_setupWindow);
        _setupWindow.PopupCentered();
    }

    private void OnSetupApply()
    {
        string clientPath = _setupClientPath?.Text.Trim() ?? "";
        string serverPath = _setupServerPath?.Text.Trim() ?? "";

        if (clientPath.Length == 0 || !Directory.Exists(clientPath))
        {
            SetStatus("La carpeta del cliente no existe.");
            return;
        }

        // Resolve Data/ subfolder automatically
        string dataPath = clientPath;
        string dataSubDir = Path.Combine(clientPath, "Data");
        if (Directory.Exists(dataSubDir))
            dataPath = dataSubDir;

        // Verify required files exist
        string graficosInd = Path.Combine(dataPath, "INIT", "Graficos.ind");
        if (!File.Exists(graficosInd))
        {
            SetStatus($"No se encontró Graficos.ind en {dataPath}/INIT/");
            return;
        }

        if (_setupWindow != null) _setupWindow.Hide();
        // Restore loading screen elements
        if (_loadingBar != null) _loadingBar.Visible = true;
        if (_loadingLabel != null) _loadingLabel.Visible = true;
        if (_loadingTitle != null) _loadingTitle.Visible = true;
        LoadDataPath(dataPath);

        // Server path — resolve dat/ subfolder
        if (serverPath.Length > 0 && Directory.Exists(serverPath))
        {
            string datSub = Path.Combine(serverPath, "dat");
            _serverMapDir = Directory.Exists(datSub) ? datSub : serverPath;
        }
    }

    private void LoadDataPath(string dataPath)
    {
        GD.Print($"[Editor] LoadDataPath: {dataPath}");
        _dataPath = dataPath;
        string graficosInd = Path.Combine(dataPath, "INIT", "Graficos.ind");
        string graficosDir = Path.Combine(dataPath, "Graficos");
        // indices.ini lives in client Data/INIT/ — load via System.IO
        string[]? indicesLines = null;
        string indicesPath = Path.Combine(dataPath, "INIT", "indices.ini");
        if (File.Exists(indicesPath))
        {
            indicesLines = File.ReadAllLines(indicesPath);
            GD.Print($"[Editor] Loaded indices.ini from {indicesPath} ({indicesLines.Length} lines)");
        }
        // Fallback: try res:// (legacy editor-bundled location)
        if (indicesLines == null && Godot.FileAccess.FileExists("res://indices.ini"))
        {
            using var f = Godot.FileAccess.Open("res://indices.ini", Godot.FileAccess.ModeFlags.Read);
            if (f != null)
            {
                string text = f.GetAsText();
                indicesLines = text.Split('\n');
                GD.Print($"[Editor] Loaded indices.ini from res:// ({indicesLines.Length} lines)");
            }
        }
        string mapsDir = Path.Combine(dataPath, "Maps");

        if (!File.Exists(graficosInd))
        {
            GD.PrintErr($"[Editor] Graficos.ind NOT FOUND at: {graficosInd}");
            SetStatus($"ERROR: {graficosInd} no encontrado");
            _preloadPhase = 0;
            if (_preloadOverlay != null) _preloadOverlay.Visible = false;
            return;
        }
        GD.Print($"[Editor] Found Graficos.ind: {graficosInd}");

        _grhs = GrhLoader.Load(graficosInd);
        _textures = new TextureManager(graficosDir);

        if (indicesLines != null)
        {
            _catalog = TextureCatalog.LoadFromLines(indicesLines);
            SetStatus($"{_grhs.Length} GRHs, {_catalog.AllRefs.Count} texturas, {_catalog.Categories.Count} categorias");
        }
        else
        {
            _catalog = new TextureCatalog();
            SetStatus($"{_grhs.Length} GRHs (indices.ini no encontrado)");
        }

        // Client maps directory
        _clientMapDir = mapsDir;
        _state.MapDir = mapsDir;

        // Find server directory (auto-detect relative to client Data/)
        string serverDir = _serverDatDir.Length > 0
            ? Path.GetDirectoryName(_serverDatDir) ?? ""
            : "";
        if (serverDir.Length == 0)
        {
            foreach (var rel in new[] { "..", "../.." })
            {
                string candidate = Path.GetFullPath(Path.Combine(dataPath, rel, "server"));
                if (Directory.Exists(candidate)) { serverDir = candidate; break; }
            }
        }

        // Store server paths
        if (serverDir.Length > 0)
        {
            _serverDatDir = Path.Combine(serverDir, "dat");
            string serverMapsDir = Path.Combine(serverDir, "maps");
            if (Directory.Exists(serverMapsDir))
            {
                _serverMapDir = serverMapsDir;
                _state.MapDir = serverMapsDir; // Prefer server maps for loading
                GD.Print($"[Editor] Server maps: {serverMapsDir}");
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
        if (_serverDatDir.Length > 0 && Directory.Exists(_serverDatDir))
        {
            string objDat = Path.Combine(_serverDatDir, "Obj.dat");
            _objGrhs = GameDataLoader.LoadObjectGrhs(objDat);
            _doorData = GameDataLoader.LoadDoorData(objDat);
            string npcDat = Path.Combine(_serverDatDir, "NPCs.dat");
            (_npcBodies, _npcHeads) = GameDataLoader.LoadNpcData(npcDat);
            _npcDb = NpcDatabase.Load(_serverDatDir);
            GD.Print($"[Editor] Server data: {_serverDatDir}");
        }
        else
        {
            GD.Print("[Editor] Server dat/ not found — NPC names unavailable. Use Archivo > Configurar Ruta Server");
        }

        // Push data to palette
        _palette!.Grhs = _grhs;
        _palette.Textures = _textures;
        _palette.Catalog = _catalog;
        _palette.IndicesPath = System.IO.Path.Combine(_dataPath, "INIT", "indices.ini");
        _palette.Rebuild();

        // Push data to NPC palette
        SyncNpcPaletteData();

        // Push data to viewport
        SyncViewportData();

        // Scan available maps and update nav bar
        _state.ScanAvailableMaps(_state.MapDir);
        GD.Print($"[Editor] Found {_state.AvailableMaps.Count} maps in {_state.MapDir}");

        // Show path status
        string pathInfo = "";
        if (_clientMapDir.Length > 0) pathInfo += $"Cliente: {_clientMapDir}  ";
        if (_serverMapDir.Length > 0) pathInfo += $"Server: {_serverMapDir}  ";
        if (_serverMapDir.Length == 0)
            pathInfo += "⚠ Sin ruta server — Archivo > Configurar Ruta Server";
        if (_clientMapDir.Length == 0)
            pathInfo += "⚠ Sin ruta cliente";
        GD.Print($"[Editor] {pathInfo}");

        // Auto-load map 1 if it exists, otherwise create empty map
        if (_state.AvailableMaps.Contains(1))
            LoadMapByNumber(1);
        else
            CreateNewMap(1);

        // Start texture preload — blocks editor interaction until complete
        if (_textures != null && _grhs != null)
        {
            _preloadPhase = 1;
            _texturePreloadIter = _textures.PreloadAll(_grhs);
            _loadingFadingOut = false;
            _loadingFadeAlpha = 1f;
            if (_preloadOverlay != null) { _preloadOverlay.Visible = true; _preloadOverlay.Modulate = Colors.White; }
            GD.Print($"[Editor] Starting preload: {_textures.PreloadTotal} textures from {_grhs.Length} GRHs");
            SetStatus("Cargando texturas...");
        }
        else
        {
            // No data loaded — skip preload, hide loading screen
            _preloadPhase = 0;
            if (_preloadOverlay != null) _preloadOverlay.Visible = false;
        }
    }

    private void TickTexturePreload()
    {
        if (_preloadPhase == 1 && _texturePreloadIter != null && _textures != null)
        {
            bool done = _textures.TickPreload(_texturePreloadIter, 12.0);
            float progress = _textures.PreloadTotal > 0
                ? (float)_textures.PreloadDone / _textures.PreloadTotal : 1f;
            if (_loadingBar != null) _loadingBar.Value = progress * 80f; // 0-80% for textures
            if (_loadingLabel != null)
                _loadingLabel.Text = $"Cargando texturas... ({_textures.PreloadDone}/{_textures.PreloadTotal})";

            if (done)
            {
                _preloadPhase = 2;
                _texturePreloadIter = null;
                _previewPreloadIter = _palette?.PreloadAllPreviews();
                if (_loadingLabel != null) _loadingLabel.Text = "Generando previews...";
            }
        }
        else if (_preloadPhase == 2 && _previewPreloadIter != null)
        {
            // Time-budget preview generation
            var sw = System.Diagnostics.Stopwatch.StartNew();
            int count = 0;
            while (sw.Elapsed.TotalMilliseconds < 8.0)
            {
                if (!_previewPreloadIter.MoveNext()) { _preloadPhase = 3; break; }
                count++;
            }
            if (_loadingBar != null) _loadingBar.Value = 80f + 20f * (_preloadPhase == 3 ? 1f : 0.5f);
            if (_loadingLabel != null && _preloadPhase != 3)
                _loadingLabel.Text = $"Generando previews... ({count})";

            if (_preloadPhase == 3)
            {
                _previewPreloadIter = null;
                if (_loadingLabel != null) _loadingLabel.Text = "Listo!";
                if (_loadingBar != null) _loadingBar.Value = 100;
                _loadingFadingOut = true;
                SetStatus("Editor listo");
            }
        }
        else if (_preloadPhase == 3 && _loadingFadingOut)
        {
            _loadingFadeAlpha -= 0.05f;
            if (_loadingFadeAlpha <= 0f)
            {
                _loadingFadeAlpha = 0f;
                _loadingFadingOut = false;
                _preloadPhase = 0;
                if (_preloadOverlay != null) _preloadOverlay.Visible = false;
            }
            else if (_preloadOverlay != null)
            {
                _preloadOverlay.Modulate = new Color(1, 1, 1, _loadingFadeAlpha);
            }
        }
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
        _viewport.NpcDb = _npcDb;
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
            case 7: _serverPathDialog?.Popup(); break;
        }
    }

    private void OnEditMenuId(long id)
    {
        switch (id)
        {
            case 0: _undo.Undo(_map!); _viewport?.QueueRedraw(); break;
            case 1: _undo.Redo(_map!); _viewport?.QueueRedraw(); break;
            case 2: _state.CopySelection(_map!); SetStatus($"Copiado {_state.ClipWidth}x{_state.ClipHeight} tiles"); break;
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
            case 11: OpenWalkMode(); return; // not a checkbox — early return
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
        string aomapFile = Path.Combine(_state.MapDir, $"Mapa{mapNumber}.aomap");
        string mapFile = Path.Combine(_state.MapDir, $"Mapa{mapNumber}.map");
        if (!File.Exists(aomapFile) && !File.Exists(mapFile))
        {
            SetStatus($"Mapa{mapNumber} no existe en {_state.MapDir}");
            return;
        }

        _map = MapLoader.Load(_state.MapDir, mapNumber);
        _state.CurrentMapNumber = mapNumber;
        _undo.Clear();
        _state.ResetDirty();
        UpdateViewport();
        UpdateNavBar();
        SetStatus($"Mapa {mapNumber} cargado ({_map.Width}x{_map.Height}) — {_map.Name}");
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

        // Save to primary map dir (server if available — all 3 files)
        MapLoader.Save(_state.MapDir, _map);

        // Dual-save: client gets .map only, server gets all 3
        string secondaryDir = "";
        bool secondaryMapOnly = false;
        if (_state.MapDir == _serverMapDir && _clientMapDir.Length > 0 && Directory.Exists(_clientMapDir))
        {
            secondaryDir = _clientMapDir;
            secondaryMapOnly = true;  // Client only needs .map
        }
        else if (_state.MapDir == _clientMapDir && _serverMapDir.Length > 0 && Directory.Exists(_serverMapDir))
        {
            secondaryDir = _serverMapDir;
            secondaryMapOnly = false; // Server needs all 3
        }

        if (secondaryDir.Length > 0)
        {
            MapLoader.Save(secondaryDir, _map, mapOnly: secondaryMapOnly);
            GD.Print($"[Editor] Dual-save: also saved to {secondaryDir} (mapOnly={secondaryMapOnly})");
        }

        _state.ResetDirty();
        _state.ScanAvailableMaps(_state.MapDir);
        UpdateNavBar();
        string dualMsg = secondaryDir.Length > 0 ? " (cliente .map + server .map/.inf/.dat)" : "";
        SetStatus($"Mapa {_map.MapNumber} guardado{dualMsg}");
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

        // Dual-save: client gets .map only, server gets all 3
        string otherDir = "";
        bool otherMapOnly = false;
        if (dir != _clientMapDir && _clientMapDir.Length > 0 && Directory.Exists(_clientMapDir))
        {
            otherDir = _clientMapDir;
            otherMapOnly = true;  // Client only needs .map
        }
        else if (dir != _serverMapDir && _serverMapDir.Length > 0 && Directory.Exists(_serverMapDir))
        {
            otherDir = _serverMapDir;
            otherMapOnly = false; // Server needs all 3
        }
        if (otherDir.Length > 0)
            MapLoader.Save(otherDir, _map, mapOnly: otherMapOnly);

        _state.ResetDirty();
        _state.ScanAvailableMaps(dir);
        UpdateNavBar();
        string dualMsg = otherDir.Length > 0 ? " (cliente .map + server .map/.inf/.dat)" : "";
        SetStatus($"Mapa {_map.MapNumber} guardado en {dir}{dualMsg}");
    }

    private void OnDataPathSelected(string path) => LoadDataPath(path);

    private void OnServerPathSelected(string path)
    {
        // Validate: must contain dat/ subfolder
        string datDir = Path.Combine(path, "dat");
        if (!Directory.Exists(datDir))
        {
            SetStatus($"ERROR: {path} no contiene carpeta dat/");
            return;
        }

        _serverDatDir = datDir;
        string mapsDir = Path.Combine(path, "maps");
        if (Directory.Exists(mapsDir))
        {
            _serverMapDir = mapsDir;
            _state.MapDir = mapsDir;
            _state.ScanAvailableMaps(mapsDir);
            GD.Print($"[Editor] Server maps: {mapsDir}");
        }

        // Load NPC + object data from the new server path
        string objDat = Path.Combine(datDir, "Obj.dat");
        _objGrhs = GameDataLoader.LoadObjectGrhs(objDat);
        _doorData = GameDataLoader.LoadDoorData(objDat);
        string npcDat = Path.Combine(datDir, "NPCs.dat");
        (_npcBodies, _npcHeads) = GameDataLoader.LoadNpcData(npcDat);
        _npcDb = NpcDatabase.Load(datDir);

        SyncNpcPaletteData();
        SyncViewportData();
        UpdateNavBar();
        SetStatus($"Server configurado: {path}");
    }

    private void SyncNpcPaletteData()
    {
        if (_npcPalette == null) return;
        _npcPalette.Database = _npcDb;
        _npcPalette.Grhs = _grhs;
        _npcPalette.Textures = _textures;
        _npcPalette.NpcBodies = _npcBodies;
        _npcPalette.NpcBodyGrhs = _npcBodyGrhs;
        _npcPalette.NpcHeads = _npcHeads;
        _npcPalette.HeadGrhs = _headGrhs;
        _npcPalette.NpcHeadOfsX = _npcHeadOfsX;
        _npcPalette.NpcHeadOfsY = _npcHeadOfsY;
        _npcPalette.Rebuild();
    }

    private void OnNpcPaletteSelected(int npcNumber)
    {
        // Set NPC tool as active and update tile properties for quick placement
        SetActiveTool(EditorTool.Npc);
        _state.SelectedNpcNumber = npcNumber;
        SetStatus($"NPC #{npcNumber} seleccionado — click en el mapa para colocar");
    }

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

    private void OpenWalkMode()
    {
        if (_map == null)
        {
            SetStatus("Carga un mapa primero para usar el modo caminata.");
            return;
        }

        // Load body/head data once
        if (_walkBodies == null && _dataPath.Length > 0)
        {
            string initDir = Path.Combine(_dataPath, "INIT");
            _walkBodies = WalkModeData.LoadBodies(Path.Combine(initDir, "Personajes.ind"));
            _walkHeads = WalkModeData.LoadHeads(Path.Combine(initDir, "Cabezas.ind"));
        }

        // Create window if needed
        if (_walkWindow == null)
        {
            _walkWindow = new Window
            {
                Title = "Modo Caminata",
                Size = new Vector2I(544, 416),
                Visible = false,
                Exclusive = false,
                AlwaysOnTop = true,
                Unresizable = true, // fixed to match AO viewport exactly
                ContentScaleMode = Window.ContentScaleModeEnum.Disabled,
            };
            _walkWindow.CloseRequested += () => _walkWindow.Visible = false;
            AddChild(_walkWindow);

            _walkPanel = new WalkModePanel();
            _walkWindow.AddChild(_walkPanel);
        }

        // Inject dependencies
        _walkPanel!.Map = _map;
        _walkPanel.Grhs = _grhs;
        _walkPanel.Textures = _textures;
        _walkPanel.Bodies = _walkBodies;
        _walkPanel.Heads = _walkHeads;
        _walkPanel.ObjGrhs = _objGrhs;
        _walkPanel.NpcBodies = _npcBodies;
        _walkPanel.NpcHeads = _npcHeads;
        _walkPanel.NpcBodyGrhs = _npcBodyGrhs;
        _walkPanel.NpcHeadOfsX = _npcHeadOfsX;
        _walkPanel.NpcHeadOfsY = _npcHeadOfsY;
        _walkPanel.HeadGrhs = _headGrhs;
        _walkPanel.DoorData = _doorData;
        _walkPanel.MapDir = _state.MapDir;

        // Start at current editor camera center tile
        int startX = Math.Clamp(_state.HoverX > 0 ? _state.HoverX : 50, 1, _map.Width);
        int startY = Math.Clamp(_state.HoverY > 0 ? _state.HoverY : 50, 1, _map.Height);
        _walkPanel.CharX = startX;
        _walkPanel.CharY = startY;

        // Default body/head (common male)
        if (_walkPanel.BodyIndex <= 0) _walkPanel.BodyIndex = 1;
        if (_walkPanel.HeadIndex <= 0) _walkPanel.HeadIndex = 1;

        _walkWindow.Visible = true;
        _walkWindow.Position = new Vector2I(
            (int)(GetViewportRect().Size.X / 2 - 280),
            (int)(GetViewportRect().Size.Y / 2 - 220));

        // Give focus to walk panel
        _walkPanel.GrabFocus();
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
        if (_map == null || _state.Clipboard == null) return;
        if (_state.Pending.Active) return; // Already in pending mode

        // Paste origin: selection top-left if available, otherwise hover cursor
        int originX, originY;
        if (_state.HasSelection)
        {
            originX = _state.SelX1;
            originY = _state.SelY1;
        }
        else if (_state.HoverValid)
        {
            originX = _state.HoverX;
            originY = _state.HoverY;
        }
        else
        {
            originX = 1;
            originY = 1;
        }

        // Copy clipboard into a 0-based tile buffer for pending placement
        var buf = new MapTile[_state.ClipWidth, _state.ClipHeight];
        for (int y = 0; y < _state.ClipHeight; y++)
            for (int x = 0; x < _state.ClipWidth; x++)
                buf[x, y] = _state.Clipboard[x + 1, y + 1];

        _state.Pending.Begin(buf, _state.ClipWidth, _state.ClipHeight, originX, originY);
        SetStatus($"Pegando {_state.ClipWidth}x{_state.ClipHeight} — mueve y ✓ para aceptar, ✗ para cancelar");
        _viewport?.QueueRedraw();
    }

    /// <summary>
    /// Commit the pending placement to the map (called from viewport accept button).
    /// </summary>
    public void CommitPendingPlacement()
    {
        if (_map == null || !_state.Pending.Active || _state.Pending.Tiles == null) return;

        var p = _state.Pending;

        // For move operations, restore map from snapshot first, then apply
        if (p.IsMove && p.MoveSnapshot != null)
        {
            _undo.BeginBatch("Move");
            // Record all changes: snapshot → cleared source + placed destination
            Array.Copy(p.MoveSnapshot, _map.Tiles, _map.Tiles.Length);

            // Clear source area
            for (int y = 0; y < p.Height; y++)
                for (int x = 0; x < p.Width; x++)
                {
                    int sx = p.SourceX + x, sy = p.SourceY + y;
                    if (_map.InBounds(sx, sy))
                    {
                        var before = _map.Tiles[sx, sy];
                        _map.Tiles[sx, sy] = new MapTile { Layer1 = 1 };
                        _undo.RecordTileChange(sx, sy, before, _map.Tiles[sx, sy]);
                    }
                }

            // Place at destination
            for (int y = 0; y < p.Height; y++)
                for (int x = 0; x < p.Width; x++)
                {
                    int dx = p.OriginX + x, dy = p.OriginY + y;
                    if (_map.InBounds(dx, dy))
                    {
                        var before = _map.Tiles[dx, dy];
                        _map.Tiles[dx, dy] = p.Tiles[x, y];
                        _undo.RecordTileChange(dx, dy, before, _map.Tiles[dx, dy]);
                    }
                }
            _undo.EndBatch();

            // Update selection to new position
            _state.SetSelection(p.OriginX, p.OriginY,
                p.OriginX + p.Width - 1, p.OriginY + p.Height - 1);

            _particles?.BuildStreamsFromMap(_map);
        }
        else
        {
            // Normal paste
            _undo.BeginBatch("Paste");
            for (int y = 0; y < p.Height; y++)
                for (int x = 0; x < p.Width; x++)
                {
                    int dx = p.OriginX + x, dy = p.OriginY + y;
                    if (!_map.InBounds(dx, dy)) continue;
                    var before = _map.Tiles[dx, dy];
                    _map.Tiles[dx, dy] = p.Tiles[x, y];
                    _undo.RecordTileChange(dx, dy, before, _map.Tiles[dx, dy]);
                }
            _undo.EndBatch();
        }

        _state.MarkDirty();
        SetStatus($"Aplicado {p.Width}x{p.Height} tiles en ({p.OriginX},{p.OriginY})");
        p.Cancel();
        _viewport?.QueueRedraw();
    }

    /// <summary>
    /// Cancel the pending placement (restore map if move).
    /// </summary>
    public void CancelPendingPlacement()
    {
        if (!_state.Pending.Active) return;

        var p = _state.Pending;
        if (p.IsMove && p.MoveSnapshot != null && _map != null)
        {
            // Restore original map state
            Array.Copy(p.MoveSnapshot, _map.Tiles, _map.Tiles.Length);
        }
        p.Cancel();
        SetStatus("Cancelado");
        _viewport?.QueueRedraw();
    }


    #endregion

    #region Map Navigation Bar

    // Map nav bar removed — single map mode. UpdateNavBar kept as no-op for callers.
    private void UpdateNavBar() { }

    #endregion

    #region Input

    private static readonly string[] ToolNames = {
        "Mano", "Pintar", "Borrar", "Seleccionar", "Mover", "Agarrar",
        "Rellenar", "Cuentagotas", "Bloquear", "Luz", "Salida", "NPC", "Objeto", "Trigger"
    };

    public override void _UnhandledKeyInput(InputEvent @event)
    {
        if (@event is not InputEventKey key || !key.Pressed) return;
        if (_preloadPhase > 0) return; // block keyboard during preload

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
                case Key.C: _state.CopySelection(_map!); SetStatus($"Copiado {_state.ClipWidth}x{_state.ClipHeight} tiles"); break;
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
                case Key.F:
                    if (_state.HoverValid && _viewport != null)
                    {
                        if (_state.SelectedTexture != null)
                            _viewport.StampMosaicPattern(_state.SelectedTexture, _state.HoverX, _state.HoverY);
                        else
                            _viewport.FloodFill(_state.HoverX, _state.HoverY);
                    }
                    break;
                case Key.I: newTool = EditorTool.Eyedrop; break;
                case Key.B: newTool = EditorTool.Block; break;
                case Key.G: _state.ShowGrid = !_state.ShowGrid; _viewport?.QueueRedraw(); break;
                case Key.T: ToggleTileProperties(); break;
                case Key.F5: OpenWalkMode(); break;
                case Key.Key1: _state.ActiveLayer = 1; SyncLayerTabs(); _viewport?.QueueRedraw(); break;
                case Key.Key2: _state.ActiveLayer = 2; SyncLayerTabs(); _viewport?.QueueRedraw(); break;
                case Key.Key3: _state.ActiveLayer = 3; SyncLayerTabs(); _viewport?.QueueRedraw(); break;
                case Key.Key4: _state.ActiveLayer = 4; SyncLayerTabs(); _viewport?.QueueRedraw(); break;
                case Key.Delete:
                    DeleteSelection();
                    break;
                case Key.Enter:
                case Key.KpEnter:
                    if (_state.Pending.Active)
                    {
                        CommitPendingPlacement();
                        _viewport?.QueueRedraw();
                    }
                    break;
                case Key.Escape:
                    if (_state.Pending.Active)
                        CancelPendingPlacement();
                    else
                    {
                        _state.ClearSelection();
                        _state.Pick.Clear();
                    }
                    _viewport?.QueueRedraw();
                    break;
            }
        }

        if (newTool.HasValue)
        {
            SetActiveTool(newTool.Value);
        }
    }

    #endregion

    #region Helpers

    private void SetActiveTool(EditorTool tool)
    {
        _state.ActiveTool = tool;
        _state.Pick.Clear();
        // Clear selection when switching away from Select/Move
        if (tool != EditorTool.Select && tool != EditorTool.Move)
            _state.ClearSelection();
        SyncToolBar();
    }

    private void SyncLayerTabs()
    {
        for (int i = 0; i < 4; i++)
            _layerTabButtons[i].ButtonPressed = _state.ActiveLayer == (i + 1);
    }

    // Maps toolbar button index to EditorTool
    private static readonly EditorTool[] ToolBarOrder = {
        EditorTool.Hand, EditorTool.Paint, EditorTool.Erase,
        EditorTool.Select, EditorTool.Move, EditorTool.Fill,
        EditorTool.Pick, EditorTool.Eyedrop, EditorTool.Block,
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
        if (tile.HasNpc)
        {
            string npcName = _npcDb?.Get(tile.NpcIndex)?.Name ?? "";
            lines.AppendLine(npcName.Length > 0
                ? $"NPC: #{tile.NpcIndex} {npcName}"
                : $"NPC: #{tile.NpcIndex}");
        }
        if (tile.HasObject) lines.AppendLine($"Obj: {tile.ObjIndex} x{tile.ObjAmount}");
        if (tile.HasLight) lines.AppendLine($"Luz: R{tile.LightRange} ({tile.LightR},{tile.LightG},{tile.LightB})");
        if (tile.ParticleGroup > 0) lines.AppendLine($"Particula: {tile.ParticleGroup}");
        _tileInfoLabel.Text = lines.ToString().TrimEnd();
    }

    public override void _Process(double delta)
    {
        DoLayout();

        // Tick texture preload (N images per frame, non-blocking)
        TickTexturePreload();

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
        _coordLabel!.Text = _state.HoverValid ? $"({_state.HoverX,3}, {_state.HoverY,3})" : "";
        int toolIdx = (int)_state.ActiveTool;
        _toolLabel!.Text = toolIdx < ToolNames.Length ? ToolNames[toolIdx] : "?";
        int layer = _state.ActiveLayer;
        _layerLabel!.Text = $"L{layer}";
        if (layer >= 1 && layer <= 4)
        {
            var layerColor = EditorTheme.LAYER_COLORS[layer];
            _layerLabel.AddThemeColorOverride("font_color", Colors.White);
            // Update the pill background to match the layer color
            var pillParent = _layerLabel.GetParent();
            if (pillParent is PanelContainer pc)
            {
                var darkLayer = layerColor * 0.4f;
                darkLayer.A = 1.0f;
                pc.AddThemeStyleboxOverride("panel", EditorTheme.FlatBox(darkLayer, 8, 6, 1, layerColor, 1));
            }
        }

        // Update tile info
        UpdateTileInfo();

        _palette?.SyncLayerUI();
        SyncToolBar();
        SyncLayerTabs();
        UpdateRightSidebar();
        _viewport?.QueueRedraw();
    }

    private TextureRef? _lastRightSidebarTexRef;
    private int _lastRightSidebarEyedrop;

    private void UpdateRightSidebar()
    {
        if (_rightPreview == null || _rightNameLabel == null || _rightInfoLabel == null) return;

        var texRef = _state.SelectedTexture;
        int eyedrop = _state.EyedropGrh;

        // Avoid rebuilding every frame — only when selection changes
        if (texRef == _lastRightSidebarTexRef && eyedrop == _lastRightSidebarEyedrop) return;
        _lastRightSidebarTexRef = texRef;
        _lastRightSidebarEyedrop = eyedrop;

        if (texRef != null)
        {
            _rightNameLabel.Text = texRef.Name;
            _rightInfoLabel.Text = $"GRH: {texRef.GrhIndex}\nLayer: {Math.Max(texRef.Layer, 1)}\nSize: {Math.Max(texRef.TileWidth, 1)}x{Math.Max(texRef.TileHeight, 1)}";
            _rightPreview.Texture = _palette?.GetPreviewTexture(texRef);
        }
        else if (eyedrop > 0)
        {
            _rightNameLabel.Text = "Eyedrop";
            _rightInfoLabel.Text = $"GRH: {eyedrop}\nLayer: {_state.ActiveLayer}";
            _rightPreview.Texture = GenerateGrhPreview(eyedrop);
        }
        else
        {
            _rightNameLabel.Text = "(none)";
            _rightInfoLabel.Text = "";
            _rightPreview.Texture = null;
        }
    }

    private Texture2D? GenerateGrhPreview(int grhIndex)
    {
        if (_grhs == null || _textures == null || grhIndex <= 0 || grhIndex >= _grhs.Length) return null;
        var grh = _grhs[grhIndex];
        if (grh.NumFrames <= 0) return null;
        if (grh.NumFrames > 1 && grh.Frames != null && grh.Frames.Length > 0)
        {
            int fIdx = grh.Frames[0];
            if (fIdx > 0 && fIdx < _grhs.Length) grh = _grhs[fIdx];
        }
        if (grh.FileNum <= 0) return null;
        var srcTex = _textures.GetTexture(grh.FileNum);
        if (srcTex == null) return null;
        var atlas = new AtlasTexture();
        atlas.Atlas = srcTex;
        atlas.Region = new Rect2(grh.SX, grh.SY,
            Math.Min(grh.PixelWidth, srcTex.GetWidth() - grh.SX),
            Math.Min(grh.PixelHeight, srcTex.GetHeight() - grh.SY));
        return atlas;
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
