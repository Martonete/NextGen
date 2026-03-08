using System.Collections.Generic;
using Godot;
using ArgentumNextgen.Data;

namespace ArgentumNextgen.Game;

public enum Screen { Login, CharSelect, CharCreate, AccountCreate, Game }

public class CharacterPreview
{
    public string Name = "", Class = "", Race = "";
    public int Slot, Head, Body, Level;
    public bool Dead;
}

/// <summary>
/// A chat/console message with color for the UI console.
/// </summary>
public class ChatMessage
{
    public string Text = "";
    public string Color = "FFFFFF"; // hex color without #
}

/// <summary>
/// Central game state: player position, characters, map, inventory, stats.
/// Updated by PacketHandler, read by renderers.
/// </summary>
public class GameState
{
    // Login / screen state
    public bool IsLogged;
    public bool Paused;
    public Screen CurrentScreen = Screen.Login;
    public string AccountName = "";
    public string SecurityCode = "";
    public string LoginError = "";
    public string ServerNotice = "";
    public string MensajeText = ""; // VB6 Mensaje form: set to show modal dialog
    public List<CharacterPreview> CharacterList = new();
    public int SelectedCharIndex = -1;

    // Account creation state (AccountCreate screen)
    public string CreateAccountName = "";
    public string CreateAccountPassword = "";
    public string CreateAccountPin = "";

    // Character creation state (CharCreate screen)
    public string CreateCharName = "";
    public int CreateCharRace = 1;    // 1=Humano, 2=Elfo, 3=Elfo Oscuro, 4=Enano, 5=Gnomo
    public int CreateCharGender = 1;  // 1=Hombre, 2=Mujer
    public int CreateCharClass = 1;   // 1-8
    public int CreateCharFaction = 1; // 1=Armada Real, 2=Fuerzas del Caos
    public int CreateCharHead;
    public int CreateCharHeadMin;
    public int CreateCharHeadMax;

    // Map
    public int CurrentMap;
    public string MapName = "";
    public int MapColorR = 160, MapColorG = 160, MapColorB = 160;
    public MapData? MapData;
    public bool NeedMapLoad;

    // User position
    public int UserPosX;
    public int UserPosY;
    public int UserCharIndex;
    public string UserName = "";

    // User status flags
    public bool Raining;
    public bool UserBlind;
    public bool UserDumb;
    public bool IsNight;
    public byte UserClass;
    public bool UserParalyzed;
    public float ParalysisTimer;    // Countdown in seconds from server-provided duration
    public float ParalysisMaxTimer; // Max duration for progress bar ratio
    public ulong PingSentMs;       // VB6: TimerPing(1) — GetTickCount when /PING sent
    public bool UserNavigating;
    public bool UserMounted;
    public bool UserStopped;
    public bool SafeMode;       // VB6: Seguro (PvP safety toggle)
    public bool ItemSafety = true; // VB6: ISItem — client-side drop prevention (toggled with numpad *)
    public bool SeguroResu;     // VB6: SeguroResu — resurrection safety
    public bool Resting;        // VB6: Descansar toggle (DOK)
    // Drop quantity dialog state
    public bool DropDialogOpen; // True when quantity dialog is visible
    public int DropDialogSlot;  // Inventory slot pending drop (0-based)
    public bool Meditating;     // VB6: Meditando
    public bool Dead;           // VB6: UserMuerto
    public bool Trading;        // VB6: Comerciando (player-to-player trade)
    public int UsingSkill; // VB6: spell slot being targeted (0 = none)
    public bool ChatActive; // VB6: true when chat input is visible/focused
    public string ChatModePrefix = ";"; // VB6 modoHabla: ";" normal, "-" yell, "/cmsg " clan, etc.
    public int ChatMode;               // 0=normal, 1=yell, 2=clan, 3=global, 4=party, 5=faction, 6=gm, 7=whisper
    public string WhisperTarget = "";   // Target name for whisper mode
    public bool ShowNames = true;       // VB6: Nombres — toggle character names display

