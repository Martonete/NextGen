using Godot;
using System;
using ArgentumNextgen.Game;
using ArgentumNextgen.Network;

namespace ArgentumNextgen.UI;

/// <summary>
/// Peace proposal panel — accept or reject peace/alliance proposals between guilds.
/// Shows proposing guild name and proposal terms.
/// Now uses RpgBaseForm for consistent RPG styling.
/// </summary>
public partial class PeaceProposalPanel : RpgBaseForm
{
    private GameState? _state;
    private AoTcpClient? _tcp;

    // Controls
    private Label? _proposalLabel;
    private string _proposingGuild = "";
    private string _proposalType = ""; // "peace" or "alliance"

    public PeaceProposalPanel() : base("Propuesta de Paz", new Vector2(340, 200), "v2") { }

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

        // Proposal text
        _proposalLabel = RpgTheme.CreateInfoLabel("", 12);
        _proposalLabel.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _proposalLabel.CustomMinimumSize = new Vector2(0, 60);
        _proposalLabel.AddThemeColorOverride("font_color", Colors.White);
        vbox.AddChild(_proposalLabel);

        // Buttons
        var btnRow = RpgTheme.CreateRow(RpgTheme.SpacingLg);
        btnRow.Alignment = BoxContainer.AlignmentMode.Center;
        vbox.AddChild(btnRow);

        var acceptBtn = RpgTheme.CreateRpgButton("Aceptar", false, 13);
        acceptBtn.CustomMinimumSize = new Vector2(110, 34);
        acceptBtn.Pressed += OnAcceptPressed;
        btnRow.AddChild(acceptBtn);

        var rejectBtn = RpgTheme.CreateRpgButton("Rechazar", false, 13);
        rejectBtn.CustomMinimumSize = new Vector2(110, 34);
        rejectBtn.Pressed += OnRejectPressed;
        btnRow.AddChild(rejectBtn);
    }

    /// <summary>
    /// Show the peace proposal panel with the proposing guild's info.
    /// </summary>
    public void ShowProposal(string guildName, string proposalType)
    {
        _proposingGuild = guildName;
        _proposalType = proposalType;

        string typeText = proposalType == "alliance" ? "alianza" : "paz";
        if (_proposalLabel != null)
            _proposalLabel.Text = $"El clan \"{guildName}\" propone una {typeText}.\n\n" +
                                  $"Aceptar o rechazar la propuesta?";
        ShowForm();
    }

    private void OnAcceptPressed()
    {
        if (_tcp == null || _proposingGuild.Length == 0) return;
        // VB6: /ACEPTARPAZ or /ACEPTARALIANZA
        string cmd = _proposalType == "alliance" ? "/ACEPTARALIANZA" : "/ACEPTARPAZ";
        _tcp.SendPacket(ClientPackets.WriteTalk($"{cmd} {_proposingGuild}"));
        HideForm();
    }

    private void OnRejectPressed()
    {
        if (_tcp == null || _proposingGuild.Length == 0) return;
        string cmd = _proposalType == "alliance" ? "/RECHAZARALIANZA" : "/RECHAZARPAZ";
        _tcp.SendPacket(ClientPackets.WriteTalk($"{cmd} {_proposingGuild}"));
        HideForm();
    }
}
