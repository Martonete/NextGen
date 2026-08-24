#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Godot;

namespace AOWorldEditor.Data;

/// <summary>A map's position on the world grid. Column grows east, row grows south.</summary>
public readonly record struct WorldCell(int Col, int Row)
{
    public WorldCell Neighbour(WorldSide side) => side switch
    {
        WorldSide.North => new WorldCell(Col, Row - 1),
        WorldSide.South => new WorldCell(Col, Row + 1),
        WorldSide.West => new WorldCell(Col - 1, Row),
        WorldSide.East => new WorldCell(Col + 1, Row),
        _ => this,
    };

    public override string ToString() => $"{Col},{Row}";
}

public enum WorldSide { North, East, South, West }

public static class WorldSideExtensions
{
    public static WorldSide Opposite(this WorldSide side) => side switch
    {
        WorldSide.North => WorldSide.South,
        WorldSide.South => WorldSide.North,
        WorldSide.West => WorldSide.East,
        _ => WorldSide.West,
    };

    public static string Label(this WorldSide side) => side switch
    {
        WorldSide.North => "Norte",
        WorldSide.South => "Sur",
        WorldSide.West => "Oeste",
        _ => "Este",
    };
}

/// <summary>
/// Which map sits in which cell of the world.
///
/// Nothing else in the project records this: neither .aomap, .dat nor .aozone
/// know that one map lies north of another. Maps are islands joined only by
/// per-tile exits, placed by hand.
///
/// The grid is kept in one file rather than spread across each map's .dat
/// because adjacency is a relation *between* maps — having it in one place is
/// what lets the editor show the whole world and generate the seams.
///
/// Only the editor reads this. The server and client see nothing new: they
/// consume the exits it generates, in the format they already understand.
/// </summary>
public sealed class WorldGrid
{
    public const string FileName = "World.ini";

    /// <summary>Map dimensions the grid assumes, for laying cells out visually.</summary>
    public int CellWidth { get; set; } = 100;
    public int CellHeight { get; set; } = 100;

    private readonly Dictionary<WorldCell, int> _byCell = new();
    private readonly Dictionary<int, WorldCell> _byMap = new();

    public IReadOnlyDictionary<WorldCell, int> Cells => _byCell;
    public int Count => _byCell.Count;

    public int? MapAt(WorldCell cell)
        => _byCell.TryGetValue(cell, out int map) ? map : null;

    public WorldCell? CellOf(int mapNumber)
        => _byMap.TryGetValue(mapNumber, out var cell) ? cell : null;

    public int? NeighbourOf(int mapNumber, WorldSide side)
    {
        var cell = CellOf(mapNumber);
        return cell is null ? null : MapAt(cell.Value.Neighbour(side));
    }

    /// <summary>
    /// Places a map on the grid, removing it from any cell it held before — a
    /// map can only be in one place, and a cell holds only one map.
    /// </summary>
    public void Assign(WorldCell cell, int mapNumber)
    {
        if (mapNumber <= 0) return;

        if (_byMap.TryGetValue(mapNumber, out var previous))
            _byCell.Remove(previous);
        if (_byCell.TryGetValue(cell, out int displaced))
            _byMap.Remove(displaced);

        _byCell[cell] = mapNumber;
        _byMap[mapNumber] = cell;
    }

    public void Clear(WorldCell cell)
    {
        if (!_byCell.TryGetValue(cell, out int mapNumber)) return;
        _byCell.Remove(cell);
        _byMap.Remove(mapNumber);
    }

    /// <summary>Every adjacent pair, each counted once, as (map, side, neighbour).</summary>
    public IEnumerable<(int Map, WorldSide Side, int Neighbour)> Borders()
    {
        // Only east and south: taking all four sides would yield each border
        // twice, once from either map.
        foreach (var (cell, map) in _byCell.OrderBy(kv => kv.Key.Row).ThenBy(kv => kv.Key.Col))
        {
            foreach (var side in new[] { WorldSide.East, WorldSide.South })
            {
                if (MapAt(cell.Neighbour(side)) is int neighbour)
                    yield return (map, side, neighbour);
            }
        }
    }

