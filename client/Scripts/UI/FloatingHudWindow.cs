using System;
using Godot;

namespace ArgentumNextgen.UI;

/// <summary>Non-modal HUD window: only its header drags; content retains normal input.
/// Skinned with the same info_window.png carved frame + name-plate badge as every other
/// RPG dialog (RpgBaseForm), instead of a flat login-screen box.</summary>
public partial class FloatingHudWindow : Panel
{
    public Control Content { get; private set; } = null!;
    public string Caption = "Ventana";
    public Action? LayoutChanged;
    /// <summary>True once the player has dragged the resize grip — from then on
    /// FitToContent leaves the window's size alone, the player's choice wins.</summary>
    public bool UserResized { get; private set; }
    private static readonly Vector2 MinSize = new(150, 90);
    private bool _dragging;
    private Vector2 _grab;
    private bool _resizing;
    private bool _rzLeft, _rzRight, _rzTop, _rzBottom;
    private Vector2 _rzStartMouse, _rzStartSize, _rzStartPos;
    private Control _header = null!;
    private NinePatchRect _frame = null!;
    private NinePatchRect _titleBadge = null!;

    public override void _Ready()
    {
        // Transparent — the nine-patch frame below draws the actual skin.
        AddThemeStyleboxOverride("panel", new StyleBoxEmpty());
        MouseFilter = MouseFilterEnum.Stop;
        // NOT ClipContents anywhere here — the title badge below deliberately straddles
        // the top border (OffsetTop=-8), and Quickbar's slot-config popup (a descendant
        // of Content, see Quickbar.OpenMenu) deliberately renders above/below its own
        // window's bounds. Clipping either `this` or Content cuts one of them off, so a
        // manually-shrunk window may show a sliver of overflowing content — an acceptable
        // trade against breaking the badge or the popup outright.

        _frame = RpgTheme.CreateNinePatch("info_window.png", new Vector4(16, 16, 16, 16));
        AddChild(_frame);
        RpgTheme.FillParent(_frame);

        // Name-plate badge straddling the top border, same as RpgBaseForm's title frame.
        _titleBadge = RpgTheme.CreateNinePatch("name_frame_mid_ready.png", new Vector4(24, 8, 24, 8));
        AddChild(_titleBadge);
        var title = RpgTheme.CreateInfoLabel(Caption, 10);
        title.HorizontalAlignment = HorizontalAlignment.Center;
        title.VerticalAlignment = VerticalAlignment.Center;
        title.MouseFilter = MouseFilterEnum.Ignore;
        _titleBadge.AddChild(title);
        RpgTheme.FillParent(title);
        LayoutTitleBadge();

        // Invisible drag strip across the top — the badge sits on top of it, unaffected.
        _header = new Control { Size = new Vector2(Size.X, 26), MouseDefaultCursorShape = CursorShape.Drag };
        AddChild(_header);

        var close = RpgTheme.CreateMiniButton("Mini_exit.png", "Mini_exit_t.png", new Vector2(20, 20));
        AddChild(close);
        close.AnchorLeft = 1f; close.AnchorRight = 1f; close.AnchorTop = 0f; close.AnchorBottom = 0f;
        close.OffsetLeft = -26; close.OffsetRight = -6; close.OffsetTop = 4; close.OffsetBottom = 24;
        close.Pressed += () => { Hide(); DeferLayoutChanged(); };

        _header.GuiInput += e =>
        {
            if (e is InputEventMouseButton b && b.ButtonIndex == MouseButton.Left)
            {
                _dragging = b.Pressed;
                _grab = GetGlobalMousePosition() - GlobalPosition;
                if (!b.Pressed) DeferLayoutChanged();
                _header.AcceptEvent();
            }
            if (e is InputEventMouseMotion && _dragging)
            {
                GlobalPosition = GetGlobalMousePosition() - _grab;
                ClampToScreen();
                _header.AcceptEvent();
            }
        };

        Content = new Control { Position = new Vector2(10, 34), Size = Size - new Vector2(20, 44), MouseFilter = MouseFilterEnum.Ignore };
        AddChild(Content);
        Resized += () =>
        {
            _header.Size = new Vector2(Size.X, 26);
            Content.Size = Size - new Vector2(20, 44);
            LayoutTitleBadge();
        };

        // Resize handles on all 4 edges + the two bottom corners (top corners are left
        // free for the close button and the title badge). Purely event-driven, like the
        // header drag above — no per-frame cost outside of an actual drag.
        const float t = 6f, c = 14f; // edge thickness, corner square size
        AddResizeHandle(new Vector2(0, 0), new Vector2(0, 1), new Vector4(0, c, t, -c), CursorShape.Hsize, left: true, top: false, right: false, bottom: false);
        AddResizeHandle(new Vector2(1, 0), new Vector2(1, 1), new Vector4(-t, c, 0, -c), CursorShape.Hsize, left: false, top: false, right: true, bottom: false);
        AddResizeHandle(new Vector2(0, 0), new Vector2(1, 0), new Vector4(c, 0, -c, t), CursorShape.Vsize, left: false, top: true, right: false, bottom: false);
        AddResizeHandle(new Vector2(0, 1), new Vector2(1, 1), new Vector4(c, -t, -c, 0), CursorShape.Vsize, left: false, top: false, right: false, bottom: true);
        AddResizeHandle(new Vector2(0, 1), new Vector2(0, 1), new Vector4(0, -c, c, 0), CursorShape.Bdiagsize, left: true, top: false, right: false, bottom: true);
        AddResizeHandle(new Vector2(1, 1), new Vector2(1, 1), new Vector4(-c, -c, 0, 0), CursorShape.Fdiagsize, left: false, top: false, right: true, bottom: true);
    }

