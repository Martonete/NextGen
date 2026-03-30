#nullable enable
using System;
using System.Collections.Generic;
using Godot;
using AOWorldEditor.Data;

namespace AOWorldEditor.Editor;

/// <summary>
/// Sidebar panel for managing zones. Lists all zones in the current map,
/// allows creating, editing, deleting zones, and selecting them for viewport highlighting.
/// </summary>
public partial class ZonePanel : VBoxContainer
{
    public EditorState? State;
    public MapZoneData? ZoneData;

    /// <summary>Fired when a zone is selected (for viewport centering/highlighting).</summary>
    public Action<ZoneInfo>? OnZoneSelected;
    /// <summary>Fired when zone data changes (add/edit/delete).</summary>
    public Action? OnZonesChanged;
    /// <summary>Fired when user wants to edit a zone's properties.</summary>
    public Action<ZoneInfo>? OnEditZone;

    private VBoxContainer? _listContainer;
    private Label? _emptyLabel;
    private int _selectedZoneId = -1;

    public override void _Ready()
    {
        SizeFlagsHorizontal = SizeFlags.ExpandFill;
        ClipContents = true;
        AddThemeConstantOverride("separation", 6);

        // Top margin so content doesn't hug the tab bar
        var topSpacer = new Control();
        topSpacer.CustomMinimumSize = new Vector2(0, 8);
        AddChild(topSpacer);

        // Header with add button
        var header = new HBoxContainer();
        header.AddThemeConstantOverride("separation", 8);
        var title = EditorTheme.SectionLabel("ZONAS");
        title.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        header.AddChild(title);
        var addBtn = EditorTheme.PrimaryButton("+ Nueva Zona", OnAddZone);
        addBtn.CustomMinimumSize = new Vector2(120, 28);
        header.AddChild(addBtn);
        AddChild(header);

        AddChild(EditorTheme.MakeHSeparator());

        // Scrollable list
        var scroll = new ScrollContainer();
        scroll.SizeFlagsVertical = SizeFlags.ExpandFill;
        scroll.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        scroll.HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled;
        scroll.ClipContents = true;
        AddChild(scroll);

        _listContainer = new VBoxContainer();
        _listContainer.AddThemeConstantOverride("separation", 4);
        _listContainer.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        scroll.AddChild(_listContainer);

        _emptyLabel = EditorTheme.MakeLabel("No hay zonas definidas.\nUsa '+ Nueva Zona' para crear una.",
            EditorTheme.TEXT_SECONDARY, EditorTheme.FONT_SM);
        _emptyLabel.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _listContainer.AddChild(_emptyLabel);

        RebuildList();
    }

    /// <summary>Rebuild the zone list from ZoneData.</summary>
    public void RebuildList()
    {
        if (_listContainer == null) return;

        // Remove all children except _emptyLabel
        foreach (var child in _listContainer.GetChildren())
        {
            if (child != _emptyLabel)
                child.QueueFree();
        }

        if (ZoneData == null || ZoneData.Zones.Count == 0)
        {
            if (_emptyLabel != null) _emptyLabel.Visible = true;
            return;
        }

        if (_emptyLabel != null) _emptyLabel.Visible = false;

        foreach (var zone in ZoneData.Zones)
        {
            var card = CreateZoneCard(zone);
            _listContainer.AddChild(card);
        }
    }

