using System;
using System.Collections.Generic;
using Godot;
using ArgentumNextgen.Data;
using ArgentumNextgen.Data.Resources;
using ArgentumNextgen.Game;

namespace ArgentumNextgen.Rendering;

/// <summary>
/// Live world backdrop for the login/char-select screens: renders a real map with
/// its normal tile animations (water, torches, trees) and its NPCs, while the
/// camera pans slowly in a loop — so the menu sits over the actual game world
/// instead of a black void.
///
/// Runs on its own GameState + WorldRenderer + GrhAnimator, fully isolated from the
/// gameplay session — nothing here touches the connected player's state. No weather,
/// lights or fog: terrain, its animations, and static NPCs only.
///
/// The world renders into a SubViewport sized to the whole window (the renderer is
/// told to draw a matching tile window via SetRenderWindow), so it fills the screen
/// without stretching and without disturbing the shared ResolutionManager tile math
/// used by the gameplay viewport.
/// </summary>
public partial class LoginBackdrop : Control
{
	// Map shown behind the menu.
	private const int BackdropMap = 28;

	// Camera pan: 24px/sec, slow enough to feel like ambient drift.
	private const float PanSpeed = 24f / ResolutionManager.TileSize;
	// Keep the camera this many tiles away from the map edge, so the pan never
	// reveals the void past the map border.
	private const int EdgeMargin = 10;

	private readonly GameState _state = new();
	// Own animator so tile/NPC animations advance even before login, when Main's
	// game loop (which ticks the gameplay animator) is gated off by a null _tcp.
	private readonly GrhAnimator _animator = new();
	private GameData? _data;
	private WorldRenderer? _renderer;
	private SubViewport? _viewport;
	private TextureRect? _display;

	// Camera position in tiles (float for smooth sub-tile panning).
	private float _camX, _camY;
	private Vector2 _panDir = new Vector2(1f, 0.35f).Normalized();
	private int _minX, _maxX, _minY, _maxY;
	private bool _ready;

	/// <summary>
	/// Build the backdrop. Safe to fail: if the map can't be loaded the node just
	/// stays blank and the menu renders over the plain background as before.
	/// </summary>
	public void Init(GameData data, IResourceProvider resources)
	{
		_data = data;

		MapData map;
		try
		{
			map = MapLoader.Load(resources, BackdropMap);
		}
		catch (Exception e)
		{
			GD.PrintErr($"[LOGIN-BG] Map {BackdropMap} failed to load — backdrop disabled: {e.Message}");
			return;
		}

		_state.MapData = map;
		_state.CurrentMap = BackdropMap;

		// Pan bounds: stay clear of the map border so we never pan into the void.
		_minX = EdgeMargin;
		_maxX = Math.Max(EdgeMargin, map.Width - EdgeMargin);
		_minY = EdgeMargin;
		_maxY = Math.Max(EdgeMargin, map.Height - EdgeMargin);
		_camX = (_minX + _maxX) / 2f;
		_camY = (_minY + _maxY) / 2f;

		MouseFilter = MouseFilterEnum.Ignore;
		ProcessMode = ProcessModeEnum.Always;
		ProcessPriority = -100;

		var size = BackdropSize();
		_viewport = new SubViewport
		{
			Size = size,
			RenderTargetUpdateMode = SubViewport.UpdateMode.Always,
			HandleInputLocally = false,
			TransparentBg = false,
		};
		AddChild(_viewport);

		var world = new Node2D
		{
			Name = "BackdropWorld",
			TextureFilter = CanvasItem.TextureFilterEnum.Linear,
		};
		_viewport.AddChild(world);

		_renderer = new WorldRenderer();
		_renderer.Init(_state, data, _animator, resources);
		_renderer.SetRenderWindow(size);
		_renderer.SetSubpixelCamera(true);
		_renderer.SetClampCameraToMap(true);
		_renderer.TextureFilter = CanvasItem.TextureFilterEnum.Linear;
		world.AddChild(_renderer);

		// The viewport is already window-sized, so draw it 1:1 with no stretching.
		// Nearest keeps the pixel art crisp.
		_display = new TextureRect
		{
			Texture = _viewport.GetTexture(),
			StretchMode = TextureRect.StretchModeEnum.Keep,
			TextureFilter = CanvasItem.TextureFilterEnum.Linear,
			MouseFilter = MouseFilterEnum.Ignore,
		};
		_display.SetAnchorsPreset(LayoutPreset.FullRect);
		AddChild(_display);

		SpawnMapNpcs(resources, map);

		_renderer.RebuildWaterMap();
		_renderer.BuildRoofRegions();
		_ready = true;
		ApplyCamera();
	}

	public override void _Process(double delta)
	{
		if (!_ready || !Visible) return;

		// Advance tile + NPC animations independently of Main's game loop.
		_animator.Update((float)delta, _data!);

		_camX += _panDir.X * PanSpeed * (float)delta;
		_camY += _panDir.Y * PanSpeed * (float)delta;

		// Bounce off the pan bounds so the drift loops forever without a cut.
		if (_camX <= _minX) { _camX = _minX; _panDir.X = Math.Abs(_panDir.X); }
		else if (_camX >= _maxX) { _camX = _maxX; _panDir.X = -Math.Abs(_panDir.X); }
		if (_camY <= _minY) { _camY = _minY; _panDir.Y = Math.Abs(_panDir.Y); }
		else if (_camY >= _maxY) { _camY = _maxY; _panDir.Y = -Math.Abs(_panDir.Y); }

		ApplyCamera();
	}

