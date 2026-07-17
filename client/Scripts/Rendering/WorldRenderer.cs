using System;
using System.Collections.Generic;
using Godot;
using ArgentumNextgen.Data;
using ArgentumNextgen.Data.Resources;
using ArgentumNextgen.Game;

namespace ArgentumNextgen.Rendering;

/// <summary>
/// Renders the game world matching VB6 RenderScreen.
///
/// Layer architecture (children of WorldRenderer, drawn after parent _Draw):
///   WorldRenderer._Draw()         → PASS 1 (water tiles) + collect reflection data
///   ReflectedAuraLayer (additive) → reflected auras (behind body)
///   ReflectionBodyLayer           → reflected character body + FX (on top of auras)
///   NonWaterMaskLayer             → PASS 1b (non-water L1 mask, covers reflection overflow)
///   Layer2Layer                   → PASS 2 (layer 2, covers overlap)
///   AuraLayer (additive)          → normal auras
///   ContentLayer                  → PASS 3 (ground objects + characters + layer 3)
///   DialogOverlayLayer (z=1)     → dialog text (above all characters/NPCs)
///   AdditiveParticleLayer (z=2)   → particles (VB6: D3DBLEND_ONE/ONE)
///   RoofLayer (z=3)               → PASS 4 (roof with fade)
///
/// Draw order: Terrain L1 → Reflections → Refl.Auras → L1 mask → L2 → Auras → Characters → Particles → Roof.
/// Reflections + reflected auras are clipped to water tiles by the mask (non-water L1 redraw)
/// and by L2 borders drawn on top.
/// </summary>
public partial class WorldRenderer : Node2D
{
	private GameState? _state;
	private GameData? _data;
	private GrhAnimator? _animator;
	private IResourceProvider? _resources;
	private readonly Dictionary<int, MapData> _mapCache = new();
	private readonly Dictionary<int, AdjacentMapCache> _adjacentCache = new();

	// Child layers
	private ReflectedAuraLayer? _reflAuraLayer;
	private ReflectionBodyLayer? _reflBodyLayer;
	private NonWaterMaskLayer? _maskLayer;
	private Layer2Layer? _layer2Layer;
	private AuraAdditiveLayer? _auraLayer;
	private ContentLayer? _contentLayer;
	private DialogOverlayLayer? _dialogLayer;
	private AdditiveParticleLayer? _additiveLayer;
	private RoofLayer? _roofLayer;
	private WeatherRenderer? _weatherRenderer;
	private FloatingTextLayer? _floatingTextLayer;
	private FogOverlayLayer? _fogOverlay;

	private const int TileSize = 32;

	// Render window override (login backdrop): when set, this renderer draws its
	// own tile window instead of the shared gameplay viewport's. Null = gameplay,
	// which reads ResolutionManager exactly as before.
	private Vector2I? _renderWindowOverride;

	/// <summary>
	/// Render a custom pixel-sized window instead of the shared gameplay viewport.
	/// Used by the login backdrop to fill the whole screen; gameplay leaves it unset.
	/// </summary>
	public void SetRenderWindow(Vector2I sizePx) => _renderWindowOverride = sizePx;

	// Viewport dimensions — dynamic, read from ResolutionManager
	private int ViewportWidth => _renderWindowOverride?.X ?? ResolutionManager.ViewportW;
	private int ViewportHeight => _renderWindowOverride?.Y ?? ResolutionManager.ViewportH;

	// How many tiles from center to edge (visible range) — dynamic
	private int HalfWindowTileWidth => _renderWindowOverride.HasValue
		? _renderWindowOverride.Value.X / (2 * TileSize) + 1
		: ResolutionManager.HalfTilesX;
	private int HalfWindowTileHeight => _renderWindowOverride.HasValue
		? _renderWindowOverride.Value.Y / (2 * TileSize) + 1
		: ResolutionManager.HalfTilesY;

	// Buffer sizes beyond visible area
	private const int TerrainBufferSize = 4; // L1-L4: supports GRHs up to 128px (4 tiles). VB6 max sprite ~128px.
	private const int CharBufferSize = 1;     // Characters/NPCs: +1 tile for smooth fade at viewport edge

	// VB6: bTechoAB — roof alpha (per-region fade, delta-time based)
	private float _roofAlpha = 255f;
	private const float RoofFadeSpeed = 400f; // units per second (255→20 in ~0.6s)
	private const float RoofMinAlpha = 45f; // ~18% opacity — visible ghost of the roof

	// Pre-computed roof region map: each L4 tile belongs to a connected region (1-based ID).
	// Tiles without L4 have regionId = 0. Built once per map load.
	private int[,]? _roofRegionMap;
	private int _activeRoofRegion;  // region the player is currently inside (0 = outdoors)
	private int _fadingRoofRegion;  // region currently being faded (persists during fade-out)

	// Delta time in ms for current frame (set in _Process, used in _Draw)
	private float _deltaMs;

	// Per-frame character position index (lists are pooled — cleared, not removed)
	private readonly Dictionary<(int, int), List<int>> _charPosIndex = new();
	private readonly List<List<int>> _listPool = new();
	private readonly List<int> _emptyCharList = new();

	// Pending particle draws for the additive blend layer
	private readonly List<(int grhIndex, int frame, Vector2 pos, Color color)> _pendingMapParticleDraws = new();
	private readonly List<(int grhIndex, int frame, Vector2 pos, Color color)> _pendingCharParticleDraws = new();

	// Pending aura draws for the aura additive layer (VB6: D3DBLEND_ONE/ONE)
	private readonly List<(int grhIndex, int frame, Vector2 pos, Color color, float angle)> _pendingAuraDraws = new();
	// Reflected auras — normal position + mirrorY, renderer does the Y-flip via DrawSetTransform
	private readonly List<(int grhIndex, int frame, Vector2 pos, Color color, float angle, float mirrorY)> _pendingReflAuraDraws = new();
	// Reflected body draws — queued in parent _Draw, drawn by ReflectionBodyLayer child
	private readonly List<(Character ch, Vector2 pos, Vector2 headOffset, int heading)> _pendingReflBodyDraws = new();

	// Pending roof tile draws (queued in _Draw, drawn by RoofLayer child node AFTER particles)
	private readonly List<(int grhIndex, Vector2 pos, Color modulate)> _pendingRoofDraws = new();

	// Pending dialog draws — queued during DrawContent, drawn by DialogOverlayLayer on top of everything
	// Each entry: (lines, textCenterX, baseY, fontSize, color)
	private readonly List<(string[] lines, int textCenterX, int baseY, int fontSize, Color color)> _pendingDialogDraws = new();

