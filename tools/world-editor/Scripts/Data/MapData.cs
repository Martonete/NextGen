#nullable enable
namespace AOWorldEditor.Data;

/// <summary>
/// Map tile data. Mirrors the client MapTile but with editor-specific additions.
/// </summary>
public struct MapTile
{
    public bool Blocked;
    public short Layer1;  // Ground terrain GRH
    public short Layer2;  // Mask/overlay GRH (transitions, details)
    public short Layer3;  // Objects/trees GRH
    public short Layer4;  // Roof GRH
    public short Trigger; // 0=None, 1=Indoor, 3=InvalidPos, 4=SafeZone, 5=AntiBlock, 6=CombatZone
    public short ParticleGroup;
    public short LightRange;
    public short LightR;
    public short LightG;
    public short LightB;

    // .inf data
    public short ExitMap;
    public short ExitX;
    public short ExitY;
    public short NpcIndex;
    public short ObjIndex;
    public short ObjAmount;

    public bool HasLight => LightRange > 0;
    public bool HasExit => ExitMap > 0;
    public bool HasNpc => NpcIndex > 0;
    public bool HasObject => ObjIndex > 0;
}

/// <summary>
/// Represents a loaded/editable map with variable dimensions.
/// Standard AO maps are 100x100, but the editor supports larger sizes.
/// Tiles are 1-indexed (VB6 convention): [1..Width, 1..Height].
/// </summary>
public class MapData
{
    public int Width { get; private set; }
    public int Height { get; private set; }
    public MapTile[,] Tiles { get; private set; }

    // Metadata (.dat)
    public string Name = "";
    public int MusicNum;
    public bool PkEnabled; // PK=0 means PvP on (inverted in .dat)
    public bool BackUp = true;
    public string Terreno = "TIERRA";
    public string Zona = "CAMPO";
    public byte AmbientR = 180;
    public byte AmbientG = 180;
    public byte AmbientB = 180;

    public int MapNumber; // The map file number (e.g. 1 for Mapa1.map)

    public MapData(int width = 100, int height = 100)
    {
        Width = width;
        Height = height;
        Tiles = new MapTile[width + 1, height + 1]; // 1-indexed
    }

    /// <summary>
    /// Resize the map, preserving existing tile data where possible.
    /// </summary>
    public void Resize(int newWidth, int newHeight)
    {
        var newTiles = new MapTile[newWidth + 1, newHeight + 1];
        int copyW = System.Math.Min(Width, newWidth);
        int copyH = System.Math.Min(Height, newHeight);

        for (int y = 1; y <= copyH; y++)
            for (int x = 1; x <= copyW; x++)
                newTiles[x, y] = Tiles[x, y];

        Width = newWidth;
        Height = newHeight;
        Tiles = newTiles;
    }

    public bool InBounds(int x, int y) => x >= 1 && x <= Width && y >= 1 && y <= Height;
}
