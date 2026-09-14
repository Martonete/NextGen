#nullable enable
using System;
using System.Collections.Generic;
using AOWorldEditor.Data;

namespace AOWorldEditor.Editor;

public enum LintKind
{
    NpcOnBlocked,
    ObjOnBlocked,
    ExitMapMissing,
    ExitDestBlocked,
    ExitDestNoGround,
    ExitDestOutOfBounds,
    GroundMissing,
    RoofWithoutIndoor,
}

public sealed record LintIssue(LintKind Kind, int X, int Y, string Message);

/// <summary>
/// "Revisar mapa": the checks a mapper would otherwise only discover in-game. Pure and
/// on demand — one pass over the tiles, destination maps loaded at most once each and
/// never assigned to the editor's own map. Nothing here mutates anything.
/// </summary>
public static class MapLint
{
    public static string Label(LintKind kind) => kind switch
    {
        LintKind.NpcOnBlocked => "NPC sobre tile bloqueado",
        LintKind.ObjOnBlocked => "Objeto sobre tile bloqueado",
        LintKind.ExitMapMissing => "Salida a un mapa que no existe",
        LintKind.ExitDestBlocked => "Salida a un tile bloqueado",
        LintKind.ExitDestNoGround => "Salida a un tile sin capa 1",
        LintKind.ExitDestOutOfBounds => "Salida fuera del mapa destino",
        LintKind.GroundMissing => "Hueco en capa 1 (zona caminable)",
        LintKind.RoofWithoutIndoor => "Techo (capa 4) sin trigger Indoor",
        _ => kind.ToString(),
    };

    public static List<LintIssue> Run(MapData map, string mapDir, HashSet<int> availableMaps)
    {
        var issues = new List<LintIssue>();
        var destCache = new Dictionary<int, MapData?>();

        // The server refuses to walk the outer band (EdgeStitcher margins); a hole
        // there is harmless, so only the walkable frame is checked for missing ground.
        int walkX1 = EdgeStitcher.MarginLeft + 1, walkY1 = EdgeStitcher.MarginTop + 1;
        int walkX2 = map.Width - EdgeStitcher.MarginRight, walkY2 = map.Height - EdgeStitcher.MarginBottom;

        MapData? Dest(int number)
        {
            if (number == map.MapNumber) return map;
            if (destCache.TryGetValue(number, out var cached)) return cached;
            MapData? loaded = null;
            if (availableMaps.Contains(number))
            {
                try { loaded = MapLoader.Load(mapDir, number); }
                catch { loaded = null; }
            }
            destCache[number] = loaded;
            return loaded;
        }

        for (int y = 1; y <= map.Height; y++)
            for (int x = 1; x <= map.Width; x++)
            {
                ref var t = ref map.Tiles[x, y];

                if (t.NpcIndex > 0 && t.Blocked)
                    issues.Add(new LintIssue(LintKind.NpcOnBlocked, x, y, $"NPC {t.NpcIndex} en ({x},{y}) está sobre un tile bloqueado"));

                if (t.ObjIndex > 0 && t.Blocked)
                    issues.Add(new LintIssue(LintKind.ObjOnBlocked, x, y, $"Objeto {t.ObjIndex} en ({x},{y}) está sobre un tile bloqueado"));

                if (t.Layer1 == 0 && x >= walkX1 && x <= walkX2 && y >= walkY1 && y <= walkY2)
                    issues.Add(new LintIssue(LintKind.GroundMissing, x, y, $"({x},{y}) no tiene capa 1 y está en la zona caminable"));

                if (t.Layer4 != 0 && !RoofRegions.IsRoofTrigger(t.Trigger))
                    issues.Add(new LintIssue(LintKind.RoofWithoutIndoor, x, y, $"({x},{y}) tiene techo pero no trigger 1 (Indoor): el techo no se va a ocultar al entrar"));

                if (t.ExitMap > 0)
                {
                    var dest = Dest(t.ExitMap);
                    if (dest == null)
                        issues.Add(new LintIssue(LintKind.ExitMapMissing, x, y, $"Salida en ({x},{y}) lleva al mapa {t.ExitMap}, que no está en la carpeta"));
                    else if (!dest.InBounds(t.ExitX, t.ExitY))
                        issues.Add(new LintIssue(LintKind.ExitDestOutOfBounds, x, y, $"Salida en ({x},{y}) apunta a ({t.ExitX},{t.ExitY}) fuera del mapa {t.ExitMap} ({dest.Width}x{dest.Height})"));
                    else
                    {
                        ref var d = ref dest.Tiles[t.ExitX, t.ExitY];
                        if (d.Blocked)
                            issues.Add(new LintIssue(LintKind.ExitDestBlocked, x, y, $"Salida en ({x},{y}) deja al jugador en un tile bloqueado del mapa {t.ExitMap} ({t.ExitX},{t.ExitY})"));
                        else if (d.Layer1 == 0)
                            issues.Add(new LintIssue(LintKind.ExitDestNoGround, x, y, $"Salida en ({x},{y}) deja al jugador sin piso en el mapa {t.ExitMap} ({t.ExitX},{t.ExitY})"));
                    }
                }
            }

        return issues;
    }
}
