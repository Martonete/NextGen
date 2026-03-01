using System;
using System.Collections.Generic;
using Godot;
using TierrasSagradasAO.Data;
using TierrasSagradasAO.Game;

namespace TierrasSagradasAO.Rendering;

/// <summary>
/// Renders the game world matching VB6 RenderScreen:
/// - 534x408 viewport centered on player
/// - 4 tile layers with correct draw order
/// - TileBufferSize=9 for multi-tile graphics
/// - Tree alpha near player, gradual roof fade
/// - Character position index for O(1) lookup
/// </summary>
public partial class WorldRenderer : Node2D
{
    private GameState? _state;
    private GameData? _data;
    private GrhAnimator? _animator;

    private const int TileSize = 32;

    // VB6 viewport: 534x408 px
    private const int ViewportWidth = 534;
    private const int ViewportHeight = 408;

    // VB6 uses ScreenX/ScreenY = (tileX - minX), with minX = userX - HalfWindowTileWidth
    // Layer 1: draw at (ScreenX - 1) * 32, Layers 2+: draw at ScreenX * 32

    // How many tiles from center to edge (visible range)
    private const int HalfWindowTileWidth = 8;
    private const int HalfWindowTileHeight = 6;

    // VB6: TileBufferSize = 9
    private const int TileBufferSize = 9;

    // VB6: bTechoAB — roof alpha
    private float _roofAlpha = 255f;
    private const float RoofFadeRate = 6f;

    // Per-frame character position index
    private readonly Dictionary<(int, int), List<int>> _charPosIndex = new();
    private readonly List<int> _emptyCharList = new();

    public void Init(GameState state, GameData data, GrhAnimator animator)
    {
        _state = state;
        _data = data;
        _animator = animator;
    }