	// Whether any reflection was drawn this frame (used by PASS 1b mask)
	private bool _frameAnyReflection;

	// Dirty flag — set whenever map data, character state, or light state changes.
	// _Process only calls QueueRedraw when this is true, avoiding per-frame redraws
	// when nothing has changed (e.g., paused game, no movement, no animation tick).
	private bool _renderDirty = true;

	/// <summary>
	/// Mark the renderer as needing a redraw on the next _Process tick.
	/// Call this whenever map data, characters, lights, or animations change.
	/// </summary>
	public void MarkRenderDirty() => _renderDirty = true;

	/// <summary>
	/// Camera values for the current frame. Updated in _Process before any Draw calls.
	/// Other layers (FloatingTextLayer, SafeZoneBorderLayer) read this instead of recomputing.
	/// </summary>
	public static CameraSnapshot CurrentCamera { get; private set; }

	// Pre-computed water tile map — built on map load, avoids per-frame GRH range checks.
	// 1-indexed: _waterMap[x, y] = true if L1 GRH is in any known water range.
	private bool[,]? _waterMap;

	// Cached per-column and per-row screen coordinate arrays (Opt 4).
	// Resized only when the frame range changes. Avoids per-tile multiply in the hot path.
	private float[] _screenXCache = Array.Empty<float>();
	private float[] _screenYCache = Array.Empty<float>();



	// Per-frame camera data (computed in _Draw, used by child layer callbacks)
	private int _frameUserX, _frameUserY;
	private float _framePixelOffsetX, _framePixelOffsetY;
	private int _frameMinX, _frameMaxX, _frameMinY, _frameMaxY;       // terrain L1-L4 (large buffer)
	private int _frameL1MinX, _frameL1MaxX, _frameL1MinY, _frameL1MaxY; // L1 water (small buffer for perf)
	private int _frameCharMinX, _frameCharMaxX, _frameCharMinY, _frameCharMaxY; // characters (viewport only)
	private bool _frameHasLights;

	// GPU lightmap shader — samples a 101×101 texture for smooth per-vertex lighting.
	// Bilinear interpolation between corner pixels eliminates visible tile edges.
	private ShaderMaterial? _lightmapMaterial;
	private ImageTexture? _lightmapTexture;
	private bool _lightmapDirty;

	private const string LightmapShaderCode = @"
shader_type canvas_item;
uniform sampler2D lightmap : filter_linear, repeat_disable;
uniform vec2 world_origin;
uniform vec2 map_size_px;
uniform bool use_lightmap;
varying vec2 v_world_px;

void vertex() {
	v_world_px = VERTEX + world_origin;
}

void fragment() {
	if (use_lightmap) {
		vec2 uv = (v_world_px - 32.0) / map_size_px;
		vec3 light = texture(lightmap, uv).rgb;
		// Multiplicative darkening/brightening (day/evening/night ambient) PLUS
		// an additive glow measured against a FIXED floor (not the scene's own
		// ambient) — so a torch reads as a real light source with the same
		// visible punch at noon, dusk, or midnight, instead of only standing
		// out when the ambient happens to be dark.
		const vec3 glowFloor = vec3(0.35);
		vec3 boost = max(light - glowFloor, vec3(0.0));
		COLOR.rgb = COLOR.rgb * light + COLOR.rgb * boost * 1.4;
	}
}
";

	/// <summary>
	/// Called by Main after RecalculateLights() to trigger lightmap texture rebuild.
	/// </summary>
	public void MarkLightmapDirty() => _lightmapDirty = true;

	/// <summary>Rebuild the fog overlay texture — call when the fog intensity option changes.</summary>
	public void RebuildFogOverlay() => _fogOverlay?.RebuildFogTexture();

	/// <summary>
	/// Rebuild the pre-computed water tile map from current MapData.
	/// Called by Main after loading a new map.
	/// </summary>
	public void RebuildWaterMap()
	{
		if (_state?.MapData == null)
		{
			_waterMap = null;
			return;
		}
		int w = _state.MapData.Width;
		int h = _state.MapData.Height;
		_waterMap = new bool[w + 1, h + 1];
		for (int y = 1; y <= h; y++)
		{
			for (int x = 1; x <= w; x++)
			{
				int g = _state.MapData.Tiles[x, y].Layer1;
				_waterMap[x, y] = IsWaterGrh(g);
			}
		}
	}

	/// <summary>
	/// Pre-compute connected roof regions from Layer4 tiles.
	/// Uses flood-fill (BFS) to assign a regionId to each connected group of L4 tiles.
	/// Called once per map load. O(mapW * mapH).
	/// </summary>
	private static readonly (int dx, int dy)[] _floodDirs = { (1, 0), (-1, 0), (0, 1), (0, -1) };

	/// <summary>
	/// Pre-compute connected roof regions based on trigger tiles (1/2/4) AND Layer4 tiles.
	/// BFS flood-fill expands through any tile that has a roof trigger OR a roof graphic (L4).
	/// This ensures a house with triggers on interior tiles and L4 on border/exterior tiles
	/// all belong to the same region. Called once per map load. O(mapW * mapH).
	/// </summary>
	public void BuildRoofRegions()
	{
		if (_state?.MapData == null) { _roofRegionMap = null; return; }
		int w = _state.MapData.Width;
		int h = _state.MapData.Height;
		_roofRegionMap = new int[w + 1, h + 1]; // 1-based, all 0 by default
		_activeRoofRegion = 0;
		int nextRegion = 0;
		var queue = new Queue<(int x, int y)>();

		for (int y = 1; y <= h; y++)
		{
			for (int x = 1; x <= w; x++)
			{
				if (_roofRegionMap[x, y] != 0) continue;
				ref var tile = ref _state.MapData.Tiles[x, y];
				bool isTrigger = tile.Trigger == 1 || tile.Trigger == 2 || tile.Trigger == 4;
				bool hasL4 = tile.Layer4 > 0;
				if (!isTrigger && !hasL4) continue;

				// New region — BFS flood fill through triggers AND L4 tiles
				nextRegion++;
				queue.Enqueue((x, y));
				_roofRegionMap[x, y] = nextRegion;

				while (queue.Count > 0)
				{
					var (cx, cy) = queue.Dequeue();
					foreach (var (dx, dy) in _floodDirs)
					{
						int nx = cx + dx, ny = cy + dy;
						if (nx < 1 || nx > w || ny < 1 || ny > h) continue;
						if (_roofRegionMap[nx, ny] != 0) continue;
						ref var nt = ref _state.MapData.Tiles[nx, ny];
						bool nTrigger = nt.Trigger == 1 || nt.Trigger == 2 || nt.Trigger == 4;
						bool nL4 = nt.Layer4 > 0;
						if (!nTrigger && !nL4) continue;
						_roofRegionMap[nx, ny] = nextRegion;
						queue.Enqueue((nx, ny));
					}
				}
			}
		}
	}

