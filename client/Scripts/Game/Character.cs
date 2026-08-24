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

	// Per-character walk animation frame. Advances only while Moving.
	//
	// AO2020 (TileEngine_Chars.Char_Move_by_Head) starts the cycle when a
	// character begins to walk and freezes it on stopping, so every stride
	// begins on the same foot. It only carries the phase over when the heading
	// changes *mid-walk*, via SyncGrhPhase — turning a corner must not restart
	// the legs.
	public float WalkFrame;

	/// <summary>
	/// Heading the current walk cycle belongs to, so a turn can be told apart
	/// from starting to walk. 0 means no cycle is running.
	/// </summary>
	public int WalkFrameHeading;

	/// <summary>
	/// Per-character speed multiplier (AO2020 charlist().Speeding). 1.0 is
	/// normal walking pace.
	/// </summary>
	public float Speeding = 1f;

	// Time-based translation, AO2020's TranslateCharacterToPos. Used when the
	// character has to cover a distance in a fixed time rather than at walking
	// speed — a server position correction, above all. Snapping straight to the
	// new tile reads as a teleport; sliding there reads as a stumble.
	public bool TranslationActive;
	public float TranslationElapsedMs;
	public float TranslationTimeMs;
	public float TranslationFromX;
	public float TranslationFromY;

	/// <summary>
	/// Slides to a new tile over <paramref name="timeMs"/>. The offset starts at
	/// the full distance back to where the character was and is interpolated to
	/// zero, so the sprite appears to travel while the logical position is
	/// already the new one.
	/// </summary>
	public void TranslateTo(int newX, int newY, float timeMs = 200f)
	{
		int diffX = newX - PosX;
		int diffY = newY - PosY;
		PosX = newX;
		PosY = newY;

		if (diffX == 0 && diffY == 0) { TranslationActive = false; return; }

		TranslationFromX = -32f * diffX;
		TranslationFromY = -32f * diffY;
		MoveOffsetX = TranslationFromX;
		MoveOffsetY = TranslationFromY;
		ScrollDirectionX = Math.Sign(diffX);
		ScrollDirectionY = Math.Sign(diffY);

		TranslationActive = true;
		TranslationElapsedMs = 0f;
		TranslationTimeMs = timeMs;
		Moving = false;
	}

	// Combat hit flash — brief color tint when this character takes or deals a blow.
	// Timer counts down in CharRenderer.UpdateCharacterTimers; tint applied in DrawCharacter.
	public const float HitFlashDuration = 0.16f; // seconds
	public float HitFlashTimer;                  // seconds remaining (0 = no flash)
	public bool HitFlashReceived;                // true=took damage (red), false=dealt (bright)

	/// <summary>Start a hit flash. received=true tints red (hurt), false brightens (struck).</summary>
	public void HitFlash(bool received)
	{
		HitFlashTimer = HitFlashDuration;
		HitFlashReceived = received;
	}

	// VB6: .pie — alternates between left/right foot for step sounds
	public bool FootToggle;

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

	// FX (VB6: up to 3 simultaneous)
	public int[] ActiveFxSlots = new int[3]; // FxData indices
	public int[] FxLoops = new int[3];         // -1 = infinite, 0 = done
	public float[] FxFrameCounter = new float[3]; // per-slot frame accumulator

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

	// FOV fade: smooth alpha transition when entering/leaving the core viewport
	// 1.0 = fully visible (inside core), 0.0 = fully invisible (outside core)
	// Starts at 0 so new characters fade in instead of popping
	public float FovAlpha;

	// Dialog WrapText cache — avoids re-wrapping every frame when text hasn't changed
	public string? CachedDialogText;
	public string[]? CachedDialogLines;

}
