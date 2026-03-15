using Godot;
using ArgentumNextgen.Game;

namespace ArgentumNextgen.UI;

/// <summary>
/// Draws the game HUD frame using RpgTheme assets.
/// big_bar.png stretched as the ENTIRE 800x600 frame.
/// Dark insets ONLY where we need black backgrounds (console, viewport).
/// Sidebar content (labels, buttons, stat bars) sits directly on big_bar.
/// An overlay copy of big_bar.png draws ON TOP of everything (high ZIndex)
/// so the frame borders always appear above game content.
/// </summary>
public partial class GameHudFrame : Control
{
    /// <summary>
    /// Overlay frame that draws on top of all game UI. Added to parent by SetupGamePanels.
    /// </summary>
    public Control? FrameOverlay { get; private set; }

    public override void _Ready()
    {
        Position = Vector2.Zero;
        Size = new Vector2(ResolutionManager.WindowWidth, ResolutionManager.WindowHeight);
        MouseFilter = MouseFilterEnum.Ignore;

        // === BACKGROUND: big_bar_bg.png (fills behind content) ===
        var bgFrame = new TextureRect();
        bgFrame.Texture = RpgTheme.GetTex("big_bar_bg.png");
        bgFrame.ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize;
        bgFrame.StretchMode = TextureRect.StretchModeEnum.Scale;
        bgFrame.Position = Vector2.Zero;
        bgFrame.Size = new Vector2(ResolutionManager.WindowWidth, ResolutionManager.WindowHeight);
        bgFrame.MouseFilter = MouseFilterEnum.Ignore;
        AddChild(bgFrame);

        // === DARK INSETS — only where we need black behind content ===

        // Console area — semi-transparent with border (extended up for minimap breathing room)
        AddStyledInset(ResolutionManager.S(18), ResolutionManager.S(14), ResolutionManager.S(536), ResolutionManager.S(128));

        // Game viewport — the world renders here (13,149 to 557,565)
        AddDarkInset(ResolutionManager.S(13), ResolutionManager.S(149), ResolutionManager.S(544), ResolutionManager.S(416));

        // === OVERLAY: big_bar_frame.png (borders only) on top of everything ===
        FrameOverlay = new Control();
        FrameOverlay.Position = Vector2.Zero;
        FrameOverlay.Size = new Vector2(ResolutionManager.WindowWidth, ResolutionManager.WindowHeight);
        FrameOverlay.MouseFilter = MouseFilterEnum.Ignore;
        FrameOverlay.ZIndex = 50;

        var overlayTex = new TextureRect();
        overlayTex.Texture = RpgTheme.GetTex("big_bar_frame.png");
        overlayTex.ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize;
        overlayTex.StretchMode = TextureRect.StretchModeEnum.Scale;
        overlayTex.Position = Vector2.Zero;
        overlayTex.Size = new Vector2(ResolutionManager.WindowWidth, ResolutionManager.WindowHeight);
        overlayTex.MouseFilter = MouseFilterEnum.Ignore;
        FrameOverlay.AddChild(overlayTex);
    }

    private void AddDarkInset(float x, float y, float w, float h)
    {
        var rect = new ColorRect();
        rect.Color = new Color(0f, 0f, 0f, 0.9f);
        rect.Position = new Vector2(x, y);
        rect.Size = new Vector2(w, h);
        rect.MouseFilter = MouseFilterEnum.Ignore;
        AddChild(rect);
    }

    private void AddStyledInset(float x, float y, float w, float h)
    {
        var panel = new Panel();
        panel.Position = new Vector2(x, y);
        panel.Size = new Vector2(w, h);
        panel.MouseFilter = MouseFilterEnum.Ignore;
        var style = new StyleBoxFlat();
        style.BgColor = new Color(0f, 0f, 0f, 0.55f);
        style.BorderColor = new Color(0.4f, 0.33f, 0.2f, 0.6f);
        style.SetBorderWidthAll(1);
        style.SetCornerRadiusAll(2);
        panel.AddThemeStyleboxOverride("panel", style);
        AddChild(panel);
    }
}
