// Geometry checks for the world-grid stitcher.
//
// The exits it writes have to line up with two things the engine already
// decides: the walkable range the server enforces (movement.rs walk_min/max)
// and the side filter find_edge_exit applies. Getting either wrong produces
// maps that look connected but trap the player at the border, which is only
// visible by walking them — hence checking the numbers here instead.
using System.IO;
using System.Linq;
using AOWorldEditor.Data;
using Godot;

namespace AOWorldEditor.Test;

internal static class EdgeStitcherTests
{
    /// <summary>
    /// Output goes through GD.Print: this runs inside Godot (the data layer
    /// needs its types), and System.Console is not in scope there.
    /// </summary>
    private static void Log(string line = "") => GD.Print(line);

    // Mirrors server/source/game/handlers/movement.rs:170-173
    private static int WalkMinX => 9;
    private static int WalkMaxX(int width) => width - 8;
    private static int WalkMinY => 7;
    private static int WalkMaxY(int height) => height - 6;

    // Mirrors the on_side filter in movement.rs:200-205
    private static bool OnSideNorth(int y) => y <= 10;
    private static bool OnSideSouth(int y, int height) => y >= height - 9;
    private static bool OnSideWest(int x) => x <= 10;
    private static bool OnSideEast(int x, int width) => x >= width - 9;

