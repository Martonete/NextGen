using Godot;

namespace ArgentumNextgen.Rendering;

public partial class ReactiveGroundLayer : Node2D
{
    public WorldRenderer? Renderer;
    public bool Water;
    public override void _Draw() => Renderer?.DrawReactiveGround(this, Water);
}
