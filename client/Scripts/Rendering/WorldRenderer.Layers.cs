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

    // No _Process QueueRedraw needed — parent WorldRenderer triggers via line 777
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
