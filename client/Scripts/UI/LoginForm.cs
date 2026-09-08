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
    private Button? _connectButton;
    private Label? _statusLabel;
    private Button? _rememberCheck;

    // Public accessors for Main.cs
    public Label? StatusLabel => _statusLabel;
    public Button? ConnectButton => _connectButton;
    public LineEdit? AccountInput => _accountInput;

    public bool Connecting { get => _connecting; set => _connecting = value; }

    /// <summary>Callback: user wants to connect with (account, password).</summary>
    public Action<string, string>? OnLoginRequest;

    /// <summary>Callback: user wants to create an account.</summary>
    public Action? OnCreateAccountPressed;

    public LoginForm(GameState state, string dataPath)
        : base("Argentum Nextgen", new Vector2(390, 520), "entry")
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
        vbox.AddChild(EntryTheme.Header("ARGENTUM NEXTGEN  /  TIERRAS SAGRADAS", "Tu aventura continúa", "Ingresá a tu cuenta para volver al mundo."));

        // Account
        vbox.AddChild(EntryTheme.Text("CUENTA", 11));
        _accountInput = EntryTheme.Input("Nombre de cuenta");
        _accountInput.TextSubmitted += (_) => _passwordInput?.GrabFocus();
        vbox.AddChild(_accountInput);

        // Password
        vbox.AddChild(EntryTheme.Text("CONTRASEÑA", 11));
        _passwordInput = EntryTheme.Input("Tu contraseña");
        _passwordInput.Secret = true;
        _passwordInput.TextSubmitted += (_) => OnConnectPressed();
        vbox.AddChild(_passwordInput);

        // Remember check
        var remember = new CheckButton { Text = "Recordar mi cuenta" };
        remember.AddThemeFontSizeOverride("font_size", 12);
        _rememberCheck = remember;
        vbox.AddChild(remember);

        // Buttons
        _connectButton = EntryTheme.Button("Ingresar a mi cuenta", true);
        _connectButton.CustomMinimumSize = new Vector2(0, 40);
        _connectButton.Pressed += OnConnectPressed;
        vbox.AddChild(_connectButton);

        var crearCuentaBtn = EntryTheme.Button("Crear una cuenta");
        crearCuentaBtn.CustomMinimumSize = new Vector2(0, 34);
        crearCuentaBtn.Pressed += () => OnCreateAccountPressed?.Invoke();
        vbox.AddChild(crearCuentaBtn);

        // Status label
        _statusLabel = RpgTheme.CreateInfoLabel("", 12);
        _statusLabel.HorizontalAlignment = HorizontalAlignment.Center;
        _statusLabel.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _statusLabel.CustomMinimumSize = new Vector2(0, 30);
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
