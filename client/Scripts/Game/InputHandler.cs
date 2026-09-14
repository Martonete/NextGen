using System;
using System.Collections.Generic;
using Godot;
using ArgentumNextgen.Data;
using ArgentumNextgen.Network;

namespace ArgentumNextgen.Game;

/// <summary>
/// Translates keyboard/mouse input to server packets with VB6-accurate client-side prediction.
/// Movement uses LegalPos check + immediate camera scroll (no server round-trip lag).
///
/// VB6 flow: CheckKeys() → MoveTo() → Char_Move_by_Head() + Engine_MoveScreen()
/// Guard: UserMoving == 0 is the ONLY movement blocker (no timer).
/// Server has NO anti-flood for movement — speed is controlled entirely by client animation.
///
/// Key bindings are loaded from Teclas.ao and configurable via KeyBindPanel.
/// </summary>
public class InputHandler
{
	private readonly AoTcpClient _tcp;
	private readonly GameState _state;
	private readonly KeyBindings _keys;
	private readonly Viewport _viewport;

	// Meditation FX IDs — cleared when player moves
	private static readonly HashSet<int> MeditationFxIds = new()
	{
		4, 5, 6, 16, 42, 43, 44, 45, 103, 104, 105
	};

	// Water tile detection delegated to WorldRenderer.IsWaterGrh()

	// VB6 map borders (InMapBounds) — offset from map edges
	private const int BorderMarginLeft = 9;
	private const int BorderMarginRight = 8;
	private const int BorderMarginTop = 7;
	private const int BorderMarginBottom = 6;

	// AO20 ModUtils.bas: client-only intervals.
	private const long ConstIntervaloHeading = 120; // CONST_INTERVALO_HEADING
	private const long ConstIntervaloClick = 200;   // CONST_INTERVALO_CLICK
	private long _intervaloHeadingMs;
	private long _intervaloClickMs;

	// AO20 Protocol_Writes rate limiter: last tick an action packet was sent.
	private long _lastAttackSentMs;
	private long _lastWalkSentMs;

	// AO20 General.bas keysMovementPressedQueue: movement keys currently held, in the
	// order they were pressed. The LAST one wins (GetLastItem).
	private readonly List<Key> _movementQueue = new();

	// AO20 UserAvisado: while resting, the first walk attempt sends WriteRest once.
	private bool _userAvisado;

	// KeyUp edge detection for action keys (AO20 dispatches Accionar from Form_KeyUp).
	private readonly Dictionary<GameAction, bool> _wasPressed = new();
	private bool _kpSubtractWasPressed;

	// Track Ctrl state for release detection (both left and right Ctrl)
	private bool _ctrlWasPressed;

	/// <summary>Callback invoked when the player presses the music toggle key.</summary>
	public Action? OnToggleMusic;

	/// <summary>Callback to play a sound at position (soundId, tileX, tileY).</summary>
	public Action<int, int, int>? OnPlaySoundAt;

	public InputHandler(AoTcpClient tcp, GameState state, KeyBindings keys, Viewport viewport)
	{
		_tcp = tcp;
		_state = state;
		_keys = keys;
		_viewport = viewport;
	}

