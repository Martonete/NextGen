using System;
using System.Collections.Generic;
using Godot;
using TierrasSagradasAO.Game;

namespace TierrasSagradasAO.Network;

/// <summary>
/// Dispatches inbound packets by opcode prefix to handler methods.
/// Updates GameState based on server packets.
/// </summary>
public class PacketHandler
{
    private readonly GameState _state;

    public PacketHandler(GameState state)
    {
        _state = state;
    }

    public void HandlePacket(string packet)
    {
        if (string.IsNullOrEmpty(packet)) return;

        // Multi-char opcodes first (longest match)
        if (packet.StartsWith("LOGGED"))
        {
            HandleLogged();
        }
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
        else if (packet.StartsWith("ERR"))
        {
            HandleError(packet[3..]);
        }
        else if (packet.StartsWith("PARADOK"))
        {
            _state.UserParalyzed = !_state.UserParalyzed;
        }
        else if (packet.StartsWith("NAVEG"))
        {
            _state.UserNavigating = !_state.UserNavigating;
        }
        else if (packet.StartsWith("STOPD"))
        {
            _state.UserStopped = packet.Length > 5 && packet[5] == '1';
        }
        else if (packet.StartsWith("EHYS"))
        {
            HandleHungerThirst(packet[4..]);
        }
        else if (packet.StartsWith("INVI"))
        {
            // Inventory init, ignore
        }
        else if (packet.StartsWith("CSI"))
        {
            HandleInventorySlot(packet[3..]);
        }
        else if (packet.StartsWith("SHS"))
        {
            HandleSpellSlot(packet[3..]);
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
        else if (packet.StartsWith("BKW"))
        {
            HandleTogglePause();
        }
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
        else if (packet.StartsWith("N~"))
        {
            _state.MapName = packet[2..];
        }
        else if (packet.StartsWith("[ES"))
        {
            HandleBulkStats(packet[3..]);
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
        else if (packet.StartsWith("[CD"))
        {
            // Combat data, ignore for now
        }
        else if (packet.StartsWith("ANM"))
        {
            // Equipment stats, ignore for now
        }
        else if (packet.StartsWith("PX"))
        {
            // Status broadcast, ignore for now
        }
        else if (packet.StartsWith("||"))
        {
            HandleConsoleMessage(packet[2..]);
        }
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
        else if (packet.StartsWith("+"))
        {
            HandleMoveChar(packet[1..]);
        }
        else
        {
            GD.Print($"[PKT] Unhandled: {(packet.Length > 40 ? packet[..40] + "..." : packet)}");
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
            _state.NeedMapLoad = true;

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

            GD.Print($"[GAME] Change map: {_state.CurrentMap} (cleared {toRemove.Count} chars, all ground objects)");
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
        var ch = new Character
        {
            CharIndex = charIndex,
            Body = ParseInt(parts[0]),
            Head = ParseInt(parts[1]),
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

        _state.Characters[charIndex] = ch;
    }

    private void HandleChangeChar(string data)
    {
        // CP<charindex>,<body>,<head>,<heading>,<weapon>,<shield>,<casco>
        var parts = data.Split(',');
        if (parts.Length < 7) return;

        int idx = ParseInt(parts[0]);
        if (_state.Characters.TryGetValue(idx, out var ch))
        {
            ch.Body = ParseInt(parts[1]);
            ch.Head = ParseInt(parts[2]);
            ch.Heading = ParseInt(parts[3]);
            ch.WeaponAnim = ParseInt(parts[4]);
            ch.ShieldAnim = ParseInt(parts[5]);
            ch.CascoAnim = ParseInt(parts[6]);
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

            // Erase characters outside the area
            var toRemove = new List<int>();
            foreach (var kvp in _state.Characters)
            {
                if (kvp.Key == _state.UserCharIndex) continue;
                var ch = kvp.Value;
                if (ch.PosX < minLimX || ch.PosX > maxLimX ||
                    ch.PosY < minLimY || ch.PosY > maxLimY)
                {
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
        var parts = data.Split(',');
        if (parts.Length >= 11)
        {
            int slot = ParseInt(parts[0]);
            if (slot >= 1 && slot <= 25)
            {
                _state.Inventory[slot - 1] = new InventorySlot
                {
                    ObjIndex = ParseInt(parts[1]),
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
        // ||<color_code>@<source>@<text> or just ||<text>
        string text = data;
        string color = "00FF00"; // default green for console

        int atIdx = data.IndexOf('@');
        if (atIdx >= 0)
        {
            // Try to parse color code before first @
            string possibleColor = data[..atIdx];
            if (possibleColor.Length <= 6 && possibleColor.Length >= 1)
            {
                int secondAt = data.IndexOf('@', atIdx + 1);
                if (secondAt > 0)
                    text = data[(secondAt + 1)..];
                else
                    text = data[(atIdx + 1)..];
            }
        }

        _state.ChatMessages.Enqueue(new ChatMessage { Text = text, Color = color });
        GD.Print($"[CONSOLE] {text}");
    }

    private void HandleTalk(string data)
    {
        // T|<color>°<text>°<charindex>
        var parts = data.Split((char)176); // ° = ASCII 176
        if (parts.Length >= 2)
        {
            string color = "FFFFFF"; // default white
            if (parts.Length >= 1 && int.TryParse(parts[0], out int vbColor))
            {
                // VB6 color: BGR format, convert to RGB hex
                int r = vbColor & 0xFF;
                int g = (vbColor >> 8) & 0xFF;
                int b = (vbColor >> 16) & 0xFF;
                color = $"{r:X2}{g:X2}{b:X2}";
            }

            _state.ChatMessages.Enqueue(new ChatMessage { Text = parts[1], Color = color });
            GD.Print($"[TALK] {parts[1]}");
        }
    }

    private void HandleYell(string data)
    {
        var parts = data.Split((char)176);
        if (parts.Length >= 2)
        {
            string color = "FF0000"; // yell is usually red
            if (int.TryParse(parts[0], out int vbColor))
            {
                int r = vbColor & 0xFF;
                int g = (vbColor >> 8) & 0xFF;
                int b = (vbColor >> 16) & 0xFF;
                color = $"{r:X2}{g:X2}{b:X2}";
            }

            _state.ChatMessages.Enqueue(new ChatMessage { Text = parts[1], Color = color });
            GD.Print($"[YELL] {parts[1]}");
        }
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

    private static int ParseInt(string s)
    {
        return int.TryParse(s.Trim(), out int v) ? v : 0;
    }
}
