using Godot;
using System;
using System.Collections.Generic;
using ArgentumNextgen.Game;
using ArgentumNextgen.Network;

namespace ArgentumNextgen.UI;

/// <summary>
/// VB6 frmSpawnList — NPC spawn list for GMs.
/// Filterable list of NPCs by name. Selecting + clicking Spawn sends /ACC command.
/// Can be populated from GameData NPC definitions or a simple index input.
/// </summary>
public partial class SpawnListPanel : PanelContainer
{
    private const int PanelW = 320;
    private const int PanelH = 420;
    private const int TitleBarH = 28;

    private GameState? _state;
    private AoTcpClient? _tcp;

    // Dragging
    private bool _dragging;
    private Vector2 _dragOffset;

    // Controls
    private LineEdit? _searchEdit;
    private ItemList? _npcList;
    private LineEdit? _manualIndexEdit;
    private LineEdit? _quantityEdit;

    // NPC data: (index, name)
    private readonly List<(int index, string name)> _allNpcs = new();
    private readonly List<(int index, string name)> _filteredNpcs = new();

    public void Init(GameState state, AoTcpClient? tcp)
    {
        _state = state;
        _tcp = tcp;
    }

    public void SetTcp(AoTcpClient? tcp) => _tcp = tcp;

    /// <summary>
    /// Populate the NPC list from known NPC definitions.
    /// Call with a dictionary of index→name pairs loaded from GameData.
    /// </summary>
    public void PopulateNpcs(Dictionary<int, string>? npcNames)
    {
        _allNpcs.Clear();
        if (npcNames != null)
        {
            foreach (var kvp in npcNames)
                _allNpcs.Add((kvp.Key, kvp.Value));
            _allNpcs.Sort((a, b) => a.index.CompareTo(b.index));
        }
        ApplyFilter("");
    }

    public override void _Ready()
    {
        Visible = false;
        CustomMinimumSize = new Vector2(PanelW, PanelH);
        Size = new Vector2(PanelW, PanelH);

        var style = new StyleBoxFlat();
        style.BgColor = new Color(0.08f, 0.08f, 0.12f, 0.95f);
        style.BorderColor = new Color(0.4f, 0.35f, 0.25f, 1f);
        style.SetBorderWidthAll(2);
        style.SetCornerRadiusAll(3);
        AddThemeStyleboxOverride("panel", style);

        var root = new VBoxContainer();
        root.SetAnchorsPreset(LayoutPreset.FullRect);
        root.AddThemeConstantOverride("separation", 4);
        AddChild(root);

        // Title bar
        var titleBar = new HBoxContainer();
        titleBar.CustomMinimumSize = new Vector2(0, TitleBarH);
        root.AddChild(titleBar);

        var titleLabel = new Label();
        titleLabel.Text = "  Lista de NPCs";
        titleLabel.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        titleLabel.AddThemeColorOverride("font_color", new Color(0.9f, 0.8f, 0.5f));
        titleLabel.AddThemeFontSizeOverride("font_size", 13);
        titleBar.AddChild(titleLabel);

        var closeBtn = new Button();
        closeBtn.Text = "X";
        closeBtn.CustomMinimumSize = new Vector2(28, 24);
        closeBtn.Pressed += () => Visible = false;
        titleBar.AddChild(closeBtn);

        // Search bar
        _searchEdit = new LineEdit();
        _searchEdit.PlaceholderText = "Filtrar por nombre...";
        _searchEdit.TextChanged += OnSearchTextChanged;
        root.AddChild(_searchEdit);

        // NPC list
        _npcList = new ItemList();
        _npcList.SizeFlagsVertical = SizeFlags.ExpandFill;
        _npcList.CustomMinimumSize = new Vector2(PanelW - 16, 200);
        _npcList.AddThemeFontSizeOverride("font_size", 11);
        root.AddChild(_npcList);

        // Manual index row
        var manualRow = new HBoxContainer();
        manualRow.AddThemeConstantOverride("separation", 4);
        root.AddChild(manualRow);

        var idxLabel = new Label();
        idxLabel.Text = "Index:";
        idxLabel.AddThemeFontSizeOverride("font_size", 11);
        manualRow.AddChild(idxLabel);

        _manualIndexEdit = new LineEdit();
        _manualIndexEdit.CustomMinimumSize = new Vector2(60, 0);
        _manualIndexEdit.PlaceholderText = "NPC #";
        manualRow.AddChild(_manualIndexEdit);

        var qtyLabel = new Label();
        qtyLabel.Text = "Cant:";
        qtyLabel.AddThemeFontSizeOverride("font_size", 11);
        manualRow.AddChild(qtyLabel);

        _quantityEdit = new LineEdit();
        _quantityEdit.CustomMinimumSize = new Vector2(40, 0);
        _quantityEdit.Text = "1";
        manualRow.AddChild(_quantityEdit);

        // Spawn button
        var spawnBtn = new Button();
        spawnBtn.Text = "Spawn";
        spawnBtn.CustomMinimumSize = new Vector2(80, 28);
        spawnBtn.Pressed += OnSpawnPressed;
        manualRow.AddChild(spawnBtn);
    }

    private void OnSearchTextChanged(string newText)
    {
        ApplyFilter(newText);
    }

    private void ApplyFilter(string filter)
    {
        _filteredNpcs.Clear();
        string lower = filter.ToLowerInvariant();
        foreach (var npc in _allNpcs)
        {
            if (string.IsNullOrEmpty(filter) || npc.name.ToLowerInvariant().Contains(lower))
                _filteredNpcs.Add(npc);
        }
        RefreshList();
    }

    private void RefreshList()
    {
        if (_npcList == null) return;
        _npcList.Clear();
        foreach (var npc in _filteredNpcs)
            _npcList.AddItem($"[{npc.index}] {npc.name}");
    }

    private void OnSpawnPressed()
    {
        string indexStr = "";
        string qty = _quantityEdit?.Text.Trim() ?? "1";
        if (qty.Length == 0) qty = "1";

        // Try manual index first
        string manual = _manualIndexEdit?.Text.Trim() ?? "";
        if (manual.Length > 0)
        {
            indexStr = manual;
        }
        else if (_npcList != null)
        {
            // Use selected from list
            var selected = _npcList.GetSelectedItems();
            if (selected.Length > 0 && selected[0] < _filteredNpcs.Count)
            {
                indexStr = _filteredNpcs[selected[0]].index.ToString();
            }
        }

        if (indexStr.Length > 0)
        {
            _tcp?.SendPacket(ClientPackets.WriteTalk($"/ACC {indexStr} {qty}"));
        }
    }

    public void Open()
    {
        Visible = true;
    }

    // ── Dragging ──────────────────────────────────────────────

    public override void _GuiInput(InputEvent @event)
    {
        if (@event is InputEventMouseButton mb)
        {
            if (mb.ButtonIndex == MouseButton.Left)
            {
                if (mb.Pressed && mb.Position.Y < TitleBarH)
                {
                    _dragging = true;
                    _dragOffset = mb.Position;
                    AcceptEvent();
                }
                else if (!mb.Pressed)
                    _dragging = false;
            }
        }
        else if (@event is InputEventMouseMotion mm && _dragging)
        {
            Position += mm.Relative;
            AcceptEvent();
        }
    }
}