	public void Process(double delta)
	{
		if (!_state.IsLogged || _state.Paused) return;

		// Block all game input while any form/panel is open (VB6: CheckKeys disabled during forms)
		if (_state.AnyFormOpen) return;

		// Block if a GUI text field has focus (e.g. LineEdit in any panel)
		// When BlockWalkOnChat is disabled, only block non-movement input (handled below)
		var focused = _viewport.GuiGetFocusOwner();
		bool lineEditFocused = focused is LineEdit;
		if (lineEditFocused && _state.Config.BlockWalkOnChat) return;

		if (!_state.Resting) _userAvisado = false;

		// ── AO20 Check_Keys: only while the screen is not scrolling ──
		bool inputFocus = _state.ChatActive || lineEditFocused;
		if (!_state.UserMoving && !(inputFocus && _state.Config.BlockWalkOnChat)
			&& System.Environment.TickCount64 >= _state.PtCooldownUntilMs)
		{
			AddMovementToKeysMovementPressedQueue(inputFocus);
			int heading = HeadingForKey(_movementQueue.Count > 0 ? _movementQueue[^1] : Key.None);
			if (heading != 0) MoveTo(heading);
		}

		// Detect Ctrl release by polling (supports both left and right Ctrl reliably)
		if (_keys.GetKey(GameAction.Attack) == Key.Ctrl)
		{
			bool ctrlNow = Input.IsKeyPressed(Key.Ctrl);
			if (_ctrlWasPressed && !ctrlNow && !_state.ChatActive)
				AttackKeyUp();
			_ctrlWasPressed = ctrlNow;
		}

		// Everything below is blocked when chat is active (letter keys would type into chat)
		if (_state.ChatActive || lineEditFocused)
		{
			_wasPressed.Clear();
			_kpSubtractWasPressed = false;
			return;
		}

		// AO20 Accionar runs from Form_KeyUp: every action fires once, on release.
		if (Released(GameAction.PickUp))
			_tcp.SendPacket(ClientPackets.WritePickUp());
		if (Released(GameAction.UseItem))
			UseItemKey();
		if (Released(GameAction.EquipItem))
			EquipSelectedItem();
		if (Released(GameAction.Drop))
			DropSelectedItem();
		if (Released(GameAction.ShowNames))
		{
			_state.ShowNames = !_state.ShowNames;
			_state.Config.ShowNames = _state.ShowNames;
		}
		if (Released(GameAction.ToggleMusic))
			OnToggleMusic?.Invoke();
		if (Released(GameAction.Steal))
		{
			if (!_state.Dead) _tcp.SendPacket(ClientPackets.WriteUseSkill(12));
		}
		if (Released(GameAction.Hide))
		{
			if (!_state.Dead) _tcp.SendPacket(ClientPackets.WriteUseSkill(8));
		}
		if (Released(GameAction.RefreshPos))
		{
			if (_state.MainTimer.Check(TimersIndex.SendRPU))
				_tcp.SendPacket(ClientPackets.WriteRequestPos());
		}
		if (Released(GameAction.Screenshot))
		{
			string? path = ScreenshotManager.CaptureScreenshot();
			if (path != null)
				_state.EnqueueChat(new ChatMessage { Text = "Captura de pantalla guardada.", Color = "00FF00" });
		}
		if (Released(GameAction.Meditate))
		{
			if (!_state.Dead) _tcp.SendPacket(ClientPackets.WriteMeditate());
		}
		if (Released(GameAction.Rest))
			_tcp.SendPacket(ClientPackets.WriteTalk("/DESCANSAR"));
		if (Released(GameAction.SafetyToggle))
			_tcp.SendPacket(ClientPackets.WriteSafeToggle());
		bool kpSubtractNow = Input.IsKeyPressed(Key.KpSubtract);
		bool resSafetyReleased = Released(GameAction.ResSafety) || (_kpSubtractWasPressed && !kpSubtractNow);
		_kpSubtractWasPressed = kpSubtractNow;
		if (resSafetyReleased)
			_tcp.SendPacket(ClientPackets.WriteTalk("/SEGR"));

		// Macro keys: 1-9, 0 (hardcoded — these are always number keys, not rebindable)
		if (!_state.MacroPanelOpen)
		{
			for (int i = 0; i < 10; i++)
			{
				var key = i == 9 ? Key.Key0 : (Key)((long)Key.Key1 + i);
				if (_state.QuickbarKeys.Contains(key)) continue;
				if (ReleasedKey(key)) ExecuteMacro(i);
			}
		}
	}

	/// <summary>KeyUp edge for a bound action: true on the frame the key is released.</summary>
	private bool Released(GameAction action)
	{
		bool now = _keys.IsActionPressed(action);
		bool was = _wasPressed.TryGetValue(action, out var w) && w;
		_wasPressed[action] = now;
		return was && !now;
	}

	private readonly Dictionary<Key, bool> _wasKeyPressed = new();
	private bool ReleasedKey(Key key)
	{
		bool now = Input.IsKeyPressed(key);
		bool was = _wasKeyPressed.TryGetValue(key, out var w) && w;
		_wasKeyPressed[key] = now;
		return was && !now;
	}