    public override void _Process(double delta)
    {
        if (_state?.MapData == null) return;
        UpdateRoofFade();
        QueueRedraw();
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
    /// Convert world tile to screen pixel position — VB6 Layer 1 formula.
    /// VB6: ScreenX = tileX - minX, draw at (ScreenX - 1) * 32 + PixelOffsetX
    /// Where minX = userX - HalfWindowTileWidth
    /// </summary>
    private static Vector2 TileToScreenL1(int tileX, int tileY, int userX, int userY,
                                           float pixelOffsetX, float pixelOffsetY)
    {
        float px = (tileX - userX + HalfWindowTileWidth - 1) * TileSize + pixelOffsetX;
        float py = (tileY - userY + HalfWindowTileHeight - 1) * TileSize + pixelOffsetY;
        return new Vector2(px, py);
    }

    /// <summary>
    /// Convert world tile to screen pixel position — VB6 Layers 2-4 formula.
    /// VB6: ScreenX = tileX - minX, draw at ScreenX * 32 + PixelOffsetX
    /// </summary>
    private static Vector2 TileToScreen(int tileX, int tileY, int userX, int userY,
                                         float pixelOffsetX, float pixelOffsetY)
    {
        float px = (tileX - userX + HalfWindowTileWidth) * TileSize + pixelOffsetX;
        float py = (tileY - userY + HalfWindowTileHeight) * TileSize + pixelOffsetY;
        return new Vector2(px, py);
    }

    public override void _Draw()
    {
        if (_state == null || _data == null || _animator == null) return;
        if (_state.MapData == null || _state.Paused) return;

        // VB6 ShowNextFrame: render center = UserPos - AddtoUserPos, offset = OffsetCounter
        // During scroll, camera center stays at the old tile while offset accumulates
        int userX = _state.UserPosX - _state.AddToUserPosX;
        int userY = _state.UserPosY - _state.AddToUserPosY;

        BuildCharPositionIndex();

        // Camera pixel offset — NEGATED because ScreenOffset grows in the movement
        // direction, but tiles must shift in the OPPOSITE direction on screen.
        // This also makes self char's MoveOffset and camera offset cancel out perfectly,
        // keeping the player centered while the world scrolls smoothly.
        float pixelOffsetX = -_state.ScreenOffsetX;
        float pixelOffsetY = -_state.ScreenOffsetY;

        // Visible tile range
        int screenMinX = userX - HalfWindowTileWidth;
        int screenMaxX = userX + HalfWindowTileWidth;
        int screenMinY = userY - HalfWindowTileHeight;
        int screenMaxY = userY + HalfWindowTileHeight;

        // Extended bounds with tile buffer
        int minX = Math.Max(1, screenMinX - TileBufferSize);
        int maxX = Math.Min(100, screenMaxX + TileBufferSize);
        int minY = Math.Max(1, screenMinY - TileBufferSize);
        int maxY = Math.Min(100, screenMaxY + TileBufferSize);

        // ==========================================
        // PASS 1: Layer 1 (Ground) — visible area +1 tile margin
        // ==========================================
        int l1MinX = Math.Max(1, screenMinX - 1);
        int l1MaxX = Math.Min(100, screenMaxX + 1);
        int l1MinY = Math.Max(1, screenMinY - 1);
        int l1MaxY = Math.Min(100, screenMaxY + 1);

        for (int y = l1MinY; y <= l1MaxY; y++)
        {
            for (int x = l1MinX; x <= l1MaxX; x++)
            {
                ref var tile = ref _state.MapData.Tiles[x, y];
                if (tile.Layer1 <= 0) continue;

                Vector2 pos = TileToScreenL1(x, y, userX, userY, pixelOffsetX, pixelOffsetY);
                DrawTileGrh(tile.Layer1, pos);
            }
        }

        // ==========================================
        // PASS 2: Layer 2 — extended buffer range
        // ==========================================
        for (int y = minY; y <= maxY; y++)
        {
            for (int x = minX; x <= maxX; x++)
            {
                ref var tile = ref _state.MapData.Tiles[x, y];
                if (tile.Layer2 <= 0) continue;

                Vector2 pos = TileToScreen(x, y, userX, userY, pixelOffsetX, pixelOffsetY);
                DrawTileGrh(tile.Layer2, pos, center: true);
            }
        }

        // ==========================================
        // PASS 3: Objects + Characters + Layer 3
        // ==========================================
        for (int y = minY; y <= maxY; y++)
        {
            for (int x = minX; x <= maxX; x++)
            {
                Vector2 tilePos = TileToScreen(x, y, userX, userY, pixelOffsetX, pixelOffsetY);
                ref var tile = ref _state.MapData.Tiles[x, y];

                // Ground objects
                if (_state.GroundObjects.TryGetValue((x, y), out int objGrh) && objGrh > 0)
                {
                    DrawTileGrh(objGrh, tilePos, center: true);
                }

                // Characters/NPCs at this tile
                var charsHere = GetCharsAt(x, y);
                for (int ci = 0; ci < charsHere.Count; ci++)
                {
                    if (!_state.Characters.TryGetValue(charsHere[ci], out var ch)) continue;

                    float charPx = tilePos.X + ch.MoveOffsetX;
                    float charPy = tilePos.Y + ch.MoveOffsetY;

                    CharRenderer.DrawCharacter(this, ch, new Vector2(charPx, charPy), _data, _animator);
                }

                // Layer 3 (trees/objects)
                if (tile.Layer3 > 0)
                {
                    bool nearPlayer = y > (userY - 2) && y < (userY + 7)
                                   && x > (userX - 4) && x < (userX + 4);
                    if (nearPlayer)
                    {
                        DrawTileGrh(tile.Layer3, tilePos, center: true,
                                    modulate: new Color(1, 1, 1, 120f / 255f));
                    }
                    else
                    {
                        DrawTileGrh(tile.Layer3, tilePos, center: true);
                    }
                }
            }
        }

        // ==========================================
        // PASS 4: Layer 4 (Roof)
        // ==========================================
        if (_roofAlpha > 0)
        {
            Color roofColor = new Color(1, 1, 1, _roofAlpha / 255f);

            for (int y = minY; y <= maxY; y++)
            {
                for (int x = minX; x <= maxX; x++)
                {
                    ref var tile = ref _state.MapData.Tiles[x, y];
                    if (tile.Layer4 <= 0) continue;

                    Vector2 pos = TileToScreen(x, y, userX, userY, pixelOffsetX, pixelOffsetY);
                    DrawTileGrh(tile.Layer4, pos, center: true, modulate: roofColor);
                }
            }
        }
    }

    private void DrawTileGrh(int grhIndex, Vector2 pos, bool center = false, Color? modulate = null)
    {
        if (_data == null || _animator == null) return;
        if (grhIndex <= 0 || grhIndex >= _data.Grhs.Length) return;

        var grh = _data.Grhs[grhIndex];
        if (grh.NumFrames > 1)
            _animator.StartAnim(grhIndex);

        int frame = _animator.GetCurrentFrame(grhIndex);
        CharRenderer.DrawGrh(this, _data, grhIndex, frame, pos, center, modulate);
    }
}
