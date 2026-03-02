using System;
using System.Collections.Generic;
using Godot;
using TierrasSagradasAO.Data;
using TierrasSagradasAO.Network;

namespace TierrasSagradasAO.Game;

/// <summary>
/// Translates keyboard/mouse input to server packets with VB6-accurate client-side prediction.
/// Movement uses LegalPos check + immediate camera scroll (no server round-trip lag).
///
/// VB6 flow: CheckKeys() → MoveTo() → Char_Move_by_Head() + Engine_MoveScreen()
/// Guard: UserMoving == 0 is the ONLY movement blocker (no timer).
/// Server has NO anti-flood for movement — speed is controlled entirely by client animation.
///
/// Key bindings match VB6 defaults (Teclas.tsao):
///   Ctrl/Space = Attack (1000ms cooldown)
///   Arrows/WASD = Movement
///   L = Refresh position (RPU)
///   T = Drop item (needs slot)
///   U = Use item (needs slot)
///   E = Equip item (needs slot)
/// </summary>
public class InputHandler
{
    private readonly AoTcpClient _tcp;
    private readonly GameState _state;

    // Meditation FX IDs — cleared when player moves
    private static readonly HashSet<int> MeditationFxIds = new()
    {
        4, 5, 6, 16, 42, 43, 44, 45, 103, 104, 105
    };

    // Water tile GRH range (VB6: Layer1 1505-1520 with no Layer2 = water)
    private const int WaterGrhMin = 1505;
    private const int WaterGrhMax = 1520;

    // VB6 map borders (InMapBounds)
    private const int MinXBorder = 9;
    private const int MaxXBorder = 92;
    private const int MinYBorder = 7;
    private const int MaxYBorder = 94;

    // VB6 attack cooldown: tAt = 1000ms
    private const float AttackCooldownMs = 1000f;
    private float _attackTimer;

    // VB6 position refresh cooldown
    private float _refreshTimer;
    private const float RefreshCooldownMs = 2000f;

    // Generic key repeat cooldown (VB6 CheckKeys runs at ~32ms tick rate)
    // Prevent rapid-fire sends when holding a key at 60fps.
    private const float KeyCooldownMs = 300f;
    private float _keyCooldown;

    public InputHandler(AoTcpClient tcp, GameState state)
    {
        _tcp = tcp;
        _state = state;
    }

