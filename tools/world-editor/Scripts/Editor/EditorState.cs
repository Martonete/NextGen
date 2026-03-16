#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using Godot;
using AOWorldEditor.Data;

namespace AOWorldEditor.Editor;

public class EditorState
{
    // Current tool
    public EditorTool ActiveTool = EditorTool.Paint;

    // Active layer for painting (1-4)
    public int ActiveLayer = 1;

    // Selected texture reference for painting
    public TextureRef? SelectedTexture;

    // Raw GRH for painting (set by eyedrop when no TextureRef match)
    public int EyedropGrh;

    // Mosaic offset: shifts the multi-tile pattern alignment on the map
    public int MosaicOffsetX, MosaicOffsetY;

    // Selection rectangle (tile coords, inclusive)
    public bool HasSelection;
    public int SelX1, SelY1, SelX2, SelY2;

    // Clipboard (copied tiles)
    public MapTile[,]? Clipboard;
    public int ClipWidth, ClipHeight;

    // Pending placement: floating preview before committing (paste or move)
    public readonly PendingPlacement Pending = new();

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

    // Selected NPC from palette (for quick-place with NPC tool)
    public int SelectedNpcNumber;

    // Zone system (editor metadata for sub-regions)
    public List<MapZone> Zones { get; } = new();
    public int SelectedZoneIndex = -1;

    // Pick tool state
    public readonly PickState Pick = new();

    // View
    public float Zoom = 1.0f;
    public Vector2 CameraOffset = Vector2.Zero;

    // Hover tile (under cursor)
    public int HoverX, HoverY;
    public bool HoverValid;

    // Dirty state
    public bool IsDirty { get; private set; }

    // Map navigation
    public int CurrentMapNumber;
    public string MapDir = "";
    public HashSet<int> AvailableMaps { get; } = new();

    // Event fired when dirty state changes
    public event Action<bool>? DirtyChanged;

    // Event fired when user wants to follow an exit
    public event Action<int, int, int>? ExitFollowRequested; // mapNum, x, y

    public void RequestExitFollow(int mapNum, int x, int y)
    {
        ExitFollowRequested?.Invoke(mapNum, x, y);
    }

    public void MarkDirty()
    {
        if (!IsDirty)
        {
            IsDirty = true;
            DirtyChanged?.Invoke(true);
        }
    }

    public void ResetDirty()
    {
        if (IsDirty)
        {
            IsDirty = false;
            DirtyChanged?.Invoke(false);
        }
    }

    public void ScanAvailableMaps(string mapDir)
    {
        AvailableMaps.Clear();
        MapDir = mapDir;
        if (!Directory.Exists(mapDir)) return;

        // Scan both .map (legacy) and .aomap (new format)
        foreach (var pattern in new[] { "Mapa*.map", "Mapa*.aomap" })
        {
            foreach (var file in Directory.GetFiles(mapDir, pattern))
            {
                string name = Path.GetFileNameWithoutExtension(file);
                if (name.StartsWith("Mapa", StringComparison.OrdinalIgnoreCase))
                {
                    if (int.TryParse(name.Substring(4), out int num) && num > 0)
                        AvailableMaps.Add(num);
                }
            }
        }
    }

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

// Pick tool state: tracks the entity being dragged
public class PickState
{
    public bool HasPick;
    public int SourceX, SourceY;
    public PickTarget Target;
    public bool IsDragging;
    public int DragX, DragY; // current drag tile position

    public void Clear()
    {
        HasPick = false;
        IsDragging = false;
    }
}

public enum PickTarget
{
    None,
    Layer3,    // tree/object graphic
    Layer4,    // roof
    Npc,
    Object,
    Particle,  // particle group
}

/// Floating tile buffer that the user can reposition before committing.
/// Used by Paste (Ctrl+V) and Move tool release.
public class PendingPlacement
{
    public bool Active;
    public MapTile[,]? Tiles;      // [0..W-1, 0..H-1]
    public int Width, Height;
    public int OriginX, OriginY;   // Current top-left position on the map
    public bool IsMove;            // True if this came from a move (clears source on commit)
    public int SourceX, SourceY;   // Original position of moved tiles (for clearing source)
    public MapTile[,]? MoveSnapshot; // Original map state for move operations

    public void Begin(MapTile[,] tiles, int w, int h, int ox, int oy,
                      bool isMove = false, MapTile[,]? snapshot = null,
                      int srcX = 0, int srcY = 0)
    {
        Active = true;
        Tiles = tiles;
        Width = w;
        Height = h;
        OriginX = ox;
        OriginY = oy;
        IsMove = isMove;
        SourceX = srcX;
        SourceY = srcY;
        MoveSnapshot = snapshot;
    }

    public void Cancel()
    {
        Active = false;
        Tiles = null;
        MoveSnapshot = null;
    }
}

/// Zone metadata for sub-regions within a map (editor-only, serialized in .dat).
public class MapZone
{
    public string Name = "";
    public string Type = "Normal";
    public int X1 = 1, Y1 = 1, X2 = 100, Y2 = 100;
    public int NpcWanderRadius;
    public bool NoMagic;
    public bool NoInvis;
    public bool NoResurrect;
    public bool NoHide;
    public bool NoSummon;
}

public enum EditorTool
{
    Hand,     // Pan: click+drag moves the camera
    Paint,    // Draw: click+drag paints with selected texture
    Erase,    // Erase: click+drag clears active layer
    Select,   // Rectangle select for copy/paste/move
    Move,     // Drag selected rectangle to new position
    Pick,     // Click on entity (L3/NPC/obj) → drag to move
    Fill,     // Flood fill with texture
    Eyedrop,  // Sample GRH from tile
    Block,    // Toggle blocked flag
    Light,    // Place/edit light source (opens properties)
    Exit,     // Place/edit tile exit (opens properties)
    Npc,      // Place NPC (opens properties)
    Object,   // Place Object (opens properties)
    Trigger,  // Set trigger type (opens properties)
}
