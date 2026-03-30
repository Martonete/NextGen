#nullable enable
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
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
    private ObjectDatabase? _objDb;
    private string _dataPath = "";
    private string _clientMapDir = "";  // client/Data/Maps/
    private string _serverMapDir = "";  // server/maps/
    private string _serverDatDir = "";  // server/dat/

    // Editor
    private readonly EditorState _state = new();
    private readonly UndoManager _undo = new();

    // UI components
    private MenuBar? _menuBar;
    private PopupMenu? _mapsMenu;
    private TabContainer? _sidebarTabs;
    private TilePalette? _palette;
    private NpcPalette? _npcPalette;
    private ObjectPalette? _objPalette;
    private ZonePanel? _zonePanel;
    private MapZoneData? _mapZones;
    private MapViewport? _viewport;
    private Window? _propsWindow;
    private TilePropertiesPanel? _propsPanel;
    private FileDialog? _openDialog;
    private FileDialog? _saveDialog;
    private FileDialog? _dataPathDialog;
    private FileDialog? _serverPathDialog;
    private Window? _insertFormatWindow;
    private OptionButton? _insertFormatSelect;
    private FileDialog? _insertFileDialog;
    private LegacyMapFormat _insertFormat;
    private PopupMenu? _viewMenu;

    // Export dialogs
    private ConfirmationDialog? _exportConfirmDialog;
    private FileDialog? _exportFolderDialog;
    private string _exportClientDataPath = "";

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

    // Right sidebar: selection area section
    private HSeparator? _rightSelSeparator;
    private VBoxContainer? _rightSelectionSection;
    private Label? _rightSelectionLabel;
    private Button? _rightFillButton;
    private Button? _rightMoveAreaButton;
    private Button? _rightDeselectButton;
    private Button? _rightInsertApplyButton;
    private Button? _rightInsertTrimButton;
    private TextureRect? _rightSelPreview;

    // Trim borders dialog
    private Window? _trimBordersWindow;
    private ZoneEditPopup? _pendingZonePopup;
    private SpinBox? _trimBordersSpin;

    // Right sidebar: light tool section
    private HSeparator? _rightLightSeparator;
    private VBoxContainer? _rightLightSection;
    private HSlider? _lightSliderR, _lightSliderG, _lightSliderB, _lightSliderRange;
    private Label? _lightLabelR, _lightLabelG, _lightLabelB, _lightLabelRange;
    private ColorRect? _lightPreviewRect;
    private Button? _lightEraseButton;

    // Right sidebar: selected tile section (Hand tool)
    private HSeparator? _rightTileSeparator;
    private VBoxContainer? _rightTileSection;
    private Label? _rightTileInfoLabel;

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
    private int _preloadPhase; // 0=idle, 1=loading+previews, 2=done (fade-out)
    private IEnumerator<int>? _previewPreloadIter;
    private float _loadingFadeAlpha = 1f;
    private bool _loadingFadingOut;

    private const float PaletteWidth = 280;
    private const float StatusHeight = 28;
    private const float ToolBarHeight = 62;
    private const float TileInfoHeight = 110;
    private const float SidebarBorderWidth = 1;
    private const float RightSidebarWidth = 250;

    private bool _readyCalled;

    public override void _Ready()
    {
        // Guard: _Ready must run exactly once (Godot C# can re-fire on assembly hot-reload)
        if (_readyCalled) return;
        _readyCalled = true;

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
        fileMenu.AddItem("Insertar Mapa... (Ctrl+I)", 8);
        fileMenu.AddSeparator();
        fileMenu.AddItem("Seleccionar Carpeta de Recursos...", 6);
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

        _mapsMenu = new PopupMenu { Name = "Mapas" };
        _mapsMenu.IdPressed += OnMapsMenuId;
        _menuBar.AddChild(_mapsMenu);

        AddChild(_menuBar);

        // --- Tool bar (professional grouped layout) ---
        _toolBar = new HBoxContainer();
        _toolBar.AddThemeConstantOverride("separation", 0);

        // -- Group 1: File operations --
        var fileGroup = EditorTheme.ToolBarGroup();
        var fileGroupH = new HBoxContainer();
        fileGroupH.AddThemeConstantOverride("separation", 6);
        fileGroupH.AddChild(EditorTheme.ActionButtonCompact("\ud83d\udcc2", "Abrir Mapa (Ctrl+O)", () => RequestOpenMap(), "Abrir"));
        fileGroupH.AddChild(EditorTheme.ActionButtonCompact("\ud83d\udcbe", "Guardar (Ctrl+S)", () => OnSaveMap(), "Guardar"));
        fileGroup.AddChild(fileGroupH);
        _toolBar.AddChild(fileGroup);

        _toolBar.AddChild(EditorTheme.ToolBarGroupSeparator());

        // -- Group 2: Undo / Redo --
        var undoGroup = EditorTheme.ToolBarGroup();
        var undoGroupH = new HBoxContainer();
        undoGroupH.AddThemeConstantOverride("separation", 6);
        undoGroupH.AddChild(EditorTheme.ActionButtonCompact("\u21a9", "Deshacer (Ctrl+Z)", () => { _undo.Undo(_map!); _viewport?.QueueRedraw(); }, "Deshacer"));
        undoGroupH.AddChild(EditorTheme.ActionButtonCompact("\u21aa", "Rehacer (Ctrl+Y)", () => { _undo.Redo(_map!); _viewport?.QueueRedraw(); }, "Rehacer"));
        undoGroup.AddChild(undoGroupH);
        _toolBar.AddChild(undoGroup);

        _toolBar.AddChild(EditorTheme.ToolBarGroupSeparator());

        // -- Group 3: Drawing tools --
        var drawGroup = EditorTheme.ToolBarGroup();
        var drawGroupH = new HBoxContainer();
        drawGroupH.AddThemeConstantOverride("separation", 6);

        var toolDefs = new (EditorTool tool, string icon, string label, string shortcut)[]
        {
            (EditorTool.Hand,    "\u270b", "Mano",        "H"),
            (EditorTool.Paint,   "\u270f", "Pintar",      "P"),
            (EditorTool.Erase,   "\u232b", "Borrar",      "E"),
            (EditorTool.Select,  "\u25a1", "Seleccionar", "R"),
            (EditorTool.Pick,    "\u21c6", "Agarrar",     "V"),
            (EditorTool.Eyedrop, "\u25ce", "Cuentagotas", "I"),
            (EditorTool.Block,   "\u2298", "Bloquear",    "B"),
        };
        _toolBarButtons = new Button[toolDefs.Length];
        for (int i = 0; i < toolDefs.Length; i++)
        {
            var (tool, icon, label, shortcut) = toolDefs[i];
            var btn = EditorTheme.ToolToggleCompact(icon, $"{label} ({shortcut})", label);
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
        propGroupH.AddThemeConstantOverride("separation", 6);

        var propToolDefs = new (EditorTool tool, string icon, string label)[]
        {
            (EditorTool.Light,   "\u2600", "Luz"),
            (EditorTool.Exit,    "\u2197", "Salida"),
            (EditorTool.Trigger, "\u26a1", "Trigger"),
        };
        var extButtons = new Button[propToolDefs.Length];
        for (int i = 0; i < propToolDefs.Length; i++)
        {
            var (tool, icon, label) = propToolDefs[i];
            var btn = EditorTheme.ToolToggleCompact(icon, label, label);
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
        layerGroupH.AddThemeConstantOverride("separation", 6);
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

        // --- Left sidebar: TabContainer with Tiles + NPCs + Zonas ---
        _sidebarTabs = new TabContainer();
        _sidebarTabs.TabAlignment = TabBar.AlignmentMode.Center;
        _sidebarTabs.AddThemeFontSizeOverride("font_size", EditorTheme.FONT_SM);

        // Tab bar strip: fixed background so it always reads as a header band
        var tabBarBg = EditorTheme.FlatBox(EditorTheme.BG_HEADER, 0, 0, 0,
            EditorTheme.BORDER_SUBTLE, 1);
        _sidebarTabs.AddThemeStyleboxOverride("tabbar_background", tabBarBg);

        // Individual tab pills — generous padding (14h, 6v) so they breathe
        var tabNormal   = EditorTheme.FlatBox(new Color(0, 0, 0, 0), 6, 14, 6);
        var tabHover    = EditorTheme.FlatBox(EditorTheme.BG_TOOL_HOVER, 6, 14, 6);
        var tabSelected = EditorTheme.FlatBox(EditorTheme.BG_TOOL_ACTIVE, 6, 14, 6,
            EditorTheme.ACCENT, 1);
        _sidebarTabs.AddThemeStyleboxOverride("tab_unselected", tabNormal);
        _sidebarTabs.AddThemeStyleboxOverride("tab_hovered",    tabHover);
        _sidebarTabs.AddThemeStyleboxOverride("tab_selected",   tabSelected);
        _sidebarTabs.AddThemeStyleboxOverride("tab_focus",      new StyleBoxEmpty());
        _sidebarTabs.AddThemeColorOverride("font_unselected_color", EditorTheme.TEXT_SECONDARY);
        _sidebarTabs.AddThemeColorOverride("font_hovered_color",    EditorTheme.TEXT_PRIMARY);
        _sidebarTabs.AddThemeColorOverride("font_selected_color",   Colors.White);

        // Content panel: transparent so sidebar children draw their own bg
        _sidebarTabs.AddThemeStyleboxOverride("panel", new StyleBoxEmpty());

        _sidebarTabs.ClipContents = true;
        _sidebarTabs.MouseFilter = MouseFilterEnum.Stop;
        AddChild(_sidebarTabs);

        _palette = new TilePalette { Name = "Tiles", State = _state };
        _palette.LayerChanged += (layer) => SyncLayerTabs();
        _sidebarTabs.AddChild(_palette);

        _npcPalette = new NpcPalette { Name = "NPCs", State = _state };
        _npcPalette.NpcSelected += OnNpcPaletteSelected;
        _sidebarTabs.AddChild(_npcPalette);

        _objPalette = new ObjectPalette { Name = "Objetos", State = _state };
        _objPalette.ObjectSelected += OnObjectPaletteSelected;
        _sidebarTabs.AddChild(_objPalette);

        _zonePanel = new ZonePanel { Name = "Zonas", State = _state, ZoneData = _mapZones };
        _zonePanel.OnZonesChanged += () => { _state.MarkDirty(); _viewport?.QueueRedraw(); };
        _zonePanel.OnZoneSelected += (zone) => { _viewport?.CenterOnTile((zone.X1 + zone.X2) / 2, (zone.Y1 + zone.Y2) / 2); };
        _zonePanel.OnEditZone += (zone) => ShowZoneEditPopup(zone);
        _sidebarTabs.AddChild(_zonePanel);

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
            ZoneData = _mapZones,
            ClipContents = true,
        };
        _viewport.OnPendingAccept += CommitPendingPlacement;
        _viewport.OnPendingCancel += CancelPendingPlacement;
        _viewport.OnSelectionCompleted += OnSelectionCompleted;
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

        // Separator between texture info and selection area
        _rightSelSeparator = new HSeparator();
        _rightSelSeparator.AddThemeConstantOverride("separation", 12);
        _rightSelSeparator.Visible = false;
        rightVBox.AddChild(_rightSelSeparator);

        // Selection area section (shown when HasSelection)
        _rightSelectionSection = new VBoxContainer();
        _rightSelectionSection.AddThemeConstantOverride("separation", 6);
        _rightSelectionSection.Visible = false;

        _rightSelectionSection.AddChild(EditorTheme.SectionLabel("ÁREA SELECCIONADA"));

        // Thumbnail of selected map area
        _rightSelPreview = new TextureRect
        {
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            CustomMinimumSize = new Vector2(0, 72),
        };
        _rightSelectionSection.AddChild(_rightSelPreview);

        _rightSelectionLabel = EditorTheme.MakeLabel("", EditorTheme.TEXT_SECONDARY, EditorTheme.FONT_SM);
        _rightSelectionLabel.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _rightSelectionSection.AddChild(_rightSelectionLabel);

        _rightFillButton = EditorTheme.PrimaryButton("Rellenar con textura");
        _rightFillButton.Disabled = true;
        _rightFillButton.Pressed += () => _viewport?.FillSelection();
        _rightSelectionSection.AddChild(_rightFillButton);

        var blockAllBtn = EditorTheme.MakeButton("Bloquear todos los tiles");
        blockAllBtn.Pressed += () => BlockSelection(true);
        _rightSelectionSection.AddChild(blockAllBtn);

        var unblockAllBtn = EditorTheme.MakeButton("Desbloquear todos los tiles");
        unblockAllBtn.Pressed += () => BlockSelection(false);
        _rightSelectionSection.AddChild(unblockAllBtn);

        _rightMoveAreaButton = EditorTheme.MakeButton("Mover área");
        _rightMoveAreaButton.ToggleMode = true;
        _rightMoveAreaButton.Pressed += () =>
        {
            if (_state.ActiveTool == EditorTool.Move)
                SetActiveTool(EditorTool.Select);
            else
                SetActiveTool(EditorTool.Move);
        };
        _rightSelectionSection.AddChild(_rightMoveAreaButton);

        _rightDeselectButton = EditorTheme.MakeButton("Cancelar");
        _rightDeselectButton.Pressed += OnDeselectSelection;
        _rightSelectionSection.AddChild(_rightDeselectButton);

        // Insert Map specific buttons (hidden by default)
        _rightInsertTrimButton = EditorTheme.MakeButton("Cortar bordes...");
        _rightInsertTrimButton.Visible = false;
        _rightInsertTrimButton.Pressed += ShowTrimBordersDialog;
        _rightSelectionSection.AddChild(_rightInsertTrimButton);

        _rightInsertApplyButton = EditorTheme.PrimaryButton("Aplicar mapa insertado");
        _rightInsertApplyButton.Visible = false;
        _rightInsertApplyButton.Pressed += () => { _state.InsertedMapSelection = false; _viewport?.QueueRedraw(); SetStatus("Mapa insertado aplicado"); };
        _rightSelectionSection.AddChild(_rightInsertApplyButton);

        rightVBox.AddChild(_rightSelectionSection);

        // ── Light tool section ──
        _rightLightSeparator = new HSeparator();
        _rightLightSeparator.AddThemeConstantOverride("separation", 12);
        _rightLightSeparator.Visible = false;
        rightVBox.AddChild(_rightLightSeparator);

        _rightLightSection = new VBoxContainer();
        _rightLightSection.AddThemeConstantOverride("separation", 4);
        _rightLightSection.Visible = false;
        _rightLightSection.AddChild(EditorTheme.SectionLabel("LUZ"));

        // Color preview
        _lightPreviewRect = new ColorRect
        {
            CustomMinimumSize = new Vector2(0, 24),
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            Color = new Color(_state.LightR / 255f, _state.LightG / 255f, _state.LightB / 255f),
        };
        _rightLightSection.AddChild(_lightPreviewRect);

        // R slider
        _lightLabelR = EditorTheme.MakeLabel($"R: {_state.LightR}", EditorTheme.TEXT_SECONDARY, EditorTheme.FONT_SM);
        _rightLightSection.AddChild(_lightLabelR);
        _lightSliderR = CreateLightSlider(0, 255, _state.LightR, v => { _state.LightR = (int)v; _lightLabelR.Text = $"R: {(int)v}"; UpdateLightPreview(); });
        _rightLightSection.AddChild(_lightSliderR);

        // G slider
        _lightLabelG = EditorTheme.MakeLabel($"G: {_state.LightG}", EditorTheme.TEXT_SECONDARY, EditorTheme.FONT_SM);
        _rightLightSection.AddChild(_lightLabelG);
        _lightSliderG = CreateLightSlider(0, 255, _state.LightG, v => { _state.LightG = (int)v; _lightLabelG.Text = $"G: {(int)v}"; UpdateLightPreview(); });
        _rightLightSection.AddChild(_lightSliderG);

        // B slider
        _lightLabelB = EditorTheme.MakeLabel($"B: {_state.LightB}", EditorTheme.TEXT_SECONDARY, EditorTheme.FONT_SM);
        _rightLightSection.AddChild(_lightLabelB);
        _lightSliderB = CreateLightSlider(0, 255, _state.LightB, v => { _state.LightB = (int)v; _lightLabelB.Text = $"B: {(int)v}"; UpdateLightPreview(); });
        _rightLightSection.AddChild(_lightSliderB);

        // Range slider
        _lightLabelRange = EditorTheme.MakeLabel($"Rango: {_state.LightRange}", EditorTheme.TEXT_SECONDARY, EditorTheme.FONT_SM);
        _rightLightSection.AddChild(_lightLabelRange);
        _lightSliderRange = CreateLightSlider(1, 20, _state.LightRange, v => { _state.LightRange = (int)v; _lightLabelRange.Text = $"Rango: {(int)v}"; });
        _rightLightSection.AddChild(_lightSliderRange);

        // Erase light button
        _lightEraseButton = EditorTheme.MakeButton("Borrar luz (click derecho)");
        _lightEraseButton.Disabled = true; // informational
        _rightLightSection.AddChild(_lightEraseButton);

        rightVBox.AddChild(_rightLightSection);

        // ── Tile info section (Hand tool click) ──
        _rightTileSeparator = new HSeparator();
        _rightTileSeparator.AddThemeConstantOverride("separation", 12);
        _rightTileSeparator.Visible = false;
        rightVBox.AddChild(_rightTileSeparator);

        _rightTileSection = new VBoxContainer();
        _rightTileSection.AddThemeConstantOverride("separation", 4);
        _rightTileSection.Visible = false;
        _rightTileSection.AddChild(EditorTheme.SectionLabel("TILE SELECCIONADO"));

        _rightTileInfoLabel = EditorTheme.MakeLabel("", EditorTheme.TEXT_SECONDARY, EditorTheme.FONT_SM);
        _rightTileInfoLabel.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _rightTileSection.AddChild(_rightTileInfoLabel);

        rightVBox.AddChild(_rightTileSection);

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
        _saveDialog.AddFilter("*.aomap", "Mapa AO");
        _saveDialog.FileSelected += OnSaveFileSelected;
        AddChild(_saveDialog);

        _dataPathDialog = new FileDialog
        {
            FileMode = FileDialog.FileModeEnum.OpenDir,
            Access = FileDialog.AccessEnum.Filesystem,
            Title = "Seleccionar carpeta de recursos",
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

        // Insert Map: compact centered format selector window
        _insertFormatWindow = new Window();
        _insertFormatWindow.Title = "Insertar Mapa";
        _insertFormatWindow.Size = new Vector2I(320, 180);
        _insertFormatWindow.Exclusive = true;
        _insertFormatWindow.Unresizable = true;
        _insertFormatWindow.Visible = false;
        _insertFormatWindow.WrapControls = true;
        _insertFormatWindow.Transient = true;
        _insertFormatWindow.CloseRequested += () => _insertFormatWindow.Hide();

        var insertMargin = new MarginContainer();
        insertMargin.AddThemeConstantOverride("margin_left", 20);
        insertMargin.AddThemeConstantOverride("margin_right", 20);
        insertMargin.AddThemeConstantOverride("margin_top", 16);
        insertMargin.AddThemeConstantOverride("margin_bottom", 16);
        insertMargin.SetAnchorsPreset(Control.LayoutPreset.FullRect);

        var insertVbox = new VBoxContainer();
        insertVbox.AddThemeConstantOverride("separation", 12);

        var insertLabel = new Label { Text = "Formato del mapa:" };
        insertLabel.AddThemeFontSizeOverride("font_size", EditorTheme.FONT_MD);
        insertVbox.AddChild(insertLabel);

        _insertFormatSelect = new OptionButton();
        _insertFormatSelect.AddThemeFontSizeOverride("font_size", EditorTheme.FONT_MD);
        _insertFormatSelect.AddItem("Auto-detectar", (int)LegacyMapFormat.AutoDetect);
        _insertFormatSelect.AddItem("0.99z (fijo)", (int)LegacyMapFormat.Fixed_099z);
        _insertFormatSelect.AddItem("11.5 - 13.3 (variable)", (int)LegacyMapFormat.Variable_Int16);
        _insertFormatSelect.Selected = 0;
        insertVbox.AddChild(_insertFormatSelect);

        var insertBtn = new Button { Text = "Seleccionar Mapa..." };
        insertBtn.AddThemeFontSizeOverride("font_size", EditorTheme.FONT_MD);
        insertBtn.CustomMinimumSize = new Vector2(0, 36);
        insertBtn.Pressed += OnInsertSelectMapPressed;
        insertVbox.AddChild(insertBtn);

        insertMargin.AddChild(insertVbox);
        _insertFormatWindow.AddChild(insertMargin);
        AddChild(_insertFormatWindow);

        // Insert Map: file selector dialog (only .map files)
        _insertFileDialog = new FileDialog
        {
            FileMode = FileDialog.FileModeEnum.OpenFile,
            Access = FileDialog.AccessEnum.Filesystem,
            Title = "Seleccionar archivo .map",
            Size = new Vector2I(600, 400),
        };
        _insertFileDialog.AddFilter("*.map", "Mapa AO Legacy (.map)");
        _insertFileDialog.FileSelected += OnInsertMapFileSelected;
        AddChild(_insertFileDialog);

        BuildMapPropsDialog();
        BuildUnsavedDialog();
        BuildExportDialogs();
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

    private void BuildExportDialogs()
    {
        _exportConfirmDialog = new ConfirmationDialog
        {
            Title = "Exportar Mapa",
            DialogText = "¿Exportar maps.aopak para el cliente?",
            Size = new Vector2I(380, 130),
            OkButtonText = "Sí",
        };
        _exportConfirmDialog.CancelButtonText = "No";
        _exportConfirmDialog.Confirmed += OnExportConfirmed;
        AddChild(_exportConfirmDialog);

        _exportFolderDialog = new FileDialog
        {
            FileMode = FileDialog.FileModeEnum.OpenDir,
            Access = FileDialog.AccessEnum.Filesystem,
            Title = "Seleccionar carpeta Data del cliente",
            Size = new Vector2I(600, 400),
        };
        _exportFolderDialog.DirSelected += OnExportFolderSelected;
        AddChild(_exportFolderDialog);
    }

    private void OnExportConfirmed()
    {
        if (_exportClientDataPath.Length > 0 && Directory.Exists(_exportClientDataPath))
        {
            ExportMapsAopak(_exportClientDataPath);
        }
        else
        {
            _exportFolderDialog!.Popup();
        }
    }

    private void OnExportFolderSelected(string path)
    {
        _exportClientDataPath = path;
        SaveConfig();
        ExportMapsAopak(path);
    }

    private void ExportMapsAopak(string clientDataPath)
    {
        if (string.IsNullOrEmpty(_dataPath))
        {
            SetStatus("ERROR: ruta de datos no configurada");
            return;
        }

        string cliProject = Path.GetFullPath(Path.Combine(_dataPath, "..", "compressor", "CLI", "AoPakCli.csproj"));
        string mapsDir = Path.Combine(_dataPath, "Maps");

        GD.Print($"[Editor] ExportMapsAopak: cli={cliProject} maps={mapsDir} outputDir={clientDataPath}");

        SetStatus("Exportando maps.aopak...");

        var psi = new ProcessStartInfo
        {
            FileName = "dotnet",
            Arguments = $"run --project \"{cliProject}\" -- pack \"{mapsDir}\" --outputDir \"{clientDataPath}\"",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        Task.Run(() =>
        {
            try
            {
                using var proc = Process.Start(psi);
                if (proc == null)
                {
                    CallDeferred(MethodName.SetStatus, "ERROR: no se pudo iniciar el compressor");
                    return;
                }
                // Read both streams concurrently to avoid deadlock
                var stdoutTask = proc.StandardOutput.ReadToEndAsync();
                var stderrTask = proc.StandardError.ReadToEndAsync();
                proc.WaitForExit();
                string stdout = stdoutTask.Result;
                string stderr = stderrTask.Result;
                if (proc.ExitCode == 0)
                {
                    GD.Print($"[Editor] Compressor output: {stdout}");
                    CallDeferred(MethodName.SetStatus, "maps.aopak exportado ✓");
                }
                else
                {
                    string err = stderr.Length > 0 ? stderr : stdout;
                    GD.PrintErr($"[Editor] Compressor error (exit {proc.ExitCode}): {err}");
                    CallDeferred(MethodName.SetStatus, $"ERROR al exportar: {err.Split('\n')[0].Trim()}");
                }
            }
            catch (Exception ex)
            {
                GD.PrintErr($"[Editor] ExportMapsAopak exception: {ex.Message}");
                CallDeferred(MethodName.SetStatus, $"ERROR: {ex.Message}");
            }
        });
    }

    private void BuildLoadingScreen()
    {
        _preloadOverlay = new Panel();
        _preloadOverlay.Visible = true; // visible from first frame — preload shows progress
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

    private const string ConfigFileName = "editor_config.ini";

    private string GetConfigPath()
    {
        // Save config next to project.godot (or exe)
        string resPath = ProjectSettings.GlobalizePath("res://");
        return Path.Combine(resPath, ConfigFileName);
    }

    private void SaveConfig()
    {
        try
        {
            var lines = new List<string>
            {
                $"client_data={_dataPath}",
                $"server_maps={_serverMapDir}",
                $"server_dat={_serverDatDir}",
                $"export_client_data={_exportClientDataPath}"
            };
            File.WriteAllLines(GetConfigPath(), lines);
            GD.Print($"[Editor] Config saved: {GetConfigPath()}");
        }
        catch (Exception ex) { GD.PrintErr($"[Editor] Failed to save config: {ex.Message}"); }
    }

    private string? LoadConfigValue(string key)
    {
        try
        {
            string cfgPath = GetConfigPath();
            if (!File.Exists(cfgPath)) return null;
            foreach (string line in File.ReadAllLines(cfgPath))
            {
                int eq = line.IndexOf('=');
                if (eq > 0 && line[..eq] == key)
                    return line[(eq + 1)..];
            }
        }
        catch { }
        return null;
    }

    private bool _dataLoaded;

    private void TryAutoDetectDataPath()
    {
        if (_dataLoaded) return;

        // 1. Try saved config first
        string? savedPath = LoadConfigValue("client_data");
        if (savedPath != null && Directory.Exists(savedPath) && File.Exists(Path.Combine(savedPath, "INIT", "Graficos.ind")))
        {
            GD.Print($"[Editor] Using saved config: {savedPath}");
            string? savedServerMaps = LoadConfigValue("server_maps");
            if (savedServerMaps != null && Directory.Exists(savedServerMaps))
                _serverMapDir = savedServerMaps;
            string? savedServerDat = LoadConfigValue("server_dat");
            if (savedServerDat != null && Directory.Exists(savedServerDat))
                _serverDatDir = savedServerDat;
            string? savedExportData = LoadConfigValue("export_client_data");
            if (savedExportData != null && Directory.Exists(savedExportData))
                _exportClientDataPath = savedExportData;
            LoadDataPath(savedPath);
            return;
        }

        // 2. Auto-detect from multiple base directories
        var candidates = new List<string>();
        string resPath = ProjectSettings.GlobalizePath("res://");
        string exeDir = OS.GetExecutablePath().GetBaseDir();
        string cwd = Directory.GetCurrentDirectory();

        // From res:// (tools/world-editor/) → primary: tools/resources/data/
        candidates.Add(Path.Combine(resPath, "../../tools/resources/data"));
        // From cwd (repo root)
        candidates.Add(Path.Combine(cwd, "tools/resources/data"));
        // Fallbacks for backwards compat
        candidates.Add(Path.Combine(resPath, "../../client/Data"));
        candidates.Add(Path.Combine(cwd, "client/Data"));
        // From exe
        candidates.Add(Path.Combine(exeDir, "Data"));
        // User data
        candidates.Add(Path.Combine(OS.GetUserDataDir(), "Data"));

        GD.Print($"[Editor] Auto-detect: res={resPath} cwd={cwd} exe={exeDir}");

        foreach (var candidate in candidates)
        {
            try
            {
                string full = Path.GetFullPath(candidate);
                if (Directory.Exists(full) && File.Exists(Path.Combine(full, "INIT", "Graficos.ind")))
                {
                    GD.Print($"[Editor] Auto-detected Data path: {full}");
                    LoadDataPath(full);
                    SaveConfig();
                    return;
                }
            }
            catch { }
        }

        GD.Print("[Editor] Auto-detect failed, showing setup form");
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
        _setupWindow.Size = new Vector2I(500, 340);
        _setupWindow.Exclusive = false;
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
        vbox.AddChild(EditorTheme.MakeLabel("Carpeta de Recursos"));
        var clientRow = new HBoxContainer();
        clientRow.AddThemeConstantOverride("separation", 6);
        _setupClientPath = new LineEdit { PlaceholderText = "Ej: C:/Proyecto/tools/resources/data" };
        _setupClientPath.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        clientRow.AddChild(_setupClientPath);
        var clientBrowse = EditorTheme.MakeButton("...");
        clientBrowse.Pressed += () =>
        {
            var dlg = new FileDialog { FileMode = FileDialog.FileModeEnum.OpenDir, Access = FileDialog.AccessEnum.Filesystem, Title = "Seleccionar carpeta de recursos" };
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
            var dlg = new FileDialog { FileMode = FileDialog.FileModeEnum.OpenDir, Access = FileDialog.AccessEnum.Filesystem, Title = "Seleccionar carpeta server/" };
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

        if (clientPath.Length == 0 || !Directory.Exists(clientPath))
        {
            SetStatus("La carpeta de recursos no existe.");
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

        // Server path
        string serverPath = _setupServerPath?.Text.Trim() ?? "";
        if (serverPath.Length > 0 && Directory.Exists(serverPath))
        {
            string datSub = Path.Combine(serverPath, "dat");
            string mapsSub = Path.Combine(serverPath, "maps");
            if (Directory.Exists(datSub)) _serverDatDir = datSub;
            if (Directory.Exists(mapsSub)) _serverMapDir = mapsSub;
        }

        if (_setupWindow != null) _setupWindow.Hide();
        // Restore loading screen elements
        if (_loadingBar != null) _loadingBar.Visible = true;
        if (_loadingLabel != null) _loadingLabel.Visible = true;
        if (_loadingTitle != null) _loadingTitle.Visible = true;
        _dataLoaded = false; // User explicitly applying new paths — allow reload
        LoadDataPath(dataPath);

        // Persist paths for next launch
        SaveConfig();
    }

    private void LoadDataPath(string dataPath)
    {
        // Prevent duplicate loads — data should load exactly once
        if (_dataLoaded)
        {
            GD.Print($"[Editor] LoadDataPath SKIPPED (already loaded): {dataPath}");
            return;
        }
        GD.Print($"[Editor] LoadDataPath: {dataPath}");
        _dataLoaded = true; // Set early to prevent re-entrant calls
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

        // Graficos.ind must be present as a loose file
        if (!File.Exists(graficosInd))
        {
            GD.PrintErr($"[Editor] Graficos.ind NOT FOUND at: {graficosInd}");
            SetStatus($"ERROR: {graficosInd} no encontrado");
            _dataLoaded = false; // Allow retry on failure
            _preloadPhase = 0;
            if (_preloadOverlay != null) _preloadOverlay.Visible = false;
            return;
        }
        GD.Print($"[Editor] Found Graficos.ind (loose)");

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

        // Find server/ directory (auto-detect relative to dataPath)
        if (_serverDatDir.Length == 0)
        {
            // Try multiple levels up from dataPath to find server/dat
            for (int up = 1; up <= 5; up++)
            {
                string dots = string.Join("/", Enumerable.Repeat("..", up));
                string candidate = Path.GetFullPath(Path.Combine(dataPath, dots, "server"));
                GD.Print($"[Editor] Server probe: {candidate}");
                if (Directory.Exists(Path.Combine(candidate, "dat")))
                {
                    _serverDatDir = Path.Combine(candidate, "dat");
                    GD.Print($"[Editor] Server dat found: {_serverDatDir}");
                    break;
                }
            }
        }

        // Server maps = server/maps/ (sibling of server/dat/)
        if (_serverDatDir.Length > 0)
        {
            string serverMapsDir = Path.Combine(Path.GetDirectoryName(_serverDatDir) ?? "", "maps");
            if (Directory.Exists(serverMapsDir))
            {
                _serverMapDir = serverMapsDir;
                _state.MapDir = serverMapsDir; // Load maps from server
                GD.Print($"[Editor] Server maps: {serverMapsDir}");
            }
        }

        // Load particle definitions
        string particlesIni = Path.Combine(dataPath, "INIT", "Particles.ini");
        _particles = new ParticleEngine();
        if (File.Exists(particlesIni))
            _particles.LoadDefinitions(particlesIni);

        // Load body + head data from loose files
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
            string objDatForDb = Path.Combine(_serverDatDir, "Obj.dat");
            _objDb = ObjectDatabase.Load(objDatForDb);
            GD.Print($"[Editor] Server data: {_serverDatDir}");
        }
        else
        {
            GD.Print("[Editor] Server dat/ not found — NPC names unavailable (expected ../dats/ sibling of resources/data).");
        }

        // Push data to palette
        _palette!.Grhs = _grhs;
        _palette.Textures = _textures;
        _palette.Catalog = _catalog;
        _palette.IndicesPath = System.IO.Path.Combine(_dataPath, "INIT", "indices.ini");
        _palette.Rebuild();

        // Push data to NPC + Object palettes
        SyncNpcPaletteData();
        SyncObjPaletteData();

        // Push data to viewport
        SyncViewportData();

        // Scan available maps and update nav bar
        _state.ScanAvailableMaps(_state.MapDir);
        UpdateNavBar();
        GD.Print($"[Editor] Found {_state.AvailableMaps.Count} maps in {_state.MapDir}");

        // Show path status
        string pathInfo = "";
        if (_clientMapDir.Length > 0) pathInfo += $"Cliente: {_clientMapDir}  ";
        if (_serverMapDir.Length > 0) pathInfo += $"Server: {_serverMapDir}  ";
        if (_serverMapDir.Length == 0)
            pathInfo += "⚠ Server no configurado — Archivo > Configurar Ruta Server";
        if (_clientMapDir.Length == 0)
            pathInfo += "⚠ Sin carpeta de recursos — Archivo > Seleccionar Carpeta de Recursos";
        GD.Print($"[Editor] {pathInfo}");

        // Auto-load map 1 if it exists, otherwise create empty map
        if (_state.AvailableMaps.Contains(1))
            LoadMapByNumber(1);
        else
            CreateNewMap(1);

        // Start preload — Phase 1 generates previews directly (which also loads textures on demand).
        // No separate texture-preload pass needed: GeneratePreview calls Textures.GetTexture()
        // internally, so textures are loaded as each preview is generated — one pass, 0→100%.
        if (_palette != null)
        {
            _preloadPhase = 1;
            _previewPreloadIter = _palette.PreloadAllPreviews();
            if (_preloadOverlay != null) { _preloadOverlay.Visible = true; _preloadOverlay.Modulate = Colors.White; }
            if (_loadingTitle != null) { _loadingTitle.Visible = true; _loadingTitle.Text = "World Editor"; }
            if (_loadingLabel != null) { _loadingLabel.Visible = true; _loadingLabel.Text = "Cargando..."; }
            if (_loadingBar != null) { _loadingBar.Visible = true; _loadingBar.Value = 0; }
            int total = _palette.PreviewPreloadTotal;
            GD.Print($"[Editor] Starting preload: {total} texture refs");
        }
        else
        {
            _preloadPhase = 0;
            if (_preloadOverlay != null) _preloadOverlay.Visible = false;
            SetStatus("Editor listo");
        }
    }

    private void TickTexturePreload()
    {
        // Phase 1: load textures + generate previews in a single time-budgeted pass
        if (_preloadPhase == 1 && _previewPreloadIter != null)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            while (sw.Elapsed.TotalMilliseconds < 12.0)
            {
                if (!_previewPreloadIter.MoveNext())
                {
                    GD.Print("[Editor] Preload complete");
                    _previewPreloadIter = null;
                    _preloadPhase = 2;
                    _loadingFadingOut = true;
                    _loadingFadeAlpha = 1f;
                    if (_loadingBar != null) _loadingBar.Value = 100;
                    if (_loadingLabel != null) _loadingLabel.Text = "Listo!";
                    SetStatus("Editor listo");
                    _palette?.UpdateGridHighlights();
                    return;
                }
            }
            int previewTotal = _palette?.PreviewPreloadTotal ?? 1;
            int previewDone  = _previewPreloadIter.Current;
            double ratio = previewDone / (double)Math.Max(1, previewTotal);
            if (_loadingBar  != null) _loadingBar.Value  = 100.0 * ratio;
            if (_loadingLabel != null) _loadingLabel.Text = $"Cargando... ({previewDone}/{previewTotal})";
        }
        // Phase 2: fade out overlay
        else if (_preloadPhase == 2 && _loadingFadingOut)
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
            case 8: InsertMap(); break;
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
        _map = new MapData(1000, 1000);
        _map.MapNumber = mapNumber;
        _map.Name = $"Mapa {mapNumber}";

        for (int y = 1; y <= 1000; y++)
            for (int x = 1; x <= 1000; x++)
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
        // Load zone data for this map
        _mapZones = MapZoneData.Load(_state.MapDir, mapNumber);
        if (_zonePanel != null) { _zonePanel.ZoneData = _mapZones; _zonePanel.RebuildList(); }
        if (_viewport != null) _viewport.ZoneData = _mapZones;
        UpdateViewport();
        UpdateNavBar();
        int zoneCount = _mapZones?.Zones.Count ?? 0;
        SetStatus($"Mapa {mapNumber} cargado ({_map.Width}x{_map.Height}) — {_map.Name} — {zoneCount} zonas");
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
        if (_map.MapNumber <= 0) _map.MapNumber = _state.CurrentMapNumber > 0 ? _state.CurrentMapNumber : 1;

        // Server: save full set (.aomap + .aoinf + .dat)
        if (_serverMapDir.Length > 0 && Directory.Exists(_serverMapDir))
        {
            MapLoader.Save(_serverMapDir, _map);
            GD.Print($"[Editor] Saved to server: {_serverMapDir}");
        }

        // Client data: save .aomap ONLY (will be packed into maps.aopak)
        if (_clientMapDir.Length > 0 && Directory.Exists(_clientMapDir))
        {
            MapLoader.Save(_clientMapDir, _map, mapOnly: true);
            GD.Print($"[Editor] Saved .aomap to client data: {_clientMapDir}");
        }

        // Save .aozone to server
        if (_mapZones != null && _map.MapNumber > 0 && _serverMapDir.Length > 0 && Directory.Exists(_serverMapDir))
        {
            _mapZones.Save(_serverMapDir, _map.MapNumber);
            GD.Print($"[Editor] Saved {_mapZones.Zones.Count} zones for map {_map.MapNumber}");
        }

        _state.ResetDirty();
        _state.ScanAvailableMaps(_state.MapDir);
        UpdateNavBar();
        SetStatus($"Mapa {_map.MapNumber} guardado (server + data)");
        _exportConfirmDialog?.PopupCentered();
    }

    private void OnSaveAsMap()
    {
        if (!string.IsNullOrEmpty(_state.MapDir))
            _saveDialog!.CurrentDir = _state.MapDir;
        _saveDialog!.CurrentFile = $"Mapa{_map?.MapNumber ?? 1}.aomap";
        _saveDialog.Popup();
    }

    private void OnSaveFileSelected(string path)
    {
        if (_map == null) return;
        string dir = Path.GetDirectoryName(path) ?? _state.MapDir;
        _state.MapDir = dir;

        // Extract map number from filename (e.g. "Mapa1.map" → 1)
        string filename = Path.GetFileNameWithoutExtension(path);
        if (filename.StartsWith("Mapa", StringComparison.OrdinalIgnoreCase) &&
            int.TryParse(filename.Substring(4), out int parsedNum) && parsedNum > 0)
        {
            _map.MapNumber = parsedNum;
            _state.CurrentMapNumber = parsedNum;
        }

        // Save map — loose files
        if (_clientMapDir.Length > 0 && Directory.Exists(_clientMapDir))
            MapLoader.Save(_clientMapDir, _map);
        if (_serverMapDir.Length > 0 && Directory.Exists(_serverMapDir))
            MapLoader.Save(_serverMapDir, _map);
        // Also save to the explicitly chosen dir if different
        if (dir != _clientMapDir && dir != _serverMapDir)
            MapLoader.Save(dir, _map);

        _state.MapDir = dir;
        _state.ResetDirty();
        _state.ScanAvailableMaps(dir);
        UpdateNavBar();
        SetStatus($"Mapa {_map.MapNumber} guardado (cliente + server)");
        _exportConfirmDialog?.PopupCentered();
    }

    private void OnDataPathSelected(string path)
    {
        _dataLoaded = false; // User explicitly chose a new path — allow reload
        LoadDataPath(path);
    }

    private void OnServerPathSelected(string path)
    {
        string datDir = Path.Combine(path, "dat");
        string mapsDir = Path.Combine(path, "maps");

        if (Directory.Exists(datDir)) _serverDatDir = datDir;
        if (Directory.Exists(mapsDir))
        {
            _serverMapDir = mapsDir;
            _state.MapDir = mapsDir;
            _state.ScanAvailableMaps(mapsDir);
        }

        // Reload NPC/object data from server
        if (_serverDatDir.Length > 0 && Directory.Exists(_serverDatDir))
        {
            string objDat = Path.Combine(_serverDatDir, "Obj.dat");
            _objGrhs = GameDataLoader.LoadObjectGrhs(objDat);
            _doorData = GameDataLoader.LoadDoorData(objDat);
            string npcDat = Path.Combine(_serverDatDir, "NPCs.dat");
            (_npcBodies, _npcHeads) = GameDataLoader.LoadNpcData(npcDat);
            _npcDb = NpcDatabase.Load(_serverDatDir);
            _objDb = ObjectDatabase.Load(objDat);
            SyncNpcPaletteData();
            SyncObjPaletteData();
            SyncViewportData();
        }

        SaveConfig();
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

    private void SyncObjPaletteData()
    {
        if (_objPalette == null) return;
        _objPalette.Database = _objDb;
        _objPalette.Grhs = _grhs;
        _objPalette.Textures = _textures;
        _objPalette.ObjGrhs = _objGrhs;
        _objPalette.Rebuild();
    }

    private void OnNpcPaletteSelected(int npcNumber)
    {
        SetActiveTool(EditorTool.Npc);
        _state.SelectedNpcNumber = npcNumber;
        SetStatus($"NPC #{npcNumber} seleccionado — click en el mapa para colocar");
    }

    private void OnObjectPaletteSelected(int objNumber)
    {
        SetActiveTool(EditorTool.Object);
        _state.SelectedObjectNumber = objNumber;
        SetStatus($"Objeto #{objNumber} seleccionado — click en el mapa para colocar");
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
                Size = new Vector2I(800, 638), // 608 (19*32 viewport) + 30 (top bar)
                Visible = false,
                Exclusive = false,
                AlwaysOnTop = true,
                Unresizable = false,
                ContentScaleMode = Window.ContentScaleModeEnum.Disabled,
            };
            _walkWindow.CloseRequested += () => _walkWindow.Visible = false;
            AddChild(_walkWindow);

            // Resolution selector bar at top
            var topBar = new HBoxContainer();
            topBar.Position = Vector2.Zero;
            topBar.AddThemeConstantOverride("separation", 8);
            var resLabel = new Label { Text = "Resolución:" };
            topBar.AddChild(resLabel);
            var resOption = new OptionButton();
            for (int i = 0; i < WalkModePanel.Resolutions.Length; i++)
                resOption.AddItem(WalkModePanel.Resolutions[i].Label, i);
            resOption.Selected = 0; // 800x600 default
            topBar.AddChild(resOption);
            _walkWindow.AddChild(topBar);

            _walkPanel = new WalkModePanel();
            _walkPanel.Position = new Vector2(0, 30); // below the top bar
            _walkWindow.AddChild(_walkPanel);

            resOption.ItemSelected += (long idx) =>
            {
                var res = WalkModePanel.Resolutions[(int)idx];
                _walkPanel.SetResolution(res.W, res.H);
                int viewH = (res.H / 32 / 2 * 2 + 1) * 32;
                _walkWindow.Size = new Vector2I(Math.Max(res.W, (res.W / 32 / 2 * 2 + 1) * 32), viewH + 30);
            };
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
            (int)(GetViewportRect().Size.X / 2 - _walkWindow.Size.X / 2),
            (int)(GetViewportRect().Size.Y / 2 - _walkWindow.Size.Y / 2));

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

    private void InsertMap()
    {
        if (_map == null) return;
        if (_state.Pending.Active) return;
        _insertFormat = LegacyMapFormat.AutoDetect;
        if (_insertFormatSelect != null) _insertFormatSelect.Selected = 0;
        _insertFormatWindow!.PopupCentered();
    }

    private void OnInsertSelectMapPressed()
    {
        if (_insertFormatSelect == null) return;
        _insertFormat = (LegacyMapFormat)_insertFormatSelect.GetSelectedId();
        _insertFormatWindow!.Hide();
        _insertFileDialog!.Popup();
    }

    private void OnInsertMapFileSelected(string path)
    {
        if (_map == null || _state.Pending.Active) return;

        MapData imported;
        string formatInfo;

        try
        {
            var resolvedFormat = _insertFormat;
            if (resolvedFormat == LegacyMapFormat.AutoDetect)
            {
                resolvedFormat = LegacyMapLoader.DetectFormat(path);
                GD.Print($"[InsertMap] Auto-detected format: {LegacyMapLoader.FormatLabel(resolvedFormat)}");
            }
            imported = LegacyMapLoader.Load(path, resolvedFormat);
            formatInfo = LegacyMapLoader.FormatLabel(resolvedFormat);
        }
        catch (Exception ex)
        {
            SetStatus($"ERROR: no se pudo cargar '{Path.GetFileName(path)}': {ex.Message}");
            GD.PrintErr($"[InsertMap] Failed to load {path}: {ex}");
            return;
        }

        int w = imported.Width;
        int h = imported.Height;
        if (w <= 0 || h <= 0)
        {
            SetStatus("ERROR: el mapa importado está vacío");
            return;
        }

        // Convert 1-indexed MapData tiles to 0-indexed buffer for PendingPlacement
        var buf = new MapTile[w, h];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
                buf[x, y] = imported.Tiles[x + 1, y + 1];

        // Place centered on current viewport
        int originX, originY;
        if (_viewport != null)
        {
            float invZoom = 1f / Math.Max(_state.Zoom, 0.01f);
            int viewCenterX = (int)((-_state.CameraOffset.X * invZoom + _viewport.Size.X * invZoom / 2) / 32);
            int viewCenterY = (int)((-_state.CameraOffset.Y * invZoom + _viewport.Size.Y * invZoom / 2) / 32);
            originX = Math.Max(1, viewCenterX - w / 2);
            originY = Math.Max(1, viewCenterY - h / 2);
        }
        else
        {
            originX = 1;
            originY = 1;
        }

        _state.Pending.Begin(buf, w, h, originX, originY, isInsert: true);
        SetStatus($"Insertando mapa {w}x{h} ({formatInfo}) — arrastrá y ✓ para aceptar, ✗ para cancelar");
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
        bool wasMove = p.IsMove;
        bool wasInsert = p.IsInsert;

        // After inserting a map, create a selection on the applied area
        if (wasInsert)
        {
            _state.SetSelection(p.OriginX, p.OriginY,
                p.OriginX + p.Width - 1, p.OriginY + p.Height - 1);
            _state.InsertedMapSelection = true;
        }

        p.Cancel();
        if (wasMove) SetActiveTool(EditorTool.Select);
        if (wasInsert) SetActiveTool(EditorTool.Select);
        _viewport?.QueueRedraw();
    }

    /// <summary>
    /// Cancel the pending placement (restore map if move).
    /// </summary>
    public void CancelPendingPlacement()
    {
        if (!_state.Pending.Active) return;

        var p = _state.Pending;
        bool wasMove = p.IsMove;
        if (wasMove && p.MoveSnapshot != null && _map != null)
            Array.Copy(p.MoveSnapshot, _map.Tiles, _map.Tiles.Length);
        p.Cancel();
        SetStatus("Cancelado");
        if (wasMove) SetActiveTool(EditorTool.Select);
        _viewport?.QueueRedraw();
    }


    private void OnDeselectSelection()
    {
        // Cancel floating insert before it's committed
        if (_state.Pending.Active && _state.Pending.IsInsert)
        {
            CancelPendingPlacement();
            return;
        }

        if (_state.InsertedMapSelection)
        {
            // Undo the insert — revert the tiles that were stamped
            _undo.Undo(_map!);
            _state.InsertedMapSelection = false;
            SetStatus("Mapa insertado descartado");
        }
        _state.ClearSelection();
        _viewport?.QueueRedraw();
    }

    private void OnSelectionCompleted()
    {
        // If a zone edit popup requested map selection, feed the coords back
        if (_pendingZonePopup != null && _state.HasSelection)
        {
            _pendingZonePopup.SetBoundsFromSelection(
                _state.SelX1, _state.SelY1, _state.SelX2, _state.SelY2);
            _pendingZonePopup = null;
        }
    }

    private void ShowTrimBordersDialog()
    {
        if (_trimBordersWindow == null) BuildTrimBordersDialog();
        _trimBordersSpin!.Value = 6;
        _trimBordersWindow!.PopupCentered();
    }

    private void BuildTrimBordersDialog()
    {
        _trimBordersWindow = new Window();
        _trimBordersWindow.Title = "Cortar Bordes";
        _trimBordersWindow.Size = new Vector2I(300, 160);
        _trimBordersWindow.Exclusive = true;
        _trimBordersWindow.Unresizable = true;
        _trimBordersWindow.Visible = false;
        _trimBordersWindow.Transient = true;
        _trimBordersWindow.WrapControls = true;
        _trimBordersWindow.CloseRequested += () => _trimBordersWindow.Hide();

        var margin = new MarginContainer();
        margin.AddThemeConstantOverride("margin_left", 20);
        margin.AddThemeConstantOverride("margin_right", 20);
        margin.AddThemeConstantOverride("margin_top", 16);
        margin.AddThemeConstantOverride("margin_bottom", 16);
        margin.SetAnchorsPreset(Control.LayoutPreset.FullRect);

        var vbox = new VBoxContainer();
        vbox.AddThemeConstantOverride("separation", 12);

        var label = new Label { Text = "Tiles a cortar de cada borde:" };
        label.AddThemeFontSizeOverride("font_size", EditorTheme.FONT_MD);
        vbox.AddChild(label);

        _trimBordersSpin = EditorTheme.MakeSpinBox(1, 50, 1);
        _trimBordersSpin.Value = 6;
        vbox.AddChild(_trimBordersSpin);

        var applyBtn = EditorTheme.PrimaryButton("Aplicar recorte");
        applyBtn.Pressed += OnTrimBordersApply;
        vbox.AddChild(applyBtn);

        margin.AddChild(vbox);
        _trimBordersWindow.AddChild(margin);
        AddChild(_trimBordersWindow);
    }

    private void OnTrimBordersApply()
    {
        _trimBordersWindow?.Hide();
        if (_map == null || !_state.HasSelection) return;

        int trim = (int)(_trimBordersSpin?.Value ?? 6);
        int newX1 = _state.SelX1 + trim;
        int newY1 = _state.SelY1 + trim;
        int newX2 = _state.SelX2 - trim;
        int newY2 = _state.SelY2 - trim;

        if (newX1 > newX2 || newY1 > newY2)
        {
            SetStatus("ERROR: recorte demasiado grande, no queda área");
            return;
        }

        // Clear the border tiles
        _undo.BeginBatch("Trim borders");
        for (int y = _state.SelY1; y <= _state.SelY2; y++)
            for (int x = _state.SelX1; x <= _state.SelX2; x++)
            {
                if (x >= newX1 && x <= newX2 && y >= newY1 && y <= newY2) continue;
                if (!_map.InBounds(x, y)) continue;
                var before = _map.Tiles[x, y];
                _map.Tiles[x, y] = new MapTile();
                _undo.RecordTileChange(x, y, before, _map.Tiles[x, y]);
            }
        _undo.EndBatch();

        // Shrink selection to trimmed area
        _state.SetSelection(newX1, newY1, newX2, newY2);
        _state.MarkDirty();
        SetStatus($"Recortados {trim} tiles de cada borde — selección ahora {newX2 - newX1 + 1}x{newY2 - newY1 + 1}");
        _viewport?.QueueRedraw();
    }

    #endregion

    #region Map Navigation Bar

    private void UpdateNavBar()
    {
        if (_mapsMenu == null) return;
        _mapsMenu.Clear();

        var maps = _state.AvailableMaps.OrderBy(n => n).ToList();
        if (maps.Count == 0)
        {
            _mapsMenu.AddItem("(sin mapas)", -1);
            _mapsMenu.SetItemDisabled(0, true);
            return;
        }

        foreach (int num in maps)
        {
            string label = _state.CurrentMapNumber == num ? $"► Mapa {num}" : $"Mapa {num}";
            _mapsMenu.AddItem(label, num);
        }
    }

    private void OnMapsMenuId(long id)
    {
        if (id <= 0) return;
        CheckDirtyThen(() =>
        {
            if (string.IsNullOrEmpty(_state.MapDir) || !System.IO.Directory.Exists(_state.MapDir)) return;
            _map = MapLoader.Load(_state.MapDir, (int)id);
            if (_map == null) { SetStatus($"No se pudo cargar Mapa {id}"); return; }
            _state.CurrentMapNumber = (int)id;
            _undo.Clear();
            _state.ResetDirty();
            UpdateViewport();
            UpdateNavBar();
            SetStatus($"Mapa {id} cargado");
        });
    }

    #endregion

    #region Input

    private static readonly string[] ToolNames = {
        "Mano", "Pintar", "Borrar", "Seleccionar", "Agarrar",
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
                case Key.I: InsertMap(); break;
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
        // Keep selection when in Select or Move (Move requires an active selection)
        if (tool != EditorTool.Select && tool != EditorTool.Move)
            _state.ClearSelection();
        SyncToolBar();
        UpdateLightSection();
    }

    private void SyncLayerTabs()
    {
        for (int i = 0; i < 4; i++)
            _layerTabButtons[i].ButtonPressed = _state.ActiveLayer == (i + 1);
    }

    // Maps toolbar button index to EditorTool
    private static readonly EditorTool[] ToolBarOrder = {
        EditorTool.Hand, EditorTool.Paint, EditorTool.Erase,
        EditorTool.Select,
        EditorTool.Pick, EditorTool.Eyedrop, EditorTool.Block,
        // property tools (after separator)
        EditorTool.Light, EditorTool.Exit, EditorTool.Trigger,
    };

    private void SyncToolBar()
    {
        for (int i = 0; i < _toolBarButtons.Length && i < ToolBarOrder.Length; i++)
        {
            bool active = _state.ActiveTool == ToolBarOrder[i];
            _toolBarButtons[i].ButtonPressed = active;
        }
    }

    private void BlockSelection(bool blocked)
    {
        if (_map == null || !_state.HasSelection) return;
        _undo.BeginBatch(blocked ? "Block Selection" : "Unblock Selection");
        for (int y = _state.SelY1; y <= _state.SelY2; y++)
            for (int x = _state.SelX1; x <= _state.SelX2; x++)
            {
                if (!_map.InBounds(x, y)) continue;
                var before = _map.Tiles[x, y];
                _map.Tiles[x, y].Blocked = blocked;
                _undo.RecordTileChange(x, y, before, _map.Tiles[x, y]);
            }
        _undo.EndBatch();
        _viewport?.QueueRedraw();
    }

    private void ClearSelectionTiles_Confirm1()
    {
        if (_map == null || !_state.HasSelection) return;
        int w = _state.SelX2 - _state.SelX1 + 1;
        int h = _state.SelY2 - _state.SelY1 + 1;
        var dlg = new ConfirmationDialog
        {
            Title = "Borrar todo",
            DialogText = $"¿Borrar TODOS los tiles en el área seleccionada ({w}×{h} tiles)?\nEsto limpia las 4 capas, NPCs, objetos, exits, triggers, luces y partículas.",
            OkButtonText = "Sí, borrar",
            CancelButtonText = "Cancelar",
            Size = new Vector2I(400, 180),
        };
        dlg.Confirmed += () => { dlg.QueueFree(); ClearSelectionTiles_Confirm2(w, h); };
        dlg.Canceled += () => dlg.QueueFree();
        AddChild(dlg);
        dlg.PopupCentered();
    }

    private void ClearSelectionTiles_Confirm2(int w, int h)
    {
        var dlg = new ConfirmationDialog
        {
            Title = "Confirmar borrado",
            DialogText = $"¿Estás SEGURO? Se borrarán {w * h} tiles. Esta acción se puede deshacer con Ctrl+Z.",
            OkButtonText = "Borrar definitivamente",
            CancelButtonText = "Cancelar",
            Size = new Vector2I(400, 160),
        };
        dlg.Confirmed += () => { dlg.QueueFree(); ClearSelectionTiles(); };
        dlg.Canceled += () => dlg.QueueFree();
        AddChild(dlg);
        dlg.PopupCentered();
    }

    private void ClearSelectionTiles()
    {
        if (_map == null || !_state.HasSelection) return;
        _undo.BeginBatch("Clear Selection");
        for (int y = _state.SelY1; y <= _state.SelY2; y++)
            for (int x = _state.SelX1; x <= _state.SelX2; x++)
            {
                if (!_map.InBounds(x, y)) continue;
                var before = _map.Tiles[x, y];
                _map.Tiles[x, y] = default;
                _undo.RecordTileChange(x, y, before, _map.Tiles[x, y]);
            }
        _undo.EndBatch();
        _state.MarkDirty();
        _viewport?.QueueRedraw();
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

    private int _lastSelectedTileX = -2, _lastSelectedTileY = -2;

    private void UpdateSelectedTileInfo()
    {
        bool hasTile = _state.HasSelectedTile && _map != null;
        bool visible = hasTile;

        if (_rightTileSection != null && _rightTileSection.Visible != visible)
        {
            _rightTileSection.Visible = visible;
            if (_rightTileSeparator != null) _rightTileSeparator.Visible = visible;
        }

        if (!visible || _rightTileInfoLabel == null) return;

        int x = _state.SelectedTileX, y = _state.SelectedTileY;

        // Only update text when the selected tile changes
        if (x == _lastSelectedTileX && y == _lastSelectedTileY) return;
        _lastSelectedTileX = x;
        _lastSelectedTileY = y;

        if (!_map!.InBounds(x, y))
        {
            _rightTileInfoLabel.Text = "Fuera del mapa";
            return;
        }

        ref var tile = ref _map.Tiles[x, y];
        var lines = new System.Text.StringBuilder();
        lines.AppendLine($"({x}, {y})");
        lines.AppendLine($"L1: {(tile.Layer1 > 0 ? tile.Layer1.ToString() : "--")}");
        lines.AppendLine($"L2: {(tile.Layer2 > 0 ? tile.Layer2.ToString() : "--")}");
        lines.AppendLine($"L3: {(tile.Layer3 > 0 ? tile.Layer3.ToString() : "--")}");
        lines.AppendLine($"L4: {(tile.Layer4 > 0 ? tile.Layer4.ToString() : "--")}");
        if (tile.Blocked) lines.AppendLine("Bloqueado: ✓");
        if (tile.Trigger > 0)
        {
            string trigName = tile.Trigger switch {
                1 => "Indoor", 3 => "Pos inválida", 4 => "Zona segura",
                5 => "Anti-bloqueo", 6 => "Combate", _ => tile.Trigger.ToString()
            };
            lines.AppendLine($"Trigger: {trigName}");
        }
        if (tile.HasExit) lines.AppendLine($"Salida: M{tile.ExitMap} ({tile.ExitX},{tile.ExitY})");
        if (tile.HasNpc)
        {
            string npcName = _npcDb?.Get(tile.NpcIndex)?.Name ?? "";
            lines.AppendLine(npcName.Length > 0
                ? $"NPC: #{tile.NpcIndex} {npcName}"
                : $"NPC: #{tile.NpcIndex}");
        }
        if (tile.HasObject) lines.AppendLine($"Obj: #{tile.ObjIndex} x{tile.ObjAmount}");
        if (tile.HasLight) lines.AppendLine($"Luz: R{tile.LightRange} ({tile.LightR},{tile.LightG},{tile.LightB})");
        if (tile.ParticleGroup > 0) lines.AppendLine($"Partícula: {tile.ParticleGroup}");

        _rightTileInfoLabel.Text = lines.ToString().TrimEnd();
    }

    public override void _Process(double delta)
    {
        DoLayout();

        // Blocking preload — nothing else runs until textures + previews are done
        if (_preloadPhase > 0)
        {
            TickTexturePreload();
            return;
        }

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
        UpdateSelectedTileInfo();

        _palette?.SyncLayerUI();
        SyncToolBar();
        SyncLayerTabs();
        UpdateRightSidebar();
        UpdateSelectionSection();
        _viewport?.QueueRedraw();
    }

    private TextureRef? _lastRightSidebarTexRef;
    private int _lastRightSidebarEyedrop;
    private bool _lastRightSidebarHasSel;
    private int _lastSelX1, _lastSelY1, _lastSelX2, _lastSelY2;
    private EditorTool _lastSelActiveTool = EditorTool.Hand;

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

        // Update fill button enabled state when texture changes
        UpdateSelectionSection();
    }

    private void UpdateSelectionSection()
    {
        if (_rightSelectionSection == null || _rightSelectionLabel == null || _rightFillButton == null) return;

        bool hasSel = _state.HasSelection;
        bool hasPendingInsert = _state.Pending.Active && _state.Pending.IsInsert;

        _rightSelectionSection.Visible = hasSel || hasPendingInsert;
        if (_rightSelSeparator != null) _rightSelSeparator.Visible = hasSel || hasPendingInsert;

        // Show insert info during floating phase (before commit)
        if (hasPendingInsert && !hasSel)
        {
            var p = _state.Pending;
            _rightSelectionLabel.Text = $"MAPA A INSERTAR\nPosición: ({p.OriginX},{p.OriginY})\n{p.Width}x{p.Height} tiles\nArrastrá para mover, ✓ para aplicar";
            _rightFillButton.Visible = false;
            if (_rightMoveAreaButton != null) _rightMoveAreaButton.Visible = false;
            if (_rightInsertApplyButton != null) _rightInsertApplyButton.Visible = false;
            if (_rightInsertTrimButton != null) _rightInsertTrimButton.Visible = false;
            if (_rightDeselectButton != null) _rightDeselectButton.Visible = true;
            return;
        }

        if (hasSel)
        {
            bool coordsChanged = hasSel != _lastRightSidebarHasSel ||
                _state.SelX1 != _lastSelX1 || _state.SelY1 != _lastSelY1 ||
                _state.SelX2 != _lastSelX2 || _state.SelY2 != _lastSelY2;

            // Only update label text when coordinates change
            if (coordsChanged)
            {
                int w = _state.SelX2 - _state.SelX1 + 1;
                int h = _state.SelY2 - _state.SelY1 + 1;
                string prefix = _state.InsertedMapSelection ? "MAPA INSERTADO\n" : "";
                _rightSelectionLabel.Text = $"{prefix}({_state.SelX1},{_state.SelY1}) \u2192 ({_state.SelX2},{_state.SelY2})\n{w}x{h} tiles";
                UpdateSelectionThumbnail();
            }

            // Restore normal button visibility (may have been hidden during floating phase)
            _rightFillButton.Visible = true;
            if (_rightMoveAreaButton != null) _rightMoveAreaButton.Visible = true;
            if (_rightDeselectButton != null) _rightDeselectButton.Visible = true;

            // Always re-evaluate fill button (texture may have changed)
            bool hasTexture = _state.SelectedTexture != null || _state.EyedropGrh > 0;
            _rightFillButton.Disabled = !hasTexture;

            // Sync move button toggle state when tool changes
            if (_state.ActiveTool != _lastSelActiveTool)
                _rightMoveAreaButton?.SetPressedNoSignal(_state.ActiveTool == EditorTool.Move);

            // Show/hide insert-specific buttons
            bool isInsert = _state.InsertedMapSelection;
            if (_rightInsertApplyButton != null) _rightInsertApplyButton.Visible = isInsert;
            if (_rightInsertTrimButton != null) _rightInsertTrimButton.Visible = isInsert;
        }

        _lastRightSidebarHasSel = hasSel;
        _lastSelX1 = _state.SelX1;
        _lastSelY1 = _state.SelY1;
        _lastSelX2 = _state.SelX2;
        _lastSelY2 = _state.SelY2;
        _lastSelActiveTool = _state.ActiveTool;
    }

    private void UpdateSelectionThumbnail()
    {
        if (_rightSelPreview == null || _map == null || _grhs == null || _textures == null) return;

        const int TileRender = 8;   // pixels per tile in the thumbnail
        const int MaxTiles = 16;    // cap both axes

        int selW = Math.Min(_state.SelX2 - _state.SelX1 + 1, MaxTiles);
        int selH = Math.Min(_state.SelY2 - _state.SelY1 + 1, MaxTiles);
        int imgW = selW * TileRender;
        int imgH = selH * TileRender;

        if (imgW <= 0 || imgH <= 0) { _rightSelPreview.Texture = null; return; }

        var img = Image.CreateEmpty(imgW, imgH, false, Image.Format.Rgba8);

        for (int ty = 0; ty < selH; ty++)
        {
            for (int tx = 0; tx < selW; tx++)
            {
                int mx = _state.SelX1 + tx;
                int my = _state.SelY1 + ty;
                if (!_map.InBounds(mx, my)) continue;

                var tile = _map.Tiles[mx, my];

                // Render L1 and L2 (base layers) into the thumbnail
                int[] grhIds = { tile.Layer1, tile.Layer2 };
                foreach (int grhId in grhIds)
                {
                    if (grhId <= 0 || grhId >= _grhs.Length) continue;
                    var grh = _grhs[grhId];
                    if (grh.NumFrames <= 0) continue;
                    if (grh.NumFrames > 1 && grh.Frames != null && grh.Frames.Length > 0)
                    {
                        int fIdx = grh.Frames[0];
                        if (fIdx > 0 && fIdx < _grhs.Length) grh = _grhs[fIdx];
                    }
                    if (grh.FileNum <= 0) continue;

                    var srcImg = _textures.GetImageCached(grh.FileNum);
                    if (srcImg == null) continue;

                    int srcX = Math.Max(0, (int)grh.SX);
                    int srcY = Math.Max(0, (int)grh.SY);
                    int srcW = Math.Max(1, Math.Min((int)grh.PixelWidth, srcImg.GetWidth() - srcX));
                    int srcH = Math.Max(1, Math.Min((int)grh.PixelHeight, srcImg.GetHeight() - srcY));

                    var region = srcImg.GetRegion(new Rect2I(srcX, srcY, srcW, srcH));
                    region.Resize(TileRender, TileRender, Image.Interpolation.Nearest);

                    img.BlitRect(region, new Rect2I(0, 0, TileRender, TileRender),
                        new Vector2I(tx * TileRender, ty * TileRender));
                }
            }
        }

        _rightSelPreview.Texture = ImageTexture.CreateFromImage(img);
    }

    private HSlider CreateLightSlider(float min, float max, float value, Action<float> onChange)
    {
        var slider = new HSlider
        {
            MinValue = min,
            MaxValue = max,
            Value = value,
            Step = 1,
            CustomMinimumSize = new Vector2(0, 20),
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
        };
        slider.ValueChanged += v => onChange((float)v);
        return slider;
    }

    private void UpdateLightPreview()
    {
        if (_lightPreviewRect != null)
            _lightPreviewRect.Color = new Color(_state.LightR / 255f, _state.LightG / 255f, _state.LightB / 255f);
    }

    private void UpdateLightSection()
    {
        bool show = _state.ActiveTool == EditorTool.Light;
        if (_rightLightSection != null) _rightLightSection.Visible = show;
        if (_rightLightSeparator != null) _rightLightSeparator.Visible = show;
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
            _textures?.Cleanup(); // Free GPU textures before exit
            if (_state.IsDirty)
            {
                CheckDirtyThen(() => GetTree().Quit());
                GetTree().AutoAcceptQuit = false;
            }
        }
    }

    #endregion

    // ── Zone editing ─────────────────────────────────────────────

    private void ShowZoneEditPopup(ZoneInfo? zone)
    {
        if (zone == null) return;

        var popup = new ZoneEditPopup
        {
            Zone = zone,
            ZoneData = _mapZones,
        };
        popup.OnSaved += () =>
        {
            _zonePanel?.RebuildList();
            _viewport?.QueueRedraw();
            _state.MarkDirty();
        };
        popup.OnRequestMapSelect += () =>
        {
            // Activate Select tool — when user finishes selection, fill bounds back
            SetActiveTool(EditorTool.Select);
            _pendingZonePopup = popup;
            SetStatus("Seleccioná el área de la zona en el mapa, luego suelta para confirmar");
        };
        AddChild(popup);
        popup.PopupCentered();
    }
}
