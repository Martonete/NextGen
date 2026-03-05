using System;
using Godot;
using AOWorldEditor.Data;

namespace AOWorldEditor.Editor;

/// <summary>
/// Renders the 100x100 map with all 4 layers, grid, overlays.
/// Handles panning, zooming, and tile interaction (paint, select, move).
/// </summary>
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
    public int[]? ObjGrhs;      // ObjIndex → GrhIndex
    public int[]? NpcBodies;    // NpcIndex → BodyIndex
    public int[]? NpcBodyGrhs;  // BodyIndex → south-walk GrhIndex

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

    private readonly System.Collections.Generic.HashSet<long> _paintedThisStroke = new();

    // Additive blend child for particles
    private ParticleOverlay? _particleOverlay;

    public override void _Ready()
    {
        _particleOverlay = new ParticleOverlay { Viewport = this };
        _particleOverlay.Material = new CanvasItemMaterial
        {
            BlendMode = CanvasItemMaterial.BlendModeEnum.Add
        };
        _particleOverlay.ZIndex = 1;
        AddChild(_particleOverlay);
    }

    public override void _Draw()
    {
        // Dark background
        DrawRect(new Rect2(Vector2.Zero, Size), new Color(0.1f, 0.1f, 0.12f, 1f));

        if (Map == null || Grhs == null || Textures == null || State == null) return;

        DrawSetTransform(State.CameraOffset, 0f, new Vector2(State.Zoom, State.Zoom));

        int mapW = Map.Width;
        int mapH = Map.Height;

        // ─── Layer 1: Ground terrain ───
        if (State.ShowLayer1)
        {
            for (int y = 1; y <= mapH; y++)
                for (int x = 1; x <= mapW; x++)
                    DrawTileGrh(Map.Tiles[x, y].Layer1, x, y);
        }

        // ─── Layer 2: Mask/overlay (borders, paths, water edges) ───
        if (State.ShowLayer2)
        {
            for (int y = 1; y <= mapH; y++)
                for (int x = 1; x <= mapW; x++)
                    if (Map.Tiles[x, y].Layer2 != 0)
                        DrawTileGrh(Map.Tiles[x, y].Layer2, x, y);
        }

        // ─── Layer 3: Objects/trees (centered, bottom-anchored) ───
        if (State.ShowLayer3)
        {
            for (int y = 1; y <= mapH; y++)
                for (int x = 1; x <= mapW; x++)
                    if (Map.Tiles[x, y].Layer3 != 0)
                        DrawTileGrh(Map.Tiles[x, y].Layer3, x, y, center: true);
        }

        // ─── Objects on map (drawn as their GRH sprite, centered) ───
        if (ObjGrhs != null)
        {
            for (int y = 1; y <= mapH; y++)
                for (int x = 1; x <= mapW; x++)
                {
                    int objIdx = Map.Tiles[x, y].ObjIndex;
                    if (objIdx > 0 && objIdx < ObjGrhs.Length && ObjGrhs[objIdx] > 0)
                        DrawTileGrh(ObjGrhs[objIdx], x, y, center: true);
                }
        }

        // ─── NPCs on map (drawn as south-facing body sprite, centered) ───
        if (NpcBodies != null && NpcBodyGrhs != null)
        {
            for (int y = 1; y <= mapH; y++)
                for (int x = 1; x <= mapW; x++)
                {
                    int npcIdx = Map.Tiles[x, y].NpcIndex;
                    if (npcIdx <= 0 || npcIdx >= NpcBodies.Length) continue;
                    int bodyIdx = NpcBodies[npcIdx];
                    if (bodyIdx <= 0 || bodyIdx >= NpcBodyGrhs.Length) continue;
                    int grhIdx = NpcBodyGrhs[bodyIdx];
                    if (grhIdx > 0)
                        DrawTileGrh(grhIdx, x, y, center: true);
                }
        }

        // ─── Layer 4: Roof (centered, bottom-anchored, semi-transparent) ───
        if (State.ShowLayer4)
        {
            for (int y = 1; y <= mapH; y++)
                for (int x = 1; x <= mapW; x++)
                    if (Map.Tiles[x, y].Layer4 != 0)
                        DrawTileGrh(Map.Tiles[x, y].Layer4, x, y, center: true,
                            modulate: new Color(1, 1, 1, 0.7f));
        }

        // ─── Overlays ───
        DrawOverlays(mapW, mapH);

        DrawSetTransform(Vector2.Zero, 0f, Vector2.One);

        // Trigger additive particle layer redraw
        _particleOverlay?.QueueRedraw();
    }

    private void DrawOverlays(int mapW, int mapH)
    {
        if (State == null || Map == null) return;

        // Grid
        if (State.ShowGrid)
        {
            var gridColor = new Color(1, 1, 1, 0.08f);
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
            var blockedColor = new Color(1, 0, 0, 0.25f);
            for (int y = 1; y <= mapH; y++)
                for (int x = 1; x <= mapW; x++)
                    if (Map.Tiles[x, y].Blocked)
                        DrawRect(new Rect2(x * TileSize, y * TileSize, TileSize, TileSize), blockedColor);
        }

        // Exits
        if (State.ShowExits)
        {
            for (int y = 1; y <= mapH; y++)
                for (int x = 1; x <= mapW; x++)
                    if (Map.Tiles[x, y].HasExit)
                    {
                        DrawRect(new Rect2(x * TileSize, y * TileSize, TileSize, TileSize),
                            new Color(0, 1, 0, 0.25f));
                        // Small label with destination
                        var exit = Map.Tiles[x, y];
                        DrawString(ThemeDB.FallbackFont,
                            new Vector2(x * TileSize + 2, (y + 1) * TileSize - 4),
                            $"M{exit.ExitMap}", HorizontalAlignment.Left, -1, 7,
                            new Color(0.5f, 1f, 0.5f, 0.8f));
                    }
        }

        // Light sources — colored circle with range ring
        for (int y = 1; y <= mapH; y++)
        {
            for (int x = 1; x <= mapW; x++)
            {
                if (Map.Tiles[x, y].HasLight)
                {
                    ref var tile = ref Map.Tiles[x, y];
                    var center = new Vector2((x + 0.5f) * TileSize, (y + 0.5f) * TileSize);
                    float radius = tile.LightRange * TileSize * 0.5f;
                    var lightCol = new Color(tile.LightR / 255f, tile.LightG / 255f, tile.LightB / 255f);

                    // Filled glow
                    DrawCircle(center, Math.Max(radius, TileSize * 0.4f),
                        new Color(lightCol.R, lightCol.G, lightCol.B, 0.15f));
                    // Center dot
                    DrawCircle(center, 4f, new Color(lightCol.R, lightCol.G, lightCol.B, 0.8f));
                    // Range ring
                    if (radius > TileSize * 0.5f)
                        DrawArc(center, radius, 0, MathF.Tau, 24,
                            new Color(lightCol.R, lightCol.G, lightCol.B, 0.3f), 1f);
                }
            }
        }

        // Particle indicators
        for (int y = 1; y <= mapH; y++)
        {
            for (int x = 1; x <= mapW; x++)
            {
                if (Map.Tiles[x, y].ParticleGroup > 0)
                {
                    var center = new Vector2((x + 0.5f) * TileSize, (y + 0.5f) * TileSize);
                    // Cyan diamond shape
                    var particleCol = new Color(0, 0.9f, 0.9f, 0.5f);
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
            }
        }

        // NPC indicators
        for (int y = 1; y <= mapH; y++)
        {
            for (int x = 1; x <= mapW; x++)
            {
                if (Map.Tiles[x, y].HasNpc)
                {
                    var center = new Vector2((x + 0.5f) * TileSize, (y + 0.5f) * TileSize);
                    DrawCircle(center, TileSize * 0.3f, new Color(1, 0.5f, 0, 0.4f));
                    DrawString(ThemeDB.FallbackFont,
                        new Vector2(x * TileSize + 2, (y + 1) * TileSize - 4),
                        $"N{Map.Tiles[x, y].NpcIndex}", HorizontalAlignment.Left, -1, 7,
                        new Color(1f, 0.7f, 0.3f, 0.8f));
                }
            }
        }

        // Object indicators
        for (int y = 1; y <= mapH; y++)
        {
            for (int x = 1; x <= mapW; x++)
            {
                if (Map.Tiles[x, y].HasObject)
                {
                    var center = new Vector2((x + 0.5f) * TileSize, (y + 0.5f) * TileSize);
                    DrawCircle(center, TileSize * 0.2f, new Color(0.5f, 0, 1, 0.4f));
                    var obj = Map.Tiles[x, y];
                    DrawString(ThemeDB.FallbackFont,
                        new Vector2(x * TileSize + 2, y * TileSize + 10),
                        $"O{obj.ObjIndex}x{obj.ObjAmount}", HorizontalAlignment.Left, -1, 7,
                        new Color(0.7f, 0.4f, 1f, 0.8f));
                }
            }
        }

        // Trigger indicators
        for (int y = 1; y <= mapH; y++)
        {
            for (int x = 1; x <= mapW; x++)
            {
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
            }
        }

        // Selection rectangle
        if (State.HasSelection)
        {
            var selRect = new Rect2(
                State.SelX1 * TileSize, State.SelY1 * TileSize,
                (State.SelX2 - State.SelX1 + 1) * TileSize,
                (State.SelY2 - State.SelY1 + 1) * TileSize);
            DrawRect(selRect, new Color(0.2f, 0.6f, 1f, 0.15f));
            DrawRect(selRect, new Color(0.2f, 0.6f, 1f, 0.7f), false, 2f);
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

    /// <summary>
    /// Draw a GRH at tile position. Resolves animations to first frame.
    /// center=true anchors large sprites at bottom-center (trees, roofs).
    /// </summary>
    private void DrawTileGrh(int grhIndex, int tileX, int tileY,
        bool center = false, Color? modulate = null)
    {
        if (grhIndex <= 0 || Grhs == null || Textures == null) return;
        if (grhIndex >= Grhs.Length) return;

        var grh = Grhs[grhIndex];

        // For animations, resolve to first frame
        if (grh.NumFrames > 1 && grh.Frames != null && grh.Frames.Length > 0)
        {
            int frameIdx = grh.Frames[0];
            if (frameIdx <= 0 || frameIdx >= Grhs.Length) return;
            grh = Grhs[frameIdx];
        }

        // Now check the resolved (static) GRH
        if (grh.FileNum <= 0 || grh.PixelWidth <= 0 || grh.PixelHeight <= 0) return;

        var texture = Textures.GetTexture(grh.FileNum);
        if (texture == null) return;

        var srcRect = new Rect2(grh.SX, grh.SY, grh.PixelWidth, grh.PixelHeight);
        float drawX = tileX * TileSize;
        float drawY = tileY * TileSize;

        if (center)
        {
            // Center horizontally, anchor at bottom (VB6 behavior for L3/L4)
            drawX += (TileSize - grh.PixelWidth) / 2f;
            drawY += (TileSize - grh.PixelHeight);
        }

        var destRect = new Rect2(drawX, drawY, grh.PixelWidth, grh.PixelHeight);
        DrawTextureRectRegion(texture, destRect, srcRect, modulate ?? Colors.White);
    }

    /// <summary>
    /// Draw all active particle sprites with additive blend.
    /// Called by ParticleOverlay._Draw() on its own canvas (which has additive material).
    /// </summary>
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
                if (!p.Alive || p.GrhIndex <= 0) continue;
                if (p.GrhIndex >= Grhs.Length) continue;

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
            if (localPos.X < 0 || localPos.Y < 0 ||
                localPos.X > Size.X || localPos.Y > Size.Y)
                return;
            HandleMouseButton(mb);
        }
        else if (@event is InputEventMouseMotion mm)
        {
            var localPos = ToPanel(mm.Position);
            if (localPos.X < 0 || localPos.Y < 0 ||
                localPos.X > Size.X || localPos.Y > Size.Y)
                return;
            HandleMouseMotion(mm);
        }
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

        // Pan with middle/right mouse
        if (mb.ButtonIndex == MouseButton.Middle ||
            (mb.ButtonIndex == MouseButton.Right && !_isPainting))
        {
            if (mb.Pressed)
                StartPan(mb.Position);
            else
                _isPanning = false;
            return;
        }

        // Left click
        if (mb.ButtonIndex == MouseButton.Left)
        {
            if (_spaceHeld)
            {
                if (mb.Pressed) StartPan(mb.Position);
                else _isPanning = false;
                return;
            }

            var tile = ScreenToTile(mb.Position);
            if (mb.Pressed)
            {
                switch (State!.ActiveTool)
                {
                    case EditorTool.Paint:
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
                        if (State.HasSelection) { _isDragging = true; _dragStart = tile; }
                        break;
                    case EditorTool.Fill:
                        FloodFill(tile.X, tile.Y);
                        break;
                    case EditorTool.Eyedrop:
                        EyedropAt(tile.X, tile.Y);
                        break;
                    case EditorTool.Light:
                    case EditorTool.Exit:
                    case EditorTool.Npc:
                    case EditorTool.Object:
                    case EditorTool.Trigger:
                        State.ShowTileProperties = true;
                        State.PropTileX = tile.X;
                        State.PropTileY = tile.Y;
                        break;
                }
            }
            else
            {
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
                    var delta = tile - _dragStart;
                    if (delta != Vector2I.Zero) MoveTiles(delta.X, delta.Y);
                }
                if (_isPanning) _isPanning = false;
            }
        }
    }

    private void HandleMouseMotion(InputEventMouseMotion mm)
    {
        if (_isPanning)
        {
            State!.CameraOffset = _panCameraStart + (ToPanel(mm.Position) - _panStart);
            QueueRedraw();
            return;
        }

        var tile = ScreenToTile(mm.Position);

        if (_isPainting)
        {
            if (State!.ActiveTool == EditorTool.Paint || State.ActiveTool == EditorTool.Block)
                ApplyToolAt(tile.X, tile.Y);
            else
                EraseAt(tile.X, tile.Y);
            return;
        }

        if (_isSelecting) { _dragCurrent = tile; QueueRedraw(); }
    }

    #endregion

    #region Tool Actions

    private void ApplyToolAt(int tx, int ty)
    {
        if (Map == null || State == null) return;

        if (State.ActiveTool == EditorTool.Paint && State.SelectedTexture != null)
        {
            var texRef = State.SelectedTexture;
            int tw = Math.Max(texRef.TileWidth, 1);
            int th = Math.Max(texRef.TileHeight, 1);

            int baseX = tx - ((tx - 1) % tw);
            int baseY = ty - ((ty - 1) % th);

            long key = (long)baseX << 32 | (uint)baseY;
            if (_paintedThisStroke.Contains(key)) return;
            _paintedThisStroke.Add(key);

            for (int py = 0; py < th; py++)
            {
                for (int px = 0; px < tw; px++)
                {
                    int tileX = baseX + px;
                    int tileY = baseY + py;
                    if (!Map.InBounds(tileX, tileY)) continue;

                    var before = Map.Tiles[tileX, tileY];
                    int grhIdx = texRef.GetGrhAt(px, py);
                    SetLayerGrh(ref Map.Tiles[tileX, tileY], State.ActiveLayer, (short)grhIdx);
                    Undo?.RecordTileChange(tileX, tileY, before, Map.Tiles[tileX, tileY]);
                }
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
        State.ActiveTool = EditorTool.Paint;
    }

    private void FloodFill(int startX, int startY)
    {
        if (Map == null || State == null || State.SelectedTexture == null) return;
        if (!Map.InBounds(startX, startY)) return;

        int layer = State.ActiveLayer;
        short targetGrh = GetLayerGrh(ref Map.Tiles[startX, startY], layer);
        short fillGrh = (short)State.SelectedTexture.GrhIndex;
        if (targetGrh == fillGrh) return;

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
            SetLayerGrh(ref Map.Tiles[x, y], layer, fillGrh);
            Undo?.RecordTileChange(x, y, before, Map.Tiles[x, y]);
            filled++;

            queue.Enqueue((x + 1, y)); queue.Enqueue((x - 1, y));
            queue.Enqueue((x, y + 1)); queue.Enqueue((x, y - 1));
        }

        Undo?.EndBatch();
        QueueRedraw();
    }

    private void MoveTiles(int dx, int dy)
    {
        if (Map == null || State == null || !State.HasSelection) return;

        Undo?.BeginBatch("Move");

        int w = State.SelX2 - State.SelX1 + 1;
        int h = State.SelY2 - State.SelY1 + 1;
        var buffer = new MapTile[w, h];

        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
                if (Map.InBounds(State.SelX1 + x, State.SelY1 + y))
                    buffer[x, y] = Map.Tiles[State.SelX1 + x, State.SelY1 + y];

        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                int sx = State.SelX1 + x, sy = State.SelY1 + y;
                if (!Map.InBounds(sx, sy)) continue;
                var before = Map.Tiles[sx, sy];
                Map.Tiles[sx, sy] = new MapTile();
                Undo?.RecordTileChange(sx, sy, before, Map.Tiles[sx, sy]);
            }

        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                int dx2 = State.SelX1 + x + dx, dy2 = State.SelY1 + y + dy;
                if (!Map.InBounds(dx2, dy2)) continue;
                var before = Map.Tiles[dx2, dy2];
                Map.Tiles[dx2, dy2] = buffer[x, y];
                Undo?.RecordTileChange(dx2, dy2, before, Map.Tiles[dx2, dy2]);
            }

        State.SetSelection(State.SelX1 + dx, State.SelY1 + dy, State.SelX2 + dx, State.SelY2 + dy);

        Undo?.EndBatch();
        QueueRedraw();
    }

    #endregion
}

/// <summary>
/// Child Control with additive blend material for rendering particles.
/// </summary>
public partial class ParticleOverlay : Control
{
    public MapViewport? Viewport;

    public override void _Draw()
    {
        Viewport?.DrawParticlesOn(this);
    }
}
