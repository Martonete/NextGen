#nullable enable
using System;
using System.Collections.Generic;
using Godot;
using AOWorldEditor.Data;

namespace AOWorldEditor.Editor;

/// <summary>
/// Visual browser for every valid entry in Graficos.ind.  Unlike the regular
/// tile palette, this does not require an indices.ini reference first.
/// </summary>
public partial class RawGrhPickerPopup : Window
{
    public GrhData[]? Grhs;
    public TextureManager? Textures;
    public Action<int>? GrhSelected;

    private const int Columns = 6;
    private const int PreviewSize = 52;
    private const int PageSize = 60;

    private readonly Dictionary<int, Texture2D?> _previewCache = new();
    private readonly List<int> _previousPages = new();
    private GridContainer? _grid;
    private LineEdit? _rangeInput;
    private Label? _pageLabel;
    private Button? _previousButton;
    private Button? _nextButton;
    // Existing maps keep their compact catalog untouched. The original
    // upstream graphics are imported into this collision-free GRH range.
    private int _rangeStart = 39001;
    private int _rangeEnd = 99274;
    private int _pageStart = 39001;

    public override void _Ready()
    {
        Title = "Explorador de GRH";
        Size = new Vector2I(470, 540);
        MinSize = new Vector2I(400, 380);
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
            "Muestra todos los sprites definidos en Graficos.ind. Elegí uno para pintarlo en la capa activa.",
            EditorTheme.TEXT_SECONDARY, EditorTheme.FONT_SM);
        help.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        root.AddChild(help);

        var filterRow = new HBoxContainer();
        filterRow.AddThemeConstantOverride("separation", 5);
        _rangeInput = new LineEdit
        {
            PlaceholderText = "GRH/rango o archivo:30326",
            ClearButtonEnabled = true,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
        };
        _rangeInput.TextSubmitted += _ => ApplyRange();
        filterRow.AddChild(_rangeInput);
        var applyButton = EditorTheme.PrimaryButton("Ver");
        applyButton.Pressed += ApplyRange;
        filterRow.AddChild(applyButton);
        root.AddChild(filterRow);

        var quickRow = new HBoxContainer();
        quickRow.AddThemeConstantOverride("separation", 4);
        foreach (var (label, start, end) in new[]
                 {
                     ("Todos", 1, int.MaxValue),
                     ("Originales", 39001, 99274),
                     ("L3 clásico", 5000, 7000),
                     ("Partículas", 27400, 27700),
                 })
        {
            var button = EditorTheme.MakeButton(label);
            button.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            button.Pressed += () => SetRange(start, end);
            quickRow.AddChild(button);
        }
        root.AddChild(quickRow);

        var scroll = new ScrollContainer
        {
            SizeFlagsVertical = Control.SizeFlags.ExpandFill,
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
        };
        root.AddChild(scroll);

        _grid = new GridContainer { Columns = Columns };
        _grid.AddThemeConstantOverride("h_separation", 4);
        _grid.AddThemeConstantOverride("v_separation", 4);
        scroll.AddChild(_grid);

        var pager = new HBoxContainer();
        pager.AddThemeConstantOverride("separation", 6);
        _previousButton = EditorTheme.MakeButton("◀", PreviousPage);
        _previousButton.CustomMinimumSize = new Vector2(36, 0);
        pager.AddChild(_previousButton);
        _pageLabel = EditorTheme.MakeLabel("", EditorTheme.TEXT_MUTED, EditorTheme.FONT_SM);
        _pageLabel.HorizontalAlignment = HorizontalAlignment.Center;
        _pageLabel.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        pager.AddChild(_pageLabel);
        _nextButton = EditorTheme.MakeButton("▶", NextPage);
        _nextButton.CustomMinimumSize = new Vector2(36, 0);
        pager.AddChild(_nextButton);
        root.AddChild(pager);

