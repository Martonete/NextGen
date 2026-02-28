using System;
using System.Collections.Generic;
using Godot;
using TierrasSagradasAO.Data;
using TierrasSagradasAO.Game;

namespace TierrasSagradasAO.Rendering;

/// <summary>
/// Renders the game world matching VB6 RenderScreen exactly:
/// - 4 tile layers with correct draw order
/// - Tile buffer (extra tiles beyond viewport)
/// - Multi-tile graphic centering (Center=1)
/// - Tree alpha near player (VB6 EsArbol check, alpha=120)
/// - Gradual roof fade (bTechoAB, ±6 alpha/frame)
/// - Character depth sorting by tile Y,X order
/// - Ground objects (HO items)
/// </summary>
public partial class WorldRenderer : Node2D
{
    private GameState? _state;
    private GameData? _data;
    private GrhAnimator? _animator;

    private const int TileSize = 32;
    private const int ScreenWidth = 800;
    private const int ScreenHeight = 600;

    // VB6: HalfWindowTileWidth=10, HalfWindowTileHeight=8
    private const int HalfTilesX = 10;
    private const int HalfTilesY = 8;

    // VB6: Engine_Get_TileBuffer = 3 (extra tiles rendered beyond visible)
    private const int TileBuffer = 3;

    // VB6: bTechoAB — roof alpha, fades gradually
    private float _roofAlpha = 255f;
    private const float RoofFadeRate = 6f; // ~0.4 * 15 per frame from VB6

    public void Init(GameState state, GameData data, GrhAnimator animator)
    {
        _state = state;
        _data = data;
        _animator = animator;
    }

    public override void _Process(double delta)
    {
        if (_state?.MapData == null) return;

        // Update roof fade (VB6: bTecho based on trigger 1, 2, or 4)
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
            // Fade out roof
            _roofAlpha -= RoofFadeRate;
            if (_roofAlpha < 0) _roofAlpha = 0;
        }
        else
        {
            // Fade in roof
            _roofAlpha += RoofFadeRate;
            if (_roofAlpha > 255) _roofAlpha = 255;
        }
    }

    public override void _Draw()
    {
        if (_state == null || _data == null || _animator == null) return;
        if (_state.MapData == null || _state.Paused) return;

        int userX = _state.UserPosX;
        int userY = _state.UserPosY;

        // Find self character for smooth scroll offset
        float moveOffX = 0, moveOffY = 0;
        if (_state.Characters.TryGetValue(_state.UserCharIndex, out var selfChar))
        {
            moveOffX = selfChar.MoveOffsetX;
            moveOffY = selfChar.MoveOffsetY;
        }

        float centerX = ScreenWidth / 2f;
        float centerY = ScreenHeight / 2f;

        // Visible tile bounds (VB6: screenminX/Y)
        int screenMinX = userX - HalfTilesX;
        int screenMaxX = userX + HalfTilesX;
        int screenMinY = userY - HalfTilesY;
        int screenMaxY = userY + HalfTilesY;

        // Extended bounds with tile buffer (VB6: minX/Y with Engine_Get_TileBuffer)
        int minX = Math.Max(1, screenMinX - TileBuffer);
        int maxX = Math.Min(100, screenMaxX + TileBuffer);
        int minY = Math.Max(1, screenMinY - TileBuffer);
        int maxY = Math.Min(100, screenMaxY + TileBuffer);

        // ==========================================
        // PASS 1: Layer 1 (ground) + Layer 2 (mid)
        // VB6: first loop, visible area only
        // ==========================================
        for (int y = screenMinY; y <= screenMaxY; y++)
        {
            for (int x = screenMinX; x <= screenMaxX; x++)
            {
                if (x < 1 || x > 100 || y < 1 || y > 100) continue;

                ref var tile = ref _state.MapData.Tiles[x, y];
                Vector2 pos = TileToScreen(x, y, userX, userY, centerX, centerY, moveOffX, moveOffY);

                // Layer 1 — always
                if (tile.Layer1 > 0)
                    DrawTileGrh(tile.Layer1, pos);

                // Layer 2
                if (tile.Layer2 > 0)
                    DrawTileGrh(tile.Layer2, pos);
            }
        }

        // ==========================================
        // PASS 2: Layer 3 + Ground objects + Characters (with buffer)
        // VB6: second loop, extended bounds for multi-tile graphics
        // Draw order: ObjGrh → charindex → Graphic(3) per tile
        // ==========================================
        for (int y = minY; y <= maxY; y++)
        {
            for (int x = minX; x <= maxX; x++)
            {
                ref var tile = ref _state.MapData.Tiles[x, y];
                Vector2 pos = TileToScreen(x, y, userX, userY, centerX, centerY, moveOffX, moveOffY);

                // Ground objects (HO items) — VB6: ObjGrh before characters
                if (_state.GroundObjects.TryGetValue((x, y), out int objGrh) && objGrh > 0)
                {
                    DrawTileGrh(objGrh, pos, center: true);
                }

                // Characters/NPCs at this tile
                foreach (var kvp in _state.Characters)
                {
                    var ch = kvp.Value;
                    if (ch.PosX != x || ch.PosY != y) continue;

                    Vector2 charPos = new Vector2(
                        pos.X + ch.MoveOffsetX,
                        pos.Y + ch.MoveOffsetY
                    );
                    CharRenderer.DrawCharacter(this, ch, charPos, _data, _animator);
                }

                // Layer 3 (trees/objects)
                if (tile.Layer3 > 0)
                {
                    // VB6: EsArbol check — reduce alpha near player
                    bool nearPlayer = y > (userY - 2) && y < (userY + 7)
                                   && x > (userX - 4) && x < (userX + 4);
                    if (nearPlayer)
                    {
                        DrawTileGrh(tile.Layer3, pos, center: true,
                                    modulate: new Color(1, 1, 1, 120f / 255f));
                    }
                    else
                    {
                        DrawTileGrh(tile.Layer3, pos, center: true);
                    }
                }
            }
        }

        // ==========================================
        // PASS 3: Layer 4 (roof) — gradual alpha fade
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

                    Vector2 pos = TileToScreen(x, y, userX, userY, centerX, centerY, moveOffX, moveOffY);
                    DrawTileGrh(tile.Layer4, pos, center: true, modulate: roofColor);
                }
            }
        }
    }

    /// <summary>
    /// Convert tile coordinates to screen pixel position.
    /// </summary>
    private static Vector2 TileToScreen(
        int tileX, int tileY, int userX, int userY,
        float centerX, float centerY, float moveOffX, float moveOffY)
    {
        float sx = centerX + (tileX - userX) * TileSize - TileSize / 2f + moveOffX;
        float sy = centerY + (tileY - userY) * TileSize - TileSize / 2f + moveOffY;
        return new Vector2(sx, sy);
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
