#nullable enable
using System;
using Godot;
using AOWorldEditor.Data;

namespace AOWorldEditor.Editor;

public partial class MapViewport : Control
{
    private const int TileSize = 32;

    // Injected dependencies
    public MapData? Map;
    public GrhData[]? Grhs;
    public TextureManager? Textures;
    public EditorState? State;
    public UndoManager? Undo;
    public MapZoneData? ZoneData;
    public ParticleEngine? Particles;
    public Action? OnPendingAccept;   // Fire when user clicks ✓ on pending placement
    public Action? OnPendingCancel;   // Fire when user clicks ✗ on pending placement
    public Action? OnSelectionCompleted; // Fire when a drag-selection is released
    public Action? OnLightEditRequested; // Fire when double-click on light with Light tool
    public int[]? ObjGrhs;
    public int[]? NpcBodies;
    public int[]? NpcHeads;
    public int[]? NpcBodyGrhs;
    public int[]? NpcHeadOfsX;
    public int[]? NpcHeadOfsY;
    public int[]? HeadGrhs;
    public NpcDatabase? NpcDb;

    // Interaction state
    private bool _isPainting;
    private bool _isPanning;
    private bool _isSelecting;
    private bool _isDragging;
    private bool _isMovingSelection;   // drag-to-move selection bounds (no tile content move)
    private Vector2I _selMoveAnchor;   // tile where the move-selection drag started
    private int _selOrigX1, _selOrigY1, _selOrigX2, _selOrigY2; // bounds snapshot for move
    private bool _isResizingSelection; // drag edge to resize selection
    private int _resizeEdge;           // 0=top, 1=right, 2=bottom, 3=left
    private bool _spaceHeld;
    private Vector2 _panStart;
    private Vector2 _panCameraStart;
    private Vector2I _selectStart;
    private Vector2I _dragStart;
    private Vector2I _dragCurrent;
    private double _lastClickTime;
    private Vector2I _lastClickTile;

    private readonly System.Collections.Generic.HashSet<long> _paintedThisStroke = new();

    // Marching ants animation for selection
    private float _marchingAntsOffset;
    private const float MarchingAntsSpeed = 40f; // pixels per second
    private const float MarchingAntsDash = 6f;
    private const float MarchingAntsGap = 4f;

    // Mosaic handle drag (reposition multi-tile pattern)
    private bool _mosaicHandleDrag;
    private Vector2I _mosaicHandleDragStart;

    // Hand tool: deferred pan — distinguish click (select tile) from drag (pan)
    private bool _handClickPending;
    private Vector2 _handClickScreenPos;
    private const float HandPanThreshold = 5f;

    // Keyboard panning (WASD / Arrow keys)
    private bool _keyUp, _keyDown, _keyLeft, _keyRight;
    private const float KeyPanSpeed = 500f; // pixels per second

    // Auto-scroll when dragging selection near viewport edge
    private const float EdgePanMargin = 40f;  // pixels from edge to start panning
    private const float EdgePanSpeed = 400f;  // pixels per second

    // Move tool: live snapshot system
    private MapTile[,]? _moveSnapshot;   // Full map state before drag started
    private MapTile[,]? _moveBuffer;     // Tiles being moved (selection copy)
    private int _moveSelX1, _moveSelY1;  // Original selection top-left
    private int _moveSelW, _moveSelH;    // Selection dimensions

    private ParticleOverlay? _particleOverlay;
    private LightingSystem? _lighting;
    private bool _lightingDirty = true;
    private bool _occludersDirty = true;

    // Weather FX driven by the currently selected zone in ZonePanel
    public ZonePanel? ZonePanelRef;  // set by EditorMain after creation
    private readonly WeatherFx _weather = new();

    public override void _Ready()
    {
        _particleOverlay = new ParticleOverlay { Viewport = this };
        _particleOverlay.Material = new CanvasItemMaterial
        {
            BlendMode = CanvasItemMaterial.BlendModeEnum.Add
        };
        _particleOverlay.ZIndex = 1;
        _particleOverlay.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(_particleOverlay);

        // Native Godot 2D lighting (CanvasModulate + PointLight2D pool)
        _lighting = new LightingSystem(this);
    }

    /// <summary>Request a lightmap rebuild on the next draw.</summary>
    public void MarkLightmapDirty()
    {
        _lightingDirty = true;
    }

    /// <summary>Request an occluder rebuild on the next draw (call after loading a new map).</summary>
    public void MarkOccludersDirty()
    {
        _occludersDirty = true;
    }

    private static bool HasAnyLight(MapData map)
    {
        for (int y = 1; y <= map.Height; y++)
            for (int x = 1; x <= map.Width; x++)
                if (map.Tiles[x, y].HasLight) return true;
        return false;
    }

    private double _animTime; // ms, for tile animations

    public override void _Process(double delta)
    {
        _animTime += delta * 1000.0;

        // Step the particle simulation so streams animate (rain falls, fire flickers, etc.)
        if (Particles != null && State != null && State.ShowParticles)
            Particles.Update((float)delta);

        // Tick weather simulation (zone-driven, draw happens in _Draw)
        // Set flags BEFORE Update so Update spawns the right particles this same frame.
        ZoneInfo? weatherZone = null;
        if (ZonePanelRef != null && ZoneData != null)
        {
            int selId = ZonePanelRef.SelectedZoneId;
            if (selId >= 0)
                weatherZone = ZoneData.Zones.Find(z => z.Id == selId);
        }
        _weather.Lluvia = weatherZone?.Lluvia ?? false;
        _weather.Nieve  = weatherZone?.Nieve  ?? false;
        _weather.Niebla = weatherZone?.Niebla ?? false;
        _weather.Update((float)delta, Size);

        // Keyboard panning (WASD / Arrow keys)
        // Pressing W = view moves UP on map = tiles with lower Y become visible
        // = world content scrolls DOWN on screen = CameraOffset.Y increases
        if (State != null && (_keyUp || _keyDown || _keyLeft || _keyRight))
        {
            float step = KeyPanSpeed * (float)delta;
            if (_keyUp)    State.CameraOffset += new Vector2(0, step);
            if (_keyDown)  State.CameraOffset -= new Vector2(0, step);
            if (_keyLeft)  State.CameraOffset += new Vector2(step, 0);
            if (_keyRight) State.CameraOffset -= new Vector2(step, 0);
        }

        // Auto-scroll when dragging selection near viewport edge
        if (State != null && (_isSelecting || _isResizingSelection || _isMovingSelection || _isDragging))
        {
            var mousePos = GetLocalMousePosition();
            float step = EdgePanSpeed * (float)delta;
            if (mousePos.Y < EdgePanMargin)          State.CameraOffset += new Vector2(0, step);
            if (mousePos.Y > Size.Y - EdgePanMargin) State.CameraOffset -= new Vector2(0, step);
            if (mousePos.X < EdgePanMargin)          State.CameraOffset += new Vector2(step, 0);
            if (mousePos.X > Size.X - EdgePanMargin) State.CameraOffset -= new Vector2(step, 0);

            // Update drag target tile while auto-scrolling
            if (_isSelecting)
                _dragCurrent = ClampToMap(ScreenToTile(GetGlobalMousePosition()));
        }

        // Animate marching ants for selection
        if (State != null && (State.HasSelection || _isSelecting))
        {
            _marchingAntsOffset = (_marchingAntsOffset + (float)(MarchingAntsSpeed * delta))
                % (MarchingAntsDash + MarchingAntsGap);
        }
        QueueRedraw();
    }

    public override void _Draw()
    {
        DrawRect(new Rect2(Vector2.Zero, Size), EditorTheme.BG_DARK);

        if (Map == null || Grhs == null || Textures == null || State == null) return;

        // ── Native 2D lighting: ambient + per-tile PointLight2D ──
        if (_lighting != null && State.ShowLights)
        {
            // Zone ambient override: use hover-tile zone if non-zero, else map default
            byte ar = Map.AmbientR, ag = Map.AmbientG, ab = Map.AmbientB;
            if (ZoneData != null && State.HoverX > 0 && State.HoverY > 0)
            {
                var hoverZone = ZoneData.GetZoneAt(State.HoverX, State.HoverY);
                if (hoverZone != null && (hoverZone.AmbientR != 0 || hoverZone.AmbientG != 0 || hoverZone.AmbientB != 0))
                {
                    ar = (byte)Mathf.Clamp(hoverZone.AmbientR, 0, 255);
                    ag = (byte)Mathf.Clamp(hoverZone.AmbientG, 0, 255);
                    ab = (byte)Mathf.Clamp(hoverZone.AmbientB, 0, 255);
                }
            }
            _lighting.SetAmbient(new Color(ar / 255f, ag / 255f, ab / 255f, 1f));
            _lighting.SetEnabled(true);
            if (_lightingDirty)
            {
                _lighting.Rebuild(Map);
                _lightingDirty = false;
            }
            if (_occludersDirty)
            {
                _lighting.RebuildOccluders(Map);
                _occludersDirty = false;
            }
            // Sync the light container's transform with the camera/zoom each frame
            _lighting.SetWorldTransform(State.CameraOffset, State.Zoom);
        }
        else
        {
            _lighting?.SetEnabled(false);
        }

        DrawSetTransform(State.CameraOffset, 0f, new Vector2(State.Zoom, State.Zoom));

        int mapW = Map.Width;
        int mapH = Map.Height;

        // Viewport culling: only draw tiles visible on screen
        float invZoom = 1f / Math.Max(State.Zoom, 0.01f);
        int minX = Math.Max(1, (int)((-State.CameraOffset.X * invZoom) / TileSize));
        int minY = Math.Max(1, (int)((-State.CameraOffset.Y * invZoom) / TileSize));
        int maxX = Math.Min(mapW, minX + (int)(Size.X * invZoom / TileSize) + 2);
        int maxY = Math.Min(mapH, minY + (int)(Size.Y * invZoom / TileSize) + 2);

        // LOD: at low zoom levels, skip tiles to keep draw calls under budget.
        // Each tile at zoom 0.02 is ~0.64px — drawing every tile wastes GPU.
        // Step=1 up to ~200 visible tiles per axis, then increase step.
        int visibleTilesX = maxX - minX;
        int visibleTilesY = maxY - minY;
        int maxVisPerAxis = 200;
        int step = 1;
        if (visibleTilesX > maxVisPerAxis || visibleTilesY > maxVisPerAxis)
            step = Math.Max(visibleTilesX, visibleTilesY) / maxVisPerAxis + 1;

        // Align minX/minY to step grid for stable sampling during pan
        if (step > 1)
        {
            minX = (minX / step) * step;
            minY = (minY / step) * step;
        }

        // Layer 1: Ground
        if (State.ShowLayer1)
            for (int y = minY; y <= maxY; y += step)
                for (int x = minX; x <= maxX; x += step)
                    DrawTileGrh(Map.Tiles[x, y].Layer1, x, y);

        // Layer 2: Mask
        if (State.ShowLayer2)
            for (int y = minY; y <= maxY; y += step)
                for (int x = minX; x <= maxX; x += step)
                    if (Map.Tiles[x, y].Layer2 != 0)
                        DrawTileGrh(Map.Tiles[x, y].Layer2, x, y, center: true);

        // Layer 3: Objects/trees
        if (State.ShowLayer3)
            for (int y = minY; y <= maxY; y += step)
                for (int x = minX; x <= maxX; x += step)
                    if (Map.Tiles[x, y].Layer3 != 0)
                        DrawTileGrh(Map.Tiles[x, y].Layer3, x, y, center: true);

        // Objects on map
        if (State.ShowObjects && ObjGrhs != null)
            for (int y = minY; y <= maxY; y += step)
                for (int x = minX; x <= maxX; x += step)
                {
                    int objIdx = Map.Tiles[x, y].ObjIndex;
                    if (objIdx > 0 && objIdx < ObjGrhs.Length && ObjGrhs[objIdx] > 0)
                        DrawTileGrh(ObjGrhs[objIdx], x, y, center: true);
                }

        // NPCs on map — always draw all (sparse, no LOD skip)
        if (State.ShowNpcs && NpcBodies != null && NpcBodyGrhs != null)
            for (int y = minY; y <= maxY; y++)
                for (int x = minX; x <= maxX; x++)
                {
                    int npcIdx = Map.Tiles[x, y].NpcIndex;
                    if (npcIdx <= 0 || npcIdx >= NpcBodies.Length) continue;
                    int bodyIdx = NpcBodies[npcIdx];
                    if (bodyIdx <= 0 || bodyIdx >= NpcBodyGrhs.Length) continue;
                    int bodyGrh = NpcBodyGrhs[bodyIdx];
                    if (bodyGrh > 0)
                        DrawTileGrh(bodyGrh, x, y, center: true, animate: false);
                    if (NpcHeads != null && HeadGrhs != null &&
                        NpcHeadOfsX != null && NpcHeadOfsY != null &&
                        npcIdx < NpcHeads.Length)
                    {
                        int headIdx = NpcHeads[npcIdx];
                        if (headIdx > 0 && headIdx < HeadGrhs.Length)
                        {
                            int headGrh = HeadGrhs[headIdx];
                            if (headGrh > 0)
                            {
                                int ofsX = bodyIdx < NpcHeadOfsX.Length ? NpcHeadOfsX[bodyIdx] : 0;
                                int ofsY = bodyIdx < NpcHeadOfsY.Length ? NpcHeadOfsY[bodyIdx] : 0;
                                DrawTileGrhOffset(headGrh, x, y, ofsX, ofsY, animate: false);
                            }
                        }
                    }
                }

        // Layer 4: Roof
        if (State.ShowLayer4)
            for (int y = minY; y <= maxY; y += step)
                for (int x = minX; x <= maxX; x += step)
                    if (Map.Tiles[x, y].Layer4 != 0)
                        DrawTileGrh(Map.Tiles[x, y].Layer4, x, y, center: true,
                            modulate: new Color(1, 1, 1, 0.7f));

        // Paint tool: ghost preview at cursor position (Sims-style)
        DrawPaintPreview();

        // NPC/Object tool: ghost preview at cursor
        DrawEntityPreview();

        // Pending placement: floating preview with accept/cancel buttons
        DrawPendingPlacement();

        // Overlays
        DrawOverlays(mapW, mapH);

        // Move tool: selection outline at current drag position (map is live-modified)
        if (_isDragging && State.HasSelection)
        {
            var delta = _dragCurrent - _dragStart;
            var moveRect = new Rect2(
                (_moveSelX1 + delta.X) * TileSize, (_moveSelY1 + delta.Y) * TileSize,
                _moveSelW * TileSize, _moveSelH * TileSize);
            DrawRect(moveRect, new Color(0.2f, 1f, 0.5f, 0.08f));
            DrawMarchingAnts(moveRect, new Color(0.3f, 1f, 0.6f, 0.85f));
        }

        // Pick tool: highlight source + ghost at drag position
        DrawPickOverlay();

        DrawSetTransform(Vector2.Zero, 0f, Vector2.One);

        // Particles
        if (_particleOverlay != null)
        {
            _particleOverlay.Visible = State.ShowParticles;
            if (State.ShowParticles)
                _particleOverlay.QueueRedraw();
        }

        // Weather FX: flags are set in _Process before Update; just draw here.
        _weather.Draw(this, Size);
    }

