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
	private FogOverlayLayer? _fogLayer;

	private const int TileSize = 32;

	// VB6 viewport: 544x416 px (MainViewPic ScaleWidth/ScaleHeight)
	private const int ViewportWidth = 544;
	private const int ViewportHeight = 416;

	// How many tiles from center to edge (visible range)
	private const int HalfWindowTileWidth = 8;
	private const int HalfWindowTileHeight = 6;

	// VB6: TileBufferSize = 9
	private const int TileBufferSize = 9;

	// VB6: bTechoAB — roof alpha
	private float _roofAlpha = 255f;
	private const float RoofFadeRate = 6f;

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

		// Fog overlay layer: z=3.5 (above roof, below floating text)
		// Darkens tiles outside the core 17x13 area with a gradient
		_fogLayer = new FogOverlayLayer();
		_fogLayer.Name = "FogOverlay";
		_fogLayer.ZIndex = 3; // Same z as roof, but added after so draws on top
		_fogLayer.SetRenderer(this);
		AddChild(_fogLayer);

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
		if (ux < 1 || ux > _state.MapData.Width || uy < 1 || uy > _state.MapData.Height) return;

		short trigger = _state.MapData.Tiles[ux, uy].Trigger;
		bool underRoof = trigger == 1 || trigger == 2 || trigger == 4;

		if (underRoof)
		{
			_roofAlpha -= RoofFadeRate;
			if (_roofAlpha < 0) _roofAlpha = 0;
		}
		else
		{
			_roofAlpha += RoofFadeRate;
			if (_roofAlpha > 255) _roofAlpha = 255;
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
	/// Uses dynamic half-render tiles so extra tiles at higher resolutions
	/// are positioned correctly within the enlarged SubViewport.
	/// </summary>
	private static Vector2 TileToScreen(int tileX, int tileY, int userX, int userY,
										 float pixelOffsetX, float pixelOffsetY)
	{
		float px = (tileX - userX + ResolutionManager.HalfRenderTilesX) * TileSize + pixelOffsetX;
		float py = (tileY - userY + ResolutionManager.HalfRenderTilesY) * TileSize + pixelOffsetY;
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

		// Visible tile range (dynamic: uses full render area at current resolution)
		int halfX = ResolutionManager.HalfRenderTilesX;
		int halfY = ResolutionManager.HalfRenderTilesY;
		int screenMinX = _frameUserX - halfX;
		int screenMaxX = _frameUserX + halfX;
		int screenMinY = _frameUserY - halfY;
		int screenMaxY = _frameUserY + halfY;

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
			float originX = (_frameUserX - halfX) * TileSize - _framePixelOffsetX;
			float originY = (_frameUserY - halfY) * TileSize - _framePixelOffsetY;
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
				// has water (L1 GRH 1505-1520). Checking 3 rows allows the reflection
				// to smoothly fade out as the character walks away from water.
				// NonWaterMaskLayer ensures reflections only show on water tiles.
				if (ch.PosY < 1 || ch.PosY > mapH - 3) continue;
				bool hasNearbyWater = false;
				int checkRangeX = ch.Mounted ? 3 : 2;
				for (int cy = ch.PosY + 1; cy <= Math.Min(mapH, ch.PosY + 5) && !hasNearbyWater; cy++)
				{
					for (int cx = Math.Max(1, ch.PosX - checkRangeX);
						 cx <= Math.Min(mapW, ch.PosX + checkRangeX) && !hasNearbyWater; cx++)
					{
						ref var wt = ref _state.MapData.Tiles[cx, cy];
						if (wt.Layer1 >= 1505 && wt.Layer1 <= 1520)
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

		// Collect roof draws — shader handles light, only need alpha for fade
		if (_roofAlpha > 0)
		{
			float roofA = _roofAlpha / 255f;

			for (int y = _frameMinY; y <= _frameMaxY; y++)
			{
				for (int x = _frameMinX; x <= _frameMaxX; x++)
				{
					ref var tile = ref _state.MapData.Tiles[x, y];
					if (tile.Layer4 <= 0) continue;

					Vector2 pos = TileToScreen(x, y, _frameUserX, _frameUserY, _framePixelOffsetX, _framePixelOffsetY);
					_pendingRoofDraws.Add((tile.Layer4, pos, new Color(1, 1, 1, roofA)));
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
		_fogLayer?.QueueRedraw();
	}

	/// <summary>
	/// Draw fog overlay: darkens tiles outside the core 17x13 viewport.
	/// Creates a gradient from fully transparent at the core edge to
	/// semi-opaque (alpha ~0.7) at the render area boundary.
	/// Called by FogOverlayLayer._Draw().
	/// </summary>
	public void DrawFogOverlay(CanvasItem canvas)
	{
		int extraX = ResolutionManager.ExtraTilesX;
		int extraY = ResolutionManager.ExtraTilesY;
		if (extraX <= 0 && extraY <= 0) return;

		int renderW = ResolutionManager.RenderTilesX * TileSize;
		int renderH = ResolutionManager.RenderTilesY * TileSize;

		// Core area bounds in pixels (centered in the render area)
		int coreW = ResolutionManager.CoreTilesX * TileSize;
		int coreH = ResolutionManager.CoreTilesY * TileSize;
		int coreLeft = (renderW - coreW) / 2;
		int coreTop = (renderH - coreH) / 2;
		int coreRight = coreLeft + coreW;
		int coreBottom = coreTop + coreH;

		// Draw fog rings from core edge outward with increasing alpha.
		// Each ring is one tile thick. Alpha increases linearly from 0 at
		// core edge to MaxAlpha at the outermost ring.
		int maxRings = Math.Max(extraX, extraY);
		const float MaxAlpha = 0.7f;

		for (int ring = 1; ring <= maxRings; ring++)
		{
			float alpha = MaxAlpha * ring / maxRings;
			var fogColor = new Color(0f, 0f, 0f, alpha);

			int ringPixelsX = Math.Min(ring, extraX) * TileSize;
			int ringPixelsY = Math.Min(ring, extraY) * TileSize;
			int prevPixelsX = Math.Min(ring - 1, extraX) * TileSize;
			int prevPixelsY = Math.Min(ring - 1, extraY) * TileSize;

			// Top strip
			if (ring <= extraY)
			{
				float stripTop = coreTop - ringPixelsY;
				float stripH = (float)TileSize;
				((Node2D)canvas).DrawRect(
					new Rect2(coreLeft - ringPixelsX, stripTop,
							  coreW + ringPixelsX * 2, stripH), fogColor);
			}

			// Bottom strip
			if (ring <= extraY)
			{
				float stripBottom = coreBottom + prevPixelsY;
				float stripH = (float)TileSize;
				((Node2D)canvas).DrawRect(
					new Rect2(coreLeft - ringPixelsX, stripBottom,
							  coreW + ringPixelsX * 2, stripH), fogColor);
			}

			// Left strip (between top and bottom strips of this ring)
			if (ring <= extraX)
			{
				float stripLeft = coreLeft - ringPixelsX;
				float stripW = (float)TileSize;
				float stripTop = coreTop - prevPixelsY;
				float stripH = coreH + prevPixelsY * 2;
				((Node2D)canvas).DrawRect(
					new Rect2(stripLeft, stripTop, stripW, stripH), fogColor);
			}

			// Right strip
			if (ring <= extraX)
			{
				float stripRight = coreRight + prevPixelsX;
				float stripW = (float)TileSize;
				float stripTop = coreTop - prevPixelsY;
				float stripH = coreH + prevPixelsY * 2;
				((Node2D)canvas).DrawRect(
					new Rect2(stripRight, stripTop, stripW, stripH), fogColor);
			}
		}
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
