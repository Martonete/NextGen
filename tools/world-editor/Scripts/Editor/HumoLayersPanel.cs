#nullable enable
using System;
using System.Collections.Generic;
using AOWorldEditor.Data;
using Godot;

namespace AOWorldEditor.Editor;

/// <summary>
/// Photoshop-style Humo layers panel — vertical list of painted fog layers
/// shown in the LEFT sidebar when the Humo tool is active. Each row is one
/// layer with: a color swatch, the layer name, tile count, and a delete
/// button. Click a row to activate that layer. A "+ Nueva capa" button at
/// the top creates a new empty layer.
///
/// This is the primary layer management UI. The right-side HumoConfigPanel
/// still has the detailed color/density sliders for the active layer, so
/// users bounce between the two:
///   - Left (this panel): pick which layer to edit, create new, delete
///   - Right (HumoConfigPanel): tune the active layer's color/density/speed
/// </summary>
public partial class HumoLayersPanel : PanelContainer
{
    public MapData? Map;
    /// <summary>Fires whenever layers are added/removed/selected so the
    /// viewport can refresh its fog masks and the right-side panel can
    /// sync to the newly-active layer.</summary>
    public Action? OnLayersChanged;

    private VBoxContainer? _layerList;
    private Button? _newLayerBtn;
    private LineEdit? _newLayerName;

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

        // Header
        var header = EditorTheme.Heading("CAPAS DE HUMO");
        vbox.AddChild(header);

        var help = EditorTheme.MakeLabel(
            "Mezclá colores en el mismo lugar creando varias capas independientes.",
            EditorTheme.TEXT_SECONDARY, EditorTheme.FONT_SM);
        help.AutowrapMode = TextServer.AutowrapMode.Word;
        vbox.AddChild(help);

        // New layer row
        var newRow = new HBoxContainer();
        newRow.AddThemeConstantOverride("separation", 4);
        _newLayerName = new LineEdit
        {
            PlaceholderText = "Nueva capa...",
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
        };
        newRow.AddChild(_newLayerName);
        _newLayerBtn = new Button { Text = "+" };
        _newLayerBtn.Pressed += OnCreateNewLayer;
        newRow.AddChild(_newLayerBtn);
        vbox.AddChild(newRow);

        vbox.AddChild(new HSeparator());

        // Layer list
        _layerList = new VBoxContainer();
        _layerList.AddThemeConstantOverride("separation", 2);
        _layerList.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        vbox.AddChild(_layerList);
    }

    /// <summary>Rebuild the layer list from Map.PaintedFogLayers. Call after
    /// any change (add/remove/rename/select) and on map load.</summary>
    public void Rebuild()
    {
        if (_layerList == null) return;
        foreach (var child in _layerList.GetChildren())
            child.QueueFree();
        if (Map == null) return;

        if (Map.PaintedFogLayers.Count == 0)
        {
            var empty = EditorTheme.MakeLabel("(sin capas — creá una arriba)", EditorTheme.TEXT_SECONDARY, EditorTheme.FONT_SM);
            empty.HorizontalAlignment = HorizontalAlignment.Center;
            _layerList.AddChild(empty);
            return;
        }

        // Draw layers TOP-DOWN in index order (layer 0 on top of the list).
        // Deeper layers render on top of shallower ones — matches Photoshop.
        for (int i = 0; i < Map.PaintedFogLayers.Count; i++)
        {
            var layer = Map.PaintedFogLayers[i];
            var row = BuildLayerRow(layer, i);
            _layerList.AddChild(row);
        }
    }

    private Control BuildLayerRow(PaintedFogLayer layer, int index)
    {
        var row = new PanelContainer();
        bool isActive = Map != null && Map.ActiveFogLayerIndex == index;
        var bg = isActive
            ? EditorTheme.FlatBox(EditorTheme.BG_SELECTED, 4, 6, 3, EditorTheme.ACCENT, 1)
            : EditorTheme.FlatBox(EditorTheme.BG_SECTION, 4, 6, 3, EditorTheme.BORDER, 1);
        row.AddThemeStyleboxOverride("panel", bg);

        var hbox = new HBoxContainer();
        hbox.AddThemeConstantOverride("separation", 6);
        row.AddChild(hbox);

        // Color swatch
        var swatch = new ColorRect
        {
            CustomMinimumSize = new Vector2(24, 24),
            Color = new Color(layer.R / 255f, layer.G / 255f, layer.B / 255f),
        };
        hbox.AddChild(swatch);

        // Name + tile count (click target)
        var labelBox = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        labelBox.AddThemeConstantOverride("separation", 0);
        hbox.AddChild(labelBox);

        var nameLbl = EditorTheme.MakeLabel(layer.Name, EditorTheme.TEXT_PRIMARY, EditorTheme.FONT_SM);
        labelBox.AddChild(nameLbl);

        var tileLbl = EditorTheme.MakeLabel($"{layer.Tiles.Count} tiles", EditorTheme.TEXT_SECONDARY, EditorTheme.FONT_SM);
        labelBox.AddChild(tileLbl);

        // Delete button
        var delBtn = new Button { Text = "🗑", CustomMinimumSize = new Vector2(24, 24) };
        int capturedIndex = index;
        delBtn.Pressed += () => OnDeleteLayer(capturedIndex);
        hbox.AddChild(delBtn);

        // Click the row body to activate this layer
        var clickable = new Button
        {
            Flat = true,
            CustomMinimumSize = new Vector2(0, 28),
        };
        clickable.AddThemeStyleboxOverride("normal", new StyleBoxEmpty());
        clickable.AddThemeStyleboxOverride("hover", new StyleBoxEmpty());
        clickable.AddThemeStyleboxOverride("pressed", new StyleBoxEmpty());
        clickable.MouseFilter = MouseFilterEnum.Pass;
        // Overlay-style: put the button BEHIND the labels so its click area
        // covers the swatch + labels area but delBtn still receives its click.
        // Simpler: handle the row's gui_input event instead.
        row.GuiInput += (@event) =>
        {
            if (@event is InputEventMouseButton mb && mb.Pressed && mb.ButtonIndex == MouseButton.Left)
            {
                SelectLayer(capturedIndex);
                row.AcceptEvent();
            }
        };

        return row;
    }

    private void OnCreateNewLayer()
    {
        if (Map == null) return;
        string name = _newLayerName?.Text?.Trim() ?? "";
        if (string.IsNullOrEmpty(name)) name = $"Capa {Map.PaintedFogLayers.Count + 1}";

        var layer = new PaintedFogLayer
        {
            Name = name,
            Density = 160,
            R = 128, G = 140, B = 160,
            SpeedX = 5, SpeedY = 2,
        };
        Map.PaintedFogLayers.Add(layer);
        Map.ActiveFogLayerIndex = Map.PaintedFogLayers.Count - 1;
        if (_newLayerName != null) _newLayerName.Text = "";
        Rebuild();
        OnLayersChanged?.Invoke();
    }

    private void OnDeleteLayer(int index)
    {
        if (Map == null || index < 0 || index >= Map.PaintedFogLayers.Count) return;
        Map.PaintedFogLayers.RemoveAt(index);
        if (Map.ActiveFogLayerIndex >= Map.PaintedFogLayers.Count)
            Map.ActiveFogLayerIndex = Map.PaintedFogLayers.Count - 1;
        Rebuild();
        OnLayersChanged?.Invoke();
    }

    private void SelectLayer(int index)
    {
        if (Map == null || index < 0 || index >= Map.PaintedFogLayers.Count) return;
        Map.ActiveFogLayerIndex = index;
        Rebuild();
        OnLayersChanged?.Invoke();
    }
}
