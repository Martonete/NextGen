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
    private const int TileGrid = 100; // AO maps are 100x100
    private const int MapSize = 100;  // rendered size in pixels (1:1 with tiles)

    // Marker sizes (radius)
    private const float SelfMarkerRadius = 2.5f;
    private const float PlayerMarkerRadius = 1.5f;
    private const float NpcMarkerRadius = 1.0f;

    // Marker colors
    private static readonly Color SelfColor = new(0f, 1f, 1f);        // Cyan
    private static readonly Color PartyColor = new(0.2f, 1f, 0.2f);   // Green
    private static readonly Color GuildColor = new(0.3f, 0.5f, 1f);   // Blue
    private static readonly Color PlayerColor = new(0.9f, 0.9f, 0.9f); // White
    private static readonly Color NpcFriendlyColor = new(1f, 0.85f, 0.2f); // Yellow
    private static readonly Color NpcHostileColor = new(1f, 0.2f, 0.2f);   // Red

    private GameState? _state;
    private GameData? _data;
    private ImageTexture? _terrainTexture;
    private int _lastRenderedMap = -1;

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
    /// Rebuild the 100x100 terrain texture by sampling each tile's Layer1 color.
    /// </summary>
    private void RebuildTerrainTexture()
    {
        if (_state?.MapData == null) return;

        _lastRenderedMap = _state.CurrentMap;
        _imageCache.Clear(); // clear source image cache for new map

        var mapImg = Image.CreateEmpty(TileGrid, TileGrid, false, Image.Format.Rgba8);

        for (int y = 1; y <= TileGrid; y++)
        {
            for (int x = 1; x <= TileGrid; x++)
            {
                var tile = _state.MapData.Tiles[x, y];
                Color c = SampleGrhColor(tile.Layer1);
                mapImg.SetPixel(x - 1, y - 1, c);
            }
        }

        _terrainTexture = ImageTexture.CreateFromImage(mapImg);
    }

    public override void _Draw()
    {
        if (_state == null) return;

        // Rebuild terrain when map changes
        if (_state.CurrentMap != _lastRenderedMap || _terrainTexture == null)
            RebuildTerrainTexture();

        // Draw terrain — 100x100 pixels, one pixel per tile
        if (_terrainTexture != null)
            DrawTextureRect(_terrainTexture, new Rect2(0, 0, MapSize, MapSize), false);

        // Scale: tile coords (1-100) → pixel coords
        float scale = MapSize / (float)TileGrid; // = 1.0

        // Draw NPCs (below players)
        foreach (var kv in _state.Characters)
        {
            var ch = kv.Value;
            if (ch.CharIndex == _state.UserCharIndex) continue;
            if (ch.NpcNumber <= 0) continue;

            float px = (ch.PosX - 1) * scale;
            float py = (ch.PosY - 1) * scale;
            Color npcColor = ch.Criminal ? NpcHostileColor : NpcFriendlyColor;
            DrawCircle(new Vector2(px, py), NpcMarkerRadius, npcColor);
        }

        // Draw other players
        string userGuild = _state.UserGuildName;
        foreach (var kv in _state.Characters)
        {
            var ch = kv.Value;
            if (ch.CharIndex == _state.UserCharIndex) continue;
            if (ch.NpcNumber > 0) continue;

            float px = (ch.PosX - 1) * scale;
            float py = (ch.PosY - 1) * scale;

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

        // Draw self (on top)
        {
            float px = (_state.UserPosX - 1) * scale;
            float py = (_state.UserPosY - 1) * scale;
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
