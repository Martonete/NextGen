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
    public ParticleEngine? Particles;
    public Action? OnPendingAccept;   // Fire when user clicks ✓ on pending placement
    public Action? OnPendingCancel;   // Fire when user clicks ✗ on pending placement
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
    private bool _spaceHeld;
    private Vector2 _panStart;
    private Vector2 _panCameraStart;
    private Vector2I _selectStart;
    private Vector2I _dragStart;
    private Vector2I _dragCurrent;
    private double _lastClickTime;
    private Vector2I _lastClickTile;

    private readonly System.Collections.Generic.HashSet<long> _paintedThisStroke = new();

    // Mosaic handle drag (reposition multi-tile pattern)
    private bool _mosaicHandleDrag;
    private Vector2I _mosaicHandleDragStart;

    // Move tool: live snapshot system
    private MapTile[,]? _moveSnapshot;   // Full map state before drag started
    private MapTile[,]? _moveBuffer;     // Tiles being moved (selection copy)
    private int _moveSelX1, _moveSelY1;  // Original selection top-left
    private int _moveSelW, _moveSelH;    // Selection dimensions

    private ParticleOverlay? _particleOverlay;

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
    }

    public override void _Draw()
    {
        DrawRect(new Rect2(Vector2.Zero, Size), EditorTheme.BG_DARK);

        if (Map == null || Grhs == null || Textures == null || State == null) return;

        DrawSetTransform(State.CameraOffset, 0f, new Vector2(State.Zoom, State.Zoom));

        int mapW = Map.Width;
        int mapH = Map.Height;

        // Layer 1: Ground
        if (State.ShowLayer1)
            for (int y = 1; y <= mapH; y++)
                for (int x = 1; x <= mapW; x++)
                    DrawTileGrh(Map.Tiles[x, y].Layer1, x, y);

        // Layer 2: Mask
        if (State.ShowLayer2)
            for (int y = 1; y <= mapH; y++)
                for (int x = 1; x <= mapW; x++)
                    if (Map.Tiles[x, y].Layer2 != 0)
                        DrawTileGrh(Map.Tiles[x, y].Layer2, x, y, center: true);

        // Layer 3: Objects/trees
        if (State.ShowLayer3)
            for (int y = 1; y <= mapH; y++)
                for (int x = 1; x <= mapW; x++)
                    if (Map.Tiles[x, y].Layer3 != 0)
                        DrawTileGrh(Map.Tiles[x, y].Layer3, x, y, center: true);

        // Objects on map
        if (State.ShowObjects && ObjGrhs != null)
            for (int y = 1; y <= mapH; y++)
                for (int x = 1; x <= mapW; x++)
                {
                    int objIdx = Map.Tiles[x, y].ObjIndex;
                    if (objIdx > 0 && objIdx < ObjGrhs.Length && ObjGrhs[objIdx] > 0)
                        DrawTileGrh(ObjGrhs[objIdx], x, y, center: true);
                }

        // NPCs on map
        if (State.ShowNpcs && NpcBodies != null && NpcBodyGrhs != null)
            for (int y = 1; y <= mapH; y++)
                for (int x = 1; x <= mapW; x++)
                {
                    int npcIdx = Map.Tiles[x, y].NpcIndex;
                    if (npcIdx <= 0 || npcIdx >= NpcBodies.Length) continue;
                    int bodyIdx = NpcBodies[npcIdx];
                    if (bodyIdx <= 0 || bodyIdx >= NpcBodyGrhs.Length) continue;
                    int bodyGrh = NpcBodyGrhs[bodyIdx];
                    if (bodyGrh > 0)
                        DrawTileGrh(bodyGrh, x, y, center: true);
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
                                DrawTileGrhOffset(headGrh, x, y, ofsX, ofsY);
                            }
                        }
                    }
                }

        // Layer 4: Roof
        if (State.ShowLayer4)
            for (int y = 1; y <= mapH; y++)
                for (int x = 1; x <= mapW; x++)
                    if (Map.Tiles[x, y].Layer4 != 0)
                        DrawTileGrh(Map.Tiles[x, y].Layer4, x, y, center: true,
                            modulate: new Color(1, 1, 1, 0.7f));

        // Paint tool: ghost preview at cursor position (Sims-style)
        DrawPaintPreview();

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
            DrawRect(moveRect, new Color(0.2f, 1f, 0.5f, 0.15f));
            DrawRect(moveRect, new Color(0.2f, 1f, 0.5f, 0.7f), false, 2f);
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

        // Clear source area (set to default ground)
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
    /// Draw a semi-transparent preview of the selected texture at the cursor position.
    /// Like Sims construction mode — shows what will be placed before clicking.
    /// </summary>
    private void DrawPaintPreview()
    {
        if (State == null || Map == null || _isPainting || _isDragging) return;
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

        // Outline around the placement area
        var placementRect = new Rect2(
            p.OriginX * TileSize, p.OriginY * TileSize,
            p.Width * TileSize, p.Height * TileSize);
        DrawRect(placementRect, new Color(0.2f, 0.8f, 1f, 0.15f));
        DrawRect(placementRect, new Color(0.2f, 0.8f, 1f, 0.8f), false, 2f);

        // Buttons at top of the placement area
        float btnSize = TileSize * 0.55f;
        float margin = 3f;

        // ✓ Accept button (green, top-right)
        _pendingAcceptRect = new Rect2(
            (p.OriginX + p.Width) * TileSize - btnSize - margin,
            p.OriginY * TileSize - btnSize - margin,
            btnSize, btnSize);
        DrawRect(_pendingAcceptRect, new Color(0.2f, 0.85f, 0.3f, 0.9f));
        DrawRect(_pendingAcceptRect, new Color(0, 0, 0, 0.5f), false, 1f);
        // Checkmark icon
        float cx = _pendingAcceptRect.Position.X, cy = _pendingAcceptRect.Position.Y;
        float s = btnSize;
        DrawLine(new Vector2(cx + s * 0.2f, cy + s * 0.55f),
                 new Vector2(cx + s * 0.4f, cy + s * 0.75f), Colors.White, 2f);
        DrawLine(new Vector2(cx + s * 0.4f, cy + s * 0.75f),
                 new Vector2(cx + s * 0.8f, cy + s * 0.25f), Colors.White, 2f);

        // ✗ Cancel button (red, top-left)
        _pendingCancelRect = new Rect2(
            p.OriginX * TileSize + margin,
            p.OriginY * TileSize - btnSize - margin,
            btnSize, btnSize);
        DrawRect(_pendingCancelRect, new Color(0.85f, 0.2f, 0.2f, 0.9f));
        DrawRect(_pendingCancelRect, new Color(0, 0, 0, 0.5f), false, 1f);
        // X icon
        float cx2 = _pendingCancelRect.Position.X, cy2 = _pendingCancelRect.Position.Y;
        float p2 = btnSize * 0.25f;
        DrawLine(new Vector2(cx2 + p2, cy2 + p2), new Vector2(cx2 + s - p2, cy2 + s - p2), Colors.White, 2f);
        DrawLine(new Vector2(cx2 + s - p2, cy2 + p2), new Vector2(cx2 + p2, cy2 + s - p2), Colors.White, 2f);

        // Drag handle hint (move arrows) at center-top
        float hx = placementRect.Position.X + placementRect.Size.X / 2 - btnSize / 2;
        float hy = p.OriginY * TileSize - btnSize - margin;
        var handleRect = new Rect2(hx, hy, btnSize, btnSize);
        DrawRect(handleRect, new Color(1f, 0.85f, 0.2f, 0.85f));
        DrawRect(handleRect, new Color(0, 0, 0, 0.5f), false, 1f);
        float hcx = hx + btnSize / 2, hcy = hy + btnSize / 2;
        float ar = btnSize * 0.3f;
        var arrCol = new Color(0, 0, 0, 0.7f);
        DrawLine(new Vector2(hcx - ar, hcy), new Vector2(hcx + ar, hcy), arrCol, 1.5f);
        DrawLine(new Vector2(hcx, hcy - ar), new Vector2(hcx, hcy + ar), arrCol, 1.5f);
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
            int grh = GetPickGrh();
            if (grh > 0)
                DrawTileGrh(grh, pick.DragX, pick.DragY, center: true,
                    modulate: new Color(1, 1, 1, 0.5f));

            // Destination outline
            var dstRect = new Rect2(pick.DragX * TileSize, pick.DragY * TileSize, TileSize, TileSize);
            DrawRect(dstRect, new Color(0.2f, 1f, 0.5f, 0.3f));
            DrawRect(dstRect, new Color(0.2f, 1f, 0.5f, 0.7f), false, 2f);
        }
    }

    private void DrawOverlays(int mapW, int mapH)
    {
        if (State == null || Map == null) return;

        // Grid
        if (State.ShowGrid)
        {
            var gridColor = EditorTheme.OVERLAY_GRID;
            for (int y = 1; y <= mapH + 1; y++)
                DrawLine(new Vector2(1 * TileSize, y * TileSize),
                         new Vector2((mapW + 1) * TileSize, y * TileSize), gridColor);
            for (int x = 1; x <= mapW + 1; x++)
                DrawLine(new Vector2(x * TileSize, 1 * TileSize),
                         new Vector2(x * TileSize, (mapH + 1) * TileSize), gridColor);
        }

        // Blocked tiles
        if (State.ShowBlocked)
        {
            var blockedColor = EditorTheme.OVERLAY_BLOCKED;
            for (int y = 1; y <= mapH; y++)
                for (int x = 1; x <= mapW; x++)
                    if (Map.Tiles[x, y].Blocked)
                        DrawRect(new Rect2(x * TileSize, y * TileSize, TileSize, TileSize), blockedColor);
        }

        // Exits
        if (State.ShowExits)
            for (int y = 1; y <= mapH; y++)
                for (int x = 1; x <= mapW; x++)
                    if (Map.Tiles[x, y].HasExit)
                    {
                        DrawRect(new Rect2(x * TileSize, y * TileSize, TileSize, TileSize),
                            EditorTheme.OVERLAY_EXIT);
                        DrawString(ThemeDB.FallbackFont,
                            new Vector2(x * TileSize + 2, (y + 1) * TileSize - 4),
                            $"M{Map.Tiles[x, y].ExitMap}", HorizontalAlignment.Left, -1, 7,
                            new Color(0.5f, 1f, 0.5f, 0.8f));
                    }

        // Lights
        if (State.ShowLights)
            for (int y = 1; y <= mapH; y++)
                for (int x = 1; x <= mapW; x++)
                    if (Map.Tiles[x, y].HasLight)
                    {
                        ref var tile = ref Map.Tiles[x, y];
                        var center = new Vector2((x + 0.5f) * TileSize, (y + 0.5f) * TileSize);
                        float radius = tile.LightRange * TileSize * 0.5f;
                        var lightCol = new Color(tile.LightR / 255f, tile.LightG / 255f, tile.LightB / 255f);
                        DrawCircle(center, Math.Max(radius, TileSize * 0.4f),
                            new Color(lightCol.R, lightCol.G, lightCol.B, 0.15f));
                        DrawCircle(center, 4f, new Color(lightCol.R, lightCol.G, lightCol.B, 0.8f));
                        if (radius > TileSize * 0.5f)
                            DrawArc(center, radius, 0, MathF.Tau, 24,
                                new Color(lightCol.R, lightCol.G, lightCol.B, 0.3f), 1f);
                    }

        // Particle indicators
        if (State.ShowParticles)
            for (int y = 1; y <= mapH; y++)
                for (int x = 1; x <= mapW; x++)
                    if (Map.Tiles[x, y].ParticleGroup > 0)
                    {
                        var center = new Vector2((x + 0.5f) * TileSize, (y + 0.5f) * TileSize);
                        var particleCol = EditorTheme.OVERLAY_PARTICLE;
                        float s = TileSize * 0.25f;
                        DrawLine(center + new Vector2(0, -s), center + new Vector2(s, 0), particleCol, 1.5f);
                        DrawLine(center + new Vector2(s, 0), center + new Vector2(0, s), particleCol, 1.5f);
                        DrawLine(center + new Vector2(0, s), center + new Vector2(-s, 0), particleCol, 1.5f);
                        DrawLine(center + new Vector2(-s, 0), center + new Vector2(0, -s), particleCol, 1.5f);
                        DrawString(ThemeDB.FallbackFont,
                            new Vector2(x * TileSize + 2, y * TileSize + 10),
                            $"P{Map.Tiles[x, y].ParticleGroup}", HorizontalAlignment.Left, -1, 7,
                            new Color(0, 0.9f, 0.9f, 0.7f));
                    }

        // NPC indicators
        if (State.ShowNpcs)
            for (int y = 1; y <= mapH; y++)
                for (int x = 1; x <= mapW; x++)
                    if (Map.Tiles[x, y].HasNpc)
                    {
                        int npcNum = Map.Tiles[x, y].NpcIndex;
                        DrawCircle(new Vector2((x + 0.5f) * TileSize, (y + 0.5f) * TileSize),
                            TileSize * 0.3f, EditorTheme.OVERLAY_NPC);
                        string npcLabel = NpcDb?.Get(npcNum)?.Name ?? $"N{npcNum}";
                        DrawString(ThemeDB.FallbackFont,
                            new Vector2(x * TileSize + 2, (y + 1) * TileSize - 4),
                            npcLabel, HorizontalAlignment.Left, -1, 7,
                            new Color(1f, 0.7f, 0.3f, 0.8f));
                    }

        // Object indicators
        if (State.ShowObjects)
            for (int y = 1; y <= mapH; y++)
                for (int x = 1; x <= mapW; x++)
                    if (Map.Tiles[x, y].HasObject)
                    {
                        DrawCircle(new Vector2((x + 0.5f) * TileSize, (y + 0.5f) * TileSize),
                            TileSize * 0.2f, EditorTheme.OVERLAY_OBJECT);
                        var obj = Map.Tiles[x, y];
                        DrawString(ThemeDB.FallbackFont,
                            new Vector2(x * TileSize + 2, y * TileSize + 10),
                            $"O{obj.ObjIndex}x{obj.ObjAmount}", HorizontalAlignment.Left, -1, 7,
                            new Color(0.7f, 0.4f, 1f, 0.8f));
                    }

        // Triggers
        for (int y = 1; y <= mapH; y++)
            for (int x = 1; x <= mapW; x++)
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
                    DrawString(ThemeDB.FallbackFont,
                        new Vector2(x * TileSize + 2, y * TileSize + 10),
                        trigName, HorizontalAlignment.Left, -1, 6,
                        new Color(trigColor.R, trigColor.G, trigColor.B, 0.8f));
                }

        // Hover highlight
        if (State.HoverValid && Map.InBounds(State.HoverX, State.HoverY))
        {
            DrawRect(new Rect2(State.HoverX * TileSize, State.HoverY * TileSize,
                TileSize, TileSize), EditorTheme.OVERLAY_HOVER);
            DrawRect(new Rect2(State.HoverX * TileSize, State.HoverY * TileSize,
                TileSize, TileSize), EditorTheme.OVERLAY_HOVER with { A = 0.4f }, false, 1f);
        }

        // Selection rectangle
        if (State.HasSelection)
        {
            var selRect = new Rect2(
                State.SelX1 * TileSize, State.SelY1 * TileSize,
                (State.SelX2 - State.SelX1 + 1) * TileSize,
                (State.SelY2 - State.SelY1 + 1) * TileSize);
            DrawRect(selRect, EditorTheme.OVERLAY_SELECTION);
            DrawRect(selRect, EditorTheme.OVERLAY_SELECTION with { A = 0.7f }, false, 2f);
        }

        // Active selection being drawn
        if (_isSelecting)
        {
            int sx1 = Math.Min(_selectStart.X, _dragCurrent.X);
            int sy1 = Math.Min(_selectStart.Y, _dragCurrent.Y);
            int sx2 = Math.Max(_selectStart.X, _dragCurrent.X);
            int sy2 = Math.Max(_selectStart.Y, _dragCurrent.Y);
            var selRect = new Rect2(sx1 * TileSize, sy1 * TileSize,
                (sx2 - sx1 + 1) * TileSize, (sy2 - sy1 + 1) * TileSize);
            DrawRect(selRect, new Color(1f, 1f, 0.2f, 0.2f));
            DrawRect(selRect, new Color(1f, 1f, 0.2f, 0.8f), false, 2f);
        }
    }

    #region GRH Drawing

    private void DrawTileGrh(int grhIndex, int tileX, int tileY,
        bool center = false, Color? modulate = null)
    {
        if (grhIndex <= 0 || Grhs == null || Textures == null) return;
        if (grhIndex >= Grhs.Length) return;

        var grh = Grhs[grhIndex];
        if (grh.NumFrames > 1 && grh.Frames != null && grh.Frames.Length > 0)
        {
            int frameIdx = grh.Frames[0];
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

    private void DrawTileGrhOffset(int grhIndex, int tileX, int tileY, int ofsX, int ofsY)
    {
        if (grhIndex <= 0 || Grhs == null || Textures == null) return;
        if (grhIndex >= Grhs.Length) return;

        var grh = Grhs[grhIndex];
        if (grh.NumFrames > 1 && grh.Frames != null && grh.Frames.Length > 0)
        {
            int frameIdx = grh.Frames[0];
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
        DrawTextureRectRegion(texture, destRect, srcRect, Colors.White);
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
    private void StampMosaicPattern(TextureRef texRef, int hoverX, int hoverY)
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
                SetLayerGrh(ref Map.Tiles[tx, ty], State.ActiveLayer, (short)grhIdx);
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
                State.Zoom = Math.Max(State.Zoom / 1.15f, 0.15f);

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

        // Right click: eyedrop (capture GRH from tile)
        if (mb.ButtonIndex == MouseButton.Right)
        {
            if (mb.Pressed && !_isPainting)
            {
                var tile = ScreenToTile(mb.Position);
                EyedropAt(tile.X, tile.Y);
            }
            else if (!mb.Pressed && _isPanning)
            {
                _isPanning = false;
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
                // Double-click: follow exits (any tool)
                if (mb.DoubleClick && Map!.InBounds(tile.X, tile.Y))
                {
                    ref var t = ref Map.Tiles[tile.X, tile.Y];
                    if (t.HasExit)
                    {
                        State!.RequestExitFollow(t.ExitMap, t.ExitX, t.ExitY);
                        return;
                    }
                }

                switch (State!.ActiveTool)
                {
                    case EditorTool.Hand:
                        StartPan(mb.Position);
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
                        _isSelecting = true;
                        _selectStart = tile;
                        _dragCurrent = tile;
                        break;
                    case EditorTool.Move:
                        if (State.HasSelection && Map != null)
                        {
                            _isDragging = true;
                            _dragStart = tile;
                            _dragCurrent = tile;

                            // Snapshot entire map for live restore during drag
                            _moveSelX1 = State.SelX1;
                            _moveSelY1 = State.SelY1;
                            _moveSelW = State.SelX2 - State.SelX1 + 1;
                            _moveSelH = State.SelY2 - State.SelY1 + 1;
                            _moveSnapshot = new MapTile[Map.Width + 1, Map.Height + 1];
                            Array.Copy(Map.Tiles, _moveSnapshot, Map.Tiles.Length);

                            // Copy selection tiles to buffer
                            _moveBuffer = new MapTile[_moveSelW, _moveSelH];
                            for (int y = 0; y < _moveSelH; y++)
                                for (int x = 0; x < _moveSelW; x++)
                                    if (Map.InBounds(_moveSelX1 + x, _moveSelY1 + y))
                                        _moveBuffer[x, y] = Map.Tiles[_moveSelX1 + x, _moveSelY1 + y];

                            // Apply live: clear source on map
                            ApplyLiveMove();
                        }
                        break;
                    case EditorTool.Pick:
                        PickStartAt(tile.X, tile.Y);
                        break;
                    case EditorTool.Fill:
                        if (State.SelectedTexture != null)
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
                    case EditorTool.Light:
                    case EditorTool.Exit:
                    case EditorTool.Object:
                    case EditorTool.Trigger:
                        State.ShowTileProperties = true;
                        State.PropTileX = tile.X;
                        State.PropTileY = tile.Y;
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
                    State!.SetSelection(_selectStart.X, _selectStart.Y, _dragCurrent.X, _dragCurrent.Y);
                    QueueRedraw();
                }
                if (_isDragging)
                {
                    _isDragging = false;
                    if (_moveBuffer != null && _moveSnapshot != null && Map != null)
                    {
                        // Restore map to original before entering pending mode
                        Array.Copy(_moveSnapshot, Map.Tiles, Map.Tiles.Length);

                        var delta = tile - _dragStart;
                        int destX = _moveSelX1 + delta.X;
                        int destY = _moveSelY1 + delta.Y;

                        // Enter pending placement mode (user confirms with ✓ or cancels with ✗)
                        State!.Pending.Begin(_moveBuffer, _moveSelW, _moveSelH,
                            destX, destY, isMove: true, snapshot: _moveSnapshot,
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
            if (State!.ActiveTool == EditorTool.Paint || State.ActiveTool == EditorTool.Block)
                ApplyToolAt(tile.X, tile.Y);
            else
                EraseAt(tile.X, tile.Y);
            return;
        }

        if (_isSelecting)
        {
            _dragCurrent = hoverTile;
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

        // Redraw on hover for paint preview and pending placement
        if (State.ActiveTool == EditorTool.Paint || State.Pending.Active)
            QueueRedraw();
    }

    #endregion

    #region Pick Tool

    /// <summary>
    /// Detect what entity is at tile (x,y) and start dragging it.
    /// Priority: NPC > Object > Layer3 > Layer4.
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
                SetLayerGrh(ref Map.Tiles[tx, ty], State.ActiveLayer, (short)grhIdx);
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
                SetLayerGrh(ref Map.Tiles[tx, ty], State.ActiveLayer, (short)State.EyedropGrh);
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

    private void SetLayerGrh(ref MapTile tile, int layer, short grhIdx)
    {
        switch (layer)
        {
            case 1: tile.Layer1 = grhIdx; break;
            case 2: tile.Layer2 = grhIdx; break;
            case 3: tile.Layer3 = grhIdx; break;
            case 4: tile.Layer4 = grhIdx; break;
        }
    }

    private short GetLayerGrh(ref MapTile tile, int layer)
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
        short grh = GetLayerGrh(ref Map.Tiles[tx, ty], State.ActiveLayer);

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

    public void FloodFill(int startX, int startY)
    {
        if (Map == null || State == null) return;
        if (!Map.InBounds(startX, startY)) return;

        int layer = State.ActiveLayer;
        short targetGrh = GetLayerGrh(ref Map.Tiles[startX, startY], layer);

        // Determine fill source (mosaic-aware or single GRH)
        var texRef = State.SelectedTexture;
        short singleFillGrh = 0;
        if (texRef == null)
        {
            if (State.EyedropGrh > 0)
                singleFillGrh = (short)State.EyedropGrh;
            else
                return;
            if (targetGrh == singleFillGrh) return;
        }
        else
        {
            // Check no-op: would the start tile get the same GRH?
            int startFillGrh = GetMosaicGrh(texRef, startX, startY);
            if (targetGrh == (short)startFillGrh) return;
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
            short fillGrh;
            if (texRef != null)
            {
                // Mosaic with user offset
                fillGrh = (short)GetMosaicGrh(texRef, x, y);
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
