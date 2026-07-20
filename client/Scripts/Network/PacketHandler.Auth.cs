using System;
using Godot;
using ArgentumNextgen.Data;
using ArgentumNextgen.Game;

namespace ArgentumNextgen.Network;

/// <summary>
/// Binary packet handlers: Auth / Login / Account
/// </summary>
public partial class PacketHandler
{

    // ════════════════════════════════════════════════════════════════
    // Individual binary packet handlers
    // ════════════════════════════════════════════════════════════════

    // ── Auth / Login ──────────────────────────────────────────────

    private void HandleBinLogged(ByteQueue bq)
    {
        byte charClass = bq.ReadByte();
        uint coordSeed = (uint)bq.ReadLong();
        _state.IsLogged = true;
        _state.UserClass = charClass;

        // Initialize anti-cheat coordinate cipher with server-provided seed
        _state.CoordCipher = new CoordCipher();
        _state.CoordCipher.Init(coordSeed);
    }


    private void HandleBinDisconnect(ByteQueue bq)
    {
        _state.IsLogged = false;
        OnServerDisconnect?.Invoke("El servidor cerró la conexión.");
    }


    private void HandleBinErrorMsg(ByteQueue bq)
    {
        string msg = bq.ReadString();
        _state.LoginError = msg;
        _state.MensajeText = msg;
    }


    private void HandleBinShowMessageBox(ByteQueue bq)
    {
        string msg = bq.ReadString();
        _state.MensajeText = msg;
    }


    private void HandleBinUserIndex(ByteQueue bq)
    {
        short index = bq.ReadInteger();
        _state.UserIndex = index;
    }


    private void HandleBinUserCharIndex(ByteQueue bq)
    {
        short index = bq.ReadInteger();
        _state.UserCharIndex = index;
    }

    // ── Commerce / Bank ───────────────────────────────────────────


    // ════════════════════════════════════════════════════════════════
    // Handlers added during full-opcode coverage pass
    // ════════════════════════════════════════════════════════════════

    // ── Auth / Login (continued) ──────────────────────────────────

    private void HandleBinShowMessageBox2(ByteQueue bq)
    {
        string msg = bq.ReadString();
        _state.MensajeText = msg;
    }


    private void HandleBinUserIndexAlt(ByteQueue bq)
    {
        short index = bq.ReadInteger();
        _state.UserIndex = index;
    }


    private void HandleBinUserCharIndexAlt(ByteQueue bq)
    {
        short index = bq.ReadInteger();
        _state.UserCharIndex = index;
    }


    private void HandleBinDiceRoll(ByteQueue bq)
    {
        byte str = bq.ReadByte();
        byte agi = bq.ReadByte();
        byte intel = bq.ReadByte();
        byte con = bq.ReadByte();
        byte cha = bq.ReadByte();
        _state.ChatMessages.Enqueue(new ChatMessage
        {
            Text = $"Dados: Fuerza={str} Agilidad={agi} Inteligencia={intel} Constitucion={con} Carisma={cha}",
            Color = "FFFF00"
        });
    }


    private void HandleBinDiceRollAlt(ByteQueue bq)
    {
        byte str = bq.ReadByte();
        byte agi = bq.ReadByte();
        byte intel = bq.ReadByte();
        byte con = bq.ReadByte();
        byte cha = bq.ReadByte();
        _state.ChatMessages.Enqueue(new ChatMessage
        {
            Text = $"Dados: Fuerza={str} Agilidad={agi} Inteligencia={intel} Constitucion={con} Carisma={cha}",
            Color = "FFFF00"
        });
    }


    private void HandleBinAccountData(ByteQueue bq)
    {
        string data = bq.ReadString();
    }

    // ── Movement / Projectiles ────────────────────────────────────

    /// <summary>
    /// Arrow (ID 108) — projectile arrow from src to tgt (FLECHI opcode).
    /// Wire: i16 srcIndex, i16 tgtIndex, i16 grhIndex
    /// </summary>


    // ── Login flow ────────────────────────────────────────────────

    private void HandleBinAddPJ(ByteQueue bq)
    {
        string name = bq.ReadString();
        byte slot = bq.ReadByte();
        short head = bq.ReadInteger();
        short body = bq.ReadInteger();
        short weapon = bq.ReadInteger();
        short shield = bq.ReadInteger();
        short helmet = bq.ReadInteger();
        byte level = bq.ReadByte();
        string charClass = bq.ReadString();
        bool dead = bq.ReadBoolean();
        string race = bq.ReadString();

        var preview = new CharacterPreview
        {
            Name = name,
            Slot = slot,
            Head = head,
            Body = body,
            Weapon = weapon,
            Shield = shield,
            Helmet = helmet,
            Level = level,
            Class = charClass,
            Dead = dead,
            Race = race,
        };
        _state.CharacterList.Add(preview);
    }


    private void HandleBinSecurityCode(ByteQueue bq)
    {
        string code = bq.ReadString();
        _state.SecurityCode = code;
        _state.CurrentScreen = Screen.CharSelect;
    }


    private void HandleBinSetInvisible(ByteQueue bq)
    {
        short charIndex = bq.ReadInteger();
        bool invisible = bq.ReadBoolean();
        short durationSecs = bq.ReadInteger();
        if (_state.Characters.TryGetValue(charIndex, out var ch))
        {
            ch.Invisible = invisible;
            if (invisible)
            {
                // Start pulsing from max alpha fading down
                ch.TransparenciaBody = 53f;
                ch.Llegoalatransp = true;
                ch.InvisibleCountdown = durationSecs;
                ch.InvisibleMaxCountdown = durationSecs;
                ch.InvisibleCountdownTimer = 0f;
            }
            else
            {
                // Becoming visible — reset transparency but preserve movement state
                // (MoveOffsetX/Y, Moving, WalkFrame stay untouched to avoid animation tosqueo)
                ch.TransparenciaBody = 0f;
                ch.Llegoalatransp = false;
                ch.InvisibleCountdown = 0;
                ch.InvisibleMaxCountdown = 0;
                ch.InvisibleCountdownTimer = 0f;
            }
        }
    }


    private void HandleBinInitAccount(ByteQueue bq)
    {
        byte numChars = bq.ReadByte();
        string notice = bq.ReadString();
        _state.CharacterList.Clear();
        _state.ServerNotice = notice;
    }

    // ── Death & status ────────────────────────────────────────────


    // ── Misc stat packets ─────────────────────────────────────────

    private void HandleBinErrorShow(ByteQueue bq)
    {
        string msg = bq.ReadString();
        _state.MensajeText = msg;
    }


    // ── Class options ────────────────────────────────────────────────

    /// <summary>
    /// ClassOptions (ID 144) — level bonus class selection (99 opcode).
    /// Wire: u8 option1, u8 option2
    /// Stub — class bonus UI not yet implemented.
    /// </summary>
    private void HandleBinClassOptions(ByteQueue bq)
    {
        byte opt1 = bq.ReadByte();
        byte opt2 = bq.ReadByte();
    }

    // ── Particles / Lights ───────────────────────────────────────────

    /// <summary>
    /// CharParticleCreate (ID 211) — character particle stream (CFF/PCB).
    /// Wire: i16 charIndex, i16 particleStreamId
    /// particleStreamId=0 clears all character particles.
    /// </summary>

}
