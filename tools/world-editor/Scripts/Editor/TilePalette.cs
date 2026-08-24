#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using AOWorldEditor.Data;

namespace AOWorldEditor.Editor;

/// <summary>
/// Sidebar panel showing texture references from indices.ini, organized by category.
/// Categories displayed as a wrapped button grid (2 rows) instead of a scrolling TabBar.
/// Uses AtlasTexture for zero-copy GPU-side previews (no GetImage() stalls).
/// </summary>
public partial class TilePalette : VBoxContainer
{
    [Signal] public delegate void LayerChangedEventHandler(int layer);

    /// <summary>Fired when the user builds a combined stamp from Ctrl+Click multi-selection.
    /// Listener should paste it the same way as Ctrl+V (State.Clipboard is already populated).</summary>
    [Signal] public delegate void MultiStampReadyEventHandler();

    /// <summary>Asks the editor to switch to the docked "Láminas" sidebar tab.</summary>
    [Signal] public delegate void SheetTabRequestedEventHandler();
    [Signal] public delegate void CommerceCaptureRequestedEventHandler();
    [Signal] public delegate void CommerceStampRequestedEventHandler();

    private ScrollContainer? _scrollContainer;
    private GridContainer? _grid;
    private Label? _infoLabel;
    private FlowContainer? _categoryFlow;
    private LineEdit? _searchBox;
    private Button? _useSelectionButton;
    private Label? _zoomLabel;
    private HBoxContainer? _recentRow;
    private VBoxContainer? _recentSection;

    // Ctrl+Click multi-selection, in click order — combined into one stamp via BuildMultiStamp()
    private readonly List<TextureRef> _multiSelected = new();

    public TextureCatalog? Catalog;
    public GrhData[]? Grhs;
    public TextureManager? Textures;
    public EditorState? State;
    public string IndicesPath = "";

    private string _activeCategory = "";
    private string _searchFilter = "";
    private readonly List<Button> _categoryButtons = new();

    // Preview cell size is user-adjustable: big art (trees, houses) is unreadable
    // at thumbnail size, while terrain tiles are easier to scan when many fit.
    private const int PreviewSizeMin = 48;
    private const int PreviewSizeMax = 128;
    private const int PreviewSizeStep = 16;
    private int _previewSize = 64;

    // Godot reserves horizontal room for the vertical scrollbar inside a
    // ScrollContainer. Ignoring it makes the last column slide underneath the
    // bar, which is what made the grid look offset against the panel edge.
    private const int ScrollbarReserve = 14;
    private const int GridSeparation = 4;

    // Columns are derived from the real available width instead of a fixed 4,
    // so the grid stays aligned at any sidebar width or preview size.
    private int _columns = 4;
    private float _lastGridWidth = -1;

    // Preview cache: GRH index → cached preview texture (AtlasTexture or composited)
    private readonly Dictionary<int, Texture2D?> _previewCache = new();
    // Grid buttons tracking for highlight updates without full rebuild
    private readonly List<(DraggableTextureButton btn, TextureRef texRef)> _gridButtons = new();

