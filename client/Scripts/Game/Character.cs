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

    // Dialog system (VB6: cDialogos — speech bubble above head)
    public string DialogText = "";
    public string DialogColor = "FFFFFF";
    public long DialogStartMs;       // Environment.TickCount64 when created
    public long DialogDurationMs;    // 5000 + 100 * text.Length
    public int DialogRiseCounter;    // VB6 Sube: 18→0, decrements each tick
    public int DialogAlpha;          // VB6 Desvanecimiento: starts 20, +12/frame while Sube>0, -10/frame on fade
    public bool DialogFading;        // VB6 Tiempito: True when lifetime expired, fading out

    // Debug helper
    public bool _debugLogged;
}
