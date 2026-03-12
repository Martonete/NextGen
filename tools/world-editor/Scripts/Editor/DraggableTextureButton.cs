#nullable enable
using Godot;
using AOWorldEditor.Data;

namespace AOWorldEditor.Editor;

/// <summary>
/// TextureButton subclass with drag-drop reordering and right-click context menu support.
/// </summary>
public partial class DraggableTextureButton : TextureButton
{
    [Signal] public delegate void ReorderedEventHandler(int fromGridIndex, int toGridIndex);
    [Signal] public delegate void ContextMenuRequestedEventHandler(int gridIndex);

    public TextureRef? TexRef;
    public int GridIndex;
    public bool SearchMode; // disables drag when showing search results

    private bool _dropHighlight;

    public override Variant _GetDragData(Vector2 atPosition)
    {
        if (SearchMode || TexRef == null) return default;

        // Create a small preview for drag
        var preview = new TextureRect
        {
            Texture = TextureNormal,
            CustomMinimumSize = new Vector2(48, 48),
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
        };
        SetDragPreview(preview);

        var data = new Godot.Collections.Dictionary
        {
            { "type", "texture_reorder" },
            { "grid_index", GridIndex }
        };
        return data;
    }

    public override bool _CanDropData(Vector2 atPosition, Variant data)
    {
        if (SearchMode) return false;
        if (data.VariantType != Variant.Type.Dictionary) return false;
        var dict = data.AsGodotDictionary();
        bool can = dict.ContainsKey("type") && dict["type"].AsString() == "texture_reorder";
        if (can != _dropHighlight)
        {
            _dropHighlight = can;
            QueueRedraw();
        }
        return can;
    }

    public override void _DropData(Vector2 atPosition, Variant data)
    {
        _dropHighlight = false;
        QueueRedraw();
        if (data.VariantType != Variant.Type.Dictionary) return;
        var dict = data.AsGodotDictionary();
        if (!dict.ContainsKey("grid_index")) return;
        int sourceIdx = dict["grid_index"].AsInt32();
        if (sourceIdx != GridIndex)
            EmitSignal(SignalName.Reordered, sourceIdx, GridIndex);
    }

    public override void _Draw()
    {
        base._Draw();
        if (_dropHighlight)
        {
            // Draw 2px blue line on left edge as drop indicator
            var color = EditorTheme.ACCENT;
            DrawLine(new Vector2(0, 0), new Vector2(0, Size.Y), color, 2);
        }
    }

    public override void _Notification(int what)
    {
        base._Notification(what);
        if (what == NotificationDragEnd)
        {
            _dropHighlight = false;
            QueueRedraw();
        }
    }

    public override void _GuiInput(InputEvent @event)
    {
        base._GuiInput(@event);
        if (@event is InputEventMouseButton mb && mb.Pressed && mb.ButtonIndex == MouseButton.Right)
        {
            EmitSignal(SignalName.ContextMenuRequested, GridIndex);
            AcceptEvent();
        }
    }
}