    // User configuration (loaded from Options.ao, applied to renderers/audio)
    public GameConfig Config = new();
    public bool OptionsPanelOpen;
    public bool EscapeMenuOpen;

    // Key bindings (loaded from Teclas.ao, used by InputHandler)
    public KeyBindings Keys = new();
    public bool KeyBindPanelOpen;

    // Macro system (VB6: frmMakro — 10 configurable commands for keys 1-0)
    public string[] Macros = new string[10];
    public bool MacroPanelOpen;

    // Camera scroll state (VB6 client-side prediction)
    public bool UserMoving;        // True while camera is scrolling between tiles
    public int AddToUserPosX;      // Camera scroll direction: -1, 0, or +1
    public int AddToUserPosY;
    public float ScreenOffsetX;    // Camera pixel offset accumulator
    public float ScreenOffsetY;

    // PT correction cooldown: blocks new moves for N frames after a position rejection.
    // Prevents the client from immediately re-sending moves that the server will reject
    // (e.g., NPC on tile that client can't see), which accumulates desync.
    public int PtCooldownFrames;

    // Pending moves counter: tracks unconfirmed client-predicted moves.
    // Caps how far ahead the client can predict (max 2 tiles).
    // Decremented when a scroll animation completes (move assumed accepted).
    // Reset to 0 on PT correction (server rejected and corrected position).
    public int PendingMoves;

    // Characters visible in area
    public Dictionary<int, Character> Characters = new();

    // Ground objects: (x,y) → GRH index
    public Dictionary<(int, int), int> GroundObjects = new();

    // Stats
    public int MaxHp, MinHp;
    public int MaxMana, MinMana;
    public int MaxSta, MinSta;
    public int MaxAgua, MinAgua;
    public int MaxHam, MinHam;
    public int Gold;
    public int Level;
    public int Exp, ExpNext;
    public int Reputation;
    public int Privileges;
    public int MusicId;
    public int OnlineCount;

    // Combat stats (from ANM packet)
    public int Strength;
    public int Agility;
    public int AttackMin, AttackMax;
    public int DefenseMin, DefenseMax;
    public int MagDefMin, MagDefMax;


    // Inventory (25 slots)
    public InventorySlot[] Inventory = new InventorySlot[25];
    public int SelectedInvSlot = -1; // Currently selected inventory slot (0-based, -1 = none)

    // Spells (20 slots)
    public SpellSlot[] Spells = new SpellSlot[20];

    // NPC Commerce (frmComerciar)
    public NpcShopItem[] NpcShopItems = new NpcShopItem[50];
    public int NpcShopCount;
    public bool Comerciando;

    // Travel panel (frmViajar)
    public bool ShowTravelPanel;

    // Death panel (frmMuertito)
    public bool ShowDeathPanel;

    // Bank (frmBanco + frmNuevoBancoObj / Bóveda)
    public BankItem[] BankItems = new BankItem[40];
    public int BankItemCount;
    public long BankGold;
    public bool Banqueando;       // frmBanco is open (gold operations)
    public bool BovedaAbierta;    // frmNuevoBancoObj is open (item vault)

    /// <summary>
    /// True when any modal form/panel is open that should block game input (CheckKeys).
    /// Used by InputHandler and Main._Input to suppress game actions while UI is active.
    /// </summary>
    public bool AnyFormOpen =>
        EscapeMenuOpen || Comerciando || Banqueando || BovedaAbierta
        || MacroPanelOpen || OptionsPanelOpen || KeyBindPanelOpen
        || ShowTravelPanel || Trading || DropDialogOpen;

    // Extended stat fields
    public int CarryBulk;           // StatBulk (ID 127) — current carry weight
    public string SpellInfoText = ""; // SpellInfoResp (ID 148)

    // Crafting lists (smith/carp)
    public string CraftListData = ""; // raw CSV from SmithWeapons/SmithArmors/CarpItems