	public void Init(GameState state, GameData data, GrhAnimator animator, IResourceProvider? resources = null)
	{
		_state = state;
		_data = data;
		_animator = animator;
		_resources = resources;

		// Create lightmap shader + material (shared across terrain layers)
		var shader = new Shader();
		shader.Code = LightmapShaderCode;
		_lightmapMaterial = new ShaderMaterial();
		_lightmapMaterial.Shader = shader;
		// Initial lightmap size — will be rebuilt when map loads
		_lightmapTexture = ImageTexture.CreateFromImage(
			Image.CreateEmpty(101, 101, false, Image.Format.Rgb8));
		_lightmapMaterial.SetShaderParameter("lightmap", _lightmapTexture);
		_lightmapMaterial.SetShaderParameter("use_lightmap", false);
		_lightmapMaterial.SetShaderParameter("map_size_px", new Vector2(3200f, 3200f));

		// Apply lightmap shader to terrain layers (self, mask, L2, roof)
		Material = _lightmapMaterial;

		var additiveMat = new CanvasItemMaterial
		{
			BlendMode = CanvasItemMaterial.BlendModeEnum.Add
		};

		// Reflected aura layer: additive blend, z=-3 (draws after parent _Draw,
		// before mask and L2 — so mask covers land overflow, L2 covers border overlap)
		_reflAuraLayer = new ReflectedAuraLayer();
		_reflAuraLayer.Name = "ReflectedAuraLayer";
		_reflAuraLayer.Material = additiveMat;
		_reflAuraLayer.ZIndex = 0;
		_reflAuraLayer.SetRenderer(this);
		AddChild(_reflAuraLayer);

		// Reflection body layer: draws character body/FX reflections AFTER auras
		_reflBodyLayer = new ReflectionBodyLayer();
		_reflBodyLayer.Name = "ReflectionBodyLayer";
		_reflBodyLayer.ZIndex = 0;
		_reflBodyLayer.SetRenderer(this);
		AddChild(_reflBodyLayer);

		// Non-water mask layer: standard blend (redraws non-water L1 tiles
		// to cover body reflection + reflected aura overflow onto land)
		_maskLayer = new NonWaterMaskLayer();
		_maskLayer.Name = "NonWaterMaskLayer";
		_maskLayer.Material = _lightmapMaterial;
		_maskLayer.ZIndex = 0;
		_maskLayer.SetRenderer(this);
		AddChild(_maskLayer);

		// Layer 2 layer: standard blend, z=-1 (draws L2 borders/objects on top,
		// occluding reflected auras under border opaque portions)
		_layer2Layer = new Layer2Layer();
		_layer2Layer.Name = "Layer2Layer";
		_layer2Layer.Material = _lightmapMaterial;
		_layer2Layer.ZIndex = 0;
		_layer2Layer.SetRenderer(this);
		AddChild(_layer2Layer);

		// Aura layer: additive blend, z=0 (normal auras only)
		// Added BEFORE ContentLayer — same z draws in tree order,
		// so auras render AFTER L2 but BEFORE characters.
		_auraLayer = new AuraAdditiveLayer();
		_auraLayer.Name = "AuraLayer";
		_auraLayer.Material = additiveMat;
		_auraLayer.ZIndex = 0;
		_auraLayer.SetRenderer(this);
		AddChild(_auraLayer);

		_contentLayer = new ContentLayer();
		_contentLayer.Name = "ContentLayer";
		_contentLayer.ZIndex = 0;
		_contentLayer.SetRenderer(this);
		AddChild(_contentLayer);

		// Dialog overlay: z=1 — above characters/NPCs, below particles/roof
		_dialogLayer = new DialogOverlayLayer();
		_dialogLayer.Name = "DialogOverlay";
		_dialogLayer.ZIndex = 1;
		_dialogLayer.SetRenderer(this);
		AddChild(_dialogLayer);

		// Particle layer: additive blend, z=2
		_additiveLayer = new AdditiveParticleLayer();
		_additiveLayer.Name = "AdditiveParticles";
		_additiveLayer.Material = additiveMat;
		_additiveLayer.ZIndex = 2;
		_additiveLayer.SetRenderer(this);
		AddChild(_additiveLayer);

		// Roof layer: standard blend, z=3 (on top of everything)
		_roofLayer = new RoofLayer();
		_roofLayer.Name = "RoofLayer";
		_roofLayer.Material = _lightmapMaterial;
		_roofLayer.ZIndex = 3;
		_roofLayer.SetRenderer(this);
		AddChild(_roofLayer);

		// Floating text layer: z=4 (above roof, below weather)
		_floatingTextLayer = new FloatingTextLayer();
		_floatingTextLayer.Name = "FloatingTextLayer";
		_floatingTextLayer.ZIndex = 4;
		_floatingTextLayer.Init(this, state);
		AddChild(_floatingTextLayer);

		// Weather layer: z=5 (topmost overlay — rain, lightning)
		_weatherRenderer = new WeatherRenderer();
		_weatherRenderer.Name = "WeatherRenderer";
		_weatherRenderer.ZIndex = 5;
		AddChild(_weatherRenderer);

		// Safe zone border: z=6 (red fog at safe zone edges to warn players)
		var safeZoneBorder = new SafeZoneBorderLayer(_state);
		safeZoneBorder.Name = "SafeZoneBorder";
		safeZoneBorder.ZIndex = 6;
		AddChild(safeZoneBorder);

		// Fog overlay: z=7 (fogs extended viewport edges beyond core 17x13)
		var fogOverlay = new FogOverlayLayer();
		fogOverlay.Name = "FogOverlay";
		fogOverlay.ZIndex = 7;
		fogOverlay.Init(_state!);
		AddChild(fogOverlay);
		_fogOverlay = fogOverlay;
	}

	/// <summary>
	/// Initialize weather renderer with sound manager (called from Main after Init).
	/// </summary>
	public void InitWeather(SoundManager? soundManager, IResourceProvider? resources = null)
	{
		_weatherRenderer?.Init(_state!, soundManager, resources);
	}

