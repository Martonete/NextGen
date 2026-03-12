using System;
using Godot;
using ArgentumNextgen.Data;
using ArgentumNextgen.Game;

namespace ArgentumNextgen.Network;

/// <summary>
/// Binary packet handlers: Map / Position / Character create-remove / Objects / Sound
/// </summary>
public partial class PacketHandler
{

    // ── Map / Position ────────────────────────────────────────────

    private void HandleBinChangeMap(ByteQueue bq)
    {
        short mapNum = bq.ReadInteger();
        short mapVersion = bq.ReadInteger();
        byte mapR = bq.ReadByte();
        byte mapG = bq.ReadByte();
        byte mapB = bq.ReadByte();

        _state.CurrentMap = mapNum;
        _state.MapColorR = mapR;
        _state.MapColorG = mapG;
        _state.MapColorB = mapB;

        // Save self aura state before clearing (charIndex changes between maps)
        if (_state.Characters.TryGetValue(_state.UserCharIndex, out var self))
            _savedSelfAuras = self;

        // Clear all characters
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

        _state.ScreenOffsetX = 0;
        _state.ScreenOffsetY = 0;
        _state.AddToUserPosX = 0;
        _state.AddToUserPosY = 0;
        _state.UserMoving = false;
        _state.PendingMoves = 0;
        if (_state.Characters.TryGetValue(_state.UserCharIndex, out var selfCh))
        {
            selfCh.MoveOffsetX = 0;
            selfCh.MoveOffsetY = 0;
            selfCh.Moving = false;
            selfCh.ScrollDirectionX = 0;
            selfCh.ScrollDirectionY = 0;
        }

        OnMapLoad?.Invoke();
        GD.Print($"[GAME] Change map (binary): {mapNum} v{mapVersion}");
    }


    private void HandleBinPosUpdate(ByteQueue bq)
    {
        byte x = bq.ReadByte();
        byte y = bq.ReadByte();

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

        // Stop stale area sounds from the previous position (warp within same map)
        OnStopSfx?.Invoke();
    }


    private void HandleBinAreaChanged(ByteQueue bq)
    {
        byte playerX = bq.ReadByte();
        byte playerY = bq.ReadByte();

        // VB6: CambioDeArea 9x9 grid zones
        int minLimX = (playerX / 9 - 1) * 9;
        int maxLimX = minLimX + 26;
        int minLimY = (playerY / 9 - 1) * 9;
        int maxLimY = minLimY + 26;

        const int HalfW = 8, HalfH = 6, VisMargin = 3;
        int visMinX = playerX - HalfW - VisMargin;
        int visMaxX = playerX + HalfW + VisMargin;
        int visMinY = playerY - HalfH - VisMargin;
        int visMaxY = playerY + HalfH + VisMargin;

        var toRemove = new System.Collections.Generic.List<int>();
        foreach (var kvp in _state.Characters)
        {
            if (kvp.Key == _state.UserCharIndex) continue;
            var c = kvp.Value;
            if (c.PosX < minLimX || c.PosX > maxLimX || c.PosY < minLimY || c.PosY > maxLimY)
            {
                if (c.PosX >= visMinX && c.PosX <= visMaxX && c.PosY >= visMinY && c.PosY <= visMaxY)
                    continue;
                toRemove.Add(kvp.Key);
            }
        }
        foreach (int key in toRemove)
            _state.Characters.Remove(key);

        var objToRemove = new System.Collections.Generic.List<(int, int)>();
        foreach (var kvp in _state.GroundObjects)
        {
            if (kvp.Key.Item1 < minLimX || kvp.Key.Item1 > maxLimX ||
                kvp.Key.Item2 < minLimY || kvp.Key.Item2 > maxLimY)
                objToRemove.Add(kvp.Key);
        }
        foreach (var key in objToRemove)
            _state.GroundObjects.Remove(key);
    }

    // ── Chat ──────────────────────────────────────────────────────


    // ── Character ─────────────────────────────────────────────────

