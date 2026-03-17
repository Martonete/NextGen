using System;
using Godot;
using ArgentumNextgen.Data;
using ArgentumNextgen.Game;

namespace ArgentumNextgen.Network;

/// <summary>
/// Text-based packet handlers: Stats, Combat feedback, Death, Login flow
/// </summary>
public partial class PacketHandler
{

    private void HandleBulkStats(string data)
    {
        var parts = data.Split(',', 15);
        if (parts.Length >= 10)
        {
            _state.MaxHp = Math.Max(1, ParseInt(parts[0]));
            _state.MinHp = Math.Max(0, ParseInt(parts[1]));
            _state.MaxMana = Math.Max(1, ParseInt(parts[2]));
            _state.MinMana = Math.Max(0, ParseInt(parts[3]));
            _state.MaxSta = Math.Max(1, ParseInt(parts[4]));
            _state.MinSta = Math.Max(0, ParseInt(parts[5]));
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
        var parts = data.Split(',', 3);
        if (parts.Length >= 2)
        {
            _state.MaxHp = Math.Max(1, ParseInt(parts[0]));
            _state.MinHp = Math.Max(0, ParseInt(parts[1]));
        }
    }

    private void HandleManaStats(string data)
    {
        var parts = data.Split(',', 3);
        if (parts.Length >= 2)
        {
            _state.MaxMana = Math.Max(1, ParseInt(parts[0]));
            _state.MinMana = Math.Max(0, ParseInt(parts[1]));
        }
    }

    private void HandleStaStats(string data)
    {
        var parts = data.Split(',', 3);
        if (parts.Length >= 2)
        {
            _state.MaxSta = Math.Max(1, ParseInt(parts[0]));
            _state.MinSta = Math.Max(0, ParseInt(parts[1]));
        }
    }

    private void HandleGold(string data)
    {
        _state.Gold = ParseInt(data);
    }

    private void HandleExp(string data)
    {
        var parts = data.Split(',', 3);
        if (parts.Length >= 2)
        {
            _state.ExpNext = ParseInt(parts[0]);
            _state.Exp = ParseInt(parts[1]);
        }
    }

    private void HandleHungerThirst(string data)
    {
        var parts = data.Split(',', 5);
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

    /// <summary>
    /// ANM — Equipment stats (20 comma-separated fields).
    /// </summary>
    private void HandleEquipmentStats(string data)
    {
        var parts = data.Split(',', 21);
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

    // ── Combat feedback ──────────────────────────────────────────

    /// <summary>
    /// N4{bodyPart},{damage},{attackerName} — PvP damage received.
    /// </summary>
    private void HandlePvpDamageReceived(string data)
    {
        var parts = data.Split(',', 4);
        if (parts.Length >= 3)
        {
            int bodyPart = ParseInt(parts[0]);
            int damage = ParseInt(parts[1]);
            string attacker = parts[2];
            string bodyName = GetPvpReceivedBodyPartText(bodyPart);
            _state.ChatMessages.Enqueue(new ChatMessage
            {
                Text = $"{attacker}{bodyName}{damage}",
                Color = "FF0000",
                Type = ChatType.Combat
            });
        }
    }

    /// <summary>
    /// N5{bodyPart},{damage},{victimName} — PvP damage dealt.
    /// </summary>
    private void HandlePvpDamageDealt(string data)
    {
        var parts = data.Split(',', 4);
        if (parts.Length >= 3)
        {
            int bodyPart = ParseInt(parts[0]);
            int damage = ParseInt(parts[1]);
            string victim = parts[2];
            string bodyName = GetPvpDealtBodyPartText(bodyPart);
            _state.ChatMessages.Enqueue(new ChatMessage
            {
                Text = $"Le has pegado a {victim}{bodyName}{damage}",
                Color = "FF0000",
                Type = ChatType.Combat
            });
        }
    }

    private void HandleUserMissed()
    {
        _state.ChatMessages.Enqueue(new ChatMessage
        {
            Text = "Has fallado el golpe!!!",
            Color = "FF0000",
            Type = ChatType.Combat
        });
    }

    private void HandleUserDamageDealt(string data)
    {
        int damage = ParseInt(data);
        _state.ChatMessages.Enqueue(new ChatMessage
        {
            Text = $"Le has pegado a la criatura por {damage}!!",
            Color = "FF0000",
            Type = ChatType.Combat
        });
    }

    private void HandleUserEvaded(string data)
    {
        string attacker = data.Trim();
        _state.ChatMessages.Enqueue(new ChatMessage
        {
            Text = $"{attacker} te ataco y fallo!!",
            Color = "FF0000",
            Type = ChatType.Combat
        });
    }

    private void HandleNpcMissed()
    {
        _state.ChatMessages.Enqueue(new ChatMessage
        {
            Text = "La criatura fallo el golpe!!!",
            Color = "FF0000",
            Type = ChatType.Combat
        });
    }

    private void HandleNpcDamageReceived(string data)
    {
        var parts = data.Split(',', 3);
        if (parts.Length >= 2)
        {
            int bodyPart = ParseInt(parts[0]);
            int damage = ParseInt(parts[1]);
            string bodyName = GetNpcHitBodyPartText(bodyPart);
            _state.ChatMessages.Enqueue(new ChatMessage
            {
                Text = $"{bodyName}{damage}",
                Color = "FF0000",
                Type = ChatType.Combat
            });
        }
    }

    private void HandleNpcKilledUser()
    {
        _state.ChatMessages.Enqueue(new ChatMessage
        {
            Text = "La criatura te ha matado!!!",
            Color = "FF0000",
            Type = ChatType.Combat
        });
    }

    // ── Death & status ───────────────────────────────────────────

    private void HandleDeath()
    {
        _state.Dead = true;
        _state.ShowDeathPanel = true;
        _state.ChatMessages.Enqueue(new ChatMessage { Type = ChatType.Combat,
            Text = "¡Has muerto!",
            Color = "FF0000"
        });
        OnPlaySound?.Invoke(SoundManager.SND_DEATH);
        GD.Print("[GAME] Player died — MUERT received");
    }

    private void HandleFinOk()
    {
        GD.Print("[GAME] FINOK: Graceful logout");
    }

    private void HandleMeditationOff()
    {
        _state.Meditating = false;
        if (_state.Characters.TryGetValue(_state.UserCharIndex, out var ch))
        {
            ClearMeditationFx(ch);
        }
    }

    private void HandleDados(string data)
    {
        _state.ChatMessages.Enqueue(new ChatMessage
        {
            Text = $"Tiraste los dados: {data}",
            Color = "FFFF00"
        });
    }

    // ── Login flow ───────────────────────────────────────────────

    private void HandleInitCharList(string data)
    {
        _state.CharacterList.Clear();
        var parts = data.Split(',', 2);
        if (parts.Length >= 2)
            _state.ServerNotice = parts[1];
        GD.Print($"[LOGIN] INIAC received, notice: {_state.ServerNotice}");
    }

    private void HandleAddCharPreview(string data)
    {
        var parts = data.Split(',', 12);
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
        _state.SecurityCode = data;
        _state.CurrentScreen = Screen.CharSelect;
        GD.Print($"[LOGIN] CODEH received, switching to char select");
    }

    private void HandleError(string data)
    {
        _state.LoginError = data;
        _state.MensajeText = data;
        GD.Print($"[LOGIN] ERR: {data}");
    }
}
