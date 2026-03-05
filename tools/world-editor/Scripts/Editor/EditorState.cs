using System;
using System.Collections.Generic;
using Godot;
using AOWorldEditor.Data;

namespace AOWorldEditor.Editor;

/// <summary>
/// Shared editor state: selected tool, layer, texture, selection, etc.
/// </summary>
public class EditorState
{
    // Current tool
    public EditorTool ActiveTool = EditorTool.Paint;

    // Active layer for painting (1-4)
    public int ActiveLayer = 1;

    // Selected texture reference for painting
    public TextureRef? SelectedTexture;

    // Selection rectangle (tile coords, inclusive)
    public bool HasSelection;
    public int SelX1, SelY1, SelX2, SelY2;

    // Clipboard (copied tiles)
    public MapTile[,]? Clipboard;
    public int ClipWidth, ClipHeight;

    // Grid / overlay toggles
    public bool ShowGrid = true;
    public bool ShowBlocked = true;
    public bool ShowExits = true;
    public bool ShowLayer1 = true;
    public bool ShowLayer2 = true;
    public bool ShowLayer3 = true;
    public bool ShowLayer4 = true;
    public bool ShowNpcs = true;
    public bool ShowObjects = true;
    public bool ShowParticles = true;
    public bool ShowLights = true;

    // Tile property editing
    public bool ShowTileProperties;
    public int PropTileX, PropTileY;

    // View
    public float Zoom = 1.0f;
    public Vector2 CameraOffset = Vector2.Zero;

    public void SetSelection(int x1, int y1, int x2, int y2)
    {
        HasSelection = true;
        SelX1 = Math.Min(x1, x2);
        SelY1 = Math.Min(y1, y2);
        SelX2 = Math.Max(x1, x2);
        SelY2 = Math.Max(y1, y2);
    }

    public void ClearSelection()
    {
        HasSelection = false;
    }

    public void CopySelection(MapData map)
    {
        if (!HasSelection) return;
        ClipWidth = SelX2 - SelX1 + 1;
        ClipHeight = SelY2 - SelY1 + 1;
        Clipboard = new MapTile[ClipWidth + 1, ClipHeight + 1];
        for (int y = 0; y < ClipHeight; y++)
            for (int x = 0; x < ClipWidth; x++)
                if (map.InBounds(SelX1 + x, SelY1 + y))
                    Clipboard[x + 1, y + 1] = map.Tiles[SelX1 + x, SelY1 + y];
    }
}

public enum EditorTool
{
    Paint,    // Click to place texture on active layer
    Erase,    // Click to clear active layer
    Select,   // Rectangle selection
    Move,     // Drag selected tiles
    Fill,     // Flood fill with texture
    Eyedrop,  // Pick GRH from map
    Block,    // Toggle blocked flag
    Light,    // Place/edit light source
    Exit,     // Place/edit tile exit
    Npc,      // Place NPC
    Object,   // Place Object
    Trigger,  // Set trigger type
}
