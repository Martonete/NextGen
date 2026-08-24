#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Godot;

namespace AOWorldEditor.Data;

/// <summary>
/// Generates the tile exits that join neighbouring maps of the world grid.
///
/// The engine already carries a player across: <c>find_edge_exit</c>
/// (server/source/game/handlers/movement.rs) works out which side you walked
/// into and warps you to the nearest exit on that side. What it does not do is
/// preserve where along the edge you were — with a single exit per side you
/// always arrive at the same spot.
///
/// So a border is stitched by putting an exit on *every* walkable tile of it,
/// each pointing at the tile directly opposite. Then the nearest exit is always
/// the one straight ahead, and walking across feels like a straight line.
///
/// Both directions are written in one pass. Connecting the way out and
/// forgetting the way back is the easiest mistake to make by hand.
/// </summary>
public static class EdgeStitcher
{
    /// <summary>
    /// Untraversable frame around every map, inherited from VB6. Kept in step
    /// with the server (movement.rs walk_min/max) and the client
    /// (InputHandler.BorderMargin*). Exits go on the first walkable ring, not
    /// on the map's literal edge, because the frame cannot be stood on.
    /// </summary>
    public const int MarginLeft = 9;
    public const int MarginRight = 8;
    public const int MarginTop = 7;
    public const int MarginBottom = 6;

    public sealed record Result(
        int BordersStitched,
        int ExitsWritten,
        List<int> MapsChanged,
        List<string> Warnings);

    /// <summary>
    /// Stitches every border of the grid. Maps are loaded once, edited in
    /// memory and written once, so a map on four borders is not rewritten
    /// four times.
    /// </summary>
    public static Result StitchAll(WorldGrid grid, string mapDir, IEnumerable<string> alsoWriteTo)
    {
        var loaded = new Dictionary<int, MapData>();
        var dirty = new HashSet<int>();
        var warnings = new List<string>();
        int borders = 0, exits = 0;

        MapData? Get(int mapNumber)
        {
            if (loaded.TryGetValue(mapNumber, out var cached)) return cached;
            try
            {
                var map = MapLoader.Load(mapDir, mapNumber);
                loaded[mapNumber] = map;
                return map;
            }
            catch (Exception ex)
            {
                warnings.Add($"Mapa {mapNumber}: no se pudo abrir ({ex.Message})");
                return null;
            }
        }

        foreach (var (mapNumber, side, neighbourNumber) in grid.Borders())
        {
            var map = Get(mapNumber);
            var neighbour = Get(neighbourNumber);
            if (map == null || neighbour == null) continue;

            int written = StitchBorder(map, neighbour, side);
            if (written > 0)
            {
                borders++;
                exits += written;
                dirty.Add(mapNumber);
                dirty.Add(neighbourNumber);
            }
            else
            {
                warnings.Add($"Mapa {mapNumber} {side.Label()} con {neighbourNumber}: sin tiles para conectar");
            }
        }

        foreach (int mapNumber in dirty)
        {
            var map = loaded[mapNumber];
            WriteExitsOnly(mapDir, map);
            foreach (string extra in alsoWriteTo) WriteExitsOnly(extra, map);
        }

        return new Result(borders, exits, dirty.OrderBy(n => n).ToList(), warnings);
    }

    /// <summary>
    /// Joins one border in both directions. Returns how many exits were written.
    /// </summary>
    public static int StitchBorder(MapData map, MapData neighbour, WorldSide side)
    {
        int written = 0;
        foreach (var (from, to) in EdgePairs(map, neighbour, side))
        {
            if (SetExit(map, from, neighbour.MapNumber, to)) written++;
            if (SetExit(neighbour, to, map.MapNumber, from)) written++;
        }
        return written;
    }

    /// <summary>
    /// Removes the exits this border owns, on both maps. Used to unstitch when
    /// a map is taken off the grid, so it is not left pointing at a neighbour
    /// it no longer has.
    /// </summary>
    public static int UnstitchBorder(MapData map, MapData neighbour, WorldSide side)
    {
        int cleared = 0;
        foreach (var (from, to) in EdgePairs(map, neighbour, side))
        {
            if (ClearExitTo(map, from, neighbour.MapNumber)) cleared++;
            if (ClearExitTo(neighbour, to, map.MapNumber)) cleared++;
        }
        return cleared;
    }