    public override void _Ready()
    {
        SizeFlagsHorizontal = SizeFlags.ExpandFill;
        ClipContents = true;
        AddThemeConstantOverride("separation", 3);

        // Search box
        _searchBox = new LineEdit();
        _searchBox.PlaceholderText = "Buscar textura...";
        _searchBox.ClearButtonEnabled = true;
        _searchBox.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        _searchBox.CustomMinimumSize = new Vector2(0, 28);
        _searchBox.AddThemeFontSizeOverride("font_size", 11);
        _searchBox.TextChanged += OnSearchChanged;
        AddChild(_searchBox);

        // The catalog is intentionally curated, but maps also need access to
        // valid GRHs that have not yet been described in indices.ini.
        var rawGrhButton = new Button
        {
            Text = "Explorar GRH libres...",
            TooltipText = "Abre todos los sprites válidos de Graficos.ind para pintarlos sin crear una referencia en indices.ini.",
            CustomMinimumSize = new Vector2(0, 26),
        };
        rawGrhButton.AddThemeFontSizeOverride("font_size", EditorTheme.FONT_SM);
        rawGrhButton.Pressed += OpenRawGrhPicker;
        AddChild(rawGrhButton);

        var sheetButton = new Button
        {
            Text = "Abrir lámina PNG...",
            TooltipText = "Abre el explorador de láminas en el panel (pestaña \"Láminas\"), sin tapar el mapa.\nShift+clic para la ventana grande.",
            CustomMinimumSize = new Vector2(0, 26),
        };
        sheetButton.AddThemeFontSizeOverride("font_size", EditorTheme.FONT_SM);
        // Docked by default — the map stays visible while choosing. The old modal
        // window is still one Shift+click away for a full-screen look at a sheet.
        sheetButton.Pressed += () =>
        {
            if (Input.IsKeyPressed(Key.Shift)) OpenGraphicsSheetPicker();
            else EmitSignal(SignalName.SheetTabRequested);
        };
        AddChild(sheetButton);

        // Recent raw GRHs — one-click reuse for big pieces that would otherwise
        // mean reopening the browser and recalling their number every time.
        _recentSection = new VBoxContainer();
        _recentSection.AddThemeConstantOverride("separation", 2);
        _recentSection.Visible = false;
        _recentSection.AddChild(EditorTheme.SectionLabel("Recientes"));
        _recentRow = new HBoxContainer();
        _recentRow.AddThemeConstantOverride("separation", 3);
        _recentSection.AddChild(_recentRow);
        AddChild(_recentSection);

        // Brush size — circular radius applied to Paint/Erase/Block so large
        // terrain areas don't have to be painted one tile at a time. 0 = single
        // tile (classic behavior). Same UX pattern as HumoConfigPanel's fog brush.
        var brushRow = new HBoxContainer();
        brushRow.AddThemeConstantOverride("separation", 6);
        brushRow.AddChild(EditorTheme.MakeLabel("Pincel:", EditorTheme.TEXT_SECONDARY, EditorTheme.FONT_SM));
        var brushSpin = new SpinBox { MinValue = 0, MaxValue = 20, Value = 0, Step = 1 };
        brushSpin.AddThemeFontSizeOverride("font_size", EditorTheme.FONT_SM);
        brushSpin.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        brushSpin.TooltipText = "Radio del pincel en tiles (0 = un tile por click)";
        brushSpin.ValueChanged += v => { if (State != null) State.PaintBrushRadius = (int)v; };
        brushRow.AddChild(brushSpin);
        AddChild(brushRow);

        // Camino is deliberately a manual brush. Road art has distinct edge
        // pieces, therefore the mapper chooses its orientation before drawing.
        var pathRow = new HBoxContainer();
        pathRow.AddThemeConstantOverride("separation", 6);
        pathRow.AddChild(EditorTheme.MakeLabel("Camino:", EditorTheme.TEXT_SECONDARY, EditorTheme.FONT_SM));
        var pathMode = new OptionButton
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            TooltipText = "Forma usada por la herramienta Camino. Pasto pinta solamente terreno.",
        };
        pathMode.AddItem("Pasto", (int)PathBrushMode.Grass);
        pathMode.AddItem("Camino centrado", (int)PathBrushMode.Centered);
        pathMode.AddItem("Recto horizontal", (int)PathBrushMode.Horizontal);
        pathMode.AddItem("Recto vertical", (int)PathBrushMode.Vertical);
        pathMode.AddItem("Recto derecha", (int)PathBrushMode.Right);
        pathMode.AddItem("Recto izquierda", (int)PathBrushMode.Left);
        pathMode.AddItem("Curva arriba derecha", (int)PathBrushMode.UpRight);
        pathMode.AddItem("Curva arriba izquierda", (int)PathBrushMode.UpLeft);
        pathMode.AddItem("Curva abajo izquierda", (int)PathBrushMode.DownLeft);
        pathMode.AddItem("Curva abajo derecha", (int)PathBrushMode.DownRight);
        pathMode.AddItem("Costa pasto: arriba", (int)PathBrushMode.CoastTop);
        pathMode.AddItem("Costa pasto: abajo", (int)PathBrushMode.CoastBottom);
        pathMode.AddItem("Costa pasto: izquierda", (int)PathBrushMode.CoastLeft);
        pathMode.AddItem("Costa pasto: derecha", (int)PathBrushMode.CoastRight);
        pathMode.AddItem("Costa pasto: arriba izquierda", (int)PathBrushMode.CoastTopLeft);
        pathMode.AddItem("Costa pasto: arriba derecha", (int)PathBrushMode.CoastTopRight);
        pathMode.AddItem("Costa pasto: abajo izquierda", (int)PathBrushMode.CoastBottomLeft);
        pathMode.AddItem("Costa pasto: abajo derecha", (int)PathBrushMode.CoastBottomRight);
        pathMode.Select((int)(State?.PathBrush ?? PathBrushMode.Grass));
        pathMode.ItemSelected += index =>
        {
            if (State != null) State.PathBrush = (PathBrushMode)index;
        };
        pathRow.AddChild(pathMode);
        AddChild(pathRow);

        var grassSizeRow = new HBoxContainer();
        grassSizeRow.AddThemeConstantOverride("separation", 4);
        grassSizeRow.AddChild(EditorTheme.MakeLabel("Pasto:", EditorTheme.TEXT_SECONDARY, EditorTheme.FONT_SM));
        grassSizeRow.AddChild(EditorTheme.MakeLabel("An.", EditorTheme.TEXT_MUTED, EditorTheme.FONT_SM));
        var grassWidth = new SpinBox { MinValue = 1, MaxValue = 40, Step = 1, Value = State?.PathGrassWidth ?? 1 };
        grassWidth.CustomMinimumSize = new Vector2(46, 0);
        grassWidth.TooltipText = "Ancho del sello de pasto en tiles";
        grassWidth.ValueChanged += value => { if (State != null) State.PathGrassWidth = (int)value; };
        grassSizeRow.AddChild(grassWidth);
        grassSizeRow.AddChild(EditorTheme.MakeLabel("Al.", EditorTheme.TEXT_MUTED, EditorTheme.FONT_SM));
        var grassHeight = new SpinBox { MinValue = 1, MaxValue = 40, Step = 1, Value = State?.PathGrassHeight ?? 1 };
        grassHeight.CustomMinimumSize = new Vector2(46, 0);
        grassHeight.TooltipText = "Alto del sello de pasto en tiles";
        grassHeight.ValueChanged += value => { if (State != null) State.PathGrassHeight = (int)value; };
        grassSizeRow.AddChild(grassHeight);
        AddChild(grassSizeRow);