    public static int Run()
    {
        int fails = 0;
        void Check(string name, bool ok, string? detail = null)
        {
            Log($"  {(ok ? "OK  " : "FALLA")} {name}{(detail is null ? "" : "  -> " + detail)}");
            if (!ok) fails++;
        }

        var left = new MapData(100, 100) { MapNumber = 1 };
        var right = new MapData(100, 100) { MapNumber = 2 };

        // ── East/west border ──────────────────────────────────────────────
        int written = EdgeStitcher.StitchBorder(left, right, WorldSide.East);
        Check("cose el borde este", written > 0, $"{written} salidas");

        int exitX = 100 - 8;   // 92
        ref var fromTile = ref left.Tiles[exitX, 50];
        Check("la salida apunta al vecino", fromTile.ExitMap == 2, $"M{fromTile.ExitMap}");
        Check("preserva la coordenada transversal", fromTile.ExitY == 50, $"y={fromTile.ExitY}");
        Check("entra por el lado opuesto", fromTile.ExitX == 9, $"x={fromTile.ExitX}");

        // The reciprocal is what makes the crossing work in both directions.
        ref var backTile = ref right.Tiles[9, 50];
        Check("existe la vuelta", backTile.ExitMap == 1, $"M{backTile.ExitMap}");
        Check("la vuelta preserva la coordenada", backTile.ExitY == 50, $"y={backTile.ExitY}");
        Check("la vuelta entra por el este", backTile.ExitX == exitX, $"x={backTile.ExitX}");

        // ── The tiles must sit where the server will accept them ──────────
        Check("la salida esta en rango caminable",
            exitX >= WalkMinX && exitX <= WalkMaxX(100), $"x={exitX}, max={WalkMaxX(100)}");
        Check("la salida pasa el filtro de lado del server",
            OnSideEast(exitX, 100), $"x={exitX} >= {100 - 9}");
        Check("el destino esta en rango caminable",
            fromTile.ExitX >= WalkMinX && fromTile.ExitX <= WalkMaxX(100));
        Check("el destino pasa el filtro oeste", OnSideWest(fromTile.ExitX));

        // ── Every row of the border, not just one ─────────────────────────
        int covered = 0;
        for (int y = WalkMinY; y <= WalkMaxY(100); y++)
            if (left.Tiles[exitX, y].ExitMap == 2 && left.Tiles[exitX, y].ExitY == y) covered++;
        int expected = WalkMaxY(100) - WalkMinY + 1;
        Check("cubre todo el borde caminable", covered == expected, $"{covered}/{expected}");

        // ── Re-stitching must not change anything ─────────────────────────
        int again = EdgeStitcher.StitchBorder(left, right, WorldSide.East);
        Check("recoser no reescribe", again == 0, $"{again} cambios");

        // ── North/south border ────────────────────────────────────────────
        var top = new MapData(100, 100) { MapNumber = 3 };
        var bottom = new MapData(100, 100) { MapNumber = 4 };
        EdgeStitcher.StitchBorder(top, bottom, WorldSide.South);

        int exitY = 100 - 6;   // 94
        ref var southTile = ref top.Tiles[40, exitY];
        Check("sur: apunta al vecino", southTile.ExitMap == 4, $"M{southTile.ExitMap}");
        Check("sur: preserva x", southTile.ExitX == 40, $"x={southTile.ExitX}");
        Check("sur: entra por el norte", southTile.ExitY == 7, $"y={southTile.ExitY}");
        Check("sur: en rango caminable",
            exitY >= WalkMinY && exitY <= WalkMaxY(100), $"y={exitY}, max={WalkMaxY(100)}");
        Check("sur: pasa el filtro de lado", OnSideSouth(exitY, 100), $"y={exitY} >= {100 - 9}");
        Check("norte: el destino pasa el filtro", OnSideNorth(southTile.ExitY));

        ref var northBack = ref bottom.Tiles[40, 7];
        Check("sur: existe la vuelta", northBack.ExitMap == 3 && northBack.ExitY == exitY,
            $"M{northBack.ExitMap} y={northBack.ExitY}");

        // ── Unstitching leaves nothing behind ─────────────────────────────
        int cleared = EdgeStitcher.UnstitchBorder(left, right, WorldSide.East);
        Check("descoser limpia", cleared > 0 && left.Tiles[exitX, 50].ExitMap == 0,
            $"{cleared} limpiados");

        // ── The grid model ────────────────────────────────────────────────
        var grid = new WorldGrid();
        grid.Assign(new WorldCell(0, 0), 1);
        grid.Assign(new WorldCell(1, 0), 2);
        grid.Assign(new WorldCell(0, 1), 3);

        Check("encuentra el vecino este", grid.NeighbourOf(1, WorldSide.East) == 2);
        Check("encuentra el vecino sur", grid.NeighbourOf(1, WorldSide.South) == 3);
        Check("no inventa vecinos", grid.NeighbourOf(1, WorldSide.North) is null);
        Check("cuenta cada borde una vez", grid.Borders().ToList().Count == 2,
            $"{grid.Borders().ToList().Count}");

        // Moving a map must not leave it in two cells at once.
        grid.Assign(new WorldCell(5, 5), 1);
        Check("mover un mapa libera su celda vieja", grid.MapAt(new WorldCell(0, 0)) is null);
        Check("el mapa esta en la celda nueva", grid.MapAt(new WorldCell(5, 5)) == 1);
        Check("un mapa esta en una sola celda", grid.CellOf(1) == new WorldCell(5, 5));

        Log();
        Log(fails == 0 ? "TODO OK" : $"{fails} FALLAS");
        return fails;
    }

