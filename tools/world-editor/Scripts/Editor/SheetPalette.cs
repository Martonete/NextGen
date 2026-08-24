#nullable enable
using System;
using System.Collections.Generic;
using Godot;
using AOWorldEditor.Data;

namespace AOWorldEditor.Editor;

/// <summary>
/// Docked version of the graphics-sheet browser. The popup equivalent is modal
/// and covers the map, which forces the mapper to choose a piece blind and then
/// close the window before painting. Living in the sidebar tab strip means the
/// sheet and the map stay visible together, which is what large multi-tile art
/// needs. Widen the sidebar (drag its right edge) for a bigger sheet view.
/// </summary>
public partial class SheetPalette : VBoxContainer
{
    /// <summary>A single region (one GRH) was picked for painting.</summary>
    [Signal] public delegate void GrhPickedEventHandler(int grhIndex);
    /// <summary>The sheet's largest piece was picked — the whole object.</summary>
    [Signal] public delegate void MainGrhPickedEventHandler(int grhIndex);

    /// <summary>
    /// A whole sheet made of several GRHs is ready as one stamp. Payload holds
    /// [grh, colX, colY] triples; the editor pastes it via the pending flow.
    /// </summary>
    [Signal] public delegate void SheetStampReadyEventHandler(int[] pieces, int cols, int rows);

    /// <summary>A repeating terrain mosaic was captured from the sheet.</summary>
    [Signal] public delegate void SheetMosaicReadyEventHandler(int cols, int rows);

    public GrhData[]? Grhs;
    public TextureManager? Textures;
    public EditorState? State;

    private const int ScrollbarReserve = 14;

    // Gallery card footprint, kept in sync with CreateSheetButton.
    private const int CardWidth = 78;
    private const int CardHeight = 88;
    private const int CardSeparation = 5;

    // Sheets per page is derived from the space actually available: a fixed
    // count left most of a tall sidebar empty and inflated the page count.
    private int _sheetsPerPage = 12;

    /// <summary>
    /// Region buttons built for one sheet. Terrain sheets reach 4096 regions
    /// after full indexing, and a textured node each freezes the editor.
    /// </summary>
    private const int MaxRegionButtons = 400;

    private readonly Dictionary<int, Texture2D?> _regionCache = new();
    private readonly List<int> _availableFiles = new();
    private readonly List<int> _currentRegions = new();

    private LineEdit? _filter;
    private ScrollContainer? _galleryScroll;
    private SpinBox? _pageInput;
    private Vector2 _lastGallerySize = Vector2.Zero;
    private Label? _status;
    private Label? _pageLabel;
    private FlowContainer? _gallery;
    private GridContainer? _regions;
    private TextureRect? _sheetPreview;
    private CheckBox? _singleTileOnly;
    private Button? _mainButton;
    private Button? _backButton;
    private HBoxContainer? _selectionBar;
    private TextureRect? _selectionPreview;
    private Label? _selectionLabel;
    private int _selectedGrh;
    private bool _dragging;
    private Vector2 _dragStart;
    // Live rectangle drawn over the sheet while dragging, so the size of the
    // capture is visible before releasing instead of only after the fact.
    private SheetDragOverlay? _dragOverlay;
    private Vector2 _dragStartLocal;
    private bool _dragMoved;
    /// <summary>Pixels the cursor must travel before a left press counts as a rectangle drag.</summary>
    private const float DragThreshold = 5f;
    private VBoxContainer? _galleryView;
    private VBoxContainer? _detailView;
    private ScrollContainer? _regionScroll;

    private int _page;
    private int _selectedFileNumber;
    private bool _built;
    private float _lastRegionWidth = -1;

    public override void _Ready()
    {
        SizeFlagsHorizontal = SizeFlags.ExpandFill;
        ClipContents = true;
        AddThemeConstantOverride("separation", 4);

        BuildGalleryView();
        BuildDetailView();
        ShowGallery();
    }