    // Event / environment
    public string BattleTeamScores = ""; // BattleTeamScores (ID 163)
    public byte AmbientColorR = 200, AmbientColorG = 200, AmbientColorB = 200; // AmbientColor (ID 164)

    // Guild
    public bool GuildBankCanObj;      // Permission: can withdraw items from guild bank
    public bool GuildBankCanGold;     // Permission: can withdraw gold from guild bank
    public int GuildBankGold;         // Current guild bank gold balance
    public bool ShowGuildBank;        // Trigger to open guild bank panel
    public GuildBankSlot[] GuildBankItems = new GuildBankSlot[40];
    public string GuildInfoData = ""; // raw guild info string from server
    public string GuildInfoType = ""; // "Leader", "Member", "Details" — which panel to show
    public string GuildListData = ""; // raw guild list from server
    public bool ShowGuildPanel;       // Trigger to open guild panel
    public bool ShowGuildFoundation;  // Trigger to open guild creation form
    public bool SeguroClan = true;    // Clan safe toggle (local mirror)
    public string UserGuildName = ""; // Current user's guild name (from CC tag)
    public string GuildNewsText = ""; // Guild news from server
    public string GuildMotdText = ""; // Guild MOTD from server
    public string GuildCodexText = ""; // Guild codex from server

    // Mail
    public string MailData = ""; // raw mail data string from server
    public bool ShowMailPanel;   // MailOpenTrigger (ID 252)

    // UI triggers
    public bool ShowTravelsPanel;  // TravelsOpen (ID 251)

    // Right-click context menu (MenuData)
    public string MenuTargetName = "";
    public int MenuTargetPriv;

    // Selection / misc data
    public string SelectData = "";   // SelectData (ID 222)
    public string MiniTopData = "";  // MiniTopData (ID 223)

    // Trading
    public int TradePartnerGold;

    // Raw account data (AccountData ID 76)
    public string RawAccountData = "";

    // Arrow/projectile system (VB6: FLECHI)
    public List<ArrowProjectile> ActiveArrows = new();

    // Particle system
    public ParticleStreamDef[] ParticleDefs = System.Array.Empty<ParticleStreamDef>();
    public List<ParticleStream> MapParticles = new();

    // Light system
    public List<MapLight> MapLights = new();
    public Color AmbientLightColor = new Color(0.627f, 0.627f, 0.627f); // RGB(160,160,160)
    public Color[,,]? TileLightColors; // [x, y, corner(0-3)] — 4 corners per tile matching VB6
    public bool LightsDirty;

    // Chat message queue — drained by Main.cs each frame
    public Queue<ChatMessage> ChatMessages = new();

    // Textos.ao message templates — loaded once, used by PacketHandler for console messages
    public TextMessage[] TextMessages = System.Array.Empty<TextMessage>();

    public GameState()
    {
        for (int i = 0; i < 25; i++)
            Inventory[i] = new InventorySlot();
        for (int i = 0; i < 20; i++)
            Spells[i] = new SpellSlot();
        for (int i = 0; i < 50; i++)
            NpcShopItems[i] = new NpcShopItem();
        for (int i = 0; i < 40; i++)
            BankItems[i] = new BankItem();

        // VB6 default macros (frmMakro defaults from Macro.ao)
        Macros[0] = "/COMERCIAR";
        Macros[1] = "/RESUCITAR";
        Macros[2] = "/CURAR";
        Macros[3] = "/ONLINE";
        Macros[4] = "/GM";
        Macros[5] = "/TORNEO";
        Macros[6] = "/PARTY";
        Macros[7] = "/EST";
        Macros[8] = "";
        Macros[9] = "";
    }
}

public class InventorySlot
{
    public int ObjIndex;
    public string Name = "";
    public int Amount;
    public bool Equipped;
    public int GrhIndex;
    public int ObjType;
    public int MaxHit, MinHit;
    public int MaxDef;
    public int Value;
}

