using System;
using Godot;

namespace ArgentumNextgen.UI;

/// <summary>Non-modal HUD window: only its header drags; content retains normal input.</summary>
public partial class FloatingHudWindow : Panel
{
    public Control Content { get; private set; } = null!;
    public string Caption = "Ventana";
    public Action? LayoutChanged;
    private bool _dragging;
    private Vector2 _grab;

    public override void _Ready()
    {
        AddThemeStyleboxOverride("panel", EntryTheme.Box("101a20f5", "9e855c", 0));
        MouseFilter = MouseFilterEnum.Stop;
        var header = new Panel { Size = new Vector2(Size.X, 28), MouseDefaultCursorShape = CursorShape.Drag };
        header.AddThemeStyleboxOverride("panel", EntryTheme.Box("253235", "62543b", 0));
        AddChild(header);
        var title = EntryTheme.Text(Caption, 12);
        title.Position = new Vector2(10, 5);
        header.AddChild(title);
        var close = new Button { Text = "×", Position = new Vector2(Size.X - 28, 0), Size = new Vector2(28, 28), FocusMode = FocusModeEnum.None };
        header.AddChild(close);
        close.Pressed += () => { Hide(); LayoutChanged?.Invoke(); };
        header.GuiInput += e =>
        {
            if (e is InputEventMouseButton b && b.ButtonIndex == MouseButton.Left)
            {
                _dragging = b.Pressed;
                _grab = GetGlobalMousePosition() - GlobalPosition;
                if (!b.Pressed) LayoutChanged?.Invoke();
                header.AcceptEvent();
            }
            if (e is InputEventMouseMotion && _dragging)
            {
                GlobalPosition = GetGlobalMousePosition() - _grab;
                ClampToScreen();
                header.AcceptEvent();
            }
        };
        Content = new Control { Position = new Vector2(10, 34), Size = Size - new Vector2(20, 44), MouseFilter = MouseFilterEnum.Ignore };
        AddChild(Content);
        Resized += () =>
        {
            header.Size = new Vector2(Size.X, 28);
            close.Position = new Vector2(Size.X - 28, 0);
            Content.Size = Size - new Vector2(20, 44);
        };
    }

    public void ClampToScreen()
    {
        Vector2 area = GetViewportRect().Size;
        Position = new Vector2(Mathf.Clamp(Position.X, 0, Math.Max(0, area.X - Size.X * Scale.X)),
            Mathf.Clamp(Position.Y, 0, Math.Max(0, area.Y - Size.Y * Scale.Y)));
    }

    public void ToggleWindow() { Visible = !Visible; ClampToScreen(); LayoutChanged?.Invoke(); }
}
