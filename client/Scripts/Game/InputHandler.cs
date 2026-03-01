using System;
using Godot;
using TierrasSagradasAO.Data;
using TierrasSagradasAO.Network;

namespace TierrasSagradasAO.Game;

/// <summary>
/// Translates keyboard/mouse input to server packets with VB6-accurate client-side prediction.
/// Movement uses LegalPos check + immediate camera scroll (no server round-trip lag).
/// </summary>
public class InputHandler
{
    private readonly AoTcpClient _tcp;
    private readonly GameState _state;

    // Water tile GRH range (VB6: Layer1 1505-1520 with no Layer2 = water)
    private const int WaterGrhMin = 1505;
    private const int WaterGrhMax = 1520;

    public InputHandler(AoTcpClient tcp, GameState state)
    {
        _tcp = tcp;
        _state = state;
    }

    public void Process(double delta)
    {
        if (!_state.IsLogged || _state.Paused) return;
        if (_state.UserParalyzed) return;

        // Movement — blocked while camera is still scrolling (VB6: UserMoving guard)
        if (!_state.UserMoving)
        {
            if (Input.IsKeyPressed(Key.W) || Input.IsKeyPressed(Key.Up))
                TryMove(1); // North
            else if (Input.IsKeyPressed(Key.D) || Input.IsKeyPressed(Key.Right))
                TryMove(2); // East
            else if (Input.IsKeyPressed(Key.S) || Input.IsKeyPressed(Key.Down))
                TryMove(3); // South
            else if (Input.IsKeyPressed(Key.A) || Input.IsKeyPressed(Key.Left))
                TryMove(4); // West
        }

        // Attack
        if (Input.IsKeyPressed(Key.Space) || Input.IsKeyPressed(Key.Ctrl))
        {
            _tcp.SendPacket("AT");
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
            case 1: dy = -1; break; // North
            case 2: dx = 1; break;  // East
            case 3: dy = 1; break;  // South
            case 4: dx = -1; break; // West
        }

        if (!_state.Characters.TryGetValue(_state.UserCharIndex, out var ch))
            return;

        int newX = ch.PosX + dx;
        int newY = ch.PosY + dy;

        // Update heading regardless
        ch.Heading = heading;

        if (LegalPos(newX, newY))
        {
            // Send movement packet to server
            _tcp.SendPacket($"M{heading}");

            // Client-side prediction: move character immediately
            // VB6 Char_Move_by_Head: set MoveOffset = -(delta * 32), update logical pos
            ch.MoveOffsetX = -(dx * 32);
            ch.MoveOffsetY = -(dy * 32);
            ch.ScrollDirectionX = dx;
            ch.ScrollDirectionY = dy;
            ch.Moving = true;
            ch.PosX = newX;
            ch.PosY = newY;

            // VB6 Engine_MoveScreen: update camera
            _state.UserPosX = newX;
            _state.UserPosY = newY;
            _state.AddToUserPosX = dx;
            _state.AddToUserPosY = dy;
            _state.UserMoving = true;
            _state.ScreenOffsetX = 0;
            _state.ScreenOffsetY = 0;
        }
        else
        {
            // Heading changed but can't move — notify server
            _tcp.SendPacket($"CHEA{heading}");
        }
    }

    /// <summary>
    /// VB6 LegalPos: checks if (x,y) is a valid destination tile.
    /// - Out of bounds (VB6 InMapBounds: x:9-92, y:7-94)
    /// - Tile is blocked
    /// - Living character already on tile
    /// - Water tile without navigating (Layer1 GRH 1505-1520, no Layer2)
    /// </summary>
    private bool LegalPos(int x, int y)
    {
        // VB6 map bounds
        if (x < 9 || x > 92 || y < 7 || y > 94) return false;

        // Need map data for tile checks
        if (_state.MapData == null) return true;

        ref var tile = ref _state.MapData.Tiles[x, y];

        // Blocked tile
        if (tile.Blocked) return false;

        // Water check: Layer1 is water GRH and no Layer2 overlay
        if (!_state.UserNavigating && tile.Layer1 >= WaterGrhMin && tile.Layer1 <= WaterGrhMax && tile.Layer2 == 0)
            return false;

        // Check for living characters on the target tile
        foreach (var kvp in _state.Characters)
        {
            if (kvp.Key == _state.UserCharIndex) continue;
            var other = kvp.Value;
            if (other.PosX == x && other.PosY == y && !other.Dead && !other.Invisible)
                return false;
        }

        return true;
    }

    /// <summary>
    /// Handle mouse click → left click packet.
    /// Called from Main._UnhandledInput with position already relative to game viewport.
    /// Game viewport is 534x408 with HalfWindowTileWidth=8, HalfWindowTileHeight=6.
    /// </summary>
    public void HandleClick(Vector2 viewportPos, int userX, int userY)
    {
        const int HalfTilesX = 8;
        const int HalfTilesY = 6;
        float centerX = HalfTilesX * 32f;
        float centerY = HalfTilesY * 32f;

        int tileX = userX + (int)((viewportPos.X - centerX) / 32);
        int tileY = userY + (int)((viewportPos.Y - centerY) / 32);

        if (tileX >= 1 && tileX <= 100 && tileY >= 1 && tileY <= 100)
        {
            _tcp.SendPacket($"LC{tileX},{tileY}");
        }
    }
}
