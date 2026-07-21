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

	// VB6 attack cooldown: tAt = 1000ms (timestamp-based, frame-rate independent)
	private const long AttackCooldownMs = 1000;
	private long _attackUntilMs;

	// VB6 position refresh cooldown
	private float _refreshTimer;
	private const float RefreshCooldownMs = 2000f;

	// Generic key repeat cooldown (VB6 CheckKeys runs at ~32ms tick rate)
	// Prevent rapid-fire sends when holding a key at 60fps.
	private const long KeyCooldownMs = 300;
	private long _keyCooldownUntilMs;

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

		float deltaMs = (float)delta * 1000f;
		long nowMs = System.Environment.TickCount64;

		// Advance refresh timer (still delta-based, non-critical)
		if (_refreshTimer > 0) _refreshTimer -= deltaMs;

		// Detect Ctrl release by polling (supports both left and right Ctrl reliably)
		if (_keys.GetKey(GameAction.Attack) == Key.Ctrl)
		{
			bool ctrlNow = Input.IsKeyPressed(Key.Ctrl);
			if (_ctrlWasPressed && !ctrlNow && !_state.ChatActive)
			{
				if (nowMs >= _attackUntilMs && !_state.Resting && !_state.Meditating && !_state.Dead)
				{
					_tcp.SendPacket(ClientPackets.WriteAttack());
					_attackUntilMs = nowMs + AttackCooldownMs;
				}
			}
			_ctrlWasPressed = ctrlNow;
		}

		// VB6: paralyzed users can attack and cast spells, only movement is blocked
		if (!_state.UserParalyzed)
		{
			// Time-based PT correction cooldown (blocks moves after server rejected one)
			if (System.Environment.TickCount64 < _state.PtCooldownUntilMs)
			{
				// Still in cooldown — skip movement
			}
			else if (!_state.UserMoving && _state.PendingMoves < 2)
			{
				// When BlockWalkOnChat is enabled, block all movement while chatting
				bool blockMovement = _state.ChatActive && _state.Config.BlockWalkOnChat;
				if (!blockMovement)
				{
				// Arrow keys: always available (hardcoded, not rebindable — VB6 same)
				if (Input.IsKeyPressed(Key.Up))
					TryMove(1); // North
				else if (Input.IsKeyPressed(Key.Right))
					TryMove(2); // East
				else if (Input.IsKeyPressed(Key.Down))
					TryMove(3); // South
				else if (Input.IsKeyPressed(Key.Left))
					TryMove(4); // West
				// Configurable movement keys (default WASD): only when chat is NOT active
				else if (!_state.ChatActive)
				{
					if (_keys.IsActionPressed(GameAction.MoveUp))
						TryMove(1);
					else if (_keys.IsActionPressed(GameAction.MoveRight))
						TryMove(2);
					else if (_keys.IsActionPressed(GameAction.MoveDown))
						TryMove(3);
					else if (_keys.IsActionPressed(GameAction.MoveLeft))
						TryMove(4);
				}
				}
			}
		}

		// Everything below is blocked when chat is active (letter keys would type into chat)
		if (_state.ChatActive) return;
		if (lineEditFocused) return;

		// Attack is handled in HandleInputEvent() on key RELEASE (VB6 Form_KeyUp parity)

		// All action keys below share a cooldown to prevent rapid-fire when held.
		if (nowMs < _keyCooldownUntilMs) return;

		// Pick up item (VB6: AG)
		if (_keys.IsActionPressed(GameAction.PickUp))
		{
			_tcp.SendPacket(ClientPackets.WritePickUp());
			_keyCooldownUntilMs = nowMs + KeyCooldownMs;
		}
		// Use item from selected inventory slot
		else if (_keys.IsActionPressed(GameAction.UseItem))
		{
			int slot = _state.SelectedInvSlot;
			if (slot >= 0 && slot < _state.MaxInventorySlots)
			{
				_tcp.SendPacket(ClientPackets.WriteUseItem((byte)(slot + 1)));
				_keyCooldownUntilMs = nowMs + KeyCooldownMs;
			}
		}
		// Equip item from selected inventory slot
		else if (_keys.IsActionPressed(GameAction.EquipItem))
		{
			int slot = _state.SelectedInvSlot;
			if (slot >= 0 && slot < _state.MaxInventorySlots)
			{
				_tcp.SendPacket(ClientPackets.WriteEquipItem((byte)(slot + 1)));
				_keyCooldownUntilMs = nowMs + KeyCooldownMs;
			}
		}
		// Drop item from selected inventory slot
		else if (_keys.IsActionPressed(GameAction.Drop))
		{
			int slot = _state.SelectedInvSlot;
			if (slot >= 0 && slot < _state.MaxInventorySlots && _state.Inventory[slot].ObjIndex > 0)
			{
				if (_state.Inventory[slot].Amount == 1)
				{
					_tcp.SendPacket(ClientPackets.WriteDropItem((byte)(slot + 1), 1));
				}
				else if (_state.Inventory[slot].Amount > 1)
				{
					_state.DropDialogSlot = slot;
					_state.DropDialogOpen = true;
				}
			}
			_keyCooldownUntilMs = nowMs + KeyCooldownMs;
		}
		// Toggle names display (client-side only)
		else if (_keys.IsActionPressed(GameAction.ShowNames))
		{
			_state.ShowNames = !_state.ShowNames;
			_state.Config.ShowNames = _state.ShowNames;
			_keyCooldownUntilMs = nowMs + KeyCooldownMs;
		}
		// Toggle music
		else if (_keys.IsActionPressed(GameAction.ToggleMusic))
		{
			OnToggleMusic?.Invoke();
			_keyCooldownUntilMs = nowMs + KeyCooldownMs;
		}
		// Steal (VB6: UK12 — eSkill.Robar)
		else if (_keys.IsActionPressed(GameAction.Steal))
		{
			_tcp.SendPacket(ClientPackets.WriteUseSkill(12));
			_keyCooldownUntilMs = nowMs + KeyCooldownMs;
		}
		// Hide/Stealth (VB6: UK9 — eSkill.Ocultarse)
		else if (_keys.IsActionPressed(GameAction.Hide))
		{
			_tcp.SendPacket(ClientPackets.WriteUseSkill(8));
			_keyCooldownUntilMs = nowMs + KeyCooldownMs;
		}
		// Refresh position (VB6: RPU)
		else if (_keys.IsActionPressed(GameAction.RefreshPos))
		{
			if (_refreshTimer <= 0)
			{
				_tcp.SendPacket(ClientPackets.WriteRequestPos());
				_refreshTimer = RefreshCooldownMs;
			}
			_keyCooldownUntilMs = nowMs + KeyCooldownMs;
		}
		// Screenshot
		else if (_keys.IsActionPressed(GameAction.Screenshot))
		{
			string? path = ScreenshotManager.CaptureScreenshot();
			if (path != null)
			{
				_state.EnqueueChat(new ChatMessage
				{
					Text = "Captura de pantalla guardada.",
					Color = "00FF00"
				});
			}
			_keyCooldownUntilMs = nowMs + KeyCooldownMs;
		}
		// Meditate (VB6: /MEDITAR)
		else if (_keys.IsActionPressed(GameAction.Meditate))
		{
			_tcp.SendPacket(ClientPackets.WriteMeditate());
			_keyCooldownUntilMs = nowMs + KeyCooldownMs;
		}
		// Rest (VB6: /DESCANSAR)
		else if (_keys.IsActionPressed(GameAction.Rest))
		{
			_tcp.SendPacket(ClientPackets.WriteTalk("/DESCANSAR"));
			_keyCooldownUntilMs = nowMs + KeyCooldownMs;
		}
		// PvP safety toggle (VB6: /SEG)
		else if (_keys.IsActionPressed(GameAction.SafetyToggle))
		{
			_tcp.SendPacket(ClientPackets.WriteSafeToggle());
			_keyCooldownUntilMs = nowMs + KeyCooldownMs;
		}
		// Resurrection safety toggle (VB6: /SEGR) — accepts both keyboard minus and numpad minus
		else if (_keys.IsActionPressed(GameAction.ResSafety) || Input.IsKeyPressed(Key.KpSubtract))
		{
			_tcp.SendPacket(ClientPackets.WriteTalk("/SEGR"));
			_keyCooldownUntilMs = nowMs + KeyCooldownMs;
		}
		// Macro keys: 1-9, 0 (hardcoded — these are always number keys, not rebindable)
		else if (!_state.MacroPanelOpen)
		{
			int macroIdx = -1;
			if (Input.IsKeyPressed(Key.Key1)) macroIdx = 0;
			else if (Input.IsKeyPressed(Key.Key2)) macroIdx = 1;
			else if (Input.IsKeyPressed(Key.Key3)) macroIdx = 2;
			else if (Input.IsKeyPressed(Key.Key4)) macroIdx = 3;
			else if (Input.IsKeyPressed(Key.Key5)) macroIdx = 4;
			else if (Input.IsKeyPressed(Key.Key6)) macroIdx = 5;
			else if (Input.IsKeyPressed(Key.Key7)) macroIdx = 6;
			else if (Input.IsKeyPressed(Key.Key8)) macroIdx = 7;
			else if (Input.IsKeyPressed(Key.Key9)) macroIdx = 8;
			else if (Input.IsKeyPressed(Key.Key0)) macroIdx = 9;

			if (macroIdx >= 0)
			{
				ExecuteMacro(macroIdx);
				_keyCooldownUntilMs = nowMs + KeyCooldownMs;
			}
		}
	}

	/// <summary>
	/// Attempt to move in given direction with client-side prediction.
	/// VB6: CheckKeys → MoveTo → Char_Move_by_Head + Engine_MoveScreen
	/// </summary>
	private void TryMove(int heading)
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

		if (LegalPos(newX, newY))
		{
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

			// Send movement packet to server (binary: Walk + heading byte)
			_tcp.SendPacket(ClientPackets.WriteWalk((byte)heading));
			_state.PendingMoves++;

			// VB6 Char_Move_by_Head: update logical position + start animation
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

			// VB6: DoPasosFx — footstep sound for own character
			// VB6: no sound for priv 1,2,3,5,25 (admin types)
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
			// Blocked tile: just turn, don't move (VB6: only send CHEA if heading changed)
			if (ch.Heading != heading)
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
		float pixelOffsetX = (float)Math.Round(-_state.ScreenOffsetX);
		float pixelOffsetY = (float)Math.Round(-_state.ScreenOffsetY);

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
		long nowMs = System.Environment.TickCount64;

		if (@event is InputEventKey keyEvent && !keyEvent.Pressed && !keyEvent.Echo)
		{
			// Key RELEASE — check for attack key (VB6: Form_KeyUp → BindKeys(1) → SendData "AT")
			// Note: Ctrl attack is handled in Process() via polling for left+right Ctrl support
			var key = keyEvent.Keycode;
			if (key == _keys.GetKey(GameAction.Attack) || key == Key.Space)
			{
				if (nowMs >= _attackUntilMs && !_state.Resting && !_state.Meditating && !_state.Dead)
				{
					_tcp.SendPacket(ClientPackets.WriteAttack());
					_attackUntilMs = nowMs + AttackCooldownMs;
				}
			}
		}
	}

	/// <summary>
	/// Check if a click position is inside the illuminated core viewport (17x13).
	/// Clicks in the fog area (extra tiles at higher resolutions) are blocked.
	/// </summary>
	private bool IsInCoreViewport(Vector2 viewportPos)
	{
		// Core area is centered in the SubViewport
		float coreLeft = (ResolutionManager.ViewportW - 544f) / 2f;
		float coreTop = (ResolutionManager.ViewportH - 416f) / 2f;
		return viewportPos.X >= coreLeft && viewportPos.X < coreLeft + 544
			&& viewportPos.Y >= coreTop && viewportPos.Y < coreTop + 416;
	}

	public void HandleLeftClick(Vector2 viewportPos)
	{
		if (!IsInCoreViewport(viewportPos)) return; // block clicks in fog area
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

	public void HandleSpellClick(Vector2 viewportPos)
	{
		if (!IsInCoreViewport(viewportPos)) return; // block clicks in fog area
		var (tileX, tileY) = ViewportToTile(viewportPos);
		if (IsInMapBounds(tileX, tileY))
		{
			_tcp.SendPacket(ClientPackets.WriteWorkLeftClick((short)tileX, (short)tileY, (byte)_state.UsingSkill, _state.CoordCipher));
			_state.UsingSkill = 0;
		}
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