    /// <summary>
    /// Live move: restore map from snapshot, clear source, place buffer at current drag position.
    /// Called on drag start and each drag motion update.
    /// </summary>
    private void ApplyLiveMove()
    {
        if (Map == null || _moveSnapshot == null || _moveBuffer == null) return;

        // Restore entire map from snapshot
        Array.Copy(_moveSnapshot, Map.Tiles, Map.Tiles.Length);

        var delta = _dragCurrent - _dragStart;

        // Clear source area
        for (int y = 0; y < _moveSelH; y++)
            for (int x = 0; x < _moveSelW; x++)
            {
                int sx = _moveSelX1 + x, sy = _moveSelY1 + y;
                if (Map.InBounds(sx, sy))
                    Map.Tiles[sx, sy] = new MapTile { Layer1 = 1 };
            }

        // Place buffer at destination
        for (int y = 0; y < _moveSelH; y++)
            for (int x = 0; x < _moveSelW; x++)
            {
                int dstX = _moveSelX1 + x + delta.X, dstY = _moveSelY1 + y + delta.Y;
                if (Map.InBounds(dstX, dstY))
                    Map.Tiles[dstX, dstY] = _moveBuffer[x, y];
            }
    }

    /// <summary>
    /// Get the GRH index for a tile position using the mosaic formula with user offset.
    /// </summary>
    private int GetMosaicGrh(TextureRef texRef, int tileX, int tileY)
    {
        int tw = Math.Max(texRef.TileWidth, 1);
        int th = Math.Max(texRef.TileHeight, 1);
        int offX = State!.MosaicOffsetX;
        int offY = State.MosaicOffsetY;
        int px = (((tileX - 1 - offX) % tw) + tw) % tw;
        int py = (((tileY - 1 - offY) % th) + th) % th;
        return texRef.GrhIndex + py * tw + px;
    }

    /// <summary>
    /// Get the top-left tile of the pattern instance nearest to the given tile.
    /// </summary>
    private (int baseX, int baseY) GetMosaicBase(int hoverX, int hoverY, TextureRef texRef)
    {
        int tw = Math.Max(texRef.TileWidth, 1);
        int th = Math.Max(texRef.TileHeight, 1);
        int offX = State!.MosaicOffsetX;
        int offY = State.MosaicOffsetY;
        int curPx = (((hoverX - 1 - offX) % tw) + tw) % tw;
        int curPy = (((hoverY - 1 - offY) % th) + th) % th;
        return (hoverX - curPx, hoverY - curPy);
    }

    /// <summary>
    /// Auto-detect mosaic offset: if the hovered tile already has a GRH from the selected
    /// multi-tile pattern, adjust MosaicOffset so the preview aligns with existing tiles.
    /// </summary>
    private void TryAutoAlignMosaic(int hoverX, int hoverY)
    {
        if (State?.SelectedTexture == null || Map == null) return;
        var texRef = State.SelectedTexture;
        int tw = Math.Max(texRef.TileWidth, 1);
        int th = Math.Max(texRef.TileHeight, 1);
        if (tw <= 1 && th <= 1) return; // only for multi-tile patterns

        if (!Map.InBounds(hoverX, hoverY)) return;

        int layer = State.ActiveLayer;
        int tileGrh = layer switch
        {
            1 => Map.Tiles[hoverX, hoverY].Layer1,
            2 => Map.Tiles[hoverX, hoverY].Layer2,
            3 => Map.Tiles[hoverX, hoverY].Layer3,
            4 => Map.Tiles[hoverX, hoverY].Layer4,
            _ => 0
        };
        if (tileGrh <= 0) return;

        // Check if the tile's GRH is part of this mosaic pattern
        int baseGrh = texRef.GrhIndex;
        int idx = tileGrh - baseGrh;
        if (idx < 0 || idx >= tw * th) return; // not part of this pattern

        // Found a match. idx = py * tw + px
        int px = idx % tw;
        int py = idx / tw;

        // The tile at (hoverX, hoverY) should be at pattern position (px, py).
        // Pattern base = (hoverX - px, hoverY - py).
        // offX = (hoverX - 1 - px) mod tw, offY = (hoverY - 1 - py) mod th
        int newOffX = ((hoverX - 1 - px) % tw + tw) % tw;
        int newOffY = ((hoverY - 1 - py) % th + th) % th;

        if (newOffX != State.MosaicOffsetX || newOffY != State.MosaicOffsetY)
        {
            State.MosaicOffsetX = newOffX;
            State.MosaicOffsetY = newOffY;
        }
    }

    /// <summary>
    /// Draw a semi-transparent preview of the selected texture at the cursor position.
    /// Like Sims construction mode — shows what will be placed before clicking.
    /// </summary>
    private void DrawPaintPreview()
    {
        if (State == null || Map == null || _isDragging) return;
        if (State.Pending.Active) return; // Don't show paint preview during pending placement
        if (State.ActiveTool != EditorTool.Paint && !_mosaicHandleDrag) return;
        if (!State.HoverValid && !_mosaicHandleDrag) return;

        int hx = State.HoverX, hy = State.HoverY;
        if (!Map.InBounds(hx, hy) && !_mosaicHandleDrag) return;

        var previewColor = new Color(1, 1, 1, 0.55f);
        bool centerOnTile = State.ActiveLayer >= 2;

        if (State.SelectedTexture != null)
        {
            var texRef = State.SelectedTexture;
            int tw = Math.Max(texRef.TileWidth, 1);
            int th = Math.Max(texRef.TileHeight, 1);

            if (tw == 1 && th == 1)
            {
                // Single tile preview
                DrawTileGrh(texRef.GrhIndex, hx, hy, center: centerOnTile, modulate: previewColor);
            }
            else
            {
                // Multi-tile mosaic with user-adjustable offset
                var (baseX, baseY) = GetMosaicBase(hx, hy, texRef);

                // Draw pattern preview
                for (int py = 0; py < th; py++)
                    for (int px = 0; px < tw; px++)
                    {
                        int tx = baseX + px;
                        int ty = baseY + py;
                        if (!Map.InBounds(tx, ty)) continue;
                        int grhIdx = texRef.GrhIndex + (py * tw) + px;
                        DrawTileGrh(grhIdx, tx, ty, center: centerOnTile, modulate: previewColor);
                    }

                // Pattern outline
                var patternRect = new Rect2(
                    baseX * TileSize, baseY * TileSize,
                    tw * TileSize, th * TileSize);
                DrawRect(patternRect, new Color(1f, 1f, 0.3f, 0.3f), false, 1.5f);

                // Draggable handle at top-left corner (move pattern)
                float hs = TileSize * 0.35f;
                float hpx = baseX * TileSize + 1;
                float hpy = baseY * TileSize + 1;
                var handleBg = _mosaicHandleDrag
                    ? new Color(1f, 0.4f, 0.1f, 0.9f)
                    : new Color(1f, 0.85f, 0.2f, 0.85f);
                DrawRect(new Rect2(hpx, hpy, hs, hs), handleBg);
                DrawRect(new Rect2(hpx, hpy, hs, hs), new Color(0, 0, 0, 0.5f), false, 1f);
                float cx = hpx + hs / 2f, cy = hpy + hs / 2f;
                float ar = hs * 0.3f;
                var arrowCol = new Color(0, 0, 0, 0.7f);
                DrawLine(new Vector2(cx - ar, cy), new Vector2(cx + ar, cy), arrowCol, 1.5f);
                DrawLine(new Vector2(cx, cy - ar), new Vector2(cx, cy + ar), arrowCol, 1.5f);

                // Fill/stamp button at top-right corner
                float fpx = (baseX + tw) * TileSize - hs - 1;
                float fpy = baseY * TileSize + 1;
                var fillBg = new Color(0.3f, 0.85f, 0.4f, 0.85f); // green
                DrawRect(new Rect2(fpx, fpy, hs, hs), fillBg);
                DrawRect(new Rect2(fpx, fpy, hs, hs), new Color(0, 0, 0, 0.5f), false, 1f);
                // Fill icon: small filled square inside
                float fi = hs * 0.25f;
                DrawRect(new Rect2(fpx + fi, fpy + fi, hs - fi * 2, hs - fi * 2),
                    new Color(0, 0, 0, 0.6f));
            }
        }
        else if (State.EyedropGrh > 0)
        {
            // Single raw GRH preview
            DrawTileGrh(State.EyedropGrh, hx, hy, center: centerOnTile, modulate: previewColor);
        }
    }