    private void HandleBinCharacterCreate(ByteQueue bq)
    {
        short charIndex = bq.ReadInteger();
        short body = bq.ReadInteger();
        short head = bq.ReadInteger();
        byte heading = bq.ReadByte();
        byte x = bq.ReadByte();
        byte y = bq.ReadByte();
        short weapon = bq.ReadInteger();
        short shield = bq.ReadInteger();
        short helmet = bq.ReadInteger();
        short fxIndex = bq.ReadInteger();
        short fxLoops = bq.ReadInteger();
        string name = bq.ReadString();
        byte nickColor = bq.ReadByte();
        byte privileges = bq.ReadByte();

        var ch = new Character
        {
            CharIndex = charIndex,
            Body = body,
            Head = head,
            Heading = heading,
            PosX = x,
            PosY = y,
            WeaponAnim = weapon,
            ShieldAnim = shield,
            CascoAnim = helmet,
            Name = name,
            Criminal = nickColor == 2,
            Privileges = privileges,
        };

        ch.Dead = IsDeadHead(head);

        // Apply FX if present
        if (fxIndex > 0)
        {
            ch.ActiveFxSlots[0] = fxIndex;
            ch.FxLoops[0] = fxLoops >= 999 ? -1 : fxLoops;
            ch.FxFrameCounter[0] = 0;
        }

        // Preserve aura state across map changes.
        // CharIndex changes between maps, so we use _savedSelfAuras (saved in ChangeMap)
        // for the player character, and existing dict entry for same-map re-creates.
        Character? auraSource = null;
        if (_savedSelfAuras != null && charIndex == _state.UserCharIndex)
        {
            auraSource = _savedSelfAuras;
            _savedSelfAuras = null; // consumed
        }
        else if (_state.Characters.TryGetValue(charIndex, out var existing))
        {
            auraSource = existing;
        }

        if (auraSource != null)
        {
            ch.AuraIndexA = auraSource.AuraIndexA;  ch.AuraAngleA = auraSource.AuraAngleA;
            ch.AuraIndexW = auraSource.AuraIndexW;  ch.AuraAngleW = auraSource.AuraAngleW;
            ch.AuraIndexE = auraSource.AuraIndexE;  ch.AuraAngleE = auraSource.AuraAngleE;
            ch.AuraIndexR = auraSource.AuraIndexR;  ch.AuraAngleR = auraSource.AuraAngleR;
            ch.AuraIndexC = auraSource.AuraIndexC;  ch.AuraAngleC = auraSource.AuraAngleC;
            ch.NpcAura = auraSource.NpcAura;        ch.NpcAuraAngle = auraSource.NpcAuraAngle;
            ch.Levitating = auraSource.Levitating;
            ch.Navigating = auraSource.Navigating;
        }

        _state.Characters[charIndex] = ch;

        // Extract guild name for own character
        if (charIndex == _state.UserCharIndex)
        {
            int ltIdx = name.IndexOf('<');
            _state.UserGuildName = ltIdx >= 0 ? name[(ltIdx + 1)..] : "";
        }

        GD.Print($"[CC] {name} idx={charIndex} body={body} head={head} weapon={weapon} shield={shield} casco={helmet} (binary)");

        if (body <= 0)
            GD.PrintErr($"[CC] WARNING: char {name} (idx={charIndex}) has body=0!");
    }


