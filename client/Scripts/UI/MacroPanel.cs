using Godot;
using TierrasSagradasAO.Game;

namespace TierrasSagradasAO.UI;

/// <summary>
/// VB6 frmMakro — Macro configuration panel.
/// 10 text inputs for keys 1-0, save/cancel buttons.
/// Macros are saved to Data/INIT/Macro.tsao in INI format.
/// </summary>
public partial class MacroPanel : PanelContainer
{
    private const int PanelW = 280;
    private const int PanelH = 380;

    // Dragging
    private bool _dragging;
    private Vector2 _dragOffset;
    private const int TitleBarH = 28;

    private GameState? _state;
    private LineEdit[] _inputs = new LineEdit[10];
    private string _macroFilePath = "";

    public void Init(GameState state, string dataPath)
    {
        _state = state;
        _macroFilePath = System.IO.Path.Combine(dataPath, "INIT", "Macro.tsao");
        LoadMacros();
    }

    public override void _Ready()
    {
        CustomMinimumSize = new Vector2(PanelW, PanelH);
        Size = new Vector2(PanelW, PanelH);
        MouseFilter = MouseFilterEnum.Stop;

        // Dark background
        var styleBox = new StyleBoxFlat();
        styleBox.BgColor = new Color(0.12f, 0.12f, 0.18f, 0.95f);
        styleBox.BorderColor = new Color(0.5f, 0.45f, 0.3f);
        styleBox.SetBorderWidthAll(2);
        styleBox.SetContentMarginAll(10);
        AddThemeStyleboxOverride("panel", styleBox);

        var vbox = new VBoxContainer();
        vbox.AddThemeFontSizeOverride("font_size", 12);

        // Title
        var title = new Label();
        title.Text = "Configurar Macros";
        title.HorizontalAlignment = HorizontalAlignment.Center;
        title.AddThemeFontSizeOverride("font_size", 14);
        title.AddThemeColorOverride("font_color", new Color(1f, 0.85f, 0.4f));
        vbox.AddChild(title);

        vbox.AddChild(CreateSpacer(6));

        // 10 macro inputs (keys 1,2,3...9,0)
        string[] keyLabels = { "1", "2", "3", "4", "5", "6", "7", "8", "9", "0" };
        for (int i = 0; i < 10; i++)
        {
            var row = new HBoxContainer();

            var label = new Label();
            label.Text = $"Tecla {keyLabels[i]}:";
            label.CustomMinimumSize = new Vector2(65, 0);
            label.AddThemeFontSizeOverride("font_size", 12);
            row.AddChild(label);

            var input = new LineEdit();
            input.CustomMinimumSize = new Vector2(180, 0);
            input.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            input.AddThemeFontSizeOverride("font_size", 11);
            input.PlaceholderText = "Comando...";

            // Style the input
            var inputStyle = new StyleBoxFlat();
            inputStyle.BgColor = new Color(0.08f, 0.08f, 0.12f);
            inputStyle.BorderColor = new Color(0.35f, 0.35f, 0.4f);
            inputStyle.SetBorderWidthAll(1);
            inputStyle.SetContentMarginAll(4);
            input.AddThemeStyleboxOverride("normal", inputStyle);
            input.AddThemeStyleboxOverride("focus", inputStyle);

            _inputs[i] = input;
            row.AddChild(input);
            vbox.AddChild(row);
        }

        vbox.AddChild(CreateSpacer(10));

        // Buttons row
        var btnRow = new HBoxContainer();
        btnRow.Alignment = BoxContainer.AlignmentMode.Center;

        var saveBtn = new Button();
        saveBtn.Text = "Grabar Configuración";
        saveBtn.CustomMinimumSize = new Vector2(140, 30);
        saveBtn.Pressed += OnSave;
        btnRow.AddChild(saveBtn);

        btnRow.AddChild(CreateSpacer(10, true));

        var cancelBtn = new Button();
        cancelBtn.Text = "Cancelar";
        cancelBtn.CustomMinimumSize = new Vector2(90, 30);
        cancelBtn.Pressed += OnCancel;
        btnRow.AddChild(cancelBtn);

        vbox.AddChild(btnRow);
        AddChild(vbox);

        Visible = false;
    }