	// ── AO20 General.bas: movement key queue ──

	private static readonly Key[] ArrowKeys = { Key.Up, Key.Down, Key.Left, Key.Right };
	private static readonly GameAction[] MovementActions = { GameAction.MoveUp, GameAction.MoveDown, GameAction.MoveLeft, GameAction.MoveRight };

	/// <summary>AO20 AddMovementToKeysMovementPressedQueue: add held movement keys (once,
	/// in press order) and drop released ones. Arrow keys are always valid; the
	/// configurable WASD binds only when no text input has focus.</summary>
	private void AddMovementToKeysMovementPressedQueue(bool inputFocus)
	{
		void Track(Key key)
		{
			if (key == Key.None) return;
			bool down = Input.IsKeyPressed(key);
			if (down) { if (!_movementQueue.Contains(key)) _movementQueue.Add(key); }
			else _movementQueue.Remove(key);
		}
		foreach (var k in ArrowKeys) Track(k);
		foreach (var action in MovementActions)
		{
			var key = _keys.GetKey(action);
			if (Array.IndexOf(ArrowKeys, key) >= 0) continue;
			if (inputFocus) _movementQueue.Remove(key);
			else Track(key);
		}
	}

	/// <summary>Heading (1 N, 2 E, 3 S, 4 W) for a movement key, 0 if it is not one.</summary>
	private int HeadingForKey(Key key)
	{
		if (key == Key.None) return 0;
		if (key == Key.Up || key == _keys.GetKey(GameAction.MoveUp)) return 1;
		if (key == Key.Right || key == _keys.GetKey(GameAction.MoveRight)) return 2;
		if (key == Key.Down || key == _keys.GetKey(GameAction.MoveDown)) return 3;
		if (key == Key.Left || key == _keys.GetKey(GameAction.MoveLeft)) return 4;
		return 0;
	}

	/// <summary>AO20 Declares.bas CanMove.</summary>
	private bool CanMove() => !_state.UserParalyzed && !_state.UserImmobilized && !_state.UserStunned;

	// ── AO20 modBindKeys.Accionar / ModGameplayUI: actions ──

	/// <summary>AO20 Accionar case BindKeys(1): attack on key release.
	/// Check(CastAttack,False) → WriteAttack (rate-limited by gIntervals.Hit) → Restart(AttackSpell).</summary>
	private void AttackKeyUp()
	{
		if (_state.Dead)
		{
			_state.EnqueueChat(new ChatMessage { Text = "¡¡Estás muerto!!", Color = "FFFF00" });
			return;
		}
		if (_state.Resting) return;
		if (!_state.MainTimer.Check(TimersIndex.CastAttack, false)) return;
		if (WriteAttack())
			_state.MainTimer.Restart(TimersIndex.AttackSpell);
	}

	/// <summary>AO20 Protocol_Writes.WriteAttack: ShouldBlockAction(ActionAttack) uses gIntervals.Hit.</summary>
	private bool WriteAttack()
	{
		long now = System.Environment.TickCount64;
		if (_lastAttackSentMs != 0 && now - _lastAttackSentMs < _state.Intervals.Hit) return false;
		_tcp.SendPacket(ClientPackets.WriteAttack());
		_lastAttackSentMs = now;
		return true;
	}

	/// <summary>AO20 UseItemKey (BindKeys(4)): gated by UseItemWithU.</summary>
	private void UseItemKey()
	{
		int slot = _state.SelectedInvSlot;
		if (slot < 0 || slot >= _state.MaxInventorySlots) return;
		if (!_state.MainTimer.Check(TimersIndex.UseItemWithU)) return;
		_tcp.SendPacket(ClientPackets.WriteUseItem((byte)(slot + 1)));
	}

	/// <summary>AO20 frmMain.frm:3221 — equip shares the UseItemWithU timer.</summary>
	private void EquipSelectedItem()
	{
		if (_state.Dead) return;
		int slot = _state.SelectedInvSlot;
		if (slot < 0 || slot >= _state.MaxInventorySlots) return;
		if (!_state.MainTimer.Check(TimersIndex.UseItemWithU)) return;
		_tcp.SendPacket(ClientPackets.WriteEquipItem((byte)(slot + 1)));
	}

