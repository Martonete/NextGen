#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using AODateador.Data;
using Godot;

namespace AODateador.Editor;

/// <summary>
/// Visual GRH chooser: a paged grid of sprite thumbnails, filtered to the sheet
/// class that suits the object being edited.
///
/// It replaces a bare SpinBox. Typing a number was unusable once the catalogue
/// reached 207145 entries, and it is what left 48 items sharing one icon.
/// </summary>
public partial class GrhPickerPopup : Window
{
    [Signal] public delegate void GrhChosenEventHandler(int grhIndex);

    // Injected before AddChild.
    public GrhData[]? Grhs;
    public TextureManager? Textures;
    public GrhCatalogIndex? Catalog;
    public int CurrentGrh;
    public string? PreferredClass;

    /// <summary>
    /// Thumbnails built per page. The catalogue runs to six figures, and a
    /// textured node for each would hang the window on open.
    /// </summary>
    private const int PageSize = 120;
    private const int ThumbSize = 52;

    private readonly List<int> _shown = new();
    private int _page;
    private int _pageCount = 1;
    private string _classFilter = "";
    private string _search = "";

    private GridContainer? _grid;
    private Label? _pageLabel;
    private Label? _infoLabel;
    private ScrollContainer? _scroll;

    public override void _Ready()
    {
        Title = "Elegir gráfico";
        Size = new Vector2I(620, 560);
        Exclusive = true;
        Unresizable = false;
        CloseRequested += Hide;

        var root = new PanelContainer();
        root.AddThemeStyleboxOverride("panel", DateadorTheme.FlatBox(DateadorTheme.BG_PANEL, 0, 0, 0));
        root.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        AddChild(root);

        var margin = new MarginContainer();
        foreach (string side in new[] { "left", "right", "top", "bottom" })
            margin.AddThemeConstantOverride($"margin_{side}", 12);
        root.AddChild(margin);

        var column = new VBoxContainer();
        column.AddThemeConstantOverride("separation", 8);
        margin.AddChild(column);

        column.AddChild(BuildFilterRow());

        _infoLabel = DateadorTheme.MakeLabel("", DateadorTheme.TEXT_SEC, DateadorTheme.FONT_SM);
        column.AddChild(_infoLabel);

        _scroll = new ScrollContainer
        {
            SizeFlagsVertical = Control.SizeFlags.ExpandFill,
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
        };
        column.AddChild(_scroll);

        _grid = new GridContainer { Columns = 10 };
        _grid.AddThemeConstantOverride("h_separation", 4);
        _grid.AddThemeConstantOverride("v_separation", 4);
        _scroll.AddChild(_grid);

        column.AddChild(BuildPager());

        _classFilter = PreferredClass ?? "";
        Rebuild();
    }

    private Control BuildFilterRow()
    {
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 6);

        // Class tabs, so a weapon offers weapon sheets rather than everything.
        var classes = new List<string> { "" };
        if (Catalog is { IsLoaded: true }) classes.AddRange(Catalog.Classes());

        var names = classes.Select(c => c.Length == 0 ? "Todos" : ClassLabel(c)).ToArray();
        var picker = DateadorTheme.MakeOptionButton(names,
            Math.Max(0, classes.IndexOf(PreferredClass ?? "")));
        picker.ItemSelected += index =>
        {
            _classFilter = classes[(int)index];
            _page = 0;
            Rebuild();
        };
        row.AddChild(DateadorTheme.MakeLabel("Clase", DateadorTheme.TEXT_SEC, DateadorTheme.FONT_SM));
        row.AddChild(picker);

        var search = DateadorTheme.MakeLineEdit("Buscar por número de GRH…", 200);
        search.TextChanged += text => { _search = text.Trim(); _page = 0; Rebuild(); };
        row.AddChild(search);

        return row;
    }

    private Control BuildPager()
    {
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 6);

        row.AddChild(DateadorTheme.SecondaryButton("<", () => GoToPage(_page - 1)));
        _pageLabel = DateadorTheme.MakeLabel("", DateadorTheme.TEXT_SEC, DateadorTheme.FONT_SM);
        _pageLabel.HorizontalAlignment = HorizontalAlignment.Center;
        _pageLabel.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        row.AddChild(_pageLabel);
        row.AddChild(DateadorTheme.SecondaryButton(">", () => GoToPage(_page + 1)));
        row.AddChild(DateadorTheme.SecondaryButton("Cerrar", Hide));

        return row;
    }

    private static string ClassLabel(string className) => className switch
    {
        GrhCatalogIndex.ClassItem => "Ítems",
        GrhCatalogIndex.ClassWeapon => "Armas",
        GrhCatalogIndex.ClassShield => "Escudos",
        GrhCatalogIndex.ClassHelmet => "Cascos",
        GrhCatalogIndex.ClassBody => "Cuerpos",
        GrhCatalogIndex.ClassHead => "Cabezas",
        GrhCatalogIndex.ClassNpc => "NPCs",
        GrhCatalogIndex.ClassProp => "Props",
        GrhCatalogIndex.ClassTile => "Terreno",
        _ => className,
    };

    private void GoToPage(int page)
    {
        int clamped = Math.Clamp(page, 0, _pageCount - 1);
        if (clamped == _page) return;
        _page = clamped;
        Rebuild();
        if (_scroll != null) _scroll.ScrollVertical = 0;
    }

    private void Rebuild()
    {
        if (_grid == null) return;
        foreach (var child in _grid.GetChildren()) child.QueueFree();

        _shown.Clear();
        var source = Catalog is { IsLoaded: true }
            ? Catalog.ForClass(_classFilter.Length == 0 ? null : _classFilter)
            : AllValidGrhs();

        foreach (int grh in source)
        {
            if (_search.Length > 0 && !grh.ToString().Contains(_search, StringComparison.Ordinal))
                continue;
            _shown.Add(grh);
        }

        _pageCount = Math.Max(1, (_shown.Count + PageSize - 1) / PageSize);
        _page = Math.Clamp(_page, 0, _pageCount - 1);

        int from = _page * PageSize;
        int to = Math.Min(from + PageSize, _shown.Count);
        for (int i = from; i < to; i++) _grid.AddChild(MakeThumb(_shown[i]));

        if (_pageLabel != null) _pageLabel.Text = $"{_page + 1}/{_pageCount}";
        if (_infoLabel != null)
            _infoLabel.Text = _shown.Count == 0
                ? "Sin gráficos para este filtro"
                : $"{_shown.Count} gráficos · actual: {CurrentGrh}";
    }

    /// <summary>Fallback when the catalogue is unavailable: every drawable GRH.</summary>
    private IReadOnlyList<int> AllValidGrhs()
    {
        var all = new List<int>();
        if (Grhs == null) return all;
        for (int i = 1; i < Grhs.Length; i++)
            if (Grhs[i].IsValid && Grhs[i].FileNum > 0) all.Add(i);
        return all;
    }

    private Control MakeThumb(int grhIndex)
    {
        var button = new TextureButton
        {
            CustomMinimumSize = new Vector2(ThumbSize, ThumbSize),
            StretchMode = TextureButton.StretchModeEnum.KeepAspectCentered,
            IgnoreTextureSize = true,
            TooltipText = $"GRH {grhIndex}",
            TextureNormal = GrhIcons.Get(Grhs, Textures, grhIndex),
        };

        // The current graphic is tinted so it stands out while browsing.
        if (grhIndex == CurrentGrh)
            button.Modulate = new Color(0.55f, 0.9f, 1f);

        button.Pressed += () =>
        {
            EmitSignal(SignalName.GrhChosen, grhIndex);
            Hide();
        };
        return button;
    }
}
