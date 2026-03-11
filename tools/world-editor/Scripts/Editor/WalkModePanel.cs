#nullable enable
using System;
using System.IO;
using Godot;
using AOWorldEditor.Data;

namespace AOWorldEditor.Editor;

/// <summary>
/// Walk mode: simulates the in-game AO view. Renders 17x13 visible tiles,
/// a centered character with walk animation, roof/tree transparency, and
/// blocked tile collision. Shows NPCs, objects, exits on the map.
/// Supports map transitions via exit tiles.
/// </summary>
public partial class WalkModePanel : Control
{
    // ── Dependencies ────────────────────────────────────────────────────────
    public MapData? Map;
    public GrhData[]? Grhs;
    public TextureManager? Textures;
    public BodyAnimData[]? Bodies;
    public HeadAnimData[]? Heads;

    // NPC/Object rendering data (same arrays as MapViewport)
    public int[]? ObjGrhs;
    public int[]? NpcBodies;
    public int[]? NpcHeads;
    public int[]? NpcBodyGrhs;
    public int[]? NpcHeadOfsX;
    public int[]? NpcHeadOfsY;
    public int[]? HeadGrhs;

    // Map loading — for exit tile transitions
    public string MapDir = "";  // directory where Mapa{N}.map/.inf/.dat live

    // ── Constants (matching AO client) ──────────────────────────────────────
    private const int TileSize = 32;
    private const int HalfTileSize = TileSize / 2; // 16
    private const int HalfTilesX = 8;
    private const int HalfTilesY = 6;
    private const int ViewTilesX = HalfTilesX * 2 + 1; // 17
    private const int ViewTilesY = HalfTilesY * 2 + 1; // 13
    private const int ViewWidth = ViewTilesX * TileSize;  // 544
    private const int ViewHeight = ViewTilesY * TileSize; // 416
    private const int ExtraTiles = 3;       // extra tiles beyond viewport for L1 scroll coverage
    private const int ExtraTilesLarge = 12; // extra tiles for L2/L3/L4 (large multi-tile GRHs like roofs)

    // Movement: AO uses ScrollPixels=8 per 40ms tick → 200 pixels/sec
    private const float PixelsPerSecond = 200f;

    // Walk animation: AO walk cycles have ~6 frames, one tile = 32px @ 200px/s = 0.16s
    private const float WalkAnimFps = 37.5f;

    // ── Character state ─────────────────────────────────────────────────────
    public int CharX = 50, CharY = 50; // current tile position (1-indexed)
    public int BodyIndex = 1;
    public int HeadIndex = 1;

    private int _heading = 3; // 1=N 2=E 3=S 4=W
    private bool _isMoving;
    private float _moveOffsetX, _moveOffsetY; // pixel offset during smooth scroll
    private float _walkFrame;
    private double _globalTime; // ms, for tile animations

    // ── Input state ─────────────────────────────────────────────────────────
    private bool _keyUp, _keyDown, _keyLeft, _keyRight;

    // ── Roof detection cache (updated per tile move) ────────────────────────
    private bool _underRoof;
    private bool _diagPrinted;

    public override void _Ready()
    {
        CustomMinimumSize = new Vector2(ViewWidth, ViewHeight);
        Size = new Vector2(ViewWidth, ViewHeight);
        ClipContents = true; // clip rendering to viewport rect
        FocusMode = FocusModeEnum.All;
        GrabFocus();
    }

    public override void _Process(double delta)
    {
        _globalTime += delta * 1000.0;

        if (_isMoving)
        {
            float advance = PixelsPerSecond * (float)delta;

            if (_moveOffsetX != 0)
            {
                float sign = Math.Sign(_moveOffsetX);
                _moveOffsetX -= sign * advance;
                if (Math.Sign(_moveOffsetX) != sign)
                    _moveOffsetX = 0;
            }
            if (_moveOffsetY != 0)
            {
                float sign = Math.Sign(_moveOffsetY);
                _moveOffsetY -= sign * advance;
                if (Math.Sign(_moveOffsetY) != sign)
                    _moveOffsetY = 0;
            }

            _walkFrame += (float)(delta * WalkAnimFps);

            if (_moveOffsetX == 0 && _moveOffsetY == 0)
            {
                _isMoving = false;
                _walkFrame = 0;

                // Check for map exit AFTER arriving at new tile
                CheckExitTile();

                TryMoveFromInput();
            }
        }
        else
        {
            TryMoveFromInput();
        }

        QueueRedraw();
    }

