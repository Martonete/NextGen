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
    public bool InsertedMapSelection; // True when selection was created by Insert Map
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

    /// <summary>Forces the "Luz avanzada" shader preview to a fixed time of day,
    /// independent of the map's own AmbientR/G/B (which stays untouched on disk).
    /// Default Day so the editor doesn't open looking like night — before this,
    /// LightRenderer's ambient was hardcoded to a night-blue tint with no way to
    /// change it. See LightRenderer.SetAmbient.</summary>
    public LightPreviewMode LightPreview = LightPreviewMode.Day;

    // Tile property editing
    public bool ShowTileProperties;
    public int PropTileX, PropTileY;

    // Selected NPC from palette (for quick-place with NPC tool)
    public int SelectedNpcNumber;

    // Selected Object from palette (for quick-place with Object tool)
    public int SelectedObjectNumber;

    // Selected Particle group (for quick-place with Particle tool)
    public int SelectedParticleGroup = 1;

    // Light tool config
    public int LightR = 255, LightG = 220, LightB = 180;
    public int LightRange = 6;

    // Trigger tool: selected trigger type to paint (0 = erase)
    public short SelectedTriggerType = 1;

    // Fog paint tool: density 0..255 to stamp on tiles (0 = erase)
    public int SelectedFogDensity = 90;
    public bool ShowFog = true;
    /// <summary>Brush radius for the Fog tool — tiles within this many
    /// tiles of the click (Chebyshev distance) are stamped in one click.
    /// 0 = single tile. Set from HumoConfigPanel brushSpin.</summary>
    public int FogBrushRadius = 0;

    /// <summary>Brush radius for Paint/Erase/Block — tiles within this many tiles
    /// of the click (circular, same pattern as FogBrushRadius) are painted in one
    /// click/drag step. 0 = single tile (classic behavior, default). Set from
    /// TilePalette's brush size control.</summary>
    public int PaintBrushRadius = 0;

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

    // Selected tile (Hand tool click)
    public int SelectedTileX = -1;
    public int SelectedTileY = -1;
    public bool HasSelectedTile => SelectedTileX >= 0 && SelectedTileY >= 0;
    public void ClearSelectedTile() { SelectedTileX = -1; SelectedTileY = -1; }

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
        ClearSelectedTile(); // Area selection and tile selection are mutually exclusive
    }

    public void ClearSelection()
    {
        HasSelection = false;
        InsertedMapSelection = false;
    }

    public void CopySelection(MapData map)
    {
        if (!HasSelection) return;
        CopyRegionFrom(map, SelX1, SelY1, SelX2, SelY2);
    }

    /// <summary>
    /// Writes into THIS state's clipboard, reading tiles from a possibly-different
    /// map/selection. Used by the auxiliary map-viewer window's Ctrl+C: the selection
    /// lives in the aux window's own EditorState, but the copied tiles should land in
    /// the main editor's clipboard so the existing Ctrl+V flow (EditorMain.PasteClipboard,
    /// which always reads _state.Clipboard) picks it up without any changes.
    /// </summary>
    public void CopyRegionFrom(MapData map, int x1, int y1, int x2, int y2)
    {
        ClipWidth = x2 - x1 + 1;
        ClipHeight = y2 - y1 + 1;
        Clipboard = new MapTile[ClipWidth + 1, ClipHeight + 1];
        for (int y = 0; y < ClipHeight; y++)
            for (int x = 0; x < ClipWidth; x++)
                if (map.InBounds(x1 + x, y1 + y))
                    Clipboard[x + 1, y + 1] = map.Tiles[x1 + x, y1 + y];
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
    Layer2,    // mask/alpha transition
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
    public bool IsInsert;           // True if this came from Insert Map (shows selection panel after commit)
    public int SourceX, SourceY;   // Original position of moved tiles (for clearing source)
    public MapTile[,]? MoveSnapshot; // Original map state for move operations

    public void Begin(MapTile[,] tiles, int w, int h, int ox, int oy,
                      bool isMove = false, MapTile[,]? snapshot = null,
                      int srcX = 0, int srcY = 0, bool isInsert = false)
    {
        Active = true;
        Tiles = tiles;
        Width = w;
        Height = h;
        OriginX = ox;
        OriginY = oy;
        IsMove = isMove;
        IsInsert = isInsert;
        SourceX = srcX;
        SourceY = srcY;
        MoveSnapshot = snapshot;
    }

    public void Cancel()
    {
        Active = false;
        IsInsert = false;
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
    Select,   // Rectangle select for copy/paste
    Move,     // Drag selected rectangle to new position (activated from selection panel)
    Pick,     // Click on entity (L3/NPC/obj) → drag to move
    Fill,     // Flood fill with texture
    Eyedrop,  // Sample GRH from tile
    Block,    // Toggle blocked flag
    Light,    // Legacy per-tile light: writes to MapTile.LightRange/R/G/B
    LightAdvanced, // New Godot-node based light: MapLight in MapData.LightData

    Exit,     // Place/edit tile exit (opens properties)
    Npc,      // Place NPC (opens properties)
    Object,   // Place Object (opens properties)
    Trigger,  // Set trigger type (opens properties)
    Particle, // Paint particle group on tiles
    Fog,      // Paint per-tile fog blobs (soft world-space fog)
}

/// Time-of-day override for the "Luz avanzada" shader preview (LightRenderer).
/// Editor-only display setting — never written to the map file.
public enum LightPreviewMode
{
    Day,
    Evening,
    Night,
}