        var commerceRow = new HBoxContainer();
        commerceRow.AddThemeConstantOverride("separation", 4);
        var captureCommerce = new Button { Text = "Guardar comercio", SizeFlagsHorizontal = SizeFlags.ExpandFill };
        captureCommerce.AddThemeFontSizeOverride("font_size", EditorTheme.FONT_SM);
        captureCommerce.TooltipText = "Guarda la selección actual como plantilla con todas sus capas";
        captureCommerce.Pressed += () => EmitSignal(SignalName.CommerceCaptureRequested);
        commerceRow.AddChild(captureCommerce);
        var stampCommerce = new Button { Text = "Comercio", SizeFlagsHorizontal = SizeFlags.ExpandFill };
        stampCommerce.AddThemeFontSizeOverride("font_size", EditorTheme.FONT_SM);
        stampCommerce.TooltipText = "Coloca la plantilla de comercio guardada";
        stampCommerce.Pressed += () => EmitSignal(SignalName.CommerceStampRequested);
        commerceRow.AddChild(stampCommerce);
        AddChild(commerceRow);

        var commerceContentRow = new HBoxContainer();
        commerceContentRow.AddThemeConstantOverride("separation", 4);
        commerceContentRow.AddChild(EditorTheme.MakeLabel("Incluye:", EditorTheme.TEXT_SECONDARY, EditorTheme.FONT_SM));
        var includeL3 = new CheckBox { Text = "L3", ButtonPressed = State?.CommerceIncludeLayer3 ?? true };
        includeL3.TooltipText = "Incluye decoración de Layer 3 al guardar el comercio";
        includeL3.Toggled += value => { if (State != null) State.CommerceIncludeLayer3 = value; };
        commerceContentRow.AddChild(includeL3);
        var includeNpcs = new CheckBox { Text = "NPC", ButtonPressed = State?.CommerceIncludeNpcs ?? true };
        includeNpcs.TooltipText = "Incluye NPCs existentes al guardar el comercio";
        includeNpcs.Toggled += value => { if (State != null) State.CommerceIncludeNpcs = value; };
        commerceContentRow.AddChild(includeNpcs);
        var includeObjects = new CheckBox { Text = "Obj.", ButtonPressed = State?.CommerceIncludeObjects ?? true };
        includeObjects.TooltipText = "Incluye objetos de mapa al guardar el comercio";
        includeObjects.Toggled += value => { if (State != null) State.CommerceIncludeObjects = value; };
        commerceContentRow.AddChild(includeObjects);
        AddChild(commerceContentRow);

        // "Usar selección" — appears once Ctrl+Click has picked 2+ textures below.
        // Combines them (in click order, wrapped at Columns per row) into a single
        // multi-tile stamp and hands it to the same Ctrl+V/Enter paste flow.
        _useSelectionButton = new Button
        {
            Text = "Usar selección",
            Visible = false,
            CustomMinimumSize = new Vector2(0, 26),
        };
        _useSelectionButton.AddThemeFontSizeOverride("font_size", EditorTheme.FONT_SM);
        _useSelectionButton.TooltipText = "Ctrl+Click en varias texturas de la grilla para armar un grupo,\nluego tocá acá para combinarlas en un solo pincel (mové y Enter para confirmar)";
        _useSelectionButton.Pressed += BuildMultiStamp;
        AddChild(_useSelectionButton);

        // Category buttons (wrapped flow — always fully visible, no scrollbar)
        _categoryFlow = new FlowContainer();
        _categoryFlow.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        AddChild(_categoryFlow);

        // Preview zoom — lets the mapper trade cell size for how many fit on screen.
        var zoomRow = new HBoxContainer();
        zoomRow.AddThemeConstantOverride("separation", 4);
        zoomRow.AddChild(EditorTheme.MakeLabel("Zoom:", EditorTheme.TEXT_SECONDARY, EditorTheme.FONT_SM));
        var zoomOut = new Button { Text = "-", CustomMinimumSize = new Vector2(26, 22) };
        zoomOut.TooltipText = "Previews más chicos (entran más por fila)";
        zoomOut.AddThemeFontSizeOverride("font_size", EditorTheme.FONT_SM);
        zoomOut.Pressed += () => ChangePreviewSize(-PreviewSizeStep);
        zoomRow.AddChild(zoomOut);
        _zoomLabel = EditorTheme.MakeLabel($"{_previewSize}px", EditorTheme.TEXT_MUTED, EditorTheme.FONT_SM);
        _zoomLabel.HorizontalAlignment = HorizontalAlignment.Center;
        _zoomLabel.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        zoomRow.AddChild(_zoomLabel);
        var zoomIn = new Button { Text = "+", CustomMinimumSize = new Vector2(26, 22) };
        zoomIn.TooltipText = "Previews más grandes (mejor para objetos grandes)";
        zoomIn.AddThemeFontSizeOverride("font_size", EditorTheme.FONT_SM);
        zoomIn.Pressed += () => ChangePreviewSize(PreviewSizeStep);
        zoomRow.AddChild(zoomIn);
        AddChild(zoomRow);

        // Info label — 2 lines, clipped, no width expansion
        _infoLabel = EditorTheme.MakeLabel("Selecciona una textura", EditorTheme.TEXT_MUTED, EditorTheme.FONT_SM);
        _infoLabel.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _infoLabel.CustomMinimumSize = new Vector2(0, 28);
        _infoLabel.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        _infoLabel.ClipText = true;
        AddChild(_infoLabel);

        // Scroll + Grid for texture previews
        _scrollContainer = new ScrollContainer();
        _scrollContainer.SizeFlagsVertical = SizeFlags.ExpandFill;
        _scrollContainer.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        _scrollContainer.CustomMinimumSize = new Vector2(0, 200);
        _scrollContainer.MouseFilter = MouseFilterEnum.Stop;
        _scrollContainer.ClipContents = true;
        // The grid never scrolls sideways; keeping the horizontal bar off stops
        // it from stealing a row of height and nudging the layout.
        _scrollContainer.HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled;
        _scrollContainer.VerticalScrollMode = ScrollContainer.ScrollMode.Auto;
        _scrollContainer.Resized += OnScrollResized;
        AddChild(_scrollContainer);