	/// <summary>
	/// Access the floating text layer to spawn damage/heal numbers.
	/// </summary>
	public FloatingTextLayer? FloatingText => _floatingTextLayer;

	public override void _Process(double delta)
	{
		if (_state?.MapData == null) return;
		_deltaMs = (float)delta * 1000f;
		UpdateRoofFade();
		UpdateStatusTimers();
		UpdateAllCharacterTimers();
		// Auto-mark dirty while a map is loaded and the game is not paused.
		// GRH tile animations advance every frame so a redraw is always needed
		// during active gameplay. When the game is paused, callers should avoid
		// calling MarkRenderDirty() so the flag stays false and no redraw happens.
		if (!(_state.Paused))
			_renderDirty = true;
		if (_renderDirty)
		{
			_renderDirty = false;
			QueueRedraw();
		}
	}

	/// <summary>
	/// Advance status timers once per frame. Must run in _Process, not _Draw,
	/// because _Draw can be called multiple times per frame.
	/// </summary>
	private void UpdateStatusTimers()
	{
		if (_state == null) return;

		// Paralysis countdown
		if (_state.UserParalyzed && _state.ParalysisTimer > 0)
		{
			_state.ParalysisTimer -= _deltaMs / 1000f;
			if (_state.ParalysisTimer < 0) _state.ParalysisTimer = 0;
		}

		// Invisibility countdown (self character)
		if (_state.Characters.TryGetValue(_state.UserCharIndex, out var selfCh) && selfCh.Invisible)
		{
			if (selfCh.InvisibleCountdown > 0)
			{
				selfCh.InvisibleCountdownTimer += _deltaMs;
				if (selfCh.InvisibleCountdownTimer >= 1000f)
				{
					selfCh.InvisibleCountdownTimer -= 1000f;
					selfCh.InvisibleCountdown--;
				}
			}
		}
	}

	/// <summary>
	/// Advance per-character timers (FOV fade, transparency, FX, dialog) for all characters.
	/// Must run in _Process, once per frame, before any draw calls.
	/// </summary>
	private void UpdateAllCharacterTimers()
	{
		if (_state == null || _data == null) return;
		int ux = _state.UserPosX;
		int uy = _state.UserPosY;
		int halfX = HalfWindowTileWidth + 3; // viewport + small buffer
		int halfY = HalfWindowTileHeight + 3;
		foreach (var ch in _state.Characters.Values)
		{
			// Full update for characters near viewport (visible or about to be)
			// Lightweight FOV-only update for distant characters
			bool nearViewport = Math.Abs(ch.PosX - ux) <= halfX && Math.Abs(ch.PosY - uy) <= halfY;
			if (nearViewport)
				CharRenderer.UpdateCharacterTimers(ch, _deltaMs, _state, _data);
			else
				CharRenderer.UpdateCharacterFovOnly(ch, _deltaMs, _state);
		}
	}

	/// <summary>
	/// VB6: all tiles/characters are drawn with the map's ambient RGB.
	/// Default is 200,200,200 (slightly dimmed). Applied via Godot's Modulate.
	/// </summary>
	private void UpdateAmbientLight()
	{
		if (!(_state!.Config?.ShowDayNight ?? true))
		{
			Modulate = Colors.White;
			return;
		}
		float r = _state.MapColorR / 255f;
		float g = _state.MapColorG / 255f;
		float b = _state.MapColorB / 255f;
		Modulate = new Color(r, g, b, 1f);
	}

	private void UpdateRoofFade()
	{
		if (_state?.MapData == null) return;

		int ux = _state.UserPosX;
		int uy = _state.UserPosY;
		int w = _state.MapData.Width;
		int h = _state.MapData.Height;
		if (ux < 1 || ux > w || uy < 1 || uy > h) return;

		// Determine which roof region the player is inside (0 = none)
		if (_roofRegionMap != null)
			_activeRoofRegion = _roofRegionMap[ux, uy];
		else
			_activeRoofRegion = 0;

		// Track which region to apply fade to
		if (_activeRoofRegion > 0)
		{
			if (_fadingRoofRegion != _activeRoofRegion)
			{
				// Entered a new/different region — start fade-out from full opacity
				_fadingRoofRegion = _activeRoofRegion;
				_roofAlpha = 255f;
			}
		}
		// When outside: keep _fadingRoofRegion so the fade-in transition applies to it

		// Fade control: delta-time based (frame-rate independent)
		float fadeDelta = RoofFadeSpeed * (_deltaMs / 1000f);
		if (_activeRoofRegion > 0)
		{
			// Inside a roof region → fade out to min alpha
			_roofAlpha -= fadeDelta;
			if (_roofAlpha < RoofMinAlpha) _roofAlpha = RoofMinAlpha;
		}
		else if (_fadingRoofRegion > 0)
		{
			// Outside but a region is still fading back in
			_roofAlpha += fadeDelta;
			if (_roofAlpha >= 255f)
			{
				_roofAlpha = 255f;
				_fadingRoofRegion = 0; // fade-in complete, clear
			}
		}
	}

	private void BuildCharPositionIndex()
	{
		// Return all lists to pool instead of discarding them
		foreach (var kvp in _charPosIndex)
		{
			kvp.Value.Clear();
			_listPool.Add(kvp.Value);
		}
		_charPosIndex.Clear();

		foreach (var kvp in _state!.Characters)
		{
			var ch = kvp.Value;
			var key = (ch.PosX, ch.PosY);
			if (!_charPosIndex.TryGetValue(key, out var list))
			{
				// Reuse pooled list or allocate new one
				if (_listPool.Count > 0)
				{
					list = _listPool[_listPool.Count - 1];
					_listPool.RemoveAt(_listPool.Count - 1);
				}
				else
				{
					list = new List<int>(2);
				}
				_charPosIndex[key] = list;
			}
			list.Add(kvp.Key);
		}
	}

	private List<int> GetCharsAt(int x, int y)
	{
		return _charPosIndex.TryGetValue((x, y), out var list) ? list : _emptyCharList;
	}

	/// <summary>
	/// Convert world tile to screen pixel position.
	/// </summary>
	private Vector2 TileToScreen(int tileX, int tileY, int userX, int userY,
										 float pixelOffsetX, float pixelOffsetY)
	{
		float px = (tileX - userX + HalfWindowTileWidth) * TileSize + pixelOffsetX;
		float py = (tileY - userY + HalfWindowTileHeight) * TileSize + pixelOffsetY;
		return new Vector2(px, py);
	}

