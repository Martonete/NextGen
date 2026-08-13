#nullable enable
using System;
using System.Collections.Generic;
using Godot;
using AOWorldEditor.Data;

namespace AOWorldEditor.Editor;

/// <summary>
/// Browser for the imported graphics sheets. It starts as a paged visual gallery,
/// then lets the mapper select either one indexed region or the whole sheet as a
/// complete, primary graphic from that sheet.
/// </summary>
public partial class GraphicsSheetPickerPopup : Window
{
    public GrhData[]? Grhs;
    public TextureManager? Textures;
    public Action<int>? GrhSelected;
    public Action<int>? MainGrhSelected;

    // Larger cards make the gallery useful for visual browsing rather than a
    // list of opaque file numbers. The selected sheet gets the wide right pane.
    private const int SheetsPerPage = 24;
    private readonly Dictionary<int, Texture2D?> _regionCache = new();
    private readonly List<int> _availableFiles = new();
    private readonly List<int> _currentRegions = new();

    private LineEdit? _fileInput;
    private LineEdit? _galleryFilter;
    private TextureRect? _sheetPreview;
    private Label? _status;
    private Label? _galleryPageLabel;
    private FlowContainer? _gallery;
    private GridContainer? _regions;
    private CheckBox? _singleTileOnly;
    private Button? _stampButton;
    private Button? _firstPageButton;
    private Button? _previousPageButton;
    private Button? _nextPageButton;
    private Button? _lastPageButton;
    private SpinBox? _pageInput;
    private int _galleryPage;
    private int _selectedFileNumber;