	/// <summary>
	/// Feed the float camera into the renderer's tile+pixel-offset camera model:
	/// integer tile is the center, the fraction becomes the smooth pixel offset.
	/// </summary>
	private void ApplyCamera()
	{
		int tileX = (int)Math.Floor(_camX);
		int tileY = (int)Math.Floor(_camY);
		_state.UserPosX = tileX;
		_state.UserPosY = tileY;
		// ScreenOffset is negated into the render offset, matching how the gameplay
		// camera scrolls between tiles.
		_state.ScreenOffsetX = (_camX - tileX) * ResolutionManager.TileSize;
		_state.ScreenOffsetY = (_camY - tileY) * ResolutionManager.TileSize;
		_renderer?.QueueRedraw();
	}

	/// <summary>
	/// Spawn the map's NPCs as static Characters from the .inf NpcIndex + NPCs.dat
	/// (Body/Head/Heading). Purely cosmetic; failures are non-fatal (no NPCs shown).
	/// </summary>
	private void SpawnMapNpcs(IResourceProvider resources, MapData map)
	{
		_state.Characters.Clear();

		var defs = NpcAppearance.LoadTable(resources);
		if (defs.Count == 0) return;

		// NPC placements live in the map .inf. The client's .aomap format doesn't
		// always ship a companion .aoinf with NPCs, so read placements straight from
		// the legacy Mapa{N}.inf (still packed) into a coord→npc map.
		var placements = LoadLegacyNpcPlacements(resources, map);

		int nextIndex = 1;
		int positionCount = 0;
		int missingAppearance = 0;
		for (int y = 1; y <= map.Height; y++)
		{
			for (int x = 1; x <= map.Width; x++)
			{
				int npcNum = map.Tiles[x, y].NpcIndex;
				if (npcNum <= 0) placements.TryGetValue((x, y), out npcNum);
				if (npcNum <= 0) continue;

				positionCount++;
				if (!defs.TryGetValue(npcNum, out var def) || (def.Body <= 0 && def.Head <= 0))
				{
					missingAppearance++;
					continue;
				}

				_state.Characters[nextIndex] = new Character
				{
					CharIndex = nextIndex,
					Body = def.Body,
					Head = def.Head,
					Heading = def.Heading is >= 1 and <= 4 ? def.Heading : 3,
					PosX = x,
					PosY = y,
					Name = "",
					NpcNumber = npcNum,
					FovAlpha = 1f,
				};
				nextIndex++;
			}
		}
		GD.Print($"[LOGIN-BG] Backdrop NPCs on map {BackdropMap}: positions={positionCount}, spawned={nextIndex - 1}, missingAppearance={missingAppearance}.");
	}

	/// <summary>
	/// Read NPC placements from the legacy Mapa{N}.inf: header(10) then per-tile
	/// flags byte with optional exit(6)/npc(2)/obj(4) payloads. Returns coord→npc.
	/// Non-fatal: returns empty on any read/format issue.
	/// </summary>
	private static Dictionary<(int, int), int> LoadLegacyNpcPlacements(IResourceProvider resources, MapData map)
	{
		var placements = new Dictionary<(int, int), int>();
		string infPath = $"Maps/Mapa{BackdropMap}.inf";
		if (!resources.Exists(infPath)) return placements;

		try
		{
			byte[] data = resources.ReadBytes(infPath);
			using var r = new System.IO.BinaryReader(new System.IO.MemoryStream(data));
			r.BaseStream.Seek(10, System.IO.SeekOrigin.Begin); // 5 × Int16 header

			// Legacy .inf is a fixed 100×100 grid regardless of the .aomap size.
			for (int y = 1; y <= 100; y++)
			{
				for (int x = 1; x <= 100; x++)
				{
					if (r.BaseStream.Position >= r.BaseStream.Length) return placements;
					byte flags = r.ReadByte();
					if ((flags & 1) != 0) r.BaseStream.Seek(6, System.IO.SeekOrigin.Current); // exit
					if ((flags & 2) != 0)
					{
						short npc = r.ReadInt16();
						if (npc > 0 && x <= map.Width && y <= map.Height) placements[(x, y)] = npc;
					}
					if ((flags & 4) != 0) r.BaseStream.Seek(4, System.IO.SeekOrigin.Current); // obj
				}
			}
		}
		catch (Exception e)
		{
			GD.PrintErr($"[LOGIN-BG] Legacy .inf NPC read failed: {e.Message}");
		}
		return placements;
	}

	/// <summary>Window-sized backdrop viewport, clamped to a sane minimum.</summary>
	private Vector2I BackdropSize()
	{
		var w = DisplayServer.WindowGetSize();
		return new Vector2I(Math.Max(w.X, ResolutionManager.ViewportW),
							Math.Max(w.Y, ResolutionManager.ViewportH));
	}

	/// <summary>Resize the backdrop viewport + render window to a new resolution.</summary>
	public void OnResolutionChanged()
	{
		if (!_ready || _viewport == null || _renderer == null) return;
		var size = BackdropSize();
		_viewport.Size = size;
		_renderer.SetRenderWindow(size);
		ApplyCamera();
	}

	/// <summary>Show/hide the backdrop and stop its work while hidden.</summary>
	public void SetActive(bool active)
	{
		Visible = active;
		if (_viewport != null)
			_viewport.RenderTargetUpdateMode = active
				? SubViewport.UpdateMode.Always
				: SubViewport.UpdateMode.Disabled;
	}
}
