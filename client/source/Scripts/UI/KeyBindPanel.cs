using Godot;
using ArgentumNextgen.Game;

namespace ArgentumNextgen.UI;

/// <summary>
/// VB6 frmTeclas — Key binding configuration panel.
/// Shows a scrollable list of actions with their bound keys.
/// Now uses RpgBaseForm for consistent RPG styling.
/// </summary>
public partial class KeyBindPanel : RpgBaseForm
{
    private GameState? _state;
    private KeyBindings? _bindings;
    private KeyBindings? _tempBindings;
    private string _dataPath = "";

    private TextureButton[] _keyButtons = new TextureButton[KeyBindings.ActionCount];
    private Label[] _actionLabels = new Label[KeyBindings.ActionCount];

    private int _rebindingIndex = -1;
    private Label? _statusLabel;

    public bool IsOpen { get; private set; }

    public KeyBindPanel() : base("Configurar Teclas", new Vector2(630, 460), "v2") { }

    public void Init(GameState state, KeyBindings bindings, string dataPath)
    {
        _state = state;
        _bindings = bindings;
        _dataPath = dataPath;
    }

    protected override void BuildContent()
    {
        var vbox = RpgTheme.CreateColumn(RpgTheme.SpacingSm);
        ContentContainer.AddChild(vbox);

        // Instruction
        var instr = RpgTheme.CreateInfoLabel("Haz click en una tecla y luego presiona la nueva tecla.", 10);
        instr.HorizontalAlignment = HorizontalAlignment.Center;
        vbox.AddChild(instr);

        vbox.AddChild(RpgTheme.CreateSeparator());

        // Scrollable area — 2 columns of key bindings
        var scrollArea = RpgTheme.CreateScrollArea(2);
        vbox.AddChild(scrollArea);
        var scrollContent = scrollArea.GetMeta("content").As<VBoxContainer>();

        var colsRow = RpgTheme.CreateRow(RpgTheme.SpacingXl);
        scrollContent.AddChild(colsRow);

        var leftCol = RpgTheme.CreateColumn(2);
        leftCol.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        colsRow.AddChild(leftCol);

        var rightCol = RpgTheme.CreateColumn(2);
        rightCol.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        colsRow.AddChild(rightCol);

        int half = KeyBindings.ActionCount / 2;

        for (int i = 0; i < KeyBindings.ActionCount; i++)
        {
            var targetCol = i < half ? leftCol : rightCol;
            var row = RpgTheme.CreateRow(RpgTheme.SpacingSm);

            var actionLabel = RpgTheme.CreateInfoLabel(KeyBindings.ActionLabels[i], 10);
            actionLabel.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            _actionLabels[i] = actionLabel;
            row.AddChild(actionLabel);

            var keyBtn = RpgTheme.CreateRpgButton("", false, 10);
            keyBtn.CustomMinimumSize = new Vector2(100, 24);
            keyBtn.FocusMode = FocusModeEnum.All;

            int capturedIdx = i;
            keyBtn.Pressed += () => StartRebind(capturedIdx);

            _keyButtons[i] = keyBtn;
            row.AddChild(keyBtn);

            targetCol.AddChild(row);
        }

        // Status label
        _statusLabel = RpgTheme.CreateInfoLabel("", 10);
        _statusLabel.HorizontalAlignment = HorizontalAlignment.Center;
        _statusLabel.AddThemeColorOverride("font_color", new Color(1f, 0.4f, 0.4f));
        vbox.AddChild(_statusLabel);

        // Bottom buttons — Cancelar (izq), Por defecto (centro), Guardar (der)
        var footer = RpgTheme.CreateFooterRow(RpgTheme.SpacingLg);
        vbox.AddChild(footer);
        var btnRow = footer.GetMeta("row").As<HBoxContainer>();

        var cancelBtn = RpgTheme.CreateRpgButton("Cancelar", false, 13);
        cancelBtn.CustomMinimumSize = new Vector2(90, 32);
        cancelBtn.Pressed += OnCancel;
        btnRow.AddChild(cancelBtn);

        var defaultsBtn = RpgTheme.CreateRpgButton("Por defecto", false, 13);
        defaultsBtn.CustomMinimumSize = new Vector2(100, 32);
        defaultsBtn.Pressed += OnDefaults;
        btnRow.AddChild(defaultsBtn);

        var saveBtn = RpgTheme.CreateRpgButton("Guardar", false, 13);
        saveBtn.CustomMinimumSize = new Vector2(90, 32);
        saveBtn.Pressed += OnSave;
        btnRow.AddChild(saveBtn);
    }