	private void ApplyCameraClamp(int rawUserX, int rawUserY, float rawPixelOffsetX, float rawPixelOffsetY,
								  int mapW, int mapH)
	{
		float originX = (rawUserX - HalfWindowTileWidth) * TileSize - rawPixelOffsetX;
		float originY = (rawUserY - HalfWindowTileHeight) * TileSize - rawPixelOffsetY;

		float maxOriginX = Math.Max(0, mapW * TileSize - ViewportWidth);
		float maxOriginY = Math.Max(0, mapH * TileSize - ViewportHeight);
		originX = Math.Clamp(originX, 0, maxOriginX);
		originY = Math.Clamp(originY, 0, maxOriginY);

		_frameUserX = (int)Math.Floor(originX / TileSize) + HalfWindowTileWidth;
		_frameUserY = (int)Math.Floor(originY / TileSize) + HalfWindowTileHeight;
		_framePixelOffsetX = (_frameUserX - HalfWindowTileWidth) * TileSize - originX;
		_framePixelOffsetY = (_frameUserY - HalfWindowTileHeight) * TileSize - originY;
	}

	private MapData? LoadCachedMap(int mapNumber)
	{
		if (mapNumber <= 0 || _resources == null) return null;
		if (_state != null && mapNumber == _state.CurrentMap) return _state.MapData;
		if (_mapCache.TryGetValue(mapNumber, out var cached)) return cached;
		try
		{
			var loaded = MapLoader.Load(_resources, mapNumber);
			_mapCache[mapNumber] = loaded;
			return loaded;
		}
		catch
		{
			return null;
		}
	}

	private bool TryResolveTile(int x, int y, out MapTile tile)
	{
		tile = default;
		if (_state?.MapData == null) return false;

		var current = _state.MapData;
		if (IsClassicEdgeBand(x, y, current) && TryResolveAdjacentTile(x, y, current, out tile))
			return true;

		if (x >= 1 && x <= current.Width && y >= 1 && y <= current.Height)
		{
			tile = current.Tiles[x, y];
			return true;
		}

		if (TryResolveAdjacentTile(x, y, current, out tile))
			return true;

		return false;
	}

	private bool TryResolveAdjacentTile(int x, int y, MapData current, out MapTile tile)
	{
		tile = default;
		int side = GetClassicEdgeSide(x, y, current);
		if (side == 0) return false;

		if (_state == null) return false;
		var cache = GetAdjacentCache(_state.CurrentMap, current);
		int coord = side is 1 or 3 ? Math.Clamp(x, 1, current.Width) : Math.Clamp(y, 1, current.Height);
		var exit = cache.Get(side, coord);
		if (exit.DestMap <= 0) return false;

		var neighbor = LoadCachedMap(exit.DestMap);
		if (neighbor == null) return false;

		int nx = exit.DestX + (x - exit.SrcX);
		int ny = exit.DestY + (y - exit.SrcY);
		if (nx < 1 || nx > neighbor.Width || ny < 1 || ny > neighbor.Height)
			return false;

		tile = neighbor.Tiles[nx, ny];
		return true;
	}

	private static bool IsClassicEdgeBand(int x, int y, MapData map)
	{
		return GetClassicEdgeSide(x, y, map) != 0;
	}

	private static int GetClassicEdgeSide(int x, int y, MapData map)
	{
		if (y < 7) return 1;
		if (y > map.Height - 6) return 3;
		if (x > map.Width - 8) return 2;
		if (x < 9) return 4;
		return 0;
	}

	private AdjacentMapCache GetAdjacentCache(int mapNumber, MapData current)
	{
		if (_adjacentCache.TryGetValue(mapNumber, out var cache)) return cache;
		cache = AdjacentMapCache.Build(current);
		_adjacentCache[mapNumber] = cache;
		return cache;
	}