    public void Process(double delta)
    {
        if (!_state.IsLogged || _state.Paused) return;

        // Block all game input while in commerce/bank mode
        if (_state.Comerciando || _state.Banqueando) return;

        float deltaMs = (float)delta * 1000f;

        // Advance cooldown timers
        if (_attackTimer > 0) _attackTimer -= deltaMs;
        if (_refreshTimer > 0) _refreshTimer -= deltaMs;
        if (_keyCooldown > 0) _keyCooldown -= deltaMs;

        // VB6: paralyzed users can attack and cast spells, only movement is blocked
        if (!_state.UserParalyzed)
        {
            // Decrement PT correction cooldown (blocks moves after server rejected one)
            if (_state.PtCooldownFrames > 0)
            {
                _state.PtCooldownFrames--;
            }
            else if (!_state.UserMoving && _state.PendingMoves < 2)
            {
                // Arrow keys: always available
                if (Input.IsKeyPressed(Key.Up))
                    TryMove(1); // North
                else if (Input.IsKeyPressed(Key.Right))
                    TryMove(2); // East
                else if (Input.IsKeyPressed(Key.Down))
                    TryMove(3); // South
                else if (Input.IsKeyPressed(Key.Left))
                    TryMove(4); // West
                // WASD: only when chat is NOT active
                else if (!_state.ChatActive)
                {
                    if (Input.IsKeyPressed(Key.W))
                        TryMove(1);
                    else if (Input.IsKeyPressed(Key.D))
                        TryMove(2);
                    else if (Input.IsKeyPressed(Key.S))
                        TryMove(3);
                    else if (Input.IsKeyPressed(Key.A))
                        TryMove(4);
                }
            }
        }

        // Everything below is blocked when chat is active (letter keys would type into chat)
        if (_state.ChatActive) return;

        // Attack (VB6: Ctrl key, 1000ms cooldown)
        // VB6: blocked while resting or meditating (CheckKeys → UserDescansar / UserMeditar)
        if (Input.IsKeyPressed(Key.Space) || Input.IsKeyPressed(Key.Ctrl))
        {
            if (_attackTimer <= 0 && !_state.Resting && !_state.Meditating && !_state.Dead)
            {
                _tcp.SendPacket("AT");
                _attackTimer = AttackCooldownMs;
            }
        }

        // All action keys below share a cooldown to prevent rapid-fire when held.
        // VB6 CheckKeys only ran once per ~32ms timer tick; at 60fps we need explicit gating.
        if (_keyCooldown > 0) return;

        // Pick up item (VB6: A key sends AGR — conflicts with WASD, use G instead)
        if (Input.IsKeyPressed(Key.G))
        {
            _tcp.SendPacket("AGR");
            _keyCooldown = KeyCooldownMs;
        }
        // Use item from selected inventory slot (VB6: U key)
        else if (Input.IsKeyPressed(Key.U))
        {
            int slot = _state.SelectedInvSlot;
            if (slot >= 0 && slot < 25)
            {
                _tcp.SendPacket($"USA{slot + 1}"); // 1-based
                _keyCooldown = KeyCooldownMs;
            }
        }
        // Equip item from selected inventory slot (VB6: E key)
        else if (Input.IsKeyPressed(Key.E))
        {
            int slot = _state.SelectedInvSlot;
            if (slot >= 0 && slot < 25)
            {
                _tcp.SendPacket($"EQUI{slot + 1}"); // 1-based
                _keyCooldown = KeyCooldownMs;
            }
        }
        // Drop item from selected inventory slot (VB6: T key → TirarItem)
        else if (Input.IsKeyPressed(Key.T))
        {
            if (_state.ItemSafety)
            {
                _state.ChatMessages.Enqueue(new ChatMessage
                {
                    Text = "Desactiva el seguro de items primero con la tecla '*'",
                    Color = "FF0000"
                });
            }
            else
            {
                int slot = _state.SelectedInvSlot;
                if (slot >= 0 && slot < 25 && _state.Inventory[slot].ObjIndex > 0)
                {
                    if (_state.Inventory[slot].Amount == 1)
                    {
                        // Single item → drop immediately (VB6: TI{slot},{qty} no comma after opcode)
                        _tcp.SendPacket($"TI{slot + 1},1");
                    }
                    else if (_state.Inventory[slot].Amount > 1)
                    {
                        // Multiple items → open quantity dialog (VB6: frmCantidad)
                        _state.DropDialogSlot = slot;
                        _state.DropDialogOpen = true;
                    }
                }
            }
            _keyCooldown = KeyCooldownMs;
        }
        // Toggle names display (VB6: N key — client-side only)
        else if (Input.IsKeyPressed(Key.N))
        {
            _state.ShowNames = !_state.ShowNames;
            _keyCooldown = KeyCooldownMs;
        }
        // Steal/Robo (VB6: R key sends UK<Robar>)
        else if (Input.IsKeyPressed(Key.R))
        {
            _tcp.SendPacket("UK12"); // VB6 eSkill.Robar = 12
            _keyCooldown = KeyCooldownMs;
        }
        // Hide/Stealth (VB6: O key sends UK<Ocultarse>)
        else if (Input.IsKeyPressed(Key.O))
        {
            _tcp.SendPacket("UK9"); // VB6 eSkill.Ocultarse = 9
            _keyCooldown = KeyCooldownMs;
        }
        // Refresh position (VB6: L key sends RPU)
        else if (Input.IsKeyPressed(Key.L))
        {
            if (_refreshTimer <= 0)
            {
                _tcp.SendPacket("RPU");
                _refreshTimer = RefreshCooldownMs;
            }
            _keyCooldown = KeyCooldownMs;
        }
        // Meditate (VB6: F6)
        else if (Input.IsKeyPressed(Key.F6))
        {
            _tcp.SendPacket(";/MEDITAR");
            _keyCooldown = KeyCooldownMs;
        }
        // PvP + Clan safety toggle (VB6: S key sends /SEG — we use F7 since S is WASD)
        else if (Input.IsKeyPressed(Key.F7))
        {
            _tcp.SendPacket("SEG");
            _keyCooldown = KeyCooldownMs;
        }
        // Resurrection safety toggle (VB6: D key sends /SEGR — we use F8 since D is WASD)
        else if (Input.IsKeyPressed(Key.F8))
        {
            _tcp.SendPacket(";/SEGR");
            _keyCooldown = KeyCooldownMs;
        }
    }

