using System;
using System.Collections.Generic;
using Godot;
using ArgentumNextgen.Data;
using ArgentumNextgen.Game;

namespace ArgentumNextgen.Network;

/// <summary>
/// Dispatches inbound packets by opcode prefix to handler methods.
/// Updates GameState based on server packets.
///
/// Infrastructure (fields, buffer, dispatch) lives here.
/// Handler implementations are in partial class files:
///   - PacketHandler.Helpers.cs     — shared utilities (ParseInt, IsDeadHead, etc.)
///   - PacketHandler.Binary.cs      — binary opcode dispatch table
///   - PacketHandler.Auth.cs        — binary auth/login handlers
///   - PacketHandler.Combat.cs      — binary combat/stats handlers
///   - PacketHandler.Movement.cs    — binary map/position/character handlers
///   - PacketHandler.Social.cs      — binary chat/guild/quest/mail handlers
///   - PacketHandler.Commerce.cs    — binary commerce/bank/trade handlers
///   - PacketHandler.Inventory.cs   — binary inventory/spell handlers
///   - PacketHandler.TextMap.cs     — text map/position/character handlers
///   - PacketHandler.TextStats.cs   — text stats/combat/death/login handlers
///   - PacketHandler.TextChat.cs    — text chat/FX/sound handlers
///   - PacketHandler.TextCommerce.cs — text inventory/commerce/trade handlers
/// </summary>
public partial class PacketHandler
{
    private readonly GameState _state;

    /// Callback to load the map immediately when CM is received.
    public Action? OnMapLoad;

    /// Callback to play a sound effect (WAV/MP3).
    public Action<int>? OnPlaySound;

    /// Callback to play music (MIDI/MP3).
    public Action<int>? OnPlayMusic;

    /// Callback to spawn a floating text above a character.
    /// Args: (charIndex, text, colorHex)
    public Action<int, string, string>? OnFloatingText;

    // Meditation FX IDs — cleared when character moves
    private static readonly HashSet<int> MeditationFxIds = new()
    {
        4, 5, 6, 16, 42, 43, 44, 45, 103, 104, 105
    };

    /// Receive buffer for accumulating partial binary packets across TCP reads.
    private byte[] _recvBuf = new byte[65536];
    private int _recvStart;
    private int _recvLen;