    private void TryMoveFromInput()
    {
        if (_isMoving) return;
        if (_keyUp) TryMove(0, -1, 1);
        else if (_keyDown) TryMove(0, 1, 3);
        else if (_keyLeft) TryMove(-1, 0, 4);
        else if (_keyRight) TryMove(1, 0, 2);
    }

    private void TryMove(int dx, int dy, int heading)
    {
        _heading = heading;
        if (Map == null) return;

        int nx = CharX + dx;
        int ny = CharY + dy;

        if (!Map.InBounds(nx, ny)) return;
        if (Map.Tiles[nx, ny].Blocked) return;

        CharX = nx;
        CharY = ny;
        _isMoving = true;
        _moveOffsetX = dx * TileSize;
        _moveOffsetY = dy * TileSize;
        _walkFrame = 0;

        _underRoof = IsUnderRoof(CharX, CharY);
    }

    // ── Map exit / transition ────────────────────────────────────────────────

    private void CheckExitTile()
    {
        if (Map == null || MapDir.Length == 0) return;
        if (!Map.InBounds(CharX, CharY)) return;

        ref var tile = ref Map.Tiles[CharX, CharY];
        if (tile.ExitMap <= 0) return;

        int destMap = tile.ExitMap;
        int destX = tile.ExitX;
        int destY = tile.ExitY;

        // Check destination map files exist before loading
        string mapFile = Path.Combine(MapDir, $"Mapa{destMap}.map");
        if (!File.Exists(mapFile))
        {
            GD.Print($"[WalkMode] Exit to map {destMap} — file not found: {mapFile}");
            return;
        }

        GD.Print($"[WalkMode] Warp: Mapa{Map.MapNumber} ({CharX},{CharY}) → Mapa{destMap} ({destX},{destY})");

        // Load the new map
        var newMap = MapLoader.Load(MapDir, destMap);

        // Validate destination coordinates
        if (!newMap.InBounds(destX, destY))
        {
            GD.Print($"[WalkMode] Invalid destination ({destX},{destY}) for map {destMap}");
            return;
        }

        // Switch to new map
        Map = newMap;
        CharX = destX;
        CharY = destY;
        _isMoving = false;
        _moveOffsetX = 0;
        _moveOffsetY = 0;
        _walkFrame = 0;
        _underRoof = IsUnderRoof(CharX, CharY);
        _diagPrinted = false; // print diagnostics for new map

        // Update window title
        var parentWindow = GetParent<Window>();
        if (parentWindow != null)
            parentWindow.Title = $"Modo Caminata — Mapa {destMap}: {newMap.Name}";
    }

    private bool IsUnderRoof(int cx, int cy)
    {
        if (Map == null) return false;
        for (int dy = -1; dy <= 1; dy++)
            for (int dx = -1; dx <= 1; dx++)
            {
                int tx = cx + dx, ty = cy + dy;
                if (Map.InBounds(tx, ty) && Map.Tiles[tx, ty].Layer4 > 0)
                    return true;
            }
        return false;
    }

    // ── Input handling ──────────────────────────────────────────────────────

    public override void _GuiInput(InputEvent @event)
    {
        if (@event is InputEventKey key)
        {
            bool pressed = key.Pressed;
            switch (key.Keycode)
            {
                case Key.W: case Key.Up: _keyUp = pressed; break;
                case Key.S: case Key.Down: _keyDown = pressed; break;
                case Key.A: case Key.Left: _keyLeft = pressed; break;
                case Key.D: case Key.Right: _keyRight = pressed; break;
                case Key.Escape:
                    if (pressed) GetParent<Window>()?.Hide();
                    break;
            }
            AcceptEvent();
        }
        else if (@event is InputEventMouseButton mb && mb.Pressed && mb.ButtonIndex == MouseButton.Left)
        {
            if (mb.ShiftPressed && Map != null)
            {
                float worldX = mb.Position.X - _moveOffsetX;
                float worldY = mb.Position.Y - _moveOffsetY;
                int tx = (int)Math.Floor(worldX / TileSize) - HalfTilesX + CharX;
                int ty = (int)Math.Floor(worldY / TileSize) - HalfTilesY + CharY;
                if (Map.InBounds(tx, ty))
                {
                    CharX = tx;
                    CharY = ty;
                    _isMoving = false;
                    _moveOffsetX = 0;
                    _moveOffsetY = 0;
                    _walkFrame = 0;
                    _underRoof = IsUnderRoof(CharX, CharY);
                    CheckExitTile();
                }
            }
            AcceptEvent();
        }
    }