    /// <summary>
    /// Draw the pending placement as a semi-transparent overlay with ✓ (accept) and ✗ (cancel) buttons.
    /// The user can drag the placement to reposition it before committing.
    /// </summary>
    private void DrawPendingPlacement()
    {
        if (State == null || Map == null || !State.Pending.Active || State.Pending.Tiles == null) return;

        var p = State.Pending;
        var previewColor = new Color(1, 1, 1, 0.6f);

        // Draw each tile of the pending buffer at current origin
        for (int py = 0; py < p.Height; py++)
            for (int px = 0; px < p.Width; px++)
            {
                int tx = p.OriginX + px, ty = p.OriginY + py;
                if (!Map.InBounds(tx, ty)) continue;
                ref var t = ref p.Tiles[px, py];
                if (t.Layer1 > 0) DrawTileGrh(t.Layer1, tx, ty, modulate: previewColor);
                if (t.Layer2 > 0) DrawTileGrh(t.Layer2, tx, ty, center: true, modulate: previewColor);
                if (t.Layer3 > 0) DrawTileGrh(t.Layer3, tx, ty, center: true, modulate: previewColor);
                if (t.Layer4 > 0) DrawTileGrh(t.Layer4, tx, ty, center: true, modulate: new Color(1, 1, 1, 0.4f));
            }

        // ── Overlays on pending tiles (blocked, exits, NPCs, objects) ──
        for (int py = 0; py < p.Height; py++)
            for (int px = 0; px < p.Width; px++)
            {
                int tx = p.OriginX + px, ty = p.OriginY + py;
                if (!Map.InBounds(tx, ty)) continue;
                ref var t = ref p.Tiles[px, py];
                float bx = tx * TileSize;
                float by = ty * TileSize;

                // Blocked: red hatching
                if (t.Blocked)
                {
                    DrawRect(new Rect2(bx, by, TileSize, TileSize), new Color(1f, 0f, 0f, 0.12f));
                    DrawLine(new Vector2(bx, by + TileSize), new Vector2(bx + TileSize, by),
                        new Color(1f, 0.2f, 0.2f, 0.5f), 0.8f);
                    DrawLine(new Vector2(bx, by), new Vector2(bx + TileSize, by + TileSize),
                        new Color(1f, 0.2f, 0.2f, 0.5f), 0.8f);
                }

                // Exit: green arrow + map label
                if (t.HasExit)
                {
                    DrawRect(new Rect2(bx + 1, by + 1, TileSize - 2, TileSize - 2),
                        new Color(0.1f, 0.8f, 0.3f, 0.15f));
                    var center = new Vector2(bx + TileSize * 0.5f, by + TileSize * 0.35f);
                    var arrowCol = new Color(0.4f, 1f, 0.5f, 0.7f);
                    DrawLine(center, center + new Vector2(0, 6), arrowCol, 1.5f);
                    DrawLine(center, center + new Vector2(-3, 3), arrowCol, 1.5f);
                    DrawLine(center, center + new Vector2(3, 3), arrowCol, 1.5f);
                    DrawOverlayPill(bx + 1, by + TileSize - 12,
                        $"M{t.ExitMap}", new Color(0.1f, 0.5f, 0.2f, 0.85f),
                        new Color(0.5f, 1f, 0.5f, 0.95f), 7);
                }

                // NPC: orange dot
                if (t.HasNpc)
                {
                    var npcCenter = new Vector2(bx + TileSize * 0.5f, by + TileSize * 0.5f);
                    DrawCircle(npcCenter, TileSize * 0.18f, new Color(1f, 0.6f, 0.2f, 0.6f));
                    DrawOverlayPill(bx + 1, by + 1,
                        $"N{t.NpcIndex}", new Color(0.6f, 0.3f, 0.05f, 0.85f),
                        new Color(1f, 0.7f, 0.3f, 0.95f), 7);
                }

                // Object: purple dot
                if (t.HasObject)
                {
                    var objCenter = new Vector2(bx + TileSize * 0.5f, by + TileSize * 0.7f);
                    DrawCircle(objCenter, TileSize * 0.12f, new Color(0.7f, 0.3f, 1f, 0.5f));
                }
            }

        // Outline around the placement area (marching ants)
        var placementRect = new Rect2(
            p.OriginX * TileSize, p.OriginY * TileSize,
            p.Width * TileSize, p.Height * TileSize);
        DrawRect(placementRect, new Color(0.2f, 0.7f, 1f, 0.08f));
        DrawMarchingAnts(placementRect, new Color(0.3f, 0.85f, 1f, 0.9f));

        // Button dimensions
        float btnW = TileSize * 0.7f;
        float btnH = TileSize * 0.6f;
        float margin = 4f;
        float shadowOfs = 1.5f;

        // ── Accept button (green, top-right) ──
        _pendingAcceptRect = new Rect2(
            (p.OriginX + p.Width) * TileSize - btnW - margin,
            p.OriginY * TileSize - btnH - margin,
            btnW, btnH);
        // Shadow
        DrawRect(new Rect2(_pendingAcceptRect.Position + new Vector2(shadowOfs, shadowOfs),
            _pendingAcceptRect.Size), new Color(0, 0, 0, 0.35f));
        // Background
        DrawRect(_pendingAcceptRect, new Color(0.18f, 0.65f, 0.28f, 0.95f));
        DrawRect(_pendingAcceptRect, new Color(0.3f, 0.9f, 0.4f, 0.4f), false, 1f);
        // Checkmark icon (clean vector)
        float acx = _pendingAcceptRect.Position.X + btnW * 0.5f;
        float acy = _pendingAcceptRect.Position.Y + btnH * 0.5f;
        float acs = btnH * 0.3f;
        DrawLine(new Vector2(acx - acs * 0.8f, acy),
                 new Vector2(acx - acs * 0.1f, acy + acs * 0.65f), Colors.White, 2.2f);
        DrawLine(new Vector2(acx - acs * 0.1f, acy + acs * 0.65f),
                 new Vector2(acx + acs, acy - acs * 0.5f), Colors.White, 2.2f);

        // ── Cancel button (red, top-left) ──
        _pendingCancelRect = new Rect2(
            p.OriginX * TileSize + margin,
            p.OriginY * TileSize - btnH - margin,
            btnW, btnH);
        // Shadow
        DrawRect(new Rect2(_pendingCancelRect.Position + new Vector2(shadowOfs, shadowOfs),
            _pendingCancelRect.Size), new Color(0, 0, 0, 0.35f));
        // Background
        DrawRect(_pendingCancelRect, new Color(0.7f, 0.18f, 0.18f, 0.95f));
        DrawRect(_pendingCancelRect, new Color(1f, 0.35f, 0.35f, 0.4f), false, 1f);
        // X icon (clean vector cross)
        float ccx = _pendingCancelRect.Position.X + btnW * 0.5f;
        float ccy = _pendingCancelRect.Position.Y + btnH * 0.5f;
        float ccs = btnH * 0.22f;
        DrawLine(new Vector2(ccx - ccs, ccy - ccs), new Vector2(ccx + ccs, ccy + ccs), Colors.White, 2.2f);
        DrawLine(new Vector2(ccx + ccs, ccy - ccs), new Vector2(ccx - ccs, ccy + ccs), Colors.White, 2.2f);

        // ── Drag handle (amber, center-top) with 4-way arrow ──
        float dhx = placementRect.Position.X + placementRect.Size.X / 2 - btnW / 2;
        float dhy = p.OriginY * TileSize - btnH - margin;
        var handleRect = new Rect2(dhx, dhy, btnW, btnH);
        // Shadow
        DrawRect(new Rect2(handleRect.Position + new Vector2(shadowOfs, shadowOfs),
            handleRect.Size), new Color(0, 0, 0, 0.35f));
        // Background
        DrawRect(handleRect, new Color(0.75f, 0.6f, 0.15f, 0.95f));
        DrawRect(handleRect, new Color(1f, 0.9f, 0.4f, 0.4f), false, 1f);
        // 4-way arrow icon
        float hcx = dhx + btnW / 2, hcy = dhy + btnH / 2;
        float ar = btnH * 0.28f;
        float arrowHead = ar * 0.35f;
        var arrCol = new Color(0, 0, 0, 0.8f);
        // Horizontal axis
        DrawLine(new Vector2(hcx - ar, hcy), new Vector2(hcx + ar, hcy), arrCol, 1.5f);
        // Left arrowhead
        DrawLine(new Vector2(hcx - ar, hcy), new Vector2(hcx - ar + arrowHead, hcy - arrowHead), arrCol, 1.5f);
        DrawLine(new Vector2(hcx - ar, hcy), new Vector2(hcx - ar + arrowHead, hcy + arrowHead), arrCol, 1.5f);
        // Right arrowhead
        DrawLine(new Vector2(hcx + ar, hcy), new Vector2(hcx + ar - arrowHead, hcy - arrowHead), arrCol, 1.5f);
        DrawLine(new Vector2(hcx + ar, hcy), new Vector2(hcx + ar - arrowHead, hcy + arrowHead), arrCol, 1.5f);
        // Vertical axis
        DrawLine(new Vector2(hcx, hcy - ar), new Vector2(hcx, hcy + ar), arrCol, 1.5f);
        // Up arrowhead
        DrawLine(new Vector2(hcx, hcy - ar), new Vector2(hcx - arrowHead, hcy - ar + arrowHead), arrCol, 1.5f);
        DrawLine(new Vector2(hcx, hcy - ar), new Vector2(hcx + arrowHead, hcy - ar + arrowHead), arrCol, 1.5f);
        // Down arrowhead
        DrawLine(new Vector2(hcx, hcy + ar), new Vector2(hcx - arrowHead, hcy + ar - arrowHead), arrCol, 1.5f);
        DrawLine(new Vector2(hcx, hcy + ar), new Vector2(hcx + arrowHead, hcy + ar - arrowHead), arrCol, 1.5f);
    }

    // Cached button rects for click detection (world-space coords set during _Draw)
    private Rect2 _pendingAcceptRect;
    private Rect2 _pendingCancelRect;

    // Pending placement dragging state
    private bool _pendingDrag;
    private Vector2I _pendingDragStart;

    /// <summary>
    /// Check if a screen-space click hits the pending accept button.
    /// </summary>
    private bool IsPendingAcceptClick(Vector2 screenPos)
    {
        var world = ScreenToWorld(screenPos);
        return _pendingAcceptRect.HasPoint(world);
    }

    /// <summary>
    /// Check if a screen-space click hits the pending cancel button.
    /// </summary>
    private bool IsPendingCancelClick(Vector2 screenPos)
    {
        var world = ScreenToWorld(screenPos);
        return _pendingCancelRect.HasPoint(world);
    }

    private void DrawPickOverlay()
    {
        if (State == null || !State.Pick.HasPick) return;
        var pick = State.Pick;

        // Highlight source tile with yellow outline
        var srcRect = new Rect2(pick.SourceX * TileSize, pick.SourceY * TileSize, TileSize, TileSize);
        DrawRect(srcRect, new Color(1f, 0.9f, 0.2f, 0.25f));
        DrawRect(srcRect, new Color(1f, 0.9f, 0.2f, 0.8f), false, 2f);

        // Ghost of entity at drag position
        if (pick.IsDragging && (pick.DragX != pick.SourceX || pick.DragY != pick.SourceY))
        {
            var ghostColor = new Color(1, 1, 1, 0.5f);
            if (pick.Target == PickTarget.Npc && Map != null && Map.InBounds(pick.SourceX, pick.SourceY))
            {
                DrawNpcFull(Map.Tiles[pick.SourceX, pick.SourceY].NpcIndex, pick.DragX, pick.DragY, ghostColor);
            }
            else
            {
                int grh = GetPickGrh();
                if (grh > 0)
                    DrawTileGrh(grh, pick.DragX, pick.DragY, center: true, modulate: ghostColor);
            }

            // Destination outline
            var dstRect = new Rect2(pick.DragX * TileSize, pick.DragY * TileSize, TileSize, TileSize);
            DrawRect(dstRect, new Color(0.2f, 1f, 0.5f, 0.3f));
            DrawRect(dstRect, new Color(0.2f, 1f, 0.5f, 0.7f), false, 2f);
        }
    }

    /// <summary>Draw ghost preview when NPC or Object tool is active and cursor hovers over map.</summary>
    private void DrawEntityPreview()
    {
        if (State == null || !State.HoverValid) return;
        int hx = State.HoverX, hy = State.HoverY;
        var previewColor = new Color(1, 1, 1, 0.5f);

        if (State.ActiveTool == EditorTool.Npc && State.SelectedNpcNumber > 0)
        {
            DrawNpcFull(State.SelectedNpcNumber, hx, hy, previewColor);
        }
        else if (State.ActiveTool == EditorTool.Object && State.SelectedObjectNumber > 0)
        {
            int grh = GetObjGrh(State.SelectedObjectNumber);
            if (grh > 0)
                DrawTileGrh(grh, hx, hy, center: true, modulate: previewColor);
        }
        else if (State.ActiveTool == EditorTool.Light && State.LightRange > 0)
        {
            var center = new Vector2((hx + 0.5f) * TileSize, (hy + 0.5f) * TileSize);
            float radius = State.LightRange * TileSize * 0.5f;
            var lightCol = new Color(State.LightR / 255f, State.LightG / 255f, State.LightB / 255f);
            DrawCircle(center, Math.Max(radius, TileSize * 0.4f),
                new Color(lightCol.R, lightCol.G, lightCol.B, 0.15f));
            DrawCircle(center, 5f, new Color(lightCol.R, lightCol.G, lightCol.B, 0.7f));
            if (radius > TileSize * 0.5f)
                DrawArc(center, radius, 0, MathF.Tau, 32,
                    new Color(lightCol.R, lightCol.G, lightCol.B, 0.4f), 1.5f);
        }
        else if (State.ActiveTool == EditorTool.Particle && State.SelectedParticleGroup > 0)
        {
            // Particle cursor preview: colored diamond + group number
            var center = new Vector2((hx + 0.5f) * TileSize, (hy + 0.5f) * TileSize);
            var pCol = new Color(0.3f, 1f, 0.9f, 0.5f);
            float s = TileSize * 0.35f;
            DrawCircle(center, s, pCol with { A = 0.2f });
            DrawCircle(center, 4f, pCol with { A = 0.8f });
            DrawArc(center, s, 0, MathF.Tau, 16, pCol with { A = 0.6f }, 1.5f);
            var font = ThemeDB.Singleton.FallbackFont;
            string label = $"P{State.SelectedParticleGroup}";
            DrawString(font, new Vector2(hx * TileSize + 2, hy * TileSize + TileSize - 3),
                label, HorizontalAlignment.Left, -1, 9, new Color(0.3f, 1f, 0.9f, 0.9f));
        }
        else if (State.ActiveTool == EditorTool.Trigger)
        {
            // Show a preview of the trigger color that will be painted
            short trigType = State.SelectedTriggerType;
            var (trigColor, trigName) = trigType switch
            {
                1 => (new Color(0.5f, 0.5f, 0.5f, 0.45f), "Indoor"),
                3 => (new Color(0.8f, 0.2f, 0.2f, 0.45f), "InvPos"),
                4 => (new Color(0, 0.7f, 1, 0.45f), "Safe"),
                5 => (new Color(0.8f, 0.8f, 0, 0.45f), "AntiBl"),
                6 => (new Color(1, 0, 0, 0.4f), "Combat"),
                0 => (new Color(0.3f, 0.3f, 0.3f, 0.35f), "Borrar"),
                _ => (new Color(1, 1, 0, 0.4f), $"T{trigType}"),
            };
            DrawRect(new Rect2(hx * TileSize + 1, hy * TileSize + 1, TileSize - 2, TileSize - 2), trigColor);
            DrawRect(new Rect2(hx * TileSize + 0.5f, hy * TileSize + 0.5f, TileSize - 1, TileSize - 1),
                trigColor with { A = 0.85f }, false, 2f);
            DrawOverlayPill(hx * TileSize + 1, hy * TileSize + 1,
                trigName, trigColor with { A = 0.75f },
                new Color(trigColor.R, trigColor.G, trigColor.B, 0.95f), 6);
        }
    }

