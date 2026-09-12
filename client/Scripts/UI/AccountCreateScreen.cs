using Godot;
using System;
using ArgentumNextgen.Game;

namespace ArgentumNextgen.UI;

/// <summary>
/// Account creation UI panel. Extracted from Main.cs.
/// Handles all account creation form logic, validation, and TCP connection for CreateAccount.
/// Styled with RpgTheme.
/// </summary>
public class AccountCreateScreen
{
    private Control? _panel;
    private LineEdit? _nameInput;
    private LineEdit? _passwordInput;
    private LineEdit? _passwordConfirmInput;
    private LineEdit? _pinInput;
    private LineEdit? _pinConfirmInput;
    private Label? _errorLabel;
    private Button? _createButton;

    private readonly GameState _state;

    /// <summary>The root panel control (for show/hide from Main).</summary>
    public Control? Panel => _panel;

    /// <summary>Timer for auto-switching back to login after successful creation.</summary>
    public double SuccessTimer { get; set; }

    /// <summary>Callback: request TCP connection + send CreateAccount(account, password, pin).</summary>
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
        _panel = new Control { Size = new Vector2(600, 630), CustomMinimumSize = new Vector2(600, 630),
            Visible = false, ZIndex = 1, MouseFilter = Control.MouseFilterEnum.Stop };
        _panel.Scale = Vector2.One * RpgBaseForm.FormScale;
        var frame = SacredTheme.Frame(true);
        _panel.AddChild(frame);
        RpgTheme.FillParent(frame);
        var margin = new MarginContainer();
        foreach (string edge in new[] { "left", "right", "top", "bottom" })
            margin.AddThemeConstantOverride("margin_" + edge, 28);
        _panel.AddChild(margin);
        RpgTheme.FillParent(margin);
        var root = RpgTheme.CreateColumn(12);
        margin.AddChild(root);
        root.AddChild(EntryTheme.Header("EL PRIMER CAPÍTULO  /  NUEVA CUENTA", "Tu leyenda empieza acá",
            "Creá tu acceso al reino. Después elegirás a tu personaje."));
        LineEdit Field(VBoxContainer target, string label, string hint, int max, bool secret = false)
        {
            target.AddChild(EntryTheme.Text(label, 11, true));
            var input = EntryTheme.Input(hint);
            input.MaxLength = max;
            input.Secret = secret;
            input.CustomMinimumSize = new Vector2(0, 36);
            target.AddChild(input);
            return input;
        }
        _nameInput = Field(root, "NOMBRE DE CUENTA", "De 3 a 15 letras o números", 15);
        var row = RpgTheme.CreateRow(16);
        root.AddChild(row);
        var left = RpgTheme.CreateColumn(6);
        var right = RpgTheme.CreateColumn(6);
        left.SizeFlagsHorizontal = right.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        row.AddChild(left); row.AddChild(right);
        _passwordInput = Field(left, "CONTRASEÑA", "De 4 a 15 caracteres", 15, true);
        _passwordConfirmInput = Field(right, "REPETIR CONTRASEÑA", "La misma contraseña", 15, true);
        _pinInput = Field(left, "PIN DE SEGURIDAD", "4 o 5 dígitos", 5, true);
        _pinConfirmInput = Field(right, "REPETIR PIN", "El mismo PIN", 5, true);
        _nameInput.TextSubmitted += _ => _passwordInput.GrabFocus();
        _passwordInput.TextSubmitted += _ => _passwordConfirmInput.GrabFocus();
        _passwordConfirmInput.TextSubmitted += _ => _pinInput.GrabFocus();
        _pinInput.TextSubmitted += _ => _pinConfirmInput.GrabFocus();
        _pinConfirmInput.TextSubmitted += _ => OnCreatePressed();
        var hint = EntryTheme.Text("Guardá tu PIN: lo vas a necesitar para gestionar tus personajes.", 12, true);
        hint.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        root.AddChild(hint);
        _errorLabel = EntryTheme.Text("", 13);
        _errorLabel.AddThemeColorOverride("font_color", SacredTheme.Danger);
        _errorLabel.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _errorLabel.CustomMinimumSize = new Vector2(0, 22);
        root.AddChild(_errorLabel);
        var footer = RpgTheme.CreateRow(12);
        root.AddChild(footer);
        var back = EntryTheme.Button("Volver");
        back.CustomMinimumSize = new Vector2(100, 38);
        back.Pressed += () => OnBack?.Invoke();
        footer.AddChild(back);
        _createButton = EntryTheme.Button("FUNDAR MI LEGADO  ›", true);
        _createButton.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        _createButton.Pressed += OnCreatePressed;
        footer.AddChild(_createButton);
        _panel.VisibilityChanged += () =>
        {
            if (!_panel.Visible) return;
            var area = _panel.GetViewportRect().Size;
            float fit = Math.Min(RpgBaseForm.FormScale, Math.Min((area.X - 24) / _panel.Size.X, (area.Y - 24) / _panel.Size.Y));
            _panel.Scale = Vector2.One * Math.Max(.5f, fit);
            _panel.Position = (area - _panel.Size * _panel.Scale) / 2;
            _nameInput.GrabFocus();
        };
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
        if (_createButton == null || _createButton.Disabled) return;
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