    public override void _Ready()
    {
        Title = "Graficos para mapear";
        Size = new Vector2I(1220, 860);
        MinSize = new Vector2I(900, 620);
        Exclusive = true;
        CloseRequested += QueueFree;

        var root = new VBoxContainer();
        root.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        root.OffsetLeft = 10;
        root.OffsetTop = 10;
        root.OffsetRight = -10;
        root.OffsetBottom = -10;
        root.AddThemeConstantOverride("separation", 6);
        AddChild(root);

        var help = EditorTheme.MakeLabel(
            "Galeria de los PNG nuevos para mapear. Elegi una lamina y usá su grafico principal completo o una region individual.",
            EditorTheme.TEXT_SECONDARY, EditorTheme.FONT_SM);
        help.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        root.AddChild(help);

        var split = new HSplitContainer
        {
            SizeFlagsVertical = Control.SizeFlags.ExpandFill,
        };
        root.AddChild(split);

        var browserPane = new VBoxContainer
        {
            CustomMinimumSize = new Vector2(340, 0),
        };
        browserPane.AddThemeConstantOverride("separation", 6);
        split.AddChild(browserPane);

        var gallerySearch = new HBoxContainer();
        gallerySearch.AddChild(EditorTheme.MakeLabel("Buscar PNG:", EditorTheme.TEXT_SECONDARY, EditorTheme.FONT_SM));
        _galleryFilter = new LineEdit
        {
            PlaceholderText = "Numero, por ejemplo 30326",
            ClearButtonEnabled = true,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
        };
        _galleryFilter.TextChanged += _ => { _galleryPage = 0; PopulateGallery(); };
        _galleryFilter.TextSubmitted += text =>
        {
            if (int.TryParse(text.Trim(), out int fileNumber)) LoadSheet(fileNumber);
        };
        gallerySearch.AddChild(_galleryFilter);
        browserPane.AddChild(gallerySearch);

        var galleryNav = new HBoxContainer();
        _firstPageButton = new Button { Text = "|<", TooltipText = "Primera pagina" };
        _firstPageButton.Pressed += () => GoToPage(0);
        galleryNav.AddChild(_firstPageButton);
        _previousPageButton = new Button { Text = "<", TooltipText = "Pagina anterior" };
        _previousPageButton.Pressed += () => GoToPage(_galleryPage - 1);
        galleryNav.AddChild(_previousPageButton);
        _pageInput = new SpinBox { MinValue = 1, MaxValue = 1, Step = 1, CustomMinimumSize = new Vector2(52, 0) };
        _pageInput.ValueChanged += value => GoToPage((int)value - 1);
        galleryNav.AddChild(_pageInput);
        _galleryPageLabel = EditorTheme.MakeLabel("", EditorTheme.TEXT_MUTED, EditorTheme.FONT_SM);
        _galleryPageLabel.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        _galleryPageLabel.HorizontalAlignment = HorizontalAlignment.Center;
        galleryNav.AddChild(_galleryPageLabel);
        _nextPageButton = new Button { Text = ">", TooltipText = "Pagina siguiente" };
        _nextPageButton.Pressed += () => GoToPage(_galleryPage + 1);
        galleryNav.AddChild(_nextPageButton);
        _lastPageButton = new Button { Text = ">|", TooltipText = "Ultima pagina" };
        _lastPageButton.Pressed += () => GoToPage(GetGalleryPageCount() - 1);
        galleryNav.AddChild(_lastPageButton);
        browserPane.AddChild(galleryNav);

        var galleryScroll = new ScrollContainer
        {
            CustomMinimumSize = new Vector2(0, 400),
            SizeFlagsVertical = Control.SizeFlags.ExpandFill,
        };
        browserPane.AddChild(galleryScroll);
        _gallery = new FlowContainer();
        _gallery.AddThemeConstantOverride("h_separation", 6);
        _gallery.AddThemeConstantOverride("v_separation", 6);
        galleryScroll.AddChild(_gallery);

        var detailPane = new VBoxContainer
        {
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
        };
        detailPane.AddThemeConstantOverride("separation", 7);
        split.AddChild(detailPane);

        var selectedRow = new HBoxContainer();
        _fileInput = new LineEdit
        {
            PlaceholderText = "Numero de PNG",
            ClearButtonEnabled = true,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
        };
        _fileInput.TextSubmitted += text => { if (int.TryParse(text.Trim(), out int fileNumber)) LoadSheet(fileNumber); };
        selectedRow.AddChild(_fileInput);
        var open = EditorTheme.PrimaryButton("Abrir lamina");
        open.Pressed += () => { if (_fileInput != null && int.TryParse(_fileInput.Text.Trim(), out int fileNumber)) LoadSheet(fileNumber); };
        selectedRow.AddChild(open);
        _stampButton = new Button { Text = "Usar grafico principal", Disabled = true };
        _stampButton.TooltipText = "Selecciona la pieza más grande de la lámina. Se colocará en la capa que elijas en el editor.";
        _stampButton.Pressed += SelectMainGraphic;
        selectedRow.AddChild(_stampButton);
        detailPane.AddChild(selectedRow);

        _status = EditorTheme.MakeLabel("Elegi una vista previa de la galeria.", EditorTheme.TEXT_MUTED, EditorTheme.FONT_SM);
        detailPane.AddChild(_status);

        var previewScroll = new ScrollContainer
        {
            CustomMinimumSize = new Vector2(700, 560),
            SizeFlagsVertical = Control.SizeFlags.ExpandFill,
        };
        detailPane.AddChild(previewScroll);
        _sheetPreview = new TextureRect
        {
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
            CustomMinimumSize = new Vector2(1500, 1100),
            MouseFilter = Control.MouseFilterEnum.Stop,
        };
        _sheetPreview.GuiInput += OnSheetPreviewInput;
        previewScroll.AddChild(_sheetPreview);

        var regionsHeader = new HBoxContainer();
        regionsHeader.AddChild(EditorTheme.MakeLabel("Regiones: clic en la lámina o elegí una pieza", EditorTheme.TEXT_SECONDARY, EditorTheme.FONT_SM));
        _singleTileOnly = new CheckBox { Text = "Sólo tiles 32×32", ButtonPressed = true };
        _singleTileOnly.TooltipText = "Ideal para caminos y terreno: oculta objetos grandes y deja sólo piezas de un tile.";
        _singleTileOnly.Toggled += _ => PopulateRegions();
        regionsHeader.AddChild(_singleTileOnly);
        detailPane.AddChild(regionsHeader);
        var regionScroll = new ScrollContainer { CustomMinimumSize = new Vector2(0, 210) };
        detailPane.AddChild(regionScroll);
        _regions = new GridContainer { Columns = 10 };
        _regions.AddThemeConstantOverride("h_separation", 5);
        _regions.AddThemeConstantOverride("v_separation", 5);
        _regions.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        regionScroll.AddChild(_regions);

        BuildAvailableFiles();
        PopulateGallery();
    }

