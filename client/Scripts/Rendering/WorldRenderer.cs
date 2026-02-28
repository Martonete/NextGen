using System;
using System.Collections.Generic;
using Godot;
using TierrasSagradasAO.Data;
using TierrasSagradasAO.Game;

namespace TierrasSagradasAO.Rendering;

/// <summary>
/// Renders the game world matching VB6 RenderScreen exactly:
/// - 534x408 viewport (VB6 PictureBox, not full 800x600 window)
/// - 4 tile layers with correct VB6 draw order and pass separation
/// - TileBufferSize=9 (VB6 value)
/// - Multi-tile graphic centering (Center=1)
/// - Tree alpha near player (VB6 EsArbol check, alpha=120)
/// - Gradual roof fade (bTechoAB, ±6 alpha/frame)
/// - Character position index for O(1) tile lookup
/// - Ground objects (HO items)
/// </summary>
public partial class WorldRenderer : Node2D
{
    private GameState? _state;
    private GameData? _data;
    private GrhAnimator? _animator;

    private const int TileSize = 32;

    // VB6 viewport: 534x408 px (the renderer PictureBox area)
    private const int ViewportWidth = 534;
    private const int ViewportHeight = 408;

    // VB6: 534/32 = 16.7 → 17 tiles wide, 408/32 = 12.7 → 13 tiles tall
    // Half values: 17/2 = 8, 13/2 = 6
    private const int HalfWindowTileWidth = 8;
    private const int HalfWindowTileHeight = 6;

    // VB6: TileBufferSize = 9 (extra tiles rendered beyond visible for multi-tile graphics)
    private const int TileBufferSize = 9;

    // VB6: bTechoAB — roof alpha, fades gradually
    private float _roofAlpha = 255f;
    private const float RoofFadeRate = 6f;

    // Per-frame character position index: (tileX, tileY) → list of char indices
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

    /// <summary>
    /// Build per-frame index mapping (tileX, tileY) → character indices
    /// for O(1) lookup instead of iterating all characters per tile.
    /// </summary>
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

