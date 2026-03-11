using System;
using System.Collections.Generic;
using Godot;
using ArgentumNextgen.Data;
using ArgentumNextgen.Game;

namespace ArgentumNextgen.Network;

/// <summary>
/// Text-based packet handlers: Map, Position, Characters, Objects, Area
/// </summary>
public partial class PacketHandler
{

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

            // Reset scroll/movement state to prevent black flash.
            _state.ScreenOffsetX = 0;
            _state.ScreenOffsetY = 0;
            _state.AddToUserPosX = 0;
            _state.AddToUserPosY = 0;
            _state.UserMoving = false;
            if (_state.Characters.TryGetValue(_state.UserCharIndex, out var selfCh))
            {
                selfCh.MoveOffsetX = 0;
                selfCh.MoveOffsetY = 0;
                selfCh.Moving = false;
                selfCh.ScrollDirectionX = 0;
                selfCh.ScrollDirectionY = 0;
            }

            // Load the map file IMMEDIATELY so that subsequent BQ/HO packets
            // in this same batch apply to the correct (new) MapData.
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

        ch.Dead = IsDeadHead(head);

        // NPC CC format has 15 fields
        if (parts.Length >= 15)
        {
            ch.NpcAura = ParseInt(parts[13]);
            ch.NpcNumber = ParseInt(parts[14]);
        }

        _state.Characters[charIndex] = ch;

        GD.Print($"[CC] {ch.Name} idx={charIndex} body={ch.Body} head={ch.Head} weapon={ch.WeaponAnim} shield={ch.ShieldAnim} casco={ch.CascoAnim} (raw parts[7]={parts[7]})");

        if (ch.Body <= 0)
            GD.PrintErr($"[CC] WARNING: char {ch.Name} (idx={charIndex}) has body=0!");
    }

    private void HandleChangeChar(string data)
    {
        // VB6 CP format: CP<charindex>,<body>,<head>,<heading>,<weapon>,<shield>,<fx>,<loops>,<casco>
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
            if (parts.Length >= 9) ch.CascoAnim = ParseInt(parts[8]);
            else if (parts.Length == 7) ch.CascoAnim = ParseInt(parts[6]);
            ch.Dead = nowDead;

            if (wasDead != nowDead)
            {
                ch.TransparenciaBody = 0;
                ch.Llegoalatransp = false;
            }

            if (idx == _state.UserCharIndex)
            {
                if (nowDead && !wasDead)
                {
                    _state.Dead = true;
                    OnPlaySound?.Invoke(SoundManager.SND_DEATH);
                    GD.Print($"[CP] User character died (head={newHead})");
                }
                else if (!nowDead && wasDead)
                {
                    _state.Dead = false;
                    OnPlaySound?.Invoke(SoundManager.SND_REVIVE);
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
        var parts = data.Split(',');
        if (parts.Length < 3) return;

        int idx = ParseInt(parts[0]);
        int newX = ParseInt(parts[1]);
        int newY = ParseInt(parts[2]);

        if (!_state.Characters.TryGetValue(idx, out var ch))
            return;

        if (idx == _state.UserCharIndex)
            return;

        {
            ClearMeditationFx(ch);

            int dx = newX - ch.PosX;
            int dy = newY - ch.PosY;

            if (dy < 0) ch.Heading = 1;
            else if (dx > 0) ch.Heading = 2;
            else if (dy > 0) ch.Heading = 3;
            else if (dx < 0) ch.Heading = 4;

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
        if (packet.Length >= 4)
        {
            int playerX = (byte)packet[2];
            int playerY = (byte)packet[3];

            int minLimX = (playerX / 9 - 1) * 9;
            int maxLimX = minLimX + 26;
            int minLimY = (playerY / 9 - 1) * 9;
            int maxLimY = minLimY + 26;

            const int HalfW = 8, HalfH = 6, VisMargin = 3;
            int visMinX = playerX - HalfW - VisMargin;
            int visMaxX = playerX + HalfW + VisMargin;
            int visMinY = playerY - HalfH - VisMargin;
            int visMaxY = playerY + HalfH + VisMargin;

            var toRemove = new List<int>();
            foreach (var kvp in _state.Characters)
            {
                if (kvp.Key == _state.UserCharIndex) continue;
                var ch = kvp.Value;
                if (ch.PosX < minLimX || ch.PosX > maxLimX ||
                    ch.PosY < minLimY || ch.PosY > maxLimY)
                {
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
    /// </summary>
    private void HandleCharData(string data)
    {
        var parts = data.Split(',');
        if (parts.Length < 2) return;

        int idx = ParseInt(parts[0]);
        if (!_state.Characters.TryGetValue(idx, out var ch)) return;

        if (parts.Length >= 7)
        {
            ch.AuraIndexA = ParseInt(parts[2]);
            ch.AuraIndexW = ParseInt(parts[3]);
            ch.AuraIndexE = ParseInt(parts[4]);
            ch.AuraIndexR = ParseInt(parts[5]);
            ch.AuraIndexC = ParseInt(parts[6]);
        }

        if (parts.Length >= 8)
        {
            ch.Levitating = ParseInt(parts[7]) == 1;
        }
    }

    /// <summary>
    /// USM — User mount state update.
    /// </summary>
    private void HandleUserMount(string data)
    {
        var parts = data.Split(',');
        if (parts.Length < 2) return;

        int idx = ParseInt(parts[0]);
        bool mounted = parts[1] == "1";

        if (_state.Characters.TryGetValue(idx, out var ch))
            ch.Mounted = mounted;

        if (idx == _state.UserCharIndex)
            _state.UserMounted = mounted;
    }

    /// <summary>
    /// MVOL — Mount flying/levitation state.
    /// </summary>
    private void HandleMountFly(string data)
    {
        var parts = data.Split(',');
        if (parts.Length < 2) return;

        int idx = ParseInt(parts[0]);
        bool levitating = parts[1] == "1";

        if (_state.Characters.TryGetValue(idx, out var ch))
            ch.Levitating = levitating;
    }

    /// <summary>
    /// AU| — Aura update broadcast.
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
    /// NOVER{charIndex},{0|1} — Hide/reveal character.
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
                ch.TransparenciaBody = 0;
                ch.Llegoalatransp = false;
            }
        }
    }

    /// <summary>
    /// NVG{charIndex},{0|1} — Character navigation state.
    /// </summary>
    private void HandleCharNavigation(string data)
    {
        var parts = data.Split(',');
        if (parts.Length >= 2)
        {
            int charIdx = ParseInt(parts[0]);
            bool navigating = ParseInt(parts[1]) == 1;
            if (_state.Characters.TryGetValue(charIdx, out var ch))
                ch.Navigating = navigating;
        }
    }

    private void HandleTogglePause()
    {
        _state.Paused = !_state.Paused;
    }

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
}