    // ── Rendering ───────────────────────────────────────────────────────────

    public override void _Draw()
    {
        if (Map == null || Grhs == null || Textures == null) return;

        if (!_diagPrinted)
        {
            _diagPrinted = true;
            GD.Print($"[WalkMode] DIAG: MapDir=\"{MapDir}\" Map#={Map.MapNumber} Grhs={Grhs.Length}");
            GD.Print($"[WalkMode] DIAG: ObjGrhs={(ObjGrhs != null ? ObjGrhs.Length.ToString() : "NULL")} NpcBodies={(NpcBodies != null ? NpcBodies.Length.ToString() : "NULL")} NpcBodyGrhs={(NpcBodyGrhs != null ? NpcBodyGrhs.Length.ToString() : "NULL")} HeadGrhs={(HeadGrhs != null ? HeadGrhs.Length.ToString() : "NULL")}");
            // Count tiles with NPCs, objects, exits
            int npcCount = 0, objCount = 0, exitCount = 0;
            for (int y = 1; y <= 100; y++)
                for (int x = 1; x <= 100; x++)
                {
                    if (Map.InBounds(x, y))
                    {
                        if (Map.Tiles[x, y].NpcIndex > 0) npcCount++;
                        if (Map.Tiles[x, y].ObjIndex > 0) objCount++;
                        if (Map.Tiles[x, y].ExitMap > 0) exitCount++;
                    }
                }
            GD.Print($"[WalkMode] DIAG: Map has {npcCount} NPC tiles, {objCount} Object tiles, {exitCount} Exit tiles");
            if (npcCount > 0 && NpcBodies != null)
            {
                // Print first NPC for debugging
                for (int y = 1; y <= 100; y++)
                    for (int x = 1; x <= 100; x++)
                        if (Map.InBounds(x, y) && Map.Tiles[x, y].NpcIndex > 0)
                        {
                            int ni = Map.Tiles[x, y].NpcIndex;
                            int bi = ni < NpcBodies.Length ? NpcBodies[ni] : -1;
                            int bg = (bi > 0 && NpcBodyGrhs != null && bi < NpcBodyGrhs.Length) ? NpcBodyGrhs[bi] : -1;
                            GD.Print($"[WalkMode] DIAG: First NPC at ({x},{y}) npcIdx={ni} bodyIdx={bi} bodyGrh={bg}");
                            goto doneDiag;
                        }
                doneDiag:;
            }
            if (exitCount > 0)
            {
                for (int y = 1; y <= 100; y++)
                    for (int x = 1; x <= 100; x++)
                        if (Map.InBounds(x, y) && Map.Tiles[x, y].ExitMap > 0)
                        {
                            GD.Print($"[WalkMode] DIAG: First exit at ({x},{y}) → Map{Map.Tiles[x,y].ExitMap} ({Map.Tiles[x,y].ExitX},{Map.Tiles[x,y].ExitY})");
                            goto doneExitDiag;
                        }
                doneExitDiag:;
            }
        }

        DrawRect(new Rect2(Vector2.Zero, Size), Colors.Black);

        float ofsX = (float)Math.Round(_moveOffsetX);
        float ofsY = (float)Math.Round(_moveOffsetY);

        // L1 uses smaller buffer; L2/L3/L4 need large buffer for multi-tile GRHs
        int minDY_L1 = -HalfTilesY - ExtraTiles;
        int maxDY_L1 = HalfTilesY + ExtraTiles;
        int minDX_L1 = -HalfTilesX - ExtraTiles;
        int maxDX_L1 = HalfTilesX + ExtraTiles;

        int minDY = -HalfTilesY - ExtraTilesLarge;
        int maxDY = HalfTilesY + ExtraTilesLarge;
        int minDX = -HalfTilesX - ExtraTilesLarge;
        int maxDX = HalfTilesX + ExtraTilesLarge;

        // ── Pass 1: Ground (L1) ── top-left aligned
        for (int dy = minDY_L1; dy <= maxDY_L1; dy++)
            for (int dx = minDX_L1; dx <= maxDX_L1; dx++)
            {
                int tx = CharX + dx, ty = CharY + dy;
                if (!Map.InBounds(tx, ty)) continue;
                float sx = (dx + HalfTilesX) * TileSize + ofsX;
                float sy = (dy + HalfTilesY) * TileSize + ofsY;
                DrawGrh(Map.Tiles[tx, ty].Layer1, sx, sy, Colors.White);
            }

        // ── Pass 2: Mask/Alpha (L2) ── centered, large buffer
        for (int dy = minDY; dy <= maxDY; dy++)
            for (int dx = minDX; dx <= maxDX; dx++)
            {
                int tx = CharX + dx, ty = CharY + dy;
                if (!Map.InBounds(tx, ty)) continue;
                short l2 = Map.Tiles[tx, ty].Layer2;
                if (l2 <= 0) continue;
                float sx = (dx + HalfTilesX) * TileSize + ofsX;
                float sy = (dy + HalfTilesY) * TileSize + ofsY;
                DrawGrhCentered(l2, sx, sy, Colors.White);
            }

        // ── Pass 3: Objects + NPCs + L3 + Character — Y-sorted ──
        for (int dy = minDY; dy <= maxDY; dy++)
        {
            int ty = CharY + dy;

            // Draw character at its Y row
            if (dy == 0)
                DrawCharacter(ofsX, ofsY);

            for (int dx = minDX; dx <= maxDX; dx++)
            {
                int tx = CharX + dx;
                if (!Map.InBounds(tx, ty)) continue;

                float sx = (dx + HalfTilesX) * TileSize + ofsX;
                float sy = (dy + HalfTilesY) * TileSize + ofsY;

                // Ground objects from .inf data (includes doors)
                DrawTileObject(tx, ty, sx, sy);

                // NPCs from .inf data
                DrawTileNpc(tx, ty, sx, sy);

                // L3 graphic layer
                short l3 = Map.Tiles[tx, ty].Layer3;
                if (l3 > 0)
                {
                    bool onCharTile = (tx == CharX && ty == CharY);
                    Color mod = onCharTile ? new Color(1, 1, 1, 0.5f) : Colors.White;
                    DrawGrhCentered(l3, sx, sy, mod);
                }
            }
        }

        // ── Pass 4: Roof (L4) — centered, large buffer, transparency when under roof ──
        for (int dy = minDY; dy <= maxDY; dy++)
            for (int dx = minDX; dx <= maxDX; dx++)
            {
                int tx = CharX + dx, ty = CharY + dy;
                if (!Map.InBounds(tx, ty)) continue;
                short l4 = Map.Tiles[tx, ty].Layer4;
                if (l4 <= 0) continue;
                float sx = (dx + HalfTilesX) * TileSize + ofsX;
                float sy = (dy + HalfTilesY) * TileSize + ofsY;

                Color mod = _underRoof ? new Color(1, 1, 1, 0.35f) : Colors.White;
                DrawGrhCentered(l4, sx, sy, mod);
            }

        // ── Exit markers ──
        DrawExitMarkers(minDX_L1, maxDX_L1, minDY_L1, maxDY_L1, ofsX, ofsY);

        // ── HUD ──
        var font = ThemeDB.Singleton.FallbackFont;
        string mapName = Map.Name.Length > 0 ? Map.Name : $"Mapa {Map.MapNumber}";
        string info = $"{mapName}  ({Map.MapNumber},{CharX},{CharY})  [{HeadingName(_heading)}]";
        DrawString(font, new Vector2(6, Size.Y - 6), info,
            HorizontalAlignment.Left, -1, 12, new Color(1, 1, 1, 0.7f));
        DrawString(font, new Vector2(6, 16),
            "WASD/Flechas: caminar  |  Shift+Click: teleport  |  Esc: cerrar",
            HorizontalAlignment.Left, -1, 11, new Color(1, 1, 0.8f, 0.5f));
    }

