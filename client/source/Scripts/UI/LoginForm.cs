using Godot;
using System;
using ArgentumNextgen.Game;

namespace ArgentumNextgen.UI;

/// <summary>
/// RPG-styled login form. Replaces the scene-based LoginPanel.
/// Programmatically created — no .tscn node needed.
/// </summary>
public partial class LoginForm : RpgBaseForm
{
    private readonly GameState _state;
    private readonly string _dataPath;
    private bool _connecting;

    private LineEdit? _accountInput;
    private LineEdit? _passwordInput;
    private TextureButton? _connectButton;
    private Label? _statusLabel;
    private Button? _rememberCheck;

    // Public accessors for Main.cs
    public Label? StatusLabel => _statusLabel;
    public TextureButton? ConnectButton => _connectButton;
    public LineEdit? AccountInput => _accountInput;

    public bool Connecting { get => _connecting; set => _connecting = value; }

    /// <summary>Callback: user wants to connect with (account, password).</summary>
    public Action<string, string>? OnLoginRequest;

    /// <summary>Callback: user wants to create an account.</summary>
    public Action? OnCreateAccountPressed;

    public LoginForm(GameState state, string dataPath)
        : base("Argentum Nextgen", new Vector2(340, 355), "v2")
    {
        _state = state;
        _dataPath = dataPath;
        Draggable = false;
        ShowCloseButton = false;
    }

    protected override void BuildContent()
    {
        var vbox = RpgTheme.CreateColumn(RpgTheme.SpacingLg);
        ContentContainer.AddChild(vbox);

        // Account
        vbox.AddChild(RpgTheme.CreateInfoLabel("Cuenta:", 13));
        _accountInput = RpgTheme.CreateRpgInput("Ingresa tu cuenta...");
        _accountInput.TextSubmitted += (_) => OnConnectPressed();
        vbox.AddChild(_accountInput);

        // Password
        vbox.AddChild(RpgTheme.CreateInfoLabel("Contraseña:", 13));
        _passwordInput = RpgTheme.CreateRpgInput("Ingresa tu contraseña...");
        _passwordInput.Secret = true;
        _passwordInput.TextSubmitted += (_) => OnConnectPressed();
        vbox.AddChild(_passwordInput);

        // Remember check
        var rememberRow = RpgTheme.CreateRpgCheckboxRow("Recordar cuenta", "default", false);
        _rememberCheck = rememberRow.GetChild(1) as Button;
        vbox.AddChild(rememberRow);

        // Buttons
        _connectButton = RpgTheme.CreateRpgButton("Conectar", true, 16);
        _connectButton.CustomMinimumSize = new Vector2(0, 40);
        _connectButton.Pressed += OnConnectPressed;
        vbox.AddChild(_connectButton);

        var crearCuentaBtn = RpgTheme.CreateRpgButton("Crear Cuenta", false, 13);
        crearCuentaBtn.CustomMinimumSize = new Vector2(0, 34);
        crearCuentaBtn.Pressed += () => OnCreateAccountPressed?.Invoke();
        vbox.AddChild(crearCuentaBtn);

        // Status label
        _statusLabel = RpgTheme.CreateInfoLabel("", 12);
        _statusLabel.HorizontalAlignment = HorizontalAlignment.Center;
        _statusLabel.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        vbox.AddChild(_statusLabel);
    }

    public void OnConnectPressed()
    {
        if (_connecting) return;

        string account = _accountInput!.Text.Trim();
        string password = _passwordInput!.Text.Trim();

        if (string.IsNullOrEmpty(account) || string.IsNullOrEmpty(password))
        {
            _statusLabel!.Text = "Ingrese cuenta y contraseña";
            return;
        }

        SaveRememberedAccount(account);

        _state.AccountName = account;
        _state.LoginError = "";
        _connectButton!.Disabled = true;
        _statusLabel!.Text = "Conectando...";

        OnLoginRequest?.Invoke(account, password);
    }

    public void LoadRememberedAccount()
    {
        string path = GetRememberFilePath();
        if (!System.IO.File.Exists(path)) return;

        try
        {
            byte[] encrypted = System.IO.File.ReadAllBytes(path);
            string decrypted = UIHelpers.XorCrypt(encrypted);
            if (!string.IsNullOrEmpty(decrypted))
            {
                _accountInput!.Text = decrypted;
                if (_rememberCheck != null) _rememberCheck.ButtonPressed = true;
            }
        }
        catch (Exception ex)
        {
            GD.Print($"[LOGIN] Failed to load remembered account: {ex.Message}");
        }
    }

    public void SaveRememberedAccount(string account)
    {
        string path = GetRememberFilePath();
        try
        {
            if (_rememberCheck != null && _rememberCheck.ButtonPressed && !string.IsNullOrEmpty(account))
            {
                byte[] encrypted = UIHelpers.XorCrypt(account);
                System.IO.File.WriteAllBytes(path, encrypted);
            }
            else
            {
                if (System.IO.File.Exists(path))
                    System.IO.File.Delete(path);
            }
        }
        catch (Exception ex)
        {
            GD.Print($"[LOGIN] Failed to save remembered account: {ex.Message}");
        }
    }

    public void LoginTimeout()
    {
        GD.PrintErr("[LOGIN] Login timeout");
        _statusLabel!.Text = "Error: El servidor no respondió.";
        _connectButton!.Disabled = false;
    }

    public void FocusAccountInput()
    {
        if (_accountInput == null) return;
        if (!string.IsNullOrEmpty(_accountInput.Text))
            _passwordInput?.GrabFocus();
        else
            _accountInput.GrabFocus();
    }

    private string GetRememberFilePath()
    {
        return System.IO.Path.Combine(_dataPath, UIHelpers.RememberFileName);
    }
}
