#nullable enable
using System;
using System.Collections.Generic;
using Godot;
using AOWorldEditor.Data;

namespace AOWorldEditor.Editor;

/// <summary>
/// Sidebar panel showing texture references from indices.ini, organized by category tabs.
/// Renders GRH previews in a scrollable grid.
/// </summary>
public partial class TilePalette : VBoxContainer
{
    private OptionButton? _layerSelect;
    private TabBar? _tabBar;
    private ScrollContainer? _scrollContainer;
    private GridContainer? _grid;
    private Label? _infoLabel;

    public TextureCatalog? Catalog;
    public GrhData[]? Grhs;
    public TextureManager? Textures;
    public EditorState? State;

    private string _activeCategory = "";
    private const int PreviewSize = 64; // px per preview cell
    private const int Columns = 4;

    public override void _Ready()
    {
        CustomMinimumSize = new Vector2(300, 0);

        // Layer selector (syncs with toolbar layer tabs)
        var layerBox = new HBoxContainer();
        layerBox.AddChild(EditorTheme.MakeLabel("Capa:", EditorTheme.TEXT_SECONDARY, EditorTheme.FONT_SM));

        _layerSelect = new OptionButton();
        _layerSelect.AddThemeFontSizeOverride("font_size", EditorTheme.FONT_SM);
        _layerSelect.AddItem("1 - Terreno", 0);
        _layerSelect.AddItem("2 - Mascara", 1);
        _layerSelect.AddItem("3 - Objetos/Arboles", 2);
        _layerSelect.AddItem("4 - Techos", 3);
        _layerSelect.Selected = 0;
        _layerSelect.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        _layerSelect.ItemSelected += OnLayerSelected;
        layerBox.AddChild(_layerSelect);
        AddChild(layerBox);

        // Category tab bar
        _tabBar = new TabBar();
        _tabBar.TabChanged += OnTabChanged;
        AddChild(_tabBar);

        // Info label
        _infoLabel = EditorTheme.MakeLabel("Selecciona una textura", EditorTheme.TEXT_MUTED, EditorTheme.FONT_SM);
        AddChild(_infoLabel);

        // Scroll + Grid for texture previews
        _scrollContainer = new ScrollContainer();
        _scrollContainer.SizeFlagsVertical = SizeFlags.ExpandFill;
        _scrollContainer.CustomMinimumSize = new Vector2(290, 400);
        AddChild(_scrollContainer);

        _grid = new GridContainer();
        _grid.Columns = Columns;
        _grid.AddThemeConstantOverride("h_separation", 4);
        _grid.AddThemeConstantOverride("v_separation", 4);
        _scrollContainer.AddChild(_grid);
    }

    /// <summary>
    /// Called after data is loaded to populate tabs.
    /// </summary>
    public void Rebuild()
    {
        if (_tabBar == null || Catalog == null) return;

        _tabBar.ClearTabs();
        foreach (var cat in Catalog.CategoryOrder)
            _tabBar.AddTab(cat);

        if (Catalog.CategoryOrder.Count > 0)
        {
            _activeCategory = Catalog.CategoryOrder[0];
            PopulateGrid();
        }
    }

    private void OnTabChanged(long tabIndex)
    {
        if (Catalog == null) return;
        if (tabIndex < 0 || tabIndex >= Catalog.CategoryOrder.Count) return;
        _activeCategory = Catalog.CategoryOrder[(int)tabIndex];
        PopulateGrid();
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

    private void OnLayerSelected(long index)
    {
        if (State != null)
            State.ActiveLayer = (int)index + 1;
    }

    /// <summary>
    /// Sync the dropdown if layer was changed externally (keyboard 1-4).
    /// </summary>
    public void SyncLayerUI()
    {
        if (_layerSelect != null && State != null)
        {
            int idx = State.ActiveLayer - 1;
            if (idx >= 0 && idx < 4 && _layerSelect.Selected != idx)
                _layerSelect.Selected = idx;
        }

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
        _infoLabel!.Text = $"{texRef.Name} | GRH {texRef.GrhIndex} | {Math.Max(texRef.TileWidth, 1)}x{Math.Max(texRef.TileHeight, 1)}";

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
