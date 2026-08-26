using Godot;
using System;
using ArgentumNextgen.Game;

namespace ArgentumNextgen.UI;

/// <summary>
/// Loading screen shown during map transitions and initial data load.
/// Full-screen overlay — NOT an RpgBaseForm (no drag, no close button).
///
/// The artwork already carries the title, the "Cargando..." caption and the
/// frame around the progress bar, so nothing here redraws them: the only live
/// element is the fill inside that frame, positioned by the fractions below.
/// </summary>
public partial class LoadingScreen : Control
{
    private GameState? _state;

    // Controls
    private ColorRect? _background;
    private TextureRect? _bgImage;
    private Label? _loadingLabel;
    private ColorRect? _barFill;
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

    private const string BackdropFile = "loading_screen.jpg";

    // Interior of the bar frame painted into the artwork, as fractions of the
    // image. Measured off the art itself, so the fill lands inside the frame
    // at any window size instead of being positioned by eye.
    private const float BarFracX = 0.2762f;
    private const float BarFracY = 0.7969f;
    private const float BarFracW = 0.4477f;
    private const float BarFracH = 0.0723f;

    /// <summary>Inset so the fill sits inside the frame's bevel, not over it.</summary>
    private const float BarInset = 3f;

    private static readonly Color BarFillColor = new(0.36f, 0.72f, 1f, 0.92f);

    /// <summary>Where the caption goes when the artwork is missing.</summary>
    private const float FallbackLabelFracY = 0.72f;

    private bool _hasBackdrop;

    public void Init(GameState state)
    {
        _state = state;
    }

    public override void _Ready()
    {
        Visible = false;
        ZIndex = RpgBaseForm.ZLoading;

        int winW = ResolutionManager.WindowWidth;
        int winH = ResolutionManager.WindowHeight;

        // Opaque backing: whatever the artwork does not cover stays black
        // rather than showing the game behind.
        _background = new ColorRect();
        _background.Color = new Color(0, 0, 0, 1f);
        _background.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        _background.Size = new Vector2(winW, winH);
        _background.MouseFilter = MouseFilterEnum.Stop;
        AddChild(_background);

        _bgImage = new TextureRect();
        _bgImage.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        _bgImage.Size = new Vector2(winW, winH);
        // Cover the window without distorting the art; the edges crop instead.
        _bgImage.StretchMode = TextureRect.StretchModeEnum.KeepAspectCovered;
        _bgImage.MouseFilter = MouseFilterEnum.Ignore;
        AddChild(_bgImage);

        LoadBackdrop();

        var barRect = BarRect(winW, winH);

        // Progress fill only — the frame around it is part of the artwork.
        _barFill = new ColorRect();
        _barFill.Color = BarFillColor;
        _barFill.Position = barRect.Position;
        _barFill.Size = new Vector2(0, barRect.Size.Y);
        _barFill.MouseFilter = MouseFilterEnum.Ignore;
        AddChild(_barFill);

        // Status text, under the bar so it never covers the painted caption.
        _loadingLabel = RpgTheme.CreateInfoLabel("", ResolutionManager.S(13));
        _loadingLabel.Position = new Vector2(barRect.Position.X,
                                             barRect.End.Y + ResolutionManager.S(8));
        _loadingLabel.Size = new Vector2(barRect.Size.X, ResolutionManager.S(22));
        _loadingLabel.HorizontalAlignment = HorizontalAlignment.Center;
        _loadingLabel.MouseFilter = MouseFilterEnum.Ignore;
        AddChild(_loadingLabel);

        // Map name, above the bar. Blank on the startup pass.
        _mapNameLabel = RpgTheme.CreateTitleLabel("", ResolutionManager.S(17));
        _mapNameLabel.Position = new Vector2(barRect.Position.X,
                                             barRect.Position.Y - ResolutionManager.S(38));
        _mapNameLabel.Size = new Vector2(barRect.Size.X, ResolutionManager.S(28));
        _mapNameLabel.HorizontalAlignment = HorizontalAlignment.Center;
        _mapNameLabel.MouseFilter = MouseFilterEnum.Ignore;
        AddChild(_mapNameLabel);
    }

    private void LoadBackdrop()
    {
        try
        {
            var tex = RpgTheme.GetTex(BackdropFile);
            if (tex != null && tex.GetWidth() > 1)
            {
                if (_bgImage != null) _bgImage.Texture = tex;
                _hasBackdrop = true;
                return;
            }
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[LoadingScreen] No se pudo cargar {BackdropFile}: {ex.Message}");
        }

        // No artwork: the plain bar on black still has to be readable.
        _hasBackdrop = false;
    }

    /// <summary>
    /// Where the progress fill goes. With the artwork on screen it tracks the
    /// painted frame — including the crop KeepAspectCovered applies, or the
    /// bar would drift off the frame on windows of a different aspect ratio.
    /// </summary>
    private Rect2 BarRect(int winW, int winH)
    {
        if (!_hasBackdrop || _bgImage?.Texture == null)
        {
            float w = winW * 0.42f;
            return new Rect2((winW - w) / 2f, winH * FallbackLabelFracY, w, ResolutionManager.S(22));
        }

        var tex = _bgImage.Texture;
        float texW = tex.GetWidth();
        float texH = tex.GetHeight();

        // KeepAspectCovered scales by the larger ratio and centres the overflow.
        float scale = Math.Max(winW / texW, winH / texH);
        float drawW = texW * scale;
        float drawH = texH * scale;
        float originX = (winW - drawW) / 2f;
        float originY = (winH - drawH) / 2f;

        return new Rect2(
            originX + drawW * BarFracX + BarInset,
            originY + drawH * BarFracY + BarInset,
            drawW * BarFracW - BarInset * 2f,
            drawH * BarFracH - BarInset * 2f);
    }

    public void Show(string mapName = "")
    {
        _progress = 0f;
        _targetProgress = 0f;
        _fadingOut = false;
        _fadeAlpha = 1f;
        _showTimer = 0f;
        Visible = true;

        // Reset the fade so a second load is not stuck at the last alpha.
        Modulate = Colors.White;

        if (_mapNameLabel != null)
            _mapNameLabel.Text = string.IsNullOrEmpty(mapName) ? "" : mapName;
        if (_background != null) _background.Color = new Color(0, 0, 0, 1f);
        if (_loadingLabel != null) _loadingLabel.Text = "";
        if (_barFill != null) _barFill.Size = new Vector2(0, _barFill.Size.Y);
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

        if (_barFill != null)
        {
            var rect = BarRect(ResolutionManager.WindowWidth, ResolutionManager.WindowHeight);
            _barFill.Position = rect.Position;
            _barFill.Size = new Vector2(rect.Size.X * _progress, rect.Size.Y);
        }

        if (!_fadingOut && _progress >= 0.99f && _showTimer >= MinShowTime)
            _fadingOut = true;

        if (_fadingOut)
        {
            _fadeAlpha = Mathf.MoveToward(_fadeAlpha, 0f, FadeSpeed * dt);
            // Fade the whole screen at once: fading each child separately let
            // the backdrop linger behind labels that had already gone.
            Modulate = new Color(1, 1, 1, _fadeAlpha);

            if (_fadeAlpha <= 0.01f)
            {
                Visible = false;
                _fadingOut = false;
                Modulate = Colors.White;
            }
        }
    }

    public void SetBackgroundImage(Texture2D? texture)
    {
        if (_bgImage != null && texture != null)
        {
            _bgImage.Texture = texture;
            _hasBackdrop = true;
        }
    }
}
