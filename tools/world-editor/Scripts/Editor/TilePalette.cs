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

        // Category tab bar
        _tabBar = new TabBar();
        _tabBar.TabChanged += OnTabChanged;
        AddChild(_tabBar);

        // Info label
        _infoLabel = new Label();
        _infoLabel.Text = "Selecciona una textura";
        _infoLabel.AddThemeFontSizeOverride("font_size", 11);
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

    private void OnTextureSelected(TextureRef texRef)
    {
        if (State == null) return;
        State.SelectedTexture = texRef;
        State.ActiveTool = EditorTool.Paint;
        _infoLabel!.Text = $"{texRef.Name} | GRH {texRef.GrhIndex} | {Math.Max(texRef.TileWidth, 1)}x{Math.Max(texRef.TileHeight, 1)}";

        // Refresh button highlights
        PopulateGrid();
    }

    private Texture2D? GeneratePreview(TextureRef texRef)
    {
        if (Grhs == null || Textures == null) return null;
        int grhIndex = texRef.GrhIndex;
        if (grhIndex <= 0 || grhIndex >= Grhs.Length) return null;

        var grh = Grhs[grhIndex];
        if (grh.NumFrames <= 0) return null;

        // For animations, use first frame
        if (grh.NumFrames > 1 && grh.Frames != null && grh.Frames.Length > 0)
        {
            int fIdx = grh.Frames[0];
            if (fIdx > 0 && fIdx < Grhs.Length)
                grh = Grhs[fIdx];
        }

        if (grh.FileNum <= 0) return null;
        var srcTex = Textures.GetTexture(grh.FileNum);
        if (srcTex == null) return null;

        // Create preview by cropping from source texture
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