	/// <summary>AO20 TirarItem / frmCantidad: gated by the Drop timer.</summary>
	private void DropSelectedItem()
	{
		if (_state.Dead) return;
		int slot = _state.SelectedInvSlot;
		if (slot < 0 || slot >= _state.MaxInventorySlots || _state.Inventory[slot].ObjIndex <= 0) return;
		if (_state.Inventory[slot].Amount == 1)
		{
			if (!_state.MainTimer.Check(TimersIndex.Drop)) return;
			_tcp.SendPacket(ClientPackets.WriteDropItem((byte)(slot + 1), 1));
		}
		else if (_state.Inventory[slot].Amount > 1)
		{
			_state.DropDialogSlot = slot;
			_state.DropDialogOpen = true;
		}
	}

	/// <summary>AO20 IntervaloPermiteClick (CONST_INTERVALO_CLICK = 200 ms).</summary>
	public bool IntervaloPermiteClick(bool actualizar = true)
	{
		long now = System.Environment.TickCount64;
		if (now - _intervaloClickMs >= ConstIntervaloClick)
		{
			if (actualizar) _intervaloClickMs = now;
			return true;
		}
		return false;
	}

	/// <summary>AO20 IntervaloPermiteHeading (CONST_INTERVALO_HEADING = 120 ms).</summary>
	private bool IntervaloPermiteHeading(bool actualizar = true)
	{
		long now = System.Environment.TickCount64;
		if (now - _intervaloHeadingMs >= ConstIntervaloHeading)
		{
			if (actualizar) _intervaloHeadingMs = now;
			return true;
		}
		return false;
	}

	/// <summary>AO20 Protocol_Writes.WriteWalk rate limiter: gIntervals.Walk / Speeding.</summary>
	private bool WriteWalk(int heading)
	{
		long now = System.Environment.TickCount64;
		float speeding = _state.UserSpeeding > 0 ? _state.UserSpeeding : 1f;
		long interval = (long)(_state.Intervals.Walk / speeding);
		if (_lastWalkSentMs != 0 && now - _lastWalkSentMs < interval) return false;
		_tcp.SendPacket(ClientPackets.WriteWalk((byte)heading));
		_lastWalkSentMs = now;
		return true;
	}

