using Godot;
using System;
using System.Collections.Generic;
using ArgentumNextgen.Data;
using ArgentumNextgen.Game;

namespace ArgentumNextgen.UI;

/// <summary>
/// 100x100 minimap that renders a miniature of the actual map using sampled tile colors.
/// Each pixel corresponds to one tile — the color is sampled from the tile's Layer1 texture.
/// Shows player, party, guild, NPC markers on top.
/// </summary>
public partial class MinimapPanel : Control
{
    private const int MapSize = 100;  // rendered size in pixels (fixed display size)

    // Marker sizes (radius)
    private const float SelfMarkerRadius = 2.5f;
    private const float PlayerMarkerRadius = 1.5f;
    private const float NpcMarkerRadius = 1.0f;

    // Marker colors
    private static readonly Color SelfColor = new(1f, 0f, 0f);        // Red
    private static readonly Color PartyColor = new(0.2f, 1f, 0.2f);   // Green
    private static readonly Color GuildColor = new(0.3f, 0.5f, 1f);   // Blue
    private static readonly Color PlayerColor = new(0.9f, 0.9f, 0.9f); // White
    private static readonly Color NpcFriendlyColor = new(1f, 0.85f, 0.2f); // Yellow
    private static readonly Color NpcHostileColor = new(1f, 0.2f, 0.2f);   // Red

    private GameState? _state;
    private GameData? _data;
    private ImageTexture? _terrainTexture;
    private int _lastRenderedMap = -1;
    // Viewport tracking — minimap re-renders when player moves enough
    private int _lastCenterX = -1;
    private int _lastCenterY = -1;
    private const int ViewRadius = 50; // render 100x100 area (±50 tiles from player)

    // Cache of source images by file number (avoids reloading PNGs per tile)
    private readonly Dictionary<int, Image?> _imageCache = new();
    private string _graficosPath = "";

    public HashSet<string> PartyMemberNames { get; } = new(StringComparer.OrdinalIgnoreCase);

    public void Init(GameState state, GameData? data = null, string graficosPath = "")
    {
        _state = state;
        _data = data;
        _graficosPath = graficosPath;
    }

    public void Toggle()
    {
        Visible = !Visible;
        if (_state != null)
            _state.Config.ShowMinimap = Visible;
    }

    public override void _Ready()
    {
        CustomMinimumSize = new Vector2(MapSize, MapSize);
        Size = new Vector2(MapSize, MapSize);
        ClipContents = true;
        MouseFilter = MouseFilterEnum.Ignore;
    }

    public override void _Process(double delta)
    {
        if (Visible)
            QueueRedraw();
    }

