#nullable enable
using System;
using System.Collections.Generic;
using Godot;
using AOWorldEditor.Data;

namespace AOWorldEditor.Editor;

/// <summary>
/// Left sidebar panel for NPC selection. Shows a searchable list of all NPCs
/// from the database. Selecting an NPC makes it the active NPC for placement.
/// </summary>
public partial class NpcPalette : VBoxContainer
{
    [Signal] public delegate void NpcSelectedEventHandler(int npcNumber);

    public NpcDatabase? Database;
    public GrhData[]? Grhs;
    public TextureManager? Textures;
    public int[]? NpcBodies;       // NPC# → body index
    public int[]? NpcBodyGrhs;     // body index → south-facing GRH
    public int[]? NpcHeads;        // NPC# → head index
    public int[]? HeadGrhs;        // head index → south-facing GRH
    public int[]? NpcHeadOfsX;     // body index → head offset X
    public int[]? NpcHeadOfsY;     // body index → head offset Y
    public EditorState? State;

    private LineEdit? _searchBox;
    private OptionButton? _filterType;
    private ScrollContainer? _scroll;
    private VBoxContainer? _listContainer;
    private Label? _infoLabel;

    private int _selectedNpc;
    private readonly List<NpcRecord> _filteredList = new();
    private readonly List<Button> _itemButtons = new();

    private const int PreviewSize = 48;

    public override void _Ready()
    {
        CustomMinimumSize = new Vector2(300, 0);
        AddThemeConstantOverride("separation", 3);

        // Search box
        _searchBox = new LineEdit
        {
            PlaceholderText = "Buscar NPC por nombre o número...",
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
        _filterType.AddItem("Hostiles", 1);
        _filterType.AddItem("Comerciantes", 2);
        _filterType.AddItem("Pacíficos", 3);
        _filterType.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        _filterType.ItemSelected += _ => RebuildList();
        filterRow.AddChild(_filterType);
        AddChild(filterRow);

        // Info label
        _infoLabel = EditorTheme.MakeLabel("Carga datos del server para ver NPCs", EditorTheme.TEXT_MUTED, EditorTheme.FONT_SM);
        _infoLabel.CustomMinimumSize = new Vector2(0, 18);
        AddChild(_infoLabel);

        // Scrollable list
        _scroll = new ScrollContainer();
        _scroll.SizeFlagsVertical = SizeFlags.ExpandFill;
        _scroll.CustomMinimumSize = new Vector2(290, 200);
        AddChild(_scroll);

        _listContainer = new VBoxContainer();
        _listContainer.AddThemeConstantOverride("separation", 1);
        _listContainer.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        _scroll.AddChild(_listContainer);
    }

    /// <summary>Rebuild the NPC list after data is loaded or filter changes.</summary>
    public void Rebuild()
    {
        RebuildList();
    }

    private void RebuildList()
    {
        if (_listContainer == null) return;

        // Clear old items
        foreach (var child in _listContainer.GetChildren())
            child.QueueFree();
        _itemButtons.Clear();
        _filteredList.Clear();

        if (Database == null || Database.All.Count == 0)
        {
            _infoLabel!.Text = "Sin datos de NPCs — configura la ruta del server";
            return;
        }

        // Apply filter
        string search = _searchBox?.Text?.Trim().ToLowerInvariant() ?? "";
        int filterIdx = (int)(_filterType?.Selected ?? 0);

        foreach (var npc in Database.All)
        {
            // Type filter
            if (filterIdx == 1 && !npc.Hostile) continue;
            if (filterIdx == 2 && !npc.Comercia) continue;
            if (filterIdx == 3 && (npc.Hostile || npc.Comercia)) continue;

            // Search filter
            if (search.Length > 0)
            {
                bool matchName = npc.Name.ToLowerInvariant().Contains(search);
                bool matchNum = npc.Number.ToString().Contains(search);
                if (!matchName && !matchNum) continue;
            }

            _filteredList.Add(npc);
        }

        _infoLabel!.Text = $"{_filteredList.Count} NPCs";

        for (int i = 0; i < _filteredList.Count; i++)
        {
            var npc = _filteredList[i];
            var btn = CreateNpcButton(npc);
            _listContainer.AddChild(btn);
            _itemButtons.Add(btn);
        }
    }

    private Button CreateNpcButton(NpcRecord npc)
    {
        var btn = new Button
        {
            ToggleMode = true,
            CustomMinimumSize = new Vector2(0, 36),
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            ClipText = true,
        };

        // Layout: HBox with [preview | text]
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
        var tex = GetNpcBodyTexture(npc.Number);
        if (tex != null)
            preview.Texture = tex;
        hbox.AddChild(preview);

        // Text info
        var vtext = new VBoxContainer();
        vtext.AddThemeConstantOverride("separation", 0);
        vtext.MouseFilter = MouseFilterEnum.Ignore;

        string hostileTag = npc.Hostile ? " [H]" : "";
        string comTag = npc.Comercia ? " [$]" : "";
        var nameLabel = EditorTheme.MakeLabel(
            $"#{npc.Number} {npc.Name}{hostileTag}{comTag}",
            EditorTheme.TEXT_PRIMARY, EditorTheme.FONT_SM);
        nameLabel.MouseFilter = MouseFilterEnum.Ignore;
        vtext.AddChild(nameLabel);

        var typeLabel = EditorTheme.MakeLabel(npc.TypeLabel, EditorTheme.TEXT_MUTED, 9);
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

        int capturedNum = npc.Number;
        btn.Pressed += () => OnNpcClicked(capturedNum, btn);

        return btn;
    }

    private void OnNpcClicked(int npcNumber, Button btn)
    {
        _selectedNpc = npcNumber;

        // Deselect all others
        foreach (var b in _itemButtons)
            if (b != btn) b.SetPressedNoSignal(false);

        btn.SetPressedNoSignal(true);

        // Switch to NPC tool
        if (State != null)
            State.ActiveTool = EditorTool.Npc;

        EmitSignal(SignalName.NpcSelected, npcNumber);
    }

    public int SelectedNpc => _selectedNpc;

    /// <summary>Get a body texture for NPC preview in the palette.</summary>
    private Texture2D? GetNpcBodyTexture(int npcNumber)
    {
        if (NpcBodies == null || Grhs == null || Textures == null || NpcBodyGrhs == null)
            return null;

        if (npcNumber < 1 || npcNumber >= NpcBodies.Length) return null;
        int bodyIdx = NpcBodies[npcNumber];
        if (bodyIdx <= 0 || bodyIdx >= NpcBodyGrhs.Length) return null;
        int grhIdx = NpcBodyGrhs[bodyIdx];
        if (grhIdx <= 0 || grhIdx >= Grhs.Length) return null;

        var grh = Grhs[grhIdx];
        if (grh.FileNum <= 0) return null;

        var baseTex = Textures.GetTexture(grh.FileNum);
        if (baseTex == null) return null;

        var atlas = new AtlasTexture();
        atlas.Atlas = baseTex;
        atlas.Region = new Rect2(grh.SX, grh.SY, grh.PixelWidth, grh.PixelHeight);
        return atlas;
    }
}
