#nullable enable
using System;
using System.Collections.Generic;
using Godot;
using AOWorldEditor.Data;

namespace AOWorldEditor.Editor;

/// <summary>
/// Sidebar panel showing texture references from indices.ini, organized by category.
/// Categories displayed as a wrapped button grid (2 rows) instead of a scrolling TabBar.
/// </summary>
public partial class TilePalette : VBoxContainer
{
    [Signal] public delegate void LayerChangedEventHandler(int layer);

    private ScrollContainer? _scrollContainer;
    private GridContainer? _grid;
    private Label? _infoLabel;
    private FlowContainer? _categoryFlow;

    public TextureCatalog? Catalog;
    public GrhData[]? Grhs;
    public TextureManager? Textures;
    public EditorState? State;

    private string _activeCategory = "";
    private readonly List<Button> _categoryButtons = new();
    private const int PreviewSize = 64; // px per preview cell
    private const int Columns = 4;

    public override void _Ready()
    {
        CustomMinimumSize = new Vector2(300, 0);
        AddThemeConstantOverride("separation", 3);

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

        if (Catalog.CategoryOrder.Count > 0)
        {
            _activeCategory = Catalog.CategoryOrder[0];
            SyncCategoryButtons();
            PopulateGrid();
        }
    }

    private void OnCategorySelected(string category)
    {
        _activeCategory = category;
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

        if (!Catalog.Categories.TryGetValue(_activeCategory, out var refs)) return;

        foreach (var texRef in refs)
        {
            var btn = new TextureButton();
            btn.CustomMinimumSize = new Vector2(PreviewSize, PreviewSize);
            btn.StretchMode = TextureButton.StretchModeEnum.KeepAspectCentered;
            btn.IgnoreTextureSize = true;
            btn.TooltipText = $"{texRef.Name}\nGRH: {texRef.GrhIndex}\n{texRef.TileWidth}x{texRef.TileHeight}";

            // Generate preview texture
            var preview = GeneratePreview(texRef);
            if (preview != null)
                btn.TextureNormal = preview;

            // Highlight selected
            btn.Modulate = (State?.SelectedTexture == texRef)
                ? new Color(1, 1, 0.5f, 1)
                : Colors.White;

            var capturedRef = texRef;
            btn.Pressed += () => OnTextureSelected(capturedRef);

            _grid.AddChild(btn);
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

        PopulateGrid();
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
            // Single tile: crop from source texture (original behavior)
            return GenerateSingleGrhPreview(grhIndex);
        }

        // Multi-tile: compose NxM pattern into a single preview image
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

        var srcImg = srcTex.GetImage();
        if (srcImg == null) return null;

        int cropW = Math.Min(grh.PixelWidth, srcImg.GetWidth() - grh.SX);
        int cropH = Math.Min(grh.PixelHeight, srcImg.GetHeight() - grh.SY);
        if (cropW <= 0 || cropH <= 0) return null;

        var preview = srcImg.GetRegion(new Rect2I(grh.SX, grh.SY, cropW, cropH));
        if (preview.GetWidth() > PreviewSize || preview.GetHeight() > PreviewSize)
            preview.Resize(PreviewSize, PreviewSize, Image.Interpolation.Nearest);

        return ImageTexture.CreateFromImage(preview);
    }
}
