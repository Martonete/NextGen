using System;
using Godot;

namespace ArgentumNextgen.Rendering;

public partial class WorldRenderer
{

    private void RebuildSceneryBounds()
    {
        _terrainHorizontalBuffer = _terrainBottomBuffer = TerrainBufferSize;
        if (_state?.MapData == null || _data == null) return;
        var seen = new System.Collections.Generic.HashSet<int>();
        void Include(int grh)
        {
            if (grh <= 0 || !seen.Add(grh)) return;
            var sprite = _data.ResolveGrh(grh, 0);
            if (sprite == null) return;
            // Multi-tile sprites extend sideways and upwards from the anchor.
            _terrainHorizontalBuffer = Math.Max(_terrainHorizontalBuffer,
                (int)Math.Ceiling(sprite.PixelWidth / 64f) + 1);
            _terrainBottomBuffer = Math.Max(_terrainBottomBuffer,
                (int)Math.Ceiling(sprite.PixelHeight / 32f) + 1);
        }
        var map = _state.MapData;
        for (int y = 1; y <= map.Height; y++)
            for (int x = 1; x <= map.Width; x++)
            {
                ref var tile = ref map.Tiles[x, y];
                Include(tile.Layer2); Include(tile.Layer3); Include(tile.Layer4);
            }
    }

    private float GetTreeOpacity(int grh, int x, int y)
    {
        if (!(_state?.Config?.TreeRoofTransparency ?? true) || _data == null || _animator == null
            || _state == null || !_state.Characters.TryGetValue(_state.UserCharIndex, out var player)) return 1f;
        var sprite = _data.ResolveGrh(grh, _animator.GetCurrentFrame(grh, _data));
        if (sprite == null) return 1f;
        float left = x * TileSize - (sprite.TileWidth != 1f && sprite.TileWidth > 0 ? (int)(sprite.TileWidth * 16) - 16 : 0);
        float top = y * TileSize - (sprite.TileHeight != 1f && sprite.TileHeight > 0 ? (int)(sprite.TileHeight * 32) - 32 : 0);
        // Match the row/column order of DrawContent; trees behind the player stay solid.
        bool foreground = y > player.PosY || (y == player.PosY && x >= player.PosX);
        return SceneryMath.TreeOpacity(left, top, sprite.PixelWidth, sprite.PixelHeight,
            player.PosX * TileSize + player.MoveOffsetX + 16f,
            player.PosY * TileSize + player.MoveOffsetY - 8f,
            foreground, (_state.Config?.TreeTransparencyAlpha ?? 47) / 100f);
    }

    private void DrawTree(CanvasItem canvas, int grh, int x, int y, Vector2 pos, Color color)
    {
        // Static sprite: no canopy deformation or wind calculations.
        DrawTileGrhTo(canvas, grh, pos, center: true, modulate: color);
    }
}
