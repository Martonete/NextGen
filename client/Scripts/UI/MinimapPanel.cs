using Godot;
using System;
using System.Collections.Generic;
using ArgentumNextgen.Data;
using ArgentumNextgen.Game;
using ArgentumNextgen.Data.Resources;

namespace ArgentumNextgen.UI;

/// <summary>
/// 100x100 minimap that renders a miniature of the actual map using sampled tile colors.
/// Each pixel corresponds to one tile — the color is sampled from the tile's Layer1 texture.
/// Shows player, party, guild, NPC markers on top.
/// </summary>
public partial class MinimapPanel : Control
{
    private const int DesignMapSize = 100;  // design-space size in pixels
    private static int MapSize => ResolutionManager.S(DesignMapSize);

    // Marker sizes (radius), scaled
    private static float SelfMarkerRadius => ResolutionManager.Sf(2.5f);
    private static float PlayerMarkerRadius => ResolutionManager.Sf(1.5f);
    private static float NpcMarkerRadius => ResolutionManager.Sf(1.0f);

    // Marker colors
    private static readonly Color SelfColor = new(1f, 0f, 0f);        // Red
    private static readonly Color PartyColor = new(0.2f, 1f, 0.2f);   // Green
    private static readonly Color GuildColor = new(0.3f, 0.5f, 1f);   // Blue
    private static readonly Color PlayerColor = new(0.9f, 0.9f, 0.9f); // White
    private static readonly Color NpcFriendlyColor = new(1f, 0.85f, 0.2f); // Yellow
    private static readonly Color NpcHostileColor = new(1f, 0.2f, 0.2f);   // Red

    private const float MinimapRedrawInterval = 0.1f; // 10 Hz
    private float _redrawTimer = 0f;

    private GameState? _state;
    private GameData? _data;
    private ImageTexture? _terrainTexture;
    private int _lastRenderedMap = -1;
    private const int ViewRadius = 50; // display ±50 tiles around player

    // Cache of source images by file number (avoids reloading PNGs per tile)
    private readonly Dictionary<int, Image?> _imageCache = new();
    private string _graficosPath = "";
    private IResourceProvider? _resources;

    public HashSet<string> PartyMemberNames { get; } = new(StringComparer.OrdinalIgnoreCase);

    public void Init(GameState state, GameData? data = null, string graficosPath = "", IResourceProvider? resources = null)
    {
        _state = state;
        _data = data;
        _graficosPath = graficosPath;
        _resources = resources;
    }

    public void Toggle()
    {
        Visible = !Visible;
        if (_state != null)
            _state.Config.ShowMinimap = Visible;
    }

    public override void _Ready()
    {
        int sz = MapSize;
        CustomMinimumSize = new Vector2(sz, sz);
        Size = new Vector2(sz, sz);
        ClipContents = true;
        MouseFilter = MouseFilterEnum.Ignore;
    }

