#nullable enable
using System;
using System.Collections.Generic;
using AOWorldEditor.Data;
using Godot;

namespace AOWorldEditor.Editor;

/// <summary>
/// Photoshop-style Humo layers panel with collapsible groups.
///
/// Layers are grouped by their `Group` field (empty string = root / no group).
/// Each group is shown as a collapsible header; clicking it expands/collapses
/// the layers under it. Users can create new groups ("Dungeon Veril") and
/// assign new layers to them — perfect for organizing dungeon-specific smokes.
///
/// Each layer row is a flat Button that captures the click reliably
/// (previous GuiInput-based approach was unreliable because Container
/// children intercepted the event).
/// </summary>
public partial class HumoLayersPanel : PanelContainer
{
    public MapData? Map;
    public Action? OnLayersChanged;

    private VBoxContainer? _listContainer;
    private LineEdit? _newLayerName;
    private LineEdit? _newGroupName;
    private OptionButton? _groupSelector;
    private readonly HashSet<string> _collapsedGroups = new();

    public override void _Ready()
    {
        Name = "Humo";
        CustomMinimumSize = new Vector2(240, 0);
        AddThemeStyleboxOverride("panel", EditorTheme.FlatBox(EditorTheme.BG_PANEL, 0, 0, 0));

        var scroll = new ScrollContainer();
        scroll.HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled;
        scroll.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        AddChild(scroll);

        var vbox = new VBoxContainer();
        vbox.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        vbox.AddThemeConstantOverride("separation", 4);
        scroll.AddChild(vbox);

        vbox.AddChild(EditorTheme.Heading("CAPAS DE HUMO"));

        var help = EditorTheme.MakeLabel(
            "Creá varias capas (y opcionalmente agrupalas en carpetas) para organizar el humo del mapa.",
            EditorTheme.TEXT_SECONDARY, EditorTheme.FONT_SM);
        help.AutowrapMode = TextServer.AutowrapMode.Word;
        vbox.AddChild(help);

        // ── New group row ──
        vbox.AddChild(EditorTheme.MakeLabel("Nuevo grupo:", EditorTheme.TEXT_SECONDARY, EditorTheme.FONT_SM));
        var groupRow = new HBoxContainer();
        groupRow.AddThemeConstantOverride("separation", 4);
        _newGroupName = new LineEdit
        {
            PlaceholderText = "Ej: Dungeon Veril",
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
        };
        groupRow.AddChild(_newGroupName);
        var newGroupBtn = new Button { Text = "+ Carpeta" };
        newGroupBtn.Pressed += OnCreateGroup;
        groupRow.AddChild(newGroupBtn);
        vbox.AddChild(groupRow);

        // ── New layer row ──
        vbox.AddChild(EditorTheme.MakeLabel("Nueva capa:", EditorTheme.TEXT_SECONDARY, EditorTheme.FONT_SM));
        _groupSelector = new OptionButton();
        _groupSelector.CustomMinimumSize = new Vector2(0, 24);
        vbox.AddChild(_groupSelector);

        var layerRow = new HBoxContainer();
        layerRow.AddThemeConstantOverride("separation", 4);
        _newLayerName = new LineEdit
        {
            PlaceholderText = "Nombre de la capa...",
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
        };
        layerRow.AddChild(_newLayerName);
        var newLayerBtn = new Button { Text = "+ Capa" };
        newLayerBtn.Pressed += OnCreateNewLayer;
        layerRow.AddChild(newLayerBtn);
        vbox.AddChild(layerRow);

        vbox.AddChild(new HSeparator());

        // ── Layer list ──
        _listContainer = new VBoxContainer();
        _listContainer.AddThemeConstantOverride("separation", 2);
        _listContainer.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        vbox.AddChild(_listContainer);
    }