	/// <summary>
	/// Main _Draw: renders PASS 1 (L1 ground) + PASS 1.5 (body reflections).
	/// PASS 1b (mask), L2, and auras are handled by child layers drawn after this.
	/// </summary>
	public override void _Draw()
	{
		if (_state == null || _data == null || _animator == null) return;
		if (_state.MapData == null) return;

		// Clear pending draws from previous frame
		_pendingMapParticleDraws.Clear();
		_pendingCharParticleDraws.Clear();
		_pendingAuraDraws.Clear();
		_pendingReflAuraDraws.Clear();
		_pendingReflBodyDraws.Clear();
		_pendingDialogDraws.Clear();
		_pendingRoofDraws.Clear();

		int mapW = _state.MapData.Width;
		int mapH = _state.MapData.Height;

		// VB6 ShowNextFrame: render center = UserPos - AddtoUserPos, offset = OffsetCounter.
		_frameUserX = _state.UserPosX - _state.AddToUserPosX;
		_frameUserY = _state.UserPosY - _state.AddToUserPosY;

		BuildCharPositionIndex();

		// Camera pixel offset — NEGATED because ScreenOffset grows in the movement
		// direction, but tiles must shift in the OPPOSITE direction on screen.
		_framePixelOffsetX = (float)Math.Round(-_state.ScreenOffsetX);
		_framePixelOffsetY = (float)Math.Round(-_state.ScreenOffsetY);

		CurrentCamera = new CameraSnapshot(_frameUserX, _frameUserY, _framePixelOffsetX, _framePixelOffsetY);

		// Visible tile range
		int screenMinX = _frameUserX - HalfWindowTileWidth;
		int screenMaxX = _frameUserX + HalfWindowTileWidth;
		int screenMinY = _frameUserY - HalfWindowTileHeight;
		int screenMaxY = _frameUserY + HalfWindowTileHeight;

		// L2/L3 terrain bounds (large buffer for 512px sprites)
		_frameMinX = screenMinX - TerrainBufferSize;
		_frameMaxX = screenMaxX + TerrainBufferSize;
		_frameMinY = screenMinY - TerrainBufferSize;
		_frameMaxY = screenMaxY + TerrainBufferSize;

		// L1 water bounds (+3 to account for pixel offset during smooth scroll)
		_frameL1MinX = screenMinX - 3;
		_frameL1MaxX = screenMaxX + 3;
		_frameL1MinY = screenMinY - 3;
		_frameL1MaxY = screenMaxY + 3;

		// Character bounds (viewport only + 1 tile for smooth edge)
		_frameCharMinX = Math.Max(1, screenMinX - CharBufferSize);
		_frameCharMaxX = Math.Min(mapW, screenMaxX + CharBufferSize);
		_frameCharMinY = Math.Max(1, screenMinY - CharBufferSize);
		_frameCharMaxY = Math.Min(mapH, screenMaxY + CharBufferSize);

		// Opt 4: Pre-compute per-column X and per-row Y screen coords for the terrain buffer range.
		// These are reused by DrawContent, DrawNonWaterMask, DrawLayer2, and the roof loop.
		{
			int colCount = _frameMaxX - _frameMinX + 1;
			int rowCount = _frameMaxY - _frameMinY + 1;
			if (_screenXCache.Length < colCount) _screenXCache = new float[colCount];
			if (_screenYCache.Length < rowCount) _screenYCache = new float[rowCount];
			for (int xi = 0; xi < colCount; xi++)
				_screenXCache[xi] = (_frameMinX + xi - _frameUserX + HalfWindowTileWidth) * TileSize + _framePixelOffsetX;
			for (int yi = 0; yi < rowCount; yi++)
				_screenYCache[yi] = (_frameMinY + yi - _frameUserY + HalfWindowTileHeight) * TileSize + _framePixelOffsetY;
		}

		_frameHasLights = (_state.Config?.ShowLights ?? true)
						  && _state.MapLights.Count > 0 && _state.TileLightColors != null;

		// GPU lightmap: when lights active, the shader handles ambient + light tinting
		// on terrain layers. Modulate = white so shader is the sole color source.
		// ContentLayer gets map ambient via its own Modulate (characters stay tinted).
		if (_frameHasLights)
		{
			if (_lightmapDirty && _state.TileLightColors != null)
			{
				var img = LightSystem.BuildLightmapImage(_state.TileLightColors, mapW, mapH);
				// Recreate texture if dimensions changed, otherwise update in-place
				if (_lightmapTexture == null
					|| _lightmapTexture.GetWidth() != img.GetWidth()
					|| _lightmapTexture.GetHeight() != img.GetHeight())
				{
					_lightmapTexture = ImageTexture.CreateFromImage(img);
					_lightmapMaterial?.SetShaderParameter("lightmap", _lightmapTexture);
				}
				else
				{
					_lightmapTexture.Update(img);
				}
				_lightmapDirty = false;
			}
			_lightmapMaterial?.SetShaderParameter("use_lightmap", true);
			_lightmapMaterial?.SetShaderParameter("map_size_px", new Vector2(mapW * TileSize, mapH * TileSize));
			float originX = (_frameUserX - HalfWindowTileWidth) * TileSize - _framePixelOffsetX;
			float originY = (_frameUserY - HalfWindowTileHeight) * TileSize - _framePixelOffsetY;
			_lightmapMaterial?.SetShaderParameter("world_origin", new Vector2(originX, originY));
			Modulate = Colors.White;
			// Parent is white → ContentLayer inherits white → full brightness. No correction needed.
			if (_contentLayer != null) _contentLayer.SelfModulate = Colors.White;
		}
		else
		{
			_lightmapMaterial?.SetShaderParameter("use_lightmap", false);
			UpdateAmbientLight();
			// Parent Modulate = map ambient → ContentLayer inherits dark tint.
			// Cancel it with inverse SelfModulate so characters draw at full brightness.
			if (_contentLayer != null)
			{
				float r = _state.MapColorR / 255f;
				float g = _state.MapColorG / 255f;
				float b = _state.MapColorB / 255f;
				_contentLayer.SelfModulate = new Color(
					r > 0.01f ? 1f / r : 1f,
					g > 0.01f ? 1f / g : 1f,
					b > 0.01f ? 1f / b : 1f, 1f);
			}
		}

		// ==========================================
		// PASS 1: Layer 1 — ONLY water tiles. Non-water tiles are drawn once
		// by NonWaterMaskLayer (PASS 1b), avoiding the double-draw that killed FPS.
		// ==========================================
		for (int y = _frameL1MinY; y <= _frameL1MaxY; y++)
		{
			for (int x = _frameL1MinX; x <= _frameL1MaxX; x++)
			{
				if (!TryResolveTile(x, y, out var tile)) continue;
				if (!IsWaterGrh(tile.Layer1)) continue; // only water

				Vector2 pos = TileToScreen(x, y, _frameUserX, _frameUserY, _framePixelOffsetX, _framePixelOffsetY);
				DrawTileGrh(tile.Layer1, pos, center: false);
			}
		}



		// ==========================================
		// PASS 1.5: Character reflections on water
		// Drawn after Layer 1 so they appear on water tiles.
		// Reflected auras are collected here and drawn by ReflectedAuraLayer child.
		// ==========================================
		bool showReflections = _state.Config?.ShowReflections ?? true;
		_frameAnyReflection = false;

		// Opt 2 & 3: viewport bounds used for culling both reflections and aura collection.
		int reflMinX = _frameUserX - HalfWindowTileWidth - 2;
		int reflMaxX = _frameUserX + HalfWindowTileWidth + 2;
		int reflMinY = _frameUserY - HalfWindowTileHeight - 2;
		int reflMaxY = _frameUserY + HalfWindowTileHeight + 2;

		if (showReflections)
		{
			foreach (var kvp in _state.Characters)
			{
				var ch = kvp.Value;
				if (ch.Invisible) continue;

				// Skip characters outside viewport + small buffer — no visual impact
				if (ch.PosX < reflMinX || ch.PosX > reflMaxX ||
					ch.PosY < reflMinY || ch.PosY > reflMaxY)
					continue;

				// Draw reflection if ANY tile below (Y+1..Y+3) and within sprite width
				// has water. Uses pre-computed _waterMap for O(1) lookups instead of
				// per-frame GRH range checks. Checking 3 rows allows the reflection
				// to smoothly fade out as the character walks away from water.
				// NonWaterMaskLayer ensures reflections only show on water tiles.
				if (ch.PosY < 1 || ch.PosY > mapH - 3) continue;
				if (_waterMap == null) continue;
				bool hasNearbyWater = false;
				int checkRangeX = ch.Mounted ? 3 : 2;
				for (int cy = ch.PosY + 1; cy <= Math.Min(mapH, ch.PosY + 5) && !hasNearbyWater; cy++)
				{
					for (int cx = Math.Max(1, ch.PosX - checkRangeX);
						 cx <= Math.Min(mapW, ch.PosX + checkRangeX) && !hasNearbyWater; cx++)
					{
						if (_waterMap[cx, cy])
							hasNearbyWater = true;
					}
				}
				if (!hasNearbyWater) continue;

				var tilePos = TileToScreen(ch.PosX, ch.PosY, _frameUserX, _frameUserY,
											_framePixelOffsetX, _framePixelOffsetY);
				float charPx = tilePos.X + (float)Math.Round(ch.MoveOffsetX);
				float charPy = tilePos.Y + (float)Math.Round(ch.MoveOffsetY);

				int heading = ch.Heading;
				if (heading < 1 || heading > 4) heading = 3;

				Vector2 headOffset = new Vector2(0, -30);
				if (ch.Body > 0 && ch.Body < _data.Bodies.Length)
				{
					var body = _data.Bodies[ch.Body];
					headOffset = new Vector2(body.HeadOffsetX, body.HeadOffsetY);
				}

				// Queue body reflection for ReflectionBodyLayer (draws AFTER reflected auras)
				_pendingReflBodyDraws.Add((ch, new Vector2(charPx, charPy), headOffset, heading));

				// Collect reflected auras (drawn by ReflectedAuraLayer BEFORE body)
				if ((_state.Config?.ShowAuras ?? true)
					&& !ch.Navigating && !ch.Mounted
					&& (!ch.Invisible || kvp.Key == _state.UserCharIndex))
				{
					CharRenderer.CollectReflAuraDraws(this, ch,
						new Vector2(charPx, charPy), headOffset, _data, _animator!.GlobalTimeMs);
				}

				_frameAnyReflection = true;
			}
		}

		// PASS 1b (mask) and PASS 2 (L2) are now drawn by child layers
		// (NonWaterMaskLayer and Layer2Layer) which draw AFTER this _Draw().

		// Pre-compute PASS 3 aura data before AuraLayer draws, matching the original flow.
		foreach (var kvp in _state.Characters)
		{
			var ch = kvp.Value;
			if (ch.Invisible && kvp.Key != _state.UserCharIndex) continue;
			if (ch.Navigating || ch.Mounted) continue;
			if (ch.PosX < reflMinX || ch.PosX > reflMaxX ||
				ch.PosY < reflMinY || ch.PosY > reflMaxY)
				continue;

			var tilePos = TileToScreen(ch.PosX, ch.PosY, _frameUserX, _frameUserY,
										_framePixelOffsetX, _framePixelOffsetY);
			float charPx = tilePos.X + (float)Math.Round(ch.MoveOffsetX);
			float charPy = tilePos.Y + (float)Math.Round(ch.MoveOffsetY);

			Vector2 headOffset = new Vector2(0, -30);
			if (ch.Body > 0 && ch.Body < _data.Bodies.Length)
			{
				var body = _data.Bodies[ch.Body];
				headOffset = new Vector2(body.HeadOffsetX, body.HeadOffsetY);
			}

			if (_state.Config?.ShowAuras ?? true)
			{
				float auraAlpha = ch.Invisible ? (ch.TransparenciaBody + 45f) / 255f : 1f;
				CharRenderer.CollectAuraDraws(this, ch, new Vector2(charPx, charPy), headOffset,
					_data, _animator!.GlobalTimeMs, auraAlpha);
			}
		}

		// Collect map particle draws for the additive layer (respects Config.ShowParticles)
		if (_state.Config?.ShowParticles ?? true)
		{
			foreach (var stream in _state.MapParticles)
			{
				if (!stream.Active || stream.CharIndex >= 0) continue;
				if (stream.DefIndex < 1 || stream.DefIndex >= _state.ParticleDefs.Length) continue;

				Vector2 streamPos = TileToScreen(stream.MapX, stream.MapY, _frameUserX, _frameUserY,
												  _framePixelOffsetX, _framePixelOffsetY);

				foreach (var p in stream.Particles)
				{
					if (!p.Alive || p.GrhIndex <= 0) continue;
					var color = new Color(ByteToFloat.Table[p.ColR], ByteToFloat.Table[p.ColG], ByteToFloat.Table[p.ColB], p.Alpha);
					Vector2 pPos = streamPos + new Vector2(p.X, p.Y);
					int frame = _animator.GetCurrentFrame(p.GrhIndex, _data);
					_pendingMapParticleDraws.Add((p.GrhIndex, frame, pPos, color));
				}
			}
		}

		// Collect roof draws — per-region fade using _fadingRoofRegion (persists during fade-out AND fade-in)
		{
			float fadeA = _roofAlpha / 255f;
			Color fadingRoofColor = new Color(1, 1, 1, fadeA); // computed once per frame, reused per fading tile

			for (int y = _frameMinY; y <= _frameMaxY; y++)
			{
				float sy = _screenYCache[y - _frameMinY];
				for (int x = _frameMinX; x <= _frameMaxX; x++)
				{
					if (!TryResolveTile(x, y, out var tile)) continue;
					if (tile.Layer4 <= 0) continue;

					// Determine color: tiles in the fading region get fade, others stay opaque
					Color roofColor;
					if (_fadingRoofRegion > 0 && _roofRegionMap != null
						&& x >= 1 && x <= mapW && y >= 1 && y <= mapH
						&& _roofRegionMap[x, y] == _fadingRoofRegion)
					{
						if (fadeA <= 0f) continue; // fully hidden, skip draw
						roofColor = fadingRoofColor;
					}
					else
					{
						roofColor = Colors.White; // other roofs always visible
					}

					// Opt 4: use pre-computed screen coords
					Vector2 pos = new Vector2(_screenXCache[x - _frameMinX], sy);
					_pendingRoofDraws.Add((tile.Layer4, pos, roofColor));
				}
			}
		}

		// Trigger child layer redraws
		_reflAuraLayer?.QueueRedraw();
		_reflBodyLayer?.QueueRedraw();
		_maskLayer?.QueueRedraw();
		_layer2Layer?.QueueRedraw();
		_auraLayer?.QueueRedraw();
		_contentLayer?.QueueRedraw();
		_dialogLayer?.QueueRedraw();
		_additiveLayer?.QueueRedraw();
		_roofLayer?.QueueRedraw();
	}

