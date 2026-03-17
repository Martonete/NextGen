using System;
using System.Collections.Generic;
using Godot;
using ArgentumNextgen.Data;
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

	private const int TileSize = 32;

	// Viewport dimensions — dynamic, read from ResolutionManager
	private static int ViewportWidth => ResolutionManager.ViewportW;
	private static int ViewportHeight => ResolutionManager.ViewportH;

	// How many tiles from center to edge (visible range) — dynamic
	private static int HalfWindowTileWidth => ResolutionManager.HalfTilesX;
	private static int HalfWindowTileHeight => ResolutionManager.HalfTilesY;

	// VB6: TileBufferSize = 9
	private const int TileBufferSize = 9;

	// VB6: bTechoAB — roof alpha (per-region fade, delta-time based)
	private float _roofAlpha = 255f;
	private const float RoofFadeSpeed = 400f; // units per second (255→20 in ~0.6s)
	private const float RoofMinAlpha = 20f; // never fully invisible — keeps a faint ghost

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

	// Pre-computed water tile map — built on map load, avoids per-frame GRH range checks.
	// 1-indexed: _waterMap[x, y] = true if L1 GRH is in water range 1505-1520.
	private bool[,]? _waterMap;

	// Per-frame camera data (computed in _Draw, used by child layer callbacks)
	private int _frameUserX, _frameUserY;
	private float _framePixelOffsetX, _framePixelOffsetY;
	private int _frameMinX, _frameMaxX, _frameMinY, _frameMaxY;
	private int _frameL1MinX, _frameL1MaxX, _frameL1MinY, _frameL1MaxY;
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
		COLOR.rgb *= light;
	}
}
";

	/// <summary>
	/// Called by Main after RecalculateLights() to trigger lightmap texture rebuild.
	/// </summary>
	public void MarkLightmapDirty() => _lightmapDirty = true;

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
				_waterMap[x, y] = g >= 1505 && g <= 1520;
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

	public void Init(GameState state, GameData data, GrhAnimator animator)
	{
		_state = state;
		_data = data;
		_animator = animator;

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

		// Fog overlay: z=6 (darkens extended viewport edges beyond core 17x13)
		var fogOverlay = new FogOverlayLayer();
		fogOverlay.Name = "FogOverlay";
		fogOverlay.ZIndex = 6;
		AddChild(fogOverlay);
	}

	/// <summary>
	/// Initialize weather renderer with sound manager (called from Main after Init).
	/// </summary>
	public void InitWeather(SoundManager? soundManager)
	{
		_weatherRenderer?.Init(_state!, soundManager);
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
		UpdateAmbientLight();
		QueueRedraw();
	}

	/// <summary>
	/// VB6: all tiles/characters are drawn with the map's ambient RGB.
	/// Default is 200,200,200 (slightly dimmed). Applied via Godot's Modulate.
	/// </summary>
	private void UpdateAmbientLight()
	{
		// When day/night is disabled, force full brightness
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
	private static Vector2 TileToScreen(int tileX, int tileY, int userX, int userY,
										 float pixelOffsetX, float pixelOffsetY)
	{
		float px = (tileX - userX + HalfWindowTileWidth) * TileSize + pixelOffsetX;
		float py = (tileY - userY + HalfWindowTileHeight) * TileSize + pixelOffsetY;
		return new Vector2(px, py);
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

		// VB6 ShowNextFrame: render center = UserPos - AddtoUserPos, offset = OffsetCounter
		_frameUserX = _state.UserPosX - _state.AddToUserPosX;
		_frameUserY = _state.UserPosY - _state.AddToUserPosY;

		BuildCharPositionIndex();

		// Camera pixel offset — NEGATED because ScreenOffset grows in the movement
		// direction, but tiles must shift in the OPPOSITE direction on screen.
		_framePixelOffsetX = (float)Math.Round(-_state.ScreenOffsetX);
		_framePixelOffsetY = (float)Math.Round(-_state.ScreenOffsetY);

		// Visible tile range
		int screenMinX = _frameUserX - HalfWindowTileWidth;
		int screenMaxX = _frameUserX + HalfWindowTileWidth;
		int screenMinY = _frameUserY - HalfWindowTileHeight;
		int screenMaxY = _frameUserY + HalfWindowTileHeight;

		// Extended bounds with tile buffer (clamped to map dimensions)
		int mapW = _state.MapData.Width;
		int mapH = _state.MapData.Height;
		_frameMinX = Math.Max(1, screenMinX - TileBufferSize);
		_frameMaxX = Math.Min(mapW, screenMaxX + TileBufferSize);
		_frameMinY = Math.Max(1, screenMinY - TileBufferSize);
		_frameMaxY = Math.Min(mapH, screenMaxY + TileBufferSize);

		// L1 bounds (visible +2 margin) — used by mask layer too
		_frameL1MinX = Math.Max(1, screenMinX - 2);
		_frameL1MaxX = Math.Min(mapW, screenMaxX + 2);
		_frameL1MinY = Math.Max(1, screenMinY - 2);
		_frameL1MaxY = Math.Min(mapH, screenMaxY + 2);

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
				ref var tile = ref _state.MapData.Tiles[x, y];
				if (tile.Layer1 < 1505 || tile.Layer1 > 1520) continue; // only water

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

		if (showReflections)
		{
			foreach (var kvp in _state.Characters)
			{
				var ch = kvp.Value;
				if (ch.Invisible) continue;

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

		// ==========================================
		// Pre-compute PASS 3 data for ContentLayer (characters queue aura draws)
		// We must process characters HERE (parent _Draw runs first) so aura data
		// is available when AuraLayer._Draw() fires.
		// But actual character drawing goes to ContentLayer via DrawContent().
		// ==========================================
		// Collect aura draws by iterating characters and updating their aura state.
		// This populates _pendingAuraDraws before AuraLayer._Draw() fires.
		foreach (var kvp in _state.Characters)
		{
			var ch = kvp.Value;
			// Invisible chars from OTHER players skip ALL rendering
			if (ch.Invisible && kvp.Key != _state.UserCharIndex) continue;
			// Auras not drawn when Navegando or Montado (equipment hidden)
			if (ch.Navigating || ch.Mounted) continue;

			// Compute character screen position
			var tilePos = TileToScreen(ch.PosX, ch.PosY, _frameUserX, _frameUserY,
										_framePixelOffsetX, _framePixelOffsetY);
			float charPx = tilePos.X + (float)Math.Round(ch.MoveOffsetX);
			float charPy = tilePos.Y + (float)Math.Round(ch.MoveOffsetY);

			// Pre-resolve head offset for aura positioning
			Vector2 headOffset = new Vector2(0, -30);
			if (ch.Body > 0 && ch.Body < _data.Bodies.Length)
			{
				var body = _data.Bodies[ch.Body];
				headOffset = new Vector2(body.HeadOffsetX, body.HeadOffsetY);
			}

			// Collect aura draws (updates angle state + queues to _pendingAuraDraws)
			// When invisible (self only), auras pulse with the same alpha as the body
			if (_state.Config?.ShowAuras ?? true)
			{
				float auraAlpha = 1f;
				if (ch.Invisible)
				{
					// TransparenciaBody ranges 18-53, maps to alpha ~45-135 out of 255
					auraAlpha = (ch.TransparenciaBody + 45f) / 255f;
				}
				CharRenderer.CollectAuraDraws(this, ch, new Vector2(charPx, charPy), headOffset, _data, _animator!.GlobalTimeMs, auraAlpha);
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
					var color = new Color(p.ColR / 255f, p.ColG / 255f, p.ColB / 255f, p.Alpha);
					Vector2 pPos = streamPos + new Vector2(p.X, p.Y);
					int frame = _animator.GetCurrentFrame(p.GrhIndex, _data);
					_pendingMapParticleDraws.Add((p.GrhIndex, frame, pPos, color));
				}
			}
		}

		// Collect roof draws — per-region fade using _fadingRoofRegion (persists during fade-out AND fade-in)
		{
			float fadeA = _roofAlpha / 255f;

			for (int y = _frameMinY; y <= _frameMaxY; y++)
			{
				for (int x = _frameMinX; x <= _frameMaxX; x++)
				{
					ref var tile = ref _state.MapData.Tiles[x, y];
					if (tile.Layer4 <= 0) continue;

					// Determine alpha: tiles in the fading region get fade, others stay opaque
					float alpha;
					if (_fadingRoofRegion > 0 && _roofRegionMap != null
						&& x >= 1 && x <= mapW && y >= 1 && y <= mapH
						&& _roofRegionMap[x, y] == _fadingRoofRegion)
					{
						alpha = fadeA; // fading region
					}
					else
					{
						alpha = 1f; // other roofs always visible
					}
					if (alpha <= 0f) continue; // fully hidden, skip draw

					Vector2 pos = TileToScreen(x, y, _frameUserX, _frameUserY, _framePixelOffsetX, _framePixelOffsetY);
					_pendingRoofDraws.Add((tile.Layer4, pos, new Color(1, 1, 1, alpha)));
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
	/// VB6 HayAgua(): checks if a tile is water (3 graphic ranges and Layer2 == 0).
	/// </summary>
	public static bool IsWater(MapData? mapData, int x, int y)
	{
		if (mapData == null || x < 1 || x > mapData.Width || y < 1 || y > mapData.Height) return false;
		ref var tile = ref mapData.Tiles[x, y];
		int g = tile.Layer1;
		bool isWater = (g >= 1505 && g <= 1520)
			|| (g >= 5665 && g <= 5680)
			|| (g >= 13547 && g <= 13562);
		return isWater && tile.Layer2 <= 0;
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