    private void BuildAvailableFiles()
    {
        if (Grhs == null) return;
        var found = new HashSet<int>();
        for (int id = 1; id < Grhs.Length; id++)
        {
            var grh = Grhs[id];
            // The restored mapping library uses this separate FileNum range.
            if (grh.IsValid && grh.FileNum >= 100000)
                found.Add(grh.FileNum);
        }
        _availableFiles.AddRange(found);
        _availableFiles.Sort();
    }

    private void PopulateGallery()
    {
        if (_gallery == null || _galleryPageLabel == null || Textures == null) return;
        foreach (var child in _gallery.GetChildren()) child.QueueFree();

        string filter = _galleryFilter?.Text.Trim() ?? "";
        var matches = new List<int>();
        foreach (int fileNumber in _availableFiles)
        {
            int original = fileNumber - 100000;
            if (filter.Length == 0 || original.ToString().Contains(filter, StringComparison.Ordinal))
                matches.Add(fileNumber);
        }

        int pageCount = Math.Max(1, (matches.Count + SheetsPerPage - 1) / SheetsPerPage);
        _galleryPage = Math.Clamp(_galleryPage, 0, pageCount - 1);
        _galleryPageLabel.Text = matches.Count == 0
            ? "No hay laminas que coincidan"
            : $"{matches.Count} laminas\nPagina {_galleryPage + 1}/{pageCount}";
        if (_pageInput != null)
        {
            _pageInput.MaxValue = pageCount;
            _pageInput.SetValueNoSignal(_galleryPage + 1);
        }
        if (_firstPageButton != null) _firstPageButton.Disabled = _galleryPage == 0;
        if (_previousPageButton != null) _previousPageButton.Disabled = _galleryPage == 0;
        if (_nextPageButton != null) _nextPageButton.Disabled = _galleryPage >= pageCount - 1;
        if (_lastPageButton != null) _lastPageButton.Disabled = _galleryPage >= pageCount - 1;

        int first = _galleryPage * SheetsPerPage;
        int last = Math.Min(first + SheetsPerPage, matches.Count);
        for (int i = first; i < last; i++)
        {
            int fileNumber = matches[i];
            _gallery.AddChild(CreateSheetButton(fileNumber));
        }
    }

    private Control CreateSheetButton(int fileNumber)
    {
        int originalFileNumber = fileNumber >= 100000 ? fileNumber - 100000 : fileNumber;
        var box = new VBoxContainer { CustomMinimumSize = new Vector2(156, 140) };
        var button = new TextureButton
        {
            CustomMinimumSize = new Vector2(154, 116),
            IgnoreTextureSize = true,
            StretchMode = TextureButton.StretchModeEnum.KeepAspectCentered,
            TooltipText = $"{originalFileNumber}.png\nAbrir lamina",
        };
        button.TextureNormal = Textures?.GetTexture(fileNumber);
        button.Pressed += () => LoadSheet(originalFileNumber);
        box.AddChild(button);
        var label = EditorTheme.MakeLabel($"{originalFileNumber}.png", EditorTheme.TEXT_MUTED, EditorTheme.FONT_SM);
        label.HorizontalAlignment = HorizontalAlignment.Center;
        box.AddChild(label);
        return box;
    }