    /// <summary>
    /// Stitches two real maps on disk, in a scratch copy, and checks the claim
    /// that matters: only the .aoinf changes. If the .aomap were rewritten too,
    /// every stitch would churn the painted art in git and any rounding bug in
    /// the map writer would quietly corrupt work.
    /// </summary>
    public static int RunFileRoundTrip()
    {
        int fails = 0;
        void Check(string name, bool ok, string? detail = null)
        {
            Log($"  {(ok ? "OK  " : "FALLA")} {name}{(detail is null ? "" : "  -> " + detail)}");
            if (!ok) fails++;
        }

        Log();
        Log("── Cosido sobre archivos reales ──");

        string? source = FindMapDir();
        if (source == null) { Check("encuentra la carpeta de mapas", false); return 1; }

        // A scratch copy: the test must never touch the real maps.
        string work = Path.Combine(Path.GetTempPath(), "ao-stitch-test");
        if (Directory.Exists(work)) Directory.Delete(work, recursive: true);
        Directory.CreateDirectory(work);

        // Two maps that exist as .aomap, copied under fresh numbers so the
        // grid can place them side by side.
        if (!CopyMapAs(source, 1, work, 901) || !CopyMapAs(source, 28, work, 902))
        {
            Check("copia dos mapas de prueba", false, "faltan Mapa1/Mapa28");
            return 1;
        }
        Check("copia dos mapas de prueba", true);

        var aomapBefore = File.ReadAllBytes(Path.Combine(work, "Mapa901.aomap"));
        string aoinfPath = Path.Combine(work, "Mapa901.aoinf");
        byte[]? aoinfBefore = File.Exists(aoinfPath) ? File.ReadAllBytes(aoinfPath) : null;

        var grid = new WorldGrid();
        grid.Assign(new WorldCell(0, 0), 901);
        grid.Assign(new WorldCell(1, 0), 902);

        var result = EdgeStitcher.StitchAll(grid, work, System.Array.Empty<string>());
        Check("cose el borde", result.BordersStitched == 1, $"{result.BordersStitched} bordes");
        Check("escribe salidas", result.ExitsWritten > 0, $"{result.ExitsWritten}");
        Check("sin avisos", result.Warnings.Count == 0,
            result.Warnings.Count == 0 ? null : string.Join(" | ", result.Warnings));

        // The point of writing only .aoinf.
        var aomapAfter = File.ReadAllBytes(Path.Combine(work, "Mapa901.aomap"));
        Check("el .aomap queda intacto", aomapBefore.SequenceEqual(aomapAfter),
            $"{aomapBefore.Length} vs {aomapAfter.Length} bytes");

        var aoinfAfter = File.ReadAllBytes(aoinfPath);
        Check("el .aoinf cambio", aoinfBefore == null || !aoinfBefore.SequenceEqual(aoinfAfter));

        // Reload and confirm the exits survived the write.
        var reloaded = MapLoader.Load(work, 901);
        ref var tile = ref reloaded.Tiles[100 - 8, 50];
        Check("la salida persiste", tile.ExitMap == 902, $"M{tile.ExitMap}");
        Check("persiste la coordenada", tile.ExitY == 50 && tile.ExitX == 9,
            $"({tile.ExitX},{tile.ExitY})");

        // Stitching again must be a no-op, so re-running it is safe.
        var second = EdgeStitcher.StitchAll(grid, work, System.Array.Empty<string>());
        Check("recoser no cambia nada", second.ExitsWritten == 0, $"{second.ExitsWritten}");

        Directory.Delete(work, recursive: true);

        Log();
        Log(fails == 0 ? "ARCHIVOS OK" : $"{fails} FALLAS EN ARCHIVOS");
        return fails;
    }

    /// <summary>Walks up from the project looking for server/maps.</summary>
    private static string? FindMapDir()
    {
        var dir = new DirectoryInfo(ProjectSettings.GlobalizePath("res://"));
        for (int depth = 0; depth < 8 && dir != null; depth++, dir = dir.Parent)
        {
            string candidate = Path.Combine(dir.FullName, "server", "maps");
            if (File.Exists(Path.Combine(candidate, "Mapa1.aomap"))) return candidate;
        }
        return null;
    }

    private static bool CopyMapAs(string sourceDir, int from, string destDir, int to)
    {
        string src = Path.Combine(sourceDir, $"Mapa{from}.aomap");
        if (!File.Exists(src)) return false;

        File.Copy(src, Path.Combine(destDir, $"Mapa{to}.aomap"));
        foreach (string ext in new[] { "aoinf", "dat" })
        {
            string extra = Path.Combine(sourceDir, $"Mapa{from}.{ext}");
            if (File.Exists(extra)) File.Copy(extra, Path.Combine(destDir, $"Mapa{to}.{ext}"));
        }
        return true;
    }
}
