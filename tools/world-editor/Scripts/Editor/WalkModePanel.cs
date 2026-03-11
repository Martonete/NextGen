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
    private const float MoveSpeed = 4.0f; // pixels per frame at 60fps (~8px per 40ms tick)

    // ── Character state ─────────────────────────────────────────────────────
    public int CharX = 50, CharY = 50; // current tile position (1-indexed)
    public int BodyIndex = 1;
    public int HeadIndex = 1;

    private int _heading = 3; // 1=N 2=E 3=S 4=W
    private bool _isMoving;
    private float _moveOffsetX, _moveOffsetY; // pixel offset during movement
    private float _walkFrame;
    private double _globalTime; // for tile animations

    // ── Input state ─────────────────────────────────────────────────────────
    private bool _keyUp, _keyDown, _keyLeft, _keyRight;

    public override void _Ready()
    {
        CustomMinimumSize = new Vector2(ViewWidth, ViewHeight);
        FocusMode = FocusModeEnum.All;
        GrabFocus();
    }

    public override void _Process(double delta)
    {
        _globalTime += delta * 1000.0; // ms

        if (_isMoving)
        {
            // Converge offset toward zero
            float speed = MoveSpeed * (float)(delta * 60.0); // normalize to ~60fps
            if (Math.Abs(_moveOffsetX) > 0.1f)
                _moveOffsetX -= Math.Sign(_moveOffsetX) * Math.Min(speed, Math.Abs(_moveOffsetX));
            else
                _moveOffsetX = 0;

            if (Math.Abs(_moveOffsetY) > 0.1f)
                _moveOffsetY -= Math.Sign(_moveOffsetY) * Math.Min(speed, Math.Abs(_moveOffsetY));
            else
                _moveOffsetY = 0;

            // Advance walk animation
            _walkFrame += (float)(delta * 8.0); // ~8 frames per second

            if (_moveOffsetX == 0 && _moveOffsetY == 0)
            {
                _isMoving = false;
                _walkFrame = 0;

                // Continue walking if key still held
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
        _moveOffsetX = -(dx * TileSize);
        _moveOffsetY = -(dy * TileSize);
        _walkFrame = 0;
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
            // Shift+click: teleport
            if (mb.ShiftPressed && Map != null)
            {
                int tx = (int)((mb.Position.X - _moveOffsetX) / TileSize) - HalfTilesX + CharX;
                int ty = (int)((mb.Position.Y - _moveOffsetY) / TileSize) - HalfTilesY + CharY;
                if (Map.InBounds(tx, ty))
                {
                    CharX = tx;
                    CharY = ty;
                    _isMoving = false;
                    _moveOffsetX = 0;
                    _moveOffsetY = 0;
                    _walkFrame = 0;
                }
            }
            AcceptEvent();
        }
    }

    // ── Rendering ───────────────────────────────────────────────────────────

    public override void _Draw()
    {
        if (Map == null || Grhs == null || Textures == null) return;

        // Background
        DrawRect(new Rect2(Vector2.Zero, Size), Colors.Black);

        float ofsX = _moveOffsetX;
        float ofsY = _moveOffsetY;

        // Determine if character is under a roof
        bool underRoof = Map.InBounds(CharX, CharY) && Map.Tiles[CharX, CharY].Layer4 > 0;

        // ── Pass 1: Ground (L1) ──
        for (int dy = -HalfTilesY - 1; dy <= HalfTilesY + 1; dy++)
        {
            for (int dx = -HalfTilesX - 1; dx <= HalfTilesX + 1; dx++)
            {
                int tx = CharX + dx;
                int ty = CharY + dy;
                if (!Map.InBounds(tx, ty)) continue;
                float sx = (dx + HalfTilesX) * TileSize + ofsX;
                float sy = (dy + HalfTilesY) * TileSize + ofsY;
                DrawGrh(Map.Tiles[tx, ty].Layer1, sx, sy);
            }
        }

        // ── Pass 2: Mask/Alpha (L2) ──
        for (int dy = -HalfTilesY - 1; dy <= HalfTilesY + 1; dy++)
        {
            for (int dx = -HalfTilesX - 1; dx <= HalfTilesX + 1; dx++)
            {
                int tx = CharX + dx;
                int ty = CharY + dy;
                if (!Map.InBounds(tx, ty)) continue;
                ref var tile = ref Map.Tiles[tx, ty];
                if (tile.Layer2 <= 0) continue;
                float sx = (dx + HalfTilesX) * TileSize + ofsX;
                float sy = (dy + HalfTilesY) * TileSize + ofsY;
                DrawGrh(tile.Layer2, sx, sy);
            }
        }

        // ── Pass 3: Objects (L3) + Character, Y-sorted ──
        for (int dy = -HalfTilesY - 1; dy <= HalfTilesY + 1; dy++)
        {
            int ty = CharY + dy;

            // Draw character at its row
            if (dy == 0)
            {
                DrawCharacter();
            }

            for (int dx = -HalfTilesX - 1; dx <= HalfTilesX + 1; dx++)
            {
                int tx = CharX + dx;
                if (!Map.InBounds(tx, ty)) continue;
                ref var tile = ref Map.Tiles[tx, ty];
                if (tile.Layer3 <= 0) continue;
                float sx = (dx + HalfTilesX) * TileSize + ofsX;
                float sy = (dy + HalfTilesY) * TileSize + ofsY;

                // Semi-transparent if this L3 sprite is on or below the character
                // (character walks "behind" objects on same or lower Y)
                Color mod = (ty >= CharY && tx >= CharX - 1 && tx <= CharX + 1)
                    ? new Color(1, 1, 1, 0.45f)
                    : Colors.White;
                DrawGrhCentered(tile.Layer3, sx, sy, mod);
            }
        }

        // ── Pass 4: Roof (L4) ──
        for (int dy = -HalfTilesY - 1; dy <= HalfTilesY + 1; dy++)
        {
            for (int dx = -HalfTilesX - 1; dx <= HalfTilesX + 1; dx++)
            {
                int tx = CharX + dx;
                int ty = CharY + dy;
                if (!Map.InBounds(tx, ty)) continue;
                ref var tile = ref Map.Tiles[tx, ty];
                if (tile.Layer4 <= 0) continue;
                float sx = (dx + HalfTilesX) * TileSize + ofsX;
                float sy = (dy + HalfTilesY) * TileSize + ofsY;

                Color mod = underRoof ? new Color(1, 1, 1, 0.35f) : Colors.White;
                DrawGrh(tile.Layer4, sx, sy, mod);
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

    private void DrawCharacter()
    {
        if (Bodies == null || Heads == null) return;
        if (BodyIndex <= 0 || BodyIndex >= Bodies.Length) return;

        var body = Bodies[BodyIndex];
        int bodyGrh = body.Walk[_heading];
        if (bodyGrh <= 0) return;

        // Screen center for character
        float cx = HalfTilesX * TileSize + _moveOffsetX;
        float cy = HalfTilesY * TileSize + _moveOffsetY;

        // Resolve walk animation frame
        int bodyFrame = _isMoving ? ((int)_walkFrame % GetGrhFrameCount(bodyGrh)) : 0;
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

    private void DrawGrh(int grhIndex, float x, float y, Color? modulate = null)
    {
        if (grhIndex <= 0 || Grhs == null || Textures == null) return;
        if (grhIndex >= Grhs.Length) return;

        var grh = ResolveFrame(grhIndex, 0);
        if (grh.FileNum <= 0 || grh.PixelWidth <= 0) return;

        var texture = Textures.GetTexture(grh.FileNum);
        if (texture == null) return;

        var src = new Rect2(grh.SX, grh.SY, grh.PixelWidth, grh.PixelHeight);
        var dst = new Rect2(x, y, grh.PixelWidth, grh.PixelHeight);
        DrawTextureRectRegion(texture, dst, src, modulate ?? Colors.White);
    }

    private void DrawGrhCentered(int grhIndex, float tileX, float tileY, Color mod)
    {
        if (grhIndex <= 0 || Grhs == null || Textures == null) return;
        if (grhIndex >= Grhs.Length) return;

        // Resolve animated tile GRH using global time
        int frameIdx = 0;
        var baseGrh = Grhs[grhIndex];
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

    private GrhData ResolveFrame(int grhIndex, int frame)
    {
        var grh = Grhs![grhIndex];
        if (grh.NumFrames > 1 && grh.Frames != null && grh.Frames.Length > 0)
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