    private void DrawOverlays(int mapW, int mapH)
    {
        if (State == null || Map == null) return;

        // Viewport culling bounds (same as tile rendering)
        float invZoom = 1f / Math.Max(State.Zoom, 0.01f);
        int ovMinX = Math.Max(1, (int)((-State.CameraOffset.X * invZoom) / TileSize));
        int ovMinY = Math.Max(1, (int)((-State.CameraOffset.Y * invZoom) / TileSize));
        int ovMaxX = Math.Min(mapW, ovMinX + (int)(Size.X * invZoom / TileSize) + 2);
        int ovMaxY = Math.Min(mapH, ovMinY + (int)(Size.Y * invZoom / TileSize) + 2);

        // Skip detailed overlays at low zoom — too many draw calls, not visible anyway
        int overlayTiles = (ovMaxX - ovMinX) * (ovMaxY - ovMinY);
        bool skipDetailedOverlays = overlayTiles > 40000; // ~200x200

        // ── Zone overlays (semi-transparent colored rectangles) ──
        if (ZoneData != null)
        {
            foreach (var zone in ZoneData.Zones)
            {
                // Zone bounds are 1-based
                float zx = zone.X1 * TileSize;
                float zy = zone.Y1 * TileSize;
                float zw = (zone.X2 - zone.X1 + 1) * TileSize;
                float zh = (zone.Y2 - zone.Y1 + 1) * TileSize;

                // Fill
                var fillColor = ZonePanel.GetZoneTypeColor(zone.Type);
                fillColor.A = 0.12f;
                DrawRect(new Rect2(zx, zy, zw, zh), fillColor);

                // Border
                var borderColor = ZonePanel.GetZoneTypeColor(zone.Type);
                borderColor.A = 0.6f;
                DrawRect(new Rect2(zx, zy, zw, zh), borderColor, false, 2f);

                // Name label (centered in zone)
                if (State.Zoom >= 0.3f && zone.Name.Length > 0)
                {
                    var font = ThemeDB.FallbackFont;
                    int fontSize = Math.Max(10, (int)(14 / State.Zoom));
                    var labelColor = ZonePanel.GetZoneTypeColor(zone.Type);
                    labelColor.A = 0.8f;
                    var labelPos = new Vector2(zx + zw / 2f, zy + zh / 2f);
                    DrawString(font, labelPos, zone.Name, HorizontalAlignment.Center, -1, fontSize, labelColor);
                }
            }
        }

        // ── Grid (subtle minor + brighter major every 10th line) ──
        if (State.ShowGrid && !skipDetailedOverlays)
        {
            var minorColor = new Color(1f, 1f, 1f, 0.04f);
            var majorColor = new Color(1f, 1f, 1f, 0.12f);
            for (int y = ovMinY; y <= ovMaxY + 1; y++)
            {
                bool isMajor = (y - 1) % 10 == 0;
                DrawLine(new Vector2(ovMinX * TileSize, y * TileSize),
                         new Vector2((ovMaxX + 1) * TileSize, y * TileSize),
                         isMajor ? majorColor : minorColor, isMajor ? 1f : 0.5f);
            }
            for (int x = ovMinX; x <= ovMaxX + 1; x++)
            {
                bool isMajor = (x - 1) % 10 == 0;
                DrawLine(new Vector2(x * TileSize, ovMinY * TileSize),
                         new Vector2(x * TileSize, (ovMaxY + 1) * TileSize),
                         isMajor ? majorColor : minorColor, isMajor ? 1f : 0.5f);
            }
        }

        // ── Blocked tiles (diagonal hatched pattern) — skip at low zoom ──
        if (State.ShowBlocked && !skipDetailedOverlays)
        {
            var hatchColor = new Color(1f, 0.15f, 0.15f, 0.4f);
            var hatchBg = new Color(1f, 0f, 0f, 0.08f);
            float hatchSpacing = 6f;
            for (int y = ovMinY; y <= ovMaxY; y++)
                for (int x = ovMinX; x <= ovMaxX; x++)
                    if (Map.Tiles[x, y].Blocked)
                    {
                        float bx = x * TileSize;
                        float by = y * TileSize;
                        // Subtle background tint
                        DrawRect(new Rect2(bx, by, TileSize, TileSize), hatchBg);
                        // Diagonal hatch lines (bottom-left to top-right)
                        for (float d = -TileSize; d <= TileSize * 2; d += hatchSpacing)
                        {
                            float x1 = bx + d;
                            float y1 = by + TileSize;
                            float x2 = bx + d + TileSize;
                            float y2 = by;
                            // Clip to tile bounds
                            if (x2 > bx + TileSize) { float t = (bx + TileSize - x1) / (x2 - x1); y2 = y1 + t * (y2 - y1); x2 = bx + TileSize; }
                            if (x1 < bx) { float t = (bx - x1) / (x2 - x1); y1 = y1 + t * (y2 - y1); x1 = bx; }
                            if (y1 > by + TileSize || y2 > by + TileSize || y1 < by || y2 < by) continue;
                            if (x1 >= bx && x1 <= bx + TileSize && x2 >= bx && x2 <= bx + TileSize)
                                DrawLine(new Vector2(x1, y1), new Vector2(x2, y2), hatchColor, 0.8f);
                        }
                        // Thin border
                        DrawRect(new Rect2(bx + 0.5f, by + 0.5f, TileSize - 1, TileSize - 1),
                            new Color(1f, 0.2f, 0.2f, 0.3f), false, 0.5f);
                    }
        }

        // ── Exits (pill badge) ──
        if (State.ShowExits && !skipDetailedOverlays)
            for (int y = ovMinY; y <= ovMaxY; y++)
                for (int x = ovMinX; x <= ovMaxX; x++)
                    if (Map.Tiles[x, y].HasExit)
                    {
                        float ex = x * TileSize;
                        float ey = y * TileSize;
                        // Subtle fill
                        DrawRect(new Rect2(ex + 1, ey + 1, TileSize - 2, TileSize - 2),
                            new Color(0.1f, 0.8f, 0.3f, 0.1f));
                        // Arrow icon (small upward arrow at center)
                        var center = new Vector2(ex + TileSize * 0.5f, ey + TileSize * 0.35f);
                        var arrowCol = new Color(0.4f, 1f, 0.5f, 0.7f);
                        DrawLine(center, center + new Vector2(0, 6), arrowCol, 1.5f);
                        DrawLine(center, center + new Vector2(-3, 3), arrowCol, 1.5f);
                        DrawLine(center, center + new Vector2(3, 3), arrowCol, 1.5f);
                        // Label pill
                        string exitLabel = $"M{Map.Tiles[x, y].ExitMap}";
                        DrawOverlayPill(ex + 1, ey + TileSize - 12,
                            exitLabel, new Color(0.1f, 0.5f, 0.2f, 0.85f),
                            new Color(0.5f, 1f, 0.5f, 0.95f), 7);
                    }

        // ── Light source markers ──
        // The actual lighting is rendered via the GPU shader (identical to game).
        // These markers only show WHERE light sources exist so you can find them.
        // Only drawn when the Light tool is active or ShowLights is off (for debug).
        bool showLightMarkers = State.ActiveTool == EditorTool.Light && State.ShowLights;
        if (showLightMarkers && !skipDetailedOverlays)
            for (int y = ovMinY; y <= ovMaxY; y++)
                for (int x = ovMinX; x <= ovMaxX; x++)
                    if (Map.Tiles[x, y].HasLight)
                    {
                        ref var tile = ref Map.Tiles[x, y];
                        var center = new Vector2((x + 0.5f) * TileSize, (y + 0.5f) * TileSize);
                        var lightCol = new Color(tile.LightR / 255f, tile.LightG / 255f, tile.LightB / 255f);
                        // Tiny bright dot at the light source (helps you locate it)
                        DrawCircle(center, 4f, new Color(lightCol.R, lightCol.G, lightCol.B, 0.9f));
                        DrawCircle(center, 2f, new Color(1f, 1f, 1f, 0.95f));
                    }

        // ── Particle indicators (diamond + pill) ──
        if (State.ShowParticles && !skipDetailedOverlays)
            for (int y = ovMinY; y <= ovMaxY; y++)
                for (int x = ovMinX; x <= ovMaxX; x++)
                    if (Map.Tiles[x, y].ParticleGroup > 0)
                    {
                        var center = new Vector2((x + 0.5f) * TileSize, (y + 0.5f) * TileSize);
                        var particleCol = EditorTheme.OVERLAY_PARTICLE;
                        float s = TileSize * 0.22f;
                        // Filled diamond
                        var diamond = new Vector2[] {
                            center + new Vector2(0, -s), center + new Vector2(s, 0),
                            center + new Vector2(0, s), center + new Vector2(-s, 0)
                        };
                        DrawColoredPolygon(diamond, particleCol with { A = 0.25f });
                        DrawLine(diamond[0], diamond[1], particleCol, 1.2f);
                        DrawLine(diamond[1], diamond[2], particleCol, 1.2f);
                        DrawLine(diamond[2], diamond[3], particleCol, 1.2f);
                        DrawLine(diamond[3], diamond[0], particleCol, 1.2f);
                        // Label pill
                        DrawOverlayPill(x * TileSize + 1, y * TileSize + 1,
                            $"P{Map.Tiles[x, y].ParticleGroup}",
                            new Color(0f, 0.35f, 0.35f, 0.85f),
                            new Color(0.3f, 1f, 0.9f, 0.9f), 7);
                    }

        // ── NPC indicators (badge with dot) ──
        if (State.ShowNpcs)
            for (int y = ovMinY; y <= ovMaxY; y++)
                for (int x = ovMinX; x <= ovMaxX; x++)
                    if (Map.Tiles[x, y].HasNpc)
                    {
                        int npcNum = Map.Tiles[x, y].NpcIndex;
                        var center = new Vector2((x + 0.5f) * TileSize, (y + 0.5f) * TileSize);
                        // Small filled circle with ring
                        DrawCircle(center, TileSize * 0.18f, EditorTheme.OVERLAY_NPC with { A = 0.5f });
                        DrawArc(center, TileSize * 0.18f, 0, MathF.Tau, 16,
                            new Color(1f, 0.7f, 0.3f, 0.7f), 1.2f);
                        // Label pill at bottom
                        string npcLabel = NpcDb?.Get(npcNum)?.Name ?? $"N{npcNum}";
                        DrawOverlayPill(x * TileSize, (y + 1) * TileSize - 11,
                            npcLabel, new Color(0.5f, 0.3f, 0.1f, 0.85f),
                            new Color(1f, 0.75f, 0.35f, 0.95f), 7);
                    }

        // ── Object indicators (small square badge) ──
        if (State.ShowObjects)
            for (int y = ovMinY; y <= ovMaxY; y++)
                for (int x = ovMinX; x <= ovMaxX; x++)
                    if (Map.Tiles[x, y].HasObject)
                    {
                        var center = new Vector2((x + 0.5f) * TileSize, (y + 0.5f) * TileSize);
                        // Small rounded square indicator
                        float hs = TileSize * 0.15f;
                        DrawRect(new Rect2(center.X - hs, center.Y - hs, hs * 2, hs * 2),
                            EditorTheme.OVERLAY_OBJECT with { A = 0.45f });
                        DrawRect(new Rect2(center.X - hs, center.Y - hs, hs * 2, hs * 2),
                            new Color(0.7f, 0.4f, 1f, 0.6f), false, 1f);
                        // Label pill
                        var obj = Map.Tiles[x, y];
                        DrawOverlayPill(x * TileSize, y * TileSize + 1,
                            $"O{obj.ObjIndex}x{obj.ObjAmount}",
                            new Color(0.3f, 0.15f, 0.5f, 0.85f),
                            new Color(0.8f, 0.5f, 1f, 0.95f), 7);
                    }

        // ── Layer 3 anchor dots ──
        // Small orange dot at the tile center marks where a L3 GRH is anchored.
        // A large multi-tile GRH (e.g. 5×5) only attaches to one tile; the dot
        // reveals which tile that is so the Hand tool can select it precisely.
        for (int y = ovMinY; y <= ovMaxY; y++)
            for (int x = ovMinX; x <= ovMaxX; x++)
            {
                if (Map.Tiles[x, y].Layer3 > 0)
                {
                    // Orange dot — tile center
                    var pos = new Vector2(x * TileSize + TileSize * 0.5f, y * TileSize + TileSize * 0.5f);
                    DrawCircle(pos, 2.5f, new Color(1.0f, 0.55f, 0.15f, 0.85f));
                    DrawArc(pos, 2.5f, 0, MathF.Tau, 8, new Color(1.0f, 0.85f, 0.55f, 0.9f), 0.8f);
                }
            }

        // ── Triggers (subtle fill + pill label) ──
        for (int y = ovMinY; y <= ovMaxY; y++)
            for (int x = ovMinX; x <= ovMaxX; x++)
                if (Map.Tiles[x, y].Trigger > 0)
                {
                    short trig = Map.Tiles[x, y].Trigger;
                    var (trigColor, trigName) = trig switch
                    {
                        1 => (new Color(0.5f, 0.5f, 0.5f, 0.25f), "Indoor"),
                        3 => (new Color(0.8f, 0.2f, 0.2f, 0.25f), "InvPos"),
                        4 => (new Color(0, 0.7f, 1, 0.25f), "Safe"),
                        5 => (new Color(0.8f, 0.8f, 0, 0.25f), "AntiBl"),
                        6 => (new Color(1, 0, 0, 0.2f), "Combat"),
                        _ => (new Color(1, 1, 0, 0.2f), $"T{trig}"),
                    };
                    DrawRect(new Rect2(x * TileSize + 1, y * TileSize + 1,
                        TileSize - 2, TileSize - 2), trigColor);
                    DrawOverlayPill(x * TileSize + 1, y * TileSize + 1,
                        trigName, trigColor with { A = 0.7f },
                        new Color(trigColor.R, trigColor.G, trigColor.B, 0.9f), 6);
                }

        // ── Selected tile highlight (Hand tool click) ──
        if (State.HasSelectedTile && Map.InBounds(State.SelectedTileX, State.SelectedTileY))
        {
            float sx = State.SelectedTileX * TileSize;
            float sy = State.SelectedTileY * TileSize;
            // Subtle cyan fill + crisp border to distinguish from hover
            DrawRect(new Rect2(sx + 1, sy + 1, TileSize - 2, TileSize - 2),
                new Color(0.3f, 0.85f, 1.0f, 0.10f));
            DrawRect(new Rect2(sx + 0.5f, sy + 0.5f, TileSize - 1, TileSize - 1),
                new Color(0.4f, 0.9f, 1.0f, 0.75f), false, 1.5f);
        }

        // ── Hover highlight (clean 2px border, no fill) ──
        if (State.HoverValid && Map.InBounds(State.HoverX, State.HoverY))
        {
            float hx = State.HoverX * TileSize;
            float hy = State.HoverY * TileSize;
            // Very subtle inner glow
            DrawRect(new Rect2(hx + 1, hy + 1, TileSize - 2, TileSize - 2),
                new Color(1f, 1f, 1f, 0.06f));
            // Crisp bright border
            DrawRect(new Rect2(hx + 0.5f, hy + 0.5f, TileSize - 1, TileSize - 1),
                new Color(1f, 1f, 1f, 0.5f), false, 1.5f);
        }

        // ── Selection rectangle (marching ants + blue fill) ──
        if (State.HasSelection)
        {
            var selRect = new Rect2(
                State.SelX1 * TileSize, State.SelY1 * TileSize,
                (State.SelX2 - State.SelX1 + 1) * TileSize,
                (State.SelY2 - State.SelY1 + 1) * TileSize);
            // Semi-transparent blue fill
            DrawRect(selRect, new Color(0.2f, 0.5f, 1f, 0.12f));
            // Marching ants: dark line underneath, then dashed white on top
            DrawMarchingAnts(selRect);
        }

        // ── Active selection being drawn (marching ants + yellow fill) ──
        if (_isSelecting)
        {
            int sx1 = Math.Min(_selectStart.X, _dragCurrent.X);
            int sy1 = Math.Min(_selectStart.Y, _dragCurrent.Y);
            int sx2 = Math.Max(_selectStart.X, _dragCurrent.X);
            int sy2 = Math.Max(_selectStart.Y, _dragCurrent.Y);
            var selRect = new Rect2(sx1 * TileSize, sy1 * TileSize,
                (sx2 - sx1 + 1) * TileSize, (sy2 - sy1 + 1) * TileSize);
            DrawRect(selRect, new Color(1f, 1f, 0.3f, 0.1f));
            DrawMarchingAnts(selRect, new Color(1f, 1f, 0.3f, 0.9f));
        }
    }