    /// <summary>
    /// Sample the dominant color from a GRH's source texture region.
    /// Returns the average color of a small sample at the center of the tile.
    /// </summary>
    private Color SampleGrhColor(int grhIndex)
    {
        if (_data == null || grhIndex <= 0 || grhIndex >= _data.Grhs.Length)
            return new Color(0.15f, 0.15f, 0.15f);

        var grh = _data.ResolveGrh(grhIndex, 0);
        if (grh == null || grh.FileNum <= 0)
            return new Color(0.15f, 0.15f, 0.15f);

        // Get source image (cached)
        if (!_imageCache.TryGetValue(grh.FileNum, out var img))
        {
            string filePath = System.IO.Path.Combine(_graficosPath, $"{grh.FileNum}.png");
            if (System.IO.File.Exists(filePath))
            {
                img = Image.LoadFromFile(filePath);
                if (img != null && img.GetFormat() != Image.Format.Rgba8)
                    img.Convert(Image.Format.Rgba8);
            }
            _imageCache[grh.FileNum] = img;
        }

        if (img == null)
            return new Color(0.15f, 0.15f, 0.15f);

        // Sample center pixel of the GRH region
        int cx = grh.SX + grh.PixelWidth / 2;
        int cy = grh.SY + grh.PixelHeight / 2;

        // Clamp to image bounds
        cx = Math.Clamp(cx, 0, img.GetWidth() - 1);
        cy = Math.Clamp(cy, 0, img.GetHeight() - 1);

        var pixel = img.GetPixel(cx, cy);

        // If pixel is near-black (color key / transparent), sample a few more points
        if (pixel.R < 0.05f && pixel.G < 0.05f && pixel.B < 0.05f)
        {
            // Try quarter points
            int qx1 = Math.Clamp(grh.SX + grh.PixelWidth / 4, 0, img.GetWidth() - 1);
            int qy1 = Math.Clamp(grh.SY + grh.PixelHeight / 4, 0, img.GetHeight() - 1);
            pixel = img.GetPixel(qx1, qy1);

            if (pixel.R < 0.05f && pixel.G < 0.05f && pixel.B < 0.05f)
            {
                int qx2 = Math.Clamp(grh.SX + grh.PixelWidth * 3 / 4, 0, img.GetWidth() - 1);
                int qy2 = Math.Clamp(grh.SY + grh.PixelHeight * 3 / 4, 0, img.GetHeight() - 1);
                pixel = img.GetPixel(qx2, qy2);
            }
        }

        // Still black? Use a neutral dark gray
        if (pixel.R < 0.02f && pixel.G < 0.02f && pixel.B < 0.02f)
            return new Color(0.12f, 0.12f, 0.12f);

        return new Color(pixel.R, pixel.G, pixel.B, 1f);
    }

    /// <summary>
    /// Rebuild terrain texture for a 100x100 tile area centered on the player.
    /// Only samples chunks near the player — does not iterate the full map.
    /// Re-renders when the player moves more than 10 tiles from last center.
    /// </summary>
    private void RebuildTerrainTexture()
    {
        if (_state?.MapData == null) return;

        int cx = _state.UserPosX;
        int cy = _state.UserPosY;
        int mapW = _state.MapData.Width;
        int mapH = _state.MapData.Height;

        // Small maps (<=100): render full map as before
        if (mapW <= 100 && mapH <= 100)
        {
            _lastRenderedMap = _state.CurrentMap;
            _lastCenterX = cx;
            _lastCenterY = cy;
            _imageCache.Clear();

            var fullImg = Image.CreateEmpty(mapW, mapH, false, Image.Format.Rgba8);
            for (int y = 1; y <= mapH; y++)
                for (int x = 1; x <= mapW; x++)
                {
                    var tile = _state.MapData.Tiles.Get(x, y);
                    fullImg.SetPixel(x - 1, y - 1, SampleGrhColor(tile.Layer1));
                }
            _terrainTexture = ImageTexture.CreateFromImage(fullImg);
            return;
        }

        // Large maps: render 100x100 viewport around player
        _lastRenderedMap = _state.CurrentMap;
        _lastCenterX = cx;
        _lastCenterY = cy;

        int minX = Math.Max(1, cx - ViewRadius);
        int minY = Math.Max(1, cy - ViewRadius);
        int maxX = Math.Min(mapW, cx + ViewRadius - 1);
        int maxY = Math.Min(mapH, cy + ViewRadius - 1);
        int renderW = maxX - minX + 1;
        int renderH = maxY - minY + 1;

        var mapImg = Image.CreateEmpty(renderW, renderH, false, Image.Format.Rgba8);
        for (int y = minY; y <= maxY; y++)
            for (int x = minX; x <= maxX; x++)
            {
                var tile = _state.MapData.Tiles.Get(x, y);
                mapImg.SetPixel(x - minX, y - minY, SampleGrhColor(tile.Layer1));
            }
        _terrainTexture = ImageTexture.CreateFromImage(mapImg);
    }

    /// <summary>Check if the minimap needs re-rendering (map changed or player moved 10+ tiles).</summary>
    private bool NeedsRebuild()
    {
        if (_state == null) return false;
        if (_state.CurrentMap != _lastRenderedMap || _terrainTexture == null) return true;
        int dx = Math.Abs(_state.UserPosX - _lastCenterX);
        int dy = Math.Abs(_state.UserPosY - _lastCenterY);
        return dx >= 10 || dy >= 10;
    }