    private void BuildGalleryView()
    {
        _galleryView = new VBoxContainer { SizeFlagsVertical = SizeFlags.ExpandFill };
        _galleryView.AddThemeConstantOverride("separation", 4);
        AddChild(_galleryView);

        _filter = new LineEdit
        {
            PlaceholderText = "Buscar lámina por número...",
            ClearButtonEnabled = true,
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
        };
        _filter.CustomMinimumSize = new Vector2(0, 28);
        _filter.AddThemeFontSizeOverride("font_size", EditorTheme.FONT_SM);
        _filter.TextChanged += _ => { _page = 0; PopulateGallery(); };
        _filter.TextSubmitted += text =>
        {
            if (int.TryParse(text.Trim(), out int fileNumber)) LoadSheet(fileNumber);
        };
        _galleryView.AddChild(_filter);

        var nav = new HBoxContainer();
        nav.AddThemeConstantOverride("separation", 3);

        var firstBtn = new Button { Text = "|<", CustomMinimumSize = new Vector2(26, 22) };
        firstBtn.AddThemeFontSizeOverride("font_size", EditorTheme.FONT_SM);
        firstBtn.TooltipText = "Primera página";
        firstBtn.Pressed += () => GoToPage(0);
        nav.AddChild(firstBtn);

        var prev = new Button { Text = "<", CustomMinimumSize = new Vector2(26, 22) };
        prev.AddThemeFontSizeOverride("font_size", EditorTheme.FONT_SM);
        prev.TooltipText = "Página anterior";
        prev.Pressed += () => GoToPage(_page - 1);
        nav.AddChild(prev);

        // Type a page number to jump straight there — scrolling 200+ pages one
        // click at a time is not a realistic way to find a sheet.
        _pageInput = new SpinBox
        {
            MinValue = 1,
            MaxValue = 1,
            Step = 1,
            CustomMinimumSize = new Vector2(74, 22),
        };
        _pageInput.AddThemeFontSizeOverride("font_size", EditorTheme.FONT_SM);
        _pageInput.TooltipText = "Escribí un número de página y Enter para saltar";
        _pageInput.ValueChanged += value => GoToPage((int)value - 1);
        nav.AddChild(_pageInput);

        _pageLabel = EditorTheme.MakeLabel("", EditorTheme.TEXT_MUTED, EditorTheme.FONT_SM);
        _pageLabel.HorizontalAlignment = HorizontalAlignment.Center;
        _pageLabel.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        nav.AddChild(_pageLabel);

        var next = new Button { Text = ">", CustomMinimumSize = new Vector2(26, 22) };
        next.AddThemeFontSizeOverride("font_size", EditorTheme.FONT_SM);
        next.TooltipText = "Página siguiente";
        next.Pressed += () => GoToPage(_page + 1);
        nav.AddChild(next);

        var lastBtn = new Button { Text = ">|", CustomMinimumSize = new Vector2(26, 22) };
        lastBtn.AddThemeFontSizeOverride("font_size", EditorTheme.FONT_SM);
        lastBtn.TooltipText = "Última página";
        lastBtn.Pressed += () => GoToPage(int.MaxValue);
        nav.AddChild(lastBtn);

        _galleryView.AddChild(nav);

        _galleryScroll = new ScrollContainer
        {
            SizeFlagsVertical = SizeFlags.ExpandFill,
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
            ClipContents = true,
        };
        _galleryScroll.Resized += OnGalleryResized;
        var galleryScroll = _galleryScroll;
        _galleryView.AddChild(galleryScroll);

        _gallery = new FlowContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        _gallery.AddThemeConstantOverride("h_separation", 5);
        _gallery.AddThemeConstantOverride("v_separation", 5);
        galleryScroll.AddChild(_gallery);
    }

    private void BuildDetailView()
    {
        _detailView = new VBoxContainer { SizeFlagsVertical = SizeFlags.ExpandFill, Visible = false };
        _detailView.AddThemeConstantOverride("separation", 4);
        AddChild(_detailView);

        var header = new HBoxContainer();
        header.AddThemeConstantOverride("separation", 4);
        _backButton = new Button { Text = "< Láminas", CustomMinimumSize = new Vector2(0, 24) };
        _backButton.AddThemeFontSizeOverride("font_size", EditorTheme.FONT_SM);
        _backButton.Pressed += ShowGallery;
        header.AddChild(_backButton);
        _mainButton = new Button
        {
            Text = "Usar completo",
            Disabled = true,
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            CustomMinimumSize = new Vector2(0, 24),
        };
        _mainButton.AddThemeFontSizeOverride("font_size", EditorTheme.FONT_SM);
        _mainButton.TooltipText = "Toma la pieza más grande de la lámina (el objeto entero) y la deja lista para pintar.";
        _mainButton.Pressed += SelectMainGraphic;
        header.AddChild(_mainButton);
        _detailView.AddChild(header);

        _status = EditorTheme.MakeLabel("", EditorTheme.TEXT_MUTED, EditorTheme.FONT_SM);
        _status.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _detailView.AddChild(_status);

        // Confirmation strip: without it, clicking a region gives no feedback at
        // all until the cursor happens to hover the map.
        _selectionBar = new HBoxContainer { Visible = false };
        _selectionBar.AddThemeConstantOverride("separation", 6);
        _selectionPreview = new TextureRect
        {
            CustomMinimumSize = new Vector2(46, 46),
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
        };
        _selectionBar.AddChild(_selectionPreview);
        _selectionLabel = EditorTheme.MakeLabel("", EditorTheme.TEXT_PRIMARY, EditorTheme.FONT_SM);
        _selectionLabel.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _selectionLabel.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        _selectionLabel.SizeFlagsVertical = SizeFlags.ShrinkCenter;
        _selectionBar.AddChild(_selectionLabel);
        _detailView.AddChild(_selectionBar);

        // Full sheet: click straight on the art to grab the piece under the
        // cursor — the fastest way to pick neighbouring road/edge tiles.
        var previewScroll = new ScrollContainer
        {
            CustomMinimumSize = new Vector2(0, 200),
            SizeFlagsVertical = SizeFlags.ExpandFill,
            ClipContents = true,
        };
        _detailView.AddChild(previewScroll);
        _sheetPreview = new TextureRect
        {
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
            MouseFilter = MouseFilterEnum.Stop,
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            SizeFlagsVertical = SizeFlags.ExpandFill,
        };
        _sheetPreview.GuiInput += OnSheetPreviewInput;
        previewScroll.AddChild(_sheetPreview);

        // Drawn on top of the art; must not eat the drag events it visualises.
        _dragOverlay = new SheetDragOverlay();
        _dragOverlay.SetAnchorsPreset(LayoutPreset.FullRect);
        _dragOverlay.MouseFilter = MouseFilterEnum.Ignore;
        _sheetPreview.AddChild(_dragOverlay);

        var hint = EditorTheme.MakeLabel(
            "Clic: una pieza · Arrastrá para capturar un bloque de terreno y pintarlo repetido",
            EditorTheme.TEXT_MUTED, EditorTheme.FONT_XS);
        hint.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _detailView.AddChild(hint);

        var optionsRow = new HBoxContainer();
        optionsRow.AddThemeConstantOverride("separation", 8);
        _singleTileOnly = new CheckBox { Text = "Sólo 32×32", ButtonPressed = false };
        _singleTileOnly.AddThemeFontSizeOverride("font_size", EditorTheme.FONT_SM);
        _singleTileOnly.TooltipText = "Filtra piezas de un tile (terreno y caminos). Desmarcado muestra también los objetos grandes.";
        _singleTileOnly.Toggled += _ => PopulateRegions();
        optionsRow.AddChild(_singleTileOnly);

        var reserve = new CheckBox { Text = "Bloquear área", ButtonPressed = State?.ReserveBigGrhFootprint ?? true };
        reserve.AddThemeFontSizeOverride("font_size", EditorTheme.FONT_SM);
        reserve.TooltipText = "Al pintar una pieza grande, marca como bloqueados los tiles que ocupa.\nDesactivalo para terreno que debe ser caminable.";
        reserve.Toggled += value => { if (State != null) State.ReserveBigGrhFootprint = value; };
        optionsRow.AddChild(reserve);
        _detailView.AddChild(optionsRow);

        _regionScroll = new ScrollContainer
        {
            CustomMinimumSize = new Vector2(0, 150),
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
            ClipContents = true,
        };
        _regionScroll.Resized += OnRegionScrollResized;
        _detailView.AddChild(_regionScroll);
        _regions = new GridContainer { Columns = 3, SizeFlagsHorizontal = SizeFlags.ExpandFill };
        _regions.AddThemeConstantOverride("h_separation", 4);
        _regions.AddThemeConstantOverride("v_separation", 4);
        _regionScroll.AddChild(_regions);
    }