    private void HandleBinCharacterMove(ByteQueue bq)
    {
        short charIndex = bq.ReadInteger();
        byte newX = bq.ReadByte();
        byte newY = bq.ReadByte();

        if (!_state.Characters.TryGetValue(charIndex, out var ch))
            return;

        if (charIndex == _state.UserCharIndex)
            return;

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


    private void HandleBinCharacterChange(ByteQueue bq)
    {
        short idx = bq.ReadInteger();
        short body = bq.ReadInteger();
        short head = bq.ReadInteger();
        byte heading = bq.ReadByte();
        short weapon = bq.ReadInteger();
        short shield = bq.ReadInteger();
        short helmet = bq.ReadInteger();
        short fxIndex = bq.ReadInteger();
        short fxLoops = bq.ReadInteger();

        if (!_state.Characters.TryGetValue(idx, out var ch))
            return;

        bool wasDead = ch.Dead;
        bool nowDead = IsDeadHead(head);

        ch.Body = body;
        ch.Head = head;
        ch.Heading = heading;
        ch.WeaponAnim = weapon;
        ch.ShieldAnim = shield;
        ch.CascoAnim = helmet;
        ch.Dead = nowDead;

        // Apply FX
        if (fxIndex > 0)
        {
            // Find empty slot or overwrite slot 0
            int slot = -1;
            for (int i = 0; i < 3; i++)
            {
                if (ch.ActiveFxSlots[i] == 0) { slot = i; break; }
            }
            if (slot < 0) slot = 0;
            ch.ActiveFxSlots[slot] = fxIndex;
            ch.FxLoops[slot] = fxLoops >= 999 ? -1 : fxLoops;
            ch.FxFrameCounter[slot] = 0;
        }

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
                GD.Print($"[CP] User died (head={head}, binary)");
            }
            else if (!nowDead && wasDead)
            {
                _state.Dead = false;
                OnPlaySound?.Invoke(SoundManager.SND_REVIVE);
                GD.Print($"[CP] User revived (binary)");
            }
        }
    }

    // ── Objects on ground ─────────────────────────────────────────


    // ── Objects on ground ─────────────────────────────────────────

    private void HandleBinObjectCreate(ByteQueue bq)
    {
        byte x = bq.ReadByte();
        byte y = bq.ReadByte();
        short grhIndex = bq.ReadInteger();
        _state.GroundObjects[(x, y)] = grhIndex;
    }


    private void HandleBinObjectDelete(ByteQueue bq)
    {
        byte x = bq.ReadByte();
        byte y = bq.ReadByte();
        _state.GroundObjects.Remove((x, y));
    }


    private void HandleBinBlockPosition(ByteQueue bq)
    {
        byte x = bq.ReadByte();
        byte y = bq.ReadByte();
        bool blocked = bq.ReadBoolean();
        if (_state.MapData != null && x >= 1 && x <= 100 && y >= 1 && y <= 100)
        {
            _state.MapData.Tiles[x, y].Blocked = blocked;
        }
    }

    // ── Sound / Music ─────────────────────────────────────────────


    // ── Sound / Music ─────────────────────────────────────────────

    private void HandleBinPlayMidi(ByteQueue bq)
    {
        byte midiIndex = bq.ReadByte();
        _state.MusicId = midiIndex;
        OnPlayMusic?.Invoke(midiIndex);
    }


    private void HandleBinPlayWave(ByteQueue bq)
    {
        byte waveIndex = bq.ReadByte();
        byte srcX = bq.ReadByte();
        byte srcY = bq.ReadByte();
        if (waveIndex > 0)
            OnPlaySoundAt?.Invoke(waveIndex, srcX, srcY);
    }

    // ── FX ────────────────────────────────────────────────────────


    // ── Sound ─────────────────────────────────────────────────────

    /// <summary>
    /// PlaySound (ID 150) — play a sound effect by index.
    /// Wire: u8 soundIndex
    /// </summary>
    private void HandleBinPlaySound(ByteQueue bq)
    {
        byte soundIndex = bq.ReadByte();
        if (soundIndex > 0)
            OnPlaySound?.Invoke(soundIndex);
    }

    // ── Work / Crafting ───────────────────────────────────────────

    /// <summary>
    /// WorkMode (ID 155) — work mode skill toggle (T01 opcode).
    /// Wire: u8 skill
    /// </summary>


    // ── Movement / Appearance ────────────────────────────────────────

    /// <summary>
    /// HeadingChange (ID 107) — broadcast char heading to area.
    /// Wire: i16 charIndex, u8 heading
    /// Matches VB6 |H opcode.
    /// </summary>
    private void HandleBinHeadingChange(ByteQueue bq)
    {
        short charIndex = bq.ReadInteger();
        byte heading = bq.ReadByte();
        if (_state.Characters.TryGetValue(charIndex, out var ch))
            ch.Heading = heading;
    }

    // ── Mount / Levitate ─────────────────────────────────────────────

    /// <summary>
    /// UserMount (ID 142) — mount/dismount a character.
    /// Wire: i16 charIndex, bool mounted
    /// Matches VB6 USM opcode.
    /// </summary>


    // ── Mount / Levitate ─────────────────────────────────────────────

    /// <summary>
    /// UserMount (ID 142) — mount/dismount a character.
    /// Wire: i16 charIndex, bool mounted
    /// Matches VB6 USM opcode.
    /// </summary>
    private void HandleBinUserMount(ByteQueue bq)
    {
        short charIndex = bq.ReadInteger();
        bool mounted = bq.ReadBoolean();

        if (_state.Characters.TryGetValue(charIndex, out var ch))
            ch.Mounted = mounted;

        if (charIndex == _state.UserCharIndex)
            _state.UserMounted = mounted;
    }

    /// <summary>
    /// Levitate (ID 143) — levitation state for a character.
    /// Wire: i16 charIndex, bool levitating
    /// Matches VB6 MVOL opcode.
    /// </summary>


    /// <summary>
    /// Levitate (ID 143) — levitation state for a character.
    /// Wire: i16 charIndex, bool levitating
    /// Matches VB6 MVOL opcode.
    /// </summary>
    private void HandleBinLevitate(ByteQueue bq)
    {
        short charIndex = bq.ReadInteger();
        bool levitating = bq.ReadBoolean();

        if (_state.Characters.TryGetValue(charIndex, out var ch))
            ch.Levitating = levitating;
    }

    // ── Animation / Equipment stats ──────────────────────────────────

    /// <summary>
    /// AnimData (ID 225) — equipment hitbox stats (20 comma-separated fields).
    /// Wire: string data (CSV: armaMin,armaMax,armorMin,armorMax,escuMin,escuMax,
    ///        cascMin,cascMax,herrMin,herrMax, then 10 magic defense fields)
    /// Matches VB6 ANM opcode.
    /// </summary>


    // ── Animation / Equipment stats ──────────────────────────────────

    /// <summary>
    /// AnimData (ID 225) — equipment hitbox stats (20 comma-separated fields).
    /// Wire: string data (CSV: armaMin,armaMax,armorMin,armorMax,escuMin,escuMax,
    ///        cascMin,cascMax,herrMin,herrMax, then 10 magic defense fields)
    /// Matches VB6 ANM opcode.
    /// </summary>
    private void HandleBinAnimData(ByteQueue bq)
    {
        string data = bq.ReadString();
        var parts = data.Split(',');
        if (parts.Length < 20) return;

        _state.AttackMin = int.TryParse(parts[0], out var v0) ? v0 : 0;
        _state.AttackMax = int.TryParse(parts[1], out var v1) ? v1 : 0;

        int armorMin  = int.TryParse(parts[2], out var p2)  ? p2  : 0;
        int armorMax  = int.TryParse(parts[3], out var p3)  ? p3  : 0;
        int escuMin   = int.TryParse(parts[4], out var p4)  ? p4  : 0;
        int escuMax   = int.TryParse(parts[5], out var p5)  ? p5  : 0;
        int cascMin   = int.TryParse(parts[6], out var p6)  ? p6  : 0;
        int cascMax   = int.TryParse(parts[7], out var p7)  ? p7  : 0;
        int herrMin   = int.TryParse(parts[8], out var p8)  ? p8  : 0;
        int herrMax   = int.TryParse(parts[9], out var p9)  ? p9  : 0;
        _state.DefenseMin = armorMin + escuMin + cascMin + herrMin;
        _state.DefenseMax = armorMax + escuMax + cascMax + herrMax;

        int magMin  = int.TryParse(parts[10], out var m0) ? m0 : 0;
        int magMax  = int.TryParse(parts[11], out var m1) ? m1 : 0;
        int magMina = int.TryParse(parts[12], out var m2) ? m2 : 0;
        int magMaxa = int.TryParse(parts[13], out var m3) ? m3 : 0;
        int magMinb = int.TryParse(parts[14], out var m4) ? m4 : 0;
        int magMaxb = int.TryParse(parts[15], out var m5) ? m5 : 0;
        int magMinc = int.TryParse(parts[16], out var m6) ? m6 : 0;
        int magMaxc = int.TryParse(parts[17], out var m7) ? m7 : 0;
        int magMind = int.TryParse(parts[18], out var m8) ? m8 : 0;
        int magMaxd = int.TryParse(parts[19], out var m9) ? m9 : 0;
        _state.MagDefMin = magMin + magMina + magMinb + magMinc + magMind;
        _state.MagDefMax = magMax + magMaxa + magMaxb + magMaxc + magMaxd;
    }

    // ── Timer ────────────────────────────────────────────────────────

    /// <summary>
    /// TimerInfo (ID 246) — scroll/timer slot (TIS opcode). Ignored for now.
    /// Wire: u8 id, i32 time1, i32 time2
    /// </summary>


    // ── Movement / Projectiles ────────────────────────────────────

    /// <summary>
    /// Arrow (ID 108) — projectile arrow from src to tgt (FLECHI opcode).
    /// Wire: i16 srcIndex, i16 tgtIndex, i16 grhIndex
    /// </summary>
    private void HandleBinArrow(ByteQueue bq)
    {
        short srcIndex = bq.ReadInteger();
        short tgtIndex = bq.ReadInteger();
        short grhIndex = bq.ReadInteger();

        // Create visual arrow projectile (same as text FLECHI handler)
        if (_state.Characters.TryGetValue(srcIndex, out var srcCh) &&
            _state.Characters.TryGetValue(tgtIndex, out var tgtCh))
        {
            float srcPixelX = srcCh.PosX * 32f + 16f;
            float srcPixelY = srcCh.PosY * 32f + 16f;
            float tgtPixelX = tgtCh.PosX * 32f + 16f;
            float tgtPixelY = tgtCh.PosY * 32f + 16f;
            _state.ActiveArrows.Add(new ArrowProjectile
            {
                ShooterCharIndex = srcIndex,
                TargetCharIndex = tgtIndex,
                GrhIndex = grhIndex,
                X = srcPixelX, Y = srcPixelY,
                TargetX = tgtPixelX, TargetY = tgtPixelY,
                Speed = 8f,
                Active = true,
            });
        }
        GD.Print($"[PKT] Arrow: src={srcIndex} tgt={tgtIndex} grh={grhIndex}");
    }

    /// <summary>
    /// NavigateBroadcast (ID 109) — broadcast navigation state for a char (NVG opcode).
    /// Wire: i16 charIndex, bool navigating
    /// </summary>


    /// <summary>
    /// NavigateBroadcast (ID 109) — broadcast navigation state for a char (NVG opcode).
    /// Wire: i16 charIndex, bool navigating
    /// </summary>
    private void HandleBinNavigateBroadcast(ByteQueue bq)
    {
        short charIndex = bq.ReadInteger();
        bool navigating = bq.ReadBoolean();
        if (_state.Characters.TryGetValue(charIndex, out var ch))
            ch.Navigating = navigating;
    }

    // ── Chat (continued) ──────────────────────────────────────────


    // ── ForceCharMove ─────────────────────────────────────────────

    /// <summary>
    /// ForceCharMove (ID 32) — server pushes the player in a direction.
    /// Used for ghost push, spell knockback, etc.
    /// Wire: u8 heading (1=N, 2=E, 3=S, 4=W)
    /// Same logic as InputHandler.TryMove but without LegalPos check or packet send.
    /// </summary>
    private void HandleBinForceCharMove(ByteQueue bq)
    {
        byte heading = bq.ReadByte();

        if (!_state.Characters.TryGetValue(_state.UserCharIndex, out var ch))
            return;

        // Direction deltas: 1=N(0,-1), 2=E(1,0), 3=S(0,1), 4=W(-1,0)
        int dx = 0, dy = 0;
        switch (heading)
        {
            case 1: dy = -1; break;
            case 2: dx = 1; break;
            case 3: dy = 1; break;
            case 4: dx = -1; break;
        }

        int newX = ch.PosX + dx;
        int newY = ch.PosY + dy;

        // Clear meditation FX on forced movement
        ClearMeditationFx(ch);

        // VB6 Char_Move_by_Head: update character logical position + start animation
        ch.Heading = heading;
        ch.MoveOffsetX = -(dx * 32);
        ch.MoveOffsetY = -(dy * 32);
        ch.ScrollDirectionX = dx;
        ch.ScrollDirectionY = dy;
        ch.Moving = true;
        ch.PosX = newX;
        ch.PosY = newY;

        // VB6 Engine_MoveScreen: start camera scroll
        _state.AddToUserPosX = dx;
        _state.AddToUserPosY = dy;
        _state.UserPosX = newX;
        _state.UserPosY = newY;
        _state.UserMoving = true;
        _state.ScreenOffsetX = 0;
        _state.ScreenOffsetY = 0;

        GD.Print($"[PKT] ForceCharMove heading={heading} -> ({newX},{newY})");
    }

    // ── Forum ───────────────────────────────────────────────────

    /// <summary>
    /// AddForumMsg (ID 117) — server sends a forum post to accumulate before showing the form.
    /// Wire: u8 forumType, string title, string author, string message
    /// forumType: 0=General, 1=GeneralSticky, 2=Caos, 3=CaosSticky, 4=Real, 5=RealSticky
    /// </summary>


    private void HandleBinCharData(ByteQueue bq)
    {
        short idx = bq.ReadInteger();
        byte color = bq.ReadByte();
        short auraA = bq.ReadInteger();
        short auraW = bq.ReadInteger();
        short auraE = bq.ReadInteger();
        short auraR = bq.ReadInteger();
        short auraC = bq.ReadInteger();
        bool levitando = bq.ReadBoolean();
        byte ranking = bq.ReadByte();

        if (_state.Characters.TryGetValue(idx, out var ch))
        {
            ch.AuraIndexA = auraA;
            ch.AuraIndexW = auraW;
            ch.AuraIndexE = auraE;
            ch.AuraIndexR = auraR;
            ch.AuraIndexC = auraC;
            ch.Levitating = levitando;
        }
    }


    private void HandleBinAuraUpdate(ByteQueue bq)
    {
        short idx = bq.ReadInteger();
        short auraA = bq.ReadInteger();
        short auraW = bq.ReadInteger();
        short auraE = bq.ReadInteger();
        short auraR = bq.ReadInteger();
        short auraC = bq.ReadInteger();

        if (_state.Characters.TryGetValue(idx, out var ch))
        {
            ch.AuraIndexA = auraA;
            ch.AuraIndexW = auraW;
            ch.AuraIndexE = auraE;
            ch.AuraIndexR = auraR;
            ch.AuraIndexC = auraC;
        }
    }


    private void HandleBinCharacterInfo(ByteQueue bq)
    {
        string name = bq.ReadString();
        byte race = bq.ReadByte();
        byte charClass = bq.ReadByte();
        byte gender = bq.ReadByte();
        byte level = bq.ReadByte();
        int gold = bq.ReadLong();
        int bankGold = bq.ReadLong();
        int reputation = bq.ReadLong();
        string description = bq.ReadString();
        string guildName = bq.ReadString();
        GD.Print($"[PKT] CharacterInfo (binary): {name} Lvl {level} Guild={guildName}");
    }

    // ── Console message by ID ─────────────────────────────────────


    private void HandleBinRemoveDialogs()
    {
        foreach (var kvp in _state.Characters)
        {
            kvp.Value.DialogText = "";
            kvp.Value.DialogDurationMs = 0;
        }
    }


    private void HandleBinRemoveCharDialog(ByteQueue bq)
    {
        short charIndex = bq.ReadInteger();
        if (_state.Characters.TryGetValue(charIndex, out var ch))
        {
            ch.DialogText = "";
            ch.DialogDurationMs = 0;
        }
    }


    // ── Map extras ────────────────────────────────────────────────

    private void HandleBinMapMusic(ByteQueue bq)
    {
        byte midiId = bq.ReadByte();
        _state.MusicId = midiId;
        OnPlayMusic?.Invoke(midiId);
    }


    /// <summary>
    /// Navigation (ID 162) — navigation mode data string.
    /// Wire: string data
    /// </summary>
    private void HandleBinNavigationData(ByteQueue bq)
    {
        string data = bq.ReadString();
        GD.Print($"[PKT] Navigation data (binary): {data}");
    }

    /// <summary>
    /// BattleTeamScores (ID 163) — BatallaMistica event kill scores.
    /// Wire: i32 t1, i32 t2, i32 t3, i32 t4
    /// </summary>


    // ── Particles / Lights ───────────────────────────────────────────

    /// <summary>
    /// CharParticleCreate (ID 211) — character particle stream (CFF/PCB).
    /// Wire: i16 charIndex, i16 particleStreamId
    /// particleStreamId=0 clears all character particles.
    /// </summary>
    private void HandleBinCharParticleCreate(ByteQueue bq)
    {
        short charIdx = bq.ReadInteger();
        short streamId = bq.ReadInteger();

        if (streamId == 0)
        {
            // Clear all particle streams attached to this character
            for (int i = _state.MapParticles.Count - 1; i >= 0; i--)
            {
                if (_state.MapParticles[i].CharIndex == charIdx && _state.MapParticles[i].CharIndex > 0)
                    _state.MapParticles.RemoveAt(i);
            }
            return;
        }

        // Remove existing char particles before creating new ones (VB6: replaces stream)
        for (int i = _state.MapParticles.Count - 1; i >= 0; i--)
        {
            if (_state.MapParticles[i].CharIndex == charIdx && _state.MapParticles[i].CharIndex > 0)
                _state.MapParticles.RemoveAt(i);
        }

        ParticleSystem.CreateCharStream(_state, streamId, charIdx);
    }

    /// <summary>
    /// ParticleCreate (ID 243) — map particle stream (PCF opcode).
    /// Wire: i16 particleGroup, u8 x, u8 y, u8 layer
    /// </summary>


    /// <summary>
    /// ParticleCreate (ID 243) — map particle stream (PCF opcode).
    /// Wire: i16 particleGroup, u8 x, u8 y, u8 layer
    /// </summary>
    private void HandleBinParticleCreate(ByteQueue bq)
    {
        short particleGroup = bq.ReadInteger();
        byte x = bq.ReadByte();
        byte y = bq.ReadByte();
        byte layer = bq.ReadByte();
        ParticleSystem.CreateMapStream(_state, particleGroup, x, y);
    }

    /// <summary>
    /// LightCreate (ID 244) — map tile light effect (PCL opcode).
    /// Wire: u8 x, u8 y, u8 range, u8 r, u8 g, u8 b
    /// </summary>


    /// <summary>
    /// LightCreate (ID 244) — map tile light effect (PCL opcode).
    /// Wire: u8 x, u8 y, u8 range, u8 r, u8 g, u8 b
    /// </summary>
    private void HandleBinLightCreate(ByteQueue bq)
    {
        byte x = bq.ReadByte();
        byte y = bq.ReadByte();
        byte range = bq.ReadByte();
        byte r = bq.ReadByte();
        byte g = bq.ReadByte();
        byte b = bq.ReadByte();
        var light = new MapLight
        {
            X = x,
            Y = y,
            Range = range,
            R = r,
            G = g,
            B = b,
            Active = true
        };
        _state.MapLights.Add(light);
        _state.LightsDirty = true;
    }

    // ════════════════════════════════════════════════════════════════
    // Handlers added during full-opcode coverage pass
    // ════════════════════════════════════════════════════════════════

    // ── Auth / Login (continued) ──────────────────────────────────


    // ── Work / Crafting ───────────────────────────────────────────

    /// <summary>
    /// WorkMode (ID 155) — work mode skill toggle (T01 opcode).
    /// Wire: u8 skill
    /// </summary>
    private void HandleBinWorkMode(ByteQueue bq)
    {
        byte skill = bq.ReadByte();
        GD.Print($"[PKT] WorkMode skill={skill}");
    }

    /// <summary>
    /// SmithWeapons/SmithArmors/CarpItems (IDs 158/159/160) — VB6 13.3 binary craft list.
    /// Smith: count, per item: name(str), grh(i16), lingH(i16), lingP(i16), lingO(i16), objIdx(i16), upgrade(i16)
    /// Carp:  count, per item: name(str), grh(i16), madera(i16), maderaElf(i16), objIdx(i16), upgrade(i16)
    /// </summary>

}