    /// <summary>
    /// Draw a text label with a rounded pill background for readability.
    /// </summary>
    private void DrawOverlayPill(float px, float py, string text,
        Color bgColor, Color textColor, int fontSize)
    {
        // Estimate text width (approximate: fontSize * 0.6 per char)
        float textW = text.Length * fontSize * 0.55f + 4;
        float textH = fontSize + 3;
        // Background pill
        DrawRect(new Rect2(px, py, textW, textH), bgColor);
        // Render text
        DrawString(ThemeDB.FallbackFont,
            new Vector2(px + 2, py + fontSize),
            text, HorizontalAlignment.Left, -1, fontSize, textColor);
    }

    /// <summary>
    /// Draw a marching ants rectangle (animated dashed border).
    /// Uses a dark shadow line underneath and a bright dashed line on top.
    /// </summary>
    private void DrawMarchingAnts(Rect2 rect, Color? dashColor = null)
    {
        var dark = new Color(0f, 0f, 0f, 0.5f);
        var bright = dashColor ?? new Color(1f, 1f, 1f, 0.9f);
        float dashLen = MarchingAntsDash;
        float gapLen = MarchingAntsGap;
        float total = dashLen + gapLen;

        // Draw solid dark line as shadow underneath
        DrawRect(rect, dark, false, 1.5f);

        // Draw dashed bright line on top (4 edges)
        DrawDashedEdge(
            new Vector2(rect.Position.X, rect.Position.Y),
            new Vector2(rect.Position.X + rect.Size.X, rect.Position.Y),
            bright, dashLen, gapLen, total);
        DrawDashedEdge(
            new Vector2(rect.Position.X + rect.Size.X, rect.Position.Y),
            new Vector2(rect.Position.X + rect.Size.X, rect.Position.Y + rect.Size.Y),
            bright, dashLen, gapLen, total);
        DrawDashedEdge(
            new Vector2(rect.Position.X + rect.Size.X, rect.Position.Y + rect.Size.Y),
            new Vector2(rect.Position.X, rect.Position.Y + rect.Size.Y),
            bright, dashLen, gapLen, total);
        DrawDashedEdge(
            new Vector2(rect.Position.X, rect.Position.Y + rect.Size.Y),
            new Vector2(rect.Position.X, rect.Position.Y),
            bright, dashLen, gapLen, total);
    }

    /// <summary>
    /// Draw a single dashed edge with marching animation offset.
    /// </summary>
    private void DrawDashedEdge(Vector2 from, Vector2 to, Color color,
        float dashLen, float gapLen, float total)
    {
        var dir = to - from;
        float edgeLen = dir.Length();
        if (edgeLen < 0.1f) return;
        var norm = dir / edgeLen;

        float pos = -_marchingAntsOffset;
        while (pos < edgeLen)
        {
            float segStart = Math.Max(pos, 0f);
            float segEnd = Math.Min(pos + dashLen, edgeLen);
            if (segEnd > segStart)
                DrawLine(from + norm * segStart, from + norm * segEnd, color, 1.5f);
            pos += total;
        }
    }

    #region GRH Drawing

    private void DrawTileGrh(int grhIndex, int tileX, int tileY,
        bool center = false, Color? modulate = null, bool animate = true)
    {
        if (grhIndex <= 0 || Grhs == null || Textures == null) return;
        if (grhIndex >= Grhs.Length) return;

        var grh = Grhs[grhIndex];
        if (grh.NumFrames > 1 && grh.Frames != null && grh.Frames.Length > 0)
        {
            int frame = animate && grh.Speed > 0
                ? (int)(_animTime * grh.NumFrames / grh.Speed) % grh.NumFrames
                : 0;
            int frameIdx = grh.Frames[frame];
            if (frameIdx <= 0 || frameIdx >= Grhs.Length) return;
            grh = Grhs[frameIdx];
        }

        if (grh.FileNum <= 0 || grh.PixelWidth <= 0 || grh.PixelHeight <= 0) return;
        var texture = Textures.GetTexture(grh.FileNum);
        if (texture == null) return;

        var srcRect = new Rect2(grh.SX, grh.SY, grh.PixelWidth, grh.PixelHeight);
        float drawX = tileX * TileSize;
        float drawY = tileY * TileSize;

        if (center)
        {
            drawX += (TileSize - grh.PixelWidth) / 2f;
            drawY += (TileSize - grh.PixelHeight);
        }

        var destRect = new Rect2(drawX, drawY, grh.PixelWidth, grh.PixelHeight);
        DrawTextureRectRegion(texture, destRect, srcRect, modulate ?? Colors.White);
    }

    private void DrawTileGrhOffset(int grhIndex, int tileX, int tileY, int ofsX, int ofsY,
        bool animate = true, Color? modulate = null)
    {
        if (grhIndex <= 0 || Grhs == null || Textures == null) return;
        if (grhIndex >= Grhs.Length) return;

        var grh = Grhs[grhIndex];
        if (grh.NumFrames > 1 && grh.Frames != null && grh.Frames.Length > 0)
        {
            int frame = animate && grh.Speed > 0
                ? (int)(_animTime * grh.NumFrames / grh.Speed) % grh.NumFrames
                : 0;
            int frameIdx = grh.Frames[frame];
            if (frameIdx <= 0 || frameIdx >= Grhs.Length) return;
            grh = Grhs[frameIdx];
        }

        if (grh.FileNum <= 0 || grh.PixelWidth <= 0 || grh.PixelHeight <= 0) return;
        var texture = Textures.GetTexture(grh.FileNum);
        if (texture == null) return;

        var srcRect = new Rect2(grh.SX, grh.SY, grh.PixelWidth, grh.PixelHeight);
        float drawX = tileX * TileSize + (TileSize - grh.PixelWidth) / 2f + ofsX;
        float drawY = tileY * TileSize + (TileSize - grh.PixelHeight) + ofsY;
        var destRect = new Rect2(drawX, drawY, grh.PixelWidth, grh.PixelHeight);
        DrawTextureRectRegion(texture, destRect, srcRect, modulate ?? Colors.White);
    }

    #endregion

    #region Particle Drawing

    public void DrawParticlesOn(CanvasItem canvas)
    {
        if (Particles == null || Grhs == null || Textures == null || State == null) return;
        canvas.DrawSetTransform(State.CameraOffset, 0f, new Vector2(State.Zoom, State.Zoom));

        foreach (var stream in Particles.Streams)
        {
            if (!stream.Active) continue;
            float streamX = stream.MapX * TileSize + TileSize / 2f;
            float streamY = stream.MapY * TileSize + TileSize / 2f;

            foreach (var p in stream.Particles)
            {
                if (!p.Alive || p.GrhIndex <= 0 || p.GrhIndex >= Grhs.Length) continue;
                var grh = Grhs[p.GrhIndex];
                if (grh.NumFrames > 1 && grh.Frames != null && grh.Frames.Length > 0)
                {
                    int frameIdx = grh.Frames[0];
                    if (frameIdx <= 0 || frameIdx >= Grhs.Length) continue;
                    grh = Grhs[frameIdx];
                }
                if (grh.FileNum <= 0 || grh.PixelWidth <= 0 || grh.PixelHeight <= 0) continue;
                var texture = Textures.GetTexture(grh.FileNum);
                if (texture == null) continue;

                var srcRect = new Rect2(grh.SX, grh.SY, grh.PixelWidth, grh.PixelHeight);
                float drawX = streamX + p.X - grh.PixelWidth / 2f;
                float drawY = streamY + p.Y - grh.PixelHeight / 2f;
                var destRect = new Rect2(drawX, drawY, grh.PixelWidth, grh.PixelHeight);
                var color = new Color(p.ColR / 255f, p.ColG / 255f, p.ColB / 255f, p.Alpha);
                canvas.DrawTextureRectRegion(texture, destRect, srcRect, color);
            }
        }
        canvas.DrawSetTransform(Vector2.Zero, 0f, Vector2.One);
    }

    #endregion

    #region Input Handling

    public override void _Input(InputEvent @event)
    {
        if (Map == null || State == null) return;

        if (@event is InputEventKey ek)
        {
            if (ek.Keycode == Key.Space)
                _spaceHeld = ek.Pressed;

            // WASD / Arrow keys for map panning
            // Always process key-up to avoid stuck keys; only set on key-down when no modifier/textfield
            bool canPanPress = !ek.CtrlPressed && !ek.AltPressed
                && GetViewport().GuiGetFocusOwner() is not LineEdit and not TextEdit;
            if (!ek.Pressed || canPanPress)
            {
                switch (ek.Keycode)
                {
                    case Key.W: case Key.Up:    _keyUp    = ek.Pressed; break;
                    case Key.S: case Key.Down:  _keyDown  = ek.Pressed; break;
                    case Key.A: case Key.Left:  _keyLeft  = ek.Pressed; break;
                    case Key.D: case Key.Right: _keyRight = ek.Pressed; break;
                }
            }
        }
        else if (@event is InputEventMouseButton mb)
        {
            var localPos = ToPanel(mb.Position);
            if (!IsInViewport(localPos)) return;
            HandleMouseButton(mb);
        }
        else if (@event is InputEventMouseMotion mm)
        {
            var localPos = ToPanel(mm.Position);
            if (!IsInViewport(localPos))
            {
                State.HoverValid = false;
                return;
            }
            HandleMouseMotion(mm);
        }
    }

    private bool IsInViewport(Vector2 localPos)
    {
        return localPos.X >= 0 && localPos.Y >= 0 &&
               localPos.X <= Size.X && localPos.Y <= Size.Y;
    }

    private Vector2I ScreenToTile(Vector2 screenPos)
    {
        var local = ToPanel(screenPos);
        int tx = (int)((local.X - State!.CameraOffset.X) / State.Zoom / TileSize);
        int ty = (int)((local.Y - State.CameraOffset.Y) / State.Zoom / TileSize);
        return new Vector2I(tx, ty);
    }

    /// <summary>
    /// Clamp a tile coordinate to valid map bounds (1..Width, 1..Height).
    /// </summary>
    private Vector2I ClampToMap(Vector2I tile)
    {
        if (Map == null) return tile;
        return new Vector2I(
            Math.Clamp(tile.X, 1, Map.Width),
            Math.Clamp(tile.Y, 1, Map.Height));
    }

    /// <summary>
    /// Returns which edge of the selection the tile is on: 0=top, 1=right, 2=bottom, 3=left.
    /// Returns -1 if not on any edge. A tile is on the edge if it's within the selection
    /// row/column at the boundary.
    /// </summary>
    private int GetSelectionEdge(Vector2I tile)
    {
        if (State == null || !State.HasSelection) return -1;
        int x = tile.X, y = tile.Y;
        int x1 = State.SelX1, y1 = State.SelY1, x2 = State.SelX2, y2 = State.SelY2;

        // Top edge: y == y1, x within bounds
        if (y == y1 && x >= x1 && x <= x2) return 0;
        // Bottom edge: y == y2
        if (y == y2 && x >= x1 && x <= x2) return 2;
        // Left edge: x == x1, y within bounds
        if (x == x1 && y >= y1 && y <= y2) return 3;
        // Right edge: x == x2
        if (x == x2 && y >= y1 && y <= y2) return 1;

        return -1;
    }

    private Vector2 ToPanel(Vector2 screenPos)
    {
        return screenPos - GlobalPosition;
    }

    /// <summary>
    /// Convert screen position to world coordinates.
    /// </summary>
    private Vector2 ScreenToWorld(Vector2 screenPos)
    {
        var local = ToPanel(screenPos);
        return (local - State!.CameraOffset) / State.Zoom;
    }

    /// <summary>
    /// Check if a screen-space click is within the mosaic move handle (top-left corner).
    /// </summary>
    private bool IsMosaicHandleClick(Vector2 screenPos, TextureRef texRef)
    {
        if (State == null || Map == null || !State.HoverValid) return false;
        var (baseX, baseY) = GetMosaicBase(State.HoverX, State.HoverY, texRef);
        var click = ScreenToWorld(screenPos);
        float hs = TileSize * 0.45f;
        float hx = baseX * TileSize;
        float hy = baseY * TileSize;
        return click.X >= hx && click.X <= hx + hs && click.Y >= hy && click.Y <= hy + hs;
    }

