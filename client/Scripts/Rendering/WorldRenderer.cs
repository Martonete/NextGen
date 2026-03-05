using System;
using System.Collections.Generic;
using Godot;
using TierrasSagradasAO.Data;
using TierrasSagradasAO.Game;

namespace TierrasSagradasAO.Rendering;

/// <summary>
/// Renders the game world matching VB6 RenderScreen.
///
/// Layer architecture (children of WorldRenderer, drawn after parent _Draw):
///   WorldRenderer._Draw()         → PASS 1 (layer 1) + PASS 1.5 (reflections)
///   ReflectedAuraLayer (z=-3, additive) → reflected auras (clipped by mask + L2)
///   NonWaterMaskLayer (z=-2)      → PASS 1b (non-water L1 mask, covers reflection/aura overflow)
///   Layer2Layer (z=-1)            → PASS 2 (layer 2, covers aura under border opaque portions)
///   AuraLayer (z=0, additive)     → normal auras
///   ContentLayer (z=0)            → PASS 3 (ground objects + characters + layer 3)
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
    private NonWaterMaskLayer? _maskLayer;
    private Layer2Layer? _layer2Layer;
    private AuraAdditiveLayer? _auraLayer;
    private ContentLayer? _contentLayer;
    private DialogOverlayLayer? _dialogLayer;
    private AdditiveParticleLayer? _additiveLayer;
    private RoofLayer? _roofLayer;

    private const int TileSize = 32;

    // VB6 viewport: 534x408 px
    private const int ViewportWidth = 534;
    private const int ViewportHeight = 408;

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

    // Per-frame character position index
    private readonly Dictionary<(int, int), List<int>> _charPosIndex = new();
    private readonly List<int> _emptyCharList = new();

    // Pending particle draws for the additive blend layer
    private readonly List<(int grhIndex, int frame, Vector2 pos, Color color)> _pendingMapParticleDraws = new();
    private readonly List<(int grhIndex, int frame, Vector2 pos, Color color)> _pendingCharParticleDraws = new();

    // Pending aura draws for the aura additive layer (VB6: D3DBLEND_ONE/ONE)
    private readonly List<(int grhIndex, int frame, Vector2 pos, Color color, float angle)> _pendingAuraDraws = new();
    // Reflected auras — pre-computed final position, no tileHeight offset applied
    private readonly List<(int grhIndex, int frame, Vector2 pos, Color color, float angle)> _pendingReflAuraDraws = new();

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
uniform bool use_lightmap;
varying vec2 v_world_px;

void vertex() {
    v_world_px = VERTEX + world_origin;
}