    private static Control CreateSpacer(int height, bool horizontal = false)
    {
        var spacer = new Control();
        if (horizontal)
            spacer.CustomMinimumSize = new Vector2(height, 0);
        else
            spacer.CustomMinimumSize = new Vector2(0, height);
        return spacer;
    }

    public void Open()
    {
        if (_state == null) return;

        // Populate inputs from current macros
        for (int i = 0; i < 10; i++)
            _inputs[i].Text = _state.Macros[i] ?? "";

        _state.MacroPanelOpen = true;
        Visible = true;

        // Focus first input
        _inputs[0].GrabFocus();
    }

    public void Close()
    {
        if (_state != null)
            _state.MacroPanelOpen = false;
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

    private void OnSave()
    {
        if (_state == null) return;

        // Copy inputs to state
        for (int i = 0; i < 10; i++)
            _state.Macros[i] = _inputs[i].Text.Trim();

        SaveMacros();
        Close();
    }

    private void OnCancel()
    {
        Close();
    }

    /// <summary>
    /// Load macros from Data/INIT/Macro.tsao (VB6 INI format).
    /// [Macro] section with Tecla0..Tecla9 keys.
    /// </summary>
    public void LoadMacros()
    {
        if (_state == null || string.IsNullOrEmpty(_macroFilePath)) return;
        if (!System.IO.File.Exists(_macroFilePath)) return;

        try
        {
            var lines = System.IO.File.ReadAllLines(_macroFilePath);
            bool inSection = false;
            foreach (var line in lines)
            {
                string trimmed = line.Trim();
                if (trimmed.Equals("[Macro]", System.StringComparison.OrdinalIgnoreCase))
                {
                    inSection = true;
                    continue;
                }
                if (trimmed.StartsWith("[")) { inSection = false; continue; }
                if (!inSection) continue;

                int eq = trimmed.IndexOf('=');
                if (eq < 0) continue;
                string key = trimmed[..eq].Trim();
                string val = trimmed[(eq + 1)..].Trim();

                // Parse TeclaN where N is 0-9
                if (key.StartsWith("Tecla", System.StringComparison.OrdinalIgnoreCase)
                    && int.TryParse(key[5..], out int idx) && idx >= 0 && idx <= 9)
                {
                    _state.Macros[idx] = val;
                }
            }
            GD.Print($"[MACRO] Loaded macros from {_macroFilePath}");
        }
        catch (System.Exception ex)
        {
            GD.PrintErr($"[MACRO] Failed to load macros: {ex.Message}");
        }
    }

    /// <summary>
    /// Save macros to Data/INIT/Macro.tsao in VB6 INI format.
    /// </summary>
    private void SaveMacros()
    {
        if (_state == null || string.IsNullOrEmpty(_macroFilePath)) return;

        try
        {
            var dir = System.IO.Path.GetDirectoryName(_macroFilePath);
            if (!string.IsNullOrEmpty(dir) && !System.IO.Directory.Exists(dir))
                System.IO.Directory.CreateDirectory(dir);

            var sb = new System.Text.StringBuilder();
            sb.AppendLine("[Macro]");
            for (int i = 0; i < 10; i++)
                sb.AppendLine($"Tecla{i}={_state.Macros[i] ?? ""}");

            System.IO.File.WriteAllText(_macroFilePath, sb.ToString());
            GD.Print($"[MACRO] Saved macros to {_macroFilePath}");

            _state.ChatMessages.Enqueue(new ChatMessage
            {
                Text = "Macros guardados correctamente.",
                Color = "00FF00"
            });
        }
        catch (System.Exception ex)
        {
            GD.PrintErr($"[MACRO] Failed to save macros: {ex.Message}");
        }
    }
}