    // ── NPC / Object / Exit rendering ───────────────────────────────────────

    private void DrawTileObject(int tx, int ty, float sx, float sy)
    {
        if (ObjGrhs == null) return;
        int objIdx = Map!.Tiles[tx, ty].ObjIndex;
        if (objIdx <= 0 || objIdx >= ObjGrhs.Length) return;
        int objGrh = ObjGrhs[objIdx];
        if (objGrh <= 0) return;
        DrawGrhCentered(objGrh, sx, sy, Colors.White);
    }

    private void DrawTileNpc(int tx, int ty, float sx, float sy)
    {
        if (NpcBodies == null || NpcBodyGrhs == null || Grhs == null) return;
        int npcIdx = Map!.Tiles[tx, ty].NpcIndex;
        if (npcIdx <= 0 || npcIdx >= NpcBodies.Length) return;

        int bodyIdx = NpcBodies[npcIdx];
        if (bodyIdx <= 0 || bodyIdx >= NpcBodyGrhs.Length) return;

        // Draw NPC body (south-facing static frame)
        int bodyGrh = NpcBodyGrhs[bodyIdx];
        if (bodyGrh > 0)
            DrawAnimGrhCentered(bodyGrh, 0, sx, sy, Colors.White);

        // Draw NPC head
        if (NpcHeads == null || HeadGrhs == null) return;
        if (npcIdx >= NpcHeads.Length) return;
        int headIdx = NpcHeads[npcIdx];
        if (headIdx <= 0 || headIdx >= HeadGrhs.Length) return;
        int headGrh = HeadGrhs[headIdx];
        if (headGrh <= 0) return;

        int hofX = (NpcHeadOfsX != null && bodyIdx < NpcHeadOfsX.Length) ? NpcHeadOfsX[bodyIdx] : 0;
        int hofY = (NpcHeadOfsY != null && bodyIdx < NpcHeadOfsY.Length) ? NpcHeadOfsY[bodyIdx] : 0;
        DrawAnimGrhCentered(headGrh, 0, sx + hofX, sy + hofY, Colors.White);
    }