    /// <summary>
    /// Check if a screen-space click is within the mosaic fill button (top-right corner).
    /// </summary>
    private bool IsMosaicFillClick(Vector2 screenPos, TextureRef texRef)
    {
        if (State == null || Map == null || !State.HoverValid) return false;
        int tw = Math.Max(texRef.TileWidth, 1);
        var (baseX, baseY) = GetMosaicBase(State.HoverX, State.HoverY, texRef);
        var click = ScreenToWorld(screenPos);
        float hs = TileSize * 0.45f;
        float fx = (baseX + tw) * TileSize - hs;
        float fy = baseY * TileSize;
        return click.X >= fx && click.X <= fx + hs && click.Y >= fy && click.Y <= fy + hs;
    }

    /// <summary>
    /// Stamp the entire NxM mosaic pattern at the current preview position.
    /// </summary>
    public void StampMosaicPattern(TextureRef texRef, int hoverX, int hoverY)
    {
        if (Map == null || State == null) return;
        int tw = Math.Max(texRef.TileWidth, 1);
        int th = Math.Max(texRef.TileHeight, 1);
        var (baseX, baseY) = GetMosaicBase(hoverX, hoverY, texRef);

        Undo?.BeginBatch("Stamp Pattern");
        for (int py = 0; py < th; py++)
            for (int px = 0; px < tw; px++)
            {
                int tx = baseX + px;
                int ty = baseY + py;
                if (!Map.InBounds(tx, ty)) continue;
                var before = Map.Tiles[tx, ty];
                int grhIdx = texRef.GrhIndex + (py * tw) + px;
                SetLayerGrh(ref Map.Tiles[tx, ty], State.ActiveLayer, (int)grhIdx);
                Undo?.RecordTileChange(tx, ty, before, Map.Tiles[tx, ty]);
            }
        Undo?.EndBatch();
        QueueRedraw();
    }

    private void StartPan(Vector2 position)
    {
        _isPanning = true;
        _panStart = ToPanel(position);
        _panCameraStart = State!.CameraOffset;
    }

    private void HandleMouseButton(InputEventMouseButton mb)
    {
        // Zoom towards cursor
        if (mb.ButtonIndex == MouseButton.WheelUp || mb.ButtonIndex == MouseButton.WheelDown)
        {
            float oldZoom = State!.Zoom;
            if (mb.ButtonIndex == MouseButton.WheelUp)
                State.Zoom = Math.Min(State.Zoom * 1.15f, 4f);
            else
                State.Zoom = Math.Max(State.Zoom / 1.15f, 0.02f);

            var localPos = ToPanel(mb.Position);
            float zoomRatio = State.Zoom / oldZoom;
            State.CameraOffset = localPos - (localPos - State.CameraOffset) * zoomRatio;
            QueueRedraw();
            return;
        }

        // Pan with middle mouse (always)
        if (mb.ButtonIndex == MouseButton.Middle)
        {
            if (mb.Pressed) StartPan(mb.Position);
            else _isPanning = false;
            return;
        }

        // Right click: light/particle/trigger tool erases, otherwise mosaic drag or eyedrop
        if (mb.ButtonIndex == MouseButton.Right)
        {
            if (mb.Pressed && State?.ActiveTool == EditorTool.Light)
            {
                var tile = ScreenToTile(mb.Position);
                EraseLightAt(tile.X, tile.Y);
                return;
            }
            if (mb.Pressed && State?.ActiveTool == EditorTool.Particle)
            {
                var tile = ScreenToTile(mb.Position);
                EraseParticleAt(tile.X, tile.Y);
                return;
            }
            if (State?.ActiveTool == EditorTool.Trigger)
            {
                if (mb.Pressed)
                {
                    _isPainting = true;
                    _paintedThisStroke.Clear();
                    Undo?.BeginBatch("Erase Trigger");
                    var tile = ScreenToTile(mb.Position);
                    EraseTriggerAt(tile.X, tile.Y);
                }
                else
                {
                    if (_isPainting) { _isPainting = false; Undo?.EndBatch(); }
                }
                return;
            }
            if (mb.Pressed && !_isPainting)
            {
                // If a multi-tile pattern is selected, right-drag repositions the mosaic
                if (State?.SelectedTexture != null &&
                    Math.Max(State.SelectedTexture.TileWidth, 1) > 1)
                {
                    var tile = ScreenToTile(mb.Position);
                    _mosaicHandleDrag = true;
                    _mosaicHandleDragStart = tile;
                }
                else
                {
                    var tile = ScreenToTile(mb.Position);
                    // Right-click grab: pick entity or layer graphic to move it
                    PickStartAt(tile.X, tile.Y);
                    if (State?.Pick.HasPick != true)
                        EyedropAt(tile.X, tile.Y); // fallback to eyedrop if nothing to pick
                }
            }
            else if (!mb.Pressed)
            {
                if (_mosaicHandleDrag) { _mosaicHandleDrag = false; QueueRedraw(); }
                if (_isPanning) _isPanning = false;
                // Drop picked entity on right-click release
                if (State?.Pick.HasPick == true)
                {
                    var tile = ScreenToTile(mb.Position);
                    PickDropAt(tile.X, tile.Y);
                }
            }
            return;
        }

        // Left click
        if (mb.ButtonIndex == MouseButton.Left)
        {
            // Space always pans regardless of tool
            if (_spaceHeld)
            {
                if (mb.Pressed) StartPan(mb.Position);
                else _isPanning = false;
                return;
            }

            var tile = ScreenToTile(mb.Position);

            // Pending placement interaction: accept, cancel, or drag to reposition
            if (State!.Pending.Active)
            {
                if (mb.Pressed && mb.ButtonIndex == MouseButton.Left)
                {
                    if (IsPendingAcceptClick(mb.Position))
                    {
                        OnPendingAccept?.Invoke();
                        return;
                    }
                    if (IsPendingCancelClick(mb.Position))
                    {
                        OnPendingCancel?.Invoke();
                        return;
                    }
                    // Start dragging the pending placement
                    _pendingDrag = true;
                    _pendingDragStart = tile;
                    return;
                }
                if (!mb.Pressed && mb.ButtonIndex == MouseButton.Left)
                {
                    _pendingDrag = false;
                    return;
                }
                // Escape to cancel (handled in _UnhandledKeyInput, but also right-click)
                if (mb.Pressed && mb.ButtonIndex == MouseButton.Right)
                {
                    OnPendingCancel?.Invoke();
                    return;
                }
                return; // Block all other input while pending
            }

            if (mb.Pressed)
            {
                // Double-click: follow exits, or edit light when Light tool is active
                if (mb.DoubleClick && Map!.InBounds(tile.X, tile.Y))
                {
                    ref var t = ref Map.Tiles[tile.X, tile.Y];
                    if (t.HasExit)
                    {
                        State!.RequestExitFollow(t.ExitMap, t.ExitX, t.ExitY);
                        return;
                    }
                    // Double-click on existing light with Light tool → load into editor
                    if (State.ActiveTool == EditorTool.Light && t.HasLight)
                    {
                        State.LightR = t.LightR;
                        State.LightG = t.LightG;
                        State.LightB = t.LightB;
                        State.LightRange = t.LightRange;
                        OnLightEditRequested?.Invoke();
                        return;
                    }
                }

                switch (State!.ActiveTool)
                {
                    case EditorTool.Hand:
                        // Defer panning: commit on first drag, select tile on quick click
                        _handClickPending = true;
                        _handClickScreenPos = mb.Position;
                        break;
                    case EditorTool.Paint:
                        // Multi-tile: check mosaic buttons first
                        if (State.SelectedTexture != null &&
                            Math.Max(State.SelectedTexture.TileWidth, 1) > 1)
                        {
                            if (IsMosaicHandleClick(mb.Position, State.SelectedTexture))
                            {
                                _mosaicHandleDrag = true;
                                _mosaicHandleDragStart = tile;
                                break;
                            }
                            if (IsMosaicFillClick(mb.Position, State.SelectedTexture))
                            {
                                StampMosaicPattern(State.SelectedTexture, tile.X, tile.Y);
                                break;
                            }
                        }
                        goto case EditorTool.Block; // fall through to paint
                    case EditorTool.Erase:
                    case EditorTool.Block:
                        _isPainting = true;
                        _paintedThisStroke.Clear();
                        Undo?.BeginBatch(State.ActiveTool == EditorTool.Paint ? "Paint" :
                                         State.ActiveTool == EditorTool.Erase ? "Erase" : "Block");
                        ApplyToolAt(tile.X, tile.Y);
                        break;
                    case EditorTool.Select:
                    {
                        var clamped = ClampToMap(tile);

                        // Check if click is on a selection edge (for resize)
                        int edge = GetSelectionEdge(clamped);
                        if (edge >= 0)
                        {
                            _isResizingSelection = true;
                            _resizeEdge = edge;
                            _selOrigX1 = State.SelX1; _selOrigY1 = State.SelY1;
                            _selOrigX2 = State.SelX2; _selOrigY2 = State.SelY2;
                            break;
                        }

                        bool insideSel = State.HasSelection &&
                            clamped.X >= State.SelX1 && clamped.X <= State.SelX2 &&
                            clamped.Y >= State.SelY1 && clamped.Y <= State.SelY2;

                        if (insideSel)
                        {
                            // Drag inside existing selection → move the selection bounds
                            _isMovingSelection = true;
                            _selMoveAnchor = clamped;
                            _selOrigX1 = State.SelX1; _selOrigY1 = State.SelY1;
                            _selOrigX2 = State.SelX2; _selOrigY2 = State.SelY2;
                        }
                        else
                        {
                            // Click outside → clear and start a new selection
                            if (State.HasSelection) State.ClearSelection();
                            _isSelecting = true;
                            _selectStart = clamped;
                            _dragCurrent = _selectStart;
                        }
                        break;
                    }
                    case EditorTool.Move:
                        if (State.HasSelection && Map != null)
                        {
                            _isDragging = true;
                            _dragStart = tile;
                            _dragCurrent = tile;

                            _moveSelX1 = State.SelX1;
                            _moveSelY1 = State.SelY1;
                            _moveSelW = State.SelX2 - State.SelX1 + 1;
                            _moveSelH = State.SelY2 - State.SelY1 + 1;
                            _moveSnapshot = new MapTile[Map.Width + 1, Map.Height + 1];
                            Array.Copy(Map.Tiles, _moveSnapshot, Map.Tiles.Length);

                            _moveBuffer = new MapTile[_moveSelW, _moveSelH];
                            for (int y = 0; y < _moveSelH; y++)
                                for (int x = 0; x < _moveSelW; x++)
                                    if (Map.InBounds(_moveSelX1 + x, _moveSelY1 + y))
                                        _moveBuffer[x, y] = Map.Tiles[_moveSelX1 + x, _moveSelY1 + y];

                            ApplyLiveMove();
                        }
                        break;
                    case EditorTool.Pick:
                        PickStartAt(tile.X, tile.Y);
                        break;
                    case EditorTool.Fill:
                        if (State.HasSelection)
                            FillSelection();
                        else if (State.SelectedTexture != null)
                            StampMosaicPattern(State.SelectedTexture, tile.X, tile.Y);
                        else
                            FloodFill(tile.X, tile.Y);
                        break;
                    case EditorTool.Eyedrop:
                        EyedropAt(tile.X, tile.Y);
                        break;
                    case EditorTool.Npc:
                        if (State.SelectedNpcNumber > 0)
                        {
                            PlaceNpcAt(tile.X, tile.Y, (short)State.SelectedNpcNumber);
                        }
                        else
                        {
                            State.ShowTileProperties = true;
                            State.PropTileX = tile.X;
                            State.PropTileY = tile.Y;
                        }
                        break;
                    case EditorTool.Object:
                        if (State.SelectedObjectNumber > 0)
                        {
                            PlaceObjectAt(tile.X, tile.Y, (short)State.SelectedObjectNumber);
                        }
                        else
                        {
                            State.ShowTileProperties = true;
                            State.PropTileX = tile.X;
                            State.PropTileY = tile.Y;
                        }
                        break;
                    case EditorTool.Light:
                        _isPainting = true;
                        _paintedThisStroke.Clear();
                        Undo?.BeginBatch("Place Light");
                        PlaceLightAt(tile.X, tile.Y);
                        break;
                    case EditorTool.Particle:
                        _isPainting = true;
                        _paintedThisStroke.Clear();
                        Undo?.BeginBatch("Paint Particle");
                        PlaceParticleAt(tile.X, tile.Y);
                        break;
                    case EditorTool.Exit:
                        State.ShowTileProperties = true;
                        State.PropTileX = tile.X;
                        State.PropTileY = tile.Y;
                        break;
                    case EditorTool.Trigger:
                        _isPainting = true;
                        _paintedThisStroke.Clear();
                        Undo?.BeginBatch("Paint Trigger");
                        PaintTriggerAt(tile.X, tile.Y);
                        break;
                }
            }
            else // Left button released
            {
                if (_mosaicHandleDrag) { _mosaicHandleDrag = false; QueueRedraw(); }
                if (_isPainting) { _isPainting = false; Undo?.EndBatch(); }
                if (_isSelecting)
                {
                    _isSelecting = false;
                    var clampedEnd = ClampToMap(_dragCurrent);
                    State!.SetSelection(_selectStart.X, _selectStart.Y, clampedEnd.X, clampedEnd.Y);
                    OnSelectionCompleted?.Invoke();
                    QueueRedraw();
                }
                if (_isMovingSelection)
                {
                    _isMovingSelection = false;
                    QueueRedraw();
                }
                if (_isResizingSelection)
                {
                    _isResizingSelection = false;
                    OnSelectionCompleted?.Invoke();
                    QueueRedraw();
                }
                if (_isDragging)
                {
                    _isDragging = false;
                    if (_moveBuffer != null && _moveSnapshot != null && Map != null)
                    {
                        // Restore map to original then enter pending placement mode
                        Array.Copy(_moveSnapshot, Map.Tiles, Map.Tiles.Length);
                        var delta = tile - _dragStart;
                        State!.Pending.Begin(_moveBuffer, _moveSelW, _moveSelH,
                            _moveSelX1 + delta.X, _moveSelY1 + delta.Y,
                            isMove: true, snapshot: _moveSnapshot,
                            srcX: _moveSelX1, srcY: _moveSelY1);
                    }
                    _moveSnapshot = null;
                    _moveBuffer = null;
                }
                if (State!.Pick.IsDragging)
                {
                    PickDropAt(tile.X, tile.Y);
                }
                if (_isPanning) _isPanning = false;
                if (_handClickPending)
                {
                    // Short click with Hand tool: select the tile
                    _handClickPending = false;
                    var clickedTile = ScreenToTile(_handClickScreenPos);
                    if (State != null && Map != null && Map.InBounds(clickedTile.X, clickedTile.Y))
                    {
                        State.SelectedTileX = clickedTile.X;
                        State.SelectedTileY = clickedTile.Y;
                        QueueRedraw();
                    }
                }
            }
        }
    }

