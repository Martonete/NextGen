namespace ArgentumNextgen.Game;

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
    public bool Mounted;
    public bool Levitating;

    // VB6: dead character transparency pulsing (TransparenciaBody oscillates 0-100)
    public float TransparenciaBody;  // 0-100, alpha = this + 45
    public bool Llegoalatransp;    // false=increasing, true=decreasing

    // Status effect countdown timers (seconds remaining, 0 = no timer/permanent)
    public int InvisibleCountdown;        // Seconds remaining for spell invisibility
    public float InvisibleMaxCountdown;   // Max seconds (for progress bar ratio)
    public float InvisibleCountdownTimer; // Accumulates deltaMs to tick each second

    // VB6: EmoticonLoops countdown (separate from FX slots)
    public int EmoticonLoops;

    // FX (VB6: up to 3 simultaneous + 1 emoticon)
    public int[] ActiveFxSlots = new int[3]; // FxData indices
    public int[] FxLoops = new int[3];         // -1 = infinite, 0 = done
    public float[] FxFrameCounter = new float[3]; // per-slot frame accumulator
    public int EmoticonIndex;

    // Dialog system (VB6: cDialogos — speech bubble above head)
    public string DialogText = "";
    public string DialogColor = "FFFFFF";
    public long DialogStartMs;       // Environment.TickCount64 when created
    public long DialogDurationMs;    // 3000 + 50 * text.Length
    public float DialogRiseCounter;  // VB6 Sube: 18→0, delta-based (60/sec at VB6 rate)
    public float DialogAlpha;        // VB6 Desvanecimiento: starts 20, +720/sec while rising, -600/sec on fade
    public bool DialogFading;        // VB6 Tiempito: True when lifetime expired, fading out

    // Auras (VB6: 5 equipment slots + 1 NPC aura)
    // Indices into Auras.dat (AurasPJ array). 0 = no aura.
    public int AuraIndexA; // Armor
    public int AuraIndexW; // Weapon
    public int AuraIndexE; // Shield
    public int AuraIndexR; // Ring
    public int AuraIndexC; // Helmet
    public int NpcNumber;  // >0 if this is an NPC (from CC packet field 15)
    public int NpcAura;    // NPC-only aura
    public float AuraAngleA;
    public float AuraAngleW;
    public float AuraAngleE;
    public float AuraAngleR;
    public float AuraAngleC;
    public float NpcAuraAngle;

    // Debug helper
    public bool _debugLogged;
    public bool _equipDebugLogged;
}
