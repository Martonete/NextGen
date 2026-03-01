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

    public void Init(GameState state, GameData data, GrhAnimator animator)
    {
        _state = state;
        _data = data;
        _animator = animator;
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
    /// VB6 uses the SAME pixel formula for all layers — only the tile RANGE differs.
    /// The -1 that appears in VB6's L1 formula cancels with the expanded range offset.
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
        // Rounded to int to match VB6+DX8 pixel-snapping (prevents sub-pixel jitter).
        float pixelOffsetX = (float)Math.Round(-_state.ScreenOffsetX);
        float pixelOffsetY = (float)Math.Round(-_state.ScreenOffsetY);

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
        // PASS 1: Layer 1 (Ground) — visible area +2 tile margin
        // +2 instead of +1 because during scrolling the camera pixel offset
        // shifts up to 32px, revealing an extra tile beyond the +1 margin.
        // Without this, you see black gaps at the scroll edges.
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

                Vector2 pos = TileToScreen(x, y, userX, userY, pixelOffsetX, pixelOffsetY);
                DrawTileGrh(tile.Layer1, pos, center: false);
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
                DrawTileGrh(tile.Layer2, pos, center: false);
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

                    // Round MoveOffset to int (VB6+DX8 pixel-snapping)
                    float charPx = tilePos.X + (float)Math.Round(ch.MoveOffsetX);
                    float charPy = tilePos.Y + (float)Math.Round(ch.MoveOffsetY);

                    CharRenderer.DrawCharacter(this, ch, new Vector2(charPx, charPy), _data, _animator, _deltaMs);
                }

                // Layer 3 (trees/objects)
                if (tile.Layer3 > 0)
                {
                    // VB6: Only trees get alpha near player (EsArbol check)
                    bool nearPlayer = IsTree(tile.Layer3)
                                   && y > (userY - 2) && y < (userY + 7)
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

    /// <summary>
    /// VB6 EsArbol(): checks if a GRH index is a tree graphic.
    /// Only trees get alpha transparency near the player.
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

    private void DrawTileGrh(int grhIndex, Vector2 pos, bool center = false, Color? modulate = null)
    {
        if (_data == null || _animator == null) return;
        if (grhIndex <= 0 || grhIndex >= _data.Grhs.Length) return;

        // Looping tile animations use the global clock — no StartAnim needed.
        int frame = _animator.GetCurrentFrame(grhIndex, _data);
        CharRenderer.DrawGrh(this, _data, grhIndex, frame, pos, center, modulate);
    }
}
