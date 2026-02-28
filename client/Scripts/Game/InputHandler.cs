using Godot;
using TierrasSagradasAO.Network;

namespace TierrasSagradasAO.Game;

/// <summary>
/// Translates keyboard/mouse input to server packets.
/// WASD/Arrows → M1-M4 (movement), Space/Ctrl → AT (attack).
/// </summary>
public class InputHandler
{
    private readonly AoTcpClient _tcp;
    private readonly GameState _state;

    // Movement throttle (matches server anti-flood: 6 ticks × ~40ms = 240ms)
    private double _moveTimer;
    private const double MoveInterval = 0.24; // seconds

    public InputHandler(AoTcpClient tcp, GameState state)
    {
        _tcp = tcp;
        _state = state;
    }

    public void Process(double delta)
    {
        if (!_state.IsLogged || _state.Paused) return;

        _moveTimer -= delta;

        // Movement
        if (_moveTimer <= 0)
        {
            if (Input.IsKeyPressed(Key.W) || Input.IsKeyPressed(Key.Up))
            {
                _tcp.SendPacket("M1"); // North
                _moveTimer = MoveInterval;
            }
            else if (Input.IsKeyPressed(Key.D) || Input.IsKeyPressed(Key.Right))
            {
                _tcp.SendPacket("M2"); // East
                _moveTimer = MoveInterval;
            }
            else if (Input.IsKeyPressed(Key.S) || Input.IsKeyPressed(Key.Down))
            {
                _tcp.SendPacket("M3"); // South
                _moveTimer = MoveInterval;
            }
            else if (Input.IsKeyPressed(Key.A) || Input.IsKeyPressed(Key.Left))
            {
                _tcp.SendPacket("M4"); // West
                _moveTimer = MoveInterval;
            }
        }

        // Attack
        if (Input.IsKeyPressed(Key.Space) || Input.IsKeyPressed(Key.Ctrl))
        {
            _tcp.SendPacket("AT");
        }
    }

    /// <summary>
    /// Handle mouse click → left click packet.
    /// Called from Main._UnhandledInput with position already relative to game viewport.
    /// Game viewport is 534x408 with HalfWindowTileWidth=8, HalfWindowTileHeight=6.
    /// </summary>
    public void HandleClick(Vector2 viewportPos, int userX, int userY)
    {
        // Convert viewport-relative position to tile coordinates
        // Viewport center maps to user position
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
