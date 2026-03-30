using Godot;
using System;
using ArgentumNextgen.Game;
using ArgentumNextgen.Network;

namespace ArgentumNextgen.UI;

/// <summary>
/// Guild MOTD (Message of the Day) editor panel.
/// Allows guild leaders to edit and save the guild MOTD.
/// TextEdit with Save/Cancel buttons.
/// Now uses RpgBaseForm for consistent RPG styling.
/// </summary>
public partial class MotdEditorPanel : RpgBaseForm
{
    private GameState? _state;
    private AoTcpClient? _tcp;

    // Controls
    private TextEdit? _motdEdit;
    private Label? _statusLabel;

    public MotdEditorPanel() : base("Mensaje del Dia (MOTD)", new Vector2(380, 280), "v2") { }

    public void Init(GameState state, AoTcpClient? tcp)
    {
        _state = state;
        _tcp = tcp;
    }

    public void SetTcp(AoTcpClient? tcp) => _tcp = tcp;

    protected override void BuildContent()
    {
        var vbox = RpgTheme.CreateColumn(RpgTheme.SpacingSm);
        ContentContainer.AddChild(vbox);

        // Text editor
        _motdEdit = RpgTheme.CreateRpgTextEdit("Escriba el mensaje del dia aqui...", 0, 150);
        vbox.AddChild(_motdEdit);

        // Status label
        _statusLabel = RpgTheme.CreateInfoLabel("", 11);
        _statusLabel.AddThemeColorOverride("font_color", new Color(0.6f, 0.6f, 0.6f));
        vbox.AddChild(_statusLabel);

        // Buttons
        var btnRow = RpgTheme.CreateRow(RpgTheme.SpacingLg);
        btnRow.Alignment = BoxContainer.AlignmentMode.Center;
        vbox.AddChild(btnRow);

        var saveBtn = RpgTheme.CreateRpgButton("Guardar", false, 13);
        saveBtn.CustomMinimumSize = new Vector2(110, 34);
        saveBtn.Pressed += OnSavePressed;
        btnRow.AddChild(saveBtn);

        var cancelBtn = RpgTheme.CreateRpgButton("Cancelar", false, 13);
        cancelBtn.CustomMinimumSize = new Vector2(110, 34);
        cancelBtn.Pressed += () => Close();
        btnRow.AddChild(cancelBtn);
    }

    private void OnSavePressed()
    {
        if (_motdEdit == null || _tcp == null) return;
        string text = _motdEdit.Text.Trim();
        if (text.Length == 0)
        {
            if (_statusLabel != null) _statusLabel.Text = "El mensaje no puede estar vacio.";
            return;
        }
        // Send MOTD update via guild news command
        _tcp.SendPacket(ClientPackets.WriteGuildNews(text));
        if (_statusLabel != null) _statusLabel.Text = "MOTD guardado.";
    }

    /// <summary>
    /// Open the MOTD editor, pre-populating with existing MOTD text.
    /// </summary>
    public void Open()
    {
        if (_state != null && _motdEdit != null)
        {
            string existing = _state.GuildMotdText ?? _state.GuildNewsText ?? "";
            _motdEdit.Text = existing;
        }
        if (_statusLabel != null) _statusLabel.Text = "";
        ShowForm();
    }

    public void Close()
    {
        HideForm();
    }
}
