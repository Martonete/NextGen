#nullable enable
using System;
using System.Collections.Generic;
using Godot;
using AOWorldEditor.Data;

namespace AOWorldEditor.Editor;

/// <summary>
/// Left sidebar panel for Object selection. Shows a searchable list of all objects
/// from Obj.dat. Selecting an object makes it the active object for placement.
/// </summary>
public partial class ObjectPalette : VBoxContainer
{
    [Signal] public delegate void ObjectSelectedEventHandler(int objNumber);
    [Signal] public delegate void RequestServerPathEventHandler();

    public ObjectDatabase? Database;
    public GrhData[]? Grhs;
    public TextureManager? Textures;
    public int[]? ObjGrhs;  // obj# → GRH index (from GameDataLoader)
    public EditorState? State;

    private LineEdit? _searchBox;
    private OptionButton? _filterType;
    private ScrollContainer? _scroll;
    private VBoxContainer? _listContainer;
    private Label? _infoLabel;
    private VBoxContainer? _emptyState;

    private int _selectedObj;
    private readonly List<ObjRecord> _filteredList = new();
    private readonly List<Button> _itemButtons = new();

    public override void _Ready()
    {
        SizeFlagsHorizontal = SizeFlags.ExpandFill;
        ClipContents = true;
        AddThemeConstantOverride("separation", 3);

        // Search box
        _searchBox = new LineEdit
        {
            PlaceholderText = "Buscar objeto por nombre o número...",
            ClearButtonEnabled = true,
        };
        _searchBox.AddThemeFontSizeOverride("font_size", EditorTheme.FONT_SM);
        _searchBox.TextChanged += _ => RebuildList();
        AddChild(_searchBox);

        // Filter dropdown
        var filterRow = new HBoxContainer();
        filterRow.AddThemeConstantOverride("separation", 4);
        filterRow.AddChild(EditorTheme.MakeLabel("Filtro:", EditorTheme.TEXT_SECONDARY, EditorTheme.FONT_SM));
        _filterType = new OptionButton();
        _filterType.AddThemeFontSizeOverride("font_size", EditorTheme.FONT_SM);
        _filterType.AddItem("Todos", 0);
        _filterType.AddItem("Decoración", 1);
        _filterType.AddItem("Equipamiento", 2);
        _filterType.AddItem("Consumibles", 3);
        _filterType.AddItem("Vestimenta", 4);
        _filterType.AddItem("Puertas/Llaves", 5);
        _filterType.AddItem("Otros", 6);
        _filterType.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        _filterType.ItemSelected += _ => RebuildList();
        filterRow.AddChild(_filterType);
        AddChild(filterRow);

        // Info label
        _infoLabel = EditorTheme.MakeLabel("Carga datos del server para ver objetos", EditorTheme.TEXT_MUTED, EditorTheme.FONT_SM);
        _infoLabel.CustomMinimumSize = new Vector2(0, 18);
        AddChild(_infoLabel);

        // Scrollable list
        _scroll = new ScrollContainer();
        _scroll.SizeFlagsVertical = SizeFlags.ExpandFill;
        _scroll.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        _scroll.CustomMinimumSize = new Vector2(0, 200);
        _scroll.HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled;
        _scroll.ClipContents = true;
        AddChild(_scroll);

        _listContainer = new VBoxContainer();
        _listContainer.AddThemeConstantOverride("separation", 1);
        _listContainer.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        _scroll.AddChild(_listContainer);

        // Empty state
        _emptyState = new VBoxContainer();
        _emptyState.AddThemeConstantOverride("separation", 8);
        _emptyState.SizeFlagsVertical = SizeFlags.ExpandFill;
        _emptyState.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        _emptyState.Visible = false;

        var emptyLabel = EditorTheme.MakeLabel("Selecciona la carpeta del servidor\npara cargar objetos.",
            EditorTheme.TEXT_MUTED, EditorTheme.FONT_SM);
        emptyLabel.HorizontalAlignment = HorizontalAlignment.Center;
        emptyLabel.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _emptyState.AddChild(emptyLabel);

        var serverBtn = EditorTheme.PrimaryButton("Seleccionar Server...");
        serverBtn.Pressed += () => EmitSignal(SignalName.RequestServerPath);
        _emptyState.AddChild(serverBtn);

        AddChild(_emptyState);
    }

    /// <summary>Rebuild the object list after data is loaded or filter changes.</summary>
    public void Rebuild()
    {
        RebuildList();
    }

    private static readonly string[] FilterCategories =
    {
        "", "Decoración", "Equipamiento", "Consumibles", "Vestimenta", "Puertas/Llaves", "Otros"
    };

