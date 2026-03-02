using Godot;
using TierrasSagradasAO.Game;

namespace TierrasSagradasAO.UI;

/// <summary>
/// VB6 frmTeclas — Key binding configuration panel.
/// Shows a scrollable list of actions with their bound keys.
/// Click a key field → press new key → validates (no duplicates, no reserved).
/// Save/Cancel/Defaults buttons. Uses temp copy for Cancel support.
/// </summary>
public partial class KeyBindPanel : PanelContainer
{
    private const int PanelW = 420;
    private const int PanelH = 500;

    private GameState? _state;
    private KeyBindings? _bindings;
    private KeyBindings? _tempBindings;
    private string _dataPath = "";

    // UI rows — one per action
    private Button[] _keyButtons = new Button[KeyBindings.ActionCount];
    private Label[] _actionLabels = new Label[KeyBindings.ActionCount];

    // Dragging
    private bool _dragging;
    private Vector2 _dragOffset;
    private const int TitleBarH = 28;

    // Rebinding state
    private int _rebindingIndex = -1; // which action is being rebound (-1 = none)
    private Label? _statusLabel;

    public bool IsOpen { get; private set; }

    public void Init(GameState state, KeyBindings bindings, string dataPath)
    {
        _state = state;
        _bindings = bindings;
        _dataPath = dataPath;
    }