    private void DrawExitMarkers(int minDX, int maxDX, int minDY, int maxDY, float ofsX, float ofsY)
    {
        if (Map == null) return;
        for (int dy = minDY; dy <= maxDY; dy++)
            for (int dx = minDX; dx <= maxDX; dx++)
            {
                int tx = CharX + dx, ty = CharY + dy;
                if (!Map.InBounds(tx, ty)) continue;
                if (!Map.Tiles[tx, ty].HasExit) continue;

                float sx = (dx + HalfTilesX) * TileSize + ofsX;
                float sy = (dy + HalfTilesY) * TileSize + ofsY;

                DrawRect(new Rect2(sx + 2, sy + 2, TileSize - 4, TileSize - 4),
                    new Color(0.2f, 1f, 0.2f, 0.25f));
                DrawRect(new Rect2(sx + 2, sy + 2, TileSize - 4, TileSize - 4),
                    new Color(0.2f, 1f, 0.2f, 0.5f), false, 1f);
            }
    }

    private void DrawCharacter(float ofsX, float ofsY)
    {
        if (Bodies == null || Heads == null) return;
        if (BodyIndex <= 0 || BodyIndex >= Bodies.Length) return;

        var body = Bodies[BodyIndex];
        int bodyGrh = body.Walk[_heading];
        if (bodyGrh <= 0) return;

        // Character is ALWAYS at viewport center
        float cx = HalfTilesX * TileSize;
        float cy = HalfTilesY * TileSize;

        // Resolve walk animation frame
        int frameCount = GetGrhFrameCount(bodyGrh);
        int bodyFrame = _isMoving ? ((int)_walkFrame % Math.Max(frameCount, 1)) : 0;
        DrawAnimGrhCentered(bodyGrh, bodyFrame, cx, cy, Colors.White);

        // Head
        if (HeadIndex > 0 && HeadIndex < Heads.Length)
        {
            int headGrh = Heads[HeadIndex].Head[_heading];
            if (headGrh > 0)
            {
                float hx = cx + body.HeadOffsetX;
                float hy = cy + body.HeadOffsetY;
                DrawAnimGrhCentered(headGrh, 0, hx, hy, Colors.White);
            }
        }
    }

    // ── GRH drawing helpers ─────────────────────────────────────────────────