    /// <summary>One resize hit-region. `anchorMin/Max` place it (Godot anchor space,
    /// 0..1); `offsets` are (left, top, right, bottom) pixel insets from those anchors.
    /// `left/top/right/bottom` say which edges this handle moves when dragged.</summary>
    private void AddResizeHandle(Vector2 anchorMin, Vector2 anchorMax, Vector4 offsets, CursorShape cursor,
        bool left, bool top, bool right, bool bottom)
    {
        var grip = new Control { MouseDefaultCursorShape = cursor };
        AddChild(grip);
        grip.AnchorLeft = anchorMin.X; grip.AnchorTop = anchorMin.Y;
        grip.AnchorRight = anchorMax.X; grip.AnchorBottom = anchorMax.Y;
        grip.OffsetLeft = offsets.X; grip.OffsetTop = offsets.Y; grip.OffsetRight = offsets.Z; grip.OffsetBottom = offsets.W;
        grip.GuiInput += e =>
        {
            if (e is InputEventMouseButton b && b.ButtonIndex == MouseButton.Left)
            {
                _resizing = b.Pressed;
                if (b.Pressed)
                {
                    UserResized = true;
                    _rzLeft = left; _rzTop = top; _rzRight = right; _rzBottom = bottom;
                    _rzStartMouse = GetGlobalMousePosition();
                    _rzStartSize = Size;
                    _rzStartPos = Position;
                }
                else DeferLayoutChanged();
                grip.AcceptEvent();
            }
            if (e is InputEventMouseMotion && _resizing)
            {
                ApplyResizeDrag();
                grip.AcceptEvent();
            }
        };
    }

    private void ApplyResizeDrag()
    {
        var delta = (GetGlobalMousePosition() - _rzStartMouse) / Scale;

        float w = _rzStartSize.X, x = _rzStartPos.X;
        if (_rzRight) w = _rzStartSize.X + delta.X;
        else if (_rzLeft) { w = _rzStartSize.X - delta.X; x = _rzStartPos.X + delta.X; }
        if (w < MinSize.X) { if (_rzLeft) x = _rzStartPos.X + _rzStartSize.X - MinSize.X; w = MinSize.X; }

        float h = _rzStartSize.Y, y = _rzStartPos.Y;
        if (_rzBottom) h = _rzStartSize.Y + delta.Y;
        else if (_rzTop) { h = _rzStartSize.Y - delta.Y; y = _rzStartPos.Y + delta.Y; }
        if (h < MinSize.Y) { if (_rzTop) y = _rzStartPos.Y + _rzStartSize.Y - MinSize.Y; h = MinSize.Y; }

        Position = new Vector2(x, y);
        Size = new Vector2(w, h);
    }

    private void LayoutTitleBadge()
    {
        float frameW = Math.Max(Caption.Length * 8 + 40, 120);
        _titleBadge.AnchorLeft = 0.5f;
        _titleBadge.AnchorRight = 0.5f;
        _titleBadge.AnchorTop = 0f;
        _titleBadge.AnchorBottom = 0f;
        _titleBadge.OffsetLeft = -frameW / 2f;
        _titleBadge.OffsetRight = frameW / 2f;
        _titleBadge.OffsetTop = -8;
        _titleBadge.OffsetBottom = 20;
    }

    public void ClampToScreen()
    {
        Vector2 area = GetViewportRect().Size;
        Position = new Vector2(Mathf.Clamp(Position.X, 0, Math.Max(0, area.X - Size.X * Scale.X)),
            Mathf.Clamp(Position.Y, 0, Math.Max(0, area.Y - Size.Y * Scale.Y)));
    }

    /// <summary>Restore a size the player picked in a previous session — skips the
    /// auto-fit from then on, same as dragging the grip live. Clamped to the current
    /// viewport too: a size saved on a bigger monitor must not come back unreachable
    /// (ClampToScreen only ever pins Position, never shrinks an oversized Size).</summary>
    public void ApplySavedSize(Vector2 size)
    {
        Vector2 area = GetViewportRect().Size;
        Size = new Vector2(
            Math.Clamp(size.X, MinSize.X, Math.Max(MinSize.X, area.X)),
            Math.Clamp(size.Y, MinSize.Y, Math.Max(MinSize.Y, area.Y)));
        UserResized = true;
    }

    /// <summary>Shrink/grow the window height to hug its currently-visible content, so
    /// switching tabs or resizing never leaves empty panel below the last widget.
    /// Width is left as configured — only vertical slack is trimmed. No-ops once the
    /// player has manually resized the window — their choice sticks.</summary>
    public void FitToContent(float bottomPadding = 6f)
    {
        if (UserResized) return;
        float maxY = 0f;
        bool any = false;
        foreach (Node child in Content.GetChildren())
        {
            if (child is not Control c || !c.Visible) continue;
            float bottom = c.Position.Y + c.Size.Y * c.Scale.Y;
            if (bottom > maxY) maxY = bottom;
            any = true;
        }
        if (!any) return;
        float chromeY = Size.Y - Content.Size.Y; // header + margins — constant regardless of height
        Size = new Vector2(Size.X, maxY + bottomPadding + chromeY);
        ClampToScreen();
    }

    // Deferred so the disk write (ConfigFile.Save in Main.SaveHudLayout) never blocks
    // the click that opened/closed the window — the toggle itself is instant.
    public void ToggleWindow() { Visible = !Visible; ClampToScreen(); DeferLayoutChanged(); }
    private void DeferLayoutChanged() { if (LayoutChanged != null) Callable.From(LayoutChanged).CallDeferred(); }
}
