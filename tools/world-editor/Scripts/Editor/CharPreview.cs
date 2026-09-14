#nullable enable
using System;
using AOWorldEditor.Data;

namespace AOWorldEditor.Editor;

/// <summary>
/// Movement + walk-animation state for the player character shown inside the main
/// editor viewport. Plain data, no Node: MapViewport ticks and draws it, EditorMain
/// places it. The numbers are the VB6 ones WalkModePanel already uses (200 px/s,
/// one animation frame per 8 px travelled), so the preview walks exactly like the
/// full Modo Caminata simulation.
/// </summary>
public sealed class CharPreview
{
    public const int TileSize = 32;
    public const float PixelsPerSecond = 200f;
    public const float ScrollPixels = 8f;

    // What the player sees at 800x600 (client ResolutionManager: 17x13 tiles).
    public const int HalfViewTilesX = 8;
    public const int HalfViewTilesY = 6;

    public bool Active;
    public int X, Y;
    public int Heading = 3; // 1=N 2=E 3=S 4=W
    public int BodyIndex = 1;
    public int HeadIndex = 1;

    /// <summary>Remaining pixels to the tile we're moving into (0 = standing still).</summary>
    public float MoveOffsetX, MoveOffsetY;
    public bool IsMoving => MoveOffsetX != 0f || MoveOffsetY != 0f;

    public bool KeyUp, KeyDown, KeyLeft, KeyRight;
    public bool AnyKey => KeyUp || KeyDown || KeyLeft || KeyRight;

    public void PlaceAt(int x, int y)
    {
        X = x;
        Y = y;
        MoveOffsetX = MoveOffsetY = 0f;
    }

    public void ClearKeys() => KeyUp = KeyDown = KeyLeft = KeyRight = false;

    /// <summary>Turn to face, then step if the target tile is inside the map and walkable.</summary>
    public bool TryMove(MapData map, int dx, int dy, int heading)
    {
        Heading = heading;
        int nx = X + dx, ny = Y + dy;
        if (!map.InBounds(nx, ny) || map.Tiles[nx, ny].Blocked) return false;

        X = nx;
        Y = ny;
        // Offset points back at where we came from, and shrinks to zero as we arrive.
        MoveOffsetX = -dx * TileSize;
        MoveOffsetY = -dy * TileSize;
        return true;
    }

    /// <summary>Advance the in-flight step; once landed, take the next step from held keys.</summary>
    public void Tick(MapData map, float delta)
    {
        if (IsMoving)
        {
            float step = PixelsPerSecond * delta;
            MoveOffsetX = MoveToward(MoveOffsetX, step);
            MoveOffsetY = MoveToward(MoveOffsetY, step);
        }
        if (IsMoving) return;

        if (KeyUp) TryMove(map, 0, -1, 1);
        else if (KeyDown) TryMove(map, 0, 1, 3);
        else if (KeyLeft) TryMove(map, -1, 0, 4);
        else if (KeyRight) TryMove(map, 1, 0, 2);
    }

    private static float MoveToward(float value, float step)
    {
        if (value > 0f) return Math.Max(0f, value - step);
        if (value < 0f) return Math.Min(0f, value + step);
        return 0f;
    }

    /// <summary>VB6 walk cycle: one frame per ScrollPixels travelled, frame 0 when idle.</summary>
    public int WalkFrame(int frameCount)
    {
        if (!IsMoving || frameCount <= 1) return 0;
        float travelled = TileSize - Math.Abs(MoveOffsetX) - Math.Abs(MoveOffsetY);
        return (int)(travelled / ScrollPixels) % frameCount;
    }
}