    public override void _Draw()
    {
        if (_state == null) return;

        // Rebuild terrain when map changes or player moved 10+ tiles
        if (NeedsRebuild())
            RebuildTerrainTexture();

        // Draw terrain texture stretched to 100x100 display
        if (_terrainTexture != null)
            DrawTextureRect(_terrainTexture, new Rect2(0, 0, MapSize, MapSize), false);

        // Calculate viewport bounds for marker positioning
        int mapW = _state.MapData?.Width ?? 100;
        int mapH = _state.MapData?.Height ?? 100;
        int minX, minY, renderW, renderH;

        if (mapW <= 100 && mapH <= 100)
        {
            // Small map: full map in minimap
            minX = 1; minY = 1;
            renderW = mapW; renderH = mapH;
        }
        else
        {
            // Large map: viewport centered on player
            minX = Math.Max(1, _lastCenterX - ViewRadius);
            minY = Math.Max(1, _lastCenterY - ViewRadius);
            int maxX = Math.Min(mapW, _lastCenterX + ViewRadius - 1);
            int maxY = Math.Min(mapH, _lastCenterY + ViewRadius - 1);
            renderW = maxX - minX + 1;
            renderH = maxY - minY + 1;
        }

        float scaleX = MapSize / (float)renderW;
        float scaleY = MapSize / (float)renderH;

        // Draw NPCs
        foreach (var kv in _state.Characters)
        {
            var ch = kv.Value;
            if (ch.CharIndex == _state.UserCharIndex) continue;
            if (ch.NpcNumber <= 0) continue;

            float px = (ch.PosX - minX) * scaleX;
            float py = (ch.PosY - minY) * scaleY;
            if (px < 0 || px > MapSize || py < 0 || py > MapSize) continue;
            DrawCircle(new Vector2(px, py), NpcMarkerRadius, ch.Criminal ? NpcHostileColor : NpcFriendlyColor);
        }

        // Draw other players
        string userGuild = _state.UserGuildName;
        foreach (var kv in _state.Characters)
        {
            var ch = kv.Value;
            if (ch.CharIndex == _state.UserCharIndex) continue;
            if (ch.NpcNumber > 0) continue;

            float px = (ch.PosX - minX) * scaleX;
            float py = (ch.PosY - minY) * scaleY;
            if (px < 0 || px > MapSize || py < 0 || py > MapSize) continue;

            string baseName = ExtractBaseName(ch.Name);
            Color color;
            if (PartyMemberNames.Contains(baseName))
                color = PartyColor;
            else if (!string.IsNullOrEmpty(userGuild) && HasClanTag(ch.Name, userGuild))
                color = GuildColor;
            else
                color = PlayerColor;

            DrawCircle(new Vector2(px, py), PlayerMarkerRadius, color);
        }

        // Draw self (always centered for large maps)
        {
            float px = (_state.UserPosX - minX) * scaleX;
            float py = (_state.UserPosY - minY) * scaleY;
            DrawCircle(new Vector2(px, py), SelfMarkerRadius + 1f, new Color(0, 0, 0, 0.6f));
            DrawCircle(new Vector2(px, py), SelfMarkerRadius, SelfColor);
        }
    }

    private static string ExtractBaseName(string name)
    {
        int ltIdx = name.IndexOf('<');
        return ltIdx >= 0 ? name[..ltIdx] : name;
    }

    private static bool HasClanTag(string name, string guildName)
    {
        int ltIdx = name.IndexOf('<');
        if (ltIdx < 0) return false;
        string tag = name[(ltIdx + 1)..];
        if (tag.EndsWith('>')) tag = tag[..^1];
        return string.Equals(tag, guildName, StringComparison.OrdinalIgnoreCase);
    }
}
