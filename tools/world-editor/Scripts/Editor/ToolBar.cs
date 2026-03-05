using System;
using Godot;
using AOWorldEditor.Data;

namespace AOWorldEditor.Editor;

/// <summary>
/// Top toolbar with tool buttons, layer selector, and overlay toggles.
/// </summary>
public partial class ToolBar : HBoxContainer
{
    public EditorState? State;

    // Signals for file operations
    [Signal] public delegate void NewMapRequestedEventHandler();
    [Signal] public delegate void OpenMapRequestedEventHandler();
    [Signal] public delegate void SaveMapRequestedEventHandler();
    [Signal] public delegate void SaveAsMapRequestedEventHandler();
    [Signal] public delegate void UndoRequestedEventHandler();
    [Signal] public delegate void RedoRequestedEventHandler();

    private Label? _coordLabel;
    private OptionButton? _layerSelect;
    private readonly Button[] _toolButtons = new Button[12];

    public override void _Ready()
    {
        AddThemeConstantOverride("separation", 4);

        // File buttons
        AddButton("Nuevo", "N", () => EmitSignal(SignalName.NewMapRequested));
        AddButton("Abrir", "O", () => EmitSignal(SignalName.OpenMapRequested));
        AddButton("Guardar", "S", () => EmitSignal(SignalName.SaveMapRequested));
        AddButton("Guardar Como", null, () => EmitSignal(SignalName.SaveAsMapRequested));
        AddSep();

        // Undo/Redo
        AddButton("Deshacer", "Z", () => EmitSignal(SignalName.UndoRequested));
        AddButton("Rehacer", "Y", () => EmitSignal(SignalName.RedoRequested));
        AddSep();

        // Tool buttons
        AddToolButton("Pintar", EditorTool.Paint);
        AddToolButton("Borrar", EditorTool.Erase);
        AddToolButton("Seleccionar", EditorTool.Select);
        AddToolButton("Mover", EditorTool.Move);
        AddToolButton("Rellenar", EditorTool.Fill);
        AddToolButton("Cuentagotas", EditorTool.Eyedrop);
        AddToolButton("Bloquear", EditorTool.Block);
        AddToolButton("Luz", EditorTool.Light);
        AddToolButton("Salida", EditorTool.Exit);
        AddToolButton("Trigger", EditorTool.Trigger);
        AddSep();

        // Layer selector
        var layerLabel = new Label { Text = "Capa:" };
        AddChild(layerLabel);

        _layerSelect = new OptionButton();
        _layerSelect.AddItem("1 - Terreno", 1);
        _layerSelect.AddItem("2 - Mascara", 2);
        _layerSelect.AddItem("3 - Objetos", 3);
        _layerSelect.AddItem("4 - Techo", 4);
        _layerSelect.Selected = 0;
        _layerSelect.ItemSelected += (idx) =>
        {
            if (State != null) State.ActiveLayer = (int)idx + 1;
        };
        AddChild(_layerSelect);
        AddSep();

        // Overlay toggles
        AddToggle("Grid", true, (v) => { if (State != null) State.ShowGrid = v; });
        AddToggle("Bloq", true, (v) => { if (State != null) State.ShowBlocked = v; });
        AddToggle("Exits", true, (v) => { if (State != null) State.ShowExits = v; });
        AddToggle("L1", true, (v) => { if (State != null) State.ShowLayer1 = v; });
        AddToggle("L2", true, (v) => { if (State != null) State.ShowLayer2 = v; });
        AddToggle("L3", true, (v) => { if (State != null) State.ShowLayer3 = v; });
        AddToggle("L4", true, (v) => { if (State != null) State.ShowLayer4 = v; });
        AddSep();

        // Coordinate display
        _coordLabel = new Label { Text = "(0, 0)" };
        _coordLabel.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        _coordLabel.HorizontalAlignment = HorizontalAlignment.Right;
        AddChild(_coordLabel);
    }

    public void UpdateCoords(int x, int y)
    {
        if (_coordLabel != null)
            _coordLabel.Text = $"({x}, {y})";
    }

    public void HighlightActiveTool()
    {
        if (State == null) return;
        for (int i = 0; i < _toolButtons.Length; i++)
        {
            if (_toolButtons[i] != null)
            {
                bool active = (EditorTool)i == State.ActiveTool;
                _toolButtons[i].Modulate = active ? new Color(0.5f, 1f, 0.5f) : Colors.White;
            }
        }
    }

    private void AddButton(string text, string? shortcut, Action action)
    {
        var btn = new Button { Text = text };
        if (shortcut != null)
            btn.TooltipText = $"Ctrl+{shortcut}";
        btn.Pressed += action;
        AddChild(btn);
    }

    private void AddToolButton(string text, EditorTool tool)
    {
        var btn = new Button { Text = text };
        btn.Pressed += () =>
        {
            if (State != null) State.ActiveTool = tool;
            HighlightActiveTool();
        };
        AddChild(btn);
        _toolButtons[(int)tool] = btn;
    }

    private void AddToggle(string text, bool initial, Action<bool> onChange)
    {
        var chk = new CheckBox { Text = text, ButtonPressed = initial };
        chk.AddThemeFontSizeOverride("font_size", 11);
        chk.Toggled += (pressed) => onChange(pressed);
        AddChild(chk);
    }

    private void AddSep()
    {
        var sep = new VSeparator();
        AddChild(sep);
    }
}
