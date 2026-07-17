using Godot;
using ArgentumNextgen.Game;

namespace ArgentumNextgen.Rendering;

/// <summary>
/// Draws an animated, textured fog over tiles outside the core 17x13 viewport,
/// using vision_fog.gdshader: a static mask (transparent core, thin gradient at
/// the boundary, opaque outside) modulated by drifting noise so the fog reads
/// as real mist instead of a flat color tint.
/// Only active when resolution > 800x600.
/// The mask texture is rebuilt on resolution change (event-driven, not per-frame);
/// intensity changes just update the shader's max_alpha uniform (no rebuild needed).
/// </summary>
public partial class FogOverlayLayer : Node2D
{
    // Dark gray-blue fog tint: reads as gloom/murk closing in rather than a pale
    // haze, and doesn't fight the day/tarde/noche tints either.
    private static readonly Color FogColor = new Color(0.13f, 0.15f, 0.19f, 1f);
    // Transition width in design pixels (~1 tile = 32px, at 1/4 res = 8 texels)
    private const float TransitionPx = 32f;

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
            var noise = new FastNoiseLite { Seed = 7, FractalOctaves = 3 };
            var noiseTexture = new NoiseTexture2D
            {
                Noise = noise,
                Seamless = true,
                Width = 256,
                Height = 256,
            };
            _material = new ShaderMaterial { Shader = shader };
            _material.SetShaderParameter("noise_texture", noiseTexture);
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
        // Dark and dense at 100%: up to ~0.75 alpha so the terrain is barely
        // legible through it, per the requested "bastante oscuro" look.
        _material?.SetShaderParameter("max_alpha", (intensity / 100f) * 0.75f);
        QueueRedraw();
    }

    public override void _Draw()
    {
        int extraX = ResolutionManager.ExtraTilesX;
        int extraY = ResolutionManager.ExtraTilesY;
        if (extraX <= 0 && extraY <= 0) return;
        if (_maskTex == null) return;

        DrawTextureRect(_maskTex, new Rect2(0, 0, _cachedVpW, _cachedVpH), false);
    }

    /// <summary>
    /// Build the fog MASK only (red channel = 0..1 shape): transparent inside
    /// core, thin gradient at core edge, then flat 1.0 everywhere else. The
    /// actual color/alpha/noise is applied by vision_fog.gdshader at draw time.
    /// Built at 1/4 resolution.
    /// </summary>
    private static ImageTexture BuildFogMask(int vpW, int vpH)
    {
        int texW = vpW / 4;
        int texH = vpH / 4;
        int coreW = 544 / 4;  // 136
        int coreH = 416 / 4;  // 104
        float centerX = texW / 2f;
        float centerY = texH / 2f;
        float halfCoreW = coreW / 2f;
        float halfCoreH = coreH / 2f;
        // Transition width in texture pixels (1/4 of design pixels)
        float transition = TransitionPx / 4f;

        var img = Image.CreateEmpty(texW, texH, false, Image.Format.Rgba8);

        for (int py = 0; py < texH; py++)
        {
            for (int px = 0; px < texW; px++)
            {
                // Distance from the core edge (0 = inside core, >0 = outside)
                float dx = System.Math.Max(0, System.Math.Abs(px - centerX) - halfCoreW);
                float dy = System.Math.Max(0, System.Math.Abs(py - centerY) - halfCoreH);
                float dist = System.Math.Max(dx, dy);

                float mask;
                if (dist <= 0f)
                {
                    mask = 0f; // inside core: no fog
                }
                else if (dist < transition)
                {
                    float t = dist / transition;
                    t = t * t * (3f - 2f * t); // smooth-step
                    mask = t;
                }
                else
                {
                    mask = 1f; // beyond transition: full fog
                }

                img.SetPixel(px, py, new Color(mask, mask, mask, 1f));
            }
        }

        return ImageTexture.CreateFromImage(img);
    }
}