        PopulateGrid();
    }

    private void ApplyRange()
    {
        string text = (_rangeInput?.Text ?? "").Trim();
        if (text.Length == 0)
        {
            SetRange(1, int.MaxValue);
            return;
        }

        // Imported upstream sheets live under a collision-free FileNum range.
        // Let the mapper look them up with the original PNG filename instead
        // of having to know the remapped GRH id (e.g. archivo:30326).
        const string filePrefix = "archivo:";
        if (text.StartsWith(filePrefix, StringComparison.OrdinalIgnoreCase)
            && int.TryParse(text[filePrefix.Length..], out int fileNumber))
        {
            if (TrySelectFileNumber(fileNumber))
                return;
            if (_pageLabel != null)
                _pageLabel.Text = $"No hay GRH para el archivo {fileNumber}.png";
            return;
        }

        int dash = text.IndexOf('-');
        if (dash > 0 && int.TryParse(text[..dash], out int low) && int.TryParse(text[(dash + 1)..], out int high))
            SetRange(Math.Min(low, high), Math.Max(low, high));
        else if (int.TryParse(text, out int single))
            SetRange(single, single);
    }

    private bool TrySelectFileNumber(int originalFileNumber)
    {
        if (Grhs == null || originalFileNumber <= 0) return false;

        // Local files retain their FileNum. Imported upstream sheets are
        // shifted by 100000 so they never overwrite an existing texture.
        // Prefer the imported copy: an existing local FileNum may use the same
        // filename but represent an unrelated graphic.
        int[] candidates = { originalFileNumber + 100000, originalFileNumber };
        foreach (int fileNumber in candidates)
        {
            int first = -1;
            int last = -1;
            for (int id = 1; id < Grhs.Length; id++)
            {
                if (!Grhs[id].IsValid || Grhs[id].FileNum != fileNumber) continue;
                if (first < 0) first = id;
                last = id;
            }

            if (first < 0) continue;
            SetRange(first, last);
            return true;
        }

        return false;
    }

    private void SetRange(int start, int end)
    {
        _rangeStart = Math.Max(start, 1);
        _rangeEnd = Math.Max(end, _rangeStart);
        _pageStart = _rangeStart;
        _previousPages.Clear();
        PopulateGrid();
    }

    private void NextPage()
    {
        int last = LastValidIdOnPage(_pageStart);
        int max = MaximumId;
        if (last < 0 || last >= max) return;
        _previousPages.Add(_pageStart);
        _pageStart = last + 1;
        PopulateGrid();
    }

    private void PreviousPage()
    {
        if (_previousPages.Count == 0) return;
        _pageStart = _previousPages[^1];
        _previousPages.RemoveAt(_previousPages.Count - 1);
        PopulateGrid();
    }

    private int MaximumId => Grhs == null ? 0 : Math.Min(_rangeEnd, Grhs.Length - 1);

    private int LastValidIdOnPage(int start)
    {
        if (Grhs == null) return -1;
        int shown = 0;
        int last = -1;
        for (int id = start; id <= MaximumId && shown < PageSize; id++)
        {
            if (!Grhs[id].IsValid) continue;
            last = id;
            shown++;
        }
        return last;
    }

    private void PopulateGrid()
    {
        if (_grid == null || Grhs == null) return;
        foreach (var child in _grid.GetChildren()) child.QueueFree();

        int shown = 0;
        int last = -1;
        for (int id = _pageStart; id <= MaximumId && shown < PageSize; id++)
        {
            if (!Grhs[id].IsValid) continue;
            _grid.AddChild(CreateSpriteButton(id));
            last = id;
            shown++;
        }

        if (_pageLabel != null)
            _pageLabel.Text = shown == 0
                ? "No hay GRH válidos en este rango"
                : $"GRH {_pageStart}–{last} ({shown} sprites)";
        if (_previousButton != null) _previousButton.Disabled = _previousPages.Count == 0;
        if (_nextButton != null) _nextButton.Disabled = last < 0 || last >= MaximumId;
    }

    private TextureButton CreateSpriteButton(int id)
    {
        var button = new TextureButton
        {
            CustomMinimumSize = new Vector2(PreviewSize, PreviewSize),
            StretchMode = TextureButton.StretchModeEnum.KeepAspectCentered,
            IgnoreTextureSize = true,
            TooltipText = $"GRH {id}\nClick para usarlo en la capa activa",
        };
        button.TextureNormal = GetPreview(id);
        button.Pressed += () =>
        {
            GrhSelected?.Invoke(id);
            QueueFree();
        };
        return button;
    }

    private Texture2D? GetPreview(int id)
    {
        if (_previewCache.TryGetValue(id, out var cached)) return cached;

        Texture2D? preview = null;
        if (Grhs != null && Textures != null && id > 0 && id < Grhs.Length)
        {
            var grh = Grhs[id];
            if (grh.NumFrames > 1 && grh.Frames is { Length: > 0 })
            {
                int firstFrame = grh.Frames[0];
                if (firstFrame > 0 && firstFrame < Grhs.Length) grh = Grhs[firstFrame];
            }
            var texture = grh.FileNum > 0 ? Textures.GetTexture(grh.FileNum) : null;
            if (texture != null && grh.SX >= 0 && grh.SY >= 0)
            {
                int width = Math.Min(grh.PixelWidth, texture.GetWidth() - grh.SX);
                int height = Math.Min(grh.PixelHeight, texture.GetHeight() - grh.SY);
                if (width > 0 && height > 0)
                    preview = new AtlasTexture { Atlas = texture, Region = new Rect2(grh.SX, grh.SY, width, height) };
            }
        }
        _previewCache[id] = preview;
        return preview;
    }
}