    public override void _Ready()
    {
        CustomMinimumSize = new Vector2(PanelW, PanelH);
        Size = new Vector2(PanelW, PanelH);
        MouseFilter = MouseFilterEnum.Stop;

        // Panel style (dark, bordered)
        var style = new StyleBoxFlat();
        style.BgColor = new Color(0.10f, 0.10f, 0.15f, 0.97f);
        style.BorderColor = new Color(0.55f, 0.48f, 0.28f);
        style.SetBorderWidthAll(2);
        style.SetCornerRadiusAll(4);
        style.SetContentMarginAll(8);
        AddThemeStyleboxOverride("panel", style);

        var root = new VBoxContainer();

        // Title
        var title = new Label();
        title.Text = "Configurar Teclas";
        title.HorizontalAlignment = HorizontalAlignment.Center;
        title.AddThemeFontSizeOverride("font_size", 16);
        title.AddThemeColorOverride("font_color", new Color(1f, 0.85f, 0.4f));
        root.AddChild(title);
        root.AddChild(Spacer(2));

        // Instruction label
        var instr = new Label();
        instr.Text = "Haz click en una tecla y luego presiona la nueva tecla.";
        instr.HorizontalAlignment = HorizontalAlignment.Center;
        instr.AddThemeFontSizeOverride("font_size", 10);
        instr.AddThemeColorOverride("font_color", new Color(0.7f, 0.7f, 0.7f));
        root.AddChild(instr);
        root.AddChild(Spacer(4));

        // Separator
        var sep = new HSeparator();
        root.AddChild(sep);
        root.AddChild(Spacer(4));

        // Scrollable area for key bindings
        var scroll = new ScrollContainer();
        scroll.SizeFlagsVertical = SizeFlags.ExpandFill;
        scroll.HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled;

        var listBox = new VBoxContainer();
        listBox.SizeFlagsHorizontal = SizeFlags.ExpandFill;

        // Header row
        var headerRow = new HBoxContainer();
        var headerAction = new Label();
        headerAction.Text = "Acción";
        headerAction.CustomMinimumSize = new Vector2(220, 0);
        headerAction.AddThemeFontSizeOverride("font_size", 11);
        headerAction.AddThemeColorOverride("font_color", new Color(0.85f, 0.75f, 0.45f));
        headerRow.AddChild(headerAction);
        var headerKey = new Label();
        headerKey.Text = "Tecla";
        headerKey.AddThemeFontSizeOverride("font_size", 11);
        headerKey.AddThemeColorOverride("font_color", new Color(0.85f, 0.75f, 0.45f));
        headerRow.AddChild(headerKey);
        listBox.AddChild(headerRow);
        listBox.AddChild(Spacer(2));

        // Create a row for each bindable action
        for (int i = 0; i < KeyBindings.ActionCount; i++)
        {
            var row = new HBoxContainer();

            var actionLabel = new Label();
            actionLabel.Text = KeyBindings.ActionLabels[i];
            actionLabel.CustomMinimumSize = new Vector2(220, 0);
            actionLabel.AddThemeFontSizeOverride("font_size", 11);
            _actionLabels[i] = actionLabel;
            row.AddChild(actionLabel);

            var keyBtn = new Button();
            keyBtn.CustomMinimumSize = new Vector2(140, 26);
            keyBtn.AddThemeFontSizeOverride("font_size", 11);
            keyBtn.FocusMode = FocusModeEnum.All;

            // Button style
            var btnStyle = new StyleBoxFlat();
            btnStyle.BgColor = new Color(0.15f, 0.15f, 0.22f);
            btnStyle.BorderColor = new Color(0.4f, 0.4f, 0.45f);
            btnStyle.SetBorderWidthAll(1);
            btnStyle.SetContentMarginAll(4);
            btnStyle.SetCornerRadiusAll(2);
            keyBtn.AddThemeStyleboxOverride("normal", btnStyle);

            int capturedIdx = i; // capture for lambda
            keyBtn.Pressed += () => StartRebind(capturedIdx);

            _keyButtons[i] = keyBtn;
            row.AddChild(keyBtn);

            listBox.AddChild(row);
        }

        scroll.AddChild(listBox);
        root.AddChild(scroll);

        root.AddChild(Spacer(4));

        // Status label (shows conflicts, etc.)
        _statusLabel = new Label();
        _statusLabel.Text = "";
        _statusLabel.HorizontalAlignment = HorizontalAlignment.Center;
        _statusLabel.AddThemeFontSizeOverride("font_size", 10);
        _statusLabel.AddThemeColorOverride("font_color", new Color(1f, 0.4f, 0.4f));
        root.AddChild(_statusLabel);

        root.AddChild(Spacer(4));

        // Bottom buttons
        var btnRow = new HBoxContainer();
        btnRow.Alignment = BoxContainer.AlignmentMode.Center;

        var saveBtn = new Button();
        saveBtn.Text = "Guardar";
        saveBtn.CustomMinimumSize = new Vector2(90, 30);
        saveBtn.Pressed += OnSave;
        btnRow.AddChild(saveBtn);

        btnRow.AddChild(Spacer(8, true));

        var defaultsBtn = new Button();
        defaultsBtn.Text = "Por defecto";
        defaultsBtn.CustomMinimumSize = new Vector2(100, 30);
        defaultsBtn.Pressed += OnDefaults;
        btnRow.AddChild(defaultsBtn);

        btnRow.AddChild(Spacer(8, true));

        var cancelBtn = new Button();
        cancelBtn.Text = "Cancelar";
        cancelBtn.CustomMinimumSize = new Vector2(90, 30);
        cancelBtn.Pressed += OnCancel;
        btnRow.AddChild(cancelBtn);

        root.AddChild(btnRow);
        AddChild(root);

        Visible = false;
    }

    public override void _GuiInput(InputEvent @event)
    {
        if (@event is InputEventMouseButton mb)
        {
            if (mb.ButtonIndex == MouseButton.Left)
            {
                if (mb.Pressed && mb.Position.Y <= TitleBarH)
                {
                    _dragging = true;
                    _dragOffset = mb.GlobalPosition - GlobalPosition;
                }
                else if (!mb.Pressed)
                    _dragging = false;
            }
            AcceptEvent();
        }
        else if (@event is InputEventMouseMotion mm && _dragging)
        {
            GlobalPosition = mm.GlobalPosition - _dragOffset;
            AcceptEvent();
        }
    }

    // ── Open / Close ──────────────────────────────────────

    public void Open()
    {
        if (_bindings == null) return;

        _tempBindings = _bindings.Clone();
        _rebindingIndex = -1;
        RefreshAllButtons();
        ClearStatus();

        IsOpen = true;
        if (_state != null) _state.KeyBindPanelOpen = true;
        Visible = true;
    }

    public void Close()
    {
        _rebindingIndex = -1;
        _tempBindings = null;
        IsOpen = false;
        if (_state != null) _state.KeyBindPanelOpen = false;
        Visible = false;
    }

    // ── Rebinding logic ───────────────────────────────────