    /// <summary>Called once the GRH/texture data is available.</summary>
    public void Rebuild()
    {
        if (Grhs == null || _built) return;
        BuildAvailableFiles();
        _built = true;
        RecomputeSheetsPerPage();
        PopulateGallery();
    }

    private void BuildAvailableFiles()
    {
        if (Grhs == null) return;
        var found = new HashSet<int>();
        for (int id = 1; id < Grhs.Length; id++)
        {
            var grh = Grhs[id];
            // Imported mapping sheets live in this dedicated FileNum range.
            if (grh.IsValid && grh.FileNum >= 100000)
                found.Add(grh.FileNum);
        }
        _availableFiles.Clear();
        _availableFiles.AddRange(found);
        _availableFiles.Sort();
    }

    private List<int> GetMatches()
    {
        string filter = _filter?.Text.Trim() ?? "";
        var matches = new List<int>();
        foreach (int fileNumber in _availableFiles)
        {
            // Match against the real sheet number, the one shown on the button.
            if (filter.Length == 0 || fileNumber.ToString().Contains(filter, StringComparison.Ordinal))
                matches.Add(fileNumber);
        }
        return matches;
    }

    /// <summary>
    /// Refits the page to the gallery's real size. Anchors on the first sheet of
    /// the current page so a resize doesn't jump the user somewhere unrelated.
    /// </summary>
    private void OnGalleryResized()
    {
        if (_galleryScroll == null) return;
        var size = _galleryScroll.Size;
        if (size.X <= 0 || size.Y <= 0) return;
        if (size.IsEqualApprox(_lastGallerySize)) return;

        int firstVisible = _page * _sheetsPerPage;
        _lastGallerySize = size;
        RecomputeSheetsPerPage();
        _page = _sheetsPerPage > 0 ? firstVisible / _sheetsPerPage : 0;
        PopulateGallery();
    }

    private void RecomputeSheetsPerPage()
    {
        if (_galleryScroll == null) return;

        float usableW = _galleryScroll.Size.X - ScrollbarReserve;
        float usableH = _galleryScroll.Size.Y;
        if (usableW <= 0 || usableH <= 0) return;

        int cols = Math.Max(1, (int)((usableW + CardSeparation) / (CardWidth + CardSeparation)));
        int rows = Math.Max(1, (int)((usableH + CardSeparation) / (CardHeight + CardSeparation)));
        _sheetsPerPage = Math.Max(1, cols * rows);
    }

    private void PopulateGallery()
    {
        if (_gallery == null || _pageLabel == null || Textures == null) return;
        foreach (var child in _gallery.GetChildren()) child.QueueFree();

        var matches = GetMatches();
        int pageCount = Math.Max(1, (matches.Count + _sheetsPerPage - 1) / _sheetsPerPage);
        _page = Math.Clamp(_page, 0, pageCount - 1);
        _pageLabel.Text = matches.Count == 0
            ? "Sin láminas"
            : $"{matches.Count} láminas";

        // Keep the jump box in range without firing its own ValueChanged.
        if (_pageInput != null)
        {
            _pageInput.MaxValue = pageCount;
            _pageInput.SetValueNoSignal(_page + 1);
            _pageInput.Suffix = $"/{pageCount}";
        }

        int first = _page * _sheetsPerPage;
        int last = Math.Min(first + _sheetsPerPage, matches.Count);
        for (int i = first; i < last; i++)
            _gallery.AddChild(CreateSheetButton(matches[i]));
    }

