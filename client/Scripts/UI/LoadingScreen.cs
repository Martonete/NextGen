using Godot;
using System;
using ArgentumNextgen.Game;

namespace ArgentumNextgen.UI;

/// <summary>
/// Loading screen shown during map transitions and initial data load.
/// Uses RpgTheme for styled progress bar and labels.
/// Full-screen overlay — NOT an RpgBaseForm (no drag, no close button).
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
    private float _showTimer;

    private const float MinShowTime = 0.5f;
    private const float FadeSpeed = 2.5f;
    private const float BarSpeed = 3.0f;

    public void Init(GameState state)
    {
        _state = state;
    }

    public override void _Ready()
    {
        Visible = false;
        ZIndex = RpgBaseForm.ZLoading;

        // Full-screen dark background
        _background = new ColorRect();
        _background.Color = new Color(0, 0, 0, 1f);
        _background.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        _background.Size = new Vector2(ResolutionManager.WindowWidth, ResolutionManager.WindowHeight);
        _background.MouseFilter = MouseFilterEnum.Stop;
        AddChild(_background);

        // Optional background image
        _bgImage = new TextureRect();
        _bgImage.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        _bgImage.Size = new Vector2(ResolutionManager.WindowWidth, ResolutionManager.WindowHeight);
        _bgImage.StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered;
        _bgImage.MouseFilter = MouseFilterEnum.Ignore;
        _bgImage.Modulate = new Color(0.3f, 0.3f, 0.3f, 0.5f);
        AddChild(_bgImage);

        // Map name label (top center) — uses RpgTheme
        _mapNameLabel = RpgTheme.CreateTitleLabel("", 18);
        _mapNameLabel.Position = new Vector2(200, 220);
        _mapNameLabel.Size = new Vector2(400, 30);
        _mapNameLabel.MouseFilter = MouseFilterEnum.Ignore;
        AddChild(_mapNameLabel);

        // "Cargando..." label — uses RpgTheme
        _loadingLabel = RpgTheme.CreateInfoLabel("Cargando...", 14);
        _loadingLabel.Position = new Vector2(300, 280);
        _loadingLabel.Size = new Vector2(200, 24);
        _loadingLabel.HorizontalAlignment = HorizontalAlignment.Center;
        _loadingLabel.MouseFilter = MouseFilterEnum.Ignore;
        AddChild(_loadingLabel);

        // Progress bar — uses RpgTheme
        _progressBar = RpgTheme.CreateRpgProgressBar(400, 22,
            fillColor: new Color(0.6f, 0.5f, 0.2f),
            bgColor: new Color(0.15f, 0.15f, 0.2f));
        _progressBar.Position = new Vector2(200, 320);
        _progressBar.Size = new Vector2(400, 22);
        _progressBar.MouseFilter = MouseFilterEnum.Ignore;
        AddChild(_progressBar);
    }

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

    public void SetLabel(string text)
    {
        if (_loadingLabel != null) _loadingLabel.Text = text;
    }

    public void SetProgress(float value)
    {
        _targetProgress = Mathf.Clamp(value, 0f, 1f);
    }

    public void Complete()
    {
        _targetProgress = 1f;
        if (_loadingLabel != null) _loadingLabel.Text = "Listo!";
    }

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

        _progress = Mathf.MoveToward(_progress, _targetProgress, BarSpeed * dt);
        if (_progressBar != null) _progressBar.Value = _progress * 100f;

        if (!_fadingOut && _progress >= 0.99f && _showTimer >= MinShowTime)
        {
            _fadingOut = true;
        }

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

    public void SetBackgroundImage(Texture2D? texture)
    {
        if (_bgImage != null) _bgImage.Texture = texture;
    }
}