    private void StartRebind(int actionIndex)
    {
        _rebindingIndex = actionIndex;
        _keyButtons[actionIndex].Text = "<<Presiona una tecla>>";
        _keyButtons[actionIndex].Modulate = new Color(1f, 1f, 0.5f);
        SetStatus($"Presiona la nueva tecla para: {KeyBindings.ActionLabels[actionIndex]}", false);
    }

    public override void _UnhandledKeyInput(InputEvent @event)
    {
        if (_rebindingIndex < 0 || _tempBindings == null) return;
        if (@event is not InputEventKey keyEvent || !keyEvent.Pressed) return;

        Key newKey = keyEvent.Keycode;

        // Escape cancels rebinding (doesn't close panel)
        if (newKey == Key.Escape)
        {
            _keyButtons[_rebindingIndex].Text = _tempBindings.Binds[_rebindingIndex].Name;
            _keyButtons[_rebindingIndex].Modulate = Colors.White;
            _rebindingIndex = -1;
            ClearStatus();
            GetViewport().SetInputAsHandled();
            return;
        }

        // Check reserved keys
        if (KeyBindings.IsReserved(newKey))
        {
            SetStatus($"'{KeyBindings.KeyToName(newKey)}' está reservada y no se puede asignar.", true);
            GetViewport().SetInputAsHandled();
            return;
        }

        // Check for conflict with other actions
        int conflict = _tempBindings.FindConflict(newKey, _rebindingIndex);
        if (conflict >= 0)
        {
            SetStatus($"'{KeyBindings.KeyToName(newKey)}' ya está asignada a: {KeyBindings.ActionLabels[conflict]}", true);
            // Reset the button appearance
            _keyButtons[_rebindingIndex].Text = _tempBindings.Binds[_rebindingIndex].Name;
            _keyButtons[_rebindingIndex].Modulate = Colors.White;
            _rebindingIndex = -1;
            GetViewport().SetInputAsHandled();
            return;
        }

        // Apply the new binding
        string name = KeyBindings.KeyToName(newKey);
        _tempBindings.Binds[_rebindingIndex] = new KeyBind(newKey, name);

        // Update button
        _keyButtons[_rebindingIndex].Text = name;
        _keyButtons[_rebindingIndex].Modulate = Colors.White;

        SetStatus($"'{name}' asignada a: {KeyBindings.ActionLabels[_rebindingIndex]}", false);
        _rebindingIndex = -1;

        GetViewport().SetInputAsHandled();
    }

    // ── Button handlers ───────────────────────────────────

    private void OnSave()
    {
        if (_bindings == null || _tempBindings == null) return;

        _bindings.CopyFrom(_tempBindings);
        _bindings.Save(_dataPath);

        _state?.ChatMessages.Enqueue(new ChatMessage
        {
            Text = "Teclas guardadas correctamente.",
            Color = "00FF00"
        });

        Close();
    }

    private void OnDefaults()
    {
        if (_tempBindings == null) return;

        _tempBindings.SetDefaults();
        _rebindingIndex = -1;
        RefreshAllButtons();
        SetStatus("Teclas restauradas a valores por defecto.", false);
    }

    private void OnCancel()
    {
        Close();
    }

    // ── UI helpers ────────────────────────────────────────

    private void RefreshAllButtons()
    {
        if (_tempBindings == null) return;

        for (int i = 0; i < KeyBindings.ActionCount; i++)
        {
            _keyButtons[i].Text = _tempBindings.Binds[i].Name;
            _keyButtons[i].Modulate = Colors.White;
        }
    }

    private void SetStatus(string text, bool isError)
    {
        if (_statusLabel == null) return;
        _statusLabel.Text = text;
        _statusLabel.AddThemeColorOverride("font_color",
            isError ? new Color(1f, 0.4f, 0.4f) : new Color(0.5f, 1f, 0.5f));
    }

    private void ClearStatus()
    {
        if (_statusLabel != null) _statusLabel.Text = "";
    }

    private static Control Spacer(int size, bool horizontal = false)
    {
        var s = new Control();
        s.CustomMinimumSize = horizontal ? new Vector2(size, 0) : new Vector2(0, size);
        return s;
    }
}