    private void RebuildList()
    {
        if (_listContainer == null) return;

        foreach (var child in _listContainer.GetChildren())
            child.QueueFree();
        _itemButtons.Clear();
        _filteredList.Clear();

        if (Database == null || Database.All.Count == 0)
        {
            _infoLabel!.Text = "";
            if (_emptyState != null) _emptyState.Visible = true;
            if (_scroll != null) _scroll.Visible = false;
            return;
        }
        if (_emptyState != null) _emptyState.Visible = false;
        if (_scroll != null) _scroll.Visible = true;

        string search = _searchBox?.Text?.Trim().ToLowerInvariant() ?? "";
        int filterIdx = (int)(_filterType?.Selected ?? 0);
        string filterCat = filterIdx > 0 && filterIdx < FilterCategories.Length ? FilterCategories[filterIdx] : "";

        foreach (var obj in Database.All)
        {
            // Category filter
            if (filterCat.Length > 0 && obj.Category != filterCat) continue;

            // Search filter
            if (search.Length > 0)
            {
                bool matchName = obj.Name.ToLowerInvariant().Contains(search);
                bool matchNum = obj.Number.ToString().Contains(search);
                if (!matchName && !matchNum) continue;
            }

            _filteredList.Add(obj);
        }

        _infoLabel!.Text = $"{_filteredList.Count} objetos";

        for (int i = 0; i < _filteredList.Count; i++)
        {
            var obj = _filteredList[i];
            var btn = CreateObjButton(obj);
            _listContainer.AddChild(btn);
            _itemButtons.Add(btn);
        }
    }

    private Button CreateObjButton(ObjRecord obj)
    {
        var btn = new Button
        {
            ToggleMode = true,
            CustomMinimumSize = new Vector2(0, 36),
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            ClipText = true,
        };

        var hbox = new HBoxContainer();
        hbox.AddThemeConstantOverride("separation", 6);
        hbox.MouseFilter = MouseFilterEnum.Ignore;

        // Preview texture
        var preview = new TextureRect
        {
            CustomMinimumSize = new Vector2(32, 32),
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        var tex = GetObjTexture(obj.GrhIndex);
        if (tex != null)
            preview.Texture = tex;
        hbox.AddChild(preview);

        // Text info
        var vtext = new VBoxContainer();
        vtext.AddThemeConstantOverride("separation", 0);
        vtext.MouseFilter = MouseFilterEnum.Ignore;

        var nameLabel = EditorTheme.MakeLabel(
            $"#{obj.Number} {obj.Name}",
            EditorTheme.TEXT_PRIMARY, EditorTheme.FONT_SM);
        nameLabel.MouseFilter = MouseFilterEnum.Ignore;
        vtext.AddChild(nameLabel);

        var typeLabel = EditorTheme.MakeLabel(obj.TypeLabel, EditorTheme.TEXT_MUTED, 9);
        typeLabel.MouseFilter = MouseFilterEnum.Ignore;
        vtext.AddChild(typeLabel);

        hbox.AddChild(vtext);
        btn.AddChild(hbox);

        // Style
        btn.AddThemeStyleboxOverride("normal",
            EditorTheme.FlatBox(EditorTheme.BG_PANEL, 2, 4, 2));
        btn.AddThemeStyleboxOverride("hover",
            EditorTheme.FlatBox(EditorTheme.BG_TOOL_HOVER, 2, 4, 2));
        btn.AddThemeStyleboxOverride("pressed",
            EditorTheme.FlatBox(EditorTheme.BG_TOOL_ACTIVE, 2, 4, 2, EditorTheme.ACCENT, 1));

        int capturedNum = obj.Number;
        btn.Pressed += () => OnObjClicked(capturedNum, btn);

        return btn;
    }

    private void OnObjClicked(int objNumber, Button btn)
    {
        _selectedObj = objNumber;

        foreach (var b in _itemButtons)
            if (b != btn) b.SetPressedNoSignal(false);

        btn.SetPressedNoSignal(true);

        if (State != null)
            State.ActiveTool = EditorTool.Object;

        EmitSignal(SignalName.ObjectSelected, objNumber);
    }

    public int SelectedObj => _selectedObj;

    /// <summary>Get a texture for object preview in the palette.</summary>
    private Texture2D? GetObjTexture(int grhIndex)
    {
        if (Grhs == null || Textures == null) return null;
        if (grhIndex <= 0 || grhIndex >= Grhs.Length) return null;

        var grh = Grhs[grhIndex];
        // Resolve animation to first frame
        if (grh.NumFrames > 1 && grh.Frames != null && grh.Frames.Length > 0)
        {
            int fIdx = grh.Frames[0];
            if (fIdx > 0 && fIdx < Grhs.Length) grh = Grhs[fIdx];
        }
        if (grh.FileNum <= 0) return null;

        var baseTex = Textures.GetTexture(grh.FileNum);
        if (baseTex == null) return null;

        if (!TryGetSafeGrhRegion(grh, baseTex, out var region))
            return null;

        var atlas = new AtlasTexture();
        atlas.Atlas = baseTex;
        atlas.Region = region;
        return atlas;
    }

    private static bool TryGetSafeGrhRegion(GrhData grh, Texture2D texture, out Rect2 region)
    {
        region = default;
        if (grh.SX < 0 || grh.SY < 0 || grh.PixelWidth <= 0 || grh.PixelHeight <= 0)
            return false;

        int width = Math.Min(grh.PixelWidth, texture.GetWidth() - grh.SX);
        int height = Math.Min(grh.PixelHeight, texture.GetHeight() - grh.SY);
        if (width <= 0 || height <= 0)
            return false;

        region = new Rect2(grh.SX, grh.SY, width, height);
        return true;
    }
}