    public override void _Draw()
    {
        if (_state == null || _data == null || _animator == null) return;
        if (_state.MapData == null || _state.Paused) return;

        int userX = _state.UserPosX;
        int userY = _state.UserPosY;

        // Build character position index once per frame
        BuildCharPositionIndex();

        // Find self character for smooth scroll offset
        float moveOffX = 0, moveOffY = 0;
        if (_state.Characters.TryGetValue(_state.UserCharIndex, out var selfChar))
        {
            moveOffX = selfChar.MoveOffsetX;
            moveOffY = selfChar.MoveOffsetY;
        }

        // VB6 pixel offset for smooth scrolling
        float pixelOffsetX = moveOffX;
        float pixelOffsetY = moveOffY;

        // Visible tile range (VB6: screenMinX/Y)
        int screenMinX = userX - HalfWindowTileWidth;
        int screenMaxX = userX + HalfWindowTileWidth;
        int screenMinY = userY - HalfWindowTileHeight;
        int screenMaxY = userY + HalfWindowTileHeight;

        // Extended bounds with tile buffer (VB6: minX/Y with TileBufferSize)
        int minX = Math.Max(1, screenMinX - TileBufferSize);
        int maxX = Math.Min(100, screenMaxX + TileBufferSize);
        int minY = Math.Max(1, screenMinY - TileBufferSize);
        int maxY = Math.Min(100, screenMaxY + TileBufferSize);

        // ==========================================
        // PASS 1: Layer 1 (Ground) — visible area only, expanded by 1
        // VB6: (screenX - 1) * 32 + PixelOffsetX
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

                // VB6 Layer 1 formula: (screenX - 1) * 32 + PixelOffsetX
                int screenX = x - userX + HalfWindowTileWidth;
                int screenY = y - userY + HalfWindowTileHeight;
                float px = (screenX - 1) * TileSize + pixelOffsetX;
                float py = (screenY - 1) * TileSize + pixelOffsetY;

                DrawTileGrh(tile.Layer1, new Vector2(px, py));
            }
        }

        // ==========================================
        // PASS 2: Layer 2 — extended buffer range
        // VB6: screenX * 32 + PixelOffsetX (different from Layer 1!)
        // ==========================================
        for (int y = minY; y <= maxY; y++)
        {
            for (int x = minX; x <= maxX; x++)
            {
                ref var tile = ref _state.MapData.Tiles[x, y];
                if (tile.Layer2 <= 0) continue;

                int screenX = x - userX + HalfWindowTileWidth;
                int screenY = y - userY + HalfWindowTileHeight;
                float px = screenX * TileSize + pixelOffsetX;
                float py = screenY * TileSize + pixelOffsetY;

                DrawTileGrh(tile.Layer2, new Vector2(px, py), center: true);
            }
        }

        // ==========================================
        // PASS 3: Objects + Characters + Layer 3 — extended buffer range
        // VB6: same screenX * 32 formula as Layer 2
        // Draw order per tile: ObjGrh → characters → Layer 3
        // ==========================================
        for (int y = minY; y <= maxY; y++)
        {
            for (int x = minX; x <= maxX; x++)
            {
                int screenX = x - userX + HalfWindowTileWidth;
                int screenY = y - userY + HalfWindowTileHeight;
                float px = screenX * TileSize + pixelOffsetX;
                float py = screenY * TileSize + pixelOffsetY;
                Vector2 tileScreenPos = new Vector2(px, py);

                ref var tile = ref _state.MapData.Tiles[x, y];

                // Ground objects (HO items) — VB6: ObjGrh before characters
                if (_state.GroundObjects.TryGetValue((x, y), out int objGrh) && objGrh > 0)
                {
                    DrawTileGrh(objGrh, tileScreenPos, center: true);
                }

                // Characters/NPCs at this tile (O(1) lookup via index)
                var charsHere = GetCharsAt(x, y);
                for (int ci = 0; ci < charsHere.Count; ci++)
                {
                    if (!_state.Characters.TryGetValue(charsHere[ci], out var ch)) continue;

                    // Character position includes their own smooth movement offset
                    // relative to the tile, but the scroll offset is already in pixelOffset
                    float charPx = px;
                    float charPy = py;

                    // For non-self characters, add their individual movement offset
                    if (charsHere[ci] != _state.UserCharIndex)
                    {
                        charPx += ch.MoveOffsetX;
                        charPy += ch.MoveOffsetY;
                    }

                    CharRenderer.DrawCharacter(this, ch, new Vector2(charPx, charPy), _data, _animator);
                }

                // Layer 3 (trees/objects)
                if (tile.Layer3 > 0)
                {
                    // VB6: EsArbol check — reduce alpha near player
                    bool nearPlayer = y > (userY - 2) && y < (userY + 7)
                                   && x > (userX - 4) && x < (userX + 4);
                    if (nearPlayer)
                    {
                        DrawTileGrh(tile.Layer3, tileScreenPos, center: true,
                                    modulate: new Color(1, 1, 1, 120f / 255f));
                    }
                    else
                    {
                        DrawTileGrh(tile.Layer3, tileScreenPos, center: true);
                    }
                }
            }
        }

        // ==========================================
        // PASS 4: Layer 4 (Roof) — gradual alpha fade
        // VB6: bTechoAB controls alpha, only renders if > 0
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

                    int screenX = x - userX + HalfWindowTileWidth;
                    int screenY = y - userY + HalfWindowTileHeight;
                    float px = screenX * TileSize + pixelOffsetX;
                    float py = screenY * TileSize + pixelOffsetY;

                    DrawTileGrh(tile.Layer4, new Vector2(px, py), center: true, modulate: roofColor);
                }
            }
        }
    }

    /// <summary>
    /// Draw a tile GRH with optional centering and color modulation.
    /// </summary>
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
