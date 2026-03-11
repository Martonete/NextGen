using Godot;
using System;
using ArgentumNextgen.Game;
using ArgentumNextgen.Network;

namespace ArgentumNextgen.UI;

/// <summary>
/// Guild MOTD (Message of the Day) editor panel.
/// Allows guild leaders to edit and save the guild MOTD.
/// TextEdit with Save/Cancel buttons.
/// </summary>
public partial class MotdEditorPanel : PanelContainer
{
    private const int PanelW = 380;
    private const int PanelH = 280;
    private const int TitleBarH = 28;

    private GameState? _state;
    private AoTcpClient? _tcp;

    // Dragging
    private bool _dragging;
    private Vector2 _dragOffset;

    // Controls
    private TextEdit? _motdEdit;
    private Label? _statusLabel;

    public void Init(GameState state, AoTcpClient? tcp)
    {
        _state = state;
        _tcp = tcp;
    }

    public void SetTcp(AoTcpClient? tcp) => _tcp = tcp;

    public override void _Ready()
    {
        Visible = false;
        CustomMinimumSize = new Vector2(PanelW, PanelH);
        Size = new Vector2(PanelW, PanelH);

        var style = new StyleBoxFlat();
        style.BgColor = new Color(0.08f, 0.08f, 0.12f, 0.95f);
        style.BorderColor = new Color(0.4f, 0.35f, 0.25f, 1f);
        style.SetBorderWidthAll(2);
        style.SetCornerRadiusAll(3);
        AddThemeStyleboxOverride("panel", style);

        var root = new VBoxContainer();
        root.SetAnchorsPreset(LayoutPreset.FullRect);
        root.AddThemeConstantOverride("separation", 4);
        AddChild(root);

        // Title bar
        var titleBar = new HBoxContainer();
        titleBar.CustomMinimumSize = new Vector2(0, TitleBarH);
        root.AddChild(titleBar);

        var titleLabel = new Label();
        titleLabel.Text = "  Mensaje del Dia (MOTD)";
        titleLabel.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        titleLabel.AddThemeColorOverride("font_color", new Color(0.9f, 0.8f, 0.5f));
        titleLabel.AddThemeFontSizeOverride("font_size", 13);
        titleBar.AddChild(titleLabel);

        var closeBtn = new Button();
        closeBtn.Text = "X";
        closeBtn.CustomMinimumSize = new Vector2(28, 24);
        closeBtn.Pressed += Close;
        titleBar.AddChild(closeBtn);

        // Text editor
        _motdEdit = new TextEdit();
        _motdEdit.SizeFlagsVertical = SizeFlags.ExpandFill;
        _motdEdit.CustomMinimumSize = new Vector2(PanelW - 16, 150);
        _motdEdit.AddThemeFontSizeOverride("font_size", 12);
        _motdEdit.PlaceholderText = "Escriba el mensaje del dia aqui...";
        root.AddChild(_motdEdit);

        // Status label
        _statusLabel = new Label();
        _statusLabel.AddThemeFontSizeOverride("font_size", 10);
        _statusLabel.AddThemeColorOverride("font_color", new Color(0.6f, 0.6f, 0.6f));
        root.AddChild(_statusLabel);

        // Buttons
        var btnRow = new HBoxContainer();
        btnRow.AddThemeConstantOverride("separation", 8);
        root.AddChild(btnRow);

        var saveBtn = new Button();
        saveBtn.Text = "Guardar";
        saveBtn.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        saveBtn.Pressed += OnSavePressed;
        btnRow.AddChild(saveBtn);

        var cancelBtn = new Button();
        cancelBtn.Text = "Cancelar";
        cancelBtn.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        cancelBtn.Pressed += Close;
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
        Visible = true;
    }

    public void Close()
    {
        Visible = false;
    }

    // ── Dragging ──────────────────────────────────────────────

    public override void _GuiInput(InputEvent @event)
    {
        if (@event is InputEventMouseButton mb)
        {
            if (mb.ButtonIndex == MouseButton.Left)
            {
                if (mb.Pressed && mb.Position.Y < TitleBarH)
                {
                    _dragging = true;
                    _dragOffset = mb.Position;
                    AcceptEvent();
                }
                else if (!mb.Pressed)
                    _dragging = false;
            }
        }
        else if (@event is InputEventMouseMotion mm && _dragging)
        {
            Position += mm.Relative;
            AcceptEvent();
        }
    }
}
