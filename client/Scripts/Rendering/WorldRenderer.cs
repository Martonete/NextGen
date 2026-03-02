using System;
using System.Collections.Generic;
using Godot;
using TierrasSagradasAO.Data;
using TierrasSagradasAO.Game;

namespace TierrasSagradasAO.Rendering;

/// <summary>
/// Renders the game world matching VB6 RenderScreen.
///
/// Layer architecture (Godot child draw order — all children draw AFTER parent):
///   WorldRenderer._Draw()       → PASS 1+2 (terrain layers 1+2)
///   AuraLayer (z=0, additive)   → auras (VB6: drawn before dibujarPersonaje, D3DBLEND_ONE/ONE)
///   ContentLayer (z=1)          → PASS 3 (ground objects + characters + layer 3) + status overlay
///   AdditiveParticleLayer (z=2) → particles (VB6: D3DBLEND_ONE/ONE)
///   RoofLayer (z=3)             → PASS 4 (roof with fade)
///
/// This ensures auras render AFTER terrain but BEFORE characters, with additive blend.
/// </summary>
public partial class WorldRenderer : Node2D
{
    private GameState? _state;
    private GameData? _data;
    private GrhAnimator? _animator;

    // Child layers
    private AuraAdditiveLayer? _auraLayer;
    private ContentLayer? _contentLayer;
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

    // Pending roof tile draws (queued in _Draw, drawn by RoofLayer child node AFTER particles)
    private readonly List<(int grhIndex, Vector2 pos, Color modulate)> _pendingRoofDraws = new();

    // Per-frame camera data (computed in _Draw, used by child layer callbacks)
    private int _frameUserX, _frameUserY;
    private float _framePixelOffsetX, _framePixelOffsetY;
    private int _frameMinX, _frameMaxX, _frameMinY, _frameMaxY;
    private bool _frameHasLights;

