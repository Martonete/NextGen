using System;
using System.Collections.Generic;
using Godot;
using ArgentumNextgen.Data;
using ArgentumNextgen.Game;

namespace ArgentumNextgen.Network;

/// <summary>
/// Dispatches inbound binary packets by opcode to handler methods.
/// 100% binary protocol — no text fallback.
///
/// Infrastructure (fields, buffer, dispatch) lives here.
/// Handler implementations are in partial class files:
///   - PacketHandler.Helpers.cs     — shared utilities (ParseInt, IsDeadHead, etc.)
///   - PacketHandler.Binary.cs      — binary opcode dispatch table
///   - PacketHandler.Auth.cs        — auth/login handlers
///   - PacketHandler.Combat.cs      — combat/stats handlers
///   - PacketHandler.Movement.cs    — map/position/character handlers
///   - PacketHandler.Social.cs      — chat/guild/quest handlers
///   - PacketHandler.Commerce.cs    — commerce/bank/trade handlers
///   - PacketHandler.Inventory.cs   — inventory/spell handlers
/// </summary>
public partial class PacketHandler
{
    private readonly GameState _state;

    /// Callback to load the map immediately when CM is received.
    public Action? OnMapLoad;

    /// Callback to play a sound effect (WAV/MP3) — no spatial positioning (UI sounds, self-actions).
    public Action<int>? OnPlaySound;

    /// Callback to play a spatial sound effect at world coordinates.
    /// Args: (soundId, srcX, srcY) — VB6: Audio.PlayWave(wav, srcX, srcY)
    public Action<int, int, int>? OnPlaySoundAt;

    /// Callback to play music (MIDI/MP3).
    public Action<int>? OnPlayMusic;

    /// Callback to spawn a floating text above a character.
    /// Args: (charIndex, text, colorHex)
    public Action<int, string, string>? OnFloatingText;

    /// Callback to stop all SFX (called on position warp within same map).
    public Action? OnStopSfx;

    // Meditation FX IDs — cleared when character moves
    private static readonly HashSet<int> MeditationFxIds = new()
    {
        4, 5, 6, 16, 42, 43, 44, 45, 103, 104, 105
    };

    /// Set when an unknown binary opcode is encountered — stream is unrecoverable.
    public bool StreamCorrupted { get; private set; }

    /// Receive buffer for accumulating partial binary packets across TCP reads.
    private byte[] _recvBuf = new byte[65536];
    private int _recvStart;
    private int _recvLen;

    /// Reusable ByteQueue for binary packet parsing (avoids per-packet allocation).
    private readonly ByteQueue _reusableBq = new();

    /// Saved self-character aura state across map changes.
    private Character? _savedSelfAuras;

    /// Flag: when true, the next ChangeBankSlot call will clear the bank first.
    /// Set by HandleBinBankInit after each full bank load completes, so the next
    /// bank open starts with a clean slate. Defaults to true for the first open.
    private bool _bankLoadPending = true;

    public PacketHandler(GameState state)
    {
        _state = state;
    }

    /// <summary>
    /// Append incoming TCP data to the receive buffer, compacting if needed.
    /// </summary>
    private void RecvAppend(byte[] data)
    {
        int count = data.Length;
        if (_recvStart + _recvLen + count > _recvBuf.Length)
        {
            if (_recvLen + count > _recvBuf.Length)
            {
                int newSize = Math.Max(_recvBuf.Length * 2, _recvLen + count);
                byte[] newBuf = new byte[newSize];
                Buffer.BlockCopy(_recvBuf, _recvStart, newBuf, 0, _recvLen);
                _recvBuf = newBuf;
            }
            else
            {
                Buffer.BlockCopy(_recvBuf, _recvStart, _recvBuf, 0, _recvLen);
            }
            _recvStart = 0;
        }
        Buffer.BlockCopy(data, 0, _recvBuf, _recvStart + _recvLen, count);
        _recvLen += count;
    }

    /// <summary>
    /// Consume N bytes from the front of the receive buffer.
    /// </summary>
    private void RecvConsume(int n)
    {
        _recvStart += n;
        _recvLen -= n;
    }

    /// <summary>
    /// Process raw binary data from the TCP client.
    /// Accumulates bytes across reads and extracts complete packets.
    /// </summary>
    public void HandleBinaryData(byte[] data)
    {
        RecvAppend(data);

        int safetyLimit = 500;
        while (_recvLen > 0 && safetyLimit-- > 0 && !StreamCorrupted)
        {
            _reusableBq.Wrap(_recvBuf, _recvStart, _recvLen);
            int startPos = _reusableBq.ReadPosition;

            try
            {
                HandleBinaryPacket(_reusableBq);
                int consumed = _reusableBq.ReadPosition - startPos;
                if (consumed <= 0)
                {
                    byte opcode = _recvBuf[_recvStart];
                    GD.PrintErr($"[PKT] Handler consumed 0 bytes for opcode={opcode}, skipping 1 byte");
                    RecvConsume(1);
                }
                else
                {
                    RecvConsume(consumed);
                }
            }
            catch (System.InvalidOperationException)
            {
                break;
            }
        }
        if (safetyLimit <= 0 && _recvLen > 0)
        {
            GD.PrintErr($"[PKT] Safety limit reached, {_recvLen} bytes remaining, first byte={_recvBuf[_recvStart]}");
        }
    }

    // HandleBinaryPacket(ByteQueue) is defined in PacketHandler.Binary.cs (partial class)
}