    private Control CreateSheetButton(int fileNumber)
    {
        // The sheet number is shown and opened as-is. Subtracting 100000 dates
        // from when that range was assumed to mirror the classic catalogue with
        // an offset; they are independent sheets, so the shift both mislabelled
        // them and made LoadSheet open a number that does not exist.
        var box = new VBoxContainer { CustomMinimumSize = new Vector2(78, 88) };
        var button = new TextureButton
        {
            CustomMinimumSize = new Vector2(76, 68),
            IgnoreTextureSize = true,
            StretchMode = TextureButton.StretchModeEnum.KeepAspectCentered,
            TooltipText = $"{fileNumber}.png",
        };
        button.TextureNormal = Textures?.GetTexture(fileNumber);
        button.Pressed += () => LoadSheet(fileNumber);
        box.AddChild(button);
        var label = EditorTheme.MakeLabel($"{fileNumber}", EditorTheme.TEXT_MUTED, EditorTheme.FONT_XS);
        label.HorizontalAlignment = HorizontalAlignment.Center;
        box.AddChild(label);
        return box;
    }

    private void GoToPage(int page)
    {
        var matches = GetMatches();
        int pageCount = Math.Max(1, (matches.Count + _sheetsPerPage - 1) / _sheetsPerPage);
        int clamped = Math.Clamp(page, 0, pageCount - 1);
        if (clamped == _page) return;
        _page = clamped;
        PopulateGallery();
    }

    private void ShowGallery()
    {
        if (_galleryView != null) _galleryView.Visible = true;
        if (_detailView != null) _detailView.Visible = false;
    }

    private void ShowDetail()
    {
        if (_galleryView != null) _galleryView.Visible = false;
        if (_detailView != null) _detailView.Visible = true;
    }

    private void LoadSheet(int fileNumber)
    {
        if (Grhs == null || Textures == null || _regions == null
            || _sheetPreview == null || _status == null) return;
        if (fileNumber <= 0) return;

        // Opened by its real number. This used to add 100000 to undo the shift
        // the gallery applied when labelling the button, so the sheet on screen
        // never matched the number beside it.
        var sheet = Textures.GetTexture(fileNumber);
        if (sheet == null)
        {
            _status.Text = $"No se encontró {fileNumber}.png";
            ShowDetail();
            return;
        }

        _selectedFileNumber = fileNumber;
        _sheetPreview.Texture = sheet;
        _currentRegions.Clear();
        for (int id = 1; id < Grhs.Length; id++)
        {
            if (!Grhs[id].IsValid) continue;
            if (ResolveFirstFrame(Grhs[id]).FileNum == fileNumber) _currentRegions.Add(id);
        }

        PopulateRegions();
        int mainGrh = FindMainGraphic();
        if (_mainButton != null) _mainButton.Disabled = mainGrh == 0;
        _status.Text = mainGrh == 0
            ? $"{fileNumber}.png — {_currentRegions.Count} regiones"
            : $"{fileNumber}.png — {_currentRegions.Count} regiones · principal GRH {mainGrh}";
        ShowDetail();
    }

    /// <summary>
    /// Recolumn only when the width actually changed. Rebuilding the grid
    /// resizes the container, which would re-enter this handler forever.
    /// </summary>
    private void OnRegionScrollResized()
    {
        if (_regionScroll == null) return;
        float usable = _regionScroll.Size.X - ScrollbarReserve;
        if (usable <= 0 || Mathf.IsEqualApprox(usable, _lastRegionWidth)) return;
        _lastRegionWidth = usable;
        PopulateRegions();
    }

    private void PopulateRegions()
    {
        if (_regions == null || Grhs == null || _regionScroll == null) return;
        foreach (var child in _regions.GetChildren()) child.QueueFree();

        // Match the column count to the sidebar's current width so the grid
        // stays aligned when the splitter moves.
        float usable = _regionScroll.Size.X - ScrollbarReserve;
        if (usable > 0)
            _regions.Columns = Math.Max(1, (int)((usable + 4) / (66 + 4)));

        bool onlySingleTile = _singleTileOnly?.ButtonPressed ?? false;
        int shown = 0;
        foreach (int id in _currentRegions)
        {
            var grh = Grhs[id];
            if (onlySingleTile && (grh.NumFrames > 1 || grh.PixelWidth != 32 || grh.PixelHeight != 32))
                continue;
            // Terrain sheets now carry up to 4096 regions each; building a
            // textured node for every one freezes the editor when the sheet is
            // opened. The block-capture drag works off the sheet image itself,
            // not this list, so a capped list loses nothing.
            if (shown >= MaxRegionButtons) break;
            _regions.AddChild(CreateRegionButton(id));
            shown++;
        }
        if (shown == 0 && onlySingleTile)
        {
            var notice = EditorTheme.MakeLabel("Sin tiles 32×32. Desmarcá el filtro.",
                EditorTheme.TEXT_MUTED, EditorTheme.FONT_SM);
            notice.AutowrapMode = TextServer.AutowrapMode.WordSmart;
            _regions.AddChild(notice);
        }
        else if (shown >= MaxRegionButtons)
        {
            var notice = EditorTheme.MakeLabel(
                $"Mostrando {shown} de {_currentRegions.Count} piezas. Usá el arrastre sobre la lámina para el resto.",
                EditorTheme.TEXT_MUTED, EditorTheme.FONT_XS);
            notice.AutowrapMode = TextServer.AutowrapMode.WordSmart;
            _regions.AddChild(notice);
        }
    }

