using Godot;

namespace ArgentumNextgen.Rendering;

/// <summary>Front half of procedural auras, drawn after the corresponding bodies.</summary>
public partial class AuraFrontLayer : Node2D
{
    public WorldRenderer? Renderer;
    public bool Reflected;
    public override void _Draw()
    {
        if (Reflected) Renderer?.DrawPendingReflAuras(this, true);
        else Renderer?.DrawPendingAuras(this, true);
    }
}
