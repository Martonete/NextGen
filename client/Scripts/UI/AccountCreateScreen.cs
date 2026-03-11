using Godot;
using System;
using ArgentumNextgen.Game;

namespace ArgentumNextgen.UI;

/// <summary>
/// Account creation UI panel. Extracted from Main.cs.
/// Handles all account creation form logic, validation, and TCP connection for NACCNT.
/// </summary>
public class AccountCreateScreen
{
    private PanelContainer? _panel;
    private LineEdit? _nameInput;
    private LineEdit? _passwordInput;
    private LineEdit? _passwordConfirmInput;
    private LineEdit? _pinInput;
    private LineEdit? _pinConfirmInput;
    private Label? _errorLabel;
    private Button? _createButton;
    private Button? _backButton;

    private readonly GameState _state;

    /// <summary>The root panel container (for show/hide from Main).</summary>
    public PanelContainer? Panel => _panel;

    /// <summary>Timer for auto-switching back to login after successful creation.</summary>
    public double SuccessTimer { get; set; }

    /// <summary>Callback: request TCP connection + send NACCNT(account, password, pin).</summary>
    public Action<string, string, string>? OnCreateAccount;

    /// <summary>Callback: user pressed Back.</summary>
    public Action? OnBack;

    public AccountCreateScreen(GameState state)
    {
        _state = state;
    }

    /// <summary>
    /// Build the account creation panel and add it to the parent node.
    /// </summary>
    public void CreatePanel(Node parent)
    {
        _panel = new PanelContainer();
        _panel.Size = new Vector2(400, 420);
        _panel.Position = new Vector2(200, 90);
        _panel.Visible = false;
        _panel.ZIndex = 1;

        var bg = new StyleBoxFlat();
        bg.BgColor = new Color(0.08f, 0.08f, 0.14f, 0.95f);
        bg.BorderColor = new Color(0.4f, 0.35f, 0.2f);
        bg.SetBorderWidthAll(2);
        bg.SetContentMarginAll(12);
        _panel.AddThemeStyleboxOverride("panel", bg);

        var vbox = new VBoxContainer();
        vbox.AddThemeConstantOverride("separation", 6);
        _panel.AddChild(vbox);

        // Title
        var title = new Label();
        title.Text = "Crear Cuenta";
        title.HorizontalAlignment = HorizontalAlignment.Center;
        title.AddThemeColorOverride("font_color", new Color(0.9f, 0.8f, 0.4f));
        title.AddThemeFontSizeOverride("font_size", 16);
        UIHelpers.ApplyFont(title);
        vbox.AddChild(title);

        // Account name
        var nameLabel = new Label();
        nameLabel.Text = "Nombre de cuenta:";
        nameLabel.AddThemeColorOverride("font_color", Colors.White);
        nameLabel.AddThemeFontSizeOverride("font_size", 11);
        vbox.AddChild(nameLabel);

        _nameInput = new LineEdit();
        _nameInput.PlaceholderText = "3-15 caracteres";
        _nameInput.MaxLength = 15;
        _nameInput.CustomMinimumSize = new Vector2(0, 28);
        _nameInput.AddThemeFontSizeOverride("font_size", 12);
        vbox.AddChild(_nameInput);

        // Password
        var passLabel = new Label();
        passLabel.Text = "Contraseña:";
        passLabel.AddThemeColorOverride("font_color", Colors.White);
        passLabel.AddThemeFontSizeOverride("font_size", 11);
        vbox.AddChild(passLabel);

        _passwordInput = new LineEdit();
        _passwordInput.PlaceholderText = "4-15 caracteres";
        _passwordInput.MaxLength = 15;
        _passwordInput.Secret = true;
        _passwordInput.CustomMinimumSize = new Vector2(0, 28);
        _passwordInput.AddThemeFontSizeOverride("font_size", 12);
        vbox.AddChild(_passwordInput);

        // Confirm password
        var passConfirmLabel = new Label();
        passConfirmLabel.Text = "Repetir contraseña:";
        passConfirmLabel.AddThemeColorOverride("font_color", Colors.White);
        passConfirmLabel.AddThemeFontSizeOverride("font_size", 11);
        vbox.AddChild(passConfirmLabel);

        _passwordConfirmInput = new LineEdit();
        _passwordConfirmInput.MaxLength = 15;
        _passwordConfirmInput.Secret = true;
        _passwordConfirmInput.CustomMinimumSize = new Vector2(0, 28);
        _passwordConfirmInput.AddThemeFontSizeOverride("font_size", 12);
        vbox.AddChild(_passwordConfirmInput);

        // PIN
        var pinLabel = new Label();
        pinLabel.Text = "PIN:";
        pinLabel.AddThemeColorOverride("font_color", Colors.White);
        pinLabel.AddThemeFontSizeOverride("font_size", 11);
        vbox.AddChild(pinLabel);

        _pinInput = new LineEdit();
        _pinInput.PlaceholderText = "4-5 dígitos";
        _pinInput.MaxLength = 5;
        _pinInput.Secret = true;
        _pinInput.CustomMinimumSize = new Vector2(0, 28);
        _pinInput.AddThemeFontSizeOverride("font_size", 12);
        vbox.AddChild(_pinInput);

        // Confirm PIN
        var pinConfirmLabel = new Label();
        pinConfirmLabel.Text = "Repetir PIN:";
        pinConfirmLabel.AddThemeColorOverride("font_color", Colors.White);
        pinConfirmLabel.AddThemeFontSizeOverride("font_size", 11);
        vbox.AddChild(pinConfirmLabel);

        _pinConfirmInput = new LineEdit();
        _pinConfirmInput.MaxLength = 5;
        _pinConfirmInput.Secret = true;
        _pinConfirmInput.CustomMinimumSize = new Vector2(0, 28);
        _pinConfirmInput.AddThemeFontSizeOverride("font_size", 12);
        vbox.AddChild(_pinConfirmInput);

        // Error/status label
        _errorLabel = new Label();
        _errorLabel.Text = "";
        _errorLabel.HorizontalAlignment = HorizontalAlignment.Center;
        _errorLabel.AddThemeColorOverride("font_color", new Color(1f, 0.4f, 0.4f));
        _errorLabel.AddThemeFontSizeOverride("font_size", 11);
        _errorLabel.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        vbox.AddChild(_errorLabel);

        // Buttons row
        var btnBox = new HBoxContainer();
        btnBox.AddThemeConstantOverride("separation", 8);
        btnBox.Alignment = BoxContainer.AlignmentMode.Center;
        vbox.AddChild(btnBox);

        _createButton = new Button();
        _createButton.Text = "Crear Cuenta";
        _createButton.CustomMinimumSize = new Vector2(140, 32);
        _createButton.Pressed += OnCreatePressed;
        btnBox.AddChild(_createButton);

        _backButton = new Button();
        _backButton.Text = "Volver";
        _backButton.CustomMinimumSize = new Vector2(100, 32);
        _backButton.Pressed += () => OnBack?.Invoke();
        btnBox.AddChild(_backButton);

        parent.AddChild(_panel);
    }

