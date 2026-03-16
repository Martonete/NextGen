using System;
using Godot;
using ArgentumNextgen.Data;
using ArgentumNextgen.Game;

namespace ArgentumNextgen.Network;

/// <summary>
/// Binary packet handlers: Combat / Stats / HP / Mana / Damage / FX
/// </summary>
public partial class PacketHandler
{

    // ── Stats ─────────────────────────────────────────────────────

    private void HandleBinUpdateSta(ByteQueue bq)
    {
        short maxSta = bq.ReadInteger();
        short minSta = bq.ReadInteger();
        _state.MaxSta = maxSta;
        _state.MinSta = minSta;
    }


    private void HandleBinUpdateMana(ByteQueue bq)
    {
        short maxMana = bq.ReadInteger();
        short minMana = bq.ReadInteger();
        _state.MaxMana = maxMana;
        _state.MinMana = minMana;
    }


    private void HandleBinUpdateHp(ByteQueue bq)
    {
        short maxHp = bq.ReadInteger();
        short minHp = bq.ReadInteger();
        int oldHp = _state.MinHp;
        _state.MaxHp = maxHp;
        _state.MinHp = minHp;

        // Detect healing: HP increased and we already had HP data (oldHp > 0)
        int delta = minHp - oldHp;
        if (delta > 0 && oldHp > 0)
        {
            OnFloatingText?.Invoke(_state.UserCharIndex, $"+{delta}", "33FF33");
        }
    }


    private void HandleBinUpdateExp(ByteQueue bq)
    {
        int exp = bq.ReadLong();
        _state.Exp = exp;
    }


    private void HandleBinUpdateUserStats(ByteQueue bq)
    {
        _state.MaxHp = bq.ReadInteger();
        _state.MinHp = bq.ReadInteger();
        _state.MaxMana = bq.ReadInteger();
        _state.MinMana = bq.ReadInteger();
        _state.MaxSta = bq.ReadInteger();
        _state.MinSta = bq.ReadInteger();
        _state.Gold = bq.ReadLong();
        _state.Level = bq.ReadByte();
        _state.ExpNext = bq.ReadLong();
        _state.Exp = bq.ReadLong();
        GD.Print($"[GAME] Stats (binary): HP {_state.MinHp}/{_state.MaxHp} Mana {_state.MinMana}/{_state.MaxMana} Lvl {_state.Level}");
    }

    // ── Map / Position ────────────────────────────────────────────


    private void HandleBinAtributes(ByteQueue bq)
    {
        _state.Strength = bq.ReadByte();
        _state.Agility = bq.ReadByte();
        _state.Intelligence = bq.ReadByte();
        _state.Constitution = bq.ReadByte();
        _state.Charisma = bq.ReadByte();
    }


    private void HandleBinSendSkills(ByteQueue bq)
    {
        for (int i = 0; i < 20; i++)
        {
            byte skillVal = bq.ReadByte();
            if (i < _state.Skills.Length)
                _state.Skills[i] = skillVal;
        }
    }


    private void HandleBinFame(ByteQueue bq)
    {
        // VB6 fame order: Asesino, Bandido, Burgues, Ladron, Noble, Plebe, Promedio
        int[] fameValues = new int[7];
        for (int i = 0; i < 7; i++)
            fameValues[i] = bq.ReadLong();
        _state.FameAsesino = fameValues[0];
        _state.FameBandido = fameValues[1];
        _state.FameBurgues = fameValues[2];
        _state.FameLadron = fameValues[3];
        _state.FameNoble = fameValues[4];
        _state.FamePlebe = fameValues[5];
        _state.Reputation = fameValues[6]; // Promedio (index 6)
    }


    private void HandleBinMiniStats(ByteQueue bq)
    {
        int gold = bq.ReadLong();
        int exp = bq.ReadLong();
        _state.Gold = gold;
        _state.Exp = exp;
    }


