using Godot;
using System;
using ArgentumNextgen.Game;
using ArgentumNextgen.Network;

namespace ArgentumNextgen.UI;

/// <summary>
/// Guild member detail panel — shows detailed info about a guild member.
/// Accept/reject/kick buttons for guild leaders. Character stats display.
/// Opened from GuildPanel when clicking a member or applicant.
/// </summary>
public partial class GuildMemberPanel : PanelContainer
{
    private const int PanelW = 320;
    private const int PanelH = 320;
    private const int TitleBarH = 28;

    private GameState? _state;
    private AoTcpClient? _tcp;

    // Dragging
    private bool _dragging;
    private Vector2 _dragOffset;

    // Controls
    private Label? _nameLabel;
    private Label? _detailLabel;
    private Button? _acceptBtn;
    private Button? _rejectBtn;
    private Button? _kickBtn;
    private LineEdit? _commentEdit;
    private Button? _commentBtn;

    // Current member info
    private string _memberName = "";
    private bool _isApplicant;

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
        root.AddThemeConstantOverride("separation", 6);
        AddChild(root);

        // Title bar
        var titleBar = new HBoxContainer();
        titleBar.CustomMinimumSize = new Vector2(0, TitleBarH);
        root.AddChild(titleBar);

        _nameLabel = new Label();
        _nameLabel.Text = "  Detalle de Miembro";
        _nameLabel.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        _nameLabel.AddThemeColorOverride("font_color", new Color(0.9f, 0.8f, 0.5f));
        _nameLabel.AddThemeFontSizeOverride("font_size", 13);
        titleBar.AddChild(_nameLabel);

        var closeBtn = new Button();
        closeBtn.Text = "X";
        closeBtn.CustomMinimumSize = new Vector2(28, 24);
        closeBtn.Pressed += () => Visible = false;
        titleBar.AddChild(closeBtn);

        // Detail info
        _detailLabel = new Label();
        _detailLabel.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _detailLabel.CustomMinimumSize = new Vector2(PanelW - 16, 80);
        _detailLabel.AddThemeFontSizeOverride("font_size", 11);
        _detailLabel.AddThemeColorOverride("font_color", Colors.White);
        root.AddChild(_detailLabel);

        // Comment section (for applicants)
        var commentLabel = new Label();
        commentLabel.Text = "Comentario:";
        commentLabel.AddThemeFontSizeOverride("font_size", 11);
        commentLabel.AddThemeColorOverride("font_color", new Color(0.8f, 0.75f, 0.6f));
        root.AddChild(commentLabel);

        _commentEdit = new LineEdit();
        _commentEdit.PlaceholderText = "Escriba un comentario...";
        root.AddChild(_commentEdit);

        // Action buttons
        var btnRow = new HBoxContainer();
        btnRow.AddThemeConstantOverride("separation", 4);
        root.AddChild(btnRow);

        _acceptBtn = new Button();
        _acceptBtn.Text = "Aceptar";
        _acceptBtn.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        _acceptBtn.AddThemeColorOverride("font_color", new Color(0.3f, 1f, 0.3f));
        _acceptBtn.Pressed += OnAcceptPressed;
        btnRow.AddChild(_acceptBtn);

        _rejectBtn = new Button();
        _rejectBtn.Text = "Rechazar";
        _rejectBtn.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        _rejectBtn.AddThemeColorOverride("font_color", new Color(1f, 0.3f, 0.3f));
        _rejectBtn.Pressed += OnRejectPressed;
        btnRow.AddChild(_rejectBtn);

        _kickBtn = new Button();
        _kickBtn.Text = "Expulsar";
        _kickBtn.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        _kickBtn.AddThemeColorOverride("font_color", new Color(1f, 0.5f, 0.2f));
        _kickBtn.Pressed += OnKickPressed;
        btnRow.AddChild(_kickBtn);

        // View info button
        var viewBtn = new Button();
        viewBtn.Text = "Ver Info (/MIRAR)";
        viewBtn.Pressed += () =>
        {
            if (_memberName.Length > 0 && _tcp != null)
                _tcp.SendPacket(ClientPackets.WriteTalk($"/MIRAR {_memberName}"));
        };
        root.AddChild(viewBtn);
    }

    /// <summary>
    /// Show member detail for a guild member (not applicant).
    /// Leader can kick, not accept/reject.
    /// </summary>
    public void ShowMember(string memberName)
    {
        _memberName = memberName;
        _isApplicant = false;
        if (_nameLabel != null) _nameLabel.Text = $"  {memberName}";
        if (_detailLabel != null)
            _detailLabel.Text = $"Miembro del clan: {memberName}\n\nUse /MIRAR para ver informacion detallada.";
        if (_acceptBtn != null) _acceptBtn.Visible = false;
        if (_rejectBtn != null) _rejectBtn.Visible = false;
        if (_kickBtn != null) _kickBtn.Visible = true;
        Visible = true;
    }

    /// <summary>
    /// Show member detail for an applicant. Leader can accept/reject.
    /// </summary>
    public void ShowApplicant(string applicantName, string petition)
    {
        _memberName = applicantName;
        _isApplicant = true;
        if (_nameLabel != null) _nameLabel.Text = $"  Solicitud: {applicantName}";
        if (_detailLabel != null)
            _detailLabel.Text = $"Solicitud de: {applicantName}\n\nMensaje: {petition}";
        if (_acceptBtn != null) _acceptBtn.Visible = true;
        if (_rejectBtn != null) _rejectBtn.Visible = true;
        if (_kickBtn != null) _kickBtn.Visible = false;
        Visible = true;
    }

    private void OnAcceptPressed()
    {
        if (_tcp == null || _memberName.Length == 0) return;
        _tcp.SendPacket(ClientPackets.WriteGuildAccept(_memberName));
        Visible = false;
    }

    private void OnRejectPressed()
    {
        if (_tcp == null || _memberName.Length == 0) return;
        string comment = _commentEdit?.Text.Trim() ?? "Rechazado";
        if (comment.Length == 0) comment = "Rechazado";
        _tcp.SendPacket(ClientPackets.WriteGuildReject($"{_memberName},{comment}"));
        Visible = false;
    }

    private void OnKickPressed()
    {
        if (_tcp == null || _memberName.Length == 0) return;
        _tcp.SendPacket(ClientPackets.WriteGuildExpel(_memberName));
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
