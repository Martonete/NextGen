using Godot;
using System;
using ArgentumNextgen.Game;

namespace ArgentumNextgen.UI;

/// <summary>
/// Loading screen shown during map transitions and initial data load.
/// Displays a progress bar with "Cargando..." text, optional background image.
/// Fades out when the map is ready.
/// </summary>
public partial class LoadingScreen : Control
{
    private GameState? _state;

    // Controls
    private ColorRect? _background;
    private TextureRect? _bgImage;
    private Label? _loadingLabel;
    private ProgressBar? _progressBar;
    private Label? _mapNameLabel;

    // State
    private float _progress;
    private float _targetProgress;
    private bool _fadingOut;
    private float _fadeAlpha = 1f;
    private float _showTimer; // Minimum display time to avoid flicker

    private const float MinShowTime = 0.5f;  // Minimum seconds to show loading screen
    private const float FadeSpeed = 2.5f;    // Alpha units per second for fade out
    private const float BarSpeed = 3.0f;     // Progress bar fill speed (0-1 per second)

    public void Init(GameState state)
    {
        _state = state;
    }

    public override void _Ready()
    {
        Visible = false;
        ZIndex = 200; // Above everything

        // Full-screen dark background
        _background = new ColorRect();
        _background.Color = new Color(0, 0, 0, 1f);
        _background.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        _background.Size = new Vector2(800, 600);
        _background.MouseFilter = MouseFilterEnum.Stop;
        AddChild(_background);

        // Optional background image (placeholder — could load Principal.jpg)
        _bgImage = new TextureRect();
        _bgImage.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        _bgImage.Size = new Vector2(800, 600);
        _bgImage.StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered;
        _bgImage.MouseFilter = MouseFilterEnum.Ignore;
        _bgImage.Modulate = new Color(0.3f, 0.3f, 0.3f, 0.5f);
        AddChild(_bgImage);

        // Map name label (top center)
        _mapNameLabel = new Label();
        _mapNameLabel.Position = new Vector2(200, 220);
        _mapNameLabel.Size = new Vector2(400, 30);
        _mapNameLabel.HorizontalAlignment = HorizontalAlignment.Center;
        _mapNameLabel.AddThemeColorOverride("font_color", new Color(0.9f, 0.8f, 0.5f));
        _mapNameLabel.AddThemeFontSizeOverride("font_size", 16);
        _mapNameLabel.MouseFilter = MouseFilterEnum.Ignore;
        AddChild(_mapNameLabel);

        // "Cargando..." label
        _loadingLabel = new Label();
        _loadingLabel.Position = new Vector2(300, 280);
        _loadingLabel.Size = new Vector2(200, 24);
        _loadingLabel.HorizontalAlignment = HorizontalAlignment.Center;
        _loadingLabel.Text = "Cargando...";
        _loadingLabel.AddThemeColorOverride("font_color", Colors.White);
        _loadingLabel.AddThemeFontSizeOverride("font_size", 14);
        _loadingLabel.MouseFilter = MouseFilterEnum.Ignore;
        AddChild(_loadingLabel);

        // Progress bar
        _progressBar = new ProgressBar();
        _progressBar.Position = new Vector2(200, 320);
        _progressBar.Size = new Vector2(400, 20);
        _progressBar.MinValue = 0;
        _progressBar.MaxValue = 100;
        _progressBar.Value = 0;
        _progressBar.ShowPercentage = false;
        _progressBar.MouseFilter = MouseFilterEnum.Ignore;

        var barBg = new StyleBoxFlat();
        barBg.BgColor = new Color(0.15f, 0.15f, 0.2f);
        barBg.SetCornerRadiusAll(4);
        _progressBar.AddThemeStyleboxOverride("background", barBg);

        var barFill = new StyleBoxFlat();
        barFill.BgColor = new Color(0.6f, 0.5f, 0.2f);
        barFill.SetCornerRadiusAll(4);
        _progressBar.AddThemeStyleboxOverride("fill", barFill);

        AddChild(_progressBar);
    }

    /// <summary>
    /// Show the loading screen with optional map name.
    /// </summary>
    public void Show(string mapName = "")
    {
        _progress = 0f;
        _targetProgress = 0f;
        _fadingOut = false;
        _fadeAlpha = 1f;
        _showTimer = 0f;
        Visible = true;

        if (_mapNameLabel != null)
            _mapNameLabel.Text = string.IsNullOrEmpty(mapName) ? "" : mapName;
        if (_progressBar != null) _progressBar.Value = 0;
        if (_background != null) _background.Color = new Color(0, 0, 0, 1f);
        if (_loadingLabel != null) _loadingLabel.Text = "Cargando...";
    }

    /// <summary>
    /// Set progress value (0.0 to 1.0). The bar animates smoothly toward this value.
    /// </summary>
    public void SetProgress(float value)
    {
        _targetProgress = Mathf.Clamp(value, 0f, 1f);
    }

    /// <summary>
    /// Signal that loading is complete. Triggers fade-out after minimum display time.
    /// </summary>
    public void Complete()
    {
        _targetProgress = 1f;
        if (_loadingLabel != null) _loadingLabel.Text = "Listo!";
    }

    /// <summary>
    /// Immediately hide the loading screen (for forced transitions).
    /// </summary>
    public void ForceHide()
    {
        Visible = false;
        _fadingOut = false;
    }

    public override void _Process(double delta)
    {
        if (!Visible) return;
        float dt = (float)delta;

        _showTimer += dt;

        // Animate progress bar
        _progress = Mathf.MoveToward(_progress, _targetProgress, BarSpeed * dt);
        if (_progressBar != null) _progressBar.Value = _progress * 100f;

        // Start fade-out when progress reaches 1.0 and minimum time elapsed
        if (!_fadingOut && _progress >= 0.99f && _showTimer >= MinShowTime)
        {
            _fadingOut = true;
        }

        // Fade out
        if (_fadingOut)
        {
            _fadeAlpha = Mathf.MoveToward(_fadeAlpha, 0f, FadeSpeed * dt);
            if (_background != null) _background.Color = new Color(0, 0, 0, _fadeAlpha);
            if (_loadingLabel != null) _loadingLabel.Modulate = new Color(1, 1, 1, _fadeAlpha);
            if (_progressBar != null) _progressBar.Modulate = new Color(1, 1, 1, _fadeAlpha);
            if (_mapNameLabel != null) _mapNameLabel.Modulate = new Color(1, 1, 1, _fadeAlpha);

            if (_fadeAlpha <= 0.01f)
            {
                Visible = false;
                _fadingOut = false;
            }
        }
    }

    /// <summary>
    /// Set the background image texture (e.g., Principal.jpg).
    /// </summary>
    public void SetBackgroundImage(Texture2D? texture)
    {
        if (_bgImage != null) _bgImage.Texture = texture;
    }
}