    private TextureButton CreateRegionButton(int id)
    {
        var grh = Grhs![id];
        int tilesW = Math.Max(1, grh.PixelWidth / 32);
        int tilesH = Math.Max(1, grh.PixelHeight / 32);
        var button = new TextureButton
        {
            CustomMinimumSize = new Vector2(66, 66),
            IgnoreTextureSize = true,
            StretchMode = TextureButton.StretchModeEnum.KeepAspectCentered,
            TooltipText = $"GRH {id}\n{grh.PixelWidth}x{grh.PixelHeight}px ({tilesW}x{tilesH} tiles)",
        };
        button.TextureNormal = GetRegionPreview(id);
        button.Pressed += () => SelectRegion(id);
        // Tint the region that is currently armed for painting.
        button.Modulate = id == _selectedGrh ? new Color(1f, 1f, 0.5f) : Colors.White;

        if (tilesW > 1 || tilesH > 1)
        {
            var badge = EditorTheme.MakeLabel($"{tilesW}x{tilesH}", Colors.White, EditorTheme.FONT_XS);
            badge.AddThemeStyleboxOverride("normal",
                EditorTheme.FlatBox(new Color(0, 0, 0, 0.72f), 2, 3, 0));
            badge.MouseFilter = MouseFilterEnum.Ignore;
            badge.SetAnchorsPreset(LayoutPreset.BottomRight);
            badge.GrowHorizontal = GrowDirection.Begin;
            badge.GrowVertical = GrowDirection.Begin;
            button.AddChild(badge);
        }
        return button;
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

    /// <summary>
    /// "Usar completo" must hand over the whole sheet, not its biggest piece.
    /// Art is often cut into several GRHs (a door split into 4 quarters), so the
    /// largest single region is only a fraction of the object. This lays every
    /// static region out at its real position in the sheet and ships the result
    /// as one multi-tile stamp. Falls back to the single-GRH path when the sheet
    /// really is one piece.
    /// </summary>
    private void SelectMainGraphic()
    {
        if (Grhs == null || _selectedFileNumber <= 0) return;

        var pieces = new List<(int Id, int SX, int SY, int W, int H)>();
        foreach (int id in _currentRegions)
        {
            var grh = Grhs[id];
            if (grh.NumFrames > 1 || grh.FileNum != _selectedFileNumber) continue;
            if (grh.PixelWidth <= 0 || grh.PixelHeight <= 0) continue;
            pieces.Add((id, grh.SX, grh.SY, grh.PixelWidth, grh.PixelHeight));
        }

        if (pieces.Count == 0) return;

        if (pieces.Count == 1)
        {
            ShowSelection(pieces[0].Id);
            EmitSignal(SignalName.MainGrhPicked, pieces[0].Id);
            return;
        }

        // Discard pieces fully contained in a bigger one: some sheets ship both
        // the assembled graphic and its parts, which would stamp twice.
        var kept = new List<(int Id, int SX, int SY, int W, int H)>();
        foreach (var p in pieces)
        {
            bool covered = false;
            foreach (var q in pieces)
            {
                if (p.Id == q.Id) continue;
                bool qBigger = q.W * q.H > p.W * p.H;
                if (qBigger && p.SX >= q.SX && p.SY >= q.SY
                    && p.SX + p.W <= q.SX + q.W && p.SY + p.H <= q.SY + q.H)
                {
                    covered = true;
                    break;
                }
            }
            if (!covered) kept.Add(p);
        }
        if (kept.Count == 0) kept = pieces;

        if (kept.Count == 1)
        {
            ShowSelection(kept[0].Id);
            EmitSignal(SignalName.MainGrhPicked, kept[0].Id);
            return;
        }

        int minX = int.MaxValue, minY = int.MaxValue;
        foreach (var p in kept)
        {
            minX = Math.Min(minX, p.SX);
            minY = Math.Min(minY, p.SY);
        }

        // Grid the pieces by their offset from the sheet's top-left corner.
        int cols = 0, rows = 0;
        var placed = new List<(int Id, int Cx, int Cy)>();
        foreach (var p in kept)
        {
            int cx = (p.SX - minX) / 32;
            int cy = (p.SY - minY) / 32;
            placed.Add((p.Id, cx, cy));
            cols = Math.Max(cols, cx + Math.Max(1, (p.W + 31) / 32));
            rows = Math.Max(rows, cy + Math.Max(1, (p.H + 31) / 32));
        }
        if (cols <= 0 || rows <= 0) return;

        EmitSignal(SignalName.SheetStampReady,
            BuildStampPayload(placed, cols, rows), cols, rows);

        if (_selectionLabel != null && _selectionBar != null && _selectionPreview != null)
        {
            _selectionPreview.Texture = GetRegionPreview(kept[0].Id);
            _selectionLabel.Text = $"Lámina completa: {kept.Count} piezas · {cols}x{rows} tiles\nMové sobre el mapa y Enter para confirmar";
            _selectionBar.Visible = true;
        }
    }

    /// <summary>
    /// Flattens the placement into a Godot-marshalable int array:
    /// [grh, cx, cy] per piece.
    /// </summary>
    private static int[] BuildStampPayload(List<(int Id, int Cx, int Cy)> placed, int cols, int rows)
    {
        var payload = new int[placed.Count * 3];
        for (int i = 0; i < placed.Count; i++)
        {
            payload[i * 3] = placed[i].Id;
            payload[i * 3 + 1] = placed[i].Cx;
            payload[i * 3 + 2] = placed[i].Cy;
        }
        return payload;
    }

    private void SelectRegion(int id)
    {
        if (id <= 0) return;
        // Picking one piece replaces any armed mosaic.
        State?.ClearSheetMosaic();
        ShowSelection(id);
        EmitSignal(SignalName.GrhPicked, id);
    }

    /// <summary>
    /// Fills the confirmation strip with the armed piece and re-tints the grid,
    /// so the mapper can see what a click actually selected.
    /// </summary>
    private void ShowSelection(int id)
    {
        _selectedGrh = id;
        if (_selectionBar == null || _selectionPreview == null || _selectionLabel == null) return;

        _selectionPreview.Texture = GetRegionPreview(id);
        if (Grhs != null && id > 0 && id < Grhs.Length)
        {
            var grh = ResolveFirstFrame(Grhs[id]);
            int tilesW = Math.Max(1, (grh.PixelWidth + 31) / 32);
            int tilesH = Math.Max(1, (grh.PixelHeight + 31) / 32);
            _selectionLabel.Text = $"Seleccionado GRH {id}\n{grh.PixelWidth}x{grh.PixelHeight}px · {tilesW}x{tilesH} tiles";
        }
        else
        {
            _selectionLabel.Text = $"Seleccionado GRH {id}";
        }
        _selectionBar.Visible = true;

        // Rebuild so the new selection tint is applied to the grid.
        PopulateRegions();
    }

    private GrhData ResolveFirstFrame(GrhData grh)
    {
        if (Grhs != null && grh.NumFrames > 1 && grh.Frames is { Length: > 0 }
            && grh.Frames[0] > 0 && grh.Frames[0] < Grhs.Length)
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
                    preview = new AtlasTexture
                    {
                        Atlas = texture,
                        Region = new Rect2(grh.SX, grh.SY, width, height),
                    };
            }
        }
        _regionCache[id] = preview;
        return preview;
    }

    /// <summary>
    /// Maps a point inside the preview control to sheet pixel coordinates,
    /// accounting for the KeepAspectCentered letterboxing.
    /// </summary>
    private bool TryGetSheetPoint(Vector2 local, out Vector2 sourcePoint)
    {
        sourcePoint = Vector2.Zero;
        if (_sheetPreview == null || Textures == null || _selectedFileNumber <= 0) return false;
        var texture = Textures.GetTexture(_selectedFileNumber);
        if (texture == null) return false;

        Vector2 controlSize = _sheetPreview.Size;
        if (controlSize.X <= 0 || controlSize.Y <= 0) return false;
        float scale = Math.Min(controlSize.X / texture.GetWidth(), controlSize.Y / texture.GetHeight());
        if (scale <= 0) return false;

        Vector2 renderedSize = new(texture.GetWidth() * scale, texture.GetHeight() * scale);
        Vector2 renderedOrigin = (controlSize - renderedSize) * 0.5f;
        var p = (local - renderedOrigin) / scale;
        if (p.X < 0 || p.Y < 0 || p.X >= texture.GetWidth() || p.Y >= texture.GetHeight()) return false;

        sourcePoint = p;
        return true;
    }

    /// <summary>
    /// Drag over the sheet to lift a rectangle of 32×32 tiles and use it as a
    /// repeating brush. Terrain sheets have no indices.ini pattern, so without
    /// this a multi-tile ground has to be laid one piece at a time.
    ///
    /// Any mouse button starts a drag: left is the natural gesture, right is
    /// kept for muscle memory. A left press that never travels past
    /// <see cref="DragThreshold"/> falls through to the single-piece pick in
    /// <see cref="OnSheetPreviewInput"/>, so clicking still grabs one tile.
    /// </summary>
    private void OnSheetDragInput(InputEvent @event)
    {
        if (Grhs == null || _selectedFileNumber <= 0) return;

        if (@event is InputEventMouseButton mb)
        {
            bool rectButton = mb.ButtonIndex == MouseButton.Right
                || mb.ButtonIndex == MouseButton.Left;
            if (!rectButton) return;

            if (mb.Pressed)
            {
                if (TryGetSheetPoint(mb.Position, out var start))
                {
                    _dragStart = start;
                    _dragStartLocal = mb.Position;
                    _dragging = true;
                    // Right-drag and Shift+left are explicit rectangle gestures,
                    // so they arm the overlay without waiting for the threshold.
                    _dragMoved = mb.ButtonIndex == MouseButton.Right || Input.IsKeyPressed(Key.Shift);
                    if (_dragMoved) UpdateDragOverlay(mb.Position);
                }
            }
            else if (_dragging)
            {
                bool captured = _dragMoved;
                _dragging = false;
                _dragMoved = false;
                _dragOverlay?.Clear();
                if (captured && TryGetSheetPoint(mb.Position, out var end))
                    CaptureSheetRect(_dragStart, end);
            }
        }
        else if (@event is InputEventMouseMotion motion && _dragging)
        {
            if (!_dragMoved
                && motion.Position.DistanceTo(_dragStartLocal) >= DragThreshold)
                _dragMoved = true;
            if (_dragMoved) UpdateDragOverlay(motion.Position);
        }
    }

    /// <summary>
    /// Feeds the overlay the current rectangle in control space, snapped to the
    /// sheet's 32×32 grid so what is highlighted is exactly what gets captured.
    /// </summary>
    private void UpdateDragOverlay(Vector2 localPos)
    {
        if (_dragOverlay == null || _sheetPreview == null || Textures == null) return;
        if (!TryGetSheetPoint(localPos, out var end)) return;
        var texture = Textures.GetTexture(_selectedFileNumber);
        if (texture == null) return;

        Vector2 controlSize = _sheetPreview.Size;
        float scale = Math.Min(controlSize.X / texture.GetWidth(), controlSize.Y / texture.GetHeight());
        if (scale <= 0) return;
        Vector2 renderedSize = new(texture.GetWidth() * scale, texture.GetHeight() * scale);
        Vector2 renderedOrigin = (controlSize - renderedSize) * 0.5f;

        // Must match CaptureSheetRect exactly, or the highlight would promise a
        // different block than the one actually captured.
        float x0 = MathF.Floor(Math.Min(_dragStart.X, end.X) / 32) * 32;
        float y0 = MathF.Floor(Math.Min(_dragStart.Y, end.Y) / 32) * 32;
        float x1 = MathF.Floor(Math.Max(_dragStart.X, end.X) / 32) * 32;
        float y1 = MathF.Floor(Math.Max(_dragStart.Y, end.Y) / 32) * 32;

        int cols = Math.Max(1, (int)(x1 - x0) / 32 + 1);
        int rows = Math.Max(1, (int)(y1 - y0) / 32 + 1);

        _dragOverlay.SetRect(
            new Rect2(renderedOrigin + new Vector2(x0, y0) * scale,
                      new Vector2(cols * 32, rows * 32) * scale),
            cols, rows);
    }

    /// <summary>
    /// Turns a pixel rectangle into a grid of GRHs and arms it as the paint
    /// mosaic. Cells with no matching GRH stay 0 and are skipped when painting.
    /// </summary>
    private void CaptureSheetRect(Vector2 a, Vector2 b)
    {
        if (State == null || Grhs == null) return;

        // Both edges resolve to the cell that physically contains the cursor.
        // Rounding the far edge up instead pulled in a whole extra column/row
        // whenever the drag ended even one pixel past a cell boundary, which is
        // how a neighbouring graphic ended up inside the capture.
        int x0 = (int)Math.Floor(Math.Min(a.X, b.X) / 32) * 32;
        int y0 = (int)Math.Floor(Math.Min(a.Y, b.Y) / 32) * 32;
        int x1 = (int)Math.Floor(Math.Max(a.X, b.X) / 32) * 32;
        int y1 = (int)Math.Floor(Math.Max(a.Y, b.Y) / 32) * 32;

        int cols = Math.Max(1, (x1 - x0) / 32 + 1);
        int rows = Math.Max(1, (y1 - y0) / 32 + 1);
        if (cols * rows <= 1)
        {
            // A click-sized rectangle is just a single pick.
            int single = FindGrhAt(x0 + 1, y0 + 1);
            if (single > 0) SelectRegion(single);
            return;
        }

        var mosaic = new int[cols * rows];
        int found = 0;
        for (int ry = 0; ry < rows; ry++)
            for (int rx = 0; rx < cols; rx++)
            {
                int grh = FindCellGrhAt(x0 + rx * 32, y0 + ry * 32);
                mosaic[ry * cols + rx] = grh;
                if (grh > 0) found++;
            }

        if (found == 0) return;

        State.SheetMosaic = mosaic;
        State.SheetMosaicW = cols;
        State.SheetMosaicH = rows;
        State.SelectedTexture = null;
        State.EyedropGrh = 0;
        _selectedGrh = 0;

        if (_selectionBar != null && _selectionLabel != null && _selectionPreview != null)
        {
            _selectionPreview.Texture = null;
            for (int i = 0; i < mosaic.Length && _selectionPreview.Texture == null; i++)
                if (mosaic[i] > 0) _selectionPreview.Texture = GetRegionPreview(mosaic[i]);
            _selectionLabel.Text = $"Bloque {cols}x{rows} ({found} tiles)\nUn clic coloca el bloque completo";
            _selectionBar.Visible = true;
        }

        EmitSignal(SignalName.SheetMosaicReady, cols, rows);
        PopulateRegions();
    }

    /// <summary>Smallest static GRH of this sheet covering a sheet pixel.</summary>
    private int FindGrhAt(int px, int py)
    {
        if (Grhs == null) return 0;
        int best = 0;
        int bestArea = int.MaxValue;
        foreach (int id in _currentRegions)
        {
            var grh = Grhs[id];
            if (grh.NumFrames > 1 || grh.FileNum != _selectedFileNumber) continue;
            if (px < grh.SX || py < grh.SY
                || px >= grh.SX + grh.PixelWidth || py >= grh.SY + grh.PixelHeight) continue;
            int area = grh.PixelWidth * grh.PixelHeight;
            if (area < bestArea)
            {
                bestArea = area;
                best = id;
            }
        }
        return best;
    }

    /// <summary>
    /// GRH for one cell of a captured block: like <see cref="FindGrhAt"/>, but
    /// restricted to pieces that fit inside the cell and start on it. A larger
    /// overlapping piece would be drawn from this cell's corner and spill over
    /// the neighbouring tiles, pulling artwork the selection never covered.
    /// </summary>
    private int FindCellGrhAt(int cellX, int cellY)
    {
        if (Grhs == null) return 0;
        int best = 0;
        int bestArea = 0;
        int fallback = 0;
        int fallbackArea = 0;

        foreach (int id in _currentRegions)
        {
            var grh = Grhs[id];
            if (grh.NumFrames > 1 || grh.FileNum != _selectedFileNumber) continue;

            int area = grh.PixelWidth * grh.PixelHeight;
            if (grh.SX == cellX && grh.SY == cellY)
            {
                // Anchored on this cell: take it whatever its size. Rejecting
                // pieces over 32px suited the old art, where everything sat on a
                // 32px grid, but the new catalogue has cells of other pitches and
                // whole props as single regions — those selected as nothing at all.
                if (area > bestArea) { bestArea = area; best = id; }
            }
            else if (grh.PixelWidth > 32 || grh.PixelHeight > 32)
            {
                // Oversized and not anchored here: it would be drawn from this
                // cell's corner and spill across neighbours the selection never
                // covered, so it is not a candidate.
                continue;
            }
            else if (best == 0
                && grh.SX >= cellX && grh.SY >= cellY
                && grh.SX + grh.PixelWidth <= cellX + 32
                && grh.SY + grh.PixelHeight <= cellY + 32)
            {
                // Sheets whose GRHs are not aligned to the 32px grid still have
                // a usable piece as long as it stays wholly inside the cell.
                if (area > fallbackArea) { fallbackArea = area; fallback = id; }
            }
        }
        return best != 0 ? best : fallback;
    }

    /// <summary>
    /// Picks the piece under the click, using its real position in the sheet.
    /// Road sets in particular are far easier to read this way, since adjacent
    /// 32×32 pieces differ only by a small edge detail.
    /// </summary>
    private void OnSheetPreviewInput(InputEvent @event)
    {
        // Rectangle capture inspects the event first and may consume the drag.
        bool wasDrag = _dragMoved;
        OnSheetDragInput(@event);
        if (@event is InputEventMouseButton { ButtonIndex: MouseButton.Right }) return;
        if (Input.IsKeyPressed(Key.Shift)) return;

        // Single pick fires on release, and only when the press never became a
        // rectangle drag — otherwise finishing a capture would also overwrite
        // the freshly armed mosaic with whatever tile is under the cursor.
        if (@event is not InputEventMouseButton { Pressed: false, ButtonIndex: MouseButton.Left }
            || wasDrag
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
        if (sourcePoint.X < 0 || sourcePoint.Y < 0
            || sourcePoint.X >= texture.GetWidth() || sourcePoint.Y >= texture.GetHeight()) return;

        int selected = 0;
        int bestArea = int.MaxValue;
        foreach (int id in _currentRegions)
        {
            var grh = Grhs[id];
            if (grh.NumFrames > 1 || grh.FileNum != _selectedFileNumber) continue;
            if (sourcePoint.X < grh.SX || sourcePoint.Y < grh.SY
                || sourcePoint.X >= grh.SX + grh.PixelWidth
                || sourcePoint.Y >= grh.SY + grh.PixelHeight)
                continue;
            int area = grh.PixelWidth * grh.PixelHeight;
            if (area < bestArea)
            {
                bestArea = area;
                selected = id;
            }
        }

        if (selected > 0) SelectRegion(selected);
    }

    public void Cleanup()
    {
        _regionCache.Clear();
        _currentRegions.Clear();
        _availableFiles.Clear();
    }
}