	/// <summary>
	/// Draw a tile GRH on this WorldRenderer's canvas (for terrain passes).
	/// </summary>
	private void DrawTileGrh(int grhIndex, Vector2 pos, bool center = false, Color? modulate = null)
	{
		if (_data == null || _animator == null) return;
		if (grhIndex <= 0 || grhIndex >= _data.Grhs.Length) return;

		int frame = _animator.GetCurrentFrame(grhIndex, _data);
		CharRenderer.DrawGrh(this, _data, grhIndex, frame, pos, center, modulate);
	}

	/// <summary>
	/// Draw a tile GRH on a specific canvas (for content/roof child layers).
	/// </summary>
	private void DrawTileGrhTo(CanvasItem canvas, int grhIndex, Vector2 pos, bool center = false, Color? modulate = null)
	{
		if (_data == null || _animator == null) return;
		if (grhIndex <= 0 || grhIndex >= _data.Grhs.Length) return;

		int frame = _animator.GetCurrentFrame(grhIndex, _data);
		CharRenderer.DrawGrh(canvas, _data, grhIndex, frame, pos, center, modulate);
	}

	/// <summary>
	/// Check if a GRH index is a water graphic (any of the known water tile ranges).
	/// </summary>
	public static bool IsWaterGrh(int g)
	{
		return (g >= 1505  && g <= 1520)   // (Animación)(AGUA) — 4×4
			|| (g >= 5665  && g <= 5680)   // Agua Clarita — 4×4
			|| (g >= 13547 && g <= 13562)  // classic variant — 4×4
			|| (g >= 28268 && g <= 28283)  // Agua verde — 4×4
			|| (g >= 30762 && g <= 30777)  // Agua azul — 4×4
			|| (g >= 32498 && g <= 32513)  // Agua celeste — 4×4
			|| (g >= 44520 && g <= 44711)  // Agua v2 — 16×12
			|| (g >= 53678 && g <= 53869); // Agua v3 — 16×12
	}

