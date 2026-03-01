namespace TierrasSagradasAO.Game;

/// <summary>
/// Represents a visible character (player or NPC) in the game world.
/// Matches VB6 Char type structure.
/// </summary>
public class Character
{
    public int CharIndex;
    public int Body;
    public int Head;
    public int Heading; // 1=N, 2=E, 3=S, 4=W
    public int PosX;
    public int PosY;
    public int WeaponAnim;
    public int ShieldAnim;
    public int CascoAnim;
    public string Name = "";
    public bool Criminal;
    public int Privileges;

    // Smooth movement
    public float MoveOffsetX;
    public float MoveOffsetY;
    public bool Moving;
    public int ScrollDirectionX;
    public int ScrollDirectionY;

    // Per-character walk animation frame counter (VB6: each char has its own FrameCounter).
    // Only advances when Moving=true. Reset to 0 on move start.
    public float WalkFrame;

    // Status
    public bool Dead;
    public bool Invisible;
    public bool Navigating;

    // FX (VB6: up to 3 simultaneous + 1 emoticon)
    public int[] ActiveFxSlots = new int[3]; // FxData indices
    public int EmoticonIndex;

    // Debug helper
    public bool _debugLogged;
}