    private void DrawGrh(int grhIndex, float x, float y, Color mod)
    {
        if (grhIndex <= 0 || Grhs == null || Textures == null) return;
        if (grhIndex >= Grhs.Length) return;

        var grh = ResolveStaticFrame(grhIndex);
        if (grh.FileNum <= 0 || grh.PixelWidth <= 0) return;

        var texture = Textures.GetTexture(grh.FileNum);
        if (texture == null) return;

        var src = new Rect2(grh.SX, grh.SY, grh.PixelWidth, grh.PixelHeight);
        var dst = new Rect2(x, y, grh.PixelWidth, grh.PixelHeight);
        DrawTextureRectRegion(texture, dst, src, mod);
    }

    private void DrawGrhCentered(int grhIndex, float tileX, float tileY, Color mod)
    {
        if (grhIndex <= 0 || Grhs == null || Textures == null) return;
        if (grhIndex >= Grhs.Length) return;

        var baseGrh = Grhs[grhIndex];
        int frameIdx = 0;
        if (baseGrh.NumFrames > 1 && baseGrh.Speed > 0)
            frameIdx = (int)(_globalTime * baseGrh.NumFrames / baseGrh.Speed) % baseGrh.NumFrames;

        var grh = ResolveFrame(grhIndex, frameIdx);
        if (grh.FileNum <= 0 || grh.PixelWidth <= 0) return;

        var texture = Textures.GetTexture(grh.FileNum);
        if (texture == null) return;

        // AO centering: multi-tile sprites offset left and up
        float drawX = tileX;
        float drawY = tileY;

        if (grh.TileWidth != 1f && grh.TileWidth > 0)
            drawX -= (int)(grh.TileWidth * HalfTileSize) - HalfTileSize;
        if (grh.TileHeight != 1f && grh.TileHeight > 0)
            drawY -= (int)(grh.TileHeight * TileSize) - TileSize;

        var src = new Rect2(grh.SX, grh.SY, grh.PixelWidth, grh.PixelHeight);
        var dst = new Rect2((float)Math.Round(drawX), (float)Math.Round(drawY), grh.PixelWidth, grh.PixelHeight);
        DrawTextureRectRegion(texture, dst, src, mod);
    }

    private void DrawAnimGrhCentered(int grhIndex, int frame, float tileX, float tileY, Color mod)
    {
        if (grhIndex <= 0 || Grhs == null || Textures == null) return;
        if (grhIndex >= Grhs.Length) return;

        var grh = ResolveFrame(grhIndex, frame);
        if (grh.FileNum <= 0 || grh.PixelWidth <= 0) return;

        var texture = Textures.GetTexture(grh.FileNum);
        if (texture == null) return;

        float drawX = tileX;
        float drawY = tileY;

        if (grh.TileWidth != 1f && grh.TileWidth > 0)
            drawX -= (int)(grh.TileWidth * HalfTileSize) - HalfTileSize;
        if (grh.TileHeight != 1f && grh.TileHeight > 0)
            drawY -= (int)(grh.TileHeight * TileSize) - TileSize;

        var src = new Rect2(grh.SX, grh.SY, grh.PixelWidth, grh.PixelHeight);
        var dst = new Rect2((float)Math.Round(drawX), (float)Math.Round(drawY), grh.PixelWidth, grh.PixelHeight);
        DrawTextureRectRegion(texture, dst, src, mod);
    }

    private GrhData ResolveStaticFrame(int grhIndex)
    {
        var grh = Grhs![grhIndex];
        if (grh.NumFrames > 1 && grh.Frames is { Length: > 0 })
        {
            int resolved = grh.Frames[0];
            if (resolved > 0 && resolved < Grhs.Length)
                return Grhs[resolved];
        }
        return grh;
    }

    private GrhData ResolveFrame(int grhIndex, int frame)
    {
        var grh = Grhs![grhIndex];
        if (grh.NumFrames > 1 && grh.Frames is { Length: > 0 })
        {
            int fi = frame % grh.Frames.Length;
            int resolved = grh.Frames[fi];
            if (resolved > 0 && resolved < Grhs.Length)
                return Grhs[resolved];
        }
        return grh;
    }

    private int GetGrhFrameCount(int grhIndex)
    {
        if (grhIndex <= 0 || grhIndex >= Grhs!.Length) return 1;
        var grh = Grhs[grhIndex];
        return grh.NumFrames > 1 ? grh.NumFrames : 1;
    }

    private static string HeadingName(int h) => h switch
    {
        1 => "Norte", 2 => "Este", 3 => "Sur", 4 => "Oeste", _ => "?"
    };
}