    /// <summary>
    /// Attempt to move in given direction with client-side prediction.
    /// VB6: CheckKeys → MoveTo → Char_Move_by_Head + Engine_MoveScreen
    /// </summary>
    private void TryMove(int heading)
    {
        // Direction deltas: 1=N(0,-1), 2=E(1,0), 3=S(0,1), 4=W(-1,0)
        int dx = 0, dy = 0;
        switch (heading)
        {
            case 1: dy = -1; break;
            case 2: dx = 1; break;
            case 3: dy = 1; break;
            case 4: dx = -1; break;
        }

        if (!_state.Characters.TryGetValue(_state.UserCharIndex, out var ch))
            return;

        int newX = ch.PosX + dx;
        int newY = ch.PosY + dy;

        if (LegalPos(newX, newY))
        {
            // Clear meditation FX on self when moving
            for (int i = 0; i < 3; i++)
            {
                if (ch.ActiveFxSlots[i] > 0 && MeditationFxIds.Contains(ch.ActiveFxSlots[i]))
                {
                    ch.ActiveFxSlots[i] = 0;
                    ch.FxLoops[i] = 0;
                    ch.FxFrameCounter[i] = 0;
                }
            }

            // Send movement packet to server
            _tcp.SendPacket($"M{heading}");
            _state.PendingMoves++;

            // VB6 Char_Move_by_Head: update logical position + start animation
            ch.Heading = heading;
            ch.MoveOffsetX = -(dx * 32);
            ch.MoveOffsetY = -(dy * 32);
            ch.ScrollDirectionX = dx;
            ch.ScrollDirectionY = dy;
            ch.Moving = true;
            // VB6: Char_Move_by_Head does NOT reset FrameCounter on consecutive moves.
            // Only reset when starting from standstill to avoid animation stutter at tile boundaries.
            ch.PosX = newX;
            ch.PosY = newY;

            // VB6 Engine_MoveScreen: start camera scroll
            _state.AddToUserPosX = dx;
            _state.AddToUserPosY = dy;
            _state.UserPosX = newX;
            _state.UserPosY = newY;
            _state.UserMoving = true;
            _state.ScreenOffsetX = 0;
            _state.ScreenOffsetY = 0;
        }
        else
        {
            // Blocked tile: just turn, don't move (VB6: only send CHEA if heading changed)
            if (ch.Heading != heading)
            {
                _tcp.SendPacket($"CHEA{heading}");
                ch.Heading = heading;
            }
        }
    }

