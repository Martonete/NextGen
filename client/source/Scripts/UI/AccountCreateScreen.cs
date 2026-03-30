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
    private TextureButton? _createButton;

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
        // --- Root panel with NinePatch frame ---
        _panel = new Control();
        _panel.Size = new Vector2(400, 480);
        _panel.CustomMinimumSize = new Vector2(400, 480);
        _panel.Visible = false;
        _panel.ZIndex = 1;
        _panel.ClipContents = true;
        _panel.MouseFilter = Control.MouseFilterEnum.Stop;
        float fs = RpgBaseForm.FormScale;
        _panel.Scale = new Vector2(fs, fs);

        // V2 background: big_bar stretched
        var bg = new TextureRect();
        bg.Texture = RpgTheme.GetTex("big_bar.png");
        bg.ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize;
        bg.StretchMode = TextureRect.StretchModeEnum.Scale;
        bg.MouseFilter = Control.MouseFilterEnum.Ignore;
        _panel.AddChild(bg);
        RpgTheme.FillParent(bg);

        // V2 title bar
        var titleBg = RpgTheme.CreateNinePatch("name_frame_mid_ready.png", new Vector4(30, 10, 30, 10));
        _panel.AddChild(titleBg);
        titleBg.AnchorLeft = 0f; titleBg.AnchorRight = 1f;
        titleBg.AnchorTop = 0f;  titleBg.AnchorBottom = 0f;
        titleBg.OffsetLeft = 10; titleBg.OffsetTop = 5;
        titleBg.OffsetRight = -10; titleBg.OffsetBottom = 48;

        var titleLabel = RpgTheme.CreateTitleLabel("Crear Cuenta", 18);
        titleBg.AddChild(titleLabel);
        RpgTheme.FillParent(titleLabel);
        titleLabel.OffsetTop = 4; titleLabel.OffsetBottom = -4;

        // Content area with V2 margins
        var marginC = new MarginContainer();
        marginC.AddThemeConstantOverride("margin_top", 54);
        marginC.AddThemeConstantOverride("margin_left", 36);
        marginC.AddThemeConstantOverride("margin_right", 36);
        marginC.AddThemeConstantOverride("margin_bottom", 38);
        marginC.MouseFilter = Control.MouseFilterEnum.Ignore;
        _panel.AddChild(marginC);
        RpgTheme.FillParent(marginC);

        // Root column: scrollable content + fixed footer
        var root = RpgTheme.CreateColumn(RpgTheme.SpacingSm);
        root.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
        marginC.AddChild(root);

        var scrollArea = RpgTheme.CreateScrollArea(RpgTheme.SpacingMd);
        root.AddChild(scrollArea);
        var vbox = scrollArea.GetMeta("content").As<VBoxContainer>();

        // Account name
        var nameLabel = RpgTheme.CreateInfoLabel("Nombre de cuenta:", 11);
        vbox.AddChild(nameLabel);

        _nameInput = RpgTheme.CreateRpgInput("3-15 caracteres");
        _nameInput.MaxLength = 15;
        vbox.AddChild(_nameInput);

        // Password
        var passLabel = RpgTheme.CreateInfoLabel("Contraseña:", 11);
        vbox.AddChild(passLabel);

        _passwordInput = RpgTheme.CreateRpgInput("4-15 caracteres");
        _passwordInput.MaxLength = 15;
        _passwordInput.Secret = true;
        vbox.AddChild(_passwordInput);

        // Confirm password
        var passConfirmLabel = RpgTheme.CreateInfoLabel("Repetir contraseña:", 11);
        vbox.AddChild(passConfirmLabel);

        _passwordConfirmInput = RpgTheme.CreateRpgInput();
        _passwordConfirmInput.MaxLength = 15;
        _passwordConfirmInput.Secret = true;
        vbox.AddChild(_passwordConfirmInput);

        // PIN
        var pinLabel = RpgTheme.CreateInfoLabel("PIN:", 11);
        vbox.AddChild(pinLabel);

        _pinInput = RpgTheme.CreateRpgInput("4-5 dígitos");
        _pinInput.MaxLength = 5;
        _pinInput.Secret = true;
        vbox.AddChild(_pinInput);

        // Confirm PIN
        var pinConfirmLabel = RpgTheme.CreateInfoLabel("Repetir PIN:", 11);
        vbox.AddChild(pinConfirmLabel);

        _pinConfirmInput = RpgTheme.CreateRpgInput();
        _pinConfirmInput.MaxLength = 5;
        _pinConfirmInput.Secret = true;
        vbox.AddChild(_pinConfirmInput);

        // Error/status label
        _errorLabel = new Label();
        _errorLabel.Text = "";
        _errorLabel.HorizontalAlignment = HorizontalAlignment.Center;
        _errorLabel.AddThemeColorOverride("font_color", new Color(1f, 0.4f, 0.4f));
        _errorLabel.AddThemeFontSizeOverride("font_size", 11);
        _errorLabel.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        vbox.AddChild(_errorLabel);

        // Footer row — fixed at bottom, outside scroll
        var footer = RpgTheme.CreateFooterRow(RpgTheme.SpacingLg);
        root.AddChild(footer);
        var btnRow = footer.GetMeta("row").As<HBoxContainer>();

        var backButton = RpgTheme.CreateRpgButton("Volver", false, 14);
        backButton.CustomMinimumSize = new Vector2(100, 36);
        backButton.Pressed += () => OnBack?.Invoke();
        btnRow.AddChild(backButton);

        _createButton = RpgTheme.CreateRpgButton("Crear Cuenta", false, 14);
        _createButton.CustomMinimumSize = new Vector2(140, 36);
        _createButton.Pressed += OnCreatePressed;
        btnRow.AddChild(_createButton);

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