    /// <summary>
    /// Tile pairs along a border: each walkable edge tile of <paramref name="map"/>
    /// paired with the tile it should lead to on <paramref name="neighbour"/>.
    ///
    /// The transverse coordinate carries over, so leaving at x=40 arrives at
    /// x=40. Where the maps differ in size the overlap is used, which is what
    /// keeps a short map from producing exits off the end of a long one.
    /// </summary>
    private static IEnumerable<(Vector2I From, Vector2I To)> EdgePairs(
        MapData map, MapData neighbour, WorldSide side)
    {
        switch (side)
        {
            case WorldSide.East:
            {
                int fromX = map.Width - MarginRight;      // last walkable column
                int toX = MarginLeft;                     // first walkable column opposite
                int first = Math.Max(MarginTop, MarginTop);
                int last = Math.Min(map.Height - MarginBottom, neighbour.Height - MarginBottom);
                for (int y = first; y <= last; y++)
                    yield return (new Vector2I(fromX, y), new Vector2I(toX, y));
                break;
            }
            case WorldSide.West:
            {
                int fromX = MarginLeft;
                int toX = neighbour.Width - MarginRight;
                int first = MarginTop;
                int last = Math.Min(map.Height - MarginBottom, neighbour.Height - MarginBottom);
                for (int y = first; y <= last; y++)
                    yield return (new Vector2I(fromX, y), new Vector2I(toX, y));
                break;
            }
            case WorldSide.South:
            {
                int fromY = map.Height - MarginBottom;
                int toY = MarginTop;
                int first = MarginLeft;
                int last = Math.Min(map.Width - MarginRight, neighbour.Width - MarginRight);
                for (int x = first; x <= last; x++)
                    yield return (new Vector2I(x, fromY), new Vector2I(x, toY));
                break;
            }
            default: // North
            {
                int fromY = MarginTop;
                int toY = neighbour.Height - MarginBottom;
                int first = MarginLeft;
                int last = Math.Min(map.Width - MarginRight, neighbour.Width - MarginRight);
                for (int x = first; x <= last; x++)
                    yield return (new Vector2I(x, fromY), new Vector2I(x, toY));
                break;
            }
        }
    }

    /// <summary>Writes one exit. Returns false when nothing changed.</summary>
    private static bool SetExit(MapData map, Vector2I at, int destMap, Vector2I dest)
    {
        if (!map.InBounds(at.X, at.Y)) return false;

        ref var tile = ref map.Tiles[at.X, at.Y];
        if (tile.ExitMap == (short)destMap && tile.ExitX == (short)dest.X && tile.ExitY == (short)dest.Y)
            return false; // already stitched, leave the file untouched

        tile.ExitMap = (short)destMap;
        tile.ExitX = (short)dest.X;
        tile.ExitY = (short)dest.Y;
        return true;
    }

    /// <summary>Clears an exit only if it points at the map given.</summary>
    private static bool ClearExitTo(MapData map, Vector2I at, int destMap)
    {
        if (!map.InBounds(at.X, at.Y)) return false;

        ref var tile = ref map.Tiles[at.X, at.Y];
        if (tile.ExitMap != (short)destMap) return false;

        tile.ExitMap = 0;
        tile.ExitX = 0;
        tile.ExitY = 0;
        return true;
    }

    /// <summary>
    /// Writes just the .aoinf. Exits live there, so the painted art in .aomap
    /// is never rewritten — stitching shows up in git as a change to one file
    /// per map, not to the whole map.
    /// </summary>
    private static void WriteExitsOnly(string mapDir, MapData map)
    {
        if (!Directory.Exists(mapDir)) return;
        string path = Path.Combine(mapDir, $"Mapa{map.MapNumber}.aoinf");
        try
        {
            File.WriteAllBytes(path, MapLoader.BuildAoInfBytes(map));
        }
        catch (Exception ex)
        {
            GD.PushError($"[World] No se pudo escribir {path}: {ex.Message}");
        }
    }
}