    /// <summary>
    /// VB6 LegalPos: checks if (x,y) is a valid destination tile.
    /// Must match VB6 EXACTLY to avoid client-server desync.
    /// </summary>
    private bool LegalPos(int x, int y)
    {
        // Map bounds (VB6: MinXBorder..MaxXBorder, MinYBorder..MaxYBorder)
        if (x < MinXBorder || x > MaxXBorder || y < MinYBorder || y > MaxYBorder)
            return false;

        // Need map data for tile checks
        if (_state.MapData == null) return true;

        ref var tile = ref _state.MapData.Tiles[x, y];

        // Blocked tile (VB6: MapData(X,Y).Blocked = 1 And montVol = 0)
        // TODO: add flying mount check when mounts are implemented
        if (tile.Blocked) return false;

        // Character standing there? (VB6: MapData(X,Y).charindex > 0, check not dead)
        foreach (var kvp in _state.Characters)
        {
            if (kvp.Key == _state.UserCharIndex) continue;
            var other = kvp.Value;
            if (other.PosX == x && other.PosY == y && !other.Dead)
                return false;
        }

        // Water checks (VB6: both directions)
        bool isWater = tile.Layer1 >= WaterGrhMin && tile.Layer1 <= WaterGrhMax && tile.Layer2 == 0;
        if (!_state.UserNavigating && isWater)
            return false;  // Can't walk on water without boat
        if (_state.UserNavigating && !isWater)
            return false;  // Can't leave water onto land while on boat

        return true;
    }

    /// <summary>
    /// Convert viewport pixel position to world tile coordinates.
    /// VB6: tX = UserPos.X + mouseX \ 32 - ScaleWidth \ 64
    /// Uses integer division throughout (\ in VB6 = truncating division).
    /// ScaleWidth=534 \ 64 = 8, ScaleHeight=408 \ 64 = 6.
    /// </summary>
    private (int tileX, int tileY) ViewportToTile(Vector2 viewportPos, int userX, int userY)
    {
        int tileX = userX + (int)viewportPos.X / 32 - 8;
        int tileY = userY + (int)viewportPos.Y / 32 - 6;
        return (tileX, tileY);
    }

    /// <summary>
    /// Handle left click → LC packet (VB6: LookatTile — inspect tile).
    /// Called from Main._UnhandledInput with position already relative to game viewport.
    /// </summary>
    public void HandleLeftClick(Vector2 viewportPos, int userX, int userY)
    {
        var (tileX, tileY) = ViewportToTile(viewportPos, userX, userY);
        if (tileX >= 1 && tileX <= 100 && tileY >= 1 && tileY <= 100)
        {
            _tcp.SendPacket($"LC{tileX},{tileY}");
        }
    }

    /// <summary>
    /// Handle right click → RC packet (VB6: Accion — interact with doors, NPCs, users).
    /// Called from Main._UnhandledInput with position already relative to game viewport.
    /// </summary>
    public void HandleRightClick(Vector2 viewportPos, int userX, int userY)
    {
        var (tileX, tileY) = ViewportToTile(viewportPos, userX, userY);
        if (tileX >= 1 && tileX <= 100 && tileY >= 1 && tileY <= 100)
        {
            _tcp.SendPacket($"RC{tileX},{tileY}");
        }
    }

    /// <summary>
    /// Handle spell targeting click → WLC packet.
    /// VB6: Form_Click when UsingSkill > 0 sends WLC{x},{y},{UsingSkill}.
    /// UsingSkill is the SKILL TYPE (Magia=2), NOT the spell slot.
    /// The spell slot is stored server-side via the LH packet.
    /// </summary>
    public void HandleSpellClick(Vector2 viewportPos, int userX, int userY)
    {
        var (tileX, tileY) = ViewportToTile(viewportPos, userX, userY);
        if (tileX >= 1 && tileX <= 100 && tileY >= 1 && tileY <= 100)
        {
            // VB6: SendData "WLC" & tX & "," & tY & "," & UsingSkill
            _tcp.SendPacket($"WLC{tileX},{tileY},{_state.UsingSkill}");
            _state.UsingSkill = 0; // Reset after casting
        }
    }

    /// <summary>
    /// GM Shift+Click → /TELEP YO mapa x y (VB6: teleport to clicked tile).
    /// VB6 uses spaces as separators, not commas.
    /// </summary>
    public void HandleGmTeleport(Vector2 viewportPos, int userX, int userY, int currentMap)
    {
        var (tileX, tileY) = ViewportToTile(viewportPos, userX, userY);
        if (tileX >= 1 && tileX <= 100 && tileY >= 1 && tileY <= 100)
        {
            _tcp.SendPacket($";/TELEP YO {currentMap} {tileX} {tileY}");
        }
    }
}