    private int GetGalleryPageCount()
    {
        string filter = _galleryFilter?.Text.Trim() ?? "";
        int count = 0;
        foreach (int fileNumber in _availableFiles)
        {
            int original = fileNumber - 100000;
            if (filter.Length == 0 || original.ToString().Contains(filter, StringComparison.Ordinal)) count++;
        }
        return Math.Max(1, (count + SheetsPerPage - 1) / SheetsPerPage);
    }

    private void GoToPage(int page)
    {
        int clamped = Math.Clamp(page, 0, GetGalleryPageCount() - 1);
        if (_galleryPage == clamped && _gallery != null && _gallery.GetChildCount() > 0) return;
        _galleryPage = clamped;
        PopulateGallery();
    }

    private void LoadSheet(int originalFileNumber)
    {
        if (Grhs == null || Textures == null || _regions == null || _sheetPreview == null || _status == null) return;
        if (originalFileNumber <= 0)
        {
            _status.Text = "Ingresa un numero de archivo valido.";
            return;
        }

        // Imported sheets take precedence: a local FileNum with the same number
        // can be an unrelated asset from the previous catalog.
        int importedFileNumber = originalFileNumber + 100000;
        int fileNumber = Textures.GetTexture(importedFileNumber) != null ? importedFileNumber : originalFileNumber;
        var sheet = Textures.GetTexture(fileNumber);
        if (sheet == null)
        {
            _status.Text = $"No se encontro {originalFileNumber}.png entre los graficos activos.";
            return;
        }

        _selectedFileNumber = fileNumber;
        if (_fileInput != null) _fileInput.Text = originalFileNumber.ToString();
        _sheetPreview.Texture = sheet;
        _sheetPreview.CustomMinimumSize = new Vector2(Math.Min(sheet.GetWidth(), 1600), Math.Min(sheet.GetHeight(), 900));
        _currentRegions.Clear();

        for (int id = 1; id < Grhs.Length; id++)
        {
            if (!Grhs[id].IsValid) continue;
            var grh = ResolveFirstFrame(Grhs[id]);
            if (grh.FileNum == fileNumber) _currentRegions.Add(id);
        }

        int staticCount = 0;
        foreach (int id in _currentRegions)
            if (Grhs[id].NumFrames <= 1) staticCount++;
        PopulateRegions();
        int mainGrh = FindMainGraphic();
        _stampButton!.Disabled = mainGrh == 0;
        _status.Text = mainGrh == 0
            ? $"{originalFileNumber}.png - {_currentRegions.Count} regiones."
            : $"{originalFileNumber}.png - {_currentRegions.Count} regiones. Gráfico principal: GRH {mainGrh}.";
    }

    private void PopulateRegions()
    {
        if (_regions == null || Grhs == null) return;
        foreach (var child in _regions.GetChildren()) child.QueueFree();

        bool onlySingleTile = _singleTileOnly?.ButtonPressed ?? false;
        int shown = 0;
        foreach (int id in _currentRegions)
        {
            var grh = Grhs[id];
            if (onlySingleTile && (grh.NumFrames > 1 || grh.PixelWidth != 32 || grh.PixelHeight != 32))
                continue;
            _regions.AddChild(CreateRegionButton(id));
            shown++;
        }
        if (shown == 0 && onlySingleTile)
        {
            var notice = EditorTheme.MakeLabel("Esta lámina no tiene tiles 32×32. Desmarcá el filtro para ver todas las piezas.", EditorTheme.TEXT_MUTED, EditorTheme.FONT_SM);
            notice.CustomMinimumSize = new Vector2(620, 36);
            _regions.AddChild(notice);
        }
    }

    private int FindMainGraphic()
    {
        if (Grhs == null || _selectedFileNumber <= 0) return 0;
        int bestId = 0;
        int bestArea = 0;
        foreach (int id in _currentRegions)
        {
            var grh = Grhs[id];
            if (grh.NumFrames > 1 || grh.FileNum != _selectedFileNumber) continue;
            int area = grh.PixelWidth * grh.PixelHeight;
            if (area > bestArea)
            {
                bestArea = area;
                bestId = id;
            }
        }
        return bestId;
    }

