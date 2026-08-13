using Godot;
using ArgentumNextgen.Game;

namespace ArgentumNextgen.Rendering;

/// <summary>
/// Draws the peripheral darkness used by the AO Libre game screen.  It keeps a
/// transparent central rectangle and fades smoothly to near-black at the
/// extended viewport edges. The rectangle follows the reference shader's UV
/// values and is scaled to the active viewport without changing resolution.
/// The mask texture is rebuilt on resolution change (event-driven, not per-frame);
/// intensity changes just update the shader's max_alpha uniform (no rebuild needed).
/// </summary>
public partial class FogOverlayLayer : Node2D
{
    // Same cold near-black used by the reference peripheral_fog shader.
    private static readonly Color FogColor = new Color(0.02f, 0.03f, 0.05f, 1f);
    private const int MaskScale = 2;

    private GameState? _state;
    private ImageTexture? _maskTex;
    private ShaderMaterial? _material;
    private int _cachedVpW, _cachedVpH;

    /// <summary>Wire the game state so the fog can read the player's configured intensity.</summary>
    public void Init(GameState state)
    {
        _state = state;
    }

    public override void _Ready()
    {
        var shader = GD.Load<Shader>("res://Shaders/vision_fog.gdshader");
        GD.Print($"[FOG] shader loaded: {shader != null}");
        if (shader != null)
        {
            _material = new ShaderMaterial { Shader = shader };
            _material.SetShaderParameter("fog_color", FogColor);
            Material = _material;
        }
        else
        {
            GD.PrintErr("[FOG] vision_fog.gdshader failed to load — fog overlay disabled to avoid corrupting the render.");
        }

        RebuildFogTexture();
        ResolutionManager.OnResolutionChanged += RebuildFogTexture;
    }

    public override void _ExitTree()
    {
        ResolutionManager.OnResolutionChanged -= RebuildFogTexture;
    }

    /// <summary>
    /// Rebuild the mask texture for the current viewport size, and refresh the
    /// shader's intensity uniform. Called on init, on resolution change, and
    /// whenever the fog intensity option changes (not per-frame).
    /// </summary>
    public void RebuildFogTexture()
    {
        int vpW = ResolutionManager.ViewportW;
        int vpH = ResolutionManager.ViewportH;
        if (vpW != _cachedVpW || vpH != _cachedVpH || _maskTex == null)
        {
            _maskTex = BuildFogMask(vpW, vpH);
            _material?.SetShaderParameter("fog_mask", _maskTex);
            _cachedVpW = vpW;
            _cachedVpH = vpH;
        }

        int intensity = _state?.Config?.FogIntensity ?? 30;
        // Keep the video option as the final opacity control. Unlike the old
        // texture-noise version this maps directly to the reference shader's
        // fog_intensity uniform.
        float t = System.Math.Clamp(intensity / 100f, 0f, 1f);
        _material?.SetShaderParameter("max_alpha", t);
        QueueRedraw();
    }

    public override void _Draw()
    {
        // Eternal/VB6 has no peripheral visibility mask in its full RenderScreen.
        if (ResolutionManager.FullscreenWorld) return;
        if (VisionRange.FogWidth >= _cachedVpW && VisionRange.FogHeight >= _cachedVpH) return;
        if (_maskTex == null) return;

        DrawTextureRect(_maskTex, new Rect2(0, 0, _cachedVpW, _cachedVpH), false);
    }

    /// <summary>
    /// Build the fog MASK only (red channel = 0..1 shape). This is the same
    /// rounded-distance calculation as peripheral_fog.gdshader in AO Libre:
    /// smoothstep(0, 0.15, distance outside the central rectangle).
    /// Built at half resolution.
    /// </summary>
    private static ImageTexture BuildFogMask(int vpW, int vpH)
    {
        int texW = System.Math.Max(1, vpW / MaskScale);
        int texH = System.Math.Max(1, vpH / MaskScale);
        int coreW = VisionRange.FogWidth / MaskScale;
        int coreH = VisionRange.FogHeight / MaskScale;
        float centerX = texW / 2f;
        float centerY = texH / 2f;
        float halfCoreW = coreW / 2f;
        float halfCoreH = coreH / 2f;

        var img = Image.CreateEmpty(texW, texH, false, Image.Format.Rgba8);

        for (int py = 0; py < texH; py++)
        {
            for (int px = 0; px < texW; px++)
            {
                // Coordinates are normalized independently per axis, exactly
                // as Godot's UV values are in the source shader.
                float dx = System.Math.Max(0, System.Math.Abs(px - centerX) / texW - halfCoreW / texW);
                float dy = System.Math.Max(0, System.Math.Abs(py - centerY) / texH - halfCoreH / texH);
                float distance = System.MathF.Sqrt(dx * dx + dy * dy);
                float t = System.Math.Clamp(distance / 0.15f, 0f, 1f);
                float mask = t * t * (3f - 2f * t); // GLSL smoothstep(0, .15, distance)

                img.SetPixel(px, py, new Color(mask, mask, mask, 1f));
            }
        }

        return ImageTexture.CreateFromImage(img);
    }
}
