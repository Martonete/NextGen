#nullable enable
using System;
using System.Collections.Generic;
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
    [Signal] public delegate void CommerceCaptureRequestedEventHandler();
    [Signal] public delegate void CommerceStampRequestedEventHandler();

    private ScrollContainer? _scrollContainer;
    private GridContainer? _grid;
    private Label? _infoLabel;
    private FlowContainer? _categoryFlow;
    private LineEdit? _searchBox;
    private Button? _useSelectionButton;

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
    private const int PreviewSize = 64; // px per preview cell
    private const int Columns = 4;

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
        AddChild(_scrollContainer);

        _grid = new GridContainer();
        _grid.Columns = Columns;
        _grid.AddThemeConstantOverride("h_separation", 4);
        _grid.AddThemeConstantOverride("v_separation", 4);
        _grid.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        _grid.MouseFilter = MouseFilterEnum.Stop;
        _scrollContainer.AddChild(_grid);
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
    }

    private void OnSearchChanged(string text)
    {
        _searchFilter = text.Trim().ToLowerInvariant();
        PopulateGrid();
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
            btn.CustomMinimumSize = new Vector2(PreviewSize, PreviewSize);
            btn.StretchMode = TextureButton.StretchModeEnum.KeepAspectCentered;
            btn.IgnoreTextureSize = true;
            btn.TooltipText = $"{texRef.Name}\nGRH: {texRef.GrhIndex}\n{texRef.TileWidth}x{texRef.TileHeight}";
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

        int cols = Math.Min(Columns, _multiSelected.Count);
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
    /// Pre-generate all preview textures. Call after texture preload finishes.
    /// Returns an enumerator for time-budgeted incremental processing.
    /// </summary>
    public IEnumerator<int> PreloadAllPreviews()
    {
        if (Catalog == null || Grhs == null || Textures == null) yield break;

        int done = 0;
        foreach (var texRef in Catalog.AllRefs)
        {
            GetOrCreatePreview(texRef);
            done++;
            yield return done;
        }
    }

    public int PreviewPreloadTotal => Catalog?.AllRefs.Count ?? 0;

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

        // Scale down to preview size (maintain aspect ratio)
        composite.Resize(PreviewSize, PreviewSize, Image.Interpolation.Nearest);
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
