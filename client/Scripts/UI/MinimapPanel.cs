using Godot;
using System;
using System.Collections.Generic;
using ArgentumNextgen.Game;

namespace ArgentumNextgen.UI;

/// <summary>
/// Minimap overlay showing the player, other players, and NPCs as colored dots.
/// Draws on a 100x100 tile grid mapped to a compact panel.
/// Toggle with the Mapa sidebar button. Respects GameConfig.ShowMinimap.
/// </summary>
public partial class MinimapPanel : Control
{
    // Panel dimensions — fits next to sidebar
    private const int MapPixels = 128; // rendered minimap size (square)
    private const int Padding = 6;
    private const int PanelW = MapPixels + Padding * 2;
    private const int PanelH = MapPixels + Padding * 2 + 20; // +20 for coord text
    private const int TileGrid = 100; // AO maps are 100x100

    // Marker sizes (radius)
    private const float SelfMarkerRadius = 3.5f;
    private const float PlayerMarkerRadius = 2.0f;
    private const float NpcMarkerRadius = 1.5f;

    // Colors
    private static readonly Color BgColor = new(0.05f, 0.05f, 0.1f, 0.85f);
    private static readonly Color BorderColor = new(0.3f, 0.3f, 0.4f, 0.8f);
    private static readonly Color SelfColor = new(0f, 1f, 1f);        // Cyan
    private static readonly Color PartyColor = new(0.2f, 1f, 0.2f);   // Green
    private static readonly Color GuildColor = new(0.3f, 0.5f, 1f);   // Blue
    private static readonly Color PlayerColor = new(0.9f, 0.9f, 0.9f); // White
    private static readonly Color NpcFriendlyColor = new(1f, 0.85f, 0.2f); // Yellow
    private static readonly Color NpcHostileColor = new(1f, 0.2f, 0.2f);   // Red
    private static readonly Color CoordColor = new(0.7f, 0.7f, 0.7f);

    private GameState? _state;

    /// <summary>
    /// Set of character names currently in the party.
    /// Updated by Main.cs from PartyPanel member data.
    /// Names are stored WITHOUT clan tag (base name only).
    /// </summary>
    public HashSet<string> PartyMemberNames { get; } = new(StringComparer.OrdinalIgnoreCase);

    public void Init(GameState state)
    {
        _state = state;
    }

    public void Toggle()
    {
        Visible = !Visible;
    }

    public override void _Ready()
    {
        CustomMinimumSize = new Vector2(PanelW, PanelH);
        Size = new Vector2(PanelW, PanelH);
        MouseFilter = MouseFilterEnum.Ignore; // click-through
    }

    public override void _Process(double delta)
    {
        if (Visible)
            QueueRedraw();
    }

    public override void _Draw()
    {
        if (_state == null) return;

        // Background
        DrawRect(new Rect2(0, 0, PanelW, PanelH), BgColor);
        DrawRect(new Rect2(0, 0, PanelW, PanelH), BorderColor, false, 1f);

        // Map area
        float mapX = Padding;
        float mapY = Padding;

        // Draw map area border
        DrawRect(new Rect2(mapX - 1, mapY - 1, MapPixels + 2, MapPixels + 2),
            new Color(0.25f, 0.25f, 0.35f, 0.6f), false, 1f);

        // Scale: tile coords (1-100) → pixel coords within the map area
        float scale = MapPixels / (float)TileGrid;

        // Draw NPCs first (below players)
        foreach (var kv in _state.Characters)
        {
            var ch = kv.Value;
            if (ch.CharIndex == _state.UserCharIndex) continue;
            if (ch.NpcNumber <= 0) continue; // only NPCs here

            float px = mapX + (ch.PosX - 1) * scale;
            float py = mapY + (ch.PosY - 1) * scale;

            // NPCs with Criminal flag = hostile (red), others = friendly (yellow)
            Color npcColor = ch.Criminal ? NpcHostileColor : NpcFriendlyColor;
            DrawCircle(new Vector2(px, py), NpcMarkerRadius, npcColor);
        }

        // Draw other players
        string userGuild = _state.UserGuildName;
        foreach (var kv in _state.Characters)
        {
            var ch = kv.Value;
            if (ch.CharIndex == _state.UserCharIndex) continue;
            if (ch.NpcNumber > 0) continue; // skip NPCs

            float px = mapX + (ch.PosX - 1) * scale;
            float py = mapY + (ch.PosY - 1) * scale;

            // Determine color based on relationship
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

        // Draw self marker (on top of everything)
        {
            float px = mapX + (_state.UserPosX - 1) * scale;
            float py = mapY + (_state.UserPosY - 1) * scale;

            // Outer ring + filled center for visibility
            DrawCircle(new Vector2(px, py), SelfMarkerRadius + 1f, new Color(0, 0, 0, 0.6f));
            DrawCircle(new Vector2(px, py), SelfMarkerRadius, SelfColor);
        }

        // Coordinate text at bottom
        if (_state.Config.ShowMinimapPosition)
        {
            string coordText = $"{_state.CurrentMap} ({_state.UserPosX},{_state.UserPosY})";
            var font = ThemeDB.FallbackFont;
            int fontSize = 10;
            float textY = mapY + MapPixels + 4;
            float textW = font.GetStringSize(coordText, HorizontalAlignment.Left, -1, fontSize).X;
            float textX = Padding + (MapPixels - textW) / 2f;
            DrawString(font, new Vector2(textX, textY + fontSize), coordText, HorizontalAlignment.Left,
                -1, fontSize, CoordColor);
        }
    }

    /// <summary>
    /// Extract the base name (without clan tag) from a character name.
    /// VB6 format: "PlayerName&lt;ClanTag&gt;" — we want just "PlayerName".
    /// </summary>
    private static string ExtractBaseName(string name)
    {
        int ltIdx = name.IndexOf('<');
        return ltIdx >= 0 ? name[..ltIdx] : name;
    }

    /// <summary>
    /// Check if a character name contains the specified clan tag.
    /// Format: "Name&lt;ClanTag&gt;"
    /// </summary>
    private static bool HasClanTag(string name, string guildName)
    {
        int ltIdx = name.IndexOf('<');
        if (ltIdx < 0) return false;
        string tag = name[(ltIdx + 1)..];
        // Remove trailing '>' if present
        if (tag.EndsWith('>')) tag = tag[..^1];
        return string.Equals(tag, guildName, StringComparison.OrdinalIgnoreCase);
    }
}
