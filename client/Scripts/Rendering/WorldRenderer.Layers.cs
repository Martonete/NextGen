using System;
using Godot;
using ArgentumNextgen.Game;

namespace ArgentumNextgen.Rendering;

/// <summary>
/// Child Node2D for character body reflections. Draws AFTER ReflectedAuraLayer
/// so auras appear behind the reflected body (same order as normal rendering).
/// </summary>
public partial class ReflectionBodyLayer : Node2D
{
    private WorldRenderer? _renderer;

    public void SetRenderer(WorldRenderer renderer)
    {
        _renderer = renderer;
    }

    public override void _Draw()
    {
        _renderer?.DrawReflectionBodies(this);
    }
}

/// <summary>
/// Child Node2D with additive blend material. Draws reflected auras.
/// Draws BEFORE ReflectionBodyLayer so auras are behind the body.
/// </summary>
public partial class ReflectedAuraLayer : Node2D
{
    private WorldRenderer? _renderer;

    public void SetRenderer(WorldRenderer renderer)
    {
        _renderer = renderer;
    }

    public override void _Draw()
    {
        _renderer?.DrawPendingReflAuras(this);
    }
}

/// <summary>
/// Child Node2D for PASS 1b: redraws non-water L1 tiles to mask reflection
/// and reflected aura overflow onto land.
/// z_index=-2, draws AFTER reflected auras but BEFORE L2.
/// </summary>
public partial class NonWaterMaskLayer : Node2D
{
    private WorldRenderer? _renderer;

    public void SetRenderer(WorldRenderer renderer)
    {
        _renderer = renderer;
    }

    public override void _Draw()
    {
        _renderer?.DrawNonWaterMask(this);
    }
}

/// <summary>
/// Child Node2D for PASS 2: draws Layer 2 tiles (borders, objects).
/// z_index=-1, draws AFTER mask but BEFORE normal auras.
/// Covers reflected aura portions under border opaque areas.
/// </summary>
public partial class Layer2Layer : Node2D
{
    private WorldRenderer? _renderer;

    public void SetRenderer(WorldRenderer renderer)
    {
        _renderer = renderer;
    }

    public override void _Draw()
    {
        _renderer?.DrawLayer2(this);
    }
}

/// <summary>
/// Child Node2D with additive blend material. Draws normal auras queued by WorldRenderer.
/// z_index=0, added before ContentLayer — same-z children draw in tree order.
/// </summary>
public partial class AuraAdditiveLayer : Node2D
{
    private WorldRenderer? _renderer;

    public void SetRenderer(WorldRenderer renderer)
    {
        _renderer = renderer;
    }

    public override void _Process(double delta)
    {
        QueueRedraw();
    }

    public override void _Draw()
    {
        _renderer?.DrawPendingAuras(this);
    }
}

/// <summary>
/// Child Node2D for PASS 3 content: ground objects, characters, layer 3, status.
/// z_index=0 — draws characters + ground objects + layer 3. Added after AuraLayer.
/// </summary>
public partial class ContentLayer : Node2D
{
    private WorldRenderer? _renderer;

    public void SetRenderer(WorldRenderer renderer)
    {
        _renderer = renderer;
    }

    public override void _Draw()
    {
        _renderer?.DrawContent(this);
    }
}

/// <summary>
/// Child Node2D for dialog text overlay. z_index=1 — above characters/NPCs,
/// below particles (z=2) and roof (z=3). Ensures dialog bubbles are always readable.
/// </summary>
public partial class DialogOverlayLayer : Node2D
{
    private WorldRenderer? _renderer;

    public void SetRenderer(WorldRenderer renderer)
    {
        _renderer = renderer;
    }

    public override void _Draw()
    {
        _renderer?.DrawPendingDialogs(this);
    }
}

/// <summary>
/// Child Node2D that draws Layer 4 (roof) AFTER particle layer.
/// z_index=3.
/// </summary>
public partial class RoofLayer : Node2D
{
    private WorldRenderer? _renderer;

    public void SetRenderer(WorldRenderer renderer)
    {
        _renderer = renderer;
    }

    public override void _Draw()
    {
        _renderer?.DrawPendingRoof(this);
    }
}

