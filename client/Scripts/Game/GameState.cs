using System.Collections.Generic;
using TierrasSagradasAO.Data;

namespace TierrasSagradasAO.Game;

public enum Screen { Login, CharSelect, Game }

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
    public List<CharacterPreview> CharacterList = new();
    public int SelectedCharIndex = -1;

    // Map
    public int CurrentMap;
    public string MapName = "";
    public int MapColorR = 200, MapColorG = 200, MapColorB = 200;
    public MapData? MapData;
    public bool NeedMapLoad;

    // User position
    public int UserPosX;
    public int UserPosY;
    public int UserCharIndex;
    public string UserName = "";

    // User status flags
    public bool UserParalyzed;
    public bool UserNavigating;
    public bool UserStopped;
    public int UsingSkill; // VB6: spell slot being targeted (0 = none)
    public bool ChatActive; // VB6: true when chat input is visible/focused

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

    // Friends list
    public List<string> FriendsList = new();

    // Inventory (25 slots)
    public InventorySlot[] Inventory = new InventorySlot[25];

    // Spells (20 slots)
    public SpellSlot[] Spells = new SpellSlot[20];

    // Chat message queue — drained by Main.cs each frame
    public Queue<ChatMessage> ChatMessages = new();

    // Textos.tsao message templates — loaded once, used by PacketHandler for console messages
    public TextMessage[] TextMessages = System.Array.Empty<TextMessage>();

    public GameState()
    {
        for (int i = 0; i < 25; i++)
            Inventory[i] = new InventorySlot();
        for (int i = 0; i < 20; i++)
            Spells[i] = new SpellSlot();
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