	/// <summary>
	/// AO20 General.bas MoveTo: LegalPos + CanMove → WriteWalk → Char_Move_by_Head +
	/// MoveScreen. Blocked: ChangeHeading only if it changes and 120 ms passed.
	/// Resting: one WriteRest, no step.
	/// </summary>
	private void MoveTo(int heading)
	{
		// Direction deltas: 1=N(0,-1), 2=E(1,0), 3=S(0,1), 4=W(-1,0)
		int dx = 0, dy = 0;
		switch (heading)
		{
			case 1: dy = -1; break;
			case 2: dx = 1; break;
			case 3: dy = 1; break;
			case 4: dx = -1; break;
		}

		if (!_state.Characters.TryGetValue(_state.UserCharIndex, out var ch))
			return;

		int newX = ch.PosX + dx;
		int newY = ch.PosY + dy;

		if (LegalPos(newX, newY) && CanMove())
		{
			if (_state.Resting)
			{
				// AO20: "Stop resting (we do NOT have the 1 step enforcing anymore)"
				if (!_userAvisado)
				{
					_tcp.SendPacket(ClientPackets.WriteTalk("/DESCANSAR"));
					_userAvisado = true;
				}
				return;
			}

			// Stop work/spell macros on movement (VB6: tmrTrabajo stops on move)
			_state.WorkMacro.Stop();
			_state.SpellMacro.Stop();

			// Clear meditation FX on self when moving
			for (int i = 0; i < 3; i++)
			{
				if (ch.ActiveFxSlots[i] > 0 && MeditationFxIds.Contains(ch.ActiveFxSlots[i]))
				{
					ch.ActiveFxSlots[i] = 0;
					ch.FxLoops[i] = 0;
					ch.FxFrameCounter[i] = 0;
				}
			}

			if (!WriteWalk(heading)) return;
			_state.PendingMoves++;
			_state.MainTimer.Restart(TimersIndex.Walk);

			// AO20 Char_Move_by_Head: update logical position + start animation
			ch.Heading = heading;
			ch.MoveOffsetX = -(dx * 32);
			ch.MoveOffsetY = -(dy * 32);
			ch.ScrollDirectionX = dx;
			ch.ScrollDirectionY = dy;
			ch.Moving = true;
			ch.PosX = newX;
			ch.PosY = newY;

			// AO20 MoveScreen: start camera scroll
			_state.AddToUserPosX = dx;
			_state.AddToUserPosY = dy;
			_state.UserPosX = newX;
			_state.UserPosY = newY;
			_state.UserMoving = true;
			_state.ScreenOffsetX = 0;
			_state.ScreenOffsetY = 0;

			// DoPasosFx — footstep sound for own character (no sound for admin privs)
			if (!ch.Dead && ch.Privileges != 1 && ch.Privileges != 2
				&& ch.Privileges != 3 && ch.Privileges != 5 && ch.Privileges != 25)
			{
				if (_state.UserNavigating)
				{
					OnPlaySoundAt?.Invoke(SoundManager.SND_NAVEGANDO, newX, newY);
				}
				else
				{
					ch.FootToggle = !ch.FootToggle;
					int sndId = ch.FootToggle ? SoundManager.SND_PASOS1 : SoundManager.SND_PASOS2;
					OnPlaySoundAt?.Invoke(sndId, newX, newY);
				}
			}
		}
		else
		{
			// Blocked (or can't move): just turn, only if the heading changes and 120 ms passed.
			if (ch.Heading != heading && IntervaloPermiteHeading(true))
			{
				_tcp.SendPacket(ClientPackets.WriteChangeHeading((byte)heading));
				ch.Heading = heading;
			}
		}
	}

	/// <summary>
	/// VB6 LegalPos: checks if (x,y) is a valid destination tile.
	/// Must match VB6 EXACTLY to avoid client-server desync.
	/// </summary>
	private bool LegalPos(int x, int y)
	{
		int maxX = (_state.MapData?.Width ?? 100) - BorderMarginRight;
		int maxY = (_state.MapData?.Height ?? 100) - BorderMarginBottom;
		if (x < BorderMarginLeft || x > maxX || y < BorderMarginTop || y > maxY)
			return false;

		if (_state.MapData == null) return true;

		ref var tile = ref _state.MapData.Tiles[x, y];

		if (tile.Blocked) return false;

		foreach (var kvp in _state.Characters)
		{
			if (kvp.Key == _state.UserCharIndex) continue;
			var other = kvp.Value;
			if (other.PosX == x && other.PosY == y && !other.Dead)
				return false;
		}

		bool isWater = ArgentumNextgen.Rendering.WorldRenderer.IsWaterGrh(tile.Layer1);
		if (!_state.UserNavigating && isWater)
			return false;
		if (_state.UserNavigating && !isWater)
			return false;

		return true;
	}

	/// <summary>
	/// Convert viewport pixel position to world tile coordinates.
	/// </summary>
	private (int tileX, int tileY) ViewportToTile(Vector2 viewportPos)
	{
		int cameraUserX = _state.UserPosX - _state.AddToUserPosX;
		int cameraUserY = _state.UserPosY - _state.AddToUserPosY;
		float pixelOffsetX = -_state.ScreenOffsetX;
		float pixelOffsetY = -_state.ScreenOffsetY;

		int tileX = (int)Math.Floor((viewportPos.X - pixelOffsetX) / 32f + cameraUserX - ResolutionManager.HalfTilesX);
		int tileY = (int)Math.Floor((viewportPos.Y - pixelOffsetY) / 32f + cameraUserY - ResolutionManager.HalfTilesY);
		return (tileX, tileY);
	}

