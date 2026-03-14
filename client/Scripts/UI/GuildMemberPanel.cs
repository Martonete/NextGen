using Godot;
using System;
using ArgentumNextgen.Game;
using ArgentumNextgen.Network;

namespace ArgentumNextgen.UI;

/// <summary>
/// Guild member detail panel — shows detailed info about a guild member.
/// Accept/reject/kick buttons for guild leaders. Character stats display.
/// Opened from GuildPanel when clicking a member or applicant.
/// Now uses RpgBaseForm for consistent RPG styling.
/// </summary>
public partial class GuildMemberPanel : RpgBaseForm
{
    private GameState? _state;
    private AoTcpClient? _tcp;

    // Controls
    private Label? _detailLabel;
    private TextureButton? _acceptBtn;
    private TextureButton? _rejectBtn;
    private TextureButton? _kickBtn;
    private LineEdit? _commentEdit;

    // Current member info
    private string _memberName = "";
    private bool _isApplicant;

    public GuildMemberPanel() : base("Detalle de Miembro", new Vector2(320, 340), "v2") { }

    public void Init(GameState state, AoTcpClient? tcp)
    {
        _state = state;
        _tcp = tcp;
    }

    public void SetTcp(AoTcpClient? tcp) => _tcp = tcp;

    protected override void BuildContent()
    {
        var vbox = RpgTheme.CreateColumn(RpgTheme.SpacingMd);
        ContentContainer.AddChild(vbox);

        // Detail info
        _detailLabel = RpgTheme.CreateInfoLabel("", 12);
        _detailLabel.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _detailLabel.CustomMinimumSize = new Vector2(0, 80);
        vbox.AddChild(_detailLabel);

        // Comment section (for applicants)
        vbox.AddChild(RpgTheme.CreateInfoLabel("Comentario:", 12));
        _commentEdit = RpgTheme.CreateRpgInput("Escriba un comentario...");
        vbox.AddChild(_commentEdit);

        // Action buttons
        var btnRow = RpgTheme.CreateRow(RpgTheme.SpacingSm);
        btnRow.Alignment = BoxContainer.AlignmentMode.Center;
        vbox.AddChild(btnRow);

        _acceptBtn = RpgTheme.CreateRpgButton("Aceptar", false, 12);
        _acceptBtn.CustomMinimumSize = new Vector2(80, 30);
        _acceptBtn.Pressed += OnAcceptPressed;
        btnRow.AddChild(_acceptBtn);

        _rejectBtn = RpgTheme.CreateRpgButton("Rechazar", false, 12);
        _rejectBtn.CustomMinimumSize = new Vector2(80, 30);
        _rejectBtn.Pressed += OnRejectPressed;
        btnRow.AddChild(_rejectBtn);

        _kickBtn = RpgTheme.CreateRpgButton("Expulsar", false, 12);
        _kickBtn.CustomMinimumSize = new Vector2(80, 30);
        _kickBtn.Pressed += OnKickPressed;
        btnRow.AddChild(_kickBtn);

        // View info button
        var viewBtn = RpgTheme.CreateRpgButton("Ver Info (/MIRAR)", true, 13);
        viewBtn.CustomMinimumSize = new Vector2(0, 34);
        viewBtn.Pressed += () =>
        {
            if (_memberName.Length > 0 && _tcp != null)
                _tcp.SendPacket(ClientPackets.WriteTalk($"/MIRAR {_memberName}"));
        };
        vbox.AddChild(viewBtn);
    }

    /// <summary>
    /// Show member detail for a guild member (not applicant).
    /// Leader can kick, not accept/reject.
    /// </summary>
    public void ShowMember(string memberName)
    {
        _memberName = memberName;
        _isApplicant = false;
        TitleText = memberName;
        if (_detailLabel != null)
            _detailLabel.Text = $"Miembro del clan: {memberName}\n\nUse /MIRAR para ver informacion detallada.";
        if (_acceptBtn != null) _acceptBtn.Visible = false;
        if (_rejectBtn != null) _rejectBtn.Visible = false;
        if (_kickBtn != null) _kickBtn.Visible = true;
        ShowForm();
    }

    /// <summary>
    /// Show member detail for an applicant. Leader can accept/reject.
    /// </summary>
    public void ShowApplicant(string applicantName, string petition)
    {
        _memberName = applicantName;
        _isApplicant = true;
        TitleText = $"Solicitud: {applicantName}";
        if (_detailLabel != null)
            _detailLabel.Text = $"Solicitud de: {applicantName}\n\nMensaje: {petition}";
        if (_acceptBtn != null) _acceptBtn.Visible = true;
        if (_rejectBtn != null) _rejectBtn.Visible = true;
        if (_kickBtn != null) _kickBtn.Visible = false;
        ShowForm();
    }

    private void OnAcceptPressed()
    {
        if (_tcp == null || _memberName.Length == 0) return;
        _tcp.SendPacket(ClientPackets.WriteGuildAccept(_memberName));
        HideForm();
    }

    private void OnRejectPressed()
    {
        if (_tcp == null || _memberName.Length == 0) return;
        string comment = _commentEdit?.Text.Trim() ?? "Rechazado";
        if (comment.Length == 0) comment = "Rechazado";
        _tcp.SendPacket(ClientPackets.WriteGuildReject($"{_memberName},{comment}"));
        HideForm();
    }

    private void OnKickPressed()
    {
        if (_tcp == null || _memberName.Length == 0) return;
        _tcp.SendPacket(ClientPackets.WriteGuildExpel(_memberName));
        HideForm();
    }
}