        _grid = new GridContainer();
        _grid.Columns = _columns;
        _grid.AddThemeConstantOverride("h_separation", GridSeparation);
        _grid.AddThemeConstantOverride("v_separation", GridSeparation);
        _grid.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        _grid.MouseFilter = MouseFilterEnum.Stop;
        _scrollContainer.AddChild(_grid);
    }

    /// <summary>
    /// Recomputes the column count from the width actually left over after the
    /// scrollbar, then rebuilds only when it changed.
    /// </summary>
    private void OnScrollResized()
    {
        if (_scrollContainer == null) return;

        float usable = _scrollContainer.Size.X - ScrollbarReserve;
        if (usable <= 0) return;
        if (Mathf.IsEqualApprox(usable, _lastGridWidth)) return;
        _lastGridWidth = usable;

        int cols = Mathf.Max(1, (int)((usable + GridSeparation) / (_previewSize + GridSeparation)));
        if (cols == _columns) return;

        _columns = cols;
        if (_grid != null) _grid.Columns = cols;
        PopulateGrid();
    }

    private void ChangePreviewSize(int delta)
    {
        int next = Math.Clamp(_previewSize + delta, PreviewSizeMin, PreviewSizeMax);
        if (next == _previewSize) return;

        _previewSize = next;
        if (_zoomLabel != null) _zoomLabel.Text = $"{_previewSize}px";

        // Composited multi-tile previews are rasterized at the old cell size.
        _previewCache.Clear();

        // Recompute columns for the new cell size directly; OnScrollResized()
        // early-outs on an unchanged width and would skip this.
        if (_scrollContainer != null)
        {
            float usable = _scrollContainer.Size.X - ScrollbarReserve;
            if (usable > 0)
            {
                _columns = Mathf.Max(1, (int)((usable + GridSeparation) / (_previewSize + GridSeparation)));
                if (_grid != null) _grid.Columns = _columns;
            }
        }
        PopulateGrid();
    }

    /// <summary>
    /// Called after data is loaded to populate category buttons.
    /// </summary>
    public void Rebuild()
    {
        if (_categoryFlow == null || Catalog == null) return;

        // Clear old buttons
        foreach (var child in _categoryFlow.GetChildren())
            child.QueueFree();
        _categoryButtons.Clear();
        _previewCache.Clear(); // invalidate cache on full rebuild

        foreach (var cat in Catalog.CategoryOrder)
        {
            var btn = new Button
            {
                Text = cat,
                ToggleMode = true,
                CustomMinimumSize = new Vector2(0, 24),
            };
            btn.AddThemeFontSizeOverride("font_size", 10);
            btn.AddThemeStyleboxOverride("normal",
                EditorTheme.FlatBox(EditorTheme.BG_TOOL_NORMAL, 3, 6, 2));
            btn.AddThemeStyleboxOverride("hover",
                EditorTheme.FlatBox(EditorTheme.BG_TOOL_HOVER, 3, 6, 2));
            btn.AddThemeStyleboxOverride("pressed",
                EditorTheme.FlatBox(EditorTheme.BG_TOOL_ACTIVE, 3, 6, 2, EditorTheme.ACCENT, 1));
            btn.AddThemeColorOverride("font_color", EditorTheme.TEXT_SECONDARY);
            btn.AddThemeColorOverride("font_pressed_color", Colors.White);

            var capturedCat = cat;
            btn.Pressed += () => OnCategorySelected(capturedCat);
            _categoryFlow.AddChild(btn);
            _categoryButtons.Add(btn);
        }

        // "+" button for adding categories
        var addCatBtn = new Button
        {
            Text = "+",
            CustomMinimumSize = new Vector2(24, 24),
        };
        addCatBtn.AddThemeFontSizeOverride("font_size", 10);
        addCatBtn.AddThemeStyleboxOverride("normal",
            EditorTheme.FlatBox(EditorTheme.BG_TOOL_NORMAL, 3, 4, 2));
        addCatBtn.AddThemeStyleboxOverride("hover",
            EditorTheme.FlatBox(EditorTheme.BG_TOOL_HOVER, 3, 4, 2));
        addCatBtn.Pressed += OnAddCategoryPressed;
        _categoryFlow.AddChild(addCatBtn);

        if (Catalog.CategoryOrder.Count > 0)
        {
            _activeCategory = Catalog.CategoryOrder[0];
            SyncCategoryButtons();
            PopulateGrid();
        }

        // Previews need the texture manager, which only exists once data is
        // loaded — so the restored strip is drawn here rather than at build time.
        RefreshRecentGrhs();
    }

    private void OnSearchChanged(string text)
    {
        _searchFilter = text.Trim().ToLowerInvariant();
        PopulateGrid();
    }

    private void OpenRawGrhPicker()
    {
        if (Grhs == null || Textures == null) return;

        var picker = new RawGrhPickerPopup
        {
            Grhs = Grhs,
            Textures = Textures,
        };
        picker.GrhSelected += SelectRawGrh;
        AddChild(picker);
        picker.PopupCentered();
    }

    private void OpenGraphicsSheetPicker()
    {
        if (Grhs == null || Textures == null) return;

        var picker = new GraphicsSheetPickerPopup { Grhs = Grhs, Textures = Textures };
        picker.GrhSelected += SelectRawGrh;
        picker.MainGrhSelected += SelectMainSheetGrh;
        AddChild(picker);
        picker.PopupCentered();
    }

    private void SelectMainSheetGrh(int grhIndex)
    {
        if (State == null || grhIndex <= 0) return;
        State.SelectedTexture = null;
        State.EyedropGrh = grhIndex;
        State.ClearSheetMosaic();
        State.ActiveTool = EditorTool.Paint;
        _infoLabel!.Text = $"GRH {grhIndex} completo listo - elegí L1, L2, L3 o L4 y hacé clic en el mapa";
        UpdateGridHighlights();
    }

    private void SelectRawGrh(int grhIndex)
    {
        if (State == null || grhIndex <= 0) return;

        State.SelectedTexture = null;
        State.EyedropGrh = grhIndex;
        State.ClearSheetMosaic();
        State.ActiveTool = EditorTool.Paint;
        State.PushRecentGrh(grhIndex);
        _infoLabel!.Text = $"GRH libre {grhIndex} — capa activa L{State.ActiveLayer}";
        RefreshRecentGrhs();
        UpdateGridHighlights();
    }

    /// <summary>
    /// Rebuilds the recent-GRH strip. Each button previews the sprite and
    /// re-selects it, so a big piece is one click away instead of a trip
    /// through the browser and its number.
    /// </summary>
    public void RefreshRecentGrhs()
    {
        if (_recentRow == null || _recentSection == null || State == null) return;

        foreach (var child in _recentRow.GetChildren())
            child.QueueFree();

        _recentSection.Visible = State.RecentGrhs.Count > 0;
        if (!_recentSection.Visible) return;

        foreach (int grhIndex in State.RecentGrhs)
        {
            var button = new Button
            {
                CustomMinimumSize = new Vector2(30, 30),
                TooltipText = $"GRH {grhIndex}",
                ExpandIcon = true,
                IconAlignment = HorizontalAlignment.Center,
            };
            button.AddThemeFontSizeOverride("font_size", EditorTheme.FONT_XS);

            var preview = GetRecentPreview(grhIndex);
            if (preview != null) button.Icon = preview;
            else button.Text = grhIndex.ToString();

            bool active = State.EyedropGrh == grhIndex;
            button.AddThemeStyleboxOverride("normal", EditorTheme.FlatBox(
                active ? EditorTheme.BG_TOOL_ACTIVE : EditorTheme.BG_TOOL_NORMAL, 4, 1, 1,
                active ? EditorTheme.ACCENT : EditorTheme.BORDER_SUBTLE, 1));
            button.AddThemeStyleboxOverride("hover", EditorTheme.FlatBox(
                EditorTheme.BG_TOOL_HOVER, 4, 1, 1, EditorTheme.ACCENT_DIM, 1));

            int captured = grhIndex;
            button.Pressed += () => SelectRawGrh(captured);
            _recentRow.AddChild(button);
        }
    }

    /// <summary>Atlas preview of a raw GRH, or null when it cannot be resolved.</summary>
    private Texture2D? GetRecentPreview(int grhIndex)
    {
        if (Grhs == null || Textures == null) return null;
        if (grhIndex <= 0 || grhIndex >= Grhs.Length) return null;

        var grh = Grhs[grhIndex];
        if (grh.NumFrames > 1 && grh.Frames is { Length: > 0 })
        {
            int first = grh.Frames[0];
            if (first > 0 && first < Grhs.Length) grh = Grhs[first];
        }
        if (!grh.IsValid || grh.FileNum <= 0) return null;

        var texture = Textures.GetTexture(grh.FileNum);
        if (texture == null) return null;
        if (grh.PixelWidth <= 0 || grh.PixelHeight <= 0) return texture;

        return new AtlasTexture
        {
            Atlas = texture,
            Region = new Rect2(grh.SX, grh.SY, grh.PixelWidth, grh.PixelHeight),
            FilterClip = true,
        };
    }

    private void OnCategorySelected(string category)
    {
        _activeCategory = category;
        _searchFilter = "";
        if (_searchBox != null) _searchBox.Text = "";
        SyncCategoryButtons();
        PopulateGrid();
    }

    private void SyncCategoryButtons()
    {
        if (Catalog == null) return;
        for (int i = 0; i < _categoryButtons.Count && i < Catalog.CategoryOrder.Count; i++)
            _categoryButtons[i].ButtonPressed = Catalog.CategoryOrder[i] == _activeCategory;
    }

    private void PopulateGrid()
    {
        if (_grid == null || Catalog == null || Grhs == null || Textures == null) return;

        // Clear existing children
        foreach (var child in _grid.GetChildren())
            child.QueueFree();
        _gridButtons.Clear();

        // Get texture list: search across ALL categories, or filter current category
        List<TextureRef> refs;
        if (_searchFilter.Length > 0)
        {
            refs = new List<TextureRef>();
            foreach (var texRef in Catalog.AllRefs)
            {
                if (texRef.Name.ToLowerInvariant().Contains(_searchFilter) ||
                    texRef.Category.ToLowerInvariant().Contains(_searchFilter))
                    refs.Add(texRef);
            }
            _infoLabel!.Text = $"Búsqueda: {refs.Count} resultados";
        }
        else
        {
            if (!Catalog.Categories.TryGetValue(_activeCategory, out var catRefs)) return;
            refs = catRefs;
        }

        bool isSearch = _searchFilter.Length > 0;
        int gridIdx = 0;
        foreach (var texRef in refs)
        {
            var btn = new DraggableTextureButton();
            btn.CustomMinimumSize = new Vector2(_previewSize, _previewSize);
            // KeepAspectCentered preserves the piece's real proportions, so a 2x4
            // object reads as tall instead of being squashed into a square.
            btn.StretchMode = TextureButton.StretchModeEnum.KeepAspectCentered;
            btn.IgnoreTextureSize = true;
            int tw = Math.Max(texRef.TileWidth, 1);
            int th = Math.Max(texRef.TileHeight, 1);
            btn.TooltipText = $"{texRef.Name}\nGRH: {texRef.GrhIndex}\n{tw}x{th} tiles";
            btn.TexRef = texRef;
            btn.GridIndex = gridIdx;
            btn.SearchMode = isSearch;

            // Get cached preview (or generate + cache)
            var preview = GetOrCreatePreview(texRef);
            if (preview != null)
                btn.TextureNormal = preview;

            // Highlight selected (multi-selection cyan takes priority over single-select yellow)
            btn.Modulate = _multiSelected.Contains(texRef)
                ? new Color(0.55f, 0.9f, 1f, 1)
                : (State?.SelectedTexture == texRef)
                    ? new Color(1, 1, 0.5f, 1)
                    : Colors.White;

            var capturedRef = texRef;
            btn.Pressed += () => OnTextureSelected(capturedRef);
            btn.Reordered += OnTextureReordered;
            btn.ContextMenuRequested += OnContextMenuRequested;

            // Multi-tile pieces carry a corner badge with their footprint. Knowing
            // a piece is 3x5 before placing it is the difference between one click
            // and undoing a misplaced building.
            if (tw > 1 || th > 1)
            {
                var badge = EditorTheme.MakeLabel($"{tw}x{th}", Colors.White, EditorTheme.FONT_XS);
                badge.AddThemeStyleboxOverride("normal",
                    EditorTheme.FlatBox(new Color(0, 0, 0, 0.72f), 2, 3, 0));
                badge.MouseFilter = MouseFilterEnum.Ignore;
                badge.SetAnchorsPreset(LayoutPreset.BottomRight);
                badge.GrowHorizontal = GrowDirection.Begin;
                badge.GrowVertical = GrowDirection.Begin;
                btn.AddChild(badge);
            }

            _grid.AddChild(btn);
            _gridButtons.Add((btn, texRef));
            gridIdx++;
        }
    }

    /// <summary>
    /// Sync the layer dropdown if layer was changed externally (keyboard 1-4).
    /// </summary>
    public void SyncLayerUI()
    {
        // Show eyedrop GRH info when no catalog texture is selected
        if (_infoLabel != null && State != null && State.SelectedTexture == null && State.EyedropGrh > 0)
            _infoLabel.Text = $"Eyedrop GRH {State.EyedropGrh}";
    }

    private void OnTextureSelected(TextureRef texRef)
    {
        if (State == null) return;

        // Ctrl+Click: toggle membership in the multi-selection instead of the normal
        // single-texture paint selection.
        if (Input.IsKeyPressed(Key.Ctrl))
        {
            ToggleMultiSelected(texRef);
            return;
        }

        // Plain click while a multi-selection is pending clears it (avoids confusing
        // half-built state) and falls through to the normal single-texture select.
        if (_multiSelected.Count > 0)
        {
            _multiSelected.Clear();
            SyncUseSelectionButton();
        }

        State.SelectedTexture = texRef;
        State.EyedropGrh = 0; // Clear raw eyedrop when selecting from catalog
        State.ClearSheetMosaic(); // and any sheet mosaic armed from the Láminas tab
        State.ActiveTool = EditorTool.Paint;

        // Auto-switch layer based on texture category (e.g. Costas→L2, Techos→L4)
        if (texRef.Layer >= 1 && texRef.Layer <= 4)
        {
            State.ActiveLayer = texRef.Layer;
            EmitSignal(SignalName.LayerChanged, texRef.Layer);
        }

        _infoLabel!.Text = $"{texRef.Name} — GRH {texRef.GrhIndex}\nL{Math.Max(texRef.Layer, 1)} — {Math.Max(texRef.TileWidth, 1)}x{Math.Max(texRef.TileHeight, 1)}";

        // Just update highlights — don't rebuild the entire grid
        UpdateGridHighlights();
    }

    /// <summary>
    /// Update button highlights without rebuilding the grid.
    /// </summary>
    public void UpdateGridHighlights()
    {
        foreach (var (btn, texRef) in _gridButtons)
        {
            int multiIdx = _multiSelected.IndexOf(texRef);
            if (multiIdx >= 0)
            {
                // Cyan tint for Ctrl+Click multi-selection, distinct from the
                // single-selection yellow so both states never look the same.
                btn.Modulate = new Color(0.55f, 0.9f, 1f, 1);
            }
            else
            {
                btn.Modulate = (State?.SelectedTexture == texRef)
                    ? new Color(1, 1, 0.5f, 1)
                    : Colors.White;
            }
        }
    }

    /// <summary>Ctrl+Click handler: add/remove a texture from the multi-selection.</summary>
    private void ToggleMultiSelected(TextureRef texRef)
    {
        if (!_multiSelected.Remove(texRef))
            _multiSelected.Add(texRef);

        SyncUseSelectionButton();
        UpdateGridHighlights();
    }

    private void SyncUseSelectionButton()
    {
        if (_useSelectionButton == null) return;
        _useSelectionButton.Visible = _multiSelected.Count >= 2;
        _useSelectionButton.Text = $"Usar selección ({_multiSelected.Count})";

        if (_infoLabel != null && _multiSelected.Count > 0)
        {
            _infoLabel.Text = _multiSelected.Count == 1
                ? "1 textura elegida — Ctrl+Click en más para combinarlas"
                : $"{_multiSelected.Count} texturas elegidas — tocá \"Usar selección\" para combinarlas";
        }
    }

    /// <summary>
    /// Combines the Ctrl+Click multi-selection (in click order, wrapped every
    /// Columns tiles — same layout as the visible grid) into a single MapTile
    /// buffer written straight into State.Clipboard, then asks the map editor
    /// to paste it via the existing Ctrl+V pending-placement flow (move + Enter
    /// to confirm, Escape to cancel).
    /// </summary>
    private void BuildMultiStamp()
    {
        if (State == null || _multiSelected.Count < 2) return;

        int cols = Math.Min(_columns, _multiSelected.Count);
        int rows = (int)Math.Ceiling(_multiSelected.Count / (double)cols);

        var clip = new MapTile[cols + 1, rows + 1];
        for (int i = 0; i < _multiSelected.Count; i++)
        {
            int cx = i % cols;
            int cy = i / cols;
            var texRef = _multiSelected[i];
            int layer = Math.Clamp(texRef.Layer >= 1 ? texRef.Layer : 1, 1, 4);

            var tile = clip[cx + 1, cy + 1];
            switch (layer)
            {
                case 1: tile.Layer1 = texRef.GrhIndex; break;
                case 2: tile.Layer2 = texRef.GrhIndex; break;
                case 3: tile.Layer3 = texRef.GrhIndex; break;
                case 4: tile.Layer4 = texRef.GrhIndex; break;
            }
            clip[cx + 1, cy + 1] = tile;
        }

        State.Clipboard = clip;
        State.ClipWidth = cols;
        State.ClipHeight = rows;

        _multiSelected.Clear();
        SyncUseSelectionButton();
        UpdateGridHighlights();
        _infoLabel!.Text = $"Grupo de {cols}x{rows} listo — moveté sobre el mapa y Enter para confirmar (Escape cancela)";

        EmitSignal(SignalName.MultiStampReady);
    }

    private void OnTextureReordered(int fromIdx, int toIdx)
    {
        if (Catalog == null || _searchFilter.Length > 0) return;
        if (!Catalog.Categories.TryGetValue(_activeCategory, out var catRefs)) return;
        if (fromIdx < 0 || fromIdx >= catRefs.Count || toIdx < 0 || toIdx >= catRefs.Count) return;

        // Snapshot BEFORE the move for rollback
        var snapshot = new List<TextureRef>(catRefs);

        // Perform the reorder
        var item = catRefs[fromIdx];
        catRefs.RemoveAt(fromIdx);
        catRefs.Insert(toIdx, item);
        PopulateGrid();

        // Show confirmation dialog
        var dialog = new ConfirmationDialog
        {
            DialogText = "¿Deseas guardar este ordenamiento?",
            Size = new Vector2I(300, 100),
            OkButtonText = "Guardar",
            CancelButtonText = "Cancelar",
        };
        dialog.Confirmed += () =>
        {
            Catalog.RebuildAllRefsFromCategories();
            if (IndicesPath.Length > 0)
                Catalog.SaveToFile(IndicesPath);
        };
        dialog.Canceled += () =>
        {
            // Restore original order
            catRefs.Clear();
            catRefs.AddRange(snapshot);
            PopulateGrid();
        };
        AddChild(dialog);
        dialog.PopupCentered();
    }

    private void OnContextMenuRequested(int gridIdx)
    {
        if (gridIdx < 0 || gridIdx >= _gridButtons.Count) return;
        var (_, texRef) = _gridButtons[gridIdx];

        var popup = new TextureEditPopup
        {
            TargetRef = texRef,
            Catalog = Catalog,
            IndicesPath = IndicesPath,
        };
        popup.Saved += () =>
        {
            _previewCache.Clear();
            Rebuild();
        };
        AddChild(popup);
        popup.PopupCentered();
    }

    private void OnAddCategoryPressed()
    {
        if (Catalog == null) return;
        var dialog = new AcceptDialog { Title = "Nueva Categoría", Size = new Vector2I(260, 120) };
        var edit = new LineEdit { PlaceholderText = "Nombre de categoría" };
        edit.CustomMinimumSize = new Vector2(220, 28);
        dialog.AddChild(edit);
        dialog.Confirmed += () =>
        {
            string name = edit.Text.Trim();
            if (name.Length > 0)
            {
                Catalog.AddCategory(name);
                Rebuild();
            }
        };
        AddChild(dialog);
        dialog.PopupCentered();
    }

    /// <summary>
    /// Pre-generate preview textures for the categories the user opens first.
    /// Returns an enumerator for time-budgeted incremental processing.
    ///
    /// Only a bounded slice is prepared, not the whole catalogue: with ~48000
    /// references, generating every thumbnail up front cost minutes of startup
    /// and held them all in memory, for categories that mostly never get opened.
    /// GetOrCreatePreview caches, so the rest are built the first time a
    /// category is shown.
    /// </summary>
    public IEnumerator<int> PreloadAllPreviews()
    {
        if (Catalog == null || Grhs == null || Textures == null) yield break;

        int done = 0;
        foreach (var texRef in PreloadSlice())
        {
            GetOrCreatePreview(texRef);
            done++;
            yield return done;
        }
    }

    /// <summary>
    /// Thumbnails generated at startup: the first screenful of the first few
    /// categories. Both limits matter — there are ~630 categories, so a
    /// per-category cap alone still decodes most of the catalogue. Everything
    /// else is built the first time its category is opened.
    /// </summary>
    private const int PreloadPerCategory = 24;
    private const int PreloadCategories = 8;

    private IEnumerable<TextureRef> PreloadSlice()
    {
        if (Catalog == null) yield break;
        int categories = 0;
        foreach (var category in Catalog.CategoryOrder)
        {
            if (!Catalog.Categories.TryGetValue(category, out var refs)) continue;
            if (++categories > PreloadCategories) yield break;
            for (int i = 0; i < refs.Count && i < PreloadPerCategory; i++)
                yield return refs[i];
        }
    }

    private int _previewPreloadTotal = -1;

    /// <summary>
    /// Size of the startup slice. Cached because the loading bar polls this
    /// every frame and the slice is a lazy walk over every category.
    /// </summary>
    public int PreviewPreloadTotal
    {
        get
        {
            if (_previewPreloadTotal < 0) _previewPreloadTotal = PreloadSlice().Count();
            return _previewPreloadTotal;
        }
    }

    public void Cleanup()
    {
        foreach (var preview in _previewCache.Values)
            SafeDispose(preview);

        _previewCache.Clear();
        _gridButtons.Clear();
        _categoryButtons.Clear();
    }

    /// <summary>
    /// Public accessor for preview textures (used by right sidebar).
    /// </summary>
    public Texture2D? GetPreviewTexture(TextureRef texRef)
    {
        return GetOrCreatePreview(texRef);
    }

    /// <summary>
    /// Get a cached preview or create one. Uses AtlasTexture for single-tile
    /// (zero-copy, GPU-side) and composited+cached for multi-tile.
    /// </summary>
    private Texture2D? GetOrCreatePreview(TextureRef texRef)
    {
        int key = texRef.GrhIndex;
        if (_previewCache.TryGetValue(key, out var cached))
            return cached;

        var preview = GeneratePreview(texRef);
        _previewCache[key] = preview;
        return preview;
    }

    private Texture2D? GeneratePreview(TextureRef texRef)
    {
        if (Grhs == null || Textures == null) return null;
        int grhIndex = texRef.GrhIndex;
        if (grhIndex <= 0 || grhIndex >= Grhs.Length) return null;

        int tw = Math.Max(texRef.TileWidth, 1);
        int th = Math.Max(texRef.TileHeight, 1);

        if (tw == 1 && th == 1)
        {
            // Single tile: use AtlasTexture (GPU-side crop, zero-copy, instant)
            return GenerateSingleGrhPreview(grhIndex);
        }

        // Multi-tile: compose NxM pattern into a single preview image (cached after first call)
        int fullW = tw * 32;
        int fullH = th * 32;
        var composite = Image.CreateEmpty(fullW, fullH, false, Image.Format.Rgba8);

        for (int py = 0; py < th; py++)
            for (int px = 0; px < tw; px++)
            {
                int subGrh = grhIndex + py * tw + px;
                if (subGrh <= 0 || subGrh >= Grhs.Length) continue;

                var grh = Grhs[subGrh];
                if (grh.NumFrames > 1 && grh.Frames != null && grh.Frames.Length > 0)
                {
                    int fIdx = grh.Frames[0];
                    if (fIdx > 0 && fIdx < Grhs.Length) grh = Grhs[fIdx];
                }
                if (grh.FileNum <= 0 || grh.PixelWidth <= 0 || grh.PixelHeight <= 0) continue;

                var srcImg = Textures.GetImageCached(grh.FileNum);
                if (srcImg == null) continue;

                int cropW = Math.Min(grh.PixelWidth, srcImg.GetWidth() - grh.SX);
                int cropH = Math.Min(grh.PixelHeight, srcImg.GetHeight() - grh.SY);
                if (cropW <= 0 || cropH <= 0) continue;

                var tileImg = srcImg.GetRegion(new Rect2I(grh.SX, grh.SY, cropW, cropH));
                composite.BlitRect(tileImg, new Rect2I(0, 0, cropW, cropH),
                    new Vector2I(px * 32, py * 32));
            }

        // Scale to fit the cell along the longer side and keep the real aspect
        // ratio. The button then centers it, so wide and tall pieces stay
        // recognisable instead of every preview looking square.
        float fit = Math.Min(_previewSize / (float)fullW, _previewSize / (float)fullH);
        int outW = Math.Max(1, (int)Math.Round(fullW * fit));
        int outH = Math.Max(1, (int)Math.Round(fullH * fit));
        composite.Resize(outW, outH, Image.Interpolation.Nearest);
        return ImageTexture.CreateFromImage(composite);
    }

    private Texture2D? GenerateSingleGrhPreview(int grhIndex)
    {
        if (Grhs == null || Textures == null) return null;
        if (grhIndex <= 0 || grhIndex >= Grhs.Length) return null;

        var grh = Grhs[grhIndex];
        if (grh.NumFrames <= 0) return null;

        if (grh.NumFrames > 1 && grh.Frames != null && grh.Frames.Length > 0)
        {
            int fIdx = grh.Frames[0];
            if (fIdx > 0 && fIdx < Grhs.Length) grh = Grhs[fIdx];
        }

        if (grh.FileNum <= 0 || grh.PixelWidth <= 0 || grh.PixelHeight <= 0) return null;
        var srcTex = Textures.GetTexture(grh.FileNum);
        if (srcTex == null) return null;

        int cropW = Math.Min(grh.PixelWidth, srcTex.GetWidth() - grh.SX);
        int cropH = Math.Min(grh.PixelHeight, srcTex.GetHeight() - grh.SY);
        if (grh.SX < 0 || grh.SY < 0 || cropW <= 0 || cropH <= 0) return null;

        // Use AtlasTexture: GPU-side crop, NO GetImage() call, instant
        var atlas = new AtlasTexture();
        atlas.Atlas = srcTex;
        atlas.Region = new Rect2(grh.SX, grh.SY, cropW, cropH);
        return atlas;
    }

    private static void SafeDispose(GodotObject? obj)
    {
        if (obj == null) return;
        if (!GodotObject.IsInstanceValid(obj)) return;
        obj.Dispose();
    }
}
