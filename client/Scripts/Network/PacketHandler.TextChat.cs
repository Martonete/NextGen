using System;
using Godot;
using ArgentumNextgen.Data;
using ArgentumNextgen.Game;

namespace ArgentumNextgen.Network;

/// <summary>
/// Text-based packet handlers: Chat, Console messages, FX, Sound, Arrows
/// </summary>
public partial class PacketHandler
{

    private void HandleConsoleMessage(string data)
    {
        // VB6 format: ||<TextID>@<param1>@<param2>@...
        var parts = data.Split('@', 10);

        if (parts.Length >= 1 && int.TryParse(parts[0], out int textId)
            && textId > 0 && textId < _state.TextMessages.Length)
        {
            var tmpl = _state.TextMessages[textId];
            string text = tmpl.Text;

            for (int i = 1; i < parts.Length && i <= 8; i++)
            {
                text = text.Replace($"%{i}", parts[i]);
            }

            string color = Data.FontTypes.GetHexColor(tmpl.FontId);
            var type = ChatType.System;
            if (tmpl.FontId == 19) type = ChatType.Combat;
            else if (tmpl.FontId == 25) type = ChatType.Party;
            else if (tmpl.FontId == 27 || tmpl.FontId == 31) type = ChatType.Clan;
            else if (tmpl.FontId == 3 || tmpl.FontId == 43 || tmpl.FontId == 44 || tmpl.FontId == 45) type = ChatType.Global;
            _state.ChatMessages.Enqueue(new ChatMessage { Text = text, Color = color, Type = type });
            GD.Print($"[CONSOLE] [{textId}] {text}");
        }
        else
        {
            string text = parts.Length > 1 ? string.Join(" ", parts[1..]) : data;
            _state.ChatMessages.Enqueue(new ChatMessage { Text = text, Color = "45BE9C" });
            GD.Print($"[CONSOLE] (raw) {text}");
        }
    }

    private void HandleTalk(string data)
    {
        // T|<color>°<text>°<charindex>  (° = ASCII 176)
        var parts = data.Split((char)176, 4);
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
            _state.ChatMessages.Enqueue(new ChatMessage { Text = text, Color = color, Type = ChatType.Global });
    }