    /// <summary>Rebuild the layer list. Call after any add/remove/rename/select.</summary>
    public void Rebuild()
    {
        if (_listContainer == null) return;
        foreach (var child in _listContainer.GetChildren())
            child.QueueFree();
        RebuildGroupSelector();
        if (Map == null) return;

        if (Map.PaintedFogLayers.Count == 0)
        {
            var empty = EditorTheme.MakeLabel("(sin capas — creá una arriba)",
                EditorTheme.TEXT_SECONDARY, EditorTheme.FONT_SM);
            empty.HorizontalAlignment = HorizontalAlignment.Center;
            _listContainer.AddChild(empty);
            return;
        }

        // Group layers by Group name (empty string = root). Preserve original
        // order within each group.
        var groups = new Dictionary<string, List<int>>();
        var groupOrder = new List<string>();
        for (int i = 0; i < Map.PaintedFogLayers.Count; i++)
        {
            var g = Map.PaintedFogLayers[i].Group ?? "";
            if (!groups.ContainsKey(g))
            {
                groups[g] = new List<int>();
                groupOrder.Add(g);
            }
            groups[g].Add(i);
        }

        foreach (var groupName in groupOrder)
        {
            if (groupName.Length > 0)
            {
                _listContainer.AddChild(BuildGroupHeader(groupName));
            }
            bool collapsed = _collapsedGroups.Contains(groupName);
            if (!collapsed)
            {
                foreach (var idx in groups[groupName])
                {
                    var indent = groupName.Length > 0;
                    _listContainer.AddChild(BuildLayerRow(Map.PaintedFogLayers[idx], idx, indent));
                }
            }
        }
    }

    private Control BuildGroupHeader(string groupName)
    {
        bool collapsed = _collapsedGroups.Contains(groupName);
        var btn = new Button
        {
            Text = $"  {(collapsed ? "▶" : "▼")}  📁 {groupName}",
            Alignment = HorizontalAlignment.Left,
            CustomMinimumSize = new Vector2(0, 26),
        };
        btn.AddThemeColorOverride("font_color", EditorTheme.TEXT_PRIMARY);
        btn.Pressed += () =>
        {
            if (!_collapsedGroups.Remove(groupName)) _collapsedGroups.Add(groupName);
            Rebuild();
        };
        return btn;
    }

    private Control BuildLayerRow(PaintedFogLayer layer, int layerIndex, bool indent)
    {
        bool isActive = Map != null && Map.ActiveFogLayerIndex == layerIndex;

        // The whole row is one flat Button — reliable click handling.
        var btn = new Button
        {
            Flat = !isActive,
            Alignment = HorizontalAlignment.Left,
            CustomMinimumSize = new Vector2(0, 32),
        };
        if (isActive)
            btn.AddThemeStyleboxOverride("normal",
                EditorTheme.FlatBox(EditorTheme.BG_SELECTED, 4, 6, 3, EditorTheme.ACCENT, 1));

        int capturedIndex = layerIndex;
        btn.Pressed += () => SelectLayer(capturedIndex);

        // Build the row content as a child HBox with Pass mouse filter so
        // clicks bubble up to the Button.
        var hbox = new HBoxContainer
        {
            MouseFilter = MouseFilterEnum.Ignore,
        };
        hbox.AddThemeConstantOverride("separation", 6);
        hbox.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        hbox.OffsetLeft = indent ? 16 : 4;
        hbox.OffsetRight = -28;  // leave room for delete button on right
        btn.AddChild(hbox);

        // Color swatch
        var swatch = new ColorRect
        {
            CustomMinimumSize = new Vector2(22, 22),
            Color = new Color(layer.R / 255f, layer.G / 255f, layer.B / 255f),
            MouseFilter = MouseFilterEnum.Ignore,
        };
        hbox.AddChild(swatch);

        // Name + tile count
        var labelBox = new VBoxContainer
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        labelBox.AddThemeConstantOverride("separation", 0);
        hbox.AddChild(labelBox);

        var nameLbl = EditorTheme.MakeLabel(layer.Name, EditorTheme.TEXT_PRIMARY, EditorTheme.FONT_SM);
        nameLbl.MouseFilter = MouseFilterEnum.Ignore;
        labelBox.AddChild(nameLbl);

        var tileLbl = EditorTheme.MakeLabel($"{layer.Tiles.Count} tiles",
            EditorTheme.TEXT_SECONDARY, EditorTheme.FONT_SM);
        tileLbl.MouseFilter = MouseFilterEnum.Ignore;
        labelBox.AddChild(tileLbl);

        // Delete button — overlaid on the right, captures its own clicks
        var delBtn = new Button
        {
            Text = "🗑",
            CustomMinimumSize = new Vector2(24, 24),
        };
        delBtn.SetAnchorsPreset(LayoutPreset.CenterRight);
        delBtn.OffsetLeft = -28;
        delBtn.OffsetRight = -4;
        delBtn.OffsetTop = -12;
        delBtn.OffsetBottom = 12;
        int capturedDel = layerIndex;
        delBtn.Pressed += () => OnDeleteLayer(capturedDel);
        btn.AddChild(delBtn);

        return btn;
    }

