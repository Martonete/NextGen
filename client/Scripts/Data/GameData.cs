using System;
using Godot;

namespace TierrasSagradasAO.Data;

/// <summary>
/// Singleton holding all loaded game data: GRH, bodies, heads, helmets, FX.
/// Initialized once at startup from binary files.
/// </summary>
public class GameData
{
    public GrhData[] Grhs = Array.Empty<GrhData>();
    public BodyData[] Bodies = Array.Empty<BodyData>();
    public HeadData[] Heads = Array.Empty<HeadData>();
    public HeadData[] Cascos = Array.Empty<HeadData>();
    public FxData[] Fxs = Array.Empty<FxData>();
    public TextureManager? Textures;

    public bool IsLoaded { get; private set; }

    /// <summary>
    /// Load all game data files from the Data/ directory.
    /// </summary>
    public void LoadAll(string dataPath)
    {
        string initPath = System.IO.Path.Combine(dataPath, "INIT");
        string graficosPath = System.IO.Path.Combine(dataPath, "Graficos");

        GD.Print("[DATA] Loading game data...");

        try
        {
            Grhs = GrhLoader.Load(System.IO.Path.Combine(initPath, "Graficos.ind"));
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[DATA] Failed to load GRH: {ex.Message}");
            Grhs = new GrhData[1];
            Grhs[0] = new GrhData();
        }

        try
        {
            Bodies = BodyLoader.LoadBodies(System.IO.Path.Combine(initPath, "Personajes.ind"));
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[DATA] Failed to load bodies: {ex.Message}");
            Bodies = new BodyData[1];
            Bodies[0] = new BodyData();
        }

        try
        {
            Heads = BodyLoader.LoadHeads(System.IO.Path.Combine(initPath, "Cabezas.ind"));
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[DATA] Failed to load heads: {ex.Message}");
            Heads = new HeadData[1];
            Heads[0] = new HeadData();
        }

        try
        {
            Cascos = BodyLoader.LoadCascos(System.IO.Path.Combine(initPath, "Cascos.ind"));
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[DATA] Failed to load cascos: {ex.Message}");
            Cascos = new HeadData[1];
            Cascos[0] = new HeadData();
        }

        try
        {
            Fxs = FxLoader.Load(System.IO.Path.Combine(initPath, "Fxs.ind"));
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[DATA] Failed to load FX: {ex.Message}");
            Fxs = new FxData[1];
            Fxs[0] = new FxData();
        }

        Textures = new TextureManager(graficosPath);
        WeaponShieldLoader.LogInfo();

        IsLoaded = true;
        GD.Print("[DATA] All game data loaded successfully");
    }

    /// <summary>
    /// Resolve an animated GRH to its current frame's GRH data.
    /// </summary>
    public GrhData? ResolveGrh(int grhIndex, int frame = 0)
    {
        if (grhIndex <= 0 || grhIndex >= Grhs.Length) return null;

        var grh = Grhs[grhIndex];
        if (grh.NumFrames <= 1) return grh;

        // Animation: get specific frame
        if (grh.Frames == null || grh.Frames.Length == 0) return grh;
        int frameIdx = frame % grh.Frames.Length;
        int resolvedIdx = grh.Frames[frameIdx];

        if (resolvedIdx <= 0 || resolvedIdx >= Grhs.Length) return grh;

        var child = Grhs[resolvedIdx];
        // Safety: if child GRH wasn't properly loaded (FileNum=0), fall back to
        // parent's first-frame data rather than rendering garbage
        if (child.FileNum <= 0) return grh;
        return child;
    }
}