    private void HandleMouseMotion(InputEventMouseMotion mm)
    {
        // Update hover position
        var hoverTile = ScreenToTile(mm.Position);
        if (State != null)
        {
            State.HoverX = hoverTile.X;
            State.HoverY = hoverTile.Y;
            State.HoverValid = Map != null && Map.InBounds(hoverTile.X, hoverTile.Y);
        }

        // Pending placement drag: reposition tile-by-tile
        if (_pendingDrag && State!.Pending.Active)
        {
            var delta = hoverTile - _pendingDragStart;
            if (delta != Vector2I.Zero)
            {
                State.Pending.OriginX += delta.X;
                State.Pending.OriginY += delta.Y;
                _pendingDragStart = hoverTile;
                QueueRedraw();
            }
            return;
        }

        // Hand tool: commit to panning once mouse has moved far enough from click point
        if (_handClickPending)
        {
            var moved = (ToPanel(mm.Position) - ToPanel(_handClickScreenPos)).Length();
            if (moved >= HandPanThreshold)
            {
                _handClickPending = false;
                StartPan(_handClickScreenPos);
            }
        }

        if (_isPanning)
        {
            State!.CameraOffset = _panCameraStart + (ToPanel(mm.Position) - _panStart);
            QueueRedraw();
            return;
        }

        // Mosaic handle drag: adjust offset tile by tile
        if (_mosaicHandleDrag)
        {
            var delta = hoverTile - _mosaicHandleDragStart;
            if (delta != Vector2I.Zero)
            {
                State!.MosaicOffsetX += delta.X;
                State.MosaicOffsetY += delta.Y;
                _mosaicHandleDragStart = hoverTile;
                QueueRedraw();
            }
            return;
        }

        if (_isPainting)
        {
            var tile = ScreenToTile(mm.Position);
            if (State!.ActiveTool == EditorTool.Light)
                PlaceLightAt(tile.X, tile.Y);
            else if (State.ActiveTool == EditorTool.Particle)
                PlaceParticleAt(tile.X, tile.Y);
            else if (State.ActiveTool == EditorTool.Trigger)
            {
                // Left-click drag paints; right-click drag erases (determined by which button started _isPainting)
                if (Input.IsMouseButtonPressed(MouseButton.Left))
                    PaintTriggerAt(tile.X, tile.Y);
                else
                    EraseTriggerAt(tile.X, tile.Y);
            }
            else if (State.ActiveTool == EditorTool.Paint || State.ActiveTool == EditorTool.Block)
                ApplyToolAt(tile.X, tile.Y);
            else
                EraseAt(tile.X, tile.Y);
            QueueRedraw();
            return;
        }

        if (_isSelecting)
        {
            _dragCurrent = ClampToMap(hoverTile);
            QueueRedraw();
        }

        if (_isMovingSelection && State != null)
        {
            var cur = ClampToMap(hoverTile);
            int dx = cur.X - _selMoveAnchor.X;
            int dy = cur.Y - _selMoveAnchor.Y;
            State.SetSelection(_selOrigX1 + dx, _selOrigY1 + dy, _selOrigX2 + dx, _selOrigY2 + dy);
            QueueRedraw();
        }

        if (_isResizingSelection && State != null)
        {
            var cur = ClampToMap(hoverTile);
            int x1 = _selOrigX1, y1 = _selOrigY1, x2 = _selOrigX2, y2 = _selOrigY2;
            switch (_resizeEdge)
            {
                case 0: y1 = Math.Min(cur.Y, y2); break; // top
                case 1: x2 = Math.Max(cur.X, x1); break; // right
                case 2: y2 = Math.Max(cur.Y, y1); break; // bottom
                case 3: x1 = Math.Min(cur.X, x2); break; // left
            }
            State.SetSelection(x1, y1, x2, y2);
            QueueRedraw();
        }

        if (_isDragging)
        {
            _dragCurrent = hoverTile;
            ApplyLiveMove();
            QueueRedraw();
        }

        // Pick tool drag
        if (State!.Pick.IsDragging)
        {
            State.Pick.DragX = hoverTile.X;
            State.Pick.DragY = hoverTile.Y;
            QueueRedraw();
        }

        // (auto-align mosaic removed — it caused the offset to jump while painting)

        // Redraw on hover for paint preview, trigger preview, and pending placement
        if (State.ActiveTool == EditorTool.Paint || State.ActiveTool == EditorTool.Trigger || State.Pending.Active)
            QueueRedraw();
    }

    #endregion

    #region Pick Tool

    /// <summary>
    /// Detect what entity is at tile (x,y) and start dragging it.
    /// Priority: NPC > Object > Particle > L3 > L4 > L2.
    /// </summary>
    private void PickStartAt(int tx, int ty)
    {
        if (Map == null || State == null || !Map.InBounds(tx, ty)) return;
        ref var tile = ref Map.Tiles[tx, ty];

        var pick = State.Pick;
        pick.Clear();

        if (tile.HasNpc)
            pick.Target = PickTarget.Npc;
        else if (tile.HasObject)
            pick.Target = PickTarget.Object;
        else if (tile.ParticleGroup > 0)
            pick.Target = PickTarget.Particle;
        else if (tile.Layer3 != 0)
            pick.Target = PickTarget.Layer3;
        else if (tile.Layer4 != 0)
            pick.Target = PickTarget.Layer4;
        else if (tile.Layer2 != 0)
            pick.Target = PickTarget.Layer2;
        else
            return; // nothing to pick

        pick.HasPick = true;
        pick.SourceX = tx;
        pick.SourceY = ty;
        pick.IsDragging = true;
        pick.DragX = tx;
        pick.DragY = ty;
        QueueRedraw();
    }

    /// <summary>
    /// Drop the picked entity at destination tile.
    /// </summary>
    private void PickDropAt(int tx, int ty)
    {
        if (Map == null || State == null) return;
        var pick = State.Pick;
        if (!pick.HasPick) { pick.Clear(); return; }

        int sx = pick.SourceX, sy = pick.SourceY;
        if (tx == sx && ty == sy) { pick.Clear(); return; } // dropped on itself
        if (!Map.InBounds(tx, ty)) { pick.Clear(); return; }

        Undo?.BeginBatch($"Pick Move {pick.Target}");
        var beforeSrc = Map.Tiles[sx, sy];
        var beforeDst = Map.Tiles[tx, ty];

        switch (pick.Target)
        {
            case PickTarget.Layer2:
                Map.Tiles[tx, ty].Layer2 = Map.Tiles[sx, sy].Layer2;
                Map.Tiles[sx, sy].Layer2 = 0;
                break;
            case PickTarget.Layer3:
                Map.Tiles[tx, ty].Layer3 = Map.Tiles[sx, sy].Layer3;
                Map.Tiles[sx, sy].Layer3 = 0;
                break;
            case PickTarget.Layer4:
                Map.Tiles[tx, ty].Layer4 = Map.Tiles[sx, sy].Layer4;
                Map.Tiles[sx, sy].Layer4 = 0;
                break;
            case PickTarget.Npc:
                Map.Tiles[tx, ty].NpcIndex = Map.Tiles[sx, sy].NpcIndex;
                Map.Tiles[sx, sy].NpcIndex = 0;
                break;
            case PickTarget.Object:
                Map.Tiles[tx, ty].ObjIndex = Map.Tiles[sx, sy].ObjIndex;
                Map.Tiles[tx, ty].ObjAmount = Map.Tiles[sx, sy].ObjAmount;
                Map.Tiles[sx, sy].ObjIndex = 0;
                Map.Tiles[sx, sy].ObjAmount = 0;
                break;
            case PickTarget.Particle:
                Map.Tiles[tx, ty].ParticleGroup = Map.Tiles[sx, sy].ParticleGroup;
                Map.Tiles[sx, sy].ParticleGroup = 0;
                break;
        }

        Undo?.RecordTileChange(sx, sy, beforeSrc, Map.Tiles[sx, sy]);
        Undo?.RecordTileChange(tx, ty, beforeDst, Map.Tiles[tx, ty]);
        Undo?.EndBatch();

        // Rebuild particle streams if a particle was moved
        if (pick.Target == PickTarget.Particle)
            Particles?.BuildStreamsFromMap(Map);

        pick.Clear();
        QueueRedraw();
    }

    /// <summary>
    /// Get the GRH index of the picked entity (for ghost rendering during drag).
    /// </summary>
    private int GetPickGrh()
    {
        if (Map == null || State == null) return 0;
        var pick = State.Pick;
        if (!pick.HasPick || !Map.InBounds(pick.SourceX, pick.SourceY)) return 0;
        ref var tile = ref Map.Tiles[pick.SourceX, pick.SourceY];

        return pick.Target switch
        {
            PickTarget.Layer2 => tile.Layer2,
            PickTarget.Layer3 => tile.Layer3,
            PickTarget.Layer4 => tile.Layer4,
            PickTarget.Npc => GetNpcBodyGrh(tile.NpcIndex),
            PickTarget.Object => GetObjGrh(tile.ObjIndex),
            _ => 0,
        };
    }

    /// <summary>Place (or replace) an NPC on a tile with undo support.</summary>
    private void PlaceNpcAt(int x, int y, short npcNumber)
    {
        if (Map == null || !Map.InBounds(x, y)) return;
        var before = Map.Tiles[x, y];
        Map.Tiles[x, y].NpcIndex = npcNumber;
        Undo?.BeginBatch("Place NPC");
        Undo?.RecordTileChange(x, y, before, Map.Tiles[x, y]);
        Undo?.EndBatch();
        QueueRedraw();
    }

    /// <summary>Place (or replace) an Object on a tile with undo support.</summary>
    private void PlaceObjectAt(int x, int y, short objIndex)
    {
        if (Map == null || !Map.InBounds(x, y)) return;
        var before = Map.Tiles[x, y];
        Map.Tiles[x, y].ObjIndex = objIndex;
        Map.Tiles[x, y].ObjAmount = 1;
        Undo?.BeginBatch("Place Object");
        Undo?.RecordTileChange(x, y, before, Map.Tiles[x, y]);
        Undo?.EndBatch();
        QueueRedraw();
    }

    /// <summary>Place a light on a tile using current editor light settings.</summary>
    private void PlaceLightAt(int x, int y)
    {
        if (Map == null || !Map.InBounds(x, y) || State == null) return;
        int key = y * 10000 + x;
        if (_paintedThisStroke.Contains(key)) return;
        _paintedThisStroke.Add(key);
        var before = Map.Tiles[x, y];
        Map.Tiles[x, y].LightR = (short)State.LightR;
        Map.Tiles[x, y].LightG = (short)State.LightG;
        Map.Tiles[x, y].LightB = (short)State.LightB;
        Map.Tiles[x, y].LightRange = (short)State.LightRange;
        Undo?.RecordTileChange(x, y, before, Map.Tiles[x, y]);
        _lightingDirty = true;
        QueueRedraw();
    }

    /// <summary>Fill a rectangular area with the current light settings.
    /// Used to quickly illuminate an entire zone or selection.</summary>
    public void FillLightInRect(int x1, int y1, int x2, int y2)
    {
        if (Map == null || State == null) return;
        int minX = Math.Min(x1, x2), maxX = Math.Max(x1, x2);
        int minY = Math.Min(y1, y2), maxY = Math.Max(y1, y2);

        Undo?.BeginBatch($"Fill Light ({minX},{minY})→({maxX},{maxY})");
        int count = 0;
        for (int y = minY; y <= maxY; y++)
            for (int x = minX; x <= maxX; x++)
            {
                if (!Map.InBounds(x, y)) continue;
                var before = Map.Tiles[x, y];
                Map.Tiles[x, y].LightR = (short)State.LightR;
                Map.Tiles[x, y].LightG = (short)State.LightG;
                Map.Tiles[x, y].LightB = (short)State.LightB;
                Map.Tiles[x, y].LightRange = (short)State.LightRange;
                Undo?.RecordTileChange(x, y, before, Map.Tiles[x, y]);
                count++;
            }
        Undo?.EndBatch();
        _lightingDirty = true;
        QueueRedraw();
    }

    /// <summary>Clear all lights in a rectangular area.</summary>
    public void ClearLightInRect(int x1, int y1, int x2, int y2)
    {
        if (Map == null) return;
        int minX = Math.Min(x1, x2), maxX = Math.Max(x1, x2);
        int minY = Math.Min(y1, y2), maxY = Math.Max(y1, y2);

        Undo?.BeginBatch($"Clear Lights ({minX},{minY})→({maxX},{maxY})");
        for (int y = minY; y <= maxY; y++)
            for (int x = minX; x <= maxX; x++)
            {
                if (!Map.InBounds(x, y) || !Map.Tiles[x, y].HasLight) continue;
                var before = Map.Tiles[x, y];
                Map.Tiles[x, y].LightR = 0;
                Map.Tiles[x, y].LightG = 0;
                Map.Tiles[x, y].LightB = 0;
                Map.Tiles[x, y].LightRange = 0;
                Undo?.RecordTileChange(x, y, before, Map.Tiles[x, y]);
            }
        Undo?.EndBatch();
        _lightingDirty = true;
        QueueRedraw();
    }

