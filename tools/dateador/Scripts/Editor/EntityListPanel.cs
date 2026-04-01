#nullable enable
using System;
using System.Collections.Generic;
using Godot;
using AODateador.Data;

namespace AODateador.Editor;

/// <summary>
/// Reusable left-panel widget: searchable list of numbered entries.
/// Used for NPCs, Objects, and Spells.
/// Fixed width ~280 px; expands vertically to fill its parent.
/// </summary>
public partial class EntityListPanel : VBoxContainer
{
    // ── Public API ──────────────────────────────────────────────────────────

    /// <summary>Fires with the 0-based index into the filtered/displayed list.</summary>
    public event Action<int>? ItemSelected;

    /// <summary>Fires when the user clicks the "+ Nuevo" button at the bottom.</summary>
    public event Action? AddRequested;

    // ── Private state ────────────────────────────────────────────────────────

    private LineEdit? _searchBox;
    private ItemList? _itemList;
    private Label?   _countLabel;

    /// <summary>Full, unfiltered data set.  Id = internal entity Id, Display = list label.</summary>
    private List<(int Id, string Display)> _allItems = new();

    /// <summary>Subset currently shown in the ItemList (may be filtered by search).</summary>
    private List<(int Id, string Display)> _filtered = new();

    // ── Godot lifecycle ───────────────────────────────────────────────────────

    public override void _Ready()
    {
        Build();
    }

    // ── UI construction ───────────────────────────────────────────────────────

    private void Build()
    {
        // Fixed width for the left panel
        CustomMinimumSize = new Vector2(280, 0);
        SizeFlagsVertical = SizeFlags.ExpandFill;

        AddThemeConstantOverride("separation", 4);

        // ── Search box ──────────────────────────────────────────────────────
        _searchBox = DateadorTheme.MakeLineEdit("Buscar…");
        _searchBox.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        _searchBox.TextChanged += OnSearchChanged;
        AddChild(_searchBox);

        // ── Item list ───────────────────────────────────────────────────────
        _itemList = new ItemList
        {
            SizeFlagsVertical   = SizeFlags.ExpandFill,
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            SelectMode          = ItemList.SelectModeEnum.Single,
            AllowReselect       = true,
        };
        DateadorTheme.StyleItemList(_itemList);
        _itemList.ItemSelected += OnItemSelected;
        AddChild(_itemList);

        // ── Bottom bar: count label + add button ────────────────────────────
        var bottomRow = new HBoxContainer();
        bottomRow.AddThemeConstantOverride("separation", 6);

        _countLabel = DateadorTheme.MakeLabel("0 items", DateadorTheme.TEXT_SEC, DateadorTheme.FONT_SM);
        _countLabel.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        bottomRow.AddChild(_countLabel);

        var addBtn = DateadorTheme.SecondaryButton("+ Nuevo", () => AddRequested?.Invoke());
        bottomRow.AddChild(addBtn);

        AddChild(bottomRow);
    }

    // ── Public methods ────────────────────────────────────────────────────────

    /// <summary>
    /// Replaces the full data set and refreshes the displayed list.
    /// Call this after loading or reloading from disk.
    /// </summary>
    public void SetItems(List<(int Id, string Display)> items)
    {
        _allItems = items;
        ApplyFilter(_searchBox?.Text ?? string.Empty);
    }

    /// <summary>
    /// Selects the list row whose Id matches <paramref name="id"/>.
    /// Fires <see cref="ItemSelected"/> with the matching index, or does nothing if not found.
    /// </summary>
    public void SelectById(int id)
    {
        for (int i = 0; i < _filtered.Count; i++)
        {
            if (_filtered[i].Id != id) continue;

            _itemList?.Select(i);
            _itemList?.EnsureCurrentIsVisible();
            ItemSelected?.Invoke(i);
            return;
        }
    }

    /// <summary>
    /// Returns the entity Id of the currently selected row, or -1 if nothing is selected.
    /// </summary>
    public int SelectedId()
    {
        if (_itemList == null) return -1;
        var sel = _itemList.GetSelectedItems();
        if (sel.Length == 0) return -1;
        int idx = sel[0];
        return idx < _filtered.Count ? _filtered[idx].Id : -1;
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    private void OnSearchChanged(string text)
    {
        int previousId = SelectedId();
        ApplyFilter(text);

        // Re-select same entity if it is still visible after filtering
        if (previousId >= 0)
            SelectById(previousId);
    }

    private void ApplyFilter(string query)
    {
        if (_itemList == null) return;

        _filtered.Clear();
        query = query.Trim();

        if (string.IsNullOrEmpty(query))
        {
            _filtered.AddRange(_allItems);
        }
        else
        {
            // Support "#123" syntax to jump directly to an Id
            if (query.StartsWith('#') && int.TryParse(query.AsSpan(1), out int targetId))
            {
                foreach (var item in _allItems)
                    if (item.Id == targetId)
                        _filtered.Add(item);
            }
            else
            {
                foreach (var item in _allItems)
                    if (item.Display.Contains(query, StringComparison.OrdinalIgnoreCase))
                        _filtered.Add(item);
            }
        }

        _itemList.Clear();
        foreach (var (_, display) in _filtered)
            _itemList.AddItem(display);

        UpdateCountLabel();
    }

    private void UpdateCountLabel()
    {
        if (_countLabel == null) return;
        int shown = _filtered.Count;
        int total = _allItems.Count;
        _countLabel.Text = shown == total
            ? $"{total} items"
            : $"{shown} / {total} items";
    }

    private void OnItemSelected(long index)
    {
        ItemSelected?.Invoke((int)index);
    }
}