    private void RebuildGroupSelector()
    {
        if (_groupSelector == null || Map == null) return;
        _groupSelector.Clear();
        _groupSelector.AddItem("(sin grupo)");
        var seen = new HashSet<string> { "" };
        foreach (var l in Map.PaintedFogLayers)
        {
            if (!string.IsNullOrEmpty(l.Group) && seen.Add(l.Group))
                _groupSelector.AddItem($"📁 {l.Group}");
        }
    }

    private string GetSelectedGroup()
    {
        if (_groupSelector == null || Map == null) return "";
        int idx = _groupSelector.Selected;
        if (idx <= 0) return "";
        // Reconstruct the groups list in the same order as RebuildGroupSelector
        int i = 1;
        var seen = new HashSet<string> { "" };
        foreach (var l in Map.PaintedFogLayers)
        {
            if (!string.IsNullOrEmpty(l.Group) && seen.Add(l.Group))
            {
                if (i == idx) return l.Group;
                i++;
            }
        }
        return "";
    }

    private void OnCreateGroup()
    {
        if (Map == null) return;
        string name = _newGroupName?.Text?.Trim() ?? "";
        if (string.IsNullOrEmpty(name)) return;
        // We create a placeholder empty layer so the group shows up in the
        // list. User can delete it later and add proper layers to the group.
        // Actually better: just add to the dropdown — no empty layer.
        // To make the group "exist" without a layer, we register it in the
        // group selector by creating a dummy layer? Cleanest solution: force
        // an immediate "+ capa" with this group.
        // Simplest: store pending group name and wait for the user to add a layer.
        // For now: create a new layer directly inside this new group.
        var layer = new PaintedFogLayer
        {
            Name = $"Capa 1",
            Group = name,
            Density = 160, R = 128, G = 140, B = 160,
        };
        Map.PaintedFogLayers.Add(layer);
        Map.ActiveFogLayerIndex = Map.PaintedFogLayers.Count - 1;
        if (_newGroupName != null) _newGroupName.Text = "";
        Rebuild();
        // Select the newly-created group in the selector so subsequent
        // "+ Capa" goes into the same group.
        SelectGroupInSelector(name);
        OnLayersChanged?.Invoke();
    }

    private void SelectGroupInSelector(string groupName)
    {
        if (_groupSelector == null || Map == null) return;
        int i = 1;
        var seen = new HashSet<string> { "" };
        foreach (var l in Map.PaintedFogLayers)
        {
            if (!string.IsNullOrEmpty(l.Group) && seen.Add(l.Group))
            {
                if (l.Group == groupName)
                {
                    _groupSelector.Select(i);
                    return;
                }
                i++;
            }
        }
    }

    private void OnCreateNewLayer()
    {
        if (Map == null) return;
        string name = _newLayerName?.Text?.Trim() ?? "";
        string group = GetSelectedGroup();
        if (string.IsNullOrEmpty(name))
            name = $"Capa {Map.PaintedFogLayers.Count + 1}";

        var layer = new PaintedFogLayer
        {
            Name = name,
            Group = group,
            Density = 160, R = 128, G = 140, B = 160,
            SpeedX = 5, SpeedY = 2,
        };
        Map.PaintedFogLayers.Add(layer);
        Map.ActiveFogLayerIndex = Map.PaintedFogLayers.Count - 1;
        if (_newLayerName != null) _newLayerName.Text = "";
        GD.Print($"[HumoLayersPanel] Created layer '{name}' in group '{group}' → active index {Map.ActiveFogLayerIndex}");
        Rebuild();
        OnLayersChanged?.Invoke();
    }

    private void OnDeleteLayer(int index)
    {
        if (Map == null || index < 0 || index >= Map.PaintedFogLayers.Count) return;
        var removed = Map.PaintedFogLayers[index];
        Map.PaintedFogLayers.RemoveAt(index);
        if (Map.ActiveFogLayerIndex >= Map.PaintedFogLayers.Count)
            Map.ActiveFogLayerIndex = Map.PaintedFogLayers.Count - 1;
        GD.Print($"[HumoLayersPanel] Deleted layer '{removed.Name}' ({removed.Tiles.Count} tiles)");
        Rebuild();
        OnLayersChanged?.Invoke();
    }

    private void SelectLayer(int index)
    {
        if (Map == null || index < 0 || index >= Map.PaintedFogLayers.Count) return;
        Map.ActiveFogLayerIndex = index;
        GD.Print($"[HumoLayersPanel] Selected layer {index}: '{Map.PaintedFogLayers[index].Name}'");
        Rebuild();
        OnLayersChanged?.Invoke();
    }
}
