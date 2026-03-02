using System;
using System.Collections.Generic;
using Godot;
using TierrasSagradasAO.Data;
using TierrasSagradasAO.Game;

namespace TierrasSagradasAO.Network;

/// <summary>
/// Dispatches inbound packets by opcode prefix to handler methods.
/// Updates GameState based on server packets.
/// </summary>
public class PacketHandler
{
    private readonly GameState _state;

    /// Callback to load the map immediately when CM is received.
    /// This ensures the map file is loaded BEFORE subsequent BQ/HO packets
    /// are processed, so blocked state and ground objects apply to the correct MapData.
    public Action? OnMapLoad;

    // Meditation FX IDs — cleared when character moves
    private static readonly HashSet<int> MeditationFxIds = new()
    {
        4, 5, 6, 16, 42, 43, 44, 45, 103, 104, 105
    };

    public PacketHandler(GameState state)
    {
        _state = state;
    }

    public void HandlePacket(string packet)
    {
        if (string.IsNullOrEmpty(packet)) return;

        // Multi-char opcodes first (longest match)
        // ── 9-char opcodes ──
        if (packet.StartsWith("INITBANCO"))
        {
            HandleInitBanco();
        }
        else if (packet.StartsWith("FINCBNOK"))
        {
            // Guild bank close confirmation
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
            _state.UserParalyzed = !_state.UserParalyzed;
            // VB6: TiempoParalizado = 22 on activate, 0 on deactivate
            _state.ParalysisTimer = _state.UserParalyzed ? 22f : 0f;
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
            // Guild creation form — UI stub
            GD.Print("[PKT] SHOWFUN: Guild creation form");
        }
        else if (packet.StartsWith("IREDAEL"))
        {
            // Guild info (leader view) — store for UI
            GD.Print($"[PKT] IREDAEL: {(packet.Length > 50 ? packet[..50] + "..." : packet)}");
        }
        else if (packet.StartsWith("IREDAEK"))
        {
            // Guild info (member view) — store for UI
            GD.Print($"[PKT] IREDAEK: {(packet.Length > 50 ? packet[..50] + "..." : packet)}");
        }
        // ── 6-char opcodes ──
        else if (packet.StartsWith("LOGGED"))
        {
            HandleLogged();
        }
        else if (packet.StartsWith("SEGOFF"))
        {
            _state.SafeMode = false;
            _state.ChatMessages.Enqueue(new ChatMessage { Text = "Seguro desactivado.", Color = "FF0000" });
        }
        else if (packet.StartsWith("FLECHI"))
        {
            HandleArrow(packet[6..]);
        }
        else if (packet.StartsWith("CIRUJA"))
        {
            // Appearance change UI — stub
            GD.Print($"[PKT] CIRUJA: {packet[6..]}");
        }
        else if (packet.StartsWith("LSTCRI"))
        {
            // Creature list (trainer) — stub
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
        else if (packet.StartsWith("STOPD"))
        {
            _state.UserStopped = packet.Length > 5 && packet[5] == '1';
        }
        else if (packet.StartsWith("SEGON"))
        {
            _state.SafeMode = true;
            _state.ChatMessages.Enqueue(new ChatMessage { Text = "Seguro activado.", Color = "00FF00" });
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
            // Guild bank permissions — stub
            GD.Print($"[PKT] KHEKD: {packet[5..]}");
        }
        else if (packet.StartsWith("ENCHAT"))
        {
            // Start chat with friend — stub
            GD.Print($"[PKT] ENCHAT: {packet[6..]}");
        }
        else if (packet.StartsWith("IRCHAT"))
        {
            HandleFriendChat(packet[6..]);
        }
        // ── 4-char opcodes ──
        else if (packet.StartsWith("EHYS"))
        {
            HandleHungerThirst(packet[4..]);
        }
        else if (packet.StartsWith("INVI"))
        {
            // Inventory init, ignore
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
            // MENU{name},{privs} — User interaction menu (right-click on player)
            GD.Print($"[PKT] MENU: {packet[4..]}");
        }
        else if (packet.StartsWith("SELE"))
        {
            // SELE{type},{name},OBJ — Item selection prompt
            GD.Print($"[PKT] SELE: {packet[4..]}");
        }
        else if (packet.StartsWith("DTLC"))
        {
            // Guild details response — stub
            GD.Print($"[PKT] DTLC: {(packet.Length > 50 ? packet[..50] + "..." : packet)}");
        }
        else if (packet.StartsWith("GINF"))
        {
            // Player info detail — stub
            GD.Print($"[PKT] GINF: {(packet.Length > 50 ? packet[..50] + "..." : packet)}");
        }
        else if (packet.StartsWith("FEST"))
        {
            // Mini statistics — stub
            GD.Print($"[PKT] FEST: {packet[4..]}");
        }
        else if (packet.StartsWith("ACDA"))
        {
            // Account data — stub
            GD.Print($"[PKT] ACDA: {packet[4..]}");
        }
        else if (packet.StartsWith("MTOP"))
        {
            // Mountain top rankings — stub
            GD.Print($"[PKT] MTOP: {packet[4..]}");
        }
        else if (packet.StartsWith("ZSOS"))
        {
            // SOS messages list — stub
            GD.Print($"[PKT] ZSOS: {(packet.Length > 50 ? packet[..50] + "..." : packet)}");
        }
        else if (packet.StartsWith("IMEJ"))
        {
            // Item preview — stub
            GD.Print($"[PKT] IMEJ: {packet[4..]}");
        }
        // ── 7+ char opcodes (before 3-char to avoid prefix collision: INITCOM vs ICO, CANCELTRADE vs CA) ──
        else if (packet.StartsWith("CANCELTRADE"))
        {
            HandleTradeCancelled();
        }
        else if (packet.StartsWith("DAMEQUEST"))
        {
            // Quest NPC trigger — stub
            GD.Print("[PKT] DAMEQUEST: Quest NPC interaction");
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
            // Error dialog (no disconnect)
            _state.ChatMessages.Enqueue(new ChatMessage { Text = packet[3..], Color = "FF0000" });
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
            // Scroll timer, ignore for now
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
            HandleFriendsList(packet[3..]);
        }
        else if (packet.StartsWith("KFM"))
        {
            HandleFriendOnline(packet[3..]);
        }
        else if (packet.StartsWith("DFM"))
        {
            HandleFriendOffline(packet[3..]);
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
            HandleCharFx(packet[3..]); // Same format as CFX
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
            // Open carpentry UI — stub
            GD.Print("[PKT] SFC: Carpentry UI");
        }
        else if (packet.StartsWith("SFH"))
        {
            // Open blacksmith UI — stub
            GD.Print("[PKT] SFH: Blacksmith UI");
        }
        else if (packet.StartsWith("LAH"))
        {
            // Buildable weapons list — stub
            GD.Print($"[PKT] LAH: {(packet.Length > 50 ? packet[..50] + "..." : packet)}");
        }
        else if (packet.StartsWith("LAR"))
        {
            // Buildable armors list — stub
            GD.Print($"[PKT] LAR: {(packet.Length > 50 ? packet[..50] + "..." : packet)}");
        }
        else if (packet.StartsWith("OBR"))
        {
            // Buildable carpentry items — stub
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
            // Mail list header — stub
            GD.Print($"[PKT] IFO: {packet[3..]}");
        }
        else if (packet.StartsWith("IDO"))
        {
            // Mail player info — stub
            GD.Print($"[PKT] IDO: {packet[3..]}");
        }
        else if (packet.StartsWith("IAO"))
        {
            // Mail friend list — stub
            GD.Print($"[PKT] IAO: {packet[3..]}");
        }
        else if (packet.StartsWith("ILO"))
        {
            // Mail content — stub
            GD.Print($"[PKT] ILO: {(packet.Length > 50 ? packet[..50] + "..." : packet)}");
        }
        else if (packet.StartsWith("ITO"))
        {
            // Mail items — stub
            GD.Print($"[PKT] ITO: {packet[3..]}");
        }
        else if (packet.StartsWith("QTL"))
        {
            // Quest list — stub
            GD.Print($"[PKT] QTL: {(packet.Length > 50 ? packet[..50] + "..." : packet)}");
        }
        else if (packet.StartsWith("MQS"))
        {
            // Quest details — stub
            GD.Print($"[PKT] MQS: {(packet.Length > 50 ? packet[..50] + "..." : packet)}");
        }
        else if (packet.StartsWith("MQC"))
        {
            // Quest progress — stub
            GD.Print($"[PKT] MQC: {(packet.Length > 50 ? packet[..50] + "..." : packet)}");
        }
        else if (packet.StartsWith("MFC"))
        {
            // Property/house form — stub
            GD.Print($"[PKT] MFC: {packet[3..]}");
        }
        else if (packet.StartsWith("GVN"))
        {
            // House owner/price — stub
            GD.Print($"[PKT] GVN: {packet[3..]}");
        }
        else if (packet.StartsWith("MAR"))
        {
            // Market/auction — stub
            GD.Print($"[PKT] MAR: {(packet.Length > 50 ? packet[..50] + "..." : packet)}");
        }
        else if (packet.StartsWith("VOT"))
        {
            // Voting list — stub
            GD.Print($"[PKT] VOT: {packet[3..]}");
        }
        else if (packet.StartsWith("LTR"))
        {
            // Tournament rankings — stub
            GD.Print($"[PKT] LTR: {packet[3..]}");
        }
        else if (packet.StartsWith("DRM"))
        {
            // Donation menu — stub
            GD.Print($"[PKT] DRM: {packet[3..]}");
        }
        else if (packet.StartsWith("DNF"))
        {
            // Donation item info — stub
            GD.Print($"[PKT] DNF: {packet[3..]}");
        }
        else if (packet.StartsWith("PRM"))
        {
            // Prize menu info — stub
            GD.Print($"[PKT] PRM: {packet[3..]}");
        }
        else if (packet.StartsWith("INF"))
        {
            // Prize list — stub
            GD.Print($"[PKT] INF: {packet[3..]}");
        }
        else if (packet.StartsWith("APT"))
        {
            // Tournament/donation points — stub
            GD.Print($"[PKT] APT: {packet[3..]}");
        }
        else if (packet.StartsWith("PNT"))
        {
            // Party points — stub
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
            // Work skill UI — stub
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
            // Status broadcast, ignore for now
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
            // Drop item echo — ignore
        }
        else if (packet.StartsWith("GL"))
        {
            // Guild list — stub
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
            // + = player move, * = NPC move (VB6: both use Char_Move_by_Pos)
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

    /// <summary>
    /// CFX{charindex},{fxId},{loops} — Apply FX animation to a character.
    /// fxId=0 clears all FX. loops>=999 means infinite (-1).
    /// </summary>
    private void HandleCharFx(string data)
    {
        var parts = data.Split(',');
        if (parts.Length < 3) return;

        int charIdx = ParseInt(parts[0]);
        int fxId = ParseInt(parts[1]);
        int loops = ParseInt(parts[2]);

        if (!_state.Characters.TryGetValue(charIdx, out var ch))
            return;

        if (fxId == 0)
        {
            // Clear all FX slots
            for (int i = 0; i < 3; i++)
            {
                ch.ActiveFxSlots[i] = 0;
                ch.FxLoops[i] = 0;
                ch.FxFrameCounter[i] = 0;
            }
            return;
        }

        // Find first empty slot
        for (int i = 0; i < 3; i++)
        {
            if (ch.ActiveFxSlots[i] == 0)
            {
                ch.ActiveFxSlots[i] = fxId;
                ch.FxLoops[i] = loops >= 999 ? -1 : loops;
                ch.FxFrameCounter[i] = 0;
                return;
            }
        }

        // All slots full — overwrite slot 0 (oldest)
        ch.ActiveFxSlots[0] = fxId;
        ch.FxLoops[0] = loops >= 999 ? -1 : loops;
        ch.FxFrameCounter[0] = 0;
    }

    /// <summary>
    /// CFE — Emoticon display on character.
    /// VB6: CFE{charindex},{fxIndex},{loops}
    /// </summary>
    private void HandleCharEmoticon(string data)
    {
        var parts = data.Split(',');
        if (parts.Length < 3) return;

        int charIdx = ParseInt(parts[0]);
        int fxId = ParseInt(parts[1]);
        int loops = ParseInt(parts[2]);

        if (!_state.Characters.TryGetValue(charIdx, out var ch))
            return;

        ch.EmoticonIndex = fxId;
        ch.EmoticonLoops = loops > 0 ? loops : 0;
    }

    /// <summary>
    /// Clear meditation FX from a character (called when they move).
    /// </summary>
    private static void ClearMeditationFx(Character ch)
    {
        for (int i = 0; i < 3; i++)
        {
            if (ch.ActiveFxSlots[i] > 0 && MeditationFxIds.Contains(ch.ActiveFxSlots[i]))
            {
                ch.ActiveFxSlots[i] = 0;
                ch.FxLoops[i] = 0;
                ch.FxFrameCounter[i] = 0;
            }
        }
    }

    private void HandleLogged()
    {
        _state.IsLogged = true;
        GD.Print("[GAME] Login successful — LOGGED received");
    }

    private void HandleChangeMap(string data)
    {
        // CM<map>,<r>,<g>,<b>
        var parts = data.Split(',');
        if (parts.Length >= 4)
        {
            _state.CurrentMap = ParseInt(parts[0]);
            _state.MapColorR = ParseInt(parts[1]);
            _state.MapColorG = ParseInt(parts[2]);
            _state.MapColorB = ParseInt(parts[3]);

            // Clear all characters (except self) and ground objects from previous map.
            // The server will re-send CC packets for entities on the new map.
            var selfIdx = _state.UserCharIndex;
            var toRemove = new System.Collections.Generic.List<int>();
            foreach (var kvp in _state.Characters)
            {
                if (kvp.Key != selfIdx)
                    toRemove.Add(kvp.Key);
            }
            foreach (int key in toRemove)
                _state.Characters.Remove(key);

            _state.GroundObjects.Clear();
            _state.MapParticles.Clear();
            _state.MapLights.Clear();
            _state.LightsDirty = true;

            // Load the map file IMMEDIATELY so that subsequent BQ/HO packets
            // in this same batch apply to the correct (new) MapData.
            // Previously NeedMapLoad deferred this to next frame, causing BQ
            // writes to be overwritten when LoadCurrentMap() finally ran.
            OnMapLoad?.Invoke();

            GD.Print($"[GAME] Change map: {_state.CurrentMap} (cleared {toRemove.Count} chars, all ground objects, particles, lights)");
        }
    }

    /// <summary>
    /// PCR{r},{g},{b} — Runtime ambient light change (GM /MODMAPINFO RGB).
    /// </summary>
    private void HandleAmbientColor(string data)
    {
        var parts = data.Split(',');
        if (parts.Length >= 3)
        {
            _state.MapColorR = ParseInt(parts[0]);
            _state.MapColorG = ParseInt(parts[1]);
            _state.MapColorB = ParseInt(parts[2]);
        }
    }

    /// <summary>
    /// PCF{particleGroup},{x},{y} — Create map particle stream at tile.
    /// </summary>
    private void HandleParticleCreate(string data)
    {
        var parts = data.Split(',');
        if (parts.Length >= 3)
        {
            int defIdx = ParseInt(parts[0]);
            int x = ParseInt(parts[1]);
            int y = ParseInt(parts[2]);
            ParticleSystem.CreateMapStream(_state, defIdx, x, y);
        }
    }

    /// <summary>
    /// PCL{x},{y},{range},{r},{g},{b} — Create light at tile with color and range.
    /// </summary>
    private void HandleLightCreate(string data)
    {
        var parts = data.Split(',');
        if (parts.Length >= 6)
        {
            var light = new MapLight
            {
                X = ParseInt(parts[0]),
                Y = ParseInt(parts[1]),
                Range = ParseInt(parts[2]),
                R = (byte)Math.Clamp(ParseInt(parts[3]), 0, 255),
                G = (byte)Math.Clamp(ParseInt(parts[4]), 0, 255),
                B = (byte)Math.Clamp(ParseInt(parts[5]), 0, 255),
                Active = true
            };
            _state.MapLights.Add(light);
            _state.LightsDirty = true;
        }
    }

    /// <summary>
    /// PCB{charIndex},{particleGroup} — Attach particle to character.
    /// </summary>
    private void HandleCharParticle(string data)
    {
        var parts = data.Split(',');
        if (parts.Length >= 2)
        {
            int charIndex = ParseInt(parts[0]);
            int defIdx = ParseInt(parts[1]);
            ParticleSystem.CreateCharStream(_state, defIdx, charIndex);
        }
    }

    private void HandlePlayerPosition(string data)
    {
        // PU<x>,<y> — authoritative position set (e.g. after warp/teleport)
        var parts = data.Split(',');
        if (parts.Length >= 2)
        {
            int x = ParseInt(parts[0]);
            int y = ParseInt(parts[1]);
            _state.UserPosX = x;
            _state.UserPosY = y;

            // Also snap the self character and cancel any in-progress scroll
            if (_state.Characters.TryGetValue(_state.UserCharIndex, out var ch))
            {
                ch.PosX = x;
                ch.PosY = y;
                ch.MoveOffsetX = 0;
                ch.MoveOffsetY = 0;
                ch.Moving = false;
                ch.ScrollDirectionX = 0;
                ch.ScrollDirectionY = 0;
            }
            _state.ScreenOffsetX = 0;
            _state.ScreenOffsetY = 0;
            _state.AddToUserPosX = 0;
            _state.AddToUserPosY = 0;
            _state.UserMoving = false;
            _state.PendingMoves = 0;
        }
    }

    private void HandlePositionRejection(string data)
    {
        // PT<x>,<y> — server rejected movement, snap back to correct position
        var parts = data.Split(',');
        if (parts.Length >= 2)
        {
            int x = ParseInt(parts[0]);
            int y = ParseInt(parts[1]);

            // Log with detail so we can diagnose desync causes
            int clientX = _state.UserPosX;
            int clientY = _state.UserPosY;
            int deltaX = clientX - x;
            int deltaY = clientY - y;
            GD.Print($"[MOVE] PT correction: client({clientX},{clientY}) → server({x},{y}) delta=({deltaX},{deltaY}) moving={_state.UserMoving}");

            _state.UserPosX = x;
            _state.UserPosY = y;

            if (_state.Characters.TryGetValue(_state.UserCharIndex, out var ch))
            {
                ch.PosX = x;
                ch.PosY = y;
                ch.MoveOffsetX = 0;
                ch.MoveOffsetY = 0;
                ch.Moving = false;
                ch.ScrollDirectionX = 0;
                ch.ScrollDirectionY = 0;
            }

            _state.ScreenOffsetX = 0;
            _state.ScreenOffsetY = 0;
            _state.AddToUserPosX = 0;
            _state.AddToUserPosY = 0;
            _state.UserMoving = false;

            // Reset pending moves counter and add cooldown.
            _state.PendingMoves = 0;
            _state.PtCooldownFrames = 3;
        }
    }

    private void HandleSelfIndex(string data)
    {
        _state.UserCharIndex = ParseInt(data);
        GD.Print($"[GAME] Self char index: {_state.UserCharIndex}");
    }

    private void HandleCreateChar(string data)
    {
        // CC<body>,<head>,<heading>,<charindex>,<x>,<y>,<weapon>,<shield>,<casco>,<name>,<status>,<privs>
        var parts = data.Split(',');
        if (parts.Length < 12) return;

        int charIndex = ParseInt(parts[3]);
        int head = ParseInt(parts[1]);
        var ch = new Character
        {
            CharIndex = charIndex,
            Body = ParseInt(parts[0]),
            Head = head,
            Heading = ParseInt(parts[2]),
            PosX = ParseInt(parts[4]),
            PosY = ParseInt(parts[5]),
            WeaponAnim = ParseInt(parts[6]),
            ShieldAnim = ParseInt(parts[7]),
            CascoAnim = ParseInt(parts[8]),
            Name = parts[9],
            Criminal = ParseInt(parts[10]) == 2,
            Privileges = ParseInt(parts[11]),
        };

        // VB6: dead characters have ghost heads (500, 501, 511, 512)
        ch.Dead = IsDeadHead(head);

        // NPC CC format has 15 fields: ...,,,,aura,npcnumber (fields 9-12 empty, 13=aura, 14=npcnumber)
        // User CC format has 12 fields: ...name,status,priv
        if (parts.Length >= 15)
        {
            ch.NpcAura = ParseInt(parts[13]);
        }

        _state.Characters[charIndex] = ch;

        GD.Print($"[CC] {ch.Name} idx={charIndex} body={ch.Body} head={ch.Head} weapon={ch.WeaponAnim} shield={ch.ShieldAnim} casco={ch.CascoAnim} (raw parts[7]={parts[7]})");

        if (ch.Body <= 0)
            GD.PrintErr($"[CC] WARNING: char {ch.Name} (idx={charIndex}) has body=0!");
    }

    private void HandleChangeChar(string data)
    {
        // VB6 CP format: CP<charindex>,<body>,<head>,<heading>,<weapon>,<shield>,<fx>,<loops>,<casco>
        // Some server paths send shorter 4-field (NPC heading) or 7-field variants.
        var parts = data.Split(',');
        if (parts.Length < 4) return;

        int idx = ParseInt(parts[0]);
        if (_state.Characters.TryGetValue(idx, out var ch))
        {
            int newHead = ParseInt(parts[2]);
            bool wasDead = ch.Dead;
            bool nowDead = IsDeadHead(newHead);

            ch.Body = ParseInt(parts[1]);
            ch.Head = newHead;
            ch.Heading = ParseInt(parts[3]);
            if (parts.Length > 4) ch.WeaponAnim = ParseInt(parts[4]);
            if (parts.Length > 5) ch.ShieldAnim = ParseInt(parts[5]);
            // VB6: casco is at position 8 (9th field), positions 6-7 are FX/loops
            if (parts.Length >= 9) ch.CascoAnim = ParseInt(parts[8]);
            else if (parts.Length == 7) ch.CascoAnim = ParseInt(parts[6]); // legacy 7-field
            ch.Dead = nowDead;

            // Reset transparency pulsing on state change
            if (wasDead != nowDead)
            {
                ch.TransparenciaBody = 0;
                ch.Llegoalatransp = false;
            }

            // If this is our character, sync _state.Dead
            if (idx == _state.UserCharIndex)
            {
                if (nowDead && !wasDead)
                {
                    _state.Dead = true;
                    GD.Print($"[CP] User character died (head={newHead})");
                }
                else if (!nowDead && wasDead)
                {
                    _state.Dead = false;
                    GD.Print($"[CP] User character revived (body={ch.Body}, head={newHead})");
                }
            }
        }
    }

    private void HandleRemoveChar(string data)
    {
        int idx = ParseInt(data);
        _state.Characters.Remove(idx);
    }

    private void HandleMoveChar(string data)
    {
        // +<charindex>,<x>,<y>
        // Server sends this ONLY to OTHER players (never to the mover).
        // VB6: SendToUserAreaButindex excludes the mover (c != conn_id).
        var parts = data.Split(',');
        if (parts.Length < 3) return;

        int idx = ParseInt(parts[0]);
        int newX = ParseInt(parts[1]);
        int newY = ParseInt(parts[2]);

        if (!_state.Characters.TryGetValue(idx, out var ch))
            return;

        // Server never sends + for self, but guard against it just in case
        if (idx == _state.UserCharIndex)
            return;

        {
            // Clear meditation FX on movement
            ClearMeditationFx(ch);

            // Other characters: VB6 Char_Move_by_Pos
            int dx = newX - ch.PosX;
            int dy = newY - ch.PosY;

            // Calculate heading from delta
            if (dy < 0) ch.Heading = 1;      // North
            else if (dx > 0) ch.Heading = 2;  // East
            else if (dy > 0) ch.Heading = 3;  // South
            else if (dx < 0) ch.Heading = 4;  // West

            // Start smooth move
            ch.MoveOffsetX = -(dx * 32);
            ch.MoveOffsetY = -(dy * 32);
            ch.ScrollDirectionX = dx != 0 ? Math.Sign(dx) : 0;
            ch.ScrollDirectionY = dy != 0 ? Math.Sign(dy) : 0;
            ch.PosX = newX;
            ch.PosY = newY;
            ch.Moving = true;
            ch.WalkFrame = 0; // Reset walk animation on new move
        }
    }

    private void HandleClearArea(string packet)
    {
        // CA + raw byte(x) + raw byte(y)
        // VB6 CambioDeArea: uses 9x9 grid zones, erases everything outside 27-tile range
        if (packet.Length >= 4)
        {
            int playerX = (byte)packet[2];
            int playerY = (byte)packet[3];

            // VB6: MinLimiteX = (X \ 9 - 1) * 9, MaxLimiteX = MinLimiteX + 26
            int minLimX = (playerX / 9 - 1) * 9;
            int maxLimX = minLimX + 26;
            int minLimY = (playerY / 9 - 1) * 9;
            int maxLimY = minLimY + 26;

            // Keep entities within the visible range + margin to prevent pop-in.
            // New CCs may arrive in the next TCP read (next frame); keeping visible
            // entities prevents the 1-frame flash where they disappear then reappear.
            const int HalfW = 8; // HalfWindowTileWidth
            const int HalfH = 6; // HalfWindowTileHeight
            const int VisMargin = 3;
            int visMinX = playerX - HalfW - VisMargin;
            int visMaxX = playerX + HalfW + VisMargin;
            int visMinY = playerY - HalfH - VisMargin;
            int visMaxY = playerY + HalfH + VisMargin;

            // Erase characters outside the area (but keep visible ones)
            var toRemove = new List<int>();
            foreach (var kvp in _state.Characters)
            {
                if (kvp.Key == _state.UserCharIndex) continue;
                var ch = kvp.Value;
                // Outside the 27-tile area zone
                if (ch.PosX < minLimX || ch.PosX > maxLimX ||
                    ch.PosY < minLimY || ch.PosY > maxLimY)
                {
                    // But keep if within visible range (prevents pop-in)
                    if (ch.PosX >= visMinX && ch.PosX <= visMaxX &&
                        ch.PosY >= visMinY && ch.PosY <= visMaxY)
                        continue;
                    toRemove.Add(kvp.Key);
                }
            }
            foreach (int key in toRemove)
            {
                _state.Characters.Remove(key);
            }

            // Erase ground objects outside the area
            var objToRemove = new List<(int, int)>();
            foreach (var kvp in _state.GroundObjects)
            {
                if (kvp.Key.Item1 < minLimX || kvp.Key.Item1 > maxLimX ||
                    kvp.Key.Item2 < minLimY || kvp.Key.Item2 > maxLimY)
                {
                    objToRemove.Add(kvp.Key);
                }
            }
            foreach (var key in objToRemove)
            {
                _state.GroundObjects.Remove(key);
            }
        }
    }

    private void HandleGroundObject(string data)
    {
        // HO<grh>,<x>,<y>
        var parts = data.Split(',');
        if (parts.Length >= 3)
        {
            int grh = ParseInt(parts[0]);
            int x = ParseInt(parts[1]);
            int y = ParseInt(parts[2]);
            _state.GroundObjects[(x, y)] = grh;
        }
    }

    private void HandleRemoveObject(string data)
    {
        // BO<x>,<y>
        var parts = data.Split(',');
        if (parts.Length >= 2)
        {
            int x = ParseInt(parts[0]);
            int y = ParseInt(parts[1]);
            _state.GroundObjects.Remove((x, y));
        }
    }

    private void HandleBlockUpdate(string data)
    {
        // BQ<x>,<y>,<blocked>
        var parts = data.Split(',');
        if (parts.Length >= 3)
        {
            int x = ParseInt(parts[0]);
            int y = ParseInt(parts[1]);
            int blocked = ParseInt(parts[2]);
            if (_state.MapData != null && x >= 1 && x <= 100 && y >= 1 && y <= 100)
            {
                _state.MapData.Tiles[x, y].Blocked = blocked != 0;
            }
        }
    }

    private void HandleTogglePause()
    {
        _state.Paused = !_state.Paused;
        GD.Print($"[GAME] Pause toggled: {_state.Paused}");
    }

    private void HandleBulkStats(string data)
    {
        // [ES<maxhp>,<minhp>,<maxmana>,<minmana>,<maxsta>,<minsta>,<gold>,<level>,<expnext>,<exp>,<name>,<attr1>,<attr0>,<rep>
        var parts = data.Split(',');
        if (parts.Length >= 10)
        {
            _state.MaxHp = ParseInt(parts[0]);
            _state.MinHp = ParseInt(parts[1]);
            _state.MaxMana = ParseInt(parts[2]);
            _state.MinMana = ParseInt(parts[3]);
            _state.MaxSta = ParseInt(parts[4]);
            _state.MinSta = ParseInt(parts[5]);
            _state.Gold = ParseInt(parts[6]);
            _state.Level = ParseInt(parts[7]);
            _state.ExpNext = ParseInt(parts[8]);
            _state.Exp = ParseInt(parts[9]);
            if (parts.Length > 10) _state.UserName = parts[10];
            if (parts.Length > 11) _state.Agility = ParseInt(parts[11]);
            if (parts.Length > 12) _state.Strength = ParseInt(parts[12]);
            if (parts.Length > 13) _state.Reputation = ParseInt(parts[13]);
            GD.Print($"[GAME] Stats: HP {_state.MinHp}/{_state.MaxHp} Mana {_state.MinMana}/{_state.MaxMana} Lvl {_state.Level}");
        }
    }

    private void HandleHpStats(string data)
    {
        var parts = data.Split(',');
        if (parts.Length >= 2)
        {
            _state.MaxHp = ParseInt(parts[0]);
            _state.MinHp = ParseInt(parts[1]);
        }
    }

    private void HandleManaStats(string data)
    {
        var parts = data.Split(',');
        if (parts.Length >= 2)
        {
            _state.MaxMana = ParseInt(parts[0]);
            _state.MinMana = ParseInt(parts[1]);
        }
    }

    private void HandleStaStats(string data)
    {
        var parts = data.Split(',');
        if (parts.Length >= 2)
        {
            _state.MaxSta = ParseInt(parts[0]);
            _state.MinSta = ParseInt(parts[1]);
        }
    }

    private void HandleGold(string data)
    {
        _state.Gold = ParseInt(data);
    }

    private void HandleExp(string data)
    {
        var parts = data.Split(',');
        if (parts.Length >= 2)
        {
            _state.ExpNext = ParseInt(parts[0]);
            _state.Exp = ParseInt(parts[1]);
        }
    }

    private void HandleHungerThirst(string data)
    {
        // EHYS<maxAgua>,<minAgua>,<maxHam>,<minHam>
        var parts = data.Split(',');
        if (parts.Length >= 4)
        {
            _state.MaxAgua = ParseInt(parts[0]);
            _state.MinAgua = ParseInt(parts[1]);
            _state.MaxHam = ParseInt(parts[2]);
            _state.MinHam = ParseInt(parts[3]);
        }
    }

    private void HandleReputation(string data)
    {
        _state.Reputation = ParseInt(data);
    }

    private void HandlePrivileges(string data)
    {
        _state.Privileges = ParseInt(data);
    }

    private void HandleInventorySlot(string data)
    {
        // CSI<slot>,<objidx>,<name>,<amt>,<equipped>,<grh>,<type>,<maxhit>,<minhit>,<maxdef>,<valor>
        // Empty slot (VB6): CSI<slot>,0,(None),0,0 (only 5 fields)
        var parts = data.Split(',');
        if (parts.Length < 2) return;

        int slot = ParseInt(parts[0]);
        if (slot < 1 || slot > 25) return;

        int objIndex = ParseInt(parts[1]);
        if (objIndex <= 0 || parts.Length < 5)
        {
            // Empty slot — clear it
            _state.Inventory[slot - 1] = new InventorySlot();
        }
        else if (parts.Length >= 11)
        {
            _state.Inventory[slot - 1] = new InventorySlot
            {
                ObjIndex = objIndex,
                Name = parts[2],
                Amount = ParseInt(parts[3]),
                Equipped = ParseInt(parts[4]) != 0,
                GrhIndex = ParseInt(parts[5]),
                ObjType = ParseInt(parts[6]),
                MaxHit = ParseInt(parts[7]),
                MinHit = ParseInt(parts[8]),
                MaxDef = ParseInt(parts[9]),
                Value = ParseInt(parts[10]),
            };
        }
    }

    private void HandleSpellSlot(string data)
    {
        // SHS<slot>,<spellid>,<name>
        var parts = data.Split(',');
        if (parts.Length >= 3)
        {
            int slot = ParseInt(parts[0]);
            if (slot >= 1 && slot <= 20)
            {
                _state.Spells[slot - 1] = new SpellSlot
                {
                    SpellId = ParseInt(parts[1]),
                    Name = parts[2],
                };
            }
        }
    }

    private void HandleMusic(string data)
    {
        _state.MusicId = ParseInt(data);
    }

    private void HandleOnlineCount(string data)
    {
        _state.OnlineCount = ParseInt(data);
        GD.Print($"[GAME] Online: {_state.OnlineCount}");
    }

    private void HandleConsoleMessage(string data)
    {
        // VB6 format: ||<TextID>@<param1>@<param2>@...
        // TextID indexes into Textos.tsao. Params substitute %1, %2, ... in the message template.
        var parts = data.Split('@');

        if (parts.Length >= 1 && int.TryParse(parts[0], out int textId)
            && textId > 0 && textId < _state.TextMessages.Length)
        {
            var tmpl = _state.TextMessages[textId];
            string text = tmpl.Text;

            // Substitute %1 through %8 with @ params
            for (int i = 1; i < parts.Length && i <= 8; i++)
            {
                text = text.Replace($"%{i}", parts[i]);
            }

            string color = Data.FontTypes.GetHexColor(tmpl.FontId);
            _state.ChatMessages.Enqueue(new ChatMessage { Text = text, Color = color });
            GD.Print($"[CONSOLE] [{textId}] {text}");
        }
        else
        {
            // Fallback: not a valid TextID, show raw text
            string text = parts.Length > 1 ? string.Join(" ", parts[1..]) : data;
            _state.ChatMessages.Enqueue(new ChatMessage { Text = text, Color = "45BE9C" }); // INFO teal
            GD.Print($"[CONSOLE] (raw) {text}");
        }
    }

    private void HandleTalk(string data)
    {
        // T|<color>°<text>°<charindex>  (° = ASCII 176)
        var parts = data.Split((char)176);
        if (parts.Length < 2) return;

        string color = "FFFFFF";
        if (int.TryParse(parts[0], out int vbColor))
        {
            int r = vbColor & 0xFF;
            int g = (vbColor >> 8) & 0xFF;
            int b = (vbColor >> 16) & 0xFF;
            color = $"{r:X2}{g:X2}{b:X2}";
        }

        string text = parts[1];

        // VB6: if charindex present → dialog bubble ONLY (no console)
        bool bubbled = false;
        if (parts.Length >= 3)
        {
            string charIdxStr = parts[2];
            int tildeIdx = charIdxStr.IndexOf('~');
            if (tildeIdx >= 0) charIdxStr = charIdxStr[..tildeIdx];
            if (int.TryParse(charIdxStr, out int charIdx))
            {
                SetCharDialog(charIdx, text, color);
                bubbled = true;
            }
        }

        if (!bubbled)
            _state.ChatMessages.Enqueue(new ChatMessage { Text = text, Color = color });
    }

    private void HandleYell(string data)
    {
        // Server uses N| for TWO formats:
        // 1. Yell (NPC shout):  N|<vbColor>°<text>°<charindex>[~r~g~b~bold~italic]
        // 2. Info (LC response): N|<text>~r~g~b~bold~italic  (no ° separator)
        var parts = data.Split((char)176); // ° = ASCII 176

        if (parts.Length >= 2)
        {
            // Format 1: yell with ° separator — color°text°charindex
            string color = "FF0000";
            if (int.TryParse(parts[0], out int vbColor))
            {
                int r = vbColor & 0xFF;
                int g = (vbColor >> 8) & 0xFF;
                int b = (vbColor >> 16) & 0xFF;
                color = $"{r:X2}{g:X2}{b:X2}";
            }

            string text = parts[1];

            // VB6: if charindex present → dialog bubble ONLY (no console)
            bool bubbled = false;
            if (parts.Length >= 3)
            {
                string charIdxStr = parts[2];
                int tildeIdx = charIdxStr.IndexOf('~');
                if (tildeIdx >= 0) charIdxStr = charIdxStr[..tildeIdx];
                if (int.TryParse(charIdxStr, out int charIdx))
                {
                    SetCharDialog(charIdx, text, color);
                    bubbled = true;
                }
            }

            if (!bubbled)
                _state.ChatMessages.Enqueue(new ChatMessage { Text = text, Color = color });
        }
        else
        {
            // Format 2: info text with ~r~g~b (LC response — no ° at all)
            var tildeParts = data.Split('~');
            string text = tildeParts[0];
            string color = "45BE9C"; // default info teal
            if (tildeParts.Length >= 4)
            {
                int r = ParseInt(tildeParts[1]);
                int g = ParseInt(tildeParts[2]);
                int b = ParseInt(tildeParts[3]);
                color = $"{r:X2}{g:X2}{b:X2}";
            }
            _state.ChatMessages.Enqueue(new ChatMessage { Text = text, Color = color });
        }
    }

    /// <summary>
    /// VB6 Dialogos.CreateDialog: set dialog bubble on a character.
    /// Duration = 5000 + 100 * text.Length ms.  Replaces any previous dialog.
    /// </summary>
    private void SetCharDialog(int charIndex, string text, string hexColor)
    {
        if (!_state.Characters.TryGetValue(charIndex, out var ch)) return;

        ch.DialogText = text;
        ch.DialogColor = hexColor;
        ch.DialogStartMs = System.Environment.TickCount64;
        ch.DialogDurationMs = 5000 + 100 * text.Length;
        ch.DialogRiseCounter = 18;  // VB6 Sube = 18
        ch.DialogAlpha = 20;        // VB6 Desvanecimiento = 20
        ch.DialogFading = false;
    }

    private void HandleWhisper(string data)
    {
        // P|<text>~r~g~b~bold~italic
        var parts = data.Split('~');
        if (parts.Length >= 4)
        {
            int r = ParseInt(parts[1]);
            int g = ParseInt(parts[2]);
            int b = ParseInt(parts[3]);
            string color = $"{r:X2}{g:X2}{b:X2}";
            _state.ChatMessages.Enqueue(new ChatMessage { Text = parts[0], Color = color });
        }
        else if (parts.Length >= 1)
        {
            _state.ChatMessages.Enqueue(new ChatMessage { Text = parts[0], Color = "00FFFF" });
        }
        GD.Print($"[WHISPER] {parts[0]}");
    }

    private void HandleInitCharList(string data)
    {
        // INIAC<num_chars>,<notice>
        _state.CharacterList.Clear();
        var parts = data.Split(',', 2);
        if (parts.Length >= 2)
            _state.ServerNotice = parts[1];
        GD.Print($"[LOGIN] INIAC received, notice: {_state.ServerNotice}");
    }

    private void HandleAddCharPreview(string data)
    {
        // ADDPJ<name>,<slot>,<head>,<body>,<weapon>,<shield>,<helmet>,<level>,<class>,<dead>,<race>
        var parts = data.Split(',');
        if (parts.Length >= 11)
        {
            var preview = new CharacterPreview
            {
                Name = parts[0],
                Slot = ParseInt(parts[1]),
                Head = ParseInt(parts[2]),
                Body = ParseInt(parts[3]),
                Level = ParseInt(parts[7]),
                Class = parts[8],
                Dead = ParseInt(parts[9]) != 0,
                Race = parts[10],
            };
            _state.CharacterList.Add(preview);
            GD.Print($"[LOGIN] ADDPJ: {preview.Name} Lvl {preview.Level} ({preview.Class})");
        }
    }

    private void HandleSecurityCode(string data)
    {
        // CODEH<code>
        _state.SecurityCode = data;
        _state.CurrentScreen = Screen.CharSelect;
        GD.Print($"[LOGIN] CODEH received, switching to char select");
    }

    private void HandleError(string data)
    {
        _state.LoginError = data;
        GD.Print($"[LOGIN] ERR: {data}");
    }

    /// <summary>
    /// |S1{slot},{amount} — Partial inventory update: change item amount.
    /// </summary>
    private void HandlePartialInvAmount(string data)
    {
        var parts = data.Split(',');
        if (parts.Length >= 2)
        {
            int slot = ParseInt(parts[0]);
            int amount = ParseInt(parts[1]);
            if (slot >= 1 && slot <= 25)
            {
                _state.Inventory[slot - 1].Amount = amount;
                // If amount reaches 0, clear the slot
                if (amount <= 0)
                {
                    _state.Inventory[slot - 1] = new InventorySlot();
                }
            }
        }
    }

    /// <summary>
    /// |S2{slot},{equipped} — Partial inventory update: change equip state.
    /// </summary>
    private void HandlePartialInvEquip(string data)
    {
        var parts = data.Split(',');
        if (parts.Length >= 2)
        {
            int slot = ParseInt(parts[0]);
            int equipped = ParseInt(parts[1]);
            if (slot >= 1 && slot <= 25)
            {
                _state.Inventory[slot - 1].Equipped = equipped != 0;
            }
        }
    }

    /// <summary>
    /// |X{charindex},{value} — Change a single appearance field on a character.
    /// X = B(body), W(weapon), E(shield), H(heading), C(casco/helmet).
    /// </summary>
    private void HandleCharAppearance(string data, char field)
    {
        var parts = data.Split(',');
        if (parts.Length < 2) return;

        int idx = ParseInt(parts[0]);
        int value = ParseInt(parts[1]);

        if (!_state.Characters.TryGetValue(idx, out var ch))
            return;

        switch (field)
        {
            case 'B': ch.Body = value; break;
            case 'W': ch.WeaponAnim = value; break;
            case 'E': ch.ShieldAnim = value; break;
            case 'H': ch.Heading = value; break;
            case 'C': ch.CascoAnim = value; break;
        }
    }

    /// <summary>
    /// [CD — Character aura/ranking data.
    /// VB6 format: [CD{charindex},{color},{aura_armor},{aura_weapon},{aura_shield},{aura_ring},{aura_helmet},{levitando},{ranking}
    /// </summary>
    private void HandleCharData(string data)
    {
        var parts = data.Split(',');
        if (parts.Length < 2) return;

        int idx = ParseInt(parts[0]);
        if (!_state.Characters.TryGetValue(idx, out var ch)) return;

        // Parse aura indices (fields 2-6, 0-indexed parts 2-6)
        if (parts.Length >= 7)
        {
            ch.AuraIndexA = ParseInt(parts[2]); // Armor aura
            ch.AuraIndexW = ParseInt(parts[3]); // Weapon aura
            ch.AuraIndexE = ParseInt(parts[4]); // Shield aura
            ch.AuraIndexR = ParseInt(parts[5]); // Ring aura
            ch.AuraIndexC = ParseInt(parts[6]); // Helmet aura
        }
    }

    /// <summary>
    /// AU| — Aura update broadcast (when equipment with aura is equipped/unequipped).
    /// VB6 format: AU|{charindex},{auraA},{auraW},{auraE},{auraR},{auraC}
    /// </summary>
    private void HandleAuraUpdate(string data)
    {
        var parts = data.Split(',');
        if (parts.Length < 6) return;

        int idx = ParseInt(parts[0]);
        if (!_state.Characters.TryGetValue(idx, out var ch)) return;

        ch.AuraIndexA = ParseInt(parts[1]);
        ch.AuraIndexW = ParseInt(parts[2]);
        ch.AuraIndexE = ParseInt(parts[3]);
        ch.AuraIndexR = ParseInt(parts[4]);
        ch.AuraIndexC = ParseInt(parts[5]);
    }

    /// <summary>
    /// ANM — Equipment stats (20 comma-separated fields).
    /// armaMin,armaMax, armorMin,armorMax, escuMin,escuMax,
    /// cascMin,cascMax, herrMin,herrMax,
    /// magMin,magMax, magMina,magMaxa, magMinb,magMaxb, magMinc,magMaxc, magMind,magMaxd
    /// </summary>
    private void HandleEquipmentStats(string data)
    {
        var parts = data.Split(',');
        if (parts.Length < 20) return;

        _state.AttackMin = ParseInt(parts[0]);
        _state.AttackMax = ParseInt(parts[1]);

        int armorMin = ParseInt(parts[2]), armorMax = ParseInt(parts[3]);
        int escuMin = ParseInt(parts[4]), escuMax = ParseInt(parts[5]);
        int cascMin = ParseInt(parts[6]), cascMax = ParseInt(parts[7]);
        int herrMin = ParseInt(parts[8]), herrMax = ParseInt(parts[9]);
        _state.DefenseMin = armorMin + escuMin + cascMin + herrMin;
        _state.DefenseMax = armorMax + escuMax + cascMax + herrMax;

        int magMin = ParseInt(parts[10]), magMax = ParseInt(parts[11]);
        int magMina = ParseInt(parts[12]), magMaxa = ParseInt(parts[13]);
        int magMinb = ParseInt(parts[14]), magMaxb = ParseInt(parts[15]);
        int magMinc = ParseInt(parts[16]), magMaxc = ParseInt(parts[17]);
        int magMind = ParseInt(parts[18]), magMaxd = ParseInt(parts[19]);
        _state.MagDefMin = magMin + magMina + magMinb + magMinc + magMind;
        _state.MagDefMax = magMax + magMaxa + magMaxb + magMaxc + magMaxd;
    }

    /// <summary>
    /// LDM{count},name1(ON),name2(OFF),... — Friends list.
    /// First field is the count, remaining are "Name(ON)" or "Name(OFF)".
    /// Filter out (NADIE) entries (empty slots).
    /// </summary>
    private void HandleFriendsList(string data)
    {
        _state.FriendsList.Clear();
        if (string.IsNullOrEmpty(data)) return;
        var parts = data.Split(',');
        // First field is count — skip it, take names from index 1 onward
        for (int i = 1; i < parts.Length; i++)
        {
            var entry = parts[i].Trim();
            if (entry.Length == 0) continue;
            // Skip empty slots: "(NADIE)(OFF)" or "(NADIE)(ON)"
            if (entry.StartsWith("(NADIE)")) continue;
            // Keep full entry with (ON)/(OFF) for display
            _state.FriendsList.Add(entry);
        }
    }

    /// <summary>
    /// KFM{name} — Friend came online.
    /// </summary>
    private void HandleFriendOnline(string data)
    {
        _state.ChatMessages.Enqueue(new ChatMessage
        {
            Text = $"{data.Trim()} se ha conectado.",
            Color = "00FF00"
        });
    }

    /// <summary>
    /// DFM{name} — Friend went offline.
    /// </summary>
    private void HandleFriendOffline(string data)
    {
        _state.ChatMessages.Enqueue(new ChatMessage
        {
            Text = $"{data.Trim()} se ha desconectado.",
            Color = "FF0000"
        });
    }

    // ── Bank handlers (frmBanco + frmNuevoBancoObj) ───────────────

    private void HandleBankReset()
    {
        _state.BankItemCount = 0;
        for (int i = 0; i < 40; i++)
            _state.BankItems[i] = new BankItem();
    }

    private void HandleBankSlot(string data)
    {
        // SBO<slot>,<obj_idx>,<name>,<amount>,<grh>,<type>,<max_hit>,<min_hit>,<max_def>
        var parts = data.Split(',');
        if (parts.Length < 9) return;

        int slotNum = ParseInt(parts[0]); // 1-based
        int idx = _state.BankItemCount;
        if (idx >= 40) return;

        _state.BankItems[idx] = new BankItem
        {
            Slot = slotNum,
            ObjIndex = ParseInt(parts[1]),
            Name = parts[2],
            Amount = ParseInt(parts[3]),
            GrhIndex = ParseInt(parts[4]),
            ObjType = ParseInt(parts[5]),
            MaxHit = ParseInt(parts[6]),
            MinHit = ParseInt(parts[7]),
            MaxDef = ParseInt(parts[8]),
        };
        _state.BankItemCount++;
    }

    private void HandleInitBanco()
    {
        _state.Banqueando = true;
        GD.Print($"[PKT] INITBANCO: Bank opened ({_state.BankItemCount} items, gold={_state.BankGold})");
    }

    private void HandleBancoOk(string data)
    {
        // BANCOOK<slot>,<type> — type: 0=withdraw, 1=deposit. Confirmation only.
        GD.Print($"[PKT] BANCOOK: {data}");
    }

    private void HandleFinBanOk()
    {
        _state.Banqueando = false;
        _state.BovedaAbierta = false;
        GD.Print("[PKT] FINBANOK: Bank closed");
    }

    private void HandleBankGold(string data)
    {
        if (long.TryParse(data.Trim(), out long v))
            _state.BankGold = v;
    }

    // ── NPC Commerce handlers ─────────────────────────────────────

    private void HandleNpcReset()
    {
        _state.NpcShopCount = 0;
        for (int i = 0; i < 50; i++)
            _state.NpcShopItems[i] = new NpcShopItem();
    }

    private void HandleNpcItem(string data)
    {
        // NPCI{name},{qty},{price},{grh},{obj_idx},{type},{max_hit},{min_hit},{max_def},{slot}
        var parts = data.Split(',');
        if (parts.Length < 10) return;

        int idx = _state.NpcShopCount;
        if (idx >= 50) return;

        _state.NpcShopItems[idx] = new NpcShopItem
        {
            Name = parts[0],
            Amount = ParseInt(parts[1]),
            Price = long.TryParse(parts[2].Trim(), out long p) ? p : 0,
            GrhIndex = ParseInt(parts[3]),
            ObjIndex = ParseInt(parts[4]),
            ObjType = ParseInt(parts[5]),
            MaxHit = ParseInt(parts[6]),
            MinHit = ParseInt(parts[7]),
            MaxDef = ParseInt(parts[8]),
            Slot = ParseInt(parts[9]),
        };
        _state.NpcShopCount++;
    }

    private void HandleNpcSlotUpdate(string data)
    {
        // NPC|{slot},{name},{qty},{price},{grh},{obj_idx},{type},{max_hit},{min_hit},{max_def},{slot2}
        var parts = data.Split(',');
        if (parts.Length < 11) return;

        int targetSlot = ParseInt(parts[0]); // 1-based from server
        for (int i = 0; i < _state.NpcShopCount; i++)
        {
            if (_state.NpcShopItems[i].Slot == targetSlot)
            {
                _state.NpcShopItems[i].Name = parts[1];
                _state.NpcShopItems[i].Amount = ParseInt(parts[2]);
                _state.NpcShopItems[i].Price = long.TryParse(parts[3].Trim(), out long p) ? p : 0;
                _state.NpcShopItems[i].GrhIndex = ParseInt(parts[4]);
                _state.NpcShopItems[i].ObjIndex = ParseInt(parts[5]);
                _state.NpcShopItems[i].ObjType = ParseInt(parts[6]);
                _state.NpcShopItems[i].MaxHit = ParseInt(parts[7]);
                _state.NpcShopItems[i].MinHit = ParseInt(parts[8]);
                _state.NpcShopItems[i].MaxDef = ParseInt(parts[9]);
                return;
            }
        }
    }

    private void HandleInitCom()
    {
        _state.Comerciando = true;
        GD.Print($"[PKT] INITCOM: NPC shop opened ({_state.NpcShopCount} items)");
    }

    private void HandleTransOk(string data)
    {
        // TRANSOK{slot},{type} — type: 0=buy, 1=sell
        GD.Print($"[PKT] TRANSOK: {data}");
    }

    private void HandleFinComOk()
    {
        _state.Comerciando = false;
        GD.Print("[PKT] FINCOMOK: NPC shop closed");
    }

    // ── Sound ─────────────────────────────────────────────────────

    /// <summary>
    /// TW{soundId} — Play a sound effect. The entire game audio depends on this.
    /// Sound files are loaded from Data/Sounds/ directory.
    /// </summary>
    private void HandlePlaySound(string data)
    {
        int soundId = ParseInt(data);
        if (soundId > 0)
        {
            // TODO: integrate with Godot AudioStreamPlayer when sound system is ready
            // For now, log it so we know sounds are being received
            GD.Print($"[SND] Play sound: {soundId}");
        }
    }

    // ── Combat feedback ──────────────────────────────────────────

    /// <summary>
    /// N4{bodyPart},{damage},{attackerName} — PvP damage received.
    /// </summary>
    private void HandlePvpDamageReceived(string data)
    {
        var parts = data.Split(',');
        if (parts.Length >= 3)
        {
            int bodyPart = ParseInt(parts[0]);
            int damage = ParseInt(parts[1]);
            string attacker = parts[2];
            string bodyName = GetBodyPartName(bodyPart);
            _state.ChatMessages.Enqueue(new ChatMessage
            {
                Text = $"{attacker} te ha golpeado en la {bodyName} por {damage} puntos de daño.",
                Color = "FF0000"
            });
        }
    }

    /// <summary>
    /// N5{bodyPart},{damage},{victimName} — PvP damage dealt.
    /// </summary>
    private void HandlePvpDamageDealt(string data)
    {
        var parts = data.Split(',');
        if (parts.Length >= 3)
        {
            int bodyPart = ParseInt(parts[0]);
            int damage = ParseInt(parts[1]);
            string victim = parts[2];
            string bodyName = GetBodyPartName(bodyPart);
            _state.ChatMessages.Enqueue(new ChatMessage
            {
                Text = $"Le has golpeado a {victim} en la {bodyName} por {damage} puntos de daño.",
                Color = "FF0000"
            });
        }
    }

    /// <summary>
    /// U1 — User attack missed (swing and miss).
    /// </summary>
    private void HandleUserMissed()
    {
        _state.ChatMessages.Enqueue(new ChatMessage
        {
            Text = "¡Le has errado!",
            Color = "FF0000"
        });
    }

    /// <summary>
    /// U2{damage} — User dealt damage to NPC.
    /// </summary>
    private void HandleUserDamageDealt(string data)
    {
        int damage = ParseInt(data);
        _state.ChatMessages.Enqueue(new ChatMessage
        {
            Text = $"Le has pegado a la criatura por {damage} puntos de daño.",
            Color = "FF0000"
        });
    }

    /// <summary>
    /// U3{attackerName} — User's attack was evaded/dodged.
    /// </summary>
    private void HandleUserEvaded(string data)
    {
        string attacker = data.Trim();
        if (attacker.Length > 0)
        {
            _state.ChatMessages.Enqueue(new ChatMessage
            {
                Text = $"{attacker} ha esquivado tu ataque.",
                Color = "FF0000"
            });
        }
        else
        {
            _state.ChatMessages.Enqueue(new ChatMessage
            {
                Text = "La criatura ha esquivado tu ataque.",
                Color = "FF0000"
            });
        }
    }

    /// <summary>
    /// N1 — NPC attack missed user.
    /// </summary>
    private void HandleNpcMissed()
    {
        _state.ChatMessages.Enqueue(new ChatMessage
        {
            Text = "La criatura te ha errado.",
            Color = "FF0000"
        });
    }

    /// <summary>
    /// N2{bodyPart},{damage} — NPC damage received by user.
    /// </summary>
    private void HandleNpcDamageReceived(string data)
    {
        var parts = data.Split(',');
        if (parts.Length >= 2)
        {
            int bodyPart = ParseInt(parts[0]);
            int damage = ParseInt(parts[1]);
            string bodyName = GetBodyPartName(bodyPart);
            _state.ChatMessages.Enqueue(new ChatMessage
            {
                Text = $"La criatura te ha golpeado en la {bodyName} por {damage} puntos de daño.",
                Color = "FF0000"
            });
        }
    }

    /// <summary>
    /// 6 — NPC killed user (death by creature).
    /// </summary>
    private void HandleNpcKilledUser()
    {
        _state.ChatMessages.Enqueue(new ChatMessage
        {
            Text = "¡La criatura te ha matado!",
            Color = "FF0000"
        });
    }

    private static string GetBodyPartName(int bodyPart)
    {
        return bodyPart switch
        {
            1 => "cabeza",
            2 => "brazo izquierdo",
            3 => "brazo derecho",
            4 => "pierna izquierda",
            5 => "pierna derecha",
            6 => "torso",
            _ => "cabeza"
        };
    }

    // ── Death & status ───────────────────────────────────────────

    /// <summary>
    /// MUERT — Death dialog. VB6: shows frmMuertito (Continuar / Regresar).
    /// The actual body→casper change happens via CP packet (sent separately by server).
    /// This packet just triggers the death UI/notification.
    /// </summary>
    private void HandleDeath()
    {
        _state.Dead = true;
        _state.ShowDeathPanel = true;
        _state.ChatMessages.Enqueue(new ChatMessage
        {
            Text = "¡Has muerto!",
            Color = "FF0000"
        });
        GD.Print("[GAME] Player died — MUERT received");
    }

    /// <summary>
    /// FINOK — Graceful logout confirmation.
    /// </summary>
    private void HandleFinOk()
    {
        GD.Print("[GAME] FINOK: Graceful logout");
        // Client should disconnect cleanly
    }

    /// <summary>
    /// MEDOK — Toggle meditation off.
    /// VB6: clears meditation FX from self character.
    /// </summary>
    private void HandleMeditationOff()
    {
        _state.Meditating = false;
        if (_state.Characters.TryGetValue(_state.UserCharIndex, out var ch))
        {
            ClearMeditationFx(ch);
        }
    }

    /// <summary>
    /// NOVER{charIndex},{0|1} — Hide/reveal character.
    /// 1=invisible (stealth), 0=visible.
    /// </summary>
    private void HandleCharVisibility(string data)
    {
        var parts = data.Split(',');
        if (parts.Length >= 2)
        {
            int charIdx = ParseInt(parts[0]);
            bool invisible = ParseInt(parts[1]) == 1;
            if (_state.Characters.TryGetValue(charIdx, out var ch))
            {
                ch.Invisible = invisible;
            }
        }
    }

    /// <summary>
    /// NVG{charIndex},{0|1} — Character navigation state (boat/walking).
    /// </summary>
    private void HandleCharNavigation(string data)
    {
        var parts = data.Split(',');
        if (parts.Length >= 2)
        {
            int charIdx = ParseInt(parts[0]);
            bool navigating = ParseInt(parts[1]) == 1;
            if (_state.Characters.TryGetValue(charIdx, out var ch))
            {
                ch.Navigating = navigating;
            }
        }
    }

    // ── Arrow/projectile ─────────────────────────────────────────

    /// <summary>
    /// FLECHI{shooterCharIndex},{targetCharIndex},{arrowGrh} — Arrow projectile.
    /// Creates a visual arrow flying from shooter to target.
    /// </summary>
    private void HandleArrow(string data)
    {
        var parts = data.Split(',');
        if (parts.Length >= 3)
        {
            int shooter = ParseInt(parts[0]);
            int target = ParseInt(parts[1]);
            int arrowGrh = ParseInt(parts[2]);

            if (_state.Characters.TryGetValue(shooter, out var shooterCh) &&
                _state.Characters.TryGetValue(target, out var targetCh))
            {
                var arrow = new ArrowProjectile
                {
                    ShooterCharIndex = shooter,
                    TargetCharIndex = target,
                    GrhIndex = arrowGrh,
                    X = shooterCh.PosX * 32f,
                    Y = shooterCh.PosY * 32f,
                    TargetX = targetCh.PosX * 32f,
                    TargetY = targetCh.PosY * 32f,
                    Active = true
                };
                _state.ActiveArrows.Add(arrow);
            }
        }
    }

    // ── Chat: Guild & Clan ───────────────────────────────────────

    /// <summary>
    /// G|{text}~r~g~b — Guild/faction chat message.
    /// </summary>
    private void HandleGuildChat(string data)
    {
        var tildeParts = data.Split('~');
        string text = tildeParts[0];
        string color = "00FF00"; // default green for guild
        if (tildeParts.Length >= 4)
        {
            int r = ParseInt(tildeParts[1]);
            int g = ParseInt(tildeParts[2]);
            int b = ParseInt(tildeParts[3]);
            color = $"{r:X2}{g:X2}{b:X2}";
        }
        _state.ChatMessages.Enqueue(new ChatMessage { Text = text, Color = color });
    }

    /// <summary>
    /// C|{text} — Clan/party chat message.
    /// </summary>
    private void HandleClanChat(string data)
    {
        var tildeParts = data.Split('~');
        string text = tildeParts[0];
        string color = "FFFF00"; // default yellow for clan
        if (tildeParts.Length >= 4)
        {
            int r = ParseInt(tildeParts[1]);
            int g = ParseInt(tildeParts[2]);
            int b = ParseInt(tildeParts[3]);
            color = $"{r:X2}{g:X2}{b:X2}";
        }
        _state.ChatMessages.Enqueue(new ChatMessage { Text = text, Color = color });
    }

    /// <summary>
    /// !!{text}\x1b — GM broadcast message.
    /// </summary>
    private void HandleGmBroadcast(string data)
    {
        // Strip trailing ESC char if present
        string text = data.TrimEnd('\x1b');
        _state.ChatMessages.Enqueue(new ChatMessage { Text = text, Color = "FFFF00" });
        GD.Print($"[GM] Broadcast: {text}");
    }

    /// <summary>
    /// RESPUES{text}*{adminName} — Admin response to SOS.
    /// </summary>
    private void HandleAdminResponse(string data)
    {
        var parts = data.Split('*');
        string text = parts.Length >= 2 ? $"[{parts[1]}] {parts[0]}" : data;
        _state.ChatMessages.Enqueue(new ChatMessage { Text = text, Color = "00FFFF" });
    }

    /// <summary>
    /// IRCHAT{senderName},{text} — Incoming friend chat message.
    /// </summary>
    private void HandleFriendChat(string data)
    {
        int commaIdx = data.IndexOf(',');
        if (commaIdx > 0)
        {
            string sender = data[..commaIdx];
            string text = data[(commaIdx + 1)..];
            _state.ChatMessages.Enqueue(new ChatMessage
            {
                Text = $"[Chat] {sender}: {text}",
                Color = "00FFFF"
            });
        }
    }

    // ── Trading (player-to-player) ───────────────────────────────

    /// <summary>
    /// ICO — Trade initiated.
    /// </summary>
    private void HandleTradeInit()
    {
        _state.Trading = true;
        GD.Print("[PKT] ICO: Trade initiated");
    }

    /// <summary>
    /// TRADEOK — Trade completed successfully.
    /// </summary>
    private void HandleTradeOk()
    {
        _state.Trading = false;
        _state.ChatMessages.Enqueue(new ChatMessage
        {
            Text = "Comercio exitoso.",
            Color = "00FF00"
        });
        GD.Print("[PKT] TRADEOK: Trade completed");
    }

    /// <summary>
    /// CANCELTRADE — Trade cancelled.
    /// </summary>
    private void HandleTradeCancelled()
    {
        _state.Trading = false;
        _state.ChatMessages.Enqueue(new ChatMessage
        {
            Text = "Comercio cancelado.",
            Color = "FF0000"
        });
        GD.Print("[PKT] CANCELTRADE: Trade cancelled");
    }

    /// <summary>
    /// IOR{goldOffered} — Trade gold offer received.
    /// </summary>
    private void HandleTradeGold(string data)
    {
        int gold = ParseInt(data);
        GD.Print($"[PKT] IOR: Trade gold offer: {gold}");
    }

    /// <summary>
    /// ICI{objIndex}-{amount}-{objName} — Trade item offered.
    /// </summary>
    private void HandleTradeItem(string data)
    {
        GD.Print($"[PKT] ICI: Trade item: {data}");
    }

    /// <summary>
    /// VCC{senderName}: {message} — Trade chat message.
    /// </summary>
    private void HandleTradeChat(string data)
    {
        _state.ChatMessages.Enqueue(new ChatMessage { Text = data, Color = "FFFFFF" });
    }

    // ── Dialog removal ───────────────────────────────────────────

    /// <summary>
    /// QDL{charIndex} — Remove dialog from character.
    /// </summary>
    private void HandleRemoveDialog(string data)
    {
        int charIdx = ParseInt(data);
        if (_state.Characters.TryGetValue(charIdx, out var ch))
        {
            ch.DialogText = "";
            ch.DialogDurationMs = 0;
        }
    }

    /// <summary>
    /// QTDL — Remove self dialog.
    /// </summary>
    private void HandleRemoveSelfDialog()
    {
        if (_state.Characters.TryGetValue(_state.UserCharIndex, out var ch))
        {
            ch.DialogText = "";
            ch.DialogDurationMs = 0;
        }
    }

    // ── Spells ───────────────────────────────────────────────────

    /// <summary>
    /// SHI{slot},{spellName} — Hide/rename spell in slot.
    /// </summary>
    private void HandleHideSpell(string data)
    {
        var parts = data.Split(',');
        if (parts.Length >= 2)
        {
            int slot = ParseInt(parts[0]);
            if (slot >= 1 && slot <= 20)
            {
                _state.Spells[slot - 1] = new SpellSlot
                {
                    SpellId = 0,
                    Name = parts[1],
                };
            }
        }
    }

    // ── Dice ─────────────────────────────────────────────────────

    /// <summary>
    /// DADOS{result} — Dice roll result.
    /// </summary>
    private void HandleDados(string data)
    {
        _state.ChatMessages.Enqueue(new ChatMessage
        {
            Text = $"Tiraste los dados: {data}",
            Color = "FFFF00"
        });
    }

    private static int ParseInt(string s)
    {
        return int.TryParse(s.Trim(), out int v) ? v : 0;
    }

    /// <summary>
    /// VB6: head values 500, 501, 511, 512 indicate a dead (ghost/casper) character.
    /// TCP.bas: If charlist(charindex).Head.Walk(3).GrhIndex = 500/501/511/512 Then .Muerto = True
    /// </summary>
    private static bool IsDeadHead(int head)
    {
        return head == 500 || head == 501 || head == 511 || head == 512;
    }
}