/// <summary>
/// Child Node2D with additive blend material. Draws particle sprites.
/// z_index=2.
/// </summary>
public partial class AdditiveParticleLayer : Node2D
{
    private WorldRenderer? _renderer;

    public void SetRenderer(WorldRenderer renderer)
    {
        _renderer = renderer;
    }

    public override void _Draw()
    {
        _renderer?.DrawPendingParticles(this);
    }
}

/// <summary>
/// Fog overlay layer: draws a radial gradient that darkens tiles outside the core
/// 17x13 viewport area. Only active when resolution > 800x600 (ExtraTilesX/Y > 0).
/// Uses a pre-built ImageTexture at 1/4 resolution (GPU bilinear upscale).
/// z_index=4 — above roof, below floating text.
/// </summary>
public partial class FogOverlayLayer : Node2D
{
    private ImageTexture? _fogTexture;
    private int _lastExtraX = -1;
    private int _lastExtraY = -1;
    private int _lastVpW;
    private int _lastVpH;

    public override void _Process(double delta)
    {
        QueueRedraw();
    }

    public override void _Draw()
    {
        int extraX = ResolutionManager.ExtraTilesX;
        int extraY = ResolutionManager.ExtraTilesY;
        if (extraX <= 0 && extraY <= 0) return;

        int vpW = ResolutionManager.ViewportPixelW;
        int vpH = ResolutionManager.ViewportPixelH;

        // Rebuild fog texture if resolution changed
        if (_fogTexture == null || extraX != _lastExtraX || extraY != _lastExtraY
            || vpW != _lastVpW || vpH != _lastVpH)
        {
            _fogTexture = BuildFogTexture(vpW, vpH, extraX, extraY);
            _lastExtraX = extraX;
            _lastExtraY = extraY;
            _lastVpW = vpW;
            _lastVpH = vpH;
        }

        // Draw the fog texture covering the full SubViewport
        DrawTexture(_fogTexture, Vector2.Zero);
    }

    /// <summary>
    /// Build a fog texture at 1/4 resolution. The center (core 17x13 area) is transparent.
    /// Outside the core, alpha increases with distance using an ease-in curve (f*f*0.75).
    /// </summary>
    private static ImageTexture BuildFogTexture(int vpW, int vpH, int extraX, int extraY)
    {
        // Quarter resolution for performance — GPU bilinear filtering smooths it
        int texW = vpW / 4;
        int texH = vpH / 4;
        if (texW < 1) texW = 1;
        if (texH < 1) texH = 1;

        var img = Image.CreateEmpty(texW, texH, false, Image.Format.Rgba8);

        // Core area bounds in full pixels
        float coreLeft = extraX * 32f;
        float coreRight = vpW - extraX * 32f;
        float coreTop = extraY * 32f;
        float coreBottom = vpH - extraY * 32f;

        // Fade distance in full pixels (how far outside core before max darkness)
        float fadeDistX = extraX * 32f;
        float fadeDistY = extraY * 32f;
        if (fadeDistX < 1f) fadeDistX = 1f;
        if (fadeDistY < 1f) fadeDistY = 1f;

        for (int py = 0; py < texH; py++)
        {
            // Map to full-resolution pixel
            float fullY = (py + 0.5f) * 4f;
            for (int px = 0; px < texW; px++)
            {
                float fullX = (px + 0.5f) * 4f;

                // Distance outside the core area (0 if inside)
                float dx = 0f;
                if (fullX < coreLeft) dx = (coreLeft - fullX) / fadeDistX;
                else if (fullX > coreRight) dx = (fullX - coreRight) / fadeDistX;

                float dy = 0f;
                if (fullY < coreTop) dy = (coreTop - fullY) / fadeDistY;
                else if (fullY > coreBottom) dy = (fullY - coreBottom) / fadeDistY;

                float dist = MathF.Sqrt(dx * dx + dy * dy);
                dist = Math.Clamp(dist, 0f, 1f);

                // Ease-in curve: alpha = dist^2 * 0.75 (max 75% opacity at edges)
                float alpha = dist * dist * 0.75f;

                img.SetPixel(px, py, new Color(0f, 0f, 0f, alpha));
            }
        }

        return ImageTexture.CreateFromImage(img);
    }
}