	/// <summary>
	/// Handle discrete input events. Called from Main._Input().
	/// VB6 parity: attack fires on key RELEASE (Form_KeyUp), not key down.
	/// </summary>
	public void HandleInputEvent(InputEvent @event)
	{
		if (!_state.IsLogged || _state.Paused) return;
		if (_state.AnyFormOpen) return;
		if (_state.ChatActive) return;

		if (@event is InputEventKey keyEvent && !keyEvent.Pressed && !keyEvent.Echo)
		{
			// Key RELEASE — check for attack key (VB6: Form_KeyUp → BindKeys(1) → SendData "AT")
			// Note: Ctrl attack is handled in Process() via polling for left+right Ctrl support
			var key = keyEvent.Keycode;
			if (key == _keys.GetKey(GameAction.Attack) || key == Key.Space)
				AttackKeyUp();
		}
	}

	/// <summary>
	/// Check whether a click lands inside AO Libre's central interaction/core
	/// rectangle. The wider creature range deliberately remains non-interactive.
	/// </summary>
	private bool IsInCoreViewport(Vector2 viewportPos)
	{
		// VB6 receives clicks over the entire MainViewPic in fullscreen.
		if (ResolutionManager.FullscreenWorld)
			return viewportPos.X >= 0 && viewportPos.X < ResolutionManager.RenderPixelW
				&& viewportPos.Y >= 0 && viewportPos.Y < ResolutionManager.RenderPixelH;

		float coreLeft = (ResolutionManager.RenderPixelW - VisionRange.CoreWidth) * 0.5f;
		float coreTop = (ResolutionManager.RenderPixelH - VisionRange.CoreHeight) * 0.5f;
		return viewportPos.X >= coreLeft && viewportPos.X < coreLeft + VisionRange.CoreWidth
			&& viewportPos.Y >= coreTop && viewportPos.Y < coreTop + VisionRange.CoreHeight;
	}

	public void HandleLeftClick(Vector2 viewportPos)
	{
		if (!IsInCoreViewport(viewportPos)) return; // block clicks in fog area
		// AO20 ModGameplayUI: plain clicks are throttled by CONST_INTERVALO_CLICK (200 ms).
		if (!IntervaloPermiteClick(true)) return;
		var (tileX, tileY) = ViewportToTile(viewportPos);
		if (IsInMapBounds(tileX, tileY))
			_tcp.SendPacket(ClientPackets.WriteLeftClick((short)tileX, (short)tileY, _state.CoordCipher));
	}

	public void HandleRightClick(Vector2 viewportPos)
	{
		if (!IsInCoreViewport(viewportPos)) return; // block clicks in fog area
		var (tileX, tileY) = ViewportToTile(viewportPos);
		if (IsInMapBounds(tileX, tileY))
			_tcp.SendPacket(ClientPackets.WriteRightClick((short)tileX, (short)tileY, _state.CoordCipher));
	}

	private const int SkillMagia = 2, SkillRobar = 3, SkillDomar = 18, SkillProyectiles = 19;
	private const int ModoBloqueoSoltar = 0, ModoBloqueoLanzar = 1, ModoSinBloqueo = 2;

