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

	// Water tile GRH range (VB6: Layer1 1505-1520 with no Layer2 = water)
	private const int WaterGrhMin = 1505;
	private const int WaterGrhMax = 1520;

	// VB6 map borders (InMapBounds) — offset from map edges
	private const int BorderMarginLeft = 9;
	private const int BorderMarginRight = 8;
	private const int BorderMarginTop = 7;
	private const int BorderMarginBottom = 6;

	// VB6 attack cooldown: tAt = 1000ms
	private const float AttackCooldownMs = 1000f;
	private float _attackTimer;

	// VB6 position refresh cooldown
	private float _refreshTimer;
	private const float RefreshCooldownMs = 2000f;

	// Generic key repeat cooldown (VB6 CheckKeys runs at ~32ms tick rate)
	// Prevent rapid-fire sends when holding a key at 60fps.
	private const float KeyCooldownMs = 300f;
	private float _keyCooldown;

	// Track Ctrl state for release detection (both left and right Ctrl)
	private bool _ctrlWasPressed;

	/// <summary>Callback invoked when the player presses the music toggle key.</summary>
	public Action? OnToggleMusic;

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
		var focused = _viewport.GuiGetFocusOwner();
		if (focused is LineEdit) return;

		float deltaMs = (float)delta * 1000f;

		// Advance cooldown timers
		if (_attackTimer > 0) _attackTimer -= deltaMs;
		if (_refreshTimer > 0) _refreshTimer -= deltaMs;
		if (_keyCooldown > 0) _keyCooldown -= deltaMs;

		// Detect Ctrl release by polling (supports both left and right Ctrl reliably)
		if (_keys.GetKey(GameAction.Attack) == Key.Ctrl)
		{
			bool ctrlNow = Input.IsKeyPressed(Key.Ctrl);
			if (_ctrlWasPressed && !ctrlNow && !_state.ChatActive)
			{
				if (_attackTimer <= 0 && !_state.Resting && !_state.Meditating && !_state.Dead)
				{
					_tcp.SendPacket(ClientPackets.WriteAttack());
					_attackTimer = AttackCooldownMs;
				}
			}
			_ctrlWasPressed = ctrlNow;
		}

		// VB6: paralyzed users can attack and cast spells, only movement is blocked
		if (!_state.UserParalyzed)
		{
			// Decrement PT correction cooldown (blocks moves after server rejected one)
			if (_state.PtCooldownFrames > 0)
			{
				_state.PtCooldownFrames--;
			}
			else if (!_state.UserMoving && _state.PendingMoves < 2)
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

		// Everything below is blocked when chat is active (letter keys would type into chat)
		if (_state.ChatActive) return;

		// Attack is handled in HandleInputEvent() on key RELEASE (VB6 Form_KeyUp parity)

		// All action keys below share a cooldown to prevent rapid-fire when held.
		if (_keyCooldown > 0) return;

		// Pick up item (VB6: AG)
		if (_keys.IsActionPressed(GameAction.PickUp))
		{
			_tcp.SendPacket(ClientPackets.WritePickUp());
			_keyCooldown = KeyCooldownMs;
		}
		// Use item from selected inventory slot
		else if (_keys.IsActionPressed(GameAction.UseItem))
		{
			int slot = _state.SelectedInvSlot;
			if (slot >= 0 && slot < 25)
			{
				_tcp.SendPacket(ClientPackets.WriteUseItem((byte)(slot + 1)));
				_keyCooldown = KeyCooldownMs;
			}
		}
		// Equip item from selected inventory slot
		else if (_keys.IsActionPressed(GameAction.EquipItem))
		{
			int slot = _state.SelectedInvSlot;
			if (slot >= 0 && slot < 25)
			{
				_tcp.SendPacket(ClientPackets.WriteEquipItem((byte)(slot + 1)));
				_keyCooldown = KeyCooldownMs;
			}
		}
		// Drop item from selected inventory slot
		else if (_keys.IsActionPressed(GameAction.Drop))
		{
			if (_state.ItemSafety)
			{
				_state.ChatMessages.Enqueue(new ChatMessage
				{
					Text = "Desactiva el seguro de items primero.",
					Color = "FF0000"
				});
			}
			else
			{
				int slot = _state.SelectedInvSlot;
				if (slot >= 0 && slot < 25 && _state.Inventory[slot].ObjIndex > 0)
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
			}
			_keyCooldown = KeyCooldownMs;
		}
		// Toggle names display (client-side only)
		else if (_keys.IsActionPressed(GameAction.ShowNames))
		{
			_state.ShowNames = !_state.ShowNames;
			_state.Config.ShowNames = _state.ShowNames;
			_keyCooldown = KeyCooldownMs;
		}
		// Toggle music
		else if (_keys.IsActionPressed(GameAction.ToggleMusic))
		{
			OnToggleMusic?.Invoke();
			_keyCooldown = KeyCooldownMs;
		}
		// Steal (VB6: UK12 — eSkill.Robar)
		else if (_keys.IsActionPressed(GameAction.Steal))
		{
			_tcp.SendPacket(ClientPackets.WriteUseSkill(12));
			_keyCooldown = KeyCooldownMs;
		}
		// Hide/Stealth (VB6: UK9 — eSkill.Ocultarse)
		else if (_keys.IsActionPressed(GameAction.Hide))
		{
			_tcp.SendPacket(ClientPackets.WriteUseSkill(8));
			_keyCooldown = KeyCooldownMs;
		}
		// Refresh position (VB6: RPU)
		else if (_keys.IsActionPressed(GameAction.RefreshPos))
		{
			if (_refreshTimer <= 0)
			{
				_tcp.SendPacket(ClientPackets.WriteRequestPos());
				_refreshTimer = RefreshCooldownMs;
			}
			_keyCooldown = KeyCooldownMs;
		}
		// Screenshot
		else if (_keys.IsActionPressed(GameAction.Screenshot))
		{
			string? path = ScreenshotManager.CaptureScreenshot();
			if (path != null)
			{
				_state.ChatMessages.Enqueue(new ChatMessage
				{
					Text = "Captura de pantalla guardada.",
					Color = "00FF00"
				});
			}
			_keyCooldown = KeyCooldownMs;
		}
		// Meditate (VB6: /MEDITAR)
		else if (_keys.IsActionPressed(GameAction.Meditate))
		{
			_tcp.SendPacket(ClientPackets.WriteMeditate());
			_keyCooldown = KeyCooldownMs;
		}
		// PvP safety toggle (VB6: /SEG)
		else if (_keys.IsActionPressed(GameAction.SafetyToggle))
		{
			_tcp.SendPacket(ClientPackets.WriteSafeToggle());
			_keyCooldown = KeyCooldownMs;
		}
		// Resurrection safety toggle (VB6: /SEGR) — accepts both keyboard minus and numpad minus
		else if (_keys.IsActionPressed(GameAction.ResSafety) || Input.IsKeyPressed(Key.KpSubtract))
		{
			_tcp.SendPacket(ClientPackets.WriteTalk("/SEGR"));
			_keyCooldown = KeyCooldownMs;
		}
		// Item safety toggle (VB6: ISItem)
		else if (_keys.IsActionPressed(GameAction.ItemSafety))
		{
			_state.ItemSafety = !_state.ItemSafety;
			_state.ChatMessages.Enqueue(new ChatMessage
			{
				Text = _state.ItemSafety
					? ">>SEGURO DE ITEMS ACTIVADO<<"
					: ">>SEGURO DE ITEMS DESACTIVADO<<",
				Color = _state.ItemSafety ? "00FF00" : "FF0000"
			});
			_keyCooldown = KeyCooldownMs;
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
				_keyCooldown = KeyCooldownMs;
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

		bool isWater = tile.Layer1 >= WaterGrhMin && tile.Layer1 <= WaterGrhMax && tile.Layer2 == 0;
		if (!_state.UserNavigating && isWater)
			return false;
		if (_state.UserNavigating && !isWater)
			return false;

		return true;
	}

	/// <summary>
	/// Convert viewport pixel position to world tile coordinates.
	/// </summary>
	private (int tileX, int tileY) ViewportToTile(Vector2 viewportPos, int userX, int userY)
	{
		int halfX = ResolutionManager.HalfRenderTilesX;
		int halfY = ResolutionManager.HalfRenderTilesY;
		int tileX = userX + (int)viewportPos.X / 32 - halfX;
		int tileY = userY + (int)viewportPos.Y / 32 - halfY;
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
			{
				if (_attackTimer <= 0 && !_state.Resting && !_state.Meditating && !_state.Dead)
				{
					_tcp.SendPacket(ClientPackets.WriteAttack());
					_attackTimer = AttackCooldownMs;
				}
			}
		}
	}

	public void HandleLeftClick(Vector2 viewportPos, int userX, int userY)
	{
		var (tileX, tileY) = ViewportToTile(viewportPos, userX, userY);
		if (IsInMapBounds(tileX, tileY))
			_tcp.SendPacket(ClientPackets.WriteLeftClick((short)tileX, (short)tileY));
	}

	public void HandleRightClick(Vector2 viewportPos, int userX, int userY)
	{
		var (tileX, tileY) = ViewportToTile(viewportPos, userX, userY);
		if (IsInMapBounds(tileX, tileY))
			_tcp.SendPacket(ClientPackets.WriteRightClick((short)tileX, (short)tileY));
	}

	public void HandleSpellClick(Vector2 viewportPos, int userX, int userY)
	{
		var (tileX, tileY) = ViewportToTile(viewportPos, userX, userY);
		if (IsInMapBounds(tileX, tileY))
		{
			_tcp.SendPacket(ClientPackets.WriteWorkLeftClick((short)tileX, (short)tileY, (byte)_state.UsingSkill));
			_state.UsingSkill = 0;
		}
	}

	public void HandleGmTeleport(Vector2 viewportPos, int userX, int userY, int currentMap)
	{
		var (tileX, tileY) = ViewportToTile(viewportPos, userX, userY);
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