	/// <summary>
	/// VB6 HayAgua(): checks if a tile is water (known water GRH ranges and Layer2 == 0).
	/// </summary>
	public static bool IsWater(MapData? mapData, int x, int y)
	{
		if (mapData == null || x < 1 || x > mapData.Width || y < 1 || y > mapData.Height) return false;
		ref var tile = ref mapData.Tiles[x, y];
		return IsWaterGrh(tile.Layer1);
	}

	/// <summary>
	/// VB6 EsArbol(): checks if a GRH index is a tree graphic.
	/// </summary>
	private static bool IsTree(int grhIndex)
	{
		return grhIndex == 7222 || grhIndex == 7223 || grhIndex == 7224 ||
			   grhIndex == 7225 || grhIndex == 7226 ||
			   grhIndex == 7000 || grhIndex == 7001 || grhIndex == 7002 ||
			   grhIndex == 22077 || grhIndex == 22078 || grhIndex == 22079 ||
			   grhIndex == 22080 || grhIndex == 22081 || grhIndex == 22082 ||
			   grhIndex == 22083 || grhIndex == 22084 || grhIndex == 22085 ||
			   grhIndex == 22086 ||
			   grhIndex == 8489 || grhIndex == 8483;
	}

}

/// <summary>
/// Camera values snapshot for the current frame.
/// Computed once in WorldRenderer._Process and shared with other rendering layers.
/// </summary>
public readonly struct CameraSnapshot
{
	public readonly float UserX;
	public readonly float UserY;
	public readonly float PixelOffsetX;
	public readonly float PixelOffsetY;

	public CameraSnapshot(float userX, float userY, float pixelOffsetX, float pixelOffsetY)
	{
		UserX = userX; UserY = userY;
		PixelOffsetX = pixelOffsetX; PixelOffsetY = pixelOffsetY;
	}
}

internal readonly struct AdjacentExit
{
	public readonly int SrcX, SrcY, DestMap, DestX, DestY;

	public AdjacentExit(int srcX, int srcY, int destMap, int destX, int destY)
	{
		SrcX = srcX;
		SrcY = srcY;
		DestMap = destMap;
		DestX = destX;
		DestY = destY;
	}
}

internal sealed class AdjacentMapCache
{
	private readonly AdjacentExit[] _north;
	private readonly AdjacentExit[] _east;
	private readonly AdjacentExit[] _south;
	private readonly AdjacentExit[] _west;

	private AdjacentMapCache(int width, int height)
	{
		_north = new AdjacentExit[width + 1];
		_south = new AdjacentExit[width + 1];
		_east = new AdjacentExit[height + 1];
		_west = new AdjacentExit[height + 1];
	}

	public AdjacentExit Get(int side, int coord) => side switch
	{
		1 => coord >= 1 && coord < _north.Length ? _north[coord] : default,
		2 => coord >= 1 && coord < _east.Length ? _east[coord] : default,
		3 => coord >= 1 && coord < _south.Length ? _south[coord] : default,
		4 => coord >= 1 && coord < _west.Length ? _west[coord] : default,
		_ => default
	};

	public static AdjacentMapCache Build(MapData map)
	{
		var cache = new AdjacentMapCache(map.Width, map.Height);
		FillHorizontal(map, cache._north, north: true);
		FillHorizontal(map, cache._south, north: false);
		FillVertical(map, cache._west, west: true);
		FillVertical(map, cache._east, west: false);
		return cache;
	}

	private static void FillHorizontal(MapData map, AdjacentExit[] target, bool north)
	{
		int edgeLimit = 10;
		for (int coord = 1; coord <= map.Width; coord++)
		{
			int bestDistance = int.MaxValue;
			AdjacentExit best = default;
			int yStart = north ? 1 : Math.Max(1, map.Height - edgeLimit + 1);
			int yEnd = north ? Math.Min(edgeLimit, map.Height) : map.Height;
			for (int y = yStart; y <= yEnd; y++)
			{
				for (int x = 1; x <= map.Width; x++)
				{
					var tile = map.Tiles[x, y];
					if (tile.ExitMap <= 0) continue;
					int distance = Math.Abs(x - coord);
					if (distance >= bestDistance) continue;
					bestDistance = distance;
					best = new AdjacentExit(x, y, tile.ExitMap, tile.ExitX, tile.ExitY);
				}
			}
			target[coord] = best;
		}
	}

	private static void FillVertical(MapData map, AdjacentExit[] target, bool west)
	{
		int edgeLimit = 10;
		for (int coord = 1; coord <= map.Height; coord++)
		{
			int bestDistance = int.MaxValue;
			AdjacentExit best = default;
			int xStart = west ? 1 : Math.Max(1, map.Width - edgeLimit + 1);
			int xEnd = west ? Math.Min(edgeLimit, map.Width) : map.Width;
			for (int y = 1; y <= map.Height; y++)
			{
				for (int x = xStart; x <= xEnd; x++)
				{
					var tile = map.Tiles[x, y];
					if (tile.ExitMap <= 0) continue;
					int distance = Math.Abs(y - coord);
					if (distance >= bestDistance) continue;
					bestDistance = distance;
					best = new AdjacentExit(x, y, tile.ExitMap, tile.ExitX, tile.ExitY);
				}
			}
			target[coord] = best;
		}
	}
}
