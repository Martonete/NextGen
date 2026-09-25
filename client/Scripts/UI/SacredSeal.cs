using Godot;

namespace ArgentumNextgen.UI;

/// <summary>Tierras Sagradas compass/sword seal, drawn at any UI scale.</summary>
public partial class SacredSeal : Control
{
    public override void _Ready() { MouseFilter = MouseFilterEnum.Ignore; Resized += QueueRedraw; }
    public override void _Draw()
    {
        var c = Size / 2;
        float r = Mathf.Min(Size.X, Size.Y) * .44f;
        DrawCircle(c, r, new Color("212a2c"));
        DrawArc(c, r, 0, Mathf.Tau, 64, SacredTheme.Edge, 1, true);
        DrawArc(c, r * .79f, 0, Mathf.Tau, 64, SacredTheme.Bronze, 1, true);
        for (int i = 0; i < 8; i++)
        {
            var v = Vector2.FromAngle(i * Mathf.Pi / 4);
            DrawLine(c + v * r * .86f, c + v * r, SacredTheme.Bronze, 1, true);
        }
        DrawColoredPolygon(new[] { c + new Vector2(0, -r * .72f), c + new Vector2(r * .15f, -r * .12f), c + new Vector2(0, r * .42f), c + new Vector2(-r * .15f, -r * .12f) }, SacredTheme.Paper);
        DrawLine(c + new Vector2(-r * .35f, r * .17f), c + new Vector2(r * .35f, r * .17f), SacredTheme.Bronze, 2, true);
        DrawLine(c + new Vector2(0, r * .28f), c + new Vector2(0, r * .62f), SacredTheme.Bronze, 2, true);
    }
}