    /// <summary>Bounding box of the occupied cells, or null when the grid is empty.</summary>
    public (int MinCol, int MinRow, int MaxCol, int MaxRow)? Bounds()
    {
        if (_byCell.Count == 0) return null;
        return (_byCell.Keys.Min(c => c.Col), _byCell.Keys.Min(c => c.Row),
                _byCell.Keys.Max(c => c.Col), _byCell.Keys.Max(c => c.Row));
    }

    // ── Persistence ───────────────────────────────────────────────────────

    public static WorldGrid Load(string initDir)
    {
        var grid = new WorldGrid();
        string path = Path.Combine(initDir, FileName);
        if (!File.Exists(path)) return grid;

        try
        {
            string section = "";
            foreach (string raw in File.ReadAllLines(path))
            {
                string line = raw.Trim();
                if (line.Length == 0 || line[0] == ';' || line[0] == '#') continue;

                if (line[0] == '[')
                {
                    int close = line.IndexOf(']');
                    if (close > 1) section = line[1..close].Trim().ToUpperInvariant();
                    continue;
                }

                int eq = line.IndexOf('=');
                if (eq <= 0) continue;
                string key = line[..eq].Trim();
                string value = line[(eq + 1)..].Trim();

                if (section == "WORLD")
                {
                    if (key.Equals("CellWidth", StringComparison.OrdinalIgnoreCase)
                        && int.TryParse(value, out int w) && w > 0) grid.CellWidth = w;
                    else if (key.Equals("CellHeight", StringComparison.OrdinalIgnoreCase)
                        && int.TryParse(value, out int h) && h > 0) grid.CellHeight = h;
                }
                else if (section == "MAPS")
                {
                    // key is "col,row"
                    var parts = key.Split(',');
                    if (parts.Length != 2) continue;
                    if (!int.TryParse(parts[0].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int col)) continue;
                    if (!int.TryParse(parts[1].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int row)) continue;
                    if (!int.TryParse(value, out int mapNumber) || mapNumber <= 0) continue;
                    grid.Assign(new WorldCell(col, row), mapNumber);
                }
            }
            GD.Print($"[World] {grid.Count} mapas en la grilla ({path})");
        }
        catch (Exception ex)
        {
            GD.PushError($"[World] No se pudo leer {path}: {ex.Message}");
        }
        return grid;
    }

    public void Save(string initDir)
    {
        string path = Path.Combine(initDir, FileName);
        var sb = new StringBuilder();
        sb.AppendLine("; Posicion de cada mapa en el mundo. Lo lee solo el World Editor:");
        sb.AppendLine("; el servidor y el cliente usan las salidas que se generan a partir de esto.");
        sb.AppendLine();
        sb.AppendLine("[WORLD]");
        sb.AppendLine($"CellWidth={CellWidth}");
        sb.AppendLine($"CellHeight={CellHeight}");
        sb.AppendLine();
        sb.AppendLine("[MAPS]");
        sb.AppendLine("; columna,fila = numero de mapa   (columna crece al este, fila al sur)");

        foreach (var (cell, map) in _byCell.OrderBy(kv => kv.Key.Row).ThenBy(kv => kv.Key.Col))
            sb.AppendLine($"{cell.Col},{cell.Row}={map}");

        try
        {
            Directory.CreateDirectory(initDir);
            File.WriteAllText(path, sb.ToString(), Encoding.Latin1);
            GD.Print($"[World] Grilla guardada: {_byCell.Count} mapas -> {path}");
        }
        catch (Exception ex)
        {
            GD.PushError($"[World] No se pudo guardar {path}: {ex.Message}");
        }
    }
}