    /// <summary>Show an error or success message from the server.</summary>
    public void ShowError(string message, bool isSuccess = false)
    {
        if (_errorLabel == null) return;
        _errorLabel.Text = message;
        _errorLabel.AddThemeColorOverride("font_color",
            isSuccess ? new Color(0.4f, 1f, 0.4f) : new Color(1f, 0.4f, 0.4f));
    }

    /// <summary>Re-enable the create button (after error).</summary>
    public void EnableCreateButton()
    {
        if (_createButton != null) _createButton.Disabled = false;
    }

    public void ResetForm()
    {
        _nameInput!.Text = "";
        _passwordInput!.Text = "";
        _passwordConfirmInput!.Text = "";
        _pinInput!.Text = "";
        _pinConfirmInput!.Text = "";
        _errorLabel!.Text = "";
        _errorLabel.AddThemeColorOverride("font_color", new Color(1f, 0.4f, 0.4f));
        _createButton!.Disabled = false;
        SuccessTimer = 0;
    }

    private void OnCreatePressed()
    {
        string name = _nameInput!.Text.Trim();
        string pass = _passwordInput!.Text;
        string passConfirm = _passwordConfirmInput!.Text;
        string pin = _pinInput!.Text;
        string pinConfirm = _pinConfirmInput!.Text;

        // Validate account name
        if (name.Length < 3 || name.Length > 15)
        {
            _errorLabel!.Text = "El nombre debe tener entre 3 y 15 caracteres.";
            return;
        }
        foreach (char c in name)
        {
            if (!char.IsLetterOrDigit(c))
            {
                _errorLabel!.Text = "El nombre solo puede contener letras y números.";
                return;
            }
        }

        // Validate password
        if (pass.Length < 4 || pass.Length > 15)
        {
            _errorLabel!.Text = "La contraseña debe tener entre 4 y 15 caracteres.";
            return;
        }
        if (pass != passConfirm)
        {
            _errorLabel!.Text = "Las contraseñas no coinciden.";
            return;
        }

        // Validate PIN
        if (pin.Length < 4 || pin.Length > 5)
        {
            _errorLabel!.Text = "El PIN debe tener 4 o 5 dígitos.";
            return;
        }
        foreach (char c in pin)
        {
            if (!char.IsDigit(c))
            {
                _errorLabel!.Text = "El PIN solo puede contener dígitos.";
                return;
            }
        }
        if (pin != pinConfirm)
        {
            _errorLabel!.Text = "Los PINs no coinciden.";
            return;
        }

        _createButton!.Disabled = true;
        _errorLabel!.Text = "Conectando...";
        _errorLabel.AddThemeColorOverride("font_color", new Color(0.8f, 0.8f, 0.8f));
        SuccessTimer = 0;

        _state.CreateAccountName = name;
        _state.CreateAccountPassword = pass;
        _state.CreateAccountPin = pin;

        OnCreateAccount?.Invoke(name, pass, pin);
    }
}