void fragment() {
    if (use_lightmap) {
        vec2 uv = (v_world_px - 32.0) / 3200.0;
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
        _lightmapTexture = ImageTexture.CreateFromImage(
            Image.CreateEmpty(101, 101, false, Image.Format.Rgb8));
        _lightmapMaterial.SetShaderParameter("lightmap", _lightmapTexture);
        _lightmapMaterial.SetShaderParameter("use_lightmap", false);

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

        // Non-water mask layer: standard blend, z=-2 (redraws non-water L1 tiles
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

    }

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
        if (ux < 1 || ux > 100 || uy < 1 || uy > 100) return;

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
        _charPosIndex.Clear();
        foreach (var kvp in _state!.Characters)
        {
            var ch = kvp.Value;
            var key = (ch.PosX, ch.PosY);
            if (!_charPosIndex.TryGetValue(key, out var list))
            {
                list = new List<int>(2);
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

        // Extended bounds with tile buffer
        _frameMinX = Math.Max(1, screenMinX - TileBufferSize);
        _frameMaxX = Math.Min(100, screenMaxX + TileBufferSize);
        _frameMinY = Math.Max(1, screenMinY - TileBufferSize);
        _frameMaxY = Math.Min(100, screenMaxY + TileBufferSize);

        // L1 bounds (visible +2 margin) — used by mask layer too
        _frameL1MinX = Math.Max(1, screenMinX - 2);
        _frameL1MaxX = Math.Min(100, screenMaxX + 2);
        _frameL1MinY = Math.Max(1, screenMinY - 2);
        _frameL1MaxY = Math.Min(100, screenMaxY + 2);

        _frameHasLights = (_state.Config?.ShowLights ?? true)
                          && _state.MapLights.Count > 0 && _state.TileLightColors != null;

        // GPU lightmap: when lights active, the shader handles ambient + light tinting
        // on terrain layers. Modulate = white so shader is the sole color source.
        // ContentLayer gets map ambient via its own Modulate (characters stay tinted).
        if (_frameHasLights)
        {
            if (_lightmapDirty && _state.TileLightColors != null)
            {
                var img = LightSystem.BuildLightmapImage(_state.TileLightColors);
                _lightmapTexture?.Update(img);
                _lightmapDirty = false;
            }
            _lightmapMaterial?.SetShaderParameter("use_lightmap", true);
            float originX = (_frameUserX - HalfWindowTileWidth) * TileSize - _framePixelOffsetX;
            float originY = (_frameUserY - HalfWindowTileHeight) * TileSize - _framePixelOffsetY;
            _lightmapMaterial?.SetShaderParameter("world_origin", new Vector2(originX, originY));
            Modulate = Colors.White;
            // ContentLayer has no shader — give it the map ambient so characters stay tinted
            if (_contentLayer != null)
                _contentLayer.Modulate = new Color(_state.MapColorR / 255f, _state.MapColorG / 255f,
                                                    _state.MapColorB / 255f, 1f);
        }
        else
        {
            _lightmapMaterial?.SetShaderParameter("use_lightmap", false);
            UpdateAmbientLight();
            if (_contentLayer != null)
                _contentLayer.Modulate = Colors.White;
        }

        // ==========================================
        // PASS 1: Layer 1 (Ground) — shader handles light, no per-tile modulate needed
        // ==========================================
        for (int y = _frameL1MinY; y <= _frameL1MaxY; y++)
        {
            for (int x = _frameL1MinX; x <= _frameL1MaxX; x++)
            {
                ref var tile = ref _state.MapData.Tiles[x, y];
                if (tile.Layer1 <= 0) continue;

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
                if (ch.PosY < 1 || ch.PosY > 97) continue;
                bool hasNearbyWater = false;
                int checkRangeX = ch.Mounted ? 3 : 2;
                for (int cy = ch.PosY + 1; cy <= Math.Min(100, ch.PosY + 5) && !hasNearbyWater; cy++)
                {
                    for (int cx = Math.Max(1, ch.PosX - checkRangeX);
                         cx <= Math.Min(100, ch.PosX + checkRangeX) && !hasNearbyWater; cx++)
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

                CharRenderer.DrawReflection(this, ch, new Vector2(charPx, charPy),
                    headOffset, heading, _data, _animator);

                // Reflected FX overlays (same Y-flip, same pass as body reflection)
                CharRenderer.DrawReflectionFx(this, ch, new Vector2(charPx, charPy),
                    headOffset, heading, _data, _animator);

                // Collect reflected auras (drawn by ReflectedAuraLayer child with additive blend)
                // Skip if Navegando or Montado. Invisible self-char still shows auras.
                if ((_state.Config?.ShowAuras ?? true)
                    && !ch.Navigating && !ch.Mounted
                    && (!ch.Invisible || kvp.Key == _state.UserCharIndex))
                {
                    CharRenderer.CollectReflAuraDraws(this, ch,
                        new Vector2(charPx, charPy), headOffset, _data);
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
                CharRenderer.CollectAuraDraws(this, ch, new Vector2(charPx, charPy), headOffset, _data, auraAlpha);
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
        _maskLayer?.QueueRedraw();
        _layer2Layer?.QueueRedraw();
        _auraLayer?.QueueRedraw();
        _contentLayer?.QueueRedraw();
        _dialogLayer?.QueueRedraw();
        _additiveLayer?.QueueRedraw();
        _roofLayer?.QueueRedraw();
    }

    /// <summary>
    /// Draw PASS 3 content: ground objects, characters, layer 3, status overlay.
    /// Called by ContentLayer._Draw().
    /// </summary>
    public void DrawContent(CanvasItem canvas)
    {
        if (_state == null || _data == null || _animator == null) return;
        if (_state.MapData == null) return;

        for (int y = _frameMinY; y <= _frameMaxY; y++)
        {
            for (int x = _frameMinX; x <= _frameMaxX; x++)
            {
                Vector2 tilePos = TileToScreen(x, y, _frameUserX, _frameUserY,
                                                _framePixelOffsetX, _framePixelOffsetY);
                ref var tile = ref _state.MapData.Tiles[x, y];

                // Per-tile light ratio: how much brighter than ambient this tile is.
                // ContentLayer.Modulate already provides the base ambient tint,
                // so we divide by ambient to get just the extra light contribution.
                // lightRatio = GetTileLight / ambient. Near torch → >1, far → =1.
                Color lightRatio = Colors.White;
                if (_frameHasLights)
                {
                    Color tl = LightSystem.GetTileLight(_state, x, y);
                    float ambR = _state.MapColorR / 255f;
                    float ambG = _state.MapColorG / 255f;
                    float ambB = _state.MapColorB / 255f;
                    lightRatio = new Color(
                        ambR > 0.001f ? tl.R / ambR : 1f,
                        ambG > 0.001f ? tl.G / ambG : 1f,
                        ambB > 0.001f ? tl.B / ambB : 1f, 1f);
                }

                // Ground objects
                if (_state.GroundObjects.TryGetValue((x, y), out int objGrh) && objGrh > 0)
                {
                    bool objNearPlayer = (_state.Config?.TreeRoofTransparency ?? true)
                                       && IsTree(objGrh)
                                       && y > (_frameUserY - 2) && y < (_frameUserY + 7)
                                       && x > (_frameUserX - 4) && x < (_frameUserX + 4);
                    if (objNearPlayer)
                    {
                        DrawTileGrhTo(canvas, objGrh, tilePos, center: true,
                            modulate: new Color(lightRatio.R, lightRatio.G, lightRatio.B, 120f / 255f));
                    }
                    else
                    {
                        DrawTileGrhTo(canvas, objGrh, tilePos, center: true, modulate: lightRatio);
                    }
                }

                // Characters/NPCs at this tile
                var charsHere = GetCharsAt(x, y);
                for (int ci = 0; ci < charsHere.Count; ci++)
                {
                    if (!_state.Characters.TryGetValue(charsHere[ci], out var ch)) continue;

                    if (ch.Invisible && charsHere[ci] != _state.UserCharIndex) continue;

                    float charPx = tilePos.X + (float)Math.Round(ch.MoveOffsetX);
                    float charPy = tilePos.Y + (float)Math.Round(ch.MoveOffsetY);

                    // Pass 'this' as worldRenderer so CharRenderer can queue particle draws
                    CharRenderer.DrawCharacter((Node2D)canvas, ch, new Vector2(charPx, charPy),
                                               _data, _animator, _deltaMs, _state, this,
                                               charTileX: x, charTileY: y,
                                               lightModulate: lightRatio);
                }

                // Layer 3 (trees/objects)
                if (tile.Layer3 > 0)
                {
                    bool nearPlayer = (_state.Config?.TreeRoofTransparency ?? true)
                                   && IsTree(tile.Layer3)
                                   && y > (_frameUserY - 2) && y < (_frameUserY + 7)
                                   && x > (_frameUserX - 4) && x < (_frameUserX + 4);
                    if (nearPlayer)
                    {
                        DrawTileGrhTo(canvas, tile.Layer3, tilePos, center: true,
                            modulate: new Color(lightRatio.R, lightRatio.G, lightRatio.B, 120f / 255f));
                    }
                    else
                    {
                        DrawTileGrhTo(canvas, tile.Layer3, tilePos, center: true, modulate: lightRatio);
                    }
                }
            }
        }

        // Status overlay (VB6: drawCounters — paralysis/invisibility bars + status icons)
        DrawStatusOverlayTo(canvas);
    }

    /// <summary>
    /// VB6 HayAgua(): checks if a tile is water (Layer1 GRH 1505-1520 and Layer2 == 0).
    /// </summary>
    public static bool IsWater(MapData? mapData, int x, int y)
    {
        if (mapData == null || x < 1 || x > 100 || y < 1 || y > 100) return false;
        ref var tile = ref mapData.Tiles[x, y];
        int grh = tile.Layer1;
        return grh >= 1505 && grh <= 1520 && tile.Layer2 <= 0;
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

    private void DrawStatusOverlayTo(CanvasItem canvas)
    {
        if (_state == null || _data == null) return;

        if (_state.UserParalyzed && _state.ParalysisTimer > 0)
        {
            // Decrement in real seconds (deltaMs is in milliseconds)
            _state.ParalysisTimer -= _deltaMs / 1000f;
            if (_state.ParalysisTimer < 0) _state.ParalysisTimer = 0;
        }

        int slot = 0;

        if (_state.UserParalyzed)
        {
            DrawStatusIconTo(canvas, slot, 23610, _state.ParalysisTimer, _state.ParalysisMaxTimer, "PARALIZADO",
                           new Color(1f, 0.2f, 0.2f));
            slot++;
        }

        if (_state.Characters.TryGetValue(_state.UserCharIndex, out var selfCh) && selfCh.Invisible)
        {
            // Decrement invisibility countdown in real seconds
            if (selfCh.InvisibleCountdown > 0)
            {
                selfCh.InvisibleCountdownTimer += _deltaMs;
                if (selfCh.InvisibleCountdownTimer >= 1000f)
                {
                    selfCh.InvisibleCountdownTimer -= 1000f;
                    selfCh.InvisibleCountdown--;
                }
            }
            float inviCurrent = selfCh.InvisibleCountdown;
            // Spell invisibility has countdown (label "INVISIBLE"), hide skill has no countdown (label "OCULTO")
            bool isSpellInvi = selfCh.InvisibleMaxCountdown > 0;
            string inviLabel = isSpellInvi ? "INVISIBLE" : "OCULTO";
            DrawStatusIconTo(canvas, slot, 23611, inviCurrent, selfCh.InvisibleMaxCountdown, inviLabel,
                           new Color(0.6f, 0.6f, 1f));
            slot++;
        }

        if (_state.Meditating)
        {
            DrawStatusIconTo(canvas, slot, 0, -1, -1, "MEDITANDO",
                           new Color(0.4f, 0.8f, 1f));
            slot++;
        }

        if (_state.Resting)
        {
            DrawStatusIconTo(canvas, slot, 0, -1, -1, "DESCANSANDO",
                           new Color(0.4f, 1f, 0.4f));
            slot++;
        }

        if (_state.UserNavigating)
        {
            // Draw on the right side to avoid overlapping countdown bars
            DrawStatusLabelRight(canvas, "NAVEGANDO", new Color(0.3f, 0.7f, 1f));
        }
    }

    private void DrawStatusIconTo(CanvasItem canvas, int slot, int grhIcon, float current, float max,
                                 string label, Color labelColor)
    {
        float baseX = 10f;
        float baseY = 5f + slot * 38f;

        if (grhIcon > 0 && _data != null)
        {
            CharRenderer.DrawGrh(canvas, _data, grhIcon, 0, new Vector2(baseX, baseY));
        }

        if (max > 0 && current >= 0)
        {
            float barX = baseX + 3;
            float barY = baseY + 35;
            float barW = 25;
            float barH = 6;

            ((Node2D)canvas).DrawRect(new Rect2(barX, barY, barW, barH),
                     new Color(0.49f, 0.49f, 0.49f, 0.59f));
            float fill = Math.Clamp(current / max, 0f, 1f) * barW;
            if (fill > 0)
            {
                ((Node2D)canvas).DrawRect(new Rect2(barX, barY, fill, barH),
                         new Color(1f, 1f, 0f, 0.78f));
            }
        }

        if (grhIcon <= 0 && _data?.Fonts?[1] != null)
        {
            _data.Fonts[1]!.DrawText(canvas, (int)baseX, (int)baseY + 2, label, labelColor);
        }
    }

    /// <summary>
    /// Draw a status label on the top-right corner of the viewport.
    /// </summary>
    private void DrawStatusLabelRight(CanvasItem canvas, string label, Color color)
    {
        if (_data?.Fonts?[1] == null) return;
        float x = 534f - 10f - (label.Length * 8f); // approximate right-align
        _data.Fonts[1]!.DrawText(canvas, (int)x, 7, label, color);
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
    /// Draw pending reflected auras on a given canvas (used by ReflectedAuraLayer).
    /// No per-tile clipping needed — NonWaterMaskLayer covers land overflow,
    /// Layer2Layer covers border opaque portions.
    /// </summary>
    public void DrawPendingReflAuras(CanvasItem canvas)
    {
        if (_data == null) return;

        foreach (var (grhIndex, frame, pos, color, angle) in _pendingReflAuraDraws)
        {
            var resolved = _data.ResolveGrh(grhIndex, frame);
            if (resolved == null || resolved.FileNum <= 0) continue;
            var texture = _data.Textures?.GetTexture(resolved.FileNum);
            if (texture == null) continue;

            int sx = resolved.SX, sy = resolved.SY;
            int pw = resolved.PixelWidth, ph = resolved.PixelHeight;
            int texW = texture.GetWidth(), texH = texture.GetHeight();
            if (texW > 0) sx = sx % texW;
            if (texH > 0) sy = sy % texH;
            if (sx + pw > texW) pw = texW - sx;
            if (sy + ph > texH) ph = texH - sy;
            if (pw <= 0 || ph <= 0) continue;

            // Only tileWidth centering — tileHeight is baked into the reflected Y position
            float drawX = pos.X;
            float drawY = pos.Y;
            if (resolved.TileWidth != 1f && resolved.TileWidth > 0)
                drawX -= (int)(resolved.TileWidth * (TileSize / 2)) - TileSize / 2;

            if (angle != 0f)
            {
                // Rotating reflected aura — DrawSetTransform around sprite center
                float cx = drawX + pw / 2f;
                float cy = drawY + ph / 2f;
                ((Node2D)canvas).DrawSetTransform(new Vector2(cx, cy), angle);
                var srcRect = new Rect2(sx, sy, pw, ph);
                var destRect = new Rect2(-pw / 2f, -ph / 2f, pw, ph);
                canvas.DrawTextureRectRegion(texture, destRect, srcRect, color);
                ((Node2D)canvas).DrawSetTransform(Vector2.Zero, 0f);
            }
            else
            {
                // Non-rotating reflected aura — draw at position
                var srcRect = new Rect2(sx, sy, pw, ph);
                var destRect = new Rect2(drawX, drawY, pw, ph);
                canvas.DrawTextureRectRegion(texture, destRect, srcRect, color);
            }
        }
    }

    /// <summary>
    /// Draw PASS 1b: redraw non-water L1 tiles ADJACENT to water to mask reflection overflow.
    /// Only redraws tiles in the precomputed water border mask (~20-50 tiles) instead of ALL
    /// non-water tiles (~300+). Called by NonWaterMaskLayer._Draw().
    /// </summary>
    public void DrawNonWaterMask(CanvasItem canvas)
    {
        if (_state?.MapData == null || _data == null || _animator == null) return;
        if (!_frameAnyReflection) return;

        for (int y = _frameL1MinY; y <= _frameL1MaxY; y++)
        {
            for (int x = _frameL1MinX; x <= _frameL1MaxX; x++)
            {
                ref var tile = ref _state.MapData.Tiles[x, y];
                if (tile.Layer1 <= 0) continue;
                if (tile.Layer1 >= 1505 && tile.Layer1 <= 1520) continue; // skip water

                Vector2 pos = TileToScreen(x, y, _frameUserX, _frameUserY,
                                            _framePixelOffsetX, _framePixelOffsetY);
                DrawTileGrhTo(canvas, tile.Layer1, pos, center: false);
            }
        }
    }

    /// <summary>
    /// Draw PASS 2: Layer 2 tiles with per-tile light modulate.
    /// Called by Layer2Layer._Draw().
    /// </summary>
    public void DrawLayer2(CanvasItem canvas)
    {
        if (_state?.MapData == null || _data == null || _animator == null) return;

        for (int y = _frameMinY; y <= _frameMaxY; y++)
        {
            for (int x = _frameMinX; x <= _frameMaxX; x++)
            {
                ref var tile = ref _state.MapData.Tiles[x, y];
                if (tile.Layer2 <= 0) continue;

                Vector2 pos = TileToScreen(x, y, _frameUserX, _frameUserY, _framePixelOffsetX, _framePixelOffsetY);
                DrawTileGrhTo(canvas, tile.Layer2, pos, center: false);
            }
        }
    }

    /// <summary>
    /// Draw pending normal aura draws on a given canvas (used by AuraAdditiveLayer).
    /// Only normal (non-reflected) auras — reflected auras are handled by ReflectedAuraLayer.
    /// </summary>
    public void DrawPendingAuras(CanvasItem canvas)
    {
        if (_data == null) return;

        foreach (var (grhIndex, frame, pos, color, angle) in _pendingAuraDraws)
        {
            if (angle != 0f)
            {
                // Rotating aura — use DrawSetTransform for rotation around sprite center
                var resolved = _data.ResolveGrh(grhIndex, frame);
                if (resolved == null || resolved.FileNum <= 0) continue;
                var texture = _data.Textures?.GetTexture(resolved.FileNum);
                if (texture == null) continue;

                int sx = resolved.SX, sy = resolved.SY;
                int pw = resolved.PixelWidth, ph = resolved.PixelHeight;
                int texW = texture.GetWidth(), texH = texture.GetHeight();
                if (texW > 0) sx = sx % texW;
                if (texH > 0) sy = sy % texH;
                if (sx + pw > texW) pw = texW - sx;
                if (sy + ph > texH) ph = texH - sy;
                if (pw <= 0 || ph <= 0) continue;

                // Center the GRH
                float drawX = pos.X;
                float drawY = pos.Y;
                if (resolved.TileWidth != 1f && resolved.TileWidth > 0)
                    drawX -= (int)(resolved.TileWidth * (TileSize / 2)) - TileSize / 2;
                if (resolved.TileHeight != 1f && resolved.TileHeight > 0)
                    drawY -= (int)(resolved.TileHeight * TileSize) - TileSize;

                float cx = drawX + pw / 2f;
                float cy = drawY + ph / 2f;
                ((Node2D)canvas).DrawSetTransform(new Vector2(cx, cy), angle);
                var srcRect = new Rect2(sx, sy, pw, ph);
                var destRect = new Rect2(-pw / 2f, -ph / 2f, pw, ph);
                canvas.DrawTextureRectRegion(texture, destRect, srcRect, color);
                ((Node2D)canvas).DrawSetTransform(Vector2.Zero, 0f);
            }
            else
            {
                CharRenderer.DrawGrh(canvas, _data, grhIndex, frame, pos, true, color);
            }
        }
    }

    /// <summary>
    /// Draw all pending particle draws on a given canvas (used by AdditiveParticleLayer).
    /// </summary>
    public void DrawPendingParticles(CanvasItem canvas)
    {
        if (_data == null) return;

        foreach (var (grhIndex, frame, pos, color) in _pendingMapParticleDraws)
        {
            CharRenderer.DrawGrh(canvas, _data, grhIndex, frame, pos, true, color);
        }
        foreach (var (grhIndex, frame, pos, color) in _pendingCharParticleDraws)
        {
            CharRenderer.DrawGrh(canvas, _data, grhIndex, frame, pos, true, color);
        }
    }

    /// <summary>
    /// Queue a character-attached particle draw for the additive blend layer.
    /// Called by CharRenderer.DrawCharParticles.
    /// </summary>
    public void QueueCharParticleDraw(int grhIndex, int frame, Vector2 pos, Color color)
    {
        _pendingCharParticleDraws.Add((grhIndex, frame, pos, color));
    }

    /// <summary>
    /// Queue an aura draw for the aura additive layer.
    /// Called by CharRenderer.CollectAuraDraws.
    /// </summary>
    public void QueueAuraDraw(int grhIndex, int frame, Vector2 pos, Color color, float angle)
    {
        _pendingAuraDraws.Add((grhIndex, frame, pos, color, angle));
    }

    /// <summary>
    /// Queue a reflected aura draw. Position is pre-computed final (no tileHeight offset applied).
    /// </summary>
    public void QueueReflAuraDraw(int grhIndex, int frame, Vector2 pos, Color color, float angle)
    {
        _pendingReflAuraDraws.Add((grhIndex, frame, pos, color, angle));
    }

    /// <summary>
    /// Queue a dialog draw for the overlay layer (above all characters).
    /// Called by CharRenderer.DrawDialog.
    /// </summary>
    public void QueueDialogDraw(string[] lines, int textCenterX, int baseY, int fontSize, Color color)
    {
        _pendingDialogDraws.Add((lines, textCenterX, baseY, fontSize, color));
    }

    /// <summary>
    /// Draw pending dialog text on a given canvas (used by DialogOverlayLayer).
    /// </summary>
    public void DrawPendingDialogs(CanvasItem canvas)
    {
        if (_data?.Fonts?[1] == null) return;
        var font = _data.Fonts[1]!;

        foreach (var (lines, textCenterX, baseY, fontSize, color) in _pendingDialogDraws)
        {
            int offset = -(fontSize + 2) * (lines.Length - 1);
            for (int i = 0; i < lines.Length; i++)
            {
                int lineY = baseY + offset + 2;
                font.DrawText(canvas, textCenterX, lineY, lines[i], color, center: true);
                offset += fontSize + 5;
            }
        }
    }

    /// <summary>
    /// Draw all pending roof tiles on a given canvas (used by RoofLayer).
    /// </summary>
    public void DrawPendingRoof(CanvasItem canvas)
    {
        if (_data == null || _animator == null) return;
        foreach (var (grhIndex, pos, modulate) in _pendingRoofDraws)
        {
            int frame = _animator.GetCurrentFrame(grhIndex, _data);
            CharRenderer.DrawGrh(canvas, _data, grhIndex, frame, pos, true, modulate);
        }
    }
}

/// <summary>
/// Child Node2D with additive blend material. Draws reflected auras.
/// z_index=-3, draws AFTER parent _Draw (body reflections) but BEFORE mask and L2.
/// NonWaterMaskLayer (z=-2) covers overflow onto land.
/// Layer2Layer (z=-1) covers overlap under border opaque portions.
/// </summary>
public partial class ReflectedAuraLayer : Node2D
{
    private WorldRenderer? _renderer;

    public void SetRenderer(WorldRenderer renderer)
    {
        _renderer = renderer;
    }

    public override void _Draw()
    {
        _renderer?.DrawPendingReflAuras(this);
    }
}

/// <summary>
/// Child Node2D for PASS 1b: redraws non-water L1 tiles to mask reflection
/// and reflected aura overflow onto land.
/// z_index=-2, draws AFTER reflected auras but BEFORE L2.
/// </summary>
public partial class NonWaterMaskLayer : Node2D
{
    private WorldRenderer? _renderer;

    public void SetRenderer(WorldRenderer renderer)
    {
        _renderer = renderer;
    }

    public override void _Draw()
    {
        _renderer?.DrawNonWaterMask(this);
    }
}

/// <summary>
/// Child Node2D for PASS 2: draws Layer 2 tiles (borders, objects).
/// z_index=-1, draws AFTER mask but BEFORE normal auras.
/// Covers reflected aura portions under border opaque areas.
/// </summary>
public partial class Layer2Layer : Node2D
{
    private WorldRenderer? _renderer;

    public void SetRenderer(WorldRenderer renderer)
    {
        _renderer = renderer;
    }

    public override void _Draw()
    {
        _renderer?.DrawLayer2(this);
    }
}

/// <summary>
/// Child Node2D with additive blend material. Draws normal auras queued by WorldRenderer.
/// z_index=0, added before ContentLayer — same-z children draw in tree order.
/// </summary>
public partial class AuraAdditiveLayer : Node2D
{
    private WorldRenderer? _renderer;

    public void SetRenderer(WorldRenderer renderer)
    {
        _renderer = renderer;
    }

    public override void _Process(double delta)
    {
        QueueRedraw();
    }

    public override void _Draw()
    {
        _renderer?.DrawPendingAuras(this);
    }
}

/// <summary>
/// Child Node2D for PASS 3 content: ground objects, characters, layer 3, status.
/// z_index=0 — draws characters + ground objects + layer 3. Added after AuraLayer.
/// </summary>
public partial class ContentLayer : Node2D
{
    private WorldRenderer? _renderer;

    public void SetRenderer(WorldRenderer renderer)
    {
        _renderer = renderer;
    }

    public override void _Draw()
    {
        _renderer?.DrawContent(this);
    }
}

/// <summary>
/// Child Node2D for dialog text overlay. z_index=1 — above characters/NPCs,
/// below particles (z=2) and roof (z=3). Ensures dialog bubbles are always readable.
/// </summary>
public partial class DialogOverlayLayer : Node2D
{
    private WorldRenderer? _renderer;

    public void SetRenderer(WorldRenderer renderer)
    {
        _renderer = renderer;
    }

    public override void _Draw()
    {
        _renderer?.DrawPendingDialogs(this);
    }
}

/// <summary>
/// Child Node2D that draws Layer 4 (roof) AFTER particle layer.
/// z_index=3.
/// </summary>
public partial class RoofLayer : Node2D
{
    private WorldRenderer? _renderer;

    public void SetRenderer(WorldRenderer renderer)
    {
        _renderer = renderer;
    }

    public override void _Draw()
    {
        _renderer?.DrawPendingRoof(this);
    }
}

/// <summary>
/// Child Node2D with additive blend material. Draws particle sprites.
/// z_index=2.
/// </summary>
public partial class AdditiveParticleLayer : Node2D
{
    private WorldRenderer? _renderer;

    public void SetRenderer(WorldRenderer renderer)
    {
        _renderer = renderer;
    }

    public override void _Draw()
    {
        _renderer?.DrawPendingParticles(this);
    }
}
