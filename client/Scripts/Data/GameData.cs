using System;
using ArgentumNextgen.Data.Resources;
using Godot;

namespace ArgentumNextgen.Data;

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
    public WeaponAnimDirs[] Weapons = Array.Empty<WeaponAnimDirs>();
    public WeaponAnimDirs[] Shields = Array.Empty<WeaponAnimDirs>();
    public AuraData[] Auras = Array.Empty<AuraData>();
    public ObjInfo[] Objects = Array.Empty<ObjInfo>();
    public TextMessage[] TextMessages = Array.Empty<TextMessage>();
    public TextureManager? Textures;

    // VB6 bitmap fonts (font1=chat/names, font2=titles, font3=medium)
    public AoFont?[] Fonts = new AoFont?[4]; // index 1-3, 0 unused

    public bool IsLoaded { get; private set; }

    /// <summary>
    /// Load all game data files using the provided resource provider.
    /// </summary>
    public void LoadAll(IResourceProvider resources)
    {
        GD.Print("[DATA] Loading game data...");

        try
        {
            Grhs = GrhLoader.Load(resources);
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[DATA] Failed to load GRH: {ex.Message}");
            Grhs = new GrhData[1];
            Grhs[0] = new GrhData();
        }

        try
        {
            Bodies = BodyLoader.LoadBodies(resources);
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[DATA] Failed to load bodies: {ex.Message}");
            Bodies = new BodyData[1];
            Bodies[0] = new BodyData();
        }

        try
        {
            Heads = BodyLoader.LoadHeads(resources);
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[DATA] Failed to load heads: {ex.Message}");
            Heads = new HeadData[1];
            Heads[0] = new HeadData();
        }

        try
        {
            Cascos = BodyLoader.LoadCascos(resources);
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[DATA] Failed to load cascos: {ex.Message}");
            Cascos = new HeadData[1];
            Cascos[0] = new HeadData();
        }

        try
        {
            Fxs = FxLoader.Load(resources);
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[DATA] Failed to load FX: {ex.Message}");
            Fxs = new FxData[1];
            Fxs[0] = new FxData();
        }

        try
        {
            TextMessages = TextosLoader.Load(resources);
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[DATA] Failed to load Textos: {ex.Message}");
            TextMessages = new TextMessage[1];
        }

        Textures = new TextureManager(resources);

        // Load VB6 bitmap fonts (font1/2/3)
        for (int fi = 1; fi <= 3; fi++)
        {
            // font1 uses .PNG (uppercase), font2/3 use .png (lowercase)
            // Try both casings for cross-platform compatibility
            string datRelPath = $"INIT/Data/font{fi}.dat";
            string pngRelPath = $"INIT/Data/font{fi}.png";
            if (!resources.Exists(pngRelPath))
                pngRelPath = $"INIT/Data/font{fi}.PNG";
            Fonts[fi] = AoFont.Load(resources, datRelPath, pngRelPath);
        }

        try
        {
            Weapons = WeaponShieldLoader.LoadWeapons(resources);
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[DATA] Failed to load weapons: {ex.Message}");
            Weapons = new WeaponAnimDirs[] { new() };
        }

        try
        {
            Shields = WeaponShieldLoader.LoadShields(resources);
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[DATA] Failed to load shields: {ex.Message}");
            Shields = new WeaponAnimDirs[] { new() };
        }

        try
        {
            Auras = AuraLoader.Load(resources);
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[DATA] Failed to load auras: {ex.Message}");
            Auras = new AuraData[] { new() };
        }

        try
        {
            Objects = ObjectLoader.Load(resources);
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[DATA] Failed to load objects: {ex.Message}");
            Objects = new ObjInfo[] { new() };
        }

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
        // If child frame wasn't loaded (FileNum=0), fall back to parent's
        // first-frame data so the tile still renders (better than invisible).
        if (child.FileNum <= 0) return grh;
        return child;
    }
}
