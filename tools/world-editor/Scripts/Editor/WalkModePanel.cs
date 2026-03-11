#nullable enable
using System;
using Godot;
using AOWorldEditor.Data;

namespace AOWorldEditor.Editor;

/// <summary>
/// Walk mode: simulates the in-game AO view. Renders 17x13 visible tiles,
/// a centered character with walk animation, roof/tree transparency, and
/// blocked tile collision.
/// </summary>
public partial class WalkModePanel : Control
{
    // ── Dependencies ────────────────────────────────────────────────────────
    public MapData? Map;
    public GrhData[]? Grhs;
    public TextureManager? Textures;
    public BodyAnimData[]? Bodies;
    public HeadAnimData[]? Heads;

    // ── Constants (matching AO client) ──────────────────────────────────────
    private const int TileSize = 32;
    private const int HalfTilesX = 8;
    private const int HalfTilesY = 6;
    private const int ViewTilesX = HalfTilesX * 2 + 1; // 17
    private const int ViewTilesY = HalfTilesY * 2 + 1; // 13
    private const int ViewWidth = ViewTilesX * TileSize;  // 544
    private const int ViewHeight = ViewTilesY * TileSize; // 416
    private const int ExtraTiles = 2; // extra tiles beyond viewport for scroll coverage

    // Movement: AO uses ScrollPixels=8 per 40ms tick → 200 pixels/sec
    private const float PixelsPerSecond = 200f;

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

    public override void _Ready()
    {
        CustomMinimumSize = new Vector2(ViewWidth, ViewHeight);
        FocusMode = FocusModeEnum.All;
        GrabFocus();
    }

    public override void _Process(double delta)
    {
        _globalTime += delta * 1000.0;

        if (_isMoving)
        {
            // Fixed pixel advance per second (AO: 8px per 40ms = 200px/s)
            float advance = PixelsPerSecond * (float)delta;

            if (_moveOffsetX != 0)
            {
                float sign = Math.Sign(_moveOffsetX);
                _moveOffsetX -= sign * advance;
                // Overshoot check: if sign changed, snap to 0
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

            // Advance walk animation (~6 fps for walk cycle)
            _walkFrame += (float)(delta * 6.0);

            if (_moveOffsetX == 0 && _moveOffsetY == 0)
            {
                _isMoving = false;
                _walkFrame = 0;
                TryMoveFromInput(); // chain movement if key held
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
        // Offset compensates the instant tile jump: CharX changed by dx,
        // so tiles shifted by -dx*32 on screen. Start at +dx*32 to cancel,
        // then animate toward 0 for smooth scroll.
        _moveOffsetX = dx * TileSize;
        _moveOffsetY = dy * TileSize;
        _walkFrame = 0;

        // Update roof detection
        _underRoof = IsUnderRoof(CharX, CharY);
    }

    private bool IsUnderRoof(int cx, int cy)
    {
        // Check character tile and immediate neighbors for L4 roof
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
                // Shift+click: teleport to clicked tile
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
                }
            }
            AcceptEvent();
        }
    }

    // ── Rendering ───────────────────────────────────────────────────────────

