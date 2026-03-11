using Godot;
using ArgentumNextgen.Game;
using ArgentumNextgen.Network;

namespace ArgentumNextgen.UI;

/// <summary>
/// Change password panel — 3 fields: current, new, confirm.
/// Sends /PASSWD old@new to server on submit.
/// Opened via /PASSWD chat command.
/// </summary>
public partial class ChangePasswordPanel : Control
{
    private const int PanelW = 340;
    private const int PanelH = 260;

    private GameState? _state;
    private AoTcpClient? _tcp;

    // Dragging
    private bool _dragging;
    private Vector2 _dragOffset;

    // Controls
    private LineEdit? _currentPasswordInput;
    private LineEdit? _newPasswordInput;
    private LineEdit? _confirmPasswordInput;
    private Label? _errorLabel;

    public void Init(GameState state, AoTcpClient tcp)
    {
        _state = state;
        _tcp = tcp;
    }

    public override void _Ready()
    {
        Visible = false;
        CustomMinimumSize = new Vector2(PanelW, PanelH);
        Size = new Vector2(PanelW, PanelH);

        // Background
        var bg = new ColorRect();
        bg.Color = new Color(0.08f, 0.08f, 0.12f, 0.95f);
        bg.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        AddChild(bg);

        // Title
        var title = new Label();
        title.Text = "Cambiar Contrasena";
        title.Position = new Vector2(10, 4);
        title.AddThemeFontSizeOverride("font_size", 14);
        title.AddThemeColorOverride("font_color", new Color(1f, 0.85f, 0.4f));
        AddChild(title);

        // Close button
        var closeBtn = new Button();
        closeBtn.Text = "X";
        closeBtn.Position = new Vector2(PanelW - 28, 2);
        closeBtn.Size = new Vector2(24, 24);
        closeBtn.Pressed += OnClose;
        AddChild(closeBtn);

        // Current password
        var currentLabel = new Label();
        currentLabel.Text = "Contrasena actual:";
        currentLabel.Position = new Vector2(16, 36);
        currentLabel.AddThemeFontSizeOverride("font_size", 12);
        AddChild(currentLabel);

        _currentPasswordInput = new LineEdit();
        _currentPasswordInput.Position = new Vector2(16, 56);
        _currentPasswordInput.Size = new Vector2(PanelW - 32, 28);
        _currentPasswordInput.Secret = true;
        _currentPasswordInput.PlaceholderText = "Ingresa tu contrasena actual...";
        _currentPasswordInput.MaxLength = 30;
        AddChild(_currentPasswordInput);

        // New password
        var newLabel = new Label();
        newLabel.Text = "Nueva contrasena:";
        newLabel.Position = new Vector2(16, 92);
        newLabel.AddThemeFontSizeOverride("font_size", 12);
        AddChild(newLabel);

        _newPasswordInput = new LineEdit();
        _newPasswordInput.Position = new Vector2(16, 112);
        _newPasswordInput.Size = new Vector2(PanelW - 32, 28);
        _newPasswordInput.Secret = true;
        _newPasswordInput.PlaceholderText = "Ingresa la nueva contrasena...";
        _newPasswordInput.MaxLength = 30;
        AddChild(_newPasswordInput);

        // Confirm password
        var confirmLabel = new Label();
        confirmLabel.Text = "Confirmar nueva contrasena:";
        confirmLabel.Position = new Vector2(16, 148);
        confirmLabel.AddThemeFontSizeOverride("font_size", 12);
        AddChild(confirmLabel);

        _confirmPasswordInput = new LineEdit();
        _confirmPasswordInput.Position = new Vector2(16, 168);
        _confirmPasswordInput.Size = new Vector2(PanelW - 32, 28);
        _confirmPasswordInput.Secret = true;
        _confirmPasswordInput.PlaceholderText = "Repite la nueva contrasena...";
        _confirmPasswordInput.MaxLength = 30;
        AddChild(_confirmPasswordInput);

        // Error label
        _errorLabel = new Label();
        _errorLabel.Position = new Vector2(16, 200);
        _errorLabel.Size = new Vector2(PanelW - 32, 18);
        _errorLabel.AddThemeFontSizeOverride("font_size", 11);
        _errorLabel.AddThemeColorOverride("font_color", new Color(1f, 0.3f, 0.3f));
        AddChild(_errorLabel);

        // Buttons
        var submitBtn = new Button();
        submitBtn.Text = "Cambiar";
        submitBtn.Position = new Vector2(PanelW / 2 - 110, PanelH - 40);
        submitBtn.Size = new Vector2(100, 30);
        submitBtn.Pressed += OnSubmit;
        AddChild(submitBtn);

        var cancelBtn = new Button();
        cancelBtn.Text = "Cancelar";
        cancelBtn.Position = new Vector2(PanelW / 2 + 10, PanelH - 40);
        cancelBtn.Size = new Vector2(100, 30);
        cancelBtn.Pressed += OnClose;
        AddChild(cancelBtn);
    }

    public void Open()
    {
        _currentPasswordInput!.Text = "";
        _newPasswordInput!.Text = "";
        _confirmPasswordInput!.Text = "";
        _errorLabel!.Text = "";
        Visible = true;

        // Center on screen
        var screenSize = GetViewportRect().Size;
        Position = new Vector2(
            (screenSize.X - PanelW) / 2,
            (screenSize.Y - PanelH) / 2
        );

        _currentPasswordInput.GrabFocus();
    }

    private void OnSubmit()
    {
        if (_state == null || _tcp == null) return;

        string current = _currentPasswordInput!.Text.Trim();
        string newPass = _newPasswordInput!.Text.Trim();
        string confirm = _confirmPasswordInput!.Text.Trim();

        // Validate
        if (string.IsNullOrEmpty(current))
        {
            _errorLabel!.Text = "Ingresa tu contrasena actual.";
            return;
        }
        if (string.IsNullOrEmpty(newPass))
        {
            _errorLabel!.Text = "Ingresa la nueva contrasena.";
            return;
        }
        if (newPass.Length < 6)
        {
            _errorLabel!.Text = "La nueva contrasena debe tener al menos 6 caracteres.";
            return;
        }
        if (newPass != confirm)
        {
            _errorLabel!.Text = "Las contrasenas nuevas no coinciden.";
            return;
        }
        if (current == newPass)
        {
            _errorLabel!.Text = "La nueva contrasena debe ser diferente a la actual.";
            return;
        }

        // Send /PASSWD old@new to server
        _tcp.SendPacket(ClientPackets.WriteTalk($"/PASSWD {current}@{newPass}"));

        _state.ChatMessages.Enqueue(new ChatMessage
        {
            Text = "Solicitud de cambio de contrasena enviada.",
            Color = "00FF00"
        });

        OnClose();
    }

    private void OnClose()
    {
        Visible = false;
    }

    // ── Dragging ─────────────────────────────────────────────────
    public override void _GuiInput(InputEvent @event)
    {
        if (@event is InputEventMouseButton mb)
        {
            if (mb.ButtonIndex == MouseButton.Left)
            {
                if (mb.Pressed && mb.Position.Y < 28)
                {
                    _dragging = true;
                    _dragOffset = mb.Position;
                }
                else
                {
                    _dragging = false;
                }
            }
        }
        else if (@event is InputEventMouseMotion mm && _dragging)
        {
            Position += mm.Position - _dragOffset;
        }
    }
}
