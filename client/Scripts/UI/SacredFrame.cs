using Godot;

namespace ArgentumNextgen.UI;

/// <summary>Engraved frame with bevels and corner inlays; no textures or shader.</summary>
public partial class SacredFrame : Control
{
    public bool Entry;
    public override void _Ready() { MouseFilter = MouseFilterEnum.Ignore; Resized += QueueRedraw; }
    public override void _Draw()
    {
        if (Size.X < 20 || Size.Y < 20) return;
        DrawRect(new Rect2(Vector2.Zero, Size), new Color("0e1317f5"));
        DrawRect(new Rect2(1, 1, Size.X - 2, Size.Y - 2), SacredTheme.Edge, false);
        DrawRect(new Rect2(5, 5, Size.X - 10, Size.Y - 10), new Color("77705a66"), false);
        DrawLine(new Vector2(12, 2), new Vector2(Size.X - 12, 2), SacredTheme.Bronze);
        float h = Entry ? 12 : 30;
        if (!Entry)
        {
            DrawRect(new Rect2(6, 6, Size.X - 12, h), new Color("242a2b"));
            DrawLine(new Vector2(12, h + 6), new Vector2(Size.X - 12, h + 6), SacredTheme.Edge);
        }
        foreach (var corner in new[] { new Vector2(9, 9), new Vector2(Size.X - 9, 9), new Vector2(9, Size.Y - 9), Size - new Vector2(9, 9) })
        {
            float dx = corner.X < Size.X / 2 ? 1 : -1, dy = corner.Y < Size.Y / 2 ? 1 : -1;
            DrawLine(corner, corner + new Vector2(20 * dx, 0), SacredTheme.Bronze);
            DrawLine(corner, corner + new Vector2(0, 20 * dy), SacredTheme.Bronze);
            DrawLine(corner + new Vector2(3 * dx, 10 * dy), corner + new Vector2(10 * dx, 3 * dy), SacredTheme.Bronze);
        }
        if (Entry)
        {
            float x = Size.X / 2;
            DrawLine(new Vector2(36, Size.Y - 16), new Vector2(x - 13, Size.Y - 16), SacredTheme.Edge);
            DrawLine(new Vector2(x + 13, Size.Y - 16), new Vector2(Size.X - 36, Size.Y - 16), SacredTheme.Edge);
            DrawColoredPolygon(new[] { new Vector2(x, Size.Y - 20), new Vector2(x + 4, Size.Y - 16), new Vector2(x, Size.Y - 12), new Vector2(x - 4, Size.Y - 16) }, SacredTheme.Bronze);
        }
    }
}
