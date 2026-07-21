using Godot;
using ArgentumNextgen.Game;
using ArgentumNextgen.Network;

namespace ArgentumNextgen.UI;

/// <summary>
/// Change password panel — 3 fields: current, new, confirm.
/// Sends /PASSWD old@new to server on submit.
/// Now uses RpgBaseForm for consistent RPG styling.
/// </summary>
public partial class ChangePasswordPanel : RpgBaseForm
{
    private GameState? _state;
    private AoTcpClient? _tcp;

    private LineEdit? _currentPasswordInput;
    private LineEdit? _newPasswordInput;
    private LineEdit? _confirmPasswordInput;
    private Label? _errorLabel;

    public ChangePasswordPanel() : base("Cambiar Contraseña", new Vector2(360, 320), "v2") { }

    public void Init(GameState state, AoTcpClient tcp)
    {
        _state = state;
        _tcp = tcp;
    }

    protected override void BuildContent()
    {
        var vbox = RpgTheme.CreateColumn(RpgTheme.SpacingMd);
        ContentContainer.AddChild(vbox);

        // Current password
        vbox.AddChild(RpgTheme.CreateInfoLabel("Contraseña actual:", 12));
        _currentPasswordInput = RpgTheme.CreateRpgInput("Ingresa tu contraseña actual...");
        _currentPasswordInput.Secret = true;
        _currentPasswordInput.MaxLength = 30;
        vbox.AddChild(_currentPasswordInput);

        // New password
        vbox.AddChild(RpgTheme.CreateInfoLabel("Nueva contraseña:", 12));
        _newPasswordInput = RpgTheme.CreateRpgInput("Ingresa la nueva contraseña...");
        _newPasswordInput.Secret = true;
        _newPasswordInput.MaxLength = 30;
        vbox.AddChild(_newPasswordInput);

        // Confirm password
        vbox.AddChild(RpgTheme.CreateInfoLabel("Confirmar nueva contraseña:", 12));
        _confirmPasswordInput = RpgTheme.CreateRpgInput("Repite la nueva contraseña...");
        _confirmPasswordInput.Secret = true;
        _confirmPasswordInput.MaxLength = 30;
        vbox.AddChild(_confirmPasswordInput);

        // Error label
        _errorLabel = RpgTheme.CreateInfoLabel("", 11);
        _errorLabel.AddThemeColorOverride("font_color", new Color(1f, 0.3f, 0.3f));
        vbox.AddChild(_errorLabel);

        // Buttons
        var btnRow = RpgTheme.CreateRow(RpgTheme.SpacingLg);
        btnRow.Alignment = BoxContainer.AlignmentMode.Center;
        vbox.AddChild(btnRow);

        var submitBtn = RpgTheme.CreateRpgButton("Cambiar", false, 13);
        submitBtn.CustomMinimumSize = new Vector2(110, 34);
        submitBtn.Pressed += OnSubmit;
        btnRow.AddChild(submitBtn);

        var cancelBtn = RpgTheme.CreateRpgButton("Cancelar", false, 13);
        cancelBtn.CustomMinimumSize = new Vector2(110, 34);
        cancelBtn.Pressed += () => HideForm();
        btnRow.AddChild(cancelBtn);
    }

    public void Open()
    {
        _currentPasswordInput!.Text = "";
        _newPasswordInput!.Text = "";
        _confirmPasswordInput!.Text = "";
        _errorLabel!.Text = "";
        ShowForm();
        _currentPasswordInput.GrabFocus();
    }

    private void OnSubmit()
    {
        if (_state == null || _tcp == null) return;

        string current = _currentPasswordInput!.Text.Trim();
        string newPass = _newPasswordInput!.Text.Trim();
        string confirm = _confirmPasswordInput!.Text.Trim();

        if (string.IsNullOrEmpty(current))
        { _errorLabel!.Text = "Ingresa tu contraseña actual."; return; }
        if (string.IsNullOrEmpty(newPass))
        { _errorLabel!.Text = "Ingresa la nueva contraseña."; return; }
        if (newPass.Length < 6)
        { _errorLabel!.Text = "La nueva contraseña debe tener al menos 6 caracteres."; return; }
        if (newPass != confirm)
        { _errorLabel!.Text = "Las contraseñas nuevas no coinciden."; return; }
        if (current == newPass)
        { _errorLabel!.Text = "La nueva contraseña debe ser diferente a la actual."; return; }

        _tcp.SendPacket(ClientPackets.WriteTalk($"/PASSWD {current}@{newPass}"));

        _state.EnqueueChat(new ChatMessage
        {
            Text = "Solicitud de cambio de contraseña enviada.",
            Color = "00FF00"
        });

        HideForm();
    }
}
