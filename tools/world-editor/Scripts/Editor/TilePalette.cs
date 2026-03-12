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

    private ScrollContainer? _scrollContainer;
    private GridContainer? _grid;
    private Label? _infoLabel;
    private FlowContainer? _categoryFlow;
    private LineEdit? _searchBox;

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
        CustomMinimumSize = new Vector2(300, 0);
        AddThemeConstantOverride("separation", 3);

        // Search box
        _searchBox = new LineEdit();
        _searchBox.PlaceholderText = "Buscar textura...";
        _searchBox.ClearButtonEnabled = true;
        _searchBox.CustomMinimumSize = new Vector2(290, 28);
        _searchBox.AddThemeFontSizeOverride("font_size", 11);
        _searchBox.TextChanged += OnSearchChanged;
        AddChild(_searchBox);

        // Category buttons (wrapped flow — always fully visible, no scrollbar)
        _categoryFlow = new FlowContainer();
        _categoryFlow.CustomMinimumSize = new Vector2(290, 0);
        _categoryFlow.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        AddChild(_categoryFlow);

        // Info label
        _infoLabel = EditorTheme.MakeLabel("Selecciona una textura", EditorTheme.TEXT_MUTED, EditorTheme.FONT_SM);
        _infoLabel.CustomMinimumSize = new Vector2(0, 18);
        AddChild(_infoLabel);

        // Scroll + Grid for texture previews
        _scrollContainer = new ScrollContainer();
        _scrollContainer.SizeFlagsVertical = SizeFlags.ExpandFill;
        _scrollContainer.CustomMinimumSize = new Vector2(290, 200);
        AddChild(_scrollContainer);

        _grid = new GridContainer();
        _grid.Columns = Columns;
        _grid.AddThemeConstantOverride("h_separation", 4);
        _grid.AddThemeConstantOverride("v_separation", 4);
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

            // Highlight selected
            btn.Modulate = (State?.SelectedTexture == texRef)
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
        State.SelectedTexture = texRef;
        State.EyedropGrh = 0; // Clear raw eyedrop when selecting from catalog
        State.ActiveTool = EditorTool.Paint;

        // Auto-switch layer based on texture category (e.g. Costas→L2, Techos→L4)
        if (texRef.Layer >= 1 && texRef.Layer <= 4)
        {
            State.ActiveLayer = texRef.Layer;
            EmitSignal(SignalName.LayerChanged, texRef.Layer);
        }

        _infoLabel!.Text = $"{texRef.Name} | GRH {texRef.GrhIndex} | L{Math.Max(texRef.Layer, 1)} | {Math.Max(texRef.TileWidth, 1)}x{Math.Max(texRef.TileHeight, 1)}";

        // Just update highlights — don't rebuild the entire grid
        UpdateGridHighlights();
    }

    /// <summary>
    /// Update button highlights without rebuilding the grid.
    /// </summary>
    private void UpdateGridHighlights()
    {
        foreach (var (btn, texRef) in _gridButtons)
        {
            btn.Modulate = (State?.SelectedTexture == texRef)
                ? new Color(1, 1, 0.5f, 1)
                : Colors.White;
        }
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

                var srcTex = Textures.GetTexture(grh.FileNum);
                if (srcTex == null) continue;
                var srcImg = srcTex.GetImage();
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

        if (grh.FileNum <= 0) return null;
        var srcTex = Textures.GetTexture(grh.FileNum);
        if (srcTex == null) return null;

        // Use AtlasTexture: GPU-side crop, NO GetImage() call, instant
        var atlas = new AtlasTexture();
        atlas.Atlas = srcTex;
        atlas.Region = new Rect2(grh.SX, grh.SY,
            Math.Min(grh.PixelWidth, srcTex.GetWidth() - grh.SX),
            Math.Min(grh.PixelHeight, srcTex.GetHeight() - grh.SY));
        return atlas;
    }
}
