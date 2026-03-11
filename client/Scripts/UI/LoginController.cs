using Godot;
using System;
using System.Threading.Tasks;
using ArgentumNextgen.Game;
using ArgentumNextgen.Network;

namespace ArgentumNextgen.UI;

/// <summary>
/// Login screen controller. Manages the login panel, connection, remembered account.
/// Extracted from Main.cs.
/// </summary>
public class LoginController
{
    private PanelContainer? _loginPanel;
    private LineEdit? _accountInput;
    private LineEdit? _passwordInput;
    private Button? _connectButton;
    private Label? _statusLabel;
    private CheckBox? _rememberCheck;

    private readonly GameState _state;
    private readonly string _dataPath;
    private bool _connecting;

    /// <summary>Whether a connection attempt is in progress.</summary>
    public bool Connecting { get => _connecting; set => _connecting = value; }

    /// <summary>Access to login UI elements for external use.</summary>
    public PanelContainer? Panel => _loginPanel;
    public Label? StatusLabel => _statusLabel;
    public Button? ConnectButton => _connectButton;
    public LineEdit? AccountInput => _accountInput;
    public LineEdit? PasswordInput => _passwordInput;

    /// <summary>Callback: user wants to connect with (account, password).</summary>
    public Action<string, string>? OnLoginRequest;

    public LoginController(GameState state, string dataPath)
    {
        _state = state;
        _dataPath = dataPath;
    }

    /// <summary>Bind to existing scene nodes (LoginPanel already exists in the scene tree).</summary>
    public void BindNodes(PanelContainer loginPanel, LineEdit accountInput, LineEdit passwordInput,
                          Button connectButton, Label statusLabel, CheckBox rememberCheck)
    {
        _loginPanel = loginPanel;
        _accountInput = accountInput;
        _passwordInput = passwordInput;
        _connectButton = connectButton;
        _statusLabel = statusLabel;
        _rememberCheck = rememberCheck;
    }

    public void OnConnectPressed()
    {
        if (_connecting) return;

        string account = _accountInput!.Text.Trim();
        string password = _passwordInput!.Text.Trim();

        if (string.IsNullOrEmpty(account) || string.IsNullOrEmpty(password))
        {
            _statusLabel!.Text = "Ingrese cuenta y password";
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
                _rememberCheck!.ButtonPressed = true;
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

    public void FocusAccountInput()
    {
        if (_accountInput == null) return;
        if (!string.IsNullOrEmpty(_accountInput.Text))
            _passwordInput?.GrabFocus();
        else
            _accountInput.GrabFocus();
    }

    public void LoginTimeout()
    {
        GD.PrintErr("[LOGIN] Login timeout — server did not respond");
        _statusLabel!.Text = "Error: El servidor no respondió.";
        _connectButton!.Disabled = false;
    }

    private string GetRememberFilePath()
    {
        return System.IO.Path.Combine(_dataPath, UIHelpers.RememberFileName);
    }
}
