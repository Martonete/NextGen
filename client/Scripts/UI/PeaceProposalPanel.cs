using Godot;
using System;
using ArgentumNextgen.Game;
using ArgentumNextgen.Network;

namespace ArgentumNextgen.UI;

/// <summary>
/// Peace proposal panel — accept or reject peace/alliance proposals between guilds.
/// Shows proposing guild name and proposal terms.
/// </summary>
public partial class PeaceProposalPanel : PanelContainer
{
    private const int PanelW = 340;
    private const int PanelH = 200;
    private const int TitleBarH = 28;

    private GameState? _state;
    private AoTcpClient? _tcp;

    // Dragging
    private bool _dragging;
    private Vector2 _dragOffset;

    // Controls
    private Label? _proposalLabel;
    private string _proposingGuild = "";
    private string _proposalType = ""; // "peace" or "alliance"

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
        style.BorderColor = new Color(0.3f, 0.5f, 0.3f, 1f);
        style.SetBorderWidthAll(2);
        style.SetCornerRadiusAll(3);
        AddThemeStyleboxOverride("panel", style);

        var root = new VBoxContainer();
        root.SetAnchorsPreset(LayoutPreset.FullRect);
        root.AddThemeConstantOverride("separation", 8);
        AddChild(root);

        // Title bar
        var titleBar = new HBoxContainer();
        titleBar.CustomMinimumSize = new Vector2(0, TitleBarH);
        root.AddChild(titleBar);

        var titleLabel = new Label();
        titleLabel.Text = "  Propuesta de Paz";
        titleLabel.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        titleLabel.AddThemeColorOverride("font_color", new Color(0.3f, 0.8f, 0.3f));
        titleLabel.AddThemeFontSizeOverride("font_size", 13);
        titleBar.AddChild(titleLabel);

        var closeBtn = new Button();
        closeBtn.Text = "X";
        closeBtn.CustomMinimumSize = new Vector2(28, 24);
        closeBtn.Pressed += () => Visible = false;
        titleBar.AddChild(closeBtn);

        // Proposal text
        _proposalLabel = new Label();
        _proposalLabel.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _proposalLabel.CustomMinimumSize = new Vector2(PanelW - 16, 60);
        _proposalLabel.AddThemeFontSizeOverride("font_size", 12);
        _proposalLabel.AddThemeColorOverride("font_color", Colors.White);
        root.AddChild(_proposalLabel);

        // Buttons
        var btnRow = new HBoxContainer();
        btnRow.AddThemeConstantOverride("separation", 8);
        root.AddChild(btnRow);

        var acceptBtn = new Button();
        acceptBtn.Text = "Aceptar";
        acceptBtn.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        acceptBtn.CustomMinimumSize = new Vector2(0, 32);
        acceptBtn.AddThemeColorOverride("font_color", new Color(0.3f, 1f, 0.3f));
        acceptBtn.Pressed += OnAcceptPressed;
        btnRow.AddChild(acceptBtn);

        var rejectBtn = new Button();
        rejectBtn.Text = "Rechazar";
        rejectBtn.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        rejectBtn.CustomMinimumSize = new Vector2(0, 32);
        rejectBtn.AddThemeColorOverride("font_color", new Color(1f, 0.3f, 0.3f));
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
        Visible = true;
    }

    private void OnAcceptPressed()
    {
        if (_tcp == null || _proposingGuild.Length == 0) return;
        // VB6: /ACEPTARPAZ or /ACEPTARALIANZA
        string cmd = _proposalType == "alliance" ? "/ACEPTARALIANZA" : "/ACEPTARPAZ";
        _tcp.SendPacket(ClientPackets.WriteTalk($"{cmd} {_proposingGuild}"));
        Visible = false;
    }

    private void OnRejectPressed()
    {
        if (_tcp == null || _proposingGuild.Length == 0) return;
        string cmd = _proposalType == "alliance" ? "/RECHAZARALIANZA" : "/RECHAZARPAZ";
        _tcp.SendPacket(ClientPackets.WriteTalk($"{cmd} {_proposingGuild}"));
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