public class SpellSlot
{
    public int SpellId;
    public string Name = "";
}

public class NpcShopItem
{
    public int Slot;       // 1-based server slot
    public string Name = "";
    public int Amount;
    public long Price;     // Server-computed (with discount/inflation)
    public int GrhIndex;
    public int ObjIndex;
    public int ObjType;    // 2=weapon, 3=armor, 16=shield, 17=helmet
    public int MaxHit, MinHit, MaxDef;
}

public class BankItem
{
    public int Slot;       // 1-based server slot
    public int ObjIndex;
    public string Name = "";
    public int Amount;
    public int GrhIndex;
    public int ObjType;
    public int MaxHit, MinHit, MaxDef;
}

/// <summary>
/// Particle effect definition loaded from Particles.ini (VB6: Particulas.bas).
/// </summary>
public class ParticleStreamDef
{
    public string Name = "";
    public int NumParticles;
    public int[] GrhList = System.Array.Empty<int>();
    public int GrhCount;
    public float Angle;                       // initial angle (degrees)
    public float VecX1, VecY1, VecX2, VecY2; // velocity bounds
    public float X1, Y1, X2, Y2;             // spawn position bounds (INI: X1,Y1,X2,Y2)
    public float MoveX1, MoveY1, MoveX2, MoveY2; // per-frame drift bounds (INI: move_x1..move_y2)
    public float LifeMin, LifeMax;
    public float Friction;
    public float Gravity;
    public float GravStrength;
    public float BounceStrength;
    public float Speed;
    public bool Spin;
    public float SpinSpeedL, SpinSpeedH;
    public bool AlphaBlend;
    public bool XMove, YMove;
    public int LifeCounter; // -1 = infinite
    public byte ColR1, ColG1, ColB1; // ColorSet1
    public byte ColR2, ColG2, ColB2; // ColorSet2
    public byte ColR3, ColG3, ColB3; // ColorSet3
    public byte ColR4, ColG4, ColB4; // ColorSet4
}

/// <summary>
/// A single particle instance within a stream.
/// </summary>
public class Particle
{
    public float X, Y;
    public float VelX, VelY;
    public float Life, MaxLife;
    public float Angle;
    public float SpinSpeed;
    public int GrhIndex;
    public float Alpha;
    public bool Alive;
    public byte ColR, ColG, ColB; // chosen color
}

/// <summary>
/// An active particle stream (map-attached or character-attached).
/// </summary>
public class ParticleStream
{
    public int DefIndex;
    public int MapX, MapY;       // tile position (map-attached)
    public int CharIndex = -1;   // character index (char-attached, -1 for map)
    public Particle[] Particles = System.Array.Empty<Particle>();
    public float FrameCounter;   // VB6: frame_counter accumulator (EngineBaseSpeed * deltaMs)
    public int LifeCountdown = -1; // VB6: life_counter — -1 = infinite, >0 = ticks remaining
    public bool Active = true;
}

/// <summary>
/// A light source on the map (from PCL packet).
/// </summary>
public class MapLight
{
    public int X, Y;       // tile position
    public int Range;      // radius in tiles
    public byte R, G, B;   // light color
    public bool Active = true;
}

/// <summary>
/// An arrow/projectile in flight (from FLECHI packet).
/// VB6: renders arrow GRH traveling from shooter to target.
/// </summary>
public class ArrowProjectile
{
    public int ShooterCharIndex;
    public int TargetCharIndex;
    public int GrhIndex;       // arrow graphic
    public float X, Y;        // current pixel position
    public float TargetX, TargetY; // destination pixel
    public float Speed = 8f;  // pixels per frame
    public bool Active = true;
}

/// A slot in the guild bank (VB6: BancoInventarioB).
public class GuildBankSlot
{
    public int ObjIndex;
    public string Name = "";
    public int Amount;
    public int GrhIndex;
    public int ObjType;
    public int MaxHit;
    public int MinHit;
    public int MaxDef;
}