	/// <summary>
	/// AO20 ModGameplayUI.bas:120-238 — the click that resolves a pending skill
	/// (UsingSkill). Spells and arrows go through the MainTimer combo gates according to
	/// ModoHechizos; steal/tame use CastSpell; everything else is sent as is.
	/// </summary>
	public void HandleSpellClick(Vector2 viewportPos)
	{
		if (!IsInCoreViewport(viewportPos)) return; // block clicks in fog area
		var (tileX, tileY) = ViewportToTile(viewportPos);
		if (!IsInMapBounds(tileX, tileY)) return;

		var timer = _state.MainTimer;
		int mode = _state.Config.SpellCastMode;
		int skill = _state.UsingSkill;
		bool sendSkill = false;

		if (skill == SkillMagia)
		{
			if (mode == ModoBloqueoLanzar)
			{
				sendSkill = true;
				timer.Restart(TimersIndex.CastAttack);
				timer.Restart(TimersIndex.CastSpell);
			}
			else if (timer.Check(TimersIndex.AttackSpell, false))
			{
				if (timer.Check(TimersIndex.CastSpell))
				{
					sendSkill = true;
					timer.Restart(TimersIndex.CastAttack);
				}
				else if (mode == ModoSinBloqueo)
				{
					sendSkill = true;
					_state.EnqueueChat(new ChatMessage { Text = "No puedes lanzar hechizos tan rápido.", Color = "FFFF00" });
				}
				else return;
			}
			else if (mode == ModoSinBloqueo)
			{
				sendSkill = true;
				_state.EnqueueChat(new ChatMessage { Text = "No puedes lanzar tan rápido después de un golpe.", Color = "FFFF00" });
			}
			else return;
		}
		else if (skill == SkillProyectiles)
		{
			if (mode == ModoBloqueoLanzar)
			{
				sendSkill = true;
				timer.Restart(TimersIndex.Attack);    // flecha-golpe
				timer.Restart(TimersIndex.CastSpell); // flecha-hechizo
				timer.Restart(TimersIndex.Arrows);
			}
			else if (timer.Check(TimersIndex.AttackSpell, false))
			{
				if (timer.Check(TimersIndex.CastAttack, false))
				{
					if (timer.Check(TimersIndex.Arrows, false))
					{
						sendSkill = true;
						timer.Restart(TimersIndex.Attack);
						timer.Restart(TimersIndex.CastSpell);
						timer.Restart(TimersIndex.Arrows);
					}
					else if (mode == ModoSinBloqueo)
					{
						sendSkill = true;
						_state.EnqueueChat(new ChatMessage { Text = "No puedes lanzar flechas tan rápido.", Color = "FFFF00" });
					}
					else return;
				}
				else if (mode == ModoSinBloqueo) sendSkill = true;
				else return;
			}
			else if (mode == ModoSinBloqueo) sendSkill = true;
			else return;
		}
		else if (skill == SkillRobar || skill == SkillDomar)
		{
			if (timer.Check(TimersIndex.CastSpell)) sendSkill = true;
		}
		else
		{
			// Work skills (talar, minería, pesca, fundir...) and the rest: no combo gate.
			sendSkill = true;
		}

		if (sendSkill)
			_tcp.SendPacket(ClientPackets.WriteWorkLeftClick((short)tileX, (short)tileY, (byte)skill, _state.CoordCipher));
		// UsaLanzar = False / UsingSkill = 0 — the cursor is reset by the caller.
		_state.UsingSkill = 0;
	}


	public void HandleGmTeleport(Vector2 viewportPos, int currentMap)
	{
		var (tileX, tileY) = ViewportToTile(viewportPos);
		if (IsInMapBounds(tileX, tileY))
			_tcp.SendPacket(ClientPackets.WriteTalk($"/TELEP YO {currentMap} {tileX} {tileY}"));
	}

	/// <summary>
	/// Check if tile coordinates are within the current map bounds (1..Width, 1..Height).
	/// </summary>
	private bool IsInMapBounds(int x, int y)
	{
		if (_state.MapData == null) return x >= 1 && x <= 100 && y >= 1 && y <= 100;
		return x >= 1 && x <= _state.MapData.Width && y >= 1 && y <= _state.MapData.Height;
	}

	/// <summary>
	/// VB6 enviarMacro: execute a configured macro command.
	/// </summary>
	private void ExecuteMacro(int index)
	{
		if (index < 0 || index >= 10) return;
		string cmd = _state.Macros[index];
		if (string.IsNullOrEmpty(cmd)) return;

		if (cmd.StartsWith("/"))
		{
			if (cmd.Equals("/PING", System.StringComparison.OrdinalIgnoreCase))
				_state.PingSentMs = Godot.Time.GetTicksMsec();
			_tcp.SendPacket(ClientPackets.WriteTalk(cmd));
		}
		else
		{
			// Normal macro message with current chat mode
			byte[] chatPkt = _state.ChatModePrefix switch
			{
				"-" => ClientPackets.WriteYell(cmd),
				"\\" => ClientPackets.WriteWhisper("", cmd),
				_ => ClientPackets.WriteTalk(cmd),
			};
			_tcp.SendPacket(chatPkt);
		}
	}
}
