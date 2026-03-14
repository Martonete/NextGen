using Godot;
using System;
using ArgentumNextgen.Game;
using ArgentumNextgen.Network;

namespace ArgentumNextgen.UI;

/// <summary>
/// Guild alignment picker panel — shown during guild foundation.
/// Player picks one of 5 faction alignments: Neutral, Royal, Chaos, Civilian, Criminal.
/// Each button sends the alignment choice to the server.
/// Now uses RpgBaseForm for consistent RPG styling.
/// </summary>
public partial class GuildAlignmentPanel : RpgBaseForm
{
    private GameState? _state;
    private AoTcpClient? _tcp;

    /// <summary>
    /// Callback raised when an alignment is selected. Passes alignment index (0-4).
    /// </summary>
    public Action<int>? OnAlignmentChosen;

    public GuildAlignmentPanel() : base("Alineacion del Clan", new Vector2(300, 320), "v2") { }

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

        // Instructions
        var infoLabel = RpgTheme.CreateInfoLabel("Seleccione la alineacion para su nuevo clan:", 12);
        infoLabel.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        vbox.AddChild(infoLabel);

        // Alignment buttons
        string[] alignNames = { "Neutral", "Armada Real", "Fuerzas del Caos", "Civil", "Criminal" };

        for (int i = 0; i < alignNames.Length; i++)
        {
            int idx = i;
            var btn = RpgTheme.CreateRpgButton(alignNames[i], true, 14);
            btn.CustomMinimumSize = new Vector2(0, 34);
            btn.Pressed += () => OnAlignmentSelected(idx);
            vbox.AddChild(btn);
        }
    }

    private void OnAlignmentSelected(int index)
    {
        // Send alignment choice to server via slash command
        // VB6: /ALINEACION <index> during guild creation
        _tcp?.SendPacket(ClientPackets.WriteTalk($"/ALINEACION {index}"));
        OnAlignmentChosen?.Invoke(index);
        HideForm();
    }

    public void Open()
    {
        ShowForm();
    }
}