    private void HandleBinLevelUp(ByteQueue bq)
    {
        short skillPoints = bq.ReadInteger();
        // VB6: SkillPts = SkillPts + Pts — accumulate, don't overwrite
        _state.FreeSkillPoints += skillPoints;
        _state.ChatMessages.Enqueue(new ChatMessage
        {
            Text = $"Has subido de nivel! Has ganado {skillPoints} skillpoints.",
            Color = "00FF00"
        });
        // Play level-up fanfare sound
        OnPlaySound?.Invoke(SoundManager.SND_LEVEL);
    }

    // ── Login flow ────────────────────────────────────────────────


    // ── FX ────────────────────────────────────────────────────────

    private void HandleBinCreateFx(ByteQueue bq)
    {
        short charIndex = bq.ReadInteger();
        short fxIndex = bq.ReadInteger();
        short fxLoops = bq.ReadInteger();

        if (!_state.Characters.TryGetValue(charIndex, out var ch))
            return;

        if (fxIndex == 0)
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
                ch.ActiveFxSlots[i] = fxIndex;
                ch.FxLoops[i] = fxLoops >= 999 ? -1 : fxLoops;
                ch.FxFrameCounter[i] = 0;
                return;
            }
        }

        ch.ActiveFxSlots[0] = fxIndex;
        ch.FxLoops[0] = fxLoops >= 999 ? -1 : fxLoops;
        ch.FxFrameCounter[0] = 0;
    }

    // ── Inventory / Spells ────────────────────────────────────────


    // ── Safe / Combat state ───────────────────────────────────────

    /// <summary>
    /// UserSwing (ID 134) — attacker index who missed player.
    /// Wire: i16 attackerIndex
    /// </summary>
    private void HandleBinUserSwing(ByteQueue bq)
    {
        short attackerIndex = bq.ReadInteger();
        string attackerName = "";
        if (_state.Characters.TryGetValue(attackerIndex, out var attacker))
            attackerName = attacker.Name;
        _state.ChatMessages.Enqueue(new ChatMessage
        {
            Text = $"{attackerName} te ataco y fallo!!",
            Color = "FF0000",
            Type = ChatType.Combat
        });
        // Floating "miss" text above player character
        OnFloatingText?.Invoke(_state.UserCharIndex, "Fallo!", "CCCCCC");
    }

    /// <summary>
    /// UserHit (ID 135) — attacker index + damage that hit player.
    /// Wire: i16 attackerIndex, i16 damage
    /// </summary>


    /// <summary>
    /// UserHit (ID 135) — attacker index + damage that hit player.
    /// Wire: i16 attackerIndex, i16 damage
    /// </summary>
    private void HandleBinUserHit(ByteQueue bq)
    {
        short attackerIndex = bq.ReadInteger();
        short damage = bq.ReadInteger();
        string attackerName = "";
        if (_state.Characters.TryGetValue(attackerIndex, out var attacker))
            attackerName = attacker.Name;
        _state.ChatMessages.Enqueue(new ChatMessage
        {
            Text = $"{attackerName} te pego {damage}!!",
            Color = "FF0000",
            Type = ChatType.Combat
        });
        // Floating red damage on player (damage received)
        OnFloatingText?.Invoke(_state.UserCharIndex, $"-{damage}", "FF3333");
    }

    /// <summary>
    /// NpcHit (ID 137) — NPC hit player. Wire: u8 bodyPart, i16 damage
    /// </summary>


    /// <summary>
    /// NpcHit (ID 137) — NPC hit player. Wire: u8 bodyPart, i16 damage
    /// </summary>
    private void HandleBinNpcHit(ByteQueue bq)
    {
        byte bodyPart = bq.ReadByte();
        short damage = bq.ReadInteger();
        string bodyName = GetNpcHitBodyPartText(bodyPart);
        _state.ChatMessages.Enqueue(new ChatMessage { Text = $"{bodyName}{damage}", Color = "FF0000", Type = ChatType.Combat });
        // Floating red damage on player (NPC hit us)
        OnFloatingText?.Invoke(_state.UserCharIndex, $"-{damage}", "FF3333");
    }

    /// <summary>
    /// PvpDmgRecv (ID 138) — PvP damage received. Wire: i16 attackerIndex, i16 damage
    /// </summary>


    /// <summary>
    /// PvpDmgRecv (ID 138) — PvP damage received. Wire: i16 attackerIndex, i16 damage
    /// </summary>
    private void HandleBinPvpDmgRecv(ByteQueue bq)
    {
        short attackerIndex = bq.ReadInteger();
        short damage = bq.ReadInteger();
        string attackerName = "";
        if (_state.Characters.TryGetValue(attackerIndex, out var attacker))
            attackerName = attacker.Name;
        _state.ChatMessages.Enqueue(new ChatMessage
        {
            Text = $"{attackerName} te pego {damage} en PvP!!",
            Color = "FF0000",
            Type = ChatType.Combat
        });
        // Floating red damage on player (PvP received)
        OnFloatingText?.Invoke(_state.UserCharIndex, $"-{damage}", "FF3333");
    }

    /// <summary>
    /// PvpDmgDeal (ID 139) — PvP damage dealt. Wire: i16 victimIndex, i16 damage
    /// </summary>


    /// <summary>
    /// PvpDmgDeal (ID 139) — PvP damage dealt. Wire: i16 victimIndex, i16 damage
    /// </summary>
    private void HandleBinPvpDmgDeal(ByteQueue bq)
    {
        short victimIndex = bq.ReadInteger();
        short damage = bq.ReadInteger();
        string victimName = "";
        if (_state.Characters.TryGetValue(victimIndex, out var victim))
            victimName = victim.Name;
        _state.ChatMessages.Enqueue(new ChatMessage
        {
            Text = $"Le pegaste {damage} a {victimName} en PvP!!",
            Color = "FF0000",
            Type = ChatType.Combat
        });
        // Floating yellow damage on victim (PvP dealt)
        OnFloatingText?.Invoke(victimIndex, $"-{damage}", "FFFF66");
    }

    // ── Spells ────────────────────────────────────────────────────

    /// <summary>
    /// SpellInfoResp (ID 148) — spell info string (INFS response).
    /// Wire: string data
    /// </summary>


    // ── Spells ────────────────────────────────────────────────────

    /// <summary>
    /// SpellInfoResp (ID 148) — spell info string (INFS response).
    /// Wire: string data
    /// </summary>
    private void HandleBinSpellInfoResp(ByteQueue bq)
    {
        string data = bq.ReadString();
        GD.Print($"[PKT] SpellInfoResp (binary): {data}");
        _state.SpellInfoText = data;
    }

    // ── Sound ─────────────────────────────────────────────────────

    /// <summary>
    /// PlaySound (ID 150) — play a sound effect by index.
    /// Wire: u8 soundIndex
    /// </summary>


    // ── Death & status ────────────────────────────────────────────

    private void HandleBinDead()
    {
        _state.Dead = true;
        _state.ShowDeathPanel = true;
        _state.ChatMessages.Enqueue(new ChatMessage { Text = "¡Has muerto!", Color = "FF0000", Type = ChatType.Combat });
        OnPlaySound?.Invoke(SoundManager.SND_DEATH);
        GD.Print("[GAME] Player died — Dead (binary)");
    }


    private void HandleBinMeditateToggle()
    {
        _state.Meditating = !_state.Meditating;
        if (!_state.Meditating && _state.Characters.TryGetValue(_state.UserCharIndex, out var ch))
        {
            ClearMeditationFx(ch);
        }
    }


    private void HandleBinParalizeOk(ByteQueue bq)
    {
        short durationSecs = bq.ReadInteger();
        _state.UserParalyzed = !_state.UserParalyzed;
        // Set countdown bar: when becoming paralyzed use duration; when unparalyzed, clear
        _state.ParalysisTimer = _state.UserParalyzed ? durationSecs : 0f;
        _state.ParalysisMaxTimer = _state.UserParalyzed ? durationSecs : 0f;
    }


    // ── MultiMessage ──────────────────────────────────────────────

    private void HandleBinMultiMessage(ByteQueue bq)
    {
        byte subType = bq.ReadByte();

        switch (subType)
        {
            case 0: // NPCSwing
                _state.ChatMessages.Enqueue(new ChatMessage { Text = "La criatura fallo el golpe!!!", Color = "FF0000", Type = ChatType.Combat });
                OnFloatingText?.Invoke(_state.UserCharIndex, "Fallo!", "CCCCCC");
                break;
            case 1: // NPCKillUser
                _state.ChatMessages.Enqueue(new ChatMessage { Text = "La criatura te ha matado!!!", Color = "FF0000", Type = ChatType.Combat });
                break;
            case 2: // BlockedWithShieldUser
                _state.ChatMessages.Enqueue(new ChatMessage { Text = "Has bloqueado el ataque con el escudo!", Color = "FF0000", Type = ChatType.Combat });
                OnFloatingText?.Invoke(_state.UserCharIndex, "Escudo!", "6699FF");
                break;
            case 3: // BlockedWithShieldOther
                _state.ChatMessages.Enqueue(new ChatMessage { Text = "El escudo del enemigo bloqueo tu ataque!", Color = "FF0000", Type = ChatType.Combat });
                break;
            case 4: // UserSwing
                _state.ChatMessages.Enqueue(new ChatMessage { Text = "Has fallado el golpe!!!", Color = "FF0000", Type = ChatType.Combat });
                break;
            case 5: // SafeModeOn
                _state.SafeMode = true;
                _state.ChatMessages.Enqueue(new ChatMessage { Text = ">>SEGURO ACTIVADO<<", Color = "00FF00" });
                break;
            case 6: // SafeModeOff
                _state.SafeMode = false;
                _state.ChatMessages.Enqueue(new ChatMessage { Text = ">>SEGURO DESACTIVADO<<", Color = "FF0000" });
                break;
            case 7: // ResuscitationSafeOff
                _state.SeguroResu = false;
                _state.ChatMessages.Enqueue(new ChatMessage { Text = ">>SEGURO DE RESURRECCION DESACTIVADO<<", Color = "FF0000" });
                break;
            case 8: // ResuscitationSafeOn
                _state.SeguroResu = true;
                _state.ChatMessages.Enqueue(new ChatMessage { Text = ">>SEGURO DE RESURRECCION ACTIVADO<<", Color = "00FF00" });
                break;
            case 9: // NobilityLost
                _state.ChatMessages.Enqueue(new ChatMessage { Text = "Has perdido tu nobleza!", Color = "FF0000" });
                break;
            case 10: // CantUseWhileMeditating
                _state.ChatMessages.Enqueue(new ChatMessage { Text = "No puedes usar eso mientras meditas!", Color = "FF0000" });
                break;
            case 12: // NPCHitUser
            {
                byte bodyPart = bq.ReadByte();
                short damage = bq.ReadInteger();
                string bodyName = GetNpcHitBodyPartText(bodyPart);
                _state.ChatMessages.Enqueue(new ChatMessage { Text = $"{bodyName}{damage}", Color = "FF0000", Type = ChatType.Combat });
                OnFloatingText?.Invoke(_state.UserCharIndex, $"-{damage}", "FF3333");
                break;
            }
            case 13: // UserHitNPC
            {
                int damage = bq.ReadLong();
                _state.ChatMessages.Enqueue(new ChatMessage { Text = $"Le has pegado a la criatura por {damage}!!", Color = "FF0000", Type = ChatType.Combat });
                // No target charIndex available for NPC hits in this packet
                break;
            }
            case 14: // UserAttackedSwing
            {
                short attackerIndex = bq.ReadInteger();
                string attackerName = "";
                if (_state.Characters.TryGetValue(attackerIndex, out var attacker))
                    attackerName = attacker.Name;
                _state.ChatMessages.Enqueue(new ChatMessage { Text = $"{attackerName} te ataco y fallo!!", Color = "FF0000", Type = ChatType.Combat });
                OnFloatingText?.Invoke(_state.UserCharIndex, "Fallo!", "CCCCCC");
                break;
            }
            case 15: // UserHittedByUser
            {
                short attackerIndex = bq.ReadInteger();
                byte bodyPart = bq.ReadByte();
                short damage = bq.ReadInteger();
                string attackerName = "";
                if (_state.Characters.TryGetValue(attackerIndex, out var attacker))
                    attackerName = attacker.Name;
                string bodyName = GetPvpReceivedBodyPartText(bodyPart);
                _state.ChatMessages.Enqueue(new ChatMessage { Text = $"{attackerName}{bodyName}{damage}", Color = "FF0000", Type = ChatType.Combat });
                OnFloatingText?.Invoke(_state.UserCharIndex, $"-{damage}", "FF3333");
                break;
            }
            case 16: // UserHittedUser
            {
                short victimIndex = bq.ReadInteger();
                byte bodyPart = bq.ReadByte();
                short damage = bq.ReadInteger();
                string victimName = "";
                if (_state.Characters.TryGetValue(victimIndex, out var victim))
                    victimName = victim.Name;
                string bodyName = GetPvpDealtBodyPartText(bodyPart);
                _state.ChatMessages.Enqueue(new ChatMessage { Text = $"Le has pegado a {victimName}{bodyName}{damage}", Color = "FF0000", Type = ChatType.Combat });
                OnFloatingText?.Invoke(victimIndex, $"-{damage}", "FFFF66");
                break;
            }
            case 17: // WorkRequestTarget
                GD.Print("[PKT] MultiMessage: WorkRequestTarget");
                break;
            case 18: // HaveKilledUser
            {
                short killedIndex = bq.ReadInteger();
                int expGained = bq.ReadLong();
                string killedName = "";
                if (_state.Characters.TryGetValue(killedIndex, out var killed))
                    killedName = killed.Name;
                _state.ChatMessages.Enqueue(new ChatMessage { Text = $"Has matado a {killedName}! Ganaste {expGained} exp.", Color = "FF0000", Type = ChatType.Combat });
                break;
            }
            case 19: // UserKill
            {
                short killerIndex = bq.ReadInteger();
                string killerName = "";
                if (_state.Characters.TryGetValue(killerIndex, out var killer))
                    killerName = killer.Name;
                _state.ChatMessages.Enqueue(new ChatMessage { Text = $"{killerName} te ha matado!!!", Color = "FF0000", Type = ChatType.Combat });
                break;
            }
            case 20: // EarnExp
                GD.Print("[PKT] MultiMessage: EarnExp");
                break;
            case 21: // GoHome
            {
                byte distance = bq.ReadByte();
                short time = bq.ReadInteger();
                string homeCity = bq.ReadString();
                _state.ChatMessages.Enqueue(new ChatMessage { Text = $"Regresando a {homeCity} en {time}s (distancia: {distance})...", Color = "00FF00" });
                break;
            }
            case 22: // CancelGoHome
                _state.ChatMessages.Enqueue(new ChatMessage { Text = "Regreso a hogar cancelado.", Color = "FF0000" });
                break;
            case 23: // FinishHome
                _state.ChatMessages.Enqueue(new ChatMessage { Text = "Has regresado a tu hogar.", Color = "00FF00" });
                break;
            default:
                GD.Print($"[PKT] MultiMessage unknown sub-type={subType}");
                break;
        }
    }

    // ── Movement / Appearance ────────────────────────────────────────

    /// <summary>
    /// HeadingChange (ID 107) — broadcast char heading to area.
    /// Wire: i16 charIndex, u8 heading
    /// Matches VB6 |H opcode.
    /// </summary>


    private void HandleBinUpdateTagAndStatus(ByteQueue bq)
    {
        short charIndex = bq.ReadInteger();
        byte nickColor = bq.ReadByte();
        string tag = bq.ReadString();

        if (_state.Characters.TryGetValue(charIndex, out var ch))
        {
            ch.Criminal = nickColor == 2;
            if (!string.IsNullOrEmpty(tag))
                ch.Name = tag;
        }
    }

    // ── Map extras ────────────────────────────────────────────────


    private void HandleBinHungerThirst(ByteQueue bq)
    {
        _state.MaxAgua = bq.ReadByte();
        _state.MinAgua = bq.ReadByte();
        _state.MaxHam = bq.ReadByte();
        _state.MinHam = bq.ReadByte();
    }


    /// <summary>
    /// HungerThirst (ID 128) — same layout as UpdateHungerAndThirst (ID 60).
    /// Wire: u8 maxAgua, u8 minAgua, u8 maxHam, u8 minHam
    /// </summary>
    private void HandleBinHungerThirst128(ByteQueue bq)
    {
        _state.MaxAgua = bq.ReadByte();
        _state.MinAgua = bq.ReadByte();
        _state.MaxHam = bq.ReadByte();
        _state.MinHam = bq.ReadByte();
    }

    // ── Safe / Combat state ───────────────────────────────────────

    /// <summary>
    /// UserSwing (ID 134) — attacker index who missed player.
    /// Wire: i16 attackerIndex
    /// </summary>


    // ── Stat variants ─────────────────────────────────────────────

    private void HandleBinStatName(ByteQueue bq)
    {
        string name = bq.ReadString();
        if (_state.Characters.TryGetValue(_state.UserCharIndex, out var ch))
            ch.Name = name;
        GD.Print($"[PKT] StatName: {name}");
    }


    private void HandleBinStatBulk(ByteQueue bq)
    {
        short bulk = bq.ReadInteger();
        _state.CarryBulk = bulk;
    }

    /// <summary>
    /// HungerThirst (ID 128) — same layout as UpdateHungerAndThirst (ID 60).
    /// Wire: u8 maxAgua, u8 minAgua, u8 maxHam, u8 minHam
    /// </summary>


    // ── Timer ────────────────────────────────────────────────────────

    /// <summary>
    /// TimerInfo (ID 246) — scroll/timer slot (TIS opcode). Ignored for now.
    /// Wire: u8 id, i32 time1, i32 time2
    /// </summary>
    private void HandleBinTimerInfo(ByteQueue bq)
    {
        byte id = bq.ReadByte();
        int time1 = bq.ReadLong();
        int time2 = bq.ReadLong();
        // Scroll timers not yet implemented on the client.
        GD.Print($"[PKT] TimerInfo id={id} t1={time1} t2={time2}");
    }

    // ── Class options ────────────────────────────────────────────────

    /// <summary>
    /// ClassOptions (ID 144) — level bonus class selection (99 opcode).
    /// Wire: u8 option1, u8 option2
    /// Stub — class bonus UI not yet implemented.
    /// </summary>


    private void HandleBinPong()
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
    }


    private void HandleBinOnlineCount(ByteQueue bq)
    {
        short count = bq.ReadInteger();
        _state.OnlineCount = count;
    }

    // ── MultiMessage ──────────────────────────────────────────────


    /// <summary>
    /// BattleTeamScores (ID 163) — BatallaMistica event kill scores.
    /// Wire: i32 t1, i32 t2, i32 t3, i32 t4
    /// </summary>
    private void HandleBinBattleTeamScores(ByteQueue bq)
    {
        int t1 = bq.ReadLong();
        int t2 = bq.ReadLong();
        int t3 = bq.ReadLong();
        int t4 = bq.ReadLong();
        GD.Print($"[PKT] BattleTeamScores: {t1}/{t2}/{t3}/{t4}");
        _state.BattleTeamScores = $"{t1},{t2},{t3},{t4}";
    }

    /// <summary>
    /// AmbientColor (ID 164) — map ambient RGB override (PCR opcode).
    /// Wire: u8 r, u8 g, u8 b
    /// </summary>


    /// <summary>
    /// AmbientColor (ID 164) — map ambient RGB override (PCR opcode).
    /// Wire: u8 r, u8 g, u8 b
    /// </summary>
    private void HandleBinAmbientColor(ByteQueue bq)
    {
        byte r = bq.ReadByte();
        byte g = bq.ReadByte();
        byte b = bq.ReadByte();
        _state.AmbientColorR = r;
        _state.AmbientColorG = g;
        _state.AmbientColorB = b;
        // Also set MapColor fields — WorldRenderer reads these for ambient light
        _state.MapColorR = r;
        _state.MapColorG = g;
        _state.MapColorB = b;
        GD.Print($"[PKT] AmbientColor R={r} G={g} B={b}");
    }

    // ── Bank (legacy) ─────────────────────────────────────────────

    /// <summary>
    /// InitBankLegacy (ID 165) — legacy bank init. Wire: string data
    /// </summary>


    /// <summary>
    /// FestData (ID 227) — character stats summary from /EST command.
    /// Wire: string "crimMatados,ciudMatados,level,class,status,0,guildIndex,reputation"
    /// </summary>
    private void HandleBinFestData(ByteQueue bq)
    {
        string data = bq.ReadString();
        GD.Print($"[PKT] FestData: {data}");
        var parts = data.Split(',');
        if (parts.Length >= 8)
        {
            int.TryParse(parts[0], out int crimMatados);
            int.TryParse(parts[1], out int ciudMatados);
            int.TryParse(parts[2], out int level);
            string className = parts[3];
            string status = parts[4];
            // parts[5] = 0 (unused)
            // parts[6] = guild index
            int.TryParse(parts[7], out int reputation);

            _state.FameCrimMatados = crimMatados;
            _state.FameCiudMatados = ciudMatados;
            _state.Level = level;
            _state.UserClassName = className;
            _state.UserCriminal = status == "Criminal";
            _state.Reputation = reputation;
        }
    }

    /// <summary>
    /// FullCharInfo (ID 245) — character info from /MIRAR or DAMINF.
    /// CSV: name,race,class,level,gold,reputation,crimMatados,ciudMatados,status,faction,guildIndex,0,maxHp,maxMana,maxSta
    /// </summary>


    /// <summary>
    /// FullCharInfo (ID 245) — character info from /MIRAR or DAMINF.
    /// CSV: name,race,class,level,gold,reputation,crimMatados,ciudMatados,status,faction,guildIndex,0,maxHp,maxMana,maxSta
    /// </summary>
    private void HandleBinFullCharInfo(ByteQueue bq)
    {
        string data = bq.ReadString();
        GD.Print($"[PKT] FullCharInfo: {data}");

        string[] fields = data.Split(',');
        if (fields.Length < 15) return;

        var info = new CharInfoData
        {
            Name = fields[0],
            Race = fields[1],
            ClassName = fields[2],
            Level = int.TryParse(fields[3], out int lvl) ? lvl : 0,
            Gold = int.TryParse(fields[4], out int gold) ? gold : 0,
            Reputation = int.TryParse(fields[5], out int rep) ? rep : 0,
            CrimMatados = int.TryParse(fields[6], out int cm) ? cm : 0,
            CiudMatados = int.TryParse(fields[7], out int cim) ? cim : 0,
            Status = fields[8],
            Faction = fields[9],
            GuildIndex = int.TryParse(fields[10], out int gi) ? gi : 0,
            MaxHp = int.TryParse(fields[12], out int hp) ? hp : 0,
            MaxMana = int.TryParse(fields[13], out int mp) ? mp : 0,
            MaxSta = int.TryParse(fields[14], out int sta) ? sta : 0,
        };

        _state.CharInfoCurrent = info;
        _state.ShowCharInfo = true;
    }

    /// <summary>
    /// Generic single-string packets (ImageData, BkwData, GinfData,
    /// IcoData, ZsosData, SbrData, AuctionList, CosmeticImage/Pcgn/Pcss/Pccc)
    /// — read one string and log.
    /// </summary>

}
