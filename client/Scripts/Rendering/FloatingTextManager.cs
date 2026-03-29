using System;
using System.Collections.Generic;
using Godot;
using ArgentumNextgen.Game;

namespace ArgentumNextgen.Rendering;

/// <summary>
/// Floating damage/heal numbers that rise and fade above characters.
/// VB6: damage numbers appear above the character head and float upward.
/// </summary>
public class FloatingText
{
    public int CharIndex;    // character this is attached to (-1 for user pos)
    public string Text;
    public Color Color;
    public float Timer;      // seconds elapsed
    public float Duration;   // total lifetime in seconds
    public float RisePixels; // total pixels to rise
    public float OffsetY;    // extra Y offset (stacks multiple texts)

    public FloatingText(int charIndex, string text, Color color, float duration = 1.5f, float risePixels = 40f, float offsetY = 0f)
    {
        CharIndex = charIndex;
        Text = text;
        Color = color;
        Timer = 0f;
        Duration = duration;
        RisePixels = risePixels;
        OffsetY = offsetY;
    }
}

/// <summary>
/// Manages and renders floating text (damage numbers, heals, misses).
/// Drawn as a child layer of WorldRenderer, above characters (z=1, same as dialogs).
/// </summary>
public partial class FloatingTextLayer : Node2D
{
    private WorldRenderer? _renderer;
    private GameState? _state;
    private readonly List<FloatingText> _texts = new();
    private Font? _font;
    private const int FontSize = 14;
    // Stacking: track active text count per character to offset vertically
    private readonly Dictionary<int, int> _recentCountPerChar = new();

    public void Init(WorldRenderer renderer, GameState state)
    {
        _renderer = renderer;
        _state = state;
        // Use default Godot font
        _font = ThemeDB.FallbackFont;
    }

    /// <summary>
    /// Spawn a floating text above a character.
    /// </summary>
    public void AddText(int charIndex, string text, Color color)
    {
        _recentCountPerChar.TryGetValue(charIndex, out int stack);
        float offsetY = stack * 16f;
        _recentCountPerChar[charIndex] = stack + 1;

        _texts.Add(new FloatingText(charIndex, text, color, 1.5f, 40f, offsetY));
    }

    /// <summary>
    /// Convenience: red damage received on user char.
    /// </summary>
    public void AddDamageReceived(int damage)
    {
        if (_state == null) return;
        AddText(_state.UserCharIndex, $"-{damage}", new Color(1f, 0.2f, 0.2f));
    }

    /// <summary>
    /// Convenience: white/yellow damage dealt on target.
    /// </summary>
    public void AddDamageDealt(int charIndex, int damage)
    {
        AddText(charIndex, $"-{damage}", new Color(1f, 1f, 0.4f));
    }

    /// <summary>
    /// Convenience: green healing number on a character.
    /// </summary>
    public void AddHeal(int charIndex, int amount)
    {
        AddText(charIndex, $"+{amount}", new Color(0.3f, 1f, 0.3f));
    }

    /// <summary>
    /// Convenience: miss text (white, smaller).
    /// </summary>
    public void AddMiss(int charIndex, string text = "Fallo!")
    {
        AddText(charIndex, text, new Color(0.8f, 0.8f, 0.8f));
    }

    public override void _Process(double delta)
    {
        float dt = (float)delta;

        // Update and remove expired texts
        for (int i = _texts.Count - 1; i >= 0; i--)
        {
            _texts[i].Timer += dt;
            if (_texts[i].Timer >= _texts[i].Duration)
            {
                int ci = _texts[i].CharIndex;
                if (_recentCountPerChar.TryGetValue(ci, out int cnt) && cnt > 1)
                    _recentCountPerChar[ci] = cnt - 1;
                else
                    _recentCountPerChar.Remove(ci);
                _texts.RemoveAt(i);
            }
        }

        if (_texts.Count > 0)
            QueueRedraw();
    }

    public override void _Draw()
    {
        if (_state == null || _renderer == null || _font == null) return;
        if (_texts.Count == 0) return;

        var cam = WorldRenderer.CurrentCamera;
        float userX = cam.UserX;
        float userY = cam.UserY;
        float pixelOffsetX = cam.PixelOffsetX;
        float pixelOffsetY = cam.PixelOffsetY;

        foreach (var ft in _texts)
        {
            // Find character position
            if (!_state.Characters.TryGetValue(ft.CharIndex, out var ch))
                continue;

            // Calculate screen position (same math as WorldRenderer TileToScreen)
            // TileToScreen: px = (tileX - userX + HalfTilesX) * TileSize + pixelOffset
            float screenX = (ch.PosX - userX + ResolutionManager.HalfTilesX) * 32f + pixelOffsetX + (float)System.Math.Round(ch.MoveOffsetX);
            float screenY = (ch.PosY - userY + ResolutionManager.HalfTilesY) * 32f + pixelOffsetY + (float)System.Math.Round(ch.MoveOffsetY);

            // Position above head (approximate head offset)
            float headY = screenY - 45f;

            // Animate: rise upward and fade out
            float progress = ft.Timer / ft.Duration; // 0..1
            float rise = ft.RisePixels * progress;
            float alpha = 1f - progress; // linear fade
            // Ease: slow down the rise over time
            rise = ft.RisePixels * (1f - (1f - progress) * (1f - progress));

            float finalY = headY - rise - ft.OffsetY;
            float finalX = screenX;

            // Draw text with outline for readability
            var textColor = new Color(ft.Color.R, ft.Color.G, ft.Color.B, alpha);
            var outlineColor = new Color(0f, 0f, 0f, alpha * 0.8f);
            var pos = new Vector2(finalX, finalY);

            // Center the text horizontally
            var textSize = _font.GetStringSize(ft.Text, HorizontalAlignment.Center, -1, FontSize);
            pos.X -= textSize.X * 0.5f;

            // Outline (draw text 4 times offset by 1px in each direction)
            DrawString(_font, pos + new Vector2(-1, 0), ft.Text, HorizontalAlignment.Left, -1, FontSize, outlineColor);
            DrawString(_font, pos + new Vector2(1, 0), ft.Text, HorizontalAlignment.Left, -1, FontSize, outlineColor);
            DrawString(_font, pos + new Vector2(0, -1), ft.Text, HorizontalAlignment.Left, -1, FontSize, outlineColor);
            DrawString(_font, pos + new Vector2(0, 1), ft.Text, HorizontalAlignment.Left, -1, FontSize, outlineColor);
            // Main text
            DrawString(_font, pos, ft.Text, HorizontalAlignment.Left, -1, FontSize, textColor);
        }
    }
}