    public override void _Draw()
    {
        if (Map == null || Grhs == null || Textures == null) return;

        DrawRect(new Rect2(Vector2.Zero, Size), Colors.Black);

        // Pixel offsets for smooth scrolling — applied to ALL world tiles
        float ofsX = (float)Math.Round(_moveOffsetX);
        float ofsY = (float)Math.Round(_moveOffsetY);

        int minDY = -HalfTilesY - ExtraTiles;
        int maxDY = HalfTilesY + ExtraTiles;
        int minDX = -HalfTilesX - ExtraTiles;
        int maxDX = HalfTilesX + ExtraTiles;

        // ── Pass 1: Ground (L1) ──
        for (int dy = minDY; dy <= maxDY; dy++)
            for (int dx = minDX; dx <= maxDX; dx++)
            {
                int tx = CharX + dx, ty = CharY + dy;
                if (!Map.InBounds(tx, ty)) continue;
                float sx = (dx + HalfTilesX) * TileSize + ofsX;
                float sy = (dy + HalfTilesY) * TileSize + ofsY;
                DrawGrh(Map.Tiles[tx, ty].Layer1, sx, sy, Colors.White);
            }

        // ── Pass 2: Mask/Alpha (L2) ──
        for (int dy = minDY; dy <= maxDY; dy++)
            for (int dx = minDX; dx <= maxDX; dx++)
            {
                int tx = CharX + dx, ty = CharY + dy;
                if (!Map.InBounds(tx, ty)) continue;
                short l2 = Map.Tiles[tx, ty].Layer2;
                if (l2 <= 0) continue;
                float sx = (dx + HalfTilesX) * TileSize + ofsX;
                float sy = (dy + HalfTilesY) * TileSize + ofsY;
                DrawGrh(l2, sx, sy, Colors.White);
            }

        // ── Pass 3: Objects (L3) + Character — Y-sorted ──
        // Draw row by row. Character is drawn AT its row, BEFORE L3 objects
        // on the same row (so objects on same Y or below render on top = in front).
        for (int dy = minDY; dy <= maxDY; dy++)
        {
            int ty = CharY + dy;

            // Draw character at its Y row (before L3 on same row)
            if (dy == 0)
                DrawCharacter(ofsX, ofsY);

            for (int dx = minDX; dx <= maxDX; dx++)
            {
                int tx = CharX + dx;
                if (!Map.InBounds(tx, ty)) continue;
                short l3 = Map.Tiles[tx, ty].Layer3;
                if (l3 <= 0) continue;
                float sx = (dx + HalfTilesX) * TileSize + ofsX;
                float sy = (dy + HalfTilesY) * TileSize + ofsY;

                // L3 objects on the character's tile become semi-transparent
                // so the character is visible underneath them
                bool onCharTile = (tx == CharX && ty == CharY);
                Color mod = onCharTile ? new Color(1, 1, 1, 0.5f) : Colors.White;
                DrawGrhCentered(l3, sx, sy, mod);
            }
        }

        // ── Pass 4: Roof (L4) — only when tile actually has roof ──
        if (HasAnyRoofInView(minDX, maxDX, minDY, maxDY))
        {
            for (int dy = minDY; dy <= maxDY; dy++)
                for (int dx = minDX; dx <= maxDX; dx++)
                {
                    int tx = CharX + dx, ty = CharY + dy;
                    if (!Map.InBounds(tx, ty)) continue;
                    short l4 = Map.Tiles[tx, ty].Layer4;
                    if (l4 <= 0) continue;
                    float sx = (dx + HalfTilesX) * TileSize + ofsX;
                    float sy = (dy + HalfTilesY) * TileSize + ofsY;

                    // Transparent when character is under a roof
                    Color mod = _underRoof ? new Color(1, 1, 1, 0.35f) : Colors.White;
                    DrawGrh(l4, sx, sy, mod);
                }
        }

        // ── HUD ──
        var font = ThemeDB.Singleton.FallbackFont;
        string info = $"Mapa {Map.MapNumber}  ({CharX},{CharY})  [{HeadingName(_heading)}]";
        DrawString(font, new Vector2(6, Size.Y - 6), info,
            HorizontalAlignment.Left, -1, 12, new Color(1, 1, 1, 0.7f));
        DrawString(font, new Vector2(6, 16),
            "WASD/Flechas: caminar  |  Shift+Click: teleport  |  Esc: cerrar",
            HorizontalAlignment.Left, -1, 11, new Color(1, 1, 0.8f, 0.5f));
    }

    private bool HasAnyRoofInView(int minDX, int maxDX, int minDY, int maxDY)
    {
        for (int dy = minDY; dy <= maxDY; dy++)
            for (int dx = minDX; dx <= maxDX; dx++)
            {
                int tx = CharX + dx, ty = CharY + dy;
                if (Map!.InBounds(tx, ty) && Map.Tiles[tx, ty].Layer4 > 0)
                    return true;
            }
        return false;
    }

    private void DrawCharacter(float ofsX, float ofsY)
    {
        if (Bodies == null || Heads == null) return;
        if (BodyIndex <= 0 || BodyIndex >= Bodies.Length) return;

        var body = Bodies[BodyIndex];
        int bodyGrh = body.Walk[_heading];
        if (bodyGrh <= 0) return;

        // Character is ALWAYS at viewport center — world scrolls, character stays fixed
        float cx = HalfTilesX * TileSize;
        float cy = HalfTilesY * TileSize;

        // Resolve walk animation frame
        int frameCount = GetGrhFrameCount(bodyGrh);
        int bodyFrame = _isMoving ? ((int)_walkFrame % frameCount) : 0;
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

        // Tile animations use global time for seamless looping
        var baseGrh = Grhs[grhIndex];
        int frameIdx = 0;
        if (baseGrh.NumFrames > 1 && baseGrh.Speed > 0)
            frameIdx = (int)(_globalTime * baseGrh.NumFrames / baseGrh.Speed) % baseGrh.NumFrames;

        var grh = ResolveFrame(grhIndex, frameIdx);
        if (grh.FileNum <= 0 || grh.PixelWidth <= 0) return;

        var texture = Textures.GetTexture(grh.FileNum);
        if (texture == null) return;

        float drawX = tileX + (TileSize - grh.PixelWidth) / 2f;
        float drawY = tileY + (TileSize - grh.PixelHeight);
        var src = new Rect2(grh.SX, grh.SY, grh.PixelWidth, grh.PixelHeight);
        var dst = new Rect2(drawX, drawY, grh.PixelWidth, grh.PixelHeight);
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

        float drawX = tileX + (TileSize - grh.PixelWidth) / 2f;
        float drawY = tileY + (TileSize - grh.PixelHeight);
        var src = new Rect2(grh.SX, grh.SY, grh.PixelWidth, grh.PixelHeight);
        var dst = new Rect2(drawX, drawY, grh.PixelWidth, grh.PixelHeight);
        DrawTextureRectRegion(texture, dst, src, mod);
    }

    /// <summary>Resolve animated GRH to its first frame (for static tiles like L1/L2/L4).</summary>
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

    /// <summary>Resolve animated GRH to a specific frame index.</summary>
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