    private void HandleYell(string data)
    {
        var parts = data.Split((char)176, 4);

        if (parts.Length >= 2)
        {
            string color = "FF0000";
            if (int.TryParse(parts[0], out int vbColor))
            {
                int r = vbColor & 0xFF;
                int g = (vbColor >> 8) & 0xFF;
                int b = (vbColor >> 16) & 0xFF;
                color = $"{r:X2}{g:X2}{b:X2}";
            }

            string text = parts[1];

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
                _state.ChatMessages.Enqueue(new ChatMessage { Text = text, Color = color, Type = ChatType.Global });
        }
        else
        {
            var tildeParts = data.Split('~', 5);
            string text = tildeParts[0];
            string color = "45BE9C";
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

    private void HandleWhisper(string data)
    {
        var parts = data.Split('~', 5);
        if (parts.Length >= 4)
        {
            int r = ParseInt(parts[1]);
            int g = ParseInt(parts[2]);
            int b = ParseInt(parts[3]);
            string color = $"{r:X2}{g:X2}{b:X2}";
            _state.ChatMessages.Enqueue(new ChatMessage { Text = parts[0], Color = color, Type = ChatType.Whisper });
        }
        else if (parts.Length >= 1)
        {
            _state.ChatMessages.Enqueue(new ChatMessage { Text = parts[0], Color = "00FFFF", Type = ChatType.Whisper });
        }
        GD.Print($"[WHISPER] {parts[0]}");
    }

    private void HandleGuildChat(string data)
    {
        var tildeParts = data.Split('~', 5);
        string text = tildeParts[0];
        string color = "00FF00";
        if (tildeParts.Length >= 4)
        {
            int r = ParseInt(tildeParts[1]);
            int g = ParseInt(tildeParts[2]);
            int b = ParseInt(tildeParts[3]);
            color = $"{r:X2}{g:X2}{b:X2}";
        }
        _state.ChatMessages.Enqueue(new ChatMessage { Text = text, Color = color });
    }

    private void HandleClanChat(string data)
    {
        var tildeParts = data.Split('~', 5);
        string text = tildeParts[0];
        string color = "FFFF00";
        if (tildeParts.Length >= 4)
        {
            int r = ParseInt(tildeParts[1]);
            int g = ParseInt(tildeParts[2]);
            int b = ParseInt(tildeParts[3]);
            color = $"{r:X2}{g:X2}{b:X2}";
        }
        _state.ChatMessages.Enqueue(new ChatMessage { Text = text, Color = color });
    }

    private void HandleGmBroadcast(string data)
    {
        string text = data.TrimEnd('\x1b');
        _state.MensajeText = text;
        GD.Print($"[GM] Message box: {text}");
    }

    private void HandleAdminResponse(string data)
    {
        var parts = data.Split('*', 3);
        string text = parts.Length >= 2 ? $"[{parts[1]}] {parts[0]}" : data;
        _state.ChatMessages.Enqueue(new ChatMessage { Text = text, Color = "00FFFF" });
    }

    // ── FX / Emotes / Particles ─────────────────────────────────

    /// <summary>
    /// CFX{charindex},{fxId},{loops} — Apply FX animation to a character.
    /// </summary>
    private void HandleCharFx(string data)
    {
        var parts = data.Split(',', 4);
        if (parts.Length < 3) return;

        int charIdx = ParseInt(parts[0]);
        int fxId = ParseInt(parts[1]);
        int loops = ParseInt(parts[2]);

        if (!_state.Characters.TryGetValue(charIdx, out var ch))
            return;

        if (fxId == 0)
        {
            for (int i = 0; i < 3; i++)
            {
                ch.ActiveFxSlots[i] = 0;
                ch.FxLoops[i] = 0;
                ch.FxFrameCounter[i] = 0;
            }
            return;
        }

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

        ch.ActiveFxSlots[0] = fxId;
        ch.FxLoops[0] = loops >= 999 ? -1 : loops;
        ch.FxFrameCounter[0] = 0;
    }

    /// <summary>
    /// CFF — Character particle stream.
    /// </summary>
    private void HandleCharParticle(string data)
    {
        var parts = data.Split(',', 3);
        if (parts.Length < 2) return;

        int charIdx = ParseInt(parts[0]);
        int streamId = ParseInt(parts[1]);

        if (streamId == 0)
        {
            for (int i = _state.MapParticles.Count - 1; i >= 0; i--)
            {
                if (_state.MapParticles[i].CharIndex == charIdx && _state.MapParticles[i].CharIndex > 0)
                    _state.MapParticles.RemoveAt(i);
            }
            return;
        }

        for (int i = _state.MapParticles.Count - 1; i >= 0; i--)
        {
            if (_state.MapParticles[i].CharIndex == charIdx && _state.MapParticles[i].CharIndex > 0)
                _state.MapParticles.RemoveAt(i);
        }

        ParticleSystem.CreateCharStream(_state, streamId, charIdx);
    }

    // ── Sound / Music ────────────────────────────────────────────

    private void HandlePlaySound(string data)
    {
        int soundId = ParseInt(data);
        if (soundId > 0)
            OnPlaySound?.Invoke(soundId);
    }

    private void HandleMusic(string data)
    {
        _state.MusicId = ParseInt(data);
        OnPlayMusic?.Invoke(_state.MusicId);
    }

    private void HandleOnlineCount(string data)
    {
        _state.OnlineCount = ParseInt(data);
        GD.Print($"[GAME] Online: {_state.OnlineCount}");
    }

    // ── Arrow/projectile ─────────────────────────────────────────

    private void HandleArrow(string data)
    {
        var parts = data.Split(',', 4);
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
}