    private PanelContainer CreateZoneCard(ZoneInfo zone)
    {
        var panel = new PanelContainer();
        var bgColor = GetZoneTypeColor(zone.Type);
        bgColor.A = 0.15f;
        panel.AddThemeStyleboxOverride("panel", EditorTheme.FlatBox(bgColor, 6, 8, 6));
        panel.CustomMinimumSize = new Vector2(0, 60);

        var hbox = new HBoxContainer();
        hbox.AddThemeConstantOverride("separation", 8);
        panel.AddChild(hbox);

        // Color indicator bar
        var colorBar = new ColorRect();
        colorBar.Color = GetZoneTypeColor(zone.Type);
        colorBar.CustomMinimumSize = new Vector2(4, 0);
        hbox.AddChild(colorBar);

        // Info column
        var info = new VBoxContainer();
        info.AddThemeConstantOverride("separation", 2);
        info.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        hbox.AddChild(info);

        var nameLabel = EditorTheme.MakeLabel(
            zone.Name.Length > 0 ? zone.Name : $"Zona {zone.Id}",
            EditorTheme.TEXT_PRIMARY, EditorTheme.FONT_MD);
        info.AddChild(nameLabel);

        string typeStr = zone.Type switch
        {
            ZoneType.Safe => "Segura",
            ZoneType.PvP => "PvP",
            ZoneType.Dungeon => "Dungeon",
            ZoneType.Arena => "Arena",
            _ => "Neutral"
        };
        var detailLabel = EditorTheme.MakeLabel(
            $"{typeStr} | ({zone.X1},{zone.Y1}) → ({zone.X2},{zone.Y2})",
            EditorTheme.TEXT_SECONDARY, EditorTheme.FONT_XS);
        info.AddChild(detailLabel);

        // Spawn count
        int spawnCount = 0;
        if (ZoneData != null)
            foreach (var s in ZoneData.Spawns)
                if (s.ZoneId == zone.Id) spawnCount++;
        if (spawnCount > 0)
        {
            var spawnLabel = EditorTheme.MakeLabel($"NPCs: {spawnCount} spawns",
                EditorTheme.TEXT_SECONDARY, EditorTheme.FONT_XS);
            info.AddChild(spawnLabel);
        }

        // Buttons column
        var buttons = new VBoxContainer();
        buttons.AddThemeConstantOverride("separation", 2);
        hbox.AddChild(buttons);

        int zoneId = zone.Id;
        var editBtn = EditorTheme.MakeButton("Editar", () => { var z = GetZoneById(zoneId); if (z != null) OnEditZone?.Invoke(z); });
        editBtn.CustomMinimumSize = new Vector2(60, 24);
        buttons.AddChild(editBtn);

        var delBtn = EditorTheme.MakeButton("Eliminar", () => DeleteZone(zoneId));
        delBtn.CustomMinimumSize = new Vector2(60, 24);
        buttons.AddChild(delBtn);

        // Click to select/highlight
        panel.GuiInput += (ev) =>
        {
            if (ev is InputEventMouseButton mb && mb.Pressed && mb.ButtonIndex == MouseButton.Left)
            {
                _selectedZoneId = zoneId;
                var z = GetZoneById(zoneId);
                if (z != null) OnZoneSelected?.Invoke(z);
            }
        };

        return panel;
    }

    private void OnAddZone()
    {
        if (ZoneData == null) ZoneData = new MapZoneData();

        int nextId = ZoneData.Zones.Count + 1;

        // Use active selection bounds if available; fall back to a default area
        int x1 = 40, y1 = 40, x2 = 60, y2 = 60;
        if (State?.HasSelection == true)
        {
            x1 = State.SelX1; y1 = State.SelY1;
            x2 = State.SelX2; y2 = State.SelY2;
        }

        var newZone = new ZoneInfo
        {
            Id = nextId,
            Name = $"Zona {nextId}",
            Type = ZoneType.Neutral,
            X1 = x1, Y1 = y1, X2 = x2, Y2 = y2,
        };
        ZoneData.Zones.Add(newZone);
        RebuildList();
        OnZonesChanged?.Invoke();
        OnEditZone?.Invoke(newZone);
    }

    private void DeleteZone(int zoneId)
    {
        if (ZoneData == null) return;
        ZoneData.Zones.RemoveAll(z => z.Id == zoneId);
        ZoneData.Spawns.RemoveAll(s => s.ZoneId == zoneId);
        // Re-number zone IDs
        for (int i = 0; i < ZoneData.Zones.Count; i++)
            ZoneData.Zones[i].Id = i + 1;
        // Fix spawn references
        foreach (var s in ZoneData.Spawns)
        {
            if (s.ZoneId > zoneId) s.ZoneId--;
        }
        RebuildList();
        OnZonesChanged?.Invoke();
    }

    private ZoneInfo? GetZoneById(int id)
    {
        if (ZoneData == null) return null;
        foreach (var z in ZoneData.Zones)
            if (z.Id == id) return z;
        return null;
    }

    /// <summary>Get the currently selected zone ID (-1 if none).</summary>
    public int SelectedZoneId => _selectedZoneId;

    /// <summary>Get zone type color for overlays and cards.</summary>
    public static Color GetZoneTypeColor(ZoneType type) => type switch
    {
        ZoneType.Safe => new Color(0.2f, 0.8f, 0.3f),      // green
        ZoneType.PvP => new Color(0.9f, 0.2f, 0.2f),       // red
        ZoneType.Dungeon => new Color(0.6f, 0.3f, 0.8f),    // purple
        ZoneType.Arena => new Color(0.9f, 0.7f, 0.1f),      // gold
        _ => new Color(0.5f, 0.5f, 0.5f),                   // gray (neutral)
    };
}