    private void SelectMainGraphic()
    {
        int id = FindMainGraphic();
        if (id <= 0) return;
        MainGrhSelected?.Invoke(id);
        QueueFree();
    }

    private TextureButton CreateRegionButton(int id)
    {
        var button = new TextureButton
        {
            CustomMinimumSize = new Vector2(70, 70),
            IgnoreTextureSize = true,
            StretchMode = TextureButton.StretchModeEnum.KeepAspectCentered,
            TooltipText = $"GRH {id}\nUsar esta región para pintar",
        };
        button.TextureNormal = GetRegionPreview(id);
        button.Pressed += () => { GrhSelected?.Invoke(id); QueueFree(); };
        return button;
    }

    private GrhData ResolveFirstFrame(GrhData grh)
    {
        if (Grhs != null && grh.NumFrames > 1 && grh.Frames is { Length: > 0 } && grh.Frames[0] > 0 && grh.Frames[0] < Grhs.Length)
            return Grhs[grh.Frames[0]];
        return grh;
    }

    private Texture2D? GetRegionPreview(int id)
    {
        if (_regionCache.TryGetValue(id, out var cached)) return cached;
        Texture2D? preview = null;
        if (Grhs != null && Textures != null && id > 0 && id < Grhs.Length)
        {
            var grh = ResolveFirstFrame(Grhs[id]);
            var texture = grh.FileNum > 0 ? Textures.GetTexture(grh.FileNum) : null;
            if (texture != null)
            {
                int width = Math.Min(grh.PixelWidth, texture.GetWidth() - grh.SX);
                int height = Math.Min(grh.PixelHeight, texture.GetHeight() - grh.SY);
                if (grh.SX >= 0 && grh.SY >= 0 && width > 0 && height > 0)
                    preview = new AtlasTexture { Atlas = texture, Region = new Rect2(grh.SX, grh.SY, width, height) };
            }
        }
        _regionCache[id] = preview;
        return preview;
    }

    /// <summary>
    /// Lets the mapper pick a tile from its real position in the sheet. This is
    /// especially useful for road sets where neighbouring 32×32 pieces only
    /// differ by a small edge or corner detail.
    /// </summary>
    private void OnSheetPreviewInput(InputEvent @event)
    {
        if (@event is not InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left }
            || _sheetPreview == null || Grhs == null || Textures == null || _selectedFileNumber <= 0)
            return;

        var texture = Textures.GetTexture(_selectedFileNumber);
        if (texture == null) return;

        Vector2 controlSize = _sheetPreview.Size;
        if (controlSize.X <= 0 || controlSize.Y <= 0) return;
        float scale = Math.Min(controlSize.X / texture.GetWidth(), controlSize.Y / texture.GetHeight());
        if (scale <= 0) return;
        Vector2 renderedSize = new(texture.GetWidth() * scale, texture.GetHeight() * scale);
        Vector2 renderedOrigin = (controlSize - renderedSize) * 0.5f;
        Vector2 sourcePoint = (_sheetPreview.GetLocalMousePosition() - renderedOrigin) / scale;
        if (sourcePoint.X < 0 || sourcePoint.Y < 0 || sourcePoint.X >= texture.GetWidth() || sourcePoint.Y >= texture.GetHeight()) return;

        int selected = 0;
        int bestArea = int.MaxValue;
        foreach (int id in _currentRegions)
        {
            var grh = Grhs[id];
            if (grh.NumFrames > 1 || grh.FileNum != _selectedFileNumber) continue;
            if (sourcePoint.X < grh.SX || sourcePoint.Y < grh.SY
                || sourcePoint.X >= grh.SX + grh.PixelWidth || sourcePoint.Y >= grh.SY + grh.PixelHeight)
                continue;
            int area = grh.PixelWidth * grh.PixelHeight;
            if (area < bestArea)
            {
                bestArea = area;
                selected = id;
            }
        }

        if (selected > 0)
        {
            GrhSelected?.Invoke(selected);
            QueueFree();
        }
    }
}
