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

    // Interaction state
    private bool _isPainting;
    private bool _isPanning;
    private bool _isSelecting;
    private bool _isDragging;
    private bool _spaceHeld; // Space key held for pan mode
    private Vector2 _panStart;
    private Vector2 _panCameraStart;
    private Vector2I _selectStart;
    private Vector2I _dragStart;
    private Vector2I _dragCurrent;

    // Track painted tiles in current stroke to avoid duplicates
    private readonly System.Collections.Generic.HashSet<long> _paintedThisStroke = new();

    private int _debugCounter;

    public override void _Ready()
    {
    }

    public override void _Draw()
    {
        // Dark background
        DrawRect(new Rect2(Vector2.Zero, Size), new Color(0.1f, 0.1f, 0.12f, 1f));

        if (_debugCounter++ % 120 == 0)
        {
            GD.Print($"[MapViewport] _Draw Size={Size} Map={Map != null} Grhs={Grhs != null} Tex={Textures != null} State={State != null}");
            if (Map != null && Grhs != null)
                GD.Print($"[MapViewport] MapW={Map.Width} MapH={Map.Height} GrhCount={Grhs.Length} Zoom={State?.Zoom} Cam={State?.CameraOffset}");
        }

        if (Map == null || Grhs == null || Textures == null || State == null) return;

        DrawSetTransform(State.CameraOffset, 0f, new Vector2(State.Zoom, State.Zoom));

        int mapW = Map.Width;
        int mapH = Map.Height;

        // Calculate visible tile range for culling
        var viewSize = Size;
        int startX = Math.Max(1, (int)(-State.CameraOffset.X / (TileSize * State.Zoom)));
        int startY = Math.Max(1, (int)(-State.CameraOffset.Y / (TileSize * State.Zoom)));
        int endX = Math.Min(mapW, startX + (int)(viewSize.X / (TileSize * State.Zoom)) + 3);
        int endY = Math.Min(mapH, startY + (int)(viewSize.Y / (TileSize * State.Zoom)) + 3);

        // Layer 1 - Ground terrain
        if (State.ShowLayer1)
        {
            for (int y = startY; y <= endY; y++)
                for (int x = startX; x <= endX; x++)
                    DrawTileGrh(Map.Tiles[x, y].Layer1, x, y);
        }

        // Layer 2 - Mask/overlay
        if (State.ShowLayer2)
        {
            for (int y = startY; y <= endY; y++)
                for (int x = startX; x <= endX; x++)
                    if (Map.Tiles[x, y].Layer2 != 0)
                        DrawTileGrh(Map.Tiles[x, y].Layer2, x, y);
        }

        // Layer 3 - Objects/trees
        if (State.ShowLayer3)
        {
            for (int y = startY; y <= endY; y++)
                for (int x = startX; x <= endX; x++)
                    if (Map.Tiles[x, y].Layer3 != 0)
                        DrawTileGrh(Map.Tiles[x, y].Layer3, x, y, center: true);
        }

        // Layer 4 - Roof
        if (State.ShowLayer4)
        {
            for (int y = startY; y <= endY; y++)
                for (int x = startX; x <= endX; x++)
                    if (Map.Tiles[x, y].Layer4 != 0)
                        DrawTileGrh(Map.Tiles[x, y].Layer4, x, y);
        }

        // Grid overlay
        if (State.ShowGrid)
        {
            var gridColor = new Color(1, 1, 1, 0.08f);
            for (int y = startY; y <= endY + 1; y++)
                DrawLine(new Vector2(startX * TileSize, y * TileSize),
                         new Vector2((endX + 1) * TileSize, y * TileSize), gridColor);
            for (int x = startX; x <= endX + 1; x++)
                DrawLine(new Vector2(x * TileSize, startY * TileSize),
                         new Vector2(x * TileSize, (endY + 1) * TileSize), gridColor);
        }

        // Blocked overlay
        if (State.ShowBlocked)
        {
            var blockedColor = new Color(1, 0, 0, 0.25f);
            for (int y = startY; y <= endY; y++)
                for (int x = startX; x <= endX; x++)
                    if (Map.Tiles[x, y].Blocked)
                        DrawRect(new Rect2(x * TileSize, y * TileSize, TileSize, TileSize), blockedColor);
        }

        // Exit overlay
        if (State.ShowExits)
        {
            var exitColor = new Color(0, 1, 0, 0.3f);
            for (int y = startY; y <= endY; y++)
                for (int x = startX; x <= endX; x++)
                    if (Map.Tiles[x, y].HasExit)
                        DrawRect(new Rect2(x * TileSize, y * TileSize, TileSize, TileSize), exitColor);
        }

        // Light source overlay
        for (int y = startY; y <= endY; y++)
        {
            for (int x = startX; x <= endX; x++)
            {
                if (Map.Tiles[x, y].HasLight)
                {
                    var lightColor = new Color(
                        Map.Tiles[x, y].LightR / 255f,
                        Map.Tiles[x, y].LightG / 255f,
                        Map.Tiles[x, y].LightB / 255f, 0.4f);
                    float radius = Map.Tiles[x, y].LightRange * TileSize * 0.5f;
                    DrawCircle(new Vector2((x + 0.5f) * TileSize, (y + 0.5f) * TileSize),
                               Math.Max(radius, TileSize * 0.3f), lightColor);
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

        // NPC indicators
        var npcColor = new Color(1, 0.5f, 0, 0.4f);
        for (int y = startY; y <= endY; y++)
            for (int x = startX; x <= endX; x++)
                if (Map.Tiles[x, y].HasNpc)
                    DrawCircle(new Vector2((x + 0.5f) * TileSize, (y + 0.5f) * TileSize),
                               TileSize * 0.3f, npcColor);

        // Object indicators
        var objColor = new Color(0.5f, 0, 1, 0.4f);
        for (int y = startY; y <= endY; y++)
            for (int x = startX; x <= endX; x++)
                if (Map.Tiles[x, y].HasObject)
                    DrawCircle(new Vector2((x + 0.5f) * TileSize, (y + 0.5f) * TileSize),
                               TileSize * 0.25f, objColor);

        // Trigger indicators
        for (int y = startY; y <= endY; y++)
        {
            for (int x = startX; x <= endX; x++)
            {
                if (Map.Tiles[x, y].Trigger > 0)
                {
                    var trigColor = Map.Tiles[x, y].Trigger switch
                    {
                        1 => new Color(0.5f, 0.5f, 0.5f, 0.3f), // Indoor
                        4 => new Color(0, 0.7f, 1, 0.3f),        // SafeZone
                        6 => new Color(1, 0, 0, 0.2f),            // CombatZone
                        _ => new Color(1, 1, 0, 0.2f),
                    };
                    DrawRect(new Rect2(x * TileSize + 1, y * TileSize + 1, TileSize - 2, TileSize - 2), trigColor);
                }
            }
        }

        DrawSetTransform(Vector2.Zero, 0f, Vector2.One);
    }

    private void DrawTileGrh(int grhIndex, int tileX, int tileY, bool center = false)
    {
        if (grhIndex <= 0 || Grhs == null || Textures == null) return;
        if (grhIndex >= Grhs.Length) return;

        var grh = Grhs[grhIndex];
        if (grh.NumFrames <= 0 || grh.FileNum <= 0) return;

        // For animations, resolve to first frame
        int frameGrh = grhIndex;
        if (grh.NumFrames > 1 && grh.Frames != null && grh.Frames.Length > 0)
        {
            frameGrh = grh.Frames[0];
            if (frameGrh <= 0 || frameGrh >= Grhs.Length) return;
            grh = Grhs[frameGrh];
        }

        var texture = Textures.GetTexture(grh.FileNum);
        if (texture == null) return;

        var srcRect = new Rect2(grh.SX, grh.SY, grh.PixelWidth, grh.PixelHeight);
        float drawX = tileX * TileSize;
        float drawY = tileY * TileSize;

        if (center && (grh.PixelWidth > TileSize || grh.PixelHeight > TileSize))
        {
            drawX += (TileSize - grh.PixelWidth) / 2f;
            drawY += (TileSize - grh.PixelHeight);
        }

        var destRect = new Rect2(drawX, drawY, grh.PixelWidth, grh.PixelHeight);
        DrawTextureRectRegion(texture, destRect, srcRect);
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
            // Only handle mouse events inside our rect
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
        // Zoom (towards mouse cursor)
        if (mb.ButtonIndex == MouseButton.WheelUp || mb.ButtonIndex == MouseButton.WheelDown)
        {
            float oldZoom = State!.Zoom;
            if (mb.ButtonIndex == MouseButton.WheelUp)
                State.Zoom = Math.Min(State.Zoom * 1.15f, 4f);
            else
                State.Zoom = Math.Max(State.Zoom / 1.15f, 0.15f);

            // Zoom towards mouse position (in panel-local coords)
            var localPos = ToPanel(mb.Position);
            float zoomRatio = State.Zoom / oldZoom;
            State.CameraOffset = localPos - (localPos - State.CameraOffset) * zoomRatio;
            QueueRedraw();
            return;
        }

        // Pan with middle mouse OR right mouse
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
            // Space+Left = pan
            if (_spaceHeld)
            {
                if (mb.Pressed)
                    StartPan(mb.Position);
                else
                    _isPanning = false;
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
                        if (State.HasSelection)
                        {
                            _isDragging = true;
                            _dragStart = tile;
                        }
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
                if (_isPainting)
                {
                    _isPainting = false;
                    Undo?.EndBatch();
                }
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
                    if (delta != Vector2I.Zero)
                        MoveTiles(delta.X, delta.Y);
                }
                if (_isPanning)
                    _isPanning = false;
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

        if (_isSelecting)
        {
            _dragCurrent = tile;
            QueueRedraw();
        }
    }

    #endregion

    #region Tool Actions

    private void ApplyToolAt(int tx, int ty)
    {
        if (Map == null || State == null) return;

        // For multi-tile textures, apply the full pattern
        if (State.ActiveTool == EditorTool.Paint && State.SelectedTexture != null)
        {
            var texRef = State.SelectedTexture;
            int tw = Math.Max(texRef.TileWidth, 1);
            int th = Math.Max(texRef.TileHeight, 1);

            // Snap to pattern grid
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
            1 => tile.Layer1,
            2 => tile.Layer2,
            3 => tile.Layer3,
            4 => tile.Layer4,
            _ => 0
        };
    }

    private void EyedropAt(int tx, int ty)
    {
        if (Map == null || State == null || !Map.InBounds(tx, ty)) return;
        // TODO: find matching TextureRef from catalog
        // For now, just switch to paint mode
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

        int maxFill = 10000; // Safety limit
        int filled = 0;

        while (queue.Count > 0 && filled < maxFill)
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

            queue.Enqueue((x + 1, y));
            queue.Enqueue((x - 1, y));
            queue.Enqueue((x, y + 1));
            queue.Enqueue((x, y - 1));
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

        // Copy source tiles
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
                if (Map.InBounds(State.SelX1 + x, State.SelY1 + y))
                    buffer[x, y] = Map.Tiles[State.SelX1 + x, State.SelY1 + y];

        // Clear source
        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                int sx = State.SelX1 + x, sy = State.SelY1 + y;
                if (!Map.InBounds(sx, sy)) continue;
                var before = Map.Tiles[sx, sy];
                Map.Tiles[sx, sy] = new MapTile();
                Undo?.RecordTileChange(sx, sy, before, Map.Tiles[sx, sy]);
            }
        }

        // Place at destination
        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                int dx2 = State.SelX1 + x + dx, dy2 = State.SelY1 + y + dy;
                if (!Map.InBounds(dx2, dy2)) continue;
                var before = Map.Tiles[dx2, dy2];
                Map.Tiles[dx2, dy2] = buffer[x, y];
                Undo?.RecordTileChange(dx2, dy2, before, Map.Tiles[dx2, dy2]);
            }
        }

        // Update selection to new position
        State.SetSelection(State.SelX1 + dx, State.SelY1 + dy, State.SelX2 + dx, State.SelY2 + dy);

        Undo?.EndBatch();
        QueueRedraw();
    }

    #endregion
}