    public void Init(GameState state, GameData data, GrhAnimator animator)
    {
        _state = state;
        _data = data;
        _animator = animator;

        var additiveMat = new CanvasItemMaterial
        {
            BlendMode = CanvasItemMaterial.BlendModeEnum.Add
        };

        // Aura layer: additive blend, z=0 (first child after parent terrain)
        _auraLayer = new AuraAdditiveLayer();
        _auraLayer.Name = "AuraLayer";
        _auraLayer.Material = additiveMat;
        _auraLayer.ZIndex = 0;
        _auraLayer.SetRenderer(this);
        AddChild(_auraLayer);

        // Content layer: standard blend, z=1 (characters + objects + layer 3)
        _contentLayer = new ContentLayer();
        _contentLayer.Name = "ContentLayer";
        _contentLayer.ZIndex = 1;
        _contentLayer.SetRenderer(this);
        AddChild(_contentLayer);

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
        float r = _state!.MapColorR / 255f;
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
    /// Main _Draw: renders PASS 1+2 (terrain) and computes per-frame data for child layers.
    /// Child layers (auras, content, particles, roof) draw in their own _Draw() after this.
    /// </summary>
    public override void _Draw()
    {
        if (_state == null || _data == null || _animator == null) return;
        if (_state.MapData == null || _state.Paused) return;

        // Clear pending draws from previous frame
        _pendingMapParticleDraws.Clear();
        _pendingCharParticleDraws.Clear();
        _pendingAuraDraws.Clear();
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

        _frameHasLights = _state.MapLights.Count > 0 && _state.TileLightColors != null;

        // ==========================================
        // PASS 1: Layer 1 (Ground) — visible area +2 tile margin
        // ==========================================
        int l1MinX = Math.Max(1, screenMinX - 2);
        int l1MaxX = Math.Min(100, screenMaxX + 2);
        int l1MinY = Math.Max(1, screenMinY - 2);
        int l1MaxY = Math.Min(100, screenMaxY + 2);

        for (int y = l1MinY; y <= l1MaxY; y++)
        {
            for (int x = l1MinX; x <= l1MaxX; x++)
            {
                ref var tile = ref _state.MapData.Tiles[x, y];
                if (tile.Layer1 <= 0) continue;

                Vector2 pos = TileToScreen(x, y, _frameUserX, _frameUserY, _framePixelOffsetX, _framePixelOffsetY);
                if (_frameHasLights)
                {
                    Color lightColor = LightSystem.GetTileLight(_state, x, y);
                    DrawTileGrh(tile.Layer1, pos, center: false, modulate: lightColor);
                }
                else
                {
                    DrawTileGrh(tile.Layer1, pos, center: false);
                }
            }
        }

        // ==========================================
        // PASS 2: Layer 2 — extended buffer range
        // ==========================================
        for (int y = _frameMinY; y <= _frameMaxY; y++)
        {
            for (int x = _frameMinX; x <= _frameMaxX; x++)
            {
                ref var tile = ref _state.MapData.Tiles[x, y];
                if (tile.Layer2 <= 0) continue;

                Vector2 pos = TileToScreen(x, y, _frameUserX, _frameUserY, _framePixelOffsetX, _framePixelOffsetY);
                if (_frameHasLights)
                {
                    Color lightColor = LightSystem.GetTileLight(_state, x, y);
                    DrawTileGrh(tile.Layer2, pos, center: false, modulate: lightColor);
                }
                else
                {
                    DrawTileGrh(tile.Layer2, pos, center: false);
                }
            }
        }

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
            // VB6: invisible chars skip ALL rendering (body, head, auras, everything)
            if (ch.Invisible && kvp.Key != _state.UserCharIndex) continue;
            // VB6: auras not drawn when Navegando or Invisible (clsDX8Engine.cls:2013)
            if (ch.Invisible || ch.Navigating) continue;

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
            CharRenderer.CollectAuraDraws(this, ch, new Vector2(charPx, charPy), headOffset, _data);
        }

        // Collect map particle draws for the additive layer
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

        // Collect roof draws
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
                    if (_frameHasLights)
                    {
                        Color tl = LightSystem.GetTileLight(_state, x, y);
                        _pendingRoofDraws.Add((tile.Layer4, pos, new Color(tl.R, tl.G, tl.B, roofA)));
                    }
                    else
                    {
                        _pendingRoofDraws.Add((tile.Layer4, pos, new Color(1, 1, 1, roofA)));
                    }
                }
            }
        }

        // Trigger child layer redraws
        _auraLayer?.QueueRedraw();
        _contentLayer?.QueueRedraw();
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

                Color tileLight = _frameHasLights ? LightSystem.GetTileLight(_state, x, y) : Colors.White;

                // Ground objects (apply same tree alpha as Layer 3)
                if (_state.GroundObjects.TryGetValue((x, y), out int objGrh) && objGrh > 0)
                {
                    bool objNearPlayer = IsTree(objGrh)
                                       && y > (_frameUserY - 2) && y < (_frameUserY + 7)
                                       && x > (_frameUserX - 4) && x < (_frameUserX + 4);
                    if (objNearPlayer)
                    {
                        Color objLight = new Color(tileLight.R, tileLight.G, tileLight.B, 120f / 255f);
                        DrawTileGrhTo(canvas, objGrh, tilePos, center: true, modulate: objLight);
                    }
                    else
                    {
                        DrawTileGrhTo(canvas, objGrh, tilePos, center: true, modulate: tileLight);
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
                                               _data, _animator, _deltaMs, _state, this);
                }

                // Layer 3 (trees/objects)
                if (tile.Layer3 > 0)
                {
                    bool nearPlayer = IsTree(tile.Layer3)
                                   && y > (_frameUserY - 2) && y < (_frameUserY + 7)
                                   && x > (_frameUserX - 4) && x < (_frameUserX + 4);
                    if (nearPlayer)
                    {
                        Color treeLight = new Color(tileLight.R, tileLight.G, tileLight.B, 120f / 255f);
                        DrawTileGrhTo(canvas, tile.Layer3, tilePos, center: true, modulate: treeLight);
                    }
                    else
                    {
                        DrawTileGrhTo(canvas, tile.Layer3, tilePos, center: true, modulate: tileLight);
                    }
                }
            }
        }

        // Status overlay (VB6: drawCounters — paralysis/invisibility bars + status icons)
        DrawStatusOverlayTo(canvas);
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
            DrawStatusIconTo(canvas, slot, 23611, -1, -1, "OCULTO",
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

        if (_state.SafeMode)
        {
            DrawStatusIconTo(canvas, slot, 0, -1, -1, "SEGURO",
                           new Color(0f, 1f, 0f));
            slot++;
        }

        if (_state.UserNavigating)
        {
            DrawStatusIconTo(canvas, slot, 0, -1, -1, "NAVEGANDO",
                           new Color(0.3f, 0.7f, 1f));
            slot++;
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
    /// Draw pending aura draws on a given canvas (used by AuraAdditiveLayer).
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
/// Child Node2D with additive blend material. Draws auras queued by WorldRenderer.
/// z_index=0 — draws after terrain but before ContentLayer (z=1).
/// </summary>
public partial class AuraAdditiveLayer : Node2D
{
    private WorldRenderer? _renderer;

    public void SetRenderer(WorldRenderer renderer)
    {
        _renderer = renderer;
    }

    public override void _Draw()
    {
        _renderer?.DrawPendingAuras(this);
    }
}

/// <summary>
/// Child Node2D for PASS 3 content: ground objects, characters, layer 3, status.
/// z_index=1 — draws after AuraLayer (z=0), before particles (z=2).
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