    public void Open()
    {
        if (_bindings == null) return;

        _tempBindings = _bindings.Clone();
        _rebindingIndex = -1;
        RefreshAllButtons();
        ClearStatus();

        IsOpen = true;
        if (_state != null) _state.KeyBindPanelOpen = true;
        ShowForm();
    }

    public override void HideForm()
    {
        _rebindingIndex = -1;
        _tempBindings = null;
        IsOpen = false;
        if (_state != null) _state.KeyBindPanelOpen = false;
        base.HideForm();
    }

    public void Close() => HideForm();

    private void StartRebind(int actionIndex)
    {
        _rebindingIndex = actionIndex;
        // Update the label inside the TextureButton
        var label = _keyButtons[actionIndex].GetChild(0) as Label;
        if (label != null) label.Text = "<<Presiona tecla>>";
        _keyButtons[actionIndex].Modulate = new Color(1f, 1f, 0.5f);
        SetStatus($"Presiona la nueva tecla para: {KeyBindings.ActionLabels[actionIndex]}", false);
    }

    public override void _Input(InputEvent @event)
    {
        if (_rebindingIndex < 0 || _tempBindings == null) return;
        if (@event is not InputEventKey keyEvent || !keyEvent.Pressed) return;

        Key newKey = keyEvent.Keycode;

        if (newKey == Key.Escape)
        {
            SetButtonText(_rebindingIndex, _tempBindings.Binds[_rebindingIndex].Name);
            _keyButtons[_rebindingIndex].Modulate = Colors.White;
            _rebindingIndex = -1;
            ClearStatus();
            GetViewport().SetInputAsHandled();
            return;
        }

        if (KeyBindings.IsReserved(newKey))
        {
            SetStatus($"'{KeyBindings.KeyToName(newKey)}' está reservada y no se puede asignar.", true);
            GetViewport().SetInputAsHandled();
            return;
        }

        int conflict = _tempBindings.FindConflict(newKey, _rebindingIndex);
        if (conflict >= 0)
        {
            SetStatus($"'{KeyBindings.KeyToName(newKey)}' ya está asignada a: {KeyBindings.ActionLabels[conflict]}", true);
            SetButtonText(_rebindingIndex, _tempBindings.Binds[_rebindingIndex].Name);
            _keyButtons[_rebindingIndex].Modulate = Colors.White;
            _rebindingIndex = -1;
            GetViewport().SetInputAsHandled();
            return;
        }

        string name = KeyBindings.KeyToName(newKey);
        _tempBindings.Binds[_rebindingIndex] = new KeyBind(newKey, name);

        SetButtonText(_rebindingIndex, name);
        _keyButtons[_rebindingIndex].Modulate = Colors.White;
        _keyButtons[_rebindingIndex].ReleaseFocus();

        SetStatus($"'{name}' asignada a: {KeyBindings.ActionLabels[_rebindingIndex]}", false);
        _rebindingIndex = -1;

        GetViewport().SetInputAsHandled();
    }

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

    private void RefreshAllButtons()
    {
        if (_tempBindings == null) return;
        for (int i = 0; i < KeyBindings.ActionCount; i++)
        {
            SetButtonText(i, _tempBindings.Binds[i].Name);
            _keyButtons[i].Modulate = Colors.White;
        }
    }

    private void SetButtonText(int index, string text)
    {
        // TextureButton has a child Label at index 0
        if (_keyButtons[index].GetChildCount() > 0 && _keyButtons[index].GetChild(0) is Label label)
            label.Text = text;
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
}
