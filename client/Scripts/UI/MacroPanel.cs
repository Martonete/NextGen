using Godot;
using ArgentumNextgen.Game;

namespace ArgentumNextgen.UI;

/// <summary>
/// VB6 frmMakro — Macro configuration panel.
/// 10 text inputs for keys 1-0, save/cancel buttons.
/// Now uses RpgBaseForm for consistent RPG styling.
/// </summary>
public partial class MacroPanel : RpgBaseForm
{
    private GameState? _state;
    private LineEdit[] _inputs = new LineEdit[10];
    private string _macroFilePath = "";

    public MacroPanel() : base("Configurar Macros", new Vector2(300, 420), "v2") { }

    public void Init(GameState state, string dataPath)
    {
        _state = state;
        _macroFilePath = System.IO.Path.Combine(dataPath, "INIT", "Macro.ao");
        LoadMacros();
    }

    protected override void BuildContent()
    {
        var vbox = RpgTheme.CreateColumn(RpgTheme.SpacingSm);
        ContentContainer.AddChild(vbox);

        // 10 macro inputs (keys 1,2,3...9,0)
        string[] keyLabels = { "1", "2", "3", "4", "5", "6", "7", "8", "9", "0" };
        for (int i = 0; i < 10; i++)
        {
            var row = RpgTheme.CreateRow(RpgTheme.SpacingMd);

            var label = RpgTheme.CreateInfoLabel($"Tecla {keyLabels[i]}:", 12);
            RpgTheme.SetMinW(label, 65);
            row.AddChild(label);

            var input = RpgTheme.CreateRpgInput("Comando...", 160);
            _inputs[i] = input;
            row.AddChild(input);

            vbox.AddChild(row);
        }

        vbox.AddChild(RpgTheme.CreateSpacer(8));

        // Buttons
        var btnRow = RpgTheme.CreateRow(RpgTheme.SpacingLg);
        btnRow.Alignment = BoxContainer.AlignmentMode.Center;
        vbox.AddChild(btnRow);

        var saveBtn = RpgTheme.CreateRpgButton("Grabar", false, 13);
        saveBtn.CustomMinimumSize = new Vector2(100, 34);
        saveBtn.Pressed += OnSave;
        btnRow.AddChild(saveBtn);

        var cancelBtn = RpgTheme.CreateRpgButton("Cancelar", false, 13);
        cancelBtn.CustomMinimumSize = new Vector2(100, 34);
        cancelBtn.Pressed += () => Close();
        btnRow.AddChild(cancelBtn);
    }

    public void Open()
    {
        if (_state == null) return;
        for (int i = 0; i < 10; i++)
            _inputs[i].Text = _state.Macros[i] ?? "";
        _state.MacroPanelOpen = true;
        ShowForm();
        _inputs[0].GrabFocus();
    }

    public override void HideForm()
    {
        if (_state != null)
            _state.MacroPanelOpen = false;
        base.HideForm();
    }

    public void Close() => HideForm();

    private void OnSave()
    {
        if (_state == null) return;
        for (int i = 0; i < 10; i++)
            _state.Macros[i] = _inputs[i].Text.Trim();
        SaveMacros();
        Close();
    }

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
                { inSection = true; continue; }
                if (trimmed.StartsWith("[")) { inSection = false; continue; }
                if (!inSection) continue;

                int eq = trimmed.IndexOf('=');
                if (eq < 0) continue;
                string key = trimmed[..eq].Trim();
                string val = trimmed[(eq + 1)..].Trim();

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