    /// <summary>Erase a light from a tile (right click).</summary>
    private void EraseLightAt(int x, int y)
    {
        if (Map == null || !Map.InBounds(x, y)) return;
        if (!Map.Tiles[x, y].HasLight) return;
        var before = Map.Tiles[x, y];
        Map.Tiles[x, y].LightR = 0;
        Map.Tiles[x, y].LightG = 0;
        Map.Tiles[x, y].LightB = 0;
        Map.Tiles[x, y].LightRange = 0;
        Undo?.BeginBatch("Erase Light");
        Undo?.RecordTileChange(x, y, before, Map.Tiles[x, y]);
        Undo?.EndBatch();
        _lightingDirty = true;
        QueueRedraw();
    }

    /// <summary>Paint a particle group on a tile using current editor particle selection.</summary>
    private void PlaceParticleAt(int x, int y)
    {
        if (Map == null || !Map.InBounds(x, y) || State == null) return;
        int key = y * 10000 + x;
        if (_paintedThisStroke.Contains(key)) return;
        _paintedThisStroke.Add(key);
        var before = Map.Tiles[x, y];
        Map.Tiles[x, y].ParticleGroup = (short)State.SelectedParticleGroup;
        Undo?.RecordTileChange(x, y, before, Map.Tiles[x, y]);
        Particles?.BuildStreamsFromMap(Map);
        QueueRedraw();
    }

    /// <summary>Erase the particle group from a tile (right-click).</summary>
    private void EraseParticleAt(int x, int y)
    {
        if (Map == null || !Map.InBounds(x, y)) return;
        if (Map.Tiles[x, y].ParticleGroup == 0) return;
        var before = Map.Tiles[x, y];
        Map.Tiles[x, y].ParticleGroup = 0;
        Undo?.BeginBatch("Erase Particle");
        Undo?.RecordTileChange(x, y, before, Map.Tiles[x, y]);
        Undo?.EndBatch();
        Particles?.BuildStreamsFromMap(Map);
        QueueRedraw();
    }

    /// <summary>Paint the selected trigger type onto a tile.</summary>
    private void PaintTriggerAt(int x, int y)
    {
        if (Map == null || !Map.InBounds(x, y) || State == null) return;
        long key = (long)x << 32 | (uint)y;
        if (_paintedThisStroke.Contains(key)) return;
        _paintedThisStroke.Add(key);
        short newTrigger = State.SelectedTriggerType;
        if (Map.Tiles[x, y].Trigger == newTrigger) return;
        var before = Map.Tiles[x, y];
        Map.Tiles[x, y].Trigger = newTrigger;
        Undo?.RecordTileChange(x, y, before, Map.Tiles[x, y]);
        QueueRedraw();
    }

    /// <summary>Erase the trigger from a tile (right-click drag).</summary>
    private void EraseTriggerAt(int x, int y)
    {
        if (Map == null || !Map.InBounds(x, y)) return;
        long key = (long)x << 32 | (uint)y;
        if (_paintedThisStroke.Contains(key)) return;
        _paintedThisStroke.Add(key);
        if (Map.Tiles[x, y].Trigger == 0) return;
        var before = Map.Tiles[x, y];
        Map.Tiles[x, y].Trigger = 0;
        Undo?.RecordTileChange(x, y, before, Map.Tiles[x, y]);
        QueueRedraw();
    }

    private int GetNpcBodyGrh(int npcIdx)
    {
        if (NpcBodies == null || NpcBodyGrhs == null) return 0;
        if (npcIdx <= 0 || npcIdx >= NpcBodies.Length) return 0;
        int bodyIdx = NpcBodies[npcIdx];
        if (bodyIdx <= 0 || bodyIdx >= NpcBodyGrhs.Length) return 0;
        return NpcBodyGrhs[bodyIdx];
    }

    private int GetObjGrh(int objIdx)
    {
        if (ObjGrhs == null) return 0;
        if (objIdx <= 0 || objIdx >= ObjGrhs.Length) return 0;
        return ObjGrhs[objIdx];
    }

    /// <summary>Draw NPC body + head at a tile position with optional modulate (for ghost/preview).</summary>
    private void DrawNpcFull(int npcIdx, int tileX, int tileY, Color? modulate = null)
    {
        if (NpcBodies == null || NpcBodyGrhs == null) return;
        if (npcIdx <= 0 || npcIdx >= NpcBodies.Length) return;
        int bodyIdx = NpcBodies[npcIdx];
        if (bodyIdx <= 0 || bodyIdx >= NpcBodyGrhs.Length) return;
        int bodyGrh = NpcBodyGrhs[bodyIdx];
        if (bodyGrh > 0)
            DrawTileGrh(bodyGrh, tileX, tileY, center: true, modulate: modulate, animate: false);
        // Head
        if (NpcHeads != null && HeadGrhs != null &&
            NpcHeadOfsX != null && NpcHeadOfsY != null &&
            npcIdx < NpcHeads.Length)
        {
            int headIdx = NpcHeads[npcIdx];
            if (headIdx > 0 && headIdx < HeadGrhs.Length)
            {
                int headGrh = HeadGrhs[headIdx];
                if (headGrh > 0)
                {
                    int ofsX = bodyIdx < NpcHeadOfsX.Length ? NpcHeadOfsX[bodyIdx] : 0;
                    int ofsY = bodyIdx < NpcHeadOfsY.Length ? NpcHeadOfsY[bodyIdx] : 0;
                    DrawTileGrhOffset(headGrh, tileX, tileY, ofsX, ofsY, animate: false, modulate: modulate);
                }
            }
        }
    }

    #endregion

    #region Tool Actions

    private void ApplyToolAt(int tx, int ty)
    {
        if (Map == null || State == null) return;

        if (State.ActiveTool == EditorTool.Paint)
        {
            if (State.SelectedTexture != null)
            {
                if (!Map.InBounds(tx, ty)) return;
                long key = (long)tx << 32 | (uint)ty;
                if (_paintedThisStroke.Contains(key)) return;
                _paintedThisStroke.Add(key);

                // Mosaic formula with user-adjustable offset
                int grhIdx = GetMosaicGrh(State.SelectedTexture, tx, ty);

                var before = Map.Tiles[tx, ty];
                SetLayerGrh(ref Map.Tiles[tx, ty], State.ActiveLayer, (int)grhIdx);
                Undo?.RecordTileChange(tx, ty, before, Map.Tiles[tx, ty]);
            }
            else if (State.EyedropGrh > 0)
            {
                // Paint with raw GRH captured by eyedrop
                if (!Map.InBounds(tx, ty)) return;
                long key = (long)tx << 32 | (uint)ty;
                if (_paintedThisStroke.Contains(key)) return;
                _paintedThisStroke.Add(key);

                var before = Map.Tiles[tx, ty];
                SetLayerGrh(ref Map.Tiles[tx, ty], State.ActiveLayer, (int)State.EyedropGrh);
                Undo?.RecordTileChange(tx, ty, before, Map.Tiles[tx, ty]);
            }
        }
        else if (State.ActiveTool == EditorTool.Block)
        {
            if (!Map.InBounds(tx, ty)) return;
            long key = (long)tx << 32 | (uint)ty;
            if (_paintedThisStroke.Contains(key)) return;
            _paintedThisStroke.Add(key);

            var before = Map.Tiles[tx, ty];
            Map.Tiles[tx, ty].Blocked = !Map.Tiles[tx, ty].Blocked;
            Undo?.RecordTileChange(tx, ty, before, Map.Tiles[tx, ty]);
            _occludersDirty = true;
        }

        QueueRedraw();
    }

    private void EraseAt(int tx, int ty)
    {
        if (Map == null || State == null || !Map.InBounds(tx, ty)) return;
        long key = (long)tx << 32 | (uint)ty;
        if (_paintedThisStroke.Contains(key)) return;
        _paintedThisStroke.Add(key);

        var before = Map.Tiles[tx, ty];
        SetLayerGrh(ref Map.Tiles[tx, ty], State.ActiveLayer, 0);
        Undo?.RecordTileChange(tx, ty, before, Map.Tiles[tx, ty]);
        QueueRedraw();
    }

    private void SetLayerGrh(ref MapTile tile, int layer, int grhIdx)
    {
        switch (layer)
        {
            case 1: tile.Layer1 = grhIdx; break;
            case 2: tile.Layer2 = grhIdx; break;
            case 3: tile.Layer3 = grhIdx; break;
            case 4: tile.Layer4 = grhIdx; break;
        }
    }

    private int GetLayerGrh(ref MapTile tile, int layer)
    {
        return layer switch
        {
            1 => tile.Layer1, 2 => tile.Layer2,
            3 => tile.Layer3, 4 => tile.Layer4, _ => 0
        };
    }

    private void EyedropAt(int tx, int ty)
    {
        if (Map == null || State == null || !Map.InBounds(tx, ty)) return;

        // Capture the GRH from the active layer
        int grh = GetLayerGrh(ref Map.Tiles[tx, ty], State.ActiveLayer);

        // If active layer is empty, try layers top-down (4→1)
        if (grh == 0)
        {
            for (int l = 4; l >= 1; l--)
            {
                grh = GetLayerGrh(ref Map.Tiles[tx, ty], l);
                if (grh != 0) break;
            }
        }

        if (grh > 0)
        {
            State.EyedropGrh = grh;
            State.SelectedTexture = null; // Clear catalog selection, use raw GRH
            State.ActiveTool = EditorTool.Paint;
        }
    }

    /// <summary>
    /// Fill only the selected rectangle with the current texture or eyedrop GRH.
    /// Uses mosaic pattern if a catalog texture with tiles is selected.
    /// </summary>
    public void FillSelection()
    {
        if (Map == null || State == null || !State.HasSelection) return;

        int layer = State.ActiveLayer;
        var texRef = State.SelectedTexture;
        int singleFillGrh = 0;

        if (texRef == null)
        {
            if (State.EyedropGrh > 0)
                singleFillGrh = State.EyedropGrh;
            else
                return;
        }

        Undo?.BeginBatch("Fill Selection");

        int x1 = State.SelX1, y1 = State.SelY1;
        int x2 = State.SelX2, y2 = State.SelY2;

        for (int y = y1; y <= y2; y++)
        {
            for (int x = x1; x <= x2; x++)
            {
                if (!Map.InBounds(x, y)) continue;

                var before = Map.Tiles[x, y];
                int fillGrh = texRef != null ? GetMosaicGrh(texRef, x, y) : singleFillGrh;
                SetLayerGrh(ref Map.Tiles[x, y], layer, fillGrh);
                Undo?.RecordTileChange(x, y, before, Map.Tiles[x, y]);
            }
        }

        Undo?.EndBatch();
        QueueRedraw();
    }

    public void FloodFill(int startX, int startY)
    {
        if (Map == null || State == null) return;
        if (!Map.InBounds(startX, startY)) return;

        int layer = State.ActiveLayer;
        int targetGrh = GetLayerGrh(ref Map.Tiles[startX, startY], layer);

        // Determine fill source (mosaic-aware or single GRH)
        var texRef = State.SelectedTexture;
        int singleFillGrh = 0;
        if (texRef == null)
        {
            if (State.EyedropGrh > 0)
                singleFillGrh = (int)State.EyedropGrh;
            else
                return;
            if (targetGrh == singleFillGrh) return;
        }
        else
        {
            // Check no-op: would the start tile get the same GRH?
            int startFillGrh = GetMosaicGrh(texRef, startX, startY);
            if (targetGrh == startFillGrh) return;
        }

        Undo?.BeginBatch("Fill");
        var visited = new System.Collections.Generic.HashSet<long>();
        var queue = new System.Collections.Generic.Queue<(int x, int y)>();
        queue.Enqueue((startX, startY));

        int filled = 0;
        while (queue.Count > 0 && filled < 10000)
        {
            var (x, y) = queue.Dequeue();
            long key = (long)x << 32 | (uint)y;
            if (visited.Contains(key)) continue;
            visited.Add(key);

            if (!Map.InBounds(x, y)) continue;
            if (GetLayerGrh(ref Map.Tiles[x, y], layer) != targetGrh) continue;

            var before = Map.Tiles[x, y];
            int fillGrh;
            if (texRef != null)
            {
                // Mosaic with user offset
                fillGrh = GetMosaicGrh(texRef, x, y);
            }
            else
            {
                fillGrh = singleFillGrh;
            }

            SetLayerGrh(ref Map.Tiles[x, y], layer, fillGrh);
            Undo?.RecordTileChange(x, y, before, Map.Tiles[x, y]);
            filled++;

            queue.Enqueue((x + 1, y)); queue.Enqueue((x - 1, y));
            queue.Enqueue((x, y + 1)); queue.Enqueue((x, y - 1));
        }

        Undo?.EndBatch();
        QueueRedraw();
    }

    /// <summary>
    /// Pan the camera so that tile (tx, ty) is centered in the viewport.
    /// </summary>
    public void CenterOnTile(int tx, int ty)
    {
        if (State == null) return;
        var vpSize = Size;
        State.CameraOffset = new Vector2(
            vpSize.X / 2f - tx * TileSize * State.Zoom,
            vpSize.Y / 2f - ty * TileSize * State.Zoom
        );
        QueueRedraw();
    }

    #endregion
}

public partial class ParticleOverlay : Control
{
    public MapViewport? Viewport;

    public override void _Ready()
    {
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Ignore;
    }

    public override void _Draw()
    {
        Viewport?.DrawParticlesOn(this);
    }
}
