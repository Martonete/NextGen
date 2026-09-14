using Godot;

namespace ArgentumNextgen.UI;

/// <summary>Keeps the TextureButton API used by gameplay, with native themed chrome.</summary>
public partial class SacredTextureButton : TextureButton
{
    public override void _Ready()
    {
        MouseEntered += QueueRedraw; MouseExited += QueueRedraw;
        ButtonDown += QueueRedraw; ButtonUp += QueueRedraw; Resized += QueueRedraw;
    }
    public override void _Draw()
    {
        bool hover = GetDrawMode() == DrawMode.Hover || GetDrawMode() == DrawMode.HoverPressed;
        bool pressed = GetDrawMode() == DrawMode.Pressed || GetDrawMode() == DrawMode.HoverPressed;
        var fill = Disabled ? new Color("171c1e") : pressed ? new Color("394039") : hover ? new Color("303b3b") : new Color("20272a");
        var edge = hover || pressed ? SacredTheme.Bronze : SacredTheme.Edge;
        DrawRect(new Rect2(Vector2.Zero, Size), fill);
        DrawRect(new Rect2(.5f, .5f, Size.X - 1, Size.Y - 1), edge, false);
        DrawLine(new Vector2(4, 2), new Vector2(Size.X - 4, 2), new Color("b99a6644"));
        DrawLine(new Vector2(4, Size.Y - 3), new Vector2(Size.X - 4, Size.Y - 3), new Color("00000088"));
        DrawLine(new Vector2(2, 5), new Vector2(2, Size.Y - 5), edge);
        DrawLine(new Vector2(Size.X - 3, 5), new Vector2(Size.X - 3, Size.Y - 5), edge);
    }
}