/// <summary>
/// Transparent overlay that draws the in-progress capture rectangle over the
/// sheet preview, with a badge showing how many 32×32 tiles it spans.
/// </summary>
public partial class SheetDragOverlay : Control
{
    private Rect2 _rect;
    private int _cols;
    private int _rows;
    private bool _active;

    public void SetRect(Rect2 rect, int cols, int rows)
    {
        _rect = rect;
        _cols = cols;
        _rows = rows;
        _active = true;
        QueueRedraw();
    }

    public void Clear()
    {
        if (!_active) return;
        _active = false;
        QueueRedraw();
    }

    public override void _Draw()
    {
        if (!_active || _rect.Size.X <= 0 || _rect.Size.Y <= 0) return;

        DrawRect(_rect, EditorTheme.ACCENT with { A = 0.20f }, filled: true);
        DrawRect(_rect, EditorTheme.ACCENT, filled: false, width: 2f);

        // Grid lines so the tile boundaries inside the capture are readable.
        if (_cols > 1 || _rows > 1)
        {
            var grid = EditorTheme.ACCENT with { A = 0.35f };
            float cw = _rect.Size.X / _cols;
            float ch = _rect.Size.Y / _rows;
            for (int c = 1; c < _cols; c++)
            {
                float x = _rect.Position.X + cw * c;
                DrawLine(new Vector2(x, _rect.Position.Y),
                         new Vector2(x, _rect.End.Y), grid, 1f);
            }
            for (int r = 1; r < _rows; r++)
            {
                float y = _rect.Position.Y + ch * r;
                DrawLine(new Vector2(_rect.Position.X, y),
                         new Vector2(_rect.End.X, y), grid, 1f);
            }
        }

        var font = ThemeDB.FallbackFont;
        if (font == null) return;
        string text = $"{_cols}×{_rows}";
        int fontSize = EditorTheme.FONT_SM;
        var textSize = font.GetStringSize(text, HorizontalAlignment.Left, -1, fontSize);

        // Badge sits just above the rectangle, flipping inside when at the top edge.
        var badgePos = new Vector2(_rect.Position.X, _rect.Position.Y - textSize.Y - 6);
        if (badgePos.Y < 0) badgePos.Y = _rect.Position.Y + 4;

        var badgeRect = new Rect2(badgePos, textSize + new Vector2(10, 6));
        DrawRect(badgeRect, EditorTheme.BG_DARK with { A = 0.85f }, filled: true);
        DrawRect(badgeRect, EditorTheme.ACCENT with { A = 0.6f }, filled: false, width: 1f);
        DrawString(font, badgePos + new Vector2(5, textSize.Y + 1),
            text, HorizontalAlignment.Left, -1, fontSize, Colors.White);
    }
}
