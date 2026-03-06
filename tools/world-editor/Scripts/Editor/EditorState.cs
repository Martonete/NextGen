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

        foreach (var file in Directory.GetFiles(mapDir, "Mapa*.map"))
        {
            string name = Path.GetFileNameWithoutExtension(file);
            if (name.StartsWith("Mapa", StringComparison.OrdinalIgnoreCase))
            {
                if (int.TryParse(name.Substring(4), out int num) && num > 0)
                    AvailableMaps.Add(num);
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

public enum EditorTool
{
    Paint,
    Erase,
    Select,
    Move,
    Fill,
    Eyedrop,
    Block,
    Light,
    Exit,
    Npc,
    Object,
    Trigger,
}