    public override void _Process(double delta)
    {
        if (!Visible) return;
        _redrawTimer += (float)delta;
        if (_redrawTimer >= MinimapRedrawInterval)
        {
            _redrawTimer = 0f;
            QueueRedraw();
        }
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
            string relativePath = $"Graficos/{grh.FileNum}.png";
            if (_resources != null)
            {
                img = _resources.ReadImage(relativePath);
            }
            else
            {
                string filePath = System.IO.Path.Combine(_graficosPath, $"{grh.FileNum}.png");
                if (System.IO.File.Exists(filePath))
                    img = Image.LoadFromFile(filePath);
            }
            if (img != null && img.GetFormat() != Image.Format.Rgba8)
                img.Convert(Image.Format.Rgba8);
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
    /// Build the full terrain texture once per map load. Each pixel = 1 tile.
    /// Uses Get() so no chunks are allocated for empty areas.
    /// The _Draw method then shows a 100x100 slice centered on the player.
    /// </summary>
    private void RebuildTerrainTexture()
    {
        if (_state?.MapData == null) return;

        _lastRenderedMap = _state.CurrentMap;
        _imageCache.Clear();

        int mapW = _state.MapData.Width;
        int mapH = _state.MapData.Height;
        var mapImg = Image.CreateEmpty(mapW, mapH, false, Image.Format.Rgba8);

        for (int y = 1; y <= mapH; y++)
            for (int x = 1; x <= mapW; x++)
            {
                var tile = _state.MapData.Tiles.Get(x, y);
                mapImg.SetPixel(x - 1, y - 1, SampleGrhColor(tile.Layer1));
            }

        _terrainTexture = ImageTexture.CreateFromImage(mapImg);
    }

    public override void _Draw()
    {
        if (_state == null) return;

        // Build full terrain texture once per map
        if (_state.CurrentMap != _lastRenderedMap || _terrainTexture == null)
            RebuildTerrainTexture();

        int mapW = _state.MapData?.Width ?? 100;
        int mapH = _state.MapData?.Height ?? 100;

        // Calculate the 100x100 viewport slice centered on the player
        int viewW = Math.Min(mapW, ViewRadius * 2);
        int viewH = Math.Min(mapH, ViewRadius * 2);
        int minX = Math.Clamp(_state.UserPosX - ViewRadius, 0, Math.Max(0, mapW - viewW));
        int minY = Math.Clamp(_state.UserPosY - ViewRadius, 0, Math.Max(0, mapH - viewH));

        // Draw the slice of the full texture — source rect moves with the player
        if (_terrainTexture != null)
        {
            var srcRect = new Rect2(minX, minY, viewW, viewH);
            DrawTextureRectRegion(_terrainTexture, new Rect2(0, 0, MapSize, MapSize), srcRect);
        }

        // Scale: viewport tile coords → 100px display
        float scaleX = MapSize / (float)viewW;
        float scaleY = MapSize / (float)viewH;

        // Draw NPCs
        foreach (var kv in _state.Characters)
        {
            var ch = kv.Value;
            if (ch.CharIndex == _state.UserCharIndex) continue;
            if (ch.NpcNumber <= 0) continue;

            float px = (ch.PosX - 1 - minX) * scaleX;
            float py = (ch.PosY - 1 - minY) * scaleY;
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

            float px = (ch.PosX - 1 - minX) * scaleX;
            float py = (ch.PosY - 1 - minY) * scaleY;
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

        // Draw self as directional arrow based on heading
        {
            float px = (_state.UserPosX - 1 - minX) * scaleX;
            float py = (_state.UserPosY - 1 - minY) * scaleY;
            var center = new Vector2(px, py);

            // Heading: 1=N, 2=E, 3=S, 4=W → rotation angle in radians
            int heading = 3; // default south
            if (_state.Characters.TryGetValue(_state.UserCharIndex, out var selfCh))
                heading = selfCh.Heading;
            float angle = heading switch
            {
                1 => -Mathf.Pi / 2f,  // North (up)
                2 => 0f,               // East (right)
                3 => Mathf.Pi / 2f,   // South (down)
                4 => Mathf.Pi,         // West (left)
                _ => Mathf.Pi / 2f,
            };

            // Arrow: 3 points (tip, left wing, right wing)
            float size = SelfMarkerRadius + 2f;
            var tip = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * size;
            var left = new Vector2(Mathf.Cos(angle + 2.5f), Mathf.Sin(angle + 2.5f)) * size * 0.7f;
            var right = new Vector2(Mathf.Cos(angle - 2.5f), Mathf.Sin(angle - 2.5f)) * size * 0.7f;

            // Shadow
            var shadow = new Color(0, 0, 0, 0.6f);
            var shadowOff = new Vector2(0.5f, 0.5f);
            DrawLine(center + left + shadowOff, center + tip + shadowOff, shadow, 1.5f);
            DrawLine(center + right + shadowOff, center + tip + shadowOff, shadow, 1.5f);
            DrawLine(center + left + shadowOff, center + right + shadowOff, shadow, 1.5f);

            // Arrow fill
            DrawColoredPolygon(new Vector2[] { center + tip, center + left, center + right }, SelfColor);
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
