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
        _state.IsLogged = true;
        _state.UserClass = charClass;
        GD.Print($"[GAME] Login successful — LOGGED (binary, class={charClass})");
    }


    private void HandleBinDisconnect(ByteQueue bq)
    {
        GD.Print("[GAME] Disconnect received (binary)");
        _state.IsLogged = false;
    }


    private void HandleBinErrorMsg(ByteQueue bq)
    {
        string msg = bq.ReadString();
        _state.LoginError = msg;
        _state.MensajeText = msg;
        GD.Print($"[LOGIN] ERR (binary): {msg}");
    }


    private void HandleBinShowMessageBox(ByteQueue bq)
    {
        string msg = bq.ReadString();
        _state.MensajeText = msg;
        GD.Print($"[GM] Message box (binary): {msg}");
    }


    private void HandleBinUserIndex(ByteQueue bq)
    {
        short index = bq.ReadInteger();
        GD.Print($"[GAME] UserIndexInServer: {index}");
    }


    private void HandleBinUserCharIndex(ByteQueue bq)
    {
        short index = bq.ReadInteger();
        _state.UserCharIndex = index;
        GD.Print($"[GAME] Self char index (binary): {index}");
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
        GD.Print($"[GM] MessageBox2 (binary): {msg}");
    }


    private void HandleBinUserIndexAlt(ByteQueue bq)
    {
        short index = bq.ReadInteger();
        GD.Print($"[GAME] UserIndexAlt: {index}");
    }


    private void HandleBinUserCharIndexAlt(ByteQueue bq)
    {
        short index = bq.ReadInteger();
        _state.UserCharIndex = index;
        GD.Print($"[GAME] UserCharIndexAlt: {index}");
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
        GD.Print($"[PKT] AccountData (binary): {data}");
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
        GD.Print($"[LOGIN] ADDPJ (binary): {name} Lvl {level} ({charClass}) body={body} head={head} weapon={weapon} shield={shield} helmet={helmet}");
    }


    private void HandleBinSecurityCode(ByteQueue bq)
    {
        string code = bq.ReadString();
        _state.SecurityCode = code;
        _state.CurrentScreen = Screen.CharSelect;
        GD.Print("[LOGIN] CODEH (binary): switching to char select");
    }


    private void HandleBinSetInvisible(ByteQueue bq)
    {
        short charIndex = bq.ReadInteger();
        bool invisible = bq.ReadBoolean();
        short durationSecs = bq.ReadInteger();
        if (_state.Characters.TryGetValue(charIndex, out var ch))
        {
            ch.Invisible = invisible;
            // Start from max alpha (135) fading down for a smooth entrance
            ch.TransparenciaBody = invisible ? 53f : 0f;
            ch.Llegoalatransp = invisible;
            // Countdown timer (0 = permanent/GM, >0 = spell seconds remaining)
            ch.InvisibleCountdown = invisible ? durationSecs : 0;
            ch.InvisibleMaxCountdown = invisible ? durationSecs : 0;
            ch.InvisibleCountdownTimer = 0f;
        }
    }


    private void HandleBinInitAccount(ByteQueue bq)
    {
        byte numChars = bq.ReadByte();
        string notice = bq.ReadString();
        _state.CharacterList.Clear();
        _state.ServerNotice = notice;
        GD.Print($"[LOGIN] INIAC (binary): {numChars} chars, notice={notice}");
    }

    // ── Death & status ────────────────────────────────────────────


    // ── Misc stat packets ─────────────────────────────────────────

    private void HandleBinErrorShow(ByteQueue bq)
    {
        string msg = bq.ReadString();
        _state.MensajeText = msg;
        GD.Print($"[GAME] ERO (binary): {msg}");
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
        GD.Print($"[PKT] ClassOptions opt1={opt1} opt2={opt2} (bonus selection UI not yet implemented)");
    }

    // ── Particles / Lights ───────────────────────────────────────────

    /// <summary>
    /// CharParticleCreate (ID 211) — character particle stream (CFF/PCB).
    /// Wire: i16 charIndex, i16 particleStreamId
    /// particleStreamId=0 clears all character particles.
    /// </summary>

}
