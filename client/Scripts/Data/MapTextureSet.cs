using System.Collections.Generic;
using ArgentumNextgen.Game;

namespace ArgentumNextgen.Data;

/// <summary>
/// Works out which sheets a map actually draws, so startup and map changes only
/// decode those.
///
/// The client used to preload every sheet in the catalogue — 5692 of them,
/// 800 megapixels — before the player could move. A single map needs about a
/// hundred. The rest were not merely wasted: the texture cache holds 4096
/// entries, so the tail of the preload evicted its own head.
/// </summary>
public static class MapTextureSet
{
    /// <summary>
    /// Sheets referenced by the map's four tile layers, plus the character and
    /// object art the player will meet as soon as the map is on screen.
    /// </summary>
    public static HashSet<int> For(GameData data, MapData? map)
    {
        var sheets = new HashSet<int>();
        if (map == null) return sheets;

        for (int y = 1; y <= map.Height; y++)
        {
            for (int x = 1; x <= map.Width; x++)
            {
                ref var tile = ref map.Tiles[x, y];
                AddGrh(data, sheets, tile.Layer1);
                AddGrh(data, sheets, tile.Layer2);
                AddGrh(data, sheets, tile.Layer3);
                AddGrh(data, sheets, tile.Layer4);
            }
        }
        return sheets;
    }

    /// <summary>
    /// Adds a GRH's sheet. Animations contribute every frame, not just the
    /// first: the others are needed a fraction of a second later, and loading
    /// them mid-animation causes a visible stutter.
    /// </summary>
    public static void AddGrh(GameData data, HashSet<int> sheets, int grhIndex)
    {
        if (grhIndex <= 0 || grhIndex >= data.Grhs.Length) return;

        var grh = data.Grhs[grhIndex];
        if (grh.NumFrames > 1 && grh.Frames != null)
        {
            foreach (int frame in grh.Frames)
            {
                if (frame <= 0 || frame >= data.Grhs.Length) continue;
                int file = data.Grhs[frame].FileNum;
                if (file > 0) sheets.Add(file);
            }
            return;
        }

        if (grh.FileNum > 0) sheets.Add(grh.FileNum);
    }

    /// <summary>
    /// Sheets for the characters currently on screen — their body, head,
    /// helmet, weapon and shield.
    ///
    /// Only the ones actually present: the catalogue holds 513 bodies and 512
    /// heads across ~740 sheets, and preloading all of them costs more than
    /// the whole map. Anyone who appears later is loaded on demand.
    /// </summary>
    public static void AddVisibleCharacters(GameData data, GameState state, HashSet<int> sheets)
    {
        foreach (var ch in state.Characters.Values)
        {
            AddBody(data, sheets, ch.Body);
            AddHead(data, sheets, data.Heads, ch.Head);
            AddHead(data, sheets, data.Cascos, ch.CascoAnim);
            AddWeapon(data, sheets, data.Weapons, ch.WeaponAnim);
            AddWeapon(data, sheets, data.Shields, ch.ShieldAnim);
        }
    }

    private static void AddBody(GameData data, HashSet<int> sheets, int bodyIndex)
    {
        if (bodyIndex <= 0 || bodyIndex >= data.Bodies.Length) return;
        var body = data.Bodies[bodyIndex];
        if (body?.Walk == null) return;
        foreach (int grh in body.Walk) AddGrh(data, sheets, grh);
    }

    private static void AddHead(GameData data, HashSet<int> sheets, HeadData[] table, int index)
    {
        if (index <= 0 || index >= table.Length) return;
        var entry = table[index];
        if (entry?.Head == null) return;
        foreach (int grh in entry.Head) AddGrh(data, sheets, grh);
    }

    private static void AddWeapon(GameData data, HashSet<int> sheets, WeaponAnimDirs[] table, int index)
    {
        if (index <= 0 || index >= table.Length) return;
        var entry = table[index];
        if (entry?.Walk == null) return;
        foreach (int grh in entry.Walk) AddGrh(data, sheets, grh);
    }
}