    /// Saved self-character aura state across map changes.
    private Character? _savedSelfAuras;

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
        while (_recvLen > 0 && safetyLimit-- > 0)
        {
            byte opcode = _recvBuf[_recvStart];

            if (opcode == ServerPacketId.GenericText)
            {
                if (_recvLen < 3) break;

                int textLen = _recvBuf[_recvStart + 1] | (_recvBuf[_recvStart + 2] << 8);
                int totalLen = 1 + 2 + textLen;

                if (_recvLen < totalLen) break;

                string text = System.Text.Encoding.Latin1.GetString(
                    _recvBuf, _recvStart + 3, textLen);

                RecvConsume(totalLen);
                HandlePacket(text);
            }
            else
            {
                var bq = new ByteQueue(_recvBuf, _recvStart, _recvLen);
                int startPos = bq.ReadPosition;

                try
                {
                    HandleBinaryPacket(bq);
                    int consumed = bq.ReadPosition - startPos;
                    if (consumed <= 0)
                    {
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
        }
        if (safetyLimit <= 0 && _recvLen > 0)
        {
            GD.PrintErr($"[PKT] Safety limit reached, {_recvLen} bytes remaining, first byte={_recvBuf[_recvStart]}");
        }
    }

    // HandleBinaryPacket(ByteQueue) is defined in PacketHandler.Binary.cs (partial class)

    /// <summary>
    /// Dispatch a text-based packet by its opcode prefix.
    /// Handler methods are defined in PacketHandler.Text*.cs partial files.
    /// </summary>
    public void HandlePacket(string packet)
    {
        if (string.IsNullOrEmpty(packet)) return;

        // Multi-char opcodes first (longest match)
        // ── 15-char opcodes ──
        if (packet == "HOLASOYUNCIRUJA")
        {
            ulong now = Time.GetTicksMsec();
            ulong rtt = now - _state.PingSentMs;
            string lagLabel;
            if (rtt < 100) lagLabel = "0 Lag";
            else if (rtt < 200) lagLabel = "Bajo";
            else if (rtt < 400) lagLabel = "Medio";
            else if (rtt < 900) lagLabel = "Alto";
            else lagLabel = "Injugable";
            _state.ChatMessages.Enqueue(new ChatMessage
            {
                Text = $"<<Recibido: el ping es de {rtt} Mili-Segundos ({rtt / 1000.0:F3} Seg) LAG: {lagLabel}",
                Color = "00FF00"
            });
            return;
        }

        // ── 9-char opcodes ──
        if (packet.StartsWith("INITBANCO"))
        {
            HandleInitBanco();
        }
        else if (packet.StartsWith("FINCBNOK"))
        {
            _state.BovedaAbierta = false;
            GD.Print("[PKT] FINCBNOK: Guild bank closed");
        }
        // ── 8-char opcodes ──
        else if (packet.StartsWith("FINCOMOK"))
        {
            HandleFinComOk();
        }
        else if (packet.StartsWith("FINBANOK"))
        {
            HandleFinBanOk();
        }
        else if (packet.StartsWith("TRADEOK"))
        {
            HandleTradeOk();
        }
        // ── 7-char opcodes ──
        else if (packet.StartsWith("PARADOK"))
        {
            var payload = packet[7..];
            if (payload.Length > 0 && int.TryParse(payload, out int durationSecs))
            {
                _state.UserParalyzed = true;
                _state.ParalysisTimer = durationSecs;
                _state.ParalysisMaxTimer = durationSecs;
            }
            else
            {
                _state.UserParalyzed = false;
                _state.ParalysisTimer = 0f;
                _state.ParalysisMaxTimer = 0f;
            }
        }
        else if (packet.StartsWith("TRANSOK"))
        {
            HandleTransOk(packet[7..]);
        }
        else if (packet.StartsWith("BANCOOK"))
        {
            HandleBancoOk(packet[7..]);
        }
        else if (packet.StartsWith("SHOWFUN"))
        {
            GD.Print("[PKT] SHOWFUN: Guild creation form");
        }
        else if (packet.StartsWith("IREDAEL"))
        {
            GD.Print($"[PKT] IREDAEL: {(packet.Length > 50 ? packet[..50] + "..." : packet)}");
        }
        else if (packet.StartsWith("IREDAEK"))
        {
            GD.Print($"[PKT] IREDAEK: {(packet.Length > 50 ? packet[..50] + "..." : packet)}");
        }
        // ── 6-char opcodes ──
        else if (packet.StartsWith("LOGGED"))
        {
            HandleLogged();
        }
        else if (packet.StartsWith("SEGOFR"))
        {
            _state.SeguroResu = false;
            _state.ChatMessages.Enqueue(new ChatMessage { Text = ">>SEGURO DE RESURRECCION DESACTIVADO<<", Color = "FF0000" });
        }
        else if (packet.StartsWith("SEGOFF"))
        {
            _state.SafeMode = false;
            _state.ChatMessages.Enqueue(new ChatMessage { Text = ">>SEGURO DESACTIVADO<<", Color = "FF0000" });
        }
        else if (packet.StartsWith("FLECHI"))
        {
            HandleArrow(packet[6..]);
        }
        else if (packet.StartsWith("CIRUJA"))
        {
            GD.Print($"[PKT] CIRUJA: {packet[6..]}");
        }
        else if (packet.StartsWith("LSTCRI"))
        {
            GD.Print($"[PKT] LSTCRI: {packet[6..]}");
        }
        // ── 5-char opcodes ──
        else if (packet.StartsWith("INIAC"))
        {
            HandleInitCharList(packet[5..]);
        }
        else if (packet.StartsWith("ADDPJ"))
        {
            HandleAddCharPreview(packet[5..]);
        }
        else if (packet.StartsWith("CODEH"))
        {
            HandleSecurityCode(packet[5..]);
        }
        else if (packet.StartsWith("NAVEG"))
        {
            _state.UserNavigating = !_state.UserNavigating;
        }
        else if (packet.StartsWith("MVOL"))
        {
            HandleMountFly(packet[4..]);
        }
        else if (packet.StartsWith("STOPD"))
        {
            _state.UserStopped = packet.Length > 5 && packet[5] == '1';
        }
        else if (packet.StartsWith("SEGONR"))
        {
            _state.SeguroResu = true;
            _state.ChatMessages.Enqueue(new ChatMessage { Text = ">>SEGURO DE RESURRECCION ACTIVADO<<", Color = "00FF00" });
        }
        else if (packet.StartsWith("SEGON"))
        {
            _state.SafeMode = true;
            _state.ChatMessages.Enqueue(new ChatMessage { Text = ">>SEGURO ACTIVADO<<", Color = "00FF00" });
        }
        else if (packet.StartsWith("NOVER"))
        {
            HandleCharVisibility(packet[5..]);
        }
        else if (packet.StartsWith("MEDOK"))
        {
            HandleMeditationOff();
        }
        else if (packet.StartsWith("MUERT"))
        {
            HandleDeath();
        }
        else if (packet.StartsWith("FINOK"))
        {
            HandleFinOk();
        }
        else if (packet.StartsWith("DADOS"))
        {
            HandleDados(packet[5..]);
        }
        else if (packet.StartsWith("KHEKD"))
        {
            GD.Print($"[PKT] KHEKD: {packet[5..]}");
        }
        else if (packet.StartsWith("ENCHAT"))
        {
        }
        else if (packet.StartsWith("IRCHAT"))
        {
        }
        // ── 4-char opcodes ──
        else if (packet.StartsWith("EHYS"))
        {
            HandleHungerThirst(packet[4..]);
        }
        else if (packet.StartsWith("INVI"))
        {
        }
        else if (packet.StartsWith("NPCR"))
        {
            HandleNpcReset();
        }
        else if (packet.StartsWith("NPCI"))
        {
            HandleNpcItem(packet[4..]);
        }
        else if (packet.StartsWith("NPC|"))
        {
            HandleNpcSlotUpdate(packet[4..]);
        }
        else if (packet.StartsWith("MENU"))
        {
            GD.Print($"[PKT] MENU: {packet[4..]}");
        }
        else if (packet.StartsWith("SELE"))
        {
            GD.Print($"[PKT] SELE: {packet[4..]}");
        }
        else if (packet.StartsWith("DTLC"))
        {
            GD.Print($"[PKT] DTLC: {(packet.Length > 50 ? packet[..50] + "..." : packet)}");
        }
        else if (packet.StartsWith("GINF"))
        {
            GD.Print($"[PKT] GINF: {(packet.Length > 50 ? packet[..50] + "..." : packet)}");
        }
        else if (packet.StartsWith("FEST"))
        {
            GD.Print($"[PKT] FEST: {packet[4..]}");
        }
        else if (packet.StartsWith("ACDA"))
        {
            GD.Print($"[PKT] ACDA: {packet[4..]}");
        }
        else if (packet.StartsWith("MTOP"))
        {
            GD.Print($"[PKT] MTOP: {packet[4..]}");
        }
        else if (packet.StartsWith("ZSOS"))
        {
            GD.Print($"[PKT] ZSOS: {(packet.Length > 50 ? packet[..50] + "..." : packet)}");
        }
        else if (packet.StartsWith("IMEJ"))
        {
            GD.Print($"[PKT] IMEJ: {packet[4..]}");
        }
        // ── 7+ char opcodes (before 3-char to avoid prefix collision) ──
        else if (packet.StartsWith("CANCELTRADE"))
        {
            HandleTradeCancelled();
        }
        else if (packet.StartsWith("DAMEQUEST"))
        {
        }
        else if (packet.StartsWith("INITCOM"))
        {
            HandleInitCom();
        }
        else if (packet.StartsWith("TRAVELS"))
        {
            GD.Print("[PKT] TRAVELS: Travel portals available");
            _state.ShowTravelPanel = true;
        }
        else if (packet.StartsWith("RESPUES"))
        {
            HandleAdminResponse(packet[7..]);
        }
        else if (packet.StartsWith("QTDL"))
        {
            HandleRemoveSelfDialog();
        }
        // ── 3-char opcodes ──
        else if (packet.StartsWith("ERR"))
        {
            HandleError(packet[3..]);
        }
        else if (packet.StartsWith("ERO"))
        {
            string eroText = packet[3..];
            _state.MensajeText = eroText;
            GD.Print($"[GAME] ERO: {eroText}");
        }
        else if (packet.StartsWith("CSI"))
        {
            HandleInventorySlot(packet[3..]);
        }
        else if (packet.StartsWith("SHS"))
        {
            HandleSpellSlot(packet[3..]);
        }
        else if (packet.StartsWith("SHI"))
        {
            HandleHideSpell(packet[3..]);
        }
        else if (packet.StartsWith("TIS"))
        {
        }
        else if (packet.StartsWith("RPT"))
        {
            HandleReputation(packet[3..]);
        }
        else if (packet.StartsWith("LDG"))
        {
            HandlePrivileges(packet[3..]);
        }
        else if (packet.StartsWith("LDM"))
        {
        }
        else if (packet.StartsWith("KFM"))
        {
        }
        else if (packet.StartsWith("DFM"))
        {
        }
        else if (packet.StartsWith("BKW"))
        {
            HandleTogglePause();
        }
        else if (packet.StartsWith("CFX"))
        {
            HandleCharFx(packet[3..]);
        }
        else if (packet.StartsWith("CFE"))
        {
            HandleCharEmoticon(packet[3..]);
        }
        else if (packet.StartsWith("CFF"))
        {
            HandleCharParticle(packet[3..]);
        }
        else if (packet.StartsWith("ANM"))
        {
            HandleEquipmentStats(packet[3..]);
        }
        else if (packet.StartsWith("SBR"))
        {
            HandleBankReset();
        }
        else if (packet.StartsWith("SBO"))
        {
            HandleBankSlot(packet[3..]);
        }
        else if (packet.StartsWith("SFC"))
        {
            GD.Print("[PKT] SFC: Carpentry UI");
        }
        else if (packet.StartsWith("SFH"))
        {
            GD.Print("[PKT] SFH: Blacksmith UI");
        }
        else if (packet.StartsWith("LAH"))
        {
            GD.Print($"[PKT] LAH: {(packet.Length > 50 ? packet[..50] + "..." : packet)}");
        }
        else if (packet.StartsWith("LAR"))
        {
            GD.Print($"[PKT] LAR: {(packet.Length > 50 ? packet[..50] + "..." : packet)}");
        }
        else if (packet.StartsWith("OBR"))
        {
            GD.Print($"[PKT] OBR: {(packet.Length > 50 ? packet[..50] + "..." : packet)}");
        }
        else if (packet.StartsWith("ICO"))
        {
            HandleTradeInit();
        }
        else if (packet.StartsWith("IOR"))
        {
            HandleTradeGold(packet[3..]);
        }
        else if (packet.StartsWith("ICI"))
        {
            HandleTradeItem(packet[3..]);
        }
        else if (packet.StartsWith("VCC"))
        {
            HandleTradeChat(packet[3..]);
        }
        else if (packet.StartsWith("IFO"))
        {
            GD.Print($"[PKT] IFO: {packet[3..]}");
        }
        else if (packet.StartsWith("IDO"))
        {
            GD.Print($"[PKT] IDO: {packet[3..]}");
        }
        else if (packet.StartsWith("IAO"))
        {
            GD.Print($"[PKT] IAO: {packet[3..]}");
        }
        else if (packet.StartsWith("ILO"))
        {
            GD.Print($"[PKT] ILO: {(packet.Length > 50 ? packet[..50] + "..." : packet)}");
        }
        else if (packet.StartsWith("ITO"))
        {
            GD.Print($"[PKT] ITO: {packet[3..]}");
        }
        else if (packet.StartsWith("QTL"))
        {
        }
        else if (packet.StartsWith("MQS"))
        {
        }
        else if (packet.StartsWith("MQC"))
        {
        }
        else if (packet.StartsWith("MFC"))
        {
            GD.Print($"[PKT] MFC: {packet[3..]}");
        }
        else if (packet.StartsWith("GVN"))
        {
            GD.Print($"[PKT] GVN: {packet[3..]}");
        }
        else if (packet.StartsWith("MAR"))
        {
        }
        else if (packet.StartsWith("VOT"))
        {
            GD.Print($"[PKT] VOT: {packet[3..]}");
        }
        else if (packet.StartsWith("LTR"))
        {
        }
        else if (packet.StartsWith("DRM"))
        {
        }
        else if (packet.StartsWith("DNF"))
        {
        }
        else if (packet.StartsWith("PRM"))
        {
        }
        else if (packet.StartsWith("INF"))
        {
        }
        else if (packet.StartsWith("APT"))
        {
        }
        else if (packet.StartsWith("PNT"))
        {
            GD.Print($"[PKT] PNT: {packet[3..]}");
        }
        else if (packet.StartsWith("DOK"))
        {
            _state.Resting = !_state.Resting;
        }
        else if (packet.StartsWith("NVG"))
        {
            HandleCharNavigation(packet[3..]);
        }
        else if (packet.StartsWith("T01"))
        {
            GD.Print($"[PKT] T01: {packet[3..]}");
        }
        else if (packet.StartsWith("QDL"))
        {
            HandleRemoveDialog(packet[3..]);
        }
        else if (packet.StartsWith("PCF"))
        {
            HandleParticleCreate(packet[3..]);
        }
        else if (packet.StartsWith("PCR"))
        {
            HandleAmbientColor(packet[3..]);
        }
        else if (packet.StartsWith("PCL"))
        {
            HandleLightCreate(packet[3..]);
        }
        else if (packet.StartsWith("PCB"))
        {
            HandleCharParticle(packet[3..]);
        }
        // ── 2-char opcodes ──
        else if (packet.StartsWith("CM"))
        {
            HandleChangeMap(packet[2..]);
        }
        else if (packet.StartsWith("PT"))
        {
            HandlePositionRejection(packet[2..]);
        }
        else if (packet.StartsWith("PU"))
        {
            HandlePlayerPosition(packet[2..]);
        }
        else if (packet.StartsWith("PX"))
        {
        }
        else if (packet.StartsWith("CC"))
        {
            HandleCreateChar(packet[2..]);
        }
        else if (packet.StartsWith("CP"))
        {
            HandleChangeChar(packet[2..]);
        }
        else if (packet.StartsWith("BP"))
        {
            HandleRemoveChar(packet[2..]);
        }
        else if (packet.StartsWith("IP"))
        {
            HandleSelfIndex(packet[2..]);
        }
        else if (packet.StartsWith("CA"))
        {
            HandleClearArea(packet);
        }
        else if (packet.StartsWith("HO"))
        {
            HandleGroundObject(packet[2..]);
        }
        else if (packet.StartsWith("BO"))
        {
            HandleRemoveObject(packet[2..]);
        }
        else if (packet.StartsWith("BQ"))
        {
            HandleBlockUpdate(packet[2..]);
        }
        else if (packet.StartsWith("XM"))
        {
            HandleMusic(packet[2..]);
        }
        else if (packet.StartsWith("ON"))
        {
            HandleOnlineCount(packet[2..]);
        }
        else if (packet.StartsWith("TW"))
        {
            HandlePlaySound(packet[2..]);
        }
        else if (packet.StartsWith("TI"))
        {
        }
        else if (packet.StartsWith("GL"))
        {
            GD.Print($"[PKT] GL: {(packet.Length > 50 ? packet[..50] + "..." : packet)}");
        }
        else if (packet.StartsWith("N~"))
        {
            _state.MapName = packet[2..];
        }
        else if (packet.StartsWith("N4"))
        {
            HandlePvpDamageReceived(packet[2..]);
        }
        else if (packet.StartsWith("N5"))
        {
            HandlePvpDamageDealt(packet[2..]);
        }
        else if (packet.StartsWith("N2"))
        {
            HandleNpcDamageReceived(packet[2..]);
        }
        else if (packet.StartsWith("N1"))
        {
            HandleNpcMissed();
        }
        else if (packet.StartsWith("U1"))
        {
            HandleUserMissed();
        }
        else if (packet.StartsWith("U2"))
        {
            HandleUserDamageDealt(packet[2..]);
        }
        else if (packet.StartsWith("U3"))
        {
            HandleUserEvaded(packet[2..]);
        }
        // ── Bracket opcodes [X] ──
        else if (packet.StartsWith("[ES"))
        {
            HandleBulkStats(packet[3..]);
        }
        else if (packet.StartsWith("[BG"))
        {
            HandleBankGold(packet[3..]);
        }
        else if (packet.StartsWith("[CD"))
        {
            HandleCharData(packet[3..]);
        }
        else if (packet.StartsWith("[H]"))
        {
            HandleHpStats(packet[3..]);
        }
        else if (packet.StartsWith("[M]"))
        {
            HandleManaStats(packet[3..]);
        }
        else if (packet.StartsWith("[S]"))
        {
            HandleStaStats(packet[3..]);
        }
        else if (packet.StartsWith("[G]"))
        {
            HandleGold(packet[3..]);
        }
        else if (packet.StartsWith("[E]"))
        {
            HandleExp(packet[3..]);
        }
        else if (packet.StartsWith("[L]"))
        {
            _state.Level = ParseInt(packet[3..]);
        }
        else if (packet.StartsWith("[F]"))
        {
            _state.Strength = ParseInt(packet[3..]);
        }
        else if (packet.StartsWith("[A]"))
        {
            _state.Agility = ParseInt(packet[3..]);
        }
        else if (packet.StartsWith("[R]"))
        {
            _state.Reputation = ParseInt(packet[3..]);
        }
        else if (packet.StartsWith("[N]"))
        {
            _state.UserName = packet[3..];
        }
        // ── AU| — Aura update broadcast ──
        else if (packet.StartsWith("AU|"))
        {
            HandleAuraUpdate(packet[3..]);
        }
        // ── USM — User mount state ──
        else if (packet.StartsWith("USM"))
        {
            HandleUserMount(packet[3..]);
        }
        // ── Pipe opcodes |X ──
        else if (packet.StartsWith("|S1"))
        {
            HandlePartialInvAmount(packet[3..]);
        }
        else if (packet.StartsWith("|S2"))
        {
            HandlePartialInvEquip(packet[3..]);
        }
        else if (packet.StartsWith("||"))
        {
            HandleConsoleMessage(packet[2..]);
        }
        else if (packet.StartsWith("|B"))
        {
            HandleCharAppearance(packet[2..], 'B');
        }
        else if (packet.StartsWith("|W"))
        {
            HandleCharAppearance(packet[2..], 'W');
        }
        else if (packet.StartsWith("|E"))
        {
            HandleCharAppearance(packet[2..], 'E');
        }
        else if (packet.StartsWith("|H"))
        {
            HandleCharAppearance(packet[2..], 'H');
        }
        else if (packet.StartsWith("|C"))
        {
            HandleCharAppearance(packet[2..], 'C');
        }
        // ── Chat opcodes ──
        else if (packet.StartsWith("T|"))
        {
            HandleTalk(packet[2..]);
        }
        else if (packet.StartsWith("N|"))
        {
            HandleYell(packet[2..]);
        }
        else if (packet.StartsWith("P|"))
        {
            HandleWhisper(packet[2..]);
        }
        else if (packet.StartsWith("G|"))
        {
            HandleGuildChat(packet[2..]);
        }
        else if (packet.StartsWith("C|"))
        {
            HandleClanChat(packet[2..]);
        }
        // ── Special prefix opcodes ──
        else if (packet.StartsWith("!!"))
        {
            HandleGmBroadcast(packet[2..]);
        }
        else if (packet.StartsWith("+") || packet.StartsWith("*"))
        {
            HandleMoveChar(packet[1..]);
        }
        else if (packet.Length >= 1 && packet[0] == '6')
        {
            HandleNpcKilledUser();
        }
        else
        {
            GD.Print($"[PKT] Unhandled: {(packet.Length > 40 ? packet[..40] + "..." : packet)}");
        }
    }
}
