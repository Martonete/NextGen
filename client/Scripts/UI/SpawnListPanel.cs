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
/// Now uses RpgBaseForm for consistent RPG styling.
/// </summary>
public partial class SpawnListPanel : RpgBaseForm
{
    private GameState? _state;
    private AoTcpClient? _tcp;

    // Controls
    private LineEdit? _searchEdit;
    private ItemList? _npcList;
    private LineEdit? _manualIndexEdit;
    private LineEdit? _quantityEdit;

    // NPC data: (index, name)
    private readonly List<(int index, string name)> _allNpcs = new();
    private readonly List<(int index, string name)> _filteredNpcs = new();

    public SpawnListPanel() : base("Lista de NPCs", new Vector2(320, 420), "v2") { }

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

    /// <summary>
    /// Populate from server SpawnList packet data (VB6 frmSpawnList flow).
    /// Format: CSV "index1,name1,index2,name2,..." or "count,name1,name2,..."
    /// </summary>
    public void PopulateFromServerData(string data)
    {
        _allNpcs.Clear();
        if (string.IsNullOrEmpty(data)) return;

        var parts = data.Split(',', StringSplitOptions.RemoveEmptyEntries);
        // Try pairs: index,name,index,name,...
        for (int i = 0; i + 1 < parts.Length; i += 2)
        {
            if (int.TryParse(parts[i].Trim(), out int idx))
            {
                _allNpcs.Add((idx, parts[i + 1].Trim()));
            }
        }
        _allNpcs.Sort((a, b) => a.index.CompareTo(b.index));
        ApplyFilter("");
    }

    protected override void BuildContent()
    {
        var vbox = RpgTheme.CreateColumn(RpgTheme.SpacingSm);
        ContentContainer.AddChild(vbox);

        // Search bar
        _searchEdit = RpgTheme.CreateRpgInput("Filtrar por nombre...");
        _searchEdit.TextChanged += OnSearchTextChanged;
        vbox.AddChild(_searchEdit);

        // NPC list
        _npcList = RpgTheme.CreateRpgItemList(0, 200);
        vbox.AddChild(_npcList);

        // Manual index row
        var manualRow = RpgTheme.CreateRow(RpgTheme.SpacingSm);
        vbox.AddChild(manualRow);

        manualRow.AddChild(RpgTheme.CreateInfoLabel("Index:", 12));

        _manualIndexEdit = RpgTheme.CreateRpgInput("NPC #", 60);
        manualRow.AddChild(_manualIndexEdit);

        manualRow.AddChild(RpgTheme.CreateInfoLabel("Cant:", 12));

        _quantityEdit = RpgTheme.CreateRpgInput("", 40);
        _quantityEdit.Text = "1";
        manualRow.AddChild(_quantityEdit);

        // Spawn button
        var spawnBtn = RpgTheme.CreateRpgButton("Spawn", false, 13);
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
        ShowForm();
    }
}
